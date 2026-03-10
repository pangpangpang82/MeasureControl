using Prism.Commands;

using Prism.Mvvm;

using Prism.Ioc;

using System;

using System.Collections.ObjectModel;

using System.Diagnostics;

using System.Globalization;

using System.Linq;

using System.Threading;

using System.Threading.Tasks;

using System.Windows;

using MeasureControl.Helpers;

using MeasureControl.Models.Devices;

using MeasureControl.Models.Devices.DeviceCategories;

using MeasureControl.Services;

using MeasureControl.Services.HardwareApis;

using MeasureControl.Simulations.A_C_6_10_7_1;



namespace MeasureControl.ViewModels.SingleBoardTest.AirController

{

    public sealed class A_C_6_10_7_1ViewModel : BindableBase, IDisposable

    {

        private static readonly byte[] EnterAtpCommand8 = { 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00 };

        private static readonly byte[] EnterAtpOk8 = { 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x02 };

        private static readonly byte[] ExitAtpCommand8 = { 0x00, 0x02, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01 };

        private static readonly byte[] ExitAtpOk8 = { 0x00, 0x02, 0x00, 0x01, 0x00, 0x00, 0x00, 0x03 };



        private static readonly byte[] AbRaiaPosition8 = { 0x07, 0x03, 0x07, 0x01, 0x00, 0x00, 0x00, 0x00 };

        private static readonly byte[] RaiaPosTelemetryPrefix4 = { 0x07, 0x03, 0x07, 0x02 };



        private const string AoChannel = "AO1";



        private readonly A_C_6_10_7_1Simulation _simulation = new A_C_6_10_7_1Simulation();

        private readonly SemaphoreSlim _arincOpLock = new SemaphoreSlim(1, 1);

        private readonly SemaphoreSlim _manualTestLock = new SemaphoreSlim(1, 1);

        private readonly SemaphoreSlim _autoTestLock = new SemaphoreSlim(1, 1);

        private readonly SemaphoreSlim _mtxOpLock = new SemaphoreSlim(1, 1);



        private CancellationTokenSource _autoTestCts;



        private IMtx532Api _mtxApi;



        private string _testTxChannel;

        private string _testRxChannel;



        private string _controllerPressureTestTxChannel;

        private string _pressureTelemetryRxChannel;



        private string _enterAtpTxChannel;

        private string _enterAtpRxChannel;

        private string _exitAtpTxChannel;

        private string _exitAtpRxChannel;



        private string _enterAtpRxDataText;

        private string _exitAtpRxDataText;



        private bool _isInAtp;



        private string _voltageSetValueText;

        private string _pressureTelemetryValueText;

        private string _pressureTelemetryRxDataText;



        private int _currentGearIndex;



        private string _voltageGear;

        private bool _suppressVoltageGearChanged;



        private bool _isMtx532RealHardware;

        private string _mtx532ModeText;



        private string _lastTestTime;

        private string _lastTestResult;

        private string _previousTestTime;

        private string _previousTestResult;



        private bool _isManualTestRunning;

        private bool _isAutoTestRunning;

        private bool _isBusy;



        private double _arincRate = 100000.0;



        public A_C_6_10_7_1ViewModel()

        {

            _testTxChannel = "CH0";

            _testRxChannel = "CH1";



            _controllerPressureTestTxChannel = null;

            _pressureTelemetryRxChannel = null;



            _enterAtpTxChannel = null;

            _enterAtpRxChannel = null;

            _exitAtpTxChannel = null;

            _exitAtpRxChannel = null;



            VoltageSetValueText = "--";

            PressureTelemetryValueText = "--";

            PressureTelemetryRxDataText = "--";



            EnterAtpRxDataText = "--";

            ExitAtpRxDataText = "--";

            IsInAtp = false;



            CurrentGearIndex = 0;

            VoltageGear = null;



            Mtx532ModeText = "MTX532：未连接";



            LastTestTime = "--";

            LastTestResult = "--";

            PreviousTestTime = "--";

            PreviousTestResult = "--";



            ManualTestCommand = new DelegateCommand(OnManualTest);

            AutoTestCommand = new DelegateCommand(OnAutoTest);



            SendEnterAtpCommand = new DelegateCommand(async () => await OnSendEnterAtpAsync());

            SendExitAtpCommand = new DelegateCommand(async () => await OnSendExitAtpAsync());

            ClearLogCommand = new DelegateCommand(() => Logs.Clear());



            SendSetControllerVoltageCommand = new DelegateCommand(async () => await OnSetSelectedGearVoltageAsync());

            SendControllerPressureTestCommand = new DelegateCommand(async () => await OnSendControllerRaiaPosTestAsync());

            TestPressureTelemetryCommand = new DelegateCommand(async () => await OnReadRaiaPosTelemetryAsync());



            _simulation.GetCurrentGearIndex = () => CurrentGearIndex <= 0 ? 1 : CurrentGearIndex;

        }



        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();



