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
using MeasureControl.Models.Devices;
using MeasureControl.Models.Devices.DeviceCategories;
using MeasureControl.Services;
using MeasureControl.Services.HardwareApis;
using Prism.Ioc;
using System.Linq;

namespace MeasureControl.ViewModels.SingleBoardTest.AirController
{
    public sealed class S_C_8_9_1ViewModel : BindableBase, IDisposable
    {
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

        // 万用表（测量 DCM_V1 对 DCM_V2 之间的直流电压）
        private const string DmmIpAddress = "192.168.1.13";
        private const int DmmTimeoutMs = 8000;
        // 万用表接入矩阵节点 ("I4", "O2", 4)；DCM_V1对DCM_V2 信号节点 ("I1", "O16", 6)
        private const string DmmMatrixRow = "I4";
        private const string DmmMatrixOutput = "O2";

        private const string PowerSupply28VIpAddress = "192.168.1.15";

        private const PowerSupplyChannel PowerSupply28VCh1 = PowerSupplyChannel.CH1;
        private const double Power28VVoltage = 28.0;
        private const double Power28VCurrentLimit = 3.0;

        // 7131 DO通道：DO10和DO11用于PH高低电平控制
        private const string Do10Channel = "DO10";
        private const string Do11Channel = "DO11";

        private readonly SemaphoreSlim _manualTestLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _autoTestLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _opLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _instrumentLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _scopeIoLock = new SemaphoreSlim(1, 1);

        private CancellationTokenSource _manualMeasureCts;
        private CancellationTokenSource _autoTestCts;

        private readonly IPxiChassisService _pxiChassisService;
        private IJy7131Api _jy7131Api;

        private IDmmApi _dmmSocket;

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

        private string _scopeIpAddress = DefaultScopeIpAddress;
        private int _scopePort = DefaultScopePort;

        private string _matrixIpAddress = DefaultMatrixIpAddress;
        private int _matrixTcpBasePort = DefaultMatrixTcpBasePort;

