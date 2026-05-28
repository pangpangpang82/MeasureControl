using MeasureControl.Drivers;
using MeasureControl.Drivers.PXI4004CAN;
using MeasureControl.Models.Devices;
using MeasureControl.Simulations.S_C_8_3_1;
using Prism.Commands;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MeasureControl.ViewModels.SingleBoardTest.AirController
{
    public class A_C_6_6_2ViewModel : AirSimpleSequenceViewModel
    {
        private const string Arinc429TxChannel = "429_CH5";
        private const string Arinc429RxChannel = "429_CH2";

        private static readonly byte[] EnterAtpCommand = { 0x30, 0x01, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] ExitAtpCommand = { 0x30, 0x02, 0x02, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] AbA825ReceiveCommand = { 0x05, 0x02, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00 };

        private static readonly byte[] ExpectedResponse = { 0x05, 0x02, 0x01, 0x02, 0x01, 0x01, 0x01, 0x01 };

        private const int CanTxChannelIndex = 0;
        private const uint CanFrameId = 0x582010;
        private static readonly byte[] CanTxData = { 0x00, 0x00, 0x00, 0x00, 0x01, 0x01, 0x01, 0x01 };
        private const uint CanBaudRate125K = 125000;
        private const int ResponseTimeoutMs = 3000;

        private readonly S_C_8_3_1Simulation _arinc = new S_C_8_3_1Simulation();
        private readonly SemaphoreSlim _opLock = new SemaphoreSlim(1, 1);
        private PXI4004Driver _canDriver;

        private CancellationTokenSource _testCts;
        private bool _isAutoTestRunning;
        private string _actualResponseText = "--";
        private string _parsedValueText = "--";

        public A_C_6_6_2ViewModel()
        {
            Title = "6.6.2 控制通道CAN接收测试";
            AutoSequenceCommand = new DelegateCommand(async () => await OnAutoSequenceAsync());
        }

        public new DelegateCommand AutoTestCommand => AutoSequenceCommand;

        public DelegateCommand AutoSequenceCommand { get; }

        public new bool IsAutoTestRunning
        {
            get => _isAutoTestRunning;
            private set => SetProperty(ref _isAutoTestRunning, value);
        }

        public string ActualResponseText
        {
            get => _actualResponseText;
            set => SetProperty(ref _actualResponseText, value);
        }

        public string ParsedValueText
        {
            get => _parsedValueText;
            set => SetProperty(ref _parsedValueText, value);
        }

        public new string TestCommandBytesText => "0x05 02 01 01 00 00 00 00";

        public string CanConfigText => "CAN0, 扩展帧, ID=0x582010, 125K";

        public string CanTxDataText => "00 00 00 00 01 01 01 01";

        public string ExpectedResponseText => "0x05 02 01 02 01 01 01 01";

        private async Task OnAutoSequenceAsync()
        {
            if (IsAutoTestRunning)
            {
                try { _testCts?.Cancel(); } catch { }
                return;
            }

            IsAutoTestRunning = true;
            _testCts?.Dispose();
            _testCts = new CancellationTokenSource();

            try
            {
                await RunAutoTestAsync(_testCts.Token);
            }
            finally
            {
                IsAutoTestRunning = false;
            }
        }

        protected override async Task RunAutoTestAsync(CancellationToken token)
        {
            try
            {
                token.ThrowIfCancellationRequested();

                LastTestTime = "--";
                LastTestResult = "--";
                ActualResponseText = "--";
                ParsedValueText = "--";

                AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试启动");
                AddLog($"[{DateTime.Now:HH:mm:ss}] 429通道：TX={Arinc429TxChannel}, RX={Arinc429RxChannel}");

                _arinc.IsRealProduct = true;
                _arinc.ArincRate = 100000.0;
                await _arinc.StartAsync(Arinc429TxChannel, Arinc429RxChannel, msg => AddLog(msg));

                token.ThrowIfCancellationRequested();
                AddLog($"[{DateTime.Now:HH:mm:ss}] 步骤1：进入ATP模式");
                await SendEnterAtpAsync(token);

                token.ThrowIfCancellationRequested();
                AddLog($"[{DateTime.Now:HH:mm:ss}] 步骤2：CAN0发送数据");
                var canOk = await EnsureCanDriverReadyAsync();
                if (!canOk)
                {
                    LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    LastTestResult = "FAIL";
                    AddLog($"[{DateTime.Now:HH:mm:ss}] CAN驱动初始化失败");
                    return;
                }

                var channelOpened = await OpenCanChannel125kExtendedAsync(CanTxChannelIndex);
                if (!channelOpened)
                {
                    LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    LastTestResult = "FAIL";
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 打开CAN通道失败");
                    return;
                }

                var sendOk = await SendCanDataAsync(token);
                if (!sendOk)
                {
                    LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    LastTestResult = "FAIL";
                    AddLog($"[{DateTime.Now:HH:mm:ss}] CAN数据发送失败");
                    return;
                }

                token.ThrowIfCancellationRequested();
                AddLog($"[{DateTime.Now:HH:mm:ss}] 步骤3：发送429测试指令 AB_A825_RECEIVE");
                var testOk = await SendTestCommandAndCheckResponseAsync(token);

                token.ThrowIfCancellationRequested();
                AddLog($"[{DateTime.Now:HH:mm:ss}] 步骤4：退出ATP模式");
                await SendExitAtpAsync(token);

                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                LastTestResult = testOk ? "PASS" : "FAIL";
                AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试完成：{LastTestResult}");
            }
            catch (OperationCanceledException)
            {
                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                LastTestResult = "已停止";
                AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试已停止");
            }
            catch (Exception ex)
            {
                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                LastTestResult = "异常";
                AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试异常：{ex.Message}");
            }
            finally
            {
                try { await _arinc.StopAsync(msg => AddLog(msg)); } catch { }
                try { await DisconnectCanAsync(); } catch { }
            }
        }

        private async Task SendEnterAtpAsync(CancellationToken token)
        {
            await _opLock.WaitAsync(token);
            try
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 发送进入ATP：{FormatBytesHex(EnterAtpCommand)}");
                await _arinc.SendBenchCommandOnlyAsync(Arinc429TxChannel, EnterAtpCommand, msg => AddLog(msg), token);
                await Task.Delay(100, token);
                AddLog($"[{DateTime.Now:HH:mm:ss}] 进入ATP指令已发送");
            }
            finally
            {
                _opLock.Release();
            }
        }

        private async Task<bool> SendCanDataAsync(CancellationToken token)
        {
            await _opLock.WaitAsync(token);
            try
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] CAN发送：CH{CanTxChannelIndex}, 扩展帧, ID=0x{CanFrameId:X}, Data={FormatBytesHex(CanTxData)}");

                var frame = PXI4004.CreateExtendedDataFrame(CanFrameId, CanTxData);

                bool sent = false;
                for (int i = 1; i <= 3; i++)
                {
                    sent = await _canDriver.SendFrameAsync(CanTxChannelIndex, frame, 0.2);
                    if (sent)
                        break;
                    await Task.Delay(50, token);
                }

                if (sent)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] CAN数据发送成功");
                }
                else
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] CAN数据发送失败");
                }

                await Task.Delay(100, token);
                return sent;
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] CAN发送异常：{ex.Message}");
                return false;
            }
            finally
            {
                _opLock.Release();
            }
        }

        private async Task<bool> SendTestCommandAndCheckResponseAsync(CancellationToken token)
        {
            await _opLock.WaitAsync(token);
            try
            {
                ActualResponseText = "--";
                ParsedValueText = "--";

                AddLog($"[{DateTime.Now:HH:mm:ss}] 发送AB_A825_RECEIVE：{FormatBytesHex(AbA825ReceiveCommand)}");

                try { await _arinc.ClearRxFifoAsync(Arinc429RxChannel); } catch { }
                await Task.Delay(20, token);

                await _arinc.SendBenchCommandOnlyAsync(Arinc429TxChannel, AbA825ReceiveCommand, msg => AddLog(msg), token);

                var resp = await _arinc.WaitBenchResponse8Async(
                    Arinc429RxChannel,
                    b => b != null && b.Length >= 8 && b[0] == 0x05 && b[1] == 0x02 && b[2] == 0x01 && b[3] == 0x02,
                    timeoutMs: ResponseTimeoutMs,
                    msg => AddLog(msg),
                    token);

                if (resp == null)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 429接收超时：未收到预期响应");
                    return false;
                }

                ActualResponseText = "0x" + FormatBytesHex(resp);
                AddLog($"[{DateTime.Now:HH:mm:ss}] 429接收：{ActualResponseText}");

                ParsedValueText = resp.Length >= 8 ? $"{resp[4]:X2} {resp[5]:X2} {resp[6]:X2} {resp[7]:X2}" : "--";
                AddLog($"[{DateTime.Now:HH:mm:ss}] 解析：{ParsedValueText}");

                bool pass = resp.Length >= 8 &&
                            resp[4] == 0x01 && resp[5] == 0x01 &&
                            resp[6] == 0x01 && resp[7] == 0x01;

                if (pass)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 判据命中：后四字节为 01 01 01 01 -> PASS");
                }
                else
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 判据不符：后四字节不是 01 01 01 01 -> FAIL");
                }

                return pass;
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 测试指令异常：{ex.Message}");
                return false;
            }
            finally
            {
                _opLock.Release();
            }
        }

        private async Task SendExitAtpAsync(CancellationToken token)
        {
            await _opLock.WaitAsync(token);
            try
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 发送退出ATP：{FormatBytesHex(ExitAtpCommand)}");
                await _arinc.SendBenchCommandOnlyAsync(Arinc429TxChannel, ExitAtpCommand, msg => AddLog(msg), token);
                await Task.Delay(100, token);
                AddLog($"[{DateTime.Now:HH:mm:ss}] 退出ATP指令已发送");
            }
            finally
            {
                _opLock.Release();
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
                        AddLog($"[{DateTime.Now:HH:mm:ss}] CAN已连接：逻辑设备{logicalIndex}");
                        return true;
                    }
                }

                AddLog($"[{DateTime.Now:HH:mm:ss}] CAN连接失败：未探测到可用PXI4004");
                return false;
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] CAN驱动异常：{ex.Message}");
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
                AddLog($"[{DateTime.Now:HH:mm:ss}] CAN已断开");
            }
            catch { }
            finally
            {
                _canDriver = null;
            }
        }

        private async Task<bool> OpenCanChannel125kExtendedAsync(int channelIndex)
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

                param.nBaudRate = CanBaudRate125K;
                param.nWorkMode = (byte)PXI4004.ARTCANX1_CAN_WORKMODE_NORMAL;
                param.bRecvTimestampEn = 1;
                param.bAccExtID = 1;
                param.nAccFilterCnt = (byte)PXI4004.ARTCANX1_CAN_ACC_NUM_NONE;
                param.nAccCodeA = 0x00000000;
                param.nAccCodeB = 0x00000000;
                param.nAccMaskA = 0xFFFFFFFF;
                param.nAccMaskB = 0xFFFFFFFF;
                param.nFrameInterval = 0;
                param.SendTrig.nTriggerType = PXI4004.ARTCANX1_TRIGTYPE_NONE;

                var ok = await _canDriver.OpenChannelAsync(channelIndex, param);
                if (ok)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] CAN通道{channelIndex}已打开：扩展帧，125K");
                }
                else
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 打开CAN通道{channelIndex}失败");
                }
                return ok;
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 打开CAN通道{channelIndex}失败：{ex.Message}");
                return false;
            }
        }

        private static string FormatBytesHex(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                return string.Empty;
            return string.Join(" ", bytes.Select(b => b.ToString("X2")));
        }
    }
}
