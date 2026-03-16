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
using MeasureControl.Simulations.A_C_6_18_2_1;

namespace MeasureControl.ViewModels.SingleBoardTest.AirController
{
    public sealed class A_C_6_18_2_1ViewModel : BindableBase, IDisposable
    {
        private static readonly byte[] EnterAtpCommand8 = { 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] EnterAtpOk8 = { 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x02 };
        private static readonly byte[] ExitAtpCommand8 = { 0x00, 0x02, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01 };
        private static readonly byte[] ExitAtpOk8 = { 0x00, 0x02, 0x00, 0x01, 0x00, 0x00, 0x00, 0x03 };

        private static readonly byte[] TestCommand8 = { 0x23, 0x01, 0x02, 0x01, 0x00, 0x00, 0x00, 0x00 };

        private const double VoltageLowerLimit = 3.0;
        private const double VoltageUpperLimit = 3.6;

        private const string FixedDmmIpAddress = "192.168.1.13";
        private const string MatrixIpAddress = "192.168.1.3";
        private const int MatrixSlotIndex = 4;

        private readonly A_C_6_18_2_1Simulation _simulation = new A_C_6_18_2_1Simulation();
        private readonly SemaphoreSlim _arincOpLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _manualTestLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _autoTestLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _matrixSwitchLock = new SemaphoreSlim(1, 1);

        private CancellationTokenSource _autoTestCts;

        private string _testTxChannel;
        private string _testRxChannel;

        private string _enterAtpTxChannel;
        private string _enterAtpRxChannel;
        private string _exitAtpTxChannel;
        private string _exitAtpRxChannel;

        private string _enterAtpRxDataText;
        private string _exitAtpRxDataText;

        private double? _j167Voltage;
        private string _j167VoltageText;
        private string _j167JudgeText;

        private string _lastTestTime;
        private string _lastTestResult;
        private string _previousTestTime;
        private string _previousTestResult;

        private bool _isManualTestRunning;
        private bool _isAutoTestRunning;
        private bool _isBusy;

        private double _arincRate = 100000.0;
        private bool _isRealProduct;

        public A_C_6_18_2_1ViewModel()
        {
            _testTxChannel = "429_CH0";
            _testRxChannel = "429_CH1";

            _enterAtpTxChannel = _testTxChannel;
            _enterAtpRxChannel = _testRxChannel;
            _exitAtpTxChannel = _testTxChannel;
            _exitAtpRxChannel = _testRxChannel;

            EnterAtpRxDataText = "--";
            ExitAtpRxDataText = "--";

            J167VoltageText = "--";
            J167JudgeText = "--";

            LastTestTime = "--";
            LastTestResult = "--";
            PreviousTestTime = "--";
            PreviousTestResult = "--";

            IsRealProduct = AppConstants.Arinc429IsRealProduct;

            ManualTestCommand = new DelegateCommand(OnManualTest);
            AutoTestCommand = new DelegateCommand(OnAutoTest);

            SendEnterAtpCommand = new DelegateCommand(async () => await OnSendEnterAtpAsync());
            SendTestCommand = new DelegateCommand(async () => await OnSendTestAsync());
            MeasureVoltageCommand = new DelegateCommand(async () => await OnMeasureVoltageAsync());
            SendExitAtpCommand = new DelegateCommand(async () => await OnSendExitAtpAsync());
            ClearLogCommand = new DelegateCommand(() => Logs.Clear());
        }

        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();

        public DelegateCommand ManualTestCommand { get; }
        public DelegateCommand AutoTestCommand { get; }
        public DelegateCommand SendEnterAtpCommand { get; }
        public DelegateCommand SendTestCommand { get; }
        public DelegateCommand MeasureVoltageCommand { get; }
        public DelegateCommand SendExitAtpCommand { get; }
        public DelegateCommand ClearLogCommand { get; }

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

        public double ArincRate
        {
            get => _arincRate;
            set => SetProperty(ref _arincRate, value);
        }

