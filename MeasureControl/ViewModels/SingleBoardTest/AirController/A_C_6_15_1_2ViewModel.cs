using Prism.Commands;
using Prism.Mvvm;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using MeasureControl.Helpers;
using MeasureControl.Services;
using MeasureControl.Services.HardwareApis;
using MeasureControl.Simulations.A_C_6_15_1_2;

namespace MeasureControl.ViewModels.SingleBoardTest.AirController
{
    public sealed class A_C_6_15_1_2ViewModel : BindableBase, IDisposable
    {
        private const string FixedTxChannel = "429_CH0";
        private const string FixedRxChannel = "429_CH2";

        private static readonly byte[] EnterAtpCommand8 = { 0x30, 0x01, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] EnterAtpOk8 = { 0x30, 0x01, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00 }; // not used (send-only)
        private static readonly byte[] ExitAtpCommand8 = { 0x30, 0x02, 0x02, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] ExitAtpOk8 = { 0x30, 0x02, 0x02, 0x01, 0x00, 0x00, 0x00, 0x00 }; // not used (send-only)

        private static readonly byte[] PhHighCommand8 = { 0x21, 0x03, 0x02, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] PhLowCommand8 = { 0x21, 0x03, 0x02, 0x02, 0x00, 0x00, 0x00, 0x00 };

        private const string MatrixIp = "192.168.1.3";
        private const int MatrixHighSlotIndex = 9;
        private const int MatrixLowSlotIndex = 4;

        private readonly A_C_6_15_1_2Simulation _simulation = new A_C_6_15_1_2Simulation();

        private readonly SemaphoreSlim _manualTestLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _autoTestLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _arincOpLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _instrumentLock = new SemaphoreSlim(1, 1);

        private CancellationTokenSource _autoTestCts;

        private bool _matrixHighRouted;
        private bool _matrixLowRouted;

        private readonly string _testTxChannel;
        private readonly string _testRxChannel;
        private double _arincRate = 100000.0;

        private string _dmmIpAddress = "192.168.1.13";

        private bool _isBusy;
        private bool _isManualTestRunning;
        private bool _isAutoTestRunning;

        private string _enterAtpRxDataText = "--";
        private string _exitAtpRxDataText = "--";

        private double? _phHighVoltage;
        private double? _phLowVoltage;

        private string _phHighVoltageText = "--";
        private string _phLowVoltageText = "--";

        private string _lastTestTime = "--";
        private string _lastTestResult = "--";
        private string _previousTestTime = "--";
        private string _previousTestResult = "--";

        public A_C_6_15_1_2ViewModel()
        {
            _testTxChannel = FixedTxChannel;
            _testRxChannel = FixedRxChannel;

            ManualTestCommand = new DelegateCommand(OnManualTest);
            AutoTestCommand = new DelegateCommand(OnAutoTest);
            ClearLogCommand = new DelegateCommand(() => Logs.Clear());

            SendEnterAtpCommand = new DelegateCommand(async () => await SendAndWaitOkAsync(EnterAtpCommand8, EnterAtpOk8, "进入ATP"));
            SendExitAtpCommand = new DelegateCommand(async () => await SendAndWaitOkAsync(ExitAtpCommand8, ExitAtpOk8, "退出ATP"));

            RunPhHighAndMeasureCommand = new DelegateCommand(async () => await SendPhAndMeasureAsync(PhHighCommand8, isHigh: true, title: "PH引脚高电平"));
            RunPhLowAndMeasureCommand = new DelegateCommand(async () => await SendPhAndMeasureAsync(PhLowCommand8, isHigh: false, title: "PH引脚低电平"));
        }

        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();

        public DelegateCommand ManualTestCommand { get; }
        public DelegateCommand AutoTestCommand { get; }
        public DelegateCommand ClearLogCommand { get; }

        public DelegateCommand SendEnterAtpCommand { get; }
        public DelegateCommand SendExitAtpCommand { get; }
        public DelegateCommand RunPhHighAndMeasureCommand { get; }
        public DelegateCommand RunPhLowAndMeasureCommand { get; }

        public string TestTxChannel
        {
            get => _testTxChannel;
        }

        public string TestRxChannel
        {
            get => _testRxChannel;
        }

        public double ArincRate
        {
            get => _arincRate;
            set => SetProperty(ref _arincRate, value);
        }

