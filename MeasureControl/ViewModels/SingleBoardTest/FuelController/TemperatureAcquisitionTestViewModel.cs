using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
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

        /// <summary>温度采集通道（IO57 -> INT_IO57）</summary>
        private const string TemperatureChannel = "IO57";

        #endregion

        #region 依赖服务

        private readonly ISingleBoardTestContextService _singleBoardTestContext;  // 单板测试上下文服务
        private readonly ProjectService _projectService;                           // 项目服务，用于数据持久化
        private readonly IEventAggregator _eventAggregator;                        // 事件聚合器，用于跨模块通信
        private readonly IComponentPowerStateApi _componentPowerStateApi;          // 组件供电状态API（复用）
        private readonly IPxiChassisService _pxiChassisService;                    // PXI机箱服务
        private readonly TemperatureAcquisitionSimulation _simulation;             // 仿真类，硬件不可用时使用

        #endregion

        #region 状态字段

        private bool _hardwareInitialized;                                         // 硬件是否已初始化
        private CancellationTokenSource _opCts;                                    // 操作取消令牌源
        private SubscriptionToken _projectSavingToken;                             // 项目保存事件订阅令牌

        private bool _isManualTestRunning;                                         // 手动测试是否正在运行
        private bool _isAutoTestRunning;                                           // 自动测试是否正在运行
        private bool _isBusy;                                                      // 是否正在执行操作
        private bool _isPowerOn;                                                   // 28V供电是否已开启

        private bool _useSimulation = true;                                        // 是否使用仿真模式

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
            IComponentPowerStateApi componentPowerStateApi,
            IPxiChassisService pxiChassisService)
        {
            // 保存依赖服务引用
            _singleBoardTestContext = singleBoardTestContext;
            _projectService = projectService;
            _eventAggregator = eventAggregator;
            _componentPowerStateApi = componentPowerStateApi;
            _pxiChassisService = pxiChassisService;
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
            using var timeoutCts = new CancellationTokenSource(DefaultTimeoutMs);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCts.Token);

            try
            {
                await InitializeHardwareAsync(linkedCts.Token);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !token.IsCancellationRequested)
            {
                throw new TimeoutException($"硬件初始化超时（{DefaultTimeoutMs / 1000}秒），请检查设备连接");
            }
        }

        /// <summary>
        /// 初始化硬件
        /// </summary>
        private async Task InitializeHardwareAsync(CancellationToken token)
        {
            // 1. 设置组件28V供电状态
            AddLog("正在设置组件28V供电状态...");
            
            if (_componentPowerStateApi != null)
            {
                try
                {
                    await _componentPowerStateApi.ApplyComponent28VStateAsync(token);
                    AddLog("组件28V供电状态已设置");
                    _useSimulation = false;
                }
                catch (Exception ex)
                {
                    AddLog($"供电API异常: {ex.Message}，使用仿真模式");
                    await _simulation.ApplyComponent28VStateAsync(AddLog, token);
                    _useSimulation = true;
                }
            }
            else
            {
                await _simulation.ApplyComponent28VStateAsync(AddLog, token);
                _useSimulation = true;
            }

            Application.Current?.Dispatcher?.Invoke(() =>
            {
                IsPowerOn = true;
                PowerStatus = "已上电";
            });

            // 2. 配置矩阵开关通路
            AddLog("正在配置矩阵开关通路...");
            await _simulation.ConnectMatrixAsync(AddLog, token);

            _hardwareInitialized = true;
            UpdateCommandStates();
        }

        /// <summary>
        /// 复位硬件
        /// </summary>
        private async Task ResetHardwareAsync(CancellationToken token)
        {
            AddLog("正在复位硬件...");

            // 断开矩阵开关
            await _simulation.DisconnectMatrixAsync(AddLog, token);

            // 下电
            if (_componentPowerStateApi != null && !_useSimulation)
            {
                try
                {
                    await _componentPowerStateApi.ApplyComponentDownStateAsync(token);
                    AddLog("组件已下电");
                }
                catch (Exception ex)
                {
                    AddLog($"下电异常: {ex.Message}");
                }
            }
            else
            {
                await _simulation.ApplyComponentDownStateAsync(AddLog, token);
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
        /// 采集温度
        /// </summary>
        private async Task MeasureTemperatureAsync()
        {
            if (IsBusy) return;

            Application.Current?.Dispatcher?.Invoke(() => IsBusy = true);

            try
            {
                AddLog("正在采集温度...");

                // TODO: 实际硬件实现时，需要解析DS18B20温度传感器信号
                // 当前使用仿真
                var temperature = await ReadTemperatureAsync(_opCts?.Token ?? CancellationToken.None);

                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    TemperatureValue = temperature;
                    AddLog($"温度采集完成: {temperature:F1}℃");

                    // 判定结果
                    if (temperature >= TemperatureLowerLimit && temperature <= TemperatureUpperLimit)
                    {
                        TestResult = "PASS";
                        OverallResult = "PASS";
                        AddLog($"判定: PASS（温度在[{TemperatureLowerLimit}℃, {TemperatureUpperLimit}℃]区间内）");
                    }
                    else
                    {
                        TestResult = "FAIL";
                        OverallResult = "FAIL";
                        AddLog($"判定: FAIL（温度超出[{TemperatureLowerLimit}℃, {TemperatureUpperLimit}℃]区间）");
                    }

                    LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
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
        /// 读取温度值
        /// </summary>
        private async Task<double> ReadTemperatureAsync(CancellationToken token)
        {
            // TODO: 实际硬件实现
            // 当前使用仿真
            var temperature = await _simulation.SimulateReadTemperatureAsync(token);
            AddLog("温度来源: 仿真");
            return temperature;
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
