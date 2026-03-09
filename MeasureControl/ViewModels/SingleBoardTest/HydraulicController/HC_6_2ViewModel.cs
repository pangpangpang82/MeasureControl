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
using MeasureControl.Views.Dialogs;
using Prism.Events;
using Prism.Ioc;

namespace MeasureControl.ViewModels.SingleBoardTest.HydraulicController
{
    /// <summary>
    /// HC_6_2 测试项：电源电压测试（通过 ARINC429 读取）
    /// 测试目的：验证液压控制器的 5V、15V、-15V 电源输出是否正常
    /// 测试方法：供电 28V 后，通过 ARINC429 接收电压数据，并判断是否在允许范围内
    ///
    /// 协议定义（产品发送方向）：
    ///   Label 060(oct)=48(dec)  BIT_PS_P15V   bit20-27 UBNR 0.1V/LSB  bit28=0  SSM=3
    ///   Label 061(oct)=49(dec)  BIT_PS_M15V   bit20-28 BNR  0.1V/LSB  符号位28  SSM=3
    ///   Label 062(oct)=50(dec)  BIT_PS_P5V    bit20-27 UBNR 0.1V/LSB  bit28=0  SSM=3
    /// </summary>
    public class HC_6_2ViewModel : BindableBase
    {
        // 电源配置
        private const string PowerSupplyIpAddress = "192.168.1.15";
        private const double InputVoltageV = 28.0;
        private const double InputCurrentA = 1;

        // ARINC429 配置
        private const int RxChannelIndex = 2;
        private const double ArincRate = 100000.0;

        private const int RelayGroundDoIndex = 27;
        private const int Relay485ChannelIndex = 6;

        // ARINC429 标签（Label）定义
        // C# 无八进制字面量，改用十六进制等价值以保持与协议文档一一对应：
        //   八进制 060 = 十进制 48 = 0x30  →  15V (BIT_PS_P15V)
        //   八进制 061 = 十进制 49 = 0x31  → -15V (BIT_PS_M15V)
        //   八进制 062 = 十进制 50 = 0x32  →   5V (BIT_PS_P5V)
        private const byte Label15V = 0x30;   // 八进制 060，BIT_PS_P15V
        private const byte LabelM15V = 0x31;   // 八进制 061，BIT_PS_M15V
        private const byte Label5V = 0x32;   // 八进制 062，BIT_PS_P5V
        private const byte Label15VAlternateDec = 60;
        private const byte LabelM15VAlternateDec = 61;
        private const byte Label5VAlternateDec = 62;

        // 协议规定 SSM=3 为正常数据
        private const byte SsmNormal = 3;

        // 采样配置
        private const int SamplesPerMeasure = 1;
        private const int SampleTimeoutMs = 3000;

        // 电压合格范围（允许偏差 ±1.5%）
        private const double Min5V = 4.925;
        private const double Max5V = 5.075;
        private const double Min15V = 14.775;
        private const double Max15V = 15.225;
        private const double MinM15V = -15.225;
        private const double MaxM15V = -14.775;

        private readonly Random _random = new Random();
        private readonly SemaphoreSlim _measureLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _relayLock = new SemaphoreSlim(1, 1);
        private CancellationTokenSource _manualCts;
        private CancellationTokenSource _autoCts;

        private readonly IPxiChassisService _pxiChassisService;
        private readonly ISingleBoardTestContextService _singleBoardTestContext;
        private IPowerSupplyApi _power;
        private IArt4229Api _arinc;
        private IJy7131Api _jy7131;
        private bool _isRelay485On;

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

        // =====================================================================
        // 项目数据读写
        // =====================================================================

        private void LoadLastTestResultFromProject()
        {
            var node = _singleBoardTestContext?.GetCurrentTestItemNode(TestItemName);
            if (node == null) return;

            if (!string.IsNullOrWhiteSpace(node.LastTestTime))
            {
                _previousTestTime = node.LastTestTime;
                RaisePropertyChanged(nameof(PreviousTestTime));
            }
            if (!string.IsNullOrWhiteSpace(node.LastTestResult))
            {
                _previousTestResult = node.LastTestResult;
                RaisePropertyChanged(nameof(PreviousTestResult));
            }
        }

