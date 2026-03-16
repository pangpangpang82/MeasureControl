using MeasureControl.Models;
using MeasureControl.Models.Devices;
using Sys = System;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MeasureControl.Drivers
{
    /// <summary>
    /// ART1553B MIL-STD-1553B 总线设备驱动
    /// 支持 BC、RT、BM 三种工作模式，支持连续采样和多线程操作
    /// </summary>
    public class ART1553BDriver : IDeviceDriver, IDisposable
    {
        /// <summary>
        /// 采集状态改变事件
        /// </summary>
        public event EventHandler<AcquisitionStatusChangedEventArgs> AcquisitionStatusChanged;

        /// <summary>
        /// 设备功能类型
        /// </summary>
        public DeviceCapability Capability => DeviceCapability.Communication;

        #region 私有字段

        private readonly DeviceBase _device;
        private readonly uint _serialNumber;
        private readonly int _slotNumber;
        private IntPtr _deviceHandle = IntPtr.Zero;
        private bool _isConnected;
        private bool _isRunning;
        private int _currentChannel = 0;
        private DeviceMode _currentMode = DeviceMode.None;

        // 模拟模式
        private static readonly bool USE_SIMULATION = false;

        // 线程控制
        private Thread _monitorThread;
        private Thread _rtReceiveThread;
        private Thread _bmMonitorThread;
        private Thread _bmDedicatedThread;
        private Thread _synchronizedMonitorThread; // 同步监控线程
        private bool _monitoring = false;
        private bool _synchronizedMonitoring = false; // 同步监控状态
        private volatile bool _bmDedicatedRunning = false;
        private readonly object _lockObject = new object();

        // 统计数据
        private ulong _bcSendCount = 0;
        private ulong _rtReceiveCount = 0;
        private ulong _rtErrorCount = 0;
        private ulong _bmReceiveCount = 0;
        private ulong _bmErrorCount = 0;

        // 消息缓存
        private readonly Dictionary<int, List<ART1553B.RMSG_STRUCT>> _messageQueues = new Dictionary<int, List<ART1553B.RMSG_STRUCT>>();
        private readonly Dictionary<int, ART1553B.SMSG_STRUCT> _bcMessages = new Dictionary<int, ART1553B.SMSG_STRUCT>();

        private readonly HashSet<int> _rtMonitoringChannels = new HashSet<int>();

        private static int[,] CreateBMAllPassFilter()
        {
            var filter = new int[32, 2];
            for (int i = 0; i < 32; i++)
            {
                filter[i, 0] = unchecked((int)0xFFFFFFFF);
                filter[i, 1] = unchecked((int)0xFFFFFFFF);
            }
            return filter;
        }

        #endregion

        #region 枚举和结构

        /// <summary>
        /// 设备工作模式
        /// </summary>
        public enum DeviceMode
        {
            None = 0,
            BC = 1,      // 总线控制器
            RT = 2,      // 远程终端
            BM = 3,      // 总线监视器
            BC_RT_BM = 4 // 混合模式
        }

        /// <summary>
        /// 消息类型
        /// </summary>
        public enum MessageType
        {
            BC_RT = ART1553B.BC_MSGTYPE_BCRT,
            RT_RT = ART1553B.BC_MSGTYPE_RTRT,
            Broadcast = ART1553B.BC_MSGTYPE_BROADCAST,
            RT_RTs = ART1553B.BC_MSGTYPE_RTRTS,
            ModeCode = ART1553B.BC_MSGTYPE_MODECODE,
            BroadcastMode = ART1553B.BC_MSGTYPE_BROADCASTMODE
        }

        /// <summary>
        /// 模式代码
        /// </summary>
        public enum ModeCodeType
        {
            DynamicBusControl = ART1553B.MODECODE_DYNCBUSCONTROL,
            SyncTransmit = ART1553B.MODECODE_SYNC_TX,
            TransmitPreviousStatus = ART1553B.MODECODE_TXPREVSTATUS,
            SelfTest = ART1553B.MODECODE_SELFTEST,
            TransmitterShutdown = ART1553B.MODECODE_XOFF,
            CancelTransmitterShutdown = ART1553B.MODECODE_CANCELXOFF,
            TransmitVectorWord = ART1553B.MODECODE_TXVECTOR,
            SyncReceive = ART1553B.MODECODE_SYNC_RX
        }

        /// <summary>
        /// 通道配置
        /// </summary>
        public class ChannelConfig
        {
            public string ChannelId { get; set; }
            public ChannelType Type { get; set; }
            public int RTAddress { get; set; } = -1;
            public int SubAddress { get; set; } = -1;
            public int DataLength { get; set; } = 32;
            public bool IsTransmit { get; set; } = false;
            public MessageType MsgType { get; set; } = MessageType.BC_RT;
            public int MessageGap { get; set; } = 20; // us
            public bool RetryEnabled { get; set; } = false;
            public int ChannelSelection { get; set; } = 1; // 0:Channel B, 1:Channel A
        }

        /// <summary>
        /// 通道类型
        /// </summary>
        public enum ChannelType
        {
            Control,
            Status,
            Data,
            RT_Address,
            Message_Config
        }

        /// <summary>
        /// 设备状态信息
        /// </summary>
        public class DeviceStatus
        {
            public bool IsConnected { get; set; }
            public DeviceMode CurrentMode { get; set; }
            public int CurrentChannel { get; set; }
            public bool IsRunning { get; set; }
            public ulong BC_SendCount { get; set; }
            public ulong RT_ReceiveCount { get; set; }
            public ulong RT_ErrorCount { get; set; }
            public ulong BM_ReceiveCount { get; set; }
            public ulong BM_ErrorCount { get; set; }
            public int MessageQueueSize { get; set; }
            public uint FirmwareVersion { get; set; }
            public uint DriverVersion { get; set; }
            public DateTime StartTime { get; set; }
            public TimeSpan Uptime => DateTime.Now - StartTime;
        }

        #endregion

        #region 属性

        public string DeviceId => _device?.Id ?? string.Empty;
        public string DeviceName => _device?.Name ?? "ART1553B";
        public bool IsConnected => _isConnected;
        public bool IsSimulated => USE_SIMULATION;
        public DeviceMode CurrentMode => _currentMode;
        public int CurrentChannel => _currentChannel;
        public uint SerialNumber => _serialNumber;
        public ulong BC_SendCount => _bcSendCount;
        public ulong RT_ReceiveCount => _rtReceiveCount;
        public ulong RT_ErrorCount => _rtErrorCount;
        public ulong BM_ReceiveCount => _bmReceiveCount;
        public ulong BM_ErrorCount => _bmErrorCount;

        // 事件声明
        public event EventHandler<MessageReceivedEventArgs> MessageReceived;
        public event EventHandler<DeviceErrorEventArgs> ErrorOccurred;
        public event EventHandler<StatusUpdatedEventArgs> StatusUpdated;

        #endregion

        #region 构造函数和初始化

        public ART1553BDriver(DeviceBase device, uint serialNumber, int slotNumber = 0)
        {
            _device = device ?? throw new ArgumentNullException(nameof(device));
            _serialNumber = serialNumber;
            _slotNumber = slotNumber;
            _isConnected = false;
            _isRunning = false;
            _currentMode = DeviceMode.None;

            // 初始化消息队列
            for (int i = 0; i <= 31; i++)
            {
                _messageQueues[i] = new List<ART1553B.RMSG_STRUCT>();
            }
        }

        #endregion

        #region IDeviceDriver 实现 - 基础连接

        public async Task<bool> ConnectAsync()
        {
            try
            {
                Debug.WriteLine($"[ART1553BDriver] 正在连接设备 {DeviceName}, 序列号: {_serialNumber}");

                if (USE_SIMULATION)
                {
                    Debug.WriteLine($"[ART1553BDriver] 【模拟模式】连接成功");
                    await Task.Delay(100);
                    _isConnected = true;
                    return true;
                }

                // 打开设备
                int ret = ART1553B.ART1553B_Open(ref _deviceHandle, _serialNumber);
                if (_deviceHandle == (IntPtr)(-1) || ret != ART1553B.ART1553Success)
                {
                    Debug.WriteLine($"[ART1553BDriver] 打开设备失败，错误码: {ret}");
                    OnErrorOccurred($"打开设备失败，错误码: {ret}");
                    return false;
                }

                // 获取设备信息
                await GetDeviceInfoAsync();

                // 复位设备
                ret = ART1553B.ART1553B_Reset(_deviceHandle);
                if (ret != ART1553B.ART1553Success)
                {
                    Debug.WriteLine($"[ART1553BDriver] 复位设备失败，错误码: {ret}");
                }

                // 复位当前通道
                ret = ART1553B.ART1553B_ChannelReset(_deviceHandle, CurrentChannel);//根据使能的通道来
                if (ret != ART1553B.ART1553Success)
                {
                    Debug.WriteLine($"[ART1553BDriver] 复位通道失败，错误码: {ret}");
                }

                await Task.Delay(100);
                _isConnected = true;
                Debug.WriteLine($"[ART1553BDriver] 设备连接成功");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ART1553BDriver] 连接失败: {ex.Message}");
                OnErrorOccurred($"连接失败: {ex.Message}");
                _isConnected = false;
                return false;
            }
        }

        public async Task<bool> DisconnectAsync()
        {
            try
            {
                Debug.WriteLine($"[ART1553BDriver] 正在断开设备 {DeviceName}");

                // 停止监控线程
                await StopMonitoringAsync();

                if (USE_SIMULATION)
                {
                    _isConnected = false;
                    _isRunning = false;
                    Debug.WriteLine($"[ART1553BDriver] 【模拟】设备断开成功");
                    return true;
                }

                // 停止所有模式
                await StopAcquisitionAsync();

                // 关闭设备
                if (_deviceHandle != IntPtr.Zero)
                {
                    int ret = ART1553B.ART1553B_Close(_deviceHandle);
                    if (ret != ART1553B.ART1553Success)
                    {
                        Debug.WriteLine($"[ART1553BDriver] 关闭设备失败，错误码: {ret}");
                    }
                    _deviceHandle = IntPtr.Zero;
                }

                await Task.Delay(50);
                _isConnected = false;
                _currentMode = DeviceMode.None;
                Debug.WriteLine($"[ART1553BDriver] 设备断开成功");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ART1553BDriver] 断开失败: {ex.Message}");
                OnErrorOccurred($"断开失败: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region 模式配置方法

        /// <summary>
        /// 配置BC模式
        /// </summary>
        public async Task<bool> ConfigureBCModeAsync(int channel, ushort responseTime = 4000, int frameGap = 10)
        {
            if (!_isConnected)
            {
                Debug.WriteLine($"[ART1553BDriver] 设备未连接");
                return false;
            }

            try
            {
                Debug.WriteLine($"[ART1553BDriver] 配置BC模式，通道: {channel}");

                // 初始化BC
                int ret = ART1553B.BC_Init(_deviceHandle, channel);
                if (ret != ART1553B.ART1553Success)
                {
                    Debug.WriteLine($"[ART1553BDriver] BC初始化失败，错误码: {ret}");
                    return false;
                }

                // 设置响应超时
                ret = ART1553B.BC_SetRespTimeout(_deviceHandle, channel, responseTime);
                if (ret != ART1553B.ART1553Success)
                {
                    Debug.WriteLine($"[ART1553BDriver] 设置响应超时失败，错误码: {ret}");
                    return false;
                }

                // 设置帧间隔
                ret = ART1553B.BC_SetFrameGap(_deviceHandle, channel, frameGap);
                if (ret != ART1553B.ART1553Success)
                {
                    Debug.WriteLine($"[ART1553BDriver] 设置帧间隔失败，错误码: {ret}");
                    return false;
                }

                _currentChannel = channel;
                _currentMode = DeviceMode.BC;
                Debug.WriteLine($"[ART1553BDriver] BC模式配置完成");

                if (_isRunning && !_monitoring)
                {
                    Task.Run(async () => await StartMonitoringAsync());
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ART1553BDriver] 配置BC模式失败: {ex.Message}");
                OnErrorOccurred($"配置BC模式失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 配置RT模式
        /// </summary>
        public async Task<bool> ConfigureRTModeAsync(int channel, int rtAddress = 1, ushort responseTime = 500, bool setAsCurrent = true)
        {
            if (!_isConnected)
            {
                Debug.WriteLine($"[ART1553BDriver] 设备未连接");
                return false;
            }

            try
            {
                Debug.WriteLine($"[ART1553BDriver] 配置RT模式，通道: {channel}, RT地址: {rtAddress}");

                // 初始化RT
                int ret = ART1553B.RT_Init(_deviceHandle, channel);
                if (ret != ART1553B.ART1553Success)
                {
                    Debug.WriteLine($"[ART1553BDriver] RT初始化失败，错误码: {ret}");
                    return false;
                }

                // 设置响应时间
                ret = ART1553B.RT_SetRespTime(_deviceHandle, channel, responseTime);
                if (ret != ART1553B.ART1553Success)
                {
                    Debug.WriteLine($"[ART1553BDriver] 设置响应时间失败，错误码: {ret}");
                    return false;
                }

                // RT使能
                int rtEnableMask = 0x01 << rtAddress;
                ret = ART1553B.RT_Select(_deviceHandle, channel, rtEnableMask);
                if (ret != ART1553B.ART1553Success)
                {
                    Debug.WriteLine($"[ART1553BDriver] RT使能失败，错误码: {ret}");
                    return false;
                }

                var txMode = new ART1553B.RT_TX_MODE_STRUCT();
                ret = ART1553B.RT_TxMode(_deviceHandle, channel, rtAddress, ref txMode);
                if (ret != ART1553B.ART1553Success)
                {
                    Debug.WriteLine($"[ART1553BDriver] RT设置TxMode失败，错误码: {ret}");
                    return false;
                }

                if (_isRunning)
                {
                    ret = ART1553B.RT_Start(_deviceHandle, channel, true);
                    if (ret != ART1553B.ART1553Success)
                    {
                        Debug.WriteLine($"[ART1553BDriver] 启动RT失败，错误码: {ret}");
                        return false;
                    }
                }

                lock (_lockObject)
                {
                    _rtMonitoringChannels.Add(channel);
                }

                if (setAsCurrent)
                {
                    _currentChannel = channel;
                }

                _currentMode = DeviceMode.RT;
                Debug.WriteLine($"[ART1553BDriver] RT模式配置完成");

                if (_isRunning && !_monitoring)
                {
                    Task.Run(async () => await StartMonitoringAsync());
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ART1553BDriver] 配置RT模式失败: {ex.Message}");
                OnErrorOccurred($"配置RT模式失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 注册RT监控通道（供外部调用，确保RT接收监控在正确的通道上运行）
        /// </summary>
        public void RegisterRTMonitoringChannel(int channel)
        {
            lock (_lockObject)
            {
                if (!_rtMonitoringChannels.Contains(channel))
                {
                    _rtMonitoringChannels.Add(channel);
                    Debug.WriteLine($"[ART1553BDriver] 注册RT监控通道: {channel}");
                }
            }
        }

        /// <summary>
        /// 取消注册RT监控通道
        /// </summary>
        public void UnregisterRTMonitoringChannel(int channel)
        {
            lock (_lockObject)
            {
                _rtMonitoringChannels.Remove(channel);
                Debug.WriteLine($"[ART1553BDriver] 取消注册RT监控通道: {channel}");
            }
        }

        public void SetCurrentMode(DeviceMode mode, int channel)
        {
            lock (_lockObject)
            {
                _currentMode = mode;
                _currentChannel = channel;
            }
        }

        /// <summary>
        /// 启动监控线程
        /// </summary>
        public async Task<bool> StartMonitoringAsync()
        {
            if (!_isConnected)
            {
                Debug.WriteLine($"[ART1553BDriver] 设备未连接");
                return false;
            }

            try
            {
                if (_monitoring)
                {
                    Debug.WriteLine($"[ART1553BDriver] 监控线程已在运行");
                    return true;
                }

                _monitoring = true;

                if (_monitorThread == null || !_monitorThread.IsAlive)
                {
                    _monitorThread = new Thread(MonitorLoop)
                    {
                        IsBackground = true,
                        Name = "ART1553B_Monitor"
                    };
                    _monitorThread.Start();
                    Debug.WriteLine($"[ART1553BDriver] 监控线程已启动");
                }

                await Task.CompletedTask;
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ART1553BDriver] 启动监控线程异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 停止监控线程
        /// </summary>
        public async Task StopMonitoringAsync()
        {
            try
            {
                if (!_monitoring)
                    return;

                _monitoring = false;

                try
                {
                    _monitorThread?.Join(1000);
                }
                catch { }

                _monitorThread = null;
                _rtReceiveThread = null;
                _bmMonitorThread = null;
                _synchronizedMonitorThread = null;

                await Task.CompletedTask;
            }
            catch { }
        }

        private void MonitorLoop()
        {
            try
            {
                while (_monitoring)
                {
                    if (_currentMode == DeviceMode.RT)
                    {
                        MonitorRTReceiveOnce();
                    }
                    else if (_currentMode == DeviceMode.BM)
                    {
                        if (!_bmDedicatedRunning)
                        {
                            PollBMOnce(_currentChannel, 64);
                        }
                    }
                    else if (_currentMode == DeviceMode.BC_RT_BM)
                    {
                        MonitorRTReceiveOnce();
                        if (!_bmDedicatedRunning)
                        {
                            PollBMOnce(_currentChannel, 64);
                        }
                    }

                    Thread.Sleep(10);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ART1553BDriver] 监控线程异常: {ex.Message}");
            }
        }

        private void MonitorRTReceiveOnce()
        {
            if (USE_SIMULATION || !_isConnected || _deviceHandle == IntPtr.Zero)
                return;

            int[] channels;
            lock (_lockObject)
            {
                channels = _rtMonitoringChannels.Count > 0 ? _rtMonitoringChannels.ToArray() : new[] { _currentChannel };
            }

            foreach (var channel in channels)
            {
                for (int rtAddr = 0; rtAddr <= 31; rtAddr++)
                {
                    var recvMsg = new ART1553B.RMSG_STRUCT();
                    recvMsg.MsgBlock.Datablk = new ushort[32];
                    int msgReadedNum = 0;

                    int ret = ART1553B.RT_ReadMsg(_deviceHandle, channel, rtAddr, ref recvMsg, ref msgReadedNum, 1);
                    if (ret == ART1553B.ART1553Success && msgReadedNum > 0)
                    {
                        lock (_lockObject)
                        {
                            _rtReceiveCount += (ulong)msgReadedNum;

                            if (!_messageQueues.TryGetValue(rtAddr, out var queue))
                            {
                                queue = new List<ART1553B.RMSG_STRUCT>();
                                _messageQueues[rtAddr] = queue;
                            }
                            queue.Add(recvMsg);
                        }

                        OnMessageReceived(new MessageReceivedEventArgs
                        {
                            Channel = channel,
                            RTAddress = rtAddr,
                            Message = recvMsg,
                            MessageType = "RT_Receive",
                            Timestamp = DateTime.Now
                        });
                    }
                }
            }
        }

        /// <summary>
        /// 配置BM模式
        /// </summary>
        public async Task<bool> ConfigureBMModeAsync(int channel)
        {
            if (!_isConnected)
            {
                Debug.WriteLine($"[ART1553BDriver] 设备未连接");
                return false;
            }

            try
            {
                Debug.WriteLine($"[ART1553BDriver] 配置BM模式，通道: {channel}");

                // 初始化BM
                int ret = ART1553B.BM_Init(_deviceHandle, channel);
                if (ret != ART1553B.ART1553Success)
                {
                    Debug.WriteLine($"[ART1553BDriver] BM初始化失败，错误码: {ret}");
                    return false;
                }

                _currentChannel = channel;
                _currentMode = DeviceMode.BM;
                Debug.WriteLine($"[ART1553BDriver] BM模式配置完成");

                if (_isRunning && !_monitoring)
                {
                    Task.Run(async () => await StartMonitoringAsync());
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ART1553BDriver] 配置BM模式失败: {ex.Message}");
                OnErrorOccurred($"配置BM模式失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 为 BM 设置命令过滤表（Filter 矩阵尺寸应为 [32,2]，未填充项为0）
        /// </summary>
        /// <param name="channel">通道号</param>
        /// <param name="filter">二维数组，最大 32x2</param>
        /// <returns>是否成功</returns>
        public bool SetBMFilterTable(int channel, int[,] filter)
        {
            if (!_isConnected || _deviceHandle == IntPtr.Zero)
            {
                Debug.WriteLine($"[ART1553BDriver] 设备未连接，无法设置BM过滤表");
                return false;
            }

            try
            {
                var ftable = new ART1553B.BM_CMD_FILTER_TABLE_STRUCT();
                // 初始化为 0
                ftable.Filter = new int[32, 2];

                if (filter != null)
                {
                    int rows = Math.Min(32, filter.GetLength(0));
                    int cols = Math.Min(2, filter.GetLength(1));
                    for (int i = 0; i < rows; i++)
                    {
                        for (int j = 0; j < cols; j++)
                        {
                            ftable.Filter[i, j] = filter[i, j];
                        }
                    }
                }

                int ret = ART1553B.BM_SetCmdFilterTable(_deviceHandle, channel, ref ftable);
                if (ret != ART1553B.ART1553Success)
                {
                    Debug.WriteLine($"[ART1553BDriver] BM 设置过滤表失败，错误码: {ret}");
                    return false;
                }

                Debug.WriteLine($"[ART1553BDriver] BM 过滤表设置成功 (channel={channel})");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ART1553BDriver] 设置BM过滤表异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 启动 BM 并可选下发过滤表（会先调用 BM_Init，然后设置过滤表并启动 BM）
        /// </summary>
        public bool StartBMWithFilter(int channel, int[,] filter = null)
        {
            if (!_isConnected || _deviceHandle == IntPtr.Zero)
            {
                Debug.WriteLine($"[ART1553BDriver] 设备未连接，无法启动BM");
                return false;
            }

            try
            {
                int ret = ART1553B.BM_Init(_deviceHandle, channel);
                if (ret != ART1553B.ART1553Success)
                {
                    Debug.WriteLine($"[ART1553BDriver] BM 初始化失败，错误码: {ret}");
                    return false;
                }

                if (filter == null)
                {
                    filter = CreateBMAllPassFilter();
                }

                if (filter != null)
                {
                    bool ok = SetBMFilterTable(channel, filter);
                    if (!ok)
                    {
                        Debug.WriteLine($"[ART1553BDriver] BM 过滤表下发失败，将继续尝试启动BM");
                    }
                }

                ret = ART1553B.BM_Start(_deviceHandle, channel, true);
                if (ret != ART1553B.ART1553Success)
                {
                    Debug.WriteLine($"[ART1553BDriver] BM 启动失败，错误码: {ret}");
                    return false;
                }

                _currentChannel = channel;
                _currentMode = DeviceMode.BM;
                Debug.WriteLine($"[ART1553BDriver] BM 已启动 (channel={channel})");

                // 启动专用的 BM 轮询线程，确保 BM 消息被持续读取（即使其他监控线程未开启）
                try
                {
                    if (!_bmDedicatedRunning || _bmDedicatedThread == null || !_bmDedicatedThread.IsAlive)
                    {
                        _bmDedicatedRunning = true;
                        _bmDedicatedThread = new Thread(() => MonitorBMModeLoop(channel))
                        {
                            IsBackground = true,
                            Name = $"ART1553B_BMLoop_ch{channel}"
                        };
                        _bmDedicatedThread.Start();
                        Debug.WriteLine($"[ART1553BDriver] BM 专用读取线程已启动 (channel={channel})");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ART1553BDriver] 启动 BM 专用线程失败: {ex.Message}");
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ART1553BDriver] 启动BM失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 停止 BM 监控
        /// </summary>
        public bool StopBM(int channel)
        {
            if (!_isConnected || _deviceHandle == IntPtr.Zero)
                return false;

            try
            {
                // 先停止专用 BM 线程，优雅退出后再停止硬件，避免并发访问设备句柄
                try
                {
                    _bmDedicatedRunning = false;
                    if (_bmDedicatedThread != null)
                    {
                        if (_bmDedicatedThread.IsAlive && !_bmDedicatedThread.Join(1000))
                        {
                            Debug.WriteLine($"[ART1553BDriver] BM 专用线程未在超时内退出，线程仍然为后台线程，不强制终止");
                        }
                        _bmDedicatedThread = null;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ART1553BDriver] 停止 BM 专用线程异常: {ex.Message}");
                    _bmDedicatedThread = null;
                }

                int ret = ART1553B.BM_Start(_deviceHandle, channel, false);
                if (ret != ART1553B.ART1553Success)
                {
                    Debug.WriteLine($"[ART1553BDriver] 停止BM失败，错误码: {ret}");
                    return false;
                }

                Debug.WriteLine($"[ART1553BDriver] BM 已停止 (channel={channel})");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ART1553BDriver] 停止BM异常: {ex.Message}");
                return false;
            }
        }

        public void PollBMOnce(int channel, int maxToRead = 64)
        {
            if (!_isConnected || _deviceHandle == IntPtr.Zero)
                return;

            try
            {
                int toRead = Math.Min(Math.Max(1, maxToRead), 64);
                var msgs = new ART1553B.RMSG_STRUCT[toRead];
                for (int i = 0; i < toRead; i++)
                {
                    var m = new ART1553B.RMSG_STRUCT();
                    m.MsgBlock.Datablk = new ushort[32];
                    msgs[i] = m;
                }

                int readedMsgCount = 0;
                int rc = ART1553B.BM_ReadMsg_Newly(_deviceHandle, channel, msgs, ref readedMsgCount, toRead);
                if (rc != ART1553B.ART1553Success)
                {
                    Debug.WriteLine($"[ART1553BDriver] BM_ReadMsg_Newly 返回错误: {rc} (channel={channel})");
                    return;
                }

                if (readedMsgCount <= 0)
                    return;

                lock (_lockObject)
                {
                    _bmReceiveCount += (ulong)readedMsgCount;
                }

                int count = Math.Min(readedMsgCount, msgs.Length);
                for (int i = 0; i < count; i++)
                {
                    var msg = msgs[i];
                    int rtAddr = (msg.MsgBlock.CmdWord1 >> 11) & 0x1F;
                    OnMessageReceived(new MessageReceivedEventArgs
                    {
                        Channel = channel,
                        RTAddress = rtAddr,
                        Message = msg,
                        MessageType = "BM_Receive",
                        Timestamp = DateTime.Now
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ART1553BDriver] PollBMOnce 异常: {ex.Message}");
            }
        }

        private void MonitorBMModeLoop(int channel)
        {
            try
            {
                Debug.WriteLine($"[ART1553BDriver] MonitorBMModeLoop start (channel={channel})");
                const int maxBatch = 64;
                var msgs = new ART1553B.RMSG_STRUCT[maxBatch];
                for (int i = 0; i < maxBatch; i++)
                {
                    var m = new ART1553B.RMSG_STRUCT();
                    m.MsgBlock.Datablk = new ushort[32];
                    msgs[i] = m;
                }

                while (_bmDedicatedRunning && _isConnected && _deviceHandle != IntPtr.Zero)
                {
                    try
                    {
                        int readedMsgCount = 0;
                        int rc = ART1553B.BM_ReadMsg_Newly(_deviceHandle, channel, msgs, ref readedMsgCount, maxBatch);
                        if (rc != ART1553B.ART1553Success)
                        {
                            Debug.WriteLine($"[ART1553BDriver] BM_ReadMsg_Newly 返回错误: {rc} (channel={channel})");
                        }
                        else if (readedMsgCount > 0)
                        {
                            lock (_lockObject)
                            {
                                _bmReceiveCount += (ulong)readedMsgCount;
                            }

                            int count = Math.Min(readedMsgCount, msgs.Length);
                            for (int i = 0; i < count; i++)
                            {
                                var msg = msgs[i];
                                int rtAddr = (msg.MsgBlock.CmdWord1 >> 11) & 0x1F;
                                OnMessageReceived(new MessageReceivedEventArgs
                                {
                                    Channel = channel,
                                    RTAddress = rtAddr,
                                    Message = msg,
                                    MessageType = "BM_Receive",
                                    Timestamp = DateTime.Now
                                });
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[ART1553BDriver] BM dedicated loop exception: {ex.Message}");
                    }
                    Thread.Sleep(10);
                }

                Debug.WriteLine($"[ART1553BDriver] MonitorBMModeLoop exit (channel={channel})");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ART1553BDriver] MonitorBMModeLoop fatal: {ex.Message}");
            }
        }

        #endregion

        #region IDeviceDriver 实现 - 采集控制
        public Task<bool> StartAcquisitionAsync()
        {
            if (!_isConnected)
            {
                Debug.WriteLine($"[ART1553BDriver] 设备未连接");
                return Task.FromResult(false);
            }

            if (_isRunning)
            {
                Debug.WriteLine($"[ART1553BDriver] 设备已在运行");
                return Task.FromResult(true);
            }

            try
            {
                Debug.WriteLine($"[ART1553BDriver] 启动数据采集，模式: {_currentMode}");

                // 根据当前模式启动相应的功能
                switch (_currentMode)
                {
                    case DeviceMode.BC:
                    case DeviceMode.BC_RT_BM:
                        // 启动BC
                        int ret = ART1553B.BC_Start(_deviceHandle, _currentChannel);
                        if (ret != ART1553B.ART1553Success)
                        {
                            Debug.WriteLine($"[ART1553BDriver] 启动BC失败，错误码: {ret}");
                            return Task.FromResult(false);
                        }
                        break;

                    case DeviceMode.RT:
                        // 启动RT
                        ret = ART1553B.RT_Start(_deviceHandle, _currentChannel, true);
                        if (ret != ART1553B.ART1553Success)
                        {
                            Debug.WriteLine($"[ART1553BDriver] 启动RT失败，错误码: {ret}");
                            return Task.FromResult(false);
                        }
                        break;

                    case DeviceMode.BM:
                        // 启动BM
                        ret = ART1553B.BM_Start(_deviceHandle, _currentChannel, true);
                        if (ret != ART1553B.ART1553Success)
                        {
                            Debug.WriteLine($"[ART1553BDriver] 启动BM失败，错误码: {ret}");
                            return Task.FromResult(false);
                        }
                        break;
                }

                _isRunning = true;

                // 启动监控线程
                Task.Run(async () => await StartMonitoringAsync());

                Debug.WriteLine($"[ART1553BDriver] 数据采集启动成功");
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ART1553BDriver] 启动采集失败: {ex.Message}");
                OnErrorOccurred($"启动采集失败: {ex.Message}");
                return Task.FromResult(false);
            }
        }

        public async Task<bool> StopAcquisitionAsync()
        {
            try
            {
                Debug.WriteLine($"[ART1553BDriver] 停止数据采集");

                // 停止监控线程
                await StopMonitoringAsync();

                if (USE_SIMULATION)
                {
                    _isRunning = false;
                    return true;
                }

                // 根据当前模式停止相应的功能
                if (_isRunning)
                {
                    switch (_currentMode)
                    {
                        case DeviceMode.BC:
                        case DeviceMode.BC_RT_BM:
                            // 停止BC
                            int ret = ART1553B.BC_Stop(_deviceHandle, _currentChannel);
                            if (ret != ART1553B.ART1553Success)
                            {
                                Debug.WriteLine($"[ART1553BDriver] 停止BC失败，错误码: {ret}");
                            }
                            break;

                        case DeviceMode.RT:
                            int[] channels;
                            lock (_lockObject)
                            {
                                channels = _rtMonitoringChannels.Count > 0 ? _rtMonitoringChannels.ToArray() : new[] { _currentChannel };
                                _rtMonitoringChannels.Clear();
                            }

                            foreach (var ch in channels)
                            {
                                ret = ART1553B.RT_Start(_deviceHandle, ch, false);
                                if (ret != ART1553B.ART1553Success)
                                {
                                    Debug.WriteLine($"[ART1553BDriver] 停止RT失败，错误码: {ret}");
                                }
                            }
                            break;

                        case DeviceMode.BM:
                            // 停止BM
                            ret = ART1553B.BM_Start(_deviceHandle, _currentChannel, false);
                            if (ret != ART1553B.ART1553Success)
                            {
                                Debug.WriteLine($"[ART1553BDriver] 停止BM失败，错误码: {ret}");
                            }
                            break;
                    }
                }

                _isRunning = false;
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ART1553BDriver] 停止采集失败: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region IDeviceDriver 实现 - 其他方法

        public Task<double> ReadChannelAsync(string channelId)
        {
            if (string.IsNullOrWhiteSpace(channelId))
                return Task.FromResult(0d);

            if (string.Equals(channelId, nameof(BC_SendCount), StringComparison.OrdinalIgnoreCase))
                return Task.FromResult((double)_bcSendCount);
            if (string.Equals(channelId, nameof(RT_ReceiveCount), StringComparison.OrdinalIgnoreCase))
                return Task.FromResult((double)_rtReceiveCount);
            if (string.Equals(channelId, nameof(RT_ErrorCount), StringComparison.OrdinalIgnoreCase))
                return Task.FromResult((double)_rtErrorCount);
            if (string.Equals(channelId, nameof(BM_ReceiveCount), StringComparison.OrdinalIgnoreCase))
                return Task.FromResult((double)_bmReceiveCount);
            if (string.Equals(channelId, nameof(BM_ErrorCount), StringComparison.OrdinalIgnoreCase))
                return Task.FromResult((double)_bmErrorCount);
 
            return Task.FromResult(0d);
        }

        public async Task<Dictionary<string, double>> ReadChannelsBatchAsync(IEnumerable<string> channelIds)
        {
            var dict = new Dictionary<string, double>();
            if (channelIds == null)
                return dict;

            foreach (var id in channelIds)
            {
                dict[id] = await ReadChannelAsync(id);
            }

            return dict;
        }

        public Task<bool> WriteChannelAsync(string channelId, double value)
        {
            return Task.FromResult(false);
        }

        public Task<bool> WriteChannelsBatchAsync(Dictionary<string, double> channelValues)
        {
            return Task.FromResult(false);
        }

        public Task<bool> ConfigureChannelAsync(string channelId, Dictionary<string, object> config)
        {
            return Task.FromResult(false);
        }

        public Task<Dictionary<string, object>> GetStatusAsync()
        {
            var dict = new Dictionary<string, object>
            {
                [nameof(IsConnected)] = _isConnected,
                [nameof(CurrentMode)] = _currentMode.ToString(),
                [nameof(CurrentChannel)] = _currentChannel,
                [nameof(BC_SendCount)] = _bcSendCount,
                [nameof(RT_ReceiveCount)] = _rtReceiveCount,
                [nameof(RT_ErrorCount)] = _rtErrorCount,
                [nameof(BM_ReceiveCount)] = _bmReceiveCount,
                [nameof(BM_ErrorCount)] = _bmErrorCount,
            };

            return Task.FromResult(dict);
        }

        public bool SendRTMessage(int rtAddress, int subAddress, ushort[] data)
        {
            if (!_isConnected || (_currentMode != DeviceMode.RT && _currentMode != DeviceMode.BC_RT_BM))
            {
                Debug.WriteLine($"[ART1553BDriver] 设备未连接或未配置RT模式");
                return false;
            }

            try
            {
                int dataLength = data?.Length ?? 0;
                if (dataLength > 32) dataLength = 32;

                int ret = ART1553B.RT_SendMsg(_deviceHandle, _currentChannel, rtAddress, subAddress, (uint)dataLength, data);
                if (ret != ART1553B.ART1553Success)
                {
                    Debug.WriteLine($"[ART1553BDriver] 发送RT消息失败，错误码: {ret}");
                    return false;
                }

                Debug.WriteLine($"[ART1553BDriver] RT消息发送成功，RT地址: {rtAddress}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ART1553BDriver] 发送RT消息失败: {ex.Message}");
                OnErrorOccurred($"发送RT消息失败: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> ResetAsync()
        {
            Debug.WriteLine($"[ART1553BDriver] 重置设备");

            if (!_isConnected)
            {
                Debug.WriteLine($"[ART1553BDriver] 设备未连接");
                return false;
            }

            try
            {
                // 停止监控
                await StopMonitoringAsync();

                // 停止采集
                if (_isRunning)
                {
                    await StopAcquisitionAsync();
                }

                if (USE_SIMULATION)
                {
                    Debug.WriteLine($"[ART1553BDriver] 【模拟】设备重置完成");
                    return true;
                }

                // 复位设备
                int ret = ART1553B.ART1553B_Reset(_deviceHandle);
                if (ret != ART1553B.ART1553Success)
                {
                    Debug.WriteLine($"[ART1553BDriver] 设备复位失败，错误码: {ret}");
                    return false;
                }

                // 复位通道
                ret = ART1553B.ART1553B_ChannelReset(_deviceHandle, _currentChannel);
                if (ret != ART1553B.ART1553Success)
                {
                    Debug.WriteLine($"[ART1553BDriver] 通道复位失败，错误码: {ret}");
                    return false;
                }

                // 重置统计数据
                lock (_lockObject)
                {
                    _bcSendCount = 0;
                    _rtReceiveCount = 0;
                    _rtErrorCount = 0;
                    _bmReceiveCount = 0;
                    _bmErrorCount = 0;

                    _rtMonitoringChannels.Clear();

                    // 清空消息队列
                    foreach (var queue in _messageQueues.Values)
                    {
                        queue.Clear();
                    }
                    _bcMessages.Clear();
                }

                Debug.WriteLine($"[ART1553BDriver] 设备重置完成");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ART1553BDriver] 重置设备失败: {ex.Message}");
                return false;
            }
        }

        public Task<bool> SelfTestAsync()
        {
            Debug.WriteLine($"[ART1553BDriver] 执行自检");

            if (!_isConnected)
            {
                Debug.WriteLine($"[ART1553BDriver] 设备未连接");
                return Task.FromResult(false);
            }

            if (USE_SIMULATION)
            {
                Debug.WriteLine($"[ART1553BDriver] 【模拟】自检通过");
                return Task.FromResult(true);
            }

            try
            {
                // 获取设备版本信息
                ulong firmwareVersion = 0;
                uint driverVersion = 0;
                int ret = ART1553B.ART1553B_GetDevVersion(_deviceHandle, ref firmwareVersion, ref driverVersion);

                if (ret == ART1553B.ART1553Success)
                {
                    Debug.WriteLine($"[ART1553BDriver] 自检通过 - 固件版本: {firmwareVersion}, 驱动版本: {driverVersion}");
                    return Task.FromResult(true);
                }
                else
                {
                    Debug.WriteLine($"[ART1553BDriver] 自检失败，错误码: {ret}");
                    return Task.FromResult(false);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ART1553BDriver] 自检失败: {ex.Message}");
                return Task.FromResult(false);
            }
        }

        #endregion

        #region 辅助方法

        /// <summary>
        /// 获取设备信息
        /// </summary>
        private async Task GetDeviceInfoAsync()
        {
            if (USE_SIMULATION || _deviceHandle == IntPtr.Zero)
                return;

            try
            {
                // 获取序列号
                uint serialNum = 0;
                int ret = ART1553B.ART1553B_GetSerialNum(_deviceHandle, ref serialNum);
                if (ret == ART1553B.ART1553Success)
                {
                    Debug.WriteLine($"[ART1553BDriver] 设备序列号: {serialNum}");
                }

                // 获取物理ID
                uint physicalId = 0;
                ret = ART1553B.ART1553B_GetPhysicalID(_deviceHandle, ref physicalId);
                if (ret == ART1553B.ART1553Success)
                {
                    Debug.WriteLine($"[ART1553BDriver] 设备物理ID: {physicalId}");
                }

                // 获取版本信息
                ulong firmwareVersion = 0;
                uint driverVersion = 0;
                ret = ART1553B.ART1553B_GetDevVersion(_deviceHandle, ref firmwareVersion, ref driverVersion);
                if (ret == ART1553B.ART1553Success)
                {
                    Debug.WriteLine($"[ART1553BDriver] 固件版本: {firmwareVersion}, 驱动版本: {driverVersion}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ART1553BDriver] 获取设备信息失败: {ex.Message}");
            }
        }

        /// <summary>
        /// BC模式发送消息到RT（参考官方例程）
        /// </summary>
        /// <param name="channel">通道号</param>
        /// <param name="messageId">消息ID（0-31）</param>
        /// <param name="rtAddress">RT地址（0-31）</param>
        /// <param name="subAddress">子地址（0-31）</param>
        /// <param name="data">数据（最多32个字）</param>
        /// <param name="channelSelect">通道选择（0:Channel B, 1:Channel A）</param>
        /// <param name="messageGap">消息间隔（单位：1us）</param>
        /// <param name="retryEnable">是否重试</param>
        /// <returns>是否成功</returns>
        public bool SendBCMessageToRT(int channel, ushort messageId, int rtAddress, int subAddress, ushort[] data, int channelSelect = 1, int messageGap = 20, bool retryEnable = false, ushort period = 1000, ushort initPeriod = 0, bool run = true)
        {
            if (!_isConnected || _deviceHandle == IntPtr.Zero)
            {
                Debug.WriteLine($"[ART1553BDriver] 设备未连接");
                return false;
            }

            try
            {
                int dataLength = data?.Length ?? 0;
                if (dataLength > 32) dataLength = 32;
                if (dataLength == 0) dataLength = 32; // 0表示32个字

                // 使用官方 API 生成命令字（更稳健）
                ushort cmdWord = ART1553B.ART1553B_SetCommandWord(rtAddress, 0, subAddress, dataLength);
                Debug.WriteLine($"[ART1553BDriver] BC->RT API 计算命令字: 0x{cmdWord:X4}, RT={rtAddress}, T/R=0, SA={subAddress}, Count={dataLength}");

                // 构造BC->RT消息结构（参考官方例程）
                var msg = new ART1553B.SMSG_STRUCT
                {
                    CtlWord = new ART1553B.CONTROL_WORD_STRUCT
                    {
                        Retry = retryEnable ? (byte)1 : (byte)0,
                        ChanSel = (byte)channelSelect, // 0:Channel B, 1:Channel A
                        MsgFmt = (byte)ART1553B.BC_MSGTYPE_BCRT // BC->RT消息类型
                    },
                    MsgGap = (ushort)messageGap,
                    MsgBlock = new ART1553B.MSG_DESCRIPTOR_STRUCT
                    {
                        // 使用手动计算的命令字（确保T/R=0）
                        CmdWord1 = cmdWord,
                        Datablk = new ushort[32]
                    }
                };
                // 周期与运行控制
                msg.Period = period;
                msg.InitPeriod = initPeriod;
                msg.Run = run ? 1 : 0;

                // 填充数据
                if (data != null && dataLength > 0)
                {
                    for (int i = 0; i < dataLength && i < 32; i++)
                    {
                        msg.MsgBlock.Datablk[i] = data[i];
                    }
                }

                // 写入消息
                ART1553B.SMSG_STRUCT[] msgArray = new ART1553B.SMSG_STRUCT[1];
                msgArray[0] = msg;

                int ret = ART1553B.BC_WriteMsg(_deviceHandle, channel, messageId, msgArray);
                if (ret != ART1553B.ART1553Success)
                {
                    Debug.WriteLine($"[ART1553BDriver] BC写入消息失败，错误码: {ret}");
                    return false;
                }

                Debug.WriteLine($"[ART1553BDriver] BC消息写入成功，消息ID: {messageId}, RT地址: {rtAddress}, 子地址: {subAddress}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ART1553BDriver] BC发送消息失败: {ex.Message}");
                OnErrorOccurred($"BC发送消息失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// BC模式发送RT→BC消息（命令RT发送数据给BC）
        /// </summary>
        /// <param name="channel">通道号</param>
        /// <param name="messageId">消息ID</param>
        /// <param name="rtAddress">RT地址</param>
        /// <param name="subAddress">子地址</param>
        /// <param name="dataLength">期望接收的数据字数</param>
        /// <param name="channelSelect">通道选择（0:Channel B, 1:Channel A）</param>
        /// <param name="messageGap">消息间隔（单位：1us）</param>
        /// <param name="retryEnable">是否重试</param>
        /// <returns>是否成功</returns>
        public bool SendRTToBCMessage(int channel, ushort messageId, int rtAddress, int subAddress, int dataLength, int channelSelect = 1, int messageGap = 20, bool retryEnable = false, ushort period = 1000, ushort initPeriod = 0, bool run = true)
        {
            if (!_isConnected || _deviceHandle == IntPtr.Zero)
            {
                Debug.WriteLine($"[ART1553BDriver] 设备未连接");
                return false;
            }

            try
            {
                if (dataLength <= 0 || dataLength > 32) dataLength = 32;

                // 使用官方 API 生成命令字（T/R=1）
                ushort cmdWord = ART1553B.ART1553B_SetCommandWord(rtAddress, 1, subAddress, dataLength);
                Debug.WriteLine($"[ART1553BDriver] RT->BC API 计算命令字: 0x{cmdWord:X4}, RT={rtAddress}, T/R=1, SA={subAddress}, Count={dataLength}");

                // 构造RT->BC消息结构（T/R=1，发送命令）
                var msg = new ART1553B.SMSG_STRUCT
                {
                    CtlWord = new ART1553B.CONTROL_WORD_STRUCT
                    {
                        Retry = retryEnable ? (byte)1 : (byte)0,
                        ChanSel = (byte)channelSelect,
                        MsgFmt = (byte)ART1553B.BC_MSGTYPE_BCRT  // BC->RT或RT->BC消息类型
                    },
                    MsgGap = (ushort)messageGap,
                    MsgBlock = new ART1553B.MSG_DESCRIPTOR_STRUCT
                    {
                        // 使用手动计算的命令字（确保T/R=1）
                        CmdWord1 = cmdWord,
                        Datablk = new ushort[32]
                    }
                };
                // 周期与运行控制
                msg.Period = period;
                msg.InitPeriod = initPeriod;
                msg.Run = run ? 1 : 0;

                // 写入消息
                ART1553B.SMSG_STRUCT[] msgArray = new ART1553B.SMSG_STRUCT[1];
                msgArray[0] = msg;

                int ret = ART1553B.BC_WriteMsg(_deviceHandle, channel, messageId, msgArray);
                if (ret != ART1553B.ART1553Success)
                {
                    Debug.WriteLine($"[ART1553BDriver] RT->BC消息写入失败，错误码: {ret}");
                    return false;
                }

                Debug.WriteLine($"[ART1553BDriver] RT->BC消息写入成功，消息ID: {messageId}, RT地址: {rtAddress}, 子地址: {subAddress}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ART1553BDriver] RT->BC发送消息失败: {ex.Message}");
                OnErrorOccurred($"RT->BC发送消息失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// BC模式发送RT→RT消息（命令RT1发送数据给RT2）
        /// </summary>
        /// <param name="channel">通道号</param>
        /// <param name="messageId">消息ID</param>
        /// <param name="srcRTAddress">源RT地址（发送方）</param>
        /// <param name="srcSubAddress">源子地址</param>
        /// <param name="dstRTAddress">目标RT地址（接收方）</param>
        /// <param name="dstSubAddress">目标子地址</param>
        /// <param name="dataLength">数据字数</param>
        /// <param name="channelSelect">通道选择（0:Channel B, 1:Channel A）</param>
        /// <param name="messageGap">消息间隔（单位：1us）</param>
        /// <param name="retryEnable">是否重试</param>
        /// <returns>是否成功</returns>
        public bool SendRTToRTMessage(int channel, ushort messageId, int srcRTAddress, int srcSubAddress, int dstRTAddress, int dstSubAddress, int dataLength, int channelSelect = 1, int messageGap = 20, bool retryEnable = false, ushort period = 1000, ushort initPeriod = 0, bool run = true)
        {
            if (!_isConnected || _deviceHandle == IntPtr.Zero)
            {
                Debug.WriteLine($"[ART1553BDriver] 设备未连接");
                return false;
            }

            try
            {
                if (dataLength <= 0 || dataLength > 32) dataLength = 32;

                // 构造RT->RT消息结构
                // 根据1553B协议，RT->RT传输需要两个命令字：
                // CmdWord1: 接收命令（目标RT接收数据）：目标RT地址 | T/R=0 | 子地址 | 字数
                // CmdWord2: 发送命令（源RT发送数据）：源RT地址 | T/R=1 | 子地址 | 字数
                
                // 使用官方 API 生成 RT->RT 的两个命令字（接收命令 T/R=0，发送命令 T/R=1）
                ushort cmdWord1 = ART1553B.ART1553B_SetCommandWord(dstRTAddress, 0, dstSubAddress, dataLength);
                ushort cmdWord2 = ART1553B.ART1553B_SetCommandWord(srcRTAddress, 1, srcSubAddress, dataLength);
                Debug.WriteLine($"[ART1553BDriver] RT->RT API 计算命令字1(接收): 0x{cmdWord1:X4}, 目标RT={dstRTAddress}, T/R=0, SA={dstSubAddress}");
                Debug.WriteLine($"[ART1553BDriver] RT->RT API 计算命令字2(发送): 0x{cmdWord2:X4}, 源RT={srcRTAddress}, T/R=1, SA={srcSubAddress}");
                
                var msg = new ART1553B.SMSG_STRUCT
                {
                    CtlWord = new ART1553B.CONTROL_WORD_STRUCT
                    {
                        Retry = retryEnable ? (byte)1 : (byte)0,
                        ChanSel = (byte)channelSelect,
                        MsgFmt = (byte)ART1553B.BC_MSGTYPE_RTRT  // RT->RT消息类型
                    },
                    MsgGap = (ushort)messageGap,
                    MsgBlock = new ART1553B.MSG_DESCRIPTOR_STRUCT
                    {
                        // 命令字1：接收命令（目标RT，T/R=0）
                        CmdWord1 = cmdWord1,
                        // 命令字2：发送命令（源RT，T/R=1）
                        CmdWord2 = cmdWord2,
                        Datablk = new ushort[32]
                    }
                };
                // 周期与运行控制
                msg.Period = period;
                msg.InitPeriod = initPeriod;
                msg.Run = run ? 1 : 0;

                // 写入消息
                ART1553B.SMSG_STRUCT[] msgArray = new ART1553B.SMSG_STRUCT[1];
                msgArray[0] = msg;

                int ret = ART1553B.BC_WriteMsg(_deviceHandle, channel, messageId, msgArray);
                if (ret != ART1553B.ART1553Success)
                {
                    Debug.WriteLine($"[ART1553BDriver] RT->RT消息写入失败，错误码: {ret}");
                    return false;
                }

                Debug.WriteLine($"[ART1553BDriver] RT->RT消息写入成功，消息ID: {messageId}, 源RT{srcRTAddress}->目标RT{dstRTAddress}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ART1553BDriver] RT->RT发送消息失败: {ex.Message}");
                OnErrorOccurred($"RT->RT发送消息失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// BC模式启动并等待消息完成（参考官方例程）
        /// </summary>
        /// <param name="channel">通道号</param>
        /// <param name="timeoutMs">超时时间（毫秒）</param>
        /// <returns>是否成功</returns>
        public bool BCStartAndWait(int channel, int timeoutMs = 5000)
        {
            if (!_isConnected || _deviceHandle == IntPtr.Zero)
            {
                Debug.WriteLine($"[ART1553BDriver] 设备未连接");
                return false;
            }

            try
            {
                // 启动BC
                int ret = ART1553B.BC_Start(_deviceHandle, channel);
                if (ret != ART1553B.ART1553Success)
                {
                    Debug.WriteLine($"[ART1553BDriver] BC启动失败，错误码: {ret}");
                    return false;
                }

                Debug.WriteLine($"[ART1553BDriver] BC已启动，等待消息完成...");

                // 等待消息完成
                int startTime = Environment.TickCount;
                while (Environment.TickCount - startTime < timeoutMs)
                {
                    ret = ART1553B.BC_IsMsgOver(_deviceHandle, channel);
                    if (ret == ART1553B.ART1553Success)
                    {
                        Debug.WriteLine($"[ART1553BDriver] BC消息执行完成");
                        return true;
                    }
                    Thread.Sleep(10);
                }

                Debug.WriteLine($"[ART1553BDriver] BC消息执行超时");
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ART1553BDriver] BC启动等待失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// BC模式读取RT返回的消息
        /// </summary>
        /// <param name="channel">通道号</param>
        /// <param name="messageId">消息ID</param>
        /// <param name="recvMsg">接收到的消息结构</param>
        /// <returns>是否成功</returns>
        public bool BCReadMessage(int channel, ushort messageId, out ART1553B.RMSG_STRUCT recvMsg)
        {
            recvMsg = new ART1553B.RMSG_STRUCT();
            recvMsg.MsgBlock.Datablk = new ushort[32];

            if (!_isConnected || _deviceHandle == IntPtr.Zero)
            {
                Debug.WriteLine($"[ART1553BDriver] 设备未连接，无法读取BC消息");
                return false;
            }

            try
            {
                int ret = ART1553B.BC_ReadMsg(_deviceHandle, channel, messageId, ref recvMsg);
                if (ret != ART1553B.ART1553Success)
                {
                    Debug.WriteLine($"[ART1553BDriver] BC_ReadMsg 失败，错误码: {ret} (channel={channel}, messageId={messageId})");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ART1553BDriver] BCReadMessage 异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// BC模式停止
        /// </summary>
        /// <param name="channel">通道号</param>
        /// <returns>是否成功</returns>
        public bool BCStop(int channel)
        {
            if (!_isConnected || _deviceHandle == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                int ret = ART1553B.BC_Stop(_deviceHandle, channel);
                if (ret != ART1553B.ART1553Success)
                {
                    Debug.WriteLine($"[ART1553BDriver] BC停止失败，错误码: {ret}");
                    return false;
                }

                Debug.WriteLine($"[ART1553BDriver] BC已停止");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ART1553BDriver] BC停止失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 读取并记录 BC 调度表中指定范围的消息信息（用于诊断）
        /// </summary>
        /// <param name="channel">通道号</param>
        /// <param name="maxMsgIdExclusive">读取的最大 MsgId（排他），通常为写入的消息数</param>
        public void LogBCMsgTable(int channel, int maxMsgIdExclusive)
        {
            if (!_isConnected || _deviceHandle == IntPtr.Zero)
                return;

            try
            {
                int maxMsgCnt = 0;
                int rc = ART1553B.BC_GetMaxMsgCnt(_deviceHandle, channel, ref maxMsgCnt);
                if (rc != ART1553B.ART1553Success)
                {
                    Debug.WriteLine($"[ART1553BDriver] BC_GetMaxMsgCnt failed: {rc}");
                }

                int limit = Math.Min(maxMsgCnt, Math.Max(0, maxMsgIdExclusive));
                Debug.WriteLine($"[ART1553BDriver] Dumping BC MsgTable channel={channel}, limit={limit}");

                for (int i = 0; i < limit; i++)
                {
                    var s = new ART1553B.SMSG_STRUCT();
                    int ret = ART1553B.BC_GetMsgInfo(_deviceHandle, channel, (ushort)i, ref s);
                    if (ret != ART1553B.ART1553Success)
                    {
                        Debug.WriteLine($"[ART1553BDriver] BC_GetMsgInfo failed for MsgId={i}, rc={ret}");
                        continue;
                    }

                    ushort cmd1 = s.MsgBlock.CmdWord1;
                    ushort cmd2 = s.MsgBlock.CmdWord2;
                    int wordCount = cmd1 & 0x1F;
                    if (wordCount == 0) wordCount = 32;
                    var sb = new System.Text.StringBuilder();
                    sb.AppendFormat("[BC MsgInfo] MsgId={0} Period={1} InitPeriod={2} Run={3} MsgGap={4} Cmd1=0x{5:X4} Cmd2=0x{6:X4} Words={7} Data:", i, s.Period, s.InitPeriod, s.Run, s.MsgGap, cmd1, cmd2, wordCount);
                    for (int w = 0; w < Math.Min(wordCount, 32); w++)
                    {
                        sb.AppendFormat(" {0:X4}", s.MsgBlock.Datablk[w]);
                    }
                    Debug.WriteLine(sb.ToString());
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ART1553BDriver] LogBCMsgTable exception: {ex.Message}");
            }
        }

        /// <summary>
        /// 发送模式代码
        /// </summary>
        public bool SendModeCode(int rtAddress, ModeCodeType modeCode, bool withData = false, ushort[] data = null)
        {
            if (!_isConnected)
                return false;

            try
            {
                var msg = new ART1553B.SMSG_STRUCT
                {
                    CtlWord = new ART1553B.CONTROL_WORD_STRUCT
                    {
                        Retry = 0,
                        ChanSel = 1,
                        MsgFmt = withData ? (byte)MessageType.ModeCode : (byte)MessageType.BroadcastMode
                    },
                    MsgGap = 20,
                    MsgBlock = new ART1553B.MSG_DESCRIPTOR_STRUCT
                    {
                        Datablk = new ushort[32]
                    }
                };

                // 构造命令字
                msg.MsgBlock.CmdWord1 = ART1553B.ART1553B_SetCommandWord(
                    rtAddress,
                    withData ? 0 : 1, // 带数据为接收，不带数据为发送
                    31, // 模式代码使用子地址31
                    (int)modeCode
                );

                // 填充数据（如果需要）
                if (withData && data != null)
                {
                    int length = Math.Min(data.Length, 32);
                    for (int i = 0; i < length; i++)
                    {
                        msg.MsgBlock.Datablk[i] = data[i];
                    }
                }

                // 写入消息
                ART1553B.SMSG_STRUCT[] msgArray = new ART1553B.SMSG_STRUCT[1];
                msgArray[0] = msg;

                int ret = ART1553B.BC_WriteMsg(_deviceHandle, _currentChannel, 0, msgArray);
                return ret == ART1553B.ART1553Success;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ART1553BDriver] 发送模式代码失败: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region 事件方法

        protected virtual void OnMessageReceived(MessageReceivedEventArgs e)
        {
            try
            {
                MessageReceived?.Invoke(this, e);
            }
            catch (Exception ex)
            {
                // 捕获并记录订阅者抛出的异常，避免冒泡导致进程崩溃
                System.Diagnostics.Debug.WriteLine($"[ART1553BDriver] OnMessageReceived handler threw: {ex.Message}");
                OnErrorOccurred($"OnMessageReceived handler exception: {ex.Message}");
            }
        }

        protected virtual void OnErrorOccurred(string errorMessage)
        {
            try
            {
                ErrorOccurred?.Invoke(this, new DeviceErrorEventArgs
                {
                    ErrorMessage = errorMessage,
                    Timestamp = DateTime.Now
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ART1553BDriver] ErrorOccurred handler threw: {ex.Message}");
            }
        }

        protected virtual void OnStatusUpdated()
        {
            try
            {
                StatusUpdated?.Invoke(this, new StatusUpdatedEventArgs
                {
                    Status = new DeviceStatus
                    {
                        IsConnected = _isConnected,
                        CurrentMode = _currentMode,
                        CurrentChannel = _currentChannel,
                        IsRunning = _isRunning,
                        BC_SendCount = _bcSendCount,
                        RT_ReceiveCount = _rtReceiveCount,
                        RT_ErrorCount = _rtErrorCount,
                        BM_ReceiveCount = _bmReceiveCount,
                        BM_ErrorCount = _bmErrorCount,
                        MessageQueueSize = _messageQueues.Sum(q => q.Value.Count)
                    },
                    Timestamp = DateTime.Now
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ART1553BDriver] StatusUpdated handler threw: {ex.Message}");
            }
        }

        #endregion

        #region 事件参数类

        public class MessageReceivedEventArgs : EventArgs
        {
            public int Channel { get; set; }
            public int RTAddress { get; set; }
            public ART1553B.RMSG_STRUCT Message { get; set; }
            public string MessageType { get; set; }
            public DateTime Timestamp { get; set; }
        }

        public class DeviceErrorEventArgs : EventArgs
        {
            public string ErrorMessage { get; set; }
            public DateTime Timestamp { get; set; }
        }

        public class StatusUpdatedEventArgs : EventArgs
        {
            public DeviceStatus Status { get; set; }
            public DateTime Timestamp { get; set; }
        }

        #endregion

        #region IDisposable 实现

        private bool _disposed = false;

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // 释放托管资源
                }

                // 释放非托管资源
                if (_isConnected)
                {
                    Task.Run(async () => await DisconnectAsync()).Wait();
                }

                _disposed = true;
            }
        }

        ~ART1553BDriver()
        {
            Dispose(false);
        }

        #endregion
    }
}