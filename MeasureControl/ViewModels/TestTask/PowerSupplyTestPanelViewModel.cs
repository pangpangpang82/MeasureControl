using System;
using System.Globalization;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using MeasureControl.Models.Devices;
using MeasureControl.Services;
using MeasureControl.Views.Dialogs;
using Prism.Commands;
using Prism.Mvvm;

namespace MeasureControl.ViewModels.TestTask
{
    public class PowerSupplyTestPanelViewModel : BindableBase, IDisposable, ICloseGuard
    {
        private readonly IPxiChassisService _pxiChassisService;
        private readonly PowerSupplyDevice _powerSupplyDevice;
        private readonly int _powerSupplyPort;
        private TcpClient _client;
        private NetworkStream _stream;
        private readonly SemaphoreSlim _ioLock = new SemaphoreSlim(1, 1);
        private bool _disposed;

        public PowerSupplyDevice Device => _powerSupplyDevice;

        private CancellationTokenSource _measurementPollCts;
        private Task _measurementPollTask;
        private int _measurementPollIntervalMs = 800;
        private int _measurementPollingSuspendCount;

        private CancellationTokenSource _ch1LiveVoltCts;
        private CancellationTokenSource _ch1LiveCurrCts;
        private CancellationTokenSource _ch2LiveVoltCts;
        private CancellationTokenSource _ch2LiveCurrCts;
        private CancellationTokenSource _ch3LiveVoltCts;
        private CancellationTokenSource _ch3LiveCurrCts;

        private IDisposable SuspendMeasurementPolling()
        {
            Interlocked.Increment(ref _measurementPollingSuspendCount);
            return new MeasurementPollingSuspension(this);
        }

        private sealed class MeasurementPollingSuspension : IDisposable
        {
            private PowerSupplyTestPanelViewModel _owner;

            public MeasurementPollingSuspension(PowerSupplyTestPanelViewModel owner)
            {
                _owner = owner;
            }

            public void Dispose()
            {
                var owner = Interlocked.Exchange(ref _owner, null);
                if (owner == null)
                    return;

                Interlocked.Decrement(ref owner._measurementPollingSuspendCount);
            }
        }

        private string _cardName;
        public string CardName
        {
            get => _cardName;
            set => SetProperty(ref _cardName, value);
        }

        private string _powerSupplyIpAddress;
        public string PowerSupplyIpAddress
        {
            get => _powerSupplyIpAddress;
            set => SetProperty(ref _powerSupplyIpAddress, value);
        }

        private bool _isPowerSupplyConnected;
        public bool IsPowerSupplyConnected
        {
            get => _isPowerSupplyConnected;
            set
            {
                if (SetProperty(ref _isPowerSupplyConnected, value))
                {
                    RaisePropertyChanged(nameof(PowerSupplyConnectButtonText));
                }
            }
        }

        private bool _isPowerSupplyConnecting;
        public bool IsPowerSupplyConnecting
        {
            get => _isPowerSupplyConnecting;
            private set => SetProperty(ref _isPowerSupplyConnecting, value);
        }

        public string PowerSupplyConnectButtonText => IsPowerSupplyConnected ? "断开中" : "连接中";

        private string _connectionStatus;
        public string ConnectionStatus
        {
            get => _connectionStatus;
            set => SetProperty(ref _connectionStatus, value);
        }

        private string _deviceIdn;
        public string DeviceIdn
        {
            get => _deviceIdn;
            set => SetProperty(ref _deviceIdn, value);
        }

        private string _ch1SetVoltage;
        public string Ch1SetVoltage
        {
            get => _ch1SetVoltage;
            set
            {
                if (SetProperty(ref _ch1SetVoltage, value))
                    ScheduleLiveSetVoltage("1");
            }
        }

        private string _ch1SetCurrent;
        public string Ch1SetCurrent
        {
            get => _ch1SetCurrent;
            set
            {
                if (SetProperty(ref _ch1SetCurrent, value))
                    ScheduleLiveSetCurrent("1");
            }
        }

