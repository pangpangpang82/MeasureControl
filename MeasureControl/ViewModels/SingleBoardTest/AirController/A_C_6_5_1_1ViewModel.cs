using Prism.Commands;
using Prism.Mvvm;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MeasureControl.Helpers;
using MeasureControl.Simulations.A_C_6_5_1_1;

namespace MeasureControl.ViewModels.SingleBoardTest.AirController
{
    public sealed class A_C_6_5_1_1ViewModel : BindableBase, IDisposable
    {
        private const string FixedTxChannel = "429_CH0";
        private const string FixedRxChannel = "429_CH2";

        private static readonly byte[] EnterAtpCommand8 = { 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] EnterAtpOk8 = { 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x02 };
        private static readonly byte[] ExitAtpCommand8 = { 0x00, 0x02, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01 };
        private static readonly byte[] ExitAtpOk8 = { 0x00, 0x02, 0x00, 0x01, 0x00, 0x00, 0x00, 0x03 };

        private static readonly byte[] AbA429Tx0TransmitCommand8 = { 0x04, 0x01, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] ExpectedData4 = { 0x7F, 0x00, 0xAA, 0x55 };
        private const byte Label50 = 0x50;
        private const byte Label51 = 0x51;

        private readonly A_C_6_5_1_1Simulation _simulation = new A_C_6_5_1_1Simulation();
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
        private string _exitAtpRxDataText;

        private string _rxConfirmText;
        private string _rxDataText;
        private string _lastTestTime;
        private string _lastTestResult;
        private string _previousTestTime;
        private string _previousTestResult;

        private ImageSource _currentStepImage;

        private bool _isManualTestRunning;
        private bool _isAutoTestRunning;
        private bool _isBusy;

        private double _arincRate = 100000.0;

        public A_C_6_5_1_1ViewModel()
        {
            _testTxChannel = FixedTxChannel;
            _testRxChannel = FixedRxChannel;

            _enterAtpTxChannel = _testTxChannel;
            _enterAtpRxChannel = _testRxChannel;
            _exitAtpTxChannel = _testTxChannel;
            _exitAtpRxChannel = _testRxChannel;

            RxConfirmText = "--";
            RxDataText = "--";
            EnterAtpRxDataText = "--";
            ExitAtpRxDataText = "--";
            LastTestTime = "--";
            LastTestResult = "--";
            PreviousTestTime = "--";
            PreviousTestResult = "--";
            CurrentStepImage = CreateImageSource("/Resources/Logo/begin.png");

            ManualTestCommand = new DelegateCommand(OnManualTest);
            AutoTestCommand = new DelegateCommand(OnAutoTest);

            SendEnterAtpCommand = new DelegateCommand(async () => await OnSendEnterAtpAsync());
            SendTestCommand = new DelegateCommand(async () => await OnSendTestAsync());
            SendExitAtpCommand = new DelegateCommand(async () => await OnSendExitAtpAsync());
            ClearLogCommand = new DelegateCommand(() => Logs.Clear());
        }

        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();

        public DelegateCommand ManualTestCommand { get; }
        public DelegateCommand AutoTestCommand { get; }
        public DelegateCommand SendEnterAtpCommand { get; }
        public DelegateCommand SendTestCommand { get; }
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

        public double ArincRate
        {
            get => _arincRate;
            set => SetProperty(ref _arincRate, value);
        }

        public string RxConfirmText
        {
            get => _rxConfirmText;
            private set => SetProperty(ref _rxConfirmText, value);
        }

