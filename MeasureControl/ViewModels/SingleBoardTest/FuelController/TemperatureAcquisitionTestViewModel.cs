using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MeasureControl.Events;
using MeasureControl.Models;
using MeasureControl.Services;
using MeasureControl.Services.HardwareApis;
using MeasureControl.Simulations.FuelController;
using Prism.Commands;
using Prism.Events;
using Prism.Ioc;
using Prism.Mvvm;
using System.Windows;
using MeasureControl.Views.Dialogs;

namespace MeasureControl.ViewModels.SingleBoardTest.FuelController
{
    /// <summary>
    /// ============================================================================
    /// 温度采集功能测试 ViewModel (TemperatureAcquisitionTestViewModel)
    /// ============================================================================
    /// 
    /// 【测试目的】
    /// 验证加放油控制器的温度采集功能是否正常。
    /// 组件28V供电状态下，按照DS18B20U+T&amp;R规格书解析CRM_PIN7的信号，
    /// 提示并记录温度值。
    /// 
    /// 【测试流程概述】
    /// ┌─────────────────────────────────────────────────────────────────┐
    /// │  步骤1: 初始化硬件                                               │
    /// │    └── 检测单板上电状态并建立温度采集通信                         │
    /// ├─────────────────────────────────────────────────────────────────┤
    /// │  步骤2: 采集温度                                                 │
    /// │    └── 解析CRM_PIN7(POWER_TEMP)的DS18B20温度传感器信号           │
    /// ├─────────────────────────────────────────────────────────────────┤
    /// │  步骤3: 判定结果                                                 │
    /// │    └── 温度值处于[15℃, 45℃]区间内为PASS                         │
    /// ├─────────────────────────────────────────────────────────────────┤
    /// │  步骤4: 复位硬件                                                 │
    /// │    └── 断开温度采集通信连接                                      │
    /// └─────────────────────────────────────────────────────────────────┘
    /// 
    /// 【测量点说明】
    /// - CRM_PIN7: POWER_TEMP（温度传感器信号）
    /// - 信号通过IO57连接到INT_IO57（D35, 2槽179通道）
    /// 
    /// 【硬件依赖】
    /// - 加放油单板上电
    /// - DS18B20温度传感器解析
    /// 
    /// 【超时保护】
    /// 所有硬件操作都有超时保护，超时后会弹出提示框，不会导致程序卡死
    /// </summary>
    public class TemperatureAcquisitionTestViewModel : BindableBase, IDisposable
    {
        #region 常量定义

        /// <summary>测试项唯一标识，用于数据持久化</summary>
        private const string TestItemKey = "FuelController_TemperatureAcquisition";

        /// <summary>温度判定下限（℃）</summary>
        private const double TemperatureLowerLimit = 15.0;

        /// <summary>温度判定上限（℃）</summary>
        private const double TemperatureUpperLimit = 45.0;

        /// <summary>硬件初始化默认超时时间（毫秒）</summary>
        private const int DefaultTimeoutMs = 15000;

        /// <summary>硬件初始化超时后的自动重试次数</summary>
        private const int HardwareInitializationRetryCount = 1;

        /// <summary>硬件初始化超时后的重试等待时间（毫秒）</summary>
        private const int HardwareInitializationRetryDelayMs = 1500;

        /// <summary>温度采集超时时间（毫秒）</summary>
        private const int TemperatureReadTimeoutMs = 5000;

        /// <summary>28V上电后等待稳定时间（毫秒）</summary>
        private const int PowerStabilizeMs = 1000;

        /// <summary>组件28V供电电源IP地址</summary>
        private const string PowerSupply1IpAddress = "192.168.1.15";

        /// <summary>组件28V供电电压（V）</summary>
        private const double ComponentVoltage = 28.0;

        /// <summary>组件28V供电电流限制（A）</summary>
        private const double ComponentCurrentLimit = 3.0;

        #endregion

        #region 依赖服务

        private readonly ISingleBoardTestContextService _singleBoardTestContext;  // 单板测试上下文服务
        private readonly ProjectService _projectService;                           // 项目服务，用于数据持久化
        private readonly IEventAggregator _eventAggregator;                        // 事件聚合器，用于跨模块通信
        private readonly IComponentPowerStateApi _componentPowerStateApi;          // 组件供电状态API（优先使用）
        private readonly TemperatureAcquisitionSimulation _simulation;             // 仿真类，硬件不可用时使用

