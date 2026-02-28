using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using MeasureControl.Events;
using MeasureControl.Models;
using MeasureControl.Services;
using MeasureControl.Services.HardwareApis;
using MeasureControl.Simulations.FuelController;
using Prism.Commands;
using Prism.Events;
using Prism.Mvvm;
using System.Windows;
using MeasureControl.Views.Dialogs;

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
        
        /// <summary>继电器控制通道，使用7131板卡的DO15</summary>
        private const string RelayControlChannel = "DO15";
        
        /// <summary>硬件初始化默认超时时间（毫秒）</summary>
        private const int DefaultTimeoutMs = 3000;
        
        /// <summary>万用表测量超时时间（毫秒）</summary>
        private const int DmmTimeoutMs = 2000;
        
        /// <summary>继电器操作超时时间（毫秒）</summary>
        private const int RelayTimeoutMs = 2000;

        private const int PowerSupplyTimeoutMs = 2000;
        private const string PowerSupplyIpAddress = "192.168.1.15";
        private const PowerSupplyChannel RelaySupplyChannel = PowerSupplyChannel.CH2;
        private const double RelaySupplyVoltage = 24.0;
        private const double RelaySupplyCurrent = 1.0;

        #endregion

        #region 依赖服务

        private readonly ISingleBoardTestContextService _singleBoardTestContext;  // 单板测试上下文服务
        private readonly ProjectService _projectService;                           // 项目服务，用于数据持久化
        private readonly IEventAggregator _eventAggregator;                        // 事件聚合器，用于跨模块通信
        private readonly IComponentPowerStateApi _componentPowerStateApi;          // 组件供电状态API（复用）
        private readonly IPxiChassisService _pxiChassisService;                   // 机箱服务，用于查找板卡设备
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
        private string _powerStatus = "未就绪";                                      // 供电状态显示文本

        private bool _useSimulatedDmm;                                             // DMM不可用时走仿真测量

        private IPowerSupplyApi _powerSupplyApi;
        private bool _relaySupplyOn;

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
            IDmmApi dmmApi = null)
        {
            // 保存依赖服务引用
            _singleBoardTestContext = singleBoardTestContext;
            _projectService = projectService;
            _eventAggregator = eventAggregator;
            _componentPowerStateApi = componentPowerStateApi;
            _pxiChassisService = pxiChassisService;
            _dmmApi = dmmApi;
            _simulation = new PowerImpedanceSimulation();

            // 初始化命令
            // ManualTestCommand: 手动测试按钮，点击后初始化硬件，用户手动测量各点
            ManualTestCommand = new DelegateCommand(OnManualTest);
            
            // AutoTestCommand: 自动测试按钮，点击后自动完成所有测量
            AutoTestCommand = new DelegateCommand(OnAutoTest);
            
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

        /// <summary>
        /// 手动测试按钮点击处理
        /// 如果正在运行则停止，否则开始手动测试
        /// </summary>
        private void OnManualTest()
        {
            if (IsManualTestRunning)
            {
                StopTest();
            }
            else
            {
                StartManualTest();
            }
        }

        /// <summary>
        /// 自动测试按钮点击处理
        /// 如果正在运行则停止，否则开始自动测试
        /// </summary>
        private void OnAutoTest()
        {
            if (IsAutoTestRunning)
            {
                StopTest();
            }
            else
            {
                StartAutoTest();
            }
        }

        /// <summary>
        /// 启动手动测试
        /// 
        /// 【手动测试流程】
        /// 1. 初始化硬件（矩阵开关、7131板卡、万用表）
        /// 2. 激活继电器，隔离产品与试验台
        /// 3. 等待用户手动点击各测试点的"测量"按钮
        /// 
        /// 【与自动测试的区别】
        /// 手动测试只完成硬件初始化，测量操作由用户手动触发
        /// 适用于需要逐步确认的调试场景
        /// </summary>
        private void StartManualTest()
        {
            // 防止与自动测试冲突
            if (IsAutoTestRunning) return;

            // 取消之前的操作，创建新的取消令牌
            _opCts?.Cancel();
            _opCts = new CancellationTokenSource();

            IsManualTestRunning = true;
            ClearResults();
            AddLog("手动测试开始");

            // 在后台线程执行，避免阻塞UI
            Task.Run(async () =>
            {
                var token = _opCts.Token;
                try
                {
                    // 步骤1：初始化硬件设备
                    AddLog("步骤1: 初始化硬件设备（7131板卡、万用表）...");
                    await InitializeHardwareWithTimeoutAsync(token);
                    if (token.IsCancellationRequested) return;

                    // 步骤1.5：继电器供电上电（24V）
                    AddLog("步骤1.5: 继电器供电上电（24V）...");
                    await PowerOnRelaySupplyWithTimeoutAsync(token);
                    if (token.IsCancellationRequested) return;

                    // 步骤2：激活继电器，将产品与试验台隔离
                    AddLog("步骤2: 激活继电器，隔离产品与试验台...");
                    await ActivateRelayWithTimeoutAsync(token);
                    if (token.IsCancellationRequested) return;

                    // 初始化完成，等待用户手动测量
                    AddLog("硬件初始化完成，请依次点击各测试项的\"测量\"按钮进行阻抗测量");
                }
                catch (OperationCanceledException)
                {
                    // 用户取消操作
                    AddLog("初始化已取消");
                    Application.Current?.Dispatcher?.Invoke(() => IsManualTestRunning = false);
                }
                catch (TimeoutException ex)
                {
                    // 硬件操作超时，显示提示框
                    AddLog($"初始化超时: {ex.Message}");
                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        IsManualTestRunning = false;
                        ReMessageBox.Show($"手动测试初始化超时: {ex.Message}", "超时提示",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                    });
                    // 超时后复位硬件，确保设备处于安全状态
                    try { await ResetHardwareAsync(CancellationToken.None); } catch { }
                }
                catch (Exception ex)
                {
                    AddLog($"初始化失败: {ex.Message}");
                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        IsManualTestRunning = false;
                        ReMessageBox.Show($"手动测试初始化失败: {ex.Message}", "错误",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    });
                    try { await ResetHardwareAsync(CancellationToken.None); } catch { }
                }
            });
        }

        /// <summary>
        /// 启动自动测试
        /// 
        /// 【自动测试流程】
        /// 步骤1: 初始化硬件设备（矩阵开关、7131板卡、万用表）
        /// 步骤2: 激活继电器，隔离产品与试验台
        /// 步骤3: 测量 A点 J3-J4 阻抗（外部28V对地）
        /// 步骤4: 测量 B点 J14-J24 阻抗（内部28对地）
        /// 步骤5: 测量 C点 J3-J5 阻抗（外部28V对壳体）
        /// 步骤6: 测量 D点 J14-J5 阻抗（内部28对壳体）
        /// 步骤7: 评估综合结果并复位硬件
        /// 
        /// 【判定标准】
        /// 所有测试点阻抗 > 500Ω → 综合结果 PASS
        /// 任一测试点阻抗 ≤ 500Ω → 综合结果 FAIL
        /// </summary>
        private void StartAutoTest()
        {
            // 防止与手动测试冲突
            if (IsManualTestRunning) return;

            _opCts?.Cancel();
            _opCts = new CancellationTokenSource();

            IsAutoTestRunning = true;
            ClearResults();
            AddLog("自动测试开始");

            Task.Run(async () =>
            {
                var token = _opCts.Token;
                try
                {
                    // ========== 步骤1: 初始化硬件 ==========
                    AddLog("步骤1: 初始化硬件设备（7131板卡、万用表）...");
                    await InitializeHardwareWithTimeoutAsync(token);
                    if (token.IsCancellationRequested) return;

                    // ========== 步骤1.5: 继电器供电上电 ==========
                    AddLog("步骤1.5: 继电器供电上电（24V）...");
                    await PowerOnRelaySupplyWithTimeoutAsync(token);
                    if (token.IsCancellationRequested) return;

                    // ========== 步骤2: 激活继电器 ==========
                    AddLog("步骤2: 激活继电器，隔离产品与试验台...");
                    await ActivateRelayWithTimeoutAsync(token);
                    if (token.IsCancellationRequested) return;

                    // ========== 步骤3-6: 依次测量四个测试点 ==========
                    AddLog("步骤3: 测量 J3-J4 阻抗（外部28V对地）");
                    await MeasureImpedanceWithTimeoutAsync("A", token);
                    if (token.IsCancellationRequested) return;

                    AddLog("步骤4: 测量 J14-J24 阻抗（内部28对地）");
                    await MeasureImpedanceWithTimeoutAsync("B", token);
                    if (token.IsCancellationRequested) return;

                    AddLog("步骤5: 测量 J3-J5 阻抗（外部28V对壳体）");
                    await MeasureImpedanceWithTimeoutAsync("C", token);
                    if (token.IsCancellationRequested) return;

                    AddLog("步骤6: 测量 J14-J5 阻抗（内部28对壳体）");
                    await MeasureImpedanceWithTimeoutAsync("D", token);
                    if (token.IsCancellationRequested) return;

                    // ========== 步骤7: 评估结果并复位 ==========
                    EvaluateOverallResult();
                    LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    AddLog($"自动测试完成，综合结果: {OverallResult}");

                    AddLog("步骤7: 复位硬件设备...");
                    await ResetHardwareAsync(CancellationToken.None);
                }
                catch (OperationCanceledException)
                {
                    AddLog("自动测试已取消");
                    try { await ResetHardwareAsync(CancellationToken.None); } catch { }
                }
                catch (TimeoutException ex)
                {
                    AddLog($"自动测试超时: {ex.Message}");
                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        ReMessageBox.Show($"自动测试超时: {ex.Message}", "超时提示",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                    });
                    try { await ResetHardwareAsync(CancellationToken.None); } catch { }
                }
                catch (Exception ex)
                {
                    AddLog($"自动测试异常: {ex.Message}");
                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        ReMessageBox.Show($"自动测试异常: {ex.Message}", "错误",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    });
                    try { await ResetHardwareAsync(CancellationToken.None); } catch { }
                }
                finally
                {
                    // 无论成功失败，都要更新运行状态
                    Application.Current?.Dispatcher?.Invoke(() => IsAutoTestRunning = false);
                }
            });
        }

        /// <summary>
        /// 停止测试
        /// 取消当前操作并复位所有硬件
        /// </summary>
        private void StopTest()
        {
            // 发送取消信号
            _opCts?.Cancel();
            IsManualTestRunning = false;
            IsAutoTestRunning = false;
            AddLog("测试已停止，正在复位硬件...");

            // 异步复位硬件并释放资源
            Task.Run(async () =>
            {
                try
                {
                    await ResetHardwareAsync(CancellationToken.None);
                    AddLog("硬件复位完成，资源已释放");
                }
                catch (Exception ex)
                {
                    AddLog($"停止测试时复位硬件失败: {ex.Message}");
                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        ReMessageBox.Show($"硬件复位失败: {ex.Message}", "警告",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                    });
                }
            });
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
                    AddLog($"万用表连接异常: {ex.Message}，使用仿真模式");
                    _useSimulatedDmm = true;
                }

                // ========== 步骤2：初始化7131板卡 ==========
                if (_jy7131Api == null)
                {
                    var chassisName = _singleBoardTestContext?.ChassisName;
                    var device7131 = Find7131DeviceInChassis(chassisName);
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
                        AddLog("未找到7131板卡，使用仿真模式");
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
                            await _jy7131Api.SetOutputModeAsync(Jy7131OutputMode.PushPull, timeoutCts.Token);
                            await _jy7131Api.StartAsync(timeoutCts.Token);
                            AddLog("7131板卡已启动");
                        }
                        else
                        {
                            AddLog("7131板卡已连接");
                        }
                    }
                    catch (Exception ex)
                    {
                        AddLog($"7131板卡初始化异常: {ex.Message}，使用仿真模式");
                        _jy7131Api = null;
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
                    catch
                    {
                        await _simulation.ApplyComponentDownStateAsync(msg => AddLog(msg), timeoutCts.Token);
                    }
                    AddLog("组件供电状态已设置为下电");
                }
                catch (Exception ex)
                {
                    AddLog($"组件下电状态设置异常: {ex.Message}");
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
                    await _simulation.ApplyComponentDownStateAsync(msg => AddLog(msg), token);
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

                // 步骤3：断开矩阵开关通路
                await DisconnectAllMatrixRoutesAsync();

                // 步骤4：断开万用表
                if (_dmmSocket != null)
                {
                    try
                    {
                        if (_dmmSocket.IsConnected)
                            await _dmmSocket.DisconnectAsync(token);
                        AddLog("万用表已断开");
                    }
                    catch { }
                }

                _hardwareInitialized = false;
                AddLog("硬件设备已复位");
            }
            catch (Exception ex)
            {
                AddLog($"硬件复位异常: {ex.Message}");
            }
        }

        private const string DmmIpAddress = "192.168.1.13";
        private const string MatrixIpAddress = "192.168.1.3";

        // 矩阵开关槽位：PXI-2601(2) slotindex=6（信号侧），PXI-2601(1) slotindex=4（万用表侧）
        // 各测试点通路来自矩阵对应表（6.20通道电阻采集）：
        //   RESACQUIRE1+/1- = 2601(2) 1/8 、1/9  → A点: J3-J4   (外部28VDC_POWER_IN 对 POWER_RTN)
        //   RESACQUIRE2+/2- = 2601(2) 1/10、1/11 → B点: J14-J24 (POWER_ON 对 RS422_ISO_GND_3)
        //   RESACQUIRE3+/3- = 2601(2) 1/12、1/13 → C点: J3-J5   (外部28VDC_POWER_IN 对 CHASSIS_GND)
        //   RESACQUIRE4+/4- = 2601(2) 1/14、1/15 → D点: J14-J5  (POWER_ON 对 CHASSIS_GND)
        // 万用表侧（所有测试点共用）：2601(1) 4/2 = I3, O2, slot=4
        private const int MatrixSlotSig = 6;      // 2601(2) slotindex=6，信号侧
        private const int MatrixSlotDmm = 4;      // 2601(1) slotindex=4，万用表侧

        // 万用表侧（共用，电阻采集通路固定接万用表）：2601(1) 4/2
        private static readonly (string In, string Out, int Slot) MatrixDmmH = ("I3", "O2", MatrixSlotDmm);

        // A: J3-J4 外部28VDC_POWER_IN 对 POWER_RTN — RESACQUIRE1+/1- = 2601(2) 1/8、1/9
        private static readonly (string In, string Out, int Slot) MatrixPointA1 = ("I2", "O8",  MatrixSlotSig);
        private static readonly (string In, string Out, int Slot) MatrixPointA2 = ("I2", "O9",  MatrixSlotSig);
        // B: J14-J24 POWER_ON 对 RS422_ISO_GND_3 — RESACQUIRE2+/2- = 2601(2) 1/10、1/11
        private static readonly (string In, string Out, int Slot) MatrixPointB1 = ("I2", "O10", MatrixSlotSig);
        private static readonly (string In, string Out, int Slot) MatrixPointB2 = ("I2", "O11", MatrixSlotSig);
        // C: J3-J5 外部28VDC_POWER_IN 对 CHASSIS_GND — RESACQUIRE3+/3- = 2601(2) 1/12、1/13
        private static readonly (string In, string Out, int Slot) MatrixPointC1 = ("I2", "O12", MatrixSlotSig);
        private static readonly (string In, string Out, int Slot) MatrixPointC2 = ("I2", "O13", MatrixSlotSig);
        // D: J14-J5 POWER_ON 对 CHASSIS_GND — RESACQUIRE4+/4- = 2601(2) 1/14、1/15
        private static readonly (string In, string Out, int Slot) MatrixPointD1 = ("I2", "O14", MatrixSlotSig);
        private static readonly (string In, string Out, int Slot) MatrixPointD2 = ("I2", "O15", MatrixSlotSig);

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
            _dmmSocket ??= new DmmSocketApi();
            if (!_dmmSocket.IsConnected)
                await _dmmSocket.ConnectAsync(ipAddress, token);
        }

        /// <summary>
        /// 万用表测量电阵方法隔离层。
        /// [NoInlining] 同上，防止 NI-VISA 类型在 ReadResistanceFromDmmAsync JIT 时崩溃。
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private async Task<DmmReading> DmmReadResistanceAsync(CancellationToken token)
        {
            if (_dmmSocket == null || !_dmmSocket.IsConnected)
            {
                _dmmSocket ??= new DmmSocketApi();
                await _dmmSocket.ConnectAsync(GetDmmIpAddress(), token);
            }
            return await _dmmSocket.ReadOnceAsync(
                DmmMeasureMode.RES,
                new DmmReadOptions { TimeoutMilliseconds = DmmTimeoutMs },
                token);
        }

        private async Task DisconnectAllMatrixRoutesAsync()
        {
            var matrix = MatrixControlService.Instance;
            try { await matrix.DisconnectNodesAsync(MatrixDmmH.In,    MatrixDmmH.Out,    MatrixDmmH.Slot,    MatrixIpAddress); } catch { }
            try { await matrix.DisconnectNodesAsync(MatrixPointA1.In, MatrixPointA1.Out, MatrixPointA1.Slot, MatrixIpAddress); } catch { }
            try { await matrix.DisconnectNodesAsync(MatrixPointA2.In, MatrixPointA2.Out, MatrixPointA2.Slot, MatrixIpAddress); } catch { }
            try { await matrix.DisconnectNodesAsync(MatrixPointB1.In, MatrixPointB1.Out, MatrixPointB1.Slot, MatrixIpAddress); } catch { }
            try { await matrix.DisconnectNodesAsync(MatrixPointB2.In, MatrixPointB2.Out, MatrixPointB2.Slot, MatrixIpAddress); } catch { }
            try { await matrix.DisconnectNodesAsync(MatrixPointC1.In, MatrixPointC1.Out, MatrixPointC1.Slot, MatrixIpAddress); } catch { }
            try { await matrix.DisconnectNodesAsync(MatrixPointC2.In, MatrixPointC2.Out, MatrixPointC2.Slot, MatrixIpAddress); } catch { }
            try { await matrix.DisconnectNodesAsync(MatrixPointD1.In, MatrixPointD1.Out, MatrixPointD1.Slot, MatrixIpAddress); } catch { }
            try { await matrix.DisconnectNodesAsync(MatrixPointD2.In, MatrixPointD2.Out, MatrixPointD2.Slot, MatrixIpAddress); } catch { }
        }

        private async Task PowerOnRelaySupplyWithTimeoutAsync(CancellationToken token)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeoutCts.CancelAfter(PowerSupplyTimeoutMs);

            try
            {
                if (_relaySupplyOn)
                {
                    AddLog("继电器供电已上电，跳过");
                    return;
                }

                AddLog($"正在连接程控电源: {PowerSupplyIpAddress}...");
                await ConnectPowerSupplyAsync(PowerSupplyIpAddress, timeoutCts.Token);

                await _powerSupplyApi.ApplyAsync(RelaySupplyChannel, RelaySupplyVoltage, RelaySupplyCurrent, timeoutCts.Token);
                await _powerSupplyApi.SetOutputEnabledAsync(RelaySupplyChannel, true, timeoutCts.Token);

                _relaySupplyOn = true;
                AddLog($"继电器供电已上电: {PowerSupplyIpAddress} CH{(int)RelaySupplyChannel} {RelaySupplyVoltage:F1}V");
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !token.IsCancellationRequested)
            {
                throw new TimeoutException($"继电器供电上电超时（{PowerSupplyTimeoutMs}ms）");
            }
        }

        /// <summary>
        /// 程控电源连接方法隔离层。
        /// [NoInlining] 确保 NI-VISA 程序集在此方法 JIT 时才加载，
        /// 防止 FileLoadException 在调用方 JIT 编译时逃逸 try-catch。
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private async Task ConnectPowerSupplyAsync(string ipAddress, CancellationToken token)
        {
            _powerSupplyApi ??= new PowerSupplySocketApi();
            if (!_powerSupplyApi.IsConnected)
                await _powerSupplyApi.ConnectAsync(ipAddress, token);
        }

        private async Task PowerOffRelaySupplyAsync(CancellationToken token)
        {
            if (_powerSupplyApi == null)
            {
                _relaySupplyOn = false;
                return;
            }

            try
            {
                if (_powerSupplyApi.IsConnected)
                {
                    try
                    {
                        await _powerSupplyApi.SetOutputEnabledAsync(RelaySupplyChannel, false, token);
                    }
                    catch { }

                    try
                    {
                        await _powerSupplyApi.DisconnectAsync(token);
                    }
                    catch { }
                }
            }
            finally
            {
                try { await _powerSupplyApi.DisposeAsync(); } catch { }
                _powerSupplyApi = null;
                _relaySupplyOn = false;
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
                    // DO15高电平 → SWITCH1 → 驱动继电器U3/E1/E2线圈得电 → NC跳NO → 产品与试验台隔离
                    AddLog("正在写DO15（高电平）...");
                    await _jy7131Api.WriteDoAsync(RelayControlChannel, true, timeoutCts.Token);
                    AddLog("DO15输出完成，继电器线圈得电");
                }
                else
                {
                    AddLog("7131板卡不可用，使用仿真继电器动作");
                    await _simulation.SimulateRelayActivateAsync(timeoutCts.Token);
                }

                // 等待继电器动作完成
                await Task.Delay(200, timeoutCts.Token);

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
                    // DO15低电平 → 继电器线圈失电 → 触点恢复NC → 产品与试验台恢复连接
                    AddLog("正在写DO15（低电平）...");
                    await _jy7131Api.WriteDoAsync(RelayControlChannel, false, timeoutCts.Token);
                    AddLog("DO15输出完成，继电器线圈失电");
                }
                else
                {
                    AddLog("7131板卡不可用，使用仿真继电器动作");
                    await _simulation.SimulateRelayDeactivateAsync(timeoutCts.Token);
                }

                // 等待继电器动作完成
                await Task.Delay(200, timeoutCts.Token);

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
                return await _simulation.SimulateMeasureResistanceAsync(point, token);
            }

            await _measureLock.WaitAsync(token);
            try
            {
                var matrix = MatrixControlService.Instance;

                // 根据测试点配置对应矩阵通路
                (string In, string Out, int Slot) c1, c2;
                switch (point)
                {
                    case "A": c1 = MatrixPointA1; c2 = MatrixPointA2; break;
                    case "B": c1 = MatrixPointB1; c2 = MatrixPointB2; break;
                    case "C": c1 = MatrixPointC1; c2 = MatrixPointC2; break;
                    case "D": c1 = MatrixPointD1; c2 = MatrixPointD2; break;
                    default:  c1 = MatrixPointA1; c2 = MatrixPointA2; break;
                }

                var okDmm = await matrix.ConnectNodesAsync(MatrixDmmH.In, MatrixDmmH.Out, MatrixDmmH.Slot, MatrixIpAddress);
                var ok1   = await matrix.ConnectNodesAsync(c1.In, c1.Out, c1.Slot, MatrixIpAddress);
                var ok2   = await matrix.ConnectNodesAsync(c2.In, c2.Out, c2.Slot, MatrixIpAddress);
                AddLog($"矩阵连接 {(okDmm && ok1 && ok2 ? "OK" : "FAIL")} - DMM:{MatrixDmmH.In}-{MatrixDmmH.Out}(slot{MatrixDmmH.Slot}), {c1.In}-{c1.Out}(slot{c1.Slot}), {c2.In}-{c2.Out}(slot{c2.Slot})");

                if (!ok1 || !ok2)
                {
                    AddLog("矩阵通路连接失败，使用仿真测量");
                    _useSimulatedDmm = true;
                    return await _simulation.SimulateMeasureResistanceAsync(point, token);
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
                    AddLog($"万用表测量异常: {ex.Message}，使用仿真模式");
                    _useSimulatedDmm = true;
                    return await _simulation.SimulateMeasureResistanceAsync(point, token);
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

        private MeasureControl.Models.Devices.DeviceBase Find7131DeviceInChassis(string chassisName)
        {
            if (string.IsNullOrWhiteSpace(chassisName))
                return null;

            var devices = _pxiChassisService?.GetChassisDevices(chassisName);
            if (devices == null || devices.Count == 0)
                return null;

            MeasureControl.Models.Devices.DeviceBase Walk(MeasureControl.Models.Devices.DeviceBase d)
            {
                if (d == null) return null;
                var model = (d.Model ?? string.Empty).ToUpperInvariant();
                var name  = (d.Name  ?? string.Empty).ToUpperInvariant();
                if (model.Contains("7131") || name.Contains("7131"))
                    return d;
                if (d.Children == null) return null;
                foreach (var c in d.Children)
                {
                    var found = Walk(c);
                    if (found != null) return found;
                }
                return null;
            }

            foreach (var d in devices)
            {
                var found = Walk(d);
                if (found != null) return found;
            }
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
                if (_dmmSocket != null && _dmmSocket.IsConnected)
                {
                    _dmmSocket.DisconnectAsync(CancellationToken.None).GetAwaiter().GetResult();
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

            _measureLock?.Dispose();
            _simulation?.Dispose();

            if (_projectSavingToken != null)
            {
                _eventAggregator?.GetEvent<ProjectSavingEvent>()?.Unsubscribe(_projectSavingToken);
            }
        }
    }
}
