using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MeasureControl.Events;
using MeasureControl.Models;
using MeasureControl.Services;
using Prism.Commands;
using Prism.Events;
using Prism.Mvvm;
using System.Windows;
using MeasureControl.Simulations.AC_6_4;
using System.Globalization;
using NationalInstruments.Visa;

namespace MeasureControl.ViewModels.SingleBoardTest.AirController
{
    public class AC_6_4ViewModel : BindableBase, IDisposable
    {
        private const byte DefaultLabel = 0x6A;
        private static readonly byte[] EnterAtpCommand = { 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] EnterAtpOk = { 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x02 };
        private static readonly byte[] ExitAtpCommand = { 0x00, 0x02, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01 };
        private static readonly byte[] ExitAtpOk = { 0x00, 0x02, 0x00, 0x01, 0x00, 0x00, 0x00, 0x03 };
        private static readonly byte[] EnableOutputCommand = { 0x01, 0x04, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] EnableOutputAck = { 0x01, 0x04, 0x01, 0x01, 0x00, 0x00, 0x00, 0x01 };

        private const string TestItemKey = "AC_6_4";
        private const string PersistKeyEnterAtpTx = "EnterAtpTxChannel";
        private const string PersistKeyEnterAtpRx = "EnterAtpRxChannel";
        private const string PersistKeySetVoltageTx = "SetVoltageTxChannel";
        private const string PersistKeySetVoltageRx = "SetVoltageRxChannel";
        private const string PersistKeyTelemetryRx = "TelemetryRxChannel";
        private const string PersistKeyExitAtpTx = "ExitAtpTxChannel";
        private const string PersistKeyExitAtpRx = "ExitAtpRxChannel";
        private const string PersistKeyDmmChannel = "DmmChannel";
        private const string PersistKeyArincRate = "ArincRate";
        private const string PersistKeyLastTestTime = "LastTestTime";
        private const string PersistKeyLastTestResult = "LastTestResult";

        private readonly ISingleBoardTestContextService _singleBoardTestContext;
        private readonly ProjectService _projectService;
        private readonly IEventAggregator _eventAggregator;
        private readonly AC_6_4Simulation _simulation = new AC_6_4Simulation();

        private readonly ResourceManager _dmmResourceManager = new ResourceManager();
        private MessageBasedSession _dmmSession;
        private readonly SemaphoreSlim _dmmIoLock = new SemaphoreSlim(1, 1);
        private CancellationTokenSource _dmmPollingCts;
        private Task _dmmPollingTask;
        private readonly SemaphoreSlim _matrixSwitchLock = new SemaphoreSlim(1, 1);
        private bool _fixedMatrixConnected;

        private SubscriptionToken _projectSavingToken;

        private bool _isManualTestRunning;
        private bool _isAutoTestRunning;
        private bool _isInAtpMode;
        private bool _outputEnabled;
        private bool _isBusy;
        private double? _latestDmmVoltage;
        private double? _latestTelemetryVoltage;

        private CancellationTokenSource _opCts;

        private string _enterAtpTxChannel;
        private string _enterAtpRxChannel;
        private string _setVoltageTxChannel;
        private string _setVoltageRxChannel;
        private string _telemetryRxChannel;
        private string _exitAtpTxChannel;
        private string _exitAtpRxChannel;

        private string _dmmChannel;

        private double _arincRate = 100000.0;

        private string _dmmVoltageText;
        private string _telemetryVoltageText;

        private string _lastTestTime;
        private string _lastTestResult;

