using Prism.Commands;
using Prism.Mvvm;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using MeasureControl.Models.Devices;
using MeasureControl.Models.Devices.DeviceCategories;
using MeasureControl.Events;
using MeasureControl.Services;
using MeasureControl.Services.HardwareApis;
using Prism.Events;
using Prism.Ioc;

namespace MeasureControl.ViewModels.SingleBoardTest.HydraulicController
{
    public class HC_6_5ViewModel : BindableBase
    {
        private const string PowerSupplyIpAddress = "192.168.1.15";
        private const double InputVoltageV = 28.0;
        private const double InputCurrentA = 1;

        private const int RxChannelIndex = 2;
        private const double ArincRate = 100000.0;

        private const int Relay485ChannelIndex = 6;
        private const int RelayAuxDoIndex = 25;
        private const int RelayGroundDoIndex = 26;

        private const string PressureUnit = "Psid";
        private const int SamplesPerMeasure = 1;
        private const int SampleTimeoutMs = 3000;
        private const int AoSettleMs = 100;
        private const int Mtx532ReadyTimeoutMs = 6000;
        private const int Mtx532ReadyPollMs = 200;
        private const int PostSwitchRxFlushMs = 120;

        private const double Current4mA = 4.0;
        private const double Current20mA = 20.0;
        private const double Current10mA = 10.0;

        private const byte LabelDptRfDec = 56;
        private const byte LabelDptEdpDec = 57;
        private const byte LabelDptSysDec = 58;
        private const byte LabelDptEmpDec = 59;
        private const byte SsmNormal = 0;

        private const int DataBitLength = 9;
        private const double DataResolution = 1.0;
        private const int DataMsbPosition = 27;

        private static readonly string[] AoChannels = { "AO4", "AO5", "AO6", "AO7", "AO8", "AO9" };

        private static readonly DptChannelDefinition[] DptChannels =
        {
            new DptChannelDefinition("RX", "EDP2", "DPT_EDP2", LabelDptEdpDec, 2),
            new DptChannelDefinition("RX", "EMP2B", "DPT_EMP2B", LabelDptEmpDec, 2),
            new DptChannelDefinition("RX", "EMP3B", "DPT_EMP3B", LabelDptEmpDec, 3),
            new DptChannelDefinition("RX", "RF2", "DPT_RF2", LabelDptRfDec, 2),
            new DptChannelDefinition("RX", "SYS2", "DPT_SYS2", LabelDptSysDec, 2),
            new DptChannelDefinition("RX", "SYS3", "DPT_SYS3", LabelDptSysDec, 3),
        };

        private readonly SemaphoreSlim _measureLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _relayLock = new SemaphoreSlim(1, 1);
        private readonly IPxiChassisService _pxiChassisService;
        private readonly ISingleBoardTestContextService _singleBoardTestContext;

        private CancellationTokenSource _manualCts;
        private CancellationTokenSource _autoCts;

        private IPowerSupplyApi _power;
        private IArt4229Api _arinc;
        private IMtx532Api _mtx532;
        private IJy7131Api _jy7131;
        private bool _isRelay485On;

        private const string TestItemName = "压差传感器信号采集测试";

        private bool _canMeasure;
        private bool _measured4mA;
        private bool _measured20mA;
        private bool _measured10mA;
        private bool _passed4mA;
        private bool _passed20mA;
        private bool _passed10mA;
        private bool _manualAborted;
        private bool _historyLoaded;
        private bool _isManualTestRunning;
        private bool _isAutoTestRunning;
        private int _selectedTabIndex;

        private string _lastTestTime = "--";
        private string _lastTestResult = "--";
        private string _previousTestTime = "--";
        private string _previousTestResult = "--";
        private string _currentTestResult = "--";

        private string _DptEdp24mAText = "--";
        private string _dptEmp2B4mAText = "--";
        private string _dptEmp3B4mAText = "--";
        private string _dptSys14mAText = "--";
        private string _dptSys24mAText = "--";
        private string _dptSys34mAText = "--";

        private string _dptEdp2A20mAText = "--";
        private string _dptEmp2B20mAText = "--";
        private string _dptEmp3B20mAText = "--";
        private string _dptSys120mAText = "--";
        private string _dptSys220mAText = "--";
        private string _dptSys320mAText = "--";

        private string _dptEdp2A10mAText = "--";
        private string _dptEmp2B10mAText = "--";
        private string _dptEmp3B10mAText = "--";
        private string _dptSys110mAText = "--";
        private string _dptSys210mAText = "--";
        private string _dptSys310mAText = "--";

        private sealed class DptChannelDefinition
        {
            public DptChannelDefinition(string group, string slotKey, string channelName, byte label, byte sdi)
            {
                Group = group;
                SlotKey = slotKey;
                ChannelName = channelName;
                Label = label;
                Sdi = sdi;
            }

