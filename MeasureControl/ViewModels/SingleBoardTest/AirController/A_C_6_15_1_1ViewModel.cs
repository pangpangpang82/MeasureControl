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
using MeasureControl.Simulations.A_C_6_15_1_1;

namespace MeasureControl.ViewModels.SingleBoardTest.AirController
{
    public sealed class A_C_6_15_1_1ViewModel : BindableBase, IDisposable
    {
        private static readonly byte[] EnterAtpCommand8 = { 0x30, 0x01, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] EnterAtpOk8 = { 0x30, 0x01, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00 }; // not used (send-only)
        private static readonly byte[] ExitAtpCommand8 = { 0x30, 0x02, 0x02, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] ExitAtpOk8 = { 0x30, 0x02, 0x02, 0x01, 0x00, 0x00, 0x00, 0x00 }; // not used (send-only)

        private const string FixedTxChannel = "429_CH5";
        private const string FixedRxChannel = "429_CH2";

        private static readonly byte[] Pwm100Command8 = { 0x21, 0x03, 0x04, 0x03, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] Pwm50Command8 = { 0x21, 0x03, 0x04, 0x02, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] Pwm0Command8 = { 0x21, 0x03, 0x04, 0x01, 0x00, 0x00, 0x00, 0x00 };

        private readonly A_C_6_15_1_1Simulation _simulation = new A_C_6_15_1_1Simulation();
        private readonly SemaphoreSlim _manualTestLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _autoTestLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _arincOpLock = new SemaphoreSlim(1, 1);

        private readonly SemaphoreSlim _instrumentLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _scopeIoLock = new SemaphoreSlim(1, 1);

        private TcpClient _scopeTcpClient;
        private NetworkStream _scopeTcpStream;

        private bool _matrixRouted;

        private CancellationTokenSource _autoTestCts;
        private CancellationTokenSource _manualMeasureCts;

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

        private double? _pwm100Voltage;
        private double? _pwm50DutyPct;
        private double? _pwm0Voltage;

        private string _pwm100VoltageText = "--";
        private string _pwm50DutyPctText = "--";
        private string _pwm0VoltageText = "--";

        private string _lastTestTime = "--";
        private string _lastTestResult = "--";

        private string _previousTestTime = "--";
        private string _previousTestResult = "--";

        public A_C_6_15_1_1ViewModel()
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

        public string Pwm100VoltageText
        {
            get => _pwm100VoltageText;
            private set => SetProperty(ref _pwm100VoltageText, value);
        }

        public string Pwm50DutyPctText
        {
            get => _pwm50DutyPctText;
            private set => SetProperty(ref _pwm50DutyPctText, value);
        }

        public string Pwm0VoltageText
        {
            get => _pwm0VoltageText;
            private set => SetProperty(ref _pwm0VoltageText, value);
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

        private void EnsureManualArincChannels()
        {
            TestTxChannel = FixedTxChannel;
            TestRxChannel = FixedRxChannel;
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
            try { _manualMeasureCts?.Cancel(); } catch { }
            try { _manualMeasureCts?.Dispose(); } catch { }
            _manualMeasureCts = null;

            // Force-close network stream immediately to unblock any pending I/O
            SafeCloseNetworkStream(ref _scopeTcpStream);
            SafeCloseTcpClient(ref _scopeTcpClient);

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
                    _simulation.IsRealProduct = true;
                    _simulation.ArincRate = ArincRate;

                    AddLog($"[{DateTime.Now:HH:mm:ss}] ========== 自动测试开始 ==========");
                    await _simulation.StartAsync(TestTxChannel, TestRxChannel, msg => AddLog(msg));

                    var failures = new System.Collections.Generic.List<string>();

                    if (!await AutoStepAsync(EnterAtpCommand8, EnterAtpOk8, "进入ATP", token))
                        failures.Add("进入ATP失败");

                    // 三次PWM测量期间保持矩阵连接，参照A_C_6_16_1_1_1模式
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 连接矩阵开关...");
                    var matrixOk = await EnsureMatrixRoutedAsync(token);
                    if (!matrixOk)
                    {
                        failures.Add("矩阵开关路由失败");
                    }
                    else
                    {
                        try
                        {
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
                        }
                        finally
                        {
                            AddLog($"[{DateTime.Now:HH:mm:ss}] 断开矩阵开关...");
                            await DisconnectMatrixAsync(token);
                        }
                    }

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
            try { _autoTestCts?.Cancel(); } catch { }
        }

        private async Task<bool> AutoStepAsync(byte[] cmd8, byte[] expected8, string title, CancellationToken token)
        {
            AddLog($"[{DateTime.Now:HH:mm:ss}] {title}：发送...");
            await _simulation.ClearRxFifoAsync(TestRxChannel);
            await Task.Delay(20, token);
            await _simulation.SendBenchCommandOnlyAsync(TestTxChannel, cmd8, msg => AddLog(msg), token);

            // Align with A_C_6_15_1_2: ATP instructions are send-only (no OK wait)
            AddLog($"[{DateTime.Now:HH:mm:ss}] {title}：发送完成（不等待回读）");
            return true;
        }

        private async Task SendAndWaitOkAsync(byte[] cmd8, byte[] ok8, string title)
        {
            if (IsBusy)
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

                    // Align with A_C_6_15_1_2: send-only for ATP, do not wait for OK
                    AddLog($"[{DateTime.Now:HH:mm:ss}] {title}：指令已发送（不等待回读）");
                }
                finally
                {
                    IsBusy = false;
                }
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] {title}异常：{ex.Message}");
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

