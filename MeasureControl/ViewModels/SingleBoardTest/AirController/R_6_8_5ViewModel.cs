using Prism.Commands;
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
using MeasureControl.Helpers;
using MeasureControl.Drivers;
using MeasureControl.Models.Devices;
using MeasureControl.Simulations.R_6_8_5;
using Prism.Ioc;

namespace MeasureControl.ViewModels.SingleBoardTest.AirController
{
    public class R_6_8_5ViewModel : BindableBase, IDisposable
    {
        private const byte DefaultLabel = 0x6A;

        private const string ResistorChannelId = "RO3";

        private static readonly byte[] AtpR = { 0x30, 0x01, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] AtpE = { 0x30, 0x02, 0x02, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] AbBtsTemperature = { 0x07, 0x01, 0x05, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] TemperatureTelemetryCommand = { 0x07, 0x01, 0x05, 0x02, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] TelemetryTemperaturePrefix = { 0x07, 0x01, 0x05, 0x02 };
        private static readonly byte[] TelemetryRawPrefix = { 0x07, 0x01, 0x05, 0x03 };

        public R_6_8_5ViewModel()
        {
            _enterAtpTxChannel = "429_CH5";
            _enterAtpRxChannel = "429_CH2";
            _controllerTemperatureTestTxChannel = "429_CH5";
            _temperatureTelemetryRxChannel = "429_CH2";
            _exitAtpTxChannel = "429_CH5";
            _exitAtpRxChannel = "429_CH2";

            _enterAtpRxDataText = "--";
            _temperatureTelemetryRxDataText = "--";
            _exitAtpRxDataText = "--";

            _resistorGear = "1挡";
            _ambientTemperatureSelection = "10~50℃";
            ResistorGearValueText = _resistorGear;
            MeasuredResistanceValueText = "--";
            TemperatureTelemetryValueText = "--";
            LastTestTime = "--";
            LastTestResult = "--";

            SendEnterAtpCommand = new DelegateCommand(async () => await OnSendEnterAtpAsync());
            SendSetControllerResistorCommand = new DelegateCommand(async () => await SendSetControllerResistorAsync(), () => !IsResistorMeasuring)
                .ObservesProperty(() => IsResistorMeasuring);
            TestControllerTemperatureCommand = new DelegateCommand(async () => await OnTestControllerTemperatureWithTelemetryAsync());
            SendExitAtpCommand = new DelegateCommand(async () => await OnSendExitAtpAsync());
            ClearLogCommand = new DelegateCommand(() => Logs.Clear());

            ManualTestCommand = new DelegateCommand(OnManualTest);
            AutoTestCommand = new DelegateCommand(OnAutoTest);
        }

        private ACTS6010Driver _resistorDriver;

        private readonly R_6_8_5Simulation _simulation = new R_6_8_5Simulation();
        private readonly SemaphoreSlim _arincOpLock = new SemaphoreSlim(1, 1);

        private readonly SemaphoreSlim _manualTestLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _autoTestLock = new SemaphoreSlim(1, 1);
        private CancellationTokenSource _autoTestCts;
        private bool _autoTestEnteredAtp;

        private CancellationTokenSource _telemetryListeningCts;
        private Task _telemetryListeningTask;

        private bool _enableTemperatureTelemetryListening;

        private byte[] _lastTemperatureTelemetryFrame;
        private byte[] _lastTemperatureRawFrame;

        private string _enterAtpTxChannel;
        private string _enterAtpRxChannel;
        private string _controllerTemperatureTestTxChannel;
        private string _temperatureTelemetryRxChannel;
        private string _exitAtpTxChannel;
        private string _exitAtpRxChannel;

        private string _enterAtpRxDataText;
        private string _temperatureTelemetryRxDataText;
        private string _exitAtpRxDataText;

        private string _resistorGear;
        private string _ambientTemperatureSelection;
        private string _resistorGearValueText;
        private string _measuredResistanceValueText;
        private string _temperatureTelemetryValueText;
        private string _lastTestTime;
        private string _lastTestResult;

        private bool _isManualTestRunning;
        private bool _isAutoTestRunning;
        private bool _isResistorMeasuring;

        private bool _suppressResultUpdates;
        private double? _lastTelemetryTemperatureC;
        private double? _gear1TemperatureC;
        private double? _gear2TemperatureC;
        private double? _gear3TemperatureC;

        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();

        public DelegateCommand ManualTestCommand { get; }
        public DelegateCommand AutoTestCommand { get; }

