using Prism.Commands;
using Prism.Mvvm;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using MeasureControl.Services;
using MeasureControl.Services.HardwareApis;

namespace MeasureControl.ViewModels.SingleBoardTest.AirController
{
    public sealed class S_C_8_10_1ViewModel : BindableBase, IDisposable
    {
        private const string DefaultFpgaIpAddress = "192.168.1.10";
        private const int DefaultFpgaPort = 5001;

        private const string DefaultMatrixIpAddress = "192.168.1.3";
        private const int DefaultMatrixTcpBasePort = 50200;

        private const int Chassis2Slot6 = 6;
        private const int Chassis2Slot4 = 4;

        private const string Slot6Row = "I1";
        private const string DefaultSlot6OutputToScope = "O0";
        private const string DcmV1ToScope = "O15";
        private const string DcmV2ToScope = "O14";
        private const string DcmV1V1ToScope = "O16";

        private const string Slot4Row = "I0";
        private const string Slot4ToScope = "O1";

        private const int DefaultScopePort = 5555;
        private const string DefaultScopeIpAddress = "192.168.1.18";

        // 万用表（测量 DCM_V1 对 DCM_V2 之间的直流电压）
        private const string DmmIpAddress = "192.168.1.13";
        private const int DmmTimeoutMs = 8000;
        // 万用表接入矩阵节点 ("I4", "O2", 4)；DCM_V1对DCM_V2 信号节点 ("I1", "O16", 6)
        private const string DmmMatrixRow = "I4";
        private const string DmmMatrixOutput = "O2";

        private static readonly byte[] DeviceInitCommandFrame = { 0xAA, 0x55, 0x02, 0x02, 0x01 };
        private static readonly byte[] ResetToInitialCommandFrame = { 0xAA, 0x55, 0x0A, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };

        private const int FixedGearFrequencyHz = 2000;
        private const int AutoSimulatedClickIntervalMs = 3000;
        private const int AutoSendToMeasureDelayMs = 10000;

        private readonly SemaphoreSlim _manualTestLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _autoTestLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _opLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _instrumentLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _scopeIoLock = new SemaphoreSlim(1, 1);

        private CancellationTokenSource _manualMeasureCts;
        private CancellationTokenSource _autoTestCts;

        private TcpClient _fpgaClient;
        private NetworkStream _fpgaStream;
        private readonly SemaphoreSlim _fpgaSendLock = new SemaphoreSlim(1, 1);
        private bool _fpgaInitialized;

        private TcpClient _scopeTcpClient;
        private NetworkStream _scopeTcpStream;

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

        private bool? _manualPwm100Pass;
        private bool? _manualPwm50Pass;
        private bool? _manualPwm0Pass;
        private string _manualPwm100FailPoint;
        private string _manualPwm50FailPoint;
        private string _manualPwm0FailPoint;

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

        public S_C_8_10_1ViewModel()
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

