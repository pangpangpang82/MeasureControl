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
using MeasureControl.Simulations.A_C_6_18_1_1;

namespace MeasureControl.ViewModels.SingleBoardTest.AirController
{
    public sealed class A_C_6_18_1_1ViewModel : BindableBase, IDisposable
    {
        private const string FixedTxChannel = "429_CH5";
        private const string FixedRxChannel = "429_CH2";

        private static readonly byte[] EnterAtpCommand8 = { 0x30, 0x01, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] ExitAtpCommand8 = { 0x30, 0x02, 0x02, 0x01, 0x00, 0x00, 0x00, 0x00 };

        private static readonly byte[] TestCommandTemplate8 = { 0x23, 0x02, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00 };

        private const double VoltageLowerLimit = 3.0;
        private const double VoltageUpperLimit = 3.6;

        private const string FixedDmmIpAddress = "192.168.1.13";
        private const string MatrixIpAddress = "192.168.1.3";
        private const int MatrixSlotIndex = 4;

        private readonly A_C_6_18_1_1Simulation _simulation = new A_C_6_18_1_1Simulation();
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

        private double? _j226Voltage;
        private string _j226VoltageText;
        private string _j226JudgeText;

        private double? _j227Voltage;
        private string _j227VoltageText;
        private string _j227JudgeText;

        private double? _j228Voltage;
        private string _j228VoltageText;
        private string _j228JudgeText;

        private double? _j165Voltage;
        private string _j165VoltageText;
        private string _j165JudgeText;

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

        public A_C_6_18_1_1ViewModel()
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
            TestCommandXxLowNibble = "1";
            J226VoltageText = "--";
            J226JudgeText = "--";
            J227VoltageText = "--";
            J227JudgeText = "--";
            J228VoltageText = "--";
            J228JudgeText = "--";
            J165VoltageText = "--";
            J165JudgeText = "--";

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
            MeasureJ227VoltageCommand = new DelegateCommand(async () => await OnMeasureJ227VoltageAsync());
            MeasureJ228VoltageCommand = new DelegateCommand(async () => await OnMeasureJ228VoltageAsync());
            MeasureJ165VoltageCommand = new DelegateCommand(async () => await OnMeasureJ165VoltageAsync());
            SendExitAtpCommand = new DelegateCommand(async () => await OnSendExitAtpAsync());
            ClearLogCommand = new DelegateCommand(() => Logs.Clear());
        }

        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();

        public DelegateCommand ManualTestCommand { get; }
        public DelegateCommand AutoTestCommand { get; }
        public DelegateCommand SendEnterAtpCommand { get; }
        public DelegateCommand SendTestCommand { get; }
        public DelegateCommand MeasureVoltageCommand { get; }
        public DelegateCommand MeasureJ227VoltageCommand { get; }
        public DelegateCommand MeasureJ228VoltageCommand { get; }
        public DelegateCommand MeasureJ165VoltageCommand { get; }
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

        public string J226VoltageText
        {
            get => _j226VoltageText;
            private set => SetProperty(ref _j226VoltageText, value);
        }

        public string J226JudgeText
        {
            get => _j226JudgeText;
            private set => SetProperty(ref _j226JudgeText, value);
        }

        public string J227VoltageText
        {
            get => _j227VoltageText;
            private set => SetProperty(ref _j227VoltageText, value);
        }

        public string J227JudgeText
        {
            get => _j227JudgeText;
            private set => SetProperty(ref _j227JudgeText, value);
        }

        public string J228VoltageText
        {
            get => _j228VoltageText;
            private set => SetProperty(ref _j228VoltageText, value);
        }

        public string J228JudgeText
        {
            get => _j228JudgeText;
            private set => SetProperty(ref _j228JudgeText, value);
        }

        public string J165VoltageText
        {
            get => _j165VoltageText;
            private set => SetProperty(ref _j165VoltageText, value);
        }

        public string J165JudgeText
        {
            get => _j165JudgeText;
            private set => SetProperty(ref _j165JudgeText, value);
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
            TestCommandDisplayText = "0x23 02 01 01 00 00 00 00";
        }

