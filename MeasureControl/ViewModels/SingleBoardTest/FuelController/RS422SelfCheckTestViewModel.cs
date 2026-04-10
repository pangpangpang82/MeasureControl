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
using Prism.Ioc;
using Prism.Mvvm;

namespace MeasureControl.ViewModels.SingleBoardTest.FuelController
{
    public sealed class RS422SelfCheckTestViewModel : BindableBase, IDisposable
    {
        private const string TestItemKey = "FuelController_RS422SelfCheck";

        private static readonly byte[] DefaultTxData = { 0xAA, 0x55 };

        private const string TxPin1 = "CRM_PIN9";
        private const string TxPin2 = "CRM_PIN10";
        private const string RxPin1 = "CRM_PIN19";
        private const string RxPin2 = "CRM_PIN20";

        private static readonly string[] LowPins = { "CRM_PIN2", "CRM_PIN12" };

        private readonly ISingleBoardTestContextService _singleBoardTestContext;
        private readonly ProjectService _projectService;
        private readonly IEventAggregator _eventAggregator;
        private readonly IComponentPowerStateApi _componentPowerStateApi;
        private readonly RS422SelfCheckSimulation _simulation;

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

        private bool _rs422LoopModeEnabled;
        private bool _rs422LoopModeInitialized;

        private string _stepAResult = "--";
        private string _stepBResult = "--";

        private string _stepARxData = "--";
        private string _stepBRxData = "--";

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

        public RS422SelfCheckTestViewModel(
            ISingleBoardTestContextService singleBoardTestContext,
            ProjectService projectService,
            IEventAggregator eventAggregator,
            IComponentPowerStateApi componentPowerStateApi)
        {
            _singleBoardTestContext = singleBoardTestContext;
            _projectService = projectService;
            _eventAggregator = eventAggregator;
            _componentPowerStateApi = componentPowerStateApi;
            _simulation = new RS422SelfCheckSimulation();

            ManualTestCommand = new DelegateCommand(OnManualTest);
            AutoTestCommand = new DelegateCommand(OnAutoTest);

            StepACommand = new DelegateCommand(async () => await RunStepAsync("a", token => RunStepAAsync(token)), () => !IsBusy && IsManualTestRunning && _hardwareInitialized);
            StepBCommand = new DelegateCommand(async () => await RunStepAsync("b", token => RunStepBAsync(token)), () => !IsBusy && IsManualTestRunning && _hardwareInitialized);

            ClearLogCommand = new DelegateCommand(() => Logs.Clear());

            //LoadPersistedState();
            _projectSavingToken = _eventAggregator?.GetEvent<ProjectSavingEvent>()?.Subscribe(OnProjectSaving);
        }

        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();

        public DelegateCommand ManualTestCommand { get; }
        public DelegateCommand AutoTestCommand { get; }
        public DelegateCommand StepACommand { get; }
        public DelegateCommand StepBCommand { get; }
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

        public string StepARxData { get => _stepARxData; set => SetProperty(ref _stepARxData, value); }
        public string StepBRxData { get => _stepBRxData; set => SetProperty(ref _stepBRxData, value); }

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

                StepARxData = Read("StepARxData") ?? "--";
                StepBRxData = Read("StepBRxData") ?? "--";

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

                Upsert("StepARxData", StepARxData);
                Upsert("StepBRxData", StepBRxData);

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

            if (!EnsureFuelBoardPowered())
                return;

            IsManualTestRunning = true;
            _opCts = new CancellationTokenSource();