        private string _ch1LimitVoltage;
        public string Ch1LimitVoltage
        {
            get => _ch1LimitVoltage;
            set => SetProperty(ref _ch1LimitVoltage, value);
        }

        private string _ch1LimitCurrent;
        public string Ch1LimitCurrent
        {
            get => _ch1LimitCurrent;
            set => SetProperty(ref _ch1LimitCurrent, value);
        }

        private string _ch1LimitPower;
        public string Ch1LimitPower
        {
            get => _ch1LimitPower;
            set => SetProperty(ref _ch1LimitPower, value);
        }

        private bool _ch1ProtectionEnabled;
        public bool Ch1ProtectionEnabled
        {
            get => _ch1ProtectionEnabled;
            set => SetProperty(ref _ch1ProtectionEnabled, value);
        }

        private bool _ch1OutputEnabled;
        public bool Ch1OutputEnabled
        {
            get => _ch1OutputEnabled;
            set => SetProperty(ref _ch1OutputEnabled, value);
        }

        private string _ch1MeasVoltage;
        public string Ch1MeasVoltage
        {
            get => _ch1MeasVoltage;
            set => SetProperty(ref _ch1MeasVoltage, value);
        }

        private string _ch1MeasCurrent;
        public string Ch1MeasCurrent
        {
            get => _ch1MeasCurrent;
            set => SetProperty(ref _ch1MeasCurrent, value);
        }

        private string _ch1MeasPower;
        public string Ch1MeasPower
        {
            get => _ch1MeasPower;
            set => SetProperty(ref _ch1MeasPower, value);
        }

        private string _ch2SetVoltage;
        public string Ch2SetVoltage
        {
            get => _ch2SetVoltage;
            set
            {
                if (SetProperty(ref _ch2SetVoltage, value))
                    ScheduleLiveSetVoltage("2");
            }
        }

        private string _ch2SetCurrent;
        public string Ch2SetCurrent
        {
            get => _ch2SetCurrent;
            set
            {
                if (SetProperty(ref _ch2SetCurrent, value))
                    ScheduleLiveSetCurrent("2");
            }
        }

        private string _ch2LimitVoltage;
        public string Ch2LimitVoltage
        {
            get => _ch2LimitVoltage;
            set => SetProperty(ref _ch2LimitVoltage, value);
        }

        private string _ch2LimitCurrent;
        public string Ch2LimitCurrent
        {
            get => _ch2LimitCurrent;
            set => SetProperty(ref _ch2LimitCurrent, value);
        }

        private string _ch2LimitPower;
        public string Ch2LimitPower
        {
            get => _ch2LimitPower;
            set => SetProperty(ref _ch2LimitPower, value);
        }

        private bool _ch2ProtectionEnabled;
        public bool Ch2ProtectionEnabled
        {
            get => _ch2ProtectionEnabled;
            set => SetProperty(ref _ch2ProtectionEnabled, value);
        }

        private bool _ch2OutputEnabled;
        public bool Ch2OutputEnabled
        {
            get => _ch2OutputEnabled;
            set => SetProperty(ref _ch2OutputEnabled, value);
        }

        private string _ch2MeasVoltage;
        public string Ch2MeasVoltage
        {
            get => _ch2MeasVoltage;
            set => SetProperty(ref _ch2MeasVoltage, value);
        }

        private string _ch2MeasCurrent;
        public string Ch2MeasCurrent
        {
            get => _ch2MeasCurrent;
            set => SetProperty(ref _ch2MeasCurrent, value);
        }

        private string _ch2MeasPower;
        public string Ch2MeasPower
        {
            get => _ch2MeasPower;
            set => SetProperty(ref _ch2MeasPower, value);
        }

        private string _ch3SetVoltage;
        public string Ch3SetVoltage
        {
            get => _ch3SetVoltage;
            set
            {
                if (SetProperty(ref _ch3SetVoltage, value))
                    ScheduleLiveSetVoltage("3");
            }
        }

