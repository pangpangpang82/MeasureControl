using Prism.Commands;
using Prism.Mvvm;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using MeasureControl.Events;
using MeasureControl.Models.Devices;
using MeasureControl.Models.Devices.DeviceCategories;
using MeasureControl.Services;
using MeasureControl.Services.HardwareApis;
using Prism.Events;
using Prism.Ioc;

namespace MeasureControl.ViewModels.SingleBoardTest.HydraulicController
{
    public class HC_6_6ViewModel : BindableBase
    {
        private const string DmmIpAddress = "192.168.1.13";
        private const string MatrixIpAddress = "192.168.1.3";
        private const string PowerSupplyIpAddress = "192.168.1.15";
        private const double InputVoltageV = 28.0;
        private const double InputCurrentA = 1.0;
        private const int Relay485ChannelIndex = 6;
        private const int RelayAuxDoIndex = 25;
        private const int RelayGroundDoIndex = 26;
        private const int RxChannelIndex = 2;
        private const int LvdtSlotIndex = 2;
        private const int MatrixSlotExcitationSignal = 6;
        private const int MatrixSlotExcitationCommon = 4;
        private const int ExcitationPlusOutputNode = 17;
        private const int ExcitationMinusOutputNode = 18;
        private const int DmmFrequencyRangeIndex = 20;
        private const double ArincRate = 100000.0;
        private const byte QtyLabelDec = 123;
        private const byte SsmNormal = 3;
        private const int QtyBitLength = 8;
        private const int QtyMsbPosition = 27;
        private const double QtyResolution = 1.0;
        private const int SamplesPerMeasure = 1;
        private const int SampleTimeoutMs = 5000;
        private const int LvdtSettleMs = 500;
        private const int PostSwitchRxFlushMs = 120;
        private const int ExcitationReadSettleMs = 80;
        //private const int ExcitationRestoreSettleMs = 120;
        private const double ExcitationFreqMinHz = 3168.0;
        private const double ExcitationFreqMaxHz = 3232.0;
        private const double ExcitationVoltMinVrms = 5.0;
        private const double ExcitationVoltMaxVrms = 7.0;
        private const double SimulationSumVrms = 6.0;
        private const int LvdtSys1Channel = 1;
        private const int LvdtSys2Channel = 2;
        private const string TestItemName = "油量传感器信号采集测试";

        private readonly SemaphoreSlim _measureLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _relayLock = new SemaphoreSlim(1, 1);
        private readonly IPxiChassisService _pxiChassisService;
        private readonly ISingleBoardTestContextService _singleBoardTestContext;

        private CancellationTokenSource _manualCts;
        private CancellationTokenSource _autoCts;

        private IDmmApi _dmm;
        private IPowerSupplyApi _power;
        private IArt4229Api _arinc;
        private IJy7131Api _jy7131;
        private IPxi4087LvdtApi _lvdt;

        private bool _historyLoaded;
        private bool _isRelay485On;
        private bool _manualAborted;
        private bool _canMeasure;
        private bool _isManualTestRunning;
        private bool _isAutoTestRunning;

        private bool _measuredExc1;
        private bool _measuredExc2;
        private bool _measuredLow;
        private bool _measuredMid;
        private bool _measuredHigh;

        private bool _passedExc1;
        private bool _passedExc2;
        private bool _passedLow;
        private bool _passedMid;
        private bool _passedHigh;

        private string _lastTestTime = "--";
        private string _lastTestResult = "--";
        private string _previousTestTime = "--";
        private string _previousTestResult = "--";
        private string _currentTestResult = "--";

        private string _pin3031FreqText = "--";
        private string _pin3031VoltText = "--";
        private string _pin3334FreqText = "--";
        private string _pin3334VoltText = "--";
        private string _pointLowSys1Text = "--";
        private string _pointLowSys2Text = "--";
        private string _pointMidSys1Text = "--";
        private string _pointMidSys2Text = "--";
        private string _pointHighSys1Text = "--";
        private string _pointHighSys2Text = "--";

        private sealed class QuantityPoint
        {
            public QuantityPoint(string name, double target, double min, double max)
            {
                Name = name;
                Target = target;
                Min = min;
                Max = max;
            }

            public string Name { get; }
            public double Target { get; }
            public double Min { get; }
            public double Max { get; }
        }

        private static readonly QuantityPoint LowPoint = new QuantityPoint("(0,2)%", 1.0, 0.0, 2.0);
        private static readonly QuantityPoint MidPoint = new QuantityPoint("(28,32)%", 30.0, 28.0, 32.0);
        private static readonly QuantityPoint HighPoint = new QuantityPoint("(98,100)%", 99.0, 98.0, 100.0);

