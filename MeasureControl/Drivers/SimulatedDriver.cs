using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MeasureControl.Models;
using MeasureControl.Models.Channels;
using MeasureControl.Models.Devices;

namespace MeasureControl.Drivers
{
    /// <summary>
    /// 模拟设备驱动实现
    /// 用于在无硬件环境下进行测试和开发
    /// </summary>
    public class SimulatedDriver : IDeviceDriver
    {
        /// <summary>
        /// 采集状态改变事件（此驱动不支持，始终为空实现）
        /// </summary>
        public event EventHandler<AcquisitionStatusChangedEventArgs> AcquisitionStatusChanged
        {
            add { }
            remove { }
        }
        private readonly DeviceBase _device;
        private readonly Random _random;
        private readonly Dictionary<string, SimulationConfig> _channelConfigs;
        private readonly Dictionary<string, double> _channelCurrentValues;
        private readonly DateTime _startTime;
        private bool _isConnected;
        private bool _isAcquiring;

        public string DeviceId => _device?.Id;
        public string DeviceName => _device?.Name;
        public bool IsConnected => _isConnected;
        public bool IsSimulated => true;
        public DeviceCapability Capability => _device?.Capability ?? DeviceCapability.Other;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="device">要模拟的设备</param>
        /// <param name="defaultConfig">默认模拟配置（可选）</param>
        public SimulatedDriver(DeviceBase device, SimulationConfig defaultConfig = null)
        {
            _device = device ?? throw new ArgumentNullException(nameof(device));
            _random = new Random();
            _channelConfigs = new Dictionary<string, SimulationConfig>();
            _channelCurrentValues = new Dictionary<string, double>();
            _startTime = DateTime.Now;
            _isConnected = false;
            _isAcquiring = false;

            // 为设备的所有通道初始化默认配置
            if (_device.Channels != null)
            {
                foreach (var channel in _device.Channels)
                {
                    var config = defaultConfig?.Clone() ?? CreateDefaultConfigForChannelType(channel.ChannelType);
                    _channelConfigs[channel.Id] = config;
                    _channelCurrentValues[channel.Id] = config.Offset;
                }
            }
        }

        /// <summary>
        /// 根据通道类型创建默认配置
        /// </summary>
        private SimulationConfig CreateDefaultConfigForChannelType(string channelType)
        {
            switch (channelType)
            {
                case "AI": // 模拟输入
                    return SimulationConfig.CreateSineWave(frequency: 1.0, amplitude: 5.0, offset: 0.0);

                case "AO": // 模拟输出
                    return SimulationConfig.CreateConstant(0.0);

                case "DI": // 数字输入
                    return SimulationConfig.CreateSquareWave(frequency: 10.0, amplitude: 0.5, offset: 0.5);

                case "DO": // 数字输出
                    return SimulationConfig.CreateConstant(0.0);

                case "CAN":
                case "ARINC429":
                case "1553B":
                    return SimulationConfig.CreateRandomNoise(amplitude: 100.0, offset: 500.0);

                case "LVDT":
                    return SimulationConfig.CreateSineWave(frequency: 0.5, amplitude: 2.0, offset: 0.0);

                default:
                    return new SimulationConfig();
            }
        }

        public async Task<bool> ConnectAsync()
        {
            await Task.Delay(100); // 模拟连接延迟
            _isConnected = true;
            return true;
        }

        public async Task<bool> DisconnectAsync()
        {
            await Task.Delay(50); // 模拟断开延迟
            _isConnected = false;
            _isAcquiring = false;
            return true;
        }

        public async Task<double> ReadChannelAsync(string channelId)
        {
            if (!_isConnected)
                throw new InvalidOperationException("驱动未连接");

            if (!_channelConfigs.ContainsKey(channelId))
                throw new ArgumentException($"未找到通道：{channelId}");

            var config = _channelConfigs[channelId];

            // 模拟采样延迟
            if (config.SamplingDelay > 0)
            {
                await Task.Delay(config.SamplingDelay);
            }

            // 生成模拟数据
            var value = GenerateValue(config);

            // 更新当前值
            _channelCurrentValues[channelId] = value;

            return value;
        }

        public async Task<Dictionary<string, double>> ReadChannelsBatchAsync(IEnumerable<string> channelIds)
        {
            var results = new Dictionary<string, double>();

            foreach (var channelId in channelIds)
            {
                try
                {
                    var value = await ReadChannelAsync(channelId);
                    results[channelId] = value;
                }
                catch (Exception)
                {
                    // 如果某个通道读取失败，跳过
                    continue;
                }
            }

            return results;
        }

        public async Task<bool> WriteChannelAsync(string channelId, double value)
        {
            if (!_isConnected)
                throw new InvalidOperationException("驱动未连接");

            if (!_channelCurrentValues.ContainsKey(channelId))
                throw new ArgumentException($"未找到通道：{channelId}");

            await Task.Delay(10); // 模拟写入延迟

            // 更新通道值
            _channelCurrentValues[channelId] = value;

            // 更新配置为常数模式
            if (_channelConfigs.ContainsKey(channelId))
            {
                _channelConfigs[channelId].WaveformType = SimulationWaveformType.Constant;
                _channelConfigs[channelId].Offset = value;
            }

            return true;
        }

        public async Task<bool> WriteChannelsBatchAsync(Dictionary<string, double> channelValues)
        {
            foreach (var kvp in channelValues)
            {
                try
                {
                    await WriteChannelAsync(kvp.Key, kvp.Value);
                }
                catch (Exception)
                {
                    return false;
                }
            }

            return true;
        }

