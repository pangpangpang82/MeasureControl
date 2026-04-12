using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO.Ports;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using MeasureControl.Events;
using MeasureControl.Views.Dialogs;
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
    public sealed class RS422CommunicationFunctionTestViewModel : BindableBase, IDisposable
    {
        private const string TestItemKey = "FuelController_RS422CommunicationFunction";

        private const int PowerStabilizeMs = 1000;
        private const string PowerSupply1IpAddress = "192.168.1.15";
        private const double ComponentVoltage = 28.0;
        private const double ComponentCurrentLimit = 3.0;

        private static readonly byte[] DefaultTxData = { 0xAA, 0x55 };

        // FPGA透传命令帧：AA 55 03 01 AA 55（命令0x01=UART0 TX透传，数据=AA 55）
        private static readonly byte[] FpgaTxFrameUart0 = { 0xAA, 0x55, 0x03, 0x01, 0xAA, 0x55 };
        // FPGA透传命令帧：AA 55 03 02 AA 55（命令0x02=UART1 TX透传，数据=AA 55）
        private static readonly byte[] FpgaTxFrameUart1 = { 0xAA, 0x55, 0x03, 0x02, 0xAA, 0x55 };

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
        private IPowerSupplyApi _powerSupply1;
        private Rs422SerialClient _serial1; // COM14 - 第1路422串口
        private Rs422SerialClient _serial2; // COM15 - 第2路422串口
        private bool _serial1Connected;
        private bool _serial2Connected;
        private bool _isPowerOn;
        private bool _powerManagedExternally;
        private bool _forceCleanupPowerOff;
        private string _powerStatus = "未上电";

        private bool _rs422LoopModeEnabled;
        private bool _rs422LoopModeInitialized;
        private bool _isManualTestInitializing;
        private bool _isAutoTestInitializing;
        private bool _isManualTestStopping;
        private bool _isAutoTestStopping;

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

            ManualTestCommand = new DelegateCommand(async () => await OnManualTestAsync());
            AutoTestCommand = new DelegateCommand(async () => await OnAutoTestAsync());

            StepACommand = new DelegateCommand(async () => await RunStepAsync("a", token => RunStepAAsync(token)), () => !IsBusy && IsManualTestRunning && _hardwareInitialized);
            StepBCommand = new DelegateCommand(async () => await RunStepAsync("b", token => RunStepBAsync(token)), () => !IsBusy && IsManualTestRunning && _hardwareInitialized);
            StepCCommand = new DelegateCommand(async () => await RunStepAsync("c", token => RunStepCAsync(token)), () => !IsBusy && IsManualTestRunning && _hardwareInitialized);
            StepDCommand = new DelegateCommand(async () => await RunStepAsync("d", token => RunStepDAsync(token)), () => !IsBusy && IsManualTestRunning && _hardwareInitialized);

            ClearLogCommand = new DelegateCommand(() => Logs.Clear());

            //LoadPersistedState();
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
                {
                    RaisePropertyChanged(nameof(CanStartManualTest));
                    RaisePropertyChanged(nameof(CanStartAutoTest));
                    UpdateCommandStates();
                }
            }
        }

        public bool IsAutoTestRunning
        {
            get => _isAutoTestRunning;
            set
            {
                if (SetProperty(ref _isAutoTestRunning, value))
                {
                    RaisePropertyChanged(nameof(CanStartManualTest));
                    RaisePropertyChanged(nameof(CanStartAutoTest));
                    UpdateCommandStates();
                }
            }
        }

        public bool IsManualTestBusy => IsManualTestInitializing || IsManualTestStopping;
        public bool IsAutoTestBusy   => IsAutoTestInitializing   || IsAutoTestStopping;

        public bool IsManualTestInitializing
        {
            get => _isManualTestInitializing;
            private set
            {
                if (SetProperty(ref _isManualTestInitializing, value))
                {
                    RaisePropertyChanged(nameof(IsManualTestBusy));
                    RaisePropertyChanged(nameof(CanStartManualTest));
                    RaisePropertyChanged(nameof(CanStartAutoTest));
                }
            }
        }

        public bool IsAutoTestInitializing
        {
            get => _isAutoTestInitializing;
            private set
            {
                if (SetProperty(ref _isAutoTestInitializing, value))
                {
                    RaisePropertyChanged(nameof(IsAutoTestBusy));
                    RaisePropertyChanged(nameof(CanStartManualTest));
                    RaisePropertyChanged(nameof(CanStartAutoTest));
                }
            }
        }

        public bool IsManualTestStopping
        {
            get => _isManualTestStopping;
            private set
            {
                if (SetProperty(ref _isManualTestStopping, value))
                {
                    RaisePropertyChanged(nameof(IsManualTestBusy));
                    RaisePropertyChanged(nameof(CanStartManualTest));
                    RaisePropertyChanged(nameof(CanStartAutoTest));
                }
            }
        }

        public bool IsAutoTestStopping
        {
            get => _isAutoTestStopping;
            private set
            {
                if (SetProperty(ref _isAutoTestStopping, value))
                {
                    RaisePropertyChanged(nameof(IsAutoTestBusy));
                    RaisePropertyChanged(nameof(CanStartManualTest));
                    RaisePropertyChanged(nameof(CanStartAutoTest));
                }
            }
        }

        public bool CanStartManualTest => !IsManualTestBusy && !IsAutoTestBusy && !IsAutoTestRunning;
        public bool CanStartAutoTest   => !IsManualTestBusy && !IsAutoTestBusy && !IsManualTestRunning;

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

        private async Task OnManualTestAsync()
        {
            if (IsManualTestStopping) return;

            if (IsManualTestRunning || IsManualTestInitializing)
            {
                await StopManualTestAsync().ConfigureAwait(false);
                return;
            }

            if (IsAutoTestRunning || IsAutoTestInitializing)
                await StopAutoTestAsync().ConfigureAwait(false);

            // 检查加放油单板是否已上电，未上电则询问用户
            var hpsCheck = ContainerLocator.Container.Resolve<IBoardPowerService>();
            bool alreadyPowered = hpsCheck != null && hpsCheck.IsPowered &&
                string.Equals(hpsCheck.PoweredBoardType, "加放油单板", StringComparison.OrdinalIgnoreCase);
            if (!alreadyPowered)
            {
                var (confirmed, selectedVoltage) = PowerOnPromptDialog.ShowPrompt("加放油单板", showVoltage: true);
                if (!confirmed) return;
                try
                {
                    using var powerCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                    await hpsCheck.PowerOnAsync("加放油单板", selectedVoltage, powerCts.Token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    AddLog($"上电失败: {ex.Message}");
                    return;
                }
            }

            IsManualTestInitializing = true;
            ClearResults();

            _opCts?.Cancel();
            _opCts?.Dispose();
            _opCts = new CancellationTokenSource();

            AddLog("========== 手动测试开始，正在初始化硬件... ==========");
            try
            {
                await InitializeHardwareAsync(_opCts.Token).ConfigureAwait(false);

                IsManualTestInitializing = false;
                IsManualTestRunning = true;
                AddLog("硬件初始化完成，可以执行a/b/c/d步骤");
            }
            catch (OperationCanceledException)
            {
                AddLog("手动测试初始化已取消");
                _forceCleanupPowerOff = true;
                await StopManualTestAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AddLog($"手动测试初始化失败: {ex.Message}");
                _forceCleanupPowerOff = true;
                await StopManualTestAsync().ConfigureAwait(false);
            }
        }

        private async Task StopManualTestAsync()
        {
            if (IsManualTestStopping) return;

            IsManualTestStopping = true;
            IsManualTestInitializing = false;
            try { _opCts?.Cancel(); } catch { }

            AddLog("手动测试停止，正在断开硬件...");
            try
            {
                await SafeResetHardwareAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AddLog($"硬件断开异常: {ex.Message}");
            }
            finally
            {
                IsManualTestInitializing = false;
                IsManualTestRunning = false;
                IsManualTestStopping = false;
                _hardwareInitialized = false;
                RaisePropertyChanged(nameof(CanStartManualTest));
                RaisePropertyChanged(nameof(CanStartAutoTest));
                UpdateCommandStates();
                AddLog("手动测试已结束");
            }
        }

        private async Task OnAutoTestAsync()
        {
            if (IsAutoTestStopping) return;

            if (IsAutoTestRunning || IsAutoTestInitializing)
            {
                await StopAutoTestAsync().ConfigureAwait(false);
                return;
            }

            if (IsManualTestRunning || IsManualTestInitializing)
                await StopManualTestAsync().ConfigureAwait(false);

            var hpsCheck = ContainerLocator.Container.Resolve<IBoardPowerService>();
            bool alreadyPowered = hpsCheck != null && hpsCheck.IsPowered &&
                string.Equals(hpsCheck.PoweredBoardType, "加放油单板", StringComparison.OrdinalIgnoreCase);
            if (!alreadyPowered)
            {
                var (confirmed, selectedVoltage) = PowerOnPromptDialog.ShowPrompt("加放油单板", showVoltage: true);
                if (!confirmed) return;
                try
                {
                    using var powerCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                    await hpsCheck.PowerOnAsync("加放油单板", selectedVoltage, powerCts.Token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    AddLog($"上电失败: {ex.Message}");
                    return;
                }
            }

            IsAutoTestInitializing = true;
            ClearResults();

            _opCts?.Cancel();
            _opCts?.Dispose();
            _opCts = new CancellationTokenSource();

            try
            {
                await ExecuteAutoTestCoreAsync(_opCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                AddLog("自动测试已取消");
                _forceCleanupPowerOff = true;
                await StopAutoTestAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AddLog($"自动测试异常: {ex.Message}");
                _forceCleanupPowerOff = true;
                await StopAutoTestAsync().ConfigureAwait(false);
            }
            finally
            {
                IsAutoTestInitializing = false;
                _opCts?.Dispose();
                _opCts = null;
            }
        }

        private async Task StopAutoTestAsync()
        {
            if (IsAutoTestStopping) return;

            IsAutoTestStopping = true;
            IsAutoTestInitializing = false;
            try { _opCts?.Cancel(); } catch { }

            AddLog("自动测试停止，正在断开硬件...");
            try
            {
                await SafeResetHardwareAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AddLog($"硬件断开异常: {ex.Message}");
            }
            finally
            {
                IsAutoTestInitializing = false;
                IsAutoTestRunning = false;
                IsAutoTestStopping = false;
                _hardwareInitialized = false;
                RaisePropertyChanged(nameof(CanStartManualTest));
                RaisePropertyChanged(nameof(CanStartAutoTest));
                UpdateCommandStates();
                AddLog("自动测试已结束");
            }
        }

        public async Task<string> RunOnceAsync(CancellationToken cancellationToken)
        {
            if (IsAutoTestRunning) await StopAutoTestAsync().ConfigureAwait(false);
            if (IsManualTestRunning) await StopManualTestAsync().ConfigureAwait(false);

            _opCts?.Cancel();
            _opCts?.Dispose();
            _opCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            Application.Current?.Dispatcher?.Invoke(() => { IsAutoTestInitializing = true; ClearResults(); });

            try
            {
                return await ExecuteAutoTestCoreAsync(_opCts.Token).ConfigureAwait(false);
            }
            catch
            {
                _forceCleanupPowerOff = true;
                throw;
            }
            finally
            {
                await StopAutoTestAsync().ConfigureAwait(false);
                Application.Current?.Dispatcher?.Invoke(() => IsAutoTestInitializing = false);
                _opCts?.Dispose();
                _opCts = null;
            }
        }

        private async Task<string> ExecuteAutoTestCoreAsync(CancellationToken token)
        {
            AddLog("========== 自动测试开始 ==========");

            await InitializeHardwareAsync(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();

            IsAutoTestInitializing = false;
            IsAutoTestRunning = true;

            await RunStepAsync("a", t => RunStepAAsync(t)).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            await RunStepAsync("b", t => RunStepBAsync(t)).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            await RunStepAsync("c", t => RunStepCAsync(t)).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            await RunStepAsync("d", t => RunStepDAsync(t)).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();

            bool overallPass =
                string.Equals(StepAResult, "PASS", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(StepBResult, "PASS", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(StepCResult, "PASS", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(StepDResult, "PASS", StringComparison.OrdinalIgnoreCase);

            Application.Current?.Dispatcher?.Invoke(() =>
            {
                OverallResult = overallPass ? "合格" : "不合格";
                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            });

            AddLog($"========== 自动测试完成: {OverallResult} ==========");
            await StopAutoTestAsync().ConfigureAwait(false);
            return OverallResult;
        }

        private void ClearResults()
        {
            StepAResult = "--";
            StepBResult = "--";
            StepCResult = "--";
            StepDResult = "--";
            StepARxData = "--";
            StepBRxData = "--";
            StepCRxData = "--";
            StepDRxData = "--";
            OverallResult = "--";
            LastTestTime = "--";
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
                var hps = ContainerLocator.Container.Resolve<IBoardPowerService>();
                bool fuelAlreadyPowered = hps != null && hps.IsPowered &&
                    string.Equals(hps.PoweredBoardType, "加放油单板", StringComparison.OrdinalIgnoreCase);
                if (fuelAlreadyPowered)
                {
                    _powerManagedExternally = true;
                    Application.Current?.Dispatcher?.Invoke(() => { IsPowerOn = true; PowerStatus = "已上电"; });
                    AddLog("加放油单板已由外部上电，跳过电源初始化");
                }
                else
                {
                    _powerManagedExternally = false;
                    AddLog("正在上组件28V电...");
                    await ApplyPower28VAsync(token);
                    hps?.SetPoweredState(true, "加放油单板", ComponentVoltage);
                }

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

                    //开启422回环模式
                    await EnsureRs422LoopModeAsync(true, token);

                    // 启动异步接收功能
                    _fpga.StartAsyncReceive(AddLog);
                }
                catch (Exception ex)
                {
                    AddLog($"FPGA连接失败: {ex.Message}");
                    _fpgaConnected = false;
                    throw;
                }

                // 连接422串口（COM14 - 第1路，COM15 - 第2路）
                AddLog("正在连接422串口...");
                try
                {
                    _serial1 = new Rs422SerialClient(Rs422SerialClient.DefaultPortName1);
                    _serial1.Open();
                    _serial1.StartAsyncReceive(AddLog);
                    _serial1Connected = true;
                    AddLog($"422串口 {Rs422SerialClient.DefaultPortName1} 连接成功");
                }
                catch (Exception ex)
                {
                    AddLog($"422串口 {Rs422SerialClient.DefaultPortName1} 连接失败: {ex.Message}");
                    _serial1Connected = false;
                    throw;
                }

                try
                {
                    _serial2 = new Rs422SerialClient(Rs422SerialClient.DefaultPortName2);
                    _serial2.Open();
                    _serial2.StartAsyncReceive(AddLog);
                    _serial2Connected = true;
                    AddLog($"422串口 {Rs422SerialClient.DefaultPortName2} 连接成功");
                }
                catch (Exception ex)
                {
                    AddLog($"422串口 {Rs422SerialClient.DefaultPortName2} 连接失败: {ex.Message}");
                    _serial2Connected = false;
                    throw;
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
                    try
                    {
                        try { _fpga.StopAsyncReceive(); } catch { }
                        await Task.Delay(50, CancellationToken.None);

                        //关闭422回环模式
                        await EnsureRs422LoopModeAsync(false, token);
                    }
                    catch (Exception ex)
                    {
                        AddLog($"[FPGA] 关闭MUX失败: {ex.Message}");
                    }

                    try { _fpga.Disconnect(); } catch { }
                    _fpga = null;
                    _fpgaConnected = false;
                    _rs422LoopModeEnabled = false;
                    _rs422LoopModeInitialized = false;
                }

                // 关闭422串口
                if (_serial1 != null)
                {
                    try { _serial1.Close(); } catch { }
                    _serial1 = null;
                    _serial1Connected = false;
                }
                if (_serial2 != null)
                {
                    try { _serial2.Close(); } catch { }
                    _serial2 = null;
                    _serial2Connected = false;
                }

                if (!_powerManagedExternally || _forceCleanupPowerOff)
                {
                    bool hadPowerSupply = _powerSupply1 != null;
                    try
                    {
                        if (_powerSupply1 != null && _powerSupply1.IsConnected)
                        {
                            await _powerSupply1.SetOutputEnabledAsync(PowerSupplyChannel.CH1, false, token);
                            await _powerSupply1.DisconnectAsync(token);
                            AddLog($"{PowerSupply1IpAddress} CH1已关闭并断开");
                        }
                    }
                    catch (Exception ex)
                    {
                        AddLog($"关闭{PowerSupply1IpAddress}异常: {ex.Message}");
                    }
                    finally
                    {
                        _powerSupply1 = null;
                    }
                    if (hadPowerSupply)
                        try { ContainerLocator.Container.Resolve<IBoardPowerService>()?.SetPoweredState(false); } catch { }
                }
                else
                {
                    if (_powerSupply1 != null)
                    {
                        try { await _powerSupply1.DisconnectAsync(token); } catch { }
                        _powerSupply1 = null;
                    }
                }
                _powerManagedExternally = false;
                _forceCleanupPowerOff = false;

                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    IsPowerOn = false;
                    PowerStatus = "未上电";
                });

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
            var powerService = ContainerLocator.Container.Resolve<IBoardPowerService>();
            if (powerService == null || !powerService.IsPowered)
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
            var powerService = ContainerLocator.Container.Resolve<IBoardPowerService>();
            var isFuelPowered = powerService != null && powerService.IsPowered &&
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
            AddLog($"正在连接{PowerSupply1IpAddress}，开启CH1 28V供电...");
            _powerSupply1 ??= new PowerSupplySocketApi();
            if (!_powerSupply1.IsConnected)
                await _powerSupply1.ConnectAsync(PowerSupply1IpAddress, token);

            await _powerSupply1.ApplyAsync(PowerSupplyChannel.CH1, ComponentVoltage, ComponentCurrentLimit, token);
            await _powerSupply1.SetOutputEnabledAsync(PowerSupplyChannel.CH1, true, token);
            AddLog($"{PowerSupply1IpAddress} CH1 {ComponentVoltage:F0}V已开启");

            Application.Current?.Dispatcher?.Invoke(() => { IsPowerOn = true; PowerStatus = "已上电"; });
            AddLog($"等待电源稳定（{PowerStabilizeMs}ms）...");
            await Task.Delay(PowerStabilizeMs, token);
        }

        private async Task EnsureRs422LoopModeAsync(bool enable, CancellationToken token)
        {
            if (!_fpgaConnected || _fpga == null)
                return;

            if (_rs422LoopModeInitialized && _rs422LoopModeEnabled == enable)
                return;

            uint mask = enable ? 0x00000003u : 0x00000000u;                         //小端模式
            await _fpga.WriteGpioOutputOnlyAsync(mask, token);
            _rs422LoopModeEnabled = enable;
            _rs422LoopModeInitialized = true;
            AddLog(enable
                ? "[FPGA] 已设置 IO11(MUX1)=1、IO12(MUX2)=1（开启RS422回环模式）"
                : "[FPGA] 已设置 IO11(MUX1)=0、IO12(MUX2)=0（关闭RS422回环模式）");

            await Task.Delay(50, token);
        }

        private async Task RunStepAAsync(CancellationToken token)
        {
            // 步骤a: 验证产品的接收功能（通道1）
            // 流程：试验台通过TCP向FPGA发送数据 → FPGA透传给产品 → 产品通过422串口发送给试验台 → 试验台接收后做一致性比对
            // TCP发送：AA 55 03 01 AA 55（命令0x01=UART0 TX透传，数据=AA 55）
            // 422串口接收：COM14接收产品回传的数据
            if (_fpgaConnected && _fpga != null && _serial1Connected && _serial1 != null)
            {
                try
                {
                    AddLog("步骤a: 通道1收发测试（验证产品接收功能）");
                    // 清空串口接收缓存
                    _serial1.ClearReceivedData();
                    var sendTime = DateTime.UtcNow;

                    // 通过TCP向FPGA发送透传命令
                    AddLog($"[TCP→FPGA] 发送UART0透传命令: {BitConverter.ToString(FpgaTxFrameUart0).Replace("-", " ")}");
                    await _fpga.UartTxOnlyAsync(0, DefaultTxData, token);

                    // 等待422串口接收产品回传的数据
                    AddLog($"[422串口] 等待 {Rs422SerialClient.DefaultPortName1} 接收产品回传数据...");
                    var rx = await _serial1.WaitForDataAfterAsync(sendTime, DefaultTxData.Length, 3000, token);

                    if (rx != null && rx.Length >= DefaultTxData.Length)
                    {
                        AddLog($"[422串口] {Rs422SerialClient.DefaultPortName1} 接收: 0x{BitConverter.ToString(rx).Replace("-", " ")}");
                        SetStepResultAndRx("a", rx);
                        return;
                    }
                    else
                    {
                        AddLog($"[422串口] {Rs422SerialClient.DefaultPortName1} 接收超时或数据不足");
                        SetStepResultAndRx("a", rx);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    AddLog($"步骤a执行失败: {ex.Message}");
                    SetStepResultAndRx("a", null);
                    return;
                }
            }
            AddLog("步骤a执行失败: FPGA或422串口未连接");
            SetStepResultAndRx("a", null);
        }

        private async Task RunStepBAsync(CancellationToken token)
        {
            // 步骤b: 验证产品的接收功能（通道2）
            // 流程：试验台通过TCP向FPGA发送数据 → FPGA透传给产品 → 产品通过422串口发送给试验台 → 试验台接收后做一致性比对
            // TCP发送：AA 55 03 02 AA 55（命令0x02=UART1 TX透传，数据=AA 55）
            // 422串口接收：COM15接收产品回传的数据
            if (_fpgaConnected && _fpga != null && _serial2Connected && _serial2 != null)
            {
                try
                {
                    AddLog("步骤b: 通道2收发测试（验证产品接收功能）");
                    // 清空串口接收缓存
                    _serial2.ClearReceivedData();
                    var sendTime = DateTime.UtcNow;

                    // 通过TCP向FPGA发送透传命令
                    AddLog($"[TCP→FPGA] 发送UART1透传命令: {BitConverter.ToString(FpgaTxFrameUart1).Replace("-", " ")}");
                    await _fpga.UartTxOnlyAsync(1, DefaultTxData, token);

                    // 等待422串口接收产品回传的数据
                    AddLog($"[422串口] 等待 {Rs422SerialClient.DefaultPortName2} 接收产品回传数据...");
                    var rx = await _serial2.WaitForDataAfterAsync(sendTime, DefaultTxData.Length, 3000, token);

                    if (rx != null && rx.Length >= DefaultTxData.Length)
                    {
                        AddLog($"[422串口] {Rs422SerialClient.DefaultPortName2} 接收: 0x{BitConverter.ToString(rx).Replace("-", " ")}");
                        SetStepResultAndRx("b", rx);
                        return;
                    }
                    else
                    {
                        AddLog($"[422串口] {Rs422SerialClient.DefaultPortName2} 接收超时或数据不足");
                        SetStepResultAndRx("b", rx);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    AddLog($"步骤b执行失败: {ex.Message}");
                    SetStepResultAndRx("b", null);
                    return;
                }
            }
            AddLog("步骤b执行失败: FPGA或422串口未连接");
            SetStepResultAndRx("b", null);
        }

        private async Task RunStepCAsync(CancellationToken token)
        {
            // 步骤c: 验证产品的发送功能（通道1）
            // 流程：试验台通过422串口向产品发送数据 → 产品发送给FPGA → FPGA通过TCP发送给试验台 → 试验台读取FPGA回读数据做一致性比对
            // 422串口发送：COM14发送0xAA55
            // TCP接收：FPGA异步接收中获取命令0x01的响应数据
            if (_fpgaConnected && _fpga != null && _serial1Connected && _serial1 != null)
            {
                try
                {
                    AddLog("步骤c: 通道1回环测试（验证产品发送功能）");
                    // 清空FPGA异步接收缓存
                    _fpga.ClearReceivedFrames();
                    var sendTime = DateTime.UtcNow;

                    // 通过422串口向产品发送数据
                    AddLog($"[422串口] {Rs422SerialClient.DefaultPortName1} 发送: 0x{BitConverter.ToString(DefaultTxData).Replace("-", " ")}");
                    await _serial1.SendAsync(DefaultTxData, token);

                    // 等待FPGA异步接收中获取命令0x01的响应数据
                    AddLog("[TCP←FPGA] 等待FPGA异步接收UART0回传数据...");
                    byte[] rx = null;
                    var startTime = DateTime.Now;
                    const int timeoutMs = 3000;

                    while ((DateTime.Now - startTime).TotalMilliseconds < timeoutMs)
                    {
                        token.ThrowIfCancellationRequested();
                        var frames = _fpga.GetReceivedFramesByCommandAfter(0x01, sendTime);
                        if (frames != null && frames.Count > 0)
                        {
                            var latestFrame = frames[frames.Count - 1];
                            if (latestFrame.Payload != null && latestFrame.Payload.Length >= DefaultTxData.Length)
                            {
                                rx = latestFrame.Payload.Take(DefaultTxData.Length).ToArray();
                                AddLog($"[TCP←FPGA] UART0接收: 0x{BitConverter.ToString(rx).Replace("-", " ")}");
                                break;
                            }
                        }
                        await Task.Delay(50, token);
                    }

                    if (rx != null)
                    {
                        SetStepResultAndRx("c", rx);
                        return;
                    }
                    else
                    {
                        AddLog("[TCP←FPGA] FPGA异步接收超时，未收到UART0回传数据");
                        SetStepResultAndRx("c", null);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    AddLog($"步骤c执行失败: {ex.Message}");
                    SetStepResultAndRx("c", null);
                    return;
                }
            }
            AddLog("步骤c执行失败: FPGA或422串口未连接");
            SetStepResultAndRx("c", null);
        }

        private async Task RunStepDAsync(CancellationToken token)
        {
            // 步骤d: 验证产品的发送功能（通道2）
            // 流程：试验台通过422串口向产品发送数据 → 产品发送给FPGA → FPGA通过TCP发送给试验台 → 试验台读取FPGA回读数据做一致性比对
            // 422串口发送：COM15发送0xAA55
            // TCP接收：FPGA异步接收中获取命令0x02的响应数据
            if (_fpgaConnected && _fpga != null && _serial2Connected && _serial2 != null)
            {
                try
                {
                    AddLog("步骤d: 通道2回环测试（验证产品发送功能）");
                    // 清空FPGA异步接收缓存
                    _fpga.ClearReceivedFrames();
                    var sendTime = DateTime.UtcNow;

                    // 通过422串口向产品发送数据
                    AddLog($"[422串口] {Rs422SerialClient.DefaultPortName2} 发送: 0x{BitConverter.ToString(DefaultTxData).Replace("-", " ")}");
                    await _serial2.SendAsync(DefaultTxData, token);

                    // 等待FPGA异步接收中获取命令0x02的响应数据
                    AddLog("[TCP←FPGA] 等待FPGA异步接收UART1回传数据...");
                    byte[] rx = null;
                    var startTime = DateTime.Now;
                    const int timeoutMs = 3000;

                    while ((DateTime.Now - startTime).TotalMilliseconds < timeoutMs)
                    {
                        token.ThrowIfCancellationRequested();
                        var frames = _fpga.GetReceivedFramesByCommandAfter(0x02, sendTime);
                        if (frames != null && frames.Count > 0)
                        {
                            var latestFrame = frames[frames.Count - 1];
                            if (latestFrame.Payload != null && latestFrame.Payload.Length >= DefaultTxData.Length)
                            {
                                rx = latestFrame.Payload.Take(DefaultTxData.Length).ToArray();
                                AddLog($"[TCP←FPGA] UART1接收: 0x{BitConverter.ToString(rx).Replace("-", " ")}");
                                break;
                            }
                        }
                        await Task.Delay(50, token);
                    }

                    if (rx != null)
                    {
                        SetStepResultAndRx("d", rx);
                        return;
                    }
                    else
                    {
                        AddLog("[TCP←FPGA] FPGA异步接收超时，未收到UART1回传数据");
                        SetStepResultAndRx("d", null);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    AddLog($"步骤d执行失败: {ex.Message}");
                    SetStepResultAndRx("d", null);
                    return;
                }
            }
            AddLog("步骤d执行失败: FPGA或422串口未连接");
            SetStepResultAndRx("d", null);
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

            // 关闭422串口
            try { _serial1?.Close(); } catch { }
            _serial1 = null;
            try { _serial2?.Close(); } catch { }
            _serial2 = null;

            if (_projectSavingToken != null)
            {
                _eventAggregator?.GetEvent<ProjectSavingEvent>()?.Unsubscribe(_projectSavingToken);
            }
        }
    }
}
