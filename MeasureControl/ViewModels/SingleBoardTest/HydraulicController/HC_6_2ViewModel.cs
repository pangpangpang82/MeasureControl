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
using MeasureControl.Services;
using MeasureControl.Services.HardwareApis;

namespace MeasureControl.ViewModels.SingleBoardTest.HydraulicController
{
    /// <summary>
    /// HC_6_2 测试项：电源电压测试（通过 ARINC429 读取）
    /// 测试目的：验证液压控制器的 5V、15V、-15V 电源输出是否正常
    /// 测试方法：供电 28V 后，通过 ARINC429 接收电压数据，并判断是否在允许范围内
    /// </summary>
    public class HC_6_2ViewModel : BindableBase
    {
        // 电源配置
        private const string PowerSupplyIpAddress = "192.168.1.15";  // 程控电源 IP 地址
        private const double InputVoltageV = 28.0;                    // 输入电压 28V
        private const double InputCurrentA = 1;                     // 输入限流 0.1A

        private const bool EnableArinc429TxSimulation = true;

        // ARINC429 配置
        private const int RxChannelIndex = 2;           // 接收通道索引
        private const int TxChannelIndex = 1;           // 发送通道索引（第2通道，0-based）
        private const double ArincRate = 12500.0;       // 通信速率 12.5kbps

        // ARINC429 标签（Label）定义
        private const byte Label5V = 050;      // 5V 电压数据标签
        private const byte Label15V = 048;     // 15V 电压数据标签
        private const byte LabelM15V = 049;    // -15V 电压数据标签

        // 采样配置
        private const int SamplesPerMeasure = 5;      // 每次测量采集 5 个样本取平均值
        private const int SampleTimeoutMs = 3000;     // 采样超时时间 3 秒

        // 电压合格范围（允许偏差 ±1.5%）
        private const double Min5V = 4.925;      // 5V 下限（4.925V）
        private const double Max5V = 5.075;      // 5V 上限（5.075V）
        private const double Min15V = 14.775;    // 15V 下限（14.775V）
        private const double Max15V = 15.225;    // 15V 上限（15.225V）
        private const double MinM15V = -15.225;  // -15V 下限（-15.225V）
        private const double MaxM15V = -14.775;  // -15V 上限（-14.775V）

        private readonly SemaphoreSlim _measureLock = new SemaphoreSlim(1, 1);
        private CancellationTokenSource _manualCts;
        private CancellationTokenSource _autoCts;

        private readonly IPxiChassisService _pxiChassisService;
        private readonly ISingleBoardTestContextService _singleBoardTestContext;
        private IPowerSupplyApi _power;
        private IArt4229Api _arinc;
        private bool _txOpened;

        private const string TestItemName = "二次电源测试";

        private bool _canMeasure;
        private bool _measured5v;
        private bool _measured15v;
        private bool _measuredM15v;
        private bool _manualAborted;

        private bool _isManualTestRunning;
        private bool _isAutoTestRunning;

        private string _lastTestTime = "--";
        private string _lastTestResult = "--";
        private string _previousTestTime = "--";
        private string _previousTestResult = "--";
        private string _currentTestResult = "--";

        private string _voltage5VText = "--";
        private string _voltage15VText = "--";
        private string _voltageM15VText = "--";

        private double? _voltage5V;
        private double? _voltage15V;
        private double? _voltageM15V;

