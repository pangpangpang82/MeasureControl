using Prism.Commands;
using Prism.Events;
using Prism.Mvvm;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using MeasureControl.Events;
using MeasureControl.Models;
using MeasureControl.Models.Devices;
using MeasureControl.Services;
using MeasureControl.Services.HardwareApis;
using MeasureControl.Simulations.FuelController;
using Prism.Ioc;

namespace MeasureControl.ViewModels.SingleBoardTest.InertController
{
    public sealed class PowerImpedanceTestViewModel : BindableBase, IDisposable
    {
        private const string TestItemKey = "InertController_PowerImpedance";
        private const double ImpedanceThresholdOhm = 500.0;

        private const string RelayControlChannel = "DO0";
        private const int RelayTimeoutMs = 5000;

        private const string RelayPowerSupplyIpAddress = "192.168.1.16";
        private const PowerSupplyChannel RelayPowerChannel = PowerSupplyChannel.CH1;
        private const double RelayPowerVoltage = 24.0;
        private const double RelayPowerCurrentLimit = 1.0;

        private const string DmmIpAddress = "192.168.1.13";
        private const string MatrixIpAddress = "192.168.1.3";
        private const int MatrixTcpBasePort = 50200;
        private const int FirstMeasurementStartupDelayMs = 1000;
        private const int DefaultMeasurementStabilizationDelayMs = 3000;
        private const int Test2MeasurementStabilizationDelayMs = 10000;

        private const string PowerSupplyIpAddress = "192.168.1.16";
        private const double InputVoltageV = 24.0;
        private const double InputCurrentA = 1.0;

        public bool SkipMainPowerOff { get; set; }

        private const int MatrixSlotSequence = 6;
        private const int MatrixSlotCommon = 4;

        private readonly ISingleBoardTestContextService _singleBoardTestContext;
        private readonly ProjectService _projectService;
        private readonly IEventAggregator _eventAggregator;
        private readonly IDmmApi _dmm;

        private readonly IComponentPowerStateApi _componentPowerStateApi;
        private readonly IPxiChassisService _pxiChassisService;
        private IJy7131Api _jy7131Api;

        private IPowerSupplyApi _relayPowerSupply;
        private IPowerSupplyApi _power;

        private readonly PowerImpedanceSimulation _simulation = new PowerImpedanceSimulation();

        private readonly SemaphoreSlim _measureLock = new SemaphoreSlim(1, 1);
        private CancellationTokenSource _cts;

        private bool _isManualTestRunning;
        private bool _isAutoTestRunning;
        private bool _isBusy;

        private bool _isRelayActivated;
        private string _relayStatus = "未激活";
        private bool _isPowerOn = true;
        private string _powerStatus = "未就绪";

        private string _overallResult = "--";
        private string _lastTestTime = "--";

        private SubscriptionToken _projectSavingToken;

        public PowerImpedanceTestViewModel(
            ISingleBoardTestContextService singleBoardTestContext,
            ProjectService projectService,
            IEventAggregator eventAggregator,
            IDmmApi dmm)
            : this(singleBoardTestContext, projectService, eventAggregator, dmm, null, null)
        {
        }

        public PowerImpedanceTestViewModel(
            ISingleBoardTestContextService singleBoardTestContext,
            ProjectService projectService,
            IEventAggregator eventAggregator,
            IDmmApi dmm,
            IComponentPowerStateApi componentPowerStateApi,
            IPxiChassisService pxiChassisService)
        {
            _singleBoardTestContext = singleBoardTestContext;
            _projectService = projectService;
            _eventAggregator = eventAggregator;
            _dmm = dmm;
            _componentPowerStateApi = componentPowerStateApi;
            _pxiChassisService = pxiChassisService;

            ManualTestCommand = new DelegateCommand(async () => await OnManualTestAsync());
            AutoTestCommand = new DelegateCommand(async () => await OnAutoTestAsync());
            ToggleRelayCommand = new DelegateCommand(async () => await ToggleRelayAsync(), () => !IsBusy && IsManualTestRunning);
            ClearLogCommand = new DelegateCommand(() => Logs.Clear());

            Items.Add(new ImpedanceItemViewModel(this, "1)", "阻抗测试1") { ColumnIndex = 8 });
            Items.Add(new ImpedanceItemViewModel(this, "2)", "阻抗测试2") { ColumnIndex = 9 });
            Items.Add(new ImpedanceItemViewModel(this, "3)", "阻抗测试3") { ColumnIndex = 10 });
            Items.Add(new ImpedanceItemViewModel(this, "4)", "阻抗测试4") { ColumnIndex = 11 });
            Items.Add(new ImpedanceItemViewModel(this, "5)", "阻抗测试5") { ColumnIndex = 12 });
            Items.Add(new ImpedanceItemViewModel(this, "6)", "阻抗测试6") { ColumnIndex = 13 });
            Items.Add(new ImpedanceItemViewModel(this, "7)", "阻抗测试7") { ColumnIndex = 14 });

            LoadPersistedState();
            _projectSavingToken = _eventAggregator?.GetEvent<ProjectSavingEvent>()?.Subscribe(OnProjectSaving);
        }

