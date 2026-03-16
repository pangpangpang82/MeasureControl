using Prism.Commands;
using Prism.Mvvm;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MeasureControl.Drivers.PXI4004CAN;
using MeasureControl.Models.Devices;
using MeasureControl.Drivers;
using MeasureControl.Simulations.AC_6_4;

namespace MeasureControl.ViewModels.SingleBoardTest.AirController
{
    public class CanReceiveTestViewModel : BindableBase
    {
        private static readonly byte[] EnterAtpCommand = { 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] EnterAtpOk = { 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x02 };
        private static readonly byte[] ExitAtpCommand = { 0x00, 0x02, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01 };
        private static readonly byte[] ExitAtpOk = { 0x00, 0x02, 0x00, 0x01, 0x00, 0x00, 0x00, 0x03 };
        private static readonly byte[] AbA825ReceiveCommand = { 0x05, 0x02, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00 };

        private const uint CanInjectFrameId = 0;
        private static readonly byte[] CanInjectData = { 0x01, 0x01, 0x01, 0x01 };

        private const int EnterAtpMaxRetries = 3;
        private const int EnterAtpTimeoutMs = 3000;
        private const int TestTimeoutMs = 3000;

        private readonly SemaphoreSlim _manualTestLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _canOpLock = new SemaphoreSlim(1, 1);
        private CancellationTokenSource _cts;
        private readonly AC_6_4Simulation _simulation = new AC_6_4Simulation();
        private PXI4004Driver _canDriver;

        private string _title = "6.6.2CAN接收测试";
        private bool _isManualTestRunning;
        private string _lastTestTime = "--";
        private string _lastTestResult = "--";

        private string _enterAtpTxChannel = "429_CH0";
        private string _enterAtpRxChannel = "429_CH1";
        private string _exitAtpTxChannel = "429_CH0";
        private string _exitAtpRxChannel = "429_CH1";
        private string _testControllerRxChannel = "429_CH2";
        private string _testBenchRxChannel = "429_CH3";
        private string _canTxChannel = "CH4";

        private string _enterAtpRxDataText = "--";
        private string _canInjectStatusText = "--";
        private string _testRxDataText = "--";
        private string _testCollectiveValueText = "--";
        private string _exitAtpRxDataText = "--";

        private bool _isBusy;

        public CanReceiveTestViewModel()
        {
            ManualTestCommand = new DelegateCommand(OnManualTest);
            ClearLogCommand = new DelegateCommand(() => Logs.Clear());
            SendEnterAtpCommand = new DelegateCommand(async () => await SendEnterAtpAsync());
            InjectCanCommand = new DelegateCommand(async () => await InjectCanAsync());
            SendTestCommand = new DelegateCommand(async () => await SendTestCommandAsync());
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
        public DelegateCommand InjectCanCommand { get; }
        public DelegateCommand SendTestCommand { get; }
        public DelegateCommand SendExitAtpCommand { get; }

        public bool IsBusy
        {
            get => _isBusy;
            set => SetProperty(ref _isBusy, value);
        }

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

        public string TestControllerRxChannel
        {
            get => _testControllerRxChannel;
            set => SetProperty(ref _testControllerRxChannel, value);
        }

        public string TestBenchRxChannel
        {
            get => _testBenchRxChannel;
            set => SetProperty(ref _testBenchRxChannel, value);
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
                CanInjectStatusText = "--";
                TestRxDataText = "--";
                TestCollectiveValueText = "--";
                ExitAtpRxDataText = "--";

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

                try
                {
                    await InjectCanAsync();
                }
                catch
                {
                }

                var passed = await SendTestCommandAsync();
                LastTestResult = passed ? "检查通过" : "检查不通过";

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
                if (!IsManualTestRunning || IsBusy)
                    return false;

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
                if (!IsManualTestRunning || IsBusy)
                    return false;

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
                if (!IsManualTestRunning || IsBusy)
                    return false;

                var token = _cts?.Token ?? CancellationToken.None;
                AddLog($"[{DateTime.Now:HH:mm:ss}] (3) 发送测试指令：TX={TestControllerRxChannel}, RX={TestBenchRxChannel}, Labels=0x31 0x32 0x33 0x34, Data={FormatBytes(AbA825ReceiveCommand)}");

                try
                {
                    await _simulation.ClearRxFifoAsync(TestBenchRxChannel);
                }
                catch
                {
                }

                await Task.Delay(50, token);

                var resp = await _simulation.SendBenchCommandAndWaitWithFragmentLabelsAsync(
                    TestControllerRxChannel,
                    TestBenchRxChannel,
                    AbA825ReceiveCommand,
                    b => b != null && b.Length == 8 && b[0] == 0x04 && b[1] == 0x01 && b[2] == 0x02 && b[3] == 0x03,
                    timeoutMs: TestTimeoutMs,
                    msg => AddLog(msg),
                    token);

                if (resp == null)
                {
                    TestRxDataText = "--";
                    TestCollectiveValueText = "--";
                    AddLog($"[{DateTime.Now:HH:mm:ss}] (4) 接收超时");
                    return false;
                }

                TestRxDataText = $"0x{FormatBytes(resp)}";
                var tail = resp.Skip(4).Take(4).ToArray();
                TestCollectiveValueText = FormatBytes(tail);
                AddLog($"[{DateTime.Now:HH:mm:ss}] (4) 接收信息：{FormatBytes(resp)}，后四字节={FormatBytes(tail)}");

                return tail.Length == 4 && tail.All(b => b == 0x01);
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] AB_A825_RECEIVE异常：{ex.Message}");
                return false;
            }
        }

        private async Task<bool> InjectCanAsync()
        {
            if (!IsManualTestRunning || IsBusy)
                return false;

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
                        CanInjectStatusText = "注入失败";
                        return false;
                    }

                    var ok = await EnsureCanDriverReadyAsync();
                    if (!ok)
                    {
                        AddLog($"[{DateTime.Now:HH:mm:ss}] CAN注入失败：CAN驱动未就绪");
                        CanInjectStatusText = "注入失败";
                        return false;
                    }

                    ok = await OpenCanChannel500kAsync(txIndex);
                    if (!ok)
                    {
                        AddLog($"[{DateTime.Now:HH:mm:ss}] CAN注入失败：打开通道失败 {CanTxChannel}");
                        CanInjectStatusText = "注入失败";
                        return false;
                    }

                    var frame = PXI4004.CreateDataFrame(CanInjectFrameId, CanInjectData);
                    AddLog($"[{DateTime.Now:HH:mm:ss}] (2) CAN注入：TX={CanTxChannel}, ID=0x{CanInjectFrameId:X}, Len={frame.nDataLength}, Data={FormatData(frame.DataBuf, frame.nDataLength)}");

                    bool sent = false;
                    for (int i = 1; i <= 3; i++)
                    {
                        sent = await _canDriver.SendFrameAsync(txIndex, frame, 0.2);
                        if (sent)
                            break;
                        await Task.Delay(50);
                    }

                    CanInjectStatusText = sent ? "已注入" : "注入失败";
                    AddLog($"[{DateTime.Now:HH:mm:ss}] (2) CAN注入{(sent ? "成功" : "失败")}");
                    return sent;
                }
                finally
                {
                    IsBusy = false;
                }
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] CAN注入异常：{ex.Message}");
                CanInjectStatusText = "注入失败";
                return false;
            }
            finally
            {
                _canOpLock.Release();
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