        public AC_6_4ViewModel(
            ISingleBoardTestContextService singleBoardTestContext,
            ProjectService projectService,
            IEventAggregator eventAggregator)
        {
            _singleBoardTestContext = singleBoardTestContext;
            _projectService = projectService;
            _eventAggregator = eventAggregator;
            _enterAtpTxChannel = "ARINC429_0";
            _enterAtpRxChannel = "ARINC429_1";
            _setVoltageTxChannel = "ARINC429_2";
            _setVoltageRxChannel = "ARINC429_3";
            _telemetryRxChannel = "ARINC429_4";
            _exitAtpTxChannel = "ARINC429_5";
            _exitAtpRxChannel = "ARINC429_6";

            _dmmChannel = "Port1";

            DmmVoltageText = "--";
            TelemetryVoltageText = "--";

            _simulation.OnOutputEnabledAsync = OnSimulationOutputEnabledAsync;

            ManualTestCommand = new DelegateCommand(OnManualTest);
            AutoTestCommand = new DelegateCommand(OnAutoTest);

            SendEnterAtpCommand = new DelegateCommand(OnSendEnterAtp, CanSendEnterAtp);
            SendSetVoltageCommand = new DelegateCommand(OnSendSetVoltage, CanSendSetVoltage);
            SendExitAtpCommand = new DelegateCommand(OnSendExitAtp, CanSendExitAtp);

            ClearLogCommand = new DelegateCommand(() => Logs.Clear());

            LoadPersistedState();
            _projectSavingToken = _eventAggregator?.GetEvent<ProjectSavingEvent>()?.Subscribe(OnProjectSaving);

            UpdateCommandStates();
        }

        private string PersistDataKey
        {
            get
            {
                var taskName = _singleBoardTestContext?.TestTaskName ?? string.Empty;
                if (string.IsNullOrWhiteSpace(taskName))
                {
                    return $"{TestItemKey}";
                }

                return $"{taskName}/{TestItemKey}";
            }
        }

        public string TelemetryVoltageText
        {
            get => _telemetryVoltageText;
            private set => SetProperty(ref _telemetryVoltageText, value);
        }

        public string DmmVoltageText
        {
            get => _dmmVoltageText;
            private set => SetProperty(ref _dmmVoltageText, value);
        }

        public double? LatestDmmVoltage
        {
            get => _latestDmmVoltage;
            private set => SetProperty(ref _latestDmmVoltage, value);
        }

        public double? LatestTelemetryVoltage
        {
            get => _latestTelemetryVoltage;
            private set => SetProperty(ref _latestTelemetryVoltage, value);
        }

