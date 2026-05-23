using Prism.Commands;
using Prism.Mvvm;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using MeasureControl.Helpers;
using MeasureControl.Services;
using MeasureControl.Services.HardwareApis;
using MeasureControl.Simulations.A_C_6_18_2_1;

namespace MeasureControl.ViewModels.SingleBoardTest.AirController
{
    public sealed class A_C_6_18_2_1ViewModel : BindableBase, IDisposable
    {
        private const string FixedTxChannel = "429_CH0";
        private const string FixedRxChannel = "429_CH2";

        private static readonly byte[] EnterAtpCommand8 = { 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] EnterAtpOk8 = { 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x02 };
        private static readonly byte[] ExitAtpCommand8 = { 0x00, 0x02, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01 };
        private static readonly byte[] ExitAtpOk8 = { 0x00, 0x02, 0x00, 0x01, 0x00, 0x00, 0x00, 0x03 };

        private static readonly byte[] TestCommandTemplate8 = { 0x23, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00 };

        private const double VoltageLowerLimit = 3.0;
        private const double VoltageUpperLimit = 3.6;

        private const string FixedDmmIpAddress = "192.168.1.13";
        private const string MatrixIpAddress = "192.168.1.3";
        private const int MatrixSlotIndex = 4;

        private readonly A_C_6_18_2_1Simulation _simulation = new A_C_6_18_2_1Simulation();
        private readonly SemaphoreSlim _arincOpLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _manualTestLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _autoTestLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _matrixSwitchLock = new SemaphoreSlim(1, 1);

        private CancellationTokenSource _autoTestCts;

        private string _testTxChannel;
        private string _testRxChannel;

        private string _enterAtpTxChannel;
        private string _enterAtpRxChannel;
        private string _exitAtpTxChannel;
        private string _exitAtpRxChannel;

        private string _enterAtpRxDataText;
        private string _exitAtpRxDataText;

        private string _testCommandXx;
        private string _testCommandXxHighNibble;
        private string _testCommandXxLowNibble;
        private string _testCommandDisplayText;

        private double? _j167Voltage;
        private string _j167VoltageText;
        private string _j167JudgeText;

        private double? _j168Voltage;
        private string _j168VoltageText;
        private string _j168JudgeText;

        private double? _j169Voltage;
        private string _j169VoltageText;
        private string _j169JudgeText;

        private double? _j231Voltage;
        private string _j231VoltageText;
        private string _j231JudgeText;

        private string _lastTestTime;
        private string _lastTestResult;
        private string _previousTestTime;
        private string _previousTestResult;

        private bool _isManualTestRunning;
        private bool _isAutoTestRunning;
        private bool _isBusy;

        private double _arincRate = 100000.0;
        private bool _isRealProduct;

        public ObservableCollection<string> HexNibbles { get; } = new ObservableCollection<string>();

        public A_C_6_18_2_1ViewModel()
        {
            _testTxChannel = FixedTxChannel;
            _testRxChannel = FixedRxChannel;

            _enterAtpTxChannel = _testTxChannel;
            _enterAtpRxChannel = _testRxChannel;
            _exitAtpTxChannel = _testTxChannel;
            _exitAtpRxChannel = _testRxChannel;

            EnterAtpRxDataText = "--";
            ExitAtpRxDataText = "--";

            HexNibbles.Clear();
            for (int i = 0; i < 16; i++)
                HexNibbles.Add(i.ToString("X"));

            TestCommandXxHighNibble = "0";
            TestCommandXxLowNibble = "0";

            J167VoltageText = "--";
            J167JudgeText = "--";
            J168VoltageText = "--";
            J168JudgeText = "--";
            J169VoltageText = "--";
            J169JudgeText = "--";
            J231VoltageText = "--";
            J231JudgeText = "--";

            LastTestTime = "--";
            LastTestResult = "--";
            PreviousTestTime = "--";
            PreviousTestResult = "--";

            IsRealProduct = true;

            ManualTestCommand = new DelegateCommand(OnManualTest);
            AutoTestCommand = new DelegateCommand(OnAutoTest);

            SendEnterAtpCommand = new DelegateCommand(async () => await OnSendEnterAtpAsync());
            SendTestCommand = new DelegateCommand(async () => await OnSendTestAsync());
            MeasureVoltageCommand = new DelegateCommand(async () => await OnMeasureVoltageAsync());
            MeasureJ168VoltageCommand = new DelegateCommand(async () => await OnMeasureJ168VoltageAsync());
            MeasureJ169VoltageCommand = new DelegateCommand(async () => await OnMeasureJ169VoltageAsync());
            MeasureJ231VoltageCommand = new DelegateCommand(async () => await OnMeasureJ231VoltageAsync());
            SendExitAtpCommand = new DelegateCommand(async () => await OnSendExitAtpAsync());
            ClearLogCommand = new DelegateCommand(() => Logs.Clear());
        }

        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();

