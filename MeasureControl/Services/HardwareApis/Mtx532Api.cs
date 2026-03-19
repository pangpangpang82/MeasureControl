using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MeasureControl.Drivers;
using MeasureControl.Models.Devices;

namespace MeasureControl.Services.HardwareApis
{
    /// <summary>
    /// MTX532 输出波形类型
    /// </summary>
    public enum Mtx532Waveform
    {
        Dc,      // 直流电压（恒定值）
        Sine,    // 正弦波
        Square   // 方波
    }

    /// <summary>
    /// MTX532 通道配置
    /// </summary>
    public sealed class Mtx532ChannelConfig
    {
        public string Channel { get; set; }
        public bool Enabled { get; set; }
        public Mtx532Waveform Waveform { get; set; } = Mtx532Waveform.Dc;
        public double OffsetV { get; set; }
        public double AmplitudeV { get; set; }
        public double FrequencyHz { get; set; }
        public double DutyCyclePercent { get; set; } = 50.0;
    }

    /// <summary>
    /// MTX532 板卡选项
    /// </summary>
    public sealed class Mtx532Options
    {
        public double SampleRateHz { get; set; } = 1000.0;
        public bool SuppressNativeDialogs { get; set; } = true;
        public bool ResetToZeroOnStop { get; set; } = true;
        public int ResetDelayMs { get; set; } = 500;
    }

    /// <summary>
    /// MTX532 模拟量输出板卡控制接口
    /// 功能：输出模拟电压信号（最多32路），支持直流、正弦波、方波
    /// </summary>
    public interface IMtx532Api : IAsyncDisposable
    {
        bool IsConnected { get; }      // 是否已连接到板卡
        bool IsOutputRunning { get; }  // 是否正在输出

        Task ConnectAsync(CancellationToken cancellationToken = default);  // 连接到板卡
        Task ConnectAsync(CancellationToken cancellationToken = default, IEnumerable<string> enabledAoChannels = null);
        Task DisconnectAsync(CancellationToken cancellationToken = default);  // 断开板卡连接

        Task SetSampleRateAsync(double sampleRateHz, CancellationToken cancellationToken = default);

        Task ConfigureChannelAsync(Mtx532ChannelConfig config, CancellationToken cancellationToken = default);
        Task ConfigureChannelsAsync(IEnumerable<Mtx532ChannelConfig> configs, CancellationToken cancellationToken = default);

        Task SetDcAsync(string aoChannel, double voltageV, bool enable = true, CancellationToken cancellationToken = default);  // 设置指定通道输出直流电压
        Task WriteOnceDcAsync(IDictionary<string, double> aoToVoltageV, CancellationToken cancellationToken = default);

        Task StartOutputAsync(CancellationToken cancellationToken = default);  // 开始输出
        Task StopOutputAsync(CancellationToken cancellationToken = default);   // 停止输出

        Task ResetAllToZeroAsync(bool disableAfterReset = false, CancellationToken cancellationToken = default);

        Task<double> GetLastOutputVoltageAsync(string aoChannel, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// MTX532 模拟量输出板卡实现类
    /// 通道命名：AO1-AO32（外部）对应 AO0-AO31（内部驱动）
    /// </summary>
    public sealed class Mtx532Api : IMtx532Api
    {
        private readonly DeviceBase _device;
        private readonly Mtx532Options _options;
        private readonly int _slotNumber;
        private MTX532Driver _driver;
        private readonly SemaphoreSlim _lifecycleLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _ioLock = new SemaphoreSlim(1, 1);
        private bool _isOutputRunning;
        private bool _disposed;
        private double _sampleRateHz;

        public Mtx532Api(DeviceBase device, Mtx532Options options = null, int slotNumber = 7)
        {
            _device = device ?? throw new ArgumentNullException(nameof(device));
            _options = options ?? new Mtx532Options();
            _slotNumber = slotNumber;
            _sampleRateHz = _options.SampleRateHz;
        }

        public bool IsConnected => _driver?.IsConnected == true;

        public bool IsOutputRunning => _isOutputRunning;

        /// <summary>
        /// 连接到 MTX532 模拟量输出板卡
        /// </summary>
        public Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            return ConnectAsync(cancellationToken, enabledAoChannels: null);
        }

        public async Task ConnectAsync(CancellationToken cancellationToken = default, IEnumerable<string> enabledAoChannels = null)
        {
            await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (IsConnected)
                    return;

                _driver = new MTX532Driver(_device, suppressNativeDialogs: _options.SuppressNativeDialogs, slotNumberOverride: _slotNumber);

                if (enabledAoChannels != null)
                {
                    var normalized = enabledAoChannels
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Select(x => NormalizeAoChannel(x))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    _driver.SetEnabledChannels(normalized);
                }

                var ok = await _driver.ConnectAsync().ConfigureAwait(false);
                if (!ok)
                    throw new InvalidOperationException("MTX532 connect returned false");

                _isOutputRunning = false;

                if (_sampleRateHz > 0)
                {
                    await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        await ConfigureSampleRateInternalAsync(_sampleRateHz).ConfigureAwait(false);
                    }
                    finally
                    {
                        _ioLock.Release();
                    }
                }
            }
            finally
            {
                _lifecycleLock.Release();
            }
        }