        public HC_6_6ViewModel(IPxiChassisService pxiChassisService, ISingleBoardTestContextService singleBoardTestContext)
        {
            _pxiChassisService = pxiChassisService;
            _singleBoardTestContext = singleBoardTestContext;

            ManualTestCommand = new DelegateCommand(async () => await OnManualTestAsync());
            AutoTestCommand = new DelegateCommand(async () => await OnAutoTestAsync());
            MeasureExcitation1Command = new DelegateCommand(async () => await OnMeasureExcitation1Async(), () => CanMeasureExcitation1);
            MeasureExcitation2Command = new DelegateCommand(async () => await OnMeasureExcitation2Async(), () => CanMeasureExcitation2);
            MeasureLowPointCommand = new DelegateCommand(async () => await OnMeasureLowPointAsync(), () => CanMeasureLowPoint);
            MeasureMidPointCommand = new DelegateCommand(async () => await OnMeasureMidPointAsync(), () => CanMeasureMidPoint);
            MeasureHighPointCommand = new DelegateCommand(async () => await OnMeasureHighPointAsync(), () => CanMeasureHighPoint);
            ClearLogCommand = new DelegateCommand(() => Logs.Clear());

            LoadLastTestResultFromProject();
        }

        public DelegateCommand ManualTestCommand { get; }
        public DelegateCommand AutoTestCommand { get; }
        public DelegateCommand MeasureExcitation1Command { get; }
        public DelegateCommand MeasureExcitation2Command { get; }
        public DelegateCommand MeasureLowPointCommand { get; }
        public DelegateCommand MeasureMidPointCommand { get; }
        public DelegateCommand MeasureHighPointCommand { get; }
        public DelegateCommand ClearLogCommand { get; }

        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();

        public bool IsManualTestRunning
        {
            get => _isManualTestRunning;
            private set
            {
                if (SetProperty(ref _isManualTestRunning, value))
                    RefreshMeasureCommands();
            }
        }

        public bool IsAutoTestRunning
        {
            get => _isAutoTestRunning;
            private set
            {
                if (SetProperty(ref _isAutoTestRunning, value))
                    RefreshMeasureCommands();
            }
        }

        public bool CanMeasure
        {
            get => _canMeasure;
            private set
            {
                if (SetProperty(ref _canMeasure, value))
                    RefreshMeasureCommands();
            }
        }

        public bool CanMeasureExcitation1 => CanMeasure && IsManualTestRunning && !_measuredExc1;
        public bool CanMeasureExcitation2 => CanMeasure && IsManualTestRunning && !_measuredExc2;
        public bool CanMeasureLowPoint => CanMeasure && IsManualTestRunning && !_measuredLow;
        public bool CanMeasureMidPoint => CanMeasure && IsManualTestRunning && !_measuredMid;
        public bool CanMeasureHighPoint => CanMeasure && IsManualTestRunning && !_measuredHigh;

        public string CurrentTestResult
        {
            get => _currentTestResult;
            private set => SetProperty(ref _currentTestResult, value);
        }

        public string LastTestTime
        {
            get => _lastTestTime ?? "--";
            private set => SetProperty(ref _lastTestTime, value);
        }

        public string LastTestResult
        {
            get => _lastTestResult ?? "--";
            private set => SetProperty(ref _lastTestResult, value);
        }

        public string PreviousTestTime
        {
            get => _previousTestTime ?? "--";
            private set => SetProperty(ref _previousTestTime, value);
        }

        public string PreviousTestResult
        {
            get => _previousTestResult ?? "--";
            private set => SetProperty(ref _previousTestResult, value);
        }

        public string Pin3031FreqText
        {
            get => _pin3031FreqText;
            private set => SetProperty(ref _pin3031FreqText, value);
        }

        public string Pin3031VoltText
        {
            get => _pin3031VoltText;
            private set => SetProperty(ref _pin3031VoltText, value);
        }

        public string Pin3334FreqText
        {
            get => _pin3334FreqText;
            private set => SetProperty(ref _pin3334FreqText, value);
        }

        public string Pin3334VoltText
        {
            get => _pin3334VoltText;
            private set => SetProperty(ref _pin3334VoltText, value);
        }

        public string PointLowSys1Text
        {
            get => _pointLowSys1Text;
            private set => SetProperty(ref _pointLowSys1Text, value);
        }

        public string PointLowSys2Text
        {
            get => _pointLowSys2Text;
            private set => SetProperty(ref _pointLowSys2Text, value);
        }

        public string PointMidSys1Text
        {
            get => _pointMidSys1Text;
            private set => SetProperty(ref _pointMidSys1Text, value);
        }

        public string PointMidSys2Text
        {
            get => _pointMidSys2Text;
            private set => SetProperty(ref _pointMidSys2Text, value);
        }

        public string PointHighSys1Text
        {
            get => _pointHighSys1Text;
            private set => SetProperty(ref _pointHighSys1Text, value);
        }

        public string PointHighSys2Text
        {
            get => _pointHighSys2Text;
            private set => SetProperty(ref _pointHighSys2Text, value);
        }

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

