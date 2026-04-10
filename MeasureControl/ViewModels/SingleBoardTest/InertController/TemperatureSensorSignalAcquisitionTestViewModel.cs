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

        private string _powerStatus = "未供电";



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

        // 保存每个点位每个传感器的测试结果，用于报表生成
        // Key: pointIndex (1-4), Value: Dictionary<sensorName, (measuredTemp, result)>
        private readonly Dictionary<int, Dictionary<string, (string measuredTemp, string result)>> _pointTestResults = new Dictionary<int, Dictionary<string, (string measuredTemp, string result)>>();

        /// <summary>
        /// 获取指定点位指定传感器的测试结果（用于报表生成）
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
        /// 保存当前点位的测试结果
        /// </summary>
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
        /// 清除所有点位的测试结果
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



            SensorItems.Add(new SensorItemViewModel("PT500A", "J52、J53、J56", aoChannel: "AO5-AO6", roChannel: "--"));

            SensorItems.Add(new SensorItemViewModel("PT500B", "J61、J62、J63", aoChannel: "--", roChannel: "RO2"));

            SensorItems.Add(new SensorItemViewModel("PT1000A", "J54、J55、J57", aoChannel: "AO3-AO4", roChannel: "--"));

            SensorItems.Add(new SensorItemViewModel("PT1000B", "J28、J29、J97", aoChannel: "--", roChannel: "RO1"));



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

            // 用于保存当前点位的测试结果（用于报表生成）
            var pointResultsForReport = new Dictionary<string, (string measuredTemp, string result)>(StringComparer.OrdinalIgnoreCase);

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

                    failItems.Add($"{sensorItem.SensorName}(未采集到数据)");

                    Log($"判定：{sensorItem.SensorName} 未采集到数据 => FAIL");

                    // 保存失败结果用于报表
                    pointResultsForReport[sensorItem.SensorName] = ("--", "FAIL");

                    continue;

                }

                var measured = freshTelemetry[sensorItem.SensorName];

                var t = measured.temperatureC.ToString("F3", CultureInfo.InvariantCulture);

                Log($"回采：{sensorItem.SensorName} 温度={t}℃");

                var tempTarget = sensorItem.TargetTemperatureC;

                var tempActual = measured.temperatureC;

                var tempDiff = Math.Abs(tempActual - tempTarget);

                var tempPass = !double.IsNaN(tempActual) && tempDiff <= TemperatureToleranceC;

                var tempResult = tempPass ? "PASS" : "FAIL";

                var itemForTemp = sensorItem;

                PostToUi(() => itemForTemp.TemperatureResultText = tempResult);

                // 保存测试结果用于报表
                pointResultsForReport[sensorItem.SensorName] = (t, tempResult);

                if (!tempPass)

                {

                    failItems.Add($"{sensorItem.SensorName}(目标温度{tempTarget:F3}℃,回采{tempActual:F3}℃,差值{tempDiff:F3}℃,容差±{TemperatureToleranceC:F1}℃)");

                }

                Log($"温度判定：{sensorItem.SensorName} 目标={tempTarget:F3}℃ 实际={tempActual:F3}℃ 差值={tempDiff:F3}℃ 容差=±{TemperatureToleranceC:F1}℃ => {tempResult}");

            }

            // 如果指定了点位索引，保存测试结果用于报表生成
            if (pointIndexForReport.HasValue)
            {
                _pointTestResults[pointIndexForReport.Value] = pointResultsForReport;
            }

            LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            if (failItems.Count == 0)

            {

                LastTestResult = "PASS";

                Log("采集判定：PASS");

            }

            else

            {

                LastTestResult = "FAIL";

                Log($"采集判定：FAIL，超差：{string.Join("; ", failItems)}，电阻容差=±{ResistanceToleranceOhm:F2}Ω，温度容差=±{TemperatureToleranceC:F1}℃");

            }

        }

        private async Task OnMeasureAsync()

        {

            if (!IsManualTestRunning && !IsAutoTestRunning)

            {

                Log("请先启动手动测试，再进行采集判定。");

                return;

            }

            await _opLock.WaitAsync().ConfigureAwait(false);

            try

            {

                if (_cts == null)

                    _cts = new CancellationTokenSource();

                var token = _cts.Token;

                ClearMeasuredTelemetryOnUi();

                Log("手动采集：等待3秒后采集最新数据...");

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

                Log($"采集判定异常：{ex.Message}");

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

        private string _connectionText = "532: 未连接 | 7012: 未连接";

        public string ConnectionText

        {

            get => _connectionText;

            private set => SetProperty(ref _connectionText, value);

        }

        private void UpdateConnectionText()

        {

            var mtx = _mtx532 != null && _mtx532.IsConnected

                ? $"532: 已连接(SLOT={_connectedSlot})"

                : "532: 未连接";

            var r = _resistor != null && _resistor.IsConnected

                ? $"7012: 已连接(逻辑ID={_connectedLogicalId})"

                : "7012: 未连接";

            var p = IsPowerOn ? PowerStatus : "未供电";

            ConnectionText = $"电源:{p} | {mtx} | {r}";

        }

        private async Task OnManualTestAsync()

        {

            if (IsManualTestRunning)

            {

                await StopAsync().ConfigureAwait(false);

                return;

            }

            // 检查是否已总上电
            var _hps = ContainerLocator.Container.Resolve<IHydraulicPowerService>();
            if (_hps == null || !_hps.IsHydraulicPowered)
            {
                MessageBox.Show("请先点击左上角组件上电按钮进行总上电，再进行测试。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
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

                Log("开始手动测试（温度传感器信号采集）：准备连接532模拟量输出板卡 + 7012电阻输出板卡");

                await EnsurePowerAsync(_cts.Token).ConfigureAwait(false);

                var ok532 = await EnsureMtx532ReadyAsync(_cts.Token).ConfigureAwait(false);

                var ok7012 = await EnsureResistorReadyAsync(_cts.Token).ConfigureAwait(false);

                UpdateConnectionText();

                if (!ok532)

                {

                    Log("532连接失败：请检查板卡/驱动/机箱配置");

                }

                if (!ok7012)

                {

                    Log("7012连接失败：请检查板卡/驱动/逻辑ID");

                }

                if (!ok532 || !ok7012)

                {

                    await StopAsync().ConfigureAwait(false);

                    return;

                }

                UpdatePreviewResistances();

                Log("已就绪：PT500A/PT1000A 由532输出电压(V=R*0.001A)，PT1000B/PT500B 由7012输出电阻(resout1/resout2)；通讯采集已接入(ART4229/100kbps/奇校验/10ms轮询)");

                await EnsureArincRxReadyAsync(_cts.Token).ConfigureAwait(false);

                StartArincRxLoopIfNeeded(_cts.Token);

            }

            catch (OperationCanceledException)

            {

            }

            catch (Exception ex)

            {

                Log($"手动测试初始化异常：{ex.Message}");

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

                Log("开始自动测试：依次测试点位1~4（每个点位下发+回采+判定）");

                ClearAllPointTestResults();

                await EnsurePowerAsync(_cts.Token).ConfigureAwait(false);

                var ok532 = await EnsureMtx532ReadyAsync(_cts.Token).ConfigureAwait(false);

                var ok7012 = await EnsureResistorReadyAsync(_cts.Token).ConfigureAwait(false);

                UpdateConnectionText();

                if (!ok532 || !ok7012)

                {

                    Log("板卡连接失败：自动测试终止");

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

                    Log($"自动测试-开始点位{pointIndex}");

                    ClearMeasuredTelemetryOnUi();

                    await ApplyPointInternalAsync(pointIndex, _cts.Token).ConfigureAwait(false);

                    await MeasureInternalAsync(_cts.Token, AutoMeasureTimeoutMs, pointIndex).ConfigureAwait(false);

                    var pointPass = string.Equals(LastTestResult, "PASS", StringComparison.OrdinalIgnoreCase);

                    if (!pointPass)

                        allPointsPass = false;

                    Log($"自动测试-结束点位{pointIndex}：{(pointPass ? "PASS" : "FAIL")}");

                    await Task.Delay(200, _cts.Token).ConfigureAwait(false);

                }

                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                LastTestResult = allPointsPass ? "PASS" : "FAIL";

                Log($"自动测试结束：点位1~4完成，总体={(allPointsPass ? "PASS" : "FAIL")}");

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

                Log($"自动测试异常：{ex.Message}");

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

            // 检查是否已总上电
            var _hps = ContainerLocator.Container.Resolve<IHydraulicPowerService>();
            if (_hps == null || !_hps.IsHydraulicPowered)
            {
                MessageBox.Show("请先点击左上角组件上电按钮进行总上电，再进行测试。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
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

                Log("开始自动测试：依次测试点位1~4（每个点位下发+回采+判定）");

                ClearAllPointTestResults();

                await EnsurePowerAsync(_cts.Token).ConfigureAwait(false);

                var ok532 = await EnsureMtx532ReadyAsync(_cts.Token).ConfigureAwait(false);

                var ok7012 = await EnsureResistorReadyAsync(_cts.Token).ConfigureAwait(false);

                UpdateConnectionText();

                if (!ok532 || !ok7012)

                {

                    Log("板卡连接失败：自动测试终止");

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

                    Log($"自动测试-开始点位{pointIndex}");

                    ClearMeasuredTelemetryOnUi();

                    await ApplyPointInternalAsync(pointIndex, _cts.Token).ConfigureAwait(false);

                    await MeasureInternalAsync(_cts.Token, AutoMeasureTimeoutMs, pointIndex).ConfigureAwait(false);

                    var pointPass = string.Equals(LastTestResult, "PASS", StringComparison.OrdinalIgnoreCase);

                    if (!pointPass)

                        allPointsPass = false;

                    Log($"自动测试-结束点位{pointIndex}：{(pointPass ? "PASS" : "FAIL")}");

                    await Task.Delay(200, _cts.Token).ConfigureAwait(false);

                }

                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                LastTestResult = allPointsPass ? "PASS" : "FAIL";

                Log($"自动测试结束：点位1~4完成，总体={(allPointsPass ? "PASS" : "FAIL")}");

            }

            catch (OperationCanceledException)

            {

            }

            catch (Exception ex)

            {

                LastTestResult = "FAIL";

                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                Log($"自动测试异常：{ex.Message}");

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

            // 192.168.1.15 CH1 不再由本测试控制上电，由总上电统一管理

            await Task.Delay(100, token).ConfigureAwait(false);

            PostToUi(() =>

            {

                IsPowerOn = true;

                PowerStatus = $"已供电(CH1 {InputVoltageV:0.###}V)";

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

                // 192.168.1.15 CH1 不再由本测试控制下电
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

                        PowerStatus = "未供电";
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

                Log("请先启动手动测试，以连接532板卡。");

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

                    Log("532未就绪：无法下发AO电压");

                    return;

                }

                if (!ok7012)

                {

                    Log("7012未就绪：无法下发电阻值");

                    return;

                }

                ClearMeasuredTelemetryOnUi();

                await ApplyPointInternalAsync(pointIndex, _cts.Token).ConfigureAwait(false);

                await MeasureInternalAsync(_cts.Token, MeasureTimeoutMs).ConfigureAwait(false);

            }

            catch (Exception ex)

            {

                Log($"设置点位{pointIndex}异常：{ex.Message}");

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

            Log($"下发点位{pointIndex}：532(AO3-AO4={ao2V.ToString("F4", CultureInfo.InvariantCulture)}V, AO5-AO6={ao1V.ToString("F4", CultureInfo.InvariantCulture)}V) + 7012(RO1={targets["PT1000B"].ToString("F1", CultureInfo.InvariantCulture)}Ω, RO2={targets["PT500B"].ToString("F2", CultureInfo.InvariantCulture)}Ω)");

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

                Log($"532回读(缓存)：AO3={v3.ToString("F4", CultureInfo.InvariantCulture)}V AO4={v4.ToString("F4", CultureInfo.InvariantCulture)}V AO5={v5.ToString("F4", CultureInfo.InvariantCulture)}V AO6={v6.ToString("F4", CultureInfo.InvariantCulture)}V");

            }

            catch (Exception ex)

            {

                Log($"532回读失败(不影响输出)：{ex.Message}");

            }

            await _resistor.SetResistancesAsync(roToOhm, Pxi7012OutputMode.NoWait, token).ConfigureAwait(false);

            foreach (var ch in new[] { "RO1", "RO2" })

            {

                try { await _resistor.SetRelayStateAsync(ch, pathRelayClosed: true, shortCircuitClosed: false, token).ConfigureAwait(false); } catch { }

            }

            await EnsureArincRxReadyAsync(token).ConfigureAwait(false);

            StartArincRxLoopIfNeeded(token);

            Log($"点位{pointIndex}设置完成：532电压 + 7012电阻已输出（矩阵开关映射未接入）");

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

                Log("未找到ART4229(ARINC429)板卡，无法采集温度电阻回读");

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
                            Log($"ART4229发送失败：tx={ArincTxChannelIndex},count=1,wordIndex={i}，{ex2.Message}");
                            throw new InvalidOperationException($"ART4229发送失败(tx={ArincTxChannelIndex},count=1)", ex2);
                        }
                    }

                    await Task.Delay(5, token).ConfigureAwait(false);
                }
            }



            // 兼容无物理回环/未接入测试件的场景：模拟发送后直接喂给解析更新界面

            // 后续测试件到位后，可通过关闭 EnableArinc429TxSimulation 停止模拟

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

                        Name = "电阻输出",

                        Model = "PXI-7012",

                        CardName = $"电阻输出(自动探测-{logicalId})",

                        SlotIndex = (int)logicalId

                    };



                    var api = new Pxi7012Api(device, logicalId);

                    await api.ConnectAsync(token).ConfigureAwait(false);



                    _resistor = api;

                    _connectedLogicalId = logicalId;

                    UpdateConnectionText();

                    Log($"7012连接成功：逻辑ID={logicalId}");

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

                ConnectionText = "532: 未连接";

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



                // 槽位探测：很多项目文件的 SlotIndex 可能未写入，但板卡面板能连上；此处通过 override 探测

                var slotCandidates = new List<int>();

                if (preferredSlot.HasValue)

                    slotCandidates.Add(preferredSlot.Value);



                // 常见槽位范围（避免尝试过多导致启动慢）

                for (int s = 2; s <= 18; s++)

                {

                    if (!slotCandidates.Contains(s))

                        slotCandidates.Add(s);

                }



                // 兜底：老逻辑默认 7

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

                        // 注意：MTX532Driver.StartAcquisitionAsync 要求至少有一个 Enabled 通道。

                        // 这里我们只需要单次写入 DC 电压，因此不启动连续输出，避免“无启用通道”导致 StartOutput 失败被误判为连接失败。

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

                        Log($"532连接成功：SLOT={slot}");

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

                Log($"532连接失败：已尝试槽位 {string.Join(",", slotCandidates)}，最后错误：{lastEx?.Message}");

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

            Log($"采集时间={DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} Label={labelRaw.ToString(CultureInfo.InvariantCulture)}({sensorName}) 电阻回采={valueOhm.ToString("F2", CultureInfo.InvariantCulture)}Ω");

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

