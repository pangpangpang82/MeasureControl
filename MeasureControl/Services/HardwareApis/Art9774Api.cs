using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MeasureControl.Drivers;
using static MeasureControl.Helpers.ArtDAQ;
using MeasureControl.Models.Devices;

namespace MeasureControl.Services.HardwareApis
{
    public enum AiAcquisitionMode
    {
        Finite,
        Continuous
    }

    public enum AiInputRange
    {
        PlusMinus10V,
        PlusMinus5V,
        PlusMinus2V,
        PlusMinus1V
    }

    public sealed class AiChannelConfig
    {
        public string Channel { get; set; }
        public bool Enabled { get; set; }
        public AiInputRange Range { get; set; } = AiInputRange.PlusMinus10V;
    }

    public sealed class AiAcquisitionOptions
    {
        public AiAcquisitionMode Mode { get; set; } = AiAcquisitionMode.Continuous;
        public double SampleRateHz { get; set; } = 10000.0;
        public int SamplesPerChannel { get; set; } = 1000;
        public string DeviceName { get; set; } = "Dev3";
        public int TerminalConfig { get; set; } = ArtDAQ_Val_Cfg_Default;
    }

    public sealed class AiSampleBlock
    {
        public IReadOnlyDictionary<string, double[]> Samples { get; set; }
        public double SampleRateHz { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public interface IArt9774AiApi : IAsyncDisposable
    {
        bool IsConnected { get; }
        bool IsRunning { get; }

        event Action<AiSampleBlock> SamplesAvailable;

        Task ConnectAsync(CancellationToken cancellationToken = default);
        Task DisconnectAsync(CancellationToken cancellationToken = default);

        Task ConfigureAcquisitionAsync(AiAcquisitionOptions options, CancellationToken cancellationToken = default);

        Task ConfigureChannelAsync(AiChannelConfig config, CancellationToken cancellationToken = default);
        Task ConfigureChannelsAsync(IEnumerable<AiChannelConfig> configs, CancellationToken cancellationToken = default);

        Task StartAsync(CancellationToken cancellationToken = default);
        Task StopAsync(CancellationToken cancellationToken = default);

        Task<double> GetLastValueAsync(string aiChannel, CancellationToken cancellationToken = default);
        Task<IDictionary<string, double>> GetLastValuesAsync(IEnumerable<string> aiChannels, CancellationToken cancellationToken = default);

        Task<IDictionary<string, double[]>> AcquireFiniteAsync(CancellationToken cancellationToken = default);
    }

    public sealed class Art9774Api : IArt9774AiApi
    {
        private readonly DeviceBase _device;
        private readonly SemaphoreSlim _lifecycleLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _ioLock = new SemaphoreSlim(1, 1);

        private Art9774Driver _driver;
        private bool _isRunning;
        private bool _disposed;
        private AiAcquisitionOptions _options;

        public Art9774Api(DeviceBase device, AiAcquisitionOptions options = null)
        {
            _device = device ?? throw new ArgumentNullException(nameof(device));
            _options = options ?? new AiAcquisitionOptions();
        }

        private void OnDriverAcquisitionStatusChanged(object sender, AcquisitionStatusChangedEventArgs e)
        {
            if (e == null)
                return;

            if (_options?.Mode == AiAcquisitionMode.Finite && e.IsRunning == false)
            {
                _isRunning = false;
            }
        }

        public bool IsConnected => _driver?.IsConnected == true;

        public bool IsRunning => _isRunning;

        public event Action<AiSampleBlock> SamplesAvailable;

        public async Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (IsConnected)
                    return;

                var deviceName = _options?.DeviceName;
                _driver = new Art9774Driver(_device, deviceNameOverride: deviceName);
                _driver.SamplesAvailable += OnDriverSamplesAvailable;
                _driver.AcquisitionStatusChanged += OnDriverAcquisitionStatusChanged;

                var ok = await _driver.ConnectAsync().ConfigureAwait(false);
                if (!ok)
                    throw new InvalidOperationException("ART9774 connect returned false");

                _isRunning = false;
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

                if (_isRunning)
                {
                    try { await StopAsync(cancellationToken).ConfigureAwait(false); } catch { }
                }

                try
                {
                    _driver.AcquisitionStatusChanged -= OnDriverAcquisitionStatusChanged;
                    _driver.SamplesAvailable -= OnDriverSamplesAvailable;
                    await _driver.DisconnectAsync().ConfigureAwait(false);
                }
                finally
                {
                    _isRunning = false;
                    _driver = null;
                }
            }
            finally
            {
                _lifecycleLock.Release();
            }
        }

