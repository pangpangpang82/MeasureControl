using System;
using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Ivi.Visa;
using MeasureControl.Models.Devices;
using MeasureControl.Services;
using MeasureControl.Views.Dialogs;
using NationalInstruments.Visa;
using Prism.Commands;
using Prism.Mvvm;

namespace MeasureControl.ViewModels.TestTask
{
    public class ElectronicLoadTestPanelViewModel : BindableBase, IDisposable, ICloseGuard
    {
        private readonly IPxiChassisService _pxiChassisService;
        private readonly ElectronicLoadDevice _device;

        private ResourceManager _resourceManager;
        private MessageBasedSession _session;
        private readonly SemaphoreSlim _ioLock = new SemaphoreSlim(1, 1);
        private CancellationTokenSource _pollCts;
        private Task _pollTask;
        private bool _disposed;

        private bool _suppressModeApply;

        public ElectronicLoadDevice Device => _device;

        public ObservableCollection<string> ModeOptions { get; }

        private string _visaResource;
        public string VisaResource
        {
            get => _visaResource;
            set => SetProperty(ref _visaResource, value);
        }

        private bool _isElectronicLoadConnected;
        public bool IsElectronicLoadConnected
        {
            get => _isElectronicLoadConnected;
            set
            {
                if (SetProperty(ref _isElectronicLoadConnected, value))
                {
                    RaisePropertyChanged(nameof(ElectronicLoadConnectButtonText));
                }
            }
        }

        private static string NormalizeMode(string mode)
        {
            var m = (mode ?? "").Trim().ToUpperInvariant();
            switch (m)
            {
                case "CCL":
                case "CCH":
                case "CRL":
                case "CRH":
                case "CV":
                case "CPL":
                case "CPH":
                    return m;
                default:
                    return "";
            }
        }

