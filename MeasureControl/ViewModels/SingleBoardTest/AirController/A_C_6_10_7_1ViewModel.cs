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

        private const string FixedTxChannel = "429_CH5";

        private const string FixedRxChannel = "429_CH2";

        private static readonly byte[] EnterAtpCommand8 = { 0x30, 0x01, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00 };

        private static readonly byte[] ExitAtpCommand8 = { 0x30, 0x02, 0x02, 0x01, 0x00, 0x00, 0x00, 0x00 };



        private static readonly byte[] AbRaiaPosition8 = { 0x07, 0x03, 0x07, 0x01, 0x00, 0x00, 0x00, 0x00 };

        private static readonly byte[] RaiaPosTelemetryPrefix4 = { 0x07, 0x03, 0x07, 0x02 };



        private const string AoChannel = "AO9";

        private static readonly string[] Mtx532EnabledAoChannels = { "AO9" };

        private const int Mtx532ReadyTimeoutMs = 6000;

        private const int Mtx532ReadyPollMs = 200;

        private const double Mtx532SampleRateHz = 20000.0;

        private const double Mtx532VoltageReadbackToleranceV = 0.15;
        private const int Mtx532VoltageSettlePollCount = 10;
        private const int Mtx532VoltageSettlePollMs = 100;

        private readonly A_C_6_10_7_1Simulation _simulation = new A_C_6_10_7_1Simulation();

        private readonly SemaphoreSlim _arincOpLock = new SemaphoreSlim(1, 1);

        private readonly SemaphoreSlim _manualTestLock = new SemaphoreSlim(1, 1);

        private readonly SemaphoreSlim _autoTestLock = new SemaphoreSlim(1, 1);

        private readonly SemaphoreSlim _mtxOpLock = new SemaphoreSlim(1, 1);



        private CancellationTokenSource _autoTestCts;

        private CancellationTokenSource _telemetryListeningCts;

        private Task _telemetryListeningTask;

        private int _pressureTelemetrySeq;

        private byte[] _lastPressureTelemetryFrame;
        private byte[] _lastPressureTelemetryRawFrame;



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



        private string _lastTestTime;

        private string _lastTestResult;

        private string _previousTestTime;

        private string _previousTestResult;



        private bool _isManualTestRunning;

        private bool _isAutoTestRunning;

        private bool _isBusy;

        private bool _autoTestEnteredAtp;



        private double _arincRate = 100000.0;



        public A_C_6_10_7_1ViewModel()

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



        public bool CanEditStepControls => !IsAutoTestRunning && !IsBusy;



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



        public string PressureTelemetryRawRxDataText

        {

            get => _pressureTelemetryRawRxDataText;

            private set => SetProperty(ref _pressureTelemetryRawRxDataText, value);

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



            if (IsManualTestRunning)
            {
                MessageBox.Show("请先停止手动测试，再开始自动测试。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _ = RunAutoTestAsync();

        }

       private async Task StartManualTestAsync()
{
    await _manualTestLock.WaitAsync();
    try
    {
        if (IsManualTestRunning || IsBusy)
            return;

        IsManualTestRunning = true;

        PressureTelemetryValueText = "--";
        PressureTelemetryRxDataText = "--";
        EnterAtpRxDataText = "--";
        ExitAtpRxDataText = "--";
        IsInAtp = false;
        LastTestTime = "--";
        LastTestResult = "--";

        IsBusy = true;
        try
        {
            EnsureManualArincChannels();

            // 预先打开 MTX532，AO9 输出 0V 并启动输出
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

            _simulation.IsRealProduct = true;
            _simulation.ArincRate = ArincRate;

            AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试启动：打开ARINC429");
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

                    var token = CancellationToken.None;



                    AddLog($"[{DateTime.Now:HH:mm:ss}] 发送退出ATP：TX={ExitAtpTxChannel}, Data={FormatData(ExitAtpCommand8)}");

                    await _simulation.SendBenchCommandOnlyAsync(ExitAtpTxChannel, ExitAtpCommand8, msg => AddLog(msg), token);

                    IsInAtp = false;

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 退出ATP指令已发送");

                    await StopPressureTelemetryListeningAsync();

                }

                finally

                {

                    try
                    {
                        StartPressureTelemetryListeningIfNeeded();
                    }
                    catch
                    {
                    }

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

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 设置档位{gearIndex}：{AoChannel}={voltageV.ToString("0.###", CultureInfo.InvariantCulture)}V");



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

                    try
                    {
                        StartPressureTelemetryListeningIfNeeded();
                    }
                    catch
                    {
                    }

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
                    var gearIndex = CurrentGearIndex <= 0 ? 1 : CurrentGearIndex;

                    PressureTelemetryValueText = "--";
                    PressureTelemetryRxDataText = "--";
                    PressureTelemetryRawRxDataText = "--";

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 控制器RAIA_POS单次测试：当前档位={gearIndex}, TX={ControllerPressureTestTxChannel}, RX={PressureTelemetryRxChannel}, IsInAtp={IsInAtp}");

                    StartPressureTelemetryListeningIfNeeded();

                    var startSeq = Volatile.Read(ref _pressureTelemetrySeq);
                    Interlocked.Exchange(ref _lastPressureTelemetryFrame, null);
                    Interlocked.Exchange(ref _lastPressureTelemetryRawFrame, null);

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 发送前遥测状态：监听任务={(_telemetryListeningTask != null ? "已启动" : "未启动")}, startSeq={startSeq}");

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 发送AB_RAIA_POSITION：TX={ControllerPressureTestTxChannel}, Data={FormatData(AbRaiaPosition8)}");

                    await _simulation.SendBenchCommandOnlyAsync(ControllerPressureTestTxChannel, AbRaiaPosition8, msg => AddLog(msg), token);

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 测试指令已发送，等待RAIA_POS遥测：RX={PressureTelemetryRxChannel}, 编码模板=07 03 07 02 00 00 00 00, timeout=8000ms");

                    var tel = await WaitNextPressureTelemetryFrameAsync(startSeq, timeoutMs: 8000, token: token);

                    var currentSeq = Volatile.Read(ref _pressureTelemetrySeq);

                    if (tel == null)

                    {

                        SetLastTestResult("FAIL");

                        AddLog($"[{DateTime.Now:HH:mm:ss}] 控制器RAIA_POS单次测试遥测超时：startSeq={startSeq}, currentSeq={currentSeq}, RX={PressureTelemetryRxChannel}, seq未增长，说明监听未收到产品回传RAIA_POS帧。请检查产品是否在RX={PressureTelemetryRxChannel}回发label=0x90/0x50/0xD0/0x30且编码模板=07 03 07 02 00 00 00 00，并确认429接线/通道配置");

                        return;

                    }



                    PressureTelemetryRxDataText = "0x" + FormatData(tel);

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 收到RAIA_POS遥测帧：Data={FormatData(tel)}, seq={currentSeq}");

                    var rawData = Interlocked.CompareExchange(ref _lastPressureTelemetryRawFrame, null, null);

                    if (rawData != null)

                    {

                        PressureTelemetryRawRxDataText = "0x" + FormatData(rawData);

                        LogRaiaPosRawData(rawData);

                    }

                    if (!TryParseTelemetryRaiaPos(tel, out var pos))

                    {

                        SetLastTestResult("FAIL");

                        PressureTelemetryValueText = "--";

                        AddLog($"[{DateTime.Now:HH:mm:ss}] 控制器RAIA_POS单次测试遥测解析失败：Data={FormatData(tel)}");

                        return;

                    }



                    PressureTelemetryValueText = pos.ToString("0.####", CultureInfo.InvariantCulture);

                    var pass = IsRaiaPosQualified(gearIndex, pos);

                    SetLastTestResult(pass ? "PASS" : "FAIL");

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 控制器RAIA_POS单次测试结束：档位{gearIndex}, 位置={pos.ToString("0.####", CultureInfo.InvariantCulture)}, 判定={LastTestResult}");

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



        private void StartPressureTelemetryListeningIfNeeded()
        {
            if (_telemetryListeningTask != null)
                return;

            if (!IsManualTestRunning && !IsAutoTestRunning)
                return;

            if (!IsInAtp)
                return;

            if (string.IsNullOrWhiteSpace(PressureTelemetryRxChannel))
                return;

            _telemetryListeningCts?.Cancel();
            _telemetryListeningCts?.Dispose();
            _telemetryListeningCts = new CancellationTokenSource();
            var token = _telemetryListeningCts.Token;
            var rxChannel = PressureTelemetryRxChannel;

            _telemetryListeningTask = Task.Run(async () =>
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 启动RAIA_POS遥测持续监听：RX={rxChannel}");
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        var tel = await _simulation.WaitRaiaPosTelemetryAsync(
                            rxChannel,
                            timeoutMs: 300,
                            log: _ => { },
                            token: token);

                        if (tel != null && TryParseTelemetryRaiaPos(tel, out var pos))
                        {
                            var frameCopy = tel.ToArray();
                            Interlocked.Exchange(ref _lastPressureTelemetryFrame, frameCopy);
                            var seq = Interlocked.Increment(ref _pressureTelemetrySeq);
                            AddLog($"[{DateTime.Now:HH:mm:ss}] RAIA_POS遥测监听收到有效帧：seq={seq}, Pos={pos.ToString("0.####", CultureInfo.InvariantCulture)}, Data={FormatData(frameCopy)}");

                            var dispatcher = Application.Current?.Dispatcher;
                            if (dispatcher != null && !dispatcher.CheckAccess())
                            {
                                dispatcher.BeginInvoke(new Action(() =>
                                {
                                    PressureTelemetryRxDataText = "0x" + FormatData(frameCopy);
                                    PressureTelemetryValueText = pos.ToString("0.####", CultureInfo.InvariantCulture);
                                }));
                            }
                            else
                            {
                                PressureTelemetryRxDataText = "0x" + FormatData(frameCopy);
                                PressureTelemetryValueText = pos.ToString("0.####", CultureInfo.InvariantCulture);
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

                AddLog($"[{DateTime.Now:HH:mm:ss}] RAIA_POS遥测监听已停止");
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

                    await StopPressureTelemetryListeningAsync();



                    _simulation.IsRealProduct = true;

                    _simulation.ArincRate = ArincRate;



                    var mtxOk = await PreconnectMtx532ForTestAsync("自动测试", token);

                    if (!mtxOk)

                    {

                        IsMtx532RealHardware = false;

                        SetLastTestResult("FAIL");

                        AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试启动失败：MTX532连接失败");

                        return;

                    }



                    IsMtx532RealHardware = true;



                    AddLog($"[{DateTime.Now:HH:mm:ss}] ========== 自动测试开始 ==========");

                    await _simulation.StartAsync(TestTxChannel, TestRxChannel, msg => AddLog(msg));

                    await AutoEnterAtpAsync(token);
                    _autoTestEnteredAtp = true;

                    var failures = new System.Collections.Generic.List<string>();

                    await RunGearAutoAsync(1, 0.25, token, failures);

                    token.ThrowIfCancellationRequested();

                    await RunGearAutoAsync(2, 5.0, token, failures);

                    token.ThrowIfCancellationRequested();

                    await RunGearAutoAsync(3, 9.75, token, failures);

                    await AutoExitAtpAsync(token);
                    _autoTestEnteredAtp = false;

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
                    if (_autoTestEnteredAtp)
                    {
                        try { await AutoExitAtpAsync(CancellationToken.None); } catch { }
                        _autoTestEnteredAtp = false;
                    }

                    try { await ShutdownOpenedBoardsForTestEndAsync(); } catch { }

                    IsAutoTestRunning = false;

                    IsBusy = false;

                }

            }

            finally

            {

                _autoTestLock.Release();

            }

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

            await _simulation.SendBenchCommandOnlyAsync(TestTxChannel, ExitAtpCommand8, msg => AddLog(msg), token);

            IsInAtp = false;

            return true;

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

                AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试停止请求");

            }

            finally

            {

                _autoTestLock.Release();

            }

        }



        private async Task RunGearAutoAsync(int gearIndex, double voltageV, CancellationToken token, System.Collections.Generic.List<string> failures)
        {

            CurrentGearIndex = gearIndex;



            AddLog($"[{DateTime.Now:HH:mm:ss}] 档位{gearIndex}：设置{AoChannel}={voltageV.ToString("0.###", CultureInfo.InvariantCulture)}V");

            var okVoltage = await OutputVoltageAsync(voltageV, token);

            if (!okVoltage)

            {

                failures.Add($"档位{gearIndex}电压输出失败");

                return;

            }



            AddLog($"[{DateTime.Now:HH:mm:ss}] 档位{gearIndex}：{AoChannel}输出后等待稳定5s");

            await Task.Delay(TimeSpan.FromSeconds(5), token);



            try { await _simulation.ClearRxFifoAsync(TestRxChannel); } catch { }

            await Task.Delay(20, token);



            StartPressureTelemetryListeningIfNeeded();

            var startSeq = Volatile.Read(ref _pressureTelemetrySeq);
            Interlocked.Exchange(ref _lastPressureTelemetryFrame, null);
            Interlocked.Exchange(ref _lastPressureTelemetryRawFrame, null);

            AddLog($"[{DateTime.Now:HH:mm:ss}] 档位{gearIndex}：发送AB_RAIA_POSITION");

            await _simulation.SendBenchCommandOnlyAsync(TestTxChannel, AbRaiaPosition8, msg => AddLog(msg), token);



            AddLog($"[{DateTime.Now:HH:mm:ss}] 档位{gearIndex}：等待RAIA_POS遥测(07 03 07 02)[持续监听]，timeout=8000ms");

            var tel = await WaitNextPressureTelemetryFrameAsync(startSeq, timeoutMs: 8000, token: token);

            if (tel == null)

            {

                AddLog($"[{DateTime.Now:HH:mm:ss}] 档位{gearIndex}：第一次等待RAIA_POS遥测超时，尝试直接从RX={PressureTelemetryRxChannel}读取RAIA_POS遥测");

                try { await StopPressureTelemetryListeningAsync(); } catch { }
                try { await _simulation.ClearRxFifoAsync(TestRxChannel); } catch { }
                await Task.Delay(20, token);

                AddLog($"[{DateTime.Now:HH:mm:ss}] 档位{gearIndex}：重发AB_RAIA_POSITION用于直接遥测读取");

                await _simulation.SendBenchCommandOnlyAsync(TestTxChannel, AbRaiaPosition8, msg => AddLog(msg), token);

                tel = await DirectWaitPressureTelemetryFrameAsync(gearIndex, token);

                StartPressureTelemetryListeningIfNeeded();

                if (tel == null)

                {

                    failures.Add($"档位{gearIndex}RAIA_POS遥测超时：RX={PressureTelemetryRxChannel}未收到label=0x90/0x50/0xD0/0x30且编码模板=07 03 07 02 00 00 00 00的回传帧");

                    return;

                }

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

        private async Task<byte[]> WaitNextPressureTelemetryFrameAsync(int startSeq, int timeoutMs, CancellationToken token)
        {
            var startUtc = DateTime.UtcNow;
            var deadline = startUtc.AddMilliseconds(Math.Max(100, timeoutMs));
            while (!token.IsCancellationRequested && DateTime.UtcNow <= deadline)
            {
                var frame = Interlocked.CompareExchange(ref _lastPressureTelemetryFrame, null, null);
                if (frame != null)
                {
                    var elapsedMs = (int)(DateTime.UtcNow - startUtc).TotalMilliseconds;
                    var currentSeq = Volatile.Read(ref _pressureTelemetrySeq);
                    AddLog($"[{DateTime.Now:HH:mm:ss}] WaitNextPressureTelemetryFrameAsync 成功：耗时={elapsedMs}ms, startSeq={startSeq}, currentSeq={currentSeq}, 当前档位={CurrentGearIndex}");
                    return frame;
                }

                await Task.Delay(20, token);
            }

            var totalElapsed = (int)(DateTime.UtcNow - startUtc).TotalMilliseconds;
            var finalSeq = Volatile.Read(ref _pressureTelemetrySeq);
            AddLog($"[{DateTime.Now:HH:mm:ss}] WaitNextPressureTelemetryFrameAsync 超时：耗时={totalElapsed}ms, startSeq={startSeq}, currentSeq={finalSeq}, 当前档位={CurrentGearIndex}");
            return null;
        }

        private async Task<byte[]> DirectWaitPressureTelemetryFrameAsync(int gearIndex, CancellationToken token)
        {
            var t0 = DateTime.UtcNow;
            var rxChannel = PressureTelemetryRxChannel;
            AddLog($"[{DateTime.Now:HH:mm:ss}] 档位{gearIndex}：直接等待RAIA_POS遥测：RX={rxChannel}，timeout=2000ms");

            var tel = await _simulation.WaitRaiaPosTelemetryAsync(
                rxChannel,
                timeoutMs: 2000,
                log: msg => AddLog(msg),
                token: token);

            if (tel == null)
            {
                var elapsed = (int)Math.Max(0, (DateTime.UtcNow - t0).TotalMilliseconds);
                AddLog($"[{DateTime.Now:HH:mm:ss}] 档位{gearIndex}：直接等待RAIA_POS遥测失败：{elapsed}ms内未收到07 03 07 02帧");
                return null;
            }

            var frameCopy = tel.ToArray();
            Interlocked.Exchange(ref _lastPressureTelemetryFrame, frameCopy);
            var seq = Interlocked.Increment(ref _pressureTelemetrySeq);
            AddLog($"[{DateTime.Now:HH:mm:ss}] 档位{gearIndex}：直接等待RAIA_POS遥测成功：seq={seq}, Data={FormatData(frameCopy)}");
            return frameCopy;
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



                AddLog($"[{DateTime.Now:HH:mm:ss}] MTX532档位电压写入开始：{AoChannel}={voltageV.ToString("0.####", CultureInfo.InvariantCulture)}V（1基，第9个物理通道）");

                await SetAo9Async(voltageV, token);

                AddLog($"[{DateTime.Now:HH:mm:ss}] MTX532档位电压写入完成：{AoChannel}={voltageV.ToString("0.####", CultureInfo.InvariantCulture)}V，IsOutputRunning={_mtxApi.IsOutputRunning}");

                if (!_mtxApi.IsOutputRunning)

                {

                    AddLog($"[{DateTime.Now:HH:mm:ss}] MTX532输出未运行，准备等待Ready后启动输出");

                    await WaitForMtx532ReadyAsync(token);

                    await _mtxApi.StartOutputAsync(token);

                    AddLog($"[{DateTime.Now:HH:mm:ss}] MTX532 StartOutputAsync完成，IsOutputRunning={_mtxApi.IsOutputRunning}");

                }



                double? lastReadBack = null;
                for (int i = 0; i < Mtx532VoltageSettlePollCount; i++)
                {
                    token.ThrowIfCancellationRequested();

                    var readBackVoltage = await ReadAo9VoltageAsync(token);
                    if (readBackVoltage.HasValue)
                    {
                        lastReadBack = readBackVoltage.Value;
                        VoltageSetValueText = readBackVoltage.Value.ToString("0.####", CultureInfo.InvariantCulture);

                        var diff = Math.Abs(readBackVoltage.Value - voltageV);
                        if (diff <= Mtx532VoltageReadbackToleranceV)
                        {
                            AddLog($"[{DateTime.Now:HH:mm:ss}] {AoChannel}设定={voltageV.ToString("0.####", CultureInfo.InvariantCulture)}V，板卡读回={VoltageSetValueText}V，已在容差±{Mtx532VoltageReadbackToleranceV.ToString("0.###", CultureInfo.InvariantCulture)}V内，视为输出稳定");
                            return true;
                        }

                        AddLog($"[{DateTime.Now:HH:mm:ss}] {AoChannel}设定={voltageV.ToString("0.####", CultureInfo.InvariantCulture)}V，板卡读回={VoltageSetValueText}V，仍未进入容差±{Mtx532VoltageReadbackToleranceV.ToString("0.###", CultureInfo.InvariantCulture)}V内，pollIndex={i + 1}/{Mtx532VoltageSettlePollCount}");
                    }
                    else
                    {
                        AddLog($"[{DateTime.Now:HH:mm:ss}] {AoChannel}板卡读回失败，pollIndex={i + 1}/{Mtx532VoltageSettlePollCount}");
                    }

                    if (i < Mtx532VoltageSettlePollCount - 1)
                    {
                        await Task.Delay(Mtx532VoltageSettlePollMs, token);
                    }
                }

                var lastText = lastReadBack.HasValue
                    ? lastReadBack.Value.ToString("0.####", CultureInfo.InvariantCulture)
                    : "null";
                AddLog($"[{DateTime.Now:HH:mm:ss}] {AoChannel}电压输出未能在{Mtx532VoltageSettlePollCount * Mtx532VoltageSettlePollMs}ms内稳定到设定值：目标={voltageV.ToString("0.####", CultureInfo.InvariantCulture)}V，最后一次读回={lastText}V，容差±{Mtx532VoltageReadbackToleranceV.ToString("0.###", CultureInfo.InvariantCulture)}V");

                return false;

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



        private async Task<double?> ReadAo9VoltageAsync(CancellationToken token)

        {

            if (_mtxApi == null || !_mtxApi.IsConnected)

                return null;



            var samples = new System.Collections.Generic.List<double>(3);

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



        private async Task SetAo9Async(double voltageV, CancellationToken token)

        {

            if (_mtxApi == null || !_mtxApi.IsConnected)

                throw new InvalidOperationException("MTX532未连接");



            await _mtxApi.WriteOnceDcAsync(new System.Collections.Generic.Dictionary<string, double>

            {

                [AoChannel] = voltageV

            }, token).ConfigureAwait(false);

        }



        private async Task<bool> EnsureMtx532ConnectedAsync(CancellationToken token = default)

        {

            if (_mtxApi != null && _mtxApi.IsConnected)

            {

                AddLog($"[{DateTime.Now:HH:mm:ss}] MTX532已连接，跳过重新连接，IsOutputRunning={_mtxApi.IsOutputRunning}");

                return true;

            }



            AddLog($"[{DateTime.Now:HH:mm:ss}] MTX532连接流程开始：硬件AO为1基，目标业务通道={AoChannel}（第9个物理通道）");

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



            AddLog($"[{DateTime.Now:HH:mm:ss}] MTX532写初始0V：{AoChannel}=0V（1基，第9个物理通道）");

            await SetAo9Async(0.0, token).ConfigureAwait(false);

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



            var raw =
                (frameData[4] << 24) |
                (frameData[5] << 16) |
                (frameData[6] << 8) |
                frameData[7];

            position = raw * 0.001;

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



        private void LogRaiaPosRawData(byte[] rawData)

        {

            if (rawData == null || rawData.Length < 8)

                return;



            AddLog($"[{DateTime.Now:HH:mm:ss}] RAIA_POS原始遥测帧：Data={FormatData(rawData)}");

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



        private async Task ShutdownOpenedBoardsForTestEndAsync()

        {

            try

            {

                try

                {

                    await StopPressureTelemetryListeningAsync();

                }

                catch

                {

                }



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

                    IsMtx532RealHardware = false;

                    VoltageSetValueText = "--";

                }

                catch

                {

                }

            }

            catch

            {

            }

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
