using MeasureControl.Services.HardwareApis;
using MeasureControl.Simulations.PT500;
using Prism.Commands;
using Prism.Mvvm;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace MeasureControl.ViewModels.SingleBoardTest.AirController
{
    public class AirSimpleSequenceViewModel : BindableBase
    {
        private const byte DefaultLabel = 0x6A;

        private static readonly byte[] AtpR = { 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] AtpEnterOk = { 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x02 };
        private static readonly byte[] AtpE = { 0x00, 0x02, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01 };
        private static readonly byte[] ExitOk = { 0x00, 0x02, 0x00, 0x01, 0x00, 0x00, 0x00, 0x03 };

        // From protocol screenshot: AB_28V_SUPPLY = 01 01 01 01 00 00 00 00
        private static readonly byte[] Ab28vSupply = { 0x01, 0x01, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00 };

        private static readonly byte[] Req15vVbit = { 0x01, 0x01, 0x01, 0x02, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] Req5vVbit = { 0x01, 0x01, 0x01, 0x03, 0x00, 0x00, 0x00, 0x00 };

        private readonly PT500TemperatureSensor429Simulation _arinc = new PT500TemperatureSensor429Simulation();
        private readonly SemaphoreSlim _arincOpLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _manualTestLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _autoTestLock = new SemaphoreSlim(1, 1);

        private CancellationTokenSource _autoTestCts;
        private bool _autoTestEnteredAtp;
        private bool _suppressResultUpdates;

        private IPowerSupplyApi _powerSupply;
        private readonly SemaphoreSlim _powerSupplyLock = new SemaphoreSlim(1, 1);

        private string _title = "测试";
        private bool _isManualTestRunning;
        private bool _isAutoTestRunning;
        private string _lastTestTime = "--";
        private string _lastTestResult = "--";

        private string _powerSupplyIpAddress = "192.168.1.15";
        private string _powerSupplyMeasuredCurrentText = "--";

        private string _enterAtpTxChannel;
        private string _enterAtpRxChannel;
        private string _setVoltageTxChannel;
        private string _dmmChannel;
        private string _telemetryRxChannel;
        private string _exitAtpTxChannel;
        private string _exitAtpRxChannel;

        private string _dmmVoltageText;
        private string _telemetryVoltageText;
        private string _enterAtpRxDataText;
        private string _telemetryRxDataText;
        private string _exitAtpRxDataText;

        public AirSimpleSequenceViewModel()
        {
            _enterAtpTxChannel = "429_CH0";
            _enterAtpRxChannel = "429_CH1";
            _setVoltageTxChannel = "429_CH2";
            _telemetryRxChannel = "429_CH4";
            _exitAtpTxChannel = "429_CH5";
            _exitAtpRxChannel = "429_CH6";
            _dmmChannel = "Port1";

            DmmVoltageText = "--";
            PowerSupplyMeasuredCurrentText = "--";
            TelemetryVoltageText = "--";
            EnterAtpRxDataText = "--";
            TelemetryRxDataText = "--";
            ExitAtpRxDataText = "--";

            ManualTestCommand = new DelegateCommand(OnManualTest);
            AutoTestCommand = new DelegateCommand(OnAutoTest);
            ClearLogCommand = new DelegateCommand(() => Logs.Clear());

            SendEnterAtpCommand = new DelegateCommand(async () => await OnSendEnterAtpAsync());
            SendSetVoltageCommand = new DelegateCommand(async () => await OnSendAb28vSupplyAsync());
            SendExitAtpCommand = new DelegateCommand(async () => await OnSendExitAtpAsync());
        }

        public string Title
        {
            get => _title;
            set => SetProperty(ref _title, value);
        }

        public string PowerSupplyIpAddress
        {
            get => _powerSupplyIpAddress;
            set => SetProperty(ref _powerSupplyIpAddress, value);
        }

        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();

        public DelegateCommand ManualTestCommand { get; }
        public DelegateCommand AutoTestCommand { get; }
        public DelegateCommand ClearLogCommand { get; }

        public DelegateCommand SendEnterAtpCommand { get; }
        public DelegateCommand SendSetVoltageCommand { get; }
        public DelegateCommand SendExitAtpCommand { get; }

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

        public string SetVoltageTxChannel
        {
            get => _setVoltageTxChannel;
            set => SetProperty(ref _setVoltageTxChannel, value);
        }

        public string DmmChannel
        {
            get => _dmmChannel;
            set => SetProperty(ref _dmmChannel, value);
        }

        public string TelemetryRxChannel
        {
            get => _telemetryRxChannel;
            set => SetProperty(ref _telemetryRxChannel, value);
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

        public string DmmVoltageText
        {
            get => _dmmVoltageText;
            set => SetProperty(ref _dmmVoltageText, value);
        }

        public string PowerSupplyMeasuredCurrentText
        {
            get => _powerSupplyMeasuredCurrentText;
            set => SetProperty(ref _powerSupplyMeasuredCurrentText, value);
        }

        public string TelemetryVoltageText
        {
            get => _telemetryVoltageText;
            set => SetProperty(ref _telemetryVoltageText, value);
        }

        public string EnterAtpRxDataText
        {
            get => _enterAtpRxDataText;
            private set => SetProperty(ref _enterAtpRxDataText, value);
        }

        public string TelemetryRxDataText
        {
            get => _telemetryRxDataText;
            private set => SetProperty(ref _telemetryRxDataText, value);
        }

        public string ExitAtpRxDataText
        {
            get => _exitAtpRxDataText;
            private set => SetProperty(ref _exitAtpRxDataText, value);
        }

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
                if (SetProperty(ref _isAutoTestRunning, value) && value)
                {
                    IsManualTestRunning = false;
                }
            }
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

                await EnsurePowerSupplyConnectedAsync(CancellationToken.None);
                await _arinc.StartAsync(EnterAtpTxChannel, EnterAtpRxChannel, msg => AddLog(msg));

                AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试启动：429板卡/电源已就绪");
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

                try { await PowerSupplyOutputOffAsync(CancellationToken.None); } catch { }
                try { await DisconnectPowerSupplyAsync(CancellationToken.None); } catch { }
                try { await _arinc.StopAsync(msg => AddLog(msg)); } catch { }

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
                DmmVoltageText = "--";
                PowerSupplyMeasuredCurrentText = "--";
                TelemetryVoltageText = "--";
                EnterAtpRxDataText = "--";
                TelemetryRxDataText = "--";
                ExitAtpRxDataText = "--";

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
            var failures = new ObservableCollection<string>();
            try
            {
                token.ThrowIfCancellationRequested();

                AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试：开始打开设备");
                await EnsurePowerSupplyConnectedAsync(token);
                await _arinc.StartAsync(EnterAtpTxChannel, EnterAtpRxChannel, msg => AddLog(msg));

                await RunSupplyVoltageScenarioAsync(
                    supplyVoltage: 32.0,
                    currentUpperLimit: 2.18,
                    token,
                    failures);

                token.ThrowIfCancellationRequested();

                await RunSupplyVoltageScenarioAsync(
                    supplyVoltage: 28.0,
                    currentUpperLimit: 2.50,
                    token,
                    failures);

                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                LastTestResult = failures.Count == 0 ? "PASS" : "FAIL";
                AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试汇总：{LastTestResult}");
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
                        await AutoExitAtpAsync(CancellationToken.None);
                }
                catch { }

                try { await PowerSupplyOutputOffAsync(CancellationToken.None); } catch { }
                try { await DisconnectPowerSupplyAsync(CancellationToken.None); } catch { }
                try { await _arinc.StopAsync(msg => AddLog(msg)); } catch { }

                _suppressResultUpdates = false;
                IsAutoTestRunning = false;
            }
        }

        private async Task RunSupplyVoltageScenarioAsync(double supplyVoltage, double currentUpperLimit, CancellationToken token, ObservableCollection<string> failures)
        {
            token.ThrowIfCancellationRequested();

            AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试：向控制通道供电 {supplyVoltage.ToString("0.###", CultureInfo.InvariantCulture)}V");
            await PowerSupplyApplyAsync(supplyVoltage, currentLimit: Math.Max(3.0, currentUpperLimit + 0.5), token);
            await Task.Delay(300, token);

            var ms = await PowerSupplyReadMeasurementsAsync(token);
            if (ms?.Voltage?.Value != null)
                DmmVoltageText = $"{ms.Voltage.Value:0.000} V";
            if (ms?.Current?.Value != null)
                PowerSupplyMeasuredCurrentText = $"{ms.Current.Value:0.000} A";

            if (ms?.Voltage?.Value != null || ms?.Current?.Value != null)
            {
                var vText = ms?.Voltage?.Value != null ? $"{ms.Voltage.Value:0.000}V" : "--";
                var iText = ms?.Current?.Value != null ? $"{ms.Current.Value:0.000}A" : "--";
                AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试：记录供电电源读数 V={vText}, I={iText}");
            }

            if (ms?.Current?.Value != null && ms.Current.Value > currentUpperLimit)
            {
                failures.Add($"{supplyVoltage:0.###}V供电电流={ms.Current.Value:0.000}A > {currentUpperLimit:0.###}A");
            }

            token.ThrowIfCancellationRequested();

            var enteredAtpOk = await AutoEnterAtpAsync(token);
            if (!enteredAtpOk)
            {
                failures.Add($"{supplyVoltage:0.###}V：进入ATP失败");
                await PowerSupplyOutputOffAsync(token);
                return;
            }

            _autoTestEnteredAtp = true;

            token.ThrowIfCancellationRequested();
            var telemetryOk = await AutoSendAb28vSupplyAndReadTelemetryAsync(token, failures);
            if (!telemetryOk)
            {
                failures.Add($"{supplyVoltage:0.###}V：AB_28V_SUPPLY 回采失败");
            }

            token.ThrowIfCancellationRequested();

            var exitOk = await AutoExitAtpAsync(token);
            if (!exitOk)
            {
                failures.Add($"{supplyVoltage:0.###}V：退出ATP失败");
            }

            _autoTestEnteredAtp = false;

            AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试：断开控制通道供电");
            await PowerSupplyOutputOffAsync(token);
            await Task.Delay(200, token);
        }

        private async Task<bool> AutoEnterAtpAsync(CancellationToken token)
        {
            await _arincOpLock.WaitAsync(token);
            try
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试：发送进入ATP");
                EnterAtpRxDataText = "--";

                try { await _arinc.ClearRxFifoAsync(EnterAtpRxChannel); } catch { }
                await Task.Delay(50, token);

                var resp = await _arinc.SendBenchCommandAndWaitAsync(
                    EnterAtpTxChannel,
                    EnterAtpRxChannel,
                    DefaultLabel,
                    AtpR,
                    b => b.SequenceEqual(AtpEnterOk),
                    timeoutMs: 3000,
                    msg => AddLog(msg),
                    token);

                if (resp == null)
                    return false;

                EnterAtpRxDataText = "0x" + FormatBytesHex(resp);
                return true;
            }
            finally
            {
                _arincOpLock.Release();
            }
        }

        private async Task<bool> AutoExitAtpAsync(CancellationToken token)
        {
            await _arincOpLock.WaitAsync(token);
            try
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试：发送退出ATP");
                ExitAtpRxDataText = "--";

                try { await _arinc.ClearRxFifoAsync(ExitAtpRxChannel); } catch { }
                await Task.Delay(50, token);

                var resp = await _arinc.SendBenchCommandAndWaitAsync(
                    ExitAtpTxChannel,
                    ExitAtpRxChannel,
                    DefaultLabel,
                    AtpE,
                    b => b.SequenceEqual(ExitOk),
                    timeoutMs: 2000,
                    msg => AddLog(msg),
                    token);

                if (resp == null)
                    return false;

                ExitAtpRxDataText = "0x" + FormatBytesHex(resp);
                return true;
            }
            finally
            {
                _arincOpLock.Release();
            }
        }

        private async Task<bool> AutoSendAb28vSupplyAndReadTelemetryAsync(CancellationToken token, ObservableCollection<string> failures)
        {
            await _arincOpLock.WaitAsync(token);
            try
            {
                TelemetryVoltageText = "--";
                TelemetryRxDataText = "--";

                AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试：发送测试指令 AB_28V_SUPPLY");
                try { await _arinc.ClearRxFifoAsync(TelemetryRxChannel); } catch { }
                await Task.Delay(30, token);

                await _arinc.SendBenchCommandOnlyAsync(
                    SetVoltageTxChannel,
                    DefaultLabel,
                    Ab28vSupply,
                    msg => AddLog(msg),
                    token);

                token.ThrowIfCancellationRequested();

                AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试：读取二次电源回采值(15V_VBIT)");
                var resp15 = await _arinc.SendBenchCommandAndWaitAsync(
                    SetVoltageTxChannel,
                    TelemetryRxChannel,
                    DefaultLabel,
                    Req15vVbit,
                    Is15vVbitPayload,
                    timeoutMs: 2000,
                    msg => AddLog(msg),
                    token);

                if (resp15 == null)
                    return false;

                if (!TryParseSingleVbitValue(resp15, Req15vVbit, out var v15))
                {
                    failures.Add($"15V_VBIT 解析失败: 0x{FormatBytesHex(resp15)}");
                    return true;
                }

                token.ThrowIfCancellationRequested();

                AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试：读取二次电源回采值(5V_VBIT)");
                var resp5 = await _arinc.SendBenchCommandAndWaitAsync(
                    SetVoltageTxChannel,
                    TelemetryRxChannel,
                    DefaultLabel,
                    Req5vVbit,
                    Is5vVbitPayload,
                    timeoutMs: 2000,
                    msg => AddLog(msg),
                    token);

                if (resp5 == null)
                    return false;

                if (!TryParseSingleVbitValue(resp5, Req5vVbit, out var v5))
                {
                    failures.Add($"5V_VBIT 解析失败: 0x{FormatBytesHex(resp5)}");
                    return true;
                }

                TelemetryRxDataText = $"15V:0x{FormatBytesHex(resp15)}  5V:0x{FormatBytesHex(resp5)}";
                TelemetryVoltageText = $"15V={v15:0.000}V, 5V={v5:0.000}V";

                if (!IsWithin(v15, 2.375, 2.625))
                    failures.Add($"15V_VBIT={v15:0.000}V 不在[2.375,2.625]V");
                if (!IsWithin(v5, 2.375, 2.625))
                    failures.Add($"5V_VBIT={v5:0.000}V 不在[2.375,2.625]V");

                return true;
            }
            finally
            {
                _arincOpLock.Release();
            }
        }

        private async Task OnSendEnterAtpAsync()
        {
            await _arincOpLock.WaitAsync();
            try
            {
                EnterAtpRxDataText = "--";
                AddLog($"[{DateTime.Now:HH:mm:ss}] 发送：进入ATP，TX={EnterAtpTxChannel}, RX={EnterAtpRxChannel}, Label=0x{DefaultLabel:X2}");

                await EnsurePowerSupplyConnectedAsync(CancellationToken.None);
                await _arinc.StartAsync(EnterAtpTxChannel, EnterAtpRxChannel, msg => AddLog(msg));

                try { await _arinc.ClearRxFifoAsync(EnterAtpRxChannel); } catch { }
                await Task.Delay(50);

                var resp = await _arinc.SendBenchCommandAndWaitAsync(
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

                EnterAtpRxDataText = "0x" + FormatBytesHex(resp);
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

        private async Task OnSendAb28vSupplyAsync()
        {
            await _arincOpLock.WaitAsync();
            try
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 发送：AB_28V_SUPPLY，TX={SetVoltageTxChannel}, RX={TelemetryRxChannel}, Label=0x{DefaultLabel:X2}");
                TelemetryRxDataText = "--";
                TelemetryVoltageText = "--";

                await EnsurePowerSupplyConnectedAsync(CancellationToken.None);
                await _arinc.StartAsync(EnterAtpTxChannel, EnterAtpRxChannel, msg => AddLog(msg));

                try { await _arinc.ClearRxFifoAsync(TelemetryRxChannel); } catch { }
                await Task.Delay(30);

                await _arinc.SendBenchCommandOnlyAsync(
                    SetVoltageTxChannel,
                    DefaultLabel,
                    Ab28vSupply,
                    msg => AddLog(msg),
                    CancellationToken.None);

                AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试：读取二次电源回采值(15V_VBIT)");
                var resp15 = await _arinc.SendBenchCommandAndWaitAsync(
                    SetVoltageTxChannel,
                    TelemetryRxChannel,
                    DefaultLabel,
                    Req15vVbit,
                    Is15vVbitPayload,
                    timeoutMs: 2000,
                    msg => AddLog(msg),
                    CancellationToken.None);

                if (resp15 == null)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 15V_VBIT 回采超时");
                    return;
                }

                AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试：读取二次电源回采值(5V_VBIT)");
                var resp5 = await _arinc.SendBenchCommandAndWaitAsync(
                    SetVoltageTxChannel,
                    TelemetryRxChannel,
                    DefaultLabel,
                    Req5vVbit,
                    Is5vVbitPayload,
                    timeoutMs: 2000,
                    msg => AddLog(msg),
                    CancellationToken.None);

                if (resp5 == null)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 5V_VBIT 回采超时");
                    return;
                }

                if (TryParseSingleVbitValue(resp15, Req15vVbit, out var v15) && TryParseSingleVbitValue(resp5, Req5vVbit, out var v5))
                {
                    TelemetryRxDataText = $"15V:0x{FormatBytesHex(resp15)}  5V:0x{FormatBytesHex(resp5)}";
                    TelemetryVoltageText = $"15V={v15:0.000}V, 5V={v5:0.000}V";
                }
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] AB_28V_SUPPLY 异常：{ex.Message}");
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
                AddLog($"[{DateTime.Now:HH:mm:ss}] 发送：退出ATP，TX={ExitAtpTxChannel}, RX={ExitAtpRxChannel}, Label=0x{DefaultLabel:X2}");

                await EnsurePowerSupplyConnectedAsync(CancellationToken.None);
                await _arinc.StartAsync(EnterAtpTxChannel, EnterAtpRxChannel, msg => AddLog(msg));

                try { await _arinc.ClearRxFifoAsync(ExitAtpRxChannel); } catch { }
                await Task.Delay(50);

                var resp = await _arinc.SendBenchCommandAndWaitAsync(
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

                ExitAtpRxDataText = "0x" + FormatBytesHex(resp);
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

        private void AddLog(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

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

        private async Task EnsurePowerSupplyConnectedAsync(CancellationToken token)
        {
            await _powerSupplyLock.WaitAsync(token);
            try
            {
                if (_powerSupply != null && _powerSupply.IsConnected)
                    return;

                if (_powerSupply != null)
                {
                    try { await _powerSupply.DisposeAsync(); } catch { }
                    _powerSupply = null;
                }

                _powerSupply = new PowerSupplySocketApi();
                await _powerSupply.ConnectAsync(PowerSupplyIpAddress, token);
                AddLog($"[{DateTime.Now:HH:mm:ss}] 程控电源已连接：{PowerSupplyIpAddress}");
            }
            finally
            {
                _powerSupplyLock.Release();
            }
        }

        private async Task DisconnectPowerSupplyAsync(CancellationToken token)
        {
            await _powerSupplyLock.WaitAsync(token);
            try
            {
                if (_powerSupply == null)
                    return;

                try { await _powerSupply.DisconnectAsync(token); } catch { }
                try { await _powerSupply.DisposeAsync(); } catch { }
                _powerSupply = null;
                AddLog($"[{DateTime.Now:HH:mm:ss}] 程控电源已断开");
            }
            finally
            {
                _powerSupplyLock.Release();
            }
        }

        private static PowerSupplyChannel MapPowerSupplyChannel(string dmmChannel)
        {
            // UI uses Port1/Port2. Map them to CH1/CH2.
            return string.Equals(dmmChannel, "Port2", StringComparison.OrdinalIgnoreCase)
                ? PowerSupplyChannel.CH2
                : PowerSupplyChannel.CH1;
        }

        private async Task PowerSupplyApplyAsync(double voltage, double currentLimit, CancellationToken token)
        {
            await EnsurePowerSupplyConnectedAsync(token);

            var ch = MapPowerSupplyChannel(DmmChannel);
            await _powerSupply.ApplyAsync(ch, voltage, currentLimit, token);
            await _powerSupply.SetOutputEnabledAsync(ch, true, token);
        }

        private async Task PowerSupplyOutputOffAsync(CancellationToken token)
        {
            if (_powerSupply == null || !_powerSupply.IsConnected)
                return;

            var ch = MapPowerSupplyChannel(DmmChannel);
            await _powerSupply.SetOutputEnabledAsync(ch, false, token);
        }

        private async Task<PowerSupplyMeasurements> PowerSupplyReadMeasurementsAsync(CancellationToken token)
        {
            if (_powerSupply == null || !_powerSupply.IsConnected)
                return null;

            var ch = MapPowerSupplyChannel(DmmChannel);
            return await _powerSupply.ReadMeasurementsAsync(ch, options: null, cancellationToken: token);
        }

        private static bool Is15vVbitPayload(byte[] frame)
        {
            return IsPrefix(frame, Req15vVbit);
        }

        private static bool Is5vVbitPayload(byte[] frame)
        {
            return IsPrefix(frame, Req5vVbit);
        }

        private static bool IsPrefix(byte[] frame, byte[] prefix8)
        {
            if (frame == null || frame.Length != 8)
                return false;
            if (prefix8 == null || prefix8.Length != 8)
                return false;

            for (int i = 0; i < 4; i++)
            {
                if (frame[i] != prefix8[i])
                    return false;
            }

            return true;
        }

        private static bool TryParseSingleVbitValue(byte[] frame, byte[] prefix8, out double value)
        {
            value = 0;
            if (!IsPrefix(frame, prefix8))
                return false;

            try
            {
                var fbytes = new byte[4] { frame[4], frame[5], frame[6], frame[7] };
                if (BitConverter.IsLittleEndian)
                    Array.Reverse(fbytes);
                float f = BitConverter.ToSingle(fbytes, 0);
                if (!float.IsNaN(f) && !float.IsInfinity(f) && f > -1000 && f < 1000)
                {
                    value = f;
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private static bool IsWithin(double value, double min, double max)
        {
            return value >= min && value <= max;
        }

        private static string FormatBytesHex(byte[] data)
        {
            if (data == null || data.Length == 0)
                return string.Empty;

            return string.Join(" ", data.Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));
        }
    }
}
