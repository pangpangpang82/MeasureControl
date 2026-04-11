using Prism.Commands;
using Prism.Events;
using Prism.Mvvm;
using Prism.Ioc;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using MeasureControl.Events;
using MeasureControl.Models.Devices;
using MeasureControl.Services;
using MeasureControl.Services.HardwareApis;

namespace MeasureControl.ViewModels.SingleBoardTest.InertController
{
    public sealed class DiscreteOutputModuleTestViewModel : BindableBase, IDisposable
    {
        private const string TestItemKey = "InertController_DiscreteOutputModule";

        private const string PowerSupplyIpAddress = "192.168.1.15";
        private const string PowerSupply2IpAddress = "192.168.1.16";
        private const double InputVoltageV = 28.0;
        private const double InputCurrentA = 1.0;

        public bool SkipMainPowerOff { get; set; }
        private const double InputVoltageCh2V = 24.0;
        private const double InputCurrentCh2A = 1.0;

        private const string DmmIpAddress = "192.168.1.13";
        private const string MatrixIpAddress = "192.168.1.3";
        private const int MatrixTcpBasePort = 50200;

        private const int MatrixSlotCommon = 4;
        private const int MatrixSlotSense = 6;

        private const int SenseRowIndex = 1;
        private const int SenseJ21ColumnIndex = 18;
        private const int SenseJ22ColumnIndex = 19;

        private const int ArincTxChannelIndex = 1;
        private const double DefaultArincRate = 100000.0;
        private const int ArincAfterTxOpenSettleDelayMs = 1000;
        private const byte ControlLabelRaw = 222;
        private const byte ControlSdi = 1;

        private const uint AtpSsmDataSdi = 0xC10001u;
        private const byte AtpLabelOctal174Dec = 124;

        private const int J13BitInData19 = 10;
        private const int J14BitInData19 = 11;
        private const int J11BitInData19 = 12;
        private const int J12BitInData19 = 13;
        private const int J17BitInData19 = 14;
        private const int J22BitInData19 = 15;
        private const int J21BitInData19 = 16;

        private const double HighVoltageLowerLimitV = 27.0;
        private const double OpenVoltageUpperLimitV = 2.0;
        private const int OutputCommandSettleDelayMs = 2000;
        private const double Jy7131DiThresholdV = 5.0;

        private readonly ISingleBoardTestContextService _singleBoardTestContext;
        private readonly ProjectService _projectService;
        private readonly IEventAggregator _eventAggregator;
        private readonly IDmmApi _dmm;
        private readonly IComponentPowerStateApi _componentPowerStateApi;

        private readonly SemaphoreSlim _opLock = new SemaphoreSlim(1, 1);
        private CancellationTokenSource _opCts;

        private IPowerSupplyApi _power;
        private IPowerSupplyApi _power2;
        private IJy7131Api _jy7131;
        private bool _jy7131DiThresholdApplied;

        private IArt4229Api _arinc;
        private bool _arincTxOpened;
        private bool _atpTxOpened;
        private bool _atpModeEntered;

        private bool _isManualTestRunning;
        private bool _isAutoTestRunning;
        private bool _isBusy;

        private string _overallResult = "--";
        private string _lastTestTime = "--";

        private SubscriptionToken _projectSavingToken;

        public DiscreteOutputModuleTestViewModel(
            ISingleBoardTestContextService singleBoardTestContext,
            ProjectService projectService,
            IEventAggregator eventAggregator,
            IDmmApi dmm,
            IComponentPowerStateApi componentPowerStateApi = null)
        {
            _singleBoardTestContext = singleBoardTestContext;
            _projectService = projectService;
            _eventAggregator = eventAggregator;
            _dmm = dmm;
            _componentPowerStateApi = componentPowerStateApi;

            ManualTestCommand = new DelegateCommand(async () => await OnManualTestAsync());
            AutoTestCommand = new DelegateCommand(async () => await OnAutoTestAsync());
            ClearLogCommand = new DelegateCommand(() => Logs.Clear());

            Items.Add(new DiscreteOutputItemViewModel(this, "1)", "J11", pinKind: DiscreteOutputPinKind.GndOc) { DiChannel = "DI0", BitInData19 = J11BitInData19 });
            Items.Add(new DiscreteOutputItemViewModel(this, "2)", "J12", pinKind: DiscreteOutputPinKind.GndOc) { DiChannel = "DI1", BitInData19 = J12BitInData19 });
            Items.Add(new DiscreteOutputItemViewModel(this, "3)", "J13", pinKind: DiscreteOutputPinKind.GndOc) { DiChannel = "DI2", BitInData19 = J13BitInData19 });
            Items.Add(new DiscreteOutputItemViewModel(this, "4)", "J14", pinKind: DiscreteOutputPinKind.GndOc) { DiChannel = "DI3", BitInData19 = J14BitInData19 });
            Items.Add(new DiscreteOutputItemViewModel(this, "5)", "J17", pinKind: DiscreteOutputPinKind.GndOc) { DiChannel = "DI4", BitInData19 = J17BitInData19 });
            Items.Add(new DiscreteOutputItemViewModel(this, "6)", "J21", pinKind: DiscreteOutputPinKind.V28Oc) { SenseColumnIndex = SenseJ21ColumnIndex, BitInData19 = J21BitInData19 });
            Items.Add(new DiscreteOutputItemViewModel(this, "7)", "J22", pinKind: DiscreteOutputPinKind.V28Oc) { SenseColumnIndex = SenseJ22ColumnIndex, BitInData19 = J22BitInData19 });

            LoadPersistedState();
            _projectSavingToken = _eventAggregator?.GetEvent<ProjectSavingEvent>()?.Subscribe(OnProjectSaving);
        }

