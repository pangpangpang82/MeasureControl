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
using MeasureControl.Simulations.A_C_6_11_1_1;

namespace MeasureControl.ViewModels.SingleBoardTest.AirController
{
    public sealed class A_C_6_11_1_1ViewModel : BindableBase, IDisposable
    {
        // ATP 进入/退出编码与 6.9.1 保持一致（无 OK 回包）
        private static readonly byte[] EnterAtpCommand8 = { 0x30, 0x01, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] ExitAtpCommand8 = { 0x30, 0x02, 0x02, 0x01, 0x00, 0x00, 0x00, 0x00 };

        // 6.11.1 OFV/TRV 角度测试完整编码与遥测/原始编码模板
        private static readonly byte[] AbOfvtrvAngle8 = { 0x07, 0x04, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] OfvtrvAngleTelemetryTemplate8 = { 0x07, 0x04, 0x01, 0x02, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] OfvtrvAngleTelemetryRawTemplate8 = { 0x07, 0x04, 0x01, 0x03, 0x00, 0x00, 0x00, 0x00 };

        private static readonly byte[] OfvtrvAngleTelemetryPrefix4 = { 0x07, 0x04, 0x01, 0x02 };
        private static readonly byte[] OfvtrvAngleTelemetryRawPrefix4 = { 0x07, 0x04, 0x01, 0x03 };

        private const string AoChannel = "AO7";
        private static readonly string[] Mtx532EnabledAoChannels = { "AO7" };
        private const int Mtx532ReadyTimeoutMs = 6000;
        private const int Mtx532ReadyPollMs = 200;
        private const int AutoGearSwitchDelayMs = 1500;
        private const double Mtx532SampleRateHz = 20000.0;

        private const double Mtx532VoltageReadbackToleranceV = 0.15;
        private const int Mtx532VoltageSettlePollCount = 10;
        private const int Mtx532VoltageSettlePollMs = 100;

        private const string FixedTxChannel = "429_CH5";
        private const string FixedRxChannel = "429_CH2";

        private readonly A_C_6_11_1_1Simulation _simulation = new A_C_6_11_1_1Simulation();
        private readonly SemaphoreSlim _arincOpLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _manualTestLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _autoTestLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _mtxOpLock = new SemaphoreSlim(1, 1);

        private CancellationTokenSource _autoTestCts;
        private IMtx532Api _mtxApi;

        private string _testTxChannel;
        private string _testRxChannel;

        private string _enterAtpTxChannel;
        private string _enterAtpRxChannel;
        private string _exitAtpTxChannel;
        private string _exitAtpRxChannel;

        private string _enterAtpRxDataText;
        private string _exitAtpRxDataText;

        private string _angleTestTxChannel;

        private string _angleTelemetryRxChannel;
        private string _angleTelemetryValueText;
        private string _angleTelemetryRxDataText;
        private string _angleTelemetryRawRxDataText;

        private bool _isInAtp;

        private string _voltageGear;
        private int _currentGearIndex;
        private string _voltageSetValueText;

        private bool _isBusy;
        private bool _isManualTestRunning;
        private bool _isAutoTestRunning;
        private bool _autoTestEnteredAtp;

        private string _lastTestTime;
        private string _lastTestResult;
        private string _previousTestTime;
        private string _previousTestResult;

        private double _arincRate = 100000.0;

        // 角度遥测持续监听（与 6.9.1 温度遥测监听风格一致）
        private CancellationTokenSource _telemetryListeningCts;
        private Task _telemetryListeningTask;
        private int _angleTelemetrySeq;
        private byte[] _lastAngleTelemetryFrame;
        private byte[] _lastAngleTelemetryRawFrame;

