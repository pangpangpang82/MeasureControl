using Prism.Commands;
using Prism.Mvvm;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Globalization;
using MeasureControl.Views.Dialogs;
using System.Runtime.CompilerServices;
using MeasureControl.Events;
using MeasureControl.Models;
using MeasureControl.Models.Devices;
using MeasureControl.Models.Devices.DeviceCategories;
using MeasureControl.Services;
using MeasureControl.Services.HardwareApis;
using Prism.Events;
using Prism.Ioc;

namespace MeasureControl.ViewModels.SingleBoardTest.HydraulicController
{
    public class HC_6_7ViewModel : BindableBase
    {
        private const string DmmIpAddress = "192.168.1.13";
        private const string MatrixIpAddress = "192.168.1.3";
        private const string PowerSupplyIpAddress = "192.168.1.15";
        private const double InputVoltageV = 28.0;
        private const double InputCurrentA = 1.0;
        private const int Relay485ChannelIndex = 6;
        private const int RelayAuxDoIndex = 25;
        //private const int RelayGroundDoIndex = 26;
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
        private const int LvdtSettleMs = 2000;
        private const int PostSwitchRxFlushMs = 120;
        private const int ExcitationReadSettleMs = 200;   // 矩阵切换后信号稳定
        private const int FreqModeSettleMs = 200;          // ACV读完后切FREQ档的稳定等待
        private const string DmmTriggerDelayCommand = "TRIG:DEL 0.5";
        private const double ExcitationFreqMinHz = 3168.0;
        private const double ExcitationFreqMaxHz = 3232.0;
        private const double ExcitationVoltMinVrms = 5.0;
        private const double ExcitationVoltMaxVrms = 7.0;
        private const double SimulationSumVrms = 6.0;
        private const int LvdtSys1Channel = 1;
        private const int LvdtSys2Channel = 2;
        private const string TestItemName = "油量传感器信号采集测试";
        private const string LvdtVaSuffix = "_VA";
        private const string LvdtVbSuffix = "_VB";

        private readonly SemaphoreSlim _measureLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _relayLock = new SemaphoreSlim(1, 1);
        private readonly IPxiChassisService _pxiChassisService;
        private readonly ISingleBoardTestContextService _singleBoardTestContext;
        private readonly IBoardPowerService _boardPowerService;

        private CancellationTokenSource _manualCts;
        private CancellationTokenSource _autoCts;

        private IDmmApi _dmmSocket;
        private IPowerSupplyApi _power;
        private IArt4229Api _arinc;
        private IJy7131Api _jy7131;
        private IPxi4087LvdtApi _lvdt;

        private bool _historyLoaded;
        private bool _isRelay485On;
        private bool _manualAborted;
        private bool _isManualTestRunning;
        private bool _isAutoTestRunning;
        private bool _isManualTestInitializing;
        private bool _isAutoTestInitializing;
        private bool _isManualTestStopping;
        private bool _isAutoTestStopping;
        private bool _canMeasure;

        private bool _measuredExc1;
        private bool _measuredExc2;
        private bool _measuredLow;
        private bool _measuredMid;
        private bool _measuredHigh;
        private double _currentQuantityPercent;

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
        private string _customRangeSys1Text = "--";
        private string _customRangeSys2Text = "--";
        private double? _scriptQtySys1;
        private double? _scriptQtySys2;
        private string _manualRangeLowInput = "28";
        private string _manualRangeHighInput = "32";

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

