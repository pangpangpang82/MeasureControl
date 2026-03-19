using MeasureControl.Services.HardwareApis;
using MeasureControl.Drivers;
using MeasureControl.Models.Devices;
using Prism.Commands;
using Prism.Mvvm;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using MeasureControl.Services;

namespace MeasureControl.ViewModels.SingleBoardTest.InertController
{
    public sealed class LatchModuleCircuitTestViewModel : BindableBase, IDisposable
    {
        private const string FpgaServerIpAddress = "192.168.1.10";
        private const int FpgaServerPort = 5001;

        //private const string FpgaServerIpAddress = "192.168.1.2";
        //private const int FpgaServerPort = 5011;

        private const string PowerSupplyIpAddress = "192.168.1.15";
        private const double MainPowerCurrentA = 1.0;
        private const double LatchSupplyCurrentA = 0.1;
        private readonly IPxiChassisService _pxiChassisService;

        private IPowerSupplyApi _power;
        private IPxi7012Api _resistor;
        private uint? _connectedResistorLogicalId;

        private TcpClient _fpgaClient;
        private NetworkStream _fpgaStream;

        private readonly SemaphoreSlim _fpgaSendLock = new SemaphoreSlim(1, 1);
        private uint _fpgaGpioWriteMask;

        private uint? _lastFpgaGpioInput;
        private DateTime? _lastFpgaGpioInputTime;

        private readonly SemaphoreSlim _opLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _resistorLock = new SemaphoreSlim(1, 1);

        private CancellationTokenSource _cts;

        private bool _isManualTestRunning;
        private bool _isAutoTestRunning;
        private bool _isBusy;

        private bool _isPowerOn;
        private string _powerStatus = "未供电";

        private string _overallResult = "--";
        private string _lastTestTime = "--";

        private PowerSupplyChannel _latchSupplyChannel = PowerSupplyChannel.CH3;

        public LatchModuleCircuitTestViewModel(IPxiChassisService pxiChassisService)
        {
            _pxiChassisService = pxiChassisService;

            ManualTestCommand = new DelegateCommand(async () => await OnManualTestAsync());
            AutoTestCommand = new DelegateCommand(async () => await OnAutoTestAsync());
            ClearLogCommand = new DelegateCommand(() => Logs.Clear());

            Items.Add(CreatePt500aItem());
            Items.Add(CreatePt1000aItem());
        }

        public ObservableCollection<LatchItemViewModel> Items { get; } = new ObservableCollection<LatchItemViewModel>();

        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();

        public DelegateCommand ManualTestCommand { get; }

        public DelegateCommand AutoTestCommand { get; }

        public DelegateCommand ClearLogCommand { get; }

        public bool IsPowerOn
        {
            get => _isPowerOn;
            private set
            {
                if (SetProperty(ref _isPowerOn, value))
                {
                    RaiseCanExecuteChanged();
                }
            }
        }

        public string PowerStatus
        {
            get => _powerStatus;
            private set => SetProperty(ref _powerStatus, value);
        }

        public bool IsManualTestRunning
        {
            get => _isManualTestRunning;
            private set
            {
                if (SetProperty(ref _isManualTestRunning, value))
                {
                    RaiseCanExecuteChanged();
                }
            }
        }

