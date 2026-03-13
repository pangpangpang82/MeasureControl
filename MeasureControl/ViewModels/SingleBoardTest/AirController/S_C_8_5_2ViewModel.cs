using Prism.Commands;
using Prism.Mvvm;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using MeasureControl.Drivers;
using MeasureControl.Drivers.PXI4004CAN;
using MeasureControl.Helpers;
using MeasureControl.Models.Devices;
using MeasureControl.Simulations.S_C_8_5_2;

namespace MeasureControl.ViewModels.SingleBoardTest.AirController
{
    public sealed class S_C_8_5_2ViewModel : BindableBase, IDisposable
    {
        private static readonly byte[] EnterAtpCommand8 = { 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] EnterAtpOk8 = { 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x02 };
        private static readonly byte[] ExitAtpCommand8 = { 0x00, 0x02, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01 };
        private static readonly byte[] ExitAtpOk8 = { 0x00, 0x02, 0x00, 0x01, 0x00, 0x00, 0x00, 0x03 };

        private static readonly byte[] SArinc825InCommand8 = { 0x14, 0x02, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] ExpectedSArinc825InOk8 = { 0x14, 0x02, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01 };

        private const uint CanInjectFrameId = 0;
        private static readonly byte[] CanInjectData = { 0x01, 0x01, 0x01, 0x01 };

        private const int ArincWaitTimeoutMs = 2500;

        private readonly S_C_8_5_2Simulation _simulation = new S_C_8_5_2Simulation();
        private PXI4004Driver _canDriver;

        private readonly SemaphoreSlim _arincOpLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _canOpLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _manualTestLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _autoTestLock = new SemaphoreSlim(1, 1);

        private CancellationTokenSource _autoTestCts;

        private string _testTxChannel;
        private string _testRxChannel;

        private string _enterAtpTxChannel;
        private string _enterAtpRxChannel;
        private string _exitAtpTxChannel;
        private string _exitAtpRxChannel;

        private string _canTxChannel;

        private string _enterAtpRxDataText;
        private string _canInjectStatusText;
        private string _sArinc825InRxDataText;
        private string _exitAtpRxDataText;

        private string _lastTestTime;
        private string _lastTestResult;

        private bool _isManualTestRunning;
        private bool _isAutoTestRunning;
        private bool _isBusy;

        private double _arincRate = 100000.0;

        public S_C_8_5_2ViewModel()
        {
            _testTxChannel = "429_CH1";
            _testRxChannel = "429_CH0";

            _enterAtpTxChannel = _testTxChannel;
            _enterAtpRxChannel = _testRxChannel;
            _exitAtpTxChannel = _testTxChannel;
            _exitAtpRxChannel = _testRxChannel;

            _canTxChannel = "CH0";

            EnterAtpRxDataText = "--";
            CanInjectStatusText = "--";
            SArinc825InRxDataText = "--";
            ExitAtpRxDataText = "--";

            LastTestTime = "--";
            LastTestResult = "--";

            ManualTestCommand = new DelegateCommand(OnManualTest);
            AutoTestCommand = new DelegateCommand(OnAutoTest);

            SendEnterAtpCommand = new DelegateCommand(async () => await OnSendEnterAtpAsync());
            InjectCanCommand = new DelegateCommand(async () => await OnInjectCanAsync());
            SendTestCommand = new DelegateCommand(async () => await OnSendSArinc825InAndWaitAsync());
            SendExitAtpCommand = new DelegateCommand(async () => await OnSendExitAtpAsync());
            ClearLogCommand = new DelegateCommand(() => Logs.Clear());
        }

        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();

        public DelegateCommand ManualTestCommand { get; }
        public DelegateCommand AutoTestCommand { get; }

        public DelegateCommand SendEnterAtpCommand { get; }
        public DelegateCommand InjectCanCommand { get; }
        public DelegateCommand SendTestCommand { get; }
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

        public string CanTxChannel
        {
            get => _canTxChannel;
            set => SetProperty(ref _canTxChannel, value);
        }