            public string Group { get; }
            public string SlotKey { get; }
            public string ChannelName { get; }
            public byte Label { get; }
            public byte Sdi { get; }
        }

        public HC_6_5ViewModel(IPxiChassisService pxiChassisService, ISingleBoardTestContextService singleBoardTestContext)
        {
            _pxiChassisService = pxiChassisService;
            _singleBoardTestContext = singleBoardTestContext;

            ManualTestCommand = new DelegateCommand(async () => await OnManualTestAsync());
            AutoTestCommand = new DelegateCommand(async () => await OnAutoTestAsync());
            Measure14Command = new DelegateCommand(async () => await OnMeasure14Async(), () => CanMeasure14);
            ClearLogCommand = new DelegateCommand(() => Logs.Clear());

            LoadLastTestResultFromProject();
        }

        public DelegateCommand ManualTestCommand { get; }
        public DelegateCommand AutoTestCommand { get; }
        public DelegateCommand Measure14Command { get; }
        public DelegateCommand ClearLogCommand { get; }

        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();

        public bool IsManualTestRunning
        {
            get => _isManualTestRunning;
            set
            {
                if (SetProperty(ref _isManualTestRunning, value))
                {
                    RaisePropertyChanged(nameof(CanMeasure14));
                    Measure14Command?.RaiseCanExecuteChanged();
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
                    RaisePropertyChanged(nameof(CanMeasure14));
                    Measure14Command?.RaiseCanExecuteChanged();
                }
            }
        }

        public int SelectedTabIndex
        {
            get => _selectedTabIndex;
            set
            {
                if (SetProperty(ref _selectedTabIndex, value))
                {
                    RaisePropertyChanged(nameof(CanMeasure14));
                    Measure14Command?.RaiseCanExecuteChanged();
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
                    RaisePropertyChanged(nameof(CanMeasure14));
                    Measure14Command?.RaiseCanExecuteChanged();
                }
            }
        }

        public bool CanMeasure14
        {
            get
            {
                if (!(IsManualTestRunning && CanMeasure))
                    return false;

                switch (SelectedTabIndex)
                {
                    case 0:
                        return !_measured4mA;
                    case 1:
                        return !_measured20mA;
                    case 2:
                        return !_measured10mA;
                    default:
                        return !_measured4mA;
                }
            }
        }

        private void RefreshMeasureCommand()
        {
            RaisePropertyChanged(nameof(CanMeasure14));
            Measure14Command?.RaiseCanExecuteChanged();
        }

        public string CurrentTestResult
        {
            get => _currentTestResult;
            private set => SetProperty(ref _currentTestResult, value);
        }

        public string LastTestTime
        {
            get
            {
                LoadLastTestResultFromProject();
                return _lastTestTime;
            }
            set => SetProperty(ref _lastTestTime, value);
        }

        public string LastTestResult
        {
            get
            {
                LoadLastTestResultFromProject();
                return _lastTestResult;
            }
            set => SetProperty(ref _lastTestResult, value);
        }

        public string PreviousTestTime
        {
            get
            {
                LoadLastTestResultFromProject();
                return _previousTestTime;
            }
            set => SetProperty(ref _previousTestTime, value);
        }

        public string PreviousTestResult
        {
            get
            {
                LoadLastTestResultFromProject();
                return _previousTestResult;
            }
            set => SetProperty(ref _previousTestResult, value);
        }

        public string DptEdp24mAText { get => _DptEdp24mAText; private set => SetProperty(ref _DptEdp24mAText, value); }
        public string DptEmp2B4mAText { get => _dptEmp2B4mAText; private set => SetProperty(ref _dptEmp2B4mAText, value); }
        public string DptEmp3B4mAText { get => _dptEmp3B4mAText; private set => SetProperty(ref _dptEmp3B4mAText, value); }
        public string DptSys14mAText { get => _dptSys14mAText; private set => SetProperty(ref _dptSys14mAText, value); }
        public string DptSys24mAText { get => _dptSys24mAText; private set => SetProperty(ref _dptSys24mAText, value); }
        public string DptSys34mAText { get => _dptSys34mAText; private set => SetProperty(ref _dptSys34mAText, value); }

        public string DptEdp2A20mAText { get => _dptEdp2A20mAText; private set => SetProperty(ref _dptEdp2A20mAText, value); }
        public string DptEmp2B20mAText { get => _dptEmp2B20mAText; private set => SetProperty(ref _dptEmp2B20mAText, value); }
        public string DptEmp3B20mAText { get => _dptEmp3B20mAText; private set => SetProperty(ref _dptEmp3B20mAText, value); }
        public string DptSys120mAText { get => _dptSys120mAText; private set => SetProperty(ref _dptSys120mAText, value); }
        public string DptSys220mAText { get => _dptSys220mAText; private set => SetProperty(ref _dptSys220mAText, value); }
        public string DptSys320mAText { get => _dptSys320mAText; private set => SetProperty(ref _dptSys320mAText, value); }