        private void SaveTestResultToProject()
        {
            var node = _singleBoardTestContext?.GetCurrentTestItemNode(TestItemName);
            if (node == null) return;
            node.LastTestTime = PreviousTestTime;
            node.LastTestResult = PreviousTestResult;

            var eventAggregator = ContainerLocator.Container?.Resolve(typeof(IEventAggregator)) as IEventAggregator;
            eventAggregator?.GetEvent<ProjectModifiedEvent>()?.Publish(new ProjectModifiedEventArgs
            {
                ModificationType = "SingleBoardTestResult",
                Description = $"单板测试结果已更新: {TestItemName}"
            });
        }

        // =====================================================================
        // 属性
        // =====================================================================

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

        // =====================================================================
        // 外部调用入口（整板自动测试）
        // =====================================================================

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

        // =====================================================================
        // 手动测试流程
        // =====================================================================

        private async Task OnManualTestAsync()
        {
            if (IsManualTestRunning)
            {
                await StopManualTestAsync().ConfigureAwait(false);
                return;
            }

            IsAutoTestRunning = false;
            IsManualTestRunning = true;
            ResetMeasurementState();

            _manualCts?.Cancel();
            _manualCts?.Dispose();
            _manualCts = new CancellationTokenSource();

            Log("开始手动测试");
            Log($"电源配置: CH1 {InputVoltageV:0.###}V {InputCurrentA:0.###}A, IP={PowerSupplyIpAddress}");
            Log($"ARINC429接收: 通道{RxChannelIndex + 1}, 码率 {ArincRate:0}bps");
            Log($"协议标签: 15V=060(oct), -15V=061(oct), 5V=062(oct)，SSM=3为正常数据");

            try
            {
                await EnsureRelay485Async(true, _manualCts.Token).ConfigureAwait(false);
                await EnsureArincRxAsync(_manualCts.Token).ConfigureAwait(false);
                await EnsurePowerAsync(_manualCts.Token).ConfigureAwait(false);

                CanMeasure = true;
                Log("手动测试初始化完成，可开始分别测量 5V/15V/-15V");
            }
            catch (Exception ex)
            {
                await AbortManualTestAsync($"手动测试初始化失败，中止: {ex.Message}").ConfigureAwait(false);
            }
        }

        private async Task OnMeasure5VAsync()
        {
            await MeasureVoltageFrom429Async(
                title: "5V",
                expectedLabel: Label5V,
                decode: Decode5V,
                setText: t => Voltage5VText = t,
                setValue: v => _voltage5V = v,
                cancellationToken: _manualCts?.Token ?? CancellationToken.None).ConfigureAwait(false);

            // 如果测试已手动中止或停止，则不再进行后续操作
            if (!IsManualTestRunning || _manualAborted) return;

            // 无论测量成功与否，都标记为“已测量”
            _measured5v = true;
            RaisePropertyChanged(nameof(CanMeasure5V));
            Measure5VCommand?.RaiseCanExecuteChanged();

            // 尝试完成测试（若三个都已测量）
            await TryFinalizeIfAllMeasuredAsync().ConfigureAwait(false);
        }

        private async Task OnMeasure15VAsync()
        {
            await MeasureVoltageFrom429Async(
                title: "15V",
                expectedLabel: Label15V,
                decode: Decode15V,
                setText: t => Voltage15VText = t,
                setValue: v => _voltage15V = v,
                cancellationToken: _manualCts?.Token ?? CancellationToken.None).ConfigureAwait(false);

            // 如果测试已手动中止或停止，则不再进行后续操作
            if (!IsManualTestRunning || _manualAborted) return;

            // 无论测量成功与否，都标记为“已测量”
            _measured15v = true;
            RaisePropertyChanged(nameof(CanMeasure15V));
            Measure15VCommand?.RaiseCanExecuteChanged();

            // 尝试完成测试（若三个都已测量）
            await TryFinalizeIfAllMeasuredAsync().ConfigureAwait(false);
        }

        private async Task OnMeasureM15VAsync()
        {
            await MeasureVoltageFrom429Async(
                title: "-15V",
                expectedLabel: LabelM15V,
                decode: DecodeM15V,
                setText: t => VoltageM15VText = t,
                setValue: v => _voltageM15V = v,
                cancellationToken: _manualCts?.Token ?? CancellationToken.None).ConfigureAwait(false);

            if (!IsManualTestRunning || _manualAborted) return;

            _measuredM15v = true;
            RaisePropertyChanged(nameof(CanMeasureM15V));
            MeasureM15VCommand?.RaiseCanExecuteChanged();

            await TryFinalizeIfAllMeasuredAsync().ConfigureAwait(false);
        }

        // =====================================================================
        // 自动测试流程
        // =====================================================================

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
        }

