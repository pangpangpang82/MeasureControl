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
    public class HC_6_7ViewModel : BindableBase
    {
        private const string PowerSupplyIpAddress = "192.168.1.15";
        private const double InputVoltageV = 28.0;
        private const double InputCurrentA = 1.0;
        private const string Mtx532AoChannel = "AO13";
        private const int RxChannelIndex = 2;
        private const int TxChannelIndex = 0;
        private const double ArincRate = 100000.0;
        private const int ReceiveTimeoutMs = 4000;
        private const int AtpRequestPeriodMs = 100;
        private const int RelaySettleDelayMs = 100;
        private const int PowerSettleDelayMs = 300;
        private const int Mtx532ReadyTimeoutMs = 6000;
        private const int Mtx532ReadyPollMs = 200;
        private const double Mtx532Ao0VoltageV = 4.0;
        private const byte DiscreteLabelDec = 103;
        private const byte AtpLabelDec = 16; // 十进制
        private const byte AtpStatusLabelDec = 20;
        private const byte AtpStatusLabelDec2 = 102;
        private const byte PbitLabelDec = 20;
        private const byte SsmNormal = 0;
        private const bool UsePeriodicAtpRequest = true;
        private const string TestItemName = "离散量采集测试";

        private static readonly int[] GroundPins =
        {
            49, 50, 51, 52, 53, 54, 55, 56, 57, 58, 59, 60, 61, 62, 63,
            89, 90, 91, 92, 93, 94, 95, 96, 97, 98, 99, 100
        };

        private static readonly int[] ReportPins =
        {
            49, 50, 51, 52, 53, 54, 55, 56, 57, 58, 59, 60, 61, 62, 63,
            89, 90, 91, 92, 93, 94, 95, 96, 97, 98, 99, 100
        };

        private static readonly int[] RelayIndicesToEnable = { 0, 1, 2, 3, 4, 5, 6 };

        private static readonly IReadOnlyList<FrameDefinition> SignalFrames = new[]
        {
            new FrameDefinition(DiscreteLabelDec, 1, new []
            {
                new BitDefinition(10, 49), new BitDefinition(12, 50), new BitDefinition(14, 53), new BitDefinition(16, 54),
                new BitDefinition(22, 91), new BitDefinition(24, 93), new BitDefinition(26, 95)
            }),
            new FrameDefinition(DiscreteLabelDec, 2, new []
            {
                new BitDefinition(10, 51), new BitDefinition(12, 52), new BitDefinition(14, 61), new BitDefinition(16, 62),
                new BitDefinition(18, 55), new BitDefinition(20, 56), new BitDefinition(23, 89), new BitDefinition(25, 92), new BitDefinition(27, 94)
            }),
            new FrameDefinition(DiscreteLabelDec, 3, new []
            {
                new BitDefinition(10, 57), new BitDefinition(12, 58), new BitDefinition(14, 59), new BitDefinition(16, 60),
                new BitDefinition(20, 90), new BitDefinition(22, 97), new BitDefinition(24, 98)
            }),
            new FrameDefinition(AtpStatusLabelDec, 0, new []
            {
                new BitDefinition(12, 63)
            }),
            new FrameDefinition(AtpStatusLabelDec2, 2, new []
            {
                new BitDefinition(25, 96)
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
        private IMtx532Api _mtx532;
        private bool _isRelay485On;
        private bool _txOpened;
        private CancellationTokenSource _atpRequestLoopCts;
        private bool _canMeasure;
        private bool _measured14;
        private bool _manualAborted;
        private bool _isManualTestRunning;
        private bool _isAutoTestRunning;
        private bool _isManualTestInitializing;
        private bool _isAutoTestInitializing;
        private bool _isManualTestStopping;
        private bool _isAutoTestStopping;
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
                    RaisePropertyChanged(nameof(CanMeasure14));
                    RaisePropertyChanged(nameof(CanStartManualTest));
                    RaisePropertyChanged(nameof(CanStartAutoTest));
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
                    RaisePropertyChanged(nameof(CanStartManualTest));
                    RaisePropertyChanged(nameof(CanStartAutoTest));
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

        public bool IsPin49Pass => IsPinPass(49);
        public bool IsPin50Pass => IsPinPass(50);
        public bool IsPin51Pass => IsPinPass(51);
        public bool IsPin52Pass => IsPinPass(52);
        public bool IsPin53Pass => IsPinPass(53);
        public bool IsPin54Pass => IsPinPass(54);
        public bool IsPin55Pass => IsPinPass(55);
        public bool IsPin56Pass => IsPinPass(56);
        public bool IsPin57Pass => IsPinPass(57);
        public bool IsPin58Pass => IsPinPass(58);
        public bool IsPin59Pass => IsPinPass(59);
        public bool IsPin60Pass => IsPinPass(60);
        public bool IsPin61Pass => IsPinPass(61);
        public bool IsPin62Pass => IsPinPass(62);
        public bool IsPin63Pass => IsPinPass(63);
        public bool IsPin89Pass => IsPinPass(89);
        public bool IsPin90Pass => IsPinPass(90);
        public bool IsPin91Pass => IsPinPass(91);
        public bool IsPin92Pass => IsPinPass(92);
        public bool IsPin93Pass => IsPinPass(93);
        public bool IsPin94Pass => IsPinPass(94);
        public bool IsPin95Pass => IsPinPass(95);
        public bool IsPin96Pass => IsPinPass(96);
        public bool IsPin97Pass => IsPinPass(97);
        public bool IsPin98Pass => IsPinPass(98);
        public bool IsPin99Pass => IsPinPass(99);
        public bool IsPin100Pass => IsPinPass(100);

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
                IsAutoTestInitializing = false;
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
            IsManualTestInitializing = true;
            IsManualTestStopping = false;
            _manualCts?.Cancel();
            _manualCts?.Dispose();
            _manualCts = new CancellationTokenSource();

            Log("开始手动测试");
            Log($"电源: CH1 {InputVoltageV:0.###}V {InputCurrentA:0.###}A, IP={PowerSupplyIpAddress}");
            Log("JY7131: 485继电器前7路闭合，DO1~25=1，DO26=1，DO27=1");
            Log($"MT532: AO0 输出 {Mtx532Ao0VoltageV:0.###}V 直流");
            Log($"ARINC429: RX通道{RxChannelIndex + 1}, TX通道{TxChannelIndex + 1}, 码率 {ArincRate:0}bps, 离散Label=147(oct), ATP请求Label=24(oct), ATP状态Label=30(oct)");

            try
            {
                await EnsureJy7131Async(_manualCts.Token).ConfigureAwait(false);
                await EnsureRelay485Async(true, _manualCts.Token).ConfigureAwait(false);
                await ApplyGroundingAsync(_manualCts.Token).ConfigureAwait(false);
                await EnsureMtx532Async(_manualCts.Token).ConfigureAwait(false);
                await EnsureArincRxAsync(_manualCts.Token).ConfigureAwait(false);
                await StartAtpRequestAsync(_manualCts.Token).ConfigureAwait(false);
                await EnsurePowerAsync(_manualCts.Token).ConfigureAwait(false);

                IsManualTestInitializing = false;
                IsManualTestRunning = true;
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

            ResetMeasurementState();
            IsAutoTestInitializing = true;
            IsAutoTestStopping = false;

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
            ResetMeasurementState();

            Log("开始自动测试");

            try
            {
                await EnsureJy7131Async(cancellationToken).ConfigureAwait(false);
                await EnsureRelay485Async(true, cancellationToken).ConfigureAwait(false);
                await ApplyGroundingAsync(cancellationToken).ConfigureAwait(false);
                await EnsureMtx532Async(cancellationToken).ConfigureAwait(false);
                await EnsureArincRxAsync(cancellationToken).ConfigureAwait(false);
                await StartAtpRequestAsync(cancellationToken).ConfigureAwait(false);
                await EnsurePowerAsync(cancellationToken).ConfigureAwait(false);

                IsAutoTestInitializing = false;
                IsAutoTestRunning = true;

                var success = await MeasureDiscreteAsync(cancellationToken).ConfigureAwait(false);
                if (!IsAutoTestRunning)
                    return CurrentTestResult ?? "--";
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

                var values = CreateDefaultPinState();
                var seenFrames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var deadline = DateTime.UtcNow.AddMilliseconds(ReceiveTimeoutMs);

                while (!cancellationToken.IsCancellationRequested && DateTime.UtcNow <= deadline)
                {
                    var words = await _arinc.ReadRxWordsAsync(RxChannelIndex, maxCount: 512, enableTimeTag: false, enableRateAdaption: false, cancellationToken: cancellationToken).ConfigureAwait(false);
                    foreach (var word in words)
                    {
                        //if (!_arinc.VerifyOddParity(word.Data429))
                        //    continue;

                        _arinc.ParseRawWord(word.Data429, out var label, out var sdi, out var data19, out var ssm);
                        if (!IsExpectedLabel(label, DiscreteLabelDec)
                            && !IsExpectedLabel(label, AtpStatusLabelDec)
                            && !IsExpectedLabel(label, AtpStatusLabelDec2)
                            && !IsExpectedLabel(label, PbitLabelDec))
                            continue;

                        if (IsExpectedLabel(label, PbitLabelDec))
                        {
                            Log($"{Convert.ToString(word.Data429, 2)}");
                        }

                        if (ssm != SsmNormal)
                            continue;

                        ApplyFrame(SignalFrames, label, sdi, data19, values, seenFrames);
                        ApplyPbitSpecialLogic(label, sdi, data19, values);
                    }

                    CommitPinState(values);
                    
                    if (HasAllRequiredFrames(seenFrames))
                    {
                        Log($"离散量采集完成，已收到 {seenFrames.Count} 帧有效信号");
                        return true;
                    }

                    await Task.Delay(10, cancellationToken).ConfigureAwait(false);
                }

                if (seenFrames.Count > 0)
                {
                    ApplyTimeoutState(values);
                    CommitPinState(values);
                    Log("离散量采集超时，按当前收到的信号输出");
                    return false;
                }

                ApplyTimeoutState(values);
                CommitPinState(values);
                DetectedChannelText = "超时";
                Log("离散量采集超时，未收到有效429信号");
                return false;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception ex)
            {
                if (IsManualTestRunning)
                {
                    await AbortManualTestAsync($"离散量采集异常，手动测试中止: {ex.Message}").ConfigureAwait(false);
                }
                else if (IsAutoTestRunning)
                {
                    await AbortAutoTestAsync($"离散量采集异常，自动测试中止: {ex.Message}").ConfigureAwait(false);
                }
                else
                {
                    Log($"离散量采集异常: {ex.Message}");
                }
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

            success = success && AreAllPinsPassing();
            var resultText = success ? "合格" : "不合格";
            var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            CurrentTestResult = resultText;
            PreviousTestTime = now;
            PreviousTestResult = resultText;
            LastTestTime = now;
            LastTestResult = resultText;
            SaveTestResultToProject();

            Log($"最终结果: {resultText}");
            Log($"接收状态: {DetectedChannelText}");

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

        private async Task AbortAutoTestAsync(string reason)
        {
            if (!string.IsNullOrWhiteSpace(reason))
                Log(reason);

            await StopAutoTestAsync().ConfigureAwait(false);
        }

        private async Task StopManualTestAsync()
        {
            IsManualTestStopping = true;
            IsManualTestInitializing = false;
            try { CanMeasure = false; _manualCts?.Cancel(); } catch { }
            Log("手动测试停止/结束，正在释放电源、MT532、7131 与 429...");
            try
            {
                await CleanupIoAsync().ConfigureAwait(false);
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
            IsAutoTestStopping = true;
            IsAutoTestInitializing = false;
            try { _autoCts?.Cancel(); } catch { }
            Log("自动测试停止/结束，正在释放电源、MT532、7131 与 429...");
            try
            {
                await CleanupIoAsync().ConfigureAwait(false);
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
                _pinTexts[pin] = "--";
                RaisePropertyChanged($"Pin{pin}Text");
            }
        }

        private string GetPinText(int pin)
        {
            return _pinTexts.TryGetValue(pin, out var value) ? value : "--";
        }

        private static Dictionary<int, string> CreateDefaultPinState()
        {
            var state = GroundPins.ToDictionary(pin => pin, pin => "--");
            return state;
        }

        private void CommitPinState(Dictionary<int, string> values)
        {
            foreach (var pair in values)
            {
                _pinTexts[pair.Key] = pair.Value;
                RaisePropertyChanged($"Pin{pair.Key}Text");
                RaisePropertyChanged($"IsPin{pair.Key}Pass");
            }

            DetectedChannelText = "已接收";
        }

        private bool AreAllPinsPassing()
        {
            return ReportPins.All(IsPinPass);
        }

        private bool IsPinPass(int pin)
        {
            return string.Equals(GetPinText(pin), "1", StringComparison.OrdinalIgnoreCase);
        }

        private void ApplyPbitSpecialLogic(byte label, byte sdi, uint data19, Dictionary<int, string> values)
        {
            if (!IsExpectedLabel(label, PbitLabelDec) || sdi != 0)
                return;

            var bit12 = ((data19 >> 2) & 0x1u) == 1u;
            var pinValue = bit12 ? "1" : "0";
            
            values[99] = pinValue;
            values[100] = pinValue;
        }

        private static bool HasAllRequiredFrames(HashSet<string> seenFrames)
        {
            return seenFrames.Contains($"{DiscreteLabelDec}:1")
                && seenFrames.Contains($"{DiscreteLabelDec}:2")
                && seenFrames.Contains($"{DiscreteLabelDec}:3")
                && seenFrames.Contains($"{AtpStatusLabelDec}:0")
                && seenFrames.Contains($"{AtpStatusLabelDec2}:2")
                && seenFrames.Contains($"{PbitLabelDec}:0");
        }

        private static void ApplyTimeoutState(Dictionary<int, string> values)
        {
            foreach (var pin in GroundPins)
            {
                if (values[pin] == "--")
                    values[pin] = "超时";
            }
        }

        private void ApplyFrame(
            IReadOnlyList<FrameDefinition> frameDefinitions,
            byte label,
            byte sdi,
            uint data19,
            Dictionary<int, string> state,
            HashSet<string> seenFrames)
        {
            var definition = frameDefinitions.FirstOrDefault(x => IsExpectedLabel(label, x.Label) && x.Sdi == sdi);
            if (definition == null)
                return;

            seenFrames.Add($"{definition.Label}:{definition.Sdi}");
            foreach (var bit in definition.Bits)
            {
                var value = ((data19 >> (bit.BitPosition - 10)) & 0x1u) == 1u ? "1" : "0";
                state[bit.Pin] = value;
            }
        }

        private bool IsExpectedLabel(byte label, byte expected)
        {
            return _arinc != null && _arinc.ReverseLabel(label) == expected;
        }

        private async Task EnsurePowerAsync(CancellationToken cancellationToken)
        {
            _power ??= new PowerSupplySocketApi();
            await _power.ConnectAsync(PowerSupplyIpAddress, cancellationToken).ConfigureAwait(false);
            await _power.ApplyAsync(PowerSupplyChannel.CH1, InputVoltageV, InputCurrentA, cancellationToken).ConfigureAwait(false);
            await _power.SetOutputEnabledAsync(PowerSupplyChannel.CH1, true, cancellationToken).ConfigureAwait(false);
            await Task.Delay(PowerSettleDelayMs, cancellationToken).ConfigureAwait(false);
        }

        private async Task EnsureMtx532Async(CancellationToken cancellationToken)
        {
            if (_mtx532 == null || !_mtx532.IsConnected)
            {
                var device = FindFirstMtx532Device();
                if (device == null)
                    throw new InvalidOperationException("未找到MTX532(模拟量输出)板卡");

                var slot = device is PxiDeviceBase pxi ? pxi.SlotIndex : 7;
                _mtx532 = new Mtx532Api(device, options: new Mtx532Options { SampleRateHz = 20000.0 }, slotNumber: slot);

                await _mtx532.ConnectAsync(cancellationToken, new[] { Mtx532AoChannel }).ConfigureAwait(false);
                await SetAo0Async(0.0, cancellationToken).ConfigureAwait(false);
                await Task.Delay(300, cancellationToken).ConfigureAwait(false);
                await WaitForMtx532ReadyAsync(cancellationToken).ConfigureAwait(false);
                await _mtx532.StartOutputAsync(cancellationToken).ConfigureAwait(false);
                await Task.Delay(300, cancellationToken).ConfigureAwait(false);
            }

            await SetAo0Async(Mtx532Ao0VoltageV, cancellationToken).ConfigureAwait(false);
            Log($"MT532 已连接，{Mtx532AoChannel}={Mtx532Ao0VoltageV:0.###}V");
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

        private async Task SetAo0Async(double voltageV, CancellationToken cancellationToken)
        {
            if (_mtx532 == null || !_mtx532.IsConnected)
                throw new InvalidOperationException("MTX532未连接");

            await _mtx532.WriteOnceDcAsync(new Dictionary<string, double>
            {
                [Mtx532AoChannel] = voltageV
            }, cancellationToken).ConfigureAwait(false);
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

            await _jy7131.SetOutputModeAsync(Jy7131OutputMode.Sinking, cancellationToken).ConfigureAwait(false);

            if (!_jy7131.IsRunning)
            {
                await _jy7131.SetOutputModeAsync(Jy7131OutputMode.Sinking, cancellationToken).ConfigureAwait(false);
                await _jy7131.StartAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task ApplyGroundingAsync(CancellationToken cancellationToken)
        {
            uint mask = 0;
            for (var doIndex = 0; doIndex <= 26; doIndex++) {
                if (doIndex == 15 || doIndex == 24)
                {
                    continue;
                }

                mask |= (1u << doIndex); }

            await _jy7131.WriteDoBitmaskAsync(mask, cancellationToken).ConfigureAwait(false);

            await Task.Delay(RelaySettleDelayMs, cancellationToken).ConfigureAwait(false);
            Log("已完成7131输出配置: DO0~26=1");
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
            Log("ARINC429接收通道已连接并启动");
        }

        private async Task EnsureArincTxAsync(CancellationToken cancellationToken)
        {
            if (_arinc == null)
                throw new InvalidOperationException("请先初始化ARINC429板卡");

            if (!_arinc.IsConnected)
                await _arinc.ConnectAsync(cancellationToken).ConfigureAwait(false);

            if (_txOpened)
                return;

            await _arinc.OpenTxAsync(TxChannelIndex, cancellationToken).ConfigureAwait(false);
            await _arinc.ConfigureTxAsync(TxChannelIndex, ArincRate, Art4229TxMode.Single, Art4229Parity.Odd, Art4229WordFormat.Standard429, cancellationToken).ConfigureAwait(false);
            _txOpened = true;
            Log("ARINC429发送通道已连接并启动");
        }

        private async Task StartAtpRequestAsync(CancellationToken cancellationToken)
        {
            await EnsureArincTxAsync(cancellationToken).ConfigureAwait(false);

            if (UsePeriodicAtpRequest)
            {
                _atpRequestLoopCts?.Cancel();
                _atpRequestLoopCts?.Dispose();
                _atpRequestLoopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

                uint data19 = 0x1u;
                var label = GetAtpLabelForTx();
                var word = _arinc.BuildRawWord(label, 0, data19, SsmNormal, true);
                Log($"ATP：True,raw=0x{word:X8}");
                await _arinc.SendWordsPeriodAsync(TxChannelIndex, new[] { word }, AtpRequestPeriodMs, 0, Art4229Parity.Odd, _atpRequestLoopCts.Token).ConfigureAwait(false);
                Log("已启动ATP请求周期发送(100ms)");
                return;
            }

            await SendAtpRequestSingleAsync(true, cancellationToken).ConfigureAwait(false);
            Log("已发送ATP单次请求");
        }

        private async Task StopAtpRequestAsync(bool sendRelease, CancellationToken cancellationToken)
        {
            try { _atpRequestLoopCts?.Cancel(); } catch { }

            if (_arinc != null && _txOpened)
            {
                try { await _arinc.StopTxAsync(TxChannelIndex, cancellationToken).ConfigureAwait(false); } catch { }
            }

            if (sendRelease && _arinc != null)
            {
                try
                {
                    await EnsureArincTxAsync(cancellationToken).ConfigureAwait(false);
                    await SendAtpRequestSingleAsync(false, cancellationToken).ConfigureAwait(false);
                    Log("已发送ATP退出请求");
                }
                catch (Exception ex)
                {
                    Log($"发送ATP退出请求失败: {ex.Message}");
                }
            }

            try { _atpRequestLoopCts?.Dispose(); } catch { }
            _atpRequestLoopCts = null;
        }

        private async Task SendAtpRequestSingleAsync(bool requestAtp, CancellationToken cancellationToken)
        {
            await EnsureArincTxAsync(cancellationToken).ConfigureAwait(false);

            uint data19 = requestAtp ? 0x1u : 0x0u;
            var label = GetAtpLabelForTx();
            var word = _arinc.BuildRawWord(label, 0, data19, SsmNormal, true);
            Log($"ATP：{requestAtp},raw=0x{word:X8}");
            await _arinc.SendWordsSingleAsync(TxChannelIndex, new[] { word }, Art4229Parity.Odd, cancellationToken).ConfigureAwait(false);
        }

        private byte GetAtpLabelForTx()
        {
            if (_arinc == null)
                return AtpLabelDec;

            var label = _arinc.ReverseLabel(AtpLabelDec);
            // var label = AtpLabelDec;
            return label;
        }

        private async Task EnsureRelay485Async(bool on, CancellationToken cancellationToken)
        {
            if (on)
            {
                if (_isRelay485On)
                    return;

                foreach (var relayIndex in RelayIndicesToEnable)
                    await _jy7131.SetRelayAsync(relayIndex, true, cancellationToken).ConfigureAwait(false);

                await Task.Delay(RelaySettleDelayMs, cancellationToken).ConfigureAwait(false);
                _isRelay485On = true;
                Log("485继电器前7路已闭合");
                return;
            }

            if (!_isRelay485On || _jy7131 == null)
                return;

            for (var i = RelayIndicesToEnable.Length - 1; i >= 0; i--)
                await _jy7131.SetRelayAsync(RelayIndicesToEnable[i], false, cancellationToken).ConfigureAwait(false);

            _isRelay485On = false;
            Log("485继电器前7路已断开");
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
            finally
            {
                _power = null;
            }

            try
            {
                await StopAtpRequestAsync(true, CancellationToken.None).ConfigureAwait(false);
            }
            catch { }

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
                if (_mtx532 != null)
                {
                    try { await _mtx532.StopOutputAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _mtx532.ResetAllToZeroAsync(disableAfterReset: true, cancellationToken: CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _mtx532.DisconnectAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _mtx532.DisposeAsync().ConfigureAwait(false); } catch { }
                }
            }
            catch { }
            finally
            {
                _mtx532 = null;
            }

            try
            {
                if (_jy7131 != null)
                {
                    try { await _jy7131.ResetAllDoAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await EnsureRelay485Async(false, CancellationToken.None).ConfigureAwait(false); } catch { }
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
