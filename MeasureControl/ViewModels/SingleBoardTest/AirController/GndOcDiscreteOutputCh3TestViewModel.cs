using Prism.Commands;
using Prism.Mvvm;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MeasureControl.Simulations.AC_6_4;

namespace MeasureControl.ViewModels.SingleBoardTest.AirController
{
    public class GndOcDiscreteOutputCh3TestViewModel : BindableBase
    {
        private const byte DefaultLabel = 0x6A;

        private static readonly byte[] EnterAtpCommand = { 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] EnterAtpOk = { 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x02 };
        private static readonly byte[] ExitAtpCommand = { 0x00, 0x02, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01 };
        private static readonly byte[] ExitAtpOk = { 0x00, 0x02, 0x00, 0x01, 0x00, 0x00, 0x00, 0x03 };

        private static readonly byte[] A_GNDDSO1_GNDTEST = { 0x09, 0x01, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] A_GNDDSO1_GNDTEST_ACK = { 0x09, 0x01, 0x01, 0x02, 0xAA, 0xAA, 0xAA, 0xAA };
        private static readonly byte[] A_GNDDSO1_GNDTEST2 = { 0x09, 0x01, 0x01, 0x03, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] A_GNDDSO1_GND_LOOPBACK_UPLOAD = { 0x09, 0x01, 0x01, 0x04, 0x00, 0x00, 0x00, 0x01 };
        private static readonly byte[] A_GNDDSO1_CURRENT_UPLOAD_PREFIX = { 0x09, 0x01, 0x01, 0x05 };
        private static readonly byte[] A_GNDDSO1_OCTEST = { 0x09, 0x01, 0x01, 0x06, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] A_GNDDSO1_OCTEST_ACK = { 0x09, 0x01, 0x01, 0x07, 0xAA, 0xAA, 0xAA, 0xAA };
        private static readonly byte[] A_GNDDSO1_OCTEST2 = { 0x09, 0x01, 0x01, 0x08, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] A_GNDDSO1_OC_LOOPBACK_UPLOAD = { 0x09, 0x01, 0x01, 0x09, 0x00, 0x00, 0x00, 0x00 };

        private readonly SemaphoreSlim _manualTestLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _opLock = new SemaphoreSlim(1, 1);

        private CancellationTokenSource _cts;
        private CancellationTokenSource _autoCts;
        private readonly AC_6_4Simulation _simulation = new AC_6_4Simulation();

        private bool _isManualTestRunning;
        private bool _isAutoTestRunning;
        private string _lastTestTime = "--";
        private string _lastTestResult = "--";

        private string _enterAtpRxDataText = "--";
        private string _gndTestAckRxDataText = "--";
        private string _gndLoopbackRxDataText = "--";
        private string _ocTestAckRxDataText = "--";
        private string _ocLoopbackRxDataText = "--";
        private string _exitAtpRxDataText = "--";

        private string _enterAtpTxChannelDisplay = "CH0";
        private string _enterAtpRxChannelDisplay = "CH1";
        private string _testTxChannelDisplay = "CH2";
        private string _testRxChannelDisplay = "CH3";
        private string _exitAtpTxChannelDisplay = "CH8";
        private string _exitAtpRxChannelDisplay = "CH9";

        public GndOcDiscreteOutputCh3TestViewModel()
        {
            ManualTestCommand = new DelegateCommand(OnManualTest);
            AutoTestCommand = new DelegateCommand(OnAutoTest);
            ClearLogCommand = new DelegateCommand(() => Logs.Clear());

            SendEnterAtpCommand = new DelegateCommand(() => _ = SendEnterAtpAsync());
            SendGndTestCommand = new DelegateCommand(() => _ = SendGndTestAsync());
            SendGndTest2Command = new DelegateCommand(() => _ = SendGndTest2Async());
            SendOcTestCommand = new DelegateCommand(() => _ = SendOcTestAsync());
            SendOcTest2Command = new DelegateCommand(() => _ = SendOcTest2Async());
            SendExitAtpCommand = new DelegateCommand(() => _ = SendExitAtpAsync());
            ClearContentCommand = new DelegateCommand(ClearContent);
        }

        public string PageTitle => "6.15.1.1GND/OC型离散输出通道3输出测试";

        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();

        public DelegateCommand ManualTestCommand { get; }
        public DelegateCommand AutoTestCommand { get; }
        public DelegateCommand ClearLogCommand { get; }

