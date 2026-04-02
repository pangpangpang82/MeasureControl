using Prism.Commands;
using Prism.Mvvm;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using MeasureControl.Helpers;
using MeasureControl.Services;
using MeasureControl.Services.HardwareApis;

namespace MeasureControl.ViewModels.SingleBoardTest.AirController
{
    public sealed class A_C_7_3_2_2ViewModel : BindableBase, IDisposable
    {
        private const string DefaultFpgaIpAddress = "192.168.1.10";
        private const int DefaultFpgaPort = 5001;

        private const string DefaultMatrixIpAddress = "192.168.1.3";
        private const int DefaultMatrixTcpBasePort = 50200;

        private const int Chassis2Slot6 = 6;
        private const int Chassis2Slot4 = 4;

        private const string Slot6Row = "I1";
        private const string Slot6ToScope = "O9";

        private const string Slot4Row = "I4";
        private const string Slot4ToScope = "O2";

        private const int DefaultScopePort = 5555;
        private const string DefaultScopeIpAddress = "192.168.1.18";

        private const string PowerSupply28VIpAddress = "192.168.1.15";
        private const string PowerSupply3V3IpAddress = "192.168.1.16";

        private const PowerSupplyChannel PowerSupply28VCh1 = PowerSupplyChannel.CH1;
        private const PowerSupplyChannel PowerSupply28VCh2 = PowerSupplyChannel.CH2;
        private const double Power28VVoltage = 28.0;
        private const double Power28VCurrentLimit = 3.0;

        private const PowerSupplyChannel PowerSupply3V3Channel = PowerSupplyChannel.CH3;
        private const double Power3V3Voltage = 3.3;
        private const double Power3V3CurrentLimit = 1.0;

        private static readonly byte[] DeviceInitCommandFrame = { 0xAA, 0x55, 0x02, 0x02, 0x01 };
        private static readonly byte[] ResetToInitialCommandFrame = { 0xAA, 0x55, 0x0A, 0x03, 0x00, 0xE8, 0x03, 0x00, 0x00, 0xE8, 0x03, 0x00, 0x00 };

        private const int FixedGearFrequencyHz = 20;

        private readonly SemaphoreSlim _manualTestLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _autoTestLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _opLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _instrumentLock = new SemaphoreSlim(1, 1);

        private CancellationTokenSource _autoTestCts;

        private FpgaTcpClient _fpga;

        private TcpClient _scopeTcpClient;
        private NetworkStream _scopeTcpStream;

        private IPowerSupplyApi _powerSupply28V;
        private IPowerSupplyApi _powerSupply3V3;

        private bool _isPowerOn;
        private string _powerStatus = "未上电";

        private bool _isTestPowerOn;

        private bool _isMatrixRouted;
        private bool _matrixRoutedSlot6;
        private bool _matrixRoutedSlot4;

        private bool _isManualTestRunning;
        private bool _isAutoTestRunning;
        private bool _isBusy;

        private int _pwmFrequencyHz;
        private int _pwmDutyPct;

        private string _lastTestTime = "--";
        private string _lastTestResult = "--";

        private string _pwmCustomResult = "--";
        private string _pwm100Result = "--";
        private string _pwm50Result = "--";
        private string _pwm0Result = "--";

        private bool _isMeasuringPwmCustom;
        private bool _isMeasuringPwm100;
        private bool _isMeasuringPwm50;
        private bool _isMeasuringPwm0;

        private string _fpgaIpAddress = DefaultFpgaIpAddress;
        private int _fpgaPort = DefaultFpgaPort;

        private string _scopeIpAddress = DefaultScopeIpAddress;
        private int _scopePort = DefaultScopePort;

        private string _matrixIpAddress = DefaultMatrixIpAddress;
        private int _matrixTcpBasePort = DefaultMatrixTcpBasePort;

        public A_C_7_3_2_2ViewModel()
        {
            PwmFrequencyHz = 2000;
            PwmDutyPct = 50;

            PwmFrequencyOptions.Add(10);
            PwmFrequencyOptions.Add(20);
            PwmFrequencyOptions.Add(50);
            PwmFrequencyOptions.Add(100);
            PwmFrequencyOptions.Add(200);
            PwmFrequencyOptions.Add(500);
            PwmFrequencyOptions.Add(1000);
            PwmFrequencyOptions.Add(2000);
            PwmFrequencyOptions.Add(5000);

            for (int i = 0; i <= 100; i += 5)
                PwmDutyOptions.Add(i);

            TogglePowerCommand = new DelegateCommand(async () => await TogglePowerAsync(), () => !IsBusy && !IsManualTestRunning && !IsAutoTestRunning);

            ManualTestCommand = new DelegateCommand(OnManualTest, () => !IsBusy && !IsAutoTestRunning);
            AutoTestCommand = new DelegateCommand(OnAutoTest, () => !IsBusy && !IsManualTestRunning);
            ClearLogCommand = new DelegateCommand(() => Logs.Clear());

            SendPwmCustomCommand = new DelegateCommand(async () => await SendCustomAsync(), () => IsConfigSendMeasureEnabled);
            MeasurePwmCustomCommand = new DelegateCommand(async () => await MeasureCustomAsync(), () => IsConfigSendMeasureEnabled);

            SendPwm100Command = new DelegateCommand(async () => await SendFixedGearAsync(100), () => !IsBusy && IsManualTestRunning);
            MeasurePwm100Command = new DelegateCommand(async () => await MeasureFixedGearAsync(100), () => !IsBusy && IsManualTestRunning);

            SendPwm50Command = new DelegateCommand(async () => await SendFixedGearAsync(50), () => !IsBusy && IsManualTestRunning);
            MeasurePwm50Command = new DelegateCommand(async () => await MeasureFixedGearAsync(50), () => !IsBusy && IsManualTestRunning);

            SendPwm0Command = new DelegateCommand(async () => await SendFixedGearAsync(0), () => !IsBusy && IsManualTestRunning);
            MeasurePwm0Command = new DelegateCommand(async () => await MeasureFixedGearAsync(0), () => !IsBusy && IsManualTestRunning);
        }

        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();

