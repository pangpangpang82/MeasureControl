using MeasureControl.Simulations.S_C_8_3_1;
using Prism.Commands;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MeasureControl.ViewModels.SingleBoardTest.AirController
{
    public class S_C_8_5_1ViewModel : AirSimpleSequenceViewModel
    {
        private static readonly byte[] AirSafetyAtpR = { 0x30, 0x01, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] AirSafetyAtpEnterOk = { 0x30, 0x01, 0x01, 0x02, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] AtpE = { 0x30, 0x02, 0x02, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] ExitOk = { 0x30, 0x02, 0x02, 0x02, 0x00, 0x00, 0x00, 0x00 };

        private static readonly byte[] SArinc825OutCommand8 = { 0x14, 0x01, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] SArinc825OutExpected8 = { 0x14, 0x01, 0x01, 0x02, 0x01, 0x01, 0x01, 0x01 };

        private readonly S_C_8_3_1Simulation _arinc = new S_C_8_3_1Simulation();
        private readonly SemaphoreSlim _opLock = new SemaphoreSlim(1, 1);

        private CancellationTokenSource _testCts;
        private bool _isAutoTestRunning;
        private string _canRxDataText = "--";
        private string _enterAtpRxDataTextLocal = "--";
        private string _exitAtpRxDataTextLocal = "--";

        public S_C_8_5_1ViewModel()
        {
            Title = "8.5.1 S安全通道CAN发送测试";
            AutoSequenceCommand = new DelegateCommand(async () => await OnAutoSequenceAsync());
            SendTestCommand = new DelegateCommand(async () => await OnSendSArinc825OutAsync());
            SendEnterAtpCommandOverride = new DelegateCommand(async () => await OnSendEnterAtpOverrideAsync());
            SendExitAtpCommandOverride = new DelegateCommand(async () => await OnSendExitAtpOverrideAsync());
        }

        public new DelegateCommand AutoTestCommand => AutoSequenceCommand;

        public DelegateCommand AutoSequenceCommand { get; }

        public DelegateCommand SendTestCommand { get; }

        public new DelegateCommand SendEnterAtpCommand => SendEnterAtpCommandOverride;
        public DelegateCommand SendEnterAtpCommandOverride { get; }

        public new DelegateCommand SendExitAtpCommand => SendExitAtpCommandOverride;
        public DelegateCommand SendExitAtpCommandOverride { get; }

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

        public new string TestCommandBytesText => "0x14 01 01 01 00 00 00 00";

        public string ExpectedResponseText => "0x14 01 01 02 01 01 01 01";

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
                AddLog($"[{DateTime.Now:HH:mm:ss}] 步骤2：发送S_ARINC825_OUT指令并等待响应");
                var testOk = await SendSArinc825OutAndWaitAsync(token);

                token.ThrowIfCancellationRequested();
                AddLog($"[{DateTime.Now:HH:mm:ss}] 步骤3：退出ATP模式");
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

        private async Task OnSendSArinc825OutAsync()
        {
            await _opLock.WaitAsync();
            try
            {
                CanRxDataText = "--";

                try { await _arinc.ClearRxFifoAsync(EnterAtpRxChannel); } catch { }
                await Task.Delay(20);

                AddLog($"[{DateTime.Now:HH:mm:ss}] 发送S_ARINC825_OUT：{FormatBytesHex(SArinc825OutCommand8)}");
                await _arinc.SendBenchCommandOnlyAsync(EnterAtpTxChannel, SArinc825OutCommand8, msg => AddLog(msg), CancellationToken.None);

                var resp = await _arinc.WaitBenchResponse8Async(
                    EnterAtpRxChannel,
                    b => b != null && b.SequenceEqual(SArinc825OutExpected8),
                    timeoutMs: 2000,
                    msg => AddLog(msg),
                    CancellationToken.None);

                if (resp != null)
                {
                    CanRxDataText = "0x" + FormatBytesHex(resp);
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 收到预期响应：{CanRxDataText}");
                }
                else
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 未收到预期响应");
                }
            }
            finally
            {
                _opLock.Release();
            }
        }

        private async Task<bool> SendSArinc825OutAndWaitAsync(CancellationToken token)
        {
            await _opLock.WaitAsync(token);
            try
            {
                CanRxDataText = "--";

                try { await _arinc.ClearRxFifoAsync(EnterAtpRxChannel); } catch { }
                await Task.Delay(20, token);

                AddLog($"[{DateTime.Now:HH:mm:ss}] 发送S_ARINC825_OUT：{FormatBytesHex(SArinc825OutCommand8)}");
                await _arinc.SendBenchCommandOnlyAsync(EnterAtpTxChannel, SArinc825OutCommand8, msg => AddLog(msg), token);

                var resp = await _arinc.WaitBenchResponse8Async(
                    EnterAtpRxChannel,
                    b => b != null && b.SequenceEqual(SArinc825OutExpected8),
                    timeoutMs: 2000,
                    msg => AddLog(msg),
                    token);

                if (resp != null)
                {
                    CanRxDataText = "0x" + FormatBytesHex(resp);
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 收到预期响应：{CanRxDataText} -> PASS");
                    return true;
                }

                AddLog($"[{DateTime.Now:HH:mm:ss}] 未收到预期响应 0x{FormatBytesHex(SArinc825OutExpected8)} -> FAIL");
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

        private async Task OnSendEnterAtpOverrideAsync()
        {
            _arinc.IsRealProduct = true;
            _arinc.ArincRate = 100000.0;
            await _arinc.StartAsync(EnterAtpTxChannel, EnterAtpRxChannel, msg => AddLog(msg));
            await SendEnterAtpAndWaitAsync(CancellationToken.None);
        }

        private async Task OnSendExitAtpOverrideAsync()
        {
            await SendExitAtpAndWaitAsync(CancellationToken.None);
        }

        private static string FormatBytesHex(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                return string.Empty;
            return string.Join(" ", bytes.Select(b => b.ToString("X2")));
        }
    }
}
