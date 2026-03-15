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
using MeasureControl.Simulations.A_C_6_16_3_1;

namespace MeasureControl.ViewModels.SingleBoardTest.AirController
{
    public sealed class A_C_6_16_3_1ViewModel : BindableBase, IDisposable
    {
        private static readonly byte[] EnterAtpCommand8 = { 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] EnterAtpOk8 = { 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x02 };
        private static readonly byte[] ExitAtpCommand8 = { 0x00, 0x02, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01 };
        private static readonly byte[] ExitAtpOk8 = { 0x00, 0x02, 0x00, 0x01, 0x00, 0x00, 0x00, 0x03 };

        private static readonly byte[] SupImpedanceCommand8 = { 0x22, 0x03, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00 };

        private const double QualifyFreqTargetHz = 1000.0;
        private const double QualifyFreqTolHz = 1.0;
        private const double QualifyDutyTargetPct = 50.0;
        private const double QualifyDutyTolPct = 5.0;

        private const string FixedTxChannel = "429_CH0";
        private const string FixedRxChannel = "429_CH2";

        private readonly A_C_6_16_3_1Simulation _simulation = new A_C_6_16_3_1Simulation();
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

        private string _freqHzText = "--";
        private string _dutyPctText = "--";

        private string _lastTestTime = "--";
        private string _lastTestResult = "--";

        private string _previousTestTime = "--";
        private string _previousTestResult = "--";

        public A_C_6_16_3_1ViewModel()
        {
            ManualTestCommand = new DelegateCommand(OnManualTest);
            AutoTestCommand = new DelegateCommand(OnAutoTest);
            ClearLogCommand = new DelegateCommand(() => Logs.Clear());

            SendEnterAtpCommand = new DelegateCommand(async () => await SendAndWaitOkAsync(EnterAtpCommand8, EnterAtpOk8, "进入ATP"));
            SendExitAtpCommand = new DelegateCommand(async () => await SendAndWaitOkAsync(ExitAtpCommand8, ExitAtpOk8, "退出ATP"));

            SendSupImpedanceCommand = new DelegateCommand(async () => await SendAndWaitEchoAsync(SupImpedanceCommand8, "AB_MOTORCABTAV_SUPIMPEDANCE"));
            MeasureSupImpedanceCommand = new DelegateCommand(async () => await MeasureAndUpdateUiAsync());
        }

        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();

        public DelegateCommand ManualTestCommand { get; }
        public DelegateCommand AutoTestCommand { get; }
        public DelegateCommand ClearLogCommand { get; }

        public DelegateCommand SendEnterAtpCommand { get; }
        public DelegateCommand SendExitAtpCommand { get; }
        public DelegateCommand SendSupImpedanceCommand { get; }
        public DelegateCommand MeasureSupImpedanceCommand { get; }

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

                    _simulation.IsRealProduct = AppConstants.Arinc429IsRealProduct;
                    _simulation.ArincRate = ArincRate;
                    _simulation.SimProductArincRate = ArincRate;
                    _simulation.SimProductRxChannelIndex = 4;
                    _simulation.SimProductTxChannelIndex = 5;

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
                    EnterAtpRxDataText = "--";
                    ExitAtpRxDataText = "--";
                    ClearMeasurementTexts();

                    _simulation.IsRealProduct = AppConstants.Arinc429IsRealProduct;
                    _simulation.ArincRate = ArincRate;
                    _simulation.SimProductArincRate = ArincRate;
                    _simulation.SimProductRxChannelIndex = 4;
                    _simulation.SimProductTxChannelIndex = 5;

                    AddLog($"[{DateTime.Now:HH:mm:ss}] ========== 自动测试开始 ==========");
                    await _simulation.StartAsync(TestTxChannel, TestRxChannel, msg => AddLog(msg));

                    var failures = new System.Collections.Generic.List<string>();

                    if (!await AutoStepAsync(EnterAtpCommand8, EnterAtpOk8, "进入ATP", token))
                        failures.Add("进入ATP失败");

                    if (!await AutoStepAsync(SupImpedanceCommand8, SupImpedanceCommand8, "AB_MOTORCABTAV_SUPIMPEDANCE", token))
                        failures.Add("AB_MOTORCABTAV_SUPIMPEDANCE回读失败");
                    else if (!await MeasureAndQualifyAsync(token))
                        failures.Add("占空比/频率判据不合格");

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

