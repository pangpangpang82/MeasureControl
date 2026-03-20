//#define USE_SIMULATION // 取消注释启用模拟模式

using MeasureControl.Models.Devices;
using MeasureControl.Drivers.PXI4004CAN;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using MeasureControl.Views.Dialogs;

namespace MeasureControl.Drivers
{
    /// <summary>
    /// 简仪 PXI4004 CAN 卡驱动
    /// 基于 ARTCANX1.dll 实现，只提供硬件连接功能
    /// </summary>
    public class PXI4004Driver : IDeviceDriver
    {
        #region 私有字段

        private readonly DeviceBase _device;
        private readonly int _slotNumber;
        private bool _isConnected;
        private IntPtr _deviceHandle;

        // CAN 初始化参数
        private PXI4004.ARTCANX1_CAN_PARAM _canParam;
        // 已打开的通道集合
        private readonly HashSet<int> _openedChannels = new HashSet<int>();

        // 模拟模式（调试用）

        // 接收优化相关
        private readonly Dictionary<int, ReceiveStats> _receiveStats = new Dictionary<int, ReceiveStats>();
        // 驱动层接收日志节流：避免高频批量接收时在输出窗口打印过多行
        private readonly Dictionary<int, DateTime> _lastDriverBatchLog = new Dictionary<int, DateTime>();
        private readonly Dictionary<int, int> _driverBatchCounter = new Dictionary<int, int>();
        private static readonly TimeSpan DriverBatchLogInterval = TimeSpan.FromSeconds(5);
        // 记录每个通道最后一次成功初始化时使用的参数，避免重复初始化相同参数导致硬件重启/错误
        private readonly Dictionary<int, PXI4004.ARTCANX1_CAN_PARAM> _channelAppliedParams = new Dictionary<int, PXI4004.ARTCANX1_CAN_PARAM>();

        #endregion

        /// <summary>
        /// 接收统计信息，用于优化接收策略
        /// </summary>
        public class ReceiveStats
        {
            public int ConsecutiveEmptyCount { get; set; } = 0;
            public int TotalFramesReceived { get; set; } = 0;
            public DateTime LastReceiveTime { get; set; } = DateTime.MinValue;
            public double AdaptiveTimeout { get; set; } = 0.01; // 起始超时时间
        }

        // 是否在驱动层打印接收相关的调试日志（默认禁用，确保输出仅在错误/异常时出现）
        private readonly bool _driverReceiveLogsEnabled = false;

        #region 属性

        /// <summary>
        /// 设备ID
        /// </summary>
        public string DeviceId => _device?.Id ?? string.Empty;

        /// <summary>
        /// 设备名称
        /// </summary>
        public string DeviceName => _device?.Name ?? "PXI4004-CAN";

        /// <summary>
        /// 是否已连接
        /// </summary>
        public bool IsConnected => _isConnected;

        /// <summary>
        /// 是否为模拟驱动
        /// </summary>
        public bool IsSimulated => false;

        /// <summary>
        /// 插槽号
        /// </summary>
        public int SlotNumber => _slotNumber;

        /// <summary>
        /// 设备句柄
        /// </summary>
        public IntPtr DeviceHandle => _deviceHandle;

        #endregion

        #region 构造函数

        /// <summary>
        /// 创建 PXI4004 驱动实例
        /// </summary>
        /// <param name="device">设备实例</param>
        /// <param name="slotNumber">PXI插槽号（默认0）</param>
        public PXI4004Driver(DeviceBase device, int slotNumber = 0)
        {
            _device = device ?? throw new ArgumentNullException(nameof(device));
            _slotNumber = slotNumber;
            _isConnected = false;
            _deviceHandle = IntPtr.Zero;

            InitializeDefaultParams();
        }

        /// <summary>
        /// 初始化默认CAN参数
        /// </summary>
        private void InitializeDefaultParams()
        {
            _canParam = new PXI4004.ARTCANX1_CAN_PARAM();
            _canParam.nBaudRate = PXI4004.CAN_BAUD_500K;         // 500Kbps
            _canParam.nWorkMode = (byte)PXI4004.ARTCANX1_CAN_WORKMODE_NORMAL;  // 正常模式
            _canParam.bRecvTimestampEn = 1;                      // 启用时间戳
            _canParam.bAccExtID = 0;                             // 不验收扩展帧ID
            _canParam.nAccFilterCnt = (byte)PXI4004.ARTCANX1_CAN_ACC_NUM_NONE; // 禁止滤波
            _canParam.nAccCodeA = 0x00000000;                    // 验收码A
            _canParam.nAccCodeB = 0x00000000;                    // 验收码B
            _canParam.nAccMaskA = 0xFFFFFFFF;                    // 屏蔽码A
            _canParam.nAccMaskB = 0xFFFFFFFF;                    // 屏蔽码B
            _canParam.nFrameInterval = 0;                        // 帧发送间隔
            _canParam.SendTrig = new PXI4004.ARTCANX1_TRIG_PARAM(); // 发送触发参数
            _canParam.SendTrig.nTriggerType = PXI4004.ARTCANX1_TRIGTYPE_NONE; // 无触发
        }

        #endregion

        #region IDeviceDriver 实现

        /// <summary>
        /// 采集状态改变事件（实现接口要求）
        /// </summary>
        public event EventHandler<AcquisitionStatusChangedEventArgs> AcquisitionStatusChanged;

        /// <summary>
        /// 设备功能类型（通信类设备）
        /// </summary>
        public DeviceCapability Capability => DeviceCapability.Communication;

        /// <summary>
        /// 触发采集状态改变事件的辅助方法
        /// </summary>
        /// <param name="isRunning"></param>
        /// <param name="mode"></param>
        protected void OnAcquisitionStatusChanged(bool isRunning, string mode)
        {
            AcquisitionStatusChanged?.Invoke(this, new AcquisitionStatusChangedEventArgs { IsRunning = isRunning, AcquisitionMode = mode });
        }

