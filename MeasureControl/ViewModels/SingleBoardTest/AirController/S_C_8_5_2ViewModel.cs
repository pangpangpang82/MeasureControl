using MeasureControl.Drivers;
using MeasureControl.Drivers.PXI4004CAN;
using MeasureControl.Models.Devices;
using MeasureControl.Simulations.S_C_8_3_1;
using Prism.Commands;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace MeasureControl.ViewModels.SingleBoardTest.AirController
{
    public class S_C_8_5_2ViewModel : AirSimpleSequenceViewModel
    {
        private static readonly byte[] AirSafetyAtpR = { 0x30, 0x01, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] AirSafetyAtpEnterOk = { 0x30, 0x01, 0x01, 0x02, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] AtpE = { 0x30, 0x02, 0x02, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] ExitOk = { 0x30, 0x02, 0x02, 0x02, 0x00, 0x00, 0x00, 0x00 };

        private static readonly byte[] SArinc825InCommand8 = { 0x14, 0x02, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] SArinc825InExpected8 = { 0x14, 0x02, 0x01, 0x02, 0x01, 0x01, 0x01, 0x01 };

        private const int CanTxChannelIndex = 1;
        private const uint CanFrameId = 0x711;
        private static readonly byte[] CanTxData = { 0x00, 0x00, 0x00, 0x00, 0x01, 0x01, 0x01, 0x01 };

        private readonly S_C_8_3_1Simulation _arinc = new S_C_8_3_1Simulation();
        private readonly SemaphoreSlim _opLock = new SemaphoreSlim(1, 1);
        private PXI4004Driver _canDriver;

        private CancellationTokenSource _testCts;
        private bool _isAutoTestRunning;
        private string _canRxDataText = "--";
        private string _enterAtpRxDataTextLocal = "--";
        private string _exitAtpRxDataTextLocal = "--";

        public S_C_8_5_2ViewModel()
        {
            Title = "8.5.2 S安全通道CAN接收测试";
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

        public new string EnterAtpRxDataText
        {
            get => _enterAtpRxDataTextLocal;
            set => SetProperty(ref _enterAtpRxDataTextLocal, value);
        }

        public new string ExitAtpRxDataText
        {
            get => _exitAtpRxDataTextLocal;
            set => SetProperty(ref _exitAtpRxDataTextLocal, value);
        }

        public new string TestCommandBytesText => "0x14 02 01 01 00 00 00 00";

        public string ExpectedResponseText => "0x14 02 01 02 01 01 01 01";

        public string CanSendDataText => "00 00 00 00 01 01 01 01";

        public string CanFrameIdText => $"0x{CanFrameId:X3} (711)";

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
                EnterAtpRxDataText = "--";
                ExitAtpRxDataText = "--";

                AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试启动");

                _arinc.IsRealProduct = true;
                _arinc.ArincRate = 100000.0;
                await _arinc.StartAsync(EnterAtpTxChannel, EnterAtpRxChannel, msg => AddLog(msg));

                token.ThrowIfCancellationRequested();
                AddLog($"[{DateTime.Now:HH:mm:ss}] 步骤1：进入ATP模式");
                var enterOk = await SendEnterAtpAndWaitAsync(token);
                if (!enterOk)
                {
                    LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    LastTestResult = "FAIL";
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 进入ATP失败");
                    return;
                }

                token.ThrowIfCancellationRequested();
                AddLog($"[{DateTime.Now:HH:mm:ss}] 步骤2：向CAN通道发送数据 {FormatBytesHex(CanTxData)}");
                var canOk = await SendCanDataAsync(token);
                if (!canOk)
                {
                    LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    LastTestResult = "FAIL";
                    AddLog($"[{DateTime.Now:HH:mm:ss}] CAN发送失败");
                    return;
                }

                token.ThrowIfCancellationRequested();
                AddLog($"[{DateTime.Now:HH:mm:ss}] 步骤3：发送S_ARINC825_IN指令并等待响应");
                var testOk = await SendSArinc825InAndWaitAsync(token);

                token.ThrowIfCancellationRequested();
                AddLog($"[{DateTime.Now:HH:mm:ss}] 步骤4：退出ATP模式");
                await SendExitAtpAndWaitAsync(token);

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

        private async Task<bool> SendEnterAtpAndWaitAsync(CancellationToken token)
        {
            await _opLock.WaitAsync(token);
            try
            {
                EnterAtpRxDataText = "--";

                try { await _arinc.ClearRxFifoAsync(EnterAtpRxChannel); } catch { }
                await Task.Delay(20, token);

                AddLog($"[{DateTime.Now:HH:mm:ss}] 发送进入ATP：{FormatBytesHex(AirSafetyAtpR)}");
                await _arinc.SendBenchCommandOnlyAsync(EnterAtpTxChannel, AirSafetyAtpR, msg => AddLog(msg), token);

                var resp = await _arinc.WaitBenchResponse8Async(
                    EnterAtpRxChannel,
                    b => b != null && b.SequenceEqual(AirSafetyAtpEnterOk),
                    timeoutMs: 2000,
                    msg => AddLog(msg),
                    token);

                if (resp != null)
                {
                    EnterAtpRxDataText = "0x" + FormatBytesHex(resp);
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 进入ATP成功：{EnterAtpRxDataText}");
                    return true;
                }

                AddLog($"[{DateTime.Now:HH:mm:ss}] 进入ATP超时");
                return false;
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
                var ok = await EnsureCanDriverReadyAsync();
                if (!ok)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] CAN驱动未就绪");
                    return false;
                }

                ok = await OpenCanChannel500kAsync(CanTxChannelIndex);
                if (!ok)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 打开CAN通道{CanTxChannelIndex}失败");
                    return false;
                }

                var frame = PXI4004.CreateDataFrame(CanFrameId, CanTxData);
                AddLog($"[{DateTime.Now:HH:mm:ss}] CAN发送：CH{CanTxChannelIndex}, ID=0x{CanFrameId:X3}, 标准帧, 500K, Data={FormatBytesHex(CanTxData)}");

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
                    AddLog($"[{DateTime.Now:HH:mm:ss}] CAN发送成功");
                    return true;
                }

                AddLog($"[{DateTime.Now:HH:mm:ss}] CAN发送失败");
                return false;
            }
            finally
            {
                _opLock.Release();
            }
        }

        private async Task<bool> SendSArinc825InAndWaitAsync(CancellationToken token)
        {
            await _opLock.WaitAsync(token);
            try
            {
                CanRxDataText = "--";

                try { await _arinc.ClearRxFifoAsync(EnterAtpRxChannel); } catch { }
                await Task.Delay(20, token);

                AddLog($"[{DateTime.Now:HH:mm:ss}] 发送S_ARINC825_IN：{FormatBytesHex(SArinc825InCommand8)}");
                await _arinc.SendBenchCommandOnlyAsync(EnterAtpTxChannel, SArinc825InCommand8, msg => AddLog(msg), token);

                var resp = await _arinc.WaitBenchResponse8Async(
                    EnterAtpRxChannel,
                    b => b != null && b.SequenceEqual(SArinc825InExpected8),
                    timeoutMs: 2000,
                    msg => AddLog(msg),
                    token);

                if (resp != null)
                {
                    CanRxDataText = "0x" + FormatBytesHex(resp);
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 收到预期响应：{CanRxDataText} -> PASS");
                    return true;
                }

                AddLog($"[{DateTime.Now:HH:mm:ss}] 未收到预期响应 0x{FormatBytesHex(SArinc825InExpected8)} -> FAIL");
                return false;
            }
            finally
            {
                _opLock.Release();
            }
        }

        private async Task SendExitAtpAndWaitAsync(CancellationToken token)
        {
            await _opLock.WaitAsync(token);
            try
            {
                ExitAtpRxDataText = "--";

                try { await _arinc.ClearRxFifoAsync(ExitAtpRxChannel); } catch { }
                await Task.Delay(20, token);

                AddLog($"[{DateTime.Now:HH:mm:ss}] 发送退出ATP：{FormatBytesHex(AtpE)}");
                await _arinc.SendBenchCommandOnlyAsync(ExitAtpTxChannel, AtpE, msg => AddLog(msg), token);

                var resp = await _arinc.WaitBenchResponse8Async(
                    ExitAtpRxChannel,
                    b => b != null && b.SequenceEqual(ExitOk),
                    timeoutMs: 2000,
                    msg => AddLog(msg),
                    token);

                if (resp != null)
                {
                    ExitAtpRxDataText = "0x" + FormatBytesHex(resp);
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 退出ATP成功：{ExitAtpRxDataText}");
                }
                else
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 退出ATP超时");
                }
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
            }
            catch { }
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
