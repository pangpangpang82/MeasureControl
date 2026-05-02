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
using Prism.Ioc;
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
        private const int DefaultTimeoutMs = 15000;
        
        /// <summary>万用表测量超时时间（毫秒）</summary>
        private const int DmmTimeoutMs = 2000;

        /// <summary>28V上电后等待稳定时间（毫秒）</summary>
        private const int PowerStabilizeMs = 1000;

        private const string AiVoltageChannel = "AI1";

        /// <summary>第一个电源IP地址（组件28V供电）</summary>
        private const string PowerSupply1IpAddress = "192.168.1.15";
        /// <summary>组件28V供电电压（V）</summary>
        private const double ComponentVoltage = 28.0;
        /// <summary>组件28V供电电流限制（A）</summary>
        private const double ComponentCurrentLimit = 3.0;
        /// <summary>第二个电源IP地址（运放供电+15V）</summary>
        private const string PowerSupply2IpAddress = "192.168.1.16";
        /// <summary>第三个电源IP地址（DI上拉信号+15V）</summary>
        private const string PowerSupply3IpAddress = "192.168.1.17";
        /// <summary>运放供电电压（V）</summary>
        private const double OpAmpSupplyVoltage = 15.0;
        /// <summary>运放供电电流限制（A）</summary>
        private const double OpAmpSupplyCurrent = 1.0;

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

        #region 运放供电和DI上拉电源

        private IPowerSupplyApi _powerSupply1;  // 第一个电源（组件28V供电）
        private IPowerSupplyApi _powerSupply2;  // 第二个电源（运放供电+15V）
        private IPowerSupplyApi _powerSupply3;  // 第三个电源（DI上拉信号+15V）
        private bool _opAmpPowerOn;             // 运放供电是否已开启

        #endregion

        #region 状态字段

        private bool _hardwareInitialized;                                         // 硬件是否已初始化
        private CancellationTokenSource _opCts;                                    // 操作取消令牌源
        private SubscriptionToken _projectSavingToken;                             // 项目保存事件订阅令牌

        private bool _isManualTestRunning;                                         // 手动测试是否正在运行
        private bool _isAutoTestRunning;                                           // 自动测试是否正在运行
        private bool _isBusy;                                                      // 是否正在执行操作
        private bool _isPowerOn;                                                   // 28V供电是否已开启
        private bool _powerManagedExternally;                                      // 电源由外部托管，测试结束时不下电
        private bool _forceCleanupPowerOff;                                        // 异常时强制下电（即使外部托管）
        private bool _isManualTestInitializing;
        private bool _isAutoTestInitializing;
        private bool _isManualTestStopping;
        private bool _isAutoTestStopping;

        private bool _useSimulatedDmm;                                             // DMM不可用时走仿真测量
        private double? _scriptPowerVoltage;                                       // 脚本测试专用：覆盖 ComponentVoltage

        #endregion

        #region 测量结果字段

        private double? _voltageValue;        // 测量的电压值（单位：V）
        private string _testResult = "--";    // 测试结果（PASS/FAIL/--）
        private string _overallResult = "--"; // 综合结果
        private string _lastTestTime = "--";  // 上次测试时间
        private string _powerStatus = "已下电"; // 供电状态显示文本

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
            ManualTestCommand = new DelegateCommand(async () => await OnManualTestAsync());
            AutoTestCommand = new DelegateCommand(async () => await OnAutoTestAsync());
            MeasureCommand = new DelegateCommand(async () => await MeasureSinglePointAsync(), () => !IsBusy && IsManualTestRunning && _hardwareInitialized && IsPowerOn);
            ClearLogCommand = new DelegateCommand(() => Logs.Clear());

            // 加载上次保存的测试结果
            LoadPersistedState();
            
            // 订阅项目保存事件
            _projectSavingToken = _eventAggregator?.GetEvent<ProjectSavingEvent>()?.Subscribe(OnProjectSaving);
            try { var hps = ContainerLocator.Container.Resolve<IBoardPowerService>(); if (hps != null) hps.IsPoweredChanged += OnBoardPowerStateChanged; } catch { }
            RefreshPowerStateDisplay();
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
            AddLog("电压来源: 万用表");
            return dmmVoltage;
        }

        private static bool Is9774(DeviceBase device)
        {
            if (device == null) return false;
            if (device is AnalogAcquisitionDevice) return true;
            var model = (device.Model ?? string.Empty).ToUpperInvariant();
            var devType = (device.DeviceTypeName ?? string.Empty).ToUpperInvariant();
            return model.Contains("9774") || 
                   devType.Contains("模拟量输入") || 
                   devType.Contains("模拟量采集");
        }

        /// <summary>
        /// 从 PXI 机箱中查找第一个 PXIe-9774 板卡
        /// </summary>
        private DeviceBase FindFirst9774Device()
        {
            var chassisList = _pxiChassisService?.GetAllChassis();
            if (chassisList == null)
            {
                AddLog("[9774查找] 机箱列表为null");
                return null;
            }

            foreach (var chassis in chassisList)
            {
                if (chassis?.Devices == null)
                    continue;

                // 直接在机箱设备列表中查找
                var device = chassis.Devices.FirstOrDefault(d => Is9774(d));
                if (device != null)
                {
                    AddLog($"[9774查找] 找到板卡: Name={device.Name}, Model={device.Model}");
                    return device;
                }

                // 遍历子设备
                foreach (var d in chassis.Devices)
                {
                    if (d?.Children == null)
                        continue;

                    var childDevice = d.Children.FirstOrDefault(c => Is9774(c));
                    if (childDevice != null)
                    {
                        AddLog($"[9774查找] 找到板卡: Name={childDevice.Name}, Model={childDevice.Model}");
                        return childDevice;
                    }
                }
            }

            AddLog("[9774查找] 未找到9774板卡");
            return null;
        }

        private async Task Initialize9774AiAsync(CancellationToken token)
        {
            if (_ai9774Api != null && _ai9774Api.IsConnected)
            {
                AddLog("9774板卡已连接，跳过");
                return;
            }

            var device = FindFirst9774Device();
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

            AddLog("手动测试开始，正在初始化硬件...");
            try
            {
                await InitializeHardwareWithTimeoutAsync(_opCts.Token).ConfigureAwait(false);
                IsManualTestInitializing = false;
                IsManualTestRunning = true;
                AddLog("硬件初始化完成，请点击\"测量\"按鈕进行电压测量");
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
                RaisePropertyChanged(nameof(CanStartManualTest));
                RaisePropertyChanged(nameof(CanStartAutoTest));
                AddLog("自动测试已结束");
            }
        }

        /// <summary>
        /// 供外部（整板自动测试）调用的异步测试方法
        /// </summary>
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

        /// <summary>脚本测试专用：在指定电压上电，测量+5V电压后下电，返回测量值。</summary>
        public async Task<double?> RunWithScriptVoltageAsync(double powerVoltage, CancellationToken cancellationToken)
        {
            _forceCleanupPowerOff = true;
            _scriptPowerVoltage = powerVoltage;
            try
            {
                await RunOnceAsync(cancellationToken).ConfigureAwait(false);
                return VoltageValue;
            }
            finally
            {
                _scriptPowerVoltage = null;
            }
        }

        private async Task<string> ExecuteAutoTestAsync(CancellationToken token)
        {
            AddLog("自动测试开始");

            AddLog("步骤1: 初始化硬件设备（28V供电，万用表）...");
            await InitializeHardwareWithTimeoutAsync(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();

            // 初始化成功，切换到运行状态
            IsAutoTestInitializing = false;
            IsAutoTestRunning = true;

            AddLog("步骤2: 测量CRM_PIN1-PIN18电压（+5V电源）");
            await MeasureVoltageWithTimeoutAsync(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();

            EvaluateOverallResult();
            LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            AddLog($"测试完成，综合结果: {OverallResult}");

            await StopAutoTestAsync().ConfigureAwait(false);
            return OverallResult ?? "--";
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
                    AddLog($"万用表连接异常: {ex.Message}");
                    throw;
                }

                await Initialize9774AiAsync(timeoutCts.Token);

                // ========== 步骤2：开启.16 CH3 +15V（运放供电） ==========
                await InitPowerSupply2Async(timeoutCts.Token);
                if (_powerSupply2 == null)
                    throw new InvalidOperationException("电源2（运放供电）连接失败，请检查192.168.1.16");

                // ========== 步骤3：开启.17 CH3 +15V（DI上拉） ==========
                await InitPowerSupply3Async(timeoutCts.Token);
                if (_powerSupply3 == null)
                    throw new InvalidOperationException("电源3（DI上拉）连接失败，请检查192.168.1.17");

                _opAmpPowerOn = true;

                // ========== 步骤4：开启CH1 28V供电（如果外部已上电则跳过） ==========
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
                    AddLog($"正在连接{PowerSupply1IpAddress}，开启CH1 28V供电...");
                    _powerSupply1 ??= new PowerSupplySocketApi();
                    if (!_powerSupply1.IsConnected)
                        await _powerSupply1.ConnectAsync(PowerSupply1IpAddress, timeoutCts.Token);
                    double applyVoltage = _scriptPowerVoltage ?? ComponentVoltage;
                    await _powerSupply1.ApplyAsync(PowerSupplyChannel.CH1, applyVoltage, ComponentCurrentLimit, timeoutCts.Token);
                    await _powerSupply1.SetOutputEnabledAsync(PowerSupplyChannel.CH1, true, timeoutCts.Token);
                    AddLog($"{PowerSupply1IpAddress} CH1 {applyVoltage:F0}V已开启");
                    hps?.SetPoweredState(true, "加放油单板", applyVoltage);
                    Application.Current?.Dispatcher?.Invoke(() => { IsPowerOn = true; PowerStatus = "已上电"; });
                }

                // 等待电源稳定
                AddLog($"等待电源稳定（{PowerStabilizeMs}ms）...");
                await Task.Delay(PowerStabilizeMs, timeoutCts.Token);

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
            AddLog("正在复位硬件（反序断开）...");

            // 步骤1: 断开矩阵开关
            await DisconnectAllMatrixRoutesAsync();

            // 步骤2: 关闭192.168.1.15 CH1并断开（外部托管时跳过下电）
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
                PowerStatus = "已下电";
            });

            // 步骤3: 关闭.17、.16电源（反序）
            await ShutdownOpAmpAndDiPullUpPowerAsync();

            // 步骤4: 断开9774板卡
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

            // 步骤5: 断开万用表
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

            _hardwareInitialized = false;
            UpdateCommandStates();
            AddLog("硬件复位完成");
            RefreshPowerStateDisplay();
        }

        /// <summary>
        /// 初始化运放供电（+15V）和DI上拉信号（+15V）
        /// 通过第二个电源（192.168.1.16）的CH3提供运放供电
        /// 通过第三个电源（192.168.1.17）的CH3提供DI上拉信号
        /// </summary>
        private async Task InitializeOpAmpAndDiPullUpPowerAsync(CancellationToken token)
        {
            AddLog("正在初始化运放供电和DI上拉信号（+15V）...");

            // 并行连接两路电源：同时发起TCP连接，避免顺序失败（ARP/TCP预热问题）
            await Task.WhenAll(
                InitPowerSupply2Async(token),
                InitPowerSupply3Async(token));

            _opAmpPowerOn = (_powerSupply2 != null) && (_powerSupply3 != null);
            if (!_opAmpPowerOn)
                throw new InvalidOperationException(
                    $"运放供电和DI上拉信号初始化失败（电源2:{(_powerSupply2 != null ? "OK" : "失败")}, 电源3:{(_powerSupply3 != null ? "OK" : "失败")}）");
            AddLog("运放供电和DI上拉信号初始化完成");
        }

        private async Task InitPowerSupply2Async(CancellationToken token)
        {
            try
            {
                AddLog($"正在连接电源2（运放供电）{PowerSupply2IpAddress}...");
                var ps = new PowerSupplySocketApi();
                await ps.ConnectAsync(PowerSupply2IpAddress, token);
                await ps.ApplyAsync(PowerSupplyChannel.CH3, OpAmpSupplyVoltage, OpAmpSupplyCurrent, token);
                await ps.SetOutputEnabledAsync(PowerSupplyChannel.CH3, true, token);
                AddLog($"电源2 CH3已配置: +{OpAmpSupplyVoltage:F1}V（运放供电）");
                _powerSupply2 = ps;
            }
            catch (Exception ex)
            {
                AddLog($"电源2连接失败: {ex.Message}");
                _powerSupply2 = null;
            }
        }

        private async Task InitPowerSupply3Async(CancellationToken token)
        {
            try
            {
                AddLog($"正在连接电源3（DI上拉）{PowerSupply3IpAddress}...");
                var ps = new PowerSupplySocketApi();
                await ps.ConnectAsync(PowerSupply3IpAddress, token);
                await ps.ApplyAsync(PowerSupplyChannel.CH3, OpAmpSupplyVoltage, OpAmpSupplyCurrent, token);
                await ps.SetOutputEnabledAsync(PowerSupplyChannel.CH3, true, token);
                AddLog($"电源3 CH3已配置: +{OpAmpSupplyVoltage:F1}V（DI上拉信号）");
                _powerSupply3 = ps;
            }
            catch (Exception ex)
            {
                AddLog($"电源3连接失败: {ex.Message}");
                _powerSupply3 = null;
            }
        }

        /// <summary>
        /// 关闭运放供电和DI上拉电源
        /// </summary>
        private async Task ShutdownOpAmpAndDiPullUpPowerAsync()
        {
            // 反序断开：启动顺序.16→.17，停止顺序.17→.16
            if (_powerSupply3 != null)
            {
                try
                {
                    if (_powerSupply3.IsConnected)
                    {
                        await _powerSupply3.SetOutputEnabledAsync(PowerSupplyChannel.CH3, false);
                        AddLog("电源3 CH3已关闭（DI上拉信号）");
                    }
                }
                catch (Exception ex)
                {
                    AddLog($"关闭电源3异常: {ex.Message}");
                }
                finally
                {
                    try { await _powerSupply3.DisposeAsync(); } catch { }
                    _powerSupply3 = null;
                }
            }

            if (_powerSupply2 != null)
            {
                try
                {
                    if (_powerSupply2.IsConnected)
                    {
                        await _powerSupply2.SetOutputEnabledAsync(PowerSupplyChannel.CH3, false);
                        AddLog("电源2 CH3已关闭（运放供电）");
                    }
                }
                catch (Exception ex)
                {
                    AddLog($"关闭电源2异常: {ex.Message}");
                }
                finally
                {
                    try { await _powerSupply2.DisposeAsync(); } catch { }
                    _powerSupply2 = null;
                }
            }

            _opAmpPowerOn = false;
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
        private const int MatrixSlotSig = 9;     // 2601(5) slotindex=9，信号侧（AD采集）
        private const int MatrixSlotDmm = 4;     // 2601(1) slotindex=4，万用表侧

        // CRM_PIN1(+5V) AD采集1：2601(5) 0/0 → 信号侧 I1,O0,slot9
        // 万用表H侧：2601(1) 4/7 → I3,O7,slot4
        private static readonly (string In, string Out, int Slot) MatrixSig = ("I1", "O0",  MatrixSlotSig);
        private static readonly (string In, string Out, int Slot) MatrixDmmH = ("I3", "O7",  MatrixSlotDmm);

        private async Task DisconnectAllMatrixRoutesAsync()
        {
            var matrix = MatrixControlService.Instance;
            try { await matrix.DisconnectNodesAsync(MatrixSig.In,  MatrixSig.Out,  MatrixSig.Slot,  MatrixIpAddress); } catch { }
            try { await matrix.DisconnectNodesAsync(MatrixDmmH.In, MatrixDmmH.Out, MatrixDmmH.Slot, MatrixIpAddress); } catch { }
        }

        /// <summary>
        /// 从万用表读取电压值（带矩阵开关路由）
        /// </summary>
        private async Task<double> ReadVoltageFromDmmAsync(CancellationToken token = default)
        {
            if (_useSimulatedDmm)
                throw new InvalidOperationException("万用表未就绪，无法执行真实电压测量");

            await _measureLock.WaitAsync(token);
            try
            {
                var matrix = MatrixControlService.Instance;
                var ok1 = await matrix.ConnectNodesAsync(MatrixSig.In,  MatrixSig.Out,  MatrixSig.Slot,  MatrixIpAddress);
                var ok2 = await matrix.ConnectNodesAsync(MatrixDmmH.In, MatrixDmmH.Out, MatrixDmmH.Slot, MatrixIpAddress);
                AddLog($"矩阵连接 {(ok1 && ok2 ? "OK" : "FAIL")} - SIG:{MatrixSig.In}-{MatrixSig.Out}(slot{MatrixSig.Slot}), DMM:{MatrixDmmH.In}-{MatrixDmmH.Out}(slot{MatrixDmmH.Slot})");

                if (!ok1 || !ok2)
                {
                    throw new InvalidOperationException("矩阵通路连接失败，无法执行真实电压测量");
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
                    PowerStatus = "已下电";
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
                    PowerStatus = "已下电";
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
                PowerStatus = isFuelPowered ? "已上电" : "已下电";
            });
        }

        private void OnBoardPowerStateChanged(object sender, EventArgs e)
        {
            if (!IsManualTestRunning && !IsAutoTestRunning)
                RefreshPowerStateDisplay();
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
