using Prism.Commands;
using Prism.Ioc;
using Prism.Mvvm;
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
using MeasureControl.Simulations.A_C_6_12_1_1;

namespace MeasureControl.ViewModels.SingleBoardTest.AirController
{
    public sealed class A_C_6_12_1_1ViewModel : BindableBase, IDisposable
    {
        private static readonly byte[] EnterAtpCommand8 = { 0x30, 0x01, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] ExitAtpCommand8 = { 0x30, 0x02, 0x02, 0x01, 0x00, 0x00, 0x00, 0x00 };

        private static readonly byte[] AbOfvtrvFinger8 = { 0x07, 0x05, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] OfvtrvFingerTelemetryPrefix4 = { 0x07, 0x05, 0x01, 0x03 };

        private const string AoChannel = "AO13";
        private static readonly string[] Mtx532EnabledAoChannels = { "AO13" };
        private const int Mtx532ReadyTimeoutMs = 6000;
        private const int Mtx532ReadyPollMs = 200;
        private const double Mtx532SampleRateHz = 20000.0;

        private const double Mtx532VoltageReadbackToleranceV = 0.15;
        private const int Mtx532VoltageSettlePollCount = 10;
        private const int Mtx532VoltageSettlePollMs = 100;

        private const string FixedTxChannel = "429_CH5";
        private const string FixedRxChannel = "429_CH2";

        private readonly A_C_6_12_1_1Simulation _simulation = new A_C_6_12_1_1Simulation();
        private readonly SemaphoreSlim _arincOpLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _manualTestLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _autoTestLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _mtxOpLock = new SemaphoreSlim(1, 1);

        private CancellationTokenSource _autoTestCts;
        private IMtx532Api _mtxApi;

        private string _testTxChannel;
        private string _testRxChannel;

        private string _enterAtpTxChannel;
        private string _exitAtpTxChannel;

        private string _fingerTestTxChannel;

        private string _fingerTelemetryRxChannel;
        private string _fingerTelemetryValueText;
        private string _fingerTelemetryRxDataText;

        private bool _isInAtp;

        private string _voltageGear;
        private int _currentGearIndex;
        private string _voltageSetValueText;

        private bool _isMtx532RealHardware;

        private bool _isBusy;
        private bool _isManualTestRunning;
        private bool _isAutoTestRunning;
        private bool _autoTestEnteredAtp;

        private string _lastTestTime;
        private string _lastTestResult;
        private string _previousTestTime;
        private string _previousTestResult;

        private double _arincRate = 100000.0;

        private CancellationTokenSource _telemetryListeningCts;
        private Task _telemetryListeningTask;
        private int _fingerTelemetrySeq;
        private byte[] _lastFingerTelemetryFrame;

        public A_C_6_12_1_1ViewModel()
        {
            _testTxChannel = FixedTxChannel;
            _testRxChannel = FixedRxChannel;

            _enterAtpTxChannel = _testTxChannel;
            _exitAtpTxChannel = _testTxChannel;
            _fingerTestTxChannel = _testTxChannel;
            _fingerTelemetryRxChannel = _testRxChannel;

            FingerTelemetryValueText = "--";
            FingerTelemetryRxDataText = "--";
            VoltageSetValueText = "--";

            VoltageGear = null;
            CurrentGearIndex = 0;

            LastTestTime = "--";
            LastTestResult = "--";
            PreviousTestTime = "--";
            PreviousTestResult = "--";

            ManualTestCommand = new DelegateCommand(OnManualTest);
            AutoTestCommand = new DelegateCommand(OnAutoTest);
            ClearLogCommand = new DelegateCommand(() => Logs.Clear());

            SendEnterAtpCommand = new DelegateCommand(async () => await OnSendEnterAtpAsync());
            SendExitAtpCommand = new DelegateCommand(async () => await OnSendExitAtpAsync());
            SendSetControllerVoltageCommand = new DelegateCommand(async () => await OnSetSelectedGearVoltageAsync());
            SendFingerTestCommand = new DelegateCommand(async () => await OnSendFingerTestCommandAsync());

            _simulation.GetCurrentGearIndex = () => CurrentGearIndex <= 0 ? 1 : CurrentGearIndex;
        }

        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();

        public DelegateCommand ManualTestCommand { get; }
        public DelegateCommand AutoTestCommand { get; }
        public DelegateCommand ClearLogCommand { get; }