        public S_C_8_9_1ViewModel()
        {
            _pxiChassisService = ContainerLocator.Container?.Resolve<IPxiChassisService>();

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
                    await ResetDoChannelsAsync(CancellationToken.None).ConfigureAwait(false);
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

            // 初始化7131板卡（用于DO10/DO11控制PH电平）
            await EnsureJy7131ReadyAsync(token).ConfigureAwait(false);

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

            // 关闭DO10/DO11并清理7131板卡
            try
            {
                await ResetDoChannelsAsync(token).ConfigureAwait(false);
                await Cleanup7131Async().ConfigureAwait(false);
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
                    await ResetDoChannelsAsync(CancellationToken.None).ConfigureAwait(false);
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
                    try { await ResetDoChannelsAsync(CancellationToken.None).ConfigureAwait(false); } catch { }

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

                J8VrmsText = "--";
                J8FreqHzText = "--";
                J8VmaxText = "--";
                J8VminText = "--";
                J8VppText = "--";
                J8DutyPctText = "--";

                J9VrmsText = "--";
                J9FreqHzText = "--";
                J9VmaxText = "--";
                J9VminText = "--";
                J9VppText = "--";
                J9DutyPctText = "--";

                J8J9VrmsText = "--";
                J8J9FreqHzText = "--";
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
                await SetPhLevelAsync(PwmDutyPct, CancellationToken.None).ConfigureAwait(false);
                AddLog($"PWM={PwmDutyPct}%：已设置PH电平，请点击“测量”查看波形");
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
                await SetPhLevelAsync(dutyPct, CancellationToken.None).ConfigureAwait(false);
                AddLog($"PWM={dutyPct}%：已设置PH电平，请点击“测量”查看波形");
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
                    await SetPhLevelAsync(dutyPct, token).ConfigureAwait(false);
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

            // 0% PWM: VRMS should be near 0V
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

            // 100% PWM: VRMS should be in [17,32]V
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

            // 50% PWM: duty cycle within ±1%
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

        private async Task SetPhLevelAsync(int dutyPct, CancellationToken token)
        {
            dutyPct = ClampDuty(dutyPct);
            await EnsureJy7131ReadyAsync(token).ConfigureAwait(false);

            if (dutyPct == 0)
            {
                // PH低电平：DO10给地(false)，DO11给开(true)
                await _jy7131Api.WriteDoAsync(Do10Channel, false, token).ConfigureAwait(false);
                await _jy7131Api.WriteDoAsync(Do11Channel, true, token).ConfigureAwait(false);
                AddLog($"PH低电平：DO10=地, DO11=开 (PWM=0%)");
            }
            else
            {
                // PH高电平：DO10给开(true)，DO11给开(true)
                await _jy7131Api.WriteDoAsync(Do10Channel, true, token).ConfigureAwait(false);
                await _jy7131Api.WriteDoAsync(Do11Channel, true, token).ConfigureAwait(false);
                AddLog($"PH高电平：DO10=开, DO11=开 (PWM={dutyPct}%)");
            }
        }

        private async Task EnsureJy7131ReadyAsync(CancellationToken token)
        {
            try
            {
                if (_jy7131Api == null)
                {
                    var device7131 = FindFirstJy7131Device();
                    if (device7131 != null)
                    {
                        var devSlot = Infer7131SlotNumber(device7131);
                        AddLog($"[{DateTime.Now:HH:mm:ss}] 找到7131板卡: {device7131.Model ?? device7131.Name}，槽位={devSlot}");
                        if (int.TryParse(devSlot, out var slotNum))
                            _jy7131Api = new Jy7131Api(device7131, slotNum);
                        else
                            _jy7131Api = new Jy7131Api(device7131);
                    }
                    else
                    {
                        throw new InvalidOperationException("未找到7131板卡");
                    }
                }

                if (!_jy7131Api.IsConnected)
                {
                    await _jy7131Api.ConnectAsync(token).ConfigureAwait(false);
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 7131板卡连接成功");
                }

                if (!_jy7131Api.IsRunning)
                {
                    await _jy7131Api.SetOutputModeAsync(Jy7131OutputMode.Sinking, token).ConfigureAwait(false);
                    await _jy7131Api.StartAsync(token).ConfigureAwait(false);
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 7131板卡已启动（Sinking模式）");
                }
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 7131板卡初始化失败: {ex.Message}");
                throw;
            }
        }

        private async Task ResetDoChannelsAsync(CancellationToken token)
        {
            if (_jy7131Api != null && _jy7131Api.IsConnected)
            {
                try
                {
                    await _jy7131Api.WriteDoAsync(Do10Channel, false, token).ConfigureAwait(false);
                    await _jy7131Api.WriteDoAsync(Do11Channel, false, token).ConfigureAwait(false);
                    AddLog($"[{DateTime.Now:HH:mm:ss}] DO10/DO11已关闭");
                }
                catch (Exception ex)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 关闭DO10/DO11失败: {ex.Message}");
                }
            }
        }

        private async Task Cleanup7131Async()
        {
            try
            {
                if (_jy7131Api != null && _jy7131Api.IsConnected)
                {
                    if (_jy7131Api.IsRunning)
                    {
                        await _jy7131Api.StopAsync(CancellationToken.None).ConfigureAwait(false);
                        AddLog($"[{DateTime.Now:HH:mm:ss}] 7131板卡已停止");
                    }
                    await _jy7131Api.DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 7131板卡已断开");
                }
            }
            catch { }
            finally
            {
                _jy7131Api = null;
            }
        }

        private DeviceBase FindFirstJy7131Device()
        {
            var chassisList = _pxiChassisService?.GetAllChassis();
            if (chassisList == null)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] [7131查找] 机箱列表为null");
                return null;
            }