        public DelegateCommand ManualTestCommand { get; }
        public DelegateCommand AutoTestCommand { get; }
        public DelegateCommand SendEnterAtpCommand { get; }
        public DelegateCommand SendTestCommand { get; }
        public DelegateCommand MeasureVoltageCommand { get; }
        public DelegateCommand MeasureJ168VoltageCommand { get; }
        public DelegateCommand MeasureJ169VoltageCommand { get; }
        public DelegateCommand MeasureJ231VoltageCommand { get; }
        public DelegateCommand SendExitAtpCommand { get; }
        public DelegateCommand ClearLogCommand { get; }

        public string TestTxChannel
        {
            get => _testTxChannel;
        }

        public string TestRxChannel
        {
            get => _testRxChannel;
        }

        public string EnterAtpTxChannel
        {
            get => _enterAtpTxChannel;
        }

        public string EnterAtpRxChannel
        {
            get => _enterAtpRxChannel;
        }

        public string ExitAtpTxChannel
        {
            get => _exitAtpTxChannel;
        }

        public string ExitAtpRxChannel
        {
            get => _exitAtpRxChannel;
        }

        public double ArincRate
        {
            get => _arincRate;
            set => SetProperty(ref _arincRate, value);
        }

        public bool IsRealProduct
        {
            get => _isRealProduct;
            set => SetProperty(ref _isRealProduct, value);
        }

        public string EnterAtpRxDataText
        {
            get => _enterAtpRxDataText;
            private set => SetProperty(ref _enterAtpRxDataText, value);
        }

        public string ExitAtpRxDataText
        {
            get => _exitAtpRxDataText;
            private set => SetProperty(ref _exitAtpRxDataText, value);
        }

        public string TestCommandXx
        {
            get => _testCommandXx;
            set
            {
                if (SetProperty(ref _testCommandXx, value))
                {
                    UpdateTestCommandNibblesFromXx();
                    UpdateTestCommandDisplayText();
                }
            }
        }

        public string TestCommandXxHighNibble
        {
            get => _testCommandXxHighNibble;
            set
            {
                if (SetProperty(ref _testCommandXxHighNibble, NormalizeHexNibble(value)))
                {
                    UpdateTestCommandXxFromNibbles();
                    UpdateTestCommandDisplayText();
                }
            }
        }

        public string TestCommandXxLowNibble
        {
            get => _testCommandXxLowNibble;
            set
            {
                if (SetProperty(ref _testCommandXxLowNibble, NormalizeHexNibble(value)))
                {
                    UpdateTestCommandXxFromNibbles();
                    UpdateTestCommandDisplayText();
                }
            }
        }

        public string TestCommandDisplayText
        {
            get => _testCommandDisplayText;
            private set => SetProperty(ref _testCommandDisplayText, value);
        }

        public string J167VoltageText
        {
            get => _j167VoltageText;
            private set => SetProperty(ref _j167VoltageText, value);
        }

        public string J167JudgeText
        {
            get => _j167JudgeText;
            private set => SetProperty(ref _j167JudgeText, value);
        }

        public string J168VoltageText
        {
            get => _j168VoltageText;
            private set => SetProperty(ref _j168VoltageText, value);
        }

        public string J168JudgeText
        {
            get => _j168JudgeText;
            private set => SetProperty(ref _j168JudgeText, value);
        }

        public string J169VoltageText
        {
            get => _j169VoltageText;
            private set => SetProperty(ref _j169VoltageText, value);
        }

        public string J169JudgeText
        {
            get => _j169JudgeText;
            private set => SetProperty(ref _j169JudgeText, value);
        }

        public string J231VoltageText
        {
            get => _j231VoltageText;
            private set => SetProperty(ref _j231VoltageText, value);
        }

        public string J231JudgeText
        {
            get => _j231JudgeText;
            private set => SetProperty(ref _j231JudgeText, value);
        }

        public string LastTestTime
        {
            get => _lastTestTime;
            private set => SetProperty(ref _lastTestTime, value);
        }

        public string LastTestResult
        {
            get => _lastTestResult;
            private set => SetProperty(ref _lastTestResult, value);
        }

        public string PreviousTestTime
        {
            get => _previousTestTime;
            private set => SetProperty(ref _previousTestTime, value);
        }

        public string PreviousTestResult
        {
            get => _previousTestResult;
            private set => SetProperty(ref _previousTestResult, value);
        }

        public bool IsManualTestRunning
        {
            get => _isManualTestRunning;
            private set => SetProperty(ref _isManualTestRunning, value);
        }

        public bool IsAutoTestRunning
        {
            get => _isAutoTestRunning;
            private set => SetProperty(ref _isAutoTestRunning, value);
        }

        public bool IsBusy
        {
            get => _isBusy;
            private set => SetProperty(ref _isBusy, value);
        }

