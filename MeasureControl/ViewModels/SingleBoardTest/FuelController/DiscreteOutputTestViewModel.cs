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
using Prism.Ioc;
using Prism.Mvvm;

namespace MeasureControl.ViewModels.SingleBoardTest.FuelController
{
    public sealed class DiscreteOutputTestViewModel : BindableBase, IDisposable
    {
        private const string TestItemKey = "FuelController_DiscreteOutput";

        private const double ImpedanceGroundedUpperLimitOhm = 10.0;
        private const double ImpedanceOpenLowerLimitOhm = 100000.0;
        private const double J14VoltageLowerLimitV = 16.0;

        private static readonly double[] GroundLoopImpedanceOffsetsOhm =
        {
            6.702, 6.706, 6.781, 6.829, 6.418, 6.611, 6.659, 6.708
        };

        // 物理DO15映射到API的DO14
        private const string RelayControlChannel = "DO14";
        private const int RelayTimeoutMs = 3000;

        private const string DmmIpAddress = "192.168.1.13";

        private const string PowerSupply1IpAddress = "192.168.1.15";
        private const PowerSupplyChannel ComponentSupplyChannel = PowerSupplyChannel.CH1;
        private const PowerSupplyChannel RelaySupplyChannel = PowerSupplyChannel.CH2;
        private const double ComponentCurrentLimitA = 3.0;
        private const double RelayVoltageV = 24.0;
        private const double RelayCurrentLimitA = 1.0;

        private const int DefaultHardwareTimeoutMs = 8000;

        private const string MatrixIpAddress = "192.168.1.3";
        private const int MatrixSlotDo = 6;
        private const int MatrixSlotDmmDo = 4;
        private const int MatrixSwitchSettleDelayMs = 200;
        private static readonly (string In, string Out) MatrixJ14VoltagePoint = ("I1", "O20");

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

        // DO0-DO13通道名称（物理DO1-DO14映射到API的DO0-DO13）
        private static readonly string[] DoChannels = new[]
        {
            "DO0", "DO1", "DO2", "DO3", "DO4", "DO5", "DO6",
            "DO7", "DO8", "DO9", "DO10", "DO11", "DO12", "DO13"
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
        private bool _componentSupplyOn;

        private readonly SemaphoreSlim _dmmLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _jy7131Lock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _powerLock = new SemaphoreSlim(1, 1);

        private readonly SemaphoreSlim _matrixLock = new SemaphoreSlim(1, 1);

        private IDmmApi _dmmSocket;
        private IPowerSupplyApi _powerSupply1;

        private CancellationTokenSource _opCts;
        private SubscriptionToken _projectSavingToken;

        private bool _hardwareInitialized;
        private bool _isManualTestRunning;
        private bool _isAutoTestRunning;
        private bool _isBusy;
        private bool _useSimulatedDmm;
        private bool _isPowerOn;
        private string _powerStatus = "未上电";

        private double _selectedSupplyVoltage = 28.0;

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

        private bool testReset = true;

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
            _dmmSocket = dmmApi ?? new DmmSocketApi();
            _simulation = new DiscreteOutputSimulation();

            ManualTestCommand = new DelegateCommand(OnManualTest, () => !IsAutoTestRunning);
            AutoTestCommand = new DelegateCommand(OnAutoTest, () => !IsManualTestRunning);
            StepACommand = new DelegateCommand(async () => await RunStepAAsync(), () => !IsBusy && IsManualTestRunning && _hardwareInitialized);
            StepBCommand = new DelegateCommand(async () => await RunStepBAsync(), () => !IsBusy && IsManualTestRunning && _hardwareInitialized);
            StepCCommand = new DelegateCommand(async () => await RunStepCAsync(), () => !IsBusy && IsManualTestRunning && _hardwareInitialized);
            ClearLogCommand = new DelegateCommand(() => Logs.Clear());
            //ToggleRelayCommand = new DelegateCommand(async () => await ToggleRelayAsync(), () => !IsBusy && IsManualTestRunning && _hardwareInitialized);
            ToggleRelayCommand = new DelegateCommand(async () => await ToggleRelayAsync(), () => testReset == true);
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

            //LoadPersistedState();
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

        public IReadOnlyList<double> SupplyVoltageOptions { get; } = new List<double> { 18.0, 28.0, 32.0 };

        public double SelectedSupplyVoltage
        {
            get => _selectedSupplyVoltage;
            set => SetProperty(ref _selectedSupplyVoltage, value);
        }

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
                ManualTestCommand.RaiseCanExecuteChanged();
                AutoTestCommand.RaiseCanExecuteChanged();
                (StepACommand as DelegateCommand)?.RaiseCanExecuteChanged();
                (StepBCommand as DelegateCommand)?.RaiseCanExecuteChanged();
                (StepCCommand as DelegateCommand)?.RaiseCanExecuteChanged();
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

        private static async Task WithTimeoutAsync(Task task, int timeoutMs, string operationName, CancellationToken token)
        {
            if (task == null)
                throw new ArgumentNullException(nameof(task));

            var completed = await Task.WhenAny(task, Task.Delay(timeoutMs, CancellationToken.None)).ConfigureAwait(false);
            if (completed != task)
            {
                token.ThrowIfCancellationRequested();
                throw new TimeoutException($"{operationName}超时（{timeoutMs}ms）");
            }

            await task.ConfigureAwait(false);
        }

        private static async Task<T> WithTimeoutAsync<T>(Task<T> task, int timeoutMs, string operationName, CancellationToken token)
        {
            if (task == null)
                throw new ArgumentNullException(nameof(task));

            var completed = await Task.WhenAny(task, Task.Delay(timeoutMs, CancellationToken.None)).ConfigureAwait(false);
            if (completed != task)
            {
                token.ThrowIfCancellationRequested();
                throw new TimeoutException($"{operationName}超时（{timeoutMs}ms）");
            }

            return await task.ConfigureAwait(false);
        }

        private async Task EnsureJy7131ConnectedAndRunningAsync(CancellationToken token)
        {
            await _jy7131Lock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (_jy7131Api == null)
                {
                    var device7131 = FindFirstJy7131Device();
                    if (device7131 == null)
                        throw new InvalidOperationException("未找到7131板卡，无法执行真实继电器控制");

                    string devSlot = Infer7131SlotNumber(device7131);
                    AddLog($"找到7131板卡: {device7131.Model ?? device7131.Name}，槽位={devSlot}");
                    if (int.TryParse(devSlot, out int slotNum))
                        _jy7131Api = new Jy7131Api(device7131, slotNum);
                    else
                        _jy7131Api = new Jy7131Api(device7131);
                }

                if (!_jy7131Api.IsConnected)
                {
                    AddLog("正在连接7131板卡...");
                    await WithTimeoutAsync(_jy7131Api.ConnectAsync(token), DefaultHardwareTimeoutMs, "7131连接", token).ConfigureAwait(false);
                    AddLog("7131板卡连接成功");
                }

                if (!_jy7131Api.IsRunning)
                {
                    await WithTimeoutAsync(_jy7131Api.SetOutputModeAsync(Jy7131OutputMode.Sinking, token), DefaultHardwareTimeoutMs, "7131设置输出模式", token).ConfigureAwait(false);
                    await WithTimeoutAsync(_jy7131Api.StartAsync(token), DefaultHardwareTimeoutMs, "7131启动", token).ConfigureAwait(false);
                    AddLog("7131板卡已启动");
                }
            }
            finally
            {
                _jy7131Lock.Release();
            }
        }

