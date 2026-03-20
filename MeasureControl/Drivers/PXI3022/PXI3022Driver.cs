 using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using MeasureControl.Models;
using MeasureControl.Models.Devices;
using OKAIPXIDevice;

namespace MeasureControl.Drivers.PXI3022
{
    /// <summary>
    /// PXI3022 矩阵继电器驱动
    /// 支持 4行 x 64列 = 256个继电器通道
    /// </summary>
    public class PXI3022Driver : IDeviceDriver
    {
        /// <summary>
        /// 采集状态改变事件
        /// </summary>
        public event EventHandler<AcquisitionStatusChangedEventArgs> AcquisitionStatusChanged;

        /// <summary>
        /// 设备功能类型
        /// </summary>
        public DeviceCapability Capability => DeviceCapability.Other;

        #region 私有字段

        private readonly DeviceBase _device;
        private readonly ushort _deviceId;
        private ushort _openedDeviceId;
        private bool _isConnected;
        private UIntPtr _deviceHandle = UIntPtr.Zero;

        // 继电器状态存储
        private readonly bool[,] _relayStates = new bool[PXI3022Constants.PXI3022_ROW_4ROW, PXI3022Constants.PXI3022_COL_64COL];
        private readonly Dictionary<string, (int row, int col)> _channelMappings = new Dictionary<string, (int, int)>();

        // 扫描表相关
        private readonly uint _maxScanTableNum = PXI3022Constants.PXI3022_SCAN_TABLE_MAX_NUM;
        private uint _currentScanTableNum = 0;

        private const ushort DefaultScanIndex = 0;

        private readonly SemaphoreSlim _relayIoLock = new SemaphoreSlim(1, 1);

        #endregion

        #region 私有辅助方法

        private ushort[] BuildRelayFlag1DFromCache()
        {
            var flags = new ushort[PXI3022Constants.RELAY_FLAG_ARRAY_SIZE];
            for (int row = 0; row < PXI3022Constants.PXI3022_ROW_4ROW; row++)
            {
                for (int col = 0; col < PXI3022Constants.PXI3022_COL_64COL; col++)
                {
                    int idx = row * PXI3022Constants.PXI3022_COL_64COL + col;
                    flags[idx] = _relayStates[row, col] ? (ushort)1 : (ushort)0;
                }
            }
            return flags;
        }

        private bool TryGetRelayFlag1DFromHardware(out ushort[] flags)
        {
            flags = new ushort[PXI3022Constants.RELAY_FLAG_ARRAY_SIZE];

            if (!_isConnected || _deviceHandle == UIntPtr.Zero)
            {
                return false;
            }

            int result = PXI3022Native.pxi3022_getRelalyFlag1D(_deviceHandle, DefaultScanIndex, flags);
            if (result != 0)
            {
                Debug.WriteLine($"[PXI3022Driver] pxi3022_getRelalyFlag1D 失败，错误码: {result}");
                return false;
            }

            return true;
        }

        private async Task<bool> ApplyRelayFlag1DAsync(ushort[] flags)
        {
            if (!_isConnected || _deviceHandle == UIntPtr.Zero)
            {
                Debug.WriteLine("[PXI3022Driver] 设备未连接或句柄无效，无法下发继电器状态");
                return false;
            }

            if (flags == null || flags.Length != PXI3022Constants.RELAY_FLAG_ARRAY_SIZE)
            {
                Debug.WriteLine($"[PXI3022Driver] 继电器状态数组长度错误: {flags?.Length}, 期望: {PXI3022Constants.RELAY_FLAG_ARRAY_SIZE}");
                return false;
            }

            PXI3022Native.pxi3022_setTrigSource(_deviceHandle, 1);

            int result = PXI3022Native.pxi3022_setRelalyFlag1D(_deviceHandle, DefaultScanIndex, flags);
            if (result != 0)
            {
                Debug.WriteLine($"[PXI3022Driver] pxi3022_setRelalyFlag1D 失败，错误码: {result}");
                return false;
            }

            result = PXI3022Native.pxi3022_setScanTableNum(_deviceHandle, 1);
            if (result != 0)
            {
                Debug.WriteLine($"[PXI3022Driver] pxi3022_setScanTableNum(1) 失败，错误码: {result}");
            }

            result = PXI3022Native.pxi3022_softImmTrig(_deviceHandle);
            if (result != 0)
            {
                Debug.WriteLine($"[PXI3022Driver] pxi3022_softImmTrig 失败，错误码: {result}");
                return false;
            }

            await Task.CompletedTask;
            return true;
        }

