using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Sockets;
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
    /// │    ├── 配置矩阵开关通路                                          │
    /// │    └── 通过J3和J4提供28V供电                                     │
    /// ├─────────────────────────────────────────────────────────────────┤
    /// │  步骤2: 采集温度                                                 │
    /// │    └── 解析CRM_PIN7(POWER_TEMP)的DS18B20温度传感器信号           │
    /// ├─────────────────────────────────────────────────────────────────┤
    /// │  步骤3: 判定结果                                                 │
    /// │    └── 温度值处于[15℃, 45℃]区间内为PASS                         │
    /// ├─────────────────────────────────────────────────────────────────┤
    /// │  步骤4: 复位硬件                                                 │
    /// │    └── 断开矩阵开关通路                                          │
    /// └─────────────────────────────────────────────────────────────────┘
    /// 
    /// 【测量点说明】
    /// - CRM_PIN7: POWER_TEMP（温度传感器信号）
    /// - 信号通过IO57连接到INT_IO57（D35, 2槽179通道）
    /// 
    /// 【硬件依赖】
    /// - 矩阵开关：配置信号通路
    /// - 电源：提供28V供电
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
        private const int DefaultTimeoutMs = 10000;

        /// <summary>温度采集超时时间（毫秒）</summary>
        private const int TemperatureReadTimeoutMs = 5000;

        /// <summary>组件供电电源IP地址</summary>
        private const string PowerSupplyIpAddress = "192.168.1.15";

        /// <summary>供电电压（V）</summary>
        private const double InputVoltageV = 28.0;

        /// <summary>供电电流限制（A）</summary>
        private const double InputCurrentA = 3.0;

        /// <summary>矩阵开关IP地址</summary>
        private const string MatrixIpAddress = "192.168.1.3";

        /// <summary>PXI-3022(1) slotindex=2，使用50300端口</summary>
        private const int MatrixSlot = 2;
        private const int MatrixTcpPort = 50300;

        /// <summary>
        /// IO57 → INT_IO57 → FPGA D35（2槽 pin179）
        /// CRM_PIN7(POWER_TEMP) 经 J44 连接到 FPGA，FPGA 负责 DS18B20 采集
        /// I7 = FPGA IO输入行, O179 = 2槽 pin179
        /// </summary>
        private const string MatrixInNode  = "I7";
        private const string MatrixOutNode = "O179";

        #endregion

        #region 依赖服务

        private readonly ISingleBoardTestContextService _singleBoardTestContext;  // 单板测试上下文服务
        private readonly ProjectService _projectService;                           // 项目服务，用于数据持久化
        private readonly IEventAggregator _eventAggregator;                        // 事件聚合器，用于跨模块通信
        private readonly IComponentPowerStateApi _componentPowerStateApi;          // 组件供电状态API（优先使用）
        private readonly TemperatureAcquisitionSimulation _simulation;             // 仿真类，硬件不可用时使用

        private IPowerSupplyApi _power;                                            // 电源API（componentPowerStateApi不可用时备用）
        private FpgaIoClient _fpga;                                                 // FPGA IO TCP客户端

        #endregion

        #region 状态字段

        private bool _hardwareInitialized;                                         // 硬件是否已初始化
        private bool _matrixConnected;                                             // 矩阵开关是否已连接
        private bool _fpgaConnected;                                               // FPGA TCP是否已连接
        private CancellationTokenSource _opCts;                                    // 操作取消令牌源
        private SubscriptionToken _projectSavingToken;                             // 项目保存事件订阅令牌

        private bool _isManualTestRunning;                                         // 手动测试是否正在运行
        private bool _isAutoTestRunning;                                           // 自动测试是否正在运行
        private bool _isBusy;                                                      // 是否正在执行操作
        private bool _isPowerOn;                                                   // 28V供电是否已开启

        private bool _useSimulation = true;                                        // 是否使用仿真模式（硬件不可用时）

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
            ManualTestCommand = new DelegateCommand(OnManualTest);
            AutoTestCommand = new DelegateCommand(OnAutoTest);
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

        /// <summary>
        /// 手动测试按钮点击处理
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
        /// </summary>
        private void StartManualTest()
        {
            _opCts?.Cancel();
            _opCts = new CancellationTokenSource();
            var token = _opCts.Token;

            IsManualTestRunning = true;
            ClearResults();
            AddLog("手动测试开始");

            Task.Run(async () =>
            {
                try
                {
                    // 步骤1: 初始化硬件（供电+配置通路）
                    AddLog("步骤1: 初始化硬件设备（28V供电）...");
                    await InitializeHardwareWithTimeoutAsync(token);
                    if (token.IsCancellationRequested) return;

                    AddLog("硬件初始化完成，请点击\"测量\"按钮进行温度采集");
                }
                catch (TimeoutException ex)
                {
                    AddLog($"超时: {ex.Message}");
                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        ReMessageBox.Show(ex.Message, "超时提示",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                    });
                    try { await ResetHardwareAsync(CancellationToken.None); } catch { }
                    Application.Current?.Dispatcher?.Invoke(() => IsManualTestRunning = false);
                }
                catch (OperationCanceledException)
                {
                    AddLog("测试已取消");
                }
                catch (Exception ex)
                {
                    AddLog($"错误: {ex.Message}");
                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        ReMessageBox.Show($"测试出错: {ex.Message}", "错误",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    });
                    try { await ResetHardwareAsync(CancellationToken.None); } catch { }
                    Application.Current?.Dispatcher?.Invoke(() => IsManualTestRunning = false);
                }
            });
        }

        /// <summary>
        /// 启动自动测试
        /// </summary>
        private void StartAutoTest()
        {
            _opCts?.Cancel();
            _opCts = new CancellationTokenSource();
            var token = _opCts.Token;

            IsAutoTestRunning = true;
            ClearResults();
            AddLog("自动测试开始");

            Task.Run(async () =>
            {
                try
                {
                    // 步骤1: 初始化硬件
                    AddLog("步骤1: 初始化硬件设备（28V供电）...");
                    await InitializeHardwareWithTimeoutAsync(token);
                    if (token.IsCancellationRequested) return;

                    // 步骤2: 自动采集温度
                    AddLog("步骤2: 采集温度...");
                    await MeasureTemperatureAsync();
                    if (token.IsCancellationRequested) return;

                    // 步骤3: 复位硬件
                    AddLog("步骤3: 复位硬件...");
                    await ResetHardwareAsync(token);

                    AddLog("自动测试完成");
                }
                catch (TimeoutException ex)
                {
                    AddLog($"超时: {ex.Message}");
                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        ReMessageBox.Show(ex.Message, "超时提示",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                    });
                }
                catch (OperationCanceledException)
                {
                    AddLog("测试已取消");
                }
                catch (Exception ex)
                {
                    AddLog($"错误: {ex.Message}");
                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        ReMessageBox.Show($"测试出错: {ex.Message}", "错误",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    });
                }
                finally
                {
                    try { await ResetHardwareAsync(CancellationToken.None); } catch { }
                    Application.Current?.Dispatcher?.Invoke(() => IsAutoTestRunning = false);
                }
            });
        }

        /// <summary>
        /// 停止测试
        /// </summary>
        private void StopTest()
        {
            AddLog("正在停止测试...");
            _opCts?.Cancel();

            Task.Run(async () =>
            {
                try
                {
                    await ResetHardwareAsync(CancellationToken.None);
                }
                catch { }
                finally
                {
                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        IsManualTestRunning = false;
                        IsAutoTestRunning = false;
                        AddLog("测试已停止");
                    });
                }
            });
        }

        #endregion

        #region 硬件操作

        /// <summary>
        /// 带超时的硬件初始化
        /// </summary>
        private async Task InitializeHardwareWithTimeoutAsync(CancellationToken token)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeoutCts.CancelAfter(DefaultTimeoutMs);

            try
            {
                await InitializeHardwareAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !token.IsCancellationRequested)
            {
                throw new TimeoutException($"硬件初始化超时（{DefaultTimeoutMs}ms），请检查设备连接");
            }
        }

        /// <summary>
        /// 初始化硬件：28V供电 → 配置矩阵开关通路
        /// </summary>
        private async Task InitializeHardwareAsync(CancellationToken token)
        {
            if (_hardwareInitialized)
            {
                AddLog("硬件已初始化，跳过");
                return;
            }

            // ========== 步骤1：28V供电 ==========
            AddLog($"正在开启28V供电（{InputVoltageV:0.###}V/{InputCurrentA:0.###}A）...");
            try
            {
                if (_componentPowerStateApi != null)
                {
                    await _componentPowerStateApi.ApplyComponent28VStateAsync(token);
                    AddLog("组件28V供电状态已设置（IComponentPowerStateApi）");
                    _useSimulation = false;
                }
                else
                {
                    await ConnectPowerSupplyAsync(PowerSupplyIpAddress, token);
                    await _power.ApplyAsync(PowerSupplyChannel.CH1, InputVoltageV, InputCurrentA, token);
                    await _power.SetOutputEnabledAsync(PowerSupplyChannel.CH1, true, token);
                    await Task.Delay(300, token);
                    AddLog($"电源输出已开启: CH1 {InputVoltageV:0.###}V");
                    _useSimulation = false;
                }
            }
            catch (Exception ex)
            {
                AddLog($"供电异常: {ex.Message}，使用仿真模式");
                await _simulation.ApplyComponent28VStateAsync(AddLog, token);
                _useSimulation = true;
            }

            Application.Current?.Dispatcher?.Invoke(() =>
            {
                IsPowerOn = true;
                PowerStatus = "已上电";
            });

            // ========== 步骤2：配置矩阵开关通路 ==========
            // IO57(CRM_PIN7/POWER_TEMP) → INT_IO57 → FPGA D35
            // 2槽 pin179 = IO57，slot=2 (PXI-3022(1)), tcpPort=50300
            AddLog($"正在配置矩阵开关通路（{MatrixInNode}→{MatrixOutNode} slot={MatrixSlot}）...");
            try
            {
                var ok = await MatrixControlService.Instance.ConnectNodesAsync(
                    MatrixInNode, MatrixOutNode, MatrixSlot, MatrixIpAddress, MatrixTcpPort);
                _matrixConnected = ok;
                AddLog($"矩阵开关通路: {(ok ? "OK" : "FAIL")} ({MatrixInNode}→{MatrixOutNode} slot={MatrixSlot})");
                if (!ok)
                    AddLog("矩阵通路配置失败，温度读取将使用仿真");
            }
            catch (Exception ex)
            {
                AddLog($"矩阵开关异常: {ex.Message}，温度读取将使用仿真");
                _matrixConnected = false;
            }

            // ========== 步骤3：连接FPGA TCP服务器 ==========
            AddLog($"正在连接FPGA TCP服务器 {FpgaIoClient.DefaultIpAddress}:{FpgaIoClient.DefaultPort} ...");
            try
            {
                _fpga ??= new FpgaIoClient();
                await _fpga.ConnectAsync(token);
                _fpgaConnected = true;
                AddLog("FPGA TCP连接成功");
            }
            catch (Exception ex)
            {
                AddLog($"FPGA TCP连接异常: {ex.Message}，温度读取将使用仿真");
                _fpgaConnected = false;
            }

            _hardwareInitialized = true;
            UpdateCommandStates();
            AddLog("硬件初始化完成");
        }

        /// <summary>
        /// 复位硬件：断开矩阵开关 → 关闭供电
        /// </summary>
        private async Task ResetHardwareAsync(CancellationToken token)
        {
            AddLog("正在复位硬件...");

            // 断开矩阵开关
            if (_matrixConnected)
            {
                try
                {
                    await MatrixControlService.Instance.DisconnectNodesAsync(
                        MatrixInNode, MatrixOutNode, MatrixSlot, MatrixIpAddress, MatrixTcpPort);
                    _matrixConnected = false;
                    AddLog("矩阵开关通路已断开");
                }
                catch (Exception ex)
                {
                    AddLog($"断开矩阵开关异常: {ex.Message}");
                }
            }

            // 断开FPGA
            if (_fpga != null)
            {
                try { _fpga.Disconnect(); } catch { }
                _fpga = null;
                _fpgaConnected = false;
                AddLog("FPGA TCP已断开");
            }

            // 关闭供电
            try
            {
                if (_componentPowerStateApi != null && !_useSimulation)
                {
                    await _componentPowerStateApi.ApplyComponentDownStateAsync(token);
                    AddLog("组件已下电");
                }
                else if (_power != null)
                {
                    try { await _power.SetOutputEnabledAsync(PowerSupplyChannel.CH1, false, token); } catch { }
                    try { await _power.DisconnectAsync(token); } catch { }
                    try { await _power.DisposeAsync(); } catch { }
                    _power = null;
                    AddLog("电源输出已关闭");
                }
                else
                {
                    await _simulation.ApplyComponentDownStateAsync(AddLog, token);
                }
            }
            catch (Exception ex)
            {
                AddLog($"下电异常: {ex.Message}");
            }

            Application.Current?.Dispatcher?.Invoke(() =>
            {
                IsPowerOn = false;
                PowerStatus = "未上电";
            });

            _hardwareInitialized = false;
            UpdateCommandStates();
            AddLog("硬件复位完成");
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
        /// 矩阵开关通路：IO57(2槽pin179) I7→O179 已在InitializeHardwareAsync中建立。
        /// </summary>
        private async Task<double> ReadTemperatureAsync(CancellationToken token)
        {
            // 矩阵通路未就绪或硬件不可用，降级到仿真
            if (_useSimulation || !_matrixConnected)
            {
                var sim = await _simulation.SimulateReadTemperatureAsync(token);
                AddLog($"温度来源: 仿真  {sim:F2}℃");
                return sim;
            }

            // 硬件路径：通过FPGA IO57采集DS18B20温度
            // FPGA已通过矩阵开关的IO57通路(I7→O179, slot=2)连接DS18B20
            // 按DS18B20U+T&R规格书：发送Convert T命令后读取温度寄存器
            try
            {
                var temp = await ReadDs18B20ViaMioAsync(token);
                AddLog($"温度来源: FPGA/DS18B20  {temp:F2}℃");
                return temp;
            }
            catch (Exception ex)
            {
                AddLog($"FPGA温度读取异常: {ex.Message}，降级到仿真");
                var sim = await _simulation.SimulateReadTemperatureAsync(token);
                AddLog($"温度来源: 仿真  {sim:F2}℃");
                return sim;
            }
        }

        /// <summary>
        /// 程控电源连接方法隔离层。[NoInlining] 防止 NI-VISA JIT 加载崩溃逃逸 try-catch。
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private async Task ConnectPowerSupplyAsync(string ipAddress, CancellationToken token)
        {
            _power ??= new PowerSupplySocketApi();
            if (!_power.IsConnected)
                await _power.ConnectAsync(ipAddress, token);
        }

        /// <summary>
        /// 通过FPGA TCP接口（IP=192.168.1.10, Port=5001）读取DS18B20温度。
        /// 协议：发送命令0x07（无数据），FPGA返回1个单精度浮点数（小端，单位℃）。
        /// 帧格式：帧头(AA 55) + 长度(01) + 命令(07) → 应答：帧头(AA 55) + 长度(05) + 命令(07) + float32
        /// </summary>
        private async Task<double> ReadDs18B20ViaMioAsync(CancellationToken token)
        {
            if (_fpga == null || !_fpgaConnected)
            {
                _fpga ??= new FpgaIoClient();
                await _fpga.ConnectAsync(token);
                _fpgaConnected = true;
            }

            float tempF = await _fpga.ReadDs18B20TemperatureAsync(token);
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

            try
            {
                if (_matrixConnected)
                {
                    MatrixControlService.Instance.DisconnectNodesAsync(
                        MatrixInNode, MatrixOutNode, MatrixSlot, MatrixIpAddress, MatrixTcpPort)
                        .GetAwaiter().GetResult();
                }
            }
            catch { }

            try { _fpga?.Disconnect(); } catch { }
            _fpga = null;

            try
            {
                if (_power != null)
                {
                    try { _power.SetOutputEnabledAsync(PowerSupplyChannel.CH1, false, CancellationToken.None).GetAwaiter().GetResult(); } catch { }
                    try { _power.DisposeAsync().AsTask().GetAwaiter().GetResult(); } catch { }
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

    /// <summary>
    /// 加放油高速 IO 板卡（FPGA）TCP 通信客户端。
    /// FPGA端TCP服务器，静态IP: 192.168.1.10，端口: 5001。
    /// 帧格式： 帧头(0xAA,0x55) + 长度(1B) + 命令(1B) + 数据(长度-1 B)
    /// </summary>
    internal sealed class FpgaIoClient : IDisposable
    {
        public const string DefaultIpAddress = "192.168.1.10";
        public const int DefaultPort = 5001;

        private static readonly byte[] FrameHeader = { 0xAA, 0x55 };

        private readonly string _ip;
        private readonly int _port;
        private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);

        private TcpClient _client;
        private NetworkStream _stream;

        public bool IsConnected => _client?.Connected == true && _stream != null;

        public FpgaIoClient(string ip = DefaultIpAddress, int port = DefaultPort)
        {
            _ip = ip;
            _port = port;
        }

        public async Task ConnectAsync(CancellationToken token = default)
        {
            if (IsConnected) return;

            try { _client?.Dispose(); } catch { }
            _client = null;
            _stream = null;

            var client = new TcpClient { NoDelay = true };
            try
            {
                using var timeoutCts = new System.Threading.CancellationTokenSource(5000);
                using var linkedCts = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCts.Token);

                var connectTask = client.ConnectAsync(_ip, _port);
                var cancelTask = Task.Delay(Timeout.Infinite, linkedCts.Token);

                var completed = await Task.WhenAny(connectTask, cancelTask);
                if (completed != connectTask)
                {
                    try { client.Close(); } catch { }
                    token.ThrowIfCancellationRequested();
                    throw new TimeoutException($"FPGA连接超时（5s），IP={_ip}:{_port}");
                }

                await connectTask;

                _client = client;
                _stream = _client.GetStream();
            }
            catch
            {
                try { client?.Close(); } catch { }
                throw;
            }
        }

        public void Disconnect()
        {
            try { _stream?.Close(); } catch { }
            try { _client?.Close(); } catch { }
            _stream = null;
            _client = null;
        }

        private static byte[] BuildFrame(byte command, byte[] data = null)
        {
            int dataLen = data?.Length ?? 0;
            byte lengthField = (byte)(1 + dataLen);
            var frame = new byte[2 + 1 + 1 + dataLen];
            frame[0] = FrameHeader[0];
            frame[1] = FrameHeader[1];
            frame[2] = lengthField;
            frame[3] = command;
            if (dataLen > 0)
                Buffer.BlockCopy(data, 0, frame, 4, dataLen);
            return frame;
        }

        private async Task<(byte cmd, byte[] payload)> ReadFrameAsync(CancellationToken token)
        {
            var header = await ReadExactAsync(2, token);
            if (header[0] != 0xAA || header[1] != 0x55)
                throw new InvalidOperationException($"FPGA帧头校验失败: 0x{header[0]:X2} 0x{header[1]:X2}");

            var lenBuf = await ReadExactAsync(1, token);
            int totalLen = lenBuf[0];

            var body = await ReadExactAsync(totalLen, token);
            byte cmd = body[0];
            byte[] payload = new byte[totalLen - 1];
            if (payload.Length > 0)
                Buffer.BlockCopy(body, 1, payload, 0, payload.Length);

            return (cmd, payload);
        }

        private async Task<byte[]> ReadExactAsync(int count, CancellationToken token)
        {
            var buf = new byte[count];
            int received = 0;
            while (received < count)
            {
                int n = await _stream.ReadAsync(buf, received, count - received, token);
                if (n == 0) throw new InvalidOperationException("FPGA连接已断开（读取0字节）");
                received += n;
            }
            return buf;
        }

        /// <summary>0x07 读取DS18B20温度，返回单精度浮点数（小端，单位℃）</summary>
        public async Task<float> ReadDs18B20TemperatureAsync(CancellationToken token = default)
        {
            await _lock.WaitAsync(token);
            try
            {
                await EnsureConnectedAsync(token);
                var frame = BuildFrame(0x07);
                await _stream.WriteAsync(frame, 0, frame.Length, token);
                await _stream.FlushAsync(token);

                var (cmd, payload) = await ReadFrameAsync(token);
                if (cmd != 0x07)
                    throw new InvalidOperationException($"DS18B20温度读取：应答命令错误 0x{cmd:X2}，期望 0x07");
                if (payload.Length < 4)
                    throw new InvalidOperationException($"DS18B20温度读取：应答数据长度不足 {payload.Length} bytes，期望 4");

                return BitConverter.ToSingle(payload, 0);
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>0x00 写GPIO输出 (IO11-32 对应 bit0-21，uint32小端)，并消费FPGA返回的0x00响应帧</summary>
        public async Task WriteGpioAsync(uint ioMask, CancellationToken token = default)
        {
            await _lock.WaitAsync(token);
            try
            {
                await EnsureConnectedAsync(token);
                var frame = BuildFrame(0x00, BitConverter.GetBytes(ioMask));
                await _stream.WriteAsync(frame, 0, frame.Length, token);
                await _stream.FlushAsync(token);
                // 协议：发送0x00后FPGA会返回一个0x00帧(GPIO输入读值)，必须消费否则后续帧错位
                var (_, _) = await ReadFrameAsync(token);
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>0x00 读GPIO输入 (IO43-64 对应 bit0-21，uint32小端)</summary>
        public async Task<uint> ReadGpioAsync(CancellationToken token = default)
        {
            await _lock.WaitAsync(token);
            try
            {
                await EnsureConnectedAsync(token);
                var frame = BuildFrame(0x00, new byte[] { 0, 0, 0, 0 });
                await _stream.WriteAsync(frame, 0, frame.Length, token);
                await _stream.FlushAsync(token);

                var (cmd, payload) = await ReadFrameAsync(token);
                if (cmd != 0x00)
                    throw new InvalidOperationException($"GPIO读取：应答命令错误 0x{cmd:X2}，期望 0x00");
                if (payload.Length < 4)
                    throw new InvalidOperationException($"GPIO读取：应答数据长度不足 {payload.Length} bytes");

                return BitConverter.ToUInt32(payload, 0);
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>0x04 初始化HI8435，等待FPGA应答帧</summary>
        public async Task InitHi8435Async(CancellationToken token = default)
        {
            await _lock.WaitAsync(token);
            try
            {
                await EnsureConnectedAsync(token);
                var frame = BuildFrame(0x04);
                await _stream.WriteAsync(frame, 0, frame.Length, token);
                await _stream.FlushAsync(token);
                // 消费FPGA应答帧（若有），防止后续帧错位
                try
                {
                    using var ackCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                    ackCts.CancelAfter(500);
                    var (_, _) = await ReadFrameAsync(ackCts.Token);
                }
                catch (OperationCanceledException) { }
                catch { }
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>0x06 读HI8435 BANK3-0状态，返回4字节 byte0-3对应bank3-0</summary>
        public async Task<byte[]> ReadHi8435Async(CancellationToken token = default)
        {
            await _lock.WaitAsync(token);
            try
            {
                await EnsureConnectedAsync(token);
                var frame = BuildFrame(0x06);
                await _stream.WriteAsync(frame, 0, frame.Length, token);
                await _stream.FlushAsync(token);

                var (cmd, payload) = await ReadFrameAsync(token);
                if (cmd != 0x06)
                    throw new InvalidOperationException($"HI8435读取：应答命令错误 0x{cmd:X2}，期望 0x06");
                if (payload.Length < 4)
                    throw new InvalidOperationException($"HI8435读取：应答数据长度不足 {payload.Length} bytes");

                return payload;
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>
        /// 0x01/02/03 UART TX+RX 一体：发送数据后等待FPGA回传回环数据。
        /// 用于自检（RS422内部回环）：TX发出后FPGA将收到的回环数据作为同命令帧返回。
        /// uartIndex: 0=SCI1(UART0), 1=SCI2(UART1), 2=UART2
        /// </summary>
        public async Task<byte[]> UartTxRxAsync(int uartIndex, byte[] data, CancellationToken token = default)
        {
            if (uartIndex < 0 || uartIndex > 2) throw new ArgumentOutOfRangeException(nameof(uartIndex));
            if (data == null || data.Length == 0 || data.Length > 201) throw new ArgumentException("数据长度需在1~201字节内");

            await _lock.WaitAsync(token);
            try
            {
                await EnsureConnectedAsync(token);
                var frame = BuildFrame((byte)(0x01 + uartIndex), data);
                await _stream.WriteAsync(frame, 0, frame.Length, token);
                await _stream.FlushAsync(token);

                byte expectedCmd = (byte)(0x01 + uartIndex);
                var (cmd, payload) = await ReadFrameAsync(token);
                if (cmd != expectedCmd)
                    throw new InvalidOperationException($"UART{uartIndex} TX/RX：应答命令错误 0x{cmd:X2}，期望 0x{expectedCmd:X2}");
                return payload;
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>
        /// 0x01/02/03 仅发送 UART TX（外部通信模式，不等待回环应答）。
        /// 发送后FPGA不会立即返回帧，外部设备收到数据后可能发回数据由 UartRxWaitAsync 接收。
        /// </summary>
        public async Task UartTxOnlyAsync(int uartIndex, byte[] data, CancellationToken token = default)
        {
            if (uartIndex < 0 || uartIndex > 2) throw new ArgumentOutOfRangeException(nameof(uartIndex));
            if (data == null || data.Length == 0 || data.Length > 201) throw new ArgumentException("数据长度需在1~201字节内");

            await _lock.WaitAsync(token);
            try
            {
                await EnsureConnectedAsync(token);
                var frame = BuildFrame((byte)(0x01 + uartIndex), data);
                await _stream.WriteAsync(frame, 0, frame.Length, token);
                await _stream.FlushAsync(token);
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>
        /// 等待并接收 UART RX 帧（外部设备主动发回的数据）。
        /// uartIndex: 0=SCI1, 1=SCI2, 2=UART2
        /// </summary>
        public async Task<byte[]> UartRxWaitAsync(int uartIndex, CancellationToken token = default)
        {
            if (uartIndex < 0 || uartIndex > 2) throw new ArgumentOutOfRangeException(nameof(uartIndex));

            await _lock.WaitAsync(token);
            try
            {
                await EnsureConnectedAsync(token);
                byte expectedCmd = (byte)(0x01 + uartIndex);
                var (cmd, payload) = await ReadFrameAsync(token);
                if (cmd != expectedCmd)
                    throw new InvalidOperationException($"UART{uartIndex} RX等待：应答命令错误 0x{cmd:X2}，期望 0x{expectedCmd:X2}");
                return payload;
            }
            finally
            {
                _lock.Release();
            }
        }

        private async Task EnsureConnectedAsync(CancellationToken token)
        {
            if (!IsConnected)
                await ConnectAsync(token);
        }

        public void Dispose()
        {
            _lock?.Dispose();
            Disconnect();
        }
    }
}