        public DelegateCommand SendEnterAtpCommand { get; }
        public DelegateCommand SendGndTestCommand { get; }
        public DelegateCommand SendGndTest2Command { get; }
        public DelegateCommand SendOcTestCommand { get; }
        public DelegateCommand SendOcTest2Command { get; }
        public DelegateCommand SendExitAtpCommand { get; }
        public DelegateCommand ClearContentCommand { get; }

        public string EnterAtpTxChannelDisplay
        {
            get => _enterAtpTxChannelDisplay;
            set => SetProperty(ref _enterAtpTxChannelDisplay, value);
        }

        public string EnterAtpRxChannelDisplay
        {
            get => _enterAtpRxChannelDisplay;
            set => SetProperty(ref _enterAtpRxChannelDisplay, value);
        }

        public string TestTxChannelDisplay
        {
            get => _testTxChannelDisplay;
            set => SetProperty(ref _testTxChannelDisplay, value);
        }

        public string TestRxChannelDisplay
        {
            get => _testRxChannelDisplay;
            set => SetProperty(ref _testRxChannelDisplay, value);
        }

        public string ExitAtpTxChannelDisplay
        {
            get => _exitAtpTxChannelDisplay;
            set => SetProperty(ref _exitAtpTxChannelDisplay, value);
        }

        public string ExitAtpRxChannelDisplay
        {
            get => _exitAtpRxChannelDisplay;
            set => SetProperty(ref _exitAtpRxChannelDisplay, value);
        }

        private static string ToSimChannel(string display) => display?.Replace("CH", "429_CH") ?? "429_CH0";

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

        public string OcTestAckRxDataText
        {
            get => _ocTestAckRxDataText;
            set => SetProperty(ref _ocTestAckRxDataText, value);
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
            Logs.Clear();
            EnterAtpRxDataText = "--";
            GndTestAckRxDataText = "--";
            GndLoopbackRxDataText = "--";
            OcTestAckRxDataText = "--";
            OcLoopbackRxDataText = "--";
            ExitAtpRxDataText = "--";
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
                GndTestAckRxDataText = "--";
                GndLoopbackRxDataText = "--";
                OcTestAckRxDataText = "--";
                OcLoopbackRxDataText = "--";
                ExitAtpRxDataText = "--";

                _cts?.Cancel();
                _cts?.Dispose();
                _cts = new CancellationTokenSource();

                AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试启动：打开ARINC429 (EnterATP TX={ToSimChannel(EnterAtpTxChannelDisplay)}, RX={ToSimChannel(EnterAtpRxChannelDisplay)})");

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
                await _simulation.StartAsync(ToSimChannel(EnterAtpTxChannelDisplay), ToSimChannel(EnterAtpRxChannelDisplay), msg => AddLog(msg));
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

                try { _autoCts?.Cancel(); } catch { }
                try { _cts?.Cancel(); } catch { }

                await _simulation.StopAsync(msg => AddLog(msg));

                IsManualTestRunning = false;
                IsAutoTestRunning = false;
                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
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
                try { _autoCts?.Cancel(); } catch { }
                return;
            }

            _ = RunAutoTestAsync();
        }

        private async Task RunAutoTestAsync()
        {
            await _opLock.WaitAsync();
            try
            {
                if (!IsManualTestRunning)
                {
                    await StartAsync();
                }

                if (!IsManualTestRunning)
                    throw new InvalidOperationException("ARINC429未启动");

                IsAutoTestRunning = true;
                LastTestTime = "--";
                LastTestResult = "--";

                _autoCts?.Cancel();
                _autoCts?.Dispose();
                _autoCts = new CancellationTokenSource();
                var token = _autoCts.Token;

                try
                {
                    var api = Prism.Ioc.ContainerLocator.Container.Resolve(typeof(MeasureControl.Services.HardwareApis.IComponentPowerStateApi)) as MeasureControl.Services.HardwareApis.IComponentPowerStateApi;
                    if (api != null)
                        await api.ApplyComponent28VStateAsync(token);
                }
                catch { }

                AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试开始");

                bool enterOk = await SendAndExpectAsync(ToSimChannel(EnterAtpTxChannelDisplay), ToSimChannel(EnterAtpRxChannelDisplay), EnterAtpCommand, b => b.SequenceEqual(EnterAtpOk), 3000, token, "进入ATP");
                if (!enterOk)
                    throw new TimeoutException("进入ATP超时");

                bool readyOk = await _simulation.EnsureBenchChannelsAsync(ToSimChannel(TestTxChannelDisplay), ToSimChannel(TestRxChannelDisplay), msg => { });
                if (!readyOk)
                    throw new InvalidOperationException($"bench通道未就绪：TX={ToSimChannel(TestTxChannelDisplay)}, RX={ToSimChannel(TestRxChannelDisplay)}");

                await TestGndPhaseAsync(token);
                await TestOcPhaseAsync(token);

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
                try
                {
                    await SendAndExpectAsync(ToSimChannel(ExitAtpTxChannelDisplay), ToSimChannel(ExitAtpRxChannelDisplay), ExitAtpCommand, b => b.SequenceEqual(ExitAtpOk), 2000, CancellationToken.None, "退出ATP");
                }
                catch
                {
                }

                IsAutoTestRunning = false;
                _opLock.Release();
            }
        }