        private IPowerSupplyApi _powerSupply1;                                     // 第一个电源（组件28V供电）
        private FpgaIoClient _fpga;                                                 // FPGA IO TCP客户端

        #endregion

        #region 状态字段

        private bool _hardwareInitialized;                                         // 硬件是否已初始化
        private bool _fpgaConnected;                                               // FPGA TCP是否已连接
        private CancellationTokenSource _opCts;                                    // 操作取消令牌源
        private SubscriptionToken _projectSavingToken;                             // 项目保存事件订阅令牌

        private bool _isManualTestRunning;                                         // 手动测试是否正在运行
        private bool _isAutoTestRunning;                                           // 自动测试是否正在运行
        private bool _isBusy;                                                      // 是否正在执行操作
        private bool _isPowerOn;                                                   // 28V供电是否已开启
        private bool _powerManagedExternally;                                      // 电源由外部（全局按钮）托管，测试结束时不下电
        private bool _forceCleanupPowerOff;                                        // 异常时强制下电（即使外部托管）
        private bool _isManualTestInitializing;
        private bool _isAutoTestInitializing;
        private bool _isManualTestStopping;
        private bool _isAutoTestStopping;

        #endregion

        #region 测量结果字段

        private double? _temperatureValue;    // 测量的温度值（单位：℃）
        private string _testResult = "--";    // 测试结果（PASS/FAIL/--）
        private string _overallResult = "--"; // 综合结果
        private string _lastTestTime = "--";  // 上次测试时间
        private string _powerStatus = "未上电"; // 供电状态显示文本

        #endregion

        #region 构造函数

        /// <summary>
        /// 构造函数 - 通过依赖注入获取所需服务
        /// </summary>
        public TemperatureAcquisitionTestViewModel(
            ISingleBoardTestContextService singleBoardTestContext,
            ProjectService projectService,
            IEventAggregator eventAggregator,
            IComponentPowerStateApi componentPowerStateApi)
        {
            // 保存依赖服务引用
            _singleBoardTestContext = singleBoardTestContext;
            _projectService = projectService;
            _eventAggregator = eventAggregator;
            _componentPowerStateApi = componentPowerStateApi;
            _simulation = new TemperatureAcquisitionSimulation();

            // 初始化命令
            ManualTestCommand = new DelegateCommand(async () => await OnManualTestAsync());
            AutoTestCommand = new DelegateCommand(async () => await OnAutoTestAsync());
            MeasureCommand = new DelegateCommand(async () => await MeasureTemperatureAsync(), () => !IsBusy && IsManualTestRunning && _hardwareInitialized && IsPowerOn);
            ClearLogCommand = new DelegateCommand(() => Logs.Clear());

            // 加载上次保存的测试结果
            LoadPersistedState();
            
            // 订阅项目保存事件
            _projectSavingToken = _eventAggregator?.GetEvent<ProjectSavingEvent>()?.Subscribe(OnProjectSaving);
        }

        #endregion

        #region 公共属性

        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();

        public DelegateCommand ManualTestCommand { get; }
        public DelegateCommand AutoTestCommand { get; }
        public DelegateCommand MeasureCommand { get; }
        public DelegateCommand ClearLogCommand { get; }

        public bool IsPowerOn
        {
            get => _isPowerOn;
            set
            {
                if (SetProperty(ref _isPowerOn, value))
                    UpdateCommandStates();
            }
        }

        public string PowerStatus
        {
            get => _powerStatus;
            set => SetProperty(ref _powerStatus, value);
        }

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

        public double? TemperatureValue
        {
            get => _temperatureValue;
            set => SetProperty(ref _temperatureValue, value);
        }

