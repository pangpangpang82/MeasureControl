using MeasureControl.Drivers.ArtSwitch;
using MeasureControl.Models;
using MeasureControl.Models.Devices;
using Prism.Mvvm;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace MeasureControl.Drivers.ArtSwitch
{
    /// <summary>
    /// ART-SWITCH 继电器矩阵模拟驱动（用于测试）
    /// 完全模拟硬件行为，包括连接状态、错误处理、拓扑管理等
    /// </summary>
    public class SimulatedArtSwitchDriver : BindableBase, IDeviceDriver
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

        #region 私有字段和常量

        private readonly DeviceBase _device;
        private readonly string _resourceName;
        private readonly int _slotNumber;  // 添加 slotNumber 字段
        private bool _isConnected;
        private string _currentTopology;

        // 模拟的连接状态存储
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, bool>> _connectionMatrix;
        private readonly ConcurrentDictionary<string, string> _channelConnections;
        private readonly ConcurrentDictionary<string, int> _relayCounters;

        // 错误模拟
        private readonly Random _random = new Random();
        private double _errorProbability = 0.05; // 5% 的随机错误概率

        // 模拟延迟
        private readonly int _simulationDelayMs = 50;

        // 支持的拓扑配置
        private readonly Dictionary<string, (int Inputs, int Outputs)> _supportedTopologies = new()
        {
            { artSwitchTopologies.ARTSWITCH_TOPOLOGY_CONFIGURED_TOPOLOGY, (4, 32) },
            { artSwitchTopologies.ARTSWITCH_TOPOLOGY_2601_2_WIRE_4X32_MATRIX, (4, 32) },
            { artSwitchTopologies.ARTSWITCH_TOPOLOGY_2601_2_WIRE_8X16_MATRIX, (8, 16) },
            { artSwitchTopologies.ARTSWITCH_TOPOLOGY_2601_2_WIRE_DUAL_4X16_MATRIX, (4, 16) },
            { artSwitchTopologies.ARTSWITCH_TOPOLOGY_2602_1_WIRE_64X1_MUX, (64, 1) }
        };

        #endregion

        #region 属性

        /// <summary>
        /// 设备ID
        /// </summary>
        public string DeviceId => _device?.Id ?? string.Empty;

        /// <summary>
        /// 设备名称
        /// </summary>
        public string DeviceName => _device?.Name ?? "ART-SWITCH (模拟)";

        /// <summary>
        /// 是否已连接
        /// </summary>
        public bool IsConnected
        {
            get => _isConnected;
            private set => SetProperty(ref _isConnected, value);
        }

        /// <summary>
        /// 当前拓扑
        /// </summary>
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
        /// 是否为模拟驱动
        /// </summary>
        public bool IsSimulated => true;

        /// <summary>
        /// SimulatedArtSwitch是模拟开关控制设备
        /// </summary>
        public DeviceCapability Capability => DeviceCapability.Other;

        /// <summary>
        /// 获取或设置模拟错误概率（0-1）
        /// </summary>
        public double ErrorProbability
        {
            get => _errorProbability;
            set => _errorProbability = Math.Max(0, Math.Min(1, value));
        }

        /// <summary>
        /// 获取或设置模拟延迟（毫秒）
        /// </summary>
        public int SimulationDelayMs
        {
            get => _simulationDelayMs;
        }

        #endregion

        #region 构造函数

        /// <summary>
        /// 创建 ART-SWITCH 模拟驱动实例
        /// </summary>
        /// <param name="device">设备实例</param>
        /// <param name="resourceName">设备号</param>
        /// <param name="slotNumber">插槽号</param>
        public SimulatedArtSwitchDriver(DeviceBase device, string resourceName, int slotNumber = 0)
        {
            _device = device ?? throw new ArgumentNullException(nameof(device));
            _resourceName = resourceName ?? throw new ArgumentNullException(nameof(resourceName));
            _slotNumber = slotNumber;

            _connectionMatrix = new ConcurrentDictionary<string, ConcurrentDictionary<string, bool>>();
            _channelConnections = new ConcurrentDictionary<string, string>();
            _relayCounters = new ConcurrentDictionary<string, int>();

            IsConnected = false;
            CurrentTopology = string.Empty;

            Debug.WriteLine($"[SimulatedArtSwitchDriver] 模拟驱动已创建: {DeviceName} ({resourceName}, Slot={slotNumber})");
        }

        #endregion

        #region IDeviceDriver 实现

        /// <summary>
        /// 连接设备（使用默认拓扑）
        /// </summary>
        public async Task<bool> ConnectAsync()
        {
            return await ConnectAsync(artSwitchTopologies.ARTSWITCH_TOPOLOGY_2601_2_WIRE_4X32_MATRIX);
        }

        /// <summary>
        /// 使用指定拓扑连接设备
        /// </summary>
        /// <param name="topology">拓扑结构字符串</param>
        public async Task<bool> ConnectAsync(string topology)
        {
            try
            {
                await Task.Delay(_simulationDelayMs); // 模拟连接延迟

                Debug.WriteLine($"[SimulatedArtSwitchDriver] 正在连接设备 {DeviceName}, 拓扑: {topology}");

                // 检查拓扑是否支持
                if (!_supportedTopologies.ContainsKey(topology))
                {
                    Debug.WriteLine($"[SimulatedArtSwitchDriver] 不支持的拓扑: {topology}");
                    return false;
                }

                // 初始化连接矩阵
                var config = _supportedTopologies[topology];
                InitializeConnectionMatrix(config.Inputs, config.Outputs);

                IsConnected = true;
                CurrentTopology = topology;

                Debug.WriteLine($"[SimulatedArtSwitchDriver] 模拟设备连接成功，拓扑: {topology} ({config.Inputs}x{config.Outputs})");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SimulatedArtSwitchDriver] 连接失败: {ex.Message}");
                IsConnected = false;
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
                await Task.Delay(_simulationDelayMs); // 模拟断开延迟

                Debug.WriteLine($"[SimulatedArtSwitchDriver] 正在断开设备 {DeviceName}");

                // 断开所有连接
                await DisconnectAllAsync();

                // 清空状态
                _connectionMatrix.Clear();
                _channelConnections.Clear();
                _relayCounters.Clear();

                IsConnected = false;
                CurrentTopology = string.Empty;

                Debug.WriteLine($"[SimulatedArtSwitchDriver] 模拟设备断开成功");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SimulatedArtSwitchDriver] 断开失败: {ex.Message}");
                return false;
            }
        }

        // 以下是 ArtSwitch 特有的方法，但 SwitchControlPanelViewModel 需要它们
        // 所以我们需要实现这些方法

        /// <summary>
        /// 连接两个通道
        /// </summary>
        public async Task<bool> ConnectChannelsAsync(string channel1, string channel2)
        {
            if (!IsConnected)
            {
                Debug.WriteLine($"[SimulatedArtSwitchDriver] 设备未连接，无法连接通道");
                return false;
            }

            try
            {
                await Task.Delay(_simulationDelayMs); // 模拟连接延迟

                // 跳过通道格式检查，直接接受所有格式的通道名称
                //if (!IsValidChannel(channel1) || !IsValidChannel(channel2))
                //{
                //    Debug.WriteLine($"[SimulatedArtSwitchDriver] 无效的通道格式: {channel1} 或 {channel2}");
                //    return false;
                //}

                // 模拟随机连接失败
                if (_random.NextDouble() < ErrorProbability)
                {
                    Debug.WriteLine($"[SimulatedArtSwitchDriver] 模拟连接失败: {channel1} <-> {channel2}");
                    return false;
                }

                // 确定输入和输出（支持IN/OUT和r/c格式）
                string input, output;
                if ((channel1.StartsWith("IN") || channel1.StartsWith("r")) && (channel2.StartsWith("OUT") || channel2.StartsWith("c")))
                {
                    input = channel1;
                    output = channel2;
                }
                else if ((channel2.StartsWith("IN") || channel2.StartsWith("r")) && (channel1.StartsWith("OUT") || channel1.StartsWith("c")))
                {
                    input = channel2;
                    output = channel1;
                }
                else
                {
                    Debug.WriteLine($"[SimulatedArtSwitchDriver] 无效的通道组合: {channel1} <-> {channel2}");
                    return false;
                }

                // 检查是否已经连接
                if (GetConnectionState(input, output))
                {
                    Debug.WriteLine($"[SimulatedArtSwitchDriver] 通道已连接: {input} <-> {output}");
                    return true;
                }

                // 对于矩阵开关，检查输出是否已经被其他输入占用
                // 真实硬件可能支持多路复用，这里模拟独占连接
                var config = _supportedTopologies[CurrentTopology];
                if (IsOutputExclusive(output))
                {
                    // 断开该输出上的现有连接
                    var existingInput = GetConnectedInput(output);
                    if (!string.IsNullOrEmpty(existingInput))
                    {
                        await DisconnectChannelsAsync(existingInput, output);
                    }
                }

                // 设置连接状态
                SetConnectionState(input, output, true);

                // 更新通道连接映射
                _channelConnections[input] = output;
                _channelConnections[output] = input;

                // 增加继电器计数器
                string relayKey = $"{input}_{output}";
                _relayCounters.AddOrUpdate(relayKey, 1, (key, oldValue) => oldValue + 1);

                Debug.WriteLine($"[SimulatedArtSwitchDriver] 通道连接成功: {input} <-> {output}");
                Debug.WriteLine($"[SimulatedArtSwitchDriver] 继电器 {relayKey} 操作次数: {_relayCounters[relayKey]}");

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SimulatedArtSwitchDriver] 连接通道失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 断开两个通道
        /// </summary>
        public async Task<bool> DisconnectChannelsAsync(string channel1, string channel2)
        {
            if (!IsConnected)
            {
                Debug.WriteLine($"[SimulatedArtSwitchDriver] 设备未连接，无法断开通道");
                return false;
            }

            try
            {
                await Task.Delay(_simulationDelayMs); // 模拟断开延迟

                // 模拟随机断开失败
                if (_random.NextDouble() < ErrorProbability)
                {
                    Debug.WriteLine($"[SimulatedArtSwitchDriver] 模拟断开失败: {channel1} <-> {channel2}");
                    return false;
                }

                // 确定输入和输出
                string input, output;
                if ((channel1.StartsWith("IN") || channel1.StartsWith("r")) && (channel2.StartsWith("OUT") || channel2.StartsWith("c")))
                {
                    input = channel1;
                    output = channel2;
                }
                else if ((channel2.StartsWith("IN") || channel2.StartsWith("r")) && (channel1.StartsWith("OUT") || channel1.StartsWith("c")))
                {
                    input = channel2;
                    output = channel1;
                }
                else
                {
                    Debug.WriteLine($"[SimulatedArtSwitchDriver] 无效的通道组合: {channel1} <-> {channel2}");
                    return false;
                }

                // 检查是否已经断开
                if (!GetConnectionState(input, output))
                {
                    Debug.WriteLine($"[SimulatedArtSwitchDriver] 通道已断开: {input} <-> {output}");
                    return true;
                }

                // 断开连接
                SetConnectionState(input, output, false);

                // 更新通道连接映射
                _channelConnections.TryRemove(input, out _);
                _channelConnections.TryRemove(output, out _);

                Debug.WriteLine($"[SimulatedArtSwitchDriver] 通道断开成功: {input} <-> {output}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SimulatedArtSwitchDriver] 断开通道失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 断开所有连接
        /// </summary>
        public async Task<bool> DisconnectAllAsync()
        {
            if (!IsConnected)
            {
                Debug.WriteLine($"[SimulatedArtSwitchDriver] 设备未连接");
                return false;
            }

            try
            {
                await Task.Delay(_simulationDelayMs * 2); // 模拟批量断开延迟

                Debug.WriteLine($"[SimulatedArtSwitchDriver] 正在断开所有连接");

                // 遍历所有连接并断开
                var config = _supportedTopologies[CurrentTopology];

                for (int i = 0; i < config.Inputs; i++)
                {
                    string input = $"IN{i}";

                    if (_connectionMatrix.TryGetValue(input, out var outputs))
                    {
                        for (int j = 0; j < config.Outputs; j++)
                        {
                            string output = $"OUT{j}";
                            outputs[output] = false;
                        }
                    }
                }

                // 清空通道连接映射
                _channelConnections.Clear();

                Debug.WriteLine($"[SimulatedArtSwitchDriver] 所有连接已断开");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SimulatedArtSwitchDriver] 断开所有连接失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 读取单个通道值（IDeviceDriver接口要求）
        /// </summary>
        public async Task<double> ReadChannelAsync(string channelId)
        {
            try
            {
                await Task.Delay(_simulationDelayMs / 2); // 模拟读取延迟

                // 检查该通道是否连接到任何其他通道
                string connectedChannel = await GetChannelConnectionAsync(channelId);
                double value = string.IsNullOrEmpty(connectedChannel) ? 0 : 1;

                // 添加一些随机噪声
                value += (_random.NextDouble() - 0.5) * 0.01;

                Debug.WriteLine($"[SimulatedArtSwitchDriver] 读取通道 {channelId} = {value:F3}");
                return value;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SimulatedArtSwitchDriver] 读取通道失败: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// 批量读取通道（IDeviceDriver接口要求）
        /// </summary>
        public async Task<Dictionary<string, double>> ReadChannelsBatchAsync(IEnumerable<string> channelIds)
        {
            var results = new Dictionary<string, double>();

            if (!IsConnected)
            {
                foreach (var channelId in channelIds)
                {
                    results[channelId] = 0;
                }
                return results;
            }

            try
            {
                await Task.Delay(_simulationDelayMs); // 模拟批量读取延迟

                foreach (var channelId in channelIds)
                {
                    double value = await ReadChannelAsync(channelId);
                    results[channelId] = value;
                }

                Debug.WriteLine($"[SimulatedArtSwitchDriver] 批量读取完成: {results.Count} 个通道");
                return results;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SimulatedArtSwitchDriver] 批量读取失败: {ex.Message}");
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
        /// 写入单个通道（IDeviceDriver接口要求）
        /// </summary>
        public async Task<bool> WriteChannelAsync(string channelId, double value)
        {
            if (!IsConnected)
            {
                Debug.WriteLine($"[SimulatedArtSwitchDriver] 设备未连接，无法写入通道");
                return false;
            }

            try
            {
                await Task.Delay(_simulationDelayMs); // 模拟写入延迟

                // 这里模拟写入操作，根据具体应用逻辑确定要连接的通道
                // 示例：如果写入值 > 0.5，则连接到默认通道
                if (value > 0.5)
                {
                    // 查找一个可用的输出通道
                    var config = _supportedTopologies[CurrentTopology];
                    string defaultTargetChannel = "OUT0";

                    // 尝试找到一个未连接的输出
                    for (int i = 0; i < config.Outputs; i++)
                    {
                        string output = $"OUT{i}";
                        if (string.IsNullOrEmpty(await GetChannelConnectionAsync(output)))
                        {
                            defaultTargetChannel = output;
                            break;
                        }
                    }

                    return await ConnectChannelsAsync(channelId, defaultTargetChannel);
                }
                else
                {
                    // 断开该通道的所有连接
                    string connectedChannel = await GetChannelConnectionAsync(channelId);
                    if (!string.IsNullOrEmpty(connectedChannel))
                    {
                        return await DisconnectChannelsAsync(channelId, connectedChannel);
                    }
                    return true; // 已经断开
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SimulatedArtSwitchDriver] 写入通道失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 批量写入通道（IDeviceDriver接口要求）
        /// </summary>
        public async Task<bool> WriteChannelsBatchAsync(Dictionary<string, double> channelValues)
        {
            bool allSuccess = true;

            foreach (var kvp in channelValues)
            {
                if (!await WriteChannelAsync(kvp.Key, kvp.Value))
                {
                    allSuccess = false;
                    Debug.WriteLine($"[SimulatedArtSwitchDriver] 批量写入失败: {kvp.Key}");
                }
            }

            return allSuccess;
        }

        /// <summary>
        /// 配置通道（IDeviceDriver接口要求）
        /// </summary>
        public async Task<bool> ConfigureChannelAsync(string channelId, Dictionary<string, object> config)
        {
            Debug.WriteLine($"[SimulatedArtSwitchDriver] 配置通道 {channelId}");
            await Task.Delay(_simulationDelayMs / 2);
            return true;
        }

        /// <summary>
        /// 启动采集（IDeviceDriver接口要求）
        /// </summary>
        public async Task<bool> StartAcquisitionAsync()
        {
            Debug.WriteLine($"[SimulatedArtSwitchDriver] 启动采集");
            await Task.Delay(_simulationDelayMs);
            return true;
        }

        /// <summary>
        /// 停止采集（IDeviceDriver接口要求）
        /// </summary>
        public async Task<bool> StopAcquisitionAsync()
        {
            Debug.WriteLine($"[SimulatedArtSwitchDriver] 停止采集");
            await Task.Delay(_simulationDelayMs / 2);
            return true;
        }

        /// <summary>
        /// 获取设备状态（IDeviceDriver接口要求）
        /// </summary>
        public async Task<Dictionary<string, object>> GetStatusAsync()
        {
            var status = new Dictionary<string, object>
            {
                { "IsConnected", IsConnected },
                { "ResourceName", ResourceName },
                { "CurrentTopology", CurrentTopology },
                { "DriverType", "Simulated" },
                { "DriverVersion", "1.0.0-Simulated" },
                { "ActiveConnections", GetActiveConnectionCount() },
                { "TotalRelays", GetTotalRelayCount() },
                { "ErrorProbability", ErrorProbability },
                { "SimulationDelayMs", SimulationDelayMs },
                { "LastOperation", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") }
            };

            await Task.Delay(10);
            return status;
        }

        /// <summary>
        /// 重置设备（IDeviceDriver接口要求）
        /// </summary>
        public async Task<bool> ResetAsync()
        {
            Debug.WriteLine($"[SimulatedArtSwitchDriver] 重置设备");
            return await DisconnectAllAsync();
        }

        /// <summary>
        /// 自检（IDeviceDriver接口要求）
        /// </summary>
        public async Task<bool> SelfTestAsync()
        {
            try
            {
                Debug.WriteLine($"[SimulatedArtSwitchDriver] 执行自检");

                if (!IsConnected)
                {
                    Debug.WriteLine($"[SimulatedArtSwitchDriver] 设备未连接，自检失败");
                    return false;
                }

                await Task.Delay(_simulationDelayMs * 3); // 模拟自检延迟

                // 模拟自检结果（90% 概率通过）
                bool selfTestPassed = _random.NextDouble() > 0.1;

                if (selfTestPassed)
                {
                    Debug.WriteLine($"[SimulatedArtSwitchDriver] 自检通过");
                }
                else
                {
                    Debug.WriteLine($"[SimulatedArtSwitchDriver] 自检失败（模拟）");
                }

                return selfTestPassed;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SimulatedArtSwitchDriver] 自检失败: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region 辅助方法（私有）

        /// <summary>
        /// 读取单个通道状态（辅助方法）
        /// </summary>
        private async Task<string> GetChannelConnectionAsync(string channelId)
        {
            if (!IsConnected)
            {
                Debug.WriteLine($"[SimulatedArtSwitchDriver] 设备未连接，无法读取通道状态");
                return string.Empty;
            }

            try
            {
                await Task.Delay(_simulationDelayMs / 2); // 模拟读取延迟

                // 模拟随机错误
                if (_random.NextDouble() < ErrorProbability)
                {
                    Debug.WriteLine($"[SimulatedArtSwitchDriver] 模拟通道查询错误: {channelId}");
                    return string.Empty;
                }

                // 从映射中获取连接状态
                if (_channelConnections.TryGetValue(channelId, out var connectedChannel))
                {
                    Debug.WriteLine($"[SimulatedArtSwitchDriver] 通道 {channelId} 连接到 {connectedChannel}");
                    return connectedChannel;
                }

                Debug.WriteLine($"[SimulatedArtSwitchDriver] 通道 {channelId} 未连接");
                return string.Empty;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SimulatedArtSwitchDriver] 读取通道状态失败: {ex.Message}");
                return string.Empty;
            }
        }

        /// <summary>
        /// 初始化连接矩阵
        /// </summary>
        private void InitializeConnectionMatrix(int inputCount, int outputCount)
        {
            _connectionMatrix.Clear();
            _channelConnections.Clear();

            for (int i = 0; i < inputCount; i++)
            {
                string input = $"IN{i}";
                var outputDict = new ConcurrentDictionary<string, bool>();

                for (int j = 0; j < outputCount; j++)
                {
                    string output = $"OUT{j}";
                    outputDict[output] = false;
                }

                _connectionMatrix[input] = outputDict;
            }

            Debug.WriteLine($"[SimulatedArtSwitchDriver] 连接矩阵已初始化: {inputCount}x{outputCount}");
        }

        /// <summary>
        /// 获取连接状态
        /// </summary>
        private bool GetConnectionState(string input, string output)
        {
            if (_connectionMatrix.TryGetValue(input, out var outputs))
            {
                if (outputs.TryGetValue(output, out bool isConnected))
                {
                    return isConnected;
                }
            }
            return false;
        }

        /// <summary>
        /// 设置连接状态
        /// </summary>
        private void SetConnectionState(string input, string output, bool isConnected)
        {
            if (_connectionMatrix.TryGetValue(input, out var outputs))
            {
                outputs[output] = isConnected;
            }
        }

        /// <summary>
        /// 检查通道是否有效
        /// </summary>
        private bool IsValidChannel(string channel)
        {
            if (channel.StartsWith("IN"))
            {
                if (int.TryParse(channel.Substring(2), out int index))
                {
                    var config = _supportedTopologies[CurrentTopology];
                    return index >= 0 && index < config.Inputs;
                }
            }
            else if (channel.StartsWith("OUT"))
            {
                if (int.TryParse(channel.Substring(3), out int index))
                {
                    var config = _supportedTopologies[CurrentTopology];
                    return index >= 0 && index < config.Outputs;
                }
            }
            return false;
        }

        /// <summary>
        /// 检查输出是否独占（一次只能连接一个输入）
        /// </summary>
        private bool IsOutputExclusive(string output)
        {
            // 对于矩阵拓扑，输出通常是独占的
            return CurrentTopology.Contains("Matrix") || CurrentTopology.Contains("Mux");
        }

        /// <summary>
        /// 获取连接到指定输出的输入
        /// </summary>
        private string GetConnectedInput(string output)
        {
            foreach (var kvp in _connectionMatrix)
            {
                if (kvp.Value.TryGetValue(output, out bool isConnected) && isConnected)
                {
                    return kvp.Key;
                }
            }
            return string.Empty;
        }

        /// <summary>
        /// 获取活跃连接数
        /// </summary>
        private int GetActiveConnectionCount()
        {
            int count = 0;
            foreach (var outputs in _connectionMatrix.Values)
            {
                count += outputs.Values.Count(v => v);
            }
            return count;
        }

        /// <summary>
        /// 获取总继电器数
        /// </summary>
        private int GetTotalRelayCount()
        {
            if (_supportedTopologies.TryGetValue(CurrentTopology, out var config))
            {
                return config.Inputs * config.Outputs;
            }
            return 0;
        }

        #endregion
    }
}