        public A_C_6_11_1_1ViewModel()
        {
            _testTxChannel = FixedTxChannel;
            _testRxChannel = FixedRxChannel;

            // 固定通道显示/使用（与 6.13.2 一致：界面下拉框禁用，仅做固定展示）
            _enterAtpTxChannel = _testTxChannel;
            _enterAtpRxChannel = _testRxChannel;
            _exitAtpTxChannel = _testTxChannel;
            _exitAtpRxChannel = _testRxChannel;
            _angleTestTxChannel = _testTxChannel;
            _angleTelemetryRxChannel = _testRxChannel;

            EnterAtpRxDataText = "--";
            ExitAtpRxDataText = "--";
            AngleTelemetryValueText = "--";
            AngleTelemetryRxDataText = "--";
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
            SendAngleTestCommand = new DelegateCommand(async () => await OnSendAngleTestCommandAsync());
            ReadAngleTelemetryCommand = new DelegateCommand(async () => await OnReadAngleTelemetryAsync());

            _simulation.GetCurrentGearIndex = () => CurrentGearIndex <= 0 ? 1 : CurrentGearIndex;
        }

        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();

        public DelegateCommand ManualTestCommand { get; }
        public DelegateCommand AutoTestCommand { get; }
        public DelegateCommand ClearLogCommand { get; }

        public DelegateCommand SendEnterAtpCommand { get; }
        public DelegateCommand SendExitAtpCommand { get; }
        public DelegateCommand SendSetControllerVoltageCommand { get; }
        public DelegateCommand SendAngleTestCommand { get; }
        public DelegateCommand ReadAngleTelemetryCommand { get; }

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

        public string EnterAtpTxDataText => "0x" + FormatData(EnterAtpCommand8);

        public string TestCommandTxDataText => "0x" + FormatData(AbOfvtrvAngle8);

        public string ExitAtpTxDataText => "0x" + FormatData(ExitAtpCommand8);

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

        public string AngleTestTxChannel
        {
            get => FixedTxChannel;
        }

        public string AngleTelemetryRxChannel
        {
            get => FixedRxChannel;
        }

        public string AngleTelemetryValueText
        {
            get => _angleTelemetryValueText;
            private set => SetProperty(ref _angleTelemetryValueText, value);
        }

        public string AngleTelemetryRxDataText
        {
            get => _angleTelemetryRxDataText;
            private set => SetProperty(ref _angleTelemetryRxDataText, value);
        }

        public string AngleTelemetryRawRxDataText
        {
            get => _angleTelemetryRawRxDataText;
            private set => SetProperty(ref _angleTelemetryRawRxDataText, value);
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

            _ = StartManualTestAsync();
        }

        private void OnAutoTest()
        {
            // 与 6.9.1 一致：自动测试运行中再次点击视为停止；
            // 若手动测试未停止则禁止启动自动测试，避免两种模式并发。
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
                    AngleTelemetryValueText = "--";
                    AngleTelemetryRxDataText = "--";
                    AngleTelemetryRawRxDataText = "--";
                    EnterAtpRxDataText = "--";
                    ExitAtpRxDataText = "--";
                    IsInAtp = false;
                    LastTestTime = "--";
                    LastTestResult = "--";

                    _simulation.IsRealProduct = true;
                    _simulation.ArincRate = ArincRate;

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试启动：开始打开设备");

                    var mtxOk = await PreconnectMtx532ForTestAsync("手动测试", CancellationToken.None);
                    if (!mtxOk)
                    {
                        IsManualTestRunning = false;
                        LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                        SetLastTestResult("FAIL");
                        AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试启动失败：MTX532连接失败");
                        return;
                    }

                    await _simulation.StartAsync(TestTxChannel, TestRxChannel, msg => AddLog(msg));
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试启动：429板卡已就绪");
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
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试停止：关闭设备");
                    IsManualTestRunning = false;

                    await ShutdownOpenedBoardsForTestEndAsync();

                    IsInAtp = false;
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
                    EnterAtpRxDataText = "--";
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 进入ATP指令已发送");

                    StartAngleTelemetryListeningIfNeeded();
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
                    await StopAngleTelemetryListeningAsync();

                    var token = CancellationToken.None;
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 发送退出ATP：TX={ExitAtpTxChannel}");
                    await _simulation.SendBenchCommandOnlyAsync(ExitAtpTxChannel, ExitAtpCommand8, msg => AddLog(msg), token);

                    IsInAtp = false;
                    ExitAtpRxDataText = "--";
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
                1 => 0.25,
                2 => 2.5,
                3 => 4.75,
                _ => 0.25
            };