        private async Task TryAutoFillUsbVisaResourceAsync()
        {
            if (_resourceManager == null)
                return;

            var current = (VisaResource ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(current) && !current.StartsWith("USB", StringComparison.OrdinalIgnoreCase))
                return;

            string[] resources;
            try
            {
                resources = await Task.Run(() => FindVisaResourcesSafe(_resourceManager, "USB?*INSTR")).ConfigureAwait(true);
            }
            catch
            {
                return;
            }

            if (resources == null || resources.Length == 0)
                return;

            string best = null;
            for (int i = 0; i < resources.Length; i++)
            {
                var r = resources[i];
                if (string.IsNullOrWhiteSpace(r))
                    continue;

                if (r.IndexOf("0x0A69", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    r.IndexOf("0x084A", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    best = r;
                    break;
                }
            }

            if (string.IsNullOrWhiteSpace(best))
            {
                for (int i = 0; i < resources.Length; i++)
                {
                    if (!string.IsNullOrWhiteSpace(resources[i]))
                    {
                        best = resources[i];
                        break;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(best))
                return;

            if (!string.Equals(best, current, StringComparison.OrdinalIgnoreCase))
                VisaResource = best;
        }

        private static string[] FindVisaResourcesSafe(ResourceManager rm, string expression)
        {
            if (rm == null)
                return Array.Empty<string>();

            try
            {
                var t = rm.GetType();

                var m = t.GetMethod("Find", new[] { typeof(string) });
                if (m != null)
                    return ConvertFindResultToStrings(m.Invoke(rm, new object[] { expression }));

                m = t.GetMethod("FindResources", new[] { typeof(string) });
                if (m != null)
                    return ConvertFindResultToStrings(m.Invoke(rm, new object[] { expression }));

                m = t.GetMethod("FindResources", new[] { typeof(string), typeof(string[]) });
                if (m != null)
                {
                    object[] args = { expression, null };
                    m.Invoke(rm, args);
                    return ConvertFindResultToStrings(args[1]);
                }
            }
            catch
            {
            }

            return Array.Empty<string>();
        }

        private static string[] ConvertFindResultToStrings(object result)
        {
            if (result == null)
                return Array.Empty<string>();

            if (result is string[] arr)
                return arr;

            if (result is IEnumerable enumerable)
            {
                var list = new List<string>();
                foreach (var item in enumerable)
                {
                    if (item == null)
                        continue;
                    list.Add(item.ToString());
                }
                return list.ToArray();
            }

            return new[] { result.ToString() };
        }

        private async Task ApplyModeForChannelAsync(string channel, string mode)
        {
            if (_session == null)
                return;

            var m = NormalizeMode(mode);
            if (string.IsNullOrWhiteSpace(m))
                return;

            try
            {
                await WriteByChannelAsync(channel, $"MODE {m}").ConfigureAwait(true);
            }
            catch
            {
            }
        }

        public string ElectronicLoadConnectButtonText => IsElectronicLoadConnected ? "断开中" : "连接中";

        private bool _isElectronicLoadConnecting;
        public bool IsElectronicLoadConnecting
        {
            get => _isElectronicLoadConnecting;
            private set => SetProperty(ref _isElectronicLoadConnecting, value);
        }

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

        private string _ch1Mode;
        public string Ch1Mode
        {
            get => _ch1Mode;
            set
            {
                if (SetProperty(ref _ch1Mode, value))
                {
                    RaisePropertyChanged(nameof(Ch1SetUnit));
                    RaisePropertyChanged(nameof(Ch1RangeText));

                    if (!_suppressModeApply)
                    {
                        _ = ApplyModeForChannelAsync("1", _ch1Mode);
                    }
                }
            }
        }

        private string _ch2Mode;
        public string Ch2Mode
        {
            get => _ch2Mode;
            set
            {
                if (SetProperty(ref _ch2Mode, value))
                {
                    RaisePropertyChanged(nameof(Ch2SetUnit));
                    RaisePropertyChanged(nameof(Ch2RangeText));

                    if (!_suppressModeApply)
                    {
                        _ = ApplyModeForChannelAsync("2", _ch2Mode);
                    }
                }
            }
        }

        private string _ch3Mode;
        public string Ch3Mode
        {
            get => _ch3Mode;
            set
            {
                if (SetProperty(ref _ch3Mode, value))
                {
                    RaisePropertyChanged(nameof(Ch3SetUnit));
                    RaisePropertyChanged(nameof(Ch3RangeText));

                    if (!_suppressModeApply)
                    {
                        _ = ApplyModeForChannelAsync("3", _ch3Mode);
                    }
                }
            }
        }

        private string _ch4Mode;
        public string Ch4Mode
        {
            get => _ch4Mode;
            set
            {
                if (SetProperty(ref _ch4Mode, value))
                {
                    RaisePropertyChanged(nameof(Ch4SetUnit));
                    RaisePropertyChanged(nameof(Ch4RangeText));

                    if (!_suppressModeApply)
                    {
                        _ = ApplyModeForChannelAsync("4", _ch4Mode);
                    }
                }
            }
        }

        private string _ch1SetValue;
        public string Ch1SetValue { get => _ch1SetValue; set => SetProperty(ref _ch1SetValue, value); }

        private string _ch2SetValue;
        public string Ch2SetValue { get => _ch2SetValue; set => SetProperty(ref _ch2SetValue, value); }

        private string _ch3SetValue;
        public string Ch3SetValue { get => _ch3SetValue; set => SetProperty(ref _ch3SetValue, value); }

        private string _ch4SetValue;
        public string Ch4SetValue { get => _ch4SetValue; set => SetProperty(ref _ch4SetValue, value); }

        public string Ch1SetUnit => GetUnitForMode(Ch1Mode);
        public string Ch2SetUnit => GetUnitForMode(Ch2Mode);
        public string Ch3SetUnit => GetUnitForMode(Ch3Mode);
        public string Ch4SetUnit => GetUnitForMode(Ch4Mode);

        public string Ch1RangeText => GetRangeHintForMode(Ch1Mode);
        public string Ch2RangeText => GetRangeHintForMode(Ch2Mode);
        public string Ch3RangeText => GetRangeHintForMode(Ch3Mode);
        public string Ch4RangeText => GetRangeHintForMode(Ch4Mode);

        private string _ch1MeasVoltage;
        public string Ch1MeasVoltage { get => _ch1MeasVoltage; set => SetProperty(ref _ch1MeasVoltage, value); }
        private string _ch1MeasCurrent;
        public string Ch1MeasCurrent { get => _ch1MeasCurrent; set => SetProperty(ref _ch1MeasCurrent, value); }
        private string _ch1MeasPower;
        public string Ch1MeasPower { get => _ch1MeasPower; set => SetProperty(ref _ch1MeasPower, value); }

        private string _ch2MeasVoltage;
        public string Ch2MeasVoltage { get => _ch2MeasVoltage; set => SetProperty(ref _ch2MeasVoltage, value); }
        private string _ch2MeasCurrent;
        public string Ch2MeasCurrent { get => _ch2MeasCurrent; set => SetProperty(ref _ch2MeasCurrent, value); }
        private string _ch2MeasPower;
        public string Ch2MeasPower { get => _ch2MeasPower; set => SetProperty(ref _ch2MeasPower, value); }

        private string _ch3MeasVoltage;
        public string Ch3MeasVoltage { get => _ch3MeasVoltage; set => SetProperty(ref _ch3MeasVoltage, value); }
        private string _ch3MeasCurrent;
        public string Ch3MeasCurrent { get => _ch3MeasCurrent; set => SetProperty(ref _ch3MeasCurrent, value); }
        private string _ch3MeasPower;
        public string Ch3MeasPower { get => _ch3MeasPower; set => SetProperty(ref _ch3MeasPower, value); }

        private string _ch4MeasVoltage;
        public string Ch4MeasVoltage { get => _ch4MeasVoltage; set => SetProperty(ref _ch4MeasVoltage, value); }
        private string _ch4MeasCurrent;
        public string Ch4MeasCurrent { get => _ch4MeasCurrent; set => SetProperty(ref _ch4MeasCurrent, value); }
        private string _ch4MeasPower;
        public string Ch4MeasPower { get => _ch4MeasPower; set => SetProperty(ref _ch4MeasPower, value); }

        private bool _ch1LoadOn;
        public bool Ch1LoadOn { get => _ch1LoadOn; set { if (SetProperty(ref _ch1LoadOn, value)) RaisePropertyChanged(nameof(Ch1LoadText)); } }
        private bool _ch2LoadOn;
        public bool Ch2LoadOn { get => _ch2LoadOn; set { if (SetProperty(ref _ch2LoadOn, value)) RaisePropertyChanged(nameof(Ch2LoadText)); } }
        private bool _ch3LoadOn;
        public bool Ch3LoadOn { get => _ch3LoadOn; set { if (SetProperty(ref _ch3LoadOn, value)) RaisePropertyChanged(nameof(Ch3LoadText)); } }
        private bool _ch4LoadOn;
        public bool Ch4LoadOn { get => _ch4LoadOn; set { if (SetProperty(ref _ch4LoadOn, value)) RaisePropertyChanged(nameof(Ch4LoadText)); } }

        private bool _ch1ShortOn;
        public bool Ch1ShortOn { get => _ch1ShortOn; set { if (SetProperty(ref _ch1ShortOn, value)) RaisePropertyChanged(nameof(Ch1ShortText)); } }
        private bool _ch2ShortOn;
        public bool Ch2ShortOn { get => _ch2ShortOn; set { if (SetProperty(ref _ch2ShortOn, value)) RaisePropertyChanged(nameof(Ch2ShortText)); } }
        private bool _ch3ShortOn;
        public bool Ch3ShortOn { get => _ch3ShortOn; set { if (SetProperty(ref _ch3ShortOn, value)) RaisePropertyChanged(nameof(Ch3ShortText)); } }
        private bool _ch4ShortOn;
        public bool Ch4ShortOn { get => _ch4ShortOn; set { if (SetProperty(ref _ch4ShortOn, value)) RaisePropertyChanged(nameof(Ch4ShortText)); } }

        public string Ch1LoadText => Ch1LoadOn ? "ON" : "OFF";
        public string Ch2LoadText => Ch2LoadOn ? "ON" : "OFF";
        public string Ch3LoadText => Ch3LoadOn ? "ON" : "OFF";
        public string Ch4LoadText => Ch4LoadOn ? "ON" : "OFF";

        public string Ch1ShortText => Ch1ShortOn ? "ON" : "OFF";
        public string Ch2ShortText => Ch2ShortOn ? "ON" : "OFF";
        public string Ch3ShortText => Ch3ShortOn ? "ON" : "OFF";
        public string Ch4ShortText => Ch4ShortOn ? "ON" : "OFF";

        public DelegateCommand ToggleDeviceCommand { get; private set; }
        public DelegateCommand<string> ToggleLoadCommand { get; private set; }
        public DelegateCommand<string> ToggleShortCommand { get; private set; }
        public DelegateCommand<string> ApplySetValueCommand { get; private set; }

        public ElectronicLoadTestPanelViewModel(string testTaskName, string configTableName, string chassisName,
            ElectronicLoadDevice device,
            IPxiChassisService pxiChassisService)
        {
            _pxiChassisService = pxiChassisService;
            _device = device;

            // 注意：如果系统未安装 NI-VISA，会在 ResourceManager() 处抛出 DllNotFoundException（nivisa64.dll）。
            // 为了不让面板“打开即崩溃”，这里不在构造函数初始化 VISA，改为在连接时延迟初始化。
            _resourceManager = null;

            ModeOptions = new ObservableCollection<string>
            {
                "CCL", "CCH", "CRL", "CRH", "CV", "CPL", "CPH"
            };

            VisaResource = GuessVisaResource(device);
            ConnectionStatus = "离线";

            _suppressModeApply = true;
            try
            {
                Ch1Mode = "CCL";
                Ch2Mode = "CCL";
                Ch3Mode = "CCL";
                Ch4Mode = "CCL";
            }
            finally
            {
                _suppressModeApply = false;
            }

            Ch1SetValue = "0";
            Ch2SetValue = "0";
            Ch3SetValue = "0";
            Ch4SetValue = "0";

            Ch1MeasVoltage = "0";
            Ch1MeasCurrent = "0";
            Ch1MeasPower = "0";

            Ch2MeasVoltage = "0";
            Ch2MeasCurrent = "0";
            Ch2MeasPower = "0";

            Ch3MeasVoltage = "0";
            Ch3MeasCurrent = "0";
            Ch3MeasPower = "0";

            Ch4MeasVoltage = "0";
            Ch4MeasCurrent = "0";
            Ch4MeasPower = "0";

            InitializeCommands();
        }

        private void InitializeCommands()
        {
            ToggleDeviceCommand = new DelegateCommand(async () => await ToggleDeviceAsync());
            ToggleLoadCommand = new DelegateCommand<string>(async ch => await ToggleLoadAsync(ch));
            ToggleShortCommand = new DelegateCommand<string>(async ch => await ToggleShortAsync(ch));
            ApplySetValueCommand = new DelegateCommand<string>(async ch => await ApplySetValueAsync(ch));
        }

        private static string GuessVisaResource(ElectronicLoadDevice device)
        {
            var fallback = "USB0::0x0A69::0x084A::6314A0011536::INSTR";
            if (device == null)
                return fallback;

            var conn = device.GetConnectionString();
            if (!string.IsNullOrWhiteSpace(conn) && conn.StartsWith("USB", StringComparison.OrdinalIgnoreCase))
                return fallback;

            return fallback;
        }

        private async Task ToggleDeviceAsync()
        {
            if (IsElectronicLoadConnected)
                await DisconnectAsync();
            else
                await ConnectAsync();
        }

        private async Task ConnectAsync()
        {
            if (IsElectronicLoadConnecting)
                return;

            if (string.IsNullOrWhiteSpace(VisaResource))
            {
                ReMessageBox.Show("请输入VISA资源字符串", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                IsElectronicLoadConnecting = true;
                ConnectionStatus = "连接中";
                IsElectronicLoadConnected = false;

                try
                {
                    if (_resourceManager == null)
                    {
                        _resourceManager = new ResourceManager();
                    }
                }
                catch (DllNotFoundException)
                {
                    ConnectionStatus = "离线";
                    IsElectronicLoadConnected = false;
                    ReMessageBox.Show(
                        "未找到 NI-VISA 运行环境（缺少 nivisa64.dll）。\n\n" +
                        "请安装 NI-VISA（建议 64位），并确保当前程序以 x64 运行。",
                        "VISA 未安装",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }
                catch (Ivi.Visa.VisaException ex)
                {
                    ConnectionStatus = "离线";
                    IsElectronicLoadConnected = false;
                    ReMessageBox.Show(
                        $"VISA 初始化失败：{ex.Message}\n\n请确认已安装 NI-VISA 并重启电脑/应用。",
                        "VISA 错误",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }

                await TryAutoFillUsbVisaResourceAsync().ConfigureAwait(true);

                await Task.Run(() =>
                {
                    _session = (MessageBasedSession)_resourceManager.Open(VisaResource.Trim());
                    _session.TimeoutMilliseconds = 5000;
                });

                try
                {
                    // 更稳定的读回：启用终止符，避免 ReadString 因未遇到结束符而等待超时。
                    _session.TerminationCharacterEnabled = true;
                    _session.TerminationCharacter = (byte)'\n';
                }
                catch
                {
                }

                // 先用标准 *IDN? 触发一次远程通信并确认链路；不支持时再回退通道级 ID。
                DeviceIdn = await QueryAsync("*IDN?").ConfigureAwait(true);
                if (string.IsNullOrWhiteSpace(DeviceIdn))
                {
                    DeviceIdn = await QueryByChannelAsync("1", "CHAN:ID?").ConfigureAwait(true);
                }

                await TrySetRemoteControlAsync(true).ConfigureAwait(true);

                // 同步 UI 当前模式到设备，确保前面板指示灯与选择一致
                _suppressModeApply = true;
                try
                {
                    await ApplyModeForChannelAsync("1", Ch1Mode).ConfigureAwait(true);
                    await ApplyModeForChannelAsync("2", Ch2Mode).ConfigureAwait(true);
                    await ApplyModeForChannelAsync("3", Ch3Mode).ConfigureAwait(true);
                    await ApplyModeForChannelAsync("4", Ch4Mode).ConfigureAwait(true);
                }
                finally
                {
                    _suppressModeApply = false;
                }

                IsElectronicLoadConnected = true;
                ConnectionStatus = "在线";

                StartPolling();
            }
            catch (Exception ex)
            {
                ConnectionStatus = "离线";
                IsElectronicLoadConnected = false;
                SafeCloseSession();
                ReMessageBox.Show($"连接失败: {ex.Message}", "连接错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsElectronicLoadConnecting = false;
            }
        }

        private async Task DisconnectAsync()
        {
            try
            {
                ConnectionStatus = "断开中";

                StopPolling();

                await TrySetRemoteControlAsync(false).ConfigureAwait(true);

                IsElectronicLoadConnected = false;
                ConnectionStatus = "离线";
            }
            catch
            {
            }
            finally
            {
                SafeCloseSession();
            }

            await Task.CompletedTask;
        }

        private void StartPolling()
        {
            if (_pollTask != null && !_pollTask.IsCompleted)
                return;

            _pollCts?.Cancel();
            _pollCts?.Dispose();
            _pollCts = new CancellationTokenSource();
            var token = _pollCts.Token;

            _pollTask = Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        if (!IsElectronicLoadConnected || _session == null)
                        {
                            await Task.Delay(200, token);
                            continue;
                        }

                        await RefreshMeasurementsOnceAsync(token).ConfigureAwait(false);
                        await Task.Delay(500, token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch
                    {
                        await Task.Delay(500, token);
                    }
                }
            }, token);
        }

        private void StopPolling()
        {
            try { _pollCts?.Cancel(); } catch { }
            try { _pollCts?.Dispose(); } catch { }
            _pollCts = null;
            _pollTask = null;
        }

        private async Task RefreshMeasurementsOnceAsync(CancellationToken token)
        {
            string v1 = null, i1 = null, p1 = null;
            string v2 = null, i2 = null, p2 = null;
            string v3 = null, i3 = null, p3 = null;
            string v4 = null, i4 = null, p4 = null;

            // 优先使用 ALL* 系列命令一次性读取所有通道，避免轮询时频繁 CHAN 切换导致前面板通道灯跳动。
            try
            {
                var allVRaw = await QueryAsync("MEAS:ALLV?", token).ConfigureAwait(false);
                var allCRaw = await QueryAsync("MEAS:ALLC?", token).ConfigureAwait(false);
                var allPRaw = await QueryAsync("MEAS:ALLP?", token).ConfigureAwait(false);

                var allV = ParseAard(allVRaw);
                var allC = ParseAard(allCRaw);
                var allP = ParseAard(allPRaw);

                if (allV == null || allC == null || allP == null ||
                    allV.Length < 8 || allC.Length < 8 || allP.Length < 8)
                {
                    throw new InvalidOperationException("MEAS:ALL* 返回格式不符合预期");
                }

                v1 = GetAardValue(allV, MapUiChannelToPhysical("1"));
                i1 = GetAardValue(allC, MapUiChannelToPhysical("1"));
                p1 = GetAardValue(allP, MapUiChannelToPhysical("1"));

                v2 = GetAardValue(allV, MapUiChannelToPhysical("2"));
                i2 = GetAardValue(allC, MapUiChannelToPhysical("2"));
                p2 = GetAardValue(allP, MapUiChannelToPhysical("2"));

                v3 = GetAardValue(allV, MapUiChannelToPhysical("3"));
                i3 = GetAardValue(allC, MapUiChannelToPhysical("3"));
                p3 = GetAardValue(allP, MapUiChannelToPhysical("3"));

                v4 = GetAardValue(allV, MapUiChannelToPhysical("4"));
                i4 = GetAardValue(allC, MapUiChannelToPhysical("4"));
                p4 = GetAardValue(allP, MapUiChannelToPhysical("4"));
            }
            catch
            {
                // 回退：逐通道查询（会切换 CHAN，因此会有前面板通道灯跳动）
                v1 = await QueryByChannelAsync("1", "MEAS:VOLT?", token).ConfigureAwait(false);
                i1 = await QueryByChannelAsync("1", "MEAS:CURR?", token).ConfigureAwait(false);
                p1 = await QueryByChannelAsync("1", "MEAS:POW?", token).ConfigureAwait(false);

                v2 = await QueryByChannelAsync("2", "MEAS:VOLT?", token).ConfigureAwait(false);
                i2 = await QueryByChannelAsync("2", "MEAS:CURR?", token).ConfigureAwait(false);
                p2 = await QueryByChannelAsync("2", "MEAS:POW?", token).ConfigureAwait(false);

                v3 = await QueryByChannelAsync("3", "MEAS:VOLT?", token).ConfigureAwait(false);
                i3 = await QueryByChannelAsync("3", "MEAS:CURR?", token).ConfigureAwait(false);
                p3 = await QueryByChannelAsync("3", "MEAS:POW?", token).ConfigureAwait(false);

                v4 = await QueryByChannelAsync("4", "MEAS:VOLT?", token).ConfigureAwait(false);
                i4 = await QueryByChannelAsync("4", "MEAS:CURR?", token).ConfigureAwait(false);
                p4 = await QueryByChannelAsync("4", "MEAS:POW?", token).ConfigureAwait(false);
            }

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                Ch1MeasVoltage = SafeNumber(v1);
                Ch1MeasCurrent = SafeNumber(i1);
                Ch1MeasPower = SafeNumber(p1);

                Ch2MeasVoltage = SafeNumber(v2);
                Ch2MeasCurrent = SafeNumber(i2);
                Ch2MeasPower = SafeNumber(p2);

                Ch3MeasVoltage = SafeNumber(v3);
                Ch3MeasCurrent = SafeNumber(i3);
                Ch3MeasPower = SafeNumber(p3);

                Ch4MeasVoltage = SafeNumber(v4);
                Ch4MeasCurrent = SafeNumber(i4);
                Ch4MeasPower = SafeNumber(p4);
            });
        }

        private async Task ToggleLoadAsync(string channel)
        {
            if (!IsElectronicLoadConnected)
            {
                ReMessageBox.Show("电子负载未连接", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            channel = NormalizeChannel(channel);
            bool next;

            if (channel == "1") next = !Ch1LoadOn;
            else if (channel == "2") next = !Ch2LoadOn;
            else if (channel == "3") next = !Ch3LoadOn;
            else next = !Ch4LoadOn;

            if (!next)
            {
                var shortOn = channel == "1" ? Ch1ShortOn : channel == "2" ? Ch2ShortOn : channel == "3" ? Ch3ShortOn : Ch4ShortOn;
                if (shortOn)
                {
                    await WriteByChannelAsync(channel, "LOAD:SHOR OFF").ConfigureAwait(true);

                    if (channel == "1") Ch1ShortOn = false;
                    else if (channel == "2") Ch2ShortOn = false;
                    else if (channel == "3") Ch3ShortOn = false;
                    else Ch4ShortOn = false;
                }
            }

            await WriteByChannelAsync(channel, next ? "LOAD ON" : "LOAD OFF").ConfigureAwait(true);

            if (channel == "1") Ch1LoadOn = next;
            else if (channel == "2") Ch2LoadOn = next;
            else if (channel == "3") Ch3LoadOn = next;
            else Ch4LoadOn = next;
        }

        private async Task ToggleShortAsync(string channel)
        {
            if (!IsElectronicLoadConnected)
            {
                ReMessageBox.Show("电子负载未连接", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            channel = NormalizeChannel(channel);

            var loadOn = channel == "1" ? Ch1LoadOn : channel == "2" ? Ch2LoadOn : channel == "3" ? Ch3LoadOn : Ch4LoadOn;
            if (!loadOn)
            {
                ReMessageBox.Show("请先开启 Load，再允许 Short", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            bool next;

            if (channel == "1") next = !Ch1ShortOn;
            else if (channel == "2") next = !Ch2ShortOn;
            else if (channel == "3") next = !Ch3ShortOn;
            else next = !Ch4ShortOn;

            await WriteByChannelAsync(channel, next ? "LOAD:SHOR ON" : "LOAD:SHOR OFF").ConfigureAwait(true);

            if (channel == "1") Ch1ShortOn = next;
            else if (channel == "2") Ch2ShortOn = next;
            else if (channel == "3") Ch3ShortOn = next;
            else Ch4ShortOn = next;
        }

        private async Task ApplySetValueAsync(string channel)
        {
            if (!IsElectronicLoadConnected)
            {
                ReMessageBox.Show("电子负载未连接", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            await ApplySetValueForChannelAsync(channel).ConfigureAwait(true);
        }

        private async Task ApplySetValueForChannelAsync(string channel)
        {
            if (!IsElectronicLoadConnected || _session == null)
                return;

            channel = NormalizeChannel(channel);

            string mode;
            string valueRaw;

            if (channel == "1") { mode = Ch1Mode; valueRaw = Ch1SetValue; }
            else if (channel == "2") { mode = Ch2Mode; valueRaw = Ch2SetValue; }
            else if (channel == "3") { mode = Ch3Mode; valueRaw = Ch3SetValue; }
            else { mode = Ch4Mode; valueRaw = Ch4SetValue; }

            if (!TryParseSetValue(valueRaw, out var value))
                return;

            var m = (mode ?? "").Trim().ToUpperInvariant();
            string cmd;
            if (m.StartsWith("CC", StringComparison.Ordinal))
                cmd = $"CURR:STAT:L1 {value.ToString(CultureInfo.InvariantCulture)}";
            else if (m.StartsWith("CR", StringComparison.Ordinal))
                cmd = $"RES:L1 {value.ToString(CultureInfo.InvariantCulture)}";
            else if (m == "CV")
                cmd = $"VOLT:L1 {value.ToString(CultureInfo.InvariantCulture)}";
            else if (m.StartsWith("CP", StringComparison.Ordinal))
                cmd = $"POW:STAT:L1 {value.ToString(CultureInfo.InvariantCulture)}";
            else
                return;

            try
            {
                await WriteByChannelAsync(channel, cmd).ConfigureAwait(true);
            }
            catch
            {
            }
        }

        private static bool TryParseSetValue(string raw, out double value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(raw))
                return false;

            var s = raw.Trim();
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                return true;
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out value))
                return true;

            return false;
        }

        private async Task TrySetRemoteControlAsync(bool enabled)
        {
            if (_session == null)
                return;

            var res = (VisaResource ?? "").Trim();
            if (!res.StartsWith("ASRL", StringComparison.OrdinalIgnoreCase))
                return;

            try
            {
                await QueryAsync(enabled ? "CONF:REM ON" : "CONF:REM OFF").ConfigureAwait(true);
            }
            catch
            {
            }
        }

        private static string NormalizeChannel(string channel)
        {
            var c = (channel ?? "").Trim();
            return string.IsNullOrWhiteSpace(c) ? "1" : c;
        }

        private static string MapUiChannelToPhysical(string uiChannel)
        {
            switch (NormalizeChannel(uiChannel))
            {
                case "1":
                    return "1";
                case "2":
                    return "3";
                case "3":
                    return "5";
                case "4":
                    return "7";
                default:
                    return NormalizeChannel(uiChannel);
            }
        }

        private async Task WriteByChannelAsync(string channel, string command)
        {
            if (_session == null)
                return;

            channel = MapUiChannelToPhysical(channel);

            await _ioLock.WaitAsync().ConfigureAwait(false);
            try
            {
                _session.RawIO.Write($"CHAN {channel}\n");
                _session.RawIO.Write(command.EndsWith("\n", StringComparison.Ordinal) ? command : command + "\n");
            }
            finally
            {
                _ioLock.Release();
            }
        }

        private async Task<string> QueryByChannelAsync(string channel, string query, CancellationToken token)
        {
            if (_session == null)
                return null;

            channel = MapUiChannelToPhysical(channel);

            await _ioLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                _session.RawIO.Write($"CHAN {channel}\n");
                _session.RawIO.Write(query.EndsWith("\n", StringComparison.Ordinal) ? query : query + "\n");
                return _session.RawIO.ReadString()?.Trim();
            }
            finally
            {
                _ioLock.Release();
            }
        }

        private Task<string> QueryByChannelAsync(string channel, string query)
        {
            return QueryByChannelAsync(channel, query, CancellationToken.None);
        }

        private async Task<string> QueryAsync(string query, CancellationToken token)
        {
            if (_session == null)
                return null;

            await _ioLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                _session.RawIO.Write(query.EndsWith("\n", StringComparison.Ordinal) ? query : query + "\n");
                return _session.RawIO.ReadString()?.Trim();
            }
            finally
            {
                _ioLock.Release();
            }
        }

        private async Task<string> QueryAsync(string query)
        {
            if (_session == null)
                return null;

            await _ioLock.WaitAsync().ConfigureAwait(false);
            try
            {
                _session.RawIO.Write(query.EndsWith("\n", StringComparison.Ordinal) ? query : query + "\n");
                return _session.RawIO.ReadString()?.Trim();
            }
            finally
            {
                _ioLock.Release();
            }
        }

        private static string[] ParseAard(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            // 手册示例返回格式："1.2, 2, 0, 0, 10.2, 0, 0, 0"
            var parts = raw.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
                parts[i] = parts[i].Trim();
            return parts;
        }

        private static string GetAardValue(string[] aard, string oneBasedChannel)
        {
            if (aard == null)
                return null;

            if (!int.TryParse((oneBasedChannel ?? "").Trim(), out var ch) || ch <= 0)
                return null;

            var idx = ch - 1;
            if (idx < 0 || idx >= aard.Length)
                return null;

            return aard[idx];
        }

        private static string SafeNumber(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return "0";

            var cleaned = raw.Trim();
            if (double.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                return cleaned;

            if (double.TryParse(cleaned, NumberStyles.Float, CultureInfo.CurrentCulture, out _))
                return cleaned;

            return cleaned;
        }

        private static string GetUnitForMode(string mode)
        {
            var m = (mode ?? "").Trim().ToUpperInvariant();
            if (m.StartsWith("CC")) return "A";
            if (m.StartsWith("CR")) return "Ohm";
            if (m == "CV") return "V";
            if (m.StartsWith("CP")) return "W";
            return "";
        }

        private static string GetRangeHintForMode(string mode)
        {
            var m = (mode ?? "").Trim().ToUpperInvariant();
            var unit = GetUnitForMode(m);
            switch (m)
            {
                case "CCL":
                    return $"0-4 {unit}".Trim();
                case "CCH":
                    return $"0-40 {unit}".Trim();
                case "CRL":
                    return $"0.0375-150 {unit}".Trim();
                case "CRH":
                    return $"1.875-7500 {unit}".Trim();
                case "CV":
                    return $"0-80 {unit}".Trim();
                case "CPL":
                    return $"0-20 {unit}".Trim();
                case "CPH":
                    return $"0-200 {unit}".Trim();
                default:
                    return "";
            }
        }

        private void SafeCloseSession()
        {
            try { _session?.Dispose(); } catch { }
            _session = null;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                try { StopPolling(); } catch { }
                try { SafeCloseSession(); } catch { }
                try { _resourceManager?.Dispose(); } catch { }
            }

            _disposed = true;
        }

        public bool CanClose()
        {
            if (IsElectronicLoadConnecting)
            {
                ReMessageBox.Show($"正在打开电子负载，请稍候连接完成后再切换页面", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }

            return true;
        }
    }
}
