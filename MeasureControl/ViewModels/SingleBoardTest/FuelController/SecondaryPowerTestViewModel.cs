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

namespace MeasureControl.ViewModels.SingleBoardTest.FuelController
{
    /// <summary>
    /// ============================================================================
    /// 二次电源测试 ViewModel (SecondaryPowerTestViewModel)
    /// ============================================================================
    /// 
    /// 【测试目的】
    /// 验证加放油控制器的二次电源（+5V）输出是否正常。
    /// 在组件28V供电状态下，测量CRM_PIN1对CRM_PIN18之间的电压值。
    /// 
    /// 【测试流程概述】
    /// ┌─────────────────────────────────────────────────────────────────┐
    /// │  步骤1: 初始化硬件                                               │
    /// │    ├── 配置矩阵开关通路（连接万用表）                             │
    /// │    ├── 通过J3和J4提供28V供电                                     │
    /// │    └── 连接万用表（用于电压测量）                                 │
    /// ├─────────────────────────────────────────────────────────────────┤
    /// │  步骤2: 测量电压                                                 │
    /// │    └── 使用万用表直流电压档测量CRM_PIN1对CRM_PIN18电压            │
    /// ├─────────────────────────────────────────────────────────────────┤
    /// │  步骤3: 判定结果                                                 │
    /// │    └── 电压值满足区间[4.5V, 5.5V]为PASS                          │
    /// ├─────────────────────────────────────────────────────────────────┤
    /// │  步骤4: 复位硬件                                                 │
    /// │    ├── 断开矩阵开关通路                                          │
    /// │    └── 断开万用表连接                                            │
    /// └─────────────────────────────────────────────────────────────────┘
    /// 
    /// 【供电说明】
    /// - 通过J3和J4提供28V供电
    /// - 继电器不动作（保持在NC状态），产品正常连接试验台
    /// 
    /// 【测量点说明】
    /// - CRM_PIN1: +5V电源输出
    /// - CRM_PIN18: GND（地）
    /// - 测量两者之间的直流电压
    /// 
    /// 【硬件依赖】
    /// - 万用表(DMM)：测量直流电压
    /// - 矩阵开关：配置信号通路
    /// - 电源：提供28V供电
    /// 
    /// 【超时保护】
    /// 所有硬件操作都有超时保护，超时后会弹出提示框，不会导致程序卡死
    /// </summary>
    public class SecondaryPowerTestViewModel : BindableBase, IDisposable
    {
        #region 常量定义

        /// <summary>测试项唯一标识，用于数据持久化</summary>
        private const string TestItemKey = "FuelController_SecondaryPower";
        
        /// <summary>电压判定下限（V）</summary>
        private const double VoltageLowerLimit = 4.5;
        
        /// <summary>电压判定上限（V）</summary>
        private const double VoltageUpperLimit = 5.5;
        
        /// <summary>硬件初始化默认超时时间（毫秒）</summary>
        private const int DefaultTimeoutMs = 10000;
        
        /// <summary>万用表测量超时时间（毫秒）</summary>
        private const int DmmTimeoutMs = 8000;

        private const string AiVoltageChannel = "AI1";

        #endregion

        #region 依赖服务

        private readonly ISingleBoardTestContextService _singleBoardTestContext;  // 单板测试上下文服务
        private readonly ProjectService _projectService;                           // 项目服务，用于数据持久化
        private readonly IEventAggregator _eventAggregator;                        // 事件聚合器，用于跨模块通信
        private readonly IComponentPowerStateApi _componentPowerStateApi;          // 组件供电状态API（复用）
        private readonly IPxiChassisService _pxiChassisService;                    // PXI机箱服务，用于查找9774板卡
        private readonly IDmmApi _dmmApi;                                          // 万用表API，测量电压
        private readonly SecondaryPowerSimulation _simulation;                     // 仿真类，硬件不可用时使用

        #endregion