        private async Task TestGndPhaseAsync(CancellationToken token)
        {
            AddLog($"[{DateTime.Now:HH:mm:ss}] 阶段1：输出GND");

            bool ackOk = await SendAndExpectAsync(ToSimChannel(TestTxChannelDisplay), ToSimChannel(TestRxChannelDisplay), A_GNDDSO1_GNDTEST, b => b.SequenceEqual(A_GNDDSO1_GNDTEST_ACK), 1500, token, "A_GNDDSO1_GNDTEST");
            if (!ackOk)
                throw new TimeoutException("GNDTEST ACK超时");

            bool loopOk = await SendAndExpectAsync(ToSimChannel(TestTxChannelDisplay), ToSimChannel(TestRxChannelDisplay), A_GNDDSO1_GNDTEST2, b => b.SequenceEqual(A_GNDDSO1_GND_LOOPBACK_UPLOAD), 2000, token, "回绕上传(GND)");
            if (!loopOk)
                throw new TimeoutException("回绕上传(GND)超时");

            _ = await TryWaitOptionalAsync(ToSimChannel(TestRxChannelDisplay), b => b != null && b.Length == 8 && b[0] == A_GNDDSO1_CURRENT_UPLOAD_PREFIX[0] && b[1] == A_GNDDSO1_CURRENT_UPLOAD_PREFIX[1] && b[2] == A_GNDDSO1_CURRENT_UPLOAD_PREFIX[2] && b[3] == A_GNDDSO1_CURRENT_UPLOAD_PREFIX[3], 400, token);
        }

        private async Task TestOcPhaseAsync(CancellationToken token)
        {
            AddLog($"[{DateTime.Now:HH:mm:ss}] 阶段2：输出OC");

            bool ackOk = await SendAndExpectAsync(ToSimChannel(TestTxChannelDisplay), ToSimChannel(TestRxChannelDisplay), A_GNDDSO1_OCTEST, b => b.SequenceEqual(A_GNDDSO1_OCTEST_ACK), 1500, token, "A_GNDDSO1_OCTEST");
            if (!ackOk)
                throw new TimeoutException("OCTEST ACK超时");

            bool loopOk = await SendAndExpectAsync(ToSimChannel(TestTxChannelDisplay), ToSimChannel(TestRxChannelDisplay), A_GNDDSO1_OCTEST2, b => b.SequenceEqual(A_GNDDSO1_OC_LOOPBACK_UPLOAD), 2000, token, "回绕上传(OC)");
            if (!loopOk)
                throw new TimeoutException("回绕上传(OC)超时");
        }

        private async Task<bool> SendAndExpectAsync(
            string txChannel,
            string rxChannel,
            byte[] cmd8,
            Func<byte[], bool> isExpected,
            int timeoutMs,
            CancellationToken token,
            string stepName)
        {
            try { await _simulation.ClearRxFifoAsync(rxChannel); } catch { }
            await Task.Delay(30, token);

            AddLog($"[{DateTime.Now:HH:mm:ss}] 发送：{stepName} TX={txChannel}, RX={rxChannel}, CMD=0x{FormatBytes(cmd8)}");

            var resp = await _simulation.SendBenchCommandAndWaitAsync(
                txChannel,
                rxChannel,
                DefaultLabel,
                cmd8,
                isExpected,
                timeoutMs,
                msg => AddLog(msg),
                token);

            if (resp == null)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 超时：{stepName}");
                return false;
            }

