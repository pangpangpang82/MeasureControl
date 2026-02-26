using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using MeasureControl.Events;
using MeasureControl.Models;
using MeasureControl.Services;
using MeasureControl.Services.HardwareApis;
using MeasureControl.Simulations.FuelController;
using Prism.Commands;
using Prism.Events;
using Prism.Mvvm;

namespace MeasureControl.ViewModels.SingleBoardTest.FuelController
{
    public sealed class DiscreteOutputTestViewModel : BindableBase, IDisposable
    {
        private const string TestItemKey = "FuelController_DiscreteOutput";

        private const double ImpedanceGroundedUpperLimitOhm = 10.0;
        private const double ImpedanceOpenLowerLimitOhm = 100000.0;
        private const double J14VoltageLowerLimitV = 16.0;

        private readonly ISingleBoardTestContextService _singleBoardTestContext;
        private readonly ProjectService _projectService;
        private readonly IEventAggregator _eventAggregator;
        private readonly IComponentPowerStateApi _componentPowerStateApi;
        private readonly IDmmApi _dmmApi;
        private readonly DiscreteOutputSimulation _simulation;

        private CancellationTokenSource _opCts;
        private SubscriptionToken _projectSavingToken;

        private bool _hardwareInitialized;
        private bool _isManualTestRunning;
        private bool _isAutoTestRunning;
        private bool _isBusy;
        private bool _useSimulatedDmm;

        private double? _impedanceGrounded;
        private double? _impedanceOpen;
        private double? _j14Voltage;

        private string _stepAResult = "--";
        private string _stepBResult = "--";
        private string _stepCResult = "--";

        private string _overallResult = "--";
        private string _lastTestTime = "--";

        public DiscreteOutputTestViewModel(
            ISingleBoardTestContextService singleBoardTestContext,
            ProjectService projectService,
            IEventAggregator eventAggregator,
            IComponentPowerStateApi componentPowerStateApi,
            IDmmApi dmmApi = null)
        {
            _singleBoardTestContext = singleBoardTestContext;
            _projectService = projectService;
            _eventAggregator = eventAggregator;
            _componentPowerStateApi = componentPowerStateApi;
            _dmmApi = dmmApi;
            _simulation = new DiscreteOutputSimulation();

            ManualTestCommand = new DelegateCommand(OnManualTest);
            AutoTestCommand = new DelegateCommand(OnAutoTest);
            StepACommand = new DelegateCommand(async () => await RunStepAAsync(), () => !IsBusy && IsManualTestRunning && _hardwareInitialized);
            StepBCommand = new DelegateCommand(async () => await RunStepBAsync(), () => !IsBusy && IsManualTestRunning && _hardwareInitialized);
            StepCCommand = new DelegateCommand(async () => await RunStepCAsync(), () => !IsBusy && IsManualTestRunning && _hardwareInitialized);
            ClearLogCommand = new DelegateCommand(() => Logs.Clear());

            LoadPersistedState();
            _projectSavingToken = _eventAggregator?.GetEvent<ProjectSavingEvent>()?.Subscribe(OnProjectSaving);
        }

        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();

        public DelegateCommand ManualTestCommand { get; }
        public DelegateCommand AutoTestCommand { get; }
        public DelegateCommand StepACommand { get; }
        public DelegateCommand StepBCommand { get; }
        public DelegateCommand StepCCommand { get; }
        public DelegateCommand ClearLogCommand { get; }

        public bool IsManualTestRunning
        {
            get => _isManualTestRunning;
            set
            {
                if (SetProperty(ref _isManualTestRunning, value))
                    UpdateCommandStates();
            }
        }