        public DelegateCommand ManualTestCommand { get; }
        public DelegateCommand AutoTestCommand { get; }
        public DelegateCommand SendEnterAtpCommand { get; }
        public DelegateCommand SendSetVoltageCommand { get; }
        public DelegateCommand SendExitAtpCommand { get; }
        public DelegateCommand ClearLogCommand { get; }

        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();

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
                    UpdateCommandStates();
                }
            }
        }

        public string EnterAtpTxChannel
        {
            get => _enterAtpTxChannel;
            set
            {
                if (SetProperty(ref _enterAtpTxChannel, value))
                {
                    UpdateCommandStates();
                    SavePersistedState();
                }
            }
        }

        public string EnterAtpRxChannel
        {
            get => _enterAtpRxChannel;
            set
            {
                if (SetProperty(ref _enterAtpRxChannel, value))
                {
                    UpdateCommandStates();
                    SavePersistedState();
                }
            }
        }

        public string SetVoltageTxChannel
        {
            get => _setVoltageTxChannel;
            set
            {
                if (SetProperty(ref _setVoltageTxChannel, value))
                {
                    UpdateCommandStates();
                    SavePersistedState();
                }
            }
        }

        public string SetVoltageRxChannel
        {
            get => _setVoltageRxChannel;
            set
            {
                if (SetProperty(ref _setVoltageRxChannel, value))
                {
                    UpdateCommandStates();
                    SavePersistedState();
                }
            }
        }

        public string TelemetryRxChannel
        {
            get => _telemetryRxChannel;
            set
            {
                if (SetProperty(ref _telemetryRxChannel, value))
                {
                    SavePersistedState();
                }
            }
        }

        public string ExitAtpTxChannel
        {
            get => _exitAtpTxChannel;
            set
            {
                if (SetProperty(ref _exitAtpTxChannel, value))
                {
                    UpdateCommandStates();
                    SavePersistedState();
                }
            }
        }

        public string ExitAtpRxChannel
        {
            get => _exitAtpRxChannel;
            set
            {
                if (SetProperty(ref _exitAtpRxChannel, value))
                {
                    UpdateCommandStates();
                    SavePersistedState();
                }
            }
        }

        public string DmmChannel
        {
            get => _dmmChannel;
            set
            {
                if (SetProperty(ref _dmmChannel, value))
                {
                    SavePersistedState();
                    TrySwitchMatrixForSelectedDmmChannel();
                }
            }
        }

        public string LastTestTime
        {
            get => _lastTestTime;
            set
            {
                if (SetProperty(ref _lastTestTime, value))
                {
                    SavePersistedState();
                }
            }
        }

        public string LastTestResult
        {
            get => _lastTestResult;
            set
            {
                if (SetProperty(ref _lastTestResult, value))
                {
                    SavePersistedState();
                }
            }
        }

        public double ArincRate
        {
            get => _arincRate;
            set
            {
                if (SetProperty(ref _arincRate, value))
                {
                    SavePersistedState();
                }
            }
        }

        private void LoadPersistedState()
        {
            try
            {
                var root = _projectService?.CurrentProjectRoot;
                if (root?.TestInterfaceControls == null)
                    return;

                if (!root.TestInterfaceControls.TryGetValue(PersistDataKey, out var items) || items == null)
                    return;

                string Read(string key)
                {
                    return items.FirstOrDefault(x => string.Equals(x?.BoundVariableName, key, StringComparison.OrdinalIgnoreCase))?.BoundVariablePath;
                }

                _enterAtpTxChannel = Read(PersistKeyEnterAtpTx) ?? _enterAtpTxChannel;
                _enterAtpRxChannel = Read(PersistKeyEnterAtpRx) ?? _enterAtpRxChannel;
                _setVoltageTxChannel = Read(PersistKeySetVoltageTx) ?? _setVoltageTxChannel;
                _setVoltageRxChannel = Read(PersistKeySetVoltageRx) ?? _setVoltageRxChannel;
                _telemetryRxChannel = Read(PersistKeyTelemetryRx) ?? _telemetryRxChannel;
                _exitAtpTxChannel = Read(PersistKeyExitAtpTx) ?? _exitAtpTxChannel;
                _exitAtpRxChannel = Read(PersistKeyExitAtpRx) ?? _exitAtpRxChannel;
                _dmmChannel = Read(PersistKeyDmmChannel) ?? _dmmChannel;

                var rateText = Read(PersistKeyArincRate);
                if (double.TryParse(rateText, NumberStyles.Any, CultureInfo.InvariantCulture, out var rate) && rate > 0)
                    _arincRate = rate;

                _lastTestTime = Read(PersistKeyLastTestTime) ?? _lastTestTime;
                _lastTestResult = Read(PersistKeyLastTestResult) ?? _lastTestResult;

                RaisePropertyChanged(nameof(EnterAtpTxChannel));
                RaisePropertyChanged(nameof(EnterAtpRxChannel));
                RaisePropertyChanged(nameof(SetVoltageTxChannel));
                RaisePropertyChanged(nameof(SetVoltageRxChannel));
                RaisePropertyChanged(nameof(TelemetryRxChannel));
                RaisePropertyChanged(nameof(ExitAtpTxChannel));
                RaisePropertyChanged(nameof(ExitAtpRxChannel));
                RaisePropertyChanged(nameof(DmmChannel));
                RaisePropertyChanged(nameof(ArincRate));
                RaisePropertyChanged(nameof(LastTestTime));
                RaisePropertyChanged(nameof(LastTestResult));
            }
            catch
            {
            }
        }

        private void SavePersistedState()
        {
            try
            {
                var root = _projectService?.CurrentProjectRoot;
                if (root?.TestInterfaceControls == null)
                    return;

                if (!root.TestInterfaceControls.TryGetValue(PersistDataKey, out var items) || items == null)
                {
                    items = new List<TestInterfaceControlItem>();
                    root.TestInterfaceControls[PersistDataKey] = items;
                }

                void Upsert(string key, string value)
                {
                    var item = items.FirstOrDefault(x => string.Equals(x?.BoundVariableName, key, StringComparison.OrdinalIgnoreCase));
                    if (item == null)
                    {
                        item = new TestInterfaceControlItem
                        {
                            ControlType = "Value",
                            BoundVariableName = key
                        };
                        items.Add(item);
                    }

                    item.BoundVariablePath = value ?? string.Empty;
                }

                Upsert(PersistKeyEnterAtpTx, EnterAtpTxChannel);
                Upsert(PersistKeyEnterAtpRx, EnterAtpRxChannel);
                Upsert(PersistKeySetVoltageTx, SetVoltageTxChannel);
                Upsert(PersistKeySetVoltageRx, SetVoltageRxChannel);
                Upsert(PersistKeyTelemetryRx, TelemetryRxChannel);
                Upsert(PersistKeyExitAtpTx, ExitAtpTxChannel);
                Upsert(PersistKeyExitAtpRx, ExitAtpRxChannel);
                Upsert(PersistKeyDmmChannel, DmmChannel);
                Upsert(PersistKeyArincRate, ArincRate.ToString(CultureInfo.InvariantCulture));
                Upsert(PersistKeyLastTestTime, LastTestTime);
                Upsert(PersistKeyLastTestResult, LastTestResult);
            }
            catch
            {
            }
        }

        private void OnProjectSaving()
        {
            SavePersistedState();
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
            if (IsAutoTestRunning)
            {
                _ = StopTestAsync();
                return;
            }

            // 当前阶段只实现仿真模式下的设备初始化；自动测试后续再接具体步骤
            _ = StartAutoTestAsync();
        }

        private async Task StartManualTestAsync()
        {
            if (IsBusy) return;
            IsBusy = true;
            try
            {
                IsManualTestRunning = true;
                LastTestTime = "--";
                LastTestResult = "--";
                _opCts?.Cancel();
                _opCts?.Dispose();
                _opCts = new CancellationTokenSource();
                AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试启动(仿真模式)：开始打开设备");

                // 仿真模式：固定占用产品侧通道
                _simulation.SimProductRxChannelIndex = 6;
                _simulation.SimProductTxChannelIndex = 7;
                _simulation.ArincRate = ArincRate;

                await _simulation.StartAsync(EnterAtpTxChannel, EnterAtpRxChannel, msg => AddLog(msg));

                IsInAtpMode = false;
                OutputEnabled = false;
                _fixedMatrixConnected = false;
                DmmVoltageText = "--";
                TelemetryVoltageText = "--";
                await DisconnectMatrixAsync(msg => AddLog(msg), CancellationToken.None);
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

        private Task StopTestAsync()
        {
            return StopTestAsync(sendExitAtp: true);
        }

        private async Task StartAutoTestAsync()
        {
            if (IsBusy) return;
            IsBusy = true;
            try
            {
                IsAutoTestRunning = true;
                LastTestTime = "--";
                LastTestResult = "--";
                _opCts?.Cancel();
                _opCts?.Dispose();
                _opCts = new CancellationTokenSource();
                AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试启动(仿真模式)：开始打开设备");

                _simulation.SimProductRxChannelIndex = 6;
                _simulation.SimProductTxChannelIndex = 7;
                _simulation.ArincRate = ArincRate;

                await _simulation.StartAsync(EnterAtpTxChannel, EnterAtpRxChannel, msg => AddLog(msg));

                IsInAtpMode = false;
                OutputEnabled = false;
                _fixedMatrixConnected = false;
                DmmVoltageText = "--";
                TelemetryVoltageText = "--";
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试启动失败: {ex.Message}");
                IsAutoTestRunning = false;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task StopTestAsync(bool sendExitAtp)
        {
            if (IsBusy) return;
            IsBusy = true;
            try
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 停止测试：发送退出ATP并关闭设备");

                await StopDmmPollingAsync();
                await DisconnectDmmAsync();
                await DisconnectMatrixAsync(msg => AddLog(msg), CancellationToken.None);

                if (sendExitAtp && SendExitAtpCommand.CanExecute())
                {
                    await SendExitAtpAsync(stopAfter: false);
                }

                await _simulation.StopAsync(msg => AddLog(msg));

                IsManualTestRunning = false;
                IsAutoTestRunning = false;
                IsInAtpMode = false;
                OutputEnabled = false;

                DmmVoltageText = "--";
                TelemetryVoltageText = "--";

                try
                {
                    _opCts?.Cancel();
                }
                catch
                {
                }

                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                LastTestResult = "--";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void TrySwitchMatrixForSelectedDmmChannel()
        {
            if (!OutputEnabled)
                return;

            var token = _opCts?.Token ?? CancellationToken.None;
            _ = Task.Run(async () =>
            {
                try
                {
                    await SwitchMatrixForSelectedDmmChannelAsync(msg => AddLog(msg), token);
                }
                catch (Exception ex)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] [SIM->VM] 矩阵开关切换失败: {ex.Message}");
                }
            });
        }

        private async Task SwitchMatrixForSelectedDmmChannelAsync(Action<string> log, CancellationToken token)
        {
            await _matrixSwitchLock.WaitAsync(token);
            try
            {
                string outNode = string.Equals(DmmChannel, "Port2", StringComparison.OrdinalIgnoreCase) ? "O31" : "O30";
                bool ok = await MatrixControlService.Instance.ConnectNodesAsync("I3", outNode, 7, "192.168.1.3");
                log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM->VM] 矩阵开关通路: I3->{outNode} slot=7 ip=192.168.1.3, ok={ok}");
            }
            finally
            {
                _matrixSwitchLock.Release();
            }
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

                bool ok1 = await MatrixControlService.Instance.ConnectNodesAsync("I4", "O6", 4, "192.168.1.3");
                _fixedMatrixConnected = ok1;
                log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM->VM] 矩阵开关通路(固定): I4->O6 slot=4 ip=192.168.1.3, ok={ok1}");
            }
            finally
            {
                _matrixSwitchLock.Release();
            }
        }

        private async Task DisconnectMatrixAsync(Action<string> log, CancellationToken token)
        {
            await _matrixSwitchLock.WaitAsync(token);
            try
            {
                bool ok1 = await MatrixControlService.Instance.DisconnectNodesAsync("I4", "O6", 4, "192.168.1.3");
                log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM->VM] 矩阵开关断开(固定): I4->O6 slot=4 ip=192.168.1.3, ok={ok1}");

                // DMM输出通路：不区分当前选择，直接把两条都断开，避免切换遗留
                bool ok2 = await MatrixControlService.Instance.DisconnectNodesAsync("I3", "O30", 7, "192.168.1.3");
                log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM->VM] 矩阵开关断开: I3->O30 slot=7 ip=192.168.1.3, ok={ok2}");

                bool ok3 = await MatrixControlService.Instance.DisconnectNodesAsync("I3", "O31", 7, "192.168.1.3");
                log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM->VM] 矩阵开关断开: I3->O31 slot=7 ip=192.168.1.3, ok={ok3}");

                _fixedMatrixConnected = false;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM->VM] 矩阵开关断开失败: {ex.Message}");
            }
            finally
            {
                _matrixSwitchLock.Release();
            }
        }

        private async Task OnSimulationOutputEnabledAsync(Action<string> log, CancellationToken token)
        {
            // 1) 切矩阵开关通路
            try
            {
                await EnsureFixedMatrixConnectedAsync(log, token);
            }
            catch (Exception ex)
            {
                log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM->VM] 矩阵开关切换失败: {ex.Message}");
            }

            // 2) 启动万用表电压回采（档位轮询）
            try
            {
                await EnsureDmmConnectedAsync(token);
                StartDmmVoltageRangePolling(token);
            }
            catch (Exception ex)
            {
                log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM->VM] 启动万用表回采失败: {ex.Message}");
            }
        }

        private async Task EnsureDmmConnectedAsync(CancellationToken token)
        {
            if (_dmmSession != null)
                return;

            // 当前项目里 DMM 面板使用 TCPIP0::<ip>::5555::SOCKET
            // 这里先沿用固定 IP，后续如需做成可配置，再从 Project/设备树里取。
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

        private void StartDmmVoltageRangePolling(CancellationToken token)
        {
            if (_dmmPollingTask != null && !_dmmPollingTask.IsCompleted)
                return;

            _dmmPollingCts?.Cancel();
            _dmmPollingCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            var ct = _dmmPollingCts.Token;

            _dmmPollingTask = Task.Run(async () =>
            {
                // 档位轮询：200mV/2V/20V/200V/750V
                // 说明：不同型号DMM对量程指令支持可能不同，这里采用“尝试设置量程 -> 查询 :MEAS:VOLT:DC?”的容错策略。
                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        var raw = await QueryDmmStringAsync(":MEAS:VOLT:DC?", ct).ConfigureAwait(false);
                        raw = raw?.Trim();

                        DmmVoltageText = FormatVoltageReading(raw);
                        LatestDmmVoltage = TryParseVoltageReading(raw, out var v) ? v : null;
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        DmmVoltageText = $"回采失败: {ex.Message}";
                    }

                    await Task.Delay(300, ct).ConfigureAwait(false);
                }
            }, ct);
        }

        private async Task TryApplyDmmVoltageRangeAsync(int rangeIndex, CancellationToken token)
        {
            if (_dmmSession == null)
                return;

            // 参考 DmmTestPanelViewModel 对频率量程的写法：优先走 :MEASure:<FUNC> <rangeIndex>
            // 这里用 :MEASure:VOLTage:DC <rangeIndex>，失败则忽略（继续用自动量程/默认量程读值）。
            try
            {
                await SendDmmCommandAsync($":MEASure:VOLTage:DC {rangeIndex}", token).ConfigureAwait(false);
            }
            catch
            {
                try
                {
                    await SendDmmCommandAsync($"VOLT:DC {rangeIndex}", token).ConfigureAwait(false);
                }
                catch
                {
                }
            }
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

        private async Task SendDmmCommandAsync(string command, CancellationToken token)
        {
            if (_dmmSession == null)
                throw new InvalidOperationException("DMM会话未建立");

            await _dmmIoLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                var cmd = command.EndsWith("\n", StringComparison.Ordinal) ? command : command + "\n";
                _dmmSession.RawIO.Write(cmd);
            }
            finally
            {
                _dmmIoLock.Release();
            }
        }

        private static string FormatVoltageReading(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return "--";

            var s = raw.Trim();
            if (s.Equals("OL", StringComparison.OrdinalIgnoreCase) ||
                s.IndexOf("OVER", StringComparison.OrdinalIgnoreCase) >= 0 ||
                s.IndexOf("OVLD", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "超出量程";
            }

            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var dv))
            {
                return $"{dv:0.00000} V";
            }

            return s;
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
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out voltage);
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

            var task = _dmmPollingTask;
            if (task != null)
            {
                try
                {
                    await Task.WhenAny(task, Task.Delay(500)).ConfigureAwait(true);
                }
                catch
                {
                }
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

        public void Dispose()
        {
            try
            {
                if (_projectSavingToken != null)
                {
                    _eventAggregator?.GetEvent<ProjectSavingEvent>()?.Unsubscribe(_projectSavingToken);
                    _projectSavingToken = null;
                }
            }
            catch
            {
            }

            try
            {
                StopDmmPollingAsync().GetAwaiter().GetResult();
            }
            catch
            {
            }

            try
            {
                DisconnectDmmAsync().GetAwaiter().GetResult();
            }
            catch
            {
            }
        }

        private bool CanSendEnterAtp()
        {
            if (IsBusy) return false;
            if (!IsManualTestRunning && !IsAutoTestRunning) return false;
            if (string.IsNullOrWhiteSpace(EnterAtpTxChannel) || string.IsNullOrWhiteSpace(EnterAtpRxChannel)) return false;
            return !string.Equals(EnterAtpTxChannel, EnterAtpRxChannel, StringComparison.OrdinalIgnoreCase);
        }

        private void OnSendEnterAtp()
        {
            _ = SendEnterAtpAsync();
        }

        private async Task SendEnterAtpAsync()
        {
            if (!SendEnterAtpCommand.CanExecute())
                return;

            IsBusy = true;
            try
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 发送进入ATP：TX={EnterAtpTxChannel}, RX={EnterAtpRxChannel}, Label=0x{DefaultLabel:X2}, Data=00 01 00 01 00 00 00 00");

                var token = _opCts?.Token ?? CancellationToken.None;
                var resp = await _simulation.SendBenchCommandAndWaitAsync(
                    EnterAtpTxChannel,
                    EnterAtpRxChannel,
                    DefaultLabel,
                    EnterAtpCommand,
                    b => b.SequenceEqual(EnterAtpOk),
                    timeoutMs: 2000,
                    msg => AddLog(msg),
                    token);

                if (resp == null)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 进入ATP超时，未收到OK");
                    return;
                }

                AddLog($"[{DateTime.Now:HH:mm:ss}] 收到ATP OK，进入ATP成功");
                IsInAtpMode = true;
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 进入ATP失败: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private bool CanSendSetVoltage()
        {
            if (IsBusy) return false;
            if (!IsInAtpMode) return false;
            if (string.IsNullOrWhiteSpace(SetVoltageTxChannel) || string.IsNullOrWhiteSpace(SetVoltageRxChannel)) return false;
            return !string.Equals(SetVoltageTxChannel, SetVoltageRxChannel, StringComparison.OrdinalIgnoreCase);
        }

        private void OnSendSetVoltage()
        {
            _ = SendSetVoltageAsync();
        }

        private async Task SendSetVoltageAsync()
        {
            if (!SendSetVoltageCommand.CanExecute())
                return;

            IsBusy = true;
            try
            {
                var token = _opCts?.Token ?? CancellationToken.None;
                await EnsureFixedMatrixConnectedAsync(msg => AddLog(msg), token);
                await SwitchMatrixForSelectedDmmChannelAsync(msg => AddLog(msg), token);

                AddLog($"[{DateTime.Now:HH:mm:ss}] 发送开启输出：TX={SetVoltageTxChannel}, RX={SetVoltageRxChannel}, Label=0x{DefaultLabel:X2}, Data=01 04 01 01 00 00 00 00");
                var resp = await _simulation.SendBenchCommandAndWaitAsync(
                    SetVoltageTxChannel,
                    SetVoltageRxChannel,
                    DefaultLabel,
                    EnableOutputCommand,
                    b => b.SequenceEqual(EnableOutputAck),
                    timeoutMs: 2000,
                    msg => AddLog(msg),
                    token);

                if (resp == null)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 开启输出超时，未收到ACK");
                    return;
                }

                AddLog($"[{DateTime.Now:HH:mm:ss}] 收到开启输出ACK");
                OutputEnabled = true;

                // 简单判定：收到回采上报的电压 (01 04 01 02 vv vv 00 00) 并在范围内即 PASS
                await EvaluateResultFromTelemetryAsync(token);
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 开启输出失败: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task EvaluateResultFromTelemetryAsync(CancellationToken token)
        {
            // 取样窗口（可按你后续规则调整）：2秒内收到任意一帧回采上报，且电压在[2.25,2.75]V则PASS，否则FAIL
            var deadline = DateTime.UtcNow.AddSeconds(2);
            bool got = false;
            bool pass = false;
            bool dmmOk = false;
            double? lastTelemetry = null;

            while (!token.IsCancellationRequested && DateTime.UtcNow <= deadline)
            {
                var resp = await _simulation.WaitBenchResponseAsync(
                    TelemetryRxChannel,
                    DefaultLabel,
                    b => b != null && b.Length == 8 && b[0] == 0x01 && b[1] == 0x04 && b[2] == 0x01 && b[3] == 0x02,
                    timeoutMs: 300,
                    msg => AddLog(msg),
                    token);

                if (resp == null)
                {
                    await Task.Delay(50, token);
                    continue;
                }

                got = true;
                ushort mv = (ushort)((resp[4] << 8) | resp[5]);
                double v = mv / 1000.0;
                LatestTelemetryVoltage = v;
                TelemetryVoltageText = $"{v:0.000} V";
                lastTelemetry = v;
                AddLog($"[{DateTime.Now:HH:mm:ss}] 回采上报: {v:F3}V");

                var dmm = LatestDmmVoltage;
                dmmOk = dmm.HasValue && dmm.Value >= 13.5 && dmm.Value <= 16.5;
                bool teleOk = v >= 2.25 && v <= 2.75;
                pass = dmmOk && teleOk;
                if (pass)
                    break;
            }

            LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            LastTestResult = (!got) ? "FAIL" : (pass ? "PASS" : "FAIL");
        }

        private bool CanSendExitAtp()
        {
            if (IsBusy) return false;
            if (!IsManualTestRunning && !IsAutoTestRunning) return false;
            if (!OutputEnabled) return false;
            if (string.IsNullOrWhiteSpace(ExitAtpTxChannel) || string.IsNullOrWhiteSpace(ExitAtpRxChannel)) return false;
            return !string.Equals(ExitAtpTxChannel, ExitAtpRxChannel, StringComparison.OrdinalIgnoreCase);
        }

        private void OnSendExitAtp()
        {
            _ = SendExitAtpAsync(stopAfter: true);
        }

        private async Task SendExitAtpAsync(bool stopAfter)
        {
            if (!SendExitAtpCommand.CanExecute())
                return;

            IsBusy = true;
            try
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 发送退出ATP：TX={ExitAtpTxChannel}, RX={ExitAtpRxChannel}, Label=0x{DefaultLabel:X2}, Data=00 02 00 01 00 00 00 01");

                var token = _opCts?.Token ?? CancellationToken.None;
                var resp = await _simulation.SendBenchCommandAndWaitAsync(
                    ExitAtpTxChannel,
                    ExitAtpRxChannel,
                    DefaultLabel,
                    ExitAtpCommand,
                    b => b.SequenceEqual(ExitAtpOk),
                    timeoutMs: 2000,
                    msg => AddLog(msg),
                    token);

                if (resp == null)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 退出ATP超时，未收到OK");
                }
                else
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 收到退出ATP OK");
                }

                IsInAtpMode = false;
                OutputEnabled = false;
                await DisconnectMatrixAsync(msg => AddLog(msg), CancellationToken.None);

                if (stopAfter)
                {
                    // 约定：退出ATP = 结束本次测试
                    _ = StopTestAsync(sendExitAtp: false);
                }
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 退出ATP失败: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void UpdateCommandStates()
        {
            SendEnterAtpCommand.RaiseCanExecuteChanged();
            SendSetVoltageCommand.RaiseCanExecuteChanged();
            SendExitAtpCommand.RaiseCanExecuteChanged();
        }
    }
}