        public async Task ConfigureAcquisitionAsync(AiAcquisitionOptions options, CancellationToken cancellationToken = default)
        {
            if (options == null)
                throw new ArgumentNullException(nameof(options));

            if (double.IsNaN(options.SampleRateHz) || double.IsInfinity(options.SampleRateHz) || options.SampleRateHz <= 0)
                throw new ArgumentOutOfRangeException(nameof(options.SampleRateHz));

            if (options.SamplesPerChannel <= 0)
                throw new ArgumentOutOfRangeException(nameof(options.SamplesPerChannel));

            if (string.IsNullOrWhiteSpace(options.DeviceName))
                throw new ArgumentException("DeviceName is required", nameof(options.DeviceName));

            if (IsConnected && !string.Equals(_options.DeviceName, options.DeviceName, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("DeviceName cannot be changed while connected. Disconnect and reconnect.");

            await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                _options = options;
            }
            finally
            {
                _ioLock.Release();
            }
        }

        public async Task ConfigureChannelAsync(AiChannelConfig config, CancellationToken cancellationToken = default)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            EnsureConnected();
            var ch = NormalizeAiChannel(config.Channel);

            await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var dict = new Dictionary<string, object>
                {
                    { "IsEnabled", config.Enabled },
                    { "Range", RangeToDriverString(config.Range) }
                };

                var ok = await _driver.ConfigureChannelAsync(ch, dict).ConfigureAwait(false);
                if (!ok)
                    throw new InvalidOperationException($"Configure {config.Channel} failed");
            }
            finally
            {
                _ioLock.Release();
            }
        }

        public async Task ConfigureChannelsAsync(IEnumerable<AiChannelConfig> configs, CancellationToken cancellationToken = default)
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

                    var ch = NormalizeAiChannel(cfg.Channel);
                    var dict = new Dictionary<string, object>
                    {
                        { "IsEnabled", cfg.Enabled },
                        { "Range", RangeToDriverString(cfg.Range) }
                    };