        public ObservableCollection<int> PwmFrequencyOptions { get; } = new ObservableCollection<int>();
        public ObservableCollection<int> PwmDutyOptions { get; } = new ObservableCollection<int>();

        public DelegateCommand TogglePowerCommand { get; }

        public DelegateCommand ManualTestCommand { get; }
        public DelegateCommand AutoTestCommand { get; }
        public DelegateCommand ClearLogCommand { get; }

        public DelegateCommand SendPwmCustomCommand { get; }
        public DelegateCommand MeasurePwmCustomCommand { get; }

        public DelegateCommand SendPwm100Command { get; }
        public DelegateCommand MeasurePwm100Command { get; }

        public DelegateCommand SendPwm50Command { get; }
        public DelegateCommand MeasurePwm50Command { get; }

        public DelegateCommand SendPwm0Command { get; }
        public DelegateCommand MeasurePwm0Command { get; }

        public string FpgaIpAddress
        {
            get => _fpgaIpAddress;
            set => SetProperty(ref _fpgaIpAddress, value);
        }

        public int FpgaPort
        {
            get => _fpgaPort;
            set => SetProperty(ref _fpgaPort, value);
        }

        public string ScopeIpAddress
        {
            get => _scopeIpAddress;
            set => SetProperty(ref _scopeIpAddress, value);
        }

        public int ScopePort
        {
            get => _scopePort;
            set => SetProperty(ref _scopePort, value);
        }

        public string MatrixIpAddress
        {
            get => _matrixIpAddress;
            set => SetProperty(ref _matrixIpAddress, value);
        }

        public int MatrixTcpBasePort
        {
            get => _matrixTcpBasePort;
            set => SetProperty(ref _matrixTcpBasePort, value);
        }

        public bool IsPowerOn
        {
            get => _isPowerOn;
            private set
            {
                if (SetProperty(ref _isPowerOn, value))
                {
                    RaisePropertyChanged(nameof(IsConfigSendMeasureEnabled));
                    RaiseAllCanExecuteChanged();
                }
            }
        }

        public string PowerStatus
        {
            get => _powerStatus;
            private set => SetProperty(ref _powerStatus, value);
        }

        public bool IsManualTestRunning
        {
            get => _isManualTestRunning;
            private set
            {
                if (SetProperty(ref _isManualTestRunning, value))
                {
                    if (value)
                        IsAutoTestRunning = false;
                    RaisePropertyChanged(nameof(IsConfigSendMeasureEnabled));
                    RaiseAllCanExecuteChanged();
                }
            }
        }

