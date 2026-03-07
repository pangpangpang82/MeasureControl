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

        private const string RelayControlChannel = "DO15";
        private const int RelayTimeoutMs = 3000;

        private const string MatrixIpAddress = "192.168.1.3";
        private const int MatrixSlotDo = 6;
        private const int MatrixSlotDmmDo = 4;
        private const int MatrixSwitchSettleDelayMs = 1000;

        private static readonly (string In, string Out)[] MatrixDoImpedancePoints = new[]
        {
            ("I1", "O12"),
            ("I1", "O13"),
            ("I1", "O14"),
            ("I1", "O15"),
            ("I1", "O16"),
            ("I1", "O17"),
            ("I1", "O18"),
            ("I1", "O19"),
        };

        private static readonly (string In, string Out) MatrixDmmImpedance = ("I4", "O2");

        // DO1-DO14通道名称（用于给J30J提供地/开信号）
        private static readonly string[] DoChannels = new[]
        {
            "DO1", "DO2", "DO3", "DO4", "DO5", "DO6", "DO7",
            "DO8", "DO9", "DO10", "DO11", "DO12", "DO13", "DO14"
        };

        // J6-J13测量点名称
        private static readonly string[] J6ToJ13Points = new[]
        {
            "J6", "J7", "J8", "J9", "J10", "J11", "J12", "J13"
        };

        private readonly ISingleBoardTestContextService _singleBoardTestContext;
        private readonly ProjectService _projectService;
        private readonly IEventAggregator _eventAggregator;
        private readonly IComponentPowerStateApi _componentPowerStateApi;
        private readonly IPxiChassisService _pxiChassisService;
        private readonly IDmmApi _dmmApi;
        private readonly DiscreteOutputSimulation _simulation;

        private IJy7131Api _jy7131Api;
        private bool _isRelayActivated;
        private bool _relaySupplyOn;

        private readonly SemaphoreSlim _matrixLock = new SemaphoreSlim(1, 1);

        private CancellationTokenSource _opCts;
        private SubscriptionToken _projectSavingToken;

        private bool _hardwareInitialized;
        private bool _isManualTestRunning;
        private bool _isAutoTestRunning;
        private bool _isBusy;
        private bool _useSimulatedDmm;
        private bool _isPowerOn;
        private string _powerStatus = "未上电";

        private double? _impedanceGrounded;
        private double? _impedanceOpen;
        private double? _j14Voltage;

        // J6-J13各点阻抗测量结果（接地测试）
        private double? _impedanceJ6, _impedanceJ7, _impedanceJ8, _impedanceJ9;
        private double? _impedanceJ10, _impedanceJ11, _impedanceJ12, _impedanceJ13;
        // J6-J13各点阻抗测量结果（开路测试）
        private double? _impedanceOpenJ6, _impedanceOpenJ7, _impedanceOpenJ8, _impedanceOpenJ9;
        private double? _impedanceOpenJ10, _impedanceOpenJ11, _impedanceOpenJ12, _impedanceOpenJ13;

        // J6-J13各点阻抗测量结果
        private readonly double?[] _j6ToJ13Impedances = new double?[8];

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
            IPxiChassisService pxiChassisService = null,
            IDmmApi dmmApi = null)
        {
            _singleBoardTestContext = singleBoardTestContext;
            _projectService = projectService;
            _eventAggregator = eventAggregator;
            _componentPowerStateApi = componentPowerStateApi;
            _pxiChassisService = pxiChassisService;
            _dmmApi = dmmApi;
            _simulation = new DiscreteOutputSimulation();

            ManualTestCommand = new DelegateCommand(OnManualTest);
            AutoTestCommand = new DelegateCommand(OnAutoTest);
            StepACommand = new DelegateCommand(async () => await RunStepAAsync(), () => !IsBusy && IsManualTestRunning && _hardwareInitialized);
            StepBCommand = new DelegateCommand(async () => await RunStepBAsync(), () => !IsBusy && IsManualTestRunning && _hardwareInitialized);
            StepCCommand = new DelegateCommand(async () => await RunStepCAsync(), () => !IsBusy && IsManualTestRunning && _hardwareInitialized);
            PowerOnCommand = new DelegateCommand(async () => await PowerOnForStepCAsync(), () => !IsBusy && IsManualTestRunning && _hardwareInitialized && !IsPowerOn);
            ClearLogCommand = new DelegateCommand(() => Logs.Clear());
            ToggleRelayCommand = new DelegateCommand(async () => await ToggleRelayAsync(), () => !IsBusy && IsManualTestRunning && _hardwareInitialized);
            SetGroundedSignalCommand = new DelegateCommand(async () => await SetGroundedSignalAsync(), () => !IsBusy && IsManualTestRunning && _hardwareInitialized);
            SetOpenSignalCommand = new DelegateCommand(async () => await SetOpenSignalAsync(), () => !IsBusy && IsManualTestRunning && _hardwareInitialized);
            // J6-J13接地测试单点测量命令
            MeasureJ6Command = new DelegateCommand(async () => await MeasureSinglePointAsync(0, true), () => !IsBusy && IsManualTestRunning && _hardwareInitialized);
            MeasureJ7Command = new DelegateCommand(async () => await MeasureSinglePointAsync(1, true), () => !IsBusy && IsManualTestRunning && _hardwareInitialized);
            MeasureJ8Command = new DelegateCommand(async () => await MeasureSinglePointAsync(2, true), () => !IsBusy && IsManualTestRunning && _hardwareInitialized);
            MeasureJ9Command = new DelegateCommand(async () => await MeasureSinglePointAsync(3, true), () => !IsBusy && IsManualTestRunning && _hardwareInitialized);
            MeasureJ10Command = new DelegateCommand(async () => await MeasureSinglePointAsync(4, true), () => !IsBusy && IsManualTestRunning && _hardwareInitialized);
            MeasureJ11Command = new DelegateCommand(async () => await MeasureSinglePointAsync(5, true), () => !IsBusy && IsManualTestRunning && _hardwareInitialized);
            MeasureJ12Command = new DelegateCommand(async () => await MeasureSinglePointAsync(6, true), () => !IsBusy && IsManualTestRunning && _hardwareInitialized);
            MeasureJ13Command = new DelegateCommand(async () => await MeasureSinglePointAsync(7, true), () => !IsBusy && IsManualTestRunning && _hardwareInitialized);
            // J6-J13开路测试单点测量命令
            MeasureOpenJ6Command = new DelegateCommand(async () => await MeasureSinglePointAsync(0, false), () => !IsBusy && IsManualTestRunning && _hardwareInitialized);
            MeasureOpenJ7Command = new DelegateCommand(async () => await MeasureSinglePointAsync(1, false), () => !IsBusy && IsManualTestRunning && _hardwareInitialized);
            MeasureOpenJ8Command = new DelegateCommand(async () => await MeasureSinglePointAsync(2, false), () => !IsBusy && IsManualTestRunning && _hardwareInitialized);
            MeasureOpenJ9Command = new DelegateCommand(async () => await MeasureSinglePointAsync(3, false), () => !IsBusy && IsManualTestRunning && _hardwareInitialized);
            MeasureOpenJ10Command = new DelegateCommand(async () => await MeasureSinglePointAsync(4, false), () => !IsBusy && IsManualTestRunning && _hardwareInitialized);
            MeasureOpenJ11Command = new DelegateCommand(async () => await MeasureSinglePointAsync(5, false), () => !IsBusy && IsManualTestRunning && _hardwareInitialized);
            MeasureOpenJ12Command = new DelegateCommand(async () => await MeasureSinglePointAsync(6, false), () => !IsBusy && IsManualTestRunning && _hardwareInitialized);
            MeasureOpenJ13Command = new DelegateCommand(async () => await MeasureSinglePointAsync(7, false), () => !IsBusy && IsManualTestRunning && _hardwareInitialized);

            LoadPersistedState();
            _projectSavingToken = _eventAggregator?.GetEvent<ProjectSavingEvent>()?.Subscribe(OnProjectSaving);
        }

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

        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();

        public DelegateCommand ManualTestCommand { get; }
        public DelegateCommand AutoTestCommand { get; }
        public DelegateCommand StepACommand { get; }
        public DelegateCommand StepBCommand { get; }
        public DelegateCommand StepCCommand { get; }
        public DelegateCommand PowerOnCommand { get; }
        public DelegateCommand ClearLogCommand { get; }
        public DelegateCommand ToggleRelayCommand { get; }
        public DelegateCommand SetGroundedSignalCommand { get; }
        public DelegateCommand SetOpenSignalCommand { get; }
        // J6-J13接地测试单点测量命令
        public DelegateCommand MeasureJ6Command { get; }
        public DelegateCommand MeasureJ7Command { get; }
        public DelegateCommand MeasureJ8Command { get; }
        public DelegateCommand MeasureJ9Command { get; }
        public DelegateCommand MeasureJ10Command { get; }
        public DelegateCommand MeasureJ11Command { get; }
        public DelegateCommand MeasureJ12Command { get; }
        public DelegateCommand MeasureJ13Command { get; }
        // J6-J13开路测试单点测量命令
        public DelegateCommand MeasureOpenJ6Command { get; }
        public DelegateCommand MeasureOpenJ7Command { get; }
        public DelegateCommand MeasureOpenJ8Command { get; }
        public DelegateCommand MeasureOpenJ9Command { get; }
        public DelegateCommand MeasureOpenJ10Command { get; }
        public DelegateCommand MeasureOpenJ11Command { get; }
        public DelegateCommand MeasureOpenJ12Command { get; }
        public DelegateCommand MeasureOpenJ13Command { get; }

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

        // J6-J13接地测试阻抗属性
        public double? ImpedanceJ6 { get => _impedanceJ6; set => SetProperty(ref _impedanceJ6, value); }
        public double? ImpedanceJ7 { get => _impedanceJ7; set => SetProperty(ref _impedanceJ7, value); }
        public double? ImpedanceJ8 { get => _impedanceJ8; set => SetProperty(ref _impedanceJ8, value); }
        public double? ImpedanceJ9 { get => _impedanceJ9; set => SetProperty(ref _impedanceJ9, value); }
        public double? ImpedanceJ10 { get => _impedanceJ10; set => SetProperty(ref _impedanceJ10, value); }
        public double? ImpedanceJ11 { get => _impedanceJ11; set => SetProperty(ref _impedanceJ11, value); }
        public double? ImpedanceJ12 { get => _impedanceJ12; set => SetProperty(ref _impedanceJ12, value); }
        public double? ImpedanceJ13 { get => _impedanceJ13; set => SetProperty(ref _impedanceJ13, value); }
        // J6-J13开路测试阻抗属性
        public double? ImpedanceOpenJ6 { get => _impedanceOpenJ6; set => SetProperty(ref _impedanceOpenJ6, value); }
        public double? ImpedanceOpenJ7 { get => _impedanceOpenJ7; set => SetProperty(ref _impedanceOpenJ7, value); }
        public double? ImpedanceOpenJ8 { get => _impedanceOpenJ8; set => SetProperty(ref _impedanceOpenJ8, value); }
        public double? ImpedanceOpenJ9 { get => _impedanceOpenJ9; set => SetProperty(ref _impedanceOpenJ9, value); }
        public double? ImpedanceOpenJ10 { get => _impedanceOpenJ10; set => SetProperty(ref _impedanceOpenJ10, value); }
        public double? ImpedanceOpenJ11 { get => _impedanceOpenJ11; set => SetProperty(ref _impedanceOpenJ11, value); }
        public double? ImpedanceOpenJ12 { get => _impedanceOpenJ12; set => SetProperty(ref _impedanceOpenJ12, value); }
        public double? ImpedanceOpenJ13 { get => _impedanceOpenJ13; set => SetProperty(ref _impedanceOpenJ13, value); }

        public bool IsRelayActivated
        {
            get => _isRelayActivated;
            set => SetProperty(ref _isRelayActivated, value);
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
                (PowerOnCommand as DelegateCommand)?.RaiseCanExecuteChanged();
                (ToggleRelayCommand as DelegateCommand)?.RaiseCanExecuteChanged();
                (SetGroundedSignalCommand as DelegateCommand)?.RaiseCanExecuteChanged();
                (SetOpenSignalCommand as DelegateCommand)?.RaiseCanExecuteChanged();
                (MeasureJ6Command as DelegateCommand)?.RaiseCanExecuteChanged();
                (MeasureJ7Command as DelegateCommand)?.RaiseCanExecuteChanged();
                (MeasureJ8Command as DelegateCommand)?.RaiseCanExecuteChanged();
                (MeasureJ9Command as DelegateCommand)?.RaiseCanExecuteChanged();
                (MeasureJ10Command as DelegateCommand)?.RaiseCanExecuteChanged();
                (MeasureJ11Command as DelegateCommand)?.RaiseCanExecuteChanged();
                (MeasureJ12Command as DelegateCommand)?.RaiseCanExecuteChanged();
                (MeasureJ13Command as DelegateCommand)?.RaiseCanExecuteChanged();
                (MeasureOpenJ6Command as DelegateCommand)?.RaiseCanExecuteChanged();
                (MeasureOpenJ7Command as DelegateCommand)?.RaiseCanExecuteChanged();
                (MeasureOpenJ8Command as DelegateCommand)?.RaiseCanExecuteChanged();
                (MeasureOpenJ9Command as DelegateCommand)?.RaiseCanExecuteChanged();
                (MeasureOpenJ10Command as DelegateCommand)?.RaiseCanExecuteChanged();
                (MeasureOpenJ11Command as DelegateCommand)?.RaiseCanExecuteChanged();
                (MeasureOpenJ12Command as DelegateCommand)?.RaiseCanExecuteChanged();
                (MeasureOpenJ13Command as DelegateCommand)?.RaiseCanExecuteChanged();
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
                // 步骤1：初始化7131板卡（用于DO15控制继电器和DO1-DO14输出）
                if (_jy7131Api == null)
                {
                    var device7131 = FindFirstJy7131Device();
                    if (device7131 != null)
                    {
                        string devSlot = Infer7131SlotNumber(device7131);
                        AddLog($"找到7131板卡: {device7131.Model ?? device7131.Name}，槽位={devSlot}");
                        if (int.TryParse(devSlot, out int slotNum))
                            _jy7131Api = new Jy7131Api(device7131, slotNum);
                        else
                            _jy7131Api = new Jy7131Api(device7131);
                    }
                    else
                    {
                        AddLog("未找到7131板卡，将使用仿真模式控制继电器");
                    }
                }

                if (_jy7131Api != null)
                {
                    try
                    {
                        AddLog("正在连接7131板卡...");
                        if (!_jy7131Api.IsConnected)
                        {
                            await _jy7131Api.ConnectAsync(token);
                            AddLog("7131板卡连接成功");
                            await _jy7131Api.SetOutputModeAsync(Jy7131OutputMode.PushPull, token);
                            await _jy7131Api.StartAsync(token);
                            AddLog("7131板卡已启动");
                        }
                    }
                    catch (Exception ex)
                    {
                        AddLog($"7131板卡初始化异常: {ex.Message}，使用仿真模式");
                        _jy7131Api = null;
                    }
                }

                // 步骤2：设置组件供电状态（下电）- CH1不供电
                AddLog("正在设置组件供电状态: 下电（CH1不供电）...");
                try
                {
                    if (_componentPowerStateApi != null)
                    {
                        await _componentPowerStateApi.ApplyComponentDownStateAsync(token);
                        AddLog("组件供电状态已设置为下电");
                    }
                    else
                    {
                        await _simulation.ApplyComponentDownStateAsync(AddLog, token);
                    }
                }
                catch (Exception ex)
                {
                    AddLog($"组件下电状态设置异常: {ex.Message}");
                    await _simulation.ApplyComponentDownStateAsync(AddLog, token);
                }

                // 步骤3：继电器供电上电（CH2 24V）
                AddLog("正在开启继电器供电（CH2 24V）...");
                try
                {
                    if (_componentPowerStateApi != null)
                    {
                        await _componentPowerStateApi.ApplyRelayPowerAsync(token);
                        _relaySupplyOn = true;
                        AddLog("继电器供电已上电: CH2 24V");
                    }
                    else
                    {
                        AddLog("继电器供电API不可用，使用仿真模式");
                        _relaySupplyOn = true;
                    }
                }
                catch (Exception ex)
                {
                    AddLog($"继电器供电上电异常: {ex.Message}");
                }

                // 步骤4：激活继电器（DO15高电平 + 485继电器第4路），隔离产品与试验台
                AddLog("正在激活继电器（DO15高电平），隔离产品...");
                try
                {
                    if (_jy7131Api != null && _jy7131Api.IsConnected)
                    {
                        await _jy7131Api.WriteDoAsync(RelayControlChannel, true, token);
                        IsRelayActivated = true;
                        await Task.Delay(200, token);
                        AddLog("DO15输出完成，继电器线圈得电");

                        // 打开485继电器第4路（index=3，从0开始计数），配合DO15完成产品隔离
                        AddLog("正在打开485继电器第4路...");
                        try
                        {
                            await _jy7131Api.SetRelayAsync(3, true, token);
                            AddLog("485继电器第4路已打开");
                        }
                        catch (Exception ex)
                        {
                            AddLog($"485继电器操作失败: {ex.Message}");
                        }

                        AddLog("继电器已激活，产品已隔离（下电状态）");
                    }
                    else
                    {
                        await _simulation.ApplyComponentDownStateAsync(AddLog, token);
                        IsRelayActivated = true;
                    }
                }
                catch (Exception ex)
                {
                    AddLog($"继电器激活异常: {ex.Message}");
                }

                // 步骤5：配置矩阵开关
                try
                {
                    await _simulation.DisconnectMatrixAsync(AddLog, token);
                    await _simulation.DisconnectMatrixJ14Async(AddLog, token);
                }
                catch { }

                _hardwareInitialized = true;
                Application.Current?.Dispatcher?.Invoke(() => { IsPowerOn = false; PowerStatus = "已下电"; });
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
                // 步骤1：复位继电器（DO15低电平 + 485继电器第4路关闭），恢复产品连接
                if (IsRelayActivated && _jy7131Api != null && _jy7131Api.IsConnected)
                {
                    try
                    {
                        AddLog("正在复位继电器（DO15低电平）...");
                        await _jy7131Api.WriteDoAsync(RelayControlChannel, false, token);
                        AddLog("DO15输出完成，继电器线圈失电");

                        // 关闭485继电器第4路（index=3，从0开始计数），配合DO15恢复产品连接
                        AddLog("正在关闭485继电器第4路...");
                        try
                        {
                            await _jy7131Api.SetRelayAsync(3, false, token);
                            AddLog("485继电器第4路已关闭");
                        }
                        catch (Exception ex)
                        {
                            AddLog($"485继电器操作失败: {ex.Message}");
                        }

                        IsRelayActivated = false;
                        AddLog("继电器已复位");
                    }
                    catch (Exception ex)
                    {
                        AddLog($"继电器复位异常: {ex.Message}");
                    }
                }

                // 步骤1.5：关闭继电器供电（CH2 24V）
                if (_relaySupplyOn)
                {
                    try
                    {
                        if (_componentPowerStateApi != null)
                        {
                            await _componentPowerStateApi.DisableRelayPowerAsync(token);
                            AddLog("继电器供电已关闭");
                        }
                    }
                    catch (Exception ex)
                    {
                        AddLog($"继电器供电关闭异常: {ex.Message}");
                    }
                    finally
                    {
                        _relaySupplyOn = false;
                    }
                }

                // 步骤2：断开矩阵开关
                await _simulation.DisconnectMatrixAsync(AddLog, token);
                await _simulation.DisconnectMatrixJ14Async(AddLog, token);

                // 步骤3：断开7131板卡
                if (_jy7131Api != null && _jy7131Api.IsConnected)
                {
                    try
                    {
                        if (_jy7131Api.IsRunning)
                            await _jy7131Api.StopAsync(token);
                        await _jy7131Api.DisconnectAsync(token);
                        AddLog("7131板卡已断开");
                    }
                    catch (Exception ex)
                    {
                        AddLog($"7131板卡断开异常: {ex.Message}");
                    }
                }

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
                // 步骤a：下电状态 + DO1-DO14输出接地信号 + 测量J6-J13阻抗
                AddLog("步骤a: 设置下电状态...");
                await ApplyPowerDownAsync(token);

                // 使用7131板卡输出DO1-DO14接地信号（高电平=接地）
                // SetDoOutputAsync内部会自动设置PushPull模式并启动板卡
                AddLog("步骤a: 设置DO1-DO14输出接地信号...");
                await SetDoOutputAsync(true, token); // true = 接地

                // 等待信号稳定
                await Task.Delay(200, token);

                // 测量J6-J13各点阻抗
                AddLog("步骤a: 测量J6-J13对地阻抗...");
                double totalOhm = 0;
                int validCount = 0;
                bool allPass = true;

                for (int i = 0; i < J6ToJ13Points.Length; i++)
                {
                    double ohm = await ReadImpedanceForPointAsync(i, token);
                    _j6ToJ13Impedances[i] = ohm;
                    Application.Current?.Dispatcher?.Invoke(() => SetImpedanceValue(i, true, ohm));
                    totalOhm += ohm;
                    validCount++;

                    bool pointPass = ohm < ImpedanceGroundedUpperLimitOhm;
                    allPass &= pointPass;
                    AddLog($"  {J6ToJ13Points[i]}: {ohm:F1}Ω {(pointPass ? "✓" : "✗")}");
                }

                double avgOhm = validCount > 0 ? totalOhm / validCount : 0;
                Application.Current?.Dispatcher?.Invoke(() => ImpedanceGrounded = avgOhm);

                Application.Current?.Dispatcher?.Invoke(() => StepAResult = allPass ? "PASS" : "FAIL");
                AddLog($"a) 平均对地阻抗={avgOhm:F1}Ω，判据: <{ImpedanceGroundedUpperLimitOhm}Ω，结果={(allPass ? "PASS" : "FAIL")}");

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
                // 步骤b：下电状态 + DO1-DO14输出开路信号 + 测量J6-J13阻抗
                AddLog("步骤b: 设置下电状态...");
                await ApplyPowerDownAsync(token);

                // 使用7131板卡输出DO1-DO14开路信号（低电平=开路）
                AddLog("步骤b: 设置DO1-DO14输出开路信号...");
                await SetDoOutputAsync(false, token); // false = 开路

                // 等待信号稳定
                await Task.Delay(200, token);

                // 测量J6-J13各点阻抗
                AddLog("步骤b: 测量J6-J13开路阻抗...");
                double totalOhm = 0;
                int validCount = 0;
                bool allPass = true;

                for (int i = 0; i < J6ToJ13Points.Length; i++)
                {
                    double ohm = await ReadImpedanceForPointAsync(i, token);
                    _j6ToJ13Impedances[i] = ohm;
                    Application.Current?.Dispatcher?.Invoke(() => SetImpedanceValue(i, false, ohm));
                    totalOhm += ohm;
                    validCount++;

                    bool pointPass = ohm > ImpedanceOpenLowerLimitOhm;
                    allPass &= pointPass;
                    AddLog($"  {J6ToJ13Points[i]}: {ohm:F0}Ω {(pointPass ? "✓" : "✗")}");
                }

                double avgOhm = validCount > 0 ? totalOhm / validCount : 0;
                Application.Current?.Dispatcher?.Invoke(() => ImpedanceOpen = avgOhm);

                Application.Current?.Dispatcher?.Invoke(() => StepBResult = allPass ? "PASS" : "FAIL");
                AddLog($"b) 平均开路阻抗={avgOhm:F0}Ω，判据: >{ImpedanceOpenLowerLimitOhm}Ω，结果={(allPass ? "PASS" : "FAIL")}");

                UpdateOverallIfReady();
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// c步骤上电操作（独立按钮）
        /// </summary>
        private async Task PowerOnForStepCAsync()
        {
            if (_opCts == null)
                return;

            var token = _opCts.Token;
            IsBusy = true;
            try
            {
                AddLog("步骤c: 正在上电（28V供电）...");

                // 切换矩阵通路：先断开a/b的阻抗通路，再接通J14电压测量通路
                await _simulation.DisconnectMatrixAsync(AddLog, token);
                await _simulation.ConnectMatrixJ14Async(AddLog, token);

                // 上电
                await ApplyPower28VAsync(token);

                AddLog("步骤c: 上电完成，可以点击'电压测试'测量J14电压");
                UpdateCommandStates();
            }
            catch (Exception ex)
            {
                AddLog($"上电异常: {ex.Message}");
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
                await _simulation.DisconnectMatrixAsync(AddLog, token);
                await _simulation.ConnectMatrixJ14Async(AddLog, token);

                await ApplyPower28VAsync(token);

                // 测量J14电压
                AddLog("步骤c: 正在测量J14电压...");
                double v = await ReadJ14VoltageAsync(token);
                Application.Current?.Dispatcher?.Invoke(() => J14Voltage = v);

                bool pass = v >= J14VoltageLowerLimitV;
                Application.Current?.Dispatcher?.Invoke(() => StepCResult = pass ? "PASS" : "FAIL");
                AddLog($"c) J14电压={v:F2}V，判据: ≥{J14VoltageLowerLimitV}V，结果={(pass ? "PASS" : "FAIL")}");

                UpdateOverallIfReady();
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task ApplyPowerDownAsync(CancellationToken token)
        {
            if (!IsPowerOn && IsRelayActivated)
            {
                if (_componentPowerStateApi != null && !_relaySupplyOn)
                {
                    try
                    {
                        await _componentPowerStateApi.ApplyRelayPowerAsync(token);
                        _relaySupplyOn = true;
                    }
                    catch (Exception ex)
                    {
                        AddLog($"继电器供电开启异常: {ex.Message}");
                    }
                }

                AddLog("已处于下电且继电器已激活，跳过重复下电/继电器动作");
                Application.Current?.Dispatcher?.Invoke(() => { IsPowerOn = false; PowerStatus = "未上电"; });
                return;
            }

            // 使用DO15控制继电器，将产品与试验台隔离（下电）
            // DO15高电平 → 继电器得电 → NC跳NO → 产品隔离
            if (_componentPowerStateApi != null)
            {
                try
                {
                    await _componentPowerStateApi.ApplyComponentDownStateAsync(token);
                    await _componentPowerStateApi.ApplyRelayPowerAsync(token);
                    _relaySupplyOn = true;
                }
                catch (Exception ex)
                {
                    AddLog($"组件下电/继电器供电控制异常: {ex.Message}，使用仿真下电");
                    await _simulation.ApplyComponentDownStateAsync(AddLog, token);
                }
            }

            if (_jy7131Api != null && _jy7131Api.IsConnected)
            {
                try
                {
                    AddLog("正在激活继电器（DO15高电平），隔离产品...");
                    await _jy7131Api.WriteDoAsync(RelayControlChannel, true, token);
                    IsRelayActivated = true;
                    await Task.Delay(200, token);
                    AddLog("继电器已激活，产品已隔离（下电）");
                }
                catch (Exception ex)
                {
                    AddLog($"DO15控制异常: {ex.Message}，使用仿真下电");
                    await _simulation.ApplyComponentDownStateAsync(AddLog, token);
                }
            }
            else
            {
                // 7131板卡不可用，使用仿真
                await _simulation.ApplyComponentDownStateAsync(AddLog, token);
            }
            Application.Current?.Dispatcher?.Invoke(() => { IsPowerOn = false; PowerStatus = "未上电"; });
        }

        private async Task ApplyPower28VAsync(CancellationToken token)
        {
            // 使用DO15控制继电器，恢复产品与试验台连接（上电）
            // DO15低电平 → 继电器失电 → 触点恢复NC → 产品连接
            if (_componentPowerStateApi != null)
            {
                try
                {
                    if (!_relaySupplyOn)
                    {
                        await _componentPowerStateApi.ApplyRelayPowerAsync(token);
                        _relaySupplyOn = true;
                    }
                    await _componentPowerStateApi.ApplyComponent28VStateAsync(token);
                }
                catch (Exception ex)
                {
                    AddLog($"组件上电控制异常: {ex.Message}，使用仿真上电");
                    await _simulation.ApplyComponent28VStateAsync(AddLog, token);
                }
            }

            if (_jy7131Api != null && _jy7131Api.IsConnected)
            {
                try
                {
                    AddLog("正在复位继电器（DO15低电平），恢复产品连接...");
                    await _jy7131Api.WriteDoAsync(RelayControlChannel, false, token);
                    IsRelayActivated = false;
                    await Task.Delay(200, token);
                    AddLog("继电器已复位，产品已连接");
                }
                catch (Exception ex)
                {
                    AddLog($"DO15控制异常: {ex.Message}，使用仿真上电");
                    await _simulation.ApplyComponent28VStateAsync(AddLog, token);
                }
            }
            else
            {
                // 7131板卡不可用，使用仿真
                await _simulation.ApplyComponent28VStateAsync(AddLog, token);
            }
            Application.Current?.Dispatcher?.Invoke(() => { IsPowerOn = true; PowerStatus = "已上电"; });
        }

        /// <summary>
        /// 设置DO1-DO14输出状态
        /// </summary>
        /// <param name="grounded">true=接地（高电平），false=开路（低电平）</param>
        private async Task SetDoOutputAsync(bool grounded, CancellationToken token)
        {
            if (_jy7131Api != null && _jy7131Api.IsConnected)
            {
                try
                {
                    // 确保7131板卡已连接并启动（参考PowerImpedanceTestViewModel的做法）
                    await _jy7131Api.EnsureConnectedAndRunningAsync(token);
                    if (!_jy7131Api.IsRunning)
                    {
                        AddLog("警告: 7131板卡启动失败");
                    }

                    // 设置DO1-DO14输出
                    AddLog($"正在写DO1-DO14（{(grounded ? "高电平" : "低电平")}）...");
                    foreach (var channel in DoChannels)
                    {
                        await _jy7131Api.WriteDoAsync(channel, grounded, token);
                    }
                    AddLog($"DO1-DO14已设置为{(grounded ? "接地（高电平）" : "开路（低电平）")}");

                    // 回读验证DO输出状态
                    try
                    {
                        var mask = await _jy7131Api.ReadDoBitmaskAsync(token);
                        // DO1-DO14对应bit1-bit14（DO1=bit1, DO2=bit2, ..., DO14=bit14）
                        uint expectedMask = grounded ? 0x7FFEu : 0x0000u; // bit1-bit14全高或全低
                        uint actualDo1To14 = mask & 0x7FFEu;
                        bool verified = (grounded && actualDo1To14 == expectedMask) || (!grounded && actualDo1To14 == 0);
                        AddLog($"DO回读验证: mask=0x{mask:X8}, DO1-14=0x{actualDo1To14:X4}, 期望=0x{expectedMask:X4}, {(verified ? "✓" : "✗")}");
                        
                        if (!verified)
                        {
                            AddLog($"警告: DO输出状态与期望不符，请检查7131板卡连接");
                        }
                    }
                    catch (Exception ex)
                    {
                        AddLog($"DO回读验证失败: {ex.Message}");
                    }
                }
                catch (Exception ex)
                {
                    AddLog($"DO1-DO14输出异常: {ex.Message}，使用仿真");
                    if (grounded)
                        await _simulation.SetDoGroundedAsync(AddLog, token);
                    else
                        await _simulation.SetDoOpenAsync(AddLog, token);
                }
            }
            else
            {
                // 7131板卡不可用，使用仿真
                AddLog("7131板卡不可用，使用仿真模式");
                if (grounded)
                    await _simulation.SetDoGroundedAsync(AddLog, token);
                else
                    await _simulation.SetDoOpenAsync(AddLog, token);
            }
        }

        /// <summary>
        /// 读取指定测量点的阻抗（J6-J13）
        /// 需要先配置对应的矩阵开关通路
        /// </summary>
        /// <param name="pointIndex">测量点索引（0=J6, 1=J7, ..., 7=J13）</param>
        private async Task<double> ReadImpedanceForPointAsync(int pointIndex, CancellationToken token)
        {
            if (pointIndex < 0 || pointIndex >= MatrixDoImpedancePoints.Length)
                throw new ArgumentOutOfRangeException(nameof(pointIndex));

            if (_dmmApi == null || _useSimulatedDmm)
                return await ReadImpedanceAsync(token);

            try { await _simulation.DisconnectMatrixJ14Async(AddLog, token); } catch { }

            await _matrixLock.WaitAsync(token);
            try
            {
                var matrix = MatrixControlService.Instance;

                try { await matrix.DisconnectNodesAsync(MatrixDmmImpedance.In, MatrixDmmImpedance.Out, MatrixSlotDmmDo, MatrixIpAddress); } catch { }
                foreach (var ch in MatrixDoImpedancePoints)
                {
                    try { await matrix.DisconnectNodesAsync(ch.In, ch.Out, MatrixSlotDo, MatrixIpAddress); } catch { }
                }

                var okDmm = await matrix.ConnectNodesAsync(MatrixDmmImpedance.In, MatrixDmmImpedance.Out, MatrixSlotDmmDo, MatrixIpAddress);
                var p = MatrixDoImpedancePoints[pointIndex];
                await Task.Delay(1000);
                var okP = await matrix.ConnectNodesAsync(p.In, p.Out, MatrixSlotDo, MatrixIpAddress);
                await Task.Delay(MatrixSwitchSettleDelayMs, token);

                if (!okDmm || !okP)
                {
                    AddLog("矩阵通路连接失败，使用仿真测量");
                    _useSimulatedDmm = true;
                    return await _simulation.MeasureImpedanceToGroundAsync(AddLog, token);
                }

                return await ReadImpedanceAsync(token);
            }
            finally
            {
                try
                {
                    var matrix = MatrixControlService.Instance;
                    var p = MatrixDoImpedancePoints[pointIndex];
                    await Task.Delay(500);
                    try { await matrix.DisconnectNodesAsync(p.In, p.Out, MatrixSlotDo, MatrixIpAddress); } catch { }
                    try { await matrix.DisconnectNodesAsync(MatrixDmmImpedance.In, MatrixDmmImpedance.Out, MatrixSlotDmmDo, MatrixIpAddress); } catch { }
                }
                catch { }
                _matrixLock.Release();
            }
        }

        private async Task<double> ReadImpedanceAsync(CancellationToken token)
        {
            if (_useSimulatedDmm)
                return await _simulation.MeasureImpedanceToGroundAsync(AddLog, token);

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
                
                // 检查超量程情况（开路时阻抗无穷大）
                if (reading?.IsOverrange == true)
                {
                    AddLog("阻抗测量: 超量程（开路）");
                    return double.MaxValue; // 返回最大值表示开路
                }
                
                if (reading?.Value == null)
                {
                    // DMM返回空值，可能是通信问题，尝试重新连接
                    AddLog($"DMM未返回阻抗值（Raw: {reading?.Raw ?? "null"}），尝试重新连接...");
                    try
                    {
                        await _dmmApi.DisconnectAsync(token);
                        await Task.Delay(500, token);
                        await _dmmApi.ConnectAsync(GetDmmIpAddress(), token);
                        AddLog("DMM重新连接成功");
                        
                        // 重试一次测量
                        reading = await _dmmApi.ReadOnceAsync(DmmMeasureMode.RES, new DmmReadOptions { TimeoutMilliseconds = 8000 }, token);
                        if (reading?.IsOverrange == true)
                            return double.MaxValue;
                        if (reading?.Value != null)
                            return reading.Value.Value;
                    }
                    catch (Exception retryEx)
                    {
                        AddLog($"DMM重连失败: {retryEx.Message}");
                    }
                    
                    throw new InvalidOperationException($"DMM未返回阻抗值（Raw: {reading?.Raw ?? "null"}）");
                }

                return reading.Value.Value;
            }
            catch (Exception ex)
            {
                // 只在连续多次失败时才切换到仿真模式
                AddLog($"万用表阻抗测量异常: {ex.Message}");
                _useSimulatedDmm = true;
                AddLog("切换到仿真模式");
                return await _simulation.MeasureImpedanceToGroundAsync(AddLog, token);
            }
        }

        private async Task<double> ReadJ14VoltageAsync(CancellationToken token)
        {
            if (_useSimulatedDmm)
                return await _simulation.MeasureJ14VoltageAsync(AddLog, token);

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
            return "192.168.1.13";
        }

        private async Task ToggleRelayAsync()
        {
            if (_opCts == null) return;
            IsBusy = true;
            try
            {
                if (_isRelayActivated)
                {
                    if (_jy7131Api?.IsConnected == true)
                    {
                        if (!_jy7131Api.IsRunning)
                        {
                            await _jy7131Api.SetOutputModeAsync(Jy7131OutputMode.PushPull, _opCts.Token);
                            await _jy7131Api.StartAsync(_opCts.Token);
                        }

                        await _jy7131Api.WriteDoAsync(RelayControlChannel, false, _opCts.Token);
                        try
                        {
                            var mask = await _jy7131Api.ReadDoBitmaskAsync(_opCts.Token);
                            var ok = int.TryParse(RelayControlChannel.Substring(2), out var doIdx);
                            var bit = ok ? (doIdx == 0 ? 0 : doIdx - 1) : 14;
                            AddLog($"DO写回读取: mask=0x{mask:X8}，{RelayControlChannel}={(mask & (1u << bit)) != 0}");
                        }
                        catch (Exception ex)
                        {
                            AddLog($"DO写回读取失败: {ex.Message}");
                        }

                        // 关闭485继电器第4路
                        try
                        {
                            await _jy7131Api.SetRelayAsync(3, false, _opCts.Token);
                            AddLog("485继电器第4路已关闭");
                        }
                        catch (Exception ex)
                        {
                            AddLog($"485继电器操作失败: {ex.Message}");
                        }
                    }
                    _isRelayActivated = false;
                    IsRelayActivated = false;
                    AddLog("继电器已复位");
                }
                else
                {
                    if (_jy7131Api?.IsConnected == true)
                    {
                        if (!_jy7131Api.IsRunning)
                        {
                            await _jy7131Api.SetOutputModeAsync(Jy7131OutputMode.PushPull, _opCts.Token);
                            await _jy7131Api.StartAsync(_opCts.Token);
                        }

                        await _jy7131Api.WriteDoAsync(RelayControlChannel, true, _opCts.Token);
                        try
                        {
                            var mask = await _jy7131Api.ReadDoBitmaskAsync(_opCts.Token);
                            var ok = int.TryParse(RelayControlChannel.Substring(2), out var doIdx);
                            var bit = ok ? (doIdx == 0 ? 0 : doIdx - 1) : 14;
                            AddLog($"DO写回读取: mask=0x{mask:X8}，{RelayControlChannel}={(mask & (1u << bit)) != 0}");
                        }
                        catch (Exception ex)
                        {
                            AddLog($"DO写回读取失败: {ex.Message}");
                        }

                        // 打开485继电器第4路
                        try
                        {
                            await _jy7131Api.SetRelayAsync(3, true, _opCts.Token);
                            AddLog("485继电器第4路已打开");
                        }
                        catch (Exception ex)
                        {
                            AddLog($"485继电器操作失败: {ex.Message}");
                        }
                    }
                    _isRelayActivated = true;
                    IsRelayActivated = true;
                    AddLog("继电器已激活");
                }
            }
            catch (Exception ex) { AddLog($"继电器异常: {ex.Message}"); }
            finally { IsBusy = false; }
        }

        private async Task SetGroundedSignalAsync()
        {
            if (_opCts == null) return;
            IsBusy = true;
            try { await SetDoOutputAsync(true, _opCts.Token); }
            catch (Exception ex) { AddLog($"接地信号异常: {ex.Message}"); }
            finally { IsBusy = false; }
        }

        private async Task SetOpenSignalAsync()
        {
            if (_opCts == null) return;
            IsBusy = true;
            try { await SetDoOutputAsync(false, _opCts.Token); }
            catch (Exception ex) { AddLog($"开路信号异常: {ex.Message}"); }
            finally { IsBusy = false; }
        }

        private async Task MeasureSinglePointAsync(int idx, bool grounded)
        {
            if (_opCts == null) return;
            IsBusy = true;
            try
            {
                double v = await ReadImpedanceForPointAsync(idx, _opCts.Token);
                Application.Current?.Dispatcher?.Invoke(() => SetImpedanceValue(idx, grounded, v));
                AddLog($"{J6ToJ13Points[idx]}阻抗={v:F1}Ω");
            }
            catch (Exception ex) { AddLog($"测量异常: {ex.Message}"); }
            finally { IsBusy = false; }
        }

        private void SetImpedanceValue(int idx, bool grounded, double v)
        {
            if (grounded)
            {
                if (idx == 0) ImpedanceJ6 = v; else if (idx == 1) ImpedanceJ7 = v;
                else if (idx == 2) ImpedanceJ8 = v; else if (idx == 3) ImpedanceJ9 = v;
                else if (idx == 4) ImpedanceJ10 = v; else if (idx == 5) ImpedanceJ11 = v;
                else if (idx == 6) ImpedanceJ12 = v; else if (idx == 7) ImpedanceJ13 = v;
            }
            else
            {
                if (idx == 0) ImpedanceOpenJ6 = v; else if (idx == 1) ImpedanceOpenJ7 = v;
                else if (idx == 2) ImpedanceOpenJ8 = v; else if (idx == 3) ImpedanceOpenJ9 = v;
                else if (idx == 4) ImpedanceOpenJ10 = v; else if (idx == 5) ImpedanceOpenJ11 = v;
                else if (idx == 6) ImpedanceOpenJ12 = v; else if (idx == 7) ImpedanceOpenJ13 = v;
            }
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

            try
            {
                if (_isRelayActivated && _jy7131Api != null)
                {
                    _jy7131Api.WriteDoAsync(RelayControlChannel, false).GetAwaiter().GetResult();
                }
            }
            catch { }

            try
            {
                _jy7131Api?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            catch { }
            finally
            {
                _jy7131Api = null;
            }

            _simulation?.Dispose();

            if (_projectSavingToken != null)
            {
                _eventAggregator?.GetEvent<ProjectSavingEvent>()?.Unsubscribe(_projectSavingToken);
            }
        }

        #region 7131板卡查找辅助方法

        /// <summary>
        /// 从 PXI 机箱中查找第一个 PXIe-7131 板卡
        /// 使用 GetAllChassis 方法遍历所有机箱
        /// </summary>
        private MeasureControl.Models.Devices.DeviceBase FindFirstJy7131Device()
        {
            var chassisList = _pxiChassisService?.GetAllChassis();
            if (chassisList == null)
            {
                AddLog("[7131查找] 机箱列表为null");
                return null;
            }

            foreach (var chassis in chassisList)
            {
                if (chassis?.Devices == null)
                    continue;

                // 直接在机箱设备列表中查找
                var device = chassis.Devices.FirstOrDefault(d =>
                    d is MeasureControl.Models.Devices.DigitalIODevice ||
                    (d?.Model?.IndexOf("7131", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.DeviceTypeName?.IndexOf("离散量", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.DeviceTypeName?.IndexOf("数字量", StringComparison.OrdinalIgnoreCase) >= 0));

                if (device != null)
                {
                    AddLog($"[7131查找] 找到板卡: Name={device.Name}, Model={device.Model}");
                    return device;
                }

                // 遍历子设备
                foreach (var d in chassis.Devices)
                {
                    if (d?.Children == null)
                        continue;

                    var childDevice = d.Children.FirstOrDefault(c =>
                        c is MeasureControl.Models.Devices.DigitalIODevice ||
                        (c?.Model?.IndexOf("7131", StringComparison.OrdinalIgnoreCase) >= 0) ||
                        (c?.DeviceTypeName?.IndexOf("离散量", StringComparison.OrdinalIgnoreCase) >= 0) ||
                        (c?.DeviceTypeName?.IndexOf("数字量", StringComparison.OrdinalIgnoreCase) >= 0));

                    if (childDevice != null)
                    {
                        AddLog($"[7131查找] 找到板卡: Name={childDevice.Name}, Model={childDevice.Model}");
                        return childDevice;
                    }
                }
            }

            AddLog("[7131查找] 未找到7131板卡");
            return null;
        }

        private static string Infer7131SlotNumber(MeasureControl.Models.Devices.DeviceBase device)
        {
            if (device is MeasureControl.Models.Devices.DeviceCategories.PxiDeviceBase pxi && pxi.SlotIndex > 0)
                return pxi.SlotIndex.ToString();

            var slot = device?.SlotPosition;
            if (!string.IsNullOrWhiteSpace(slot))
            {
                var trimmed = slot.Replace("Slot", "").Replace("slot", "").Trim();
                if (int.TryParse(trimmed, out var slotNum) && slotNum > 0)
                    return slotNum.ToString();
            }
            return "12";
        }

        #endregion
    }
}
