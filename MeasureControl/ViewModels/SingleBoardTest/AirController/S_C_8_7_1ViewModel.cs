using Prism.Commands;
using Prism.Ioc;
using Prism.Mvvm;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using MeasureControl.Models.Devices;
using MeasureControl.Models.Devices.DeviceCategories;
using MeasureControl.Services;
using MeasureControl.Services.HardwareApis;
using MeasureControl.Simulations.S_C_8_7_1;

namespace MeasureControl.ViewModels.SingleBoardTest.AirController
{
    public sealed class S_C_8_7_1ViewModel : BindableBase, IDisposable
    {
        private const string FixedTxChannel = "429_CH1";
        private const string FixedRxChannel = "429_CH0";

        private static readonly byte[] EnterAtpCommand8 = { 0x30, 0x01, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] EnterAtpOk8 = { 0x30, 0x01, 0x01, 0x02, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] ExitAtpCommand8 = { 0x30, 0x02, 0x02, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] ExitAtpOk8 = { 0x30, 0x02, 0x02, 0x02, 0x00, 0x00, 0x00, 0x00 };

        private static readonly byte[] SFwdAventsMea018 = { 0x15, 0x02, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] TelemetryPrefix4 = { 0x15, 0x02, 0x01, 0x02 };
        private static readonly byte[] TelemetryRawPrefix4 = { 0x15, 0x02, 0x01, 0x03 };

        private const string AoChannel = "AO3";
        private static readonly string[] Mtx532EnabledAoChannels = { "AO1", "AO2", "AO3" };
        private const int AtpResponseTimeoutMs = 3000;
        private const int Mtx532ReadyTimeoutMs = 6000;
        private const int Mtx532ReadyPollMs = 200;
        private const int AutoGearSwitchDelayMs = 1500;
        private const double Mtx532SampleRateHz = 20000.0;

        private readonly S_C_8_7_1Simulation _simulation = new S_C_8_7_1Simulation();
        private readonly SemaphoreSlim _arincOpLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _manualTestLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _autoTestLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _mtxOpLock = new SemaphoreSlim(1, 1);

        private CancellationTokenSource _autoTestCts;
        private IMtx532Api _mtxApi;

        private string _testTxChannel = FixedTxChannel;
        private string _testRxChannel = FixedRxChannel;
        private string _enterAtpTxChannel = FixedTxChannel;
        private string _enterAtpRxChannel = FixedRxChannel;
        private string _exitAtpTxChannel = FixedTxChannel;
        private string _exitAtpRxChannel = FixedRxChannel;
        private string _testCommandTxChannel = FixedTxChannel;
        private string _testCommandRxChannel = FixedRxChannel;
        private string _telemetryRxChannel = FixedRxChannel;

        private string _voltageGear = "1挡";

        private string _enterAtpRxDataText = "--";
        private string _testCommandRxDataText = "--";
        private string _telemetryRxDataText = "--";
        private string _temperatureValueText = "--";
        private string _exitAtpRxDataText = "--";
        private string _voltageSetValueText = "--";
        private string _lastTestTime = "--";
        private string _lastTestResult = "--";
        private string _previousTestTime = "--";
        private string _previousTestResult = "--";
        private string _mtx532ModeText = "MTX532：未连接";

        private bool _isInAtp;
        private bool _isManualTestRunning;
        private bool _isAutoTestRunning;
        private bool _isBusy;
        private bool _isMtx532RealHardware;
        private bool _autoTestEnteredAtp;
        private int _currentGearIndex = 1;
        private double? _gear1TemperatureC;
        private double? _gear2TemperatureC;
        private double? _gear3TemperatureC;

        private CancellationTokenSource _telemetryListeningCts;
        private Task _telemetryListeningTask;

        private byte[] _lastTemperatureTelemetryFrame;
        private byte[] _lastTemperatureRawFrame;
        private int _telemetrySeq;
        private byte[] _lastPressureTelemetryFrame;

        public S_C_8_7_1ViewModel()
        {
            ManualTestCommand = new DelegateCommand(OnManualTest);
            AutoTestCommand = new DelegateCommand(OnAutoTest);

            SendEnterAtpCommand = new DelegateCommand(async () => await OnSendEnterAtpAsync());
            TestGear1Command = new DelegateCommand(async () => await OnTestGearAsync(1));
            TestGear2Command = new DelegateCommand(async () => await OnTestGearAsync(2));
            TestGear3Command = new DelegateCommand(async () => await OnTestGearAsync(3));
            TestSelectedGearCommand = new DelegateCommand(async () => await OnTestGearAsync(CurrentGearIndex));
            SendExitAtpCommand = new DelegateCommand(async () => await OnSendExitAtpAsync());
            ToggleMtx532HardwareCommand = new DelegateCommand(async () => await ToggleMtx532HardwareAsync());
            ClearLogCommand = new DelegateCommand(() => Logs.Clear());

            SendSetControllerVoltageCommand = new DelegateCommand(async () => await OnSetSelectedGearVoltageAsync());
            SendControllerPressureTestCommand = new DelegateCommand(async () => await OnSendControllerPressureTestAsync());

            _simulation.GetCurrentGearIndex = () => CurrentGearIndex;
        }

        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();
        public DelegateCommand ManualTestCommand { get; }
        public DelegateCommand AutoTestCommand { get; }
        public DelegateCommand SendEnterAtpCommand { get; }
        public DelegateCommand TestGear1Command { get; }
        public DelegateCommand TestGear2Command { get; }
        public DelegateCommand TestGear3Command { get; }
        public DelegateCommand TestSelectedGearCommand { get; }
        public DelegateCommand SendExitAtpCommand { get; }
        public DelegateCommand ToggleMtx532HardwareCommand { get; }
        public DelegateCommand ClearLogCommand { get; }

