using Prism.Commands;
using Prism.Mvvm;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using MeasureControl.Views.Dialogs;
using MeasureControl.Drivers;
using MeasureControl.Events;
using MeasureControl.Models.Devices;
using MeasureControl.Models.Devices.DeviceCategories;
using MeasureControl.Services;
using MeasureControl.Services.HardwareApis;
using Prism.Events;
using Prism.Ioc;
using MeasureControl.Models;

namespace MeasureControl.ViewModels.SingleBoardTest.HydraulicController
{
    /// <summary>
    /// HC_6_9 测试项：输出有效性测试（Output Valid 控制验证）
    /// 测试目的：验证液压控制器通过 ARINC429 接收"输出有效"指令后，
    ///          能够正确控制输出针脚（Pin9-15）的开关状态。
    /// 测试方法：
    /// 1) 给被测板供电 28V。
    /// 2) 开路测试：不发送指令，测量 Pin9-15 对地阻抗（应为高阻，>100kΩ）。
    /// 3) 通路测试：通过 ARINC429 发送"输出有效"指令（Label=65oct），
    ///    再测量 Pin9-15 对地阻抗（应为低阻，<10Ω）.
    /// 4) 所有针脚都满足判据则“合格”。
    /// </summary>
    public class HC_6_9ViewModel : BindableBase
    {
        // 电源配置（给被测板供电）
        private const string PowerSupply28VAddress = "192.168.1.15";
        private const string PowerSupplyAAddress = "192.168.1.16";
        private const string PowerSupplyBAddress = "192.168.1.17";
        private const double Input28VoltageV = 28.0;
        private const double Input28CurrentA = 1.0;
        private const double MeasureChannelCurrentA = 5.0;
        private const double Input24VoltageV = 24.0;
        private const double Input24CurrentA = 1.0;
        private const double InputVoltageV = 5.0;
        private const double InputCurrentA = 1;

        // ARINC429 发送配置
        private const int TxChannelIndex = 0;
        private const int TxChannelIndex2 = 1;
        private const int RxChannelIndex = 2;
        private const double ArincRate = 100000.0;
        private const uint ArincPeriodMs = 100;
        private const uint SpecialArincPeriodMs = 15;

        private const byte CloseFeedbackLabelDec = 53;
        private const byte CloseFeedbackSdi = 0;

        private static readonly HashSet<uint> FastPeriodWords = new HashSet<uint>
        {
            //0x800040BC,
            //0x800004BC,
            //0x000042BC,
        };

        private static readonly string[] CloseSignalAoChannels = { "AO2", "AO3", "AO4", "AO5" };
        private static readonly IReadOnlyList<int> CloseMeasurementPins = new[] { 9, 10, 11, 12, 13, 14, 15 };
        private static readonly IReadOnlyList<int> CloseFeedbackBits = new[] { 10, 11, 12, 13, 14, 15, 16 };
        private static readonly IReadOnlyList<int> AllCloseDoIndices = new[] { 3, 21 };
        private static readonly IReadOnlyList<int> AllCloseRelayChannels = new[] { 1, 6 };
        private static readonly IReadOnlyList<int> CloseGroupPins1 = new[] { 9, 10, 11, 12 };
        private static readonly IReadOnlyList<int> CloseGroupPins2 = new[] { 13, 14, 15 };
        private static readonly IReadOnlyList<int> CloseGroup1ExpectedBits = new[] { 13, 14, 15, 16 };
        private static readonly IReadOnlyList<int> CloseGroup2ExpectedBits = new[] { 10, 11, 12, 15 };
        private const int LvdtSlotIndex = 2;
        private const int LvdtChannel1 = 1;
        private const int LvdtChannel2 = 2;
        private const double SimulationSumVrms = 6.0;
        private const double BleedResistanceOhm = 1155.4;
        private const double BleedLvdtVaV = 2.4;
        private const double BleedLvdtVbV = 3.6;
        private const string LvdtVaSuffix = "_VA";
        private const string LvdtVbSuffix = "_VB";
        private const double CloseResultOffsetOhm = 30.0;
        private const int CloseSignalSettleMs = 180;
        private const int CloseSignalStepDelayMs = 1000;
        private const int CloseFeedbackDebugLogIntervalMs = 2000;
        private const int CloseGroup1TimeoutSeconds = 30;
        private const int CloseGroup2TimeoutSeconds = 170;
        private const double OpenCircuitCurrentThresholdA = 1e-6;

        // 阻抗判据
        private const double OpenPassThresholdOhm = 100_000.0;   // 开路阻抗阈值：>100kΩ 为合格
        private const double ClosePassThresholdOhm = 10.0;       // 通路阻抗阈值：<10Ω 为合格

        private readonly SemaphoreSlim _measureLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _relayLock = new SemaphoreSlim(1, 1);
        private readonly IPxiChassisService _pxiChassisService;
        private readonly ISingleBoardTestContextService _singleBoardTestContext;
        private readonly IBoardPowerService _boardPowerService;

        private const string TestItemName = "离散量输出测试";

        private CancellationTokenSource _manualCts;
        private CancellationTokenSource _autoCts;

        private IPowerSupplyApi _power28;
        private IPowerSupplyApi _powerA;
        private IPowerSupplyApi _powerB;
        private IArt4229Api _arinc;
        private IJy7131Api _jy7131;
        private IMtx532Api _mtx532;
        private IPxi4087LvdtApi _lvdt;
        private ACTS6010Driver _res;
        private CancellationTokenSource _txChannel1LoopCts;
        private CancellationTokenSource _txChannel2LoopCts;
        private Task _txChannel1LoopTask = Task.CompletedTask;
        private Task _txChannel2LoopTask = Task.CompletedTask;

        private bool _txChannel1Opened;
        private bool _txChannel2Opened;
        private bool _rxChannelOpened;
        private bool _isPtuPrePowerPrepared;
        private bool _isCloseSharedPrepared;

        private bool _isManualTestRunning;
        private bool _isAutoTestRunning;
        private bool _isManualTestInitializing;
        private bool _isAutoTestInitializing;
        private bool _isManualTestStopping;
        private bool _isAutoTestStopping;
        private bool _canMeasure;

        private bool _measuredOpen;
        private bool _measuredClose;
        private bool _manualAborted;

        private string _lastTestTime = "--";
        private string _lastTestResult = "--";
        private string _previousTestTime = "--";
        private string _previousTestResult = "--";
        private string _currentTestResult = "--";

        private string _openPin9Text = "--";
        private string _openPin10Text = "--";
        private string _openPin11Text = "--";
        private string _openPin12Text = "--";
        private string _openPin13Text = "--";
        private string _openPin14Text = "--";
        private string _openPin15Text = "--";

        private string _closePin9Text = "--";
        private string _closePin10Text = "--";
        private string _closePin11Text = "--";
        private string _closePin12Text = "--";
        private string _closePin13Text = "--";
        private string _closePin14Text = "--";
        private string _closePin15Text = "--";

        private readonly Dictionary<int, double?> _openValuesByPin = new Dictionary<int, double?>();
        private readonly Dictionary<int, double?> _closeValuesByPin = new Dictionary<int, double?>();

        public HC_6_9ViewModel(IPxiChassisService pxiChassisService, ISingleBoardTestContextService singleBoardTestContext, IBoardPowerService hydraulicPowerService)
        {
            _pxiChassisService = pxiChassisService;
            _singleBoardTestContext = singleBoardTestContext;
            _boardPowerService = hydraulicPowerService;

            ManualTestCommand = new DelegateCommand(async () => await OnManualTestAsync());
            AutoTestCommand = new DelegateCommand(async () => await OnAutoTestAsync());
            MeasureOpenCommand = new DelegateCommand(async () => await OnMeasureOpenAsync(), () => CanMeasureOpen);
            MeasureCloseCommand = new DelegateCommand(async () => await OnMeasureCloseAsync(), () => CanMeasureClose);
            ClearLogCommand = new DelegateCommand(() => Logs.Clear());

            LoadLastTestResultFromProject();
        }

        private void LoadLastTestResultFromProject()
        {
            var testItemNode = _singleBoardTestContext?.GetCurrentTestItemNode(TestItemName);
            if (testItemNode != null)
            {
                if (!string.IsNullOrWhiteSpace(testItemNode.LastTestTime))
                {
                    _previousTestTime = testItemNode.LastTestTime;
                    RaisePropertyChanged(nameof(PreviousTestTime));
                }
                if (!string.IsNullOrWhiteSpace(testItemNode.LastTestResult))
                {
                    _previousTestResult = testItemNode.LastTestResult;
                    RaisePropertyChanged(nameof(PreviousTestResult));
                }
            }
        }