        public string RxDataText
        {
            get => _rxDataText;
            private set => SetProperty(ref _rxDataText, value);
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

        public ImageSource CurrentStepImage
        {
            get => _currentStepImage;
            private set => SetProperty(ref _currentStepImage, value);
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
                    RxConfirmText = "--";
                    RxDataText = "--";
                    EnterAtpRxDataText = "--";
                    ExitAtpRxDataText = "--";
                    LastTestTime = "--";
                    LastTestResult = "--";
                    CurrentStepImage = CreateImageSource("/Resources/Logo/begin.png");

                    _simulation.IsRealProduct = AppConstants.Arinc429IsRealProduct;
                    _simulation.ArincRate = ArincRate;
                    _simulation.SimProductArincRate = ArincRate;
                    _simulation.SimProductRxChannelIndex = 4;
                    _simulation.SimProductTxChannelIndex = 5;

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试启动({(_simulation.IsRealProduct ? "真实产品模式" : "仿真模式")})：开始打开设备");
                    await _simulation.StartAsync(TestTxChannel, TestRxChannel, msg => AddLog(msg));
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试已启动：可发送测试指令");
                }
                catch (Exception ex)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试启动失败: {ex.Message}");
                    IsManualTestRunning = false;
                    CurrentStepImage = CreateImageSource("/Resources/Logo/warning.png");
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
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试停止：释放仿真资源");
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
                    CurrentStepImage = CreateImageSource("/Resources/Logo/begin.png");
                }
            }
            finally
            {
                _manualTestLock.Release();
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
                    CurrentStepImage = CreateImageSource("/Resources/Logo/communicate.png");
                    await _simulation.ClearRxFifoAsync(TestRxChannel);
                    await Task.Delay(20);

                    var token = CancellationToken.None;

                    RxConfirmText = "--";

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 发送AB_A429TX0_TRANSMIT：{FormatBytes(AbA429Tx0TransmitCommand8)}");
                    await _simulation.SendBenchCommandOnlyAsync(TestTxChannel, AbA429Tx0TransmitCommand8, msg => AddLog(msg), token);

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 等待接收数据(LABEL0x{Label50:X2}/0x{Label51:X2})...");
                    var data4 = await _simulation.WaitBenchData4Async(
                        TestRxChannel,
                        Label50,
                        Label51,
                        timeoutMs: 1200,
                        log: msg => AddLog(msg),
                        token: token);

                    if (data4 == null)
                    {
                        AddLog($"[{DateTime.Now:HH:mm:ss}] 接收数据超时");
                        SetLastTestResult("FAIL");
                        CurrentStepImage = CreateImageSource("/Resources/Logo/warning.png");
                        return;
                    }

                    RxDataText = $"{FormatBytesHex(data4)}";

                    bool pass = data4.SequenceEqual(ExpectedData4);
                    SetLastTestResult(pass ? "PASS" : "FAIL");
                    CurrentStepImage = CreateImageSource(pass ? "/Resources/Logo/over.png" : "/Resources/Logo/warning.png");

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 测试结束：{LastTestResult}");
                }
                finally
                {
                    IsBusy = false;
                }
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 发送/接收异常: {ex.Message}");
                CurrentStepImage = CreateImageSource("/Resources/Logo/warning.png");
            }
            finally
            {
                _arincOpLock.Release();
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
                    RxConfirmText = "--";
                    RxDataText = "--";
                    EnterAtpRxDataText = "--";
                    ExitAtpRxDataText = "--";
                    LastTestTime = "--";
                    LastTestResult = "--";
                    CurrentStepImage = CreateImageSource("/Resources/Logo/begin.png");

                    _autoTestCts?.Cancel();
                    _autoTestCts?.Dispose();
                    _autoTestCts = new CancellationTokenSource();

                    var token = _autoTestCts.Token;

                    _simulation.IsRealProduct = AppConstants.Arinc429IsRealProduct;
                    _simulation.ArincRate = ArincRate;
                    _simulation.SimProductArincRate = ArincRate;
                    _simulation.SimProductRxChannelIndex = 4;
                    _simulation.SimProductTxChannelIndex = 5;

                    AddLog($"[{DateTime.Now:HH:mm:ss}] ========== 自动测试开始 ==========");
                    await _simulation.StartAsync(TestTxChannel, TestRxChannel, msg => AddLog(msg));

                    await _simulation.ClearRxFifoAsync(TestRxChannel);
                    await Task.Delay(20, token);

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 步骤1：进入ATP");
                    CurrentStepImage = CreateImageSource("/Resources/Logo/communicate.png");
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

                    await _simulation.ClearRxFifoAsync(TestRxChannel);
                    await Task.Delay(20, token);

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 步骤2：发送AB_A429TX0_TRANSMIT");
                    CurrentStepImage = CreateImageSource("/Resources/Logo/communicate.png");
                    RxConfirmText = "--";
                    await _simulation.SendBenchCommandOnlyAsync(TestTxChannel, AbA429Tx0TransmitCommand8, msg => AddLog(msg), token);

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 步骤3：等待接收数据(LABEL50/51)");
                    var data4 = await _simulation.WaitBenchData4Async(
                        TestRxChannel,
                        Label50,
                        Label51,
                        timeoutMs: 1200,
                        log: msg => AddLog(msg),
                        token: token);

                    if (data4 == null)
                    {
                        SetLastTestResult("FAIL");
                        CurrentStepImage = CreateImageSource("/Resources/Logo/warning.png");
                        AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试失败：接收数据超时");
                        return;
                    }

                    RxDataText = $"{FormatBytesHex(data4)}";

                    bool pass = data4.SequenceEqual(ExpectedData4);
                    SetLastTestResult(pass ? "PASS" : "FAIL");
                    CurrentStepImage = CreateImageSource(pass ? "/Resources/Logo/over.png" : "/Resources/Logo/warning.png");

                    await _simulation.ClearRxFifoAsync(TestRxChannel);
                    await Task.Delay(20, token);

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 步骤4：退出ATP");
                    CurrentStepImage = CreateImageSource("/Resources/Logo/communicate.png");
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

        private static ImageSource CreateImageSource(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                return null;
            }

            try
            {
                var uri = new Uri($"pack://application:,,,{relativePath}", UriKind.Absolute);
                var img = new BitmapImage();
                img.BeginInit();
                img.UriSource = uri;
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.EndInit();
                img.Freeze();
                return img;
            }
            catch
            {
                return null;
            }
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
            }
            catch
            {
            }
        }
    }
}
