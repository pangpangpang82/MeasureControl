using Prism.Commands;
using Prism.Mvvm;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using MeasureControl.Helpers;
using MeasureControl.Models.Devices;
using MeasureControl.Services;
using MeasureControl.Services.HardwareApis;

namespace MeasureControl.ViewModels.SingleBoardTest.AirController
{
    public sealed class B_C_7_4_6_1ViewModel : BindableBase, IDisposable
    {
        private const string DefaultFpgaIpAddress = "192.168.1.10";
        private const int DefaultFpgaPort = 5001;

        private const string PowerSupply28VIpAddress = "192.168.1.15";
        private const string PowerSupply3V3IpAddress = "192.168.1.16";

        private const PowerSupplyChannel PowerSupply28VCh1 = PowerSupplyChannel.CH1;
        private const PowerSupplyChannel PowerSupply28VCh2 = PowerSupplyChannel.CH2;
        private const double Power28VVoltage = 28.0;
        private const double Power28VCurrentLimit = 3.0;

        private const PowerSupplyChannel PowerSupply3V3Channel = PowerSupplyChannel.CH3;
        private const double Power3V3Voltage = 3.3;
        private const double Power3V3CurrentLimit = 1.0;

        private const string DefaultMatrixIpAddress = "192.168.1.3";
        private const int DefaultMatrixTcpBasePort = 50200;

        private const int Chassis2Slot2601_1 = 4; // 2601(1)
        private const int Chassis2Slot2601_2 = 6; // 2601(2)

        // 2601(2) 1/14: route measurement signal between J33 and J27
        private const string Slot2601_2_In = "I1";
        private const string Slot2601_2_Out = "O14";

        // 2601(1) 0/2: route to scope CH1
        private const string Slot2601_1_In = "I0";
        private const string Slot2601_1_Out = "O2";

        private const int DefaultScopePort = 5555;
        private const string DefaultScopeIpAddress = "192.168.1.18";

        private static readonly byte[] DeviceInitCommandFrame = { 0xAA, 0x55, 0x02, 0x02, 0x01 };
        private static readonly byte[] StepperPulseCommandFrame = { 0xAA, 0x55, 0x06, 0x04, 0x60, 0xE8, 0x03, 0xF4, 0x01 };

        private const double ExpectedFrequencyHz = 250.0; // 1000/4
        private const double FrequencyToleranceHz = 1.0;

        private readonly SemaphoreSlim _manualTestLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _autoTestLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _opLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _instrumentLock = new SemaphoreSlim(1, 1);

        private CancellationTokenSource _autoTestCts;

        private FpgaTcpClient _fpga;

        private IPowerSupplyApi _powerSupply28V;
        private IPowerSupplyApi _powerSupply3V3;

        private TcpClient _scopeTcpClient;
        private NetworkStream _scopeTcpStream;

        private bool _isMatrixRouted;
        private bool _matrixRouted2601_2;
        private bool _matrixRouted2601_1;

        private bool _isManualTestRunning;
        private bool _isAutoTestRunning;
        private bool _isBusy;

        private bool _isPowerOn;
        private string _powerStatus = "未上电";

        private string _fpgaIpAddress = DefaultFpgaIpAddress;
        private int _fpgaPort = DefaultFpgaPort;

        private string _frequencyText = "--";
        private string _result = "--";

        private string _lastTestTime = "--";
        private string _lastTestResult = "--";

        public B_C_7_4_6_1ViewModel()
        {
            ManualTestCommand = new DelegateCommand(OnManualTest, () => !IsBusy && !IsAutoTestRunning);
            AutoTestCommand = new DelegateCommand(OnAutoTest, () => !IsBusy && !IsManualTestRunning);
            ClearLogCommand = new DelegateCommand(() => Logs.Clear());

            SendCommandCommand = new DelegateCommand(async () => await SendCommandAsync(CancellationToken.None), () => !IsBusy && IsManualTestRunning);
            MeasureCommand = new DelegateCommand(async () => await MeasureAsync(CancellationToken.None), () => !IsBusy && IsManualTestRunning);
        }

        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();

        public DelegateCommand ManualTestCommand { get; }
        public DelegateCommand AutoTestCommand { get; }
        public DelegateCommand ClearLogCommand { get; }

        public DelegateCommand SendCommandCommand { get; }
        public DelegateCommand MeasureCommand { get; }

        public bool IsManualTestRunning
        {
            get => _isManualTestRunning;
            private set
            {
                if (SetProperty(ref _isManualTestRunning, value))
                    RaiseAllCanExecuteChanged();
            }
        }