        public string DptEdp2A10mAText { get => _dptEdp2A10mAText; private set => SetProperty(ref _dptEdp2A10mAText, value); }
        public string DptEmp2B10mAText { get => _dptEmp2B10mAText; private set => SetProperty(ref _dptEmp2B10mAText, value); }
        public string DptEmp3B10mAText { get => _dptEmp3B10mAText; private set => SetProperty(ref _dptEmp3B10mAText, value); }
        public string DptSys110mAText { get => _dptSys110mAText; private set => SetProperty(ref _dptSys110mAText, value); }
        public string DptSys210mAText { get => _dptSys210mAText; private set => SetProperty(ref _dptSys210mAText, value); }
        public string DptSys310mAText { get => _dptSys310mAText; private set => SetProperty(ref _dptSys310mAText, value); }

        public async Task<string> RunOnceAsync(CancellationToken cancellationToken)
        {
            if (IsAutoTestRunning)
                await StopAutoTestAsync().ConfigureAwait(false);

            if (IsManualTestRunning)
                await StopManualTestAsync().ConfigureAwait(false);

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

        private void LoadLastTestResultFromProject()
        {
            if (_historyLoaded)
                return;

            var testItemNode = _singleBoardTestContext?.GetCurrentTestItemNode(TestItemName);
            if (testItemNode == null)
                return;

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

            _historyLoaded = true;
        }

        private void SaveTestResultToProject()
        {
            var testItemNode = _singleBoardTestContext?.GetCurrentTestItemNode(TestItemName);
            if (testItemNode == null)
                return;

            testItemNode.LastTestTime = PreviousTestTime;
            testItemNode.LastTestResult = PreviousTestResult;

            var eventAggregator = ContainerLocator.Container?.Resolve(typeof(IEventAggregator)) as IEventAggregator;
            eventAggregator?.GetEvent<ProjectModifiedEvent>()?.Publish(new ProjectModifiedEventArgs
            {
                ModificationType = "SingleBoardTestResult",
                Description = $"单板测试结果已更新: {TestItemName}"
            });
        }

        private async Task OnManualTestAsync()
        {
            if (IsManualTestRunning)
            {
                await StopManualTestAsync().ConfigureAwait(false);
                return;
            }

            if (IsAutoTestRunning)
                await StopAutoTestAsync().ConfigureAwait(false);

            IsManualTestRunning = true;
            CurrentTestResult = "--";
            CanMeasure = false;
            _manualAborted = false;
            _measured4mA = false;
            _measured20mA = false;
            _measured10mA = false;
            _passed4mA = false;
            _passed20mA = false;
            _passed10mA = false;

            ResetAllDisplays();

            _manualCts?.Cancel();
            _manualCts?.Dispose();
            _manualCts = new CancellationTokenSource();

            Log("开始手动测试");
            Log($"485继电器: 第7路开启，7131 DO{RelayGroundDoIndex}=1");
            Log($"电源: CH1/CH2 {InputVoltageV:0.###}V {InputCurrentA:0.###}A, IP={PowerSupplyIpAddress}");
            Log($"MTX532: AO4-AO9 六通道同档输出电流等效电压");
            Log($"ARINC429: RX通道{RxChannelIndex + 1}, 码率 {ArincRate:0}bps, DPT按Label+SDI精确匹配, bit19-27 UBNR(9bit) LSB=1, SSM=0");

            try
            {
                await EnsureRelay485Async(on: true, cancellationToken: _manualCts.Token).ConfigureAwait(false);
                await EnsureGroundDoAsync(on: true, cancellationToken: _manualCts.Token).ConfigureAwait(false);
                await EnsureArincRxAsync(_manualCts.Token).ConfigureAwait(false);
                await EnsureMtx532Async(_manualCts.Token).ConfigureAwait(false);
                await EnsurePowerAsync(_manualCts.Token).ConfigureAwait(false);

                CanMeasure = true;
                Log("手动测试初始化完成，可点击测量");
            }
            catch (Exception ex)
            {
                await AbortManualTestAsync($"手动测试初始化失败，中止: {ex.Message}").ConfigureAwait(false);
            }
        }

        private async Task OnAutoTestAsync()
        {
            if (IsAutoTestRunning)
            {
                await StopAutoTestAsync().ConfigureAwait(false);
                return;
            }

            if (IsManualTestRunning)
                await StopManualTestAsync().ConfigureAwait(false);

            IsAutoTestRunning = true;
            CurrentTestResult = "--";
            CanMeasure = false;
            _manualAborted = false;
            _measured4mA = false;
            _measured20mA = false;
            _measured10mA = false;
            _passed4mA = false;
            _passed20mA = false;
            _passed10mA = false;

            ResetAllDisplays();

            _autoCts?.Cancel();
            _autoCts?.Dispose();
            _autoCts = new CancellationTokenSource();

            Log("开始自动测试");

            try
            {
                await EnsureRelay485Async(on: true, cancellationToken: _autoCts.Token).ConfigureAwait(false);
                await EnsureGroundDoAsync(on: true, cancellationToken: _autoCts.Token).ConfigureAwait(false);
                await EnsureArincRxAsync(_autoCts.Token).ConfigureAwait(false);
                await EnsureMtx532Async(_autoCts.Token).ConfigureAwait(false);
                await EnsurePowerAsync(_autoCts.Token).ConfigureAwait(false);

                var ok4 = await MeasureGroupAsync("4mA", Current4mA, Set4mA, _autoCts.Token).ConfigureAwait(false);
                await Task.Delay(80, _autoCts.Token).ConfigureAwait(false);
                var ok20 = await MeasureGroupAsync("20mA", Current20mA, Set20mA, _autoCts.Token).ConfigureAwait(false);
                await Task.Delay(80, _autoCts.Token).ConfigureAwait(false);
                var ok10 = await MeasureGroupAsync("10mA", Current10mA, Set10mA, _autoCts.Token).ConfigureAwait(false);

                _measured4mA = true;
                _measured20mA = true;
                _measured10mA = true;
                _passed4mA = ok4;
                _passed20mA = ok20;
                _passed10mA = ok10;
                await TryFinalizeAsync().ConfigureAwait(false);
                await StopAutoTestAsync().ConfigureAwait(false);
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
                _autoCts?.Dispose();
                _autoCts = null;
            }
        }

        private async Task<string> ExecuteAutoTestAsync(CancellationToken cancellationToken)
        {
            IsAutoTestRunning = true;
            CurrentTestResult = "--";
            CanMeasure = false;
            _manualAborted = false;
            _measured4mA = false;
            _measured20mA = false;
            _measured10mA = false;
            _passed4mA = false;
            _passed20mA = false;
            _passed10mA = false;

            ResetAllDisplays();

            await EnsureRelay485Async(on: true, cancellationToken: cancellationToken).ConfigureAwait(false);
            await EnsureGroundDoAsync(on: true, cancellationToken: cancellationToken).ConfigureAwait(false);
            await EnsureArincRxAsync(cancellationToken).ConfigureAwait(false);
            await EnsureMtx532Async(cancellationToken).ConfigureAwait(false);
            await EnsurePowerAsync(cancellationToken).ConfigureAwait(false);

            var ok4 = await MeasureGroupAsync("4mA", Current4mA, Set4mA, cancellationToken).ConfigureAwait(false);
            await Task.Delay(80, cancellationToken).ConfigureAwait(false);
            var ok20 = await MeasureGroupAsync("20mA", Current20mA, Set20mA, cancellationToken).ConfigureAwait(false);
            await Task.Delay(80, cancellationToken).ConfigureAwait(false);
            var ok10 = await MeasureGroupAsync("10mA", Current10mA, Set10mA, cancellationToken).ConfigureAwait(false);

            _measured4mA = true;
            _measured20mA = true;
            _measured10mA = true;
            _passed4mA = ok4;
            _passed20mA = ok20;
            _passed10mA = ok10;

            await TryFinalizeAsync().ConfigureAwait(false);
            await StopAutoTestAsync().ConfigureAwait(false);
            return LastTestResult;
        }

        private async Task OnMeasure14Async()
        {
            var token = _manualCts?.Token ?? CancellationToken.None;
            var ok = false;
            switch (SelectedTabIndex)
            {
                case 0:
                    ok = await MeasureGroupAsync("4mA", Current4mA, Set4mA, token).ConfigureAwait(false);
                    break;
                case 1:
                    ok = await MeasureGroupAsync("20mA", Current20mA, Set20mA, token).ConfigureAwait(false);
                    break;
                case 2:
                    ok = await MeasureGroupAsync("10mA", Current10mA, Set10mA, token).ConfigureAwait(false);
                    break;
                default:
                    ok = await MeasureGroupAsync("当前档位", Current4mA, Set4mA, token).ConfigureAwait(false);
                    break;
            }

            if (!IsManualTestRunning || _manualAborted)
                return;

            switch (SelectedTabIndex)
            {
                case 0:
                    _measured4mA = true;
                    _passed4mA = ok;
                    break;
                case 1:
                    _measured20mA = true;
                    _passed20mA = ok;
                    break;
                case 2:
                    _measured10mA = true;
                    _passed10mA = ok;
                    break;
                default:
                    _measured4mA = true;
                    _passed4mA = ok;
                    break;
            }
            RefreshMeasureCommand();

            await TryFinalizeAsync().ConfigureAwait(false);
        }

        private void Set4mA(string name, string text)
        {
            switch (name)
            {
                case "EDP2": DptEdp24mAText = text; break;
                case "EMP2B": DptEmp2B4mAText = text; break;
                case "EMP3B": DptEmp3B4mAText = text; break;
                case "RF2": DptSys14mAText = text; break;
                case "SYS2": DptSys24mAText = text; break;
                case "SYS3": DptSys34mAText = text; break;
            }
        }

        private void Set20mA(string name, string text)
        {
            switch (name)
            {
                case "EDP2": DptEdp2A20mAText = text; break;
                case "EMP2B": DptEmp2B20mAText = text; break;
                case "EMP3B": DptEmp3B20mAText = text; break;
                case "RF2": DptSys120mAText = text; break;
                case "SYS2": DptSys220mAText = text; break;
                case "SYS3": DptSys320mAText = text; break;
            }
        }

        private void Set10mA(string name, string text)
        {
            switch (name)
            {
                case "EDP2": DptEdp2A10mAText = text; break;
                case "EMP2B": DptEmp2B10mAText = text; break;
                case "EMP3B": DptEmp3B10mAText = text; break;
                case "RF2": DptSys110mAText = text; break;
                case "SYS2": DptSys210mAText = text; break;
                case "SYS3": DptSys310mAText = text; break;
            }
        }

        private async Task<bool> MeasureGroupAsync(string title, double currentmA, Action<string, string> setTextByName, CancellationToken cancellationToken)
        {
            if (!(IsAutoTestRunning || (IsManualTestRunning && CanMeasure)))
            {
                Log($"{title}: 当前未处于测试状态");
                return false;
            }

            await _measureLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var voltageV = ConvertCurrentToVoltage(currentmA);
                Log($"{title}: 设置AO4-AO9={voltageV:0.###}V（等效 {currentmA:0.###}mA）");
                await SetAo456789Async(voltageV, cancellationToken).ConfigureAwait(false);
                await Task.Delay(AoSettleMs, cancellationToken).ConfigureAwait(false);
                _ = await _arinc.ReadRxWordsAsync(RxChannelIndex, 4096, false, false, cancellationToken).ConfigureAwait(false);
                await Task.Delay(PostSwitchRxFlushMs, cancellationToken).ConfigureAwait(false);
                Log($"{title}: 开始接收DPT数据，按各自Label/SDI/SSM独立匹配");

                var samples = new Dictionary<string, List<double>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["EDP2"] = new List<double>(SamplesPerMeasure),
                    ["EMP2B"] = new List<double>(SamplesPerMeasure),
                    ["EMP3B"] = new List<double>(SamplesPerMeasure),
                    ["RF2"] = new List<double>(SamplesPerMeasure),
                    ["SYS2"] = new List<double>(SamplesPerMeasure),
                    ["SYS3"] = new List<double>(SamplesPerMeasure),
                };

                var deadline = DateTime.UtcNow.AddMilliseconds(SampleTimeoutMs);
                while (!cancellationToken.IsCancellationRequested && DateTime.UtcNow <= deadline)
                {
                    var words = await _arinc.ReadRxWordsAsync(RxChannelIndex, maxCount: 512, enableTimeTag: false, enableRateAdaption: false, cancellationToken: cancellationToken)
                        .ConfigureAwait(false);

                    foreach (var w in words)
                    {
                        if (!_arinc.VerifyOddParity(w.Data429))
                            continue;

                        _arinc.ParseRawWord(w.Data429, out var label, out var wordSdi, out var data19, out var ssm);
                        var definition = ResolveChannel(label, wordSdi);
                        if (definition == null)
                            continue;

                        if (ssm != SsmNormal)
                            continue;

                        var value = DecodeValue(data19);
                        if (!value.HasValue)
                            continue;

                        var list = samples[definition.SlotKey];
                        if (list.Count >= SamplesPerMeasure)
                            continue;

                        list.Add(value.Value);
                        var avg = list.Average();
                        setTextByName(definition.SlotKey, $"{value.Value:0.0} {PressureUnit}");
                    }

                    if (samples.Values.All(l => l.Count >= SamplesPerMeasure))
                    {
                        foreach (var kv in samples)
                        {
                            setTextByName(kv.Key, $"{kv.Value.Average():0.0} {PressureUnit}");
                        }

                        Log($"{title}: 完成");
                        return true;
                    }

                    await Task.Delay(10, cancellationToken).ConfigureAwait(false);
                }

                foreach (var key in samples.Keys)
                {
                    if (samples[key].Count >= SamplesPerMeasure)
                        setTextByName(key, $"{samples[key].Average():0.0} {PressureUnit}");
                    else
                        setTextByName(key, "超时");
                }

                if (IsManualTestRunning)
                {
                    var missing = string.Join(",", samples.Where(kv => kv.Value.Count < SamplesPerMeasure).Select(kv => kv.Key));
                    Log($"{title}: 接收超时，以下通道未获取到{SamplesPerMeasure}帧有效DPT数据: {missing}");
                    Log($"{title}: 本次测量按超时结束处理，结果保留为--，不可重复点击");
                }
                else
                {
                    var missing = string.Join(",", samples.Where(kv => kv.Value.Count < SamplesPerMeasure).Select(kv => kv.Key));
                    Log($"{title}: 接收超时，以下通道未获取到{SamplesPerMeasure}帧有效DPT数据: {missing}");
                }

                return false;
            }
            finally
            {
                try
                {
                    if (_mtx532 != null && _mtx532.IsConnected)
                    {
                        await SetAo456789Async(0.0, CancellationToken.None).ConfigureAwait(false);
                        Log($"{title}: 测量结束，AO4-AO9已停止输出(0V)");
                    }
                }
                catch (Exception ex)
                {
                    Log($"{title}: 测量结束后停止AO输出失败: {ex.Message}");
                }

                _measureLock.Release();
            }
        }