        public bool IsAutoTestRunning
        {
            get => _isAutoTestRunning;
            private set
            {
                if (SetProperty(ref _isAutoTestRunning, value))
                {
                    if (value)
                        IsManualTestRunning = false;
                    RaisePropertyChanged(nameof(IsConfigSendMeasureEnabled));
                    RaiseAllCanExecuteChanged();
                }
            }
        }

        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (SetProperty(ref _isBusy, value))
                {
                    RaisePropertyChanged(nameof(IsConfigSendMeasureEnabled));
                    RaiseAllCanExecuteChanged();
                }
            }
        }

        public bool IsConfigSendMeasureEnabled => IsPowerOn && !IsBusy && !IsManualTestRunning && !IsAutoTestRunning;

        public int PwmFrequencyHz
        {
            get => _pwmFrequencyHz;
            set
            {
                if (SetProperty(ref _pwmFrequencyHz, ClampFreq(value)))
                {
                    RaisePropertyChanged(nameof(PwmFrequencyText));
                }
            }
        }

        public int PwmDutyPct
        {
            get => _pwmDutyPct;
            set
            {
                if (SetProperty(ref _pwmDutyPct, ClampDuty(value)))
                {
                    RaisePropertyChanged(nameof(PwmDutyText));
                }
            }
        }

        public string PwmFrequencyText => $"{PwmFrequencyHz} Hz";
        public string PwmDutyText => $"{PwmDutyPct}%";

        public string PwmCustomResult
        {
            get => _pwmCustomResult;
            private set => SetProperty(ref _pwmCustomResult, value);
        }

        public string Pwm100Result
        {
            get => _pwm100Result;
            private set => SetProperty(ref _pwm100Result, value);
        }

        public string Pwm50Result
        {
            get => _pwm50Result;
            private set => SetProperty(ref _pwm50Result, value);
        }

        public string Pwm0Result
        {
            get => _pwm0Result;
            private set => SetProperty(ref _pwm0Result, value);
        }

        public bool IsMeasuringPwmCustom
        {
            get => _isMeasuringPwmCustom;
            private set => SetProperty(ref _isMeasuringPwmCustom, value);
        }

        public bool IsMeasuringPwm100
        {
            get => _isMeasuringPwm100;
            private set => SetProperty(ref _isMeasuringPwm100, value);
        }

        public bool IsMeasuringPwm50
        {
            get => _isMeasuringPwm50;
            private set => SetProperty(ref _isMeasuringPwm50, value);
        }

        public bool IsMeasuringPwm0
        {
            get => _isMeasuringPwm0;
            private set => SetProperty(ref _isMeasuringPwm0, value);
        }

        public string LastTestTime
        {
            get => _lastTestTime;
            private set => SetProperty(ref _lastTestTime, value);
        }

        public string LastTestResult
        {
            get => _lastTestResult;
            private set => SetProperty(ref _lastTestResult, value);
        }

        private void AddLog(string msg)
        {
            var line = $"[{DateTime.Now:HH:mm:ss.fff}] {msg}";
            Application.Current?.Dispatcher?.Invoke(() => Logs.Add(line));
        }

        private void RaiseAllCanExecuteChanged()
        {
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                TogglePowerCommand?.RaiseCanExecuteChanged();
                ManualTestCommand?.RaiseCanExecuteChanged();
                AutoTestCommand?.RaiseCanExecuteChanged();
                SendPwmCustomCommand?.RaiseCanExecuteChanged();
                MeasurePwmCustomCommand?.RaiseCanExecuteChanged();

                SendPwm100Command?.RaiseCanExecuteChanged();
                MeasurePwm100Command?.RaiseCanExecuteChanged();
                SendPwm50Command?.RaiseCanExecuteChanged();
                MeasurePwm50Command?.RaiseCanExecuteChanged();
                SendPwm0Command?.RaiseCanExecuteChanged();
                MeasurePwm0Command?.RaiseCanExecuteChanged();
            });
        }

        private async Task TogglePowerAsync()
        {
            if (IsBusy)
                return;

            IsBusy = true;
            try
            {
                if (IsPowerOn)
                {
                    await DisconnectFpgaAsync(CancellationToken.None).ConfigureAwait(false);
                    await PowerOffAsync(CancellationToken.None).ConfigureAwait(false);
                    return;
                }

                await PowerOnAsync(CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task PowerOnAsync(CancellationToken token)
        {
            await PowerOnHardwareAsync(token).ConfigureAwait(false);
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                IsPowerOn = true;
                PowerStatus = "已上电";
            });
        }

        private async Task PowerOffAsync(CancellationToken token)
        {
            await PowerOffHardwareAsync(token).ConfigureAwait(false);
            _isTestPowerOn = false;
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                IsPowerOn = false;
                PowerStatus = "未上电";
            });
        }

        private async Task PowerOnHardwareAsync(CancellationToken token)
        {
            AddLog("组件供电：上电中...");

            await EnsurePowerSupply28VConnectedAsync(token).ConfigureAwait(false);
            try
            {
                await _powerSupply28V.ApplyAsync(PowerSupply28VCh1, Power28VVoltage, Power28VCurrentLimit, token).ConfigureAwait(false);
                await _powerSupply28V.ApplyAsync(PowerSupply28VCh2, Power28VVoltage, Power28VCurrentLimit, token).ConfigureAwait(false);
                await _powerSupply28V.SetOutputEnabledAsync(PowerSupply28VCh1, true, token).ConfigureAwait(false);
                await _powerSupply28V.SetOutputEnabledAsync(PowerSupply28VCh2, true, token).ConfigureAwait(false);
                await Task.Delay(300, token).ConfigureAwait(false);
                AddLog($"组件供电：28V 上电(程控电源1) CH1+CH2, IP={PowerSupply28VIpAddress}");
            }
            catch (Exception ex)
            {
                AddLog($"组件供电：28V 上电失败(程控电源1): {ex.Message}");
            }

            await EnsurePowerSupply3V3ConnectedAsync(token).ConfigureAwait(false);
            try
            {
                await _powerSupply3V3.ApplyAsync(PowerSupply3V3Channel, Power3V3Voltage, Power3V3CurrentLimit, token).ConfigureAwait(false);
                await _powerSupply3V3.SetOutputEnabledAsync(PowerSupply3V3Channel, true, token).ConfigureAwait(false);
                await Task.Delay(200, token).ConfigureAwait(false);
                AddLog($"组件供电：3.3V 上电(程控电源2) CH3, IP={PowerSupply3V3IpAddress}");
            }
            catch (Exception ex)
            {
                AddLog($"组件供电：3.3V 上电失败(程控电源2): {ex.Message}");
            }
        }

        private async Task PowerOffHardwareAsync(CancellationToken token)
        {
            AddLog("组件供电：下电中...");

            try
            {
                await UnrouteMatrixAsync(token).ConfigureAwait(false);
            }
            catch { }

            try
            {
                if (_powerSupply3V3 != null)
                    try { await _powerSupply3V3.SetOutputEnabledAsync(PowerSupply3V3Channel, false, token).ConfigureAwait(false); } catch { }
            }
            catch { }

            try
            {
                if (_powerSupply28V != null)
                {
                    try { await _powerSupply28V.SetOutputEnabledAsync(PowerSupply28VCh1, false, token).ConfigureAwait(false); } catch { }
                    try { await _powerSupply28V.SetOutputEnabledAsync(PowerSupply28VCh2, false, token).ConfigureAwait(false); } catch { }
                }
            }
            catch { }
        }

        private async Task EnsurePowerSupply28VConnectedAsync(CancellationToken token)
        {
            if (_powerSupply28V != null && _powerSupply28V.IsConnected)
                return;

            _powerSupply28V ??= new PowerSupplySocketApi();
            await _powerSupply28V.ConnectAsync(PowerSupply28VIpAddress, token).ConfigureAwait(false);
        }

        private async Task EnsurePowerSupply3V3ConnectedAsync(CancellationToken token)
        {
            if (_powerSupply3V3 != null && _powerSupply3V3.IsConnected)
                return;

            _powerSupply3V3 ??= new PowerSupplySocketApi();
            await _powerSupply3V3.ConnectAsync(PowerSupply3V3IpAddress, token).ConfigureAwait(false);
        }

        private void OnManualTest()
        {
            if (IsManualTestRunning)
            {
                _ = StopManualTestAsync();
                return;
            }

            _ = StartManualTestAsync();
        }

        private async Task StartManualTestAsync()
        {
            await _manualTestLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (IsManualTestRunning)
                    return;

                ClearResults();
                IsBusy = true;
                try
                {
                    if (!IsPowerOn && !_isTestPowerOn)
                    {
                        await PowerOnHardwareAsync(CancellationToken.None).ConfigureAwait(false);
                        _isTestPowerOn = true;
                    }

                    IsManualTestRunning = true;
                    AddLog($"[{DateTime.Now:HH:mm:ss}] ========== 手动测试开始 ==========");
                }
                finally
                {
                    IsBusy = false;
                }
            }
            finally
            {
                _manualTestLock.Release();
            }
        }

        private async Task StopManualTestAsync()
        {
            await _manualTestLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!IsManualTestRunning)
                    return;

                IsBusy = true;
                try
                {
                    await DisconnectFpgaAsync(CancellationToken.None).ConfigureAwait(false);
                    if (_isTestPowerOn)
                    {
                        await PowerOffHardwareAsync(CancellationToken.None).ConfigureAwait(false);
                        _isTestPowerOn = false;
                    }
                    IsManualTestRunning = false;
                    AddLog($"[{DateTime.Now:HH:mm:ss}] ========== 手动测试已停止 ==========");
                }
                finally
                {
                    IsBusy = false;
                }
            }
            finally
            {
                _manualTestLock.Release();
            }
        }

        private void OnAutoTest()
        {
            if (IsAutoTestRunning)
            {
                _autoTestCts?.Cancel();
                return;
            }

            _ = StartAutoTestAsync();
        }

        private async Task StartAutoTestAsync()
        {
            await _autoTestLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (IsAutoTestRunning)
                    return;

                ClearResults();
                IsAutoTestRunning = true;
                _autoTestCts = new CancellationTokenSource();
                var token = _autoTestCts.Token;

                try
                {
                    if (!IsPowerOn)
                    {
                        IsBusy = true;
                        try
                        {
                            if (!_isTestPowerOn)
                            {
                                await PowerOnHardwareAsync(token).ConfigureAwait(false);
                                _isTestPowerOn = true;
                            }
                        }
                        finally
                        {
                            IsBusy = false;
                        }
                    }

                    AddLog($"[{DateTime.Now:HH:mm:ss}] ========== 自动测试开始 ==========");

                    bool ok100 = await SendAndMeasureFixedAsync(100, delayBeforeMeasureMs: 500, token).ConfigureAwait(false);
                    bool ok50 = await SendAndMeasureFixedAsync(50, delayBeforeMeasureMs: 500, token).ConfigureAwait(false);
                    bool ok0 = await SendAndMeasureFixedAsync(0, delayBeforeMeasureMs: 500, token).ConfigureAwait(false);

                    var ok = ok100 && ok50 && ok0;
                    SetLastTestResult(ok ? "PASS" : "FAIL");

                    AddLog($"[{DateTime.Now:HH:mm:ss}] ========== 自动测试完成: {(ok ? "PASS" : "FAIL")} ==========");
                }
                catch (OperationCanceledException)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试已取消");
                }
                catch (Exception ex)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试异常: {ex.Message}");
                }
                finally
                {
                    try { await DisconnectFpgaAsync(CancellationToken.None).ConfigureAwait(false); } catch { }

                    try
                    {
                        if (_isTestPowerOn)
                        {
                            IsBusy = true;
                            try
                            {
                                await PowerOffHardwareAsync(CancellationToken.None).ConfigureAwait(false);
                                _isTestPowerOn = false;
                            }
                            finally
                            {
                                IsBusy = false;
                            }
                        }
                    }
                    catch { }

                    IsAutoTestRunning = false;
                    try { _autoTestCts?.Dispose(); } catch { }
                    _autoTestCts = null;
                }
            }
            finally
            {
                _autoTestLock.Release();
            }
        }

        private void SetLastTestResult(string result)
        {
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                LastTestResult = result;
                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            });
        }

        private void ClearResults()
        {
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                PwmCustomResult = "--";
                Pwm100Result = "--";
                Pwm50Result = "--";
                Pwm0Result = "--";
                LastTestResult = "--";
                LastTestTime = "--";
            });
        }

        private async Task SendCustomAsync()
        {
            if (!IsConfigSendMeasureEnabled)
                return;

            IsBusy = true;
            try
            {
                await SendPwmFrameAsync(PwmDutyPct, PwmFrequencyHz, sendInit: true, CancellationToken.None).ConfigureAwait(false);
                AddLog($"PWM={PwmDutyPct}%：已发送到 FPGA，请点击“测量”查看波形");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task MeasureCustomAsync()
        {
            if (!IsConfigSendMeasureEnabled)
                return;

            IsMeasuringPwmCustom = true;
            try
            {
                var m = await MeasureOnceWithMatrixAsync("自定义PWM", CancellationToken.None).ConfigureAwait(false);
                if (m == null)
                {
                    PwmCustomResult = "FAIL";
                    return;
                }

                PwmCustomResult = IsMeasurementPass(m, expectedDutyPct: PwmDutyPct, out _) ? "PASS" : "FAIL";
            }
            finally
            {
                IsMeasuringPwmCustom = false;
            }
        }

        private async Task SendFixedGearAsync(int dutyPct)
        {
            if (IsBusy || !IsManualTestRunning)
                return;

            IsBusy = true;
            try
            {
                await SendPwmFrameAsync(dutyPct, FixedGearFrequencyHz, sendInit: dutyPct == 100, CancellationToken.None).ConfigureAwait(false);
                AddLog($"PWM={dutyPct}%：已发送到 FPGA，请点击“测量”查看波形");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task MeasureFixedGearAsync(int dutyPct)
        {
            if (IsBusy || !IsManualTestRunning)
                return;

            await SetMeasuringAsync(dutyPct, true);
            try
            {
                var m = await MeasureOnceWithMatrixAsync($"PWM={dutyPct}%", CancellationToken.None).ConfigureAwait(false);
                var pass = m != null && IsMeasurementPass(m, dutyPct, out _);
                SetFixedResult(dutyPct, pass ? "PASS" : "FAIL");
            }
            finally
            {
                await SetMeasuringAsync(dutyPct, false);
            }
        }

        private async Task RouteMatrixToScopeAsync(string title, CancellationToken token)
        {
            await _instrumentLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                var matrix = MatrixControlService.Instance;

                if (_isMatrixRouted)
                {
                    await UnrouteMatrixAsync(token).ConfigureAwait(false);
                }

                AddLog($"[{DateTime.Now:HH:mm:ss}] 预览路由{title}：slot6 {Slot6Row}-{Slot6ToScope} + slot4 {Slot4Row}-{Slot4ToScope}");

                _matrixRoutedSlot6 = await matrix.ConnectNodesAsync(Slot6Row, Slot6ToScope, Chassis2Slot6, MatrixIpAddress, MatrixTcpBasePort).ConfigureAwait(false);
                _matrixRoutedSlot4 = await matrix.ConnectNodesAsync(Slot4Row, Slot4ToScope, Chassis2Slot4, MatrixIpAddress, MatrixTcpBasePort).ConfigureAwait(false);

                _isMatrixRouted = _matrixRoutedSlot6 && _matrixRoutedSlot4;

                AddLog($"[{DateTime.Now:HH:mm:ss}] 预览路由{title}：slot6 {(_matrixRoutedSlot6 ? "OK" : "FAIL")}, slot4 {(_matrixRoutedSlot4 ? "OK" : "FAIL")}");
                if (!_isMatrixRouted)
                {
                    await UnrouteMatrixAsync(token).ConfigureAwait(false);
                    return;
                }

                await EnsureScopeConnectedAsync(token).ConfigureAwait(false);
            }
            finally
            {
                _instrumentLock.Release();
            }
        }

        private async Task UnrouteMatrixAsync(CancellationToken token)
        {
            if (!_isMatrixRouted && !_matrixRoutedSlot6 && !_matrixRoutedSlot4)
                return;

            var matrix = MatrixControlService.Instance;
            try
            {
                if (_matrixRoutedSlot4)
                    _ = await matrix.DisconnectNodesAsync(Slot4Row, Slot4ToScope, Chassis2Slot4, MatrixIpAddress, MatrixTcpBasePort).ConfigureAwait(false);
            }
            catch { }

            try
            {
                if (_matrixRoutedSlot6)
                    _ = await matrix.DisconnectNodesAsync(Slot6Row, Slot6ToScope, Chassis2Slot6, MatrixIpAddress, MatrixTcpBasePort).ConfigureAwait(false);
            }
            catch { }

            _isMatrixRouted = false;
            _matrixRoutedSlot6 = false;
            _matrixRoutedSlot4 = false;
        }

        private async Task<bool> SendAndMeasureFixedAsync(int dutyPct, int delayBeforeMeasureMs, CancellationToken token)
        {
            await _opLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                await SetMeasuringAsync(dutyPct, true);
                try
                {
                    await SendPwmFrameAsync(dutyPct, FixedGearFrequencyHz, sendInit: dutyPct == 100, token).ConfigureAwait(false);
                    if (delayBeforeMeasureMs > 0)
                        await Task.Delay(delayBeforeMeasureMs, token).ConfigureAwait(false);

                    var m = await MeasureOnceWithMatrixAsync($"PWM={dutyPct}%", token).ConfigureAwait(false);
                    string reason = null;
                    var pass = m != null && IsMeasurementPass(m, dutyPct, out reason);
                    if (!pass && !string.IsNullOrWhiteSpace(reason))
                        AddLog($"PWM={dutyPct}% 判据失败: {reason}");

                    SetFixedResult(dutyPct, pass ? "PASS" : "FAIL");
                    return pass;
                }
                finally
                {
                    await SetMeasuringAsync(dutyPct, false);
                }
            }
            finally
            {
                _opLock.Release();
            }
        }

        private async Task SetMeasuringAsync(int dutyPct, bool value)
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                switch (dutyPct)
                {
                    case 100:
                        IsMeasuringPwm100 = value;
                        break;
                    case 50:
                        IsMeasuringPwm50 = value;
                        break;
                    case 0:
                        IsMeasuringPwm0 = value;
                        break;
                }
            });
        }

        private void SetFixedResult(int dutyPct, string result)
        {
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                switch (dutyPct)
                {
                    case 100:
                        Pwm100Result = result;
                        break;
                    case 50:
                        Pwm50Result = result;
                        break;
                    case 0:
                        Pwm0Result = result;
                        break;
                }
            });
        }

        private sealed class MeasurementResult
        {
            public string Title { get; set; }
            public double? Vmax { get; set; }
            public double? Vmin { get; set; }
            public double? Vpp { get; set; }
            public double? DutyPct { get; set; }
        }

        private static bool IsMeasurementPass(MeasurementResult m, int expectedDutyPct, out string reason)
        {
            if (m == null)
            {
                reason = "测量结果为空";
                return false;
            }

            if (expectedDutyPct == 0)
            {
                if (!m.Vmax.HasValue || !m.Vmin.HasValue)
                {
                    reason = "VMAX/VMIN无有效值";
                    return false;
                }

                if (m.Vmax.Value > 1.0 || m.Vmin.Value < -1.0)
                {
                    reason = $"电压不在[-1,1]V：VMAX={m.Vmax.Value:F4}V, VMIN={m.Vmin.Value:F4}V";
                    return false;
                }

                reason = null;
                return true;
            }

            if (!m.Vmax.HasValue)
            {
                reason = "VMAX无有效值";
                return false;
            }

            if (m.Vmax.Value < 17.0 || m.Vmax.Value > 32.0)
            {
                reason = $"VMAX不在[17,32]V：{m.Vmax.Value:F4}V";
                return false;
            }

            if (!m.DutyPct.HasValue)
            {
                reason = "占空比无有效值";
                return false;
            }

            if (Math.Abs(m.DutyPct.Value - expectedDutyPct) > 1.0)
            {
                reason = $"占空比不在({expectedDutyPct}±1)%：{m.DutyPct.Value:F3}%";
                return false;
            }

            reason = null;
            return true;
        }

        private async Task SendPwmFrameAsync(int dutyPct, int freqHz, bool sendInit, CancellationToken token)
        {
            dutyPct = ClampDuty(dutyPct);
            freqHz = ClampFreq(freqHz);

            AddLog($"PWM={dutyPct}%：发送到FPGA... (Freq={freqHz}Hz)");
            await EnsureFpgaConnectedAsync(token).ConfigureAwait(false);

            if (sendInit)
            {
                await _fpga.WriteAsync(DeviceInitCommandFrame, 0, DeviceInitCommandFrame.Length, token).ConfigureAwait(false);
                AddLog($"PWM={dutyPct}%：已发送设备初始化 {FormatData(DeviceInitCommandFrame)}");
            }

            var cmd = BuildPwmCommand(dutyPct, freqHz);
            await _fpga.WriteAsync(cmd, 0, cmd.Length, token).ConfigureAwait(false);
            AddLog($"PWM={dutyPct}%：已发送 {FormatData(cmd)}");
        }

        private async Task EnsureFpgaConnectedAsync(CancellationToken token)
        {
            if (_fpga != null && _fpga.IsConnected)
                return;

            _fpga?.Dispose();
            _fpga = new FpgaTcpClient();
            await _fpga.ConnectAsync(FpgaIpAddress, FpgaPort, token).ConfigureAwait(false);
            AddLog("FPGA连接成功");
        }

        private async Task DisconnectFpgaAsync(CancellationToken token)
        {
            await _opLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (_fpga != null && _fpga.IsConnected)
                {
                    try
                    {
                        AddLog($"FPGA复位: 发送 {FormatData(ResetToInitialCommandFrame)}");
                        await _fpga.WriteAsync(ResetToInitialCommandFrame, 0, ResetToInitialCommandFrame.Length, token).ConfigureAwait(false);
                    }
                    catch { }
                }

                try { _fpga?.Disconnect(); } catch { }
                try { _fpga?.Dispose(); } catch { }
                _fpga = null;
            }
            finally
            {
                _opLock.Release();
            }
        }

        private async Task<MeasurementResult> MeasureOnceWithMatrixAsync(string title, CancellationToken token)
        {
            await _instrumentLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                var matrix = MatrixControlService.Instance;

                bool ok1 = false;
                bool ok2 = false;

                try
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 路由{title}：slot6 {Slot6Row}-{Slot6ToScope} + slot4 {Slot4Row}-{Slot4ToScope}");

                    ok1 = await matrix.ConnectNodesAsync(Slot6Row, Slot6ToScope, Chassis2Slot6, MatrixIpAddress, MatrixTcpBasePort).ConfigureAwait(false);
                    ok2 = await matrix.ConnectNodesAsync(Slot4Row, Slot4ToScope, Chassis2Slot4, MatrixIpAddress, MatrixTcpBasePort).ConfigureAwait(false);

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 路由{title}：slot6 {(ok1 ? "OK" : "FAIL")}, slot4 {(ok2 ? "OK" : "FAIL")}");
                    if (!ok1 || !ok2)
                        return null;

                    await EnsureScopeConnectedAsync(token).ConfigureAwait(false);
                    await Task.Delay(200, token).ConfigureAwait(false);

                    var vmax = await QueryScopeDoubleAsync(":MEASure:ITEM? VMAX", token).ConfigureAwait(false);
                    var vmin = await QueryScopeDoubleAsync(":MEASure:ITEM? VMIN", token).ConfigureAwait(false);
                    var vpp = await QueryScopeDoubleAsync(":MEASure:ITEM? VPP", token).ConfigureAwait(false);
                    var duty = await QueryScopeDoubleAsync(":MEASure:ITEM? DUTY", token).ConfigureAwait(false);

                    return new MeasurementResult
                    {
                        Title = title,
                        Vmax = vmax,
                        Vmin = vmin,
                        Vpp = vpp,
                        DutyPct = duty
                    };
                }
                finally
                {
                    try
                    {
                        if (ok2)
                            _ = await matrix.DisconnectNodesAsync(Slot4Row, Slot4ToScope, Chassis2Slot4, MatrixIpAddress, MatrixTcpBasePort).ConfigureAwait(false);
                    }
                    catch { }

                    try
                    {
                        if (ok1)
                            _ = await matrix.DisconnectNodesAsync(Slot6Row, Slot6ToScope, Chassis2Slot6, MatrixIpAddress, MatrixTcpBasePort).ConfigureAwait(false);
                    }
                    catch { }
                }
            }
            finally
            {
                _instrumentLock.Release();
            }
        }

        private async Task EnsureScopeConnectedAsync(CancellationToken token)
        {
            if (_scopeTcpClient != null && _scopeTcpStream != null)
                return;

            _scopeTcpClient = new TcpClient();
            await _scopeTcpClient.ConnectAsync(ScopeIpAddress, ScopePort).ConfigureAwait(false);
            _scopeTcpStream = _scopeTcpClient.GetStream();

            try
            {
                _scopeTcpStream.ReadTimeout = 5000;
                _scopeTcpStream.WriteTimeout = 5000;
            }
            catch
            {
            }

            await QueryScopeStringAsync(":MEASure:SOURce CHANnel1", token).ConfigureAwait(false);
            await QueryScopeStringAsync(":MEASure:CLEar", token).ConfigureAwait(false);
        }

        private async Task<double?> QueryScopeDoubleAsync(string command, CancellationToken token)
        {
            var s = await QueryScopeStringAsync(command, token).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(s))
                return null;

            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                return v;

            return null;
        }

        private async Task<string> QueryScopeStringAsync(string command, CancellationToken token)
        {
            if (_scopeTcpStream == null)
                throw new InvalidOperationException("示波器未连接");

            var cmd = Encoding.ASCII.GetBytes(command + "\n");
            await _scopeTcpStream.WriteAsync(cmd, 0, cmd.Length, token).ConfigureAwait(false);
            await _scopeTcpStream.FlushAsync(token).ConfigureAwait(false);

            if (!command.TrimEnd().EndsWith("?", StringComparison.Ordinal))
                return string.Empty;

            return await ReadLineAsync(_scopeTcpStream, 5000, token).ConfigureAwait(false);
        }

        private static async Task<string> ReadLineAsync(NetworkStream stream, int timeoutMs, CancellationToken token)
        {
            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                cts.CancelAfter(timeoutMs);

                var sb = new StringBuilder();
                var buffer = new byte[1];

                while (true)
                {
                    int n = await stream.ReadAsync(buffer, 0, 1, cts.Token).ConfigureAwait(false);
                    if (n <= 0)
                        break;

                    char ch = (char)buffer[0];
                    if (ch == '\n')
                        break;
                    if (ch == '\r')
                        continue;
                    sb.Append(ch);

                    if (sb.Length > 4096)
                        break;
                }

                return sb.ToString().Trim();
            }
        }

        private static byte[] BuildPwmCommand(int dutyPct, int freqHz)
        {
            dutyPct = ClampDuty(dutyPct);
            freqHz = ClampFreq(freqHz);

            var periodUs = (long)Math.Round(1_000_000.0 / freqHz);
            if (periodUs < 1)
                periodUs = 1;

            long onUs;
            if (dutyPct >= 100)
                onUs = 0;
            else if (dutyPct <= 0)
                onUs = periodUs;
            else
                onUs = (long)Math.Round(periodUs * dutyPct / 100.0);

            if (onUs < 0)
                onUs = 0;
            if (onUs > periodUs)
                onUs = periodUs;

            ulong pScaled = (ulong)periodUs * 10UL;
            ulong dutyScaled = (ulong)onUs * 10UL;
            if (dutyScaled > pScaled)
                dutyScaled = pScaled;

            uint p = pScaled > uint.MaxValue ? uint.MaxValue : (uint)pScaled;
            uint on = dutyScaled > p ? p : (uint)dutyScaled;

            return new byte[]
            {
                0xAA, 0x55,
                0x0A,0x03,
                0x03,
                (byte)(p & 0xFF), (byte)((p >> 8) & 0xFF), (byte)((p >> 16) & 0xFF), (byte)((p >> 24) & 0xFF),
                (byte)(on & 0xFF), (byte)((on >> 8) & 0xFF), (byte)((on >> 16) & 0xFF), (byte)((on >> 24) & 0xFF)
            };
        }

        private static int ClampDuty(int dutyPct)
        {
            if (dutyPct < 0) return 0;
            if (dutyPct > 100) return 100;
            return dutyPct;
        }

        private static int ClampFreq(int freqHz)
        {
            if (freqHz < 10) return 10;
            if (freqHz > 5000) return 5000;
            return freqHz;
        }

        private static string FormatData(byte[] data)
        {
            if (data == null)
                return "--";
            return BitConverter.ToString(data).Replace("-", " ");
        }

        public void Dispose()
        {
            try { _autoTestCts?.Cancel(); } catch { }
            try { _autoTestCts?.Dispose(); } catch { }
            _autoTestCts = null;

            try { DisconnectFpgaAsync(CancellationToken.None).GetAwaiter().GetResult(); } catch { }

            try { UnrouteMatrixAsync(CancellationToken.None).GetAwaiter().GetResult(); } catch { }

            try { _scopeTcpStream?.Dispose(); } catch { }
            _scopeTcpStream = null;
            try { _scopeTcpClient?.Close(); } catch { }
            try { _scopeTcpClient?.Dispose(); } catch { }
            _scopeTcpClient = null;

            try { _powerSupply28V?.DisconnectAsync(CancellationToken.None).GetAwaiter().GetResult(); } catch { }
            try { _powerSupply28V?.DisposeAsync().AsTask().Wait(1000); } catch { }
            _powerSupply28V = null;

            try { _powerSupply3V3?.DisconnectAsync(CancellationToken.None).GetAwaiter().GetResult(); } catch { }
            try { _powerSupply3V3?.DisposeAsync().AsTask().Wait(1000); } catch { }
            _powerSupply3V3 = null;

            try { _manualTestLock?.Dispose(); } catch { }
            try { _autoTestLock?.Dispose(); } catch { }
            try { _opLock?.Dispose(); } catch { }
            try { _instrumentLock?.Dispose(); } catch { }
        }
    }
}
