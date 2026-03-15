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
using MeasureControl.Simulations.A_C_6_5_1_2;
using MeasureControl.Simulations.Common;

namespace MeasureControl.ViewModels.SingleBoardTest.AirController
{
    public sealed class A_C_6_5_1_2ViewModel : BindableBase, IDisposable
    {
        private const string FixedATxChannel = "429_CH3";
        private const string FixedARxChannel = "429_CH1";
        private const string FixedBTxChannel = "429_CH3";
        private const string FixedBRxChannel = "429_CH1";

        private static readonly byte[] EnterAtpCommand8 = { 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] EnterAtpOk8 = { 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x02 };
        private static readonly byte[] ExitAtpCommand8 = { 0x00, 0x02, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01 };
        private static readonly byte[] ExitAtpOk8 = { 0x00, 0x02, 0x00, 0x01, 0x00, 0x00, 0x00, 0x03 };

        private static readonly byte[] A_TransmitCommand8 = { 0x04, 0x01, 0x02, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] B_ReceiveCommand8 = { 0x04, 0x01, 0x02, 0x03, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] TestData4 = { 0x7F, 0x00, 0xAA, 0x55 };

        private readonly A_C_6_5_1_2Simulation _simulation = new A_C_6_5_1_2Simulation();
        private readonly SemaphoreSlim _arincOpLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _manualTestLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _autoTestLock = new SemaphoreSlim(1, 1);

        private CancellationTokenSource _autoTestCts;

        private string _aTxChannel;
        private string _aRxChannel;
        private string _bTxChannel;
        private string _bRxChannel;

        private string _enterAtpTxChannel;
        private string _enterAtpRxChannel;
        private string _exitAtpTxChannel;
        private string _exitAtpRxChannel;

        private string _enterAtpRxDataText;
        private string _aRxDataText;
        private string _bRxDataText;
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

        public A_C_6_5_1_2ViewModel()
        {
            _aTxChannel = FixedATxChannel;
            _aRxChannel = FixedARxChannel;
            _bTxChannel = FixedBTxChannel;
            _bRxChannel = FixedBRxChannel;

            _enterAtpTxChannel = ATxChannel;
            _enterAtpRxChannel = ARxChannel;
            _exitAtpTxChannel = ATxChannel;
            _exitAtpRxChannel = ARxChannel;

            EnterAtpRxDataText = "--";
            ARxDataText = "--";
            BRxDataText = "--";
            ExitAtpRxDataText = "--";

            LastTestTime = "--";
            LastTestResult = "--";
            PreviousTestTime = "--";
            PreviousTestResult = "--";
            CurrentStepImage = CreateImageSource("/Resources/Logo/begin.png");

            ManualTestCommand = new DelegateCommand(OnManualTest);
            AutoTestCommand = new DelegateCommand(OnAutoTest);

            SendEnterAtpCommand = new DelegateCommand(async () => await OnSendEnterAtpAsync());
            SendATransmitCommand = new DelegateCommand(async () => await OnSendATransmitAsync());
            SendBReceiveCommand = new DelegateCommand(async () => await OnSendBReceiveAsync());
            SendExitAtpCommand = new DelegateCommand(async () => await OnSendExitAtpAsync());

            ClearLogCommand = new DelegateCommand(() => Logs.Clear());
        }

        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();

        public DelegateCommand ManualTestCommand { get; }
        public DelegateCommand AutoTestCommand { get; }
        public DelegateCommand SendEnterAtpCommand { get; }
        public DelegateCommand SendATransmitCommand { get; }
        public DelegateCommand SendBReceiveCommand { get; }
        public DelegateCommand SendExitAtpCommand { get; }
        public DelegateCommand ClearLogCommand { get; }

        public string ATxChannel
        {
            get => _aTxChannel;
        }

        public string ARxChannel
        {
            get => _aRxChannel;
        }

        public string BTxChannel
        {
            get => _bTxChannel;
        }

        public string BRxChannel
        {
            get => _bRxChannel;
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
            private set => SetProperty(ref _enterAtpRxDataText, value);
        }

        public string ARxDataText
        {
            get => _aRxDataText;
            private set => SetProperty(ref _aRxDataText, value);
        }

