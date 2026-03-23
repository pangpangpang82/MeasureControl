using Prism.Commands;
using Prism.Events;
using Prism.Mvvm;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using MeasureControl.Events;
using MeasureControl.Models;
using MeasureControl.Services;
using MeasureControl.Services.HardwareApis;

namespace MeasureControl.ViewModels.SingleBoardTest.InertController
{
    public sealed class SecondaryTertiaryPowerTestViewModel : BindableBase, IDisposable
    {
        private const string TestItemKey = "InertController_SecondaryTertiaryPower";

        private const string DmmIpAddress = "192.168.1.13";
        private const string MatrixIpAddress = "192.168.1.3";
        private const int MatrixTcpBasePort = 50200;

        private const string PowerSupplyIpAddress = "192.168.1.15";
        private const double InputVoltageV = 28.0;
        private const double InputCurrentA = 3.0;

        public bool SkipMainPowerOff { get; set; }

        private const int DmmTimeoutMs = 2000;
        private const double VoltageToleranceRatio = 0.1;

        private const int MatrixSlotSequence = 6;
        private const int MatrixSlotCommon = 4;

        private readonly ISingleBoardTestContextService _singleBoardTestContext;
        private readonly ProjectService _projectService;
        private readonly IEventAggregator _eventAggregator;
        private readonly IComponentPowerStateApi _componentPowerStateApi;
        private readonly IDmmApi _dmm;
        private IPowerSupplyApi _power;

        private readonly SemaphoreSlim _measureLock = new SemaphoreSlim(1, 1);
        private CancellationTokenSource _cts;

        private bool _isManualTestRunning;
        private bool _isAutoTestRunning;
        private bool _isBusy;

        private bool _isPowerOn;
        private string _powerStatus = "未供电";

        private string _overallResult = "--";
        private string _lastTestTime = "--";

        private SubscriptionToken _projectSavingToken;

        public SecondaryTertiaryPowerTestViewModel(
            ISingleBoardTestContextService singleBoardTestContext,
            ProjectService projectService,
            IEventAggregator eventAggregator,
            IDmmApi dmm,
            IComponentPowerStateApi componentPowerStateApi = null)
        {
            _singleBoardTestContext = singleBoardTestContext;
            _projectService = projectService;
            _eventAggregator = eventAggregator;
            _dmm = dmm;
            _componentPowerStateApi = componentPowerStateApi;

            ManualTestCommand = new DelegateCommand(async () => await OnManualTestAsync());
            AutoTestCommand = new DelegateCommand(async () => await OnAutoTestAsync());
            ClearLogCommand = new DelegateCommand(() => Logs.Clear());
            Items.Add(new VoltageItemViewModel(this, "a)", "15V检测", "J83", "J91", nominal: 15.0, tolerance: 15.0 * VoltageToleranceRatio, measurementScale: 6.0) { ColumnIndex = 15 });
            Items.Add(new VoltageItemViewModel(this, "b)", "-15V检测", "J84", "J91", nominal: -15.0, tolerance: 15.0 * VoltageToleranceRatio, measurementScale: -6.0) { ColumnIndex = 16 });
            Items.Add(new VoltageItemViewModel(this, "c)", "5V检测", "J97", "J94", nominal: 5.0, tolerance: 5.0 * VoltageToleranceRatio, measurementScale: 2.0) { ColumnIndex = 17 });
            Items.Add(new VoltageItemViewModel(this, "d)", "3.3V检测", "J98", "J94", nominal: 3.3, tolerance: 3.3 * VoltageToleranceRatio, measurementScale: 1.0) { ColumnIndex = 18 });

            LoadPersistedState();
            _projectSavingToken = _eventAggregator?.GetEvent<ProjectSavingEvent>()?.Subscribe(OnProjectSaving);
        }

        public ObservableCollection<VoltageItemViewModel> Items { get; } = new ObservableCollection<VoltageItemViewModel>();

        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();

        public DelegateCommand ManualTestCommand { get; }

        public DelegateCommand AutoTestCommand { get; }

        public DelegateCommand ClearLogCommand { get; }

        public bool IsPowerOn
        {
            get => _isPowerOn;
            private set
            {
                if (SetProperty(ref _isPowerOn, value))
                {
                    RaiseCanExecuteChangedForItems();
                }
            }
        }

        public string PowerStatus
        {
            get => _powerStatus;
            private set => SetProperty(ref _powerStatus, value);
        }

