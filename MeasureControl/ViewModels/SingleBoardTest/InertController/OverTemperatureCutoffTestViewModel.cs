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
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace MeasureControl.ViewModels.SingleBoardTest.InertController
{
    public sealed class OverTemperatureCutoffTestViewModel : BindableBase, IDisposable
    {
        private const string TestItemKey = "InertController_OverTemperatureCutoff";

        private const string DmmIpAddress = "192.168.1.13";
        private const string MatrixIpAddress = "192.168.1.3";
        private const int MatrixTcpBasePort = 50200;

        private const string PowerSupplyIpAddress = "192.168.1.15";
        private const double InputCurrentA = 0.1;

        private const int MatrixSlotSequence = 6;
        private const int MatrixSlotCommon = 4;

        private readonly ISingleBoardTestContextService _singleBoardTestContext;
        private readonly ProjectService _projectService;
        private readonly IEventAggregator _eventAggregator;
        private readonly IDmmApi _dmm;
        private readonly IPxiChassisService _pxiChassisService;

        private IPowerSupplyApi _power;
        private ACTS6010Driver _resistor;

        private readonly SemaphoreSlim _measureLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _resistorLock = new SemaphoreSlim(1, 1);

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
            private set => SetProperty(ref _isPowerOn, value);
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
                expected: "开路(≤16V)",
                evaluation: OverTempEvaluation.OpenLe16));

            item.Checks.Add(new OverTempCheckViewModel(this, item,
                pin: "J12",
                pinName: "IIV +28VDC PWR IN",
                expected: "开路(≤16V)",
                evaluation: OverTempEvaluation.OpenLe16));

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
                expected: "开路(≤16V)",
                evaluation: OverTempEvaluation.OpenLe16));

            item.Checks.Add(new OverTempCheckViewModel(this, item,
                pin: "J14",
                pinName: "TIV +28VDC PWR IN",
                expected: "开路(≤16V)",
                evaluation: OverTempEvaluation.OpenLe16));

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

            IsManualTestRunning = true;
            IsAutoTestRunning = false;

            Log("开始手动测试");

            try
            {
                await _dmm.ConnectAsync(DmmIpAddress, _cts.Token).ConfigureAwait(false);
                Log($"万用表连接成功: {DmmIpAddress}");

                var matrix = MatrixControlService.Instance;
                var okCommon = await matrix.ConnectNodesAsync("I4", "O2", MatrixSlotCommon, MatrixIpAddress, MatrixTcpBasePort).ConfigureAwait(false);
                Log($"矩阵公共通路 I4-O2(slot{MatrixSlotCommon}) {(okCommon ? "OK" : "FAIL")}");

                Log($"电源: CH1/CH2, IP={PowerSupplyIpAddress}");
                await EnsurePowerAsync(28.0, _cts.Token).ConfigureAwait(false);
                IsPowerOn = true;
                PowerStatus = "已供电";

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
            OverallResult = "--";
            LastTestTime = "--";

            IsAutoTestRunning = true;
            IsManualTestRunning = false;

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

                Log($"电源: CH1/CH2, IP={PowerSupplyIpAddress}");
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
                    item.UpdateResistance(null, "FAIL", measured: true);
                    return;
                }

                var okRelay = await _resistor.SetRelayStateAsync(item.RoChannel, pathRelayClosed: true, shortCircuitClosed: false).ConfigureAwait(false);
                if (!okRelay)
                {
                    Log("设置继电器失败");
                    item.UpdateResistance(null, "FAIL", measured: true);
                    return;
                }

                var okWrite = await _resistor.WriteChannelAsync(item.RoChannel, item.TargetResistanceOhm).ConfigureAwait(false);
                if (!okWrite)
                {
                    Log("设置电阻失败");
                    item.UpdateResistance(null, "FAIL", measured: true);
                    return;
                }

                await Task.Delay(50, token).ConfigureAwait(false);
                double? r = null;
                try
                {
                    r = await _resistor.ReadChannelAsync(item.RoChannel).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Log($"电阻回读异常: {ex.Message}");
                }

                item.UpdateResistance(r, IsResistanceInRange(item, r) ? "PASS" : "FAIL", measured: true);

                Log($"电阻回读: {(r == null ? "--" : r.Value.ToString("0.###", CultureInfo.InvariantCulture))}Ω => {item.ResistanceResult}");
            }
            catch (Exception ex)
            {
                Log($"设置电阻异常: {ex.Message}");
                item.UpdateResistance(null, "FAIL", measured: true);
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
            }
            finally
            {
                try
                {
                    var matrixPoint = ResolveMatrixPointForPin(check.Pin);
                    if (!string.IsNullOrWhiteSpace(matrixPoint))
                    {
                        var matrix = MatrixControlService.Instance;
                        _ = await matrix.DisconnectNodesAsync("I1", matrixPoint, MatrixSlotSequence, MatrixIpAddress, MatrixTcpBasePort).ConfigureAwait(false);
                    }
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

        private static bool EvaluateCheckVoltage(OverTempEvaluation evaluation, double v)
        {
            switch (evaluation)
            {
                case OverTempEvaluation.High33:
                    return Math.Abs(v - 3.3) <= 0.33;
                case OverTempEvaluation.OpenLe16:
                    return v <= 16.0;
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
                foreach (var item in Items)
                {
                    foreach (var check in item.Checks)
                    {
                        var matrixPoint = ResolveMatrixPointForPin(check.Pin);
                        if (!string.IsNullOrWhiteSpace(matrixPoint))
                        {
                            var matrix = MatrixControlService.Instance;
                            _ = await matrix.DisconnectNodesAsync("I1", matrixPoint, MatrixSlotSequence, MatrixIpAddress, MatrixTcpBasePort).ConfigureAwait(false);
                        }
                    }
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

            try { await CleanupPowerAsync().ConfigureAwait(false); } catch { }
            try { await CleanupResistorAsync().ConfigureAwait(false); } catch { }

            IsPowerOn = false;
            PowerStatus = "未供电";

            RaiseCanExecuteChangedForItems();
        }

        private void RaiseCanExecuteChangedForItems()
        {
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
            await _power.ApplyAsync(PowerSupplyChannel.CH2, voltageV, InputCurrentA, cancellationToken).ConfigureAwait(false);
            await _power.SetOutputEnabledAsync(PowerSupplyChannel.CH1, true, cancellationToken).ConfigureAwait(false);
            await _power.SetOutputEnabledAsync(PowerSupplyChannel.CH2, true, cancellationToken).ConfigureAwait(false);
            await Task.Delay(300, cancellationToken).ConfigureAwait(false);
        }

        private async Task CleanupPowerAsync()
        {
            try
            {
                if (_power != null)
                {
                    try { await _power.SetOutputEnabledAsync(PowerSupplyChannel.CH1, false, CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _power.SetOutputEnabledAsync(PowerSupplyChannel.CH2, false, CancellationToken.None).ConfigureAwait(false); } catch { }
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

            var device = FindFirstActs6010Device();
            if (device == null)
            {
                Log("未找到ACTS6010(程控电阻)板卡");
                return false;
            }

            _resistor = new ACTS6010Driver(device, logicalId: 0);
            var ok = await _resistor.ConnectAsync().ConfigureAwait(false);
            if (!ok)
            {
                Log("ACTS6010连接失败");
                return false;
            }

            return true;
        }

        private async Task CleanupResistorAsync()
        {
            try
            {
                if (_resistor != null)
                {
                    try { await _resistor.DisconnectAsync().ConfigureAwait(false); } catch { }
                }
            }
            finally
            {
                _resistor = null;
            }
        }

        private DeviceBase FindFirstActs6010Device()
        {
            var chassisList = _pxiChassisService?.GetAllChassis();
            if (chassisList == null)
                return null;

            foreach (var chassis in chassisList)
            {
                var device = chassis?.Devices?.FirstOrDefault(d =>
                    (d?.Model?.IndexOf("6010", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.Model?.IndexOf("ACTS", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.Name?.IndexOf("6010", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.Name?.IndexOf("ACTS", StringComparison.OrdinalIgnoreCase) >= 0));

                if (device != null)
                    return device;
            }

            return null;
        }

        public void Dispose()
        {
            try { _cts?.Cancel(); } catch { }
            _cts?.Dispose();
            _cts = null;

            try { CleanupPowerAsync().GetAwaiter().GetResult(); } catch { }
            try { CleanupResistorAsync().GetAwaiter().GetResult(); } catch { }

            try { _measureLock?.Dispose(); } catch { }
            try { _resistorLock?.Dispose(); } catch { }
        }

        public enum OverTempEvaluation
        {
            High33,
            OpenLe16
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

            public bool IsPass => string.Equals(ResistanceResult, "PASS", StringComparison.OrdinalIgnoreCase)
                                && Checks.All(c => string.Equals(c.Result, "PASS", StringComparison.OrdinalIgnoreCase));

            public DelegateCommand ApplyResistanceCommand { get; }

            internal void UpdateResistance(double? valueOhm, string result, bool measured)
            {
                MeasuredResistanceText = valueOhm == null
                    ? "---"
                    : valueOhm.Value.ToString("0.###", CultureInfo.InvariantCulture);

                ResistanceResult = result;
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

            internal OverTempCheckViewModel(OverTemperatureCutoffTestViewModel owner, OverTempItemViewModel item, string pin, string pinName, string expected, OverTempEvaluation evaluation)
            {
                _owner = owner;
                _item = item;
                Pin = pin;
                PinName = pinName;
                Expected = expected;
                Evaluation = evaluation;

                MeasureCommand = new DelegateCommand(async () => await _owner.MeasureAsync(this), () => _owner.CanMeasureCheck(this));
            }

            public string Pin { get; }

            public string PinName { get; }

            public string Expected { get; }

            public OverTempEvaluation Evaluation { get; }

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