        public string TestResult
        {
            get => _testResult;
            set => SetProperty(ref _testResult, value);
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

        #endregion

        #region 命令处理方法

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
                AddLog("步骤1: 初始化硬件设备（28V供电）...");
                await InitializeHardwareWithTimeoutAsync(_opCts.Token).ConfigureAwait(false);

                IsManualTestInitializing = false;
                IsManualTestRunning = true;
                AddLog("硬件初始化完成，请点击\"测量\"按钮进行温度采集");
            }
            catch (TimeoutException ex)
            {
                AddLog($"超时: {ex.Message}");
                Application.Current?.Dispatcher?.Invoke(() =>
                    ReMessageBox.Show(ex.Message, "超时提示", MessageBoxButton.OK, MessageBoxImage.Warning));
                _forceCleanupPowerOff = true;
                await StopManualTestAsync().ConfigureAwait(false);
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
                await ResetHardwareAsync(CancellationToken.None).ConfigureAwait(false);
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
                await ExecuteAutoTestAsync(_opCts.Token).ConfigureAwait(false);
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
                await ResetHardwareAsync(CancellationToken.None).ConfigureAwait(false);
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
                return await ExecuteAutoTestAsync(_opCts.Token).ConfigureAwait(false);
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

        private async Task<string> ExecuteAutoTestAsync(CancellationToken token)
        {
            AddLog("自动测试开始");

            AddLog("步骤1: 初始化硬件设备（28V供电）...");
            await InitializeHardwareWithTimeoutAsync(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();

            IsAutoTestInitializing = false;
            IsAutoTestRunning = true;

            AddLog("步骤2: 采集温度...");
            await MeasureTemperatureAsync().ConfigureAwait(false);
            token.ThrowIfCancellationRequested();

            AddLog($"自动测试完成，结果: {OverallResult}");
            await StopAutoTestAsync().ConfigureAwait(false);
            return OverallResult;
        }

        #endregion

        #region 硬件操作

        /// <summary>
        /// 带超时的硬件初始化
        /// </summary>
        private async Task InitializeHardwareWithTimeoutAsync(CancellationToken token)
        {
            var maxAttempts = HardwareInitializationRetryCount + 1;
            TimeoutException lastTimeoutException = null;

            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                token.ThrowIfCancellationRequested();

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeoutCts.CancelAfter(DefaultTimeoutMs);

                try
                {
                    if (attempt > 1)
                        AddLog($"开始第{attempt}次硬件初始化...");

                    await InitializeHardwareAsync(timeoutCts.Token).ConfigureAwait(false);
                    return;
                }
                catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !token.IsCancellationRequested)
                {
                    lastTimeoutException = attempt < maxAttempts
                        ? new TimeoutException($"硬件初始化超时（{DefaultTimeoutMs}ms），准备自动重试...")
                        : new TimeoutException($"硬件初始化超时（{DefaultTimeoutMs}ms），自动重试后仍失败，请检查设备连接");
                }

                if (lastTimeoutException == null)
                    break;

                AddLog(lastTimeoutException.Message);

                if (attempt >= maxAttempts)
                    break;

                try
                {
                    await ResetHardwareAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                }

                await Task.Delay(HardwareInitializationRetryDelayMs, token).ConfigureAwait(false);
            }

            throw lastTimeoutException ?? new TimeoutException($"硬件初始化超时（{DefaultTimeoutMs}ms），请检查设备连接");
        }

        /// <summary>
        /// 初始化硬件：检测供电状态并连接FPGA
        /// </summary>
        private async Task InitializeHardwareAsync(CancellationToken token)
        {
            if (_hardwareInitialized)
            {
                AddLog("硬件已初始化，跳过");
                return;
            }

            // ========== 步骤1：组件28V上电 ==========
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

            // ========== 步骤2：连接FPGA TCP服务器 ==========
            AddLog($"正在连接FPGA TCP服务器 {FpgaIoClient.DefaultIpAddress}:{FpgaIoClient.DefaultPort} ...");
            try
            {
                _fpga ??= new FpgaIoClient();
                await _fpga.ConnectAsync(token);
                _fpgaConnected = true;
                AddLog("FPGA TCP连接成功");

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

                // 启动异步接收功能
                _fpga.StartAsyncReceive(AddLog);
            }
            catch (Exception ex)
            {
                AddLog($"FPGA TCP连接异常: {ex.Message}");
                _fpgaConnected = false;
                throw;
            }

            _hardwareInitialized = true;
            UpdateCommandStates();
            AddLog("硬件初始化完成");
        }