        public DelegateCommand ManualTestCommand { get; }

        public DelegateCommand AutoTestCommand { get; }

        public DelegateCommand SendEnterAtpCommand { get; }

        public DelegateCommand SendExitAtpCommand { get; }

        public DelegateCommand ClearLogCommand { get; }



        public DelegateCommand SendSetControllerVoltageCommand { get; }

        public DelegateCommand SendControllerPressureTestCommand { get; }

        public DelegateCommand TestPressureTelemetryCommand { get; }



        public bool CanEditStepControls => !IsAutoTestRunning && !IsBusy;



        public string TestTxChannel

        {

            get => _testTxChannel;

            set => SetProperty(ref _testTxChannel, value);

        }



        public string TestRxChannel

        {

            get => _testRxChannel;

            set => SetProperty(ref _testRxChannel, value);

        }



        public string ControllerPressureTestTxChannel

        {

            get => _controllerPressureTestTxChannel;

            set => SetProperty(ref _controllerPressureTestTxChannel, value);

        }



        public string PressureTelemetryRxChannel

        {

            get => _pressureTelemetryRxChannel;

            set => SetProperty(ref _pressureTelemetryRxChannel, value);

        }



        public string EnterAtpTxChannel

        {

            get => _enterAtpTxChannel;

            set => SetProperty(ref _enterAtpTxChannel, value);

        }



        public string EnterAtpRxChannel

        {

            get => _enterAtpRxChannel;

            set => SetProperty(ref _enterAtpRxChannel, value);

        }



        public string ExitAtpTxChannel

        {

            get => _exitAtpTxChannel;

            set => SetProperty(ref _exitAtpTxChannel, value);

        }



        public string ExitAtpRxChannel

        {

            get => _exitAtpRxChannel;

            set => SetProperty(ref _exitAtpRxChannel, value);

        }



        public double ArincRate

        {

            get => _arincRate;

            set => SetProperty(ref _arincRate, value);

        }



        public string VoltageSetValueText

        {

            get => _voltageSetValueText;

            private set => SetProperty(ref _voltageSetValueText, value);

        }



        public string PressureTelemetryValueText

        {

            get => _pressureTelemetryValueText;

            private set => SetProperty(ref _pressureTelemetryValueText, value);

        }



        public string PressureTelemetryRxDataText

        {

            get => _pressureTelemetryRxDataText;

            private set => SetProperty(ref _pressureTelemetryRxDataText, value);

        }



        public string EnterAtpRxDataText

        {

            get => _enterAtpRxDataText;

            private set => SetProperty(ref _enterAtpRxDataText, value);

        }



        public string ExitAtpRxDataText

        {

            get => _exitAtpRxDataText;

            private set => SetProperty(ref _exitAtpRxDataText, value);

        }



        public bool IsInAtp

        {

            get => _isInAtp;

            private set => SetProperty(ref _isInAtp, value);

        }



        public int CurrentGearIndex

        {

            get => _currentGearIndex;

            private set => SetProperty(ref _currentGearIndex, value);

        }



        public string VoltageGear

        {

            get => _voltageGear;

            set

            {

                if (!SetProperty(ref _voltageGear, value))

                    return;



                if (_suppressVoltageGearChanged)

                    return;



                if (string.IsNullOrWhiteSpace(value))

                {

                    CurrentGearIndex = 0;

                    return;

                }



                var gearIndex = value switch

                {

                    "1挡" => 1,

                    "2挡" => 2,

                    "3挡" => 3,

                    _ => 1

                };



                CurrentGearIndex = gearIndex;

            }

        }



        public bool IsMtx532RealHardware

        {

            get => _isMtx532RealHardware;

            private set

            {

                if (SetProperty(ref _isMtx532RealHardware, value))

                {

                    Mtx532ModeText = value ? "MTX532：已连接" : "MTX532：未连接";

                }

            }

        }



        public string Mtx532ModeText