        public string BRxDataText
        {
            get => _bRxDataText;
            private set => SetProperty(ref _bRxDataText, value);
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

        public double ArincRate
        {
            get => _arincRate;
            set => SetProperty(ref _arincRate, value);
        }

        private void AddLog(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;

            try
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (Logs.Count > 500)
                    {
                        Logs.RemoveAt(0);
                    }

                    Logs.Add(message);
                });
            }
            catch
            {
                try
                {
                    Logs.Add(message);
                }
                catch
                {
                }
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
                    ARxDataText = "--";
                    BRxDataText = "--";
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

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试启动({(_simulation.IsRealProduct ? "真实产品模式" : "仿真模式")})：打开通道 A(TX={ATxChannel},RX={ARxChannel}) B(TX={BTxChannel},RX={BRxChannel})");

                    try
                    {
                        var api = Prism.Ioc.ContainerLocator.Container.Resolve(typeof(MeasureControl.Services.HardwareApis.IComponentPowerStateApi)) as MeasureControl.Services.HardwareApis.IComponentPowerStateApi;
                        if (api != null)
                            await api.ApplyComponent28VStateAsync(CancellationToken.None);
                    }
                    catch { }

                    await _simulation.StartAsync(ATxChannel, ARxChannel, BTxChannel, BRxChannel, msg => AddLog(msg));

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试启动完成");
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
            await _manualTestLock.WaitAsync();
            try
            {
                if (IsBusy)
                    return;

                IsBusy = true;
                try
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试停止：开始关闭仿真");
                    await _simulation.StopAsync(msg => AddLog(msg));

                    IsManualTestRunning = false;
                    CurrentStepImage = CreateImageSource("/Resources/Logo/begin.png");

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试已停止");
                }
                catch (Exception ex)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试停止失败: {ex.Message}");
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

        private async Task OnSendEnterAtpAsync()
        {
            if (!IsManualTestRunning || IsBusy)
                return;

            if (!string.Equals(EnterAtpTxChannel, ATxChannel, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(EnterAtpRxChannel, ARxChannel, StringComparison.OrdinalIgnoreCase))
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 进入ATP使用A通道：TX={ATxChannel}, RX={ARxChannel}");
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

        private async Task OnSendATransmitAsync()
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
                    await _simulation.ClearRxFifoAsync(ARxChannel);
                    await Task.Delay(20);

                    var token = CancellationToken.None;

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 发送A通道发送指令：{FormatBytes(A_TransmitCommand8)}");
                    await _simulation.SendBenchCommandOnlyAsync(ATxChannel, A_TransmitCommand8, msg => AddLog(msg), token);

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 等待接收4包数据(LABEL=0x09/0x0A/0x0B/0x0C)...");
                    var resp8 = await _simulation.WaitBenchResponse8Async(ARxChannel, isExpected: null, timeoutMs: 1200, log: msg => AddLog(msg), token: token);
                    if (resp8 == null || resp8.Length != 8)
                    {
                        AddLog($"[{DateTime.Now:HH:mm:ss}] A通道测试数据超时");
                        CurrentStepImage = CreateImageSource("/Resources/Logo/warning.png");
                        return;
                    }

                    var data4 = resp8.Take(4).ToArray();

                    ARxDataText = $"0x{FormatBytesHex(data4)}";
                    AddLog($"[{DateTime.Now:HH:mm:ss}] A通道收到测试数据");
                }
                finally
                {
                    IsBusy = false;
                }
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] A通道发送异常: {ex.Message}");
                CurrentStepImage = CreateImageSource("/Resources/Logo/warning.png");
            }
            finally
            {
                _arincOpLock.Release();
            }
        }

        private async Task OnSendBReceiveAsync()
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
                    await _simulation.ClearRxFifoAsync(BRxChannel);
                    await Task.Delay(20);

                    var token = CancellationToken.None;

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 发送B通道接收回传指令：{FormatBytes(B_ReceiveCommand8)}");
                    await _simulation.SendBenchCommandOnlyAsync(BTxChannel, B_ReceiveCommand8, msg => AddLog(msg), token);

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 等待接收4包数据(LABEL=0x09/0x0A/0x0B/0x0C)...");
                    var resp8 = await _simulation.WaitBenchResponse8Async(BRxChannel, isExpected: null, timeoutMs: 1200, log: msg => AddLog(msg), token: token);
                    if (resp8 == null || resp8.Length != 8)
                    {
                        AddLog($"[{DateTime.Now:HH:mm:ss}] B通道回传数据超时");
                        CurrentStepImage = CreateImageSource("/Resources/Logo/warning.png");
                        return;
                    }

                    var data4 = resp8.Take(4).ToArray();

                    BRxDataText = $"0x{FormatBytesHex(data4)}";
                    AddLog($"[{DateTime.Now:HH:mm:ss}] B通道收到回传数据");
                }
                finally
                {
                    IsBusy = false;
                }
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] B通道发送异常: {ex.Message}");
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

            if (!string.Equals(ExitAtpTxChannel, ATxChannel, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(ExitAtpRxChannel, ARxChannel, StringComparison.OrdinalIgnoreCase))
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 退出ATP使用A通道：TX={ATxChannel}, RX={ARxChannel}");
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

        private void OnAutoTest()
        {
            if (IsAutoTestRunning)
            {
                _ = StopAutoTestAsync();
                return;
            }

            _ = RunAutoTestAsync();
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
                    ARxDataText = "--";
                    BRxDataText = "--";
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

                    await _simulation.StartAsync(ATxChannel, ARxChannel, BTxChannel, BRxChannel, msg => AddLog(msg));

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 步骤1：进入ATP");
                    CurrentStepImage = CreateImageSource("/Resources/Logo/communicate.png");
                    await _simulation.ClearRxFifoAsync(ARxChannel);
                    await Task.Delay(20, token);
                    await _simulation.SendBenchCommandOnlyAsync(ATxChannel, EnterAtpCommand8, msg => AddLog(msg), token);

                    var enterOk = await _simulation.WaitBenchResponse8Async(
                        ARxChannel,
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

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 步骤2：A通道发送测试数据");
                    CurrentStepImage = CreateImageSource("/Resources/Logo/communicate.png");
                    await _simulation.ClearRxFifoAsync(ARxChannel);
                    await Task.Delay(20, token);
                    await _simulation.SendBenchCommandOnlyAsync(ATxChannel, A_TransmitCommand8, msg => AddLog(msg), token);

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 等待接收4包数据(LABEL=0x09/0x0A/0x0B/0x0C)...");
                    var aResp8 = await _simulation.WaitBenchResponse8Async(ARxChannel, isExpected: null, timeoutMs: 1200, log: msg => AddLog(msg), token: token);
                    if (aResp8 == null || aResp8.Length != 8)
                    {
                        SetLastTestResult("FAIL");
                        CurrentStepImage = CreateImageSource("/Resources/Logo/warning.png");
                        AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试失败：A通道数据超时");
                        return;
                    }

                    var aData4 = aResp8.Take(4).ToArray();
                    ARxDataText = $"0x{FormatBytesHex(aData4)}";

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 步骤3：B通道回传接收数据");
                    CurrentStepImage = CreateImageSource("/Resources/Logo/communicate.png");
                    await _simulation.ClearRxFifoAsync(BRxChannel);
                    await Task.Delay(20, token);
                    await _simulation.SendBenchCommandOnlyAsync(BTxChannel, B_ReceiveCommand8, msg => AddLog(msg), token);

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 等待接收4包数据(LABEL=0x09/0x0A/0x0B/0x0C)...");
                    var bResp8 = await _simulation.WaitBenchResponse8Async(BRxChannel, isExpected: null, timeoutMs: 1200, log: msg => AddLog(msg), token: token);
                    if (bResp8 == null || bResp8.Length != 8)
                    {
                        SetLastTestResult("FAIL");
                        CurrentStepImage = CreateImageSource("/Resources/Logo/warning.png");
                        AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试失败：B通道数据超时");
                        return;
                    }

                    var bData4 = bResp8.Take(4).ToArray();
                    BRxDataText = $"0x{FormatBytesHex(bData4)}";

                    bool pass = aData4.SequenceEqual(TestData4) && bData4.SequenceEqual(TestData4);
                    SetLastTestResult(pass ? "PASS" : "FAIL");
                    CurrentStepImage = CreateImageSource(pass ? "/Resources/Logo/over.png" : "/Resources/Logo/warning.png");

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 步骤4：退出ATP");
                    await _simulation.ClearRxFifoAsync(ARxChannel);
                    await Task.Delay(20, token);
                    await _simulation.SendBenchCommandOnlyAsync(ATxChannel, ExitAtpCommand8, msg => AddLog(msg), token);

                    var exitOk = await _simulation.WaitBenchResponse8Async(
                        ARxChannel,
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

        private async Task StopAutoTestAsync()
        {
            await _autoTestLock.WaitAsync();
            try
            {
                try
                {
                    _autoTestCts?.Cancel();
                }
                catch
                {
                }
            }
            finally
            {
                _autoTestLock.Release();
            }
        }

        private void SetLastTestResult(string result)
        {
            var now = DateTime.Now;

            PreviousTestTime = LastTestTime;
            PreviousTestResult = LastTestResult;

            LastTestTime = now.ToString("yyyy-MM-dd HH:mm:ss");
            LastTestResult = result;
        }

        private bool TrySetupSimChannelMapping(out string error)
        {
            error = null;

            int aTx = ARINC429SimulationBase.ParseChannelIndex(ATxChannel);
            int aRx = ARINC429SimulationBase.ParseChannelIndex(ARxChannel);
            int bTx = ARINC429SimulationBase.ParseChannelIndex(BTxChannel);
            int bRx = ARINC429SimulationBase.ParseChannelIndex(BRxChannel);

            if (aTx < 0 || aRx < 0 || bTx < 0 || bRx < 0)
            {
                error = "通道索引无效";
                return false;
            }

            // 约定：产品侧通道 = bench通道 + 4（与6.5.1.1一致：benchTX0->simRX4, benchRX1<-simTX5）
            // 为避免越界，bench通道最大只能到 11。
            int maxBench = Math.Max(Math.Max(aTx, aRx), Math.Max(bTx, bRx));
            if (maxBench > 11)
            {
                error = "当前仿真映射规则要求 bench 通道索引 <= 11（因为产品侧使用 bench+4）";
                return false;
            }

            _simulation.SimProductRxChannelIndex = aTx + 4;
            _simulation.SimProductTxChannelIndex = aRx + 4;
            _simulation.SimProduct2RxChannelIndex = bTx + 4;
            _simulation.SimProduct2TxChannelIndex = bRx + 4;
            return true;
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
            return string.Concat(bytes.Select(b => b.ToString("X2")));
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
