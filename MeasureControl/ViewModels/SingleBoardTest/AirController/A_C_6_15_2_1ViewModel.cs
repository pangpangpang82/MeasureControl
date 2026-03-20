using Prism.Commands;
using Prism.Mvvm;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using MeasureControl.Helpers;
using MeasureControl.Services;
using MeasureControl.Services.HardwareApis;
using MeasureControl.Simulations.A_C_6_15_2_1;

namespace MeasureControl.ViewModels.SingleBoardTest.AirController
{
    public sealed class A_C_6_15_2_1ViewModel : BindableBase, IDisposable
    {
        private static readonly byte[] EnterAtpCommand8 = { 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] EnterAtpOk8 = { 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x02 };
        private static readonly byte[] ExitAtpCommand8 = { 0x00, 0x02, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01 };
        private static readonly byte[] ExitAtpOk8 = { 0x00, 0x02, 0x00, 0x01, 0x00, 0x00, 0x00, 0x03 };

        private static readonly byte[] Pwm100Command8 = { 0x21, 0x04, 0x04, 0x03, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] Pwm50Command8 = { 0x21, 0x04, 0x04, 0x02, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] Pwm0Command8 = { 0x21, 0x04, 0x04, 0x01, 0x00, 0x00, 0x00, 0x00 };

        private const string FixedTxChannel = "429_CH0";
        private const string FixedRxChannel = "429_CH2";

        private readonly A_C_6_15_2_1Simulation _simulation = new A_C_6_15_2_1Simulation();
        private readonly SemaphoreSlim _manualTestLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _autoTestLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _arincOpLock = new SemaphoreSlim(1, 1);

        private readonly SemaphoreSlim _instrumentLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _scopeIoLock = new SemaphoreSlim(1, 1);

        private TcpClient _scopeTcpClient;
        private NetworkStream _scopeTcpStream;

        private bool _matrixRouted;

        private CancellationTokenSource _autoTestCts;

        private string _testTxChannel = FixedTxChannel;
        private string _testRxChannel = FixedRxChannel;

        private string _oscilloscopeIpAddress = "192.168.1.18";

        private bool _isBusy;
        private bool _isManualTestRunning;
        private bool _isAutoTestRunning;

        private double _arincRate = 100000.0;

        private string _enterAtpRxDataText = "--";
        private string _exitAtpRxDataText = "--";

        private string _scopeVmaxText = "--";
        private string _scopeVminText = "--";
        private string _scopeVavgText = "--";
        private string _scopeVrmsText = "--";
        private string _scopeVppText = "--";
        private string _freqHzText = "--";
        private string _dutyPctText = "--";

        private string _pwm100VrmsText = "--";
        private string _pwm100VmaxText = "--";
        private string _pwm50VrmsText = "--";
        private string _pwm50VmaxText = "--";
        private string _pwm50DutyPctText = "--";
        private string _pwm0VrmsText = "--";
        private string _pwm0VmaxText = "--";

        private string _lastTestTime = "--";
        private string _lastTestResult = "--";

        private string _previousTestTime = "--";
        private string _previousTestResult = "--";

        public A_C_6_15_2_1ViewModel()
        {
            ManualTestCommand = new DelegateCommand(OnManualTest);
            AutoTestCommand = new DelegateCommand(OnAutoTest);
            ClearLogCommand = new DelegateCommand(() => Logs.Clear());

            SendEnterAtpCommand = new DelegateCommand(async () => await SendAndWaitOkAsync(EnterAtpCommand8, EnterAtpOk8, "进入ATP"));
            SendExitAtpCommand = new DelegateCommand(async () => await SendAndWaitOkAsync(ExitAtpCommand8, ExitAtpOk8, "退出ATP"));

            SendPwm100Command = new DelegateCommand(async () => await SendAndWaitEchoAsync(Pwm100Command8, "PWM=100%"));
            SendPwm50Command = new DelegateCommand(async () => await SendAndWaitEchoAsync(Pwm50Command8, "PWM=50%"));
            SendPwm0Command = new DelegateCommand(async () => await SendAndWaitEchoAsync(Pwm0Command8, "PWM=0%"));

            MeasurePwm100Command = new DelegateCommand(async () => await MeasureAndUpdateUiAsync(100));
            MeasurePwm50Command = new DelegateCommand(async () => await MeasureAndUpdateUiAsync(50));
            MeasurePwm0Command = new DelegateCommand(async () => await MeasureAndUpdateUiAsync(0));
        }

        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();

