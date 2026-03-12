using Prism.Commands;
using Prism.Mvvm;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using MeasureControl.Helpers;
using MeasureControl.Services;
using MeasureControl.Services.HardwareApis;
using MeasureControl.Simulations.A_C_6_18_3_1;

namespace MeasureControl.ViewModels.SingleBoardTest.AirController
{
    public sealed class A_C_6_18_3_1ViewModel : BindableBase, IDisposable
    {
        private static readonly byte[] EnterAtpCommand8 = { 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] EnterAtpOk8 = { 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x02 };
        private static readonly byte[] ExitAtpCommand8 = { 0x00, 0x02, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01 };
        private static readonly byte[] ExitAtpOk8 = { 0x00, 0x02, 0x00, 0x01, 0x00, 0x00, 0x00, 0x03 };

        private static readonly byte[] TestCommand8 = { 0x23, 0x03, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00 };

        private const double VoltageLowerLimit = 3.0;
        private const double VoltageUpperLimit = 3.6;

        private const string FixedDmmIpAddress = "192.168.1.13";
        private const string MatrixIpAddress = "192.168.1.3";
        private const int MatrixSlotIndex = 4;

        private readonly A_C_6_18_3_1Simulation _simulation = new A_C_6_18_3_1Simulation();
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

        private double? _j233Voltage;
        private string _j233VoltageText;
        private string _j233JudgeText;

        private double? _j234Voltage;
        private string _j234VoltageText;
        private string _j234JudgeText;

        private double? _j235Voltage;
        private string _j235VoltageText;
        private string _j235JudgeText;

        private double? _j172Voltage;
        private string _j172VoltageText;
        private string _j172JudgeText;

        private string _lastTestTime;
        private string _lastTestResult;
        private string _previousTestTime;
        private string _previousTestResult;

        private bool _isManualTestRunning;
        private bool _isAutoTestRunning;
        private bool _isBusy;

        private double _arincRate = 100000.0;
        private bool _isRealProduct;

        public A_C_6_18_3_1ViewModel()
        {
            _testTxChannel = "429_CH0";
            _testRxChannel = "429_CH2";

            _enterAtpTxChannel = _testTxChannel;
            _enterAtpRxChannel = _testRxChannel;
            _exitAtpTxChannel = _testTxChannel;
            _exitAtpRxChannel = _testRxChannel;

            EnterAtpRxDataText = "--";
            ExitAtpRxDataText = "--";

            J233VoltageText = "--";
            J233JudgeText = "--";
            J234VoltageText = "--";
            J234JudgeText = "--";
            J235VoltageText = "--";
            J235JudgeText = "--";
            J172VoltageText = "--";
            J172JudgeText = "--";

            LastTestTime = "--";
            LastTestResult = "--";
            PreviousTestTime = "--";
            PreviousTestResult = "--";

            IsRealProduct = AppConstants.Arinc429IsRealProduct;

            ManualTestCommand = new DelegateCommand(OnManualTest);
            AutoTestCommand = new DelegateCommand(OnAutoTest);

            SendEnterAtpCommand = new DelegateCommand(async () => await OnSendEnterAtpAsync());
            SendTestCommand = new DelegateCommand(async () => await OnSendTestAsync());
            MeasureVoltageCommand = new DelegateCommand(async () => await OnMeasureVoltageAsync());
            MeasureJ234VoltageCommand = new DelegateCommand(async () => await OnMeasureJ234VoltageAsync());
            MeasureJ235VoltageCommand = new DelegateCommand(async () => await OnMeasureJ235VoltageAsync());
            MeasureJ172VoltageCommand = new DelegateCommand(async () => await OnMeasureJ172VoltageAsync());
            SendExitAtpCommand = new DelegateCommand(async () => await OnSendExitAtpAsync());
            ClearLogCommand = new DelegateCommand(() => Logs.Clear());
        }

        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();

        public DelegateCommand ManualTestCommand { get; }
        public DelegateCommand AutoTestCommand { get; }
        public DelegateCommand SendEnterAtpCommand { get; }
        public DelegateCommand SendTestCommand { get; }
        public DelegateCommand MeasureVoltageCommand { get; }
        public DelegateCommand MeasureJ234VoltageCommand { get; }
        public DelegateCommand MeasureJ235VoltageCommand { get; }
        public DelegateCommand MeasureJ172VoltageCommand { get; }
        public DelegateCommand SendExitAtpCommand { get; }
        public DelegateCommand ClearLogCommand { get; }

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

        public string J233VoltageText
        {
            get => _j233VoltageText;
            private set => SetProperty(ref _j233VoltageText, value);
        }

        public string J233JudgeText
        {
            get => _j233JudgeText;
            private set => SetProperty(ref _j233JudgeText, value);
        }

        public string J234VoltageText
        {
            get => _j234VoltageText;
            private set => SetProperty(ref _j234VoltageText, value);
        }

        public string J234JudgeText
        {
            get => _j234JudgeText;
            private set => SetProperty(ref _j234JudgeText, value);
        }

