using Prism.Commands;
using Prism.Mvvm;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MeasureControl.Helpers;
using MeasureControl.Services;
using MeasureControl.Simulations.A_C_6_17_2_2;

namespace MeasureControl.ViewModels.SingleBoardTest.AirController
{
    public sealed class A_C_6_17_2_2ViewModel : BindableBase, IDisposable
    {
        private static readonly byte[] EnterAtpCommand8 = { 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] EnterAtpOk8 = { 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x02 };
        private static readonly byte[] ExitAtpCommand8 = { 0x00, 0x02, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01 };
        private static readonly byte[] ExitAtpOk8 = { 0x00, 0x02, 0x00, 0x01, 0x00, 0x00, 0x00, 0x03 };

        private static readonly byte[] DirHighCommand8 = { 0x22, 0x02, 0x03, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] DirLowCommand8 = { 0x22, 0x02, 0x03, 0x02, 0x00, 0x00, 0x00, 0x00 };

        private const string MatrixIp = "192.168.1.3";

        private readonly A_C_6_17_2_2Simulation _simulation = new A_C_6_17_2_2Simulation();

        private readonly SemaphoreSlim _manualTestLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _autoTestLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _arincOpLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _instrumentLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _scopeIoLock = new SemaphoreSlim(1, 1);

        private CancellationTokenSource _autoTestCts;

        private bool _matrixRouted;

        private TcpClient _scopeTcpClient;
        private NetworkStream _scopeTcpStream;

        private string _testTxChannel = "CH0";
        private string _testRxChannel = "CH2";
        private double _arincRate = 100000.0;

        private string _oscilloscopeIpAddress = "192.168.1.18";

        private bool _isBusy;
        private bool _isManualTestRunning;
        private bool _isAutoTestRunning;

        private string _enterAtpRxDataText = "--";
        private string _exitAtpRxDataText = "--";

        private double? _dirHighLevel;
        private double? _dirLowLevel;

        private string _dirHighVmaxText = "--";
        private string _dirHighVminText = "--";
        private string _dirLowVmaxText = "--";
        private string _dirLowVminText = "--";

        private ImageSource _waveformImage;

        private string _lastTestTime = "--";
        private string _lastTestResult = "--";
        private string _previousTestTime = "--";
        private string _previousTestResult = "--";

        public A_C_6_17_2_2ViewModel()
        {
            ManualTestCommand = new DelegateCommand(OnManualTest);
            AutoTestCommand = new DelegateCommand(OnAutoTest);
            ClearLogCommand = new DelegateCommand(() => Logs.Clear());

            SendEnterAtpCommand = new DelegateCommand(async () => await SendAndWaitOkAsync(EnterAtpCommand8, EnterAtpOk8, "进入ATP"));
            SendExitAtpCommand = new DelegateCommand(async () => await SendAndWaitOkAsync(ExitAtpCommand8, ExitAtpOk8, "退出ATP"));

            RunDirHighAndMeasureCommand = new DelegateCommand(async () => await SendDirAndMeasureAsync(DirHighCommand8, isHigh: true, title: "DIR引脚引入高电平"));
            RunDirLowAndMeasureCommand = new DelegateCommand(async () => await SendDirAndMeasureAsync(DirLowCommand8, isHigh: false, title: "DIR引脚引入低电平"));
        }

        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();

        public DelegateCommand ManualTestCommand { get; }
        public DelegateCommand AutoTestCommand { get; }
        public DelegateCommand ClearLogCommand { get; }

        public DelegateCommand SendEnterAtpCommand { get; }
        public DelegateCommand SendExitAtpCommand { get; }
        public DelegateCommand RunDirHighAndMeasureCommand { get; }
        public DelegateCommand RunDirLowAndMeasureCommand { get; }

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

        public string DirHighVmaxText
        {
            get => _dirHighVmaxText;
            private set => SetProperty(ref _dirHighVmaxText, value);
        }

        public string DirHighVminText
        {
            get => _dirHighVminText;
            private set => SetProperty(ref _dirHighVminText, value);
        }

        public string DirLowVmaxText
        {
            get => _dirLowVmaxText;
            private set => SetProperty(ref _dirLowVmaxText, value);
        }

        public string DirLowVminText
        {
            get => _dirLowVminText;
            private set => SetProperty(ref _dirLowVminText, value);
        }

