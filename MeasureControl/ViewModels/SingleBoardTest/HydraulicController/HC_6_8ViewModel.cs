using Prism.Commands;
using Prism.Mvvm;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using MeasureControl.Drivers;
using MeasureControl.Events;
using MeasureControl.Models.Devices;
using MeasureControl.Models.Devices.DeviceCategories;
using MeasureControl.Services;
using MeasureControl.Services.HardwareApis;
using Prism.Events;
using Prism.Ioc;

namespace MeasureControl.ViewModels.SingleBoardTest.HydraulicController
{
    /// <summary>
    /// HC_6_8 测试项：输出有效性测试（Output Valid 控制验证）
    /// 测试目的：验证液压控制器通过 ARINC429 接收"输出有效"指令后，
    ///          能够正确控制输出针脚（Pin9-15）的开关状态。
    /// 测试方法：
    /// 1) 给被测板供电 28V。
    /// 2) 开路测试：不发送指令，测量 Pin9-15 对地阻抗（应为高阻，>100kΩ）。
    /// 3) 通路测试：通过 ARINC429 发送"输出有效"指令（Label=65oct），
    ///    再测量 Pin9-15 对地阻抗（应为低阻，<10Ω）。
    /// 4) 所有针脚都满足判据则“合格”。
    /// </summary>
    public class HC_6_8ViewModel : BindableBase
    {
        // 电源配置（给被测板供电）
        private const string PowerSupply28VAddress = "192.168.1.15";
        private const string PowerSupplyAAddress = "192.168.1.16";
        private const string PowerSupplyBAddress = "192.168.1.17";
        private const double Input28VoltageV = 28.0;
        private const double Input28CurrentA = 1.0;
        private const double Input24VoltageV = 24.0;
        private const double Input24CurrentA = 1.0;
        private const double InputVoltageV = 5.0;
        private const double InputCurrentA = 1;

        // 万用表和矩阵开关配置
        private const string DmmIpAddress = "192.168.1.13";
        private const string MatrixIpAddress = "192.168.1.3";
        private const string DmmTriggerDelayCommand= "TRIG:DEL 0.01";

		// 矩阵开关槽位配置
		private const int MatrixSlotCommon = 4;       // 公共端槽位
        private const int MatrixSlotPinRoute = 6;     // 针脚路由槽位

        // ARINC429 发送配置
        private const int TxChannelIndex = 0;
        private const int TxChannelIndex2 = 1;
        private const double ArincRate = 12500.0;
        private const uint ArincPeriodMs = 100;
        private const uint SpecialArincPeriodMs = 15;

        private static readonly HashSet<uint> FastPeriodWords = new HashSet<uint>
        {
            0x800040BC,
            0x800004BC,
            0x000042BC,
        };

        private static readonly string[] CloseSignalAoChannels = { "AO0", "AO1", "AO2" };
        private const int LvdtSlotIndex = 2;
        private const int LvdtChannel1 = 1;
        private const int LvdtChannel2 = 2;
        private const double CloseSignalAoVoltageV = 3.0;
        private const double BleedResistanceOhm = 1155.4;
        private const double BleedLvdtVaV = 2.4;
        private const double BleedLvdtVbV = 3.6;
        private const int CloseSignalSettleMs = 180;

        // 阻抗判据
        private const double OpenPassThresholdOhm = 100_000.0;   // 开路阻抗阈值：>100kΩ 为合格
        private const double ClosePassThresholdOhm = 10.0;       // 通路阻抗阈值：<10Ω 为合格

        private readonly SemaphoreSlim _measureLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _relayLock = new SemaphoreSlim(1, 1);
        private readonly IPxiChassisService _pxiChassisService;
        private readonly ISingleBoardTestContextService _singleBoardTestContext;

        private const string TestItemName = "离散量输出测试";

        private CancellationTokenSource _manualCts;
        private CancellationTokenSource _autoCts;

        private IDmmApi _dmm;
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
        private bool _isMatrixCommonConnected;
        private int? _currentMeasuredPin;

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