        #region 9774板卡(优先采集)

        private IArt9774AiApi _ai9774Api;

        #endregion

        #region 万用表Socket连接

        private IDmmApi _dmmSocket;                                                 // DmmSocketApi实例
        private readonly SemaphoreSlim _measureLock = new SemaphoreSlim(1, 1);    // 测量操作锁

        #endregion

        #region 状态字段

        private bool _hardwareInitialized;                                         // 硬件是否已初始化
        private CancellationTokenSource _opCts;                                    // 操作取消令牌源
        private SubscriptionToken _projectSavingToken;                             // 项目保存事件订阅令牌

        private bool _isManualTestRunning;                                         // 手动测试是否正在运行
        private bool _isAutoTestRunning;                                           // 自动测试是否正在运行
        private bool _isBusy;                                                      // 是否正在执行操作
        private bool _isPowerOn;                                                   // 28V供电是否已开启

        private bool _useSimulatedDmm;                                             // DMM不可用时走仿真测量

        #endregion

        #region 测量结果字段

        private double? _voltageValue;        // 测量的电压值（单位：V）
        private string _testResult = "--";    // 测试结果（PASS/FAIL/--）
        private string _overallResult = "--"; // 综合结果
        private string _lastTestTime = "--";  // 上次测试时间
        private string _powerStatus = "未上电"; // 供电状态显示文本

        #endregion

        #region 构造函数

        /// <summary>
        /// 构造函数 - 通过依赖注入获取所需服务
        /// </summary>
        public SecondaryPowerTestViewModel(
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
            _simulation = new SecondaryPowerSimulation();

            // 初始化命令
            ManualTestCommand = new DelegateCommand(OnManualTest);
            AutoTestCommand = new DelegateCommand(OnAutoTest);
            MeasureCommand = new DelegateCommand(async () => await MeasureSinglePointAsync(), () => !IsBusy && IsManualTestRunning && _hardwareInitialized && IsPowerOn);
            ClearLogCommand = new DelegateCommand(() => Logs.Clear());

            // 加载上次保存的测试结果
            LoadPersistedState();
            
            // 订阅项目保存事件
            _projectSavingToken = _eventAggregator?.GetEvent<ProjectSavingEvent>()?.Subscribe(OnProjectSaving);
        }

        private async Task<double> ReadVoltageAsync(CancellationToken token = default)
        {
            if (_ai9774Api != null && _ai9774Api.IsConnected)
            {
                try
                {
                    if (!_ai9774Api.IsRunning)
                    {
                        try { await _ai9774Api.StartAsync(token); } catch { }
                    }

                    var v = await _ai9774Api.GetLastValueAsync(AiVoltageChannel, token);
                    AddLog("电压来源: 9774");
                    return v;
                }
                catch (Exception ex)
                {
                    AddLog($"9774采集异常: {ex.Message}，切换到万用表/仿真");
                }
            }

            var dmmVoltage = await ReadVoltageFromDmmAsync(token);
            AddLog(_useSimulatedDmm ? "电压来源: 仿真" : "电压来源: 万用表");
            return dmmVoltage;
        }

        private static bool Is9774(DeviceBase device)
        {
            var model = (device?.Model ?? string.Empty).ToUpperInvariant();
            return model.Contains("9774") || model.Contains("PXIE-9774") || model.Contains("PXI-9774");
        }

        private DeviceBase Find9774DeviceInChassis(string chassisName)
        {
            if (string.IsNullOrWhiteSpace(chassisName))
                return null;

            var devices = _pxiChassisService?.GetChassisDevices(chassisName);
            if (devices == null || devices.Count == 0)
                return null;

            DeviceBase Walk(DeviceBase d)
            {
                if (Is9774(d))
                    return d;

                if (d?.Children == null)
                    return null;

                foreach (var c in d.Children)
                {
                    var found = Walk(c);
                    if (found != null)
                        return found;
                }

                return null;
            }

            foreach (var d in devices)
            {
                var found = Walk(d);
                if (found != null)
                    return found;
            }

            return null;
        }

