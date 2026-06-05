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
        private const string FixedTxChannel = "429_CH5";
        private const string FixedRxChannel = "429_CH2";

        private static readonly byte[] EnterAtpCommand8 = { 0x30, 0x01, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] ExitAtpCommand8 = { 0x30, 0x02, 0x02, 0x01, 0x00, 0x00, 0x00, 0x00 };

        private static readonly byte[] TestCommand8 = { 0x23, 0x02, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00 };

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
            _testTxChannel = FixedTxChannel;
            _testRxChannel = FixedRxChannel;

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

            IsRealProduct = true;

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
                    try { await DisconnectMatrixAsync("J233", GetMatrixOpsForJ233(), CancellationToken.None); } catch { }
                    try { await DisconnectMatrixAsync("J234", GetMatrixOpsForJ234(), CancellationToken.None); } catch { }
                    try { await DisconnectMatrixAsync("J235", GetMatrixOpsForJ235(), CancellationToken.None); } catch { }
                    try { await DisconnectMatrixAsync("J172", GetMatrixOpsForJ172(), CancellationToken.None); } catch { }
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
                        bool pass = v.Value < 0;
                        J172JudgeText = pass ? "PASS" : "FAIL";
                        AddLog($"[{DateTime.Now:HH:mm:ss}] J172 电压={v.Value:0.00000} V, 判据[<0]V -> {J172JudgeText}");
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
            if (_j233Voltage.HasValue && _j234Voltage.HasValue && _j235Voltage.HasValue && _j172Voltage.HasValue)
            {
                bool pass = (_j233Voltage.Value >= VoltageLowerLimit && _j233Voltage.Value <= VoltageUpperLimit) &&
                             (_j234Voltage.Value >= VoltageLowerLimit && _j234Voltage.Value <= VoltageUpperLimit) &&
                             (_j235Voltage.Value >= VoltageLowerLimit && _j235Voltage.Value <= VoltageUpperLimit) &&
                             (_j172Voltage.Value < 0);
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
                        bool pass = v172.Value < 0;
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
            try { await DisconnectMatrixAsync("J233", GetMatrixOpsForJ233(), CancellationToken.None); } catch { }
            try { await DisconnectMatrixAsync("J234", GetMatrixOpsForJ234(), CancellationToken.None); } catch { }
            try { await DisconnectMatrixAsync("J235", GetMatrixOpsForJ235(), CancellationToken.None); } catch { }
            try { await DisconnectMatrixAsync("J172", GetMatrixOpsForJ172(), CancellationToken.None); } catch { }
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