        public ImageSource WaveformImage
        {
            get => _waveformImage;
            private set => SetProperty(ref _waveformImage, value);
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

        private void ResetUi()
        {
            EnterAtpRxDataText = "--";
            ExitAtpRxDataText = "--";
            _dirHighLevel = null;
            _dirLowLevel = null;
            DirHighVmaxText = "--";
            DirHighVminText = "--";
            DirLowVmaxText = "--";
            DirLowVminText = "--";
            WaveformImage = null;
            LastTestTime = "--";
            LastTestResult = "--";
            PreviousTestTime = "--";
            PreviousTestResult = "--";
        }

        private static bool QualifyDirLevel(bool isHigh, double? level, out string reason)
        {
            const double HighMinV = 3.0;
            const double HighMaxV = 3.6;
            const double LowMinV = -1.0;
            const double LowMaxV = 1.0;

            if (!level.HasValue)
            {
                reason = isHigh ? "DIR高电平电压无有效值" : "DIR低电平电压无有效值";
                return false;
            }

            if (isHigh)
            {
                if (level.Value < HighMinV || level.Value > HighMaxV)
                {
                    reason = $"DIR高电平电压幅值应为[{HighMinV:0.0},{HighMaxV:0.0}]V，当前={level.Value:0.00000}V";
                    return false;
                }
            }
            else
            {
                if (level.Value < LowMinV || level.Value > LowMaxV)
                {
                    reason = $"DIR低电平电压幅值应为[{LowMinV:0.0},{LowMaxV:0.0}]V，当前={level.Value:0.00000}V";
                    return false;
                }
            }

            reason = null;
            return true;
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

                IsBusy = true;
                try
                {
                    ResetUi();

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
                    ResetUi();
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
                    ResetUi();

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

                    if (failures.Count == 0)
                    {
                        if (!await SendDirAndMeasureCoreAsync(DirHighCommand8, isHigh: true, token))
                            failures.Add("DIR高电平发送/测量失败");
                        if (!await SendDirAndMeasureCoreAsync(DirLowCommand8, isHigh: false, token))
                            failures.Add("DIR低电平发送/测量失败");

                        if (failures.Count == 0)
                        {
                            if (!QualifyDirLevel(true, _dirHighLevel, out var reasonHigh))
                                failures.Add(reasonHigh ?? "DIR高电平判据不合格");
                            if (!QualifyDirLevel(false, _dirLowLevel, out var reasonLow))
                                failures.Add(reasonLow ?? "DIR低电平判据不合格");
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

            AddLog($"[{DateTime.Now:HH:mm:ss}] {title}：OK (0x{FormatBytesHex(resp)})");
            if (cmd8.SequenceEqual(EnterAtpCommand8))
                EnterAtpRxDataText = $"0x{FormatBytesHex(resp)}";
            if (cmd8.SequenceEqual(ExitAtpCommand8))
                ExitAtpRxDataText = $"0x{FormatBytesHex(resp)}";
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

                    AddLog($"[{DateTime.Now:HH:mm:ss}] {title}：OK (0x{FormatBytesHex(resp)})");

                    if (cmd8.SequenceEqual(EnterAtpCommand8))
                        EnterAtpRxDataText = $"0x{FormatBytesHex(resp)}";
                    else if (cmd8.SequenceEqual(ExitAtpCommand8))
                        ExitAtpRxDataText = $"0x{FormatBytesHex(resp)}";

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

        private async Task SendDirAndMeasureAsync(byte[] cmd8, bool isHigh, string title)
        {
            if (!IsManualTestRunning || IsBusy)
                return;

            await _instrumentLock.WaitAsync();
            try
            {
                IsBusy = true;
                var token = CancellationToken.None;
                AddLog($"[{DateTime.Now:HH:mm:ss}] {title}：开始...");

                var ok = await SendDirAndMeasureCoreAsync(cmd8, isHigh, token);
                if (!ok)
                {
                    SetLastTestResult("FAIL");
                    return;
                }

                if (QualifyDirLevel(isHigh, isHigh ? _dirHighLevel : _dirLowLevel, out var reason))
                {
                    SetLastTestResult("PASS");
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(reason))
                        AddLog($"[{DateTime.Now:HH:mm:ss}] 判据FAIL：{reason}");
                    SetLastTestResult("FAIL");
                }
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] {title}异常：{ex.Message}");
                SetLastTestResult("FAIL");
            }
            finally
            {
                IsBusy = false;
                _instrumentLock.Release();
            }
        }

        private async Task<bool> SendDirAndMeasureCoreAsync(byte[] cmd8, bool isHigh, CancellationToken token)
        {
            if (!await EnsureMatrixRoutedAsync(token))
                return false;

            await EnsureInstrumentsConnectedAsync(token);

            await Task.Delay(20, token);
            await _simulation.SendBenchCommandOnlyAsync(TestTxChannel, cmd8, msg => AddLog(msg), token);
            await Task.Delay(200, token);
            var vmax = await QueryScopeDoubleAsync(1, ":MEASure:ITEM? VMAX", token);
            var vmin = await QueryScopeDoubleAsync(1, ":MEASure:ITEM? VMIN", token);

            if (!vmax.HasValue)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] VMAX测量无有效值");
                return false;
            }

            var level = vmax;

            var screenshotBytes = await CaptureScopeScreenshotAsync(token);
            if (screenshotBytes != null && screenshotBytes.Length > 0)
            {
                var img = CreateBitmapImage(screenshotBytes);
                await Application.Current.Dispatcher.InvokeAsync(() => WaveformImage = img);
            }

            if (isHigh)
            {
                _dirHighLevel = level;
                if (vmax.HasValue) DirHighVmaxText = $"{vmax.Value:0.00000} V";
                if (vmin.HasValue) DirHighVminText = $"{vmin.Value:0.00000} V";
                AddLog($"[{DateTime.Now:HH:mm:ss}] DIR高电平 VMAX={(vmax.HasValue ? vmax.Value.ToString("0.00000", CultureInfo.InvariantCulture) : "--")} V, VMIN={(vmin.HasValue ? vmin.Value.ToString("0.00000", CultureInfo.InvariantCulture) : "--")} V");
            }
            else
            {
                _dirLowLevel = level;
                if (vmax.HasValue) DirLowVmaxText = $"{vmax.Value:0.00000} V";
                if (vmin.HasValue) DirLowVminText = $"{vmin.Value:0.00000} V";
                AddLog($"[{DateTime.Now:HH:mm:ss}] DIR低电平 VMAX={(vmax.HasValue ? vmax.Value.ToString("0.00000", CultureInfo.InvariantCulture) : "--")} V, VMIN={(vmin.HasValue ? vmin.Value.ToString("0.00000", CultureInfo.InvariantCulture) : "--")} V");
            }

            return true;
        }

        private async Task<bool> EnsureMatrixRoutedAsync(CancellationToken token)
        {
            if (_matrixRouted)
                return true;

            var svc = MatrixControlService.Instance;

            var operations = new (string inNode, string outNode, int slot, string ip)[]
            {
                ("I1", "O17", 9, MatrixIp),
                ("I0", "O8", 4, MatrixIp)
            };

            var connectTasks = operations
                .Select(op => svc.ConnectNodesAsync(op.inNode, op.outNode, op.slot, op.ip))
                .ToArray();

            var results = await Task.WhenAll(connectTasks);
            _matrixRouted = results.All(r => r);

            if (_matrixRouted)
                await Task.Delay(200, token);
            _ = token;
            AddLog($"[{DateTime.Now:HH:mm:ss}] 矩阵开关通路(示波器): I1->O17 slot=9 + I0->O8 slot=4, ip={MatrixIp}, ok={_matrixRouted}");
            return _matrixRouted;
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

                await QueryScopeAsync(":MEASure:CLEar", token);
            }
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
                            ("I1", "O17", 9, MatrixIp),
                            ("I0", "O8", 4, MatrixIp)
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

        private async Task<byte[]> CaptureScopeScreenshotAsync(CancellationToken token)
        {
            if (_scopeTcpStream == null)
                return null;

            await _scopeIoLock.WaitAsync(token);
            try
            {
                var cmd = Encoding.ASCII.GetBytes(":DISPlay:DATA?\n");
                await _scopeTcpStream.WriteAsync(cmd, 0, cmd.Length, token);
                await _scopeTcpStream.FlushAsync(token);

                return await Task.Run(() => ReadIeee4882DefiniteLengthBlock(_scopeTcpStream, 100_000_000), token);
            }
            catch
            {
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

        private static BitmapImage CreateBitmapImage(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                return null;

            var img = new BitmapImage();
            using (var ms = new MemoryStream(bytes))
            {
                img.BeginInit();
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.StreamSource = ms;
                img.EndInit();
                img.Freeze();
            }
            return img;
        }

        private static byte[] ReadIeee4882DefiniteLengthBlock(NetworkStream stream, int maxBytes)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));
            if (maxBytes <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxBytes));

