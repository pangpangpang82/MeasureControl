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
using MeasureControl.Simulations.R_6_8_8;
using Prism.Ioc;

namespace MeasureControl.ViewModels.SingleBoardTest.AirController
{
    public class R_6_8_8ViewModel : BindableBase, IDisposable
    {
        private const byte DefaultLabel = 0x6A;

        private static readonly byte[] AtpR = { 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] AtpEnterOk = { 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x02 };
        private static readonly byte[] AtpE = { 0x00, 0x02, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01 };
        private static readonly byte[] ExitOk = { 0x00, 0x02, 0x00, 0x01, 0x00, 0x00, 0x00, 0x03 };
        private static readonly byte[] AbPtsTemperature = { 0x07, 0x01, 0x08, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] TelemetryTemperaturePrefix = { 0x07, 0x01, 0x08, 0x02 };
        private static readonly byte[] TelemetryRawPrefix = { 0x07, 0x01, 0x08, 0x03 };

        public R_6_8_8ViewModel()
        {
            _enterAtpTxChannel = "CH0";
            _enterAtpRxChannel = "CH2";
            _controllerTemperatureTestTxChannel = "CH0";
            _controllerTemperatureTestRxChannel = "CH2";
            _temperatureTelemetryRxChannel = "CH2";
            _exitAtpTxChannel = "CH0";
            _exitAtpRxChannel = "CH2";

            _enterAtpRxDataText = "--";
            _controllerTemperatureTestRxDataText = "--";
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
            TestControllerTemperatureCommand = new DelegateCommand(async () => await OnTestControllerTemperatureAsync());
            TestTemperatureTelemetryCommand = new DelegateCommand(async () => await OnTestTemperatureTelemetryAsync());
            SendExitAtpCommand = new DelegateCommand(async () => await OnSendExitAtpAsync());
            ClearLogCommand = new DelegateCommand(() => Logs.Clear());

            ManualTestCommand = new DelegateCommand(OnManualTest);
            AutoTestCommand = new DelegateCommand(OnAutoTest);
        }

        private ACTS6010Driver _resistorDriver;

        private readonly R_6_8_8Simulation _simulation = new R_6_8_8Simulation();
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
        private string _ambientTemperatureSelection;
        private string _resistorGearValueText;
        private string _measuredResistanceValueText;
        private string _temperatureTelemetryValueText;
        private string _lastTestTime;
        private string _lastTestResult;

        private bool _isManualTestRunning;
        private bool _isAutoTestRunning;
        private bool _isResistorMeasuring;

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