        public string EnterAtpRxDataText
        {
            get => _enterAtpRxDataText;
            set => SetProperty(ref _enterAtpRxDataText, value);
        }

        public string CanInjectStatusText
        {
            get => _canInjectStatusText;
            set => SetProperty(ref _canInjectStatusText, value);
        }

        public string SArinc825InRxDataText
        {
            get => _sArinc825InRxDataText;
            set => SetProperty(ref _sArinc825InRxDataText, value);
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
                try { Logs.Add(msg); } catch { }
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
                    CanInjectStatusText = "--";
                    SArinc825InRxDataText = "--";
                    ExitAtpRxDataText = "--";

                    LastTestTime = "--";
                    LastTestResult = "--";

                    _simulation.SimProductRxChannelIndex = 4;
                    _simulation.SimProductTxChannelIndex = 5;
                    _simulation.ArincRate = ArincRate;

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试启动：打开ARINC429");

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
                    try { await DisconnectCanAsync(); } catch { }
                    IsBusy = false;
                    LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
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

        private async Task RunAutoTestAsync()
        {
            await _autoTestLock.WaitAsync();
            try
            {
                if (IsAutoTestRunning)
                    return;

                IsAutoTestRunning = true;
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
                    if (!IsManualTestRunning)
                    {
                        await StartManualTestAsync();
                        if (!IsManualTestRunning)
                        {
                            LastTestResult = "启动失败";
                            LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                            return;
                        }
                    }

                    token.ThrowIfCancellationRequested();
                    await OnSendEnterAtpAsync();

                    token.ThrowIfCancellationRequested();
                    await OnInjectCanAsync();

                    token.ThrowIfCancellationRequested();
                    var ok = await OnSendSArinc825InAndWaitAsync();

                    token.ThrowIfCancellationRequested();
                    await OnSendExitAtpAsync();

                    LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    LastTestResult = ok ? "PASS" : "FAIL";
                }
                catch (OperationCanceledException)
                {
                    LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    LastTestResult = "已停止";
                }
                catch (Exception ex)
                {
                    LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    LastTestResult = "异常";
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试异常：{ex.Message}");
                }
                finally
                {
                    IsAutoTestRunning = false;
                }
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
                    await _simulation.ClearRxFifoAsync(EnterAtpRxChannel);
                    await Task.Delay(20);

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 发送进入ATP：{FormatBytes(EnterAtpCommand8)}");
                    await _simulation.SendBenchCommandOnlyAsync(EnterAtpTxChannel, EnterAtpCommand8, msg => AddLog(msg), CancellationToken.None);

                    var resp = await _simulation.WaitBenchResponse8Async(
                        EnterAtpRxChannel,
                        b => b != null && b.SequenceEqual(EnterAtpOk8),
                        timeoutMs: 1500,
                        log: msg => AddLog(msg),
                        token: CancellationToken.None);

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
                AddLog($"[{DateTime.Now:HH:mm:ss}] 退出ATP使用测试通道：TX={TestTxChannel}, RX={TestRxChannel}");
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

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 发送退出ATP：{FormatBytes(ExitAtpCommand8)}");
                    await _simulation.SendBenchCommandOnlyAsync(ExitAtpTxChannel, ExitAtpCommand8, msg => AddLog(msg), CancellationToken.None);

                    var resp = await _simulation.WaitBenchResponse8Async(
                        ExitAtpRxChannel,
                        b => b != null && b.SequenceEqual(ExitAtpOk8),
                        timeoutMs: 1500,
                        log: msg => AddLog(msg),
                        token: CancellationToken.None);

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

        private async Task OnInjectCanAsync()
        {
            if (IsBusy)
                return;

            await _canOpLock.WaitAsync();
            try
            {
                IsBusy = true;
                try
                {
                    CanInjectStatusText = "--";

                    var txIndex = ParseCanChannelIndex(CanTxChannel);
                    if (txIndex < 0)
                    {
                        AddLog($"[{DateTime.Now:HH:mm:ss}] CAN注入失败：通道选择无效");
                        return;
                    }

                    var ok = await EnsureCanDriverReadyAsync();
                    if (!ok)
                    {
                        AddLog($"[{DateTime.Now:HH:mm:ss}] CAN注入失败：CAN驱动未就绪");
                        return;
                    }

                    ok = await OpenCanChannel500kAsync(txIndex);
                    if (!ok)
                    {
                        AddLog($"[{DateTime.Now:HH:mm:ss}] CAN注入失败：打开通道失败 {CanTxChannel}");
                        return;
                    }

                    var frame = PXI4004.CreateDataFrame(CanInjectFrameId, CanInjectData);
                    AddLog($"[{DateTime.Now:HH:mm:ss}] CAN注入：TX={CanTxChannel}, ID=0x{CanInjectFrameId:X}, Len={frame.nDataLength}, Data={FormatData(frame.DataBuf, frame.nDataLength)}");

                    bool sent = false;
                    for (int i = 1; i <= 3; i++)
                    {
                        sent = await _canDriver.SendFrameAsync(txIndex, frame, 0.2);
                        if (sent)
                            break;
                        await Task.Delay(50);
                    }

                    CanInjectStatusText = sent ? "已注入" : "注入失败";
                    AddLog($"[{DateTime.Now:HH:mm:ss}] CAN注入{(sent ? "成功" : "失败")}");
                }
                finally
                {
                    IsBusy = false;
                }
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] CAN注入异常：{ex.Message}");
            }
            finally
            {
                _canOpLock.Release();
            }
        }

        private async Task<bool> OnSendSArinc825InAndWaitAsync()
        {
            if (!IsManualTestRunning || IsBusy)
                return false;

            await _arincOpLock.WaitAsync();
            try
            {
                IsBusy = true;
                try
                {
                    SArinc825InRxDataText = "--";

                    await _simulation.ClearRxFifoAsync(TestRxChannel);
                    await Task.Delay(20);

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 发送S_ARINC825_IN：{FormatBytes(SArinc825InCommand8)}");
                    await _simulation.SendBenchCommandOnlyAsync(TestTxChannel, SArinc825InCommand8, msg => AddLog(msg), CancellationToken.None);

                    var resp = await _simulation.WaitBenchResponse8Async(
                        TestRxChannel,
                        b => b != null && b.SequenceEqual(ExpectedSArinc825InOk8),
                        timeoutMs: ArincWaitTimeoutMs,
                        log: msg => AddLog(msg),
                        token: CancellationToken.None);

                    if (resp == null)
                    {
                        AddLog($"[{DateTime.Now:HH:mm:ss}] S_ARINC825_IN回包超时");
                        LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                        LastTestResult = "FAIL";
                        return false;
                    }

                    SArinc825InRxDataText = $"01010101 | 0x{FormatBytesHex(resp)}";
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 回包：{FormatBytes(resp)} -> PASS");

                    LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    LastTestResult = "PASS";
                    return true;
                }
                finally
                {
                    IsBusy = false;
                }
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] S_ARINC825_IN异常: {ex.Message}");
                return false;
            }
            finally
            {
                _arincOpLock.Release();
            }
        }

