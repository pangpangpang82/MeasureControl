using Prism.Commands;
using Prism.Mvvm;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MeasureControl.Helpers;
using MeasureControl.Simulations.Common;
using MeasureControl.Simulations.S_C_8_3_3;

namespace MeasureControl.ViewModels.SingleBoardTest.AirController
{
    public sealed class S_C_8_3_3ViewModel : BindableBase, IDisposable
    {
        private const string FixedTxChannel = "429_CH2";
        private const string FixedRxChannel = "429_CH0";

        private static readonly byte[] EnterAtpCommand8 = { 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] EnterAtpOk8 = { 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x02 };
        private static readonly byte[] ExitAtpCommand8 = { 0x00, 0x02, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01 };
        private static readonly byte[] ExitAtpOk8 = { 0x00, 0x02, 0x00, 0x01, 0x00, 0x00, 0x00, 0x03 };

        private static readonly byte[] SArinc429In02Command8 = { 0x13, 0x02, 0x02, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] SArinc429In02OkPrefix4 = { 0x13, 0x02, 0x02, 0x02 };

        private static readonly byte[] TestData4 = { 0x01, 0x01, 0x01, 0x01 };

        private static readonly byte[] ExpectedReceiveResp8 = { 0x13, 0x02, 0x02, 0x02, 0x01, 0x01, 0x01, 0x80 };

        private readonly S_C_8_3_3Simulation _simulation = new S_C_8_3_3Simulation();
        private readonly SemaphoreSlim _arincOpLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _manualTestLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _autoTestLock = new SemaphoreSlim(1, 1);

        private CancellationTokenSource _autoTestCts;

        private string _testTxChannel;
        private string _testRxChannel;

        private string _enterAtpTxChannel;
        private string _enterAtpRxChannel;
        private string _exitAtpTxChannel;
        private string _exitAtpRxChannel;

        private string _enterAtpRxDataText;
        private string _rxDataText;
        private string _exitAtpRxDataText;

        private string _lastTestTime;
        private string _lastTestResult;
        private string _previousTestTime;
        private string _previousTestResult;

        private ImageSource _currentStepImage;

        private bool _isManualTestRunning;
        private bool _isAutoTestRunning;
        private bool _isBusy;

        private double _arincRate = 100000.0;

        public S_C_8_3_3ViewModel()
        {
            _testTxChannel = FixedTxChannel;
            _testRxChannel = FixedRxChannel;

            _enterAtpTxChannel = _testTxChannel;
            _enterAtpRxChannel = _testRxChannel;
            _exitAtpTxChannel = _testTxChannel;
            _exitAtpRxChannel = _testRxChannel;

            EnterAtpRxDataText = "--";
            RxDataText = "--";
            ExitAtpRxDataText = "--";

            LastTestTime = "--";
            LastTestResult = "--";
            PreviousTestTime = "--";
            PreviousTestResult = "--";

            CurrentStepImage = CreateImageSource("/Resources/Logo/begin.png");

            ManualTestCommand = new DelegateCommand(OnManualTest);
            AutoTestCommand = new DelegateCommand(OnAutoTest);

            SendEnterAtpCommand = new DelegateCommand(async () => await OnSendEnterAtpAsync());
            SendTestDataCommand = new DelegateCommand(async () => await OnSendTestDataAsync());
            SendReceiveCommand = new DelegateCommand(async () => await OnSendReceiveAsync());
            SendExitAtpCommand = new DelegateCommand(async () => await OnSendExitAtpAsync());
            ClearLogCommand = new DelegateCommand(() => Logs.Clear());
        }

        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();

        public DelegateCommand ManualTestCommand { get; }
        public DelegateCommand AutoTestCommand { get; }
        public DelegateCommand SendEnterAtpCommand { get; }
        public DelegateCommand SendTestDataCommand { get; }
        public DelegateCommand SendReceiveCommand { get; }
        public DelegateCommand SendExitAtpCommand { get; }
        public DelegateCommand ClearLogCommand { get; }

        public string TestTxChannel
        {
            get => _testTxChannel;
        }

        public string TestRxChannel
        {
            get => _testRxChannel;
        }

        public string EnterAtpTxChannel
        {
            get => _enterAtpTxChannel;
        }

        public string EnterAtpRxChannel
        {
            get => _enterAtpRxChannel;
        }

        public string ExitAtpTxChannel
        {
            get => _exitAtpTxChannel;
        }

        public string ExitAtpRxChannel
        {
            get => _exitAtpRxChannel;
        }

        public string EnterAtpRxDataText
        {
            get => _enterAtpRxDataText;
            set => SetProperty(ref _enterAtpRxDataText, value);
        }

