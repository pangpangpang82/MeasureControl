using Prism.Commands;
using Prism.Mvvm;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using MeasureControl.Services;
using MeasureControl.Simulations.AC_6_4;
using NationalInstruments.Visa;
using Ivi.Visa;

namespace MeasureControl.ViewModels.SingleBoardTest.AirController
{
    public class Pot5VSupplyTestViewModel : BindableBase
    {
        private const byte DefaultLabel = 0x6A;

        private static readonly byte[] EnterAtpCommand = { 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] EnterAtpOk = { 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x02 };
        private static readonly byte[] ExitAtpCommand = { 0x00, 0x02, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01 };
        private static readonly byte[] ExitAtpOk = { 0x00, 0x02, 0x00, 0x01, 0x00, 0x00, 0x00, 0x03 };

        private static readonly byte[] Ab5VPotSupplyCommand = { 0x01, 0x02, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00 };

        private const int EnterAtpMaxRetries = 3;
        private const int EnterAtpTimeoutMs = 3000;

        private const double DmmMin = 4.5;
        private const double DmmMax = 5.5;
        private const double PotSupMin = 2.25;
        private const double PotSupMax = 2.75;

        private readonly SemaphoreSlim _manualTestLock = new SemaphoreSlim(1, 1);
        private CancellationTokenSource _opCts;

        private CancellationTokenSource _potSupListeningCts;
        private Task _potSupListeningTask;

        private CancellationTokenSource _dmmPollingCts;
        private Task _dmmPollingTask;

        private CancellationTokenSource _samplingCts;
        private Task _samplingTask;

        private readonly AC_6_4Simulation _simulation = new AC_6_4Simulation();

        private ResourceManager _dmmResourceManager;
        private MessageBasedSession _dmmSession;
        private readonly SemaphoreSlim _dmmIoLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _matrixSwitchLock = new SemaphoreSlim(1, 1);
        private bool _fixedMatrixConnected;

        private bool _isBusy;
        private bool _isInAtpMode;
        private bool _outputEnabled;
        private double? _latestDmmVoltage;
        private double? _latestPotSupVoltage;

        private string _title = "6.3 5V传感器供电电压测试";
        private bool _isManualTestRunning;
        private bool _isAutoTestRunning;
        private string _lastTestTime = "--";
        private string _lastTestResult = "--";

        private string _enterAtpTxChannel = "429_CH0";
        private string _enterAtpRxChannel = "429_CH2";

        private string _supplyTxChannel = "429_CH0";
        private string _potSupRxChannel = "429_CH2";

        private string _exitAtpTxChannel = "429_CH0";
        private string _exitAtpRxChannel = "429_CH2";

        private string _enterAtpRxDataText = "--";
        private string _dmmVoltageText = "--";
        private string _potSupRxDataText = "--";
        private string _potSupVoltageText = "--";
        private string _exitAtpRxDataText = "--";

        public Pot5VSupplyTestViewModel()
        {
            ManualTestCommand = new DelegateCommand(OnManualTest);
            AutoTestCommand = new DelegateCommand(OnAutoTest);
            ClearLogCommand = new DelegateCommand(() => Logs.Clear());

            SendEnterAtpCommand = new DelegateCommand(OnSendEnterAtp, CanSendEnterAtp);
            SendSupplyCommand = new DelegateCommand(OnSendSupply, CanSendSupply);
            MeasureVoltageCommand = new DelegateCommand(OnMeasureVoltage, CanMeasureVoltage);
            SendExitAtpCommand = new DelegateCommand(OnSendExitAtp, CanSendExitAtp);
        }

        public string Title
        {
            get => _title;
            set => SetProperty(ref _title, value);
        }

        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();

        public DelegateCommand ManualTestCommand { get; }
        public DelegateCommand AutoTestCommand { get; }
        public DelegateCommand ClearLogCommand { get; }