        public async Task<bool> ConfigureChannelAsync(string channelId, Dictionary<string, object> config)
        {
            if (!_isConnected)
                throw new InvalidOperationException("驱动未连接");

            if (!_channelConfigs.ContainsKey(channelId))
                throw new ArgumentException($"未找到通道：{channelId}");

            await Task.Delay(20); // 模拟配置延迟

            var simConfig = _channelConfigs[channelId];

            // 更新配置参数
            if (config.ContainsKey("WaveformType") && config["WaveformType"] is SimulationWaveformType waveformType)
                simConfig.WaveformType = waveformType;

            if (config.ContainsKey("Frequency") && config["Frequency"] is double frequency)
                simConfig.Frequency = frequency;

            if (config.ContainsKey("Amplitude") && config["Amplitude"] is double amplitude)
                simConfig.Amplitude = amplitude;

            if (config.ContainsKey("Offset") && config["Offset"] is double offset)
                simConfig.Offset = offset;

            if (config.ContainsKey("NoiseLevel") && config["NoiseLevel"] is double noiseLevel)
                simConfig.NoiseLevel = noiseLevel;

            return true;
        }

        public async Task<bool> StartAcquisitionAsync()
        {
            if (!_isConnected)
                return false;

            await Task.Delay(50); // 模拟启动延迟
            _isAcquiring = true;
            return true;
        }

        public async Task<bool> StopAcquisitionAsync()
        {
            await Task.Delay(30); // 模拟停止延迟
            _isAcquiring = false;
            return true;
        }

        public async Task<Dictionary<string, object>> GetStatusAsync()
        {
            await Task.Delay(10);

            var status = new Dictionary<string, object>
            {
                { "IsConnected", _isConnected },
                { "IsAcquiring", _isAcquiring },
                { "DeviceName", DeviceName },
                { "DeviceId", DeviceId },
                { "ChannelCount", _channelConfigs.Count },
                { "Uptime", (DateTime.Now - _startTime).TotalSeconds },
                { "IsSimulated", true }
            };

            return status;
        }

        public async Task<bool> ResetAsync()
        {
            await Task.Delay(100); // 模拟重置延迟

            _isAcquiring = false;

            // 重置所有通道值
            foreach (var channelId in _channelCurrentValues.Keys.ToList())
            {
                var config = _channelConfigs[channelId];
                _channelCurrentValues[channelId] = config.Offset;
            }

            return true;
        }

        public async Task<bool> SelfTestAsync()
        {
            await Task.Delay(200); // 模拟自检延迟
            return true; // 模拟驱动总是通过自检
        }

        /// <summary>
        /// 生成模拟数据值
        /// </summary>
        private double GenerateValue(SimulationConfig config)
        {
            var timeElapsed = (DateTime.Now - _startTime).TotalSeconds;
            double value = 0.0;

            // 根据波形类型生成基础值
            switch (config.WaveformType)
            {
                case SimulationWaveformType.Sine:
                    value = config.Amplitude * Math.Sin(2 * Math.PI * config.Frequency * timeElapsed + config.Phase * Math.PI / 180.0);
                    break;

                case SimulationWaveformType.Square:
                    value = config.Amplitude * Math.Sign(Math.Sin(2 * Math.PI * config.Frequency * timeElapsed + config.Phase * Math.PI / 180.0));
                    break;

                case SimulationWaveformType.Triangle:
                    var trianglePhase = (config.Frequency * timeElapsed + config.Phase / 360.0) % 1.0;
                    value = config.Amplitude * (4 * Math.Abs(trianglePhase - 0.5) - 1);
                    break;

                case SimulationWaveformType.Sawtooth:
                    var sawtoothPhase = (config.Frequency * timeElapsed + config.Phase / 360.0) % 1.0;
                    value = config.Amplitude * (2 * sawtoothPhase - 1);
                    break;

                case SimulationWaveformType.Random:
                    value = config.Amplitude * (2 * _random.NextDouble() - 1);
                    break;

                case SimulationWaveformType.Constant:
                    value = 0.0;
                    break;
            }

            // 添加偏移
            value += config.Offset;

            // 添加噪声
            if (config.NoiseLevel > 0)
            {
                var noise = config.NoiseLevel * config.Amplitude * (2 * _random.NextDouble() - 1);
                value += noise;
            }

            // 添加趋势（漂移）
            if (config.EnableTrend)
            {
                value += config.TrendRate * timeElapsed;
            }

            // 限幅
            value = Math.Max(config.MinValue, Math.Min(config.MaxValue, value));

            return value;
        }

        /// <summary>
        /// 设置通道的模拟配置
        /// </summary>
        public void SetChannelConfig(string channelId, SimulationConfig config)
        {
            if (_channelConfigs.ContainsKey(channelId))
            {
                _channelConfigs[channelId] = config ?? throw new ArgumentNullException(nameof(config));
            }
        }

        /// <summary>
        /// 获取通道的模拟配置
        /// </summary>
        public SimulationConfig GetChannelConfig(string channelId)
        {
            return _channelConfigs.ContainsKey(channelId) ? _channelConfigs[channelId] : null;
        }

        /// <summary>
        /// 获取通道当前值（不进行新的采样）
        /// </summary>
        public double GetChannelCurrentValue(string channelId)
        {
            return _channelCurrentValues.ContainsKey(channelId) ? _channelCurrentValues[channelId] : 0.0;
        }
    }
}

