using Prism.Commands;
using Prism.Events;
using Prism.Mvvm;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
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
    public sealed class ControlBoardPowerImpedanceTestViewModel : BindableBase, IDisposable
    {
        private const string TestItemKey = "InertController_ControlBoard_PowerImpedance";
        private const double ImpedanceThresholdOhm = 500.0;

        private const string RelayControlChannel = "DO15";
        private const int RelayTimeoutMs = 5000;

        private const string RelayPowerSupplyIpAddress = "192.168.1.15";
        private const PowerSupplyChannel RelayPowerChannel = PowerSupplyChannel.CH2;
        private const double RelayPowerVoltage = 24.0;
        private const double RelayPowerCurrentLimit = 1.0;

        private const string DmmIpAddress = "192.168.1.13";
        private const string MatrixIpAddress = "192.168.1.3";
        private const int MatrixTcpBasePort = 50200;

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

        private readonly SemaphoreSlim _measureLock = new SemaphoreSlim(1, 1);
        private CancellationTokenSource _cts;

        private bool _dmmWarmedUp;

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

        public ControlBoardPowerImpedanceTestViewModel(
            ISingleBoardTestContextService singleBoardTestContext,
            ProjectService projectService,
            IEventAggregator eventAggregator,
            IDmmApi dmm)
            : this(singleBoardTestContext, projectService, eventAggregator, dmm, null, null)
        {
        }

        public ControlBoardPowerImpedanceTestViewModel(
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
            ClearLogCommand = new DelegateCommand(() => Logs.Clear());

            Items.Add(new ImpedanceItemViewModel(
                this,
                "1)",
                "测J1J2到COM阻抗",
                "COM",
                signalPinOptions: new[] { "J1J2" },
                groundPinOptions: new[] { "COM" })
            { ColumnIndex = 8 });

            Items.Add(new ImpedanceItemViewModel(
                this,
                "2)",
                "测J4J5到COM阻抗",
                "COM",
                signalPinOptions: new[] { "J4J5" },
                groundPinOptions: new[] { "COM" })
            { ColumnIndex = 9 });

            Items.Add(new ImpedanceItemViewModel(
                this,
                "3)",
                "测28V_D到COM阻抗",
                "COM",
                signalPinOptions: new[] { "28V_D" },
                groundPinOptions: new[] { "COM" })
            { ColumnIndex = 10 });

            Items.Add(new ImpedanceItemViewModel(
                this,
                "4)",
                "测J1J2到J70EARTH阻抗",
                "EARTH",
                signalPinOptions: new[] { "J1J2" },
                groundPinOptions: new[] { "J70(EARTH)" })
            { ColumnIndex = 11 });

            Items.Add(new ImpedanceItemViewModel(
                this,
                "5)",
                "测J4J5到EARTH阻抗",
                "EARTH",
                signalPinOptions: new[] { "J4J5" },
                groundPinOptions: new[] { "J70(EARTH)" })
            { ColumnIndex = 12 });

            Items.Add(new ImpedanceItemViewModel(
                this,
                "6)",
                "测28V_D到EARTH阻抗",
                "EARTH",
                signalPinOptions: new[] { "28V_D" },
                groundPinOptions: new[] { "J70(EARTH)" })
            { ColumnIndex = 13 });

            LoadPersistedState();
            _projectSavingToken = _eventAggregator?.GetEvent<ProjectSavingEvent>()?.Subscribe(OnProjectSaving);
        }

        public ObservableCollection<ImpedanceItemViewModel> Items { get; } = new ObservableCollection<ImpedanceItemViewModel>();

        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();

        public DelegateCommand ManualTestCommand { get; }
        public DelegateCommand AutoTestCommand { get; }
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

        private async Task WarmupDmmAsync(CancellationToken token)
        {
            if (_dmmWarmedUp)
                return;

            try
            {
                await Task.Delay(300, token).ConfigureAwait(false);
                await EnsureDmmResistanceModeAsync(token).ConfigureAwait(false);
                _ = await _dmm.ReadOnceAsync(DmmMeasureMode.RES, new DmmReadOptions { TimeoutMilliseconds = 2000 }, token).ConfigureAwait(false);
                await Task.Delay(120, token).ConfigureAwait(false);
                _dmmWarmedUp = true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log($"万用表预热异常: {ex.Message}");
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

            if (IsAutoTestRunning)
            {
                await StopTestAsync().ConfigureAwait(false);
            }

            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            _dmmWarmedUp = false;

            ResetResults();

            IsManualTestRunning = true;
            IsAutoTestRunning = false;
            OverallResult = "--";
            LastTestTime = "--";

            Log("开始手动测试（控制板电源阻抗）");

            try
            {
                await EnsureComponentDownAsync(_cts.Token).ConfigureAwait(false);

                await Ensure7131ReadyAsync(_cts.Token).ConfigureAwait(false);

                await ActivateRelayWithTimeoutAsync(_cts.Token).ConfigureAwait(false);

                await _dmm.ConnectAsync(DmmIpAddress, _cts.Token).ConfigureAwait(false);
                Log($"万用表连接成功: {DmmIpAddress}");

                await EnsureDmmResistanceModeAsync(_cts.Token).ConfigureAwait(false);

                var matrix = MatrixControlService.Instance;
                var okCommon = await matrix.ConnectNodesAsync("I4", "O2", MatrixSlotCommon, MatrixIpAddress, MatrixTcpBasePort).ConfigureAwait(false);
                Log($"矩阵公共通路 I4-O2(slot{MatrixSlotCommon}) {(okCommon ? "OK" : "FAIL")}");
                if (!okCommon)
                {
                    await StopTestAsync().ConfigureAwait(false);
                }

                await WarmupDmmAsync(_cts.Token).ConfigureAwait(false);
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
            _dmmWarmedUp = false;

            ResetResults();

            IsAutoTestRunning = true;
            IsManualTestRunning = false;
            OverallResult = "--";
            LastTestTime = "--";

            Log("开始自动测试（控制板电源阻抗）");

            try
            {
                await EnsureComponentDownAsync(_cts.Token).ConfigureAwait(false);

                await Ensure7131ReadyAsync(_cts.Token).ConfigureAwait(false);

                await _dmm.ConnectAsync(DmmIpAddress, _cts.Token).ConfigureAwait(false);
                Log($"万用表连接成功: {DmmIpAddress}");

                await EnsureDmmResistanceModeAsync(_cts.Token).ConfigureAwait(false);

                var matrix = MatrixControlService.Instance;
                var okCommon = await matrix.ConnectNodesAsync("I4", "O2", MatrixSlotCommon, MatrixIpAddress, MatrixTcpBasePort).ConfigureAwait(false);
                Log($"矩阵公共通路 I4-O2(slot{MatrixSlotCommon}) {(okCommon ? "OK" : "FAIL")}");
                if (!okCommon)
                {
                    await StopTestAsync().ConfigureAwait(false);
                    return;
                }

                await WarmupDmmAsync(_cts.Token).ConfigureAwait(false);

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
            _dmmWarmedUp = false;

            ResetResults();
            OverallResult = "--";
            LastTestTime = "--";

            IsAutoTestRunning = true;
            IsManualTestRunning = false;

            Log("开始自动测试（控制板电源阻抗）");

            try
            {
                await EnsureComponentDownAsync(_cts.Token).ConfigureAwait(false);

                await Ensure7131ReadyAsync(_cts.Token).ConfigureAwait(false);

                await _dmm.ConnectAsync(DmmIpAddress, _cts.Token).ConfigureAwait(false);
                Log($"万用表连接成功: {DmmIpAddress}");

                await EnsureDmmResistanceModeAsync(_cts.Token).ConfigureAwait(false);

                var matrix = MatrixControlService.Instance;
                var okCommon = await matrix.ConnectNodesAsync("I4", "O2", MatrixSlotCommon, MatrixIpAddress, MatrixTcpBasePort).ConfigureAwait(false);
                Log($"矩阵公共通路 I4-O2(slot{MatrixSlotCommon}) {(okCommon ? "OK" : "FAIL")}");
                if (!okCommon)
                {
                    await StopTestAsync().ConfigureAwait(false);
                    return "FAIL";
                }

                await WarmupDmmAsync(_cts.Token).ConfigureAwait(false);

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
            return IsManualTestRunning && !IsBusy && !IsPowerOn && IsRelayActivated;
        }

        internal async Task MeasureAsync(ImpedanceItemViewModel item)
        {
            if (item == null) return;
            var cts = _cts;
            if (cts == null)
            {
                Log("测量已取消：测试未运行");
                return;
            }

            if (cts.IsCancellationRequested)
            {
                Log("测量已取消：当前测试已停止");
                return;
            }

            var token = cts.Token;
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

            await _measureLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (token.IsCancellationRequested)
                    return;

                IsBusy = true;

                Log($"开始测量: {item.Name}");

                var matrix = MatrixControlService.Instance;

                var okFixed = await matrix.ConnectNodesAsync("I4", "O2", MatrixSlotCommon, MatrixIpAddress, MatrixTcpBasePort).ConfigureAwait(false);
                Log($"矩阵固定通路 I4-O2(slot{MatrixSlotCommon}) {(okFixed ? "OK" : "FAIL")}");
                if (!okFixed)
                {
                    item.UpdateMeasurement(null, "--", "FAIL", measured: true);
                    return;
                }

                var output = $"O{item.ColumnIndex}";

                var ok = await matrix.ConnectNodesAsync("I1", output, MatrixSlotSequence, MatrixIpAddress, MatrixTcpBasePort).ConfigureAwait(false);
                Log($"矩阵连接 I1-{output}(slot{MatrixSlotSequence}) {(ok ? "OK" : "FAIL")}");
                if (!ok)
                {
                    item.UpdateMeasurement(null, "--", "FAIL", measured: true);
                    return;
                }

                await Task.Delay(150, token).ConfigureAwait(false);

                await EnsureDmmResistanceModeAsync(token).ConfigureAwait(false);
                var reading = await SafeReadResistanceAsync(token).ConfigureAwait(false);
                ApplyReading(item, reading);
            }
            catch (OperationCanceledException)
            {
                Log("测量已取消");
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

        private async Task EnsureDmmResistanceModeAsync(CancellationToken token)
        {
            try
            {
                await _dmm.SendAsync(":CONF:RES", token).ConfigureAwait(false);
                await Task.Delay(80, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log($"万用表切换电阻档异常: {ex.Message}");
            }
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

        private async Task ResetAndClose7131Async(CancellationToken token)
        {
            var api = _jy7131Api;
            if (api == null)
                return;

            try
            {
                if (api.IsConnected)
                {
                    if (api.IsRunning)
                    {
                        try { await api.StopAsync(token).ConfigureAwait(false); } catch { }
                    }

                    try { await api.DisconnectAsync(token).ConfigureAwait(false); } catch { }
                }
            }
            finally
            {
                try { await api.DisposeAsync().ConfigureAwait(false); } catch { }
                if (ReferenceEquals(_jy7131Api, api))
                {
                    _jy7131Api = null;
                }
            }
        }

        private async Task StopTestAsync()
        {
            try { _cts?.Cancel(); } catch { }

            IsManualTestRunning = false;
            IsAutoTestRunning = false;
            IsBusy = false;

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
                await ResetAndClose7131Async(CancellationToken.None).ConfigureAwait(false);
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

            await DisableRelay24VPowerAsync(CancellationToken.None).ConfigureAwait(false);

            RaiseCanExecuteChangedForItems();
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
        }

        private async Task EnsureComponentDownAsync(CancellationToken token)
        {
            bool componentDownOk = false;

            if (_componentPowerStateApi != null)
            {
                try
                {
                    Log("正在设置组件供电状态: 下电...");
                    await _componentPowerStateApi.ApplyComponentDownStateAsync(token).ConfigureAwait(false);
                    Log("组件供电状态已设置为下电");
                    componentDownOk = true;
                }
                catch (Exception ex)
                {
                    Log($"组件下电状态设置异常: {ex.Message}");
                }
            }

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
                if (_componentPowerStateApi != null && componentDownOk)
                {
                    IsPowerOn = false;
                    PowerStatus = "已下电";
                }
                else if (_componentPowerStateApi != null && !componentDownOk)
                {
                    IsPowerOn = true;
                    PowerStatus = "下电失败";
                }
                else
                {
                    IsPowerOn = false;
                    PowerStatus = "请确认已下电";
                }
            });
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
            if (_jy7131Api == null)
            {
                Log("7131未就绪，无法吸合继电器");
                return;
            }

            if (IsRelayActivated)
                return;

            var start = DateTime.UtcNow;
            RelayStatus = "正在激活";
            Log($"继电器吸合: 写 {RelayControlChannel}=1");

            while (!token.IsCancellationRequested)
            {
                try
                {
                    await _jy7131Api.WriteDoAsync(RelayControlChannel, true, token).ConfigureAwait(false);
                    IsRelayActivated = true;
                    RelayStatus = "已激活";
                    Log("继电器已激活");
                    return;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Log($"继电器吸合写DO异常: {ex.Message}");
                }

                if ((DateTime.UtcNow - start).TotalMilliseconds >= RelayTimeoutMs)
                {
                    RelayStatus = "激活超时";
                    Log("继电器激活超时");
                    return;
                }

                await Task.Delay(100, token).ConfigureAwait(false);
            }
        }

        private async Task DeactivateRelayWithTimeoutAsync(CancellationToken token)
        {
            if (_jy7131Api == null)
            {
                IsRelayActivated = false;
                RelayStatus = "未激活";
                return;
            }

            if (!IsRelayActivated)
                return;

            var start = DateTime.UtcNow;
            RelayStatus = "正在释放";
            Log($"继电器释放: 写 {RelayControlChannel}=0");

            while (!token.IsCancellationRequested)
            {
                try
                {
                    await _jy7131Api.WriteDoAsync(RelayControlChannel, false, token).ConfigureAwait(false);
                    IsRelayActivated = false;
                    RelayStatus = "未激活";
                    Log("继电器已释放");
                    return;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Log($"继电器释放写DO异常: {ex.Message}");
                }

                if ((DateTime.UtcNow - start).TotalMilliseconds >= RelayTimeoutMs)
                {
                    RelayStatus = "释放超时";
                    Log("继电器释放超时");
                    return;
                }

                await Task.Delay(100, token).ConfigureAwait(false);
            }
        }

        private DeviceBase FindFirstJy7131Device()
        {
            try
            {
                var chassisList = _pxiChassisService?.GetAllChassis();
                if (chassisList == null)
                    return null;

                foreach (var chassis in chassisList)
                {
                    if (chassis?.Devices == null)
                        continue;

                    var dev = chassis.Devices.FirstOrDefault(d =>
                        (d?.Model?.IndexOf("7131", StringComparison.OrdinalIgnoreCase) >= 0) ||
                        (d?.DeviceTypeName?.IndexOf("离散量", StringComparison.OrdinalIgnoreCase) >= 0) ||
                        (d?.DeviceTypeName?.IndexOf("数字量", StringComparison.OrdinalIgnoreCase) >= 0));
                    if (dev != null)
                        return dev;
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private string Infer7131SlotNumber(DeviceBase device)
        {
            try
            {
                var slotPos = device?.SlotPosition;
                if (string.IsNullOrWhiteSpace(slotPos))
                    return null;

                var m = Regex.Match(slotPos, "(\\d+)");
                if (m.Success)
                    return m.Groups[1].Value;

                return null;
            }
            catch
            {
                return null;
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
            if (string.IsNullOrWhiteSpace(message)) return;

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null)
                return;

            void Append()
            {
                Logs.Add($"[{DateTime.Now:HH:mm:ss.fff}] {message}");
                while (Logs.Count > 500)
                {
                    Logs.RemoveAt(0);
                }
            }

            if (dispatcher.CheckAccess())
            {
                Append();
                return;
            }

            dispatcher.BeginInvoke(new Action(Append));
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

            if (_projectSavingToken != null)
            {
                _eventAggregator?.GetEvent<ProjectSavingEvent>()?.Unsubscribe(_projectSavingToken);
            }
        }

        public sealed class ImpedanceItemViewModel : BindableBase
        {
            private readonly ControlBoardPowerImpedanceTestViewModel _owner;
            private string _impedanceText = "--";
            private string _result = "--";
            private bool _isMeasured;

            private string _signalPin;
            private string _groundPin;

            internal ImpedanceItemViewModel(
                ControlBoardPowerImpedanceTestViewModel owner,
                string indexText,
                string name,
                string groupKey,
                string[] signalPinOptions,
                string[] groundPinOptions)
            {
                _owner = owner;
                IndexText = indexText;
                Name = name;
                GroupKey = groupKey;

                SignalPinOptions = new ObservableCollection<string>((signalPinOptions ?? Array.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)));
                GroundPinOptions = new ObservableCollection<string>((groundPinOptions ?? Array.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)));

                SignalPin = SignalPinOptions.FirstOrDefault();
                GroundPin = GroundPinOptions.FirstOrDefault();

                MeasureCommand = new DelegateCommand(async () => await _owner.MeasureAsync(this), () => _owner.CanMeasureItem(this));
            }

            public string IndexText { get; }
            public string Name { get; }
            public string GroupKey { get; }

            public ObservableCollection<string> SignalPinOptions { get; }

            public ObservableCollection<string> GroundPinOptions { get; }

            public string SignalPin
            {
                get => _signalPin;
                set => SetProperty(ref _signalPin, value);
            }

            public string GroundPin
            {
                get => _groundPin;
                set => SetProperty(ref _groundPin, value);
            }

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
