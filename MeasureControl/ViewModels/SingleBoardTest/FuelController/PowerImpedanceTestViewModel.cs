using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using MeasureControl.Events;
using MeasureControl.Models;
using MeasureControl.Models.Devices;
using MeasureControl.Services;
using MeasureControl.Services.HardwareApis;
using MeasureControl.Simulations.FuelController;
using Prism.Commands;
using Prism.Events;
using Prism.Mvvm;
using System.Windows;
using MeasureControl.Views.Dialogs;
using Prism.Ioc;

namespace MeasureControl.ViewModels.SingleBoardTest.FuelController
{
    /// <summary>
    /// ============================================================================
    /// 电源阻抗测试 ViewModel (PowerImpedanceTestViewModel)
    /// ============================================================================
    /// 
    /// 【测试目的】
    /// 验证加放油控制器在断电状态下，各电源引脚之间的隔离阻抗是否满足要求。
    /// 阻抗值大于500Ω表示隔离良好，产品合格。
    /// 
    /// 【测试流程概述】
    /// ┌─────────────────────────────────────────────────────────────────┐
    /// │  步骤1: 初始化硬件                                               │
    /// │    ├── 配置矩阵开关通路（连接7131板卡和万用表）                    │
    /// │    ├── 连接7131板卡（用于DO信号输出）                             │
    /// │    └── 连接万用表（用于阻抗测量）                                 │
    /// ├─────────────────────────────────────────────────────────────────┤
    /// │  步骤2: 激活继电器                                               │
    /// │    └── 通过DO15信号控制继电器，将产品与试验台电气隔离              │
    /// ├─────────────────────────────────────────────────────────────────┤
    /// │  步骤3-6: 测量四个测试点的阻抗                                    │
    /// │    ├── A点: J3-J4 (外部28V对地)                                  │
    /// │    ├── B点: J14-J24 (内部28对地)                                 │
    /// │    ├── C点: J3-J5 (外部28V对壳体)                                │
    /// │    └── D点: J14-J5 (内部28对壳体)                                │
    /// ├─────────────────────────────────────────────────────────────────┤
    /// │  步骤7: 复位硬件                                                 │
    /// │    ├── 复位继电器（恢复产品与试验台连接）                          │
    /// │    ├── 断开矩阵开关通路                                          │
    /// │    └── 断开万用表连接                                            │
    /// └─────────────────────────────────────────────────────────────────┘
    /// 
    /// 【两种测试模式】
    /// 1. 手动测试：初始化硬件后，用户手动点击各测试点的"测量"按钮
    /// 2. 自动测试：自动依次完成所有测试点的测量，并给出综合结果
    /// 
    /// 【硬件依赖】
    /// - JY7131板卡：提供DO15数字输出信号，控制继电器
    /// - 万用表(DMM)：测量电阻值
    /// - 矩阵开关：配置信号通路
    /// 
    /// 【、保护】
    /// 所有硬件操作都有超时保护，超时后会弹出提示框，不会导致程序卡死
    /// </summary>
    public class PowerImpedanceTestViewModel : BindableBase, IDisposable
    {
        #region 常量定义

        /// <summary>测试项唯一标识，用于数据持久化</summary>
        private const string TestItemKey = "FuelController_PowerImpedance";
        
        /// <summary>阻抗判定阈值（Ω），大于此值为PASS</summary>
        private const double ImpedanceThreshold = 500.0;
        
        /// <summary>继电器控制通道，使用7131板卡的DO14（物理DO15映射到API的DO14）</summary>
        private const string RelayControlChannel = "DO14";
        
        /// <summary>硬件初始化默认超时时间（毫秒）</summary>
        private const int DefaultTimeoutMs = 6000;
        
        /// <summary>万用表测量超时时间（毫秒）</summary>
        private const int DmmTimeoutMs = 5000;
        private const string DmmTriggerDelayCommand = "TRIG:DEL 0.01";
        
        /// <summary>继电器操作超时时间（毫秒）</summary>
        private const int RelayTimeoutMs = 4000;

        private const int RelayPowerTimeoutMs = 4000;

        #endregion

        #region 依赖服务

        private readonly ISingleBoardTestContextService _singleBoardTestContext;  // 单板测试上下文服务
        private readonly ProjectService _projectService;                           // 项目服务，用于数据持久化
        private readonly IEventAggregator _eventAggregator;                        // 事件聚合器，用于跨模块通信
        private readonly IComponentPowerStateApi _componentPowerStateApi;          // 组件供电状态API（复用）
        private readonly IPxiChassisService _pxiChassisService;                   // 机箱服务，用于查找板卡设备
        private readonly IBoardPowerService _boardPowerService;           // 组件上电服务，用于强制下电
        private IJy7131Api _jy7131Api;                                             // 7131板卡API，控制DO输出（运行时动态创建）
        private readonly IDmmApi _dmmApi;                                          // 万用表API，测量电阻
        private readonly PowerImpedanceSimulation _simulation;                     // 仿真类，硬件不可用时使用

        #endregion

        #region 万用表Socket连接

        private IDmmApi _dmmSocket;                                                 // DmmSocketApi实例（与HC_6_1一致）
        private readonly SemaphoreSlim _measureLock = new SemaphoreSlim(1, 1);    // 测量操作锁

        #endregion

        #region 状态字段

        private bool _hardwareInitialized;                                         // 硬件是否已初始化
        private CancellationTokenSource _opCts;                                    // 操作取消令牌源
        private SubscriptionToken _projectSavingToken;                             // 项目保存事件订阅令牌

        private bool _isManualTestRunning;                                         // 手动测试是否正在运行
        private bool _isAutoTestRunning;                                           // 自动测试是否正在运行
        private bool _isBusy;                                                      // 是否正在执行操作
        private bool _isRelayActivated;                                            // 继电器是否已激活
        private bool _isPowerOn;                                                   // 组件供电状态（阻抗测试要求下电，此字段标记是否已完成下电初始化）
        private string _powerStatus = "已下电";                                      // 供电状态显示文本
        private bool _isManualTestInitializing;
        private bool _isAutoTestInitializing;
        private bool _isManualTestStopping;
        private bool _isAutoTestStopping;

        private bool _useSimulatedDmm;                                             // DMM不可用时走仿真测量
        private bool _relaySupplyOn;                                            // 继电器供电状态

        #endregion

        #region 测量结果字段

