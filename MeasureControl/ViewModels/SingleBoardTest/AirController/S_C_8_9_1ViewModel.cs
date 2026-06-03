using Prism.Commands;
using Prism.Mvvm;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using MeasureControl.Helpers;
using MeasureControl.Services;
using MeasureControl.Services.HardwareApis;

namespace MeasureControl.ViewModels.SingleBoardTest.AirController
{
    public sealed class S_C_8_9_1ViewModel : BindableBase, IDisposable
    {
        private const string DefaultFpgaIpAddress = "192.168.1.10";
        private const int DefaultFpgaPort = 5001;

        private const string DefaultMatrixIpAddress = "192.168.1.3";
        private const int DefaultMatrixTcpBasePort = 50200;

        private const int Chassis2Slot6 = 6;
        private const int Chassis2Slot4 = 4;

        private const string Slot6Row = "I1";
        private const string Slot4Row = "I0";
        private const string Slot4ToScope = "O1";

        private const string DefaultSlot6OutputToScope = "O0";
        private const string DcmV1ToScope = "O15";
        private const string DcmV2ToScope = "O14";
        private const string DcmV1V1ToScope = "O16";

        private const int DefaultScopePort = 5555;
        private const string DefaultScopeIpAddress = "192.168.1.18";

        private const string PowerSupply28VIpAddress = "192.168.1.15";

        private const PowerSupplyChannel PowerSupply28VCh1 = PowerSupplyChannel.CH1;
        private const double Power28VVoltage = 28.0;
        private const double Power28VCurrentLimit = 3.0;

        private static readonly byte[] DeviceInitCommandFrame = { 0xAA, 0x55, 0x02, 0x02, 0x01 };
        private static readonly byte[] ResetToInitialCommandFrame = { 0xAA, 0x55, 0x0A, 0x05, 0x00, 0xA8, 0x00, 0x00, 0x00, 0xA8, 0x00, 0x00, 0x00 };

        private const int FixedGearFrequencyHz = 2000;

        private readonly SemaphoreSlim _manualTestLock = new SemaphoreSlim(1, 1); 
        private readonly SemaphoreSlim _autoTestLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _opLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _instrumentLock = new SemaphoreSlim(1, 1); 
        private readonly SemaphoreSlim _scopeIoLock = new SemaphoreSlim(1, 1);

        private CancellationTokenSource _manualMeasureCts;
        private CancellationTokenSource _autoTestCts;

        private FpgaTcpClient _fpga;

        private TcpClient _scopeTcpClient;
        private NetworkStream _scopeTcpStream;

        private IPowerSupplyApi _powerSupply28V;

        private bool _isPowerOn;
        private string _powerStatus = "未上电";

        private bool _isTestPowerOn;

        private bool _isMatrixRouted;
        private bool _matrixRoutedSlot6;
        private bool _matrixRoutedSlot4;
        private string _matrixRoutedSlot6Output;

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

        private string _j8VrmsText = "--";
        private string _j8FreqHzText = "--";
        private string _j8VmaxText = "--";
        private string _j8VminText = "--";
        private string _j8VppText = "--";
        private string _j8DutyPctText = "--";

        private string _j9VrmsText = "--";
        private string _j9FreqHzText = "--";
        private string _j9VmaxText = "--";
        private string _j9VminText = "--";
        private string _j9VppText = "--";
        private string _j9DutyPctText = "--";

        private string _j8j9VrmsText = "--";
        private string _j8j9FreqHzText = "--";
        private string _j8j9VmaxText = "--";
        private string _j8j9VminText = "--";
        private string _j8j9VppText = "--";
        private string _j8j9DutyPctText = "--";

        private string _fpgaIpAddress = DefaultFpgaIpAddress;
        private int _fpgaPort = DefaultFpgaPort;

        private string _scopeIpAddress = DefaultScopeIpAddress;
        private int _scopePort = DefaultScopePort;

        private string _matrixIpAddress = DefaultMatrixIpAddress;
        private int _matrixTcpBasePort = DefaultMatrixTcpBasePort;