        public string DmmIpAddress
        {
            get => _dmmIpAddress;
            set => SetProperty(ref _dmmIpAddress, value);
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

        public string PhHighVoltageText
        {
            get => _phHighVoltageText;
            private set => SetProperty(ref _phHighVoltageText, value);
        }

        public string PhLowVoltageText
        {
            get => _phLowVoltageText;
            private set => SetProperty(ref _phLowVoltageText, value);
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

                    _simulation.IsRealProduct = true;
                    _simulation.ArincRate = ArincRate;

                    AddLog($"[{DateTime.Now:HH:mm:ss}] ========== 手动测试开始 ==========");

                    // removed power on step per requirement

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
                    try { await DisconnectMatrixAsync(CancellationToken.None); } catch { }
                    ResetUi();
                    IsManualTestRunning = false;
                    AddLog($"[{DateTime.Now:HH:mm:ss}] ========== 手动测试已停止 ==========");
                }
                finally
                {
                    // removed power off step per requirement
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

                // removed power on step per requirement

                try
                {
                    ResetUi();

                    _simulation.IsRealProduct = true;
                    _simulation.ArincRate = ArincRate;

                    AddLog($"[{DateTime.Now:HH:mm:ss}] ========== 自动测试开始 ==========");
                    await _simulation.StartAsync(TestTxChannel, TestRxChannel, msg => AddLog(msg));

                    var failures = new System.Collections.Generic.List<string>();

                    if (!await AutoStepAsync(EnterAtpCommand8, EnterAtpOk8, "进入ATP", token))
                        failures.Add("进入ATP失败");

                    if (failures.Count == 0)
                    {
                        if (!await SendPhAndMeasureCoreAsync(PhHighCommand8, isHigh: true, token))
                            failures.Add("PH高电平发送/测量失败");
                        if (!await SendPhAndMeasureCoreAsync(PhLowCommand8, isHigh: false, token))
                            failures.Add("PH低电平发送/测量失败");

                        if (!QualifyVoltages(_phHighVoltage, _phLowVoltage, out var reason))
                            failures.Add(reason ?? "电压判据不合格");
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
                    try { await DisconnectMatrixAsync(CancellationToken.None); } catch { }
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

            // Align with A_C_6_5_2_1: all ATP and other commands are send-only (no OK wait)
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

                    // Align with A_C_6_5_2_1: send-only for ATP, do not wait for OK
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

        private async Task SendPhAndMeasureAsync(byte[] cmd8, bool isHigh, string title)
        {
            if (IsBusy)
                return;

            await _instrumentLock.WaitAsync();
            try
            {
                IsBusy = true;
                var token = CancellationToken.None;
                AddLog($"[{DateTime.Now:HH:mm:ss}] {title}：开始...");

                var ok = await SendPhAndMeasureCoreAsync(cmd8, isHigh, token);
                if (!ok)
                {
                    SetLastTestResult("FAIL");
                    return;
                }

                if (QualifyVoltages(_phHighVoltage, _phLowVoltage, out var reason))
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

        private async Task<bool> SendPhAndMeasureCoreAsync(byte[] cmd8, bool isHigh, CancellationToken token)
        {
            if (!await EnsureMatrixRoutedAsync(isHigh, token))
                return false;

            await Task.Delay(20, token);
            await _simulation.SendBenchCommandOnlyAsync(TestTxChannel, cmd8, msg => AddLog(msg), token);
            await Task.Delay(80, token);

            var v = await ReadDmmVoltageAsync(isHigh, token);
            if (!v.HasValue)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 电压测量无有效值");
                return false;
            }

            if (isHigh)
            {
                _phHighVoltage = v;
                PhHighVoltageText = $"{v.Value:0.00000} V";
                AddLog($"[{DateTime.Now:HH:mm:ss}] PH高电平电压={v.Value:0.00000} V");
            }
            else
            {
                _phLowVoltage = v;
                PhLowVoltageText = $"{v.Value:0.00000} V";
                AddLog($"[{DateTime.Now:HH:mm:ss}] PH低电平电压={v.Value:0.00000} V");
            }

            return true;
        }

        private async Task<double?> ReadDmmVoltageAsync(bool isHigh, CancellationToken token)
        {
            var ip = (DmmIpAddress ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(ip))
                throw new InvalidOperationException("DmmIpAddress 为空");

            await using IDmmApi dmm = new DmmSocketApi();
            try
            {
                await dmm.ConnectAsync(ip, token);
                var r = await dmm.ReadOnceAsync(DmmMeasureMode.DCV, new DmmReadOptions { TimeoutMilliseconds = 8000 }, token);
                if (r == null)
                    return null;
                if (r.IsOverrange)
                    return null;
                return r.Value;
            }
            finally
            {
                try { await dmm.DisconnectAsync(token); } catch { }
            }
        }

        private async Task<bool> EnsureMatrixRoutedAsync(bool isHigh, CancellationToken token)
        {
            var svc = MatrixControlService.Instance;

            if (_matrixHighRouted && _matrixLowRouted)
                return true;

            var connectTasks = new[]
            {
                svc.ConnectNodesAsync("I0", "O1", MatrixHighSlotIndex, MatrixIp),
                svc.ConnectNodesAsync("I4", "O7", MatrixLowSlotIndex, MatrixIp)
            };

            var results = await Task.WhenAll(connectTasks);
            var ok = results.All(r => r);
            _matrixHighRouted = ok;
            _matrixLowRouted = ok;
            _ = token;
            AddLog($"[{DateTime.Now:HH:mm:ss}] 矩阵开关通路(PH{(isHigh ? "高" : "低")}): I0->O1 slot={MatrixHighSlotIndex} & I4->O7 slot={MatrixLowSlotIndex} ip={MatrixIp}, ok={ok}");
            return ok;
        }

        private async Task DisconnectMatrixAsync(CancellationToken token)
        {
            var svc = MatrixControlService.Instance;

            try
            {
                if (_matrixHighRouted || _matrixLowRouted)
                {
                    var disconnectTasks = new[]
                    {
                        svc.DisconnectNodesAsync("I0", "O1", MatrixHighSlotIndex, MatrixIp),
                        svc.DisconnectNodesAsync("I4", "O7", MatrixLowSlotIndex, MatrixIp)
                    };

                    _ = await Task.WhenAll(disconnectTasks);
                }
            }
            catch
            {
            }

            _matrixHighRouted = false;
            _matrixLowRouted = false;
            _ = token;
        }

        private static bool QualifyVoltages(double? vHigh, double? vLow, out string reason)
        {
            const double HighMinV = 3.0;
            const double HighMaxV = 3.6;
            const double LowMinV = -1.0;
            const double LowMaxV = 1.0;

            if (!vHigh.HasValue)
            {
                reason = "PH高电平电压无有效值";
                return false;
            }

            if (!vLow.HasValue)
            {
                reason = "PH低电平电压无有效值";
                return false;
            }

            if (vHigh.Value < HighMinV || vHigh.Value > HighMaxV)
            {
                reason = $"PH高电平电压幅值应为[{HighMinV:0.0},{HighMaxV:0.0}]V，当前={vHigh.Value:0.00000}V";
                return false;
            }

            if (vLow.Value < LowMinV || vLow.Value > LowMaxV)
            {
                reason = $"PH低电平电压幅值应为[{LowMinV:0.0},{LowMaxV:0.0}]V，当前={vLow.Value:0.00000}V";
                return false;
            }

            reason = null;
            return true;
        }

        private void SetLastTestResult(string result)
        {
            PreviousTestTime = LastTestTime;
            PreviousTestResult = LastTestResult;
            LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            LastTestResult = result;
        }

        private void ResetUi()
        {
            EnterAtpRxDataText = "--";
            ExitAtpRxDataText = "--";
            _phHighVoltage = null;
            _phLowVoltage = null;
            PhHighVoltageText = "--";
            PhLowVoltageText = "--";
            LastTestTime = "--";
            LastTestResult = "--";
            PreviousTestTime = "--";
            PreviousTestResult = "--";
        }

        private static string FormatBytesHex(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                return string.Empty;
            return string.Join(string.Empty, bytes.Select(b => b.ToString("X2")));
        }

        public void Dispose()
        {
            try { _autoTestCts?.Cancel(); } catch { }
            try { _autoTestCts?.Dispose(); } catch { }
            try { _simulation.StopAsync(_ => { }).GetAwaiter().GetResult(); } catch { }
            try { DisconnectMatrixAsync(CancellationToken.None).GetAwaiter().GetResult(); } catch { }

            try { _manualTestLock.Dispose(); } catch { }
            try { _autoTestLock.Dispose(); } catch { }
            try { _arincOpLock.Dispose(); } catch { }
            try { _instrumentLock.Dispose(); } catch { }
        }
    }
}