        public string RxDataText
        {
            get => _rxDataText;
            set => SetProperty(ref _rxDataText, value);
        }

        public string ExitAtpRxDataText
        {
            get => _exitAtpRxDataText;
            set => SetProperty(ref _exitAtpRxDataText, value);
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

        public string PreviousTestTime
        {
            get => _previousTestTime;
            set => SetProperty(ref _previousTestTime, value);
        }

        public string PreviousTestResult
        {
            get => _previousTestResult;
            set => SetProperty(ref _previousTestResult, value);
        }

        public ImageSource CurrentStepImage
        {
            get => _currentStepImage;
            set => SetProperty(ref _currentStepImage, value);
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

        public bool IsBusy
        {
            get => _isBusy;
            set => SetProperty(ref _isBusy, value);
        }

        public double ArincRate
        {
            get => _arincRate;
            set => SetProperty(ref _arincRate, value);
        }

        private void AddLog(string msg)
        {
            try
            {
                Application.Current?.Dispatcher?.Invoke(() => Logs.Add(msg));
            }
            catch
            {
                Logs.Add(msg);
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
                    IsManualTestRunning = true;

                    EnterAtpRxDataText = "--";
                    RxDataText = "--";
                    ExitAtpRxDataText = "--";

                    LastTestTime = "--";
                    LastTestResult = "--";
                    CurrentStepImage = CreateImageSource("/Resources/Logo/begin.png");

                    _simulation.IsRealProduct = AppConstants.Arinc429IsRealProduct;
                    _simulation.ArincRate = ArincRate;
                    _simulation.SimProductArincRate = ArincRate;

                    if (!_simulation.IsRealProduct)
                    {
                        if (!TrySetupSimChannelMapping(out var mapError))
                            throw new InvalidOperationException(mapError);
                    }

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试启动({(_simulation.IsRealProduct ? "真实产品模式" : "仿真模式")})：TX={TestTxChannel}, RX={TestRxChannel}");

                    try
                    {
                        var api = Prism.Ioc.ContainerLocator.Container.Resolve(typeof(MeasureControl.Services.HardwareApis.IComponentPowerStateApi)) as MeasureControl.Services.HardwareApis.IComponentPowerStateApi;
                        if (api != null)
                            await api.ApplyComponent28VStateAsync(CancellationToken.None);
                    }
                    catch { }

                    await _simulation.StartAsync(TestTxChannel, TestRxChannel, msg => AddLog(msg));
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试启动完成");
                }
                catch (Exception ex)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试启动失败: {ex.Message}");
                    CurrentStepImage = CreateImageSource("/Resources/Logo/warning.png");
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
            await _manualTestLock.WaitAsync();
            try
            {
                if (IsBusy)
                    return;

                IsBusy = true;
                try
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试停止中...");
                    await _simulation.StopAsync(msg => AddLog(msg));
                    IsManualTestRunning = false;
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试已停止");
                }
                catch (Exception ex)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试停止异常: {ex.Message}");
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

        private void OnAutoTest()
        {
            if (IsAutoTestRunning)
            {
                _ = StopAutoTestAsync();
                return;
            }

            _ = RunAutoTestAsync();
        }

        private async Task StopAutoTestAsync()
        {
            await _autoTestLock.WaitAsync();
            try
            {
                _autoTestCts?.Cancel();
            }
            finally
            {
                _autoTestLock.Release();
            }
        }

        private async Task OnSendEnterAtpAsync()
        {
            if (!IsManualTestRunning || IsBusy)
                return;

            if (!string.Equals(EnterAtpTxChannel, TestTxChannel, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(EnterAtpRxChannel, TestRxChannel, StringComparison.OrdinalIgnoreCase))
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 进入ATP使用测试通道：TX={TestTxChannel}, RX={TestRxChannel}");
                return;
            }

            await _arincOpLock.WaitAsync();
            try
            {
                IsBusy = true;
                try
                {
                    CurrentStepImage = CreateImageSource("/Resources/Logo/communicate.png");
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
                        CurrentStepImage = CreateImageSource("/Resources/Logo/warning.png");
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
                CurrentStepImage = CreateImageSource("/Resources/Logo/warning.png");
            }
            finally
            {
                _arincOpLock.Release();
            }
        }

        private async Task OnSendTestDataAsync()
        {
            if (!IsManualTestRunning || IsBusy)
                return;

            await _arincOpLock.WaitAsync();
            try
            {
                IsBusy = true;
                try
                {
                    CurrentStepImage = CreateImageSource("/Resources/Logo/communicate.png");
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 发送测试信息：{FormatBytes(TestData4)}");

                    await _simulation.SendBenchWord32Async(TestTxChannel, 0x01010101, msg => AddLog(msg), CancellationToken.None);
                }
                finally
                {
                    IsBusy = false;
                }
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 发送测试信息异常: {ex.Message}");
                CurrentStepImage = CreateImageSource("/Resources/Logo/warning.png");
            }
            finally
            {
                _arincOpLock.Release();
            }
        }

        private async Task OnSendReceiveAsync()
        {
            if (!IsManualTestRunning || IsBusy)
                return;

            await _arincOpLock.WaitAsync();
            try
            {
                IsBusy = true;
                try
                {
                    CurrentStepImage = CreateImageSource("/Resources/Logo/communicate.png");

                    await _simulation.ClearRxFifoAsync(TestRxChannel);
                    await Task.Delay(20);

                    var token = CancellationToken.None;

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 发送接收指令：{FormatBytes(SArinc429In02Command8)}");
                    await _simulation.SendBenchCommandOnlyAsync(TestTxChannel, SArinc429In02Command8, msg => AddLog(msg), token);

                    var resp8 = await _simulation.WaitBenchResponse8Async(
                        TestRxChannel,
                        b => b != null && b.Length == 8 && b[0] == SArinc429In02OkPrefix4[0] && b[1] == SArinc429In02OkPrefix4[1] && b[2] == SArinc429In02OkPrefix4[2] && b[3] == SArinc429In02OkPrefix4[3],
                        timeoutMs: 1200,
                        log: msg => AddLog(msg),
                        token: token);

                    if (resp8 == null)
                    {
                        AddLog($"[{DateTime.Now:HH:mm:ss}] 接收回传超时");
                        CurrentStepImage = CreateImageSource("/Resources/Logo/warning.png");
                        return;
                    }

                    RxDataText = $"0x{FormatBytesHex(resp8)}";

                    bool pass = resp8.SequenceEqual(ExpectedReceiveResp8);
                    SetLastTestResult(pass ? "PASS" : "FAIL");
                    CurrentStepImage = CreateImageSource(pass ? "/Resources/Logo/over.png" : "/Resources/Logo/warning.png");

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 接收通道测试结果：{LastTestResult}");
                }
                finally
                {
                    IsBusy = false;
                }
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 接收指令异常: {ex.Message}");
                CurrentStepImage = CreateImageSource("/Resources/Logo/warning.png");
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
                AddLog($"[{DateTime.Now:HH:mm:ss}] 退出ATP使用测试通道：TX={TestTxChannel}, RX={TestRxChannel}");
                return;
            }

            await _arincOpLock.WaitAsync();
            try
            {
                IsBusy = true;
                try
                {
                    CurrentStepImage = CreateImageSource("/Resources/Logo/communicate.png");
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
                        CurrentStepImage = CreateImageSource("/Resources/Logo/warning.png");
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
                CurrentStepImage = CreateImageSource("/Resources/Logo/warning.png");
            }
            finally
            {
                _arincOpLock.Release();
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
                try
                {
                    IsAutoTestRunning = true;

                    EnterAtpRxDataText = "--";
                    RxDataText = "--";
                    ExitAtpRxDataText = "--";

                    LastTestTime = "--";
                    LastTestResult = "--";
                    CurrentStepImage = CreateImageSource("/Resources/Logo/begin.png");

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

                    _simulation.IsRealProduct = AppConstants.Arinc429IsRealProduct;
                    _simulation.ArincRate = ArincRate;
                    _simulation.SimProductArincRate = ArincRate;

                    if (!_simulation.IsRealProduct)
                    {
                        if (!TrySetupSimChannelMapping(out var mapError))
                            throw new InvalidOperationException(mapError);
                    }

                    AddLog($"[{DateTime.Now:HH:mm:ss}] ========== 自动测试开始 ==========");

                    await _simulation.StartAsync(TestTxChannel, TestRxChannel, msg => AddLog(msg));

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 步骤1：进入ATP");
                    CurrentStepImage = CreateImageSource("/Resources/Logo/communicate.png");
                    await _simulation.ClearRxFifoAsync(TestRxChannel);
                    await Task.Delay(20, token);
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
                        CurrentStepImage = CreateImageSource("/Resources/Logo/warning.png");
                        AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试失败：进入ATP超时");
                        return;
                    }

                    EnterAtpRxDataText = $"0x{FormatBytesHex(enterOk)}";

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 步骤2：发送测试信息01010101");
                    CurrentStepImage = CreateImageSource("/Resources/Logo/communicate.png");
                    await _simulation.SendBenchWord32Async(TestTxChannel, 0x01010101, msg => AddLog(msg), token);
                    await Task.Delay(30, token);

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 步骤3：发送S_ARINC429_IN02并等待回传");
                    CurrentStepImage = CreateImageSource("/Resources/Logo/communicate.png");
                    await _simulation.ClearRxFifoAsync(TestRxChannel);
                    await Task.Delay(20, token);
                    await _simulation.SendBenchCommandOnlyAsync(TestTxChannel, SArinc429In02Command8, msg => AddLog(msg), token);

                    var rx8 = await _simulation.WaitBenchResponse8Async(
                        TestRxChannel,
                        b => b != null && b.Length == 8 && b[0] == SArinc429In02OkPrefix4[0] && b[1] == SArinc429In02OkPrefix4[1] && b[2] == SArinc429In02OkPrefix4[2] && b[3] == SArinc429In02OkPrefix4[3],
                        timeoutMs: 1200,
                        log: msg => AddLog(msg),
                        token: token);

                    if (rx8 == null)
                    {
                        SetLastTestResult("FAIL");
                        CurrentStepImage = CreateImageSource("/Resources/Logo/warning.png");
                        AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试失败：回传超时");
                        return;
                    }

                    RxDataText = $"0x{FormatBytesHex(rx8)}";

                    bool pass = rx8.SequenceEqual(ExpectedReceiveResp8);
                    SetLastTestResult(pass ? "PASS" : "FAIL");
                    CurrentStepImage = CreateImageSource(pass ? "/Resources/Logo/over.png" : "/Resources/Logo/warning.png");

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 步骤4：退出ATP");
                    await _simulation.ClearRxFifoAsync(TestRxChannel);
                    await Task.Delay(20, token);
                    await _simulation.SendBenchCommandOnlyAsync(TestTxChannel, ExitAtpCommand8, msg => AddLog(msg), token);

                    var exitOk = await _simulation.WaitBenchResponse8Async(
                        TestRxChannel,
                        b => b != null && b.SequenceEqual(ExitAtpOk8),
                        timeoutMs: 1200,
                        log: msg => AddLog(msg),
                        token: token);

                    if (exitOk != null)
                        ExitAtpRxDataText = $"0x{FormatBytesHex(exitOk)}";

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试结束：{LastTestResult}");
                }
                catch (OperationCanceledException)
                {
                    SetLastTestResult("FAIL");
                    CurrentStepImage = CreateImageSource("/Resources/Logo/warning.png");
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试已取消");
                }
                catch (Exception ex)
                {
                    SetLastTestResult("FAIL");
                    CurrentStepImage = CreateImageSource("/Resources/Logo/warning.png");
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试异常: {ex.Message}");
                }
                finally
                {
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

        private bool TrySetupSimChannelMapping(out string error)
        {
            error = null;

            int tx = ARINC429SimulationBase.ParseChannelIndex(TestTxChannel);
            int rx = ARINC429SimulationBase.ParseChannelIndex(TestRxChannel);

            if (tx < 0 || rx < 0)
            {
                error = "通道索引无效";
                return false;
            }

            int maxBench = Math.Max(tx, rx);
            if (maxBench > 11)
            {
                error = "当前仿真映射规则要求 bench 通道索引 <= 11（因为产品侧使用 bench+4）";
                return false;
            }

            _simulation.SimProductRxChannelIndex = tx + 4;
            _simulation.SimProductTxChannelIndex = rx + 4;
            return true;
        }

        private void SetLastTestResult(string result)
        {
            var now = DateTime.Now;

            PreviousTestTime = LastTestTime;
            PreviousTestResult = LastTestResult;

            LastTestTime = now.ToString("yyyy-MM-dd HH:mm:ss");
            LastTestResult = result;
        }

        private static ImageSource CreateImageSource(string path)
        {
            try
            {
                return new BitmapImage(new Uri($"pack://application:,,,{path}", UriKind.Absolute));
            }
            catch
            {
                return null;
            }
        }

        private static string FormatBytesHex(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                return string.Empty;
            return string.Join(" ", bytes.Select(b => b.ToString("X2")));
        }

        private static string FormatBytes(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                return string.Empty;
            return string.Join(" ", bytes.Select(b => b.ToString("X2")));
        }

        public void Dispose()
        {
            try
            {
                _autoTestCts?.Cancel();
                _autoTestCts?.Dispose();
            }
            catch
            {
            }

            try
            {
                _simulation?.Dispose();
            }
            catch
            {
            }

            try
            {
                _arincOpLock?.Dispose();
                _manualTestLock?.Dispose();
                _autoTestLock?.Dispose();
            }
            catch
            {
            }
        }
    }
}