        private async Task MeasureAndUpdateUiAsync()
        {
            if (!IsManualTestRunning || IsBusy)
                return;

            await _instrumentLock.WaitAsync();
            try
            {
                IsBusy = true;
                var token = CancellationToken.None;
                AddLog($"[{DateTime.Now:HH:mm:ss}] 测量：开始... ");

                var m = await MeasureRawCoreAsync(token);
                if (m == null)
                    return;

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    FreqHzText = FormatNum(m.FreqHz);
                    DutyPctText = FormatNum(m.DutyPct);
                });

                AddLog($"[{DateTime.Now:HH:mm:ss}] 测量完成：F={FormatNum(m.FreqHz)} Hz, DUTY={FormatNum(m.DutyPct)} %");
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 测量异常：{ex.Message}");
            }
            finally
            {
                IsBusy = false;
                _instrumentLock.Release();
            }
        }

        private async Task<bool> MeasureAndQualifyAsync(CancellationToken token)
        {
            if (!AppConstants.Arinc429IsRealProduct)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] [SIM] 跳过示波器/频率计判据（仿真模式）");
                return true;
            }

            await _instrumentLock.WaitAsync(token);
            try
            {
                var m = await MeasureRawCoreAsync(token);
                if (m == null)
                    return false;

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    FreqHzText = FormatNum(m.FreqHz);
                    DutyPctText = FormatNum(m.DutyPct);
                });

                if (!m.FreqHz.HasValue)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 判据FAIL：频率无有效值");
                    return false;
                }

                if (Math.Abs(m.FreqHz.Value - QualifyFreqTargetHz) > QualifyFreqTolHz)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 判据FAIL：频率不合格 {m.FreqHz.Value:F3}Hz，期望 {QualifyFreqTargetHz}±{QualifyFreqTolHz}Hz");
                    return false;
                }

                if (!m.DutyPct.HasValue)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 判据FAIL：占空比无有效值");
                    return false;
                }

                if (Math.Abs(m.DutyPct.Value - QualifyDutyTargetPct) > QualifyDutyTolPct)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 判据FAIL：占空比不合格 {m.DutyPct.Value:F3}% ，期望 {QualifyDutyTargetPct}±{QualifyDutyTolPct}%");
                    return false;
                }

                AddLog($"[{DateTime.Now:HH:mm:ss}] 判据PASS：频率/占空比合格");
                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 判据测量异常：{ex.Message}");
                return false;
            }
            finally
            {
                _instrumentLock.Release();
            }
        }

        private sealed class Measurement
        {
            public double? FreqHz { get; set; }
            public double? DutyPct { get; set; }
        }

        private async Task<Measurement> MeasureRawCoreAsync(CancellationToken token)
        {
            var routed = await EnsureMatrixRoutedAsync(token);
            if (!routed)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 矩阵开关路由失败");
                return null;
            }

            await EnsureInstrumentsConnectedAsync(token);
            await Task.Delay(200, token);
            var freq = await QueryScopeDoubleAsync(1, ":MEASure:ITEM? FREQuency", token);
            double? dutyPct = await QueryScopeDutyPctAsync(1, token);
            if (!dutyPct.HasValue)
            {
                var pw = await QueryScopeDoubleAsync(1, ":MEASure:ITEM? PWIDth", token);
                var nw = await QueryScopeDoubleAsync(1, ":MEASure:ITEM? NWIDth", token);
                dutyPct = TryCalcDutyPctFromPulseWidths(pw, nw);
            }

            return new Measurement
            {
                FreqHz = freq,
                DutyPct = dutyPct
            };
        }

        private void ClearMeasurementTexts()
        {
            FreqHzText = "--";
            DutyPctText = "--";
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
                ("I1", "O7", 9, "192.168.1.3"),
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
                            ("I1", "O7", 9, "192.168.1.3"),
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

        private static double? TryCalcDutyPctFromPulseWidths(double? pwSeconds, double? nwSeconds)
        {
            if (!pwSeconds.HasValue || !nwSeconds.HasValue)
                return null;

            var pw = pwSeconds.Value;
            var nw = nwSeconds.Value;
            if (double.IsNaN(pw) || double.IsInfinity(pw) || double.IsNaN(nw) || double.IsInfinity(nw))
                return null;

            var period = pw + nw;
            if (period <= 0)
                return null;

            return pw / period * 100.0;
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