            foreach (var chassis in chassisList)
            {
                if (chassis?.Devices == null)
                    continue;

                var device = chassis.Devices.FirstOrDefault(d =>
                    d is DigitalIODevice ||
                    (d?.Model?.IndexOf("7131", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.DeviceTypeName?.IndexOf("离散量", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.DeviceTypeName?.IndexOf("数字量", StringComparison.OrdinalIgnoreCase) >= 0));

                if (device != null)
                    return device;

                foreach (var d in chassis.Devices)
                {
                    if (d?.Children == null)
                        continue;

                    var childDevice = d.Children.FirstOrDefault(c =>
                        c is DigitalIODevice ||
                        (c?.Model?.IndexOf("7131", StringComparison.OrdinalIgnoreCase) >= 0) ||
                        (c?.DeviceTypeName?.IndexOf("离散量", StringComparison.OrdinalIgnoreCase) >= 0) ||
                        (c?.DeviceTypeName?.IndexOf("数字量", StringComparison.OrdinalIgnoreCase) >= 0));

                    if (childDevice != null)
                        return childDevice;
                }
            }

            return null;
        }

        private static string Infer7131SlotNumber(DeviceBase device)
        {
            if (device is PxiDeviceBase pxi && pxi.SlotIndex > 0)
                return pxi.SlotIndex.ToString();

            var slot = device?.SlotPosition;
            if (!string.IsNullOrWhiteSpace(slot))
            {
                var trimmed = slot.Replace("Slot", "").Replace("slot", "").Trim();
                if (int.TryParse(trimmed, out var slotNum) && slotNum > 0)
                    return slotNum.ToString();
            }

            return "12";
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
                        // 100%/0% PWM: measure VRMS + FREQuency (referencing A_C_6_16_1_1_1ViewModel)
                        AddLog($"[{DateTime.Now:HH:mm:ss}] {title}：PWM={expectedDutyPct}%，配置测量项 VRMS, FREQuency...");
                        try
                        {
                            await SendScopeCommandAsync(":MEASure:SOURce CHANnel1", token).ConfigureAwait(false);
                            await SendScopeCommandAsync(":MEASure:CLEar", token).ConfigureAwait(false);
                            await SendScopeCommandAsync(":MEASure:ITEM VRMS", token).ConfigureAwait(false);
                            await SendScopeCommandAsync(":MEASure:ITEM FREQuency", token).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            AddLog($"[{DateTime.Now:HH:mm:ss}] {title}：配置测量项异常：{ex.Message}");
                        }

                        // Delay 5s for waveform stabilization (referencing A_C_6_16_1_1_1ViewModel)
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

                        try
                        {
                            await SendScopeCommandAsync(":MEASure:SOURce CHANnel1", token).ConfigureAwait(false);
                            var rawFreq = await QueryScopeAsync(":MEASure:ITEM? FREQuency", 10000, token).ConfigureAwait(false);
                            freq = ParseScopeDouble(rawFreq);
                            if (freq.HasValue)
                                AddLog($"[{DateTime.Now:HH:mm:ss}] {title}：频率解析值：{freq.Value:F3} Hz");
                            else
                                AddLog($"[{DateTime.Now:HH:mm:ss}] {title}：频率解析失败");
                        }
                        catch (Exception ex)
                        {
                            AddLog($"[{DateTime.Now:HH:mm:ss}] {title}：查询频率异常：{ex.Message}");
                        }

                        AddLog($"[{DateTime.Now:HH:mm:ss}] {title}：测量完成 VRMS={FormatNum(vrms)}V, FREQ={FormatNum(freq)}Hz");
                    }
                    else if (expectedDutyPct == 50)
                    {
                        // 50% PWM: measure FREQuency + PWIDth + NWIDth (referencing A_C_6_16_1_1_1ViewModel)
                        AddLog($"[{DateTime.Now:HH:mm:ss}] {title}：PWM=50%，配置测量项 FREQuency, PWIDth, NWIDth...");
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

                        // Delay 5s for waveform stabilization (referencing A_C_6_16_1_1_1ViewModel)
                        AddLog($"[{DateTime.Now:HH:mm:ss}] {title}：延时5秒等待波形稳定...");
                        await Task.Delay(5000, token).ConfigureAwait(false);

                        // Query frequency
                        try
                        {
                            await SendScopeCommandAsync(":MEASure:SOURce CHANnel1", token).ConfigureAwait(false);
                            var rawFreq = await QueryScopeAsync(":MEASure:ITEM? FREQuency", 10000, token).ConfigureAwait(false);
                            freq = ParseScopeDouble(rawFreq);
                            if (freq.HasValue)
                                AddLog($"[{DateTime.Now:HH:mm:ss}] {title}：频率解析值：{freq.Value:F3} Hz");
                            else
                                AddLog($"[{DateTime.Now:HH:mm:ss}] {title}：频率解析失败");
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
                                AddLog($"[{DateTime.Now:HH:mm:ss}] {title}：占空比计算值：PWIDth={pw.Value:F6}s, NWIDth={nw.Value:F6}s, DUTY={duty.Value:F3}%");
                            }
                        }
                        catch (Exception ex)
                        {
                            AddLog($"[{DateTime.Now:HH:mm:ss}] {title}：查询占空比异常：{ex.Message}");
                        }

                        AddLog($"[{DateTime.Now:HH:mm:ss}] {title}：测量完成 FREQ={FormatNum(freq)}Hz, DUTY={FormatNum(duty)}%");
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
            // 100% / 0% PWM：用万用表测量 DCM_V1 对 DCM_V2 之间的直流电压（不再测量三处波形）
            if (expectedDutyPct == 100 || expectedDutyPct == 0)
            {
                var (vok, voltage) = await MeasureV1V2VoltageWithMatrixAsync(token).ConfigureAwait(false);

                Application.Current.Dispatcher.Invoke(() =>
                {
                    J8VrmsText = "--"; J8FreqHzText = "--"; J8VmaxText = "--"; J8VminText = "--"; J8VppText = "--"; J8DutyPctText = "--";
                    J9VrmsText = "--"; J9FreqHzText = "--"; J9VmaxText = "--"; J9VminText = "--"; J9VppText = "--"; J9DutyPctText = "--";
                    J8J9VrmsText = FormatNum(voltage);
                    J8J9FreqHzText = "--"; J8J9VmaxText = "--"; J8J9VminText = "--"; J8J9VppText = "--"; J8J9DutyPctText = "--";
                });

                var rVolt = string.Empty;
                var voltageOk = vok && IsVoltagePass(voltage, expectedDutyPct, out rVolt);
                if (voltageOk)
                {
                    AddLog($"DCM_V1对DCM_V2(万用表电压) 判据PASS：{FormatNum(voltage)}V");
                    AddLog($"[{DateTime.Now:HH:mm:ss}] PWM={expectedDutyPct}%：电压测量PASS");
                    return (true, string.Empty);
                }

                AddLog($"DCM_V1对DCM_V2(万用表电压) 判据FAIL: {(vok ? rVolt : "万用表测量失败")}");
                return (false, "DCM_V1对DCM_V2电压");
            }

            // 其他占空比（含50%）：只测量 DCM_V2 对地波形，判断占空比
            var mV2 = await MeasureOnceWithMatrixAsync(NormalizeSlot6Output(DcmV2ToScope), "DCM_V2对地", expectedDutyPct, token).ConfigureAwait(false);

            Application.Current.Dispatcher.Invoke(() =>
            {
                J8VrmsText = "--"; J8FreqHzText = "--"; J8VmaxText = "--"; J8VminText = "--"; J8VppText = "--"; J8DutyPctText = "--";
                J8J9VrmsText = "--"; J8J9FreqHzText = "--"; J8J9VmaxText = "--"; J8J9VminText = "--"; J8J9VppText = "--"; J8J9DutyPctText = "--";
                if (mV2 != null)
                {
                    J9VrmsText = FormatNum(mV2.Vrms);
                    J9FreqHzText = FormatNum(mV2.FreqHz);
                    J9VmaxText = FormatNum(mV2.Vmax);
                    J9VminText = FormatNum(mV2.Vmin);
                    J9VppText = FormatNum(mV2.Vpp);
                    J9DutyPctText = FormatNum(mV2.DutyPct);
                }
            });

            var okV2 = IsMeasurementPass(mV2, expectedDutyPct, PwmFrequencyHz, out var rV2);
            if (okV2)
            {
                AddLog($"{mV2?.Title ?? "DCM_V2对地"} 判据PASS");
                AddLog($"[{DateTime.Now:HH:mm:ss}] PWM={expectedDutyPct}%：DCM_V2对地占空比测量PASS");
                return (true, string.Empty);
            }

            AddLog($"{mV2?.Title ?? "DCM_V2对地"} 判据FAIL: {rV2}");
            return (false, mV2?.Title ?? "DCM_V2对地");
        }