        private async Task<bool> EnsureCanDriverReadyAsync()
        {
            if (_canDriver != null && _canDriver.IsConnected)
                return true;

            try
            {
                for (var logicalIndex = 0; logicalIndex <= 7; logicalIndex++)
                {
                    var dummy = new CanBusDevice
                    {
                        Name = "PXI4004",
                        Model = "PXI-4004",
                        CardName = $"PXI4004(直连-{logicalIndex})",
                        SlotIndex = logicalIndex
                    };

                    var direct = new PXI4004Driver(dummy, logicalIndex);
                    var ok = await direct.ConnectAsync();
                    if (ok)
                    {
                        _canDriver = direct;
                        AddLog($"[{DateTime.Now:HH:mm:ss}] CAN已连接(直连)：逻辑设备{logicalIndex}");
                        return true;
                    }
                }

                AddLog($"[{DateTime.Now:HH:mm:ss}] CAN连接失败：未探测到可用PXI4004逻辑设备(0-7)");
                return false;
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] CAN驱动未准备：{ex.Message}");
                return false;
            }
        }

        private async Task DisconnectCanAsync()
        {
            if (_canDriver == null)
                return;

            try
            {
                await _canDriver.DisconnectAsync();
            }
            catch
            {
            }
            finally
            {
                _canDriver = null;
            }
        }