        public bool IsAutoTestRunning
        {
            get => _isAutoTestRunning;
            private set
            {
                if (SetProperty(ref _isAutoTestRunning, value))
                {
                    RaiseCanExecuteChanged();
                }
            }
        }

        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (SetProperty(ref _isBusy, value))
                {
                    RaiseCanExecuteChanged();
                }
            }
        }

        public string OverallResult
        {
            get => _overallResult;
            private set => SetProperty(ref _overallResult, value);
        }

        public string LastTestTime
        {
            get => _lastTestTime;
            private set => SetProperty(ref _lastTestTime, value);
        }

        public uint? LastFpgaGpioInput
        {
            get => _lastFpgaGpioInput;
            private set => SetProperty(ref _lastFpgaGpioInput, value);
        }

        public PowerSupplyChannel LatchSupplyChannel
        {
            get => _latchSupplyChannel;
            set => SetProperty(ref _latchSupplyChannel, value);
        }

        private LatchItemViewModel CreatePt500aItem()
        {
            var item = new LatchItemViewModel(this,
                title: "PT500A 锁存模块电路测试",
                roChannel: "RO0",
                supplyPin: "J34",
                measurePin: "J31",
                measurePinName: "T1_AWARN");

            item.Steps.Add(new LatchStepViewModel(this, item,
                stepName: "a)",
                actionDescription: "PT500A=730Ω",
                resistanceOhm: 730.0,
                inject3v3: false,
                expected: "高电平(3.3±0.33V)",
                evaluation: LatchEvaluation.High33));

            item.Steps.Add(new LatchStepViewModel(this, item,
                stepName: "b)",
                actionDescription: "PT500A=500Ω",
                resistanceOhm: 500.0,
                inject3v3: false,
                expected: "高电平(3.3±0.33V)",
                evaluation: LatchEvaluation.High33));

            item.Steps.Add(new LatchStepViewModel(this, item,
                stepName: "c)",
                actionDescription: "J34 动作(FPGA GPIO)",
                resistanceOhm: null,
                inject3v3: true,
                expected: "低电平(0±0.1V)",
                evaluation: LatchEvaluation.Low0));

            return item;
        }

        private LatchItemViewModel CreatePt1000aItem()
        {
            var item = new LatchItemViewModel(this,
                title: "PT1000A 锁存模块电路测试",
                roChannel: "RO1",
                supplyPin: "J35",
                measurePin: "J32",
                measurePinName: "T2_AWARN");

            item.Steps.Add(new LatchStepViewModel(this, item,
                stepName: "a)",
                actionDescription: "PT1000A=1500Ω",
                resistanceOhm: 1500.0,
                inject3v3: false,
                expected: "高电平(3.3±0.33V)",
                evaluation: LatchEvaluation.High33));

            item.Steps.Add(new LatchStepViewModel(this, item,
                stepName: "b)",
                actionDescription: "PT1000A=1000Ω",
                resistanceOhm: 1000.0,
                inject3v3: false,
                expected: "高电平(3.3±0.33V)",
                evaluation: LatchEvaluation.High33));

            item.Steps.Add(new LatchStepViewModel(this, item,
                stepName: "c)",
                actionDescription: "J35 动作(FPGA GPIO)",
                resistanceOhm: null,
                inject3v3: true,
                expected: "低电平(0±0.1V)",
                evaluation: LatchEvaluation.Low0));

            return item;
        }

        private async Task OnManualTestAsync()
        {
            if (IsManualTestRunning)
            {
                await StopTestAsync().ConfigureAwait(false);
                return;
            }

            if (IsAutoTestRunning)
            {
                await StopTestAsync().ConfigureAwait(false);
            }

            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();

            ResetResults();
            OverallResult = "--";
            LastTestTime = "--";

            IsManualTestRunning = true;
            IsAutoTestRunning = false;

            Log("开始手动测试");

            try
            {
                ResetFpgaCapture();

                Log($"电源: CH1 28V 1A, IP={PowerSupplyIpAddress}");
                await EnsureMainPowerAsync(28.0, _cts.Token).ConfigureAwait(false);
                SetPowerState(true, "已供电");
            }
            catch (OperationCanceledException)
            {
                await StopTestAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log($"手动测试初始化失败: {ex.Message}");
                await StopTestAsync().ConfigureAwait(false);
            }
        }

        private async Task OnAutoTestAsync()
        {
            if (IsAutoTestRunning)
            {
                await StopTestAsync().ConfigureAwait(false);
                return;
            }

            if (IsManualTestRunning)
            {
                await StopTestAsync().ConfigureAwait(false);
            }

            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();

            ResetResults();
            OverallResult = "--";
            LastTestTime = "--";

            IsAutoTestRunning = true;
            IsManualTestRunning = false;

            Log("开始自动测试");

            try
            {
                ResetFpgaCapture();

                Log($"电源: CH1 28V 1A, IP={PowerSupplyIpAddress}");
                await EnsureMainPowerAsync(28.0, _cts.Token).ConfigureAwait(false);
                SetPowerState(true, "已供电");

                foreach (var item in Items)
                {
                    if (_cts.IsCancellationRequested)
                        return;

                    foreach (var step in item.Steps)
                    {
                        if (_cts.IsCancellationRequested)
                            return;

                        await ExecuteStepAsync(step, _cts.Token).ConfigureAwait(false);
                        await Task.Delay(100, _cts.Token).ConfigureAwait(false);
                    }
                }

                EvaluateOverall();
                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                Log($"自动测试完成，总体结果: {OverallResult}");

                await StopTestAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                await StopTestAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log($"自动测试失败: {ex.Message}");
                await StopTestAsync().ConfigureAwait(false);
            }
        }

        internal bool CanExecuteStep(LatchStepViewModel step)
        {
            if (step == null) return false;
            return IsManualTestRunning && !IsBusy && IsPowerOn;
        }

        internal async Task ExecuteStepAsync(LatchStepViewModel step)
        {
            if (step == null) return;
            var token = _cts?.Token ?? CancellationToken.None;
            await ExecuteStepAsync(step, token).ConfigureAwait(false);

            EvaluateOverall();
            if (Items.All(i => i.IsMeasured))
            {
                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            }
        }

        private async Task ExecuteStepAsync(LatchStepViewModel step, CancellationToken token)
        {
            await _opLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                IsBusy = true;

                if (step.ResistanceOhm != null)
                {
                    await ApplyResistanceAsync(step.Item, step.ResistanceOhm.Value, token).ConfigureAwait(false);
                    await Task.Delay(2000).ConfigureAwait(false);
                }

                await MeasureAsync(step, token).ConfigureAwait(false);
            }
            finally
            {
                IsBusy = false;
                _opLock.Release();
            }
        }

        private async Task ApplyResistanceAsync(LatchItemViewModel item, double resistanceOhm, CancellationToken token)
        {
            await _resistorLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                Log($"设置电阻: {item.Title}, 通道={item.RoChannel}, 目标={resistanceOhm.ToString("0.###", CultureInfo.InvariantCulture)}Ω");

                var okReady = await EnsureResistorAsync(token).ConfigureAwait(false);
                if (!okReady)
                {
                    return;
                }

                var apiChannel = MapRoChannelTo7012Api(item.RoChannel);
                try
                {
                    await _resistor.SetRelayStateAsync(apiChannel, pathRelayClosed: true, shortCircuitClosed: false, token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Log($"设置7012继电器失败: {ex.Message}");
                    return;
                }

                try
                {
                    await _resistor.SetResistanceAsync(apiChannel, resistanceOhm, Pxi7012OutputMode.NoWait, token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Log($"设置7012电阻失败: {ex.Message}");
                    return;
                }

                try
                {
                    var r = await _resistor.GetResistanceAsync(apiChannel, token).ConfigureAwait(false);
                    Log($"电阻回读: {r.ToString("0.###", CultureInfo.InvariantCulture)}Ω");
                }
                catch (Exception ex)
                {
                    Log($"电阻回读异常: {ex.Message}");
                }

                await Task.Delay(50, token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log($"设置电阻异常: {ex.Message}");
            }
            finally
            {
                _resistorLock.Release();
            }
        }

        private async Task MeasureAsync(LatchStepViewModel step, CancellationToken token)
        {
            Log($"开始测量: {step.Item.MeasurePin}({step.Item.MeasurePinName}) {step.ActionDescription}");

            await MeasureFpgaIoAsync(step, token).ConfigureAwait(false);
        }

        private async Task EnsureLatchSupplyAsync(LatchItemViewModel item, CancellationToken token)
        {
            _power ??= new PowerSupplySocketApi();
            await _power.ConnectAsync(PowerSupplyIpAddress, token).ConfigureAwait(false);
            await _power.ApplyAsync(LatchSupplyChannel, 3.3, LatchSupplyCurrentA, token).ConfigureAwait(false);
            await _power.SetOutputEnabledAsync(LatchSupplyChannel, true, token).ConfigureAwait(false);
            await Task.Delay(300, token).ConfigureAwait(false);
            Log($"锁存供电已开启: {LatchSupplyChannel} 3.3V (请确认已接线到 {item.SupplyPin})");
        }

        private void ResetResults()
        {
            foreach (var item in Items)
            {
                foreach (var step in item.Steps)
                {
                    step.UpdateMeasurement(null, "---", "--", measured: false);
                }
            }
        }

        private void EvaluateOverall()
        {
            if (Items.Count == 0)
            {
                OverallResult = "--";
                return;
            }

            if (!Items.All(i => i.IsMeasured))
            {
                OverallResult = "--";
                return;
            }

            OverallResult = Items.All(i => i.IsPass) ? "PASS" : "FAIL";
        }

        private async Task StopTestAsync()
        {
            try { _cts?.Cancel(); } catch { }

            IsManualTestRunning = false;
            IsAutoTestRunning = false;
            IsBusy = false;

            try { await DisconnectFpgaTcpAsync().ConfigureAwait(false); } catch { }

            try { await CleanupPowerAsync().ConfigureAwait(false); } catch { }
            try { await CleanupResistorAsync().ConfigureAwait(false); } catch { }

            SetPowerState(false, "未供电");

            RaiseCanExecuteChanged();
        }

        private void SetPowerState(bool isOn, string status)
        {
            try
            {
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher != null && !dispatcher.CheckAccess())
                {
                    dispatcher.BeginInvoke(new Action(() => SetPowerState(isOn, status)));
                    return;
                }
            }
            catch
            {
            }

            IsPowerOn = isOn;
            PowerStatus = status;
        }

        private void RaiseCanExecuteChanged()
        {
            try
            {
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher != null && !dispatcher.CheckAccess())
                {
                    dispatcher.BeginInvoke(new Action(RaiseCanExecuteChanged));
                    return;
                }
            }
            catch
            {
            }

            foreach (var item in Items)
            {
                foreach (var step in item.Steps)
                {
                    step.ExecuteCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        private void Log(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
            try
            {
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher != null && !dispatcher.CheckAccess())
                {
                    dispatcher.BeginInvoke(new Action(() => Logs.Add(line)));
                }
                else
                {
                    Logs.Add(line);
                }
            }
            catch
            {
            }
        }

        private async Task EnsureMainPowerAsync(double voltageV, CancellationToken cancellationToken)
        {
            _power ??= new PowerSupplySocketApi();
            await _power.ConnectAsync(PowerSupplyIpAddress, cancellationToken).ConfigureAwait(false);
            await _power.ApplyAsync(PowerSupplyChannel.CH1, voltageV, MainPowerCurrentA, cancellationToken).ConfigureAwait(false);
            await _power.SetOutputEnabledAsync(PowerSupplyChannel.CH1, true, cancellationToken).ConfigureAwait(false);
            await Task.Delay(300, cancellationToken).ConfigureAwait(false);
        }

        private async Task CleanupPowerAsync()
        {
            try
            {
                if (_power != null)
                {
                    try { await _power.SetOutputEnabledAsync(PowerSupplyChannel.CH1, false, CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _power.SetOutputEnabledAsync(LatchSupplyChannel, false, CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _power.DisconnectAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _power.DisposeAsync().ConfigureAwait(false); } catch { }
                }
            }
            finally
            {
                _power = null;
            }
        }

        private async Task<bool> EnsureResistorAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_resistor != null && _resistor.IsConnected)
                return true;

            await CleanupResistorAsync().ConfigureAwait(false);

            var candidates = new uint[] { 1, 0, 2, 3, 4, 5, 6, 7 };
            foreach (var logicalId in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var device = new ProgrammableResistorDevice
                    {
                        Name = "电阻输出",
                        Model = "PXI-7012",
                        CardName = $"电阻输出(自动探测-{logicalId})",
                        SlotIndex = (int)logicalId
                    };

                    var api = new Pxi7012Api(device, logicalId);
                    await api.ConnectAsync(cancellationToken).ConfigureAwait(false);

                    _resistor = api;
                    _connectedResistorLogicalId = logicalId;
                    Log($"7012连接成功：逻辑ID={logicalId}");
                    return true;
                }
                catch
                {
                    try
                    {
                        if (_resistor != null)
                            await _resistor.DisposeAsync().ConfigureAwait(false);
                    }
                    catch
                    {
                    }
                    _resistor = null;
                    _connectedResistorLogicalId = null;
                }
            }

            Log("未找到PXI-7012(程控电阻)板卡");
            return false;
        }

        private async Task CleanupResistorAsync()
        {
            try
            {
                if (_resistor != null)
                {
                    try { await _resistor.DisposeAsync().ConfigureAwait(false); } catch { }
                }
            }
            finally
            {
                _resistor = null;
                _connectedResistorLogicalId = null;
            }
        }

        private static string MapRoChannelTo7012Api(string roChannel)
        {
            if (string.IsNullOrWhiteSpace(roChannel))
                throw new ArgumentException("RO channel is required", nameof(roChannel));

            var raw = roChannel.Trim();
            if (!raw.StartsWith("RO", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("RO channel must start with 'RO'", nameof(roChannel));

            if (!int.TryParse(raw.Substring(2), NumberStyles.Integer, CultureInfo.InvariantCulture, out var idx))
                throw new ArgumentException("Invalid RO channel index", nameof(roChannel));

            return $"RO{idx + 1}";
        }

        public void Dispose()
        {
            try { _cts?.Cancel(); } catch { }
            _cts?.Dispose();
            _cts = null;

            try { DisconnectFpgaTcpAsync().GetAwaiter().GetResult(); } catch { }

            try { CleanupPowerAsync().GetAwaiter().GetResult(); } catch { }
            try { CleanupResistorAsync().GetAwaiter().GetResult(); } catch { }

            try { _opLock?.Dispose(); } catch { }
            try { _resistorLock?.Dispose(); } catch { }
            try { _fpgaSendLock?.Dispose(); } catch { }
        }

        private static readonly byte[] FpgaFrameHeader = { 0xAA, 0x55 };

        private static string FpgaTs()
        {
            return DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
        }

        private static string ToHex(byte[] data)
        {
            if (data == null || data.Length == 0) return "--";
            return string.Join(" ", data.Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));
        }

        private static byte[] BuildFpgaFrame(byte command, byte[] data)
        {
            var dataLen = data?.Length ?? 0;
            var lengthField = (byte)(1 + dataLen);
            var frame = new byte[2 + 1 + 1 + dataLen];
            frame[0] = FpgaFrameHeader[0];
            frame[1] = FpgaFrameHeader[1];
            frame[2] = lengthField;
            frame[3] = command;
            if (dataLen > 0)
                Buffer.BlockCopy(data, 0, frame, 4, dataLen);
            return frame;
        }

        private void ResetFpgaCapture()
        {
            LastFpgaGpioInput = null;
            _lastFpgaGpioInputTime = null;
            _fpgaGpioWriteMask = 0;
        }

        private async Task EnsureFpgaTcpConnectedAsync(CancellationToken token)
        {
            if (_fpgaClient?.Connected == true && _fpgaStream != null)
                return;

            await DisconnectFpgaTcpAsync().ConfigureAwait(false);

            var client = new TcpClient { NoDelay = true };
            try
            {
                using var timeoutCts = new CancellationTokenSource(2000);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCts.Token);

                var connectTask = client.ConnectAsync(FpgaServerIpAddress, FpgaServerPort);
                var cancelTask = Task.Delay(Timeout.Infinite, linkedCts.Token);
                var completed = await Task.WhenAny(connectTask, cancelTask).ConfigureAwait(false);
                if (completed != connectTask)
                {
                    try { client.Close(); } catch { }
                    token.ThrowIfCancellationRequested();
                    throw new TimeoutException($"FPGA连接超时(2s): {FpgaServerIpAddress}:{FpgaServerPort}");
                }

                await connectTask.ConfigureAwait(false);
                _fpgaClient = client;
                _fpgaStream = _fpgaClient.GetStream();

                Log($"FPGA TCP连接成功: {FpgaServerIpAddress}:{FpgaServerPort}");
            }
            catch (Exception ex)
            {
                try { client.Close(); } catch { }
                _fpgaClient = null;
                _fpgaStream = null;
                Log($"FPGA TCP连接失败: {ex.Message}");
                throw;
            }
        }

        private async Task SendFpgaFrameAsync(byte command, byte[] payload, CancellationToken token)
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                await EnsureFpgaTcpConnectedAsync(token).ConfigureAwait(false);
                if (_fpgaStream == null)
                    throw new InvalidOperationException("FPGA未连接");

                await _fpgaSendLock.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    var frame = BuildFpgaFrame(command, payload);
                    Log($"[{FpgaTs()}][FPGA 发送TX] CMD=0x{command:X2} LEN={payload?.Length ?? 0} FRAME={ToHex(frame)}");
                    await _fpgaStream.WriteAsync(frame, 0, frame.Length, token).ConfigureAwait(false);
                    await _fpgaStream.FlushAsync(token).ConfigureAwait(false);
                    return;
                }
                catch (Exception ex) when (
                    ex is System.IO.IOException ||
                    ex is SocketException ||
                    ex is ObjectDisposedException)
                {
                    Log($"FPGA发送异常(第{attempt + 1}次): {ex.Message}");
                }
                finally
                {
                    _fpgaSendLock.Release();
                }

                try { await DisconnectFpgaTcpAsync().ConfigureAwait(false); } catch { }

                if (attempt == 0)
                {
                    Log("FPGA连接已断开，准备重连并重发一次");
                }
            }

            throw new InvalidOperationException("FPGA发送失败(已重试)");
        }

        private static int? MapSupplyPinToIo11To32BitIndex(string supplyPin)
        {
            if (string.Equals(supplyPin, "J34", StringComparison.OrdinalIgnoreCase))
                return 0;
            if (string.Equals(supplyPin, "J35", StringComparison.OrdinalIgnoreCase))
                return 1;
            return null;
        }

        private async Task ApplySupplyViaFpgaAsync(LatchStepViewModel step, CancellationToken token)
        {
            if (step?.Item == null)
                throw new ArgumentNullException(nameof(step));

            var bitIndex = MapSupplyPinToIo11To32BitIndex(step.Item.SupplyPin);
            if (bitIndex == null)
                throw new InvalidOperationException($"未配置FPGA供电引脚映射: {step.Item.SupplyPin}");

            // Only drive the current supply pin high to avoid cross-interference between J34 and J35.
            _fpgaGpioWriteMask = 1u << bitIndex.Value;
            //_fpgaGpioWriteMask = 3u;
            var payload = BitConverter.GetBytes(_fpgaGpioWriteMask);
            await SendFpgaFrameAsync(0x00, payload, token).ConfigureAwait(false);
            //Log($"[FPGA TX] GPIO Write(IO11-32) MASK=0x{_fpgaGpioWriteMask:X8} ({step.Item.SupplyPin})");
        }

        private async Task DisconnectFpgaTcpAsync()
        {
            try { _fpgaStream?.Close(); } catch { }
            try { _fpgaClient?.Close(); } catch { }

            _fpgaStream = null;
            _fpgaClient = null;
        }

        private async Task<byte[]> ReadExactFpgaAsync(int count, int timeoutMilliseconds, CancellationToken token)
        {
            var buf = new byte[count];
            var received = 0;
            while (received < count)
            {
                var readTask = _fpgaStream.ReadAsync(buf, received, count - received, token);
                var timeoutTask = Task.Delay(timeoutMilliseconds, token);
                var completed = await Task.WhenAny(readTask, timeoutTask).ConfigureAwait(false);
                if (completed != readTask)
                {
                    token.ThrowIfCancellationRequested();
                    throw new TimeoutException($"FPGA接收超时({timeoutMilliseconds}ms)");
                }

                var n = await readTask.ConfigureAwait(false);
                if (n == 0)
                    throw new InvalidOperationException("FPGA连接已断开(读取0字节)");
                received += n;
            }
            return buf;
        }

        private async Task<(byte cmd, byte[] payload)> ReadFpgaFrameAsync(int timeoutMilliseconds, CancellationToken token)
        {
            var header = await ReadExactFpgaAsync(2, timeoutMilliseconds, token).ConfigureAwait(false);
            if (header[0] != FpgaFrameHeader[0] || header[1] != FpgaFrameHeader[1])
                throw new InvalidOperationException($"FPGA帧头校验失败: 0x{header[0]:X2} 0x{header[1]:X2}");

            var lenBuf = await ReadExactFpgaAsync(1, timeoutMilliseconds, token).ConfigureAwait(false);
            var totalLen = lenBuf[0];
            var body = await ReadExactFpgaAsync(totalLen, timeoutMilliseconds, token).ConfigureAwait(false);

            var cmd = body[0];
            var payloadLen = totalLen - 1;
            var payload = new byte[payloadLen];
            if (payloadLen > 0)
                Buffer.BlockCopy(body, 1, payload, 0, payloadLen);

            var frame = new byte[2 + 1 + body.Length];
            frame[0] = header[0];
            frame[1] = header[1];
            frame[2] = lenBuf[0];
            Buffer.BlockCopy(body, 0, frame, 3, body.Length);
            Log($"[{FpgaTs()}][FPGA RX] CMD=0x{cmd:X2} LEN={payloadLen} FRAME={ToHex(frame)}");

            return (cmd, payload);
        }

        private async Task<uint> ReadFpgaGpioInputOnceAsync(int timeoutMilliseconds, CancellationToken token, byte? acceptCmd = null)
        {
            await EnsureFpgaTcpConnectedAsync(token).ConfigureAwait(false);
            if (_fpgaStream == null)
                throw new InvalidOperationException("FPGA未连接");

            using var timeoutCts = new CancellationTokenSource(timeoutMilliseconds);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCts.Token);

            while (!linkedCts.IsCancellationRequested)
            {
                var (cmd, payload) = await ReadFpgaFrameAsync(timeoutMilliseconds, linkedCts.Token).ConfigureAwait(false);
                var cmdOk = cmd == 0x00 || (acceptCmd != null && cmd == acceptCmd.Value);
                if (cmdOk && payload != null && payload.Length >= 4)
                {
                    var v = BitConverter.ToUInt32(payload, 0);
                    LastFpgaGpioInput = v;
                    _lastFpgaGpioInputTime = DateTime.Now;

                    var hex = string.Join(" ", payload.Take(4).Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));
                    Log($"[FPGA RX] GPIO Read(IO43-64) VALUE=0x{v:X8} DATA={hex}");
                    return v;
                }
            }

            token.ThrowIfCancellationRequested();
            throw new TimeoutException($"等待FPGA数据超时({timeoutMilliseconds}ms)");
        }

        private static int? MapPinToIo43To64BitIndex(string pin)
        {
            if (string.Equals(pin, "J31", StringComparison.OrdinalIgnoreCase))
                return 0;
            if (string.Equals(pin, "J32", StringComparison.OrdinalIgnoreCase))
                return 1;
            return null;
        }

        private static bool GetIo43To64Bit(uint gpioValue, int bitIndex)
        {
            if (bitIndex < 0 || bitIndex > 21)
                return false;
            return ((gpioValue >> bitIndex) & 0x1u) == 1u;
        }

        private async Task MeasureFpgaIoAsync(LatchStepViewModel step, CancellationToken token)
        {
            var needGpio = step?.Inject3V3 == true;
            try
            {
                await EnsureFpgaTcpConnectedAsync(token).ConfigureAwait(false);

                if (needGpio)
                {
                    //for (int i = 0; i < 3; i++)
                    //{
                        await ApplySupplyViaFpgaAsync(step, token).ConfigureAwait(false);
                        Log("等待复位高！！！");
                        await Task.Delay(1000, token).ConfigureAwait(false);
                        Log("等待复位低！！！");
                        await SendFpgaFrameAsync(0x00, new byte[] { 0x00, 0x00, 0x00, 0x00 }, token).ConfigureAwait(false);
                        //Log("[FPGA TX] Pre Read: AA 55 05 0A 00 00 00 00");
                        await Task.Delay(2000, token).ConfigureAwait(false);
                        await SendFpgaFrameAsync(0x0A, new byte[] { 0x00 }, token).ConfigureAwait(false);
                        await ReadFpgaGpioInputOnceAsync(2000, token, acceptCmd: 0x0A).ConfigureAwait(false);
                        await SendFpgaFrameAsync(0x0A, new byte[] { 0x00 }, token).ConfigureAwait(false);
                        await ReadFpgaGpioInputOnceAsync(2000, token, acceptCmd: 0x0A).ConfigureAwait(false);
                        await SendFpgaFrameAsync(0x0A, new byte[] { 0x00 }, token).ConfigureAwait(false);
                        await ReadFpgaGpioInputOnceAsync(2000, token, acceptCmd: 0x0A).ConfigureAwait(false);
                    //}

                }
                else
                {
                    await SendFpgaFrameAsync(0x0A, new byte[] { 0x00 }, token).ConfigureAwait(false);
                    await ReadFpgaGpioInputOnceAsync(2000, token, acceptCmd: 0x0A).ConfigureAwait(false);
                }

                if (!needGpio)
                {
                    await Task.Delay(1000, token).ConfigureAwait(false);
                    await SendFpgaFrameAsync(0x0A, new byte[] { 0x00 }, token).ConfigureAwait(false);
                    await ReadFpgaGpioInputOnceAsync(2000, token, acceptCmd: 0x0A).ConfigureAwait(false);
                }

                await SendFpgaFrameAsync(0x0A, new byte[] { 0x00 }, token).ConfigureAwait(false);
                Log("[FPGA TX] Force Read: AA 55 02 0A 00");

                var gpio = await ReadFpgaGpioInputOnceAsync(2000, token, acceptCmd: 0x0A).ConfigureAwait(false);
                var bitIndex = MapPinToIo43To64BitIndex(step.Item.MeasurePin);
                if (bitIndex == null)
                {
                    step.UpdateMeasurement(null, "--", "FAIL", measured: true);
                    Log($"未配置FPGA IO映射: {step.Item.MeasurePin}");
                    return;
                }

                var isHigh = GetIo43To64Bit(gpio, bitIndex.Value);
                var valueText = isHigh ? "高电平" : "低电平";
                var pass = step.Evaluation == LatchEvaluation.High33 ? isHigh : !isHigh;
                step.UpdateMeasurement(isHigh ? 1.0 : 0.0, valueText, pass ? "PASS" : "FAIL", measured: true);

                var ioNumber = 43 + bitIndex.Value;
                var ts = _lastFpgaGpioInputTime?.ToString("HH:mm:ss", CultureInfo.InvariantCulture) ?? "--";
                Log($"FPGA IO读取: {step.Item.MeasurePin}=IO{ioNumber}(bit{bitIndex.Value}) => {valueText} => {(pass ? "PASS" : "FAIL")}, 数据时间={ts}");
            }
            catch (TimeoutException ex)
            {
                step.UpdateMeasurement(null, "未接收", "FAIL", measured: true);
                Log(ex.Message);
            }
            catch (Exception ex)
            {
                step.UpdateMeasurement(null, "异常", "FAIL", measured: true);
                Log($"FPGA采集异常: {ex.Message}");
            }
            finally
            {
                if (needGpio)
                {
                    try
                    {
                        //await SendFpgaFrameAsync(0x00, new byte[] { 0x00, 0x00, 0x00, 0x00 }, CancellationToken.None).ConfigureAwait(false);
                        //Log("[FPGA TX] Reset: AA 55 05 00 00 00 00 00");
                    }
                    catch (Exception ex)
                    {
                        Log($"FPGA复位发送失败: {ex.Message}");
                    }
                }
            }
        }

        public enum LatchEvaluation
        {
            High33,
            Low0
        }

        public sealed class LatchItemViewModel : BindableBase
        {
            internal LatchItemViewModel(LatchModuleCircuitTestViewModel owner, string title, string roChannel, string supplyPin, string measurePin, string measurePinName)
            {
                _ = owner;
                Title = title;
                RoChannel = roChannel;
                SupplyPin = supplyPin;
                MeasurePin = measurePin;
                MeasurePinName = measurePinName;
            }

            public string Title { get; }

            public string RoChannel { get; }

            public string SupplyPin { get; }

            public string MeasurePin { get; }

            public string MeasurePinName { get; }

            public ObservableCollection<LatchStepViewModel> Steps { get; } = new ObservableCollection<LatchStepViewModel>();

            public bool IsMeasured => Steps.All(s => s.IsMeasured);

            public bool IsPass => Steps.All(s => string.Equals(s.Result, "PASS", StringComparison.OrdinalIgnoreCase));
        }

        public sealed class LatchStepViewModel : BindableBase
        {
            private readonly LatchModuleCircuitTestViewModel _owner;

            private string _voltageText = "---";
            private string _result = "--";
            private bool _isMeasured;

            internal LatchStepViewModel(
                LatchModuleCircuitTestViewModel owner,
                LatchItemViewModel item,
                string stepName,
                string actionDescription,
                double? resistanceOhm,
                bool inject3v3,
                string expected,
                LatchEvaluation evaluation)
            {
                _owner = owner;
                Item = item;
                StepName = stepName;
                ActionDescription = actionDescription;
                ResistanceOhm = resistanceOhm;
                Inject3V3 = inject3v3;
                Expected = expected;
                Evaluation = evaluation;

                ExecuteCommand = new DelegateCommand(async () => await _owner.ExecuteStepAsync(this), () => _owner.CanExecuteStep(this));
            }

            public LatchItemViewModel Item { get; }

            public string StepName { get; }

            public string ActionDescription { get; }

            public double? ResistanceOhm { get; }

            public bool Inject3V3 { get; }

            public string Expected { get; }

            public LatchEvaluation Evaluation { get; }

            public string VoltageText
            {
                get => _voltageText;
                private set => SetProperty(ref _voltageText, value);
            }

            public string Result
            {
                get => _result;
                private set => SetProperty(ref _result, value);
            }

            public bool IsMeasured
            {
                get => _isMeasured;
                private set => SetProperty(ref _isMeasured, value);
            }

            public DelegateCommand ExecuteCommand { get; }

            internal void UpdateMeasurement(double? valueVolt, string valueText, string result, bool measured)
            {
                _ = valueVolt;
                VoltageText = valueText;
                Result = result;
                IsMeasured = measured;
            }
        }
    }
}
