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

        private const int DefaultTimeoutMs = 15000;

        

        /// <summary>单步测量超时时间（毫秒）</summary>

        private const int StepTimeoutMs = 2000;



        /// <summary>AD采集通道（INT_AD2对应的通道）</summary>

        private const string AdChannel = "AI2";



        /// <summary>第二个电源IP地址（运放供电+15V）</summary>

        private const string PowerSupply2IpAddress = "192.168.1.16";

        /// <summary>第三个电源IP地址（DI上拉信号+15V）</summary>

        private const string PowerSupply3IpAddress = "192.168.1.17";

        /// <summary>运放供电电压（V）</summary>

        private const double OpAmpSupplyVoltage = 15.0;

        /// <summary>运放供电电流限制（A）</summary>

        private const double OpAmpSupplyCurrent = 1.0;

        private const string MatrixIpAddress = "192.168.1.3";

        private const int MatrixSlot2601_1 = 4;

        private const int MatrixSlot3022_1 = 2;

        private const int MatrixTcpBasePort3022 = 50300;

        private static readonly (string In, string Out, int Slot) Matrix2601 = ("I4", "O0", MatrixSlot2601_1);

        private static readonly (string In, string Out, int Slot, int BasePort) Matrix3022 = ("I0", "O41", MatrixSlot3022_1, MatrixTcpBasePort3022);



        #endregion



        #region 依赖服务



        private readonly ISingleBoardTestContextService _singleBoardTestContext;
        private readonly ProjectService _projectService;
        private readonly IEventAggregator _eventAggregator;
        private readonly IComponentPowerStateApi _componentPowerStateApi;
        private readonly IPxiChassisService _pxiChassisService;
        private readonly IHydraulicPowerService _hydraulicPowerService;
        private readonly LowVoltageAlarmSimulation _simulation;



        #endregion



        #region 电源控制



        private IPowerSupplyApi _powerSupplyApi;

        private const string PowerSupplyIpAddress = "192.168.1.15"; // 电源1 IP

        private const double PowerSupplyCurrent = 3.0; // 电流限制3A



        #endregion



        #region 9774板卡



        private IArt9774AiApi _ai9774Api;



        #endregion



        #region 运放供电和DI上拉电源



        private IPowerSupplyApi _powerSupply2;  // 第二个电源（运放供电+15V）

        private IPowerSupplyApi _powerSupply3;  // 第三个电源（DI上拉信号+15V）

        private bool _opAmpPowerOn;             // 运放供电是否已开启



        #endregion



        #region 状态字段



        private bool _hardwareInitialized;

        private CancellationTokenSource _opCts;

        private SubscriptionToken _projectSavingToken;



        private bool _isManualTestRunning;
        private bool _isAutoTestRunning;
        private bool _isBusy;
        private bool _isPowerOn;
        
        /// <summary>测试前保存的原始上电电压，用于测试结束后恢复</summary>
        private double _originalVoltage;
        /// <summary>测试前组件是否已上电</summary>
        private bool _wasOriginallyPowered;
        /// <summary>手动测试是否已完成首次17V设置</summary>
        private bool _initialVoltageSet;
        /// <summary>手动测试是否已检测到电平翻转（或已到最低电压）</summary>
        private bool _flipDetected;
        private string _stepDownButtonText = "降到17V";
        private bool _isManualTestInitializing;
        private bool _isAutoTestInitializing;
        private bool _isManualTestStopping;
        private bool _isAutoTestStopping;



        #endregion



        #region 测量结果字段



        private double _currentVoltage;           // 当前供电电压

        private bool _currentPinLevel;            // 当前CRM_PIN3电平

        private double _currentPin3Voltage;       // 当前CRM_PIN3电压值（AD2采集）

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
            IPxiChassisService pxiChassisService,
            IHydraulicPowerService hydraulicPowerService)
        {
            _singleBoardTestContext = singleBoardTestContext;
            _projectService = projectService;
            _eventAggregator = eventAggregator;
            _componentPowerStateApi = componentPowerStateApi;
            _pxiChassisService = pxiChassisService;
            _hydraulicPowerService = hydraulicPowerService;
            _simulation = new LowVoltageAlarmSimulation();



            // 初始化命令

            ManualTestCommand = new DelegateCommand(async () => await OnManualTestAsync());

            AutoTestCommand = new DelegateCommand(async () => await OnAutoTestAsync());

            StepDownCommand = new DelegateCommand(async () => await StepDownVoltageAsync(), () => !IsBusy && IsManualTestRunning && _hardwareInitialized && !_flipDetected);

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



        /// <summary>

        /// 当前CRM_PIN3电压值（AD2采集的实际电压）

        /// </summary>

        public double CurrentPin3Voltage

        {

            get => _currentPin3Voltage;

            set => SetProperty(ref _currentPin3Voltage, value);

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



        public string StepDownButtonText
        {
            get => _stepDownButtonText;
            set => SetProperty(ref _stepDownButtonText, value);
        }

        public bool IsManualTestBusy => IsManualTestInitializing || IsManualTestStopping;
        public bool IsAutoTestBusy => IsAutoTestInitializing || IsAutoTestStopping;

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



        #endregion



        #region 命令



        public DelegateCommand ManualTestCommand { get; }

        public DelegateCommand AutoTestCommand { get; }

        public DelegateCommand StepDownCommand { get; }

        public DelegateCommand ClearLogCommand { get; }



        #endregion



        #region 命令处理



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
                AddLog("硬件初始化完成，请点击\"降到17V\"按鈕开始测试");
            }
            catch (OperationCanceledException)
            {
                AddLog("手动测试初始化已取消");
                await AbortManualTestAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AddLog($"手动测试初始化失败: {ex.Message}");
                await AbortManualTestAsync().ConfigureAwait(false);
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
                _initialVoltageSet = false;
                _flipDetected = false;
                IsManualTestInitializing = false;
                IsManualTestRunning = false;
                IsManualTestStopping = false;
                Application.Current?.Dispatcher?.Invoke(() => StepDownButtonText = "降到17V");
                RaisePropertyChanged(nameof(CanStartManualTest));
                RaisePropertyChanged(nameof(CanStartAutoTest));
                UpdateCommandStates();
                AddLog("手动测试已结束");
            }
        }

        private async Task AbortManualTestAsync()
        {
            await StopManualTestAsync().ConfigureAwait(false);
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

            AddLog("步骤1: 初始化硬件设备...");

            await InitializeHardwareWithTimeoutAsync(token).ConfigureAwait(false);

            token.ThrowIfCancellationRequested();



            // 初始化成功，切换到运行状态
            IsAutoTestInitializing = false;
            IsAutoTestRunning = true;



            AddLog("步骤2: 设置初始电压（17V）...");

            await SetInitialVoltageAsync(token).ConfigureAwait(false);

            token.ThrowIfCancellationRequested();



            AddLog("步骤3: 开始梯度降压测试...");

            await RunGradientTestAsync(token).ConfigureAwait(false);

            token.ThrowIfCancellationRequested();



            AddLog("步骤4: 判定测试结果...");

            EvaluateTestResult();



            AddLog($"自动测试完成，结果: {OverallResult}");

            await StopAutoTestAsync().ConfigureAwait(false);

            return OverallResult ?? "--";

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



                // 步骤1：初始化9774板卡（用于AD采集）
                await Initialize9774AiAsync(timeoutCts.Token);

                // 步骤2：配置矩阵开关通路
                AddLog("正在配置矩阵开关通路...");
                bool matrixOk = false;
                try
                {
                    await ConnectMatrixRoutesAsync(timeoutCts.Token);
                    matrixOk = true;
                }
                catch (Exception ex)
                {
                    AddLog($"矩阵开关配置异常: {ex.Message}");
                }
                if (!matrixOk)
                    throw new InvalidOperationException("矩阵开关配置失败，无法执行真实测试");

                // 步骤3/4：并行开启.16和.17（+15V 运放供电 / DI上拉）
                await InitializeOpAmpAndDiPullUpPowerAsync(timeoutCts.Token);

                // 步骤5：直接连接192.168.1.15，开启CH1 28V供电
                AddLog($"正在连接{PowerSupplyIpAddress}，开启CH1 28V供电...");
                _powerSupplyApi ??= new PowerSupplySocketApi();
                if (!_powerSupplyApi.IsConnected)
                    await _powerSupplyApi.ConnectAsync(PowerSupplyIpAddress, timeoutCts.Token);
                await _powerSupplyApi.ApplyAsync(PowerSupplyChannel.CH1, 28.0, PowerSupplyCurrent, timeoutCts.Token);
                await _powerSupplyApi.SetOutputEnabledAsync(PowerSupplyChannel.CH1, true, timeoutCts.Token);
                AddLog($"{PowerSupplyIpAddress} CH1 28V已开启");

                // 等待电源稳定
                await Task.Delay(500, timeoutCts.Token);

                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    IsPowerOn = true;
                    PowerStatus = "已上电(28V)";
                });



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



            var device = FindFirst9774Device();

            if (device == null)

            {

                throw new InvalidOperationException("未找到9774板卡，无法执行真实测试");

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

                AddLog($"9774板卡初始化失败: {ex.Message}");

                _ai9774Api = null;

                throw;

            }

        }

        private async Task ConnectMatrixRoutesAsync(CancellationToken token)

        {

            var svc = MatrixControlService.Instance;

            bool ok2601 = await svc.ConnectNodesAsync(Matrix2601.In, Matrix2601.Out, Matrix2601.Slot, MatrixIpAddress);

            AddLog($"2601(1): {Matrix2601.In}->{Matrix2601.Out} slot={Matrix2601.Slot}, ok={ok2601}");

            bool ok3022 = await svc.ConnectNodesAsync(Matrix3022.In, Matrix3022.Out, Matrix3022.Slot, MatrixIpAddress, Matrix3022.BasePort);

            AddLog($"3022(1): {Matrix3022.In}->{Matrix3022.Out} slot={Matrix3022.Slot}, basePort={Matrix3022.BasePort}, ok={ok3022}");

            if (!ok2601 || !ok3022)

            {

                throw new InvalidOperationException("矩阵开关通路连接失败");

            }

        }

        private async Task DisconnectMatrixRoutesAsync(CancellationToken token)

        {

            var svc = MatrixControlService.Instance;

            try { await svc.DisconnectNodesAsync(Matrix2601.In, Matrix2601.Out, Matrix2601.Slot, MatrixIpAddress); } catch { }

            try { await svc.DisconnectNodesAsync(Matrix3022.In, Matrix3022.Out, Matrix3022.Slot, MatrixIpAddress, Matrix3022.BasePort); } catch { }

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

                var device = chassis.Devices.FirstOrDefault(d =>

                    d is AnalogAcquisitionDevice ||

                    (d?.Model?.IndexOf("9774", StringComparison.OrdinalIgnoreCase) >= 0) ||

                    (d?.DeviceTypeName?.IndexOf("模拟量输入", StringComparison.OrdinalIgnoreCase) >= 0) ||

                    (d?.DeviceTypeName?.IndexOf("模拟量采集", StringComparison.OrdinalIgnoreCase) >= 0));



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



                    var childDevice = d.Children.FirstOrDefault(c =>

                        c is AnalogAcquisitionDevice ||

                        (c?.Model?.IndexOf("9774", StringComparison.OrdinalIgnoreCase) >= 0) ||

                        (c?.DeviceTypeName?.IndexOf("模拟量输入", StringComparison.OrdinalIgnoreCase) >= 0) ||

                        (c?.DeviceTypeName?.IndexOf("模拟量采集", StringComparison.OrdinalIgnoreCase) >= 0));



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
            // 使用组件电源API设置初始电压17V
            await SetPowerSupplyVoltageAsync(StartVoltage, token);
            
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                CurrentVoltage = StartVoltage;
                PowerStatus = $"测试中({StartVoltage:F1}V)";
                TestProgress = 0;
            });



            // 等待电压稳定

            await Task.Delay(300, token);



            // 读取初始电平和PIN3电压

            var (level, pin3Voltage) = await ReadPin3VoltageAsync(token);

            Application.Current?.Dispatcher?.Invoke(() =>

            {

                CurrentPinLevel = level;

                CurrentPin3Voltage = pin3Voltage;

                AddTestRecord(StartVoltage, level, pin3Voltage);

            });

            AddLog($"初始电压: {StartVoltage:F1}V, PIN3电压: {pin3Voltage:F3}V, 电平: {(level ? "高" : "低")}");

        }



        private async Task StepDownVoltageAsync()
        {
            if (IsBusy || !IsManualTestRunning || !_hardwareInitialized || _flipDetected)
                return;

            IsBusy = true;
            try
            {
                var token = _opCts?.Token ?? CancellationToken.None;
                bool wasFirstStep = !_initialVoltageSet;

                double newVoltage;
                if (wasFirstStep)
                {
                    // 首次点击：设置初始电压 17V
                    newVoltage = StartVoltage;
                }
                else
                {
                    // 后续点击：降低 0.2V
                    newVoltage = Math.Round(CurrentVoltage - VoltageStep, 1);
                    if (newVoltage < EndVoltage) newVoltage = EndVoltage;
                }

                await SetPowerSupplyVoltageAsync(newVoltage, token);
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    CurrentVoltage = newVoltage;
                    TestProgress = wasFirstStep ? 0
                        : (int)((StartVoltage - newVoltage) / (StartVoltage - EndVoltage) * 100);
                });

                // 等待电压稳定
                await Task.Delay(400, token);

                // 读取PIN3电平
                bool previousLevel = CurrentPinLevel;
                var (level, pin3Voltage) = await ReadPin3VoltageAsync(token);
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    CurrentPinLevel = level;
                    CurrentPin3Voltage = pin3Voltage;
                    AddTestRecord(newVoltage, level, pin3Voltage);
                });
                AddLog($"供电: {newVoltage:F1}V, PIN3: {pin3Voltage:F3}V, 电平: {(level ? "高" : "低")}");

                if (wasFirstStep)
                {
                    _initialVoltageSet = true;
                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        StepDownButtonText = "降低0.2V";
                        UpdateCommandStates();
                    });
                    AddLog($"初始电压已设置为 {StartVoltage:F1}V，请点击\"降低0.2V\"继续降压");
                }
                else
                {
                    // 检测电平翻转
                    if (previousLevel != level && FlipVoltage == null)
                    {
                        Application.Current?.Dispatcher?.Invoke(() => FlipVoltage = newVoltage);
                        AddLog($"*** 电平翻转检测到！翻转电压: {newVoltage:F1}V ***");
                        _flipDetected = true;
                        UpdateCommandStates();
                        EvaluateTestResult();
                        AddLog("翻转电压已记录，测试完成，正在停止测试...");
                        await StopManualTestAsync().ConfigureAwait(false);
                        return;
                    }

                    // 已达到最低电压，未检测到翻转
                    if (newVoltage <= EndVoltage)
                    {
                        AddLog($"已达到最低电压 {EndVoltage:F1}V，未检测到电平翻转，判定不合格");
                        _flipDetected = true;
                        UpdateCommandStates();
                        EvaluateTestResult();
                        await StopManualTestAsync().ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                AddLog("降压操作已取消");
            }
            catch (Exception ex)
            {
                AddLog($"降压操作失败: {ex.Message}");
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



                // 使用真实电源API设置电压

                await SetPowerSupplyVoltageAsync(voltage, token);

                Application.Current?.Dispatcher?.Invoke(() =>

                {

                    CurrentVoltage = voltage;

                    TestProgress = (int)((StartVoltage - voltage) / (StartVoltage - EndVoltage) * 100);

                });



                // 等待电压稳定

                await Task.Delay(200, token);



                // 读取电平和PIN3电压

                var (level, pin3Voltage) = await ReadPin3VoltageAsync(token);

                Application.Current?.Dispatcher?.Invoke(() =>

                {

                    CurrentPinLevel = level;

                    CurrentPin3Voltage = pin3Voltage;

                    AddTestRecord(voltage, level, pin3Voltage);

                });



                // 检测电平翻转

                if (previousLevel != level && FlipVoltage == null)

                {

                    Application.Current?.Dispatcher?.Invoke(() =>

                    {

                        FlipVoltage = voltage;

                    });

                    AddLog($"*** 电平翻转检测到！翻转电压: {voltage:F1}V ***");
                    break;

                }



                previousLevel = level;

                AddLog($"供电: {voltage:F1}V, PIN3: {pin3Voltage:F3}V, 电平: {(level ? "高" : "低")}");

            }



        }



        /// <summary>
        /// 设置供电电压（直接操作192.168.1.15 CH1）
        /// </summary>
        private async Task SetPowerSupplyVoltageAsync(double voltage, CancellationToken token)
        {
            if (_powerSupplyApi == null || !_powerSupplyApi.IsConnected)
                throw new InvalidOperationException($"{PowerSupplyIpAddress}电源未连接，无法设置供电电压");

            await _powerSupplyApi.ApplyAsync(PowerSupplyChannel.CH1, voltage, PowerSupplyCurrent, token);
            await _powerSupplyApi.SetOutputEnabledAsync(PowerSupplyChannel.CH1, true, token);
            AddLog($"[{PowerSupplyIpAddress} CH1] 电压已设置为 {voltage:F1}V");
        }



        /// <summary>

        /// 初始化电源连接

        /// </summary>

        private async Task InitializePowerSupplyAsync(CancellationToken token)

        {

            if (_powerSupplyApi != null && _powerSupplyApi.IsConnected)

            {

                AddLog("电源已连接，跳过");

                return;

            }



            try

            {

                AddLog($"正在连接电源 {PowerSupplyIpAddress}...");

                _powerSupplyApi = new PowerSupplySocketApi();

                await _powerSupplyApi.ConnectAsync(PowerSupplyIpAddress, token);

                

                // 配置CH1输出

                await _powerSupplyApi.ApplyAsync(PowerSupplyChannel.CH1, StartVoltage, PowerSupplyCurrent, token);

                await _powerSupplyApi.SetOutputEnabledAsync(PowerSupplyChannel.CH1, true, token);

                AddLog($"电源连接成功，CH1已配置: {StartVoltage:F1}V / {PowerSupplyCurrent:F1}A");

            }

            catch (Exception ex)

            {

                AddLog($"电源连接失败: {ex.Message}");

                _powerSupplyApi = null;

            }

        }



        /// <summary>

        /// 读取PIN3电压值并判断电平状态

        /// </summary>

        /// <returns>元组：(电平状态, 实际电压值)</returns>

        private async Task<(bool level, double voltage)> ReadPin3VoltageAsync(CancellationToken token)

        {

            double adVoltage = 0.0;

            bool level = false;



            // 优先使用9774板卡读取AD2通道

            if (_ai9774Api != null && _ai9774Api.IsConnected)

            {

                try

                {

                    // 确保采集已启动

                    if (!_ai9774Api.IsRunning)

                    {

                        try { await _ai9774Api.StartAsync(token); } catch { }

                        await Task.Delay(100, token); // 等待采集稳定

                    }



                    adVoltage = await _ai9774Api.GetLastValueAsync(AdChannel, token);

                    // 电压大于1.5V认为是高电平

                    level = adVoltage > 1.5;

                    AddLog($"[9774 AD2] 读取电压: {adVoltage:F3}V, 电平: {(level ? "高" : "低")}");

                    return (level, adVoltage);

                }
                catch (Exception ex)

                {

                    AddLog($"9774读取异常: {ex.Message}");

                    throw;

                }

            }

            throw new InvalidOperationException("9774板卡未连接，无法读取PIN3电压");

        }

        /// <summary>

        /// 兼容旧接口：只返回电平状态

        /// </summary>

        private async Task<bool> ReadPinLevelAsync(CancellationToken token)

        {

            var (level, _) = await ReadPin3VoltageAsync(token);

            return level;

        }



        private async Task ResetHardwareAsync(CancellationToken token)
        {
            AddLog("正在复位硬件（反序断开）...");

            // 步骤1: 关闭192.168.1.15 CH1并断开
            try
            {
                if (_powerSupplyApi != null && _powerSupplyApi.IsConnected)
                {
                    await _powerSupplyApi.SetOutputEnabledAsync(PowerSupplyChannel.CH1, false, token);
                    await _powerSupplyApi.DisconnectAsync(token);
                    AddLog($"{PowerSupplyIpAddress} CH1已关闭并断开");
                }
            }
            catch (Exception ex)
            {
                AddLog($"关闭{PowerSupplyIpAddress}异常: {ex.Message}");
            }
            finally
            {
                _powerSupplyApi = null;
            }
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                IsPowerOn = false;
                PowerStatus = "未上电";
                CurrentVoltage = 0;
            });

            // 步骤2/3: 关闭.17和.16（反序）
            await ShutdownOpAmpAndDiPullUpPowerAsync();

            // 步骤4: 断开矩阵开关
            try
            {
                await DisconnectMatrixRoutesAsync(token);
                AddLog("矩阵开关已断开");
            }
            catch (Exception ex)
            {
                AddLog($"断开矩阵开关异常: {ex.Message}");
            }

            // 步骤5: 断开9774板卡（最后）
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
            AddLog("硬件复位完成");
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



        private void AddTestRecord(double voltage, bool level, double pin3Voltage)

        {

            TestRecords.Add(new VoltageAlarmRecord

            {

                Voltage = voltage,

                Level = level,

                LevelText = level ? "高" : "低",

                Pin3Voltage = pin3Voltage,

                Timestamp = DateTime.Now,

                PassResult = EvaluateSingleRecord(voltage, pin3Voltage)

            });

        }



        /// <summary>

        /// 单条记录的合格判定（预留框架）

        /// </summary>

        /// <param name="supplyVoltage">供电电压</param>

        /// <param name="pin3Voltage">PIN3电压值</param>

        /// <returns>合格判定结果</returns>

        private string EvaluateSingleRecord(double supplyVoltage, double pin3Voltage)

        {

            // TODO: 根据实际合格判据完善此逻辑

            // 当前框架：暂不判定，返回"--"

            // 未来可根据供电电压和PIN3电压的关系判定合格与否

            // 例如：当供电电压 > 15V 时，PIN3应为高电平（>1.5V）

            //       当供电电压 < 15V 时，PIN3应为低电平（<1.5V）

            return "--";

        }



        private void ClearResults()
        {
            _initialVoltageSet = false;
            _flipDetected = false;

            Application.Current?.Dispatcher?.Invoke(() =>
            {
                CurrentVoltage = 0;
                CurrentPinLevel = false;
                FlipVoltage = null;
                TestResult = "--";
                OverallResult = "--";
                LastTestTime = "--";
                TestProgress = 0;
                StepDownButtonText = "降到17V";
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

        /// <summary>供电电压（V）</summary>

        public double Voltage { get; set; }

        /// <summary>PIN3电平状态</summary>

        public bool Level { get; set; }

        /// <summary>PIN3电平文本（高/低）</summary>

        public string LevelText { get; set; }

        /// <summary>PIN3实际电压值（V）- 9774 AD2采集</summary>

        public double Pin3Voltage { get; set; }

        /// <summary>记录时间</summary>

        public DateTime Timestamp { get; set; }

        /// <summary>合格判定结果（预留）</summary>

        public string PassResult { get; set; } = "--";

    }

}