        public DelegateCommand SendSetControllerVoltageCommand { get; }
        public DelegateCommand SendControllerPressureTestCommand { get; }

        public string TestTxChannel { get => FixedTxChannel; }
        public string TestRxChannel { get => FixedRxChannel; }
        public string EnterAtpTxChannel { get => FixedTxChannel; }
        public string EnterAtpRxChannel { get => FixedRxChannel; }
        public string ExitAtpTxChannel { get => FixedTxChannel; }
        public string ExitAtpRxChannel { get => FixedRxChannel; }
        public string TestCommandTxChannel { get => FixedTxChannel; }
        public string TestCommandRxChannel { get => FixedRxChannel; }
        public string TelemetryRxChannel { get => FixedRxChannel; }
        public string EnterAtpTxDataText { get => "0x" + FormatData(EnterAtpCommand8); }
        public string TestCommandTxDataText { get => "0x" + FormatData(SFwdAventsMea018); }
        public string ExitAtpTxDataText { get => "0x" + FormatData(ExitAtpCommand8); }

        public string VoltageGear
        {
            get => _voltageGear;
            set
            {
                if (!SetProperty(ref _voltageGear, value))
                    return;

                if (string.IsNullOrWhiteSpace(value))
                    return;

                CurrentGearIndex = value switch
                {
                    "1挡" => 1,
                    "2挡" => 2,
                    "3挡" => 3,
                    _ => 1
                };
            }
        }

        public string ControllerPressureTestTxChannel
        {
            get => TestCommandTxChannel;
        }

        public string PressureTelemetryRxChannel
        {
            get => TelemetryRxChannel;
        }

        public string PressureTelemetryValueText => TemperatureValueText;

        public string PressureTelemetryRxDataText => TelemetryRxDataText;

        public string EnterAtpRxDataText { get => _enterAtpRxDataText; private set => SetProperty(ref _enterAtpRxDataText, value); }
        public string TestCommandRxDataText
        {
            get => _testCommandRxDataText;
            private set
            {
                SetProperty(ref _testCommandRxDataText, value);
            }
        }

        public string TelemetryRxDataText
        {
            get => _telemetryRxDataText;
            private set
            {
                if (SetProperty(ref _telemetryRxDataText, value))
                {
                    RaisePropertyChanged(nameof(PressureTelemetryRxDataText));
                }
            }
        }

        public string TemperatureValueText
        {
            get => _temperatureValueText;
            private set
            {
                if (SetProperty(ref _temperatureValueText, value))
                {
                    RaisePropertyChanged(nameof(PressureTelemetryValueText));
                }
            }
        }
        public string ExitAtpRxDataText { get => _exitAtpRxDataText; private set => SetProperty(ref _exitAtpRxDataText, value); }
        public string VoltageSetValueText { get => _voltageSetValueText; private set => SetProperty(ref _voltageSetValueText, value); }
        public string LastTestTime { get => _lastTestTime; private set => SetProperty(ref _lastTestTime, value); }
        public string LastTestResult { get => _lastTestResult; private set => SetProperty(ref _lastTestResult, value); }
        public string PreviousTestTime { get => _previousTestTime; private set => SetProperty(ref _previousTestTime, value); }
        public string PreviousTestResult { get => _previousTestResult; private set => SetProperty(ref _previousTestResult, value); }
        public string Mtx532ModeText { get => _mtx532ModeText; private set => SetProperty(ref _mtx532ModeText, value); }

        public bool IsInAtp { get => _isInAtp; private set => SetProperty(ref _isInAtp, value); }
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

        public int CurrentGearIndex
        {
            get => _currentGearIndex;
            set
            {
                if (SetProperty(ref _currentGearIndex, value))
                {
                    RaisePropertyChanged(nameof(CurrentGearName));
                }
            }
        }

        public string CurrentGearName => CurrentGearIndex switch
        {
            1 => "1挡",
            2 => "2挡",
            3 => "3挡",
            _ => "1挡"
        };

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

        private void OnManualTest()
        {
            AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试按钮点击");
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
                try { _autoTestCts?.Cancel(); } catch { }
                AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试停止请求");
                return;
            }

