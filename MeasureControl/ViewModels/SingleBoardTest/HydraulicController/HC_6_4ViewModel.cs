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
using MeasureControl.Services;
using MeasureControl.Services.HardwareApis;

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

        // ---- 模拟产品开关（后续注释掉两处调用即可，无需改此常量）----
        private const bool EnableArincTxSimulation = true;
        // ---- END ----

        // ARINC429 发送配置
        private const int TxChannelIndex = 0;

        // 压力数据定义（Label=174(oct) 即十进制 124）
        private const byte PressLabelDec = 124; // 174(oct)

        // 协议规定 SSM=3 为正常数据
        private const byte SsmNormal = 0;

        // 采样参数
        private const int SamplesPerMeasure = 5;      // 每路采集 5 帧取平均
        private const int SampleTimeoutMs = 5000;     // 采样超时 5 秒
        private const int AoSettleMs = 100;            // 模拟量输出稳定等待时间

        // 三个测试点对应的模拟电压（由 MTX532 输出到压力传感器模拟通道）
        private const double Point1VoltageV = 0.5;    // 点1: 0.5V
        private const double Point2VoltageV = 7.17;   // 点2: 7.17V
        private const double Point3VoltageV = 3.0;    // 点3: 3.0V

        // 压力的 ARINC429 编码参数（12-bit 数据，位于 bit16-27）
        private const int PressureBitLength = 12;
        private const int PressureData19Shift = 5;    // 在 19-bit 数据域中的偏移
        private const uint PressureMask = (1u << PressureBitLength) - 1u;

        private readonly SemaphoreSlim _measureLock = new SemaphoreSlim(1, 1);
        private readonly IPxiChassisService _pxiChassisService;
        private readonly ISingleBoardTestContextService _singleBoardTestContext;

        private CancellationTokenSource _manualCts;
        private CancellationTokenSource _autoCts;

        private IPowerSupplyApi _power;
        private IArt4229Api _arinc;
        private IMtx532Api _mtx532;
        private bool _txOpened;

        private const string TestItemName = "压力传感器信号采集测试";

        private bool _canMeasure;
        private bool _measured1;
        private bool _measured2;
        private bool _measured3;
        private bool _manualAborted;

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

        private void SaveTestResultToProject()
        {
            var testItemNode = _singleBoardTestContext?.GetCurrentTestItemNode(TestItemName);
            if (testItemNode != null)
            {
                testItemNode.LastTestTime = PreviousTestTime;
                testItemNode.LastTestResult = PreviousTestResult;
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
            Log($"ARINC429: RX通道{RxChannelIndex + 1}, 码率 {ArincRate:0}bps, 压力Label=174(oct) SDI=1/2/3->SYS1/2/3");

            try
            {
                await EnsurePowerAsync(_manualCts.Token).ConfigureAwait(false);
                await EnsureMtx532Async(_manualCts.Token).ConfigureAwait(false);
                await EnsureArincRxAsync(_manualCts.Token).ConfigureAwait(false);

                // ---- 模拟产品启动（后续注释掉此 if 块即可关闭模拟）----
                if (EnableArincTxSimulation)
                {
                    await EnsureArincTxAsync(_manualCts.Token).ConfigureAwait(false);
                    _ = SimulateProductContinuousTxAsync(_manualCts.Token);
                    await Task.Delay(100, _manualCts.Token).ConfigureAwait(false);
                    Log("模拟产品: 已启动压力数据持续发送");
                }
                // ---- 模拟产品启动 END ----

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
                await EnsurePowerAsync(cancellationToken).ConfigureAwait(false);
                await EnsureMtx532Async(cancellationToken).ConfigureAwait(false);
                await EnsureArincRxAsync(cancellationToken).ConfigureAwait(false);

                // ---- 模拟产品启动（后续注释掉此 if 块即可关闭模拟）----
                if (EnableArincTxSimulation)
                {
                    await EnsureArincTxAsync(cancellationToken).ConfigureAwait(false);
                    _ = SimulateProductContinuousTxAsync(cancellationToken);
                    await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                    Log("模拟产品: 已启动压力数据持续发送");
                }
                // ---- 模拟产品启动 END ----

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
            if (ok)
            {
                _measured1 = true;
                RaisePropertyChanged(nameof(CanMeasurePoint1));
                MeasurePoint1Command?.RaiseCanExecuteChanged();
            }

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
            if (ok)
            {
                _measured2 = true;
                RaisePropertyChanged(nameof(CanMeasurePoint2));
                MeasurePoint2Command?.RaiseCanExecuteChanged();
            }

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
            if (ok)
            {
                _measured3 = true;
                RaisePropertyChanged(nameof(CanMeasurePoint3));
                MeasurePoint3Command?.RaiseCanExecuteChanged();
            }

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
                Log($"{title}: 设置AO输出 AO0/AO1/AO2={aoVoltage:0.###}V");
                await SetAo012Async(aoVoltage, cancellationToken).ConfigureAwait(false);
                await Task.Delay(AoSettleMs, cancellationToken).ConfigureAwait(false);

                var ok1 = await MeasureSingleSystemAsync($"{title}-SYS1", sdi: 1, setSys1, setV1, cancellationToken).ConfigureAwait(false);
                var ok2 = await MeasureSingleSystemAsync($"{title}-SYS2", sdi: 2, setSys2, setV2, cancellationToken).ConfigureAwait(false);
                var ok3 = await MeasureSingleSystemAsync($"{title}-SYS3", sdi: 3, setSys3, setV3, cancellationToken).ConfigureAwait(false);

                return ok1 && ok2 && ok3;
            }
            finally
            {
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
                        if (!_arinc.VerifyOddParity(w.Data429))
                            continue;

                        _arinc.ParseRawWord(w.Data429, out var label, out var wordSdi, out var data19, out var ssm);

                        if (!IsExpectedLabel(label))
                            continue;

                        if (wordSdi != sdi)
                            continue;

                        var v = DecodePressure(data19);
                        samples.Add(v);

                        var avg = samples.Average();
                        setText($"{v:0.###} ({samples.Count}/{SamplesPerMeasure}) 平均:{avg:0.###}");

                        if (samples.Count >= SamplesPerMeasure)
                        {
                            setValue(avg);
                            setText($"{avg:0.###}");
                            Log($"{title}: 完成，平均压力={avg:0.###}");
                            return true;
                        }
                    }
                }

                await Task.Delay(10, cancellationToken).ConfigureAwait(false);
            }

            setText("--");
            setValue(null);

            if (IsManualTestRunning)
            {
                await AbortManualTestAsync($"{title}: 接收超时，未获取到{SamplesPerMeasure}帧有效压力数据").ConfigureAwait(false);
            }
            else
            {
                Log($"{title}: 接收超时，未获取到{SamplesPerMeasure}帧有效压力数据");
            }

            return false;
        }

        private bool IsExpectedLabel(byte label)
        {
            return label == PressLabelDec || label == _arinc.ReverseLabel(PressLabelDec);
        }

        private double DecodePressure(uint data19)
        {
            // PRESS_SYS: bit16..bit27 (12 bits). In 19-bit ARINC data field (bit11..bit29), it maps to data19 bit5..bit16.
            var raw = (data19 >> PressureData19Shift) & PressureMask;
            return raw;
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

            Log("手动测试停止/结束，正在关闭电源输出并停止429接收...");
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

            Log("自动测试停止/结束，正在关闭电源输出并停止429接收...");
            await CleanupIoAsync().ConfigureAwait(false);
            IsAutoTestRunning = false;
            Log("自动测试已结束");
        }

        private async Task CleanupIoAsync()
        {
            try
            {
                if (_arinc != null)
                {
                    try { await _arinc.StopRxAsync(RxChannelIndex, CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _arinc.CloseRxAsync(RxChannelIndex, CancellationToken.None).ConfigureAwait(false); } catch { }
                    
                    if (_txOpened)
                    {
                        try { await _arinc.CloseTxAsync(TxChannelIndex, CancellationToken.None).ConfigureAwait(false); } catch { }
                        _txOpened = false;
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
                _txOpened = false;
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

            await _mtx532.ConnectAsync(cancellationToken).ConfigureAwait(false);
            await _mtx532.StartOutputAsync(cancellationToken).ConfigureAwait(false);
            await SetAo012Async(0.0, cancellationToken).ConfigureAwait(false);
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

            // Note: Mtx532Api NormalizeAoChannel expects AO1..AO32 and maps to AO0..AO31.
            await _mtx532.WriteOnceDcAsync(new Dictionary<string, double>
            {
                ["AO1"] = voltageV,
                ["AO2"] = voltageV,
                ["AO3"] = voltageV
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

        private DeviceBase FindFirstMtx532Device()
        {
            var chassisList = _pxiChassisService?.GetAllChassis();
            if (chassisList == null)
                return null;

            foreach (var chassis in chassisList)
            {
                var device = chassis?.Devices?.FirstOrDefault(d =>
                    (d?.Model?.IndexOf("mtx532", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.Name?.IndexOf("mtx532", StringComparison.OrdinalIgnoreCase) >= 0));

                if (device != null)
                    return device;
            }

            return null;
        }

        // =====================================================================
        // 模拟产品持续发送
        // =====================================================================

        /// <summary>
        /// 模拟产品持续发送压力数据
        /// 根据图片中的协议定义：
        /// - Label=174(oct) 对应所有三个压力系统
        /// - PRESS_SYS1_T1A: SDI=1
        /// - PRESS_SYS2_T1A: SDI=2  
        /// - PRESS_SYS3_T1A: SDI=3
        /// - 数据格式：12-bit UBNR，位于bit16-27，分辨率1，单位psia
        /// - SSM=3（正常数据），奇校验
        /// </summary>
        private async Task SimulateProductContinuousTxAsync(CancellationToken cancellationToken)
        {
            Log("模拟产品: 开始持续发送压力数据 (PRESS_SYS1/2/3)");
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var aoVoltage = await GetCurrentAoVoltageAsync(cancellationToken).ConfigureAwait(false);
                    var pressureValue = VoltageToPressure(aoVoltage);

                    // Label 174(oct) PRESS_SYS1_T1A: SDI=1
                    await SendSimulatedPressureWordAsync(sdi: 1, pressureValue: pressureValue, cancellationToken).ConfigureAwait(false);
                    await Task.Delay(40, cancellationToken).ConfigureAwait(false);

                    // Label 174(oct) PRESS_SYS2_T1A: SDI=2
                    await SendSimulatedPressureWordAsync(sdi: 2, pressureValue: pressureValue, cancellationToken).ConfigureAwait(false);
                    await Task.Delay(40, cancellationToken).ConfigureAwait(false);

                    // Label 174(oct) PRESS_SYS3_T1A: SDI=3
                    await SendSimulatedPressureWordAsync(sdi: 3, pressureValue: pressureValue, cancellationToken).ConfigureAwait(false);
                    await Task.Delay(40, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Log($"模拟产品: 发送异常: {ex.Message}"); }
            finally { Log("模拟产品: 持续发送已停止"); }
        }

        /// <summary>
        /// 电压到压力的转换函数
        /// 根据测试点的电压范围和对应的压力范围进行映射
        /// </summary>
        private double VoltageToPressure(double voltage)
        {
            if (Math.Abs(voltage - Point1VoltageV) < 0.1)
                return 42.5; // 0.5V -> 中间值约42.5 psia
            else if (Math.Abs(voltage - Point2VoltageV) < 0.1)
                return 3957.5; // 7.17V -> 中间值约3957.5 psia
            else if (Math.Abs(voltage - Point3VoltageV) < 0.1)
                return 1499.5; // 3.0V -> 中间值约1499.5 psia
            else
                return voltage * 100; // 默认简单映射
        }

        private async Task<double> GetCurrentAoVoltageAsync(CancellationToken cancellationToken)
        {
            if (_mtx532 == null || !_mtx532.IsConnected)
                return 0.0;

            try
            {
                return await _mtx532.GetLastOutputVoltageAsync("AO1", cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                return 0.0;
            }
        }

        /// <summary>
        /// 构造并发送单个模拟压力字
        /// 根据协议图片中的定义：
        /// - Label: 174(oct) 
        /// - SDI: 1/2/3 (对应SYS1/2/3)
        /// - 数据: 12-bit UBNR，位于bit16-27，分辨率1，单位psia
        /// - SSM=3，奇校验
        /// </summary>
        private async Task SendSimulatedPressureWordAsync(byte sdi, double pressureValue, CancellationToken cancellationToken)
        {
            // 压力数据编码：12-bit UBNR，位于bit16-27
            // 在19-bit数据域中，对应bit5-16（因为bit16-27映射到data19的bit5-16）
            uint data19 = (uint)pressureValue & 0xFFFu; // 12-bit数据
            data19 <<= 5; // 移位到bit5-16位置

            // bit10-15和bit28协议规定未使用，必须为0
            data19 &= ~((0x3Fu << 10) | (1u << 18)); // 清零bit10-15和bit28

            // SSM=3（正常数据），奇校验
            var word = _arinc.BuildRawWord(PressLabelDec, sdi: sdi, data19: data19, ssm: SsmNormal, applyOddParity: true);
            await _arinc.SendWordsSingleAsync(TxChannelIndex, new[] { word }, Art4229Parity.Odd, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// 确保ARINC429发送通道已初始化
        /// </summary>
        private async Task EnsureArincTxAsync(CancellationToken cancellationToken)
        {
            if (_arinc == null)
            {
                var device = FindFirstArincDevice();
                if (device == null)
                    throw new InvalidOperationException("未找到ART4227/ART4229(ARINC429)板卡，无法发送429数据");
                _arinc = new Art4229Api(device, deviceIndex: 0);
            }

            if (!_arinc.IsConnected)
            {
                await _arinc.ConnectAsync(cancellationToken).ConfigureAwait(false);
            }

            if (!_txOpened)
            {
                await _arinc.OpenTxAsync(TxChannelIndex, cancellationToken).ConfigureAwait(false);
                await _arinc.ConfigureTxAsync(
                    TxChannelIndex,
                    rate: ArincRate,
                    mode: Art4229TxMode.Single,
                    parity: Art4229Parity.Odd,
                    wordFormat: Art4229WordFormat.Standard429,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                _txOpened = true;
            }
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
                dispatcher.Invoke(() => Logs.Add(line));
                return;
            }

            Logs.Add(line);
        }
    }
}
