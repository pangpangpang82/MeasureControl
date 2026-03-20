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
using MeasureControl.Simulations.S_C_8_5_1;

namespace MeasureControl.ViewModels.SingleBoardTest.AirController
{
    public sealed class S_C_8_5_1ViewModel : BindableBase, IDisposable
    {
        private const string FixedTxChannel = "429_CH1";
        private const string FixedRxChannel = "429_CH0";

        private static readonly byte[] EnterAtpCommand8 = { 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] EnterAtpOk8 = { 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x02 };
        private static readonly byte[] ExitAtpCommand8 = { 0x00, 0x02, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01 };
        private static readonly byte[] ExitAtpOk8 = { 0x00, 0x02, 0x00, 0x01, 0x00, 0x00, 0x00, 0x03 };

        private static readonly byte[] SArinc825OutCommand8 = { 0x14, 0x01, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00 };

        private static readonly byte[] ExpectedCanTail4 = { 0x01, 0x01, 0x01, 0x01 };

        private readonly S_C_8_5_1Simulation _simulation = new S_C_8_5_1Simulation();
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

        private string _canRxChannel;

        private string _enterAtpRxDataText;
        private string _canRxDataText;
        private string _exitAtpRxDataText;

        private string _lastTestTime;
        private string _lastTestResult;

        private bool _isManualTestRunning;
        private bool _isAutoTestRunning;
        private bool _isBusy;

        private double _arincRate = 100000.0;

        public S_C_8_5_1ViewModel()
        {
            _testTxChannel = FixedTxChannel;
            _testRxChannel = FixedRxChannel;

            _enterAtpTxChannel = _testTxChannel;
            _enterAtpRxChannel = _testRxChannel;
            _exitAtpTxChannel = _testTxChannel;
            _exitAtpRxChannel = _testRxChannel;

            _canRxChannel = "CH2";

            EnterAtpRxDataText = "--";
            CanRxDataText = "--";
            ExitAtpRxDataText = "--";

            LastTestTime = "--";
            LastTestResult = "--";

            ManualTestCommand = new DelegateCommand(OnManualTest);
            AutoTestCommand = new DelegateCommand(OnAutoTest);

            SendEnterAtpCommand = new DelegateCommand(async () => await OnSendEnterAtpAsync());
            SendTestCommand = new DelegateCommand(async () => await OnSendSArinc825OutAsync());
            ListenCanCommand = new DelegateCommand(async () => await OnListenCanAsync());
            SendExitAtpCommand = new DelegateCommand(async () => await OnSendExitAtpAsync());
            ClearLogCommand = new DelegateCommand(() => Logs.Clear());
        }

        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();

        public DelegateCommand ManualTestCommand { get; }
        public DelegateCommand AutoTestCommand { get; }

        public DelegateCommand SendEnterAtpCommand { get; }
        public DelegateCommand SendTestCommand { get; }
        public DelegateCommand ListenCanCommand { get; }
        public DelegateCommand SendExitAtpCommand { get; }
        public DelegateCommand ClearLogCommand { get; }

        public string TestTxChannel
        {
            get => FixedTxChannel;
        }

        public string TestRxChannel
        {
            get => FixedRxChannel;
        }

        public string EnterAtpTxChannel
        {
            get => FixedTxChannel;
        }

        public string EnterAtpRxChannel
        {
            get => FixedRxChannel;
        }

        public string ExitAtpTxChannel
        {
            get => FixedTxChannel;
        }

        public string ExitAtpRxChannel
        {
            get => FixedRxChannel;
        }

        public string CanRxChannel
        {
            get => _canRxChannel;
            set => SetProperty(ref _canRxChannel, value);
        }

        public string EnterAtpRxDataText
        {
            get => _enterAtpRxDataText;
            set => SetProperty(ref _enterAtpRxDataText, value);
        }

