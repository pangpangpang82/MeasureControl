using Prism.Commands;
using Prism.Mvvm;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using MeasureControl.Services;
using MeasureControl.Services.HardwareApis;

namespace MeasureControl.ViewModels.SingleBoardTest.HydraulicController
{
    public class HC_6_1ViewModel : BindableBase
    {
        private const string DmmIpAddress = "192.168.1.13";
        private const string MatrixIpAddress = "192.168.1.3";

        private const int MatrixSlotResistanceCh1 = 6;
        private const int MatrixSlotResistanceCh2 = 6;
        private const int MatrixSlotCommon = 4;

        private const double PassThresholdOhm = 500.0;

        private readonly SemaphoreSlim _measureLock = new SemaphoreSlim(1, 1);
        private CancellationTokenSource _manualCts;
        private CancellationTokenSource _autoCts;

        private IDmmApi _dmm;

        private bool _isManualTestRunning;
        private bool _isAutoTestRunning;

        private bool _canMeasure;

        private string _resistance14Text = "--";
        private string _resistance182Text = "--";

        private double? _resistance14;
        private double? _resistance182;

        private string _lastTestTime = "--";
        private string _lastTestResult = "--";
        private string _previousTestTime = "--";
        private string _previousTestResult = "--";

        public HC_6_1ViewModel()
        {
            ManualTestCommand = new DelegateCommand(async () => await OnManualTestAsync());
            AutoTestCommand = new DelegateCommand(async () => await OnAutoTestAsync());
            Measure14Command = new DelegateCommand(async () => await OnMeasure14Async(), () => CanMeasure);
            Measure182Command = new DelegateCommand(async () => await OnMeasure182Async(), () => CanMeasure);
            ClearLogCommand = new DelegateCommand(() => Logs.Clear());
        }

        public DelegateCommand ManualTestCommand { get; }
        public DelegateCommand AutoTestCommand { get; }

        public DelegateCommand Measure14Command { get; }

        public DelegateCommand Measure182Command { get; }

        public DelegateCommand ClearLogCommand { get; }

        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();