        public HC_6_7ViewModel(IPxiChassisService pxiChassisService, ISingleBoardTestContextService singleBoardTestContext, IBoardPowerService hydraulicPowerService)
        {
            _pxiChassisService = pxiChassisService;
            _singleBoardTestContext = singleBoardTestContext;
            _boardPowerService = hydraulicPowerService;

            ManualTestCommand = new DelegateCommand(async () => await OnManualTestAsync());
            AutoTestCommand = new DelegateCommand(async () => await OnAutoTestAsync());
            MeasureExcitation1Command = new DelegateCommand(async () => await OnMeasureExcitation1Async(), () => CanMeasureExcitation1);
            MeasureExcitation2Command = new DelegateCommand(async () => await OnMeasureExcitation2Async(), () => CanMeasureExcitation2);
            MeasureLowPointCommand = new DelegateCommand(async () => await OnMeasureLowPointAsync(), () => CanMeasureLowPoint);
            MeasureMidPointCommand = new DelegateCommand(async () => await OnMeasureMidPointAsync(), () => CanMeasureMidPoint);
            MeasureHighPointCommand = new DelegateCommand(async () => await OnMeasureHighPointAsync(), () => CanMeasureHighPoint);
            MeasureCustomRangeCommand = new DelegateCommand(async () => await OnMeasureCustomRangeAsync(), () => CanMeasureCustomRange);
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
        public DelegateCommand MeasureCustomRangeCommand { get; }
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
            private set
            {
                if (SetProperty(ref _isManualTestRunning, value))
                {
                    RaisePropertyChanged(nameof(CanStartManualTest));
                    RaisePropertyChanged(nameof(CanStartAutoTest));
                    RefreshMeasureCommands();
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
                    RaisePropertyChanged(nameof(CanStartManualTest));
                    RaisePropertyChanged(nameof(CanStartAutoTest));
                    RefreshMeasureCommands();
                }
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

        public bool CanMeasureExcitation1 => CanMeasure && IsManualTestRunning;
        public bool CanMeasureExcitation2 => CanMeasure && IsManualTestRunning;
        public bool CanMeasureLowPoint => CanMeasure && IsManualTestRunning;
        public bool CanMeasureMidPoint => CanMeasure && IsManualTestRunning;
        public bool CanMeasureHighPoint => CanMeasure && IsManualTestRunning;
        public bool CanMeasureCustomRange => CanMeasure && IsManualTestRunning && TryCreateCustomRangePoint(out _);
        public bool CanStartManualTest => !IsManualTestBusy && !IsAutoTestBusy && !IsAutoTestRunning;
        public bool CanStartAutoTest => !IsManualTestBusy && !IsAutoTestBusy && !IsManualTestRunning;

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

        public double? Pin3031FreqValue => TryParseMeasurementValue(_pin3031FreqText, "Hz", out var v1) ? v1 : (double?)null;

        public bool IsPin3031FreqPass => TryParseMeasurementValue(_pin3031FreqText, "Hz", out var value)
            && value >= ExcitationFreqMinHz
            && value <= ExcitationFreqMaxHz;

        public bool IsPin3031Pass => _passedExc1;

        public string Pin3031VoltText
        {
            get => _pin3031VoltText;
            private set => SetProperty(ref _pin3031VoltText, value);
        }

        public double? Pin3031VoltValue => TryParseMeasurementValue(_pin3031VoltText, "Vrms", out var v2) ? v2 : (double?)null;

        public bool IsPin3031VoltPass => TryParseMeasurementValue(_pin3031VoltText, "Vrms", out var value)
            && value >= ExcitationVoltMinVrms
            && value <= ExcitationVoltMaxVrms;

        public string Pin3334FreqText
        {
            get => _pin3334FreqText;
            private set => SetProperty(ref _pin3334FreqText, value);
        }

        public double? Pin3334FreqValue => TryParseMeasurementValue(_pin3334FreqText, "Hz", out var v3) ? v3 : (double?)null;

        public bool IsPin3334FreqPass => TryParseMeasurementValue(_pin3334FreqText, "Hz", out var value)
            && value >= ExcitationFreqMinHz
            && value <= ExcitationFreqMaxHz;

        public bool IsPin3334Pass => _passedExc2;

        public string Pin3334VoltText
        {
            get => _pin3334VoltText;
            private set => SetProperty(ref _pin3334VoltText, value);
        }

        public double? Pin3334VoltValue => TryParseMeasurementValue(_pin3334VoltText, "Vrms", out var v4) ? v4 : (double?)null;

        public bool IsPin3334VoltPass => TryParseMeasurementValue(_pin3334VoltText, "Vrms", out var value)
            && value >= ExcitationVoltMinVrms
            && value <= ExcitationVoltMaxVrms;

        public string PointLowSys1Text
        {
            get => _pointLowSys1Text;
            private set => SetProperty(ref _pointLowSys1Text, value);
        }

        public bool IsPointLowSys1Pass => TryParsePercent(_pointLowSys1Text, out var value) && IsQuantityInRange(value, LowPoint);

        public string PointLowSys2Text
        {
            get => _pointLowSys2Text;
            private set => SetProperty(ref _pointLowSys2Text, value);
        }

        public bool IsPointLowSys2Pass => TryParsePercent(_pointLowSys2Text, out var value) && IsQuantityInRange(value, LowPoint);

        public string PointMidSys1Text
        {
            get => _pointMidSys1Text;
            private set => SetProperty(ref _pointMidSys1Text, value);
        }

        public bool IsPointMidSys1Pass => TryParsePercent(_pointMidSys1Text, out var value) && IsQuantityInRange(value, MidPoint);

        public string PointMidSys2Text
        {
            get => _pointMidSys2Text;
            private set => SetProperty(ref _pointMidSys2Text, value);
        }

        public bool IsPointMidSys2Pass => TryParsePercent(_pointMidSys2Text, out var value) && IsQuantityInRange(value, MidPoint);

        public string PointHighSys1Text
        {
            get => _pointHighSys1Text;
            private set => SetProperty(ref _pointHighSys1Text, value);
        }

        public bool IsPointHighSys1Pass => TryParsePercent(_pointHighSys1Text, out var value) && IsQuantityInRange(value, HighPoint);

        public string PointHighSys2Text
        {
            get => _pointHighSys2Text;
            private set => SetProperty(ref _pointHighSys2Text, value);
        }

        public bool IsPointHighSys2Pass => TryParsePercent(_pointHighSys2Text, out var value) && IsQuantityInRange(value, HighPoint);

        public string CustomRangeSys1Text
        {
            get => _customRangeSys1Text;
            private set => SetProperty(ref _customRangeSys1Text, value);
        }

        public string CustomRangeSys2Text
        {
            get => _customRangeSys2Text;
            private set => SetProperty(ref _customRangeSys2Text, value);
        }

        public double? ScriptQtySys1Value => _scriptQtySys1;

        public double? ScriptQtySys2Value => _scriptQtySys2;

        public string ManualRangeLowInput
        {
            get => _manualRangeLowInput;
            set
            {
                var normalized = NormalizeIntegerInput(value);
                if (SetProperty(ref _manualRangeLowInput, normalized))
                {
                    RaisePropertyChanged(nameof(CanMeasureCustomRange));
                    MeasureCustomRangeCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        public string ManualRangeHighInput
        {
            get => _manualRangeHighInput;
            set
            {
                var normalized = NormalizeIntegerInput(value);
                if (SetProperty(ref _manualRangeHighInput, normalized))
                {
                    RaisePropertyChanged(nameof(CanMeasureCustomRange));
                    MeasureCustomRangeCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        public void NormalizeManualRangeInputs(string preferredSide)
        {
            var lowText = NormalizeIntegerInput(ManualRangeLowInput);
            var highText = NormalizeIntegerInput(ManualRangeHighInput);

            var hasLow = int.TryParse(lowText, NumberStyles.None, CultureInfo.InvariantCulture, out var low);
            var hasHigh = int.TryParse(highText, NumberStyles.None, CultureInfo.InvariantCulture, out var high);

            if (hasLow)
                low = Math.Max(0, Math.Min(100, low));

            if (hasHigh)
                high = Math.Max(0, Math.Min(100, high));

            if (hasLow && hasHigh && low >= high)
            {
                if (string.Equals(preferredSide, "Low", StringComparison.Ordinal))
                {
                    if (high <= 0)
                    {
                        low = 0;
                        high = 1;
                    }
                    else
                    {
                        low = high - 1;
                    }
                }
                else
                {
                    if (low >= 100)
                    {
                        low = 99;
                        high = 100;
                    }
                    else
                    {
                        high = low + 1;
                    }
                }
            }

            if (hasLow)
                ManualRangeLowInput = low.ToString(CultureInfo.InvariantCulture);

            if (hasHigh)
                ManualRangeHighInput = high.ToString(CultureInfo.InvariantCulture);
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

        public async Task RunWithScriptLvdtAsync(double va1, double vb1, double va2, double vb2, CancellationToken cancellationToken)
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
                await ExecuteScriptLvdtTestAsync(va1, vb1, va2, vb2, _autoCts.Token).ConfigureAwait(false);
            }
            finally
            {
                IsAutoTestInitializing = false;
                _autoCts?.Dispose();
                _autoCts = null;
            }
        }

        private async Task ExecuteScriptLvdtTestAsync(double va1, double vb1, double va2, double vb2, CancellationToken cancellationToken)
        {
            _scriptQtySys1 = null;
            _scriptQtySys2 = null;
            CustomRangeSys1Text = "--";
            CustomRangeSys2Text = "--";
            IsAutoTestInitializing = true;
            IsAutoTestStopping = false;
            PreviousTestTime = "--";
            Log($"脚本LVDT测试: Sys1(Va={va1:0.##}V Vb={vb1:0.##}V), Sys2(Va={va2:0.##}V Vb={vb2:0.##}V)");

            try
            {
                await EnsureRelay485Async(true, cancellationToken).ConfigureAwait(false);
                await EnsureGroundDoAsync(true, cancellationToken).ConfigureAwait(false);
                await EnsureArincRxAsync(cancellationToken).ConfigureAwait(false);
                await EnsurePowerAsync(cancellationToken).ConfigureAwait(false);
                IsAutoTestInitializing = false;
                IsAutoTestRunning = true;

                _passedExc1 = await MeasureExcitationAsync("针脚30/31", LvdtSys1Channel, (f, v) =>
                {
                    Pin3031FreqText = f;
                    Pin3031VoltText = v;
                }, cancellationToken).ConfigureAwait(false);
                if (!IsAutoTestRunning) return;
                _measuredExc1 = true;

                _passedExc2 = await MeasureExcitationAsync("针脚33/34", LvdtSys2Channel, (f, v) =>
                {
                    Pin3334FreqText = f;
                    Pin3334VoltText = v;
                }, cancellationToken).ConfigureAwait(false);
                if (!IsAutoTestRunning) return;
                _measuredExc2 = true;

                await EnsureLvdtAsync(cancellationToken).ConfigureAwait(false);

                await MeasureQuantityWithVaVbAsync(va1, vb1, va2, vb2, (sdi, text) =>
                {
                    if (sdi == 2) CustomRangeSys1Text = text;
                    else if (sdi == 3) CustomRangeSys2Text = text;
                }, cancellationToken).ConfigureAwait(false);

                await StopAutoTestAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Log("脚本LVDT测试已停止");
                await StopAutoTestAsync().ConfigureAwait(false);
                throw;
            }
            catch (Exception ex)
            {
                Log($"脚本LVDT测试异常: {ex.Message}");
                await StopAutoTestAsync().ConfigureAwait(false);
                throw;
            }
        }

        private async Task<bool> MeasureQuantityWithVaVbAsync(
            double va1, double vb1, double va2, double vb2,
            Action<byte, string> setText,
            CancellationToken cancellationToken)
        {
            if (!IsAutoTestRunning && !IsManualTestRunning)
            {
                Log("脚本油量测试: 当前未处于测试状态");
                return false;
            }

            await _measureLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_lvdt == null)
                    throw new InvalidOperationException("LVDT设备未初始化");

                Log($"脚本油量: 设置 Sys1(Va={va1:0.##}V Vb={vb1:0.##}V), Sys2(Va={va2:0.##}V Vb={vb2:0.##}V)");
                await _lvdt.SetVaVbAsync(LvdtSys1Channel, va1, vb1, cancellationToken).ConfigureAwait(false);
                await _lvdt.SetVaVbAsync(LvdtSys2Channel, va2, vb2, cancellationToken).ConfigureAwait(false);
                await _lvdt.StartAsync(LvdtSys1Channel, cancellationToken).ConfigureAwait(false);
                await _lvdt.StartAsync(LvdtSys2Channel, cancellationToken).ConfigureAwait(false);
                await Task.Delay(LvdtSettleMs, cancellationToken).ConfigureAwait(false);
                await DrainArincBufferAsync(cancellationToken).ConfigureAwait(false);
                await Task.Delay(PostSwitchRxFlushMs, cancellationToken).ConfigureAwait(false);

                var samples = new Dictionary<byte, List<double>>
                {
                    [2] = new List<double>(SamplesPerMeasure),
                    [3] = new List<double>(SamplesPerMeasure)
                };
                var assignedText = new HashSet<byte>();
                var deadline = DateTime.UtcNow.AddMilliseconds(SampleTimeoutMs);
                while (!cancellationToken.IsCancellationRequested && DateTime.UtcNow <= deadline)
                {
                    var words = await _arinc.ReadRxWordsAsync(RxChannelIndex, maxCount: 512, enableTimeTag: false, enableRateAdaption: false, cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
                    if (words != null)
                        foreach (var w in words)
                        {
                            _arinc.ParseRawWord(w.Data429, out var label, out var sdi, out var data19, out var ssm);
                            if (!IsExpectedLabel(label) || ssm != SsmNormal || (sdi != 2 && sdi != 3)) continue;
                            var value = DecodeQuantity(data19);
                            if (!value.HasValue) continue;
                            var list = samples[sdi];
                            if (list.Count >= SamplesPerMeasure) continue;
                            list.Add(value.Value);
                            if (list.Count >= SamplesPerMeasure && !assignedText.Contains(sdi))
                            {
                                var avg = list.Average();
                                setText(sdi, $"{avg:0} %");
                                assignedText.Add(sdi);
                                if (sdi == 2) _scriptQtySys1 = avg;
                                else if (sdi == 3) _scriptQtySys2 = avg;
                                Log($"脚本油量: SDI={sdi}, 均値={avg:0}%");
                            }
                        }
                    if (samples.Values.All(x => x.Count >= SamplesPerMeasure)) break;
                    await Task.Delay(10, cancellationToken).ConfigureAwait(false);
                }

                if (samples[2].Count < SamplesPerMeasure) { setText(2, "超时"); Log("脚本油量 Sys1 超时"); }
                if (samples[3].Count < SamplesPerMeasure) { setText(3, "超时"); Log("脚本油量 Sys2 超时"); }
                return samples[2].Count >= SamplesPerMeasure && samples[3].Count >= SamplesPerMeasure;
            }
            finally
            {
                _measureLock.Release();
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
                await StopAutoTestAsync().ConfigureAwait(false);

            ResetStateForNewRun();
            IsManualTestInitializing = true;
            IsManualTestStopping = false;
            CurrentTestResult = "--";
            PreviousTestTime = "--";
            _manualAborted = false;

            _manualCts?.Cancel();
            _manualCts?.Dispose();
            _manualCts = new CancellationTokenSource();

            Log("开始手动测试");
            Log("正在初始化设备...");

            try
            {
                await EnsureRelay485Async(true, _manualCts.Token).ConfigureAwait(false);
                await EnsureGroundDoAsync(true, _manualCts.Token).ConfigureAwait(false);
                await EnsureArincRxAsync(_manualCts.Token).ConfigureAwait(false);
                await EnsureLvdtAsync(_manualCts.Token).ConfigureAwait(false);
                await EnsurePowerAsync(_manualCts.Token).ConfigureAwait(false);
                await ApplyQuantityOutputsAsync(0.0, _manualCts.Token).ConfigureAwait(false);
                IsManualTestInitializing = false;
                IsManualTestRunning = true;
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
                await StopManualTestAsync().ConfigureAwait(false);

            ResetStateForNewRun();
            IsAutoTestInitializing = true;
            IsAutoTestStopping = false;
            CurrentTestResult = "--";
            PreviousTestTime = "--";
            _manualAborted = false;
            Log("开始自动测试");
            Log("正在初始化设备...");


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
                IsAutoTestInitializing = false;
                _autoCts?.Dispose();
                _autoCts = null;
            }
        }

        private async Task<string> ExecuteAutoTestAsync(CancellationToken cancellationToken)
        {
            PreviousTestTime = "--";
            await EnsureRelay485Async(true, cancellationToken).ConfigureAwait(false);
            await EnsureGroundDoAsync(true, cancellationToken).ConfigureAwait(false);
            await EnsureArincRxAsync(cancellationToken).ConfigureAwait(false);
            await EnsurePowerAsync(cancellationToken).ConfigureAwait(false);
            IsAutoTestInitializing = false;
            IsAutoTestRunning = true;

            // 激励测量在 LVDT 启动前执行，避免 LVDT 输出干扰矩阵测量回路
            _passedExc1 = await MeasureExcitationAsync("针脚30/31", LvdtSys1Channel, (f, v) =>
            {
                Pin3031FreqText = f;
                Pin3031VoltText = v;
            }, cancellationToken).ConfigureAwait(false);
            if (!IsAutoTestRunning)
                return CurrentTestResult ?? "--";
            _measuredExc1 = true;

            _passedExc2 = await MeasureExcitationAsync("针脚33/34", LvdtSys2Channel, (f, v) =>
            {
                Pin3334FreqText = f;
                Pin3334VoltText = v;
            }, cancellationToken).ConfigureAwait(false);
            if (!IsAutoTestRunning)
                return CurrentTestResult ?? "--";
            _measuredExc2 = true;

            // 激励测量完成后启动 LVDT，供油量档位测量使用
            await EnsureLvdtAsync(cancellationToken).ConfigureAwait(false);

            _passedLow = await MeasureQuantityPointAsync(LowPoint, (sdi, text) =>
            {
                if (sdi == 2)
                    PointLowSys1Text = text;
                else if (sdi == 3)
                    PointLowSys2Text = text;
            }, cancellationToken).ConfigureAwait(false);
            if (!IsAutoTestRunning)
                return CurrentTestResult ?? "--";
            _measuredLow = true;

            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
            _passedMid = await MeasureQuantityPointAsync(MidPoint, (sdi, text) =>
            {
                if (sdi == 2)
                    PointMidSys1Text = text;
                else if (sdi == 3)
                    PointMidSys2Text = text;
            }, cancellationToken).ConfigureAwait(false);
            if (!IsAutoTestRunning)
                return CurrentTestResult ?? "--";
            _measuredMid = true;

            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
            _passedHigh = await MeasureQuantityPointAsync(HighPoint, (sdi, text) =>
            {
                if (sdi == 2)
                    PointHighSys1Text = text;
                else if (sdi == 3)
                    PointHighSys2Text = text;
            }, cancellationToken).ConfigureAwait(false);
            if (!IsAutoTestRunning)
                return CurrentTestResult ?? "--";
            _measuredHigh = true;

            await TryFinalizeAsync().ConfigureAwait(false);
            await StopAutoTestAsync().ConfigureAwait(false);
            return LastTestResult;
        }

        private async Task OnMeasureExcitation1Async()
        {
            Pin3031FreqText = "--";
            Pin3031VoltText = "--";
            CanMeasure = false;
            var token = _manualCts?.Token ?? CancellationToken.None;
            _passedExc1 = await MeasureExcitationAsync("针脚30/31", LvdtSys1Channel, (f, v) =>
            {
                Pin3031FreqText = f;
                Pin3031VoltText = v;
            }, token).ConfigureAwait(false);
            CanMeasure = IsManualTestRunning;
            if (!IsManualTestRunning || _manualAborted) return;
            _measuredExc1 = true;
            RefreshMeasureCommands();
            await TryFinalizeAsync().ConfigureAwait(false);
        }

        private async Task OnMeasureCustomRangeAsync()
        {
            if (!TryCreateCustomRangePoint(out var point))
            {
                Log("自定义油量范围无效：请输入整数，且左侧必须小于右侧");
                RefreshMeasureCommands();
                return;
            }

            CustomRangeSys1Text = "--";
            CustomRangeSys2Text = "--";
            CanMeasure = false;
            var token = _manualCts?.Token ?? CancellationToken.None;
            var pass = await MeasureQuantityPointAsync(point, (sdi, text) =>
            {
                if (sdi == 2)
                    CustomRangeSys1Text = text;
                else if (sdi == 3)
                    CustomRangeSys2Text = text;
            }, token).ConfigureAwait(false);
            CanMeasure = IsManualTestRunning;

            if (!IsManualTestRunning || _manualAborted)
                return;

            Log($"{point.Name}: 自定义区间测量完成，结果={(pass ? "PASS" : "FAIL")}，可继续测量");
        }

        private async Task OnMeasureExcitation2Async()
        {
            Pin3334FreqText = "--";
            Pin3334VoltText = "--";
            CanMeasure = false;
            var token = _manualCts?.Token ?? CancellationToken.None;
            _passedExc2 = await MeasureExcitationAsync("针脚33/34", LvdtSys2Channel, (f, v) =>
            {
                Pin3334FreqText = f;
                Pin3334VoltText = v;
            }, token).ConfigureAwait(false);
            CanMeasure = IsManualTestRunning;
            if (!IsManualTestRunning || _manualAborted) return;
            _measuredExc2 = true;
            RefreshMeasureCommands();
            await TryFinalizeAsync().ConfigureAwait(false);
        }

        private async Task OnMeasureLowPointAsync()
        {
            PointLowSys1Text = "--";
            PointLowSys2Text = "--";
            CanMeasure = false;
            var token = _manualCts?.Token ?? CancellationToken.None;
            _passedLow = await MeasureQuantityPointAsync(LowPoint, (sdi, text) =>
            {
                if (sdi == 2)
                    PointLowSys1Text = text;
                else if (sdi == 3)
                    PointLowSys2Text = text;
            }, token).ConfigureAwait(false);
            CanMeasure = IsManualTestRunning;
            if (!IsManualTestRunning || _manualAborted) return;
            _measuredLow = true;
            RefreshMeasureCommands();
            await TryFinalizeAsync().ConfigureAwait(false);
        }

        private async Task OnMeasureMidPointAsync()
        {
            PointMidSys1Text = "--";
            PointMidSys2Text = "--";
            CanMeasure = false;
            var token = _manualCts?.Token ?? CancellationToken.None;
            _passedMid = await MeasureQuantityPointAsync(MidPoint, (sdi, text) =>
            {
                if (sdi == 2)
                    PointMidSys1Text = text;
                else if (sdi == 3)
                    PointMidSys2Text = text;
            }, token).ConfigureAwait(false);
            CanMeasure = IsManualTestRunning;
            if (!IsManualTestRunning || _manualAborted) return;
            _measuredMid = true;
            RefreshMeasureCommands();
            await TryFinalizeAsync().ConfigureAwait(false);
        }

        private async Task OnMeasureHighPointAsync()
        {
            PointHighSys1Text = "--";
            PointHighSys2Text = "--";
            CanMeasure = false;
            var token = _manualCts?.Token ?? CancellationToken.None;
            _passedHigh = await MeasureQuantityPointAsync(HighPoint, (sdi, text) =>
            {
                if (sdi == 2)
                    PointHighSys1Text = text;
                else if (sdi == 3)
                    PointHighSys2Text = text;
            }, token).ConfigureAwait(false);
            CanMeasure = IsManualTestRunning;
            if (!IsManualTestRunning || _manualAborted) return;
            _measuredHigh = true;
            RefreshMeasureCommands();
            await TryFinalizeAsync().ConfigureAwait(false);
        }

        private async Task<bool> MeasureExcitationAsync(string title, int channel, Action<string, string> setTexts, CancellationToken cancellationToken)
        {
            if (!IsAutoTestRunning && !IsManualTestRunning)
            {
                Log($"{title}: 当前未处于测试状态");
                return false;
            }

            await _measureLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                connectCts.CancelAfter(6000);
                await ConnectDmmAsync(DmmIpAddress, connectCts.Token).ConfigureAwait(false);

                if (_lvdt != null)
                {
                    try { await _lvdt.StopAsync(LvdtSys1Channel, cancellationToken).ConfigureAwait(false); } catch { }
                    try { await _lvdt.StopAsync(LvdtSys2Channel, cancellationToken).ConfigureAwait(false); } catch { }
                    try { await _lvdt.ResetAsync(cancellationToken).ConfigureAwait(false); } catch { }
                }

                await ApplyExcitationMeasurementRouteAsync(channel, cancellationToken).ConfigureAwait(false);
                await Task.Delay(ExcitationReadSettleMs, cancellationToken).ConfigureAwait(false);



                await Task.Delay(FreqModeSettleMs, cancellationToken).ConfigureAwait(false);

                // ③ 再读电压
                using var acvCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                acvCts.CancelAfter(8000);
                var voltageReading = await DmmReadOnceAsync(
                    DmmMeasureMode.ACV,
                    new DmmReadOptions { TimeoutMilliseconds = 8000 },
                    acvCts.Token).ConfigureAwait(false);

                // ① 先读频率
                //var frequencyReading = 12;
                await Task.Delay(FreqModeSettleMs, cancellationToken).ConfigureAwait(false);
                using var freqCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                freqCts.CancelAfter(10000);
                var frequencyReading = await DmmReadOnceAsync(
                    DmmMeasureMode.FREQ,
                    new DmmReadOptions
                    {
                        TimeoutMilliseconds = 10000,
                        FrequencyRangeIndex = 10,          // ← 改为10(V)，不是档位索引
                        FrequencyApertureSeconds = 0.1
                    },
                    freqCts.Token).ConfigureAwait(false);

                // ② FREQ读完后等一下，再切ACV档
                await Task.Delay(FreqModeSettleMs, cancellationToken).ConfigureAwait(false);

                //// ③ 再读电压
                //var voltageReading = await _dmmSocket.ReadOnceAsync(
                //    DmmMeasureMode.ACV,
                //    new DmmReadOptions { TimeoutMilliseconds = 8000 },
                //    cancellationToken).ConfigureAwait(false);



                var voltage = voltageReading?.Value;
                var frequency = frequencyReading?.Value;
                var freqText = frequency.HasValue ? $"{frequency.Value:0.0} Hz" : "--";
                var voltText = voltage.HasValue ? $"{voltage.Value:0.00} Vrms" : "--";
                setTexts(freqText, voltText);

                var pass = frequency.HasValue && voltage.HasValue
                    && frequency.Value >= ExcitationFreqMinHz && frequency.Value <= ExcitationFreqMaxHz
                    && voltage.Value >= ExcitationVoltMinVrms && voltage.Value <= ExcitationVoltMaxVrms;

                Log($"{title}: 频率={(frequency.HasValue ? frequency.Value.ToString("0.0") : "--")}Hz, 电压={(voltage.HasValue ? voltage.Value.ToString("0.00") : "--")}Vrms, 结果={(pass ? "PASS" : "FAIL")}");
                return pass;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception ex)
            {
                setTexts("--", "--");
                if (IsManualTestRunning)
                {
                    await AbortManualTestAsync($"{title}: 激励测量异常，手动测试中止: {ex.Message}").ConfigureAwait(false);
                }
                else if (IsAutoTestRunning)
                {
                    await AbortAutoTestAsync($"{title}: 激励测量异常，自动测试中止: {ex.Message}").ConfigureAwait(false);
                    throw;
                }

                return false;
            }
            finally
            {
                try
                {
                    await ClearExcitationMeasurementRouteAsync(channel).ConfigureAwait(false);
                }
                catch { }

                if (_lvdt != null)
                {
                    try { await RestoreLvdtChannelsAsync().ConfigureAwait(false); } catch { }
                }

                _measureLock.Release();
            }
        }

        private async Task<bool> MeasureQuantityPointAsync(QuantityPoint point, Action<byte, string> setText, CancellationToken cancellationToken)
        {
            if (!IsAutoTestRunning && !IsManualTestRunning)
            {
                Log($"{point.Name}: 当前未处于测试状态");
                return false;
            }

            await _measureLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await ApplyQuantityOutputsAsync(point.Target, cancellationToken).ConfigureAwait(false);
                await DrainArincBufferAsync(cancellationToken).ConfigureAwait(false);
                await Task.Delay(PostSwitchRxFlushMs, cancellationToken).ConfigureAwait(false);

                var samples = new Dictionary<byte, List<double>>
                {
                    [2] = new List<double>(SamplesPerMeasure),
                    [3] = new List<double>(SamplesPerMeasure)
                };


                var deadline = DateTime.UtcNow.AddMilliseconds(SampleTimeoutMs);
                var assignedText = new HashSet<byte>();
                while (!cancellationToken.IsCancellationRequested && DateTime.UtcNow <= deadline)
                {
                    var words = await _arinc.ReadRxWordsAsync(RxChannelIndex, maxCount: 512, enableTimeTag: false, enableRateAdaption: false, cancellationToken: cancellationToken)
                        .ConfigureAwait(false);

                    if (words != null && words.Count > 0)

                    foreach (var w in words)
                    {
                        _arinc.ParseRawWord(w.Data429, out var label, out var sdi, out var data19, out var ssm);

                        if (!IsExpectedLabel(label))
                        {
                            continue;
                        }

                        if (ssm != SsmNormal)
                        {
                            continue;
                        }

                        if (sdi != 2 && sdi != 3)
                        {
                            continue;
                        }

                        var value = DecodeQuantity(data19);
                        if (!value.HasValue)
                        {
                            continue;
                        }


                        var list = samples[sdi];
                        if (list.Count >= SamplesPerMeasure)
                        {
                            continue;
                        }

                        list.Add(value.Value);


                        if (list.Count >= SamplesPerMeasure && !assignedText.Contains(sdi))
                        {
                            var avg = list.Average();
                            setText(sdi, $"{avg:0} %");
                            assignedText.Add(sdi);
                            Log($"{point.Name}: {(sdi == 2 ? 1 : 2)}号系统油量，已收到 {avg:0}%");
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
                Log($"{point.Name}: 2号系统油量={avg1:0}%, 3号系统油量={avg2:0}%, 判定范围=[{point.Min:0.###},{point.Max:0.###}]%, 结果={(pass ? "PASS" : "FAIL")}");
                return pass;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception ex)
            {
                setText(2, "--");
                setText(3, "--");
                if (IsManualTestRunning)
                {
                    await AbortManualTestAsync($"{point.Name}: 油量测量异常，手动测试中止: {ex.Message}").ConfigureAwait(false);
                }
                else if (IsAutoTestRunning)
                {
                    await AbortAutoTestAsync($"{point.Name}: 油量测量异常，自动测试中止: {ex.Message}").ConfigureAwait(false);
                    throw;
                }

                return false;
            }
            finally
            {
                _measureLock.Release();
            }
        }

        private static bool IsQuantityInRange(double value, QuantityPoint point)
        {
            return value >= point.Min && value <= point.Max;
        }

        private string NormalizeIntegerInput(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var chars = text.Where(char.IsDigit).ToArray();
            return new string(chars);
        }

        private bool TryCreateCustomRangePoint(out QuantityPoint point)
        {
            point = null;

            var lowText = NormalizeIntegerInput(ManualRangeLowInput);
            var highText = NormalizeIntegerInput(ManualRangeHighInput);
            if (string.IsNullOrWhiteSpace(lowText) || string.IsNullOrWhiteSpace(highText))
                return false;

            if (!int.TryParse(lowText, NumberStyles.None, CultureInfo.InvariantCulture, out var low))
                return false;

            if (!int.TryParse(highText, NumberStyles.None, CultureInfo.InvariantCulture, out var high))
                return false;

            if (low < 0 || high > 100 || low >= high)
                return false;

            var target = Math.Round((low + high) / 2.0, 1, MidpointRounding.AwayFromZero);
            point = new QuantityPoint($"({low},{high})%", target, low, high);
            return true;
        }

        private static bool TryParsePercent(string text, out double value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(text) || string.Equals(text.Trim(), "--", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var normalized = text.Trim().Replace("%", string.Empty);
            return double.TryParse(normalized, out value);
        }

        private static bool TryParseMeasurementValue(string text, string unit, out double value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(text) || string.Equals(text.Trim(), "--", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var normalized = text.Trim();
            if (!string.IsNullOrWhiteSpace(unit))
            {
                var unitIndex = normalized.IndexOf(unit, StringComparison.OrdinalIgnoreCase);
                if (unitIndex >= 0)
                {
                    normalized = normalized.Remove(unitIndex, unit.Length);
                }
            }

            normalized = normalized.Trim();
            return double.TryParse(normalized, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out value)
                || double.TryParse(normalized, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out value);
        }

        private async Task ApplyQuantityOutputsAsync(double quantityPercent, CancellationToken cancellationToken)
        {
            _currentQuantityPercent = quantityPercent;
            var (s1, s2) = CalculateSecondaryVoltages(quantityPercent);
            await _lvdt.SetVaVbAsync(LvdtSys1Channel, s1, s2, cancellationToken).ConfigureAwait(false);
            await _lvdt.SetVaVbAsync(LvdtSys2Channel, s1, s2, cancellationToken).ConfigureAwait(false);
            await _lvdt.StartAsync(LvdtSys1Channel, cancellationToken).ConfigureAwait(false);
            await _lvdt.StartAsync(LvdtSys2Channel, cancellationToken).ConfigureAwait(false);
            Log($"LVDT输出: 目标油量={quantityPercent:0.###}%, S1(Va)={s1:0.00}Vrms, S2(Vb)={s2:0.00}Vrms, Sum={SimulationSumVrms:0.00}Vrms");
            await Task.Delay(2000, cancellationToken).ConfigureAwait(false);
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

        private async Task ConfigureDmmAsync(CancellationToken cancellationToken)
        {
            if (_dmmSocket == null)
                return;

            await _dmmSocket.SendAsync("*CLS", cancellationToken).ConfigureAwait(false);
            await _dmmSocket.SendAsync(DmmTriggerDelayCommand, cancellationToken).ConfigureAwait(false);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private async Task ConnectDmmAsync(string ipAddress, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(ipAddress))
                throw new InvalidOperationException("万用表IP地址为空");

            _dmmSocket ??= new DmmSocketApi();

            try
            {
                if (!_dmmSocket.IsConnected)
                    await _dmmSocket.ConnectAsync(ipAddress, token).ConfigureAwait(false);

                await ConfigureDmmAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                await CleanupDmmSocketAsync().ConfigureAwait(false);
                throw;
            }
            catch (Exception ex)
            {
                Log($"万用表首次连接失败，准备重建连接: {ex.Message}");
                await CleanupDmmSocketAsync().ConfigureAwait(false);

                _dmmSocket = new DmmSocketApi();
                await _dmmSocket.ConnectAsync(ipAddress, token).ConfigureAwait(false);
                await ConfigureDmmAsync(token).ConfigureAwait(false);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private async Task<DmmReading> DmmReadOnceAsync(DmmMeasureMode mode, DmmReadOptions options, CancellationToken token)
        {
            await ConnectDmmAsync(DmmIpAddress, token).ConfigureAwait(false);

            return await _dmmSocket.ReadOnceAsync(mode, options, token).ConfigureAwait(false);
        }

        private async Task TryFinalizeAsync()
        {
            if (!IsAutoTestRunning && !IsManualTestRunning)
                return;

            if (!(_measuredExc1 && _measuredExc2 && _measuredLow && _measuredMid && _measuredHigh))
                return;

            var resultText = (_passedExc1 && _passedExc2 && _passedLow && _passedMid && _passedHigh) ? "PASS" : "FAIL";
            var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            CurrentTestResult = resultText;
            PreviousTestTime = now;
            PreviousTestResult = resultText;
            LastTestTime = now;
            LastTestResult = resultText;
            SaveTestResultToProject();
            Log($"测试结果: {resultText}");
        }

        private async Task AbortManualTestAsync(string reason)
        {
            _manualAborted = true;
            if (!string.IsNullOrWhiteSpace(reason))
                Log(reason);

            await StopManualTestAsync().ConfigureAwait(false);
        }

        private async Task AbortAutoTestAsync(string reason)
        {
            if (!string.IsNullOrWhiteSpace(reason))
                Log(reason);

            await StopAutoTestAsync().ConfigureAwait(false);
        }

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

            Log($"手动测试停止/结束，正在断开设备...");
            try
            {
                await RunCleanupExclusiveAsync(async () =>
                {
                    await CleanupDmmSocketAsync().ConfigureAwait(false);
                    await DisconnectAllExcitationMatrixRoutesAsync().ConfigureAwait(false);
                    await CleanupPowerAsync().ConfigureAwait(false);
                    await CleanupLvdtAsync().ConfigureAwait(false);
                    await CleanupArincAsync().ConfigureAwait(false);
                    await EnsureGroundDoAsync(false, CancellationToken.None).ConfigureAwait(false);
                    await EnsureRelay485Async(false, CancellationToken.None).ConfigureAwait(false);
                    await CleanupJy7131Async().ConfigureAwait(false);
                }).ConfigureAwait(false);
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

            Log($"自动测试停止/结束，正在断开设备...");
            try
            {
                await RunCleanupExclusiveAsync(async () =>
                {
                    await CleanupDmmSocketAsync().ConfigureAwait(false);
                    await DisconnectAllExcitationMatrixRoutesAsync().ConfigureAwait(false);
                    await CleanupPowerAsync().ConfigureAwait(false);
                    await CleanupLvdtAsync().ConfigureAwait(false);
                    await CleanupArincAsync().ConfigureAwait(false);
                    await EnsureGroundDoAsync(false, CancellationToken.None).ConfigureAwait(false);
                    await EnsureRelay485Async(false, CancellationToken.None).ConfigureAwait(false);
                    await CleanupJy7131Async().ConfigureAwait(false);
                }).ConfigureAwait(false);
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

        private async Task EnsurePowerAsync(CancellationToken cancellationToken)
        {
            if (!_boardPowerService.IsPowered)
            {
                var (confirmed, _) = PowerOnPromptDialog.ShowPrompt("液压单板", showVoltage: false);
                if (!confirmed) throw new OperationCanceledException("用户取消上电");
                await _boardPowerService.PowerOnAsync("液压单板", cancellationToken: cancellationToken).ConfigureAwait(false);
            }
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

            await ConfigureLvdtOutputCalibrationAsync(LvdtSys1Channel, cancellationToken).ConfigureAwait(false);
            await ConfigureLvdtOutputCalibrationAsync(LvdtSys2Channel, cancellationToken).ConfigureAwait(false);

            var config = CreateSimulationConfig();

            await _lvdt.ConfigureSimulationChannelAsync(LvdtSys1Channel, config, cancellationToken).ConfigureAwait(false);
            await _lvdt.ConfigureSimulationChannelAsync(LvdtSys2Channel, config, cancellationToken).ConfigureAwait(false);
            await _lvdt.StartAsync(LvdtSys1Channel, cancellationToken).ConfigureAwait(false);
            await _lvdt.StartAsync(LvdtSys2Channel, cancellationToken).ConfigureAwait(false);
            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
        }

        private async Task RestoreLvdtChannelsAsync()
        {
            if (_lvdt == null)
                return;

            await ConfigureLvdtOutputCalibrationAsync(LvdtSys1Channel, CancellationToken.None).ConfigureAwait(false);
            await ConfigureLvdtOutputCalibrationAsync(LvdtSys2Channel, CancellationToken.None).ConfigureAwait(false);

            var config = CreateSimulationConfig();
            await _lvdt.ConfigureSimulationChannelAsync(LvdtSys1Channel, config, CancellationToken.None).ConfigureAwait(false);
            await _lvdt.ConfigureSimulationChannelAsync(LvdtSys2Channel, config, CancellationToken.None).ConfigureAwait(false);

            await ApplyQuantityOutputsAsync(_currentQuantityPercent, CancellationToken.None).ConfigureAwait(false);
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

        private async Task DrainArincBufferAsync(CancellationToken cancellationToken)
        {
            for (int i = 0; i < 100; i++)
            {
                var batch = await _arinc.ReadRxWordsAsync(
                    RxChannelIndex, maxCount: 4096,
                    enableTimeTag: false, enableRateAdaption: false,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                if (batch == null || batch.Count == 0)
                    break;
            }
        }

        private bool IsExpectedLabel(byte label)
        {
            return _arinc.ReverseLabel(label) == QtyLabelDec;
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

            var vaRecord = TryGetCalibrationRecord(records, device.Id, $"CH{channel - 1}{LvdtVaSuffix}");
            var vbRecord = TryGetCalibrationRecord(records, device.Id, $"CH{channel - 1}{LvdtVbSuffix}");
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
                AdcRangeIndex = 3
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
                    _isRelay485On = true;
                }
                else
                {
                    if (!_isRelay485On || _jy7131 == null)
                        return;

                    try
                    {
                        await _jy7131.SetRelayAsync(Relay485ChannelIndex, false, cancellationToken).ConfigureAwait(false);
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

        private async Task CleanupDmmSocketAsync()
        {
            var socket = _dmmSocket;
            _dmmSocket = null;

            if (socket == null) return;

            try
            {
                if (socket.IsConnected)
                    await Task.WhenAny(socket.DisconnectAsync(CancellationToken.None), Task.Delay(3000)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log($"万用表断开异常: {ex.Message}");
            }

            try
            {
                await Task.WhenAny(socket.DisposeAsync().AsTask(), Task.Delay(3000)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log($"万用表释放异常: {ex.Message}");
            }
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
            _currentQuantityPercent = 0.0;
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
            CustomRangeSys1Text = "--";
            CustomRangeSys2Text = "--";
            RefreshMeasureCommands();
        }

        private void RefreshMeasureCommands()
        {
            RaisePropertyChanged(nameof(CanMeasureExcitation1));
            RaisePropertyChanged(nameof(CanMeasureExcitation2));
            RaisePropertyChanged(nameof(CanMeasureLowPoint));
            RaisePropertyChanged(nameof(CanMeasureMidPoint));
            RaisePropertyChanged(nameof(CanMeasureHighPoint));
            RaisePropertyChanged(nameof(CanMeasureCustomRange));
            MeasureExcitation1Command?.RaiseCanExecuteChanged();
            MeasureExcitation2Command?.RaiseCanExecuteChanged();
            MeasureLowPointCommand?.RaiseCanExecuteChanged();
            MeasureMidPointCommand?.RaiseCanExecuteChanged();
            MeasureHighPointCommand?.RaiseCanExecuteChanged();
            MeasureCustomRangeCommand?.RaiseCanExecuteChanged();
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