        private void AddLog(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            try
            {
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher != null && !dispatcher.CheckAccess())
                {
                    dispatcher.BeginInvoke(new Action(() => Logs.Add(message)));
                }
                else
                {
                    Logs.Add(message);
                }
            }
            catch
            {
            }

            try
            {
                Debug.WriteLine(message);
            }
            catch
            {
            }
        }

        private void UpdateTestCommandDisplayText()
        {
            UpdateTestCommandXxFromNibbles();
            var xx = string.IsNullOrWhiteSpace(TestCommandXx) ? "--" : TestCommandXx.Trim().ToUpperInvariant();
            TestCommandDisplayText = $"0x23 01 01 {xx} 00 00 00 00";
        }

        private byte[] BuildTestCommand8OrNull(out string error)
        {
            if (!TryParseHexByte(TestCommandXx, out var xx))
            {
                error = "测试指令XX无效，应为2位16进制（00~FF）";
                return null;
            }

            error = null;
            var cmd = (byte[])TestCommandTemplate8.Clone();
            cmd[3] = xx;
            return cmd;
        }

        private void UpdateTestCommandXxFromNibbles()
        {
            var hi = NormalizeHexNibble(TestCommandXxHighNibble);
            var lo = NormalizeHexNibble(TestCommandXxLowNibble);
            var xx = hi + lo;

            if (!string.Equals(_testCommandXx, xx, StringComparison.Ordinal))
            {
                _testCommandXx = xx;
                RaisePropertyChanged(nameof(TestCommandXx));
            }
        }

        private void UpdateTestCommandNibblesFromXx()
        {
            var raw = (TestCommandXx ?? string.Empty).Trim().ToUpperInvariant();
            if (raw.StartsWith("0X", StringComparison.Ordinal))
                raw = raw.Substring(2);

            if (raw.Length != 2)
                return;

            var hi = NormalizeHexNibble(raw[0].ToString());
            var lo = NormalizeHexNibble(raw[1].ToString());

            if (!string.Equals(_testCommandXxHighNibble, hi, StringComparison.Ordinal))
            {
                _testCommandXxHighNibble = hi;
                RaisePropertyChanged(nameof(TestCommandXxHighNibble));
            }

            if (!string.Equals(_testCommandXxLowNibble, lo, StringComparison.Ordinal))
            {
                _testCommandXxLowNibble = lo;
                RaisePropertyChanged(nameof(TestCommandXxLowNibble));
            }
        }

        private static string NormalizeHexNibble(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
                return "0";

            s = s.Trim().ToUpperInvariant();
            if (s.Length != 1)
                return "0";

            char c = s[0];
            if (c >= '0' && c <= '9')
                return c.ToString();
            if (c >= 'A' && c <= 'F')
                return c.ToString();
            return "0";
        }

        private static bool TryParseHexByte(string text, out byte value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            var raw = text.Trim().ToUpperInvariant();
            if (raw.StartsWith("0X", StringComparison.Ordinal))
                raw = raw.Substring(2);
            if (raw.Length != 2)
                return false;

            return byte.TryParse(raw, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
        }

        private void OnManualTest()
        {
            if (IsManualTestRunning)
            {
                _ = StopManualTestAsync();
                return;
            }

            _ = StartManualTestAsync();
        }

        private void OnAutoTest()
        {
            if (IsAutoTestRunning)
            {
                _ = StopAutoTestAsync();
                return;
            }

            _ = RunAutoTestAsync();
        }

        private static async Task TryApplyComponentDownStateAsync(CancellationToken token)
        {
            try
            {
                var api = Prism.Ioc.ContainerLocator.Container.Resolve(typeof(MeasureControl.Services.HardwareApis.IComponentPowerStateApi)) as MeasureControl.Services.HardwareApis.IComponentPowerStateApi;
                if (api != null)
                    await api.ApplyComponentDownStateAsync(token).ConfigureAwait(false);
            }
            catch
            {
            }
        }

        private async Task StartManualTestAsync()
        {
            if (IsBusy)
                return;

            await _manualTestLock.WaitAsync();
            try
            {
                if (IsBusy)
                    return;

                IsBusy = true;
                try
                {
                    IsManualTestRunning = true;
                    EnterAtpRxDataText = "--";
                    ExitAtpRxDataText = "--";
                    J167VoltageText = "--";
                    J167JudgeText = "--";
                    _j167Voltage = null;
                    J168VoltageText = "--";
                    J168JudgeText = "--";
                    _j168Voltage = null;
                    J169VoltageText = "--";
                    J169JudgeText = "--";
                    _j169Voltage = null;
                    J231VoltageText = "--";
                    J231JudgeText = "--";
                    _j231Voltage = null;
                    LastTestTime = "--";
                    LastTestResult = "--";

                    _simulation.IsRealProduct = true;
                    _simulation.ArincRate = ArincRate;

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试启动({(_simulation.IsRealProduct ? "真实产品模式" : "仿真模式")})：开始打开设备");

                    try
                    {
                        var api = Prism.Ioc.ContainerLocator.Container.Resolve(typeof(MeasureControl.Services.HardwareApis.IComponentPowerStateApi)) as MeasureControl.Services.HardwareApis.IComponentPowerStateApi;
                        if (api != null)
                            await api.ApplyComponent28VStateAsync(CancellationToken.None);
                    }
                    catch { }

                    await _simulation.StartAsync(TestTxChannel, TestRxChannel, msg => AddLog(msg));
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试已启动：可发送测试指令");
                }
                catch (Exception ex)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试启动失败: {ex.Message}");
                    IsManualTestRunning = false;
                }
                finally
                {
                    IsBusy = false;
                }
            }
            finally
            {
                _manualTestLock.Release();
            }
        }

