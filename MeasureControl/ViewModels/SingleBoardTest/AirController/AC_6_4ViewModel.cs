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
using Ivi.Visa;
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
        private const string PersistKeyTelemetryRx = "TelemetryRxChannel";
        private const string PersistKeyExitAtpTx = "ExitAtpTxChannel";
        private const string PersistKeyExitAtpRx = "ExitAtpRxChannel";
        private const string PersistKeyArincRate = "ArincRate";
        private const string PersistKeyLastTestTime = "LastTestTime";
        private const string PersistKeyLastTestResult = "LastTestResult";

        private readonly ISingleBoardTestContextService _singleBoardTestContext;
        private readonly ProjectService _projectService;
        private readonly IEventAggregator _eventAggregator;
        private readonly AC_6_4Simulation _simulation = new AC_6_4Simulation();

        private ResourceManager _dmmResourceManager;
        private string _dmmInitError;
        private MessageBasedSession _dmmSession;
        private readonly SemaphoreSlim _dmmIoLock = new SemaphoreSlim(1, 1);
        private CancellationTokenSource _dmmPollingCts;
        private Task _dmmPollingTask;
        private readonly SemaphoreSlim _matrixSwitchLock = new SemaphoreSlim(1, 1);
        private bool _fixedMatrixConnected;

        private CancellationTokenSource _telemetryListeningCts;
        private Task _telemetryListeningTask;

        private CancellationTokenSource _samplingCts;
        private Task _samplingTask;

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
        private string _telemetryRxChannel;
        private string _exitAtpTxChannel;
        private string _exitAtpRxChannel;

        private double _arincRate = 100000.0;

        private string _dmmVoltageText;
        private string _telemetryVoltageText;

        private string _enterAtpRxDataText = "--";
        private string _telemetryRxDataText = "--";
        private string _exitAtpRxDataText = "--";

        private string _lastTestTime = "--";
        private string _lastTestResult = "--";
        private string _previousTestTime = "--";
        private string _previousTestResult = "--";

        public AC_6_4ViewModel(
            ISingleBoardTestContextService singleBoardTestContext,
            ProjectService projectService,
            IEventAggregator eventAggregator)
        {
            _singleBoardTestContext = singleBoardTestContext;
            _projectService = projectService;
            _eventAggregator = eventAggregator;
            _enterAtpTxChannel = "429_CH0";
            _enterAtpRxChannel = "429_CH1";
            _setVoltageTxChannel = "429_CH2";
            _telemetryRxChannel = "429_CH3";
            _exitAtpTxChannel = "429_CH8";
            _exitAtpRxChannel = "429_CH9";

            DmmVoltageText = "--";
            TelemetryVoltageText = "--";
            EnterAtpRxDataText = "--";
            TelemetryRxDataText = "--";
            ExitAtpRxDataText = "--";

            _simulation.OnOutputEnabledAsync = OnSimulationOutputEnabledAsync;

            ManualTestCommand = new DelegateCommand(OnManualTest);
            AutoTestCommand = new DelegateCommand(OnAutoTest);

            SendEnterAtpCommand = new DelegateCommand(OnSendEnterAtp, CanSendEnterAtp);
            SendSetVoltageCommand = new DelegateCommand(OnSendSetVoltage, CanSendSetVoltage);
            MeasureVoltageCommand = new DelegateCommand(OnMeasureVoltage, CanMeasureVoltage);
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

                DmmVoltageText = FormatVoltageReading(raw);
                LatestDmmVoltage = TryParseVoltageReading(raw, out var v) ? v : null;
                AddLog($"[{DateTime.Now:HH:mm:ss}] 万用表测量: {DmmVoltageText}");
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

        private static bool IsTelemetryPayload(byte[] b)
        {
            return b != null && b.Length == 8 && b[0] == 0x01 && b[1] == 0x04 && b[2] == 0x01 && b[3] == 0x02;
        }

        private static bool TryParseTelemetryVoltage(byte[] resp, out double voltage)
        {
            voltage = 0;
            if (!IsTelemetryPayload(resp))
                return false;

            // New protocol: bytes[4..7] = IEEE754 float, fixed endian (big-endian)
            try
            {
                var fbytes = new byte[4] { resp[4], resp[5], resp[6], resp[7] };//遥测数据
                if (BitConverter.IsLittleEndian)//小端            
                    Array.Reverse(fbytes);//大端
                float f = BitConverter.ToSingle(fbytes, 0);//遥测电压
                if (!float.IsNaN(f) && !float.IsInfinity(f) && f > -1000 && f < 1000)//遥测电压范围
                {
                    voltage = f;
                    return true;
                }
            }
            catch
            {
            }

            // Legacy fallback: bytes[4..5] = UInt16 millivolt (big-endian)
            ushort mv = (ushort)((resp[4] << 8) | resp[5]);
            voltage = mv / 1000.0;
            return true;
        }

        private void UpdateTelemetryUi(double voltage)
        {
            try
            {
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher != null && !dispatcher.CheckAccess())
                {
                    dispatcher.BeginInvoke(new Action(() =>
                    {
                        LatestTelemetryVoltage = voltage;
                        TelemetryVoltageText = $"{voltage:0.000} V";
                    }));
                }
                else
                {
                    LatestTelemetryVoltage = voltage;
                    TelemetryVoltageText = $"{voltage:0.000} V";
                }
            }
            catch
            {
            }
        }

        private void UpdateTelemetryRxDataText(byte[] resp)
        {
            try
            {
                var text = $"0x{FormatBytesHex(resp)}";
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher != null && !dispatcher.CheckAccess())
                {
                    dispatcher.BeginInvoke(new Action(() =>
                    {
                        TelemetryRxDataText = text;
                    }));
                }
                else
                {
                    TelemetryRxDataText = text;
                }
            }
            catch
            {
            }
        }

        private void StartTelemetryListeningIfNeeded()//开始监听遥测数据
        {
            if (_telemetryListeningTask != null)//如果遥测监听任务不为空
                return;
            if (string.IsNullOrWhiteSpace(TelemetryRxChannel))//遥测RX通道为空
                return;

            _telemetryListeningCts?.Cancel();
            _telemetryListeningCts?.Dispose();
            _telemetryListeningCts = new CancellationTokenSource();//创建遥测监听任务
            var token = _telemetryListeningCts.Token;//获取遥测监听任务的取消令牌

            _telemetryListeningTask = Task.Run(async () =>//开始遥测监听任务
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        var resp = await _simulation.WaitBenchResponseAsync(
                            TelemetryRxChannel, // 遥测接收通道（如429_CH3）
                            DefaultLabel,       // 默认标签0x6A
                            IsTelemetryPayload, // 判断是否为遥测数据的谓词（数据有效性判断函数）
                            timeoutMs: 300,     // 300ms超时时间
                            msg => { },        // 日志回调
                            token);            // 取消令牌

                        if (resp != null && TryParseTelemetryVoltage(resp, out var v))//解析遥测数据
                        {
                            UpdateTelemetryUi(v);//更新遥测电压
                            UpdateTelemetryRxDataText(resp);//更新遥测RX数据
                        }
                        else
                        {
                            await Task.Delay(30, token);
                        }
                    }
                    catch (OperationCanceledException)//操作取消异常
                    {
                        break;
                    }
                    catch
                    {
                        try
                        {
                            await Task.Delay(100, token);
                        }
                        catch
                        {
                            break;
                        }
                    }
                }
            }, token);
        }

        private async Task StopTelemetryListeningAsync()
        {
            try
            {
                _telemetryListeningCts?.Cancel();
            }
            catch
            {
            }

            var task = _telemetryListeningTask;
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

            _telemetryListeningTask = null;
            try
            {
                _telemetryListeningCts?.Dispose();
            }
            catch
            {
            }
            _telemetryListeningCts = null;
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

        public string EnterAtpRxDataText
        {
            get => _enterAtpRxDataText;
            private set => SetProperty(ref _enterAtpRxDataText, value);
        }

        public string TelemetryRxDataText
        {
            get => _telemetryRxDataText;
            private set => SetProperty(ref _telemetryRxDataText, value);
        }

        public string ExitAtpRxDataText
        {
            get => _exitAtpRxDataText;
            private set => SetProperty(ref _exitAtpRxDataText, value);
        }

        public DelegateCommand ManualTestCommand { get; }
        public DelegateCommand AutoTestCommand { get; }
        public DelegateCommand SendEnterAtpCommand { get; }
        public DelegateCommand SendSetVoltageCommand { get; }
        public DelegateCommand MeasureVoltageCommand { get; }
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

        public string PreviousTestTime
        {
            get => _previousTestTime;
            set => SetProperty(ref _previousTestTime, value);
        }

        public string PreviousTestResult
        {
            get => _previousTestResult;
            set => SetProperty(ref _previousTestResult, value);
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
                _telemetryRxChannel = Read(PersistKeyTelemetryRx) ?? _telemetryRxChannel;
                _exitAtpTxChannel = Read(PersistKeyExitAtpTx) ?? _exitAtpTxChannel;
                _exitAtpRxChannel = Read(PersistKeyExitAtpRx) ?? _exitAtpRxChannel;

                var rateText = Read(PersistKeyArincRate);
                if (double.TryParse(rateText, NumberStyles.Any, CultureInfo.InvariantCulture, out var rate) && rate > 0)
                    _arincRate = rate;

                _lastTestTime = Read(PersistKeyLastTestTime) ?? _lastTestTime;
                _lastTestResult = Read(PersistKeyLastTestResult) ?? _lastTestResult;

                RaisePropertyChanged(nameof(EnterAtpTxChannel));
                RaisePropertyChanged(nameof(EnterAtpRxChannel));
                RaisePropertyChanged(nameof(SetVoltageTxChannel));
                RaisePropertyChanged(nameof(TelemetryRxChannel));
                RaisePropertyChanged(nameof(ExitAtpTxChannel));
                RaisePropertyChanged(nameof(ExitAtpRxChannel));
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
                Upsert(PersistKeyTelemetryRx, TelemetryRxChannel);
                Upsert(PersistKeyExitAtpTx, ExitAtpTxChannel);
                Upsert(PersistKeyExitAtpRx, ExitAtpRxChannel);
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

            // 先清理之前的采集任务
            StopSamplingTask();

            IsBusy = true;
            try
            {
                IsManualTestRunning = true;
                IsInAtpMode = false;
                OutputEnabled = false;
                LastTestTime = "--";
                LastTestResult = "--";
                LatestDmmVoltage = null;
                LatestTelemetryVoltage = null;
                DmmVoltageText = "--";
                TelemetryVoltageText = "--";
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

                // 仿真模式：固定占用产品侧通道
                _simulation.SimProductRxChannelIndex = 4;
                _simulation.SimProductTxChannelIndex = 5;
                _simulation.ArincRate = ArincRate;

                await _simulation.StartAsync(EnterAtpTxChannel, EnterAtpRxChannel, msg => AddLog(msg));

                // 遥测监听在开启输出后启动，避免干扰ATP指令收发

                // 初始化矩阵开关（断开所有通路，防止其他测试遗留）
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
                IsBusy = false;
            }
        }

        private Task StopTestAsync()
        {
            return StopTestAsync(sendExitAtp: true);
        }

        private async Task StopTestAsync(bool sendExitAtp)
        {
            IsBusy = true;
            try
            {
                // 先清理采集任务
                StopSamplingTask();

                try { _opCts?.Cancel(); } catch { }

                await StopTelemetryListeningAsync();
                await StopDmmPollingAsync();

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
                TelemetryVoltageText = "--";
                LatestDmmVoltage = null;
                LatestTelemetryVoltage = null;
                EnterAtpRxDataText = "--";
                TelemetryRxDataText = "--";
                ExitAtpRxDataText = "--";
                _fixedMatrixConnected = false;

                try
                {
                    _opCts?.Cancel();
                    _opCts?.Dispose();
                    _opCts = null;
                }
                catch
                {
                }

                // 保留测试结果和时间，不清除（用户要求停止测试时保留结果显示）
                AddLog($"[{DateTime.Now:HH:mm:ss}] 测试已停止，资源已释放");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task StartAutoTestAsync()
        {
            if (IsBusy) return;

            // 先清理之前的采集任务
            StopSamplingTask();

            IsBusy = true;
            string testResult = "FAIL";
            try
            {
                IsAutoTestRunning = true;
                IsInAtpMode = false;
                OutputEnabled = false;
                LastTestTime = "--";
                LastTestResult = "--";
                LatestDmmVoltage = null;
                LatestTelemetryVoltage = null;
                DmmVoltageText = "--";
                TelemetryVoltageText = "--";
                _opCts?.Cancel();
                _opCts?.Dispose();
                _opCts = new CancellationTokenSource();

                try
                {
                    var api = Prism.Ioc.ContainerLocator.Container.Resolve(typeof(MeasureControl.Services.HardwareApis.IComponentPowerStateApi)) as MeasureControl.Services.HardwareApis.IComponentPowerStateApi;
                    if (api != null)
                        await api.ApplyComponent28VStateAsync(_opCts.Token);
                }
                catch { }

                // 自动测试使用固定通道：TX=429_CH0, RX=429_CH1
                const string autoTxChannel = "429_CH0";
                const string autoRxChannel = "429_CH1";
                const int stepTimeoutMs = 30000;

                AddLog($"[{DateTime.Now:HH:mm:ss}] ========== 自动测试开始 ==========");
                AddLog($"[{DateTime.Now:HH:mm:ss}] TX通道: {autoTxChannel}, RX通道: {autoRxChannel}");

                _simulation.SimProductRxChannelIndex = 4;
                _simulation.SimProductTxChannelIndex = 5;
                _simulation.ArincRate = ArincRate;

                await _simulation.StartAsync(autoTxChannel, autoRxChannel, msg => AddLog(msg));

                // 遥测监听在开启输出后启动，避免干扰ATP指令收发

                // 初始化矩阵开关（断开所有通路，防止其他测试遗留）
                _fixedMatrixConnected = false;
                await DisconnectMatrixAsync(msg => AddLog(msg), CancellationToken.None);

                var token = _opCts.Token;

                // ========== 步骤1：进入ATP模式 ==========
                AddLog($"[{DateTime.Now:HH:mm:ss}] [步骤1/4] 进入ATP模式...");
                using (var stepCts = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    stepCts.CancelAfter(stepTimeoutMs);
                    bool enterOk = await AutoEnterAtpAsync(autoTxChannel, autoRxChannel, stepCts.Token);
                    if (!enterOk)
                    {
                        testResult = "FAIL (进入ATP超时)";
                        AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试失败：进入ATP超时");
                        return;
                    }
                }
                IsInAtpMode = true;
                AddLog($"[{DateTime.Now:HH:mm:ss}] [步骤1/4] 进入ATP成功");

                await Task.Delay(200, token);

                // ========== 步骤2：控制输出电压 ==========
                AddLog($"[{DateTime.Now:HH:mm:ss}] [步骤2/4] 发送控制输出电压指令...");
                await EnsureFixedMatrixConnectedAsync(msg => AddLog(msg), token);
                await SwitchMatrixForSelectedDmmChannelAsync(msg => AddLog(msg), token);

                AddLog($"[{DateTime.Now:HH:mm:ss}] 发送开启输出：TX={autoTxChannel}, Label=0x{DefaultLabel:X2}");
                await _simulation.SendBenchCommandOnlyAsync(
                    autoTxChannel,
                    DefaultLabel,
                    EnableOutputCommand,
                    msg => AddLog(msg),
                    token);
                OutputEnabled = true;
                AddLog($"[{DateTime.Now:HH:mm:ss}] [步骤2/4] 开启输出指令已发送");

                await Task.Delay(500, token);

                // ========== 步骤3：采集并判定 ==========
                AddLog($"[{DateTime.Now:HH:mm:ss}] [步骤3/4] 开始采集并判定...");
                using (var stepCts = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    stepCts.CancelAfter(stepTimeoutMs);
                    testResult = await AutoEvaluateAsync(stepCts.Token);
                }
                AddLog($"[{DateTime.Now:HH:mm:ss}] [步骤3/4] 采集判定完成: {testResult}");

                await Task.Delay(200, token);

                // ========== 步骤4：退出ATP模式 ==========
                AddLog($"[{DateTime.Now:HH:mm:ss}] [步骤4/4] 退出ATP模式...");
                using (var stepCts = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    stepCts.CancelAfter(stepTimeoutMs);
                    bool exitOk = await AutoExitAtpAsync(autoTxChannel, autoRxChannel, stepCts.Token);
                    if (!exitOk)
                    {
                        AddLog($"[{DateTime.Now:HH:mm:ss}] 警告：退出ATP超时，但测试结果已记录");
                    }
                    else
                    {
                        AddLog($"[{DateTime.Now:HH:mm:ss}] [步骤4/4] 退出ATP成功");
                    }
                }
                IsInAtpMode = false;
                OutputEnabled = false;
            }
            catch (OperationCanceledException)
            {
                testResult = "FAIL (已取消)";
                AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试被取消");
            }
            catch (Exception ex)
            {
                testResult = "FAIL (异常)";
                AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试异常: {ex.Message}");
            }
            finally
            {
                // 保存上次测试结果（在更新本次结果之前）
                PreviousTestTime = LastTestTime;
                PreviousTestResult = LastTestResult;

                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                LastTestResult = testResult;
                AddLog($"[{DateTime.Now:HH:mm:ss}] ========== 自动测试结束: {testResult} ==========");

                await StopTelemetryListeningAsync();
                await StopDmmPollingAsync();
                await DisconnectDmmAsync();
                await DisconnectMatrixAsync(msg => AddLog(msg), CancellationToken.None);
                await _simulation.StopAsync(msg => AddLog(msg));

                IsAutoTestRunning = false;
                IsInAtpMode = false;
                OutputEnabled = false;
                IsBusy = false;
            }
        }

        private async Task<bool> AutoEnterAtpAsync(string txChannel, string rxChannel, CancellationToken token)
        {
            const int timeoutMs = 3000;

            AddLog($"[{DateTime.Now:HH:mm:ss}] 发送进入ATP：TX={txChannel}, RX={rxChannel}");

            try { await _simulation.ClearRxFifoAsync(rxChannel); } catch { }
            await Task.Delay(50, token);

            var resp = await _simulation.SendBenchCommandAndWaitAsync(
                txChannel,
                rxChannel,
                DefaultLabel,
                EnterAtpCommand,
                b => b.SequenceEqual(EnterAtpOk),
                timeoutMs: timeoutMs,
                msg => AddLog(msg),
                token);

            if (resp != null)
            {
                EnterAtpRxDataText = $"0x{FormatBytesHex(resp)}";
                return true;
            }

            AddLog($"[{DateTime.Now:HH:mm:ss}] 进入ATP超时，未收到OK");
            return false;
        }

        private async Task<string> AutoEvaluateAsync(CancellationToken token)
        {
            const int testCount = 5;
            const double dmmMin = 13.5;
            const double dmmMax = 16.5;
            const double teleMin = 2.25;
            const double teleMax = 2.75;
            const int stabilizeTimeoutSeconds = 10;

            double? lastDmm = null;
            double? lastTelemetry = null;
            var startTime = DateTime.UtcNow;

            // 阶段1：等待数据稳定
            AddLog($"[{DateTime.Now:HH:mm:ss}] 等待硬件稳定（{stabilizeTimeoutSeconds}秒超时）...");
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(200, token);
                }
                catch (OperationCanceledException)
                {
                    return "FAIL (已取消)";
                }

                var dmm = LatestDmmVoltage;
                var tele = LatestTelemetryVoltage;

                if (dmm.HasValue && dmm.Value != 0 && tele.HasValue && tele.Value != 0)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 硬件已稳定: DMM={dmm.Value:F3}V, 回采={tele.Value:F3}V");
                    break;
                }

                if ((DateTime.UtcNow - startTime).TotalSeconds > stabilizeTimeoutSeconds)
                {
                    return "FAIL (等待数据超时)";
                }
            }

            // 阶段2：只测5次
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

                var dmm = LatestDmmVoltage;
                var tele = LatestTelemetryVoltage;

                if (!dmm.HasValue || !tele.HasValue)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 采样#{i}: 数据无效 -> 不合格");
                    continue;
                }

                lastDmm = dmm;
                lastTelemetry = tele;

                bool dmmOk = dmm.Value >= dmmMin && dmm.Value <= dmmMax;
                bool teleOk = tele.Value >= teleMin && tele.Value <= teleMax;

                if (dmmOk && teleOk)
                {
                    passCount++;
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 采样#{i}: DMM={dmm.Value:F3}V, 回采={tele.Value:F3}V -> 合格");
                }
                else
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 采样#{i}: DMM={dmm.Value:F3}V(需{dmmMin}~{dmmMax}), 回采={tele.Value:F3}V(需{teleMin}~{teleMax}) -> 不合格");
                }
            }

            // 采集完成后停止DMM轮询和遥测监听，不再刷新显示
            await StopDmmPollingAsync();
            await StopTelemetryListeningAsync();
            _simulation.StopTelemetryOutput(); // 停止仿真侧遥测输出，避免继续打印日志

            if (passCount == testCount)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 最终值: 供电模块={lastDmm:F3}V, 回采={lastTelemetry:F3}V");
                return "PASS";
            }
            else if (token.IsCancellationRequested)
            {
                return "FAIL (超时)";
            }
            else
            {
                return $"FAIL ({passCount}/{testCount}次合格)";
            }
        }

        private async Task<bool> AutoExitAtpAsync(string txChannel, string rxChannel, CancellationToken token)
        {
            AddLog($"[{DateTime.Now:HH:mm:ss}] 发送退出ATP：TX={txChannel}, RX={rxChannel}");

            try
            {
                var resp = await _simulation.SendBenchCommandAndWaitAsync(
                    txChannel,
                    rxChannel,
                    DefaultLabel,
                    ExitAtpCommand,
                    b => b.SequenceEqual(ExitAtpOk),
                    timeoutMs: 2000,
                    msg => AddLog(msg),
                    token);

                if (resp != null)
                {
                    ExitAtpRxDataText = $"0x{FormatBytesHex(resp)}";
                    return true;
                }

                AddLog($"[{DateTime.Now:HH:mm:ss}] 退出ATP超时，未收到OK");
                return false;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 退出ATP异常: {ex.Message}");
                return false;
            }
        }

        private async Task SwitchMatrixForSelectedDmmChannelAsync(Action<string> log, CancellationToken token)
        {
            await EnsureFixedMatrixConnectedAsync(log, token);
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

                var task1 = MatrixControlService.Instance.ConnectNodesAsync("I1", "O16", 6, "192.168.1.3");
                var task2 = MatrixControlService.Instance.ConnectNodesAsync("I4", "O2", 4, "192.168.1.3");

                var results = await Task.WhenAll(task1, task2);
                bool ok1 = results.Length > 0 && results[0];
                bool ok2 = results.Length > 1 && results[1];

                _fixedMatrixConnected = results.All(r => r);
                log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM->VM] 矩阵开关通路(固定): I1->O16 slot=6 ip=192.168.1.3, ok={ok1}");
                log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM->VM] 矩阵开关通路(固定): I4->O2 slot=4 ip=192.168.1.3, ok={ok2}");
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
                var task1 = MatrixControlService.Instance.DisconnectNodesAsync("I1", "O16", 6, "192.168.1.3");
                var task2 = MatrixControlService.Instance.DisconnectNodesAsync("I4", "O2", 4, "192.168.1.3");

                var results = await Task.WhenAll(task1, task2);
                bool ok1 = results.Length > 0 && results[0];
                bool ok2 = results.Length > 1 && results[1];

                log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM->VM] 矩阵开关断开(固定): I1->O16 slot=6 ip=192.168.1.3, ok={ok1}");
                log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM->VM] 矩阵开关断开(固定): I4->O2 slot=4 ip=192.168.1.3, ok={ok2}");

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

            // 3) 启动遥测监听（在开启输出后启动，避免干扰ATP指令收发）
            StartTelemetryListeningIfNeeded();
            log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM->VM] 遥测监听已启动");
        }

        private async Task EnsureDmmConnectedAsync(CancellationToken token)
        {
            if (_dmmSession != null)
                return;

            if (!EnsureDmmResourceManagerAvailable())
                throw new InvalidOperationException(_dmmInitError ?? "DMM资源管理器初始化失败");

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

        private bool EnsureDmmResourceManagerAvailable()
        {
            if (_dmmResourceManager != null)
                return true;
            if (!string.IsNullOrWhiteSpace(_dmmInitError))
                return false;

            try
            {
                _dmmResourceManager = new ResourceManager();
                return true;
            }
            catch (DllNotFoundException ex)
            {
                _dmmInitError = ex.Message;
                return false;
            }
            catch (VisaException ex)
            {
                _dmmInitError = ex.Message;
                return false;
            }
            catch (Exception ex)
            {
                _dmmInitError = ex.Message;
                return false;
            }
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
                StopSamplingTask();
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
                StopTelemetryListeningAsync().GetAwaiter().GetResult();
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

            try
            {
                _dmmResourceManager?.Dispose();
            }
            catch
            {
            }
            finally
            {
                _dmmResourceManager = null;
            }

            try
            {
                _simulation.StopAsync(msg => { }).GetAwaiter().GetResult();
            }
            catch
            {
            }

            try
            {
                _opCts?.Cancel();
                _opCts?.Dispose();
                _opCts = null;
            }
            catch
            {
            }

            IsManualTestRunning = false;
            IsAutoTestRunning = false;
            IsInAtpMode = false;
            OutputEnabled = false;
            IsBusy = false;
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

        private async Task SendEnterAtpAsync()
        {
            if (!SendEnterAtpCommand.CanExecute())
                return;

            IsBusy = true;
            try
            {
                var token = _opCts?.Token ?? CancellationToken.None;
                const int timeoutMs = 3000;

                AddLog($"[{DateTime.Now:HH:mm:ss}] 发送进入ATP：TX={EnterAtpTxChannel}, RX={EnterAtpRxChannel}, Label=0x{DefaultLabel:X2}");

                try
                {
                    await _simulation.ClearRxFifoAsync(EnterAtpRxChannel);
                }
                catch
                {
                }

                await Task.Delay(50, token);

                var resp = await _simulation.SendBenchCommandAndWaitAsync(
                    EnterAtpTxChannel,
                    EnterAtpRxChannel,
                    DefaultLabel,
                    EnterAtpCommand,
                    b => b.SequenceEqual(EnterAtpOk),
                    timeoutMs: timeoutMs,
                    msg => AddLog(msg),
                    token);

                if (resp != null)
                {
                    EnterAtpRxDataText = $"0x{FormatBytesHex(resp)}";
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 收到ATP OK，进入ATP成功");
                    IsInAtpMode = true;
                }
                else
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 进入ATP超时，未收到OK");
                }
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
            return !string.IsNullOrWhiteSpace(SetVoltageTxChannel);
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

                AddLog($"[{DateTime.Now:HH:mm:ss}] 发送开启输出：TX={SetVoltageTxChannel}, Label=0x{DefaultLabel:X2}, Data=01 04 01 01 00 00 00 00");
                await _simulation.SendBenchCommandOnlyAsync(
                    SetVoltageTxChannel,
                    DefaultLabel,
                    EnableOutputCommand,
                    msg => AddLog(msg),
                    token);

                AddLog($"[{DateTime.Now:HH:mm:ss}] 开启输出指令已发送");
                OutputEnabled = true;

                // 启动后台采集任务（不阻塞），让退出ATP/停止测试随时可用
                StartSamplingTask();
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

        private void StartSamplingTask()
        {
            StopSamplingTask();

            _samplingCts = new CancellationTokenSource();
            var token = _samplingCts.Token;

            _samplingTask = Task.Run(async () =>
            {
                await EvaluateResultFromTelemetryAsync(token);
            }, token);
        }

        private void StopSamplingTask()
        {
            try
            {
                _samplingCts?.Cancel();
            }
            catch { }

            try
            {
                if (_samplingTask != null && !_samplingTask.IsCompleted)
                {
                    _samplingTask.Wait(500);
                }
            }
            catch { }

            _samplingCts?.Dispose();
            _samplingCts = null;
            _samplingTask = null;
        }

        private async Task EvaluateResultFromTelemetryAsync(CancellationToken token)
        {
            const int testCount = 5;//测试次数
            const double dmmMin = 13.5;//DMM电压下限
            const double dmmMax = 16.5;//DMM电压上限
            const double teleMin = 2.25;//回采电压下限
            const double teleMax = 2.75;//回采电压上限
            const int stabilizeTimeoutSeconds = 30;//稳定超时时间

            double? lastDmm = null;
            double? lastTelemetry = null;
            var startTime = DateTime.UtcNow;

            // 阶段1：等待数据稳定（电压值和回采值都有数据后跳出）
            AddLog($"[{DateTime.Now:HH:mm:ss}] 等待硬件稳定（{stabilizeTimeoutSeconds}秒超时）...");
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(200, token);
                }
                catch (OperationCanceledException)
                {
                    LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    LastTestResult = "FAIL";
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 测试结果: FAIL (已取消)");
                    return;
                }

                var dmm = LatestDmmVoltage;
                var tele = LatestTelemetryVoltage;

                if (dmm.HasValue && dmm.Value != 0 && tele.HasValue && tele.Value != 0)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 硬件已稳定: DMM={dmm.Value:F3}V, 回采={tele.Value:F3}V");
                    break;
                }

                if ((DateTime.UtcNow - startTime).TotalSeconds > stabilizeTimeoutSeconds)
                {
                    LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    LastTestResult = "FAIL";
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 测试结果: FAIL (等待数据超时，请检查通道配置)");
                    return;
                }
            }

            // 阶段2：只测5次，判定PASS/FAIL
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

                var dmm = LatestDmmVoltage;
                var tele = LatestTelemetryVoltage;

                if (!dmm.HasValue || !tele.HasValue)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 采样#{i}: 数据无效 -> 不合格");
                    continue;
                }

                lastDmm = dmm;
                lastTelemetry = tele;

                bool dmmOk = dmm.Value >= dmmMin && dmm.Value <= dmmMax;
                bool teleOk = tele.Value >= teleMin && tele.Value <= teleMax;

                if (dmmOk && teleOk)
                {
                    passCount++;
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 采样#{i}: DMM={dmm.Value:F3}V, 回采={tele.Value:F3}V -> 合格");
                }
                else
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 采样#{i}: DMM={dmm.Value:F3}V(需{dmmMin}~{dmmMax}), 回采={tele.Value:F3}V(需{teleMin}~{teleMax}) -> 不合格");
                }
            }

            // 采集完成后停止DMM轮询和遥测监听，不再刷新显示
            await StopDmmPollingAsync();
            await StopTelemetryListeningAsync();
            _simulation.StopTelemetryOutput(); // 停止仿真侧遥测输出，避免继续打印日志

            // 保存上次测试结果（在更新本次结果之前）
            PreviousTestTime = LastTestTime;
            PreviousTestResult = LastTestResult;

            LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            if (passCount == testCount)
            {
                LastTestResult = "PASS";
                AddLog($"[{DateTime.Now:HH:mm:ss}] 测试结果: PASS ({testCount}次全部合格)");
                AddLog($"[{DateTime.Now:HH:mm:ss}] 最终值: 供电模块={lastDmm:F3}V, 回采={lastTelemetry:F3}V");
            }
            else if (token.IsCancellationRequested)
            {
                LastTestResult = "FAIL";
                AddLog($"[{DateTime.Now:HH:mm:ss}] 测试结果: FAIL (已取消)");
            }
            else
            {
                LastTestResult = "FAIL";
                AddLog($"[{DateTime.Now:HH:mm:ss}] 测试结果: FAIL ({passCount}/{testCount}次合格)");
            }
        }

        private bool CanSendExitAtp()
        {
            // 不检查IsBusy，让退出ATP按钮在采集过程中也可用
            if (!IsManualTestRunning && !IsAutoTestRunning) return false;
            if (string.IsNullOrWhiteSpace(ExitAtpTxChannel) || string.IsNullOrWhiteSpace(ExitAtpRxChannel)) return false;
            return true;
        }

        private void OnSendExitAtp()
        {
            _ = SendExitAtpAsync(stopAfter: true);
        }

        private async Task SendExitAtpAsync(bool stopAfter)
        {
            // 先停止采集任务
            StopSamplingTask();

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
                    ExitAtpRxDataText = $"0x{FormatBytesHex(resp)}";
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
            MeasureVoltageCommand.RaiseCanExecuteChanged();
            SendExitAtpCommand.RaiseCanExecuteChanged();
        }

        private static string FormatBytesHex(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                return "--";
            return string.Join(" ", bytes.Select(b => b.ToString("X2")));
        }
    }
}