        public string TemperatureTelemetryRxChannel
        {
            get => _temperatureTelemetryRxChannel;
            set => SetProperty(ref _temperatureTelemetryRxChannel, value);
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

        public string EnterAtpRxDataText
        {
            get => _enterAtpRxDataText;
            set => SetProperty(ref _enterAtpRxDataText, value);
        }

        public string ControllerTemperatureTestRxDataText
        {
            get => _controllerTemperatureTestRxDataText;
            set => SetProperty(ref _controllerTemperatureTestRxDataText, value);
        }

        public string TemperatureTelemetryRxDataText
        {
            get => _temperatureTelemetryRxDataText;
            set => SetProperty(ref _temperatureTelemetryRxDataText, value);
        }

        public string ExitAtpRxDataText
        {
            get => _exitAtpRxDataText;
            set => SetProperty(ref _exitAtpRxDataText, value);
        }

        public string ResistorGear
        {
            get => _resistorGear;
            set
            {
                if (SetProperty(ref _resistorGear, value))
                {
                    ResistorGearValueText = value ?? "1挡";
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
            set => SetProperty(ref _measuredResistanceValueText, value);
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

        public bool IsManualTestRunning
        {
            get => _isManualTestRunning;
            set => SetProperty(ref _isManualTestRunning, value);
        }

        public bool IsAutoTestRunning
        {
            get => _isAutoTestRunning;
            set => SetProperty(ref _isAutoTestRunning, value);
        }

        public bool IsResistorMeasuring
        {
            get => _isResistorMeasuring;
            set => SetProperty(ref _isResistorMeasuring, value);
        }

        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();

        public DelegateCommand SendEnterAtpCommand { get; }
        public DelegateCommand SendSetControllerResistorCommand { get; }
        public DelegateCommand TestControllerTemperatureCommand { get; }
        public DelegateCommand TestTemperatureTelemetryCommand { get; }
        public DelegateCommand SendExitAtpCommand { get; }
        public DelegateCommand ClearLogCommand { get; }
        public DelegateCommand ManualTestCommand { get; }
        public DelegateCommand AutoTestCommand { get; }

        public bool CanEditStepControls => !IsManualTestRunning && !IsAutoTestRunning;

        private void AddLog(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                Logs.Add(message);
            });
        }

        private async Task OnSendEnterAtpAsync()
        {
            await _arincOpLock.WaitAsync();
            try
            {
                EnterAtpRxDataText = "--";
                AddLog($"[{DateTime.Now:HH:mm:ss}] 发送：进入ATP模式，TX={EnterAtpTxChannel}, RX={EnterAtpRxChannel}, Label=0x{DefaultLabel:X2}");

                _simulation.GetCurrentResistorGear = () => ResistorGear;
                _simulation.GetCurrentAmbientTemperatureSelection = () => AmbientTemperatureSelection;
                await _simulation.StartAsync(EnterAtpTxChannel, EnterAtpRxChannel, msg => AddLog(msg));

                try { await _simulation.ClearRxFifoAsync(EnterAtpRxChannel); } catch { }
                await Task.Delay(30);

                var resp = await _simulation.SendBenchCommandAndWaitAsync(
                    EnterAtpTxChannel, EnterAtpRxChannel,
                    DefaultLabel, AtpR,
                    b => b != null && b.Length == 8 && b.SequenceEqual(AtpEnterOk),
                    timeoutMs: 800,
                    msg => AddLog(msg), CancellationToken.None);

                if (resp != null)
                {
                    EnterAtpRxDataText = "0x" + FormatData(resp);
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 进入ATP模式成功，收到回包");
                }
                else
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 进入ATP模式失败，未收到预期回包");
                }
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 异常：{ex.Message}");
            }
            finally
            {
                _arincOpLock.Release();
            }
        }

        private async Task SendSetControllerResistorAsync()
        {
            if (IsResistorMeasuring)
                return;
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

                var readBack = await _resistorDriver.ReadChannelAsync("RO0");
                MeasuredResistanceValueText = $"{readBack.ToString("F5", CultureInfo.InvariantCulture)}Ω";
                AddLog($"[{DateTime.Now:HH:mm:ss}] 电阻板卡读回电阻：{MeasuredResistanceValueText}");
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 异常：{ex.Message}");
            }
            finally
            {
                try { await DisconnectResistorAsync(); } catch { }
                IsResistorMeasuring = false;
            }
        }