        // 四个测试点的阻抗值（单位：Ω）
        private double? _impedanceA;  // A点: J3-J4 (外部28V对地)
        private double? _impedanceB;  // B点: J14-J24 (内部28对地)
        private double? _impedanceC;  // C点: J3-J5 (外部28V对壳体)
        private double? _impedanceD;  // D点: J14-J5 (内部28对壳体)

        // 四个测试点的判定结果（PASS/FAIL/--）
        private string _resultA = "--";
        private string _resultB = "--";
        private string _resultC = "--";
        private string _resultD = "--";
        
        private string _overallResult = "--";   // 综合结果
        private string _lastTestTime = "--";    // 上次测试时间
        private string _relayStatus = "未激活"; // 继电器状态显示文本

        #endregion

        #region 构造函数

        /// <summary>
        /// 构造函数 - 通过依赖注入获取所需服务
        /// </summary>
        /// <param name="singleBoardTestContext">单板测试上下文</param>
        /// <param name="projectService">项目服务</param>
        /// <param name="eventAggregator">事件聚合器</param>
        /// <param name="jy7131Api">7131板卡API（可选，为null时使用仿真）</param>
        /// <param name="dmmApi">万用表API（可选，为null时使用仿真）</param>
        public PowerImpedanceTestViewModel(
            ISingleBoardTestContextService singleBoardTestContext,
            ProjectService projectService,
            IEventAggregator eventAggregator,
            IComponentPowerStateApi componentPowerStateApi,
            IPxiChassisService pxiChassisService,
            IBoardPowerService hydraulicPowerService = null,
            IDmmApi dmmApi = null)
        {
            // 保存依赖服务引用
            _singleBoardTestContext = singleBoardTestContext;
            _projectService = projectService;
            _eventAggregator = eventAggregator;
            _componentPowerStateApi = componentPowerStateApi;
            _pxiChassisService = pxiChassisService;
            _boardPowerService = hydraulicPowerService;
            _dmmApi = dmmApi;
            _simulation = new PowerImpedanceSimulation();

            // 初始化命令
            // ManualTestCommand: 手动测试按钮，点击后初始化硬件，用户手动测量各点
            ManualTestCommand = new DelegateCommand(async () => await OnManualTestAsync());
            
            // AutoTestCommand: 自动测试按钮，点击后自动完成所有测量
            AutoTestCommand = new DelegateCommand(async () => await OnAutoTestAsync());
            
            // ToggleRelayCommand: 继电器激活/复位按钮，只有在手动测试运行时才可用
            ToggleRelayCommand = new DelegateCommand(async () => await ToggleRelayAsync(), () => !IsBusy && IsManualTestRunning);

            // MeasureXCommand: 各测试点的测量按钮，只有继电器激活后才能使用
            MeasureACommand = new DelegateCommand(async () => await MeasureSinglePointAsync("A"), () => !IsBusy && IsRelayActivated);
            MeasureBCommand = new DelegateCommand(async () => await MeasureSinglePointAsync("B"), () => !IsBusy && IsRelayActivated);
            MeasureCCommand = new DelegateCommand(async () => await MeasureSinglePointAsync("C"), () => !IsBusy && IsRelayActivated);
            MeasureDCommand = new DelegateCommand(async () => await MeasureSinglePointAsync("D"), () => !IsBusy && IsRelayActivated);

            // ClearLogCommand: 清空日志
            ClearLogCommand = new DelegateCommand(() => Logs.Clear());

            // 加载上次保存的测试结果
            LoadPersistedState();
            
            // 订阅项目保存事件，在保存项目时自动保存测试结果
            _projectSavingToken = _eventAggregator?.GetEvent<ProjectSavingEvent>()?.Subscribe(OnProjectSaving);
            if (_boardPowerService != null)
                _boardPowerService.IsPoweredChanged += OnBoardPowerStateChanged;
            RefreshPowerStateDisplay();
        }

        #endregion

        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();

        public DelegateCommand ManualTestCommand { get; }
        public DelegateCommand AutoTestCommand { get; }
        public DelegateCommand ToggleRelayCommand { get; }
        public DelegateCommand MeasureACommand { get; }
        public DelegateCommand MeasureBCommand { get; }
        public DelegateCommand MeasureCCommand { get; }
        public DelegateCommand MeasureDCommand { get; }
        public DelegateCommand ClearLogCommand { get; }

        public bool IsRelayActivated
        {
            get => _isRelayActivated;
            set
            {
                if (SetProperty(ref _isRelayActivated, value))
                    UpdateCommandStates();
            }
        }

        public string RelayStatus
        {
            get => _relayStatus;
            set => SetProperty(ref _relayStatus, value);
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

        public double? ImpedanceA
        {
            get => _impedanceA;
            set => SetProperty(ref _impedanceA, value);
        }

        public double? ImpedanceB
        {
            get => _impedanceB;
            set => SetProperty(ref _impedanceB, value);
        }

        public double? ImpedanceC
        {
            get => _impedanceC;
            set => SetProperty(ref _impedanceC, value);
        }

        public double? ImpedanceD
        {
            get => _impedanceD;
            set => SetProperty(ref _impedanceD, value);
        }

        public string ResultA
        {
            get => _resultA;
            set => SetProperty(ref _resultA, value);
        }

        public string ResultB
        {
            get => _resultB;
            set => SetProperty(ref _resultB, value);
        }

        public string ResultC
        {
            get => _resultC;
            set => SetProperty(ref _resultC, value);
        }

        public string ResultD
        {
            get => _resultD;
            set => SetProperty(ref _resultD, value);
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
                return $"{taskName}/{TestItemKey}";
            }
        }

        #region 测试控制方法

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

            var hpsPreCheck = ContainerLocator.Container.Resolve<IBoardPowerService>();
            bool isPoweredBefore = hpsPreCheck != null && hpsPreCheck.IsPowered &&
                string.Equals(hpsPreCheck.PoweredBoardType, "加放油单板", StringComparison.OrdinalIgnoreCase);
            if (isPoweredBefore)
            {
                bool confirmed = false;
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    var r = ReMessageBox.Show("加放油单板当前已上电，阻抗测试需将其下电，是否继续？",
                        "提示", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    confirmed = r == MessageBoxResult.Yes;
                });
                if (!confirmed) return;
            }

            IsManualTestInitializing = true;
            ClearResults();

            _opCts?.Cancel();
            _opCts?.Dispose();
            _opCts = new CancellationTokenSource();

            _hardwareInitialized = false;
            _relaySupplyOn = false;

