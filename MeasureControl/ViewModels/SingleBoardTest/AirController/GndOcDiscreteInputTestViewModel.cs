using Prism.Commands;
using Prism.Mvvm;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MeasureControl.Helpers;
using MeasureControl.Simulations.AC_6_4;
using SimGndOcState = MeasureControl.Simulations.AC_6_4.GndOcState;

namespace MeasureControl.ViewModels.SingleBoardTest.AirController
{
    public class GndOcDiscreteInputTestViewModel : BindableBase
    {
        private const byte DefaultLabel = 0x6A;

        private static readonly byte[] EnterAtpCommand = { 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] EnterAtpOk = { 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x02 };
        private static readonly byte[] ExitAtpCommand = { 0x00, 0x02, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01 };
        private static readonly byte[] ExitAtpOk = { 0x00, 0x02, 0x00, 0x01, 0x00, 0x00, 0x00, 0x03 };

        private static readonly int[] DsiChannels =
        {
            0, 1, 2, 3, 5, 6, 7, 8, 9, 12,
            13, 14, 15, 16, 17, 18, 19, 20, 21, 22,
            23, 24, 25, 26, 27, 28, 29, 35, 36
        };

        private readonly SemaphoreSlim _manualTestLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _opLock = new SemaphoreSlim(1, 1);

        private CancellationTokenSource _cts;
        private readonly AC_6_4Simulation _simulation = new AC_6_4Simulation();

        private string _title = "控制通道GND/OC离散输入通道输入测试";
        private bool _isManualTestRunning;
        private string _lastTestTime = "--";
        private string _lastTestResult = "--";

        private string _enterAtpTxChannel = "429_CH0";
        private string _enterAtpRxChannel = "429_CH1";

        private string _testTxChannel = "429_CH2";
        private string _testRxChannel = "429_CH3";

        private string _exitAtpTxChannel = "429_CH8";
        private string _exitAtpRxChannel = "429_CH9";

        private string _enterAtpRxDataText = "--";
        private string _testRxDataText = "--";
        private string _exitAtpRxDataText = "--";

        // 工装继电器配置（后端常量）
        private const string RelayComPort = "COM27";      // 485继电器板串口
        private const int RelayIndex = 0;                   // 继电器通道号
        private const int RelaySettleDelayMs = 120;         // 切换后稳定延时

        public GndOcDiscreteInputTestViewModel()
        {
            ManualTestCommand = new DelegateCommand(OnManualTest);
            ClearLogCommand = new DelegateCommand(() => Logs.Clear());

            SendEnterAtpCommand = new DelegateCommand(async () => await SendEnterAtpAsync());
            RunGroundOneKeyTestCommand = new DelegateCommand(async () => await RunOneKeyTestAsync(SimGndOcState.Gnd));
            RunOpenOneKeyTestCommand = new DelegateCommand(async () => await RunOneKeyTestAsync(SimGndOcState.Oc));
            SendExitAtpCommand = new DelegateCommand(async () => await SendExitAtpAsync());
        }

        private async Task SetExternalRelayAsync(int index, bool on, CancellationToken token)
        {
            var ports = SerialPort.GetPortNames() ?? Array.Empty<string>();
            if (!ports.Any(p => string.Equals(p, RelayComPort, StringComparison.OrdinalIgnoreCase)))
            {
                throw new IOException($"继电器串口不存在: {RelayComPort}。当前可用串口: {(ports.Length == 0 ? "(无)" : string.Join(", ", ports))}");
            }

            using (await SerialPortMutex.AcquireAsync(RelayComPort).ConfigureAwait(false))
            {
                token.ThrowIfCancellationRequested();
                await Task.Run(() =>
                {
                    using var cli = new RelayModbusClient(RelayComPort, slave: 1, baud: 9600);
                    cli.WriteSingleCoil((ushort)index, on);
                }, token).ConfigureAwait(false);
            }
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
        public DelegateCommand RunGroundOneKeyTestCommand { get; }
        public DelegateCommand RunOpenOneKeyTestCommand { get; }
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
            set => SetProperty(ref _enterAtpTxChannel, value);
        }

        public string EnterAtpRxChannel
        {
            get => _enterAtpRxChannel;
            set => SetProperty(ref _enterAtpRxChannel, value);
        }

        public string TestTxChannel
        {
            get => _testTxChannel;
            set => SetProperty(ref _testTxChannel, value);
        }

