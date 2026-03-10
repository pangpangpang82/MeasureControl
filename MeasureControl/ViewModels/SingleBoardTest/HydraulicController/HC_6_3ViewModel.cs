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
using MeasureControl.Models.Devices;
using MeasureControl.Models.Devices.DeviceCategories;
using MeasureControl.Events;
using MeasureControl.Services;
using MeasureControl.Services.HardwareApis;
using Prism.Events;
using Prism.Ioc;
using MeasureControl.Drivers;

namespace MeasureControl.ViewModels.SingleBoardTest.HydraulicController
{
    /// <summary>
    /// HC_6_3 测试项：温度信号测试（PT500/温度通道校验）
    /// 测试目的：通过“程控电阻箱”模拟 PT500 传感器的电阻值，
    ///          再从 ARINC429 总线接收温度数据，验证两路温度（SDI0/SDI1）是否在允许范围内。
    /// 测试方法：
    /// 1) 给被测板供电 28V。
    /// 2) 将程控电阻 RO0/RO1 同时设置为指定电阻（点1/点2/点3）。
    /// 3) 从 ARINC429 接收温度 Label=175(oct)（十进制 125）数据，分别统计 SDI0 与 SDI1。
    /// 4) 每路采集 5 帧有效数据取平均值，并与阈值范围比对，给出“合格/不合格”。
    /// </summary>
    public class HC_6_3ViewModel : BindableBase
    {
        // 电源配置（给被测板供电）
        private const string PowerSupplyIpAddress = "192.168.1.15";
        private const double InputVoltageV = 28.0;
        private const double InputCurrentA = 1;

        // ARINC429 接收配置
        private const int RxChannelIndex = 2;
        private const double ArincRate = 100000.0;

        // 温度数据定义与采样参数
        private const byte TempLabelDec = 125; // 175(oct)
        private const int SamplesPerMeasure = 1;
        private const int SampleTimeoutMs = 3000;
        private const int ResistanceSettleMs = 400;

        // 三个测试点对应的“模拟电阻值”（由程控电阻箱输出到 PT500 模拟通道）
        private const double R1_Ohm = 763.3;
        private const double R2_Ohm = 1758.6;
        private const double R3_Ohm = 1155.4;

        // 三个测试点的温度判据范围（单位：℃）
        private const double T1_Min = -66.60;
        private const double T1_Max = -53.40;
        private const double T2_Min = 193.40;
        private const double T2_Max = 206.60;
        private const double T3_Min = 32.40;
        private const double T3_Max = 46.60;

        // 温度的 ARINC429 编码参数（BNR：有符号二进制数）
        private const int TempBnrBitLength = 9;
        private const double TempResolution = 1.0;
        private const int TempMsbPosition = 28;

        // 同一温度 Label 下，用 SDI 区分两路温度系统
        private const int RelayAuxDoIndex = 25;
        private const int RelayGroundDoIndex = 26;
        private const int Relay485ChannelIndex = 6;
        private const byte TempChannel112To114Sdi = 2;
        private const byte TempChannel116To118Sdi = 3;

        private readonly SemaphoreSlim _measureLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _relayLock = new SemaphoreSlim(1, 1);
        private CancellationTokenSource _manualCts;
        private CancellationTokenSource _autoCts;

        private readonly IPxiChassisService _pxiChassisService;
        private readonly ISingleBoardTestContextService _singleBoardTestContext;
        private IPowerSupplyApi _power;
        private IArt4229Api _arinc;
        private IJy7131Api _jy7131;

        private const string TestItemName = "温度采集测试";
        private ACTS6010Driver _res;
        private bool _isRelay485On;
        private bool _canMeasure;
        private bool _measured1;
        private bool _measured2;
        private bool _measured3;
        private bool _manualAborted;
        private bool _historyLoaded;

        private bool _isManualTestRunning;
        private bool _isAutoTestRunning;

        private string _lastTestTime = "--";
        private string _lastTestResult = "--";
        private string _previousTestTime = "--";
        private string _previousTestResult = "--";
        private string _currentTestResult = "--";

        private string _temp1Text = "--";
        private string _temp2Text = "--";
        private string _temp3Text = "--";

        private string _temp1BText = "--";
        private string _temp2BText = "--";
        private string _temp3BText = "--";
        private double? _temp1;
        private double? _temp2;
        private double? _temp3;
        private double? _temp1B;
        private double? _temp2B;
        private double? _temp3B;