        /// <summary>
        /// 连接设备
        /// </summary>
        public async Task<bool> ConnectAsync()
        {
            try
            {
                Debug.WriteLine($"[PXI4004Driver] 正在连接设备 {DeviceName}, 序列号: {_slotNumber}");

                // ========== 模拟模式：跳过真实硬件初始化 ==========
#if USE_SIMULATION
                Debug.WriteLine($"[PXI4004Driver] 【模拟模式】跳过硬件初始化，直接连接成功");
                await Task.Delay(100);
                _isConnected = true;
                return true;
#endif
                // ================================================

                // 1. 使用序列号创建设备句柄
                Debug.WriteLine($"[PXI4004Driver] 尝试创建设备（逻辑序列）: {_slotNumber}");
                _deviceHandle = PXI4004.CreateDevice(0);

                if (_deviceHandle == IntPtr.Zero)
                {
                    uint nativeErr = 0;
                    try
                    {
                        nativeErr = PXI4004.ARTCANX1_AUX_GetLastError();
                    }
                    catch
                    {
                        // ignore if auxiliary call fails
                    }

                    Debug.WriteLine($"[PXI4004Driver] 创建设备句柄返回失败，原生错误码: 0x{nativeErr:X8}");

                    // 通知用户并返回失败
                    try
                    {
                        string msg = $"创建设备失败，未获得有效句柄（序列号: {_slotNumber}）。\n原生错误码: 0x{nativeErr:X8}\n\n建议检查：\n- PXI机箱指示灯是否正常\n- PXI4004板卡是否安装在插槽 {_slotNumber}\n- ARTCANX1 驱动/DLL 是否已安装且位数匹配 (x64)\n- 确认设备已上电";
                        ReMessageBox.Show(msg, "设备创建失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    catch
                    {
                        // ignore UI failures
                    }

                    return false;
                }

                Debug.WriteLine($"[PXI4004Driver] 设备创建成功，句柄: 0x{_deviceHandle.ToInt64():X}");

                // 连接成功后不自动初始化任何CAN通道
                // 让用户通过UI明确操作来打开需要的通道
                await Task.Delay(100); // 等待硬件稳定
                _isConnected = true;
                Debug.WriteLine($"[PXI4004Driver] 设备连接成功");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PXI4004Driver] 连接失败: {ex.Message}");
                _isConnected = false;

                // 清理资源
                if (_deviceHandle != IntPtr.Zero)
                {
                    PXI4004.ReleaseDevice(_deviceHandle);
                    _deviceHandle = IntPtr.Zero;
                }

                // 在 UI 线程弹窗通知用户（防止在无 UI 环境抛出异常）
                try
                {
                    string msg = $"连接设备失败：{ex.Message}\n\n建议检查：\n- PXI机箱指示灯是否正常\n- PXI4004板卡是否安装在插槽 {_slotNumber}\n- ARTCANX1 驱动/DLL 是否已安装且位数匹配 (x64)\n- 确认设备已上电\n\n详细信息请查看输出窗口的日志。";
                    ReMessageBox.Show(msg, "设备连接失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                catch
                {
                    // 可能在无 UI 环境或非 STA 线程，忽略弹窗错误
                }

                return false;
            }
        }

        /// <summary>
        /// 断开连接
        /// </summary>
        public async Task<bool> DisconnectAsync()
        {
            try
            {
                Debug.WriteLine($"[PXI4004Driver] 正在断开设备 {DeviceName}");

                // ========== 模拟模式：直接返回 ==========
#if USE_SIMULATION
                _isConnected = false;
                Debug.WriteLine($"[PXI4004Driver] 【模拟】设备断开成功");
                return true;
#endif
                // ========================================

                // 停止并释放所有已打开的CAN通道
                foreach (var channelIndex in _openedChannels.ToList())
                {
                    try
                    {
                        PXI4004.StopCAN(_deviceHandle, (uint)channelIndex);
                        PXI4004.ReleaseCAN(_deviceHandle, (uint)channelIndex);
                        Debug.WriteLine($"[PXI4004Driver] 通道 {channelIndex} 已停止并释放");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[PXI4004Driver] 停止通道 {channelIndex} 时出错: {ex.Message}");
                    }
                }
                _openedChannels.Clear();

                // 清理所有接收统计信息
                _receiveStats.Clear();

                // 3. 释放设备
                if (_deviceHandle != IntPtr.Zero)
                {
                    bool released = PXI4004.ReleaseDevice(_deviceHandle);
                    if (released)
                    {
                        Debug.WriteLine($"[PXI4004Driver] 设备释放成功");
                    }
                    _deviceHandle = IntPtr.Zero;
                }

                await Task.Delay(50); // 等待硬件释放
                _isConnected = false;
                Debug.WriteLine($"[PXI4004Driver] 设备断开成功");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PXI4004Driver] 断开失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 打开指定的物理通道（初始化并启动CAN任务）
        /// </summary>
        /// <param name="channelIndex">通道索引（0 基）</param>
        public async Task<bool> OpenChannelAsync(int channelIndex)
        {
            if (!_isConnected)
            {
                Debug.WriteLine($"[PXI4004Driver] 设备未连接，无法打开通道 {channelIndex}");
                return false;
            }

            if (_openedChannels.Contains(channelIndex))
            {
                // 如果通道已打开且之前已使用相同参数初始化，则跳过重复初始化以避免硬件多次重启
                try
                {
                    var currentDefault = PXI4004.GetDefaultCANParam(_deviceHandle, (uint)channelIndex);
                    if (_channelAppliedParams.TryGetValue(channelIndex, out var applied) && AreCanParamsEqual(applied, currentDefault))
                    {
                        Debug.WriteLine($"[PXI4004Driver] 通道 {channelIndex} 已打开且参数与默认匹配，跳过重复初始化");
                        return true;
                    }
                }
                catch
                {
                    // 如果无法比较参数，则按原逻辑继续重新初始化以保证一致性
                }

                // 通道已打开但参数不同，需要安全停止并释放以重新初始化
                Debug.WriteLine($"[PXI4004Driver] 通道 {channelIndex} 已打开，准备重新初始化以应用新参数");
                try
                {
                    PXI4004.StopCAN(_deviceHandle, (uint)channelIndex);
                }
                catch { }
                try
                {
                    PXI4004.ReleaseCAN(_deviceHandle, (uint)channelIndex);
                }
                catch { }
                _openedChannels.Remove(channelIndex);
                _channelAppliedParams.Remove(channelIndex);
                await Task.Delay(20);
            }

            try
            {
                // 获取默认参数并使用当前默认配置作为起点
                var param = PXI4004.GetDefaultCANParam(_deviceHandle, (uint)channelIndex);
                // 可以在这里根据需要修改 param，例如波特率或过滤器
                param.nBaudRate = _canParam.nBaudRate;

                Debug.WriteLine($"[PXI4004Driver] 初始化通道 {channelIndex} 的 CAN 参数 -> Baud={param.nBaudRate}, WorkMode={param.nWorkMode}, AccFilterCnt={param.nAccFilterCnt}, AccCodeA=0x{param.nAccCodeA:X8}, AccMaskA=0x{param.nAccMaskA:X8}");
                bool initOk = false;
                uint nativeErr = 0;
                try
                {
                    initOk = PXI4004.InitCAN(_deviceHandle, (uint)channelIndex, ref param);
                }
                catch
                {
                    initOk = false;
                }

                if (!initOk)
                {
                    // 尝试兼容调用底层 InitTask（部分固件/驱动环境可能需要此调用）
                    try
                    {
                        initOk = PXI4004.ARTCANX1_CAN_InitTask(_deviceHandle, (uint)channelIndex, ref param, IntPtr.Zero);
                    }
                    catch
                    {
                        initOk = false;
                    }
                }

                if (!initOk)
                {
                    try { nativeErr = PXI4004.ARTCANX1_AUX_GetLastError(); } catch { nativeErr = 0; }
                    Debug.WriteLine($"[PXI4004Driver] 初始化通道 {channelIndex} 失败，nativeErr=0x{nativeErr:X8}");
                    // 确保不保留错误的 opened 状态或 applied 参数
                    _openedChannels.Remove(channelIndex);
                    _channelAppliedParams.Remove(channelIndex);
                    return false;
                }

                Debug.WriteLine($"[PXI4004Driver] 启动通道 {channelIndex}");
                if (!PXI4004.StartCAN(_deviceHandle, (uint)channelIndex))
                {
                    try { nativeErr = PXI4004.ARTCANX1_AUX_GetLastError(); } catch { nativeErr = 0; }
                    Debug.WriteLine($"[PXI4004Driver] 启动通道 {channelIndex} 失败，nativeErr=0x{nativeErr:X8}");
                    // 尝试再次 Stop/Release 清理并返回失败，避免残留不一致状态
                    try { PXI4004.StopCAN(_deviceHandle, (uint)channelIndex); } catch { }
                    try { PXI4004.ReleaseCAN(_deviceHandle, (uint)channelIndex); } catch { }
                    _openedChannels.Remove(channelIndex);
                    _channelAppliedParams.Remove(channelIndex);
                    return false;
                }

                _openedChannels.Add(channelIndex);
                await Task.Delay(20); // 稍作等待
                // 记录实际应用到硬件的参数，后续避免重复初始化相同配置
                try
                {
                    _channelAppliedParams[channelIndex] = param;
                }
                catch { }
                Debug.WriteLine($"[PXI4004Driver] 通道 {channelIndex} 已打开");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PXI4004Driver] 打开通道 {channelIndex} 时发生异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 使用指定参数打开指定的物理通道（初始化并启动CAN任务）
        /// </summary>
        /// <param name="channelIndex">通道索引（0 基）</param>
        /// <param name="param">CAN参数</param>
        public async Task<bool> OpenChannelAsync(int channelIndex, PXI4004.ARTCANX1_CAN_PARAM param)
        {
            if (!_isConnected)
            {
                Debug.WriteLine($"[PXI4004Driver] 设备未连接，无法打开通道 {channelIndex}");
                return false;
            }

            if (_openedChannels.Contains(channelIndex))
            {
                // 如果通道已打开且已应用相同参数，跳过重复初始化
                try
                {
                    if (_channelAppliedParams.TryGetValue(channelIndex, out var applied) && AreCanParamsEqual(applied, param))
                    {
                        Debug.WriteLine($"[PXI4004Driver] 通道 {channelIndex} 已打开且已应用相同参数，跳过重复初始化");
                        return true;
                    }
                }
                catch { }

                // 否则需要停止并释放以重新初始化为自定义参数
                Debug.WriteLine($"[PXI4004Driver] 通道 {channelIndex} 已打开，准备重新初始化以应用自定义参数");
                try { PXI4004.StopCAN(_deviceHandle, (uint)channelIndex); } catch { }
                try { PXI4004.ReleaseCAN(_deviceHandle, (uint)channelIndex); } catch { }
                _openedChannels.Remove(channelIndex);
                _channelAppliedParams.Remove(channelIndex);
                await Task.Delay(20);
            }

            try
            {
                // Use provided param directly
                Debug.WriteLine($"[PXI4004Driver] 使用自定义参数初始化通道 {channelIndex}");
                Debug.WriteLine($"[PXI4004Driver] 自定义 Init 参数 -> Baud={param.nBaudRate}, WorkMode={param.nWorkMode}, AccFilterCnt={param.nAccFilterCnt}, AccCodeA=0x{param.nAccCodeA:X8}, AccMaskA=0x{param.nAccMaskA:X8}");
                bool initOk = false;
                uint nativeErr = 0;
                try
                {
                    initOk = PXI4004.InitCAN(_deviceHandle, (uint)channelIndex, ref param);
                }
                catch
                {
                    initOk = false;
                }

                if (!initOk)
                {
                    try
                    {
                        initOk = PXI4004.ARTCANX1_CAN_InitTask(_deviceHandle, (uint)channelIndex, ref param, IntPtr.Zero);
                    }
                    catch
                    {
                        initOk = false;
                    }
                }

                if (!initOk)
                {
                    try { nativeErr = PXI4004.ARTCANX1_AUX_GetLastError(); } catch { nativeErr = 0; }
                    Debug.WriteLine($"[PXI4004Driver] 初始化通道 {channelIndex} 失败，nativeErr=0x{nativeErr:X8}");
                    _openedChannels.Remove(channelIndex);
                    _channelAppliedParams.Remove(channelIndex);
                    return false;
                }

                Debug.WriteLine($"[PXI4004Driver] 启动通道 {channelIndex}");
                if (!PXI4004.StartCAN(_deviceHandle, (uint)channelIndex))
                {
                    try { nativeErr = PXI4004.ARTCANX1_AUX_GetLastError(); } catch { nativeErr = 0; }
                    Debug.WriteLine($"[PXI4004Driver] 启动通道 {channelIndex} 失败，nativeErr=0x{nativeErr:X8}");
                    try { PXI4004.StopCAN(_deviceHandle, (uint)channelIndex); } catch { }
                    try { PXI4004.ReleaseCAN(_deviceHandle, (uint)channelIndex); } catch { }
                    _openedChannels.Remove(channelIndex);
                    _channelAppliedParams.Remove(channelIndex);
                    return false;
                }

                _openedChannels.Add(channelIndex);
                await Task.Delay(20);
                Debug.WriteLine($"[PXI4004Driver] 通道 {channelIndex} 已打开（自定义参数）");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PXI4004Driver] 打开通道 {channelIndex} 时发生异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 关闭指定的物理通道（停止并释放CAN任务）
        /// </summary>
        /// <param name="channelIndex">通道索引（0 基）</param>
        public async Task<bool> CloseChannelAsync(int channelIndex)
        {
            if (!_isConnected)
            {
                Debug.WriteLine($"[PXI4004Driver] 设备未连接，无法关闭通道 {channelIndex}");
                return false;
            }

            if (!_openedChannels.Contains(channelIndex))
            {
                Debug.WriteLine($"[PXI4004Driver] 通道 {channelIndex} 未打开，跳过");
                return true;
            }

            try
            {
                Debug.WriteLine($"[PXI4004Driver] 停止通道 {channelIndex}");
                PXI4004.StopCAN(_deviceHandle, (uint)channelIndex);

                Debug.WriteLine($"[PXI4004Driver] 释放通道 {channelIndex}");
                PXI4004.ReleaseCAN(_deviceHandle, (uint)channelIndex);

                _openedChannels.Remove(channelIndex);

                // 清理该通道的接收统计信息
                _receiveStats.Remove(channelIndex);
                // 清理记录的已应用参数
                _channelAppliedParams.Remove(channelIndex);

                await Task.Delay(20);
                Debug.WriteLine($"[PXI4004Driver] 通道 {channelIndex} 已关闭并清理统计信息");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PXI4004Driver] 关闭通道 {channelIndex} 时发生异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 读取单个通道（返回连接状态）
        /// </summary>
        public Task<double> ReadChannelAsync(string channelId)
        {
            // 简化为返回连接状态：1表示已连接，0表示未连接
            return Task.FromResult(_isConnected ? 1.0 : 0.0);
        }

        /// <summary>
        /// 批量读取通道
        /// </summary>
        public async Task<Dictionary<string, double>> ReadChannelsBatchAsync(IEnumerable<string> channelIds)
        {
            var results = new Dictionary<string, double>();

            // 未连接时返回默认值
            if (!_isConnected)
            {
                foreach (var channelId in channelIds)
                {
                    results[channelId] = 0;
                }
                return results;
            }

            // 逐个读取通道
            foreach (var channelId in channelIds)
            {
                results[channelId] = await ReadChannelAsync(channelId);
            }

            return results;
        }

        /// <summary>
        /// 写入单个通道
        /// </summary>
        public Task<bool> WriteChannelAsync(string channelId, double value)
        {
            // 简化为返回连接状态
            return Task.FromResult(_isConnected);
        }

        /// <summary>
        /// 批量写入通道
        /// </summary>
        public Task<bool> WriteChannelsBatchAsync(Dictionary<string, double> channelValues)
        {
            // 简化为返回连接状态
            return Task.FromResult(_isConnected);
        }


        /// <summary>
        /// 接收CAN帧（异步，按需接收）
        /// </summary>
        /// <param name="channelIndex">通道索引</param>
        /// <param name="timeout">超时时间（秒）</param>
        /// <returns>接收到的帧，如果没有帧则返回null</returns>
        public async Task<PXI4004.ARTCANX1_CAN_FRAME?> ReceiveFrameAsync(int channelIndex, double timeout = 0.01)
        {
            if (!_isConnected || !_openedChannels.Contains(channelIndex))
            {
                if (_driverReceiveLogsEnabled)
                {
                    Debug.WriteLine($"[PXI4004Driver] 通道 {channelIndex} 未连接或未打开，无法接收帧");
                }
                return null;
            }

            try
            {
                // ========== 模拟模式：模拟接收帧 ==========
#if USE_SIMULATION
                await Task.Delay(5); // 模拟接收延迟
                // 随机决定是否收到帧（30%概率）
                if (new Random().Next(100) < 30)
                {
                    var frame = new PXI4004.ARTCANX1_CAN_FRAME();
                    frame.DataBuf = new byte[8];
                    frame.nFrameID = (uint)new Random().Next(0x100, 0x200); // 随机ID
                    frame.nDataLength = 8;
                    for (int i = 0; i < 8; i++) frame.DataBuf[i] = (byte)new Random().Next(0, 256);
                    frame.nFrameType = 0; // 数据帧
                    frame.bExtendedID = 0; // 标准帧
                    if (_driverReceiveLogsEnabled) Debug.WriteLine($"[PXI4004Driver] 【模拟】从通道 {channelIndex} 接收到帧: ID=0x{frame.nFrameID:X}");
                    return frame;
                }
                else
                {
                    return null; // 模拟无帧可读
                }
#endif
                // ============================================

                // 使用Task.Run将同步调用包装为异步
                return await Task.Run(() =>
                {
                    var frame = new PXI4004.ARTCANX1_CAN_FRAME();
                    frame.DataBuf = new byte[8];
                    bool hasFrame = PXI4004.ReceiveFrame(_deviceHandle, (uint)channelIndex, ref frame, timeout);
                    if (hasFrame)
                    {
                        if (_driverReceiveLogsEnabled) Debug.WriteLine($"[PXI4004Driver] 从通道 {channelIndex} 接收到帧: ID=0x{frame.nFrameID:X}, Len={frame.nDataLength}");
                        return frame;
                    }
                    else
                    {
                        return (PXI4004.ARTCANX1_CAN_FRAME?)null;
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PXI4004Driver] 接收帧异常: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 批量接收CAN帧（异步，优化版本）
        /// </summary>
        /// <param name="channelIndex">通道索引</param>
        /// <param name="maxFrames">最大接收帧数</param>
        /// <param name="timeout">基础超时时间（秒），方法会根据情况自适应调整</param>
        /// <returns>接收到的帧列表</returns>
        public async Task<List<PXI4004.ARTCANX1_CAN_FRAME>> ReceiveFramesBatchAsync(int channelIndex, int maxFrames = 100, double timeout = 0.01)
        {
            var frames = new List<PXI4004.ARTCANX1_CAN_FRAME>();

            if (!_isConnected || !_openedChannels.Contains(channelIndex))
            {
                if (_driverReceiveLogsEnabled) Debug.WriteLine($"[PXI4004Driver] 通道 {channelIndex} 未连接或未打开，无法批量接收帧");
                return frames;
            }

            try
            {
                // 使用Task.Run将同步调用包装为异步
                return await Task.Run(() =>
                {
                    // 获取或创建该通道的接收统计信息
                    if (!_receiveStats.TryGetValue(channelIndex, out var stats))
                    {
                        stats = new ReceiveStats();
                        _receiveStats[channelIndex] = stats;
                    }

                    var resultFrames = new List<PXI4004.ARTCANX1_CAN_FRAME>();
                    int framesReceivedThisBatch = 0;

                    // 使用自适应超时策略
                    double currentTimeout = Math.Min(timeout * (1.0 + stats.ConsecutiveEmptyCount * 0.1), timeout * 5.0);

                    // 根据接收频率动态调整批量大小
                    int batchSize = stats.ConsecutiveEmptyCount > 5 ? 3 :  // 低频时减少批量
                                   stats.ConsecutiveEmptyCount == 0 ? 15 :  // 高频时增加批量
                                   8; // 默认批量大小

                    batchSize = Math.Min(batchSize, maxFrames - framesReceivedThisBatch);

                    // 预分配帧结构数组以减少GC压力
                    var frameBatch = new PXI4004.ARTCANX1_CAN_FRAME[batchSize];

                    // 初始化帧结构
                    for (int i = 0; i < batchSize; i++)
                    {
                        frameBatch[i] = new PXI4004.ARTCANX1_CAN_FRAME();
                        frameBatch[i].DataBuf = new byte[8];
                    }

                    int currentBatchReceived = 0;

                    // 批量接收：一次性尝试接收多帧
                    for (int i = 0; i < batchSize && framesReceivedThisBatch < maxFrames; i++)
                    {
                        bool hasFrame = PXI4004.ReceiveFrame(_deviceHandle, (uint)channelIndex, ref frameBatch[i], currentTimeout);
                        if (hasFrame)
                        {
                            // 在驱动层尽早丢弃空帧（长度为0或数据全部为0），避免不必要的内存分配和传递到上层
                            bool isEmptyFrame = frameBatch[i].nDataLength == 0;
                            if (!isEmptyFrame)
                            {
                                // 检查指定长度内是否全部为0
                                int dataLen = Math.Min(8, (int)frameBatch[i].nDataLength);
                                isEmptyFrame = true;
                                for (int k = 0; k < dataLen; k++)
                                {
                                    if (frameBatch[i].DataBuf != null && frameBatch[i].DataBuf[k] != 0)
                                    {
                                        isEmptyFrame = false;
                                        break;
                                    }
                                }
                            }

                            if (isEmptyFrame)
                            {
                                // 增加空帧计数以便驱动自适应策略使用（例如扩大超时或减少批量）
                                try
                                {
                                    stats.ConsecutiveEmptyCount++;
                                }
                                catch { }

                                // 驱动层节流日志：仅在需要时打印丢弃空帧摘要，避免输出洪水
                                if (_driverReceiveLogsEnabled)
                                {
                                    var now = DateTime.UtcNow;
                                    bool shouldLog = false;
                                    if (!_lastDriverBatchLog.TryGetValue(channelIndex, out var lastLog))
                                    {
                                        shouldLog = true;
                                    }
                                    else if ((now - lastLog) > DriverBatchLogInterval)
                                    {
                                        shouldLog = true;
                                    }

                                    if (shouldLog)
                                    {
                                        _lastDriverBatchLog[channelIndex] = now;
                                        Debug.WriteLine($"[PXI4004Driver] 从通道 {channelIndex} 丢弃空帧 (ID=0x{frameBatch[i].nFrameID:X}, Len={frameBatch[i].nDataLength})");
                                    }
                                }

                                // 丢弃此帧，不创建副本，不加入 resultFrames
                                continue;
                            }

                            // 创建帧的副本，避免重用问题
                            var frameCopy = new PXI4004.ARTCANX1_CAN_FRAME
                            {
                                nFrameID = frameBatch[i].nFrameID,
                                bExtendedID = frameBatch[i].bExtendedID,
                                nFrameType = frameBatch[i].nFrameType,
                                nDataLength = frameBatch[i].nDataLength,
                                DataBuf = new byte[8]
                            };
                            Array.Copy(frameBatch[i].DataBuf, frameCopy.DataBuf, 8);

                            resultFrames.Add(frameCopy);
                            framesReceivedThisBatch++;
                            currentBatchReceived++;
                        }
                        else
                        {
                            // 没有更多帧了，结束批量接收
                            break;
                        }
                    }

                    // 更新统计信息
                    if (currentBatchReceived > 0)
                    {
                        // 重置空接收计数
                        stats.ConsecutiveEmptyCount = 0;
                        stats.LastReceiveTime = DateTime.UtcNow;

                        // 根据接收效率调整批量大小
                        if (currentBatchReceived >= batchSize * 0.8) // 批量利用率高
                        {
                            // 保持或增加批量大小
                        }
                        else if (currentBatchReceived < batchSize * 0.3) // 批量利用率低
                        {
                            // 适当减少批量大小以适应低频场景
                        }
                    }
                    else
                    {
                        // 这一批没有收到帧，增加空接收计数
                        stats.ConsecutiveEmptyCount++;
                    }

                    // 如果没有收到任何帧，结束并返回当前结果
                    if (currentBatchReceived == 0)
                    {
                        return resultFrames;
                    }

                    // 更新统计信息
                    if (framesReceivedThisBatch > 0)
                    {
                        stats.TotalFramesReceived += framesReceivedThisBatch;

                        // 自适应调整超时时间：如果接收频繁，减少超时；如果接收稀疏，增加超时
                        if (framesReceivedThisBatch >= maxFrames * 0.8) // 高负载
                        {
                            stats.AdaptiveTimeout = Math.Max(timeout * 0.5, stats.AdaptiveTimeout * 0.9);
                        }
                        else if (framesReceivedThisBatch < maxFrames * 0.2) // 低负载
                        {
                            stats.AdaptiveTimeout = Math.Min(timeout * 3.0, stats.AdaptiveTimeout * 1.1);
                        }

                        // 驱动层节流打印：累加每通道在短周期内的接收量，仅每 DriverBatchLogInterval 打印一次摘要，避免输出窗口被淹没
                        if (!_driverBatchCounter.TryGetValue(channelIndex, out var drvExisting)) drvExisting = 0;
                        drvExisting += framesReceivedThisBatch;
                        _driverBatchCounter[channelIndex] = drvExisting;

                        var now = DateTime.UtcNow;
                        bool shouldLog = false;
                        if (!_lastDriverBatchLog.TryGetValue(channelIndex, out var lastLog))
                        {
                            shouldLog = true;
                        }
                        else if ((now - lastLog) > DriverBatchLogInterval)
                        {
                            shouldLog = true;
                        }

                        if (shouldLog)
                        {
                            _lastDriverBatchLog[channelIndex] = now;
                            int totalSinceLast = _driverBatchCounter[channelIndex];
                            _driverBatchCounter[channelIndex] = 0; // reset counter after logging
                            if (_driverReceiveLogsEnabled) Debug.WriteLine($"[PXI4004Driver] 从通道 {channelIndex} 批量接收到 {framesReceivedThisBatch} 帧 (本周期总计: {totalSinceLast}, 累计总计: {stats.TotalFramesReceived})");
                        }
                    }
                    else
                    {
                        // 长时间没有数据时，逐渐增加休眠时间以节省CPU
                        if (stats.ConsecutiveEmptyCount > 10)
                        {
                            // 短暂休眠以减少CPU占用
                            System.Threading.Thread.Sleep(1);
                        }
                    }

                    return resultFrames;
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PXI4004Driver] 批量接收帧异常: {ex.Message}");
                return frames;
            }
        }

        /// <summary>
        /// 配置通道
        /// </summary>
        public async Task<bool> ConfigureChannelAsync(string channelId, Dictionary<string, object> config)
        {
            Debug.WriteLine($"[PXI4004Driver] 配置通道 {channelId}");
            // PXI4004 CAN 通道配置较简单，主要在初始化时完成
            return await Task.FromResult(true);
        }

        /// <summary>
        /// 启动采集
        /// </summary>
        public Task<bool> StartAcquisitionAsync()
        {
            if (!_isConnected)
            {
                Debug.WriteLine($"[PXI4004Driver] 设备未连接，无法启动采集");
                return Task.FromResult(false);
            }

            try
            {
                Debug.WriteLine($"[PXI4004Driver] 启动数据采集");
                // CAN 设备初始化后即可收发数据，这里不需要额外操作
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PXI4004Driver] 启动采集失败: {ex.Message}");
                return Task.FromResult(false);
            }
        }

        /// <summary>
        /// 停止采集
        /// </summary>
        public Task<bool> StopAcquisitionAsync()
        {
            try
            {
                Debug.WriteLine($"[PXI4004Driver] 停止数据采集");
                // 停止采集操作
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PXI4004Driver] 停止采集失败: {ex.Message}");
                return Task.FromResult(false);
            }
        }

        /// <summary>
        /// 获取设备状态
        /// </summary>
        public Task<Dictionary<string, object>> GetStatusAsync()
        {
            return Task.FromResult(new Dictionary<string, object>
            {
                { "IsConnected", _isConnected },
                { "IsSimulated", false },
                { "SlotNumber", _slotNumber },
                { "DeviceName", DeviceName },
                { "DeviceId", DeviceId },
                { "DeviceHandle", _deviceHandle.ToInt64() }
            });
        }

        /// <summary>
        /// 重置设备
        /// </summary>
        public Task<bool> ResetAsync()
        {
            Debug.WriteLine($"[PXI4004Driver] 重置设备");

#if USE_SIMULATION
            return Task.FromResult(true);
#else
            if (!_isConnected)
            {
                return Task.FromResult(true);
            }
#endif

            try
            {
                // 重新初始化CAN参数
                if (!PXI4004.InitCAN(_deviceHandle, 0, ref _canParam))
                {
                    Debug.WriteLine($"[PXI4004Driver] 重置失败：CAN重新初始化失败");
                    return Task.FromResult(false);
                }

                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PXI4004Driver] 重置失败: {ex.Message}");
                return Task.FromResult(false);
            }
        }

        /// <summary>
        /// 软件验收过滤器：根据通道配置参数过滤接收到的CAN帧
        /// </summary>
        /// <param name="channelIndex">通道索引</param>
        /// <param name="frame">接收到的CAN帧</param>
        /// <param name="channelParams">通道配置参数</param>
        /// <returns>是否接受此帧</returns>
        public bool ApplySoftwareAcceptanceFilter(int channelIndex, PXI4004.ARTCANX1_CAN_FRAME frame, PXI4004.ARTCANX1_CAN_PARAM channelParams)
        {
            try
            {
                // 添加调试日志显示当前通道的验收过滤配置
                if (_driverReceiveLogsEnabled) Debug.WriteLine($"[PXI4004Driver] 通道 {channelIndex} 验收过滤配置 - 滤波计数: {channelParams.nAccFilterCnt}, 扩展帧支持: {channelParams.bAccExtID}, 掩码A: 0x{channelParams.nAccMaskA:X}, 代码A: 0x{channelParams.nAccCodeA:X}");

                // 如果未启用任何验收过滤则直接接收
                if (channelParams.nAccFilterCnt == 0)
                {
                    // 不参与滤波，接收所有帧
                    return true;
                }
                else
                {
                    // 已启用验收过滤，检查帧类型兼容性
                    // 对于扩展帧：若硬件未启用扩展帧验收，则过滤
                    if (frame.bExtendedID == 1 && channelParams.bAccExtID == 0)
                    {
                        return false;
                    }
                    else
                    {
                        // 验收过滤器逻辑：
                        // 如果验收掩码为0，表示接收所有帧（不进行ID过滤）
                        if (channelParams.nAccMaskA == 0)
                        {
                            return true;
                        }
                        else
                        {
                            // 仅使用单滤波 A（厂商例程中的常用模式）
                            // 对于标准帧，驱动/硬件将 11-bit ID 存放在 ID28..ID18 位（即左移 18 位）。
                            // 因此在软件过滤时需要将接收帧的标准 ID 左移 18 位再与掩码比较。
                            uint recvField;
                            if (frame.bExtendedID == 0)
                            {
                                // 标准帧：只取低 11 位并左移到 ID28..ID18
                                recvField = (frame.nFrameID & 0x7FFu) << 18;
                            }
                            else
                            {
                                // 扩展帧：帧 ID 已经包含扩展位
                                recvField = frame.nFrameID;
                            }

                            uint maskedRecv = recvField & channelParams.nAccMaskA;
                            uint maskedCode = channelParams.nAccCodeA & channelParams.nAccMaskA;
                            bool accepted = (maskedRecv == maskedCode);

                            // 添加调试日志
                            if (_driverReceiveLogsEnabled) Debug.WriteLine($"[PXI4004Driver] 通道 {channelIndex} 验收过滤 - 帧ID: 0x{frame.nFrameID:X}, 扩展帧: {frame.bExtendedID}, 接收字段: 0x{recvField:X}, 掩码: 0x{channelParams.nAccMaskA:X}, 代码: 0x{channelParams.nAccCodeA:X}, 过滤结果: {accepted}");
                            // 额外调试：如果上层使用原始 11-bit 格式，显示校正后的值供排查
                            try
                            {
                                if ((channelParams.nAccMaskA & ~0x7FFu) == 0)
                                {
                                    if (_driverReceiveLogsEnabled) Debug.WriteLine($"[PXI4004Driver] 通道 {channelIndex} 当前验收参数疑似 RAW 11-bit: code=0x{channelParams.nAccCodeA:X8}, mask=0x{channelParams.nAccMaskA:X8} (注意：建议使用 SetAcceptanceFilterAsync 传入 RAW 值，驱动会自动转换)");
                                }
                            }
                            catch { }

                            return accepted;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PXI4004Driver] 验收过滤异常: {ex.Message}");
                // 出错时保守策略：接收帧以便不丢失数据
                return true;
            }
        }

        /// <summary>
        /// 从任务参数构建CAN帧
        /// </summary>
        /// <param name="task">发送任务</param>
        /// <returns>CAN帧</returns>
        public PXI4004.ARTCANX1_CAN_FRAME BuildFrameFromTask(SendTaskParams task)
        {
            var frame = new PXI4004.ARTCANX1_CAN_FRAME();
            frame.DataBuf = new byte[8];

            // 解析帧ID
            uint idVal = 0;
            try
            {
                string s = task.Id?.Trim() ?? "0";
                if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    idVal = Convert.ToUInt32(s.Substring(2), 16);
                else if (s.EndsWith("h", StringComparison.OrdinalIgnoreCase))
                    idVal = Convert.ToUInt32(s.Substring(0, s.Length - 1), 16);
                else
                    idVal = Convert.ToUInt32(s);
            }
            catch
            {
                idVal = 0;
                Debug.WriteLine($"[PXI4004Driver] 帧ID解析失败，使用默认值0: {task.Id}");
            }
            frame.nFrameID = idVal;

            // 设置帧格式
            frame.bExtendedID = (byte)((task.FrameFormat?.Contains("扩展") == true) ? 1 : 0);
            frame.nFrameType = (byte)((task.FrameType?.Contains("远程") == true) ? 1 : 0);

            // 解析数据
            var dataParts = (task.Data ?? "").Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            int len = Math.Min(8, dataParts.Length);
            frame.nDataLength = (byte)len;

            // 初始化数据缓冲区
            for (int i = 0; i < 8; i++) frame.DataBuf[i] = 0;

            // 转换数据
            for (int i = 0; i < len; i++)
            {
                try
                {
                    frame.DataBuf[i] = Convert.ToByte(dataParts[i], 16);
                }
                catch
                {
                    frame.DataBuf[i] = 0;
                    Debug.WriteLine($"[PXI4004Driver] 数据字节解析失败，使用默认值0: {dataParts[i]}");
                }
            }

            return frame;
        }

        /// <summary>
        /// 发送任务并返回发送统计信息
        /// </summary>
        /// <param name="channelIndex">通道索引</param>
        /// <param name="task">发送任务</param>
        /// <param name="timeout">超时时间（秒）</param>
        /// <returns>发送结果和统计信息</returns>
        public async Task<SendResult> SendTaskAsync(int channelIndex, SendTaskParams task, double timeout = 0.2)
        {
            var result = new SendResult();

            if (task == null)
            {
                result.Success = false;
                result.ErrorMessage = "任务为空";
                return result;
            }

            try
            {
                // 构建帧
                var frame = BuildFrameFromTask(task);

                // 记录发送开始时间
                var startTime = System.Diagnostics.Stopwatch.GetTimestamp();

                // 发送帧
                result.Success = await SendFrameAsync(channelIndex, frame, timeout);

                // 计算耗时
                if (result.Success)
                {
                    var endTime = System.Diagnostics.Stopwatch.GetTimestamp();
                    var elapsedTicks = endTime - startTime;
                    result.ElapsedMs = (long)(elapsedTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
                }

                result.Frame = frame;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                Debug.WriteLine($"[PXI4004Driver] 发送任务异常: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// CAN帧发送任务参数
        /// </summary>
        public class SendTaskParams
        {
            public string Id { get; set; }
            public string FrameType { get; set; }
            public string FrameFormat { get; set; }
            public string Data { get; set; }
        }

        /// <summary>
        /// 发送结果
        /// </summary>
        public class SendResult
        {
            public bool Success { get; set; }
            public string ErrorMessage { get; set; }
            public long ElapsedMs { get; set; }
            public PXI4004.ARTCANX1_CAN_FRAME Frame { get; set; }
        }

        // 接收功能已移除（按需接收已在 ViewModel 层禁用）。如需恢复请调用底层 PXI4004.ReceiveFrame。

        /// <summary>
        /// 发送CAN帧（到指定通道）
        /// </summary>
        /// <param name="channelIndex">通道索引（0基）</param>
        /// <param name="frame">要发送的CAN帧</param>
        /// <param name="timeout">超时时间（秒），默认0.2秒</param>
        /// <returns>发送是否成功</returns>
        public async Task<bool> SendFrameAsync(int channelIndex, PXI4004.ARTCANX1_CAN_FRAME frame, double timeout = 0.2)
        {
            if (!_isConnected)
            {
                Debug.WriteLine($"[PXI4004Driver] 设备未连接，无法发送帧");
                return false;
            }

            if (!_openedChannels.Contains(channelIndex))
            {
                Debug.WriteLine($"[PXI4004Driver] 通道 {channelIndex} 未打开，无法发送帧");
                return false;
            }

            try
            {
                // ========== 模拟模式：模拟发送成功 ==========
#if USE_SIMULATION
                await Task.Delay(10); // 模拟发送延迟
                Debug.WriteLine($"[PXI4004Driver] 【模拟】发送帧到通道 {channelIndex}: ID=0x{frame.nFrameID:X}, DataLen={frame.nDataLength}");
                return true;
#endif
                // =============================================

                await Task.Yield(); // 避免阻塞UI线程

                bool success = PXI4004.SendFrame(_deviceHandle, (uint)channelIndex, ref frame, timeout);

                if (success)
                {
                    Debug.WriteLine($"[PXI4004Driver] 通道 {channelIndex} 发送帧成功: ID=0x{frame.nFrameID:X}, Len={frame.nDataLength}");
                }
                else
                {
                    Debug.WriteLine($"[PXI4004Driver] 通道 {channelIndex} 发送帧失败");
                }

                return success;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PXI4004Driver] 发送帧时发生异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 设置验收过滤器（标准帧单滤波验收）
        /// </summary>
        /// <param name="channelIndex">通道索引</param>
        /// <param name="acceptanceCode">验收码A（32位）</param>
        /// <param name="acceptanceMask">屏蔽码A（32位）</param>
        /// <returns>是否成功</returns>
        public async Task<bool> SetAcceptanceFilterAsync(int channelIndex, uint acceptanceCode, uint acceptanceMask)
        {
            if (!_isConnected || !_openedChannels.Contains(channelIndex))
            {
                Debug.WriteLine($"[PXI4004Driver] 通道 {channelIndex} 未连接或未打开，无法设置验收过滤器");
                return false;
            }

            try
            {
                // 获取当前参数作为基础
                var param = PXI4004.GetDefaultCANParam(_deviceHandle, (uint)channelIndex);

                // 设置验收过滤器参数
                param.nAccFilterCnt = (byte)PXI4004.ARTCANX1_CAN_ACC_NUM_SINGLE; // 单滤波

                // 接受码/掩码兼容处理：
                // - UI/上层可能以原始 11-bit 标准 ID（bit0..bit10）来指定代码和掩码；
                // - 硬件/驱动在寄存器中使用 ID28..ID18 的位置（即左移 18 位）。
                // 因此如果上层传入的掩码仅占 11 位（<= 0x7FF），我们自动将其视为 raw ID 并左移 18 位；
                // 否则视为已经是硬件格式，直接使用。
                uint convCode;
                uint convMask;
                if ((acceptanceMask & ~0x7FFu) == 0)
                {
                    // 上层以原始 11-bit 指定（或只指定了低 11 位），执行左移转换
                    convCode = (acceptanceCode & 0x7FFu) << 18;
                    convMask = (acceptanceMask & 0x7FFu) << 18;
                    Debug.WriteLine($"[PXI4004Driver] SetAcceptanceFilter: treating inputs as RAW 11-bit -> inputCode=0x{acceptanceCode:X8}, inputMask=0x{acceptanceMask:X8}, convCode=0x{convCode:X8}, convMask=0x{convMask:X8}");
                }
                else
                {
                    // 已经是硬件/寄存器格式，直接使用
                    convCode = acceptanceCode;
                    convMask = acceptanceMask;
                    Debug.WriteLine($"[PXI4004Driver] SetAcceptanceFilter: treating inputs as HW-format -> code=0x{convCode:X8}, mask=0x{convMask:X8}");
                }

                param.nAccCodeA = convCode;
                param.nAccMaskA = convMask;
                param.nAccCodeB = 0x00000000; // B组不使用
                param.nAccMaskB = 0xFFFFFFFF; // B组不使用
                // 验证参数（优先调用厂商提供的 VerifyParam），如果校验失败，尝试直接调用 InitTask（兼容部分固件）
                bool initSucceeded = false;
                uint nativeErr = 0;

                try
                {
                    bool verifyOk = true;
                    try
                    {
                        verifyOk = PXI4004.ARTCANX1_CAN_VerifyParam(_deviceHandle, (uint)channelIndex, ref param);
                    }
                    catch (Exception verifyEx)
                    {
                        // Verify 出错：记录但继续尝试 InitTask 以提高兼容性
                        Debug.WriteLine($"[PXI4004Driver] ARTCANX1_CAN_VerifyParam 异常: {verifyEx.Message}");
                        verifyOk = false;
                    }

                    // 先停止通道以便安全地重新初始化参数（部分硬件要求先停止通道）
                    try { PXI4004.StopCAN(_deviceHandle, (uint)channelIndex); } catch { }

                    if (verifyOk)
                    {
                        // 使用 InitCAN（包含 Verify 调用）进行标准初始化
                        try
                        {
                            initSucceeded = PXI4004.InitCAN(_deviceHandle, (uint)channelIndex, ref param);
                        }
                        catch (Exception exInit)
                        {
                            Debug.WriteLine($"[PXI4004Driver] PXI4004.InitCAN 异常: {exInit.Message}");
                            initSucceeded = false;
                        }
                    }
                    else
                    {
                        // Verify 未通过或不可用，尝试直接调用底层 InitTask（跳过 Verify）
                        try
                        {
                            initSucceeded = PXI4004.ARTCANX1_CAN_InitTask(_deviceHandle, (uint)channelIndex, ref param, IntPtr.Zero);
                        }
                        catch (Exception exDirect)
                        {
                            Debug.WriteLine($"[PXI4004Driver] 直接调用 ARTCANX1_CAN_InitTask 异常: {exDirect.Message}");
                            initSucceeded = false;
                        }
                    }

                    if (!initSucceeded)
                    {
                        // 获取原生错误码帮助定位
                        try { nativeErr = PXI4004.ARTCANX1_AUX_GetLastError(); } catch { nativeErr = 0; }
                        Debug.WriteLine($"[PXI4004Driver] 设置验收过滤器失败：InitCAN/InitTask 返回失败，nativeErr=0x{nativeErr:X8}");
                        return false;
                    }

                    // 重新启动CAN任务以应用新参数
                    if (!PXI4004.StartCAN(_deviceHandle, (uint)channelIndex))
                    {
                        try { nativeErr = PXI4004.ARTCANX1_AUX_GetLastError(); } catch { nativeErr = 0; }
                        Debug.WriteLine($"[PXI4004Driver] 设置验收过滤器失败：StartCAN 返回失败，nativeErr=0x{nativeErr:X8}");
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[PXI4004Driver] 设置验收过滤器异常: {ex.Message}");
                    try { nativeErr = PXI4004.ARTCANX1_AUX_GetLastError(); } catch { nativeErr = 0; }
                    Debug.WriteLine($"[PXI4004Driver] 原生错误码: 0x{nativeErr:X8}");
                    return false;
                }

                Debug.WriteLine($"[PXI4004Driver] 通道 {channelIndex} 验收过滤器设置成功 - 验收码: 0x{acceptanceCode:X8}, 屏蔽码: 0x{acceptanceMask:X8}");
                await Task.Delay(20); // 稍作等待
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PXI4004Driver] 设置验收过滤器异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 禁用验收过滤器
        /// </summary>
        /// <param name="channelIndex">通道索引</param>
        /// <returns>是否成功</returns>
        public async Task<bool> DisableAcceptanceFilterAsync(int channelIndex)
        {
            if (!_isConnected || !_openedChannels.Contains(channelIndex))
            {
                Debug.WriteLine($"[PXI4004Driver] 通道 {channelIndex} 未连接或未打开，无法禁用验收过滤器");
                return false;
            }

            try
            {
                // 获取当前参数作为基础
                var param = PXI4004.GetDefaultCANParam(_deviceHandle, (uint)channelIndex);

                // 禁用验收过滤器
                param.nAccFilterCnt = (byte)PXI4004.ARTCANX1_CAN_ACC_NUM_NONE; // 禁止滤波
                param.nAccCodeA = 0x00000000;
                param.nAccMaskA = 0xFFFFFFFF;
                param.nAccCodeB = 0x00000000;
                param.nAccMaskB = 0xFFFFFFFF;

                // 验证参数
                if (!PXI4004.InitCAN(_deviceHandle, (uint)channelIndex, ref param))
                {
                    Debug.WriteLine($"[PXI4004Driver] 禁用验收过滤器失败：参数验证失败");
                    return false;
                }

                // 重新启动CAN任务以应用新参数
                if (!PXI4004.StartCAN(_deviceHandle, (uint)channelIndex))
                {
                    Debug.WriteLine($"[PXI4004Driver] 禁用验收过滤器失败：重启CAN任务失败");
                    return false;
                }

                Debug.WriteLine($"[PXI4004Driver] 通道 {channelIndex} 验收过滤器已禁用");
                await Task.Delay(20); // 稍作等待
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PXI4004Driver] 禁用验收过滤器异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 获取通道接收性能统计信息
        /// </summary>
        /// <param name="channelIndex">通道索引</param>
        /// <returns>接收统计信息</returns>
        public ReceiveStats GetReceiveStats(int channelIndex)
        {
            if (_receiveStats.TryGetValue(channelIndex, out var stats))
            {
                return new ReceiveStats
                {
                    ConsecutiveEmptyCount = stats.ConsecutiveEmptyCount,
                    TotalFramesReceived = stats.TotalFramesReceived,
                    LastReceiveTime = stats.LastReceiveTime,
                    AdaptiveTimeout = stats.AdaptiveTimeout
                };
            }
            return new ReceiveStats();
        }

        // 简单比较两个 CAN 参数结构的关键字段，判断是否等价（用于避免重复初始化）
        private bool AreCanParamsEqual(PXI4004.ARTCANX1_CAN_PARAM a, PXI4004.ARTCANX1_CAN_PARAM b)
        {
            try
            {
                if (a.nBaudRate != b.nBaudRate) return false;
                if (a.nWorkMode != b.nWorkMode) return false;
                if (a.bAccExtID != b.bAccExtID) return false;
                if (a.nAccFilterCnt != b.nAccFilterCnt) return false;
                if (a.nAccCodeA != b.nAccCodeA) return false;
                if (a.nAccMaskA != b.nAccMaskA) return false;
                if (a.nAccCodeB != b.nAccCodeB) return false;
                if (a.nAccMaskB != b.nAccMaskB) return false;
                // 如果需要进一步比较其他字段可在此扩展
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 重置通道接收统计信息
        /// </summary>
        /// <param name="channelIndex">通道索引</param>
        public void ResetReceiveStats(int channelIndex)
        {
            _receiveStats.Remove(channelIndex);
            Debug.WriteLine($"[PXI4004Driver] 通道 {channelIndex} 接收统计信息已重置");
            // 同步清理已记录的已应用参数（避免遗留状态导致重复初始化判断异常）
            _channelAppliedParams.Remove(channelIndex);
        }


        /// <summary>
        /// 启动接收任务（驱动层）：对于本驱动，物理通道在 OpenChannelAsync 时已调用 StartCAN。
        /// 本方法仅验证通道已打开并返回准备就绪状态，避免重复调用 StartCAN 导致内部失败。
        /// </summary>
        /// <param name="channelIndex">通道索引</param>
        /// <returns>是否成功</returns>
        public Task<bool> StartReceiveTaskAsync(int channelIndex)
        {
            if (!_isConnected || _deviceHandle == IntPtr.Zero)
            {
                Debug.WriteLine($"[PXI4004Driver] 启动接收任务失败：设备未连接");
                return Task.FromResult(false);
            }

            if (!_openedChannels.Contains(channelIndex))
            {
                Debug.WriteLine($"[PXI4004Driver] 启动接收任务失败：通道 {channelIndex} 未打开");
                return Task.FromResult(false);
            }

            try
            {
                // 通道已经通过 OpenChannelAsync 初始化并启动底层任务（InitCAN + StartCAN）。
                // 这里避免重复调用 StartCAN，直接认为接收已准备就绪。
                Debug.WriteLine($"[PXI4004Driver] 通道 {channelIndex} 接收任务已准备就绪（通道已打开）");
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PXI4004Driver] 验证接收任务准备就绪时发生异常: {ex.Message}");
                return Task.FromResult(false);
            }
        }

        /// <summary>
        /// 停止接收任务
        /// </summary>
        /// <param name="channelIndex">通道索引</param>
        /// <returns>是否成功</returns>
        public Task<bool> StopReceiveTaskAsync(int channelIndex)
        {
            if (!_isConnected || _deviceHandle == IntPtr.Zero)
            {
                Debug.WriteLine($"[PXI4004Driver] 停止接收任务失败：设备未连接");
                return Task.FromResult(false);
            }

            try
            {
                Debug.WriteLine($"[PXI4004Driver] 停止通道 {channelIndex} 接收任务");

                bool success = PXI4004.StopCAN(_deviceHandle, (uint)channelIndex);
                if (success)
                {
                    Debug.WriteLine($"[PXI4004Driver] 通道 {channelIndex} 接收任务停止成功");
                    // 标记该通道不再处于已打开/运行状态，避免上层误认为硬件仍在接收
                    try
                    {
                        // 停止通道后同时释放通道资源以确保可以安全重新初始化
                        try { PXI4004.ReleaseCAN(_deviceHandle, (uint)channelIndex); } catch { }
                        _openedChannels.Remove(channelIndex);
                        _channelAppliedParams.Remove(channelIndex);
                    }
                    catch { }
                }
                else
                {
                    Debug.WriteLine($"[PXI4004Driver] 通道 {channelIndex} 接收任务停止失败");
                }

                return Task.FromResult(success);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PXI4004Driver] 停止接收任务时发生异常: {ex.Message}");
                return Task.FromResult(false);
            }
        }


        /// <summary>
        /// 自检
        /// </summary>
        public Task<bool> SelfTestAsync()
        {
            Debug.WriteLine($"[PXI4004Driver] 执行自检");

            // 简单的自检：检查连接状态和设备句柄
            if (!_isConnected)
            {
                Debug.WriteLine($"[PXI4004Driver] 自检失败：设备未连接");
                return Task.FromResult(false);
            }

            if (_deviceHandle == IntPtr.Zero || _deviceHandle == (IntPtr)(-1))
            {
                Debug.WriteLine($"[PXI4004Driver] 自检失败：设备句柄无效");
                return Task.FromResult(false);
            }

            Debug.WriteLine($"[PXI4004Driver] 自检通过");
            return Task.FromResult(true);
        }

        #endregion
    }
}