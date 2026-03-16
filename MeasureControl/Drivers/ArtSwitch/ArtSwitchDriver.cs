using DryIoc;
using MeasureControl.Drivers.ArtSwitch;
using MeasureControl.Models;
using MeasureControl.Models.Devices;
using Prism.Mvvm;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace MeasureControl.Drivers.ArtSwitch
{
    /// <summary>
    /// ART-SWITCH 继电器矩阵驱动
    /// 支持多种拓扑结构（如矩阵、多路复用器等）
    /// </summary>
    public class ArtSwitchDriver : BindableBase, IDeviceDriver
    {
        #region 事件

        /// <summary>
        /// 采集状态改变事件（此驱动不支持，始终为空实现）
        /// </summary>
        public event EventHandler<AcquisitionStatusChangedEventArgs> AcquisitionStatusChanged
        {
            add { }
            remove { }
        }

        #endregion

        #region 私有字段

        private readonly DeviceBase _device;
        private readonly string _resourceName;
        private readonly int _slotNumber;
        private uint _switchSession;
        private bool _isConnected;
        private string _currentTopology;

        #endregion

        #region 属性

        /// <summary>
        /// 设备ID
        /// </summary>
        public string DeviceId => _device?.Id ?? string.Empty;

        /// <summary>
        /// 设备名称
        /// </summary>
        public string DeviceName => _device?.Name ?? "ART-SWITCH";

        /// <summary>
        /// 是否已连接
        /// </summary>
        public bool IsConnected => _isConnected;

        public string CurrentTopology
        {
            get => _currentTopology;
            set => SetProperty(ref _currentTopology, value);
        }

        /// <summary>
        /// 设备号（如 "Dev2"）
        /// </summary>
        public string ResourceName => _resourceName;

        /// <summary>
        /// 是否为模拟驱动（真实硬件驱动，固定返回false）
        /// </summary>
        public bool IsSimulated => false;  // 这里直接返回false

        /// <summary>
        /// ArtSwitch是开关控制设备
        /// </summary>
        public DeviceCapability Capability => DeviceCapability.Other;


        #endregion

        #region 构造函数

        /// <summary>
        /// 创建 ART-SWITCH 驱动实例
        /// </summary>
        /// <param name="device">设备实例</param>
        /// <param name="resourceName">设备号（如 "Dev2"）</param>
        /// <param name="slotNumber">插槽号（可选，默认0）</param>
        public ArtSwitchDriver(DeviceBase device, string resourceName, int slotNumber = 0)
        {
            _device = device ?? throw new ArgumentNullException(nameof(device));
            _resourceName = resourceName ?? throw new ArgumentNullException(nameof(resourceName));
            //_resourceName = "Dev1";
            _switchSession = artSwitch.VI_NULL;
            _slotNumber = slotNumber;
            _isConnected = false;
            _currentTopology = string.Empty;
        }

        #endregion

        #region IDeviceDriver 实现

        /// <summary>
        /// 连接设备（使用默认拓扑）
        /// </summary>
        public async Task<bool> ConnectAsync()
        {
            return await ConnectWithTopologyAsync(artSwitchTopologies.ARTSWITCH_TOPOLOGY_CONFIGURED_TOPOLOGY);
        }


        /// <summary>
        /// 使用指定拓扑连接设备（新添加的重载方法）
        /// </summary>
        /// <param name="topology">拓扑结构字符串</param>
        public async Task<bool> ConnectAsync(string topology)
        {
            return await ConnectWithTopologyAsync(topology);
        }


        /// <summary>
        /// 使用指定拓扑连接设备
        /// </summary>
        public async Task<bool> ConnectWithTopologyAsync(string topology)
        {
            try
            {
                Debug.WriteLine($"[ArtSwitchDriver] 正在连接设备 {DeviceName}, 设备号: {_resourceName}, 拓扑: {topology}");

                // 转换为非托管字符串
                IntPtr topologyPtr = Marshal.StringToHGlobalAnsi(topology);

                // 打开会话并设置拓扑
                int switchError = artSwitch.artSwitch_InitWithTopology(
                    _resourceName,
                    topologyPtr,
                    (ushort)artSwitch.VI_FALSE,
                    (ushort)artSwitch.VI_TRUE,
                    ref _switchSession);

                Marshal.FreeHGlobal(topologyPtr);

                // 关键：输出错误码
                Debug.WriteLine($"[ArtSwitchDriver] API返回错误码: {switchError} (0x{switchError:X8})");

                if (switchError < artSwitch.VI_SUCCESS)
                {
                    string error = GetHardwareError(_switchSession);
                    Debug.WriteLine($"[ArtSwitchDriver] 连接失败，错误: {error}");
                    _isConnected = false;
                    return false;
                }

                _isConnected = true;
                _currentTopology = topology;
                Debug.WriteLine($"[ArtSwitchDriver] 设备连接成功，会话ID: {_switchSession}");
                await Task.Yield(); // 确保方法真正异步
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ArtSwitchDriver] 连接失败: {ex.Message}");
                _isConnected = false;
                throw;
            }
        }
        /// <summary>
        /// 断开连接
        /// </summary>
        public async Task<bool> DisconnectAsync()
        {
            try
            {
                Debug.WriteLine($"[ArtSwitchDriver] 正在断开设备 {DeviceName}");

                if (_switchSession != artSwitch.VI_NULL)
                {
                    // 断开所有连接
                    artSwitch.artSwitch_DisconnectAll(_switchSession);

                    // 关闭会话
                    artSwitch.artSwitch_close(_switchSession);
                    _switchSession = artSwitch.VI_NULL;
                }

                _isConnected = false;
                _currentTopology = string.Empty;
                Debug.WriteLine($"[ArtSwitchDriver] 设备断开成功");
                await Task.Yield(); // 确保方法真正异步
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ArtSwitchDriver] 断开失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 将硬件状态转换为SwitchConnectionState
        /// </summary>
        private SwitchConnectionState ConvertToConnectionState(bool hardwareState, bool hasError = false)
        {
            if (hasError)
                return SwitchConnectionState.Error;

            return hardwareState ? SwitchConnectionState.Connected : SwitchConnectionState.Disconnected;
        }

        /// <summary>
        /// 将SwitchConnectionState转换为硬件状态
        /// </summary>
        private bool ConvertFromConnectionState(SwitchConnectionState state)
        {
            return state == SwitchConnectionState.Connected;
        }

        private void NormalizeColumnRow(string channel1, string channel2, out string column, out string row)
        {
            // 约定：channel1=列(c*)，channel2=行(r*)。
            // 但如果调用方传反（r*, c*），这里会自动纠正。
            column = channel1;
            row = channel2;

            if (string.IsNullOrWhiteSpace(channel1) || string.IsNullOrWhiteSpace(channel2))
            {
                return;
            }

            bool ch1IsColumn = channel1.StartsWith("c", StringComparison.OrdinalIgnoreCase);
            bool ch1IsRow = channel1.StartsWith("r", StringComparison.OrdinalIgnoreCase);
            bool ch2IsColumn = channel2.StartsWith("c", StringComparison.OrdinalIgnoreCase);
            bool ch2IsRow = channel2.StartsWith("r", StringComparison.OrdinalIgnoreCase);

            if (ch1IsRow && ch2IsColumn)
            {
                column = channel2;
                row = channel1;
                return;
            }

            if (ch1IsColumn && ch2IsRow)
            {
                return;
            }
        }

        /// <summary>
        /// 读取连接状态（适配新的枚举）
        /// </summary>
        public async Task<SwitchConnectionState> ReadConnectionStateAsync(string input, string output)
        {
            try
            {
                if (!_isConnected)
                {
                    Debug.WriteLine($"[ArtSwitchDriver] 设备未连接，无法读取连接状态 {input}->{output}");
                    return SwitchConnectionState.Error;
                }

                // 真实硬件读取逻辑
                try
                {
                    // 用 GetPath 判断是否存在路径（存在表示已连接）
                    NormalizeColumnRow(input, output, out string column, out string row);

                    byte[] pathBuffer = new byte[1024];
                    int result = artSwitch.artSwitch_GetPath(_switchSession, column, row, pathBuffer.Length, pathBuffer);
                    if (result < artSwitch.VI_SUCCESS)
                    {
                        return SwitchConnectionState.Error;
                    }

                    string path = Encoding.ASCII.GetString(pathBuffer).TrimEnd('\0', ' ', '\r', '\n', '\t');
                    bool isConnected = !string.IsNullOrWhiteSpace(path);
                    return ConvertToConnectionState(isConnected);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ArtSwitchDriver] 读取连接状态失败 {input}->{output}: {ex.Message}");
                    return SwitchConnectionState.Error;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ArtSwitchDriver] 读取连接状态异常 {input}->{output}: {ex.Message}");
                await Task.Yield(); // 确保方法真正异步
                return SwitchConnectionState.Error;
            }
        }

        /// <summary>
        /// 批量读取连接状态
        /// </summary>
        public async Task<Dictionary<string, SwitchConnectionState>> ReadConnectionStatesBatchAsync(
            IEnumerable<string> inputs, IEnumerable<string> outputs)
        {
            var results = new Dictionary<string, SwitchConnectionState>();

            if (!_isConnected)
            {
                foreach (var input in inputs)
                {
                    foreach (var output in outputs)
                    {
                        results[$"{input}->{output}"] = SwitchConnectionState.Error;
                    }
                }

                return results;
            }

            try
            {
                foreach (var input in inputs)
                {
                    foreach (var output in outputs)
                    {
                        var state = await ReadConnectionStateAsync(input, output);
                        results[$"{input}->{output}"] = state;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ArtSwitchDriver] 批量读取连接状态失败: {ex.Message}");
                foreach (var input in inputs)
                {
                    foreach (var output in outputs)
                    {
                        var key = $"{input}->{output}";
                        if (!results.ContainsKey(key))
                        {
                            results[key] = SwitchConnectionState.Error;
                        }
                    }
                }
            }

            return results;
        }

        public async Task<bool> ConnectChannelsAsync(string channel1, string channel2)
        {
            if (!_isConnected)
            {
                Debug.WriteLine($"[ArtSwitchDriver] 设备未连接，无法连接通道");
                return false;
            }

            try
            {
                NormalizeColumnRow(channel1, channel2, out string column, out string row);
                int ret = artSwitch.artSwitch_Connect(_switchSession, column, row);
                if (ret < artSwitch.VI_SUCCESS)
                {
                    string error = GetHardwareError(_switchSession);
                    Debug.WriteLine($"[ArtSwitchDriver] 连接通道失败 {column}<->{row}: {error}");
                    return false;
                }

                await Task.Yield();
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ArtSwitchDriver] 连接通道异常 {channel1}<->{channel2}: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> ConnectChannelsWithoutDisconnectAsync(string channel1, string channel2)
        {
            if (!_isConnected)
            {
                Debug.WriteLine($"[ArtSwitchDriver] 设备未连接，无法连接通道");
                return false;
            }

            try
            {
                NormalizeColumnRow(channel1, channel2, out string column, out string row);
                int ret = artSwitch.artSwitch_Connect(_switchSession, column, row);
                if (ret < artSwitch.VI_SUCCESS)
                {
                    string error = GetHardwareError(_switchSession);
                    if (!string.IsNullOrEmpty(error) && (error.Contains("already exists") || error.Contains("0xBFFA200C")))
                    {
                        return true;
                    }

                    Debug.WriteLine($"[ArtSwitchDriver] 连接通道失败 {column}<->{row}: {error}");
                    return false;
                }

                await Task.Yield();
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ArtSwitchDriver] 连接通道异常 {channel1}<->{channel2}: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> DisconnectChannelsAsync(string channel1, string channel2)
        {
            if (!_isConnected)
            {
                Debug.WriteLine($"[ArtSwitchDriver] 设备未连接，无法断开通道");
                return false;
            }

            try
            {
                NormalizeColumnRow(channel1, channel2, out string column, out string row);
                int ret = artSwitch.artSwitch_Disconnect(_switchSession, column, row);
                if (ret < artSwitch.VI_SUCCESS)
                {
                    string error = GetHardwareError(_switchSession);
                    Debug.WriteLine($"[ArtSwitchDriver] 断开通道失败 {column}<->{row}: {error}");
                    return false;
                }

                await Task.Yield();
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ArtSwitchDriver] 断开通道异常 {channel1}<->{channel2}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 断开单个连接（新方法，只断开指定连接）
        /// </summary>
        public async Task<bool> DisconnectSingleConnectionAsync(string channel1, string channel2)
        {
            return await DisconnectChannelsAsync(channel1, channel2);
        }

        /// <summary>
        /// 断开所有连接
        /// </summary>
        public async Task<bool> DisconnectAllAsync()
        {
            if (!_isConnected)
            {
                Debug.WriteLine($"[ArtSwitchDriver] 设备未连接");
                return false;
            }

            try
            {
                Debug.WriteLine($"[ArtSwitchDriver] 断开所有连接");

                int switchError = artSwitch.artSwitch_DisconnectAll(_switchSession);

                if (switchError < artSwitch.VI_SUCCESS)
                {
                    string error = GetHardwareError(_switchSession);
                    Debug.WriteLine($"[ArtSwitchDriver] 断开所有连接失败，错误: {error}");
                    return false;
                }

                Debug.WriteLine($"[ArtSwitchDriver] 所有连接已断开");
                await Task.Yield(); // 确保方法真正异步
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ArtSwitchDriver] 断开所有连接失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 读取单个通道状态（返回连接到的通道ID，空字符串表示未连接）
        /// </summary>
        public async Task<string> GetChannelConnectionAsync(string channelId)
        {
            if (!_isConnected)
            {
                Debug.WriteLine($"[ArtSwitchDriver] 设备未连接，无法读取通道状态");
                return string.Empty;
            }

            try
            {
                // ART-SWITCH API 通常不提供直接查询通道连接状态的方法
                // 这里需要根据拓扑结构来判断可能的连接
                Debug.WriteLine($"[ArtSwitchDriver] 当前API不支持直接查询通道连接状态");
                await Task.Yield(); // 确保方法真正异步
                return string.Empty;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ArtSwitchDriver] 读取通道状态失败: {ex.Message}");
                return string.Empty;
            }
        }

        /// <summary>
        /// 读取单个通道（IDeviceDriver接口实现）
        /// </summary>
        public async Task<double> ReadChannelAsync(string channelId)
        {
            // 对于开关设备，读取连接状态（0=未连接，1=已连接）
            // 由于无法直接查询，需要根据应用逻辑实现
            try
            {
                // 检查该通道是否连接到任何其他通道
                string connectedChannel = await GetChannelConnectionAsync(channelId);
                return string.IsNullOrEmpty(connectedChannel) ? 0 : 1;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// 批量读取通道
        /// </summary>
        public async Task<Dictionary<string, double>> ReadChannelsBatchAsync(IEnumerable<string> channelIds)
        {
            var results = new Dictionary<string, double>();

            if (!_isConnected)
            {
                foreach (var channelId in channelIds)
                {
                    results[channelId] = 0;
                }
                return results;
            }

            try
            {
                // 真实硬件模式
                // 由于硬件限制，可能只能逐个读取
                foreach (var channelId in channelIds)
                {
                    double value = await ReadChannelAsync(channelId);
                    results[channelId] = value;
                }

                return results;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ArtSwitchDriver] 批量读取失败: {ex.Message}");
                foreach (var channelId in channelIds)
                {
                    if (!results.ContainsKey(channelId))
                    {
                        results[channelId] = 0;
                    }
                }
                return results;
            }
        }

        /// <summary>
        /// 写入单个通道（IDeviceDriver接口实现）
        /// 对于开关设备，写入操作对应连接/断开操作
        /// value 大于 0.5: 连接到默认通道
        /// value 小于等于 0.5: 断开连接
        /// </summary>
        public async Task<bool> WriteChannelAsync(string channelId, double value)
        {
            if (!_isConnected)
            {
                Debug.WriteLine($"[ArtSwitchDriver] 设备未连接，无法写入通道");
                return false;
            }

            try
            {
                // 这里需要根据具体应用逻辑确定要连接的通道
                // 示例：假设默认连接到通道0
                string defaultTargetChannel = "ch0";

                if (value > 0.5)
                {
                    // 使用新方法，避免断开现有连接
                    return await ConnectChannelsWithoutDisconnectAsync(channelId, defaultTargetChannel);
                }
                else
                {
                    // 使用新方法，只断开指定连接
                    return await DisconnectSingleConnectionAsync(channelId, defaultTargetChannel);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ArtSwitchDriver] 写入通道失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 批量写入通道
        /// </summary>
        public async Task<bool> WriteChannelsBatchAsync(Dictionary<string, double> channelValues)
        {
            bool allSuccess = true;

            foreach (var kvp in channelValues)
            {
                if (!await WriteChannelAsync(kvp.Key, kvp.Value))
                {
                    allSuccess = false;
                }
            }

            return allSuccess;
        }

        /// <summary>
        /// 配置通道（开关设备通常不需要复杂配置）
        /// </summary>
        public async Task<bool> ConfigureChannelAsync(string channelId, Dictionary<string, object> config)
        {
            Debug.WriteLine($"[ArtSwitchDriver] 配置通道 {channelId}");
            // ART-SWITCH 通道配置主要在拓扑设置时完成
            await Task.Yield(); // 确保方法真正异步
            return true;
        }

        /// <summary>
        /// 启动采集（开关设备通常不需要连续采集）
        /// </summary>
        public async Task<bool> StartAcquisitionAsync()
        {
            Debug.WriteLine($"[ArtSwitchDriver] 启动采集（开关设备通常不需要连续采集）");
            await Task.Yield(); // 确保方法真正异步
            return true;
        }

        /// <summary>
        /// 停止采集
        /// </summary>
        public async Task<bool> StopAcquisitionAsync()
        {
            Debug.WriteLine($"[ArtSwitchDriver] 停止采集");
            await Task.Yield(); // 确保方法真正异步
            return true;
        }

        /// <summary>
        /// 获取设备状态
        /// </summary>
        public async Task<Dictionary<string, object>> GetStatusAsync()
        {
            var status = new Dictionary<string, object>
            {
                { "IsConnected", _isConnected },
                { "ResourceName", _resourceName },
                { "CurrentTopology", _currentTopology },
                { "SessionHandle", _switchSession },
                { "DriverVersion", "1.0.0" },
                { "LastOperation", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") }
            };

            await Task.Yield(); // 确保方法真正异步
            return status;
        }

        /// <summary>
        /// 重置设备（断开所有连接）
        /// </summary>
        public async Task<bool> ResetAsync()
        {
            Debug.WriteLine($"[ArtSwitchDriver] 重置设备");
            return await DisconnectAllAsync();
        }

        /// <summary>
        /// 自检
        /// </summary>
        public async Task<bool> SelfTestAsync()
        {
            try
            {
                Debug.WriteLine($"[ArtSwitchDriver] 执行自检");

                if (!_isConnected)
                {
                    Debug.WriteLine($"[ArtSwitchDriver] 设备未连接，自检失败");
                    return false;
                }

                short selfTestResult = 0;
                byte[] selfTestMessage = new byte[256];

                int result = artSwitch.artSwitch_self_test(_switchSession, ref selfTestResult, selfTestMessage);

                if (result < artSwitch.VI_SUCCESS)
                {
                    string error = GetHardwareError(_switchSession);
                    Debug.WriteLine($"[ArtSwitchDriver] 自检失败: {error}");
                    return false;
                }

                string message = System.Text.Encoding.ASCII.GetString(selfTestMessage).TrimEnd('\0');
                Debug.WriteLine($"[ArtSwitchDriver] 自检结果: {selfTestResult}, 消息: {message}");

                await Task.Yield(); // 确保方法真正异步
                return selfTestResult == 0; // 0 表示通过
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ArtSwitchDriver] 自检失败: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region 辅助方法

        /// <summary>
        /// 获取支持的拓扑列表
        /// </summary>
        public static List<string> GetSupportedTopologies()
        {
            var topologies = new List<string>
            {
                artSwitchTopologies.ARTSWITCH_TOPOLOGY_CONFIGURED_TOPOLOGY,
                artSwitchTopologies.ARTSWITCH_TOPOLOGY_2601_2_WIRE_4X32_MATRIX,
                artSwitchTopologies.ARTSWITCH_TOPOLOGY_2601_2_WIRE_8X16_MATRIX,
                artSwitchTopologies.ARTSWITCH_TOPOLOGY_2601_2_WIRE_DUAL_4X16_MATRIX,
                artSwitchTopologies.ARTSWITCH_TOPOLOGY_2602_1_WIRE_64X1_MUX,
            };

            return topologies;
        }

        /// <summary>
        /// 获取连接对应的继电器名称（针对4x32矩阵）
        /// </summary>
        private string GetRelayNameForConnection(string input, string output)
        {
            try
            {
                // 对于4x32矩阵，通道格式：r0, r1, r2, r3 和 c0, c1, ..., c31
                if (input.StartsWith("r") && output.StartsWith("c"))
                {
                    int inputIndex = int.Parse(input.Substring(1));
                    int outputIndex = int.Parse(output.Substring(1));

                    if (!string.IsNullOrWhiteSpace(_currentTopology) &&
                        _currentTopology.IndexOf("8x16", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        // 8x16：按行分板卡
                        // r0-r3 -> boardIndex=0, r4-r7 -> boardIndex=1
                        if (inputIndex < 0 || inputIndex > 7 || outputIndex < 0 || outputIndex > 15)
                            return string.Empty;

                        int boardIndex = inputIndex / 4;
                        int localRowIndex = inputIndex % 4;
                        return $"b{boardIndex}r{localRowIndex}c{outputIndex}";
                    }

                    if (!string.IsNullOrWhiteSpace(_currentTopology) &&
                        _currentTopology.IndexOf("4x32", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        // 4x32：按列分板卡
                        // c0-c15 -> boardIndex=0, c16-c31 -> boardIndex=1
                        if (inputIndex < 0 || inputIndex > 3 || outputIndex < 0 || outputIndex > 31)
                            return string.Empty;

                        int boardIndex = outputIndex / 16;
                        int localColIndex = outputIndex % 16;
                        return $"b{boardIndex}r{inputIndex}c{localColIndex}";
                    }

                    // 未识别拓扑：保持原有默认行为
                    return $"b1r{inputIndex}c{outputIndex}";
                }

                // 对于其他拓扑结构，可能需要不同的计算方式
                return $"b1{input}{output}";
            }
            catch
            {
                // 解析失败，返回空
                return string.Empty;
            }
        }

        /// <summary>
        /// 获取硬件错误信息
        /// </summary>
        private string GetHardwareError(uint session)
        {
            try
            {
                int errorNumber = 0;
                int bufferSize = artSwitch.artSwitch_GetError(session, ref errorNumber, 0, null);

                if (bufferSize > 0)
                {
                    byte[] buffer = new byte[bufferSize + 256];
                    artSwitch.artSwitch_GetError(session, ref errorNumber, bufferSize, buffer);

                    string errorMessage = System.Text.Encoding.Default.GetString(buffer).TrimEnd('\0');
                    return $"错误代码: 0x{errorNumber:X} - {errorMessage}";
                }

                return "未知错误";
            }
            catch
            {
                return "获取错误信息失败";
            }
        }

        #endregion
    }
}