        private bool SyncRelayStatesFromHardware()
        {
            if (!_isConnected || _deviceHandle == UIntPtr.Zero)
            {
                return false;
            }

            try
            {
                var flags = new ushort[PXI3022Constants.RELAY_FLAG_ARRAY_SIZE];
                int result = PXI3022Native.pxi3022_getRelalyFlag1D(_deviceHandle, DefaultScanIndex, flags);
                if (result != 0)
                {
                    Debug.WriteLine($"[PXI3022Driver] pxi3022_getRelalyFlag1D 失败，错误码: {result}");
                    return false;
                }

                for (int row = 0; row < PXI3022Constants.PXI3022_ROW_4ROW; row++)
                {
                    for (int col = 0; col < PXI3022Constants.PXI3022_COL_64COL; col++)
                    {
                        int idx = row * PXI3022Constants.PXI3022_COL_64COL + col;
                        _relayStates[row, col] = flags[idx] != 0;
                    }
                }

                Debug.WriteLine("[PXI3022Driver] 已从硬件同步继电器状态缓存");

                // 输出所有通道状态
                Debug.WriteLine("=== 当前所有通道状态 ===");
                for (int row = 0; row < PXI3022Constants.PXI3022_ROW_4ROW; row++)
                {
                    for (int col = 0; col < PXI3022Constants.PXI3022_COL_64COL; col++)
                    {
                        if (_relayStates[row, col])
                        {
                            Debug.WriteLine($"通道 ({row},{col}) 已连接");
                        }
                    }
                }
                Debug.WriteLine("=== 扫描完成 ===");

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PXI3022Driver] 同步继电器状态失败: {ex.Message}");
                return false;
            }
        }