        private async Task StopManualTestAsync()
        {
            if (IsBusy)
                return;

            await _manualTestLock.WaitAsync();
            try
            {
                if (IsBusy)
                    return;

                IsBusy = true;
                try
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试停止：释放资源");
                    await _simulation.StopAsync(msg => AddLog(msg));
                }
                catch (Exception ex)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 停止失败: {ex.Message}");
                }
                finally
                {
                    try { await TryApplyComponentDownStateAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                    IsManualTestRunning = false;
                    IsBusy = false;
                }
            }
            finally
            {
                _manualTestLock.Release();
            }
        }

        private async Task OnSendEnterAtpAsync()
        {
            if (!IsManualTestRunning || IsBusy)
                return;

            if (!string.Equals(EnterAtpTxChannel, TestTxChannel, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(EnterAtpRxChannel, TestRxChannel, StringComparison.OrdinalIgnoreCase))
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 当前仿真仅打开通道：TX={TestTxChannel}, RX={TestRxChannel}。进入ATP的TX/RX需与其一致");
                return;
            }

            await _arincOpLock.WaitAsync();
            try
            {
                IsBusy = true;
                try
                {
                    await _simulation.ClearRxFifoAsync(EnterAtpRxChannel);
                    await Task.Delay(20);

                    var token = CancellationToken.None;

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 发送进入ATP：{FormatBytes(EnterAtpCommand8)}");
                    await _simulation.SendBenchCommandOnlyAsync(EnterAtpTxChannel, EnterAtpCommand8, msg => AddLog(msg), token);

                    var resp = await _simulation.WaitBenchResponse8Async(
                        EnterAtpRxChannel,
                        b => b != null && b.SequenceEqual(EnterAtpOk8),
                        timeoutMs: 1200,
                        log: msg => AddLog(msg),
                        token: token);

                    if (resp == null)
                    {
                        AddLog($"[{DateTime.Now:HH:mm:ss}] 进入ATP失败：未收到OK");
                        return;
                    }

                    EnterAtpRxDataText = $"0x{FormatBytesHex(resp)}";
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 进入ATP成功");
                }
                finally
                {
                    IsBusy = false;
                }
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 进入ATP异常: {ex.Message}");
            }
            finally
            {
                _arincOpLock.Release();
            }
        }

        private async Task OnSendExitAtpAsync()
        {
            if (!IsManualTestRunning || IsBusy)
                return;

            if (!string.Equals(ExitAtpTxChannel, TestTxChannel, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(ExitAtpRxChannel, TestRxChannel, StringComparison.OrdinalIgnoreCase))
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 当前仿真仅打开通道：TX={TestTxChannel}, RX={TestRxChannel}。退出ATP的TX/RX需与其一致");
                return;
            }

            await _arincOpLock.WaitAsync();
            try
            {
                IsBusy = true;
                try
                {
                    await _simulation.ClearRxFifoAsync(ExitAtpRxChannel);
                    await Task.Delay(20);

                    var token = CancellationToken.None;

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 发送退出ATP：{FormatBytes(ExitAtpCommand8)}");
                    await _simulation.SendBenchCommandOnlyAsync(ExitAtpTxChannel, ExitAtpCommand8, msg => AddLog(msg), token);

                    var resp = await _simulation.WaitBenchResponse8Async(
                        ExitAtpRxChannel,
                        b => b != null && b.SequenceEqual(ExitAtpOk8),
                        timeoutMs: 1200,
                        log: msg => AddLog(msg),
                        token: token);

                    if (resp == null)
                    {
                        AddLog($"[{DateTime.Now:HH:mm:ss}] 退出ATP失败：未收到OK");
                        return;
                    }

                    ExitAtpRxDataText = $"0x{FormatBytesHex(resp)}";
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 退出ATP成功");
                }
                finally
                {
                    IsBusy = false;
                }
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 退出ATP异常: {ex.Message}");
            }
            finally
            {
                _arincOpLock.Release();
            }
        }

        private async Task OnSendTestAsync()
        {
            if (!IsManualTestRunning || IsBusy)
                return;

            await _arincOpLock.WaitAsync();
            try
            {
                IsBusy = true;
                try
                {
                    var token = CancellationToken.None;

                    var cmd8 = BuildTestCommand8OrNull(out var err);
                    if (cmd8 == null)
                    {
                        AddLog($"[{DateTime.Now:HH:mm:ss}] {err}");
                        return;
                    }

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 发送测试指令：{FormatBytes(cmd8)}（无回包）");
                    await _simulation.SendBenchCommandOnlyAsync(TestTxChannel, cmd8, msg => AddLog(msg), token);
                }
                finally
                {
                    IsBusy = false;
                }
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 发送测试指令异常: {ex.Message}");
            }
            finally
            {
                _arincOpLock.Release();
            }
        }

        private async Task OnMeasureVoltageAsync()
        {
            if (!IsManualTestRunning || IsBusy)
                return;

            await _arincOpLock.WaitAsync();
            try
            {
                IsBusy = true;
                try
                {
                    var token = CancellationToken.None;
                    var v = await ReadDmmVoltageAsync("J167", GetMatrixOpsForJ167(), token);
                    _j167Voltage = v;

                    if (v.HasValue)
                    {
                        J167VoltageText = $"{v.Value:0.00000} V";
                        bool pass = v.Value >= VoltageLowerLimit && v.Value <= VoltageUpperLimit;
                        J167JudgeText = pass ? "PASS" : "FAIL";
                        AddLog($"[{DateTime.Now:HH:mm:ss}] J167 电压={v.Value:0.00000} V, 判据[{VoltageLowerLimit:0.0},{VoltageUpperLimit:0.0}]V -> {J167JudgeText}");
                    }
                    else
                    {
                        J167VoltageText = "--";
                        J167JudgeText = "FAIL";
                        AddLog($"[{DateTime.Now:HH:mm:ss}] J167 电压测量无有效值");
                    }
                }
                finally
                {
                    IsBusy = false;
                }
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 电压测量异常: {ex.Message}");
            }
            finally
            {
                _arincOpLock.Release();
            }
        }

        private async Task OnMeasureJ168VoltageAsync()
        {
            if (!IsManualTestRunning || IsBusy)
                return;

            await _arincOpLock.WaitAsync();
            try
            {
                IsBusy = true;
                try
                {
                    var token = CancellationToken.None;
                    var v = await ReadDmmVoltageAsync("J168", GetMatrixOpsForJ168(), token);
                    _j168Voltage = v;

                    if (v.HasValue)
                    {
                        J168VoltageText = $"{v.Value:0.00000} V";
                        bool pass = v.Value >= VoltageLowerLimit && v.Value <= VoltageUpperLimit;
                        J168JudgeText = pass ? "PASS" : "FAIL";
                        AddLog($"[{DateTime.Now:HH:mm:ss}] J168 电压={v.Value:0.00000} V, 判据[{VoltageLowerLimit:0.0},{VoltageUpperLimit:0.0}]V -> {J168JudgeText}");
                    }
                    else
                    {
                        J168VoltageText = "--";
                        J168JudgeText = "FAIL";
                        AddLog($"[{DateTime.Now:HH:mm:ss}] J168 电压测量无有效值");
                    }
                }
                finally
                {
                    IsBusy = false;
                }
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 电压测量异常: {ex.Message}");
            }
            finally
            {
                _arincOpLock.Release();
            }
        }

        private async Task OnMeasureJ169VoltageAsync()
        {
            if (!IsManualTestRunning || IsBusy)
                return;

            await _arincOpLock.WaitAsync();
            try
            {
                IsBusy = true;
                try
                {
                    var token = CancellationToken.None;
                    var v = await ReadDmmVoltageAsync("J169", GetMatrixOpsForJ169(), token);
                    _j169Voltage = v;

                    if (v.HasValue)
                    {
                        J169VoltageText = $"{v.Value:0.00000} V";
                        bool pass = v.Value >= VoltageLowerLimit && v.Value <= VoltageUpperLimit;
                        J169JudgeText = pass ? "PASS" : "FAIL";
                        AddLog($"[{DateTime.Now:HH:mm:ss}] J169 电压={v.Value:0.00000} V, 判据[{VoltageLowerLimit:0.0},{VoltageUpperLimit:0.0}]V -> {J169JudgeText}");
                    }
                    else
                    {
                        J169VoltageText = "--";
                        J169JudgeText = "FAIL";
                        AddLog($"[{DateTime.Now:HH:mm:ss}] J169 电压测量无有效值");
                    }
                }
                finally
                {
                    IsBusy = false;
                }
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 电压测量异常: {ex.Message}");
            }
            finally
            {
                _arincOpLock.Release();
            }
        }

        private async Task OnMeasureJ231VoltageAsync()
        {
            if (!IsManualTestRunning || IsBusy)
                return;

            await _arincOpLock.WaitAsync();
            try
            {
                IsBusy = true;
                try
                {
                    var token = CancellationToken.None;
                    var v = await ReadDmmVoltageAsync("J231", GetMatrixOpsForJ231(), token);
                    _j231Voltage = v;

                    if (v.HasValue)
                    {
                        J231VoltageText = $"{v.Value:0.00000} V";
                        bool pass = v.Value >= VoltageLowerLimit && v.Value <= VoltageUpperLimit;
                        J231JudgeText = pass ? "PASS" : "FAIL";
                        AddLog($"[{DateTime.Now:HH:mm:ss}] J231 电压={v.Value:0.00000} V, 判据[{VoltageLowerLimit:0.0},{VoltageUpperLimit:0.0}]V -> {J231JudgeText}");
                    }
                    else
                    {
                        J231VoltageText = "--";
                        J231JudgeText = "FAIL";
                        AddLog($"[{DateTime.Now:HH:mm:ss}] J231 电压测量无有效值");
                    }
                }
                finally
                {
                    IsBusy = false;
                }
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 电压测量异常: {ex.Message}");
            }
            finally
            {
                _arincOpLock.Release();
            }
        }

        private async Task<double?> ReadDmmVoltageAsync(string pointName, (string inNode, string outNode, int slot, int? basePort)[] ops, CancellationToken token)
        {
            bool matrixOk = await ConnectMatrixAsync(pointName, ops, token);
            if (!matrixOk)
                throw new InvalidOperationException("矩阵开关通路建立失败");

            await using IDmmApi dmm = new DmmSocketApi();
            try
            {
                await dmm.ConnectAsync(FixedDmmIpAddress, token);
                var r = await dmm.ReadOnceAsync(DmmMeasureMode.DCV, new DmmReadOptions { TimeoutMilliseconds = 8000 }, token);
                if (r == null)
                    return null;
                if (r.IsOverrange)
                    return null;
                return r.Value;
            }
            finally
            {
                try { await dmm.DisconnectAsync(token); } catch { }
                await DisconnectMatrixAsync(pointName, ops, token);
            }
        }

        private async Task<bool> ConnectMatrixAsync(string pointName, (string inNode, string outNode, int slot, int? basePort)[] ops, CancellationToken token)
        {
            await _matrixSwitchLock.WaitAsync(token);
            try
            {
                var tasks = ops.Select(op =>
                {
                    if (op.basePort.HasValue)
                        return MatrixControlService.Instance.ConnectNodesAsync(op.inNode, op.outNode, op.slot, MatrixIpAddress, op.basePort.Value);
                    return MatrixControlService.Instance.ConnectNodesAsync(op.inNode, op.outNode, op.slot, MatrixIpAddress);
                }).ToArray();

                var results = await Task.WhenAll(tasks);
                for (int i = 0; i < ops.Length; i++)
                {
                    var op = ops[i];
                    bool okOne = i < results.Length && results[i];
                    string type = op.basePort.HasValue ? "3022" : "2601";
                    string portText = op.basePort.HasValue ? $" basePort={op.basePort.Value}" : string.Empty;
                    AddLog($"[{DateTime.Now:HH:mm:ss}] {pointName} 矩阵开关通路({type}): {op.inNode}->{op.outNode} slot={op.slot} ip={MatrixIpAddress}{portText}, ok={okOne}");
                }

                bool allOk = results.All(r => r);
                if (allOk)
                    await Task.Delay(200, token);
                return allOk;
            }
            finally
            {
                _matrixSwitchLock.Release();
            }
        }

        private async Task DisconnectMatrixAsync(string pointName, (string inNode, string outNode, int slot, int? basePort)[] ops, CancellationToken token)
        {
            await _matrixSwitchLock.WaitAsync(token);
            try
            {
                var tasks = ops.Select(op =>
                {
                    if (op.basePort.HasValue)
                        return MatrixControlService.Instance.DisconnectNodesAsync(op.inNode, op.outNode, op.slot, MatrixIpAddress, op.basePort.Value);
                    return MatrixControlService.Instance.DisconnectNodesAsync(op.inNode, op.outNode, op.slot, MatrixIpAddress);
                }).ToArray();

                var results = await Task.WhenAll(tasks);
                for (int i = 0; i < ops.Length; i++)
                {
                    var op = ops[i];
                    bool ok = i < results.Length && results[i];
                    string type = op.basePort.HasValue ? "3022" : "2601";
                    string portText = op.basePort.HasValue ? $" basePort={op.basePort.Value}" : string.Empty;
                    AddLog($"[{DateTime.Now:HH:mm:ss}] {pointName} 矩阵开关断开({type}): {op.inNode}->{op.outNode} slot={op.slot} ip={MatrixIpAddress}{portText}, ok={ok}");
                }
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 矩阵开关断开异常: {ex.Message}");
            }
            finally
            {
                _matrixSwitchLock.Release();
            }
        }

        private static (string inNode, string outNode, int slot, int? basePort)[] GetMatrixOpsForJ167()
        {
            return new (string inNode, string outNode, int slot, int? basePort)[]
            {
                ("I0", "O29", 9, null),
                ("I4", "O7", MatrixSlotIndex, null)
            };
        }

        private static (string inNode, string outNode, int slot, int? basePort)[] GetMatrixOpsForJ168()
        {
            return new (string inNode, string outNode, int slot, int? basePort)[]
            {
                ("I0", "O30", 9, null),
                ("I4", "O7", MatrixSlotIndex, null)
            };
        }

        private static (string inNode, string outNode, int slot, int? basePort)[] GetMatrixOpsForJ169()
        {
            return new (string inNode, string outNode, int slot, int? basePort)[]
            {
                ("I0", "O31", 9, null),
                ("I4", "O7", MatrixSlotIndex, null)
            };
        }

        private static (string inNode, string outNode, int slot, int? basePort)[] GetMatrixOpsForJ231()
        {
            return new (string inNode, string outNode, int slot, int? basePort)[]
            {
                ("I0", "O55", 3, 50300),
                ("I4", "O11", MatrixSlotIndex, null)
            };
        }

        private async Task RunAutoTestAsync()
        {
            if (IsBusy)
                return;

            await _autoTestLock.WaitAsync();
            try
            {
                if (IsBusy)
                    return;

                IsBusy = true;
                try
                {
                    IsAutoTestRunning = true;
                    EnterAtpRxDataText = "--";
                    ExitAtpRxDataText = "--";
                    J167VoltageText = "--";
                    J167JudgeText = "--";
                    _j167Voltage = null;
                    J168VoltageText = "--";
                    J168JudgeText = "--";
                    _j168Voltage = null;
                    J169VoltageText = "--";
                    J169JudgeText = "--";
                    _j169Voltage = null;
                    J231VoltageText = "--";
                    J231JudgeText = "--";
                    _j231Voltage = null;
                    LastTestTime = "--";
                    LastTestResult = "--";

                    _autoTestCts?.Cancel();
                    _autoTestCts?.Dispose();
                    _autoTestCts = new CancellationTokenSource();

                    var token = _autoTestCts.Token;

                    try
                    {
                        var api = Prism.Ioc.ContainerLocator.Container.Resolve(typeof(MeasureControl.Services.HardwareApis.IComponentPowerStateApi)) as MeasureControl.Services.HardwareApis.IComponentPowerStateApi;
                        if (api != null)
                            await api.ApplyComponent28VStateAsync(token);
                    }
                    catch { }

                    _simulation.IsRealProduct = true;
                    _simulation.ArincRate = ArincRate;

                    AddLog($"[{DateTime.Now:HH:mm:ss}] ========== 自动测试开始 ==========");
                    await _simulation.StartAsync(TestTxChannel, TestRxChannel, msg => AddLog(msg));

                    await _simulation.ClearRxFifoAsync(TestRxChannel);
                    await Task.Delay(20, token);

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 步骤1：进入ATP");
                    await _simulation.SendBenchCommandOnlyAsync(TestTxChannel, EnterAtpCommand8, msg => AddLog(msg), token);

                    var enterOk = await _simulation.WaitBenchResponse8Async(
                        TestRxChannel,
                        b => b != null && b.SequenceEqual(EnterAtpOk8),
                        timeoutMs: 1200,
                        log: msg => AddLog(msg),
                        token: token);

                    if (enterOk == null)
                    {
                        SetLastTestResult("FAIL");
                        AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试失败：进入ATP超时");
                        return;
                    }

                    EnterAtpRxDataText = $"0x{FormatBytesHex(enterOk)}";

                    await _simulation.ClearRxFifoAsync(TestRxChannel);
                    await Task.Delay(20, token);

                    var cmd8 = BuildTestCommand8OrNull(out var err);
                    if (cmd8 == null)
                    {
                        SetLastTestResult("FAIL");
                        AddLog($"[{DateTime.Now:HH:mm:ss}] {err}");
                        return;
                    }

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 步骤2：发送测试指令（无回包）");
                    await _simulation.SendBenchCommandOnlyAsync(TestTxChannel, cmd8, msg => AddLog(msg), token);

                    bool passAll = true;

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 步骤3：万用表测量J167电压");
                    var v167 = await ReadDmmVoltageAsync("J167", GetMatrixOpsForJ167(), token);
                    _j167Voltage = v167;
                    if (v167.HasValue)
                    {
                        J167VoltageText = $"{v167.Value:0.00000} V";
                        bool pass = v167.Value >= VoltageLowerLimit && v167.Value <= VoltageUpperLimit;
                        J167JudgeText = pass ? "PASS" : "FAIL";
                        passAll &= pass;
                    }
                    else
                    {
                        J167VoltageText = "--";
                        J167JudgeText = "FAIL";
                        passAll = false;
                    }

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 步骤4：万用表测量J168电压");
                    var v168 = await ReadDmmVoltageAsync("J168", GetMatrixOpsForJ168(), token);
                    _j168Voltage = v168;
                    if (v168.HasValue)
                    {
                        J168VoltageText = $"{v168.Value:0.00000} V";
                        bool pass = v168.Value >= VoltageLowerLimit && v168.Value <= VoltageUpperLimit;
                        J168JudgeText = pass ? "PASS" : "FAIL";
                        passAll &= pass;
                    }
                    else
                    {
                        J168VoltageText = "--";
                        J168JudgeText = "FAIL";
                        passAll = false;
                    }

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 步骤5：万用表测量J169电压");
                    var v169 = await ReadDmmVoltageAsync("J169", GetMatrixOpsForJ169(), token);
                    _j169Voltage = v169;
                    if (v169.HasValue)
                    {
                        J169VoltageText = $"{v169.Value:0.00000} V";
                        bool pass = v169.Value >= VoltageLowerLimit && v169.Value <= VoltageUpperLimit;
                        J169JudgeText = pass ? "PASS" : "FAIL";
                        passAll &= pass;
                    }
                    else
                    {
                        J169VoltageText = "--";
                        J169JudgeText = "FAIL";
                        passAll = false;
                    }

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 步骤6：万用表测量J231电压");
                    var v231 = await ReadDmmVoltageAsync("J231", GetMatrixOpsForJ231(), token);
                    _j231Voltage = v231;
                    if (v231.HasValue)
                    {
                        J231VoltageText = $"{v231.Value:0.00000} V";
                        bool pass = v231.Value >= VoltageLowerLimit && v231.Value <= VoltageUpperLimit;
                        J231JudgeText = pass ? "PASS" : "FAIL";
                        passAll &= pass;
                    }
                    else
                    {
                        J231VoltageText = "--";
                        J231JudgeText = "FAIL";
                        passAll = false;
                    }

                    SetLastTestResult(passAll ? "PASS" : "FAIL");

                    await _simulation.ClearRxFifoAsync(TestRxChannel);
                    await Task.Delay(20, token);

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 步骤7：退出ATP");
                    await _simulation.SendBenchCommandOnlyAsync(TestTxChannel, ExitAtpCommand8, msg => AddLog(msg), token);

                    var exitOk = await _simulation.WaitBenchResponse8Async(
                        TestRxChannel,
                        b => b != null && b.SequenceEqual(ExitAtpOk8),
                        timeoutMs: 1200,
                        log: msg => AddLog(msg),
                        token: token);

                    if (exitOk != null)
                    {
                        ExitAtpRxDataText = $"0x{FormatBytesHex(exitOk)}";
                    }

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试结束：{LastTestResult}");
                }
                catch (OperationCanceledException)
                {
                    SetLastTestResult("FAIL");
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试已取消");
                }
                catch (Exception ex)
                {
                    SetLastTestResult("FAIL");
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试异常: {ex.Message}");
                }
                finally
                {
                    if (LastTestTime == "--")
                    {
                        LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    }

                    try
                    {
                        await _simulation.StopAsync(msg => AddLog(msg));
                    }
                    catch
                    {
                    }

                    try { await TryApplyComponentDownStateAsync(CancellationToken.None).ConfigureAwait(false); } catch { }

                    IsAutoTestRunning = false;
                    IsBusy = false;
                }
            }
            finally
            {
                _autoTestLock.Release();
            }
        }

        private void SetLastTestResult(string result)
        {
            PreviousTestTime = LastTestTime;
            PreviousTestResult = LastTestResult;
            LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            LastTestResult = result;
        }

        private async Task StopAutoTestAsync()
        {
            try
            {
                _autoTestCts?.Cancel();
            }
            catch
            {
            }

            await Task.CompletedTask;
        }

        private static string FormatBytes(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                return string.Empty;
            return string.Join(" ", bytes.Select(b => b.ToString("X2")));
        }

        private static string FormatBytesHex(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                return string.Empty;
            return string.Join(string.Empty, bytes.Select(b => b.ToString("X2")));
        }

        public void Dispose()
        {
            try
            {
                _autoTestCts?.Cancel();
            }
            catch
            {
            }

            try
            {
                _autoTestCts?.Dispose();
            }
            catch
            {
            }

            try
            {
                _simulation.Dispose();
            }
            catch
            {
            }

            try
            {
                _arincOpLock.Dispose();
                _manualTestLock.Dispose();
                _autoTestLock.Dispose();
                _matrixSwitchLock.Dispose();
            }
            catch
            {
            }
        }
    }
}