        {

            get => _mtx532ModeText;

            private set => SetProperty(ref _mtx532ModeText, value);

        }



        public string LastTestTime

        {

            get => _lastTestTime;

            private set => SetProperty(ref _lastTestTime, value);

        }



        public string LastTestResult

        {

            get => _lastTestResult;

            private set => SetProperty(ref _lastTestResult, value);

        }



        public string PreviousTestTime

        {

            get => _previousTestTime;

            private set => SetProperty(ref _previousTestTime, value);

        }



        public string PreviousTestResult

        {

            get => _previousTestResult;

            private set => SetProperty(ref _previousTestResult, value);

        }



        public bool IsManualTestRunning

        {

            get => _isManualTestRunning;

            private set

            {

                if (SetProperty(ref _isManualTestRunning, value))

                {

                    RaisePropertyChanged(nameof(CanEditStepControls));

                }

            }

        }



        public bool IsAutoTestRunning

        {

            get => _isAutoTestRunning;

            private set

            {

                if (SetProperty(ref _isAutoTestRunning, value))

                {

                    RaisePropertyChanged(nameof(CanEditStepControls));

                }

            }

        }



        public bool IsBusy

        {

            get => _isBusy;

            private set

            {

                if (SetProperty(ref _isBusy, value))

                {

                    RaisePropertyChanged(nameof(CanEditStepControls));

                }

            }

        }



        private void OnManualTest()

        {

            if (IsManualTestRunning)

            {

                _ = StopManualTestAsync();

                return;

            }



            _ = StartManualTestAsync();

        }



        private void OnAutoTest()

        {

            if (IsAutoTestRunning)

            {

                _ = StopAutoTestAsync();

                return;

            }



            _ = RunAutoTestAsync();

        }



        private async Task StartManualTestAsync()

        {

            if (IsBusy)

                return;



            await _manualTestLock.WaitAsync();

            try

            {

                if (IsBusy)

                    return;



                IsBusy = true;

                try

                {

                    EnsureManualArincChannels();



                    IsManualTestRunning = true;

                    PressureTelemetryValueText = "--";

                    PressureTelemetryRxDataText = "--";

                    EnterAtpRxDataText = "--";

                    ExitAtpRxDataText = "--";

                    IsInAtp = false;

                    LastTestTime = "--";

                    LastTestResult = "--";



                    _simulation.IsRealProduct = AppConstants.Arinc429IsRealProduct;

                    _simulation.ArincRate = ArincRate;

                    _simulation.SimProductArincRate = ArincRate;

                    _simulation.SimProductRxChannelIndex = 4;

                    _simulation.SimProductTxChannelIndex = 5;



                    AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试启动({(_simulation.IsRealProduct ? "真实产品模式" : "仿真模式")})：打开ARINC429");

                    try
                    {
                        var api = Prism.Ioc.ContainerLocator.Container.Resolve(typeof(MeasureControl.Services.HardwareApis.IComponentPowerStateApi)) as MeasureControl.Services.HardwareApis.IComponentPowerStateApi;
                        if (api != null)
                            await api.ApplyComponent28VStateAsync(CancellationToken.None);
                    }
                    catch { }

                    await _simulation.StartAsync(TestTxChannel, TestRxChannel, msg => AddLog(msg));

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试已启动");

                }

                catch (Exception ex)

                {

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试启动失败: {ex.Message}");

                    IsManualTestRunning = false;

                }

                finally

                {

                    IsBusy = false;

                }

            }

            finally

            {

                _manualTestLock.Release();

            }

        }



        private async Task StopManualTestAsync()

        {

            await _manualTestLock.WaitAsync();

            try

            {

                if (!IsManualTestRunning)

                    return;



                IsBusy = true;

                try

                {

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 停止手动测试：关闭ARINC429");

                    try

                    {

                        await _simulation.StopAsync(msg => AddLog(msg));

                    }

                    catch

                    {

                    }



                    try

                    {

                        await DisconnectMtx532Async();

                    }

                    catch

                    {

                    }



                    IsInAtp = false;

                    IsManualTestRunning = false;

                }

                finally

                {

                    IsBusy = false;

                }

            }

            finally

            {

                _manualTestLock.Release();

            }

        }



        private async Task OnSendEnterAtpAsync()

