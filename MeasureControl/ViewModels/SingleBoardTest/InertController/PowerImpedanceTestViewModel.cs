using Prism.Commands;
using Prism.Events;
using Prism.Mvvm;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Collections.Generic;
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
    public sealed class PowerImpedanceTestViewModel : BindableBase, IDisposable
    {
        private const string TestItemKey = "InertController_PowerImpedance";
        private const double ImpedanceThresholdOhm = 500.0;

        private const string DmmIpAddress = "192.168.1.13";
        private const string MatrixIpAddress = "192.168.1.3";
        private const int MatrixTcpBasePort = 50200;

        private const int MatrixSlotSequence = 6;
        private const int MatrixSlotCommon = 4;

        private readonly ISingleBoardTestContextService _singleBoardTestContext;
        private readonly ProjectService _projectService;
        private readonly IEventAggregator _eventAggregator;
        private readonly IDmmApi _dmm;

        private readonly SemaphoreSlim _measureLock = new SemaphoreSlim(1, 1);
        private CancellationTokenSource _cts;

        private bool _isManualTestRunning;
        private bool _isAutoTestRunning;
        private bool _isBusy;

        private string _overallResult = "--";
        private string _lastTestTime = "--";

        private SubscriptionToken _projectSavingToken;

        public PowerImpedanceTestViewModel(
            ISingleBoardTestContextService singleBoardTestContext,
            ProjectService projectService,
            IEventAggregator eventAggregator,
            IDmmApi dmm)
        {
            _singleBoardTestContext = singleBoardTestContext;
            _projectService = projectService;
            _eventAggregator = eventAggregator;
            _dmm = dmm;

            ManualTestCommand = new DelegateCommand(async () => await OnManualTestAsync());
            AutoTestCommand = new DelegateCommand(async () => await OnAutoTestAsync());
            ClearLogCommand = new DelegateCommand(() => Logs.Clear());

            Items.Add(new ImpedanceItemViewModel(
                this,
                "a)",
                "到 COM 的阻抗",
                "COM",
                signalPinOptions: new[] { "J7", "J8", "J9", "J10" },
                groundPinOptions: new[] { "J5(COM)" })
            { ColumnIndex = 20 });

            Items.Add(new ImpedanceItemViewModel(
                this,
                "b)",
                "到 EARTH 的阻抗",
                "EARTH",
                signalPinOptions: new[] { "J7", "J8", "J9", "J10" },
                groundPinOptions: new[] { "J70(EARTH)" })
            { ColumnIndex = 21 });

            Items.Add(new ImpedanceItemViewModel(
                this,
                "c)",
                "28V+ 到 28V- 的阻抗",
                "28V-",
                signalPinOptions: new[] { "J1", "J2", "J3" },
                groundPinOptions: new[] { "J36", "J37", "J38" })
            { ColumnIndex = 22 });

            LoadPersistedState();
            _projectSavingToken = _eventAggregator?.GetEvent<ProjectSavingEvent>()?.Subscribe(OnProjectSaving);
        }

        public ObservableCollection<ImpedanceItemViewModel> Items { get; } = new ObservableCollection<ImpedanceItemViewModel>();

        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();

        public DelegateCommand ManualTestCommand { get; }
        public DelegateCommand AutoTestCommand { get; }
        public DelegateCommand ClearLogCommand { get; }

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

        internal bool CanMeasureItem(ImpedanceItemViewModel item)
        {
            if (item == null) return false;
            return IsManualTestRunning && !IsBusy;
        }

        internal async Task MeasureAsync(ImpedanceItemViewModel item)
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

        private async Task MeasureAsync(ImpedanceItemViewModel item, CancellationToken token)
        {
            if (item == null) return;

            await _measureLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                IsBusy = true;

                if (string.IsNullOrWhiteSpace(item.SignalPin) || string.IsNullOrWhiteSpace(item.GroundPin))
                {
                    item.UpdateMeasurement(null, "--", "FAIL", measured: true);
                    Log($"未选择引脚，无法测量: {item.Name}");
                    return;
                }

                Log($"开始测量: {item.Name} {item.SignalPin}-{item.GroundPin}");

                if (string.Equals(item.GroupKey, "COM", StringComparison.OrdinalIgnoreCase))
                {
                    await MeasureJ7ToJ10Async(item, token).ConfigureAwait(false);
                    return;
                }

                if (string.Equals(item.GroupKey, "EARTH", StringComparison.OrdinalIgnoreCase))
                {
                    await MeasureJ7ToJ10Async(item, token).ConfigureAwait(false);
                    return;
                }

                if (string.Equals(item.GroupKey, "28V-", StringComparison.OrdinalIgnoreCase))
                {
                    await MeasureJ1ToJ3ToJ36ToJ38Async(item, token).ConfigureAwait(false);
                    return;
                }

                item.UpdateMeasurement(null, "--", "FAIL", measured: true);
                Log("未知测试项，判为FAIL");
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

        private async Task MeasureJ7ToJ10Async(ImpedanceItemViewModel item, CancellationToken token)
        {
            // 当前工程没有惰化单板的矩阵映射表，这里先沿用“空气单板”的测量模式：
            // 通过矩阵把万用表接到一个输出列后读取 RES。
            // ColumnIndex 只是为了区分不同测试项占用不同列，后续接线表明确后可替换。

            var matrix = MatrixControlService.Instance;
            var output = $"O{item.ColumnIndex}";

            var ok = await matrix.ConnectNodesAsync("I1", output, MatrixSlotSequence, MatrixIpAddress, MatrixTcpBasePort).ConfigureAwait(false);
            Log($"矩阵连接 I1-{output}(slot{MatrixSlotSequence}) {(ok ? "OK" : "FAIL")}");
            if (!ok)
            {
                item.UpdateMeasurement(null, "--", "FAIL", measured: true);
                return;
            }

            var reading = await SafeReadResistanceAsync(token).ConfigureAwait(false);
            ApplyReading(item, reading);
        }

        private async Task MeasureJ1ToJ3ToJ36ToJ38Async(ImpedanceItemViewModel item, CancellationToken token)
        {
            var matrix = MatrixControlService.Instance;
            var output = $"O{item.ColumnIndex}";

            var ok = await matrix.ConnectNodesAsync("I1", output, MatrixSlotSequence, MatrixIpAddress, MatrixTcpBasePort).ConfigureAwait(false);
            Log($"矩阵连接 I1-{output}(slot{MatrixSlotSequence}) {(ok ? "OK" : "FAIL")}");
            if (!ok)
            {
                item.UpdateMeasurement(null, "--", "FAIL", measured: true);
                return;
            }

            var reading = await SafeReadResistanceAsync(token).ConfigureAwait(false);
            ApplyReading(item, reading);
        }

        private async Task<DmmReading> SafeReadResistanceAsync(CancellationToken token)
        {
            try
            {
                return await _dmm.ReadOnceAsync(DmmMeasureMode.RES, new DmmReadOptions { TimeoutMilliseconds = 8000 }, token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log($"万用表读数异常: {ex.Message}");
                return null;
            }
        }

        private void ApplyReading(ImpedanceItemViewModel item, DmmReading reading)
        {
            if (reading == null)
            {
                item.UpdateMeasurement(null, "--", "FAIL", measured: true);
                return;
            }

            if (reading.IsOverrange)
            {
                item.UpdateMeasurement(null, "OL", "PASS", measured: true);
                Log("读数为OL(过量程)，判为PASS");
                return;
            }

            if (reading.Value == null)
            {
                item.UpdateMeasurement(null, "--", "FAIL", measured: true);
                return;
            }

            var ohm = reading.Value.Value;
            var text = ohm.ToString("0.###", CultureInfo.InvariantCulture);
            var pass = ohm > ImpedanceThresholdOhm;

            item.UpdateMeasurement(ohm, text, pass ? "PASS" : "FAIL", measured: true);
            Log($"读数: {ohm:0.###} Ω => {(pass ? "PASS" : "FAIL")}");
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

            RaiseCanExecuteChangedForItems();
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

        public void Dispose()
        {
            try { _cts?.Cancel(); } catch { }
            _cts?.Dispose();
            _cts = null;

            _measureLock?.Dispose();

            if (_projectSavingToken != null)
            {
                _eventAggregator?.GetEvent<ProjectSavingEvent>()?.Unsubscribe(_projectSavingToken);
            }
        }

        public sealed class ImpedanceItemViewModel : BindableBase
        {
            private readonly PowerImpedanceTestViewModel _owner;
            private string _impedanceText = "--";
            private string _result = "--";
            private bool _isMeasured;

            private string _signalPin;
            private string _groundPin;

            internal ImpedanceItemViewModel(
                PowerImpedanceTestViewModel owner,
                string indexText,
                string name,
                string groupKey,
                IEnumerable<string> signalPinOptions,
                IEnumerable<string> groundPinOptions)
            {
                _owner = owner;
                IndexText = indexText;
                Name = name;
                GroupKey = groupKey;

                SignalPinOptions = new ObservableCollection<string>((signalPinOptions ?? Array.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)));
                GroundPinOptions = new ObservableCollection<string>((groundPinOptions ?? Array.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)));

                SignalPin = SignalPinOptions.FirstOrDefault();
                GroundPin = GroundPinOptions.FirstOrDefault();

                MeasureCommand = new DelegateCommand(async () => await _owner.MeasureAsync(this), () => _owner.CanMeasureItem(this));
            }

            public string IndexText { get; }
            public string Name { get; }
            public string GroupKey { get; }

            public ObservableCollection<string> SignalPinOptions { get; }

            public ObservableCollection<string> GroundPinOptions { get; }

            public string SignalPin
            {
                get => _signalPin;
                set => SetProperty(ref _signalPin, value);
            }

            public string GroundPin
            {
                get => _groundPin;
                set => SetProperty(ref _groundPin, value);
            }

            public int ColumnIndex { get; set; }

            public string ImpedanceText
            {
                get => _impedanceText;
                private set => SetProperty(ref _impedanceText, value);
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

            internal void UpdateMeasurement(double? valueOhm, string valueText, string result, bool measured)
            {
                ImpedanceText = valueText;
                Result = result;
                IsMeasured = measured;
            }
        }
    }
}