        public HC_6_3ViewModel(IPxiChassisService pxiChassisService, ISingleBoardTestContextService singleBoardTestContext)
        {
            _pxiChassisService = pxiChassisService;
            _singleBoardTestContext = singleBoardTestContext;

            ManualTestCommand = new DelegateCommand(async () => await OnManualTestAsync());
            AutoTestCommand = new DelegateCommand(async () => await OnAutoTestAsync());

            MeasurePoint1Command = new DelegateCommand(async () => await OnMeasurePoint1Async(), () => CanMeasurePoint1);
            MeasurePoint2Command = new DelegateCommand(async () => await OnMeasurePoint2Async(), () => CanMeasurePoint2);
            MeasurePoint3Command = new DelegateCommand(async () => await OnMeasurePoint3Async(), () => CanMeasurePoint3);

            ClearLogCommand = new DelegateCommand(() => Logs.Clear());

            LoadLastTestResultFromProject();
        }

        private void LoadLastTestResultFromProject()
        {
            if (_historyLoaded)
                return;

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

                _historyLoaded = true;
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

        public string CurrentTestResult
        {
            get => _currentTestResult;
            private set => SetProperty(ref _currentTestResult, value);
        }

        public DelegateCommand ManualTestCommand { get; }
        public DelegateCommand AutoTestCommand { get; }

        public DelegateCommand MeasurePoint1Command { get; }
        public DelegateCommand MeasurePoint2Command { get; }
        public DelegateCommand MeasurePoint3Command { get; }

        public DelegateCommand ClearLogCommand { get; }

        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();

        public bool IsManualTestRunning
        {
            get => _isManualTestRunning;
            set
            {
                if (SetProperty(ref _isManualTestRunning, value))
                {
                    RaisePropertyChanged(nameof(CanMeasurePoint1));
                    RaisePropertyChanged(nameof(CanMeasurePoint2));
                    RaisePropertyChanged(nameof(CanMeasurePoint3));
                    MeasurePoint1Command?.RaiseCanExecuteChanged();
                    MeasurePoint2Command?.RaiseCanExecuteChanged();
                    MeasurePoint3Command?.RaiseCanExecuteChanged();
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
                    RaisePropertyChanged(nameof(CanMeasurePoint1));
                    RaisePropertyChanged(nameof(CanMeasurePoint2));
                    RaisePropertyChanged(nameof(CanMeasurePoint3));
                    MeasurePoint1Command?.RaiseCanExecuteChanged();
                    MeasurePoint2Command?.RaiseCanExecuteChanged();
                    MeasurePoint3Command?.RaiseCanExecuteChanged();
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
                    RaisePropertyChanged(nameof(CanMeasurePoint1));
                    RaisePropertyChanged(nameof(CanMeasurePoint2));
                    RaisePropertyChanged(nameof(CanMeasurePoint3));
                    MeasurePoint1Command?.RaiseCanExecuteChanged();
                    MeasurePoint2Command?.RaiseCanExecuteChanged();
                    MeasurePoint3Command?.RaiseCanExecuteChanged();
                }
            }
        }

        public bool CanMeasurePoint1 => IsManualTestRunning && CanMeasure && !_measured1;
        public bool CanMeasurePoint2 => IsManualTestRunning && CanMeasure && !_measured2;
        public bool CanMeasurePoint3 => IsManualTestRunning && CanMeasure && !_measured3;

        private void RefreshMeasureCommands()
        {
            RaisePropertyChanged(nameof(CanMeasurePoint1));
            RaisePropertyChanged(nameof(CanMeasurePoint2));
            RaisePropertyChanged(nameof(CanMeasurePoint3));
            MeasurePoint1Command?.RaiseCanExecuteChanged();
            MeasurePoint2Command?.RaiseCanExecuteChanged();
            MeasurePoint3Command?.RaiseCanExecuteChanged();
        }

        /// <summary>
        /// 整板串行自动测试入口。
        /// 由外部(整板自动测试)调用，支持 await 等待完成，并通过 CancellationToken 实现"立即停止当前测量"。
        /// 返回值仅为"合格/不合格"。
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

        public string Temp1Text
        {
            get => _temp1Text;
            private set => SetProperty(ref _temp1Text, value);
        }

        public string Temp2Text
        {
            get => _temp2Text;
            private set => SetProperty(ref _temp2Text, value);
        }

        public string Temp3Text
        {
            get => _temp3Text;
            private set => SetProperty(ref _temp3Text, value);
        }

        public string Temp1BText
        {
            get => _temp1BText;
            private set => SetProperty(ref _temp1BText, value);
        }

        public string Temp2BText
        {
            get => _temp2BText;
            private set => SetProperty(ref _temp2BText, value);
        }

        public string Temp3BText
        {
            get => _temp3BText;
            private set => SetProperty(ref _temp3BText, value);
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

        /// <summary>
        /// 手动测试流程
        /// 进入手动模式后，先初始化电源/ARINC429/程控电阻箱，
        /// 然后由用户分别点击“点1/点2/点3”执行测量。
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

            IsManualTestRunning = true;
            CurrentTestResult = "--";
            CanMeasure = false;
            _manualAborted = false;

            _measured1 = false;
            _measured2 = false;
            _measured3 = false;

            _temp1 = null;
            _temp2 = null;
            _temp3 = null;
            _temp1B = null;
            _temp2B = null;
            _temp3B = null;
            Temp1Text = "--";
            Temp2Text = "--";
            Temp3Text = "--";
            Temp1BText = "--";
            Temp2BText = "--";
            Temp3BText = "--";

            _manualCts?.Cancel();
            _manualCts?.Dispose();
            _manualCts = new CancellationTokenSource();

            Log("开始手动测试");
            Log($"电源: CH1 {InputVoltageV:0.###}V {InputCurrentA:0.###}A, IP={PowerSupplyIpAddress}");
            Log($"ARINC429: RX通道{RxChannelIndex + 1}, 码率 {ArincRate:0}bps, 温度Label=175(oct), SDI=2对应112~114, SDI=3对应116~118");

            try
            {
                await EnsureRelay485Async(on: true, cancellationToken: _manualCts.Token).ConfigureAwait(false);
                await EnsureGroundDoAsync(on: true, cancellationToken: _manualCts.Token).ConfigureAwait(false);
                await EnsureArincRxAsync(_manualCts.Token).ConfigureAwait(false);
                await EnsurePowerAsync(_manualCts.Token).ConfigureAwait(false);
                await EnsureResistanceAsync(_manualCts.Token).ConfigureAwait(false);
                CanMeasure = true;
                Log("手动测试初始化完成，可分别点击三档电阻测量温度");
            }
            catch (Exception ex)
            {
                await AbortManualTestAsync($"手动测试初始化失败，中止: {ex.Message}").ConfigureAwait(false);
            }
        }

        private async Task<string> ExecuteAutoTestAsync(CancellationToken cancellationToken)
        {
            IsAutoTestRunning = true;
            CanMeasure = false;
            _manualAborted = false;

            _measured1 = false;
            _measured2 = false;
            _measured3 = false;

            _temp1 = null;
            _temp2 = null;
            _temp3 = null;
            _temp1B = null;
            _temp2B = null;
            _temp3B = null;
            Temp1Text = "--";
            Temp2Text = "--";
            Temp3Text = "--";
            Temp1BText = "--";
            Temp2BText = "--";
            Temp3BText = "--";

            Log("开始自动测试");
            Log($"点1: R={R1_Ohm:0.###}Ω SDI=2对应112~114, SDI=3对应116~118 温度[{T1_Min:0.###},{T1_Max:0.###}]℃");
            Log($"点2: R={R2_Ohm:0.###}Ω SDI=2对应112~114, SDI=3对应116~118 温度[{T2_Min:0.###},{T2_Max:0.###}]℃");
            Log($"点3: R={R3_Ohm:0.###}Ω SDI=2对应112~114, SDI=3对应116~118 温度[{T3_Min:0.###},{T3_Max:0.###}]℃");

            try
            {
                await EnsureRelay485Async(on: true, cancellationToken: cancellationToken).ConfigureAwait(false);
                await EnsureGroundDoAsync(on: true, cancellationToken: cancellationToken).ConfigureAwait(false);
                await EnsureArincRxAsync(cancellationToken).ConfigureAwait(false);
                await EnsurePowerAsync(cancellationToken).ConfigureAwait(false);
                await EnsureResistanceAsync(cancellationToken).ConfigureAwait(false);

                await MeasurePointAsync(
                        "点1",
                        R1_Ohm,
                        setTextA: t => Temp1Text = t,
                        setValueA: v => _temp1 = v,
                        setTextB: t => Temp1BText = t,
                        setValueB: v => _temp1B = v,
                        cancellationToken)
                    .ConfigureAwait(false);
                await Task.Delay(80, cancellationToken).ConfigureAwait(false);

                await MeasurePointAsync(
                        "点2",
                        R2_Ohm,
                        setTextA: t => Temp2Text = t,
                        setValueA: v => _temp2 = v,
                        setTextB: t => Temp2BText = t,
                        setValueB: v => _temp2B = v,
                        cancellationToken)
                    .ConfigureAwait(false);
                await Task.Delay(80, cancellationToken).ConfigureAwait(false);

                await MeasurePointAsync(
                        "点3",
                        R3_Ohm,
                        setTextA: t => Temp3Text = t,
                        setValueA: v => _temp3 = v,
                        setTextB: t => Temp3BText = t,
                        setValueB: v => _temp3B = v,
                        cancellationToken)
                    .ConfigureAwait(false);

                _measured1 = true;
                _measured2 = true;
                _measured3 = true;

                await TryFinalizeIfAllMeasuredAsync().ConfigureAwait(false);
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
        /// 自动测试流程
        /// 自动按点1->点2->点3顺序设置电阻并接收温度，三点都满足判据则“合格”。
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

            IsAutoTestRunning = true;
            CurrentTestResult = "--";
            CanMeasure = false;
            _manualAborted = false;

            _measured1 = false;
            _measured2 = false;
            _measured3 = false;

            _temp1 = null;
            _temp2 = null;
            _temp3 = null;
            _temp1B = null;
            _temp2B = null;
            _temp3B = null;
            Temp1Text = "--";
            Temp2Text = "--";
            Temp3Text = "--";
            Temp1BText = "--";
            Temp2BText = "--";
            Temp3BText = "--";

            _autoCts?.Cancel();
            _autoCts?.Dispose();
            _autoCts = new CancellationTokenSource();

            Log("开始自动测试");
            Log($"点1: R={R1_Ohm:0.###}Ω SDI=2对应112~114, SDI=3对应116~118 温度[{T1_Min:0.###},{T1_Max:0.###}]℃");
            Log($"点2: R={R2_Ohm:0.###}Ω SDI=2对应112~114, SDI=3对应116~118 温度[{T2_Min:0.###},{T2_Max:0.###}]℃");
            Log($"点3: R={R3_Ohm:0.###}Ω SDI=2对应112~114, SDI=3对应116~118 温度[{T3_Min:0.###},{T3_Max:0.###}]℃");

            try
            {
                await EnsureRelay485Async(on: true, cancellationToken: _autoCts.Token).ConfigureAwait(false);
                await EnsureGroundDoAsync(on: true, cancellationToken: _autoCts.Token).ConfigureAwait(false);
                await EnsureArincRxAsync(_autoCts.Token).ConfigureAwait(false);
                await EnsurePowerAsync(_autoCts.Token).ConfigureAwait(false);
                await EnsureResistanceAsync(_autoCts.Token).ConfigureAwait(false);

                await MeasurePointAsync(
                        "点1",
                        R1_Ohm,
                        setTextA: t => Temp1Text = t,
                        setValueA: v => _temp1 = v,
                        setTextB: t => Temp1BText = t,
                        setValueB: v => _temp1B = v,
                        cancellationToken: _autoCts.Token)
                    .ConfigureAwait(false);
                await Task.Delay(80, _autoCts.Token).ConfigureAwait(false);

                await MeasurePointAsync(
                        "点2",
                        R2_Ohm,
                        setTextA: t => Temp2Text = t,
                        setValueA: v => _temp2 = v,
                        setTextB: t => Temp2BText = t,
                        setValueB: v => _temp2B = v,
                        cancellationToken: _autoCts.Token)
                    .ConfigureAwait(false);
                await Task.Delay(80, _autoCts.Token).ConfigureAwait(false);

                await MeasurePointAsync(
                        "点3",
                        R3_Ohm,
                        setTextA: t => Temp3Text = t,
                        setValueA: v => _temp3 = v,
                        setTextB: t => Temp3BText = t,
                        setValueB: v => _temp3B = v,
                        cancellationToken: _autoCts.Token)
                    .ConfigureAwait(false);

                _measured1 = true;
                _measured2 = true;
                _measured3 = true;

                await TryFinalizeIfAllMeasuredAsync().ConfigureAwait(false);
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

        /// <summary>
        /// 测量点1（手动模式）
        /// </summary>
        private async Task OnMeasurePoint1Async()
        {
            var ok = await MeasurePointAsync(
                    "点1",
                    R1_Ohm,
                    setTextA: t => Temp1Text = t,
                    setValueA: v => _temp1 = v,
                    setTextB: t => Temp1BText = t,
                    setValueB: v => _temp1B = v,
                    _manualCts?.Token ?? CancellationToken.None)
                .ConfigureAwait(false);
            if (!IsManualTestRunning || _manualAborted) return;
            _measured1 = true;
            RefreshMeasureCommands();
            await TryFinalizeIfAllMeasuredAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 测量点2（手动模式）
        /// </summary>
        private async Task OnMeasurePoint2Async()
        {
            var ok = await MeasurePointAsync(
                    "点2",
                    R2_Ohm,
                    setTextA: t => Temp2Text = t,
                    setValueA: v => _temp2 = v,
                    setTextB: t => Temp2BText = t,
                    setValueB: v => _temp2B = v,
                    _manualCts?.Token ?? CancellationToken.None)
                .ConfigureAwait(false);
            if (!IsManualTestRunning || _manualAborted) return;
            _measured2 = true;
            RefreshMeasureCommands();
            await TryFinalizeIfAllMeasuredAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 测量点3（手动模式）
        /// </summary>
        private async Task OnMeasurePoint3Async()
        {
            var ok = await MeasurePointAsync(
                    "点3",
                    R3_Ohm,
                    setTextA: t => Temp3Text = t,
                    setValueA: v => _temp3 = v,
                    setTextB: t => Temp3BText = t,
                    setValueB: v => _temp3B = v,
                    _manualCts?.Token ?? CancellationToken.None)
                .ConfigureAwait(false);
            if (!IsManualTestRunning || _manualAborted) return;
            _measured3 = true;
            RefreshMeasureCommands();
            await TryFinalizeIfAllMeasuredAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 测量一个测试点（核心测量方法）
        /// 流程：
        /// 1) 设置程控电阻 RO0/RO1 为指定电阻，并等待稳定。
        /// 2) 从 ARINC429 接收温度数据，过滤 Label=175(oct) 并分别统计 SDI0 与 SDI1。
        /// 3) 每路采集 5 帧有效数据取平均值，写回界面显示并返回成功。
        /// </summary>
        /// <param name="title">点位名称（点1/点2/点3，用于日志）</param>
        /// <param name="resistanceOhm">需要设置到电阻箱的阻值（Ω）</param>
        /// <returns>true=成功采集到两路温度，false=超时/异常/被取消</returns>
        private async Task<bool> MeasurePointAsync(
            string title,
            double resistanceOhm,
            Action<string> setTextA,
            Action<double?> setValueA,
            Action<string> setTextB,
            Action<double?> setValueB,
            CancellationToken cancellationToken)
        {
            if (!(IsAutoTestRunning || (IsManualTestRunning && CanMeasure)))
            {
                Log($"{title}: 当前未处于测试状态");
                return false;
            }

            await _measureLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                Log($"{title}: 设置程控电阻 RO0/RO1={resistanceOhm:0.###}Ω");
                await SetResistanceAsync(resistanceOhm, cancellationToken).ConfigureAwait(false);
                await Task.Delay(ResistanceSettleMs, cancellationToken).ConfigureAwait(false);
                _ = await _arinc.ReadRxWordsAsync(RxChannelIndex, maxCount: 4096, enableTimeTag: false, enableRateAdaption: false, cancellationToken: cancellationToken).ConfigureAwait(false);

                Log($"{title}: 开始接收温度数据，Label=175(oct)，SDI=2更新112~114，SDI=3更新116~118");

                var samplesA = new List<double>(SamplesPerMeasure);
                var samplesB = new List<double>(SamplesPerMeasure);
                var deadline = DateTime.UtcNow.AddMilliseconds(SampleTimeoutMs);

                // 循环接收 ARINC429 数据直到采集足够样本或超时
                while (!cancellationToken.IsCancellationRequested && DateTime.UtcNow <= deadline)
                {
                    var words = await _arinc.ReadRxWordsAsync(RxChannelIndex, maxCount: 512, enableTimeTag: false, enableRateAdaption: false, cancellationToken: cancellationToken)
                        .ConfigureAwait(false);

                    if (words.Count > 0)
                    {
                        foreach (var w in words)
                        {
                            // 奇偶校验不通过的数据直接丢弃
                            if (!_arinc.VerifyOddParity(w.Data429))
                                continue;

                            _arinc.ParseRawWord(w.Data429, out var label, out var wordSdi, out var data19, out _);

                            // 只处理温度 Label（175(oct)）
                            if (!IsExpectedLabel(label))
                                continue;

                            var v = DecodeTemp(data19);
                            if (v == null)
                                continue;

                            if (wordSdi == TempChannel112To114Sdi)
                            {
                                if (samplesA.Count < SamplesPerMeasure)
                                    samplesA.Add(v.Value);
                                setTextA($"{v.Value:0} ℃");
                            }
                            else if (wordSdi == TempChannel116To118Sdi)
                            {
                                if (samplesB.Count < SamplesPerMeasure)
                                    samplesB.Add(v.Value);
                                setTextB($"{v.Value:0} ℃");
                            }
                            else
                            {
                                continue;
                            }

                            if (samplesA.Count >= SamplesPerMeasure && samplesB.Count >= SamplesPerMeasure)
                            {
                                var avgA = samplesA.Average();
                                var avgB = samplesB.Average();

                                setValueA(avgA);
                                setValueB(avgB);

                                setTextA($"{avgA:0} ℃");
                                setTextB($"{avgB:0} ℃");

                                Log($"{title}: 完成，112~114平均={avgA:0.###}℃ 116~118平均={avgB:0.###}℃");
                                return true;
                            }
                        }
                    }

                    await Task.Delay(10, cancellationToken).ConfigureAwait(false);
                }

                setTextA("超时");
                setValueA(null);

                setTextB("超时");
                setValueB(null);

                if (IsManualTestRunning)
                {
                    Log($"{title}: 接收超时，未获取到{SamplesPerMeasure}帧有效温度数据");
                    Log($"{title}: 本次测量按超时结束处理，结果保留为--，不可重复点击");
                }
                else
                {
                    Log($"{title}: 接收超时，未获取到{SamplesPerMeasure}帧有效温度数据");
                }

                return false;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception ex)
            {
                setTextA("--");
                setValueA(null);
                setTextB("--");
                setValueB(null);
                if (IsManualTestRunning)
                {
                    await AbortManualTestAsync($"{title}: 采集异常，手动测试中止: {ex.Message}").ConfigureAwait(false);
                }
                else
                {
                    Log($"{title}: 采集异常: {ex.Message}");
                }
                return false;
            }
            finally
            {
                _measureLock.Release();
            }
        }

        /// <summary>
        /// 判断当前 ARINC429 Label 是否为温度 Label（兼容字节序反转）
        /// </summary>
        private bool IsExpectedLabel(byte label)
        {
            return label == TempLabelDec || label == _arinc.ReverseLabel(TempLabelDec);
        }

        /// <summary>
        /// 解码温度值（BNR：有符号二进制数）
        /// </summary>
        private double? DecodeTemp(uint data19)
        {
            var value = _arinc.DecodeBnr(data19, bitLength: TempBnrBitLength, resolution: TempResolution, msbPosition: TempMsbPosition);
            return Math.Round(value, 0, MidpointRounding.AwayFromZero);
        }

        /// <summary>
        /// 当三点都已测量完成时，按阈值范围判定结果，并更新“上次/本次”测试结论。
        /// </summary>
        private async Task TryFinalizeIfAllMeasuredAsync()
        {
            if (!(_measured1 && _measured2 && _measured3))
                return;

            var p1a = _temp1 != null && _temp1 >= T1_Min && _temp1 <= T1_Max;
            var p1b = _temp1B != null && _temp1B >= T1_Min && _temp1B <= T1_Max;
            var p2a = _temp2 != null && _temp2 >= T2_Min && _temp2 <= T2_Max;
            var p2b = _temp2B != null && _temp2B >= T2_Min && _temp2B <= T2_Max;
            var p3a = _temp3 != null && _temp3 >= T3_Min && _temp3 <= T3_Max;
            var p3b = _temp3B != null && _temp3B >= T3_Min && _temp3B <= T3_Max;

            var p1 = p1a && p1b;
            var p2 = p2a && p2b;
            var p3 = p3a && p3b;
            var pass = p1 && p2 && p3;

            var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            var resultText = pass ? "合格" : "不合格";
            CurrentTestResult = resultText;
            PreviousTestTime = now;
            PreviousTestResult = resultText;
            LastTestTime = now;
            LastTestResult = resultText;

            SaveTestResultToProject();

            Log($"判据: 点1[{T1_Min:0.###},{T1_Max:0.###}] => {FormatBool(p1)}");
            Log($"判据: 点2[{T2_Min:0.###},{T2_Max:0.###}] => {FormatBool(p2)}");
            Log($"判据: 点3[{T3_Min:0.###},{T3_Max:0.###}] => {FormatBool(p3)}");
            Log($"最终结果: {resultText}");

            if (IsManualTestRunning)
            {
                await StopManualTestAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// 中止手动测试（通常用于初始化失败、采集超时、采集异常等）
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

        /// <summary>
        /// 停止手动测试并释放硬件资源（关闭电源输出、停止 ARINC429 接收、断开电阻箱）
        /// </summary>
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

            Log("手动测试停止/结束，正在按反序断开电阻箱、28V、429、DO27、485继电器...");
            await CleanupIoAsync().ConfigureAwait(false);
            IsManualTestRunning = false;
            Log("手动测试已结束");
        }

        /// <summary>
        /// 停止自动测试并释放硬件资源
        /// </summary>
        private async Task StopAutoTestAsync()
        {
            try
            {
                _autoCts?.Cancel();
            }
            catch
            {
            }

            Log("自动测试停止/结束，正在按反序断开电阻箱、28V、429、DO27、485继电器...");
            await CleanupIoAsync().ConfigureAwait(false);
            IsAutoTestRunning = false;
            Log("自动测试已结束");
        }

        /// <summary>
        /// 清理硬件资源（ARINC429、电源、程控电阻箱）
        /// 这里使用大量 try-catch，目的是：即使某个设备清理失败，也尽量继续清理其它设备。
        /// </summary>
        private async Task CleanupIoAsync()
        {
            try
            {
                if (_res != null)
                {
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

            try
            {
                if (_power != null)
                {
                    try { await _power.SetOutputEnabledAsync(PowerSupplyChannel.CH1, false, CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _power.DisconnectAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _power.DisposeAsync().ConfigureAwait(false); } catch { }
                }
            }
            catch
            {
            }
            finally
            {
                _power = null;
            }

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
            catch
            {
            }
            finally
            {
                _arinc = null;
            }

            try
            {
                await EnsureGroundDoAsync(on: false, cancellationToken: CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
            }

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
            catch
            {
            }
            finally
            {
                _jy7131 = null;
                _isRelay485On = false;
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
                    {
                        return;
                    }

                    var device = FindFirstJy7131Device();
                    if (device == null)
                    {
                        throw new InvalidOperationException("未找到PXIe-7131(JY7131)板卡，无法开启485继电器第7路");
                    }

                    if (_jy7131 == null)
                    {
                        var slot = device is DigitalIODevice dio ? dio.SlotIndex : 0;
                        _jy7131 = new Jy7131Api(device, slot);
                    }

                    if (!_jy7131.IsConnected)
                    {
                        await _jy7131.ConnectAsync(cancellationToken).ConfigureAwait(false);
                    }

                    if (!_jy7131.IsRunning)
                    {
                        await _jy7131.SetOutputModeAsync(Jy7131OutputMode.Sinking, cancellationToken).ConfigureAwait(false);
                        await _jy7131.StartAsync(cancellationToken).ConfigureAwait(false);
                    }

                    await _jy7131.SetRelayAsync(Relay485ChannelIndex, true, cancellationToken).ConfigureAwait(false);
                    Log($"485继电器板 第{Relay485ChannelIndex + 1}路已闭合");

                    await Task.Delay(100, cancellationToken).ConfigureAwait(false);

                    _isRelay485On = true;
                    Log($"485继电器准备完成: 第{Relay485ChannelIndex + 1}路=ON");
                }
                else
                {
                    if (!_isRelay485On)
                    {
                        return;
                    }

                    if (_jy7131 != null)
                    {
                        try
                        {
                            await _jy7131.SetRelayAsync(Relay485ChannelIndex, false, cancellationToken).ConfigureAwait(false);
                            Log($"485继电器板 第{Relay485ChannelIndex + 1}路已断开");
                        }
                        catch (Exception ex)
                        {
                            Log($"关闭485继电器板 第{Relay485ChannelIndex + 1}路失败: {ex.Message}");
                        }
                    }

                    _isRelay485On = false;
                    Log($"485继电器已关闭: 第{Relay485ChannelIndex + 1}路=OFF");
                }
            }
            finally
            {
                _relayLock.Release();
            }
        }

        private async Task EnsureGroundDoAsync(bool on, CancellationToken cancellationToken)
        {
            var device = FindFirstJy7131Device();
            if (device == null)
            {
                throw new InvalidOperationException("未找到PXIe-7131(JY7131)板卡，无法控制DO27");
            }

            if (_jy7131 == null)
            {
                var slot = device is DigitalIODevice dio ? dio.SlotIndex : 0;
                _jy7131 = new Jy7131Api(device, slot);
            }

            if (!_jy7131.IsConnected)
            {
                await _jy7131.ConnectAsync(cancellationToken).ConfigureAwait(false);
            }

            if (!_jy7131.IsRunning)
            {
                await _jy7131.SetOutputModeAsync(Jy7131OutputMode.Sinking, cancellationToken).ConfigureAwait(false);
                await _jy7131.StartAsync(cancellationToken).ConfigureAwait(false);
            }

            await WriteInitDosAsync(on, cancellationToken).ConfigureAwait(false);
        }

        private async Task WriteInitDosAsync(bool on, CancellationToken cancellationToken)
        {
            await _jy7131.WriteDoAsync($"DO{RelayAuxDoIndex}", on, cancellationToken).ConfigureAwait(false);
            Log($"7131 DO{RelayAuxDoIndex} 已{(on ? "置位" : "复位")}");

            await _jy7131.WriteDoAsync($"DO{RelayGroundDoIndex}", on, cancellationToken).ConfigureAwait(false);
            Log($"7131 DO{RelayGroundDoIndex} 已{(on ? "置位" : "复位")}");
        }

        /// <summary>
        /// 确保程控电源已连接并输出 28V（CH1）
        /// </summary>
        private async Task EnsurePowerAsync(CancellationToken cancellationToken)
        {
            _power ??= new PowerSupplySocketApi();
            await _power.ConnectAsync(PowerSupplyIpAddress, cancellationToken).ConfigureAwait(false);
            await _power.ApplyAsync(PowerSupplyChannel.CH1, InputVoltageV, InputCurrentA, cancellationToken).ConfigureAwait(false);
            await _power.SetOutputEnabledAsync(PowerSupplyChannel.CH1, true, cancellationToken).ConfigureAwait(false);
            await Task.Delay(300, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// 确保 ARINC429 接收通道已打开并开始接收（Odd parity, 标准 429 格式）
        /// </summary>
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
            {
                await _arinc.ConnectAsync(cancellationToken).ConfigureAwait(false);
            }

            await _arinc.OpenRxAsync(RxChannelIndex, cancellationToken).ConfigureAwait(false);
            await _arinc.ConfigureRxAsync(
                RxChannelIndex,
                rate: ArincRate,
                parity: Art4229Parity.Odd,
                wordFormat: Art4229WordFormat.Standard429,
                enableInterrupt: false,
                interruptDepth: 512,
                enableTimeTag: false,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            await _arinc.StartRxAsync(RxChannelIndex, cancellationToken).ConfigureAwait(false);
            _ = await _arinc.ReadRxWordsAsync(RxChannelIndex, maxCount: 4096, enableTimeTag: false, enableRateAdaption: false, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// 确保程控电阻箱已连接，并初始化为 0Ω
        /// </summary>
        private async Task EnsureResistanceAsync(CancellationToken cancellationToken)
        {
            if (_res != null && _res.IsConnected)
                return;

            var device = FindFirstActs6010Device();
            if (device == null)
                throw new InvalidOperationException("未找到ACTS6010(程控电阻)板卡");

            _res = new ACTS6010Driver(device, logicalId: 0);
            var ok = await _res.ConnectAsync().ConfigureAwait(false);
            if (!ok)
                throw new InvalidOperationException("ACTS6010连接失败");

            await SetResistanceAsync(0.0, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// 设置程控电阻箱阻值（同时设置 RO0 与 RO1）
        /// </summary>
        private async Task SetResistanceAsync(double resistanceOhm, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_res == null || !_res.IsConnected)
                throw new InvalidOperationException("ACTS6010未连接");

            var ok0 = await _res.WriteChannelAsync("RO0", resistanceOhm).ConfigureAwait(false);
            var ok1 = await _res.WriteChannelAsync("RO1", resistanceOhm).ConfigureAwait(false);
            if (!(ok0 && ok1))
                throw new InvalidOperationException("设置ACTS6010阻值失败");
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

        /// <summary>
        /// 在 PXI 机箱中查找 ACTS6010 程控电阻箱设备
        /// </summary>
        private DeviceBase FindFirstActs6010Device()
        {
            var chassisList = _pxiChassisService?.GetAllChassis();
            if (chassisList == null)
                return null;

            var preferredChassisName = _singleBoardTestContext?.ChassisName;

            foreach (var chassis in chassisList)
            {
                if (!string.IsNullOrWhiteSpace(preferredChassisName) &&
                    !string.Equals(chassis?.Name, preferredChassisName, StringComparison.OrdinalIgnoreCase))
                    continue;

                var typedDevice = chassis?.Devices?.FirstOrDefault(d => d is ProgrammableResistorDevice);
                if (typedDevice != null)
                    return typedDevice;
            }

            foreach (var chassis in chassisList)
            {
                var device = chassis?.Devices?.FirstOrDefault(d =>
                    (d is ProgrammableResistorDevice) ||
                    (d?.Model?.IndexOf("7012", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.Model?.IndexOf("ACTS", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.DeviceType?.IndexOf("resistor", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.Name?.IndexOf("6010", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.Name?.IndexOf("ACTS", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.CardName?.IndexOf("6010", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.CardName?.IndexOf("ACTS", StringComparison.OrdinalIgnoreCase) >= 0));

                if (device != null)
                    return device;
            }

            return null;
        }

        /// <summary>
        /// 记录日志到界面
        /// </summary>
        private void Log(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.Invoke(() => Logs.Add(line));
                return;
            }

            Logs.Add(line);
        }

        private static string FormatBool(bool value) => value ? "合格" : "不合格";
    }
}