        public DelegateCommand ManualTestCommand { get; }
        public DelegateCommand AutoTestCommand { get; }
        public DelegateCommand ClearLogCommand { get; }

        public DelegateCommand SendEnterAtpCommand { get; }
        public DelegateCommand SendExitAtpCommand { get; }
        public DelegateCommand SendPwm100Command { get; }
        public DelegateCommand SendPwm50Command { get; }
        public DelegateCommand SendPwm0Command { get; }

        public DelegateCommand MeasurePwm100Command { get; }
        public DelegateCommand MeasurePwm50Command { get; }
        public DelegateCommand MeasurePwm0Command { get; }

        public double ArincRate
        {
            get => _arincRate;
            set => SetProperty(ref _arincRate, value);
        }

        public string TestTxChannel
        {
            get => _testTxChannel;
            set => SetProperty(ref _testTxChannel, value);
        }

        public string TestRxChannel
        {
            get => _testRxChannel;
            set => SetProperty(ref _testRxChannel, value);
        }

        public string OscilloscopeIpAddress
        {
            get => _oscilloscopeIpAddress;
            set => SetProperty(ref _oscilloscopeIpAddress, value);
        }

        public bool IsBusy
        {
            get => _isBusy;
            private set => SetProperty(ref _isBusy, value);
        }

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

        public string ScopeVmaxText
        {
            get => _scopeVmaxText;
            private set => SetProperty(ref _scopeVmaxText, value);
        }

        public string ScopeVminText
        {
            get => _scopeVminText;
            private set => SetProperty(ref _scopeVminText, value);
        }

        public string ScopeVavgText
        {
            get => _scopeVavgText;
            private set => SetProperty(ref _scopeVavgText, value);
        }

        public string ScopeVrmsText
        {
            get => _scopeVrmsText;
            private set => SetProperty(ref _scopeVrmsText, value);
        }

        public string ScopeVppText
        {
            get => _scopeVppText;
            private set => SetProperty(ref _scopeVppText, value);
        }

        public string FreqHzText
        {
            get => _freqHzText;
            private set => SetProperty(ref _freqHzText, value);
        }

        public string DutyPctText
        {
            get => _dutyPctText;
            private set => SetProperty(ref _dutyPctText, value);
        }

        public string Pwm100VrmsText
        {
            get => _pwm100VrmsText;
            private set => SetProperty(ref _pwm100VrmsText, value);
        }

        public string Pwm100VmaxText
        {
            get => _pwm100VmaxText;
            private set => SetProperty(ref _pwm100VmaxText, value);
        }

        public string Pwm50VrmsText
        {
            get => _pwm50VrmsText;
            private set => SetProperty(ref _pwm50VrmsText, value);
        }

        public string Pwm50VmaxText
        {
            get => _pwm50VmaxText;
            private set => SetProperty(ref _pwm50VmaxText, value);
        }

        public string Pwm50DutyPctText
        {
            get => _pwm50DutyPctText;
            private set => SetProperty(ref _pwm50DutyPctText, value);
        }

        public string Pwm0VrmsText
        {
            get => _pwm0VrmsText;
            private set => SetProperty(ref _pwm0VrmsText, value);
        }