            ResetStateForNewRun();
            IsManualTestRunning = true;
            CurrentTestResult = "--";
            _manualAborted = false;

            _manualCts?.Cancel();
            _manualCts?.Dispose();
            _manualCts = new CancellationTokenSource();

            Log("开始手动测试");
            Log($"485继电器: 第{Relay485ChannelIndex + 1}路开启，7131 DO{RelayGroundDoIndex}=1");
            Log($"7131: DO{RelayGroundDoIndex} 输出1");
            Log($"ARINC429: RX通道{RxChannelIndex + 1}, 码率 {ArincRate:0}bps, 油量Label=173(oct), SDI=1/2, SSM=0");
            Log($"LVDT: 固定使用PXI槽号 {LvdtSlotIndex}, CH1对应30/31与71/72/73, CH2对应33/34与75/76/77, 激励6Vrms/3200Hz");
            Log($"电源: CH1 {InputVoltageV:0.###}V {InputCurrentA:0.###}A, IP={PowerSupplyIpAddress}");
            Log($"DMM: IP={DmmIpAddress}, 激励测量使用 ACV/FREQ, 频率档位={DmmFrequencyRangeIndex}");
            Log($"矩阵: EXC1+=I1-O{ExcitationPlusOutputNode}(slot{MatrixSlotExcitationSignal}), EXC1-=I1-O{ExcitationMinusOutputNode}(slot{MatrixSlotExcitationSignal}), COM=I4-O2(slot{MatrixSlotExcitationCommon}), IP={MatrixIpAddress}");

            try
            {
                await EnsureRelay485Async(true, _manualCts.Token).ConfigureAwait(false);
                await EnsureGroundDoAsync(true, _manualCts.Token).ConfigureAwait(false);
                await EnsureArincRxAsync(_manualCts.Token).ConfigureAwait(false);
                await EnsureLvdtAsync(_manualCts.Token).ConfigureAwait(false);
                await EnsurePowerAsync(_manualCts.Token).ConfigureAwait(false);
                await EnsureDmmAsync(_manualCts.Token).ConfigureAwait(false);
                await ApplyQuantityOutputsAsync(0.0, _manualCts.Token).ConfigureAwait(false);
                CanMeasure = true;
                Log("手动测试初始化完成，可分别测量激励与三档油量");
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

            ResetStateForNewRun();
            IsAutoTestRunning = true;
            CurrentTestResult = "--";
            _manualAborted = false;
            Log("开始自动测试");

            _autoCts?.Cancel();
            _autoCts?.Dispose();
            _autoCts = new CancellationTokenSource();

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
                _autoCts?.Dispose();
                _autoCts = null;
            }
        }

        private async Task<string> ExecuteAutoTestAsync(CancellationToken cancellationToken)
        {
            await EnsureRelay485Async(true, cancellationToken).ConfigureAwait(false);
            await EnsureGroundDoAsync(true, cancellationToken).ConfigureAwait(false);
            await EnsureArincRxAsync(cancellationToken).ConfigureAwait(false);
            await EnsureLvdtAsync(cancellationToken).ConfigureAwait(false);
            await EnsurePowerAsync(cancellationToken).ConfigureAwait(false);
            await EnsureDmmAsync(cancellationToken).ConfigureAwait(false);

            _passedExc1 = await MeasureExcitationAsync("针脚30/31", LvdtSys1Channel, (f, v) =>
            {
                Pin3031FreqText = f;
                Pin3031VoltText = v;
            }, cancellationToken).ConfigureAwait(false);
            _measuredExc1 = true;

            _passedExc2 = await MeasureExcitationAsync("针脚33/34", LvdtSys2Channel, (f, v) =>
            {
                Pin3334FreqText = f;
                Pin3334VoltText = v;
            }, cancellationToken).ConfigureAwait(false);
            _measuredExc2 = true;

            _passedLow = await MeasureQuantityPointAsync(LowPoint, (sdi, text) =>
            {
                if (sdi == 2)
                    PointLowSys1Text = text;
                else if (sdi == 3)
                    PointLowSys2Text = text;
            }, cancellationToken).ConfigureAwait(false);
            _measuredLow = true;

            _passedMid = await MeasureQuantityPointAsync(MidPoint, (sdi, text) =>
            {
                if (sdi == 2)
                    PointMidSys1Text = text;
                else if (sdi == 3)
                    PointMidSys2Text = text;
            }, cancellationToken).ConfigureAwait(false);
            _measuredMid = true;

            _passedHigh = await MeasureQuantityPointAsync(HighPoint, (sdi, text) =>
            {
                if (sdi == 2)
                    PointHighSys1Text = text;
                else if (sdi == 3)
                    PointHighSys2Text = text;
            }, cancellationToken).ConfigureAwait(false);
            _measuredHigh = true;

            await TryFinalizeAsync().ConfigureAwait(false);
            await StopAutoTestAsync().ConfigureAwait(false);
            return LastTestResult;
        }

