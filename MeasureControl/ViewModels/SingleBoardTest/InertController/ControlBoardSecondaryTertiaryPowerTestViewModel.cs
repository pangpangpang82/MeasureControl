using Prism.Commands;
using Prism.Events;
using Prism.Ioc;
using Prism.Mvvm;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using MeasureControl.Events;
using MeasureControl.Models;
using MeasureControl.Models.Devices;
using MeasureControl.Services;
using MeasureControl.Services.HardwareApis;

namespace MeasureControl.ViewModels.SingleBoardTest.InertController
{
    public sealed class ControlBoardSecondaryTertiaryPowerTestViewModel : BindableBase, IDisposable
    {
        private const string TestItemKey = "InertController_ControlBoard_SecondaryTertiaryPower";
        private const string PowerSupplyIpAddress = "192.168.1.15";
        private const double InputVoltageV = 28.0;
        private const double InputCurrentA = 1.0;
        private const int PowerOnDelayMs = 1000;

        public bool SkipMainPowerOff { get; set; }

        private const int ArincRxChannelIndex = 0;
        private const int ArincTxChannelIndex = 1;
        private const double ArincRate = 100000.0;
        private const int ArincPollIntervalMs = 50;
        private const int ReceiveTimeoutMs = 3000;
        private const int ItemCollectSettleDelayMs = 400;
        private const int FirstItemExtraCollectSettleDelayMs = 100;
        private const byte ArincExpectedSdi = 1;
        private const uint AtpSsmDataSdi = 0xC10001u;
        private const byte AtpLabelOctal174Dec = 124;
        private const double VoltageResolution = 0.01;
        private const uint VoltageDataMask12 = 0x0FFFu;
        private const ushort VoltageSignMagnitudeMask11 = 0x07FF;
        private const int Signed12BitSignMask = 0x0800;
        private const int Signed12BitRange = 0x1000;

        private readonly ISingleBoardTestContextService _singleBoardTestContext;
        private readonly ProjectService _projectService;
        private readonly IEventAggregator _eventAggregator;
        private readonly IPxiChassisService _pxiChassisService;
        private readonly IComponentPowerStateApi _componentPowerStateApi;

        private readonly SemaphoreSlim _measureLock = new SemaphoreSlim(1, 1);
        private CancellationTokenSource _cts;

        private IPowerSupplyApi _power;
        private IArt4229Api _arinc;
        private bool _arincRxOpened;
        private bool _atpTxOpened;
        private bool _atpModeEntered;

        private bool _isManualTestRunning;
        private bool _isAutoTestRunning;
        private bool _isBusy;
        private bool _isPowerOn;
        private string _powerStatus = "未供电";
        private string _arincStatus = "429未连接";
        private string _overallResult = "--";
        private string _lastTestTime = "--";

        private SubscriptionToken _projectSavingToken;

        public ControlBoardSecondaryTertiaryPowerTestViewModel(
            ISingleBoardTestContextService singleBoardTestContext,
            ProjectService projectService,
            IEventAggregator eventAggregator,
            IPxiChassisService pxiChassisService = null,
            IComponentPowerStateApi componentPowerStateApi = null)
        {
            _singleBoardTestContext = singleBoardTestContext;
            _projectService = projectService;
            _eventAggregator = eventAggregator;
            _pxiChassisService = pxiChassisService;
            _componentPowerStateApi = componentPowerStateApi;

            ManualTestCommand = new DelegateCommand(async () => await OnManualTestAsync());
            AutoTestCommand = new DelegateCommand(async () => await OnAutoTestAsync());
            ClearLogCommand = new DelegateCommand(() => Logs.Clear());

            Items.Add(new PowerItemViewModel(this, "1)", "控制板-15V", "131", nominal: -15.0, tolerance: 0.75, resolution: VoltageResolution, useSigned12Bit: false, useSignMagnitude12Bit: true));
            Items.Add(new PowerItemViewModel(this, "2)", "控制板+15V", "132", nominal: 15.0, tolerance: 0.75, resolution: VoltageResolution, useSigned12Bit: true));
            Items.Add(new PowerItemViewModel(this, "3)", "控制板5V", "152", nominal: 5.0, tolerance: 0.25, resolution: VoltageResolution, useSigned12Bit: false, useSignMagnitude12Bit: false));
            Items.Add(new PowerItemViewModel(this, "4)", "控制板3.3V", "153", nominal: 3.3, tolerance: 0.165, resolution: VoltageResolution, useSigned12Bit: false, useSignMagnitude12Bit: false));
            Items.Add(new PowerItemViewModel(this, "5)", "控制板1.5V", "154", nominal: 1.5, tolerance: 0.075, resolution: VoltageResolution, useSigned12Bit: false, useSignMagnitude12Bit: false));

            LoadPersistedState();
            _projectSavingToken = _eventAggregator?.GetEvent<ProjectSavingEvent>()?.Subscribe(OnProjectSaving);
        }