        private async Task CleanupRelayAsync()
        {
            try
            {
                await EnsureRelay485Async(on: false, cancellationToken: CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
            }

            try
            {
                if (_jy7131 != null)
                {
                    try { await _jy7131.StopAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _jy7131.DisconnectAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _jy7131.DisposeAsync().ConfigureAwait(false); } catch { }
                }
            }
            finally
            {
                _jy7131 = null;
            }
        }

        private void SaveTestResultToProject()
        {
            var testItemNode = _singleBoardTestContext?.GetCurrentTestItemNode(TestItemName);
            if (testItemNode != null)
            {
                testItemNode.LastTestTime = PreviousTestTime;
                testItemNode.LastTestResult = PreviousTestResult;

                var eventAggregator = ContainerLocator.Container?.Resolve(typeof(IEventAggregator)) as IEventAggregator;
                eventAggregator?.GetEvent<ProjectModifiedEvent>()?.Publish(new ProjectModifiedEventArgs
                {
                    ModificationType = "SingleBoardTestResult",
                    Description = $"单板测试结果已更新: {TestItemName}"
                });
            }
        }

        public string CurrentTestResult { get => _currentTestResult; private set => SetProperty(ref _currentTestResult, value); }

        public DelegateCommand ManualTestCommand { get; }
        public DelegateCommand AutoTestCommand { get; }
        public DelegateCommand MeasureOpenCommand { get; }
        public DelegateCommand MeasureCloseCommand { get; }
        public DelegateCommand ClearLogCommand { get; }

        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();

        public bool IsManualTestBusy => IsManualTestInitializing || IsManualTestStopping;

        public bool IsAutoTestBusy => IsAutoTestInitializing || IsAutoTestStopping;

        public bool IsManualTestInitializing
        {
            get => _isManualTestInitializing;
            private set
            {
                if (SetProperty(ref _isManualTestInitializing, value))
                {
                    RaisePropertyChanged(nameof(IsManualTestBusy));
                    RaisePropertyChanged(nameof(CanStartManualTest));
                    RaisePropertyChanged(nameof(CanStartAutoTest));
                }
            }
        }

        public bool IsAutoTestInitializing
        {
            get => _isAutoTestInitializing;
            private set
            {
                if (SetProperty(ref _isAutoTestInitializing, value))
                {
                    RaisePropertyChanged(nameof(IsAutoTestBusy));
                    RaisePropertyChanged(nameof(CanStartManualTest));
                    RaisePropertyChanged(nameof(CanStartAutoTest));
                }
            }
        }

        public bool IsManualTestStopping
        {
            get => _isManualTestStopping;
            private set
            {
                if (SetProperty(ref _isManualTestStopping, value))
                {
                    RaisePropertyChanged(nameof(IsManualTestBusy));
                    RaisePropertyChanged(nameof(CanStartManualTest));
                    RaisePropertyChanged(nameof(CanStartAutoTest));
                }
            }
        }

        public bool IsAutoTestStopping
        {
            get => _isAutoTestStopping;
            private set
            {
                if (SetProperty(ref _isAutoTestStopping, value))
                {
                    RaisePropertyChanged(nameof(IsAutoTestBusy));
                    RaisePropertyChanged(nameof(CanStartManualTest));
                    RaisePropertyChanged(nameof(CanStartAutoTest));
                }
            }
        }

        public bool IsManualTestRunning
        {
            get => _isManualTestRunning;
            set
            {
                if (SetProperty(ref _isManualTestRunning, value))
                {
                    RaisePropertyChanged(nameof(CanMeasureOpen));
                    RaisePropertyChanged(nameof(CanMeasureClose));
                    RaisePropertyChanged(nameof(CanStartManualTest));
                    RaisePropertyChanged(nameof(CanStartAutoTest));
                    MeasureOpenCommand?.RaiseCanExecuteChanged();
                    MeasureCloseCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        public bool IsAutoTestRunning
        {
            get => _isAutoTestRunning;
            set
            {
                if (SetProperty(ref _isAutoTestRunning, value))
                {
                    RaisePropertyChanged(nameof(CanStartManualTest));
                    RaisePropertyChanged(nameof(CanStartAutoTest));
                }
            }
        }

        public bool CanMeasure
        {
            get => _canMeasure;
            private set
            {
                if (SetProperty(ref _canMeasure, value))
                {
                    RaisePropertyChanged(nameof(CanMeasureOpen));
                    RaisePropertyChanged(nameof(CanMeasureClose));
                    MeasureOpenCommand?.RaiseCanExecuteChanged();
                    MeasureCloseCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        public bool CanMeasureOpen => IsManualTestRunning && CanMeasure && _measuredClose && !_measuredOpen;
        public bool CanMeasureClose => IsManualTestRunning && CanMeasure && !_measuredClose;
        public bool CanStartManualTest => !IsManualTestBusy && !IsAutoTestBusy && !IsAutoTestRunning;
        public bool CanStartAutoTest => !IsManualTestBusy && !IsAutoTestBusy && !IsManualTestRunning;

        /// <summary>
        /// 整板串行自动测试入口。
        /// 由外部(整板自动测试)调用，支持 await 等待完成，并通过 CancellationToken 实现“立即停止当前测量”。
        /// 返回值仅为“合格/不合格”。
        /// </summary>
        public async Task<string> RunOnceAsync(CancellationToken cancellationToken)
        {
            if (IsAutoTestRunning)
            {
                await StopAutoTestAsync().ConfigureAwait(false);
            }

            if (IsManualTestRunning)
            {
                await StopManualTestAsync().ConfigureAwait(false);
            }

            _autoCts?.Cancel();
            _autoCts?.Dispose();
            _autoCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            try
            {
                return await ExecuteAutoTestAsync(_autoCts.Token).ConfigureAwait(false);
            }
            finally
            {
                _autoCts?.Dispose();
                _autoCts = null;
            }
        }

        public string LastTestTime { get => _lastTestTime; private set => SetProperty(ref _lastTestTime, value); }
        public string LastTestResult { get => _lastTestResult; private set => SetProperty(ref _lastTestResult, value); }
        public string PreviousTestTime { get => _previousTestTime; private set => SetProperty(ref _previousTestTime, value); }
        public string PreviousTestResult { get => _previousTestResult; private set => SetProperty(ref _previousTestResult, value); }

        public string OpenPin9Text { get => _openPin9Text; private set => SetProperty(ref _openPin9Text, value); }
        public string OpenPin10Text { get => _openPin10Text; private set => SetProperty(ref _openPin10Text, value); }
        public string OpenPin11Text { get => _openPin11Text; private set => SetProperty(ref _openPin11Text, value); }
        public string OpenPin12Text { get => _openPin12Text; private set => SetProperty(ref _openPin12Text, value); }
        public string OpenPin13Text { get => _openPin13Text; private set => SetProperty(ref _openPin13Text, value); }
        public string OpenPin14Text { get => _openPin14Text; private set => SetProperty(ref _openPin14Text, value); }
        public string OpenPin15Text { get => _openPin15Text; private set => SetProperty(ref _openPin15Text, value); }

        public string ClosePin9Text { get => _closePin9Text; private set => SetProperty(ref _closePin9Text, value); }
        public string ClosePin10Text { get => _closePin10Text; private set => SetProperty(ref _closePin10Text, value); }
        public string ClosePin11Text { get => _closePin11Text; private set => SetProperty(ref _closePin11Text, value); }
        public string ClosePin12Text { get => _closePin12Text; private set => SetProperty(ref _closePin12Text, value); }
        public string ClosePin13Text { get => _closePin13Text; private set => SetProperty(ref _closePin13Text, value); }
        public string ClosePin14Text { get => _closePin14Text; private set => SetProperty(ref _closePin14Text, value); }
        public string ClosePin15Text { get => _closePin15Text; private set => SetProperty(ref _closePin15Text, value); }

        public bool IsOpenPin9Pass => IsOpenPinPass(9);
        public bool IsOpenPin10Pass => IsOpenPinPass(10);
        public bool IsOpenPin11Pass => IsOpenPinPass(11);
        public bool IsOpenPin12Pass => IsOpenPinPass(12);
        public bool IsOpenPin13Pass => IsOpenPinPass(13);
        public bool IsOpenPin14Pass => IsOpenPinPass(14);
        public bool IsOpenPin15Pass => IsOpenPinPass(15);

        public bool IsClosePin9Pass => IsClosePinPass(9);
        public bool IsClosePin10Pass => IsClosePinPass(10);
        public bool IsClosePin11Pass => IsClosePinPass(11);
        public bool IsClosePin12Pass => IsClosePinPass(12);
        public bool IsClosePin13Pass => IsClosePinPass(13);
        public bool IsClosePin14Pass => IsClosePinPass(14);
        public bool IsClosePin15Pass => IsClosePinPass(15);

        /// <summary>
        /// 手动测试流程
        /// 进入手动模式后，先初始化万用表和电源，
        /// 然后由用户先点击"测量通路"，再点击"测量开路"按钮执行测量。
        /// </summary>
        private async Task OnManualTestAsync()
        {
            if (IsManualTestStopping)
            {
                return;
            }

            if (IsManualTestRunning || IsManualTestInitializing)
            {
                await StopManualTestAsync().ConfigureAwait(false);
                return;
            }

            if (IsAutoTestRunning)
            {
                await StopAutoTestAsync().ConfigureAwait(false);
            }

            IsManualTestInitializing = true;
            IsManualTestStopping = false;
            CurrentTestResult = "--";
            CanMeasure = false;
            _manualAborted = false;
            _measuredOpen = false;
            _measuredClose = false;

            ResetAllDisplays();
            _openValuesByPin.Clear();
            _closeValuesByPin.Clear();

            _manualCts?.Cancel();
            _manualCts?.Dispose();
            _manualCts = new CancellationTokenSource();

            if (_boardPowerService?.IsPowered == true)
            {
                MessageBoxResult cycleResult = MessageBoxResult.No;
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    cycleResult = MessageBox.Show(
                        "该测试项需要下电后重新上电，是否继续执行？",
                        "需要重新上电",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);
                });
                if (cycleResult != MessageBoxResult.Yes)
                {
                    IsManualTestInitializing = false;
                    return;
                }
            }
            else
            {
                var (confirmed, _) = PowerOnPromptDialog.ShowPrompt("液压单板", showVoltage: false);
                if (!confirmed)
                {
                    IsManualTestInitializing = false;
                    return;
                }
            }

            Log("开始手动测试");
            Log($"判据: 开路>={OpenPassThresholdOhm:0}Ω, 通路<={ClosePassThresholdOhm:0}Ω");

            try
            {
                await EnsureRelay485Async(on: true, cancellationToken: _manualCts.Token).ConfigureAwait(false);

                await EnsureArincTxAsync(_manualCts.Token).ConfigureAwait(false);

                await EnsureMtx532Async(_manualCts.Token).ConfigureAwait(false);

                await EnsureResistanceAsync(_manualCts.Token).ConfigureAwait(false);

                await EnsureLvdtAsync(_manualCts.Token).ConfigureAwait(false);

                await EnsureArincRxAsync(_manualCts.Token).ConfigureAwait(false);

                await PrepareCloseTestPowerPreconditionsAsync(_manualCts.Token).ConfigureAwait(false);
                await EnsurePowerAsync(_manualCts.Token).ConfigureAwait(false);

                IsManualTestInitializing = false;
                IsManualTestRunning = true;
                CanMeasure = true;
                Log("手动测试初始化完成，可以开始通路测试");
            }
            catch (Exception ex)
            {
                await AbortManualTestAsync($"手动测试初始化失败，中止: {ex.Message}").ConfigureAwait(false);
            }
        }

        /// <summary>
        /// 自动测试流程
        /// 自动依次执行通路测试和开路测试，所有针脚都满足判据则“合格”。
        /// </summary>
        private async Task OnAutoTestAsync()
        {
            if (IsAutoTestStopping)
            {
                return;
            }

            if (IsAutoTestRunning || IsAutoTestInitializing)
            {
                await StopAutoTestAsync().ConfigureAwait(false);
                return;
            }

            if (IsManualTestRunning)
            {
                await StopManualTestAsync().ConfigureAwait(false);
            }

            IsAutoTestInitializing = true;
            IsAutoTestStopping = false;
            CurrentTestResult = "--";
            CanMeasure = false;
            _manualAborted = false;
            _measuredOpen = false;
            _measuredClose = false;

            ResetAllDisplays();
            _openValuesByPin.Clear();
            _closeValuesByPin.Clear();

            _autoCts?.Cancel();
            _autoCts?.Dispose();
            _autoCts = new CancellationTokenSource();

            if (_boardPowerService?.IsPowered == true)
            {
                MessageBoxResult cycleResult = MessageBoxResult.No;
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    cycleResult = MessageBox.Show(
                        "该测试项需要下电后重新上电，是否继续执行？",
                        "需要重新上电",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);
                });
                if (cycleResult != MessageBoxResult.Yes)
                {
                    IsAutoTestInitializing = false;
                    _autoCts?.Dispose();
                    _autoCts = null;
                    return;
                }
            }
            else
            {
                var (confirmed, _) = PowerOnPromptDialog.ShowPrompt("液压单板", showVoltage: false);
                if (!confirmed)
                {
                    IsAutoTestInitializing = false;
                    _autoCts?.Dispose();
                    _autoCts = null;
                    return;
                }
            }

            Log("开始自动测试");
            Log("正在初始化设备...");


            try
            {
                _ = await ExecuteAutoTestAsync(_autoCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Log("自动测试已停止");
                await StopAutoTestAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log($"自动测试异常: {ex.Message}");
                await StopAutoTestAsync().ConfigureAwait(false);
            }
            finally
            {
                IsAutoTestInitializing = false;
                _autoCts?.Dispose();
                _autoCts = null;
            }
        }

        private async Task<string> ExecuteAutoTestAsync(CancellationToken cancellationToken)
        {
            CanMeasure = false;
            _manualAborted = false;
            _measuredOpen = false;
            _measuredClose = false;

            ResetAllDisplays();
            _openValuesByPin.Clear();
            _closeValuesByPin.Clear();

            Log("开始自动测试");
            Log("正在初始化设备...");
            Log($"判据: 开路>={OpenPassThresholdOhm:0}Ω, 通路<={ClosePassThresholdOhm:0}Ω");

            try
            {
                await EnsureRelay485Async(on: true, cancellationToken: cancellationToken).ConfigureAwait(false);

                await EnsureArincTxAsync(cancellationToken).ConfigureAwait(false);

                await EnsureMtx532Async(cancellationToken).ConfigureAwait(false);

                await EnsureResistanceAsync(cancellationToken).ConfigureAwait(false);

                await EnsureLvdtAsync(cancellationToken).ConfigureAwait(false);

                await EnsureArincRxAsync(cancellationToken).ConfigureAwait(false);

                await PrepareCloseTestPowerPreconditionsAsync(cancellationToken).ConfigureAwait(false);

                await EnsurePowerAsync(cancellationToken).ConfigureAwait(false);

                IsAutoTestInitializing = false;
                IsAutoTestRunning = true;

                var okClose = await MeasureCloseAsync(cancellationToken).ConfigureAwait(false);
                if (!IsAutoTestRunning)
                {
                    return CurrentTestResult ?? "--";
                }

                _measuredClose = okClose;

                await ClearAllCloseSignalOutputsAsync(CancellationToken.None).ConfigureAwait(false);
                await ReleaseCloseSharedPreconditionsAsync(CancellationToken.None).ConfigureAwait(false);

                await Task.Delay(100, cancellationToken).ConfigureAwait(false);

                var okOpen = await MeasureOpenAsync(cancellationToken).ConfigureAwait(false);
                if (!IsAutoTestRunning)
                {
                    return CurrentTestResult ?? "--";
                }

                _measuredOpen = okOpen;

                await TryFinalizeAsync().ConfigureAwait(false);
                await StopAutoTestAsync().ConfigureAwait(false);

                return LastTestResult;
            }
            catch (OperationCanceledException)
            {
                Log("自动测试已停止");
                await StopAutoTestAsync().ConfigureAwait(false);
                throw;
            }
            catch (Exception ex)
            {
                Log($"自动测试异常: {ex.Message}");
                await StopAutoTestAsync().ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>
        /// 测量开路（手动模式）
        /// 不发送 ARINC429 指令，直接测量 Pin9-15 对地阻抗。
        /// </summary>
        private async Task OnMeasureOpenAsync()
        {
            var token = _manualCts?.Token ?? CancellationToken.None;
            var ok = await MeasureOpenAsync(token).ConfigureAwait(false);

            if (!IsManualTestRunning || _manualAborted)
                return;

            if (ok)
            {
                _measuredOpen = true;
                RaisePropertyChanged(nameof(CanMeasureOpen));
                RaisePropertyChanged(nameof(CanMeasureClose));
                MeasureOpenCommand?.RaiseCanExecuteChanged();
                MeasureCloseCommand?.RaiseCanExecuteChanged();
            }

            await TryFinalizeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 测量通路（手动模式）
        /// 逐项配置信号，等待唯一反馈位后，按电源CH2电压/电流计算 Pin9-15 结果。
        /// </summary>
        private async Task OnMeasureCloseAsync()
        {
            var token = _manualCts?.Token ?? CancellationToken.None;
            var ok = await MeasureCloseAsync(token).ConfigureAwait(false);

            if (!IsManualTestRunning || _manualAborted)
                return;

            if (ok)
            {
                _measuredClose = true;
                RaisePropertyChanged(nameof(CanMeasureClose));
                MeasureCloseCommand?.RaiseCanExecuteChanged();
            }

            await TryFinalizeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 开路测试（核心测量方法）
        /// 流程：
        /// 1) 不发送 ARINC429 指令（输出应处于关闭状态）。
        /// 2) 依次测量 Pin9-15 对地阻抗（应为高阻，>100kΩ）。
        /// 3) 通过矩阵开关连接万用表到各针脚进行测量。
        /// </summary>
        private async Task<bool> MeasureOpenAsync(CancellationToken cancellationToken)
        {
            await _measureLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await ClearAllCloseSignalOutputsAsync(CancellationToken.None).ConfigureAwait(false);

                await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
                var (value, text) = await MeasureOpenResultFromPowerAsync(cancellationToken).ConfigureAwait(false);
                foreach (var pin in CloseMeasurementPins)
                {
                    _openValuesByPin[pin] = value;
                    SetOpenPinText(pin, text);
                }

                Log($"开路: PIN9~15统一结果={text}");
                return true;
            }
            catch (Exception ex)
            {
                if (IsManualTestRunning)
                {
                    await AbortManualTestAsync($"开路: 测量异常，手动测试中止: {ex.Message}").ConfigureAwait(false);
                }
                else if (IsAutoTestRunning)
                {
                    await AbortAutoTestAsync($"开路: 测量异常，自动测试中止: {ex.Message}").ConfigureAwait(false);
                }
                else
                {
                    Log($"开路: 测量异常: {ex.Message}");
                }
                return false;
            }
            finally
            {
                _measureLock.Release();
            }
        }

        /// <summary>
        /// 通路测试（核心测量方法）
        /// 流程：
        /// 1) 先建立 PTU / ABV 共享预置。
        /// 2) 按 Pin9-15 逐项配置专属信号并发送429字。
        /// 3) 等待 label=65(oct) sdi=0 的唯一目标反馈位到位。
        /// 4) 读取 192.168.1.15 CH2 电压/电流，按 V/I-30Ω 计算结果。
        /// 5) 清除当前项专属信号，保留共享预置，进入下一项。
        /// </summary>
        private async Task<bool> MeasureCloseAsync(CancellationToken cancellationToken)
        {
            await _measureLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await PrepareCloseSharedPreconditionsAsync(cancellationToken).ConfigureAwait(false);

                cancellationToken.ThrowIfCancellationRequested();
                await ExecuteCloseMeasurementGroupAsync(
                    CloseGroupPins1,
                    CloseGroup1ExpectedBits,
                    CloseGroup1TimeoutSeconds,
                    4,
                    "PIN9-12",
                    cancellationToken).ConfigureAwait(false);

                await ClearAllCloseSignalOutputsAsync(CancellationToken.None, preserveSharedPreconditions: true).ConfigureAwait(false);
                await Task.Delay(CloseSignalStepDelayMs, cancellationToken).ConfigureAwait(false);

                cancellationToken.ThrowIfCancellationRequested();
                await ExecuteCloseMeasurementGroupAsync(
                    CloseGroupPins2,
                    CloseGroup2ExpectedBits,
                    CloseGroup2TimeoutSeconds,
                    4,
                    "PIN13-15",
                    cancellationToken).ConfigureAwait(false);

                Log("通路: 测量完成");
                return true;
            }
            catch (Exception ex)
            {
                if (IsManualTestRunning)
                {
                    await AbortManualTestAsync($"通路: 测量异常，手动测试中止: {ex.Message}").ConfigureAwait(false);
                }
                else if (IsAutoTestRunning)
                {
                    await AbortAutoTestAsync($"通路: 测量异常，自动测试中止: {ex.Message}").ConfigureAwait(false);
                }
                else
                {
                    Log($"通路: 测量异常: {ex.Message}");
                }
                return false;
            }
            finally
            {
                try { await ClearAllCloseSignalOutputsAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                try { await ReleaseCloseSharedPreconditionsAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                _measureLock.Release();
            }
        }

        private void SetOpenPinText(int pin, string text)
        {
            switch (pin)
            {
                case 9: OpenPin9Text = text; break;
                case 10: OpenPin10Text = text; break;
                case 11: OpenPin11Text = text; break;
                case 12: OpenPin12Text = text; break;
                case 13: OpenPin13Text = text; break;
                case 14: OpenPin14Text = text; break;
                case 15: OpenPin15Text = text; break;
            }

            RaisePropertyChanged($"IsOpenPin{pin}Pass");
        }

        private void SetClosePinText(int pin, string text)
        {
            switch (pin)
            {
                case 9: ClosePin9Text = text; break;
                case 10: ClosePin10Text = text; break;
                case 11: ClosePin11Text = text; break;
                case 12: ClosePin12Text = text; break;
                case 13: ClosePin13Text = text; break;
                case 14: ClosePin14Text = text; break;
                case 15: ClosePin15Text = text; break;
            }

            RaisePropertyChanged($"IsClosePin{pin}Pass");
        }

        private bool IsOpenPinPass(int pin)
        {
            return _openValuesByPin.TryGetValue(pin, out var value) && value.HasValue && value.Value >= OpenPassThresholdOhm;
        }

        private bool IsClosePinPass(int pin)
        {
            return _closeValuesByPin.TryGetValue(pin, out var value) && value.HasValue && value.Value <= ClosePassThresholdOhm;
        }

        private async Task PrepareCloseTestPowerPreconditionsAsync(CancellationToken cancellationToken)
        {
            if (_isPtuPrePowerPrepared)
                return;

            await EnsureLvdtAsync(cancellationToken).ConfigureAwait(false);
            await _lvdt.SetVaVbAsync(LvdtChannel1, BleedLvdtVaV, BleedLvdtVbV, cancellationToken).ConfigureAwait(false);
            await _lvdt.StartAsync(LvdtChannel1, cancellationToken).ConfigureAwait(false);
            _isPtuPrePowerPrepared = true;
        }

        private async Task PrepareCloseSharedPreconditionsAsync(CancellationToken cancellationToken)
        {
            if (_isCloseSharedPrepared)
                return;

            await EnsureResistanceAsync(cancellationToken).ConfigureAwait(false);
            await SetResistanceChannelAsync("RO0", BleedResistanceOhm, cancellationToken).ConfigureAwait(false);
            await SetResistanceChannelAsync("RO1", BleedResistanceOhm, cancellationToken).ConfigureAwait(false);

            await EnsureLvdtAsync(cancellationToken).ConfigureAwait(false);
            await _lvdt.SetVaVbAsync(LvdtChannel1, BleedLvdtVaV, BleedLvdtVbV, cancellationToken).ConfigureAwait(false);
            await _lvdt.StartAsync(LvdtChannel1, cancellationToken).ConfigureAwait(false);
            await _lvdt.SetVaVbAsync(LvdtChannel2, BleedLvdtVaV, BleedLvdtVbV, cancellationToken).ConfigureAwait(false);
            await _lvdt.StartAsync(LvdtChannel2, cancellationToken).ConfigureAwait(false);

            _isCloseSharedPrepared = true;
        }

        private async Task ReleaseCloseSharedPreconditionsAsync(CancellationToken cancellationToken)
        {
            if (_lvdt != null)
            {
                try { await _lvdt.StopAsync(LvdtChannel1, cancellationToken).ConfigureAwait(false); } catch { }
                try { await _lvdt.SetVaVbAsync(LvdtChannel1, 0.0, 0.0, cancellationToken).ConfigureAwait(false); } catch { }
                try { await _lvdt.StopAsync(LvdtChannel2, cancellationToken).ConfigureAwait(false); } catch { }
                try { await _lvdt.SetVaVbAsync(LvdtChannel2, 0.0, 0.0, cancellationToken).ConfigureAwait(false); } catch { }
            }

            if (_res != null && _res.IsConnected)
            {
                try { await _res.SetRelayStateAsync("RO0", false, false).ConfigureAwait(false); } catch { }
                try { await _res.WriteChannelAsync("RO0", 0.0).ConfigureAwait(false); } catch { }
                try { await _res.SetRelayStateAsync("RO1", false, false).ConfigureAwait(false); } catch { }
                try { await _res.WriteChannelAsync("RO1", 0.0).ConfigureAwait(false); } catch { }
            }

            _isCloseSharedPrepared = false;
        }

        private void TryLogCloseFeedbackDebugFrame(uint rawWord, ref DateTime nextDebugLogAt)
        {
            if (_arinc == null)
                return;

            _arinc.ParseRawWord(rawWord, out var label, out var sdi, out var data19, out var ssm);
            if (_arinc.ReverseLabel(label) != CloseFeedbackLabelDec || sdi != CloseFeedbackSdi)
                return;

            var now = DateTime.UtcNow;
            if (now < nextDebugLogAt)
                return;

            var data19Binary = Convert.ToString(data19, 2).PadLeft(19, '0');
            nextDebugLogAt = now.AddMilliseconds(CloseFeedbackDebugLogIntervalMs);
        }

        private async Task ExecuteCloseMeasurementGroupAsync(IReadOnlyList<int> pins, IReadOnlyList<int> expectedBits, int timeoutSeconds, int divisor, string groupName, CancellationToken cancellationToken)
        {
            if (pins == null || pins.Count == 0)
                throw new ArgumentException("pins不能为空", nameof(pins));

            Log($"通路: 开始执行{groupName}批量闭合测试");
            await ConfigureCloseSignalForPinsAsync(pins, cancellationToken).ConfigureAwait(false);
            await Task.Delay(CloseSignalSettleMs, cancellationToken).ConfigureAwait(false);

            var ready = await WaitForCloseSignalReadyAsync(groupName, expectedBits, timeoutSeconds, cancellationToken).ConfigureAwait(false);
            if (!ready)
            {
                foreach (var pin in pins)
                {
                    _closeValuesByPin[pin] = null;
                    SetClosePinText(pin, "超时");
                }

                Log($"通路: {groupName} 在{timeoutSeconds}s内未等到反馈位 {string.Join(",", expectedBits.Select(bit => $"bit{bit}"))}");
                return;
            }

            await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
            var (value, text) = await MeasureCloseResultFromPowerAsync(groupName, divisor, cancellationToken).ConfigureAwait(false);
            foreach (var pin in pins)
            {
                _closeValuesByPin[pin] = value;
                SetClosePinText(pin, text);
            }

            Log($"通路: {groupName} 统一结果={text}");
        }

        private Task ConfigureCloseSignalForPinsAsync(IReadOnlyList<int> pins, CancellationToken cancellationToken)
        {
            if (pins.SequenceEqual(CloseGroupPins1))
            {
                return ConfigureCloseSignalGroup1Async(cancellationToken);
            }

            if (pins.SequenceEqual(CloseGroupPins2))
            {
                return ConfigureCloseSignalGroup2Async(cancellationToken);
            }

            throw new ArgumentException("不支持的通路测试分组", nameof(pins));
        }

        private async Task ConfigureCloseSignalGroup1Async(CancellationToken cancellationToken)
        {
            await EnsureJy7131ReadyAsync(cancellationToken).ConfigureAwait(false);
            await EnsureMtx532Async(cancellationToken).ConfigureAwait(false);

            await SetCloseDoAsync(3, true, cancellationToken).ConfigureAwait(false);
            await SetCloseDoAsync(20, true, cancellationToken).ConfigureAwait(false);
            await SetCloseRelayAsync(1, true, cancellationToken).ConfigureAwait(false);
            await SetCloseRelayAsync(6, true, cancellationToken).ConfigureAwait(false);
            await SetCloseSignalAoValuesAsync(new Dictionary<string, double>
            {
                ["AO2"] = 3.0,
                ["AO4"] = 3.0,
                ["AO3"] = 0.0,
                ["AO5"] = 0.0,
            }, cancellationToken).ConfigureAwait(false);
            await StartCloseWordsAsync(
                new uint[] { 0x000005C4, 0x000006C4, 0x80000524, 0x80000624, 0x9400019D, 0x8000043D, 0x800009BD },
                new uint[] { 0x000005C4, 0x000006C4, 0x80000524, 0x80000624, 0x80000ABD },
                cancellationToken).ConfigureAwait(false);
        }

        private async Task ConfigureCloseSignalGroup2Async(CancellationToken cancellationToken)
        {
            await EnsureJy7131ReadyAsync(cancellationToken).ConfigureAwait(false);

            await SetCloseDoAsync(3, true, cancellationToken).ConfigureAwait(false);
            await SetCloseDoAsync(20, true, cancellationToken).ConfigureAwait(false);
            await SetCloseRelayAsync(1, true, cancellationToken).ConfigureAwait(false);
            await SetCloseRelayAsync(6, true, cancellationToken).ConfigureAwait(false);
            await StartCloseWordsAsync(
                new uint[] { 0x00002D9D, 0x8000043D, 0x0000423D, 0x800009BD, 0x80100394, 0x00100294 },
                new uint[] { 0x80000ABD },
                cancellationToken).ConfigureAwait(false);
        }

        private async Task ClearAllCloseSignalOutputsAsync(CancellationToken cancellationToken, bool preserveSharedPreconditions = false)
        {
            try { await StopPeriodicWordsAsync(TxChannelIndex, cancellationToken).ConfigureAwait(false); } catch { }
            try { await StopPeriodicWordsAsync(TxChannelIndex2, cancellationToken).ConfigureAwait(false); } catch { }

            if (_mtx532 != null && _mtx532.IsConnected)
            {
                try { await SetCloseSignalAoValuesAsync(CloseSignalAoChannels.ToDictionary(ch => ch, _ => 0.0), cancellationToken).ConfigureAwait(false); } catch { }
            }

            if (_jy7131 != null)
            {
                foreach (var relayChannel in AllCloseRelayChannels)
                {
                    try { await _jy7131.SetRelayAsync(relayChannel - 1, false, cancellationToken).ConfigureAwait(false); } catch { }
                }

                foreach (var doIndex in AllCloseDoIndices)
                {
                    try { await _jy7131.WriteDoAsync($"DO{doIndex}", false, cancellationToken).ConfigureAwait(false); } catch { }
                }
            }

            if (_lvdt != null)
            {
                if (!preserveSharedPreconditions)
                {
                    try { await _lvdt.StopAsync(LvdtChannel1, cancellationToken).ConfigureAwait(false); } catch { }
                    try { await _lvdt.SetVaVbAsync(LvdtChannel1, 0.0, 0.0, cancellationToken).ConfigureAwait(false); } catch { }
                    try { await _lvdt.StopAsync(LvdtChannel2, cancellationToken).ConfigureAwait(false); } catch { }
                    try { await _lvdt.SetVaVbAsync(LvdtChannel2, 0.0, 0.0, cancellationToken).ConfigureAwait(false); } catch { }
                }
            }

            if (_res != null && _res.IsConnected)
            {
                if (!preserveSharedPreconditions)
                {
                    try { await _res.SetRelayStateAsync("RO0", false, false).ConfigureAwait(false); } catch { }
                    try { await _res.WriteChannelAsync("RO0", 0.0).ConfigureAwait(false); } catch { }
                    try { await _res.SetRelayStateAsync("RO1", false, false).ConfigureAwait(false); } catch { }
                    try { await _res.WriteChannelAsync("RO1", 0.0).ConfigureAwait(false); } catch { }
                }
            }
        }

        private async Task StartCloseWordsAsync(IReadOnlyList<uint> channel1Words, IReadOnlyList<uint> channel2Words, CancellationToken cancellationToken)
        {
            await EnsureArincTxAsync(cancellationToken).ConfigureAwait(false);
            await StartPeriodicWordsAsync(TxChannelIndex, channel1Words, cancellationToken).ConfigureAwait(false);
            await StartPeriodicWordsAsync(TxChannelIndex2, channel2Words, cancellationToken).ConfigureAwait(false);
        }

        private async Task SetCloseDoAsync(int doIndex, bool value, CancellationToken cancellationToken)
        {
            await EnsureJy7131ReadyAsync(cancellationToken).ConfigureAwait(false);
            await _jy7131.WriteDoAsync($"DO{doIndex}", value, cancellationToken).ConfigureAwait(false);
        }

        private async Task SetCloseRelayAsync(int relayChannel, bool value, CancellationToken cancellationToken)
        {
            await EnsureJy7131ReadyAsync(cancellationToken).ConfigureAwait(false);
            await _jy7131.SetRelayAsync(relayChannel - 1, value, cancellationToken).ConfigureAwait(false);
        }

        private async Task SetCloseSignalAoValuesAsync(IReadOnlyDictionary<string, double> channelValues, CancellationToken cancellationToken)
        {
            if (_mtx532 == null || !_mtx532.IsConnected)
                throw new InvalidOperationException("MTX532未连接");

            var values = CloseSignalAoChannels.ToDictionary(ch => ch, _ => 0.0);
            foreach (var pair in channelValues)
            {
                if (values.ContainsKey(pair.Key))
                    values[pair.Key] = pair.Value;
            }

            await _mtx532.WriteOnceDcAsync(values, cancellationToken).ConfigureAwait(false);
        }

        private async Task<bool> WaitForCloseSignalReadyAsync(string groupName, IReadOnlyList<int> expectedBits, int timeoutSeconds, CancellationToken cancellationToken)
        {
            await EnsureArincRxAsync(cancellationToken).ConfigureAwait(false);
            _ = await _arinc.ReadRxWordsAsync(RxChannelIndex, maxCount: 4096, enableTimeTag: false, enableRateAdaption: false, cancellationToken: cancellationToken).ConfigureAwait(false);

            var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
            var nextDebugLogAt = DateTime.MinValue;
            while (!cancellationToken.IsCancellationRequested && DateTime.UtcNow <= deadline)
            {
                var words = await _arinc.ReadRxWordsAsync(RxChannelIndex, maxCount: 512, enableTimeTag: false, enableRateAdaption: false, cancellationToken: cancellationToken).ConfigureAwait(false);
                foreach (var word in words)
                {
                    TryLogCloseFeedbackDebugFrame(word.Data429, ref nextDebugLogAt);

                    if (TryMatchCloseFeedback(word.Data429, expectedBits, out var activeBits))
                    {
                        Log($"通路: {groupName} 已收到反馈，已导通{string.Join(",", activeBits.Select(x => $"bit{x}"))}");
                        return true;
                    }
                }

                await Task.Delay(10, cancellationToken).ConfigureAwait(false);
            }

            return false;
        }

        private async Task<(double? Value, string Text)> MeasureResistanceFromPowerAsync(string scenarioName, int divisor, bool treatLowCurrentAsOl, CancellationToken cancellationToken)
        {
            if (_power28 == null || !_power28.IsConnected)
                throw new InvalidOperationException("28V程控电源未连接");

            var options = new PowerSupplyReadOptions { TimeoutMilliseconds = 5000 };
            var voltageReading = await _power28.ReadOnceAsync(PowerSupplyReadMode.MeasuredVoltage, PowerSupplyChannel.CH2, options, cancellationToken).ConfigureAwait(false);
            var currentReading = await _power28.ReadOnceAsync(PowerSupplyReadMode.MeasuredCurrent, PowerSupplyChannel.CH2, options, cancellationToken).ConfigureAwait(false);

            var voltage = voltageReading?.Value;
            var current = currentReading?.Value;
            if (!voltage.HasValue || !current.HasValue)
            {
                Log($"{scenarioName}: 电源CH2读数无效，Voltage={voltageReading?.Raw ?? "--"}, Current={currentReading?.Raw ?? "--"}");
                return (null, "--");
            }

            if (Math.Abs(current.Value) < OpenCircuitCurrentThresholdA)
            {
                Log($"{scenarioName}: 电源CH2电流过小，Voltage={voltage.Value:0.###}V Current={current.Value:0.###}A");
                return treatLowCurrentAsOl ? ((double?)OpenPassThresholdOhm, "OL") : ((double?)null, "--");
            }

            var resistance = (voltage.Value / current.Value * divisor) - CloseResultOffsetOhm;
            if (treatLowCurrentAsOl && resistance >= OpenPassThresholdOhm)
            {
                Log($"{scenarioName}: 电源CH2测量 Voltage={voltage.Value:0.###}V Current={current.Value:0.###}A => R={resistance:0.###}Ω，按OL显示");
                return (resistance, "OL");
            }

            Log($"{scenarioName}: 电源CH2测量 Voltage={voltage.Value:0.###}V Current={current.Value:0.###}A => R={resistance:0.###}Ω");
            return (resistance, $"{resistance:0.000} Ω");
        }

        private Task<(double? Value, string Text)> MeasureCloseResultFromPowerAsync(string scenarioName, int divisor, CancellationToken cancellationToken)
        {
            return MeasureResistanceFromPowerAsync(scenarioName, divisor, treatLowCurrentAsOl: false, cancellationToken);
        }

        private Task<(double? Value, string Text)> MeasureOpenResultFromPowerAsync(CancellationToken cancellationToken)
        {
            return MeasureResistanceFromPowerAsync("开路", 7, treatLowCurrentAsOl: true, cancellationToken);
        }

        private async Task StartPeriodicWordsAsync(int txChannelIndex, IReadOnlyList<uint> words, CancellationToken cancellationToken)
        {
            if (words == null || words.Count == 0)
                return;

            await EnsureArincTxAsync(cancellationToken).ConfigureAwait(false);
            await StopPeriodicWordsAsync(txChannelIndex, CancellationToken.None).ConfigureAwait(false);

            var specialWords = words.Where(FastPeriodWords.Contains).ToArray();
            var normalWords = words.Where(w => !FastPeriodWords.Contains(w)).ToArray();

            var loopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            SetTxLoopState(txChannelIndex, loopCts, Task.CompletedTask);

            if (specialWords.Length > 0)
            {
                await _arinc.SendWordsPeriodAsync(txChannelIndex, specialWords, SpecialArincPeriodMs, 0, Art4229Parity.Odd, loopCts.Token).ConfigureAwait(false);
            }

            if (normalWords.Length > 0)
            {
                await _arinc.SendWordsPeriodAsync(txChannelIndex, normalWords, ArincPeriodMs, 0, Art4229Parity.Odd, loopCts.Token).ConfigureAwait(false);
            }
        }
        private async Task StopPeriodicWordsAsync(int txChannelIndex, CancellationToken cancellationToken)
        {
            if (_arinc == null)
                return;

            var (loopCts, loopTask) = GetTxLoopState(txChannelIndex);

            try { loopCts?.Cancel(); } catch { }

            if (loopTask != null)
            {
                try { await loopTask.ConfigureAwait(false); } catch { }
            }

            try { loopCts?.Dispose(); } catch { }
            SetTxLoopState(txChannelIndex, null, Task.CompletedTask);

            try { await _arinc.StopTxAsync(txChannelIndex, cancellationToken).ConfigureAwait(false); } catch { }
        }

        private static int GetNextLoopDelayMs(DateTime nextSpecialAt, DateTime nextNormalAt, bool hasSpecialWords, bool hasNormalWords)
        {
            var now = DateTime.UtcNow;
            DateTime nextAt;

            if (hasSpecialWords && hasNormalWords)
                nextAt = nextSpecialAt <= nextNormalAt ? nextSpecialAt : nextNormalAt;
            else if (hasSpecialWords)
                nextAt = nextSpecialAt;
            else if (hasNormalWords)
                nextAt = nextNormalAt;
            else
                return (int)ArincPeriodMs;

            var delay = (int)Math.Max(1, (nextAt - now).TotalMilliseconds);
            return delay;
        }

        private (CancellationTokenSource cts, Task task) GetTxLoopState(int txChannelIndex)
        {
            return txChannelIndex == TxChannelIndex
                ? (_txChannel1LoopCts, _txChannel1LoopTask)
                : (_txChannel2LoopCts, _txChannel2LoopTask);
        }

        private void SetTxLoopState(int txChannelIndex, CancellationTokenSource cts, Task task)
        {
            if (txChannelIndex == TxChannelIndex)
            {
                _txChannel1LoopCts = cts;
                _txChannel1LoopTask = task ?? Task.CompletedTask;
                return;
            }

            _txChannel2LoopCts = cts;
            _txChannel2LoopTask = task ?? Task.CompletedTask;
        }

        /// <summary>
        /// 确保 ARINC429 发送通道已打开并配置（Odd parity, 标准 429 格式）
        /// </summary>
        private async Task EnsureArincTxAsync(CancellationToken cancellationToken)
        {
            if (_arinc == null)
            {
                var device = FindFirstArincDevice();
                if (device == null)
                    throw new InvalidOperationException("未找到ART4227/ART4229(ARINC429)板卡，无法发送429指令");

                _arinc = new Art4229Api(device, deviceIndex: 0);
            }

            if (!_arinc.IsConnected)
            {
                await _arinc.ConnectAsync(cancellationToken).ConfigureAwait(false);
            }

            if (!_txChannel1Opened)
            {
                await _arinc.OpenTxAsync(TxChannelIndex, cancellationToken).ConfigureAwait(false);
                await _arinc.ConfigureTxAsync(TxChannelIndex, ArincRate, Art4229TxMode.Single, Art4229Parity.Odd, Art4229WordFormat.Standard429, cancellationToken).ConfigureAwait(false);
                _txChannel1Opened = true;
            }

            if (!_txChannel2Opened)
            {
                await _arinc.OpenTxAsync(TxChannelIndex2, cancellationToken).ConfigureAwait(false);
                await _arinc.ConfigureTxAsync(TxChannelIndex2, ArincRate, Art4229TxMode.Single, Art4229Parity.Odd, Art4229WordFormat.Standard429, cancellationToken).ConfigureAwait(false);
                _txChannel2Opened = true;
            }
        }

        private async Task EnsureArincRxAsync(CancellationToken cancellationToken)
        {
            await EnsureArincTxAsync(cancellationToken).ConfigureAwait(false);

            if (_rxChannelOpened)
                return;

            await _arinc.OpenRxAsync(RxChannelIndex, cancellationToken).ConfigureAwait(false);
            await _arinc.ConfigureRxAsync(RxChannelIndex, ArincRate, Art4229Parity.Odd, Art4229WordFormat.Standard429, false, 512, false, cancellationToken).ConfigureAwait(false);
            await _arinc.StartRxAsync(RxChannelIndex, cancellationToken).ConfigureAwait(false);
            _ = await _arinc.ReadRxWordsAsync(RxChannelIndex, 4096, false, false, cancellationToken).ConfigureAwait(false);
            _rxChannelOpened = true;
        }

        /// <summary>
        /// 在 PXI 机箱中查找 ARINC429 板卡（4227/4229）
        /// </summary>
        private DeviceBase FindFirstArincDevice()
        {
            var chassisList = _pxiChassisService?.GetAllChassis();
            if (chassisList == null)
                return null;

            foreach (var chassis in chassisList)
            {
                var device = chassis?.Devices?.FirstOrDefault(d =>
                    (d?.Model?.IndexOf("4227", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.Model?.IndexOf("4229", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.Model?.IndexOf("ARINC", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.Model?.IndexOf("429", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.Name?.IndexOf("4227", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.Name?.IndexOf("4229", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.Name?.IndexOf("429", StringComparison.OrdinalIgnoreCase) >= 0));

                if (device != null)
                    return device;
            }

            return null;
        }

        /// <summary>
        /// 当开路和通路都已测量完成时，按阈值范围判定结果，并更新"上次/本次"测试结论。
        /// 判据：开路 >100kΩ, 通路 <10Ω
        /// </summary>
        private async Task TryFinalizeAsync()
        {
            if (!_measuredOpen || !_measuredClose)
                return;

            var openPass = true;
            var closePass = true;

            for (int pin = 9; pin <= 15; pin++)
            {
                var vOpen = _openValuesByPin.TryGetValue(pin, out var o) ? o : null;
                var vClose = _closeValuesByPin.TryGetValue(pin, out var c) ? c : null;

                if (!(vOpen != null && vOpen >= OpenPassThresholdOhm))
                    openPass = false;

                if (!(vClose != null && vClose <= ClosePassThresholdOhm))
                    closePass = false;
            }

            var pass = openPass && closePass;

            var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            var resultText = pass ? "合格" : "不合格";
            CurrentTestResult = resultText;
            PreviousTestTime = now;
            PreviousTestResult = resultText;
            LastTestTime = now;
            LastTestResult = resultText;

            SaveTestResultToProject();
            Log($"测试结果: {resultText}");

            if (IsManualTestRunning)
            {
                await StopManualTestAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// 中止手动测试（通常用于初始化失败、测量超时、测量异常等）
        /// </summary>
        private async Task AbortManualTestAsync(string reason)
        {
            _manualAborted = true;
            if (!string.IsNullOrWhiteSpace(reason))
            {
                Log(reason);
            }

            await StopManualTestAsync().ConfigureAwait(false);
        }

        private async Task AbortAutoTestAsync(string reason)
        {
            if (!string.IsNullOrWhiteSpace(reason))
            {
                Log(reason);
            }

            await StopAutoTestAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 停止手动测试并释放硬件资源（断开矩阵开关、万用表、ARINC429、关闭电源输出）
        /// </summary>
        private async Task StopManualTestAsync()
        {
            if (IsManualTestStopping)
            {
                return;
            }

            IsManualTestStopping = true;
            IsManualTestInitializing = false;
            try
            {
                CanMeasure = false;
                _manualCts?.Cancel();
            }
            catch
            {
            }

            Log("手动测试停止/结束，正在断开设备...");
            try
            {
                try { await ClearAllCloseSignalOutputsAsync(CancellationToken.None).ConfigureAwait(false); } catch { }

                try
                {
                    await CleanupArincAsync().ConfigureAwait(false);
                }
                catch
                {
                }

                try
                {
                    await CleanupPowerAsync().ConfigureAwait(false);
                }
                catch
                {
                }

                try
                {
                    await CleanupMtx532Async().ConfigureAwait(false);
                }
                catch
                {
                }

                try
                {
                    await CleanupLvdtAsync().ConfigureAwait(false);
                }
                catch
                {
                }

                try
                {
                    await CleanupResistanceAsync().ConfigureAwait(false);
                }
                catch
                {
                }

                try
                {
                    await CleanupRelayAsync().ConfigureAwait(false);
                }
                catch
                {
                }
            }
            finally
            {
                IsManualTestInitializing = false;
                IsManualTestRunning = false;
                IsManualTestStopping = false;
                RaisePropertyChanged(nameof(CanStartManualTest));
                RaisePropertyChanged(nameof(CanStartAutoTest));
                Log("手动测试已结束");
            }
        }

        /// <summary>
        /// 停止自动测试并释放硬件资源
        /// </summary>
        private async Task StopAutoTestAsync()
        {
            if (IsAutoTestStopping)
            {
                return;
            }

            IsAutoTestStopping = true;
            IsAutoTestInitializing = false;
            try
            {
                _autoCts?.Cancel();
            }
            catch
            {
            }

            Log("自动测试停止/结束，正在断开设备...");
            try
            {
                try { await ClearAllCloseSignalOutputsAsync(CancellationToken.None).ConfigureAwait(false); } catch { }

                try
                {
                    await CleanupArincAsync().ConfigureAwait(false);
                }
                catch
                {
                }

                try
                {
                    await CleanupPowerAsync().ConfigureAwait(false);
                }
                catch
                {
                }

                try
                {
                    await CleanupMtx532Async().ConfigureAwait(false);
                }
                catch
                {
                }

                try
                {
                    await CleanupLvdtAsync().ConfigureAwait(false);
                }
                catch
                {
                }

                try
                {
                    await CleanupResistanceAsync().ConfigureAwait(false);
                }
                catch
                {
                }

                try
                {
                    await CleanupRelayAsync().ConfigureAwait(false);
                }
                catch
                {
                }
            }
            finally
            {
                IsAutoTestInitializing = false;
                IsAutoTestRunning = false;
                IsAutoTestStopping = false;
                RaisePropertyChanged(nameof(CanStartManualTest));
                RaisePropertyChanged(nameof(CanStartAutoTest));
                Log("自动测试已结束");
            }
        }

        private async Task EnsureJy7131ReadyAsync(CancellationToken cancellationToken)
        {
            var device = FindFirstJy7131Device();
            if (device == null)
                throw new InvalidOperationException("未找到PXIe-7131(JY7131)板卡，无法配置DO和485继电器");

            if (_jy7131 == null)
            {
                var slot = device is DigitalIODevice dio ? dio.SlotIndex : 0;
                _jy7131 = new Jy7131Api(device, slot);
            }

            if (!_jy7131.IsConnected)
                await _jy7131.ConnectAsync(cancellationToken).ConfigureAwait(false);

            if (!_jy7131.IsRunning)
            {
                await _jy7131.SetOutputModeAsync(Jy7131OutputMode.Sinking, cancellationToken).ConfigureAwait(false);
                await _jy7131.StartAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task EnsureRelay485Async(bool on, CancellationToken cancellationToken)
        {
            await _relayLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (on)
                {
                    await EnsureJy7131ReadyAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        await _jy7131.WriteDoAsync("DO25", true, cancellationToken).ConfigureAwait(false);
                        await _jy7131.WriteDoAsync("DO28", true, cancellationToken).ConfigureAwait(false);  // DI 对地
                    }
                    catch { }

                    try
                    {
                        await _jy7131.SetRelayAsync(6, true, cancellationToken).ConfigureAwait(false);
                        await _jy7131.SetRelayAsync(7, true, cancellationToken).ConfigureAwait(false);
                    }
                    catch { }
                }
                else
                {
                    if (_jy7131 != null)
                    {
                        foreach (var relayChannel in AllCloseRelayChannels.Distinct())
                        {
                            try { await _jy7131.SetRelayAsync(relayChannel - 1, false, cancellationToken).ConfigureAwait(false); } catch { }
                        }

                        foreach (var doIndex in AllCloseDoIndices.Distinct())
                        {
                            try { await _jy7131.WriteDoAsync($"DO{doIndex}", false, cancellationToken).ConfigureAwait(false); } catch { }
                        }

                        try { await _jy7131.WriteDoAsync("DO25", false, cancellationToken).ConfigureAwait(false); } catch { }
                        try { await _jy7131.WriteDoAsync("DO28", false, cancellationToken).ConfigureAwait(false); } catch { }
                        try { await _jy7131.SetRelayAsync(6, false, cancellationToken).ConfigureAwait(false); } catch { }
                        try { await _jy7131.SetRelayAsync(7, false, cancellationToken).ConfigureAwait(false); } catch { }
                    }
                }
            }
            finally
            {
                _relayLock.Release();
            }
        }

        private async Task EnsureMtx532Async(CancellationToken cancellationToken)
        {
            if (_mtx532 != null && _mtx532.IsConnected)
                return;

            var device = FindFirstMtx532Device();
            if (device == null)
                throw new InvalidOperationException("未找到MTX532(模拟量输出)板卡");

            var slot = device is PxiDeviceBase pxi ? pxi.SlotIndex : 7;
            _mtx532 = new Mtx532Api(device, options: new Mtx532Options { SampleRateHz = 20000.0 }, slotNumber: slot);
            await _mtx532.ConnectAsync(cancellationToken, CloseSignalAoChannels).ConfigureAwait(false);
            await _mtx532.WriteOnceDcAsync(CloseSignalAoChannels.ToDictionary(ch => ch, _ => 0.0), cancellationToken).ConfigureAwait(false);
            await Task.Delay(300, cancellationToken).ConfigureAwait(false);
            if (await _mtx532.CanStartOutputAsync(cancellationToken).ConfigureAwait(false))
            {
                await _mtx532.StartOutputAsync(cancellationToken).ConfigureAwait(false);
                await Task.Delay(300, cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task EnsureResistanceAsync(CancellationToken cancellationToken)
        {
            if (_res != null && _res.IsConnected)
                return;

            var device = FindFirstActs6010Device();
            if (device == null)
                throw new InvalidOperationException("未找到ACTS6010(程控电阻)板卡");

            _res = new ACTS6010Driver(device, logicalId: 1);
            var ok = await _res.ConnectAsync().ConfigureAwait(false);
            if (!ok)
                throw new InvalidOperationException("ACTS6010连接失败");
        }

        private async Task SetResistanceChannelAsync(string channel, double resistanceOhm, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_res == null || !_res.IsConnected)
                throw new InvalidOperationException("ACTS6010未连接");

            await _res.SetRelayStateAsync(channel, true, false).ConfigureAwait(false);
            var ok = await _res.WriteChannelAsync(channel, resistanceOhm).ConfigureAwait(false);
            if (!ok)
                throw new InvalidOperationException($"设置ACTS6010阻值失败: {channel}");
        }

        private async Task EnsureLvdtAsync(CancellationToken cancellationToken)
        {
            if (_lvdt == null)
                _lvdt = new Pxi4087LvdtApi();

            if (!_lvdt.IsConnected)
                await _lvdt.ConnectAsync(LvdtSlotIndex, cancellationToken).ConfigureAwait(false);

            await ConfigureLvdtOutputCalibrationAsync(LvdtChannel1, cancellationToken).ConfigureAwait(false);
            await ConfigureLvdtOutputCalibrationAsync(LvdtChannel2, cancellationToken).ConfigureAwait(false);

            var config = CreateSimulationConfig();
            await _lvdt.ConfigureSimulationChannelAsync(LvdtChannel1, config, cancellationToken).ConfigureAwait(false);
            await _lvdt.ConfigureSimulationChannelAsync(LvdtChannel2, config, cancellationToken).ConfigureAwait(false);
        }

        private async Task ConfigureLvdtOutputCalibrationAsync(int channel, CancellationToken cancellationToken)
        {
            var calibration = ResolveLvdtOutputCalibration(channel);
            if (calibration == null)
            {
                await _lvdt.ClearOutputCalibrationAsync(channel, cancellationToken).ConfigureAwait(false);
                return;
            }

            await _lvdt.ConfigureOutputCalibrationAsync(channel, calibration, cancellationToken).ConfigureAwait(false);
        }

        private LvdtOutputCalibration ResolveLvdtOutputCalibration(int channel)
        {
            var device = FindFirstLvdtDevice();
            if (device == null)
                return null;

            var records = _singleBoardTestContext?.GetCurrentTestItemNode(TestItemName)?.CalibrationRecords;
            if (records == null || records.Count == 0)
                return null;

            var vaRecord = TryGetCalibrationRecord(records, device.Id, $"CH{channel-1}{LvdtVaSuffix}");
            var vbRecord = TryGetCalibrationRecord(records, device.Id, $"CH{channel-1}{LvdtVbSuffix}");
            if (vaRecord == null && vbRecord == null)
                return null;

            return new LvdtOutputCalibration
            {
                VaSlope = vaRecord?.Slope ?? 1.0,
                VaIntercept = vaRecord?.Intercept ?? 0.0,
                IsVaCalibrated = vaRecord?.IsCalibrated ?? false,
                VbSlope = vbRecord?.Slope ?? 1.0,
                VbIntercept = vbRecord?.Intercept ?? 0.0,
                IsVbCalibrated = vbRecord?.IsCalibrated ?? false
            };
        }

        private static ChannelCalibrationRecord TryGetCalibrationRecord(Dictionary<string, ChannelCalibrationRecord> records, string deviceId, string signalAddress)
        {
            if (records == null || string.IsNullOrWhiteSpace(signalAddress))
                return null;

            if (!string.IsNullOrWhiteSpace(deviceId))
            {
                var scopedKey = $"{deviceId}/{signalAddress}";
                if (records.TryGetValue(scopedKey, out var scopedRecord))
                    return scopedRecord;
            }

            if (records.TryGetValue(signalAddress, out var record))
                return record;

            return null;
        }

        private DeviceBase FindFirstLvdtDevice()
        {
            var chassisList = _pxiChassisService?.GetAllChassis();
            if (chassisList == null)
                return null;

            foreach (var chassis in chassisList)
            {
                var device = chassis?.Devices?.FirstOrDefault(d =>
                    (d?.Model?.IndexOf("4087", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.Model?.IndexOf("LVDT", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.Name?.IndexOf("4087", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.Name?.IndexOf("LVDT", StringComparison.OrdinalIgnoreCase) >= 0));

                if (device != null)
                    return device;
            }

            return null;
        }

        private LvdtSimulationConfig CreateSimulationConfig()
        {
            return new LvdtSimulationConfig
            {
                UseInternalExcitation = true,
                ExcitationVoltage = SimulationSumVrms,
                ExcitationFrequency = 3200.0,
                TransmissionRatio = 1.0,
                PhaseDelay = 0,
                AdcRangeIndex = 3
            };
        }

        private DeviceBase FindFirstMtx532Device()
        {
            var chassisList = _pxiChassisService?.GetAllChassis();
            if (chassisList == null)
                return null;

            foreach (var chassis in chassisList)
            {
                var device = chassis?.Devices?.FirstOrDefault(d =>
                    (d?.Model?.IndexOf("532", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.Name?.IndexOf("532", StringComparison.OrdinalIgnoreCase) >= 0));

                if (device != null)
                    return device;
            }

            return null;
        }

        private DeviceBase FindFirstActs6010Device()
        {
            var chassisList = _pxiChassisService?.GetAllChassis();
            if (chassisList == null)
                return null;

            var preferredChassisName = _singleBoardTestContext?.ChassisName;
            foreach (var chassis in chassisList)
            {
                if (!string.IsNullOrWhiteSpace(preferredChassisName) && !string.Equals(chassis?.Name, preferredChassisName, StringComparison.OrdinalIgnoreCase))
                    continue;

                var device = chassis?.Devices?.FirstOrDefault(d =>
                    (d?.Model?.IndexOf("6010", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.Name?.IndexOf("6010", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.Model?.IndexOf("7012", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.Name?.IndexOf("7012", StringComparison.OrdinalIgnoreCase) >= 0));

                if (device != null)
                    return device;
            }

            foreach (var chassis in chassisList)
            {
                var device = chassis?.Devices?.FirstOrDefault(d =>
                    (d?.Model?.IndexOf("6010", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.Name?.IndexOf("6010", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.Model?.IndexOf("7012", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.Name?.IndexOf("7012", StringComparison.OrdinalIgnoreCase) >= 0));

                if (device != null)
                    return device;
            }

            return null;
        }

        private DeviceBase FindFirstJy7131Device()
        {
            var chassisList = _pxiChassisService?.GetAllChassis();
            if (chassisList == null)
            {
                return null;
            }

            foreach (var chassis in chassisList)
            {
                var device = chassis?.Devices?.FirstOrDefault(d =>
                    d is DigitalIODevice ||
                    (d?.Model?.IndexOf("7131", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.DeviceTypeName?.IndexOf("离散量", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.DeviceTypeName?.IndexOf("数字量", StringComparison.OrdinalIgnoreCase) >= 0));

                if (device != null)
                {
                    return device;
                }
            }

            return null;
        }

        private async Task EnsurePowerAsync(CancellationToken cancellationToken)
        {
            _power28 ??= new PowerSupplySocketApi();
            _powerA ??= new PowerSupplySocketApi();
            _powerB ??= new PowerSupplySocketApi();

            if (!_power28.IsConnected)
            {
                await _power28.ConnectAsync(PowerSupply28VAddress, cancellationToken).ConfigureAwait(false);
            }

            if (!_powerA.IsConnected)
            {
                await _powerA.ConnectAsync(PowerSupplyAAddress, cancellationToken).ConfigureAwait(false);
            }

            if (!_powerB.IsConnected)
            {
                await _powerB.ConnectAsync(PowerSupplyBAddress, cancellationToken).ConfigureAwait(false);
            }

            await _powerA.ApplyAsync(PowerSupplyChannel.CH3, InputVoltageV, InputCurrentA, cancellationToken).ConfigureAwait(false);
            await _powerB.ApplyAsync(PowerSupplyChannel.CH3, InputVoltageV, InputCurrentA, cancellationToken).ConfigureAwait(false);
            await _powerA.SetOutputEnabledAsync(PowerSupplyChannel.CH3, true, cancellationToken).ConfigureAwait(false);
            await _powerB.SetOutputEnabledAsync(PowerSupplyChannel.CH3, true, cancellationToken).ConfigureAwait(false);
            await _powerA.ApplyAsync(PowerSupplyChannel.CH1, Input24VoltageV, Input24CurrentA, cancellationToken).ConfigureAwait(false);
            await _powerA.SetOutputEnabledAsync(PowerSupplyChannel.CH1, true, cancellationToken).ConfigureAwait(false);

            if (_boardPowerService.IsPowered)
            {
                await _power28.SetOutputEnabledAsync(PowerSupplyChannel.CH1, false, cancellationToken).ConfigureAwait(false);
                await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
            }
            await _power28.ApplyAsync(PowerSupplyChannel.CH1, Input28VoltageV, Input28CurrentA, cancellationToken).ConfigureAwait(false);
            await _power28.SetOutputEnabledAsync(PowerSupplyChannel.CH1, true, cancellationToken).ConfigureAwait(false);
            _boardPowerService.SetPoweredState(true, "液压单板", Input28VoltageV);

            await _power28.ApplyAsync(PowerSupplyChannel.CH2, Input28VoltageV, MeasureChannelCurrentA, cancellationToken).ConfigureAwait(false);
            await _power28.SetOutputEnabledAsync(PowerSupplyChannel.CH2, true, cancellationToken).ConfigureAwait(false);
            await Task.Delay(300, cancellationToken).ConfigureAwait(false);
        }

        private async Task CleanupPowerAsync()
        {
            try
            {
                if (_power28 != null)
                {
                    // CH1 保持输出（不下电），仅关闭 CH2 测量通道
                    try { await _power28.SetOutputEnabledAsync(PowerSupplyChannel.CH2, false, CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _power28.DisconnectAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _power28.DisposeAsync().ConfigureAwait(false); } catch { }
                }

                if (_powerA != null)
                {
                    try { await _powerA.SetOutputEnabledAsync(PowerSupplyChannel.CH1, false, CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _powerA.SetOutputEnabledAsync(PowerSupplyChannel.CH3, false, CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _powerA.DisconnectAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _powerA.DisposeAsync().ConfigureAwait(false); } catch { }
                }

                if (_powerB != null)
                {
                    try { await _powerB.SetOutputEnabledAsync(PowerSupplyChannel.CH3, false, CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _powerB.DisconnectAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _powerB.DisposeAsync().ConfigureAwait(false); } catch { }
                }
            }
            finally
            {
                _power28 = null;
                _powerA = null;
                _powerB = null;
            }
        }

        private async Task CleanupArincAsync()
        {
            try
            {
                if (_arinc != null)
                {
                    try { _txChannel1LoopCts?.Cancel(); } catch { }
                    try { _txChannel2LoopCts?.Cancel(); } catch { }

                    if (_txChannel1Opened)
                    {
                        try { await _arinc.StopTxAsync(TxChannelIndex, CancellationToken.None).ConfigureAwait(false); } catch { }
                        try { await _arinc.CloseTxAsync(TxChannelIndex, CancellationToken.None).ConfigureAwait(false); } catch { }
                    }

                    if (_txChannel2Opened)
                    {
                        try { await _arinc.StopTxAsync(TxChannelIndex2, CancellationToken.None).ConfigureAwait(false); } catch { }
                        try { await _arinc.CloseTxAsync(TxChannelIndex2, CancellationToken.None).ConfigureAwait(false); } catch { }
                    }

                    if (_rxChannelOpened)
                    {
                        try { await _arinc.StopRxAsync(RxChannelIndex, CancellationToken.None).ConfigureAwait(false); } catch { }
                        try { await _arinc.CloseRxAsync(RxChannelIndex, CancellationToken.None).ConfigureAwait(false); } catch { }
                    }

                    try { await _arinc.DisconnectAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _arinc.DisposeAsync().ConfigureAwait(false); } catch { }
                }
            }
            catch
            {
            }
            finally
            {
                _arinc = null;
                try { _txChannel1LoopCts?.Dispose(); } catch { }
                try { _txChannel2LoopCts?.Dispose(); } catch { }
                _txChannel1LoopCts = null;
                _txChannel2LoopCts = null;
                _txChannel1LoopTask = Task.CompletedTask;
                _txChannel2LoopTask = Task.CompletedTask;
                _txChannel1Opened = false;
                _txChannel2Opened = false;
                _rxChannelOpened = false;
            }
        }

        private async Task CleanupMtx532Async()
        {
            try
            {
                if (_mtx532 != null)
                {
                    try { await _mtx532.WriteOnceDcAsync(CloseSignalAoChannels.ToDictionary(ch => ch, _ => 0.0), CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _mtx532.StopOutputAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _mtx532.DisconnectAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _mtx532.DisposeAsync().ConfigureAwait(false); } catch { }
                }
            }
            catch
            {
            }
            finally
            {
                _mtx532 = null;
            }
        }

        private async Task CleanupLvdtAsync()
        {
            try
            {
                if (_lvdt != null)
                {
                    try { await _lvdt.StopAsync(LvdtChannel1, CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _lvdt.StopAsync(LvdtChannel2, CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _lvdt.SetVaVbAsync(LvdtChannel1, 0.0, 0.0, CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _lvdt.SetVaVbAsync(LvdtChannel2, 0.0, 0.0, CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _lvdt.ResetAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _lvdt.DisposeAsync().ConfigureAwait(false); } catch { }
                }
            }
            catch
            {
            }
            finally
            {
                _lvdt = null;
                _isPtuPrePowerPrepared = false;
                _isCloseSharedPrepared = false;
            }
        }

        private async Task CleanupResistanceAsync()
        {
            try
            {
                if (_res != null)
                {
                    try { await _res.SetRelayStateAsync("RO0", false, false).ConfigureAwait(false); } catch { }
                    try { await _res.SetRelayStateAsync("RO1", false, false).ConfigureAwait(false); } catch { }
                    try { await _res.WriteChannelAsync("RO0", 0.0).ConfigureAwait(false); } catch { }
                    try { await _res.WriteChannelAsync("RO1", 0.0).ConfigureAwait(false); } catch { }
                    try { await _res.DisconnectAsync().ConfigureAwait(false); } catch { }
                }
            }
            catch
            {
            }
            finally
            {
                _res = null;
                _isCloseSharedPrepared = false;
            }
        }

        private bool TryMatchCloseFeedback(uint rawWord, IReadOnlyList<int> expectedBits, out List<int> activeBits)
        {
            activeBits = null;
            if (_arinc == null)
                return false;

            _arinc.ParseRawWord(rawWord, out var label, out var sdi, out var data19, out var ssm);
            if (_arinc.ReverseLabel(label) != CloseFeedbackLabelDec || sdi != CloseFeedbackSdi)
                return false;

            activeBits = CloseFeedbackBits.Where(bit => ((rawWord >> bit) & 0x1u) == 1u).OrderBy(bit => bit).ToList();
            return expectedBits.OrderBy(bit => bit).SequenceEqual(activeBits);
        }

        private void ResetAllDisplays()
        {
            OpenPin9Text = "--";
            OpenPin10Text = "--";
            OpenPin11Text = "--";
            OpenPin12Text = "--";
            OpenPin13Text = "--";
            OpenPin14Text = "--";
            OpenPin15Text = "--";

            ClosePin9Text = "--";
            ClosePin10Text = "--";
            ClosePin11Text = "--";
            ClosePin12Text = "--";
            ClosePin13Text = "--";
            ClosePin14Text = "--";
            ClosePin15Text = "--";

            LastTestTime = "--";
            LastTestResult = "--";
        }

        private void Log(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            var line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.Invoke(() => Logs.Add(line));
                return;
            }

            Logs.Add(line);
        }
    }
}