        public bool IsRealProduct
        {
            get => _isRealProduct;
            set => SetProperty(ref _isRealProduct, value);
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

        public string J167VoltageText
        {
            get => _j167VoltageText;
            private set => SetProperty(ref _j167VoltageText, value);
        }

        public string J167JudgeText
        {
            get => _j167JudgeText;
            private set => SetProperty(ref _j167JudgeText, value);
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
            if (IsBusy)
                return;

            await _manualTestLock.WaitAsync();
            try
            {
                if (IsBusy)
                    return;

                IsBusy = true;
                try
                {
                    IsManualTestRunning = true;
                    EnterAtpRxDataText = "--";
                    ExitAtpRxDataText = "--";
                    J167VoltageText = "--";
                    J167JudgeText = "--";
                    _j167Voltage = null;
                    LastTestTime = "--";
                    LastTestResult = "--";

                    _simulation.IsRealProduct = IsRealProduct;
                    _simulation.ArincRate = ArincRate;
                    _simulation.SimProductArincRate = ArincRate;
                    _simulation.SimProductRxChannelIndex = 4;
                    _simulation.SimProductTxChannelIndex = 5;

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试启动({(_simulation.IsRealProduct ? "真实产品模式" : "仿真模式")})：开始打开设备");

                    try
                    {
                        var api = Prism.Ioc.ContainerLocator.Container.Resolve(typeof(MeasureControl.Services.HardwareApis.IComponentPowerStateApi)) as MeasureControl.Services.HardwareApis.IComponentPowerStateApi;
                        if (api != null)
                            await api.ApplyComponent28VStateAsync(CancellationToken.None);
                    }
                    catch { }

                    await _simulation.StartAsync(TestTxChannel, TestRxChannel, msg => AddLog(msg));
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试已启动：可发送测试指令");
                }
                catch (Exception ex)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试启动失败: {ex.Message}");
                    IsManualTestRunning = false;
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
            if (IsBusy)
                return;

            await _manualTestLock.WaitAsync();
            try
            {
                if (IsBusy)
                    return;

                IsBusy = true;
                try
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试停止：释放资源");
                    await _simulation.StopAsync(msg => AddLog(msg));
                }
                catch (Exception ex)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 停止失败: {ex.Message}");
                }
                finally
                {
                    IsManualTestRunning = false;
                    IsBusy = false;
                }
            }
            finally
            {
                _manualTestLock.Release();
            }
        }

        private async Task OnSendEnterAtpAsync()
        {
            if (!IsManualTestRunning || IsBusy)
                return;

            if (!string.Equals(EnterAtpTxChannel, TestTxChannel, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(EnterAtpRxChannel, TestRxChannel, StringComparison.OrdinalIgnoreCase))
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 当前仿真仅打开通道：TX={TestTxChannel}, RX={TestRxChannel}。进入ATP的TX/RX需与其一致");
                return;
            }

            await _arincOpLock.WaitAsync();
            try
            {
                IsBusy = true;
                try
                {
                    await _simulation.ClearRxFifoAsync(EnterAtpRxChannel);
                    await Task.Delay(20);

                    var token = CancellationToken.None;

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 发送进入ATP：{FormatBytes(EnterAtpCommand8)}");
                    await _simulation.SendBenchCommandOnlyAsync(EnterAtpTxChannel, EnterAtpCommand8, msg => AddLog(msg), token);

                    var resp = await _simulation.WaitBenchResponse8Async(
                        EnterAtpRxChannel,
                        b => b != null && b.SequenceEqual(EnterAtpOk8),
                        timeoutMs: 1200,
                        log: msg => AddLog(msg),
                        token: token);

                    if (resp == null)
                    {
                        AddLog($"[{DateTime.Now:HH:mm:ss}] 进入ATP失败：未收到OK");
                        return;
                    }

                    EnterAtpRxDataText = $"0x{FormatBytesHex(resp)}";
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 进入ATP成功");
                }
                finally
                {
                    IsBusy = false;
                }
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 进入ATP异常: {ex.Message}");
            }
            finally
            {
                _arincOpLock.Release();
            }
        }

        private async Task OnSendExitAtpAsync()
        {
            if (!IsManualTestRunning || IsBusy)
                return;

            if (!string.Equals(ExitAtpTxChannel, TestTxChannel, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(ExitAtpRxChannel, TestRxChannel, StringComparison.OrdinalIgnoreCase))
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 当前仿真仅打开通道：TX={TestTxChannel}, RX={TestRxChannel}。退出ATP的TX/RX需与其一致");
                return;
            }

            await _arincOpLock.WaitAsync();
            try
            {
                IsBusy = true;
                try
                {
                    await _simulation.ClearRxFifoAsync(ExitAtpRxChannel);
                    await Task.Delay(20);

                    var token = CancellationToken.None;

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 发送退出ATP：{FormatBytes(ExitAtpCommand8)}");
                    await _simulation.SendBenchCommandOnlyAsync(ExitAtpTxChannel, ExitAtpCommand8, msg => AddLog(msg), token);

                    var resp = await _simulation.WaitBenchResponse8Async(
                        ExitAtpRxChannel,
                        b => b != null && b.SequenceEqual(ExitAtpOk8),
                        timeoutMs: 1200,
                        log: msg => AddLog(msg),
                        token: token);

                    if (resp == null)
                    {
                        AddLog($"[{DateTime.Now:HH:mm:ss}] 退出ATP失败：未收到OK");
                        return;
                    }

                    ExitAtpRxDataText = $"0x{FormatBytesHex(resp)}";
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 退出ATP成功");
                }
                finally
                {
                    IsBusy = false;
                }
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 退出ATP异常: {ex.Message}");
            }
            finally
            {
                _arincOpLock.Release();
            }
        }

