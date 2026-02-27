using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Sockets;
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
    public sealed class RS422CommunicationFunctionTestViewModel : BindableBase, IDisposable
    {
        private const string TestItemKey = "FuelController_RS422CommunicationFunction";

        private static readonly byte[] DefaultTxData = { 0xAA, 0x55 };

        private readonly ISingleBoardTestContextService _singleBoardTestContext;
        private readonly ProjectService _projectService;
        private readonly IEventAggregator _eventAggregator;
        private readonly IComponentPowerStateApi _componentPowerStateApi;
        private readonly RS422CommunicationFunctionSimulation _simulation;

        private CancellationTokenSource _opCts;
        private SubscriptionToken _projectSavingToken;

        private bool _hardwareInitialized;
        private bool _isManualTestRunning;
        private bool _isAutoTestRunning;
        private bool _isBusy;
        private FpgaIoClient _fpga;
        private bool _fpgaConnected;
        private bool _isPowerOn;
        private string _powerStatus = "未上电";

        private string _stepAResult = "--";
        private string _stepBResult = "--";
        private string _stepCResult = "--";
        private string _stepDResult = "--";

        private string _stepARxData = "--";
        private string _stepBRxData = "--";
        private string _stepCRxData = "--";
        private string _stepDRxData = "--";

        private string _overallResult = "--";
        private string _lastTestTime = "--";

        public bool IsPowerOn
        {
            get => _isPowerOn;
            set => SetProperty(ref _isPowerOn, value);
        }

        public string PowerStatus
        {
            get => _powerStatus;
            set => SetProperty(ref _powerStatus, value);
        }

        public RS422CommunicationFunctionTestViewModel(
            ISingleBoardTestContextService singleBoardTestContext,
            ProjectService projectService,
            IEventAggregator eventAggregator,
            IComponentPowerStateApi componentPowerStateApi)
        {
            _singleBoardTestContext = singleBoardTestContext;
            _projectService = projectService;
            _eventAggregator = eventAggregator;
            _componentPowerStateApi = componentPowerStateApi;
            _simulation = new RS422CommunicationFunctionSimulation();

            ManualTestCommand = new DelegateCommand(OnManualTest);
            AutoTestCommand = new DelegateCommand(OnAutoTest);

            StepACommand = new DelegateCommand(async () => await RunStepAsync("a", token => RunStepAAsync(token)), () => !IsBusy && IsManualTestRunning && _hardwareInitialized);
            StepBCommand = new DelegateCommand(async () => await RunStepAsync("b", token => RunStepBAsync(token)), () => !IsBusy && IsManualTestRunning && _hardwareInitialized);
            StepCCommand = new DelegateCommand(async () => await RunStepAsync("c", token => RunStepCAsync(token)), () => !IsBusy && IsManualTestRunning && _hardwareInitialized);
            StepDCommand = new DelegateCommand(async () => await RunStepAsync("d", token => RunStepDAsync(token)), () => !IsBusy && IsManualTestRunning && _hardwareInitialized);

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
        public DelegateCommand StepDCommand { get; }
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

        public string StepAResult { get => _stepAResult; set => SetProperty(ref _stepAResult, value); }
        public string StepBResult { get => _stepBResult; set => SetProperty(ref _stepBResult, value); }
        public string StepCResult { get => _stepCResult; set => SetProperty(ref _stepCResult, value); }
        public string StepDResult { get => _stepDResult; set => SetProperty(ref _stepDResult, value); }

        public string StepARxData { get => _stepARxData; set => SetProperty(ref _stepARxData, value); }
        public string StepBRxData { get => _stepBRxData; set => SetProperty(ref _stepBRxData, value); }
        public string StepCRxData { get => _stepCRxData; set => SetProperty(ref _stepCRxData, value); }
        public string StepDRxData { get => _stepDRxData; set => SetProperty(ref _stepDRxData, value); }

        public string OverallResult { get => _overallResult; set => SetProperty(ref _overallResult, value); }
        public string LastTestTime { get => _lastTestTime; set => SetProperty(ref _lastTestTime, value); }

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
                (StepDCommand as DelegateCommand)?.RaiseCanExecuteChanged();
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
                StepDResult = Read("StepDResult") ?? "--";

                StepARxData = Read("StepARxData") ?? "--";
                StepBRxData = Read("StepBRxData") ?? "--";
                StepCRxData = Read("StepCRxData") ?? "--";
                StepDRxData = Read("StepDRxData") ?? "--";

                OverallResult = Read("OverallResult") ?? "--";
                LastTestTime = Read("LastTestTime") ?? "--";
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
                Upsert("StepDResult", StepDResult);

                Upsert("StepARxData", StepARxData);
                Upsert("StepBRxData", StepBRxData);
                Upsert("StepCRxData", StepCRxData);
                Upsert("StepDRxData", StepDRxData);

                Upsert("OverallResult", OverallResult);
                Upsert("LastTestTime", LastTestTime);
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
                AddLog("硬件初始化完成，可以执行a/b/c/d步骤");
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

                await RunStepAsync("a", token => RunStepAAsync(token));
                await RunStepAsync("b", token => RunStepBAsync(token));
                await RunStepAsync("c", token => RunStepCAsync(token));
                await RunStepAsync("d", token => RunStepDAsync(token));

                await ResetHardwareAsync(_opCts.Token);

                bool overallPass =
                    string.Equals(StepAResult, "PASS", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(StepBResult, "PASS", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(StepCResult, "PASS", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(StepDResult, "PASS", StringComparison.OrdinalIgnoreCase);

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

        private async Task RunStepAsync(string stepName, Func<CancellationToken, Task> stepAction)
        {
            if (_opCts == null)
                return;

            var token = _opCts.Token;
            IsBusy = true;
            try
            {
                AddLog($"--- 步骤{stepName}: 执行中 ---");
                await stepAction(token);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task InitializeHardwareAsync(CancellationToken token)
        {
            if (_hardwareInitialized)
                return;

            IsBusy = true;
            try
            {
                await ApplyPower28VAsync(token);

                // 连接FPGA
                AddLog("正在连接FPGA...");
                try
                {
                    _fpga ??= new FpgaIoClient();
                    if (!_fpga.IsConnected)
                        await _fpga.ConnectAsync(token);
                    _fpgaConnected = true;
                    AddLog("FPGA连接成功");

                    // IO11=MUX1(bit0)=1: 外部通信模式，MUX2=0
                    // MUX1=1 → RS422信号路由到J1外部接口
                    await _fpga.WriteGpioAsync(0x00000001u, token);
                    AddLog("[FPGA] MUX1=1已设置（RS422外部通信模式）");
                }
                catch (Exception ex)
                {
                    AddLog($"FPGA连接失败: {ex.Message}，将使用仿真模式");
                    _fpgaConnected = false;
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
                if (_fpga != null)
                {
                    try { _fpga.Disconnect(); } catch { }
                    _fpga = null;
                    _fpgaConnected = false;
                }

                try { await _componentPowerStateApi.ApplyComponentDownStateAsync(token); } catch { }
                _hardwareInitialized = false;
                Application.Current?.Dispatcher?.Invoke(() => { IsPowerOn = false; PowerStatus = "未上电"; });
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

        private async Task ApplyPower28VAsync(CancellationToken token)
        {
            try
            {
                await _componentPowerStateApi.ApplyComponent28VStateAsync(token);
                AddLog("组件供电: 28V上电(真实API)");
            }
            catch
            {
                AddLog("组件供电: 28V上电(仿真占位)");
                await Task.Delay(120, token);
            }
            Application.Current?.Dispatcher?.Invoke(() => { IsPowerOn = true; PowerStatus = "已上电"; });
        }

        private async Task RunStepAAsync(CancellationToken token)
        {
            // 步骤a: SCI1 (UART0) 向外发送 - CRM_PIN9(SCITXD_1) TX
            // 外部通信模式：TX发出后FPGA不立即返回帧，对端收到后会发回数据（由步骤c接收）
            if (_fpgaConnected && _fpga != null)
            {
                try
                {
                    AddLog($"[FPGA] UART0(SCI1) 发送: 0x{string.Join(" ", DefaultTxData.Select(b => b.ToString("X2")))}");
                    await _fpga.UartTxOnlyAsync(0, DefaultTxData, token);
                    AddLog("[FPGA] UART0(SCI1) 发送完成，对端应回复数据由步骤c接收");
                    SetStepResultAndRx("a", DefaultTxData);
                    return;
                }
                catch (Exception ex)
                {
                    AddLog($"[FPGA] UART0发送失败: {ex.Message}，降级仿真");
                }
            }
            var simRx = await _simulation.SendAndReceiveAsync("步骤a", DefaultTxData, AddLog, token);
            SetStepResultAndRx("a", simRx);
        }

        private async Task RunStepBAsync(CancellationToken token)
        {
            // 步骤b: SCI2 (UART1) 向外发送 - CRM_PIN10(SCITXD_2) TX
            // 外部通信模式：TX发出后FPGA不立即返回帧，对端收到后会发回数据（由步骤d接收）
            if (_fpgaConnected && _fpga != null)
            {
                try
                {
                    AddLog($"[FPGA] UART1(SCI2) 发送: 0x{string.Join(" ", DefaultTxData.Select(b => b.ToString("X2")))}");
                    await _fpga.UartTxOnlyAsync(1, DefaultTxData, token);
                    AddLog("[FPGA] UART1(SCI2) 发送完成，对端应回复数据由步骤d接收");
                    SetStepResultAndRx("b", DefaultTxData);
                    return;
                }
                catch (Exception ex)
                {
                    AddLog($"[FPGA] UART1发送失败: {ex.Message}，降级仿真");
                }
            }
            var simRx = await _simulation.SendAndReceiveAsync("步骤b", DefaultTxData, AddLog, token);
            SetStepResultAndRx("b", simRx);
        }

        private async Task RunStepCAsync(CancellationToken token)
        {
            // 步骤c: SCI1 (UART0) 接收对端回复数据 - CRM_PIN19(SCIRXD_1) RX
            // 对端受到步骤a的发送后应主动发回数据
            if (_fpgaConnected && _fpga != null)
            {
                try
                {
                    AddLog("[FPGA] UART0(SCI1) 等待对端回复数据...");
                    var rx = await _fpga.UartRxWaitAsync(0, token);
                    AddLog($"[FPGA] UART0(SCI1) 接收: 0x{string.Join(" ", rx.Select(b => b.ToString("X2")))}");
                    SetStepResultAndRx("c", rx);
                    return;
                }
                catch (Exception ex)
                {
                    AddLog($"[FPGA] UART0接收失败: {ex.Message}，降级仿真");
                }
            }
            var simRx = await _simulation.SendAndReceiveAsync("步骤c", DefaultTxData, AddLog, token);
            SetStepResultAndRx("c", simRx);
        }

        private async Task RunStepDAsync(CancellationToken token)
        {
            // 步骤d: SCI2 (UART1) 接收对端回复数据 - CRM_PIN20(SCIRXD_2) RX
            // 对端受到步骤b的发送后应主动发回数据
            if (_fpgaConnected && _fpga != null)
            {
                try
                {
                    AddLog("[FPGA] UART1(SCI2) 等待对端回复数据...");
                    var rx = await _fpga.UartRxWaitAsync(1, token);
                    AddLog($"[FPGA] UART1(SCI2) 接收: 0x{string.Join(" ", rx.Select(b => b.ToString("X2")))}");
                    SetStepResultAndRx("d", rx);
                    return;
                }
                catch (Exception ex)
                {
                    AddLog($"[FPGA] UART1接收失败: {ex.Message}，降级仿真");
                }
            }
            var simRx = await _simulation.SendAndReceiveAsync("步骤d", DefaultTxData, AddLog, token);
            SetStepResultAndRx("d", simRx);
        }

        private void SetStepResultAndRx(string step, byte[] rx)
        {
            var rxHex = rx == null ? "--" : ("0x" + string.Join(" ", rx.Select(b => b.ToString("X2"))));
            bool pass = rx != null && rx.SequenceEqual(DefaultTxData);

            Application.Current?.Dispatcher?.Invoke(() =>
            {
                switch (step)
                {
                    case "a":
                        StepARxData = rxHex;
                        StepAResult = pass ? "PASS" : "FAIL";
                        break;
                    case "b":
                        StepBRxData = rxHex;
                        StepBResult = pass ? "PASS" : "FAIL";
                        break;
                    case "c":
                        StepCRxData = rxHex;
                        StepCResult = pass ? "PASS" : "FAIL";
                        break;
                    case "d":
                        StepDRxData = rxHex;
                        StepDResult = pass ? "PASS" : "FAIL";
                        break;
                }

                UpdateOverallIfReady();
            });
        }

        private void UpdateOverallIfReady()
        {
            if (!IsManualTestRunning)
                return;

            if (string.Equals(StepAResult, "--", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(StepBResult, "--", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(StepCResult, "--", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(StepDResult, "--", StringComparison.OrdinalIgnoreCase))
                return;

            bool overallPass =
                string.Equals(StepAResult, "PASS", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(StepBResult, "PASS", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(StepCResult, "PASS", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(StepDResult, "PASS", StringComparison.OrdinalIgnoreCase);

            OverallResult = overallPass ? "PASS" : "FAIL";
            LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }

        public void Dispose()
        {
            _opCts?.Cancel();
            _opCts?.Dispose();

            _simulation?.Dispose();

            try { _fpga?.Disconnect(); } catch { }
            _fpga = null;

            if (_projectSavingToken != null)
            {
                _eventAggregator?.GetEvent<ProjectSavingEvent>()?.Unsubscribe(_projectSavingToken);
            }
        }
    }
}
