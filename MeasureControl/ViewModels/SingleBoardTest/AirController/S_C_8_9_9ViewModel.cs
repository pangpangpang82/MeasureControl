using MeasureControl.Simulations.S_C_8_3_1;
using Prism.Commands;
using Prism.Mvvm;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MeasureControl.ViewModels.SingleBoardTest.AirController
{
    public class S_C_8_9_9ViewModel : BindableBase
    {
        private const string FixedTxChannel = "429_CH1";
        private const string FixedRxChannel = "429_CH0";

        private static readonly byte[] AirSafetyAtpR = { 0x30, 0x01, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] AirSafetyAtpEnterOk = { 0x30, 0x01, 0x01, 0x02, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] AtpE = { 0x30, 0x02, 0x02, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] ExitOk = { 0x30, 0x02, 0x02, 0x02, 0x00, 0x00, 0x00, 0x00 };

        private static readonly byte[] S_GNDOC_DISOUT01 = { 0x17, 0x01, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] S_GNDOC_DISOUT01_ACK = { 0x17, 0x01, 0x01, 0x02, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] S_GNDOC_DISOUT01_FB = { 0x17, 0x01, 0x01, 0x03, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] S_GNDOC_DISOUT01_GND_LOOPBACK = { 0x17, 0x01, 0x01, 0x04, 0x00, 0x00, 0xAA, 0xAA };
        private static readonly byte[] S_GNDOC_DISOUT01_CURRENT_PREFIX = { 0x17, 0x01, 0x01, 0x05 };
        private static readonly byte[] S_GNDOC_DISOUT02 = { 0x17, 0x01, 0x01, 0x06, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] S_GNDOC_DISOUT02_OC_LOOPBACK = { 0x17, 0x01, 0x01, 0x07, 0x00, 0x00, 0x00, 0x00 };

        private readonly S_C_8_3_1Simulation _arinc = new S_C_8_3_1Simulation();
        private readonly SemaphoreSlim _opLock = new SemaphoreSlim(1, 1);
        private readonly object _testLock = new object();

        private CancellationTokenSource _autoCts;
        private bool _isTestBusy;
        private bool _isAutoTestRunning;
        private string _lastTestTime = "--";
        private string _lastTestResult = "--";

        private string _enterAtpRxDataText = "--";
        private string _gndTestAckRxDataText = "--";
        private string _gndLoopbackRxDataText = "--";
        private string _ocLoopbackRxDataText = "--";
        private string _exitAtpRxDataText = "--";
        private string _gndLabel14ActualText = "--";
        private string _ocLabel14ActualText = "--";

        public S_C_8_9_9ViewModel()
        {
            AutoTestCommand = new DelegateCommand(OnAutoTest);
            ClearLogCommand = new DelegateCommand(() => Logs.Clear());
        }

        public string PageTitle => "8.9.9 S安全通道GND/OC型离散输出通道1输出测试";

        public string TestCommandBytesText => "0x17 01 01 01 00 00 00 00";
        public string ExpectedResponseText => "0x17 01 01 04 00 00 AA AA";

        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();

        public DelegateCommand AutoTestCommand { get; }
        public DelegateCommand ClearLogCommand { get; }

        public string EnterAtpRxDataText
        {
            get => _enterAtpRxDataText;
            set => SetProperty(ref _enterAtpRxDataText, value);
        }

        public string GndTestAckRxDataText
        {
            get => _gndTestAckRxDataText;
            set => SetProperty(ref _gndTestAckRxDataText, value);
        }

        public string GndLoopbackRxDataText
        {
            get => _gndLoopbackRxDataText;
            set => SetProperty(ref _gndLoopbackRxDataText, value);
        }

        public string OcLoopbackRxDataText
        {
            get => _ocLoopbackRxDataText;
            set => SetProperty(ref _ocLoopbackRxDataText, value);
        }

        public string ExitAtpRxDataText
        {
            get => _exitAtpRxDataText;
            set => SetProperty(ref _exitAtpRxDataText, value);
        }

        public string GndLabel14ActualText
        {
            get => _gndLabel14ActualText;
            set => SetProperty(ref _gndLabel14ActualText, value);
        }

        public string OcLabel14ActualText
        {
            get => _ocLabel14ActualText;
            set => SetProperty(ref _ocLabel14ActualText, value);
        }

        public bool IsAutoTestRunning
        {
            get => _isAutoTestRunning;
            set => SetProperty(ref _isAutoTestRunning, value);
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

        private void ClearContent()
        {
            EnterAtpRxDataText = "--";
            GndTestAckRxDataText = "--";
            GndLoopbackRxDataText = "--";
            OcLoopbackRxDataText = "--";
            ExitAtpRxDataText = "--";
            GndLabel14ActualText = "--";
            OcLabel14ActualText = "--";
        }

        private void OnAutoTest()
        {
            lock (_testLock)
            {
                if (_isTestBusy)
                {
                    // 如果正在运行则停止
                    if (IsAutoTestRunning)
                    {
                        _autoCts?.Cancel();
                    }
                    return;
                }
                _isTestBusy = true;
            }

            _ = RunAutoTestAsync();
        }

        private async Task RunAutoTestAsync()
        {
            await _opLock.WaitAsync();
            try
            {
                IsAutoTestRunning = true;
                LastTestTime = "--";
                LastTestResult = "--";
                ClearContent();

                _autoCts?.Cancel();
                _autoCts?.Dispose();
                _autoCts = new CancellationTokenSource();
                var token = _autoCts.Token;

                AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试开始");

                // 确保先停止并清理之前的状态
                try { await _arinc.StopAsync(msg => { }); } catch { }
                await Task.Delay(100);

                _arinc.IsRealProduct = true;
                _arinc.ArincRate = 100000.0;
                await _arinc.StartAsync(FixedTxChannel, FixedRxChannel, msg => AddLog(msg));

                // 启动后多次清理接收缓存，确保无残留数据
                for (int i = 0; i < 3; i++)
                {
                    try { await _arinc.ClearRxFifoAsync(FixedRxChannel); } catch { }
                    await Task.Delay(50, token);
                }

                bool enterOk = await SendEnterAtpAndWaitAsync(token);
                if (!enterOk)
                    throw new TimeoutException("进入ATP超时");

                await TestGndPhaseAsync(token);
                await TestOcPhaseAsync(token);

                await SendExitAtpAndWaitAsync(token);

                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                LastTestResult = "PASS";
                AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试完成：PASS");
            }
            catch (OperationCanceledException)
            {
                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                LastTestResult = "已停止";
                AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试已停止");

                // 停止时清理缓存
                try { await _arinc.ClearRxFifoAsync(FixedRxChannel); } catch { }
                try { await _arinc.ClearRxFifoAsync(FixedTxChannel); } catch { }
            }
            catch (Exception ex)
            {
                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                LastTestResult = "FAIL";
                AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试异常：{ex.Message}");
            }
            finally
            {
                // 彻底清理并停止
                try { await _arinc.ClearRxFifoAsync(FixedRxChannel); } catch { }
                try { await _arinc.ClearRxFifoAsync(FixedTxChannel); } catch { }
                try { await _arinc.StopAsync(msg => AddLog(msg)); } catch { }
                await Task.Delay(200);
                
                IsAutoTestRunning = false;
                lock (_testLock)
                {
                    _isTestBusy = false;
                }
                _opLock.Release();
            }
        }

        private async Task TestGndPhaseAsync(CancellationToken token)
        {
            AddLog($"[{DateTime.Now:HH:mm:ss}] 阶段1：输出GND信号");

            // 多次清理确保无残留
            for (int i = 0; i < 2; i++)
            {
                try { await _arinc.ClearRxFifoAsync(FixedRxChannel); } catch { }
                await Task.Delay(30, token);
            }

            AddLog($"[{DateTime.Now:HH:mm:ss}] 发送S_GNDOC_DISOUT01：{FormatBytesHex(S_GNDOC_DISOUT01)}");
            await _arinc.SendBenchCommandOnlyAsync(FixedTxChannel, S_GNDOC_DISOUT01, msg => AddLog(msg), token);

            var ackResp = await _arinc.WaitBenchResponse8Async(
                FixedRxChannel,
                b => b != null && b.SequenceEqual(S_GNDOC_DISOUT01_ACK),
                timeoutMs: 2000,
                msg => AddLog(msg),
                token);

            if (ackResp == null)
                throw new TimeoutException("S_GNDOC_DISOUT01 ACK超时");

            GndTestAckRxDataText = "0x" + FormatBytesHex(ackResp);
            AddLog($"[{DateTime.Now:HH:mm:ss}] 收到ACK：{GndTestAckRxDataText}");

            try { await _arinc.ClearRxFifoAsync(FixedRxChannel); } catch { }
            await Task.Delay(20, token);

            AddLog($"[{DateTime.Now:HH:mm:ss}] 发送S_GNDOC_DISOUT01_FB：{FormatBytesHex(S_GNDOC_DISOUT01_FB)}");
            await _arinc.SendBenchCommandOnlyAsync(FixedTxChannel, S_GNDOC_DISOUT01_FB, msg => AddLog(msg), token);

            var loopResp = await _arinc.WaitBenchResponse8Async(
                FixedRxChannel,
                b => b != null && b.Length == 8 && b[0] == 0x17 && b[1] == 0x01 && b[2] == 0x01 && b[3] == 0x04,
                timeoutMs: 2000,
                msg => AddLog(msg),
                token);

            if (loopResp == null)
                throw new TimeoutException("GND回采超时");

            GndLoopbackRxDataText = "0x" + FormatBytesHex(loopResp);
            AddLog($"[{DateTime.Now:HH:mm:ss}] 收到GND回采：{GndLoopbackRxDataText}");

            // label14 data: 小端序，loopResp[7]是高字节，loopResp[6]是低字节
            ushort data14 = (ushort)((loopResp[7] << 8) | loopResp[6]);
            string binaryStr = Convert.ToString(data14, 2).PadLeft(16, '0');
            GndLabel14ActualText = $"{binaryStr}";
            AddLog($"[{DateTime.Now:HH:mm:ss}] GND label14实际值：{binaryStr} (0x{data14:X4})");

            // 期望值：0x5555 = 0101010101010101 (GND信号)
            if (data14 != 0x5555)
                throw new InvalidOperationException($"GND回采数据不符：期望0x5555，实际0x{data14:X4}");

            AddLog($"[{DateTime.Now:HH:mm:ss}] GND回采判读：离散输入接收GND信号 -> PASS");

            _ = await TryWaitCurrentUploadAsync(token);
        }

        private async Task TestOcPhaseAsync(CancellationToken token)
        {
            AddLog($"[{DateTime.Now:HH:mm:ss}] 阶段2：输出OC信号");

            // 多次清理确保无残留
            for (int i = 0; i < 2; i++)
            {
                try { await _arinc.ClearRxFifoAsync(FixedRxChannel); } catch { }
                await Task.Delay(30, token);
            }

            AddLog($"[{DateTime.Now:HH:mm:ss}] 发送S_GNDOC_DISOUT02：{FormatBytesHex(S_GNDOC_DISOUT02)}");
            await _arinc.SendBenchCommandOnlyAsync(FixedTxChannel, S_GNDOC_DISOUT02, msg => AddLog(msg), token);

            var loopResp = await _arinc.WaitBenchResponse8Async(
                FixedRxChannel,
                b => b != null && b.Length == 8 && b[0] == 0x17 && b[1] == 0x01 && b[2] == 0x01 && b[3] == 0x07,
                timeoutMs: 2000,
                msg => AddLog(msg),
                token);

            if (loopResp == null)
                throw new TimeoutException("OC回采超时");

            OcLoopbackRxDataText = "0x" + FormatBytesHex(loopResp);
            AddLog($"[{DateTime.Now:HH:mm:ss}] 收到OC回采：{OcLoopbackRxDataText}");

            // label14 data: 小端序，loopResp[7]是高字节，loopResp[6]是低字节
            ushort data14 = (ushort)((loopResp[7] << 8) | loopResp[6]);
            string binaryStr = Convert.ToString(data14, 2).PadLeft(16, '0');
            OcLabel14ActualText = $"{binaryStr}";
            AddLog($"[{DateTime.Now:HH:mm:ss}] OC label14实际值：{binaryStr} (0x{data14:X4})");

            // 期望值：0x0000 = 0000000000000000 (OC信号)
            if (data14 != 0x0000)
                throw new InvalidOperationException($"OC回采数据不符：期望0x0000，实际0x{data14:X4}");

            AddLog($"[{DateTime.Now:HH:mm:ss}] OC回采判读：离散输入接收OC信号 -> PASS");
        }

        private async Task<byte[]> TryWaitCurrentUploadAsync(CancellationToken token)
        {
            try
            {
                var resp = await _arinc.WaitBenchResponse8Async(
                    FixedRxChannel,
                    b => b != null && b.Length == 8 && b[0] == S_GNDOC_DISOUT01_CURRENT_PREFIX[0] && b[1] == S_GNDOC_DISOUT01_CURRENT_PREFIX[1] && b[2] == S_GNDOC_DISOUT01_CURRENT_PREFIX[2] && b[3] == S_GNDOC_DISOUT01_CURRENT_PREFIX[3],
                    timeoutMs: 500,
                    msg => { },
                    token);

                if (resp != null)
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 收到电流回采(忽略)：0x{FormatBytesHex(resp)}");

                return resp;
            }
            catch
            {
                return null;
            }
        }

        private async Task<bool> SendEnterAtpAndWaitAsync(CancellationToken token)
        {
            EnterAtpRxDataText = "--";

            try { await _arinc.ClearRxFifoAsync(FixedRxChannel); } catch { }
            await Task.Delay(20, token);

            AddLog($"[{DateTime.Now:HH:mm:ss}] 发送进入ATP：{FormatBytesHex(AirSafetyAtpR)}");
            await _arinc.SendBenchCommandOnlyAsync(FixedTxChannel, AirSafetyAtpR, msg => AddLog(msg), token);

            var resp = await _arinc.WaitBenchResponse8Async(
                FixedRxChannel,
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

        private async Task SendExitAtpAndWaitAsync(CancellationToken token)
        {
            ExitAtpRxDataText = "--";

            try { await _arinc.ClearRxFifoAsync(FixedRxChannel); } catch { }
            await Task.Delay(20, token);

            AddLog($"[{DateTime.Now:HH:mm:ss}] 发送退出ATP：{FormatBytesHex(AtpE)}");
            await _arinc.SendBenchCommandOnlyAsync(FixedTxChannel, AtpE, msg => AddLog(msg), token);

            var resp = await _arinc.WaitBenchResponse8Async(
                FixedRxChannel,
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

        private static string FormatBytesHex(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                return string.Empty;
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