            IsBusy = true;
            try
            {
                var token = CancellationToken.None;
                AddLog($"[{DateTime.Now:HH:mm:ss}] 设置档位{CurrentGearIndex}：AO7={voltageV.ToString("0.###", CultureInfo.InvariantCulture)}V");

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

        private async Task OnSendAngleTestCommandAsync()
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

                    AngleTelemetryValueText = "--";
                    AngleTelemetryRxDataText = "--";
                    AngleTelemetryRawRxDataText = "--";

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 准备发送AB_OFVTRV_ANGLE：TX={AngleTestTxChannel}, RX={AngleTelemetryRxChannel}, 当前档位={gearIndex}, IsInAtp={IsInAtp}");

                    StartAngleTelemetryListeningIfNeeded();

                    var startSeq = Volatile.Read(ref _angleTelemetrySeq);
                    Interlocked.Exchange(ref _lastAngleTelemetryFrame, null);
                    Interlocked.Exchange(ref _lastAngleTelemetryRawFrame, null);

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 发送前遥测状态：监听任务={(_telemetryListeningTask != null ? "已启动" : "未启动")}, startSeq={startSeq}");
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 发送AB_OFVTRV_ANGLE：TX={AngleTestTxChannel}, Data={FormatData(AbOfvtrvAngle8)}");

                    await _simulation.SendBenchCommandOnlyAsync(AngleTestTxChannel, AbOfvtrvAngle8, msg => AddLog(msg), token);

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 测试指令已发送，等待角度遥测：RX={AngleTelemetryRxChannel}, 编码模板=07 04 01 02 00 00 00 00, timeout=8000ms");
                    var tel = await WaitNextAngleTelemetryFrameAsync(startSeq, timeoutMs: 8000, token: token);
                    var currentSeq = Volatile.Read(ref _angleTelemetrySeq);

                    if (tel == null)
                    {
                        SetLastTestResult("FAIL");
                        AddLog($"[{DateTime.Now:HH:mm:ss}] 角度遥测超时：startSeq={startSeq}, currentSeq={currentSeq}, RX={AngleTelemetryRxChannel}, 未在超时时间内收到前缀 07 04 01 02 的角度帧，请检查产品回发/429接线/通道配置");
                        return;
                    }

                    AngleTelemetryRxDataText = "0x" + FormatData(tel);
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 收到角度遥测帧：Data={FormatData(tel)}, seq={currentSeq}");

                    var rawData = Interlocked.CompareExchange(ref _lastAngleTelemetryRawFrame, null, null);
                    if (rawData != null)
                    {
                        AngleTelemetryRawRxDataText = "0x" + FormatData(rawData);
                    }

                    if (!TryParseTelemetryAngle(tel, out var angle))
                    {
                        SetLastTestResult("FAIL");
                        AngleTelemetryValueText = "--";
                        AddLog($"[{DateTime.Now:HH:mm:ss}] 角度遥测解析失败：Data={FormatData(tel)}");
                        return;
                    }

                    AngleTelemetryValueText = angle.ToString("0.####", CultureInfo.InvariantCulture);
                    var pass = IsAngleQualified(gearIndex, angle);
                    SetLastTestResult(pass ? "PASS" : "FAIL");
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 角度回采完成：档位{gearIndex}, 角度={angle.ToString("0.####", CultureInfo.InvariantCulture)}, 判定={LastTestResult}");
                }
                finally
                {
                    IsBusy = false;
                }
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 发送角度测试命令异常：{ex.Message}");
            }
            finally
            {
                _arincOpLock.Release();
            }
        }