        private bool TryGetDeviceSlot(UIntPtr handle, out ushort slot)
        {
            slot = 0;
            if (handle == UIntPtr.Zero)
            {
                return false;
            }

            try
            {
                int status = OKAIDaqNative.DAQDevice_getSlot(handle, out slot);
                if (status != 0)
                {
                    Debug.WriteLine($"[PXI3022Driver] DAQDevice_getSlot 失败，错误码: {status}");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PXI3022Driver] DAQDevice_getSlot 异常: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region 属性

        /// <summary>
        /// 设备ID
        /// </summary>
        public string DeviceId => _device?.Id ?? string.Empty;

        /// <summary>
        /// 设备名称
        /// </summary>
        public string DeviceName => _device?.Name ?? "PXI3022";

        /// <summary>
        /// 是否已连接
        /// </summary>
        public bool IsConnected => _isConnected;

        /// <summary>
        /// 是否为模拟驱动（PXI3022 不支持模拟，始终为 false）
        /// </summary>
        public bool IsSimulated => false;

        /// <summary>
        /// 设备句柄
        /// </summary>
        public UIntPtr DeviceHandle => _deviceHandle;

        #endregion

        #region 构造函数

        /// <summary>
        /// 创建 PXI3022 驱动实例
        /// </summary>
        /// <param name="device">设备实例</param>
        /// <param name="deviceId">设备ID（默认为0）</param>
        public PXI3022Driver(DeviceBase device, ushort deviceId = 0)
        {
            _device = device ?? throw new ArgumentNullException(nameof(device));
            _deviceId = deviceId;
            _isConnected = false;

            // 初始化通道映射和继电器状态
            InitializeChannels();
        }

        /// <summary>
        /// 初始化通道映射
        /// </summary>
        private void InitializeChannels()
        {
            // 初始化所有继电器为断开状态
            for (int row = 0; row < PXI3022Constants.PXI3022_ROW_4ROW; row++)
            {
                for (int col = 0; col < PXI3022Constants.PXI3022_COL_64COL; col++)
                {
                    _relayStates[row, col] = false;
                    string channelId = $"R{row}C{col}";
                    _channelMappings[channelId] = (row, col);
                }
            }
        }

        #endregion

        #region IDeviceDriver 实现

        /// <summary>
        /// 连接设备
        /// </summary>
        public async Task<bool> ConnectAsync()
        {
            try
            {
                ushort openDeviceId = _deviceId;
                if (openDeviceId == 0)
                    openDeviceId = 1;
                else if (openDeviceId == 1)
                    openDeviceId = 2;
                else if (openDeviceId == 2)
                    openDeviceId = 1;

                _deviceHandle = UIntPtr.Zero;
                _openedDeviceId = 0;

                Debug.WriteLine($"[PXI3022Driver] 正在连接设备 {DeviceName}, openDeviceId: {openDeviceId}");
                _deviceHandle = PXI3022Native.pxi3022_openDevice(openDeviceId);
                if (_deviceHandle == UIntPtr.Zero)
                {
                    Debug.WriteLine($"[PXI3022Driver] 无法打开设备，openDeviceId: {openDeviceId}");
                    return false;
                }

                _openedDeviceId = openDeviceId;

                // 重置设备
                int result = PXI3022Native.pxi3022_reset(_deviceHandle);
                if (result != 0)
                {
                    Debug.WriteLine($"[PXI3022Driver] 设备重置失败，错误码: {result}");
                    PXI3022Native.pxi3022_releaseDevice(_deviceHandle);
                    _deviceHandle = UIntPtr.Zero;
                    return false;
                }

                // 配置扫描表数量
                result = PXI3022Native.pxi3022_setScanTableNum(_deviceHandle, _maxScanTableNum);
                if (result != 0)
                {
                    Debug.WriteLine($"[PXI3022Driver] 设置扫描表数量失败，错误码: {result}");
                }

                // 启动设备（后续软触发才会生效）
                result = PXI3022Native.pxi3022_start(_deviceHandle);
                if (result != 0)
                {
                    Debug.WriteLine($"[PXI3022Driver] pxi3022_start 失败，错误码: {result}");
                    PXI3022Native.pxi3022_releaseDevice(_deviceHandle);
                    _deviceHandle = UIntPtr.Zero;
                    return false;
                }

                await Task.Delay(100);

                _isConnected = true;
                Debug.WriteLine($"[PXI3022Driver] 设备连接成功，句柄: {_deviceHandle}, 设备ID: {_openedDeviceId}");

                // 同步硬件当前继电器状态，确保后续改单点不会把其他点误写为 0
                await _relayIoLock.WaitAsync();
                try
                {
                    SyncRelayStatesFromHardware();
                }
                finally
                {
                    _relayIoLock.Release();
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PXI3022Driver] 连接失败: {ex.Message}");
                _isConnected = false;
                _deviceHandle = UIntPtr.Zero;
                throw;
            }
        }

        public async Task<bool> DisconnectAsync()
        {
            if (!_isConnected)
            {
                return true;
            }

            await _relayIoLock.WaitAsync();
            try
            {
                if (_deviceHandle == UIntPtr.Zero)
                {
                    _isConnected = false;
                    _openedDeviceId = 0;
                    return true;
                }

                try
                {
                    // 尝试停止（即使已停止也无妨）
                    PXI3022Native.pxi3022_stop(_deviceHandle);
                }
                catch
                {
                }

                try
                {
                    int result = PXI3022Native.pxi3022_releaseDevice(_deviceHandle);
                    if (result != 0)
                    {
                        Debug.WriteLine($"[PXI3022Driver] 释放设备失败，错误码: {result}");
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[PXI3022Driver] 断开连接失败: {ex.Message}");
                    return false;
                }

                _deviceHandle = UIntPtr.Zero;
                _isConnected = false;
                _openedDeviceId = 0;
                return true;
            }
            finally
            {
                _relayIoLock.Release();
            }
        }

        public async Task<double> ReadChannelAsync(string channelId)
        {
            if (string.IsNullOrWhiteSpace(channelId))
            {
                return 0;
            }

            if (!_channelMappings.TryGetValue(channelId, out var location))
            {
                return 0;
            }

            if (!_isConnected || _deviceHandle == UIntPtr.Zero)
            {
                return _relayStates[location.row, location.col] ? 1.0 : 0.0;
            }

            await _relayIoLock.WaitAsync();
            try
            {
                SyncRelayStatesFromHardware();
                return _relayStates[location.row, location.col] ? 1.0 : 0.0;
            }
            finally
            {
                _relayIoLock.Release();
            }
        }

        public async Task<Dictionary<string, double>> ReadChannelsBatchAsync(IEnumerable<string> channelIds)
        {
            var values = new Dictionary<string, double>();
            if (channelIds == null)
            {
                return values;
            }

            if (_isConnected && _deviceHandle != UIntPtr.Zero)
            {
                await _relayIoLock.WaitAsync();
                try
                {
                    SyncRelayStatesFromHardware();
                }
                finally
                {
                    _relayIoLock.Release();
                }
            }

            foreach (var id in channelIds)
            {
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                if (_channelMappings.TryGetValue(id, out var location))
                {
                    values[id] = _relayStates[location.row, location.col] ? 1.0 : 0.0;
                }
            }

            return values;
        }

        /// <summary>
        /// 写入单个通道（控制继电器闭合/断开）
        /// </summary>
        public async Task<bool> WriteChannelAsync(string channelId, double value)
        {
            if (!_isConnected)
            {
                Debug.WriteLine($"[PXI3022Driver] 设备未连接，无法写入通道 {channelId}");
                return false;
            }

            if (_deviceHandle == UIntPtr.Zero)
            {
                Debug.WriteLine($"[PXI3022Driver] 设备句柄无效，无法写入通道 {channelId}");
                return false;
            }

            try
            {
                if (!_channelMappings.TryGetValue(channelId, out var location))
                {
                    Debug.WriteLine($"[PXI3022Driver] 无效的通道ID: {channelId}");
                    return false;
                }

                bool relayState = value > 0.5;

                await _relayIoLock.WaitAsync();
                try
                {
                    ushort[] flags;
                    if (!TryGetRelayFlag1DFromHardware(out flags))
                    {
                        flags = BuildRelayFlag1DFromCache();
                    }

                    int idx = location.row * PXI3022Constants.PXI3022_COL_64COL + location.col;
                    if (idx < 0 || idx >= PXI3022Constants.RELAY_FLAG_ARRAY_SIZE)
                    {
                        Debug.WriteLine($"[PXI3022Driver] 通道索引越界: {channelId}, idx={idx}");
                        return false;
                    }

                    flags[idx] = relayState ? (ushort)1 : (ushort)0;

                    bool success = await ApplyRelayFlag1DAsync(flags);
                    if (!success)
                    {
                        Debug.WriteLine($"[PXI3022Driver] 写入通道 {channelId} = {value} 下发失败");
                        return false;
                    }

                    _relayStates[location.row, location.col] = relayState;

                    Debug.WriteLine($"[PXI3022Driver] 写入通道 {channelId} = {value} (状态: {relayState})");

                    // 操作成功后，验证硬件状态并输出所有通道状态
                    await Task.Delay(50); // 等待硬件稳定
                    SyncRelayStatesFromHardware(); // 自动同步并输出所有通道状态

                    return true;
                }
                finally
                {
                    _relayIoLock.Release();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PXI3022Driver] 写入通道 {channelId} 失败: {ex.Message}");
                return false;
            }
        }

        
        public void Write3022()
        {
            ushort slot;
            UIntPtr pPxi3022;
           
            pPxi3022 = PXI3022Native.pxi3022_openDevice(2);
            if (pPxi3022 == UIntPtr.Zero)
            {
                //MessageBox.Show("Error!!");
            }
            else
            {
                //MessageBox.Show("Sucess,,,");

                OKAIDaqNative.DAQDevice_getSlot(pPxi3022, out slot);
                Console.WriteLine("Sucess,,," + slot);
            }
            int status;
            ushort scanIndex = 0;

            ushort[] rowColFlag = new ushort[PXI3022Constants.RELAY_FLAG_ARRAY_SIZE];
            int k, rowIndex, colIndex, dataIndex;

            //置默认值
            for (k = 0; k < PXI3022Constants.RELAY_FLAG_ARRAY_SIZE; k++)
            {
                rowColFlag[k] = 0;
            }

            //置需要连通的点
            rowIndex = 0;  //分4排，0-3,
            colIndex = 2;   //每排64个列，0-63
            dataIndex = rowIndex * 64 + colIndex;   //计算在1维数组中的位置
            rowColFlag[dataIndex] = 1;
            //rowColFlag[dataIndex+1] = 1;

            //设置扫描表继电器联接状态,0 :断开该点继电器，1:接通该点继电器
            PXI3022Native.pxi3022_setTrigSource(pPxi3022, 1);
            PXI3022Native.pxi3022_setRelalyFlag1D(pPxi3022, scanIndex, rowColFlag);
            status = PXI3022Native.pxi3022_setScanTableNum(pPxi3022, 1);
            status = PXI3022Native.pxi3022_start(pPxi3022);
            status = PXI3022Native.pxi3022_softImmTrig(pPxi3022);

        }


        public async Task<bool> WriteChannelsBatchAsync(Dictionary<string, double> channelValues)
        {
            if (channelValues == null || channelValues.Count == 0)
            {
                return true;
            }

            if (!_isConnected || _deviceHandle == UIntPtr.Zero)
            {
                return false;
            }

            await _relayIoLock.WaitAsync();
            try
            {
                ushort[] flags;
                if (!TryGetRelayFlag1DFromHardware(out flags))
                {
                    flags = BuildRelayFlag1DFromCache();
                }

                foreach (var kv in channelValues)
                {
                    if (string.IsNullOrWhiteSpace(kv.Key))
                    {
                        continue;
                    }

                    if (!_channelMappings.TryGetValue(kv.Key, out var location))
                    {
                        return false;
                    }

                    bool relayState = kv.Value > 0.5;

                    int idx = location.row * PXI3022Constants.PXI3022_COL_64COL + location.col;
                    if (idx < 0 || idx >= PXI3022Constants.RELAY_FLAG_ARRAY_SIZE)
                    {
                        Debug.WriteLine($"[PXI3022Driver] 行标志索引越界: row={location.row}, col={location.col}, idx={idx}");
                        return false;
                    }

                    flags[idx] = relayState ? (ushort)1 : (ushort)0;
                }

                bool ok = await ApplyRelayFlag1DAsync(flags);
                if (!ok)
                {
                    return false;
                }

                foreach (var kv in channelValues)
                {
                    if (string.IsNullOrWhiteSpace(kv.Key))
                    {
                        continue;
                    }

                    if (_channelMappings.TryGetValue(kv.Key, out var location))
                    {
                        _relayStates[location.row, location.col] = kv.Value > 0.5;
                    }
                }

                return true;
            }
            finally
            {
                _relayIoLock.Release();
            }
        }

        public async Task<bool> ConfigureChannelAsync(string channelId, Dictionary<string, object> config)
        {
            await Task.CompletedTask;
            return !string.IsNullOrWhiteSpace(channelId) && _channelMappings.ContainsKey(channelId);
        }

        public async Task<bool> StartAcquisitionAsync()
        {
            if (!_isConnected || _deviceHandle == UIntPtr.Zero)
            {
                return false;
            }

            int result = PXI3022Native.pxi3022_start(_deviceHandle);
            await Task.CompletedTask;
            if (result != 0)
            {
                Debug.WriteLine($"[PXI3022Driver] 启动采集失败，错误码: {result}");
                return false;
            }
            return true;
        }

        public async Task<bool> StopAcquisitionAsync()
        {
            if (!_isConnected || _deviceHandle == UIntPtr.Zero)
            {
                return true;
            }

            int result = PXI3022Native.pxi3022_stop(_deviceHandle);
            await Task.CompletedTask;
            if (result != 0)
            {
                Debug.WriteLine($"[PXI3022Driver] 停止采集失败，错误码: {result}");
                return false;
            }
            return true;
        }

        public async Task<Dictionary<string, object>> GetStatusAsync()
        {
            var status = new Dictionary<string, object>();
            status["IsConnected"] = _isConnected;
            status["DeviceId"] = _deviceId;
            status["CurrentScanTableNum"] = _currentScanTableNum;

            if (_isConnected && _deviceHandle != UIntPtr.Zero)
            {
                try
                {
                    PXI3022Native.pxi3022_getScanTableNum(_deviceHandle, out uint scanNum);
                    status["ScanTableNum"] = scanNum;
                    status["WaitingTrigStatus"] = GetWaitingTriggerStatus();
                    status["CurrentScanTablePtr"] = GetCurrentScanTablePointer();
                }
                catch
                {
                }
            }

            await Task.CompletedTask;
            return status;
        }

        public async Task<bool> SelfTestAsync()
        {
            if (!_isConnected || _deviceHandle == UIntPtr.Zero)
            {
                return false;
            }

            try
            {
                PXI3022Native.pxi3022_getScanTableNum(_deviceHandle, out uint _);
                await Task.CompletedTask;
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 重置设备（断开所有继电器）
        /// </summary>
        public async Task<bool> ResetAsync()
        {
            Debug.WriteLine($"[PXI3022Driver] 重置设备，断开所有继电器");

            try
            {
                await _relayIoLock.WaitAsync();
                try
                {
                    // 断开所有继电器（一次整表下发）
                    for (int row = 0; row < PXI3022Constants.PXI3022_ROW_4ROW; row++)
                    {
                        for (int col = 0; col < PXI3022Constants.PXI3022_COL_64COL; col++)
                        {
                            _relayStates[row, col] = false;
                        }
                    }

                    ushort[] flags = BuildRelayFlag1DFromCache();
                    bool applied = await ApplyRelayFlag1DAsync(flags);
                    if (!applied)
                    {
                        Debug.WriteLine("[PXI3022Driver] Reset: 下发全断开状态失败");
                        return false;
                    }
                }
                finally
                {
                    _relayIoLock.Release();
                }

                // 执行硬件重置
                if (_deviceHandle != UIntPtr.Zero)
                {
                    int result = PXI3022Native.pxi3022_reset(_deviceHandle);
                    if (result != 0)
                    {
                        Debug.WriteLine($"[PXI3022Driver] 硬件重置失败，错误码: {result}");
                        return false;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PXI3022Driver] 重置失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 写入行扫描标志（批量控制一行中的多个继电器）
        /// </summary>
        public async Task<bool> WriteRowFlagsAsync(int row, ulong columnFlags)
        {
            if (!_isConnected)
            {
                Debug.WriteLine($"[PXI3022Driver] 设备未连接，无法写入行标志");
                return false;
            }

            if (_deviceHandle == UIntPtr.Zero)
            {
                Debug.WriteLine($"[PXI3022Driver] 设备句柄无效，无法写入行标志");
                return false;
            }

            try
            {
                if (row < 0 || row >= PXI3022Constants.PXI3022_ROW_4ROW)
                {
                    Debug.WriteLine($"[PXI3022Driver] 无效的行号: {row}");
                    return false;
                }

                await _relayIoLock.WaitAsync();
                try
                {
                    ushort[] flags;
                    if (!TryGetRelayFlag1DFromHardware(out flags))
                    {
                        flags = BuildRelayFlag1DFromCache();
                    }

                    // 更新这一行（64列）
                    for (int col = 0; col < PXI3022Constants.PXI3022_COL_64COL; col++)
                    {
                        bool state = (columnFlags & (1UL << col)) != 0;

                        int idx = row * PXI3022Constants.PXI3022_COL_64COL + col;
                        if (idx < 0 || idx >= PXI3022Constants.RELAY_FLAG_ARRAY_SIZE)
                        {
                            Debug.WriteLine($"[PXI3022Driver] 行标志索引越界: row={row}, col={col}, idx={idx}");
                            return false;
                        }

                        flags[idx] = state ? (ushort)1 : (ushort)0;
                    }

                    bool success = await ApplyRelayFlag1DAsync(flags);
                    if (!success)
                    {
                        Debug.WriteLine($"[PXI3022Driver] 写入第{row}行标志下发失败");
                        return false;
                    }

                    for (int col = 0; col < PXI3022Constants.PXI3022_COL_64COL; col++)
                    {
                        bool state = (columnFlags & (1UL << col)) != 0;
                        _relayStates[row, col] = state;
                    }

                    Debug.WriteLine($"[PXI3022Driver] 写入第{row}行标志: 0x{columnFlags:X16}");
                    return true;
                }
                finally
                {
                    _relayIoLock.Release();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PXI3022Driver] 写入行标志失败: {ex.Message}");
                return false;
            }
        }

        public ushort GetWaitingTriggerStatus()
        {
            if (_deviceHandle != UIntPtr.Zero && _isConnected)
            {
                PXI3022Native.pxi3022_getWaitingTrigStatus(_deviceHandle, out ushort status);
                return status;
            }
            return 0;
        }

        /// <summary>
        /// 获取当前扫描表指针
        /// </summary>
        public uint GetCurrentScanTablePointer()
        {
            if (_deviceHandle != UIntPtr.Zero && _isConnected)
            {
                PXI3022Native.pxi3022_getCurrentScanTablePtr(_deviceHandle, out uint pointer);
                return pointer;
            }
            return 0;
        }

        /// <summary>
        /// 软件立即触发
        /// </summary>
        public bool SoftImmediateTrigger()
        {
            if (_deviceHandle != UIntPtr.Zero && _isConnected)
            {
                int result = PXI3022Native.pxi3022_softImmTrig(_deviceHandle);
                return result == 0;
            }
            return false;
        }

        #endregion

        #region 析构函数

        /// <summary>
        /// 析构函数，确保资源释放
        /// </summary>
        ~PXI3022Driver()
        {
            if (_deviceHandle != UIntPtr.Zero)
            {
                try
                {
                    PXI3022Native.pxi3022_stop(_deviceHandle);
                    PXI3022Native.pxi3022_releaseDevice(_deviceHandle);
                }
                catch
                {
                    // 析构函数中忽略异常
                }
            }
        }

        #endregion
    }
}