        public string J235VoltageText
        {
            get => _j235VoltageText;
            private set => SetProperty(ref _j235VoltageText, value);
        }

        public string J235JudgeText
        {
            get => _j235JudgeText;
            private set => SetProperty(ref _j235JudgeText, value);
        }

        public string J172VoltageText
        {
            get => _j172VoltageText;
            private set => SetProperty(ref _j172VoltageText, value);
        }

        public string J172JudgeText
        {
            get => _j172JudgeText;
            private set => SetProperty(ref _j172JudgeText, value);
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
                    J233VoltageText = "--";
                    J233JudgeText = "--";
                    _j233Voltage = null;
                    J234VoltageText = "--";
                    J234JudgeText = "--";
                    _j234Voltage = null;
                    J235VoltageText = "--";
                    J235JudgeText = "--";
                    _j235Voltage = null;
                    J172VoltageText = "--";
                    J172JudgeText = "--";
                    _j172Voltage = null;
                    LastTestTime = "--";
                    LastTestResult = "--";

                    _simulation.IsRealProduct = IsRealProduct;
                    _simulation.ArincRate = ArincRate;
                    _simulation.SimProductArincRate = ArincRate;
                    _simulation.SimProductRxChannelIndex = 4;
                    _simulation.SimProductTxChannelIndex = 5;

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

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 发送测试指令：{FormatBytes(TestCommand8)}（无回包）");
                    await _simulation.SendBenchCommandOnlyAsync(TestTxChannel, TestCommand8, msg => AddLog(msg), token);
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
                    var v = await ReadDmmVoltageAsync("J233", GetMatrixOpsForJ233(), token);
                    _j233Voltage = v;

                    if (v.HasValue)
                    {
                        J233VoltageText = $"{v.Value:0.00000} V";
                        bool pass = v.Value >= VoltageLowerLimit && v.Value <= VoltageUpperLimit;
                        J233JudgeText = pass ? "PASS" : "FAIL";
                        AddLog($"[{DateTime.Now:HH:mm:ss}] J233 电压={v.Value:0.00000} V, 判据[{VoltageLowerLimit:0.0},{VoltageUpperLimit:0.0}]V -> {J233JudgeText}");
                    }
                    else
                    {
                        J233VoltageText = "--";
                        J233JudgeText = "FAIL";
                        AddLog($"[{DateTime.Now:HH:mm:ss}] J233 电压测量无有效值");
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

        private async Task OnMeasureJ234VoltageAsync()
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
                    var v = await ReadDmmVoltageAsync("J234", GetMatrixOpsForJ234(), token);
                    _j234Voltage = v;

                    if (v.HasValue)
                    {
                        J234VoltageText = $"{v.Value:0.00000} V";
                        bool pass = v.Value >= VoltageLowerLimit && v.Value <= VoltageUpperLimit;
                        J234JudgeText = pass ? "PASS" : "FAIL";
                        AddLog($"[{DateTime.Now:HH:mm:ss}] J234 电压={v.Value:0.00000} V, 判据[{VoltageLowerLimit:0.0},{VoltageUpperLimit:0.0}]V -> {J234JudgeText}");
                    }
                    else
                    {
                        J234VoltageText = "--";
                        J234JudgeText = "FAIL";
                        AddLog($"[{DateTime.Now:HH:mm:ss}] J234 电压测量无有效值");
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

        private async Task OnMeasureJ235VoltageAsync()
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
                    var v = await ReadDmmVoltageAsync("J235", GetMatrixOpsForJ235(), token);
                    _j235Voltage = v;

                    if (v.HasValue)
                    {
                        J235VoltageText = $"{v.Value:0.00000} V";
                        bool pass = v.Value >= VoltageLowerLimit && v.Value <= VoltageUpperLimit;
                        J235JudgeText = pass ? "PASS" : "FAIL";
                        AddLog($"[{DateTime.Now:HH:mm:ss}] J235 电压={v.Value:0.00000} V, 判据[{VoltageLowerLimit:0.0},{VoltageUpperLimit:0.0}]V -> {J235JudgeText}");
                    }
                    else
                    {
                        J235VoltageText = "--";
                        J235JudgeText = "FAIL";
                        AddLog($"[{DateTime.Now:HH:mm:ss}] J235 电压测量无有效值");
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

        private async Task OnMeasureJ172VoltageAsync()
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
                    var v = await ReadDmmVoltageAsync("J172", GetMatrixOpsForJ172(), token);
                    _j172Voltage = v;

                    if (v.HasValue)
                    {
                        J172VoltageText = $"{v.Value:0.00000} V";
                        bool pass = v.Value >= VoltageLowerLimit && v.Value <= VoltageUpperLimit;
                        J172JudgeText = pass ? "PASS" : "FAIL";
                        AddLog($"[{DateTime.Now:HH:mm:ss}] J172 电压={v.Value:0.00000} V, 判据[{VoltageLowerLimit:0.0},{VoltageUpperLimit:0.0}]V -> {J172JudgeText}");
                    }
                    else
                    {
                        J172VoltageText = "--";
                        J172JudgeText = "FAIL";
                        AddLog($"[{DateTime.Now:HH:mm:ss}] J172 电压测量无有效值");
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
            if (!IsRealProduct)
            {
                await Task.Delay(50, token);
                return 3.3;
            }

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

        private static (string inNode, string outNode, int slot, int? basePort)[] GetMatrixOpsForJ233()
        {
            return new (string inNode, string outNode, int slot, int? basePort)[]
            {
                ("I0", "O53", 3, 50300),
                ("I4", "O11", MatrixSlotIndex, null)
            };
        }

        private static (string inNode, string outNode, int slot, int? basePort)[] GetMatrixOpsForJ234()
        {
            return new (string inNode, string outNode, int slot, int? basePort)[]
            {
                ("I0", "O58", 3, 50300),
                ("I4", "O11", MatrixSlotIndex, null)
            };
        }

        private static (string inNode, string outNode, int slot, int? basePort)[] GetMatrixOpsForJ235()
        {
            return new (string inNode, string outNode, int slot, int? basePort)[]
            {
                ("I0", "O59", 3, 50300),
                ("I4", "O11", MatrixSlotIndex, null)
            };
        }

        private static (string inNode, string outNode, int slot, int? basePort)[] GetMatrixOpsForJ172()
        {
            return new (string inNode, string outNode, int slot, int? basePort)[]
            {
                ("I0", "O43", 3, 50300),
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
                    J233VoltageText = "--";
                    J233JudgeText = "--";
                    _j233Voltage = null;
                    J234VoltageText = "--";
                    J234JudgeText = "--";
                    _j234Voltage = null;
                    J235VoltageText = "--";
                    J235JudgeText = "--";
                    _j235Voltage = null;
                    J172VoltageText = "--";
                    J172JudgeText = "--";
                    _j172Voltage = null;
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

                    _simulation.IsRealProduct = IsRealProduct;
                    _simulation.ArincRate = ArincRate;
                    _simulation.SimProductArincRate = ArincRate;
                    _simulation.SimProductRxChannelIndex = 4;
                    _simulation.SimProductTxChannelIndex = 5;

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

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 步骤2：发送测试指令（无回包）");
                    await _simulation.SendBenchCommandOnlyAsync(TestTxChannel, TestCommand8, msg => AddLog(msg), token);

                    bool passAll = true;

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 步骤3：万用表测量J233电压");
                    var v233 = await ReadDmmVoltageAsync("J233", GetMatrixOpsForJ233(), token);
                    _j233Voltage = v233;
                    if (v233.HasValue)
                    {
                        J233VoltageText = $"{v233.Value:0.00000} V";
                        bool pass = v233.Value >= VoltageLowerLimit && v233.Value <= VoltageUpperLimit;
                        J233JudgeText = pass ? "PASS" : "FAIL";
                        passAll &= pass;
                    }
                    else
                    {
                        J233VoltageText = "--";
                        J233JudgeText = "FAIL";
                        passAll = false;
                    }

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 步骤4：万用表测量J234电压");
                    var v234 = await ReadDmmVoltageAsync("J234", GetMatrixOpsForJ234(), token);
                    _j234Voltage = v234;
                    if (v234.HasValue)
                    {
                        J234VoltageText = $"{v234.Value:0.00000} V";
                        bool pass = v234.Value >= VoltageLowerLimit && v234.Value <= VoltageUpperLimit;
                        J234JudgeText = pass ? "PASS" : "FAIL";
                        passAll &= pass;
                    }
                    else
                    {
                        J234VoltageText = "--";
                        J234JudgeText = "FAIL";
                        passAll = false;
                    }

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 步骤5：万用表测量J235电压");
                    var v235 = await ReadDmmVoltageAsync("J235", GetMatrixOpsForJ235(), token);
                    _j235Voltage = v235;
                    if (v235.HasValue)
                    {
                        J235VoltageText = $"{v235.Value:0.00000} V";
                        bool pass = v235.Value >= VoltageLowerLimit && v235.Value <= VoltageUpperLimit;
                        J235JudgeText = pass ? "PASS" : "FAIL";
                        passAll &= pass;
                    }
                    else
                    {
                        J235VoltageText = "--";
                        J235JudgeText = "FAIL";
                        passAll = false;
                    }

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 步骤6：万用表测量J172电压");
                    var v172 = await ReadDmmVoltageAsync("J172", GetMatrixOpsForJ172(), token);
                    _j172Voltage = v172;
                    if (v172.HasValue)
                    {
                        J172VoltageText = $"{v172.Value:0.00000} V";
                        bool pass = v172.Value >= VoltageLowerLimit && v172.Value <= VoltageUpperLimit;
                        J172JudgeText = pass ? "PASS" : "FAIL";
                        passAll &= pass;
                    }
                    else
                    {
                        J172VoltageText = "--";
                        J172JudgeText = "FAIL";
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