            try
            {
                int b;
                do
                {
                    b = stream.ReadByte();
                    if (b < 0)
                        return Array.Empty<byte>();
                } while (b != '#');

                int n = stream.ReadByte();
                if (n < 0)
                    return Array.Empty<byte>();
                int nDigits = n - '0';
                if (nDigits < 0 || nDigits > 9)
                    return Array.Empty<byte>();

                var lenBuf = new byte[nDigits];
                ReadExact(stream, lenBuf, 0, nDigits);
                if (!int.TryParse(Encoding.ASCII.GetString(lenBuf), out var payloadLen) || payloadLen < 0)
                    return Array.Empty<byte>();
                if (payloadLen > maxBytes)
                    return Array.Empty<byte>();

                var payload = new byte[payloadLen];
                ReadExact(stream, payload, 0, payloadLen);

                try
                {
                    while (stream.DataAvailable)
                    {
                        int next = stream.ReadByte();
                        if (next < 0)
                            break;
                        if (next != '\n' && next != '\r')
                            break;
                    }
                }
                catch
                {
                }

                return payload;
            }
            catch
            {
                return Array.Empty<byte>();
            }
        }

        private static void ReadExact(NetworkStream stream, byte[] buffer, int offset, int count)
        {
            int read = 0;
            while (read < count)
            {
                int r = stream.Read(buffer, offset + read, count - read);
                if (r <= 0)
                    throw new IOException("网络读取失败");
                read += r;
            }
        }

        private static string FormatBytesHex(byte[] data)
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