        private sealed class CloseSignalDefinition
        {
            public int Pin { get; set; }
            public string Name { get; set; }
            public IReadOnlyList<uint> Channel1Words { get; set; }
            public IReadOnlyList<uint> Channel2Words { get; set; }
            public IReadOnlyList<string> AoChannels { get; set; } = Array.Empty<string>();
            public IReadOnlyList<int> DoIndices { get; set; } = Array.Empty<int>();
            public IReadOnlyList<int> RelayChannels { get; set; } = Array.Empty<int>();
            public string ResistanceChannel { get; set; }
            public double? ResistanceOhm { get; set; }
            public int? LvdtChannel { get; set; }
            public double? LvdtVaV { get; set; }
            public double? LvdtVbV { get; set; }
        }

        private static readonly IReadOnlyList<CloseSignalDefinition> CloseSignals = new[]
        {
            new CloseSignalDefinition
            {
                Pin = 9,
                Name = "EMP_1B_AUTO_ENABLE_DO",
                Channel1Words = new uint[] { 0x800009BD, 0x800004BC, 0x0002C1B9, 0x80000624, 0x80000524, 0x00000623, 0x00000523 },
                Channel2Words = new uint[] { 0x00000523, 0x00000623, 0x80000524, 0x80000624, 0x80000ABD },
                AoChannels = new[] { "AO1", "AO2" },
                DoIndices = new[] { 20 },
                RelayChannels = new[] { 6 }
            },
            new CloseSignalDefinition
            {
                Pin = 10,
                Name = "EMP_2B_AUTO_ENABLE_DO",
                Channel1Words = new uint[] { 0x800009BD, 0x800004BC, 0x940001B9, 0x80000624, 0x80000524, 0x00000623, 0x00000523 },
                Channel2Words = new uint[] { 0x00000523, 0x00000623, 0x80000524, 0x80000624, 0x80000ABD },
                AoChannels = new[] { "AO0", "AO2" },
                DoIndices = new[] { 21 },
                RelayChannels = new[] { 6 }
            },
            new CloseSignalDefinition
            {
                Pin = 11,
                Name = "EMP_3B_AUTO_ENABLE_DO",
                Channel1Words = new uint[] { 0x00000523, 0x00000623, 0x80000524, 0x80000624, 0x82F401B9, 0x800040BC },
                Channel2Words = new uint[] { 0x00000523, 0x00000623, 0x80000524, 0x80000624, 0x003401B9 },
                AoChannels = new[] { "AO0", "AO1" },
                DoIndices = new[] { 9, 22, 23 },
                RelayChannels = new[] { 3, 6 }
            },
            new CloseSignalDefinition
            {
                Pin = 12,
                Name = "EMP_3A_AUTO_ENABLE_DO",
                Channel1Words = new uint[] { 0x00000523, 0x00000623, 0x80000524, 0x80000624, 0x002C01B9, 0x800040BC },
                Channel2Words = new uint[] { 0x00000523, 0x00000623, 0x80000524, 0x80000624 },
                AoChannels = new[] { "AO0", "AO1" },
                DoIndices = new[] { 22 },
                RelayChannels = new[] { 6 }
            },
            new CloseSignalDefinition
            {
                Pin = 13,
                Name = "PTU_AUTO_ENABLE_DO",
                Channel1Words = new uint[] { 0x00002DB9, 0x800004BC, 0x000042BC, 0x800009BD },
                Channel2Words = new uint[] { 0x80000ABD },
                DoIndices = new[] { 3 },
                RelayChannels = new[] { 1 }
            },
            new CloseSignalDefinition
            {
                Pin = 14,
                Name = "RSVR_2_ABV_BLEED_ENABLE_DO",
                Channel1Words = new uint[] { 0x00100229, 0x800009BD },
                Channel2Words = new uint[] { 0x80000ABD },
                //DoIndices = new[] { 26 },
                //RelayChannels = new[] { 7 },
                ResistanceChannel = "RO0",
                ResistanceOhm = BleedResistanceOhm,
                LvdtChannel = LvdtChannel1,
                LvdtVaV = BleedLvdtVaV,
                LvdtVbV = BleedLvdtVbV,
            },
            new CloseSignalDefinition
            {
                Pin = 15,
                Name = "RSVR_3_ABV_BLEED_ENABLE_DO",
                Channel1Words = new uint[] { 0x80100329, 0x800009BD },
                Channel2Words = new uint[] { 0x80000ABD },
                //DoIndices = new[] { 15 },
                //RelayChannels = new[] { 4 },
                ResistanceChannel = "RO1",
                ResistanceOhm = BleedResistanceOhm,
                LvdtChannel = LvdtChannel2,
                LvdtVaV = BleedLvdtVaV,
                LvdtVbV = BleedLvdtVbV,
            }
        };