        public S_C_8_9_1ViewModel()
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

        public string J8VrmsText
        {
            get => _j8VrmsText;
            private set => SetProperty(ref _j8VrmsText, value);
        }

        public string J8FreqHzText
        {
            get => _j8FreqHzText;
            private set => SetProperty(ref _j8FreqHzText, value);
        }

        public string J8VmaxText
        {
            get => _j8VmaxText;
            private set => SetProperty(ref _j8VmaxText, value);
        }

        public string J8VminText
        {
            get => _j8VminText;
            private set => SetProperty(ref _j8VminText, value);
        }

        public string J8VppText
        {
            get => _j8VppText;
            private set => SetProperty(ref _j8VppText, value);
        }

        public string J8DutyPctText
        {
            get => _j8DutyPctText;
            private set => SetProperty(ref _j8DutyPctText, value);
        }

        public string J9VrmsText
        {
            get => _j9VrmsText;
            private set => SetProperty(ref _j9VrmsText, value);
        }

        public string J9FreqHzText
        {
            get => _j9FreqHzText;
            private set => SetProperty(ref _j9FreqHzText, value);
        }

        public string J9VmaxText
        {
            get => _j9VmaxText;
            private set => SetProperty(ref _j9VmaxText, value);
        }

        public string J9VminText
        {
            get => _j9VminText;
            private set => SetProperty(ref _j9VminText, value);
        }

        public string J9VppText
        {
            get => _j9VppText;
            private set => SetProperty(ref _j9VppText, value);
        }

        public string J9DutyPctText
        {
            get => _j9DutyPctText;
            private set => SetProperty(ref _j9DutyPctText, value);
        }

        public string J8J9VrmsText
        {
            get => _j8j9VrmsText;
            private set => SetProperty(ref _j8j9VrmsText, value);
        }

        public string J8J9FreqHzText
        {
            get => _j8j9FreqHzText;
            private set => SetProperty(ref _j8j9FreqHzText, value);
        }

        public string J8J9VmaxText
        {
            get => _j8j9VmaxText;
            private set => SetProperty(ref _j8j9VmaxText, value);
        }

        public string J8J9VminText
        {
            get => _j8j9VminText;
            private set => SetProperty(ref _j8j9VminText, value);
        }

        public string J8J9VppText
        {
            get => _j8j9VppText;
            private set => SetProperty(ref _j8j9VppText, value);
        }

        public string J8J9DutyPctText
        {
            get => _j8j9DutyPctText;
            private set => SetProperty(ref _j8j9DutyPctText, value);
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
                await _powerSupply28V.SetOutputEnabledAsync(PowerSupply28VCh1, true, token).ConfigureAwait(false);
                await Task.Delay(300, token).ConfigureAwait(false);
                AddLog($"组件供电：28V 上电(程控电源1) CH1, IP={PowerSupply28VIpAddress}");
            }
            catch (Exception ex)
            {
                AddLog($"组件供电：28V 上电失败(程控电源1) CH1: {ex.Message}");
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
                if (_powerSupply28V != null)
                    try { await _powerSupply28V.SetOutputEnabledAsync(PowerSupply28VCh1, false, token).ConfigureAwait(false); } catch { }
            }
            catch { }

            await Task.Delay(200, token).ConfigureAwait(false);
            AddLog("组件供电：下电完成");
        }