        public ObservableCollection<DiscreteOutputItemViewModel> Items { get; } = new ObservableCollection<DiscreteOutputItemViewModel>();

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
                {
                    RaiseCanExecuteChangedForItems();
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
                    RaiseCanExecuteChangedForItems();
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
                    RaiseCanExecuteChangedForItems();
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
            var _hps = ContainerLocator.Container.Resolve<IHydraulicPowerService>();
            if (_hps == null || !_hps.IsHydraulicPowered)
            {
                MessageBox.Show("请先点击左上角组件上电按钮进行总上电，再进行测试。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (IsAutoTestRunning)
                await StopTestAsync().ConfigureAwait(false);

            _opCts?.Cancel();
            _opCts?.Dispose();
            _opCts = new CancellationTokenSource();

            ResetResults();

            IsManualTestRunning = true;
            IsBusy = true;
            try
            {
                await EnsurePowerAsync(InputVoltageV, _opCts.Token).ConfigureAwait(false);
                await EnsureJy7131ReadyAsync(_opCts.Token).ConfigureAwait(false);
                await EnsureArincTxReadyAsync(_opCts.Token).ConfigureAwait(false);

                await EnsureAtpModeAsync(_opCts.Token).ConfigureAwait(false);
                await EnsureArincTxReadyAsync(_opCts.Token).ConfigureAwait(false);
                await EnsureDmmReadyAsync(_opCts.Token).ConfigureAwait(false);
                await EnsureMatrixCommonReadyAsync(_opCts.Token).ConfigureAwait(false);

                Log($"[{DateTime.Now:HH:mm:ss}] 手动测试已就绪");
            }
            catch (Exception ex)
            {
                Log($"[{DateTime.Now:HH:mm:ss}] 手动测试初始化失败: {ex.Message}");
                await StopTestAsync().ConfigureAwait(false);
            }
            finally
            {
                IsBusy = false;
            }
        }

        public async Task<string> RunOnceAsync(CancellationToken cancellationToken)
        {
            if (IsAutoTestRunning || IsManualTestRunning)
            {
                return OverallResult;
            }

            _opCts?.Cancel();
            _opCts?.Dispose();
            _opCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            ResetResults();

            IsAutoTestRunning = true;
            IsBusy = true;

            try
            {
                await EnsurePowerAsync(InputVoltageV, _opCts.Token).ConfigureAwait(false);
                await EnsureJy7131ReadyAsync(_opCts.Token).ConfigureAwait(false);
                await EnsureArincTxReadyAsync(_opCts.Token).ConfigureAwait(false);

                await EnsureAtpModeAsync(_opCts.Token).ConfigureAwait(false);
                await EnsureArincTxReadyAsync(_opCts.Token).ConfigureAwait(false);
                await EnsureDmmReadyAsync(_opCts.Token).ConfigureAwait(false);
                await EnsureMatrixCommonReadyAsync(_opCts.Token).ConfigureAwait(false);

                foreach (var item in Items)
                {
                    await MeasureItemAsync(item, _opCts.Token).ConfigureAwait(false);
                }

                var pass = Items.All(x => string.Equals(x.HighResult, "PASS", StringComparison.OrdinalIgnoreCase) &&
                                          string.Equals(x.LowResult, "PASS", StringComparison.OrdinalIgnoreCase));

                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    OverallResult = pass ? "PASS" : "FAIL";
                    LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                });

                Log($"[{DateTime.Now:HH:mm:ss}] 自动测试完成: {(pass ? "PASS" : "FAIL")}");
                return OverallResult;
            }
            catch (OperationCanceledException)
            {
                Log($"[{DateTime.Now:HH:mm:ss}] 自动测试已取消");
                return "FAIL";
            }
            catch (Exception ex)
            {
                Log($"[{DateTime.Now:HH:mm:ss}] 自动测试异常: {ex.Message}");
                return "FAIL";
            }
            finally
            {
                IsBusy = false;
                IsAutoTestRunning = false;
                await SafeCleanupHardwareAsync().ConfigureAwait(false);
                SavePersistedState();
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
            var _hps = ContainerLocator.Container.Resolve<IHydraulicPowerService>();
            if (_hps == null || !_hps.IsHydraulicPowered)
            {
                MessageBox.Show("请先点击左上角组件上电按钮进行总上电，再进行测试。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (IsManualTestRunning)
                await StopTestAsync().ConfigureAwait(false);

            _opCts?.Cancel();
            _opCts?.Dispose();
            _opCts = new CancellationTokenSource();

            ResetResults();

            IsAutoTestRunning = true;
            IsBusy = true;

            try
            {
                await EnsurePowerAsync(InputVoltageV, _opCts.Token).ConfigureAwait(false);
                await EnsureJy7131ReadyAsync(_opCts.Token).ConfigureAwait(false);
                await EnsureArincTxReadyAsync(_opCts.Token).ConfigureAwait(false);

                await EnsureAtpModeAsync(_opCts.Token).ConfigureAwait(false);
                await EnsureArincTxReadyAsync(_opCts.Token).ConfigureAwait(false);
                await EnsureDmmReadyAsync(_opCts.Token).ConfigureAwait(false);
                await EnsureMatrixCommonReadyAsync(_opCts.Token).ConfigureAwait(false);

                foreach (var item in Items)
                {
                    await MeasureItemAsync(item, _opCts.Token).ConfigureAwait(false);
                }

                var pass = Items.All(x => string.Equals(x.HighResult, "PASS", StringComparison.OrdinalIgnoreCase) &&
                                          string.Equals(x.LowResult, "PASS", StringComparison.OrdinalIgnoreCase));

                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    OverallResult = pass ? "PASS" : "FAIL";
                    LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                });

                Log($"[{DateTime.Now:HH:mm:ss}] 自动测试完成: {(pass ? "PASS" : "FAIL")}");
            }
            catch (OperationCanceledException)
            {
                Log($"[{DateTime.Now:HH:mm:ss}] 自动测试已取消");
            }
            catch (Exception ex)
            {
                Log($"[{DateTime.Now:HH:mm:ss}] 自动测试异常: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
                IsAutoTestRunning = false;
                await SafeCleanupHardwareAsync().ConfigureAwait(false);
                SavePersistedState();
            }
        }

        internal async Task MeasureItemAsync(DiscreteOutputItemViewModel item, CancellationToken token)
        {
            if (item == null)
                return;

            await _opLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                IsBusy = true;

                await EnsurePowerAsync(InputVoltageV, token).ConfigureAwait(false);
                await EnsureArincTxReadyAsync(token).ConfigureAwait(false);

              
                
                
                await SendPinCommandAsync(item.BitInData19, high: true, token).ConfigureAwait(false);
                await ReEnsureAtpModeAsync(_opCts.Token).ConfigureAwait(false);
                await Task.Delay(OutputCommandSettleDelayMs, token).ConfigureAwait(false);
                var highMeasured = await MeasurePinAsync(item, token).ConfigureAwait(false);

                
                
                await SendPinCommandAsync(item.BitInData19, high: false, token).ConfigureAwait(false);
                await ReEnsureAtpModeAsync(_opCts.Token).ConfigureAwait(false);
                await Task.Delay(OutputCommandSettleDelayMs, token).ConfigureAwait(false);
                var lowMeasured = await MeasurePinAsync(item, token).ConfigureAwait(false);

                var highPass = IsExpectedHigh(item.PinKind, highMeasured);
                var lowPass = IsExpectedLow(item.PinKind, lowMeasured);

                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    item.UpdateMeasurement(highMeasured, highPass ? "PASS" : "FAIL", lowMeasured, lowPass ? "PASS" : "FAIL");
                    OverallResult = Items.All(x => string.Equals(x.HighResult, "PASS", StringComparison.OrdinalIgnoreCase) &&
                                                   string.Equals(x.LowResult, "PASS", StringComparison.OrdinalIgnoreCase)) ? "PASS" : "FAIL";
                    LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                });

                //Log($"[{DateTime.Now:HH:mm:ss}] {item.Pin}: 高={highMeasured}({(highPass ? "PASS" : "FAIL")}), 低={lowMeasured}({(lowPass ? "PASS" : "FAIL")})");
            }
            finally
            {
                IsBusy = false;
                _opLock.Release();
            }
        }