        public bool IsConfigSendMeasureEnabled => !IsBusy && !IsManualTestRunning && !IsAutoTestRunning;

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
                    IsManualTestRunning = true;
                    AddLog($"[{DateTime.Now:HH:mm:ss}] ========== 手动测试开始 ==========");
                    await EnsureFpgaConnectedAsync(CancellationToken.None).ConfigureAwait(false);
                }
                finally
                {
                    IsBusy = false;
                }
            }
            catch (Exception ex)
            {
                IsManualTestRunning = false;
                AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试启动异常: {ex.Message}");
            }
            finally
            {
                _manualTestLock.Release();
            }
        }

        private async Task StopManualTestAsync()
        {
            try { _manualMeasureCts?.Cancel(); } catch { }
            try { _manualMeasureCts?.Dispose(); } catch { }
            _manualMeasureCts = null;

            await _manualTestLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!IsManualTestRunning)
                    return;

                IsBusy = true;
                try
                {
                    IsManualTestRunning = false;
                    await DisconnectFpgaAsync(CancellationToken.None).ConfigureAwait(false);
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
                    AddLog($"[{DateTime.Now:HH:mm:ss}] ========== 自动测试开始 ==========");
                    IsBusy = true;
                    try
                    {
                        await EnsureFpgaConnectedAsync(token).ConfigureAwait(false);
                    }
                    finally
                    {
                        IsBusy = false;
                    }

                    await SimulateManualClickDelayAsync("自动测试：模拟点击“开始手动测试”后的等待", token).ConfigureAwait(false);

                    var r100 = await RunAutoFixedGearLikeManualAsync(100, token).ConfigureAwait(false);
                    var r50 = await RunAutoFixedGearLikeManualAsync(50, token).ConfigureAwait(false);
                    var r0 = await RunAutoFixedGearLikeManualAsync(0, token).ConfigureAwait(false);

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

                _manualPwm100Pass = null;
                _manualPwm50Pass = null;
                _manualPwm0Pass = null;
                _manualPwm100FailPoint = null;
                _manualPwm50FailPoint = null;
                _manualPwm0FailPoint = null;

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
                var (pass, failPoint, _) = await MeasureAllPointsAsync(PwmDutyPct, PwmFrequencyHz, CancellationToken.None).ConfigureAwait(false);
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
                await SendFixedGearCoreAsync(dutyPct, CancellationToken.None).ConfigureAwait(false);
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

            try { _manualMeasureCts?.Cancel(); } catch { }
            try { _manualMeasureCts?.Dispose(); } catch { }
            _manualMeasureCts = new CancellationTokenSource();

            SetMeasuring(dutyPct, true);
            try
            {
                var result = await MeasureFixedGearCoreAsync(dutyPct, _manualMeasureCts.Token).ConfigureAwait(false);
                RecordManualFixedResult(dutyPct, result.Pass, result.FailPoint);
            }
            catch (OperationCanceledException)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] PWM={dutyPct}%：测量已取消");
            }
            finally
            {
                SetMeasuring(dutyPct, false);
            }
        }

        private void RecordManualFixedResult(int dutyPct, bool pass, string failPoint)
        {
            switch (dutyPct)
            {
                case 100:
                    _manualPwm100Pass = pass;
                    _manualPwm100FailPoint = failPoint;
                    break;
                case 50:
                    _manualPwm50Pass = pass;
                    _manualPwm50FailPoint = failPoint;
                    break;
                case 0:
                    _manualPwm0Pass = pass;
                    _manualPwm0FailPoint = failPoint;
                    break;
            }

            TrySetManualLastTestResult();
        }

        private void TrySetManualLastTestResult()
        {
            if (!_manualPwm100Pass.HasValue || !_manualPwm50Pass.HasValue || !_manualPwm0Pass.HasValue)
                return;

            var ok = _manualPwm100Pass.Value && _manualPwm50Pass.Value && _manualPwm0Pass.Value;
            var failPoint = !_manualPwm100Pass.Value ? _manualPwm100FailPoint : (!_manualPwm50Pass.Value ? _manualPwm50FailPoint : (!_manualPwm0Pass.Value ? _manualPwm0FailPoint : string.Empty));

            SetLastTestResult(ok ? "PASS" : (string.IsNullOrWhiteSpace(failPoint) ? "FAIL" : $"FAIL({failPoint})"));
            AddLog($"[{DateTime.Now:HH:mm:ss}] ========== 手动测试三项完成: {(ok ? "PASS" : "FAIL")} ==========");
        }

        private async Task SendFixedGearCoreAsync(int dutyPct, CancellationToken token)
        {
            await SendPwmFrameAsync(dutyPct, FixedGearFrequencyHz, sendInit: dutyPct == 100, token).ConfigureAwait(false);
            AddLog($"PWM={dutyPct}%：已发送到 FPGA，请点击“测量”查看波形");
        }

        private async Task<(bool Pass, string FailPoint)> MeasureFixedGearCoreAsync(int dutyPct, CancellationToken token)
        {
            var (pass, failPoint, displayValue) = await MeasureAllPointsAsync(dutyPct, FixedGearFrequencyHz, token).ConfigureAwait(false);
            SetFixedResult(dutyPct, BuildFixedResultText(displayValue, pass, failPoint));
            return (pass, failPoint);
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

                    try { await DisconnectScopeAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
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
                        AddLog($"[{DateTime.Now:HH:mm:ss}] {title}：PWM=50%，改用波形采样点计算频率/占空比...");
                        AddLog($"[{DateTime.Now:HH:mm:ss}] {title}：延时5秒等待波形稳定后读取 CH1 波形...");
                        await Task.Delay(5000, token).ConfigureAwait(false);

                        try
                        {
                            var waveform = await MeasurePwmFromWaveformWithRetryAsync(title, token).ConfigureAwait(false);
                            if (waveform != null)
                            {
                                vrms = waveform.Vrms;
                                freq = waveform.FreqHz;
                                vmax = waveform.Vmax;
                                vmin = waveform.Vmin;
                                vpp = waveform.Vpp;
                                duty = waveform.DutyPct;

                                AddLog($"[{DateTime.Now:HH:mm:ss}] {title}：波形计算结果 FREQ={FormatNum(freq)}Hz, DUTY={FormatNum(duty)}%, VMIN={FormatNum(vmin)}V, VMAX={FormatNum(vmax)}V");
                            }
                            else
                            {
                                AddLog($"[{DateTime.Now:HH:mm:ss}] {title}：波形采样点计算失败");
                            }
                        }
                        catch (Exception ex)
                        {
                            AddLog($"[{DateTime.Now:HH:mm:ss}] {title}：读取波形采样点异常：{ex.Message}");
                        }

                        AddLog($"[{DateTime.Now:HH:mm:ss}] {title}：测量完成 FREQ={FormatNum(freq)}Hz, DUTY={FormatNum(duty)}%");
                    }
                    else
                    {
                        AddLog($"[{DateTime.Now:HH:mm:ss}] {title}：PWM={expectedDutyPct}%，改用波形采样点计算频率/占空比...");
                        AddLog($"[{DateTime.Now:HH:mm:ss}] {title}：延时5秒等待波形稳定后读取 CH1 波形...");
                        await Task.Delay(5000, token).ConfigureAwait(false);

                        try
                        {
                            var waveform = await MeasurePwmFromWaveformAsync(title, token).ConfigureAwait(false);
                            if (waveform != null)
                            {
                                vrms = waveform.Vrms;
                                freq = waveform.FreqHz;
                                vmax = waveform.Vmax;
                                vmin = waveform.Vmin;
                                vpp = waveform.Vpp;
                                duty = waveform.DutyPct;

                                AddLog($"[{DateTime.Now:HH:mm:ss}] {title}：波形计算结果 FREQ={FormatNum(freq)}Hz, DUTY={FormatNum(duty)}%, VMIN={FormatNum(vmin)}V, VMAX={FormatNum(vmax)}V");
                            }
                            else
                            {
                                AddLog($"[{DateTime.Now:HH:mm:ss}] {title}：波形采样点计算失败");
                            }
                        }
                        catch (Exception ex)
                        {
                            AddLog($"[{DateTime.Now:HH:mm:ss}] {title}：读取波形采样点异常：{ex.Message}");
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
                    try { await DisconnectScopeAsync(CancellationToken.None).ConfigureAwait(false); } catch { }

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
        private async Task<(bool Pass, string FailPoint)> RunAutoFixedGearLikeManualAsync(int dutyPct, CancellationToken token)
        {
            AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试：模拟点击“发送{dutyPct}%PWM”按钮");
            await SendFixedGearCoreAsync(dutyPct, token).ConfigureAwait(false);

            AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试：模拟手动点击间隔，等待 {AutoSendToMeasureDelayMs / 1000.0:F1} 秒后测量 PWM={dutyPct}%");
            await Task.Delay(AutoSendToMeasureDelayMs, token).ConfigureAwait(false);

            AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试：模拟点击“测量{dutyPct}%PWM”按钮");
            SetMeasuring(dutyPct, true);
            (bool Pass, string FailPoint) result;
            try
            {
                result = await MeasureFixedGearCoreAsync(dutyPct, token).ConfigureAwait(false);
            }
            finally
            {
                SetMeasuring(dutyPct, false);
            }

            await SimulateManualClickDelayAsync($"自动测试：PWM={dutyPct}%测量完成后的手动点击间隔", token).ConfigureAwait(false);
            return result;
        }

        private async Task SimulateManualClickDelayAsync(string message, CancellationToken token)
        {
            AddLog($"[{DateTime.Now:HH:mm:ss}] {message}，等待 {AutoSimulatedClickIntervalMs / 1000.0:F1} 秒");
            await Task.Delay(AutoSimulatedClickIntervalMs, token).ConfigureAwait(false);
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

        private static string BuildFixedResultText(string displayValue, bool pass, string failPoint)
        {
            if (!string.IsNullOrWhiteSpace(displayValue) && displayValue != "--")
                return displayValue;

            return pass ? "--" : $"FAIL({failPoint})";
        }

        private async Task DisconnectFpgaAsync(CancellationToken token)
        {
            try
            {
                if (_fpgaClient?.Connected == true && _fpgaStream != null)
                {
                    await _fpgaSendLock.WaitAsync(token).ConfigureAwait(false);
                    try
                    {
                        AddLog($"FPGA复位: 发送 {FormatData(ResetToInitialCommandFrame)}");
                        await _fpgaStream.WriteAsync(ResetToInitialCommandFrame, 0, ResetToInitialCommandFrame.Length, token).ConfigureAwait(false);
                        await _fpgaStream.FlushAsync(token).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        AddLog($"FPGA复位失败: {ex.Message}");
                    }
                    finally
                    {
                        _fpgaSendLock.Release();
                    }
                }
            }
            catch { }

            await DisconnectFpgaTcpAsync().ConfigureAwait(false);
            AddLog("FPGA已断开释放");
        }

        private static string NormalizeSlot6Output(string slot6Output)
        {
            return string.IsNullOrWhiteSpace(slot6Output) ? DefaultSlot6OutputToScope : slot6Output;
        }

        private async Task<(bool Pass, string FailPoint, string DisplayValue)> MeasureAllPointsAsync(int expectedDutyPct, int expectedFreqHz, CancellationToken token)
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

                string rVolt = null;
                var displayValue = FormatMeasurementValue(voltage, "V");
                var voltageOk = vok && IsVoltagePass(voltage, expectedDutyPct, out rVolt);
                if (voltageOk)
                {
                    AddLog($"DCM_V1对DCM_V2(万用表电压) 判据PASS：{FormatNum(voltage)}V");
                    AddLog($"[{DateTime.Now:HH:mm:ss}] PWM={expectedDutyPct}%：电压测量PASS");
                    return (true, string.Empty, displayValue);
                }

                AddLog($"DCM_V1对DCM_V2(万用表电压) 判据FAIL: {(vok ? rVolt : "万用表测量失败")}");
                return (false, "DCM_V1对DCM_V2电压", displayValue);
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

            var dutyValue = FormatMeasurementValue(mV2?.DutyPct, "%");
            var okV2 = IsMeasurementPass(mV2, expectedDutyPct, expectedFreqHz, out var rV2);
            if (okV2)
            {
                AddLog($"{mV2?.Title ?? "DCM_V2对地"} 判据PASS");
                AddLog($"[{DateTime.Now:HH:mm:ss}] PWM={expectedDutyPct}%：DCM_V2对地占空比测量PASS");
                return (true, string.Empty, dutyValue);
            }

            AddLog($"{mV2?.Title ?? "DCM_V2对地"} 判据FAIL: {rV2}");
            return (false, mV2?.Title ?? "DCM_V2对地", dutyValue);
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
            await using IDmmApi dmm = new DmmSocketApi();
            try
            {
                await dmm.ConnectAsync(DmmIpAddress, token).ConfigureAwait(false);
                return await dmm.ReadOnceAsync(DmmMeasureMode.DCV, new DmmReadOptions { TimeoutMilliseconds = DmmTimeoutMs }, token).ConfigureAwait(false);
            }
            finally
            {
                try { await dmm.DisconnectAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
            }
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

        private sealed class WaveformPwmResult
        {
            public double? Vrms { get; set; }
            public double? FreqHz { get; set; }
            public double? Vmax { get; set; }
            public double? Vmin { get; set; }
            public double? Vpp { get; set; }
            public double? DutyPct { get; set; }
            public int SampleCount { get; set; }
            public int RisingEdgeCount { get; set; }
            public int FallingEdgeCount { get; set; }
            public double Threshold { get; set; }
        }

        private async Task<WaveformPwmResult> MeasurePwmFromWaveformWithRetryAsync(string title, CancellationToken token)
        {
            const int maxAttempts = 2;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] {title}：第{attempt}次读取 CH1 波形采样点...");
                var result = await MeasurePwmFromWaveformAsync(title, token).ConfigureAwait(false);
                if (IsWaveformPwmValid(result))
                    return result;

                if (attempt < maxAttempts)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] {title}：第{attempt}次波形未得到有效频率/占空比，保持矩阵连接并重试一次...");
                    await Task.Delay(1500, token).ConfigureAwait(false);
                }
            }

            AddLog($"[{DateTime.Now:HH:mm:ss}] {title}：两次波形读取仍无有效频率/占空比，结束测量并断开矩阵");
            return null;
        }

        private static bool IsWaveformPwmValid(WaveformPwmResult result)
        {
            return result != null && result.FreqHz.HasValue && result.DutyPct.HasValue;
        }

        private async Task<WaveformPwmResult> MeasurePwmFromWaveformAsync(string title, CancellationToken token)
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

                    string preamble = null;
                    byte[] data = null;

                    try
                    {
                        WriteScopeUnsafe(":STOP");
                        token.ThrowIfCancellationRequested();

                        WriteScopeUnsafe(":WAVeform:SOURce CHANnel1");
                        WriteScopeUnsafe(":WAVeform:MODE NORM");
                        WriteScopeUnsafe(":WAVeform:FORMat BYTE");
                        WriteScopeUnsafe(":WAVeform:PREamble?");
                        preamble = ReadLineAsync(stream, 10000, token).GetAwaiter().GetResult();
                        AddLog($"[{DateTime.Now:HH:mm:ss}] {title}：波形前导信息：'{preamble}'");

                        var cmd = Encoding.ASCII.GetBytes(":WAVeform:DATA?\n");
                        stream.Write(cmd, 0, cmd.Length);
                        data = ReadIeee4882DefiniteLengthBlock(stream, 50_000_000);
                    }
                    finally
                    {
                        try { WriteScopeUnsafe(":RUN"); } catch { }
                    }

                    var result = AnalyzePwmWaveform(preamble, data);
                    if (result != null)
                    {
                        AddLog($"[{DateTime.Now:HH:mm:ss}] {title}：波形点数={result.SampleCount}, 上升沿={result.RisingEdgeCount}, 下降沿={result.FallingEdgeCount}, 阈值={result.Threshold:F3}V");
                    }

                    return result;
                }, token).ConfigureAwait(false);
            }
            finally
            {
                _scopeIoLock.Release();
            }
        }

        private static WaveformPwmResult AnalyzePwmWaveform(string preamble, byte[] data)
        {
            if (string.IsNullOrWhiteSpace(preamble) || data == null || data.Length < 10)
                return null;

            var items = preamble
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : double.NaN)
                .ToArray();
            if (items.Length < 10)
                return null;

            double xInc = items[4];
            double yInc = items[7];
            double yOrig = items[8];
            double yRef = items[9];
            if (double.IsNaN(xInc) || xInc <= 0 || double.IsNaN(yInc))
                return null;

            var volts = new double[data.Length];
            for (int i = 0; i < data.Length; i++)
                volts[i] = (data[i] - yRef) * yInc + yOrig;

            double vMin = volts.Min();
            double vMax = volts.Max();
            double vpp = vMax - vMin;
            if (vpp <= 1e-9)
                return new WaveformPwmResult
                {
                    Vrms = Math.Sqrt(volts.Select(v => v * v).Average()),
                    Vmax = vMax,
                    Vmin = vMin,
                    Vpp = vpp,
                    SampleCount = volts.Length
                };

            double vLow = Percentile(volts, 0.10);
            double vHigh = Percentile(volts, 0.90);
            double span = vHigh - vLow;
            if (span <= 1e-9)
                span = vpp;

            double threshold = vLow + span * 0.5;
            double highThreshold = vLow + span * 0.6;
            double lowThreshold = vLow + span * 0.4;

            var risingEdges = new List<double>();
            var fallingEdges = new List<double>();
            bool isHigh = volts[0] >= threshold;

            for (int i = 1; i < volts.Length; i++)
            {
                if (!isHigh && volts[i] >= highThreshold)
                {
                    risingEdges.Add(GetCrossingTime(volts[i - 1], volts[i], i, threshold, xInc));
                    isHigh = true;
                }
                else if (isHigh && volts[i] <= lowThreshold)
                {
                    fallingEdges.Add(GetCrossingTime(volts[i - 1], volts[i], i, threshold, xInc));
                    isHigh = false;
                }
            }

            double? freq = CalculateFrequencyFromEdges(risingEdges, fallingEdges);
            double? duty = CalculateDutyFromEdges(risingEdges, fallingEdges);

            return new WaveformPwmResult
            {
                Vrms = Math.Sqrt(volts.Select(v => v * v).Average()),
                FreqHz = freq,
                Vmax = vMax,
                Vmin = vMin,
                Vpp = vpp,
                DutyPct = duty,
                SampleCount = volts.Length,
                RisingEdgeCount = risingEdges.Count,
                FallingEdgeCount = fallingEdges.Count,
                Threshold = threshold
            };
        }

        private static double Percentile(double[] values, double percentile)
        {
            var sorted = values.OrderBy(v => v).ToArray();
            if (sorted.Length == 0)
                return double.NaN;

            var index = (int)Math.Round((sorted.Length - 1) * percentile);
            if (index < 0)
                index = 0;
            if (index >= sorted.Length)
                index = sorted.Length - 1;
            return sorted[index];
        }

        private static double GetCrossingTime(double previous, double current, int index, double threshold, double xInc)
        {
            var denom = current - previous;
            var fraction = Math.Abs(denom) < 1e-12 ? 0.0 : (threshold - previous) / denom;
            if (fraction < 0.0)
                fraction = 0.0;
            if (fraction > 1.0)
                fraction = 1.0;
            return (index - 1 + fraction) * xInc;
        }

        private static double? CalculateFrequencyFromEdges(List<double> risingEdges, List<double> fallingEdges)
        {
            var periods = new List<double>();
            for (int i = 1; i < risingEdges.Count; i++)
            {
                var period = risingEdges[i] - risingEdges[i - 1];
                if (period > 0)
                    periods.Add(period);
            }

            if (periods.Count == 0)
            {
                for (int i = 1; i < fallingEdges.Count; i++)
                {
                    var period = fallingEdges[i] - fallingEdges[i - 1];
                    if (period > 0)
                        periods.Add(period);
                }
            }

            if (periods.Count == 0)
                return null;

            var medianPeriod = Median(periods);
            return medianPeriod > 0 ? 1.0 / medianPeriod : (double?)null;
        }

        private static double? CalculateDutyFromEdges(List<double> risingEdges, List<double> fallingEdges)
        {
            var duties = new List<double>();
            for (int i = 0; i < risingEdges.Count - 1; i++)
            {
                var rise = risingEdges[i];
                var nextRise = risingEdges[i + 1];
                var fall = fallingEdges.FirstOrDefault(t => t > rise && t < nextRise);
                if (fall <= rise)
                    continue;

                var period = nextRise - rise;
                var highWidth = fall - rise;
                if (period > 0 && highWidth > 0)
                    duties.Add(highWidth / period * 100.0);
            }

            if (duties.Count == 0)
                return null;

            return Median(duties);
        }

        private static double Median(List<double> values)
        {
            var sorted = values.OrderBy(v => v).ToArray();
            int mid = sorted.Length / 2;
            return sorted.Length % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2.0;
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

        private async Task SendPwmFrameAsync(int dutyPct, int freqHz, bool sendInit, CancellationToken token)
        {
            dutyPct = ClampDuty(dutyPct);
            freqHz = ClampFreq(freqHz);

            await EnsureFpgaConnectedAsync(token).ConfigureAwait(false);
            if (_fpgaStream == null)
                throw new InvalidOperationException("FPGA未连接");

            await _fpgaSendLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                AddLog($"PWM={dutyPct}%：发送到FPGA... (Freq={freqHz}Hz)");

                if (sendInit || !_fpgaInitialized)
                {
                    AddLog($"FPGA发送指令(设备初始化): {FormatData(DeviceInitCommandFrame)}");
                    await _fpgaStream.WriteAsync(DeviceInitCommandFrame, 0, DeviceInitCommandFrame.Length, token).ConfigureAwait(false);
                    await _fpgaStream.FlushAsync(token).ConfigureAwait(false);
                    _fpgaInitialized = true;
                }

                var cmd = BuildPwmCommand(dutyPct, freqHz);
                AddLog($"FPGA发送指令(PWM={dutyPct}%): {FormatData(cmd)}");
                await _fpgaStream.WriteAsync(cmd, 0, cmd.Length, token).ConfigureAwait(false);
                await _fpgaStream.FlushAsync(token).ConfigureAwait(false);
            }
            finally
            {
                _fpgaSendLock.Release();
            }
        }

        private async Task EnsureFpgaConnectedAsync(CancellationToken token)
        {
            if (_fpgaClient?.Connected == true && _fpgaStream != null)
                return;

            await DisconnectFpgaTcpAsync().ConfigureAwait(false);

            var client = new TcpClient { NoDelay = true };
            try
            {
                using (var timeoutCts = new CancellationTokenSource(2000))
                using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCts.Token))
                {
                    var connectTask = client.ConnectAsync(FpgaIpAddress, FpgaPort);
                    var cancelTask = Task.Delay(Timeout.Infinite, linkedCts.Token);
                    var completed = await Task.WhenAny(connectTask, cancelTask).ConfigureAwait(false);
                    if (completed != connectTask)
                    {
                        try { client.Close(); } catch { }
                        token.ThrowIfCancellationRequested();
                        throw new TimeoutException($"FPGA连接超时(2s): {FpgaIpAddress}:{FpgaPort}");
                    }

                    await connectTask.ConfigureAwait(false);
                }
                _fpgaClient = client;
                _fpgaStream = _fpgaClient.GetStream();
                AddLog($"FPGA TCP连接成功: {FpgaIpAddress}:{FpgaPort}");
            }
            catch (Exception ex)
            {
                try { client.Close(); } catch { }
                _fpgaClient = null;
                _fpgaStream = null;
                _fpgaInitialized = false;
                AddLog($"FPGA TCP连接失败: {ex.Message}");
                throw;
            }
        }

        private async Task DisconnectFpgaTcpAsync()
        {
            try { _fpgaStream?.Close(); } catch { }
            try { _fpgaClient?.Close(); } catch { }

            _fpgaStream = null;
            _fpgaClient = null;
            _fpgaInitialized = false;
        }

        private async Task EnsureScopeConnectedAsync(CancellationToken token)
        {
            if (_scopeTcpClient != null && _scopeTcpStream != null && _scopeTcpClient.Connected)
                return;

            await DisconnectScopeAsync(CancellationToken.None).ConfigureAwait(false);

            var client = new TcpClient();
            try
            {
                await client.ConnectAsync(ScopeIpAddress, ScopePort).ConfigureAwait(false);
                _scopeTcpClient = client;
                _scopeTcpStream = client.GetStream();
            }
            catch
            {
                try { client.Close(); } catch { }
                try { client.Dispose(); } catch { }
                _scopeTcpClient = null;
                _scopeTcpStream = null;
                throw;
            }

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

        private async Task DisconnectScopeAsync(CancellationToken token)
        {
            await _scopeIoLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                try { _scopeTcpStream?.Dispose(); } catch { }
                _scopeTcpStream = null;
                try { _scopeTcpClient?.Close(); } catch { }
                try { _scopeTcpClient?.Dispose(); } catch { }
                _scopeTcpClient = null;
            }
            finally
            {
                _scopeIoLock.Release();
            }
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

        private static byte[] ReadIeee4882DefiniteLengthBlock(NetworkStream stream, int maxBytes)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));
            if (maxBytes <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxBytes));

            try
            {
                int b;
                do
                {
                    b = stream.ReadByte();
                    if (b < 0)
                        return Array.Empty<byte>();
                } while (b != '#');

                int n = stream.ReadByte();
                if (n < 0)
                    return Array.Empty<byte>();

                int nDigits = n - '0';
                if (nDigits < 0 || nDigits > 9)
                    return Array.Empty<byte>();

                var lenBuf = new byte[nDigits];
                ReadExact(stream, lenBuf, 0, nDigits);
                if (!int.TryParse(Encoding.ASCII.GetString(lenBuf), out var payloadLen) || payloadLen < 0)
                    return Array.Empty<byte>();
                if (payloadLen > maxBytes)
                    throw new InvalidOperationException($"块数据长度超出限制: {payloadLen} bytes");

                var payload = new byte[payloadLen];
                ReadExact(stream, payload, 0, payloadLen);

                try
                {
                    while (stream.DataAvailable)
                    {
                        int next = stream.ReadByte();
                        if (next < 0)
                            break;
                        if (next != '\n' && next != '\r')
                            break;
                    }
                }
                catch
                {
                }

                return payload;
            }
            catch (IOException)
            {
                return Array.Empty<byte>();
            }
            catch (SocketException)
            {
                return Array.Empty<byte>();
            }
            catch (ObjectDisposedException)
            {
                return Array.Empty<byte>();
            }
            catch (InvalidOperationException)
            {
                return Array.Empty<byte>();
            }
            catch
            {
                return Array.Empty<byte>();
            }
        }

        private static void ReadExact(NetworkStream stream, byte[] buffer, int offset, int count)
        {
            int read = 0;
            while (read < count)
            {
                int r = stream.Read(buffer, offset + read, count - read);
                if (r <= 0)
                    throw new IOException("网络读取失败");
                read += r;
            }
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

        private static string FormatNum(double? v)
        {
            if (!v.HasValue || double.IsNaN(v.Value) || double.IsInfinity(v.Value))
                return "--";
            return v.Value.ToString("G6", CultureInfo.InvariantCulture);
        }

        private static string FormatMeasurementValue(double? v, string unit)
        {
            if (!v.HasValue || double.IsNaN(v.Value) || double.IsInfinity(v.Value))
                return "--";

            return $"{v.Value:F3}{unit}";
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
                0x0A,0x04,
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

            try { DisconnectScopeAsync(CancellationToken.None).GetAwaiter().GetResult(); } catch { }

            try { _manualMeasureCts?.Cancel(); } catch { }
            try { _manualMeasureCts?.Dispose(); } catch { }
            _manualMeasureCts = null;

            try { _manualTestLock?.Dispose(); } catch { }
            try { _autoTestLock?.Dispose(); } catch { }
            try { _opLock?.Dispose(); } catch { }
            try { _instrumentLock?.Dispose(); } catch { }
            try { _scopeIoLock?.Dispose(); } catch { }
            try { _fpgaSendLock?.Dispose(); } catch { }
        }
    }
}
