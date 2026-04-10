using Prism.Commands;

using Prism.Mvvm;

using System;

using System.Collections.Generic;

using System.Collections.ObjectModel;

using System.Globalization;

using System.Linq;

using System.Threading;

using System.Threading.Tasks;

using MeasureControl.Models.Devices;

using MeasureControl.Models.Devices.DeviceCategories;

using MeasureControl.Services.HardwareApis;

using MeasureControl.Services;

using Prism.Ioc;
using System.Windows;



namespace MeasureControl.ViewModels.SingleBoardTest.InertController

{

    public sealed class TemperatureSensorSignalAcquisitionTestViewModel : BindableBase, IDisposable

    {

        private const string PowerSupplyIpAddress = "192.168.1.15";

        public bool SkipMainPowerOff { get; set; }

        private const double InputVoltageV = 28.0;

        private const double InputCurrentA = 3.0;

        private const string TestItemKey = "InertController_TemperatureSensorSignalAcquisition";



        private readonly ISingleBoardTestContextService _singleBoardTestContext;

        private readonly SynchronizationContext _uiContext;

        private readonly Prism.Events.IEventAggregator _eventAggregator;

        private readonly IComponentPowerStateApi _componentPowerStateApi;

        private readonly SemaphoreSlim _opLock = new SemaphoreSlim(1, 1);

        private readonly SemaphoreSlim _arincRxReadLock = new SemaphoreSlim(1, 1);

        private readonly object _telemetryCacheLock = new object();

        private CancellationTokenSource _cts;



        private bool _isManualTestRunning;

        private bool _isAutoTestRunning;

        private string _lastTestTime = "--";

        private string _lastTestResult = "--";

        private string _manualResistancePt500AText;

        private string _manualResistancePt500BText;

        private string _manualResistancePt1000AText;

        private string _manualResistancePt1000BText;



        private IPowerSupplyApi _power;

        private bool _isPowerOn;

        private string _powerStatus = "鏈緵鐢?;



        private IMtx532Api _mtx532;

        private int? _connectedSlot;



        private IPxi7012Api _resistor;

        private uint? _connectedLogicalId;



        private IArt4229Api _arinc;

        private Task _arincRxLoopTask;

        private bool _arincTxOpened;

        private long _lastLabel158LogTicks;

        private long _lastLabel238LogTicks;

        private readonly Dictionary<string, (double value, DateTime timestampUtc)> _lastResistanceOhmBySensor = new Dictionary<string, (double value, DateTime timestampUtc)>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, (double value, DateTime timestampUtc)> _lastTemperatureCBySensor = new Dictionary<string, (double value, DateTime timestampUtc)>(StringComparer.OrdinalIgnoreCase);

        // 淇濆瓨姣忎釜鐐逛綅姣忎釜浼犳劅鍣ㄧ殑娴嬭瘯缁撴灉锛岀敤浜庢姤琛ㄧ敓鎴?        // Key: pointIndex (1-4), Value: Dictionary<sensorName, (measuredTemp, result)>
        private readonly Dictionary<int, Dictionary<string, (string measuredTemp, string result)>> _pointTestResults = new Dictionary<int, Dictionary<string, (string measuredTemp, string result)>>();

        /// <summary>
        /// 鑾峰彇鎸囧畾鐐逛綅鎸囧畾浼犳劅鍣ㄧ殑娴嬭瘯缁撴灉锛堢敤浜庢姤琛ㄧ敓鎴愶級
        /// </summary>
        public (string measuredTemp, string result) GetPointTestResult(int pointIndex, string sensorName)
        {
            if (_pointTestResults.TryGetValue(pointIndex, out var sensorResults))
            {
                if (sensorResults.TryGetValue(sensorName, out var result))
                {
                    return result;
                }
            }
            return ("--", "--");
        }

        /// <summary>
        /// 淇濆瓨褰撳墠鐐逛綅鐨勬祴璇曠粨鏋?        /// </summary>
        private void SaveCurrentPointTestResults(int pointIndex)
        {
            var sensorResults = new Dictionary<string, (string measuredTemp, string result)>(StringComparer.OrdinalIgnoreCase);
            foreach (var sensor in SensorItems)
            {
                sensorResults[sensor.SensorName] = (sensor.MeasuredTemperatureText ?? "--", sensor.TemperatureResultText ?? "--");
            }
            _pointTestResults[pointIndex] = sensorResults;
        }

        /// <summary>
        /// 娓呴櫎鎵€鏈夌偣浣嶇殑娴嬭瘯缁撴灉
        /// </summary>
        private void ClearAllPointTestResults()
        {
            _pointTestResults.Clear();
        }



        public TemperatureSensorSignalAcquisitionTestViewModel(

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



            MeasureCommand = new DelegateCommand(async () => await OnMeasureAsync());


            ApplySelectedPointCommand = new DelegateCommand(async () => await ApplyPointAsync(SelectedPointIndex));



            SensorItems.Add(new SensorItemViewModel("PT500A", "J52銆丣53銆丣56", aoChannel: "AO5-AO6", roChannel: "--"));

            SensorItems.Add(new SensorItemViewModel("PT500B", "J61銆丣62銆丣63", aoChannel: "--", roChannel: "RO2"));

            SensorItems.Add(new SensorItemViewModel("PT1000A", "J54銆丣55銆丣57", aoChannel: "AO3-AO4", roChannel: "--"));

            SensorItems.Add(new SensorItemViewModel("PT1000B", "J28銆丣29銆丣97", aoChannel: "--", roChannel: "RO1"));



            SelectedPointIndex = 1;

        }



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



        public string ManualResistancePt500AText

        {

            get => _manualResistancePt500AText;

            set => SetProperty(ref _manualResistancePt500AText, value);

        }



        public string ManualResistancePt500BText

        {

            get => _manualResistancePt500BText;

            set => SetProperty(ref _manualResistancePt500BText, value);

        }



        public string ManualResistancePt1000AText

        {

            get => _manualResistancePt1000AText;

            set => SetProperty(ref _manualResistancePt1000AText, value);

        }



        public string ManualResistancePt1000BText

        {

            get => _manualResistancePt1000BText;

            set => SetProperty(ref _manualResistancePt1000BText, value);

        }



        private static bool TryParseManualResistance(string text, out double value)

        {

            value = 0;

            if (string.IsNullOrWhiteSpace(text))

                return false;

            if (!double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value))

                return false;

            if (double.IsNaN(value) || double.IsInfinity(value))

                return false;

            return true;

        }



        private void ApplyManualResistancesIfAny(Dictionary<string, double> targets)

        {

            if (targets == null)

                return;

            if (TryParseManualResistance(ManualResistancePt500AText, out var pt500a))

                targets["PT500A"] = pt500a;

            if (TryParseManualResistance(ManualResistancePt500BText, out var pt500b))

                targets["PT500B"] = pt500b;

            if (TryParseManualResistance(ManualResistancePt1000AText, out var pt1000a))

                targets["PT1000A"] = pt1000a;

            if (TryParseManualResistance(ManualResistancePt1000BText, out var pt1000b))

                targets["PT1000B"] = pt1000b;

        }



        private static Dictionary<string, double> GetTargetTemperaturesForPoint(int pointIndex)

        {

            var temp = pointIndex switch

            {

                1 => -70.0,

                2 => 0.0,

                3 => 112.0,

                _ => 176.0,

            };

            return new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)