        public bool IsManualTestRunning
        {
            get => _isManualTestRunning;
            set => SetProperty(ref _isManualTestRunning, value);
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
                    Measure14Command?.RaiseCanExecuteChanged();
                    Measure182Command?.RaiseCanExecuteChanged();
                }
            }
        }

        private async Task OnAutoTestAsync()
        {
            if (IsAutoTestRunning)
            {
                await StopAutoTestAsync().ConfigureAwait(false);
                return;
            }

            // stop manual mode if it was running
            if (IsManualTestRunning)
            {
                await StopManualTestAsync().ConfigureAwait(false);
            }

            IsAutoTestRunning = true;
            CanMeasure = false;

            _resistance14 = null;
            _resistance182 = null;
            Resistance14Text = "--";
            Resistance182Text = "--";

            _autoCts?.Cancel();
            _autoCts?.Dispose();
            _autoCts = new CancellationTokenSource();

            Log("开始自动测试");
            Log($"判据: R14>{PassThresholdOhm:0}Ω && R182>{PassThresholdOhm:0}Ω");
            Log($"连接万用表 {DmmIpAddress} ...");

            try
            {
                _dmm ??= new DmmSocketApi();
                await _dmm.ConnectAsync(DmmIpAddress, _autoCts.Token).ConfigureAwait(false);
                Log("万用表连接成功");

                // 自动完成两路测量（测完矩阵立刻断开，逻辑已在 MeasureResistanceAsync 的 finally 中处理）
                await MeasureResistanceAsync(
                        name: "通道1(1-4)",
                        connect1: ("I1", "O8", MatrixSlotResistanceCh1),
                        connect2: ("I4", "O2", MatrixSlotCommon),
                        afterSetText: (v, text) => { _resistance14 = v; Resistance14Text = text; },
                        cancellationToken: _autoCts.Token)
                    .ConfigureAwait(false);

                // 给继电器/采样一点间隔，避免读数抖动
                await Task.Delay(120, _autoCts.Token).ConfigureAwait(false);

                await MeasureResistanceAsync(
                        name: "通道2(1-82)",
                        connect1: ("I1", "O9", MatrixSlotResistanceCh2),
                        connect2: ("I4", "O2", MatrixSlotCommon),
                        afterSetText: (v, text) => { _resistance182 = v; Resistance182Text = text; },
                        cancellationToken: _autoCts.Token)
                    .ConfigureAwait(false);

                await FinalizeIfBothMeasuredAsync(stopAfterFinalize: true, isAutoMode: true).ConfigureAwait(false);
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

        public string Resistance14Text
        {
            get => _resistance14Text;
            private set => SetProperty(ref _resistance14Text, value);
        }

        public string Resistance182Text
        {
            get => _resistance182Text;
            private set => SetProperty(ref _resistance182Text, value);
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

        private async Task OnManualTestAsync()
        {
            if (IsManualTestRunning)
            {
                await StopManualTestAsync().ConfigureAwait(false);
                return;
            }

            IsAutoTestRunning = false;
            IsManualTestRunning = true;
            CanMeasure = false;

            _resistance14 = null;
            _resistance182 = null;
            Resistance14Text = "--";
            Resistance182Text = "--";

            _manualCts?.Cancel();
            _manualCts?.Dispose();
            _manualCts = new CancellationTokenSource();

            Log("开始手动测试");
            Log($"连接万用表 {DmmIpAddress} ...");

            try
            {
                _dmm ??= new DmmSocketApi();
                await _dmm.ConnectAsync(DmmIpAddress, _manualCts.Token).ConfigureAwait(false);
                Log("万用表连接成功");
                CanMeasure = true;
            }
            catch (Exception ex)
            {
                Log($"万用表连接失败: {ex.Message}");
                await StopManualTestAsync().ConfigureAwait(false);
            }
        }

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

            Log("手动测试停止/结束，正在断开矩阵与万用表...");

            try
            {
                await DisconnectAllMatrixRoutesAsync().ConfigureAwait(false);
            }
            catch
            {
            }

            try
            {
                if (_dmm != null)
                {
                    await _dmm.DisconnectAsync().ConfigureAwait(false);
                }
            }
            catch
            {
            }

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

            Log("自动测试停止/结束，正在断开矩阵与万用表...");

            try
            {
                await DisconnectAllMatrixRoutesAsync().ConfigureAwait(false);
            }
            catch
            {
            }

            try
            {
                if (_dmm != null)
                {
                    await _dmm.DisconnectAsync().ConfigureAwait(false);
                }
            }
            catch
            {
            }

            IsAutoTestRunning = false;
            Log("自动测试已结束");
        }

        private async Task OnMeasure14Async()
        {
            await MeasureResistanceAsync(
                name: "通道1(1-4)",
                connect1: ("I1", "O8", MatrixSlotResistanceCh1),
                connect2: ("I4", "O2", MatrixSlotCommon),
                afterSetText: (v, text) => { _resistance14 = v; Resistance14Text = text; },
                cancellationToken: _manualCts?.Token ?? CancellationToken.None).ConfigureAwait(false);

            await TryFinalizeIfBothMeasuredAsync().ConfigureAwait(false);
        }

        private async Task OnMeasure182Async()
        {
            await MeasureResistanceAsync(
                name: "通道2(1-82)",
                connect1: ("I1", "O9", MatrixSlotResistanceCh2),
                connect2: ("I4", "O2", MatrixSlotCommon),
                afterSetText: (v, text) => { _resistance182 = v; Resistance182Text = text; },
                cancellationToken: _manualCts?.Token ?? CancellationToken.None).ConfigureAwait(false);

            await TryFinalizeIfBothMeasuredAsync().ConfigureAwait(false);
        }

        private async Task MeasureResistanceAsync(
            string name,
            (string In, string Out, int Slot) connect1,
            (string In, string Out, int Slot) connect2,
            Action<double?, string> afterSetText,
            CancellationToken cancellationToken)
        {
            if (!(IsAutoTestRunning || (IsManualTestRunning && CanMeasure)))
            {
                Log($"{name}: 当前未处于手动测试状态");
                return;
            }

            await _measureLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                Log($"{name}: 开始测量");

                var matrix = MatrixControlService.Instance;

                var ok1 = await matrix.ConnectNodesAsync(connect1.In, connect1.Out, connect1.Slot, MatrixIpAddress).ConfigureAwait(false);
                var ok2 = await matrix.ConnectNodesAsync(connect2.In, connect2.Out, connect2.Slot, MatrixIpAddress).ConfigureAwait(false);
                Log($"{name}: 矩阵连接 {(ok1 && ok2 ? "OK" : "FAIL")} - {connect1.In}-{connect1.Out}(slot{connect1.Slot}), {connect2.In}-{connect2.Out}(slot{connect2.Slot})");
                if (!ok1 || !ok2)
                {
                    afterSetText(null, "--");
                    return;
                }

                DmmReading reading = null;
                try
                {
                    reading = await _dmm.ReadOnceAsync(DmmMeasureMode.RES, new DmmReadOptions { TimeoutMilliseconds = 8000 }, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Log($"{name}: 电阻采集异常: {ex.Message}");
                }

                var value = reading?.Value;
                var text = FormatOhmText(reading);
                afterSetText(value, text);

                Log($"{name}: 读数 Raw={reading?.Raw ?? ""} Value={(value?.ToString("0.###", CultureInfo.InvariantCulture) ?? "--")} Unit={reading?.Unit ?? ""}");
                Log($"{name}: 阻抗={text}");
            }
            finally
            {
                try
                {
                    await DisconnectAllMatrixRoutesAsync().ConfigureAwait(false);
                }
                catch
                {
                }

                _measureLock.Release();
            }
        }

        private static string FormatOhmText(DmmReading reading)
        {
            if (reading == null)
                return "--";

            if (reading.IsOverrange)
                return "OL";

            if (reading.Value == null)
                return "--";

            return $"{reading.Value.Value:0.###} Ω";
        }

        private async Task DisconnectAllMatrixRoutesAsync()
        {
            var matrix = MatrixControlService.Instance;

            // disconnect all nodes that might be used by this test
            _ = await matrix.DisconnectNodesAsync("I1", "O8", MatrixSlotResistanceCh1, MatrixIpAddress).ConfigureAwait(false);
            _ = await matrix.DisconnectNodesAsync("I1", "O9", MatrixSlotResistanceCh2, MatrixIpAddress).ConfigureAwait(false);
            _ = await matrix.DisconnectNodesAsync("I4", "O2", MatrixSlotCommon, MatrixIpAddress).ConfigureAwait(false);
        }

        private async Task TryFinalizeIfBothMeasuredAsync()
        {
            await FinalizeIfBothMeasuredAsync(stopAfterFinalize: true, isAutoMode: false).ConfigureAwait(false);
        }

        private async Task FinalizeIfBothMeasuredAsync(bool stopAfterFinalize, bool isAutoMode)
        {
            if (_resistance14 == null || _resistance182 == null)
            {
                return;
            }

            var pass14 = _resistance14 > PassThresholdOhm;
            var pass182 = _resistance182 > PassThresholdOhm;
            var pass = pass14 && pass182;

            Log($"判据: R14>{PassThresholdOhm:0}Ω && R182>{PassThresholdOhm:0}Ω");
            Log($"R14={Resistance14Text} => {(pass14 ? "OK" : "NG")}");
            Log($"R182={Resistance182Text} => {(pass182 ? "OK" : "NG")}");

            PreviousTestTime = LastTestTime;
            PreviousTestResult = LastTestResult;
            LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            LastTestResult = pass ? "合格" : "不合格";
            Log($"最终结果: {LastTestResult}");

            if (!stopAfterFinalize)
            {
                return;
            }

            if (isAutoMode)
            {
                await StopAutoTestAsync().ConfigureAwait(false);
            }
            else
            {
                await StopManualTestAsync().ConfigureAwait(false);
            }
        }

        private void Log(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            var text = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
            Logs.Add(text);
        }
    }
}
