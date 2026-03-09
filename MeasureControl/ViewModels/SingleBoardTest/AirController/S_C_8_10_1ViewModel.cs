using Prism.Commands;
using Prism.Mvvm;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using MeasureControl.Helpers;
using MeasureControl.Services;

namespace MeasureControl.ViewModels.SingleBoardTest.AirController
{
    public sealed class S_C_8_10_1ViewModel : BindableBase, IDisposable
    {
        private const string DefaultFpgaIpAddress = "127.0.0.1";
        private const int DefaultFpgaPort = 9000;

        private const string DefaultMatrixIpAddress = "192.168.1.3";
        private const int DefaultMatrixTcpBasePort = 50200;

        private const int Chassis2Slot6 = 6;
        private const int Chassis2Slot4 = 4;

        private const string Slot4Common = "I0";
        private const string Slot4ToScopeCh1 = "O2";

        private const string Slot6Row1 = "I1";
        private const string J8Route = "O14";   // r1c14
        private const string J9Route = "O15";   // r1c15
        private const string J8J9Route = "O16"; // r1c16

        private const int DefaultScopePort = 5555;
        private const string DefaultScopeIpAddress = "192.168.1.18";

        private readonly SemaphoreSlim _manualTestLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _autoTestLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _opLock = new SemaphoreSlim(1, 1);

        private CancellationTokenSource _autoTestCts;

        private FpgaTcpClient _fpga;

        private TcpClient _scopeTcpClient;
        private NetworkStream _scopeTcpStream;

        private bool _isManualTestRunning;
        private bool _isAutoTestRunning;
        private bool _isBusy;

        private int _pwmFrequencyHz = 2000;
        private int _pwmDutyPct = 50;

        private string _lastTestTime = "--";
        private string _lastTestResult = "--";

        private string _pwmCustomResult = "--";
        private string _pwm100Result = "--";
        private string _pwm50Result = "--";
        private string _pwm0Result = "--";

        private bool _isMeasuringPwmCustom;
        private bool _isMeasuringPwm100;
        private bool _isMeasuringPwm50;
        private bool _isMeasuringPwm0;

        private string _j8VmaxText = "--";
        private string _j8VminText = "--";
        private string _j8VppText = "--";
        private string _j8DutyPctText = "--";

        private string _j9VmaxText = "--";
        private string _j9VminText = "--";
        private string _j9VppText = "--";
        private string _j9DutyPctText = "--";

        private string _j8j9VmaxText = "--";
        private string _j8j9VminText = "--";
        private string _j8j9VppText = "--";
        private string _j8j9DutyPctText = "--";

        public S_C_8_10_1ViewModel()
        {
            ManualTestCommand = new DelegateCommand(OnManualTest);
            AutoTestCommand = new DelegateCommand(OnAutoTest);
            ClearLogCommand = new DelegateCommand(() => Logs.Clear());

            SendPwmCustomCommand = new DelegateCommand(async () => await SendAndMeasureWithUiStateAsync(PwmDutyPct, PwmUiButton.Custom));
            SendPwm100Command = new DelegateCommand(async () => await SendAndMeasureWithUiStateAsync(100, PwmUiButton.Pwm100));
            SendPwm50Command = new DelegateCommand(async () => await SendAndMeasureWithUiStateAsync(50, PwmUiButton.Pwm50));
            SendPwm0Command = new DelegateCommand(async () => await SendAndMeasureWithUiStateAsync(0, PwmUiButton.Pwm0));
        }

        private enum PwmUiButton
        {
            Custom,
            Pwm100,
            Pwm50,
            Pwm0
        }

        private async Task SendAndMeasureWithUiStateAsync(int dutyPct, PwmUiButton button)
        {
            await Application.Current.Dispatcher.InvokeAsync(() => SetMeasuring(button, true));
            try
            {
                await SendAndMeasureAsync(dutyPct, CancellationToken.None);
            }
            finally
            {
                await Application.Current.Dispatcher.InvokeAsync(() => SetMeasuring(button, false));
            }
        }

