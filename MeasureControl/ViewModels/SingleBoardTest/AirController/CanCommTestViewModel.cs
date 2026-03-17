using Prism.Commands;
using Prism.Mvvm;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MeasureControl.Drivers;
using MeasureControl.Drivers.PXI4004CAN;
using MeasureControl.Models.Devices;
using MeasureControl.Simulations.AC_6_4;

namespace MeasureControl.ViewModels.SingleBoardTest.AirController
{
    public class CanCommTestViewModel : BindableBase
    {
        private const string FixedTxChannel = "429_CH0";
        private const string FixedRxChannel = "429_CH2";

        private static readonly byte[] EnterAtpCommand = { 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] EnterAtpOk = { 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x02 };
        private static readonly byte[] ExitAtpCommand = { 0x00, 0x02, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01 };
        private static readonly byte[] ExitAtpOk = { 0x00, 0x02, 0x00, 0x01, 0x00, 0x00, 0x00, 0x03 };
        private static readonly byte[] AbA825TransmitCommand = { 0x05, 0x01, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00 };

        private const int EnterAtpMaxRetries = 3;
        private const int EnterAtpTimeoutMs = 3000;
        private const int TestTimeoutMs = 3000;
        private const int ExpectedCollectiveValue = 12700;
        private const int CanListenTimeoutMs = 2500;

        private readonly SemaphoreSlim _manualTestLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _canOpLock = new SemaphoreSlim(1, 1);
        private CancellationTokenSource _cts;
        private readonly AC_6_4Simulation _simulation = new AC_6_4Simulation();
        private PXI4004Driver _canDriver;

        private string _title = "6.6.1CAN发送测试";
        private bool _isManualTestRunning;
        private string _lastTestTime = "--";
        private string _lastTestResult = "--";

        private string _enterAtpTxChannel = FixedTxChannel;
        private string _enterAtpRxChannel = FixedRxChannel;
        private string _exitAtpTxChannel = FixedTxChannel;
        private string _exitAtpRxChannel = FixedRxChannel;
        private string _testControllerRxChannel = FixedRxChannel;
        private string _testBenchRxChannel = FixedRxChannel;
        private string _testCommandTxChannel = FixedTxChannel;

        private string _enterAtpRxDataText = "--";
        private string _testRxDataText = "--";
        private string _testCollectiveValueText = "--";
        private string _exitAtpRxDataText = "--";
        private string _canRxChannel = "CH2";
        private string _canRxDataText = "--";

        public CanCommTestViewModel()
        {
            ManualTestCommand = new DelegateCommand(OnManualTest);
            ClearLogCommand = new DelegateCommand(() => Logs.Clear());
            SendEnterAtpCommand = new DelegateCommand(async () => await SendEnterAtpAsync());
            SendTestCommand = new DelegateCommand(async () => await SendTestCommandAsync());
            ListenCanCommand = new DelegateCommand(async () => await OnListenCanAsync());
            SendExitAtpCommand = new DelegateCommand(async () => await SendExitAtpAsync());
        }

        public string Title
        {
            get => _title;
            set => SetProperty(ref _title, value);
        }

        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();

        public DelegateCommand ManualTestCommand { get; }
        public DelegateCommand ClearLogCommand { get; }
        public DelegateCommand SendEnterAtpCommand { get; }
        public DelegateCommand SendTestCommand { get; }
        public DelegateCommand ListenCanCommand { get; }
        public DelegateCommand SendExitAtpCommand { get; }