        private async Task Initialize9774AiAsync(CancellationToken token)
        {
            if (_ai9774Api != null && _ai9774Api.IsConnected)
            {
                AddLog("9774板卡已连接，跳过");
                return;
            }

            var chassisName = _singleBoardTestContext?.ChassisName;
            var device = Find9774DeviceInChassis(chassisName);
            if (device == null)
            {
                AddLog("未找到9774板卡，二次电源电压将使用万用表/仿真");
                return;
            }

            try
            {
                AddLog($"正在连接9774板卡... Model={device.Model} Name={device.Name}");

                var inferredDevName = Infer9774DevName(device);
                _ai9774Api = new Art9774Api(device, new AiAcquisitionOptions
                {
                    Mode = AiAcquisitionMode.Continuous,
                    SampleRateHz = 10000.0,
                    SamplesPerChannel = 1000,
                    DeviceName = string.IsNullOrWhiteSpace(inferredDevName) ? "Dev3" : inferredDevName
                });

                await _ai9774Api.ConnectAsync(token);
                await _ai9774Api.ConfigureChannelsAsync(new[]
                {
                    new AiChannelConfig { Channel = AiVoltageChannel, Enabled = true, Range = AiInputRange.PlusMinus10V }
                }, token);

                await _ai9774Api.StartAsync(token);
                AddLog("9774板卡初始化完成");
            }
            catch (Exception ex)
            {
                AddLog($"9774板卡初始化异常: {ex.Message}，二次电源电压将使用万用表/仿真");
                try
                {
                    if (_ai9774Api != null)
                        await _ai9774Api.DisposeAsync();
                }
                catch { }
                _ai9774Api = null;
            }
        }

        private static string Infer9774DevName(DeviceBase device)
        {
            string ExtractDev(string text)
            {
                if (string.IsNullOrWhiteSpace(text))
                    return null;

                var parts = text.Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var p in parts)
                {
                    if (p.Length >= 4 && p.StartsWith("Dev", StringComparison.OrdinalIgnoreCase))
                    {
                        var suffix = p.Substring(3);
                        if (int.TryParse(suffix, out var n) && n > 0)
                            return $"Dev{n}";
                    }
                }

                return null;
            }

            var byName = ExtractDev(device?.CardName) ?? ExtractDev(device?.Name);
            if (!string.IsNullOrWhiteSpace(byName))
                return byName;

            var slot = device?.SlotPosition;
            if (string.IsNullOrWhiteSpace(slot))
                return null;

            var digits = new string(slot.Where(char.IsDigit).ToArray());
            if (!int.TryParse(digits, out var slotIndex))
                return null;

            switch (slotIndex)
            {
                case 4:
                    return "Dev2";
                case 6:
                    return "Dev3";
                case 9:
                    return "Dev4";
                case 8:
                    return "Dev5";
                case 7:
                    return "Dev6";
                default:
                    return null;
            }
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