        {

            if (!IsManualTestRunning || IsBusy)

                return;



            await _arincOpLock.WaitAsync();

            try

            {

                IsBusy = true;

                try

                {

                    await _simulation.ClearRxFifoAsync(EnterAtpRxChannel);

                    await Task.Delay(20);



                    var token = CancellationToken.None;



                    AddLog($"[{DateTime.Now:HH:mm:ss}] 发送进入ATP：TX={EnterAtpTxChannel}, RX={EnterAtpRxChannel}");

                    await _simulation.SendBenchCommandOnlyAsync(EnterAtpTxChannel, EnterAtpCommand8, msg => AddLog(msg), token);



                    var resp = await _simulation.WaitBenchResponse8Async(

                        EnterAtpRxChannel,

                        b => b != null && b.SequenceEqual(EnterAtpOk8),

                        timeoutMs: 1200,

                        log: msg => AddLog(msg),

                        token: token);



                    if (resp == null)

                    {

                        AddLog($"[{DateTime.Now:HH:mm:ss}] 进入ATP超时");

                        SetLastTestResult("FAIL");

                        IsInAtp = false;

                        return;

                    }



                    EnterAtpRxDataText = "0x" + FormatData(resp);

                    IsInAtp = true;

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 进入ATP成功");

                }

                finally

                {

                    IsBusy = false;

                }

            }

            catch (Exception ex)

            {

                AddLog($"[{DateTime.Now:HH:mm:ss}] 进入ATP异常: {ex.Message}");

            }

            finally

            {

                _arincOpLock.Release();

            }

        }



        private async Task OnSendExitAtpAsync()

        {

            if (!IsManualTestRunning || IsBusy)

                return;



            await _arincOpLock.WaitAsync();

            try

            {

                IsBusy = true;

                try

                {

                    await _simulation.ClearRxFifoAsync(ExitAtpRxChannel);

                    await Task.Delay(20);



                    var token = CancellationToken.None;



                    AddLog($"[{DateTime.Now:HH:mm:ss}] 发送退出ATP：TX={ExitAtpTxChannel}, RX={ExitAtpRxChannel}");

                    await _simulation.SendBenchCommandOnlyAsync(ExitAtpTxChannel, ExitAtpCommand8, msg => AddLog(msg), token);



                    var resp = await _simulation.WaitBenchResponse8Async(

                        ExitAtpRxChannel,

                        b => b != null && b.SequenceEqual(ExitAtpOk8),

                        timeoutMs: 1200,

                        log: msg => AddLog(msg),

                        token: token);



                    if (resp == null)

                    {

                        AddLog($"[{DateTime.Now:HH:mm:ss}] 退出ATP超时");

                        SetLastTestResult("FAIL");

                        return;

                    }



                    ExitAtpRxDataText = "0x" + FormatData(resp);

                    IsInAtp = false;

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 退出ATP成功");

                }

                finally

                {

                    IsBusy = false;

                }

            }

            catch (Exception ex)

            {

                AddLog($"[{DateTime.Now:HH:mm:ss}] 退出ATP异常: {ex.Message}");

            }

            finally

            {

                _arincOpLock.Release();

            }

        }



        private async Task OnSetSelectedGearVoltageAsync()

        {

            if (IsBusy)

                return;



            var gearIndex = CurrentGearIndex;

            if (gearIndex is < 1 or > 3)

            {

                AddLog($"[{DateTime.Now:HH:mm:ss}] 请先选择接入电压挡位");

                return;

            }



            double voltageV = gearIndex switch

            {

                1 => 0.25,

                2 => 5.0,

                3 => 9.75,

                _ => 0.25

            };



            try

            {

                IsBusy = true;

                try

                {

                    var token = CancellationToken.None;

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 设置档位{gearIndex}：AO1={voltageV.ToString("0.###", CultureInfo.InvariantCulture)}V");



                    var ok = await OutputVoltageAsync(voltageV, token);

                    if (!ok)

                    {

                        SetLastTestResult("FAIL");

                        AddLog($"[{DateTime.Now:HH:mm:ss}] 电压输出失败");

                        return;

                    }

                }

                finally

                {

                    IsBusy = false;

                }

            }

            catch (Exception ex)

            {

                AddLog($"[{DateTime.Now:HH:mm:ss}] 电压输出异常：{ex.Message}");

            }

        }



        private async Task OnSendControllerRaiaPosTestAsync()