        private bool CanMeasureItem(DiscreteOutputItemViewModel item)
        {
            if (item == null)
                return false;
            if (IsBusy)
                return false;
            if (IsAutoTestRunning)
                return false;
            return IsManualTestRunning;
        }

        private async Task<string> MeasurePinAsync(DiscreteOutputItemViewModel item, CancellationToken token)
        {
            if (item.PinKind == DiscreteOutputPinKind.GndOc)
            {
                await EnsureJy7131ReadyAsync(token).ConfigureAwait(false);
                int trueCount = 0;
                for (int i = 0; i < 5; i++)
                {
                    var v = await _jy7131.ReadDiAsync(item.DiChannel, token).ConfigureAwait(false);
                    //var v = await _jy7131.ReadDiAsync("DI3", token).ConfigureAwait(false);

                    if (v) {  trueCount++;
                    Console.WriteLine("高电平");
                    }
                    else
                    {
                        Console.WriteLine("低电平");
                    }
                       
                    if (i < 4)
                        await Task.Delay(200, token).ConfigureAwait(false);
                }
                return trueCount >= 3 ? "GND" : "OPEN";
            }

            await EnsureDmmReadyAsync(token).ConfigureAwait(false);
            await EnsureMatrixCommonReadyAsync(token).ConfigureAwait(false);

            var other = item.SenseColumnIndex == SenseJ21ColumnIndex ? SenseJ22ColumnIndex : SenseJ21ColumnIndex;
            var matrix = MatrixControlService.Instance;

            try
            {
                _ = await matrix.DisconnectNodesAsync($"I{SenseRowIndex}", $"O{other}", MatrixSlotSense, MatrixIpAddress, MatrixTcpBasePort).ConfigureAwait(false);
            }
            catch
            {
            }

            var ok = await matrix.ConnectNodesAsync($"I{SenseRowIndex}", $"O{item.SenseColumnIndex}", MatrixSlotSense, MatrixIpAddress, MatrixTcpBasePort).ConfigureAwait(false);
            if (!ok)
                throw new InvalidOperationException($"矩阵连接失败：slot{MatrixSlotSense} I{SenseRowIndex}-O{item.SenseColumnIndex}");

            try
            {
                await Task.Delay(1000, token).ConfigureAwait(false);
                var reading = await _dmm.ReadOnceAsync(DmmMeasureMode.DCV, new DmmReadOptions { TimeoutMilliseconds = 8000 }, token).ConfigureAwait(false);
                if (reading?.Value == null)
                    return "--";

                var v = reading.Value.Value;
                if (v >= HighVoltageLowerLimitV)
                    return $"{v:0.###}V(28V)";
                if (v <= OpenVoltageUpperLimitV)
                    return $"{v:0.###}V(OPEN)";

                return $"{v:0.###}V";
            }
            finally
            {
                try
                {
                    _ = await matrix.DisconnectNodesAsync($"I{SenseRowIndex}", $"O{item.SenseColumnIndex}", MatrixSlotSense, MatrixIpAddress, MatrixTcpBasePort).ConfigureAwait(false);
                }
                catch
                {
                }
            }
        }