        public DelegateCommand SendEnterAtpCommand { get; }
        public DelegateCommand SendExitAtpCommand { get; }
        public DelegateCommand SendSetControllerVoltageCommand { get; }
        public DelegateCommand SendFingerTestCommand { get; }

        public bool CanEditStepControls => !IsAutoTestRunning && !IsBusy;

        public double ArincRate
        {
            get => _arincRate;
            set => SetProperty(ref _arincRate, value);
        }

        public string TestTxChannel
        {
            get => FixedTxChannel;
        }

        public string TestRxChannel
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

        public bool IsInAtp
        {
            get => _isInAtp;
            private set => SetProperty(ref _isInAtp, value);
        }

        public string VoltageGear
        {
            get => _voltageGear;
            set
            {
                if (!SetProperty(ref _voltageGear, value))
                    return;

                CurrentGearIndex = value switch
                {
                    "1挡" => 1,
                    "2挡" => 2,
                    "3挡" => 3,
                    _ => 0
                };
            }
        }

        public int CurrentGearIndex
        {
            get => _currentGearIndex;
            private set => SetProperty(ref _currentGearIndex, value);
        }

        public string VoltageSetValueText
        {
            get => _voltageSetValueText;
            private set => SetProperty(ref _voltageSetValueText, value);
        }

        public string FingerTestTxChannel
        {
            get => FixedTxChannel;
        }

        public string FingerTelemetryRxChannel
        {
            get => FixedRxChannel;
        }

        public string FingerTelemetryValueText
        {
            get => _fingerTelemetryValueText;
            private set => SetProperty(ref _fingerTelemetryValueText, value);
        }

        public string FingerTelemetryRxDataText
        {
            get => _fingerTelemetryRxDataText;
            private set => SetProperty(ref _fingerTelemetryRxDataText, value);
        }

