using MeasureControl.Events;
using MeasureControl.Models;
using MeasureControl.Models.Devices;
using MeasureControl.Services;
using MeasureControl.Services.HardwareApis;
using MeasureControl.Drivers;
using Prism.Commands;
using Prism.Events;
using Prism.Ioc;
using Prism.Mvvm;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace MeasureControl.ViewModels.SingleBoardTest.InertController
{
    public sealed class OverTemperatureCutoffTestViewModel : BindableBase, IDisposable
    {
        private const string TestItemKey = "InertController_OverTemperatureCutoff";

        private static readonly byte[] FpgaFrameHeader = new byte[] { 0xAA, 0x55 };

        private const double PtA = 3.9083e-3;
        private const double PtB = -5.775e-7;
        private const double PtC = -4.183e-12;

        // PT500A 温度-阻值对照表 (温度单位: 0.1℃, 阻值单位: Ω)
        // 温度范围: 110.2℃ ~ 113.8℃, 步长0.2℃
        private static readonly Dictionary<int, double> Pt500ResistanceTable = new Dictionary<int, double>
        {
            { 1102, 711.8407 },  // 110.2℃
            { 1104, 712.2188 },  // 110.4℃
            { 1106, 712.5969 },  // 110.6℃
            { 1108, 712.9749 },  // 110.8℃
            { 1110, 713.3530 },  // 111.0℃
            { 1112, 713.7310 },  // 111.2℃
            { 1114, 714.1089 },  // 111.4℃
            { 1116, 714.4869 },  // 111.6℃
            { 1118, 714.8648 },  // 111.8℃
            { 1120, 715.2427 },  // 112.0℃
            { 1122, 715.6206 },  // 112.2℃
            { 1124, 715.9985 },  // 112.4℃
            { 1126, 716.3763 },  // 112.6℃
            { 1128, 716.7541 },  // 112.8℃
            { 1130, 717.1319 },  // 113.0℃
            { 1132, 717.5097 },  // 113.2℃
            { 1134, 717.8874 },  // 113.4℃
            { 1136, 718.2651 },  // 113.6℃
            { 1138, 718.6428 },  // 113.8℃
        };//(711.8407, 718.6428)

        // PT1000A 温度-阻值对照表 (温度单位: 0.1℃, 阻值单位: Ω)
        // 温度范围: 105.2℃ ~ 108.8℃, 步长0.2℃
        private static readonly Dictionary<int, double> Pt1000ResistanceTable = new Dictionary<int, double>
        {
            { 1052, 1404.7619 },  // 105.2℃
            { 1054, 1405.5193 },  // 105.4℃
            { 1056, 1406.2766 },  // 105.6℃
            { 1058, 1407.0338 },  // 105.8℃
            { 1060, 1407.7910 },  // 106.0℃
            { 1062, 1408.5482 },  // 106.2℃
            { 1064, 1409.3053 },  // 106.4℃
            { 1066, 1410.0623 },  // 106.6℃
            { 1068, 1410.8193 },  // 106.8℃
            { 1070, 1411.5763 },  // 107.0℃
            { 1072, 1412.3332 },  // 107.2℃
            { 1074, 1413.0901 },  // 107.4℃
            { 1076, 1413.8469 },  // 107.6℃
            { 1078, 1414.6037 },  // 107.8℃
            { 1080, 1415.3604 },  // 108.0℃
            { 1082, 1416.1171 },  // 108.2℃
            { 1084, 1416.8738 },  // 108.4℃
            { 1086, 1417.6304 },  // 108.6℃
            { 1088, 1418.3869 },  // 108.8℃
        };//(1404.7619, 1418.3869)

        private const string FpgaServerIpAddress = "192.168.1.10";
        private const int FpgaServerPort = 5001;

        private const string DmmIpAddress = "192.168.1.13";
        private const string MatrixIpAddress = "192.168.1.3";
        private const int MatrixTcpBasePort = 50200;

        private const string PowerSupplyIpAddress = "192.168.1.15";
        private const double InputCurrentA = 1.0;

        public bool SkipMainPowerOff { get; set; }

        private const int MatrixSlotSequence = 6;
        private const int MatrixSlotCommon = 4;

        private readonly ISingleBoardTestContextService _singleBoardTestContext;
        private readonly ProjectService _projectService;
        private readonly IEventAggregator _eventAggregator;
        private readonly IDmmApi _dmm;
        private readonly IPxiChassisService _pxiChassisService;
        private readonly IComponentPowerStateApi _componentPowerStateApi;

        private IPowerSupplyApi _power;
        private IPxi7012Api _resistor;
        private uint? _connectedResistorLogicalId;
        private JY7131Driver _diDriver;

        private TcpClient _fpgaClient;
        private NetworkStream _fpgaStream;

        private readonly SemaphoreSlim _fpgaSendLock = new SemaphoreSlim(1, 1);

        private uint? _lastFpgaGpioInput;
        private DateTime? _lastFpgaGpioInputTime;

        private readonly SemaphoreSlim _resistorLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _diLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _sweepLock = new SemaphoreSlim(1, 1);

        private CancellationTokenSource _cts;
        private DateTime _lastAutoTestEndTime = DateTime.MinValue;

        private bool _isManualTestRunning;
        private bool _isAutoTestRunning;
        private bool _isBusy;

        private bool _isPowerOn;
        private string _powerStatus = "未供电";

        private string _overallResult = "--";
        private string _lastTestTime = "--";

        private readonly Dictionary<string, string> _pinMatrixPointMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public OverTemperatureCutoffTestViewModel(
            ISingleBoardTestContextService singleBoardTestContext,
            ProjectService projectService,
            IEventAggregator eventAggregator,
            IDmmApi dmm,
            IPxiChassisService pxiChassisService,
            IComponentPowerStateApi componentPowerStateApi = null)
        {
            _singleBoardTestContext = singleBoardTestContext;
            _projectService = projectService;
            _eventAggregator = eventAggregator;
            _dmm = dmm;
            _pxiChassisService = pxiChassisService;
            _componentPowerStateApi = componentPowerStateApi;

            ManualTestCommand = new DelegateCommand(async () => await OnManualTestAsync());
            AutoTestCommand = new DelegateCommand(async () => await OnAutoTestAsync());
            ClearLogCommand = new DelegateCommand(() => Logs.Clear());

            Items.Add(CreatePt500aItem());
            Items.Add(CreatePt1000aItem());
        }

        public ObservableCollection<OverTempItemViewModel> Items { get; } = new ObservableCollection<OverTempItemViewModel>();

        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();

        public DelegateCommand ManualTestCommand { get; }

        public DelegateCommand AutoTestCommand { get; }

        public DelegateCommand ClearLogCommand { get; }

        public bool IsPowerOn
        {
            get => _isPowerOn;
            private set
            {
                if (SetProperty(ref _isPowerOn, value))
                {
                    RaiseCanExecuteChangedForItems();
                }
            }
        }

        private static int? MapPinToIo43To64BitIndex(string pin)
        {
            if (string.Equals(pin, "J31", StringComparison.OrdinalIgnoreCase))
                return 0;
            if (string.Equals(pin, "J32", StringComparison.OrdinalIgnoreCase))
                return 1;
            return null;
        }

        private static bool GetIo43To64Bit(uint gpioValue, int bitIndex)
        {
            if (bitIndex < 0 || bitIndex > 21)
                return false;
            return ((gpioValue >> bitIndex) & 0x1u) == 1u;
        }

        private async Task MeasureFpgaIoAsync(OverTempCheckViewModel check, CancellationToken token)
        {
            var bitIndex = MapPinToIo43To64BitIndex(check.Pin);
            if (bitIndex == null)
            {
                check.UpdateMeasurement(null, "--", "FAIL", measured: true);
                Log($"未配置FPGA IO映射: {check.Pin}");
                return;
            }

            try
            {
                await EnsureFpgaTcpConnectedAsync(token).ConfigureAwait(false);

                await SendFpgaFrameAsync(0x0A, new byte[] { 0x00 }, token).ConfigureAwait(false);
                Log("[FPGA TX] Force Read: AA 55 02 0A 00");

                var gpio = await ReadFpgaGpioInputOnceAsync(2000, token, acceptCmd: 0x0A).ConfigureAwait(false);
                var isHigh = GetIo43To64Bit(gpio, bitIndex.Value);

                var valueText = isHigh ? "高电平" : "低电平";
                var pass = isHigh;
                check.UpdateMeasurement(isHigh ? 1.0 : 0.0, valueText, pass ? "PASS" : "FAIL", measured: true);

                var ioNumber = 43 + bitIndex.Value;
                var ts = _lastFpgaGpioInputTime?.ToString("HH:mm:ss", CultureInfo.InvariantCulture) ?? "--";
                Log($"FPGA IO读取: {check.Pin}=IO{ioNumber}(bit{bitIndex.Value}) => {valueText} => {(pass ? "PASS" : "FAIL")}, 数据时间={ts}");
            }
            catch (TimeoutException ex)
            {
                check.UpdateMeasurement(null, "未接收", "FAIL", measured: true);
                Log(ex.Message);
            }
            catch (Exception ex)
            {
                check.UpdateMeasurement(null, "异常", "FAIL", measured: true);
                Log($"FPGA采集异常: {ex.Message}");
            }
            finally
            {
                try { await DisconnectFpgaTcpAsync().ConfigureAwait(false); } catch { }
            }
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

        public uint? LastFpgaGpioInput
        {
            get => _lastFpgaGpioInput;
            private set => SetProperty(ref _lastFpgaGpioInput, value);
        }

        private OverTempItemViewModel CreatePt500aItem()
        {
            const double r0 = 500.0;
            const int minTempDeciC = 1102;  // 110.2℃
            const int maxTempDeciC = 1138;  // 113.8℃
            const int stepDeciC = 2;        // 0.2℃ 步长
            const int nominalFirstOverTempDeciC = 1120; // 112.0℃
            const int firstOverTempToleranceDeciC = 18; // ±1.8℃

            var item = new OverTempItemViewModel(this,
                title: "PT500A 超温切断",
                resistanceLabel: $"{FormatTemp(minTempDeciC)}~{FormatTemp(maxTempDeciC)}℃ ({FormatOhm(GetPt500Resistance(minTempDeciC))}~{FormatOhm(GetPt500Resistance(maxTempDeciC))})Ω",
                targetResistanceOhm: GetPt500Resistance(maxTempDeciC),
                resistanceToleranceOhm: 3.5,
                roChannel: "RO0",
                r0Ohm: r0,
                minTempDeciC: minTempDeciC,
                maxTempDeciC: maxTempDeciC,
                stepTempDeciC: stepDeciC,
                resistanceTableType: ResistanceTableType.PT500,
                nominalFirstOverTempDeciC: nominalFirstOverTempDeciC,
                firstOverTempToleranceDeciC: firstOverTempToleranceDeciC);

            item.Checks.Add(new OverTempCheckViewModel(this, item,
                pin: "J31",
                pinName: "T1_AWARN",
                expected: "高电平(3.3±0.33V)",
                evaluation: OverTempEvaluation.High33));

            item.Checks.Add(new OverTempCheckViewModel(this, item,
                pin: "J11",
                pinName: "IIV +28VDC PWR IN_FB",
                expected: "开路",
                evaluation: OverTempEvaluation.DIOpen,
                diChannel: "DI1"));

            item.Checks.Add(new OverTempCheckViewModel(this, item,
                pin: "J12",
                pinName: "IIV +28VDC PWR IN",
                expected: "开路",
                evaluation: OverTempEvaluation.DIOpen,
                diChannel: "DI2"));

            return item;
        }

        private OverTempItemViewModel CreatePt1000aItem()
        {
            const double r0 = 1000.0;
            const int minTempDeciC = 1052;  // 105.2℃
            const int maxTempDeciC = 1088;  // 108.8℃
            const int stepDeciC = 2;        // 0.2℃ 步长
            const int nominalFirstOverTempDeciC = 1070; // 107.0℃
            const int firstOverTempToleranceDeciC = 18; // ±1.8℃

            var item = new OverTempItemViewModel(this,
                title: "PT1000A 超温切断",
                resistanceLabel: $"{FormatTemp(minTempDeciC)}~{FormatTemp(maxTempDeciC)}℃ ({FormatOhm(GetPt1000Resistance(minTempDeciC))}~{FormatOhm(GetPt1000Resistance(maxTempDeciC))})Ω",
                targetResistanceOhm: GetPt1000Resistance(maxTempDeciC),
                resistanceToleranceOhm: 7.1,
                roChannel: "RO1",
                r0Ohm: r0,
                minTempDeciC: minTempDeciC,
                maxTempDeciC: maxTempDeciC,
                stepTempDeciC: stepDeciC,
                resistanceTableType: ResistanceTableType.PT1000,
                nominalFirstOverTempDeciC: nominalFirstOverTempDeciC,
                firstOverTempToleranceDeciC: firstOverTempToleranceDeciC);

            item.Checks.Add(new OverTempCheckViewModel(this, item,
                pin: "J32",
                pinName: "T2_AWARN",
                expected: "高电平(3.3±0.33V)",
                evaluation: OverTempEvaluation.High33));

            item.Checks.Add(new OverTempCheckViewModel(this, item,
                pin: "J13",
                pinName: "TIV +28VDC PWR IN_FB",
                expected: "开路",
                evaluation: OverTempEvaluation.DIOpen,
                diChannel: "DI3"));

            item.Checks.Add(new OverTempCheckViewModel(this, item,
                pin: "J14",
                pinName: "TIV +28VDC PWR IN",
                expected: "开路",
                evaluation: OverTempEvaluation.DIOpen,
                diChannel: "DI4"));

            return item;
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
            {
                await StopTestAsync().ConfigureAwait(false);
            }

            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();

            ResetResults();
            OverallResult = "--";
            LastTestTime = "--";
            LastFpgaGpioInput = null;
            _lastFpgaGpioInputTime = null;

            IsManualTestRunning = true;
            IsAutoTestRunning = false;

            Log("开始手动测试");

            try
            {
                Log($"电源: CH1 28V 1A, IP={PowerSupplyIpAddress}");
                await EnsurePowerAsync(28.0, _cts.Token).ConfigureAwait(false);
                IsPowerOn = true;
                PowerStatus = "已供电";
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
                Log("自动测试已在运行中，停止当前测试");
                await StopTestAsync().ConfigureAwait(false);
                return;
            }

            // 检查是否已总上电
            var _hps = ContainerLocator.Container.Resolve<IBoardPowerService>();
            Log($"总上电状态: HPS={(_hps == null ? "null" : (_hps.IsPowered ? "Powered" : "NotPowered"))}");
            if (_hps == null || !_hps.IsPowered)
            {
                MessageBox.Show("请先点击左上角组件上电按钮进行总上电，再进行测试。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var elapsed = (DateTime.Now - _lastAutoTestEndTime).TotalMilliseconds;
            if (elapsed < 1000)
            {
                Log($"自动测试冷却中，请等待{(int)(1000 - elapsed)}ms后再试");
                return;
            }

            if (IsManualTestRunning)
            {
                Log("手动测试正在运行，先停止手动测试");
                await StopTestAsync().ConfigureAwait(false);
            }

            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();

            ResetResults();
            OverallResult = "--";
            LastTestTime = "--";
            LastFpgaGpioInput = null;
            _lastFpgaGpioInputTime = null;

            IsAutoTestRunning = true;
            IsManualTestRunning = false;

            Log("========== 开始自动测试 ==========");

            try
            {
                Log($"电源: CH1 28V 1A, IP={PowerSupplyIpAddress}");
                await EnsurePowerAsync(28.0, _cts.Token).ConfigureAwait(false);
                IsPowerOn = true;
                PowerStatus = "已供电";
                Log("供电完成");

                Log($"共有 {Items.Count} 个测试项需要执行");
                var itemIndex = 0;
                foreach (var item in Items)
                {
                    itemIndex++;
                    if (_cts.IsCancellationRequested)
                    {
                        Log("测试被取消");
                        return;
                    }

                    Log($"---------- 开始测试项 {itemIndex}/{Items.Count}: {item.Title} ----------");
                    await ExecuteOverTempSweepAndMeasureAsync(item, _cts.Token).ConfigureAwait(false);
                    Log($"---------- 测试项 {itemIndex}/{Items.Count} 完成 ----------");
                }

                EvaluateOverall();
                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                Log($"========== 自动测试完成，总体结果: {OverallResult} ==========");

                await StopTestAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Log("自动测试被取消");
                await StopTestAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log($"自动测试失败: {ex.GetType().Name}: {ex.Message}");
                Log($"堆栈: {ex.StackTrace}");
                await StopTestAsync().ConfigureAwait(false);
            }
            finally
            {
                _lastAutoTestEndTime = DateTime.Now;
                Log("自动测试流程结束");
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
            OverallResult = "--";
            LastTestTime = "--";
            LastFpgaGpioInput = null;
            _lastFpgaGpioInputTime = null;

            IsAutoTestRunning = true;
            IsManualTestRunning = false;

            Log("开始自动测试");

            try
            {
                Log($"电源: CH1 28V 1A, IP={PowerSupplyIpAddress}");
                await EnsurePowerAsync(28.0, _cts.Token).ConfigureAwait(false);
                IsPowerOn = true;
                PowerStatus = "已供电";

                foreach (var item in Items)
                {
                    _cts.Token.ThrowIfCancellationRequested();

                    await ExecuteOverTempSweepAndMeasureAsync(item, _cts.Token).ConfigureAwait(false);
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

        internal bool CanApplyResistance(OverTempItemViewModel item)
        {
            if (item == null) return false;
            return IsManualTestRunning && !IsBusy && IsPowerOn;
        }

        internal bool CanSweepItem(OverTempItemViewModel item)
        {
            if (item == null) return false;
            return IsManualTestRunning && !IsBusy && IsPowerOn;
        }

        internal async Task ApplyResistanceAsync(OverTempItemViewModel item)
        {
            if (item == null) return;
            var token = _cts?.Token ?? CancellationToken.None;
            await ApplyResistanceAsync(item, token).ConfigureAwait(false);

            EvaluateOverall();
        }

        internal async Task SweepItemAsync(OverTempItemViewModel item)
        {
            if (item == null) return;
            var token = _cts?.Token ?? CancellationToken.None;

            await _sweepLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                IsBusy = true;
                Log($"开始扫温: {item.Title}");
                await ExecuteOverTempSweepAndMeasureAsync(item, token).ConfigureAwait(false);
                EvaluateOverall();
                if (Items.All(i => i.IsMeasured))
                {
                    LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                }
            }
            catch (OperationCanceledException)
            {
                Log($"扫温已取消: {item.Title}");
            }
            catch (Exception ex)
            {
                Log($"扫温失败: {item.Title}, {ex.Message}");
            }
            finally
            {
                IsBusy = false;
                _sweepLock.Release();
            }
        }

        private async Task ApplyResistanceAsync(OverTempItemViewModel item, CancellationToken token)
        {
            await _resistorLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                IsBusy = true;

                Log($"设置电阻: {item.Title}, 通道={item.RoChannel}, 目标={item.TargetResistanceOhm.ToString("0.###", CultureInfo.InvariantCulture)}Ω");

                var okReady = await EnsureResistorAsync(token).ConfigureAwait(false);
                if (!okReady)
                {
                    item.UpdateResistance(null, "--", measured: true);
                    return;
                }

                var apiChannel = MapRoChannelTo7012Api(item.RoChannel);
                try
                {
                    await _resistor.SetRelayStateAsync(apiChannel, pathRelayClosed: true, shortCircuitClosed: false, token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Log($"设置7012继电器失败: {ex.Message}");
                    item.UpdateResistance(null, "--", measured: true);
                    return;
                }

                try
                {
                    await _resistor.SetResistanceAsync(apiChannel, item.TargetResistanceOhm, Pxi7012OutputMode.NoWait, token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Log($"设置7012电阻失败: {ex.Message}");
                    item.UpdateResistance(null, "--", measured: true);
                    return;
                }

                await Task.Delay(50, token).ConfigureAwait(false);
                double? r = null;
                try
                {
                    r = await _resistor.GetResistanceAsync(apiChannel, token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Log($"电阻回读异常: {ex.Message}");
                }

                var resistanceResult = IsResistanceInRange(item, r) ? "PASS" : "FAIL";
                item.UpdateResistance(r, resistanceResult, measured: true);

                Log($"电阻回读: {(r == null ? "--" : r.Value.ToString("0.###", CultureInfo.InvariantCulture))}Ω");

                LastFpgaGpioInput = null;
                _lastFpgaGpioInputTime = null;
            }
            catch (Exception ex)
            {
                Log($"设置电阻异常: {ex.Message}");
                item.UpdateResistance(null, "--", measured: true);
            }
            finally
            {
                IsBusy = false;
                _resistorLock.Release();
            }
        }

        private async Task<DmmReading> SafeReadVoltageAsync(CancellationToken token)
        {
            try
            {
                return await _dmm.ReadOnceAsync(DmmMeasureMode.DCV, new DmmReadOptions { TimeoutMilliseconds = 8000 }, token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log($"万用表读数异常: {ex.Message}");
                return null;
            }
        }

        private void ApplyReading(OverTempCheckViewModel check, DmmReading reading)
        {
            if (reading == null)
            {
                check.UpdateMeasurement(null, "--", "FAIL", measured: true);
                return;
            }

            if (reading.IsOverrange)
            {
                check.UpdateMeasurement(null, "OL", "FAIL", measured: true);
                Log("读数为OL(过量程)，判为FAIL");
                return;
            }

            if (reading.Value == null)
            {
                check.UpdateMeasurement(null, "--", "FAIL", measured: true);
                return;
            }

            var v = reading.Value.Value;
            var text = v.ToString("0.###", CultureInfo.InvariantCulture);
            var pass = EvaluateCheckVoltage(check.Evaluation, v);
            check.UpdateMeasurement(v, text, pass ? "PASS" : "FAIL", measured: true);
            Log($"读数: {v:0.###} V, 期望: {check.Expected} => {(pass ? "PASS" : "FAIL")}");
        }

        private async Task MeasureDIAsync(OverTempCheckViewModel check, CancellationToken token)
        {
            try
            {
                var okReady = await EnsureDIDriverAsync(token).ConfigureAwait(false);
                if (!okReady)
                {
                    check.UpdateMeasurement(null, "--", "FAIL", measured: true);
                    Log($"7131板卡未连接");
                    return;
                }

                await _diLock.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    var diValue = await _diDriver.ReadChannelAsync(check.DiChannel).ConfigureAwait(false);
                    var isHigh = diValue > 0.5;
                    var stateText = isHigh ? "GND" : "开路";
                    var pass = EvaluateDICheck(check.Evaluation, isHigh);
                    
                    check.UpdateMeasurement(diValue, stateText, pass ? "PASS" : "FAIL", measured: true);
                    Log($"DI读数: {check.DiChannel}={stateText} => {(pass ? "PASS" : "FAIL")}");
                }
                finally
                {
                    _diLock.Release();
                }
            }
            catch (Exception ex)
            {
                Log($"DI测量异常: {ex.Message}");
                check.UpdateMeasurement(null, "--", "FAIL", measured: true);
            }
        }

        private static bool EvaluateCheckVoltage(OverTempEvaluation evaluation, double v)
        {
            switch (evaluation)
            {
                case OverTempEvaluation.High33:
                    return Math.Abs(v - 3.3) <= 0.33;
                case OverTempEvaluation.OpenLe16:
                    return v <= 16.0;
                case OverTempEvaluation.DIOpen:
                case OverTempEvaluation.DIGND:
                    return false;
                default:
                    return false;
            }
        }

        private static bool EvaluateDICheck(OverTempEvaluation evaluation, bool isHigh)
        {
            switch (evaluation)
            {
                case OverTempEvaluation.DIOpen:
                    return !isHigh;
                case OverTempEvaluation.DIGND:
                    return isHigh;
                default:
                    return false;
            }
        }

        private static bool IsResistanceInRange(OverTempItemViewModel item, double? r)
        {
            if (item == null || r == null) return false;
            return Math.Abs(r.Value - item.TargetResistanceOhm) <= item.ResistanceToleranceOhm;
        }

        private string ResolveMatrixPointForPin(string pin)
        {
            if (string.IsNullOrWhiteSpace(pin))
                return null;

            if (_pinMatrixPointMap.TryGetValue(pin.Trim(), out var point))
                return point;

            return null;
        }

        private void ResetResults()
        {
            foreach (var item in Items)
            {
                item.UpdateResistance(null, "--", measured: false);
                item.ResetFirstOverTempTrigger();
                item.UpdatePass(false);
                item.UpdateTriggerStatus(null);
                item.UnfreezeResistance();
                foreach (var check in item.Checks)
                {
                    check.Unfreeze();
                    check.UpdateMeasurement(null, "---", "--", measured: false);
                }
            }
        }

        private static string FormatTemp(int deciC)
        {
            return (deciC / 10.0).ToString("0.0", CultureInfo.InvariantCulture);
        }

        private static string FormatOhm(double ohm)
        {
            return Math.Round(ohm, 2, MidpointRounding.AwayFromZero).ToString("0.##", CultureInfo.InvariantCulture);
        }

        private static double PtResistanceOhm(double r0Ohm, double tempC)
        {
            if (tempC >= 0)
                return r0Ohm * (1.0 + PtA * tempC + PtB * tempC * tempC);

            return r0Ohm * (1.0 + PtA * tempC + PtB * tempC * tempC + PtC * (tempC - 100.0) * tempC * tempC * tempC);
        }

        private static double GetPt500Resistance(int tempDeciC)
        {
            if (Pt500ResistanceTable.TryGetValue(tempDeciC, out var r))
                return r;
            // 如果不在表中，使用公式计算作为后备
            return PtResistanceOhm(500.0, tempDeciC / 10.0);
        }

        private static double GetPt1000Resistance(int tempDeciC)
        {
            if (Pt1000ResistanceTable.TryGetValue(tempDeciC, out var r))
                return r;
            // 如果不在表中，使用公式计算作为后备
            return PtResistanceOhm(1000.0, tempDeciC / 10.0);
        }

        private static double GetResistanceByTable(ResistanceTableType tableType, int tempDeciC)
        {
            switch (tableType)
            {
                case ResistanceTableType.PT500:
                    return GetPt500Resistance(tempDeciC);
                case ResistanceTableType.PT1000:
                    return GetPt1000Resistance(tempDeciC);
                default:
                    return PtResistanceOhm(500.0, tempDeciC / 10.0);
            }
        }

        public enum ResistanceTableType
        {
            PT500,
            PT1000
        }

        private async Task<bool?> ReadAwarnAsync(string pin, CancellationToken token)
        {
            var bitIndex = MapPinToIo43To64BitIndex(pin);
            if (bitIndex == null)
                return null;

            await EnsureFpgaTcpConnectedAsync(token).ConfigureAwait(false);
            await SendFpgaFrameAsync(0x0A, new byte[] { 0x00 }, token).ConfigureAwait(false);
            var gpio = await ReadFpgaGpioInputOnceAsync(2000, token, acceptCmd: 0x0A).ConfigureAwait(false);
            return GetIo43To64Bit(gpio, bitIndex.Value);
        }

        private static List<int> BuildSweepTemps(int minDeciC, int maxDeciC, int stepDeciC)
        {
            if (stepDeciC <= 0) throw new ArgumentOutOfRangeException(nameof(stepDeciC));

            var list = new List<int>();
            for (int t = minDeciC; t <= maxDeciC; t += stepDeciC)
                list.Add(t);
            if (list.Count == 0 || list[list.Count - 1] != maxDeciC)
                list.Add(maxDeciC);
            return list;
        }

        private async Task ExecuteOverTempSweepAndMeasureAsync(OverTempItemViewModel item, CancellationToken token)
        {
            if (item == null) return;

            var awarnCheck = item.Checks.FirstOrDefault();
            var minDeciC = item.MinTempDeciC;
            var maxDeciC = item.MaxTempDeciC;
            var stepDeciC = item.StepTempDeciC;

            var belowDeciC = minDeciC - stepDeciC;
            var aboveDeciC = maxDeciC + stepDeciC;
            var inRangeTemps = BuildSweepTemps(minDeciC, maxDeciC, stepDeciC);

            bool pass = true;
            int? firstOverTempDeciC = null;
            bool freezeChecks = false;

            try
            {
                await EnsureResistorAsync(token).ConfigureAwait(false);
                await EnsureFpgaTcpConnectedAsync(token).ConfigureAwait(false);

                Log($"[{item.Title}] 扫描范围: {FormatTemp(minDeciC)}~{FormatTemp(maxDeciC)}℃, 步长={stepDeciC / 10.0:0.0}℃");

                var isHighBelow = await StepAndReadAwarnOnlyAsync(item, awarnCheck, belowDeciC, token, updateUi: false).ConfigureAwait(false);
                if (isHighBelow == null)
                {
                    pass = false;
                    Log($"[{item.Title}] AWARN读取失败: {FormatTemp(belowDeciC)}℃");

                    item.UpdateTriggerStatus("AWARN读取失败");

                    if (awarnCheck != null)
                        awarnCheck.UpdateMeasurement(null, "--", "FAIL", measured: true);
                    foreach (var check in item.Checks.Skip(1))
                        check.UpdateMeasurement(null, "--", "FAIL", measured: true);
                    item.ResetFirstOverTempTrigger();
                    item.UpdatePass(false);
                    EvaluateOverall();
                    return;
                }

                if (isHighBelow == true)
                {
                    pass = false;
                    item.UpdateFirstOverTempTriggerStatus("低温触发");
                    item.UpdateTriggerStatus("低温触发");
                    Log($"[{item.Title}] 低温触发: {FormatTemp(belowDeciC)}℃");

                    if (awarnCheck != null)
                        awarnCheck.UpdateMeasurement(1.0, "高电平", "FAIL", measured: true);

                    foreach (var check in item.Checks.Skip(1))
                        check.UpdateMeasurement(null, "--", "FAIL", measured: true);

                    item.UpdatePass(false);
                    EvaluateOverall();
                    return;
                }

                foreach (var t in inRangeTemps)
                {
                    token.ThrowIfCancellationRequested();
                    var isHigh = await StepAndReadAwarnOnlyAsync(item, awarnCheck, t, token, updateUi: false).ConfigureAwait(false);
                    if (isHigh == true)
                    {
                        var diAllPass = await ReadAllDiPassWithoutUiAsync(item, token).ConfigureAwait(false);
                        if (diAllPass && firstOverTempDeciC == null)
                        {
                            await MeasureAllDiAsync(item, token).ConfigureAwait(false);

                            firstOverTempDeciC = t;

                            var r = GetResistanceByTable(item.ResistanceTableType, t);
                            item.UpdateFirstOverTempTrigger(r, t);
                            Log($"[{item.Title}] 首次超温触发: {FormatTemp(t)}℃, R={FormatOhm(r)}Ω");

                            item.FreezeResistance();

                            if (awarnCheck != null)
                                awarnCheck.UpdateMeasurement(1.0, "高电平", "PASS", measured: true);

                            foreach (var check in item.Checks)
                                check.Freeze();

                            freezeChecks = true;
                            break;
                        }
                    }
                }

                if (firstOverTempDeciC == null)
                {
                    pass = false;
                    Log($"[{item.Title}] 在区间内未触发超温: {FormatTemp(minDeciC)}~{FormatTemp(maxDeciC)}℃");
                }

                var isHighAbove = await StepAndReadAwarnOnlyAsync(item, awarnCheck, aboveDeciC, token, updateUi: false).ConfigureAwait(false);
                if (isHighAbove == null)
                {
                    pass = false;
                    Log($"[{item.Title}] AWARN读取失败: {FormatTemp(aboveDeciC)}℃");
                    item.UpdateTriggerStatus("AWARN读取失败");
                }
                else if (isHighAbove != true)
                {
                    pass = false;
                    if (firstOverTempDeciC == null)
                        item.UpdateFirstOverTempTriggerStatus("超温不触发");

                    item.UpdateTriggerStatus("超温不触发");

                    Log($"[{item.Title}] 超温不触发: {FormatTemp(aboveDeciC)}℃");
                }
                else
                {
                    bool diAllPassAtAbove;
                    if (freezeChecks)
                        diAllPassAtAbove = await ReadAllDiPassWithoutUiAsync(item, token).ConfigureAwait(false);
                    else
                        diAllPassAtAbove = await MeasureAllDiAsync(item, token).ConfigureAwait(false);

                    if (!diAllPassAtAbove)
                    {
                        pass = false;
                        Log($"[{item.Title}] DI判定FAIL: {FormatTemp(aboveDeciC)}℃ 期望开路");
                    }
                }

                if (firstOverTempDeciC == null)
                {
                    foreach (var check in item.Checks.Skip(1))
                    {
                        check.UpdateMeasurement(null, "--", "FAIL", measured: true);
                    }
                }

                item.UpdatePass(pass);
                if (awarnCheck != null && !freezeChecks)
                {
                    var awarnText = isHighAbove == true ? "高电平" : (isHighAbove == false ? "低电平" : "--");
                    awarnCheck.UpdateMeasurement(isHighAbove == true ? 1.0 : 0.0, awarnText, pass ? "PASS" : "FAIL", measured: true);
                }

                EvaluateOverall();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                pass = false;
                if (awarnCheck != null)
                    awarnCheck.UpdateMeasurement(null, "异常", "FAIL", measured: true);
                item.UpdatePass(false);
                Log($"[{item.Title}] 扫描异常: {ex.GetType().Name}: {ex.Message}");
                Log($"[{item.Title}] 异常堆栈: {ex.StackTrace}");
            }
            finally
            {
                try { await DisconnectFpgaTcpAsync().ConfigureAwait(false); } catch { }
                if (awarnCheck != null && !awarnCheck.IsMeasured)
                    awarnCheck.UpdateMeasurement(null, awarnCheck.VoltageText, pass ? "PASS" : "FAIL", measured: true);
            }
        }

        private async Task<bool> ReadAllDiPassWithoutUiAsync(OverTempItemViewModel item, CancellationToken token)
        {
            try
            {
                var okReady = await EnsureDIDriverAsync(token).ConfigureAwait(false);
                if (!okReady)
                {
                    Log("7131板卡未连接");
                    return false;
                }

                await _diLock.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    foreach (var check in item.Checks.Skip(1))
                    {
                        token.ThrowIfCancellationRequested();
                        var diValue = await _diDriver.ReadChannelAsync(check.DiChannel).ConfigureAwait(false);
                        var isHigh = diValue > 0.5;
                        var pass = EvaluateDICheck(check.Evaluation, isHigh);
                        if (!pass)
                            return false;
                    }

                    return true;
                }
                finally
                {
                    _diLock.Release();
                }
            }
            catch (Exception ex)
            {
                Log($"DI测量异常: {ex.Message}");
                return false;
            }
        }

        private async Task StepResistanceOnlyAsync(OverTempItemViewModel item, int tempDeciC, CancellationToken token)
        {
            var r = GetResistanceByTable(item.ResistanceTableType, tempDeciC);
            var apiChannel = MapRoChannelTo7012Api(item.RoChannel);

            await _resistorLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                var prevBusy = IsBusy;
                IsBusy = true;
                await EnsureResistorAsync(token).ConfigureAwait(false);
                await _resistor.SetRelayStateAsync(apiChannel, pathRelayClosed: true, shortCircuitClosed: false, token).ConfigureAwait(false);
                await _resistor.SetResistanceAsync(apiChannel, r, Pxi7012OutputMode.NoWait, token).ConfigureAwait(false);
                item.UpdateResistance(r, "--", measured: true);
                Log($"[{item.Title}] 输出电阻: T={FormatTemp(tempDeciC)}℃ => R={FormatOhm(r)}Ω");

                IsBusy = prevBusy;
            }
            finally
            {
                _resistorLock.Release();
            }
        }

        private async Task<bool?> StepAndReadAwarnOnlyAsync(OverTempItemViewModel item, OverTempCheckViewModel awarnCheck, int tempDeciC, CancellationToken token, bool updateUi)
        {
            await StepResistanceOnlyAsync(item, tempDeciC, token).ConfigureAwait(false);
            await Task.Delay(200, token).ConfigureAwait(false);

            var isHigh = await ReadAwarnAsync(awarnCheck?.Pin, token).ConfigureAwait(false);
            var text = isHigh == true ? "高电平" : (isHigh == false ? "低电平" : "--");
            Log($"[{item.Title}] AWARN={text} @ {FormatTemp(tempDeciC)}℃");

            if (awarnCheck != null && updateUi)
                awarnCheck.UpdateMeasurement(isHigh == true ? 1.0 : 0.0, text, "--", measured: true);

            return isHigh;
        }

        private async Task<bool> MeasureAllDiAsync(OverTempItemViewModel item, CancellationToken token)
        {
            foreach (var check in item.Checks.Skip(1))
            {
                token.ThrowIfCancellationRequested();
                await MeasureDIAsync(check, token).ConfigureAwait(false);
                await Task.Delay(50, token).ConfigureAwait(false);
            }

            return item.Checks.Skip(1).All(c => string.Equals(c.Result, "PASS", StringComparison.OrdinalIgnoreCase));
        }

        private async Task<bool> StepAndCheckAwarnAsync(OverTempItemViewModel item, OverTempCheckViewModel awarnCheck, int tempDeciC, bool expectedHigh, CancellationToken token)
        {
            var isHigh = await StepAndReadAwarnOnlyAsync(item, awarnCheck, tempDeciC, token, updateUi: true).ConfigureAwait(false);
            if (isHigh == null)
            {
                Log($"[{item.Title}] AWARN读取失败: {FormatTemp(tempDeciC)}℃");
                return false;
            }

            var ok = isHigh.Value == expectedHigh;
            if (!ok)
            {
                var expText = expectedHigh ? "高电平" : "低电平";
                Log($"[{item.Title}] AWARN判定FAIL: {FormatTemp(tempDeciC)}℃ 期望{expText}");
            }
            return ok;
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

            OverallResult = Items.All(i => i.IsPass) ? "PASS" : "FAIL";
        }

        private async Task StopTestAsync()
        {
            try
            {
                var stack = new System.Diagnostics.StackTrace(1, true).ToString();
                Log($"StopTestAsync触发: IsManualTestRunning={IsManualTestRunning}, IsAutoTestRunning={IsAutoTestRunning}, IsBusy={IsBusy}, IsPowerOn={IsPowerOn}");
                Log($"StopTestAsync堆栈: {stack}");
            }
            catch
            {
            }
            try { _cts?.Cancel(); } catch { }

            IsManualTestRunning = false;
            IsAutoTestRunning = false;
            IsBusy = false;
            try
            {
                await _dmm.DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
            }

            try { await DisconnectFpgaTcpAsync().ConfigureAwait(false); } catch { }

            try { await CleanupPowerAsync().ConfigureAwait(false); } catch { }

            try { await ResetResistorOutputsToNominalAsync().ConfigureAwait(false); } catch { }
            try { await CleanupResistorAsync().ConfigureAwait(false); } catch { }

            if (!SkipMainPowerOff)
            {
                IsPowerOn = false;
                PowerStatus = "未供电";
            }

            RaiseCanExecuteChangedForItems();
        }

        private async Task ResetResistorOutputsToNominalAsync()
        {
            try
            {
                using var cts = new CancellationTokenSource(2000);
                var token = cts.Token;

                var okReady = await EnsureResistorAsync(token).ConfigureAwait(false);
                if (!okReady || _resistor == null || !_resistor.IsConnected)
                    return;

                await _resistorLock.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    var ro0 = MapRoChannelTo7012Api("RO0");
                    var ro1 = MapRoChannelTo7012Api("RO1");

                    await _resistor.SetRelayStateAsync(ro0, pathRelayClosed: true, shortCircuitClosed: false, token).ConfigureAwait(false);
                    await _resistor.SetRelayStateAsync(ro1, pathRelayClosed: true, shortCircuitClosed: false, token).ConfigureAwait(false);

                    await _resistor.SetResistanceAsync(ro0, 500.0, Pxi7012OutputMode.NoWait, token).ConfigureAwait(false);
                    await _resistor.SetResistanceAsync(ro1, 1000.0, Pxi7012OutputMode.NoWait, token).ConfigureAwait(false);

                    Log("结束复位: 7012输出 RO0=500Ω, RO1=1000Ω");
                }
                finally
                {
                    _resistorLock.Release();
                }
            }
            catch (Exception ex)
            {
                Log($"结束复位电阻输出失败: {ex.Message}");
            }
        }

        private void RaiseCanExecuteChangedForItems()
        {
            try
            {
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher != null && !dispatcher.CheckAccess())
                {
                    dispatcher.BeginInvoke(new Action(RaiseCanExecuteChangedForItems));
                    return;
                }
            }
            catch
            {
            }

            foreach (var item in Items)
            {
                item.ApplyResistanceCommand?.RaiseCanExecuteChanged();
                item.SweepCommand?.RaiseCanExecuteChanged();
            }
        }

        private void Log(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
            try
            {
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher != null && !dispatcher.CheckAccess())
                {
                    dispatcher.BeginInvoke(new Action(() => Logs.Add(line)));
                }
                else
                {
                    Logs.Add(line);
                }
            }
            catch
            {
            }
        }

        private async Task EnsurePowerAsync(double voltageV, CancellationToken cancellationToken)
        {
            // 192.168.1.15 CH1 不再由本测试控制上电，由总上电统一管理
            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        }

        private async Task CleanupPowerAsync()
        {
            var power = _power;
            _power = null;

            // 192.168.1.15 CH1 不再由本测试控制下电
            if (power != null)
            {
                try { await power.DisconnectAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                try { await power.DisposeAsync().ConfigureAwait(false); } catch { }
            }
        }

        private async Task<bool> EnsureResistorAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_resistor != null && _resistor.IsConnected)
                return true;

            await CleanupResistorAsync().ConfigureAwait(false);

            var candidates = new uint[] { 1, 0, 2, 3, 4, 5, 6, 7 };
            foreach (var logicalId in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var device = new ProgrammableResistorDevice
                    {
                        Name = "电阻输出",
                        Model = "PXI-7012",
                        CardName = $"电阻输出(自动探测-{logicalId})",
                        SlotIndex = (int)logicalId
                    };

                    var api = new Pxi7012Api(device, logicalId);
                    await api.ConnectAsync(cancellationToken).ConfigureAwait(false);

                    _resistor = api;
                    _connectedResistorLogicalId = logicalId;
                    Log($"7012连接成功：逻辑ID={logicalId}");
                    return true;
                }
                catch
                {
                    try
                    {
                        if (_resistor != null)
                            await _resistor.DisposeAsync().ConfigureAwait(false);
                    }
                    catch
                    {
                    }
                    _resistor = null;
                    _connectedResistorLogicalId = null;
                }
            }

            return false;
        }

        private async Task CleanupResistorAsync()
        {
            try
            {
                if (_resistor != null)
                {
                    try { await _resistor.DisposeAsync().ConfigureAwait(false); } catch { }
                }
            }
            finally
            {
                _resistor = null;
                _connectedResistorLogicalId = null;
            }
        }

        private static string MapRoChannelTo7012Api(string roChannel)
        {
            if (string.IsNullOrWhiteSpace(roChannel))
                throw new ArgumentException("RO channel is required", nameof(roChannel));

            var raw = roChannel.Trim();
            if (!raw.StartsWith("RO", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("RO channel must start with 'RO'", nameof(roChannel));

            if (!int.TryParse(raw.Substring(2), NumberStyles.Integer, CultureInfo.InvariantCulture, out var idx))
                throw new ArgumentException("Invalid RO channel index", nameof(roChannel));

            // ViewModel uses 0-based (RO0/RO1). Pxi7012Api public contract is 1-based (RO1..RO9).
            return $"RO{idx + 1}";
        }

        private async Task<bool> EnsureDIDriverAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_diDriver != null && _diDriver.IsConnected)
                return true;

            var device = FindFirst7131Device();
            if (device == null)
            {
                Log("未找到JY7131(数字量输入输出)板卡");
                return false;
            }

            _diDriver = new JY7131Driver(device, slotNumber: 0);
            var ok = await _diDriver.ConnectAsync().ConfigureAwait(false);
            if (!ok)
            {
                Log("JY7131连接失败");
                return false;
            }

            Log("JY7131连接成功");
            return true;
        }

        private async Task CleanupDIDriverAsync()
        {
            try
            {
                if (_diDriver != null)
                {
                    try { await _diDriver.DisconnectAsync().ConfigureAwait(false); } catch { }
                }
            }
            finally
            {
                _diDriver = null;
            }
        }

        private DeviceBase FindFirst7131Device()
        {
            var chassisList = _pxiChassisService?.GetAllChassis();
            if (chassisList == null)
                return null;

            foreach (var chassis in chassisList)
            {
                var device = chassis?.Devices?.FirstOrDefault(d =>
                    (d?.Model?.IndexOf("7131", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.Model?.IndexOf("JY7131", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.Name?.IndexOf("7131", StringComparison.OrdinalIgnoreCase) >= 0));

                if (device != null)
                    return device;
            }

            return null;
        }

        // (Removed) FindFirstActs6010Device: over-temperature cutoff test uses PXI-7012 for resistance output.

        public void Dispose()
        {
            try { StopTestAsync().GetAwaiter().GetResult(); } catch { }
            try { CleanupDIDriverAsync().GetAwaiter().GetResult(); } catch { }

            try { _cts?.Cancel(); } catch { }
            try { _cts?.Dispose(); } catch { }
            _cts = null;

            try { _fpgaSendLock?.Dispose(); } catch { }
            try { _resistorLock?.Dispose(); } catch { }
            try { _diLock?.Dispose(); } catch { }
            try { _sweepLock?.Dispose(); } catch { }
        }

        private static string FpgaTs()
        {
            return DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
        }

        private static string ToHex(byte[] data)
        {
            if (data == null || data.Length == 0) return "--";
            return string.Join(" ", data.Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));
        }

        private static byte[] BuildFpgaFrame(byte command, byte[] data)
        {
            var dataLen = data?.Length ?? 0;
            var lengthField = (byte)(1 + dataLen);
            var frame = new byte[2 + 1 + 1 + dataLen];
            frame[0] = FpgaFrameHeader[0];
            frame[1] = FpgaFrameHeader[1];
            frame[2] = lengthField;
            frame[3] = command;
            if (dataLen > 0)
                Buffer.BlockCopy(data, 0, frame, 4, dataLen);
            return frame;
        }

        private async Task SendFpgaFrameAsync(byte command, byte[] payload, CancellationToken token)
        {
            await EnsureFpgaTcpConnectedAsync(token).ConfigureAwait(false);
            if (_fpgaStream == null)
                throw new InvalidOperationException("FPGA未连接");

            await _fpgaSendLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                var frame = BuildFpgaFrame(command, payload);
                Log($"[{FpgaTs()}][FPGA TX] CMD=0x{command:X2} LEN={payload?.Length ?? 0} FRAME={ToHex(frame)}");
                await _fpgaStream.WriteAsync(frame, 0, frame.Length, token).ConfigureAwait(false);
                await _fpgaStream.FlushAsync(token).ConfigureAwait(false);
            }
            finally
            {
                _fpgaSendLock.Release();
            }
        }

        private async Task EnsureFpgaTcpConnectedAsync(CancellationToken token)
        {
            if (_fpgaClient?.Connected == true && _fpgaStream != null)
                return;

            await DisconnectFpgaTcpAsync().ConfigureAwait(false);

            var client = new TcpClient { NoDelay = true };
            try
            {
                using var timeoutCts = new CancellationTokenSource(2000);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCts.Token);

                var connectTask = client.ConnectAsync(FpgaServerIpAddress, FpgaServerPort);
                var cancelTask = Task.Delay(Timeout.Infinite, linkedCts.Token);
                var completed = await Task.WhenAny(connectTask, cancelTask).ConfigureAwait(false);
                if (completed != connectTask)
                {
                    try { client.Close(); } catch { }
                    token.ThrowIfCancellationRequested();
                    throw new TimeoutException($"FPGA连接超时(2s): {FpgaServerIpAddress}:{FpgaServerPort}");
                }

                await connectTask.ConfigureAwait(false);
                _fpgaClient = client;
                _fpgaStream = _fpgaClient.GetStream();

                Log($"FPGA TCP连接成功: {FpgaServerIpAddress}:{FpgaServerPort}");
            }
            catch (Exception ex)
            {
                try { client.Close(); } catch { }
                _fpgaClient = null;
                _fpgaStream = null;
                Log($"FPGA TCP连接失败: {ex.Message}");
                throw;
            }
        }

        private async Task DisconnectFpgaTcpAsync()
        {
            try { _fpgaStream?.Close(); } catch { }
            try { _fpgaClient?.Close(); } catch { }

            _fpgaStream = null;
            _fpgaClient = null;
        }

        private async Task<byte[]> ReadExactFpgaAsync(int count, int timeoutMilliseconds, CancellationToken token)
        {
            var buf = new byte[count];
            var received = 0;
            while (received < count)
            {
                var readTask = _fpgaStream.ReadAsync(buf, received, count - received, token);
                var timeoutTask = Task.Delay(timeoutMilliseconds, token);
                var completed = await Task.WhenAny(readTask, timeoutTask).ConfigureAwait(false);
                if (completed != readTask)
                {
                    token.ThrowIfCancellationRequested();
                    throw new TimeoutException($"FPGA接收超时({timeoutMilliseconds}ms)");
                }

                var n = await readTask.ConfigureAwait(false);
                if (n == 0)
                    throw new InvalidOperationException("FPGA连接已断开(读取0字节)");
                received += n;
            }
            return buf;
        }

        private async Task<(byte cmd, byte[] payload)> ReadFpgaFrameAsync(int timeoutMilliseconds, CancellationToken token)
        {
            var header = await ReadExactFpgaAsync(2, timeoutMilliseconds, token).ConfigureAwait(false);
            if (header[0] != FpgaFrameHeader[0] || header[1] != FpgaFrameHeader[1])
                throw new InvalidOperationException($"FPGA帧头校验失败: 0x{header[0]:X2} 0x{header[1]:X2}");

            var lenBuf = await ReadExactFpgaAsync(1, timeoutMilliseconds, token).ConfigureAwait(false);
            var totalLen = lenBuf[0];
            var body = await ReadExactFpgaAsync(totalLen, timeoutMilliseconds, token).ConfigureAwait(false);

            var cmd = body[0];
            var payloadLen = totalLen - 1;
            var payload = new byte[payloadLen];
            if (payloadLen > 0)
                Buffer.BlockCopy(body, 1, payload, 0, payloadLen);

            var frame = new byte[2 + 1 + body.Length];
            frame[0] = header[0];
            frame[1] = header[1];
            frame[2] = lenBuf[0];
            Buffer.BlockCopy(body, 0, frame, 3, body.Length);
            Log($"[{FpgaTs()}][FPGA RX] CMD=0x{cmd:X2} LEN={payloadLen} FRAME={ToHex(frame)}");

            return (cmd, payload);
        }

        private async Task<uint> ReadFpgaGpioInputOnceAsync(int timeoutMilliseconds, CancellationToken token, byte? acceptCmd = null)
        {
            await EnsureFpgaTcpConnectedAsync(token).ConfigureAwait(false);
            if (_fpgaStream == null)
                throw new InvalidOperationException("FPGA未连接");

            using var timeoutCts = new CancellationTokenSource(timeoutMilliseconds);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCts.Token);

            while (!linkedCts.IsCancellationRequested)
            {
                var (cmd, payload) = await ReadFpgaFrameAsync(timeoutMilliseconds, linkedCts.Token).ConfigureAwait(false);
                var cmdOk = cmd == 0x00 || (acceptCmd != null && cmd == acceptCmd.Value);
                if (cmdOk && payload != null && payload.Length >= 4)
                {
                    var v = BitConverter.ToUInt32(payload, 0);
                    LastFpgaGpioInput = v;
                    _lastFpgaGpioInputTime = DateTime.Now;

                    var hex = string.Join(" ", payload.Take(4).Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));
                    Log($"[FPGA RX] GPIO Read(IO43-64) VALUE=0x{v:X8} DATA={hex}");
                    return v;
                }
            }

            token.ThrowIfCancellationRequested();
            throw new TimeoutException($"等待FPGA数据超时({timeoutMilliseconds}ms)");
        }

        public enum OverTempEvaluation
        {
            High33,
            OpenLe16,
            DIOpen,
            DIGND
        }

        public sealed class OverTempItemViewModel : BindableBase
        {
            private readonly OverTemperatureCutoffTestViewModel _owner;

            private string _measuredResistanceText = "---";
            private string _resistanceResult = "--";
            private bool _isResistanceMeasured;
            private string _firstOverTempTemperatureText = "--";
            private double? _firstOverTempResistanceOhm;
            private int? _firstOverTempTempDeciC;
            private string _firstOverTempTriggerResult = "--";
            private bool _isPass;
            private string _triggerStatusText = "--";
            private bool _isResistanceFrozen;

            internal OverTempItemViewModel(
                OverTemperatureCutoffTestViewModel owner,
                string title,
                string resistanceLabel,
                double targetResistanceOhm,
                double resistanceToleranceOhm,
                string roChannel,
                double r0Ohm,
                int minTempDeciC,
                int maxTempDeciC,
                int stepTempDeciC,
                ResistanceTableType resistanceTableType = ResistanceTableType.PT500,
                int nominalFirstOverTempDeciC = 0,
                int firstOverTempToleranceDeciC = 0)
            {
                _owner = owner;
                Title = title;
                ResistanceLabel = resistanceLabel;
                TargetResistanceOhm = targetResistanceOhm;
                ResistanceToleranceOhm = resistanceToleranceOhm;
                RoChannel = roChannel;
                R0Ohm = r0Ohm;
                MinTempDeciC = minTempDeciC;
                MaxTempDeciC = maxTempDeciC;
                StepTempDeciC = stepTempDeciC;
                ResistanceTableType = resistanceTableType;
                NominalFirstOverTempDeciC = nominalFirstOverTempDeciC;
                FirstOverTempToleranceDeciC = firstOverTempToleranceDeciC;

                ApplyResistanceCommand = new DelegateCommand(async () => await _owner.ApplyResistanceAsync(this), () => _owner.CanApplyResistance(this));
                SweepCommand = new DelegateCommand(async () => await _owner.SweepItemAsync(this), () => _owner.CanSweepItem(this));
            }

            public string Title { get; }

            public string ResistanceLabel { get; }

            public double TargetResistanceOhm { get; }

            public double ResistanceToleranceOhm { get; }

            public string RoChannel { get; set; }

            public double R0Ohm { get; }

            public int MinTempDeciC { get; }

            public int MaxTempDeciC { get; }

            public int StepTempDeciC { get; }

            public ResistanceTableType ResistanceTableType { get; }

            public int NominalFirstOverTempDeciC { get; }

            public int FirstOverTempToleranceDeciC { get; }

            public ObservableCollection<OverTempCheckViewModel> Checks { get; } = new ObservableCollection<OverTempCheckViewModel>();

            public string MeasuredResistanceText
            {
                get => _measuredResistanceText;
                private set => SetProperty(ref _measuredResistanceText, value);
            }

            public string ResistanceResult
            {
                get => _resistanceResult;
                private set => SetProperty(ref _resistanceResult, value);
            }

            public bool IsResistanceMeasured
            {
                get => _isResistanceMeasured;
                private set => SetProperty(ref _isResistanceMeasured, value);
            }

            public string FirstOverTempTemperatureText
            {
                get => _firstOverTempTemperatureText;
                private set => SetProperty(ref _firstOverTempTemperatureText, value);
            }

            public string FirstOverTempTriggerDisplayText => FirstOverTempTemperatureText;

            public double? FirstOverTempResistanceOhm
            {
                get => _firstOverTempResistanceOhm;
                private set => SetProperty(ref _firstOverTempResistanceOhm, value);
            }

            public int? FirstOverTempTempDeciC
            {
                get => _firstOverTempTempDeciC;
                private set => SetProperty(ref _firstOverTempTempDeciC, value);
            }

            public string FirstOverTempTriggerResult
            {
                get => _firstOverTempTriggerResult;
                private set => SetProperty(ref _firstOverTempTriggerResult, value);
            }

            public string FirstOverTempReportText => FirstOverTempTriggerDisplayText;

            public string TriggerStatusText
            {
                get => _triggerStatusText;
                private set => SetProperty(ref _triggerStatusText, value);
            }

            public string TriggerStatusDisplayText
            {
                get
                {
                    if (!IsMeasured)
                        return "--";

                    if (string.Equals(TriggerStatusText, "低温触发", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(TriggerStatusText, "超温不触发", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(TriggerStatusText, "AWARN读取失败", StringComparison.OrdinalIgnoreCase))
                    {
                        return TriggerStatusText;
                    }

                    return IsPass ? "正常" : "--";
                }
            }

            public bool IsMeasured => IsResistanceMeasured && Checks.All(c => c.IsMeasured);

            public bool IsPass
            {
                get => _isPass;
                private set => SetProperty(ref _isPass, value);
            }

            public DelegateCommand ApplyResistanceCommand { get; }

            public DelegateCommand SweepCommand { get; }

            internal void UpdateResistance(double? valueOhm, string result, bool measured)
            {
                if (_isResistanceFrozen && measured)
                    return;

                MeasuredResistanceText = valueOhm == null
                    ? "---"
                    : valueOhm.Value.ToString("0.###", CultureInfo.InvariantCulture);

                ResistanceResult = result;
                IsResistanceMeasured = measured;
            }

            internal void FreezeResistance()
            {
                _isResistanceFrozen = true;
            }

            internal void UnfreezeResistance()
            {
                _isResistanceFrozen = false;
            }

            internal void ResetFirstOverTempTrigger()
            {
                FirstOverTempResistanceOhm = null;
                FirstOverTempTempDeciC = null;
                FirstOverTempTemperatureText = "--";
                FirstOverTempTriggerResult = "--";
                RaisePropertyChanged(nameof(FirstOverTempTriggerDisplayText));
                RaisePropertyChanged(nameof(FirstOverTempReportText));
            }

            internal void UpdateFirstOverTempTrigger(double resistanceOhm, int tempDeciC)
            {
                FirstOverTempResistanceOhm = resistanceOhm;
                FirstOverTempTempDeciC = tempDeciC;
                FirstOverTempTemperatureText = $"{FormatOhm(resistanceOhm)}Ω/{FormatTemp(tempDeciC)}℃";

                var expectedR = OverTemperatureCutoffTestViewModel.GetResistanceByTable(ResistanceTableType, NominalFirstOverTempDeciC);
                var tempOk = FirstOverTempToleranceDeciC > 0 && Math.Abs(tempDeciC - NominalFirstOverTempDeciC) <= FirstOverTempToleranceDeciC;
                var rOk = Math.Abs(resistanceOhm - expectedR) <= ResistanceToleranceOhm;
                FirstOverTempTriggerResult = (tempOk && rOk) ? "PASS" : "FAIL";

                RaisePropertyChanged(nameof(FirstOverTempTriggerDisplayText));
                RaisePropertyChanged(nameof(FirstOverTempReportText));
            }

            internal void UpdateFirstOverTempTriggerStatus(string statusText)
            {
                ResetFirstOverTempTrigger();
                FirstOverTempTemperatureText = string.IsNullOrWhiteSpace(statusText) ? "--" : statusText;
                FirstOverTempTriggerResult = "FAIL";
                RaisePropertyChanged(nameof(FirstOverTempTriggerDisplayText));
                RaisePropertyChanged(nameof(FirstOverTempReportText));
            }

            internal void UpdateFirstOverTempTemperature(double? tempC)
            {
                FirstOverTempTemperatureText = tempC == null
                    ? "--"
                    : tempC.Value.ToString("0.0", CultureInfo.InvariantCulture);
            }

            internal void UpdateFirstOverTempTemperature(string text)
            {
                FirstOverTempTemperatureText = string.IsNullOrWhiteSpace(text) ? "--" : text;
            }

            internal void UpdatePass(bool pass)
            {
                IsPass = pass;
                RaisePropertyChanged(nameof(TriggerStatusDisplayText));
            }

            internal void UpdateTriggerStatus(string text)
            {
                if (string.IsNullOrWhiteSpace(text))
                {
                    TriggerStatusText = "--";
                    RaisePropertyChanged(nameof(TriggerStatusDisplayText));
                    return;
                }

                TriggerStatusText = text;
                RaisePropertyChanged(nameof(TriggerStatusDisplayText));
            }
        }

        public sealed class OverTempCheckViewModel : BindableBase
        {
            private readonly OverTemperatureCutoffTestViewModel _owner;
            private readonly OverTempItemViewModel _item;

            private string _voltageText = "---";
            private string _result = "--";
            private bool _isMeasured;
            private bool _isFrozen;

            internal OverTempCheckViewModel(OverTemperatureCutoffTestViewModel owner, OverTempItemViewModel item, string pin, string pinName, string expected, OverTempEvaluation evaluation, string diChannel = null)
            {
                _owner = owner;
                _item = item;
                Pin = pin;
                PinName = pinName;
                Expected = expected;
                Evaluation = evaluation;
                DiChannel = diChannel;
            }

            public string Pin { get; }

            public string PinName { get; }

            public string Expected { get; }

            public OverTempEvaluation Evaluation { get; }

            public string DiChannel { get; }

            public string VoltageText
            {
                get => _voltageText;
                private set => SetProperty(ref _voltageText, value);
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

            internal void UpdateMeasurement(double? valueVolt, string valueText, string result, bool measured)
            {
                if (_isFrozen && measured)
                    return;

                _ = valueVolt;
                VoltageText = valueText;
                Result = result;
                IsMeasured = measured;
            }

            internal void Freeze()
            {
                _isFrozen = true;
            }

            internal void Unfreeze()
            {
                _isFrozen = false;
            }
        }
    }
}