        public ObservableCollection<ImpedanceItemViewModel> Items { get; } = new ObservableCollection<ImpedanceItemViewModel>();

        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();

        public DelegateCommand ManualTestCommand { get; }
        public DelegateCommand AutoTestCommand { get; }
        public DelegateCommand ToggleRelayCommand { get; }
        public DelegateCommand ClearLogCommand { get; }

        public bool IsRelayActivated
        {
            get => _isRelayActivated;
            private set
            {
                if (SetProperty(ref _isRelayActivated, value))
                    RaiseCanExecuteChangedForItems();
            }
        }

        private async Task EnsureRelay24VPowerAsync(CancellationToken token)
        {
            _relayPowerSupply ??= new PowerSupplySocketApi();
            if (!_relayPowerSupply.IsConnected)
                await _relayPowerSupply.ConnectAsync(RelayPowerSupplyIpAddress, token).ConfigureAwait(false);

            await _relayPowerSupply.ApplyAsync(RelayPowerChannel, RelayPowerVoltage, RelayPowerCurrentLimit, token).ConfigureAwait(false);
            await _relayPowerSupply.SetOutputEnabledAsync(RelayPowerChannel, true, token).ConfigureAwait(false);
            await Task.Delay(200, token).ConfigureAwait(false);
        }

