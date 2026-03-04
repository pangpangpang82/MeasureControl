using MeasureControl.Services;
using MeasureControl.Services.HardwareApis;
using MeasureControl.Drivers;
using Prism.Commands;
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
    public sealed class LatchModuleCircuitTestViewModel : BindableBase, IDisposable
    {
        private const string DmmIpAddress = "192.168.1.13";
        private const string MatrixIpAddress = "192.168.1.3";
        private const int MatrixTcpBasePort = 50200;

        private const string PowerSupplyIpAddress = "192.168.1.15";
        private const double InputCurrentA = 0.1;

        private const int MatrixSlotSequence = 6;
        private const int MatrixSlotCommon = 4;

        private readonly IDmmApi _dmm;
        private readonly IPxiChassisService _pxiChassisService;

        private IPowerSupplyApi _power;
        private ACTS6010Driver _resistor;

        private readonly SemaphoreSlim _opLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _resistorLock = new SemaphoreSlim(1, 1);

        private CancellationTokenSource _cts;

        private bool _isManualTestRunning;
        private bool _isAutoTestRunning;
        private bool _isBusy;

        private bool _isPowerOn;
        private string _powerStatus = "未供电";

        private string _overallResult = "--";
        private string _lastTestTime = "--";

        private PowerSupplyChannel _latchSupplyChannel = PowerSupplyChannel.CH3;

        private readonly Dictionary<string, string> _pinMatrixPointMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public LatchModuleCircuitTestViewModel(IDmmApi dmm, IPxiChassisService pxiChassisService)
        {
            _dmm = dmm;
            _pxiChassisService = pxiChassisService;

            ManualTestCommand = new DelegateCommand(async () => await OnManualTestAsync());
            AutoTestCommand = new DelegateCommand(async () => await OnAutoTestAsync());
            ClearLogCommand = new DelegateCommand(() => Logs.Clear());

            Items.Add(CreatePt500aItem());
            Items.Add(CreatePt1000aItem());
        }

        public ObservableCollection<LatchItemViewModel> Items { get; } = new ObservableCollection<LatchItemViewModel>();

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
                    RaiseCanExecuteChanged();
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
                    RaiseCanExecuteChanged();
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
                    RaiseCanExecuteChanged();
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

        public PowerSupplyChannel LatchSupplyChannel
        {
            get => _latchSupplyChannel;
            set => SetProperty(ref _latchSupplyChannel, value);
        }

        private LatchItemViewModel CreatePt500aItem()
        {
            var item = new LatchItemViewModel(this,
                title: "PT500A 锁存模块电路测试",
                roChannel: "RO0",
                supplyPin: "J34",
                measurePin: "J31",
                measurePinName: "T1_AWARN");

            item.Steps.Add(new LatchStepViewModel(this, item,
                stepName: "a)",
                actionDescription: "PT500A=730Ω",
                resistanceOhm: 730.0,
                inject3v3: false,
                expected: "高电平(3.3±0.33V)",
                evaluation: LatchEvaluation.High33));

            item.Steps.Add(new LatchStepViewModel(this, item,
                stepName: "b)",
                actionDescription: "PT500A=500Ω",
                resistanceOhm: 500.0,
                inject3v3: false,
                expected: "高电平(3.3±0.33V)",
                evaluation: LatchEvaluation.High33));

            item.Steps.Add(new LatchStepViewModel(this, item,
                stepName: "c)",
                actionDescription: "J34 供电3.3V",
                resistanceOhm: null,
                inject3v3: true,
                expected: "低电平(0±0.1V)",
                evaluation: LatchEvaluation.Low0));

            return item;
        }

        private LatchItemViewModel CreatePt1000aItem()
        {
            var item = new LatchItemViewModel(this,
                title: "PT1000A 锁存模块电路测试",
                roChannel: "RO1",
                supplyPin: "J35",
                measurePin: "J32",
                measurePinName: "T2_AWARN");

            item.Steps.Add(new LatchStepViewModel(this, item,
                stepName: "a)",
                actionDescription: "PT1000A=1500Ω",
                resistanceOhm: 1500.0,
                inject3v3: false,
                expected: "高电平(3.3±0.33V)",
                evaluation: LatchEvaluation.High33));

            item.Steps.Add(new LatchStepViewModel(this, item,
                stepName: "b)",
                actionDescription: "PT1000A=1000Ω",
                resistanceOhm: 1000.0,
                inject3v3: false,
                expected: "高电平(3.3±0.33V)",
                evaluation: LatchEvaluation.High33));

            item.Steps.Add(new LatchStepViewModel(this, item,
                stepName: "c)",
                actionDescription: "J35 供电3.3V",
                resistanceOhm: null,
                inject3v3: true,
                expected: "低电平(0±0.1V)",
                evaluation: LatchEvaluation.Low0));

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

                Log($"电源: CH1/CH2 28V, IP={PowerSupplyIpAddress}");
                await EnsureMainPowerAsync(28.0, _cts.Token).ConfigureAwait(false);
                IsPowerOn = true;
                PowerStatus = "已供电";

                if (!okCommon)
                {
                    await StopTestAsync().ConfigureAwait(false);
                    return;
                }

                Log($"锁存供电: {LatchSupplyChannel} => 3.3V (接线: PT500->J34, PT1000->J35)");
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

                Log($"电源: CH1/CH2 28V, IP={PowerSupplyIpAddress}");
                await EnsureMainPowerAsync(28.0, _cts.Token).ConfigureAwait(false);
                IsPowerOn = true;
                PowerStatus = "已供电";

                foreach (var item in Items)
                {
                    if (_cts.IsCancellationRequested)
                        return;

                    foreach (var step in item.Steps)
                    {
                        if (_cts.IsCancellationRequested)
                            return;

                        await ExecuteStepAsync(step, _cts.Token).ConfigureAwait(false);
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

        internal bool CanExecuteStep(LatchStepViewModel step)
        {
            if (step == null) return false;
            return IsManualTestRunning && !IsBusy && IsPowerOn;
        }

        internal async Task ExecuteStepAsync(LatchStepViewModel step)
        {
            if (step == null) return;
            var token = _cts?.Token ?? CancellationToken.None;
            await ExecuteStepAsync(step, token).ConfigureAwait(false);

            EvaluateOverall();
            if (Items.All(i => i.IsMeasured))
            {
                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            }
        }

        private async Task ExecuteStepAsync(LatchStepViewModel step, CancellationToken token)
        {
            await _opLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                IsBusy = true;

                if (step.ResistanceOhm != null)
                {
                    await ApplyResistanceAsync(step.Item, step.ResistanceOhm.Value, token).ConfigureAwait(false);
                }

                if (step.Inject3V3)
                {
                    await EnsureLatchSupplyAsync(step.Item, token).ConfigureAwait(false);
                }

                await MeasureAsync(step, token).ConfigureAwait(false);
            }
            finally
            {
                IsBusy = false;
                _opLock.Release();
            }
        }

        private async Task ApplyResistanceAsync(LatchItemViewModel item, double resistanceOhm, CancellationToken token)
        {
            await _resistorLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                Log($"设置电阻: {item.Title}, 通道={item.RoChannel}, 目标={resistanceOhm.ToString("0.###", CultureInfo.InvariantCulture)}Ω");

                var okReady = await EnsureResistorAsync(token).ConfigureAwait(false);
                if (!okReady)
                {
                    return;
                }

                var okRelay = await _resistor.SetRelayStateAsync(item.RoChannel, pathRelayClosed: true, shortCircuitClosed: false).ConfigureAwait(false);
                if (!okRelay)
                {
                    Log("设置继电器失败");
                    return;
                }

                var okWrite = await _resistor.WriteChannelAsync(item.RoChannel, resistanceOhm).ConfigureAwait(false);
                if (!okWrite)
                {
                    Log("设置电阻失败");
                    return;
                }

                await Task.Delay(50, token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log($"设置电阻异常: {ex.Message}");
            }
            finally
            {
                _resistorLock.Release();
            }
        }

        private async Task MeasureAsync(LatchStepViewModel step, CancellationToken token)
        {
            Log($"开始测量: {step.Item.MeasurePin}({step.Item.MeasurePinName}) {step.ActionDescription}");

            var matrixPoint = ResolveMatrixPointForPin(step.Item.MeasurePin);
            if (string.IsNullOrWhiteSpace(matrixPoint))
            {
                step.UpdateMeasurement(null, "--", "--", measured: true);
                Log($"未配置引脚矩阵映射: {step.Item.MeasurePin}");
                return;
            }

            var matrix = MatrixControlService.Instance;
            var ok = await matrix.ConnectNodesAsync("I1", matrixPoint, MatrixSlotSequence, MatrixIpAddress, MatrixTcpBasePort).ConfigureAwait(false);
            Log($"矩阵连接 I1-{matrixPoint}(slot{MatrixSlotSequence}) {(ok ? "OK" : "FAIL")}");
            if (!ok)
            {
                step.UpdateMeasurement(null, "--", "FAIL", measured: true);
                return;
            }

            try
            {
                var reading = await SafeReadVoltageAsync(token).ConfigureAwait(false);
                ApplyReading(step, reading);
            }
            finally
            {
                try
                {
                    _ = await matrix.DisconnectNodesAsync("I1", matrixPoint, MatrixSlotSequence, MatrixIpAddress, MatrixTcpBasePort).ConfigureAwait(false);
                }
                catch
                {
                }
            }
        }

        private async Task EnsureLatchSupplyAsync(LatchItemViewModel item, CancellationToken token)
        {
            _power ??= new PowerSupplySocketApi();
            await _power.ConnectAsync(PowerSupplyIpAddress, token).ConfigureAwait(false);
            await _power.ApplyAsync(LatchSupplyChannel, 3.3, InputCurrentA, token).ConfigureAwait(false);
            await _power.SetOutputEnabledAsync(LatchSupplyChannel, true, token).ConfigureAwait(false);
            await Task.Delay(300, token).ConfigureAwait(false);
            Log($"锁存供电已开启: {LatchSupplyChannel} 3.3V (请确认已接线到 {item.SupplyPin})");
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

        private void ApplyReading(LatchStepViewModel step, DmmReading reading)
        {
            if (reading == null)
            {
                step.UpdateMeasurement(null, "--", "FAIL", measured: true);
                return;
            }

            if (reading.IsOverrange)
            {
                step.UpdateMeasurement(null, "OL", "FAIL", measured: true);
                Log("读数为OL(过量程)，判为FAIL");
                return;
            }

            if (reading.Value == null)
            {
                step.UpdateMeasurement(null, "--", "FAIL", measured: true);
                return;
            }

            var v = reading.Value.Value;
            var text = v.ToString("0.###", CultureInfo.InvariantCulture);
            var pass = EvaluateVoltage(step.Evaluation, v);

            step.UpdateMeasurement(v, text, pass ? "PASS" : "FAIL", measured: true);
            Log($"读数: {v:0.###} V, 期望: {step.Expected} => {(pass ? "PASS" : "FAIL")}");
        }

        private static bool EvaluateVoltage(LatchEvaluation evaluation, double v)
        {
            switch (evaluation)
            {
                case LatchEvaluation.High33:
                    return Math.Abs(v - 3.3) <= 0.33;
                case LatchEvaluation.Low0:
                    return Math.Abs(v - 0.0) <= 0.1;
                default:
                    return false;
            }
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
                foreach (var step in item.Steps)
                {
                    step.UpdateMeasurement(null, "---", "--", measured: false);
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
                await _dmm.DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
            }

            try { await CleanupPowerAsync().ConfigureAwait(false); } catch { }
            try { await CleanupResistorAsync().ConfigureAwait(false); } catch { }

            IsPowerOn = false;
            PowerStatus = "未供电";

            RaiseCanExecuteChanged();
        }

        private void RaiseCanExecuteChanged()
        {
            foreach (var item in Items)
            {
                foreach (var step in item.Steps)
                {
                    step.ExecuteCommand?.RaiseCanExecuteChanged();
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

        private async Task EnsureMainPowerAsync(double voltageV, CancellationToken cancellationToken)
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
                    try { await _power.SetOutputEnabledAsync(LatchSupplyChannel, false, CancellationToken.None).ConfigureAwait(false); } catch { }
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

        private MeasureControl.Models.Devices.DeviceBase FindFirstActs6010Device()
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

            try { _opLock?.Dispose(); } catch { }
            try { _resistorLock?.Dispose(); } catch { }
        }

        public enum LatchEvaluation
        {
            High33,
            Low0
        }

        public sealed class LatchItemViewModel : BindableBase
        {
            internal LatchItemViewModel(LatchModuleCircuitTestViewModel owner, string title, string roChannel, string supplyPin, string measurePin, string measurePinName)
            {
                _ = owner;
                Title = title;
                RoChannel = roChannel;
                SupplyPin = supplyPin;
                MeasurePin = measurePin;
                MeasurePinName = measurePinName;
            }

            public string Title { get; }

            public string RoChannel { get; }

            public string SupplyPin { get; }

            public string MeasurePin { get; }

            public string MeasurePinName { get; }

            public ObservableCollection<LatchStepViewModel> Steps { get; } = new ObservableCollection<LatchStepViewModel>();

            public bool IsMeasured => Steps.All(s => s.IsMeasured);

            public bool IsPass => Steps.All(s => string.Equals(s.Result, "PASS", StringComparison.OrdinalIgnoreCase));
        }

        public sealed class LatchStepViewModel : BindableBase
        {
            private readonly LatchModuleCircuitTestViewModel _owner;

            private string _voltageText = "---";
            private string _result = "--";
            private bool _isMeasured;

            internal LatchStepViewModel(
                LatchModuleCircuitTestViewModel owner,
                LatchItemViewModel item,
                string stepName,
                string actionDescription,
                double? resistanceOhm,
                bool inject3v3,
                string expected,
                LatchEvaluation evaluation)
            {
                _owner = owner;
                Item = item;
                StepName = stepName;
                ActionDescription = actionDescription;
                ResistanceOhm = resistanceOhm;
                Inject3V3 = inject3v3;
                Expected = expected;
                Evaluation = evaluation;

                ExecuteCommand = new DelegateCommand(async () => await _owner.ExecuteStepAsync(this), () => _owner.CanExecuteStep(this));
            }

            public LatchItemViewModel Item { get; }

            public string StepName { get; }

            public string ActionDescription { get; }

            public double? ResistanceOhm { get; }

            public bool Inject3V3 { get; }

            public string Expected { get; }

            public LatchEvaluation Evaluation { get; }

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

            public DelegateCommand ExecuteCommand { get; }

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