        private async Task OnMeasureExcitation1Async()
        {
            var token = _manualCts?.Token ?? CancellationToken.None;
            _passedExc1 = await MeasureExcitationAsync("针脚30/31", LvdtSys1Channel, (f, v) =>
            {
                Pin3031FreqText = f;
                Pin3031VoltText = v;
            }, token).ConfigureAwait(false);
            _measuredExc1 = true;
            RefreshMeasureCommands();
            await TryFinalizeAsync().ConfigureAwait(false);
        }

        private async Task OnMeasureExcitation2Async()
        {
            var token = _manualCts?.Token ?? CancellationToken.None;
            _passedExc2 = await MeasureExcitationAsync("针脚33/34", LvdtSys2Channel, (f, v) =>
            {
                Pin3334FreqText = f;
                Pin3334VoltText = v;
            }, token).ConfigureAwait(false);
            _measuredExc2 = true;
            RefreshMeasureCommands();
            await TryFinalizeAsync().ConfigureAwait(false);
        }

        private async Task OnMeasureLowPointAsync()
        {
            var token = _manualCts?.Token ?? CancellationToken.None;
            _passedLow = await MeasureQuantityPointAsync(LowPoint, (sdi, text) =>
            {
                if (sdi == 2)
                    PointLowSys1Text = text;
                else if (sdi == 3)
                    PointLowSys2Text = text;
            }, token).ConfigureAwait(false);
            _measuredLow = true;
            RefreshMeasureCommands();
            await TryFinalizeAsync().ConfigureAwait(false);
        }

        private async Task OnMeasureMidPointAsync()
        {
            var token = _manualCts?.Token ?? CancellationToken.None;
            _passedMid = await MeasureQuantityPointAsync(MidPoint, (sdi, text) =>
            {
                if (sdi == 2)
                    PointMidSys1Text = text;
                else if (sdi == 3)
                    PointMidSys2Text = text;
            }, token).ConfigureAwait(false);
            _measuredMid = true;
            RefreshMeasureCommands();
            await TryFinalizeAsync().ConfigureAwait(false);
        }

        private async Task OnMeasureHighPointAsync()
        {
            var token = _manualCts?.Token ?? CancellationToken.None;
            _passedHigh = await MeasureQuantityPointAsync(HighPoint, (sdi, text) =>
            {
                if (sdi == 2)
                    PointHighSys1Text = text;
                else if (sdi == 3)
                    PointHighSys2Text = text;
            }, token).ConfigureAwait(false);
            _measuredHigh = true;
            RefreshMeasureCommands();
            await TryFinalizeAsync().ConfigureAwait(false);
        }

