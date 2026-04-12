using Prism.Commands;
using Prism.Ioc;
using Prism.Mvvm;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MeasureControl.Models.Devices;
using MeasureControl.Models.Devices.DeviceCategories;
using MeasureControl.Services;
using MeasureControl.Services.HardwareApis;
using System.Windows;

namespace MeasureControl.ViewModels.SingleBoardTest.InertController
{
    public sealed class OxygenSensorSignalAcquisitionTestViewModel : BindableBase, IDisposable
    {
        private const string PowerSupplyIpAddress = "192.168.1.15";
        private const double PowerCh1VoltageV = 28.0;
        private const double PowerCh2VoltageV = 24.0;
        private const double PowerCurrentLimitA = 3.0;

        public bool SkipMainPowerOff { get; set; }

        private static readonly string[] AllPowerSupplyIpAddresses =
        {
            "192.168.1.15",
            "192.168.1.16",
            "192.168.1.17",
        };

        private const string ConcentrationAoChannel = "AO1";
        private const string PressureAoChannel = "AO9";

        private const double MaxCurrentMa = 27.0;
        private const double MaxVoltageV = 10.0;

        private const int MeasureTimeoutMs = 1200;
        private const int SamplesPerMeasure = 5;
        private const int AoSettleDelayMs = 80;

        private const int ArincRxChannelIndex = 0;
        private const int ArincTxChannelIndex = 1;
        private const double ArincRate = 100000.0;
        private const int ArincPollIntervalMs = 10;
        private const byte ArincExpectedSdi = 1;

        private const uint AtpSsmDataSdi = 0xC10001u;
        private const byte AtpLabelOctal174Dec = 124;

        // 注意：协议中的Label按“八进制”理解，接收端拿到的labelRaw为bit反转后的值。
        // 例如：160(oct) -> 112(dec) -> 0b0111_0000 -> bit反转 0b0000_1110 -> 14(dec)
        private const byte OxygenConcentrationCurrentLabelRxDec = 14;  // 160(oct)
        private const byte OxygenConcentrationValueLabelRxDec = 142;   // 161(oct)
        private const byte OxygenPressureCurrentLabelRxDec = 246;      // 157(oct)
        private const byte OxygenPressureValueLabelRxDec = 78;         // 162(oct)

        private const double OxygenCurrentResolutionMa = 0.0001;
        private const double OxygenConcentrationResolutionPercent = 0.0001;
        private const double OxygenPressureResolutionPsi = 0.0005;

        private const double ConcentrationCurrentToleranceMa = 0.125;
        private const double PressureCurrentToleranceMa = 0.1;

        private readonly ISingleBoardTestContextService _singleBoardTestContext;
        private readonly SynchronizationContext _uiContext;
        private readonly Prism.Events.IEventAggregator _eventAggregator;
        private readonly IComponentPowerStateApi _componentPowerStateApi;

        private readonly SemaphoreSlim _opLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _stopLock = new SemaphoreSlim(1, 1);
        private CancellationTokenSource _cts;

        private bool _isManualTestRunning;
        private bool _isAutoTestRunning;
        private string _lastTestTime = "--";
        private string _lastTestResult = "--";

        private bool _isPowerOn;
        private string _powerStatus = "未供电";

        private IPowerSupplyApi _power;

        private IJy7131Api _jy7131;

        private IMtx532Api _mtx532;
        private int? _connectedSlot;
        private string _connectionText = "532: 未连接";

        private IArt4229Api _arinc;
        private Task _arincRxLoopTask;
        private bool _atpTxOpened;
        private bool _atpModeEntered;
        private bool _arincRxStarted;

        private readonly object _arincCacheLock = new object();
        private List<double> _concCurrentCache = new List<double>();
        private List<double> _concValueCache = new List<double>();
        private List<double> _pressCurrentCache = new List<double>();
        private List<double> _pressValueCache = new List<double>();

        private string _manualConcentrationCurrentMaText;
        private string _manualPressureCurrentMaText;

        public OxygenSensorSignalAcquisitionTestViewModel(
            ISingleBoardTestContextService singleBoardTestContext,
            Prism.Events.IEventAggregator eventAggregator,
            IComponentPowerStateApi componentPowerStateApi = null)
        {
            _singleBoardTestContext = singleBoardTestContext;
            _uiContext = SynchronizationContext.Current;

            _eventAggregator = eventAggregator;
            _componentPowerStateApi = componentPowerStateApi;

            ManualTestCommand = new DelegateCommand(async () => await OnManualTestAsync());
            AutoTestCommand = new DelegateCommand(async () => await OnAutoTestAsync());
            ClearLogCommand = new DelegateCommand(() => Logs.Clear());

            Items = new ObservableCollection<OxygenPointItemViewModel>
            {
                new OxygenPointItemViewModel("1)", "氧气浓度", "(0±0.125)mA", 0.0, expectedValueText: "--", aoChannel: ConcentrationAoChannel, this),
                new OxygenPointItemViewModel("2)", "氧气浓度", "(4±0.125)mA", 4.0, expectedValueText: "1.0±0.1%", aoChannel: ConcentrationAoChannel, this),
                new OxygenPointItemViewModel("3)", "氧气浓度", "(12±0.125)mA", 12.0, expectedValueText: "7.5±0.1%", aoChannel: ConcentrationAoChannel, this),
                new OxygenPointItemViewModel("4)", "氧气浓度", "(20±0.125)mA", 20.0, expectedValueText: "14±0.1%", aoChannel: ConcentrationAoChannel, this),
                new OxygenPointItemViewModel("5)", "氧气浓度", "(25±0.125)mA", 25.0, expectedValueText: "--", aoChannel: ConcentrationAoChannel, this),

                new OxygenPointItemViewModel("1)", "氧气压力", "(0±0.1)mA", 0.0, expectedValueText: "--", aoChannel: PressureAoChannel, this),
                new OxygenPointItemViewModel("2)", "氧气压力", "(4±0.1)mA", 4.0, expectedValueText: "15.0±0.34psia", aoChannel: PressureAoChannel, this),
                new OxygenPointItemViewModel("3)", "氧气压力", "(12±0.1)mA", 12.0, expectedValueText: "42.5±0.34psia", aoChannel: PressureAoChannel, this),
                new OxygenPointItemViewModel("4)", "氧气压力", "(20±0.1)mA", 20.0, expectedValueText: "70.0±0.34psia", aoChannel: PressureAoChannel, this),
                new OxygenPointItemViewModel("5)", "氧气压力", "(25±0.1)mA", 25.0, expectedValueText: "--", aoChannel: PressureAoChannel, this),
            };
        }

        public string ManualConcentrationCurrentMaText
        {
            get => _manualConcentrationCurrentMaText;
            set => SetProperty(ref _manualConcentrationCurrentMaText, value);
        }

        public string ManualPressureCurrentMaText
        {
            get => _manualPressureCurrentMaText;
            set => SetProperty(ref _manualPressureCurrentMaText, value);
        }