        public DelegateCommand SendEnterAtpCommand { get; }
        public DelegateCommand SendSetControllerResistorCommand { get; }
        public DelegateCommand TestControllerTemperatureCommand { get; }
        public DelegateCommand SendExitAtpCommand { get; }
        public DelegateCommand ClearLogCommand { get; }

        public bool IsManualTestRunning
        {
            get => _isManualTestRunning;
            set
            {
                if (SetProperty(ref _isManualTestRunning, value) && value)
                {
                    IsAutoTestRunning = false;
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
                    RaisePropertyChanged(nameof(CanEditStepControls));
                    RaisePropertyChanged(nameof(CanClickManualTestButton));
                    RaisePropertyChanged(nameof(CanClickAutoTestButton));

                    if (value)
                    {
                        IsManualTestRunning = false;
                    }
                }
            }
        }

        public bool CanEditStepControls => !IsAutoTestRunning;

        public bool CanClickManualTestButton => !IsAutoTestRunning;

        public bool CanClickAutoTestButton => !IsManualTestRunning;

        public double? LastTelemetryTemperatureC
        {
            get => _lastTelemetryTemperatureC;
            private set => SetProperty(ref _lastTelemetryTemperatureC, value);
        }

        public double? Gear1TemperatureC
        {
            get => _gear1TemperatureC;
            private set => SetProperty(ref _gear1TemperatureC, value);
        }

        public double? Gear2TemperatureC
        {
            get => _gear2TemperatureC;
            private set => SetProperty(ref _gear2TemperatureC, value);
        }

        public double? Gear3TemperatureC
        {
            get => _gear3TemperatureC;
            private set => SetProperty(ref _gear3TemperatureC, value);
        }

        public string EnterAtpTxChannel
        {
            get => _enterAtpTxChannel;
        }

        public string EnterAtpRxChannel
        {
            get => _enterAtpRxChannel;
        }

        public string EnterAtpRxDataText
        {
            get => _enterAtpRxDataText;
            private set => SetProperty(ref _enterAtpRxDataText, value);
        }

        public string ResistorGear
        {
            get => _resistorGear;
            set
            {
                if (SetProperty(ref _resistorGear, value))
                {
                    ResistorGearValueText = _resistorGear;
                }
            }
        }

        public string AmbientTemperatureSelection
        {
            get => _ambientTemperatureSelection;
            set => SetProperty(ref _ambientTemperatureSelection, value);
        }

        public string ResistorGearValueText
        {
            get => _resistorGearValueText;
            set => SetProperty(ref _resistorGearValueText, value);
        }

        public string MeasuredResistanceValueText
        {
            get => _measuredResistanceValueText;
            private set => SetProperty(ref _measuredResistanceValueText, value);
        }

        public bool IsResistorMeasuring
        {
            get => _isResistorMeasuring;
            private set => SetProperty(ref _isResistorMeasuring, value);
        }

        public string ControllerTemperatureTestTxChannel
        {
            get => _controllerTemperatureTestTxChannel;
        }

        public string TemperatureTelemetryRxChannel
        {
            get => _temperatureTelemetryRxChannel;
        }

        public string TemperatureTelemetryRxDataText
        {
            get => _temperatureTelemetryRxDataText;
            private set => SetProperty(ref _temperatureTelemetryRxDataText, value);
        }

        public string ExitAtpTxChannel
        {
            get => _exitAtpTxChannel;
        }

        public string ExitAtpRxChannel
        {
            get => _exitAtpRxChannel;
        }

        public string ExitAtpRxDataText
        {
            get => _exitAtpRxDataText;
            private set => SetProperty(ref _exitAtpRxDataText, value);
        }

        public string TemperatureTelemetryValueText
        {
            get => _temperatureTelemetryValueText;
            set => SetProperty(ref _temperatureTelemetryValueText, value);
        }

        public string LastTestTime
        {
            get => _lastTestTime;
            set => SetProperty(ref _lastTestTime, value);
        }

        public string LastTestResult
        {
            get => _lastTestResult;
            set => SetProperty(ref _lastTestResult, value);
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

            _ = StartAutoTestAsync();
        }