        private async Task OnTestControllerTemperatureAsync()
        {
            await _arincOpLock.WaitAsync();
            try
            {
                ControllerTemperatureTestRxDataText = "--";
                AddLog($"[{DateTime.Now:HH:mm:ss}] 测试：PTS温度，TX={ControllerTemperatureTestTxChannel}, RX={ControllerTemperatureTestRxChannel}, Label=0x{DefaultLabel:X2}");

                try { await _simulation.ClearRxFifoAsync(ControllerTemperatureTestRxChannel); } catch { }
                await Task.Delay(30);

                var resp = await _simulation.SendBenchCommandAndWaitAsync(
                    ControllerTemperatureTestTxChannel, ControllerTemperatureTestRxChannel,
                    DefaultLabel, AbPtsTemperature,
                    b => b != null && b.Length == 8 && b.SequenceEqual(AbPtsTemperature),
                    timeoutMs: 800,
                    msg => AddLog(msg), CancellationToken.None);

                if (resp != null)
                {
                    ControllerTemperatureTestRxDataText = "0x" + FormatData(resp);
                    AddLog($"[{DateTime.Now:HH:mm:ss}] PTS温度测试收到回包");
                }
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 异常：{ex.Message}");
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
                TemperatureTelemetryRxDataText = "--";
                TemperatureTelemetryValueText = "--";
                AddLog($"[{DateTime.Now:HH:mm:ss}] 测试：温度回采值，RX={TemperatureTelemetryRxChannel}");

                try { await _simulation.ClearRxFifoAsync(TemperatureTelemetryRxChannel); } catch { }
                await Task.Delay(30);

                bool ok = await _simulation.EnsureBenchRxChannelAsync(TemperatureTelemetryRxChannel, msg => AddLog(msg));
                if (!ok)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 温度回采测试失败：通道未就绪");
                    return;
                }

                var (temp8, raw8) = await _simulation.WaitTelemetryAsync(
                    TemperatureTelemetryRxChannel,
                    timeoutMs: 2000,
                    msg => AddLog(msg),
                    CancellationToken.None);

                if (temp8 != null)
                {
                    TemperatureTelemetryRxDataText = "0x" + FormatData(temp8);
                    double tempValue = ParseTemperatureFromBytes(temp8);
                    TemperatureTelemetryValueText = $"{tempValue:F2}°C";
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 温度回采值={tempValue:F2}°C");
                }
                else
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 未收到温度遥测帧");
                }
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 异常：{ex.Message}");
            }
            finally
            {
                _arincOpLock.Release();
            }
        }

        private async Task OnSendExitAtpAsync()
        {
            await _arincOpLock.WaitAsync();
            try
            {
                ExitAtpRxDataText = "--";
                AddLog($"[{DateTime.Now:HH:mm:ss}] 发送：退出ATP模式，TX={ExitAtpTxChannel}, RX={ExitAtpRxChannel}, Label=0x{DefaultLabel:X2}");

                try { await _simulation.ClearRxFifoAsync(ExitAtpRxChannel); } catch { }
                await Task.Delay(30);

                var resp = await _simulation.SendBenchCommandAndWaitAsync(
                    ExitAtpTxChannel, ExitAtpRxChannel,
                    DefaultLabel, AtpE,
                    b => b != null && b.Length == 8 && b.SequenceEqual(ExitOk),
                    timeoutMs: 800,
                    msg => AddLog(msg), CancellationToken.None);

                if (resp != null)
                {
                    ExitAtpRxDataText = "0x" + FormatData(resp);
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 退出ATP模式成功，收到回包");
                }
                else
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 退出ATP模式失败，未收到预期回包");
                }
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 异常：{ex.Message}");
            }
            finally
            {
                _arincOpLock.Release();
            }
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

                try
                {
                    var api = Prism.Ioc.ContainerLocator.Container.Resolve(typeof(MeasureControl.Services.HardwareApis.IComponentPowerStateApi)) as MeasureControl.Services.HardwareApis.IComponentPowerStateApi;
                    if (api != null)
                        await api.ApplyComponent28VStateAsync(CancellationToken.None);
                }
                catch { }

                _simulation.IsRealProduct = AppConstants.Arinc429IsRealProduct;
                _simulation.GetCurrentResistorGear = () => ResistorGear;
                _simulation.GetCurrentAmbientTemperatureSelection = () => AmbientTemperatureSelection;
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

        private void OnAutoTest()
        {
            if (IsAutoTestRunning)
            {
                try { _autoTestCts?.Cancel(); } catch { }
                AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试已停止");
                return;
            }

            Task.Run(async () =>
            {
                if (!await _autoTestLock.WaitAsync(0))
                    return;
                IsAutoTestRunning = true;
                _autoTestCts = new CancellationTokenSource();
                _autoTestEnteredAtp = false;
                var token = _autoTestCts.Token;

                try
                {
                    try
                    {
                        var api = Prism.Ioc.ContainerLocator.Container.Resolve(typeof(MeasureControl.Services.HardwareApis.IComponentPowerStateApi)) as MeasureControl.Services.HardwareApis.IComponentPowerStateApi;
                        if (api != null)
                            await api.ApplyComponent28VStateAsync(token);
                    }
                    catch { }

                    AddLog($"[{DateTime.Now:HH:mm:ss}] ========== 自动测试开始（3档循环） ==========");
                    var gears = new[] { "1挡", "2挡", "3挡" };

                    bool firstRound = true;
                    while (!token.IsCancellationRequested)
                    {
                        foreach (var gear in gears)
                        {
                            if (token.IsCancellationRequested) break;
                            AddLog($"[{DateTime.Now:HH:mm:ss}] === 当前档位：{gear} ===");

                            if (!firstRound || gear == "1挡")
                            {
                                ResistorGear = gear;
                                await Task.Delay(100, token);
                                await SendSetControllerResistorAsync();
                                await Task.Delay(200, token);
                            }
                            firstRound = false;

                            await OnTestControllerTemperatureAsync();
                            await Task.Delay(200, token);
                            await OnTestTemperatureTelemetryAsync();
                            await Task.Delay(500, token);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试已取消");
                }
                finally
                {
                    IsAutoTestRunning = false;
                    _autoTestLock.Release();
                }
            });
        }

        private async Task RunTestSequenceAsync(CancellationToken token)
        {
            try
            {
                await _simulation.StartAsync(
                    ControllerTemperatureTestTxChannel,
                    ControllerTemperatureTestRxChannel,
                    msg => AddLog(msg));
                _simulation.GetCurrentResistorGear = () => ResistorGear;

                await OnSendEnterAtpAsync();
                await Task.Delay(300, token);

                await SendSetControllerResistorAsync();
                await Task.Delay(300, token);

                await OnTestControllerTemperatureAsync();
                await Task.Delay(300, token);

                await OnTestTemperatureTelemetryAsync();
                await Task.Delay(300, token);

                await OnSendExitAtpAsync();
                await Task.Delay(200, token);

                await EvaluateTestResultAsync();
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 测试序列异常：{ex.Message}");
            }
            finally
            {
                try
                {
                    await _simulation.StopAsync(msg => AddLog(msg));
                }
                catch { }
            }
        }

        private async Task EvaluateTestResultAsync()
        {
            await Task.Delay(100);

            string gear = ResistorGear ?? "1挡";
            double targetOhm = GetTargetResistanceOhm(gear);

            bool resistorOk = false;
            if (double.TryParse(MeasuredResistanceValueText.Replace("Ω", "").Trim(), out double measuredOhm))
            {
                double tolerance = 0.5;
                resistorOk = Math.Abs(measuredOhm - targetOhm) <= tolerance;
            }

            bool telemetryOk = false;
            double tempValue = double.NaN;
            if (double.TryParse(TemperatureTelemetryValueText.Replace("°C", "").Trim(), out tempValue))
            {
                var (min, max) = GetQualifiedTemperatureRangeForGear(gear, AmbientTemperatureSelection);
                telemetryOk = tempValue >= min && tempValue <= max;
            }

            bool overall = resistorOk && telemetryOk;

            string resultText = overall ? "合格" : "不合格";
            string detail = $"档位={FormatGearForResult(gear)}, 目标电阻={targetOhm}Ω, 实测电阻={MeasuredResistanceValueText}, 温度={TemperatureTelemetryValueText}, 结果={resultText}";

            LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            LastTestResult = resultText;

            AddLog($"[{DateTime.Now:HH:mm:ss}] 测试结果：{detail}");
        }

        private static (double Min, double Max) GetQualifiedTemperatureRangeForGear(string gear, string ambientSelection)
        {
            bool isNormalAmbient = string.Equals(ambientSelection, "10~50℃", StringComparison.OrdinalIgnoreCase);
            return gear switch
            {
                "1挡" => isNormalAmbient ? (-77.05, -72.95) : (-79.05, -70.95),
                "2挡" => isNormalAmbient ? (23.63, 27.73) : (21.63, 29.73),
                "3挡" => isNormalAmbient ? (372.94, 377.06) : (370.94, 379.06),
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
                _ => gear
            };
        }

        private static double GetTargetResistanceOhm(string gear)
        {
            return gear switch
            {
                "1挡" => 351.65,
                "2挡" => 550.0,
                "3挡" => 1192.2,
                _ => 351.65
            };
        }

        private static double ParseTemperatureFromBytes(byte[] data)
        {
            if (data == null || data.Length < 8)
                return double.NaN;

            int intPart = (data[4] << 8) | data[5];
            int fracPart = (data[6] << 8) | data[7];
            double frac = fracPart / 10000.0;
            double sign = intPart >= 0x8000 ? -1 : 1;
            intPart = intPart & 0x7FFF;
            return sign * (intPart + frac);
        }

        private static string FormatData(byte[] data)
        {
            if (data == null || data.Length == 0)
                return string.Empty;
            int len = Math.Min(8, data.Length);
            return string.Join(" ", data.Take(len).Select(b => b.ToString("X2")));
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

        public void Dispose()
        {
            try
            {
                _autoTestCts?.Cancel();
                _simulation?.Dispose();
                if (_resistorDriver != null)
                {
                    _ = _resistorDriver.DisconnectAsync();
                }
            }
            catch { }
        }
    }
}
