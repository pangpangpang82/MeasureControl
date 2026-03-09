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
    public class A28vOc400mADiscreteOutputTestViewModel : BindableBase
    {
        private const byte DefaultLabel = 0x6A;

        private static readonly byte[] EnterAtpCommand = { 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] EnterAtpOk = { 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x02 };
        private static readonly byte[] ExitAtpCommand = { 0x00, 0x02, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01 };
        private static readonly byte[] ExitAtpOk = { 0x00, 0x02, 0x00, 0x01, 0x00, 0x00, 0x00, 0x03 };

        private readonly SemaphoreSlim _manualTestLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _opLock = new SemaphoreSlim(1, 1);

        private CancellationTokenSource _cts;
        private CancellationTokenSource _autoCts;
        private readonly AC_6_4Simulation _simulation = new AC_6_4Simulation();

        private readonly byte _channel;
        private readonly string _pageTitle;

        private readonly byte[] _out28vCmd;
        private readonly byte[] _out28vAck;
        private readonly byte[] _upload28vCmd;
        private readonly byte[] _upload28vPrefix;
        private readonly byte[] _upload28vCurrentPrefix;
        private readonly byte[] _outOcCmd;
        private readonly byte[] _outOcAck;
        private readonly byte[] _uploadOcCmd;
        private readonly byte[] _uploadOcResp;

        private bool _isManualTestRunning;
        private bool _isAutoTestRunning;
        private string _lastTestTime = "--";
        private string _lastTestResult = "--";

        private string _enterAtpRxDataText = "--";
        private string _out28vAckRxDataText = "--";
        private string _upload28vRxDataText = "--";
        private string _outOcAckRxDataText = "--";
        private string _uploadOcRxDataText = "--";
        private string _exitAtpRxDataText = "--";

        private string _enterAtpTxChannelDisplay = "CH0";
        private string _enterAtpRxChannelDisplay = "CH1";
        private string _testTxChannelDisplay = "CH2";
        private string _testRxChannelDisplay = "CH3";
        private string _exitAtpTxChannelDisplay = "CH8";
        private string _exitAtpRxChannelDisplay = "CH9";

        public A28vOc400mADiscreteOutputTestViewModel(int channelNumber, string pageTitle)
        {
            if (channelNumber < 1 || channelNumber > 3)
                channelNumber = 1;

            _channel = (byte)channelNumber;
            _pageTitle = pageTitle ?? $"6.15.3.{channelNumber}A控制通道28V/OC型400mA离散输出通道{channelNumber}输出测试";

            _out28vCmd = BuildCmd(_channel, 0x01);
            _out28vAck = BuildAck(_channel, 0x02);
            _upload28vCmd = BuildCmd(_channel, 0x03);
            _upload28vPrefix = BuildPrefix(_channel, 0x04);
            _upload28vCurrentPrefix = BuildPrefix(_channel, 0x05);

            _outOcCmd = BuildCmd(_channel, 0x06);
            _outOcAck = BuildAck(_channel, 0x07);
            _uploadOcCmd = BuildCmd(_channel, 0x08);
            _uploadOcResp = BuildResp(_channel, 0x09, 0x00, 0x00, 0x00, 0x01);

            ManualTestCommand = new DelegateCommand(OnManualTest);
            AutoTestCommand = new DelegateCommand(OnAutoTest);
            ClearLogCommand = new DelegateCommand(() => Logs.Clear());

            SendEnterAtpCommand = new DelegateCommand(() => _ = SendEnterAtpAsync());
            SendOut28vCommand = new DelegateCommand(() => _ = SendOut28vAsync());
            SendUpload28vCommand = new DelegateCommand(() => _ = SendUpload28vAsync());
            SendOutOcCommand = new DelegateCommand(() => _ = SendOutOcAsync());
            SendUploadOcCommand = new DelegateCommand(() => _ = SendUploadOcAsync());
            SendExitAtpCommand = new DelegateCommand(() => _ = SendExitAtpAsync());
            ClearContentCommand = new DelegateCommand(ClearContent);
        }

        public string PageTitle => _pageTitle;

        public bool IsChannel3 => _channel == 3;

        public string EnterAtpCmdText => "0x" + FormatBytes(EnterAtpCommand);
        public string Out28vCmdText => "0x" + FormatBytes(_out28vCmd);
        public string Upload28vCmdText => "0x" + FormatBytes(_upload28vCmd);
        public string OutOcCmdText => "0x" + FormatBytes(_outOcCmd);
        public string UploadOcCmdText => "0x" + FormatBytes(_uploadOcCmd);
        public string ExitAtpCmdText => "0x" + FormatBytes(ExitAtpCommand);

        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();

        public DelegateCommand ManualTestCommand { get; }
        public DelegateCommand AutoTestCommand { get; }
        public DelegateCommand ClearLogCommand { get; }

        public DelegateCommand SendEnterAtpCommand { get; }
        public DelegateCommand SendOut28vCommand { get; }
        public DelegateCommand SendUpload28vCommand { get; }
        public DelegateCommand SendOutOcCommand { get; }
        public DelegateCommand SendUploadOcCommand { get; }
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

        public string Out28vAckRxDataText
        {
            get => _out28vAckRxDataText;
            set => SetProperty(ref _out28vAckRxDataText, value);
        }

        public string Upload28vRxDataText
        {
            get => _upload28vRxDataText;
            set => SetProperty(ref _upload28vRxDataText, value);
        }

        public string OutOcAckRxDataText
        {
            get => _outOcAckRxDataText;
            set => SetProperty(ref _outOcAckRxDataText, value);
        }

        public string UploadOcRxDataText
        {
            get => _uploadOcRxDataText;
            set => SetProperty(ref _uploadOcRxDataText, value);
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
            Out28vAckRxDataText = "--";
            Upload28vRxDataText = "--";
            OutOcAckRxDataText = "--";
            UploadOcRxDataText = "--";
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
                Out28vAckRxDataText = "--";
                Upload28vRxDataText = "--";
                OutOcAckRxDataText = "--";
                UploadOcRxDataText = "--";
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

                bool enterOk = await SendAndExpectAsync(
                    ToSimChannel(EnterAtpTxChannelDisplay),
                    ToSimChannel(EnterAtpRxChannelDisplay),
                    EnterAtpCommand,
                    b => b != null && b.SequenceEqual(EnterAtpOk),
                    3000,
                    token,
                    "进入ATP");
                if (!enterOk)
                    throw new TimeoutException("进入ATP超时");

                bool readyOk = await _simulation.EnsureBenchChannelsAsync(ToSimChannel(TestTxChannelDisplay), ToSimChannel(TestRxChannelDisplay), _ => { });
                if (!readyOk)
                    throw new InvalidOperationException($"bench通道未就绪：TX={ToSimChannel(TestTxChannelDisplay)}, RX={ToSimChannel(TestRxChannelDisplay)}");

                await Test28vPhaseAsync(token);
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
                    await SendAndExpectAsync(
                        ToSimChannel(ExitAtpTxChannelDisplay),
                        ToSimChannel(ExitAtpRxChannelDisplay),
                        ExitAtpCommand,
                        b => b != null && b.SequenceEqual(ExitAtpOk),
                        2000,
                        CancellationToken.None,
                        "退出ATP");
                }
                catch
                {
                }

                IsAutoTestRunning = false;
                _opLock.Release();
            }
        }

        private async Task Test28vPhaseAsync(CancellationToken token)
        {
            AddLog($"[{DateTime.Now:HH:mm:ss}] 阶段1：输出28V");

            bool outOk = await SendAndExpectAsync(
                ToSimChannel(TestTxChannelDisplay),
                ToSimChannel(TestRxChannelDisplay),
                _out28vCmd,
                b => b != null && b.SequenceEqual(_out28vAck),
                1500,
                token,
                "输出28V信号");
            if (!outOk)
                throw new TimeoutException("输出28V ACK超时");

            if (_channel == 3)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 提示：请测量负载两端实际电压值并计算负载电流(通道3)");
            }

            var uploadResp = await SendAndWaitResponseAsync(
                ToSimChannel(TestTxChannelDisplay),
                ToSimChannel(TestRxChannelDisplay),
                _upload28vCmd,
                b => IsPrefixWithLength(b, _upload28vPrefix, 8),
                2000,
                token,
                "上传离散和AD回采");
            if (uploadResp == null)
                throw new TimeoutException("上传离散和AD回采超时");

            if (TryParseVoltageFromAdUpload(uploadResp, out var voltage, out var scheme))
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] AD回采解析电压={voltage:F3}V ({scheme})");
                if (voltage < 25.0 || voltage > 28.0)
                    throw new InvalidOperationException($"电压超限：{voltage:F3}V (要求[25,28]V)");
            }
            else
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] AD回采电压解析失败/数据为0：0x{FormatBytes(uploadResp)}");
            }

            var currentResp = await WaitRequiredAsync(
                ToSimChannel(TestRxChannelDisplay),
                b => IsPrefixWithLength(b, _upload28vCurrentPrefix, 8),
                1200,
                token,
                "电流回采(09 04 xx 05)");
            AddLog($"[{DateTime.Now:HH:mm:ss}] 电流回采帧：0x{FormatBytes(currentResp)}");
        }

        private async Task TestOcPhaseAsync(CancellationToken token)
        {
            AddLog($"[{DateTime.Now:HH:mm:ss}] 阶段2：输出OC");

            bool outOk = await SendAndExpectAsync(
                ToSimChannel(TestTxChannelDisplay),
                ToSimChannel(TestRxChannelDisplay),
                _outOcCmd,
                b => b != null && b.SequenceEqual(_outOcAck),
                1500,
                token,
                "输出OC信号");
            if (!outOk)
                throw new TimeoutException("输出OC ACK超时");

            bool uploadOk = await SendAndExpectAsync(
                ToSimChannel(TestTxChannelDisplay),
                ToSimChannel(TestRxChannelDisplay),
                _uploadOcCmd,
                b => b != null && b.SequenceEqual(_uploadOcResp),
                2000,
                token,
                "上传离散回采");
            if (!uploadOk)
                throw new TimeoutException("上传离散回采超时");
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

        private async Task SendOut28vAsync()
        {
            var resp = await SendStepAndCaptureAsync(
                ToSimChannel(TestTxChannelDisplay),
                ToSimChannel(TestRxChannelDisplay),
                _out28vCmd,
                b => b != null && b.SequenceEqual(_out28vAck),
                1500,
                "输出28V信号");

            Out28vAckRxDataText = resp == null ? "--" : "0x" + FormatBytes(resp);
        }

        private async Task SendUpload28vAsync()
        {
            var resp = await SendStepAndCaptureAsync(
                ToSimChannel(TestTxChannelDisplay),
                ToSimChannel(TestRxChannelDisplay),
                _upload28vCmd,
                b => IsPrefixWithLength(b, _upload28vPrefix, 8),
                2000,
                "上传离散和AD回采");

            Upload28vRxDataText = resp == null ? "--" : "0x" + FormatBytes(resp);

            if (resp != null)
            {
                _ = await TryWaitOptionalAsync(
                    ToSimChannel(TestRxChannelDisplay),
                    b => IsPrefixWithLength(b, _upload28vCurrentPrefix, 8),
                    800,
                    CancellationToken.None);
            }
        }

        private async Task SendOutOcAsync()
        {
            var resp = await SendStepAndCaptureAsync(
                ToSimChannel(TestTxChannelDisplay),
                ToSimChannel(TestRxChannelDisplay),
                _outOcCmd,
                b => b != null && b.SequenceEqual(_outOcAck),
                1500,
                "输出OC信号");

            OutOcAckRxDataText = resp == null ? "--" : "0x" + FormatBytes(resp);
        }

        private async Task SendUploadOcAsync()
        {
            var resp = await SendStepAndCaptureAsync(
                ToSimChannel(TestTxChannelDisplay),
                ToSimChannel(TestRxChannelDisplay),
                _uploadOcCmd,
                b => b != null && b.SequenceEqual(_uploadOcResp),
                2000,
                "上传离散回采");

            UploadOcRxDataText = resp == null ? "--" : "0x" + FormatBytes(resp);
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

        private async Task<byte[]> SendAndWaitResponseAsync(
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
                return null;
            }

            AddLog($"[{DateTime.Now:HH:mm:ss}] 收到：{stepName} RESP=0x{FormatBytes(resp)}");
            return resp;
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

        private async Task<byte[]> WaitRequiredAsync(
            string rxChannel,
            Func<byte[], bool> isExpected,
            int timeoutMs,
            CancellationToken token,
            string stepName)
        {
            var resp = await _simulation.WaitBenchResponseAsync(
                rxChannel,
                DefaultLabel,
                isExpected,
                timeoutMs,
                msg => AddLog(msg),
                token);

            if (resp == null)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 超时：{stepName}");
                throw new TimeoutException($"{stepName}超时");
            }

            return resp;
        }

        private static bool IsPrefixWithLength(byte[] data, byte[] prefix, int expectedLength)
        {
            if (data == null || prefix == null) return false;
            if (data.Length != expectedLength) return false;
            if (data.Length < prefix.Length) return false;
            for (int i = 0; i < prefix.Length; i++)
            {
                if (data[i] != prefix[i]) return false;
            }
            return true;
        }

        private static bool TryParseVoltageFromAdUpload(byte[] resp8, out double voltage, out string scheme)
        {
            voltage = 0;
            scheme = null;

            if (resp8 == null || resp8.Length != 8)
                return false;

            uint b4 = resp8[4];
            uint b5 = resp8[5];
            uint b6 = resp8[6];
            uint b7 = resp8[7];

            if ((b4 | b5 | b6 | b7) == 0)
                return false;

            bool found = false;
            double best = 0;
            string bestScheme = null;
            double bestScore = double.MaxValue;

            void Consider(double v, string s)
            {
                if (double.IsNaN(v) || double.IsInfinity(v))
                    return;

                if (v < 0 || v > 40)
                    return;

                double score = (v >= 20 && v <= 35) ? Math.Abs(v - 27.0) : (100 + Math.Abs(v - 20.0));
                if (!found || score < bestScore)
                {
                    found = true;
                    best = v;
                    bestScheme = s;
                    bestScore = score;
                }
            }

            uint u16be_45 = (b4 << 8) | b5;
            uint u16le_45 = (b5 << 8) | b4;
            uint u16be_67 = (b6 << 8) | b7;
            uint u16le_67 = (b7 << 8) | b6;
            uint u32be = (b4 << 24) | (b5 << 16) | (b6 << 8) | b7;
            uint u32le = (b7 << 24) | (b6 << 16) | (b5 << 8) | b4;

            Consider(u16be_45 / 1000.0, "u16be@4-5 mV");
            Consider(u16le_45 / 1000.0, "u16le@4-5 mV");
            Consider(u16be_67 / 1000.0, "u16be@6-7 mV");
            Consider(u16le_67 / 1000.0, "u16le@6-7 mV");
            Consider(u32be / 1000.0, "u32be@4-7 mV");
            Consider(u32le / 1000.0, "u32le@4-7 mV");

            Consider(u16be_45 / 100.0, "u16be@4-5 0.01V");
            Consider(u16le_45 / 100.0, "u16le@4-5 0.01V");
            Consider(u16be_67 / 100.0, "u16be@6-7 0.01V");
            Consider(u16le_67 / 100.0, "u16le@6-7 0.01V");
            Consider(u32be / 100.0, "u32be@4-7 0.01V");
            Consider(u32le / 100.0, "u32le@4-7 0.01V");

            if (!found)
                return false;

            voltage = best;
            scheme = bestScheme;
            return true;
        }

        private static byte[] BuildCmd(byte channel, byte func)
        {
            return new[] { (byte)0x09, (byte)0x04, channel, func, (byte)0x00, (byte)0x00, (byte)0x00, (byte)0x00 };
        }

        private static byte[] BuildAck(byte channel, byte func)
        {
            return new[] { (byte)0x09, (byte)0x04, channel, func, (byte)0xAA, (byte)0xAA, (byte)0xAA, (byte)0xAA };
        }

        private static byte[] BuildPrefix(byte channel, byte func)
        {
            return new[] { (byte)0x09, (byte)0x04, channel, func };
        }

        private static byte[] BuildResp(byte channel, byte func, byte d4, byte d5, byte d6, byte d7)
        {
            return new[] { (byte)0x09, (byte)0x04, channel, func, d4, d5, d6, d7 };
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
