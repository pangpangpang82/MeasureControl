using Prism.Commands;
using Prism.Mvvm;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
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

            SendPwmCustomCommand = new DelegateCommand(async () => await SendCustomAsync(), () => !IsBusy && !IsManualTestRunning && !IsAutoTestRunning);
            MeasurePwmCustomCommand = new DelegateCommand(async () => await MeasureCustomAsync(), () => !IsBusy && !IsManualTestRunning && !IsAutoTestRunning);

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
                    RaiseAllCanExecuteChanged();
                }
            }
        }

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
                IsManualTestRunning = true;
                AddLog($"[{DateTime.Now:HH:mm:ss}] ========== 手动测试开始 ==========");
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

                await DisconnectFpgaAsync(CancellationToken.None).ConfigureAwait(false);

                // 断开示波器连接和矩阵开关（参照A_C_6_16_1_1_1ViewModel StopAutoTestAsync）
                try { await DisconnectInstrumentsAndMatrixAsync(CancellationToken.None).ConfigureAwait(false); } catch { }

                IsManualTestRunning = false;
                AddLog($"[{DateTime.Now:HH:mm:ss}] ========== 手动测试已停止 ==========");
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

                    // 断开示波器连接和矩阵开关（参照A_C_6_16_1_1_1ViewModel auto test finally）
                    try { await DisconnectInstrumentsAndMatrixAsync(CancellationToken.None).ConfigureAwait(false); } catch { }

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
            if (IsBusy || IsManualTestRunning || IsAutoTestRunning)
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
            if (IsBusy)
                return;

            IsBusy = true;
            IsMeasuringPwmCustom = true;
            try
            {
                // 自定义PWM只测量显示结果，不做合格判据
                var mV2 = await MeasureOnceWithMatrixAsync(NormalizeSlot6Output(DcmV2ToScope), "DCM_V2对地", PwmDutyPct, CancellationToken.None).ConfigureAwait(false);

                Application.Current.Dispatcher.Invoke(() =>
                {
                    J8VrmsText = "--"; J8FreqHzText = "--"; J8VmaxText = "--"; J8VminText = "--"; J8VppText = "--"; J8DutyPctText = "--";
                    J8J9VrmsText = "--"; J8J9FreqHzText = "--"; J8J9VmaxText = "--"; J8J9VminText = "--"; J8J9VppText = "--"; J8J9DutyPctText = "--";
                    J9VrmsText = "--"; J9VmaxText = "--"; J9VminText = "--"; J9VppText = "--";
                    if (mV2 != null)
                    {
                        J9FreqHzText = FormatNum(mV2.FreqHz);
                        J9DutyPctText = FormatNum(mV2.DutyPct);
                    }
                    else
                    {
                        J9FreqHzText = "--";
                        J9DutyPctText = "--";
                    }
                });

                if (mV2 != null)
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 自定义PWM={PwmDutyPct}%：FREQ={FormatNum(mV2.FreqHz)}Hz, DUTY={FormatNum(mV2.DutyPct)}%（仅显示，无合格判据）");
                else
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 自定义PWM={PwmDutyPct}%：测量失败");

                PwmCustomResult = mV2 != null ? $"FREQ={FormatNum(mV2.FreqHz)}Hz DUTY={FormatNum(mV2.DutyPct)}%" : "测量失败";
            }
            catch (OperationCanceledException)
            {
                AddLog($"自定义PWM测量已取消");
                PwmCustomResult = "CANCEL";
            }
            catch (Exception ex)
            {
                AddLog($"自定义PWM测量异常: {ex.Message}");
                PwmCustomResult = $"FAIL(异常:{ex.Message})";
            }
            finally
            {
                IsMeasuringPwmCustom = false;
                IsBusy = false;
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

            IsBusy = true;
            SetMeasuring(dutyPct, true);
            try
            {
                var (pass, failPoint) = await MeasureAllPointsAsync(dutyPct, CancellationToken.None).ConfigureAwait(false);
                SetFixedResult(dutyPct, pass ? "PASS" : $"FAIL({failPoint})");
            }
            catch (OperationCanceledException)
            {
                AddLog($"PWM={dutyPct}%测量已取消");
                SetFixedResult(dutyPct, "CANCEL");
            }
            catch (Exception ex)
            {
                AddLog($"PWM={dutyPct}%测量异常: {ex.Message}");
                SetFixedResult(dutyPct, $"FAIL(异常:{ex.Message})");
            }
            finally
            {
                SetMeasuring(dutyPct, false);
                IsBusy = false;
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

        /// <summary>
        /// 断开示波器连接和矩阵开关（参照A_C_6_16_1_1_1ViewModel DisconnectInstrumentsAndMatrixAsync）。
        /// 先强制关闭示波器网络流，再在_instrumentLock保护下断开矩阵。
        /// </summary>
        private async Task DisconnectInstrumentsAndMatrixAsync(CancellationToken token)
        {
            // 先强制关闭示波器连接（不等待锁，确保stop不会死锁）
            SafeCloseNetworkStream(ref _scopeTcpStream);
            SafeCloseTcpClient(ref _scopeTcpClient);

            try
            {
                // 用超时获取锁，避免测量持有锁时stop死锁
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
                cts.CancelAfter(3000);
                await _instrumentLock.WaitAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // 锁不可用，已在上面强制关闭了示波器
                await UnrouteMatrixAsync(CancellationToken.None).ConfigureAwait(false);
                return;
            }
            try
            {
                SafeCloseNetworkStream(ref _scopeTcpStream);
                SafeCloseTcpClient(ref _scopeTcpClient);
                await UnrouteMatrixAsync(CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                _instrumentLock.Release();
            }
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

                    // 示波器连接跨测量保持（参照A_C_6_16_1_1_1 MeasureRawCoreAsync：scope只在test stop时断开）
                    await EnsureScopeConnectedAsync(token).ConfigureAwait(false);

                    // Send AUToscale to auto-configure the scope settings (referencing A_C_6_16_1_1_1ViewModel)
                    AddLog($"[{DateTime.Now:HH:mm:ss}] {title}：发送 :AUToscale (示波器自动设置) 并等待完成...");
                    try
                    {
                        await SendScopeCommandAsync(":AUToscale", token).ConfigureAwait(false);
                        try
                        {
                            var opc = await QueryScopeAsync("*OPC?", 20000, token).ConfigureAwait(false);
                            AddLog($"[{DateTime.Now:HH:mm:ss}] *OPC? 响应：{opc ?? "(null)"}");
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

                    // Configure measurement items: FREQuency + PWIDth + NWIDth (referencing A_C_6_16_1_1_1ViewModel)
                    double? freq = null, duty = null;

                    AddLog($"[{DateTime.Now:HH:mm:ss}] {title}：PWM={expectedDutyPct}%，配置测量项 FREQuency, PWIDth, NWIDth...");
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
                        AddLog($"[{DateTime.Now:HH:mm:ss}] {title}：频率原始响应：'{rawFreq}'");
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

                    // Query duty cycle — calculate from PWIDth (高电平时间) + NWIDth (低电平时间) (referencing A_C_6_16_1_1_1ViewModel)
                    try
                    {
                        AddLog($"[{DateTime.Now:HH:mm:ss}] {title}：通过PWIDth+NWIDth计算占空比...");
                        var rawPw = await QueryScopeAsync(":MEASure:ITEM? PWIDth", 10000, token).ConfigureAwait(false);
                        AddLog($"[{DateTime.Now:HH:mm:ss}] {title}：高电平时间原始响应(PWIDth)：'{rawPw}'");
                        var pw = ParseScopeDouble(rawPw);

                        var rawNw = await QueryScopeAsync(":MEASure:ITEM? NWIDth", 10000, token).ConfigureAwait(false);
                        AddLog($"[{DateTime.Now:HH:mm:ss}] {title}：低电平时间原始响应(NWIDth)：'{rawNw}'");
                        var nw = ParseScopeDouble(rawNw);

                        if (pw.HasValue && nw.HasValue && (pw.Value + nw.Value) > 0)
                        {
                            duty = pw.Value / (pw.Value + nw.Value) * 100.0;
                            AddLog($"[{DateTime.Now:HH:mm:ss}] {title}：占空比计算值：PWIDth={pw.Value:F6}s, NWIDth={nw.Value:F6}s, DUTY={duty.Value:F3}%");
                        }
                        else
                        {
                            AddLog($"[{DateTime.Now:HH:mm:ss}] {title}：PWIDth/NWIDth无法获取有效值，占空比计算失败");
                        }
                    }
                    catch (Exception ex)
                    {
                        AddLog($"[{DateTime.Now:HH:mm:ss}] {title}：查询占空比异常：{ex.Message}");
                    }

                    AddLog($"[{DateTime.Now:HH:mm:ss}] {title}：测量完成 FREQ={FormatNum(freq)}Hz, DUTY={FormatNum(duty)}%");

                    return new MeasurementResult
                    {
                        Title = title,
                        FreqHz = freq,
                        DutyPct = duty
                    };
                }
                finally
                {
                    // 测量完成后断开矩阵开关（参照A_C_6_16_1_1_1 MeasureRawCoreAsync DisconnectMatrixAsync）
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
                J9VrmsText = "--"; J9VmaxText = "--"; J9VminText = "--"; J9VppText = "--";
                if (mV2 != null)
                {
                    J9FreqHzText = FormatNum(mV2.FreqHz);
                    J9DutyPctText = FormatNum(mV2.DutyPct);
                }
                else
                {
                    J9FreqHzText = "--";
                    J9DutyPctText = "--";
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
        /// 100%/0% PWM：路由矩阵节点，用万用表测量 DCM_V1 对 DCM_V2 直流电压。
        /// 参照 A_C_6_18_1_1ViewModel ReadDmmVoltageAsync 模式：
        /// 矩阵连接 → 延时2秒 → DMM连接+单次读数+断开 → 矩阵断开。
        /// 每次新建DMM实例，无需flush read。
        /// </summary>
        private async Task<(bool Ok, double? Voltage)> MeasureV1V2VoltageWithMatrixAsync(CancellationToken token)
        {
            await _instrumentLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                var matrix = MatrixControlService.Instance;

                // 矩阵连接：DMM通路 + 信号通路
                var ops = new (string inNode, string outNode, int slot)[]
                {
                    (DmmMatrixRow, DmmMatrixOutput, Chassis2Slot4),  // DMM通路
                    (Slot6Row, DcmV1V1ToScope, Chassis2Slot6)        // 信号通路
                };

                AddLog($"[{DateTime.Now:HH:mm:ss}] 路由DCM_V1对DCM_V2：DMM({DmmMatrixRow}->{DmmMatrixOutput} slot{Chassis2Slot4}) + 信号({Slot6Row}->{DcmV1V1ToScope} slot{Chassis2Slot6})");

                var connectTasks = ops.Select(op =>
                    matrix.ConnectNodesAsync(op.inNode, op.outNode, op.slot, MatrixIpAddress, MatrixTcpBasePort)).ToArray();
                var connectResults = await Task.WhenAll(connectTasks).ConfigureAwait(false);
                bool allOk = connectResults.All(r => r);

                if (!allOk)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] DCM_V1对DCM_V2：矩阵路由失败 DMM={connectResults[0]}, 信号={connectResults[1]}");
                    // 即使失败也尝试断开已连接的
                    try
                    {
                        var disconnectTasks = ops.Select(op =>
                            matrix.DisconnectNodesAsync(op.inNode, op.outNode, op.slot, MatrixIpAddress, MatrixTcpBasePort)).ToArray();
                        await Task.WhenAll(disconnectTasks).ConfigureAwait(false);
                    }
                    catch { }
                    return (false, null);
                }

                // 延时2秒等待继电器闭合与信号通路稳定（参照A_C_6_18_1_1ViewModel ReadDmmVoltageAsync）
                AddLog($"[{DateTime.Now:HH:mm:ss}] DCM_V1对DCM_V2：延时2秒等待信号稳定...");
                await Task.Delay(2000, token).ConfigureAwait(false);

                // DMM测量：每次新建实例，连接+读数+断开（参照A_C_6_18_1_1ViewModel ReadDmmVoltageAsync）
                double? voltage = null;
                try
                {
                    var ip = (DmmIpAddress ?? string.Empty).Trim();
                    await using IDmmApi dmm = new DmmSocketApi();
                    try
                    {
                        await dmm.ConnectAsync(ip, token).ConfigureAwait(false);
                        var r = await dmm.ReadOnceAsync(DmmMeasureMode.DCV, new DmmReadOptions { TimeoutMilliseconds = DmmTimeoutMs }, token).ConfigureAwait(false);
                        if (r != null && !r.IsOverrange && r.Value.HasValue)
                        {
                            voltage = r.Value.Value;
                            AddLog($"[{DateTime.Now:HH:mm:ss}] DCM_V1对DCM_V2：万用表直流电压={voltage.Value:F4}V");
                        }
                        else
                        {
                            AddLog($"[{DateTime.Now:HH:mm:ss}] DCM_V1对DCM_V2：万用表读数无效 (raw={r?.Raw}, overrange={r?.IsOverrange})");
                        }
                    }
                    finally
                    {
                        try { await dmm.DisconnectAsync(token).ConfigureAwait(false); } catch { }
                    }
                }
                catch (Exception ex)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] DCM_V1对DCM_V2：电压测量异常：{ex.Message}");
                }
                finally
                {
                    // 矩阵断开（参照A_C_6_18_1_1ViewModel ReadDmmVoltageAsync finally）
                    try
                    {
                        var disconnectTasks = ops.Select(op =>
                            matrix.DisconnectNodesAsync(op.inNode, op.outNode, op.slot, MatrixIpAddress, MatrixTcpBasePort)).ToArray();
                        await Task.WhenAll(disconnectTasks).ConfigureAwait(false);
                    }
                    catch { }
                }

                return (voltage.HasValue, voltage);
            }
            finally
            {
                _instrumentLock.Release();
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
            public double? FreqHz { get; set; }
            public double? DutyPct { get; set; }
        }

        private static bool IsMeasurementPass(MeasurementResult m, int expectedDutyPct, int expectedFreqHz, out string reason)
        {
            if (m == null)
            {
                reason = "测量结果为空";
                return false;
            }

            // 50% PWM: duty cycle within ±1% (only duty, no frequency check)
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

            // 其他非0%/100%占空比不参与合格判据（由调用方决定是否判据）
            reason = "非固定占空比，无合格判据";
            return false;
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

        private async Task EnsureScopeConnectedAsync(CancellationToken token)
        {
            if (_scopeTcpClient != null && _scopeTcpStream != null)
                return;

            _scopeTcpClient = new TcpClient();
            var connectTask = _scopeTcpClient.ConnectAsync(ScopeIpAddress, ScopePort);
            var delayTask = Task.Delay(3000, token);
            var completedTask = await Task.WhenAny(connectTask, delayTask).ConfigureAwait(false);
            if (completedTask == delayTask)
            {
                _scopeTcpClient.Close();
                _scopeTcpClient = null;
                throw new TimeoutException("连接示波器超时（限时3秒），请检查示波器网络。");
            }
            await connectTask.ConfigureAwait(false);
            _scopeTcpStream = _scopeTcpClient.GetStream();

            try
            {
                _scopeTcpStream.ReadTimeout = 5000;
                _scopeTcpStream.WriteTimeout = 5000;
            }
            catch
            {
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

        private static void SafeCloseNetworkStream(ref NetworkStream stream)
        {
            if (stream == null)
                return;
            try { stream.Close(); } catch { }
            try { stream.Dispose(); } catch { }
            stream = null;
        }

        private static void SafeCloseTcpClient(ref TcpClient client)
        {
            if (client == null)
                return;
            try { client.Close(); } catch { }
            try { client.Dispose(); } catch { }
            client = null;
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

            // 断开示波器和矩阵
            try { DisconnectInstrumentsAndMatrixAsync(CancellationToken.None).GetAwaiter().GetResult(); } catch { }

            try { _manualMeasureCts?.Cancel(); } catch { }
            try { _manualMeasureCts?.Dispose(); } catch { }
            _manualMeasureCts = null;

            try { _manualTestLock?.Dispose(); } catch { }
            try { _autoTestLock?.Dispose(); } catch { }
            try { _opLock?.Dispose(); } catch { }
            try { _instrumentLock?.Dispose(); } catch { }
            try { _scopeIoLock?.Dispose(); } catch { }
        }
    }
}