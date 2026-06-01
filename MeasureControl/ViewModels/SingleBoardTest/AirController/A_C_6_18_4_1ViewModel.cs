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
using MeasureControl.Simulations.A_C_6_18_4_1;

namespace MeasureControl.ViewModels.SingleBoardTest.AirController
{
    public sealed class A_C_6_18_4_1ViewModel : BindableBase, IDisposable
    {
        private const string FixedTxChannel = "429_CH5";
        private const string FixedRxChannel = "429_CH2";

        private static readonly byte[] EnterAtpCommand8 = { 0x30, 0x01, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] ExitAtpCommand8 = { 0x30, 0x02, 0x02, 0x01, 0x00, 0x00, 0x00, 0x00 };

        private static readonly byte[] TestCommand8 = { 0x23, 0x04, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00 };

        private const double VoltageLowerLimit = 3.0;
        private const double VoltageUpperLimit = 3.6;

        private const string FixedDmmIpAddress = "192.168.1.13";
        private const string MatrixIpAddress = "192.168.1.3";
        private const int MatrixSlotIndex = 4;

        private readonly A_C_6_18_4_1Simulation _simulation = new A_C_6_18_4_1Simulation();
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

        private double? _j174Voltage;
        private string _j174VoltageText;
        private string _j174JudgeText;

        private double? _j175Voltage;
        private string _j175VoltageText;
        private string _j175JudgeText;

        private double? _j176Voltage;
        private string _j176VoltageText;
        private string _j176JudgeText;

        private double? _j238Voltage;
        private string _j238VoltageText;
        private string _j238JudgeText;

        private string _lastTestTime;
        private string _lastTestResult;
        private string _previousTestTime;
        private string _previousTestResult;

        private bool _isManualTestRunning;
        private bool _isAutoTestRunning;
        private bool _isBusy;

        private double _arincRate = 100000.0;
        private bool _isRealProduct;

        public A_C_6_18_4_1ViewModel()
        {
            _testTxChannel = FixedTxChannel;
            _testRxChannel = FixedRxChannel;

            _enterAtpTxChannel = _testTxChannel;
            _enterAtpRxChannel = _testRxChannel;
            _exitAtpTxChannel = _testTxChannel;
            _exitAtpRxChannel = _testRxChannel;

            EnterAtpRxDataText = "--";
            ExitAtpRxDataText = "--";

            J174VoltageText = "--";
            J174JudgeText = "--";
            J175VoltageText = "--";
            J175JudgeText = "--";
            J176VoltageText = "--";
            J176JudgeText = "--";
            J238VoltageText = "--";
            J238JudgeText = "--";

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
            MeasureJ175VoltageCommand = new DelegateCommand(async () => await OnMeasureJ175VoltageAsync());
            MeasureJ176VoltageCommand = new DelegateCommand(async () => await OnMeasureJ176VoltageAsync());
            MeasureJ238VoltageCommand = new DelegateCommand(async () => await OnMeasureJ238VoltageAsync());
            SendExitAtpCommand = new DelegateCommand(async () => await OnSendExitAtpAsync());
            ClearLogCommand = new DelegateCommand(() => Logs.Clear());
        }

        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();

        public DelegateCommand ManualTestCommand { get; }
        public DelegateCommand AutoTestCommand { get; }
        public DelegateCommand SendEnterAtpCommand { get; }
        public DelegateCommand SendTestCommand { get; }
        public DelegateCommand MeasureVoltageCommand { get; }
        public DelegateCommand MeasureJ175VoltageCommand { get; }
        public DelegateCommand MeasureJ176VoltageCommand { get; }
        public DelegateCommand MeasureJ238VoltageCommand { get; }
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

        public string J174VoltageText
        {
            get => _j174VoltageText;
            private set => SetProperty(ref _j174VoltageText, value);
        }

        public string J174JudgeText
        {
            get => _j174JudgeText;
            private set => SetProperty(ref _j174JudgeText, value);
        }

        public string J175VoltageText
        {
            get => _j175VoltageText;
            private set => SetProperty(ref _j175VoltageText, value);
        }

        public string J175JudgeText
        {
            get => _j175JudgeText;
            private set => SetProperty(ref _j175JudgeText, value);
        }

        public string J176VoltageText
        {
            get => _j176VoltageText;
            private set => SetProperty(ref _j176VoltageText, value);
        }

        public string J176JudgeText
        {
            get => _j176JudgeText;
            private set => SetProperty(ref _j176JudgeText, value);
        }

        public string J238VoltageText
        {
            get => _j238VoltageText;
            private set => SetProperty(ref _j238VoltageText, value);
        }

