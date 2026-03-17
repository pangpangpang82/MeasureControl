using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using MeasureControl.Events;
using MeasureControl.Models;
using MeasureControl.Models.Devices;
using MeasureControl.Services;
using MeasureControl.Services.HardwareApis;
using MeasureControl.Simulations.AirController;
using MeasureControl.Views.Dialogs;
using Prism.Commands;
using Prism.Events;
using Prism.Mvvm;

namespace MeasureControl.ViewModels.SingleBoardTest.AirController
{
    public sealed class A_C_8_1ViewModel : BindableBase, IDisposable
    {
        private const string TestItemKey = "AirController_8_1_PowerToGroundImpedance";
        private const double ImpedanceThreshold = 200.0;
        private const string RelayControlChannel = "DO9";

        private const string RelayPowerSupplyIpAddress = "192.168.1.16";
        private const PowerSupplyChannel RelayPowerChannel = PowerSupplyChannel.CH2;
        private const double RelayPowerVoltage = 24.0;
        private const double RelayPowerCurrentLimit = 1.0;

        private const int DefaultTimeoutMs = 3000;
        private const int DmmTimeoutMs = 2000;
        private const int RelayTimeoutMs = 2000;
        private const int RelayPowerTimeoutMs = 2000;

        private readonly ISingleBoardTestContextService _singleBoardTestContext;
        private readonly ProjectService _projectService;
        private readonly IEventAggregator _eventAggregator;
        private readonly IComponentPowerStateApi _componentPowerStateApi;
        private readonly IPxiChassisService _pxiChassisService;
        private IJy7131Api _jy7131Api;
        private readonly IDmmApi _dmmApi;

        private IPowerSupplyApi _relayPowerSupply;

        private readonly A_C_8_1Simulation _simulation;

        private IDmmApi _dmmSocket;
        private readonly SemaphoreSlim _measureLock = new SemaphoreSlim(1, 1);

        private bool _hardwareInitialized;
        private CancellationTokenSource _opCts;
        private SubscriptionToken _projectSavingToken;

        private bool _isManualTestRunning;
        private bool _isAutoTestRunning;
        private bool _isBusy;
        private bool _isRelayActivated;
        private bool _isPowerOn;
        private string _powerStatus = "未就绪";
        private bool _useSimulatedDmm;
        private bool _relaySupplyOn;

        private double? _impedanceA;
        private double? _impedanceB;
        private double? _impedanceC;
        private double? _impedanceD;
        private double? _impedanceE;
        private double? _impedanceF;

        private string _resultA = "--";
        private string _resultB = "--";
        private string _resultC = "--";
        private string _resultD = "--";
        private string _resultE = "--";
        private string _resultF = "--";

        private string _overallResult = "--";
        private string _lastTestTime = "--";
        private string _relayStatus = "未激活";

        public A_C_8_1ViewModel(
            ISingleBoardTestContextService singleBoardTestContext,
            ProjectService projectService,
            IEventAggregator eventAggregator,
            IComponentPowerStateApi componentPowerStateApi,
            IPxiChassisService pxiChassisService,
            IDmmApi dmmApi = null)
        {
            _singleBoardTestContext = singleBoardTestContext;
            _projectService = projectService;
            _eventAggregator = eventAggregator;
            _componentPowerStateApi = componentPowerStateApi;
            _pxiChassisService = pxiChassisService;
            _dmmApi = dmmApi;

            _simulation = new A_C_8_1Simulation { ImpedanceThreshold = ImpedanceThreshold };

            ManualTestCommand = new DelegateCommand(OnManualTest);
            AutoTestCommand = new DelegateCommand(OnAutoTest);
            ToggleRelayCommand = new DelegateCommand(async () => await ToggleRelayAsync(), () => !IsBusy && IsManualTestRunning);

            MeasureACommand = new DelegateCommand(async () => await MeasureSinglePointAsync("A"), () => !IsBusy && IsRelayActivated);
            MeasureBCommand = new DelegateCommand(async () => await MeasureSinglePointAsync("B"), () => !IsBusy && IsRelayActivated);
            MeasureCCommand = new DelegateCommand(async () => await MeasureSinglePointAsync("C"), () => !IsBusy && IsRelayActivated);
            MeasureDCommand = new DelegateCommand(async () => await MeasureSinglePointAsync("D"), () => !IsBusy && IsRelayActivated);
            MeasureECommand = new DelegateCommand(async () => await MeasureSinglePointAsync("E"), () => !IsBusy && IsRelayActivated);
            MeasureFCommand = new DelegateCommand(async () => await MeasureSinglePointAsync("F"), () => !IsBusy && IsRelayActivated);

            ClearLogCommand = new DelegateCommand(() => Logs.Clear());

            LoadPersistedState();
            _projectSavingToken = _eventAggregator?.GetEvent<ProjectSavingEvent>()?.Subscribe(OnProjectSaving);
        }

        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();