        public string TestRxChannel
        {
            get => _testRxChannel;
            set => SetProperty(ref _testRxChannel, value);
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

        public string EnterAtpRxDataText
        {
            get => _enterAtpRxDataText;
            private set => SetProperty(ref _enterAtpRxDataText, value);
        }

        public string TestRxDataText
        {
            get => _testRxDataText;
            private set => SetProperty(ref _testRxDataText, value);
        }

        public string ExitAtpRxDataText
        {
            get => _exitAtpRxDataText;
            private set => SetProperty(ref _exitAtpRxDataText, value);
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
                ExitAtpRxDataText = "--";

                _cts?.Cancel();
                _cts?.Dispose();
                _cts = new CancellationTokenSource();

                AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试启动(仿真模式)：开始打开设备");

                _simulation.SimProductRxChannelIndex = 4;
                _simulation.SimProductTxChannelIndex = 5;
                _simulation.ArincRate = 100000.0;
                await _simulation.StartAsync(EnterAtpTxChannel, EnterAtpRxChannel, msg => AddLog(msg));
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

                IsManualTestRunning = false;
                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            }
            finally
            {
                _manualTestLock.Release();
            }
        }

        private async Task ApplyGndOcSwitchAsync(SimGndOcState state, CancellationToken token)
        {
            try
            {
                // 外置485继电器板切换（接地=ON，接开=OFF）
                bool on = state == SimGndOcState.Gnd;
                await SetExternalRelayAsync(RelayIndex, on, token);

                var delay = Math.Max(0, RelaySettleDelayMs);
                if (delay > 0)
                    await Task.Delay(delay, token);
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 继电器切换{(state == SimGndOcState.Gnd ? "接地" : "接开")}失败：{ex.Message}");
            }
        }

        private async Task<bool> SendEnterAtpAsync()
        {
            await _opLock.WaitAsync();
            try
            {
                var token = _cts?.Token ?? CancellationToken.None;

                EnterAtpRxDataText = "--";
                AddLog($"[{DateTime.Now:HH:mm:ss}] 发送：进入ATP，TX={EnterAtpTxChannel}, RX={EnterAtpRxChannel}");

                try { await _simulation.ClearRxFifoAsync(EnterAtpRxChannel); } catch { }
                await Task.Delay(50, token);

                var resp = await _simulation.SendBenchCommandAndWaitAsync(
                    EnterAtpTxChannel,
                    EnterAtpRxChannel,
                    DefaultLabel,
                    EnterAtpCommand,
                    b => b != null && b.SequenceEqual(EnterAtpOk),
                    timeoutMs: 3000,
                    msg => AddLog(msg),
                    token);

                if (resp == null)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 进入ATP超时，未收到OK");
                    return false;
                }

                EnterAtpRxDataText = "0x" + FormatBytes(resp);
                AddLog($"[{DateTime.Now:HH:mm:ss}] 收到ATP OK");
                return true;
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 进入ATP异常：{ex.Message}");
                return false;
            }
            finally
            {
                _opLock.Release();
            }
        }