        private async Task CleanupJy7131Async()
        {
            await _jy7131Lock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                if (_jy7131Api == null)
                    return;

                try { if (_jy7131Api.IsRunning) await WithTimeoutAsync(_jy7131Api.StopAsync(CancellationToken.None), DefaultHardwareTimeoutMs, "7131停止", CancellationToken.None).ConfigureAwait(false); } catch { }
                try { if (_jy7131Api.IsConnected) await WithTimeoutAsync(_jy7131Api.DisconnectAsync(CancellationToken.None), DefaultHardwareTimeoutMs, "7131断开", CancellationToken.None).ConfigureAwait(false); } catch { }
                try { await _jy7131Api.DisposeAsync().ConfigureAwait(false); } catch { }
            }
            finally
            {
                _jy7131Api = null;
                _isRelayActivated = false;
                _jy7131Lock.Release();
            }
        }

        private async Task CleanupDmmAsync()
        {
            await _dmmLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                if (_dmmSocket == null)
                    return;

                try { if (_dmmSocket.IsConnected) await WithTimeoutAsync(_dmmSocket.DisconnectAsync(CancellationToken.None), DefaultHardwareTimeoutMs, "万用表断开", CancellationToken.None).ConfigureAwait(false); } catch { }
            }
            finally
            {
                _dmmLock.Release();
            }
        }

        private async Task EnsurePowerSupply1ConnectedAsync(CancellationToken token)
        {
            _powerSupply1 ??= new PowerSupplySocketApi();
            if (_powerSupply1.IsConnected)
                return;

            await _powerLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (_powerSupply1.IsConnected)
                    return;

                AddLog($"正在连接电源1: {PowerSupply1IpAddress}...");
                await WithTimeoutAsync(_powerSupply1.ConnectAsync(PowerSupply1IpAddress, token), DefaultHardwareTimeoutMs, "电源1连接", token).ConfigureAwait(false);
                AddLog("电源1连接成功");
            }
            finally
            {
                _powerLock.Release();
            }
        }

        private async Task EnableRelaySupplyAsync(CancellationToken token)
        {
            await EnsurePowerSupply1ConnectedAsync(token).ConfigureAwait(false);
            await _powerLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (_relaySupplyOn)
                    return;

                await WithTimeoutAsync(_powerSupply1.ApplyAsync(RelaySupplyChannel, RelayVoltageV, RelayCurrentLimitA, token), DefaultHardwareTimeoutMs, "继电器供电参数设置", token).ConfigureAwait(false);
                await WithTimeoutAsync(_powerSupply1.SetOutputEnabledAsync(RelaySupplyChannel, true, token), DefaultHardwareTimeoutMs, "继电器供电开启", token).ConfigureAwait(false);
                _relaySupplyOn = true;
                await Task.Delay(200, token).ConfigureAwait(false);
                AddLog("继电器供电已上电: CH2 24V");
            }
            finally
            {
                _powerLock.Release();
            }
        }

        private async Task DisableRelaySupplyAsync(CancellationToken token)
        {
            await _powerLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (!_relaySupplyOn || _powerSupply1 == null)
                    return;

                try { await WithTimeoutAsync(_powerSupply1.SetOutputEnabledAsync(RelaySupplyChannel, false, CancellationToken.None), DefaultHardwareTimeoutMs, "继电器供电关闭", CancellationToken.None).ConfigureAwait(false); } catch { }
                _relaySupplyOn = false;
            }
            finally
            {
                _powerLock.Release();
            }
        }

        private async Task ApplyComponentDownAsync(CancellationToken token)
        {
            try { await EnsurePowerSupply1ConnectedAsync(token).ConfigureAwait(false); } catch { }

            await _powerLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (_powerSupply1 == null)
                    return;

                bool globallyPowered = false;
                try
                {
                    var svc = ContainerLocator.Container.Resolve<IBoardPowerService>();
                    globallyPowered = svc?.IsPowered == true;
                }
                catch { }

                if (_componentSupplyOn || globallyPowered)
                {
                    try { await WithTimeoutAsync(_powerSupply1.SetOutputEnabledAsync(ComponentSupplyChannel, false, CancellationToken.None), DefaultHardwareTimeoutMs, "组件供电关闭", CancellationToken.None).ConfigureAwait(false); } catch { }
                }

                _componentSupplyOn = false;
            }
            finally
            {
                _powerLock.Release();
            }

            Application.Current?.Dispatcher?.Invoke(() => { IsPowerOn = false; PowerStatus = "未上电"; });
        }

        private async Task ApplyComponentVoltageAsync(double voltage, CancellationToken token)
        {
            await EnsurePowerSupply1ConnectedAsync(token).ConfigureAwait(false);
            await _powerLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                await WithTimeoutAsync(_powerSupply1.ApplyAsync(ComponentSupplyChannel, voltage, ComponentCurrentLimitA, token), DefaultHardwareTimeoutMs, "组件供电参数设置", token).ConfigureAwait(false);
                await WithTimeoutAsync(_powerSupply1.SetOutputEnabledAsync(ComponentSupplyChannel, true, token), DefaultHardwareTimeoutMs, "组件供电开启", token).ConfigureAwait(false);
                _componentSupplyOn = true;
                await Task.Delay(500, token).ConfigureAwait(false);
            }
            finally
            {
                _powerLock.Release();
            }

            Application.Current?.Dispatcher?.Invoke(() => { IsPowerOn = true; PowerStatus = "已上电"; });
        }

        private async Task CleanupPowerSupplyAsync()
        {
            await _powerLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                if (_powerSupply1 == null)
                    return;

                try { if (_componentSupplyOn) await WithTimeoutAsync(_powerSupply1.SetOutputEnabledAsync(ComponentSupplyChannel, false, CancellationToken.None), DefaultHardwareTimeoutMs, "组件供电关闭", CancellationToken.None).ConfigureAwait(false); } catch { }
                try { if (_relaySupplyOn) await WithTimeoutAsync(_powerSupply1.SetOutputEnabledAsync(RelaySupplyChannel, false, CancellationToken.None), DefaultHardwareTimeoutMs, "继电器供电关闭", CancellationToken.None).ConfigureAwait(false); } catch { }
                try { await WithTimeoutAsync(_powerSupply1.DisconnectAsync(CancellationToken.None), DefaultHardwareTimeoutMs, "电源断开", CancellationToken.None).ConfigureAwait(false); } catch { }
                try { await _powerSupply1.DisposeAsync().ConfigureAwait(false); } catch { }
            }
            finally
            {
                _powerSupply1 = null;
                _relaySupplyOn = false;
                _componentSupplyOn = false;
                _powerLock.Release();
            }
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

        /// <summary>
        /// 检查全局上电状态，如已上电则弹窗询问是否下电后继续
        /// </summary>
        private bool CheckAndRequestPowerOffIfNeeded()
        {
            IBoardPowerService svc;
            try { svc = ContainerLocator.Container.Resolve<IBoardPowerService>(); }
            catch { return true; }

            if (svc?.IsPowered != true)
                return true;

            var result = MessageBox.Show(
                "当前加放油单板已上电（192.168.1.15 CH1），该测试项需要先下电再重新上电才能正确执行。\n\n是否立即下电并开始测试？",
                "上电状态确认",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            return result == MessageBoxResult.Yes;
        }

        /// <summary>
        /// 重置所有测试数据为默认值
        /// </summary>
        private void ResetTestData()
        {
            StepAResult = "--";
            StepBResult = "--";
            StepCResult = "--";
            OverallResult = "--";
            LastTestTime = "--";
            ImpedanceGrounded = null;
            ImpedanceOpen = null;
            J14Voltage = null;
            ImpedanceJ6 = null; ImpedanceJ7 = null; ImpedanceJ8 = null; ImpedanceJ9 = null;
            ImpedanceJ10 = null; ImpedanceJ11 = null; ImpedanceJ12 = null; ImpedanceJ13 = null;
            ImpedanceOpenJ6 = null; ImpedanceOpenJ7 = null; ImpedanceOpenJ8 = null; ImpedanceOpenJ9 = null;
            ImpedanceOpenJ10 = null; ImpedanceOpenJ11 = null; ImpedanceOpenJ12 = null; ImpedanceOpenJ13 = null;
        }

        /// <summary>
        /// 步骤c单电压档测试（手动/自动测试使用下拉框选中的电压值）
        /// </summary>
        private async Task<bool> RunStepCSingleAsync(double voltage, CancellationToken token)
        {
            AddLog($"步骤c: 正在上电（{voltage:F0}V）并测量J14电压...");

            await ConnectJ14VoltageMatrixRoutesAsync(token).ConfigureAwait(false);
            await Task.Delay(500, token).ConfigureAwait(false);
            await ApplyPowerAsync(voltage, token).ConfigureAwait(false);

            double v = await ReadJ14VoltageAsync(token).ConfigureAwait(false);

            Application.Current?.Dispatcher?.Invoke(() => J14Voltage = v);

            bool pass = v >= J14VoltageLowerLimitV;
            AddLog($"  {voltage:F0}V: J14电压={v:F2}V，判据: ≥{J14VoltageLowerLimitV}V，结果={(pass ? "PASS" : "FAIL")}");
            return pass;
        }

        private async void OnManualTest()
        {
            if (IsManualTestRunning)
            {
                await StopManualTestAsync();
                return;
            }

            if (!CheckAndRequestPowerOffIfNeeded())
            {
                AddLog("用户已取消，手动测试未开始");
                return;
            }

            Application.Current?.Dispatcher?.Invoke(ResetTestData);
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
                AddLog("正在停止自动测试...");
                _opCts?.Cancel();
                await SafeResetHardwareAsync();
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

            if (!CheckAndRequestPowerOffIfNeeded())
            {
                AddLog("用户已取消，自动测试未开始");
                return;
            }

            Application.Current?.Dispatcher?.Invoke(ResetTestData);
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

            Application.Current?.Dispatcher?.Invoke(ResetTestData);
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

        private async Task<string> ExecuteAutoTestCoreAsync(CancellationToken token, bool batchMode = false)
        {
            Application.Current?.Dispatcher?.Invoke(() => IsAutoTestRunning = true);
            AddLog("========== 自动测试开始 ==========");

            try
            {
                await InitializeHardwareAsync(token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();

                AddLog("--- 步骤a: 下电 + DO接地 + 测阻抗 ---");
                await RunStepAAsync().ConfigureAwait(false);
                token.ThrowIfCancellationRequested();

                AddLog("--- 步骤b: 下电 + DO开路 + 测阻抗 ---");
                await RunStepBAsync().ConfigureAwait(false);
                token.ThrowIfCancellationRequested();

                AddLog(batchMode ? "--- 步骤c: 18V/28V/32V上电 + 测J14电压 ---" : $"--- 步骤c: {SelectedSupplyVoltage:F0}V上电 + 测J14电压 ---");
                bool stepCPass;
                if (batchMode)
                    stepCPass = await RunStepCAcrossVoltagesAsync(new[] { 18.0, 28.0, 32.0 }, token).ConfigureAwait(false);
                else
                    stepCPass = await RunStepCSingleAsync(SelectedSupplyVoltage, token).ConfigureAwait(false);
                Application.Current?.Dispatcher?.Invoke(() => StepCResult = stepCPass ? "PASS" : "FAIL");
                token.ThrowIfCancellationRequested();

                await ResetHardwareAsync(CancellationToken.None, preserveComponentPower: false).ConfigureAwait(false);

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
                return overallPass ? "PASS" : "FAIL";
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
                return "FAIL";
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
                await EnsureJy7131ConnectedAndRunningAsync(token).ConfigureAwait(false);

                // 步骤2：设置组件供电状态（下电）- CH1不供电
                AddLog("正在设置组件供电状态: 下电（CH1不供电）...");
                try
                {
                    await EnsurePowerSupply1ConnectedAsync(token).ConfigureAwait(false);
                    await ApplyComponentDownAsync(token).ConfigureAwait(false);
                    AddLog("组件供电状态已设置为下电");
                }
                catch (Exception ex)
                {
                    AddLog($"组件下电状态设置异常: {ex.Message}");
                    throw;
                }

                // 步骤3：继电器供电上电（CH2 24V）
                AddLog("正在开启继电器供电（CH2 24V）...");
                try
                {
                    await EnableRelaySupplyAsync(token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    AddLog($"继电器供电上电异常: {ex.Message}");
                    throw;
                }

                //// 步骤4：激活继电器（DO15高电平 + 485继电器第4路），隔离产品与试验台
                //AddLog("正在激活继电器（DO15高电平），隔离产品...");
                //try
                //{
                //    if (_jy7131Api != null && _jy7131Api.IsConnected)
                //    {
                //        await _jy7131Api.WriteDoAsync(RelayControlChannel, true, token);
                //        IsRelayActivated = true;
                //        await Task.Delay(500, token);
                //        AddLog("DO15输出完成，继电器线圈得电");

                //        // 打开485继电器第4路（index=3，从0开始计数），配合DO15完成产品隔离
                //        AddLog("正在打开485继电器第4路...");
                //        try
                //        {
                //            await _jy7131Api.SetRelayAsync(3, true, token);
                //            AddLog("485继电器第4路已打开");
                //        }
                //        catch (Exception ex)
                //        {
                //            AddLog($"485继电器操作失败: {ex.Message}");
                //        }

                //        AddLog("继电器已激活，产品已隔离（下电状态）");
                //    }
                //    else
                //    {
                //        await _simulation.ApplyComponentDownStateAsync(AddLog, token);
                //        IsRelayActivated = true;
                //    }
                //}
                //catch (Exception ex)
                //{
                //    AddLog($"继电器激活异常: {ex.Message}");
                //}

                // 步骤5：配置矩阵开关
                try
                {
                    await DisconnectAllMatrixRoutesAsync(token).ConfigureAwait(false);
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

        private async Task ResetHardwareAsync(CancellationToken token, bool preserveComponentPower = false)
        {
            IsBusy = true;
            try
            {
                // 步骤1：复位继电器（DO15低电平 + 485继电器第4路关闭），恢复产品连接
                if (IsRelayActivated)
                {
                    try
                    {
                        await EnsureJy7131ConnectedAndRunningAsync(token).ConfigureAwait(false);

                        AddLog("正在复位继电器（DO15低电平）...");
                        await _jy7131Api.WriteDoAsync(RelayControlChannel, false, token);
                        await Task.Delay(300);
                        AddLog("DO15输出完成，继电器线圈失电");

                        // 关闭485继电器第4路（index=3，从0开始计数），配合DO15恢复产品连接
                        AddLog("正在关闭485继电器前4路...");
                        try
                        {
                            await _jy7131Api.SetRelayAsync(0, false, token);
                            await _jy7131Api.SetRelayAsync(1, false, token);
                            await _jy7131Api.SetRelayAsync(2, false, token);
                            await _jy7131Api.SetRelayAsync(3, false, token);
                            await Task.Delay(300);
                            AddLog("485继电器前4路已关闭");
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
                try
                {
                    await DisableRelaySupplyAsync(token).ConfigureAwait(false);
                    AddLog("继电器供电已关闭");
                }
                catch (Exception ex)
                {
                    AddLog($"继电器供电关闭异常: {ex.Message}");
                }

                // 步骤2：断开矩阵开关
                await DisconnectAllMatrixRoutesAsync(token).ConfigureAwait(false);

                // 断开万用表
                try
                {
                    await CleanupDmmAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    AddLog($"万用表断开异常: {ex.Message}");
                }

                // 步骤3：断开7131板卡
                try
                {
                    await CleanupJy7131Async().ConfigureAwait(false);
                    AddLog("7131板卡已断开");
                }
                catch (Exception ex)
                {
                    AddLog($"7131板卡断开异常: {ex.Message}");
                }

                if (!preserveComponentPower)
                {
                    await ApplyComponentDownAsync(token).ConfigureAwait(false);
                    AddLog($"组件下电");
                }
                else
                {
                    AddLog("保留组件当前供电状态，不执行自动下电");
                }

                try { await CleanupPowerSupplyAsync().ConfigureAwait(false); } catch { }

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
                using (var cts = new CancellationTokenSource(15000))
                {
                    await ResetHardwareAsync(cts.Token);
                }
            }
            catch { }
        }

        private async Task<bool> MatrixDisconnectWithTimeoutAsync(string nodeIn, string nodeOut, int slot, string ip, CancellationToken token)
        {
            var matrix = MatrixControlService.Instance;
            try
            {
                return await WithTimeoutAsync(matrix.DisconnectNodesAsync(nodeIn, nodeOut, slot, ip), DefaultHardwareTimeoutMs, "矩阵断开", token).ConfigureAwait(false);
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> MatrixConnectWithTimeoutAsync(string nodeIn, string nodeOut, int slot, string ip, CancellationToken token)
        {
            var matrix = MatrixControlService.Instance;
            return await WithTimeoutAsync(matrix.ConnectNodesAsync(nodeIn, nodeOut, slot, ip), DefaultHardwareTimeoutMs, "矩阵连接", token).ConfigureAwait(false);
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
                // SetDoOutputAsync内部会自动设置sinking模式并启动板卡
                AddLog("步骤a: 设置DO1-DO14输出接地信号...");
                await SetDoOutputAsync(true, token); // true = 接地

                // 等待信号稳定
                await Task.Delay(300, token);

                // 测量J6-J13各点阻抗
                AddLog("步骤a: 测量J6-J13对地阻抗...");
                double totalOhm = 0;
                int validCount = 0;
                bool allPass = true;

                for (int i = 0; i < J6ToJ13Points.Length; i++)
                {
                    double ohm = await ReadImpedanceForPointAsync(i, token);
                    double adjustedOhm = AdjustGroundedImpedance(i, ohm);
                    _j6ToJ13Impedances[i] = ohm;
                    Application.Current?.Dispatcher?.Invoke(() => SetImpedanceValue(i, true, ohm));
                    totalOhm += adjustedOhm;
                    validCount++;

                    bool pointPass = adjustedOhm < ImpedanceGroundedUpperLimitOhm;
                    allPass &= pointPass;
                    AddLog($"  {J6ToJ13Points[i]}: 实测{ohm:F1}Ω，回路{adjustedOhm:F1}Ω {(pointPass ? "✓" : "✗")}");
                }

                double avgOhm = validCount > 0 ? totalOhm / validCount : 0;
                Application.Current?.Dispatcher?.Invoke(() => ImpedanceGrounded = avgOhm);

                Application.Current?.Dispatcher?.Invoke(() => StepAResult = allPass ? "PASS" : "FAIL");
                AddLog($"a) 平均对地阻抗(扣回路)={avgOhm:F1}Ω，判据: <{ImpedanceGroundedUpperLimitOhm}Ω，结果={(allPass ? "PASS" : "FAIL")}");

                UpdateOverallIfReady();
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task<bool> RunStepCAcrossVoltagesAsync(IEnumerable<double> voltages, CancellationToken token)
        {
            bool allPass = true;

            foreach (var voltage in voltages)
            {
                token.ThrowIfCancellationRequested();

                AddLog($"步骤c: 正在上电（{voltage:F0}V）并测量J14电压...");

                await ConnectJ14VoltageMatrixRoutesAsync(token).ConfigureAwait(false);
                await Task.Delay(500, token).ConfigureAwait(false);

                await ApplyPowerAsync(voltage, token).ConfigureAwait(false);

                double v = await ReadJ14VoltageAsync(token).ConfigureAwait(false);
                Application.Current?.Dispatcher?.Invoke(() => J14Voltage = v);

                bool pass = v >= J14VoltageLowerLimitV;
                allPass &= pass;
                AddLog($"  {voltage:F0}V: J14电压={v:F2}V，判据: ≥{J14VoltageLowerLimitV}V，结果={(pass ? "PASS" : "FAIL")}");
            }

            AddLog($"步骤c汇总: {(allPass ? "PASS" : "FAIL")}");
            return allPass;
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
                await Task.Delay(300, token);

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

        private async Task RunStepCAsync()
        {
            if (_opCts == null)
                return;

            var token = _opCts.Token;
            IsBusy = true;
            try
            {
                // 切换矩阵通路：先断开a/b的阻抗通路，再接通J14电压测量通路
                await ConnectJ14VoltageMatrixRoutesAsync(token);
                await Task.Delay(300, token);

                //如果继电器已激活则需复位 --> 然后再上电测电压
                if (IsRelayActivated)
                {
                    if (_jy7131Api?.IsConnected == true)
                    {
                        if (!_jy7131Api.IsRunning)
                        {
                            await _jy7131Api.SetOutputModeAsync(Jy7131OutputMode.Sinking, _opCts.Token);
                            await _jy7131Api.StartAsync(_opCts.Token);
                        }

                        // 1. 先复位 DO15
                        AddLog("正在复位 DO15 隔离继电器...");
                        await _jy7131Api.WriteDoAsync(RelayControlChannel, false, _opCts.Token);
                        await Task.Delay(200);

                        // 2. 再关闭 485 继电器第4路
                        try
                        {
                            await _jy7131Api.SetRelayAsync(3, false, _opCts.Token);
                            await Task.Delay(200);
                            AddLog("485 继电器第 4 路已关闭");
                        }
                        catch (Exception ex)
                        {
                            AddLog($"485继电器操作失败: {ex.Message}");
                        }

                        await _jy7131Api.WriteDoAsync(RelayControlChannel, false, _opCts.Token);
                        try
                        {
                            var mask = await _jy7131Api.ReadDoBitmaskAsync(_opCts.Token);
                            var ok = int.TryParse(RelayControlChannel.Substring(2), out var doIdx);
                            var bit = ok ? doIdx : 14;
                            AddLog($"DO写回读取: mask=0x{mask:X8}，{RelayControlChannel}={(mask & (1u << bit)) != 0}");
                        }
                        catch (Exception ex)
                        {
                            AddLog($"DO写回读取失败: {ex.Message}");
                        }
                    }
                    IsRelayActivated = false;
                    await Task.Delay(100);
                    AddLog("继电器已复位");
                }

                // 上电
                await ApplyPowerAsync(SelectedSupplyVoltage, token);

                // 测量J14电压
                AddLog("步骤c: 正在测量J14电压...");
                double v = await ReadJ14VoltageAsync(token);
                Application.Current?.Dispatcher?.Invoke(() => J14Voltage = v);

                bool pass = v >= J14VoltageLowerLimitV;
                Application.Current?.Dispatcher?.Invoke(UpdateStepCResultFromVoltages);
                AddLog($"c) {SelectedSupplyVoltage:F0}V: J14电压={v:F2}V，判据: ≥{J14VoltageLowerLimitV}V，结果={(pass ? "PASS" : "FAIL")}");

                UpdateOverallIfReady();
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void UpdateStepCResultFromVoltages()
        {
            if (!J14Voltage.HasValue)
            {
                StepCResult = "--";
                return;
            }
            StepCResult = J14Voltage.Value >= J14VoltageLowerLimitV ? "PASS" : "FAIL";
        }

        private async Task ApplyPowerDownAsync(CancellationToken token)
        {
            if (!IsPowerOn && IsRelayActivated)
            {
                try { await EnableRelaySupplyAsync(token).ConfigureAwait(false); }
                catch (Exception ex) { AddLog($"继电器供电开启异常: {ex.Message}"); }

                AddLog("已处于下电且继电器已激活，跳过重复下电/继电器动作");
                Application.Current?.Dispatcher?.Invoke(() => { IsPowerOn = false; PowerStatus = "未上电"; });
                return;
            }

            // 使用DO15控制继电器，将产品与试验台隔离（下电）
            // DO15高电平 → 继电器得电 → NC跳NO → 产品隔离
            try
            {
                await ApplyComponentDownAsync(token).ConfigureAwait(false);
                await EnableRelaySupplyAsync(token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AddLog($"组件下电/继电器供电控制异常: {ex.Message}");
                throw;
            }

            try
            {
                await EnsureJy7131ConnectedAndRunningAsync(token).ConfigureAwait(false);

                AddLog("正在激活继电器（DO15高电平），隔离产品...");
                await _jy7131Api.WriteDoAsync(RelayControlChannel, false, token);
                IsRelayActivated = true;
                await Task.Delay(500, token);
                AddLog("继电器已激活，产品已隔离（下电）");
            }
            catch (Exception ex)
            {
                AddLog($"DO15控制异常: {ex.Message}");
                throw;
            }
            Application.Current?.Dispatcher?.Invoke(() => { IsPowerOn = false; PowerStatus = "未上电"; });
        }

        private async Task ApplyPowerAsync(double voltage, CancellationToken token)
        {
            // 使用DO15控制继电器，恢复产品与试验台连接（上电）
            // DO15低电平 → 继电器失电 → 触点恢复NC → 产品连接
            try
            {
                await EnableRelaySupplyAsync(token).ConfigureAwait(false);
                await ApplyComponentVoltageAsync(voltage, token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AddLog($"组件上电控制异常: {ex.Message}");
                throw;
            }

            try
            {
                await EnsureJy7131ConnectedAndRunningAsync(token).ConfigureAwait(false);

                AddLog("正在复位继电器（DO15低电平），恢复产品连接...");
                await _jy7131Api.WriteDoAsync(RelayControlChannel, false, token);
                IsRelayActivated = false;
                await Task.Delay(500, token);
                AddLog("继电器已复位，产品已连接");
            }
            catch (Exception ex)
            {
                AddLog($"DO15控制异常: {ex.Message}");
                throw;
            }
            Application.Current?.Dispatcher?.Invoke(() => { IsPowerOn = true; PowerStatus = "已上电"; });
        }

        /// <summary>
        /// 设置DO1-DO14输出状态
        /// </summary>
        /// <param name="grounded">true=接地（高电平），false=开路（低电平）</param>
        private async Task SetDoOutputAsync(bool grounded, CancellationToken token)
        {
            try
            {
                await EnsureJy7131ConnectedAndRunningAsync(token).ConfigureAwait(false);

                // 1. 先操作 485 继电器第 4 路
                AddLog($"正在{(grounded ? "打开" : "关闭")} 485 继电器前 4 路...");
                try
                {
                    await _jy7131Api.SetRelayAsync(0, grounded, token);
                    await _jy7131Api.SetRelayAsync(1, grounded, token);
                    await _jy7131Api.SetRelayAsync(2, grounded, token);
                    await _jy7131Api.SetRelayAsync(3, grounded, token);
                    await Task.Delay(300, token);
                    AddLog($"485 继电器前 4 路已{(grounded ? "打开" : "关闭")}");
                }
                catch (Exception ex)
                {
                    AddLog($"485 继电器操作失败: {ex.Message}");
                }

                // 2. 再设置 DO1-DO14 输出
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
                    uint expectedMask = grounded ? 0x3FFFu : 0x0000u; // bit0-bit13全高或全低
                    uint actualDo0To13 = mask & 0x3FFFu;
                    bool verified = (grounded && actualDo0To13 == expectedMask) || (!grounded && actualDo0To13 == 0);
                    AddLog($"DO回读验证: mask=0x{mask:X8}, DO0-13=0x{actualDo0To13:X4}, 期望=0x{expectedMask:X4}, {(verified ? "✓" : "✗")}");

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
                AddLog($"DO1-DO14输出异常: {ex.Message}");
                throw;
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

            try { await DisconnectJ14VoltageMatrixRoutesAsync(token).ConfigureAwait(false); } catch { }

            await _matrixLock.WaitAsync(token);
            try
            {
                try { await MatrixDisconnectWithTimeoutAsync(MatrixDmmImpedance.In, MatrixDmmImpedance.Out, MatrixSlotDmmDo, MatrixIpAddress, token).ConfigureAwait(false); } catch { }
                foreach (var ch in MatrixDoImpedancePoints)
                {
                    try { await MatrixDisconnectWithTimeoutAsync(ch.In, ch.Out, MatrixSlotDo, MatrixIpAddress, token).ConfigureAwait(false); } catch { }
                }

                var okDmm = await MatrixConnectWithTimeoutAsync(MatrixDmmImpedance.In, MatrixDmmImpedance.Out, MatrixSlotDmmDo, MatrixIpAddress, token).ConfigureAwait(false);
                var p = MatrixDoImpedancePoints[pointIndex];
                await Task.Delay(250, token);
                var okP = await MatrixConnectWithTimeoutAsync(p.In, p.Out, MatrixSlotDo, MatrixIpAddress, token).ConfigureAwait(false);
                await Task.Delay(MatrixSwitchSettleDelayMs, token);

                if (!okDmm || !okP)
                {
                    throw new InvalidOperationException("矩阵通路连接失败，无法执行真实阻抗测量");
                }

                return await ReadImpedanceAsync(token);
            }
            finally
            {
                try
                {
                    var p = MatrixDoImpedancePoints[pointIndex];
                    await Task.Delay(250, token);
                    try { await MatrixDisconnectWithTimeoutAsync(p.In, p.Out, MatrixSlotDo, MatrixIpAddress, token).ConfigureAwait(false); } catch { }
                    try { await MatrixDisconnectWithTimeoutAsync(MatrixDmmImpedance.In, MatrixDmmImpedance.Out, MatrixSlotDmmDo, MatrixIpAddress, token).ConfigureAwait(false); } catch { }
                }
                catch { }
                _matrixLock.Release();
            }
        }

        private async Task<double> ReadImpedanceAsync(CancellationToken token)
        {
            if (_useSimulatedDmm)
                throw new InvalidOperationException("万用表未就绪，无法执行真实阻抗测量");

            if (_dmmSocket == null)
            {
                throw new InvalidOperationException("未注入DMM API，无法执行真实阻抗测量");
            }

            try
            {
                await EnsureDmmConnectedAsync(token);
                var reading = await _dmmSocket.ReadOnceAsync(DmmMeasureMode.RES, new DmmReadOptions { TimeoutMilliseconds = 8000 }, token);
                
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
                        await _dmmSocket.DisconnectAsync(CancellationToken.None);
                        await Task.Delay(500, token);
                        await _dmmSocket.ConnectAsync(DmmIpAddress, token);
                        AddLog("DMM重新连接成功");
                        
                        // 重试一次测量
                        reading = await _dmmSocket.ReadOnceAsync(DmmMeasureMode.RES, new DmmReadOptions { TimeoutMilliseconds = 8000 }, token);
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
                AddLog($"万用表阻抗测量异常: {ex.Message}");
                throw;
            }
        }

        private async Task<double> ReadJ14VoltageAsync(CancellationToken token)
        {
            if (_useSimulatedDmm)
                throw new InvalidOperationException("万用表未就绪，无法执行真实电压测量");

            if (_dmmSocket == null)
            {
                throw new InvalidOperationException("未注入DMM API，无法执行真实电压测量");
            }

            try
            {
                await EnsureDmmConnectedAsync(token);
                var reading = await _dmmSocket.ReadOnceAsync(DmmMeasureMode.DCV, new DmmReadOptions { TimeoutMilliseconds = 8000 }, token);
                if (reading?.Value == null)
                    throw new InvalidOperationException("DMM未返回电压值");

                _useSimulatedDmm = false;
                AddLog("电压来源: 万用表");
                return reading.Value.Value;
            }
            catch (Exception ex)
            {
                AddLog($"万用表电压测量异常: {ex.Message}");
                throw;
            }
        }

        private async Task EnsureDmmConnectedAsync(CancellationToken token)
        {
            if (_dmmSocket == null)
                return;

            if (_dmmSocket.IsConnected)
                return;

            await _dmmLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (_dmmSocket.IsConnected)
                    return;

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeoutCts.CancelAfter(DefaultHardwareTimeoutMs);

                try
                {
                    AddLog($"正在连接万用表: {DmmIpAddress}...");
                    await _dmmSocket.ConnectAsync(DmmIpAddress, timeoutCts.Token).ConfigureAwait(false);
                    AddLog($"万用表连接成功: {_dmmSocket.IpAddress}");
                }
                catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !token.IsCancellationRequested)
                {
                    throw new TimeoutException($"万用表连接超时（{DefaultHardwareTimeoutMs}ms）");
                }
            }
            finally
            {
                _dmmLock.Release();
            }
        }

        private async Task DisconnectAllMatrixRoutesAsync(CancellationToken token)
        {
            await _matrixLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                try { await DisconnectJ14VoltageMatrixRoutesAsync(token).ConfigureAwait(false); } catch { }
                try { await MatrixDisconnectWithTimeoutAsync(MatrixDmmImpedance.In, MatrixDmmImpedance.Out, MatrixSlotDmmDo, MatrixIpAddress, token).ConfigureAwait(false); } catch { }
                foreach (var ch in MatrixDoImpedancePoints)
                {
                    try { await MatrixDisconnectWithTimeoutAsync(ch.In, ch.Out, MatrixSlotDo, MatrixIpAddress, token).ConfigureAwait(false); } catch { }
                }
            }
            finally
            {
                _matrixLock.Release();
            }
        }

        private async Task DisconnectJ14VoltageMatrixRoutesAsync(CancellationToken token)
        {
            try { await MatrixDisconnectWithTimeoutAsync(MatrixJ14VoltagePoint.In, MatrixJ14VoltagePoint.Out, MatrixSlotDo, MatrixIpAddress, token).ConfigureAwait(false); } catch { }
            try { await MatrixDisconnectWithTimeoutAsync(MatrixDmmImpedance.In, MatrixDmmImpedance.Out, MatrixSlotDmmDo, MatrixIpAddress, token).ConfigureAwait(false); } catch { }
        }

        private async Task ConnectJ14VoltageMatrixRoutesAsync(CancellationToken token)
        {
            await DisconnectAllMatrixRoutesAsync(token).ConfigureAwait(false);
            var okPoint = await MatrixConnectWithTimeoutAsync(MatrixJ14VoltagePoint.In, MatrixJ14VoltagePoint.Out, MatrixSlotDo, MatrixIpAddress, token).ConfigureAwait(false);
            var okDmm = await MatrixConnectWithTimeoutAsync(MatrixDmmImpedance.In, MatrixDmmImpedance.Out, MatrixSlotDmmDo, MatrixIpAddress, token).ConfigureAwait(false);
            await Task.Delay(MatrixSwitchSettleDelayMs, token);
            if (!okPoint || !okDmm)
                throw new InvalidOperationException("矩阵通路连接失败，无法执行J14电压测量");
        }

        private string GetDmmIpAddress() => DmmIpAddress;

        private async Task ToggleRelayAsync()
        {
            if (_opCts == null) return;
            IsBusy = true;
            try
            {
                if (IsRelayActivated)
                {
                    await EnsureJy7131ConnectedAndRunningAsync(_opCts.Token).ConfigureAwait(false);

                    // 1. 先复位 DO15
                    AddLog("正在复位 DO15 隔离继电器...");
                    await _jy7131Api.WriteDoAsync(RelayControlChannel, false, _opCts.Token);
                    await Task.Delay(200);

                    // 2. 再关闭 485 继电器第4路
                    try
                    {
                        await _jy7131Api.SetRelayAsync(3, false, _opCts.Token);
                        await Task.Delay(200);
                        AddLog("485 继电器第 4 路已关闭");
                    }
                    catch (Exception ex)
                    {
                        AddLog($"485继电器操作失败: {ex.Message}");
                    }

                    await _jy7131Api.WriteDoAsync(RelayControlChannel, false, _opCts.Token);
                    try
                    {
                        var mask = await _jy7131Api.ReadDoBitmaskAsync(_opCts.Token);
                        var ok = int.TryParse(RelayControlChannel.Substring(2), out var doIdx);
                        var bit = ok ? doIdx : 14;
                        AddLog($"DO写回读取: mask=0x{mask:X8}，{RelayControlChannel}={(mask & (1u << bit)) != 0}");
                    }
                    catch (Exception ex)
                    {
                        AddLog($"DO写回读取失败: {ex.Message}");
                    }
                    IsRelayActivated = false;
                    await Task.Delay(100);
                    AddLog("继电器已复位");
                }
                else
                {
                    await EnsureJy7131ConnectedAndRunningAsync(_opCts.Token).ConfigureAwait(false);

                    // 1. 先打开 485 继电器第4路
                    try
                    {
                        await _jy7131Api.SetRelayAsync(3, true, _opCts.Token);
                        await Task.Delay(200);
                        AddLog("485 继电器第 4 路已打开");
                    }
                    catch (Exception ex)
                    {
                        AddLog($"485 继电器操作失败: {ex.Message}");
                    }

                    // 2. 再输出 DO15 激活隔离继电器
                    AddLog("正在输出 DO15 激活隔离继电器...");
                    await _jy7131Api.WriteDoAsync(RelayControlChannel, true, _opCts.Token);
                    await Task.Delay(500);
                    try
                    {
                        var mask = await _jy7131Api.ReadDoBitmaskAsync(_opCts.Token);
                        var ok = int.TryParse(RelayControlChannel.Substring(2), out var doIdx);
                        var bit = ok ? doIdx : 14;
                        AddLog($"DO写回读取: mask=0x{mask:X8}，{RelayControlChannel}={(mask & (1u << bit)) != 0}");
                    }
                    catch (Exception ex)
                    {
                        AddLog($"DO写回读取失败: {ex.Message}");
                    }
                    IsRelayActivated = true;
                    await Task.Delay(100);
                    AddLog("继电器已激活");
                }
            }
            catch (Exception ex) { AddLog($"继电器异常: {ex.Message}"); }
            finally { IsBusy = false; }

            return;
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
                if (grounded)
                {
                    double adjusted = AdjustGroundedImpedance(idx, v);
                    AddLog($"{J6ToJ13Points[idx]}阻抗: 实测{v:F1}Ω，回路{adjusted:F1}Ω");
                }
                else
                {
                    AddLog($"{J6ToJ13Points[idx]}阻抗={v:F1}Ω");
                }
            }
            catch (Exception ex) { AddLog($"测量异常: {ex.Message}"); }
            finally { IsBusy = false; }
        }

        private static double AdjustGroundedImpedance(int idx, double measuredOhm)
        {
            if (idx < 0 || idx >= GroundLoopImpedanceOffsetsOhm.Length)
                return measuredOhm;

            return measuredOhm - GroundLoopImpedanceOffsetsOhm[idx];
        }

        private void SetImpedanceValue(int idx, bool grounded, double v)
        {
            if (grounded)
            {
                 //减去回路阻抗
                var adjusted = AdjustGroundedImpedance(idx, v);
                if (idx == 0) ImpedanceJ6 = adjusted; else if (idx == 1) ImpedanceJ7 = adjusted;
                else if (idx == 2) ImpedanceJ8 = adjusted; else if (idx == 3) ImpedanceJ9 = adjusted;
                else if (idx == 4) ImpedanceJ10 = adjusted; else if (idx == 5) ImpedanceJ11 = adjusted;
                else if (idx == 6) ImpedanceJ12 = adjusted; else if (idx == 7) ImpedanceJ13 = adjusted;
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
