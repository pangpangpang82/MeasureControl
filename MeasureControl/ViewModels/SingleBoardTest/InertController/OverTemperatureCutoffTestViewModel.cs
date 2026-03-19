using MeasureControl.Events;
using MeasureControl.Models;
using MeasureControl.Models.Devices;
using MeasureControl.Services;
using MeasureControl.Services.HardwareApis;
using MeasureControl.Drivers;
using Prism.Commands;
using Prism.Events;
using Prism.Mvvm;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace MeasureControl.ViewModels.SingleBoardTest.InertController
{
    public sealed class OverTemperatureCutoffTestViewModel : BindableBase, IDisposable
    {
        private const string TestItemKey = "InertController_OverTemperatureCutoff";

        private const string FpgaServerIpAddress = "192.168.1.10";
        private const int FpgaServerPort = 5001;

        private const string DmmIpAddress = "192.168.1.13";
        private const string MatrixIpAddress = "192.168.1.3";
        private const int MatrixTcpBasePort = 50200;

        private const string PowerSupplyIpAddress = "192.168.1.15";
        private const double InputCurrentA = 1.0;

        private const int MatrixSlotSequence = 6;
        private const int MatrixSlotCommon = 4;

        private readonly ISingleBoardTestContextService _singleBoardTestContext;
        private readonly ProjectService _projectService;
        private readonly IEventAggregator _eventAggregator;
        private readonly IDmmApi _dmm;
        private readonly IPxiChassisService _pxiChassisService;

        private IPowerSupplyApi _power;
        private IPxi7012Api _resistor;
        private uint? _connectedResistorLogicalId;
        private JY7131Driver _diDriver;

        private TcpClient _fpgaClient;
        private NetworkStream _fpgaStream;
        private CancellationTokenSource _fpgaRxCts;
        private Task _fpgaRxTask;

        private readonly SemaphoreSlim _fpgaSendLock = new SemaphoreSlim(1, 1);
        private TaskCompletionSource<uint> _fpgaForceReadTcs;

        private uint? _lastFpgaGpioInput;
        private DateTime? _lastFpgaGpioInputTime;
        private volatile bool _isFpgaCaptureEnabled;

        private readonly SemaphoreSlim _measureLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _resistorLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _diLock = new SemaphoreSlim(1, 1);

        private CancellationTokenSource _cts;

        private bool _isManualTestRunning;
        private bool _isAutoTestRunning;
        private bool _isBusy;

        private bool _isPowerOn;
        private string _powerStatus = "未供电";

        private string _overallResult = "--";
        private string _lastTestTime = "--";

        private readonly Dictionary<string, string> _pinMatrixPointMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public OverTemperatureCutoffTestViewModel(
            ISingleBoardTestContextService singleBoardTestContext,
            ProjectService projectService,
            IEventAggregator eventAggregator,
            IDmmApi dmm,
            IPxiChassisService pxiChassisService)
        {
            _singleBoardTestContext = singleBoardTestContext;
            _projectService = projectService;
            _eventAggregator = eventAggregator;
            _dmm = dmm;
            _pxiChassisService = pxiChassisService;

            ManualTestCommand = new DelegateCommand(async () => await OnManualTestAsync());
            AutoTestCommand = new DelegateCommand(async () => await OnAutoTestAsync());
            ClearLogCommand = new DelegateCommand(() => Logs.Clear());

            Items.Add(CreatePt500aItem());
            Items.Add(CreatePt1000aItem());
        }

        public ObservableCollection<OverTempItemViewModel> Items { get; } = new ObservableCollection<OverTempItemViewModel>();

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

        private static int? MapPinToIo43To64BitIndex(string pin)
        {
            if (string.Equals(pin, "J31", StringComparison.OrdinalIgnoreCase))
                return 0;
            if (string.Equals(pin, "J32", StringComparison.OrdinalIgnoreCase))
                return 1;
            return null;
        }

        private static bool GetIo43To64Bit(uint gpioValue, int bitIndex)
        {
            if (bitIndex < 0 || bitIndex > 21)
                return false;
            return ((gpioValue >> bitIndex) & 0x1u) == 1u;
        }

        private async Task MeasureFpgaIoAsync(OverTempCheckViewModel check, CancellationToken token)
        {
            var bitIndex = MapPinToIo43To64BitIndex(check.Pin);
            if (bitIndex == null)
            {
                check.UpdateMeasurement(null, "--", "FAIL", measured: true);
                Log($"未配置FPGA IO映射: {check.Pin}");
                return;
            }

            await EnsureFpgaTcpConnectedAsync(token).ConfigureAwait(false);

            TaskCompletionSource<uint> tcs;
            lock (this)
            {
                _fpgaForceReadTcs = new TaskCompletionSource<uint>(TaskCreationOptions.RunContinuationsAsynchronously);
                tcs = _fpgaForceReadTcs;
            }

            try
            {
                await SendFpgaFrameAsync(0x0A, new byte[] { 0x00 }, token).ConfigureAwait(false);
                Log("[FPGA TX] Force Read: AA 55 02 0A 00");

                var completed = await Task.WhenAny(tcs.Task, Task.Delay(2000, token)).ConfigureAwait(false);
                if (completed != tcs.Task)
                {
                    token.ThrowIfCancellationRequested();
                    check.UpdateMeasurement(null, "未接收", "FAIL", measured: true);
                    Log("等待FPGA强制读取回包超时(2000ms)");
                    return;
                }

                var gpio = await tcs.Task.ConfigureAwait(false);
                var isHigh = GetIo43To64Bit(gpio, bitIndex.Value);

                var valueText = isHigh ? "高电平" : "低电平";
                var pass = isHigh;
                check.UpdateMeasurement(isHigh ? 1.0 : 0.0, valueText, pass ? "PASS" : "FAIL", measured: true);

                var ioNumber = 43 + bitIndex.Value;
                var ts = _lastFpgaGpioInputTime?.ToString("HH:mm:ss", CultureInfo.InvariantCulture) ?? "--";
                Log($"FPGA IO读取: {check.Pin}=IO{ioNumber}(bit{bitIndex.Value}) => {valueText} => {(pass ? "PASS" : "FAIL")}, 数据时间={ts}");
            }
            finally
            {
                lock (this)
                {
                    if (ReferenceEquals(_fpgaForceReadTcs, tcs))
                        _fpgaForceReadTcs = null;
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

        public uint? LastFpgaGpioInput
        {
            get => _lastFpgaGpioInput;
            private set => SetProperty(ref _lastFpgaGpioInput, value);
        }

        private OverTempItemViewModel CreatePt500aItem()
        {
            var item = new OverTempItemViewModel(this,
                title: "PT500A 超温切断(模拟112℃)",
                resistanceLabel: "(715.25±3.5)Ω",
                targetResistanceOhm: 715.25,
                resistanceToleranceOhm: 3.5,
                roChannel: "RO0");

            item.Checks.Add(new OverTempCheckViewModel(this, item,
                pin: "J31",
                pinName: "T1_AWARN",
                expected: "高电平(3.3±0.33V)",
                evaluation: OverTempEvaluation.High33));

            item.Checks.Add(new OverTempCheckViewModel(this, item,
                pin: "J11",
                pinName: "IIV +28VDC PWR IN_FB",
                expected: "开路",
                evaluation: OverTempEvaluation.DIOpen,
                diChannel: "DI1"));

            item.Checks.Add(new OverTempCheckViewModel(this, item,
                pin: "J12",
                pinName: "IIV +28VDC PWR IN",
                expected: "开路",
                evaluation: OverTempEvaluation.DIOpen,
                diChannel: "DI2"));

            return item;
        }

        private OverTempItemViewModel CreatePt1000aItem()
        {
            var item = new OverTempItemViewModel(this,
                title: "PT1000A 超温切断(模拟107℃)",
                resistanceLabel: "(1411.6±7.1)Ω",
                targetResistanceOhm: 1411.6,
                resistanceToleranceOhm: 7.1,
                roChannel: "RO1");

            item.Checks.Add(new OverTempCheckViewModel(this, item,
                pin: "J32",
                pinName: "T2_AWARN",
                expected: "高电平(3.3±0.33V)",
                evaluation: OverTempEvaluation.High33));

            item.Checks.Add(new OverTempCheckViewModel(this, item,
                pin: "J13",
                pinName: "TIV +28VDC PWR IN_FB",
                expected: "开路",
                evaluation: OverTempEvaluation.DIOpen,
                diChannel: "DI3"));

            item.Checks.Add(new OverTempCheckViewModel(this, item,
                pin: "J14",
                pinName: "TIV +28VDC PWR IN",
                expected: "开路",
                evaluation: OverTempEvaluation.DIOpen,
                diChannel: "DI4"));

            return item;
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
            OverallResult = "--";
            LastTestTime = "--";
            _isFpgaCaptureEnabled = false;
            LastFpgaGpioInput = null;
            _lastFpgaGpioInputTime = null;

            IsManualTestRunning = true;
            IsAutoTestRunning = false;

            Log("开始手动测试");

            try
            {
                await EnsureFpgaTcpConnectedAsync(_cts.Token).ConfigureAwait(false);

                Log($"电源: CH1 28V 1A, IP={PowerSupplyIpAddress}");
                await EnsurePowerAsync(28.0, _cts.Token).ConfigureAwait(false);
                IsPowerOn = true;
                PowerStatus = "已供电";
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
            OverallResult = "--";
            LastTestTime = "--";
            _isFpgaCaptureEnabled = false;
            LastFpgaGpioInput = null;
            _lastFpgaGpioInputTime = null;

            IsAutoTestRunning = true;
            IsManualTestRunning = false;

            Log("开始自动测试");

            try
            {
                await EnsureFpgaTcpConnectedAsync(_cts.Token).ConfigureAwait(false);

                Log($"电源: CH1 28V 1A, IP={PowerSupplyIpAddress}");
                await EnsurePowerAsync(28.0, _cts.Token).ConfigureAwait(false);
                IsPowerOn = true;
                PowerStatus = "已供电";

                foreach (var item in Items)
                {
                    if (_cts.IsCancellationRequested)
                        return;

                    await ApplyResistanceAsync(item, _cts.Token).ConfigureAwait(false);
                    await Task.Delay(100, _cts.Token).ConfigureAwait(false);

                    foreach (var check in item.Checks)
                    {
                        if (_cts.IsCancellationRequested)
                            return;

                        await MeasureAsync(check, _cts.Token).ConfigureAwait(false);
                        await Task.Delay(100, _cts.Token).ConfigureAwait(false);
                    }
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

        internal bool CanApplyResistance(OverTempItemViewModel item)
        {
            if (item == null) return false;
            return IsManualTestRunning && !IsBusy && IsPowerOn;
        }

        internal bool CanMeasureCheck(OverTempCheckViewModel check)
        {
            if (check == null) return false;
            return IsManualTestRunning && !IsBusy && IsPowerOn;
        }

        internal async Task ApplyResistanceAsync(OverTempItemViewModel item)
        {
            if (item == null) return;
            var token = _cts?.Token ?? CancellationToken.None;
            await ApplyResistanceAsync(item, token).ConfigureAwait(false);

            EvaluateOverall();
        }

        internal async Task MeasureAsync(OverTempCheckViewModel check)
        {
            if (check == null) return;
            var token = _cts?.Token ?? CancellationToken.None;
            await MeasureAsync(check, token).ConfigureAwait(false);

            EvaluateOverall();
            if (Items.All(i => i.IsMeasured))
            {
                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            }
        }

        private async Task ApplyResistanceAsync(OverTempItemViewModel item, CancellationToken token)
        {
            await _resistorLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                IsBusy = true;

                Log($"设置电阻: {item.Title}, 通道={item.RoChannel}, 目标={item.TargetResistanceOhm.ToString("0.###", CultureInfo.InvariantCulture)}Ω");

                var okReady = await EnsureResistorAsync(token).ConfigureAwait(false);
                if (!okReady)
                {
                    item.UpdateResistance(null, "--", measured: true);
                    return;
                }

                var apiChannel = MapRoChannelTo7012Api(item.RoChannel);
                try
                {
                    await _resistor.SetRelayStateAsync(apiChannel, pathRelayClosed: true, shortCircuitClosed: false, token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Log($"设置7012继电器失败: {ex.Message}");
                    item.UpdateResistance(null, "--", measured: true);
                    return;
                }

                try
                {
                    await _resistor.SetResistanceAsync(apiChannel, item.TargetResistanceOhm, Pxi7012OutputMode.NoWait, token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Log($"设置7012电阻失败: {ex.Message}");
                    item.UpdateResistance(null, "--", measured: true);
                    return;
                }

                await Task.Delay(50, token).ConfigureAwait(false);
                double? r = null;
                try
                {
                    r = await _resistor.GetResistanceAsync(apiChannel, token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Log($"电阻回读异常: {ex.Message}");
                }

                item.UpdateResistance(r, "--", measured: true);

                Log($"电阻回读: {(r == null ? "--" : r.Value.ToString("0.###", CultureInfo.InvariantCulture))}Ω");

                _isFpgaCaptureEnabled = true;
                LastFpgaGpioInput = null;
                _lastFpgaGpioInputTime = null;
                Log("已启用FPGA数据保存(电阻输出后)");
            }
            catch (Exception ex)
            {
                Log($"设置电阻异常: {ex.Message}");
                item.UpdateResistance(null, "--", measured: true);
            }
            finally
            {
                IsBusy = false;
                _resistorLock.Release();
            }
        }
        private async Task MeasureAsync(OverTempCheckViewModel check, CancellationToken token)
        {
            if (check == null) return;

            await _measureLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                IsBusy = true;

                Log($"开始测量: {check.Pin}({check.PinName})");

                if (string.Equals(check.Pin, "J31", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(check.Pin, "J32", StringComparison.OrdinalIgnoreCase))
                {
                    await MeasureFpgaIoAsync(check, token).ConfigureAwait(false);
                    return;
                }

                if (!string.IsNullOrWhiteSpace(check.DiChannel))
                {
                    await MeasureDIAsync(check, token).ConfigureAwait(false);
                }
                else
                {
                    var matrixPoint = ResolveMatrixPointForPin(check.Pin);
                    if (string.IsNullOrWhiteSpace(matrixPoint))
                    {
                        check.UpdateMeasurement(null, "--", "--", measured: true);
                        Log($"未配置引脚矩阵映射: {check.Pin}");
                        return;
                    }

                    var matrix = MatrixControlService.Instance;
                    var ok = await matrix.ConnectNodesAsync("I1", matrixPoint, MatrixSlotSequence, MatrixIpAddress, MatrixTcpBasePort).ConfigureAwait(false);
                    Log($"矩阵连接 I1-{matrixPoint}(slot{MatrixSlotSequence}) {(ok ? "OK" : "FAIL")}");
                    if (!ok)
                    {
                        check.UpdateMeasurement(null, "--", "FAIL", measured: true);
                        return;
                    }

                    var reading = await SafeReadVoltageAsync(token).ConfigureAwait(false);
                    ApplyReading(check, reading);

                    try
                    {
                        _ = await matrix.DisconnectNodesAsync("I1", matrixPoint, MatrixSlotSequence, MatrixIpAddress, MatrixTcpBasePort).ConfigureAwait(false);
                    }
                    catch
                    {
                    }
                }
            }
            finally
            {
                IsBusy = false;
                _measureLock.Release();
            }
        }

        private async Task<DmmReading> SafeReadVoltageAsync(CancellationToken token)
        {
            try
            {
                return await _dmm.ReadOnceAsync(DmmMeasureMode.DCV, new DmmReadOptions { TimeoutMilliseconds = 8000 }, token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log($"万用表读数异常: {ex.Message}");
                return null;
            }
        }

        private void ApplyReading(OverTempCheckViewModel check, DmmReading reading)
        {
            if (reading == null)
            {
                check.UpdateMeasurement(null, "--", "FAIL", measured: true);
                return;
            }

            if (reading.IsOverrange)
            {
                check.UpdateMeasurement(null, "OL", "FAIL", measured: true);
                Log("读数为OL(过量程)，判为FAIL");
                return;
            }

            if (reading.Value == null)
            {
                check.UpdateMeasurement(null, "--", "FAIL", measured: true);
                return;
            }

            var v = reading.Value.Value;
            var text = v.ToString("0.###", CultureInfo.InvariantCulture);
            var pass = EvaluateCheckVoltage(check.Evaluation, v);
            check.UpdateMeasurement(v, text, pass ? "PASS" : "FAIL", measured: true);
            Log($"读数: {v:0.###} V, 期望: {check.Expected} => {(pass ? "PASS" : "FAIL")}");
        }

        private async Task MeasureDIAsync(OverTempCheckViewModel check, CancellationToken token)
        {
            try
            {
                var okReady = await EnsureDIDriverAsync(token).ConfigureAwait(false);
                if (!okReady)
                {
                    check.UpdateMeasurement(null, "--", "FAIL", measured: true);
                    Log($"7131板卡未连接");
                    return;
                }

                await _diLock.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    var diValue = await _diDriver.ReadChannelAsync(check.DiChannel).ConfigureAwait(false);
                    var isHigh = diValue > 0.5;
                    var stateText = isHigh ? "GND" : "开路";
                    var pass = EvaluateDICheck(check.Evaluation, isHigh);
                    
                    check.UpdateMeasurement(diValue, stateText, pass ? "PASS" : "FAIL", measured: true);
                    Log($"DI读数: {check.DiChannel}={stateText} => {(pass ? "PASS" : "FAIL")}");
                }
                finally
                {
                    _diLock.Release();
                }
            }
            catch (Exception ex)
            {
                Log($"DI测量异常: {ex.Message}");
                check.UpdateMeasurement(null, "--", "FAIL", measured: true);
            }
        }

        private static bool EvaluateCheckVoltage(OverTempEvaluation evaluation, double v)
        {
            switch (evaluation)
            {
                case OverTempEvaluation.High33:
                    return Math.Abs(v - 3.3) <= 0.33;
                case OverTempEvaluation.OpenLe16:
                    return v <= 16.0;
                case OverTempEvaluation.DIOpen:
                case OverTempEvaluation.DIGND:
                    return false;
                default:
                    return false;
            }
        }

        private static bool EvaluateDICheck(OverTempEvaluation evaluation, bool isHigh)
        {
            switch (evaluation)
            {
                case OverTempEvaluation.DIOpen:
                    return !isHigh;
                case OverTempEvaluation.DIGND:
                    return isHigh;
                default:
                    return false;
            }
        }

        private static bool IsResistanceInRange(OverTempItemViewModel item, double? r)
        {
            if (item == null || r == null) return false;
            return Math.Abs(r.Value - item.TargetResistanceOhm) <= item.ResistanceToleranceOhm;
        }

        private string ResolveMatrixPointForPin(string pin)
        {
            if (string.IsNullOrWhiteSpace(pin))
                return null;

            if (_pinMatrixPointMap.TryGetValue(pin.Trim(), out var point))
                return point;

            return null;
        }

        private void ResetResults()
        {
            foreach (var item in Items)
            {
                item.UpdateResistance(null, "--", measured: false);
                foreach (var check in item.Checks)
                {
                    check.UpdateMeasurement(null, "---", "--", measured: false);
                }
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

            OverallResult = Items.All(i => i.IsPass) ? "PASS" : "FAIL";
        }

        private async Task StopTestAsync()
        {
            try { _cts?.Cancel(); } catch { }

            IsManualTestRunning = false;
            IsAutoTestRunning = false;
            IsBusy = false;
            _isFpgaCaptureEnabled = false;

            try
            {
                await _dmm.DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
            }

            try { await DisconnectFpgaTcpAsync().ConfigureAwait(false); } catch { }

            try { await CleanupPowerAsync().ConfigureAwait(false); } catch { }
            try { await CleanupResistorAsync().ConfigureAwait(false); } catch { }

            IsPowerOn = false;
            PowerStatus = "未供电";

            RaiseCanExecuteChangedForItems();
        }

        private void RaiseCanExecuteChangedForItems()
        {
            try
            {
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher != null && !dispatcher.CheckAccess())
                {
                    dispatcher.BeginInvoke(new Action(RaiseCanExecuteChangedForItems));
                    return;
                }
            }
            catch
            {
            }

            foreach (var item in Items)
            {
                item.ApplyResistanceCommand?.RaiseCanExecuteChanged();
                foreach (var check in item.Checks)
                {
                    check.MeasureCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        private void Log(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
            try
            {
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher != null && !dispatcher.CheckAccess())
                {
                    dispatcher.BeginInvoke(new Action(() => Logs.Add(line)));
                }
                else
                {
                    Logs.Add(line);
                }
            }
            catch
            {
            }
        }

        private async Task EnsurePowerAsync(double voltageV, CancellationToken cancellationToken)
        {
            _power ??= new PowerSupplySocketApi();
            await _power.ConnectAsync(PowerSupplyIpAddress, cancellationToken).ConfigureAwait(false);
            await _power.ApplyAsync(PowerSupplyChannel.CH1, voltageV, InputCurrentA, cancellationToken).ConfigureAwait(false);
            await _power.SetOutputEnabledAsync(PowerSupplyChannel.CH1, true, cancellationToken).ConfigureAwait(false);
            await Task.Delay(300, cancellationToken).ConfigureAwait(false);
        }

        private async Task CleanupPowerAsync()
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
            finally
            {
                _power = null;
            }
        }

        private async Task<bool> EnsureResistorAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_resistor != null && _resistor.IsConnected)
                return true;

            await CleanupResistorAsync().ConfigureAwait(false);

            var candidates = new uint[] { 1, 0, 2, 3, 4, 5, 6, 7 };
            foreach (var logicalId in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
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
                    await api.ConnectAsync(cancellationToken).ConfigureAwait(false);

                    _resistor = api;
                    _connectedResistorLogicalId = logicalId;
                    Log($"7012连接成功：逻辑ID={logicalId}");
                    return true;
                }
                catch
                {
                    try
                    {
                        if (_resistor != null)
                            await _resistor.DisposeAsync().ConfigureAwait(false);
                    }
                    catch
                    {
                    }
                    _resistor = null;
                    _connectedResistorLogicalId = null;
                }
            }

            return false;
        }

        private async Task CleanupResistorAsync()
        {
            try
            {
                if (_resistor != null)
                {
                    try { await _resistor.DisposeAsync().ConfigureAwait(false); } catch { }
                }
            }
            finally
            {
                _resistor = null;
                _connectedResistorLogicalId = null;
            }
        }

        private static string MapRoChannelTo7012Api(string roChannel)
        {
            if (string.IsNullOrWhiteSpace(roChannel))
                throw new ArgumentException("RO channel is required", nameof(roChannel));

            var raw = roChannel.Trim();
            if (!raw.StartsWith("RO", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("RO channel must start with 'RO'", nameof(roChannel));

            if (!int.TryParse(raw.Substring(2), NumberStyles.Integer, CultureInfo.InvariantCulture, out var idx))
                throw new ArgumentException("Invalid RO channel index", nameof(roChannel));

            // ViewModel uses 0-based (RO0/RO1). Pxi7012Api public contract is 1-based (RO1..RO9).
            return $"RO{idx + 1}";
        }

        private async Task<bool> EnsureDIDriverAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_diDriver != null && _diDriver.IsConnected)
                return true;

            var device = FindFirst7131Device();
            if (device == null)
            {
                Log("未找到JY7131(数字量输入输出)板卡");
                return false;
            }

            _diDriver = new JY7131Driver(device, slotNumber: 0);
            var ok = await _diDriver.ConnectAsync().ConfigureAwait(false);
            if (!ok)
            {
                Log("JY7131连接失败");
                return false;
            }

            Log("JY7131连接成功");
            return true;
        }

        private async Task CleanupDIDriverAsync()
        {
            try
            {
                if (_diDriver != null)
                {
                    try { await _diDriver.DisconnectAsync().ConfigureAwait(false); } catch { }
                }
            }
            finally
            {
                _diDriver = null;
            }
        }

        private DeviceBase FindFirst7131Device()
        {
            var chassisList = _pxiChassisService?.GetAllChassis();
            if (chassisList == null)
                return null;

            foreach (var chassis in chassisList)
            {
                var device = chassis?.Devices?.FirstOrDefault(d =>
                    (d?.Model?.IndexOf("7131", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.Model?.IndexOf("JY7131", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.Name?.IndexOf("7131", StringComparison.OrdinalIgnoreCase) >= 0));

                if (device != null)
                    return device;
            }

            return null;
        }

        // (Removed) FindFirstActs6010Device: over-temperature cutoff test uses PXI-7012 for resistance output.

        public void Dispose()
        {
            try { _cts?.Cancel(); } catch { }
            _cts?.Dispose();
            _cts = null;

            try { DisconnectFpgaTcpAsync().GetAwaiter().GetResult(); } catch { }

            try { CleanupPowerAsync().GetAwaiter().GetResult(); } catch { }
            try { CleanupResistorAsync().GetAwaiter().GetResult(); } catch { }
            try { CleanupDIDriverAsync().GetAwaiter().GetResult(); } catch { }

            try { _measureLock?.Dispose(); } catch { }
            try { _resistorLock?.Dispose(); } catch { }
            try { _diLock?.Dispose(); } catch { }
        }

        private static readonly byte[] FpgaFrameHeader = { 0xAA, 0x55 };

        private static string FpgaTs()
        {
            return DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
        }

        private static string ToHex(byte[] data)
        {
            if (data == null || data.Length == 0) return "--";
            return string.Join(" ", data.Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));
        }

        private static byte[] BuildFpgaFrame(byte command, byte[] data)
        {
            var dataLen = data?.Length ?? 0;
            var lengthField = (byte)(1 + dataLen);
            var frame = new byte[2 + 1 + 1 + dataLen];
            frame[0] = FpgaFrameHeader[0];
            frame[1] = FpgaFrameHeader[1];
            frame[2] = lengthField;
            frame[3] = command;
            if (dataLen > 0)
                Buffer.BlockCopy(data, 0, frame, 4, dataLen);
            return frame;
        }

        private async Task SendFpgaFrameAsync(byte command, byte[] payload, CancellationToken token)
        {
            await EnsureFpgaTcpConnectedAsync(token).ConfigureAwait(false);
            if (_fpgaStream == null)
                throw new InvalidOperationException("FPGA未连接");

            await _fpgaSendLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                var frame = BuildFpgaFrame(command, payload);
                Log($"[{FpgaTs()}][FPGA TX] CMD=0x{command:X2} LEN={payload?.Length ?? 0} FRAME={ToHex(frame)}");
                await _fpgaStream.WriteAsync(frame, 0, frame.Length, token).ConfigureAwait(false);
                await _fpgaStream.FlushAsync(token).ConfigureAwait(false);
            }
            finally
            {
                _fpgaSendLock.Release();
            }
        }

        private async Task EnsureFpgaTcpConnectedAsync(CancellationToken token)
        {
            if (_fpgaClient?.Connected == true && _fpgaStream != null)
                return;

            await DisconnectFpgaTcpAsync().ConfigureAwait(false);

            var client = new TcpClient { NoDelay = true };
            try
            {
                using var timeoutCts = new CancellationTokenSource(2000);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCts.Token);

                var connectTask = client.ConnectAsync(FpgaServerIpAddress, FpgaServerPort);
                var cancelTask = Task.Delay(Timeout.Infinite, linkedCts.Token);
                var completed = await Task.WhenAny(connectTask, cancelTask).ConfigureAwait(false);
                if (completed != connectTask)
                {
                    try { client.Close(); } catch { }
                    token.ThrowIfCancellationRequested();
                    throw new TimeoutException($"FPGA连接超时(2s): {FpgaServerIpAddress}:{FpgaServerPort}");
                }

                await connectTask.ConfigureAwait(false);
                _fpgaClient = client;
                _fpgaStream = _fpgaClient.GetStream();

                _fpgaRxCts?.Cancel();
                _fpgaRxCts?.Dispose();
                _fpgaRxCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                _fpgaRxTask = Task.Run(() => FpgaReceiveLoopAsync(_fpgaRxCts.Token));

                Log($"FPGA TCP连接成功: {FpgaServerIpAddress}:{FpgaServerPort}");
            }
            catch (Exception ex)
            {
                try { client.Close(); } catch { }
                _fpgaClient = null;
                _fpgaStream = null;
                Log($"FPGA TCP连接失败: {ex.Message}");
                throw;
            }
        }

        private async Task DisconnectFpgaTcpAsync()
        {
            try { _fpgaRxCts?.Cancel(); } catch { }

            try
            {
                if (_fpgaRxTask != null)
                {
                    await Task.WhenAny(_fpgaRxTask, Task.Delay(300)).ConfigureAwait(false);
                }
            }
            catch
            {
            }

            try { _fpgaStream?.Close(); } catch { }
            try { _fpgaClient?.Close(); } catch { }

            _fpgaStream = null;
            _fpgaClient = null;

            try { _fpgaRxCts?.Dispose(); } catch { }
            _fpgaRxCts = null;
            _fpgaRxTask = null;
        }

        private async Task<byte[]> ReadExactFpgaAsync(int count, CancellationToken token)
        {
            var buf = new byte[count];
            var received = 0;
            while (received < count)
            {
                var n = await _fpgaStream.ReadAsync(buf, received, count - received, token).ConfigureAwait(false);
                if (n == 0)
                    throw new InvalidOperationException("FPGA连接已断开(读取0字节)");
                received += n;
            }
            return buf;
        }

        private async Task<(byte cmd, byte[] payload)> ReadFpgaFrameAsync(CancellationToken token)
        {
            var header = await ReadExactFpgaAsync(2, token).ConfigureAwait(false);
            if (header[0] != FpgaFrameHeader[0] || header[1] != FpgaFrameHeader[1])
                throw new InvalidOperationException($"FPGA帧头校验失败: 0x{header[0]:X2} 0x{header[1]:X2}");

            var lenBuf = await ReadExactFpgaAsync(1, token).ConfigureAwait(false);
            var totalLen = lenBuf[0];
            var body = await ReadExactFpgaAsync(totalLen, token).ConfigureAwait(false);

            var cmd = body[0];
            var payloadLen = totalLen - 1;
            var payload = new byte[payloadLen];
            if (payloadLen > 0)
                Buffer.BlockCopy(body, 1, payload, 0, payloadLen);

            return (cmd, payload);
        }

        private async Task FpgaReceiveLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (_fpgaStream == null)
                    {
                        await Task.Delay(50, token).ConfigureAwait(false);
                        continue;
                    }

                    var (cmd, payload) = await ReadFpgaFrameAsync(token).ConfigureAwait(false);

                    var payloadLen = payload?.Length ?? 0;
                    var frame = new byte[2 + 1 + 1 + payloadLen];
                    frame[0] = FpgaFrameHeader[0];
                    frame[1] = FpgaFrameHeader[1];
                    frame[2] = (byte)(1 + payloadLen);
                    frame[3] = cmd;
                    if (payloadLen > 0)
                        Buffer.BlockCopy(payload, 0, frame, 4, payloadLen);
                    Log($"[{FpgaTs()}][FPGA RX] CMD=0x{cmd:X2} LEN={payloadLen} FRAME={ToHex(frame)}");

                    if (cmd == 0x0A && payload != null && payload.Length >= 4)
                    {
                        TaskCompletionSource<uint> tcs;
                        lock (this)
                        {
                            tcs = _fpgaForceReadTcs;
                        }

                        if (tcs != null)
                        {
                            var v = BitConverter.ToUInt32(payload, 0);
                            LastFpgaGpioInput = v;
                            _lastFpgaGpioInputTime = DateTime.Now;
                            tcs.TrySetResult(v);
                        }
                    }

                    if (cmd == 0x00 && payload != null && payload.Length >= 4)
                    {
                        if (_isFpgaCaptureEnabled)
                        {
                            var v = BitConverter.ToUInt32(payload, 0);
                            LastFpgaGpioInput = v;
                            _lastFpgaGpioInputTime = DateTime.Now;
                        }
                    }

                    var hex = payload == null || payload.Length == 0
                        ? "--"
                        : string.Join(" ", payload.Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));
                    Log($"[FPGA RX] CMD=0x{cmd:X2} LEN={payload?.Length ?? 0} DATA={hex}");
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Log($"FPGA接收异常: {ex.Message}");
                    break;
                }
            }
        }

        public enum OverTempEvaluation
        {
            High33,
            OpenLe16,
            DIOpen,
            DIGND
        }

        public sealed class OverTempItemViewModel : BindableBase
        {
            private readonly OverTemperatureCutoffTestViewModel _owner;

            private string _measuredResistanceText = "---";
            private string _resistanceResult = "--";
            private bool _isResistanceMeasured;

            internal OverTempItemViewModel(
                OverTemperatureCutoffTestViewModel owner,
                string title,
                string resistanceLabel,
                double targetResistanceOhm,
                double resistanceToleranceOhm,
                string roChannel)
            {
                _owner = owner;
                Title = title;
                ResistanceLabel = resistanceLabel;
                TargetResistanceOhm = targetResistanceOhm;
                ResistanceToleranceOhm = resistanceToleranceOhm;
                RoChannel = roChannel;

                ApplyResistanceCommand = new DelegateCommand(async () => await _owner.ApplyResistanceAsync(this), () => _owner.CanApplyResistance(this));
            }

            public string Title { get; }

            public string ResistanceLabel { get; }

            public double TargetResistanceOhm { get; }

            public double ResistanceToleranceOhm { get; }

            public string RoChannel { get; set; }

            public ObservableCollection<OverTempCheckViewModel> Checks { get; } = new ObservableCollection<OverTempCheckViewModel>();

            public string MeasuredResistanceText
            {
                get => _measuredResistanceText;
                private set => SetProperty(ref _measuredResistanceText, value);
            }

            public string ResistanceResult
            {
                get => _resistanceResult;
                private set => SetProperty(ref _resistanceResult, value);
            }

            public bool IsResistanceMeasured
            {
                get => _isResistanceMeasured;
                private set => SetProperty(ref _isResistanceMeasured, value);
            }

            public bool IsMeasured => IsResistanceMeasured && Checks.All(c => c.IsMeasured);

            public bool IsPass => Checks.All(c => string.Equals(c.Result, "PASS", StringComparison.OrdinalIgnoreCase));

            public DelegateCommand ApplyResistanceCommand { get; }

            internal void UpdateResistance(double? valueOhm, string result, bool measured)
            {
                MeasuredResistanceText = valueOhm == null
                    ? "---"
                    : valueOhm.Value.ToString("0.###", CultureInfo.InvariantCulture);

                ResistanceResult = "--";
                IsResistanceMeasured = measured;
            }
        }

        public sealed class OverTempCheckViewModel : BindableBase
        {
            private readonly OverTemperatureCutoffTestViewModel _owner;
            private readonly OverTempItemViewModel _item;

            private string _voltageText = "---";
            private string _result = "--";
            private bool _isMeasured;

            internal OverTempCheckViewModel(OverTemperatureCutoffTestViewModel owner, OverTempItemViewModel item, string pin, string pinName, string expected, OverTempEvaluation evaluation, string diChannel = null)
            {
                _owner = owner;
                _item = item;
                Pin = pin;
                PinName = pinName;
                Expected = expected;
                Evaluation = evaluation;
                DiChannel = diChannel;

                MeasureCommand = new DelegateCommand(async () => await _owner.MeasureAsync(this), () => _owner.CanMeasureCheck(this));
            }

            public string Pin { get; }

            public string PinName { get; }

            public string Expected { get; }

            public OverTempEvaluation Evaluation { get; }

            public string DiChannel { get; }

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
                _ = valueVolt;
                VoltageText = valueText;
                Result = result;
                IsMeasured = measured;
            }
        }
    }
}