        public string Pwm0VmaxText
        {
            get => _pwm0VmaxText;
            private set => SetProperty(ref _pwm0VmaxText, value);
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

        private void EnsureManualArincChannels()
        {
            TestTxChannel = FixedTxChannel;
            TestRxChannel = FixedRxChannel;
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
                    EnterAtpRxDataText = "--";
                    ExitAtpRxDataText = "--";
                    ClearMeasurementTexts();

                    IsManualTestRunning = true;
                    await Task.Yield();

                    _simulation.IsRealProduct = true;
                    _simulation.ArincRate = ArincRate;

                    AddLog($"[{DateTime.Now:HH:mm:ss}] ========== 手动测试开始 ==========");

                    try
                    {
                        var api = Prism.Ioc.ContainerLocator.Container.Resolve(typeof(MeasureControl.Services.HardwareApis.IComponentPowerStateApi)) as MeasureControl.Services.HardwareApis.IComponentPowerStateApi;
                        if (api != null)
                            await api.ApplyComponent28VStateAsync(CancellationToken.None);
                    }
                    catch { }

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
                IsManualTestRunning = false;
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
                    try { await _simulation.StopAsync(msg => AddLog(msg)); } catch { }
                    try { await DisconnectInstrumentsAndMatrixAsync(CancellationToken.None); } catch { }
                    EnterAtpRxDataText = "--";
                    ExitAtpRxDataText = "--";
                    ClearMeasurementTexts();
                    IsManualTestRunning = false;
                    AddLog($"[{DateTime.Now:HH:mm:ss}] ========== 手动测试已停止 ==========");
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

        private async Task RunAutoTestAsync()
        {
            await _autoTestLock.WaitAsync();
            try
            {
                if (IsBusy)
                    return;

                EnsureManualArincChannels();

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
                    _simulation.IsRealProduct = true;
                    _simulation.ArincRate = ArincRate;

                    AddLog($"[{DateTime.Now:HH:mm:ss}] ========== 自动测试开始 ==========");
                    await _simulation.StartAsync(TestTxChannel, TestRxChannel, msg => AddLog(msg));

                    var failures = new System.Collections.Generic.List<string>();

                    if (!await AutoStepAsync(EnterAtpCommand8, EnterAtpOk8, "进入ATP", token))
                        failures.Add("进入ATP失败");

                    if (!await AutoStepAsync(Pwm100Command8, Pwm100Command8, "PWM=100%", token))
                        failures.Add("PWM=100%回读失败");
                    else if (!await MeasureAndQualifyPwmAsync(100, token))
                        failures.Add("PWM=100%波形判据不合格");

                    if (!await AutoStepAsync(Pwm50Command8, Pwm50Command8, "PWM=50%", token))
                        failures.Add("PWM=50%回读失败");
                    else if (!await MeasureAndQualifyPwmAsync(50, token))
                        failures.Add("PWM=50%波形判据不合格");

                    if (!await AutoStepAsync(Pwm0Command8, Pwm0Command8, "PWM=0%", token))
                        failures.Add("PWM=0%回读失败");
                    else if (!await MeasureAndQualifyPwmAsync(0, token))
                        failures.Add("PWM=0%波形判据不合格");

                    if (!await AutoStepAsync(ExitAtpCommand8, ExitAtpOk8, "退出ATP", token))
                        failures.Add("退出ATP失败");

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
                    try { await _simulation.StopAsync(msg => AddLog(msg)); } catch { }
                    try { await DisconnectInstrumentsAndMatrixAsync(CancellationToken.None); } catch { }
                    try { await TryApplyComponentDownStateAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
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

        private async Task<bool> AutoStepAsync(byte[] cmd8, byte[] expected8, string title, CancellationToken token)
        {
            AddLog($"[{DateTime.Now:HH:mm:ss}] {title}：发送...");
            await _simulation.ClearRxFifoAsync(TestRxChannel);
            await Task.Delay(20, token);
            await _simulation.SendBenchCommandOnlyAsync(TestTxChannel, cmd8, msg => AddLog(msg), token);

            if (!cmd8.SequenceEqual(EnterAtpCommand8) && !cmd8.SequenceEqual(ExitAtpCommand8))
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] {title}：发送完成（不等待回读）");
                return true;
            }

            var resp = await _simulation.WaitBenchResponse8Async(
                TestRxChannel,
                b => b != null && b.SequenceEqual(expected8),
                timeoutMs: 1500,
                log: msg => AddLog(msg),
                token: token);

            if (resp == null)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] {title}：等待超时");
                return false;
            }

            AddLog($"[{DateTime.Now:HH:mm:ss}] {title}：OK ({FormatData(resp)})");
            return true;
        }

        private async Task SendAndWaitOkAsync(byte[] cmd8, byte[] ok8, string title)
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
                    AddLog($"[{DateTime.Now:HH:mm:ss}] {title}：发送... TX={TestTxChannel}, RX={TestRxChannel}");
                    await _simulation.ClearRxFifoAsync(TestRxChannel);
                    await Task.Delay(20, token);

                    await _simulation.SendBenchCommandOnlyAsync(TestTxChannel, cmd8, msg => AddLog(msg), token);

                    var resp = await _simulation.WaitBenchResponse8Async(
                        TestRxChannel,
                        b => b != null && b.SequenceEqual(ok8),
                        timeoutMs: 1500,
                        log: msg => AddLog(msg),
                        token: token);

                    if (resp == null)
                    {
                        AddLog($"[{DateTime.Now:HH:mm:ss}] {title}：等待超时");
                        SetLastTestResult("FAIL");
                        return;
                    }

                    AddLog($"[{DateTime.Now:HH:mm:ss}] {title}：OK ({FormatData(resp)})");

                    if (cmd8.SequenceEqual(EnterAtpCommand8))
                        EnterAtpRxDataText = FormatData(resp);
                    else if (cmd8.SequenceEqual(ExitAtpCommand8))
                        ExitAtpRxDataText = FormatData(resp);

                    SetLastTestResult("PASS");
                }
                finally
                {
                    IsBusy = false;
                }
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] {title}异常：{ex.Message}");
                SetLastTestResult("FAIL");
            }
            finally
            {
                _arincOpLock.Release();
            }
        }

        private async Task<bool> MeasureAndQualifyPwmAsync(int pwmPercent, CancellationToken token)
        {
            await _instrumentLock.WaitAsync(token);
            try
            {
                var m = await MeasurePwmRawCoreAsync(pwmPercent, token);
                if (m == null)
                    return false;

                AddLog($"[{DateTime.Now:HH:mm:ss}] PWM={pwmPercent}%测量：VMAX={FormatNum(m.Vmax)} V, VMIN={FormatNum(m.Vmin)} V, VAVG={FormatNum(m.Vavg)} V, VRMS={FormatNum(m.Vrms)} V, VPP={FormatNum(m.Vpp)} V, F={FormatNum(m.FreqHz)} Hz, DUTY={FormatNum(m.DutyPct)} %");

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    ScopeVmaxText = FormatNum(m.Vmax);
                    ScopeVminText = FormatNum(m.Vmin);
                    ScopeVavgText = FormatNum(m.Vavg);
                    ScopeVrmsText = FormatNum(m.Vrms);
                    ScopeVppText = FormatNum(m.Vpp);
                    FreqHzText = FormatNum(m.FreqHz);
                    DutyPctText = FormatNum(m.DutyPct);
                });

                return pwmPercent switch
                {
                    100 => QualifyPwm100(m.Vmax, m.Vmin, m.Vavg, m.Vpp, m.DutyPct, out var reason100) ? true : FailWithReason(pwmPercent, reason100),
                    50 => QualifyPwm50(m.Vmax, m.Vmin, m.Vavg, m.Vpp, m.DutyPct, out var reason50) ? true : FailWithReason(pwmPercent, reason50),
                    0 => QualifyPwm0(m.Vmax, m.Vmin, m.Vavg, m.Vpp, m.DutyPct, out var reason0) ? true : FailWithReason(pwmPercent, reason0),
                    _ => true
                };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] PWM={pwmPercent}%测量异常：{ex.Message}");
                return false;
            }
            finally
            {
                _instrumentLock.Release();
            }

            bool FailWithReason(int pwm, string reason)
            {
                if (!string.IsNullOrWhiteSpace(reason))
                    AddLog($"[{DateTime.Now:HH:mm:ss}] PWM={pwm}%判据FAIL：{reason}");
                return false;
            }
        }

        private void ClearMeasurementTexts()
        {
            ScopeVmaxText = "--";
            ScopeVminText = "--";
            ScopeVavgText = "--";
            ScopeVrmsText = "--";
            ScopeVppText = "--";
            FreqHzText = "--";
            DutyPctText = "--";

            Pwm100VrmsText = "--";
            Pwm100VmaxText = "--";
            Pwm50VrmsText = "--";
            Pwm50VmaxText = "--";
            Pwm50DutyPctText = "--";
            Pwm0VrmsText = "--";
            Pwm0VmaxText = "--";
        }

        private sealed class PwmMeasurement
        {
            public int PwmPercent { get; set; }
            public double? Vmax { get; set; }
            public double? Vmin { get; set; }
            public double? Vavg { get; set; }
            public double? Vrms { get; set; }
            public double? Vpp { get; set; }
            public double? FreqHz { get; set; }
            public double? DutyPct { get; set; }
        }

        private async Task<PwmMeasurement> MeasurePwmRawCoreAsync(int pwmPercent, CancellationToken token)
        {
            var routed = await EnsureMatrixRoutedAsync(token);
            if (!routed)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] PWM={pwmPercent}%：矩阵开关路由失败");
                return null;
            }

            await EnsureInstrumentsConnectedAsync(token);
            await Task.Delay(200, token);

            var vmax = await QueryScopeDoubleAsync(1, ":MEASure:ITEM? VMAX", token);
            var vmin = await QueryScopeDoubleAsync(1, ":MEASure:ITEM? VMIN", token);
            var vavg = await QueryScopeDoubleAsync(1, ":MEASure:ITEM? VAVG", token);
            var vrms = await QueryScopeDoubleAsync(1, ":MEASure:ITEM? VRMS", token);
            var vpp = await QueryScopeDoubleAsync(1, ":MEASure:ITEM? VPP", token);

            var freq = await QueryScopeDoubleAsync(1, ":MEASure:ITEM? FREQuency", token);
            var dutyPct = await QueryScopeDutyPctAsync(1, token);

            return new PwmMeasurement
            {
                PwmPercent = pwmPercent,
                Vmax = vmax,
                Vmin = vmin,
                Vavg = vavg,
                Vrms = vrms,
                Vpp = vpp,
                FreqHz = freq,
                DutyPct = dutyPct
            };
        }

        private async Task MeasureAndUpdateUiAsync(int pwmPercent)
        {
            if (!IsManualTestRunning || IsBusy)
                return;

            await _instrumentLock.WaitAsync();
            try
            {
                IsBusy = true;
                var token = CancellationToken.None;
                AddLog($"[{DateTime.Now:HH:mm:ss}] PWM={pwmPercent}%：开始测量...");
                var m = await MeasurePwmRawCoreAsync(pwmPercent, token);
                if (m == null)
                    return;

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (pwmPercent == 100)
                    {
                        Pwm100VrmsText = FormatNum(m.Vrms);
                        Pwm100VmaxText = FormatNum(m.Vmax);
                    }
                    else if (pwmPercent == 50)
                    {
                        Pwm50VrmsText = FormatNum(m.Vrms);
                        Pwm50VmaxText = FormatNum(m.Vmax);
                        Pwm50DutyPctText = FormatNum(m.DutyPct);
                    }
                    else if (pwmPercent == 0)
                    {
                        Pwm0VrmsText = FormatNum(m.Vrms);
                        Pwm0VmaxText = FormatNum(m.Vmax);
                    }
                });

                AddLog($"[{DateTime.Now:HH:mm:ss}] PWM={pwmPercent}%：测量完成 VRMS={FormatNum(m.Vrms)} V, VMAX={FormatNum(m.Vmax)} V{(pwmPercent == 50 ? $", DUTY={FormatNum(m.DutyPct)} %" : string.Empty)}");
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] PWM={pwmPercent}%测量异常：{ex.Message}");
            }
            finally
            {
                IsBusy = false;
                _instrumentLock.Release();
            }
        }

        private static string FormatNum(double? v)
        {
            if (!v.HasValue || double.IsNaN(v.Value) || double.IsInfinity(v.Value))
                return "--";
            return v.Value.ToString("F6", CultureInfo.InvariantCulture);
        }

        private static double NormalizeDutyToPercent(double dutyValue)
        {
            if (double.IsNaN(dutyValue) || double.IsInfinity(dutyValue))
                return dutyValue;
            if (dutyValue <= 1.0)
                return dutyValue * 100.0;
            return dutyValue;
        }

        private async Task<double?> QueryScopeDutyPctAsync(int channel, CancellationToken token)
        {
            var duty = await QueryScopeDoubleAsync(channel, ":MEASure:ITEM? DUTY", token);
            if (duty.HasValue)
                return NormalizeDutyToPercent(duty.Value);

            duty = await QueryScopeDoubleAsync(channel, ":MEASure:ITEM? DUTYcycle", token);
            if (duty.HasValue)
                return NormalizeDutyToPercent(duty.Value);

            return null;
        }

        private bool QualifyPwm100(double? vmax, double? vmin, double? vavg, double? vpp, double? dutyPct, out string reason)
        {
            const double vHigh = 3.3;
            const double vHighTol = 0.5;
            const double vppMax = 0.5;
            const double dutyMin = 90.0;

            if (!vmax.HasValue)
            {
                reason = "示波器VMAX无有效值";
                return false;
            }

            if (Math.Abs(vmax.Value - vHigh) > vHighTol)
            {
                reason = $"VMAX不在范围: {vmax.Value:F4}V, 期望 {vHigh}±{vHighTol}V";
                return false;
            }

            if (vpp.HasValue && vpp.Value > vppMax)
            {
                reason = $"VPP过大: {vpp.Value:F4}V > {vppMax}V";
                return false;
            }

            if (!dutyPct.HasValue)
            {
                reason = "示波器占空比无有效值";
                return false;
            }

            if (dutyPct.Value < dutyMin)
            {
                reason = $"占空比过低: {dutyPct.Value:F3}% < {dutyMin}%";
                return false;
            }

            _ = vmin + vavg;
            reason = null;
            return true;
        }

        private bool QualifyPwm50(double? vmax, double? vmin, double? vavg, double? vpp, double? dutyPct, out string reason)
        {
            const double dutyTarget = 50.0;
            const double dutyTol = 1.0;

            if (!dutyPct.HasValue)
            {
                reason = "示波器占空比无有效值";
                return false;
            }

            if (Math.Abs(dutyPct.Value - dutyTarget) > dutyTol)
            {
                reason = $"占空比不合格: {dutyPct.Value:F3}% , 期望 {dutyTarget}±{dutyTol}%";
                return false;
            }

            _ = vmax + vmin + vavg + vpp;
            reason = null;
            return true;
        }

        private bool QualifyPwm0(double? vmax, double? vmin, double? vavg, double? vpp, double? dutyPct, out string reason)
        {
            const double vAbsMax = 1.0;
            const double dutyMax = 10.0;
            const double vppMax = 0.5;

            if (!vmax.HasValue || !vmin.HasValue)
            {
                reason = "示波器VMAX/VMIN无有效值";
                return false;
            }

            if (vmax.Value > vAbsMax || vmin.Value < -vAbsMax)
            {
                reason = $"电压超范围: VMAX={vmax.Value:F4}V, VMIN={vmin.Value:F4}V, 期望均在[-{vAbsMax},{vAbsMax}]";
                return false;
            }

            if (vpp.HasValue && vpp.Value > vppMax)
            {
                reason = $"VPP过大: {vpp.Value:F4}V > {vppMax}V";
                return false;
            }

            if (!dutyPct.HasValue)
            {
                reason = "示波器占空比无有效值";
                return false;
            }

            if (dutyPct.Value > dutyMax)
            {
                reason = $"占空比过高: {dutyPct.Value:F3}% > {dutyMax}%";
                return false;
            }

            _ = vavg;
            reason = null;
            return true;
        }

        private async Task EnsureInstrumentsConnectedAsync(CancellationToken token)
        {
            if (_scopeTcpClient == null || _scopeTcpStream == null)
            {
                if (string.IsNullOrWhiteSpace(OscilloscopeIpAddress))
                    throw new InvalidOperationException("OscilloscopeIpAddress 为空");

                _scopeTcpClient = new TcpClient();
                await _scopeTcpClient.ConnectAsync(OscilloscopeIpAddress.Trim(), 5555);
                _scopeTcpStream = _scopeTcpClient.GetStream();
                try
                {
                    _scopeTcpStream.ReadTimeout = 5000;
                    _scopeTcpStream.WriteTimeout = 5000;
                }
                catch
                {
                }
            }
        }

        private async Task<bool> EnsureMatrixRoutedAsync(CancellationToken token)
        {
            if (_matrixRouted)
                return true;

            var svc = MatrixControlService.Instance;

            var operations = new (string inNode, string outNode, int slot, string ip)[]
            {
                ("I1", "O2", 9, "192.168.1.3"),
                ("I0", "O8", 4, "192.168.1.3")
            };

            var connectTasks = operations
                .Select(op => svc.ConnectNodesAsync(op.inNode, op.outNode, op.slot, op.ip))
                .ToArray();

            var results = await Task.WhenAll(connectTasks);
            _matrixRouted = results.All(r => r);
            _ = token;
            return _matrixRouted;
        }

        private async Task DisconnectInstrumentsAndMatrixAsync(CancellationToken token)
        {
            await _instrumentLock.WaitAsync(token);
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
                    if (_matrixRouted)
                    {
                        var svc = MatrixControlService.Instance;

                        var operations = new (string inNode, string outNode, int slot, string ip)[]
                        {
                            ("I1", "O2", 9, "192.168.1.3"),
                            ("I0", "O8", 4, "192.168.1.3")
                        };

                        var disconnectTasks = operations
                            .Select(op => svc.DisconnectNodesAsync(op.inNode, op.outNode, op.slot, op.ip))
                            .ToArray();

                        _ = await Task.WhenAll(disconnectTasks);
                    }
                }
                catch
                {
                }
                finally
                {
                    _matrixRouted = false;
                }
            }
            finally
            {
                _instrumentLock.Release();
            }
        }

        private static void SafeCloseNetworkStream(ref NetworkStream stream)
        {
            if (stream == null)
                return;
            try { stream.Close(); } catch { }
            try { stream.Dispose(); } catch { }
            stream = null;
        }

        private static void SafeCloseTcpClient(ref TcpClient client)
        {
            if (client == null)
                return;
            try { client.Close(); } catch { }
            try { client.Dispose(); } catch { }
            client = null;
        }

        private async Task<double?> QueryScopeDoubleAsync(int channel, string query, CancellationToken token)
        {
            if (_scopeTcpStream == null)
                return null;

            await _scopeIoLock.WaitAsync(token);
            try
            {
                await WriteScopeAsync($":MEASure:SOURce CHANnel{channel}", token);
                var raw = await QueryScopeAsync(query, token);
                if (string.IsNullOrWhiteSpace(raw))
                    return null;

                raw = raw.Trim();
                if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                    return v;
                if (double.TryParse(raw, NumberStyles.Float, CultureInfo.CurrentCulture, out v))
                    return v;
                return null;
            }
            finally
            {
                _scopeIoLock.Release();
            }
        }

        private async Task WriteScopeAsync(string command, CancellationToken token)
        {
            if (_scopeTcpStream == null)
                return;

            var cmd = command.EndsWith("\n", StringComparison.Ordinal) ? command : command + "\n";
            var bytes = Encoding.ASCII.GetBytes(cmd);
            await _scopeTcpStream.WriteAsync(bytes, 0, bytes.Length, token);
            await _scopeTcpStream.FlushAsync(token);
        }

        private async Task<string> QueryScopeAsync(string command, CancellationToken token)
        {
            await WriteScopeAsync(command, token);
            return await ReadLineAsync(_scopeTcpStream, 5000, token);
        }

        private static async Task<string> ReadLineAsync(NetworkStream stream, int timeoutMs, CancellationToken token)
        {
            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                cts.CancelAfter(timeoutMs);
                var sb = new StringBuilder();
                var buf = new byte[1];
                while (true)
                {
                    int n;
                    try
                    {
                        n = await stream.ReadAsync(buf, 0, 1, cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        if (token.IsCancellationRequested)
                            throw;
                        throw new TimeoutException($"示波器读取超时({timeoutMs}ms)");
                    }
                    if (n <= 0)
                        break;
                    char ch = (char)buf[0];
                    if (ch == '\n')
                        break;
                    if (ch != '\r')
                        sb.Append(ch);
                }
                return sb.ToString().Trim();
            }
        }

        private async Task SendAndWaitEchoAsync(byte[] cmd8, string title)
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
                    AddLog($"[{DateTime.Now:HH:mm:ss}] {title}：发送... TX={TestTxChannel}, RX={TestRxChannel}");
                    await _simulation.ClearRxFifoAsync(TestRxChannel);
                    await Task.Delay(20, token);

                    await _simulation.SendBenchCommandOnlyAsync(TestTxChannel, cmd8, msg => AddLog(msg), token);

                    var resp = await _simulation.WaitBenchResponse8Async(
                        TestRxChannel,
                        b => b != null && b.SequenceEqual(cmd8),
                        timeoutMs: 1500,
                        log: msg => AddLog(msg),
                        token: token);

                    if (resp == null)
                    {
                        AddLog($"[{DateTime.Now:HH:mm:ss}] {title}：等待超时");
                        SetLastTestResult("FAIL");
                        return;
                    }

                    AddLog($"[{DateTime.Now:HH:mm:ss}] {title}：回读OK ({FormatData(resp)})");

                    SetLastTestResult("PASS");
                }
                finally
                {
                    IsBusy = false;
                }
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] {title}异常：{ex.Message}");
                SetLastTestResult("FAIL");
            }
            finally
            {
                _arincOpLock.Release();
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
            PreviousTestTime = LastTestTime;
            PreviousTestResult = LastTestResult;
            LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            LastTestResult = result;
        }

        private static string FormatData(byte[] data)
        {
            if (data == null || data.Length == 0)
                return "--";

            return string.Join(" ", data.Select(b => b.ToString("X2")));
        }

        public void Dispose()
        {
            try { _autoTestCts?.Cancel(); } catch { }
            try { _autoTestCts?.Dispose(); } catch { }
            try { _simulation.StopAsync(_ => { }).GetAwaiter().GetResult(); } catch { }
            try { DisconnectInstrumentsAndMatrixAsync(CancellationToken.None).GetAwaiter().GetResult(); } catch { }
        }
    }
}