            if (IsManualTestRunning)
            {
                MessageBox.Show("请先停止手动测试，再开始自动测试。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _ = StartAutoTestAsync();
        }

        private static async Task TryApplyComponentDownStateAsync(CancellationToken token)
        {
            try
            {
                var api = Prism.Ioc.ContainerLocator.Container.Resolve(typeof(MeasureControl.Services.HardwareApis.IComponentPowerStateApi)) as MeasureControl.Services.HardwareApis.IComponentPowerStateApi;
                if (api != null)
                    await api.ApplyComponentDownStateAsync(token).ConfigureAwait(false);
            }
            catch
            {
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
                LastTestTime = "--";
                LastTestResult = "--";
                _gear1TemperatureC = null;
                _gear2TemperatureC = null;
                _gear3TemperatureC = null;
                Interlocked.Exchange(ref _lastTemperatureTelemetryFrame, null);
                Interlocked.Exchange(ref _lastTemperatureRawFrame, null);
                Interlocked.Exchange(ref _lastPressureTelemetryFrame, null);
                AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试启动：开始打开设备");

                IsBusy = true;
                try
                {
                    SyncChannels();
                    ResetDisplayState();

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
                    _simulation.ArincRate = 100000.0;
                    _simulation.SimProductArincRate = 100000.0;
                    _simulation.SimProductRxChannelIndex = 4;
                    _simulation.SimProductTxChannelIndex = 5;

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
                    try { await TryApplyComponentDownStateAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
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

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 发送进入ATP：{FormatData(EnterAtpCommand8)}");
                    await _simulation.SendBenchCommandOnlyAsync(EnterAtpTxChannel, EnterAtpCommand8, msg => AddLog(msg), CancellationToken.None);

                    var resp = await _simulation.WaitBenchResponse8Async(
                        EnterAtpRxChannel,
                        b => b != null && b.SequenceEqual(EnterAtpOk8),
                        timeoutMs: AtpResponseTimeoutMs,
                        log: msg => AddLog(msg),
                        token: CancellationToken.None);

                    if (resp == null)
                    {
                        SetLastTestResult("FAIL");
                        AddLog($"[{DateTime.Now:HH:mm:ss}] 进入ATP超时");
                        return;
                    }

                    EnterAtpRxDataText = "0x" + FormatData(resp);
                    IsInAtp = true;
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 进入ATP成功");
                    StartTelemetryListeningIfNeeded();
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

        private async Task OnTestGearAsync(int gearIndex)
        {
            if (!IsManualTestRunning || IsBusy)
                return;
            if (!IsInAtp)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 请先进入ATP模式");
                return;
            }
            if (gearIndex is < 1 or > 3)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 档位无效");
                return;
            }

            await _arincOpLock.WaitAsync();
            try
            {
                IsBusy = true;
                try
                {
                    CurrentGearIndex = gearIndex;
                    TelemetryRxDataText = "--";
                    TemperatureValueText = "--";
                    TestCommandRxDataText = "--";
                    Interlocked.Exchange(ref _lastTemperatureTelemetryFrame, null);
                    Interlocked.Exchange(ref _lastTemperatureRawFrame, null);

                    var voltageV = GetGearVoltage(gearIndex);
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 档位{gearIndex}：设置AO3={voltageV.ToString("0.###", CultureInfo.InvariantCulture)}V");

                    var okVoltage = await OutputVoltageAsync(voltageV, CancellationToken.None);
                    if (!okVoltage)
                    {
                        SetLastTestResult("FAIL");
                        AddLog($"[{DateTime.Now:HH:mm:ss}] AO3输出失败");
                        return;
                    }

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 档位{gearIndex}：AO3输出后等待稳定3s");
                    await Task.Delay(TimeSpan.FromSeconds(3));

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 发送S_FWDAVENTS_MEA01：{FormatData(SFwdAventsMea018)}");
                    await _simulation.SendBenchCommandOnlyAsync(TestCommandTxChannel, SFwdAventsMea018, msg => AddLog(msg), CancellationToken.None);

                    var startSeq = Volatile.Read(ref _telemetrySeq);
                    Interlocked.Exchange(ref _lastPressureTelemetryFrame, null);
                    Interlocked.Exchange(ref _lastTemperatureTelemetryFrame, null);
                    Interlocked.Exchange(ref _lastTemperatureRawFrame, null);
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 档位{gearIndex}：等待温度遥测[持续监听]");

                    var maxWaitMs = 2000;
                    var sw = Stopwatch.StartNew();
                    byte[] tel = null;
                    while (sw.ElapsedMilliseconds < maxWaitMs)
                    {
                        var frame = Interlocked.CompareExchange(ref _lastPressureTelemetryFrame, null, null);
                        if (frame != null)
                        {
                            tel = frame;
                            break;
                        }
                        await Task.Delay(50);
                    }

                    if (tel == null)
                    {
                        SetLastTestResult("FAIL");
                        AddLog($"[{DateTime.Now:HH:mm:ss}] 温度遥测超时");
                        return;
                    }

                    TelemetryRxDataText = "0x" + FormatData(tel);
                    var rawData = Interlocked.CompareExchange(ref _lastTemperatureRawFrame, null, null);
                    if (!TryParseTelemetryTemperature(tel, out var temperature))
                    {
                        SetLastTestResult("FAIL");
                        AddLog($"[{DateTime.Now:HH:mm:ss}] 温度遥测解析失败");
                        return;
                    }

                    TemperatureValueText = temperature.ToString("0.####", CultureInfo.InvariantCulture);
                    if (rawData != null)
                        LogTemperatureRawData(rawData);
                    SetGearTemperature(gearIndex, temperature);
                    var (min, max) = GetQualifiedTemperatureRange(gearIndex);
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 档位{gearIndex}：温度={temperature.ToString("0.####", CultureInfo.InvariantCulture)}℃，范围[{min.ToString("0.####", CultureInfo.InvariantCulture)},{max.ToString("0.####", CultureInfo.InvariantCulture)}]℃");
                    var pass = IsTemperatureQualified(gearIndex, temperature);
                    SetLastTestResult(pass ? $"{FormatGearForResult(gearIndex)}温度PASS" : $"{FormatGearForResult(gearIndex)}温度不通过");
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 档位{gearIndex}测试结束：{LastTestResult}");
                }
                finally
                {
                    IsBusy = false;
                }
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 档位测试异常: {ex.Message}");
            }
            finally
            {
                _arincOpLock.Release();
            }
        }

        private async Task OnSetSelectedGearVoltageAsync()
        {
            if (!IsManualTestRunning && !IsAutoTestRunning)
                return;
            if (IsBusy)
                return;
            if (CurrentGearIndex is < 1 or > 3)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 请先选择接入电压挡位");
                return;
            }

            try
            {
                IsBusy = true;

                var voltageV = GetGearVoltage(CurrentGearIndex);
                AddLog($"[{DateTime.Now:HH:mm:ss}] 发送：接入电压，档位={CurrentGearName}，目标={voltageV.ToString("0.###", CultureInfo.InvariantCulture)}V");

                var ok = await OutputVoltageAsync(voltageV, CancellationToken.None);
                if (!ok)
                {
                    SetLastTestResult("FAIL");
                    AddLog($"[{DateTime.Now:HH:mm:ss}] AO3输出失败");
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 电压输出失败");
                }
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 电压输出异常：{ex.Message}");
            }
            finally
            {
                IsBusy = false;
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
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 发送S_FWDAVENTS_MEA01：TX={ControllerPressureTestTxChannel}");
                    await _simulation.SendBenchCommandOnlyAsync(ControllerPressureTestTxChannel, SFwdAventsMea018, msg => AddLog(msg), CancellationToken.None);
                }
                finally
                {
                    IsBusy = false;
                }
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 测试指令异常：{ex.Message}");
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
                    await StopTelemetryListeningAsync();

                    try { await _simulation.ClearRxFifoAsync(ExitAtpRxChannel); } catch { }
                    await Task.Delay(20);

                    var token = CancellationToken.None;

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 发送退出ATP：{FormatData(ExitAtpCommand8)}");
                    await _simulation.SendBenchCommandOnlyAsync(ExitAtpTxChannel, ExitAtpCommand8, msg => AddLog(msg), token);

                    var resp = await _simulation.WaitBenchResponse8Async(
                        ExitAtpRxChannel,
                        b => b != null && b.SequenceEqual(ExitAtpOk8),
                        timeoutMs: 1200,
                        log: msg => AddLog(msg),
                        token: token);

                    if (resp == null)
                    {
                        SetLastTestResult("FAIL");
                        AddLog($"[{DateTime.Now:HH:mm:ss}] 退出ATP失败：未收到OK");
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

        private async Task StartAutoTestAsync()
        {
            await _autoTestLock.WaitAsync();
            try
            {
                if (IsManualTestRunning)
                    return;

                if (IsBusy)
                    return;

                IsBusy = true;
                IsAutoTestRunning = true;
                try
                {
                    SyncChannels();
                    ResetDisplayState();
                    _autoTestEnteredAtp = false;
                    _gear1TemperatureC = null;
                    _gear2TemperatureC = null;
                    _gear3TemperatureC = null;
                    Interlocked.Exchange(ref _lastTemperatureTelemetryFrame, null);
                    Interlocked.Exchange(ref _lastTemperatureRawFrame, null);
                    Interlocked.Exchange(ref _lastPressureTelemetryFrame, null);

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
                    _simulation.ArincRate = 100000.0;
                    _simulation.SimProductArincRate = 100000.0;
                    _simulation.SimProductRxChannelIndex = 4;
                    _simulation.SimProductTxChannelIndex = 5;

                    AddLog($"[{DateTime.Now:HH:mm:ss}] ========== 自动测试开始 ==========");
                    await _simulation.StartAsync(TestTxChannel, TestRxChannel, msg => AddLog(msg));

                    var enteredAtpOk = await AutoEnterAtpAsync(token);
                    if (!enteredAtpOk)
                    {
                        SetLastTestResult("FAIL");
                        AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试失败：进入ATP超时");
                        return;
                    }

                    _autoTestEnteredAtp = true;

                    var failures = new List<string>();
                    for (int gear = 1; gear <= 3; gear++)
                    {
                        token.ThrowIfCancellationRequested();
                        await RunAutoGearAsync(gear, token, failures);
                        if (gear < 3)
                        {
                            AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试：档位{gear}完成，等待{AutoGearSwitchDelayMs}ms后切换下一档位");
                            await Task.Delay(AutoGearSwitchDelayMs, token);
                        }
                    }

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试汇总：1挡温度={(_gear1TemperatureC?.ToString("0.####", CultureInfo.InvariantCulture) ?? "--")}℃");
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试汇总：2挡温度={(_gear2TemperatureC?.ToString("0.####", CultureInfo.InvariantCulture) ?? "--")}℃");
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试汇总：3挡温度={(_gear3TemperatureC?.ToString("0.####", CultureInfo.InvariantCulture) ?? "--")}℃");

                    var exitedAtpOk = await AutoExitAtpAsync(token);
                    if (!exitedAtpOk)
                    {
                        failures.Add("退出ATP超时");
                    }
                    else
                    {
                        _autoTestEnteredAtp = false;
                    }

                    if (failures.Count == 0)
                    {
                        SetLastTestResult("三档温度PASS");
                    }
                    else
                    {
                        SetLastTestResult("三档温度不通过");
                        foreach (var f in failures)
                            AddLog($"[{DateTime.Now:HH:mm:ss}] 不合格：{f}");
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

        private async Task<bool> AutoEnterAtpAsync(CancellationToken token)
        {
            AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试：发送进入ATP");
            await _simulation.ClearRxFifoAsync(TestRxChannel);
            await Task.Delay(20, token);
            await _simulation.SendBenchCommandOnlyAsync(TestTxChannel, EnterAtpCommand8, msg => AddLog(msg), token);

            var enterOk = await _simulation.WaitBenchResponse8Async(
                TestRxChannel,
                b => b != null && b.SequenceEqual(EnterAtpOk8),
                timeoutMs: AtpResponseTimeoutMs,
                log: msg => AddLog(msg),
                token: token);

            if (enterOk == null)
                return false;

            EnterAtpRxDataText = "0x" + FormatData(enterOk);
            IsInAtp = true;
            StartTelemetryListeningIfNeeded();
            return true;
        }

        private async Task<bool> AutoExitAtpAsync(CancellationToken token)
        {
            AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试：发送退出ATP");
            try { await StopTelemetryListeningAsync(); } catch { }
            try { await _simulation.ClearRxFifoAsync(ExitAtpRxChannel); } catch { }
            await Task.Delay(20, token);
            await _simulation.SendBenchCommandOnlyAsync(ExitAtpTxChannel, ExitAtpCommand8, msg => AddLog(msg), token);

            var exitOk = await _simulation.WaitBenchResponse8Async(
                ExitAtpRxChannel,
                b => b != null && b.SequenceEqual(ExitAtpOk8),
                timeoutMs: 1200,
                log: msg => AddLog(msg),
                token: token);

            if (exitOk == null)
                return false;

            ExitAtpRxDataText = "0x" + FormatData(exitOk);
            IsInAtp = false;
            return true;
        }

        private Task StopAutoTestAsync()
        {
            try { _autoTestCts?.Cancel(); } catch { }
            AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试停止请求");
            return Task.CompletedTask;
        }

        private async Task RunAutoGearAsync(int gearIndex, CancellationToken token, List<string> failures)
        {
            CurrentGearIndex = gearIndex;
            var voltageV = GetGearVoltage(gearIndex);
            AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试：{CurrentGearName} AO3={voltageV.ToString("0.###", CultureInfo.InvariantCulture)}V");
            var okVoltage = await OutputVoltageAsync(voltageV, token);
            if (!okVoltage)
            {
                failures.Add($"档位{gearIndex} AO3输出失败");
                return;
            }

            AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试：{CurrentGearName} AO3输出后等待稳定3s");
            await Task.Delay(TimeSpan.FromSeconds(3), token);

            StartTelemetryListeningIfNeeded();
            var startSeq = Volatile.Read(ref _telemetrySeq);
            Interlocked.Exchange(ref _lastPressureTelemetryFrame, null);
            Interlocked.Exchange(ref _lastTemperatureTelemetryFrame, null);
            Interlocked.Exchange(ref _lastTemperatureRawFrame, null);

            await _simulation.SendBenchCommandOnlyAsync(TestTxChannel, SFwdAventsMea018, msg => AddLog(msg), token);

            var maxWaitMs = 2000;
            var sw = Stopwatch.StartNew();
            byte[] tel = null;
            while (sw.ElapsedMilliseconds < maxWaitMs && !token.IsCancellationRequested)
            {
                if (Volatile.Read(ref _telemetrySeq) > startSeq)
                {
                    var frame = Interlocked.CompareExchange(ref _lastPressureTelemetryFrame, null, null);
                    if (frame != null)
                    {
                        tel = frame;
                        break;
                    }
                }
                await Task.Delay(50, token);
            }

            if (tel == null)
            {
                failures.Add($"档位{gearIndex} 温度遥测超时");
                return;
            }

            TelemetryRxDataText = "0x" + FormatData(tel);
            var rawData = Interlocked.CompareExchange(ref _lastTemperatureRawFrame, null, null);
            if (!TryParseTelemetryTemperature(tel, out var temperature))
            {
                failures.Add($"档位{gearIndex} 温度遥测解析失败");
                return;
            }

            TemperatureValueText = temperature.ToString("0.####", CultureInfo.InvariantCulture);
            if (rawData != null)
                LogTemperatureRawData(rawData);
            SetGearTemperature(gearIndex, temperature);
            var (min, max) = GetQualifiedTemperatureRange(gearIndex);
            AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试：{CurrentGearName} 温度={temperature.ToString("0.####", CultureInfo.InvariantCulture)}℃，范围[{min.ToString("0.####", CultureInfo.InvariantCulture)},{max.ToString("0.####", CultureInfo.InvariantCulture)}]℃");
            if (!IsTemperatureQualified(gearIndex, temperature))
            {
                failures.Add($"档位{gearIndex} 温度不通过：{temperature.ToString("0.####", CultureInfo.InvariantCulture)}℃");
            }
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

        private static string FormatGearForResult(int gearIndex)
        {
            return gearIndex switch
            {
                1 => "第1档",
                2 => "第2档",
                3 => "第3档",
                _ => "第?档"
            };
        }

        private void SetGearTemperature(int gearIndex, double temperature)
        {
            switch (gearIndex)
            {
                case 1:
                    _gear1TemperatureC = temperature;
                    break;
                case 2:
                    _gear2TemperatureC = temperature;
                    break;
                case 3:
                    _gear3TemperatureC = temperature;
                    break;
            }
        }

        private async Task ResetMtx532OutputToZeroAsync()
        {
            await _mtxOpLock.WaitAsync();
            try
            {
                if (_mtxApi == null || !_mtxApi.IsConnected)
                    return;

                await SetAo3Async(0.0, CancellationToken.None);
                VoltageSetValueText = "--";
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] MTX532复位AO3异常：{ex.Message}");
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
                await StopTelemetryListeningAsync();
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
                AddLog($"[{DateTime.Now:HH:mm:ss}] MTX532档位电压写入开始：{AoChannel}={voltageV.ToString("0.####", CultureInfo.InvariantCulture)}V（1基，第3个物理通道）");
                await SetAo3Async(voltageV, token);
                AddLog($"[{DateTime.Now:HH:mm:ss}] MTX532档位电压写入完成：{AoChannel}={voltageV.ToString("0.####", CultureInfo.InvariantCulture)}V，IsOutputRunning={_mtxApi.IsOutputRunning}");
                if (!_mtxApi.IsOutputRunning)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] MTX532输出未运行，准备等待Ready后启动输出");
                    await WaitForMtx532ReadyAsync(token);
                    AddLog($"[{DateTime.Now:HH:mm:ss}] MTX532 Ready完成，开始StartOutputAsync");
                    await _mtxApi.StartOutputAsync(token);
                    AddLog($"[{DateTime.Now:HH:mm:ss}] MTX532 StartOutputAsync完成，IsOutputRunning={_mtxApi.IsOutputRunning}");
                }

                await Task.Delay(100, token);
                var readBackVoltage = await ReadAo3VoltageAsync(token);
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

        private async Task<double?> ReadAo3VoltageAsync(CancellationToken token)
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

        private async Task SetAo3Async(double voltageV, CancellationToken token)
        {
            if (_mtxApi == null || !_mtxApi.IsConnected)
                throw new InvalidOperationException("MTX532未连接");

            await _mtxApi.WriteOnceDcAsync(new Dictionary<string, double>
            {
                ["AO1"] = 0.0,
                ["AO2"] = 0.0,
                ["AO3"] = voltageV
            }, token).ConfigureAwait(false);
        }

        private async Task ToggleMtx532HardwareAsync()
        {
            await _mtxOpLock.WaitAsync();
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
                    AddLog($"[{DateTime.Now:HH:mm:ss}] MTX532连接失败");
                }
            }
            finally
            {
                _mtxOpLock.Release();
            }
        }

        private async Task<bool> EnsureMtx532ConnectedAsync(CancellationToken token = default)
        {
            if (_mtxApi != null && _mtxApi.IsConnected)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] MTX532已连接，跳过重新连接，IsOutputRunning={_mtxApi.IsOutputRunning}");
                return true;
            }

            AddLog($"[{DateTime.Now:HH:mm:ss}] MTX532连接流程开始：参考HC_6_5；硬件AO为1基，目标业务通道={AoChannel}（第3个物理通道）");
            var device = FindMtx532Device();
            if (device == null)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] MTX532连接失败：未找到MTX532(模拟量输出)板卡");
                return false;
            }

            var slotNumber = device is PxiDeviceBase pxi ? pxi.SlotIndex : 7;
            AddLog($"[{DateTime.Now:HH:mm:ss}] MTX532设备已找到：Name={device.Name ?? "--"}，Model={device.Model ?? "--"}，Slot={slotNumber}");
            var options = new Mtx532Options
            {
                SampleRateHz = Mtx532SampleRateHz,
                UseOneBasedAoChannelNumbering = true
            };

            _mtxApi = new Mtx532Api(device, options, slotNumber: slotNumber);
            AddLog($"[{DateTime.Now:HH:mm:ss}] MTX532开始ConnectAsync：enabledAoChannels={string.Join(",", Mtx532EnabledAoChannels)}（1基），目标输出={AoChannel}，SampleRate={Mtx532SampleRateHz.ToString("0.####", CultureInfo.InvariantCulture)}Hz");
            await _mtxApi.ConnectAsync(token, Mtx532EnabledAoChannels).ConfigureAwait(false);
            AddLog($"[{DateTime.Now:HH:mm:ss}] MTX532 ConnectAsync完成：IsConnected={_mtxApi.IsConnected}");

            AddLog($"[{DateTime.Now:HH:mm:ss}] MTX532写初始0V：{AoChannel}=0V（1基，第3个物理通道）");
            await SetAo3Async(0.0, token).ConfigureAwait(false);
            AddLog($"[{DateTime.Now:HH:mm:ss}] MTX532初始0V写入完成，等待300ms");
            await Task.Delay(300, token).ConfigureAwait(false);
            AddLog($"[{DateTime.Now:HH:mm:ss}] MTX532开始等待Ready");
            await WaitForMtx532ReadyAsync(token).ConfigureAwait(false);
            AddLog($"[{DateTime.Now:HH:mm:ss}] MTX532开始StartOutputAsync");
            await _mtxApi.StartOutputAsync(token).ConfigureAwait(false);
            AddLog($"[{DateTime.Now:HH:mm:ss}] MTX532 StartOutputAsync完成：IsOutputRunning={_mtxApi.IsOutputRunning}");
            await Task.Delay(300, token).ConfigureAwait(false);
            AddLog($"[{DateTime.Now:HH:mm:ss}] MTX532连接流程完成：IsConnected={_mtxApi.IsConnected}，IsOutputRunning={_mtxApi.IsOutputRunning}");
            return _mtxApi.IsConnected;
        }

        private async Task WaitForMtx532ReadyAsync(CancellationToken token)
        {
            if (_mtxApi == null || !_mtxApi.IsConnected)
                throw new InvalidOperationException("MTX532未连接");

            var deadline = DateTime.UtcNow.AddMilliseconds(Mtx532ReadyTimeoutMs);
            var pollCount = 0;
            while (DateTime.UtcNow <= deadline)
            {
                token.ThrowIfCancellationRequested();
                pollCount++;

                if (await _mtxApi.CanStartOutputAsync(token))
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] MTX532 Ready成功：poll={pollCount}，IsOutputPrepared={_mtxApi.IsOutputPrepared}，IsOutputRunning={_mtxApi.IsOutputRunning}");
                    return;
                }

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
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] MTX532查找失败：无法解析IPxiChassisService");
                    return null;
                }

                var chassisList = pxiService.GetAllChassis();
                if (chassisList == null)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] MTX532查找失败：GetAllChassis返回null");
                    return null;
                }

                foreach (var chassis in chassisList)
                {
                    var chassisName = chassis?.Name ?? "--";
                    var candidates = new List<DeviceBase>();
                    AddMtx532Candidates(candidates, chassis?.Devices);

                    var serviceDevices = pxiService.GetChassisDevices(chassisName);
                    AddMtx532Candidates(candidates, serviceDevices);

                    AddLog($"[{DateTime.Now:HH:mm:ss}] MTX532查找：机箱={chassisName}，候选总数={candidates.Count}");
                    foreach (var candidate in candidates)
                    {
                        AddLog($"[{DateTime.Now:HH:mm:ss}] MTX532候选：机箱={chassisName}，Type={candidate.GetType().Name}，DeviceType={candidate.DeviceType ?? "--"}，Name={candidate.Name ?? "--"}，CardName={candidate.CardName ?? "--"}，Model={candidate.Model ?? "--"}，DeviceTypeName={candidate.DeviceTypeName ?? "--"}，ParentNode={candidate.ParentNode ?? "--"}，SlotPosition={candidate.SlotPosition ?? "--"}，Slot={(candidate as PxiDeviceBase)?.SlotIndex.ToString(CultureInfo.InvariantCulture) ?? "--"}");
                    }

                    var device = candidates.FirstOrDefault(IsMtx532Device);
                    if (device != null)
                    {
                        AddLog($"[{DateTime.Now:HH:mm:ss}] MTX532查找成功：机箱={chassisName}，Type={device.GetType().Name}，Name={device.Name ?? "--"}，CardName={device.CardName ?? "--"}，Model={device.Model ?? "--"}，SlotPosition={device.SlotPosition ?? "--"}，Slot={(device as PxiDeviceBase)?.SlotIndex.ToString(CultureInfo.InvariantCulture) ?? "--"}");
                        return device;
                    }
                }

                var fallback = new AnalogOutputDevice("模拟量输出", "Slot 7")
                {
                    CardName = "模拟量输出",
                    SlotIndex = 7
                };

                AddLog($"[{DateTime.Now:HH:mm:ss}] MTX532查找未命中服务设备，按UI固定配置兜底创建AnalogOutputDevice：Model={fallback.Model ?? "--"}，SlotPosition={fallback.SlotPosition ?? "--"}，Slot={fallback.SlotIndex}");
                return fallback;
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] MTX532查找异常：{ex.Message}");
                return null;
            }
        }

