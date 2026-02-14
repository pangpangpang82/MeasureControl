using Prism.Commands;
using Prism.Mvvm;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MeasureControl.Services;
using MeasureControl.Services.HardwareApis;

namespace MeasureControl.ViewModels.SingleBoardTest.AirController
{
    public sealed class PowerToGroundImpedanceTestViewModel : BindableBase, IDisposable
    {
        private const string DmmIpAddress = "192.168.1.13";
        private const string MatrixIpAddress = "192.168.1.3";

        private const int MatrixTcpBasePort = 50200;
        private const int MatrixSlotSequence = 6;
        private const int MatrixSlotCommon = 4;

        private const double PassThresholdOhm = 200.0;

        private readonly IDmmApi _dmm;
        private readonly SemaphoreSlim _measureLock = new SemaphoreSlim(1, 1);
        private CancellationTokenSource _cts;

        private bool _isManualTestRunning;
        private bool _isAutoTestRunning;
        private bool _isBusy;

        private string _overallResult = "--";
        private string _lastTestTime = "--";

        public PowerToGroundImpedanceTestViewModel(IDmmApi dmm)
        {
            _dmm = dmm;

            ManualTestCommand = new DelegateCommand(async () => await OnManualTestAsync());
            AutoTestCommand = new DelegateCommand(async () => await OnAutoTestAsync());
            ClearLogCommand = new DelegateCommand(() => Logs.Clear());

            Items.Add(new ImpedanceItemViewModel(this, 20, "28V_DC_BUS1", "输入", "J189", "J126"));
            Items.Add(new ImpedanceItemViewModel(this, 21, "28V_DC_BUS1", "输入", "J190", "J127"));
            Items.Add(new ImpedanceItemViewModel(this, 22, "+15V", "输出", "J222", "J209"));
            Items.Add(new ImpedanceItemViewModel(this, 23, "+5V", "输出", "J160", "J146"));
            Items.Add(new ImpedanceItemViewModel(this, 24, "+1.9V", "输出", "J223", "J242"));
            Items.Add(new ImpedanceItemViewModel(this, 25, "+1.5V", "输出", "J98", "J118"));
            Items.Add(new ImpedanceItemViewModel(this, 26, "+3.3V", "输出", "J97", "J118"));
            Items.Add(new ImpedanceItemViewModel(this, 27, "+3.3V", "输出", "J35", "J55"));
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

                var matrix = MatrixControlService.Instance;
                var output = $"O{item.ColumnIndex}";

                Log($"开始测量: {item.PowerName} {item.SignalPin}-{item.GroundPin} (r1c{item.ColumnIndex})");

                var ok = await matrix.ConnectNodesAsync("I1", output, MatrixSlotSequence, MatrixIpAddress, MatrixTcpBasePort).ConfigureAwait(false);
                Log($"矩阵连接 I1-{output}(slot{MatrixSlotSequence}) {(ok ? "OK" : "FAIL")}");
                if (!ok)
                {
                    item.UpdateMeasurement(null, "--", "FAIL", measured: true);
                    return;
                }

                DmmReading reading = null;
                try
                {
                    reading = await _dmm.ReadOnceAsync(DmmMeasureMode.RES, new DmmReadOptions { TimeoutMilliseconds = 8000 }, token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Log($"万用表读数异常: {ex.Message}");
                    item.UpdateMeasurement(null, "--", "FAIL", measured: true);
                    return;
                }

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
                var kohm = ohm / 1000.0;
                var text = kohm.ToString("0.###", CultureInfo.InvariantCulture);
                var pass = ohm >= PassThresholdOhm;

                item.UpdateMeasurement(kohm, text, pass ? "PASS" : "FAIL", measured: true);
                Log($"读数: {ohm:0.###} Ω ({text} kΩ) => {(pass ? "PASS" : "FAIL")}");
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
            try
            {
                _cts?.Cancel();
            }
            catch
            {
            }

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
        }

        private void RaiseCanExecuteChangedForItems()
        {
            foreach (var item in Items)
            {
                item.MeasureCommand?.RaiseCanExecuteChanged();
            }
        }

        private void Log(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            Logs.Add($"[{DateTime.Now:HH:mm:ss.fff}] {message}");
        }

        public void Dispose()
        {
            try
            {
                _cts?.Cancel();
            }
            catch
            {
            }

            _cts?.Dispose();
            _cts = null;

            _measureLock?.Dispose();
        }

        public sealed class ImpedanceItemViewModel : BindableBase
        {
            private readonly PowerToGroundImpedanceTestViewModel _owner;

            private string _impedanceKohmText = "--";
            private string _result = "--";
            private bool _isMeasured;

            internal ImpedanceItemViewModel(PowerToGroundImpedanceTestViewModel owner, int columnIndex, string powerName, string signalType, string signalPin, string groundPin)
            {
                _owner = owner;
                ColumnIndex = columnIndex;
                PowerName = powerName;
                SignalType = signalType;
                SignalPin = signalPin;
                GroundPin = groundPin;

                MeasureCommand = new DelegateCommand(async () => await _owner.MeasureAsync(this), () => _owner.CanMeasureItem(this));
            }

            public int ColumnIndex { get; }

            public string PowerName { get; }

            public string SignalType { get; }

            public string SignalPin { get; }

            public string GroundPin { get; }

            public string ImpedanceKohmText
            {
                get => _impedanceKohmText;
                private set => SetProperty(ref _impedanceKohmText, value);
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

            internal void UpdateMeasurement(double? valueKohm, string valueText, string result, bool measured)
            {
                ImpedanceKohmText = valueText;
                Result = result;
                IsMeasured = measured;
            }
        }
    }
}
