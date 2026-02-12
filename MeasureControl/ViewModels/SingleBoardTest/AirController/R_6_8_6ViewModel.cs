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
using MeasureControl.Services;
using MeasureControl.Simulations.R_6_8_6;
using NationalInstruments.Visa;
using Prism.Ioc;

namespace MeasureControl.ViewModels.SingleBoardTest.AirController
{
    public class R_6_8_6ViewModel : BindableBase, IDisposable
    {
        private const byte DefaultLabel = 0x6A;

        private static readonly byte[] AtpR = { 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] AtpEnterOk = { 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x02 };
        private static readonly byte[] AtpE = { 0x00, 0x02, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01 };
        private static readonly byte[] ExitOk = { 0x00, 0x02, 0x00, 0x01, 0x00, 0x00, 0x00, 0x03 };
        private static readonly byte[] AbCarDtsTemperature = { 0x07, 0x01, 0x06, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] TelemetryTemperaturePrefix = { 0x07, 0x01, 0x06, 0x02 };
        private static readonly byte[] TelemetryRawPrefix = { 0x07, 0x01, 0x06, 0x03 };

        public R_6_8_6ViewModel()
        {
            _enterAtpTxChannel = "429_CH0";
            _enterAtpRxChannel = "429_CH1";
            _controllerTemperatureTestTxChannel = "429_CH0";
            _controllerTemperatureTestRxChannel = "429_CH1";
            _temperatureTelemetryRxChannel = "429_CH1";
            _exitAtpTxChannel = "429_CH0";
            _exitAtpRxChannel = "429_CH1";

            _enterAtpRxDataText = "--";
            _controllerTemperatureTestRxDataText = "--";
            _temperatureTelemetryRxDataText = "--";
            _exitAtpRxDataText = "--";

            _resistorGear = "1挡";
            ResistorGearValueText = _resistorGear;
            MeasuredResistanceValueText = "--";
            TemperatureTelemetryValueText = "--";
            LastTestTime = "--";
            LastTestResult = "--";

            SendEnterAtpCommand = new DelegateCommand(async () => await OnSendEnterAtpAsync());
            SendSetControllerResistorCommand = new DelegateCommand(async () => await SendSetControllerResistorAsync(), () => !IsResistorMeasuring)
                .ObservesProperty(() => IsResistorMeasuring);
            TestControllerTemperatureCommand = new DelegateCommand(async () => await OnTestControllerTemperatureAsync());
            TestTemperatureTelemetryCommand = new DelegateCommand(async () => await OnTestTemperatureTelemetryAsync());
            SendExitAtpCommand = new DelegateCommand(async () => await OnSendExitAtpAsync());
            ClearLogCommand = new DelegateCommand(() => Logs.Clear());

            ManualTestCommand = new DelegateCommand(OnManualTest);
            AutoTestCommand = new DelegateCommand(OnAutoTest);
        }

        private ACTS6010Driver _resistorDriver;

        private readonly R_6_8_6Simulation _simulation = new R_6_8_6Simulation();
        private readonly SemaphoreSlim _arincOpLock = new SemaphoreSlim(1, 1);

        private readonly SemaphoreSlim _manualTestLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _autoTestLock = new SemaphoreSlim(1, 1);
        private CancellationTokenSource _autoTestCts;
        private bool _autoTestEnteredAtp;

        private string _enterAtpTxChannel;
        private string _enterAtpRxChannel;
        private string _controllerTemperatureTestTxChannel;
        private string _controllerTemperatureTestRxChannel;
        private string _temperatureTelemetryRxChannel;
        private string _exitAtpTxChannel;
        private string _exitAtpRxChannel;

        private string _enterAtpRxDataText;
        private string _controllerTemperatureTestRxDataText;
        private string _temperatureTelemetryRxDataText;
        private string _exitAtpRxDataText;

        private string _resistorGear;
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
        public DelegateCommand TestTemperatureTelemetryCommand { get; }
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
            set => SetProperty(ref _enterAtpTxChannel, value);
        }

        public string EnterAtpRxChannel
        {
            get => _enterAtpRxChannel;
            set => SetProperty(ref _enterAtpRxChannel, value);
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
            set => SetProperty(ref _controllerTemperatureTestTxChannel, value);
        }