        private async Task EnsurePowerSupply28VConnectedAsync(CancellationToken token)
        {
            if (_powerSupply28V != null && _powerSupply28V.IsConnected)
                return;

            _powerSupply28V ??= new PowerSupplySocketApi();
            await _powerSupply28V.ConnectAsync(PowerSupply28VIpAddress, token).ConfigureAwait(false);
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
                    await PowerOnHardwareAsync(CancellationToken.None).ConfigureAwait(false);
                    _isTestPowerOn = true;

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
                    await PowerOffHardwareAsync(CancellationToken.None).ConfigureAwait(false);
                    _isTestPowerOn = false;
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
                    IsBusy = true;
                    try
                    {
                        await PowerOnHardwareAsync(token).ConfigureAwait(false);
                        _isTestPowerOn = true;
                    }
                    finally
                    {
                        IsBusy = false;
                    }

                    AddLog($"[{DateTime.Now:HH:mm:ss}] ========== 自动测试开始 ==========");

                    var r100 = await SendAndMeasureFixedAsync(100, delayBeforeMeasureMs: 500, token).ConfigureAwait(false);
                    var r50 = await SendAndMeasureFixedAsync(50, delayBeforeMeasureMs: 500, token).ConfigureAwait(false);
                    var r0 = await SendAndMeasureFixedAsync(0, delayBeforeMeasureMs: 500, token).ConfigureAwait(false);

                    var ok = r100.Pass && r50.Pass && r0.Pass;
                    var failPoint = !r100.Pass ? r100.FailPoint : (!r50.Pass ? r50.FailPoint : (!r0.Pass ? r0.FailPoint : string.Empty));
                    SetLastTestResult(ok ? "PASS" : (string.IsNullOrWhiteSpace(failPoint) ? "FAIL" : $"FAIL({failPoint})"));

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

                J8VmaxText = "--";
                J8VminText = "--";
                J8VppText = "--";
                J8DutyPctText = "--";

                J9VmaxText = "--";
                J9VminText = "--";
                J9VppText = "--";
                J9DutyPctText = "--";

                J8J9VmaxText = "--";
                J8J9VminText = "--";
                J8J9VppText = "--";
                J8J9DutyPctText = "--";
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
                var (pass, failPoint) = await MeasureAllPointsAsync(PwmDutyPct, CancellationToken.None).ConfigureAwait(false);
                PwmCustomResult = pass ? "PASS" : $"FAIL({failPoint})";
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

            SetMeasuring(dutyPct, true);
            try
            {
                var (pass, failPoint) = await MeasureAllPointsAsync(dutyPct, CancellationToken.None).ConfigureAwait(false);
                SetFixedResult(dutyPct, pass ? "PASS" : $"FAIL({failPoint})");
            }
            finally
            {
                SetMeasuring(dutyPct, false);
            }
        }