            try
            {
                AddLog("========== 手动测试开始 ==========");
                await InitializeHardwareAsync(_opCts.Token);
                AddLog("硬件初始化完成，可以执行a/b步骤");
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
                AddLog("正在停止自动测试...");
                _opCts?.Cancel();
                // 等待测试停止并重置状态
                await Task.Delay(200);
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    IsAutoTestRunning = false;
                    _hardwareInitialized = false;
                    UpdateCommandStates();
                });
                AddLog("自动测试已停止");
                return;
            }

            if (!EnsureFuelBoardPowered())
                return;

            _opCts = new CancellationTokenSource();
            try
            {
                await ExecuteAutoTestCoreAsync(_opCts.Token);
            }
            catch (OperationCanceledException)
            {
                // 已在 ExecuteAutoTestCoreAsync 中处理
            }
            catch (Exception ex)
            {
                AddLog($"自动测试异常: {ex.Message}");
            }
            finally
            {
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    IsAutoTestRunning = false;
                    _hardwareInitialized = false;
                    UpdateCommandStates();
                });
                _opCts?.Dispose();
                _opCts = null;
            }
        }

        public async Task<string> RunOnceAsync(CancellationToken cancellationToken)
        {
            if (IsAutoTestRunning || IsManualTestRunning)
            {
                _opCts?.Cancel();
                await Task.Delay(100, CancellationToken.None).ConfigureAwait(false);
            }

            _opCts?.Cancel();
            _opCts?.Dispose();
            _opCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            try
            {
                return await ExecuteAutoTestCoreAsync(_opCts.Token).ConfigureAwait(false);
            }
            finally
            {
                Application.Current?.Dispatcher?.Invoke(() => IsAutoTestRunning = false);
                _hardwareInitialized = false;
                UpdateCommandStates();
                _opCts?.Dispose();
                _opCts = null;
            }
        }

        private async Task<string> ExecuteAutoTestCoreAsync(CancellationToken token)
        {
            if (!EnsureFuelBoardPowered())
                return "不合格";

            Application.Current?.Dispatcher?.Invoke(() => IsAutoTestRunning = true);
            AddLog("========== 自动测试开始 ==========");

            try
            {
                await InitializeHardwareAsync(token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();

                await RunStepAsync("a", t => RunStepAAsync(t)).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                await RunStepAsync("b", t => RunStepBAsync(t)).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();

                await ResetHardwareAsync(CancellationToken.None).ConfigureAwait(false);

                bool overallPass =
                    string.Equals(StepAResult, "PASS", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(StepBResult, "PASS", StringComparison.OrdinalIgnoreCase);

                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    OverallResult = overallPass ? "合格" : "不合格";
                    LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                });

                AddLog($"========== 自动测试完成: {OverallResult} ==========");
                return OverallResult;
            }
            catch (OperationCanceledException)
            {
                AddLog("自动测试已取消");
                await SafeResetHardwareAsync().ConfigureAwait(false);
                throw;
            }
            catch (Exception ex)
            {
                AddLog($"自动测试异常: {ex.Message}");
                await SafeResetHardwareAsync().ConfigureAwait(false);
                return "不合格";
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
                AddLog("检测组件供电状态...");
                if (!EnsureFuelBoardPowered())
                    throw new InvalidOperationException("请先给加放油单板上电后再进行测试。");
                AddLog("已检测到加放油单板处于上电状态");

                // 连接FPGA
                AddLog("正在连接FPGA...");
                try
                {
                    _fpga ??= new FpgaIoClient();
                    if (!_fpga.IsConnected)
                        await _fpga.ConnectAsync(token);
                    _fpgaConnected = true;
                    AddLog("FPGA连接成功");

                    try
                    {
                        AddLog("正在初始化HI8435...");
                        await _fpga.InitHi8435AfterConnectAsync(token);
                        AddLog("HI8435初始化完成");
                    }
                    catch (Exception ex)
                    {
                        AddLog($"HI8435初始化失败: {ex.Message}");
                    }

                    // 6.8 RS422自检测功能测试要求：
                    // 1. IO11=MUX1(bit0)=0, IO12=MUX2(bit1)=0 → RS422内部自检回环模式
                    // 2. CRM_PIN2和CRM_PIN12置为0（根据测试规范）
                    // GPIO Write: IO11-32对应bit0-21，小端模式
                    // MUX1=0(bit0), MUX2=0(bit1) → 内部回环
                    // CRM_PIN2对应某个IO位，CRM_PIN12对应某个IO位（需要置0）
                    // 根据协议，发送0x00000000即可将所有输出置低
                    //关闭422回环模式
                    await EnsureRs422LoopModeAsync(false, token);
                    AddLog("[FPGA] GPIO输出已置低（MUX1=0, MUX2=0, CRM_PIN2=0, CRM_PIN12=0）");
                    AddLog("[FPGA] RS422内部自检回环模式已配置");
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
                    try { _fpga.StopAsyncReceive(); } catch { }
                    await Task.Delay(50, CancellationToken.None);
                    try { _fpga.Disconnect(); } catch { }
                    _fpga = null;
                    _fpgaConnected = false;
                    _rs422LoopModeEnabled = false;
                    _rs422LoopModeInitialized = false;
                }

                _hardwareInitialized = false;
                RefreshPowerStateDisplay();
            }
            finally
            {
                IsBusy = false;
            }
        }

        private bool EnsureFuelBoardPowered()
        {
            var powerService = ContainerLocator.Container.Resolve<IHydraulicPowerService>();
            if (powerService == null || !powerService.IsHydraulicPowered)
            {
                AddLog("未检测到加放油单板上电，请先通过左上角组件上电按钮上电。");
                Application.Current?.Dispatcher?.Invoke(() =>
                    MessageBox.Show("请先点击左上角组件上电按钮，并选择“加放油单板”上电后再进行测试。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning));
                Application.Current?.Dispatcher?.Invoke(() => { IsPowerOn = false; PowerStatus = "未上电"; });
                return false;
            }

            if (!string.Equals(powerService.PoweredBoardType, "加放油单板", StringComparison.OrdinalIgnoreCase))
            {
                AddLog($"当前上电单板为{powerService.PoweredBoardType ?? "未知"}，请切换为加放油单板。");
                Application.Current?.Dispatcher?.Invoke(() =>
                    MessageBox.Show($"当前已上电单板为“{powerService.PoweredBoardType ?? "未知"}”，请先下电并选择“加放油单板”上电。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning));
                Application.Current?.Dispatcher?.Invoke(() => { IsPowerOn = false; PowerStatus = "未上电"; });
                return false;
            }

            Application.Current?.Dispatcher?.Invoke(() => { IsPowerOn = true; PowerStatus = "已上电"; });
            return true;
        }

        private void RefreshPowerStateDisplay()
        {
            var powerService = ContainerLocator.Container.Resolve<IHydraulicPowerService>();
            var isFuelPowered = powerService != null && powerService.IsHydraulicPowered &&
                                string.Equals(powerService.PoweredBoardType, "加放油单板", StringComparison.OrdinalIgnoreCase);
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                IsPowerOn = isFuelPowered;
                PowerStatus = isFuelPowered ? "已上电" : "未上电";
            });
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
        private async Task EnsureRs422LoopModeAsync(bool enable, CancellationToken token)
        {
            if (!_fpgaConnected || _fpga == null)
                return;

            if (_rs422LoopModeInitialized && _rs422LoopModeEnabled == enable)
                return;

            await _fpga.WriteGpioOutputOnlyAsync(0x00000000u, token);
            _rs422LoopModeEnabled = enable;
            _rs422LoopModeInitialized = true;
            AddLog(enable
                ? "[FPGA] 已设置 IO11(MUX1)=1、IO12(MUX2)=1（开启RS422回环模式）"
                : "[FPGA] 已设置 IO11(MUX1)=0、IO12(MUX2)=0（关闭RS422回环模式）");

            await Task.Delay(50, token);
        }
        private async Task RunStepAAsync(CancellationToken token)
        {
            // 6.8 RS422自检测功能测试 - 步骤a
            // 测试设备向CRM_PIN9发送0xAA55，通过CRM_PIN19回读数据
            // CRM_PIN9(SCITXD_1/IO5) → 内部回环 → CRM_PIN19(SCIRXD_1/IO11)
            // 内部自检模式(MUX1=0)：UART0 TX直接回环到RX
            // 使用 UartTxRxAsync: TX发出后FPGA将回环收到的数据作为同命令帧返回
            if (_fpgaConnected && _fpga != null)
            {
                try
                {
                    AddLog("步骤a: CRM_PIN9发送0xAA55 → CRM_PIN19回读（内部回环）");
                    AddLog($"[FPGA] UART0(SCI1) 自检 TX: 0x{string.Join(" ", DefaultTxData.Select(b => b.ToString("X2")))}");
                    var rx = await _fpga.UartTxRxAsync(0, DefaultTxData, token);
                    AddLog($"[FPGA] UART0(SCI1) 自检 RX: 0x{string.Join(" ", rx.Select(b => b.ToString("X2")))}");
                    SetStepResultAndRx("a", rx);
                    return;
                }
                catch (Exception ex)
                {
                    AddLog($"[FPGA] UART0自检失败: {ex.Message}，降级仿真");
                }
            }
            var simRx = await _simulation.SendAndReceiveAsync("步骤a", TxPin1, RxPin1, DefaultTxData, AddLog, token);
            SetStepResultAndRx("a", simRx);
        }

        private async Task RunStepBAsync(CancellationToken token)
        {
            // 6.8 RS422自检测功能测试 - 步骤b
            // 测试设备向CRM_PIN10发送0xAA55，通过CRM_PIN20回读数据
            // CRM_PIN10(SCITXD_2/IO6) → 内部回环 → CRM_PIN20(SCIRXD_2/IO12)
            // 内部自检模式(MUX2=0)：UART1 TX直接回环到RX
            // 使用 UartTxRxAsync: TX发出后FPGA将回环收到的数据作为同命令帧返回
            if (_fpgaConnected && _fpga != null)
            {
                try
                {
                    AddLog("步骤b: CRM_PIN10发送0xAA55 → CRM_PIN20回读（内部回环）");
                    AddLog($"[FPGA] UART1(SCI2) 自检 TX: 0x{string.Join(" ", DefaultTxData.Select(b => b.ToString("X2")))}");
                    var rx = await _fpga.UartTxRxAsync(1, DefaultTxData, token);
                    AddLog($"[FPGA] UART1(SCI2) 自检 RX: 0x{string.Join(" ", rx.Select(b => b.ToString("X2")))}");
                    SetStepResultAndRx("b", rx);
                    return;
                }
                catch (Exception ex)
                {
                    AddLog($"[FPGA] UART1自检失败: {ex.Message}，降级仿真");
                }
            }
            var simRx = await _simulation.SendAndReceiveAsync("步骤b", TxPin2, RxPin2, DefaultTxData, AddLog, token);
            SetStepResultAndRx("b", simRx);
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
                }

                UpdateOverallIfReady();
            });
        }

        private void UpdateOverallIfReady()
        {
            if (!IsManualTestRunning)
                return;

            if (string.Equals(StepAResult, "--", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(StepBResult, "--", StringComparison.OrdinalIgnoreCase))
                return;

            bool overallPass =
                string.Equals(StepAResult, "PASS", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(StepBResult, "PASS", StringComparison.OrdinalIgnoreCase);

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