        public HC_6_8ViewModel(IPxiChassisService pxiChassisService, ISingleBoardTestContextService singleBoardTestContext)
        {
            _pxiChassisService = pxiChassisService;
            _singleBoardTestContext = singleBoardTestContext;

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

        public bool CanMeasureOpen => IsManualTestRunning && CanMeasure && !_measuredOpen;
        public bool CanMeasureClose => IsManualTestRunning && CanMeasure && _measuredOpen && !_measuredClose;
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
        /// 然后由用户分别点击"测量开路"和"测量通路"按钮执行测量。
        /// </summary>
        private async Task OnManualTestAsync()
        {
            if (IsManualTestRunning)
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

            Log("开始手动测试");
            Log($"判据: 开路>={OpenPassThresholdOhm:0}Ω, 通路<={ClosePassThresholdOhm:0}Ω");

            try
            {
                await EnsureRelay485Async(on: true, cancellationToken: _manualCts.Token).ConfigureAwait(false);
                Log("7131控制板已初始化");

                await EnsureArincTxAsync(_manualCts.Token).ConfigureAwait(false);
                Log("ARINC429板卡连接成功");

                await EnsureMtx532Async(_manualCts.Token).ConfigureAwait(false);
                Log("MT532板卡连接成功");

                await EnsureResistanceAsync(_manualCts.Token).ConfigureAwait(false);
                Log("程控电阻板卡连接成功");

                await EnsureLvdtAsync(_manualCts.Token).ConfigureAwait(false);
                Log("LVDT板卡连接成功");

                _dmm ??= new DmmSocketApi();
                await _dmm.ConnectAsync(DmmIpAddress, _manualCts.Token).ConfigureAwait(false);
                await ConfigureDmmAsync(_manualCts.Token).ConfigureAwait(false);
                Log("万用表连接成功");

                await EnsurePowerAsync(_manualCts.Token).ConfigureAwait(false);
                Log("192.168.1.16已输出CH3 5V 1A、CH1 24V 1A，192.168.1.17已输出CH3 5V 1A，192.168.1.15已输出28V 1A");

                IsManualTestInitializing = false;
                IsManualTestRunning = true;
                CanMeasure = true;
                Log("手动测试初始化完成，可以开始开路测试");
            }
            catch (Exception ex)
            {
                await AbortManualTestAsync($"手动测试初始化失败，中止: {ex.Message}").ConfigureAwait(false);
            }
        }

        /// <summary>
        /// 自动测试流程
        /// 自动依次执行开路测试和通路测试，所有针脚都满足判据则“合格”。
        /// </summary>
        private async Task OnAutoTestAsync()
        {
            if (IsAutoTestRunning)
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

            Log("开始自动测试");
            Log($"判据: 开路>={OpenPassThresholdOhm:0}Ω, 通路<={ClosePassThresholdOhm:0}Ω");

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
            Log($"判据: 开路>={OpenPassThresholdOhm:0}Ω, 通路<={ClosePassThresholdOhm:0}Ω");

            try
            {
                await EnsureRelay485Async(on: true, cancellationToken: cancellationToken).ConfigureAwait(false);
                Log("7131控制板已初始化");

                await EnsureArincTxAsync(cancellationToken).ConfigureAwait(false);
                Log("ARINC429板卡连接成功");

                await EnsureMtx532Async(cancellationToken).ConfigureAwait(false);
                Log("MT532板卡连接成功");

                await EnsureResistanceAsync(cancellationToken).ConfigureAwait(false);
                Log("程控电阻板卡连接成功");

                await EnsureLvdtAsync(cancellationToken).ConfigureAwait(false);
                Log("LVDT板卡连接成功");

                _dmm ??= new DmmSocketApi();
                await _dmm.ConnectAsync(DmmIpAddress, cancellationToken).ConfigureAwait(false);
                await ConfigureDmmAsync(cancellationToken).ConfigureAwait(false);
                Log("万用表连接成功");

                await EnsurePowerAsync(cancellationToken).ConfigureAwait(false);
                Log("192.168.1.16已输出CH3 5V 1A、CH1 24V 1A，192.168.1.17已输出CH3 5V 1A，192.168.1.15已输出28V 1A");

                IsAutoTestInitializing = false;
                IsAutoTestRunning = true;

                var okOpen = await MeasureOpenAsync(cancellationToken).ConfigureAwait(false);
                if (!IsAutoTestRunning)
                {
                    return CurrentTestResult ?? "--";
                }

                _measuredOpen = true;

                await Task.Delay(100, cancellationToken).ConfigureAwait(false);

                var okClose = await MeasureCloseAsync(cancellationToken).ConfigureAwait(false);
                if (!IsAutoTestRunning)
                {
                    return CurrentTestResult ?? "--";
                }

                _measuredClose = true;

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
        /// 先发送 ARINC429"输出有效"指令，再测量 Pin9-15 对地阻抗。
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
                Log("开路: 不发送使能指令，保持当前不使能状态");

                Log("开路: 开始测量针脚9~15对地阻抗");

                for (int pin = 9; pin <= 15; pin++)
                {
                    var (value, text) = await MeasureOnePinResistanceAsync(pin, cancellationToken).ConfigureAwait(false);
                    _openValuesByPin[pin] = value;
                    SetOpenPinText(pin, text);

                    if (cancellationToken.IsCancellationRequested)
                        return false;

                    await Task.Delay(50, cancellationToken).ConfigureAwait(false);
                }

                Log("开路: 测量完成");
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
                try { await DisconnectCurrentMeasurementRouteAsync().ConfigureAwait(false); } catch { }
                _measureLock.Release();
            }
        }

        /// <summary>
        /// 通路测试（核心测量方法）
        /// 流程：
        /// 1) 通过 ARINC429 发送"输出有效"指令（Label=65oct）。
        /// 2) 等待 100ms 让输出稳定。
        /// 3) 依次测量 Pin9-15 对地阻抗（应为低阻，<10Ω）。
        /// 4) 测量完成后发送"关闭输出"指令。
        /// </summary>
        private async Task<bool> MeasureCloseAsync(CancellationToken cancellationToken)
        {
            await _measureLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                Log("通路: 按针脚逐项建立前置条件并测量阻抗");
                foreach (var signal in CloseSignals)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Log($"通路: PIN{signal.Pin} => {signal.Name}，开始建立前置条件");

                    await ApplyCloseSignalAsync(signal, cancellationToken).ConfigureAwait(false);
                    await Task.Delay(CloseSignalSettleMs, cancellationToken).ConfigureAwait(false);

                    var (value, text) = await MeasureOnePinResistanceAsync(signal.Pin, cancellationToken).ConfigureAwait(false);
                    _closeValuesByPin[signal.Pin] = value;
                    SetClosePinText(signal.Pin, text);

                    Log($"通路: PIN{signal.Pin} => {signal.Name} 测量完成，开始清理前置条件");
                    await ClearCloseSignalAsync(signal, CancellationToken.None).ConfigureAwait(false);

                    if (cancellationToken.IsCancellationRequested)
                        return false;

                    await Task.Delay(50, cancellationToken).ConfigureAwait(false);
                }

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
                try { await DisconnectCurrentMeasurementRouteAsync().ConfigureAwait(false); } catch { }
                _measureLock.Release();
            }
        }