        public HC_6_2ViewModel(IPxiChassisService pxiChassisService, ISingleBoardTestContextService singleBoardTestContext)
        {
            _pxiChassisService = pxiChassisService;
            _singleBoardTestContext = singleBoardTestContext;

            ManualTestCommand = new DelegateCommand(async () => await OnManualTestAsync());
            AutoTestCommand = new DelegateCommand(async () => await OnAutoTestAsync());

            Measure5VCommand = new DelegateCommand(async () => await OnMeasure5VAsync(), () => CanMeasure5V);
            Measure15VCommand = new DelegateCommand(async () => await OnMeasure15VAsync(), () => CanMeasure15V);
            MeasureM15VCommand = new DelegateCommand(async () => await OnMeasureM15VAsync(), () => CanMeasureM15V);
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

        public DelegateCommand Measure5VCommand { get; }
        public DelegateCommand Measure15VCommand { get; }
        public DelegateCommand MeasureM15VCommand { get; }

        public DelegateCommand ClearLogCommand { get; }

        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();

        public bool IsManualTestRunning
        {
            get => _isManualTestRunning;
            set
            {
                if (SetProperty(ref _isManualTestRunning, value))
                {
                    RaisePropertyChanged(nameof(CanMeasure5V));
                    RaisePropertyChanged(nameof(CanMeasure15V));
                    RaisePropertyChanged(nameof(CanMeasureM15V));
                    Measure5VCommand?.RaiseCanExecuteChanged();
                    Measure15VCommand?.RaiseCanExecuteChanged();
                    MeasureM15VCommand?.RaiseCanExecuteChanged();
                }
            }
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

        private async Task<string> ExecuteAutoTestAsync(CancellationToken cancellationToken)
        {
            IsAutoTestRunning = true;
            CurrentTestResult = "--";
            _manualAborted = false;
            CanMeasure = false;

            _measured5v = false;
            _measured15v = false;
            _measuredM15v = false;

            _voltage5V = null;
            _voltage15V = null;
            _voltageM15V = null;
            Voltage5VText = "--";
            Voltage15VText = "--";
            VoltageM15VText = "--";

            Log("开始自动测试");
            Log($"判据: 5V[{Min5V:0.###},{Max5V:0.###}]  15V[{Min15V:0.###},{Max15V:0.###}]  -15V[{MinM15V:0.###},{MaxM15V:0.###}]");

            try
            {
                await EnsurePowerAsync(cancellationToken).ConfigureAwait(false);
                await EnsureArincRxAsync(cancellationToken).ConfigureAwait(false);

                await MeasureVoltageFrom429Async(
                        title: "5V",
                        expectedLabel: Label5V,
                        decode: Decode5V,
                        setText: t => Voltage5VText = t,
                        setValue: v => _voltage5V = v,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                await Task.Delay(120, cancellationToken).ConfigureAwait(false);

                await MeasureVoltageFrom429Async(
                        title: "15V",
                        expectedLabel: Label15V,
                        decode: Decode15V,
                        setText: t => Voltage15VText = t,
                        setValue: v => _voltage15V = v,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                await Task.Delay(120, cancellationToken).ConfigureAwait(false);

                await MeasureVoltageFrom429Async(
                        title: "-15V",
                        expectedLabel: LabelM15V,
                        decode: DecodeM15V,
                        setText: t => VoltageM15VText = t,
                        setValue: v => _voltageM15V = v,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                _measured5v = _voltage5V != null;
                _measured15v = _voltage15V != null;
                _measuredM15v = _voltageM15V != null;

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

        public bool IsAutoTestRunning
        {
            get => _isAutoTestRunning;
            set => SetProperty(ref _isAutoTestRunning, value);
        }

        public bool CanMeasure
        {
            get => _canMeasure;
            private set
            {
                if (SetProperty(ref _canMeasure, value))
                {
                    RaisePropertyChanged(nameof(CanMeasure5V));
                    RaisePropertyChanged(nameof(CanMeasure15V));
                    RaisePropertyChanged(nameof(CanMeasureM15V));
                    Measure5VCommand?.RaiseCanExecuteChanged();
                    Measure15VCommand?.RaiseCanExecuteChanged();
                    MeasureM15VCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        public bool CanMeasure5V => IsManualTestRunning && CanMeasure && !_measured5v;
        public bool CanMeasure15V => IsManualTestRunning && CanMeasure && !_measured15v;
        public bool CanMeasureM15V => IsManualTestRunning && CanMeasure && !_measuredM15v;

        public string Voltage5VText
        {
            get => _voltage5VText;
            private set => SetProperty(ref _voltage5VText, value);
        }

        public string Voltage15VText
        {
            get => _voltage15VText;
            private set => SetProperty(ref _voltage15VText, value);
        }

        public string VoltageM15VText
        {
            get => _voltageM15VText;
            private set => SetProperty(ref _voltageM15VText, value);
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

        /// <summary>
        /// 手动测试流程
        /// 用户可以手动点击按钮分别测量 5V、15V、-15V 三个电压
        /// </summary>
        private async Task OnManualTestAsync()
        {
            if (IsManualTestRunning)
            {
                await StopManualTestAsync().ConfigureAwait(false);
                return;
            }

            IsAutoTestRunning = false;
            IsManualTestRunning = true;
            CurrentTestResult = "--";

            CanMeasure = false;
            _manualAborted = false;
            _measured5v = false;
            _measured15v = false;
            _measuredM15v = false;

            _voltage5V = null;
            _voltage15V = null;
            _voltageM15V = null;
            Voltage5VText = "--";
            Voltage15VText = "--";
            VoltageM15VText = "--";

            _manualCts?.Cancel();
            _manualCts?.Dispose();
            _manualCts = new CancellationTokenSource();

            Log("开始手动测试");
            Log($"电源配置: CH1 {InputVoltageV:0.###}V {InputCurrentA:0.###}A, IP={PowerSupplyIpAddress}");
            Log($"ARINC429接收: 通道{RxChannelIndex + 1}, 码率 {ArincRate:0}bps");

            try
            {
                // 确保电源已开启并输出 28V
                await EnsurePowerAsync(_manualCts.Token).ConfigureAwait(false);
                // 确保 ARINC429 接收通道已启动
                await EnsureArincRxAsync(_manualCts.Token).ConfigureAwait(false);
                CanMeasure = true;
                Log("手动测试初始化完成，可开始分别测量 5V/15V/-15V");
            }
            catch (Exception ex)
            {
                await AbortManualTestAsync($"手动测试初始化失败，中止: {ex.Message}").ConfigureAwait(false);
            }
        }

        /// <summary>
        /// 自动测试流程
        /// 自动依次测量 5V、15V、-15V 三个电压并判断结果
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

            _autoCts?.Cancel();
            _autoCts?.Dispose();
            _autoCts = new CancellationTokenSource();

            _manualAborted = false;
            CanMeasure = false;

            _measured5v = false;
            _measured15v = false;
            _measuredM15v = false;

            _voltage5V = null;
            _voltage15V = null;
            _voltageM15V = null;
            Voltage5VText = "--";
            Voltage15VText = "--";
            VoltageM15VText = "--";

            Log("开始自动测试");
            Log($"判据: 5V[{Min5V:0.###},{Max5V:0.###}]  15V[{Min15V:0.###},{Max15V:0.###}]  -15V[{MinM15V:0.###},{MaxM15V:0.###}]");

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
        }

        private async Task OnMeasure5VAsync()
        {
            var ok = await MeasureVoltageFrom429Async(
                title: "5V",
                expectedLabel: Label5V,
                decode: Decode5V,
                setText: t => Voltage5VText = t,
                setValue: v => _voltage5V = v,
                cancellationToken: _manualCts?.Token ?? CancellationToken.None).ConfigureAwait(false);

            if (!IsManualTestRunning || _manualAborted)
                return;

            if (ok)
            {
                _measured5v = true;
                RaisePropertyChanged(nameof(CanMeasure5V));
                Measure5VCommand?.RaiseCanExecuteChanged();
            }

            await TryFinalizeIfAllMeasuredAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 测量 15V 电压（手动模式）
        /// </summary>
        private async Task OnMeasure15VAsync()
        {
            var ok = await MeasureVoltageFrom429Async(
                title: "15V",
                expectedLabel: Label15V,
                decode: Decode15V,
                setText: t => Voltage15VText = t,
                setValue: v => _voltage15V = v,
                cancellationToken: _manualCts?.Token ?? CancellationToken.None).ConfigureAwait(false);

            if (!IsManualTestRunning || _manualAborted)
                return;

            if (ok)
            {
                _measured15v = true;
                RaisePropertyChanged(nameof(CanMeasure15V));
                Measure15VCommand?.RaiseCanExecuteChanged();
            }

            await TryFinalizeIfAllMeasuredAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 测量 -15V 电压（手动模式）
        /// </summary>
        private async Task OnMeasureM15VAsync()
        {
            var ok = await MeasureVoltageFrom429Async(
                title: "-15V",
                expectedLabel: LabelM15V,
                decode: DecodeM15V,
                setText: t => VoltageM15VText = t,
                setValue: v => _voltageM15V = v,
                cancellationToken: _manualCts?.Token ?? CancellationToken.None).ConfigureAwait(false);

            if (!IsManualTestRunning || _manualAborted)
                return;

            if (ok)
            {
                _measuredM15v = true;
                RaisePropertyChanged(nameof(CanMeasureM15V));
                MeasureM15VCommand?.RaiseCanExecuteChanged();
            }

            await TryFinalizeIfAllMeasuredAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 从 ARINC429 数据中测量电压（核心测量方法）
        /// 流程：1) 接收 ARINC429 数据  2) 过滤指定 Label 的数据  3) 验证奇偶校验  4) 解码电压值  5) 采集 5 个样本取平均
        /// </summary>
        /// <param name="title">电压名称（如 "5V", "15V", "-15V"）</param>
        /// <param name="expectedLabel">期望的 ARINC429 标签</param>
        /// <param name="decode">解码函数（将 19-bit 数据转换为电压值）</param>
        /// <param name="setText">设置界面显示文本的回调</param>
        /// <param name="setValue">设置测量值的回调</param>
        /// <returns>true=测量成功，false=测量失败</returns>
        private async Task<bool> MeasureVoltageFrom429Async(
            string title,
            byte expectedLabel,
            Func<uint, double?> decode,
            Action<string> setText,
            Action<double?> setValue,
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
                Log($"{title}: 开始接收429数据，label={expectedLabel}");

                if (EnableArinc429TxSimulation)
                {
                    try
                    {
                        await EnsureArincTxAsync(cancellationToken).ConfigureAwait(false);
                        _ = SimulateVoltageTxAsync(title, expectedLabel, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        Log($"{title}: 模拟发送初始化失败(忽略): {ex.Message}");
                    }
                }

                // 准备采样容器和超时时间
                var samples = new List<double>(SamplesPerMeasure);
                var deadline = DateTime.UtcNow.AddMilliseconds(SampleTimeoutMs);

                // 循环接收 ARINC429 数据直到采集足够样本或超时
                while (!cancellationToken.IsCancellationRequested && DateTime.UtcNow <= deadline)
                {
                    // 读取 ARINC429 接收缓冲区的数据
                    var words = await _arinc.ReadRxWordsAsync(RxChannelIndex, maxCount: 512, enableTimeTag: false, enableRateAdaption: false, cancellationToken: cancellationToken)
                        .ConfigureAwait(false);

                    if (words.Count > 0)
                    {
                        foreach (var w in words)
                        {
                            // 过滤：只处理指定 Label 的数据
                            if (!IsExpectedLabel(w.Data429, expectedLabel))
                                continue;

                            // 验证奇偶校验位
                            if (!_arinc.VerifyOddParity(w.Data429))
                                continue;

                            // 解析 ARINC429 数据字：提取 SDI 和 19-bit 数据域
                            _arinc.ParseRawWord(w.Data429, out _, out var sdi, out var data19, out _);
                            if (sdi != 0)
                                continue;

                            // 数据格式验证：bit10-19 固定为0（协议规定）
                            if ((data19 & 0x3FFu) != 0)
                                continue;

                            // 数据格式验证：5V/15V 的 bit28 固定为0（-15V 的 bit28 为符号位，不限制）
                            if (!string.Equals(title, "-15V", StringComparison.OrdinalIgnoreCase))
                            {
                                // bit28 对应 data19 的 bit18
                                if ((data19 & (1u << 18)) != 0)
                                    continue;
                            }

                            // 解码电压值
                            var v = decode(data19);
                            if (v == null)
                                continue;

                            // 添加到采样列表
                            samples.Add(v.Value);

                            // 计算平均值并更新界面显示
                            var avg = samples.Average();
                            setText($"{v.Value:0.###} V ({samples.Count}/{SamplesPerMeasure})  平均:{avg:0.###} V");

                            // 如果已采集足够样本，完成测量
                            if (samples.Count >= SamplesPerMeasure)
                            {
                                setValue(avg);
                                setText($"{avg:0.###} V");
                                Log($"{title}: 完成，平均值={avg:0.###}V");
                                return true;
                            }
                        }
                    }

                    await Task.Delay(10, cancellationToken).ConfigureAwait(false);
                }

                setText("--");
                setValue(null);

                var timeoutMsg = $"{title}: 测量超时(3秒内未接收到{SamplesPerMeasure}帧有效数据)";
                Log(timeoutMsg);
                if (IsManualTestRunning)
                {
                    var dispatcher = Application.Current?.Dispatcher;
                    if (dispatcher != null && !dispatcher.CheckAccess())
                    {
                        dispatcher.Invoke(() => MessageBox.Show(timeoutMsg, "提示", MessageBoxButton.OK, MessageBoxImage.Warning));
                    }
                    else
                    {
                        MessageBox.Show(timeoutMsg, "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }

                return false;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception ex)
            {
                setText("--");
                setValue(null);
                Log($"{title}: 采集异常: {ex.Message}");
                return false;
            }
            finally
            {
                _measureLock.Release();
            }
        }

        private async Task EnsureArincTxAsync(CancellationToken cancellationToken)
        {
            if (_arinc == null)
            {
                await EnsureArincRxAsync(cancellationToken).ConfigureAwait(false);
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

        private async Task SimulateVoltageTxAsync(string title, byte expectedLabel, CancellationToken cancellationToken)
        {
            double value;
            if (string.Equals(title, "5V", StringComparison.OrdinalIgnoreCase))
                value = 5.0;
            else if (string.Equals(title, "15V", StringComparison.OrdinalIgnoreCase))
                value = 15.0;
            else if (string.Equals(title, "-15V", StringComparison.OrdinalIgnoreCase))
                value = -15.0;
            else
                return;

            for (var i = 0; i < SamplesPerMeasure; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                uint data19;
                if (string.Equals(title, "-15V", StringComparison.OrdinalIgnoreCase))
                {
                    data19 = _arinc.EncodeBnr(value, bitLength: 9, resolution: 0.1, msbPosition: 28);
                }
                else
                {
                    data19 = _arinc.EncodeUbnr(value, bitLength: 8, resolution: 0.1, msbPosition: 27);
                }

                data19 &= ~0x3FFu;
                if (!string.Equals(title, "-15V", StringComparison.OrdinalIgnoreCase))
                {
                    data19 &= ~(1u << 18);
                }
                var word = _arinc.BuildRawWord(expectedLabel, sdi: 0, data19: data19, ssm: 0, applyOddParity: true);
                await _arinc.SendWordsSingleAsync(TxChannelIndex, new[] { word }, Art4229Parity.Odd, cancellationToken).ConfigureAwait(false);
                await Task.Delay(20, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// 解码 5V 电压值（UBNR 格式：无符号二进制数）
        /// </summary>
        /// <param name="data19">ARINC429 数据域（19-bit）</param>
        /// <returns>解码后的电压值</returns>
        private double? Decode5V(uint data19)
        {
            return _arinc.DecodeUbnr(data19, bitLength: 8, resolution: 0.1, msbPosition: 27);
        }

        /// <summary>
        /// 解码 15V 电压值（UBNR 格式：无符号二进制数）
        /// </summary>
        /// <param name="data19">ARINC429 数据域（19-bit）</param>
        /// <returns>解码后的电压值</returns>
        private double? Decode15V(uint data19)
        {
            return _arinc.DecodeUbnr(data19, bitLength: 8, resolution: 0.1, msbPosition: 27);
        }

        /// <summary>
        /// 解码 -15V 电压值（BNR 格式：有符号二进制数，支持负值）
        /// </summary>
        /// <param name="data19">ARINC429 数据域（19-bit）</param>
        /// <returns>解码后的电压值</returns>
        private double? DecodeM15V(uint data19)
        {
            return _arinc.DecodeBnr(data19, bitLength: 9, resolution: 0.1, msbPosition: 28);
        }

        /// <summary>
        /// 判断 ARINC429 数据字的 Label 是否匹配期望值
        /// </summary>
        /// <param name="rawWord">原始 ARINC429 数据字</param>
        /// <param name="expected">期望的 Label 值</param>
        /// <returns>true=匹配，false=不匹配</returns>
        private bool IsExpectedLabel(uint rawWord, byte expected)
        {
            _arinc.ParseRawWord(rawWord, out var label, out _, out _, out _);
            return label == expected || label == _arinc.ReverseLabel(expected);
        }

        /// <summary>
        /// 检查是否所有电压都已测量完成，如果是则判断结果并结束测试
        /// </summary>
        private async Task TryFinalizeIfAllMeasuredAsync()
        {
            if (_manualAborted)
            {
                return;
            }

            if (!(_measured5v && _measured15v && _measuredM15v))
                return;

            // 判断每个电压是否在合格范围内
            var pass5 = _voltage5V != null && _voltage5V >= Min5V && _voltage5V <= Max5V;
            var pass15 = _voltage15V != null && _voltage15V >= Min15V && _voltage15V <= Max15V;
            var passM15 = _voltageM15V != null && _voltageM15V >= MinM15V && _voltageM15V <= MaxM15V;

            var pass = pass5 && pass15 && passM15;

            var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            var resultText = pass ? "合格" : "不合格";
            CurrentTestResult = resultText;
            PreviousTestTime = now;
            PreviousTestResult = resultText;
            LastTestTime = now;
            LastTestResult = resultText;

            SaveTestResultToProject();
            Log($"最终结果: {resultText}");
            Log($"判据: 5V[{Min5V:0.###},{Max5V:0.###}] => {FormatBool(pass5)}");
            Log($"判据: 15V[{Min15V:0.###},{Max15V:0.###}] => {FormatBool(pass15)}");
            Log($"判据: -15V[{MinM15V:0.###},{MaxM15V:0.###}] => {FormatBool(passM15)}");

            await StopManualTestAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 中止手动测试（发生错误时调用）
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
        /// 停止手动测试并清理资源
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

        private async Task EnsurePowerAsync(CancellationToken cancellationToken)
        {
            _power ??= new PowerSupplySocketApi();
            await _power.ConnectAsync(PowerSupplyIpAddress, cancellationToken).ConfigureAwait(false);
            await _power.ApplyAsync(PowerSupplyChannel.CH1, InputVoltageV, InputCurrentA, cancellationToken).ConfigureAwait(false);
            await _power.SetOutputEnabledAsync(PowerSupplyChannel.CH1, true, cancellationToken).ConfigureAwait(false);
            await Task.Delay(300, cancellationToken).ConfigureAwait(false);
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
                {
                    return device;
                }
            }

            return null;
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

        private static string FormatBool(bool value) => value ? "合格" : "不合格";
    }
}