        private static bool IsExpectedHigh(DiscreteOutputPinKind kind, string measured)
        {
            if (kind == DiscreteOutputPinKind.GndOc)
                return string.Equals(measured, "GND", StringComparison.OrdinalIgnoreCase);

            return measured != null && measured.IndexOf("(28V)", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsExpectedLow(DiscreteOutputPinKind kind, string measured)
        {
            if (kind == DiscreteOutputPinKind.GndOc)
                return string.Equals(measured, "OPEN", StringComparison.OrdinalIgnoreCase);

            return measured != null && measured.IndexOf("(OPEN)", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private async Task SendPinCommandAsync(int bitInData19, bool high, CancellationToken token)
        {
            await EnsureArincTxReadyAsync(token).ConfigureAwait(false);

            if (bitInData19 < 0 || bitInData19 > 18)
                throw new ArgumentOutOfRangeException(nameof(bitInData19));

            uint data19 = high ? (1u << bitInData19) : 0u;

            var word = _arinc.BuildRawWord(ControlLabelRaw, ControlSdi, data19, ssm: 10, applyOddParity: true);
            Log($"[{DateTime.Now:HH:mm:ss}] 控制下发: bit={bitInData19}, {(high ? "HIGH" : "LOW")}, Word=0x{word:X8}");
            try
            {
                for (int i = 0; i < 1; i++)
                {
                    await _arinc.SendWordsSingleAsync(ArincTxChannelIndex, new[] { word }, Art4229Parity.Odd, token).ConfigureAwait(false);
                 
                }
            }
            catch (Exception ex)
            {
                Log($"[{DateTime.Now:HH:mm:ss}] 控制下发失败: bit={bitInData19}, {(high ? "HIGH" : "LOW")}, 异常={ex.Message}");
                throw;
            }
        }

        private async Task TryLogDiSnapshotAsync(string tag, CancellationToken token)
        {
            try
            {
                await EnsureJy7131ReadyAsync(token).ConfigureAwait(false);
                if (_jy7131 == null || !_jy7131.IsConnected)
                    return;
                var mask = await _jy7131.ReadDiBitmaskAsync(token).ConfigureAwait(false);
                Log($"[{DateTime.Now:HH:mm:ss}] DI快照{tag}: 0x{mask:X8}");
            }
            catch
            {
            }
        }

        private async Task EnsurePowerAsync(double voltageV, CancellationToken token)
        {
            // 192.168.1.15 CH1 不再由本测试控制上电，由总上电统一管理
            // 仍需连接电源以便控制CH2
            _power ??= new PowerSupplySocketApi();
            if (!_power.IsConnected)
                await _power.ConnectAsync(PowerSupplyIpAddress, token).ConfigureAwait(false);

            await _power.ApplyAsync(PowerSupplyChannel.CH2, InputVoltageCh2V, InputCurrentCh2A, token).ConfigureAwait(false);
            await _power.SetOutputEnabledAsync(PowerSupplyChannel.CH2, true, token).ConfigureAwait(false);

            _power2 ??= new PowerSupplySocketApi();
            if (!_power2.IsConnected)
                await _power2.ConnectAsync(PowerSupply2IpAddress, token).ConfigureAwait(false);

            await _power2.ApplyAsync(PowerSupplyChannel.CH1, voltageV, InputCurrentA, token).ConfigureAwait(false);
            await _power2.SetOutputEnabledAsync(PowerSupplyChannel.CH1, true, token).ConfigureAwait(false);
            await Task.Delay(300, token).ConfigureAwait(false);
        }

        private async Task CleanupPowerAsync()
        {
            try
            {
                using var cts = new CancellationTokenSource(1500);

                if (_power != null)
                {
                    if (!_power.IsConnected)
                        await _power.ConnectAsync(PowerSupplyIpAddress, cts.Token).ConfigureAwait(false);

                    // 192.168.1.15 CH1 不再由本测试控制下电
                    await _power.SetOutputEnabledAsync(PowerSupplyChannel.CH2, false, cts.Token).ConfigureAwait(false);
                    await Task.Delay(100, cts.Token).ConfigureAwait(false);
                    await _power.DisconnectAsync(cts.Token).ConfigureAwait(false);
                    await _power.DisposeAsync();
                }

                if (_power2 != null)
                {
                    if (!_power2.IsConnected)
                        await _power2.ConnectAsync(PowerSupply2IpAddress, cts.Token).ConfigureAwait(false);

                    await _power2.SetOutputEnabledAsync(PowerSupplyChannel.CH1, false, cts.Token).ConfigureAwait(false);
                    await Task.Delay(100, cts.Token).ConfigureAwait(false);
                    await _power2.DisconnectAsync(cts.Token).ConfigureAwait(false);
                    await _power2.DisposeAsync();
                }
            }
            catch
            {
            }
            finally
            {
                _power = null;
                _power2 = null;
            }
        }

        private async Task EnsureDmmReadyAsync(CancellationToken token)
        {
            if (_dmm == null)
                throw new InvalidOperationException("DMM接口为空");
            await _dmm.ConnectAsync(DmmIpAddress, token).ConfigureAwait(false);
        }

        private async Task EnsureMatrixCommonReadyAsync(CancellationToken token)
        {
            var matrix = MatrixControlService.Instance;
            var ok = await matrix.ConnectNodesAsync("I4", "O2", MatrixSlotCommon, MatrixIpAddress, MatrixTcpBasePort).ConfigureAwait(false);
            if (!ok)
                throw new InvalidOperationException("矩阵公共通路 I4-O2 连接失败");
        }

        private async Task EnsureArincTxReadyAsync(CancellationToken token)
        {
            if (_arinc != null && _arinc.IsConnected && _arincTxOpened)
                return;

            if (_arinc == null)
            {
                DeviceBase dev = null;
                try
                {
                    var pxi = ContainerLocator.Container.Resolve<IPxiChassisService>();
                    var chassisList = pxi?.GetAllChassis();
                    if (chassisList != null)
                    {
                        foreach (var chassis in chassisList)
                        {
                            if (chassis?.Devices == null) continue;
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
                catch
                {
                }

                if (dev != null)
                    _arinc = new Art4229Api(dev, deviceIndex: 0);
            }

            if (_arinc == null)
                throw new InvalidOperationException("未找到ART4229(ARINC429)板卡");

            if (!_arinc.IsConnected)
                await _arinc.ConnectAsync(token).ConfigureAwait(false);

            await _arinc.OpenTxAsync(ArincTxChannelIndex, token).ConfigureAwait(false);
            await _arinc.ConfigureTxAsync(
                ArincTxChannelIndex,
                DefaultArincRate,
                Art4229TxMode.Single,
                Art4229Parity.Odd,
                Art4229WordFormat.Standard429,
                token).ConfigureAwait(false);

            try { await Task.Delay(ArincAfterTxOpenSettleDelayMs, token).ConfigureAwait(false); } catch { }

            _arincTxOpened = true;
        }

        private static byte ReverseLabelBits(byte label)
        {
            byte reversed = 0;
            for (var i = 0; i < 8; i++)
            {
                if ((label & (1 << i)) != 0)
                    reversed |= (byte)(1 << (7 - i));
            }
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
                    rate: DefaultArincRate,
                    mode: Art4229TxMode.Single,
                    parity: Art4229Parity.None,
                    wordFormat: Art4229WordFormat.Standard429,
                    cancellationToken: token).ConfigureAwait(false);
                _atpTxOpened = true;
                try { await Task.Delay(ArincAfterTxOpenSettleDelayMs, token).ConfigureAwait(false); } catch { }
            }

            if (_atpModeEntered)
                return;

            var word = BuildAtpEnterWord(out var txLabel);
            Log($"[{DateTime.Now:HH:mm:ss}] 测试信息-ATP发送准备: TX通道{ArincTxChannelIndex}, SSM/Data/SDI=0x{AtpSsmDataSdi:X6}, Label(oct174)=0x{AtpLabelOctal174Dec:X2}, Label反转后=0x{txLabel:X2}, Word=0x{word:X8}");
            try
            {
                await _arinc.SendWordsSingleAsync(ArincTxChannelIndex, new[] { word }, Art4229Parity.None, token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log($"[{DateTime.Now:HH:mm:ss}] 测试信息-ATP发送失败: TX通道{ArincTxChannelIndex}, Word=0x{word:X8}, 异常={ex.Message}");
                throw;
            }
            Log($"[{DateTime.Now:HH:mm:ss}] 测试信息-ATP发送完成: TX通道{ArincTxChannelIndex}, Word=0x{word:X8}");
            _atpModeEntered = true;
        }

        private async Task ReEnsureAtpModeAsync(CancellationToken token)
        {
           
            if (!_atpTxOpened)
            {
                await _arinc.OpenTxAsync(ArincTxChannelIndex, token).ConfigureAwait(false);
                await _arinc.ConfigureTxAsync(
                    ArincTxChannelIndex,
                    rate: DefaultArincRate,
                    mode: Art4229TxMode.Single,
                    parity: Art4229Parity.None,
                    wordFormat: Art4229WordFormat.Standard429,
                    cancellationToken: token).ConfigureAwait(false);
                _atpTxOpened = true;
                try { await Task.Delay(ArincAfterTxOpenSettleDelayMs, token).ConfigureAwait(false); } catch { }
            }

           

            var word = BuildAtpEnterWord(out var txLabel);
            Log($"[{DateTime.Now:HH:mm:ss}] 测试信息-ATP发送准备: TX通道{ArincTxChannelIndex}, SSM/Data/SDI=0x{AtpSsmDataSdi:X6}, Label(oct174)=0x{AtpLabelOctal174Dec:X2}, Label反转后=0x{txLabel:X2}, Word=0x{word:X8}");
            try
            {
                await _arinc.SendWordsSingleAsync(ArincTxChannelIndex, new[] { word }, Art4229Parity.None, token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log($"[{DateTime.Now:HH:mm:ss}] 测试信息-ATP发送失败: TX通道{ArincTxChannelIndex}, Word=0x{word:X8}, 异常={ex.Message}");
                throw;
            }
            Log($"[{DateTime.Now:HH:mm:ss}] 测试信息-ATP发送完成: TX通道{ArincTxChannelIndex}, Word=0x{word:X8}");
            _atpModeEntered = true;
        }


        private async Task EnsureJy7131ReadyAsync(CancellationToken token)
        {
            if (_jy7131 != null && _jy7131.IsConnected && _jy7131.IsRunning && _jy7131DiThresholdApplied)
                return;

            if (_jy7131 == null)
            {
                var device = FindFirstJy7131Device();
                if (device != null)
                {
                    var slot = Infer7131SlotNumber(device);
                    if (int.TryParse(slot, out var slotNum))
                        _jy7131 = new Jy7131Api(device, slotNum);
                    else
                        _jy7131 = new Jy7131Api(device);
                }
            }

            if (_jy7131 == null)
                throw new InvalidOperationException("未找到7131板卡");

            if (!_jy7131.IsConnected)
                await _jy7131.ConnectAsync(token).ConfigureAwait(false);
            if (!_jy7131.IsRunning)
            {
                await _jy7131.SetOutputModeAsync(Jy7131OutputMode.Sinking, token).ConfigureAwait(false);
                await _jy7131.StartAsync(token).ConfigureAwait(false);
            }

            if (!_jy7131DiThresholdApplied)
            {
                await _jy7131.ApplyDiThresholdsAsync(new Jy7131DiThresholds
                {
                    Group1 = Jy7131DiThresholdV,
                    Group2 = Jy7131DiThresholdV,
                    Group3 = Jy7131DiThresholdV,
                    Group4 = Jy7131DiThresholdV,
                    Group5 = Jy7131DiThresholdV,
                    Group6 = Jy7131DiThresholdV,
                    Group7 = Jy7131DiThresholdV,
                    Group8 = Jy7131DiThresholdV,
                }, token).ConfigureAwait(false);
                _jy7131DiThresholdApplied = true;
            }
        }

        private DeviceBase FindFirstJy7131Device()
        {
            try
            {
                var pxi = ContainerLocator.Container.Resolve<IPxiChassisService>();
                var chassisList = pxi?.GetAllChassis();
                if (chassisList == null)
                    return null;

                foreach (var chassis in chassisList)
                {
                    if (chassis?.Devices == null)
                        continue;

                    var device = chassis.Devices.FirstOrDefault(d =>
                        d is MeasureControl.Models.Devices.DigitalIODevice ||
                        (d?.Model?.IndexOf("7131", StringComparison.OrdinalIgnoreCase) >= 0) ||
                        (d?.DeviceTypeName?.IndexOf("离散量", StringComparison.OrdinalIgnoreCase) >= 0) ||
                        (d?.DeviceTypeName?.IndexOf("数字量", StringComparison.OrdinalIgnoreCase) >= 0));

                    if (device != null)
                        return device;
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private static string Infer7131SlotNumber(DeviceBase device)
        {
            if (device is MeasureControl.Models.Devices.DeviceCategories.PxiDeviceBase pxi && pxi.SlotIndex > 0)
                return pxi.SlotIndex.ToString(CultureInfo.InvariantCulture);

            var slot = device?.SlotPosition;
            if (!string.IsNullOrWhiteSpace(slot))
            {
                var trimmed = slot.Replace("Slot", "").Replace("slot", "").Trim();
                if (int.TryParse(trimmed, out var slotNum) && slotNum > 0)
                    return slotNum.ToString(CultureInfo.InvariantCulture);
            }

            return "12";
        }

        private void ResetResults()
        {
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                foreach (var item in Items)
                {
                    item.UpdateMeasurement("--", "--", "--", "--");
                }

                OverallResult = "--";
                LastTestTime = "--";
            });
        }

        private async Task StopTestAsync()
        {
            try
            {
                _opCts?.Cancel();
            }
            catch
            {
            }

            IsManualTestRunning = false;
            IsAutoTestRunning = false;
            IsBusy = false;

            await SafeCleanupHardwareAsync().ConfigureAwait(false);
            SavePersistedState();
        }

        private async Task SafeCleanupHardwareAsync()
        {
            try
            {
                using var cts = new CancellationTokenSource(1500);

                try
                {
                    if (_jy7131 != null)
                    {
                        if (_jy7131.IsConnected && _jy7131.IsRunning)
                            await _jy7131.StopAsync(cts.Token).ConfigureAwait(false);
                        if (_jy7131.IsConnected)
                            await _jy7131.DisconnectAsync(cts.Token).ConfigureAwait(false);
                        await _jy7131.DisposeAsync();
                    }
                }
                catch
                {
                }
                finally
                {
                    _jy7131 = null;
                    _jy7131DiThresholdApplied = false;
                }

                try
                {
                    if (_arinc != null)
                    {
                        if (_arinc.IsConnected && _arincTxOpened)
                        {
                            try { await _arinc.CloseTxAsync(ArincTxChannelIndex, cts.Token).ConfigureAwait(false); } catch { }
                        }

                        if (_arinc.IsConnected)
                            await _arinc.DisconnectAsync(cts.Token).ConfigureAwait(false);
                        await _arinc.DisposeAsync();
                    }
                }
                catch
                {
                }
                finally
                {
                    _arinc = null;
                    _arincTxOpened = false;
                    _atpTxOpened = false;
                    _atpModeEntered = false;
                }

                await CleanupPowerAsync().ConfigureAwait(false);
            }
            catch
            {
            }
        }

        private void RaiseCanExecuteChangedForItems()
        {
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                foreach (var item in Items)
                {
                    item.MeasureCommand?.RaiseCanExecuteChanged();
                }
            });
        }

        private void Log(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            Application.Current?.Dispatcher?.Invoke(() =>
            {
                Logs.Add(message);
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

                RaisePropertyChanged(nameof(LastTestTime));
                RaisePropertyChanged(nameof(OverallResult));
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
                if (root == null)
                    return;

                if (root.TestInterfaceControls == null)
                    root.TestInterfaceControls = new Dictionary<string, List<MeasureControl.Models.TestInterfaceControlItem>>(StringComparer.OrdinalIgnoreCase);

                var list = new List<MeasureControl.Models.TestInterfaceControlItem>
                {
                    new MeasureControl.Models.TestInterfaceControlItem { BoundVariableName = "LastTestTime", BoundVariablePath = LastTestTime ?? "--" },
                    new MeasureControl.Models.TestInterfaceControlItem { BoundVariableName = "OverallResult", BoundVariablePath = OverallResult ?? "--" },
                };

                root.TestInterfaceControls[PersistDataKey] = list;
            }
            catch
            {
            }
        }

        private void OnProjectSaving()
        {
            SavePersistedState();
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
                _opCts?.Cancel();
            }
            catch
            {
            }

            try
            {
                SafeCleanupHardwareAsync().GetAwaiter().GetResult();
            }
            catch
            {
            }
        }

        public enum DiscreteOutputPinKind
        {
            GndOc,
            V28Oc
        }

        public sealed class DiscreteOutputItemViewModel : BindableBase
        {
            private readonly DiscreteOutputModuleTestViewModel _owner;

            private string _highMeasuredText = "--";
            private string _highResult = "--";
            private string _lowMeasuredText = "--";
            private string _lowResult = "--";

            public DiscreteOutputItemViewModel(DiscreteOutputModuleTestViewModel owner, string indexText, string pin, DiscreteOutputPinKind pinKind)
            {
                _owner = owner;
                IndexText = indexText;
                Pin = pin;
                PinKind = pinKind;

                MeasureCommand = new DelegateCommand(async () =>
                {
                    if (_owner?._opCts == null)
                        return;
                    await _owner.MeasureItemAsync(this, _owner._opCts.Token).ConfigureAwait(false);
                }, () => _owner.CanMeasureItem(this));
            }

            public string IndexText { get; }
            public string Pin { get; }
            public DiscreteOutputPinKind PinKind { get; }

            public string DiChannel { get; set; }
            public int SenseColumnIndex { get; set; }
            public int BitInData19 { get; set; }

            public string HighMeasuredText
            {
                get => _highMeasuredText;
                private set => SetProperty(ref _highMeasuredText, value);
            }

            public string HighResult
            {
                get => _highResult;
                private set => SetProperty(ref _highResult, value);
            }

            public string LowMeasuredText
            {
                get => _lowMeasuredText;
                private set => SetProperty(ref _lowMeasuredText, value);
            }

            public string LowResult
            {
                get => _lowResult;
                private set => SetProperty(ref _lowResult, value);
            }

            public DelegateCommand MeasureCommand { get; }

            internal void UpdateMeasurement(string highMeasured, string highResult, string lowMeasured, string lowResult)
            {
                HighMeasuredText = highMeasured ?? "--";
                HighResult = highResult ?? "--";
                LowMeasuredText = lowMeasured ?? "--";
                LowResult = lowResult ?? "--";
            }
        }
    }
}
