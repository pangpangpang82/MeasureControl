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

using MeasureControl.Simulations.A_C_6_10_2_1;



namespace MeasureControl.ViewModels.SingleBoardTest.AirController

{

    public sealed class A_C_6_10_2_1ViewModel : BindableBase, IDisposable

    {

        private static readonly byte[] EnterAtpCommand8 = { 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00 };

        private static readonly byte[] EnterAtpOk8 = { 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x02 };

        private static readonly byte[] ExitAtpCommand8 = { 0x00, 0x02, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01 };

        private static readonly byte[] ExitAtpOk8 = { 0x00, 0x02, 0x00, 0x01, 0x00, 0x00, 0x00, 0x03 };



        private static readonly byte[] AbBpsPressure8 = { 0x07, 0x03, 0x02, 0x01, 0x00, 0x00, 0x00, 0x00 };

        private static readonly byte[] PressureTelemetryPrefix4 = { 0x07, 0x03, 0x02, 0x02 };



        private const string AoChannel = "AO2";



        private readonly A_C_6_10_2_1Simulation _simulation = new A_C_6_10_2_1Simulation();

        private readonly SemaphoreSlim _arincOpLock = new SemaphoreSlim(1, 1);

        private readonly SemaphoreSlim _manualTestLock = new SemaphoreSlim(1, 1);

        private readonly SemaphoreSlim _autoTestLock = new SemaphoreSlim(1, 1);

        private readonly SemaphoreSlim _mtxOpLock = new SemaphoreSlim(1, 1);



        private CancellationTokenSource _autoTestCts;



        private IMtx532Api _mtxApi;



        private string _testTxChannel;

        private string _testRxChannel;



        private string _controllerPressureTestTxChannel;

        private string _controllerPressureTestRxChannel;

        private string _controllerPressureTestRxDataText;



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



        private bool _gear1Checked;

        private bool _gear2Checked;

        private bool _gear3Checked;

        private bool _suppressGearCheckChanged;



        private string _lastTestTime;

        private string _lastTestResult;

        private string _previousTestTime;

        private string _previousTestResult;



        private bool _isManualTestRunning;

        private bool _isAutoTestRunning;

        private bool _isBusy;



        private double _arincRate = 100000.0;



        // 压力遥测持续监听（AC_6_4风格）

        private CancellationTokenSource _telemetryListeningCts;

        private Task _telemetryListeningTask;



        public A_C_6_10_2_1ViewModel()