        private async Task StartManualTestAsync()
        {
            await _manualTestLock.WaitAsync();
            try
            {
                if (IsManualTestRunning)
                    return;

                IsManualTestRunning = true;
                LastTestTime = "--";
                LastTestResult = "--";
                AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试启动：开始打开设备");

                _simulation.IsRealProduct = true;
                _simulation.GetCurrentResistorGear = () => ResistorGear;
                _simulation.GetCurrentAmbientTemperatureSelection = () => AmbientTemperatureSelection;
                await _simulation.StartAsync(EnterAtpTxChannel, EnterAtpRxChannel, msg => AddLog(msg));

                AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试启动：429板卡已就绪");

                await StopTemperatureTelemetryListeningAsync();
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试启动异常：{ex.Message}");
                IsManualTestRunning = false;
                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
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

                AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试停止：关闭设备");
                IsManualTestRunning = false;

                await StopTemperatureTelemetryListeningAsync();

                await _simulation.StopAsync(msg => AddLog(msg));
                await StopResistorOutputAsync();

                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试停止异常：{ex.Message}");
                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            }
            finally
            {
                _manualTestLock.Release();
            }
        }

        private async Task StartAutoTestAsync()
        {
            await _autoTestLock.WaitAsync();
            try
            {
                if (IsAutoTestRunning)
                    return;

                await StopTemperatureTelemetryListeningAsync();

                IsAutoTestRunning = true;
                _suppressResultUpdates = true;
                _autoTestEnteredAtp = false;
                LastTestTime = "--";
                LastTestResult = "--";
                Gear1TemperatureC = null;
                Gear2TemperatureC = null;
                Gear3TemperatureC = null;
                LastTelemetryTemperatureC = null;

                _autoTestCts?.Dispose();
                _autoTestCts = new CancellationTokenSource();
                var token = _autoTestCts.Token;

                AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试启动");
                await RunAutoTestAsync(token);
            }
            finally
            {
                _autoTestLock.Release();
            }
        }

        private void StartTemperatureTelemetryListeningIfNeeded()
        {
            if (!_enableTemperatureTelemetryListening)
                return;
            if (_telemetryListeningTask != null)
                return;
            if (!IsManualTestRunning && !IsAutoTestRunning)
                return;
            if (string.IsNullOrWhiteSpace(TemperatureTelemetryRxChannel))
                return;

            _telemetryListeningCts?.Cancel();
            _telemetryListeningCts?.Dispose();
            _telemetryListeningCts = new CancellationTokenSource();
            var token = _telemetryListeningCts.Token;

            _telemetryListeningTask = Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        var (tempData, rawData) = await _simulation.WaitTelemetryAsync(
                            TemperatureTelemetryRxChannel,
                            timeoutMs: 300,
                            log: _ => { },
                            token: token);

                        if (tempData != null && TryParseTelemetryTemperature(tempData, out var temperature))
                        {
                            _lastTemperatureTelemetryFrame = tempData;
                            if (rawData != null)
                                _lastTemperatureRawFrame = rawData;

                            var dispatcher = Application.Current?.Dispatcher;
                            if (dispatcher != null && !dispatcher.CheckAccess())
                            {
                                dispatcher.BeginInvoke(new Action(() =>
                                {
                                    TemperatureTelemetryRxDataText = "0x" + FormatData(tempData);
                                    TemperatureTelemetryValueText = temperature.ToString("0.####", CultureInfo.InvariantCulture);
                                    LastTelemetryTemperatureC = temperature;

                                    if (string.Equals(ResistorGear, "1挡", StringComparison.Ordinal))
                                        Gear1TemperatureC = temperature;
                                    else if (string.Equals(ResistorGear, "2挡", StringComparison.Ordinal))
                                        Gear2TemperatureC = temperature;
                                    else if (string.Equals(ResistorGear, "3挡", StringComparison.Ordinal))
                                        Gear3TemperatureC = temperature;
                                }));
                            }
                            else
                            {
                                TemperatureTelemetryRxDataText = "0x" + FormatData(tempData);
                                TemperatureTelemetryValueText = temperature.ToString("0.####", CultureInfo.InvariantCulture);
                                LastTelemetryTemperatureC = temperature;

                                if (string.Equals(ResistorGear, "1挡", StringComparison.Ordinal))
                                    Gear1TemperatureC = temperature;
                                else if (string.Equals(ResistorGear, "2挡", StringComparison.Ordinal))
                                    Gear2TemperatureC = temperature;
                                else if (string.Equals(ResistorGear, "3挡", StringComparison.Ordinal))
                                    Gear3TemperatureC = temperature;
                            }
                        }
                        else
                        {
                            await Task.Delay(50, token);
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
            }, token);
        }

        private async Task StopTemperatureTelemetryListeningAsync()
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
                    var completed = await Task.WhenAny(task, Task.Delay(500));
                    if (!ReferenceEquals(completed, task) && !_suppressResultUpdates)
                        AddLog($"[{DateTime.Now:HH:mm:ss}] 警告：停止温度遥测监听超时(500ms)，后台可能仍在读取RX，可能发生抢帧");
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