        public bool IsManualTestRunning
        {
            get => _isManualTestRunning;
            set => SetProperty(ref _isManualTestRunning, value);
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

        public string EnterAtpTxChannel
        {
            get => _enterAtpTxChannel;
            set => SetProperty(ref _enterAtpTxChannel, FixedTxChannel);
        }

        public string EnterAtpRxChannel
        {
            get => _enterAtpRxChannel;
            set => SetProperty(ref _enterAtpRxChannel, FixedRxChannel);
        }

        public string ExitAtpTxChannel
        {
            get => _exitAtpTxChannel;
            set => SetProperty(ref _exitAtpTxChannel, FixedTxChannel);
        }

        public string ExitAtpRxChannel
        {
            get => _exitAtpRxChannel;
            set => SetProperty(ref _exitAtpRxChannel, FixedRxChannel);
        }

        public string TestControllerRxChannel
        {
            get => _testControllerRxChannel;
            set => SetProperty(ref _testControllerRxChannel, FixedRxChannel);
        }

        public string TestBenchRxChannel
        {
            get => _testBenchRxChannel;
            set => SetProperty(ref _testBenchRxChannel, FixedRxChannel);
        }

        public string TestCommandTxChannel
        {
            get => _testCommandTxChannel;
            set => SetProperty(ref _testCommandTxChannel, FixedTxChannel);
        }

        public string EnterAtpRxDataText
        {
            get => _enterAtpRxDataText;
            set => SetProperty(ref _enterAtpRxDataText, value);
        }

        public string TestRxDataText
        {
            get => _testRxDataText;
            set => SetProperty(ref _testRxDataText, value);
        }

        public string TestCollectiveValueText
        {
            get => _testCollectiveValueText;
            set => SetProperty(ref _testCollectiveValueText, value);
        }

        public string ExitAtpRxDataText
        {
            get => _exitAtpRxDataText;
            set => SetProperty(ref _exitAtpRxDataText, value);
        }

        public string CanRxChannel
        {
            get => _canRxChannel;
            set => SetProperty(ref _canRxChannel, value);
        }

        public string CanRxDataText
        {
            get => _canRxDataText;
            set => SetProperty(ref _canRxDataText, value);
        }

        private void OnManualTest()
        {
            if (IsManualTestRunning)
            {
                _ = StopAsync();
                return;
            }

            _ = StartAsync();
        }

        private async Task StartAsync()
        {
            await _manualTestLock.WaitAsync();
            try
            {
                if (IsManualTestRunning)
                    return;

                IsManualTestRunning = true;
                LastTestTime = "--";
                LastTestResult = "--";
                EnterAtpRxDataText = "--";
                TestRxDataText = "--";
                TestCollectiveValueText = "--";
                ExitAtpRxDataText = "--";
                CanRxDataText = "--";

                _cts?.Cancel();
                _cts?.Dispose();
                _cts = new CancellationTokenSource();

                AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试启动(仿真模式)：开始打开设备");

                try
                {
                    var api = Prism.Ioc.ContainerLocator.Container.Resolve(typeof(MeasureControl.Services.HardwareApis.IComponentPowerStateApi)) as MeasureControl.Services.HardwareApis.IComponentPowerStateApi;
                    if (api != null)
                        await api.ApplyComponent28VStateAsync(CancellationToken.None);
                }
                catch { }

                _simulation.SimProductRxChannelIndex = 4;
                _simulation.SimProductTxChannelIndex = 5;
                _simulation.ArincRate = 100000.0;
                await _simulation.StartAsync(EnterAtpTxChannel, EnterAtpRxChannel, msg => AddLog(msg));

                var entered = await SendEnterAtpAsync();
                if (!entered)
                {
                    LastTestResult = "进入ATP失败";
                    return;
                }

                var passed = await SendTestCommandAsync();

                var canAvailable = await EnsureCanDriverReadyAsync();
                if (canAvailable)
                {
                    var canPassed = await ListenCanFor12700Async();
                    LastTestResult = canPassed ? "检查通过" : "检查不通过";
                }
                else
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] CAN驱动不可用：回退为ARINC回读判据");
                    LastTestResult = passed ? "检查通过" : "检查不通过";
                }

                try
                {
                    await SendExitAtpAsync();
                }
                catch
                {
                }
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试启动异常：{ex.Message}");
                IsManualTestRunning = false;
                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            }
            finally
            {
                _manualTestLock.Release();
            }
        }

        private async Task StopAsync()
        {
            await _manualTestLock.WaitAsync();
            try
            {
                if (!IsManualTestRunning)
                    return;

                AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试停止：释放ARINC429");

                try { _cts?.Cancel(); } catch { }

                await _simulation.StopAsync(msg => AddLog(msg));

                try { await DisconnectCanAsync(); } catch { }

                IsManualTestRunning = false;
                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            }
            finally
            {
                _manualTestLock.Release();
            }
        }