        {

            _testTxChannel = "CH0";

            _testRxChannel = "CH1";



            _controllerPressureTestTxChannel = null;

            _controllerPressureTestRxChannel = null;

            _controllerPressureTestRxDataText = "--";



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



            _gear1Checked = true;

            _gear2Checked = false;

            _gear3Checked = false;



            Mtx532ModeText = "MTX532：未连接";



            LastTestTime = "--";

            LastTestResult = "--";

            PreviousTestTime = "--";

            PreviousTestResult = "--";



            ManualTestCommand = new DelegateCommand(OnManualTest);

            AutoTestCommand = new DelegateCommand(OnAutoTest);



            ToggleMtx532HardwareCommand = new DelegateCommand(OnToggleMtx532Hardware);

            SendEnterAtpCommand = new DelegateCommand(async () => await OnSendEnterAtpAsync());

            TestGear1Command = new DelegateCommand(async () => await OnTestGearAsync(1));

            TestGear2Command = new DelegateCommand(async () => await OnTestGearAsync(2));

            TestGear3Command = new DelegateCommand(async () => await OnTestGearAsync(3));

            TestSelectedGearCommand = new DelegateCommand(async () => await OnTestGearAsync(CurrentGearIndex));

            SendExitAtpCommand = new DelegateCommand(async () => await OnSendExitAtpAsync());

            ClearLogCommand = new DelegateCommand(() => Logs.Clear());



            SendSetControllerVoltageCommand = new DelegateCommand(async () => await OnSetSelectedGearVoltageAsync());

            SendControllerPressureTestCommand = new DelegateCommand(async () => await OnSendControllerPressureTestAsync());

            TestPressureTelemetryCommand = new DelegateCommand(async () => await OnReadPressureTelemetryAsync());



            _simulation.GetCurrentGearIndex = () => CurrentGearIndex <= 0 ? 1 : CurrentGearIndex;

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

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 设置档位{gearIndex}：AO2={voltageV.ToString("0.###", CultureInfo.InvariantCulture)}V");

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



        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();



        public DelegateCommand ManualTestCommand { get; }

        public DelegateCommand AutoTestCommand { get; }

        public DelegateCommand ToggleMtx532HardwareCommand { get; }

        public DelegateCommand SendEnterAtpCommand { get; }

        public DelegateCommand TestGear1Command { get; }

        public DelegateCommand TestGear2Command { get; }

        public DelegateCommand TestGear3Command { get; }

        public DelegateCommand TestSelectedGearCommand { get; }

        public DelegateCommand SendExitAtpCommand { get; }

        public DelegateCommand ClearLogCommand { get; }

        public DelegateCommand SendSetControllerVoltageCommand { get; }

        public DelegateCommand SendControllerPressureTestCommand { get; }

        public DelegateCommand TestPressureTelemetryCommand { get; }



        public bool Gear1Checked
        {
            get => _gear1Checked;
            set
            {
                if (_suppressGearCheckChanged)
                {
                    SetProperty(ref _gear1Checked, value);
                    return;
                }

                if (!SetProperty(ref _gear1Checked, value))
                    return;

                if (value)
                {
                    SelectGear(1);
                }
                else
                {
                    EnsureOneGearSelected();
                }
            }
        }

        public bool Gear2Checked
        {
            get => _gear2Checked;
            set
            {
                if (_suppressGearCheckChanged)
                {
                    SetProperty(ref _gear2Checked, value);
                    return;
                }

                if (!SetProperty(ref _gear2Checked, value))
                    return;

                if (value)
                {
                    SelectGear(2);
                }
                else
                {
                    EnsureOneGearSelected();
                }
            }
        }

        public bool Gear3Checked
        {
            get => _gear3Checked;
            set
            {
                if (_suppressGearCheckChanged)
                {
                    SetProperty(ref _gear3Checked, value);
                    return;
                }

                if (!SetProperty(ref _gear3Checked, value))
                    return;

                if (value)
                {
                    SelectGear(3);
                }
                else
                {
                    EnsureOneGearSelected();
                }
            }
        }

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

        public string ControllerPressureTestRxChannel
        {
            get => _controllerPressureTestRxChannel;
            set => SetProperty(ref _controllerPressureTestRxChannel, value);
        }

        public string ControllerPressureTestRxDataText
        {
            get => _controllerPressureTestRxDataText;
            private set => SetProperty(ref _controllerPressureTestRxDataText, value);
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
            private set
            {
                if (SetProperty(ref _currentGearIndex, value))
                {
                    SyncVoltageGearToCurrentGear();
                }
            }
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

                SelectGear(gearIndex);
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

        public bool CanEditStepControls => !IsAutoTestRunning && !IsBusy;



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

        private void EnsureManualArincChannels()
        {
            var tx = FirstNonEmpty(EnterAtpTxChannel, ExitAtpTxChannel, ControllerPressureTestTxChannel, TestTxChannel);
            var rx = FirstNonEmpty(EnterAtpRxChannel, ExitAtpRxChannel, ControllerPressureTestRxChannel, PressureTelemetryRxChannel, TestRxChannel);

            tx ??= "CH0";
            rx ??= "CH1";

            TestTxChannel = tx;
            TestRxChannel = rx;

            EnterAtpTxChannel ??= tx;
            EnterAtpRxChannel ??= rx;
            ExitAtpTxChannel ??= tx;
            ExitAtpRxChannel ??= rx;

            ControllerPressureTestTxChannel ??= tx;
            ControllerPressureTestRxChannel ??= rx;
            PressureTelemetryRxChannel ??= rx;
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

        private void SelectGear(int gearIndex)
        {
            _suppressGearCheckChanged = true;
            try
            {
                Gear1Checked = gearIndex == 1;
                Gear2Checked = gearIndex == 2;
                Gear3Checked = gearIndex == 3;
            }
            finally
            {
                _suppressGearCheckChanged = false;
            }

            CurrentGearIndex = gearIndex;
        }

        private void SyncVoltageGearToCurrentGear()
        {
            _suppressVoltageGearChanged = true;
            try
            {
                VoltageGear = CurrentGearIndex switch
                {
                    <= 0 => null,
                    1 => "1挡",
                    2 => "2挡",
                    3 => "3挡",
                    _ => "1挡"
                };
            }
            finally
            {
                _suppressVoltageGearChanged = false;
            }
        }

        private void EnsureOneGearSelected()
        {
            if (Gear1Checked || Gear2Checked || Gear3Checked)
            {
                if (Gear1Checked) CurrentGearIndex = 1;
                else if (Gear2Checked) CurrentGearIndex = 2;
                else if (Gear3Checked) CurrentGearIndex = 3;
                return;
            }

            SelectGear(CurrentGearIndex <= 0 ? 1 : CurrentGearIndex);
        }

        private void OnManualTest()
        {
            AddLog($"[{DateTime.Now:HH:mm:ss}] 点击手动测试：IsManualTestRunning={IsManualTestRunning}, IsBusy={IsBusy}");
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

        private void OnToggleMtx532Hardware()
        {
            _ = ToggleMtx532HardwareAsync();
        }

        private async Task ToggleMtx532HardwareAsync()
        {
            await _mtxOpLock.WaitAsync();
            try
            {
                if (IsBusy)
                    return;

                IsBusy = true;
                try
                {
                    if (IsMtx532RealHardware)
                    {
                        await DisconnectMtx532Async();
                        IsMtx532RealHardware = false;
                        AddLog($"[{DateTime.Now:HH:mm:ss}] MTX532断开连接");
                        return;
                    }

                    var ok = await EnsureMtx532ConnectedAsync();
                    if (ok)
                    {
                        IsMtx532RealHardware = true;
                        AddLog($"[{DateTime.Now:HH:mm:ss}] MTX532连接成功");
                    }
                    else
                    {
                        AddLog($"[{DateTime.Now:HH:mm:ss}] MTX532连接失败：未找到或连接失败");
                    }
                }
                finally
                {
                    IsBusy = false;
                }
            }
            finally
            {
                _mtxOpLock.Release();
            }
        }

        private async Task StartManualTestAsync()
        {
            if (IsBusy)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试启动被阻止：当前IsBusy=True");
                return;
            }

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

                    _simulation.IsRealProduct = false;
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

                    StartPressureTelemetryListeningIfNeeded();
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

        private async Task StopManualTestAsync()
        {
            if (IsBusy)
                return;

            await StopPressureTelemetryListeningAsync();

            await _manualTestLock.WaitAsync();
            try
            {
                if (IsBusy)
                    return;

                IsBusy = true;
                try
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试停止：释放ARINC429资源");
                    await _simulation.StopAsync(msg => AddLog(msg));
                }
                catch (Exception ex)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 停止失败: {ex.Message}");
                }
                finally
                {
                    IsManualTestRunning = false;
                    IsBusy = false;
                }
            }
            finally
            {
                _manualTestLock.Release();
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

                    _simulation.IsRealProduct = false;
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

            AddLog($"[{DateTime.Now:HH:mm:ss}] 档位{gearIndex}：设置AO2={voltageV.ToString("0.###", CultureInfo.InvariantCulture)}V");

            var okVoltage = await OutputVoltageAsync(voltageV, token);
            if (!okVoltage)
            {
                failures.Add($"档位{gearIndex}电压输出失败");
                return;
            }

            await Task.Delay(50, token);

            await _simulation.ClearRxFifoAsync(TestRxChannel);
            await Task.Delay(20, token);

            AddLog($"[{DateTime.Now:HH:mm:ss}] 档位{gearIndex}：发送AB_BPS_PRESSURE");
            await _simulation.SendBenchCommandOnlyAsync(TestTxChannel, AbBpsPressure8, msg => AddLog(msg), token);

            var confirm = await _simulation.WaitBenchResponse8Async(
                TestRxChannel,
                b => b != null && b.SequenceEqual(AbBpsPressure8),
                timeoutMs: 1200,
                log: msg => AddLog(msg),
                token: token);

            if (confirm == null)
            {
                failures.Add($"档位{gearIndex}确认帧超时");
                return;
            }

            AddLog($"[{DateTime.Now:HH:mm:ss}] 档位{gearIndex}：等待压力遥测(07 03 02 02)");
            var tel = await _simulation.WaitPressureTelemetryAsync(TestRxChannel, timeoutMs: 1500, log: msg => AddLog(msg), token: token);
            if (tel == null)
            {
                failures.Add($"档位{gearIndex}压力遥测超时");
                return;
            }

            PressureTelemetryRxDataText = "0x" + FormatData(tel);
            if (!TryParseTelemetryPressure(tel, out var pressureBar))
            {
                failures.Add($"档位{gearIndex}压力遥测解析失败");
                PressureTelemetryValueText = "--";
                return;
            }

            PressureTelemetryValueText = pressureBar.ToString("0.####", CultureInfo.InvariantCulture);

            if (!IsPressureQualified(gearIndex, pressureBar))
            {
                failures.Add($"档位{gearIndex}压力不通过：{pressureBar.ToString("0.####", CultureInfo.InvariantCulture)}Bar");
            }
        }

        private async Task OnTestGearAsync(int gearIndex)
        {
            if (!IsManualTestRunning || IsBusy)
                return;

            if (!IsInAtp)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 请先进入ATP模式");
                return;
            }

            double voltageV = gearIndex switch
            {
                1 => 0.25,
                2 => 5.0,
                3 => 9.75,
                _ => 0.25
            };

            await _arincOpLock.WaitAsync();
            try
            {
                IsBusy = true;
                try
                {
                    CurrentGearIndex = gearIndex;

                    PressureTelemetryValueText = "--";
                    PressureTelemetryRxDataText = "--";

                    var token = CancellationToken.None;

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 手动档位{gearIndex}：设置AO2={voltageV.ToString("0.###", CultureInfo.InvariantCulture)}V");
                    var okVoltage = await OutputVoltageAsync(voltageV, token);
                    if (!okVoltage)
                    {
                        SetLastTestResult("FAIL");
                        AddLog($"[{DateTime.Now:HH:mm:ss}] 电压输出失败");
                        return;
                    }

                    await Task.Delay(50);

                    await _simulation.ClearRxFifoAsync(ControllerPressureTestRxChannel);
                    await Task.Delay(20);

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 发送AB_BPS_PRESSURE：TX={ControllerPressureTestTxChannel}");
                    await _simulation.SendBenchCommandOnlyAsync(ControllerPressureTestTxChannel, AbBpsPressure8, msg => AddLog(msg), token);

                    var confirm = await _simulation.WaitBenchResponse8Async(
                        ControllerPressureTestRxChannel,
                        b => b != null && b.SequenceEqual(AbBpsPressure8),
                        timeoutMs: 1200,
                        log: msg => AddLog(msg),
                        token: token);

                    if (confirm == null)
                    {
                        SetLastTestResult("FAIL");
                        AddLog($"[{DateTime.Now:HH:mm:ss}] 确认帧超时");
                        return;
                    }

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 等待压力遥测：RX={PressureTelemetryRxChannel}");
                    var tel = await _simulation.WaitPressureTelemetryAsync(PressureTelemetryRxChannel, timeoutMs: 1500, log: msg => AddLog(msg), token: token);
                    if (tel == null)
                    {
                        SetLastTestResult("FAIL");
                        AddLog($"[{DateTime.Now:HH:mm:ss}] 压力遥测超时");
                        return;
                    }

                    PressureTelemetryRxDataText = "0x" + FormatData(tel);
                    if (!TryParseTelemetryPressure(tel, out var pressureBar))
                    {
                        SetLastTestResult("FAIL");
                        AddLog($"[{DateTime.Now:HH:mm:ss}] 压力遥测解析失败");
                        PressureTelemetryValueText = "--";
                        return;
                    }

                    PressureTelemetryValueText = pressureBar.ToString("0.####", CultureInfo.InvariantCulture);

                    var pass = IsPressureQualified(gearIndex, pressureBar);
                    SetLastTestResult(pass ? "PASS" : "FAIL");
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 手动档位{gearIndex}测试结束：{LastTestResult}");
                }
                finally
                {
                    IsBusy = false;
                }
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试异常：{ex.Message}");
            }
            finally
            {
                _arincOpLock.Release();
            }
        }

        private async Task OnSendControllerPressureTestAsync()
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
                    ControllerPressureTestRxDataText = "--";

                    var token = CancellationToken.None;

                    await _simulation.ClearRxFifoAsync(ControllerPressureTestRxChannel);
                    await Task.Delay(20, token);

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 发送AB_BPS_PRESSURE：TX={ControllerPressureTestTxChannel}, RX={ControllerPressureTestRxChannel}, Data={FormatData(AbBpsPressure8)}");
                    await _simulation.SendBenchCommandOnlyAsync(ControllerPressureTestTxChannel, AbBpsPressure8, msg => AddLog(msg), token);

                    var confirm = await _simulation.WaitBenchResponse8Async(
                        ControllerPressureTestRxChannel,
                        b => b != null && b.SequenceEqual(AbBpsPressure8),
                        timeoutMs: 1200,
                        log: msg => AddLog(msg),
                        token: token);

                    if (confirm == null)
                    {
                        AddLog($"[{DateTime.Now:HH:mm:ss}] 控制器压力测试确认帧超时");
                        SetLastTestResult("FAIL");
                        return;
                    }

                    ControllerPressureTestRxDataText = "0x" + FormatData(confirm);
                }
                finally
                {
                    IsBusy = false;
                }
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 控制器压力测试异常：{ex.Message}");
            }
            finally
            {
                _arincOpLock.Release();
            }
        }

        private async Task OnReadPressureTelemetryAsync()
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

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 等待压力遥测(07 03 02 02)：RX={PressureTelemetryRxChannel}");
                    var tel = await _simulation.WaitPressureTelemetryAsync(PressureTelemetryRxChannel, timeoutMs: 1500, log: msg => AddLog(msg), token: token);
                    if (tel == null)
                    {
                        AddLog($"[{DateTime.Now:HH:mm:ss}] 压力遥测超时");
                        SetLastTestResult("FAIL");
                        return;
                    }

                    PressureTelemetryRxDataText = "0x" + FormatData(tel);
                    if (!TryParseTelemetryPressure(tel, out var pressureBar))
                    {
                        AddLog($"[{DateTime.Now:HH:mm:ss}] 压力遥测解析失败");
                        SetLastTestResult("FAIL");
                        PressureTelemetryValueText = "--";
                        return;
                    }

                    PressureTelemetryValueText = pressureBar.ToString("0.####", CultureInfo.InvariantCulture);
                    SetLastTestResult(IsPressureQualified(CurrentGearIndex, pressureBar) ? "PASS" : "FAIL");
                }
                finally
                {
                    IsBusy = false;
                }
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 压力回采异常：{ex.Message}");
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

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 发送退出ATP：TX={ExitAtpTxChannel}, RX={ExitAtpRxChannel}, Data={FormatData(ExitAtpCommand8)}");
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



