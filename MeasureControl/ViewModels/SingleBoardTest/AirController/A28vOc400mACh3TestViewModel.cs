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
    public class A28vOc400mACh3TestViewModel : BindableBase
    {
        private const string TxChannel = "429_CH5";
        private const string RxChannel = "429_CH2";

        private static readonly byte[] AtpEnterCommand = { 0x30, 0x01, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] AtpExitCommand = { 0x30, 0x02, 0x02, 0x01, 0x00, 0x00, 0x00, 0x00 };

        private static readonly byte[] A_28VDSOH_28VTEST = { 0x09, 0x04, 0x03, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] A_28VDSOH_28VTEST_ACK = { 0x09, 0x04, 0x03, 0x02, 0xAA, 0xAA, 0xAA, 0xAA };
        private static readonly byte[] A_28VDSOH_28VTEST2 = { 0x09, 0x04, 0x03, 0x03, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] A_28VDSOH_OCTEST = { 0x09, 0x04, 0x03, 0x06, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] A_28VDSOH_OCTEST_ACK = { 0x09, 0x04, 0x03, 0x07, 0xAA, 0xAA, 0xAA, 0xAA };
        private static readonly byte[] A_28VDSOH_OCTEST2 = { 0x09, 0x04, 0x03, 0x08, 0x00, 0x00, 0x00, 0x00 };

        private readonly S_C_8_3_1Simulation _arinc = new S_C_8_3_1Simulation();
        private readonly SemaphoreSlim _opLock = new SemaphoreSlim(1, 1);
        private readonly object _testLock = new object();

        private CancellationTokenSource _autoCts;
        private bool _isTestBusy;
        private bool _isAutoTestRunning;
        private string _lastTestTime = "--";
        private string _lastTestResult = "--";

        private string _v28Label14ActualText = "--";
        private string _ocLabel14ActualText = "--";

        public A28vOc400mACh3TestViewModel()
        {
            AutoTestCommand = new DelegateCommand(OnAutoTest);
            ClearLogCommand = new DelegateCommand(() => Logs.Clear());
        }

        public string PageTitle => "6.15.3.3 28V/OC型400mA离散输出通道3输出测试";

        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();

        public DelegateCommand AutoTestCommand { get; }
        public DelegateCommand ClearLogCommand { get; }

        public string V28Label14ActualText
        {
            get => _v28Label14ActualText;
            set => SetProperty(ref _v28Label14ActualText, value);
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
            V28Label14ActualText = "--";
            OcLabel14ActualText = "--";
        }

        private void OnAutoTest()
        {
            lock (_testLock)
            {
                if (_isTestBusy)
                {
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

        private async Task<byte[]> SendAndReadWithRetryAsync(byte[] sendCmd, Func<byte[], bool> predicate, int timeoutMs, CancellationToken token)
        {
            for (int i = 0; i < 3; i++)
            {
                await Task.Delay(200, token);
                try { await _arinc.ClearRxFifoAsync(RxChannel); } catch { }
                await Task.Delay(50, token);

                await _arinc.SendBenchCommandOnlyAsync(TxChannel, sendCmd, msg => { }, token);
                
                try
                {
                    var resp = await _arinc.WaitBenchResponse8Async(
                        RxChannel,
                        predicate,
                        timeoutMs,
                        msg => { },
                        token);

                    if (resp != null)
                    {
                        return resp;
                    }
                }
                catch (TimeoutException)
                {
                }
                
                AddLog($"[{DateTime.Now:HH:mm:ss}] 响应超时或未匹配，正在重试第 {i + 1}/3 次...");
            }
            throw new TimeoutException("多次重试均未收到预期响应包，请检查硬件连接或时序");
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

                try { await _arinc.StopAsync(msg => { }); } catch { }
                await Task.Delay(100, token);

                _arinc.IsRealProduct = true;
                _arinc.ArincRate = 100000.0;
                await _arinc.StartAsync(TxChannel, RxChannel, msg => AddLog(msg));
                AddLog($"[{DateTime.Now:HH:mm:ss}] ARINC429初始化完成 (TX:{TxChannel}, RX:{RxChannel})");

                for (int i = 0; i < 3; i++)
                {
                    try { await _arinc.ClearRxFifoAsync(RxChannel); } catch { }
                    await Task.Delay(50, token);
                }

                // (1) 429发送指令0x30 01 01 01 00 00 00 00进入ATP模式；
                AddLog($"[{DateTime.Now:HH:mm:ss}] (1) 发送进入ATP：{FormatBytesHex(AtpEnterCommand)}");
                await _arinc.SendBenchCommandOnlyAsync(TxChannel, AtpEnterCommand, msg => AddLog(msg), token);
                await Task.Delay(300, token);
                AddLog($"[{DateTime.Now:HH:mm:ss}] ATP指令已发送");

                // (2) - (5) 28V测试阶段
                await Test28vPhaseAsync(token);

                // (6) - (8) OC测试阶段
                await TestOcPhaseAsync(token);

                // 退出ATP
                AddLog($"[{DateTime.Now:HH:mm:ss}] 发送退出ATP：{FormatBytesHex(AtpExitCommand)}");
                await _arinc.SendBenchCommandOnlyAsync(TxChannel, AtpExitCommand, msg => AddLog(msg), token);
                await Task.Delay(100, token);
                AddLog($"[{DateTime.Now:HH:mm:ss}] 退出ATP完成");

                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                LastTestResult = "PASS";
                AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试完成：PASS");
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
                LastTestResult = "FAIL";
                AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试异常：{ex.Message}");
            }
            finally
            {
                try { await _arinc.ClearRxFifoAsync(RxChannel); } catch { }
                try { await _arinc.ClearRxFifoAsync(TxChannel); } catch { }
                try { await _arinc.StopAsync(msg => AddLog(msg)); } catch { }
                
                IsAutoTestRunning = false;
                lock (_testLock)
                {
                    _isTestBusy = false;
                }
                _opLock.Release();
            }
        }

        private async Task Test28vPhaseAsync(CancellationToken token)
        {
            // (2) 429发送测试指令A_28VDSOH_28VTEST 0x09 04 03 01 00 00 00 00
            AddLog($"[{DateTime.Now:HH:mm:ss}] (2) 发送A_28VDSOH_28VTEST：{FormatBytesHex(A_28VDSOH_28VTEST)}");
            var ackResp = await SendAndReadWithRetryAsync(
                A_28VDSOH_28VTEST,
                b => b != null && b.SequenceEqual(A_28VDSOH_28VTEST_ACK),
                2000,
                token);
            AddLog($"[{DateTime.Now:HH:mm:ss}] (3) 收到28VTEST ACK：0x{FormatBytesHex(ackResp)}");

            // (4) 429发送测试指令A_28VDSOH_28VTEST2 0x09 04 03 03 00 00 00 00；
            AddLog($"[{DateTime.Now:HH:mm:ss}] (4) 发送A_28VDSOH_28VTEST2：{FormatBytesHex(A_28VDSOH_28VTEST2)}");
            var loopResp = await SendAndReadWithRetryAsync(
                A_28VDSOH_28VTEST2,
                b => b != null && b.Length == 8 && b[0] == 0x09 && b[1] == 0x04 && b[2] == 0x03 && b[3] == 0x04,
                2000,
                token);
            AddLog($"[{DateTime.Now:HH:mm:ss}] (5) 收到28V回绕指令：0x{FormatBytesHex(loopResp)}");

            // 判读lable14数据位为55 55
            ushort data14 = (ushort)((loopResp[7] << 8) | loopResp[6]);
            V28Label14ActualText = $"{data14:X4}";
            AddLog($"[{DateTime.Now:HH:mm:ss}] 28V label14实际值：0x{data14:X4}");

            if (data14 != 0x5555)
                throw new InvalidOperationException($"28V回采数据不符：期望0x5555，实际0x{data14:X4}");

            AddLog($"[{DateTime.Now:HH:mm:ss}] 28V回采判读：PASS");
        }

        private async Task TestOcPhaseAsync(CancellationToken token)
        {
            // (6) 429发送测试指令A_28VDSOH_OCTEST 0x09 04 03 06 00 00 00 00
            AddLog($"[{DateTime.Now:HH:mm:ss}] (6) 发送A_28VDSOH_OCTEST：{FormatBytesHex(A_28VDSOH_OCTEST)}");
            var ackResp = await SendAndReadWithRetryAsync(
                A_28VDSOH_OCTEST,
                b => b != null && b.SequenceEqual(A_28VDSOH_OCTEST_ACK),
                2000,
                token);
            AddLog($"[{DateTime.Now:HH:mm:ss}] (6) 收到OCTEST ACK：0x{FormatBytesHex(ackResp)}");

            // (7) 429发送测试指令A_28VDSOH_OCTEST2 0x09 04 03 08 00 00 00 00；
            AddLog($"[{DateTime.Now:HH:mm:ss}] (7) 发送A_28VDSOH_OCTEST2：{FormatBytesHex(A_28VDSOH_OCTEST2)}");
            var loopResp = await SendAndReadWithRetryAsync(
                A_28VDSOH_OCTEST2,
                b => b != null && b.Length == 8 && b[0] == 0x09 && b[1] == 0x04 && b[2] == 0x03 && b[3] == 0x09,
                2000,
                token);
            AddLog($"[{DateTime.Now:HH:mm:ss}] (8) 收到OC回绕指令：0x{FormatBytesHex(loopResp)}");

            // 判读lable14数据为AA AA
            ushort data14 = (ushort)((loopResp[7] << 8) | loopResp[6]);
            OcLabel14ActualText = $"{data14:X4}";
            AddLog($"[{DateTime.Now:HH:mm:ss}] OC label14实际值：0x{data14:X4}");

            if (data14 != 0xAAAA)
                throw new InvalidOperationException($"OC回采数据不符：期望0xAAAA，实际0x{data14:X4}");

            AddLog($"[{DateTime.Now:HH:mm:ss}] OC回采判读：PASS");
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
