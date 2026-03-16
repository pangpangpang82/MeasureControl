//#define USE_SIMULATION // 取消注释启用ART4229驱动的模拟模式（便于无硬件调试）

using MeasureControl.Drivers.ART4229;
using MeasureControl.Models.Devices;
using MeasureControl.Views.Dialogs;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace MeasureControl.Drivers.ART4229
{
    /// <summary>
    /// 阿尔泰科技 ART4229 ARINC 429 通讯卡驱动
    /// 基于 ART4229_64.DLL 实现，提供硬件连接和基本的通道管理功能
    /// </summary>
    public class ART4229Driver : IDeviceDriver
    {
        #region 私有字段

        private readonly DeviceBase _device;
        private readonly int _deviceIndex;
        private bool _isConnected;
        private IntPtr _deviceHandle;
        private ART4229_32.ART4229_MAIN_INFO _deviceInfo;
        private uint _serialNumber;

        // 已打开的发送通道集合
        private readonly HashSet<int> _openedTxChannels = new HashSet<int>();
        // 已打开的接收通道集合
        private HashSet<int> _openedRxChannels = new HashSet<int>();

        private static bool IsSuccess(int ret) => ret == ART4229_32.ART4229_SUCCESS || ret == 0;

        #endregion

        #region 属性

        /// <summary>
        /// 设备ID
        /// </summary>
        public string DeviceId => _device?.Id ?? string.Empty;

        /// <summary>
        /// 设备名称
        /// </summary>
        public string DeviceName => _device?.Name ?? "ART4229";

        /// <summary>
        /// 是否已连接
        /// </summary>
        public bool IsConnected => _isConnected;

        /// <summary>
        /// 是否为模拟驱动
        /// </summary>
        public bool IsSimulated
        {
            get
            {
#if USE_SIMULATION
                return true;
#else
                return false;
#endif
            }
        }

        /// <summary>
        /// 设备索引
        /// </summary>
        public int DeviceIndex => _deviceIndex;

        /// <summary>
        /// 设备句柄
        /// </summary>
        public IntPtr DeviceHandle => _deviceHandle;

        /// <summary>
        /// 设备主要信息
        /// </summary>
        public ART4229_32.ART4229_MAIN_INFO DeviceInfo => _deviceInfo;

        /// <summary>
        /// 设备序列号
        /// </summary>
        public uint SerialNumber => _serialNumber;

        #endregion

        #region 构造函数

        /// <summary>
        /// 创建 ART4229 驱动实例
        /// </summary>
        /// <param name="device">设备实例</param>
        /// <param name="deviceIndex">设备索引（默认0）</param>
        public ART4229Driver(DeviceBase device, int deviceIndex = 0)
        {
            _device = device ?? throw new ArgumentNullException(nameof(device));
            _deviceIndex = deviceIndex;
            _isConnected = false;
            _deviceHandle = IntPtr.Zero;
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
                Debug.WriteLine($"[ART4229Driver] 正在连接设备 {DeviceName}, 设备索引: {_deviceIndex}");

#if USE_SIMULATION
                Debug.WriteLine($"[ART4229Driver] 【模拟模式】跳过硬件初始化，直接连接成功");
                await Task.Delay(100);
                
                _deviceInfo = new ART4229_32.ART4229_MAIN_INFO
                {
                    nDeviceType = 0x20215620,
                    nRevision = 1,
                    nChannelCount = 40,
                    fMainClock = 10000000.0,
                    fMaxRate = 100000.0,
                    fMinRate = 1000.0
                };
                _serialNumber = 0x12345678;
                _isConnected = true;
                return true;
#endif
                // 1. 使用设备索引创建设备句柄
                Debug.WriteLine($"[ART4229Driver] 尝试创建设备（物理ID）: {_deviceIndex}");
                _deviceHandle = ART4229_32.ART4229_DEV_Create(0, 0);     //(uint)_deviceIndex == 0

                if (_deviceHandle == IntPtr.Zero || _deviceHandle == (IntPtr)ART4229_32.INVALID_HANDLE_VALUE)
                {
                    Debug.WriteLine($"[ART4229Driver] 创建设备句柄返回失败，句柄: 0x{_deviceHandle.ToInt64():X}");

                    // 尝试使用序列号方式创建
                    Debug.WriteLine($"[ART4229Driver] 尝试通过序列号方式创建设备...");
                    
                    byte deviceCount = 0;
                    try
                    {
                        var devInfoArray = new ART4229_32.ART4229_DEV_INFO[8];
                        GCHandle handle = GCHandle.Alloc(devInfoArray, GCHandleType.Pinned);
                        try
                        {
                            IntPtr ptr = handle.AddrOfPinnedObject();
                            int result = ART4229_32.ART4229_DeviceList(ptr, (byte)devInfoArray.Length, ref deviceCount);
                            Debug.WriteLine($"[ART4229Driver] 设备枚举返回: result={result}, count={deviceCount}");
                            
                            if (deviceCount > 0 && _deviceIndex < deviceCount)
                            {
                                var targetDev = devInfoArray[_deviceIndex];
                                Debug.WriteLine($"[ART4229Driver] 目标设备信息: 序列号=0x{targetDev.nSerialCode:X}, 类型=0x{targetDev.nDeviceType:X}, 已使用={targetDev.bUsed}");
                                
                                if (targetDev.bUsed == 0)
                                {
                                    Debug.WriteLine($"[ART4229Driver] 尝试使用序列号 0x{targetDev.nSerialCode:X} 创建设备...");
                                    _deviceHandle = ART4229_32.ART4229_DEV_Create(targetDev.nSerialCode, 1);
                                }
                            }
                        }
                        finally
                        {
                            handle.Free();
                        }
                    }
                    catch (Exception enumEx)
                    {
                        Debug.WriteLine($"[ART4229Driver] 设备枚举异常: {enumEx.Message}");
                    }

                    if (_deviceHandle == IntPtr.Zero || _deviceHandle == (IntPtr)ART4229_32.INVALID_HANDLE_VALUE)
                    {
                        try
                        {
                            string msg = $"创建设备失败，未获得有效句柄（设备索引: {_deviceIndex}）。\n\n建议检查：\n- 设备是否已正确连接\n- ART4229 驱动/DLL 是否已安装且位数匹配 (x64)\n- 确认设备已上电\n- 检查设备管理器中是否有未知设备";
                            ReMessageBox.Show(msg, "设备创建失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                        }
                        catch { }
                        return false;
                    }
                }

                Debug.WriteLine($"[ART4229Driver] 设备创建成功，句柄: 0x{_deviceHandle.ToInt64():X}");

                // 2. 获取设备序列号
                try
                {
                    uint serialNum = 0;
                    int result = ART4229_32.ART4229_DEV_GetSerialNumber(_deviceHandle, ref serialNum);
                    if (IsSuccess(result))
                    {
                        _serialNumber = serialNum;
                        Debug.WriteLine($"[ART4229Driver] 设备序列号: 0x{_serialNumber:X}");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ART4229Driver] 获取序列号异常: {ex.Message}");
                }

                // 3. 获取设备主要信息
                try
                {
                    int result = ART4229_32.ART4229_DEV_GetMainInfo(_deviceHandle, ref _deviceInfo);
                    if (IsSuccess(result))
                    {
                        Debug.WriteLine($"[ART4229Driver] 设备信息: 类型=0x{_deviceInfo.nDeviceType:X}, 通道数={_deviceInfo.nChannelCount}, " +
                            $"主时钟={_deviceInfo.fMainClock}Hz, 最大速率={_deviceInfo.fMaxRate}bps, 最小速率={_deviceInfo.fMinRate}bps");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ART4229Driver] 获取设备信息异常: {ex.Message}");
                }

                await Task.Delay(100);
                _isConnected = true;
                Debug.WriteLine($"[ART4229Driver] 设备连接成功");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ART4229Driver] 连接失败: {ex.Message}");
                _isConnected = false;

                if (_deviceHandle != IntPtr.Zero)
                {
                    try
                    {
                        ART4229_32.ART4229_DEV_Release(_deviceHandle);
                    }
                    catch { }
                    _deviceHandle = IntPtr.Zero;
                }

                try
                {
                    string msg = $"连接设备失败：{ex.Message}\n\n建议检查：\n- 设备是否已正确连接\n- ART4229 驱动/DLL 是否已安装且位数匹配 (x64)\n- 确认设备已上电\n\n详细信息请查看输出窗口的日志。";
                    MessageBox.Show(msg, "设备连接失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                catch { }

                return false;
            }
        }

        /// <summary>
        /// 等待接收中断（仅在RX通道启用中断模式时有意义）
        /// </summary>
        public Task<bool> WaitForRxInterruptAsync(int channelIndex, double timeout = 10)
        {
            try
            {
                if (!_isConnected || _deviceHandle == IntPtr.Zero)
                    return Task.FromResult(false);

                int result = ART4229_32.ART4229_RX_WaitForInterrupt(_deviceHandle, channelIndex, timeout);
                // 本工程统一：SUCCESS=1, FAIL=0
                return Task.FromResult(IsSuccess(result));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ART4229Driver] WaitForRxInterrupt异常: {ex.Message}");
                return Task.FromResult(false);
            }
        }

        /// <summary>
        /// 断开连接
        /// </summary>
        public async Task<bool> DisconnectAsync()
        {
            try
            {
                Debug.WriteLine($"[ART4229Driver] 正在断开设备 {DeviceName}");

#if USE_SIMULATION
                _isConnected = false;
                Debug.WriteLine($"[ART4229Driver] 【模拟】设备断开成功");
                return true;
#endif
                // 关闭所有已打开的发送通道
                foreach (var channelIndex in _openedTxChannels.ToList())
                {
                    try
                    {
                        ART4229_32.ART4229_TX_Stop(_deviceHandle, channelIndex);
                        ART4229_32.ART4229_TX_CloseChannel(_deviceHandle, channelIndex);
                        Debug.WriteLine($"[ART4229Driver] 发送通道 {channelIndex} 已关闭");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[ART4229Driver] 关闭发送通道 {channelIndex} 时出错: {ex.Message}");
                    }
                }
                _openedTxChannels.Clear();

                // 关闭所有已打开的接收通道
                foreach (var channelIndex in _openedRxChannels.ToList())
                {
                    try
                    {
                        ART4229_32.ART4229_RX_Stop(_deviceHandle, channelIndex);
                        ART4229_32.ART4229_RX_CloseChannel(_deviceHandle, channelIndex);
                        Debug.WriteLine($"[ART4229Driver] 接收通道 {channelIndex} 已关闭");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[ART4229Driver] 关闭接收通道 {channelIndex} 时出错: {ex.Message}");
                    }
                }
                _openedRxChannels.Clear();

                // 释放设备
                if (_deviceHandle != IntPtr.Zero)
                {
                    int result = ART4229_32.ART4229_DEV_Release(_deviceHandle);
                    if (IsSuccess(result))
                    {
                        Debug.WriteLine($"[ART4229Driver] 设备释放成功");
                    }
                }
                _deviceHandle = IntPtr.Zero;

                await Task.Delay(50);
                _isConnected = false;
                Debug.WriteLine($"[ART4229Driver] 设备断开成功");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ART4229Driver] 断开失败: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region 通道操作方法

        /// <summary>
        /// 打开发送通道
        /// </summary>
        public async Task<bool> OpenTxChannelAsync(int channelIndex)
        {
            try
            {
                if (!_isConnected || _deviceHandle == IntPtr.Zero)
                {
                    Debug.WriteLine($"[ART4229Driver] 设备未连接，无法打开发送通道");
                    return false;
                }

                if (channelIndex < 0 || channelIndex >= _deviceInfo.nChannelCount)
                {
                    Debug.WriteLine($"[ART4229Driver] 发送通道号 {channelIndex} 超出范围");
                    return false;
                }

                if (_openedTxChannels.Contains(channelIndex))
                {
                    try
                    {
                        ART4229_32.ART4229_TX_Stop(_deviceHandle, channelIndex);
                        ART4229_32.ART4229_TX_CloseChannel(_deviceHandle, channelIndex);
                    }
                    catch { }
                    _openedTxChannels.Remove(channelIndex);
                }

                int result = ART4229_32.ART4229_TX_OpenChannel(_deviceHandle, channelIndex);
                if (IsSuccess(result))
                {
                    _openedTxChannels.Add(channelIndex);
                    Debug.WriteLine($"[ART4229Driver] 发送通道 {channelIndex} 打开成功");
                    return true;
                }
                else
                {
                    Debug.WriteLine($"[ART4229Driver] 发送通道 {channelIndex} 打开失败，返回码: {result}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ART4229Driver] 打开发送通道异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 关闭发送通道
        /// </summary>
        public async Task<bool> CloseTxChannelAsync(int channelIndex)
        {
            try
            {
                if (!_isConnected || _deviceHandle == IntPtr.Zero)
                {
                    return false;
                }

                if (!_openedTxChannels.Contains(channelIndex))
                {
                    return true;
                }

                try { ART4229_32.ART4229_TX_Stop(_deviceHandle, channelIndex); } catch { }
                
                int result = ART4229_32.ART4229_TX_CloseChannel(_deviceHandle, channelIndex);
                if (IsSuccess(result))
                {
                    _openedTxChannels.Remove(channelIndex);
                    Debug.WriteLine($"[ART4229Driver] 发送通道 {channelIndex} 已关闭");
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ART4229Driver] 关闭发送通道异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 打开接收通道
        /// </summary>
        public async Task<bool> OpenRxChannelAsync(int channelIndex)
        {
            try
            {
                if (!_isConnected || _deviceHandle == IntPtr.Zero)
                {
                    Debug.WriteLine($"[ART4229Driver] 设备未连接，无法打开接收通道");
                    return false;
                }

                if (channelIndex < 0 || channelIndex >= _deviceInfo.nChannelCount)
                {
                    Debug.WriteLine($"[ART4229Driver] 接收通道号 {channelIndex} 超出范围");
                    return false;
                }

                if (_openedRxChannels.Contains(channelIndex))
                {
                    try
                    {
                        ART4229_32.ART4229_RX_Stop(_deviceHandle, channelIndex);
                        ART4229_32.ART4229_RX_CloseChannel(_deviceHandle, channelIndex);
                    }
                    catch { }
                    _openedRxChannels.Remove(channelIndex);
                }

                int result = ART4229_32.ART4229_RX_OpenChannel(_deviceHandle, channelIndex);
                if (IsSuccess(result))
                {
                    _openedRxChannels.Add(channelIndex);
                    Debug.WriteLine($"[ART4229Driver] 接收通道 {channelIndex} 打开成功");
                    return true;
                }
                else
                {
                    Debug.WriteLine($"[ART4229Driver] 接收通道 {channelIndex} 打开失败，返回码: {result}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ART4229Driver] 打开接收通道异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 关闭接收通道
        /// </summary>
        public async Task<bool> CloseRxChannelAsync(int channelIndex)
        {
            try
            {
                if (!_isConnected || _deviceHandle == IntPtr.Zero)
                {
                    return false;
                }

                if (!_openedRxChannels.Contains(channelIndex))
                {
                    return true;
                }

                try { ART4229_32.ART4229_RX_Stop(_deviceHandle, channelIndex); } catch { }
                
                int result = ART4229_32.ART4229_RX_CloseChannel(_deviceHandle, channelIndex);
                if (IsSuccess(result))
                {
                    _openedRxChannels.Remove(channelIndex);
                    Debug.WriteLine($"[ART4229Driver] 接收通道 {channelIndex} 已关闭");
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ART4229Driver] 关闭接收通道异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 切换通道方向（TX 和 RX 互换）
        /// 步骤：清空FIFO，关闭当前方向通道，等待10ms，打开新方向通道
        /// </summary>
        /// <param name="channelIndex">通道索引</param>
        /// <param name="currentIsTx">当前是否为发送通道</param>
        /// <returns>切换是否成功</returns>
        public async Task<bool> SwitchChannelDirectionAsync(int channelIndex, bool currentIsTx)
        {
            try
            {
                if (!_isConnected || _deviceHandle == IntPtr.Zero)
                {
                    Debug.WriteLine($"[ART4229Driver] 设备未连接，无法切换通道方向");
                    return false;
                }

                Debug.WriteLine($"[ART4229Driver] 开始切换通道 {channelIndex} 方向: {(currentIsTx ? "TX->RX" : "RX->TX")}");

                int result;

                if (currentIsTx)
                {
                    // 当前是TX，切换到RX
                    // 1. 停止发送
                    try { ART4229_32.ART4229_TX_Stop(_deviceHandle, channelIndex); } catch { }

                    // 2. 清空发送缓冲区
                    result = ART4229_32.ART4229_TX_ResetFIFO(_deviceHandle, channelIndex);
                    Debug.WriteLine($"[ART4229Driver] 清空发送FIFO结果: {result}");

                    // 3. 关闭发送通道
                    result = ART4229_32.ART4229_TX_CloseChannel(_deviceHandle, channelIndex);
                    Debug.WriteLine($"[ART4229Driver] 关闭发送通道结果: {result}");
                    _openedTxChannels.Remove(channelIndex);

                    // 4. 等待10ms确保硬件完全释放
                    await Task.Delay(10);

                    // 5. 打开接收通道
                    result = ART4229_32.ART4229_RX_OpenChannel(_deviceHandle, channelIndex);
                    if (IsSuccess(result))
                    {
                        _openedRxChannels.Add(channelIndex);
                        Debug.WriteLine($"[ART4229Driver] 通道 {channelIndex} 已切换为接收通道");
                    }
                    return true;
                }
                else
                {
                    // 当前是RX，切换到TX
                    // 1. 停止接收
                    try { ART4229_32.ART4229_RX_Stop(_deviceHandle, channelIndex); } catch { }

                    // 2. 清空接收缓冲区
                    result = ART4229_32.ART4229_RX_ResetFIFO(_deviceHandle, channelIndex);
                    Debug.WriteLine($"[ART4229Driver] 清空接收FIFO结果: {result}");

                    // 3. 关闭接收通道
                    result = ART4229_32.ART4229_RX_CloseChannel(_deviceHandle, channelIndex);
                    Debug.WriteLine($"[ART4229Driver] 关闭接收通道结果: {result}");
                    _openedRxChannels.Remove(channelIndex);

                    // 4. 等待10ms确保硬件完全释放
                    await Task.Delay(10);

                    // 5. 打开发送通道
                    result = ART4229_32.ART4229_TX_OpenChannel(_deviceHandle, channelIndex);
                    if (IsSuccess(result))
                    {
                        _openedTxChannels.Add(channelIndex);
                        Debug.WriteLine($"[ART4229Driver] 通道 {channelIndex} 已切换为发送通道");
                    }
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ART4229Driver] 切换通道方向异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 配置发送通道参数
        /// </summary>
        /// <param name="channelIndex">通道索引</param>
        /// <param name="rate">码率</param>
        /// <param name="sendMode">发送模式：0=Single, 1=Period</param>
        /// <param name="parity">校验：0=None, 1=ODD, 2=EVEN</param>
        /// <param name="wordFormat">字格式：0=FORMAT1, 1=FORMAT2(标准429)</param>
        /// <returns>配置是否成功</returns>
        public async Task<bool> ConfigureTxChannelAsync(int channelIndex, double rate, int sendMode, int parity, int wordFormat)
        {
            try
            {
                if (!_isConnected || _deviceHandle == IntPtr.Zero)
                {
                    Debug.WriteLine($"[ART4229Driver] 设备未连接，无法配置发送通道");
                    return false;
                }

                Debug.WriteLine($"[ART4229Driver] 配置发送通道 {channelIndex}: 码率={rate}, 模式={sendMode}, 校验={parity}");
                // 避免残留：停止并清空FIFO
                ART4229_32.ART4229_TX_Stop(_deviceHandle, channelIndex);
                ART4229_32.ART4229_TX_ResetFIFO(_deviceHandle, channelIndex);

                // 设置字格式
                // 注意：界面/VM中 WordFormat=0 表示“标准429”，对应底层 WORD_FORMAT2
                int formatResult = ART4229_32.ART4229_Channel_SetWordFormat(_deviceHandle, channelIndex,
                    (uint)(wordFormat == 0 ? ART4229_32.ART4229_WORD_FORMAT2 : ART4229_32.ART4229_WORD_FORMAT1));
                Debug.WriteLine($"[ART4229Driver] 设置字格式结果: {formatResult}");

                // 配置发送通道参数
                ART4229_32.ART4229_TX_CH_PARAM txParam = new ART4229_32.ART4229_TX_CH_PARAM
                {
                    nChannel = channelIndex,
                    nDataLength = (uint)ART4229_32.ART4229_DATALEN_32BITS,
                    fTranRate = rate,
                    nSendMode = (uint)(sendMode == 0 ? ART4229_32.ART4229_TX_MODE_SINGLE : ART4229_32.ART4229_TX_MODE_PERIOD)
                };

                int result = ART4229_32.ART4229_TX_InitChannel(_deviceHandle, channelIndex, ref txParam);
                if (IsSuccess(result))
                {
                    Debug.WriteLine($"[ART4229Driver] 发送通道 {channelIndex} 配置成功");
                    return true;
                }
                else
                {
                    Debug.WriteLine($"[ART4229Driver] 发送通道 {channelIndex} 配置失败，返回码: {result}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ART4229Driver] 配置发送通道异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 配置接收通道参数
        /// </summary>
        /// <param name="channelIndex">通道索引</param>
        /// <param name="rate">码率（0表示自适应）</param>
        /// <param name="parity">校验：0=None, 1=ODD, 2=EVEN</param>
        /// <param name="wordFormat">字格式：0=FORMAT1, 1=FORMAT2(标准429)</param>
        /// <param name="enableInterrupt">是否启用中断</param>
        /// <param name="interruptDepth">中断深度</param>
        /// <param name="enableTimeTag">是否启用时标</param>
        /// <returns>配置是否成功</returns>
        public async Task<bool> ConfigureRxChannelAsync(int channelIndex, double rate, int parity, int wordFormat, 
            bool enableInterrupt, int interruptDepth, bool enableTimeTag)
        {
            try
            {
                if (!_isConnected || _deviceHandle == IntPtr.Zero)
                {
                    Debug.WriteLine($"[ART4229Driver] 设备未连接，无法配置接收通道");
                    return false;
                }

                Debug.WriteLine($"[ART4229Driver] 配置接收通道 {channelIndex}: 码率={rate}, 校验={parity}, 中断={enableInterrupt}, 时标={enableTimeTag}");
                // 避免残留：停止并清空FIFO
                ART4229_32.ART4229_RX_Stop(_deviceHandle, channelIndex);
                ART4229_32.ART4229_RX_ResetFIFO(_deviceHandle, channelIndex);

                // 设置字格式
                // 注意：界面/VM中 WordFormat=0 表示“标准429”，对应底层 WORD_FORMAT2
                int formatResult = ART4229_32.ART4229_Channel_SetWordFormat(_deviceHandle, channelIndex,
                    (uint)(wordFormat == 0 ? ART4229_32.ART4229_WORD_FORMAT2 : ART4229_32.ART4229_WORD_FORMAT1));
                Debug.WriteLine($"[ART4229Driver] 设置字格式结果: {formatResult}");

                // 配置接收通道参数
                ART4229_32.ART4229_RX_CH_PARAM rxParam = new ART4229_32.ART4229_RX_CH_PARAM
                {
                    nChannel = channelIndex,
                    nDataLength = (uint)ART4229_32.ART4229_DATALEN_32BITS,
                    nParity = (uint)(parity == 0 ? ART4229_32.ART4229_PARITY_NONE :
                              (parity == 1 ? ART4229_32.ART4229_PARITY_ODD : ART4229_32.ART4229_PARITY_EVEN)),
                    bInterrupt = (uint)(enableInterrupt ? ART4229_32.ART4229_RX_INTERRUPT_OPEN : ART4229_32.ART4229_RX_INTERRUPT_CLOSE),
                    nInterruptDepth = (uint)interruptDepth,
                    bRateAdaption = (uint)(rate == 0 ? ART4229_32.ART4229_RX_RATE_SELFADAPTION : ART4229_32.ART4229_RX_RATE_FIXED),
                    fRecvRate = rate
                };

                int result = ART4229_32.ART4229_RX_InitChannel(_deviceHandle, channelIndex, ref rxParam);
                if (IsSuccess(result))
                {
                    // 对齐厂家例程：默认关闭过滤（不过滤）
                    try
                    {
                        int filterResult = ART4229_32.ART4229_RX_EnableFilter(_deviceHandle, channelIndex, 0);
                        Debug.WriteLine($"[ART4229Driver] RX_EnableFilter(0) result: {filterResult}");
                    }
                    catch { }

                    // 配置时标
                    if (enableTimeTag)
                    {
                        ART4229_32.ART4229_SetTimeTag(_deviceHandle);
                        ART4229_32.ART4229_EnableTimeTag(_deviceHandle, 1);
                    }
                    else
                    {
                        // 显式关闭时标，避免设备端仍在返回“带时标”的数据包导致解析错位
                        ART4229_32.ART4229_EnableTimeTag(_deviceHandle, 0);
                    }

                    Debug.WriteLine($"[ART4229Driver] 接收通道 {channelIndex} 配置成功");
                    return true;
                }
                else
                {
                    Debug.WriteLine($"[ART4229Driver] 接收通道 {channelIndex} 配置失败，返回码: {result}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ART4229Driver] 配置接收通道异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 发送数据（Single模式）- 一次性发送所有数据
        /// </summary>
        public async Task<bool> SendDataSingleAsync(int channelIndex, uint[] data429, uint[] parity)
        {
            try
            {
                if (!_isConnected || _deviceHandle == IntPtr.Zero)
                {
                    Debug.WriteLine($"[ART4229Driver] 设备未连接，无法发送数据");
                    return false;
                }

                if (data429 == null || data429.Length == 0)
                {
                    Debug.WriteLine($"[ART4229Driver] Single发送失败：数据为空");
                    return false;
                }

                if (parity == null || parity.Length != data429.Length)
                {
                    Debug.WriteLine($"[ART4229Driver] Single发送失败：Parity数组长度不匹配 dataLen={data429.Length}, parityLen={(parity == null ? -1 : parity.Length)}");
                    return false;
                }

                uint dataCount = (uint)data429.Length;
                uint realWrited = 0;

                // 确认通道已打开（避免上层状态不同步导致写入失败）
                if (!_openedTxChannels.Contains(channelIndex))
                {
                    int openResult = ART4229_32.ART4229_TX_OpenChannel(_deviceHandle, channelIndex);
                    Debug.WriteLine($"[ART4229Driver] Single发送前自动打开TX通道 {channelIndex} 结果: {openResult}");
                    if (!IsSuccess(openResult))
                    {
                        return false;
                    }
                    _openedTxChannels.Add(channelIndex);
                }

                int txStatus = 0;
                try
                {
                    int statusResult = ART4229_32.ART4229_TX_GetStatus(_deviceHandle, channelIndex, ref txStatus);
                    Debug.WriteLine($"[ART4229Driver] Single发送前TX状态: result={statusResult}, status={txStatus} (0:空闲,1:发送中,2:发送完成)");
                }
                catch { }

                if (txStatus == 1)
                {
                    int stopResult = ART4229_32.ART4229_TX_Stop(_deviceHandle, channelIndex);
                    Debug.WriteLine($"[ART4229Driver] Single停止发送结果: {stopResult}");
                }

                // 清空发送FIFO
                int resetFifoResult = ART4229_32.ART4229_TX_ResetFIFO(_deviceHandle, channelIndex);
                Debug.WriteLine($"[ART4229Driver] Single复位FIFO结果: {resetFifoResult}");
                if (!IsSuccess(resetFifoResult))
                {
                    return false;
                }

                // 等待缓冲区有空间（厂家例程会先查空间再写）
                int surplus = 0;
                for (int i = 0; i < 20; i++)
                {
                    int spaceResult = ART4229_32.ART4229_TX_GetBufSurplusSpace(_deviceHandle, channelIndex, ref surplus);
                    Debug.WriteLine($"[ART4229Driver] Single剩余空间查询: result={spaceResult}, surplus={surplus}");
                    if (!IsSuccess(spaceResult))
                    {
                        return false;
                    }
                    if (surplus > 0)
                    {
                        break;
                    }
                    await Task.Delay(10);
                }

                if (surplus <= 0)
                {
                    Debug.WriteLine($"[ART4229Driver] Single写入前无可用空间，surplus={surplus}");
                    return false;
                }

                // 写入数据（Single模式：period/count/interval为null）
                int result = ART4229_32.ART4229_TX_WriteData(_deviceHandle, channelIndex, 
                    data429, null, null, null, parity, dataCount, ref realWrited);

                Debug.WriteLine($"[ART4229Driver] Single写入返回码: {result:X4}, realWrited={realWrited}/{dataCount}");

                bool writeOk = IsSuccess(result);

                // 失败场景：返回码不成功，或实际写入为0（你当前遇到的是 realWrited=0/3）
                if (realWrited == 0 || !writeOk)
                {
                    Debug.WriteLine($"[ART4229Driver] Single首次写入未成功，准备重试：ret={result:X4}, realWrited={realWrited}");

                    // 1) 某些驱动对NULL不兼容：用默认数组重试
                    realWrited = 0;
                    uint[] period = new uint[data429.Length];
                    uint[] count = new uint[data429.Length];
                    uint[] interval = new uint[data429.Length];

                    for (int i = 0; i < data429.Length; i++)
                    {
                        period[i] = 0;
                        count[i] = 1;
                        interval[i] = 4;
                    }

                    result = ART4229_32.ART4229_TX_WriteData(_deviceHandle, channelIndex,
                        data429, period, count, interval, parity, dataCount, ref realWrited);
                    Debug.WriteLine($"[ART4229Driver] Single二次写入返回码: {result:X4}, realWrited={realWrited}/{dataCount}");
                    writeOk = IsSuccess(result);

                    // 2) 若仍未写入：尝试复位通道再写一次
                    if (realWrited == 0)
                    {
                        try
                        {
                            int resetChResult = ART4229_32.ART4229_TX_ResetChannel(_deviceHandle, channelIndex);
                            Debug.WriteLine($"[ART4229Driver] Single复位TX通道结果: {resetChResult}");
                        }
                        catch (EntryPointNotFoundException ex)
                        {
                            Debug.WriteLine($"[ART4229Driver] Single复位TX通道接口不存在: {ex.Message}");
                        }
                        int resetAgain = ART4229_32.ART4229_TX_ResetFIFO(_deviceHandle, channelIndex);
                        Debug.WriteLine($"[ART4229Driver] Single复位FIFO(二次)结果: {resetAgain}");

                        int surplus2 = 0;
                        int spaceResult2 = ART4229_32.ART4229_TX_GetBufSurplusSpace(_deviceHandle, channelIndex, ref surplus2);
                        Debug.WriteLine($"[ART4229Driver] Single重试前剩余空间: result={spaceResult2}, surplus={surplus2}");

                        realWrited = 0;
                        result = ART4229_32.ART4229_TX_WriteData(_deviceHandle, channelIndex,
                            data429, period, count, interval, parity, dataCount, ref realWrited);
                        Debug.WriteLine($"[ART4229Driver] Single三次写入返回码: {result:X4}, realWrited={realWrited}/{dataCount}");
                        writeOk = IsSuccess(result);
                    }
                }

                // 兼容：成功码可能为0或1；最终以 realWrited>0 为准
                if (realWrited == 0)
                {
                    Debug.WriteLine($"[ART4229Driver] Single写入失败：realWrited=0 (返回码: {result:X4})");
                    return false;
                }

                // 启动发送（Single模式也需要Start）
                int startResult = ART4229_32.ART4229_TX_Start(_deviceHandle, channelIndex);
                bool startOk = IsSuccess(startResult);

                // 某些DLL版本成功码为0；为避免误判，Start后复核一次TX状态
                int txStatusAfterStart = 0;
                try
                {
                    await Task.Delay(5);
                    int statusResult2 = ART4229_32.ART4229_TX_GetStatus(_deviceHandle, channelIndex, ref txStatusAfterStart);
                    Debug.WriteLine($"[ART4229Driver] Single启动后TX状态: result={statusResult2}, status={txStatusAfterStart} (0:空闲,1:发送中,2:发送完成)");
                }
                catch { }

                if (!startOk && txStatusAfterStart == 0)
                {
                    Debug.WriteLine($"[ART4229Driver] Single启动发送失败，返回码: {startResult:X4}");
                    return false;
                }

                Debug.WriteLine($"[ART4229Driver] Single模式发送启动成功，写入{realWrited}个字");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ART4229Driver] 发送数据异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 发送数据（Period模式）- 周期性发送数据
        /// </summary>
        public async Task<bool> SendDataPeriodAsync(int channelIndex, uint[] data429, uint[] period, 
            uint[] count, uint[] interval, uint[] parity)
        {
            try
            {
                if (!_isConnected || _deviceHandle == IntPtr.Zero)
                {
                    Debug.WriteLine($"[ART4229Driver] 设备未连接，无法发送数据");
                    return false;
                }

                uint dataCount = (uint)data429.Length;
                uint realWrited = 0;

                if (!_openedTxChannels.Contains(channelIndex))
                {
                    int openResult = ART4229_32.ART4229_TX_OpenChannel(_deviceHandle, channelIndex);
                    Debug.WriteLine($"[ART4229Driver] Period发送前自动打开TX通道 {channelIndex} 结果: {openResult}");
                    if (!IsSuccess(openResult))
                    {
                        return false;
                    }
                    _openedTxChannels.Add(channelIndex);
                }

                int txStatus = 0;
                try
                {
                    int statusResult = ART4229_32.ART4229_TX_GetStatus(_deviceHandle, channelIndex, ref txStatus);
                    Debug.WriteLine($"[ART4229Driver] Period发送前TX状态: result={statusResult}, status={txStatus} (0:空闲,1:发送中,2:发送完成)");
                }
                catch { }

                if (txStatus == 1)
                {
                    int stopResult = ART4229_32.ART4229_TX_Stop(_deviceHandle, channelIndex);
                    Debug.WriteLine($"[ART4229Driver] Period停止发送结果: {stopResult}");
                }

                // 清空发送FIFO
                int resetFifoResult = ART4229_32.ART4229_TX_ResetFIFO(_deviceHandle, channelIndex);
                Debug.WriteLine($"[ART4229Driver] Period复位FIFO结果: {resetFifoResult}");
                if (!IsSuccess(resetFifoResult))
                {
                    return false;
                }

                int surplus = 0;
                for (int i = 0; i < 20; i++)
                {
                    int spaceResult = ART4229_32.ART4229_TX_GetBufSurplusSpace(_deviceHandle, channelIndex, ref surplus);
                    Debug.WriteLine($"[ART4229Driver] Period剩余空间查询: result={spaceResult}, surplus={surplus}");
                    if (!IsSuccess(spaceResult))
                    {
                        return false;
                    }
                    if (surplus > 0)
                    {
                        break;
                    }
                    await Task.Delay(10);
                }

                if (surplus <= 0)
                {
                    Debug.WriteLine($"[ART4229Driver] Period写入前无可用空间，surplus={surplus}");
                    return false;
                }

                // 写入数据（Period模式：包含period/count/interval）
                int result = ART4229_32.ART4229_TX_WriteData(_deviceHandle, channelIndex,
                    data429, period, count, interval, parity, dataCount, ref realWrited);

                Debug.WriteLine($"[ART4229Driver] Period写入返回码: {result:X4}, realWrited={realWrited}/{dataCount}");

                bool writeOk = IsSuccess(result);

                // Period失败同样以 realWrited 判断；必要时复位通道并重试一次
                if (realWrited == 0 || !writeOk)
                {
                    Debug.WriteLine($"[ART4229Driver] Period首次写入未成功，准备重试：ret={result:X4}, realWrited={realWrited}");
                    try
                    {
                        int resetChResult = ART4229_32.ART4229_TX_ResetChannel(_deviceHandle, channelIndex);
                        Debug.WriteLine($"[ART4229Driver] Period复位TX通道结果: {resetChResult}");
                    }
                    catch (EntryPointNotFoundException ex)
                    {
                        Debug.WriteLine($"[ART4229Driver] Period复位TX通道接口不存在: {ex.Message}");
                    }
                    int resetFifo2 = ART4229_32.ART4229_TX_ResetFIFO(_deviceHandle, channelIndex);
                    Debug.WriteLine($"[ART4229Driver] Period复位FIFO(二次)结果: {resetFifo2}");

                    realWrited = 0;
                    result = ART4229_32.ART4229_TX_WriteData(_deviceHandle, channelIndex,
                        data429, period, count, interval, parity, dataCount, ref realWrited);
                    Debug.WriteLine($"[ART4229Driver] Period二次写入返回码: {result:X4}, realWrited={realWrited}/{dataCount}");
                    writeOk = IsSuccess(result);
                }

                // 兼容：成功码可能为0或1；最终以 realWrited>0 为准
                if (realWrited == 0)
                {
                    Debug.WriteLine($"[ART4229Driver] Period写入失败：realWrited=0 (返回码: {result:X4})");
                    return false;
                }

                // 启动发送
                result = ART4229_32.ART4229_TX_Start(_deviceHandle, channelIndex);
                bool startOk = IsSuccess(result);

                int txStatusAfterStart = 0;
                try
                {
                    await Task.Delay(5);
                    int statusResult2 = ART4229_32.ART4229_TX_GetStatus(_deviceHandle, channelIndex, ref txStatusAfterStart);
                    Debug.WriteLine($"[ART4229Driver] Period启动后TX状态: result={statusResult2}, status={txStatusAfterStart} (0:空闲,1:发送中,2:发送完成)");
                }
                catch { }

                if (!startOk && txStatusAfterStart == 0)
                {
                    Debug.WriteLine($"[ART4229Driver] 启动周期发送失败，返回码: {result}");
                    return false;
                }

                Debug.WriteLine($"[ART4229Driver] Period模式发送启动成功，写入{realWrited}个字");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ART4229Driver] 周期发送异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 停止发送
        /// </summary>
        public async Task<bool> StopSendAsync(int channelIndex)
        {
            try
            {
                if (!_isConnected || _deviceHandle == IntPtr.Zero)
                    return false;

                int result = ART4229_32.ART4229_TX_Stop(_deviceHandle, channelIndex);
                ART4229_32.ART4229_TX_ResetFIFO(_deviceHandle, channelIndex);
                
                Debug.WriteLine($"[ART4229Driver] 停止发送通道 {channelIndex}");
                return IsSuccess(result);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ART4229Driver] 停止发送异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 启动接收
        /// </summary>
        public async Task<bool> StartReceiveAsync(int channelIndex)
        {
            try
            {
                if (!_isConnected || _deviceHandle == IntPtr.Zero)
                {
                    Debug.WriteLine($"[ART4229Driver] 设备未连接，无法启动接收");
                    return false;
                }

                // 清空接收FIFO
                ART4229_32.ART4229_RX_ResetFIFO(_deviceHandle, channelIndex);

                // 启动接收
                int result = ART4229_32.ART4229_RX_Start(_deviceHandle, channelIndex);
                if (!IsSuccess(result))
                {
                    Debug.WriteLine($"[ART4229Driver] 启动接收失败，返回码: {result}");
                    return false;
                }

                Debug.WriteLine($"[ART4229Driver] 接收通道 {channelIndex} 启动成功");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ART4229Driver] 启动接收异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 停止接收
        /// </summary>
        public async Task<bool> StopReceiveAsync(int channelIndex)
        {
            try
            {
                if (!_isConnected || _deviceHandle == IntPtr.Zero)
                    return false;

                int result = ART4229_32.ART4229_RX_Stop(_deviceHandle, channelIndex);
                Debug.WriteLine($"[ART4229Driver] 停止接收通道 {channelIndex}");
                return IsSuccess(result);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ART4229Driver] 停止接收异常: {ex.Message}");
                return false;
            }
        }

        public async Task<int> GetTxStatusAsync(int channelIndex)
        {
            try
            {
                if (!_isConnected || _deviceHandle == IntPtr.Zero)
                    return -1;

                int status = 0;
                int ret = ART4229_32.ART4229_TX_GetStatus(_deviceHandle, channelIndex, ref status);
                if (!IsSuccess(ret))
                {
                    Debug.WriteLine($"[ART4229Driver] 获取TX状态失败，返回码: {ret:X4}");
                    return -1;
                }

                return status;
            }
            catch
            {
                return -1;
            }
        }

        public async Task<bool> WaitForTxCompleteAsync(int channelIndex, int timeoutMs = 2000)
        {
            try
            {
                var sw = Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < timeoutMs)
                {
                    int status = await GetTxStatusAsync(channelIndex);
                    if (status < 0)
                        return false;

                    if (status == 0 || status == 2)
                        return true;

                    int isComplete = 0;
                    try
                    {
                        int ret = ART4229_32.ART4229_TX_IsComplete(_deviceHandle, channelIndex, ref isComplete);
                        if (IsSuccess(ret) && isComplete == 1)
                            return true;
                    }
                    catch { }

                    await Task.Delay(10);
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 读取接收数据
        /// </summary>
        /// <param name="channelIndex">通道索引</param>
        /// <param name="maxCount">最大读取数量</param>
        /// <param name="enableTimeTag">是否启用时标</param>
        /// <param name="enableRateAdaption">是否启用码率自适应</param>
        /// <returns>接收到的数据列表(每项包含：Data429, Rate, TimeHigh, TimeLow)</returns>
        public async Task<List<(uint Data429, uint Rate, uint TimeHigh, uint TimeLow)>> ReadReceiveDataAsync(
            int channelIndex, uint maxCount = 1024, bool enableTimeTag = true, bool enableRateAdaption = true)
        {
            var result = new List<(uint, uint, uint, uint)>();
            try
            {
                if (!_isConnected || _deviceHandle == IntPtr.Zero)
                    return result;

                // 获取接收数据数量
                uint dataCount = 0;
                int ret = ART4229_32.ART4229_RX_GetCountOfRecvData(_deviceHandle, channelIndex, ref dataCount);
                if (!IsSuccess(ret))
                {
                    Debug.WriteLine($"[ART4229Driver] 获取接收数据数量失败，返回码: {ret:X4}");
                    return result;
                }

                if (dataCount == 0)
                    return result;

                dataCount = Math.Min(dataCount, maxCount);

                // 根据时标和码率自适应计算每个数据包的长度
                int pktLen = 1; // 默认只有数据
                if (enableTimeTag && enableRateAdaption)
                    pktLen = 4; // 码率+时标高+时标低+数据
                else if (enableTimeTag)
                    pktLen = 3; // 时标高+时标低+数据
                else if (enableRateAdaption)
                    pktLen = 2; // 码率+数据

                // 始终按最大包长(4)申请，避免设备端实际返回包长与参数不一致导致越界/错位
                // pRXData 的格式由设备配置决定：
                // 1: rate + timeHigh + timeLow + word
                // 2: timeHigh + timeLow + word
                // 3: rate + word
                // 4: word
                const int maxPktLen = 4;
                uint[] buffer = new uint[dataCount * maxPktLen];
                uint realCount = 0;

                ret = ART4229_32.ART4229_RX_ReadData(_deviceHandle, channelIndex, buffer, dataCount, ref realCount);
                if (!IsSuccess(ret))
                {
                    Debug.WriteLine($"[ART4229Driver] 读取接收数据失败，返回码: {ret:X4}");
                    return result;
                }

                // 解析数据
                for (int i = 0; i < realCount; i++)
                {
                    uint rate = 0, timeHigh = 0, timeLow = 0, data429 = 0;
                    
                    if (pktLen == 4)
                    {
                        rate = buffer[i * 4];
                        timeHigh = buffer[i * 4 + 1];
                        timeLow = buffer[i * 4 + 2];
                        data429 = buffer[i * 4 + 3];
                    }
                    else if (pktLen == 3)
                    {
                        timeHigh = buffer[i * 3];
                        timeLow = buffer[i * 3 + 1];
                        data429 = buffer[i * 3 + 2];
                    }
                    else if (pktLen == 2)
                    {
                        rate = buffer[i * 2];
                        data429 = buffer[i * 2 + 1];
                    }
                    else
                    {
                        data429 = buffer[i];
                    }

                    result.Add((data429, rate, timeHigh, timeLow));
                }

                Debug.WriteLine($"[ART4229Driver] 读取到 {realCount} 个接收数据");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ART4229Driver] 读取接收数据异常: {ex.Message}");
            }
            return result;
        }

        #endregion

        #region 未实现的接口成员

        public Task<Dictionary<string, object>> GetStatusAsync()
        {
            return Task.FromResult(new Dictionary<string, object>
            {
                { "DeviceName", DeviceName },
                { "IsConnected", IsConnected },
                { "SerialNumber", SerialNumber },
                { "ChannelCount", (int)_deviceInfo.nChannelCount },
                { "MainClock", _deviceInfo.fMainClock },
                { "MaxRate", _deviceInfo.fMaxRate },
                { "MinRate", _deviceInfo.fMinRate }
            });
        }

        public Task<bool> ConfigureChannelAsync(string channelId, Dictionary<string, object> config)
        {
            return Task.FromResult(false);
        }

        public Task<double> ReadChannelAsync(string channelId)
        {
            return Task.FromResult(0.0);
        }

        public Task<Dictionary<string, double>> ReadChannelsBatchAsync(IEnumerable<string> channelIds)
        {
            return Task.FromResult(new Dictionary<string, double>());
        }

        public Task<bool> ResetAsync()
        {
            try
            {
                if (_isConnected && _deviceHandle != IntPtr.Zero)
                {
                    int result = ART4229_32.ART4229_DEV_Reset(_deviceHandle);
                    return Task.FromResult(IsSuccess(result));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ART4229Driver] 复位设备异常: {ex.Message}");
            }
            return Task.FromResult(false);
        }

        public Task<bool> SelfTestAsync()
        {
            return Task.FromResult(true);
        }

        public Task<bool> StartAcquisitionAsync()
        {
            return Task.FromResult(false);
        }

        public Task<bool> StopAcquisitionAsync()
        {
            return Task.FromResult(false);
        }

        public Task<bool> WriteChannelAsync(string channelId, double value)
        {
            return Task.FromResult(false);
        }

        public Task<bool> WriteChannelsBatchAsync(Dictionary<string, double> channelValues)
        {
            return Task.FromResult(false);
        }

        #endregion
    }
}