        private async Task<bool> SendEnterAtpAsync()
        {
            try
            {
                var token = _cts?.Token ?? CancellationToken.None;
                for (int attempt = 1; attempt <= EnterAtpMaxRetries; attempt++)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 发送进入ATP(第{attempt}次)：TX={EnterAtpTxChannel}, RX={EnterAtpRxChannel}, Labels=0x31 0x32 0x33 0x34");

                    try
                    {
                        await _simulation.ClearRxFifoAsync(EnterAtpRxChannel);
                    }
                    catch
                    {
                    }

                    await Task.Delay(50, token);

                    var resp = await _simulation.SendBenchCommandAndWaitWithFragmentLabelsAsync(
                        EnterAtpTxChannel,
                        EnterAtpRxChannel,
                        EnterAtpCommand,
                        b => b != null && b.SequenceEqual(EnterAtpOk),
                        timeoutMs: EnterAtpTimeoutMs,
                        msg => AddLog(msg),
                        token);

                    if (resp != null)
                    {
                        EnterAtpRxDataText = $"0x{FormatBytes(resp)}";
                        AddLog($"[{DateTime.Now:HH:mm:ss}] 收到ATP OK，进入ATP成功");
                        return true;
                    }

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 进入ATP第{attempt}次超时，未收到OK");

                    if (attempt < EnterAtpMaxRetries)
                    {
                        await Task.Delay(200, token);
                    }
                }

                AddLog($"[{DateTime.Now:HH:mm:ss}] 进入ATP失败：已重试{EnterAtpMaxRetries}次均超时");
                return false;
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 进入ATP异常：{ex.Message}");
                return false;
            }
        }

        private async Task<bool> SendExitAtpAsync()
        {
            try
            {
                var token = _cts?.Token ?? CancellationToken.None;
                AddLog($"[{DateTime.Now:HH:mm:ss}] 发送退出ATP：TX={ExitAtpTxChannel}, RX={ExitAtpRxChannel}, Labels=0x31 0x32 0x33 0x34");

                try
                {
                    await _simulation.ClearRxFifoAsync(ExitAtpRxChannel);
                }
                catch
                {
                }

                await Task.Delay(50, token);

                var resp = await _simulation.SendBenchCommandAndWaitWithFragmentLabelsAsync(
                    ExitAtpTxChannel,
                    ExitAtpRxChannel,
                    ExitAtpCommand,
                    b => b != null && b.SequenceEqual(ExitAtpOk),
                    timeoutMs: EnterAtpTimeoutMs,
                    msg => AddLog(msg),
                    token);

                if (resp != null)
                {
                    ExitAtpRxDataText = $"0x{FormatBytes(resp)}";
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 收到EXIT OK，退出ATP成功");
                    return true;
                }

                AddLog($"[{DateTime.Now:HH:mm:ss}] 退出ATP超时，未收到OK");
                return false;
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 退出ATP异常：{ex.Message}");
                return false;
            }
        }

        private async Task<bool> SendTestCommandAsync()
        {
            try
            {
                var token = _cts?.Token ?? CancellationToken.None;
                AddLog($"[{DateTime.Now:HH:mm:ss}] (2) 发送测试指令：TX={TestCommandTxChannel}, RX={TestBenchRxChannel}, Labels=0x31 0x32 0x33 0x34, Data={FormatBytes(AbA825TransmitCommand)}");

                try
                {
                    await _simulation.ClearRxFifoAsync(TestBenchRxChannel);
                }
                catch
                {
                }

                await Task.Delay(50, token);

                var resp = await _simulation.SendBenchCommandAndWaitWithFragmentLabelsAsync(
                    TestCommandTxChannel,
                    TestBenchRxChannel,
                    AbA825TransmitCommand,
                    b => b != null && b.Length == 8 && b[0] == 0x05 && b[1] == 0x01 && b[2] == 0x01 && b[3] == 0x01,
                    timeoutMs: TestTimeoutMs,
                    msg => AddLog(msg),
                    token);

                if (resp == null)
                {
                    TestRxDataText = "--";
                    TestCollectiveValueText = "--";
                    AddLog($"[{DateTime.Now:HH:mm:ss}] (3) 接收超时");
                    return false;
                }

                var rxHex = FormatBytes(resp);
                int value32Be = (resp[4] << 24) | (resp[5] << 16) | (resp[6] << 8) | resp[7];
                int value16Le = (resp[7] << 8) | resp[6];
                string tail2Hex = $"{resp[6]:X2} {resp[7]:X2}";

                TestCollectiveValueText = value32Be == ExpectedCollectiveValue ? value32Be.ToString() : value16Le.ToString();
                TestRxDataText = $"{TestCollectiveValueText} ({tail2Hex}) | 0x{rxHex}";
                AddLog($"[{DateTime.Now:HH:mm:ss}] (3) 接收信息：{FormatBytes(resp)}，集体数据(32位大端)={value32Be}，尾2字节={tail2Hex}(16位小端)={value16Le}");

                // 判据兼容：上位机可能显示“12700”(数值) 或 “9C 31”(字节序显示)
                return value32Be == ExpectedCollectiveValue || value16Le == ExpectedCollectiveValue;
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] AB_A825_TRANSMIT异常：{ex.Message}");
                return false;
            }
        }

        private async Task<bool> ListenCanFor12700Async()
        {
            await _canOpLock.WaitAsync();
            try
            {
                CanRxDataText = "--";

                var rxIndex = ParseCanChannelIndex(CanRxChannel);
                if (rxIndex < 0)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] CAN监听失败：通道选择无效({CanRxChannel})");
                    return false;
                }

                var ok = await OpenCanChannel500kAsync(rxIndex);
                if (!ok)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] CAN监听失败：打开通道失败({CanRxChannel})");
                    return false;
                }

                AddLog($"[{DateTime.Now:HH:mm:ss}] 开始监听CAN：RX={CanRxChannel}，500K，等待出现12700(0x319C)或字节序9C 31/31 9C");

                var token = _cts?.Token ?? CancellationToken.None;
                var deadline = DateTime.UtcNow.AddMilliseconds(CanListenTimeoutMs);
                while (!token.IsCancellationRequested && DateTime.UtcNow <= deadline)
                {
                    var frames = await _canDriver.ReceiveFramesBatchAsync(rxIndex, 30, 0.02);
                    if (frames != null && frames.Count > 0)
                    {
                        foreach (var f in frames)
                        {
                            if (f.nFrameType != (byte)PXI4004.ARTCANX1_CAN_FRAME_TYPE_DATA_FRM)
                                continue;

                            var len = f.nDataLength;
                            if (len <= 0)
                                continue;

                            var hex = FormatData(f.DataBuf, len);
                            CanRxDataText = hex;
                            AddLog($"[{DateTime.Now:HH:mm:ss}] CAN RX：{CanRxChannel}, ID=0x{f.nFrameID:X}, Len={len}, Data={hex}");

                            if (Contains12700Pattern(f.DataBuf, len))
                            {
                                TestCollectiveValueText = ExpectedCollectiveValue.ToString();
                                TestRxDataText = $"{ExpectedCollectiveValue} (CAN) | {hex}";
                                CanRxDataText = hex;
                                AddLog($"[{DateTime.Now:HH:mm:ss}] 判据命中：CAN数据包含12700(0x319C) -> PASS");
                                return true;
                            }
                        }
                    }

                    await Task.Delay(10, token);
                }

                AddLog($"[{DateTime.Now:HH:mm:ss}] CAN监听超时：未发现12700(0x319C)");
                return false;
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] CAN监听异常：{ex.Message}");
                return false;
            }
            finally
            {
                _canOpLock.Release();
            }
        }

        private async Task OnListenCanAsync()
        {
            if (!IsManualTestRunning)
                return;

            try
            {
                CanRxDataText = "--";

                var ok = await EnsureCanDriverReadyAsync();
                if (!ok)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] CAN监听失败：CAN驱动未就绪");
                    return;
                }

                await ListenCanFor12700Async();
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] CAN监听异常：{ex.Message}");
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

                return false;
            }
            catch
            {
                return false;
            }
        }

        private async Task DisconnectCanAsync()
        {
            var d = _canDriver;
            _canDriver = null;
            if (d != null)
            {
                try { await d.DisconnectAsync(); } catch { }
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
            catch
            {
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

        private static bool Contains12700Pattern(byte[] data, int length)
        {
            if (data == null)
                return false;

            var len = Math.Min(length, data.Length);
            if (len <= 0)
                return false;

            var p16Le = new byte[] { 0x9C, 0x31 };
            var p16Be = new byte[] { 0x31, 0x9C };
            var p32Be = new byte[] { 0x00, 0x00, 0x31, 0x9C };
            var p32Le = new byte[] { 0x9C, 0x31, 0x00, 0x00 };

            return Contains(data, len, p16Le) || Contains(data, len, p16Be) || Contains(data, len, p32Be) || Contains(data, len, p32Le);
        }

        private static bool Contains(byte[] data, int len, byte[] pattern)
        {
            if (pattern == null || pattern.Length == 0 || len < pattern.Length)
                return false;

            for (int i = 0; i <= len - pattern.Length; i++)
            {
                bool ok = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (data[i + j] != pattern[j])
                    {
                        ok = false;
                        break;
                    }
                }
                if (ok) return true;
            }

            return false;
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

        private static string FormatBytes(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                return "--";
            return string.Join(" ", bytes.Select(b => b.ToString("X2")));
        }

        private void AddLog(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            try
            {
                Logs.Add(message);
            }
            catch
            {
            }
        }
    }
}