        private string _ch3SetCurrent;
        public string Ch3SetCurrent
        {
            get => _ch3SetCurrent;
            set
            {
                if (SetProperty(ref _ch3SetCurrent, value))
                    ScheduleLiveSetCurrent("3");
            }
        }

        private string _ch3LimitVoltage;
        public string Ch3LimitVoltage
        {
            get => _ch3LimitVoltage;
            set => SetProperty(ref _ch3LimitVoltage, value);
        }

        private string _ch3LimitCurrent;
        public string Ch3LimitCurrent
        {
            get => _ch3LimitCurrent;
            set => SetProperty(ref _ch3LimitCurrent, value);
        }

        private string _ch3LimitPower;
        public string Ch3LimitPower
        {
            get => _ch3LimitPower;
            set => SetProperty(ref _ch3LimitPower, value);
        }

        private bool _ch3ProtectionEnabled;
        public bool Ch3ProtectionEnabled
        {
            get => _ch3ProtectionEnabled;
            set => SetProperty(ref _ch3ProtectionEnabled, value);
        }

        private bool _ch3OutputEnabled;
        public bool Ch3OutputEnabled
        {
            get => _ch3OutputEnabled;
            set => SetProperty(ref _ch3OutputEnabled, value);
        }

        private string _ch3MeasVoltage;
        public string Ch3MeasVoltage
        {
            get => _ch3MeasVoltage;
            set => SetProperty(ref _ch3MeasVoltage, value);
        }

        private string _ch3MeasCurrent;
        public string Ch3MeasCurrent
        {
            get => _ch3MeasCurrent;
            set => SetProperty(ref _ch3MeasCurrent, value);
        }

        private string _ch3MeasPower;
        public string Ch3MeasPower
        {
            get => _ch3MeasPower;
            set => SetProperty(ref _ch3MeasPower, value);
        }

        public DelegateCommand ToggleDeviceCommand { get; private set; }
        public DelegateCommand<string> ToggleChannelOutputCommand { get; private set; }

        public PowerSupplyTestPanelViewModel(string testTaskName, string configTableName, string chassisName, PowerSupplyDevice device, IPxiChassisService pxiChassisService)
        {
            _pxiChassisService = pxiChassisService;
            _powerSupplyDevice = device;
            ConnectionStatus = "离线";

            var ip = _powerSupplyDevice?.IpAddress;

            PowerSupplyIpAddress = string.IsNullOrWhiteSpace(ip) ? "192.168.1.15" : ip;
            _powerSupplyPort = 30000;

            Ch1SetVoltage = "0";
            Ch1SetCurrent = "0";
            Ch2SetVoltage = "0";
            Ch2SetCurrent = "0";
            Ch3SetVoltage = "0";
            Ch3SetCurrent = "0";

            Ch1LimitVoltage = string.Empty;
            Ch1LimitCurrent = string.Empty;
            Ch1LimitPower = string.Empty;
            Ch1ProtectionEnabled = false;

            Ch2LimitVoltage = string.Empty;
            Ch2LimitCurrent = string.Empty;
            Ch2LimitPower = string.Empty;
            Ch2ProtectionEnabled = false;

            Ch3LimitVoltage = string.Empty;
            Ch3LimitCurrent = string.Empty;
            Ch3LimitPower = string.Empty;
            Ch3ProtectionEnabled = false;

            InitializeCommands();
        }

        private void InitializeCommands()
        {
            ToggleDeviceCommand = new DelegateCommand(async () => await ToggleDeviceAsync());
            ToggleChannelOutputCommand = new DelegateCommand<string>(async ch => await ToggleOutputAsync(ch));
        }

        private async Task ToggleDeviceAsync()
        {
            if (IsPowerSupplyConnected)
            {
                await DisconnectAsync();
            }
            else
            {
                await ConnectAsync();
            }
        }

