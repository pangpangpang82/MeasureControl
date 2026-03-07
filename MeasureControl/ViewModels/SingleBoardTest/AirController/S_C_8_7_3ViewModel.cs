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
using MeasureControl.Simulations.S_C_8_7_3;

namespace MeasureControl.ViewModels.SingleBoardTest.AirController
{
    public sealed class S_C_8_7_3ViewModel : BindableBase, IDisposable
    {
        private static readonly byte[] EnterAtpCommand8 = { 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] EnterAtpOk8 = { 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x02 };
        private static readonly byte[] ExitAtpCommand8 = { 0x00, 0x02, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01 };
        private static readonly byte[] ExitAtpOk8 = { 0x00, 0x02, 0x00, 0x01, 0x00, 0x00, 0x00, 0x03 };

        private static readonly byte[] SAftAventsMea038 = { 0x15, 0x02, 0x03, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] TelemetryPrefix4 = { 0x15, 0x02, 0x03, 0x02 };

        private const string AoChannel = "AO4";

        private readonly S_C_8_7_3Simulation _simulation = new S_C_8_7_3Simulation();
        private readonly SemaphoreSlim _arincOpLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _manualTestLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _autoTestLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _mtxOpLock = new SemaphoreSlim(1, 1);

        private CancellationTokenSource _autoTestCts;
        private IMtx532Api _mtxApi;

        private string _testTxChannel = "CH0";
        private string _testRxChannel = "CH1";
        private string _enterAtpTxChannel = "CH0";
        private string _enterAtpRxChannel = "CH1";
        private string _exitAtpTxChannel = "CH0";
        private string _exitAtpRxChannel = "CH1";
        private string _testCommandTxChannel = "CH0";
        private string _testCommandRxChannel = "CH1";
        private string _telemetryRxChannel = "CH1";

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
        private int _currentGearIndex = 1;

        public S_C_8_7_3ViewModel()
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

        public string TestTxChannel { get => _testTxChannel; set => SetProperty(ref _testTxChannel, value); }
        public string TestRxChannel { get => _testRxChannel; set => SetProperty(ref _testRxChannel, value); }
        public string EnterAtpTxChannel { get => _enterAtpTxChannel; set => SetProperty(ref _enterAtpTxChannel, value); }
        public string EnterAtpRxChannel { get => _enterAtpRxChannel; set => SetProperty(ref _enterAtpRxChannel, value); }
        public string ExitAtpTxChannel { get => _exitAtpTxChannel; set => SetProperty(ref _exitAtpTxChannel, value); }
        public string ExitAtpRxChannel { get => _exitAtpRxChannel; set => SetProperty(ref _exitAtpRxChannel, value); }
        public string TestCommandTxChannel { get => _testCommandTxChannel; set => SetProperty(ref _testCommandTxChannel, value); }
        public string TestCommandRxChannel { get => _testCommandRxChannel; set => SetProperty(ref _testCommandRxChannel, value); }
        public string TelemetryRxChannel { get => _telemetryRxChannel; set => SetProperty(ref _telemetryRxChannel, value); }

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
            set
            {
                TestCommandTxChannel = value;
                RaisePropertyChanged(nameof(ControllerPressureTestTxChannel));
            }
        }

        public string ControllerPressureTestRxChannel
        {
            get => TestCommandRxChannel;
            set
            {
                TestCommandRxChannel = value;
                RaisePropertyChanged(nameof(ControllerPressureTestRxChannel));
            }
        }

        public string ControllerPressureTestRxDataText => TestCommandRxDataText;

        public string PressureTelemetryRxChannel
        {
            get => TelemetryRxChannel;
            set
            {
                TelemetryRxChannel = value;
                RaisePropertyChanged(nameof(PressureTelemetryRxChannel));
            }
        }

        public string PressureTelemetryValueText => TemperatureValueText;

        public string PressureTelemetryRxDataText => TelemetryRxDataText;

        public string EnterAtpRxDataText { get => _enterAtpRxDataText; private set => SetProperty(ref _enterAtpRxDataText, value); }