        private async Task RunAutoTestAsync(CancellationToken token)
        {
            var failures = new List<string>();
            try
            {
                token.ThrowIfCancellationRequested();

                AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试：开始打开设备");
                _simulation.IsRealProduct = true;
                _simulation.GetCurrentResistorGear = () => ResistorGear;
                _simulation.GetCurrentAmbientTemperatureSelection = () => AmbientTemperatureSelection;
                await _simulation.StartAsync(EnterAtpTxChannel, EnterAtpRxChannel, msg => AddLog(msg));

                await StopTemperatureTelemetryListeningAsync();

                var enteredAtpOk = await AutoEnterAtpAsync(token);
                if (!enteredAtpOk)
                {
                    failures.Add("进入ATP失败");
                    return;
                }

                _autoTestEnteredAtp = true;

                await RunGearAsync("1挡", t => Gear1TemperatureC = t, token, failures);
                token.ThrowIfCancellationRequested();
                await RunGearAsync("2挡", t => Gear2TemperatureC = t, token, failures);
                token.ThrowIfCancellationRequested();
                await RunGearAsync("3挡", t => Gear3TemperatureC = t, token, failures);

                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                LastTestResult = failures.Count == 0 ? "三档电阻温度PASS" : "三档电阻温度不通过";

                AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试汇总：{LastTestResult}");
                AddLog($"[{DateTime.Now:HH:mm:ss}] 1挡温度={(Gear1TemperatureC?.ToString("F2", CultureInfo.InvariantCulture) ?? "--")}℃");
                AddLog($"[{DateTime.Now:HH:mm:ss}] 2挡温度={(Gear2TemperatureC?.ToString("F2", CultureInfo.InvariantCulture) ?? "--")}℃");
                AddLog($"[{DateTime.Now:HH:mm:ss}] 3挡温度={(Gear3TemperatureC?.ToString("F2", CultureInfo.InvariantCulture) ?? "--")}℃");
                foreach (var f in failures)
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 不合格：{f}");
            }
            catch (OperationCanceledException)
            {
                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                LastTestResult = "已停止";
                AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试已停止");
            }
            catch (Exception ex)
            {
                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                LastTestResult = "异常";
                AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试异常：{ex.Message}");
            }
            finally
            {
                try
                {
                    if (_autoTestEnteredAtp)
                        await AutoExitAtpAsync(token: CancellationToken.None);
                }
                catch { }

                try { await StopTemperatureTelemetryListeningAsync(); } catch { }

                try { await _simulation.StopAsync(msg => AddLog(msg)); } catch { }
                try { await StopResistorOutputAsync(); } catch { }

                _suppressResultUpdates = false;
                IsAutoTestRunning = false;
            }
        }

        private async Task RunGearAsync(string gear, Action<double?> setTemp, CancellationToken token, List<string> failures)
        {
            token.ThrowIfCancellationRequested();

            ResistorGear = gear;
            await SendSetControllerResistorAsync();
            token.ThrowIfCancellationRequested();

            AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试：{gear}接入电阻后等待温度稳定10s");
            await Task.Delay(TimeSpan.FromSeconds(10), token);
            token.ThrowIfCancellationRequested();

            await OnTestControllerTemperatureAsync();
            token.ThrowIfCancellationRequested();
            await OnTestTemperatureTelemetryAsync();

            var t = LastTelemetryTemperatureC;
            setTemp(t);
            if (t == null)
            {
                failures.Add($"{gear}温度回采失败");
                return;
            }

            var (min, max) = GetQualifiedTemperatureRangeForGear(gear, AmbientTemperatureSelection);
            if (t.Value < min || t.Value > max)
            {
                failures.Add($"{gear}回采温度={t.Value.ToString("F2", CultureInfo.InvariantCulture)}℃ 不在[{min.ToString("F2", CultureInfo.InvariantCulture)},{max.ToString("F2", CultureInfo.InvariantCulture)}]℃");
            }
        }