        private DptChannelDefinition ResolveChannel(byte label, byte sdi)
        {
            return DptChannels.FirstOrDefault(d => IsExpectedLabel(label, d.Label) && d.Sdi == sdi);
        }

        private bool IsExpectedLabel(byte label, byte expected)
        {
            return _arinc.ReverseLabel(label) == expected;
        }

        private double? DecodeValue(uint data19)
        {
            var value = _arinc.DecodeUbnr(data19, bitLength: DataBitLength, resolution: DataResolution, msbPosition: DataMsbPosition);
            if (value < 0 || value > 511)
                return null;

            return Math.Round(value, 1, MidpointRounding.AwayFromZero);
        }

        private async Task TryFinalizeAsync()
        {
            if (!(_measured4mA && _measured20mA && _measured10mA))
                return;

            var resultText = (_passed4mA && _passed20mA && _passed10mA) ? "合格" : "不合格";
            var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            CurrentTestResult = resultText;
            PreviousTestTime = now;
            PreviousTestResult = resultText;
            LastTestTime = now;
            LastTestResult = resultText;
            SaveTestResultToProject();
            Log($"最终结果: {resultText}");

            if (IsManualTestRunning)
                await StopManualTestAsync().ConfigureAwait(false);
        }

        private async Task AbortManualTestAsync(string reason)
        {
            _manualAborted = true;
            if (!string.IsNullOrWhiteSpace(reason))
                Log(reason);

            await StopManualTestAsync().ConfigureAwait(false);
        }