        public bool IsAutoTestRunning
        {
            get => _isAutoTestRunning;
            private set
            {
                if (SetProperty(ref _isAutoTestRunning, value))
                    RaiseAllCanExecuteChanged();
            }
        }

        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (SetProperty(ref _isBusy, value))
                    RaiseAllCanExecuteChanged();
            }
        }

        public string PowerStatus
        {
            get => _powerStatus;
            private set => SetProperty(ref _powerStatus, value);
        }

        public bool IsPowerOn
        {
            get => _isPowerOn;
            private set => SetProperty(ref _isPowerOn, value);
        }

        public string FpgaIpAddress
        {
            get => _fpgaIpAddress;
            set => SetProperty(ref _fpgaIpAddress, value);
        }

        public int FpgaPort
        {
            get => _fpgaPort;
            set => SetProperty(ref _fpgaPort, value);
        }

        public string FrequencyText
        {
            get => _frequencyText;
            private set => SetProperty(ref _frequencyText, value);
        }

        public string Result
        {
            get => _result;
            private set => SetProperty(ref _result, value);
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

        private void RaiseAllCanExecuteChanged()
        {
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                ManualTestCommand?.RaiseCanExecuteChanged();
                AutoTestCommand?.RaiseCanExecuteChanged();
                SendCommandCommand?.RaiseCanExecuteChanged();
                MeasureCommand?.RaiseCanExecuteChanged();
            });
        }

        private void AddLog(string msg)
        {
            var line = $"[{DateTime.Now:HH:mm:ss.fff}] {msg}";
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                Logs.Add(line);
                while (Logs.Count > 500)
                    Logs.RemoveAt(0);
            });
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

        private async Task StartManualTestAsync()
        {
            await _manualTestLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (IsManualTestRunning)
                    return;

                ClearResults();
                IsManualTestRunning = true;
                AddLog("========== 手动测试开始 ==========");

                IsBusy = true;
                try
                {
                    await PowerOnAsync(CancellationToken.None).ConfigureAwait(false);
                    await EnsureFpgaConnectedAsync(CancellationToken.None).ConfigureAwait(false);
                }
                finally
                {
                    IsBusy = false;
                }
            }
            catch (Exception ex)
            {
                AddLog($"手动测试启动异常: {ex.Message}");
                Application.Current?.Dispatcher?.Invoke(() => IsManualTestRunning = false);
            }
            finally
            {
                _manualTestLock.Release();
            }
        }

        private async Task StopManualTestAsync()
        {
            await _manualTestLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!IsManualTestRunning)
                    return;

                IsBusy = true;
                try
                {
                    await CleanupAsync(CancellationToken.None).ConfigureAwait(false);
                    if (IsPowerOn)
                        await PowerOffAsync(CancellationToken.None).ConfigureAwait(false);
                }
                finally
                {
                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        IsManualTestRunning = false;
                        IsBusy = false;
                    });
                    AddLog("========== 手动测试已停止 ==========");
                }
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
                _autoTestCts?.Cancel();
                return;
            }

            _ = StartAutoTestAsync();
        }

        private async Task StartAutoTestAsync()
        {
            await _autoTestLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (IsAutoTestRunning)
                    return;

                ClearResults();
                IsAutoTestRunning = true;
                _autoTestCts = new CancellationTokenSource();
                var token = _autoTestCts.Token;

                try
                {
                    IsBusy = true;
                    try
                    {
                        await PowerOnAsync(token).ConfigureAwait(false);
                        await EnsureFpgaConnectedAsync(token).ConfigureAwait(false);
                    }
                    finally
                    {
                        IsBusy = false;
                    }

                    AddLog("========== 自动测试开始 ==========");

                    var okSend = await SendCommandAsync(token).ConfigureAwait(false);
                    if (!okSend)
                    {
                        SetLastTestResult("FAIL");
                        return;
                    }

                    AddLog("等待500ms后开始波形检测...");
                    await Task.Delay(500, token).ConfigureAwait(false);

                    var (okMeasure, freq) = await MeasureFrequencyAsync(token).ConfigureAwait(false);
                    FrequencyText = freq.HasValue ? $"{freq.Value:F3} Hz" : "--";

                    var pass = okMeasure && freq.HasValue && Math.Abs(freq.Value - ExpectedFrequencyHz) <= FrequencyToleranceHz;
                    Result = pass ? "PASS" : "FAIL";
                    SetLastTestResult(pass ? "PASS" : "FAIL");

                    AddLog($"频率测量: {FrequencyText}  判据: ({ExpectedFrequencyHz:F0}±{FrequencyToleranceHz:F0})Hz  => {Result}");
                    AddLog($"========== 自动测试完成: {(pass ? "PASS" : "FAIL")} ==========");
                }
                catch (OperationCanceledException)
                {
                    AddLog("自动测试已取消");
                }
                catch (Exception ex)
                {
                    AddLog($"自动测试异常: {ex.Message}");
                }
                finally
                {
                    try { await CleanupAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { if (IsPowerOn) await PowerOffAsync(CancellationToken.None).ConfigureAwait(false); } catch { }

                    IsAutoTestRunning = false;
                    try { _autoTestCts?.Dispose(); } catch { }
                    _autoTestCts = null;
                }
            }
            finally
            {
                _autoTestLock.Release();
            }
        }

        private void SetLastTestResult(string result)
        {
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                LastTestResult = result;
                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            });
        }

        private void ClearResults()
        {
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                FrequencyText = "--";
                Result = "--";
                LastTestTime = "--";
                LastTestResult = "--";
            });
        }

        private async Task PowerOnAsync(CancellationToken token)
        {
            AddLog("组件供电：上电中...");

            await EnsurePowerSupply28VConnectedAsync(token).ConfigureAwait(false);
            try
            {
                await _powerSupply28V.ApplyAsync(PowerSupply28VCh1, Power28VVoltage, Power28VCurrentLimit, token).ConfigureAwait(false);
                await _powerSupply28V.ApplyAsync(PowerSupply28VCh2, Power28VVoltage, Power28VCurrentLimit, token).ConfigureAwait(false);
                await _powerSupply28V.SetOutputEnabledAsync(PowerSupply28VCh1, true, token).ConfigureAwait(false);
                await _powerSupply28V.SetOutputEnabledAsync(PowerSupply28VCh2, true, token).ConfigureAwait(false);
                await Task.Delay(300, token).ConfigureAwait(false);
                AddLog($"组件供电：28V 上电 CH1+CH2, IP={PowerSupply28VIpAddress}");
            }
            catch (Exception ex)
            {
                AddLog($"组件供电：28V 上电失败: {ex.Message}");
            }

            await EnsurePowerSupply3V3ConnectedAsync(token).ConfigureAwait(false);
            try
            {
                await _powerSupply3V3.ApplyAsync(PowerSupply3V3Channel, Power3V3Voltage, Power3V3CurrentLimit, token).ConfigureAwait(false);
                await _powerSupply3V3.SetOutputEnabledAsync(PowerSupply3V3Channel, true, token).ConfigureAwait(false);
                await Task.Delay(200, token).ConfigureAwait(false);
                AddLog($"组件供电：3.3V 上电 CH3, IP={PowerSupply3V3IpAddress}");
            }
            catch (Exception ex)
            {
                AddLog($"组件供电：3.3V 上电失败: {ex.Message}");
            }

            Application.Current?.Dispatcher?.Invoke(() =>
            {
                IsPowerOn = true;
                PowerStatus = "已上电";
            });
        }

        private async Task PowerOffAsync(CancellationToken token)
        {
            AddLog("组件供电：下电中...");

            try { await UnrouteMatrixAsync(token).ConfigureAwait(false); } catch { }

            try
            {
                if (_powerSupply3V3 != null)
                    await _powerSupply3V3.SetOutputEnabledAsync(PowerSupply3V3Channel, false, token).ConfigureAwait(false);
            }
            catch { }

            try
            {
                if (_powerSupply28V != null)
                {
                    await _powerSupply28V.SetOutputEnabledAsync(PowerSupply28VCh1, false, token).ConfigureAwait(false);
                    await _powerSupply28V.SetOutputEnabledAsync(PowerSupply28VCh2, false, token).ConfigureAwait(false);
                }
            }
            catch { }

            Application.Current?.Dispatcher?.Invoke(() =>
            {
                IsPowerOn = false;
                PowerStatus = "未上电";
            });
        }

        private async Task EnsurePowerSupply28VConnectedAsync(CancellationToken token)
        {
            if (_powerSupply28V != null && _powerSupply28V.IsConnected)
                return;

            _powerSupply28V ??= new PowerSupplySocketApi();
            await _powerSupply28V.ConnectAsync(PowerSupply28VIpAddress, token).ConfigureAwait(false);
        }

        private async Task EnsurePowerSupply3V3ConnectedAsync(CancellationToken token)
        {
            if (_powerSupply3V3 != null && _powerSupply3V3.IsConnected)
                return;

            _powerSupply3V3 ??= new PowerSupplySocketApi();
            await _powerSupply3V3.ConnectAsync(PowerSupply3V3IpAddress, token).ConfigureAwait(false);
        }

        private async Task EnsureFpgaConnectedAsync(CancellationToken token)
        {
            if (_fpga != null && _fpga.IsConnected)
                return;

            _fpga?.Dispose();
            _fpga = new FpgaTcpClient();
            await _fpga.ConnectAsync(FpgaIpAddress, FpgaPort, token).ConfigureAwait(false);
            AddLog("FPGA连接成功");
        }

        private async Task<bool> SendCommandAsync(CancellationToken token)
        {
            await _opLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                IsBusy = true;
                try
                {
                    await EnsureFpgaConnectedAsync(token).ConfigureAwait(false);

                    AddLog($"FPGA发送指令(设备初始化): {FormatData(DeviceInitCommandFrame)}");
                    await _fpga.WriteAsync(DeviceInitCommandFrame, 0, DeviceInitCommandFrame.Length, token).ConfigureAwait(false);

                    AddLog($"FPGA发送指令(STEP脉冲1000Hz): {FormatData(StepperPulseCommandFrame)}");
                    await _fpga.WriteAsync(StepperPulseCommandFrame, 0, StepperPulseCommandFrame.Length, token).ConfigureAwait(false);
                    return true;
                }
                finally
                {
                    IsBusy = false;
                }
            }
            catch (Exception ex)
            {
                AddLog($"FPGA发送失败: {ex.Message}");
                return false;
            }
            finally
            {
                _opLock.Release();
            }
        }

        private async Task MeasureAsync(CancellationToken token)
        {
            if (!IsManualTestRunning)
                return;

            await _opLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                IsBusy = true;
                try
                {
                    var (ok, freq) = await MeasureFrequencyAsync(token).ConfigureAwait(false);
                    FrequencyText = freq.HasValue ? $"{freq.Value:F3} Hz" : "--";

                    var pass = ok && freq.HasValue && Math.Abs(freq.Value - ExpectedFrequencyHz) <= FrequencyToleranceHz;
                    Result = pass ? "PASS" : "FAIL";
                    SetLastTestResult(pass ? "PASS" : "FAIL");

                    if (!ok)
                        AddLog("频率测量失败");
                    else
                        AddLog($"频率测量: {FrequencyText}  判据: ({ExpectedFrequencyHz:F0}±{FrequencyToleranceHz:F0})Hz  => {Result}");
                }
                finally
                {
                    IsBusy = false;
                }
            }
            finally
            {
                _opLock.Release();
            }
        }

        private async Task<(bool ok, double? freq)> MeasureFrequencyAsync(CancellationToken token)
        {
            await _instrumentLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                var matrix = MatrixControlService.Instance;

                bool ok1 = false;
                bool ok2 = false;

                try
                {
                    if (_isMatrixRouted)
                        await UnrouteMatrixAsync(token).ConfigureAwait(false);

                    AddLog($"路由测频：2601(2) {Slot2601_2_In}-{Slot2601_2_Out} + 2601(1) {Slot2601_1_In}-{Slot2601_1_Out}");

                    ok1 = await matrix.ConnectNodesAsync(Slot2601_2_In, Slot2601_2_Out, Chassis2Slot2601_2, DefaultMatrixIpAddress, DefaultMatrixTcpBasePort).ConfigureAwait(false);
                    ok2 = await matrix.ConnectNodesAsync(Slot2601_1_In, Slot2601_1_Out, Chassis2Slot2601_1, DefaultMatrixIpAddress, DefaultMatrixTcpBasePort).ConfigureAwait(false);

                    _matrixRouted2601_2 = ok1;
                    _matrixRouted2601_1 = ok2;
                    _isMatrixRouted = ok1 && ok2;

                    AddLog($"路由测频：2601(2) {(ok1 ? "OK" : "FAIL")}, 2601(1) {(ok2 ? "OK" : "FAIL")}");
                    if (!_isMatrixRouted)
                        return (false, null);

                    await EnsureScopeConnectedAsync(token).ConfigureAwait(false);
                    await Task.Delay(200, token).ConfigureAwait(false);

                    var freq = await QueryScopeDoubleAsync(":MEASure:ITEM? FREQuency", token).ConfigureAwait(false);
                    return (freq.HasValue, freq);
                }
                finally
                {
                    await UnrouteMatrixAsync(token).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                AddLog($"测量异常: {ex.Message}");
                return (false, null);
            }
            finally
            {
                _instrumentLock.Release();
            }
        }

        private async Task UnrouteMatrixAsync(CancellationToken token)
        {
            if (!_isMatrixRouted && !_matrixRouted2601_1 && !_matrixRouted2601_2)
                return;

            var matrix = MatrixControlService.Instance;

            try
            {
                if (_matrixRouted2601_1)
                    _ = await matrix.DisconnectNodesAsync(Slot2601_1_In, Slot2601_1_Out, Chassis2Slot2601_1, DefaultMatrixIpAddress, DefaultMatrixTcpBasePort).ConfigureAwait(false);
            }
            catch { }

            try
            {
                if (_matrixRouted2601_2)
                    _ = await matrix.DisconnectNodesAsync(Slot2601_2_In, Slot2601_2_Out, Chassis2Slot2601_2, DefaultMatrixIpAddress, DefaultMatrixTcpBasePort).ConfigureAwait(false);
            }
            catch { }

            _isMatrixRouted = false;
            _matrixRouted2601_1 = false;
            _matrixRouted2601_2 = false;
        }

        private async Task EnsureScopeConnectedAsync(CancellationToken token)
        {
            if (_scopeTcpClient != null && _scopeTcpStream != null)
                return;

            _scopeTcpClient = new TcpClient();
            await _scopeTcpClient.ConnectAsync(DefaultScopeIpAddress, DefaultScopePort).ConfigureAwait(false);
            _scopeTcpStream = _scopeTcpClient.GetStream();

            try
            {
                _scopeTcpStream.ReadTimeout = 5000;
                _scopeTcpStream.WriteTimeout = 5000;
            }
            catch
            {
            }

            await QueryScopeStringAsync(":MEASure:SOURce CHANnel1", token).ConfigureAwait(false);
            await QueryScopeStringAsync(":MEASure:CLEar", token).ConfigureAwait(false);
            AddLog("示波器连接成功");
        }

        private async Task<double?> QueryScopeDoubleAsync(string command, CancellationToken token)
        {
            var s = await QueryScopeStringAsync(command, token).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(s))
                return null;

            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                return v;

            return null;
        }

        private async Task<string> QueryScopeStringAsync(string command, CancellationToken token)
        {
            if (_scopeTcpStream == null)
                throw new InvalidOperationException("示波器未连接");

            var cmd = Encoding.ASCII.GetBytes(command + "\n");
            await _scopeTcpStream.WriteAsync(cmd, 0, cmd.Length, token).ConfigureAwait(false);
            await _scopeTcpStream.FlushAsync(token).ConfigureAwait(false);

            if (!command.TrimEnd().EndsWith("?", StringComparison.Ordinal))
                return string.Empty;

            return await ReadLineAsync(_scopeTcpStream, 5000, token).ConfigureAwait(false);
        }

        private static async Task<string> ReadLineAsync(NetworkStream stream, int timeoutMs, CancellationToken token)
        {
            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                cts.CancelAfter(timeoutMs);

                var sb = new StringBuilder();
                var buffer = new byte[1];

                while (true)
                {
                    int n = await stream.ReadAsync(buffer, 0, 1, cts.Token).ConfigureAwait(false);
                    if (n <= 0)
                        break;

                    char ch = (char)buffer[0];
                    if (ch == '\n')
                        break;
                    if (ch == '\r')
                        continue;
                    sb.Append(ch);

                    if (sb.Length > 4096)
                        break;
                }

                return sb.ToString().Trim();
            }
        }

        private async Task CleanupAsync(CancellationToken token)
        {
            try { await UnrouteMatrixAsync(token).ConfigureAwait(false); } catch { }

            try { _scopeTcpStream?.Dispose(); } catch { }
            _scopeTcpStream = null;
            try { _scopeTcpClient?.Close(); } catch { }
            try { _scopeTcpClient?.Dispose(); } catch { }
            _scopeTcpClient = null;

            try { _fpga?.Dispose(); } catch { }
            _fpga = null;
        }

        private static string FormatData(byte[] data)
        {
            if (data == null)
                return "--";
            return BitConverter.ToString(data).Replace("-", " ");
        }

        public void Dispose()
        {
            try { _autoTestCts?.Cancel(); } catch { }
            try { _autoTestCts?.Dispose(); } catch { }
            _autoTestCts = null;

            try { CleanupAsync(CancellationToken.None).GetAwaiter().GetResult(); } catch { }
            try { if (IsPowerOn) PowerOffAsync(CancellationToken.None).GetAwaiter().GetResult(); } catch { }

            try { _manualTestLock?.Dispose(); } catch { }
            try { _autoTestLock?.Dispose(); } catch { }
            try { _opLock?.Dispose(); } catch { }
            try { _instrumentLock?.Dispose(); } catch { }
        }
    }
}