        private async Task DisableRelay24VPowerAsync(CancellationToken token)
        {
            try
            {
                if (_relayPowerSupply == null)
                    return;

                if (!_relayPowerSupply.IsConnected)
                    await _relayPowerSupply.ConnectAsync(RelayPowerSupplyIpAddress, token).ConfigureAwait(false);

                await _relayPowerSupply.SetOutputEnabledAsync(RelayPowerChannel, false, token).ConfigureAwait(false);
                await Task.Delay(200, token).ConfigureAwait(false);
                Log("继电器24V已下电");
            }
            catch (Exception ex)
            {
                Log($"继电器24V下电异常: {ex.Message}");
            }
            finally
            {
                // 断开并释放连接，确保下次测试能建立新连接
                try
                {
                    if (_relayPowerSupply != null)
                    {
                        try { await _relayPowerSupply.DisconnectAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                        try { await _relayPowerSupply.DisposeAsync().ConfigureAwait(false); } catch { }
                    }
                }
                catch { }
                _relayPowerSupply = null;
            }
        }

        public string RelayStatus
        {
            get => _relayStatus;
            private set => SetProperty(ref _relayStatus, value);
        }

        public bool IsPowerOn
        {
            get => _isPowerOn;
            private set => SetProperty(ref _isPowerOn, value);
        }

        public string PowerStatus
        {
            get => _powerStatus;
            private set => SetProperty(ref _powerStatus, value);
        }

        public bool IsManualTestRunning
        {
            get => _isManualTestRunning;
            private set
            {
                if (SetProperty(ref _isManualTestRunning, value))
                {
                    ToggleRelayCommand?.RaiseCanExecuteChanged();
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
                    ToggleRelayCommand?.RaiseCanExecuteChanged();
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

            if (IsAutoTestRunning)
            {
                await StopTestAsync().ConfigureAwait(false);
            }

            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();

            ResetResults();

            IsManualTestRunning = true;
            IsAutoTestRunning = false;
            OverallResult = "--";
            LastTestTime = "--";

            Log("开始手动测试");

            try
            {
                await EnsurePowerAsync(_cts.Token).ConfigureAwait(false);

                await Ensure7131ReadyAsync(_cts.Token).ConfigureAwait(false);

                await ActivateRelayWithTimeoutAsync(_cts.Token).ConfigureAwait(false);

                await _dmm.ConnectAsync(DmmIpAddress, _cts.Token).ConfigureAwait(false);
                Log($"万用表连接成功: {DmmIpAddress}");

                var matrix = MatrixControlService.Instance;
                var okCommon = await matrix.ConnectNodesAsync("I4", "O2", MatrixSlotCommon, MatrixIpAddress, MatrixTcpBasePort).ConfigureAwait(false);
                Log($"矩阵公共通路 I4-O2(slot{MatrixSlotCommon}) {(okCommon ? "OK" : "FAIL")}");
                if (!okCommon)
                {
                    await StopTestAsync().ConfigureAwait(false);
                }
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

        private async Task OnAutoTestAsync()
        {
            if (IsAutoTestRunning)
            {
                await StopTestAsync().ConfigureAwait(false);
                return;
            }

            if (IsManualTestRunning)
            {
                await StopTestAsync().ConfigureAwait(false);
            }

            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();

            ResetResults();

            IsAutoTestRunning = true;
            IsManualTestRunning = false;
            OverallResult = "--";
            LastTestTime = "--";

            Log("开始自动测试");

            try
            {
                await EnsurePowerAsync(_cts.Token).ConfigureAwait(false);

                await Ensure7131ReadyAsync(_cts.Token).ConfigureAwait(false);

                await _dmm.ConnectAsync(DmmIpAddress, _cts.Token).ConfigureAwait(false);
                Log($"万用表连接成功: {DmmIpAddress}");

                var matrix = MatrixControlService.Instance;
                var okCommon = await matrix.ConnectNodesAsync("I4", "O2", MatrixSlotCommon, MatrixIpAddress, MatrixTcpBasePort).ConfigureAwait(false);
                Log($"矩阵公共通路 I4-O2(slot{MatrixSlotCommon}) {(okCommon ? "OK" : "FAIL")}");
                if (!okCommon)
                {
                    await StopTestAsync().ConfigureAwait(false);
                    return;
                }

                await ActivateRelayWithTimeoutAsync(_cts.Token).ConfigureAwait(false);

                foreach (var item in Items)
                {
                    if (_cts.IsCancellationRequested)
                    {
                        return;
                    }

                    await MeasureAsync(item, _cts.Token).ConfigureAwait(false);
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

        public async Task<string> RunOnceAsync(CancellationToken cancellationToken)
        {
            if (IsAutoTestRunning || IsManualTestRunning)
            {
                await StopTestAsync().ConfigureAwait(false);
                await Task.Delay(100, CancellationToken.None).ConfigureAwait(false);
            }

            _cts?.Cancel();
            _cts?.Dispose();
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            ResetResults();

            IsAutoTestRunning = true;
            IsManualTestRunning = false;
            OverallResult = "--";
            LastTestTime = "--";

            try
            {
                Log("开始自动测试");

                await EnsurePowerAsync(_cts.Token).ConfigureAwait(false);

                await Ensure7131ReadyAsync(_cts.Token).ConfigureAwait(false);

                await _dmm.ConnectAsync(DmmIpAddress, _cts.Token).ConfigureAwait(false);
                Log($"万用表连接成功: {DmmIpAddress}");

                var matrix = MatrixControlService.Instance;
                var okCommon = await matrix.ConnectNodesAsync("I4", "O2", MatrixSlotCommon, MatrixIpAddress, MatrixTcpBasePort).ConfigureAwait(false);
                Log($"矩阵公共通路 I4-O2(slot{MatrixSlotCommon}) {(okCommon ? "OK" : "FAIL")}");
                if (!okCommon)
                {
                    await StopTestAsync().ConfigureAwait(false);
                    return "FAIL";
                }

                await ActivateRelayWithTimeoutAsync(_cts.Token).ConfigureAwait(false);

                foreach (var item in Items)
                {
                    _cts.Token.ThrowIfCancellationRequested();
                    await MeasureAsync(item, _cts.Token).ConfigureAwait(false);
                    await Task.Delay(100, _cts.Token).ConfigureAwait(false);
                }

                EvaluateOverall();
                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                Log($"自动测试完成，总体结果: {OverallResult}");

                var finalResult = OverallResult;
                await StopTestAsync().ConfigureAwait(false);
                return string.IsNullOrWhiteSpace(finalResult) || string.Equals(finalResult, "--", StringComparison.OrdinalIgnoreCase)
                    ? "FAIL"
                    : finalResult;
            }
            catch (OperationCanceledException)
            {
                await StopTestAsync().ConfigureAwait(false);
                throw;
            }
            catch (Exception ex)
            {
                Log($"自动测试失败: {ex.Message}");
                await StopTestAsync().ConfigureAwait(false);
                return "FAIL";
            }
            finally
            {
                IsAutoTestRunning = false;
                _cts?.Dispose();
                _cts = null;
            }
        }

        internal bool CanMeasureItem(ImpedanceItemViewModel item)
        {
            if (item == null) return false;
            return IsManualTestRunning && !IsBusy && IsRelayActivated;
        }

        internal async Task MeasureAsync(ImpedanceItemViewModel item)
        {
            if (item == null) return;
            var token = _cts?.Token ?? CancellationToken.None;
            await MeasureAsync(item, token).ConfigureAwait(false);

            EvaluateOverall();
            if (Items.All(i => i.IsMeasured))
            {
                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            }
        }

        private async Task MeasureAsync(ImpedanceItemViewModel item, CancellationToken token)
        {
            if (item == null) return;

            var firstItem = Items.FirstOrDefault();
            var isFirstMeasurement = ReferenceEquals(item, firstItem) && !Items.Any(i => i.IsMeasured);
            if (isFirstMeasurement)
            {
                Log($"首项测试前等待 {FirstMeasurementStartupDelayMs}ms，确保7131等板卡启动稳定");
                await Task.Delay(FirstMeasurementStartupDelayMs, token).ConfigureAwait(false);
            }

            await _measureLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                IsBusy = true;

                Log($"开始测量: {item.Name}");
                await MeasureByMatrixAsync(item, token).ConfigureAwait(false);
            }
            finally
            {
                try
                {
                    var matrix = MatrixControlService.Instance;
                    var output = $"O{item.ColumnIndex}";
                    _ = await matrix.DisconnectNodesAsync("I1", output, MatrixSlotSequence, MatrixIpAddress, MatrixTcpBasePort).ConfigureAwait(false);
                }
                catch
                {
                }

                IsBusy = false;
                _measureLock.Release();
            }
        }

        // 当前工程没有惰化模拟板的矩阵映射表，这里先沿用“空气单板”的测量模式：
        // 通过矩阵把万用表接到一个输出列后读取 RES。
        // ColumnIndex 只是为了区分不同测试项占用不同列，后续接线表明确后可替换。

        private async Task MeasureByMatrixAsync(ImpedanceItemViewModel item, CancellationToken token)
        {
            var matrix = MatrixControlService.Instance;
            var output = $"O{item.ColumnIndex}";

            var ok = await matrix.ConnectNodesAsync("I1", output, MatrixSlotSequence, MatrixIpAddress, MatrixTcpBasePort).ConfigureAwait(false);
            Log($"矩阵连接 I1-{output}(slot{MatrixSlotSequence}) {(ok ? "OK" : "FAIL")}");
            if (!ok)
            {
                item.UpdateMeasurement(null, "--", "FAIL", measured: true);
                return;
            }

            var stabilizationDelayMs = item?.ColumnIndex == 9
                ? Test2MeasurementStabilizationDelayMs
                : DefaultMeasurementStabilizationDelayMs;
            Log($"等待{stabilizationDelayMs / 1000.0:0.#}秒，信号稳定后采集...");
            await Task.Delay(stabilizationDelayMs, token).ConfigureAwait(false);

            const int maxAttempts = 3;
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                var reading = await SafeReadResistanceAsync(token).ConfigureAwait(false);
                var pass = IsReadingPass(reading);

                if (pass || attempt == maxAttempts)
                {
                    if (attempt > 1)
                        Log($"第{attempt}次采集{(pass ? "通过" : "仍失败")}");
                    ApplyReading(item, reading);
                    return;
                }

                Log($"第{attempt}次采集未通过，等待1秒后重试...");
                await Task.Delay(1000, token).ConfigureAwait(false);
            }
        }

        private bool IsReadingPass(DmmReading reading)
        {
            if (reading == null) return false;
            if (reading.IsOverrange) return true;
            if (reading.Value == null) return false;
            return reading.Value.Value > ImpedanceThresholdOhm;
        }

        private async Task<DmmReading> SafeReadResistanceAsync(CancellationToken token)
        {
            try
            {
                return await _dmm.ReadOnceAsync(DmmMeasureMode.RES, new DmmReadOptions { TimeoutMilliseconds = 8000 }, token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log($"万用表读数异常: {ex.Message}");
                return null;
            }
        }

        private void ApplyReading(ImpedanceItemViewModel item, DmmReading reading)
        {
            if (reading == null)
            {
                item.UpdateMeasurement(null, "--", "FAIL", measured: true);
                return;
            }

            if (reading.IsOverrange)
            {
                item.UpdateMeasurement(null, "OL", "PASS", measured: true);
                Log("读数为OL(过量程)，判为PASS");
                return;
            }

            if (reading.Value == null)
            {
                item.UpdateMeasurement(null, "--", "FAIL", measured: true);
                return;
            }

            var ohm = reading.Value.Value;
            var text = ohm.ToString("0.###", CultureInfo.InvariantCulture);
            var pass = ohm > ImpedanceThresholdOhm;

            item.UpdateMeasurement(ohm, text, pass ? "PASS" : "FAIL", measured: true);
            Log($"读数: {ohm:0.###} Ω => {(pass ? "PASS" : "FAIL")}");
        }

        private void ResetResults()
        {
            foreach (var item in Items)
            {
                item.UpdateMeasurement(null, "--", "--", measured: false);
            }
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

        private async Task StopTestAsync()
        {
            try { _cts?.Cancel(); } catch { }

            IsManualTestRunning = false;
            IsAutoTestRunning = false;
            IsBusy = false;

            try
            {
                if (IsRelayActivated)
                    await DeactivateRelayWithTimeoutAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
            }

            try
            {
                var matrix = MatrixControlService.Instance;
                _ = await matrix.DisconnectNodesAsync("I4", "O2", MatrixSlotCommon, MatrixIpAddress, MatrixTcpBasePort).ConfigureAwait(false);
            }
            catch
            {
            }

            try
            {
                if (_jy7131Api != null && _jy7131Api.IsConnected)
                {
                    if (_jy7131Api.IsRunning)
                        await _jy7131Api.StopAsync(CancellationToken.None).ConfigureAwait(false);
                    await _jy7131Api.DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch
            {
            }

            try
            {
                await _dmm.DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
            }

            try
            {
                if (_power != null)
                {
                    // 阻抗测试完成后始终关闭192.168.1.16 CH1（不受SkipMainPowerOff影响）
                    await _power.SetOutputEnabledAsync(PowerSupplyChannel.CH1, false, CancellationToken.None).ConfigureAwait(false);
                    await _power.DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        IsPowerOn = false;
                        PowerStatus = "未供电";
                    });
                }
            }
            catch
            {
            }

            await DisableRelay24VPowerAsync(CancellationToken.None).ConfigureAwait(false);

            RaiseCanExecuteChangedForItems();
        }

        private async Task ToggleRelayAsync()
        {
            IsBusy = true;
            try
            {
                if (IsRelayActivated)
                    await DeactivateRelayWithTimeoutAsync(CancellationToken.None).ConfigureAwait(false);
                else
                    await ActivateRelayWithTimeoutAsync(CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task EnsureComponentDownAsync(CancellationToken token)
        {
            try
            {
                if (_componentPowerStateApi != null)
                {
                    Log("正在设置组件供电状态: 下电...");
                    await _componentPowerStateApi.ApplyComponentDownStateAsync(token).ConfigureAwait(false);
                    Log("组件供电状态已设置为下电");

                    try
                    {
                        Log("正在确保继电器24V常供电...");
                        await EnsureRelay24VPowerAsync(token).ConfigureAwait(false);
                        Log("继电器24V供电已保持开启");
                    }
                    catch (Exception ex)
                    {
                        Log($"继电器24V供电保持异常: {ex.Message}");
                    }

                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        IsPowerOn = false;
                        PowerStatus = "已下电";
                    });
                    return;
                }

                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    IsPowerOn = true;
                    PowerStatus = "请确认已下电";
                });
            }
            catch (Exception ex)
            {
                Log($"组件下电状态设置异常: {ex.Message}");

                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    IsPowerOn = true;
                    PowerStatus = "下电失败";
                });
            }
        }

        private async Task EnsurePowerAsync(CancellationToken token)
        {
            // 测阻抗前先关闭192.168.1.15 CH1，防止上电测阻抗
            try
            {
                Log("正在关闭192.168.1.15 CH1...");
                await using var tempPower = new PowerSupplySocketApi();
                await tempPower.ConnectAsync("192.168.1.15", token).ConfigureAwait(false);
                await tempPower.SetOutputEnabledAsync(PowerSupplyChannel.CH1, false, token).ConfigureAwait(false);
                await tempPower.DisconnectAsync(token).ConfigureAwait(false);
                Log("192.168.1.15 CH1 已关闭");
                // 同步更新左上角上电状态
                try { Prism.Ioc.ContainerLocator.Container.Resolve<IHydraulicPowerService>()?.SetPoweredState(false); } catch { }
            }
            catch (Exception ex)
            {
                Log($"关闭192.168.1.15 CH1异常: {ex.Message}");
            }

            _power ??= new PowerSupplySocketApi();
            await _power.ConnectAsync(PowerSupplyIpAddress, token).ConfigureAwait(false);
            await _power.ApplyAsync(PowerSupplyChannel.CH1, InputVoltageV, InputCurrentA, token).ConfigureAwait(false);
            await _power.SetOutputEnabledAsync(PowerSupplyChannel.CH1, true, token).ConfigureAwait(false);
            await Task.Delay(300, token).ConfigureAwait(false);
            
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                IsPowerOn = true;
                PowerStatus = "已供电";
            });
            
            Log($"程控电源已打开: {InputVoltageV}V {InputCurrentA}A");
        }

        private async Task Ensure7131ReadyAsync(CancellationToken token)
        {
            if (_jy7131Api == null)
            {
                var device = FindFirstJy7131Device();
                if (device != null)
                {
                    var slot = Infer7131SlotNumber(device);
                    if (int.TryParse(slot, out var slotNum))
                        _jy7131Api = new Jy7131Api(device, slotNum);
                    else
                        _jy7131Api = new Jy7131Api(device);
                }
            }

            if (_jy7131Api == null)
            {
                Log("未找到7131板卡");
                return;
            }

            if (!_jy7131Api.IsConnected)
            {
                await _jy7131Api.ConnectAsync(token).ConfigureAwait(false);
                await _jy7131Api.SetOutputModeAsync(Jy7131OutputMode.Sinking, token).ConfigureAwait(false);
                await _jy7131Api.StartAsync(token).ConfigureAwait(false);
                Log("7131板卡已启动");
            }
            else if (!_jy7131Api.IsRunning)
            {
                await _jy7131Api.SetOutputModeAsync(Jy7131OutputMode.Sinking, token).ConfigureAwait(false);
                await _jy7131Api.StartAsync(token).ConfigureAwait(false);
                Log("7131板卡已启动");
            }
        }

        private async Task ActivateRelayWithTimeoutAsync(CancellationToken token)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeoutCts.CancelAfter(RelayTimeoutMs);

            await Ensure7131ReadyAsync(timeoutCts.Token).ConfigureAwait(false);

            Log($"正在激活继电器（{RelayControlChannel}）...");
            if (_jy7131Api != null && _jy7131Api.IsConnected)
            {
                await _jy7131Api.WriteDoAsync(RelayControlChannel, true, timeoutCts.Token).ConfigureAwait(false);
                try
                {
                    var mask = await _jy7131Api.ReadDoBitmaskAsync(timeoutCts.Token).ConfigureAwait(false);
                    var ok = int.TryParse(RelayControlChannel.Substring(2), out var doIdx);
                    var bit = ok ? doIdx : 1;
                    Log($"DO写回读取: mask=0x{mask:X8}，{RelayControlChannel}={(mask & (1u << bit)) != 0}");
                }
                catch (Exception ex)
                {
                    Log($"DO写回读取失败: {ex.Message}");
                }
            }
            else
            {
                Log("7131板卡不可用，使用仿真继电器动作");
                await _simulation.SimulateRelayActivateAsync(timeoutCts.Token).ConfigureAwait(false);
            }

            await Task.Delay(200, timeoutCts.Token).ConfigureAwait(false);
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                IsRelayActivated = true;
                RelayStatus = "已激活";
            });
        }

