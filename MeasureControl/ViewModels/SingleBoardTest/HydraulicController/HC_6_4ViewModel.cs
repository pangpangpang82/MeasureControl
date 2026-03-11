using Prism.Commands;
using Prism.Mvvm;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
    /// HC_6_4 测试项：压力信号测试（压力传感器校验）
    /// 测试目的：通过 MTX532 模拟量输出板卡输出特定电压，模拟压力传感器信号，
    ///          再从 ARINC429 总线接收压力数据，验证三路压力系统（SDI0/1/2）是否正确。
    /// 测试方法：
    /// 1) 给被测板供电 28V。
    /// 2) MTX532 的 AO1/AO2/AO3 同时输出指定电压（点1: 0.5V, 点2: 7.17V, 点3: 3.0V）。
    /// 3) 从 ARINC429 接收压力 Label=174(oct)（十进制 124）数据，分别统计 SDI0/1/2（对应 SYS1/2/3）。
    /// 4) 每路采集 5 帧有效数据取平均值，并与阈值范围比对，给出“合格/不合格”。
    /// </summary>
    public class HC_6_4ViewModel : BindableBase
    {
        // 电源配置（给被测板供电）
        private const string PowerSupplyIpAddress = "192.168.1.15";
        private const double InputVoltageV = 28.0;
        private const double InputCurrentA = 1;

        // ARINC429 接收配置
        private const int RxChannelIndex = 2;
        private const double ArincRate = 100000.0;

        // 压力数据定义（Label=174(oct) 即十进制 124）
        private const byte PressLabelDec = 124; // 174(oct)
        private const string TestItemName = "压力信号测试";
        private const int PressureBitLength = 12;

        // 协议规定 SSM=0 为正常数据
        private const byte SsmNormal = 3;

        // 采样参数
        private const int SamplesPerMeasure = 1;      // 每路采集 3 帧取平均
        private const int SampleTimeoutMs = 3000;     // 采样超时 5 秒
        private const int AoSettleMs = 500;            // 模拟量输出稳定等待时间
        private const int Mtx532ReadyTimeoutMs = 6000;
        private const int Mtx532ReadyPollMs = 200;
        private const int PostSwitchRxFlushMs = 120;

        // 三个测试点对应的模拟电压（由 MTX532 输出到压力传感器模拟通道）
        private const double Point1VoltageV = 0.5;    // 点1: 0.5V
        private const double Point2VoltageV = 7.17;   // 点2: 7.17V
        private const double Point3VoltageV = 3.0;    // 点3: 3.0V

        private readonly SemaphoreSlim _measureLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _relayLock = new SemaphoreSlim(1, 1);
        private readonly IPxiChassisService _pxiChassisService;
        private readonly ISingleBoardTestContextService _singleBoardTestContext;

        private CancellationTokenSource _manualCts;
        private CancellationTokenSource _autoCts;

        private IPowerSupplyApi _power;
        private IArt4229Api _arinc;
        private IMtx532Api _mtx532;
        private Jy7131Api _jy7131;
        private bool _isRelay485On;

        private const int Relay485ChannelIndex = 6;
        private const int RelayAuxDoIndex = 25;
        private const int RelayGroundDoIndex = 26;

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

        private string _p1Sys1Text = "--";
        private string _p1Sys2Text = "--";
        private string _p1Sys3Text = "--";

        private string _p2Sys1Text = "--";
        private string _p2Sys2Text = "--";
        private string _p2Sys3Text = "--";

        private string _p3Sys1Text = "--";
        private string _p3Sys2Text = "--";
        private string _p3Sys3Text = "--";

        private double? _p1Sys1;
        private double? _p1Sys2;
        private double? _p1Sys3;
        private double? _p2Sys1;
        private double? _p2Sys2;
        private double? _p2Sys3;
        private double? _p3Sys1;
        private double? _p3Sys2;
        private double? _p3Sys3;

        private bool _canMeasure;

        public HC_6_4ViewModel(IPxiChassisService pxiChassisService, ISingleBoardTestContextService singleBoardTestContext)
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

        public string LastTestTime
        {
            get => _lastTestTime;
            set => SetProperty(ref _lastTestTime, value);
        }

        public string LastTestResult
        {
            get => _lastTestResult;
            set => SetProperty(ref _lastTestResult, value);
        }

        public string PreviousTestTime
        {
            get => _previousTestTime;
            set => SetProperty(ref _previousTestTime, value);
        }

        public string PreviousTestResult
        {
            get => _previousTestResult;
            set => SetProperty(ref _previousTestResult, value);
        }

        public string PressurePoint1Sys1Text
        {
            get => _p1Sys1Text;
            private set => SetProperty(ref _p1Sys1Text, value);
        }

        public string PressurePoint1Sys2Text
        {
            get => _p1Sys2Text;
            private set => SetProperty(ref _p1Sys2Text, value);
        }

        public string PressurePoint1Sys3Text
        {
            get => _p1Sys3Text;
            private set => SetProperty(ref _p1Sys3Text, value);
        }

        public string PressurePoint2Sys1Text
        {
            get => _p2Sys1Text;
            private set => SetProperty(ref _p2Sys1Text, value);
        }

        public string PressurePoint2Sys2Text
        {
            get => _p2Sys2Text;
            private set => SetProperty(ref _p2Sys2Text, value);
        }

        public string PressurePoint2Sys3Text
        {
            get => _p2Sys3Text;
            private set => SetProperty(ref _p2Sys3Text, value);
        }

        public string PressurePoint3Sys1Text
        {
            get => _p3Sys1Text;
            private set => SetProperty(ref _p3Sys1Text, value);
        }

        public string PressurePoint3Sys2Text
        {
            get => _p3Sys2Text;
            private set => SetProperty(ref _p3Sys2Text, value);
        }

        public string PressurePoint3Sys3Text
        {
            get => _p3Sys3Text;
            private set => SetProperty(ref _p3Sys3Text, value);
        }

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

            ResetPointDisplays();

            _manualCts?.Cancel();
            _manualCts?.Dispose();
            _manualCts = new CancellationTokenSource();

            Log("开始手动测试");
            Log($"电源: CH1 {InputVoltageV:0.###}V {InputCurrentA:0.###}A, IP={PowerSupplyIpAddress}");
            Log($"MTX532: AO前三通道输出同电压");
            Log($"ARINC429: RX通道{RxChannelIndex + 1}, 码率 {ArincRate:0}bps, 压力Label=174(oct) SDI=1/2/3->SYS1/2/3, SSM=0");

            try
            {
                await EnsureRelay485Async(on: true, cancellationToken: _manualCts.Token).ConfigureAwait(false);
                await EnsureGroundDoAsync(on: true, cancellationToken: _manualCts.Token).ConfigureAwait(false);
                await EnsureArincRxAsync(_manualCts.Token).ConfigureAwait(false);
                await EnsureMtx532Async(_manualCts.Token).ConfigureAwait(false);
                await EnsurePowerAsync(_manualCts.Token).ConfigureAwait(false);

                CanMeasure = true;
                Log("手动测试初始化完成，可分别点击三档电压测量压力");
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

            ResetPointDisplays();

            _autoCts?.Cancel();
            _autoCts?.Dispose();
            _autoCts = new CancellationTokenSource();

            Log("开始自动测试");

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
            IsAutoTestRunning = true;
            CanMeasure = false;
            _manualAborted = false;

            _measured1 = false;
            _measured2 = false;
            _measured3 = false;

            ResetPointDisplays();

            Log("开始自动测试");

            try
            {
                await EnsureRelay485Async(on: true, cancellationToken: cancellationToken).ConfigureAwait(false);
                await EnsureGroundDoAsync(on: true, cancellationToken: cancellationToken).ConfigureAwait(false);
                await EnsureArincRxAsync(cancellationToken).ConfigureAwait(false);
                await EnsureMtx532Async(cancellationToken).ConfigureAwait(false);
                await EnsurePowerAsync(cancellationToken).ConfigureAwait(false);

                var ok1 = await MeasurePointAllSystemsAsync("0.5V点", Point1VoltageV,
                    setSys1: t => PressurePoint1Sys1Text = t,
                    setSys2: t => PressurePoint1Sys2Text = t,
                    setSys3: t => PressurePoint1Sys3Text = t,
                    setV1: v => _p1Sys1 = v,
                    setV2: v => _p1Sys2 = v,
                    setV3: v => _p1Sys3 = v,
                    cancellationToken).ConfigureAwait(false);

                await Task.Delay(80, cancellationToken).ConfigureAwait(false);

                var ok2 = await MeasurePointAllSystemsAsync("7.17V点", Point2VoltageV,
                    setSys1: t => PressurePoint2Sys1Text = t,
                    setSys2: t => PressurePoint2Sys2Text = t,
                    setSys3: t => PressurePoint2Sys3Text = t,
                    setV1: v => _p2Sys1 = v,
                    setV2: v => _p2Sys2 = v,
                    setV3: v => _p2Sys3 = v,
                    cancellationToken).ConfigureAwait(false);

                await Task.Delay(80, cancellationToken).ConfigureAwait(false);

                var ok3 = await MeasurePointAllSystemsAsync("3.0V点", Point3VoltageV,
                    setSys1: t => PressurePoint3Sys1Text = t,
                    setSys2: t => PressurePoint3Sys2Text = t,
                    setSys3: t => PressurePoint3Sys3Text = t,
                    setV1: v => _p3Sys1 = v,
                    setV2: v => _p3Sys2 = v,
                    setV3: v => _p3Sys3 = v,
                    cancellationToken).ConfigureAwait(false);

                _measured1 = ok1;
                _measured2 = ok2;
                _measured3 = ok3;

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

        private async Task OnMeasurePoint1Async()
        {
            var ok = await MeasurePointAllSystemsAsync("0.5V点", Point1VoltageV,
                setSys1: t => PressurePoint1Sys1Text = t,
                setSys2: t => PressurePoint1Sys2Text = t,
                setSys3: t => PressurePoint1Sys3Text = t,
                setV1: v => _p1Sys1 = v,
                setV2: v => _p1Sys2 = v,
                setV3: v => _p1Sys3 = v,
                _manualCts?.Token ?? CancellationToken.None).ConfigureAwait(false);

            if (!IsManualTestRunning || _manualAborted) return;
            _measured1 = true;
            RefreshMeasureCommands();

            await TryFinalizeIfAllMeasuredAsync().ConfigureAwait(false);
        }

        private async Task OnMeasurePoint2Async()
        {
            var ok = await MeasurePointAllSystemsAsync("7.17V点", Point2VoltageV,
                setSys1: t => PressurePoint2Sys1Text = t,
                setSys2: t => PressurePoint2Sys2Text = t,
                setSys3: t => PressurePoint2Sys3Text = t,
                setV1: v => _p2Sys1 = v,
                setV2: v => _p2Sys2 = v,
                setV3: v => _p2Sys3 = v,
                _manualCts?.Token ?? CancellationToken.None).ConfigureAwait(false);

            if (!IsManualTestRunning || _manualAborted) return;
            _measured2 = true;
            RefreshMeasureCommands();

            await TryFinalizeIfAllMeasuredAsync().ConfigureAwait(false);
        }

        private async Task OnMeasurePoint3Async()
        {
            var ok = await MeasurePointAllSystemsAsync("3.0V点", Point3VoltageV,
                setSys1: t => PressurePoint3Sys1Text = t,
                setSys2: t => PressurePoint3Sys2Text = t,
                setSys3: t => PressurePoint3Sys3Text = t,
                setV1: v => _p3Sys1 = v,
                setV2: v => _p3Sys2 = v,
                setV3: v => _p3Sys3 = v,
                _manualCts?.Token ?? CancellationToken.None).ConfigureAwait(false);

            if (!IsManualTestRunning || _manualAborted) return;
            _measured3 = true;
            RefreshMeasureCommands();

            await TryFinalizeIfAllMeasuredAsync().ConfigureAwait(false);
        }

        private async Task<bool> MeasurePointAllSystemsAsync(
            string title,
            double aoVoltage,
            Action<string> setSys1,
            Action<string> setSys2,
            Action<string> setSys3,
            Action<double?> setV1,
            Action<double?> setV2,
            Action<double?> setV3,
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
                var halfVoltage = aoVoltage / 2.0;
                Log($"{title}: 设置差分AO输出 SYS1(AO0={halfVoltage:0.###}V,AO1={-halfVoltage:0.###}V) SYS2(AO2={halfVoltage:0.###}V,AO3={-halfVoltage:0.###}V) SYS3(AO4={halfVoltage:0.###}V,AO5={-halfVoltage:0.###}V)");
                await SetAo012Async(aoVoltage, cancellationToken).ConfigureAwait(false);
                await Task.Delay(AoSettleMs, cancellationToken).ConfigureAwait(false);
                _ = await _arinc.ReadRxWordsAsync(RxChannelIndex, maxCount: 4096, enableTimeTag: false, enableRateAdaption: false, cancellationToken: cancellationToken).ConfigureAwait(false);
                await Task.Delay(PostSwitchRxFlushMs, cancellationToken).ConfigureAwait(false);

                var ok1 = await MeasureSingleSystemAsync($"{title}-SYS1", sdi: 1, setSys1, setV1, cancellationToken).ConfigureAwait(false);
                await Task.Delay(80, cancellationToken).ConfigureAwait(false);
                var ok2 = await MeasureSingleSystemAsync($"{title}-SYS2", sdi: 2, setSys2, setV2, cancellationToken).ConfigureAwait(false);
                await Task.Delay(80, cancellationToken).ConfigureAwait(false);
                var ok3 = await MeasureSingleSystemAsync($"{title}-SYS3", sdi: 3, setSys3, setV3, cancellationToken).ConfigureAwait(false);

                return ok1 && ok2 && ok3;
            }
            finally
            {
                try
                {
                    if (_mtx532 != null && _mtx532.IsConnected)
                    {
                        await SetAo012Async(0.0, CancellationToken.None).ConfigureAwait(false);
                        Log($"{title}: 测量结束，AO0~AO5差分输出已停止(0V)");
                    }
                }
                catch (Exception ex)
                {
                    Log($"{title}: 测量结束后停止AO输出失败: {ex.Message}");
                }

                _measureLock.Release();
            }
        }

        private async Task<bool> MeasureSingleSystemAsync(
            string title,
            byte sdi,
            Action<string> setText,
            Action<double?> setValue,
            CancellationToken cancellationToken)
        {
            Log($"{title}: 开始接收压力，Label=174(oct) SDI={sdi}");

            var samples = new List<double>(SamplesPerMeasure);
            var deadline = DateTime.UtcNow.AddMilliseconds(SampleTimeoutMs);

            while (!cancellationToken.IsCancellationRequested && DateTime.UtcNow <= deadline)
            {
                var words = await _arinc.ReadRxWordsAsync(RxChannelIndex, maxCount: 512, enableTimeTag: false, enableRateAdaption: false, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (words.Count > 0)
                {
                    foreach (var w in words)
                    {
                        //if (!_arinc.VerifyOddParity(w.Data429))
                        //    continue;

                        _arinc.ParseRawWord(w.Data429, out var label, out var wordSdi, out var data19, out var ssm);

                        if (!IsExpectedLabel(label))
                            continue;

                        if (wordSdi != sdi)
                            continue;

                        if (ssm != SsmNormal)
                            continue;

                        var v = DecodePressure(data19);
                        samples.Add(v);

                        var avg = samples.Average();
                        setText($"{v:0.0}");

                        if (samples.Count >= SamplesPerMeasure)
                        {
                            setValue(avg);
                            setText($"{avg:0.0}");
                            Log($"{title}: 完成，平均压力={avg:0.###}");
                            return true;
                        }
                    }
                }

                await Task.Delay(10, cancellationToken).ConfigureAwait(false);
            }

            setText("超时");
            setValue(null);

            if (IsManualTestRunning)
            {
                Log($"{title}: 接收超时，未获取到{SamplesPerMeasure}帧有效压力数据");
                Log($"{title}: 本次测量按超时结束处理，结果保留为--，不可重复点击");
            }
            else
            {
                Log($"{title}: 接收超时，未获取到{SamplesPerMeasure}帧有效压力数据");
            }

            return false;
        }

        private bool IsExpectedLabel(byte label)
        {
            return _arinc.ReverseLabel(label) == PressLabelDec;
        }

        private double DecodePressure(uint data19)
        {
            var value = _arinc.DecodeUbnr(data19, bitLength: PressureBitLength, resolution: 1.0, msbPosition: 27);
            return Math.Round(value, 1, MidpointRounding.AwayFromZero);
        }

        private async Task TryFinalizeIfAllMeasuredAsync()
        {
            if (!(_measured1 && _measured2 && _measured3))
                return;

            var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            const double P1Min = 0.0;
            const double P1Max = 85.0;
            const double P2Min = 3915.0;
            const double P2Max = 4000.0;
            const double P3Min = 1414.0;
            const double P3Max = 1585.0;

            var failures = new List<string>();

            void Check(string name, double? value, double min, double max)
            {
                if (!value.HasValue)
                {
                    failures.Add($"{name}=--");
                    return;
                }

                var v = value.Value;
                if (v < min || v > max)
                {
                    failures.Add($"{name}={v:0.###}psi not in [{min:0.###},{max:0.###}]");
                }
            }

            Check("P1-SYS1", _p1Sys1, P1Min, P1Max);
            Check("P1-SYS2", _p1Sys2, P1Min, P1Max);
            Check("P1-SYS3", _p1Sys3, P1Min, P1Max);

            Check("P2-SYS1", _p2Sys1, P2Min, P2Max);
            Check("P2-SYS2", _p2Sys2, P2Min, P2Max);
            Check("P2-SYS3", _p2Sys3, P2Min, P2Max);

            Check("P3-SYS1", _p3Sys1, P3Min, P3Max);
            Check("P3-SYS2", _p3Sys2, P3Min, P3Max);
            Check("P3-SYS3", _p3Sys3, P3Min, P3Max);

            var resultText = failures.Count == 0 ? "合格" : "不合格";
            CurrentTestResult = resultText;
            PreviousTestTime = now;
            PreviousTestResult = resultText;
            LastTestTime = now;
            LastTestResult = resultText;

            if (failures.Count == 0)
            {
                Log("自动判定: 合格 (三档位三路压力均在范围内)");
            }
            else
            {
                Log("自动判定: 不合格");
                foreach (var f in failures)
                {
                    Log($"判据不满足: {f}");
                }
            }

            SaveTestResultToProject();

            if (IsManualTestRunning)
            {
                await StopManualTestAsync().ConfigureAwait(false);
            }
        }

        private async Task AbortManualTestAsync(string reason)
        {
            _manualAborted = true;
            if (!string.IsNullOrWhiteSpace(reason))
            {
                Log(reason);
            }

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

            Log("手动测试停止/结束，正在按反序断开28V、MT532、429、DO27、485继电器...");
            await CleanupIoAsync().ConfigureAwait(false);
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

            Log("自动测试停止/结束，正在按反序断开28V、MT532、429、DO27、485继电器...");
            await CleanupIoAsync().ConfigureAwait(false);
            IsAutoTestRunning = false;
            Log("自动测试已结束");
        }

        private async Task CleanupIoAsync()
        {
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
                if (_mtx532 != null)
                {
                    try { await _mtx532.StopOutputAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _mtx532.ResetAllToZeroAsync(disableAfterReset: true, cancellationToken: CancellationToken.None).ConfigureAwait(false); } catch { }
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
                if (_jy7131 != null)
                {
                    try { await WriteInitDosAsync(false, CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _jy7131.SetRelayAsync(Relay485ChannelIndex, false, CancellationToken.None).ConfigureAwait(false); } catch { }
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

        private void ResetPointDisplays()
        {
            _p1Sys1 = null;
            _p1Sys2 = null;
            _p1Sys3 = null;
            _p2Sys1 = null;
            _p2Sys2 = null;
            _p2Sys3 = null;
            _p3Sys1 = null;
            _p3Sys2 = null;
            _p3Sys3 = null;

            PressurePoint1Sys1Text = "--";
            PressurePoint1Sys2Text = "--";
            PressurePoint1Sys3Text = "--";
            PressurePoint2Sys1Text = "--";
            PressurePoint2Sys2Text = "--";
            PressurePoint2Sys3Text = "--";
            PressurePoint3Sys1Text = "--";
            PressurePoint3Sys2Text = "--";
            PressurePoint3Sys3Text = "--";
        }

        private async Task EnsurePowerAsync(CancellationToken cancellationToken)
        {
            _power ??= new PowerSupplySocketApi();
            await _power.ConnectAsync(PowerSupplyIpAddress, cancellationToken).ConfigureAwait(false);
            await _power.ApplyAsync(PowerSupplyChannel.CH1, InputVoltageV, InputCurrentA, cancellationToken).ConfigureAwait(false);
            await _power.SetOutputEnabledAsync(PowerSupplyChannel.CH1, true, cancellationToken).ConfigureAwait(false);
            await Task.Delay(300, cancellationToken).ConfigureAwait(false);
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

            await _mtx532.ConnectAsync(cancellationToken, new[] { "AO0", "AO1", "AO2", "AO3", "AO4", "AO5" }).ConfigureAwait(false);
            await SetAo012Async(0.0, cancellationToken).ConfigureAwait(false);
            await Task.Delay(300, cancellationToken).ConfigureAwait(false);
            await WaitForMtx532ReadyAsync(cancellationToken).ConfigureAwait(false);
            await _mtx532.StartOutputAsync(cancellationToken).ConfigureAwait(false);
            await Task.Delay(300, cancellationToken).ConfigureAwait(false);
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

        private async Task SetAo012Async(double voltageV, CancellationToken cancellationToken)
        {
            if (_mtx532 == null || !_mtx532.IsConnected)
                throw new InvalidOperationException("MTX532未连接");

            //var halfVoltage = voltageV / 2.0;
            var halfVoltage = voltageV;
            await _mtx532.WriteOnceDcAsync(new Dictionary<string, double>
            {
                ["AO0"] = halfVoltage,
                //["AO1"] = -halfVoltage,
                ["AO1"] = 0.0,
                ["AO2"] = halfVoltage,
                //["AO3"] = -halfVoltage,
                ["AO3"] = 0.0,
                ["AO4"] = halfVoltage,
                //["AO5"] = -halfVoltage
                ["AO5"] = 0.0,

            }, cancellationToken).ConfigureAwait(false);
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
                dispatcher.BeginInvoke(new Action(() => Logs.Add(line)));
                return;
            }

            Logs.Add(line);
        }
    }
}