        private void SetMeasuring(PwmUiButton button, bool value)
        {
            switch (button)
            {
                case PwmUiButton.Custom:
                    IsMeasuringPwmCustom = value;
                    break;
                case PwmUiButton.Pwm100:
                    IsMeasuringPwm100 = value;
                    break;
                case PwmUiButton.Pwm50:
                    IsMeasuringPwm50 = value;
                    break;
                case PwmUiButton.Pwm0:
                    IsMeasuringPwm0 = value;
                    break;
            }
        }

        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();

        public DelegateCommand ManualTestCommand { get; }
        public DelegateCommand AutoTestCommand { get; }
        public DelegateCommand ClearLogCommand { get; }

        public DelegateCommand SendPwmCustomCommand { get; }
        public DelegateCommand SendPwm100Command { get; }
        public DelegateCommand SendPwm50Command { get; }
        public DelegateCommand SendPwm0Command { get; }

        public bool IsManualTestRunning
        {
            get => _isManualTestRunning;
            private set => SetProperty(ref _isManualTestRunning, value);
        }

        public bool IsAutoTestRunning
        {
            get => _isAutoTestRunning;
            private set => SetProperty(ref _isAutoTestRunning, value);
        }

        public bool IsBusy
        {
            get => _isBusy;
            private set => SetProperty(ref _isBusy, value);
        }

        public int PwmFrequencyHz
        {
            get => _pwmFrequencyHz;
            set => SetProperty(ref _pwmFrequencyHz, value);
        }