        private async Task<(bool Pass, string FailPoint)> SendAndMeasureFixedAsync(int dutyPct, int delayBeforeMeasureMs, CancellationToken token)
        {
            await _opLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                SetMeasuring(dutyPct, true);
                try
                {
                    await SendPwmFrameAsync(dutyPct, FixedGearFrequencyHz, sendInit: dutyPct == 100, token).ConfigureAwait(false);
                    if (delayBeforeMeasureMs > 0)
                        await Task.Delay(delayBeforeMeasureMs, token).ConfigureAwait(false);

                    var (pass, failPoint) = await MeasureAllPointsAsync(dutyPct, token).ConfigureAwait(false);
                    SetFixedResult(dutyPct, pass ? "PASS" : $"FAIL({failPoint})");
                    return (pass, failPoint);
                }
                finally
                {
                    SetMeasuring(dutyPct, false);
                }
            }
            finally
            {
                _opLock.Release();
            }
        }

        private void SetMeasuring(int dutyPct, bool value)
        {
            Application.Current?.Dispatcher?.Invoke(() =>
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

        private static bool IsMeasurementPass(MeasurementResult m, int expectedDutyPct, int expectedFreqHz, out string reason)
        {
            if (m == null)
            {
                reason = "测量结果为空";
                return false;
            }

            // 0% PWM: VRMS should be near 0V (DC low level, VRMS avoids glitch peaks)
            if (expectedDutyPct == 0)
            {
                if (!m.Vrms.HasValue)
                {
                    reason = "示波器VRMS无有效值";
                    return false;
                }

                if (m.Vrms.Value < -1.0 || m.Vrms.Value > 1.0)
                {
                    reason = $"VRMS不在[-1,1]V：{m.Vrms.Value:F4}V";
                    return false;
                }

                reason = null;
                return true;
            }

            // 100% PWM: VRMS should be in [17,32]V (DC high level, VRMS avoids glitch peaks)
            if (expectedDutyPct == 100)
            {
                if (!m.Vrms.HasValue)
                {
                    reason = "示波器VRMS无有效值";
                    return false;
                }

                if (m.Vrms.Value < 17.0 || m.Vrms.Value > 32.0)
                {
                    reason = $"VRMS不在[17,32]V：{m.Vrms.Value:F4}V";
                    return false;
                }

                reason = null;
                return true;
            }

            // 50% PWM: duty cycle within ±1% (calculated from PWIDth/NWIDth)
            if (expectedDutyPct == 50)
            {
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

            // Custom PWM: frequency within ±5% (min ±2Hz) and duty cycle within ±1%
            if (!m.FreqHz.HasValue)
            {
                reason = "频率无有效值";
                return false;
            }

            var freqTol = Math.Max(expectedFreqHz * 0.05, 2.0);
            if (Math.Abs(m.FreqHz.Value - expectedFreqHz) > freqTol)
            {
                reason = $"频率不在({expectedFreqHz}±{freqTol:F1})Hz：{m.FreqHz.Value:F3}Hz";
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

                var slot6Output = NormalizeSlot6Output(DcmV1ToScope);
                AddLog($"[{DateTime.Now:HH:mm:ss}] 预览路由{title}：slot6 {Slot6Row}-{slot6Output} + slot4 {Slot4Row}-{Slot4ToScope}");

                _matrixRoutedSlot6Output = slot6Output;
                _matrixRoutedSlot6 = await matrix.ConnectNodesAsync(Slot6Row, slot6Output, Chassis2Slot6, MatrixIpAddress, MatrixTcpBasePort).ConfigureAwait(false);
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
                    _ = await matrix.DisconnectNodesAsync(Slot6Row, _matrixRoutedSlot6Output, Chassis2Slot6, MatrixIpAddress, MatrixTcpBasePort).ConfigureAwait(false);
            }
            catch { }

            _isMatrixRouted = false;
            _matrixRoutedSlot6 = false;
            _matrixRoutedSlot4 = false;
            _matrixRoutedSlot6Output = null;
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
                        await Task.Delay(500, token).ConfigureAwait(false);
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

        private static string NormalizeSlot6Output(string slot6Output)
        {
            return string.IsNullOrWhiteSpace(slot6Output) ? DefaultSlot6OutputToScope : slot6Output;
        }

        private async Task<MeasurementResult> MeasureOnceWithMatrixAsync(string slot6Output, string title, int expectedDutyPct, CancellationToken token)
        {
            slot6Output = NormalizeSlot6Output(slot6Output);
            await _instrumentLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                var matrix = MatrixControlService.Instance;

                bool ok1 = false;
                bool ok2 = false;

                try
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 路由{title}：slot6 {Slot6Row}-{slot6Output} + slot4 {Slot4Row}-{Slot4ToScope}");

                    ok1 = await matrix.ConnectNodesAsync(Slot6Row, slot6Output, Chassis2Slot6, MatrixIpAddress, MatrixTcpBasePort).ConfigureAwait(false);
                    ok2 = await matrix.ConnectNodesAsync(Slot4Row, Slot4ToScope, Chassis2Slot4, MatrixIpAddress, MatrixTcpBasePort).ConfigureAwait(false);

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 路由{title}：slot6 {(ok1 ? "OK" : "FAIL")}, slot4 {(ok2 ? "OK" : "FAIL")}");
                    if (!ok1 || !ok2)
                        return null;

                    await EnsureScopeConnectedAsync(token).ConfigureAwait(false);

                    // Send AUToscale to auto-configure the scope settings
                    AddLog($"[{DateTime.Now:HH:mm:ss}] {title}：发送 :AUToscale (示波器自动设置) 并等待完成...");
                    try
                    {
                        await SendScopeCommandAsync(":AUToscale", token).ConfigureAwait(false);
                        try
                        {
                            var opc = await QueryScopeAsync("*OPC?", 20000, token).ConfigureAwait(false);
                            _ = opc;
                        }
                        catch (Exception ex)
                        {
                            AddLog($"[{DateTime.Now:HH:mm:ss}] {title}：等待 *OPC? 超时/异常：{ex.Message}");
                        }
                    }
                    catch (Exception ex)
                    {
                        AddLog($"[{DateTime.Now:HH:mm:ss}] {title}：发送 :AUToscale 失败：{ex.Message}");
                    }

                    // Configure measurement items based on PWM percentage
                    double? vrms = null, freq = null, vmax = null, vmin = null, vpp = null, duty = null;

                    if (expectedDutyPct == 100 || expectedDutyPct == 0)
                    {
                        // 100%/0% PWM: measure VRMS only (DC signal, VRMS avoids glitch peaks)
                        AddLog($"[{DateTime.Now:HH:mm:ss}] {title}：PWM={expectedDutyPct}%，配置测量项 VRMS...");
                        try
                        {
                            await SendScopeCommandAsync(":MEASure:SOURce CHANnel1", token).ConfigureAwait(false);
                            await SendScopeCommandAsync(":MEASure:CLEar", token).ConfigureAwait(false);
                            await SendScopeCommandAsync(":MEASure:ITEM VRMS", token).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            AddLog($"[{DateTime.Now:HH:mm:ss}] {title}：配置测量项异常：{ex.Message}");
                        }

                        // Delay 5s for waveform stabilization
                        AddLog($"[{DateTime.Now:HH:mm:ss}] {title}：延时5秒等待波形稳定...");
                        await Task.Delay(5000, token).ConfigureAwait(false);

                        try
                        {
                            var rawVrms = await QueryScopeAsync(":MEASure:ITEM? VRMS", 10000, token).ConfigureAwait(false);
                            vrms = ParseScopeDouble(rawVrms);
                        }
                        catch (Exception ex)
                        {
                            AddLog($"[{DateTime.Now:HH:mm:ss}] {title}：查询VRMS异常：{ex.Message}");
                        }

                        AddLog($"[{DateTime.Now:HH:mm:ss}] {title}：测量完成 VRMS={FormatNum(vrms)}V");
                    }
                    else if (expectedDutyPct == 50)
                    {
                        // 50% PWM: measure PWIDth + NWIDth for duty cycle calculation
                        AddLog($"[{DateTime.Now:HH:mm:ss}] {title}：PWM=50%，配置测量项 PWIDth, NWIDth...");
                        try
                        {
                            await SendScopeCommandAsync(":MEASure:SOURce CHANnel1", token).ConfigureAwait(false);
                            await SendScopeCommandAsync(":MEASure:CLEar", token).ConfigureAwait(false);
                            await SendScopeCommandAsync(":MEASure:ITEM PWIDth", token).ConfigureAwait(false);
                            await SendScopeCommandAsync(":MEASure:ITEM NWIDth", token).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            AddLog($"[{DateTime.Now:HH:mm:ss}] {title}：配置测量项异常：{ex.Message}");
                        }

                        // Delay 8s + 3s for waveform stabilization and data accumulation
                        AddLog($"[{DateTime.Now:HH:mm:ss}] {title}：延时8秒等待波形稳定...");
                        await Task.Delay(8000, token).ConfigureAwait(false);
                        AddLog($"[{DateTime.Now:HH:mm:ss}] {title}：延时3秒等待数据累积...");
                        await Task.Delay(3000, token).ConfigureAwait(false);

                        try
                        {
                            var rawPw = await QueryScopeAsync(":MEASure:ITEM? PWIDth", 10000, token).ConfigureAwait(false);
                            var pw = ParseScopeDouble(rawPw);
                            var rawNw = await QueryScopeAsync(":MEASure:ITEM? NWIDth", 10000, token).ConfigureAwait(false);
                            var nw = ParseScopeDouble(rawNw);
                            if (pw.HasValue && nw.HasValue && (pw.Value + nw.Value) > 0)
                            {
                                duty = pw.Value / (pw.Value + nw.Value) * 100.0;
                            }
                        }
                        catch (Exception ex)
                        {
                            AddLog($"[{DateTime.Now:HH:mm:ss}] {title}：查询占空比异常：{ex.Message}");
                        }

                        AddLog($"[{DateTime.Now:HH:mm:ss}] {title}：测量完成 DUTY={FormatNum(duty)}%");
                    }
                    else
                    {
                        // Custom PWM: measure FREQuency + PWIDth + NWIDth (referencing A_C_6_16_1_1_1ViewModel)
                        AddLog($"[{DateTime.Now:HH:mm:ss}] {title}：PWM={expectedDutyPct}%，配置测量项 (FREQuency, PWIDth, NWIDth)...");
                        try
                        {
                            await SendScopeCommandAsync(":MEASure:SOURce CHANnel1", token).ConfigureAwait(false);
                            await SendScopeCommandAsync(":MEASure:CLEar", token).ConfigureAwait(false);
                            await SendScopeCommandAsync(":MEASure:ITEM FREQuency", token).ConfigureAwait(false);
                            await SendScopeCommandAsync(":MEASure:ITEM PWIDth", token).ConfigureAwait(false);
                            await SendScopeCommandAsync(":MEASure:ITEM NWIDth", token).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            AddLog($"[{DateTime.Now:HH:mm:ss}] {title}：配置测量项异常：{ex.Message}");
                        }

                        // Delay 5s for waveform stabilization
                        AddLog($"[{DateTime.Now:HH:mm:ss}] {title}：延时5秒等待波形稳定...");
                        await Task.Delay(5000, token).ConfigureAwait(false);

                        // Query frequency
                        try
                        {
                            await SendScopeCommandAsync(":MEASure:SOURce CHANnel1", token).ConfigureAwait(false);
                            var rawFreq = await QueryScopeAsync(":MEASure:ITEM? FREQuency", 10000, token).ConfigureAwait(false);
                            freq = ParseScopeDouble(rawFreq);
                        }
                        catch (Exception ex)
                        {
                            AddLog($"[{DateTime.Now:HH:mm:ss}] {title}：查询频率异常：{ex.Message}");
                        }

                        // Query duty cycle — calculate from PWIDth + NWIDth
                        try
                        {
                            var rawPw = await QueryScopeAsync(":MEASure:ITEM? PWIDth", 10000, token).ConfigureAwait(false);
                            var pw = ParseScopeDouble(rawPw);
                            var rawNw = await QueryScopeAsync(":MEASure:ITEM? NWIDth", 10000, token).ConfigureAwait(false);
                            var nw = ParseScopeDouble(rawNw);
                            if (pw.HasValue && nw.HasValue && (pw.Value + nw.Value) > 0)
                            {
                                duty = pw.Value / (pw.Value + nw.Value) * 100.0;
                            }
                        }
                        catch (Exception ex)
                        {
                            AddLog($"[{DateTime.Now:HH:mm:ss}] {title}：查询占空比异常：{ex.Message}");
                        }

                        AddLog($"[{DateTime.Now:HH:mm:ss}] {title}：测量完成 FREQ={FormatNum(freq)}Hz, DUTY={FormatNum(duty)}%");
                    }

                    return new MeasurementResult
                    {
                        Title = title,
                        Vrms = vrms,
                        FreqHz = freq,
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
                            _ = await matrix.DisconnectNodesAsync(Slot6Row, slot6Output, Chassis2Slot6, MatrixIpAddress, MatrixTcpBasePort).ConfigureAwait(false);
                    }
                    catch { }
                }
            }
            finally
            {
                _instrumentLock.Release();
            }
        }

        private async Task<(bool Pass, string FailPoint)> MeasureAllPointsAsync(int expectedDutyPct, CancellationToken token)
        {
            var mV1 = await MeasureOnceWithMatrixAsync(NormalizeSlot6Output(DcmV1ToScope), "DCM_V1对地", expectedDutyPct, token).ConfigureAwait(false);
            var mV2 = await MeasureOnceWithMatrixAsync(NormalizeSlot6Output(DcmV2ToScope), "DCM_V2对地", expectedDutyPct, token).ConfigureAwait(false);
            var mV11 = await MeasureOnceWithMatrixAsync(NormalizeSlot6Output(DcmV1V1ToScope), "DCM_V1对DCM_V2", expectedDutyPct, token).ConfigureAwait(false);

            Application.Current.Dispatcher.Invoke(() =>
            {
                if (mV1 != null)
                {
                    J8VrmsText = FormatNum(mV1.Vrms);
                    J8FreqHzText = FormatNum(mV1.FreqHz);
                    J8VmaxText = FormatNum(mV1.Vmax);
                    J8VminText = FormatNum(mV1.Vmin);
                    J8VppText = FormatNum(mV1.Vpp);
                    J8DutyPctText = FormatNum(mV1.DutyPct);
                }
                if (mV2 != null)
                {
                    J9VrmsText = FormatNum(mV2.Vrms);
                    J9FreqHzText = FormatNum(mV2.FreqHz);
                    J9VmaxText = FormatNum(mV2.Vmax);
                    J9VminText = FormatNum(mV2.Vmin);
                    J9VppText = FormatNum(mV2.Vpp);
                    J9DutyPctText = FormatNum(mV2.DutyPct);
                }
                if (mV11 != null)
                {
                    J8J9VrmsText = FormatNum(mV11.Vrms);
                    J8J9FreqHzText = FormatNum(mV11.FreqHz);
                    J8J9VmaxText = FormatNum(mV11.Vmax);
                    J8J9VminText = FormatNum(mV11.Vmin);
                    J8J9VppText = FormatNum(mV11.Vpp);
                    J8J9DutyPctText = FormatNum(mV11.DutyPct);
                }
            });

            var okV1 = IsMeasurementPass(mV1, expectedDutyPct, PwmFrequencyHz, out var rV1);
            var okV2 = IsMeasurementPass(mV2, expectedDutyPct, PwmFrequencyHz, out var rV2);
            var okV11 = IsMeasurementPass(mV11, expectedDutyPct, PwmFrequencyHz, out var rV11);

            if (okV1)
                AddLog($"{mV1?.Title ?? "DCM_V1对地"} 判据PASS");
            else
                AddLog($"{mV1?.Title ?? "DCM_V1对地"} 判据FAIL: {rV1}");
            if (okV2)
                AddLog($"{mV2?.Title ?? "DCM_V2对地"} 判据PASS");
            else
                AddLog($"{mV2?.Title ?? "DCM_V2对地"} 判据FAIL: {rV2}");
            if (okV11)
                AddLog($"{mV11?.Title ?? "DCM_V1对DCM_V2"} 判据PASS");
            else
                AddLog($"{mV11?.Title ?? "DCM_V1对DCM_V2"} 判据FAIL: {rV11}");

            if (okV1 && okV2 && okV11)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] PWM={expectedDutyPct}%：全部测量点PASS");
                return (true, string.Empty);
            }

            if (!okV1) return (false, mV1?.Title ?? "DCM_V1对地");
            if (!okV2) return (false, mV2?.Title ?? "DCM_V2对地");
            return (false, mV11?.Title ?? "DCM_V1对DCM_V2");
        }

        private static string FormatNum(double? v)
        {
            if (!v.HasValue || double.IsNaN(v.Value) || double.IsInfinity(v.Value))
                return "--";
            return v.Value.ToString("G6", CultureInfo.InvariantCulture);
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

            await SendScopeCommandAsync(":MEASure:SOURce CHANnel1", token).ConfigureAwait(false);
            await SendScopeCommandAsync(":MEASure:CLEar", token).ConfigureAwait(false);
        }

        /// <summary>
        /// Synchronous scope write — caller must hold _scopeIoLock.
        /// Matches OscilloscopeTestPanelViewModel.WriteOscilloscopeUnsafe pattern.
        /// </summary>
        private void WriteScopeUnsafe(string command)
        {
            var stream = _scopeTcpStream;
            if (stream == null) return;
            var cmd = command.EndsWith("\n", StringComparison.Ordinal) ? command : command + "\n";
            var bytes = Encoding.ASCII.GetBytes(cmd);
            stream.Write(bytes, 0, bytes.Length);
        }

        /// <summary>
        /// Async send-only scope command with independent lock acquisition.
        /// Matches OscilloscopeTestPanelViewModel.SendOscilloscopeCommandAsync pattern.
        /// </summary>
        private async Task SendScopeCommandAsync(string command, CancellationToken token)
        {
            if (_scopeTcpStream == null && _scopeTcpClient == null)
                return;

            await _scopeIoLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                await Task.Run(() =>
                {
                    WriteScopeUnsafe(command);
                }, token).ConfigureAwait(false);
            }
            finally
            {
                _scopeIoLock.Release();
            }
        }

        /// <summary>
        /// Async scope query with independent lock acquisition.
        /// Matches OscilloscopeTestPanelViewModel.QueryOscilloscopeAsync pattern:
        /// acquire lock → Task.Run(sync write + sync ReadLine) → release lock.
        /// </summary>
        private async Task<string> QueryScopeAsync(string command, int timeoutMs, CancellationToken token)
        {
            if (_scopeTcpStream == null && _scopeTcpClient == null)
                return null;

            await _scopeIoLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                return await Task.Run(() =>
                {
                    var stream = _scopeTcpStream;
                    if (stream == null)
                        return null;

                    // Synchronous write inside lock
                    var cmd = command.EndsWith("\n", StringComparison.Ordinal) ? command : command + "\n";
                    var bytes = Encoding.ASCII.GetBytes(cmd);
                    stream.Write(bytes, 0, bytes.Length);

                    // Synchronous read with timeout
                    try
                    {
                        return ReadLineAsync(stream, timeoutMs, token).GetAwaiter().GetResult();
                    }
                    catch (TimeoutException)
                    {
                        return null;
                    }
                }, token).ConfigureAwait(false);
            }
            finally
            {
                _scopeIoLock.Release();
            }
        }

        /// <summary>
        /// Parse a scope response string into a double, matching OscilloscopeTestPanelViewModel.NormalizeOscilloscopeNumber pattern.
        /// </summary>
        private static double? ParseScopeDouble(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            raw = raw.Trim();
            int comma = raw.IndexOf(',');
            if (comma > 0)
                raw = raw.Substring(0, comma);

            var match = Regex.Match(raw, @"[-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?");
            if (!match.Success)
                return null;

            var clean = match.Value;
            if (double.TryParse(clean, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            {
                if (double.IsNaN(v) || double.IsInfinity(v) || Math.Abs(v) > 1e36)
                    return null;
                return v;
            }
            return null;
        }

        private static async Task<string> ReadLineAsync(NetworkStream stream, int timeoutMs, CancellationToken token)
        {
            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                cts.CancelAfter(timeoutMs);
                var sb = new StringBuilder();
                var buf = new byte[1];
                while (true)
                {
                    int n;
                    try
                    {
                        n = await stream.ReadAsync(buf, 0, 1, cts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        if (token.IsCancellationRequested)
                            throw;
                        throw new TimeoutException($"示波器读取超时({timeoutMs}ms)");
                    }
                    if (n <= 0)
                        break;
                    char ch = (char)buf[0];
                    if (ch == '\n')
                        break;
                    if (ch != '\r')
                        sb.Append(ch);
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
                0x0A,0x05,
                0x01,
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

            try { _manualMeasureCts?.Cancel(); } catch { }
            try { _manualMeasureCts?.Dispose(); } catch { }
            _manualMeasureCts = null;

            try { _manualTestLock?.Dispose(); } catch { }
            try { _autoTestLock?.Dispose(); } catch { }
            try { _opLock?.Dispose(); } catch { }
            try { _instrumentLock?.Dispose(); } catch { }
            try { _scopeIoLock?.Dispose(); } catch { }
        }

        private sealed class MeasurementResult
        {
            public string Title { get; set; }
            public double? Vrms { get; set; }
            public double? FreqHz { get; set; }
            public double? Vmax { get; set; }
            public double? Vmin { get; set; }
            public double? Vpp { get; set; }
            public double? DutyPct { get; set; }
        }
    }
}