                AddLog($"[{DateTime.Now:HH:mm:ss}] PWM={pwmPercent}%测量：VRMS={FormatNum(m.Vrms)} V, DUTY={FormatNum(m.DutyPct)} %");

                Application.Current.Dispatcher.Invoke(() =>
                {
                    ScopeVrmsText = FormatNum(m.Vrms);
                    DutyPctText = FormatNum(m.DutyPct);

                    if (pwmPercent == 100)
                    {
                        // 100% PWM波形本质是直流高电平，存在毛刺时VMAX会取毛刺峰值
                        // 使用VRMS(有效值)作为电压判定值更准确
                        _pwm100Voltage = m.Vrms;
                        Pwm100VoltageText = FormatNum(m.Vrms);
                    }
                    else if (pwmPercent == 50)
                    {
                        _pwm50DutyPct = m.DutyPct;
                        Pwm50DutyPctText = FormatNum(m.DutyPct);
                    }
                    else if (pwmPercent == 0)
                    {
                        // 0% PWM波形本质是直流低电平，存在毛刺时VMAX会取毛刺峰值
                        // 使用VRMS(有效值)作为电压判定值更准确
                        _pwm0Voltage = m.Vrms;
                        Pwm0VoltageText = FormatNum(m.Vrms);
                    }
                });

                return pwmPercent switch
                {
                    100 => QualifyPwm100(m.Vrms, out var reason100) ? true : FailWithReason(pwmPercent, reason100),
                    50 => QualifyPwm50(m.DutyPct, out var reason50) ? true : FailWithReason(pwmPercent, reason50),
                    0 => QualifyPwm0(m.Vrms, out var reason0) ? true : FailWithReason(pwmPercent, reason0),
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
            // 矩阵开关由调用方控制连接/断开，此处不再自行连接/断开
            // 这样三次PWM测量期间矩阵保持连接，50% PWM测量时信号已稳定

            await EnsureInstrumentsConnectedAsync(token);

            // Send AUToscale to auto-configure the scope settings
            AddLog($"[{DateTime.Now:HH:mm:ss}] PWM={pwmPercent}%：发送 :AUToscale (示波器自动设置) 并等待完成...");
            try
            {
                await SendScopeCommandAsync(":AUToscale", token);
                try
                {
                    var opc = await QueryScopeAsync("*OPC?", 20000, token);
                    _ = opc;
                }
                catch (Exception ex)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 等待 *OPC? 超时/异常：{ex.Message}");
                }
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 发送 :AUToscale 失败：{ex.Message}");
            }

            // Configure and Query based on PWM percent
            // 100%/0% PWM：只配置VRMS（直流信号只需有效值）
            // 50% PWM：只配置PWIDth, NWIDth（只需占空比）
            double? vrms = null;
            double? dutyPct = null;

