using System;
using System.Globalization;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using NationalInstruments.Visa;

namespace MeasureControl.Services.HardwareApis
{
    public enum DmmMeasureMode
    {
        DCV,
        ACV,
        DCI,
        ACI,
        RES,
        CAP,
        CONT,
        DIODE,
        FREQ
    }

    public sealed class DmmReadOptions
    {
        public int? FrequencyRangeIndex { get; set; }
        public int? TimeoutMilliseconds { get; set; }
    }

    public sealed class DmmReading
    {
        public double? Value { get; set; }
        public bool IsOverrange { get; set; }
        public string Raw { get; set; }
        public string Unit { get; set; }
    }

    public interface IDmmApi : IAsyncDisposable
    {
        bool IsConnected { get; }
        string IpAddress { get; }

        Task ConnectAsync(string ipAddress, CancellationToken cancellationToken = default);
        Task DisconnectAsync(CancellationToken cancellationToken = default);

        Task<DmmReading> ReadOnceAsync(DmmMeasureMode mode, DmmReadOptions options = null, CancellationToken cancellationToken = default);

        Task SendAsync(string scpi, CancellationToken cancellationToken = default);
        Task<string> QueryAsync(string scpi, CancellationToken cancellationToken = default);
    }

    public sealed class DmmSocketApi : IDmmApi
    {
        private const int DefaultPort = 5555;

        private readonly SemaphoreSlim _ioLock = new SemaphoreSlim(1, 1);
        private ResourceManager _resourceManager;
        private MessageBasedSession _session;
        private string _ipAddress;
        private bool _disposed;

        public bool IsConnected => _session != null;
        public string IpAddress => _ipAddress;

        public async Task ConnectAsync(string ipAddress, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(ipAddress))
                throw new ArgumentException("ipAddress is required", nameof(ipAddress));

            var trimmed = ipAddress.Trim();
            if (!IPAddress.TryParse(trimmed, out _))
                throw new ArgumentException("Invalid IP address", nameof(ipAddress));

            if (IsConnected && string.Equals(_ipAddress, trimmed, StringComparison.OrdinalIgnoreCase))
                return;

            await DisconnectAsync(cancellationToken).ConfigureAwait(false);

            _resourceManager = new ResourceManager();
            var resourceString = $"TCPIP0::{trimmed}::{DefaultPort}::SOCKET";

            MessageBasedSession session = null;
            try
            {
                session = (MessageBasedSession)_resourceManager.Open(resourceString, 0, 5000);
                try
                {
                    session.TimeoutMilliseconds = 8000;
                    session.TerminationCharacterEnabled = true;
                    session.TerminationCharacter = (byte)'\n';
                }
                catch
                {
                }

                _session = session;
                _ipAddress = trimmed;

                await SendAsync("*CLS", cancellationToken).ConfigureAwait(false);
                await SendAsync(":SYST:REM", cancellationToken).ConfigureAwait(false);

                _ = await QueryAsync("*IDN?", cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                try { session?.Dispose(); } catch { }
                try { _resourceManager?.Dispose(); } catch { }
                _resourceManager = null;
                _session = null;
                _ipAddress = null;
                throw;
            }
        }

        public async Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            if (!IsConnected && _resourceManager == null)
                return;

            await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_session != null)
                {
                    try { _session.RawIO.Write(":SYST:LOC\n"); } catch { }
                    try { _session.Dispose(); } catch { }
                    _session = null;
                }

                if (_resourceManager != null)
                {
                    try { _resourceManager.Dispose(); } catch { }
                    _resourceManager = null;
                }