        public DelegateCommand ManualTestCommand { get; }
        public DelegateCommand AutoTestCommand { get; }
        public DelegateCommand ToggleRelayCommand { get; }
        public DelegateCommand MeasureACommand { get; }
        public DelegateCommand MeasureBCommand { get; }
        public DelegateCommand MeasureCCommand { get; }
        public DelegateCommand MeasureDCommand { get; }
        public DelegateCommand MeasureECommand { get; }
        public DelegateCommand MeasureFCommand { get; }
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

        public double? ImpedanceA { get => _impedanceA; set => SetProperty(ref _impedanceA, value); }
        public double? ImpedanceB { get => _impedanceB; set => SetProperty(ref _impedanceB, value); }
        public double? ImpedanceC { get => _impedanceC; set => SetProperty(ref _impedanceC, value); }
        public double? ImpedanceD { get => _impedanceD; set => SetProperty(ref _impedanceD, value); }
        public double? ImpedanceE { get => _impedanceE; set => SetProperty(ref _impedanceE, value); }
        public double? ImpedanceF { get => _impedanceF; set => SetProperty(ref _impedanceF, value); }

        public string ResultA { get => _resultA; set => SetProperty(ref _resultA, value); }
        public string ResultB { get => _resultB; set => SetProperty(ref _resultB, value); }
        public string ResultC { get => _resultC; set => SetProperty(ref _resultC, value); }
        public string ResultD { get => _resultD; set => SetProperty(ref _resultD, value); }
        public string ResultE { get => _resultE; set => SetProperty(ref _resultE, value); }
        public string ResultF { get => _resultF; set => SetProperty(ref _resultF, value); }

        public string OverallResult { get => _overallResult; set => SetProperty(ref _overallResult, value); }
        public string LastTestTime { get => _lastTestTime; set => SetProperty(ref _lastTestTime, value); }

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

        private void OnManualTest()
        {
            if (IsManualTestRunning)
            {
                StopTest();
                return;
            }
            StartManualTest();
        }

        private void OnAutoTest()
        {
            if (IsAutoTestRunning)
            {
                StopTest();
                return;
            }
            StartAutoTest();
        }