        private static bool TryParseManualCurrentMa(string text, out double valueMa)
        {
            valueMa = 0;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            var raw = text.Trim();
            var cleaned = new string(raw.Where(c => char.IsDigit(c) || c == '.' || c == '-' || c == '+').ToArray());
            if (string.IsNullOrWhiteSpace(cleaned))
                return false;

            if (!double.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out valueMa))
                return false;

            if (valueMa < 0 || valueMa > 25)
                return false;

            return true;
        }

        private async Task<bool> EnsureArincRxReadyAsync(CancellationToken token)
        {
            if (_arinc != null && _arinc.IsConnected && _arincRxStarted)
                return true;

            if (_arinc == null)
            {
                DeviceBase dev = null;
                try
                {
                    var pxi = ContainerLocator.Container.Resolve<IPxiChassisService>();
                    var chassisList = pxi?.GetAllChassis();
                    if (chassisList != null)
                    {
                        foreach (var chassis in chassisList)
                        {
                            if (chassis?.Devices == null) continue;
                            dev = chassis.Devices.FirstOrDefault(d =>
                                (d?.Model?.IndexOf("4227", StringComparison.OrdinalIgnoreCase) >= 0) ||
                                (d?.Model?.IndexOf("4229", StringComparison.OrdinalIgnoreCase) >= 0) ||
                                (d?.Model?.IndexOf("ARINC", StringComparison.OrdinalIgnoreCase) >= 0) ||
                                (d?.Model?.IndexOf("429", StringComparison.OrdinalIgnoreCase) >= 0) ||
                                (d?.Name?.IndexOf("4227", StringComparison.OrdinalIgnoreCase) >= 0) ||
                                (d?.Name?.IndexOf("4229", StringComparison.OrdinalIgnoreCase) >= 0) ||
                                (d?.Name?.IndexOf("429", StringComparison.OrdinalIgnoreCase) >= 0));
                            if (dev != null)
                                break;
                        }
                    }
                }
                catch
                {
                }

                if (dev != null)
                    _arinc = new Art4229Api(dev, deviceIndex: 0);
                _atpTxOpened = false;
                _atpModeEntered = false;
            }

            if (_arinc == null)
            {
                Log("未找到ART4229(ARINC429)板卡，无法采集氧气数据");
                return false;
            }

            try
            {
                if (!_arinc.IsConnected)
                    await _arinc.ConnectAsync(token).ConfigureAwait(false);
                await _arinc.OpenRxAsync(ArincRxChannelIndex, token).ConfigureAwait(false);
                await _arinc.ConfigureRxAsync(
                    ArincRxChannelIndex,
                    rate: ArincRate,
                    parity: Art4229Parity.Odd,
                    wordFormat: Art4229WordFormat.Standard429,
                    enableInterrupt: false,
                    interruptDepth: 512,
                    enableTimeTag: false,
                    cancellationToken: token).ConfigureAwait(false);

                await _arinc.StartRxAsync(ArincRxChannelIndex, token).ConfigureAwait(false);
                _arincRxStarted = true;
                Log($"429接收已启动：RX通道{ArincRxChannelIndex}, {ArincRate / 1000:0.#}kbps, 奇校验");

                // 与温度VM一致：先启动RX，再进入ATP模式（TX通道1发送 0xC100013E）
                await EnsureAtpModeAsync(token).ConfigureAwait(false);
                return true;
            }
            catch (Exception ex)
            {
                Log($"429初始化失败：{ex.Message}");
                try { await _arinc.StopRxAsync(ArincRxChannelIndex, CancellationToken.None).ConfigureAwait(false); } catch { }
                try { await _arinc.CloseRxAsync(ArincRxChannelIndex, CancellationToken.None).ConfigureAwait(false); } catch { }
                _arincRxStarted = false;
                if (_atpTxOpened)
                {
                    try { await _arinc.CloseTxAsync(ArincTxChannelIndex, CancellationToken.None).ConfigureAwait(false); } catch { }
                    _atpTxOpened = false;
                }
                _atpModeEntered = false;
                return false;
            }
        }

        private void ResetArincTelemetryCache()
        {
            lock (_arincCacheLock)
            {
                _concCurrentCache = new List<double>();
                _concValueCache = new List<double>();
                _pressCurrentCache = new List<double>();
                _pressValueCache = new List<double>();
            }
        }

        private void StartArincRxLoopIfNeeded(CancellationToken token)
        {
            if (_arinc == null || !_arinc.IsConnected)
                return;
            if (!_arincRxStarted)
                return;

            if (_arincRxLoopTask != null && !_arincRxLoopTask.IsCompleted)
                return;

            _arincRxLoopTask = Task.Run(() => ArincRxLoopAsync(token), token);
        }

        private async Task ArincRxLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (_arinc == null || !_arinc.IsConnected || !_arincRxStarted)
                    {
                        await Task.Delay(ArincPollIntervalMs, token).ConfigureAwait(false);
                        continue;
                    }

                    var words = await _arinc.ReadRxWordsAsync(ArincRxChannelIndex, maxCount: 256, enableTimeTag: false, enableRateAdaption: false, cancellationToken: token).ConfigureAwait(false);
                    if (words.Count > 0)
                    {
                        foreach (var w in words)
                        {
                            _arinc.ParseRawWord(w.Data429, out var label, out var sdi, out var data19, out var ssm);

                            if (sdi != ArincExpectedSdi)
                                continue;

                            if (label == OxygenConcentrationCurrentLabelRxDec)
                            {
                                var v = DecodeSignMagnitude19(data19, OxygenCurrentResolutionMa);
                                AppendCache(_concCurrentCache, v);
                                continue;
                            }
                            if (label == OxygenConcentrationValueLabelRxDec)
                            {
                                var v = DecodeSignMagnitude19(data19, OxygenConcentrationResolutionPercent);
                                AppendCache(_concValueCache, v);
                                continue;
                            }
                            if (label == OxygenPressureCurrentLabelRxDec)
                            {
                                var v = DecodeSignMagnitude19(data19, OxygenCurrentResolutionMa);
                                AppendCache(_pressCurrentCache, v);
                                continue;
                            }
                            if (label == OxygenPressureValueLabelRxDec)
                            {
                                var v = DecodeSignMagnitude19(data19, OxygenPressureResolutionPsi);
                                AppendCache(_pressValueCache, v);
                                continue;
                            }
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch
                {
                    // 避免接收线程异常退出
                }

                try { await Task.Delay(ArincPollIntervalMs, token).ConfigureAwait(false); } catch { }
            }
        }

        private void ClearCachesForLabels(byte currentLabel, byte valueLabel)
        {
            lock (_arincCacheLock)
            {
                if (currentLabel == OxygenConcentrationCurrentLabelRxDec)
                    _concCurrentCache.Clear();
                else if (currentLabel == OxygenPressureCurrentLabelRxDec)
                    _pressCurrentCache.Clear();

                if (valueLabel == OxygenConcentrationValueLabelRxDec)
                    _concValueCache.Clear();
                else if (valueLabel == OxygenPressureValueLabelRxDec)
                    _pressValueCache.Clear();
            }
        }