        private static void AddMtx532Candidates(List<DeviceBase> candidates, IEnumerable<DeviceBase> devices)
        {
            if (candidates == null || devices == null)
                return;

            foreach (var device in FlattenDevices(devices))
            {
                if (device == null)
                    continue;
                if (candidates.Contains(device))
                    continue;

                candidates.Add(device);
            }
        }

        private static IEnumerable<DeviceBase> FlattenDevices(IEnumerable<DeviceBase> devices)
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

            // 1) 优先按 C# 类型判定（默认构造器下Model为空也能识别）
            if (device is AnalogOutputDevice)
                return true;

            // 2) 业务语义：ParentNode / DeviceTypeName 为 “模拟量输出”
            if (!string.IsNullOrWhiteSpace(device.ParentNode) &&
                device.ParentNode.IndexOf("模拟量输出", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (!string.IsNullOrWhiteSpace(device.DeviceTypeName) &&
                device.DeviceTypeName.IndexOf("模拟量输出", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            // 3) 字符串兑底（带参构造器写了 Model="MT-X532" 的情况）
            var fields = new[]
            {
                device.Name,
                device.DisplayName,
                device.CardName,
                device.Model,
                device.DeviceTypeName,
                device.DeviceType,
                device.Description,
                device.Details
            };

            return fields.Any(value =>
                !string.IsNullOrWhiteSpace(value) &&
                (value.IndexOf("MT-X532", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 value.IndexOf("MTX532", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 value.IndexOf("X532", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 value.IndexOf("532", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 value.IndexOf("MTX", StringComparison.OrdinalIgnoreCase) >= 0));
        }

        private static bool TryParseTelemetryTemperature(byte[] frameData, out double temperature)
        {
            temperature = 0;
            if (frameData == null || frameData.Length < 8)
                return false;
            if (!IsPrefix(frameData, TelemetryPrefix4))
                return false;

            var raw = (ushort)((frameData[6] << 8) | frameData[7]);
            var signedRaw = unchecked((short)raw);
            temperature = signedRaw * 0.01;
            return true;
        }

        private static bool TryParseBase6FromNibbles(byte[] frameData, out long value)
        {
            value = 0;
            if (frameData == null || frameData.Length < 8)
                return false;
            if (!IsPrefix(frameData, TelemetryRawPrefix4))
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

        private void LogTemperatureRawData(byte[] rawData)
        {
            var rawHex = FormatData(rawData);
            if (TryParseBase6FromNibbles(rawData, out var rawBase6Decimal))
                AddLog($"[{DateTime.Now:HH:mm:ss}] FWD_AVENTS1温度原始数据(15 02 01 03) 后四字节(6进制)->10进制：{rawBase6Decimal}，Data={rawHex}");
            else
                AddLog($"[{DateTime.Now:HH:mm:ss}] FWD_AVENTS1温度原始数据(15 02 01 03) Data={rawHex}");
        }

        private static bool IsTemperatureQualified(int gearIndex, double temperature)
        {
            var (min, max) = GetQualifiedTemperatureRange(gearIndex);
            return temperature >= min && temperature <= max;
        }

        private static (double Min, double Max) GetQualifiedTemperatureRange(int gearIndex)
        {
            return gearIndex switch
            {
                1 => (-65.98, -64.02),
                2 => (25.12, 28.88),
                3 => (134.02, 137.98),
                _ => (-65.98, -64.02)
            };
        }

        private static bool IsPrefix(byte[] data, byte[] prefix)
        {
            if (data == null || prefix == null || data.Length < prefix.Length)
                return false;
            for (int i = 0; i < prefix.Length; i++)
            {
                if (data[i] != prefix[i])
                    return false;
            }
            return true;
        }

        private static string FormatData(byte[] data)
        {
            if (data == null || data.Length == 0)
                return string.Empty;
            return string.Join(" ", data.Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));
        }

        private void SyncChannels()
        {
        }

        private void ResetDisplayState()
        {
            EnterAtpRxDataText = "--";
            TestCommandRxDataText = "--";
            TelemetryRxDataText = "--";
            TemperatureValueText = "--";
            ExitAtpRxDataText = "--";
            VoltageSetValueText = "--";
            LastTestTime = "--";
            LastTestResult = "--";
            IsInAtp = false;
        }

        private void SetLastTestResult(string result)
        {
            PreviousTestTime = LastTestTime;
            PreviousTestResult = LastTestResult;
            LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            LastTestResult = result;
        }

        private void StartTelemetryListeningIfNeeded()
        {
            if (_telemetryListeningTask != null)
                return;
            if (string.IsNullOrWhiteSpace(TelemetryRxChannel))
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
                AddLog($"[{DateTime.Now:HH:mm:ss}] 启动温度遥测持续监听：RX={TelemetryRxChannel}");
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        var (tempData, rawData) = await _simulation.WaitTelemetryAsync(
                            TelemetryRxChannel,
                            timeoutMs: 300,
                            msg => { },
                            token);

                        if (tempData != null && TryParseTelemetryTemperature(tempData, out var temperature))
                        {
                            var frameCopy = tempData.ToArray();
                            var rawCopy = rawData?.ToArray();
                            Interlocked.Exchange(ref _lastTemperatureTelemetryFrame, frameCopy);
                            if (rawCopy != null)
                                Interlocked.Exchange(ref _lastTemperatureRawFrame, rawCopy);
                            Interlocked.Exchange(ref _lastPressureTelemetryFrame, frameCopy);
                            Interlocked.Increment(ref _telemetrySeq);

                            var dispatcher = Application.Current?.Dispatcher;
                            if (dispatcher != null && !dispatcher.CheckAccess())
                            {
                                dispatcher.BeginInvoke(new Action(() =>
                                {
                                    TelemetryRxDataText = "0x" + FormatData(frameCopy);
                                    TemperatureValueText = temperature.ToString("0.####", CultureInfo.InvariantCulture);
                                }));
                            }
                            else
                            {
                                TelemetryRxDataText = "0x" + FormatData(frameCopy);
                                TemperatureValueText = temperature.ToString("0.####", CultureInfo.InvariantCulture);
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
                        await Task.Delay(50, token);
                    }
                }
                AddLog($"[{DateTime.Now:HH:mm:ss}] 温度遥测持续监听已停止");
            }, token);
        }

        private async Task StopTelemetryListeningAsync()
        {
            _telemetryListeningCts?.Cancel();
            if (_telemetryListeningTask != null)
            {
                try { await _telemetryListeningTask; } catch { }
                _telemetryListeningTask = null;
            }
            _telemetryListeningCts?.Dispose();
            _telemetryListeningCts = null;
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