        private async Task StopManualTestAsync()
        {
            try
            {
                CanMeasure = false;
                _manualCts?.Cancel();
            }
            catch
            {
            }

            Log($"手动测试停止/结束，正在按反序断开28V、MTX532、429、DO{RelayGroundDoIndex}、485继电器...");
            await CleanupPowerAsync().ConfigureAwait(false);
            await CleanupMtxAsync().ConfigureAwait(false);
            await CleanupArincAsync().ConfigureAwait(false);
            await EnsureGroundDoAsync(on: false, cancellationToken: CancellationToken.None).ConfigureAwait(false);
            await EnsureRelay485Async(on: false, cancellationToken: CancellationToken.None).ConfigureAwait(false);
            await CleanupJy7131Async().ConfigureAwait(false);

            IsManualTestRunning = false;
            Log("手动测试已结束");
        }

        private async Task StopAutoTestAsync()
        {
            try
            {
                _autoCts?.Cancel();
            }
            catch
            {
            }

            Log($"自动测试停止/结束，正在按反序断开28V、MTX532、429、DO{RelayGroundDoIndex}、485继电器...");
            await CleanupPowerAsync().ConfigureAwait(false);
            await CleanupMtxAsync().ConfigureAwait(false);
            await CleanupArincAsync().ConfigureAwait(false);
            await EnsureGroundDoAsync(on: false, cancellationToken: CancellationToken.None).ConfigureAwait(false);
            await EnsureRelay485Async(on: false, cancellationToken: CancellationToken.None).ConfigureAwait(false);
            await CleanupJy7131Async().ConfigureAwait(false);

            IsAutoTestRunning = false;
            Log("自动测试已结束");
        }