        private void StartManualTest()
        {
            if (IsAutoTestRunning) return;

            _opCts?.Cancel();
            _opCts = new CancellationTokenSource();

            IsManualTestRunning = true;
            ClearResults();
            AddLog("手动测试开始");

            Task.Run(async () =>
            {
                var token = _opCts.Token;
                try
                {
                    AddLog("步骤1: 初始化硬件设备（7131板卡、万用表）...");
                    await InitializeHardwareWithTimeoutAsync(token);
                    if (token.IsCancellationRequested) return;

                    AddLog("步骤1.5: 继电器供电上电（24V）...");
                    await PowerOnRelaySupplyWithTimeoutAsync(token);
                    if (token.IsCancellationRequested) return;

                    AddLog("步骤2: 激活继电器，隔离产品与试验台...");
                    await ActivateRelayWithTimeoutAsync(token);
                    if (token.IsCancellationRequested) return;

                    AddLog("硬件初始化完成，请依次点击各测试项的\"测量\"按钮进行阻抗测量");
                }
                catch (OperationCanceledException)
                {
                    AddLog("初始化已取消");
                    Application.Current?.Dispatcher?.Invoke(() => IsManualTestRunning = false);
                }
                catch (TimeoutException ex)
                {
                    AddLog($"初始化超时: {ex.Message}");
                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        IsManualTestRunning = false;
                        ReMessageBox.Show($"手动测试初始化超时: {ex.Message}", "超时提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    });
                    try { await ResetHardwareAsync(CancellationToken.None); } catch { }
                }
                catch (Exception ex)
                {
                    AddLog($"初始化失败: {ex.Message}");
                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        IsManualTestRunning = false;
                        ReMessageBox.Show($"手动测试初始化失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    });
                    try { await ResetHardwareAsync(CancellationToken.None); } catch { }
                }
            });
        }

        private void StartAutoTest()
        {
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
                    AddLog("步骤1: 初始化硬件设备（7131板卡、万用表）...");
                    await InitializeHardwareWithTimeoutAsync(token);
                    if (token.IsCancellationRequested) return;

                    AddLog("步骤1.5: 继电器供电上电（24V）...");
                    await PowerOnRelaySupplyWithTimeoutAsync(token);
                    if (token.IsCancellationRequested) return;

                    AddLog("步骤2: 激活继电器，隔离产品与试验台...");
                    await ActivateRelayWithTimeoutAsync(token);
                    if (token.IsCancellationRequested) return;

                    AddLog("步骤3: 测量 28V_IN 对地阻抗");
                    await MeasureImpedanceWithTimeoutAsync("A", token);
                    if (token.IsCancellationRequested) return;

                    AddLog("步骤4: 测量 +15V 对地阻抗");
                    await MeasureImpedanceWithTimeoutAsync("B", token);
                    if (token.IsCancellationRequested) return;

                    AddLog("步骤5: 测量 -15V 对地阻抗");
                    await MeasureImpedanceWithTimeoutAsync("C", token);
                    if (token.IsCancellationRequested) return;

                    AddLog("步骤6: 测量 5V 对地阻抗");
                    await MeasureImpedanceWithTimeoutAsync("D", token);
                    if (token.IsCancellationRequested) return;

                    AddLog("步骤7: 测量 3.3V 对地阻抗");
                    await MeasureImpedanceWithTimeoutAsync("E", token);
                    if (token.IsCancellationRequested) return;

                    AddLog("步骤8: 测量 1.5V 对地阻抗");
                    await MeasureImpedanceWithTimeoutAsync("F", token);
                    if (token.IsCancellationRequested) return;

                    EvaluateOverallResult();
                    LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    AddLog($"自动测试完成，综合结果: {OverallResult}");

                    AddLog("步骤9: 复位硬件设备...");
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
                        ReMessageBox.Show($"自动测试超时: {ex.Message}", "超时提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    });
                    try { await ResetHardwareAsync(CancellationToken.None); } catch { }
                }
                catch (Exception ex)
                {
                    AddLog($"自动测试异常: {ex.Message}");
                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        ReMessageBox.Show($"自动测试异常: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    });
                    try { await ResetHardwareAsync(CancellationToken.None); } catch { }
                }
                finally
                {
                    Application.Current?.Dispatcher?.Invoke(() => IsAutoTestRunning = false);
                }
            });
        }

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
                        ReMessageBox.Show($"硬件复位失败: {ex.Message}", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                    });
                }
            });
        }

        private async Task ToggleRelayAsync()
        {
            IsBusy = true;
            try
            {
                using var cts = new CancellationTokenSource(RelayTimeoutMs);
                if (IsRelayActivated)
                    await DeactivateRelayWithTimeoutAsync(cts.Token);
                else
                    await ActivateRelayWithTimeoutAsync(cts.Token);
            }
            catch (TimeoutException ex)
            {
                AddLog($"继电器操作超时: {ex.Message}");
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    ReMessageBox.Show($"继电器操作超时: {ex.Message}", "超时提示", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                    ReMessageBox.Show($"继电器操作失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
            finally
            {
                IsBusy = false;
            }
        }

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

                if (_jy7131Api == null)
                {
                    var device7131 = FindFirstJy7131Device();
                    if (device7131 != null)
                    {
                        var devSlot = Infer7131SlotNumber(device7131);
                        AddLog($"找到7131板卡: {device7131.Model ?? device7131.Name}，槽位={devSlot}");
                        if (int.TryParse(devSlot, out var slotNum))
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
                        else if (!_jy7131Api.IsRunning)
                        {
                            await _jy7131Api.SetOutputModeAsync(Jy7131OutputMode.PushPull, timeoutCts.Token);
                            await _jy7131Api.StartAsync(timeoutCts.Token);
                            AddLog("7131板卡已启动");
                        }
                    }
                    catch (Exception ex)
                    {
                        AddLog($"7131板卡初始化异常: {ex.Message}，使用仿真模式");
                        _jy7131Api = null;
                    }
                }

                AddLog("正在设置组件供电状态: 下电...");
                try
                {
                    try
                    {
                        if (_componentPowerStateApi != null)
                            await _componentPowerStateApi.ApplyComponentDownStateAsync(timeoutCts.Token);
                        else
                            throw new InvalidOperationException("组件供电API未就绪");
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

        private async Task ResetHardwareAsync(CancellationToken token)
        {
            try
            {
                AddLog("正在复位硬件设备...");

                try
                {
                    if (_componentPowerStateApi != null)
                        await _componentPowerStateApi.ApplyComponentDownStateAsync(token);
                    await _simulation.ApplyComponentDownStateAsync(msg => AddLog(msg), token);
                }
                catch (Exception ex)
                {
                    AddLog($"复位时组件下电状态设置异常: {ex.Message}");
                }

                if (IsRelayActivated)
                    await DeactivateRelayWithTimeoutAsync(token);

                await PowerOffRelaySupplyAsync(token);

                if (_jy7131Api != null && _jy7131Api.IsConnected)
                {
                    try
                    {
                        if (_jy7131Api.IsRunning)
                        {
                            AddLog("正在停止7131板卡...");
                            await _jy7131Api.StopAsync(token);
                            AddLog("7131板卡已停止");
                        }

                        AddLog("正在断开7131板卡连接...");
                        await _jy7131Api.DisconnectAsync(token);
                        AddLog("7131板卡已断开");
                    }
                    catch (Exception ex)
                    {
                        AddLog($"7131板卡复位异常: {ex.Message}");
                    }
                }

                await DisconnectAllMatrixRoutesAsync();

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

        private const int MatrixSlotSig = 6;
        private const int MatrixSlotDmm = 4;

        private static readonly (string In, string Out, int Slot) MatrixDmmH = ("I4", "O2", MatrixSlotDmm);

        private static readonly (string In, string Out, int Slot) MatrixPointA1 = ("I1", "O8", MatrixSlotSig);
        private static readonly (string In, string Out, int Slot) MatrixPointB1 = ("I1", "O9", MatrixSlotSig);
        private static readonly (string In, string Out, int Slot) MatrixPointC1 = ("I1", "O10", MatrixSlotSig);
        private static readonly (string In, string Out, int Slot) MatrixPointD1 = ("I1", "O11", MatrixSlotSig);
        private static readonly (string In, string Out, int Slot) MatrixPointE1 = ("I1", "O12", MatrixSlotSig);
        private static readonly (string In, string Out, int Slot) MatrixPointF1 = ("I1", "O13", MatrixSlotSig);

        private string GetDmmIpAddress() => DmmIpAddress;

        [MethodImpl(MethodImplOptions.NoInlining)]
        private async Task ConnectDmmAsync(string ipAddress, CancellationToken token)
        {
            _dmmSocket ??= new DmmSocketApi();
            if (!_dmmSocket.IsConnected)
                await _dmmSocket.ConnectAsync(ipAddress, token);
        }

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
            await Task.Delay(500);
            try { await matrix.DisconnectNodesAsync(MatrixDmmH.In, MatrixDmmH.Out, MatrixDmmH.Slot, MatrixIpAddress); } catch { }
            try { await matrix.DisconnectNodesAsync(MatrixPointA1.In, MatrixPointA1.Out, MatrixPointA1.Slot, MatrixIpAddress); } catch { }
            try { await matrix.DisconnectNodesAsync(MatrixPointB1.In, MatrixPointB1.Out, MatrixPointB1.Slot, MatrixIpAddress); } catch { }
            try { await matrix.DisconnectNodesAsync(MatrixPointC1.In, MatrixPointC1.Out, MatrixPointC1.Slot, MatrixIpAddress); } catch { }
            try { await matrix.DisconnectNodesAsync(MatrixPointD1.In, MatrixPointD1.Out, MatrixPointD1.Slot, MatrixIpAddress); } catch { }
            try { await matrix.DisconnectNodesAsync(MatrixPointE1.In, MatrixPointE1.Out, MatrixPointE1.Slot, MatrixIpAddress); } catch { }
            try { await matrix.DisconnectNodesAsync(MatrixPointF1.In, MatrixPointF1.Out, MatrixPointF1.Slot, MatrixIpAddress); } catch { }
        }

        private async Task PowerOnRelaySupplyWithTimeoutAsync(CancellationToken token)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeoutCts.CancelAfter(RelayPowerTimeoutMs);

            try
            {
                if (_relaySupplyOn)
                {
                    AddLog("继电器供电已上电，跳过");
                    return;
                }

                AddLog($"正在开启继电器供电（24V）：电源2 {RelayPowerSupplyIpAddress} CH2...");
                _relayPowerSupply ??= new PowerSupplySocketApi();
                if (!_relayPowerSupply.IsConnected)
                    await _relayPowerSupply.ConnectAsync(RelayPowerSupplyIpAddress, timeoutCts.Token);

                await _relayPowerSupply.ApplyAsync(RelayPowerChannel, RelayPowerVoltage, RelayPowerCurrentLimit, timeoutCts.Token);
                await _relayPowerSupply.SetOutputEnabledAsync(RelayPowerChannel, true, timeoutCts.Token);
                await Task.Delay(200, timeoutCts.Token);

                _relaySupplyOn = true;
                AddLog($"继电器供电已上电：电源2 {RelayPowerSupplyIpAddress} CH2 {RelayPowerVoltage:0.###}V/{RelayPowerCurrentLimit:0.###}A");
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !token.IsCancellationRequested)
            {
                throw new TimeoutException($"继电器供电上电超时（{RelayPowerTimeoutMs}ms）");
            }
            catch (Exception ex)
            {
                AddLog($"继电器供电上电失败: {ex.Message}，使用仿真模式");
                await Task.Delay(200, timeoutCts.Token);
                _relaySupplyOn = true;
            }
        }

        private async Task PowerOffRelaySupplyAsync(CancellationToken token)
        {
            if (!_relaySupplyOn)
                return;

            try
            {
                if (_relayPowerSupply != null)
                {
                    try { await _relayPowerSupply.SetOutputEnabledAsync(RelayPowerChannel, false, token); } catch { }
                    try { await _relayPowerSupply.DisconnectAsync(token); } catch { }
                    try { await _relayPowerSupply.DisposeAsync(); } catch { }
                    _relayPowerSupply = null;
                }

                AddLog("继电器供电已关闭");
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

        private async Task ActivateRelayWithTimeoutAsync(CancellationToken token)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeoutCts.CancelAfter(RelayTimeoutMs);

            try
            {
                AddLog($"正在激活继电器（{RelayControlChannel}）...");

                if (_jy7131Api != null && _jy7131Api.IsConnected)
                {
                    if (!_jy7131Api.IsRunning)
                    {
                        await _jy7131Api.SetOutputModeAsync(Jy7131OutputMode.PushPull, timeoutCts.Token);
                        await _jy7131Api.StartAsync(timeoutCts.Token);
                        AddLog("7131板卡已启动");
                    }

                    AddLog($"正在写{RelayControlChannel}（高电平）...");
                    await _jy7131Api.WriteDoAsync(RelayControlChannel, true, timeoutCts.Token);
                    try
                    {
                        var mask = await _jy7131Api.ReadDoBitmaskAsync(timeoutCts.Token);
                        var ok = int.TryParse(RelayControlChannel.Substring(2), out var doIdx);
                        var bit = ok ? doIdx : 9;
                        AddLog($"DO写回读取: mask=0x{mask:X8}，{RelayControlChannel}={(mask & (1u << bit)) != 0}");
                    }
                    catch (Exception ex)
                    {
                        AddLog($"DO写回读取失败: {ex.Message}");
                    }
                }
                else
                {
                    AddLog("7131板卡不可用，使用仿真继电器动作");
                    await _simulation.SimulateRelayActivateAsync(timeoutCts.Token);
                }

                await Task.Delay(200, timeoutCts.Token);

                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    IsRelayActivated = true;
                    RelayStatus = "已激活";
                });

                AddLog("继电器已激活");
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !token.IsCancellationRequested)
            {
                throw new TimeoutException($"激活继电器超时（{RelayTimeoutMs}ms）");
            }
        }

        private async Task DeactivateRelayWithTimeoutAsync(CancellationToken token)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeoutCts.CancelAfter(RelayTimeoutMs);

            try
            {
                AddLog($"正在复位继电器（{RelayControlChannel}）...");

                if (_jy7131Api != null && _jy7131Api.IsConnected)
                {
                    AddLog($"正在写{RelayControlChannel}（低电平）...");
                    await _jy7131Api.WriteDoAsync(RelayControlChannel, false, timeoutCts.Token);
                    try
                    {
                        var mask = await _jy7131Api.ReadDoBitmaskAsync(timeoutCts.Token);
                        var ok = int.TryParse(RelayControlChannel.Substring(2), out var doIdx);
                        var bit = ok ? doIdx : 9;
                        AddLog($"DO写回读取: mask=0x{mask:X8}，{RelayControlChannel}={(mask & (1u << bit)) != 0}");
                    }
                    catch (Exception ex)
                    {
                        AddLog($"DO写回读取失败: {ex.Message}");
                    }
                }
                else
                {
                    AddLog("7131板卡不可用，使用仿真继电器动作");
                    await _simulation.SimulateRelayDeactivateAsync(timeoutCts.Token);
                }

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
                AddLog($"测量 {point} 超时");
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    ReMessageBox.Show($"测量阻抗超时（{DmmTimeoutMs}ms）", "超时提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                });
            }
            catch (TimeoutException)
            {
                AddLog($"测量 {point} 超时");
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    ReMessageBox.Show($"测量阻抗超时（{DmmTimeoutMs}ms）", "超时提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                });
            }
            catch (Exception ex)
            {
                AddLog($"测量失败: {ex.Message}");
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    ReMessageBox.Show($"测量阻抗失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
        }

        private void ClearResults()
        {
            ImpedanceA = null;
            ImpedanceB = null;
            ImpedanceC = null;
            ImpedanceD = null;
            ImpedanceE = null;
            ImpedanceF = null;

            ResultA = "--";
            ResultB = "--";
            ResultC = "--";
            ResultD = "--";
            ResultE = "--";
            ResultF = "--";

            OverallResult = "--";
        }

        private async Task MeasureImpedanceAsync(string point, CancellationToken token = default)
        {
            IsBusy = true;
            try
            {
                var pointName = point switch
                {
                    "A" => "28V_IN（J1/J2/J3 - J37/J38/J39）",
                    "B" => "+15V（J24/J25 - J22/J23）",
                    "C" => "-15V（J61/J62 - J59/J60）",
                    "D" => "5V（J95/J96 - J93/J94）",
                    "E" => "3.3V（J63/J64 - J93/J94）",
                    "F" => "1.5V（J97/J98 - J93/J94）",
                    _ => point
                };

                AddLog($"正在测量 {pointName} 对地阻抗...");

                var impedance = await ReadResistanceFromDmmAsync(point, token);
                var result = impedance >= ImpedanceThreshold ? "PASS" : "FAIL";

                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    switch (point)
                    {
                        case "A": ImpedanceA = impedance; ResultA = result; break;
                        case "B": ImpedanceB = impedance; ResultB = result; break;
                        case "C": ImpedanceC = impedance; ResultC = result; break;
                        case "D": ImpedanceD = impedance; ResultD = result; break;
                        case "E": ImpedanceE = impedance; ResultE = result; break;
                        case "F": ImpedanceF = impedance; ResultF = result; break;
                    }
                });

                AddLog($"{pointName} 阻抗: {impedance:F1}Ω, 结果: {result}");

                if (IsManualTestRunning)
                {
                    EvaluateOverallResult();
                    if (new[] { ResultA, ResultB, ResultC, ResultD, ResultE, ResultF }.All(r => r != "--"))
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
                if (new[] { ResultA, ResultB, ResultC, ResultD, ResultE, ResultF }.All(r => r == "PASS"))
                {
                    OverallResult = "PASS";
                }
                else if (new[] { ResultA, ResultB, ResultC, ResultD, ResultE, ResultF }.Any(r => r == "FAIL"))
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
                return await _simulation.SimulateMeasureResistanceAsync(point, token);

            await _measureLock.WaitAsync(token);
            try
            {
                var matrix = MatrixControlService.Instance;

                (string In, string Out, int Slot) c1 = point switch
                {
                    "A" => MatrixPointA1,
                    "B" => MatrixPointB1,
                    "C" => MatrixPointC1,
                    "D" => MatrixPointD1,
                    "E" => MatrixPointE1,
                    "F" => MatrixPointF1,
                    _ => MatrixPointA1
                };

                var okDmm = await matrix.ConnectNodesAsync(MatrixDmmH.In, MatrixDmmH.Out, MatrixDmmH.Slot, MatrixIpAddress);
                var ok1 = await matrix.ConnectNodesAsync(c1.In, c1.Out, c1.Slot, MatrixIpAddress);
                await Task.Delay(1000, token);

                AddLog($"矩阵连接 {(okDmm && ok1 ? "OK" : "FAIL")} - DMM:{MatrixDmmH.In}-{MatrixDmmH.Out}(slot{MatrixDmmH.Slot}), {c1.In}-{c1.Out}(slot{c1.Slot})");

                if (!okDmm || !ok1)
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
                MeasureECommand?.RaiseCanExecuteChanged();
                MeasureFCommand?.RaiseCanExecuteChanged();
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

        private DeviceBase FindFirstJy7131Device()
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

                var device = chassis.Devices.FirstOrDefault(d =>
                    d is DigitalIODevice ||
                    (d?.Model?.IndexOf("7131", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.DeviceTypeName?.IndexOf("离散量", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.DeviceTypeName?.IndexOf("数字量", StringComparison.OrdinalIgnoreCase) >= 0));

                if (device != null)
                    return device;

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
                        return childDevice;
                }
            }

            return null;
        }

        private static string Infer7131SlotNumber(DeviceBase device)
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
                    _jy7131Api.WriteDoAsync(RelayControlChannel, false).GetAwaiter().GetResult();
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
                    _dmmSocket.DisconnectAsync(CancellationToken.None).GetAwaiter().GetResult();
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
                _eventAggregator?.GetEvent<ProjectSavingEvent>()?.Unsubscribe(_projectSavingToken);
        }
    }
}
