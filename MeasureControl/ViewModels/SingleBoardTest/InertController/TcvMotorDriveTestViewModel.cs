using Prism.Commands;
using Prism.Events;
using Prism.Ioc;
using Prism.Mvvm;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using MeasureControl.Models.Devices;
using MeasureControl.Services;
using MeasureControl.Services.HardwareApis;

namespace MeasureControl.ViewModels.SingleBoardTest.InertController
{
    public sealed class TcvMotorDriveTestViewModel : BindableBase, IDisposable
    {
        private const string PowerSupplyIpAddress = "192.168.1.15";
        private const double InputVoltageV = 28.0;
        private const double InputCurrentA = 1.0;

        public bool SkipMainPowerOff { get; set; }

        private const int ArincTxChannelIndex = 1;
        private const int ArincRxChannelIndex = 0;
        private const double DefaultArincRate = 100000.0;
        private const int ArincAfterTxOpenSettleDelayMs = 1000;
        private const int ArincPollIntervalMs = 10;
        private const byte ArincExpectedSdi = 1;

        private const uint AtpSsmDataSdi = 0xC10001u;
        private const byte AtpLabelOctal174Dec = 124;

        private const byte Label173Raw = 222;
        private const byte ControlSdi = 1;
        private const int FreqBitOffsetInData19 = 0;
        private const int FreqBitLength = 10;
        private const int EnableBitOffsetInData19 = 12;
        private const int AtpBitOffsetInData19 = 14;
        private const int DirectionBitOffsetInData19 = 15;

        private const int MotorCurrentBitOffsetInData19 = 0;
        private const int MotorCurrentBitLength = 12;
        private const double MotorCurrentResolutionA = 0.01;

        private const byte MotorPhaseACurrentLabelRaw = 218;
        private const byte MotorPhaseBCurrentLabelRaw = 58;

        private const double DefaultShuntResistanceOhm = 0.1;
        private const double DefaultNonZeroThresholdA = 0.018;

        private readonly SemaphoreSlim _opLock = new SemaphoreSlim(1, 1);
        private CancellationTokenSource _cts;

        private readonly ISingleBoardTestContextService _singleBoardTestContext;
        private readonly ProjectService _projectService;
        private readonly IEventAggregator _eventAggregator;
        private readonly IComponentPowerStateApi _componentPowerStateApi;
        private IPowerSupplyApi _power;

        private IArt4229Api _arinc;
        private bool _arincTxOpened;
        private bool _arincRxOpened;
        private Task _arincRxLoopTask;
        private bool _atpTxOpened;
        private bool _atpModeEntered;

        private readonly object _motorCurrentLock = new object();
        private System.Collections.Generic.List<double> _phaseACurrentCache = new System.Collections.Generic.List<double>();
        private System.Collections.Generic.List<double> _phaseBCurrentCache = new System.Collections.Generic.List<double>();

        private bool _isManualTestRunning;
        private bool _isAutoTestRunning;
        private bool _isBusy;

        private string _lastTestTime = "--";
        private string _lastTestResult = "--";
        private string _overallResult = "--";

        private int _stepFrequencyHz = 500;
        private bool _isReverse;
        private bool _isMotorEnabled;

        private double _shuntResistanceOhm = DefaultShuntResistanceOhm;
        private double _nonZeroThresholdA = DefaultNonZeroThresholdA;

        public TcvMotorDriveTestViewModel(
            ISingleBoardTestContextService singleBoardTestContext,
            ProjectService projectService,
            IEventAggregator eventAggregator,
            IComponentPowerStateApi componentPowerStateApi = null)
        {
            _singleBoardTestContext = singleBoardTestContext;
            _projectService = projectService;
            _eventAggregator = eventAggregator;
            _componentPowerStateApi = componentPowerStateApi;

            ManualTestCommand = new DelegateCommand(async () => await OnManualTestAsync());
            AutoTestCommand = new DelegateCommand(async () => await OnAutoTestAsync());
            ClearLogCommand = new DelegateCommand(() => Logs.Clear());

            ApplyControlCommand = new DelegateCommand(async () => await OnApplyControlAsync(), () => IsManualTestRunning && !IsBusy);
            MeasurePhaseACommand = new DelegateCommand(async () => await OnMeasurePhaseAsync(MotorPhase.PhaseA), () => IsManualTestRunning && !IsBusy);
            MeasurePhaseBCommand = new DelegateCommand(async () => await OnMeasurePhaseAsync(MotorPhase.PhaseB), () => IsManualTestRunning && !IsBusy);

            FixedFwd500Command = new DelegateCommand(async () => await OnFixedGroupAsync(reverse: false, freqHz: 500), () => IsManualTestRunning && !IsBusy);
            FixedFwd1000Command = new DelegateCommand(async () => await OnFixedGroupAsync(reverse: false, freqHz: 1000), () => IsManualTestRunning && !IsBusy);
            FixedRev500Command = new DelegateCommand(async () => await OnFixedGroupAsync(reverse: true, freqHz: 500), () => IsManualTestRunning && !IsBusy);
            FixedRev1000Command = new DelegateCommand(async () => await OnFixedGroupAsync(reverse: true, freqHz: 1000), () => IsManualTestRunning && !IsBusy);

            Results = new ObservableCollection<TcvMotorStepResultViewModel>();
            InitializeFixedResults();
        }

        private void InitializeFixedResults()
        {
            Results.Clear();

            Results.Add(new TcvMotorStepResultViewModel { Sequence = "a)", DirectionText = "正转", StepFrequencyHz = 500 });
            Results.Add(new TcvMotorStepResultViewModel { Sequence = "b)", DirectionText = "正转", StepFrequencyHz = 1000 });
            Results.Add(new TcvMotorStepResultViewModel { Sequence = "c)", DirectionText = "反转", StepFrequencyHz = 500 });
            Results.Add(new TcvMotorStepResultViewModel { Sequence = "d)", DirectionText = "反转", StepFrequencyHz = 1000 });
        }