        private static bool TryParseTelemetryPressure(byte[] frameData, out double pressure)

        {

            pressure = 0;

            if (frameData == null || frameData.Length < 8)

                return false;



            if (!IsPrefix(frameData, PressureTelemetryPrefix4))

                return false;



            var intPartRaw = (ushort)((frameData[4] << 8) | frameData[5]);

            var fracPart = (ushort)((frameData[6] << 8) | frameData[7]);

            var signedInt = unchecked((short)intPartRaw);

            var frac = fracPart / 10000.0;

            pressure = signedInt < 0 ? signedInt - frac : signedInt + frac;

            return true;

        }



        private static bool IsPressureQualified(int gearIndex, double pressureBar)

        {

            var (min, max) = gearIndex switch

            {

                1 => (-0.2445, -0.0999),

                2 => (3.0277, 3.1723),

                3 => (6.2999, 6.4445),

                _ => (-0.2445, -0.0999)

            };

            return pressureBar >= min && pressureBar <= max;

        }



        private static string FormatData(byte[] data)

        {

            if (data == null || data.Length == 0)

                return string.Empty;

            return string.Join(" ", data.Select(b => b.ToString("X2")));

        }



        #region 持续压力遥测监听 (AC_6_4风格)



        private void StartPressureTelemetryListeningIfNeeded()