        {

            if (!IsManualTestRunning || IsBusy)

                return;



            if (!IsInAtp)

            {

                AddLog($"[{DateTime.Now:HH:mm:ss}] 请先进入ATP模式");

                return;

            }



            await _arincOpLock.WaitAsync();

            try

            {

                IsBusy = true;

                try

                {

                    var token = CancellationToken.None;



                    AddLog($"[{DateTime.Now:HH:mm:ss}] 发送AB_RAIA_POSITION：TX={ControllerPressureTestTxChannel}, Data={FormatData(AbRaiaPosition8)}");

                    await _simulation.SendBenchCommandOnlyAsync(ControllerPressureTestTxChannel, AbRaiaPosition8, msg => AddLog(msg), token);

                }

                finally

                {

                    IsBusy = false;

                }

            }

            catch (Exception ex)

            {

                AddLog($"[{DateTime.Now:HH:mm:ss}] 控制器RAIA_POS测试异常：{ex.Message}");

            }

            finally

            {

                _arincOpLock.Release();

            }

        }



        private async Task OnReadRaiaPosTelemetryAsync()

        {

            if (!IsManualTestRunning || IsBusy)

                return;



            if (CurrentGearIndex is < 1 or > 3)

            {

                AddLog($"[{DateTime.Now:HH:mm:ss}] 请先选择接入电压挡位");

                return;

            }



            if (!IsInAtp)

            {

                AddLog($"[{DateTime.Now:HH:mm:ss}] 请先进入ATP模式");

                return;

            }



            await _arincOpLock.WaitAsync();

            try

            {

                IsBusy = true;

                try

                {

                    PressureTelemetryValueText = "--";

                    PressureTelemetryRxDataText = "--";



                    var token = CancellationToken.None;

                    await _simulation.ClearRxFifoAsync(PressureTelemetryRxChannel);

                    await Task.Delay(20, token);



                    AddLog($"[{DateTime.Now:HH:mm:ss}] 等待RAIA_POS遥测(07 03 07 02)：RX={PressureTelemetryRxChannel}");

                    var tel = await _simulation.WaitRaiaPosTelemetryAsync(PressureTelemetryRxChannel, timeoutMs: 1500, log: msg => AddLog(msg), token: token);

                    if (tel == null)

                    {

                        AddLog($"[{DateTime.Now:HH:mm:ss}] RAIA_POS遥测超时");

                        SetLastTestResult("FAIL");

                        return;

                    }



                    PressureTelemetryRxDataText = "0x" + FormatData(tel);

                    if (!TryParseTelemetryRaiaPos(tel, out var pos))

                    {

                        AddLog($"[{DateTime.Now:HH:mm:ss}] RAIA_POS遥测解析失败");

                        SetLastTestResult("FAIL");

                        PressureTelemetryValueText = "--";

                        return;

                    }



                    PressureTelemetryValueText = pos.ToString("0.####", CultureInfo.InvariantCulture);

                    SetLastTestResult(IsRaiaPosQualified(CurrentGearIndex, pos) ? "PASS" : "FAIL");

                }

                finally

                {

                    IsBusy = false;

                }

            }

            catch (Exception ex)

            {

                AddLog($"[{DateTime.Now:HH:mm:ss}] RAIA_POS回采异常：{ex.Message}");

            }

            finally

            {

                _arincOpLock.Release();

            }

        }



        private async Task RunAutoTestAsync()

