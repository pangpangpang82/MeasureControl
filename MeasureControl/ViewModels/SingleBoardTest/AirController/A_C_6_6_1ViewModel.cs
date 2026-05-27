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
    public class A_C_6_6_1ViewModel : AirSimpleSequenceViewModel
    {
        private const string Arinc429TxChannel = "429_CH5";
        private const string Arinc429RxChannel = "429_CH2";

        private static readonly byte[] EnterAtpCommand = { 0x30, 0x01, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] ExitAtpCommand = { 0x30, 0x02, 0x02, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] AbA825TransmitCommand = { 0x05, 0x01, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00 };

        private const int CanRxChannelIndex = 0;
        private const uint CanBaudRate125K = 125000;
        private const int CanListenTimeoutMs = 3000;

        private readonly S_C_8_3_1Simulation _arinc = new S_C_8_3_1Simulation();
        private readonly SemaphoreSlim _opLock = new SemaphoreSlim(1, 1);
        private PXI4004Driver _canDriver;

        private CancellationTokenSource _testCts;
        private bool _isAutoTestRunning;
        private string _canRxDataText = "--";
        private string _parsedValueText = "--";

        public A_C_6_6_1ViewModel()
        {
            Title = "6.6.1 控制通道CAN发送测试";
            AutoSequenceCommand = new DelegateCommand(async () => await OnAutoSequenceAsync());
        }

        public new DelegateCommand AutoTestCommand => AutoSequenceCommand;

        public DelegateCommand AutoSequenceCommand { get; }

        public new bool IsAutoTestRunning
        {
            get => _isAutoTestRunning;
            private set => SetProperty(ref _isAutoTestRunning, value);
        }

        public string CanRxDataText
        {
            get => _canRxDataText;
            set => SetProperty(ref _canRxDataText, value);
        }

        public string ParsedValueText
        {
            get => _parsedValueText;
            set => SetProperty(ref _parsedValueText, value);
        }

        public new string TestCommandBytesText => "0x05 01 01 01 00 00 00 00";

        public string CanConfigText => "CAN0, 扩展帧, 125K";

        public string ExpectedCanDataText => "01 9C 31 ... (12700)";

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
                CanRxDataText = "--";
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
                AddLog($"[{DateTime.Now:HH:mm:ss}] 步骤2：发送429测试指令 AB_A825_TRANSMIT");
                await SendTestCommandAsync(token);

                token.ThrowIfCancellationRequested();
                AddLog($"[{DateTime.Now:HH:mm:ss}] 步骤3：CAN0通道接收信息");
                var canOk = await EnsureCanDriverReadyAsync();
                if (!canOk)
                {
                    LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    LastTestResult = "FAIL";
                    AddLog($"[{DateTime.Now:HH:mm:ss}] CAN驱动初始化失败");
                    return;
                }

                var channelOpened = await OpenCanChannel125kExtendedAsync(CanRxChannelIndex);
                if (!channelOpened)
                {
                    LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    LastTestResult = "FAIL";
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 打开CAN通道失败");
                    return;
                }

                var testOk = await ListenCanFor12700Async(token);

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

        private async Task SendTestCommandAsync(CancellationToken token)
        {
            await _opLock.WaitAsync(token);
            try
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 发送AB_A825_TRANSMIT：{FormatBytesHex(AbA825TransmitCommand)}");
                AddLog($"[{DateTime.Now:HH:mm:ss}] 使B控制通道通过CAN发送通道将通风RFAN速度设置为12700(RPM)");

                await _arinc.SendBenchCommandOnlyAsync(Arinc429TxChannel, AbA825TransmitCommand, msg => AddLog(msg), token);

                await Task.Delay(100, token);
            }
            finally
            {
                _opLock.Release();
            }
        }

        private async Task<bool> ListenCanFor12700Async(CancellationToken token)
        {
            await _opLock.WaitAsync(token);
            try
            {
                CanRxDataText = "--";
                ParsedValueText = "--";

                AddLog($"[{DateTime.Now:HH:mm:ss}] 开始监听CAN0：扩展帧，125K，等待前三位为 01 9C 31");

                var deadline = DateTime.UtcNow.AddMilliseconds(CanListenTimeoutMs);
                while (!token.IsCancellationRequested && DateTime.UtcNow <= deadline)
                {
                    var frames = await _canDriver.ReceiveFramesBatchAsync(CanRxChannelIndex, 30, 0.05);
                    if (frames != null && frames.Count > 0)
                    {
                        foreach (var f in frames)
                        {
                            if (f.nFrameType != (byte)PXI4004.ARTCANX1_CAN_FRAME_TYPE_DATA_FRM)
                                continue;

                            var len = f.nDataLength;
                            if (len < 3)
                                continue;

                            var hex = FormatData(f.DataBuf, len);
                            CanRxDataText = hex;
                            AddLog($"[{DateTime.Now:HH:mm:ss}] CAN RX：CH{CanRxChannelIndex}, ID=0x{f.nFrameID:X}, Len={len}, Data={hex}");

                            if (len >= 3 && f.DataBuf[0] == 0x01 && f.DataBuf[1] == 0x9C && f.DataBuf[2] == 0x31)
                            {
                                int parsedValue = (f.DataBuf[2] << 8) | f.DataBuf[1];
                                ParsedValueText = "9C 31 -> 12700";
                                CanRxDataText = hex;
                                AddLog($"[{DateTime.Now:HH:mm:ss}] 判据命中：前三位为 01 9C 31，9C 31翻转为31 9C = {parsedValue} -> PASS");
                                return true;
                            }

                            if (Contains12700Pattern(f.DataBuf, len))
                            {
                                ParsedValueText = "9C 31 -> 12700";
                                CanRxDataText = hex;
                                AddLog($"[{DateTime.Now:HH:mm:ss}] 判据命中：CAN数据包含12700(0x319C) -> PASS");
                                return true;
                            }
                        }
                    }

                    await Task.Delay(20, token);
                }

                AddLog($"[{DateTime.Now:HH:mm:ss}] CAN监听超时：未发现预期数据");
                return false;
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] CAN监听异常：{ex.Message}");
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

        private static bool Contains12700Pattern(byte[] data, int length)
        {
            if (data == null)
                return false;

            var len = Math.Min(length, data.Length);
            if (len <= 0)
                return false;

            var p16Le = new byte[] { 0x9C, 0x31 };
            var p16Be = new byte[] { 0x31, 0x9C };

            return Contains(data, len, p16Le) || Contains(data, len, p16Be);
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

        private static string FormatBytesHex(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                return string.Empty;
            return string.Join(" ", bytes.Select(b => b.ToString("X2")));
        }
    }
}