            {

                ["PT500A"] = temp,

                ["PT500B"] = temp,

                ["PT1000A"] = temp,

                ["PT1000B"] = temp,

            };

        }



        private const int MeasureTimeoutMs = 3000;

        private const int AutoMeasureTimeoutMs = 4000;

        private const int MeasurementStabilizeDelayMs = 3000;

        private const int AutoMeasurementStabilizeDelayMs = 3000;

        private const double ResistanceToleranceOhm = 1.0;

        private const double MinimumResistanceChangeOhm = 10.0;

        private const double TemperatureToleranceC = 1.5;



        private async Task MeasureInternalAsync(CancellationToken token, int timeoutMs, int? pointIndexForReport = null)

        {

            if (timeoutMs <= 0)

                timeoutMs = MeasureTimeoutMs;

            var measurementStartUtc = await PrepareForFreshMeasurementAsync(token).ConfigureAwait(false);

            var deadline = measurementStartUtc.AddMilliseconds(timeoutMs);

            Dictionary<string, (double resistanceOhm, double temperatureC)> freshTelemetry = null;

            while (!token.IsCancellationRequested && DateTime.UtcNow <= deadline)

            {

                var currentTelemetry = new Dictionary<string, (double resistanceOhm, double temperatureC)>(StringComparer.OrdinalIgnoreCase);

                var allOk = true;

                foreach (var item in SensorItems)

                {

                    if (!TryGetFreshTelemetry(item.SensorName, measurementStartUtc, out var resistanceOhm, out var temperatureC))

                    {

                        allOk = false;

                        break;

                    }

                    currentTelemetry[item.SensorName] = (resistanceOhm, temperatureC);

                }

                if (allOk)

                {

                    freshTelemetry = currentTelemetry;

                    break;

                }

                await Task.Delay(10, token).ConfigureAwait(false);

            }

            var missing = SensorItems

                .Where(x => freshTelemetry == null || !freshTelemetry.ContainsKey(x.SensorName))

                .Select(x => x.SensorName)

                .ToList();

            PostToUi(() =>

            {

                foreach (var item in SensorItems)

                {

                    if (freshTelemetry != null && freshTelemetry.TryGetValue(item.SensorName, out var measured))

                    {

                        item.MeasuredTemperatureC = measured.temperatureC;

                    }

                }

            });

            var failItems = new List<string>();

            // 鐢ㄤ簬淇濆瓨褰撳墠鐐逛綅鐨勬祴璇曠粨鏋滐紙鐢ㄤ簬鎶ヨ〃鐢熸垚锛?            var pointResultsForReport = new Dictionary<string, (string measuredTemp, string result)>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in SensorItems)

            {

                var sensorItem = item;

                if (missing.Contains(sensorItem.SensorName))

                {

                    var itemToUpdate = sensorItem;

                    PostToUi(() =>

                    {

                        itemToUpdate.TemperatureResultText = "FAIL";

                    });

                    failItems.Add($"{sensorItem.SensorName}(鏈噰闆嗗埌鏁版嵁)");

                    Log($"鍒ゅ畾锛歿sensorItem.SensorName} 鏈噰闆嗗埌鏁版嵁 => FAIL");

                    // 淇濆瓨澶辫触缁撴灉鐢ㄤ簬鎶ヨ〃
                    pointResultsForReport[sensorItem.SensorName] = ("--", "FAIL");

                    continue;

                }

                var measured = freshTelemetry[sensorItem.SensorName];

                var t = measured.temperatureC.ToString("F3", CultureInfo.InvariantCulture);

                Log($"鍥為噰锛歿sensorItem.SensorName} 娓╁害={t}鈩?);

                var tempTarget = sensorItem.TargetTemperatureC;

                var tempActual = measured.temperatureC;

                var tempDiff = Math.Abs(tempActual - tempTarget);

                var tempPass = !double.IsNaN(tempActual) && tempDiff <= TemperatureToleranceC;

                var tempResult = tempPass ? "PASS" : "FAIL";

                var itemForTemp = sensorItem;

                PostToUi(() => itemForTemp.TemperatureResultText = tempResult);

                // 淇濆瓨娴嬭瘯缁撴灉鐢ㄤ簬鎶ヨ〃
                pointResultsForReport[sensorItem.SensorName] = (t, tempResult);

                if (!tempPass)

                {

                    failItems.Add($"{sensorItem.SensorName}(鐩爣娓╁害{tempTarget:F3}鈩?鍥為噰{tempActual:F3}鈩?宸€納tempDiff:F3}鈩?瀹瑰樊卤{TemperatureToleranceC:F1}鈩?");

                }

                Log($"娓╁害鍒ゅ畾锛歿sensorItem.SensorName} 鐩爣={tempTarget:F3}鈩?瀹為檯={tempActual:F3}鈩?宸€?{tempDiff:F3}鈩?瀹瑰樊=卤{TemperatureToleranceC:F1}鈩?=> {tempResult}");

            }

            // 濡傛灉鎸囧畾浜嗙偣浣嶇储寮曪紝淇濆瓨娴嬭瘯缁撴灉鐢ㄤ簬鎶ヨ〃鐢熸垚
            if (pointIndexForReport.HasValue)
            {
                _pointTestResults[pointIndexForReport.Value] = pointResultsForReport;
            }

            LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            if (failItems.Count == 0)

            {

                LastTestResult = "PASS";

                Log("閲囬泦鍒ゅ畾锛歅ASS");

            }

            else

            {

                LastTestResult = "FAIL";

                Log($"閲囬泦鍒ゅ畾锛欶AIL锛岃秴宸細{string.Join("; ", failItems)}锛岀數闃诲宸?卤{ResistanceToleranceOhm:F2}惟锛屾俯搴﹀宸?卤{TemperatureToleranceC:F1}鈩?);

            }

        }

        private async Task OnMeasureAsync()

        {

            if (!IsManualTestRunning && !IsAutoTestRunning)

            {

                Log("璇峰厛鍚姩鎵嬪姩娴嬭瘯锛屽啀杩涜閲囬泦鍒ゅ畾銆?);

                return;

            }

            await _opLock.WaitAsync().ConfigureAwait(false);

            try

            {

                if (_cts == null)

                    _cts = new CancellationTokenSource();

                var token = _cts.Token;

                ClearMeasuredTelemetryOnUi();

                Log("鎵嬪姩閲囬泦锛氱瓑寰?绉掑悗閲囬泦鏈€鏂版暟鎹?..");

                await Task.Delay(3000, token).ConfigureAwait(false);

                await MeasureInternalAsync(token, MeasureTimeoutMs).ConfigureAwait(false);

            }

            catch (OperationCanceledException)

            {

            }

            catch (Exception ex)

            {

                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                LastTestResult = "FAIL";

                Log($"閲囬泦鍒ゅ畾寮傚父锛歿ex.Message}");

            }

            finally

            {

                _opLock.Release();

            }

        }

        private void ClearMeasuredTelemetryOnUi()

        {

            PostToUi(() =>

            {

                foreach (var item in SensorItems)

                {

                    item.MeasuredTemperatureC = null;

                    item.TemperatureResultText = "--";

                }

            });

        }

        private async Task<DateTime> PrepareForFreshMeasurementAsync(CancellationToken token)

        {

            await EnsureArincRxReadyAsync(token).ConfigureAwait(false);

            await FlushArincRxBufferAsync(token).ConfigureAwait(false);

            StartArincRxLoopIfNeeded(token);

            var delayMs = IsAutoTestRunning ? AutoMeasurementStabilizeDelayMs : MeasurementStabilizeDelayMs;

            await Task.Delay(delayMs, token).ConfigureAwait(false);

            return DateTime.UtcNow;

        }

        private bool TryGetFreshTelemetry(string sensorName, DateTime measurementStartUtc, out double resistanceOhm, out double temperatureC)

        {

            resistanceOhm = default;

            temperatureC = default;

            lock (_telemetryCacheLock)

            {

                if (!_lastResistanceOhmBySensor.TryGetValue(sensorName, out var resistance) || resistance.timestampUtc < measurementStartUtc)

                    return false;

                if (!_lastTemperatureCBySensor.TryGetValue(sensorName, out var temperature) || temperature.timestampUtc < measurementStartUtc)

                    return false;

                resistanceOhm = resistance.value;

                temperatureC = temperature.value;

                return true;

            }

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

        public ObservableCollection<SensorItemViewModel> SensorItems { get; } = new ObservableCollection<SensorItemViewModel>();

        public DelegateCommand ManualTestCommand { get; }

        public DelegateCommand AutoTestCommand { get; }

        public DelegateCommand ClearLogCommand { get; }

        public DelegateCommand MeasureCommand { get; }

        public bool CanMeasure => IsManualTestRunning || IsAutoTestRunning;

        public DelegateCommand ApplySelectedPointCommand { get; }

        public bool IsManualTestRunning

        {

            get => _isManualTestRunning;

            private set

            {

                if (SetProperty(ref _isManualTestRunning, value))

                {

                    RaisePropertyChanged(nameof(CanMeasure));

                }

            }

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

        private int _selectedPointIndex;

        public int SelectedPointIndex

        {

            get => _selectedPointIndex;

            set

            {

                if (SetProperty(ref _selectedPointIndex, value))

                {

                    UpdatePreviewResistances();

                    RaisePropertyChanged(nameof(CurrentPointTargetTemperatureText));

                }

            }

        }

        private string _connectionText = "532: 鏈繛鎺?| 7012: 鏈繛鎺?;

        public string ConnectionText

        {

            get => _connectionText;

            private set => SetProperty(ref _connectionText, value);

        }

        private void UpdateConnectionText()

        {

            var mtx = _mtx532 != null && _mtx532.IsConnected

                ? $"532: 宸茶繛鎺?SLOT={_connectedSlot})"

                : "532: 鏈繛鎺?;

            var r = _resistor != null && _resistor.IsConnected

                ? $"7012: 宸茶繛鎺?閫昏緫ID={_connectedLogicalId})"

                : "7012: 鏈繛鎺?;

            var p = IsPowerOn ? PowerStatus : "鏈緵鐢?;

            ConnectionText = $"鐢垫簮:{p} | {mtx} | {r}";

        }

        private async Task OnManualTestAsync()

        {

            if (IsManualTestRunning)

            {

                await StopAsync().ConfigureAwait(false);

                return;

            }

            // 妫€鏌ユ槸鍚﹀凡鎬讳笂鐢?            var _hps = ContainerLocator.Container.Resolve<IHydraulicPowerService>();
            if (_hps == null || !_hps.IsHydraulicPowered)
            {
                MessageBox.Show("璇峰厛鐐瑰嚮宸︿笂瑙掔粍浠朵笂鐢垫寜閽繘琛屾€讳笂鐢碉紝鍐嶈繘琛屾祴璇曘€?, "鎻愮ず", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (IsAutoTestRunning)

            {

                await StopAsync().ConfigureAwait(false);

            }

            var lockTaken = false;

            await _opLock.WaitAsync().ConfigureAwait(false);

            lockTaken = true;

            try

            {

                _cts?.Cancel();

                _cts?.Dispose();

                _cts = new CancellationTokenSource();

                IsManualTestRunning = true;

                IsAutoTestRunning = false;

                PublishNavigationLock(isLocked: true, source: "TemperatureSensor");

                LastTestTime = "--";

                LastTestResult = "--";

                Log("寮€濮嬫墜鍔ㄦ祴璇曪紙娓╁害浼犳劅鍣ㄤ俊鍙烽噰闆嗭級锛氬噯澶囪繛鎺?32妯℃嫙閲忚緭鍑烘澘鍗?+ 7012鐢甸樆杈撳嚭鏉垮崱");

                await EnsurePowerAsync(_cts.Token).ConfigureAwait(false);

                var ok532 = await EnsureMtx532ReadyAsync(_cts.Token).ConfigureAwait(false);

                var ok7012 = await EnsureResistorReadyAsync(_cts.Token).ConfigureAwait(false);

                UpdateConnectionText();

                if (!ok532)

                {

                    Log("532杩炴帴澶辫触锛氳妫€鏌ユ澘鍗?椹卞姩/鏈虹閰嶇疆");

                }

                if (!ok7012)

                {

                    Log("7012杩炴帴澶辫触锛氳妫€鏌ユ澘鍗?椹卞姩/閫昏緫ID");

                }

                if (!ok532 || !ok7012)

                {

                    await StopAsync().ConfigureAwait(false);

                    return;

                }

                UpdatePreviewResistances();

                Log("宸插氨缁細PT500A/PT1000A 鐢?32杈撳嚭鐢靛帇(V=R*0.001A)锛孭T1000B/PT500B 鐢?012杈撳嚭鐢甸樆(resout1/resout2)锛涢€氳閲囬泦宸叉帴鍏?ART4229/100kbps/濂囨牎楠?10ms杞)");

                await EnsureArincRxReadyAsync(_cts.Token).ConfigureAwait(false);

                StartArincRxLoopIfNeeded(_cts.Token);

            }

            catch (OperationCanceledException)

            {

            }

            catch (Exception ex)

            {

                Log($"鎵嬪姩娴嬭瘯鍒濆鍖栧紓甯革細{ex.Message}");

                await StopAsync().ConfigureAwait(false);

            }

            finally

            {

                if (lockTaken)

                {

                    _opLock.Release();

                    lockTaken = false;

                }

            }

        }

        public async Task<string> RunOnceAsync(CancellationToken cancellationToken)

        {

            if (IsAutoTestRunning || IsManualTestRunning)

            {

                return LastTestResult;

            }

            var lockTaken = false;

            await _opLock.WaitAsync(cancellationToken).ConfigureAwait(false);

            lockTaken = true;

            try

            {

                _cts?.Cancel();

                _cts?.Dispose();

                _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

                IsAutoTestRunning = true;

                IsManualTestRunning = false;

                PublishNavigationLock(isLocked: true, source: "TemperatureSensor");

                LastTestTime = "--";

                LastTestResult = "--";

                Log("寮€濮嬭嚜鍔ㄦ祴璇曪細渚濇娴嬭瘯鐐逛綅1~4锛堟瘡涓偣浣嶄笅鍙?鍥為噰+鍒ゅ畾锛?);

                ClearAllPointTestResults();

                await EnsurePowerAsync(_cts.Token).ConfigureAwait(false);

                var ok532 = await EnsureMtx532ReadyAsync(_cts.Token).ConfigureAwait(false);

                var ok7012 = await EnsureResistorReadyAsync(_cts.Token).ConfigureAwait(false);

                UpdateConnectionText();

                if (!ok532 || !ok7012)

                {

                    Log("鏉垮崱杩炴帴澶辫触锛氳嚜鍔ㄦ祴璇曠粓姝?);

                    LastTestResult = "FAIL";

                    LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                    return "FAIL";

                }

                _cts.Token.ThrowIfCancellationRequested();

                var allPointsPass = true;

                for (var pointIndex = 1; pointIndex <= 4; pointIndex++)

                {

                    _cts.Token.ThrowIfCancellationRequested();

                    PostToUi(() => { SelectedPointIndex = pointIndex; });

                    Log($"鑷姩娴嬭瘯-寮€濮嬬偣浣峽pointIndex}");

                    ClearMeasuredTelemetryOnUi();

                    await ApplyPointInternalAsync(pointIndex, _cts.Token).ConfigureAwait(false);

                    await MeasureInternalAsync(_cts.Token, AutoMeasureTimeoutMs, pointIndex).ConfigureAwait(false);

                    var pointPass = string.Equals(LastTestResult, "PASS", StringComparison.OrdinalIgnoreCase);

                    if (!pointPass)

                        allPointsPass = false;

                    Log($"鑷姩娴嬭瘯-缁撴潫鐐逛綅{pointIndex}锛歿(pointPass ? "PASS" : "FAIL")}");

                    await Task.Delay(200, _cts.Token).ConfigureAwait(false);

                }

                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                LastTestResult = allPointsPass ? "PASS" : "FAIL";

                Log($"鑷姩娴嬭瘯缁撴潫锛氱偣浣?~4瀹屾垚锛屾€讳綋={(allPointsPass ? "PASS" : "FAIL")}");

                return LastTestResult;

            }

            catch (OperationCanceledException)

            {

                return "FAIL";

            }

            catch (Exception ex)

            {

                LastTestResult = "FAIL";

                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                Log($"鑷姩娴嬭瘯寮傚父锛歿ex.Message}");

                return "FAIL";

            }

            finally

            {

                if (lockTaken)

                {

                    _opLock.Release();

                    lockTaken = false;

                }

                try { await StopAsync().ConfigureAwait(false); } catch { }

            }

        }

        private async Task OnAutoTestAsync()

        {

            if (IsAutoTestRunning)

            {

                await StopAsync().ConfigureAwait(false);

                return;

            }

            // 妫€鏌ユ槸鍚﹀凡鎬讳笂鐢?            var _hps = ContainerLocator.Container.Resolve<IHydraulicPowerService>();
            if (_hps == null || !_hps.IsHydraulicPowered)
            {
                MessageBox.Show("璇峰厛鐐瑰嚮宸︿笂瑙掔粍浠朵笂鐢垫寜閽繘琛屾€讳笂鐢碉紝鍐嶈繘琛屾祴璇曘€?, "鎻愮ず", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (IsManualTestRunning)

            {

                await StopAsync().ConfigureAwait(false);

            }

            var lockTaken = false;

            await _opLock.WaitAsync().ConfigureAwait(false);

            lockTaken = true;

            try

            {

                _cts?.Cancel();

                _cts?.Dispose();

                _cts = new CancellationTokenSource();

                IsAutoTestRunning = true;

                IsManualTestRunning = false;

                PublishNavigationLock(isLocked: true, source: "TemperatureSensor");

                LastTestTime = "--";

                LastTestResult = "--";

                Log("寮€濮嬭嚜鍔ㄦ祴璇曪細渚濇娴嬭瘯鐐逛綅1~4锛堟瘡涓偣浣嶄笅鍙?鍥為噰+鍒ゅ畾锛?);

                ClearAllPointTestResults();

                await EnsurePowerAsync(_cts.Token).ConfigureAwait(false);

                var ok532 = await EnsureMtx532ReadyAsync(_cts.Token).ConfigureAwait(false);

                var ok7012 = await EnsureResistorReadyAsync(_cts.Token).ConfigureAwait(false);

                UpdateConnectionText();

                if (!ok532 || !ok7012)

                {

                    Log("鏉垮崱杩炴帴澶辫触锛氳嚜鍔ㄦ祴璇曠粓姝?);

                    LastTestResult = "FAIL";

                    LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                    return;

                }

                _cts.Token.ThrowIfCancellationRequested();

                var allPointsPass = true;

                for (var pointIndex = 1; pointIndex <= 4; pointIndex++)

                {

                    _cts.Token.ThrowIfCancellationRequested();

                    PostToUi(() => { SelectedPointIndex = pointIndex; });

                    Log($"鑷姩娴嬭瘯-寮€濮嬬偣浣峽pointIndex}");

                    ClearMeasuredTelemetryOnUi();

                    await ApplyPointInternalAsync(pointIndex, _cts.Token).ConfigureAwait(false);

                    await MeasureInternalAsync(_cts.Token, AutoMeasureTimeoutMs, pointIndex).ConfigureAwait(false);

                    var pointPass = string.Equals(LastTestResult, "PASS", StringComparison.OrdinalIgnoreCase);

                    if (!pointPass)

                        allPointsPass = false;

                    Log($"鑷姩娴嬭瘯-缁撴潫鐐逛綅{pointIndex}锛歿(pointPass ? "PASS" : "FAIL")}");

                    await Task.Delay(200, _cts.Token).ConfigureAwait(false);

                }

                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                LastTestResult = allPointsPass ? "PASS" : "FAIL";

                Log($"鑷姩娴嬭瘯缁撴潫锛氱偣浣?~4瀹屾垚锛屾€讳綋={(allPointsPass ? "PASS" : "FAIL")}");

            }

            catch (OperationCanceledException)

            {

            }

            catch (Exception ex)

            {

                LastTestResult = "FAIL";

                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                Log($"鑷姩娴嬭瘯寮傚父锛歿ex.Message}");

            }

            finally

            {

                if (lockTaken)

                {

                    _opLock.Release();

                    lockTaken = false;

                }

                try { await StopAsync().ConfigureAwait(false); } catch { }

            }

        }

        private async Task StopAsync()

        {

            await _opLock.WaitAsync().ConfigureAwait(false);

            try

            {

                try { _cts?.Cancel(); } catch { }

                await DisablePowerAsync(CancellationToken.None).ConfigureAwait(false);

                if (_mtx532 != null)

                {

                    try { await _mtx532.StopOutputAsync(CancellationToken.None).ConfigureAwait(false); } catch { }

                    try { await _mtx532.ResetAllToZeroAsync(disableAfterReset: true, cancellationToken: CancellationToken.None).ConfigureAwait(false); } catch { }

                    try { await _mtx532.DisconnectAsync(CancellationToken.None).ConfigureAwait(false); } catch { }

                    try

                    {

                        await _mtx532.DisposeAsync().ConfigureAwait(false);

                    }

                    catch

                    {

                    }

                    finally

                    {

                        _mtx532 = null;

                    }

                }

                if (_resistor != null)

                {

                    try { await _resistor.DisconnectAsync(CancellationToken.None).ConfigureAwait(false); } catch { }

                    try { await _resistor.DisposeAsync().ConfigureAwait(false); } catch { }

                    _resistor = null;

                }

                if (_arinc != null)

                {

                    await CloseArincAsync().ConfigureAwait(false);

                }

                if (_power != null)

                {

                    try { await _power.DisconnectAsync(CancellationToken.None).ConfigureAwait(false); } catch { }

                    try { await _power.DisposeAsync().ConfigureAwait(false); } catch { }

                    _power = null;

                }

                _connectedSlot = null;

                _connectedLogicalId = null;

                UpdateConnectionText();

                IsManualTestRunning = false;

                IsAutoTestRunning = false;

                PublishNavigationLock(isLocked: false, source: "TemperatureSensor");

                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            }

            finally

            {

                _opLock.Release();

            }

        }

        private async Task EnsurePowerAsync(CancellationToken token)

        {

            // 192.168.1.15 CH1 涓嶅啀鐢辨湰娴嬭瘯鎺у埗涓婄數锛岀敱鎬讳笂鐢电粺涓€绠＄悊

            await Task.Delay(100, token).ConfigureAwait(false);

            PostToUi(() =>

            {

                IsPowerOn = true;

                PowerStatus = $"宸蹭緵鐢?CH1 {InputVoltageV:0.###}V)";

                UpdateConnectionText();

            });

        }

        private async Task DisablePowerAsync(CancellationToken token)

        {

            try

            {

                if (_power == null)

                    return;

                if (!_power.IsConnected)

                    await _power.ConnectAsync(PowerSupplyIpAddress, token).ConfigureAwait(false);

                // 192.168.1.15 CH1 涓嶅啀鐢辨湰娴嬭瘯鎺у埗涓嬬數
                foreach (var ch in Enum.GetValues(typeof(PowerSupplyChannel)).Cast<PowerSupplyChannel>())

                {

                    if (ch == PowerSupplyChannel.CH1)
                        continue;

                    try { await _power.SetOutputEnabledAsync(ch, false, token).ConfigureAwait(false); } catch { }

                }

                await Task.Delay(200, token).ConfigureAwait(false);

            }

            catch

            {

            }

            finally

            {

                PostToUi(() =>

                {

                    if (!SkipMainPowerOff)
                    {
                        IsPowerOn = false;

                        PowerStatus = "鏈緵鐢?;
                    }

                    UpdateConnectionText();

                });

            }

        }

        private async Task ApplyPointAsync(int pointIndex)

        {

            if (pointIndex < 1 || pointIndex > 4)

                return;

            if (!IsManualTestRunning && !IsAutoTestRunning)

            {

                Log("璇峰厛鍚姩鎵嬪姩娴嬭瘯锛屼互杩炴帴532鏉垮崱銆?);

                return;

            }

            await _opLock.WaitAsync().ConfigureAwait(false);

            try

            {

                if (_cts == null)

                    _cts = new CancellationTokenSource();

                var ok532 = await EnsureMtx532ReadyAsync(_cts.Token).ConfigureAwait(false);

                var ok7012 = await EnsureResistorReadyAsync(_cts.Token).ConfigureAwait(false);

                UpdateConnectionText();

                if (!ok532)

                {

                    Log("532鏈氨缁細鏃犳硶涓嬪彂AO鐢靛帇");

                    return;

                }

                if (!ok7012)

                {

                    Log("7012鏈氨缁細鏃犳硶涓嬪彂鐢甸樆鍊?);

                    return;

                }

                ClearMeasuredTelemetryOnUi();

                await ApplyPointInternalAsync(pointIndex, _cts.Token).ConfigureAwait(false);

                await MeasureInternalAsync(_cts.Token, MeasureTimeoutMs).ConfigureAwait(false);

            }

            catch (Exception ex)

            {

                Log($"璁剧疆鐐逛綅{pointIndex}寮傚父锛歿ex.Message}");

            }

            finally

            {

                _opLock.Release();

            }

        }

        private async Task ApplyPointInternalAsync(int pointIndex, CancellationToken token)

        {

            token.ThrowIfCancellationRequested();

            var targets = GetTargetsForPoint(pointIndex);

            ApplyManualResistancesIfAny(targets);

            var tempTargets = GetTargetTemperaturesForPoint(pointIndex);

            PostToUi(() =>

            {

                foreach (var item in SensorItems)

                {

                    if (targets.TryGetValue(item.SensorName, out var ohm))

                    {

                        item.TargetResistanceOhm = ohm;

                    }

                    if (tempTargets.TryGetValue(item.SensorName, out var tc))

                    {

                        item.TargetTemperatureC = tc;

                    }

                }

            });

            var ao1V = targets["PT500A"] * 0.001;

            var ao2V = targets["PT1000A"] * 0.001;

            const double diffLowV = 4.0;

            var pt500aHigh = diffLowV + ao1V;

            var pt500aLow = diffLowV;

            var pt1000aHigh = diffLowV + ao2V;

            var pt1000aLow = diffLowV;

            var roToOhm = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)

            {

                // resout1: PT1000B (J28/J29/J97) -> RO1 (API is 1-based, maps to driver RO0)

                ["RO1"] = targets["PT1000B"],

                // resout2: PT500B (J61/J62/J63) -> RO2 (API is 1-based, maps to driver RO1)

                ["RO2"] = targets["PT500B"],

            };

            Log($"涓嬪彂鐐逛綅{pointIndex}锛?32(AO3-AO4={ao2V.ToString("F4", CultureInfo.InvariantCulture)}V, AO5-AO6={ao1V.ToString("F4", CultureInfo.InvariantCulture)}V) + 7012(RO1={targets["PT1000B"].ToString("F1", CultureInfo.InvariantCulture)}惟, RO2={targets["PT500B"].ToString("F2", CultureInfo.InvariantCulture)}惟)");

            await _mtx532.WriteOnceDcAsync(new Dictionary<string, double>

            {

                ["AO3"] = pt1000aHigh,

                ["AO4"] = pt1000aLow,

                ["AO5"] = pt500aHigh,

                ["AO6"] = pt500aLow,

            }, token).ConfigureAwait(false);

            if (!_mtx532.IsOutputRunning)

                await _mtx532.StartOutputAsync(token).ConfigureAwait(false);

            try

            {

                var v3 = await _mtx532.GetLastOutputVoltageAsync("AO3", token).ConfigureAwait(false);

                var v4 = await _mtx532.GetLastOutputVoltageAsync("AO4", token).ConfigureAwait(false);

                var v5 = await _mtx532.GetLastOutputVoltageAsync("AO5", token).ConfigureAwait(false);

                var v6 = await _mtx532.GetLastOutputVoltageAsync("AO6", token).ConfigureAwait(false);

                Log($"532鍥炶(缂撳瓨)锛欰O3={v3.ToString("F4", CultureInfo.InvariantCulture)}V AO4={v4.ToString("F4", CultureInfo.InvariantCulture)}V AO5={v5.ToString("F4", CultureInfo.InvariantCulture)}V AO6={v6.ToString("F4", CultureInfo.InvariantCulture)}V");

            }

            catch (Exception ex)

            {

                Log($"532鍥炶澶辫触(涓嶅奖鍝嶈緭鍑?锛歿ex.Message}");

            }

            await _resistor.SetResistancesAsync(roToOhm, Pxi7012OutputMode.NoWait, token).ConfigureAwait(false);

            foreach (var ch in new[] { "RO1", "RO2" })

            {

                try { await _resistor.SetRelayStateAsync(ch, pathRelayClosed: true, shortCircuitClosed: false, token).ConfigureAwait(false); } catch { }

            }

            await EnsureArincRxReadyAsync(token).ConfigureAwait(false);

            StartArincRxLoopIfNeeded(token);

            Log($"鐐逛綅{pointIndex}璁剧疆瀹屾垚锛?32鐢靛帇 + 7012鐢甸樆宸茶緭鍑猴紙鐭╅樀寮€鍏虫槧灏勬湭鎺ュ叆锛?);

        }

        private const int ArincRxChannelIndex = 0;

        private const int ArincTxChannelIndex = 1;

        private const uint AtpSsmDataSdi = 0xC10001u;

        private const byte AtpLabelOctal174Dec = 124;

        private const double ArincRate = 100000.0;

        private const int ArincPollIntervalMs = 10;

        private const byte ArincExpectedSdi = 1;

        private const double ArincResolutionOhm = 0.01;

        private const double ArincResolutionC = 0.001;

        private static readonly Dictionary<byte, string> LabelToSensorName = new Dictionary<byte, string>

        {

            [(byte)206] = "PT500A",

            [(byte)238] = "PT500B",

            [(byte)174] = "PT1000A",

            [(byte)158] = "PT1000B",

        };

        private static readonly Dictionary<byte, string> TempLabelToSensorName = new Dictionary<byte, string>

        {

            [(byte)46] = "PT500A",

            [(byte)30] = "PT500B",

            [(byte)110] = "PT1000A",

            [(byte)94] = "PT1000B",

        };

        private async Task EnsureArincRxReadyAsync(CancellationToken token)

        {

            if (_arinc != null && _arinc.IsConnected)

                return;



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

                _arincTxOpened = false;

            }



            if (_arinc == null)

            {

                Log("鏈壘鍒癆RT4229(ARINC429)鏉垮崱锛屾棤娉曢噰闆嗘俯搴︾數闃诲洖璇?);

                return;

            }



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

            await EnsureAtpModeAsync(token).ConfigureAwait(false);

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



        private bool _atpTxOpened;

        private bool _atpModeEntered;

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

            Log($"娴嬭瘯淇℃伅-ATP鍙戦€佸噯澶? TX閫氶亾{ArincTxChannelIndex}, SSM/Data/SDI=0x{AtpSsmDataSdi:X6}, Label(oct174)=0x{AtpLabelOctal174Dec:X2}, Label鍙嶈浆鍚?0x{txLabel:X2}, Word=0x{word:X8}");

            try
            {
                await _arinc.SendWordsSingleAsync(ArincTxChannelIndex, new[] { word }, Art4229Parity.None, token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log($"娴嬭瘯淇℃伅-ATP鍙戦€佸け璐? TX閫氶亾{ArincTxChannelIndex}, Word=0x{word:X8}, 寮傚父={ex.Message}");
                throw;
            }

            Log($"娴嬭瘯淇℃伅-ATP鍙戦€佸畬鎴? TX閫氶亾{ArincTxChannelIndex}, Word=0x{word:X8}");

            _atpModeEntered = true;

        }



        private async Task CloseArincAsync()

        {

            if (_arinc == null)

                return;

            try { await _arinc.StopRxAsync(ArincRxChannelIndex, CancellationToken.None).ConfigureAwait(false); } catch { }

            try { await _arinc.CloseRxAsync(ArincRxChannelIndex, CancellationToken.None).ConfigureAwait(false); } catch { }

            if (_atpTxOpened)

            {

                try { await _arinc.CloseTxAsync(ArincTxChannelIndex, CancellationToken.None).ConfigureAwait(false); } catch { }

            }

            try { await _arinc.DisconnectAsync(CancellationToken.None).ConfigureAwait(false); } catch { }

            try { await _arinc.DisposeAsync().ConfigureAwait(false); } catch { }

            _arinc = null;

            _arincRxLoopTask = null;

            _arincTxOpened = false;

            _atpTxOpened = false;

            _atpModeEntered = false;

        }



        private async Task FlushArincRxBufferAsync(CancellationToken token)

        {

            if (_arinc == null || !_arinc.IsConnected)

                return;

            var idleRounds = 0;

            while (!token.IsCancellationRequested && idleRounds < 2)

            {

                await _arincRxReadLock.WaitAsync(token).ConfigureAwait(false);

                try

                {

                    var words = await _arinc.ReadRxWordsAsync(ArincRxChannelIndex, maxCount: 128, enableTimeTag: false, enableRateAdaption: false, cancellationToken: token).ConfigureAwait(false);

                    idleRounds = words.Count == 0 ? idleRounds + 1 : 0;

                }

                finally

                {

                    _arincRxReadLock.Release();

                }

                if (idleRounds < 2)

                    await Task.Delay(ArincPollIntervalMs, token).ConfigureAwait(false);

            }

        }



        /*
        private async Task EnsureArincTxReadyAsync(CancellationToken token)

        {

            await EnsureArincRxReadyAsync(token).ConfigureAwait(false);

            if (_arinc == null || !_arinc.IsConnected)

                return;



            if (_arincTxOpened)

                return;



            await _arinc.OpenTxAsync(ArincTxChannelIndex, token).ConfigureAwait(false);

            await _arinc.ConfigureTxAsync(

                ArincTxChannelIndex,

                rate: ArincRate,

                mode: Art4229TxMode.Single,

                parity: Art4229Parity.Odd,

                wordFormat: Art4229WordFormat.Standard429,

                cancellationToken: token).ConfigureAwait(false);



            _arincTxOpened = true;

        }



        private async Task SimulateTesterSendOnceAsync(IReadOnlyDictionary<string, double> targetsOhm, CancellationToken token)

        {

            if (!EnableArinc429TxSimulation)

                return;



            if (targetsOhm == null || targetsOhm.Count == 0)

                return;



            await EnsureArincTxReadyAsync(token).ConfigureAwait(false);

            if (_arinc == null || !_arinc.IsConnected)

                return;



            uint BuildData19FromOhm(double ohm)

            {

                var q = (int)Math.Round(ohm / ArincResolutionOhm, MidpointRounding.AwayFromZero);

                const int signBit = 1 << 18;
                const int maxMag = (1 << 18) - 1;

                var sign = q < 0;
                var mag = Math.Abs(q);
                if (mag > maxMag) mag = maxMag;

                return (uint)(mag | (sign ? signBit : 0));

            }



            var words = new List<uint>(8);



            if (targetsOhm.TryGetValue("PT500A", out var pt500a))

                words.Add(_arinc.BuildRawWord(163, ArincExpectedSdi, BuildData19FromOhm(pt500a), ssm: 0, applyOddParity: true));

            if (targetsOhm.TryGetValue("PT500B", out var pt500b))

                words.Add(_arinc.BuildRawWord(167, ArincExpectedSdi, BuildData19FromOhm(pt500b), ssm: 0, applyOddParity: true));

            if (targetsOhm.TryGetValue("PT1000A", out var pt1000a))

                words.Add(_arinc.BuildRawWord(165, ArincExpectedSdi, BuildData19FromOhm(pt1000a), ssm: 0, applyOddParity: true));

            if (targetsOhm.TryGetValue("PT1000B", out var pt1000b))

                words.Add(_arinc.BuildRawWord(171, ArincExpectedSdi, BuildData19FromOhm(pt1000b), ssm: 0, applyOddParity: true));



            double GetTargetTempC(string sensor)
            {
                var item = SensorItems.FirstOrDefault(x => string.Equals(x.SensorName, sensor, StringComparison.OrdinalIgnoreCase));
                return item?.TargetTemperatureC ?? double.NaN;
            }

            uint BuildData19FromC(double c)
            {
                var q = (int)Math.Round(c / ArincResolutionC, MidpointRounding.AwayFromZero);

                const int signBit = 1 << 18;
                const int maxMag = (1 << 18) - 1;

                var sign = q < 0;
                var mag = Math.Abs(q);
                if (mag > maxMag) mag = maxMag;

                return (uint)(mag | (sign ? signBit : 0));
            }

            var t1a = GetTargetTempC("PT500A");
            var t1b = GetTargetTempC("PT500B");
            var t2a = GetTargetTempC("PT1000A");
            var t2b = GetTargetTempC("PT1000B");

            if (!double.IsNaN(t1a))
                words.Add(_arinc.BuildRawWord(164, ArincExpectedSdi, BuildData19FromC(t1a), ssm: 0, applyOddParity: true));
            if (!double.IsNaN(t1b))
                words.Add(_arinc.BuildRawWord(170, ArincExpectedSdi, BuildData19FromC(t1b), ssm: 0, applyOddParity: true));
            if (!double.IsNaN(t2a))
                words.Add(_arinc.BuildRawWord(166, ArincExpectedSdi, BuildData19FromC(t2a), ssm: 0, applyOddParity: true));
            if (!double.IsNaN(t2b))
                words.Add(_arinc.BuildRawWord(172, ArincExpectedSdi, BuildData19FromC(t2b), ssm: 0, applyOddParity: true));



            if (words.Count == 0)

                return;



            try
            {
                await _arinc.SendWordsSingleAsync(ArincTxChannelIndex, words, Art4229Parity.Odd, token).ConfigureAwait(false);
            }
            catch
            {
                try
                {
                    _arincTxOpened = false;
                    await EnsureArincTxReadyAsync(token).ConfigureAwait(false);
                }
                catch
                {
                }

                for (int i = 0; i < words.Count; i++)
                {
                    try
                    {
                        await _arinc.SendWordsSingleAsync(ArincTxChannelIndex, new[] { words[i] }, Art4229Parity.Odd, token).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        try
                        {
                            _arincTxOpened = false;
                            await EnsureArincTxReadyAsync(token).ConfigureAwait(false);
                            await _arinc.SendWordsSingleAsync(ArincTxChannelIndex, new[] { words[i] }, Art4229Parity.Odd, token).ConfigureAwait(false);
                        }
                        catch (Exception ex2)
                        {
                            Log($"ART4229鍙戦€佸け璐ワ細tx={ArincTxChannelIndex},count=1,wordIndex={i}锛寋ex2.Message}");
                            throw new InvalidOperationException($"ART4229鍙戦€佸け璐?tx={ArincTxChannelIndex},count=1)", ex2);
                        }
                    }

                    await Task.Delay(5, token).ConfigureAwait(false);
                }
            }



            // 鍏煎鏃犵墿鐞嗗洖鐜?鏈帴鍏ユ祴璇曚欢鐨勫満鏅細妯℃嫙鍙戦€佸悗鐩存帴鍠傜粰瑙ｆ瀽鏇存柊鐣岄潰

            // 鍚庣画娴嬭瘯浠跺埌浣嶅悗锛屽彲閫氳繃鍏抽棴 EnableArinc429TxSimulation 鍋滄妯℃嫙

            if (EnableArinc429RxSelfFeedSimulation)

            {

                foreach (var w in words)

                {

                    ParseAndUpdateTelemetry(w);

                }

            }

        }
        */



        private void StartArincRxLoopIfNeeded(CancellationToken token)

        {

            if (_arinc == null || !_arinc.IsConnected)

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

                    if (_arinc == null || !_arinc.IsConnected)

                    {

                        await Task.Delay(ArincPollIntervalMs, token).ConfigureAwait(false);

                        continue;

                    }



                    await _arincRxReadLock.WaitAsync(token).ConfigureAwait(false);

                    try

                    {

                        var words = await _arinc.ReadRxWordsAsync(ArincRxChannelIndex, maxCount: 128, enableTimeTag: false, enableRateAdaption: false, cancellationToken: token).ConfigureAwait(false);

                        if (words.Count > 0)

                        {


                            for (int i = words.Count - 1; i >= 0; i--)

                            {

                                ParseAndUpdateTelemetry(words[i].Data429);

                            }

                        }

                    }

                    finally

                    {

                        _arincRxReadLock.Release();

                    }



                    await Task.Delay(ArincPollIntervalMs, token).ConfigureAwait(false);

                }

                catch (OperationCanceledException)

                {

                    break;

                }

                catch

                {

                    try { await Task.Delay(100, token).ConfigureAwait(false); } catch { break; }

                }

            }

        }



        private void ParseAndUpdateTelemetry(uint rawWord)

        {

            byte labelRaw;
            byte sdi;
            uint data19;
            byte ssm;

            if (_arinc != null)
            {
                _arinc.ParseRawWord(rawWord, out labelRaw, out sdi, out data19, out ssm);
            }
            else
            {
                labelRaw = (byte)(rawWord & 0xFF);
                sdi = (byte)((rawWord >> 8) & 0x03);
                data19 = (rawWord >> 10) & 0x7FFFF;
                ssm = (byte)((rawWord >> 29) & 0x03);
            }

            


            var isResistance = LabelToSensorName.TryGetValue(labelRaw, out var sensorName);
            var isTemp = TempLabelToSensorName.TryGetValue(labelRaw, out var tempSensorName);
            if (!isResistance && !isTemp)
                return;



   



            var signed = DecodeSigned19(data19);

            if (isResistance)
            {
                var valueOhm = signed * ArincResolutionOhm;



                lock (_telemetryCacheLock)

                {

                    _lastResistanceOhmBySensor[sensorName] = (valueOhm, DateTime.UtcNow);

                }

                return;
            }

            if (isTemp)
            {
                var valueC = signed * ArincResolutionC;

                lock (_telemetryCacheLock)

                {

                    _lastTemperatureCBySensor[tempSensorName] = (valueC, DateTime.UtcNow);

                }

                PostToUi(() =>
                {
                    var item = SensorItems.FirstOrDefault(x => string.Equals(x.SensorName, tempSensorName, StringComparison.OrdinalIgnoreCase));
                    if (item != null)
                        item.MeasuredTemperatureC = valueC;
                });

               
            }

        }



        private static int DecodeSigned19(uint data19)

        {

            data19 &= 0x7FFFF;

            const int signBit = 1 << 18;
            const int magMask = (1 << 18) - 1;

            var sign = (data19 & (uint)signBit) != 0;
            var mag = (int)(data19 & (uint)magMask);
            return sign ? -mag : mag;

        }



        private async Task<bool> EnsureResistorReadyAsync(CancellationToken token)

        {

            if (_resistor != null && _resistor.IsConnected)

                return true;



            await DisconnectResistorAsync().ConfigureAwait(false);



            var candidates = new uint[] { 1, 0, 2, 3, 4, 5, 6, 7 };

            foreach (var logicalId in candidates)

            {

                token.ThrowIfCancellationRequested();



                try

                {

                    var device = new ProgrammableResistorDevice

                    {

                        Name = "鐢甸樆杈撳嚭",

                        Model = "PXI-7012",

                        CardName = $"鐢甸樆杈撳嚭(鑷姩鎺㈡祴-{logicalId})",

                        SlotIndex = (int)logicalId

                    };



                    var api = new Pxi7012Api(device, logicalId);

                    await api.ConnectAsync(token).ConfigureAwait(false);



                    _resistor = api;

                    _connectedLogicalId = logicalId;

                    UpdateConnectionText();

                    Log($"7012杩炴帴鎴愬姛锛氶€昏緫ID={logicalId}");

                    return true;

                }

                catch

                {

                    try

                    {

                        if (_resistor != null)

                        {

                            await _resistor.DisposeAsync().ConfigureAwait(false);

                        }

                    }

                    catch

                    {

                    }



                    _resistor = null;

                    _connectedLogicalId = null;

                }

            }



            UpdateConnectionText();

            return false;

        }



        private async Task DisconnectResistorAsync()

        {

            if (_resistor == null)

                return;



            try { await _resistor.DisconnectAsync(CancellationToken.None).ConfigureAwait(false); } catch { }

            try { await _resistor.DisposeAsync().ConfigureAwait(false); } catch { }

            _resistor = null;

            _connectedLogicalId = null;

            UpdateConnectionText();

        }



        private static Dictionary<string, double> GetTargetsForPoint(int pointIndex)

        {

            return pointIndex switch

            {

                1 => new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)

                {

                    ["PT500A"] = 361.65,

                    ["PT500B"] = 361.65,

                    ["PT1000A"] = 723.3,

                    ["PT1000B"] = 723.3,

                },

                2 => new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)

                {

                    ["PT500A"] = 500.0,

                    ["PT500B"] = 500.0,

                    ["PT1000A"] = 1000.0,

                    ["PT1000B"] = 1000.0,

                },

                3 => new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)

                {

                    ["PT500A"] = 715.25,

                    ["PT500B"] = 715.25,

                    ["PT1000A"] = 1430.5,

                    ["PT1000B"] = 1430.5,

                },

                _ => new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)

                {

                    ["PT500A"] = 835.0,

                    ["PT500B"] = 835.0,

                    ["PT1000A"] = 1670.0,

                    ["PT1000B"] = 1670.0,

                }

            };

        }



        private async Task<bool> EnsureMtx532ReadyAsync(CancellationToken token)

        {

            if (_mtx532 != null && _mtx532.IsConnected)

                return true;



            await DisconnectMtx532Async().ConfigureAwait(false);



            DeviceBase device = FindMtx532Device();

            if (device == null)

            {

                ConnectionText = "532: 鏈繛鎺?;

                return false;

            }



            try

            {

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

                if (device is PxiDeviceBase pxi && pxi.SlotIndex > 0)

                    preferredSlot = pxi.SlotIndex;



                preferredSlot ??= TryParseSlotFromText(device.SlotPosition);

                preferredSlot ??= TryParseSlotFromText(device.Name);

                preferredSlot ??= TryParseSlotFromText(device.CardName);



                // 妲戒綅鎺㈡祴锛氬緢澶氶」鐩枃浠剁殑 SlotIndex 鍙兘鏈啓鍏ワ紝浣嗘澘鍗￠潰鏉胯兘杩炰笂锛涙澶勯€氳繃 override 鎺㈡祴

                var slotCandidates = new List<int>();

                if (preferredSlot.HasValue)

                    slotCandidates.Add(preferredSlot.Value);



                // 甯歌妲戒綅鑼冨洿锛堥伩鍏嶅皾璇曡繃澶氬鑷村惎鍔ㄦ參锛?
                for (int s = 2; s <= 18; s++)

                {

                    if (!slotCandidates.Contains(s))

                        slotCandidates.Add(s);

                }



                // 鍏滃簳锛氳€侀€昏緫榛樿 7

                if (!slotCandidates.Contains(7))

                    slotCandidates.Add(7);



                Exception lastEx = null;

                foreach (var slot in slotCandidates)

                {

                    token.ThrowIfCancellationRequested();



                    IMtx532Api api = null;

                    try

                    {

                        api = new Mtx532Api(device, options: new Mtx532Options { SampleRateHz = 1000.0, UseOneBasedAoChannelNumbering = true }, slotNumber: slot);

                        await api.ConnectAsync(token).ConfigureAwait(false);

                        // 娉ㄦ剰锛歁TX532Driver.StartAcquisitionAsync 瑕佹眰鑷冲皯鏈変竴涓?Enabled 閫氶亾銆?
                        // 杩欓噷鎴戜滑鍙渶瑕佸崟娆″啓鍏?DC 鐢靛帇锛屽洜姝や笉鍚姩杩炵画杈撳嚭锛岄伩鍏嶁€滄棤鍚敤閫氶亾鈥濆鑷?StartOutput 澶辫触琚鍒や负杩炴帴澶辫触銆?
                        await api.WriteOnceDcAsync(new Dictionary<string, double>

                        {

                            ["AO3"] = 4.0,

                            ["AO4"] = 4.0,

                            ["AO5"] = 4.0,

                            ["AO6"] = 4.0,

                        }, token).ConfigureAwait(false);



                        _mtx532 = api;

                        _connectedSlot = slot;

                        UpdateConnectionText();

                        Log($"532杩炴帴鎴愬姛锛歋LOT={slot}");

                        return true;

                    }

                    catch (Exception ex)

                    {

                        lastEx = ex;

                        try

                        {

                            if (api != null)

                                await api.DisposeAsync().ConfigureAwait(false);

                        }

                        catch

                        {

                        }

                    }

                }



                UpdateConnectionText();

                Log($"532杩炴帴澶辫触锛氬凡灏濊瘯妲戒綅 {string.Join(",", slotCandidates)}锛屾渶鍚庨敊璇細{lastEx?.Message}");

                return false;

            }

            catch

            {

                try

                {

                    if (_mtx532 != null)

                    {

                        await _mtx532.DisposeAsync().ConfigureAwait(false);

                    }

                }

                catch

                {

                }



                _mtx532 = null;

                _connectedSlot = null;

                UpdateConnectionText();

                return false;

            }

        }



        private async Task DisconnectMtx532Async()

        {

            if (_mtx532 == null)

                return;



            try { await _mtx532.StopOutputAsync(CancellationToken.None).ConfigureAwait(false); } catch { }

            try { await _mtx532.ResetAllToZeroAsync(disableAfterReset: true, cancellationToken: CancellationToken.None).ConfigureAwait(false); } catch { }

            try { await _mtx532.DisconnectAsync(CancellationToken.None).ConfigureAwait(false); } catch { }

            try { await _mtx532.DisposeAsync().ConfigureAwait(false); } catch { }

            _mtx532 = null;

            _connectedSlot = null;

            UpdateConnectionText();

        }



        private DeviceBase FindMtx532Device()

        {

            try

            {

                var pxiService = ContainerLocator.Container?.Resolve(typeof(IPxiChassisService)) as IPxiChassisService;

                if (pxiService == null)

                    return null;



                var chassisName = _singleBoardTestContext?.ChassisName ?? string.Empty;



                List<DeviceBase> devices = null;

                if (!string.IsNullOrWhiteSpace(chassisName))

                {

                    devices = pxiService.GetChassisDevices(chassisName);

                }



                if (devices == null)

                {

                    var all = pxiService.GetAllChassis();

                    if (all != null)

                    {

                        devices = all.Where(c => c?.Devices != null)

                            .SelectMany(c => FlattenDevices(c.Devices))

                            .ToList();

                    }

                }



                if (devices == null || devices.Count == 0)

                    return null;



                return FlattenDevices(devices).FirstOrDefault(IsMtx532Device);

            }

            catch

            {

                return null;

            }

        }



        private static IEnumerable<DeviceBase> FlattenDevices(IEnumerable<DeviceBase> devices)

        {

            if (devices == null)

                yield break;



            foreach (var d in devices)

            {

                if (d == null)

                    continue;



                yield return d;



                if (d.Children == null)

                    continue;



                foreach (var child in FlattenDevices(d.Children))

                    yield return child;

            }

        }



        private static bool IsMtx532Device(DeviceBase device)

        {

            if (device == null)

                return false;



            if (device is not PxiDeviceBase && !string.Equals(device.DeviceType, "Card", StringComparison.OrdinalIgnoreCase))

                return false;



            var model = (device.Model ?? string.Empty).ToUpperInvariant();

            return model.Contains("MT-X532") || model.Contains("MTX532") || model.Contains("X532");

        }



        private void UpdatePreviewResistances()

        {

            var targets = GetTargetsForPoint(SelectedPointIndex);

            var tempTargets = GetTargetTemperaturesForPoint(SelectedPointIndex);

            PostToUi(() =>

            {

                foreach (var item in SensorItems)

                {

                    if (targets.TryGetValue(item.SensorName, out var ohm))

                    {

                        item.TargetResistanceOhm = ohm;

                    }

                    if (tempTargets.TryGetValue(item.SensorName, out var tc))

                    {

                        item.TargetTemperatureC = tc;

                    }

                }

            });

        }



        public string CurrentPointTargetTemperatureText

        {

            get

            {

                var tempTargets = GetTargetTemperaturesForPoint(SelectedPointIndex);

                if (tempTargets.TryGetValue("PT500A", out var t))

                    return t.ToString("F1", CultureInfo.InvariantCulture);

                return "--";

            }

        }



        private void Log(string message)

        {

            if (string.IsNullOrWhiteSpace(message))

                return;

            PostToUi(() =>
            {
                if (Logs.Count >= 40)
                    Logs.Clear();
                Logs.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
            });

        }



        private void LogLabelResistanceThrottled(byte labelRaw, string sensorName, double valueOhm)

        {

            var nowTicks = DateTime.UtcNow.Ticks;

            ref long lastTicks = ref _lastLabel158LogTicks;

            if (labelRaw == 238)

                lastTicks = ref _lastLabel238LogTicks;

            var prev = Interlocked.Read(ref lastTicks);

            if (prev != 0 && nowTicks - prev < TimeSpan.FromSeconds(5).Ticks)

                return;

            Interlocked.Exchange(ref lastTicks, nowTicks);

            Log($"閲囬泦鏃堕棿={DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} Label={labelRaw.ToString(CultureInfo.InvariantCulture)}({sensorName}) 鐢甸樆鍥為噰={valueOhm.ToString("F2", CultureInfo.InvariantCulture)}惟");

        }



        public void Dispose()

        {

            try { _cts?.Cancel(); } catch { }

            try { _cts?.Dispose(); } catch { }

            try { _opLock?.Dispose(); } catch { }



            try

            {

                if (_mtx532 != null)

                {

                    _mtx532.DisposeAsync().AsTask().GetAwaiter().GetResult();

                }



                if (_resistor != null)

                {

                    _resistor.DisposeAsync().AsTask().GetAwaiter().GetResult();

                }



                if (_arinc != null)

                {

                    _arinc.DisposeAsync().AsTask().GetAwaiter().GetResult();

                }

            }

            catch

            {

            }

        }



        public sealed class SensorItemViewModel : BindableBase

        {

            private double _targetResistanceOhm;

            private double? _measuredResistanceOhm;

            private double _targetTemperatureC;

            private double? _measuredTemperatureC;

            private string _resistanceResultText = "--";

            private string _temperatureResultText = "--";



            public SensorItemViewModel(string sensorName, string pins, string aoChannel, string roChannel)

            {

                SensorName = sensorName;

                Pins = pins;

                AoChannel = aoChannel;

                RoChannel = roChannel;

            }



            public string SensorName { get; }



            public string Pins { get; }



            public string AoChannel { get; }



            public string RoChannel { get; }



            public double TargetResistanceOhm

            {

                get => _targetResistanceOhm;

                set

                {

                    if (SetProperty(ref _targetResistanceOhm, value))

                    {

                        RaisePropertyChanged(nameof(TargetResistanceText));

                    }

                }

            }



            public double? MeasuredResistanceOhm

            {

                get => _measuredResistanceOhm;

                set

                {

                    if (SetProperty(ref _measuredResistanceOhm, value))

                    {

                        RaisePropertyChanged(nameof(MeasuredResistanceText));

                    }

                }

            }



            public string TargetResistanceText => TargetResistanceOhm.ToString("F2", CultureInfo.InvariantCulture);



            public string MeasuredResistanceText => MeasuredResistanceOhm.HasValue

                ? MeasuredResistanceOhm.Value.ToString("F2", CultureInfo.InvariantCulture)

                : "--";



            public double TargetTemperatureC

            {

                get => _targetTemperatureC;

                set

                {

                    if (SetProperty(ref _targetTemperatureC, value))

                    {

                        RaisePropertyChanged(nameof(TargetTemperatureText));

                    }

                }

            }



            public double? MeasuredTemperatureC

            {

                get => _measuredTemperatureC;

                set

                {

                    if (SetProperty(ref _measuredTemperatureC, value))

                    {

                        RaisePropertyChanged(nameof(MeasuredTemperatureText));

                    }

                }

            }



            public string TargetTemperatureText => TargetTemperatureC.ToString("F3", CultureInfo.InvariantCulture);



            public string MeasuredTemperatureText => MeasuredTemperatureC.HasValue

                ? MeasuredTemperatureC.Value.ToString("F3", CultureInfo.InvariantCulture)

                : "--";



            public string ResistanceResultText

            {

                get => _resistanceResultText;

                set => SetProperty(ref _resistanceResultText, value);

            }



            public string TemperatureResultText

            {

                get => _temperatureResultText;

                set => SetProperty(ref _temperatureResultText, value);

            }

            //public string MeasuredTemperatureText => MeasuredTemperatureC.HasValue

            //    ? MeasuredTemperatureC.ToString()

            //    : "--";

        }

    }

}