        public DelegateCommand SendEnterAtpCommand { get; }
        public DelegateCommand SendSupplyCommand { get; }
        public DelegateCommand MeasureVoltageCommand { get; }
        public DelegateCommand SendExitAtpCommand { get; }

        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (SetProperty(ref _isBusy, value))
                {
                    UpdateCommandStates();
                }
            }
        }

        public bool IsInAtpMode
        {
            get => _isInAtpMode;
            private set
            {
                if (SetProperty(ref _isInAtpMode, value))
                {
                    UpdateCommandStates();
                }
            }
        }

        public bool OutputEnabled
        {
            get => _outputEnabled;
            private set
            {
                if (SetProperty(ref _outputEnabled, value))
                {
                    if (value)
                    {
                        // fixed matrix only
                    }
                    else
                    {
                        StopSamplingTask();
                    }

                    UpdateCommandStates();
                }
            }
        }

        public bool IsManualTestRunning
        {
            get => _isManualTestRunning;
            set
            {
                if (SetProperty(ref _isManualTestRunning, value))
                {
                    if (value)
                    {
                        IsAutoTestRunning = false;
                    }

                    UpdateCommandStates();
                }
            }
        }

        public bool IsAutoTestRunning
        {
            get => _isAutoTestRunning;
            set
            {
                if (SetProperty(ref _isAutoTestRunning, value))
                {
                    if (value)
                    {
                        IsManualTestRunning = false;
                    }

                    UpdateCommandStates();
                }
            }
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
        }

        public string EnterAtpRxChannel
        {
            get => _enterAtpRxChannel;
        }

        public string SupplyTxChannel
        {
            get => _supplyTxChannel;
        }

        public string PotSupRxChannel
        {
            get => _potSupRxChannel;
        }

        public string ExitAtpTxChannel
        {
            get => _exitAtpTxChannel;
        }

        public string ExitAtpRxChannel
        {
            get => _exitAtpRxChannel;
        }

        public string EnterAtpRxDataText
        {
            get => _enterAtpRxDataText;
            set => SetProperty(ref _enterAtpRxDataText, value);
        }

        public string DmmVoltageText
        {
            get => _dmmVoltageText;
            set => SetProperty(ref _dmmVoltageText, value);
        }

        public string PotSupRxDataText
        {
            get => _potSupRxDataText;
            set => SetProperty(ref _potSupRxDataText, value);
        }

        public string PotSupVoltageText
        {
            get => _potSupVoltageText;
            set => SetProperty(ref _potSupVoltageText, value);
        }

        public string ExitAtpRxDataText
        {
            get => _exitAtpRxDataText;
            set => SetProperty(ref _exitAtpRxDataText, value);
        }

        private void OnManualTest()
        {
            if (IsManualTestRunning)
            {
                _ = StopTestAsync();
                return;
            }

            _ = StartManualTestAsync();
        }

        private void OnAutoTest()
        {
            AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试功能暂未实现");
        }

        private async Task StartManualTestAsync()
        {
            if (IsBusy) return;

            StopSamplingTask();

            IsBusy = true;
            await _manualTestLock.WaitAsync();
            try
            {
                IsManualTestRunning = true;
                IsAutoTestRunning = false;
                IsInAtpMode = false;
                OutputEnabled = false;

                LastTestTime = "--";
                LastTestResult = "--";
                _latestDmmVoltage = null;
                _latestPotSupVoltage = null;
                DmmVoltageText = "--";
                PotSupVoltageText = "--";
                EnterAtpRxDataText = "--";
                PotSupRxDataText = "--";
                ExitAtpRxDataText = "--";

                await StopPotSupListeningAsync();
                await StopDmmPollingAsync();

                _opCts?.Cancel();
                _opCts?.Dispose();
                _opCts = new CancellationTokenSource();

                AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试启动(仿真模式)：开始打开设备");

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
                await _simulation.StartAsync(EnterAtpTxChannel, EnterAtpRxChannel, msg => AddLog(msg));

                _fixedMatrixConnected = false;
                await DisconnectMatrixAsync(msg => AddLog(msg), CancellationToken.None);
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试启动失败: {ex.Message}");
                IsManualTestRunning = false;
            }
            finally
            {
                _manualTestLock.Release();
                IsBusy = false;
            }
        }

        private Task StopTestAsync()
        {
            return StopTestAsync(sendExitAtp: true);
        }

        private async Task StopTestAsync(bool sendExitAtp)
        {
            await _manualTestLock.WaitAsync();
            try
            {
                if (!IsManualTestRunning)
                    return;

                IsBusy = true;
                AddLog($"[{DateTime.Now:HH:mm:ss}] 停止测试：发送退出ATP并关闭设备");

                StopSamplingTask();

                try { _opCts?.Cancel(); } catch { }

                await StopPotSupListeningAsync();
                await StopDmmPollingAsync();

                try { _simulation.StopPotSupOutput(); } catch { }

                await DisconnectDmmAsync();
                await DisconnectMatrixAsync(msg => AddLog(msg), CancellationToken.None);

                if (sendExitAtp && IsInAtpMode)
                {
                    await SendExitAtpAsync(stopAfter: false);
                }

                await _simulation.StopAsync(msg => AddLog(msg));

                IsManualTestRunning = false;
                IsAutoTestRunning = false;
                IsInAtpMode = false;
                OutputEnabled = false;

                DmmVoltageText = "--";
                PotSupVoltageText = "--";
                _latestDmmVoltage = null;
                _latestPotSupVoltage = null;
                EnterAtpRxDataText = "--";
                PotSupRxDataText = "--";
                ExitAtpRxDataText = "--";

                try
                {
                    _opCts?.Cancel();
                    _opCts?.Dispose();
                    _opCts = null;
                }
                catch
                {
                }

                if (string.IsNullOrWhiteSpace(LastTestTime) || LastTestTime == "--")
                {
                    LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                }

                AddLog($"[{DateTime.Now:HH:mm:ss}] 测试已停止，资源已释放");
            }
            finally
            {
                IsBusy = false;
                _manualTestLock.Release();
            }
        }

        private void UpdateCommandStates()
        {
            try
            {
                SendEnterAtpCommand?.RaiseCanExecuteChanged();
                SendSupplyCommand?.RaiseCanExecuteChanged();
                MeasureVoltageCommand?.RaiseCanExecuteChanged();
                SendExitAtpCommand?.RaiseCanExecuteChanged();
            }
            catch
            {
            }
        }

        private bool CanMeasureVoltage()
        {
            if (IsBusy) return false;
            if (!IsManualTestRunning && !IsAutoTestRunning) return false;
            return true;
        }

        private void OnMeasureVoltage()
        {
            _ = MeasureVoltageOnceAsync();
        }

        private async Task MeasureVoltageOnceAsync()
        {
            if (!MeasureVoltageCommand.CanExecute())
                return;

            try
            {
                IsBusy = true;
                var token = _opCts?.Token ?? CancellationToken.None;

                await EnsureFixedMatrixConnectedAsync(msg => AddLog(msg), token);

                await EnsureDmmConnectedAsync(token);
                var raw = await QueryDmmStringAsync(":MEAS:VOLT:DC?", token).ConfigureAwait(false);
                raw = raw?.Trim();

                if (TryParseVoltageReading(raw, out var v))
                {
                    _latestDmmVoltage = v;
                    DmmVoltageText = $"{v:0.000} V";
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 万用表测量: {v:0.000} V");
                }
                else
                {
                    _latestDmmVoltage = null;
                    DmmVoltageText = "--";
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 万用表测量无有效值: {raw}");
                }
            }
            catch (Exception ex)
            {
                DmmVoltageText = $"回采失败: {ex.Message}";
                AddLog($"[{DateTime.Now:HH:mm:ss}] 万用表测量异常: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private bool CanSendEnterAtp()
        {
            if (IsBusy) return false;
            if (!IsManualTestRunning && !IsAutoTestRunning) return false;
            if (string.IsNullOrWhiteSpace(EnterAtpTxChannel) || string.IsNullOrWhiteSpace(EnterAtpRxChannel)) return false;
            return true;
        }

        private void OnSendEnterAtp()
        {
            _ = SendEnterAtpAsync();
        }

        private async Task<bool> SendEnterAtpAsync()
        {
            if (!SendEnterAtpCommand.CanExecute())
                return false;

            try
            {
                IsBusy = true;
                var token = _opCts?.Token ?? CancellationToken.None;
                for (int attempt = 1; attempt <= EnterAtpMaxRetries; attempt++)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] (1) 进入ATP(第{attempt}次)：TX={EnterAtpTxChannel}, RX={EnterAtpRxChannel}, Label=0x{DefaultLabel:X2}");

                    try { await _simulation.ClearRxFifoAsync(EnterAtpRxChannel); } catch { }
                    await Task.Delay(50, token);

                    var resp = await _simulation.SendBenchCommandAndWaitAsync(
                        EnterAtpTxChannel,
                        EnterAtpRxChannel,
                        DefaultLabel,
                        EnterAtpCommand,
                        b => b != null && b.SequenceEqual(EnterAtpOk),
                        timeoutMs: EnterAtpTimeoutMs,
                        msg => AddLog(msg),
                        token);

                    if (resp != null)
                    {
                        EnterAtpRxDataText = $"0x{FormatBytes(resp)}";
                        AddLog($"[{DateTime.Now:HH:mm:ss}] 收到ATP OK，进入ATP成功");
                        IsInAtpMode = true;
                        return true;
                    }

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 进入ATP第{attempt}次超时，未收到OK");
                    if (attempt < EnterAtpMaxRetries)
                        await Task.Delay(200, token);
                }

                AddLog($"[{DateTime.Now:HH:mm:ss}] 进入ATP失败：已重试{EnterAtpMaxRetries}次均超时");
                return false;
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 进入ATP异常：{ex.Message}");
                return false;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private bool CanSendSupply()
        {
            if (IsBusy) return false;
            if (!IsInAtpMode) return false;
            if (!IsManualTestRunning && !IsAutoTestRunning) return false;
            return !string.IsNullOrWhiteSpace(SupplyTxChannel);
        }

        private void OnSendSupply()
        {
            _ = SendAb5VPotSupplyAsync();
        }

        private async Task SendAb5VPotSupplyAsync()
        {
            if (!SendSupplyCommand.CanExecute())
                return;

            try
            {
                IsBusy = true;
                var token = _opCts?.Token ?? CancellationToken.None;

                try
                {
                    // 确保回采 RX 通道已打开并启动接收（步骤4仅RX选择）
                    await _simulation.EnsureBenchChannelsAsync(SupplyTxChannel, PotSupRxChannel, msg => AddLog(msg));
                }
                catch
                {
                }

                await EnsureFixedMatrixConnectedAsync(msg => AddLog(msg), token);
                await SwitchMatrixForSelectedDmmChannelAsync(msg => AddLog(msg), token);

                AddLog($"[{DateTime.Now:HH:mm:ss}] (2) 发送AB_5VPOT_SUPPLY：TX={SupplyTxChannel}, Label=0x{DefaultLabel:X2}, Data={FormatBytes(Ab5VPotSupplyCommand)}");
                await _simulation.SendBenchCommandOnlyAsync(
                    SupplyTxChannel,
                    DefaultLabel,
                    Ab5VPotSupplyCommand,
                    msg => AddLog(msg),
                    token);

                OutputEnabled = true;

                try
                {
                    await EnsureDmmConnectedAsync(token);
                    StartDmmVoltageRangePolling(token);
                }
                catch (Exception ex)
                {
                    DmmVoltageText = $"回采失败: {ex.Message}";
                }

                StartPotSupListeningIfNeeded();
                StartSamplingTask();
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] AB_5VPOT_SUPPLY异常：{ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private bool CanSendExitAtp()
        {
            if (!IsManualTestRunning && !IsAutoTestRunning) return false;
            if (string.IsNullOrWhiteSpace(ExitAtpTxChannel) || string.IsNullOrWhiteSpace(ExitAtpRxChannel)) return false;
            return true;
        }

        private void OnSendExitAtp()
        {
            _ = SendExitAtpAsync(stopAfter: true);
        }

        private async Task<bool> SendExitAtpAsync(bool stopAfter)
        {
            try
            {
                StopSamplingTask();
                IsBusy = true;

                var token = _opCts?.Token ?? CancellationToken.None;
                AddLog($"[{DateTime.Now:HH:mm:ss}] (5) 退出ATP：TX={ExitAtpTxChannel}, RX={ExitAtpRxChannel}, Label=0x{DefaultLabel:X2}");

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

                if (resp != null)
                {
                    ExitAtpRxDataText = $"0x{FormatBytes(resp)}";
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 收到退出ATP OK");

                    IsInAtpMode = false;
                    OutputEnabled = false;
                    return true;
                }

                AddLog($"[{DateTime.Now:HH:mm:ss}] 退出ATP超时，未收到OK");
                return false;
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 退出ATP异常：{ex.Message}");
                return false;
            }
            finally
            {
                await DisconnectMatrixAsync(msg => AddLog(msg), CancellationToken.None);

                if (stopAfter)
                {
                    _ = StopTestAsync(sendExitAtp: false);
                }

                IsBusy = false;
            }
        }

        private void StartPotSupListeningIfNeeded()
        {
            if (_potSupListeningTask != null)
                return;
            if (string.IsNullOrWhiteSpace(PotSupRxChannel))
                return;

            _potSupListeningCts?.Cancel();
            _potSupListeningCts?.Dispose();
            var baseToken = _opCts?.Token ?? CancellationToken.None;
            _potSupListeningCts = CancellationTokenSource.CreateLinkedTokenSource(baseToken);
            var token = _potSupListeningCts.Token;

            _potSupListeningTask = Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        var resp = await _simulation.WaitBenchResponseAsync(
                            PotSupRxChannel,
                            DefaultLabel,
                            IsPotSupPayload,
                            timeoutMs: 300,
                            msg => { },
                            token);

                        if (resp != null)
                        {
                            UpdatePotSupRxDataText(resp);
                            if (TryParsePotSupVoltage(resp, out var v))
                            {
                                UpdatePotSupUi(v);
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch
                    {
                        try { await Task.Delay(100, token); } catch { break; }
                    }
                }
            }, token);
        }

        private async Task StopPotSupListeningAsync()
        {
            try
            {
                _potSupListeningCts?.Cancel();
            }
            catch
            {
            }

            var task = _potSupListeningTask;
            if (task != null)
            {
                try
                {
                    await task.ConfigureAwait(false);
                }
                catch
                {
                }
            }

            _potSupListeningTask = null;
            try
            {
                _potSupListeningCts?.Dispose();
            }
            catch
            {
            }
            _potSupListeningCts = null;
        }

        private void StartSamplingTask()
        {
            StopSamplingTask();

            var baseToken = _opCts?.Token ?? CancellationToken.None;
            _samplingCts = CancellationTokenSource.CreateLinkedTokenSource(baseToken);
            var token = _samplingCts.Token;
            _samplingTask = Task.Run(async () =>
            {
                await EvaluateResultAsync(token);
            }, token);
        }

        private void StopSamplingTask()
        {
            try
            {
                _samplingCts?.Cancel();
            }
            catch
            {
            }

            try
            {
                _samplingCts?.Dispose();
            }
            catch
            {
            }
            _samplingCts = null;
            _samplingTask = null;
        }

        private async Task EvaluateResultAsync(CancellationToken token)
        {
            const int testCount = 5;
            const int stabilizeTimeoutSeconds = 30;

            double? lastDmm = null;
            double? lastPot = null;
            var startTime = DateTime.UtcNow;

            AddLog($"[{DateTime.Now:HH:mm:ss}] 等待硬件稳定（{stabilizeTimeoutSeconds}秒超时）...");
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(200, token);
                }
                catch (OperationCanceledException)
                {
                    RunOnUi(() =>
                    {
                        LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                        LastTestResult = "FAIL";
                    });
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 测试结果: FAIL (已取消)");
                    return;
                }

                var dmm = _latestDmmVoltage;
                var pot = _latestPotSupVoltage;
                if (dmm.HasValue && dmm.Value != 0 && pot.HasValue && pot.Value != 0)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 硬件已稳定: DMM={dmm.Value:F3}V, 回采={pot.Value:F3}V");
                    break;
                }

                if ((DateTime.UtcNow - startTime).TotalSeconds > stabilizeTimeoutSeconds)
                {
                    RunOnUi(() =>
                    {
                        LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                        LastTestResult = "FAIL";
                    });
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 测试结果: FAIL (等待数据超时，请检查通道配置)");
                    return;
                }
            }

            AddLog($"[{DateTime.Now:HH:mm:ss}] 开始采集判定，共{testCount}次");
            int passCount = 0;
            for (int i = 1; i <= testCount && !token.IsCancellationRequested; i++)
            {
                try
                {
                    await Task.Delay(300, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                var dmm = _latestDmmVoltage;
                var pot = _latestPotSupVoltage;
                if (!dmm.HasValue || !pot.HasValue)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 采样#{i}: 数据无效 -> 不合格");
                    continue;
                }

                lastDmm = dmm;
                lastPot = pot;

                bool dmmOk = dmm.Value >= DmmMin && dmm.Value <= DmmMax;
                bool potOk = pot.Value >= PotSupMin && pot.Value <= PotSupMax;
                if (dmmOk && potOk)
                {
                    passCount++;
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 采样#{i}: DMM={dmm.Value:F3}V, 回采={pot.Value:F3}V -> 合格");
                }
                else
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 采样#{i}: DMM={dmm.Value:F3}V(需{DmmMin}~{DmmMax}), 回采={pot.Value:F3}V(需{PotSupMin}~{PotSupMax}) -> 不合格");
                }
            }

            await StopDmmPollingAsync();
            await StopPotSupListeningAsync();

            try { _simulation.StopPotSupOutput(); } catch { }

            RunOnUi(() =>
            {
                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                if (passCount == testCount)
                {
                    LastTestResult = "PASS";
                }
                else
                {
                    LastTestResult = "FAIL";
                }
            });

            if (passCount == testCount)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 测试结果: PASS ({testCount}次全部合格)");
                AddLog($"[{DateTime.Now:HH:mm:ss}] 最终值: 供电模块={lastDmm:F3}V, 回采={lastPot:F3}V");
            }
            else if (token.IsCancellationRequested)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 测试结果: FAIL (已取消)");
            }
            else
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 测试结果: FAIL ({passCount}/{testCount}次合格)");
            }
        }

        private static bool IsPotSupPayload(byte[] b)
        {
            return b != null && b.Length == 8 && b[0] == 0x01 && b[1] == 0x02 && b[2] == 0x01 && b[3] == 0x02;
        }

        private static bool TryParsePotSupVoltage(byte[] resp, out double voltage)
        {
            voltage = 0;
            if (!IsPotSupPayload(resp))
                return false;

            uint raw = (uint)(resp[4] << 24) | (uint)(resp[5] << 16) | (uint)(resp[6] << 8) | resp[7];
            voltage = raw / 1000.0;
            return true;
        }

        private void UpdatePotSupUi(double voltage)
        {
            try
            {
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher != null && !dispatcher.CheckAccess())
                {
                    dispatcher.BeginInvoke(new Action(() =>
                    {
                        _latestPotSupVoltage = voltage;
                        PotSupVoltageText = $"{voltage:0.000} V";
                    }));
                }
                else
                {
                    _latestPotSupVoltage = voltage;
                    PotSupVoltageText = $"{voltage:0.000} V";
                }
            }
            catch
            {
            }
        }

        private void UpdatePotSupRxDataText(byte[] resp)
        {
            try
            {
                var text = $"0x{FormatBytes(resp)}";
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher != null && !dispatcher.CheckAccess())
                {
                    dispatcher.BeginInvoke(new Action(() =>
                    {
                        PotSupRxDataText = text;
                    }));
                }
                else
                {
                    PotSupRxDataText = text;
                }
            }
            catch
            {
            }
        }

        private void StartDmmVoltageRangePolling(CancellationToken token)
        {
            if (_dmmPollingTask != null && !_dmmPollingTask.IsCompleted)
                return;

            _dmmPollingCts?.Cancel();
            _dmmPollingCts?.Dispose();
            _dmmPollingCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            var ct = _dmmPollingCts.Token;

            _dmmPollingTask = Task.Run(async () =>
            {
                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        var raw = await QueryDmmStringAsync(":MEAS:VOLT:DC?", ct).ConfigureAwait(false);
                        raw = raw?.Trim();

                        if (TryParseVoltageReading(raw, out var v))
                        {
                            _latestDmmVoltage = v;
                            RunOnUi(() => DmmVoltageText = $"{v:0.00000} V");
                        }
                        else
                        {
                            RunOnUi(() => DmmVoltageText = raw);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        RunOnUi(() => DmmVoltageText = $"回采失败: {ex.Message}");
                    }

                    await Task.Delay(300, ct).ConfigureAwait(false);
                }
            }, ct);
        }

        private async Task StopDmmPollingAsync()
        {
            try
            {
                _dmmPollingCts?.Cancel();
            }
            catch
            {
            }

            try
            {
                if (_dmmPollingTask != null && !_dmmPollingTask.IsCompleted)
                {
                    await _dmmPollingTask.ConfigureAwait(false);
                }
            }
            catch
            {
            }

            _dmmPollingTask = null;
            try
            {
                _dmmPollingCts?.Dispose();
            }
            catch
            {
            }
            _dmmPollingCts = null;
        }

        private async Task EnsureDmmConnectedAsync(CancellationToken token)
        {
            if (_dmmSession != null)
                return;

            _dmmResourceManager ??= new ResourceManager();

            const string ip = "192.168.1.13";
            const int port = 5555;

            await Task.Run(() =>
            {
                string resourceString = $"TCPIP0::{ip}::{port}::SOCKET";
                _dmmSession = (MessageBasedSession)_dmmResourceManager.Open(resourceString, 0, 5000);
                try
                {
                    _dmmSession.TimeoutMilliseconds = 8000;
                    _dmmSession.TerminationCharacterEnabled = true;
                    _dmmSession.TerminationCharacter = (byte)'\n';
                }
                catch
                {
                }
            }, token);
        }

        private async Task<string> QueryDmmStringAsync(string command, CancellationToken token)
        {
            if (_dmmSession == null)
                throw new InvalidOperationException("DMM会话未建立");

            await _dmmIoLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                var cmd = command.EndsWith("\n", StringComparison.Ordinal) ? command : command + "\n";
                _dmmSession.RawIO.Write(cmd);
                return _dmmSession.RawIO.ReadString();
            }
            finally
            {
                _dmmIoLock.Release();
            }
        }

        private Task DisconnectDmmAsync()
        {
            try
            {
                _dmmSession?.Dispose();
            }
            catch
            {
            }
            finally
            {
                _dmmSession = null;
            }

            return Task.CompletedTask;
        }

        private async Task EnsureFixedMatrixConnectedAsync(Action<string> log, CancellationToken token)
        {
            if (_fixedMatrixConnected)
                return;

            await _matrixSwitchLock.WaitAsync(token);
            try
            {
                if (_fixedMatrixConnected)
                    return;

                var task1 = MatrixControlService.Instance.ConnectNodesAsync("I1", "O15", 6, "192.168.1.3");
                var task2 = MatrixControlService.Instance.ConnectNodesAsync("I4", "O2", 4, "192.168.1.3");

                var results = await Task.WhenAll(task1, task2);
                bool ok1 = results.Length > 0 && results[0];
                bool ok2 = results.Length > 1 && results[1];

                _fixedMatrixConnected = results.All(r => r);
                log?.Invoke($"[{DateTime.Now:HH:mm:ss}] 矩阵开关通路(固定): I1->O15 slot=6 ip=192.168.1.3, ok={ok1}");
                log?.Invoke($"[{DateTime.Now:HH:mm:ss}] 矩阵开关通路(固定): I4->O2 slot=4 ip=192.168.1.3, ok={ok2}");
            }
            finally
            {
                _matrixSwitchLock.Release();
            }
        }

        private Task SwitchMatrixForSelectedDmmChannelAsync(Action<string> log, CancellationToken token)
        {
            return EnsureFixedMatrixConnectedAsync(log, token);
        }

        private async Task DisconnectMatrixAsync(Action<string> log, CancellationToken token)
        {
            await _matrixSwitchLock.WaitAsync(token);
            try
            {
                var task1 = MatrixControlService.Instance.DisconnectNodesAsync("I1", "O15", 6, "192.168.1.3");
                var task2 = MatrixControlService.Instance.DisconnectNodesAsync("I4", "O2", 4, "192.168.1.3");

                var results = await Task.WhenAll(task1, task2);
                bool ok1 = results.Length > 0 && results[0];
                bool ok2 = results.Length > 1 && results[1];

                log?.Invoke($"[{DateTime.Now:HH:mm:ss}] 矩阵开关断开(固定): I1->O15 slot=6 ip=192.168.1.3, ok={ok1}");
                log?.Invoke($"[{DateTime.Now:HH:mm:ss}] 矩阵开关断开(固定): I4->O2 slot=4 ip=192.168.1.3, ok={ok2}");

                _fixedMatrixConnected = false;
            }
            catch (Exception ex)
            {
                log?.Invoke($"[{DateTime.Now:HH:mm:ss}] 矩阵开关断开失败: {ex.Message}");
            }
            finally
            {
                _matrixSwitchLock.Release();
            }
        }

        private static bool TryParseVoltageReading(string raw, out double voltage)
        {
            voltage = 0;
            if (string.IsNullOrWhiteSpace(raw))
                return false;

            var s = raw.Trim();
            if (s.Equals("OL", StringComparison.OrdinalIgnoreCase) ||
                s.IndexOf("OVER", StringComparison.OrdinalIgnoreCase) >= 0 ||
                s.IndexOf("OVLD", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }

            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out voltage) ||
                   double.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out voltage);
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
        }

        private void RunOnUi(Action action)
        {
            if (action == null)
                return;

            try
            {
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher != null && !dispatcher.CheckAccess())
                {
                    dispatcher.BeginInvoke(action);
                }
                else
                {
                    action();
                }
            }
            catch
            {
            }
        }
    }
}