        public string ControllerTemperatureTestRxChannel
        {
            get => _controllerTemperatureTestRxChannel;
            set => SetProperty(ref _controllerTemperatureTestRxChannel, value);
        }

        public string ControllerTemperatureTestRxDataText
        {
            get => _controllerTemperatureTestRxDataText;
            private set => SetProperty(ref _controllerTemperatureTestRxDataText, value);
        }

        public string TemperatureTelemetryRxChannel
        {
            get => _temperatureTelemetryRxChannel;
            set => SetProperty(ref _temperatureTelemetryRxChannel, value);
        }

        public string TemperatureTelemetryRxDataText
        {
            get => _temperatureTelemetryRxDataText;
            private set => SetProperty(ref _temperatureTelemetryRxDataText, value);
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

                _simulation.IsRealProduct = AppConstants.Arinc429IsRealProduct;
                _simulation.GetCurrentResistorGear = () => ResistorGear;
                await _simulation.StartAsync(EnterAtpTxChannel, EnterAtpRxChannel, msg => AddLog(msg));

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

                await _simulation.StopAsync(msg => AddLog(msg));
                await DisconnectResistorAsync();

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

        private async Task RunAutoTestAsync(CancellationToken token)
        {
            var failures = new List<string>();
            try
            {
                token.ThrowIfCancellationRequested();

                AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试：开始打开设备");
                _simulation.IsRealProduct = AppConstants.Arinc429IsRealProduct;
                _simulation.GetCurrentResistorGear = () => ResistorGear;
                await _simulation.StartAsync(EnterAtpTxChannel, EnterAtpRxChannel, msg => AddLog(msg));

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

                try { await _simulation.StopAsync(msg => AddLog(msg)); } catch { }
                try { await DisconnectResistorAsync(); } catch { }

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

            var (min, max) = GetQualifiedTemperatureRangeForGear(gear);
            if (t.Value < min || t.Value > max)
            {
                failures.Add($"{gear}回采温度={t.Value.ToString("F2", CultureInfo.InvariantCulture)}℃ 不在[{min.ToString("F2", CultureInfo.InvariantCulture)},{max.ToString("F2", CultureInfo.InvariantCulture)}]℃");
            }
        }

        private async Task<bool> AutoEnterAtpAsync(CancellationToken token)
        {
            AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试：发送进入ATP");

            try { await _simulation.ClearRxFifoAsync(EnterAtpRxChannel); } catch { }
            await Task.Delay(50, token);

            var resp = await _simulation.SendBenchCommandAndWaitAsync(
                EnterAtpTxChannel, EnterAtpRxChannel,
                DefaultLabel, AtpR,
                b => b.SequenceEqual(AtpEnterOk),
                timeoutMs: 3000,
                msg => AddLog(msg), token);

            if (resp != null)
            {
                EnterAtpRxDataText = "0x" + FormatData(resp);
                return true;
            }
            return false;
        }

        private async Task<bool> AutoExitAtpAsync(CancellationToken token)
        {
            AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试：发送退出ATP");

            try { await _simulation.ClearRxFifoAsync(ExitAtpRxChannel); } catch { }
            await Task.Delay(50, token);

            var resp = await _simulation.SendBenchCommandAndWaitAsync(
                ExitAtpTxChannel, ExitAtpRxChannel,
                DefaultLabel, AtpE,
                b => b.SequenceEqual(ExitOk),
                timeoutMs: 2000,
                msg => AddLog(msg), token);

            if (resp != null)
            {
                ExitAtpRxDataText = "0x" + FormatData(resp);
                return true;
            }
            return false;
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
                await _simulation.StartAsync(EnterAtpTxChannel, EnterAtpRxChannel, msg => AddLog(msg));

                try { await _simulation.ClearRxFifoAsync(EnterAtpRxChannel); } catch { }
                await Task.Delay(50);

                var resp = await _simulation.SendBenchCommandAndWaitAsync(
                    EnterAtpTxChannel, EnterAtpRxChannel,
                    DefaultLabel, AtpR,
                    b => b.SequenceEqual(AtpEnterOk),
                    timeoutMs: 3000,
                    msg => AddLog(msg), CancellationToken.None);

                if (resp == null)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 进入ATP超时，未收到OK");
                    return;
                }

                EnterAtpRxDataText = "0x" + FormatData(resp);
                AddLog($"[{DateTime.Now:HH:mm:ss}] 收到ATP OK");
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

        private static (double Min, double Max) GetQualifiedTemperatureRangeForGear(string gear)
        {
            return gear switch
            {
                "1挡" => (-65.93, -64.07),
                "2挡" => (24.75, 26.61),
                "3挡" => (134.06, 135.94),
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

        private static bool IsTemperatureQualified(string gear, double temperature)
        {
            var (min, max) = GetQualifiedTemperatureRangeForGear(gear);
            return temperature >= min && temperature <= max;
        }

        private async Task OnSendExitAtpAsync()
        {
            await _arincOpLock.WaitAsync();
            try
            {
                ExitAtpRxDataText = "--";
                AddLog($"[{DateTime.Now:HH:mm:ss}] 发送：退出ATP，TX={ExitAtpTxChannel}, RX={ExitAtpRxChannel}, Label=0x{DefaultLabel:X2}");

                try { await _simulation.ClearRxFifoAsync(ExitAtpRxChannel); } catch { }
                await Task.Delay(50);

                var resp = await _simulation.SendBenchCommandAndWaitAsync(
                    ExitAtpTxChannel, ExitAtpRxChannel,
                    DefaultLabel, AtpE,
                    b => b.SequenceEqual(ExitOk),
                    timeoutMs: 2000,
                    msg => AddLog(msg), CancellationToken.None);

                if (resp == null)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 退出ATP超时，未收到OK");
                    return;
                }

                ExitAtpRxDataText = "0x" + FormatData(resp);
                AddLog($"[{DateTime.Now:HH:mm:ss}] 收到退出ATP OK");
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 退出ATP异常：{ex.Message}");
            }
            finally
            {
                _arincOpLock.Release();
            }
        }

        private async Task OnTestControllerTemperatureAsync()
        {
            await _arincOpLock.WaitAsync();
            try
            {
                ControllerTemperatureTestRxDataText = "--";
                AddLog($"[{DateTime.Now:HH:mm:ss}] 测试：AB_CAR_DTS_Temperature，TX={ControllerTemperatureTestTxChannel}, RX={ControllerTemperatureTestRxChannel}, Label=0x{DefaultLabel:X2}");

                try { await _simulation.ClearRxFifoAsync(ControllerTemperatureTestRxChannel); } catch { }
                await Task.Delay(30);

                var resp = await _simulation.SendBenchCommandAndWaitAsync(
                    ControllerTemperatureTestTxChannel, ControllerTemperatureTestRxChannel,
                    DefaultLabel, AbCarDtsTemperature,
                    b => b != null && b.Length == 8 && b.SequenceEqual(AbCarDtsTemperature),
                    timeoutMs: 800,
                    msg => AddLog(msg), CancellationToken.None);

                if (resp != null)
                {
                    ControllerTemperatureTestRxDataText = "0x" + FormatData(resp);
                    AddLog($"[{DateTime.Now:HH:mm:ss}] AB_CAR_DTS_Temperature 收到回包");
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

        private async Task OnTestTemperatureTelemetryAsync()
        {
            await _arincOpLock.WaitAsync();
            try
            {
                TemperatureTelemetryValueText = "--";
                TemperatureTelemetryRxDataText = "--";
                LastTelemetryTemperatureC = null;
                AddLog($"[{DateTime.Now:HH:mm:ss}] 测试：温度回采值，RX通道={TemperatureTelemetryRxChannel}");

                var ok = await _simulation.EnsureBenchRxChannelAsync(TemperatureTelemetryRxChannel, msg => AddLog(msg));
                if (!ok)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 温度回采值失败：打开/配置RX通道失败");
                    return;
                }

                try { await _simulation.ClearRxFifoAsync(TemperatureTelemetryRxChannel); } catch { }
                await Task.Delay(20);

                var (tempData, rawData) = await _simulation.WaitTelemetryAsync(
                    TemperatureTelemetryRxChannel, timeoutMs: 1500, msg => AddLog(msg), CancellationToken.None);

                if (tempData == null)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 温度回采值失败：未收到温度采集值(07 01 06 02)");

                    if (!_suppressResultUpdates)
                    {
                        LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                        LastTestResult = $"{FormatGearForResult(ResistorGear)}电阻温度不通过";
                    }
                    return;
                }

                TemperatureTelemetryRxDataText = "0x" + FormatData(tempData);
                if (TryParseTelemetryTemperature(tempData, out var temperature))
                {
                    TemperatureTelemetryValueText = temperature.ToString("0.####", CultureInfo.InvariantCulture);
                    LastTelemetryTemperatureC = temperature;
                    if (string.Equals(ResistorGear, "1挡", StringComparison.Ordinal))
                        Gear1TemperatureC = temperature;
                    else if (string.Equals(ResistorGear, "2挡", StringComparison.Ordinal))
                        Gear2TemperatureC = temperature;
                    else if (string.Equals(ResistorGear, "3挡", StringComparison.Ordinal))
                        Gear3TemperatureC = temperature;
                }
                else
                {
                    TemperatureTelemetryValueText = "--";
                }

                if (!_suppressResultUpdates)
                {
                    LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    if (!TryParseTelemetryTemperature(tempData, out temperature))
                    {
                        LastTestResult = $"{FormatGearForResult(ResistorGear)}电阻温度不通过";
                    }
                    else
                    {
                        var qualified = IsTemperatureQualified(ResistorGear, temperature);
                        LastTestResult = qualified
                            ? $"{FormatGearForResult(ResistorGear)}电阻温度PASS"
                            : $"{FormatGearForResult(ResistorGear)}电阻温度不通过";
                    }
                }

                if (rawData != null)
                {
                    var rawHex = FormatData(rawData, rawData.Length);
                    if (TryParseBase6FromNibbles(rawData, out var rawBase6Decimal))
                        AddLog($"[{DateTime.Now:HH:mm:ss}] CAR_DTS温度原始数据(07 01 06 03) 后四字节(6进制)->10进制：{rawBase6Decimal}，Data={rawHex}");
                    else
                        AddLog($"[{DateTime.Now:HH:mm:ss}] CAR_DTS温度原始数据(07 01 06 03) Data={rawHex}");
                }
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 温度回采值异常：{ex.Message}");
            }
            finally
            {
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

            var intPartRaw = (ushort)((frameData[4] << 8) | frameData[5]);
            var fracPart = (ushort)((frameData[6] << 8) | frameData[7]);
            var signedInt = unchecked((short)intPartRaw);
            var frac = fracPart / 10000.0;
            temperature = signedInt < 0 ? signedInt - frac : signedInt + frac;
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

        private static async Task<(MessageBasedSession Session, ResourceManager Rm)> OpenDmmAsync()
        {
            var rm = new ResourceManager();
            var resource = "TCPIP0::192.168.1.13::inst0::INSTR";
            try
            {
                var session = (MessageBasedSession)rm.Open(resource);
                session.TimeoutMilliseconds = 3000;
                session.RawIO.Write("*CLS\n");
                session.RawIO.Write(":SYST:REM\n");
                session.RawIO.Write(":CONF:RES\n");
                await Task.Yield();
                return (session, rm);
            }
            catch
            {
                try { rm.Dispose(); } catch { }
                throw;
            }
        }

        private static double QueryDmmResistance(MessageBasedSession session)
        {
            session.RawIO.Write(":MEAS:RES?\n");
            var resp = session.RawIO.ReadString();
            if (double.TryParse(resp?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var r))
            {
                return r;
            }

            if (double.TryParse(resp?.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out r))
            {
                return r;
            }

            return double.NaN;
        }

        private static DeviceBase Resolve7012Device(string chassisName, IPxiChassisService pxiChassisService)
        {
            if (pxiChassisService == null) return null;

            var chassis = pxiChassisService.GetAllChassis()?.FirstOrDefault(c =>
                string.Equals(c?.Name, chassisName, StringComparison.OrdinalIgnoreCase));

            var devices = chassis?.Devices;
            if (devices == null) return null;

            return devices.FirstOrDefault(d => d is ProgrammableResistorDevice)
                   ?? devices.FirstOrDefault(d => (d?.Model ?? string.Empty).ToUpperInvariant().Contains("7012"));
        }

        private async Task<bool> EnsureResistorReadyAsync()
        {
            if (_resistorDriver != null && _resistorDriver.IsConnected)
                return true;

            try
            {
                var candidates = new uint[] { 1, 0, 2, 3, 4, 5, 6, 7 };
                foreach (var logicalId in candidates)
                {
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
                        _resistorDriver = driver;
                        AddLog($"[{DateTime.Now:HH:mm:ss}] 电阻板卡已连接：ACTS6010 逻辑ID={logicalId}");
                        return true;
                    }
                }

                AddLog($"[{DateTime.Now:HH:mm:ss}] 电阻板卡打开失败：ACTS6010 逻辑ID 0-7 均连接失败");
                return false;
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 电阻板卡打开异常：{ex.Message}");
                _resistorDriver = null;
                return false;
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

            MessageBasedSession dmmSession = null;
            ResourceManager dmmRm = null;
            bool matrix1Connected = false;
            bool matrix2Connected = false;
            bool resistorReady = false;

            try
            {
                // 优先使用上游新增的"电阻板卡直连(逻辑ID探测)"能力，不依赖机箱上下文
                resistorReady = await EnsureResistorReadyAsync();
                if (!resistorReady || _resistorDriver == null || !_resistorDriver.IsConnected)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 接入电阻失败：电阻板卡未就绪");
                    return;
                }

                var targetOhm = GetTargetResistanceOhm(ResistorGear);
                AddLog($"[{DateTime.Now:HH:mm:ss}] 发送：接入电阻，档位={ResistorGear}，目标={targetOhm.ToString("F2", CultureInfo.InvariantCulture)}Ω");

                var relayOk = await _resistorDriver.SetRelayStateAsync("RO0", true, false);
                if (!relayOk)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 7012设置RO0继电器失败(通路闭合/短路断开)");
                    return;
                }

                var writeOk = await _resistorDriver.WriteChannelAsync("RO0", targetOhm);
                if (!writeOk)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 7012写入RO0失败");
                    return;
                }

                await Task.Delay(50);

                var matrixSvc = MatrixControlService.Instance;
                matrix1Connected = await matrixSvc.ConnectNodesAsync("I1", "O8", 6, "192.168.1.3");
                if (!matrix1Connected)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 矩阵开关1连接失败(I1->O8 slot6)");
                    return;
                }

                matrix2Connected = await matrixSvc.ConnectNodesAsync("I4", "O2", 4, "192.168.1.3");
                if (!matrix2Connected)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 矩阵开关2连接失败(I4->O2 slot4)");
                    return;
                }

                (dmmSession, dmmRm) = await OpenDmmAsync();
                await Task.Delay(200);
                var measured = QueryDmmResistance(dmmSession);

                if (double.IsNaN(measured))
                {
                    MeasuredResistanceValueText = "NaN";
                }
                else
                {
                    MeasuredResistanceValueText = $"{measured.ToString("F5", CultureInfo.InvariantCulture)}Ω";
                }

                AddLog($"[{DateTime.Now:HH:mm:ss}] 万用表实测电阻：{MeasuredResistanceValueText}");
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 接入电阻异常：{ex.Message}");
            }
            finally
            {
                var matrixSvc = MatrixControlService.Instance;

                try
                {
                    if (matrix2Connected)
                    {
                        await matrixSvc.DisconnectNodesAsync("I4", "O2", 4, "192.168.1.3");
                    }
                }
                catch { }

                try
                {
                    if (matrix1Connected)
                    {
                        await matrixSvc.DisconnectNodesAsync("I1", "O8", 6, "192.168.1.3");
                    }
                }
                catch { }

                try
                {
                    if (dmmSession != null)
                    {
                        try { dmmSession.RawIO.Write(":SYST:LOC\n"); } catch { }
                        try { dmmSession.Dispose(); } catch { }
                    }
                }
                catch { }

                try
                {
                    if (dmmRm != null)
                    {
                        try { dmmRm.Dispose(); } catch { }
                    }
                }
                catch { }

                try
                {
                    if (resistorReady)
                        await DisconnectResistorAsync();
                }
                catch { }

                IsResistorMeasuring = false;
            }
        }

        public void Dispose()
        {
            try
            {
                _simulation?.StopAsync(msg => AddLog(msg)).GetAwaiter().GetResult();
            }
            catch { }

            try
            {
                DisconnectResistorAsync().GetAwaiter().GetResult();
            }
            catch { }
        }
    }
}