        public int PwmDutyPct
        {
            get => _pwmDutyPct;
            set => SetProperty(ref _pwmDutyPct, value);
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

        public string PwmCustomResult
        {
            get => _pwmCustomResult;
            private set => SetProperty(ref _pwmCustomResult, value);
        }

        public string Pwm100Result
        {
            get => _pwm100Result;
            private set => SetProperty(ref _pwm100Result, value);
        }

        public string Pwm50Result
        {
            get => _pwm50Result;
            private set => SetProperty(ref _pwm50Result, value);
        }

        public string Pwm0Result
        {
            get => _pwm0Result;
            private set => SetProperty(ref _pwm0Result, value);
        }

        public bool IsMeasuringPwmCustom
        {
            get => _isMeasuringPwmCustom;
            private set => SetProperty(ref _isMeasuringPwmCustom, value);
        }

        public bool IsMeasuringPwm100
        {
            get => _isMeasuringPwm100;
            private set => SetProperty(ref _isMeasuringPwm100, value);
        }

        public bool IsMeasuringPwm50
        {
            get => _isMeasuringPwm50;
            private set => SetProperty(ref _isMeasuringPwm50, value);
        }

        public bool IsMeasuringPwm0
        {
            get => _isMeasuringPwm0;
            private set => SetProperty(ref _isMeasuringPwm0, value);
        }

        public string J8VmaxText
        {
            get => _j8VmaxText;
            private set => SetProperty(ref _j8VmaxText, value);
        }

        public string J8VminText
        {
            get => _j8VminText;
            private set => SetProperty(ref _j8VminText, value);
        }

        public string J8VppText
        {
            get => _j8VppText;
            private set => SetProperty(ref _j8VppText, value);
        }

        public string J8DutyPctText
        {
            get => _j8DutyPctText;
            private set => SetProperty(ref _j8DutyPctText, value);
        }

        public string J9VmaxText
        {
            get => _j9VmaxText;
            private set => SetProperty(ref _j9VmaxText, value);
        }

        public string J9VminText
        {
            get => _j9VminText;
            private set => SetProperty(ref _j9VminText, value);
        }

        public string J9VppText
        {
            get => _j9VppText;
            private set => SetProperty(ref _j9VppText, value);
        }

        public string J9DutyPctText
        {
            get => _j9DutyPctText;
            private set => SetProperty(ref _j9DutyPctText, value);
        }

        public string J8J9VmaxText
        {
            get => _j8j9VmaxText;
            private set => SetProperty(ref _j8j9VmaxText, value);
        }

        public string J8J9VminText
        {
            get => _j8j9VminText;
            private set => SetProperty(ref _j8j9VminText, value);
        }

        public string J8J9VppText
        {
            get => _j8j9VppText;
            private set => SetProperty(ref _j8j9VppText, value);
        }

        public string J8J9DutyPctText
        {
            get => _j8j9DutyPctText;
            private set => SetProperty(ref _j8j9DutyPctText, value);
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
                    ClearResults();
                    IsManualTestRunning = true;

                    try
                    {
                        var api = Prism.Ioc.ContainerLocator.Container.Resolve(typeof(MeasureControl.Services.HardwareApis.IComponentPowerStateApi)) as MeasureControl.Services.HardwareApis.IComponentPowerStateApi;
                        if (api != null)
                            await api.ApplyComponent28VStateAsync(CancellationToken.None);
                    }
                    catch { }

                    AddLog($"[{DateTime.Now:HH:mm:ss}] ========== 手动测试开始 ==========");
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
                IsBusy = true;
                try
                {
                    await DisconnectAllAsync(CancellationToken.None);
                    IsManualTestRunning = false;
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

        private async Task RunAutoTestAsync()
        {
            await _autoTestLock.WaitAsync();
            try
            {
                if (IsBusy)
                    return;

                IsBusy = true;
                IsAutoTestRunning = true;

                await Task.Yield();

                _autoTestCts?.Cancel();
                _autoTestCts?.Dispose();
                _autoTestCts = new CancellationTokenSource();
                var token = _autoTestCts.Token;

                try
                {
                    var api = Prism.Ioc.ContainerLocator.Container.Resolve(typeof(MeasureControl.Services.HardwareApis.IComponentPowerStateApi)) as MeasureControl.Services.HardwareApis.IComponentPowerStateApi;
                    if (api != null)
                        await api.ApplyComponent28VStateAsync(token);
                }
                catch { }

                try
                {
                    ClearResults();
                    AddLog($"[{DateTime.Now:HH:mm:ss}] ========== 自动测试开始 ==========");

                    bool ok100 = await SendAndMeasureAsync(100, token);
                    bool ok50 = await SendAndMeasureAsync(50, token);
                    bool ok0 = await SendAndMeasureAsync(0, token);

                    var ok = ok100 && ok50 && ok0;
                    SetLastTestResult(ok ? "PASS" : "FAIL");
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
                    try { await DisconnectAllAsync(CancellationToken.None); } catch { }
                }
            }
            finally
            {
                IsAutoTestRunning = false;
                IsBusy = false;
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

        private async Task SendAndMeasureAsync(int dutyPct)
        {
            await SendAndMeasureAsync(dutyPct, CancellationToken.None);
        }

        private async Task<bool> SendAndMeasureAsync(int dutyPct, CancellationToken token)
        {
            if (!IsManualTestRunning && !IsAutoTestRunning)
                return false;

            if (PwmFrequencyHz < 10 || PwmFrequencyHz > 5000)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] PWM频率不合法：{PwmFrequencyHz}Hz（范围10-5000Hz）");
                SetLastTestResult("FAIL");
                return false;
            }

            if (dutyPct < 0 || dutyPct > 100)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 占空比不合法：{dutyPct}%（范围0-100%）");
                SetLastTestResult("FAIL");
                return false;
            }

            await _opLock.WaitAsync(token);
            try
            {
                IsBusy = true;
                try
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] PWM={dutyPct}%：发送到FPGA... (Freq={PwmFrequencyHz}Hz)");
                    await EnsureFpgaConnectedAsync(token);

                    byte[] cmd8 = BuildPwmCommand(dutyPct, PwmFrequencyHz);
                    await _fpga.WriteAsync(cmd8, 0, cmd8.Length, token).ConfigureAwait(false);
                    AddLog($"[{DateTime.Now:HH:mm:ss}] PWM={dutyPct}%：已发送 {FormatData(cmd8)}");

                    await Task.Delay(500, token);

                    var allOk = true;

                    var j8 = await MeasureOnceWithMatrixAsync(J8Route, "J8", token);
                    var j9 = await MeasureOnceWithMatrixAsync(J9Route, "J9", token);
                    var j8j9 = await MeasureOnceWithMatrixAsync(J8J9Route, "J8-J9", token);

                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        if (j8 != null)
                        {
                            J8VmaxText = FormatNum(j8.Vmax);
                            J8VminText = FormatNum(j8.Vmin);
                            J8VppText = FormatNum(j8.Vpp);
                            J8DutyPctText = FormatNum(j8.DutyPct);
                        }

                        if (j9 != null)
                        {
                            J9VmaxText = FormatNum(j9.Vmax);
                            J9VminText = FormatNum(j9.Vmin);
                            J9VppText = FormatNum(j9.Vpp);
                            J9DutyPctText = FormatNum(j9.DutyPct);
                        }

                        if (j8j9 != null)
                        {
                            J8J9VmaxText = FormatNum(j8j9.Vmax);
                            J8J9VminText = FormatNum(j8j9.Vmin);
                            J8J9VppText = FormatNum(j8j9.Vpp);
                            J8J9DutyPctText = FormatNum(j8j9.DutyPct);
                        }
                    });

                    if (!QualifyDuty(dutyPct, j8, out var reasonJ8))
                    {
                        allOk = false;
                        AddLog($"[{DateTime.Now:HH:mm:ss}] PWM={dutyPct}% J8判据FAIL：{reasonJ8}");
                    }

                    if (!QualifyDuty(dutyPct, j9, out var reasonJ9))
                    {
                        allOk = false;
                        AddLog($"[{DateTime.Now:HH:mm:ss}] PWM={dutyPct}% J9判据FAIL：{reasonJ9}");
                    }

                    if (!QualifyDuty(dutyPct, j8j9, out var reasonJ8J9))
                    {
                        allOk = false;
                        AddLog($"[{DateTime.Now:HH:mm:ss}] PWM={dutyPct}% J8-J9判据FAIL：{reasonJ8J9}");
                    }

                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        if (dutyPct == 100)
                            Pwm100Result = allOk ? "PASS" : "FAIL";
                        else if (dutyPct == 50)
                            Pwm50Result = allOk ? "PASS" : "FAIL";
                        else if (dutyPct == 0)
                            Pwm0Result = allOk ? "PASS" : "FAIL";
                        else
                            PwmCustomResult = allOk ? "PASS" : "FAIL";
                    });