        public double? VoltageValue
        {
            get => _voltageValue;
            set => SetProperty(ref _voltageValue, value);
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
                    // 步骤1: 初始化硬件（供电+连接万用表）
                    AddLog("步骤1: 初始化硬件设备（28V供电，万用表）...");
                    await InitializeHardwareWithTimeoutAsync(token);
                    if (token.IsCancellationRequested) return;

                    AddLog("硬件初始化完成，请点击\"测量\"按钮进行电压测量");
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
                    AddLog("步骤1: 初始化硬件设备（28V供电，万用表）...");
                    await InitializeHardwareWithTimeoutAsync(token);
                    if (token.IsCancellationRequested) return;

                    // 步骤2: 测量电压
                    AddLog("步骤2: 测量CRM_PIN1-PIN18电压（+5V电源）");
                    await MeasureVoltageWithTimeoutAsync(token);
                    if (token.IsCancellationRequested) return;

                    // 步骤3: 评估结果
                    EvaluateOverallResult();
                    LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    AddLog($"测试完成，综合结果: {OverallResult}");

                    // 步骤4: 复位硬件
                    AddLog("步骤4: 复位硬件...");
                    await ResetHardwareAsync(token);
                    AddLog("硬件复位完成");
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
                }
                finally
                {
                    Application.Current?.Dispatcher?.Invoke(() => IsAutoTestRunning = false);
                }
            });
        }

        /// <summary>
        /// 停止测试
        /// </summary>
        private void StopTest()
        {
            _opCts?.Cancel();
            IsManualTestRunning = false;
            IsAutoTestRunning = false;
            AddLog("测试已停止，正在复位硬件...");

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
        /// 初始化硬件（带超时保护）
        /// 流程：配置矩阵开关 → 开启28V供电 → 连接万用表
        /// </summary>
        private async Task InitializeHardwareWithTimeoutAsync(CancellationToken token)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeoutCts.CancelAfter(DefaultTimeoutMs);

            try
            {
                if (_hardwareInitialized)
                {
                    AddLog("硬件已初始化，跳过");
                    return;
                }

                // ========== 步骤1：连接万用表 ==========
                AddLog($"正在连接万用表 {DmmIpAddress} ...");
                try
                {
                    await ConnectDmmAsync(DmmIpAddress, timeoutCts.Token);
                    AddLog("万用表连接成功");
                    _useSimulatedDmm = false;
                }
                catch (Exception ex)
                {
                    AddLog($"万用表连接异常: {ex.Message}，使用仿真模式");
                    _useSimulatedDmm = true;
                }

                await Initialize9774AiAsync(timeoutCts.Token);

                // ========== 步骤3：设置组件供电状态（28V供电状态） ==========
                AddLog("正在设置组件供电状态: 28V供电状态...");
                try
                {
                    if (_componentPowerStateApi != null)
                    {
                        await _componentPowerStateApi.ApplyComponent28VStateAsync(timeoutCts.Token);
                    }

                    await _simulation.ApplyComponent28VStateAsync(msg => AddLog(msg), timeoutCts.Token);
                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        IsPowerOn = true;
                        PowerStatus = "已上电";
                    });
                    AddLog("组件28V供电状态已设置");
                }
                catch (Exception ex)
                {
                    AddLog($"上电异常: {ex.Message}");
                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        IsPowerOn = false;
                        PowerStatus = "未上电";
                    });
                    throw;
                }

                _hardwareInitialized = true;
                AddLog("硬件初始化完成");
                UpdateCommandStates();
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !token.IsCancellationRequested)
            {
                throw new TimeoutException($"硬件初始化超时（{DefaultTimeoutMs}ms）");
            }
        }


        /// <summary>
        /// 复位硬件
        /// </summary>
        private async Task ResetHardwareAsync(CancellationToken token)
        {
            AddLog("正在复位硬件...");

            // 设置组件下电状态
            try
            {
                if (_componentPowerStateApi != null)
                {
                    await _componentPowerStateApi.ApplyComponentDownStateAsync(token);
                }

                await _simulation.ApplyComponentDownStateAsync(msg => AddLog(msg), token);
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    IsPowerOn = false;
                    PowerStatus = "未上电";
                });
            }
            catch (Exception ex)
            {
                AddLog($"关闭供电异常: {ex.Message}");
            }

            // 断开矩阵开关
            await DisconnectAllMatrixRoutesAsync();

            // 断开万用表
            if (_dmmSocket != null)
            {
                try
                {
                    if (_dmmSocket.IsConnected)
                        await _dmmSocket.DisconnectAsync(token);
                    AddLog("万用表已断开");
                }
                catch (Exception ex)
                {
                    AddLog($"断开万用表异常: {ex.Message}");
                }
            }

            if (_ai9774Api != null)
            {
                try
                {
                    await _ai9774Api.DisposeAsync();
                    AddLog("9774板卡已断开");
                }
                catch (Exception ex)
                {
                    AddLog($"断开9774异常: {ex.Message}");
                }
                finally
                {
                    _ai9774Api = null;
                }
            }


            _hardwareInitialized = false;
            UpdateCommandStates();
        }

        /// <summary>
        /// 测量电压（带超时保护）
        /// </summary>
        private async Task MeasureVoltageWithTimeoutAsync(CancellationToken token)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeoutCts.CancelAfter(DmmTimeoutMs);

            try
            {
                await MeasureVoltageAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !token.IsCancellationRequested)
            {
                throw new TimeoutException($"测量电压超时（{DmmTimeoutMs}ms）");
            }
        }

        /// <summary>
        /// 手动单点测量
        /// </summary>
        private async Task MeasureSinglePointAsync()
        {
            try
            {
                using var cts = new CancellationTokenSource(DmmTimeoutMs);
                await MeasureVoltageAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                AddLog("测量电压超时");
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    ReMessageBox.Show($"测量电压超时（{DmmTimeoutMs}ms）", "超时提示",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                });
            }
            catch (TimeoutException)
            {
                AddLog("测量电压超时");
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    ReMessageBox.Show($"测量电压超时（{DmmTimeoutMs}ms）", "超时提示",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                });
            }
            catch (Exception ex)
            {
                AddLog($"测量失败: {ex.Message}");
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    ReMessageBox.Show($"测量电压失败: {ex.Message}", "错误",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
        }

        /// <summary>
        /// 测量电压
        /// </summary>
        private async Task MeasureVoltageAsync(CancellationToken token = default)
        {
            IsBusy = true;
            try
            {
                AddLog("正在测量 CRM_PIN1-PIN18 电压（+5V电源）...");

                double voltage = await ReadVoltageAsync(token);
                string result = (voltage >= VoltageLowerLimit && voltage <= VoltageUpperLimit) ? "PASS" : "FAIL";

                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    VoltageValue = voltage;
                    TestResult = result;
                });

                AddLog($"电压测量值: {voltage:F3}V, 结果: {result}");

                if (IsManualTestRunning)
                {
                    EvaluateOverallResult();
                    LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    AddLog($"测试完成，综合结果: {OverallResult}");
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

        private const string DmmIpAddress = "192.168.1.13";
        private const string MatrixIpAddress = "192.168.1.3";

        /// <summary>[NoInlining] 隔离NI-VISA加载，防止JIT编译调用方时崩溃</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private async Task ConnectDmmAsync(string ipAddress, CancellationToken token)
        {
            _dmmSocket ??= new DmmSocketApi();
            if (!_dmmSocket.IsConnected)
                await _dmmSocket.ConnectAsync(ipAddress, token);
        }

        /// <summary>[NoInlining] 隔离NI-VISA加载，防止JIT编译调用方时崩溃</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private async Task<DmmReading> DmmReadVoltageAsync(CancellationToken token)
        {
            if (_dmmSocket == null || !_dmmSocket.IsConnected)
            {
                _dmmSocket ??= new DmmSocketApi();
                await _dmmSocket.ConnectAsync(DmmIpAddress, token);
            }
            return await _dmmSocket.ReadOnceAsync(
                DmmMeasureMode.DCV,
                new DmmReadOptions { TimeoutMilliseconds = DmmTimeoutMs },
                token);
        }
        private const int MatrixTcpPort = 50300;   // PXI-3022 使用50300端口
        private const int MatrixSlot = 2;          // 3022(1) slotindex=2

        // CRM_PIN1(+5V) 对 CRM_PIN18(GND)：AD采集1+/1- = 2槽 pin37/38
        private static readonly (string In, string Out) MatrixSig = ("I1", "O37");   // AD1+ → 万用表H
        private static readonly (string In, string Out) MatrixRet = ("I4", "O38");   // AD1- → 万用表L

        private async Task DisconnectAllMatrixRoutesAsync()
        {
            var matrix = MatrixControlService.Instance;
            try { await matrix.DisconnectNodesAsync(MatrixSig.In, MatrixSig.Out, MatrixSlot, MatrixIpAddress, MatrixTcpPort); } catch { }
            try { await matrix.DisconnectNodesAsync(MatrixRet.In, MatrixRet.Out, MatrixSlot, MatrixIpAddress, MatrixTcpPort); } catch { }
        }

        /// <summary>
        /// 从万用表读取电压值（带矩阵开关路由）
        /// </summary>
        private async Task<double> ReadVoltageFromDmmAsync(CancellationToken token = default)
        {
            if (_useSimulatedDmm)
                return await _simulation.SimulateMeasureVoltageAsync(token);

            await _measureLock.WaitAsync(token);
            try
            {
                var matrix = MatrixControlService.Instance;
                var ok1 = await matrix.ConnectNodesAsync(MatrixSig.In, MatrixSig.Out, MatrixSlot, MatrixIpAddress, MatrixTcpPort);
                var ok2 = await matrix.ConnectNodesAsync(MatrixRet.In, MatrixRet.Out, MatrixSlot, MatrixIpAddress, MatrixTcpPort);
                AddLog($"矩阵连接 {(ok1 && ok2 ? "OK" : "FAIL")} - {MatrixSig.In}-{MatrixSig.Out}, {MatrixRet.In}-{MatrixRet.Out} (slot{MatrixSlot})");

                if (!ok1 || !ok2)
                {
                    AddLog("矩阵通路连接失败，使用仿真测量");
                    _useSimulatedDmm = true;
                    return await _simulation.SimulateMeasureVoltageAsync(token);
                }

                try
                {
                    var reading = await DmmReadVoltageAsync(token);

                    if (reading?.Value != null)
                        return reading.Value.Value;

                    throw new InvalidOperationException($"万用表读数无效: {reading?.Raw}");
                }
                catch (Exception ex)
                {
                    AddLog($"万用表测量异常: {ex.Message}，使用仿真模式");
                    _useSimulatedDmm = true;
                    return await _simulation.SimulateMeasureVoltageAsync(token);
                }
            }
            finally
            {
                try { await DisconnectAllMatrixRoutesAsync(); } catch { }
                _measureLock.Release();
            }
        }

        #endregion

        #region 辅助方法

        private void ClearResults()
        {
            VoltageValue = null;
            TestResult = "--";
            OverallResult = "--";
            LastTestTime = "--";
        }

        private void EvaluateOverallResult()
        {
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                if (TestResult == "PASS")
                {
                    OverallResult = "PASS";
                }
                else if (TestResult == "FAIL")
                {
                    OverallResult = "FAIL";
                }
                else
                {
                    OverallResult = "--";
                }
            });
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
                MeasureCommand?.RaiseCanExecuteChanged();
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
            }
            catch { }
        }

        #endregion

        public void Dispose()
        {
            _opCts?.Cancel();
            _opCts?.Dispose();

            try
            {
                _ai9774Api?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            catch { }
            finally
            {
                _ai9774Api = null;
            }

            try
            {
                DisconnectAllMatrixRoutesAsync().GetAwaiter().GetResult();
            }
            catch { }

            try
            {
                if (_dmmSocket != null && _dmmSocket.IsConnected)
                    _dmmSocket.DisconnectAsync(CancellationToken.None).GetAwaiter().GetResult();
            }
            catch { }

            _measureLock?.Dispose();
            _simulation?.Dispose();

            if (_projectSavingToken != null)
            {
                _eventAggregator?.GetEvent<ProjectSavingEvent>()?.Unsubscribe(_projectSavingToken);
            }
        }
    }
}
