using Prism.Commands;

using Prism.Mvvm;

using Prism.Ioc;

using System;

using System.Collections.Generic;

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

using MeasureControl.Simulations.A_C_6_9_1_1;



namespace MeasureControl.ViewModels.SingleBoardTest.AirController

{

    public sealed class A_C_6_9_1_1ViewModel : BindableBase, IDisposable

    {

        private const string FixedTxChannel = "429_CH0";

        private const string FixedRxChannel = "429_CH2";

        private static readonly byte[] EnterAtpCommand8 = { 0x30, 0x01, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00 };

        private static readonly byte[] ExitAtpCommand8 = { 0x30, 0x02, 0x02, 0x01, 0x00, 0x00, 0x00, 0x00 };



        private static readonly byte[] AbCkptVentsTemperature8 = { 0x07, 0x02, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00 };

        private static readonly byte[] PressureTelemetryPrefix4 = { 0x07, 0x02, 0x01, 0x02 };
        private static readonly byte[] PressureTelemetryRawPrefix4 = { 0x07, 0x02, 0x01, 0x03 };



        private const string AoChannel = "AO5";

        private static readonly string[] Mtx532EnabledAoChannels = { "AO1", "AO2", "AO3", "AO4", "AO5" };

        private const int Mtx532ReadyTimeoutMs = 6000;

        private const int Mtx532ReadyPollMs = 200;

        private const int AutoGearSwitchDelayMs = 1500;

        private const double Mtx532SampleRateHz = 20000.0;



        private readonly A_C_6_9_1_1Simulation _simulation = new A_C_6_9_1_1Simulation();

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
        private string _pressureTelemetryRawRxDataText;



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

        private bool _autoTestEnteredAtp;



        private double _arincRate = 100000.0;



        // 压力遥测持续监听（AC_6_4风格）

        private CancellationTokenSource _telemetryListeningCts;

        private Task _telemetryListeningTask;

        private int _pressureTelemetrySeq;

        private byte[] _lastPressureTelemetryFrame;
        private byte[] _lastPressureTelemetryRawFrame;



        public A_C_6_9_1_1ViewModel()

        {

            _testTxChannel = FixedTxChannel;

            _testRxChannel = FixedRxChannel;



            _controllerPressureTestTxChannel = _testTxChannel;

            _pressureTelemetryRxChannel = _testRxChannel;



            _enterAtpTxChannel = _testTxChannel;

            _enterAtpRxChannel = _testRxChannel;

            _exitAtpTxChannel = _testTxChannel;

            _exitAtpRxChannel = _testRxChannel;



            VoltageSetValueText = "--";

            PressureTelemetryValueText = "--";

            PressureTelemetryRxDataText = "--";
            PressureTelemetryRawRxDataText = "--";



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

                1 => 2.08,

                2 => 3.0,

                3 => 4.08,

                _ => 2.08

            };



            try

            {

                IsBusy = true;

                try

                {

                    var token = CancellationToken.None;

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 设置档位{gearIndex}：AO5={voltageV.ToString("0.###", CultureInfo.InvariantCulture)}V");



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

            get => FixedTxChannel;

        }



        public string TestRxChannel

        {

            get => FixedRxChannel;

        }



        public string ControllerPressureTestTxChannel

        {

            get => FixedTxChannel;

        }



        public string PressureTelemetryRxChannel

        {

            get => FixedRxChannel;

        }



        public string EnterAtpTxChannel

        {

            get => FixedTxChannel;

        }



        public string EnterAtpRxChannel

        {

            get => FixedRxChannel;

        }



        public string ExitAtpTxChannel

        {

            get => FixedTxChannel;

        }



        public string ExitAtpRxChannel

        {

            get => FixedRxChannel;

        }

        public string EnterAtpTxDataText => "0x" + FormatData(EnterAtpCommand8);

        public string TestCommandTxDataText => "0x" + FormatData(AbCkptVentsTemperature8);

        public string ExitAtpTxDataText => "0x" + FormatData(ExitAtpCommand8);



        public double ArincRate

        {

            get => _arincRate;

            set => SetProperty(ref _arincRate, value);

        }



        public string VoltageSetValueText

        {

            get => _voltageSetValueText;

            private set

            {

                SetProperty(ref _voltageSetValueText, value);

            }

        }



        public string PressureTelemetryValueText

        {

            get => _pressureTelemetryValueText;

            private set

            {

                SetProperty(ref _pressureTelemetryValueText, value);

            }

        }



        public string PressureTelemetryRxDataText

        {

            get => _pressureTelemetryRxDataText;

            private set

            {

                SetProperty(ref _pressureTelemetryRxDataText, value);

            }

        }



        public string PressureTelemetryRawRxDataText

        {

            get => _pressureTelemetryRawRxDataText;

            private set

            {

                SetProperty(ref _pressureTelemetryRawRxDataText, value);

            }

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

                    var token = CancellationToken.None;

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 发送AB_CKPTVENTS_Temperature：TX={ControllerPressureTestTxChannel}, Data={FormatData(AbCkptVentsTemperature8)}");

                    await _simulation.SendBenchCommandOnlyAsync(ControllerPressureTestTxChannel, AbCkptVentsTemperature8, msg => AddLog(msg), token);

                }

                finally

                {

                    IsBusy = false;

                }

            }

