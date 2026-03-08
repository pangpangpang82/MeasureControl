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
    public class HC_6_7ViewModel : BindableBase
    {
        private const string PowerSupplyIpAddress = "192.168.1.15";
        private const double InputVoltageV = 28.0;
        private const double InputCurrentA = 1.0;
        private const int RxChannelIndex = 2;
        private const int TxChannelIndex = 1;
        private const double ArincRate = 100000.0;
        private const bool EnableArinc429TxSimulation = true;
        private const int ReceiveTimeoutMs = 4000;
        private const int PostInitDelayMs = 100;
        private const int RelaySettleDelayMs = 100;
        private const byte DiscreteLabelDec = 103;
        private const byte AtpLabelDec = 20;
        private const byte SsmNormal = 0;
        private const string TestItemName = "离散量采集测试";

        private static readonly int[] GroundPins =
        {
            49, 50, 51, 52, 53, 54, 55, 56, 57, 58, 59, 60, 61, 62, 63,
            89, 90, 91, 92, 93, 94, 95, 96, 97, 98, 99, 100
        };

        private static readonly Dictionary<int, int> PinToDoIndex = Enumerable.Range(49, 15)
            .ToDictionary(pin => pin, pin => pin - 48);

        private static readonly IReadOnlyDictionary<int, int> PinToDoIndexExtended = CreatePinToDoMap();

        private static readonly int[] RelayIndicesToEnable = { 0, 1, 2, 3 };

        private static readonly IReadOnlyList<FrameDefinition> ChannelAFrames = new[]
        {
            new FrameDefinition(DiscreteLabelDec, 1, new []
            {
                new BitDefinition(10, 49), new BitDefinition(12, 50), new BitDefinition(14, 53), new BitDefinition(16, 54),
                new BitDefinition(19, 89), new BitDefinition(21, 91), new BitDefinition(23, 93), new BitDefinition(25, 95)
            }),
            new FrameDefinition(DiscreteLabelDec, 2, new []
            {
                new BitDefinition(10, 51), new BitDefinition(12, 52), new BitDefinition(14, 61), new BitDefinition(16, 62),
                new BitDefinition(18, 55), new BitDefinition(20, 56), new BitDefinition(22, 92), new BitDefinition(24, 94), new BitDefinition(26, 96)
            }),
            new FrameDefinition(DiscreteLabelDec, 3, new []
            {
                new BitDefinition(10, 57), new BitDefinition(12, 58), new BitDefinition(14, 59), new BitDefinition(16, 60),
                new BitDefinition(22, 97), new BitDefinition(24, 98)
            }),
            new FrameDefinition(AtpLabelDec, 0, new []
            {
                new BitDefinition(12, 63)
            })
        };

        private static readonly IReadOnlyList<FrameDefinition> ChannelBFrames = new[]
        {
            new FrameDefinition(DiscreteLabelDec, 1, new []
            {
                new BitDefinition(10, 49), new BitDefinition(12, 50), new BitDefinition(14, 53), new BitDefinition(16, 54),
                new BitDefinition(21, 91), new BitDefinition(23, 93), new BitDefinition(25, 95)
            }),
            new FrameDefinition(DiscreteLabelDec, 2, new []
            {
                new BitDefinition(10, 51), new BitDefinition(12, 52), new BitDefinition(14, 61), new BitDefinition(16, 62),
                new BitDefinition(18, 55), new BitDefinition(20, 56), new BitDefinition(20, 89), new BitDefinition(22, 92), new BitDefinition(24, 94), new BitDefinition(26, 96)
            }),
            new FrameDefinition(DiscreteLabelDec, 3, new []
            {
                new BitDefinition(10, 57), new BitDefinition(12, 58), new BitDefinition(14, 59), new BitDefinition(16, 60),
                new BitDefinition(20, 90), new BitDefinition(22, 97), new BitDefinition(24, 98)
            }),
            new FrameDefinition(AtpLabelDec, 0, new []
            {
                new BitDefinition(12, 63)
            })
        };

        private readonly SemaphoreSlim _measureLock = new SemaphoreSlim(1, 1);
        private readonly IPxiChassisService _pxiChassisService;
        private readonly ISingleBoardTestContextService _singleBoardTestContext;
        private readonly Dictionary<int, string> _pinTexts = new Dictionary<int, string>();

        private CancellationTokenSource _manualCts;
        private CancellationTokenSource _autoCts;
        private IPowerSupplyApi _power;
        private IJy7131Api _jy7131;
        private IArt4229Api _arinc;
        private bool _txOpened;
        private bool _canMeasure;
        private bool _measured14;
        private bool _manualAborted;
        private bool _isManualTestRunning;
        private bool _isAutoTestRunning;
        private string _lastTestTime = "--";
        private string _lastTestResult = "--";
        private string _previousTestTime = "--";
        private string _previousTestResult = "--";
        private string _currentTestResult = "--";
        private string _detectedChannelText = "--";

        public HC_6_7ViewModel(IPxiChassisService pxiChassisService, ISingleBoardTestContextService singleBoardTestContext)
        {
            _pxiChassisService = pxiChassisService;
            _singleBoardTestContext = singleBoardTestContext;

            ManualTestCommand = new DelegateCommand(async () => await OnManualTestAsync());
            AutoTestCommand = new DelegateCommand(async () => await OnAutoTestAsync());
            Measure14Command = new DelegateCommand(async () => await OnMeasure14Async(), () => CanMeasure14);
            ClearLogCommand = new DelegateCommand(() => Logs.Clear());

            ResetMeasurementState();
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

        public bool CanMeasure14 => IsManualTestRunning && CanMeasure && !_measured14;

        public string CurrentTestResult
        {
            get => _currentTestResult;
            private set => SetProperty(ref _currentTestResult, value);
        }

        public string LastTestTime
        {
            get => _lastTestTime ?? "--";
            set => SetProperty(ref _lastTestTime, value);
        }

        public string LastTestResult
        {
            get => _lastTestResult ?? "--";
            set => SetProperty(ref _lastTestResult, value);
        }

        public string PreviousTestTime
        {
            get => _previousTestTime ?? "--";
            set => SetProperty(ref _previousTestTime, value);
        }

        public string PreviousTestResult
        {
            get => _previousTestResult ?? "--";
            set => SetProperty(ref _previousTestResult, value);
        }

        public string DetectedChannelText
        {
            get => _detectedChannelText;
            private set => SetProperty(ref _detectedChannelText, value);
        }

        public string Pin49Text => GetPinText(49);
        public string Pin50Text => GetPinText(50);
        public string Pin51Text => GetPinText(51);
        public string Pin52Text => GetPinText(52);
        public string Pin53Text => GetPinText(53);
        public string Pin54Text => GetPinText(54);
        public string Pin55Text => GetPinText(55);
        public string Pin56Text => GetPinText(56);
        public string Pin57Text => GetPinText(57);
        public string Pin58Text => GetPinText(58);
        public string Pin59Text => GetPinText(59);
        public string Pin60Text => GetPinText(60);
        public string Pin61Text => GetPinText(61);
        public string Pin62Text => GetPinText(62);
        public string Pin63Text => GetPinText(63);
        public string Pin89Text => GetPinText(89);
        public string Pin90Text => GetPinText(90);
        public string Pin91Text => GetPinText(91);
        public string Pin92Text => GetPinText(92);
        public string Pin93Text => GetPinText(93);
        public string Pin94Text => GetPinText(94);
        public string Pin95Text => GetPinText(95);
        public string Pin96Text => GetPinText(96);
        public string Pin97Text => GetPinText(97);
        public string Pin98Text => GetPinText(98);
        public string Pin99Text => GetPinText(99);
        public string Pin100Text => GetPinText(100);

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

        private async Task OnManualTestAsync()
        {
            if (IsManualTestRunning)
            {
                await StopManualTestAsync().ConfigureAwait(false);
                return;
            }

            if (IsAutoTestRunning)
                await StopAutoTestAsync().ConfigureAwait(false);

            ResetMeasurementState();
            IsManualTestRunning = true;
            _manualCts?.Cancel();
            _manualCts?.Dispose();
            _manualCts = new CancellationTokenSource();

            Log("开始手动测试");
            Log($"电源: CH1 {InputVoltageV:0.###}V {InputCurrentA:0.###}A, IP={PowerSupplyIpAddress}");
            Log($"JY7131: DO1~27 接地, 485继电器 DO0~3 使能");
            Log($"ARINC429: RX通道{RxChannelIndex + 1}, TX通道{TxChannelIndex + 1}, 码率 {ArincRate:0}bps, 离散Label=147(oct), ATP Label=24(oct), 模拟发送={(EnableArinc429TxSimulation ? "启用" : "禁用")}");

            try
            {
                await EnsurePowerAsync(_manualCts.Token).ConfigureAwait(false);
                await EnsureJy7131Async(_manualCts.Token).ConfigureAwait(false);
                await ApplyGroundingAsync(_manualCts.Token).ConfigureAwait(false);
                await EnsureArincRxAsync(_manualCts.Token).ConfigureAwait(false);
                if (EnableArinc429TxSimulation)
                {
                    await EnsureArincTxAsync(_manualCts.Token).ConfigureAwait(false);
                    _ = SimulateProductContinuousTxAsync(_manualCts.Token);
                    await Task.Delay(PostInitDelayMs, _manualCts.Token).ConfigureAwait(false);
                }

                CanMeasure = true;
                Log("手动测试初始化完成，可执行采集");
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
            ResetMeasurementState();
            IsAutoTestRunning = true;

            Log("开始自动测试");

            try
            {
                await EnsurePowerAsync(cancellationToken).ConfigureAwait(false);
                await EnsureJy7131Async(cancellationToken).ConfigureAwait(false);
                await ApplyGroundingAsync(cancellationToken).ConfigureAwait(false);
                await EnsureArincRxAsync(cancellationToken).ConfigureAwait(false);
                if (EnableArinc429TxSimulation)
                {
                    await EnsureArincTxAsync(cancellationToken).ConfigureAwait(false);
                    _ = SimulateProductContinuousTxAsync(cancellationToken);
                    await Task.Delay(PostInitDelayMs, cancellationToken).ConfigureAwait(false);
                }

                var success = await MeasureDiscreteAsync(cancellationToken).ConfigureAwait(false);
                _measured14 = true;
                await FinalizeAsync(success).ConfigureAwait(false);
                await StopAutoTestAsync().ConfigureAwait(false);
                return LastTestResult;
            }
            catch
            {
                throw;
            }
        }

        private async Task OnMeasure14Async()
        {
            var success = await MeasureDiscreteAsync(_manualCts?.Token ?? CancellationToken.None).ConfigureAwait(false);
            if (!IsManualTestRunning || _manualAborted)
                return;

            _measured14 = true;
            RaisePropertyChanged(nameof(CanMeasure14));
            Measure14Command?.RaiseCanExecuteChanged();
            await FinalizeAsync(success).ConfigureAwait(false);
        }

        private async Task<bool> MeasureDiscreteAsync(CancellationToken cancellationToken)
        {
            if (!(IsAutoTestRunning || (IsManualTestRunning && CanMeasure)))
            {
                Log("当前未处于可测量状态");
                return false;
            }

            await _measureLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ResetPinTextsForMeasurement();
                Log("开始接收离散量采集结果");
                _ = await _arinc.ReadRxWordsAsync(RxChannelIndex, 4096, false, false, cancellationToken).ConfigureAwait(false);

                var aValues = CreateDefaultPinState();
                var bValues = CreateDefaultPinState();
                var seenFrames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var deadline = DateTime.UtcNow.AddMilliseconds(ReceiveTimeoutMs);

                while (!cancellationToken.IsCancellationRequested && DateTime.UtcNow <= deadline)
                {
                    var words = await _arinc.ReadRxWordsAsync(RxChannelIndex, maxCount: 512, enableTimeTag: false, enableRateAdaption: false, cancellationToken: cancellationToken).ConfigureAwait(false);
                    foreach (var word in words)
                    {
                        if (!_arinc.VerifyOddParity(word.Data429))
                            continue;

                        _arinc.ParseRawWord(word.Data429, out var label, out var sdi, out var data19, out var ssm);
                        if (!IsExpectedLabel(label, DiscreteLabelDec) && !IsExpectedLabel(label, AtpLabelDec))
                            continue;

                        if (ssm != SsmNormal)
                            continue;

                        ApplyFrame(ChannelAFrames, label, sdi, data19, aValues, seenFrames, "A");
                        ApplyFrame(ChannelBFrames, label, sdi, data19, bValues, seenFrames, "B");
                    }

                    var detectedMode = ResolveMode(aValues, bValues, seenFrames);
                    if (detectedMode != HscuChannelMode.Unknown && HasRequiredPins(detectedMode == HscuChannelMode.A ? aValues : bValues))
                    {
                        CommitPinState(detectedMode == HscuChannelMode.A ? aValues : bValues, detectedMode);
                        Log($"离散量采集完成，识别通道: {detectedMode}");
                        return true;
                    }

                    await Task.Delay(10, cancellationToken).ConfigureAwait(false);
                }

                var fallbackMode = ResolveMode(aValues, bValues, seenFrames);
                if (fallbackMode != HscuChannelMode.Unknown)
                {
                    CommitPinState(fallbackMode == HscuChannelMode.A ? aValues : bValues, fallbackMode);
                    Log($"离散量采集超时，按当前匹配结果输出: {fallbackMode}");
                    return false;
                }

                DetectedChannelText = "--";
                Log("离散量采集超时，未能识别 A/B 通道");
                return false;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception ex)
            {
                Log($"离散量采集异常: {ex.Message}");
                return false;
            }
            finally
            {
                _measureLock.Release();
            }
        }

        private async Task FinalizeAsync(bool success)
        {
            if (_manualAborted || !_measured14)
                return;

            var resultText = success ? "合格" : "不合格";
            var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            CurrentTestResult = resultText;
            PreviousTestTime = now;
            PreviousTestResult = resultText;
            LastTestTime = now;
            LastTestResult = resultText;
            SaveTestResultToProject();

            Log($"最终结果: {resultText}");
            Log($"识别通道: {DetectedChannelText}");

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
            try { CanMeasure = false; _manualCts?.Cancel(); } catch { }
            Log("手动测试停止/结束，正在释放电源、7131 与 429...");
            await CleanupIoAsync().ConfigureAwait(false);
            IsManualTestRunning = false;
            Log("手动测试已结束");
        }

        private async Task StopAutoTestAsync()
        {
            try { _autoCts?.Cancel(); } catch { }
            Log("自动测试停止/结束，正在释放电源、7131 与 429...");
            await CleanupIoAsync().ConfigureAwait(false);
            IsAutoTestRunning = false;
            Log("自动测试已结束");
        }

        private void LoadLastTestResultFromProject()
        {
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

        private void ResetMeasurementState()
        {
            CurrentTestResult = "--";
            CanMeasure = false;
            _manualAborted = false;
            _measured14 = false;
            DetectedChannelText = "--";
            ResetPinTextsForMeasurement();
            RaisePropertyChanged(nameof(CanMeasure14));
            Measure14Command?.RaiseCanExecuteChanged();
        }

        private void ResetPinTextsForMeasurement()
        {
            foreach (var pin in GroundPins)
            {
                _pinTexts[pin] = (pin == 99 || pin == 100) ? "0" : "--";
                RaisePropertyChanged($"Pin{pin}Text");
            }
        }

        private static IReadOnlyDictionary<int, int> CreatePinToDoMap()
        {
            var result = new Dictionary<int, int>(PinToDoIndex);
            for (int pin = 89; pin <= 100; pin++)
                result[pin] = pin - 73;
            return result;
        }

        private string GetPinText(int pin)
        {
            return _pinTexts.TryGetValue(pin, out var value) ? value : "--";
        }

        private static Dictionary<int, string> CreateDefaultPinState()
        {
            var state = GroundPins.ToDictionary(pin => pin, pin => (pin == 99 || pin == 100) ? "0" : "--");
            return state;
        }

        private void CommitPinState(Dictionary<int, string> values, HscuChannelMode mode)
        {
            foreach (var pair in values)
            {
                _pinTexts[pair.Key] = pair.Value;
                RaisePropertyChanged($"Pin{pair.Key}Text");
            }

            DetectedChannelText = mode.ToString();
        }

        private static bool HasRequiredPins(Dictionary<int, string> values)
        {
            return values[49] != "--" && values[50] != "--" && values[63] != "--";
        }

        private HscuChannelMode ResolveMode(Dictionary<int, string> aValues, Dictionary<int, string> bValues, HashSet<string> seenFrames)
        {
            var aScore = 0;
            var bScore = 0;

            if (aValues[89] != "--") aScore += 2;
            if (bValues[90] != "--") bScore += 2;
            if (bValues[89] != "--") bScore += 1;
            if (aValues[91] != "--") aScore += 1;
            if (bValues[91] != "--") bScore += 1;

            aScore += seenFrames.Count(x => x.StartsWith("A:", StringComparison.OrdinalIgnoreCase));
            bScore += seenFrames.Count(x => x.StartsWith("B:", StringComparison.OrdinalIgnoreCase));

            if (aScore == 0 && bScore == 0)
                return HscuChannelMode.Unknown;

            return aScore >= bScore ? HscuChannelMode.A : HscuChannelMode.B;
        }

        private void ApplyFrame(
            IReadOnlyList<FrameDefinition> frameDefinitions,
            byte label,
            byte sdi,
            uint data19,
            Dictionary<int, string> state,
            HashSet<string> seenFrames,
            string modeTag)
        {
            var definition = frameDefinitions.FirstOrDefault(x => IsExpectedLabel(label, x.Label) && x.Sdi == sdi);
            if (definition == null)
                return;

            seenFrames.Add($"{modeTag}:{definition.Label}:{definition.Sdi}");
            foreach (var bit in definition.Bits)
            {
                var value = ((data19 >> (bit.BitPosition - 10)) & 0x1u) == 1u ? "1" : "0";
                state[bit.Pin] = value;
            }
        }

        private bool IsExpectedLabel(byte label, byte expected)
        {
            return label == expected || (_arinc != null && label == _arinc.ReverseLabel(expected));
        }

        private async Task EnsurePowerAsync(CancellationToken cancellationToken)
        {
            _power ??= new PowerSupplySocketApi();
            await _power.ConnectAsync(PowerSupplyIpAddress, cancellationToken).ConfigureAwait(false);
            await _power.ApplyAsync(PowerSupplyChannel.CH1, InputVoltageV, InputCurrentA, cancellationToken).ConfigureAwait(false);
            await _power.SetOutputEnabledAsync(PowerSupplyChannel.CH1, true, cancellationToken).ConfigureAwait(false);
            await Task.Delay(300, cancellationToken).ConfigureAwait(false);
        }

        private async Task EnsureJy7131Async(CancellationToken cancellationToken)
        {
            if (_jy7131 == null)
            {
                var device = FindFirstJy7131Device();
                if (device == null)
                    throw new InvalidOperationException("未找到PXIe-7131(JY7131)板卡，无法执行接地控制");

                var slot = device is DigitalIODevice dio ? dio.SlotIndex : 0;
                _jy7131 = new Jy7131Api(device, slot);
            }

            if (!_jy7131.IsConnected)
                await _jy7131.ConnectAsync(cancellationToken).ConfigureAwait(false);

            await _jy7131.SetOutputModeAsync(Jy7131OutputMode.PushPull, cancellationToken).ConfigureAwait(false);

            if (!_jy7131.IsRunning)
                await _jy7131.StartAsync(cancellationToken).ConfigureAwait(false);
        }

        private async Task ApplyGroundingAsync(CancellationToken cancellationToken)
        {
            uint mask = 0;
            foreach (var pin in GroundPins)
            {
                if (!PinToDoIndexExtended.TryGetValue(pin, out var doIndex))
                    continue;
                mask |= (1u << doIndex);
            }

            await _jy7131.WriteDoBitmaskAsync(mask, cancellationToken).ConfigureAwait(false);
            foreach (var relayIndex in RelayIndicesToEnable)
                await _jy7131.SetRelayAsync(relayIndex, true, cancellationToken).ConfigureAwait(false);

            await Task.Delay(RelaySettleDelayMs, cancellationToken).ConfigureAwait(false);
            Log("已完成针脚49~63、89~100接地控制");
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

        private async Task EnsureArincTxAsync(CancellationToken cancellationToken)
        {
            if (_arinc == null)
                throw new InvalidOperationException("请先调用 EnsureArincRxAsync 初始化板卡");

            if (!_arinc.IsConnected)
                await _arinc.ConnectAsync(cancellationToken).ConfigureAwait(false);

            if (_txOpened)
                return;

            await _arinc.OpenTxAsync(TxChannelIndex, cancellationToken).ConfigureAwait(false);
            await _arinc.ConfigureTxAsync(TxChannelIndex, ArincRate, Art4229TxMode.Single, Art4229Parity.Odd, Art4229WordFormat.Standard429, cancellationToken).ConfigureAwait(false);
            _txOpened = true;
        }

        private async Task SimulateProductContinuousTxAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    await SendSimulatedFrameAsync(ChannelAFrames[0], cancellationToken).ConfigureAwait(false);
                    await Task.Delay(20, cancellationToken).ConfigureAwait(false);
                    await SendSimulatedFrameAsync(ChannelAFrames[1], cancellationToken).ConfigureAwait(false);
                    await Task.Delay(20, cancellationToken).ConfigureAwait(false);
                    await SendSimulatedFrameAsync(ChannelAFrames[2], cancellationToken).ConfigureAwait(false);
                    await Task.Delay(20, cancellationToken).ConfigureAwait(false);
                    await SendSimulatedFrameAsync(ChannelAFrames[3], cancellationToken).ConfigureAwait(false);
                    await Task.Delay(40, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Log($"模拟产品发送异常: {ex.Message}");
            }
        }

        private async Task SendSimulatedFrameAsync(FrameDefinition definition, CancellationToken cancellationToken)
        {
            uint data19 = 0;
            foreach (var bit in definition.Bits)
            {
                data19 |= (1u << (bit.BitPosition - 10));
            }

            var word = _arinc.BuildRawWord(definition.Label, definition.Sdi, data19, SsmNormal, true);
            await _arinc.SendWordsSingleAsync(TxChannelIndex, new[] { word }, Art4229Parity.Odd, cancellationToken).ConfigureAwait(false);
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
            catch { }
            finally
            {
                _arinc = null;
            }

            try
            {
                if (_jy7131 != null)
                {
                    try { await _jy7131.SetAllRelaysAsync(false, CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _jy7131.ResetAllDoAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _jy7131.StopAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _jy7131.DisconnectAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _jy7131.DisposeAsync().ConfigureAwait(false); } catch { }
                }
            }
            catch { }
            finally
            {
                _jy7131 = null;
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
            catch { }
            finally
            {
                _power = null;
            }
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
                    (d?.Name?.IndexOf("7131", StringComparison.OrdinalIgnoreCase) >= 0));

                if (device != null)
                    return device;
            }

            return null;
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

        private enum HscuChannelMode
        {
            Unknown,
            A,
            B
        }

        private sealed class FrameDefinition
        {
            public FrameDefinition(byte label, byte sdi, IReadOnlyList<BitDefinition> bits)
            {
                Label = label;
                Sdi = sdi;
                Bits = bits;
            }

            public byte Label { get; }
            public byte Sdi { get; }
            public IReadOnlyList<BitDefinition> Bits { get; }
        }

        private sealed class BitDefinition
        {
            public BitDefinition(int bitPosition, int pin)
            {
                BitPosition = bitPosition;
                Pin = pin;
            }

            public int BitPosition { get; }
            public int Pin { get; }
        }
    }
}