        /// <summary>
        /// 复位硬件：断开FPGA连接
        /// </summary>
        private async Task ResetHardwareAsync(CancellationToken token)
        {
            AddLog("正在复位硬件...");

            // 断开FPGA
            if (_fpga != null)
            {
                try { _fpga.StopAsyncReceive(); } catch { }
                try { _fpga.Disconnect(); } catch { }
                _fpga = null;
                _fpgaConnected = false;
                AddLog("FPGA TCP已断开");
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
            UpdateCommandStates();
            AddLog("硬件复位完成");
            RefreshPowerStateDisplay();
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

            Application.Current?.Dispatcher?.Invoke(() =>
            {
                IsPowerOn = true;
                PowerStatus = "已上电";
            });

            AddLog($"等待电源稳定（{PowerStabilizeMs}ms）...");
            await Task.Delay(PowerStabilizeMs, token);
        }

        private bool EnsureFuelBoardPowered()
        {
            var powerService = ContainerLocator.Container.Resolve<IBoardPowerService>();
            if (powerService == null || !powerService.IsPowered)
            {
                AddLog("未检测到加放油单板上电，请先通过左上角组件上电按钮上电。");
                Application.Current?.Dispatcher?.Invoke(() =>
                    MessageBox.Show("请先点击左上角组件上电按钮，并选择“加放油单板”上电后再进行测试。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning));
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    IsPowerOn = false;
                    PowerStatus = "未上电";
                });
                return false;
            }

            if (!string.Equals(powerService.PoweredBoardType, "加放油单板", StringComparison.OrdinalIgnoreCase))
            {
                AddLog($"当前上电单板为{powerService.PoweredBoardType ?? "未知"}，请切换为加放油单板。");
                Application.Current?.Dispatcher?.Invoke(() =>
                    MessageBox.Show($"当前已上电单板为“{powerService.PoweredBoardType ?? "未知"}”，请先下电并选择“加放油单板”上电。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning));
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    IsPowerOn = false;
                    PowerStatus = "未上电";
                });
                return false;
            }

            Application.Current?.Dispatcher?.Invoke(() =>
            {
                IsPowerOn = true;
                PowerStatus = "已上电";
            });
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

        #endregion

        #region 温度采集

        /// <summary>
        /// 采集温度（带超时保护）
        /// </summary>
        private async Task MeasureTemperatureAsync()
        {
            if (IsBusy) return;

            Application.Current?.Dispatcher?.Invoke(() => IsBusy = true);

            try
            {
                using var cts = new CancellationTokenSource(TemperatureReadTimeoutMs);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                    _opCts?.Token ?? CancellationToken.None, cts.Token);

                AddLog("正在采集DS18B20温度...");
                var temperature = await ReadTemperatureAsync(linked.Token);

                var result = (temperature >= TemperatureLowerLimit && temperature <= TemperatureUpperLimit)
                    ? "PASS" : "FAIL";

                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    TemperatureValue = temperature;
                    TestResult = result;
                    OverallResult = result;
                    LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                });

                AddLog($"温度: {temperature:F2}℃  判定: {result}  [判据: {TemperatureLowerLimit}℃ ~ {TemperatureUpperLimit}℃]");
            }
            catch (OperationCanceledException)
            {
                AddLog($"温度采集超时（{TemperatureReadTimeoutMs}ms）");
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    ReMessageBox.Show($"温度采集超时（{TemperatureReadTimeoutMs}ms）", "超时提示",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                });
            }
            catch (Exception ex)
            {
                AddLog($"温度采集失败: {ex.Message}");
            }
            finally
            {
                Application.Current?.Dispatcher?.Invoke(() => IsBusy = false);
            }
        }

        /// <summary>
        /// 读取DS18B20温度值。
        /// 硬件路径：FPGA通过IO57(CRM_PIN7/POWER_TEMP)采集DS18B20信号，
        /// 上位机发送温度请求指令，FPGA回传温度数据。
        /// </summary>
        private async Task<double> ReadTemperatureAsync(CancellationToken token)
        {
            if (_fpga == null || !_fpgaConnected)
            {
                throw new InvalidOperationException("FPGA未连接，无法读取DS18B20温度");
            }

            try
            {
                var temp = await ReadDs18B20ViaMioAsync(token);
                AddLog($"温度来源: FPGA/DS18B20  {temp:F2}℃");
                return temp;
            }
            catch (Exception ex)
            {
                AddLog($"FPGA温度读取异常: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 通过FPGA TCP接口（IP=192.168.1.10, Port=5001）读取DS18B20温度。
        /// 协议：发送命令0x07（无数据），FPGA返回1个单精度浮点数（小端，单位℃）。
        /// 帧格式：帧头(AA 55) + 长度(02) + 命令(07) + 数据(00) → 应答：帧头(AA 55) + 长度(05) + 命令(07) + float32
        /// 
        /// 使用异步接收模式：发送命令后等待2-3秒，从缓存中查找07命令的响应。
        /// 这样可以避免06命令（状态消息）的干扰。
        /// </summary>
        private async Task<double> ReadDs18B20ViaMioAsync(CancellationToken token)
        {
            if (_fpga == null || !_fpgaConnected)
            {
                _fpga ??= new FpgaIoClient();
                await _fpga.ConnectAsync(token);
                _fpgaConnected = true;
                _fpga.StartAsyncReceive(AddLog);
            }

            // 记录发送时间，用于筛选发送后收到的响应
            var sendTime = DateTime.UtcNow;

            // 发送温度采集命令: AA 55 02 07 00
            AddLog("发送温度采集命令: AA 55 02 07 00");
            await _fpga.SendTemperatureCommandAsync(token);

            // 等待2-3秒，让异步接收任务收集响应
            AddLog("等待FPGA响应（2.5秒）...");
            await Task.Delay(2500, token);

            // 从缓存中查找命令为07的响应（发送时间之后收到的）
            var frames = _fpga.GetReceivedFramesByCommandAfter(0x07, sendTime);
            if (frames == null || frames.Count == 0)
            {
                // 没有找到命令07的响应，弹窗报错
                throw new InvalidOperationException("未收到FPGA温度采集响应（命令07），请检查FPGA连接状态");
            }

            // 取最新的一帧
            var latestFrame = frames.Last();
            AddLog($"收到温度响应: {latestFrame.RawHex}");

            // 解析温度数据（单精度浮点数，小端模式）
            if (latestFrame.Payload == null || latestFrame.Payload.Length < 4)
            {
                throw new InvalidOperationException($"温度数据长度不足: {latestFrame.Payload?.Length ?? 0} bytes，期望 4 bytes");
            }

            float tempF = BitConverter.ToSingle(latestFrame.Payload, 0);

            return (double)tempF;
        }

        #endregion

        #region 辅助方法

        private void AddLog(string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            var logMessage = $"[{timestamp}] {message}";

            Application.Current?.Dispatcher?.Invoke(() =>
            {
                Logs.Add(logMessage);
            });
        }

        private void ClearResults()
        {
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                TemperatureValue = null;
                TestResult = "--";
                OverallResult = "--";
                LastTestTime = "--";
            });
        }

        private void UpdateCommandStates()
        {
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                MeasureCommand?.RaiseCanExecuteChanged();
            });
        }

        #endregion

        #region 数据持久化

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

                LastTestTime = Read("LastTestTime") ?? "--";
                OverallResult = Read("OverallResult") ?? "--";
                TestResult = Read("TestResult") ?? "--";
                var tempStr = Read("TemperatureValue");
                if (!string.IsNullOrEmpty(tempStr) && double.TryParse(tempStr, out var temp))
                    TemperatureValue = temp;

                RaisePropertyChanged(nameof(LastTestTime));
                RaisePropertyChanged(nameof(OverallResult));
                RaisePropertyChanged(nameof(TestResult));
                RaisePropertyChanged(nameof(TemperatureValue));
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

                Upsert("LastTestTime", LastTestTime);
                Upsert("OverallResult", OverallResult);
                Upsert("TestResult", TestResult);
                Upsert("TemperatureValue", TemperatureValue?.ToString() ?? string.Empty);
            }
            catch { }
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            _opCts?.Cancel();
            _opCts?.Dispose();

            try { _fpga?.Disconnect(); } catch { }
            _fpga = null;

            try
            {
                if (_powerSupply1 != null)
                {
                    try { _powerSupply1.SetOutputEnabledAsync(PowerSupplyChannel.CH1, false, CancellationToken.None).GetAwaiter().GetResult(); } catch { }
                    try { _powerSupply1.DisconnectAsync(CancellationToken.None).GetAwaiter().GetResult(); } catch { }
                }
            }
            catch { }

            if (_projectSavingToken != null)
            {
                _eventAggregator?.GetEvent<ProjectSavingEvent>()?.Unsubscribe(_projectSavingToken);
                _projectSavingToken = null;
            }

            _simulation?.Dispose();
        }

        #endregion
    }
}
