using System;
using System.Globalization;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using NationalInstruments.Visa;

namespace MeasureControl.Services.HardwareApis
{
    public enum FrequencyCounterChannel
    {
        CH1 = 1,
        CH2 = 2
    }

    public enum FrequencyCounterMeasureMode
    {
        Frequency,
        Period,
        TimeInterval,
        PulseWidth,
        DutyCycle
    }

    public sealed class FrequencyCounterReadOptions
    {
        public int? TimeoutMilliseconds { get; set; }
    }

    public sealed class FrequencyCounterReading
    {
        public double? Value { get; set; }
        public string Raw { get; set; }
        public string Unit { get; set; }
    }

    public interface IFrequencyCounterApi : IAsyncDisposable
    {
        bool IsConnected { get; }
        string IpAddress { get; }

        Task ConnectAsync(string ipAddress, CancellationToken cancellationToken = default);
        Task DisconnectAsync(CancellationToken cancellationToken = default);

        Task<FrequencyCounterReading> ReadOnceAsync(
            FrequencyCounterMeasureMode mode,
            FrequencyCounterChannel channel,
            FrequencyCounterReadOptions options = null,
            CancellationToken cancellationToken = default);

        Task SendAsync(string scpi, CancellationToken cancellationToken = default);
        Task<string> QueryAsync(string scpi, CancellationToken cancellationToken = default);
    }

    public sealed class FrequencyCounterSocketApi : IFrequencyCounterApi
    {
        private const int FixedPort = 5025;

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
            var resourceString = $"TCPIP0::{trimmed}::{FixedPort}::SOCKET";

            MessageBasedSession session = null;
            try
            {
                session = (MessageBasedSession)_resourceManager.Open(resourceString, 0, 5000);
                try
                {
                    session.TimeoutMilliseconds = 3000;
                    session.TerminationCharacterEnabled = true;
                    session.TerminationCharacter = (byte)'\n';
                }
                catch
                {
                }

                _session = session;
                _ipAddress = trimmed;

                await SendAsync("*CLS", cancellationToken).ConfigureAwait(false);
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

        public async Task<FrequencyCounterReading> ReadOnceAsync(
            FrequencyCounterMeasureMode mode,
            FrequencyCounterChannel channel,
            FrequencyCounterReadOptions options = null,
            CancellationToken cancellationToken = default)
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
                var (query, unit) = GetQuery(mode, channel);
                var raw = await QueryAsync(query, cancellationToken).ConfigureAwait(false);
                raw = raw?.Trim();

                return new FrequencyCounterReading
                {
                    Raw = raw,
                    Unit = unit,
                    Value = TryParseDouble(raw)
                };
            }
            finally
            {
                if (previousTimeout != null)
                {
                    try { _session.TimeoutMilliseconds = previousTimeout.Value; } catch { }
                }
            }
        }

        private static (string Query, string Unit) GetQuery(FrequencyCounterMeasureMode mode, FrequencyCounterChannel channel)
        {
            int ch = (int)channel;

            return mode switch
            {
                FrequencyCounterMeasureMode.Frequency => ($"MEASure:FREQuency? (@{ch})", "Hz"),
                FrequencyCounterMeasureMode.Period => ($"MEASure:PERiod? (@{ch})", "s"),
                FrequencyCounterMeasureMode.TimeInterval => ($"MEASure:TINTeRval? (@{ch})", "s"),
                FrequencyCounterMeasureMode.PulseWidth => ($"MEASure:PWIDth? (@{ch})", "s"),
                FrequencyCounterMeasureMode.DutyCycle => ($"MEASure:PDUTycycle? 50,(@{ch})", "%"),
                _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
            };
        }

        private void EnsureConnected()
        {
            if (_session == null)
                throw new InvalidOperationException("Frequency counter is not connected");
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

        private static double? TryParseDouble(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            if (double.TryParse(raw.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                return v;

            if (double.TryParse(raw.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out v))
                return v;

            return null;
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