        public bool IsMtx532RealHardware
        {
            get => _isMtx532RealHardware;
            private set => SetProperty(ref _isMtx532RealHardware, value);
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

        private void OnManualTest()
        {
            if (IsManualTestRunning)
            {
                _ = StopManualTestAsync();
                return;
            }

            _ = RunManualTestAsync();
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

        private async Task RunManualTestAsync()
        {
            await _manualTestLock.WaitAsync();
            try
            {
                if (IsBusy)
                    return;

                EnsureManualArincChannels();

                IsBusy = true;
                try
                {
                    IsManualTestRunning = true;

                    FingerTelemetryValueText = "--";
                    FingerTelemetryRxDataText = "--";
                    IsInAtp = false;

                    Interlocked.Exchange(ref _lastFingerTelemetryFrame, null);
                    Volatile.Write(ref _fingerTelemetrySeq, 0);

                    _simulation.IsRealProduct = true;
                    _simulation.ArincRate = ArincRate;

                    AddLog($"[{DateTime.Now:HH:mm:ss}] ========== 手动测试开始 ==========");

                    var mtxOk = await PreconnectMtx532ForTestAsync("手动测试", CancellationToken.None);
                    if (!mtxOk)
                    {
                        IsManualTestRunning = false;
                        AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试启动失败：MTX532连接失败");
                        return;
                    }

                    await _simulation.StartAsync(TestTxChannel, TestRxChannel, msg => AddLog(msg));
                }
                finally
                {
                    IsBusy = false;
                }
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试启动异常：{ex.Message}");
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
                IsBusy = true;
                try
                {
                    await ShutdownOpenedBoardsForTestEndAsync();

                    IsManualTestRunning = false;
                    IsInAtp = false;

                    AddLog($"[{DateTime.Now:HH:mm:ss}] ========== 手动测试已停止 ==========");
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

                    StartFingerTelemetryListeningIfNeeded();
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
                    await StopFingerTelemetryListeningAsync();

                    var token = CancellationToken.None;
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 发送退出ATP：TX={ExitAtpTxChannel}");
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

        private async Task OnSetSelectedGearVoltageAsync()
        {
            if (IsBusy)
                return;

            if (CurrentGearIndex is < 1 or > 3)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 请先选择接入电压挡位");
                return;
            }

            double voltageV = CurrentGearIndex switch
            {
                1 => 0.5,
                2 => 5.0,
                3 => 9.7,
                _ => 0.5
            };

            IsBusy = true;
            try
            {
                var token = CancellationToken.None;
                AddLog($"[{DateTime.Now:HH:mm:ss}] 设置档位{CurrentGearIndex}：{AoChannel}={voltageV.ToString("0.###", CultureInfo.InvariantCulture)}V");

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

        private async Task OnSendFingerTestCommandAsync()
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

            var sendOk = false;

            await _arincOpLock.WaitAsync();
            try
            {
                IsBusy = true;
                try
                {
                    var token = CancellationToken.None;

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 发送AB_OFVTRV_FINGER：TX={FingerTestTxChannel}, Data={FormatData(AbOfvtrvFinger8)}");
                    await _simulation.SendBenchCommandOnlyAsync(FingerTestTxChannel, AbOfvtrvFinger8, msg => AddLog(msg), token);
                    sendOk = true;
                }
                finally
                {
                    IsBusy = false;
                }
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 发送选气楔测试命令异常：{ex.Message}");
            }
            finally
            {
                _arincOpLock.Release();
            }

            if (sendOk)
            {
                await OnReadFingerTelemetryAsync();
            }
        }

        private async Task OnReadFingerTelemetryAsync()
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
                    FingerTelemetryValueText = "--";
                    FingerTelemetryRxDataText = "--";

                    var token = CancellationToken.None;

                    StartFingerTelemetryListeningIfNeeded();

                    var startSeq = Volatile.Read(ref _fingerTelemetrySeq);
                    Interlocked.Exchange(ref _lastFingerTelemetryFrame, null);

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 手动读取：等待选气楔遥测(07 05 01 02)[持续监听]，timeout=3000ms");
                    var tel = await WaitNextFingerTelemetryFrameAsync(startSeq, timeoutMs: 3000, token: token);
                    if (tel == null)
                    {
                        AddLog($"[{DateTime.Now:HH:mm:ss}] 手动读取：第一次等待选气楔遥测超时，尝试直接从RX={FingerTelemetryRxChannel}读取");

                        try { await StopFingerTelemetryListeningAsync(); } catch { }

                        tel = await DirectWaitFingerTelemetryFrameAsync(CurrentGearIndex, token);

                        StartFingerTelemetryListeningIfNeeded();

                        if (tel == null)
                        {
                            AddLog($"[{DateTime.Now:HH:mm:ss}] 手动读取：选气楔遥测超时");
                            SetLastTestResult("FAIL");
                            return;
                        }
                    }

                    FingerTelemetryRxDataText = "0x" + FormatData(tel);
                    if (!TryParseTelemetryFingerStatus(tel, out var statusText, out var code))
                    {
                        AddLog($"[{DateTime.Now:HH:mm:ss}] 选气楔遥测解析失败");
                        SetLastTestResult("FAIL");
                        FingerTelemetryValueText = "解析失败";
                        return;
                    }

                    FingerTelemetryValueText = statusText;
                    SetLastTestResult(IsFingerQualified(CurrentGearIndex, code) ? "PASS" : "FAIL");
                }
                finally
                {
                    IsBusy = false;
                }
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 选气楔回采异常：{ex.Message}");
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
                    FingerTelemetryValueText = "--";
                    FingerTelemetryRxDataText = "--";
                    LastTestTime = "--";
                    LastTestResult = "--";

                    _autoTestCts?.Cancel();
                    _autoTestCts?.Dispose();
                    _autoTestCts = new CancellationTokenSource();
                    var token = _autoTestCts.Token;

                    Interlocked.Exchange(ref _lastFingerTelemetryFrame, null);
                    Volatile.Write(ref _fingerTelemetrySeq, 0);

                    _simulation.IsRealProduct = true;
                    _simulation.ArincRate = ArincRate;

                    AddLog($"[{DateTime.Now:HH:mm:ss}] ========== 自动测试开始 ==========");

                    var mtxOk = await PreconnectMtx532ForTestAsync("自动测试", token);
                    if (!mtxOk)
                    {
                        IsMtx532RealHardware = false;
                        SetLastTestResult("FAIL");
                        AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试启动失败：MTX532连接失败");
                        return;
                    }

                    IsMtx532RealHardware = true;

                    await _simulation.StartAsync(TestTxChannel, TestRxChannel, msg => AddLog(msg));

                    await AutoEnterAtpAsync(token);
                    _autoTestEnteredAtp = true;

                    var failures = new System.Collections.Generic.List<string>();

                    await RunGearAutoAsync(1, 0.5, token, failures);
                    token.ThrowIfCancellationRequested();
                    await RunGearAutoAsync(2, 5.0, token, failures);
                    token.ThrowIfCancellationRequested();
                    await RunGearAutoAsync(3, 9.7, token, failures);

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
            StartFingerTelemetryListeningIfNeeded();
            return true;
        }

        private async Task<bool> AutoExitAtpAsync(CancellationToken token)
        {
            AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试：发送退出ATP");
            try { await StopFingerTelemetryListeningAsync(); } catch { }
            await _simulation.SendBenchCommandOnlyAsync(TestTxChannel, ExitAtpCommand8, msg => AddLog(msg), token);
            IsInAtp = false;
            return true;
        }

        private async Task StopAutoTestAsync()
        {
            await _autoTestLock.WaitAsync();
            try
            {
                try { _autoTestCts?.Cancel(); } catch { }
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

            AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试：档位{gearIndex} {AoChannel}输出后等待稳定1s");
            await Task.Delay(TimeSpan.FromSeconds(1), token);

            try { await _simulation.ClearRxFifoAsync(TestRxChannel); } catch { }
            await Task.Delay(20, token);

            StartFingerTelemetryListeningIfNeeded();
            var startSeq = Volatile.Read(ref _fingerTelemetrySeq);
            Interlocked.Exchange(ref _lastFingerTelemetryFrame, null);

            AddLog($"[{DateTime.Now:HH:mm:ss}] 档位{gearIndex}：发送AB_OFVTRV_FINGER");
            await _simulation.SendBenchCommandOnlyAsync(TestTxChannel, AbOfvtrvFinger8, msg => AddLog(msg), token);

            AddLog($"[{DateTime.Now:HH:mm:ss}] 档位{gearIndex}：等待选气楔遥测(07 05 01 02)[持续监听]，timeout=7000ms");
            var tel = await WaitNextFingerTelemetryFrameAsync(startSeq, timeoutMs: 7000, token: token);
            if (tel == null)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 档位{gearIndex}：第一次等待选气楔遥测超时，尝试直接从RX={FingerTelemetryRxChannel}读取");

                try { await StopFingerTelemetryListeningAsync(); } catch { }
                try { await _simulation.ClearRxFifoAsync(TestRxChannel); } catch { }
                await Task.Delay(20, token);

                AddLog($"[{DateTime.Now:HH:mm:ss}] 档位{gearIndex}：重发AB_OFVTRV_FINGER用于直接遥测读取");
                await _simulation.SendBenchCommandOnlyAsync(TestTxChannel, AbOfvtrvFinger8, msg => AddLog(msg), token);

                tel = await DirectWaitFingerTelemetryFrameAsync(gearIndex, token);
                StartFingerTelemetryListeningIfNeeded();

                if (tel == null)
                {
                    failures.Add($"档位{gearIndex}选气楔遥测超时：RX={FingerTelemetryRxChannel}未收到label=0x09/0x0A/0x0B/0x0C且编码模板=07 05 01 02 00 00 00 00的回传帧");
                    return;
                }
            }

            FingerTelemetryRxDataText = "0x" + FormatData(tel);
            if (!TryParseTelemetryFingerStatus(tel, out var statusText2, out var code2))
            {
                failures.Add($"档位{gearIndex}选气楔遥测解析失败");
                FingerTelemetryValueText = "解析失败";
                return;
            }

            FingerTelemetryValueText = statusText2;
            if (!IsFingerQualified(gearIndex, code2))
            {
                failures.Add($"档位{gearIndex}状态不通过：{statusText2}");
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

                AddLog($"[{DateTime.Now:HH:mm:ss}] MTX532档位电压写入开始：{AoChannel}={voltageV.ToString("0.####", CultureInfo.InvariantCulture)}V（1基）");
                await SetAo13Async(voltageV, token);
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

                    var readBackVoltage = await ReadAo13VoltageAsync(token);
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

        private async Task<double?> ReadAo13VoltageAsync(CancellationToken token)
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

        private async Task SetAo13Async(double voltageV, CancellationToken token)
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

            AddLog($"[{DateTime.Now:HH:mm:ss}] MTX532连接流程开始：硬件AO为1基，目标业务通道={AoChannel}");
            var device = FindMtx532Device();
            if (device == null)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] MTX532连接失败：未找到MTX532(模拟量输出)板卡");
                return false;
            }

            var slot = (device as PxiDeviceBase)?.SlotIndex;
            var options = new Mtx532Options
            {
                SampleRateHz = Mtx532SampleRateHz,
                UseOneBasedAoChannelNumbering = true
            };

            _mtxApi = new Mtx532Api(device, options, slotNumber: slot.HasValue && slot.Value > 0 ? slot.Value : 7);
            AddLog($"[{DateTime.Now:HH:mm:ss}] MTX532开始ConnectAsync：enabledAoChannels={string.Join(",", Mtx532EnabledAoChannels)}（1基），目标输出={AoChannel}");
            await _mtxApi.ConnectAsync(token, Mtx532EnabledAoChannels).ConfigureAwait(false);

            AddLog($"[{DateTime.Now:HH:mm:ss}] MTX532写初始0V：{AoChannel}=0V（1基）");
            await SetAo13Async(0.0, token).ConfigureAwait(false);
            await Task.Delay(300, token).ConfigureAwait(false);
            await WaitForMtx532ReadyAsync(token).ConfigureAwait(false);
            await _mtxApi.StartOutputAsync(token).ConfigureAwait(false);

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
                AddLog($"[{DateTime.Now:HH:mm:ss}] {testName}：MTX532预打开结果={ok}，IsConnected={_mtxApi?.IsConnected == true}");
                return ok;
            }
            finally
            {
                _mtxOpLock.Release();
            }
        }

        private async Task ShutdownOpenedBoardsForTestEndAsync()
        {
            try { await StopFingerTelemetryListeningAsync(); } catch { }
            try { await _simulation.StopAsync(msg => AddLog(msg)); } catch { }

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

        private static bool TryParseTelemetryFingerStatus(byte[] frameData, out string statusText, out uint code)
        {
            statusText = null;
            code = 0;

            if (frameData == null || frameData.Length < 8)
                return false;

            if (!IsPrefix(frameData, OfvtrvFingerTelemetryPrefix4))
                return false;

            code = (uint)((frameData[4] << 24) | (frameData[5] << 16) | (frameData[6] << 8) | frameData[7]);
            
            if (code == 0x00005555)
            {
                statusText = "工作状态";
            }
            else if (code == 0x0000AAAA)
            {
                statusText = "非工作状态";
            }
            else
            {
                statusText = "故障状态";
            }

            return true;
        }

        private static bool IsFingerQualified(int gearIndex, uint statusCode)
        {
            return gearIndex switch
            {
                1 => statusCode == 0x00005555,
                2 => statusCode != 0x00005555 && statusCode != 0x0000AAAA,
                3 => statusCode == 0x0000AAAA,
                _ => false
            };
        }

        private void EnsureManualArincChannels()
        {
        }

        private void StartFingerTelemetryListeningIfNeeded()
        {
            if (_telemetryListeningTask != null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(FingerTelemetryRxChannel))
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 选气楔遥测监听未启动：RX通道为空");
                return;
            }

            if (!IsInAtp)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 选气楔遥测监听未启动：当前未进入ATP模式");
                return;
            }

            if (!IsManualTestRunning && !IsAutoTestRunning)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 选气楔遥测监听未启动：手动/自动测试均未运行");
                return;
            }

            _telemetryListeningCts?.Cancel();
            _telemetryListeningCts?.Dispose();
            _telemetryListeningCts = new CancellationTokenSource();
            var token = _telemetryListeningCts.Token;
            var rxChannel = FingerTelemetryRxChannel;

            AddLog($"[{DateTime.Now:HH:mm:ss}] 准备启动选气楔遥测持续监听：RX={rxChannel}, IsManual={IsManualTestRunning}, IsAuto={IsAutoTestRunning}, IsInAtp={IsInAtp}");

            _telemetryListeningTask = Task.Run(async () =>
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 启动选气楔遥测持续监听：RX={rxChannel}");
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        var tel = await _simulation.WaitFingerTelemetryAsync(
                            rxChannel,
                            timeoutMs: 300,
                            log: msg => { },
                            token: token);

                        if (tel != null)
                        {
                            var frameCopy = tel.ToArray();
                            Interlocked.Exchange(ref _lastFingerTelemetryFrame, frameCopy);
                            var seq = Interlocked.Increment(ref _fingerTelemetrySeq);

                            bool isParsed = TryParseTelemetryFingerStatus(frameCopy, out var statusText, out var code);
                            string displayStatus = isParsed ? statusText : "解析失败";

                            AddLog($"[{DateTime.Now:HH:mm:ss}] 选气楔遥测监听收到帧：seq={seq}, Status={displayStatus}, Data={FormatData(frameCopy)}");

                            var dispatcher = Application.Current?.Dispatcher;
                            if (dispatcher != null && !dispatcher.CheckAccess())
                            {
                                dispatcher.BeginInvoke(new Action(() =>
                                {
                                    FingerTelemetryRxDataText = "0x" + FormatData(frameCopy);
                                    FingerTelemetryValueText = displayStatus;
                                }));
                            }
                            else
                            {
                                FingerTelemetryRxDataText = "0x" + FormatData(frameCopy);
                                FingerTelemetryValueText = displayStatus;
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
                    catch (Exception ex)
                    {
                        AddLog($"[{DateTime.Now:HH:mm:ss}] 选气楔遥测监听异常：{ex.Message}");
                        try { await Task.Delay(100, token); } catch { break; }
                    }
                }

                AddLog($"[{DateTime.Now:HH:mm:ss}] 选气楔遥测监听已停止");
            }, token);
        }

        private async Task StopFingerTelemetryListeningAsync()
        {
            try
            {
                _telemetryListeningCts?.Cancel();
            }
            catch
            {
            }

            try
            {
                if (_telemetryListeningTask != null)
                    await _telemetryListeningTask.ConfigureAwait(false);
            }
            catch
            {
            }
            finally
            {
                _telemetryListeningTask = null;
                _telemetryListeningCts?.Dispose();
                _telemetryListeningCts = null;
            }
        }

        private async Task<byte[]> WaitNextFingerTelemetryFrameAsync(int startSeq, int timeoutMs, CancellationToken token)
        {
            var startUtc = DateTime.UtcNow;
            var deadline = startUtc.AddMilliseconds(Math.Max(100, timeoutMs));

            while (!token.IsCancellationRequested && DateTime.UtcNow <= deadline)
            {
                var frame = Interlocked.CompareExchange(ref _lastFingerTelemetryFrame, null, null);
                if (frame != null)
                {
                    var elapsedMs = (int)(DateTime.UtcNow - startUtc).TotalMilliseconds;
                    var currentSeq = Volatile.Read(ref _fingerTelemetrySeq);
                    AddLog($"[{DateTime.Now:HH:mm:ss}] WaitNextFingerTelemetryFrameAsync 成功：耗时={elapsedMs}ms, startSeq={startSeq}, currentSeq={currentSeq}, 当前档位={CurrentGearIndex}");
                    return frame;
                }

                await Task.Delay(20, token);
            }

            var totalElapsedMs = (int)(DateTime.UtcNow - startUtc).TotalMilliseconds;
            var finalSeq = Volatile.Read(ref _fingerTelemetrySeq);
            AddLog($"[{DateTime.Now:HH:mm:ss}] WaitNextFingerTelemetryFrameAsync 超时：耗时={totalElapsedMs}ms, startSeq={startSeq}, currentSeq={finalSeq}, 当前档位={CurrentGearIndex}");
            return null;
        }

        private async Task<byte[]> DirectWaitFingerTelemetryFrameAsync(int gearIndex, CancellationToken token)
        {
            var t0 = DateTime.UtcNow;
            var rxChannel = FingerTelemetryRxChannel;
            AddLog($"[{DateTime.Now:HH:mm:ss}] 档位{gearIndex}：直接等待选气楔遥测：RX={rxChannel}，timeout=2000ms");

            var tel = await _simulation.WaitFingerTelemetryAsync(
                rxChannel,
                timeoutMs: 2000,
                log: msg => AddLog(msg),
                token: token);

            if (tel == null)
            {
                var elapsed = (int)Math.Max(0, (DateTime.UtcNow - t0).TotalMilliseconds);
                AddLog($"[{DateTime.Now:HH:mm:ss}] 档位{gearIndex}：直接等待选气楔遥测失败：{elapsed}ms内未收到07 05 01 02帧");
                return null;
            }

            var frameCopy = tel.ToArray();
            Interlocked.Exchange(ref _lastFingerTelemetryFrame, frameCopy);
            var seq = Interlocked.Increment(ref _fingerTelemetrySeq);
            AddLog($"[{DateTime.Now:HH:mm:ss}] 档位{gearIndex}：直接等待选气楔遥测成功：seq={seq}, Data={FormatData(frameCopy)}");
            return frameCopy;
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

            try { Logs.Add(message); } catch { }
            try { Debug.WriteLine(message); } catch { }
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

            try { _simulation.StopAsync(_ => { }).GetAwaiter().GetResult(); } catch { }
            try { DisconnectMtx532Async().GetAwaiter().GetResult(); } catch { }
        }
    }
}