        {

            if (IsBusy)

                return;



            await _autoTestLock.WaitAsync();

            try

            {

                if (IsBusy)

                    return;



                IsBusy = true;

                try

                {

                    EnsureManualArincChannels();



                    IsAutoTestRunning = true;

                    PressureTelemetryValueText = "--";

                    PressureTelemetryRxDataText = "--";

                    LastTestTime = "--";

                    LastTestResult = "--";



                    _autoTestCts?.Cancel();

                    _autoTestCts?.Dispose();

                    _autoTestCts = new CancellationTokenSource();



                    var token = _autoTestCts.Token;

                    try
                    {
                        var api = Prism.Ioc.ContainerLocator.Container.Resolve(typeof(MeasureControl.Services.HardwareApis.IComponentPowerStateApi)) as MeasureControl.Services.HardwareApis.IComponentPowerStateApi;
                        if (api != null)
                            await api.ApplyComponent28VStateAsync(token);
                    }
                    catch { }



                    _simulation.IsRealProduct = AppConstants.Arinc429IsRealProduct;

                    _simulation.ArincRate = ArincRate;

                    _simulation.SimProductArincRate = ArincRate;

                    _simulation.SimProductRxChannelIndex = 4;

                    _simulation.SimProductTxChannelIndex = 5;



                    AddLog($"[{DateTime.Now:HH:mm:ss}] ========== 自动测试开始 ==========");

                    await _simulation.StartAsync(TestTxChannel, TestRxChannel, msg => AddLog(msg));



                    await _simulation.ClearRxFifoAsync(TestRxChannel);

                    await Task.Delay(20, token);



                    AddLog($"[{DateTime.Now:HH:mm:ss}] 步骤1：进入ATP");

                    await _simulation.SendBenchCommandOnlyAsync(TestTxChannel, EnterAtpCommand8, msg => AddLog(msg), token);



                    var enterOk = await _simulation.WaitBenchResponse8Async(

                        TestRxChannel,

                        b => b != null && b.SequenceEqual(EnterAtpOk8),

                        timeoutMs: 1200,

                        log: msg => AddLog(msg),

                        token: token);



                    if (enterOk == null)

                    {

                        SetLastTestResult("FAIL");

                        AddLog($"[{DateTime.Now:HH:mm:ss}] 进入ATP超时");

                        return;

                    }



                    await _simulation.ClearRxFifoAsync(TestRxChannel);

                    await Task.Delay(20, token);



                    var failures = new System.Collections.Generic.List<string>();



                    await RunGearAutoAsync(1, 0.25, token, failures);

                    token.ThrowIfCancellationRequested();

                    await RunGearAutoAsync(2, 5.0, token, failures);

                    token.ThrowIfCancellationRequested();

                    await RunGearAutoAsync(3, 9.75, token, failures);



                    await _simulation.ClearRxFifoAsync(TestRxChannel);

                    await Task.Delay(20, token);



                    AddLog($"[{DateTime.Now:HH:mm:ss}] 步骤X：退出ATP");

                    await _simulation.SendBenchCommandOnlyAsync(TestTxChannel, ExitAtpCommand8, msg => AddLog(msg), token);



                    var exitOk = await _simulation.WaitBenchResponse8Async(

                        TestRxChannel,

                        b => b != null && b.SequenceEqual(ExitAtpOk8),

                        timeoutMs: 1200,

                        log: msg => AddLog(msg),

                        token: token);



                    if (exitOk == null)

                    {

                        failures.Add("退出ATP超时");

                    }



                    if (failures.Count == 0)

                    {

                        SetLastTestResult("PASS");

                        AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试结束：PASS");

                    }

                    else

                    {

                        SetLastTestResult("FAIL");

                        AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试结束：FAIL");

                        foreach (var f in failures)

                            AddLog($"[{DateTime.Now:HH:mm:ss}] FAIL原因：{f}");

                    }

                }

                catch (OperationCanceledException)

                {

                    SetLastTestResult("FAIL");

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试已取消");

                }

                catch (Exception ex)

                {

                    SetLastTestResult("FAIL");

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试异常：{ex.Message}");

                }

                finally

                {

                    try

                    {

                        await _simulation.StopAsync(msg => AddLog(msg));

                    }

                    catch

                    {

                    }



                    IsAutoTestRunning = false;

                    IsBusy = false;

                }

            }

            finally

            {

                _autoTestLock.Release();

            }

        }



        private async Task StopAutoTestAsync()

        {

            await _autoTestLock.WaitAsync();

            try

            {

                try

                {

                    _autoTestCts?.Cancel();

                }

                catch

                {

                }

            }

            finally

            {

                _autoTestLock.Release();

            }

        }



        private async Task RunGearAutoAsync(int gearIndex, double voltageV, CancellationToken token, System.Collections.Generic.List<string> failures)