        private void AppendCache(List<double> target, double value)
        {
            lock (_arincCacheLock)
            {
                target.Add(value);
                var max = Math.Max(SamplesPerMeasure * 4, 20);
                if (target.Count > max)
                    target.RemoveRange(0, target.Count - max);
            }
        }

        private static byte ReverseLabelBits(byte label)
        {
            byte reversed = 0;
            for (int i = 0; i < 8; i++)
            {
                reversed = (byte)((reversed << 1) | ((label >> i) & 0x01));
            }
            return reversed;
        }

        private byte GetAtpLabelForTx()
        {
            if (_arinc != null)
                return _arinc.ReverseLabel(AtpLabelOctal174Dec);

            return ReverseLabelBits(AtpLabelOctal174Dec);
        }

        private uint BuildAtpEnterWord(out byte txLabel)
        {
            txLabel = GetAtpLabelForTx();
            return ((AtpSsmDataSdi & 0x00FFFFFFu) << 8) | txLabel;
        }

        private async Task EnsureAtpModeAsync(CancellationToken token)
        {
            if (_arinc == null || !_arinc.IsConnected)
                return;

            if (!_atpTxOpened)
            {
                await _arinc.OpenTxAsync(ArincTxChannelIndex, token).ConfigureAwait(false);
                await _arinc.ConfigureTxAsync(
                    ArincTxChannelIndex,
                    rate: ArincRate,
                    mode: Art4229TxMode.Single,
                    parity: Art4229Parity.None,
                    wordFormat: Art4229WordFormat.Standard429,
                    cancellationToken: token).ConfigureAwait(false);
                _atpTxOpened = true;
            }

            if (_atpModeEntered)
                return;

            var word = BuildAtpEnterWord(out var txLabel);
            Log($"测试信息-ATP发送准备: TX通道{ArincTxChannelIndex}, SSM/Data/SDI=0x{AtpSsmDataSdi:X6}, Label(oct174)=0x{AtpLabelOctal174Dec:X2}, Label反转后=0x{txLabel:X2}, Word=0x{word:X8}");
            try
            {
                await _arinc.SendWordsSingleAsync(ArincTxChannelIndex, new[] { word }, Art4229Parity.None, token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log($"测试信息-ATP发送失败: TX通道{ArincTxChannelIndex}, Word=0x{word:X8}, 异常={ex.Message}");
                throw;
            }
            Log($"测试信息-ATP发送完成: TX通道{ArincTxChannelIndex}, Word=0x{word:X8}");
            _atpModeEntered = true;
        }

        private static bool IsExpectedLabel(byte labelRaw, byte expectedLabelRxDec)
            => labelRaw == expectedLabelRxDec;

        private static bool TryParseExpectedTolerance(string expectedValueText, out double expected, out double tol)
        {
            expected = 0;
            tol = 0;
            if (string.IsNullOrWhiteSpace(expectedValueText))
                return false;

            var raw = expectedValueText.Trim();
            if (raw == "--")
                return false;

            var parts = raw.Split(new[] { '±' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2)
                return false;

            var left = new string(parts[0].Where(c => char.IsDigit(c) || c == '.' || c == '-' || c == '+').ToArray());
            var right = new string(parts[1].Where(c => char.IsDigit(c) || c == '.' || c == '-' || c == '+').ToArray());
            if (!double.TryParse(left, NumberStyles.Float, CultureInfo.InvariantCulture, out expected))
                return false;
            if (!double.TryParse(right, NumberStyles.Float, CultureInfo.InvariantCulture, out tol))
                return false;
            if (tol < 0)
                tol = Math.Abs(tol);
            return true;
        }

        private async Task<(double? currentMa, double? value, string result)> MeasureOxygenVia429Async(
            byte currentLabel,
            double currentResolution,
            byte valueLabel,
            double valueResolution,
            CancellationToken token)
        {
            if (_arinc == null || !_arinc.IsConnected || !_arincRxStarted)
                return (null, null, "FAIL");

            // 与温度VM一致：采集阶段不再直接读取硬件缓冲，避免与后台RX循环竞争；
            // 仅等待缓存攒够样本并取平均。
            var deadline = DateTime.UtcNow.AddMilliseconds(MeasureTimeoutMs);
            while (!token.IsCancellationRequested && DateTime.UtcNow <= deadline)
            {
                var currentAvg = TryGetAverageFromCache(currentLabel, SamplesPerMeasure);
                var valueAvg = TryGetAverageFromCache(valueLabel, SamplesPerMeasure);
                if (currentAvg.HasValue && valueAvg.HasValue)
                    return (currentAvg, valueAvg, "PASS");

                await Task.Delay(ArincPollIntervalMs, token).ConfigureAwait(false);
            }

            return (
                TryGetAverageFromCache(currentLabel, 1),
                TryGetAverageFromCache(valueLabel, 1),
                "FAIL");
        }

        private async Task<(double? currentMa, string result)> MeasureOxygenCurrentVia429Async(
            byte currentLabel,
            CancellationToken token)
        {
            if (_arinc == null || !_arinc.IsConnected || !_arincRxStarted)
                return (null, "FAIL");

            var deadline = DateTime.UtcNow.AddMilliseconds(MeasureTimeoutMs);
            while (!token.IsCancellationRequested && DateTime.UtcNow <= deadline)
            {
                var currentAvg = TryGetAverageFromCache(currentLabel, SamplesPerMeasure);
                if (currentAvg.HasValue)
                    return (currentAvg, "PASS");

                await Task.Delay(ArincPollIntervalMs, token).ConfigureAwait(false);
            }

            return (TryGetAverageFromCache(currentLabel, 1), "FAIL");
        }

        private double? TryGetAverageFromCache(byte labelRxDec, int takeLastCount)
        {
            if (takeLastCount <= 0)
                takeLastCount = 1;

            List<double> src;
            lock (_arincCacheLock)
            {
                if (labelRxDec == OxygenConcentrationCurrentLabelRxDec)
                    src = _concCurrentCache;
                else if (labelRxDec == OxygenConcentrationValueLabelRxDec)
                    src = _concValueCache;
                else if (labelRxDec == OxygenPressureCurrentLabelRxDec)
                    src = _pressCurrentCache;
                else if (labelRxDec == OxygenPressureValueLabelRxDec)
                    src = _pressValueCache;
                else
                    return null;

                if (src == null || src.Count <= 0)
                    return null;

                var n = Math.Min(takeLastCount, src.Count);
                var slice = src.Skip(src.Count - n).Take(n).ToArray();
                return slice.Average();
            }
        }

        private static double DecodeSignMagnitude19(uint data19, double resolution)
        {
            data19 &= 0x7FFFF;
            var sign = ((data19 >> 18) & 0x1u) != 0;
            var magnitude = data19 & 0x3FFFF;
            var value = magnitude * resolution;
            return sign ? -value : value;
        }

        private void UpdateConnectionText()
        {
            var mtx = _mtx532 != null && _mtx532.IsConnected
                ? $"532: 已连接(SLOT={_connectedSlot})"
                : "532: 未连接";

            var arinc = _arinc != null && _arinc.IsConnected
                ? "4229: 已连接"
                : "4229: 未连接";

            ConnectionText = $"{mtx} | {arinc}";
        }

        private void PostToUi(Action action)
        {
            if (action == null)
                return;

            if (_uiContext != null && !ReferenceEquals(SynchronizationContext.Current, _uiContext))
            {
                _uiContext.Post(_ =>
                {
                    try { action(); } catch { }
                }, null);
                return;
            }

            action();
        }



        private void PublishNavigationLock(bool isLocked, string source)

        {

            try

            {

                _eventAggregator?.GetEvent<MeasureControl.Events.NavigationLockChangedEvent>()

                    ?.Publish(new MeasureControl.Events.NavigationLockChangedEventArgs { IsLocked = isLocked, Source = source });

            }

            catch

            {

            }

        }

        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();

        public ObservableCollection<OxygenPointItemViewModel> Items { get; }

        public DelegateCommand ManualTestCommand { get; }
        public DelegateCommand AutoTestCommand { get; }
        public DelegateCommand ClearLogCommand { get; }

        public bool IsPowerOn
        {
            get => _isPowerOn;
            private set => SetProperty(ref _isPowerOn, value);
        }

        public string PowerStatus
        {
            get => _powerStatus;
            private set => SetProperty(ref _powerStatus, value);
        }

        public string ConnectionText
        {
            get => _connectionText;
            private set => SetProperty(ref _connectionText, value);
        }

        public bool IsManualTestRunning
        {
            get => _isManualTestRunning;
            private set => SetProperty(ref _isManualTestRunning, value);
        }

        public bool IsAutoTestRunning
        {
            get => _isAutoTestRunning;
            private set => SetProperty(ref _isAutoTestRunning, value);
        }

        public string LastTestTime
        {
            get => _lastTestTime;
            private set => SetProperty(ref _lastTestTime, value);
        }

        public string LastTestResult
        {
            get => _lastTestResult;
            private set => SetProperty(ref _lastTestResult, value);
        }

        private async Task OnManualTestAsync()
        {
            if (IsManualTestRunning)
            {
                await StopAsync().ConfigureAwait(false);
                return;
            }

            // 检查是否已总上电
            var _hps = ContainerLocator.Container.Resolve<IBoardPowerService>();
            if (_hps == null || !_hps.IsPowered)
            {
                MessageBox.Show("请先点击左上角组件上电按钮进行总上电，再进行测试。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (IsAutoTestRunning)
                await StopAsync().ConfigureAwait(false);

            await _opLock.WaitAsync().ConfigureAwait(false);
            try
            {
                _cts?.Cancel();
                _cts?.Dispose();
                _cts = new CancellationTokenSource();

                IsManualTestRunning = true;
                IsAutoTestRunning = false;

                PublishNavigationLock(isLocked: true, source: "OxygenSensor");
                LastTestTime = "--";
                LastTestResult = "--";

                PostToUi(() =>
                {
                    foreach (var item in Items)
                        item.Reset();
                });

                await EnsurePowerAsync(_cts.Token).ConfigureAwait(false);

                Log("开始手动测试（氧气传感器信号采集）：准备连接532模拟量输出板卡");

                var ok532 = await EnsureMtx532ReadyAsync(_cts.Token).ConfigureAwait(false);
                var ok429 = await EnsureArincRxReadyAsync(_cts.Token).ConfigureAwait(false);
                ResetArincTelemetryCache();
                StartArincRxLoopIfNeeded(_cts.Token);
                UpdateConnectionText();
                if (!ok532 || !ok429)
                {
                    Log("板卡连接失败：请检查 532/4229 板卡、驱动、机箱配置");
                    await StopAsync().ConfigureAwait(false);
                    return;
                }

                Log($"已就绪：浓度通道={ConcentrationAoChannel}，压力通道={PressureAoChannel}；0-10V对应0-27mA换算输出");
            }
            catch (OperationCanceledException)
            {
                await StopAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log($"手动测试初始化异常：{ex.Message}");
                await StopAsync().ConfigureAwait(false);
            }
            finally
            {
                _opLock.Release();
            }
        }

        public async Task<string> RunOnceAsync(CancellationToken cancellationToken)
        {
            if (IsAutoTestRunning || IsManualTestRunning)
            {
                return LastTestResult;
            }

            CancellationToken token;
            await _opLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                _cts?.Cancel();
                _cts?.Dispose();
                _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                token = _cts.Token;

                IsAutoTestRunning = true;
                IsManualTestRunning = false;

                PublishNavigationLock(isLocked: true, source: "OxygenSensor");
                LastTestTime = "--";
                LastTestResult = "--";

                PostToUi(() =>
                {
                    foreach (var item in Items)
                        item.Reset();
                });

                await EnsurePowerAsync(token).ConfigureAwait(false);

                Log("开始自动测试：将依次执行10个电流点位的电压输出");
                var ok532 = await EnsureMtx532ReadyAsync(token).ConfigureAwait(false);
                var ok429 = await EnsureArincRxReadyAsync(token).ConfigureAwait(false);
                ResetArincTelemetryCache();
                StartArincRxLoopIfNeeded(token);
                UpdateConnectionText();
                if (!ok532 || !ok429)
                {
                    LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    LastTestResult = "FAIL";
                    Log("板卡连接失败：自动测试终止");
                    return "FAIL";
                }
            }
            finally
            {
                _opLock.Release();
            }

            try
            {
                foreach (var item in Items)
                {
                    token.ThrowIfCancellationRequested();
                    await item.MeasureAsync().ConfigureAwait(false);
                    await Task.Delay(60).ConfigureAwait(false);
                }

                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                LastTestResult = Items.All(x => string.Equals(x.Result, "PASS", StringComparison.OrdinalIgnoreCase)) ? "PASS" : "FAIL";
                Log($"自动测试结束：汇总结果={LastTestResult}");

                return LastTestResult;
            }
            catch (OperationCanceledException)
            {
                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                LastTestResult = "FAIL";
                Log("自动测试已取消");
                return "FAIL";
            }
            catch (Exception ex)
            {
                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                LastTestResult = "FAIL";
                Log($"自动测试异常：{ex.Message}");
                return "FAIL";
            }
            finally
            {
                try { await StopAsync().ConfigureAwait(false); } catch { }
                IsAutoTestRunning = false;
            }
        }

        private async Task OnAutoTestAsync()
        {
            if (IsAutoTestRunning)
            {
                await StopAsync().ConfigureAwait(false);
                return;
            }

            // 检查是否已总上电
            var _hps = ContainerLocator.Container.Resolve<IBoardPowerService>();
            if (_hps == null || !_hps.IsPowered)
            {
                MessageBox.Show("请先点击左上角组件上电按钮进行总上电，再进行测试。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (IsManualTestRunning)
                await StopAsync().ConfigureAwait(false);

            CancellationToken token;
            await _opLock.WaitAsync().ConfigureAwait(false);
            try
            {
                _cts?.Cancel();
                _cts?.Dispose();
                _cts = new CancellationTokenSource();
                token = _cts.Token;

                IsAutoTestRunning = true;
                IsManualTestRunning = false;

                PublishNavigationLock(isLocked: true, source: "OxygenSensor");
                LastTestTime = "--";
                LastTestResult = "--";

                PostToUi(() =>
                {
                    foreach (var item in Items)
                        item.Reset();
                });

                await EnsurePowerAsync(token).ConfigureAwait(false);

                Log("开始自动测试：将依次执行10个电流点位的电压输出");
                var ok532 = await EnsureMtx532ReadyAsync(token).ConfigureAwait(false);
                var ok429 = await EnsureArincRxReadyAsync(token).ConfigureAwait(false);
                ResetArincTelemetryCache();
                StartArincRxLoopIfNeeded(token);
                UpdateConnectionText();
                if (!ok532 || !ok429)
                {
                    LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    LastTestResult = "FAIL";
                    Log("板卡连接失败：自动测试终止");
                    return;
                }
            }
            finally
            {
                _opLock.Release();
            }

            try
            {
                foreach (var item in Items)
                {
                    token.ThrowIfCancellationRequested();
                    await item.MeasureAsync().ConfigureAwait(false);
                    await Task.Delay(60).ConfigureAwait(false);
                }

                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                LastTestResult = Items.All(x => string.Equals(x.Result, "PASS", StringComparison.OrdinalIgnoreCase)) ? "PASS" : "FAIL";
                Log($"自动测试结束：汇总结果={LastTestResult}");

                try
                {
                    var reportPath = ExportAutoTestReportToCsv();
                    Log($"自动测试报表已生成：{reportPath}");
                }
                catch (Exception ex)
                {
                    Log($"自动测试报表生成失败：{ex.Message}");
                }
            }
            catch (Exception ex)
            {
                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                LastTestResult = "FAIL";
                Log($"自动测试异常：{ex.Message}");
            }
            finally
            {
                try { await StopAsync().ConfigureAwait(false); } catch { }
                IsAutoTestRunning = false;
            }
        }

        private async Task StopAsync()
        {
            if (_stopLock.Wait(0) == false)
                return;
            try
            {
                try { _cts?.Cancel(); } catch { }

                if (!SkipMainPowerOff)
                {
                    await DisableAllPowerSuppliesAsync(CancellationToken.None).ConfigureAwait(false);
                }

                await DisablePowerAsync(CancellationToken.None).ConfigureAwait(false);

                if (_mtx532 != null)
                {
                    try { await _mtx532.StopOutputAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _mtx532.ResetAllToZeroAsync(disableAfterReset: true, cancellationToken: CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _mtx532.DisconnectAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _mtx532.DisposeAsync().ConfigureAwait(false); } catch { }
                    _mtx532 = null;
                }

                if (_arinc != null)
                {
                    try { await _arinc.StopRxAsync(ArincRxChannelIndex, CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _arinc.CloseRxAsync(ArincRxChannelIndex, CancellationToken.None).ConfigureAwait(false); } catch { }
                    if (_atpTxOpened)
                    {
                        try { await _arinc.CloseTxAsync(ArincTxChannelIndex, CancellationToken.None).ConfigureAwait(false); } catch { }
                    }
                    try { await _arinc.DisconnectAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _arinc.DisposeAsync().ConfigureAwait(false); } catch { }
                    _arinc = null;
                    _arincRxStarted = false;
                    _arincRxLoopTask = null;
                    _atpTxOpened = false;
                    _atpModeEntered = false;
                }

                if (_power != null)
                {
                    try { await _power.DisconnectAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _power.DisposeAsync().ConfigureAwait(false); } catch { }
                    _power = null;
                }

                if (_jy7131 != null)
                {
                    try
                    {
                        if (_jy7131.IsConnected)
                        {
                            if (_jy7131.IsRunning)
                                await _jy7131.StopAsync(CancellationToken.None).ConfigureAwait(false);
                            await _jy7131.DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
                        }
                    }
                    catch
                    {
                    }
                    finally
                    {
                        try { await _jy7131.DisposeAsync().ConfigureAwait(false); } catch { }
                        _jy7131 = null;
                    }
                }

                _connectedSlot = null;
                UpdateConnectionText();

                IsManualTestRunning = false;
                IsAutoTestRunning = false;

                PublishNavigationLock(isLocked: false, source: "OxygenSensor");

                if (!SkipMainPowerOff)
                {
                    IsPowerOn = false;
                    PowerStatus = "未供电";
                }
                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            }
            finally
            {
                try { _stopLock.Release(); } catch { }
            }
        }

        private async Task EnsurePowerAsync(CancellationToken token)
        {
            await Ensure7131ReadyAsync(token).ConfigureAwait(false);

            // 192.168.1.15 CH1 不再由本测试控制上电，由总上电统一管理
            // 仍需连接电源以便控制CH2
            _power ??= new PowerSupplySocketApi();
            if (!_power.IsConnected)
                await _power.ConnectAsync(PowerSupplyIpAddress, token).ConfigureAwait(false);

            await _power.ApplyAsync(PowerSupplyChannel.CH2, PowerCh2VoltageV, PowerCurrentLimitA, token).ConfigureAwait(false);
            await _power.SetOutputEnabledAsync(PowerSupplyChannel.CH2, true, token).ConfigureAwait(false);
            await Task.Delay(200, token).ConfigureAwait(false);

            PostToUi(() =>
            {
                IsPowerOn = true;
                PowerStatus = $"已供电(CH1 {PowerCh1VoltageV:0.###}V, CH2 {PowerCh2VoltageV:0.###}V)";
            });
        }

        private async Task Ensure7131ReadyAsync(CancellationToken token)
        {
            if (_jy7131 == null)
            {
                DeviceBase device = null;
                try
                {
                    var pxi = ContainerLocator.Container.Resolve<IPxiChassisService>();
                    var chassisList = pxi?.GetAllChassis();
                    if (chassisList != null)
                    {
                        foreach (var chassis in chassisList)
                        {
                            if (chassis?.Devices == null) continue;
                            device = chassis.Devices.FirstOrDefault(d =>
                                d is MeasureControl.Models.Devices.DigitalIODevice ||
                                (d?.Model?.IndexOf("7131", StringComparison.OrdinalIgnoreCase) >= 0) ||
                                (d?.Name?.IndexOf("7131", StringComparison.OrdinalIgnoreCase) >= 0) ||
                                (d?.DeviceTypeName?.IndexOf("离散量", StringComparison.OrdinalIgnoreCase) >= 0) ||
                                (d?.DeviceTypeName?.IndexOf("数字量", StringComparison.OrdinalIgnoreCase) >= 0));
                            if (device != null)
                                break;
                        }
                    }
                }
                catch
                {
                }

                if (device != null)
                {
                    var slot = Infer7131SlotNumber(device);
                    if (int.TryParse(slot, out var slotNum))
                        _jy7131 = new Jy7131Api(device, slotNum);
                    else
                        _jy7131 = new Jy7131Api(device);
                }
            }

            if (_jy7131 == null)
            {
                Log("未找到7131板卡");
                return;
            }

            try
            {
                if (!_jy7131.IsConnected)
                {
                    await _jy7131.ConnectAsync(token).ConfigureAwait(false);
                    await _jy7131.SetOutputModeAsync(Jy7131OutputMode.Sinking, token).ConfigureAwait(false);
                    await _jy7131.StartAsync(token).ConfigureAwait(false);
                    Log("7131板卡已启动(Sinking模式)");
                }
                else if (!_jy7131.IsRunning)
                {
                    await _jy7131.SetOutputModeAsync(Jy7131OutputMode.Sinking, token).ConfigureAwait(false);
                    await _jy7131.StartAsync(token).ConfigureAwait(false);
                    Log("7131板卡已启动(Sinking模式)");
                }
                else
                {
                    await _jy7131.SetOutputModeAsync(Jy7131OutputMode.Sinking, token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log($"7131板卡初始化失败(将继续运行，不影响氧气采集)：{ex.Message}");
                try
                {
                    if (_jy7131 != null)
                    {
                        if (_jy7131.IsConnected)
                        {
                            if (_jy7131.IsRunning)
                                await _jy7131.StopAsync(CancellationToken.None).ConfigureAwait(false);
                            await _jy7131.DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
                        }
                    }
                }
                catch
                {
                }
                finally
                {
                    try { if (_jy7131 != null) await _jy7131.DisposeAsync().ConfigureAwait(false); } catch { }
                    _jy7131 = null;
                }
            }
        }

        private static string Infer7131SlotNumber(DeviceBase device)
        {
            if (device is PxiDeviceBase pxi && pxi.SlotIndex > 0)
                return pxi.SlotIndex.ToString();

            var slot = device?.SlotPosition;
            if (!string.IsNullOrWhiteSpace(slot))
            {
                var trimmed = slot.Replace("Slot", "").Replace("slot", "").Trim();
                if (int.TryParse(trimmed, out var slotNum) && slotNum > 0)
                    return slotNum.ToString();
            }
            return "12";
        }

        private async Task DisablePowerAsync(CancellationToken token)
        {
            try
            {
                if (_power == null)
                    return;

                if (!_power.IsConnected)
                    await _power.ConnectAsync(PowerSupplyIpAddress, token).ConfigureAwait(false);

                // 192.168.1.15 CH1 不再由本测试控制下电
                await _power.SetOutputEnabledAsync(PowerSupplyChannel.CH2, false, token).ConfigureAwait(false);
                await Task.Delay(200, token).ConfigureAwait(false);
            }
            catch
            {
            }
            finally
            {
                PostToUi(() =>
                {
                    IsPowerOn = false;
                    PowerStatus = "未供电";
                });
            }
        }

        private static async Task DisableAllPowerSuppliesAsync(CancellationToken token)
        {
            foreach (var ip in AllPowerSupplyIpAddresses)
            {
                IPowerSupplyApi ps = null;
                try
                {
                    ps = new PowerSupplySocketApi();
                    await ps.ConnectAsync(ip, token).ConfigureAwait(false);
                    // 192.168.1.15 CH1 不再由本测试控制下电
                    if (ip != "192.168.1.15")
                    {
                        await ps.SetOutputEnabledAsync(PowerSupplyChannel.CH1, false, token).ConfigureAwait(false);
                    }
                    await ps.SetOutputEnabledAsync(PowerSupplyChannel.CH2, false, token).ConfigureAwait(false);
                    await ps.SetOutputEnabledAsync(PowerSupplyChannel.CH3, false, token).ConfigureAwait(false);
                }
                catch
                {
                }
                finally
                {
                    if (ps != null)
                    {
                        try { await ps.DisconnectAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                        try { await ps.DisposeAsync().ConfigureAwait(false); } catch { }
                    }
                }
            }
        }

        private static double CurrentMaToVoltageV(double currentMa)
        {
            // 标定点(单位: mA -> V)
            // 0  -> 0
            // 4  -> 0.798
            // 12 -> 2.4
            // 20 -> 4.01
            // 25 -> 5.01
            if (currentMa <= 0)
                return 0;
            if (currentMa >= 25)
                return 5.01;

            if (currentMa <= 4)
                return Lerp(0, 0, 4, 0.798, currentMa);
            if (currentMa <= 12)
                return Lerp(4, 0.798, 12, 2.4, currentMa);
            if (currentMa <= 20)
                return Lerp(12, 2.4, 20, 4.01, currentMa);
            return Lerp(20, 4.01, 25, 5.01, currentMa);
        }

        private static double Lerp(double x0, double y0, double x1, double y1, double x)
        {
            if (Math.Abs(x1 - x0) < 1e-12)
                return y0;
            return y0 + (y1 - y0) * ((x - x0) / (x1 - x0));
        }

        private async Task<bool> EnsureMtx532ReadyAsync(CancellationToken token)
        {
            if (_mtx532 != null && _mtx532.IsConnected)
                return true;

            DeviceBase dev = null;
            try
            {
                var pxiService = ContainerLocator.Container.Resolve<IPxiChassisService>();
                var chassisList = pxiService?.GetAllChassis();
                if (chassisList != null)
                {
                    foreach (var chassis in chassisList)
                    {
                        if (chassis?.Devices == null) continue;
                        dev = chassis.Devices.FirstOrDefault(d =>
                            (d?.Model?.IndexOf("532", StringComparison.OrdinalIgnoreCase) >= 0) ||
                            (d?.Name?.IndexOf("532", StringComparison.OrdinalIgnoreCase) >= 0) ||
                            (d?.Model?.IndexOf("MTX", StringComparison.OrdinalIgnoreCase) >= 0) ||
                            (d?.Name?.IndexOf("MTX", StringComparison.OrdinalIgnoreCase) >= 0));
                        if (dev != null)
                            break;
                    }
                }
            }
            catch
            {
            }

            if (dev == null)
                return false;

            static int? TryParseSlotFromText(string text)
            {
                if (string.IsNullOrWhiteSpace(text))
                    return null;
                var digits = new string(text.Where(char.IsDigit).ToArray());
                if (int.TryParse(digits, out var n) && n > 0)
                    return n;
                return null;
            }

            int? preferredSlot = null;
            if (dev is PxiDeviceBase pxiDev && pxiDev.SlotIndex > 0)
                preferredSlot = pxiDev.SlotIndex;

            preferredSlot ??= TryParseSlotFromText(dev.SlotPosition);
            preferredSlot ??= TryParseSlotFromText(dev.Name);
            preferredSlot ??= TryParseSlotFromText(dev.CardName);

            var slotCandidates = new List<int>();
            if (preferredSlot.HasValue)
                slotCandidates.Add(preferredSlot.Value);
            for (int s = 2; s <= 18; s++)
            {
                if (!slotCandidates.Contains(s))
                    slotCandidates.Add(s);
            }
            if (!slotCandidates.Contains(7))
                slotCandidates.Add(7);

            Exception lastEx = null;
            foreach (var slot in slotCandidates)
            {
                token.ThrowIfCancellationRequested();

                IMtx532Api api = null;
                try
                {
                    api = new Mtx532Api(dev, options: new Mtx532Options { SampleRateHz = 1000.0, UseOneBasedAoChannelNumbering = true }, slotNumber: slot);
                    await api.ConnectAsync(token, enabledAoChannels: new[] { ConcentrationAoChannel, PressureAoChannel }).ConfigureAwait(false);

                    await api.SetDcAsync(ConcentrationAoChannel, 0.0, enable: true, cancellationToken: token).ConfigureAwait(false);

                    // 直流输出需要持续运行，确保万用表可稳定测到
                    try { await api.StartOutputAsync(token).ConfigureAwait(false); } catch { }

                    try { await api.ResetAllToZeroAsync(disableAfterReset: false, cancellationToken: token).ConfigureAwait(false); } catch { }

                    _mtx532 = api;
                    _connectedSlot = slot;
                    UpdateConnectionText();
                    Log($"532连接成功：SLOT={slot}");
                    return true;
                }
                catch (Exception ex)
                {
                    lastEx = ex;
                    try { if (api != null) await api.DisposeAsync().ConfigureAwait(false); } catch { }
                }
            }

            if (lastEx != null)
                Log($"532连接失败：{lastEx.Message}");

            return false;
        }

        internal async Task<(double? MeasuredVoltage, double? MeasuredCurrentMa, double? MeasuredValue, string Result)> MeasurePointAsync(string sensorName, string expectedValueText, string aoChannel, double targetCurrentMa)
        {
            if (!IsManualTestRunning && !IsAutoTestRunning)
            {
                Log("请先启动手动测试，再进行采集判定。");
                return (null, null, null, "--");
            }

            await _opLock.WaitAsync().ConfigureAwait(false);
            try
            {
                var token = _cts?.Token ?? CancellationToken.None;
                var ok532 = await EnsureMtx532ReadyAsync(token).ConfigureAwait(false);
                var ok429 = await EnsureArincRxReadyAsync(token).ConfigureAwait(false);
                UpdateConnectionText();
                if (!ok532 || !ok429)
                {
                    Log("532未就绪：无法下发AO电压");
                    return (null, null, null, "FAIL");
                }

                var isConcentration = string.Equals(sensorName, "氧气浓度", StringComparison.OrdinalIgnoreCase);
                double effectiveTargetCurrentMa;
                var hasManual = false;
                if (isConcentration)
                    hasManual = TryParseManualCurrentMa(ManualConcentrationCurrentMaText, out effectiveTargetCurrentMa);
                else
                    hasManual = TryParseManualCurrentMa(ManualPressureCurrentMaText, out effectiveTargetCurrentMa);

                if (!hasManual)
                    effectiveTargetCurrentMa = targetCurrentMa;

                var targetVoltageV = CurrentMaToVoltageV(effectiveTargetCurrentMa);
                if (hasManual)
                    Log($"下发(手动电流)：{aoChannel}={targetVoltageV.ToString("F4", CultureInfo.InvariantCulture)}V -> 电流={effectiveTargetCurrentMa.ToString("F3", CultureInfo.InvariantCulture)}mA");
                else
                    Log($"下发：{aoChannel}={targetVoltageV.ToString("F4", CultureInfo.InvariantCulture)}V -> 目标电流={effectiveTargetCurrentMa.ToString("F3", CultureInfo.InvariantCulture)}mA");

                // 使用持续输出模式，避免一次性写入仅输出很短时间导致万用表难以稳定测到
                if (!_mtx532.IsOutputRunning)
                {
                    try { await _mtx532.StartOutputAsync(token).ConfigureAwait(false); } catch { }
                }
                await _mtx532.SetDcAsync(aoChannel, targetVoltageV, enable: true, cancellationToken: token).ConfigureAwait(false);

                double back;
                try
                {
                    back = await _mtx532.GetLastOutputVoltageAsync(aoChannel, token).ConfigureAwait(false);
                    Log($"532回读(缓存)：{aoChannel}={back.ToString("F4", CultureInfo.InvariantCulture)}V");
                }
                catch
                {
                    back = targetVoltageV;
                }

                await Task.Delay(AoSettleDelayMs, token).ConfigureAwait(false);

                var currentLabel = isConcentration ? OxygenConcentrationCurrentLabelRxDec : OxygenPressureCurrentLabelRxDec;
                var valueLabel = isConcentration ? OxygenConcentrationValueLabelRxDec : OxygenPressureValueLabelRxDec;
                var valueResolution = isConcentration ? OxygenConcentrationResolutionPercent : OxygenPressureResolutionPsi;

                // 点击采集后：清缓存 -> 固定延迟1秒 -> 取最新数据判定
                ClearCachesForLabels(currentLabel, valueLabel);
                await Task.Delay(1000, token).ConfigureAwait(false);

                // 第1点(0mA)与第5点(25mA)：只判电流，不判值，也不等待值Label。
                var onlyJudgeCurrent = Math.Abs(effectiveTargetCurrentMa - 0.0) < 1e-9 || Math.Abs(effectiveTargetCurrentMa - 25.0) < 1e-9;

                double? measuredCurrent;
                double? measuredValue;

                if (onlyJudgeCurrent)
                {
                    var (cur, _) = await MeasureOxygenCurrentVia429Async(currentLabel, token).ConfigureAwait(false);
                    measuredCurrent = cur;
                    measuredValue = null;
                    if (!measuredCurrent.HasValue)
                    {
                        Log($"采集超时：未获取到电流数据（电流Label={currentLabel} SDI={ArincExpectedSdi}）");
                        return (back, measuredCurrent, measuredValue, "FAIL");
                    }
                }
                else
                {
                    var (cur, val, _) = await MeasureOxygenVia429Async(
                        currentLabel: currentLabel,
                        currentResolution: OxygenCurrentResolutionMa,
                        valueLabel: valueLabel,
                        valueResolution: valueResolution,
                        token: token).ConfigureAwait(false);
                    measuredCurrent = cur;
                    measuredValue = val;

                    if (!measuredCurrent.HasValue || !measuredValue.HasValue)
                    {
                        Log($"采集超时：未同时获取到两项数据（电流Label={currentLabel} 值Label={valueLabel} SDI={ArincExpectedSdi}）");
                        return (back, measuredCurrent, measuredValue, "FAIL");
                    }
                }

                var currentTol = isConcentration ? ConcentrationCurrentToleranceMa : PressureCurrentToleranceMa;
                var currentOk = Math.Abs(measuredCurrent.Value - effectiveTargetCurrentMa) <= currentTol;

                var valueOk = true;
                if (!onlyJudgeCurrent && TryParseExpectedTolerance(expectedValueText, out var expected, out var tol))
                {
                    valueOk = Math.Abs(measuredValue.Value - expected) <= tol;
                }

                var pass = currentOk && valueOk;
                Log($"判定：电流 {measuredCurrent.Value:F4}mA 目标 {effectiveTargetCurrentMa:F3}±{currentTol:F3} => {(currentOk ? "OK" : "NG")}");
                if (!onlyJudgeCurrent && TryParseExpectedTolerance(expectedValueText, out var e2, out var t2))
                {
                    Log($"判定：值 {measuredValue.Value:F4} 目标 {e2:F4}±{t2:F4} => {(valueOk ? "OK" : "NG")}");
                }
                else
                {
                    Log("判定：值(无判据) => 跳过");
                }

                return (back, measuredCurrent, measuredValue, pass ? "PASS" : "FAIL");
            }
            catch (OperationCanceledException)
            {
                return (null, null, null, "--");
            }
            catch (Exception ex)
            {
                Log($"采集异常：{ex.Message}");
                return (null, null, null, "FAIL");
            }
            finally
            {
                _opLock.Release();
            }
        }

        private void Log(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            PostToUi(() => Logs.Add($"[{DateTime.Now:HH:mm:ss}] {message}"));
        }

        private static string EscapeCsvField(string field)
        {
            if (string.IsNullOrEmpty(field))
                return string.Empty;

            if (field.Contains(",") || field.Contains("\"") || field.Contains("\n") || field.Contains("\r"))
                return "\"" + field.Replace("\"", "\"\"") + "\"";

            return field;
        }

        private string ExportAutoTestReportToCsv()
        {
            var dir = @"C:\excel";
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var filePath = Path.Combine(dir, $"氧气传感器信号采集_自动测试_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

            using (var writer = new StreamWriter(filePath, append: false, encoding: new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)))
            {
                writer.WriteLine("时间,最后结果,序号,类型,AO通道,目标电流(mA),目标电压(V),期望值,实测电流,实测值,判定");

                var testTime = LastTestTime;
                var testResult = LastTestResult;
                foreach (var item in Items)
                {
                    writer.WriteLine(string.Join(",",
                        EscapeCsvField(testTime),
                        EscapeCsvField(testResult),
                        EscapeCsvField(item.IndexText),
                        EscapeCsvField(item.SensorShortName),
                        EscapeCsvField(item.AoChannel),
                        item.TargetCurrentMa.ToString("F3", CultureInfo.InvariantCulture),
                        EscapeCsvField(item.TargetVoltageText),
                        EscapeCsvField(item.ExpectedValueText),
                        EscapeCsvField(item.MeasuredCurrentText),
                        EscapeCsvField(item.MeasuredValueText),
                        EscapeCsvField(item.Result)));
                }
            }

            return filePath;
        }

        public void Dispose()
        {
            try { _cts?.Cancel(); } catch { }
            try { _cts?.Dispose(); } catch { }
            try { _opLock?.Dispose(); } catch { }
        }

        public sealed class OxygenPointItemViewModel : BindableBase
        {
            private readonly OxygenSensorSignalAcquisitionTestViewModel _owner;

            private string _measuredValueText = "--";
            private string _measuredCurrentText = "--";
            private string _result = "--";

            public OxygenPointItemViewModel(
                string indexText,
                string sensorName,
                string currentText,
                double targetCurrentMa,
                string expectedValueText,
                string aoChannel,
                OxygenSensorSignalAcquisitionTestViewModel owner)
            {
                IndexText = indexText;
                SensorName = sensorName;
                CurrentText = currentText;
                TargetCurrentMa = targetCurrentMa;
                ExpectedValueText = expectedValueText;
                AoChannel = aoChannel;
                _owner = owner;
                MeasureCommand = new DelegateCommand(async () => await MeasureAsync());
            }

            public string IndexText { get; }
            public string SensorName { get; }
            public string SensorShortName
            {
                get
                {
                    if (string.Equals(SensorName, "氧气浓度", StringComparison.OrdinalIgnoreCase))
                        return "浓度";
                    if (string.Equals(SensorName, "氧气压力", StringComparison.OrdinalIgnoreCase))
                        return "压力";
                    return SensorName;
                }
            }
            public string CurrentText { get; }
            public double TargetCurrentMa { get; }
            public string ExpectedValueText { get; }
            public string AoChannel { get; }

            public bool ShowExpectedValue
            {
                get
                {
                    if (string.IsNullOrWhiteSpace(ExpectedValueText))
                        return false;
                    if (ExpectedValueText.Trim() == "--")
                        return false;
                    return true;
                }
            }

            public string ExpectedValueDisplay
            {
                get
                {
                    if (!ShowExpectedValue)
                        return string.Empty;
                    if (string.Equals(SensorName, "氧气浓度", StringComparison.OrdinalIgnoreCase))
                        return ExpectedValueText.Replace("%", "");
                    return ExpectedValueText;
                }
            }

            public string TargetVoltageText
            {
                get
                {
                    var v = CurrentMaToVoltageV(TargetCurrentMa);
                    return v.ToString("F4", CultureInfo.InvariantCulture);
                }
            }

            public bool ShowValueReadback
            {
                get
                {
                    if (Math.Abs(TargetCurrentMa - 0.0) < 1e-9)
                        return false;
                    if (Math.Abs(TargetCurrentMa - 25.0) < 1e-9)
                        return false;
                    return true;
                }
            }

            public string MeasuredCurrentText
            {
                get => _measuredCurrentText;
                private set => SetProperty(ref _measuredCurrentText, value);
            }

            public string MeasuredValueText
            {
                get => _measuredValueText;
                private set => SetProperty(ref _measuredValueText, value);
            }

            public string Result
            {
                get => _result;
                private set => SetProperty(ref _result, value);
            }

            public DelegateCommand MeasureCommand { get; }

            internal void Reset()
            {
                MeasuredValueText = "--";
                MeasuredCurrentText = "--";
                Result = "--";
            }

            public async Task MeasureAsync()
            {
                Reset();
                if (_owner == null)
                    return;

                var (voltage, current, value, result) = await _owner.MeasurePointAsync(SensorName, ExpectedValueText, AoChannel, TargetCurrentMa).ConfigureAwait(false);
                _owner.PostToUi(() =>
                {
                    MeasuredCurrentText = current.HasValue
                        ? current.Value.ToString("F4", CultureInfo.InvariantCulture)
                        : "--";

                    MeasuredValueText = value.HasValue
                        ? value.Value.ToString("F4", CultureInfo.InvariantCulture)
                        : "--";
                    Result = result;
                });

                if (_owner.IsAutoTestRunning || _owner.IsManualTestRunning)
                {
                    _owner.LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    if (_owner.Items.All(x => x.Result == "--"))
                        _owner.LastTestResult = "--";
                    else if (_owner.Items.Any(x => x.Result == "FAIL"))
                        _owner.LastTestResult = "FAIL";
                    else if (_owner.Items.All(x => x.Result == "PASS"))
                        _owner.LastTestResult = "PASS";
                }
            }
        }
    }
}