        private async Task<bool> AutoEnterAtpAsync(CancellationToken token)
        {
            AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试：发送进入ATP");

            await StopTemperatureTelemetryListeningAsync();

            try { await _simulation.ClearRxFifoAsync(EnterAtpRxChannel); } catch { }
            await Task.Delay(50, token);

            try
            {
                await _simulation.SendBenchCommandOnlyAsync(
                    EnterAtpTxChannel,
                    DefaultLabel,
                    AtpR,
                    msg => AddLog(msg),
                    token);

                var resp = await _simulation.WaitBenchResponseAsync(
                    EnterAtpRxChannel,
                    DefaultLabel,
                    isExpectedResponse: null,
                    timeoutMs: 600,
                    log: msg => AddLog(msg),
                    token: token);

                if (resp != null)
                {
                    EnterAtpRxDataText = "0x" + FormatData(resp);
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试：收到进入ATP回包(不校验)");
                }
                else
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试：进入ATP未收到回包(不校验)");
                }

                StartTemperatureTelemetryListeningIfNeeded();
                return true;
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试：进入ATP发送异常：{ex.Message}");
                return false;
            }
        }

        private async Task<bool> AutoExitAtpAsync(CancellationToken token)
        {
            AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试：发送退出ATP");

            await StopTemperatureTelemetryListeningAsync();

            try { await _simulation.ClearRxFifoAsync(ExitAtpRxChannel); } catch { }
            await Task.Delay(50, token);

            try
            {
                await _simulation.SendBenchCommandOnlyAsync(
                    ExitAtpTxChannel,
                    DefaultLabel,
                    AtpE,
                    msg => AddLog(msg),
                    token);

                var resp = await _simulation.WaitBenchResponseAsync(
                    ExitAtpRxChannel,
                    DefaultLabel,
                    isExpectedResponse: null,
                    timeoutMs: 600,
                    log: msg => AddLog(msg),
                    token: token);

                if (resp != null)
                {
                    ExitAtpRxDataText = "0x" + FormatData(resp);
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试：收到退出ATP回包(不校验)");
                }
                else
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试：退出ATP未收到回包(不校验)");
                }
                return true;
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试：退出ATP发送异常：{ex.Message}");
                return false;
            }
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
                    dispatcher.BeginInvoke(new Action(() => Logs.Add(message)));
                }
                else
                {
                    Logs.Add(message);
                }
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

        private async Task OnSendEnterAtpAsync()
        {
            await _arincOpLock.WaitAsync();
            try
            {
                EnterAtpRxDataText = "--";
                AddLog($"[{DateTime.Now:HH:mm:ss}] 发送：进入ATP，TX={EnterAtpTxChannel}, RX={EnterAtpRxChannel}, Label=0x{DefaultLabel:X2}");

                _simulation.GetCurrentResistorGear = () => ResistorGear;
                _simulation.GetCurrentAmbientTemperatureSelection = () => AmbientTemperatureSelection;
                await _simulation.StartAsync(EnterAtpTxChannel, EnterAtpRxChannel, msg => AddLog(msg));

                await StopTemperatureTelemetryListeningAsync();

                try { await _simulation.ClearRxFifoAsync(EnterAtpRxChannel); } catch { }
                await Task.Delay(50);

                await _simulation.SendBenchCommandOnlyAsync(
                    EnterAtpTxChannel,
                    DefaultLabel,
                    AtpR,
                    msg => AddLog(msg),
                    CancellationToken.None);

                AddLog($"[{DateTime.Now:HH:mm:ss}] 进入ATP指令已发送");

                var resp = await _simulation.WaitBenchResponseAsync(
                    EnterAtpRxChannel,
                    DefaultLabel,
                    isExpectedResponse: null,
                    timeoutMs: 600,
                    log: msg => AddLog(msg),
                    token: CancellationToken.None);

                if (resp != null)
                {
                    EnterAtpRxDataText = "0x" + FormatData(resp);
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 收到进入ATP回包(不校验)");
                }
                else
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 进入ATP未收到回包(不校验)");
                }

                StartTemperatureTelemetryListeningIfNeeded();
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 进入ATP异常：{ex.Message}");
            }
            finally
            {
                _arincOpLock.Release();
            }
        }

        private static (double Min, double Max) GetQualifiedTemperatureRangeForGear(string gear, string ambientSelection)
        {
            bool isNormalAmbient = string.Equals(ambientSelection, "10~50℃", StringComparison.OrdinalIgnoreCase);
            return gear switch
            {
                "1挡" => isNormalAmbient ? (-65.93, -64.07) : (-69.05, -60.95),
                "2挡" => isNormalAmbient ? (24.75, 26.61) : (21.63, 29.73),
                "3挡" => isNormalAmbient ? (134.06, 135.94) : (130.94, 139.06),
                _ => (double.NegativeInfinity, double.PositiveInfinity)
            };
        }

        private static string FormatGearForResult(string gear)
        {
            if (string.IsNullOrWhiteSpace(gear))
                return "第?档";

            return gear switch
            {
                "1挡" => "第1档",
                "2挡" => "第2档",
                "3挡" => "第3档",
                _ => "第" + gear
            };
        }

        private static bool IsTemperatureQualified(string gear, double temperature, string ambientSelection)
        {
            var (min, max) = GetQualifiedTemperatureRangeForGear(gear, ambientSelection);
            return temperature >= min && temperature <= max;
        }

        private async Task OnSendExitAtpAsync()
        {
            await _arincOpLock.WaitAsync();
            try
            {
                ExitAtpRxDataText = "--";
                AddLog($"[{DateTime.Now:HH:mm:ss}] 发送：退出ATP，TX={ExitAtpTxChannel}, RX={ExitAtpRxChannel}, Label=0x{DefaultLabel:X2}");

                await StopTemperatureTelemetryListeningAsync();

                try { await _simulation.ClearRxFifoAsync(ExitAtpRxChannel); } catch { }
                await Task.Delay(50);

                await _simulation.SendBenchCommandOnlyAsync(
                    ExitAtpTxChannel,
                    DefaultLabel,
                    AtpE,
                    msg => AddLog(msg),
                    CancellationToken.None);

                AddLog($"[{DateTime.Now:HH:mm:ss}] 退出ATP指令已发送");

                var resp = await _simulation.WaitBenchResponseAsync(
                    ExitAtpRxChannel,
                    DefaultLabel,
                    isExpectedResponse: null,
                    timeoutMs: 600,
                    log: msg => AddLog(msg),
                    token: CancellationToken.None);

                if (resp != null)
                {
                    ExitAtpRxDataText = "0x" + FormatData(resp);
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 收到退出ATP回包(不校验)");
                }
                else
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 退出ATP未收到回包(不校验)");
                }
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 退出ATP异常：{ex.Message}");
            }
            finally
            {
                await StopTemperatureTelemetryListeningAsync();
                _arincOpLock.Release();
            }
        }

        private async Task OnTestControllerTemperatureAsync()
        {
            await _arincOpLock.WaitAsync();
            try
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 测试：控制器温度，TX={ControllerTemperatureTestTxChannel}, Label=0x{DefaultLabel:X2}");

                await _simulation.SendBenchCommandOnlyAsync(
                    ControllerTemperatureTestTxChannel,
                    DefaultLabel,
                    AbBtsTemperature,
                    msg => AddLog(msg),
                    CancellationToken.None);
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

        private async Task OnTestControllerTemperatureWithTelemetryAsync()
        {
            await StopTemperatureTelemetryListeningAsync();
            await OnTestControllerTemperatureAsync();
            await OnTestTemperatureTelemetryAsync();
        }

        private async Task OnTestTemperatureTelemetryAsync()
        {
            await _arincOpLock.WaitAsync();
            try
            {
                var t0 = DateTime.UtcNow;
                TemperatureTelemetryValueText = "--";
                TemperatureTelemetryRxDataText = "--";
                LastTelemetryTemperatureC = null;
                _lastTemperatureTelemetryFrame = null;
                _lastTemperatureRawFrame = null;

                AddLog($"[{DateTime.Now:HH:mm:ss}] 测试：温度回采值开始，TX={ControllerTemperatureTestTxChannel}, RX={TemperatureTelemetryRxChannel}, Label=0x{DefaultLabel:X2}, Req={FormatData(TemperatureTelemetryCommand)}");

                await StopTemperatureTelemetryListeningAsync();

                AddLog($"[{DateTime.Now:HH:mm:ss}] 等待温度遥测(不发送回采请求，超时800ms)...");
                var (tempData, rawData) = await _simulation.WaitTelemetryAsync(
                    TemperatureTelemetryRxChannel,
                    timeoutMs: 800,
                    log: msg => AddLog(msg),
                    token: CancellationToken.None);

                if (tempData == null)
                {
                    try { await _simulation.ClearRxFifoAsync(TemperatureTelemetryRxChannel); } catch { }
                    await Task.Delay(20);

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 发送温度回采请求：{FormatData(TemperatureTelemetryCommand)}");
                    await _simulation.SendBenchCommandOnlyAsync(
                        ControllerTemperatureTestTxChannel,
                        DefaultLabel,
                        TemperatureTelemetryCommand,
                        msg => AddLog(msg),
                        CancellationToken.None);

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 已发送回采请求，等待温度遥测(超时2000ms)...");
                    (tempData, rawData) = await _simulation.WaitTelemetryAsync(
                        TemperatureTelemetryRxChannel,
                        timeoutMs: 2000,
                        log: msg => AddLog(msg),
                        token: CancellationToken.None);
                }

                double? temperature = null;
                if (tempData != null && TryParseTelemetryTemperature(tempData, out var tC))
                {
                    temperature = tC;
                    LastTelemetryTemperatureC = tC;
                    _lastTemperatureTelemetryFrame = tempData;
                    _lastTemperatureRawFrame = rawData;
                    TemperatureTelemetryRxDataText = "0x" + FormatData(tempData);
                }

                if (temperature == null)
                {
                    var elapsed = (int)Math.Max(0, (DateTime.UtcNow - t0).TotalMilliseconds);
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 温度回采值失败：{elapsed}ms内未收到温度采集值(07 01 07 02)");
                    if (!_suppressResultUpdates)
                    {
                        LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                        LastTestResult = $"{FormatGearForResult(ResistorGear)}电阻温度不通过";
                    }
                    return;
                }

                {
                    var elapsed = (int)Math.Max(0, (DateTime.UtcNow - t0).TotalMilliseconds);
                    var tempHex = tempData != null ? ("0x" + FormatData(tempData)) : "--";
                    var rawHex = rawData != null ? ("0x" + FormatData(rawData)) : "--";
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 温度回采值成功：{elapsed}ms，Temp={temperature.Value:0.####}℃，TempFrame={tempHex}，RawFrame={rawHex}");
                }

                TemperatureTelemetryValueText = temperature.Value.ToString("0.####", CultureInfo.InvariantCulture);

                if (string.Equals(ResistorGear, "1挡", StringComparison.Ordinal))
                    Gear1TemperatureC = temperature;
                else if (string.Equals(ResistorGear, "2挡", StringComparison.Ordinal))
                    Gear2TemperatureC = temperature;
                else if (string.Equals(ResistorGear, "3挡", StringComparison.Ordinal))
                    Gear3TemperatureC = temperature;

                if (!_suppressResultUpdates)
                {
                    LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    var qualified = IsTemperatureQualified(ResistorGear, temperature.Value, AmbientTemperatureSelection);
                    LastTestResult = qualified
                        ? $"{FormatGearForResult(ResistorGear)}电阻温度PASS"
                        : $"{FormatGearForResult(ResistorGear)}电阻温度不通过";
                }

                if (rawData != null)
                {
                    var rawHex = FormatData(rawData, rawData.Length);
                    if (TryParseBase6FromNibbles(rawData, out var rawBase6Decimal))
                        AddLog($"[{DateTime.Now:HH:mm:ss}] 传感器温度原始数据(07 01 07 03) 后四字节(6进制)->10进制：{rawBase6Decimal}，Data={rawHex}");
                    else
                        AddLog($"[{DateTime.Now:HH:mm:ss}] 传感器温度原始数据(07 01 07 03) Data={rawHex}");
                }
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 温度回采值异常：{ex.Message}");
            }
            finally
            {
                StartTemperatureTelemetryListeningIfNeeded();
                _arincOpLock.Release();
            }
        }

        private static bool IsPrefix(byte[] data, byte[] prefix)
        {
            if (data == null || prefix == null) return false;
            if (data.Length < prefix.Length) return false;
            for (int i = 0; i < prefix.Length; i++)
            {
                if (data[i] != prefix[i]) return false;
            }
            return true;
        }

        private static bool TryParseTelemetryTemperature(byte[] frameData, out double temperature)
        {
            temperature = 0;
            if (frameData == null || frameData.Length < 8)
                return false;

            if (!IsPrefix(frameData, TelemetryTemperaturePrefix))
                return false;

            var raw = (frameData[4] << 24) | (frameData[5] << 16) | (frameData[6] << 8) | frameData[7];
            temperature = raw * 0.01;
            return true;
        }

        private static bool TryParseBase6FromNibbles(byte[] frameData, out long value)
        {
            value = 0;
            if (frameData == null || frameData.Length < 8)
                return false;
            if (!IsPrefix(frameData, TelemetryRawPrefix))
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

        private static string FormatData(byte[] data, int length = -1)
        {
            if (data == null)
                return string.Empty;
            var len = length;
            if (len < 0)
                len = data.Length;
            len = Math.Min(len, data.Length);
            if (len <= 0)
                return string.Empty;
            return string.Join(" ", data.Take(len).Select(b => b.ToString("X2")));
        }

        private static double GetTargetResistanceOhm(string gear)
        {
            return gear switch
            {
                "1挡" => 371.65,
                "2挡" => 550.0,
                "3挡" => 758.55,
                _ => 371.65
            };
        }

        private async Task<bool> EnsureResistorReadyAsync(bool enableLog = true)
        {
            if (_resistorDriver != null && _resistorDriver.IsConnected)
                return true;

            try
            {
                var candidates = new uint[] { 1, 0, 2, 3, 4, 5, 6, 7 };
                int foundCount = 0;
                const int targetBoardIndex = 1; // 使用第2块电阻板卡

                foreach (var logicalId in candidates)
                {
                    if (enableLog)
                        AddLog($"[{DateTime.Now:HH:mm:ss}] 电阻板卡直连：尝试ACTS6010逻辑ID={logicalId}");
                    var dummy = new ProgrammableResistorDevice
                    {
                        Name = "电阻输出",
                        Model = "PXI-7012",
                        CardName = $"电阻输出(自动探测-{logicalId})",
                        SlotIndex = (int)logicalId
                    };

                    var driver = new ACTS6010Driver(dummy, logicalId);
                    var ok = await driver.ConnectAsync();
                    if (ok)
                    {
                        foundCount++;
                        if (foundCount == targetBoardIndex)
                        {
                            _resistorDriver = driver;
                            if (enableLog)
                                AddLog($"[{DateTime.Now:HH:mm:ss}] 电阻板卡已连接：ACTS6010 逻辑ID={logicalId}（第{foundCount}块板卡）");
                            return true;
                        }
                        else
                        {
                            if (enableLog)
                                AddLog($"[{DateTime.Now:HH:mm:ss}] 跳过第{foundCount}块板卡（逻辑ID={logicalId}），需要第{targetBoardIndex}块");
                            await driver.DisconnectAsync();
                        }
                    }
                }

                if (enableLog)
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 电阻板卡打开失败：未找到第{targetBoardIndex}块ACTS6010板卡");
                return false;
            }
            catch (Exception ex)
            {
                if (enableLog)
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 电阻板卡打开异常：{ex.Message}");
                _resistorDriver = null;
                return false;
            }
        }

        private async Task StopResistorOutputAsync()
        {
            try
            {
                var ready = await EnsureResistorReadyAsync(enableLog: false);
                if (!ready || _resistorDriver == null || !_resistorDriver.IsConnected)
                    return;

                await _resistorDriver.SetRelayStateAsync(ResistorChannelId, pathRelayClosed: false, shortCircuitClosed: false);
            }
            catch
            {
            }
            finally
            {
                try { await DisconnectResistorAsync(); } catch { }
            }
        }

        private async Task DisconnectResistorAsync()
        {
            try
            {
                if (_resistorDriver != null)
                {
                    await _resistorDriver.DisconnectAsync();
                }
            }
            catch
            {
            }
            finally
            {
                _resistorDriver = null;
            }
        }

        private async Task SendSetControllerResistorAsync()
        {
            if (IsResistorMeasuring)
            {
                return;
            }

            IsResistorMeasuring = true;
            MeasuredResistanceValueText = "--";

            try
            {
                bool resistorReady = await EnsureResistorReadyAsync();
                if (!resistorReady || _resistorDriver == null || !_resistorDriver.IsConnected)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 接入电阻失败：电阻板卡未就绪");
                    return;
                }

                var targetOhm = GetTargetResistanceOhm(ResistorGear);
                AddLog($"[{DateTime.Now:HH:mm:ss}] 发送：接入电阻，档位={ResistorGear}，目标={targetOhm.ToString("F2", CultureInfo.InvariantCulture)}Ω");

                var relayOk = await _resistorDriver.SetRelayStateAsync(ResistorChannelId, true, false);
                if (!relayOk)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 7012设置{ResistorChannelId}继电器失败(通路闭合/短路断开)");
                    return;
                }

                var writeOk = await _resistorDriver.WriteChannelAsync(ResistorChannelId, targetOhm);
                if (!writeOk)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 7012写入{ResistorChannelId}失败");
                    return;
                }

                await Task.Delay(50);

                var readBack = await _resistorDriver.ReadChannelAsync(ResistorChannelId);
                MeasuredResistanceValueText = $"{readBack.ToString("F5", CultureInfo.InvariantCulture)}Ω";
                AddLog($"[{DateTime.Now:HH:mm:ss}] 电阻板卡读回电阻：{MeasuredResistanceValueText}");
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 接入电阻异常：{ex.Message}");
            }
            finally
            {
                IsResistorMeasuring = false;
            }
        }

        public void Dispose()
        {
            try
            {
                _autoTestCts?.Cancel();
                _simulation?.Dispose();
                StopResistorOutputAsync().GetAwaiter().GetResult();
            }
            catch { }
        }
    }
}
