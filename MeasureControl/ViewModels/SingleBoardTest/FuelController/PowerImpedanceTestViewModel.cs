using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
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
using Prism.Mvvm;
using System.Windows;
using Ivi.Visa;
using NationalInstruments.Visa;
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
        private const int DefaultTimeoutMs = 10000;
        
        /// <summary>万用表测量超时时间（毫秒）</summary>
        private const int DmmTimeoutMs = 8000;
        
        /// <summary>继电器操作超时时间（毫秒）</summary>
        private const int RelayTimeoutMs = 5000;

        private const int PowerSupplyTimeoutMs = 8000;
        private const string PowerSupplyIpAddress = "192.168.1.15";
        private const PowerSupplyChannel RelaySupplyChannel = PowerSupplyChannel.CH1;
        private const double RelaySupplyVoltage = 24.0;
        private const double RelaySupplyCurrent = 1.0;

        #endregion

        #region 依赖服务

        private readonly ISingleBoardTestContextService _singleBoardTestContext;  // 单板测试上下文服务
        private readonly ProjectService _projectService;                           // 项目服务，用于数据持久化
        private readonly IEventAggregator _eventAggregator;                        // 事件聚合器，用于跨模块通信
        private readonly IComponentPowerStateApi _componentPowerStateApi;          // 组件供电状态API（复用）
        private readonly IJy7131Api _jy7131Api;                                    // 7131板卡API，控制DO输出
        private readonly IDmmApi _dmmApi;                                          // 万用表API，测量电阻
        private readonly PowerImpedanceSimulation _simulation;                     // 仿真类，硬件不可用时使用

        #endregion

        #region 万用表VISA通信（备用）

        private ResourceManager _dmmResourceManager;                               // VISA资源管理器
        private MessageBasedSession _dmmSession;                                   // VISA会话
        private readonly SemaphoreSlim _dmmIoLock = new SemaphoreSlim(1, 1);      // IO操作锁
        
        #endregion

        #region 状态字段

        private bool _hardwareInitialized;                                         // 硬件是否已初始化
        private CancellationTokenSource _opCts;                                    // 操作取消令牌源
        private SubscriptionToken _projectSavingToken;                             // 项目保存事件订阅令牌

        private bool _isManualTestRunning;                                         // 手动测试是否正在运行
        private bool _isAutoTestRunning;                                           // 自动测试是否正在运行
        private bool _isBusy;                                                      // 是否正在执行操作
        private bool _isRelayActivated;                                            // 继电器是否已激活

        private bool _useSimulatedDmm;                                             // DMM不可用时强制走仿真测量（避免VISA阻塞导致无响应）

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
            IJy7131Api jy7131Api = null,
            IDmmApi dmmApi = null)
        {
            // 保存依赖服务引用
            _singleBoardTestContext = singleBoardTestContext;
            _projectService = projectService;
            _eventAggregator = eventAggregator;
            _componentPowerStateApi = componentPowerStateApi;
            _jy7131Api = jy7131Api;
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

                // ========== 步骤1：配置矩阵开关通路 ==========
                // 矩阵开关用于将仪器连接到被测设备
                AddLog("正在配置矩阵开关通路...");
                bool matrixOk = false;
                try
                {
                    matrixOk = await _simulation.ConnectMatrixAsync(msg => AddLog(msg), timeoutCts.Token);
                }
                catch (Exception ex)
                {
                    AddLog($"矩阵开关配置异常: {ex.Message}");
                }
                if (!matrixOk)
                {
                    AddLog("矩阵开关配置失败，继续使用仿真模式");
                }

                // ========== 步骤2：初始化7131板卡 ==========
                // 流程：Connect → SetOutputMode(PushPull) → Start
                if (_jy7131Api != null)
                {
                    try
                    {
                        AddLog("正在连接7131板卡...");
                        if (!_jy7131Api.IsConnected)
                        {
                            // 2.1 连接板卡
                            await _jy7131Api.ConnectAsync(timeoutCts.Token);
                            AddLog("7131板卡连接成功");
                            
                            // 2.2 设置DO输出模式为推挽模式
                            AddLog("正在设置DO输出模式(PushPull)...");
                            await _jy7131Api.SetOutputModeAsync(Jy7131OutputMode.PushPull, timeoutCts.Token);
                            AddLog("DO输出模式设置完成");
                            
                            // 2.3 启动采集（DI/DO task start）
                            AddLog("正在启动7131板卡...");
                            await _jy7131Api.StartAsync(timeoutCts.Token);
                            AddLog("7131板卡启动成功");
                        }
                        else
                        {
                            AddLog("7131板卡已连接");
                        }
                    }
                    catch (Exception ex)
                    {
                        AddLog($"7131板卡初始化异常: {ex.Message}，使用仿真模式");
                    }
                }
                else
                {
                    AddLog("7131板卡未配置，使用仿真模式");
                }

                // ========== 步骤3：连接万用表 ==========
                // 流程：Connect（测量时调用ReadOnce，结束时Disconnect）
                if (_dmmApi != null)
                {
                    try
                    {
                        AddLog("正在连接万用表...");
                        if (!_dmmApi.IsConnected)
                        {
                            var dmmIp = GetDmmIpAddress();
                            await _dmmApi.ConnectAsync(dmmIp, timeoutCts.Token);
                        }
                        AddLog($"万用表连接成功: {_dmmApi.IpAddress}");
                        _useSimulatedDmm = false;
                    }
                    catch (Exception ex)
                    {
                        AddLog($"万用表连接异常: {ex.Message}，使用仿真模式");
                        _useSimulatedDmm = true;
                    }
                }
                else
                {
                    // 使用备用的VISA方式连接万用表
                    try
                    {
                        await InitializeDmmAsync();
                        _useSimulatedDmm = false;
                    }
                    catch (Exception ex)
                    {
                        AddLog($"万用表VISA初始化异常: {ex.Message}，使用仿真模式");
                        _useSimulatedDmm = true;
                    }
                }

                // ========== 步骤3.5：设置组件供电状态（下电） ==========
                // 电源阻抗测试要求：除“继电器供电/24V输出”等试验台侧供电外，组件本体处于下电状态
                AddLog("正在设置组件供电状态: 下电...");
                try
                {
                    if (_componentPowerStateApi != null)
                    {
                        await _componentPowerStateApi.ApplyComponentDownStateAsync(timeoutCts.Token);
                    }
                    await _simulation.ApplyComponentDownStateAsync(msg => AddLog(msg), timeoutCts.Token);
                    AddLog("组件供电状态已设置为下电");
                }
                catch (Exception ex)
                {
                    AddLog($"组件下电状态设置异常: {ex.Message}");
                }

                _hardwareInitialized = true;
                AddLog("硬件初始化完成");
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !token.IsCancellationRequested)
            {
                // 超时取消（非用户取消），转换为TimeoutException
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
                await _simulation.DisconnectMatrixAsync(msg => AddLog(msg), token);

                // 步骤4：断开万用表
                if (_dmmApi != null)
                {
                    try
                    {
                        await _dmmApi.DisconnectAsync(token);
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

        private string GetDmmIpAddress()
        {
            return "192.168.1.100";
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

                _powerSupplyApi ??= new PowerSupplySocketApi();

                if (!_powerSupplyApi.IsConnected)
                {
                    AddLog($"正在连接程控电源: {PowerSupplyIpAddress}...");
                    await _powerSupplyApi.ConnectAsync(PowerSupplyIpAddress, timeoutCts.Token);
                }

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
                    // 1. 单点写DO15输出高电平，控制外部继电器
                    AddLog("正在写DO15输出...");
                    await _jy7131Api.WriteDoAsync(RelayControlChannel, true, timeoutCts.Token);
                    AddLog("DO15输出完成");
                    
                    // 2. 打开485继电器第4路（参数是3，索引从0开始）
                    AddLog("正在打开485继电器第4路...");
                    await _jy7131Api.SetRelayAsync(3, true, timeoutCts.Token);
                    AddLog("485继电器第4路已打开");
                }
                else
                {
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
                    // 1. 单点写DO15输出低电平
                    AddLog("正在写DO15输出(低电平)...");
                    await _jy7131Api.WriteDoAsync(RelayControlChannel, false, timeoutCts.Token);
                    AddLog("DO15输出完成");
                    
                    // 2. 关闭485继电器第4路
                    AddLog("正在关闭485继电器第4路...");
                    await _jy7131Api.SetRelayAsync(3, false, timeoutCts.Token);
                    AddLog("485继电器第4路已关闭");
                }
                else
                {
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

        private async Task InitializeDmmAsync()
        {
            await _dmmIoLock.WaitAsync();
            try
            {
                if (_dmmSession != null)
                    return;

                _dmmResourceManager = new ResourceManager();
                var resources = _dmmResourceManager.Find("GPIB?*INSTR");

                string dmmAddress = null;
                foreach (var res in resources)
                {
                    if (res.Contains("GPIB"))
                    {
                        dmmAddress = res;
                        break;
                    }
                }

                if (string.IsNullOrEmpty(dmmAddress))
                {
                    dmmAddress = "GPIB0::22::INSTR";
                }

                _dmmSession = (MessageBasedSession)_dmmResourceManager.Open(dmmAddress);
                _dmmSession.TimeoutMilliseconds = 5000;

                _dmmSession.RawIO.Write("*RST\n");
                await Task.Delay(500);
                _dmmSession.RawIO.Write("*IDN?\n");
                string idn = _dmmSession.RawIO.ReadString();
                AddLog($"万用表: {idn.Trim()}");

                _dmmSession.RawIO.Write("CONF:RES\n");
                await Task.Delay(200);
            }
            finally
            {
                _dmmIoLock.Release();
            }
        }

        private async Task<double> ReadResistanceFromDmmAsync(string point, CancellationToken token = default)
        {
            if (_useSimulatedDmm)
            {
                return await _simulation.SimulateMeasureResistanceAsync(point, token);
            }

            if (_dmmApi != null)
            {
                // 按规范：Connect → ReadOnce(RES) → Disconnect
                try
                {
                    if (!_dmmApi.IsConnected)
                    {
                        var dmmIp = GetDmmIpAddress();
                        await _dmmApi.ConnectAsync(dmmIp, token);
                    }

                    var reading = await _dmmApi.ReadOnceAsync(
                        DmmMeasureMode.RES,
                        new DmmReadOptions { TimeoutMilliseconds = DmmTimeoutMs },
                        token);

                    if (reading?.Value != null)
                    {
                        return reading.Value.Value;
                    }

                    if (reading?.IsOverrange == true)
                    {
                        return double.MaxValue;
                    }

                    throw new InvalidOperationException($"万用表读数无效: {reading?.Raw}");
                }
                finally
                {
                    try
                    {
                        if (_dmmApi.IsConnected)
                            await _dmmApi.DisconnectAsync(CancellationToken.None);
                    }
                    catch
                    {
                    }
                }
            }

            // 备用VISA路径：如果当前环境缺少NI-VISA实现，RawIO.ReadString 可能阻塞导致“无响应”。
            // 因此这里任何初始化失败都直接切到仿真。

            await _dmmIoLock.WaitAsync(token);
            try
            {
                if (_dmmSession == null)
                {
                    try
                    {
                        await InitializeDmmAsync();
                    }
                    catch
                    {
                        _useSimulatedDmm = true;
                        return await _simulation.SimulateMeasureResistanceAsync(point, token);
                    }
                }

                // RawIO.ReadString 可能阻塞且不可被 token 取消，这里增加一层超时保护：
                // 超时则切到仿真并提示。
                var visaTask = Task.Run(() =>
                {
                    _dmmSession.RawIO.Write("MEAS:RES?\n");
                    Thread.Sleep(500);
                    return _dmmSession.RawIO.ReadString();
                }, CancellationToken.None);

                var completed = await Task.WhenAny(visaTask, Task.Delay(DmmTimeoutMs, token));
                if (completed != visaTask)
                {
                    _useSimulatedDmm = true;
                    throw new TimeoutException($"万用表VISA读取超时（{DmmTimeoutMs}ms）");
                }

                string response = await visaTask;

                if (double.TryParse(response.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out double resistance))
                {
                    return resistance;
                }

                throw new InvalidOperationException($"无法解析万用表返回值: {response}");
            }
            finally
            {
                _dmmIoLock.Release();
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
                // 确保继电器复位
                if (IsRelayActivated)
                {
                    _jy7131Api.WriteDoAsync(RelayControlChannel, false).GetAwaiter().GetResult();
                }
            }
            catch { }

            try
            {
                _simulation?.DisconnectMatrixAsync(null, CancellationToken.None).GetAwaiter().GetResult();
            }
            catch { }

            try
            {
                if (_dmmApi != null)
                {
                    _dmmApi.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }
            }
            catch { }

            try
            {
                _dmmSession?.Dispose();
                _dmmResourceManager?.Dispose();
            }
            catch { }

            _dmmIoLock?.Dispose();
            _simulation?.Dispose();

            if (_projectSavingToken != null)
            {
                _eventAggregator?.GetEvent<ProjectSavingEvent>()?.Unsubscribe(_projectSavingToken);
            }
        }
    }
}