        {

            CurrentGearIndex = gearIndex;



            AddLog($"[{DateTime.Now:HH:mm:ss}] 档位{gearIndex}：设置AO1={voltageV.ToString("0.###", CultureInfo.InvariantCulture)}V");

            var okVoltage = await OutputVoltageAsync(voltageV, token);

            if (!okVoltage)

            {

                failures.Add($"档位{gearIndex}电压输出失败");

                return;

            }



            await Task.Delay(50, token);



            await _simulation.ClearRxFifoAsync(TestRxChannel);

            await Task.Delay(20, token);



            AddLog($"[{DateTime.Now:HH:mm:ss}] 档位{gearIndex}：发送AB_RAIA_POSITION");

            await _simulation.SendBenchCommandOnlyAsync(TestTxChannel, AbRaiaPosition8, msg => AddLog(msg), token);



            AddLog($"[{DateTime.Now:HH:mm:ss}] 档位{gearIndex}：等待RAIA_POS遥测(07 03 07 02)");

            var tel = await _simulation.WaitRaiaPosTelemetryAsync(TestRxChannel, timeoutMs: 1500, log: msg => AddLog(msg), token: token);

            if (tel == null)

            {

                failures.Add($"档位{gearIndex}RAIA_POS遥测超时");

                return;

            }



            PressureTelemetryRxDataText = "0x" + FormatData(tel);

            if (!TryParseTelemetryRaiaPos(tel, out var pos))

            {

                failures.Add($"档位{gearIndex}RAIA_POS遥测解析失败");

                PressureTelemetryValueText = "--";

                return;

            }



            PressureTelemetryValueText = pos.ToString("0.####", CultureInfo.InvariantCulture);

            if (!IsRaiaPosQualified(gearIndex, pos))

            {

                failures.Add($"档位{gearIndex}RAIA_POS不通过：{pos.ToString("0.####", CultureInfo.InvariantCulture)}");

            }

        }



        private async Task<bool> OutputVoltageAsync(double voltageV, CancellationToken token)

        {

            VoltageSetValueText = voltageV.ToString("0.###", CultureInfo.InvariantCulture);



            await _mtxOpLock.WaitAsync(token);

            try

            {

                var ok = await EnsureMtx532ConnectedAsync(token);

                if (!ok)

                {

                    IsMtx532RealHardware = false;

                    return false;

                }



                IsMtx532RealHardware = true;



                await _mtxApi.SetDcAsync(AoChannel, voltageV, enable: true, cancellationToken: token);

                return true;

            }

            catch (Exception ex)

            {

                AddLog($"[{DateTime.Now:HH:mm:ss}] MTX532输出异常：{ex.Message}");

                IsMtx532RealHardware = false;

                return false;

            }

            finally

            {

                _mtxOpLock.Release();

            }

        }



        private async Task<bool> EnsureMtx532ConnectedAsync(CancellationToken token = default)

        {

            if (_mtxApi != null && _mtxApi.IsConnected)

                return true;



            var device = FindMtx532Device();

            if (device == null)

                return false;



            var slot = (device as PxiDeviceBase)?.SlotIndex;

            var options = new Mtx532Options

            {

                SampleRateHz = 1000.0,

                SuppressNativeDialogs = true,

                ResetToZeroOnStop = true,

                ResetDelayMs = 500

            };



            _mtxApi = new Mtx532Api(device, options, slotNumber: slot.HasValue && slot.Value > 0 ? slot.Value : 7);

            await _mtxApi.ConnectAsync(token);

            return _mtxApi.IsConnected;

        }



        private async Task DisconnectMtx532Async()

        {

            if (_mtxApi == null)

                return;



            try

            {

                if (_mtxApi.IsConnected)

                {

                    try { await _mtxApi.ResetAllToZeroAsync(disableAfterReset: true); } catch { }

                    try { await _mtxApi.DisconnectAsync(); } catch { }

                }

            }

            finally

            {

                try { await _mtxApi.DisposeAsync(); } catch { }

                _mtxApi = null;

            }

        }



        private DeviceBase FindMtx532Device()

        {

            try

            {

                var pxiService = ContainerLocator.Container?.Resolve(typeof(IPxiChassisService)) as IPxiChassisService;

                if (pxiService == null)

                    return null;



                var ctx = ContainerLocator.Container?.Resolve(typeof(ISingleBoardTestContextService)) as ISingleBoardTestContextService;

                var chassisName = ctx?.ChassisName ?? string.Empty;



                System.Collections.Generic.List<DeviceBase> devices = null;

                if (!string.IsNullOrWhiteSpace(chassisName))

                {

                    devices = pxiService.GetChassisDevices(chassisName);

                }

                if (devices == null)

                {

                    var all = pxiService.GetAllChassis();

                    if (all != null)

                    {

                        devices = all.Where(c => c?.Devices != null).SelectMany(c => FlattenDevices(c.Devices)).ToList();

                    }

                }



                if (devices == null || devices.Count == 0)

                    return null;



                var dev = FlattenDevices(devices).FirstOrDefault(IsMtx532Device);

                return dev;

            }

            catch

            {

                return null;

            }

        }



        private static System.Collections.Generic.IEnumerable<DeviceBase> FlattenDevices(System.Collections.Generic.IEnumerable<DeviceBase> devices)

        {

            if (devices == null)

                yield break;



            foreach (var d in devices)

            {

                if (d == null)

                    continue;



                yield return d;



                if (d.Children == null)

                    continue;



                foreach (var child in FlattenDevices(d.Children))

                    yield return child;

            }

        }



        private static bool IsMtx532Device(DeviceBase device)

        {

            if (device == null)

                return false;



            if (device is not PxiDeviceBase && !string.Equals(device.DeviceType, "Card", StringComparison.OrdinalIgnoreCase))

                return false;



            var model = (device.Model ?? string.Empty).ToUpperInvariant();

            return model.Contains("MT-X532") || model.Contains("MTX532") || model.Contains("X532");

        }



        private static bool TryParseTelemetryRaiaPos(byte[] frameData, out double position)

        {

            position = 0;

            if (frameData == null || frameData.Length < 8)

                return false;



            if (!IsPrefix(frameData, RaiaPosTelemetryPrefix4))

                return false;



            var intPartRaw = (ushort)((frameData[4] << 8) | frameData[5]);

            var fracPart = (ushort)((frameData[6] << 8) | frameData[7]);

            var signedInt = unchecked((short)intPartRaw);

            var frac = fracPart / 10000.0;

            position = signedInt < 0 ? signedInt - frac : signedInt + frac;

            return true;

        }



        private static bool IsRaiaPosQualified(int gearIndex, double position)

        {

            var (min, max) = gearIndex switch

            {

                1 => (-3.9445, -1.6111),

                2 => (48.8333, 51.1667),

                3 => (101.6111, 103.9445),

                _ => (-3.9445, -1.6111)

            };

            return position >= min && position <= max;

        }



        private void EnsureManualArincChannels()

        {

            var tx = FirstNonEmpty(EnterAtpTxChannel, ExitAtpTxChannel, ControllerPressureTestTxChannel, TestTxChannel);

            var rx = FirstNonEmpty(EnterAtpRxChannel, ExitAtpRxChannel, PressureTelemetryRxChannel, TestRxChannel);



            tx ??= "CH0";

            rx ??= "CH1";



            TestTxChannel = tx;

            TestRxChannel = rx;



            EnterAtpTxChannel ??= tx;

            EnterAtpRxChannel ??= rx;

            ExitAtpTxChannel ??= tx;

            ExitAtpRxChannel ??= rx;



            ControllerPressureTestTxChannel ??= tx;

            PressureTelemetryRxChannel ??= rx;

        }



        private static string FirstNonEmpty(params string[] values)

        {

            if (values == null)

                return null;

            foreach (var v in values)

            {

                if (!string.IsNullOrWhiteSpace(v))

                    return v;

            }

            return null;

        }



        private void AddLog(string message)

        {

            if (string.IsNullOrWhiteSpace(message))

                return;



            try

            {

                var dispatcher = Application.Current?.Dispatcher;

                if (dispatcher != null && !dispatcher.CheckAccess())

                {

                    dispatcher.BeginInvoke(new Action(() => AddLog(message)));

                    return;

                }

            }

            catch

            {

            }



            try

            {

                Logs.Add(message);

            }

            catch

            {

            }



            try

            {

                Debug.WriteLine(message);

            }

            catch

            {

            }

        }



        private void SetLastTestResult(string result)

        {

            try

            {

                PreviousTestTime = LastTestTime;

                PreviousTestResult = LastTestResult;

            }

            catch

            {

            }



            LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

            LastTestResult = result;

        }



        private static string FormatData(byte[] data)

        {

            if (data == null || data.Length == 0)

                return string.Empty;

            return string.Join(" ", data.Select(b => b.ToString("X2")));

        }



        private static bool IsPrefix(byte[] data, byte[] prefix)

        {

            if (data == null || prefix == null)

                return false;

            if (data.Length < prefix.Length)

                return false;

            for (int i = 0; i < prefix.Length; i++)

            {

                if (data[i] != prefix[i])

                    return false;

            }

            return true;

        }



        public void Dispose()

        {

            try { _autoTestCts?.Cancel(); } catch { }

            try { _autoTestCts?.Dispose(); } catch { }

            try { _autoTestCts = null; } catch { }



            try { _simulation.StopAsync(_ => { }).GetAwaiter().GetResult(); } catch { }

            try { DisconnectMtx532Async().GetAwaiter().GetResult(); } catch { }

        }

    }

}