        public bool IsAutoTestRunning
        {
            get => _isAutoTestRunning;
            set
            {
                if (SetProperty(ref _isAutoTestRunning, value))
                    UpdateCommandStates();
            }
        }

        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                if (SetProperty(ref _isBusy, value))
                    UpdateCommandStates();
            }
        }

        public double? ImpedanceGrounded
        {
            get => _impedanceGrounded;
            set => SetProperty(ref _impedanceGrounded, value);
        }

        public double? ImpedanceOpen
        {
            get => _impedanceOpen;
            set => SetProperty(ref _impedanceOpen, value);
        }

        public double? J14Voltage
        {
            get => _j14Voltage;
            set => SetProperty(ref _j14Voltage, value);
        }

        public string StepAResult
        {
            get => _stepAResult;
            set => SetProperty(ref _stepAResult, value);
        }

        public string StepBResult
        {
            get => _stepBResult;
            set => SetProperty(ref _stepBResult, value);
        }

        public string StepCResult
        {
            get => _stepCResult;
            set => SetProperty(ref _stepCResult, value);
        }

        public string OverallResult
        {
            get => _overallResult;
            set => SetProperty(ref _overallResult, value);
        }

        public string LastTestTime
        {
            get => _lastTestTime;
            set => SetProperty(ref _lastTestTime, value);
        }

        private void AddLog(string msg)
        {
            var line = $"[{DateTime.Now:HH:mm:ss.fff}] {msg}";
            Application.Current?.Dispatcher?.Invoke(() => Logs.Add(line));
        }

        private void UpdateCommandStates()
        {
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                (StepACommand as DelegateCommand)?.RaiseCanExecuteChanged();
                (StepBCommand as DelegateCommand)?.RaiseCanExecuteChanged();
                (StepCCommand as DelegateCommand)?.RaiseCanExecuteChanged();
            });
        }

        private string PersistDataKey
        {
            get
            {
                var taskName = _singleBoardTestContext?.TestTaskName ?? string.Empty;
                if (string.IsNullOrWhiteSpace(taskName))
                    return TestItemKey;
                return $"{taskName}_{TestItemKey}";
            }
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

                StepAResult = Read("StepAResult") ?? "--";
                StepBResult = Read("StepBResult") ?? "--";
                StepCResult = Read("StepCResult") ?? "--";
                OverallResult = Read("OverallResult") ?? "--";
                LastTestTime = Read("LastTestTime") ?? "--";

                if (double.TryParse(Read("ImpedanceGrounded"), out var ig))
                    ImpedanceGrounded = ig;
                if (double.TryParse(Read("ImpedanceOpen"), out var io))
                    ImpedanceOpen = io;
                if (double.TryParse(Read("J14Voltage"), out var v))
                    J14Voltage = v;
            }
            catch { }
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
                    items = new List<TestInterfaceControlItem>();
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

                Upsert("StepAResult", StepAResult);
                Upsert("StepBResult", StepBResult);
                Upsert("StepCResult", StepCResult);
                Upsert("OverallResult", OverallResult);
                Upsert("LastTestTime", LastTestTime);
                Upsert("ImpedanceGrounded", ImpedanceGrounded?.ToString() ?? string.Empty);
                Upsert("ImpedanceOpen", ImpedanceOpen?.ToString() ?? string.Empty);
                Upsert("J14Voltage", J14Voltage?.ToString() ?? string.Empty);
            }
            catch { }
        }

        private async void OnManualTest()
        {
            if (IsManualTestRunning)
            {
                await StopManualTestAsync();
                return;
            }

            IsManualTestRunning = true;
            _opCts = new CancellationTokenSource();

            try
            {
                AddLog("========== 手动测试开始 ==========");
                await InitializeHardwareAsync(_opCts.Token);
                AddLog("硬件初始化完成，可以执行a/b/c步骤");
            }
            catch (OperationCanceledException)
            {
                AddLog("手动测试已取消");
            }
            catch (Exception ex)
            {
                AddLog($"手动测试异常: {ex.Message}");
            }
        }

        private async Task StopManualTestAsync()
        {
            try
            {
                AddLog("========== 手动测试停止中... ==========");
                _opCts?.Cancel();
                await SafeResetHardwareAsync();
            }
            catch (Exception ex)
            {
                AddLog($"停止手动测试异常: {ex.Message}");
            }
            finally
            {
                try { _opCts?.Dispose(); } catch { }
                _opCts = null;
                _hardwareInitialized = false;
                IsManualTestRunning = false;
                UpdateCommandStates();
                AddLog("========== 手动测试已停止 ==========");
            }
        }

        private async void OnAutoTest()
        {
            if (IsAutoTestRunning)
            {
                _opCts?.Cancel();
                return;
            }

            IsAutoTestRunning = true;
            _opCts = new CancellationTokenSource();

            try
            {
                AddLog("========== 自动测试开始 ==========");
                await InitializeHardwareAsync(_opCts.Token);

                AddLog("--- 步骤a: 下电 + DO接地 + 测阻抗 ---");
                await RunStepAAsync();

                AddLog("--- 步骤b: 下电 + DO开路 + 测阻抗 ---");
                await RunStepBAsync();

                AddLog("--- 步骤c: 28V上电 + 测J14电压 ---");
                await RunStepCAsync();

                await ResetHardwareAsync(_opCts.Token);

                bool overallPass =
                    string.Equals(StepAResult, "PASS", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(StepBResult, "PASS", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(StepCResult, "PASS", StringComparison.OrdinalIgnoreCase);

                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    OverallResult = overallPass ? "PASS" : "FAIL";
                    LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                });

                AddLog($"========== 自动测试完成: {(overallPass ? "PASS" : "FAIL")} ==========");
            }
            catch (OperationCanceledException)
            {
                AddLog("自动测试已取消");
                await SafeResetHardwareAsync();
            }
            catch (Exception ex)
            {
                AddLog($"自动测试异常: {ex.Message}");
                await SafeResetHardwareAsync();
            }
            finally
            {
                IsAutoTestRunning = false;
                _hardwareInitialized = false;
                UpdateCommandStates();
            }
        }

        private async Task InitializeHardwareAsync(CancellationToken token)
        {
            if (_hardwareInitialized)
                return;

            IsBusy = true;
            try
            {
                bool ok = await _simulation.ConnectMatrixAsync(AddLog, token);
                if (!ok)
                {
                    AddLog("矩阵开关配置失败，将继续使用仿真结果");
                }

                _hardwareInitialized = true;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task ResetHardwareAsync(CancellationToken token)
        {
            IsBusy = true;
            try
            {
                try
                {
                    await _componentPowerStateApi.ApplyComponentDownStateAsync(token);
                }
                catch
                {
                    await _simulation.ApplyComponentDownStateAsync(AddLog, token);
                }

                await _simulation.DisconnectMatrixAsync(AddLog, token);
                _hardwareInitialized = false;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task SafeResetHardwareAsync()
        {
            try
            {
                using (var cts = new CancellationTokenSource(1500))
                {
                    await ResetHardwareAsync(cts.Token);
                }
            }
            catch { }
        }

        private async Task RunStepAAsync()
        {
            if (_opCts == null)
                return;

            var token = _opCts.Token;
            IsBusy = true;
            try
            {
                await ApplyPowerDownAsync(token);
                await _simulation.SetDoGroundedAsync(AddLog, token);

                double ohm = await ReadImpedanceAsync(token);
                Application.Current?.Dispatcher?.Invoke(() => ImpedanceGrounded = ohm);

                bool pass = ohm < ImpedanceGroundedUpperLimitOhm;
                Application.Current?.Dispatcher?.Invoke(() => StepAResult = pass ? "PASS" : "FAIL");
                AddLog($"a) 对地阻抗={ohm}Ω，判据: <{ImpedanceGroundedUpperLimitOhm}Ω，结果={(pass ? "PASS" : "FAIL")}");

                UpdateOverallIfReady();
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task RunStepBAsync()
        {
            if (_opCts == null)
                return;

            var token = _opCts.Token;
            IsBusy = true;
            try
            {
                await ApplyPowerDownAsync(token);
                await _simulation.SetDoOpenAsync(AddLog, token);

                double ohm = await ReadImpedanceAsync(token);
                Application.Current?.Dispatcher?.Invoke(() => ImpedanceOpen = ohm);

                bool pass = ohm > ImpedanceOpenLowerLimitOhm;
                Application.Current?.Dispatcher?.Invoke(() => StepBResult = pass ? "PASS" : "FAIL");
                AddLog($"b) 对地阻抗={ohm}Ω，判据: >{ImpedanceOpenLowerLimitOhm}Ω，结果={(pass ? "PASS" : "FAIL")}");

                UpdateOverallIfReady();
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task RunStepCAsync()
        {
            if (_opCts == null)
                return;

            var token = _opCts.Token;
            IsBusy = true;
            try
            {
                await ApplyPower28VAsync(token);

                double v = await ReadJ14VoltageAsync(token);
                Application.Current?.Dispatcher?.Invoke(() => J14Voltage = v);

                bool pass = v >= J14VoltageLowerLimitV;
                Application.Current?.Dispatcher?.Invoke(() => StepCResult = pass ? "PASS" : "FAIL");
                AddLog($"c) J14电压={v}V，判据: ≥{J14VoltageLowerLimitV}V，结果={(pass ? "PASS" : "FAIL")}");

                UpdateOverallIfReady();
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task ApplyPowerDownAsync(CancellationToken token)
        {
            try
            {
                await _componentPowerStateApi.ApplyComponentDownStateAsync(token);
                AddLog("组件供电: 下电(真实API)");
            }
            catch
            {
                await _simulation.ApplyComponentDownStateAsync(AddLog, token);
            }
        }

        private async Task ApplyPower28VAsync(CancellationToken token)
        {
            try
            {
                await _componentPowerStateApi.ApplyComponent28VStateAsync(token);
                AddLog("组件供电: 28V上电(真实API)");
            }
            catch
            {
                await _simulation.ApplyComponent28VStateAsync(AddLog, token);
            }
        }

        private async Task<double> ReadImpedanceAsync(CancellationToken token)
        {
            if (_dmmApi == null)
            {
                _useSimulatedDmm = true;
                AddLog("阻抗来源: 仿真(未注入DMM API)");
                return await _simulation.MeasureImpedanceToGroundAsync(AddLog, token);
            }

            try
            {
                await EnsureDmmConnectedAsync(token);
                var reading = await _dmmApi.ReadOnceAsync(DmmMeasureMode.RES, new DmmReadOptions { TimeoutMilliseconds = 8000 }, token);
                if (reading?.Value == null)
                    throw new InvalidOperationException("DMM未返回阻抗值");

                _useSimulatedDmm = false;
                AddLog("阻抗来源: 万用表");
                return reading.Value.Value;
            }
            catch (Exception ex)
            {
                _useSimulatedDmm = true;
                AddLog($"万用表阻抗测量异常: {ex.Message}，切换到仿真");
                return await _simulation.MeasureImpedanceToGroundAsync(AddLog, token);
            }
        }

        private async Task<double> ReadJ14VoltageAsync(CancellationToken token)
        {
            if (_dmmApi == null)
            {
                _useSimulatedDmm = true;
                AddLog("电压来源: 仿真(未注入DMM API)");
                return await _simulation.MeasureJ14VoltageAsync(AddLog, token);
            }

            try
            {
                await EnsureDmmConnectedAsync(token);
                var reading = await _dmmApi.ReadOnceAsync(DmmMeasureMode.DCV, new DmmReadOptions { TimeoutMilliseconds = 8000 }, token);
                if (reading?.Value == null)
                    throw new InvalidOperationException("DMM未返回电压值");

                _useSimulatedDmm = false;
                AddLog("电压来源: 万用表");
                return reading.Value.Value;
            }
            catch (Exception ex)
            {
                _useSimulatedDmm = true;
                AddLog($"万用表电压测量异常: {ex.Message}，切换到仿真");
                return await _simulation.MeasureJ14VoltageAsync(AddLog, token);
            }
        }

        private async Task EnsureDmmConnectedAsync(CancellationToken token)
        {
            if (_dmmApi == null)
                return;

            if (_dmmApi.IsConnected)
                return;

            var ip = GetDmmIpAddress();
            AddLog($"正在连接万用表: {ip}...");
            await _dmmApi.ConnectAsync(ip, token);
            AddLog($"万用表连接成功: {_dmmApi.IpAddress}");
        }

        private string GetDmmIpAddress()
        {
            return "192.168.1.100";
        }

        private void UpdateOverallIfReady()
        {
            if (!IsManualTestRunning)
                return;

            if (string.Equals(StepAResult, "--", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(StepBResult, "--", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(StepCResult, "--", StringComparison.OrdinalIgnoreCase))
                return;

            bool overallPass =
                string.Equals(StepAResult, "PASS", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(StepBResult, "PASS", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(StepCResult, "PASS", StringComparison.OrdinalIgnoreCase);

            Application.Current?.Dispatcher?.Invoke(() =>
            {
                OverallResult = overallPass ? "PASS" : "FAIL";
                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            });
        }

        public void Dispose()
        {
            _opCts?.Cancel();
            _opCts?.Dispose();

            _simulation?.Dispose();

            if (_projectSavingToken != null)
            {
                _eventAggregator?.GetEvent<ProjectSavingEvent>()?.Unsubscribe(_projectSavingToken);
            }
        }
    }
}
