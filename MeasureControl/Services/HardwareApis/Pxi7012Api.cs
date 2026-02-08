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
    public enum Pxi7012OutputMode
    {
        NoWait,
        BreakBeforeMake,
        MakeBeforeBreak,
        WaitSettleTime
    }

    public sealed class Pxi7012RelayState
    {
        public bool PathRelayClosed { get; set; }
        public bool ShortCircuitClosed { get; set; }
    }

    public interface IPxi7012Api : IAsyncDisposable
    {
        bool IsConnected { get; }

        Task ConnectAsync(CancellationToken cancellationToken = default);
        Task DisconnectAsync(CancellationToken cancellationToken = default);

        Task<double> GetResistanceAsync(string roChannel, CancellationToken cancellationToken = default);
        Task SetResistanceAsync(string roChannel, double resistanceOhm, Pxi7012OutputMode mode = Pxi7012OutputMode.NoWait, CancellationToken cancellationToken = default);

        Task<IDictionary<string, double>> GetResistancesAsync(IEnumerable<string> roChannels, CancellationToken cancellationToken = default);
        Task SetResistancesAsync(IDictionary<string, double> roToOhm, Pxi7012OutputMode mode = Pxi7012OutputMode.NoWait, CancellationToken cancellationToken = default);

        Task<Pxi7012RelayState> GetRelayStateAsync(string roChannel, CancellationToken cancellationToken = default);
        Task SetRelayStateAsync(string roChannel, bool pathRelayClosed, bool shortCircuitClosed, CancellationToken cancellationToken = default);

        Task ResetAsync(CancellationToken cancellationToken = default);

        Task<IDictionary<string, object>> GetStatusAsync(CancellationToken cancellationToken = default);
    }

    public sealed class Pxi7012Api : IPxi7012Api
    {
        private readonly DeviceBase _device;
        private readonly uint _logicalId;
        private ACTS6010Driver _driver;
        private readonly SemaphoreSlim _lifecycleLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _ioLock = new SemaphoreSlim(1, 1);
        private bool _disposed;

        public Pxi7012Api(DeviceBase device, uint logicalId = 0)
        {
            _device = device ?? throw new ArgumentNullException(nameof(device));
            _logicalId = logicalId;
        }

        public bool IsConnected => _driver?.IsConnected == true;

        public async Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (IsConnected)
                    return;

                _driver = new ACTS6010Driver(_device, _logicalId);
                var ok = await _driver.ConnectAsync().ConfigureAwait(false);
                if (!ok)
                    throw new InvalidOperationException("ACTS6010 connect returned false");
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

                try
                {
                    await _driver.DisconnectAsync().ConfigureAwait(false);
                }
                finally
                {
                    _driver = null;
                }
            }
            finally
            {
                _lifecycleLock.Release();
            }
        }

        public async Task<double> GetResistanceAsync(string roChannel, CancellationToken cancellationToken = default)
        {
            EnsureConnected();
            var channel = NormalizeRoChannel(roChannel);

            await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await _driver.ReadChannelAsync(channel).ConfigureAwait(false);
            }
            finally
            {
                _ioLock.Release();
            }
        }

        public async Task SetResistanceAsync(string roChannel, double resistanceOhm, Pxi7012OutputMode mode = Pxi7012OutputMode.NoWait, CancellationToken cancellationToken = default)
        {
            EnsureConnected();
            var channel = NormalizeRoChannel(roChannel);

            await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                // Note: ACTS6010Driver currently uses NOWAIT internally.
                // We still accept mode at API level for forward-compat.
                _ = mode;

                var ok = await _driver.WriteChannelAsync(channel, resistanceOhm).ConfigureAwait(false);
                if (!ok)
                    throw new InvalidOperationException($"Set resistance failed: {channel}={resistanceOhm}");
            }
            finally
            {
                _ioLock.Release();
            }
        }

        public async Task<IDictionary<string, double>> GetResistancesAsync(IEnumerable<string> roChannels, CancellationToken cancellationToken = default)
        {
            EnsureConnected();
            if (roChannels == null)
                throw new ArgumentNullException(nameof(roChannels));

            var list = roChannels.Select(NormalizeRoChannel).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var values = await _driver.ReadChannelsBatchAsync(list).ConfigureAwait(false);
                return values;
            }
            finally
            {
                _ioLock.Release();
            }
        }

        public async Task SetResistancesAsync(IDictionary<string, double> roToOhm, Pxi7012OutputMode mode = Pxi7012OutputMode.NoWait, CancellationToken cancellationToken = default)
        {
            EnsureConnected();
            if (roToOhm == null)
                throw new ArgumentNullException(nameof(roToOhm));

            var dict = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in roToOhm)
            {
                dict[NormalizeRoChannel(kv.Key)] = kv.Value;
            }

            await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                _ = mode;
                var ok = await _driver.WriteChannelsBatchAsync(dict).ConfigureAwait(false);
                if (!ok)
                    throw new InvalidOperationException("Set resistances batch failed");
            }
            finally
            {
                _ioLock.Release();
            }
        }

        public async Task<Pxi7012RelayState> GetRelayStateAsync(string roChannel, CancellationToken cancellationToken = default)
        {
            EnsureConnected();
            var channel = NormalizeRoChannel(roChannel);

            // ACTS6010Driver exposes GetPSRelayState via DLL but does not currently wrap it publicly.
            // For now, we return the last known state from SetRelayStateAsync calls.
            await Task.Yield();
            throw new NotSupportedException("GetRelayStateAsync is not available until ACTS6010Driver exposes relay readback");
        }

        public async Task SetRelayStateAsync(string roChannel, bool pathRelayClosed, bool shortCircuitClosed, CancellationToken cancellationToken = default)
        {
            EnsureConnected();
            var channel = NormalizeRoChannel(roChannel);

            await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var ok = await _driver.SetRelayStateAsync(channel, pathRelayClosed, shortCircuitClosed).ConfigureAwait(false);
                if (!ok)
                    throw new InvalidOperationException($"Set relay state failed: {channel}");
            }
            finally
            {
                _ioLock.Release();
            }
        }

        public async Task ResetAsync(CancellationToken cancellationToken = default)
        {
            EnsureConnected();

            // Define a conservative safe reset:
            // - open path relay
            // - open short circuit relay
            // - set resistance to min (driver clamps to device min anyway)
            await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var channels = Enumerable.Range(0, 9).Select(i => $"RO{i}").ToList();
                foreach (var ch in channels)
                {
                    await _driver.SetRelayStateAsync(ch, pathRelayClosed: false, shortCircuitClosed: false).ConfigureAwait(false);
                }

                foreach (var ch in channels)
                {
                    await _driver.WriteChannelAsync(ch, 0.0).ConfigureAwait(false);
                }
            }
            finally
            {
                _ioLock.Release();
            }
        }

        public async Task<IDictionary<string, object>> GetStatusAsync(CancellationToken cancellationToken = default)
        {
            EnsureConnected();

            await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await _driver.GetStatusAsync().ConfigureAwait(false);
            }
            finally
            {
                _ioLock.Release();
            }
        }

        private void EnsureConnected()
        {
            if (_driver == null || !_driver.IsConnected)
                throw new InvalidOperationException("PXI-7012 is not connected");
        }

        private static string NormalizeRoChannel(string channel)
        {
            if (string.IsNullOrWhiteSpace(channel))
                throw new ArgumentException("Channel is required", nameof(channel));

            var raw = channel.Trim();
            if (!raw.StartsWith("RO", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Channel must start with 'RO'", nameof(channel));

            var num = raw.Substring(2);
            if (!int.TryParse(num, NumberStyles.Integer, CultureInfo.InvariantCulture, out var idx))
                throw new ArgumentException("Invalid RO channel index", nameof(channel));

            // Public API accepts 1-based channel number (RO1..RO9).
            // Hardware uses 0-based (RO0..RO8).
            if (idx < 1 || idx > 9)
                throw new ArgumentOutOfRangeException(nameof(channel), "RO channel index must be 1..9");

            return $"RO{idx - 1}";
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
