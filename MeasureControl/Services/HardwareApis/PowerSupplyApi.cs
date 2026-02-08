using System;
using System.Globalization;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using NationalInstruments.Visa;

namespace MeasureControl.Services.HardwareApis
{
    public enum PowerSupplyChannel
    {
        CH1 = 1,
        CH2 = 2,
        CH3 = 3
    }

    public enum PowerSupplyReadMode
    {
        MeasuredVoltage,
        MeasuredCurrent,
        MeasuredPower
    }

    public sealed class PowerSupplyReadOptions
    {
        public int? TimeoutMilliseconds { get; set; }
    }

    public sealed class PowerSupplyReading
    {
        public double? Value { get; set; }
        public string Raw { get; set; }
        public string Unit { get; set; }
    }

    public sealed class PowerSupplyMeasurements
    {
        public PowerSupplyReading Voltage { get; set; }
        public PowerSupplyReading Current { get; set; }
        public PowerSupplyReading Power { get; set; }
    }

    public interface IPowerSupplyApi : IAsyncDisposable
    {
        bool IsConnected { get; }
        string IpAddress { get; }

        Task ConnectAsync(string ipAddress, CancellationToken cancellationToken = default);
        Task DisconnectAsync(CancellationToken cancellationToken = default);

        Task ApplyAsync(PowerSupplyChannel channel, double voltage, double current, CancellationToken cancellationToken = default);
        Task SetVoltageAsync(PowerSupplyChannel channel, double voltage, CancellationToken cancellationToken = default);
        Task SetCurrentAsync(PowerSupplyChannel channel, double current, CancellationToken cancellationToken = default);

        Task SetOverVoltageProtectionAsync(PowerSupplyChannel channel, double voltage, CancellationToken cancellationToken = default);
        Task SetOverCurrentProtectionAsync(PowerSupplyChannel channel, double current, CancellationToken cancellationToken = default);
        Task SetOverPowerProtectionAsync(PowerSupplyChannel channel, double power, CancellationToken cancellationToken = default);
        Task SetProtectionEnabledAsync(PowerSupplyChannel channel, bool enabled, CancellationToken cancellationToken = default);
        Task ClearProtectionAsync(CancellationToken cancellationToken = default);

        Task SetOutputEnabledAsync(PowerSupplyChannel channel, bool enabled, CancellationToken cancellationToken = default);

        Task<PowerSupplyReading> ReadOnceAsync(
            PowerSupplyReadMode mode,
            PowerSupplyChannel channel,
            PowerSupplyReadOptions options = null,
            CancellationToken cancellationToken = default);

        Task<PowerSupplyMeasurements> ReadMeasurementsAsync(
            PowerSupplyChannel channel,
            PowerSupplyReadOptions options = null,
            CancellationToken cancellationToken = default);

        Task SendAsync(string scpi, CancellationToken cancellationToken = default);
        Task<string> QueryAsync(string scpi, CancellationToken cancellationToken = default);
    }

    public sealed class PowerSupplySocketApi : IPowerSupplyApi
    {
        private const int FixedPort = 30000;

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
                    session.TimeoutMilliseconds = 5000;
                    session.TerminationCharacterEnabled = true;
                    session.TerminationCharacter = (byte)'\n';
                }
                catch
                {
                }

                _session = session;
                _ipAddress = trimmed;

                await SendAsync("*CLS", cancellationToken).ConfigureAwait(false);
                await SendAsync("SYST:REM", cancellationToken).ConfigureAwait(false);
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
                    try { _session.RawIO.Write("SYST:LOC\n"); } catch { }
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

        public Task ApplyAsync(PowerSupplyChannel channel, double voltage, double current, CancellationToken cancellationToken = default)
        {
            var vv = FormatScpiNumber(voltage);
            var cc = FormatScpiNumber(current);
            return SendAsync($"APPLy {vv},{cc},{FormatChanList(channel)}", cancellationToken);
        }

        public Task SetVoltageAsync(PowerSupplyChannel channel, double voltage, CancellationToken cancellationToken = default)
        {
            var vv = FormatScpiNumber(voltage);
            return SendAsync($"VOLT {vv},{FormatChanList(channel)}", cancellationToken);
        }

        public Task SetCurrentAsync(PowerSupplyChannel channel, double current, CancellationToken cancellationToken = default)
        {
            var cc = FormatScpiNumber(current);
            return SendAsync($"CURR {cc},{FormatChanList(channel)}", cancellationToken);
        }