                    SetLastTestResult(allOk ? "PASS" : "FAIL");
                    return allOk;
                }
                finally
                {
                    IsBusy = false;
                }
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] PWM={dutyPct}%异常：{ex.Message}");
                SetLastTestResult("FAIL");
                return false;
            }
            finally
            {
                _opLock.Release();
            }
        }

        private bool QualifyDuty(int dutyPct, MeasurementResult m, out string reason)
        {
            if (m == null)
            {
                reason = "无测量数据";
                return false;
            }

            if (dutyPct == 100)
            {
                if (!m.Vmax.HasValue)
                {
                    reason = "VMAX无有效值";
                    return false;
                }

                if (m.Vmax.Value < 17.0 || m.Vmax.Value > 32.0)
                {
                    reason = $"VMAX不在[17,32]V：{m.Vmax.Value:F4}V";
                    return false;
                }

                reason = null;
                return true;
            }

            if (dutyPct == 50)
            {
                if (!m.Vmax.HasValue)
                {
                    reason = "VMAX无有效值";
                    return false;
                }

                if (m.Vmax.Value < 17.0 || m.Vmax.Value > 32.0)
                {
                    reason = $"VMAX不在[17,32]V：{m.Vmax.Value:F4}V";
                    return false;
                }

                if (!m.DutyPct.HasValue)
                {
                    reason = "占空比无有效值";
                    return false;
                }

                if (Math.Abs(m.DutyPct.Value - 50.0) > 1.0)
                {
                    reason = $"占空比不在(50±1)%：{m.DutyPct.Value:F3}%";
                    return false;
                }

                reason = null;
                return true;
            }

            if (dutyPct == 0)
            {
                if (!m.Vmax.HasValue || !m.Vmin.HasValue)
                {
                    reason = "VMAX/VMIN无有效值";
                    return false;
                }

                if (m.Vmax.Value > 1.0 || m.Vmin.Value < -1.0)
                {
                    reason = $"电压不在[-1,1]V：VMAX={m.Vmax.Value:F4}V, VMIN={m.Vmin.Value:F4}V";
                    return false;
                }

                reason = null;
                return true;
            }

            if (!m.Vmax.HasValue)
            {
                reason = "VMAX无有效值";
                return false;
            }

            if (m.Vmax.Value < 17.0 || m.Vmax.Value > 32.0)
            {
                reason = $"VMAX不在[17,32]V：{m.Vmax.Value:F4}V";
                return false;
            }

            if (!m.DutyPct.HasValue)
            {
                reason = "占空比无有效值";
                return false;
            }

            if (Math.Abs(m.DutyPct.Value - dutyPct) > 1.0)
            {
                reason = $"占空比不在({dutyPct}±1)%：{m.DutyPct.Value:F3}%";
                return false;
            }

            reason = null;
            return true;
        }

        private async Task<MeasurementResult> MeasureOnceWithMatrixAsync(string slot6Output, string title, CancellationToken token)
        {
            var matrix = MatrixControlService.Instance;

            bool ok1 = false;
            bool ok2 = false;

            try
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 路由{title}：slot6 {Slot6Row1}-{slot6Output} + slot4 {Slot4Common}-{Slot4ToScopeCh1}");

                ok1 = await matrix.ConnectNodesAsync(Slot6Row1, slot6Output, Chassis2Slot6, DefaultMatrixIpAddress, DefaultMatrixTcpBasePort).ConfigureAwait(false);
                ok2 = await matrix.ConnectNodesAsync(Slot4Common, Slot4ToScopeCh1, Chassis2Slot4, DefaultMatrixIpAddress, DefaultMatrixTcpBasePort).ConfigureAwait(false);

                AddLog($"[{DateTime.Now:HH:mm:ss}] 路由{title}：slot6 {(ok1 ? "OK" : "FAIL")}, slot4 {(ok2 ? "OK" : "FAIL")}");
                if (!ok1 || !ok2)
                    return null;

                await EnsureScopeConnectedAsync(token);
                await Task.Delay(200, token);

                var vmax = await QueryScopeDoubleAsync(":MEASure:ITEM? VMAX", token);
                var vmin = await QueryScopeDoubleAsync(":MEASure:ITEM? VMIN", token);
                var vpp = await QueryScopeDoubleAsync(":MEASure:ITEM? VPP", token);
                var duty = await QueryScopeDoubleAsync(":MEASure:ITEM? DUTY", token);

                return new MeasurementResult
                {
                    Title = title,
                    Vmax = vmax,
                    Vmin = vmin,
                    Vpp = vpp,
                    DutyPct = duty
                };
            }
            finally
            {
                try
                {
                    if (ok2)
                        _ = await matrix.DisconnectNodesAsync(Slot4Common, Slot4ToScopeCh1, Chassis2Slot4, DefaultMatrixIpAddress, DefaultMatrixTcpBasePort).ConfigureAwait(false);
                }
                catch { }

                try
                {
                    if (ok1)
                        _ = await matrix.DisconnectNodesAsync(Slot6Row1, slot6Output, Chassis2Slot6, DefaultMatrixIpAddress, DefaultMatrixTcpBasePort).ConfigureAwait(false);
                }
                catch { }
            }
        }

        private async Task EnsureFpgaConnectedAsync(CancellationToken token)
        {
            if (_fpga != null && _fpga.IsConnected)
                return;

            _fpga?.Dispose();
            _fpga = new FpgaTcpClient();
            await _fpga.ConnectAsync(DefaultFpgaIpAddress, DefaultFpgaPort, token).ConfigureAwait(false);
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

        private static byte[] BuildPwmCommand(int dutyPct, int freqHz)
        {
            if (dutyPct < 0) dutyPct = 0;
            if (dutyPct > 100) dutyPct = 100;
            if (freqHz < 10) freqHz = 10;
            if (freqHz > 5000) freqHz = 5000;

            ushort f = (ushort)freqHz;

            return new byte[]
            {
                0xAA, 0x55,
                0x81,
                (byte)dutyPct,
                (byte)(f >> 8), (byte)(f & 0xFF),
                0x00, 0x00
            };
        }

        private static string FormatData(byte[] data)
        {
            if (data == null)
                return "--";
            return BitConverter.ToString(data).Replace("-", " ");
        }

        private static string FormatNum(double? v)
        {
            if (!v.HasValue || double.IsNaN(v.Value) || double.IsInfinity(v.Value))
                return "--";
            return v.Value.ToString("F6", CultureInfo.InvariantCulture);
        }

        private void ClearResults()
        {
            LastTestTime = "--";
            LastTestResult = "--";

            PwmCustomResult = "--";
            Pwm100Result = "--";
            Pwm50Result = "--";
            Pwm0Result = "--";

            IsMeasuringPwmCustom = false;
            IsMeasuringPwm100 = false;
            IsMeasuringPwm50 = false;
            IsMeasuringPwm0 = false;

            J8VmaxText = "--";
            J8VminText = "--";
            J8VppText = "--";
            J8DutyPctText = "--";

            J9VmaxText = "--";
            J9VminText = "--";
            J9VppText = "--";
            J9DutyPctText = "--";

            J8J9VmaxText = "--";
            J8J9VminText = "--";
            J8J9VppText = "--";
            J8J9DutyPctText = "--";
        }

        private void SetLastTestResult(string result)
        {
            LastTestResult = result ?? "--";
            LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        private void AddLog(string msg)
        {
            if (string.IsNullOrWhiteSpace(msg))
                return;

            try
            {
                Application.Current?.Dispatcher?.Invoke(() => Logs.Add(msg));
            }
            catch
            {
                Logs.Add(msg);
            }
        }

        private async Task DisconnectAllAsync(CancellationToken token)
        {
            await _opLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                try
                {
                    SafeCloseNetworkStream(ref _scopeTcpStream);
                    SafeCloseTcpClient(ref _scopeTcpClient);
                }
                catch
                {
                }

                try
                {
                    _fpga?.Dispose();
                    _fpga = null;
                }
                catch
                {
                }
            }
            finally
            {
                _opLock.Release();
            }
        }

        private static void SafeCloseNetworkStream(ref NetworkStream stream)
        {
            try { stream?.Close(); } catch { }
            try { stream?.Dispose(); } catch { }
            stream = null;
        }

        private static void SafeCloseTcpClient(ref TcpClient client)
        {
            try { client?.Close(); } catch { }
            try { client?.Dispose(); } catch { }
            client = null;
        }

        public void Dispose()
        {
            try { _autoTestCts?.Cancel(); } catch { }
            try { _autoTestCts?.Dispose(); } catch { }
            _autoTestCts = null;

            try { DisconnectAllAsync(CancellationToken.None).GetAwaiter().GetResult(); } catch { }

            try { _manualTestLock?.Dispose(); } catch { }
            try { _autoTestLock?.Dispose(); } catch { }
            try { _opLock?.Dispose(); } catch { }
        }

        private sealed class MeasurementResult
        {
            public string Title { get; set; }
            public double? Vmax { get; set; }
            public double? Vmin { get; set; }
            public double? Vpp { get; set; }
            public double? DutyPct { get; set; }
        }
    }
}
