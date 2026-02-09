using Prism.Commands;
using Prism.Mvvm;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MeasureControl.Simulations.AC_6_4;

namespace MeasureControl.ViewModels.SingleBoardTest.AirController
{
    public class CanReceiveTestViewModel : BindableBase
    {
        private static readonly byte[] EnterAtpCommand = { 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] EnterAtpOk = { 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x02 };
        private static readonly byte[] AbA825ReceiveCommand = { 0x05, 0x02, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00 };

        private const int EnterAtpMaxRetries = 3;
        private const int EnterAtpTimeoutMs = 3000;
        private const int TestTimeoutMs = 3000;

        private readonly SemaphoreSlim _manualTestLock = new SemaphoreSlim(1, 1);
        private CancellationTokenSource _cts;
        private readonly AC_6_4Simulation _simulation = new AC_6_4Simulation();

        private string _title = "CAN接收测试";
        private bool _isManualTestRunning;
        private string _lastTestTime = "--";
        private string _lastTestResult = "--";

        private string _enterAtpTxChannel = "429_CH0";
        private string _enterAtpRxChannel = "429_CH1";
        private string _testControllerRxChannel = "429_CH2";
        private string _testBenchRxChannel = "429_CH3";

        private string _enterAtpRxDataText = "--";
        private string _testRxDataText = "--";
        private string _testCollectiveValueText = "--";

        public CanReceiveTestViewModel()
        {
            ManualTestCommand = new DelegateCommand(OnManualTest);
            ClearLogCommand = new DelegateCommand(() => Logs.Clear());
            SendEnterAtpCommand = new DelegateCommand(async () => await SendEnterAtpAsync());
            SendTestCommand = new DelegateCommand(async () => await SendTestCommandAsync());
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

                _cts?.Cancel();
                _cts?.Dispose();
                _cts = new CancellationTokenSource();

                AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试启动(仿真模式)：开始打开设备");

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
                LastTestResult = passed ? "检查通过" : "检查不通过";
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

        private async Task<bool> SendTestCommandAsync()
        {
            try
            {
                var token = _cts?.Token ?? CancellationToken.None;
                AddLog($"[{DateTime.Now:HH:mm:ss}] (2) 发送测试指令：TX={TestControllerRxChannel}, RX={TestBenchRxChannel}, Labels=0x31 0x32 0x33 0x34, Data={FormatBytes(AbA825ReceiveCommand)}");

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
                    AddLog($"[{DateTime.Now:HH:mm:ss}] (3) 接收超时");
                    return false;
                }

                TestRxDataText = $"0x{FormatBytes(resp)}";
                var tail = resp.Skip(4).Take(4).ToArray();
                TestCollectiveValueText = FormatBytes(tail);
                AddLog($"[{DateTime.Now:HH:mm:ss}] (3) 接收信息：{FormatBytes(resp)}，后四字节={FormatBytes(tail)}");

                return tail.Length == 4 && tail.All(b => b == 0x01);
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] AB_A825_RECEIVE异常：{ex.Message}");
                return false;
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