        public string TestCommandRxDataText
        {
            get => _testCommandRxDataText;
            private set
            {
                if (SetProperty(ref _testCommandRxDataText, value))
                {
                    RaisePropertyChanged(nameof(ControllerPressureTestRxDataText));
                }
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
            await _manualTestLock.WaitAsync();
            try
            {
                if (IsBusy)
                    return;

                IsBusy = true;
                try
                {
                    SyncChannels();
                    ResetDisplayState();

                    _simulation.IsRealProduct = false;
                    _simulation.ArincRate = 100000.0;
                    _simulation.SimProductArincRate = 100000.0;
                    _simulation.SimProductRxChannelIndex = 4;
                    _simulation.SimProductTxChannelIndex = 5;

                    await _simulation.StartAsync(TestTxChannel, TestRxChannel, msg => AddLog(msg));
                    IsManualTestRunning = true;
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
                if (IsBusy)
                    return;

                IsBusy = true;
                try
                {
                    await _simulation.StopAsync(msg => AddLog(msg));
                    IsManualTestRunning = false;
                    IsInAtp = false;
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试已停止");
                }
                catch (Exception ex)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试停止异常: {ex.Message}");
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

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 发送进入ATP：{FormatData(EnterAtpCommand8)}");
                    await _simulation.SendBenchCommandOnlyAsync(EnterAtpTxChannel, EnterAtpCommand8, msg => AddLog(msg), CancellationToken.None);

                    var resp = await _simulation.WaitBenchResponse8Async(
                        EnterAtpRxChannel,
                        b => b != null && b.SequenceEqual(EnterAtpOk8),
                        timeoutMs: 1200,
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

                    var voltageV = GetGearVoltage(gearIndex);
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 档位{gearIndex}：设置AO4={voltageV.ToString("0.###", CultureInfo.InvariantCulture)}V");

                    var okVoltage = await OutputVoltageAsync(voltageV, CancellationToken.None);
                    if (!okVoltage)
                    {
                        SetLastTestResult("FAIL");
                        AddLog($"[{DateTime.Now:HH:mm:ss}] AO4输出失败");
                        return;
                    }

                    await Task.Delay(50);
                    await _simulation.ClearRxFifoAsync(TestCommandRxChannel);
                    await Task.Delay(20);

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 发送S_AFTAVENTS_MEA：{FormatData(SAftAventsMea038)}");
                    await _simulation.SendBenchCommandOnlyAsync(TestCommandTxChannel, SAftAventsMea038, msg => AddLog(msg), CancellationToken.None);

                    var confirm = await _simulation.WaitBenchResponse8Async(
                        TestCommandRxChannel,
                        b => b != null && b.SequenceEqual(SAftAventsMea038),
                        timeoutMs: 1200,
                        log: msg => AddLog(msg),
                        token: CancellationToken.None);

                    if (confirm == null)
                    {
                        SetLastTestResult("FAIL");
                        AddLog($"[{DateTime.Now:HH:mm:ss}] 指令确认帧超时");
                        return;
                    }

                    TestCommandRxDataText = "0x" + FormatData(confirm);

                    var tel = await _simulation.WaitTelemetryAsync(TelemetryRxChannel, timeoutMs: 1500, log: msg => AddLog(msg), token: CancellationToken.None);
                    if (tel == null)
                    {
                        SetLastTestResult("FAIL");
                        AddLog($"[{DateTime.Now:HH:mm:ss}] 温度遥测超时");
                        return;
                    }

                    TelemetryRxDataText = "0x" + FormatData(tel);
                    if (!TryParseTelemetryTemperature(tel, out var temperature))
                    {
                        SetLastTestResult("FAIL");
                        AddLog($"[{DateTime.Now:HH:mm:ss}] 温度遥测解析失败");
                        return;
                    }

                    TemperatureValueText = temperature.ToString("0.####", CultureInfo.InvariantCulture);
                    var pass = IsTemperatureQualified(gearIndex, temperature);
                    SetLastTestResult(pass ? "PASS" : "FAIL");
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
            if (!IsManualTestRunning || IsBusy)
                return;

            var gearIndex = CurrentGearIndex;
            if (gearIndex is < 1 or > 3)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 请先选择接入电压挡位");
                return;
            }

            try
            {
                IsBusy = true;

                var voltageV = GetGearVoltage(gearIndex);
                AddLog($"[{DateTime.Now:HH:mm:ss}] 设置档位{gearIndex}：AO4={voltageV.ToString("0.###", CultureInfo.InvariantCulture)}V");

                var ok = await OutputVoltageAsync(voltageV, CancellationToken.None);
                if (!ok)
                {
                    SetLastTestResult("FAIL");
                    AddLog($"[{DateTime.Now:HH:mm:ss}] AO4输出失败");
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
                    await _simulation.ClearRxFifoAsync(ControllerPressureTestRxChannel);
                    await Task.Delay(20);

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 发送S_AFTAVENTS_MEA：TX={ControllerPressureTestTxChannel}, RX={ControllerPressureTestRxChannel}");
                    await _simulation.SendBenchCommandOnlyAsync(ControllerPressureTestTxChannel, SAftAventsMea038, msg => AddLog(msg), CancellationToken.None);

                    var confirm = await _simulation.WaitBenchResponse8Async(
                        ControllerPressureTestRxChannel,
                        b => b != null && b.SequenceEqual(SAftAventsMea038),
                        timeoutMs: 1200,
                        log: msg => AddLog(msg),
                        token: CancellationToken.None);

                    if (confirm == null)
                    {
                        SetLastTestResult("FAIL");
                        AddLog($"[{DateTime.Now:HH:mm:ss}] 指令确认帧超时");
                        return;
                    }

                    TestCommandRxDataText = "0x" + FormatData(confirm);

                    var tel = await _simulation.WaitTelemetryAsync(PressureTelemetryRxChannel, timeoutMs: 1500, log: msg => AddLog(msg), token: CancellationToken.None);
                    if (tel == null)
                    {
                        SetLastTestResult("FAIL");
                        AddLog($"[{DateTime.Now:HH:mm:ss}] 温度遥测超时");
                        return;
                    }

                    TelemetryRxDataText = "0x" + FormatData(tel);
                    if (!TryParseTelemetryTemperature(tel, out var temperature))
                    {
                        SetLastTestResult("FAIL");
                        AddLog($"[{DateTime.Now:HH:mm:ss}] 温度遥测解析失败");
                        return;
                    }

                    TemperatureValueText = temperature.ToString("0.####", CultureInfo.InvariantCulture);
                    var pass = IsTemperatureQualified(CurrentGearIndex <= 0 ? 1 : CurrentGearIndex, temperature);
                    SetLastTestResult(pass ? "PASS" : "FAIL");
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 测试结果：{LastTestResult}");
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
                    await _simulation.ClearRxFifoAsync(ExitAtpRxChannel);
                    await Task.Delay(20);

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 发送退出ATP：{FormatData(ExitAtpCommand8)}");
                    await _simulation.SendBenchCommandOnlyAsync(ExitAtpTxChannel, ExitAtpCommand8, msg => AddLog(msg), CancellationToken.None);

                    var resp = await _simulation.WaitBenchResponse8Async(
                        ExitAtpRxChannel,
                        b => b != null && b.SequenceEqual(ExitAtpOk8),
                        timeoutMs: 1200,
                        log: msg => AddLog(msg),
                        token: CancellationToken.None);

                    if (resp == null)
                    {
                        SetLastTestResult("FAIL");
                        AddLog($"[{DateTime.Now:HH:mm:ss}] 退出ATP超时");
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
                    SyncChannels();
                    ResetDisplayState();

                    _autoTestCts?.Cancel();
                    _autoTestCts?.Dispose();
                    _autoTestCts = new CancellationTokenSource();
                    var token = _autoTestCts.Token;

                    _simulation.IsRealProduct = false;
                    _simulation.ArincRate = 100000.0;
                    _simulation.SimProductArincRate = 100000.0;
                    _simulation.SimProductRxChannelIndex = 4;
                    _simulation.SimProductTxChannelIndex = 5;

                    AddLog($"[{DateTime.Now:HH:mm:ss}] ========== 自动测试开始 ==========");
                    await _simulation.StartAsync(TestTxChannel, TestRxChannel, msg => AddLog(msg));

                    await _simulation.ClearRxFifoAsync(TestRxChannel);
                    await Task.Delay(20, token);
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
                        AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试失败：进入ATP超时");
                        return;
                    }

                    EnterAtpRxDataText = "0x" + FormatData(enterOk);
                    IsInAtp = true;

                    var failures = new List<string>();
                    for (int gear = 1; gear <= 3; gear++)
                    {
                        token.ThrowIfCancellationRequested();
                        await RunAutoGearAsync(gear, token, failures);
                    }

                    await _simulation.ClearRxFifoAsync(TestRxChannel);
                    await Task.Delay(20, token);
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
                    else
                    {
                        ExitAtpRxDataText = "0x" + FormatData(exitOk);
                    }

                    IsInAtp = false;
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
                    try { await _simulation.StopAsync(msg => AddLog(msg)); } catch { }
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
                _autoTestCts?.Cancel();
            }
            finally
            {
                _autoTestLock.Release();
            }
        }

        private async Task RunAutoGearAsync(int gearIndex, CancellationToken token, List<string> failures)
        {
            CurrentGearIndex = gearIndex;
            var voltageV = GetGearVoltage(gearIndex);
            var okVoltage = await OutputVoltageAsync(voltageV, token);
            if (!okVoltage)
            {
                failures.Add($"档位{gearIndex} AO4输出失败");
                return;
            }

            await Task.Delay(50, token);
            await _simulation.ClearRxFifoAsync(TestRxChannel);
            await Task.Delay(20, token);
            await _simulation.SendBenchCommandOnlyAsync(TestTxChannel, SAftAventsMea038, msg => AddLog(msg), token);

            var confirm = await _simulation.WaitBenchResponse8Async(
                TestRxChannel,
                b => b != null && b.SequenceEqual(SAftAventsMea038),
                timeoutMs: 1200,
                log: msg => AddLog(msg),
                token: token);

            if (confirm == null)
            {
                failures.Add($"档位{gearIndex} 指令确认帧超时");
                return;
            }

            var tel = await _simulation.WaitTelemetryAsync(TestRxChannel, timeoutMs: 1500, log: msg => AddLog(msg), token: token);
            if (tel == null)
            {
                failures.Add($"档位{gearIndex} 温度遥测超时");
                return;
            }

            TelemetryRxDataText = "0x" + FormatData(tel);
            if (!TryParseTelemetryTemperature(tel, out var temperature))
            {
                failures.Add($"档位{gearIndex} 温度遥测解析失败");
                return;
            }

            TemperatureValueText = temperature.ToString("0.####", CultureInfo.InvariantCulture);
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

                List<DeviceBase> devices = null;
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

                return FlattenDevices(devices).FirstOrDefault(IsMtx532Device);
            }
            catch
            {
                return null;
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

            if (device is not PxiDeviceBase && !string.Equals(device.DeviceType, "Card", StringComparison.OrdinalIgnoreCase))
                return false;

            var model = (device.Model ?? string.Empty).ToUpperInvariant();
            return model.Contains("MT-X532") || model.Contains("MTX532") || model.Contains("X532");
        }

        private static bool TryParseTelemetryTemperature(byte[] frameData, out double temperature)
        {
            temperature = 0;
            if (frameData == null || frameData.Length < 8)
                return false;
            if (!IsPrefix(frameData, TelemetryPrefix4))
                return false;

            var intPartRaw = (ushort)((frameData[4] << 8) | frameData[5]);
            var fracPart = (ushort)((frameData[6] << 8) | frameData[7]);
            var signedInt = unchecked((short)intPartRaw);
            var frac = fracPart / 10000.0;
            temperature = signedInt < 0 ? signedInt - frac : signedInt + frac;
            return true;
        }

        private static bool IsTemperatureQualified(int gearIndex, double temperature)
        {
            var (min, max) = gearIndex switch
            {
                1 => (-65.98, -64.02),
                2 => (25.12, 28.88),
                3 => (134.02, 137.98),
                _ => (-65.98, -64.02)
            };
            return temperature >= min && temperature <= max;
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
            EnterAtpTxChannel = TestTxChannel;
            EnterAtpRxChannel = TestRxChannel;
            ExitAtpTxChannel = TestTxChannel;
            ExitAtpRxChannel = TestRxChannel;
            TestCommandTxChannel = TestTxChannel;
            TestCommandRxChannel = TestRxChannel;
            TelemetryRxChannel = TestRxChannel;
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