        private async Task DeactivateRelayWithTimeoutAsync(CancellationToken token)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeoutCts.CancelAfter(RelayTimeoutMs);

            Log($"正在复位继电器（{RelayControlChannel}）...");
            if (_jy7131Api != null && _jy7131Api.IsConnected)
            {
                await _jy7131Api.WriteDoAsync(RelayControlChannel, false, timeoutCts.Token).ConfigureAwait(false);
                try
                {
                    var mask = await _jy7131Api.ReadDoBitmaskAsync(timeoutCts.Token).ConfigureAwait(false);
                    var ok = int.TryParse(RelayControlChannel.Substring(2), out var doIdx);
                    var bit = ok ? doIdx : 1;
                    Log($"DO写回读取: mask=0x{mask:X8}，{RelayControlChannel}={(mask & (1u << bit)) != 0}");
                }
                catch (Exception ex)
                {
                    Log($"DO写回读取失败: {ex.Message}");
                }
            }
            else
            {
                Log("7131板卡不可用，使用仿真继电器动作");
                await _simulation.SimulateRelayDeactivateAsync(timeoutCts.Token).ConfigureAwait(false);
            }

            await Task.Delay(200, timeoutCts.Token).ConfigureAwait(false);
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                IsRelayActivated = false;
                RelayStatus = "未激活";
            });
        }

        private DeviceBase FindFirstJy7131Device()
        {
            var chassisList = _pxiChassisService?.GetAllChassis();
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

        private static string Infer7131SlotNumber(DeviceBase device)
        {
            if (device is MeasureControl.Models.Devices.DeviceCategories.PxiDeviceBase pxi && pxi.SlotIndex > 0)
                return pxi.SlotIndex.ToString();

            var slot = device?.SlotPosition;
            if (!string.IsNullOrWhiteSpace(slot))
            {
                var trimmed = slot.Replace("Slot", "").Replace("slot", "").Trim();
                if (int.TryParse(trimmed, out var slotNum) && slotNum > 0)
                    return slotNum.ToString();
            }
            return "12";
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
            if (string.IsNullOrWhiteSpace(message)) return;
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                Logs.Add($"[{DateTime.Now:HH:mm:ss.fff}] {message}");
                while (Logs.Count > 500)
                {
                    Logs.RemoveAt(0);
                }
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

            _measureLock?.Dispose();

            try
            {
                if (IsRelayActivated && _jy7131Api != null)
                    _jy7131Api.WriteDoAsync(RelayControlChannel, false).GetAwaiter().GetResult();
            }
            catch
            {
            }

            try
            {
                _jy7131Api?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            catch
            {
            }
            finally
            {
                _jy7131Api = null;
            }

            try
            {
                _relayPowerSupply?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            catch
            {
            }
            finally
            {
                _relayPowerSupply = null;
            }

            if (_projectSavingToken != null)
            {
                _eventAggregator?.GetEvent<ProjectSavingEvent>()?.Unsubscribe(_projectSavingToken);
            }
        }

        public sealed class ImpedanceItemViewModel : BindableBase
        {
            private readonly PowerImpedanceTestViewModel _owner;
            private string _impedanceText = "--";
            private string _result = "--";
            private bool _isMeasured;

            internal ImpedanceItemViewModel(
                PowerImpedanceTestViewModel owner,
                string indexText,
                string name)
            {
                _owner = owner;
                IndexText = indexText;
                Name = name;

                MeasureCommand = new DelegateCommand(async () => await _owner.MeasureAsync(this), () => _owner.CanMeasureItem(this));
            }

            public string IndexText { get; }
            public string Name { get; }

            public int ColumnIndex { get; set; }

            public string ImpedanceText
            {
                get => _impedanceText;
                private set => SetProperty(ref _impedanceText, value);
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

            public DelegateCommand MeasureCommand { get; }

            internal void UpdateMeasurement(double? valueOhm, string valueText, string result, bool measured)
            {
                ImpedanceText = valueText;
                Result = result;
                IsMeasured = measured;
            }
        }
    }
}