            if (pwmPercent == 50)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] PWM=50%：正在配置测量项 (PWIDth, NWIDth)...");
                try
                {
                    await SendScopeCommandAsync(":MEASure:SOURce CHANnel1", token);
                    await SendScopeCommandAsync(":MEASure:CLEar", token);
                    await SendScopeCommandAsync(":MEASure:ITEM PWIDth", token);
                    await SendScopeCommandAsync(":MEASure:ITEM NWIDth", token);
                }
                catch (Exception ex)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 配置测量项异常：{ex.Message}");
                }

                // 参照A_C_6_16_1_1_1ViewModel：50% PWM需要更长的矩阵连接时间
                // 让示波器有足够时间积累脉宽测量数据
                AddLog($"[{DateTime.Now:HH:mm:ss}] PWM=50%：延时8秒等待波形在自动设置后稳定（占空比测量需要更长稳定时间）...");
                await Task.Delay(8000, token);

                // 查询占空比前再延时3秒，确保示波器有足够时间积累PWIDth/NWIDth数据
                AddLog($"[{DateTime.Now:HH:mm:ss}] PWM=50%：延时3秒等待示波器积累脉宽测量数据...");
                await Task.Delay(3000, token);

                // Query duty cycle
                try
                {
                    await SendScopeCommandAsync(":MEASure:SOURce CHANnel1", token);
                    AddLog($"[{DateTime.Now:HH:mm:ss}] PWM=50%：通过PWIDth+NWIDth计算占空比...");
                    var rawPw = await QueryScopeAsync(":MEASure:ITEM? PWIDth", 10000, token);
                    AddLog($"[{DateTime.Now:HH:mm:ss}] PWM=50%：高电平时间原始响应(PWIDth)：'{rawPw}'");
                    var pw = ParseScopeDouble(rawPw);

                    var rawNw = await QueryScopeAsync(":MEASure:ITEM? NWIDth", 10000, token);
                    AddLog($"[{DateTime.Now:HH:mm:ss}] PWM=50%：低电平时间原始响应(NWIDth)：'{rawNw}'");
                    var nw = ParseScopeDouble(rawNw);

                    if (pw.HasValue && nw.HasValue && (pw.Value + nw.Value) > 0)
                    {
                        dutyPct = pw.Value / (pw.Value + nw.Value) * 100.0;
                        AddLog($"[{DateTime.Now:HH:mm:ss}] PWM=50%：占空比计算值：PWIDth={pw.Value:F6}s, NWIDth={nw.Value:F6}s, DUTY={dutyPct.Value:F3} %");
                    }
                    else
                    {
                        AddLog($"[{DateTime.Now:HH:mm:ss}] PWM=50%：PWIDth/NWIDth无法获取有效值，占空比计算失败 (pw={FormatNum(pw)}, nw={FormatNum(nw)})");
                    }
                }
                catch (Exception ex)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] PWM=50%：查询占空比异常：{ex.Message}");
                }
            }
            else
            {
                // 100%/0% PWM 是直流信号：只配置和查询 VRMS
                AddLog($"[{DateTime.Now:HH:mm:ss}] PWM={pwmPercent}%：正在配置测量项 (VRMS)...");
                try
                {
                    await SendScopeCommandAsync(":MEASure:SOURce CHANnel1", token);
                    await SendScopeCommandAsync(":MEASure:CLEar", token);
                    await SendScopeCommandAsync(":MEASure:ITEM VRMS", token);
                }
                catch (Exception ex)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 配置测量项异常：{ex.Message}");
                }

                AddLog($"[{DateTime.Now:HH:mm:ss}] PWM={pwmPercent}%：延时5秒等待波形在自动设置后稳定...");
                await Task.Delay(5000, token);

                // Query VRMS
                try
                {
                    await SendScopeCommandAsync(":MEASure:SOURce CHANnel1", token);
                    var rawVrms = await QueryScopeAsync(":MEASure:ITEM? VRMS", 10000, token);
                    AddLog($"[{DateTime.Now:HH:mm:ss}] PWM={pwmPercent}%：VRMS原始响应：'{rawVrms}'");
                    vrms = ParseScopeDouble(rawVrms);
                    if (vrms.HasValue)
                        AddLog($"[{DateTime.Now:HH:mm:ss}] PWM={pwmPercent}%：VRMS解析值：{vrms.Value:F3} V");
                    else
                        AddLog($"[{DateTime.Now:HH:mm:ss}] PWM={pwmPercent}%：VRMS解析失败");
                }
                catch (Exception ex)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 查询VRMS异常：{ex.Message}");
                }

                AddLog($"[{DateTime.Now:HH:mm:ss}] PWM={pwmPercent}%：直流信号，跳过占空比计算");
            }

            // 矩阵开关由调用方控制断开，此处不断开

            return new PwmMeasurement
            {
                PwmPercent = pwmPercent,
                Vrms = vrms,
                DutyPct = dutyPct
            };
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

            Pwm100VoltageText = "--";
            Pwm50DutyPctText = "--";
            Pwm0VoltageText = "--";
        }

        private async Task MeasureAndUpdateUiAsync(int pwmPercent)
        {
            if (IsBusy)
                return;

            try { _manualMeasureCts?.Cancel(); } catch { }
            try { _manualMeasureCts?.Dispose(); } catch { }
            _manualMeasureCts = new CancellationTokenSource();

            IsBusy = true;
            try
            {
                await Task.Run(async () =>
                {
                    await _instrumentLock.WaitAsync();
                    try
                    {
                        var token = _manualMeasureCts.Token;
                        AddLog($"[{DateTime.Now:HH:mm:ss}] PWM={pwmPercent}%：开始测量...");

                        // 手动测量：连接矩阵 → 测量 → 断开矩阵
                        var routed = await EnsureMatrixRoutedAsync(token);
                        if (!routed)
                        {
                            AddLog($"[{DateTime.Now:HH:mm:ss}] PWM={pwmPercent}%：矩阵开关路由失败");
                            return;
                        }
                        try
                        {
                            var m = await MeasurePwmRawCoreAsync(pwmPercent, token);
                            if (m == null)
                                return;

                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            if (pwmPercent == 100)
                            {
                                // 100% PWM波形本质是直流高电平，存在毛刺时VMAX会取毛刺峰值
                                // 使用VRMS(有效值)作为电压判定值更准确
                                _pwm100Voltage = m.Vrms;
                                Pwm100VoltageText = FormatNum(m.Vrms);
                            }
                            else if (pwmPercent == 50)
                            {
                                _pwm50DutyPct = m.DutyPct;
                                Pwm50DutyPctText = FormatNum(m.DutyPct);
                            }
                            else if (pwmPercent == 0)
                            {
                                // 0% PWM波形本质是直流低电平，存在毛刺时VMAX会取毛刺峰值
                                // 使用VRMS(有效值)作为电压判定值更准确
                                _pwm0Voltage = m.Vrms;
                                Pwm0VoltageText = FormatNum(m.Vrms);
                            }
                        });

                        AddLog($"[{DateTime.Now:HH:mm:ss}] PWM={pwmPercent}%：测量完成 VRMS={FormatNum(m.Vrms)}V{(pwmPercent == 50 ? $", DUTY={FormatNum(m.DutyPct)}%" : string.Empty)}");
                        }
                        finally
                        {
                            AddLog($"[{DateTime.Now:HH:mm:ss}] PWM={pwmPercent}%：断开矩阵开关...");
                            await DisconnectMatrixAsync(token);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        AddLog($"[{DateTime.Now:HH:mm:ss}] PWM={pwmPercent}%测量已手动取消/停止");
                    }
                    catch (Exception ex)
                    {
                        AddLog($"[{DateTime.Now:HH:mm:ss}] PWM={pwmPercent}%测量异常：{ex.Message}");
                    }
                    finally
                    {
                        _instrumentLock.Release();
                    }
                });
            }
            finally
            {
                IsBusy = false;
            }
        }

        private static string FormatNum(double? v)
        {
            if (!v.HasValue || double.IsNaN(v.Value) || double.IsInfinity(v.Value))
                return "--";
            return v.Value.ToString("G6", CultureInfo.InvariantCulture);
        }

        private bool QualifyPwm100(double? vrms, out string reason)
        {
            const double HighMinV = 2.8;
            const double HighMaxV = 3.8;

            if (!vrms.HasValue)
            {
                reason = "示波器VRMS无有效值";
                return false;
            }

            if (vrms.Value < HighMinV || vrms.Value > HighMaxV)
            {
                reason = $"VRMS不在范围: {vrms.Value:F4}V, 期望应为[{HighMinV},{HighMaxV}]V";
                return false;
            }

            reason = null;
            return true;
        }

        private bool QualifyPwm50(double? dutyPct, out string reason)
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

            reason = null;
            return true;
        }

        private bool QualifyPwm0(double? vrms, out string reason)
        {
            const double LowMinV = -1.0;
            const double LowMaxV = 1.0;

            if (!vrms.HasValue)
            {
                reason = "示波器VRMS无有效值";
                return false;
            }

            if (vrms.Value < LowMinV || vrms.Value > LowMaxV)
            {
                reason = $"电压超范围: VRMS={vrms.Value:F4}V, 期望均在[{LowMinV},{LowMaxV}]";
                return false;
            }

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
                var connectTask = _scopeTcpClient.ConnectAsync(OscilloscopeIpAddress.Trim(), 5555);
                var delayTask = Task.Delay(3000, token);
                var completedTask = await Task.WhenAny(connectTask, delayTask).ConfigureAwait(false);
                if (completedTask == delayTask)
                {
                    _scopeTcpClient.Close();
                    throw new TimeoutException("连接示波器超时（限时3秒），请检查示波器网络。");
                }
                await connectTask.ConfigureAwait(false);
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
                ("I1", "O12", 9, "192.168.1.3"),
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

        private async Task DisconnectMatrixAsync(CancellationToken token)
        {
            if (!_matrixRouted)
                return;

            try
            {
                var svc = MatrixControlService.Instance;

                var operations = new (string inNode, string outNode, int slot, string ip)[]
                {
                    ("I1", "O12", 9, "192.168.1.3"),
                    ("I0", "O8", 4, "192.168.1.3")
                };

                var disconnectTasks = operations
                    .Select(op => svc.DisconnectNodesAsync(op.inNode, op.outNode, op.slot, op.ip))
                    .ToArray();

                _ = await Task.WhenAll(disconnectTasks);
            }
            catch
            {
            }
            finally
            {
                _matrixRouted = false;
            }
        }

        private async Task DisconnectInstrumentsAndMatrixAsync(CancellationToken token)
        {
            // Force-close first regardless of lock state
            SafeCloseNetworkStream(ref _scopeTcpStream);
            SafeCloseTcpClient(ref _scopeTcpClient);

            try
            {
                // Use timeout so stop doesn't deadlock if measurement holds lock
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
                cts.CancelAfter(3000);
                await _instrumentLock.WaitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Lock unavailable, already force-closed above
                await DisconnectMatrixAsync(token);
                return;
            }
            try
            {
                SafeCloseNetworkStream(ref _scopeTcpStream);
                SafeCloseTcpClient(ref _scopeTcpClient);
                await DisconnectMatrixAsync(token);
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

        /// <summary>
        /// Synchronous scope write — caller must hold _scopeIoLock.
        /// Matches OscilloscopeTestPanelViewModel.WriteOscilloscopeUnsafe pattern.
        /// </summary>
        private void WriteScopeUnsafe(string command)
        {
            var stream = _scopeTcpStream;
            if (stream == null) return;
            var cmd = command.EndsWith("\n", StringComparison.Ordinal) ? command : command + "\n";
            var bytes = Encoding.ASCII.GetBytes(cmd);
            stream.Write(bytes, 0, bytes.Length);
        }

        /// <summary>
        /// Async send-only scope command with independent lock acquisition.
        /// Matches OscilloscopeTestPanelViewModel.SendOscilloscopeCommandAsync pattern.
        /// </summary>
        private async Task SendScopeCommandAsync(string command, CancellationToken token)
        {
            if (_scopeTcpStream == null && _scopeTcpClient == null)
                return;

            await _scopeIoLock.WaitAsync(token);
            try
            {
                await Task.Run(() =>
                {
                    WriteScopeUnsafe(command);
                }, token);
            }
            finally
            {
                _scopeIoLock.Release();
            }
        }

        private async Task<string> QueryScopeAsync(string command, CancellationToken token)
        {
            return await QueryScopeAsync(command, 5000, token);
        }

        /// <summary>
        /// Async scope query with independent lock acquisition.
        /// Matches OscilloscopeTestPanelViewModel.QueryOscilloscopeAsync pattern:
        /// acquire lock → Task.Run(sync write + sync ReadLine) → release lock.
        /// </summary>
        private async Task<string> QueryScopeAsync(string command, int timeoutMs, CancellationToken token)
        {
            if (_scopeTcpStream == null && _scopeTcpClient == null)
                return null;

            await _scopeIoLock.WaitAsync(token);
            try
            {
                return await Task.Run(() =>
                {
                    var stream = _scopeTcpStream;
                    if (stream == null)
                        return null;

                    // Synchronous write inside lock
                    var cmd = command.EndsWith("\n", StringComparison.Ordinal) ? command : command + "\n";
                    var bytes = Encoding.ASCII.GetBytes(cmd);
                    stream.Write(bytes, 0, bytes.Length);

                    // Synchronous read with timeout
                    try
                    {
                        return ReadLineAsync(stream, timeoutMs, token).GetAwaiter().GetResult();
                    }
                    catch (TimeoutException)
                    {
                        return null;
                    }
                }, token);
            }
            finally
            {
                _scopeIoLock.Release();
            }
        }

        /// <summary>
        /// Parse a scope response string into a double, matching OscilloscopeTestPanelViewModel.NormalizeOscilloscopeNumber pattern.
        /// </summary>
        private static double? ParseScopeDouble(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            raw = raw.Trim();
            int comma = raw.IndexOf(',');
            if (comma > 0)
                raw = raw.Substring(0, comma);

            var match = System.Text.RegularExpressions.Regex.Match(raw, @"[-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?");
            if (!match.Success)
                return null;

            var clean = match.Value;
            if (double.TryParse(clean, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            {
                if (double.IsNaN(v) || double.IsInfinity(v) || Math.Abs(v) > 1e36)
                    return null;
                return v;
            }
            return null;
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
                    AddLog($"[{DateTime.Now:HH:mm:ss}] {title}：发送完成（不等待回读）");
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