        private async Task OnReadAngleTelemetryAsync()
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

            // 兼容旧界面“角度回采值”按钮：直接复用发送+等待逻辑
            await OnSendAngleTestCommandAsync();
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
                    if (IsManualTestRunning)
                        return;

                    EnsureManualArincChannels();

                    IsAutoTestRunning = true;
                    AngleTelemetryValueText = "--";
                    AngleTelemetryRxDataText = "--";
                    AngleTelemetryRawRxDataText = "--";
                    LastTestTime = "--";
                    LastTestResult = "--";

                    _autoTestCts?.Cancel();
                    _autoTestCts?.Dispose();
                    _autoTestCts = new CancellationTokenSource();
                    var token = _autoTestCts.Token;

                    _simulation.IsRealProduct = true;
                    _simulation.ArincRate = ArincRate;

                    AddLog($"[{DateTime.Now:HH:mm:ss}] ========== 自动测试开始 ==========");

                    var mtxOk = await PreconnectMtx532ForTestAsync("自动测试", token);
                    if (!mtxOk)
                    {
                        SetLastTestResult("FAIL");
                        AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试启动失败：MTX532连接失败");
                        return;
                    }

                    await _simulation.StartAsync(TestTxChannel, TestRxChannel, msg => AddLog(msg));

                    await AutoEnterAtpAsync(token);
                    _autoTestEnteredAtp = true;

                    var failures = new System.Collections.Generic.List<string>();

                    await RunGearAutoAsync(1, 0.25, token, failures);
                    token.ThrowIfCancellationRequested();
                    await RunGearAutoAsync(2, 2.5, token, failures);
                    token.ThrowIfCancellationRequested();
                    await RunGearAutoAsync(3, 4.75, token, failures);

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

            AddLog($"[{DateTime.Now:HH:mm:ss}] 档位{gearIndex}：设置AO7={voltageV.ToString("0.###", CultureInfo.InvariantCulture)}V");
            var okVoltage = await OutputVoltageAsync(voltageV, token);
            if (!okVoltage)
            {
                failures.Add($"档位{gearIndex}电压输出失败");
                return;
            }

            await Task.Delay(50, token);

            await _simulation.ClearRxFifoAsync(TestRxChannel);
            await Task.Delay(20, token);

            StartAngleTelemetryListeningIfNeeded();

            var startSeq = Volatile.Read(ref _angleTelemetrySeq);
            Interlocked.Exchange(ref _lastAngleTelemetryFrame, null);
            Interlocked.Exchange(ref _lastAngleTelemetryRawFrame, null);

            AddLog($"[{DateTime.Now:HH:mm:ss}] 档位{gearIndex}：发送AB_OFVTRV_ANGLE");
            await _simulation.SendBenchCommandOnlyAsync(TestTxChannel, AbOfvtrvAngle8, msg => AddLog(msg), token);

            AddLog($"[{DateTime.Now:HH:mm:ss}] 档位{gearIndex}：等待角度遥测(07 04 01 02)[持续监听]");
            var tel = await WaitNextAngleTelemetryFrameAsync(startSeq, timeoutMs: 7000, token: token);

            if (tel == null)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 档位{gearIndex}：第一次等待角度遥测超时，尝试直接从RX={AngleTelemetryRxChannel}读取角度遥测");

                try { await StopAngleTelemetryListeningAsync(); } catch { }

                try { await _simulation.ClearRxFifoAsync(TestRxChannel); } catch { }
                await Task.Delay(20, token);

                AddLog($"[{DateTime.Now:HH:mm:ss}] 档位{gearIndex}：重发AB_OFVTRV_ANGLE用于直接遥测读取");

                await _simulation.SendBenchCommandOnlyAsync(TestTxChannel, AbOfvtrvAngle8, msg => AddLog(msg), token);

                tel = await DirectWaitAngleTelemetryFrameAsync(gearIndex, token);

                StartAngleTelemetryListeningIfNeeded();

                if (tel == null)
                {
                    failures.Add($"档位{gearIndex}角度遥测超时：RX={AngleTelemetryRxChannel}未收到前缀 07 04 01 02 的回传帧");
                    return;
                }
            }