        private async Task ConnectAsync()
        {
            if (string.IsNullOrWhiteSpace(PowerSupplyIpAddress))
            {
                ReMessageBox.Show("请输入电源IP地址", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var port = _powerSupplyPort;
            if (port <= 0 || port > 65535)
            {
                ReMessageBox.Show("端口号无效", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                ConnectionStatus = "检测中";
                IsPowerSupplyConnected = false;
                IsPowerSupplyConnecting = true;

                string idn = null;

                await Task.Run(async () =>
                {
                    _client = new TcpClient();
                    await ConnectTcpClientWithTimeoutAsync(_client, PowerSupplyIpAddress.Trim(), port, 5000);
                    _stream = _client.GetStream();

                    idn = await QueryScpiAsync("*IDN?", 5000);
                });

                Application.Current.Dispatcher.Invoke(() =>
                {
                    DeviceIdn = idn;
                });

                await SendScpiAsync("SYST:REM", 5000);
                await ReportScpiErrorToStatusAsync("SYST:REM");

                IsPowerSupplyConnected = true;
                ConnectionStatus = "在线";

                await RefreshMeasurementsAsync("1");
                await RefreshMeasurementsAsync("2");
                await RefreshMeasurementsAsync("3");

                StartMeasurementPolling();
            }
            catch (Exception ex)
            {
                ConnectionStatus = "离线";
                IsPowerSupplyConnected = false;
                SafeCloseNetworkStream(ref _stream);
                SafeCloseTcpClient(ref _client);

                ReMessageBox.Show($"连接失败: {ex.Message}", "连接错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsPowerSupplyConnecting = false;
            }
        }

        private async Task DisconnectAsync()
        {
            try
            {
                ConnectionStatus = "断开中";

                IsPowerSupplyConnecting = true;

                StopMeasurementPolling();

                await Task.Run(() =>
                {
                    SafeCloseNetworkStream(ref _stream);
                    SafeCloseTcpClient(ref _client);
                });
            }
            catch
            {
            }
            finally
            {
                IsPowerSupplyConnecting = false;
            }

            IsPowerSupplyConnected = false;
            ConnectionStatus = "离线";
        }

        private void StartMeasurementPolling()
        {
            StopMeasurementPolling();

            _measurementPollCts = new CancellationTokenSource();
            var token = _measurementPollCts.Token;

            _measurementPollTask = Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    if (Volatile.Read(ref _measurementPollingSuspendCount) > 0)
                    {
                        try
                        {
                            await Task.Delay(50, token);
                        }
                        catch
                        {
                        }

                        continue;
                    }

                    if (IsPowerSupplyConnected && _stream != null)
                    {
                        try
                        {
                            await RefreshMeasurementsAsync("1");
                            await RefreshMeasurementsAsync("2");
                            await RefreshMeasurementsAsync("3");
                        }
                        catch
                        {
                        }
                    }

                    try
                    {
                        await Task.Delay(_measurementPollIntervalMs, token);
                    }
                    catch
                    {
                    }
                }
            }, token);
        }

        private void StopMeasurementPolling()
        {
            var cts = _measurementPollCts;
            _measurementPollCts = null;
            _measurementPollTask = null;

            if (cts == null)
                return;

            try { cts.Cancel(); } catch { }
            try { cts.Dispose(); } catch { }
        }

        private async Task ApplyChannelAsync(string channel, bool refreshMeasurements = true)
        {
            if (!IsPowerSupplyConnected || _stream == null)
            {
                ReMessageBox.Show("电源未连接", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            string ch = NormalizeChannel(channel);
            string v = ch == "1" ? Ch1SetVoltage : ch == "2" ? Ch2SetVoltage : Ch3SetVoltage;
            string c = ch == "1" ? Ch1SetCurrent : ch == "2" ? Ch2SetCurrent : Ch3SetCurrent;
            string chanList = FormatChanList(ch);

            if (string.IsNullOrWhiteSpace(v) && string.IsNullOrWhiteSpace(c))
            {
                if (refreshMeasurements)
                    await RefreshMeasurementsAsync(ch);
                return;
            }

            // IT-N6300 编程手册：APPLy <V>,<I>[,(@chanlist)]
            string vv = NormalizeScpiNumberOrDefault(v, "0");
            string cc = NormalizeScpiNumberOrDefault(c, "0");
            await SendScpiAsync($"APPLy {vv},{cc},{chanList}", 5000);
            await ReportScpiErrorToStatusAsync($"APPLy CH{ch}");

            if (refreshMeasurements)
                await RefreshMeasurementsAsync(ch);
        }

        private void ScheduleLiveSetVoltage(string channel)
        {
            string ch = NormalizeChannel(channel);
            if (!GetChannelOutputEnabled(ch))
                return;

            var old = GetAndReplaceLiveCtsForVoltage(ch);
            try { old?.Cancel(); } catch { }
            try { old?.Dispose(); } catch { }

            var cts = GetLiveCtsForVoltage(ch);
            _ = LiveSetVoltageDebouncedAsync(ch, cts.Token);
        }

        private void ScheduleLiveSetCurrent(string channel)
        {
            string ch = NormalizeChannel(channel);
            if (!GetChannelOutputEnabled(ch))
                return;

            var old = GetAndReplaceLiveCtsForCurrent(ch);
            try { old?.Cancel(); } catch { }
            try { old?.Dispose(); } catch { }

            var cts = GetLiveCtsForCurrent(ch);
            _ = LiveSetCurrentDebouncedAsync(ch, cts.Token);
        }

        private async Task LiveSetVoltageDebouncedAsync(string channel, CancellationToken token)
        {
            try
            {
                await Task.Delay(300);
                if (token.IsCancellationRequested)
                    return;

                if (!IsPowerSupplyConnected || _stream == null)
                    return;

                string ch = NormalizeChannel(channel);
                if (!GetChannelOutputEnabled(ch))
                    return;

                string input = ch == "1" ? Ch1SetVoltage : ch == "2" ? Ch2SetVoltage : Ch3SetVoltage;
                if (!TryNormalizeScpiNumber(input, out string vv))
                    return;

                string chanList = FormatChanList(ch);
                await SendScpiAsync($"VOLT {vv},{chanList}", 5000);

                if (token.IsCancellationRequested)
                    return;

                await RefreshMeasurementsAsync(ch);
            }
            catch
            {
            }
        }

        private async Task LiveSetCurrentDebouncedAsync(string channel, CancellationToken token)
        {
            try
            {
                await Task.Delay(300);
                if (token.IsCancellationRequested)
                    return;

                if (!IsPowerSupplyConnected || _stream == null)
                    return;

                string ch = NormalizeChannel(channel);
                if (!GetChannelOutputEnabled(ch))
                    return;

                string input = ch == "1" ? Ch1SetCurrent : ch == "2" ? Ch2SetCurrent : Ch3SetCurrent;
                if (!TryNormalizeScpiNumber(input, out string cc))
                    return;

                string chanList = FormatChanList(ch);
                await SendScpiAsync($"CURR {cc},{chanList}", 5000);

                if (token.IsCancellationRequested)
                    return;

                await RefreshMeasurementsAsync(ch);
            }
            catch
            {
            }
        }

        private CancellationTokenSource GetAndReplaceLiveCtsForVoltage(string channel)
        {
            var newCts = new CancellationTokenSource();
            if (channel == "1") { var old = _ch1LiveVoltCts; _ch1LiveVoltCts = newCts; return old; }
            if (channel == "2") { var old = _ch2LiveVoltCts; _ch2LiveVoltCts = newCts; return old; }
            { var old = _ch3LiveVoltCts; _ch3LiveVoltCts = newCts; return old; }
        }

        private CancellationTokenSource GetAndReplaceLiveCtsForCurrent(string channel)
        {
            var newCts = new CancellationTokenSource();
            if (channel == "1") { var old = _ch1LiveCurrCts; _ch1LiveCurrCts = newCts; return old; }
            if (channel == "2") { var old = _ch2LiveCurrCts; _ch2LiveCurrCts = newCts; return old; }
            { var old = _ch3LiveCurrCts; _ch3LiveCurrCts = newCts; return old; }
        }

        private CancellationTokenSource GetLiveCtsForVoltage(string channel)
        {
            if (channel == "1") return _ch1LiveVoltCts;
            if (channel == "2") return _ch2LiveVoltCts;
            return _ch3LiveVoltCts;
        }

        private CancellationTokenSource GetLiveCtsForCurrent(string channel)
        {
            if (channel == "1") return _ch1LiveCurrCts;
            if (channel == "2") return _ch2LiveCurrCts;
            return _ch3LiveCurrCts;
        }

        private static bool TryNormalizeScpiNumber(string input, out string normalized)
        {
            normalized = null;
            if (string.IsNullOrWhiteSpace(input))
                return false;

            string s = input.Trim();
            if (s.EndsWith(".", StringComparison.Ordinal) || string.Equals(s, "-", StringComparison.Ordinal) || string.Equals(s, "+", StringComparison.Ordinal))
                return false;

            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
            {
                normalized = v.ToString("0.########", CultureInfo.InvariantCulture);
                return true;
            }

            if (double.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out v))
            {
                normalized = v.ToString("0.########", CultureInfo.InvariantCulture);
                return true;
            }

            return false;
        }

        private async Task ApplyProtectionAsync(string channel)
        {
            if (!IsPowerSupplyConnected || _stream == null)
                return;

            string ch = NormalizeChannel(channel);
            string chanList = FormatChanList(ch);

            string lv;
            string lc;
            string lp;
            bool enabled;

            if (ch == "1")
            {
                lv = Ch1LimitVoltage;
                lc = Ch1LimitCurrent;
                lp = Ch1LimitPower;
                enabled = Ch1ProtectionEnabled;
            }
            else if (ch == "2")
            {
                lv = Ch2LimitVoltage;
                lc = Ch2LimitCurrent;
                lp = Ch2LimitPower;
                enabled = Ch2ProtectionEnabled;
            }
            else
            {
                lv = Ch3LimitVoltage;
                lc = Ch3LimitCurrent;
                lp = Ch3LimitPower;
                enabled = Ch3ProtectionEnabled;
            }

            try
            {
                if (!enabled)
                {
                    await SendScpiAsync($"VOLT:OVER:PROT:STAT OFF,{chanList}", 5000);
                    await SendScpiAsync($"CURR:OVER:PROT:STAT OFF,{chanList}", 5000);
                    await SendScpiAsync($"POW:PROT:STAT OFF,{chanList}", 5000);
                    await ReportScpiErrorToStatusAsync($"PROT OFF CH{ch}");
                    return;
                }

                if (!string.IsNullOrWhiteSpace(lv))
                    await SendScpiAsync($"VOLT:OVER:PROT {NormalizeScpiNumberOrDefault(lv, lv.Trim())},{chanList}", 5000);
                if (!string.IsNullOrWhiteSpace(lc))
                    await SendScpiAsync($"CURR:OVER:PROT {NormalizeScpiNumberOrDefault(lc, lc.Trim())},{chanList}", 5000);
                if (!string.IsNullOrWhiteSpace(lp))
                    await SendScpiAsync($"POW:PROT {NormalizeScpiNumberOrDefault(lp, lp.Trim())},{chanList}", 5000);

                await SendScpiAsync($"VOLT:OVER:PROT:STAT ON,{chanList}", 5000);
                await SendScpiAsync($"CURR:OVER:PROT:STAT ON,{chanList}", 5000);
                await SendScpiAsync($"POW:PROT:STAT ON,{chanList}", 5000);
                await ReportScpiErrorToStatusAsync($"PROT ON CH{ch}");
            }
            catch
            {
            }
        }

        private async Task ToggleOutputAsync(string channel)
        {
            if (!IsPowerSupplyConnected || _stream == null)
            {
                ReMessageBox.Show("电源未连接", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            string ch = NormalizeChannel(channel);
            bool enabled = GetChannelOutputEnabled(ch);
            string chanList = FormatChanList(ch);

            using (SuspendMeasurementPolling())
            {
                if (!enabled)
                {
                    await ApplyChannelAsync(ch, refreshMeasurements: false);
                    await ApplyProtectionAsync(ch);
                    // 清除保护锁存（避免上一次保护导致无法开启输出）
                    await SendScpiAsync("OUTP:PROT:CLE", 5000);
                    await SendScpiAsync($"OUTP ON,{chanList}", 5000);
                    await ReportScpiErrorToStatusAsync($"OUTP ON CH{ch}");
                    SetChannelOutputEnabled(ch, true);
                }
                else
                {
                    await SendScpiAsync($"OUTP OFF,{chanList}", 5000);
                    await ReportScpiErrorToStatusAsync($"OUTP OFF CH{ch}");
                    SetChannelOutputEnabled(ch, false);
                }

                await RefreshMeasurementsAsync(ch);
            }
        }

        private async Task RefreshMeasurementsAsync(string channel)
        {
            if (!IsPowerSupplyConnected || _stream == null)
                return;

            string ch = NormalizeChannel(channel);
            string chanList = FormatChanList(ch);

            try
            {
                var mv = await QueryScpiAsync($"MEAS:VOLT? {chanList}", 5000);
                var mc = await QueryScpiAsync($"MEAS:CURR? {chanList}", 5000);

                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (ch == "1")
                    {
                        Ch1MeasVoltage = mv;
                        Ch1MeasCurrent = mc;
                        Ch1MeasPower = CalculatePowerText(mv, mc);
                    }
                    else if (ch == "2")
                    {
                        Ch2MeasVoltage = mv;
                        Ch2MeasCurrent = mc;
                        Ch2MeasPower = CalculatePowerText(mv, mc);
                    }
                    else
                    {
                        Ch3MeasVoltage = mv;
                        Ch3MeasCurrent = mc;
                        Ch3MeasPower = CalculatePowerText(mv, mc);
                    }
                });
            }
            catch
            {
            }
        }

        private static string CalculatePowerText(string voltageText, string currentText)
        {
            if (string.IsNullOrWhiteSpace(voltageText) || string.IsNullOrWhiteSpace(currentText))
                return string.Empty;

            if (!double.TryParse(voltageText.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                return string.Empty;
            if (!double.TryParse(currentText.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var c))
                return string.Empty;

            var p = v * c;
            return p.ToString("F3", CultureInfo.InvariantCulture);
        }

        private static string NormalizeScpiNumberOrDefault(string input, string defaultValue)
        {
            if (string.IsNullOrWhiteSpace(input))
                return defaultValue;

            string s = input.Trim();
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                return v.ToString("0.########", CultureInfo.InvariantCulture);
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out v))
                return v.ToString("0.########", CultureInfo.InvariantCulture);

            return defaultValue;
        }

        private static string FormatChanList(string channel)
        {
            string ch = NormalizeChannel(channel);
            return $"(@{ch})";
        }

        private static string NormalizeChannel(string channel)
        {
            string ch = (channel ?? "").Trim();
            if (string.Equals(ch, "CH1", StringComparison.OrdinalIgnoreCase)) return "1";
            if (string.Equals(ch, "CH2", StringComparison.OrdinalIgnoreCase)) return "2";
            if (string.Equals(ch, "CH3", StringComparison.OrdinalIgnoreCase)) return "3";
            if (ch == "1" || ch == "2" || ch == "3") return ch;
            return "1";
        }

        private bool GetChannelOutputEnabled(string channel)
        {
            string ch = NormalizeChannel(channel);
            if (ch == "1") return Ch1OutputEnabled;
            if (ch == "2") return Ch2OutputEnabled;
            return Ch3OutputEnabled;
        }

        private void SetChannelOutputEnabled(string channel, bool enabled)
        {
            string ch = NormalizeChannel(channel);
            if (ch == "1") Ch1OutputEnabled = enabled;
            else if (ch == "2") Ch2OutputEnabled = enabled;
            else Ch3OutputEnabled = enabled;
        }

        private async Task SelectChannelAsync(string channel)
        {
            if (!IsPowerSupplyConnected || _stream == null)
                return;

            string ch = NormalizeChannel(channel);
            await SendScpiAsync($"INST:NSEL {ch}", 5000);
        }

        private async Task ReportScpiErrorToStatusAsync(string context)
        {
            if (!IsPowerSupplyConnected || _stream == null)
                return;

            return;

            try
            {
                string resp = await QueryScpiAsync("SYST:ERR?", 2000);
                if (string.IsNullOrWhiteSpace(resp))
                    return;

                string r = resp.Trim();
                if (r.StartsWith("0", StringComparison.Ordinal))
                    return;

                Application.Current.Dispatcher.Invoke(() =>
                {
                    ConnectionStatus = $"{context}: {r}";
                });
            }
            catch
            {
            }
        }

        private async Task<string> QueryScpiAsync(string command, int timeoutMs)
        {
            if (_stream == null)
                throw new InvalidOperationException("Power supply stream not initialized.");

            await _ioLock.WaitAsync();
            try
            {
                await WriteLineAsync(_stream, command, timeoutMs);
                return await ReadLineAsync(_stream, timeoutMs);
            }
            finally
            {
                _ioLock.Release();
            }
        }

        private async Task SendScpiAsync(string command, int timeoutMs)
        {
            if (_stream == null)
                throw new InvalidOperationException("Power supply stream not initialized.");

            await _ioLock.WaitAsync();
            try
            {
                await WriteLineAsync(_stream, command, timeoutMs);
            }
            finally
            {
                _ioLock.Release();
            }
        }

        private static async Task WriteLineAsync(NetworkStream stream, string command, int timeoutMs)
        {
            using (var cts = new CancellationTokenSource(timeoutMs))
            {
                string payload = command.EndsWith("\n", StringComparison.Ordinal) ? command : command + "\n";
                byte[] bytes = Encoding.ASCII.GetBytes(payload);
                await stream.WriteAsync(bytes, 0, bytes.Length, cts.Token);
                await stream.FlushAsync(cts.Token);
            }
        }

        private static async Task<string> ReadLineAsync(NetworkStream stream, int timeoutMs)
        {
            using (var cts = new CancellationTokenSource(timeoutMs))
            {
                var buffer = new byte[1];
                var sb = new StringBuilder();

                while (true)
                {
                    int read = await stream.ReadAsync(buffer, 0, 1, cts.Token);
                    if (read <= 0)
                        throw new InvalidOperationException("Connection closed by remote host.");

                    char ch = (char)buffer[0];
                    if (ch == '\n')
                        break;
                    if (ch != '\r')
                        sb.Append(ch);
                }

                return sb.ToString().Trim();
            }
        }

        private static async Task ConnectTcpClientWithTimeoutAsync(TcpClient client, string host, int port, int timeoutMs)
        {
            var connectTask = client.ConnectAsync(host, port);
            var delayTask = Task.Delay(timeoutMs);
            var completed = await Task.WhenAny(connectTask, delayTask);

            if (completed != connectTask)
            {
                try
                {
                    client.Close();
                }
                catch
                {
                }
                throw new TimeoutException("连接超时");
            }

            await connectTask;
        }

        private static void SafeCloseNetworkStream(ref NetworkStream stream)
        {
            try
            {
                if (stream != null)
                {
                    stream.Close();
                    stream.Dispose();
                }
            }
            catch
            {
            }
            finally
            {
                stream = null;
            }
        }

        private static void SafeCloseTcpClient(ref TcpClient client)
        {
            try
            {
                if (client != null)
                {
                    client.Close();
                }
            }
            catch
            {
            }
            finally
            {
                client = null;
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            try
            {
                StopMeasurementPolling();
                SafeCloseNetworkStream(ref _stream);
                SafeCloseTcpClient(ref _client);
            }
            catch
            {
            }

            try
            {
                _ioLock?.Dispose();
            }
            catch
            {
            }
        }

        public bool CanClose()
        {
            if (IsPowerSupplyConnecting)
            {
                ReMessageBox.Show($"正在打开程控电源({CardName})，请稍候连接完成后再切换页面", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }

            return true;
        }
    }
}