        private async Task<bool> OpenCanChannel500kAsync(int channelIndex)
        {
            if (_canDriver == null || !_canDriver.IsConnected)
                return false;

            try
            {
                PXI4004.ARTCANX1_CAN_PARAM param;
                try
                {
                    var handle = _canDriver.DeviceHandle;
                    param = handle != IntPtr.Zero
                        ? PXI4004.GetDefaultCANParam(handle, (uint)channelIndex)
                        : new PXI4004.ARTCANX1_CAN_PARAM();
                }
                catch
                {
                    param = new PXI4004.ARTCANX1_CAN_PARAM();
                }

                if (param.nReserved1 == null || param.nReserved1.Length != 7)
                    param.nReserved1 = new uint[7];
                if (param.nReserved2 == null || param.nReserved2.Length != 32)
                    param.nReserved2 = new uint[32];
                if (param.SendTrig.nReserved == null || param.SendTrig.nReserved.Length != 20)
                    param.SendTrig.nReserved = new uint[20];

                param.nBaudRate = PXI4004.CAN_BAUD_500K;
                param.nWorkMode = (byte)PXI4004.ARTCANX1_CAN_WORKMODE_NORMAL;
                param.bRecvTimestampEn = 1;
                param.bAccExtID = 0;
                param.nAccFilterCnt = (byte)PXI4004.ARTCANX1_CAN_ACC_NUM_NONE;
                param.nAccCodeA = 0x00000000;
                param.nAccCodeB = 0x00000000;
                param.nAccMaskA = 0xFFFFFFFF;
                param.nAccMaskB = 0xFFFFFFFF;
                param.nFrameInterval = 0;
                param.SendTrig.nTriggerType = PXI4004.ARTCANX1_TRIGTYPE_NONE;

                return await _canDriver.OpenChannelAsync(channelIndex, param);
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 打开通道CH{channelIndex}失败：{ex.Message}");
                return false;
            }
        }

        private static int ParseCanChannelIndex(string channel)
        {
            if (string.IsNullOrWhiteSpace(channel))
                return -1;

            var s = channel.Trim();
            var idx = s.LastIndexOf("CH", StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
                return -1;

            var numberPart = s.Substring(idx + 2).Trim();
            if (!int.TryParse(numberPart, out var n))
                return -1;

            if (n < 0)
                return -1;

            return n;
        }

        private static string FormatBytes(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                return "--";
            return string.Join(" ", bytes.Select(b => b.ToString("X2")));
        }

        private static string FormatBytesHex(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                return string.Empty;
            return string.Join(" ", bytes.Select(b => b.ToString("X2")));
        }

        private static string FormatData(byte[] data, int length)
        {
            if (data == null)
                return string.Empty;
            var len = Math.Min(length, data.Length);
            if (len <= 0)
                return string.Empty;
            return string.Join(" ", data.Take(len).Select(b => b.ToString("X2")));
        }

        public void Dispose()
        {
            try { _autoTestCts?.Cancel(); } catch { }
            try { _autoTestCts?.Dispose(); } catch { }
            try { _simulation?.Dispose(); } catch { }
            try { _canDriver?.DisconnectAsync().GetAwaiter().GetResult(); } catch { }
            try { _arincOpLock?.Dispose(); } catch { }
            try { _canOpLock?.Dispose(); } catch { }
            try { _manualTestLock?.Dispose(); } catch { }
            try { _autoTestLock?.Dispose(); } catch { }
        }
    }
}
