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
    public enum Mtx532Waveform
    {
        Dc,
        Sine,
        Square
    }

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

    public sealed class Mtx532Options
    {
        public double SampleRateHz { get; set; } = 1000.0;
        public bool SuppressNativeDialogs { get; set; } = true;
        public bool ResetToZeroOnStop { get; set; } = true;
        public int ResetDelayMs { get; set; } = 500;
    }

    public interface IMtx532Api : IAsyncDisposable
    {
        bool IsConnected { get; }
        bool IsOutputRunning { get; }

        Task ConnectAsync(CancellationToken cancellationToken = default);
        Task DisconnectAsync(CancellationToken cancellationToken = default);

        Task SetSampleRateAsync(double sampleRateHz, CancellationToken cancellationToken = default);

        Task ConfigureChannelAsync(Mtx532ChannelConfig config, CancellationToken cancellationToken = default);
        Task ConfigureChannelsAsync(IEnumerable<Mtx532ChannelConfig> configs, CancellationToken cancellationToken = default);

        Task SetDcAsync(string aoChannel, double voltageV, bool enable = true, CancellationToken cancellationToken = default);
        Task WriteOnceDcAsync(IDictionary<string, double> aoToVoltageV, CancellationToken cancellationToken = default);

        Task StartOutputAsync(CancellationToken cancellationToken = default);
        Task StopOutputAsync(CancellationToken cancellationToken = default);

        Task ResetAllToZeroAsync(bool disableAfterReset = false, CancellationToken cancellationToken = default);

        Task<double> GetLastOutputVoltageAsync(string aoChannel, CancellationToken cancellationToken = default);
    }

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

        public async Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (IsConnected)
                    return;

                _driver = new MTX532Driver(_device, suppressNativeDialogs: _options.SuppressNativeDialogs, slotNumberOverride: _slotNumber);
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

        public async Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_driver == null)
                    return;

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

        public async Task StopOutputAsync(CancellationToken cancellationToken = default)
        {
            EnsureConnected();

            await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
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

        private void EnsureConnected()
        {
            if (_driver == null || !_driver.IsConnected)
                throw new InvalidOperationException("MTX532 is not connected");
        }

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

        private async Task ConfigureSampleRateInternalAsync(double sampleRateHz)
        {
            if (sampleRateHz <= 0)
                return;

            for (int i = 1; i <= 32; i++)
            {
                var ch = NormalizeAoChannel($"AO{i}");
                var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    ["SampleRate"] = sampleRateHz
                };
                await _driver.ConfigureChannelAsync(ch, dict).ConfigureAwait(false);
            }
        }

        private async Task ResetAllToZeroInternalAsync(bool disableAfterReset)
        {
            for (int i = 1; i <= 32; i++)
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

            if (idx < 1 || idx > 32)
                throw new ArgumentOutOfRangeException(nameof(channel), "AO channel index must be 1..32");

            return $"AO{idx - 1}";
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