        public Task SetOverVoltageProtectionAsync(PowerSupplyChannel channel, double voltage, CancellationToken cancellationToken = default)
        {
            var vv = FormatScpiNumber(voltage);
            return SendAsync($"VOLT:OVER:PROT {vv},{FormatChanList(channel)}", cancellationToken);
        }

        public Task SetOverCurrentProtectionAsync(PowerSupplyChannel channel, double current, CancellationToken cancellationToken = default)
        {
            var cc = FormatScpiNumber(current);
            return SendAsync($"CURR:OVER:PROT {cc},{FormatChanList(channel)}", cancellationToken);
        }

        public Task SetOverPowerProtectionAsync(PowerSupplyChannel channel, double power, CancellationToken cancellationToken = default)
        {
            var pp = FormatScpiNumber(power);
            return SendAsync($"POW:PROT {pp},{FormatChanList(channel)}", cancellationToken);
        }

        public async Task SetProtectionEnabledAsync(PowerSupplyChannel channel, bool enabled, CancellationToken cancellationToken = default)
        {
            var onOff = enabled ? "ON" : "OFF";
            var ch = FormatChanList(channel);

            await SendAsync($"VOLT:OVER:PROT:STAT {onOff},{ch}", cancellationToken).ConfigureAwait(false);
            await SendAsync($"CURR:OVER:PROT:STAT {onOff},{ch}", cancellationToken).ConfigureAwait(false);
            await SendAsync($"POW:PROT:STAT {onOff},{ch}", cancellationToken).ConfigureAwait(false);
        }

        public Task ClearProtectionAsync(CancellationToken cancellationToken = default)
        {
            return SendAsync("OUTP:PROT:CLE", cancellationToken);
        }

        public Task SetOutputEnabledAsync(PowerSupplyChannel channel, bool enabled, CancellationToken cancellationToken = default)
        {
            var onOff = enabled ? "ON" : "OFF";
            return SendAsync($"OUTP {onOff},{FormatChanList(channel)}", cancellationToken);
        }

        public async Task<PowerSupplyReading> ReadOnceAsync(
            PowerSupplyReadMode mode,
            PowerSupplyChannel channel,
            PowerSupplyReadOptions options = null,
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
                if (mode == PowerSupplyReadMode.MeasuredPower)
                {
                    var ms = await ReadMeasurementsAsync(channel, options, cancellationToken).ConfigureAwait(false);
                    return ms?.Power;
                }

                var (query, unit) = GetQuery(mode, channel);
                var raw = await QueryAsync(query, cancellationToken).ConfigureAwait(false);
                raw = raw?.Trim();

                return new PowerSupplyReading
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

        public async Task<PowerSupplyMeasurements> ReadMeasurementsAsync(
            PowerSupplyChannel channel,
            PowerSupplyReadOptions options = null,
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
                var ch = FormatChanList(channel);
                var rawV = await QueryAsync($"MEAS:VOLT? {ch}", cancellationToken).ConfigureAwait(false);
                var rawC = await QueryAsync($"MEAS:CURR? {ch}", cancellationToken).ConfigureAwait(false);

                rawV = rawV?.Trim();
                rawC = rawC?.Trim();

                var v = TryParseDouble(rawV);
                var c = TryParseDouble(rawC);

                double? p = null;
                if (v != null && c != null)
                    p = v.Value * c.Value;

                return new PowerSupplyMeasurements
                {
                    Voltage = new PowerSupplyReading { Raw = rawV, Unit = "V", Value = v },
                    Current = new PowerSupplyReading { Raw = rawC, Unit = "A", Value = c },
                    Power = new PowerSupplyReading { Raw = p?.ToString("0.########", CultureInfo.InvariantCulture), Unit = "W", Value = p }
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

        private static (string Query, string Unit) GetQuery(PowerSupplyReadMode mode, PowerSupplyChannel channel)
        {
            var ch = FormatChanList(channel);

            return mode switch
            {
                PowerSupplyReadMode.MeasuredVoltage => ($"MEAS:VOLT? {ch}", "V"),
                PowerSupplyReadMode.MeasuredCurrent => ($"MEAS:CURR? {ch}", "A"),
                PowerSupplyReadMode.MeasuredPower => ($"MEAS:POW? {ch}", "W"),
                _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
            };
        }

        private void EnsureConnected()
        {
            if (_session == null)
                throw new InvalidOperationException("Power supply is not connected");
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

        private static string FormatChanList(PowerSupplyChannel channel)
        {
            return $"(@{(int)channel})";
        }

        private static string FormatScpiNumber(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentOutOfRangeException(nameof(value));

            return value.ToString("0.########", CultureInfo.InvariantCulture);
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
