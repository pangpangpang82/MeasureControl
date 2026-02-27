using System;
using System.Globalization;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using NationalInstruments.Visa;

namespace MeasureControl.Services.HardwareApis
{
    /// <summary>
    /// 万用表测量模式
    /// </summary>
    public enum DmmMeasureMode
    {
        DCV,      // 直流电压
        ACV,      // 交流电压
        DCI,      // 直流电流
        ACI,      // 交流电流
        RES,      // 电阻
        CAP,      // 电容
        CONT,     // 通断测试
        DIODE,    // 二极管测试
        FREQ      // 频率
    }

    /// <summary>
    /// 万用表读数选项（可选配置）
    /// </summary>
    public sealed class DmmReadOptions
    {
        public int? FrequencyRangeIndex { get; set; }
        public int? TimeoutMilliseconds { get; set; }
    }

    /// <summary>
    /// 万用表读数结果
    /// </summary>
    public sealed class DmmReading
    {
        public double? Value { get; set; }
        public bool IsOverrange { get; set; }
        public string Raw { get; set; }
        public string Unit { get; set; }
    }

    /// <summary>
    /// 万用表控制接口（通过网络 Socket 连接）
    /// 功能：测量电压、电流、电阻、频率等
    /// </summary>
    public interface IDmmApi : IAsyncDisposable
    {
        bool IsConnected { get; }  // 是否已连接到万用表
        string IpAddress { get; }

        Task ConnectAsync(string ipAddress, CancellationToken cancellationToken = default);  // 连接到万用表（通过 IP 地址）
        Task DisconnectAsync(CancellationToken cancellationToken = default);  // 断开万用表连接

        Task<DmmReading> ReadOnceAsync(DmmMeasureMode mode, DmmReadOptions options = null, CancellationToken cancellationToken = default);  // 读取一次测量值

        Task SendAsync(string scpi, CancellationToken cancellationToken = default);
        Task<string> QueryAsync(string scpi, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// 万用表 Socket 通信实现类
    /// 使用 VISA 库通过 TCP/IP 与万用表通信（默认端口 5555）
    /// </summary>
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

        /// <summary>
        /// 连接到万用表
        /// </summary>
        /// <param name="ipAddress">万用表的 IP 地址</param>
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

                // 清除错误队列
                await SendAsync("*CLS", cancellationToken).ConfigureAwait(false);
                // 切换到远程控制模式
                await SendAsync(":SYST:REM", cancellationToken).ConfigureAwait(false);
                // 查询设备标识（验证连接）
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
            // 如果未连接，直接返回（安全的重复调用）
            if (!IsConnected && _resourceManager == null)
                return;

            await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_session != null)
                {
                    // 切换回本地控制模式（恢复万用表面板操作）
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

        /// <summary>
        /// 发送 SCPI 命令到万用表（不等待响应）
        /// </summary>
        public Task SendAsync(string scpi, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(scpi))
                throw new ArgumentException("scpi is required", nameof(scpi));

            return IoAsync(scpi, expectResponse: false, cancellationToken);
        }

        /// <summary>
        /// 发送 SCPI 查询命令并读取响应
        /// </summary>
        public Task<string> QueryAsync(string scpi, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(scpi))
                throw new ArgumentException("scpi is required", nameof(scpi));

            return IoAsync(scpi, expectResponse: true, cancellationToken);
        }

        /// <summary>
        /// 读取一次测量值
        /// </summary>
        /// <param name="mode">测量模式（电压/电流/电阻等）</param>
        /// <param name="options">可选配置（超时时间、频率档位等）</param>
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

        /// <summary>
        /// 尝试设置万用表为频率测量模式（兼容不同型号的命令格式）
        /// </summary>
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

        /// <summary>
        /// 根据测量模式获取对应的 SCPI 查询命令和单位
        /// </summary>
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

        /// <summary>
        /// 确保已连接到万用表，否则抛出异常
        /// </summary>
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

        /// <summary>
        /// 解析万用表返回的原始字符串为读数对象
        /// </summary>
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

        /// <summary>
        /// 判断原始读数是否为超量程（OL/OVLD/INF 等）
        /// </summary>
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