            AddLog($"[{DateTime.Now:HH:mm:ss}] 收到：{stepName} RESP=0x{FormatBytes(resp)}");
            return true;
        }

        private async Task<byte[]> TryWaitOptionalAsync(string rxChannel, Func<byte[], bool> isExpected, int timeoutMs, CancellationToken token)
        {
            try
            {
                var resp = await _simulation.WaitBenchResponseAsync(
                    rxChannel,
                    DefaultLabel,
                    isExpected,
                    timeoutMs,
                    msg => AddLog(msg),
                    token);

                if (resp != null)
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 收到(可选)：0x{FormatBytes(resp)}");

                return resp;
            }
            catch
            {
                return null;
            }
        }

        private async Task SendEnterAtpAsync()
        {
            var resp = await SendStepAndCaptureAsync(
                ToSimChannel(EnterAtpTxChannelDisplay),
                ToSimChannel(EnterAtpRxChannelDisplay),
                EnterAtpCommand,
                b => b != null && b.SequenceEqual(EnterAtpOk),
                3000,
                "进入ATP");

            EnterAtpRxDataText = resp == null ? "--" : "0x" + FormatBytes(resp);
        }

        private async Task SendGndTestAsync()
        {
            var resp = await SendStepAndCaptureAsync(
                ToSimChannel(TestTxChannelDisplay),
                ToSimChannel(TestRxChannelDisplay),
                A_GNDDSO1_GNDTEST,
                b => b != null && b.SequenceEqual(A_GNDDSO1_GNDTEST_ACK),
                1500,
                "A_GNDDSO1_GNDTEST");

            GndTestAckRxDataText = resp == null ? "--" : "0x" + FormatBytes(resp);
        }

        private async Task SendGndTest2Async()
        {
            var resp = await SendStepAndCaptureAsync(
                ToSimChannel(TestTxChannelDisplay),
                ToSimChannel(TestRxChannelDisplay),
                A_GNDDSO1_GNDTEST2,
                b => b != null && b.SequenceEqual(A_GNDDSO1_GND_LOOPBACK_UPLOAD),
                2000,
                "A_GNDDSO1_GNDTEST2");

            GndLoopbackRxDataText = resp == null ? "--" : "0x" + FormatBytes(resp);
        }

        private async Task SendOcTestAsync()
        {
            var resp = await SendStepAndCaptureAsync(
                ToSimChannel(TestTxChannelDisplay),
                ToSimChannel(TestRxChannelDisplay),
                A_GNDDSO1_OCTEST,
                b => b != null && b.SequenceEqual(A_GNDDSO1_OCTEST_ACK),
                1500,
                "A_GNDDSO1_OCTEST");

            OcTestAckRxDataText = resp == null ? "--" : "0x" + FormatBytes(resp);
        }

        private async Task SendOcTest2Async()
        {
            var resp = await SendStepAndCaptureAsync(
                ToSimChannel(TestTxChannelDisplay),
                ToSimChannel(TestRxChannelDisplay),
                A_GNDDSO1_OCTEST2,
                b => b != null && b.SequenceEqual(A_GNDDSO1_OC_LOOPBACK_UPLOAD),
                2000,
                "A_GNDDSO1_OCTEST2");

            OcLoopbackRxDataText = resp == null ? "--" : "0x" + FormatBytes(resp);
        }

        private async Task SendExitAtpAsync()
        {
            var resp = await SendStepAndCaptureAsync(
                ToSimChannel(ExitAtpTxChannelDisplay),
                ToSimChannel(ExitAtpRxChannelDisplay),
                ExitAtpCommand,
                b => b != null && b.SequenceEqual(ExitAtpOk),
                2000,
                "退出ATP");

            ExitAtpRxDataText = resp == null ? "--" : "0x" + FormatBytes(resp);
        }

        private async Task<byte[]> SendStepAndCaptureAsync(
            string txChannel,
            string rxChannel,
            byte[] cmd8,
            Func<byte[], bool> isExpected,
            int timeoutMs,
            string stepName)
        {
            await _opLock.WaitAsync();
            try
            {
                if (!IsManualTestRunning)
                {
                    await StartAsync();
                }

                if (!IsManualTestRunning)
                    throw new InvalidOperationException("ARINC429未启动");

                try { await _simulation.ClearRxFifoAsync(rxChannel); } catch { }
                await Task.Delay(30);

                AddLog($"[{DateTime.Now:HH:mm:ss}] 发送：{stepName} TX={txChannel}, RX={rxChannel}, CMD=0x{FormatBytes(cmd8)}");

                var resp = await _simulation.SendBenchCommandAndWaitAsync(
                    txChannel,
                    rxChannel,
                    DefaultLabel,
                    cmd8,
                    isExpected,
                    timeoutMs,
                    msg => AddLog(msg),
                    CancellationToken.None);

                if (resp == null)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 超时：{stepName}");
                    return null;
                }

                AddLog($"[{DateTime.Now:HH:mm:ss}] 收到：{stepName} RESP=0x{FormatBytes(resp)}");
                return resp;
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 步骤异常({stepName})：{ex.Message}");
                return null;
            }
            finally
            {
                _opLock.Release();
            }
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