            AddLog("手动测试开始，正在初始化硬件...");
            try
            {
                AddLog("步骤0: 强制关闭加放油单板电源（192.168.1.15 CH1）...");
                await ForceComponentPowerOffAsync(_opCts.Token).ConfigureAwait(false);
                AddLog("步骤1: 初始化硬件设备（7131板卡、万用表）...");
                await InitializeHardwareWithTimeoutAsync(_opCts.Token).ConfigureAwait(false);
                AddLog("步骤1.5: 继电器供电上电（24V）...");
                await PowerOnRelaySupplyWithTimeoutAsync(_opCts.Token).ConfigureAwait(false);
                AddLog("步骤2: 激活继电器，隔离产品与试验台...");
                await ActivateRelayWithTimeoutAsync(_opCts.Token).ConfigureAwait(false);

                IsManualTestInitializing = false;
                IsManualTestRunning = true;
                AddLog("硬件初始化完成，请依次点击各测试项的\"测量\"按鈕进行阻抗测量");
            }
            catch (OperationCanceledException)
            {
                AddLog("手动测试初始化已取消");
                await StopManualTestAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AddLog($"手动测试初始化失败: {ex.Message}");
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
                RaisePropertyChanged(nameof(CanStartManualTest));
                RaisePropertyChanged(nameof(CanStartAutoTest));
                UpdateCommandStates();
                RefreshPowerStateDisplay();
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

            var hpsPreCheck2 = ContainerLocator.Container.Resolve<IBoardPowerService>();
            bool isPoweredBefore2 = hpsPreCheck2 != null && hpsPreCheck2.IsPowered &&
                string.Equals(hpsPreCheck2.PoweredBoardType, "加放油单板", StringComparison.OrdinalIgnoreCase);
            if (isPoweredBefore2)
            {
                bool confirmed2 = false;
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    var r = ReMessageBox.Show("加放油单板当前已上电，阻抗测试需将其下电，是否继续？",
                        "提示", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    confirmed2 = r == MessageBoxResult.Yes;
                });
                if (!confirmed2) return;
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
                await StopAutoTestAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AddLog($"自动测试异常: {ex.Message}");
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
                RaisePropertyChanged(nameof(CanStartManualTest));
                RaisePropertyChanged(nameof(CanStartAutoTest));
                RefreshPowerStateDisplay();
                AddLog("自动测试已结束");
            }
        }

        /// <summary>
        /// 供外部（整板自动测试）调用的异步测试方法
        /// 支持 await 等待完成，并通过 CancellationToken 实现取消
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>测试结果："合格" 或 "不合格"</returns>
        public async Task<string> RunOnceAsync(CancellationToken cancellationToken)
        {
            if (IsAutoTestRunning)  await StopAutoTestAsync().ConfigureAwait(false);
            if (IsManualTestRunning) await StopManualTestAsync().ConfigureAwait(false);

            _opCts?.Cancel();
            _opCts?.Dispose();
            _opCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            Application.Current?.Dispatcher?.Invoke(() => { IsAutoTestInitializing = true; ClearResults(); });

            try
            {
                return await ExecuteAutoTestAsync(_opCts.Token).ConfigureAwait(false);
            }
            finally
            {
                await StopAutoTestAsync().ConfigureAwait(false);
                Application.Current?.Dispatcher?.Invoke(() => IsAutoTestInitializing = false);
                _opCts?.Dispose();
                _opCts = null;
            }
        }

        /// <summary>
        /// 执行自动测试的核心逻辑（可等待版本）
        /// </summary>
        private async Task<string> ExecuteAutoTestAsync(CancellationToken token)
        {
            AddLog("自动测试开始");

            _hardwareInitialized = false;
            _relaySupplyOn = false;

            AddLog("步骤0: 强制关闭加放油单板电源（192.168.1.15 CH1）...");
            await ForceComponentPowerOffAsync(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();

            AddLog("步骤1: 初始化硬件设备（7131板卡、万用表）...");
            await InitializeHardwareWithTimeoutAsync(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();

            AddLog("步骤1.5: 继电器供电上电（24V）...");
            await PowerOnRelaySupplyWithTimeoutAsync(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();

            AddLog("步骤2: 激活继电器，隔离产品与试验台...");
            await ActivateRelayWithTimeoutAsync(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();

            // 初始化完成，切换到运行状态
            IsAutoTestInitializing = false;
            IsAutoTestRunning = true;

            AddLog("步骤3: 测量 J3-J4 阻抗（外郥28V对地）");
            await MeasureImpedanceWithTimeoutAsync("A", token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();

            AddLog("步骤4: 测量 J14-J24 阻抗（内郥28对地）");
            await MeasureImpedanceWithTimeoutAsync("B", token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();

            AddLog("步骤5: 测量 J3-J5 阻抗（外郥28V对壳体）");
            await MeasureImpedanceWithTimeoutAsync("C", token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();

            AddLog("步骤6: 测量 J14-J5 阻抗（内郥28对壳体）");
            await MeasureImpedanceWithTimeoutAsync("D", token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();

            EvaluateOverallResult();
            LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            AddLog($"自动测试完成，综合结果: {OverallResult}");

            await StopAutoTestAsync().ConfigureAwait(false);
            return OverallResult ?? "--";
        }

        #endregion

        #region 硬件操作方法

        /// <summary>
        /// 切换继电器状态（激活/复位）
        /// 由UI的"激活"/"复位"按钮触发
        /// </summary>
        private async Task ToggleRelayAsync()
        {
            IsBusy = true;
            try
            {
                using var cts = new CancellationTokenSource(RelayTimeoutMs);
                if (IsRelayActivated)
                {
                    // 当前已激活，执行复位操作
                    await DeactivateRelayWithTimeoutAsync(cts.Token);
                }
                else
                {
                    // 当前未激活，执行激活操作
                    await ActivateRelayWithTimeoutAsync(cts.Token);
                }
            }
            catch (TimeoutException ex)
            {
                AddLog($"继电器操作超时: {ex.Message}");
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    ReMessageBox.Show($"继电器操作超时: {ex.Message}", "超时提示",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                });
            }
            catch (OperationCanceledException)
            {
                AddLog("继电器操作已取消");
            }
            catch (Exception ex)
            {
                AddLog($"继电器操作失败: {ex.Message}");
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    ReMessageBox.Show($"继电器操作失败: {ex.Message}", "错误",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// 初始化硬件设备（带超时保护）
        /// 
        /// 【初始化顺序 - 根据API规范】
        /// 1. 配置矩阵开关通路 - 建立7131板卡和万用表的信号路由
        /// 2. 连接7131板卡 - Connect → SetOutputMode → Start
        /// 3. 连接万用表 - Connect
        /// 
        /// 【超时处理】
        /// 如果在10秒内未完成初始化，抛出TimeoutException
        /// </summary>
        private async Task InitializeHardwareWithTimeoutAsync(CancellationToken token)
        {
            // 创建链接的取消令牌，支持外部取消和超时取消
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeoutCts.CancelAfter(DefaultTimeoutMs);

            try
            {
                // 避免重复初始化
                if (_hardwareInitialized)
                {
                    AddLog("硬件已初始化，跳过");
                    return;
                }

                // ========== 步骤1：连接万用表 ==========
                AddLog($"正在连接万用表 {GetDmmIpAddress()} ...");
                try
                {
                    await ConnectDmmAsync(GetDmmIpAddress(), timeoutCts.Token);
                    AddLog("万用表连接成功");
                    _useSimulatedDmm = false;
                }
                catch (Exception ex)
                {
                    AddLog($"万用表连接异常: {ex.Message}");
                    throw;
                }

                // ========== 步骤2：初始化7131板卡 ==========
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
                        throw new InvalidOperationException("未找到7131板卡，无法执行真实继电器控制");
                    }
                }

                if (_jy7131Api != null)
                {
                    try
                    {
                        AddLog("正在连接7131板卡...");
                        if (!_jy7131Api.IsConnected)
                        {
                            await _jy7131Api.ConnectAsync(timeoutCts.Token);
                            AddLog("7131板卡连接成功");
                            await _jy7131Api.SetOutputModeAsync(Jy7131OutputMode.Sinking, timeoutCts.Token);
                            await _jy7131Api.StartAsync(timeoutCts.Token);
                            AddLog("7131板卡已启动");
                        }
                        else
                        {
                            AddLog("7131板卡已连接，检查运行状态...");
                            if (!_jy7131Api.IsRunning)
                            {
                                await _jy7131Api.SetOutputModeAsync(Jy7131OutputMode.Sinking, timeoutCts.Token);
                                await _jy7131Api.StartAsync(timeoutCts.Token);
                                AddLog("7131板卡已启动");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        AddLog($"7131板卡初始化异常: {ex.Message}");
                        await CleanupJy7131ApiAsync();
                        throw;
                    }
                }

                // ========== 步骤3：设置组件供电状态（下电） ==========
                // 电源阻抗测试要求：除“继电器供电/24V输出”等试验台侧供电外，组件本体处于下电状态
                AddLog("正在设置组件供电状态: 下电...");
                try
                {
                    try
                    {
                        if (_componentPowerStateApi != null)
                        {
                            await _componentPowerStateApi.ApplyComponentDownStateAsync(timeoutCts.Token);
                        }
                        else
                        {
                            throw new InvalidOperationException("组件供电API未就绪");
                        }
                    }
                    catch (Exception ex)
                    {
                        AddLog($"组件供电状态设置异常: {ex.Message}");
                        throw;
                    }
                    AddLog("组件供电状态已设置为下电");
                }
                catch (Exception ex)
                {
                    AddLog($"组件下电状态设置异常: {ex.Message}");
                    throw;
                }

                _hardwareInitialized = true;
                AddLog("硬件初始化完成");
                Application.Current?.Dispatcher?.Invoke(() => { IsPowerOn = false; PowerStatus = "已下电"; });
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !token.IsCancellationRequested)
            {
                throw new TimeoutException($"硬件初始化超时（{DefaultTimeoutMs}ms）");
            }
        }

        /// <summary>
        /// 复位所有硬件设备
        /// 
        /// 【复位顺序 - 根据API规范】
        /// 1. 复位继电器 - WriteDo(DO15, false) + SetRelay(3, false)
        /// 2. 停止7131板卡 - Stop → Disconnect
        /// 3. 断开矩阵开关通路 - 释放信号路由资源
        /// 4. 断开万用表连接 - Disconnect
        /// 
        /// 【调用时机】
        /// - 测试完成后
        /// - 测试取消/停止时
        /// - 发生异常时
        /// </summary>
        private async Task ResetHardwareAsync(CancellationToken token)
        {
            try
            {
                AddLog("正在复位硬件设备...");

                // 步骤0：确保组件处于下电状态
                try
                {
                    if (_componentPowerStateApi != null)
                    {
                        await _componentPowerStateApi.ApplyComponentDownStateAsync(token);
                    }
                    else
                    {
                        throw new InvalidOperationException("组件供电API未就绪");
                    }
                }
                catch (Exception ex)
                {
                    AddLog($"复位时组件下电状态设置异常: {ex.Message}");
                }

                // 步骤1：复位继电器，恢复产品与试验台连接
                if (IsRelayActivated)
                {
                    await DeactivateRelayWithTimeoutAsync(token);
                }

                // 步骤1.5：继电器供电断电
                await PowerOffRelaySupplyAsync(token);

                // 步骤2：停止并断开7131板卡
                // 流程：Stop → Disconnect（Dispose由using或ViewModel.Dispose处理）
                if (_jy7131Api != null && _jy7131Api.IsConnected)
                {
                    try
                    {
                        // 2.1 停止采集
                        if (_jy7131Api.IsRunning)
                        {
                            AddLog("正在停止7131板卡...");
                            await _jy7131Api.StopAsync(token);
                            AddLog("7131板卡已停止");
                        }
                        
                        // 2.2 断开连接
                        AddLog("正在断开7131板卡连接...");
                        await _jy7131Api.DisconnectAsync(token);
                        AddLog("7131板卡已断开");
                    }
                    catch (Exception ex)
                    {
                        AddLog($"7131板卡复位异常: {ex.Message}");
                    }
                }
                await CleanupJy7131ApiAsync();

                // 步骤3：断开矩阵开关通路
                await DisconnectAllMatrixRoutesAsync();

                // 步骤4：断开万用表
                await CleanupDmmSocketAsync();
                AddLog("万用表已断开");

                _hardwareInitialized = false;
                AddLog("硬件设备已复位");
            }
            catch (Exception ex)
            {
                AddLog($"硬件复位异常: {ex.Message}");
            }
        }

        private async Task CleanupJy7131ApiAsync()
        {
            try
            {
                if (_jy7131Api != null)
                {
                    try
                    {
                        if (_jy7131Api.IsRunning)
                        {
                            await _jy7131Api.StopAsync(CancellationToken.None);
                        }
                    }
                    catch (Exception ex)
                    {
                        AddLog($"7131板卡停止异常: {ex.Message}");
                    }

                    try
                    {
                        if (_jy7131Api.IsConnected)
                        {
                            await _jy7131Api.DisconnectAsync(CancellationToken.None);
                        }
                    }
                    catch (Exception ex)
                    {
                        AddLog($"7131板卡断开异常: {ex.Message}");
                    }

                    try
                    {
                        await _jy7131Api.DisposeAsync().ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        AddLog($"7131板卡释放异常: {ex.Message}");
                    }
                }
            }
            finally
            {
                _jy7131Api = null;
            }
        }

        private const string DmmIpAddress = "192.168.1.13";
        private const string MatrixIpAddress = "192.168.1.3";

        // 矩阵开关槽位：PXI-2601(2) slotindex=6（信号侧），PXI-2601(1) slotindex=4（万用表侧）
        // 各测试点通路来自矩阵对应表（6.20通道电阻采集）：
        //   A点: J3-J4  → 2601(2) 1/8  + 2601(1) 4/2（固定）
        //   B点: J14-J24→ 2601(2) 1/9  + 2601(1) 4/2（固定）
        //   C点: J3-J5  → 2601(2) 1/10 + 2601(1) 4/2（固定）
        //   D点: J14-J5 → 2601(2) 1/11 + 2601(1) 4/2（固定）
        // 万用表侧（所有测试点共用）：2601(1) 4/2 = I3, O2, slot=4
        private const int MatrixSlotSig = 6;      // 2601(2) slotindex=6，信号侧
        private const int MatrixSlotDmm = 4;      // 2601(1) slotindex=4，万用表侧

        // 万用表侧（共用，电阻采集通路固定接万用表）：2601(1) 4/2
        private static readonly (string In, string Out, int Slot) MatrixDmmH = ("I4", "O2", MatrixSlotDmm);

        // A: J3-J4 外部28VDC_POWER_IN 对 POWER_RTN — 2601(2) 1/8
        private static readonly (string In, string Out, int Slot) MatrixPointA1 = ("I1", "O8",  MatrixSlotSig);
        // B: J14-J24 POWER_ON 对 RS422_ISO_GND_3 — 2601(2) 1/9
        private static readonly (string In, string Out, int Slot) MatrixPointB1 = ("I1", "O9", MatrixSlotSig);
        // C: J3-J5 外部28VDC_POWER_IN 对 CHASSIS_GND — 2601(2) 1/10
        private static readonly (string In, string Out, int Slot) MatrixPointC1 = ("I1", "O10", MatrixSlotSig);
        // D: J14-J5 POWER_ON 对 CHASSIS_GND — 2601(2) 1/11
        private static readonly (string In, string Out, int Slot) MatrixPointD1 = ("I1", "O11", MatrixSlotSig);

        private string GetDmmIpAddress() => DmmIpAddress;

        /// <summary>
        /// 万用表连接方法隔离层。
        /// [NoInlining] 确保 NI-VISA 程序集在此方法 JIT 时才加载，
        /// 而不是在调用方 InitializeHardwareWithTimeoutAsync JIT 时加载。
        /// 这样调用方的 try-catch 可以正常捕获 FileLoadException/TypeLoadException。
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private async Task ConnectDmmAsync(string ipAddress, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(ipAddress))
                throw new InvalidOperationException("万用表IP地址为空");

            _dmmSocket ??= new DmmSocketApi();

            try
            {
                if (!_dmmSocket.IsConnected)
                    await _dmmSocket.ConnectAsync(ipAddress, token);

                await ConfigureDmmAsync(token);
            }
            catch (OperationCanceledException)
            {
                await CleanupDmmSocketAsync();
                throw;
            }
            catch (Exception ex)
            {
                AddLog($"万用表首次连接失败，准备重建连接: {ex.Message}");
                await CleanupDmmSocketAsync();

                _dmmSocket = new DmmSocketApi();
                await _dmmSocket.ConnectAsync(ipAddress, token);
                await ConfigureDmmAsync(token);
            }
        }

        private async Task ConfigureDmmAsync(CancellationToken token)
        {
            if (_dmmSocket == null)
                return;

            await _dmmSocket.SendAsync(DmmTriggerDelayCommand, token);
        }

        /// <summary>
        /// 万用表测量电阵方法隔离层。
        /// [NoInlining] 同上，防止 NI-VISA 类型在 ReadResistanceFromDmmAsync JIT 时崩溃。
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private async Task<DmmReading> DmmReadResistanceAsync(CancellationToken token)
        {
            await ConnectDmmAsync(GetDmmIpAddress(), token);

            return await _dmmSocket.ReadOnceAsync(
                DmmMeasureMode.RES,
                new DmmReadOptions { TimeoutMilliseconds = DmmTimeoutMs },
                token);
        }

        private async Task CleanupDmmSocketAsync()
        {
            try
            {
                if (_dmmSocket != null)
                {
                    try
                    {
                        if (_dmmSocket.IsConnected)
                            await _dmmSocket.DisconnectAsync(CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        AddLog($"万用表断开异常: {ex.Message}");
                    }

                    try
                    {
                        await _dmmSocket.DisposeAsync().ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        AddLog($"万用表释放异常: {ex.Message}");
                    }
                }
            }
            finally
            {
                _dmmSocket = null;
            }
        }

        private async Task DisconnectAllMatrixRoutesAsync()
        {
            var matrix = MatrixControlService.Instance;
            await Task.Delay(500);
            try { await matrix.DisconnectNodesAsync(MatrixDmmH.In,    MatrixDmmH.Out,    MatrixDmmH.Slot,    MatrixIpAddress); } catch { }
            try { await matrix.DisconnectNodesAsync(MatrixPointA1.In, MatrixPointA1.Out, MatrixPointA1.Slot, MatrixIpAddress); } catch { }
            try { await matrix.DisconnectNodesAsync(MatrixPointB1.In, MatrixPointB1.Out, MatrixPointB1.Slot, MatrixIpAddress); } catch { }
            try { await matrix.DisconnectNodesAsync(MatrixPointC1.In, MatrixPointC1.Out, MatrixPointC1.Slot, MatrixIpAddress); } catch { }
            try { await matrix.DisconnectNodesAsync(MatrixPointD1.In, MatrixPointD1.Out, MatrixPointD1.Slot, MatrixIpAddress); } catch { }
        }

        private async Task PowerOnRelaySupplyWithTimeoutAsync(CancellationToken token)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeoutCts.CancelAfter(RelayPowerTimeoutMs);

            try
            {
                // 【重要】直接操作电源开启 CH2 24V
                // 不依赖 ComponentPowerStateApi 的内部状态，确保每次都真正执行
                
                AddLog("正在开启继电器供电（24V）...");
                
                var power = new PowerSupplySocketApi();
                try
                {
                    await power.ConnectAsync("192.168.1.15", timeoutCts.Token).ConfigureAwait(false);
                    
                    // 设置 CH2 为 24V/1A 并开启输出
                    await power.ApplyAsync(PowerSupplyChannel.CH2, 24.0, 1.0, timeoutCts.Token).ConfigureAwait(false);
                    await power.SetOutputEnabledAsync(PowerSupplyChannel.CH2, true, timeoutCts.Token).ConfigureAwait(false);
                    
                    // 等待电源稳定
                    await Task.Delay(200, timeoutCts.Token).ConfigureAwait(false);
                    
                    _relaySupplyOn = true;
                    AddLog("继电器供电已上电: CH2 24V");
                }
                finally
                {
                    try { await power.DisconnectAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await power.DisposeAsync().ConfigureAwait(false); } catch { }
                }
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !token.IsCancellationRequested)
            {
                throw new TimeoutException($"继电器供电上电超时（{RelayPowerTimeoutMs}ms）");
            }
        }

        private async Task PowerOffRelaySupplyAsync(CancellationToken token)
        {
            if (!_relaySupplyOn)
                return;

            try
            {
                // 直接操作电源关闭 CH2
                var power = new PowerSupplySocketApi();
                try
                {
                    await power.ConnectAsync("192.168.1.15", token).ConfigureAwait(false);
                    await power.SetOutputEnabledAsync(PowerSupplyChannel.CH2, false, token).ConfigureAwait(false);
                    AddLog("继电器供电已关闭");
                }
                finally
                {
                    try { await power.DisconnectAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await power.DisposeAsync().ConfigureAwait(false); } catch { }
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

        /// <summary>
        /// 强制关闭加放油单板电源
        /// 
        /// 【电源阻抗测试要求】
        /// 电源阻抗测试必须在板子未上电的情况下进行测量。
        /// 点击手动或自动测试时，强制将加放油单板电源（18V、28V、32.2V）下掉。
        /// 
        /// 【硬件配置】
        /// 加放油单板电源：低压电源1 (IT-N6332B) 192.168.1.15
        ///   - CH1: 组件28V供电（实际输出28V/3A）
        ///   - CH2: 继电器24V供电（实际输出24V/1A）
        /// 
        /// 【操作流程】
        /// 1. 通过 IComponentPowerStateApi 关闭组件供电（CH1）
        /// 2. 同步更新 IBoardPowerService 状态（UI上的"组件已下电"状态）
        /// </summary>
        private void RefreshPowerStateDisplay()
        {
            var isFuelPowered = _boardPowerService != null && _boardPowerService.IsPowered &&
                                string.Equals(_boardPowerService.PoweredBoardType, "加放油单板", StringComparison.OrdinalIgnoreCase);
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                IsPowerOn = isFuelPowered;
                PowerStatus = isFuelPowered ? "已上电" : "已下电";
            });
        }

        private void OnBoardPowerStateChanged(object sender, EventArgs e)
        {
            if (!IsManualTestRunning && !IsAutoTestRunning && !IsManualTestInitializing && !IsAutoTestInitializing)
                RefreshPowerStateDisplay();
        }

        private async Task ForceComponentPowerOffAsync(CancellationToken token)
        {
            try
            {
                // 【重要】电源阻抗测试的电源控制策略：
                // 
                // 问题背景：
                // - IBoardPowerService 和 ComponentPowerStateApi 都操作 192.168.1.15
                // - 它们各自维护独立的连接和状态，容易不同步
                // - 左上角"组件上电"按钮使用 IBoardPowerService
                // - 阻抗测试使用 ComponentPowerStateApi
                // 
                // 解决方案：
                // 直接操作电源，确保 CH1 关闭，不依赖任何服务的内部状态
                
                // 直接操作电源，确保 CH1 真正关闭
                var power = new PowerSupplySocketApi();
                try
                {
                    await power.ConnectAsync("192.168.1.15", token).ConfigureAwait(false);
                    await power.SetOutputEnabledAsync(PowerSupplyChannel.CH1, false, token).ConfigureAwait(false);
                    AddLog("加放油单板电源已关闭（192.168.1.15 CH1）");
                }
                finally
                {
                    try { await power.DisconnectAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await power.DisposeAsync().ConfigureAwait(false); } catch { }
                }
                
                // 同步更新 IBoardPowerService 状态（仅更新UI状态）
                _boardPowerService?.SetPoweredState(false);

                // 更新本地状态
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    IsPowerOn = false;
                    PowerStatus = "已下电";
                });
            }
            catch (Exception ex)
            {
                AddLog($"强制下电异常: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 激活继电器（带超时保护）
        /// 
        /// 【操作流程 - 根据API规范】
        /// 1. WriteDo(DO15, true) - 单点写DO15输出高电平
        /// 2. SetRelay(3, true) - 打开485继电器第4路（参数是3，从0开始计数）
        /// 
        /// 【作用】
        /// 将产品与试验台电气隔离，确保阻抗测量的准确性
        /// </summary>
        private async Task ActivateRelayWithTimeoutAsync(CancellationToken token)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeoutCts.CancelAfter(RelayTimeoutMs);

            try
            {
                AddLog("正在激活继电器（DO15），隔离产品与试验台...");

                if (_jy7131Api != null && _jy7131Api.IsConnected)
                {
                    // 如果板卡已连接但未运行，需要先启动
                    if (!_jy7131Api.IsRunning)
                    {
                        await _jy7131Api.SetOutputModeAsync(Jy7131OutputMode.Sinking, timeoutCts.Token);
                        await _jy7131Api.StartAsync(timeoutCts.Token);
                        AddLog("7131板卡已启动");
                    }

                    // 打开485继电器第4路
                    try
                    {
                        await _jy7131Api.SetRelayAsync(3, true, _opCts.Token);
                        await Task.Delay(200);
                        AddLog("485继电器第4路已打开");
                    }
                    catch (Exception ex)
                    {
                        AddLog($"485继电器操作失败: {ex.Message}");
                    }

                    // DO15高电平 → SWITCH1 → 驱动继电器U3/E1/E2线圈得电 → NC跳NO → 产品与试验台隔离
                    AddLog("正在写DO15（高电平）...");
                    await _jy7131Api.WriteDoAsync(RelayControlChannel, true, timeoutCts.Token);
                    try
                    {
                        var mask = await _jy7131Api.ReadDoBitmaskAsync(timeoutCts.Token);
                        var ok = int.TryParse(RelayControlChannel.Substring(2), out var doIdx);
                        var bit = ok ? doIdx : 14;
                        AddLog($"DO写回读取: mask=0x{mask:X8}，{RelayControlChannel}={(mask & (1u << bit)) != 0}");
                    }
                    catch (Exception ex)
                    {
                        AddLog($"DO写回读取失败: {ex.Message}");
                    }
                    AddLog("DO14输出完成，继电器线圈得电");
                }
                else
                {
                    throw new InvalidOperationException("7131板卡不可用，无法执行继电器激活");
                }

                // 等待继电器动作完成
                await Task.Delay(500, timeoutCts.Token);

                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    IsRelayActivated = true;
                    RelayStatus = "已激活";
                });

                AddLog("继电器已激活，产品与试验台已隔离");
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !token.IsCancellationRequested)
            {
                throw new TimeoutException($"激活继电器超时（{RelayTimeoutMs}ms）");
            }
        }

        /// <summary>
        /// 复位继电器（带超时保护）
        /// 
        /// 【操作流程 - 根据API规范】
        /// 1. WriteDo(DO15, false) - 单点写DO15输出低电平
        /// 2. SetRelay(3, false) - 关闭485继电器第4路
        /// 
        /// 【作用】
        /// 恢复产品与试验台的连接
        /// </summary>
        private async Task DeactivateRelayWithTimeoutAsync(CancellationToken token)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeoutCts.CancelAfter(RelayTimeoutMs);

            try
            {
                AddLog("正在复位继电器（DO15）...");

                if (_jy7131Api != null && _jy7131Api.IsConnected)
                {
                    // 某些情况下Stop后仍保持IsConnected，此时写DO可能无效；确保DO任务已Start
                    if (!_jy7131Api.IsRunning)
                    {
                        await _jy7131Api.SetOutputModeAsync(Jy7131OutputMode.Sinking, timeoutCts.Token);
                        await _jy7131Api.StartAsync(timeoutCts.Token);
                        AddLog("7131板卡已启动");
                    }

                    // DO15低电平 → 继电器线圈失电 → 触点恢复NC → 产品与试验台恢复连接
                    AddLog("正在写DO15（低电平）...");
                    await _jy7131Api.WriteDoAsync(RelayControlChannel, false, timeoutCts.Token);

                    // 关闭485继电器第4路
                    try
                    {
                        await _jy7131Api.SetRelayAsync(3, false, _opCts.Token);
                        await Task.Delay(200);
                        AddLog("485继电器第4路已关闭");
                    }
                    catch (Exception ex)
                    {
                        AddLog($"485继电器操作失败: {ex.Message}");
                    }

                    try
                    {
                        var mask = await _jy7131Api.ReadDoBitmaskAsync(timeoutCts.Token);
                        var ok = int.TryParse(RelayControlChannel.Substring(2), out var doIdx);
                        var bit = ok ? doIdx : 14;
                        AddLog($"DO写回读取: mask=0x{mask:X8}，{RelayControlChannel}={(mask & (1u << bit)) != 0}");
                    }
                    catch (Exception ex)
                    {
                        AddLog($"DO写回读取失败: {ex.Message}");
                    }
                    AddLog("DO14输出完成，继电器线圈失电");
                }
                else
                {
                    throw new InvalidOperationException("7131板卡不可用，无法执行继电器复位");
                }

                // 等待继电器动作完成
                await Task.Delay(500, timeoutCts.Token);

                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    IsRelayActivated = false;
                    RelayStatus = "未激活";
                });

                AddLog("继电器已复位");
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !token.IsCancellationRequested)
            {
                throw new TimeoutException($"复位继电器超时（{RelayTimeoutMs}ms）");
            }
        }

        private async Task MeasureImpedanceWithTimeoutAsync(string point, CancellationToken token)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeoutCts.CancelAfter(DmmTimeoutMs);

            try
            {
                await MeasureImpedanceAsync(point, timeoutCts.Token);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !token.IsCancellationRequested)
            {
                throw new TimeoutException($"测量阻抗超时（{DmmTimeoutMs}ms）");
            }
        }

        private async Task MeasureSinglePointAsync(string point)
        {
            try
            {
                using var cts = new CancellationTokenSource(DmmTimeoutMs);
                await MeasureImpedanceAsync(point, cts.Token);
            }
            catch (OperationCanceledException)
            {
                string pointName = point switch
                {
                    "A" => "J3-J4",
                    "B" => "J14-J24",
                    "C" => "J3-J5",
                    "D" => "J14-J5",
                    _ => point
                };
                AddLog($"测量 {pointName} 超时");
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    ReMessageBox.Show($"测量 {pointName} 阻抗超时（{DmmTimeoutMs}ms）", "超时提示",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                });
            }
            catch (TimeoutException)
            {
                string pointName = point switch
                {
                    "A" => "J3-J4",
                    "B" => "J14-J24",
                    "C" => "J3-J5",
                    "D" => "J14-J5",
                    _ => point
                };
                AddLog($"测量 {pointName} 超时");
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    ReMessageBox.Show($"测量 {pointName} 阻抗超时（{DmmTimeoutMs}ms）", "超时提示",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                });
            }
            catch (Exception ex)
            {
                AddLog($"测量失败: {ex.Message}");
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    ReMessageBox.Show($"测量阻抗失败: {ex.Message}", "错误",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
        }

        private void ClearResults()
        {
            ImpedanceA = null;
            ImpedanceB = null;
            ImpedanceC = null;
            ImpedanceD = null;
            ResultA = "--";
            ResultB = "--";
            ResultC = "--"; 
            ResultD = "--";
            OverallResult = "--";
            LastTestTime = "--";
        }

        private async Task MeasureImpedanceAsync(string point, CancellationToken token = default)
        {
            IsBusy = true;
            try
            {
                string pointName = point switch
                {
                    "A" => "J3-J4（外部28V对地）",
                    "B" => "J14-J24（内部28对地）",
                    "C" => "J3-J5（外部28V对壳体）",
                    "D" => "J14-J5（内部28对壳体）",
                    _ => point
                };

                AddLog($"正在测量 {pointName} 阻抗...");

                double impedance = await ReadResistanceFromDmmAsync(point, token);
                string result = impedance > ImpedanceThreshold ? "PASS" : "FAIL";

                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    switch (point)
                    {
                        case "A":
                            ImpedanceA = impedance;
                            ResultA = result;
                            break;
                        case "B":
                            ImpedanceB = impedance;
                            ResultB = result;
                            break;
                        case "C":
                            ImpedanceC = impedance;
                            ResultC = result;
                            break;
                        case "D":
                            ImpedanceD = impedance;
                            ResultD = result;
                            break;
                    }
                });

                AddLog($"{pointName} 阻抗: {impedance:F1}Ω, 结果: {result}");

                if (IsManualTestRunning)
                {
                    EvaluateOverallResult();
                    if (ResultA != "--" && ResultB != "--" && ResultC != "--" && ResultD != "--")
                    {
                        LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                        AddLog($"所有测试点测量完成，综合结果: {OverallResult}");
                    }
                }
            }
            catch (Exception ex)
            {
                AddLog($"测量失败: {ex.Message}");
                throw;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void EvaluateOverallResult()
        {
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                if (ResultA == "PASS" && ResultB == "PASS" && ResultC == "PASS" && ResultD == "PASS")
                {
                    OverallResult = "PASS";
                }
                else if (ResultA == "FAIL" || ResultB == "FAIL" || ResultC == "FAIL" || ResultD == "FAIL")
                {
                    OverallResult = "FAIL";
                }
                else
                {
                    OverallResult = "--";
                }
            });
        }

        private async Task<double> ReadResistanceFromDmmAsync(string point, CancellationToken token = default)
        {
            if (_useSimulatedDmm)
            {
                throw new InvalidOperationException("万用表未就绪，无法执行真实阻抗测量");
            }

            await _measureLock.WaitAsync(token);
            try
            {
                var matrix = MatrixControlService.Instance;

                // 根据测试点配置对应矩阵通路
                (string In, string Out, int Slot) c1;
                (string In, string Out, int Slot)? c2;
                switch (point)
                {
                    case "A": c1 = MatrixPointA1; c2 = null; break;
                    case "B": c1 = MatrixPointB1; c2 = null; break;
                    case "C": c1 = MatrixPointC1; c2 = null; break;
                    case "D": c1 = MatrixPointD1; c2 = null; break;
                    default:  c1 = MatrixPointA1; c2 = null; break;
                }

                var okDmm = await matrix.ConnectNodesAsync(MatrixDmmH.In, MatrixDmmH.Out, MatrixDmmH.Slot, MatrixIpAddress);
                var ok1   = await matrix.ConnectNodesAsync(c1.In, c1.Out, c1.Slot, MatrixIpAddress);
                await Task.Delay(300);
                var ok2   = c2.HasValue ? await matrix.ConnectNodesAsync(c2.Value.In, c2.Value.Out, c2.Value.Slot, MatrixIpAddress) : true;
                AddLog(
                    c2.HasValue
                        ? $"矩阵连接 {(okDmm && ok1 && ok2 ? "OK" : "FAIL")} - DMM:{MatrixDmmH.In}-{MatrixDmmH.Out}(slot{MatrixDmmH.Slot}), {c1.In}-{c1.Out}(slot{c1.Slot}), {c2.Value.In}-{c2.Value.Out}(slot{c2.Value.Slot})"
                        : $"矩阵连接 {(okDmm && ok1 ? "OK" : "FAIL")} - DMM:{MatrixDmmH.In}-{MatrixDmmH.Out}(slot{MatrixDmmH.Slot}), {c1.In}-{c1.Out}(slot{c1.Slot})");

                if (!ok1 || !ok2)
                {
                    throw new InvalidOperationException("矩阵通路连接失败，无法执行真实阻抗测量");
                }

                try
                {
                    var reading = await DmmReadResistanceAsync(token);

                    if (reading?.IsOverrange == true)
                        return double.MaxValue;

                    if (reading?.Value != null)
                        return reading.Value.Value;

                    throw new InvalidOperationException($"万用表读数无效: {reading?.Raw}");
                }
                catch (Exception ex)
                {
                    AddLog($"万用表测量异常: {ex.Message}");
                    throw;
                }
            }
            finally
            {
                try { await DisconnectAllMatrixRoutesAsync(); } catch { }
                _measureLock.Release();
            }
        }

        private void AddLog(string message)
        {
            var logEntry = $"[{DateTime.Now:HH:mm:ss}] {message}";
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                Logs.Add(logEntry);
                while (Logs.Count > 500)
                    Logs.RemoveAt(0);
            });
        }

        private void UpdateCommandStates()
        {
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                ToggleRelayCommand?.RaiseCanExecuteChanged();
                MeasureACommand?.RaiseCanExecuteChanged();
                MeasureBCommand?.RaiseCanExecuteChanged();
                MeasureCCommand?.RaiseCanExecuteChanged();
                MeasureDCommand?.RaiseCanExecuteChanged();
            });
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

                LastTestTime = Read("LastTestTime") ?? "--";
                OverallResult = Read("OverallResult") ?? "--";

                RaisePropertyChanged(nameof(LastTestTime));
                RaisePropertyChanged(nameof(OverallResult));
            }
            catch
            {
            }
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
            }
            catch
            {
            }
        }

        #endregion

        #region 7131板卡查找辅助方法

        /// <summary>
        /// 从 PXI 机箱中查找第一个 PXIe-7131 板卡
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
                    d is DigitalIODevice ||
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
                        c is DigitalIODevice ||
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

        public void Dispose()
        {
            _opCts?.Cancel();
            _opCts?.Dispose();

            try
            {
                PowerOffRelaySupplyAsync(CancellationToken.None).GetAwaiter().GetResult();
            }
            catch { }

            try
            {
                if (IsRelayActivated && _jy7131Api != null)
                {
                    _jy7131Api.WriteDoAsync(RelayControlChannel, false).GetAwaiter().GetResult();
                }
            }
            catch { }

            try
            {
                DisconnectAllMatrixRoutesAsync().GetAwaiter().GetResult();
            }
            catch { }

            try
            {
                CleanupDmmSocketAsync().GetAwaiter().GetResult();
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

            _measureLock?.Dispose();
            _simulation?.Dispose();

            if (_projectSavingToken != null)
            {
                _eventAggregator?.GetEvent<ProjectSavingEvent>()?.Unsubscribe(_projectSavingToken);
            }
        }
    }
}