        private void ResetFixedResults()
        {
            foreach (var r in Results)
            {
                r.PhaseACurrentA = null;
                r.PhaseBCurrentA = null;
                r.PhaseAMaxAbsCurrentA = null;
                r.PhaseBMaxAbsCurrentA = null;
                r.Result = "--";
            }
        }

        private void UpdateOverallFromFixedResults()
        {
            if (Results == null || Results.Count == 0)
            {
                OverallResult = "--";
                return;
            }

            if (Results.Any(r => string.Equals(r.Result, "FAIL", StringComparison.OrdinalIgnoreCase)))
            {
                OverallResult = "FAIL";
                return;
            }

            if (Results.All(r => string.Equals(r.Result, "PASS", StringComparison.OrdinalIgnoreCase)))
            {
                OverallResult = "PASS";
                return;
            }

            OverallResult = "--";
        }

        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();

        public ObservableCollection<TcvMotorStepResultViewModel> Results { get; }

        public DelegateCommand ManualTestCommand { get; }
        public DelegateCommand AutoTestCommand { get; }
        public DelegateCommand ClearLogCommand { get; }

        public DelegateCommand ApplyControlCommand { get; }
        public DelegateCommand MeasurePhaseACommand { get; }
        public DelegateCommand MeasurePhaseBCommand { get; }

        public DelegateCommand FixedFwd500Command { get; }
        public DelegateCommand FixedFwd1000Command { get; }
        public DelegateCommand FixedRev500Command { get; }
        public DelegateCommand FixedRev1000Command { get; }