        private async Task<bool> MeasureExcitationAsync(string title, int channel, Action<string, string> setTexts, CancellationToken cancellationToken)
        {
            if (!(IsAutoTestRunning || (IsManualTestRunning && CanMeasure)))
            {
                Log($"{title}: 当前未处于测试状态");
                return false;
            }

            await _measureLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await EnsureDmmAsync(cancellationToken).ConfigureAwait(false);
                await ApplyExcitationMeasurementRouteAsync(channel, cancellationToken).ConfigureAwait(false);
                await Task.Delay(ExcitationReadSettleMs, cancellationToken).ConfigureAwait(false);
                var voltageReading = await _dmm.ReadOnceAsync(
                    DmmMeasureMode.ACV,
                    new DmmReadOptions { TimeoutMilliseconds = 8000 },
                    cancellationToken).ConfigureAwait(false);
                var frequencyReading = await _dmm.ReadOnceAsync(
                    DmmMeasureMode.FREQ,
                    new DmmReadOptions { TimeoutMilliseconds = 8000, FrequencyRangeIndex = DmmFrequencyRangeIndex },
                    cancellationToken).ConfigureAwait(false);
                var voltage = voltageReading?.Value;
                var frequency = frequencyReading?.Value;
                var freqText = frequency.HasValue ? $"{frequency.Value:0.0} Hz" : "--";
                var voltText = voltage.HasValue ? $"{voltage.Value:0.00} Vrms" : "--";
                setTexts(freqText, voltText);

                var pass = frequency.HasValue && voltage.HasValue
                    && frequency.Value >= ExcitationFreqMinHz && frequency.Value <= ExcitationFreqMaxHz
                    && voltage.Value >= ExcitationVoltMinVrms && voltage.Value <= ExcitationVoltMaxVrms;

                Log($"{title}: 频率={(frequency.HasValue ? frequency.Value.ToString("0.0") : "--")}Hz, 电压={(voltage.HasValue ? voltage.Value.ToString("0.00") : "--")}Vrms, 结果={(pass ? "合格" : "不合格")}");
                return pass;
            }
            finally
            {
                try
                {
                    await ClearExcitationMeasurementRouteAsync(channel).ConfigureAwait(false);
                }
                finally
                {
                    _measureLock.Release();
                }
            }
        }

        private async Task<bool> MeasureQuantityPointAsync(QuantityPoint point, Action<byte, string> setText, CancellationToken cancellationToken)
        {
            if (!(IsAutoTestRunning || (IsManualTestRunning && CanMeasure)))
            {
                Log($"{point.Name}: 当前未处于测试状态");
                return false;
            }

            await _measureLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await ApplyQuantityOutputsAsync(point.Target, cancellationToken).ConfigureAwait(false);
                await Task.Delay(LvdtSettleMs, cancellationToken).ConfigureAwait(false);
                _ = await _arinc.ReadRxWordsAsync(RxChannelIndex, 4096, false, false, cancellationToken).ConfigureAwait(false);
                await Task.Delay(PostSwitchRxFlushMs, cancellationToken).ConfigureAwait(false);

                var samples = new Dictionary<byte, List<double>>
                {
                    [2] = new List<double>(SamplesPerMeasure),
                    [3] = new List<double>(SamplesPerMeasure)
                };

                Log($"{point.Name}: 已设置LVDT输出，目标油量={point.Target:0.###}%");

                var deadline = DateTime.UtcNow.AddMilliseconds(SampleTimeoutMs);
                var assignedText = new HashSet<byte>();
                while (!cancellationToken.IsCancellationRequested && DateTime.UtcNow <= deadline)
                {
                    var words = await _arinc.ReadRxWordsAsync(RxChannelIndex, maxCount: 512, enableTimeTag: false, enableRateAdaption: false, cancellationToken: cancellationToken)
                        .ConfigureAwait(false);

                    if (words != null && words.Count > 0)
                        Log($"{point.Name}: 本轮收到 {words.Count} 条429字");

                    foreach (var w in words)
                    {
                        var parityOk = _arinc.VerifyOddParity(w.Data429);
                        if (!parityOk)
                        {
                            Log($"{point.Name}: 丢弃429字 Raw=0x{w.Data429:X8}, 原因=奇校验失败");
                            continue;
                        }

                        _arinc.ParseRawWord(w.Data429, out var label, out var sdi, out var data19, out var ssm);
                        Log($"{point.Name}: 429解析 Raw=0x{w.Data429:X8}, Label={label}, SDI={sdi}, SSM={ssm}, Data19=0x{data19:X5}");

                        if (!IsExpectedLabel(label))
                        {
                            Log($"{point.Name}: 丢弃429字 Raw=0x{w.Data429:X8}, 原因=Label不匹配");
                            continue;
                        }

                        if (ssm != SsmNormal)
                        {
                            Log($"{point.Name}: 丢弃429字 Raw=0x{w.Data429:X8}, 原因=SSM={ssm} 不等于期望值 {SsmNormal}");
                            continue;
                        }

                        if (sdi != 2 && sdi != 3)
                        {
                            Log($"{point.Name}: 丢弃429字 Raw=0x{w.Data429:X8}, 原因=SDI={sdi} 不在期望范围[2,3]");
                            continue;
                        }

                        var value = DecodeQuantity(data19);
                        if (!value.HasValue)
                        {
                            Log($"{point.Name}: 丢弃429字 Raw=0x{w.Data429:X8}, 原因=数据解码失败");
                            continue;
                        }

                        Log($"{point.Name}: 命中目标429字 SDI={sdi}, 解码油量={value.Value:0.###}%");

                        var list = samples[sdi];
                        if (list.Count >= SamplesPerMeasure)
                        {
                            Log($"{point.Name}: SDI={sdi} 已满足样本数 {SamplesPerMeasure}，忽略后续数据");
                            continue;
                        }

                        list.Add(value.Value);
                        Log($"{point.Name}: SDI={sdi} 已采样 {list.Count}/{SamplesPerMeasure}");

                        if (list.Count >= SamplesPerMeasure && !assignedText.Contains(sdi))
                        {
                            var avg = list.Average();
                            setText(sdi, $"{avg:0} %");
                            assignedText.Add(sdi);
                            Log($"{point.Name}: SYS{(sdi == 2 ? 1 : 2)} 已收到 {avg:0}%");
                        }
                    }

                    if (samples.Values.All(x => x.Count >= SamplesPerMeasure))
                        break;

                    await Task.Delay(10, cancellationToken).ConfigureAwait(false);
                }

                if (samples[2].Count < SamplesPerMeasure || samples[3].Count < SamplesPerMeasure)
                {
                    if (samples[2].Count < SamplesPerMeasure)
                        setText(2, "超时");

                    if (samples[3].Count < SamplesPerMeasure)
                        setText(3, "超时");

                    Log($"{point.Name}: 接收油量429数据超时");
                    return false;
                }

                var avg1 = samples[2].Average();
                var avg2 = samples[3].Average();

                var pass = IsQuantityInRange(avg1, point) && IsQuantityInRange(avg2, point);
                Log($"{point.Name}: SYS1={avg1:0}%, SYS2={avg2:0}%, 判定范围=[{point.Min:0.###},{point.Max:0.###}]%, 结果={(pass ? "合格" : "不合格")}");
                return pass;
            }
            finally
            {
                try
                {
                    await StopQuantityOutputsAsync().ConfigureAwait(false);
                }
                catch
                {
                }

                _measureLock.Release();
            }
        }

        private static bool IsQuantityInRange(double value, QuantityPoint point)
        {
            return value >= point.Min && value <= point.Max;
        }

        private async Task ApplyQuantityOutputsAsync(double quantityPercent, CancellationToken cancellationToken)
        {
            var (s1, s2) = CalculateSecondaryVoltages(quantityPercent);
            await _lvdt.SetVaVbAsync(LvdtSys1Channel, s1, s2, cancellationToken).ConfigureAwait(false);
            await _lvdt.SetVaVbAsync(LvdtSys2Channel, s1, s2, cancellationToken).ConfigureAwait(false);
            await _lvdt.StartAsync(LvdtSys1Channel, cancellationToken).ConfigureAwait(false);
            await _lvdt.StartAsync(LvdtSys2Channel, cancellationToken).ConfigureAwait(false);
            Log($"LVDT输出: 目标油量={quantityPercent:0.###}%, S1={s1:0.00}Vrms, S2={s2:0.00}Vrms, Sum={SimulationSumVrms:0.00}Vrms");
        }

        private async Task ApplyExcitationMeasurementRouteAsync(int channel, CancellationToken cancellationToken)
        {
            await DisconnectAllExcitationMatrixRoutesAsync().ConfigureAwait(false);

            var signalOutputNode = channel == LvdtSys1Channel ? ExcitationPlusOutputNode : ExcitationMinusOutputNode;
            var matrix = MatrixControlService.Instance;
            var okSignal = await matrix.ConnectNodesAsync("I1", $"O{signalOutputNode}", MatrixSlotExcitationSignal, MatrixIpAddress).ConfigureAwait(false);
            var okCommon = await matrix.ConnectNodesAsync("I4", "O2", MatrixSlotExcitationCommon, MatrixIpAddress).ConfigureAwait(false);

            if (!okSignal || !okCommon)
                throw new InvalidOperationException($"{(channel == LvdtSys1Channel ? "EXC1+" : "EXC1-")} 矩阵切换失败");

            Log($"{(channel == LvdtSys1Channel ? "EXC1+" : "EXC1-")}: 矩阵已连接 I1-O{signalOutputNode}(slot{MatrixSlotExcitationSignal}), I4-O2(slot{MatrixSlotExcitationCommon})");
        }

        private async Task ClearExcitationMeasurementRouteAsync(int channel)
        {
            var signalOutputNode = channel == LvdtSys1Channel ? ExcitationPlusOutputNode : ExcitationMinusOutputNode;
            var matrix = MatrixControlService.Instance;
            try { await matrix.DisconnectNodesAsync("I1", $"O{signalOutputNode}", MatrixSlotExcitationSignal, MatrixIpAddress).ConfigureAwait(false); } catch { }
            try { await matrix.DisconnectNodesAsync("I4", "O2", MatrixSlotExcitationCommon, MatrixIpAddress).ConfigureAwait(false); } catch { }
        }

        private async Task DisconnectAllExcitationMatrixRoutesAsync()
        {
            var matrix = MatrixControlService.Instance;
            try { await matrix.DisconnectNodesAsync("I1", $"O{ExcitationPlusOutputNode}", MatrixSlotExcitationSignal, MatrixIpAddress).ConfigureAwait(false); } catch { }
            try { await matrix.DisconnectNodesAsync("I1", $"O{ExcitationMinusOutputNode}", MatrixSlotExcitationSignal, MatrixIpAddress).ConfigureAwait(false); } catch { }
            try { await matrix.DisconnectNodesAsync("I4", "O2", MatrixSlotExcitationCommon, MatrixIpAddress).ConfigureAwait(false); } catch { }
        }

        private async Task EnsureDmmAsync(CancellationToken cancellationToken)
        {
            _dmm ??= new DmmSocketApi();
            if (!_dmm.IsConnected)
                await _dmm.ConnectAsync(DmmIpAddress, cancellationToken).ConfigureAwait(false);
        }

        private async Task TryFinalizeAsync()
        {
            if (!(_measuredExc1 && _measuredExc2 && _measuredLow && _measuredMid && _measuredHigh))
                return;

            var resultText = (_passedExc1 && _passedExc2 && _passedLow && _passedMid && _passedHigh) ? "合格" : "不合格";
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

            Log($"手动测试停止/结束，正在按反序断开28V、LVDT、429、DO{RelayGroundDoIndex}、485继电器、DMM与矩阵...");
            await RunCleanupExclusiveAsync(async () =>
            {
                await CleanupPowerAsync().ConfigureAwait(false);
                await CleanupLvdtAsync().ConfigureAwait(false);
                await CleanupArincAsync().ConfigureAwait(false);
                await EnsureGroundDoAsync(false, CancellationToken.None).ConfigureAwait(false);
                await EnsureRelay485Async(false, CancellationToken.None).ConfigureAwait(false);
                await CleanupJy7131Async().ConfigureAwait(false);
                await CleanupDmmAsync().ConfigureAwait(false);
                await DisconnectAllExcitationMatrixRoutesAsync().ConfigureAwait(false);
            }).ConfigureAwait(false);
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

            Log($"自动测试停止/结束，正在按反序断开28V、LVDT、429、DO{RelayGroundDoIndex}、485继电器、DMM与矩阵...");
            await RunCleanupExclusiveAsync(async () =>
            {
                await CleanupPowerAsync().ConfigureAwait(false);
                await CleanupLvdtAsync().ConfigureAwait(false);
                await CleanupArincAsync().ConfigureAwait(false);
                await EnsureGroundDoAsync(false, CancellationToken.None).ConfigureAwait(false);
                await EnsureRelay485Async(false, CancellationToken.None).ConfigureAwait(false);
                await CleanupJy7131Async().ConfigureAwait(false);
                await CleanupDmmAsync().ConfigureAwait(false);
                await DisconnectAllExcitationMatrixRoutesAsync().ConfigureAwait(false);
            }).ConfigureAwait(false);
            IsAutoTestRunning = false;
            Log("自动测试已结束");
        }

        private async Task EnsurePowerAsync(CancellationToken cancellationToken)
        {
            _power ??= new PowerSupplySocketApi();
            if (!_power.IsConnected)
                await _power.ConnectAsync(PowerSupplyIpAddress, cancellationToken).ConfigureAwait(false);
            await _power.ApplyAsync(PowerSupplyChannel.CH1, InputVoltageV, InputCurrentA, cancellationToken).ConfigureAwait(false);
            await _power.SetOutputEnabledAsync(PowerSupplyChannel.CH1, true, cancellationToken).ConfigureAwait(false);
            await Task.Delay(300, cancellationToken).ConfigureAwait(false);
        }

        private async Task EnsureLvdtAsync(CancellationToken cancellationToken)
        {
            if (_lvdt == null)
                _lvdt = new Pxi4087LvdtApi();

            if (!_lvdt.IsConnected)
            {
                await _lvdt.ConnectAsync(LvdtSlotIndex, cancellationToken).ConfigureAwait(false);
            }

            var config = CreateSimulationConfig();

            await _lvdt.ConfigureSimulationChannelAsync(LvdtSys1Channel, config, cancellationToken).ConfigureAwait(false);
            await _lvdt.ConfigureSimulationChannelAsync(LvdtSys2Channel, config, cancellationToken).ConfigureAwait(false);
            await _lvdt.StartAsync(LvdtSys1Channel, cancellationToken).ConfigureAwait(false);
            await _lvdt.StartAsync(LvdtSys2Channel, cancellationToken).ConfigureAwait(false);
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

        private bool IsExpectedLabel(byte label)
        {
            return _arinc.ReverseLabel(label) == QtyLabelDec;
        }

        private double? DecodeQuantity(uint data19)
        {
            if (_arinc == null)
                return null;

            try
            {
                var value = _arinc.DecodeUbnr(data19, QtyBitLength, QtyResolution, QtyMsbPosition);
                return Math.Round(value, 0, MidpointRounding.AwayFromZero);
            }
            catch
            {
                return null;
            }
        }

        private (double s1, double s2) CalculateSecondaryVoltages(double quantityPercent)
        {
            var boundedQuantity = Math.Max(0.0, Math.Min(100.0, quantityPercent));
            var diff = (boundedQuantity / 100.0 - 0.5) * SimulationSumVrms;
            var s1 = (SimulationSumVrms + diff) / 2.0;
            var s2 = (SimulationSumVrms - diff) / 2.0; //1.8 4.2  2.4 3.6 1.755 4.47

            return (s1, s2);
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
                AdcRangeIndex = 4
            };
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
                    (d?.Model?.IndexOf("7131", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.Name?.IndexOf("7131", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.Model?.IndexOf("JY7131", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.Name?.IndexOf("JY7131", StringComparison.OrdinalIgnoreCase) >= 0));

                if (device != null)
                    return device;
            }

            return null;
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

                    if (_jy7131 == null)
                    {
                        var device = FindFirstJy7131Device();
                        if (device == null)
                            throw new InvalidOperationException("未找到PXIe-7131(JY7131)板卡，无法开启485继电器");

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
                    Log($"485继电器板 第{Relay485ChannelIndex + 1}路已开启");
                    _isRelay485On = true;
                }
                else
                {
                    if (!_isRelay485On || _jy7131 == null)
                        return;

                    try
                    {
                        await _jy7131.SetRelayAsync(Relay485ChannelIndex, false, cancellationToken).ConfigureAwait(false);
                        Log($"485继电器板 第{Relay485ChannelIndex + 1}路已关闭");
                    }
                    catch (Exception ex)
                    {
                        Log($"关闭485继电器板 第{Relay485ChannelIndex + 1}路失败: {ex.Message}");
                    }

                    _isRelay485On = false;
                }
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

        private async Task EnsureGroundDoAsync(bool on, CancellationToken cancellationToken)
        {
            await _relayLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!on && _jy7131 == null)
                    return;

                if (_jy7131 == null)
                {
                    var device = FindFirstJy7131Device();
                    if (device == null)
                        throw new InvalidOperationException("未找到PXIe-7131(JY7131)板卡，无法控制DO25/DO26");

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

        private async Task RunCleanupExclusiveAsync(Func<Task> cleanupAsync)
        {
            await _measureLock.WaitAsync().ConfigureAwait(false);
            try
            {
                await cleanupAsync().ConfigureAwait(false);
            }
            finally
            {
                _measureLock.Release();
            }
        }

        private async Task CleanupPowerAsync()
        {
            if (_power == null)
                return;

            try { await _power.SetOutputEnabledAsync(PowerSupplyChannel.CH1, false, CancellationToken.None).ConfigureAwait(false); } catch { }
            try { await _power.DisconnectAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
            _power = null;
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

        private async Task CleanupDmmAsync()
        {
            if (_dmm == null)
                return;

            try { await _dmm.DisconnectAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
            try { await _dmm.DisposeAsync().ConfigureAwait(false); } catch { }
            _dmm = null;
        }

        private async Task CleanupLvdtAsync()
        {
            if (_lvdt == null)
                return;

            try { await _lvdt.StopAsync(LvdtSys1Channel, CancellationToken.None).ConfigureAwait(false); } catch { }
            try { await _lvdt.StopAsync(LvdtSys2Channel, CancellationToken.None).ConfigureAwait(false); } catch { }
            try { await _lvdt.ResetAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
            try { await _lvdt.DisposeAsync().ConfigureAwait(false); } catch { }
            _lvdt = null;
        }

        private async Task StopQuantityOutputsAsync()
        {
            if (_lvdt == null)
                return;

            try { await _lvdt.StopAsync(LvdtSys1Channel, CancellationToken.None).ConfigureAwait(false); } catch { }
            try { await _lvdt.StopAsync(LvdtSys2Channel, CancellationToken.None).ConfigureAwait(false); } catch { }
        }

        private async Task CleanupArincAsync()
        {
            if (_arinc == null)
                return;

            try { await _arinc.StopRxAsync(RxChannelIndex, CancellationToken.None).ConfigureAwait(false); } catch { }
            try { await _arinc.CloseRxAsync(RxChannelIndex, CancellationToken.None).ConfigureAwait(false); } catch { }
            try { await _arinc.DisconnectAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
            _arinc = null;
        }

        private void ResetStateForNewRun()
        {
            CanMeasure = false;
            _measuredExc1 = false;
            _measuredExc2 = false;
            _measuredLow = false;
            _measuredMid = false;
            _measuredHigh = false;
            _passedExc1 = false;
            _passedExc2 = false;
            _passedLow = false;
            _passedMid = false;
            _passedHigh = false;
            Pin3031FreqText = "--";
            Pin3031VoltText = "--";
            Pin3334FreqText = "--";
            Pin3334VoltText = "--";
            PointLowSys1Text = "--";
            PointLowSys2Text = "--";
            PointMidSys1Text = "--";
            PointMidSys2Text = "--";
            PointHighSys1Text = "--";
            PointHighSys2Text = "--";
            RefreshMeasureCommands();
        }

        private void RefreshMeasureCommands()
        {
            RaisePropertyChanged(nameof(CanMeasureExcitation1));
            RaisePropertyChanged(nameof(CanMeasureExcitation2));
            RaisePropertyChanged(nameof(CanMeasureLowPoint));
            RaisePropertyChanged(nameof(CanMeasureMidPoint));
            RaisePropertyChanged(nameof(CanMeasureHighPoint));
            MeasureExcitation1Command?.RaiseCanExecuteChanged();
            MeasureExcitation2Command?.RaiseCanExecuteChanged();
            MeasureLowPointCommand?.RaiseCanExecuteChanged();
            MeasureMidPointCommand?.RaiseCanExecuteChanged();
            MeasureHighPointCommand?.RaiseCanExecuteChanged();
        }

        private void Log(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

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
