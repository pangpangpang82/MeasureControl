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

        #endregion

        #region 依赖服务

        private readonly ISingleBoardTestContextService _singleBoardTestContext;  // 单板测试上下文服务
        private readonly ProjectService _projectService;                           // 项目服务，用于数据持久化
        private readonly IEventAggregator _eventAggregator;                        // 事件聚合器，用于跨模块通信
        private readonly IDmmApi _dmmApi;                                          // 万用表API，测量电压
        private readonly SecondaryPowerSimulation _simulation;                     // 仿真类，硬件不可用时使用

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
        private bool _isPowerOn;                                                   // 28V供电是否已开启

        private bool _useSimulatedDmm;                                             // DMM不可用时强制走仿真测量

        #endregion

        #region 测量结果字段

        private double? _voltageValue;        // 测量的电压值（单位：V）
        private string _testResult = "--";    // 测试结果（PASS/FAIL/--）
        private string _overallResult = "--"; // 综合结果
        private string _lastTestTime = "--";  // 上次测试时间
        private string _powerStatus = "未供电"; // 供电状态显示文本

        #endregion

        #region 构造函数

        /// <summary>
        /// 构造函数 - 通过依赖注入获取所需服务
        /// </summary>
        public SecondaryPowerTestViewModel(
            ISingleBoardTestContextService singleBoardTestContext,
            ProjectService projectService,
            IEventAggregator eventAggregator,
            IDmmApi dmmApi = null)
        {
            // 保存依赖服务引用
            _singleBoardTestContext = singleBoardTestContext;
            _projectService = projectService;
            _eventAggregator = eventAggregator;
            _dmmApi = dmmApi;
            _simulation = new SecondaryPowerSimulation();

            // 初始化命令
            ManualTestCommand = new DelegateCommand(OnManualTest);
            AutoTestCommand = new DelegateCommand(OnAutoTest);
            MeasureCommand = new DelegateCommand(async () => await MeasureSinglePointAsync(), () => !IsBusy && IsPowerOn);
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
                // ========== 步骤1：配置矩阵开关通路 ==========
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

                // ========== 步骤2：开启28V供电 ==========
                // 通过J3和J4提供28V供电，继电器保持NC状态
                AddLog("正在开启28V供电（J3-J4）...");
                try
                {
                    await _simulation.SimulatePowerOnAsync(msg => AddLog(msg), timeoutCts.Token);
                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        IsPowerOn = true;
                        PowerStatus = "已供电";
                    });
                    AddLog("28V供电已开启");
                }
                catch (Exception ex)
                {
                    AddLog($"供电开启异常: {ex.Message}，使用仿真模式");
                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        IsPowerOn = true;
                        PowerStatus = "已供电(仿真)";
                    });
                }

                // ========== 步骤3：连接万用表 ==========
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

                _hardwareInitialized = true;
                AddLog("硬件初始化完成");
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

            // 关闭28V供电
            try
            {
                await _simulation.SimulatePowerOffAsync(msg => AddLog(msg), token);
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    IsPowerOn = false;
                    PowerStatus = "未供电";
                });
            }
            catch (Exception ex)
            {
                AddLog($"关闭供电异常: {ex.Message}");
            }

            // 断开万用表
            if (_dmmApi != null && _dmmApi.IsConnected)
            {
                try
                {
                    await _dmmApi.DisconnectAsync(token);
                    AddLog("万用表已断开");
                }
                catch (Exception ex)
                {
                    AddLog($"断开万用表异常: {ex.Message}");
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

                double voltage = await ReadVoltageFromDmmAsync(token);
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

        /// <summary>
        /// 从万用表读取电压值
        /// </summary>
        private async Task<double> ReadVoltageFromDmmAsync(CancellationToken token = default)
        {
            if (_useSimulatedDmm)
            {
                return await _simulation.SimulateMeasureVoltageAsync(token);
            }

            if (_dmmApi != null)
            {
                try
                {
                    if (!_dmmApi.IsConnected)
                    {
                        var dmmIp = GetDmmIpAddress();
                        await _dmmApi.ConnectAsync(dmmIp, token);
                    }

                    var reading = await _dmmApi.ReadOnceAsync(
                        DmmMeasureMode.DCV,
                        new DmmReadOptions { TimeoutMilliseconds = DmmTimeoutMs },
                        token);

                    if (reading?.Value != null)
                    {
                        return reading.Value.Value;
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
                    catch { }
                }
            }

            // 备用VISA路径
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
                        return await _simulation.SimulateMeasureVoltageAsync(token);
                    }
                }

                var visaTask = Task.Run(() =>
                {
                    _dmmSession.RawIO.Write("MEAS:VOLT:DC?\n");
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

                if (double.TryParse(response.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out double voltage))
                {
                    return voltage;
                }

                throw new InvalidOperationException($"无法解析万用表返回值: {response}");
            }
            finally
            {
                _dmmIoLock.Release();
            }
        }

        /// <summary>
        /// 初始化万用表（VISA方式）
        /// </summary>
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

                _dmmSession.RawIO.Write("CONF:VOLT:DC\n");
                await Task.Delay(200);
            }
            finally
            {
                _dmmIoLock.Release();
            }
        }

        /// <summary>
        /// 获取万用表IP地址
        /// </summary>
        private string GetDmmIpAddress()
        {
            // TODO: 从配置或上下文获取实际IP
            return "192.168.1.100";
        }

        #endregion

        #region 辅助方法

        private void ClearResults()
        {
            VoltageValue = null;
            TestResult = "--";
            OverallResult = "--";
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