        public bool IsManualTestRunning
        {
            get => _isManualTestRunning;
            private set
            {
                if (SetProperty(ref _isManualTestRunning, value))
                {
                    RaiseCanExecuteChanged();
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
                    RaiseCanExecuteChanged();
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
                    RaiseCanExecuteChanged();
                }
            }
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

        public string OverallResult
        {
            get => _overallResult;
            private set => SetProperty(ref _overallResult, value);
        }

        public int StepFrequencyHz
        {
            get => _stepFrequencyHz;
            set => SetProperty(ref _stepFrequencyHz, value);
        }

        public bool IsReverse
        {
            get => _isReverse;
            set => SetProperty(ref _isReverse, value);
        }

        public bool IsMotorEnabled
        {
            get => _isMotorEnabled;
            set => SetProperty(ref _isMotorEnabled, value);
        }

        public double ShuntResistanceOhm
        {
            get => _shuntResistanceOhm;
            set => SetProperty(ref _shuntResistanceOhm, value);
        }

        public double NonZeroThresholdA
        {
            get => _nonZeroThresholdA;
            set => SetProperty(ref _nonZeroThresholdA, value);
        }

        private void RaiseCanExecuteChanged()
        {
            ApplyControlCommand?.RaiseCanExecuteChanged();
            MeasurePhaseACommand?.RaiseCanExecuteChanged();
            MeasurePhaseBCommand?.RaiseCanExecuteChanged();

            FixedFwd500Command?.RaiseCanExecuteChanged();
            FixedFwd1000Command?.RaiseCanExecuteChanged();
            FixedRev500Command?.RaiseCanExecuteChanged();
            FixedRev1000Command?.RaiseCanExecuteChanged();
        }

        private async Task OnFixedGroupAsync(bool reverse, int freqHz)
        {
            if (!IsManualTestRunning || IsBusy)
                return;

            await _opLock.WaitAsync().ConfigureAwait(false);
            try
            {
                IsBusy = true;
                if (_cts == null)
                    _cts = new CancellationTokenSource();

                await EnsureArincTxReadyAsync(_cts.Token).ConfigureAwait(false);
                await EnsureArincRxReadyAsync(_cts.Token).ConfigureAwait(false);
                ResetMotorCurrentCache();
                StartArincRxLoopIfNeeded(_cts.Token);

                var step = await RunFixedGroupInternalAsync(reverse, freqHz, _cts.Token).ConfigureAwait(false);
                if (step == null)
                    throw new InvalidOperationException("固定组结果行未找到");

                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                LastTestResult = step.Result;
                UpdateOverallFromFixedResults();
            }
            catch (OperationCanceledException)
            {
                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                LastTestResult = "FAIL";
                OverallResult = "FAIL";
                Log($"[{DateTime.Now:HH:mm:ss}] 固定组测试已取消");
            }
            catch (Exception ex)
            {
                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                LastTestResult = "FAIL";
                OverallResult = "FAIL";
                Log($"[{DateTime.Now:HH:mm:ss}] 固定组测试异常：{ex.Message}");
            }
            finally
            {
                IsBusy = false;
                _opLock.Release();
            }
        }

        private async Task<TcvMotorStepResultViewModel> RunFixedGroupInternalAsync(bool reverse, int freqHz, CancellationToken token)
        {
            ResetMotorCurrentCache();
            var directionText = reverse ? "反转" : "正转";
            var step = Results?.FirstOrDefault(r =>
                string.Equals(r.DirectionText, directionText, StringComparison.OrdinalIgnoreCase) &&
                r.StepFrequencyHz == freqHz);

            StepFrequencyHz = freqHz;
            IsReverse = reverse;
            IsMotorEnabled = true;

            await ApplyControlInternalAsync(token).ConfigureAwait(false);
            await Task.Delay(300, token).ConfigureAwait(false);

            var (aPass, aMax, aLast) = await ObservePhaseAsync(MotorPhase.PhaseA, token).ConfigureAwait(false);
            var (bPass, bMax, bLast) = await ObservePhaseAsync(MotorPhase.PhaseB, token).ConfigureAwait(false);

            if (step != null)
            {
                step.PhaseACurrentA = aMax;
                step.PhaseBCurrentA = bMax;
                step.PhaseAMaxAbsCurrentA = aMax;
                step.PhaseBMaxAbsCurrentA = bMax;
                step.Result = (aPass && bPass) ? "PASS" : "FAIL";
            }

            await Task.Delay(4000, token).ConfigureAwait(false);
            IsMotorEnabled = false;
            await ApplyControlInternalAsync(token).ConfigureAwait(false);
            await Task.Delay(120, token).ConfigureAwait(false);

            return step;
        }

        private async Task OnManualTestAsync()
        {
            if (IsManualTestRunning)
            {
                await StopAsync().ConfigureAwait(false);
                return;
            }

            // 检查是否已总上电
            var _hps = ContainerLocator.Container.Resolve<IHydraulicPowerService>();
            if (_hps == null || !_hps.IsHydraulicPowered)
            {
                MessageBox.Show("请先点击左上角组件上电按钮进行总上电，再进行测试。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (IsAutoTestRunning)
            {
                await StopAsync().ConfigureAwait(false);
            }

            await _opLock.WaitAsync().ConfigureAwait(false);
            try
            {
                _cts?.Cancel();
                _cts?.Dispose();
                _cts = new CancellationTokenSource();

                Logs.Clear();
                ResetFixedResults();

                IsManualTestRunning = true;
                IsAutoTestRunning = false;
                IsBusy = true;
                LastTestTime = "--";
                LastTestResult = "--";
                OverallResult = "--";

                Log($"[{DateTime.Now:HH:mm:ss}] 开始手动测试：TCV电机驱动测试");

                await EnsurePowerAsync(InputVoltageV, _cts.Token).ConfigureAwait(false);
                await EnsureArincTxReadyAsync(_cts.Token).ConfigureAwait(false);
                await EnsureArincRxReadyAsync(_cts.Token).ConfigureAwait(false);

                await EnsureAtpModeAsync(_cts.Token).ConfigureAwait(false);
                await EnsureArincTxReadyAsync(_cts.Token).ConfigureAwait(false);

                ResetMotorCurrentCache();
                StartArincRxLoopIfNeeded(_cts.Token);

                Log($"[{DateTime.Now:HH:mm:ss}] 已就绪：ARINC429 TX通道{ArincTxChannelIndex}下发Label173/174；RX通道{ArincRxChannelIndex}接收电流判据");
            }
            catch (OperationCanceledException)
            {
                await StopAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log($"[{DateTime.Now:HH:mm:ss}] 手动测试初始化异常：{ex.Message}");
                await StopAsync().ConfigureAwait(false);
            }
            finally
            {
                IsBusy = false;
                _opLock.Release();
            }
        }

        public async Task<string> RunOnceAsync(CancellationToken cancellationToken)
        {
            if (IsAutoTestRunning || IsManualTestRunning)
            {
                return OverallResult;
            }

            await _opLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                _cts?.Cancel();
                _cts?.Dispose();
                _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

                Logs.Clear();
                ResetFixedResults();

                IsAutoTestRunning = true;
                IsManualTestRunning = false;
                IsBusy = true;
                LastTestTime = "--";
                LastTestResult = "--";
                OverallResult = "--";

                Log($"[{DateTime.Now:HH:mm:ss}] 自动测试开始：500/1000Hz + 正反转 + 使能，采集A/B相电流");

                await EnsurePowerAsync(InputVoltageV, _cts.Token).ConfigureAwait(false);
                await EnsureArincTxReadyAsync(_cts.Token).ConfigureAwait(false);
                await EnsureArincRxReadyAsync(_cts.Token).ConfigureAwait(false);

                await EnsureAtpModeAsync(_cts.Token).ConfigureAwait(false);
                await EnsureArincTxReadyAsync(_cts.Token).ConfigureAwait(false);

                ResetMotorCurrentCache();
                StartArincRxLoopIfNeeded(_cts.Token);

                var failures = new System.Collections.Generic.List<string>();
                bool isFirstesp = true;
                foreach (var reverse in new[] { false, true })
                {
                    foreach (var freq in new[] { 500, 1000 })
                    {
                        _cts.Token.ThrowIfCancellationRequested();
                        if(!isFirstesp)
                        {
                            await Task.Delay(2000, _cts.Token).ConfigureAwait(false);
                        }
                        var step = await RunFixedGroupInternalAsync(reverse, freq, _cts.Token).ConfigureAwait(false);
                        isFirstesp = false;
                        if (step == null)
                            continue;

                        if (!string.Equals(step.Result, "PASS", StringComparison.OrdinalIgnoreCase))
                        {
                            failures.Add($"{step.DirectionText}/{freq}Hz 电流判据不通过(阈值{NonZeroThresholdA:0.###}A)");
                        }
                    }
                }

                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                if (failures.Count == 0)
                {
                    LastTestResult = "PASS";
                    UpdateOverallFromFixedResults();
                    Log($"[{DateTime.Now:HH:mm:ss}] 自动测试结束：PASS");
                }
                else
                {
                    LastTestResult = "FAIL";
                    UpdateOverallFromFixedResults();
                    Log($"[{DateTime.Now:HH:mm:ss}] 自动测试结束：FAIL");
                    foreach (var f in failures)
                    {
                        Log($"[{DateTime.Now:HH:mm:ss}] FAIL原因：{f}");
                    }
                }

                return OverallResult;
            }
            catch (OperationCanceledException)
            {
                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                LastTestResult = "FAIL";
                OverallResult = "FAIL";
                Log($"[{DateTime.Now:HH:mm:ss}] 自动测试已取消");
                return "FAIL";
            }
            catch (Exception ex)
            {
                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                LastTestResult = "FAIL";
                OverallResult = "FAIL";
                Log($"[{DateTime.Now:HH:mm:ss}] 自动测试异常：{ex.Message}");
                return "FAIL";
            }
            finally
            {
                IsBusy = false;
                IsAutoTestRunning = false;
                await StopInternalAsync().ConfigureAwait(false);
                _opLock.Release();
            }
        }

        private async Task OnAutoTestAsync()
        {
            if (IsAutoTestRunning)
            {
                await StopAsync().ConfigureAwait(false);
                return;
            }

            // 检查是否已总上电
            var _hps = ContainerLocator.Container.Resolve<IHydraulicPowerService>();
            if (_hps == null || !_hps.IsHydraulicPowered)
            {
                MessageBox.Show("请先点击左上角组件上电按钮进行总上电，再进行测试。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (IsManualTestRunning)
            {
                await StopAsync().ConfigureAwait(false);
            }

            await _opLock.WaitAsync().ConfigureAwait(false);
            try
            {
                _cts?.Cancel();
                _cts?.Dispose();
                _cts = new CancellationTokenSource();

                Logs.Clear();
                ResetFixedResults();

                IsAutoTestRunning = true;
                IsManualTestRunning = false;
                IsBusy = true;
                LastTestTime = "--";
                LastTestResult = "--";
                OverallResult = "--";

                Log($"[{DateTime.Now:HH:mm:ss}] 自动测试开始：500/1000Hz + 正反转 + 使能，采集A/B相电流");

                await EnsurePowerAsync(InputVoltageV, _cts.Token).ConfigureAwait(false);
                await EnsureArincTxReadyAsync(_cts.Token).ConfigureAwait(false);
                await EnsureArincRxReadyAsync(_cts.Token).ConfigureAwait(false);

                await EnsureAtpModeAsync(_cts.Token).ConfigureAwait(false);
                await EnsureArincTxReadyAsync(_cts.Token).ConfigureAwait(false);

                ResetMotorCurrentCache();
                StartArincRxLoopIfNeeded(_cts.Token);

                var failures = new System.Collections.Generic.List<string>();
                bool isFirstesp = true;
                foreach (var reverse in new[] { false, true })
                {
                    foreach (var freq in new[] { 500, 1000 })
                    {
                        _cts.Token.ThrowIfCancellationRequested();
                        if(!isFirstesp)
                        {
                            await Task.Delay(2000, _cts.Token).ConfigureAwait(false);
                                

                        }
                        var step = await RunFixedGroupInternalAsync(reverse, freq, _cts.Token).ConfigureAwait(false);
                        isFirstesp = false;
                        if (step == null)
                            continue;

                        if (!string.Equals(step.Result, "PASS", StringComparison.OrdinalIgnoreCase))
                        {
                            failures.Add($"{step.DirectionText}/{freq}Hz 电流判据不通过(阈值{NonZeroThresholdA:0.###}A)");
                        }
                    }
                }

                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                if (failures.Count == 0)
                {
                    LastTestResult = "PASS";
                    UpdateOverallFromFixedResults();
                    Log($"[{DateTime.Now:HH:mm:ss}] 自动测试结束：PASS");
                }
                else
                {
                    LastTestResult = "FAIL";
                    UpdateOverallFromFixedResults();
                    Log($"[{DateTime.Now:HH:mm:ss}] 自动测试结束：FAIL");
                    foreach (var f in failures)
                    {
                        Log($"[{DateTime.Now:HH:mm:ss}] FAIL原因：{f}");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                LastTestResult = "FAIL";
                OverallResult = "FAIL";
                Log($"[{DateTime.Now:HH:mm:ss}] 自动测试已取消");
            }
            catch (Exception ex)
            {
                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                LastTestResult = "FAIL";
                OverallResult = "FAIL";
                Log($"[{DateTime.Now:HH:mm:ss}] 自动测试异常：{ex.Message}");
            }
            finally
            {
                IsBusy = false;
                IsAutoTestRunning = false;
                await StopInternalAsync().ConfigureAwait(false);
                _opLock.Release();
            }
        }

        private async Task OnApplyControlAsync()
        {
            if (!IsManualTestRunning || IsBusy)
                return;

            await _opLock.WaitAsync().ConfigureAwait(false);
            try
            {
                IsBusy = true;
                if (_cts == null)
                    _cts = new CancellationTokenSource();

                await EnsureArincTxReadyAsync(_cts.Token).ConfigureAwait(false);
                await EnsureArincRxReadyAsync(_cts.Token).ConfigureAwait(false);
                StartArincRxLoopIfNeeded(_cts.Token);
                await ApplyControlInternalAsync(_cts.Token).ConfigureAwait(false);
                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                LastTestResult = "--";
                OverallResult = "--";
            }
            catch (Exception ex)
            {
                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                LastTestResult = "FAIL";
                OverallResult = "FAIL";
                Log($"[{DateTime.Now:HH:mm:ss}] 控制下发异常：{ex.Message}");
            }
            finally
            {
                IsBusy = false;
                _opLock.Release();
            }
        }

        private async Task OnMeasurePhaseAsync(MotorPhase phase)
        {
            if (!IsManualTestRunning || IsBusy)
                return;

            await _opLock.WaitAsync().ConfigureAwait(false);
            try
            {
                IsBusy = true;
                if (_cts == null)
                    _cts = new CancellationTokenSource();

                await EnsureArincTxReadyAsync(_cts.Token).ConfigureAwait(false);
                await EnsureArincRxReadyAsync(_cts.Token).ConfigureAwait(false);
                StartArincRxLoopIfNeeded(_cts.Token);

                var (pass, maxAbs, last) = await ObservePhaseAsync(phase, _cts.Token).ConfigureAwait(false);

                var directionText = IsReverse ? "反转" : "正转";
                var step = Results?.FirstOrDefault(r =>
                    string.Equals(r.DirectionText, directionText, StringComparison.OrdinalIgnoreCase) &&
                    r.StepFrequencyHz == StepFrequencyHz);

                if (step == null)
                    throw new InvalidOperationException("固定组结果行未找到");

                if (phase == MotorPhase.PhaseA)
                {
                    step.PhaseACurrentA = maxAbs;
                    step.PhaseAMaxAbsCurrentA = maxAbs;
                }
                else
                {
                    step.PhaseBCurrentA = maxAbs;
                    step.PhaseBMaxAbsCurrentA = maxAbs;
                }

                if (step.PhaseAMaxAbsCurrentA.HasValue && step.PhaseBMaxAbsCurrentA.HasValue)
                {
                    var aPass = Math.Abs(step.PhaseAMaxAbsCurrentA.Value) > NonZeroThresholdA;
                    var bPass = Math.Abs(step.PhaseBMaxAbsCurrentA.Value) > NonZeroThresholdA;
                    step.Result = (aPass && bPass) ? "PASS" : "FAIL";
                }
                else
                {
                    step.Result = pass ? "PASS" : "FAIL";
                }

                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                LastTestResult = step.Result;
                UpdateOverallFromFixedResults();
            }
            catch (Exception ex)
            {
                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                LastTestResult = "FAIL";
                OverallResult = "FAIL";
                Log($"[{DateTime.Now:HH:mm:ss}] 采集异常：{ex.Message}");
            }
            finally
            {
                IsBusy = false;
                _opLock.Release();
            }
        }

        private async Task ApplyControlInternalAsync(CancellationToken token)
        {
            await EnsureArincTxReadyAsync(token).ConfigureAwait(false);
            if (_arinc == null || !_arinc.IsConnected)
                throw new InvalidOperationException("ARINC429未连接");

            var wFreq = BuildFrequencyWord();
            var wEnDir = BuildEnableDirectionWord();

            await _arinc.SendWordsSingleAsync(ArincTxChannelIndex, new[] { wFreq, wEnDir }, Art4229Parity.Odd, token).ConfigureAwait(false);

            Log($"[{DateTime.Now:HH:mm:ss}] 控制下发：Freq={StepFrequencyHz}Hz, Dir={(IsReverse ? "REV" : "FWD")}, EN={(IsMotorEnabled ? 1 : 0)}");
            Log($"[{DateTime.Now:HH:mm:ss}] Label173(频率): 0x{wFreq:X8}");
            Log($"[{DateTime.Now:HH:mm:ss}] Label174(使能/方向): 0x{wEnDir:X8}");
        }

        private async Task<(bool pass, double? maxAbsA, double? lastA)> ObservePhaseAsync(MotorPhase phase, CancellationToken token)
        {
            const int sampleCount = 15;
            const int sampleIntervalMs = 50;

            double? maxAbs = null;
            double? last = null;

            for (int i = 0; i < sampleCount; i++)
            {
                token.ThrowIfCancellationRequested();
                var curr = await MeasurePhaseCurrentOnceAsync(phase, token).ConfigureAwait(false);
                last = curr;
                var abs = curr.HasValue ? Math.Abs(curr.Value) : (double?)null;
                if (abs.HasValue)
                {
                    if (!maxAbs.HasValue || abs.Value > maxAbs.Value)
                        maxAbs = abs.Value;
                }

                await Task.Delay(sampleIntervalMs, token).ConfigureAwait(false);
            }

            var pass = maxAbs.HasValue && maxAbs.Value > NonZeroThresholdA;
            Log($"[{DateTime.Now:HH:mm:ss}] {(phase == MotorPhase.PhaseA ? "A相" : "B相")} 观测：Max|I|={(maxAbs.HasValue ? maxAbs.Value.ToString("0.###", CultureInfo.InvariantCulture) : "--")}A, 判据阈值={NonZeroThresholdA:0.###}A => {(pass ? "PASS" : "FAIL")}");
            return (pass, maxAbs, last);
        }

        private async Task<double?> MeasurePhaseCurrentOnceAsync(MotorPhase phase, CancellationToken token)
        {
            await EnsureArincRxReadyAsync(token).ConfigureAwait(false);
            StartArincRxLoopIfNeeded(token);

            var deadline = DateTime.UtcNow.AddMilliseconds(500);
            var startCount = GetMotorCurrentCount(phase);
            while (!token.IsCancellationRequested && DateTime.UtcNow <= deadline)
            {
                token.ThrowIfCancellationRequested();

                var v = TryGetLatestMotorCurrent(phase, out var countNow);
                if (v.HasValue && countNow > startCount)
                {
                    Log($"[{DateTime.Now:HH:mm:ss}] {(phase == MotorPhase.PhaseA ? "A相" : "B相")} 电流={v.Value:0.######}A");
                    return v.Value;
                }

                await Task.Delay(ArincPollIntervalMs, token).ConfigureAwait(false);
            }

            var last = TryGetLatestMotorCurrent(phase, out _);
            if (last.HasValue)
                Log($"[{DateTime.Now:HH:mm:ss}] {(phase == MotorPhase.PhaseA ? "A相" : "B相")} 电流={last.Value:0.######}A(超时取最近值)");
            return last;
        }

        private void ResetMotorCurrentCache()
        {
            lock (_motorCurrentLock)
            {
                _phaseACurrentCache = new System.Collections.Generic.List<double>();
                _phaseBCurrentCache = new System.Collections.Generic.List<double>();
            }
        }

        private int GetMotorCurrentCount(MotorPhase phase)
        {
            lock (_motorCurrentLock)
            {
                return phase == MotorPhase.PhaseA ? _phaseACurrentCache.Count : _phaseBCurrentCache.Count;
            }
        }

        private double? TryGetLatestMotorCurrent(MotorPhase phase, out int count)
        {
            lock (_motorCurrentLock)
            {
                var list = phase == MotorPhase.PhaseA ? _phaseACurrentCache : _phaseBCurrentCache;
                count = list.Count;
                if (count <= 0)
                    return null;
                return list[count - 1];
            }
        }

        private void StartArincRxLoopIfNeeded(CancellationToken token)
        {
            if (_arinc == null || !_arinc.IsConnected || !_arincRxOpened)
                return;

            if (_arincRxLoopTask != null && !_arincRxLoopTask.IsCompleted)
                return;

            _arincRxLoopTask = Task.Run(() => ArincRxLoopAsync(token), token);
        }

        private async Task ArincRxLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (_arinc == null || !_arinc.IsConnected || !_arincRxOpened)
                    {
                        await Task.Delay(ArincPollIntervalMs, token).ConfigureAwait(false);
                        continue;
                    }

                    var words = await _arinc.ReadRxWordsAsync(ArincRxChannelIndex, maxCount: 256, enableTimeTag: false, enableRateAdaption: false, cancellationToken: token).ConfigureAwait(false);
                    if (words.Count > 0)
                    {
                        for (int i = words.Count - 1; i >= 0; i--)
                        {
                            ParseAndCacheMotorCurrent(words[i].Data429);
                        }
                    }

                    await Task.Delay(ArincPollIntervalMs, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    try { await Task.Delay(80, token).ConfigureAwait(false); } catch { break; }
                }
            }
        }

        private void ParseAndCacheMotorCurrent(uint rawWord)
        {
            if (_arinc == null)
                return;

            _arinc.ParseRawWord(rawWord, out var labelRaw, out var sdi, out var data19, out var ssm);

            if (sdi != ArincExpectedSdi)
                return;

            if (!_arinc.VerifyOddParity(rawWord))
                return;

            var isA = labelRaw == MotorPhaseACurrentLabelRaw;
            var isB = labelRaw == MotorPhaseBCurrentLabelRaw;
            if (!isA && !isB)
                return;

            var valueA = DecodeMotorCurrentA(data19);

            lock (_motorCurrentLock)
            {
                const int maxKeep = 128;
                if (isA)
                {
                    _phaseACurrentCache.Add(valueA);
                    if (_phaseACurrentCache.Count > maxKeep)
                        _phaseACurrentCache.RemoveRange(0, _phaseACurrentCache.Count - maxKeep);
                }

                if (isB)
                {
                    _phaseBCurrentCache.Add(valueA);
                    if (_phaseBCurrentCache.Count > maxKeep)
                        _phaseBCurrentCache.RemoveRange(0, _phaseBCurrentCache.Count - maxKeep);
                }
            }
        }

        private static double DecodeMotorCurrentA(uint data19)
        {
            var bits = (data19 >> MotorCurrentBitOffsetInData19) & ((1u << MotorCurrentBitLength) - 1u);
            var signed = SignExtendTwosComplement(bits, MotorCurrentBitLength);
            return signed * MotorCurrentResolutionA;
        }

        private static int SignExtendTwosComplement(uint raw, int bitLength)
        {
            var signBit = 1 << (bitLength - 1);
            var value = (int)raw;
            if ((value & signBit) != 0)
                value -= 1 << bitLength;
            return value;
        }

        private async Task EnsureArincTxReadyAsync(CancellationToken token)
        {
            if (_arinc != null && _arinc.IsConnected && _arincTxOpened)
                return;

            if (_arinc == null)
            {
                DeviceBase dev = null;
                try
                {
                    var pxi = ContainerLocator.Container.Resolve<IPxiChassisService>();
                    var chassisList = pxi?.GetAllChassis();
                    if (chassisList != null)
                    {
                        foreach (var chassis in chassisList)
                        {
                            if (chassis?.Devices == null) continue;
                            dev = chassis.Devices.FirstOrDefault(d =>
                                (d?.Model?.IndexOf("4227", StringComparison.OrdinalIgnoreCase) >= 0) ||
                                (d?.Model?.IndexOf("4229", StringComparison.OrdinalIgnoreCase) >= 0) ||
                                (d?.Model?.IndexOf("ARINC", StringComparison.OrdinalIgnoreCase) >= 0) ||
                                (d?.Model?.IndexOf("429", StringComparison.OrdinalIgnoreCase) >= 0) ||
                                (d?.Name?.IndexOf("4227", StringComparison.OrdinalIgnoreCase) >= 0) ||
                                (d?.Name?.IndexOf("4229", StringComparison.OrdinalIgnoreCase) >= 0) ||
                                (d?.Name?.IndexOf("429", StringComparison.OrdinalIgnoreCase) >= 0));
                            if (dev != null)
                                break;
                        }
                    }
                }
                catch
                {
                }

                if (dev != null)
                    _arinc = new Art4229Api(dev, deviceIndex: 0);
            }

            if (_arinc == null)
                throw new InvalidOperationException("未找到ART4229(ARINC429)板卡");

            if (!_arinc.IsConnected)
                await _arinc.ConnectAsync(token).ConfigureAwait(false);

            await _arinc.OpenTxAsync(ArincTxChannelIndex, token).ConfigureAwait(false);
            await _arinc.ConfigureTxAsync(
                ArincTxChannelIndex,
                rate: DefaultArincRate,
                mode: Art4229TxMode.Single,
                parity: Art4229Parity.Odd,
                wordFormat: Art4229WordFormat.Standard429,
                cancellationToken: token).ConfigureAwait(false);

            _arincTxOpened = true;
        }

        private async Task EnsureArincRxReadyAsync(CancellationToken token)
        {
            if (_arinc != null && _arinc.IsConnected && _arincRxOpened)
                return;

            await EnsureArincTxReadyAsync(token).ConfigureAwait(false);
            if (_arinc == null || !_arinc.IsConnected)
                throw new InvalidOperationException("ARINC429未连接");

            await _arinc.OpenRxAsync(ArincRxChannelIndex, token).ConfigureAwait(false);
            await _arinc.ConfigureRxAsync(
                ArincRxChannelIndex,
                rate: DefaultArincRate,
                parity: Art4229Parity.Odd,
                wordFormat: Art4229WordFormat.Standard429,
                enableInterrupt: false,
                interruptDepth: 512,
                enableTimeTag: false,
                cancellationToken: token).ConfigureAwait(false);

            await _arinc.StartRxAsync(ArincRxChannelIndex, token).ConfigureAwait(false);
            _arincRxOpened = true;
        }

        private static byte ReverseLabelBits(byte label)
        {
            byte reversed = 0;
            for (var i = 0; i < 8; i++)
            {
                if ((label & (1 << i)) != 0)
                    reversed |= (byte)(1 << (7 - i));
            }
            return reversed;
        }

        private byte GetAtpLabelForTx()
        {
            if (_arinc != null)
                return _arinc.ReverseLabel(AtpLabelOctal174Dec);

            return ReverseLabelBits(AtpLabelOctal174Dec);
        }

        private uint BuildAtpEnterWord(out byte txLabel)
        {
            txLabel = GetAtpLabelForTx();
            return ((AtpSsmDataSdi & 0x00FFFFFFu) << 8) | txLabel;
        }

        private async Task EnsureAtpModeAsync(CancellationToken token)
        {
            if (_arinc == null || !_arinc.IsConnected)
                return;

            if (!_atpTxOpened)
            {
                await _arinc.OpenTxAsync(ArincTxChannelIndex, token).ConfigureAwait(false);
                await _arinc.ConfigureTxAsync(
                    ArincTxChannelIndex,
                    rate: DefaultArincRate,
                    mode: Art4229TxMode.Single,
                    parity: Art4229Parity.None,
                    wordFormat: Art4229WordFormat.Standard429,
                    cancellationToken: token).ConfigureAwait(false);
                _atpTxOpened = true;
                try { await Task.Delay(ArincAfterTxOpenSettleDelayMs, token).ConfigureAwait(false); } catch { }
            }

            if (_atpModeEntered)
                return;

            var word = BuildAtpEnterWord(out var txLabel);
            Log($"[{DateTime.Now:HH:mm:ss}] 测试信息-ATP发送准备: TX通道{ArincTxChannelIndex}, SSM/Data/SDI=0x{AtpSsmDataSdi:X6}, Label(oct174)=0x{AtpLabelOctal174Dec:X2}, Label反转后=0x{txLabel:X2}, Word=0x{word:X8}");
            try
            {
                await _arinc.SendWordsSingleAsync(ArincTxChannelIndex, new[] { word }, Art4229Parity.None, token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log($"[{DateTime.Now:HH:mm:ss}] 测试信息-ATP发送失败: TX通道{ArincTxChannelIndex}, Word=0x{word:X8}, 异常={ex.Message}");
                throw;
            }
            Log($"[{DateTime.Now:HH:mm:ss}] 测试信息-ATP发送完成: TX通道{ArincTxChannelIndex}, Word=0x{word:X8}");
            _atpModeEntered = true;
        }

        private uint BuildFrequencyWord()
        {
            uint data19 = 0;

            var freq = Math.Max(0, Math.Min(1000, StepFrequencyHz));
            data19 = SetBits(data19, FreqBitOffsetInData19, FreqBitLength, (uint)freq);

            return _arinc.BuildRawWord(Label173Raw, ControlSdi, data19, ssm: 10, applyOddParity: false);
        }

        private uint BuildEnableDirectionWord()
        {
            uint data19 = 0;

            data19 = SetBits(data19, EnableBitOffsetInData19, 1, IsMotorEnabled ? 1u : 0u);
            data19 = SetBits(data19, DirectionBitOffsetInData19, 1, IsReverse ? 1u : 0u);
            data19 = SetBits(data19, AtpBitOffsetInData19, 1, 1u);

            var label174 = GetAtpLabelForTx();
            return _arinc.BuildRawWord(label174, ControlSdi, data19, ssm: 10, applyOddParity: false);
        }

        private static uint SetBits(uint data19, int bitOffset, int bitLength, uint value)
        {
            if (bitOffset < 0 || bitOffset > 18)
                return data19;
            if (bitLength <= 0)
                return data19;
            if (bitOffset + bitLength > 19)
                bitLength = 19 - bitOffset;

            uint mask = (uint)((1u << bitLength) - 1u);
            data19 &= ~(mask << bitOffset);
            data19 |= (value & mask) << bitOffset;
            return data19 & 0x7FFFF;
        }

        private async Task StopAsync()
        {
            await _opLock.WaitAsync().ConfigureAwait(false);
            try
            {
                await StopInternalAsync().ConfigureAwait(false);
            }
            finally
            {
                _opLock.Release();
            }
        }

        private async Task StopInternalAsync()
        {
            try { _cts?.Cancel(); } catch { }

            IsManualTestRunning = false;
            IsAutoTestRunning = false;
            IsBusy = false;

            if (_arinc != null)
            {
                try { await _arinc.StopRxAsync(ArincRxChannelIndex, CancellationToken.None).ConfigureAwait(false); } catch { }
                try { await _arinc.CloseRxAsync(ArincRxChannelIndex, CancellationToken.None).ConfigureAwait(false); } catch { }
                try { await _arinc.CloseTxAsync(ArincTxChannelIndex, CancellationToken.None).ConfigureAwait(false); } catch { }
                try { await _arinc.DisconnectAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                try { await _arinc.DisposeAsync().ConfigureAwait(false); } catch { }
                _arinc = null;
                _arincTxOpened = false;
                _arincRxOpened = false;
                _arincRxLoopTask = null;
                _atpTxOpened = false;
                _atpModeEntered = false;
            }

            ResetMotorCurrentCache();

            await CleanupPowerAsync().ConfigureAwait(false);
        }

        private async Task EnsurePowerAsync(double voltageV, CancellationToken token)
        {
            // 192.168.1.15 CH1 不再由本测试控制上电，由总上电统一管理
            await Task.Delay(100, token).ConfigureAwait(false);

            Log($"[{DateTime.Now:HH:mm:ss}] 已供电：{PowerSupplyIpAddress} CH1 {voltageV:0.###}V {InputCurrentA:0.###}A");
        }

        private async Task CleanupPowerAsync()
        {
            try
            {
                if (_power == null)
                    return;

                // 192.168.1.15 CH1 不再由本测试控制下电
                try { await _power.DisconnectAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                try { await _power.DisposeAsync().ConfigureAwait(false); } catch { }
            }
            catch
            {
            }
            finally
            {
                _power = null;
            }
        }

        private void Log(string msg)
        {
            if (string.IsNullOrWhiteSpace(msg))
                return;

            try
            {
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher != null && !dispatcher.CheckAccess())
                {
                    dispatcher.BeginInvoke(new Action(() => Log(msg)));
                    return;
                }
            }
            catch
            {
            }

            try { Logs.Add(msg); } catch { }
        }

        public void Dispose()
        {
            try { _cts?.Cancel(); } catch { }
            try { _cts?.Dispose(); } catch { }
            try { StopAsync().GetAwaiter().GetResult(); } catch { }
        }

        private enum MotorPhase
        {
            PhaseA,
            PhaseB
        }
    }

    public sealed class TcvMotorStepResultViewModel : BindableBase
    {
        private string _sequence;
        private string _directionText;
        private int _stepFrequencyHz;
        private double? _phaseACurrentA;
        private double? _phaseBCurrentA;
        private double? _phaseAMaxAbsCurrentA;
        private double? _phaseBMaxAbsCurrentA;
        private string _result = "--";

        public string Sequence
        {
            get => _sequence;
            set => SetProperty(ref _sequence, value);
        }

        public string DirectionText
        {
            get => _directionText;
            set
            {
                if (SetProperty(ref _directionText, value))
                {
                    RaisePropertyChanged(nameof(TestItemText));
                }
            }
        }

        public int StepFrequencyHz
        {
            get => _stepFrequencyHz;
            set
            {
                if (SetProperty(ref _stepFrequencyHz, value))
                {
                    RaisePropertyChanged(nameof(TestItemText));
                }
            }
        }

        public double? PhaseACurrentA
        {
            get => _phaseACurrentA;
            set
            {
                if (SetProperty(ref _phaseACurrentA, value))
                {
                    RaisePropertyChanged(nameof(PhaseACurrentText));
                }
            }
        }

        public double? PhaseBCurrentA
        {
            get => _phaseBCurrentA;
            set
            {
                if (SetProperty(ref _phaseBCurrentA, value))
                {
                    RaisePropertyChanged(nameof(PhaseBCurrentText));
                }
            }
        }

        public double? PhaseAMaxAbsCurrentA
        {
            get => _phaseAMaxAbsCurrentA;
            set
            {
                if (SetProperty(ref _phaseAMaxAbsCurrentA, value))
                {
                    RaisePropertyChanged(nameof(PhaseAMaxAbsCurrentText));
                }
            }
        }

        public double? PhaseBMaxAbsCurrentA
        {
            get => _phaseBMaxAbsCurrentA;
            set
            {
                if (SetProperty(ref _phaseBMaxAbsCurrentA, value))
                {
                    RaisePropertyChanged(nameof(PhaseBMaxAbsCurrentText));
                }
            }
        }

        public string Result
        {
            get => _result;
            set => SetProperty(ref _result, value);
        }

        public string PhaseACurrentText => PhaseACurrentA.HasValue ? PhaseACurrentA.Value.ToString("0.######", CultureInfo.InvariantCulture) : "--";
        public string PhaseBCurrentText => PhaseBCurrentA.HasValue ? PhaseBCurrentA.Value.ToString("0.######", CultureInfo.InvariantCulture) : "--";
        public string PhaseAMaxAbsCurrentText => PhaseAMaxAbsCurrentA.HasValue ? PhaseAMaxAbsCurrentA.Value.ToString("0.######", CultureInfo.InvariantCulture) : "--";
        public string PhaseBMaxAbsCurrentText => PhaseBMaxAbsCurrentA.HasValue ? PhaseBMaxAbsCurrentA.Value.ToString("0.######", CultureInfo.InvariantCulture) : "--";

        public string TestItemText => string.IsNullOrWhiteSpace(DirectionText) ? "--" : $"{DirectionText}{StepFrequencyHz}Hz";
    }
}