                _ipAddress = null;
            }
            finally
            {
                _ioLock.Release();
            }
        }

        public Task SendAsync(string scpi, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(scpi))
                throw new ArgumentException("scpi is required", nameof(scpi));

            return IoAsync(scpi, expectResponse: false, cancellationToken);
        }

        public Task<string> QueryAsync(string scpi, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(scpi))
                throw new ArgumentException("scpi is required", nameof(scpi));

            return IoAsync(scpi, expectResponse: true, cancellationToken);
        }

        public async Task<DmmReading> ReadOnceAsync(DmmMeasureMode mode, DmmReadOptions options = null, CancellationToken cancellationToken = default)
        {
            EnsureConnected();

            int? previousTimeout = null;
            if (options?.TimeoutMilliseconds != null)
            {
                try
                {
                    previousTimeout = _session.TimeoutMilliseconds;
                    _session.TimeoutMilliseconds = options.TimeoutMilliseconds.Value;
                }
                catch
                {
                    previousTimeout = null;
                }
            }

            try
            {
                if (mode == DmmMeasureMode.FREQ && options?.FrequencyRangeIndex != null)
                {
                    await TrySendFrequencyModeAsync(cancellationToken).ConfigureAwait(false);
                    await SendAsync($":MEASure:FREQuency {options.FrequencyRangeIndex.Value}", cancellationToken).ConfigureAwait(false);
                }

                var (query, unit) = GetQuery(mode);
                var raw = await QueryAsync(query, cancellationToken).ConfigureAwait(false);
                raw = raw?.Trim();

                return ParseReading(raw, unit);
            }
            finally
            {
                if (previousTimeout != null)
                {
                    try { _session.TimeoutMilliseconds = previousTimeout.Value; } catch { }
                }
            }
        }

        private async Task TrySendFrequencyModeAsync(CancellationToken cancellationToken)
        {
            try
            {
                await SendAsync("FREQ", cancellationToken).ConfigureAwait(false);
                await Task.Delay(80, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch
            {
            }

            try
            {
                await SendAsync("FUNC FREQ", cancellationToken).ConfigureAwait(false);
                await Task.Delay(80, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
            }
        }

        private static (string Query, string Unit) GetQuery(DmmMeasureMode mode)
        {
            return mode switch
            {
                DmmMeasureMode.DCV => (":MEAS:VOLT:DC?", "V"),
                DmmMeasureMode.ACV => (":MEAS:VOLT:AC?", "V"),
                DmmMeasureMode.DCI => (":MEAS:CURR:DC?", "A"),
                DmmMeasureMode.ACI => (":MEAS:CURR:AC?", "A"),
                DmmMeasureMode.RES => (":MEAS:RES?", "Ω"),
                DmmMeasureMode.CAP => (":MEAS:CAP?", "F"),
                DmmMeasureMode.DIODE => (":MEAS:DIODe?", "V"),
                DmmMeasureMode.FREQ => (":MEASure:FREQuency?", "Hz"),
                DmmMeasureMode.CONT => (":MEAS:CONT?", ""),
                _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
            };
        }

        private void EnsureConnected()
        {
            if (_session == null)
                throw new InvalidOperationException("DMM is not connected");
        }

        private async Task<string> IoAsync(string scpi, bool expectResponse, CancellationToken cancellationToken)
        {
            EnsureConnected();

            await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var cmd = scpi.EndsWith("\n", StringComparison.Ordinal) ? scpi : scpi + "\n";
                _session.RawIO.Write(cmd);
                if (!expectResponse)
                    return null;
                return _session.RawIO.ReadString();
            }
            finally
            {
                _ioLock.Release();
            }
        }

        private static DmmReading ParseReading(string raw, string unit)
        {
            var reading = new DmmReading
            {
                Raw = raw,
                Unit = unit
            };

            if (string.IsNullOrWhiteSpace(raw))
            {
                reading.Value = null;
                return reading;
            }

            if (IsOverrangeRaw(raw))
            {
                reading.IsOverrange = true;
                reading.Value = null;
                return reading;
            }

            if (double.TryParse(raw.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var dv) ||
                double.TryParse(raw.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out dv))
            {
                if (double.IsNaN(dv) || double.IsInfinity(dv) || Math.Abs(dv) >= 1e36)
                {
                    reading.IsOverrange = true;
                    reading.Value = null;
                    return reading;
                }

                reading.Value = dv;
                return reading;
            }

            reading.Value = null;
            return reading;
        }

        private static bool IsOverrangeRaw(string raw)
        {
            var s = raw.Trim();
            if (s.Equals("OL", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("OVLD", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("OVER", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("OVERLOAD", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("INF", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("INFINITY", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("+INF", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("-INF", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return s.IndexOf("OVLD", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   s.IndexOf("OVER", StringComparison.OrdinalIgnoreCase) >= 0;
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
                try { _ioLock.Dispose(); } catch { }
            }
        }
    }
}