            catch (Exception ex)

            {

                AddLog($"[{DateTime.Now:HH:mm:ss}] 控制器温度测试异常：{ex.Message}");

            }

            finally

            {

                _arincOpLock.Release();

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

                    await StopPressureTelemetryListeningAsync();

                    var token = CancellationToken.None;

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 发送退出ATP：TX={ExitAtpTxChannel}, Data={FormatData(ExitAtpCommand8)}");

                    await _simulation.SendBenchCommandOnlyAsync(ExitAtpTxChannel, ExitAtpCommand8, msg => AddLog(msg), token);

                    IsInAtp = false;

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 退出ATP指令已发送");

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



        private static async Task TryApplyComponentDownStateAsync(CancellationToken token)

        {

            try

            {

                var api = Prism.Ioc.ContainerLocator.Container.Resolve(typeof(IComponentPowerStateApi)) as IComponentPowerStateApi;

                if (api != null)

                    await api.ApplyComponentDownStateAsync(token).ConfigureAwait(false);

            }

            catch

            {

            }

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
            await _manualTestLock.WaitAsync();
            try
            {
                if (IsManualTestRunning || IsBusy)
                    return;

                IsManualTestRunning = true;
                ResetDisplayState();
                Interlocked.Exchange(ref _lastPressureTelemetryFrame, null);
                Interlocked.Exchange(ref _lastPressureTelemetryRawFrame, null);
                AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试启动：开始打开设备");

                IsBusy = true;
                try
                {
                    EnsureManualArincChannels();

                    var mtxOk = await PreconnectMtx532ForTestAsync("手动测试", CancellationToken.None);
                    if (!mtxOk)
                    {
                        IsMtx532RealHardware = false;
                        IsManualTestRunning = false;
                        LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                        AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试启动失败：MTX532连接失败");
                        return;
                    }

                    IsMtx532RealHardware = true;

                    try
                    {
                        var api = Prism.Ioc.ContainerLocator.Container.Resolve(typeof(MeasureControl.Services.HardwareApis.IComponentPowerStateApi)) as MeasureControl.Services.HardwareApis.IComponentPowerStateApi;
                        if (api != null)
                            await api.ApplyComponent28VStateAsync(CancellationToken.None);
                    }
                    catch { }

                    _simulation.IsRealProduct = true;
                    _simulation.ArincRate = ArincRate;

                    await _simulation.StartAsync(TestTxChannel, TestRxChannel, msg => AddLog(msg));
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试启动：429板卡已就绪");
                }
                catch (Exception ex)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试启动异常：{ex.Message}");
                    IsManualTestRunning = false;
                    LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
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

                    var token = CancellationToken.None;

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 发送进入ATP：TX={EnterAtpTxChannel}");

                    await _simulation.SendBenchCommandOnlyAsync(EnterAtpTxChannel, EnterAtpCommand8, msg => AddLog(msg), token);

                    IsInAtp = true;

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 进入ATP指令已发送");



                    // 启动持续压力遥测监听（AC_6_4风格）

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
            await _manualTestLock.WaitAsync();
            try
            {
                if (!IsManualTestRunning || IsBusy)
                    return;

                AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试停止：关闭设备");
                IsManualTestRunning = false;

                IsBusy = true;
                try
                {
                    await ShutdownOpenedBoardsForTestEndAsync();
                    IsInAtp = false;
                    LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                }
                catch (Exception ex)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试停止异常：{ex.Message}");
                    LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                }
                finally
                {
                    try { await TryApplyComponentDownStateAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
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
            await _autoTestLock.WaitAsync();
            try
            {
                if (IsBusy)
                    return;

                IsBusy = true;
                IsAutoTestRunning = true;
                try
                {
                    EnsureManualArincChannels();
                    ResetDisplayState();
                    _autoTestEnteredAtp = false;
                    Interlocked.Exchange(ref _lastPressureTelemetryFrame, null);
                    Interlocked.Exchange(ref _lastPressureTelemetryRawFrame, null);

                    _autoTestCts?.Cancel();
                    _autoTestCts?.Dispose();
                    _autoTestCts = new CancellationTokenSource();
                    var token = _autoTestCts.Token;

                    var mtxOk = await PreconnectMtx532ForTestAsync("自动测试", token);
                    if (!mtxOk)
                    {
                        IsMtx532RealHardware = false;
                        SetLastTestResult("FAIL");
                        AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试启动失败：MTX532连接失败");
                        return;
                    }

                    IsMtx532RealHardware = true;

                    try
                    {
                        var api = Prism.Ioc.ContainerLocator.Container.Resolve(typeof(MeasureControl.Services.HardwareApis.IComponentPowerStateApi)) as MeasureControl.Services.HardwareApis.IComponentPowerStateApi;
                        if (api != null)
                            await api.ApplyComponent28VStateAsync(token);
                    }
                    catch { }

                    _simulation.IsRealProduct = true;
                    _simulation.ArincRate = ArincRate;

                    AddLog($"[{DateTime.Now:HH:mm:ss}] ========== 自动测试开始 ==========");
                    await _simulation.StartAsync(TestTxChannel, TestRxChannel, msg => AddLog(msg));

                    await AutoEnterAtpAsync(token);
                    _autoTestEnteredAtp = true;

                    var failures = new List<string>();
                    for (int gear = 1; gear <= 3; gear++)
                    {
                        token.ThrowIfCancellationRequested();
                        await RunGearAutoAsync(gear, token, failures);
                        if (gear < 3)
                        {
                            AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试：档位{gear}完成，等待{AutoGearSwitchDelayMs}ms后切换下一档位");
                            await Task.Delay(AutoGearSwitchDelayMs, token);
                        }
                    }

                    await AutoExitAtpAsync(token);
                    _autoTestEnteredAtp = false;

                    if (failures.Count == 0)
                    {
                        SetLastTestResult("PASS");
                    }
                    else
                    {
                        SetLastTestResult("FAIL");
                        foreach (var f in failures)
                            AddLog($"[{DateTime.Now:HH:mm:ss}] FAIL原因：{f}");
                    }

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试结束：{LastTestResult}");
                }
                catch (OperationCanceledException)
                {
                    SetLastTestResult("已停止");
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试已停止");
                }
                catch (Exception ex)
                {
                    SetLastTestResult("异常");
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试异常：{ex.Message}");
                }
                finally
                {
                    if (_autoTestEnteredAtp)
                    {
                        try { await AutoExitAtpAsync(CancellationToken.None); } catch { }
                        _autoTestEnteredAtp = false;
                    }
                    await ShutdownOpenedBoardsForTestEndAsync();
                    try { await TryApplyComponentDownStateAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                    IsAutoTestRunning = false;
                    IsBusy = false;
                }
            }
            finally
            {
                _autoTestLock.Release();
            }
        }



        private Task StopAutoTestAsync()
        {
            try { _autoTestCts?.Cancel(); } catch { }
            AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试停止请求");
            return Task.CompletedTask;
        }

        private async Task<bool> AutoEnterAtpAsync(CancellationToken token)
        {
            AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试：发送进入ATP");
            await _simulation.SendBenchCommandOnlyAsync(TestTxChannel, EnterAtpCommand8, msg => AddLog(msg), token);
            IsInAtp = true;
            StartPressureTelemetryListeningIfNeeded();
            return true;
        }

        private async Task<bool> AutoExitAtpAsync(CancellationToken token)
        {
            AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试：发送退出ATP");
            try { await StopPressureTelemetryListeningAsync(); } catch { }
            await _simulation.SendBenchCommandOnlyAsync(ExitAtpTxChannel, ExitAtpCommand8, msg => AddLog(msg), token);
            IsInAtp = false;
            return true;
        }



        private async Task RunGearAutoAsync(int gearIndex, CancellationToken token, List<string> failures)

        {

            CurrentGearIndex = gearIndex;



            var voltageV = GetGearVoltage(gearIndex);
            AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试：档位{gearIndex} AO5={voltageV.ToString("0.###", CultureInfo.InvariantCulture)}V");

            var okVoltage = await OutputVoltageAsync(voltageV, token);

            if (!okVoltage)

            {

                failures.Add($"档位{gearIndex} AO5输出失败");

                return;

            }



            AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试：档位{gearIndex} AO5输出后等待稳定3s");
            await Task.Delay(TimeSpan.FromSeconds(3), token);
            StartPressureTelemetryListeningIfNeeded();

            var startSeq = Volatile.Read(ref _pressureTelemetrySeq);
            Interlocked.Exchange(ref _lastPressureTelemetryFrame, null);
            Interlocked.Exchange(ref _lastPressureTelemetryRawFrame, null);

            AddLog($"[{DateTime.Now:HH:mm:ss}] 档位{gearIndex}：发送AB_CKPTVENTS_Temperature");

            await _simulation.SendBenchCommandOnlyAsync(TestTxChannel, AbCkptVentsTemperature8, msg => AddLog(msg), token);

            AddLog($"[{DateTime.Now:HH:mm:ss}] 档位{gearIndex}：等待温度遥测(07 02 01 02)[持续监听]");

            var tel = await WaitNextPressureTelemetryFrameAsync(startSeq, timeoutMs: 1500, token: token);

            if (tel == null)

            {

                failures.Add($"档位{gearIndex}温度遥测超时");

                return;

            }



            PressureTelemetryRxDataText = "0x" + FormatData(tel);
            var rawData = Interlocked.CompareExchange(ref _lastPressureTelemetryRawFrame, null, null);
            if (rawData != null)
            {
                PressureTelemetryRawRxDataText = "0x" + FormatData(rawData);
                LogPressureRawData(rawData);
            }

            if (!TryParseTelemetryPressure(tel, out var pressureBar))

            {

                failures.Add($"档位{gearIndex}温度遥测解析失败");

                PressureTelemetryValueText = "--";

                return;

            }



            PressureTelemetryValueText = pressureBar.ToString("0.####", CultureInfo.InvariantCulture);




            if (!IsPressureQualified(gearIndex, pressureBar))

            {

                failures.Add($"档位{gearIndex}温度不通过：{pressureBar.ToString("0.####", CultureInfo.InvariantCulture)}℃");

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

                1 => 2.08,

                2 => 3.0,

                3 => 4.08,

                _ => 2.08

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



                    AddLog($"[{DateTime.Now:HH:mm:ss}] 手动档位{gearIndex}：设置AO5={voltageV.ToString("0.###", CultureInfo.InvariantCulture)}V");

                    var okVoltage = await OutputVoltageAsync(voltageV, token);

                    if (!okVoltage)

                    {

                        SetLastTestResult("FAIL");

                        AddLog($"[{DateTime.Now:HH:mm:ss}] 电压输出失败");

                        return;

                    }



                    AddLog($"[{DateTime.Now:HH:mm:ss}] 手动档位{gearIndex}：AO5输出后等待稳定3s");
                    await Task.Delay(TimeSpan.FromSeconds(3));
                    StartPressureTelemetryListeningIfNeeded();

                    var startSeq = Volatile.Read(ref _pressureTelemetrySeq);

                    Interlocked.Exchange(ref _lastPressureTelemetryFrame, null);
                    Interlocked.Exchange(ref _lastPressureTelemetryRawFrame, null);

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 发送AB_CKPTVENTS_Temperature：TX={ControllerPressureTestTxChannel}");

                    await _simulation.SendBenchCommandOnlyAsync(ControllerPressureTestTxChannel, AbCkptVentsTemperature8, msg => AddLog(msg), token);

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 等待温度遥测：RX={PressureTelemetryRxChannel}[持续监听]");

                    var tel = await WaitNextPressureTelemetryFrameAsync(startSeq, timeoutMs: 1500, token: token);

                    if (tel == null)

                    {

                        SetLastTestResult("FAIL");

                        AddLog($"[{DateTime.Now:HH:mm:ss}] 温度遥测超时");

                        return;

                    }



                    PressureTelemetryRxDataText = "0x" + FormatData(tel);
                    var rawData = Interlocked.CompareExchange(ref _lastPressureTelemetryRawFrame, null, null);
                    if (rawData != null)
                    {
                        PressureTelemetryRawRxDataText = "0x" + FormatData(rawData);
                        LogPressureRawData(rawData);
                    }

                    if (!TryParseTelemetryPressure(tel, out var pressureBar))

                    {

                        SetLastTestResult("FAIL");

                        AddLog($"[{DateTime.Now:HH:mm:ss}] 温度遥测解析失败");

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



        private async Task<bool> OutputVoltageAsync(double voltageV, CancellationToken token)

        {

            VoltageSetValueText = "--";



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



                AddLog($"[{DateTime.Now:HH:mm:ss}] MTX532档位电压写入开始：{AoChannel}={voltageV.ToString("0.####", CultureInfo.InvariantCulture)}V（1基，第5个物理通道）");
                await SetAo5Async(voltageV, token);
                AddLog($"[{DateTime.Now:HH:mm:ss}] MTX532档位电压写入完成：{AoChannel}={voltageV.ToString("0.####", CultureInfo.InvariantCulture)}V，IsOutputRunning={_mtxApi.IsOutputRunning}");
                if (!_mtxApi.IsOutputRunning)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] MTX532输出未运行，准备等待Ready后启动输出");
                    await WaitForMtx532ReadyAsync(token);
                    await _mtxApi.StartOutputAsync(token);
                    AddLog($"[{DateTime.Now:HH:mm:ss}] MTX532 StartOutputAsync完成，IsOutputRunning={_mtxApi.IsOutputRunning}");
                }

                await Task.Delay(100, token);
                var readBackVoltage = await ReadAo5VoltageAsync(token);
                if (readBackVoltage.HasValue)
                {
                    VoltageSetValueText = readBackVoltage.Value.ToString("0.####", CultureInfo.InvariantCulture);
                    AddLog($"[{DateTime.Now:HH:mm:ss}] {AoChannel}设定={voltageV.ToString("0.####", CultureInfo.InvariantCulture)}V，板卡读回={VoltageSetValueText}V");
                }
                else
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] {AoChannel}板卡读回失败");
                }

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

        private async Task<double?> ReadAo5VoltageAsync(CancellationToken token)
        {
            if (_mtxApi == null || !_mtxApi.IsConnected)
                return null;

            var samples = new List<double>(3);
            for (int i = 0; i < 3; i++)
            {
                token.ThrowIfCancellationRequested();
                var value = await _mtxApi.GetLastOutputVoltageAsync(AoChannel, token);
                samples.Add(value);
                if (i < 2)
                    await Task.Delay(20, token);
            }

            return samples.Count > 0 ? samples.Average() : (double?)null;
        }

        private async Task SetAo5Async(double voltageV, CancellationToken token)
        {
            if (_mtxApi == null || !_mtxApi.IsConnected)
                throw new InvalidOperationException("MTX532未连接");

            await _mtxApi.WriteOnceDcAsync(new Dictionary<string, double>
            {
                ["AO1"] = 0.0,
                ["AO2"] = 0.0,
                ["AO3"] = 0.0,
                ["AO4"] = 0.0,
                ["AO5"] = voltageV
            }, token).ConfigureAwait(false);
        }

        private async Task<bool> PreconnectMtx532ForTestAsync(string testName, CancellationToken token)
        {
            AddLog($"[{DateTime.Now:HH:mm:ss}] {testName}：按钮启动后优先打开MTX532");
            await _mtxOpLock.WaitAsync(token);
            try
            {
                var ok = await EnsureMtx532ConnectedAsync(token);
                AddLog($"[{DateTime.Now:HH:mm:ss}] {testName}：MTX532预打开结果={ok}，IsConnected={_mtxApi?.IsConnected == true}，IsOutputRunning={_mtxApi?.IsOutputRunning == true}");
                return ok;
            }
            finally
            {
                _mtxOpLock.Release();
            }
        }

        private async Task ShutdownMtx532ForTestEndAsync()
        {
            await _mtxOpLock.WaitAsync();
            try
            {
                if (_mtxApi == null)
                {
                    IsMtx532RealHardware = false;
                    VoltageSetValueText = "--";
                    return;
                }

                await DisconnectMtx532Async();
                IsMtx532RealHardware = false;
                VoltageSetValueText = "--";
                AddLog($"[{DateTime.Now:HH:mm:ss}] MTX532已关闭并断开连接");
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] MTX532关闭异常：{ex.Message}");
            }
            finally
            {
                _mtxOpLock.Release();
            }
        }

        private async Task ShutdownOpenedBoardsForTestEndAsync()
        {
            try
            {
                await StopPressureTelemetryListeningAsync();
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 遥测监听停止异常：{ex.Message}");
            }

            try
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] ARINC429板卡关闭开始");
                await _simulation.StopAsync(msg => AddLog(msg));
                AddLog($"[{DateTime.Now:HH:mm:ss}] ARINC429板卡已关闭");
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] ARINC429板卡关闭异常：{ex.Message}");
            }

            await ShutdownMtx532ForTestEndAsync();
        }



        private async Task<bool> EnsureMtx532ConnectedAsync(CancellationToken token = default)

        {

            if (_mtxApi != null && _mtxApi.IsConnected)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] MTX532已连接，跳过重新连接，IsOutputRunning={_mtxApi.IsOutputRunning}");
                return true;
            }



            AddLog($"[{DateTime.Now:HH:mm:ss}] MTX532连接流程开始：硬件AO为1基，目标业务通道={AoChannel}（第5个物理通道）");
            var device = FindMtx532Device();

            if (device == null)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] MTX532连接失败：未找到MTX532(模拟量输出)板卡");
                return false;
            }



            var slotNumber = device is PxiDeviceBase pxi ? pxi.SlotIndex : 7;

            var options = new Mtx532Options

            {

                SampleRateHz = Mtx532SampleRateHz,

                UseOneBasedAoChannelNumbering = true

            };



            _mtxApi = new Mtx532Api(device, options, slotNumber: slotNumber);

            AddLog($"[{DateTime.Now:HH:mm:ss}] MTX532开始ConnectAsync：enabledAoChannels={string.Join(",", Mtx532EnabledAoChannels)}（1基），目标输出={AoChannel}");
            await _mtxApi.ConnectAsync(token, Mtx532EnabledAoChannels).ConfigureAwait(false);

            AddLog($"[{DateTime.Now:HH:mm:ss}] MTX532写初始0V：{AoChannel}=0V（1基，第5个物理通道）");
            await SetAo5Async(0.0, token).ConfigureAwait(false);
            await Task.Delay(300, token).ConfigureAwait(false);
            await WaitForMtx532ReadyAsync(token).ConfigureAwait(false);
            await _mtxApi.StartOutputAsync(token).ConfigureAwait(false);
            await Task.Delay(300, token).ConfigureAwait(false);

            return _mtxApi.IsConnected;

        }

        private async Task WaitForMtx532ReadyAsync(CancellationToken token)
        {
            if (_mtxApi == null || !_mtxApi.IsConnected)
                throw new InvalidOperationException("MTX532未连接");

            var deadline = DateTime.UtcNow.AddMilliseconds(Mtx532ReadyTimeoutMs);
            while (DateTime.UtcNow <= deadline)
            {
                token.ThrowIfCancellationRequested();
                if (await _mtxApi.CanStartOutputAsync(token))
                    return;

                await Task.Delay(Mtx532ReadyPollMs, token);
            }

            throw new InvalidOperationException("MTX532已连接，但在等待超时前仍未准备好输出");
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
            var raw = (ushort)((frameData[6] << 8) | frameData[7]);

            var signedRaw = unchecked((short)raw);

            pressure = signedRaw * 0.01;

            return true;

        }



        private static bool TryParseBase6FromNibbles(byte[] frameData, out long value)
        {
            value = 0;
            if (frameData == null || frameData.Length < 8)
                return false;
            if (!IsPrefix(frameData, PressureTelemetryRawPrefix4))
                return false;

            for (var i = 4; i <= 7; i++)
            {
                var b = frameData[i];
                var hi = (b >> 4) & 0xF;
                var lo = b & 0xF;
                if (hi > 5 || lo > 5)
                    return false;
                value = checked(value * 6 + hi);
                value = checked(value * 6 + lo);
            }

            return true;
        }

        private void LogPressureRawData(byte[] rawData)
        {
            var rawHex = FormatData(rawData);
            if (TryParseBase6FromNibbles(rawData, out var rawBase6Decimal))
                AddLog($"[{DateTime.Now:HH:mm:ss}] AB_CKPTVENTS温度原始数据(07 02 01 03) 后四字节(6进制)->10进制：{rawBase6Decimal}，Data={rawHex}");
            else
                AddLog($"[{DateTime.Now:HH:mm:ss}] AB_CKPTVENTS温度原始数据(07 02 01 03) Data={rawHex}");
        }
        private static bool IsPressureQualified(int gearIndex, double pressureBar)

        {

            var (min, max) = gearIndex switch

            {

                1 => (-65.98, -64.02),

                2 => (25.12, 28.88),

                3 => (134.02, 137.98),

                _ => (-65.98, -64.02)

            };

            return pressureBar >= min && pressureBar <= max;

        }

        private static double GetGearVoltage(int gearIndex)
        {
            return gearIndex switch
            {
                1 => 2.08,
                2 => 3.0,
                3 => 4.08,
                _ => 2.08
            };
        }

        private void ResetDisplayState()
        {
            PressureTelemetryValueText = "--";
            PressureTelemetryRxDataText = "--";
            PressureTelemetryRawRxDataText = "--";
            EnterAtpRxDataText = "--";
            ExitAtpRxDataText = "--";
            VoltageSetValueText = "--";
            LastTestTime = "--";
            LastTestResult = "--";
            IsInAtp = false;
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

            if (!IsInAtp)

                return;

            if (!IsManualTestRunning && !IsAutoTestRunning)

                return;



            _telemetryListeningCts?.Cancel();

            _telemetryListeningCts?.Dispose();

            _telemetryListeningCts = new CancellationTokenSource();

            var token = _telemetryListeningCts.Token;



            _telemetryListeningTask = Task.Run(async () =>

            {

                AddLog($"[{DateTime.Now:HH:mm:ss}] 启动温度遥测持续监听：RX={PressureTelemetryRxChannel}");

                while (!token.IsCancellationRequested)

                {
                    try
                    {
                        var telemetry = await _simulation.WaitTelemetryAsync(
                            PressureTelemetryRxChannel,
                            timeoutMs: 300,
                            msg => { },
                            token);

                        var tel = telemetry.Temperature;
                        var raw = telemetry.Raw;

                        if (tel != null && TryParseTelemetryPressure(tel, out var pressureBar))
                        {
                            var frameCopy = tel.ToArray();
                            var rawCopy = raw?.ToArray();

                            Interlocked.Exchange(ref _lastPressureTelemetryFrame, frameCopy);
                            if (rawCopy != null)
                                Interlocked.Exchange(ref _lastPressureTelemetryRawFrame, rawCopy);
                            Interlocked.Increment(ref _pressureTelemetrySeq);

                            var dispatcher = Application.Current?.Dispatcher;
                            if (dispatcher != null && !dispatcher.CheckAccess())
                            {
                                dispatcher.BeginInvoke(new Action(() =>
                                {
                                    PressureTelemetryRxDataText = "0x" + FormatData(frameCopy);
                                    if (rawCopy != null)
                                        PressureTelemetryRawRxDataText = "0x" + FormatData(rawCopy);
                                    PressureTelemetryValueText = pressureBar.ToString("0.####", CultureInfo.InvariantCulture);
                                }));
                            }
                            else
                            {
                                PressureTelemetryRxDataText = "0x" + FormatData(frameCopy);
                                if (rawCopy != null)
                                    PressureTelemetryRawRxDataText = "0x" + FormatData(rawCopy);
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

                AddLog($"[{DateTime.Now:HH:mm:ss}] 温度遥测监听已停止");

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

        private async Task<byte[]> WaitNextPressureTelemetryFrameAsync(int startSeq, int timeoutMs, CancellationToken token)

        {

            var deadline = DateTime.UtcNow.AddMilliseconds(Math.Max(100, timeoutMs));

            while (!token.IsCancellationRequested && DateTime.UtcNow <= deadline)

            {

                if (Volatile.Read(ref _pressureTelemetrySeq) > startSeq)

                {

                    var frame = Interlocked.CompareExchange(ref _lastPressureTelemetryFrame, null, null);

                    if (frame != null)

                        return frame;

                }



                await Task.Delay(20, token);

            }



            return null;

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