        {

            if (_telemetryListeningTask != null)

                return;

            if (string.IsNullOrWhiteSpace(PressureTelemetryRxChannel))

                return;



            _telemetryListeningCts?.Cancel();

            _telemetryListeningCts?.Dispose();

            _telemetryListeningCts = new CancellationTokenSource();

            var token = _telemetryListeningCts.Token;



            _telemetryListeningTask = Task.Run(async () =>

            {

                AddLog($"[{DateTime.Now:HH:mm:ss}] 启动压力遥测持续监听：RX={PressureTelemetryRxChannel}");

                while (!token.IsCancellationRequested)

                {

                    try

                    {

                        var tel = await _simulation.WaitPressureTelemetryAsync(

                            PressureTelemetryRxChannel,

                            timeoutMs: 300,

                            msg => { },

                            token);



                        if (tel != null && TryParseTelemetryPressure(tel, out var pressureBar))

                        {

                            // 更新UI（需要在UI线程）

                            var dispatcher = Application.Current?.Dispatcher;

                            if (dispatcher != null && !dispatcher.CheckAccess())

                            {

                                dispatcher.BeginInvoke(new Action(() =>

                                {

                                    PressureTelemetryRxDataText = "0x" + FormatData(tel);

                                    PressureTelemetryValueText = pressureBar.ToString("0.####", CultureInfo.InvariantCulture);

                                }));

                            }

                            else

                            {

                                PressureTelemetryRxDataText = "0x" + FormatData(tel);

                                PressureTelemetryValueText = pressureBar.ToString("0.####", CultureInfo.InvariantCulture);

                            }

                        }

                        else

                        {

                            await Task.Delay(30, token);

                        }

                    }

                    catch (OperationCanceledException)

                    {

                        break;

                    }

                    catch

                    {

                        try { await Task.Delay(100, token); } catch { break; }

                    }

                }

                AddLog($"[{DateTime.Now:HH:mm:ss}] 压力遥测监听已停止");

            }, token);

        }



        private async Task StopPressureTelemetryListeningAsync()

        {

            try

            {

                _telemetryListeningCts?.Cancel();

            }

            catch { }



            var task = _telemetryListeningTask;

            if (task != null)

            {

                try

                {

                    await Task.WhenAny(task, Task.Delay(500));

                }

                catch { }

            }



            _telemetryListeningTask = null;

            try

            {

                _telemetryListeningCts?.Dispose();

            }

            catch { }

            _telemetryListeningCts = null;

        }



        #endregion



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



            LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            LastTestResult = result;

        }



        public void Dispose()

        {

            try { _autoTestCts?.Cancel(); } catch { }

            try { _autoTestCts?.Dispose(); } catch { }



            try { _simulation?.StopAsync(_ => { }).GetAwaiter().GetResult(); } catch { }



            try { DisconnectMtx532Async().GetAwaiter().GetResult(); } catch { }



            try { _arincOpLock?.Dispose(); } catch { }

            try { _manualTestLock?.Dispose(); } catch { }

            try { _autoTestLock?.Dispose(); } catch { }

            try { _mtxOpLock?.Dispose(); } catch { }

        }

    }

}