        /// <summary>
        /// 断开与 MTX532 板卡的连接
        /// </summary>
        public async Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                // 如果未连接，直接返回（安全的重复调用）
                if (_driver == null)
                    return;

                // 如果正在输出，先停止
                if (_isOutputRunning)
                {
                    try { await StopOutputAsync(cancellationToken).ConfigureAwait(false); } catch { }
                }

                try
                {
                    await _driver.DisconnectAsync().ConfigureAwait(false);
                }
                finally
                {
                    _isOutputRunning = false;
                    _driver = null;
                }
            }
            finally
            {
                _lifecycleLock.Release();
            }
        }

        /// <summary>
        /// 设置采样率（应用到所有 32 个通道）
        /// </summary>
        /// <param name="sampleRateHz">采样率（Hz）</param>
        public async Task SetSampleRateAsync(double sampleRateHz, CancellationToken cancellationToken = default)
        {
            if (double.IsNaN(sampleRateHz) || double.IsInfinity(sampleRateHz) || sampleRateHz <= 0)
                throw new ArgumentOutOfRangeException(nameof(sampleRateHz));

            _sampleRateHz = sampleRateHz;

            EnsureConnected();
            await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await ConfigureSampleRateInternalAsync(sampleRateHz).ConfigureAwait(false);
            }
            finally
            {
                _ioLock.Release();
            }
        }

        /// <summary>
        /// 配置单个模拟量输出通道
        /// </summary>
        /// <param name="config">通道配置（波形类型、偏置、幅度、频率等）</param>
        public async Task ConfigureChannelAsync(Mtx532ChannelConfig config, CancellationToken cancellationToken = default)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            EnsureConnected();
            var ch = NormalizeAoChannel(config.Channel);

            await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var dict = BuildConfigureDict(config);
                var ok = await _driver.ConfigureChannelAsync(ch, dict).ConfigureAwait(false);
                if (!ok)
                    throw new InvalidOperationException($"Configure {ch} failed");
            }
            finally
            {
                _ioLock.Release();
            }
        }

        /// <summary>
        /// 批量配置多个模拟量输出通道
        /// </summary>
        /// <param name="configs">多个通道的配置列表</param>
        public async Task ConfigureChannelsAsync(IEnumerable<Mtx532ChannelConfig> configs, CancellationToken cancellationToken = default)
        {
            EnsureConnected();
            if (configs == null)
                throw new ArgumentNullException(nameof(configs));

            var list = configs.ToList();

            await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                foreach (var cfg in list)
                {
                    if (cfg == null)
                        continue;

                    var ch = NormalizeAoChannel(cfg.Channel);
                    var dict = BuildConfigureDict(cfg);
                    var ok = await _driver.ConfigureChannelAsync(ch, dict).ConfigureAwait(false);
                    if (!ok)
                        throw new InvalidOperationException($"Configure {ch} failed");
                }
            }
            finally
            {
                _ioLock.Release();
            }
        }

        /// <summary>
        /// 快捷设置单个通道输出直流电压
        /// </summary>
        /// <param name="aoChannel">通道名称，如 "AO1", "AO16" 等（1-32）</param>
        /// <param name="voltageV">输出电压值（V）</param>
        /// <param name="enable">是否启用该通道</param>
        public async Task SetDcAsync(string aoChannel, double voltageV, bool enable = true, CancellationToken cancellationToken = default)
        {
            EnsureConnected();
            var ch = NormalizeAoChannel(aoChannel);

            var cfg = new Mtx532ChannelConfig
            {
                Channel = ch,
                Enabled = enable,
                Waveform = Mtx532Waveform.Dc,
                OffsetV = voltageV,
                AmplitudeV = 0.0,
                FrequencyHz = 0.0,
                DutyCyclePercent = 50.0
            };

            await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var dict = BuildConfigureDict(cfg);
                var ok = await _driver.ConfigureChannelAsync(ch, dict).ConfigureAwait(false);
                if (!ok)
                    throw new InvalidOperationException($"Configure {ch} failed");
            }
            finally
            {
                _ioLock.Release();
            }
        }

        /// <summary>
        /// 批量写入多个通道的直流电压（一次性操作）
        /// </summary>
        /// <param name="aoToVoltageV">通道名到电压值的字典</param>
        public async Task WriteOnceDcAsync(IDictionary<string, double> aoToVoltageV, CancellationToken cancellationToken = default)
        {
            EnsureConnected();
            if (aoToVoltageV == null)
                throw new ArgumentNullException(nameof(aoToVoltageV));

            var dict = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in aoToVoltageV)
            {
                dict[NormalizeAoChannel(kv.Key)] = kv.Value;
            }

            await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await ConfigureSampleRateInternalAsync(_sampleRateHz).ConfigureAwait(false);

                var ok = await _driver.WriteChannelsBatchAsync(dict).ConfigureAwait(false);
                if (!ok)
                    throw new InvalidOperationException("MTX532 write once failed");
            }
            finally
            {
                _ioLock.Release();
            }
        }

        /// <summary>
        /// 开始输出信号（根据配置的波形连续输出）
        /// </summary>
        public async Task StartOutputAsync(CancellationToken cancellationToken = default)
        {
            EnsureConnected();

            await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await ConfigureSampleRateInternalAsync(_sampleRateHz).ConfigureAwait(false);

                var ok = await _driver.StartAcquisitionAsync().ConfigureAwait(false);
                if (!ok)
                    throw new InvalidOperationException("MTX532 start output failed");

                _isOutputRunning = true;
            }
            finally
            {
                _ioLock.Release();
            }
        }

        /// <summary>
        /// 停止输出信号（可选自动复位到 0V）
        /// </summary>
        public async Task StopOutputAsync(CancellationToken cancellationToken = default)
        {
            EnsureConnected();

            await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                // 如果配置了停止时复位，先将所有通道设置为 0V
                if (_options.ResetToZeroOnStop)
                {
                    await ResetAllToZeroInternalAsync(disableAfterReset: false).ConfigureAwait(false);
                    if (_options.ResetDelayMs > 0)
                    {
                        await Task.Delay(_options.ResetDelayMs, cancellationToken).ConfigureAwait(false);
                    }
                }

                var ok = await _driver.StopAcquisitionAsync().ConfigureAwait(false);
                if (!ok)
                    throw new InvalidOperationException("MTX532 stop output failed");

                _isOutputRunning = false;
            }
            finally
            {
                _ioLock.Release();
            }
        }

        /// <summary>
        /// 将所有通道复位为 0V
        /// </summary>
        /// <param name="disableAfterReset">复位后是否禁用通道</param>
        public async Task ResetAllToZeroAsync(bool disableAfterReset = false, CancellationToken cancellationToken = default)
        {
            EnsureConnected();

            await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await ConfigureSampleRateInternalAsync(_sampleRateHz).ConfigureAwait(false);
                await ResetAllToZeroInternalAsync(disableAfterReset).ConfigureAwait(false);
            }
            finally
            {
                _ioLock.Release();
            }
        }

        /// <summary>
        /// 读取单个通道的当前输出电压
        /// </summary>
        /// <param name="aoChannel">通道名称</param>
        /// <returns>当前输出的电压值</returns>
        public async Task<double> GetLastOutputVoltageAsync(string aoChannel, CancellationToken cancellationToken = default)
        {
            EnsureConnected();
            var ch = NormalizeAoChannel(aoChannel);

            await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await _driver.ReadChannelAsync(ch).ConfigureAwait(false);
            }
            finally
            {
                _ioLock.Release();
            }
        }

        /// <summary>
        /// 确保已连接到板卡，否则抛出异常
        /// </summary>
        private void EnsureConnected()
        {
            if (_driver == null || !_driver.IsConnected)
                throw new InvalidOperationException("MTX532 is not connected");
        }

        /// <summary>
        /// 构建通道配置字典（将配置对象转换为驱动所需的格式）
        /// </summary>
        private Dictionary<string, object> BuildConfigureDict(Mtx532ChannelConfig config)
        {
            var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["Enabled"] = config.Enabled,
                ["SampleRate"] = _sampleRateHz,
                ["Amplitude"] = config.Waveform == Mtx532Waveform.Dc ? 0.0 : config.AmplitudeV,
                ["Offset"] = config.OffsetV,
                ["Frequency"] = config.Waveform == Mtx532Waveform.Dc ? 0.0 : config.FrequencyHz,
                ["DutyCycle"] = config.Waveform == Mtx532Waveform.Square ? config.DutyCyclePercent : 50.0
            };

            dict["Waveform"] = config.Waveform switch
            {
                Mtx532Waveform.Dc => MTX532Driver.WaveformType.Dc,
                Mtx532Waveform.Sine => MTX532Driver.WaveformType.Sine,
                Mtx532Waveform.Square => MTX532Driver.WaveformType.Square,
                _ => MTX532Driver.WaveformType.Dc
            };

            return dict;
        }

        /// <summary>
        /// 内部方法：为所有 32 个通道设置采样率
        /// </summary>
        private async Task ConfigureSampleRateInternalAsync(double sampleRateHz)
        {
            if (sampleRateHz <= 0)
                return;

            for (int i = 0; i < 32; i++)
            {
                var ch = NormalizeAoChannel($"AO{i}");
                var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    ["SampleRate"] = sampleRateHz
                };
                await _driver.ConfigureChannelAsync(ch, dict).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// 内部方法：将所有 32 个通道复位为 0V
        /// </summary>
        private async Task ResetAllToZeroInternalAsync(bool disableAfterReset)
        {
            for (int i = 0; i < 32; i++)
            {
                var ch = NormalizeAoChannel($"AO{i}");
                var cfg = new Mtx532ChannelConfig
                {
                    Channel = ch,
                    Enabled = !disableAfterReset,
                    Waveform = Mtx532Waveform.Dc,
                    OffsetV = 0.0,
                    AmplitudeV = 0.0,
                    FrequencyHz = 0.0,
                    DutyCyclePercent = 50.0
                };
                var dict = BuildConfigureDict(cfg);
                await _driver.ConfigureChannelAsync(ch, dict).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// 将外部通道名转换为内部驱动通道名（AO0-AO31）
        /// 兼容两种命名：
        /// - AO0-AO31（推荐，和板卡测试界面/驱动一致）
        /// - AO1-AO32（历史兼容）
        /// </summary>
        private static string NormalizeAoChannel(string channel)
        {
            if (string.IsNullOrWhiteSpace(channel))
                throw new ArgumentException("Channel is required", nameof(channel));

            var raw = channel.Trim();
            if (!raw.StartsWith("AO", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Channel must start with 'AO'", nameof(channel));

            var num = raw.Substring(2);
            if (!int.TryParse(num, NumberStyles.Integer, CultureInfo.InvariantCulture, out var idx))
                throw new ArgumentException("Invalid AO channel index", nameof(channel));

            // 约定：
            // - 只有输入 AO0 时，才认为调用方显式使用内部编号。
            // - AO1-AO32 统一按外部端子/文档编号处理，映射到内部 AO0-AO31。
            // 这样可避免常见歧义：例如输入 "AO2"，更符合外部端子 2 的语义（内部应为 AO1）。
            if (idx == 0)
                return "AO0";
            if (idx >= 1 && idx <= 32)
                return $"AO{idx - 1}";

            throw new ArgumentOutOfRangeException(nameof(channel), "AO channel index must be 0 or 1..32");
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
                return;

            _disposed = true;

            try
            {
                await DisconnectAsync().ConfigureAwait(false);
            }
            catch
            {
            }
            finally
            {
                try { _lifecycleLock.Dispose(); } catch { }
                try { _ioLock.Dispose(); } catch { }
            }
        }
    }
}