        private async Task OnSendTestAsync()
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

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 发送测试指令：{FormatBytes(TestCommand8)}（无回包）");
                    await _simulation.SendBenchCommandOnlyAsync(TestTxChannel, TestCommand8, msg => AddLog(msg), token);
                }
                finally
                {
                    IsBusy = false;
                }
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 发送测试指令异常: {ex.Message}");
            }
            finally
            {
                _arincOpLock.Release();
            }
        }

        private async Task OnMeasureVoltageAsync()
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
                    var v = await ReadDmmVoltageAsync(token);
                    _j167Voltage = v;

                    if (v.HasValue)
                    {
                        J167VoltageText = $"{v.Value:0.00000} V";
                        bool pass = v.Value >= VoltageLowerLimit && v.Value <= VoltageUpperLimit;
                        J167JudgeText = pass ? "PASS" : "FAIL";
                        AddLog($"[{DateTime.Now:HH:mm:ss}] J167 电压={v.Value:0.00000} V, 判据[{VoltageLowerLimit:0.0},{VoltageUpperLimit:0.0}]V -> {J167JudgeText}");
                    }
                    else
                    {
                        J167VoltageText = "--";
                        J167JudgeText = "FAIL";
                        AddLog($"[{DateTime.Now:HH:mm:ss}] J167 电压测量无有效值");
                    }
                }
                finally
                {
                    IsBusy = false;
                }
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 电压测量异常: {ex.Message}");
            }
            finally
            {
                _arincOpLock.Release();
            }
        }

        private async Task<double?> ReadDmmVoltageAsync(CancellationToken token)
        {
            if (!IsRealProduct)
            {
                await Task.Delay(50, token);
                return 3.3;
            }

            bool matrixOk = await ConnectMatrixForJ167Async(token);
            if (!matrixOk)
                throw new InvalidOperationException("矩阵开关通路建立失败");

            await using IDmmApi dmm = new DmmSocketApi();
            try
            {
                await dmm.ConnectAsync(FixedDmmIpAddress, token);
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
                await DisconnectMatrixForJ167Async(token);
            }
        }

        private async Task<bool> ConnectMatrixForJ167Async(CancellationToken token)
        {
            await _matrixSwitchLock.WaitAsync(token);
            try
            {
                var task1 = MatrixControlService.Instance.ConnectNodesAsync("I0", "O29", 9, MatrixIpAddress);
                var task2 = MatrixControlService.Instance.ConnectNodesAsync("I4", "O7", MatrixSlotIndex, MatrixIpAddress);

                var results = await Task.WhenAll(task1, task2);
                bool ok1 = results.Length > 0 && results[0];
                bool ok2 = results.Length > 1 && results[1];

                AddLog($"[{DateTime.Now:HH:mm:ss}] 矩阵开关通路(2601): I0->O29 slot=9 ip={MatrixIpAddress}, ok={ok1}");
                AddLog($"[{DateTime.Now:HH:mm:ss}] 矩阵开关通路(2601): I4->O7 slot={MatrixSlotIndex} ip={MatrixIpAddress}, ok={ok2}");

                bool ok = results.All(r => r);
                if (ok)
                    await Task.Delay(200, token);
                return ok;
            }
            finally
            {
                _matrixSwitchLock.Release();
            }
        }

        private async Task DisconnectMatrixForJ167Async(CancellationToken token)
        {
            await _matrixSwitchLock.WaitAsync(token);
            try
            {
                var task1 = MatrixControlService.Instance.DisconnectNodesAsync("I0", "O29", 9, MatrixIpAddress);
                var task2 = MatrixControlService.Instance.DisconnectNodesAsync("I4", "O7", MatrixSlotIndex, MatrixIpAddress);

                var results = await Task.WhenAll(task1, task2);
                bool ok1 = results.Length > 0 && results[0];
                bool ok2 = results.Length > 1 && results[1];

                AddLog($"[{DateTime.Now:HH:mm:ss}] 矩阵开关断开(2601): I0->O29 slot=9 ip={MatrixIpAddress}, ok={ok1}");
                AddLog($"[{DateTime.Now:HH:mm:ss}] 矩阵开关断开(2601): I4->O7 slot={MatrixSlotIndex} ip={MatrixIpAddress}, ok={ok2}");
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 矩阵开关断开异常: {ex.Message}");
            }
            finally
            {
                _matrixSwitchLock.Release();
            }
        }

        private async Task RunAutoTestAsync()
        {
            if (IsBusy)
                return;

            await _autoTestLock.WaitAsync();
            try
            {
                if (IsBusy)
                    return;

                IsBusy = true;
                try
                {
                    IsAutoTestRunning = true;
                    EnterAtpRxDataText = "--";
                    ExitAtpRxDataText = "--";
                    J167VoltageText = "--";
                    J167JudgeText = "--";
                    _j167Voltage = null;
                    LastTestTime = "--";
                    LastTestResult = "--";

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

                    _simulation.IsRealProduct = IsRealProduct;
                    _simulation.ArincRate = ArincRate;
                    _simulation.SimProductArincRate = ArincRate;
                    _simulation.SimProductRxChannelIndex = 4;
                    _simulation.SimProductTxChannelIndex = 5;

                    AddLog($"[{DateTime.Now:HH:mm:ss}] ========== 自动测试开始 ==========");
                    await _simulation.StartAsync(TestTxChannel, TestRxChannel, msg => AddLog(msg));

                    await _simulation.ClearRxFifoAsync(TestRxChannel);
                    await Task.Delay(20, token);

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 步骤1：进入ATP");
                    await _simulation.SendBenchCommandOnlyAsync(TestTxChannel, EnterAtpCommand8, msg => AddLog(msg), token);

                    var enterOk = await _simulation.WaitBenchResponse8Async(
                        TestRxChannel,
                        b => b != null && b.SequenceEqual(EnterAtpOk8),
                        timeoutMs: 1200,
                        log: msg => AddLog(msg),
                        token: token);

                    if (enterOk == null)
                    {
                        SetLastTestResult("FAIL");
                        AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试失败：进入ATP超时");
                        return;
                    }

                    EnterAtpRxDataText = $"0x{FormatBytesHex(enterOk)}";

                    await _simulation.ClearRxFifoAsync(TestRxChannel);
                    await Task.Delay(20, token);

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 步骤2：发送测试指令（无回包）");
                    await _simulation.SendBenchCommandOnlyAsync(TestTxChannel, TestCommand8, msg => AddLog(msg), token);

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 步骤3：万用表测量J167电压");
                    var v = await ReadDmmVoltageAsync(token);
                    _j167Voltage = v;

                    if (v.HasValue)
                    {
                        J167VoltageText = $"{v.Value:0.00000} V";
                        bool pass = v.Value >= VoltageLowerLimit && v.Value <= VoltageUpperLimit;
                        J167JudgeText = pass ? "PASS" : "FAIL";
                        SetLastTestResult(pass ? "PASS" : "FAIL");
                    }
                    else
                    {
                        J167VoltageText = "--";
                        J167JudgeText = "FAIL";
                        SetLastTestResult("FAIL");
                    }

                    await _simulation.ClearRxFifoAsync(TestRxChannel);
                    await Task.Delay(20, token);

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 步骤4：退出ATP");
                    await _simulation.SendBenchCommandOnlyAsync(TestTxChannel, ExitAtpCommand8, msg => AddLog(msg), token);

                    var exitOk = await _simulation.WaitBenchResponse8Async(
                        TestRxChannel,
                        b => b != null && b.SequenceEqual(ExitAtpOk8),
                        timeoutMs: 1200,
                        log: msg => AddLog(msg),
                        token: token);

                    if (exitOk != null)
                    {
                        ExitAtpRxDataText = $"0x{FormatBytesHex(exitOk)}";
                    }

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
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试异常: {ex.Message}");
                }
                finally
                {
                    if (LastTestTime == "--")
                    {
                        LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    }

                    try
                    {
                        await _simulation.StopAsync(msg => AddLog(msg));
                    }
                    catch
                    {
                    }

                    IsAutoTestRunning = false;
                    IsBusy = false;
                }
            }
            finally
            {
                _autoTestLock.Release();
            }
        }

        private void SetLastTestResult(string result)
        {
            PreviousTestTime = LastTestTime;
            PreviousTestResult = LastTestResult;
            LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            LastTestResult = result;
        }

        private async Task StopAutoTestAsync()
        {
            try
            {
                _autoTestCts?.Cancel();
            }
            catch
            {
            }

            await Task.CompletedTask;
        }

        private static string FormatBytes(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                return string.Empty;
            return string.Join(" ", bytes.Select(b => b.ToString("X2")));
        }

        private static string FormatBytesHex(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                return string.Empty;
            return string.Join(string.Empty, bytes.Select(b => b.ToString("X2")));
        }

        public void Dispose()
        {
            try
            {
                _autoTestCts?.Cancel();
            }
            catch
            {
            }

            try
            {
                _autoTestCts?.Dispose();
            }
            catch
            {
            }

            try
            {
                _simulation.Dispose();
            }
            catch
            {
            }

            try
            {
                _arincOpLock.Dispose();
                _manualTestLock.Dispose();
                _autoTestLock.Dispose();
                _matrixSwitchLock.Dispose();
            }
            catch
            {
            }
        }
    }
}
