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
    /// 低电压告警功能测试 ViewModel (LowVoltageAlarmTestViewModel)
    /// ============================================================================
    /// 
    /// 【测试目的】
    /// 验证加放油控制器的低电压告警功能是否正常。
    /// 在供电电压从17V逐步降低的过程中，监测CRM_PIN3的电平状态，
    /// 确认在供电电压低于15V之前电平发生翻转。
    /// 
    /// 【测试流程概述】
    /// ┌─────────────────────────────────────────────────────────────────┐
    /// │  步骤1: 初始化硬件                                               │
    /// │    ├── 配置矩阵开关通路（连接9774 AD采集）                        │
    /// │    ├── 连接9774板卡（用于电平监测）                              │
    /// │    └── 连接程控电源（用于可调供电）                              │
    /// ├─────────────────────────────────────────────────────────────────┤
    /// │  步骤2: 设置初始供电电压17V                                      │
    /// ├─────────────────────────────────────────────────────────────────┤
    /// │  步骤3: 梯度降压测试                                             │
    /// │    ├── 以0.2V步长递减供电电压                                    │
    /// │    ├── 每次降压后读取CRM_PIN3电平                                │
    /// │    └── 记录电平翻转时的电压值                                    │
    /// ├─────────────────────────────────────────────────────────────────┤
    /// │  步骤4: 判定结果                                                 │
    /// │    └── 电平翻转发生在15V之前为PASS                               │
    /// ├─────────────────────────────────────────────────────────────────┤
    /// │  步骤5: 复位硬件                                                 │
    /// │    ├── 关闭供电                                                  │
    /// │    └── 断开矩阵开关通路                                          │
    /// └─────────────────────────────────────────────────────────────────┘
    /// 
    /// 【供电说明】
    /// - 使用程控电源提供可调电压（17V~12V）
    /// - 以0.2V步长递减
    /// 
    /// 【测量点说明】
    /// - CRM_PIN3: 低电压告警输出信号（对应INT_AD2）
    /// - 通过9774板卡AD采集通道监测电平
    /// 
    /// 【硬件依赖】
    /// - 9774板卡：AD采集，监测电平
    /// - 程控电源：提供可调供电
    /// - 矩阵开关：配置信号通路
    /// 
    /// 【超时保护】
    /// 所有硬件操作都有超时保护，超时后会弹出提示框，不会导致程序卡死
    /// </summary>
    public class LowVoltageAlarmTestViewModel : BindableBase, IDisposable
    {
        #region 常量定义

        /// <summary>测试项唯一标识，用于数据持久化</summary>
        private const string TestItemKey = "FuelController_LowVoltageAlarm";
        
        /// <summary>起始电压（V）</summary>
        private const double StartVoltage = 17.0;
        
        /// <summary>结束电压（V）</summary>
        private const double EndVoltage = 12.0;
        
        /// <summary>电压递减步长（V）</summary>
        private const double VoltageStep = 0.2;
        
        /// <summary>告警阈值电压（V）- 电平应在此电压之前翻转</summary>
        private const double AlarmThresholdVoltage = 15.0;
        
        /// <summary>硬件初始化默认超时时间（毫秒）</summary>
        private const int DefaultTimeoutMs = 3000;
        
        /// <summary>单步测量超时时间（毫秒）</summary>
        private const int StepTimeoutMs = 2000;

        /// <summary>AD采集通道（INT_AD2对应的通道）</summary>
        private const string AdChannel = "AI2";

        #endregion

        #region 依赖服务

        private readonly ISingleBoardTestContextService _singleBoardTestContext;
        private readonly ProjectService _projectService;
        private readonly IEventAggregator _eventAggregator;
        private readonly IComponentPowerStateApi _componentPowerStateApi;
        private readonly IPxiChassisService _pxiChassisService;
        private readonly LowVoltageAlarmSimulation _simulation;

        #endregion

        #region 9774板卡

        private IArt9774AiApi _ai9774Api;

        #endregion

        #region 状态字段

        private bool _hardwareInitialized;
        private CancellationTokenSource _opCts;
        private SubscriptionToken _projectSavingToken;

        private bool _isManualTestRunning;
        private bool _isAutoTestRunning;
        private bool _isBusy;
        private bool _isPowerOn;

        #endregion

        #region 测量结果字段

        private double _currentVoltage;           // 当前供电电压
        private bool _currentPinLevel;            // 当前CRM_PIN3电平
        private double? _flipVoltage;             // 电平翻转时的电压
        private string _testResult = "--";        // 测试结果（PASS/FAIL/--）
        private string _overallResult = "--";     // 综合结果
        private string _lastTestTime = "--";      // 上次测试时间
        private string _powerStatus = "未上电";   // 供电状态显示文本
        private int _testProgress;                // 测试进度（0-100）

        #endregion

        #region 测试数据记录

        /// <summary>
        /// 测试过程中的电压-电平记录
        /// </summary>
        public ObservableCollection<VoltageAlarmRecord> TestRecords { get; } = new ObservableCollection<VoltageAlarmRecord>();

        #endregion

        #region 构造函数

        public LowVoltageAlarmTestViewModel(
            ISingleBoardTestContextService singleBoardTestContext,
            ProjectService projectService,
            IEventAggregator eventAggregator,
            IComponentPowerStateApi componentPowerStateApi,
            IPxiChassisService pxiChassisService)
        {
            _singleBoardTestContext = singleBoardTestContext;
            _projectService = projectService;
            _eventAggregator = eventAggregator;
            _componentPowerStateApi = componentPowerStateApi;
            _pxiChassisService = pxiChassisService;
            _simulation = new LowVoltageAlarmSimulation();

            // 初始化命令
            ManualTestCommand = new DelegateCommand(OnManualTest);
            AutoTestCommand = new DelegateCommand(OnAutoTest);
            StepDownCommand = new DelegateCommand(async () => await StepDownVoltageAsync(), () => !IsBusy && IsManualTestRunning && _hardwareInitialized && IsPowerOn);
            ClearLogCommand = new DelegateCommand(() => Logs.Clear());

            // 加载上次保存的测试结果
            LoadPersistedState();
            
            // 订阅项目保存事件
            _projectSavingToken = _eventAggregator?.GetEvent<ProjectSavingEvent>()?.Subscribe(OnProjectSaving);
        }

        #endregion

        #region 公共属性

        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();

        public bool IsManualTestRunning
        {
            get => _isManualTestRunning;
            set
            {
                if (SetProperty(ref _isManualTestRunning, value))
                {
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
                    UpdateCommandStates();
                }
            }
        }

        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                if (SetProperty(ref _isBusy, value))
                {
                    UpdateCommandStates();
                }
            }
        }

        public bool IsPowerOn
        {
            get => _isPowerOn;
            set
            {
                if (SetProperty(ref _isPowerOn, value))
                {
                    UpdateCommandStates();
                }
            }
        }

        public double CurrentVoltage
        {
            get => _currentVoltage;
            set => SetProperty(ref _currentVoltage, value);
        }

        public bool CurrentPinLevel
        {
            get => _currentPinLevel;
            set => SetProperty(ref _currentPinLevel, value);
        }

        public double? FlipVoltage
        {
            get => _flipVoltage;
            set => SetProperty(ref _flipVoltage, value);
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

        public string PowerStatus
        {
            get => _powerStatus;
            set => SetProperty(ref _powerStatus, value);
        }

        public int TestProgress
        {
            get => _testProgress;
            set => SetProperty(ref _testProgress, value);
        }

        #endregion

        #region 命令

        public DelegateCommand ManualTestCommand { get; }
        public DelegateCommand AutoTestCommand { get; }
        public DelegateCommand StepDownCommand { get; }
        public DelegateCommand ClearLogCommand { get; }

        #endregion

        #region 命令处理

        private void OnManualTest()
        {
            if (IsManualTestRunning)
            {
                StopManualTest();
            }
            else
            {
                StartManualTest();
            }
        }

        private void OnAutoTest()
        {
            if (IsAutoTestRunning)
            {
                StopAutoTest();
            }
            else
            {
                StartAutoTest();
            }
        }

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
                    AddLog("步骤1: 初始化硬件设备...");
                    await InitializeHardwareWithTimeoutAsync(token);
                    if (token.IsCancellationRequested) return;

                    AddLog("步骤2: 设置初始供电电压17V...");
                    await SetInitialVoltageAsync(token);
                    if (token.IsCancellationRequested) return;

                    AddLog("硬件初始化完成，请点击\"降压\"按钮逐步降低电压");
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

        private void StopManualTest()
        {
            _opCts?.Cancel();
            AddLog("正在停止手动测试...");

            Task.Run(async () =>
            {
                try
                {
                    await ResetHardwareAsync(CancellationToken.None);
                }
                catch (Exception ex)
                {
                    AddLog($"停止测试时复位硬件失败: {ex.Message}");
                }
                finally
                {
                    Application.Current?.Dispatcher?.Invoke(() => IsManualTestRunning = false);
                }
            });
        }

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
                    AddLog("步骤1: 初始化硬件设备...");
                    await InitializeHardwareWithTimeoutAsync(token);
                    if (token.IsCancellationRequested) return;

                    AddLog("步骤2: 设置初始供电电压17V...");
                    await SetInitialVoltageAsync(token);
                    if (token.IsCancellationRequested) return;

                    AddLog("步骤3: 开始梯度降压测试...");
                    await RunGradientTestAsync(token);
                    if (token.IsCancellationRequested) return;

                    AddLog("步骤4: 判定测试结果...");
                    EvaluateTestResult();

                    AddLog("步骤5: 复位硬件...");
                    await ResetHardwareAsync(token);

                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                        IsAutoTestRunning = false;
                    });

                    AddLog($"自动测试完成，结果: {TestResult}");
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
                    Application.Current?.Dispatcher?.Invoke(() => IsAutoTestRunning = false);
                }
                catch (OperationCanceledException)
                {
                    AddLog("测试已取消");
                    try { await ResetHardwareAsync(CancellationToken.None); } catch { }
                    Application.Current?.Dispatcher?.Invoke(() => IsAutoTestRunning = false);
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
                    Application.Current?.Dispatcher?.Invoke(() => IsAutoTestRunning = false);
                }
            });
        }

        private void StopAutoTest()
        {
            _opCts?.Cancel();
            AddLog("正在停止自动测试...");

            Task.Run(async () =>
            {
                try
                {
                    await ResetHardwareAsync(CancellationToken.None);
                }
                catch (Exception ex)
                {
                    AddLog($"停止测试时复位硬件失败: {ex.Message}");
                }
                finally
                {
                    Application.Current?.Dispatcher?.Invoke(() => IsAutoTestRunning = false);
                }
            });
        }

        #endregion

        #region 硬件操作方法

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

                // 步骤1：配置矩阵开关通路
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

                // 步骤2：初始化9774板卡（用于AD采集）
                await Initialize9774AiAsync(timeoutCts.Token);

                // 步骤3：设置组件供电状态（28V供电状态）
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
                AddLog("未找到9774板卡，将使用仿真模式");
                return;
            }

            try
            {
                string devName = InferDeviceName(device);
                AddLog($"正在连接9774板卡: {devName}...");

                _ai9774Api = new Art9774Api(device, new AiAcquisitionOptions
                {
                    Mode = AiAcquisitionMode.Continuous,
                    SampleRateHz = 10000.0,
                    SamplesPerChannel = 1000,
                    DeviceName = string.IsNullOrWhiteSpace(devName) ? "Dev3" : devName
                });
                await _ai9774Api.ConnectAsync(token);
                await _ai9774Api.ConfigureChannelsAsync(new[]
                {
                    new AiChannelConfig { Channel = AdChannel, Enabled = true, Range = AiInputRange.PlusMinus10V }
                }, token);
                AddLog("9774板卡连接成功");

                await _ai9774Api.StartAsync(token);
                AddLog("9774板卡采集已启动");
            }
            catch (Exception ex)
            {
                AddLog($"9774板卡初始化失败: {ex.Message}，将使用仿真模式");
                _ai9774Api = null;
            }
        }

        private DeviceBase Find9774DeviceInChassis(string chassisName)
        {
            DeviceBase Walk(DeviceBase d)
            {
                var model = (d?.Model ?? string.Empty).ToUpperInvariant();
                if (model.Contains("9774") || model.Contains("PXIE-9774") || model.Contains("PXI-9774"))
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

            if (!string.IsNullOrWhiteSpace(chassisName))
            {
                var devices = _pxiChassisService?.GetChassisDevices(chassisName);
                if (devices != null && devices.Count > 0)
                {
                    foreach (var d in devices)
                    {
                        var found = Walk(d);
                        if (found != null)
                            return found;
                    }
                }
            }

            var chassisList = _pxiChassisService?.GetAllChassis();
            if (chassisList == null || chassisList.Count == 0)
                return null;

            foreach (var chassis in chassisList)
            {
                if (chassis == null)
                    continue;

                System.Collections.Generic.IList<DeviceBase> devices = chassis.Devices;
                if (devices == null || devices.Count == 0)
                {
                    if (!string.IsNullOrWhiteSpace(chassis.Name))
                    {
                        devices = _pxiChassisService?.GetChassisDevices(chassis.Name);
                    }
                }

                if (devices == null || devices.Count == 0)
                    continue;

                foreach (var d in devices)
                {
                    var found = Walk(d);
                    if (found != null)
                        return found;
                }
            }

            return null;
        }

        private string InferDeviceName(DeviceBase device)
        {
            // 尝试从CardName或Name中提取DevX
            var cardName = device?.GetType().GetProperty("CardName")?.GetValue(device) as string;
            if (!string.IsNullOrEmpty(cardName) && cardName.StartsWith("Dev", StringComparison.OrdinalIgnoreCase))
                return cardName;

            var name = device?.Name;
            if (!string.IsNullOrEmpty(name) && name.StartsWith("Dev", StringComparison.OrdinalIgnoreCase))
                return name;

            // 优先使用 SlotIndex（PxiDeviceBase 子类）
            if (device is MeasureControl.Models.Devices.DeviceCategories.PxiDeviceBase pxi && pxi.SlotIndex > 0)
                return $"Dev{pxi.SlotIndex}";

            // 根据SlotPosition推断，格式为 "Slot N" 或纯数字
            var slot = device?.SlotPosition;
            if (!string.IsNullOrWhiteSpace(slot))
            {
                var trimmed = slot.Replace("Slot", "").Replace("slot", "").Trim();
                if (int.TryParse(trimmed, out var slotNum) && slotNum > 0)
                    return $"Dev{slotNum}";
            }

            return "Dev3"; // 默认值
        }

        private async Task SetInitialVoltageAsync(CancellationToken token)
        {
            await _simulation.SetSupplyVoltageAsync(StartVoltage, msg => AddLog(msg), token);
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                CurrentVoltage = StartVoltage;
                TestProgress = 0;
            });

            // 读取初始电平
            var level = await ReadPinLevelAsync(token);
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                CurrentPinLevel = level;
                AddTestRecord(StartVoltage, level);
            });
            AddLog($"初始电平: {(level ? "高" : "低")}");
        }

        private async Task StepDownVoltageAsync()
        {
            if (IsBusy || !IsManualTestRunning || !_hardwareInitialized)
                return;

            IsBusy = true;
            try
            {
                var token = _opCts?.Token ?? CancellationToken.None;

                double newVoltage = CurrentVoltage - VoltageStep;
                if (newVoltage < EndVoltage)
                {
                    AddLog("已达到最低电压，测试完成");
                    EvaluateTestResult();
                    return;
                }

                await _simulation.SetSupplyVoltageAsync(newVoltage, msg => AddLog(msg), token);
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    CurrentVoltage = newVoltage;
                    TestProgress = (int)((StartVoltage - newVoltage) / (StartVoltage - EndVoltage) * 100);
                });

                // 读取电平
                bool previousLevel = CurrentPinLevel;
                var level = await ReadPinLevelAsync(token);
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    CurrentPinLevel = level;
                    AddTestRecord(newVoltage, level);
                });

                // 检测电平翻转
                if (previousLevel != level && FlipVoltage == null)
                {
                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        FlipVoltage = newVoltage;
                    });
                    AddLog($"*** 电平翻转检测到！翻转电压: {newVoltage:F1}V ***");
                }

                AddLog($"电压: {newVoltage:F1}V, 电平: {(level ? "高" : "低")}");

                // 检查是否已达到最低电压
                if (newVoltage <= EndVoltage)
                {
                    AddLog("已达到最低电压，测试完成");
                    EvaluateTestResult();
                }
            }
            catch (Exception ex)
            {
                AddLog($"降压测试异常: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task RunGradientTestAsync(CancellationToken token)
        {
            bool previousLevel = CurrentPinLevel;
            double voltage = StartVoltage;

            while (voltage > EndVoltage && !token.IsCancellationRequested)
            {
                voltage -= VoltageStep;
                if (voltage < EndVoltage)
                    voltage = EndVoltage;

                await _simulation.SetSupplyVoltageAsync(voltage, msg => AddLog(msg), token);
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    CurrentVoltage = voltage;
                    TestProgress = (int)((StartVoltage - voltage) / (StartVoltage - EndVoltage) * 100);
                });

                // 等待电压稳定
                await Task.Delay(200, token);

                // 读取电平
                var level = await ReadPinLevelAsync(token);
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    CurrentPinLevel = level;
                    AddTestRecord(voltage, level);
                });

                // 检测电平翻转
                if (previousLevel != level && FlipVoltage == null)
                {
                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        FlipVoltage = voltage;
                    });
                    AddLog($"*** 电平翻转检测到！翻转电压: {voltage:F1}V ***");
                }

                previousLevel = level;
                AddLog($"电压: {voltage:F1}V, 电平: {(level ? "高" : "低")}");
            }

            Application.Current?.Dispatcher?.Invoke(() => TestProgress = 100);
        }

        private async Task<bool> ReadPinLevelAsync(CancellationToken token)
        {
            // 优先使用9774板卡
            if (_ai9774Api != null && _ai9774Api.IsConnected)
            {
                try
                {
                    var adVoltage = await _ai9774Api.GetLastValueAsync(AdChannel, token);
                    // 电压大于1.5V认为是高电平
                    return adVoltage > 1.5;
                }
                catch (Exception ex)
                {
                    AddLog($"9774读取异常: {ex.Message}，使用仿真");
                }
            }

            // 使用仿真
            return await _simulation.ReadPinLevelAsync(token);
        }

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
                    CurrentVoltage = 0;
                });
            }
            catch (Exception ex)
            {
                AddLog($"关闭供电异常: {ex.Message}");
            }

            // 断开9774板卡
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

            // 断开矩阵开关
            try
            {
                await _simulation.DisconnectMatrixAsync(msg => AddLog(msg), token);
            }
            catch (Exception ex)
            {
                AddLog($"断开矩阵开关异常: {ex.Message}");
            }

            _hardwareInitialized = false;
            UpdateCommandStates();
        }

        #endregion

        #region 结果判定

        private void EvaluateTestResult()
        {
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                if (FlipVoltage.HasValue)
                {
                    // 电平翻转发生在15V之前为PASS
                    if (FlipVoltage.Value > AlarmThresholdVoltage)
                    {
                        TestResult = "PASS";
                        OverallResult = "PASS";
                        AddLog($"测试通过：电平在 {FlipVoltage.Value:F1}V 时翻转（阈值 {AlarmThresholdVoltage}V）");
                    }
                    else
                    {
                        TestResult = "FAIL";
                        OverallResult = "FAIL";
                        AddLog($"测试失败：电平在 {FlipVoltage.Value:F1}V 时翻转，晚于阈值 {AlarmThresholdVoltage}V");
                    }
                }
                else
                {
                    TestResult = "FAIL";
                    OverallResult = "FAIL";
                    AddLog("测试失败：未检测到电平翻转");
                }

                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            });
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

        private void AddTestRecord(double voltage, bool level)
        {
            TestRecords.Add(new VoltageAlarmRecord
            {
                Voltage = voltage,
                Level = level,
                LevelText = level ? "高" : "低",
                Timestamp = DateTime.Now
            });
        }

        private void ClearResults()
        {
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                CurrentVoltage = 0;
                CurrentPinLevel = false;
                FlipVoltage = null;
                TestResult = "--";
                TestProgress = 0;
                TestRecords.Clear();
            });
        }

        private void UpdateCommandStates()
        {
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                (StepDownCommand as DelegateCommand)?.RaiseCanExecuteChanged();
            });
        }

        #endregion

        #region 数据持久化

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

                LastTestTime = Read("LastTestTime") ?? "--";
                OverallResult = Read("OverallResult") ?? "--";
                TestResult = Read("TestResult") ?? "--";
                var flipVStr = Read("FlipVoltage");
                if (!string.IsNullOrEmpty(flipVStr) && double.TryParse(flipVStr, out var flipV))
                    FlipVoltage = flipV;

                RaisePropertyChanged(nameof(LastTestTime));
                RaisePropertyChanged(nameof(OverallResult));
                RaisePropertyChanged(nameof(TestResult));
                RaisePropertyChanged(nameof(FlipVoltage));
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
                Upsert("FlipVoltage", FlipVoltage?.ToString() ?? string.Empty);
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
                _ai9774Api?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            catch { }

            _simulation?.Dispose();

            if (_projectSavingToken != null)
            {
                _eventAggregator?.GetEvent<ProjectSavingEvent>()?.Unsubscribe(_projectSavingToken);
            }
        }

        #endregion
    }

    /// <summary>
    /// 电压-告警记录
    /// </summary>
    public class VoltageAlarmRecord
    {
        public double Voltage { get; set; }
        public bool Level { get; set; }
        public string LevelText { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