        private async Task EnsurePowerAsync(CancellationToken cancellationToken)
        {
            _power ??= new PowerSupplySocketApi();
            await _power.ConnectAsync(PowerSupplyIpAddress, cancellationToken).ConfigureAwait(false);
            await _power.ApplyAsync(PowerSupplyChannel.CH1, InputVoltageV, InputCurrentA, cancellationToken).ConfigureAwait(false);
            await _power.ApplyAsync(PowerSupplyChannel.CH2, InputVoltageV, InputCurrentA, cancellationToken).ConfigureAwait(false);
            await _power.SetOutputEnabledAsync(PowerSupplyChannel.CH1, true, cancellationToken).ConfigureAwait(false);
            await _power.SetOutputEnabledAsync(PowerSupplyChannel.CH2, true, cancellationToken).ConfigureAwait(false);
            await Task.Delay(300, cancellationToken).ConfigureAwait(false);
        }

        private async Task CleanupPowerAsync()
        {
            try
            {
                if (_power != null)
                {
                    try { await _power.SetOutputEnabledAsync(PowerSupplyChannel.CH1, false, CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _power.SetOutputEnabledAsync(PowerSupplyChannel.CH2, false, CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _power.DisconnectAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _power.DisposeAsync().ConfigureAwait(false); } catch { }
                }
            }
            finally
            {
                _power = null;
            }
        }

        private async Task EnsureMtx532Async(CancellationToken cancellationToken)
        {
            if (_mtx532 != null && _mtx532.IsConnected)
                return;

            var device = FindFirstMtx532Device();
            if (device == null)
                throw new InvalidOperationException("未找到 MTX532 模拟量输出设备");

            var slot = device is PxiDeviceBase pxi ? pxi.SlotIndex : 7;
            _mtx532 = new Mtx532Api(device, options: new Mtx532Options { SampleRateHz = 20000.0 }, slotNumber: slot);
            await _mtx532.ConnectAsync(cancellationToken).ConfigureAwait(false);
            await SetAo456789Async(0.0, cancellationToken).ConfigureAwait(false);
            await Task.Delay(300, cancellationToken).ConfigureAwait(false);
            await WaitForMtx532ReadyAsync(cancellationToken).ConfigureAwait(false);
            await _mtx532.StartOutputAsync(cancellationToken).ConfigureAwait(false);
            await Task.Delay(300, cancellationToken).ConfigureAwait(false);
        }

        private async Task WaitForMtx532ReadyAsync(CancellationToken cancellationToken)
        {
            if (_mtx532 == null || !_mtx532.IsConnected)
                throw new InvalidOperationException("MTX532未连接");

            var deadline = DateTime.UtcNow.AddMilliseconds(Mtx532ReadyTimeoutMs);
            while (DateTime.UtcNow <= deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (await _mtx532.CanStartOutputAsync(cancellationToken).ConfigureAwait(false))
                    return;

                await Task.Delay(Mtx532ReadyPollMs, cancellationToken).ConfigureAwait(false);
            }

            throw new InvalidOperationException("MTX532已连接，但在等待超时前仍未准备好输出");
        }

        private async Task CleanupMtxAsync()
        {
            try
            {
                if (_mtx532 != null)
                {
                    try { await _mtx532.StopOutputAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _mtx532.ResetAllToZeroAsync(disableAfterReset: true, cancellationToken: CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _mtx532.DisconnectAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _mtx532.DisposeAsync().ConfigureAwait(false); } catch { }
                }
            }
            finally
            {
                _mtx532 = null;
            }
        }

        private async Task SetAo456789Async(double voltageV, CancellationToken cancellationToken)
        {
            if (_mtx532 == null || !_mtx532.IsConnected)
                throw new InvalidOperationException("MTX532未连接");

            var outputs = AoChannels.ToDictionary(ch => ch, _ => voltageV, StringComparer.OrdinalIgnoreCase);
            await _mtx532.WriteOnceDcAsync(outputs, cancellationToken).ConfigureAwait(false);
        }

        private double ConvertCurrentToVoltage(double currentmA)
        {
            return (currentmA - 21.0) * 10.0 / 21.0;
        }

        private async Task EnsureArincRxAsync(CancellationToken cancellationToken)
        {
            if (_arinc == null)
            {
                var device = FindFirstArincDevice();
                if (device == null)
                    throw new InvalidOperationException("未找到ART4227/ART4229(ARINC429)板卡，无法接收429数据");
                _arinc = new Art4229Api(device, deviceIndex: 0);
            }

            if (!_arinc.IsConnected)
                await _arinc.ConnectAsync(cancellationToken).ConfigureAwait(false);

            await _arinc.OpenRxAsync(RxChannelIndex, cancellationToken).ConfigureAwait(false);
            await _arinc.ConfigureRxAsync(RxChannelIndex, ArincRate, Art4229Parity.Odd, Art4229WordFormat.Standard429, false, 512, false, cancellationToken).ConfigureAwait(false);
            await _arinc.StartRxAsync(RxChannelIndex, cancellationToken).ConfigureAwait(false);
            _ = await _arinc.ReadRxWordsAsync(RxChannelIndex, 4096, false, false, cancellationToken).ConfigureAwait(false);
        }

        private async Task CleanupArincAsync()
        {
            try
            {
                if (_arinc != null)
                {
                    try { await _arinc.StopRxAsync(RxChannelIndex, CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _arinc.CloseRxAsync(RxChannelIndex, CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _arinc.DisconnectAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _arinc.DisposeAsync().ConfigureAwait(false); } catch { }
                }
            }
            finally
            {
                _arinc = null;
            }
        }

        private async Task EnsureRelay485Async(bool on, CancellationToken cancellationToken)
        {
            await _relayLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (on)
                {
                    if (_isRelay485On)
                        return;

                    var device = FindFirstJy7131Device();
                    if (device == null)
                        throw new InvalidOperationException("未找到PXIe-7131(JY7131)板卡，无法开启485继电器");

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

                    await _jy7131.SetRelayAsync(Relay485ChannelIndex, true, cancellationToken).ConfigureAwait(false);
                    _isRelay485On = true;
                    Log($"485继电器板 第{Relay485ChannelIndex + 1}路已开启");
                }
                else
                {
                    if (!_isRelay485On)
                        return;

                    if (_jy7131 != null)
                    {
                        try
                        {
                            await _jy7131.SetRelayAsync(Relay485ChannelIndex, false, cancellationToken).ConfigureAwait(false);
                            Log($"485继电器板 第{Relay485ChannelIndex + 1}路已关闭");
                        }
                        catch (Exception ex)
                        {
                            Log($"关闭继电器板 第{Relay485ChannelIndex + 1}路失败: {ex.Message}");
                        }
                    }

                    _isRelay485On = false;
                }
            }
            finally
            {
                _relayLock.Release();
            }
        }

        private async Task EnsureGroundDoAsync(bool on, CancellationToken cancellationToken)
        {
            await _relayLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_jy7131 == null)
                {
                    var device = FindFirstJy7131Device();
                    if (device == null)
                        throw new InvalidOperationException("未找到PXIe-7131(JY7131)板卡，无法设置DO");

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

                await WriteInitDosAsync(on, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _relayLock.Release();
            }
        }

        private async Task WriteInitDosAsync(bool on, CancellationToken cancellationToken)
        {
            await _jy7131.WriteDoAsync($"DO{RelayAuxDoIndex}", on, cancellationToken).ConfigureAwait(false);
            Log($"7131 DO{RelayAuxDoIndex} 已{(on ? "置位" : "复位")}");

            await _jy7131.WriteDoAsync($"DO{RelayGroundDoIndex}", on, cancellationToken).ConfigureAwait(false);
            Log($"7131 DO{RelayGroundDoIndex} 已{(on ? "置位" : "复位")}");
        }

        private async Task CleanupJy7131Async()
        {
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
                _isRelay485On = false;
            }
        }

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

        private DeviceBase FindFirstJy7131Device()
        {
            var chassisList = _pxiChassisService?.GetAllChassis();
            if (chassisList == null)
                return null;

            foreach (var chassis in chassisList)
            {
                var device = chassis?.Devices?.FirstOrDefault(d =>
                    d is DigitalIODevice ||
                    (d?.Model?.IndexOf("7131", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.DeviceTypeName?.IndexOf("离散量", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.DeviceTypeName?.IndexOf("数字量", StringComparison.OrdinalIgnoreCase) >= 0));

                if (device != null)
                    return device;
            }

            return null;
        }

        private DeviceBase FindFirstMtx532Device()
        {
            var chassisList = _pxiChassisService?.GetAllChassis();
            if (chassisList == null)
                return null;

            foreach (var chassis in chassisList)
            {
                var device = chassis?.Devices?.FirstOrDefault(d =>
                    (d?.Model?.IndexOf("X532", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.Name?.IndexOf("mtx532", StringComparison.OrdinalIgnoreCase) >= 0));

                if (device != null)
                    return device;
            }

            return null;
        }

        private void ResetAllDisplays()
        {
            DptEdp24mAText = "--";
            DptEmp2B4mAText = "--";
            DptEmp3B4mAText = "--";
            DptSys14mAText = "--";
            DptSys24mAText = "--";
            DptSys34mAText = "--";

            DptEdp2A20mAText = "--";
            DptEmp2B20mAText = "--";
            DptEmp3B20mAText = "--";
            DptSys120mAText = "--";
            DptSys220mAText = "--";
            DptSys320mAText = "--";

            DptEdp2A10mAText = "--";
            DptEmp2B10mAText = "--";
            DptEmp3B10mAText = "--";
            DptSys110mAText = "--";
            DptSys210mAText = "--";
            DptSys310mAText = "--";
        }

        private void Log(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke(new Action(() => Logs.Add(line)));
                return;
            }

            Logs.Add(line);
        }
    }
}
