using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using MeasureControl.Models;
using MeasureControl.Models.Devices;
//123
namespace MeasureControl.Drivers
{
    /// <summary>
    /// ACTS6010 可编程电阻驱动
    /// 支持多通道可编程电阻输出
    /// </summary>
    public class ACTS6010Driver : IDeviceDriver
    {
        /// <summary>
        /// 采集状态改变事件（此驱动不支持，始终为空实现）
        /// </summary>
        public event EventHandler<AcquisitionStatusChangedEventArgs> AcquisitionStatusChanged
        {
            add { }
            remove { }
        }
        #region ACTS6010 API 声明

        // 设备信息结构体
        [StructLayout(LayoutKind.Sequential)]
        public struct ACTS6010_MAIN_INFO
        {
            public UInt32 nDeviceType;        // 设备类型
            public UInt32 nRevisionID;        // Revision ID
            public UInt32 nHardwareVer;       // 硬件版本
            public UInt32 nFirmwareVer;       // 固件版本
            public UInt32 nCheckBusH;         // 总线通信检查高
            public UInt32 nCheckBusL;         // 总线通信检查低
            public UInt32 nChannelCount;      // 通道数(1~16)
            public UInt32 nTimeBase;          // 时基
            public UInt32 nMaxResistance;     // 最大阻值,单位欧姆
            public UInt32 nMinResistance;     // 最小阻值,单位欧姆
            public UInt32 nStepResistance;    // 步进阻值,单位微欧
            public UInt32 nReserved0;
            public UInt32 nResolution;        // 分辨率
            public UInt32 nCodeCount;         // 编码总数
            public UInt32 nMaxLSB;            // 最大值
            public UInt32 nReserved1;
            public UInt32 nReserved2;
            public UInt32 nReserved3;
            public UInt32 nReserved4;
            public UInt32 nReserved5;
        }

        // 输出模式常量
        public const Int32 ACTS6010_RES_OUT_MODE_NOWAIT = 0;    // 无等待
        public const Int32 ACTS6010_RES_OUT_MODE_DEFAULT = 1;   // 先断开后闭合
        public const Int32 ACTS6010_RES_OUT_MODE_MBB = 2;       // 先闭合后断开
        public const Int32 ACTS6010_RES_OUT_MODE_WAIT = 3;     // 等待稳定时间

        // DLL 路径（根据系统位数选择）
        private const string DLL_32 = "ACTS6010_32.DLL";
        private const string DLL_64 = "ACTS6010_64.dll";