        public ObservableCollection<PowerItemViewModel> Items { get; } = new ObservableCollection<PowerItemViewModel>();

        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();

        public DelegateCommand ManualTestCommand { get; }

        public DelegateCommand AutoTestCommand { get; }

        public DelegateCommand ClearLogCommand { get; }

        public bool IsManualTestRunning
        {
            get => _isManualTestRunning;
            private set
            {
                if (SetProperty(ref _isManualTestRunning, value))
                    RaiseCanExecuteChangedForItems();
            }
        }

        public bool IsAutoTestRunning
        {
            get => _isAutoTestRunning;
            private set
            {
                if (SetProperty(ref _isAutoTestRunning, value))
                    RaiseCanExecuteChangedForItems();
            }
        }

        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (SetProperty(ref _isBusy, value))
                    RaiseCanExecuteChangedForItems();
            }
        }

        public bool IsPowerOn
        {
            get => _isPowerOn;
            private set
            {
                if (SetProperty(ref _isPowerOn, value))
                    RaiseCanExecuteChangedForItems();
            }
        }

        public string PowerStatus
        {
            get => _powerStatus;
            private set => SetProperty(ref _powerStatus, value);
        }

        public string ArincStatus
        {
            get => _arincStatus;
            private set => SetProperty(ref _arincStatus, value);
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

        private string PersistDataKey
        {
            get
            {
                var taskName = _singleBoardTestContext?.TestTaskName ?? string.Empty;
                if (string.IsNullOrWhiteSpace(taskName))
                    return TestItemKey;
                return $"{taskName}/{TestItemKey}";
            }
        }

        private async Task OnManualTestAsync()
        {
            if (IsManualTestRunning)
            {
                await StopTestAsync().ConfigureAwait(false);
                return;
            }

            // 检查是否已总上电
            var _hps = ContainerLocator.Container.Resolve<IBoardPowerService>();
            if (_hps == null || !_hps.IsPowered)
            {
                MessageBox.Show("请先点击左上角组件上电按钮进行总上电，再进行测试。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (IsAutoTestRunning)
                await StopTestAsync().ConfigureAwait(false);

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
                await PrepareEnvironmentAsync(_cts.Token).ConfigureAwait(false);
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

        public async Task<string> RunOnceAsync(CancellationToken cancellationToken)
        {
            if (IsAutoTestRunning || IsManualTestRunning)
            {
                return OverallResult;
            }

            _cts?.Cancel();
            _cts?.Dispose();
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            ResetResults();
            OverallResult = "--";
            LastTestTime = "--";

            IsAutoTestRunning = true;
            IsManualTestRunning = false;

            Log("开始自动测试");

            try
            {
                await PrepareEnvironmentAsync(_cts.Token).ConfigureAwait(false);

                foreach (var item in Items)
                {
                    _cts.Token.ThrowIfCancellationRequested();
                    await CollectAsync(item, _cts.Token).ConfigureAwait(false);
                    await Task.Delay(100, _cts.Token).ConfigureAwait(false);
                }

                EvaluateOverall();
                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                Log($"自动测试完成，总体结果: {OverallResult}");

                await StopTestAsync().ConfigureAwait(false);
                return OverallResult;
            }
            catch (OperationCanceledException)
            {
                await StopTestAsync().ConfigureAwait(false);
                return "FAIL";
            }
            catch (Exception ex)
            {
                Log($"自动测试失败: {ex.Message}");
                await StopTestAsync().ConfigureAwait(false);
                return "FAIL";
            }
        }

        private async Task OnAutoTestAsync()
        {
            if (IsAutoTestRunning)
            {
                await StopTestAsync().ConfigureAwait(false);
                return;
            }

            // 检查是否已总上电
            var _hps = ContainerLocator.Container.Resolve<IBoardPowerService>();
            if (_hps == null || !_hps.IsPowered)
            {
                MessageBox.Show("请先点击左上角组件上电按钮进行总上电，再进行测试。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (IsManualTestRunning)
                await StopTestAsync().ConfigureAwait(false);

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
                await PrepareEnvironmentAsync(_cts.Token).ConfigureAwait(false);

                foreach (var item in Items)
                {
                    _cts.Token.ThrowIfCancellationRequested();
                    await CollectAsync(item, _cts.Token).ConfigureAwait(false);
                    await Task.Delay(100, _cts.Token).ConfigureAwait(false);
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

        private async Task PrepareEnvironmentAsync(CancellationToken token)
        {
            Log($"程控电源准备: IP={PowerSupplyIpAddress}, CH1={InputVoltageV:0.###}V/{InputCurrentA:0.###}A");
            await EnsurePowerAsync(token).ConfigureAwait(false);
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                IsPowerOn = true;
                PowerStatus = "CH1已输出28V";
            });

            await Task.Delay(PowerOnDelayMs, token).ConfigureAwait(false);
            Log($"上电稳定等待 {PowerOnDelayMs}ms 完成，开始准备429并进入ATP模式");

            var arincReady = await EnsureArincReadyAsync(token).ConfigureAwait(false);
            if (!arincReady)
                throw new InvalidOperationException("429板卡未就绪，无法进入ATP模式");

            await EnsureAtpModeAsync(token).ConfigureAwait(false);
            Application.Current?.Dispatcher?.Invoke(() => ArincStatus = "429已连接，ATP已进入");
        }

        internal bool CanCollectItem(PowerItemViewModel item)
        {
            if (item == null)
                return false;
            return IsManualTestRunning && !IsBusy && IsPowerOn;
        }

        internal async Task CollectAsync(PowerItemViewModel item)
        {
            if (item == null)
                return;

            var token = _cts?.Token ?? CancellationToken.None;
            await CollectAsync(item, token).ConfigureAwait(false);

            EvaluateOverall();
            if (Items.All(i => i.IsMeasured))
                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        private async Task CollectAsync(PowerItemViewModel item, CancellationToken token)
        {
            await _measureLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                IsBusy = true;
                Log($"开始采集: {item.Name}, 源Label(oct)={item.ProtocolLabelText}, 接收Label(dec)={item.ReceiveLabelDec}");

                var settleDelayMs = GetCollectSettleDelayMs(item);
                if (settleDelayMs > 0)
                {
                    Log($"采集等待: {item.Name} 延时{settleDelayMs}ms后开始读取429");
                    await Task.Delay(settleDelayMs, token).ConfigureAwait(false);
                }

                var result = await ReadVoltageByLabelAsync(item, token).ConfigureAwait(false);
                if (!result.HasValue)
                {
                    item.UpdateMeasurement(null, "超时", "FAIL", measured: true);
                    Log($"采集失败: 未在超时时间内接收到 源Label(oct)={item.ProtocolLabelText} / 接收Label(dec)={item.ReceiveLabelDec} 的429数据");
                    return;
                }

                var voltage = result.Value;
                var text = voltage.ToString("0.###", CultureInfo.InvariantCulture);
                var pass = Math.Abs(voltage - item.Nominal) <= item.Tolerance;
                item.UpdateMeasurement(voltage, text, pass ? "PASS" : "FAIL", measured: true);
                Log($"采集完成: {item.Name}={text}V, 判据={item.Nominal:0.###}±{item.Tolerance:0.###}V => {(pass ? "PASS" : "FAIL")}");
            }
            finally
            {
                IsBusy = false;
                _measureLock.Release();
            }
        }

        private int GetCollectSettleDelayMs(PowerItemViewModel item)
        {
            if (item == null)
                return ItemCollectSettleDelayMs;

            var delayMs = ItemCollectSettleDelayMs;
            if (ReferenceEquals(item, Items.FirstOrDefault()))
                delayMs += FirstItemExtraCollectSettleDelayMs;

            return delayMs;
        }

        private async Task<double?> ReadVoltageByLabelAsync(PowerItemViewModel item, CancellationToken token)
        {
            if (!await EnsureArincReadyAsync(token).ConfigureAwait(false))
                return null;

            try
            {
                //await _arinc.ReadRxWordsAsync(ArincRxChannelIndex, maxCount: 1024, cancellationToken: token).ConfigureAwait(false);
            }
            catch
            {
            }

            var deadline = DateTime.UtcNow.AddMilliseconds(ReceiveTimeoutMs);
            while (DateTime.UtcNow <= deadline)
            {
                token.ThrowIfCancellationRequested();

                var words = await _arinc.ReadRxWordsAsync(ArincRxChannelIndex, maxCount: 256, enableTimeTag: false, enableRateAdaption: false, cancellationToken: token).ConfigureAwait(false);
                if (words != null && words.Count > 0)
                {
                    foreach (var word in words.Reverse())
                    {
                        _arinc.ParseRawWord(word.Data429, out var rxLabel, out var sdi, out var data19, out var ssm);
                        if (rxLabel != item.ReceiveLabelDec)
                            continue;
                        

                        var raw12 = (ushort)(data19 & VoltageDataMask12);
                        var voltage = DecodeVoltageFromProtocol(raw12, item);
                        Log($"429接收: {item.Name}, 源Label(oct)={item.ProtocolLabelText}, 接收Label(dec)={item.ReceiveLabelDec}, RxLabel={rxLabel}, SDI={sdi}, SSM={ssm}, Data19=0x{data19:X5}, Raw12=0x{raw12:X3}({raw12}), 分辨率={item.Resolution:0.##}, 解析值={voltage:0.###}V");
                        return voltage;
                    }
                }

                await Task.Delay(ArincPollIntervalMs, token).ConfigureAwait(false);
            }

            return null;
        }

        private static double DecodeVoltageFromProtocol(ushort raw12, PowerItemViewModel item)
        {
            if (item.UseSignMagnitude12Bit)
            {
                var isNegative = (raw12 & Signed12BitSignMask) != 0;
                var magnitude = raw12 & VoltageSignMagnitudeMask11;
                var value = magnitude * item.Resolution;
                return isNegative ? -value : value;
            }

            if (item.UseSigned12Bit)
            {
                var signedValue = (raw12 & Signed12BitSignMask) != 0
                    ? raw12 - Signed12BitRange
                    : raw12;
                return signedValue * item.Resolution;
            }

            return raw12 * item.Resolution;
        }

        private async Task<bool> EnsureArincReadyAsync(CancellationToken token)
        {
            if (_arinc != null && _arinc.IsConnected && _arincRxOpened)
                return true;

            if (_pxiChassisService == null)
            {
                Log("未注入PXI机箱服务，无法自动查找429板卡");
                return false;
            }

            if (_arinc == null)
            {
                DeviceBase dev = null;
                try
                {
                    var chassisList = _pxiChassisService.GetAllChassis();
                    if (chassisList != null)
                    {
                        foreach (var chassis in chassisList)
                        {
                            if (chassis?.Devices == null)
                                continue;

                            dev = chassis.Devices.FirstOrDefault(d =>
                                (d?.Model?.IndexOf("4227", StringComparison.OrdinalIgnoreCase) >= 0) ||
                                (d?.Model?.IndexOf("4229", StringComparison.OrdinalIgnoreCase) >= 0) ||
                                (d?.Model?.IndexOf("ARINC", StringComparison.OrdinalIgnoreCase) >= 0) ||
                                (d?.Model?.IndexOf("429", StringComparison.OrdinalIgnoreCase) >= 0) ||
                                (d?.Name?.IndexOf("4227", StringComparison.OrdinalIgnoreCase) >= 0) ||
                                (d?.Name?.IndexOf("4229", StringComparison.OrdinalIgnoreCase) >= 0) ||
                                (d?.Name?.IndexOf("429", StringComparison.OrdinalIgnoreCase) >= 0));
                            if (dev != null)
                                break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log($"查找429板卡失败: {ex.Message}");
                }

                if (dev != null)
                {
                    _arinc = new Art4229Api(dev, deviceIndex: 0);
                    _arincRxOpened = false;
                    _atpTxOpened = false;
                    _atpModeEntered = false;
                }
            }

            if (_arinc == null)
            {
                Log("未找到ART4229(ARINC429)板卡");
                Application.Current?.Dispatcher?.Invoke(() => ArincStatus = "429未找到");
                return false;
            }

            try
            {
                if (!_arinc.IsConnected)
                    await _arinc.ConnectAsync(token).ConfigureAwait(false);

                if (!_arincRxOpened)
                {
                    await _arinc.OpenRxAsync(ArincRxChannelIndex, token).ConfigureAwait(false);
                    await _arinc.ConfigureRxAsync(
                        ArincRxChannelIndex,
                        rate: ArincRate,
                        parity: Art4229Parity.Odd,
                        wordFormat: Art4229WordFormat.Standard429,
                        enableInterrupt: false,
                        interruptDepth: 512,
                        enableTimeTag: false,
                        cancellationToken: token).ConfigureAwait(false);
                    await _arinc.StartRxAsync(ArincRxChannelIndex, token).ConfigureAwait(false);
                    _arincRxOpened = true;
                }

                Application.Current?.Dispatcher?.Invoke(() => ArincStatus = "429接收已就绪");
                return true;
            }
            catch (Exception ex)
            {
                Log($"429初始化失败: {ex.Message}");
                Application.Current?.Dispatcher?.Invoke(() => ArincStatus = "429初始化失败");
                return false;
            }
        }

        private static byte ReverseLabelBits(byte label)
        {
            byte reversed = 0;
            for (var i = 0; i < 8; i++)
                reversed = (byte)((reversed << 1) | ((label >> i) & 0x01));
            return reversed;
        }

        private byte GetAtpLabelForTx()
        {
            if (_arinc != null)
                return _arinc.ReverseLabel(AtpLabelOctal174Dec);
            return ReverseLabelBits(AtpLabelOctal174Dec);
        }

        private uint BuildAtpEnterWord(out byte txLabel)
        {
            txLabel = GetAtpLabelForTx();
            return ((AtpSsmDataSdi & 0x00FFFFFFu) << 8) | txLabel;
        }

        private async Task EnsureAtpModeAsync(CancellationToken token)
        {
            if (_arinc == null || !_arinc.IsConnected)
                return;

            if (!_atpTxOpened)
            {
                await _arinc.OpenTxAsync(ArincTxChannelIndex, token).ConfigureAwait(false);
                await _arinc.ConfigureTxAsync(
                    ArincTxChannelIndex,
                    rate: ArincRate,
                    mode: Art4229TxMode.Single,
                    parity: Art4229Parity.None,
                    wordFormat: Art4229WordFormat.Standard429,
                    cancellationToken: token).ConfigureAwait(false);
                _atpTxOpened = true;
            }

            if (_atpModeEntered)
                return;

            var word = BuildAtpEnterWord(out var txLabel);
            Log($"ATP发送准备: TX通道{ArincTxChannelIndex}, Label(oct174)=0x{AtpLabelOctal174Dec:X2}, Label反转后=0x{txLabel:X2}, Word=0x{word:X8}");
            await _arinc.SendWordsSingleAsync(ArincTxChannelIndex, new[] { word }, Art4229Parity.None, token).ConfigureAwait(false);
            Log($"ATP发送完成: TX通道{ArincTxChannelIndex}, Word=0x{word:X8}");
            _atpModeEntered = true;
        }

        private async Task EnsurePowerAsync(CancellationToken token)
        {
            // 192.168.1.15 CH1 不再由本测试控制上电，由总上电统一管理
            await Task.Delay(100, token).ConfigureAwait(false);
        }

        private async Task StopTestAsync()
        {
            try { _cts?.Cancel(); } catch { }

            IsManualTestRunning = false;
            IsAutoTestRunning = false;
            IsBusy = false;

            await CleanupArincAsync().ConfigureAwait(false);
            await CleanupPowerAsync().ConfigureAwait(false);

            Application.Current?.Dispatcher?.Invoke(() =>
            {
                if (!SkipMainPowerOff)
                {
                    IsPowerOn = false;
                    PowerStatus = "未供电";
                }
                ArincStatus = "429未连接";
                RaiseCanExecuteChangedForItems();
            });
        }

        private async Task CleanupArincAsync()
        {
            try
            {
                if (_arinc != null)
                {
                    try { if (_arincRxOpened) await _arinc.StopRxAsync(ArincRxChannelIndex, CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { if (_arincRxOpened) await _arinc.CloseRxAsync(ArincRxChannelIndex, CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { if (_atpTxOpened) await _arinc.CloseTxAsync(ArincTxChannelIndex, CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _arinc.DisconnectAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _arinc.DisposeAsync().ConfigureAwait(false); } catch { }
                }
            }
            finally
            {
                _arinc = null;
                _arincRxOpened = false;
                _atpTxOpened = false;
                _atpModeEntered = false;
            }
        }

        private async Task CleanupPowerAsync()
        {
            // 192.168.1.15 CH1 不再由本测试控制下电
            try
            {
                if (_power != null)
                {
                    try { await _power.DisconnectAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _power.DisposeAsync().ConfigureAwait(false); } catch { }
                }
            }
            finally
            {
                _power = null;
            }
        }

        private void ResetResults()
        {
            foreach (var item in Items)
                item.UpdateMeasurement(null, "---", "--", measured: false);
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

            OverallResult = Items.All(i => string.Equals(i.Result, "PASS", StringComparison.OrdinalIgnoreCase)) ? "PASS" : "FAIL";
        }

        private void RaiseCanExecuteChangedForItems()
        {
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                foreach (var item in Items)
                    item.CollectCommand?.RaiseCanExecuteChanged();
            });
        }

        private void Log(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            Application.Current?.Dispatcher?.Invoke(() =>
            {
                Logs.Add($"[{DateTime.Now:HH:mm:ss.fff}] {message}");
                while (Logs.Count > 500)
                    Logs.RemoveAt(0);
            });
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

                LastTestTime = Read("LastTestTime") ?? "--";
                OverallResult = Read("OverallResult") ?? "--";
            }
            catch
            {
            }
        }

        private void OnProjectSaving()
        {
            try
            {
                var root = _projectService?.CurrentProjectRoot;
                if (root?.TestInterfaceControls == null)
                    return;

                if (!root.TestInterfaceControls.TryGetValue(PersistDataKey, out var items) || items == null)
                {
                    items = new System.Collections.Generic.List<TestInterfaceControlItem>();
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

                Upsert("LastTestTime", LastTestTime);
                Upsert("OverallResult", OverallResult);
            }
            catch
            {
            }
        }

        public void Dispose()
        {
            try { _cts?.Cancel(); } catch { }
            _cts?.Dispose();
            _cts = null;

            try { CleanupArincAsync().GetAwaiter().GetResult(); } catch { }
            try { CleanupPowerAsync().GetAwaiter().GetResult(); } catch { }
            try { _measureLock.Dispose(); } catch { }

            if (_projectSavingToken != null)
                _eventAggregator?.GetEvent<ProjectSavingEvent>()?.Unsubscribe(_projectSavingToken);
        }

        public sealed class PowerItemViewModel : BindableBase
        {
            private readonly ControlBoardSecondaryTertiaryPowerTestViewModel _owner;
            private string _valueText = "---";
            private string _result = "--";
            private bool _isMeasured;

            internal PowerItemViewModel(
                ControlBoardSecondaryTertiaryPowerTestViewModel owner,
                string indexText,
                string name,
                string protocolLabelText,
                double nominal,
                double tolerance,
                double resolution,
                bool useSigned12Bit,
                bool useSignMagnitude12Bit = false)
            {
                _owner = owner;
                IndexText = indexText;
                Name = name;
                ProtocolLabelText = protocolLabelText;
                ProtocolLabelDec = Convert.ToByte(Convert.ToInt32(protocolLabelText, 8));
                ReceiveLabelDec = ControlBoardSecondaryTertiaryPowerTestViewModel.ReverseLabelBits(ProtocolLabelDec);
                Nominal = nominal;
                Tolerance = tolerance;
                Resolution = resolution;
                UseSigned12Bit = useSigned12Bit;
                UseSignMagnitude12Bit = useSignMagnitude12Bit;
                CriteriaText = $"{nominal:0.###}±{tolerance:0.###}";
                CollectCommand = new DelegateCommand(async () => await _owner.CollectAsync(this), () => _owner.CanCollectItem(this));
            }

            public string IndexText { get; }

            public string Name { get; }

            public string ProtocolLabelText { get; }

            public byte ProtocolLabelDec { get; }

            public byte ReceiveLabelDec { get; }

            public double Nominal { get; }

            public double Tolerance { get; }

            public double Resolution { get; }

            public bool UseSigned12Bit { get; }

            public bool UseSignMagnitude12Bit { get; }

            public string CriteriaText { get; }

            public string ValueText
            {
                get => _valueText;
                private set => SetProperty(ref _valueText, value);
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

            public DelegateCommand CollectCommand { get; }

            internal void UpdateMeasurement(double? valueVolt, string valueText, string result, bool measured)
            {
                ValueText = valueText;
                Result = result;
                IsMeasured = measured;
            }
        }
    }
}