                    var ok = await _driver.ConfigureChannelAsync(ch, dict).ConfigureAwait(false);
                    if (!ok)
                        throw new InvalidOperationException($"Configure {cfg.Channel} failed");
                }
            }
            finally
            {
                _ioLock.Release();
            }
        }

        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            EnsureConnected();

            await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_isRunning)
                    return;

                var ok = await StartInternalAsync().ConfigureAwait(false);
                if (!ok)
                    throw new InvalidOperationException("Start acquisition returned false");

                _isRunning = true;
            }
            finally
            {
                _ioLock.Release();
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            EnsureConnected();

            await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!_isRunning)
                    return;

                var ok = await _driver.StopAcquisitionAsync().ConfigureAwait(false);
                if (!ok)
                    throw new InvalidOperationException("Stop acquisition returned false");

                _isRunning = false;
            }
            finally
            {
                _ioLock.Release();
            }
        }

        public async Task<double> GetLastValueAsync(string aiChannel, CancellationToken cancellationToken = default)
        {
            EnsureConnected();
            var ch = NormalizeAiChannel(aiChannel);

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

        public async Task<IDictionary<string, double>> GetLastValuesAsync(IEnumerable<string> aiChannels, CancellationToken cancellationToken = default)
        {
            EnsureConnected();
            if (aiChannels == null)
                throw new ArgumentNullException(nameof(aiChannels));

            var external = aiChannels.ToList();
            var internalChs = external.Select(NormalizeAiChannel).ToList();

            await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var dict = await _driver.ReadChannelsBatchAsync(internalChs).ConfigureAwait(false);
                var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

                foreach (var kvp in dict)
                {
                    result[ToExternalChannel(kvp.Key)] = kvp.Value;
                }

                return result;
            }
            finally
            {
                _ioLock.Release();
            }
        }

        public async Task<IDictionary<string, double[]>> AcquireFiniteAsync(CancellationToken cancellationToken = default)
        {
            EnsureConnected();

            if (_options.Mode != AiAcquisitionMode.Finite)
                throw new InvalidOperationException("AcquireFiniteAsync requires Mode=Finite");

            await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_isRunning)
                    throw new InvalidOperationException("Acquisition is already running");

                var tcs = new TaskCompletionSource<Dictionary<string, double[]>>(TaskCreationOptions.RunContinuationsAsynchronously);

                void Handler(Dictionary<string, double[]> internalSamples)
                {
                    try
                    {
                        var ext = MapToExternalSamples(internalSamples);
                        tcs.TrySetResult(ext);
                    }
                    catch (Exception ex)
                    {
                        tcs.TrySetException(ex);
                    }
                }

                _driver.SamplesAvailable += Handler;
                try
                {
                    var ok = await StartInternalAsync().ConfigureAwait(false);
                    if (!ok)
                        throw new InvalidOperationException("Start finite acquisition returned false");

                    _isRunning = true;

                    using (cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken)))
                    {
                        var samples = await tcs.Task.ConfigureAwait(false);
                        return samples;
                    }
                }
                finally
                {
                    _driver.SamplesAvailable -= Handler;
                }
            }
            finally
            {
                _ioLock.Release();
            }
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
            catch { }

            _lifecycleLock.Dispose();
            _ioLock.Dispose();
        }

        private void EnsureConnected()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(Art9774Api));

            if (!IsConnected)
                throw new InvalidOperationException("Device is not connected");
        }

        private async Task<bool> StartInternalAsync()
        {
            if (_options == null)
                _options = new AiAcquisitionOptions();

            if (_options.Mode == AiAcquisitionMode.Continuous)
            {
                return await _driver.StartContinuousAcquisitionAsync(_options.SampleRateHz, _options.SamplesPerChannel, _options.TerminalConfig).ConfigureAwait(false);
            }

            return await _driver.StartFiniteAcquisitionAsync(_options.SampleRateHz, _options.SamplesPerChannel, _options.TerminalConfig).ConfigureAwait(false);
        }

        private void OnDriverSamplesAvailable(Dictionary<string, double[]> internalSamples)
        {
            var handler = SamplesAvailable;
            if (handler == null)
                return;

            if (_options?.Mode != AiAcquisitionMode.Continuous)
                return;

            AiSampleBlock block;
            try
            {
                block = new AiSampleBlock
                {
                    Samples = MapToExternalSamples(internalSamples),
                    SampleRateHz = _options?.SampleRateHz ?? 0,
                    Timestamp = DateTime.UtcNow
                };
            }
            catch
            {
                return;
            }

            try
            {
                handler(block);
            }
            catch
            {
            }
        }

        private static Dictionary<string, double[]> MapToExternalSamples(Dictionary<string, double[]> internalSamples)
        {
            var ext = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase);
            if (internalSamples == null)
                return ext;

            foreach (var kvp in internalSamples)
            {
                ext[ToExternalChannel(kvp.Key)] = kvp.Value;
            }

            return ext;
        }

        private static string NormalizeAiChannel(string externalChannel)
        {
            if (string.IsNullOrWhiteSpace(externalChannel))
                throw new ArgumentException("Channel is required", nameof(externalChannel));

            var s = externalChannel.Trim();
            if (!s.StartsWith("AI", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("AI channel must be in form AI1..AI32", nameof(externalChannel));

            var idxText = s.Substring(2);
            if (!int.TryParse(idxText, out var idx))
                throw new ArgumentException("AI channel must be in form AI1..AI32", nameof(externalChannel));

            if (idx < 1 || idx > 32)
                throw new ArgumentOutOfRangeException(nameof(externalChannel), "AI channel must be in range AI1..AI32");

            return $"AI{idx - 1}";
        }

        private static string ToExternalChannel(string internalChannel)
        {
            if (string.IsNullOrWhiteSpace(internalChannel))
                return internalChannel;

            var s = internalChannel.Trim();
            if (!s.StartsWith("AI", StringComparison.OrdinalIgnoreCase))
                return internalChannel;

            var idxText = s.Substring(2);
            if (!int.TryParse(idxText, out var idx))
                return internalChannel;

            return $"AI{idx + 1}";
        }

        private static string RangeToDriverString(AiInputRange range)
        {
            switch (range)
            {
                case AiInputRange.PlusMinus10V:
                    return "±10V";
                case AiInputRange.PlusMinus5V:
                    return "±5V";
                case AiInputRange.PlusMinus2V:
                    return "±2V";
                case AiInputRange.PlusMinus1V:
                    return "±1V";
                default:
                    return "±10V";
            }
        }
    }
}