        // 设备管理函数
        [DllImport(DLL_32, EntryPoint = "ACTS6010_DEV_Create", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr ACTS6010_DEV_Create_32(UInt32 nIndex, UInt32 nIndexType);

        [DllImport(DLL_64, EntryPoint = "ACTS6010_DEV_Create", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr ACTS6010_DEV_Create_64(UInt32 nIndex, UInt32 nIndexType);

        [DllImport(DLL_32, EntryPoint = "ACTS6010_DEV_GetMainInfo", CallingConvention = CallingConvention.Cdecl)]
        public static extern bool ACTS6010_DEV_GetMainInfo_32(IntPtr hDevice, ref ACTS6010_MAIN_INFO pMainInfo);

        [DllImport(DLL_64, EntryPoint = "ACTS6010_DEV_GetMainInfo", CallingConvention = CallingConvention.Cdecl)]
        public static extern bool ACTS6010_DEV_GetMainInfo_64(IntPtr hDevice, ref ACTS6010_MAIN_INFO pMainInfo);

        [DllImport(DLL_32, EntryPoint = "ACTS6010_DEV_Release", CallingConvention = CallingConvention.Cdecl)]
        public static extern bool ACTS6010_DEV_Release_32(IntPtr hDevice);

        [DllImport(DLL_64, EntryPoint = "ACTS6010_DEV_Release", CallingConvention = CallingConvention.Cdecl)]
        public static extern bool ACTS6010_DEV_Release_64(IntPtr hDevice);

        // 电阻控制函数
        [DllImport(DLL_32, EntryPoint = "ACTS6010_RES_SetResistance", CallingConvention = CallingConvention.Cdecl)]
        public static extern bool ACTS6010_RES_SetResistance_32(IntPtr hDevice, UInt32 nChannel, UInt32 nOutputMode, ref Double pResistance);

        [DllImport(DLL_64, EntryPoint = "ACTS6010_RES_SetResistance", CallingConvention = CallingConvention.Cdecl)]
        public static extern bool ACTS6010_RES_SetResistance_64(IntPtr hDevice, UInt32 nChannel, UInt32 nOutputMode, ref Double pResistance);

        [DllImport(DLL_32, EntryPoint = "ACTS6010_RES_GetResistance", CallingConvention = CallingConvention.Cdecl)]
        public static extern bool ACTS6010_RES_GetResistance_32(IntPtr hDevice, UInt32 nChannel, UInt32 nOutputMode, ref Double pResistance);

        [DllImport(DLL_64, EntryPoint = "ACTS6010_RES_GetResistance", CallingConvention = CallingConvention.Cdecl)]
        public static extern bool ACTS6010_RES_GetResistance_64(IntPtr hDevice, UInt32 nChannel, UInt32 nOutputMode, ref Double pResistance);

        [DllImport(DLL_32, EntryPoint = "ACTS6010_RES_SetPSRelayState", CallingConvention = CallingConvention.Cdecl)]
        public static extern bool ACTS6010_RES_SetPSRelayState_32(IntPtr hDevice, UInt32 nChannel, UInt32 nPathRelay, UInt32 nShortCircuit);

        [DllImport(DLL_64, EntryPoint = "ACTS6010_RES_SetPSRelayState", CallingConvention = CallingConvention.Cdecl)]
        public static extern bool ACTS6010_RES_SetPSRelayState_64(IntPtr hDevice, UInt32 nChannel, UInt32 nPathRelay, UInt32 nShortCircuit);

        [DllImport(DLL_32, EntryPoint = "ACTS6010_RES_GetPSRelayState", CallingConvention = CallingConvention.Cdecl)]
        public static extern bool ACTS6010_RES_GetPSRelayState_32(IntPtr hDevice, UInt32 nChannel, ref UInt32 pPathRelay, ref UInt32 pShortCircuit);

        [DllImport(DLL_64, EntryPoint = "ACTS6010_RES_GetPSRelayState", CallingConvention = CallingConvention.Cdecl)]
        public static extern bool ACTS6010_RES_GetPSRelayState_64(IntPtr hDevice, UInt32 nChannel, ref UInt32 pPathRelay, ref UInt32 pShortCircuit);

        // 根据系统位数选择 DLL
        private static bool Is64Bit => IntPtr.Size == 8;

        private static IntPtr ACTS6010_DEV_Create(UInt32 nIndex, UInt32 nIndexType)
        {
            return Is64Bit ? ACTS6010_DEV_Create_64(nIndex, nIndexType) : ACTS6010_DEV_Create_32(nIndex, nIndexType);
        }

        private static bool ACTS6010_DEV_GetMainInfo(IntPtr hDevice, ref ACTS6010_MAIN_INFO pMainInfo)
        {
            return Is64Bit ? ACTS6010_DEV_GetMainInfo_64(hDevice, ref pMainInfo) : ACTS6010_DEV_GetMainInfo_32(hDevice, ref pMainInfo);
        }

        private static bool ACTS6010_DEV_Release(IntPtr hDevice)
        {
            return Is64Bit ? ACTS6010_DEV_Release_64(hDevice) : ACTS6010_DEV_Release_32(hDevice);
        }

        private static bool ACTS6010_RES_SetResistance(IntPtr hDevice, UInt32 nChannel, UInt32 nOutputMode, ref Double pResistance)
        {
            return Is64Bit ? ACTS6010_RES_SetResistance_64(hDevice, nChannel, nOutputMode, ref pResistance) : ACTS6010_RES_SetResistance_32(hDevice, nChannel, nOutputMode, ref pResistance);
        }

        private static bool ACTS6010_RES_GetResistance(IntPtr hDevice, UInt32 nChannel, UInt32 nOutputMode, ref Double pResistance)
        {
            return Is64Bit ? ACTS6010_RES_GetResistance_64(hDevice, nChannel, nOutputMode, ref pResistance) : ACTS6010_RES_GetResistance_32(hDevice, nChannel, nOutputMode, ref pResistance);
        }

        private static bool ACTS6010_RES_SetPSRelayState(IntPtr hDevice, UInt32 nChannel, UInt32 nPathRelay, UInt32 nShortCircuit)
        {
            return Is64Bit ? ACTS6010_RES_SetPSRelayState_64(hDevice, nChannel, nPathRelay, nShortCircuit) : ACTS6010_RES_SetPSRelayState_32(hDevice, nChannel, nPathRelay, nShortCircuit);
        }

        private static bool ACTS6010_RES_GetPSRelayState(IntPtr hDevice, UInt32 nChannel, ref UInt32 pPathRelay, ref UInt32 pShortCircuit)
        {
            return Is64Bit ? ACTS6010_RES_GetPSRelayState_64(hDevice, nChannel, ref pPathRelay, ref pShortCircuit) : ACTS6010_RES_GetPSRelayState_32(hDevice, nChannel, ref pPathRelay, ref pShortCircuit);
        }

        #endregion

        #region 私有字段

        private readonly DeviceBase _device;
        private readonly UInt32 _logicalId;
        private IntPtr _hDevice;
        private bool _isConnected;
        private ACTS6010_MAIN_INFO _mainInfo;
        private readonly Dictionary<string, double> _channelValues = new Dictionary<string, double>();
        private readonly Dictionary<string, RelayState> _relayStates = new Dictionary<string, RelayState>();

        private struct RelayState
        {
            public bool PathRelayClosed;
            public bool ShortCircuitClosed;
        }

        #endregion

        #region 属性

        public string DeviceId => _device?.Id ?? string.Empty;

        public string DeviceName => _device?.Name ?? "ACTS6010";

        public bool IsConnected => _isConnected;

        public bool IsSimulated => false;

        /// <summary>
        /// ACTS6010是数据采集设备
        /// </summary>
        public DeviceCapability Capability => DeviceCapability.Input;

        public UInt32 LogicalId => _logicalId;

        public ACTS6010_MAIN_INFO MainInfo => _mainInfo;

        #endregion

        #region 构造函数

        /// <summary>
        /// 创建 ACTS6010 驱动实例
        /// </summary>
        /// <param name="device">设备实例</param>
        /// <param name="logicalId">逻辑设备ID（默认0）</param>
        public ACTS6010Driver(DeviceBase device, UInt32 logicalId = 0)
        {
            _device = device ?? throw new ArgumentNullException(nameof(device));
            _logicalId = logicalId;
            _isConnected = false;
            _hDevice = IntPtr.Zero;

            // 初始化通道值
            InitializeChannels();
        }

        private void InitializeChannels()
        {
            // 初始化 9 个通道（RO0-RO8）
            for (int i = 0; i < 9; i++)
            {
                string channelId = $"RO{i}";
                _channelValues[channelId] = 0.0;
                _relayStates[channelId] = new RelayState
                {
                    PathRelayClosed = false,
                    ShortCircuitClosed = false
                };
            }
        }

        #endregion

        #region IDeviceDriver 实现

        public async Task<bool> ConnectAsync()
        {
            await Task.Yield();
            try
            {
                Debug.WriteLine($"[ACTS6010Driver] 正在连接设备 {DeviceName}, 逻辑ID: {_logicalId}");
                Debug.WriteLine($"[ACTS6010Driver] 系统位数: {(Is64Bit ? "64位" : "32位")}, 使用的DLL: {(Is64Bit ? DLL_64 : DLL_32)}");

                // 检查DLL文件是否存在（在可执行文件目录）
                string dllPath = System.IO.Path.Combine(
                    System.AppDomain.CurrentDomain.BaseDirectory,
                    Is64Bit ? DLL_64 : DLL_32);
                
                if (!System.IO.File.Exists(dllPath))
                {
                    string errorMsg = $"DLL文件不存在: {dllPath}\n" +
                                     $"请确保DLL文件已复制到输出目录。\n" +
                                     $"检查位置:\n" +
                                     $"- {System.AppDomain.CurrentDomain.BaseDirectory}\n" +
                                     $"- 系统PATH环境变量";
                    Debug.WriteLine($"[ACTS6010Driver] {errorMsg}");
                    throw new System.IO.FileNotFoundException(errorMsg);
                }

                Debug.WriteLine($"[ACTS6010Driver] DLL文件已找到: {dllPath}");

                // 创建设备句柄
                _hDevice = ACTS6010_DEV_Create(_logicalId, 0);
                
                if (_hDevice == (IntPtr)(-1))
                {
                    string errorMsg = $"创建设备句柄失败，返回 -1\n" +
                                     $"可能原因:\n" +
                                     $"1. 逻辑ID ({_logicalId}) 不正确，请检查板卡插槽号\n" +
                                     $"2. 系统驱动未安装，请检查设备管理器\n" +
                                     $"3. 板卡未插入或未上电\n" +
                                     $"4. 板卡不在指定插槽";
                    Debug.WriteLine($"[ACTS6010Driver] {errorMsg}");
                    _isConnected = false;
                    throw new InvalidOperationException(errorMsg);
                }
                
                if (_hDevice == IntPtr.Zero)
                {
                    string errorMsg = $"创建设备句柄失败，返回 Zero\n" +
                                     $"可能原因:\n" +
                                     $"1. 逻辑ID ({_logicalId}) 不正确\n" +
                                     $"2. 系统驱动未安装\n" +
                                     $"3. 板卡未连接或未上电";
                    Debug.WriteLine($"[ACTS6010Driver] {errorMsg}");
                    _isConnected = false;
                    throw new InvalidOperationException(errorMsg);
                }

                // 获取设备信息
                _mainInfo = new ACTS6010_MAIN_INFO();
                if (!ACTS6010_DEV_GetMainInfo(_hDevice, ref _mainInfo))
                {
                    Debug.WriteLine($"[ACTS6010Driver] 获取设备信息失败");
                    ACTS6010_DEV_Release(_hDevice);
                    _hDevice = IntPtr.Zero;
                    _isConnected = false;
                    return false;
                }

                Debug.WriteLine($"[ACTS6010Driver] 设备信息 - 通道数: {_mainInfo.nChannelCount}, 阻值范围: {_mainInfo.nMinResistance}Ω ~ {_mainInfo.nMaxResistance}Ω");

                _isConnected = true;
                Debug.WriteLine($"[ACTS6010Driver] 设备连接成功");
                return true;
            }
            catch (System.IO.FileNotFoundException ex)
            {
                string errorMsg = $"DLL文件未找到: {ex.Message}\n" +
                                 $"解决方案:\n" +
                                 $"1. 检查 Libs 文件夹中是否有 DLL 文件\n" +
                                 $"2. 重新编译项目，确保 DLL 被复制到输出目录\n" +
                                 $"3. 手动将 DLL 复制到: {System.AppDomain.CurrentDomain.BaseDirectory}";
                Debug.WriteLine($"[ACTS6010Driver] {errorMsg}");
                Debug.WriteLine($"[ACTS6010Driver] 异常详情: {ex}");
                _isConnected = false;
                throw new System.IO.FileNotFoundException(errorMsg, ex);
            }
            catch (System.DllNotFoundException ex)
            {
                string errorMsg = $"无法加载DLL: {ex.Message}\n" +
                                 $"可能原因:\n" +
                                 $"1. DLL文件缺失或路径不正确\n" +
                                 $"2. DLL依赖的其他库缺失（如 Visual C++ 运行库）\n" +
                                 $"3. DLL版本与系统不匹配";
                Debug.WriteLine($"[ACTS6010Driver] {errorMsg}");
                Debug.WriteLine($"[ACTS6010Driver] 异常详情: {ex}");
                _isConnected = false;
                throw new System.DllNotFoundException(errorMsg, ex);
            }
            catch (System.BadImageFormatException ex)
            {
                string errorMsg = $"DLL格式错误: {ex.Message}\n" +
                                 $"可能原因:\n" +
                                 $"1. DLL版本与系统位数不匹配（32位/64位）\n" +
                                 $"2. DLL文件损坏";
                Debug.WriteLine($"[ACTS6010Driver] {errorMsg}");
                Debug.WriteLine($"[ACTS6010Driver] 异常详情: {ex}");
                _isConnected = false;
                throw new System.BadImageFormatException(errorMsg, ex);
            }
            catch (Exception ex)
            {
                string errorMsg = $"连接失败: {ex.Message}\n" +
                                 $"异常类型: {ex.GetType().Name}";
                Debug.WriteLine($"[ACTS6010Driver] {errorMsg}");
                Debug.WriteLine($"[ACTS6010Driver] 异常堆栈: {ex.StackTrace}");
                _isConnected = false;
                if (_hDevice != IntPtr.Zero)
                {
                    try
                    {
                        ACTS6010_DEV_Release(_hDevice);
                    }
                    catch { }
                    _hDevice = IntPtr.Zero;
                }
                throw; // 重新抛出异常，让调用者处理
            }
        }

        public async Task<bool> DisconnectAsync()
        {
            await Task.Yield();
            try
            {
                if (_hDevice != IntPtr.Zero)
                {
                    ACTS6010_DEV_Release(_hDevice);
                    _hDevice = IntPtr.Zero;
                }
                _isConnected = false;
                Debug.WriteLine($"[ACTS6010Driver] 设备已断开");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ACTS6010Driver] 断开连接失败: {ex.Message}");
                return false;
            }
        }

        public async Task<double> ReadChannelAsync(string channelId)
        {
            await Task.Yield();
            if (!_isConnected || _hDevice == IntPtr.Zero)
            {
                throw new InvalidOperationException("设备未连接");
            }

            // 解析通道号（RO0 -> 0, RO1 -> 1, ...）
            if (!TryParseChannelIndex(channelId, out UInt32 channelIndex))
            {
                throw new ArgumentException($"无效的通道ID: {channelId}");
            }

            try
            {
                double resistance = 0.0;
                if (ACTS6010_RES_GetResistance(_hDevice, channelIndex, ACTS6010_RES_OUT_MODE_NOWAIT, ref resistance))
                {
                    _channelValues[channelId] = resistance;
                    Debug.WriteLine($"[ACTS6010Driver] 读取 {channelId} 阻值: {resistance:F4}Ω");
                    return resistance;
                }
                else
                {
                    Debug.WriteLine($"[ACTS6010Driver] 读取 {channelId} 阻值失败");
                    return _channelValues.ContainsKey(channelId) ? _channelValues[channelId] : 0.0;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ACTS6010Driver] 读取通道 {channelId} 失败: {ex.Message}");
                return _channelValues.ContainsKey(channelId) ? _channelValues[channelId] : 0.0;
            }
        }

        public async Task<Dictionary<string, double>> ReadChannelsBatchAsync(IEnumerable<string> channelIds)
        {
            await Task.Yield();
            var result = new Dictionary<string, double>();
            foreach (var channelId in channelIds)
            {
                result[channelId] = await ReadChannelAsync(channelId);
            }
            return result;
        }

        public async Task<bool> WriteChannelAsync(string channelId, double value)
        {
            await Task.Yield();
            if (!_isConnected || _hDevice == IntPtr.Zero)
            {
                Debug.WriteLine($"[ACTS6010Driver] 写入失败：设备未连接");
                return false;
            }

            // 解析通道号
            if (!TryParseChannelIndex(channelId, out UInt32 channelIndex))
            {
                Debug.WriteLine($"[ACTS6010Driver] 无效的通道ID: {channelId}");
                return false;
            }

            try
            {
                // 限制阻值范围
                if (value < _mainInfo.nMinResistance)
                    value = _mainInfo.nMinResistance;
                if (value > _mainInfo.nMaxResistance)
                    value = _mainInfo.nMaxResistance;

                double resistance = value;
                if (ACTS6010_RES_SetResistance(_hDevice, channelIndex, ACTS6010_RES_OUT_MODE_NOWAIT, ref resistance))
                {
                    _channelValues[channelId] = resistance;
                    Debug.WriteLine($"[ACTS6010Driver] 写入 {channelId} 阻值: {resistance:F4}Ω");
                    return true;
                }
                else
                {
                    Debug.WriteLine($"[ACTS6010Driver] 写入 {channelId} 阻值失败");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ACTS6010Driver] 写入通道 {channelId} 失败: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> WriteChannelsBatchAsync(Dictionary<string, double> channelValues)
        {
            await Task.Yield();
            bool allSuccess = true;
            foreach (var kvp in channelValues)
            {
                bool success = await WriteChannelAsync(kvp.Key, kvp.Value);
                if (!success) allSuccess = false;
            }
            return allSuccess;
        }

        /// <summary>
        /// 设置继电器状态
        /// </summary>
        public async Task<bool> SetRelayStateAsync(string channelId, bool pathRelayClosed, bool shortCircuitClosed)
        {
            await Task.Yield();
            if (!_isConnected || _hDevice == IntPtr.Zero)
            {
                Debug.WriteLine($"[ACTS6010Driver] 设置继电器状态失败：设备未连接");
                return false;
            }

            if (!TryParseChannelIndex(channelId, out UInt32 channelIndex))
            {
                Debug.WriteLine($"[ACTS6010Driver] 无效的通道ID: {channelId}");
                return false;
            }

            try
            {
                UInt32 pathRelay = pathRelayClosed ? (UInt32)1 : (UInt32)0;
                UInt32 shortCircuit = shortCircuitClosed ? (UInt32)1 : (UInt32)0;

                if (ACTS6010_RES_SetPSRelayState(_hDevice, channelIndex, pathRelay, shortCircuit))
                {
                    if (_relayStates.ContainsKey(channelId))
                    {
                        _relayStates[channelId] = new RelayState
                        {
                            PathRelayClosed = pathRelayClosed,
                            ShortCircuitClosed = shortCircuitClosed
                        };
                    }
                    Debug.WriteLine($"[ACTS6010Driver] 设置 {channelId} 继电器状态 - 通路: {(pathRelayClosed ? "闭合" : "断开")}, 短路: {(shortCircuitClosed ? "闭合" : "断开")}");
                    return true;
                }
                else
                {
                    Debug.WriteLine($"[ACTS6010Driver] 设置 {channelId} 继电器状态失败");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ACTS6010Driver] 设置继电器状态失败: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> ConfigureChannelAsync(string channelId, Dictionary<string, object> config)
        {
            await Task.Yield();
            // ACTS6010 不需要额外的通道配置
            return true;
        }

        public async Task<bool> StartAcquisitionAsync()
        {
            await Task.Yield();
            // ACTS6010 不需要启动采集任务
            return true;
        }

        public async Task<bool> StopAcquisitionAsync()
        {
            await Task.Yield();
            // ACTS6010 不需要停止采集任务
            return true;
        }

        public async Task<Dictionary<string, object>> GetStatusAsync()
        {
            await Task.Yield();
            var status = new Dictionary<string, object>
            {
                ["IsConnected"] = _isConnected,
                ["LogicalId"] = _logicalId,
                ["ChannelCount"] = _mainInfo.nChannelCount,
                ["MinResistance"] = _mainInfo.nMinResistance,
                ["MaxResistance"] = _mainInfo.nMaxResistance
            };
            return status;
        }

        public async Task<bool> ResetAsync()
        {
            await Task.Yield();
            // 可以在这里实现设备复位逻辑
            return true;
        }

        public async Task<bool> SelfTestAsync()
        {
            await Task.Yield();
            // 可以在这里实现自检逻辑
            return true;
        }

        #endregion

        #region 辅助方法

        /// <summary>
        /// 解析通道ID，提取通道索引（RO0 -> 0, RO1 -> 1, ...）
        /// </summary>
        private bool TryParseChannelIndex(string channelId, out UInt32 channelIndex)
        {
            channelIndex = 0;
            if (string.IsNullOrEmpty(channelId))
                return false;

            // 移除 "RO" 前缀
            if (channelId.StartsWith("RO", StringComparison.OrdinalIgnoreCase))
            {
                string indexStr = channelId.Substring(2);
                if (UInt32.TryParse(indexStr, out channelIndex))
                {
                    return true;
                }
            }

            return false;
        }

        #endregion
    }
}