            AngleTelemetryRxDataText = "0x" + FormatData(tel);

            var rawData = Interlocked.CompareExchange(ref _lastAngleTelemetryRawFrame, null, null);
            if (rawData != null)
            {
                AngleTelemetryRawRxDataText = "0x" + FormatData(rawData);
            }

            if (!TryParseTelemetryAngle(tel, out var angle))
            {
                failures.Add($"档位{gearIndex}角度遥测解析失败");
                AngleTelemetryValueText = "--";
                return;
            }

            AngleTelemetryValueText = angle.ToString("0.####", CultureInfo.InvariantCulture);
            if (!IsAngleQualified(gearIndex, angle))
            {
                failures.Add($"档位{gearIndex}角度不通过：{angle.ToString("0.####", CultureInfo.InvariantCulture)}");
            }
        }

        private async Task<bool> AutoEnterAtpAsync(CancellationToken token)
        {
            AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试：发送进入ATP");
            await _simulation.SendBenchCommandOnlyAsync(TestTxChannel, EnterAtpCommand8, msg => AddLog(msg), token);
            IsInAtp = true;
            StartAngleTelemetryListeningIfNeeded();
            return true;
        }

        private async Task<bool> AutoExitAtpAsync(CancellationToken token)
        {
            AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试：发送退出ATP");
            try { await StopAngleTelemetryListeningAsync(); } catch { }
            await _simulation.SendBenchCommandOnlyAsync(ExitAtpTxChannel, ExitAtpCommand8, msg => AddLog(msg), token);
            IsInAtp = false;
            return true;
        }

        private async Task<bool> OutputVoltageAsync(double voltageV, CancellationToken token)
        {
            VoltageSetValueText = "--";

            await _mtxOpLock.WaitAsync(token);
            try
            {
                var ok = await EnsureMtx532ConnectedAsync(token);
                if (!ok)
                    return false;

                AddLog($"[{DateTime.Now:HH:mm:ss}] MTX532档位电压写入开始：{AoChannel}={voltageV.ToString("0.####", CultureInfo.InvariantCulture)}V（1基，第7个物理通道）");
                await SetAo7Async(voltageV, token);
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

                    var readBackVoltage = await ReadAo7VoltageAsync(token);
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
                return false;
            }
            finally
            {
                _mtxOpLock.Release();
            }
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
                    VoltageSetValueText = "--";
                    return;
                }

                await DisconnectMtx532Async();
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
                await StopAngleTelemetryListeningAsync();
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
                return true;

            AddLog($"[{DateTime.Now:HH:mm:ss}] MTX532连接流程开始：硬件AO为1基，目标业务通道={AoChannel}（第7个物理通道）");
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

            AddLog($"[{DateTime.Now:HH:mm:ss}] MTX532写初始0V：{AoChannel}=0V（1基，第7个物理通道）");
            await SetAo7Async(0.0, token).ConfigureAwait(false);
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

        private async Task<double?> ReadAo7VoltageAsync(CancellationToken token)
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

        private async Task SetAo7Async(double voltageV, CancellationToken token)
        {
            if (_mtxApi == null || !_mtxApi.IsConnected)
                throw new InvalidOperationException("MTX532未连接");

            await _mtxApi.WriteOnceDcAsync(new System.Collections.Generic.Dictionary<string, double>
            {
                [AoChannel] = voltageV
            }, token).ConfigureAwait(false);
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

        private static bool TryParseTelemetryAngle(byte[] frameData, out double angle)
        {
            angle = 0;
            if (frameData == null || frameData.Length < 8)
                return false;

            if (!IsPrefix(frameData, OfvtrvAngleTelemetryPrefix4))
                return false;

            // 根据数据特征：高两字节为00 00，低两字节为 FE 3C (负数)，
            // 说明实际有效数据是16位有符号整数 (short)。
            short raw = (short)((frameData[6] << 8) | frameData[7]);

            // 倍率为 0.01
            angle = raw * 0.01;
            return true;
        }

        #region 持续角度遥测监听

        private void StartAngleTelemetryListeningIfNeeded()
        {
            if (_telemetryListeningTask != null)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 角度遥测监听已在运行：RX={AngleTelemetryRxChannel}, seq={Volatile.Read(ref _angleTelemetrySeq)}");
                return;
            }

            if (string.IsNullOrWhiteSpace(AngleTelemetryRxChannel))
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 角度遥测监听未启动：RX通道为空");
                return;
            }

            if (!IsInAtp)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 角度遥测监听未启动：当前未进入ATP模式");
                return;
            }