        private byte[] BuildTestCommand8OrNull(out string error)
        {
            error = null;
            return (byte[])TestCommandTemplate8.Clone();
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
                    J226VoltageText = "--";
                    J226JudgeText = "--";
                    _j226Voltage = null;
                    J227VoltageText = "--";
                    J227JudgeText = "--";
                    _j227Voltage = null;
                    J228VoltageText = "--";
                    J228JudgeText = "--";
                    _j228Voltage = null;
                    J165VoltageText = "--";
                    J165JudgeText = "--";
                    _j165Voltage = null;
                    LastTestTime = "--";
                    LastTestResult = "--";

                    _simulation.IsRealProduct = true;
                    _simulation.ArincRate = ArincRate;

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试启动({(_simulation.IsRealProduct ? "真实产品模式" : "仿真模式")})：开始打开设备");

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
                    // 断开所有可能的矩阵开关连接
                    try { await DisconnectMatrixAsync("J226", GetMatrixOpsForJ226(), CancellationToken.None); } catch { }
                    try { await DisconnectMatrixAsync("J227", GetMatrixOpsForJ227(), CancellationToken.None); } catch { }
                    try { await DisconnectMatrixAsync("J228", GetMatrixOpsForJ228(), CancellationToken.None); } catch { }
                    try { await DisconnectMatrixAsync("J165", GetMatrixOpsForJ165(), CancellationToken.None); } catch { }
                }
                catch (Exception ex)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 停止失败: {ex.Message}");
                }
                finally
                {
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

            await _arincOpLock.WaitAsync();
            try
            {
                IsBusy = true;
                try
                {
                    await _simulation.ClearRxFifoAsync(TestRxChannel);
                    await Task.Delay(20);

                    var token = CancellationToken.None;

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 发送进入ATP：{FormatBytes(EnterAtpCommand8)}");
                    await _simulation.SendBenchCommandOnlyAsync(TestTxChannel, EnterAtpCommand8, msg => AddLog(msg), token);
                    await Task.Delay(50);
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 进入ATP指令已发送（不等待回读）");
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

            await _arincOpLock.WaitAsync();
            try
            {
                IsBusy = true;
                try
                {
                    await _simulation.ClearRxFifoAsync(TestRxChannel);
                    await Task.Delay(20);

                    var token = CancellationToken.None;

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 发送退出ATP：{FormatBytes(ExitAtpCommand8)}");
                    await _simulation.SendBenchCommandOnlyAsync(TestTxChannel, ExitAtpCommand8, msg => AddLog(msg), token);
                    await Task.Delay(50);
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 退出ATP指令已发送（不等待回读）");
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
                    var cmd8 = BuildTestCommand8OrNull(out var err);
                    if (cmd8 == null)
                    {
                        AddLog($"[{DateTime.Now:HH:mm:ss}] {err}");
                        return;
                    }

                    var token = CancellationToken.None;

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
                    var v = await ReadDmmVoltageAsync("J226", GetMatrixOpsForJ226(), token);
                    _j226Voltage = v;

                    if (v.HasValue)
                    {
                        J226VoltageText = $"{v.Value:0.00000} V";
                        bool pass = v.Value >= VoltageLowerLimit && v.Value <= VoltageUpperLimit;
                        J226JudgeText = pass ? "PASS" : "FAIL";
                        AddLog($"[{DateTime.Now:HH:mm:ss}] J226 电压={v.Value:0.00000} V, 判据[{VoltageLowerLimit:0.0},{VoltageUpperLimit:0.0}]V -> {J226JudgeText}");
                    }
                    else
                    {
                        J226VoltageText = "--";
                        J226JudgeText = "FAIL";
                        AddLog($"[{DateTime.Now:HH:mm:ss}] J226 电压测量无有效值");
                    }
                }
                finally
                {
                    IsBusy = false;
                }
                CheckManualTestResult();
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

        private async Task OnMeasureJ227VoltageAsync()
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
                    var v = await ReadDmmVoltageAsync("J227", GetMatrixOpsForJ227(), token);
                    _j227Voltage = v;

                    if (v.HasValue)
                    {
                        J227VoltageText = $"{v.Value:0.00000} V";
                        bool pass = v.Value >= VoltageLowerLimit && v.Value <= VoltageUpperLimit;
                        J227JudgeText = pass ? "PASS" : "FAIL";
                        AddLog($"[{DateTime.Now:HH:mm:ss}] J227 电压={v.Value:0.00000} V, 判据[{VoltageLowerLimit:0.0},{VoltageUpperLimit:0.0}]V -> {J227JudgeText}");
                    }
                    else
                    {
                        J227VoltageText = "--";
                        J227JudgeText = "FAIL";
                        AddLog($"[{DateTime.Now:HH:mm:ss}] J227 电压测量无有效值");
                    }
                }
                finally
                {
                    IsBusy = false;
                }
                CheckManualTestResult();
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

        private async Task OnMeasureJ228VoltageAsync()
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
                    var v = await ReadDmmVoltageAsync("J228", GetMatrixOpsForJ228(), token);
                    _j228Voltage = v;

                    if (v.HasValue)
                    {
                        J228VoltageText = $"{v.Value:0.00000} V";
                        bool pass = v.Value >= VoltageLowerLimit && v.Value <= VoltageUpperLimit;
                        J228JudgeText = pass ? "PASS" : "FAIL";
                        AddLog($"[{DateTime.Now:HH:mm:ss}] J228 电压={v.Value:0.00000} V, 判据[{VoltageLowerLimit:0.0},{VoltageUpperLimit:0.0}]V -> {J228JudgeText}");
                    }
                    else
                    {
                        J228VoltageText = "--";
                        J228JudgeText = "FAIL";
                        AddLog($"[{DateTime.Now:HH:mm:ss}] J228 电压测量无有效值");
                    }
                }
                finally
                {
                    IsBusy = false;
                }
                CheckManualTestResult();
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

        private async Task OnMeasureJ165VoltageAsync()
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
                    var v = await ReadDmmVoltageAsync("J165", GetMatrixOpsForJ165(), token);
                    _j165Voltage = v;

                    if (v.HasValue)
                    {
                        J165VoltageText = $"{v.Value:0.00000} V";
                        bool pass = v.Value < 0;
                        J165JudgeText = pass ? "PASS" : "FAIL";
                        AddLog($"[{DateTime.Now:HH:mm:ss}] J165 电压={v.Value:0.00000} V, 判据[<0]V -> {J165JudgeText}");
                    }
                    else
                    {
                        J165VoltageText = "--";
                        J165JudgeText = "FAIL";
                        AddLog($"[{DateTime.Now:HH:mm:ss}] J165 电压测量无有效值");
                    }
                }
                finally
                {
                    IsBusy = false;
                }
                CheckManualTestResult();
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

        private void CheckManualTestResult()
        {
            if (_j226Voltage.HasValue && _j227Voltage.HasValue && _j228Voltage.HasValue && _j165Voltage.HasValue)
            {
                bool pass = (_j226Voltage.Value >= VoltageLowerLimit && _j226Voltage.Value <= VoltageUpperLimit) &&
                             (_j227Voltage.Value >= VoltageLowerLimit && _j227Voltage.Value <= VoltageUpperLimit) &&
                             (_j228Voltage.Value >= VoltageLowerLimit && _j228Voltage.Value <= VoltageUpperLimit) &&
                             (_j165Voltage.Value < 0);
                SetLastTestResult(pass ? "PASS" : "FAIL");
                AddLog($"[{DateTime.Now:HH:mm:ss}] 所有4个点测量完成，最终测试结果：{LastTestResult}");
            }
            else
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 测量成功，等待其它点全部测量后再判定最终结果...");
            }
        }

        private async Task<double?> ReadDmmVoltageAsync(string pointName, (string inNode, string outNode, int slot, int? basePort)[] ops, CancellationToken token)
        {
            bool matrixOk = await ConnectMatrixAsync(pointName, ops, token);
            if (!matrixOk)
                throw new InvalidOperationException("矩阵开关通路建立失败");

            AddLog($"[{DateTime.Now:HH:mm:ss}] {pointName} 延时2秒等待波形稳定...");
            await Task.Delay(2000, token);

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

        private static (string inNode, string outNode, int slot, int? basePort)[] GetMatrixOpsForJ226()
        {
            return new (string inNode, string outNode, int slot, int? basePort)[]
            {
                ("I0", "O51", 3, 50300),
                ("I4", "O11", MatrixSlotIndex, null)
            };
        }

        private static (string inNode, string outNode, int slot, int? basePort)[] GetMatrixOpsForJ227()
        {
            return new (string inNode, string outNode, int slot, int? basePort)[]
            {
                ("I0", "O52", 3, 50300),
                ("I4", "O11", MatrixSlotIndex, null)
            };
        }

        private static (string inNode, string outNode, int slot, int? basePort)[] GetMatrixOpsForJ228()
        {
            return new (string inNode, string outNode, int slot, int? basePort)[]
            {
                ("I0", "O53", 3, 50300),
                ("I4", "O11", MatrixSlotIndex, null)
            };
        }

        private static (string inNode, string outNode, int slot, int? basePort)[] GetMatrixOpsForJ165()
        {
            return new (string inNode, string outNode, int slot, int? basePort)[]
            {
                ("I0", "O28", 9, null),
                ("I4", "O7", MatrixSlotIndex, null)
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
                    J226VoltageText = "--";
                    J226JudgeText = "--";
                    _j226Voltage = null;
                    J227VoltageText = "--";
                    J227JudgeText = "--";
                    _j227Voltage = null;
                    J228VoltageText = "--";
                    J228JudgeText = "--";
                    _j228Voltage = null;
                    J165VoltageText = "--";
                    J165JudgeText = "--";
                    _j165Voltage = null;
                    LastTestTime = "--";
                    LastTestResult = "--";

                    _autoTestCts?.Cancel();
                    _autoTestCts?.Dispose();
                    _autoTestCts = new CancellationTokenSource();

                    var token = _autoTestCts.Token;

                    _simulation.IsRealProduct = true;
                    _simulation.ArincRate = ArincRate;

                    AddLog($"[{DateTime.Now:HH:mm:ss}] ========== 自动测试开始 ==========");
                    await _simulation.StartAsync(TestTxChannel, TestRxChannel, msg => AddLog(msg));

                    await _simulation.ClearRxFifoAsync(TestRxChannel);
                    await Task.Delay(20, token);

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 步骤1：进入ATP");
                    await _simulation.SendBenchCommandOnlyAsync(TestTxChannel, EnterAtpCommand8, msg => AddLog(msg), token);
                    await Task.Delay(50, token);

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

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 步骤3：万用表测量J226电压");
                    var v226 = await ReadDmmVoltageAsync("J226", GetMatrixOpsForJ226(), token);
                    _j226Voltage = v226;
                    if (v226.HasValue)
                    {
                        J226VoltageText = $"{v226.Value:0.00000} V";
                        bool pass = v226.Value >= VoltageLowerLimit && v226.Value <= VoltageUpperLimit;
                        J226JudgeText = pass ? "PASS" : "FAIL";
                        passAll &= pass;
                    }
                    else
                    {
                        J226VoltageText = "--";
                        J226JudgeText = "FAIL";
                        passAll = false;
                    }

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 步骤4：万用表测量J227电压");
                    var v227 = await ReadDmmVoltageAsync("J227", GetMatrixOpsForJ227(), token);
                    _j227Voltage = v227;
                    if (v227.HasValue)
                    {
                        J227VoltageText = $"{v227.Value:0.00000} V";
                        bool pass = v227.Value >= VoltageLowerLimit && v227.Value <= VoltageUpperLimit;
                        J227JudgeText = pass ? "PASS" : "FAIL";
                        passAll &= pass;
                    }
                    else
                    {
                        J227VoltageText = "--";
                        J227JudgeText = "FAIL";
                        passAll = false;
                    }

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 步骤5：万用表测量J228电压");
                    var v228 = await ReadDmmVoltageAsync("J228", GetMatrixOpsForJ228(), token);
                    _j228Voltage = v228;
                    if (v228.HasValue)
                    {
                        J228VoltageText = $"{v228.Value:0.00000} V";
                        bool pass = v228.Value >= VoltageLowerLimit && v228.Value <= VoltageUpperLimit;
                        J228JudgeText = pass ? "PASS" : "FAIL";
                        passAll &= pass;
                    }
                    else
                    {
                        J228VoltageText = "--";
                        J228JudgeText = "FAIL";
                        passAll = false;
                    }

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 步骤6：万用表测量J165电压");
                    var v165 = await ReadDmmVoltageAsync("J165", GetMatrixOpsForJ165(), token);
                    _j165Voltage = v165;
                    if (v165.HasValue)
                    {
                        J165VoltageText = $"{v165.Value:0.00000} V";
                        bool pass = v165.Value < 0;
                        J165JudgeText = pass ? "PASS" : "FAIL";
                        passAll &= pass;
                    }
                    else
                    {
                        J165VoltageText = "--";
                        J165JudgeText = "FAIL";
                        passAll = false;
                    }

                    SetLastTestResult(passAll ? "PASS" : "FAIL");

                    await _simulation.ClearRxFifoAsync(TestRxChannel);
                    await Task.Delay(20, token);

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 步骤7：退出ATP");
                    await _simulation.SendBenchCommandOnlyAsync(TestTxChannel, ExitAtpCommand8, msg => AddLog(msg), token);
                    await Task.Delay(50, token);

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

            // 断开所有可能的矩阵开关连接
            try { await DisconnectMatrixAsync("J226", GetMatrixOpsForJ226(), CancellationToken.None); } catch { }
            try { await DisconnectMatrixAsync("J227", GetMatrixOpsForJ227(), CancellationToken.None); } catch { }
            try { await DisconnectMatrixAsync("J228", GetMatrixOpsForJ228(), CancellationToken.None); } catch { }
            try { await DisconnectMatrixAsync("J165", GetMatrixOpsForJ165(), CancellationToken.None); } catch { }
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