        public string J238JudgeText
        {
            get => _j238JudgeText;
            private set => SetProperty(ref _j238JudgeText, value);
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
                    J174VoltageText = "--";
                    J174JudgeText = "--";
                    _j174Voltage = null;
                    J175VoltageText = "--";
                    J175JudgeText = "--";
                    _j175Voltage = null;
                    J176VoltageText = "--";
                    J176JudgeText = "--";
                    _j176Voltage = null;
                    J238VoltageText = "--";
                    J238JudgeText = "--";
                    _j238Voltage = null;
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
                    var v = await ReadDmmVoltageAsync("J174", GetMatrixOpsForJ174(), token);
                    _j174Voltage = v;

                    if (v.HasValue)
                    {
                        J174VoltageText = $"{v.Value:0.00000} V";
                        bool pass = v.Value >= VoltageLowerLimit && v.Value <= VoltageUpperLimit;
                        J174JudgeText = pass ? "PASS" : "FAIL";
                        AddLog($"[{DateTime.Now:HH:mm:ss}] J174 电压={v.Value:0.00000} V, 判据[{VoltageLowerLimit:0.0},{VoltageUpperLimit:0.0}]V -> {J174JudgeText}");
                    }
                    else
                    {
                        J174VoltageText = "--";
                        J174JudgeText = "FAIL";
                        AddLog($"[{DateTime.Now:HH:mm:ss}] J174 电压测量无有效值");
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

        private async Task OnMeasureJ175VoltageAsync()
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
                    var v = await ReadDmmVoltageAsync("J175", GetMatrixOpsForJ175(), token);
                    _j175Voltage = v;

                    if (v.HasValue)
                    {
                        J175VoltageText = $"{v.Value:0.00000} V";
                        bool pass = v.Value >= VoltageLowerLimit && v.Value <= VoltageUpperLimit;
                        J175JudgeText = pass ? "PASS" : "FAIL";
                        AddLog($"[{DateTime.Now:HH:mm:ss}] J175 电压={v.Value:0.00000} V, 判据[{VoltageLowerLimit:0.0},{VoltageUpperLimit:0.0}]V -> {J175JudgeText}");
                    }
                    else
                    {
                        J175VoltageText = "--";
                        J175JudgeText = "FAIL";
                        AddLog($"[{DateTime.Now:HH:mm:ss}] J175 电压测量无有效值");
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

        private async Task OnMeasureJ176VoltageAsync()
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
                    var v = await ReadDmmVoltageAsync("J176", GetMatrixOpsForJ176(), token);
                    _j176Voltage = v;

                    if (v.HasValue)
                    {
                        J176VoltageText = $"{v.Value:0.00000} V";
                        bool pass = v.Value >= VoltageLowerLimit && v.Value <= VoltageUpperLimit;
                        J176JudgeText = pass ? "PASS" : "FAIL";
                        AddLog($"[{DateTime.Now:HH:mm:ss}] J176 电压={v.Value:0.00000} V, 判据[{VoltageLowerLimit:0.0},{VoltageUpperLimit:0.0}]V -> {J176JudgeText}");
                    }
                    else
                    {
                        J176VoltageText = "--";
                        J176JudgeText = "FAIL";
                        AddLog($"[{DateTime.Now:HH:mm:ss}] J176 电压测量无有效值");
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

        private async Task OnMeasureJ238VoltageAsync()
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
                    var v = await ReadDmmVoltageAsync("J238", GetMatrixOpsForJ238(), token);
                    _j238Voltage = v;

                    if (v.HasValue)
                    {
                        J238VoltageText = $"{v.Value:0.00000} V";
                        bool pass = v.Value >= VoltageLowerLimit && v.Value <= VoltageUpperLimit;
                        J238JudgeText = pass ? "PASS" : "FAIL";
                        AddLog($"[{DateTime.Now:HH:mm:ss}] J238 电压={v.Value:0.00000} V, 判据[{VoltageLowerLimit:0.0},{VoltageUpperLimit:0.0}]V -> {J238JudgeText}");
                    }
                    else
                    {
                        J238VoltageText = "--";
                        J238JudgeText = "FAIL";
                        AddLog($"[{DateTime.Now:HH:mm:ss}] J238 电压测量无有效值");
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
            if (_j174Voltage.HasValue && _j175Voltage.HasValue && _j176Voltage.HasValue && _j238Voltage.HasValue)
            {
                bool pass = (_j174Voltage.Value >= VoltageLowerLimit && _j174Voltage.Value <= VoltageUpperLimit) &&
                             (_j175Voltage.Value >= VoltageLowerLimit && _j175Voltage.Value <= VoltageUpperLimit) &&
                             (_j176Voltage.Value >= VoltageLowerLimit && _j176Voltage.Value <= VoltageUpperLimit) &&
                             (_j238Voltage.Value >= VoltageLowerLimit && _j238Voltage.Value <= VoltageUpperLimit);
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

        private static (string inNode, string outNode, int slot, int? basePort)[] GetMatrixOpsForJ174()
        {
            return new (string inNode, string outNode, int slot, int? basePort)[]
            {
                ("I0", "O45", 3, 50300),
                ("I4", "O11", MatrixSlotIndex, null)
            };
        }

        private static (string inNode, string outNode, int slot, int? basePort)[] GetMatrixOpsForJ175()
        {
            return new (string inNode, string outNode, int slot, int? basePort)[]
            {
                ("I0", "O46", 3, 50300),
                ("I4", "O11", MatrixSlotIndex, null)
            };
        }

        private static (string inNode, string outNode, int slot, int? basePort)[] GetMatrixOpsForJ176()
        {
            return new (string inNode, string outNode, int slot, int? basePort)[]
            {
                ("I0", "O47", 3, 50300),
                ("I4", "O11", MatrixSlotIndex, null)
            };
        }

        private static (string inNode, string outNode, int slot, int? basePort)[] GetMatrixOpsForJ238()
        {
            return new (string inNode, string outNode, int slot, int? basePort)[]
            {
                ("I0", "O60", 3, 50300),
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
                    J174VoltageText = "--";
                    J174JudgeText = "--";
                    _j174Voltage = null;
                    J175VoltageText = "--";
                    J175JudgeText = "--";
                    _j175Voltage = null;
                    J176VoltageText = "--";
                    J176JudgeText = "--";
                    _j176Voltage = null;
                    J238VoltageText = "--";
                    J238JudgeText = "--";
                    _j238Voltage = null;
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

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 步骤3：万用表测量J174电压");
                    var v174 = await ReadDmmVoltageAsync("J174", GetMatrixOpsForJ174(), token);
                    _j174Voltage = v174;
                    if (v174.HasValue)
                    {
                        J174VoltageText = $"{v174.Value:0.00000} V";
                        bool pass = v174.Value >= VoltageLowerLimit && v174.Value <= VoltageUpperLimit;
                        J174JudgeText = pass ? "PASS" : "FAIL";
                        passAll &= pass;
                    }
                    else
                    {
                        J174VoltageText = "--";
                        J174JudgeText = "FAIL";
                        passAll = false;
                    }

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 步骤4：万用表测量J175电压");
                    var v175 = await ReadDmmVoltageAsync("J175", GetMatrixOpsForJ175(), token);
                    _j175Voltage = v175;
                    if (v175.HasValue)
                    {
                        J175VoltageText = $"{v175.Value:0.00000} V";
                        bool pass = v175.Value >= VoltageLowerLimit && v175.Value <= VoltageUpperLimit;
                        J175JudgeText = pass ? "PASS" : "FAIL";
                        passAll &= pass;
                    }
                    else
                    {
                        J175VoltageText = "--";
                        J175JudgeText = "FAIL";
                        passAll = false;
                    }

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 步骤5：万用表测量J176电压");
                    var v176 = await ReadDmmVoltageAsync("J176", GetMatrixOpsForJ176(), token);
                    _j176Voltage = v176;
                    if (v176.HasValue)
                    {
                        J176VoltageText = $"{v176.Value:0.00000} V";
                        bool pass = v176.Value >= VoltageLowerLimit && v176.Value <= VoltageUpperLimit;
                        J176JudgeText = pass ? "PASS" : "FAIL";
                        passAll &= pass;
                    }
                    else
                    {
                        J176VoltageText = "--";
                        J176JudgeText = "FAIL";
                        passAll = false;
                    }

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 步骤6：万用表测量J238电压");
                    var v238 = await ReadDmmVoltageAsync("J238", GetMatrixOpsForJ238(), token);
                    _j238Voltage = v238;
                    if (v238.HasValue)
                    {
                        J238VoltageText = $"{v238.Value:0.00000} V";
                        bool pass = v238.Value >= VoltageLowerLimit && v238.Value <= VoltageUpperLimit;
                        J238JudgeText = pass ? "PASS" : "FAIL";
                        passAll &= pass;
                    }
                    else
                    {
                        J238VoltageText = "--";
                        J238JudgeText = "FAIL";
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