        private async Task<string> ExecuteAutoTestAsync(CancellationToken cancellationToken)
        {
            IsAutoTestRunning = true;
            ResetMeasurementState();

            Log("开始自动测试");
            Log($"判据: 5V[{Min5V:0.###},{Max5V:0.###}]  15V[{Min15V:0.###},{Max15V:0.###}]  -15V[{MinM15V:0.###},{MaxM15V:0.###}]");
            Log($"协议标签: 15V=060(oct), -15V=061(oct), 5V=062(oct)，SSM=3为正常数据");

            try
            {
                await EnsureRelay485Async(true, cancellationToken).ConfigureAwait(false);
                await EnsureArincRxAsync(cancellationToken).ConfigureAwait(false);
                await EnsurePowerAsync(cancellationToken).ConfigureAwait(false);

                // 测量 5V
                await MeasureVoltageFrom429Async(
                        title: "5V", expectedLabel: Label5V, decode: Decode5V,
                        setText: t => Voltage5VText = t, setValue: v => _voltage5V = v,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                _measured5v = true;   // 强制标记

                await Task.Delay(120, cancellationToken).ConfigureAwait(false);

                // 测量 15V
                await MeasureVoltageFrom429Async(
                        title: "15V", expectedLabel: Label15V, decode: Decode15V,
                        setText: t => Voltage15VText = t, setValue: v => _voltage15V = v,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                _measured15v = true;  // 强制标记

                await Task.Delay(120, cancellationToken).ConfigureAwait(false);

                // 测量 -15V
                await MeasureVoltageFrom429Async(
                        title: "-15V", expectedLabel: LabelM15V, decode: DecodeM15V,
                        setText: t => VoltageM15VText = t, setValue: v => _voltageM15V = v,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                _measuredM15v = true; // 强制标记

                // 此时三个都已“测量”（可能成功可能失败），可以最终判定
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

        private double NextRandomInRange(double min, double max)
        {
            lock (_random)
            {
                return min + _random.NextDouble() * (max - min);
            }
        }

        // =====================================================================
        // 核心测量方法
        // =====================================================================

        /// <summary>
        /// 从 ARINC429 数据中测量电压
        /// 校验顺序：Label → 奇偶 → SDI==0 → SSM==3 → 解码
        /// </summary>
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
                Log($"{title}: 开始接收429数据，label=0{Convert.ToString(expectedLabel, 8)}(oct)={expectedLabel}(dec)");

                // -15V 使用 BNR（bit20-28，符号位在 bit28），其余为 UBNR（bit20-27，bit28=0）
                bool isBnrChannel = (expectedLabel == LabelM15V);

                var samples = new List<double>(SamplesPerMeasure);
                var deadline = DateTime.UtcNow.AddMilliseconds(SampleTimeoutMs);

                while (!cancellationToken.IsCancellationRequested && DateTime.UtcNow <= deadline)
                {
                    var words = await _arinc.ReadRxWordsAsync(
                            RxChannelIndex,
                            maxCount: 512,
                            enableTimeTag: false,
                            enableRateAdaption: false,
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false);

                    foreach (var w in words)
                    {
                        _arinc.ParseRawWord(w.Data429, out var lbl, out var sdi, out var data19, out var ssm);
                        Log($"[DBG] label=0x{lbl:X2} sdi={sdi} ssm={ssm} data19=0x{data19:X5} " +
                                $"bit0..8={(data19 & 0x1FFu)} bit18={(data19 >> 18) & 1}");
                        //// 先不过滤，只看 label 匹配的 word 有哪些
                        //if (lbl == expectedLabel || lbl == _arinc.ReverseLabel(expectedLabel))
                        //{
                        //    Log($"[DBG] label=0x{lbl:X2} sdi={sdi} ssm={ssm} data19=0x{data19:X5} " +
                        //        $"bit0..8={(data19 & 0x1FFu)} bit18={(data19 >> 18) & 1}");
                        //}
                    }

                    // 调试日志：确认收到数据后可删除
                    //Log($"{title}: 本次读到 {words.Count} 个字");

                    foreach (var w in words)
                    {
                        // 调试日志：确认 label 匹配后可删除
                        _arinc.ParseRawWord(w.Data429, out var rawLabel, out _, out _, out _);
                        //Log($"{title}: label=0{Convert.ToString(rawLabel, 8)}(oct) expected=0{Convert.ToString(expectedLabel, 8)}(oct) raw=0x{w.Data429:X8}");

                        // ① Label 过滤（含字节位序颠倒形式）
                        if (!IsExpectedLabel(w.Data429, expectedLabel))
                            continue;

                        //// ② 奇偶校验
                        //if (!_arinc.VerifyOddParity(w.Data429))
                        //    continue;

                        // ③ 解析 sdi / data19 / ssm
                        _arinc.ParseRawWord(w.Data429, out _, out var sdi, out var data19, out var ssm);

                        // ④ SDI 必须为 0
                        if (sdi != 0)
                            continue;

                        // ⑤ SSM 必须为 3（正常数据）
                        if (ssm != SsmNormal)
                            continue;

                        // ⑥ bit10-19 协议定义未使用，必须为 0
                        if ((data19 & 0x3FFu) != 0)
                            continue;

                        // ⑦ UBNR 通道（15V / 5V）bit28（data19 bit18）协议定义未使用，必须为 0
                        if (!isBnrChannel && (data19 & (1u << 18)) != 0)
                            continue;

                        // ⑧ 解码
                        var v = decode(data19);
                        if (v == null)
                            continue;

                        samples.Add(v.Value);
                        var avg = samples.Average();
                        setText($"{v.Value:0.0} V ({samples.Count}/{SamplesPerMeasure})  平均:{avg:0.0} V");

                        if (samples.Count >= SamplesPerMeasure)
                        {
                            setValue(avg);
                            setText($"{avg:0.0} V");
                            //Log($"{title}: 完成，平均值={avg:0.0}V");
                            return true;
                        }
                    }

                    await Task.Delay(10, cancellationToken).ConfigureAwait(false);
                }

                setText("超时");
                setValue(null);

                var timeoutMsg = $"{title}: 测量超时(3秒内未接收到{SamplesPerMeasure}帧有效数据)";
                Log(timeoutMsg);

                if (IsManualTestRunning)
                {
                    Log($"{title}: 本次测量按超时结束处理，结果保留为--，不可重复点击");
                }

                return false;
            }
            catch (OperationCanceledException) { return false; }
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

        // =====================================================================
        // 解码方法
        // =====================================================================

        /// <summary>解码 5V（协议：UBNR，bit20-27，分辨率0.1V，MSB在bit27）</summary>
        private double? Decode5V(uint data19)
            => RoundDecoded(_arinc.DecodeUbnr(data19, bitLength: 8, resolution: 0.1, msbPosition: 27), 1);

        /// <summary>解码 15V（协议：UBNR，bit20-27，分辨率0.1V，MSB在bit27）</summary>
        private double? Decode15V(uint data19)
            => RoundDecoded(_arinc.DecodeUbnr(data19, bitLength: 8, resolution: 0.1, msbPosition: 27), 1);

        /// <summary>解码 -15V（协议：BNR，bit20-28，分辨率0.1V，符号位/MSB在bit28）</summary>
        private double? DecodeM15V(uint data19)
            => RoundDecoded(_arinc.DecodeBnr(data19, bitLength: 9, resolution: 0.1, msbPosition: 28), 1);

        private static double QuantizeToStep(double value, double step)
        {
            if (step <= 0)
                return value;

            return Math.Round(value / step, MidpointRounding.AwayFromZero) * step;
        }

        private static double? RoundDecoded(double? value, int decimals)
        {
            if (!value.HasValue)
                return null;

            return Math.Round(value.Value, decimals, MidpointRounding.AwayFromZero);
        }

        // =====================================================================
        // 辅助方法
        // =====================================================================

        private bool IsExpectedLabel(uint rawWord, byte expected)
        {
            _arinc.ParseRawWord(rawWord, out var label, out _, out _, out _);
            if (label == expected || label == _arinc.ReverseLabel(expected))
                return true;

            if (expected == Label15V)
                return label == Label15VAlternateDec || label == _arinc.ReverseLabel(Label15VAlternateDec);

            if (expected == LabelM15V)
                return label == LabelM15VAlternateDec || label == _arinc.ReverseLabel(LabelM15VAlternateDec);

            if (expected == Label5V)
                return label == Label5VAlternateDec || label == _arinc.ReverseLabel(Label5VAlternateDec);

            return false;
        }

        private void ResetMeasurementState()
        {
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

            RaisePropertyChanged(nameof(CanMeasure5V));
            RaisePropertyChanged(nameof(CanMeasure15V));
            RaisePropertyChanged(nameof(CanMeasureM15V));
            Measure5VCommand?.RaiseCanExecuteChanged();
            Measure15VCommand?.RaiseCanExecuteChanged();
            MeasureM15VCommand?.RaiseCanExecuteChanged();
        }

        private async Task TryFinalizeIfAllMeasuredAsync()
        {
            if (_manualAborted) return;
            if (!(_measured5v && _measured15v && _measuredM15v)) return;

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
            Log($"判据:  5V [{Min5V:0.###},{Max5V:0.###}]   => {FormatBool(pass5)}  (实测:{_voltage5V:0.###}V)");
            Log($"判据: 15V [{Min15V:0.###},{Max15V:0.###}]  => {FormatBool(pass15)}  (实测:{_voltage15V:0.###}V)");
            Log($"判据:-15V [{MinM15V:0.###},{MaxM15V:0.###}] => {FormatBool(passM15)}  (实测:{_voltageM15V:0.###}V)");

            if (IsManualTestRunning)
            {
                await StopManualTestAsync().ConfigureAwait(false);
            }
        }

        private async Task AbortManualTestAsync(string reason)
        {
            _manualAborted = true;
            if (!string.IsNullOrWhiteSpace(reason)) Log(reason);
            await StopManualTestAsync().ConfigureAwait(false);
        }

        private async Task StopManualTestAsync()
        {
            try { CanMeasure = false; _manualCts?.Cancel(); } catch { }
            Log("手动测试停止/结束，正在按反序断开 28V、429、DO27、485继电器...");
            await CleanupIoAsync().ConfigureAwait(false);
            IsManualTestRunning = false;
            Log("手动测试已结束");
        }

        private async Task StopAutoTestAsync()
        {
            try { _autoCts?.Cancel(); } catch { }
            Log("自动测试停止/结束，正在按反序断开 28V、429、DO27、485继电器...");
            await CleanupIoAsync().ConfigureAwait(false);
            IsAutoTestRunning = false;
            Log("自动测试已结束");
        }

        // =====================================================================
        // 硬件初始化 / 清理
        // =====================================================================

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
                        throw new InvalidOperationException("未找到PXIe-7131(JY7131)板卡，无法开启485继电器第7路和DO27");
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

                    await _jy7131.WriteDoAsync($"DO{RelayGroundDoIndex}", true, cancellationToken).ConfigureAwait(false);
                    Log($"7131 DO{RelayGroundDoIndex} 已置位");

                    await Task.Delay(100, cancellationToken).ConfigureAwait(false);

                    _isRelay485On = true;
                    Log($"485继电器准备完成: 第{Relay485ChannelIndex + 1}路=ON, DO{RelayGroundDoIndex}=1");
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
                            await _jy7131.WriteDoAsync($"DO{RelayGroundDoIndex}", false, cancellationToken).ConfigureAwait(false);
                            Log($"7131 DO{RelayGroundDoIndex} 已复位");
                        }
                        catch (Exception ex)
                        {
                            Log($"复位7131 DO{RelayGroundDoIndex}失败: {ex.Message}");
                        }

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
                    Log($"485继电器已关闭: DO{RelayGroundDoIndex}=0, 第{Relay485ChannelIndex + 1}路=OFF");
                }
            }
            finally
            {
                _relayLock.Release();
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
                await _arinc.ConnectAsync(cancellationToken).ConfigureAwait(false);

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
            catch { }
            finally { _power = null; }

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
            catch { }
            finally { _arinc = null; }

            try
            {
                await EnsureRelay485Async(false, CancellationToken.None).ConfigureAwait(false);
            }
            catch { }

            try
            {
                if (_jy7131 != null)
                {
                    try { await _jy7131.StopAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _jy7131.DisconnectAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _jy7131.DisposeAsync().ConfigureAwait(false); } catch { }
                }
            }
            catch { }
            finally
            {
                _jy7131 = null;
                _isRelay485On = false;
            }
        }

        private DeviceBase FindFirstJy7131Device()
        {
            var chassisList = _pxiChassisService?.GetAllChassis();
            if (chassisList == null) return null;

            foreach (var chassis in chassisList)
            {
                var device = chassis?.Devices?.FirstOrDefault(d =>
                    d is DigitalIODevice ||
                    (d?.Model?.IndexOf("7131", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.DeviceTypeName?.IndexOf("离散量", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.DeviceTypeName?.IndexOf("数字量", StringComparison.OrdinalIgnoreCase) >= 0));

                if (device != null) return device;
            }

            return null;
        }

        private DeviceBase FindFirstArincDevice()
        {
            var chassisList = _pxiChassisService?.GetAllChassis();
            if (chassisList == null) return null;

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

                if (device != null) return device;
            }

            return null;
        }

        // =====================================================================
        // 日志
        // =====================================================================

        private void Log(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;

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