        public string CanRxDataText
        {
            get => _canRxDataText;
            set => SetProperty(ref _canRxDataText, value);
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
                    CanRxDataText = "--";
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
                    try { await TryApplyComponentDownStateAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
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
                    await OnSendSArinc825OutAsync();

                    token.ThrowIfCancellationRequested();
                    await ListenCanAndJudgeAsync(token);

                    token.ThrowIfCancellationRequested();
                    await OnSendExitAtpAsync();
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
                    try { await TryApplyComponentDownStateAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
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

        private async Task OnSendSArinc825OutAsync()
        {
            if (!IsManualTestRunning || IsBusy)
                return;

            await _arincOpLock.WaitAsync();
            try
            {
                IsBusy = true;
                try
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 发送S_ARINC825_OUT：{FormatBytes(SArinc825OutCommand8)}");
                    await _simulation.SendBenchCommandOnlyAsync(TestTxChannel, SArinc825OutCommand8, msg => AddLog(msg), CancellationToken.None);
                }
                finally
                {
                    IsBusy = false;
                }
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 发送S_ARINC825_OUT异常: {ex.Message}");
            }
            finally
            {
                _arincOpLock.Release();
            }
        }

        private async Task OnListenCanAsync()
        {
            if (IsBusy)
                return;

            await _canOpLock.WaitAsync();
            try
            {
                IsBusy = true;
                try
                {
                    using (var cts = new CancellationTokenSource(2000))
                    {
                        await ListenCanAndJudgeAsync(cts.Token);
                    }
                }
                finally
                {
                    IsBusy = false;
                }
            }
            finally
            {
                _canOpLock.Release();
            }
        }

        private async Task ListenCanAndJudgeAsync(CancellationToken token)
        {
            CanRxDataText = "--";

            var rxIndex = ParseCanChannelIndex(CanRxChannel);
            if (rxIndex < 0)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] CAN监听失败：通道选择无效");
                return;
            }

            var ok = await EnsureCanDriverReadyAsync();
            if (!ok)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] CAN监听失败：CAN驱动未就绪");
                return;
            }

            ok = await OpenCanChannel500kAsync(rxIndex);
            if (!ok)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] CAN监听失败：打开通道失败 {CanRxChannel}");
                return;
            }

            await FlushRxChannelAsync(rxIndex, TimeSpan.FromMilliseconds(120), token);

            AddLog($"[{DateTime.Now:HH:mm:ss}] 开始监听CAN：RX={CanRxChannel}，等待后4字节=01 01 01 01");

            var deadline = DateTime.UtcNow.AddMilliseconds(2000);
            while (!token.IsCancellationRequested && DateTime.UtcNow <= deadline)
            {
                var frames = await _canDriver.ReceiveFramesBatchAsync(rxIndex, 20, 0.02);
                if (frames != null && frames.Count > 0)
                {
                    foreach (var f in frames)
                    {
                        var buf = f.DataBuf;
                        var len = f.nDataLength;
                        if (len <= 0 || f.nFrameType != (byte)PXI4004.ARTCANX1_CAN_FRAME_TYPE_DATA_FRM)
                            continue;

                        var hex = FormatData(buf, len);
                        AddLog($"[{DateTime.Now:HH:mm:ss}] CAN RX：{CanRxChannel}, ID=0x{f.nFrameID:X}, Len={len}, Data={hex}");

                        if (len >= 4)
                        {
                            var tail = buf.Skip(len - 4).Take(4).ToArray();
                            if (tail.SequenceEqual(ExpectedCanTail4))
                            {
                                CanRxDataText = hex;
                                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                                LastTestResult = "PASS";
                                AddLog($"[{DateTime.Now:HH:mm:ss}] 判据命中：后4字节=01 01 01 01 -> PASS");
                                return;
                            }
                        }
                    }
                }

                await Task.Delay(10, token);
            }

            LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            LastTestResult = "FAIL";
            AddLog($"[{DateTime.Now:HH:mm:ss}] CAN监听超时：未收到后4字节=01 01 01 01");
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

        private async Task FlushRxChannelAsync(int rxChannelIndex, TimeSpan duration, CancellationToken token)
        {
            var start = DateTime.UtcNow;
            while ((DateTime.UtcNow - start) < duration && !token.IsCancellationRequested)
            {
                var frames = await _canDriver.ReceiveFramesBatchAsync(rxChannelIndex, 50, 0.001);
                if (frames == null || frames.Count == 0)
                    break;
                await Task.Delay(1, token);
            }
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