            if (!IsManualTestRunning && !IsAutoTestRunning)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 角度遥测监听未启动：手动/自动测试均未运行");
                return;
            }

            _telemetryListeningCts?.Cancel();
            _telemetryListeningCts?.Dispose();
            _telemetryListeningCts = new CancellationTokenSource();
            var token = _telemetryListeningCts.Token;
            var rxChannel = AngleTelemetryRxChannel;

            AddLog($"[{DateTime.Now:HH:mm:ss}] 准备启动角度遥测持续监听：RX={rxChannel}, IsManual={IsManualTestRunning}, IsAuto={IsAutoTestRunning}, IsInAtp={IsInAtp}");

            _telemetryListeningTask = Task.Run(async () =>
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 启动角度遥测持续监听：RX={rxChannel}");
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        var telemetry = await _simulation.WaitTelemetryAsync(
                            rxChannel,
                            timeoutMs: 300,
                            log: _ => { },
                            token: token);

                        var tel = telemetry.Angle;
                        var raw = telemetry.Raw;

                        if (tel != null && TryParseTelemetryAngle(tel, out var angleValue))
                        {
                            var frameCopy = tel.ToArray();
                            var rawCopy = raw?.ToArray();

                            Interlocked.Exchange(ref _lastAngleTelemetryFrame, frameCopy);
                            if (rawCopy != null)
                                Interlocked.Exchange(ref _lastAngleTelemetryRawFrame, rawCopy);
                            var seq = Interlocked.Increment(ref _angleTelemetrySeq);

                            AddLog($"[{DateTime.Now:HH:mm:ss}] 角度遥测监听收到有效帧：seq={seq}, Angle={angleValue.ToString("0.####", CultureInfo.InvariantCulture)}, Data={FormatData(frameCopy)}, Raw={(rawCopy != null ? FormatData(rawCopy) : "--")}");

                            var dispatcher = Application.Current?.Dispatcher;
                            if (dispatcher != null && !dispatcher.CheckAccess())
                            {
                                dispatcher.BeginInvoke(new Action(() =>
                                {
                                    AngleTelemetryRxDataText = "0x" + FormatData(frameCopy);
                                    if (rawCopy != null)
                                        AngleTelemetryRawRxDataText = "0x" + FormatData(rawCopy);
                                    AngleTelemetryValueText = angleValue.ToString("0.####", CultureInfo.InvariantCulture);
                                }));
                            }
                            else
                            {
                                AngleTelemetryRxDataText = "0x" + FormatData(frameCopy);
                                if (rawCopy != null)
                                    AngleTelemetryRawRxDataText = "0x" + FormatData(rawCopy);
                                AngleTelemetryValueText = angleValue.ToString("0.####", CultureInfo.InvariantCulture);
                            }
                        }
                        else
                        {
                            if (tel != null)
                                AddLog($"[{DateTime.Now:HH:mm:ss}] 角度遥测监听收到帧但解析失败：Data={FormatData(tel)}");
                            else if (raw != null)
                                AddLog($"[{DateTime.Now:HH:mm:ss}] 角度遥测监听仅收到原始帧：Raw={FormatData(raw)}");

                            await Task.Delay(30, token);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        AddLog($"[{DateTime.Now:HH:mm:ss}] 角度遥测监听异常：{ex.Message}");
                        try { await Task.Delay(100, token); } catch { break; }
                    }
                }

                AddLog($"[{DateTime.Now:HH:mm:ss}] 角度遥测监听已停止");
            }, token);
        }

        private async Task StopAngleTelemetryListeningAsync()
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

        private async Task<byte[]> WaitNextAngleTelemetryFrameAsync(int startSeq, int timeoutMs, CancellationToken token)
        {
            var startUtc = DateTime.UtcNow;
            var deadline = startUtc.AddMilliseconds(Math.Max(100, timeoutMs));

            while (!token.IsCancellationRequested && DateTime.UtcNow <= deadline)
            {
                var frame = Interlocked.CompareExchange(ref _lastAngleTelemetryFrame, null, null);
                if (frame != null)
                {
                    var elapsedMs = (int)(DateTime.UtcNow - startUtc).TotalMilliseconds;
                    var currentSeq = Volatile.Read(ref _angleTelemetrySeq);
                    AddLog($"[{DateTime.Now:HH:mm:ss}] WaitNextAngleTelemetryFrameAsync 成功：耗时={elapsedMs}ms, startSeq={startSeq}, currentSeq={currentSeq}, 当前档位={CurrentGearIndex}");
                    return frame;
                }

                await Task.Delay(20, token);
            }

            var totalElapsed = (int)(DateTime.UtcNow - startUtc).TotalMilliseconds;
            var finalSeq = Volatile.Read(ref _angleTelemetrySeq);
            AddLog($"[{DateTime.Now:HH:mm:ss}] WaitNextAngleTelemetryFrameAsync 超时：耗时={totalElapsed}ms, startSeq={startSeq}, finalSeq={finalSeq}, 当前档位={CurrentGearIndex}");
            return null;
        }

        private async Task<byte[]> DirectWaitAngleTelemetryFrameAsync(int gearIndex, CancellationToken token)
        {
            var t0 = DateTime.UtcNow;
            var rxChannel = AngleTelemetryRxChannel;
            AddLog($"[{DateTime.Now:HH:mm:ss}] 档位{gearIndex}：直接等待角度遥测：RX={rxChannel}，timeout=2000ms");

            var telemetry = await _simulation.WaitTelemetryAsync(
                rxChannel,
                timeoutMs: 2000,
                log: msg => AddLog(msg),
                token: token);

            var tel = telemetry.Angle;
            var raw = telemetry.Raw;

            if (tel == null)
            {
                var elapsed = (int)Math.Max(0, (DateTime.UtcNow - t0).TotalMilliseconds);
                AddLog($"[{DateTime.Now:HH:mm:ss}] 档位{gearIndex}：直接等待角度遥测失败：{elapsed}ms内未收到07 04 01 02帧");
                return null;
            }

            var frameCopy = tel.ToArray();
            var rawCopy = raw?.ToArray();

            Interlocked.Exchange(ref _lastAngleTelemetryFrame, frameCopy);
            if (rawCopy != null)
                Interlocked.Exchange(ref _lastAngleTelemetryRawFrame, rawCopy);
            var seq = Interlocked.Increment(ref _angleTelemetrySeq);

            AddLog($"[{DateTime.Now:HH:mm:ss}] 档位{gearIndex}：直接等待角度遥测成功：seq={seq}, AngleFrame={FormatData(frameCopy)}, RawFrame={(rawCopy != null ? FormatData(rawCopy) : "--")}");
            return frameCopy;
        }

        #endregion

        private static bool IsAngleQualified(int gearIndex, double angle)
        {
            var (min, max) = gearIndex switch
            {
                1 => (-6.7500, -4.5000),
                2 => (43.8750, 46.1250),
                3 => (94.5000, 96.7500),
                _ => (-6.7500, -4.5000)
            };

            return angle >= min && angle <= max;
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