        private async Task<bool> SendExitAtpAsync()
        {
            await _opLock.WaitAsync();
            try
            {
                var token = _cts?.Token ?? CancellationToken.None;

                ExitAtpRxDataText = "--";
                AddLog($"[{DateTime.Now:HH:mm:ss}] 发送：退出ATP，TX={ExitAtpTxChannel}, RX={ExitAtpRxChannel}");

                try { await _simulation.ClearRxFifoAsync(ExitAtpRxChannel); } catch { }
                await Task.Delay(50, token);

                var resp = await _simulation.SendBenchCommandAndWaitAsync(
                    ExitAtpTxChannel,
                    ExitAtpRxChannel,
                    DefaultLabel,
                    ExitAtpCommand,
                    b => b != null && b.SequenceEqual(ExitAtpOk),
                    timeoutMs: 2000,
                    msg => AddLog(msg),
                    token);

                if (resp == null)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 退出ATP超时，未收到OK");
                    return false;
                }

                ExitAtpRxDataText = "0x" + FormatBytes(resp);
                AddLog($"[{DateTime.Now:HH:mm:ss}] 收到退出ATP OK");
                return true;
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 退出ATP异常：{ex.Message}");
                return false;
            }
            finally
            {
                _opLock.Release();
            }
        }

        private async Task RunOneKeyTestAsync(SimGndOcState inputMode)
        {
            await _opLock.WaitAsync();
            try
            {
                var token = _cts?.Token ?? CancellationToken.None;

                TestRxDataText = "--";

                _simulation.DsiSimInputMode = inputMode;

                var failures = new List<string>();

                AddLog($"[{DateTime.Now:HH:mm:ss}] 开始离散输入一键测试：输入状态={(inputMode == SimGndOcState.Gnd ? "接地" : "接开")}");
                await ApplyGndOcSwitchAsync(inputMode, token);

                try { await _simulation.ClearRxFifoAsync(TestRxChannel); } catch { }
                await Task.Delay(30, token);

                foreach (var (ch, seq) in EnumerateDsiChannels())
                {
                    token.ThrowIfCancellationRequested();
                    var expected = GetExpectedStateText(inputMode, ch);

                    var (ok, actual, respText) = await TestSingleChannelAsync(ch, seq, expected, token);
                    if (!ok)
                    {
                        failures.Add($"DSI_GND_{ch}: 超时");
                        AddLog($"[{DateTime.Now:HH:mm:ss}] DSI_GND_{ch}: expected={expected}, actual=--, resp=-- -> FAIL(超时)");
                        continue;
                    }

                    bool pass = string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);
                    if (!pass)
                        failures.Add($"DSI_GND_{ch}: expected={expected}, actual={actual}");

                    AddLog($"[{DateTime.Now:HH:mm:ss}] DSI_GND_{ch}: expected={expected}, actual={actual}, resp={respText} -> {(pass ? "PASS" : "FAIL")}");
                }

                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                LastTestResult = failures.Count == 0 ? "PASS" : "FAIL";

                AddLog($"[{DateTime.Now:HH:mm:ss}] 一键测试完成：{LastTestResult}");
                foreach (var f in failures)
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 不合格：{f}");
            }
            catch (OperationCanceledException)
            {
                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                LastTestResult = "已停止";
                AddLog($"[{DateTime.Now:HH:mm:ss}] 一键测试已停止");
            }
            catch (Exception ex)
            {
                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                LastTestResult = "异常";
                AddLog($"[{DateTime.Now:HH:mm:ss}] 一键测试异常：{ex.Message}");
            }
            finally
            {
                _simulation.DsiSimInputMode = SimGndOcState.Oc;
                _opLock.Release();
            }
        }

        private static string GetExpectedStateText(SimGndOcState inputMode, int channel)
        {
            if (inputMode == SimGndOcState.Gnd)
            {
                return channel == 28 ? "OC" : "GND";
            }

            return channel == 29 ? "GND" : "OC";
        }

        private IEnumerable<(int ChannelNumber, byte SequenceId)> EnumerateDsiChannels()
        {
            for (int i = 0; i < DsiChannels.Length; i++)
            {
                yield return (DsiChannels[i], (byte)(i + 1));
            }
        }

        private async Task<(bool Ok, string StateText, string RespText)> TestSingleChannelAsync(int channelNumber, byte sequenceId, string expectedStateText, CancellationToken token)
        {
            var cmd = new byte[8] { 0x08, 0x01, sequenceId, 0x01, 0x00, 0x00, 0x00, 0x00 };

            try { await _simulation.ClearRxFifoAsync(TestRxChannel); } catch { }
            await Task.Delay(10, token);

            bool readyOk;
            try
            {
                readyOk = await _simulation.EnsureBenchChannelsAsync(TestTxChannel, TestRxChannel, _ => { });
            }
            catch
            {
                readyOk = false;
            }

            if (!readyOk)
                return (false, "--", "--");

            await _simulation.SendBenchCommandOnlyAsync(TestTxChannel, DefaultLabel, cmd, _ => { }, token);

            byte[] lastResp = null;
            string lastState = "--";
            string lastRespText = "--";

            DateTime? matchStartUtc = null;
            var deadline = DateTime.UtcNow.AddMilliseconds(1200);

            while (!token.IsCancellationRequested && DateTime.UtcNow <= deadline)
            {
                int sliceMs = (int)Math.Min(200, Math.Max(1, (deadline - DateTime.UtcNow).TotalMilliseconds));

                var resp = await _simulation.WaitBenchResponseAsync(
                    TestRxChannel,
                    DefaultLabel,
                    b => b != null
                         && b.Length == 8
                         && b[0] == 0x08
                         && b[1] == 0x01
                         && (b[2] == sequenceId || b[2] == unchecked((byte)channelNumber))
                         && (b[3] == 0x02 || b[3] == 0x01),
                    timeoutMs: sliceMs,
                    log: _ => { },
                    token);

                if (resp == null)
                {
                    if (matchStartUtc.HasValue && (DateTime.UtcNow - matchStartUtc.Value).TotalMilliseconds >= 200)
                        break;
                    continue;
                }

                lastResp = resp;
                lastState = ParseStateText(resp);
                lastRespText = "0x" + FormatBytes(resp);
                TestRxDataText = lastRespText;

                if (string.Equals(lastState, expectedStateText, StringComparison.OrdinalIgnoreCase))
                {
                    matchStartUtc ??= DateTime.UtcNow;
                }
                else
                {
                    matchStartUtc = null;
                }
            }

            if (lastResp == null)
            {
                TestRxDataText = "--";
                return (false, "--", "--");
            }

            return (true, lastState, lastRespText);
        }

        private static string ParseStateText(byte[] resp)
        {
            if (resp == null || resp.Length != 8)
                return "--";

            // 真实控制器：0x08 0x01 <id> 0x02 <value32>
            // 这里按 value32==0 => GND, 非0 => OC
            if (resp[3] == 0x02)
            {
                uint v = ((uint)resp[4] << 24) | ((uint)resp[5] << 16) | ((uint)resp[6] << 8) | resp[7];
                return v == 0 ? "GND" : "OC";
            }

            // 兼容旧仿真：末字节 0x00=GND, 0x01=OC
            return resp[7] == 0x00 ? "GND" : "OC";
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