        /// <summary>
        /// 测量单个针脚对地阻抗（通过矩阵开关连接万用表）
        /// </summary>
        /// <param name="pin">针脚编号（9-15）</param>
        /// <returns>阻抗值和显示文本</returns>
        private async Task<(double? Value, string Text)> MeasureOnePinResistanceAsync(int pin, CancellationToken cancellationToken)
        {
            if (_dmm == null)
                throw new InvalidOperationException("万用表未连接");

            var okRoute = await EnsureMeasurementRouteAsync(pin).ConfigureAwait(false);
            var outNode = $"O{pin + 1}";

            Log($"PIN{pin}: 矩阵连接 {(okRoute ? "成功" : "失败")} - I4-O2(slot{MatrixSlotCommon}), I1-{outNode}(slot{MatrixSlotPinRoute})");
            if (!okRoute)
            {
                return (null, "--");
            }

            await Task.Delay(80, cancellationToken).ConfigureAwait(false);
            DmmReading reading = await _dmm.ReadOnceAsync(DmmMeasureMode.RES, new DmmReadOptions { TimeoutMilliseconds = 8000 }, cancellationToken)
                .ConfigureAwait(false);

            if (reading == null)
            {
                return (null, "--");
            }

            if (reading.IsOverrange)
            {
                return (null, "OL");
            }

            if (reading.Value == null)
            {
                return (null, "--");
            }

            var text = FormatOhmText(reading);
            Log($"PIN{pin}: 读数 Raw={reading.Raw ?? ""} Value={(reading.Value?.ToString("0.###", CultureInfo.InvariantCulture) ?? "--")} Unit={reading.Unit ?? ""}");
            return (reading.Value, text);
        }

        private static string FormatOhmText(DmmReading reading)
        {
            if (reading == null)
                return "--";

            if (reading.IsOverrange)
                return "OL";

            if (reading.Value == null)
                return "--";

            var value = reading.Value.Value;
            if (Math.Abs(value) >= 1_000_000.0)
                return $"{value / 1_000_000.0:0.000} MΩ";

            if (Math.Abs(value) >= 1000.0)
                return $"{value / 1000.0:0.000} kΩ";

            return $"{value:0.000} Ω";
        }

        private async Task ConfigureDmmAsync(CancellationToken cancellationToken)
        {
            if (_dmm == null)
                return;

            await _dmm.SendAsync(DmmTriggerDelayCommand, cancellationToken).ConfigureAwait(false);
        }

        private async Task<bool> EnsureMeasurementRouteAsync(int pin)
        {
            var matrix = MatrixControlService.Instance;

            var okCommon = true;
            if (!_isMatrixCommonConnected)
            {
                okCommon = await matrix.ConnectNodesAsync("I4", "O2", MatrixSlotCommon, MatrixIpAddress).ConfigureAwait(false);
                if (okCommon)
                    _isMatrixCommonConnected = true;
            }

            if (!okCommon)
                return false;

            if (_currentMeasuredPin.HasValue && _currentMeasuredPin.Value != pin)
            {
                _ = await matrix.DisconnectNodesAsync("I1", $"O{_currentMeasuredPin.Value + 1}", MatrixSlotPinRoute, MatrixIpAddress).ConfigureAwait(false);
                _currentMeasuredPin = null;
            }

            if (_currentMeasuredPin == pin)
                return true;

            var okPin = await matrix.ConnectNodesAsync("I1", $"O{pin + 1}", MatrixSlotPinRoute, MatrixIpAddress).ConfigureAwait(false);
            if (okPin)
                _currentMeasuredPin = pin;

            return okPin;
        }

        private async Task DisconnectCurrentMeasurementRouteAsync()
        {
            var matrix = MatrixControlService.Instance;

            if (_currentMeasuredPin.HasValue)
            {
                _ = await matrix.DisconnectNodesAsync("I1", $"O{_currentMeasuredPin.Value + 1}", MatrixSlotPinRoute, MatrixIpAddress).ConfigureAwait(false);
                _currentMeasuredPin = null;
            }

            if (_isMatrixCommonConnected)
            {
                _ = await matrix.DisconnectNodesAsync("I4", "O2", MatrixSlotCommon, MatrixIpAddress).ConfigureAwait(false);
                _isMatrixCommonConnected = false;
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

        private async Task ApplyCloseSignalAsync(CloseSignalDefinition signal, CancellationToken cancellationToken)
        {
            await EnsureArincTxAsync(cancellationToken).ConfigureAwait(false);
            await StartPeriodicWordsAsync(TxChannelIndex, signal.Channel1Words, cancellationToken).ConfigureAwait(false);
            await StartPeriodicWordsAsync(TxChannelIndex2, signal.Channel2Words, cancellationToken).ConfigureAwait(false);

            if (signal.AoChannels.Count > 0)
            {
                await EnsureMtx532Async(cancellationToken).ConfigureAwait(false);
                await SetCloseSignalAoChannelsAsync(signal.AoChannels, CloseSignalAoVoltageV, cancellationToken).ConfigureAwait(false);
            }

            if (signal.DoIndices.Count > 0)
            {
                await EnsureJy7131ReadyAsync(cancellationToken).ConfigureAwait(false);
                foreach (var doIndex in signal.DoIndices)
                {
                    await _jy7131.WriteDoAsync($"DO{doIndex}", true, cancellationToken).ConfigureAwait(false);
                    Log($"7131 DO{doIndex}=1");
                }
            }

            if (signal.RelayChannels.Count > 0)
            {
                await EnsureJy7131ReadyAsync(cancellationToken).ConfigureAwait(false);
                foreach (var relayChannel in signal.RelayChannels)
                {
                    await _jy7131.SetRelayAsync(relayChannel - 1, true, cancellationToken).ConfigureAwait(false);
                    Log($"485继电器第{relayChannel}路已闭合");
                }
            }

            if (!string.IsNullOrWhiteSpace(signal.ResistanceChannel) && signal.ResistanceOhm.HasValue)
            {
                await EnsureResistanceAsync(cancellationToken).ConfigureAwait(false);
                await SetResistanceChannelAsync(signal.ResistanceChannel, signal.ResistanceOhm.Value, cancellationToken).ConfigureAwait(false);
            }

            if (signal.LvdtChannel.HasValue && signal.LvdtVaV.HasValue && signal.LvdtVbV.HasValue)
            {
                await EnsureLvdtAsync(cancellationToken).ConfigureAwait(false);
                await _lvdt.SetVaVbAsync(signal.LvdtChannel.Value, signal.LvdtVaV.Value, signal.LvdtVbV.Value, cancellationToken).ConfigureAwait(false);
                await _lvdt.StartAsync(signal.LvdtChannel.Value, cancellationToken).ConfigureAwait(false);
                Log($"LVDT CH{signal.LvdtChannel.Value}: VA={signal.LvdtVaV.Value:0.0}V, VB={signal.LvdtVbV.Value:0.0}V");
            }
        }

        private async Task ClearCloseSignalAsync(CloseSignalDefinition signal, CancellationToken cancellationToken)
        {
            if (signal.LvdtChannel.HasValue && _lvdt != null)
            {
                try { await _lvdt.StopAsync(signal.LvdtChannel.Value, cancellationToken).ConfigureAwait(false); } catch { }
                try { await _lvdt.SetVaVbAsync(signal.LvdtChannel.Value, 0.0, 0.0, cancellationToken).ConfigureAwait(false); } catch { }
            }

            if (!string.IsNullOrWhiteSpace(signal.ResistanceChannel) && _res != null && _res.IsConnected)
            {
                try { await _res.SetRelayStateAsync(signal.ResistanceChannel, false, false).ConfigureAwait(false); } catch { }
                try { await _res.WriteChannelAsync(signal.ResistanceChannel, 0.0).ConfigureAwait(false); } catch { }
            }

            if (_jy7131 != null)
            {
                foreach (var relayChannel in signal.RelayChannels)
                {
                    try { await _jy7131.SetRelayAsync(relayChannel - 1, false, cancellationToken).ConfigureAwait(false); } catch { }
                }

                foreach (var doIndex in signal.DoIndices)
                {
                    try { await _jy7131.WriteDoAsync($"DO{doIndex}", false, cancellationToken).ConfigureAwait(false); } catch { }
                }
            }

            if (signal.AoChannels.Count > 0 && _mtx532 != null && _mtx532.IsConnected)
            {
                try { await SetCloseSignalAoChannelsAsync(signal.AoChannels, 0.0, cancellationToken).ConfigureAwait(false); } catch { }
            }

            try { await StopPeriodicWordsAsync(TxChannelIndex2, cancellationToken).ConfigureAwait(false); } catch { }
            try { await StopPeriodicWordsAsync(TxChannelIndex, cancellationToken).ConfigureAwait(false); } catch { }
        }

        private async Task ClearAllCloseSignalOutputsAsync(CancellationToken cancellationToken)
        {
            try { await StopPeriodicWordsAsync(TxChannelIndex, cancellationToken).ConfigureAwait(false); } catch { }
            try { await StopPeriodicWordsAsync(TxChannelIndex2, cancellationToken).ConfigureAwait(false); } catch { }

            if (_mtx532 != null && _mtx532.IsConnected)
            {
                try { await SetCloseSignalAoChannelsAsync(CloseSignalAoChannels, 0.0, cancellationToken).ConfigureAwait(false); } catch { }
            }

            if (_lvdt != null)
            {
                try { await _lvdt.StopAsync(LvdtChannel1, cancellationToken).ConfigureAwait(false); } catch { }
                try { await _lvdt.StopAsync(LvdtChannel2, cancellationToken).ConfigureAwait(false); } catch { }
                try { await _lvdt.SetVaVbAsync(LvdtChannel1, 0.0, 0.0, cancellationToken).ConfigureAwait(false); } catch { }
                try { await _lvdt.SetVaVbAsync(LvdtChannel2, 0.0, 0.0, cancellationToken).ConfigureAwait(false); } catch { }
            }

            if (_res != null && _res.IsConnected)
            {
                try { await _res.SetRelayStateAsync("RO0", false, false).ConfigureAwait(false); } catch { }
                try { await _res.SetRelayStateAsync("RO1", false, false).ConfigureAwait(false); } catch { }
                try { await _res.WriteChannelAsync("RO0", 0.0).ConfigureAwait(false); } catch { }
                try { await _res.WriteChannelAsync("RO1", 0.0).ConfigureAwait(false); } catch { }
            }
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

            var loopTask = Task.Run(async () =>
            {
                var nextSpecialAt = DateTime.UtcNow;
                var nextNormalAt = DateTime.UtcNow;

                while (!loopCts.IsCancellationRequested)
                {
                    var now = DateTime.UtcNow;

                    if (specialWords.Length > 0 && now >= nextSpecialAt)
                    {
                        await _arinc.SendWordsSingleAsync(txChannelIndex, specialWords, Art4229Parity.Odd, loopCts.Token).ConfigureAwait(false);
                        nextSpecialAt = now.AddMilliseconds(SpecialArincPeriodMs);
                    }

                    if (normalWords.Length > 0 && now >= nextNormalAt)
                    {
                        await _arinc.SendWordsSingleAsync(txChannelIndex, normalWords, Art4229Parity.Odd, loopCts.Token).ConfigureAwait(false);
                        nextNormalAt = now.AddMilliseconds(ArincPeriodMs);
                    }

                    var nextDelayMs = GetNextLoopDelayMs(nextSpecialAt, nextNormalAt, specialWords.Length > 0, normalWords.Length > 0);
                    await Task.Delay(nextDelayMs, loopCts.Token).ConfigureAwait(false);
                }
            }, loopCts.Token);

            SetTxLoopState(txChannelIndex, loopCts, loopTask);

            if (specialWords.Length > 0)
                Log($"429 TX{txChannelIndex + 1}: 特殊字15ms周期发送 {string.Join(", ", specialWords.Select(w => $"0x{w:X8}"))}");

            if (normalWords.Length > 0)
                Log($"429 TX{txChannelIndex + 1}: 其余字100ms周期发送 {string.Join(", ", normalWords.Select(w => $"0x{w:X8}"))}");
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

        private async Task DisconnectAllMatrixRoutesAsync()
        {
            var matrix = MatrixControlService.Instance;

            _ = await matrix.DisconnectNodesAsync("I4", "O2", MatrixSlotCommon, MatrixIpAddress).ConfigureAwait(false);
            for (int i = 10; i <= 16; i++)
            {
                _ = await matrix.DisconnectNodesAsync("I1", $"O{i}", MatrixSlotPinRoute, MatrixIpAddress).ConfigureAwait(false);
            }

            _isMatrixCommonConnected = false;
            _currentMeasuredPin = null;
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
            Log($"最终结果: {resultText}");

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

            Log("手动测试停止/结束，按初始化反序断开设备...");
            try
            {
                try { await DisconnectCurrentMeasurementRouteAsync().ConfigureAwait(false); } catch { }

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
                    if (_dmm != null)
                    {
                        await _dmm.DisconnectAsync().ConfigureAwait(false);
                    }
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
            IsAutoTestStopping = true;
            IsAutoTestInitializing = false;
            try
            {
                _autoCts?.Cancel();
            }
            catch
            {
            }

            Log("自动测试停止/结束，按初始化反序断开设备...");
            try
            {
                try { await DisconnectCurrentMeasurementRouteAsync().ConfigureAwait(false); } catch { }

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
                    if (_dmm != null)
                    {
                        await _dmm.DisconnectAsync().ConfigureAwait(false);
                    }
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
                }
                else
                {
                    if (_jy7131 != null)
                    {
                        foreach (var signal in CloseSignals)
                        {
                            foreach (var relayChannel in signal.RelayChannels.Distinct())
                            {
                                try { await _jy7131.SetRelayAsync(relayChannel - 1, false, cancellationToken).ConfigureAwait(false); } catch { }
                            }
                            foreach (var doIndex in signal.DoIndices.Distinct())
                            {
                                try { await _jy7131.WriteDoAsync($"DO{doIndex}", false, cancellationToken).ConfigureAwait(false); } catch { }
                            }
                        }
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

        private async Task SetCloseSignalAoChannelsAsync(IReadOnlyList<string> channels, double voltage, CancellationToken cancellationToken)
        {
            if (_mtx532 == null || !_mtx532.IsConnected)
                throw new InvalidOperationException("MTX532未连接");

            var values = CloseSignalAoChannels.ToDictionary(ch => ch, _ => 0.0);
            foreach (var channel in channels)
            {
                if (values.ContainsKey(channel))
                    values[channel] = voltage;
            }

            await _mtx532.WriteOnceDcAsync(values, cancellationToken).ConfigureAwait(false);
            Log($"MT532: {string.Join(", ", values.Select(kvp => $"{kvp.Key}={kvp.Value:0.0}V"))}");
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
            Log($"程控电阻 {channel}={resistanceOhm:0.0}Ω");
        }

        private async Task EnsureLvdtAsync(CancellationToken cancellationToken)
        {
            if (_lvdt == null)
                _lvdt = new Pxi4087LvdtApi();

            if (!_lvdt.IsConnected)
                await _lvdt.ConnectAsync(LvdtSlotIndex, cancellationToken).ConfigureAwait(false);
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
            await _power28.ApplyAsync(PowerSupplyChannel.CH1, Input28VoltageV, Input28CurrentA, cancellationToken).ConfigureAwait(false);
            await _power28.ApplyAsync(PowerSupplyChannel.CH2, Input28VoltageV, Input28CurrentA, cancellationToken).ConfigureAwait(false);
            await _power28.SetOutputEnabledAsync(PowerSupplyChannel.CH1, true, cancellationToken).ConfigureAwait(false);
            await _power28.SetOutputEnabledAsync(PowerSupplyChannel.CH2, true, cancellationToken).ConfigureAwait(false);
            await Task.Delay(300, cancellationToken).ConfigureAwait(false);
        }

        private async Task CleanupPowerAsync()
        {
            try
            {
                if (_power28 != null)
                {
                    try { await _power28.SetOutputEnabledAsync(PowerSupplyChannel.CH1, false, CancellationToken.None).ConfigureAwait(false); } catch { }
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
            }
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