        public bool IsManualTestRunning
        {
            get => _isManualTestRunning;
            private set
            {
                if (SetProperty(ref _isManualTestRunning, value))
                {
                    RaiseCanExecuteChangedForItems();
                }
            }
        }

        public bool IsAutoTestRunning
        {
            get => _isAutoTestRunning;
            private set
            {
                if (SetProperty(ref _isAutoTestRunning, value))
                {
                    RaiseCanExecuteChangedForItems();
                }
            }
        }

        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (SetProperty(ref _isBusy, value))
                {
                    RaiseCanExecuteChangedForItems();
                }
            }
        }

        public string OverallResult
        {
            get => _overallResult;
            private set => SetProperty(ref _overallResult, value);
        }

        public string LastTestTime
        {
            get => _lastTestTime;
            private set => SetProperty(ref _lastTestTime, value);
        }

        private string PersistDataKey
        {
            get
            {
                var taskName = _singleBoardTestContext?.TestTaskName ?? string.Empty;
                if (string.IsNullOrWhiteSpace(taskName))
                    return TestItemKey;
                return $"{taskName}/{TestItemKey}";
            }
        }

        private async Task OnManualTestAsync()
        {
            if (IsManualTestRunning)
            {
                await StopTestAsync().ConfigureAwait(false);
                return;
            }

            if (IsAutoTestRunning)
            {
                await StopTestAsync().ConfigureAwait(false);
            }

            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();

            ResetResults();

            IsManualTestRunning = true;
            IsAutoTestRunning = false;
            OverallResult = "--";
            LastTestTime = "--";

            Log("开始手动测试");

            try
            {
                await _dmm.ConnectAsync(DmmIpAddress, _cts.Token).ConfigureAwait(false);
                Log($"万用表连接成功: {DmmIpAddress}");

                var matrix = MatrixControlService.Instance;
                var okCommon = await matrix.ConnectNodesAsync("I4", "O2", MatrixSlotCommon, MatrixIpAddress, MatrixTcpBasePort).ConfigureAwait(false);
                Log($"矩阵公共通路 I4-O2(slot{MatrixSlotCommon}) {(okCommon ? "OK" : "FAIL")}");

                Log($"电源: CH1 {InputVoltageV:0.###}V {InputCurrentA:0.###}A, IP={PowerSupplyIpAddress}");
                await EnsurePowerAsync(InputVoltageV, _cts.Token).ConfigureAwait(false);
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    IsPowerOn = true;
                    PowerStatus = "已供电";
                });

                if (!okCommon)
                {
                    await StopTestAsync().ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                await StopTestAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log($"手动测试初始化失败: {ex.Message}");
                await StopTestAsync().ConfigureAwait(false);
            }
        }

        private async Task OnAutoTestAsync()
        {
            if (IsAutoTestRunning)
            {
                await StopTestAsync().ConfigureAwait(false);
                return;
            }

            if (IsManualTestRunning)
            {
                await StopTestAsync().ConfigureAwait(false);
            }

            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();

            ResetResults();

            IsAutoTestRunning = true;
            IsManualTestRunning = false;
            OverallResult = "--";
            LastTestTime = "--";

            Log("开始自动测试");

            try
            {
                await _dmm.ConnectAsync(DmmIpAddress, _cts.Token).ConfigureAwait(false);
                Log($"万用表连接成功: {DmmIpAddress}");

                var matrix = MatrixControlService.Instance;
                var okCommon = await matrix.ConnectNodesAsync("I4", "O2", MatrixSlotCommon, MatrixIpAddress, MatrixTcpBasePort).ConfigureAwait(false);
                Log($"矩阵公共通路 I4-O2(slot{MatrixSlotCommon}) {(okCommon ? "OK" : "FAIL")}");
                if (!okCommon)
                {
                    await StopTestAsync().ConfigureAwait(false);
                    return;
                }

                Log($"电源: CH1 {InputVoltageV:0.###}V {InputCurrentA:0.###}A, IP={PowerSupplyIpAddress}");
                await EnsurePowerAsync(InputVoltageV, _cts.Token).ConfigureAwait(false);
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    IsPowerOn = true;
                    PowerStatus = "已供电";
                });

                foreach (var item in Items)
                {
                    if (_cts.IsCancellationRequested)
                    {
                        return;
                    }

                    await MeasureAsync(item, _cts.Token).ConfigureAwait(false);
                    await Task.Delay(100, _cts.Token).ConfigureAwait(false);
                }

                EvaluateOverall();
                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                Log($"自动测试完成，总体结果: {OverallResult}");

                await StopTestAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                await StopTestAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log($"自动测试失败: {ex.Message}");
                await StopTestAsync().ConfigureAwait(false);
            }
        }

        public async Task<string> RunOnceAsync(CancellationToken cancellationToken)
        {
            if (IsAutoTestRunning || IsManualTestRunning)
            {
                await StopTestAsync().ConfigureAwait(false);
                await Task.Delay(100, CancellationToken.None).ConfigureAwait(false);
            }

            _cts?.Cancel();
            _cts?.Dispose();
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            ResetResults();

            IsAutoTestRunning = true;
            IsManualTestRunning = false;
            OverallResult = "--";
            LastTestTime = "--";

            try
            {
                Log("开始自动测试");

                await _dmm.ConnectAsync(DmmIpAddress, _cts.Token).ConfigureAwait(false);
                Log($"万用表连接成功: {DmmIpAddress}");

                var matrix = MatrixControlService.Instance;
                var okCommon = await matrix.ConnectNodesAsync("I4", "O2", MatrixSlotCommon, MatrixIpAddress, MatrixTcpBasePort).ConfigureAwait(false);
                Log($"矩阵公共通路 I4-O2(slot{MatrixSlotCommon}) {(okCommon ? "OK" : "FAIL")}");
                if (!okCommon)
                {
                    await StopTestAsync().ConfigureAwait(false);
                    return "FAIL";
                }

                Log($"电源: CH1 {InputVoltageV:0.###}V {InputCurrentA:0.###}A, IP={PowerSupplyIpAddress}");
                await EnsurePowerAsync(InputVoltageV, _cts.Token).ConfigureAwait(false);
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    IsPowerOn = true;
                    PowerStatus = "已供电";
                });

                foreach (var item in Items)
                {
                    _cts.Token.ThrowIfCancellationRequested();
                    await MeasureAsync(item, _cts.Token).ConfigureAwait(false);
                    await Task.Delay(100, _cts.Token).ConfigureAwait(false);
                }

                EvaluateOverall();
                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                Log($"自动测试完成，总体结果: {OverallResult}");

                var finalResult = OverallResult;
                await StopTestAsync().ConfigureAwait(false);
                return string.IsNullOrWhiteSpace(finalResult) || string.Equals(finalResult, "--", StringComparison.OrdinalIgnoreCase)
                    ? "FAIL"
                    : finalResult;
            }
            catch (OperationCanceledException)
            {
                await StopTestAsync().ConfigureAwait(false);
                throw;
            }
            catch (Exception ex)
            {
                Log($"自动测试失败: {ex.Message}");
                await StopTestAsync().ConfigureAwait(false);
                return "FAIL";
            }
            finally
            {
                IsAutoTestRunning = false;
                _cts?.Dispose();
                _cts = null;
            }
        }

        internal bool CanMeasureItem(VoltageItemViewModel item)
        {
            if (item == null) return false;
            return IsManualTestRunning && !IsBusy && IsPowerOn;
        }

        internal async Task MeasureAsync(VoltageItemViewModel item)
        {
            if (item == null) return;
            var token = _cts?.Token ?? CancellationToken.None;
            await MeasureAsync(item, token).ConfigureAwait(false);

            EvaluateOverall();
            if (Items.All(i => i.IsMeasured))
            {
                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            }
        }

        private async Task MeasureAsync(VoltageItemViewModel item, CancellationToken token)
        {
            if (item == null) return;

            await _measureLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                IsBusy = true;

                Log($"开始测量: {item.Name} {item.SignalPin}-{item.GroundPin}");

                var matrix = MatrixControlService.Instance;
                var output = $"O{item.ColumnIndex}";
                var ok = await matrix.ConnectNodesAsync("I1", output, MatrixSlotSequence, MatrixIpAddress, MatrixTcpBasePort).ConfigureAwait(false);
                Log($"矩阵连接 I1-{output}(slot{MatrixSlotSequence}) {(ok ? "OK" : "FAIL")}");
                if (!ok)
                {
                    item.UpdateMeasurement(null, "--", "FAIL", measured: true);
                    return;
                }

                await Task.Delay(500, token).ConfigureAwait(false);

                var reading = await SafeReadVoltageAsync(token).ConfigureAwait(false);
                ApplyReading(item, reading);
            }
            finally
            {
                try
                {
                    var matrix = MatrixControlService.Instance;
                    var output = $"O{item.ColumnIndex}";
                    _ = await matrix.DisconnectNodesAsync("I1", output, MatrixSlotSequence, MatrixIpAddress, MatrixTcpBasePort).ConfigureAwait(false);
                }
                catch
                {
                }

                IsBusy = false;
                _measureLock.Release();
            }
        }

        private async Task<DmmReading> SafeReadVoltageAsync(CancellationToken token)
        {
            try
            {
                return await _dmm.ReadOnceAsync(DmmMeasureMode.DCV, new DmmReadOptions { TimeoutMilliseconds = DmmTimeoutMs }, token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log($"万用表读数异常: {ex.Message}");
                return null;
            }
        }

        private void ApplyReading(VoltageItemViewModel item, DmmReading reading)
        {
            if (reading == null)
            {
                item.UpdateMeasurement(null, "--", "FAIL", measured: true);
                return;
            }

            if (reading.IsOverrange)
            {
                item.UpdateMeasurement(null, "OL", "FAIL", measured: true);
                Log("读数为OL(过量程)，判为FAIL");
                return;
            }

            if (reading.Value == null)
            {
                item.UpdateMeasurement(null, "--", "FAIL", measured: true);
                return;
            }

            var rawValue = reading.Value.Value;
            var scaledValue = rawValue * item.MeasurementScale;
            var text = scaledValue.ToString("0.###", CultureInfo.InvariantCulture);
            var pass = Math.Abs(scaledValue - item.Nominal) <= item.Tolerance;

            item.UpdateMeasurement(scaledValue, text, pass ? "PASS" : "FAIL", measured: true);
            Log($"读数: 原始={rawValue:0.###} V, 换算后={scaledValue:0.###} V, 标准={item.Nominal:0.###}±{item.Tolerance:0.###} V => {(pass ? "PASS" : "FAIL")}");
        }

        private void ResetResults()
        {
            foreach (var item in Items)
            {
                item.UpdateMeasurement(null, "--", "--", measured: false);
            }
        }

        private void EvaluateOverall()
        {
            if (Items.Count == 0)
            {
                OverallResult = "--";
                return;
            }

            if (!Items.All(i => i.IsMeasured))
            {
                OverallResult = "--";
                return;
            }

            OverallResult = Items.All(i => string.Equals(i.Result, "PASS", StringComparison.OrdinalIgnoreCase)) ? "PASS" : "FAIL";
        }

        private async Task StopTestAsync()
        {
            try { _cts?.Cancel(); } catch { }

            IsManualTestRunning = false;
            IsAutoTestRunning = false;
            IsBusy = false;

            try
            {
                var matrix = MatrixControlService.Instance;
                _ = await matrix.DisconnectNodesAsync("I4", "O2", MatrixSlotCommon, MatrixIpAddress, MatrixTcpBasePort).ConfigureAwait(false);

                foreach (var item in Items)
                {
                    var output = $"O{item.ColumnIndex}";
                    _ = await matrix.DisconnectNodesAsync("I1", output, MatrixSlotSequence, MatrixIpAddress, MatrixTcpBasePort).ConfigureAwait(false);
                }
            }
            catch
            {
            }

            try
            {
                await _dmm.DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
            }

            await CleanupPowerAsync().ConfigureAwait(false);

            Application.Current?.Dispatcher?.Invoke(() =>
            {
                if (!SkipMainPowerOff)
                {
                    IsPowerOn = false;
                    PowerStatus = "未供电";
                }
                RaiseCanExecuteChangedForItems();
            });
        }

        private void RaiseCanExecuteChangedForItems()
        {
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                foreach (var item in Items)
                {
                    item.MeasureCommand?.RaiseCanExecuteChanged();
                }
            });
        }

        private void Log(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                Logs.Add($"[{DateTime.Now:HH:mm:ss.fff}] {message}");
                while (Logs.Count > 500)
                {
                    Logs.RemoveAt(0);
                }
            });
        }

        private void LoadPersistedState()
        {
            try
            {
                var root = _projectService?.CurrentProjectRoot;
                if (root?.TestInterfaceControls == null)
                    return;

                if (!root.TestInterfaceControls.TryGetValue(PersistDataKey, out var items) || items == null)
                    return;

                string Read(string key)
                {
                    return items.FirstOrDefault(x => string.Equals(x?.BoundVariableName, key, StringComparison.OrdinalIgnoreCase))?.BoundVariablePath;
                }

                LastTestTime = Read("LastTestTime") ?? "--";
                OverallResult = Read("OverallResult") ?? "--";

                RaisePropertyChanged(nameof(LastTestTime));
                RaisePropertyChanged(nameof(OverallResult));
            }
            catch
            {
            }
        }

        private void OnProjectSaving()
        {
            try
            {
                var root = _projectService?.CurrentProjectRoot;
                if (root?.TestInterfaceControls == null)
                    return;

                if (!root.TestInterfaceControls.TryGetValue(PersistDataKey, out var items) || items == null)
                {
                    items = new System.Collections.Generic.List<TestInterfaceControlItem>();
                    root.TestInterfaceControls[PersistDataKey] = items;
                }

                void Upsert(string key, string value)
                {
                    var item = items.FirstOrDefault(x => string.Equals(x?.BoundVariableName, key, StringComparison.OrdinalIgnoreCase));
                    if (item == null)
                    {
                        item = new TestInterfaceControlItem
                        {
                            ControlType = "Value",
                            BoundVariableName = key
                        };
                        items.Add(item);
                    }

                    item.BoundVariablePath = value ?? string.Empty;
                }

                Upsert("LastTestTime", LastTestTime);
                Upsert("OverallResult", OverallResult);
            }
            catch
            {
            }
        }

        private async Task EnsurePowerAsync(double voltageV, CancellationToken cancellationToken)
        {
            _power ??= new PowerSupplySocketApi();
            if (!_power.IsConnected)
                await _power.ConnectAsync(PowerSupplyIpAddress, cancellationToken).ConfigureAwait(false);
            await _power.ApplyAsync(PowerSupplyChannel.CH1, voltageV, InputCurrentA, cancellationToken).ConfigureAwait(false);
            await _power.SetOutputEnabledAsync(PowerSupplyChannel.CH1, true, cancellationToken).ConfigureAwait(false);
            await Task.Delay(300, cancellationToken).ConfigureAwait(false);
        }

        private async Task CleanupPowerAsync()
        {
            if (_power != null)
            {
                if (!SkipMainPowerOff)
                {
                    try { await _power.SetOutputEnabledAsync(PowerSupplyChannel.CH1, false, CancellationToken.None).ConfigureAwait(false); } catch { }
                }
                try { await _power.DisconnectAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
            }
        }

        public void Dispose()
        {
            try { _cts?.Cancel(); } catch { }
            _cts?.Dispose();
            _cts = null;

            try { CleanupPowerAsync().GetAwaiter().GetResult(); } catch { }
            try { _power?.DisposeAsync().AsTask().GetAwaiter().GetResult(); } catch { }
            _power = null;

            _measureLock?.Dispose();

            if (_projectSavingToken != null)
            {
                _eventAggregator?.GetEvent<ProjectSavingEvent>()?.Unsubscribe(_projectSavingToken);
            }
        }

        public sealed class VoltageItemViewModel : BindableBase
        {
            private readonly SecondaryTertiaryPowerTestViewModel _owner;

            private string _voltageText = "--";
            private string _result = "--";
            private bool _isMeasured;

            internal VoltageItemViewModel(
                SecondaryTertiaryPowerTestViewModel owner,
                string indexText,
                string name,
                string signalPin,
                string groundPin,
                double nominal,
                double tolerance,
                double measurementScale)
            {
                _owner = owner;
                IndexText = indexText;
                Name = name;
                SignalPin = signalPin;
                GroundPin = groundPin;
                Nominal = nominal;
                Tolerance = tolerance;
                MeasurementScale = measurementScale;

                MeasureCommand = new DelegateCommand(async () => await _owner.MeasureAsync(this), () => _owner.CanMeasureItem(this));
            }

            public string IndexText { get; }

            public string Name { get; }

            public string SignalPin { get; }

            public string GroundPin { get; }

            public double Nominal { get; }

            public double Tolerance { get; }

            public double MeasurementScale { get; }

            public int ColumnIndex { get; set; }

            public string VoltageText
            {
                get => _voltageText;
                private set => SetProperty(ref _voltageText, value);
            }

            public string Result
            {
                get => _result;
                private set => SetProperty(ref _result, value);
            }

            public bool IsMeasured
            {
                get => _isMeasured;
                private set => SetProperty(ref _isMeasured, value);
            }

            public DelegateCommand MeasureCommand { get; }

            internal void UpdateMeasurement(double? valueVolt, string valueText, string result, bool measured)
            {
                VoltageText = valueText;
                Result = result;
                IsMeasured = measured;
            }
        }
    }
}