        /// <summary>
        /// 100%/0% PWM：路由矩阵节点 ("I4","O2",4) + ("I1","O16",6)，用万用表测量 DCM_V1 对 DCM_V2 直流电压。
        /// </summary>
        private async Task<(bool Ok, double? Voltage)> MeasureV1V2VoltageWithMatrixAsync(CancellationToken token)
        {
            await _instrumentLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                var matrix = MatrixControlService.Instance;
                bool okDmm = false;
                bool okSig = false;
                try
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 路由DCM_V1对DCM_V2(万用表)：slot{Chassis2Slot4} {DmmMatrixRow}-{DmmMatrixOutput} + slot{Chassis2Slot6} {Slot6Row}-{DcmV1V1ToScope}");

                    okDmm = await matrix.ConnectNodesAsync(DmmMatrixRow, DmmMatrixOutput, Chassis2Slot4, MatrixIpAddress, MatrixTcpBasePort).ConfigureAwait(false);
                    okSig = await matrix.ConnectNodesAsync(Slot6Row, DcmV1V1ToScope, Chassis2Slot6, MatrixIpAddress, MatrixTcpBasePort).ConfigureAwait(false);

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 路由结果：DMM={(okDmm ? "OK" : "FAIL")}, SIG={(okSig ? "OK" : "FAIL")}");
                    if (!okDmm || !okSig)
                        return (false, null);

                    // 等待继电器与信号稳定
                    await Task.Delay(200, token).ConfigureAwait(false);

                    var reading = await DmmReadVoltageAsync(token).ConfigureAwait(false);
                    if (reading?.Value == null)
                    {
                        AddLog($"[{DateTime.Now:HH:mm:ss}] DCM_V1对DCM_V2：万用表读数无效");
                        return (false, null);
                    }

                    AddLog($"[{DateTime.Now:HH:mm:ss}] DCM_V1对DCM_V2：万用表直流电压={reading.Value.Value:F4}V");
                    return (true, reading.Value.Value);
                }
                catch (Exception ex)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] DCM_V1对DCM_V2：电压测量异常：{ex.Message}");
                    return (false, null);
                }
                finally
                {
                    try
                    {
                        if (okSig)
                            _ = await matrix.DisconnectNodesAsync(Slot6Row, DcmV1V1ToScope, Chassis2Slot6, MatrixIpAddress, MatrixTcpBasePort).ConfigureAwait(false);
                    }
                    catch { }

                    try
                    {
                        if (okDmm)
                            _ = await matrix.DisconnectNodesAsync(DmmMatrixRow, DmmMatrixOutput, Chassis2Slot4, MatrixIpAddress, MatrixTcpBasePort).ConfigureAwait(false);
                    }
                    catch { }
                }
            }
            finally
            {
                _instrumentLock.Release();
            }
        }

        private async Task<DmmReading> DmmReadVoltageAsync(CancellationToken token)
        {
            if (_dmmSocket == null)
                _dmmSocket = new DmmSocketApi();

            if (!_dmmSocket.IsConnected)
                await _dmmSocket.ConnectAsync(DmmIpAddress, token).ConfigureAwait(false);

            return await _dmmSocket.ReadOnceAsync(DmmMeasureMode.DCV, new DmmReadOptions { TimeoutMilliseconds = DmmTimeoutMs }, token).ConfigureAwait(false);
        }

        /// <summary>
        /// 万用表电压判据：0% 时电压应接近 0V（|V|≤1）；100% 时电压幅值应在 [17,32]V。
        /// </summary>
        private static bool IsVoltagePass(double? voltage, int expectedDutyPct, out string reason)
        {
            if (!voltage.HasValue)
            {
                reason = "万用表电压无有效值";
                return false;
            }

            var v = voltage.Value;
            var absV = Math.Abs(v);

            if (expectedDutyPct == 0)
            {
                if (absV > 1.0)
                {
                    reason = $"电压不在[-1,1]V：{v:F4}V";
                    return false;
                }

                reason = null;
                return true;
            }

            // 100% PWM
            if (absV < 17.0 || absV > 32.0)
            {
                reason = $"电压幅值不在[17,32]V：{absV:F4}V";
                return false;
            }

            reason = null;
            return true;
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

        public void Dispose()
        {
            try { _autoTestCts?.Cancel(); } catch { }
            try { _autoTestCts?.Dispose(); } catch { }
            _autoTestCts = null;

            try { ResetDoChannelsAsync(CancellationToken.None).GetAwaiter().GetResult(); } catch { }
            try { Cleanup7131Async().GetAwaiter().GetResult(); } catch { }

            try { UnrouteMatrixAsync(CancellationToken.None).GetAwaiter().GetResult(); } catch { }

            try { if (_dmmSocket != null && _dmmSocket.IsConnected) _dmmSocket.DisconnectAsync(CancellationToken.None).GetAwaiter().GetResult(); } catch { }
            try { _dmmSocket?.DisposeAsync().AsTask().Wait(1000); } catch { }
            _dmmSocket = null;

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