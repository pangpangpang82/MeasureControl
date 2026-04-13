using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
    public sealed class RS422SelfCheckTestViewModel : BindableBase, IDisposable
    {
        private const string TestItemKey = "FuelController_RS422SelfCheck";

        private const int PowerStabilizeMs = 1000;
        private const string PowerSupply1IpAddress = "192.168.1.15";
        private const double ComponentVoltage = 28.0;
        private const double ComponentCurrentLimit = 3.0;

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
        private IPowerSupplyApi _powerSupply1;
        private bool _isPowerOn;
        private bool _powerManagedExternally;
        private bool _forceCleanupPowerOff;
        private string _powerStatus = "已下电";

        private bool _rs422LoopModeEnabled;
        private bool _rs422LoopModeInitialized;
        private bool _isManualTestInitializing;
        private bool _isAutoTestInitializing;
        private bool _isManualTestStopping;
        private bool _isAutoTestStopping;

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

            ManualTestCommand = new DelegateCommand(async () => await OnManualTestAsync());
            AutoTestCommand = new DelegateCommand(async () => await OnAutoTestAsync());

            StepACommand = new DelegateCommand(async () => await RunStepAsync("a", token => RunStepAAsync(token)), () => !IsBusy && IsManualTestRunning && _hardwareInitialized);
            StepBCommand = new DelegateCommand(async () => await RunStepAsync("b", token => RunStepBAsync(token)), () => !IsBusy && IsManualTestRunning && _hardwareInitialized);

            ClearLogCommand = new DelegateCommand(() => Logs.Clear());

            //LoadPersistedState();
            _projectSavingToken = _eventAggregator?.GetEvent<ProjectSavingEvent>()?.Subscribe(OnProjectSaving);
            try { var hps = ContainerLocator.Container.Resolve<IBoardPowerService>(); if (hps != null) hps.IsPoweredChanged += OnBoardPowerStateChanged; } catch { }
            RefreshPowerStateDisplay();
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
                AddLog("硬件初始化完成，可以执行a/b步骤");
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

            bool overallPass =
                string.Equals(StepAResult, "PASS", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(StepBResult, "PASS", StringComparison.OrdinalIgnoreCase);

            Application.Current?.Dispatcher?.Invoke(() =>
            {
                OverallResult = overallPass ? "PASS" : "FAIL";
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
            StepARxData = "--";
            StepBRxData = "--";
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
                    AddLog($"FPGA连接失败: {ex.Message}");
                    _fpgaConnected = false;
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
                    try { _fpga.StopAsyncReceive(); } catch { }
                    await Task.Delay(50, CancellationToken.None);
                    try { _fpga.Disconnect(); } catch { }
                    _fpga = null;
                    _fpgaConnected = false;
                    _rs422LoopModeEnabled = false;
                    _rs422LoopModeInitialized = false;
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
                    PowerStatus = "已下电";
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
                Application.Current?.Dispatcher?.Invoke(() => { IsPowerOn = false; PowerStatus = "已下电"; });
                return false;
            }

            if (!string.Equals(powerService.PoweredBoardType, "加放油单板", StringComparison.OrdinalIgnoreCase))
            {
                AddLog($"当前上电单板为{powerService.PoweredBoardType ?? "未知"}，请切换为加放油单板。");
                Application.Current?.Dispatcher?.Invoke(() =>
                    MessageBox.Show($"当前已上电单板为“{powerService.PoweredBoardType ?? "未知"}”，请先下电并选择“加放油单板”上电。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning));
                Application.Current?.Dispatcher?.Invoke(() => { IsPowerOn = false; PowerStatus = "已下电"; });
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
                PowerStatus = isFuelPowered ? "已上电" : "已下电";
            });
        }

        private void OnBoardPowerStateChanged(object sender, EventArgs e)
        {
            if (!IsManualTestRunning && !IsAutoTestRunning)
                RefreshPowerStateDisplay();
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
                    AddLog($"[FPGA] UART0自检失败: {ex.Message}");
                    SetStepResultAndRx("a", null);
                    return;
                }
            }
            AddLog("步骤a执行失败: FPGA未连接");
            SetStepResultAndRx("a", null);
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
                    AddLog($"[FPGA] UART1自检失败: {ex.Message}");
                    SetStepResultAndRx("b", null);
                    return;
                }
            }
            AddLog("步骤b执行失败: FPGA未连接");
            SetStepResultAndRx("b", null);
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
