using Prism.Commands;
using Prism.Ioc;
using Prism.Mvvm;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MeasureControl.Models.Devices;
using MeasureControl.Models.Devices.DeviceCategories;
using MeasureControl.Services;
using MeasureControl.Services.HardwareApis;
using System.Windows;

namespace MeasureControl.ViewModels.SingleBoardTest.InertController
{
    public sealed class PressureSensorSignalAcquisitionTestViewModel : BindableBase, IDisposable
    {
        private const string PowerSupplyIpAddress = "192.168.1.15";
        private const double InputVoltageV = 28.0;
        private const double InputCurrentA = 3.0;

        public bool SkipMainPowerOff { get; set; }

        private readonly ISingleBoardTestContextService _singleBoardTestContext;
        private readonly SynchronizationContext _uiContext;
        private readonly Prism.Events.IEventAggregator _eventAggregator;
        private readonly IComponentPowerStateApi _componentPowerStateApi;

        private readonly SemaphoreSlim _opLock = new SemaphoreSlim(1, 1);
        private CancellationTokenSource _cts;

        private bool _isManualTestRunning;
        private bool _isAutoTestRunning;
        private string _lastTestTime = "--";
        private string _lastTestResult = "--";

        private IPowerSupplyApi _power;
        private bool _isPowerOn;
        private string _powerStatus = "未供电";

        private IMtx532Api _mtx532;
        private IArt4229Api _arinc;
        private Task _arincRxLoopTask;
        private int? _connectedSlot;
        private string _connectionText = "532: 未连接 | 4229: 未连接";
        private bool _atpTxOpened;
        private bool _atpModeEntered;
        // private bool _arincTxOpened;
        // private bool _isSimulateEnabled;

        private readonly object _arincTelemetryLock = new object();
        private readonly List<double> _voltageTelemetry = new List<double>(64);
        private readonly List<double> _pressureTelemetry = new List<double>(64);

        private string _realtimeVoltageText = "--";
        private string _realtimePressureText = "--";

        private string _manualOutputVoltageText = "";

        public PressureSensorSignalAcquisitionTestViewModel(
            ISingleBoardTestContextService singleBoardTestContext,
            Prism.Events.IEventAggregator eventAggregator,
            IComponentPowerStateApi componentPowerStateApi = null)
        {
            _singleBoardTestContext = singleBoardTestContext;
            _uiContext = SynchronizationContext.Current;

            _eventAggregator = eventAggregator;
            _componentPowerStateApi = componentPowerStateApi;

            ManualTestCommand = new DelegateCommand(async () => await OnManualTestAsync());
            AutoTestCommand = new DelegateCommand(async () => await OnAutoTestAsync());
            ClearLogCommand = new DelegateCommand(() => Logs.Clear());
            // ToggleSimulateCommand = new DelegateCommand(() => IsSimulateEnabled = !IsSimulateEnabled);

            Items = new ObservableCollection<PressurePointItemViewModel>
            {
                new PressurePointItemViewModel("1)", "(0.52±0.0425)V", targetVoltageV: 0.52, voltageToleranceV: 0.0425, expectedPressurePsi: 0.0, pressureTolerancePsi: 0.45, this),
                new PressurePointItemViewModel("2)", "(5.62±0.0425)V", targetVoltageV: 5.62, voltageToleranceV: 0.0425, expectedPressurePsi: 54.0, pressureTolerancePsi: 0.45, this),
                new PressurePointItemViewModel("3)", "(9.02±0.0425)V", targetVoltageV: 9.02, voltageToleranceV: 0.0425, expectedPressurePsi: 90.0, pressureTolerancePsi: 0.45, this),
            };
        }

        private void PostToUi(Action action)
        {
            if (action == null)
                return;

            if (_uiContext != null && !ReferenceEquals(SynchronizationContext.Current, _uiContext))
            {
                _uiContext.Post(_ =>
                {
                    try { action(); } catch { }
                }, null);
                return;
            }

            action();
        }

        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();

        public ObservableCollection<PressurePointItemViewModel> Items { get; }

        public DelegateCommand ManualTestCommand { get; }
        public DelegateCommand AutoTestCommand { get; }
        public DelegateCommand ClearLogCommand { get; }
        // public DelegateCommand ToggleSimulateCommand { get; }

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

        // public bool IsSimulateEnabled
        // {
        //     get => _isSimulateEnabled;
        //     set
        //     {
        //         if (SetProperty(ref _isSimulateEnabled, value))
        //         {
        //             Log(value ? "已启用模拟信号：采集前将由4229通道2发送一帧模拟数据" : "已关闭模拟信号：采集将只读取实际接收数据");
        //         }
        //     }
        // }

        public string ConnectionText
        {
            get => _connectionText;
            private set => SetProperty(ref _connectionText, value);
        }

        public string RealtimeVoltageText
        {
            get => _realtimeVoltageText;
            private set => SetProperty(ref _realtimeVoltageText, value);
        }

        public string RealtimePressureText
        {
            get => _realtimePressureText;
            private set => SetProperty(ref _realtimePressureText, value);
        }

        public string ManualOutputVoltageText
        {
            get => _manualOutputVoltageText;
            set
            {
                var next = value ?? string.Empty;
                if (string.IsNullOrWhiteSpace(next))
                {
                    SetProperty(ref _manualOutputVoltageText, string.Empty);
                    return;
                }

                var raw = next.Trim();
                if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ||
                    double.TryParse(raw, NumberStyles.Float, CultureInfo.CurrentCulture, out v))
                {
                    if (v >= 0.0 && v <= 10.0)
                        SetProperty(ref _manualOutputVoltageText, raw);
                }
            }
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

        private void UpdateConnectionText()
        {
            var mtx = _mtx532 != null && _mtx532.IsConnected
                ? $"532: 已连接(SLOT={_connectedSlot})"
                : "532: 未连接";

            var a = _arinc != null && _arinc.IsConnected
                ? "4229: 已连接"
                : "4229: 未连接";

            ConnectionText = $"{mtx} | {a}";
        }

        private async Task OnManualTestAsync()
        {
            if (IsManualTestRunning)
            {
                await StopAsync().ConfigureAwait(false);
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
            {
                await StopAsync().ConfigureAwait(false);
            }

            var lockTaken = false;
            await _opLock.WaitAsync().ConfigureAwait(false);
            lockTaken = true;
            try
            {
                _cts?.Cancel();
                _cts?.Dispose();
                _cts = new CancellationTokenSource();

                IsManualTestRunning = true;
                IsAutoTestRunning = false;

                PublishNavigationLock(isLocked: true, source: "PressureSensor");
                LastTestTime = "--";
                LastTestResult = "--";

                PostToUi(() =>
                {
                    foreach (var item in Items)
                        item.Reset();
                });

                Log("开始手动测试（压力传感器信号采集）：准备连接532模拟量输出板卡 + 4229(ARINC429)通讯采集");
                await EnsurePowerAsync(_cts.Token).ConfigureAwait(false);
                var ok532 = await EnsureMtx532ReadyAsync(_cts.Token).ConfigureAwait(false);
                var ok429 = await EnsureArincRxReadyAsync(_cts.Token).ConfigureAwait(false);
                UpdateConnectionText();

                if (!ok532)
                    Log("532连接失败：请检查板卡/驱动/机箱配置");
                if (!ok429)
                    Log("4229连接失败：请检查板卡/驱动/机箱配置");

                if (!ok532 || !ok429)
                {
                    await StopAsync().ConfigureAwait(false);
                    return;
                }

                StartArincRxLoopIfNeeded(_cts.Token);

                Log("已就绪：AO2输出模拟电压(低端接地)，通过ARINC429采集压力并按表7-6判定");
            }
            catch (OperationCanceledException)
            {
                await StopAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log($"手动测试初始化异常：{ex.Message}");
                await StopAsync().ConfigureAwait(false);
            }
            finally
            {
                _opLock.Release();
            }
        }

        private static int? TryParseSlotNumber(DeviceBase dev)
        {
            if (dev == null)
                return null;

            var s = dev.SlotPosition;
            if (string.IsNullOrWhiteSpace(s))
                return null;

            // 常见格式："SLOT=7" / "Slot 7" / "7" 等
            var digits = new string(s.Where(char.IsDigit).ToArray());
            if (int.TryParse(digits, out var n))
                return n;

            return null;
        }

        public async Task<string> RunOnceAsync(CancellationToken cancellationToken)
        {
            if (IsAutoTestRunning || IsManualTestRunning)
            {
                return LastTestResult;
            }

            var lockTaken = false;
            await _opLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            lockTaken = true;
            try
            {
                _cts?.Cancel();
                _cts?.Dispose();
                _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

                IsAutoTestRunning = true;
                IsManualTestRunning = false;

                PublishNavigationLock(isLocked: true, source: "PressureSensor");
                LastTestTime = "--";
                LastTestResult = "--";

                PostToUi(() =>
                {
                    foreach (var item in Items)
                        item.Reset();
                });

                Log("开始自动测试：将依次执行三档电压输出与压力采集判定");

                await EnsurePowerAsync(_cts.Token).ConfigureAwait(false);
                var ok532 = await EnsureMtx532ReadyAsync(_cts.Token).ConfigureAwait(false);
                var ok429 = await EnsureArincRxReadyAsync(_cts.Token).ConfigureAwait(false);
                UpdateConnectionText();
                if (!ok532 || !ok429)
                {
                    LastTestResult = "FAIL";
                    LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    Log("板卡连接失败：自动测试终止");
                    return "FAIL";
                }

                StartArincRxLoopIfNeeded(_cts.Token);

                _opLock.Release();
                lockTaken = false;

                foreach (var item in Items)
                {
                    _cts.Token.ThrowIfCancellationRequested();
                    await item.MeasureAsync().ConfigureAwait(false);
                    await Task.Delay(80, _cts.Token).ConfigureAwait(false);
                }

                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                LastTestResult = Items.All(x => string.Equals(x.Result, "PASS", StringComparison.OrdinalIgnoreCase)) ? "PASS" : "FAIL";
                Log($"自动测试结束：汇总结果={LastTestResult}");
                return LastTestResult;
            }
            catch (OperationCanceledException)
            {
                Log("自动测试已取消");
                return "FAIL";
            }
            catch (Exception ex)
            {
                LastTestResult = "FAIL";
                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                Log($"自动测试异常：{ex.Message}");
                return "FAIL";
            }
            finally
            {
                if (lockTaken)
                {
                    _opLock.Release();
                    lockTaken = false;
                }

                try { await StopAsync().ConfigureAwait(false); } catch { }
            }
        }

        private async Task OnAutoTestAsync()
        {
            if (IsAutoTestRunning)
            {
                await StopAsync().ConfigureAwait(false);
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
            {
                await StopAsync().ConfigureAwait(false);
            }

            var lockTaken = false;
            await _opLock.WaitAsync().ConfigureAwait(false);
            lockTaken = true;
            try
            {
                _cts?.Cancel();
                _cts?.Dispose();
                _cts = new CancellationTokenSource();

                IsAutoTestRunning = true;
                IsManualTestRunning = false;

                PublishNavigationLock(isLocked: true, source: "PressureSensor");
                LastTestTime = "--";
                LastTestResult = "--";

                PostToUi(() =>
                {
                    foreach (var item in Items)
                        item.Reset();
                });

                Log("开始自动测试：将依次执行三档电压输出与压力采集判定");

                await EnsurePowerAsync(_cts.Token).ConfigureAwait(false);
                var ok532 = await EnsureMtx532ReadyAsync(_cts.Token).ConfigureAwait(false);
                var ok429 = await EnsureArincRxReadyAsync(_cts.Token).ConfigureAwait(false);
                UpdateConnectionText();
                if (!ok532 || !ok429)
                {
                    LastTestResult = "FAIL";
                    LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    Log("板卡连接失败：自动测试终止");
                    return;
                }

                StartArincRxLoopIfNeeded(_cts.Token);

                // 重要：此处必须释放 _opLock
                // 否则后续 item.MeasureAsync -> MeasurePointAsync 会再次等待 _opLock，导致不可重入死锁（自动测试卡住）
                _opLock.Release();
                lockTaken = false;

                foreach (var item in Items)
                {
                    _cts.Token.ThrowIfCancellationRequested();
                    await item.MeasureAsync().ConfigureAwait(false);
                    await Task.Delay(80, _cts.Token).ConfigureAwait(false);
                }

                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                LastTestResult = Items.All(x => string.Equals(x.Result, "PASS", StringComparison.OrdinalIgnoreCase)) ? "PASS" : "FAIL";
                Log($"自动测试结束：汇总结果={LastTestResult}");
            }
            catch (OperationCanceledException)
            {
                Log("自动测试已取消");
            }
            catch (Exception ex)
            {
                LastTestResult = "FAIL";
                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                Log($"自动测试异常：{ex.Message}");
            }
            finally
            {
                if (lockTaken)
                {
                    _opLock.Release();
                    lockTaken = false;
                }

                try { await StopAsync().ConfigureAwait(false); } catch { }
            }
        }

        private async Task StopAsync()
        {
            await _opLock.WaitAsync().ConfigureAwait(false);
            try
            {
                try { _cts?.Cancel(); } catch { }

                await DisablePowerAsync(CancellationToken.None).ConfigureAwait(false);

                if (_arinc != null)
                {
                    await CloseArincAsync().ConfigureAwait(false);
                }

                if (_mtx532 != null)
                {
                    try { await _mtx532.StopOutputAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _mtx532.ResetAllToZeroAsync(disableAfterReset: true, cancellationToken: CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _mtx532.DisconnectAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _mtx532.DisposeAsync().ConfigureAwait(false); } catch { }
                    _mtx532 = null;
                }

                if (_power != null)
                {
                    try { await _power.DisconnectAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _power.DisposeAsync().ConfigureAwait(false); } catch { }
                    _power = null;
                }

                _connectedSlot = null;
                UpdateConnectionText();

                ResetArincTelemetryCache();
                RealtimeVoltageText = "--";
                RealtimePressureText = "--";

                IsManualTestRunning = false;
                IsAutoTestRunning = false;

                PublishNavigationLock(isLocked: false, source: "PressureSensor");

                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            }
            finally
            {
                _opLock.Release();
            }
        }



        private void PublishNavigationLock(bool isLocked, string source)

        {

            try

            {

                _eventAggregator?.GetEvent<MeasureControl.Events.NavigationLockChangedEvent>()

                    ?.Publish(new MeasureControl.Events.NavigationLockChangedEventArgs { IsLocked = isLocked, Source = source });

            }

            catch

            {

            }

        }



        private async Task CloseArincAsync()

        {

            if (_arinc == null)

                return;

            try { await _arinc.StopRxAsync(ArincRxChannelIndex, CancellationToken.None).ConfigureAwait(false); } catch { }

            try { await _arinc.CloseRxAsync(ArincRxChannelIndex, CancellationToken.None).ConfigureAwait(false); } catch { }

            if (_atpTxOpened)

            {

                try { await _arinc.CloseTxAsync(ArincTxChannelIndex, CancellationToken.None).ConfigureAwait(false); } catch { }

            }

            try { await _arinc.DisconnectAsync(CancellationToken.None).ConfigureAwait(false); } catch { }

            try { await _arinc.DisposeAsync().ConfigureAwait(false); } catch { }

            _arinc = null;

            _arincRxLoopTask = null;

            _atpTxOpened = false;

            _atpModeEntered = false;

        }

        private async Task EnsurePowerAsync(CancellationToken token)
        {
            // 192.168.1.15 CH1 不再由本测试控制上电，由总上电统一管理
            await Task.Delay(100, token).ConfigureAwait(false);

            PostToUi(() =>
            {
                IsPowerOn = true;
                PowerStatus = $"已供电(CH1 {InputVoltageV:0.###}V)";
            });
        }

        private async Task DisablePowerAsync(CancellationToken token)
        {
            try
            {
                if (_power == null)
                    return;

                if (!_power.IsConnected)
                    await _power.ConnectAsync(PowerSupplyIpAddress, token).ConfigureAwait(false);

                // 192.168.1.15 CH1 不再由本测试控制下电
                foreach (var ch in Enum.GetValues(typeof(PowerSupplyChannel)).Cast<PowerSupplyChannel>())
                {
                    if (ch == PowerSupplyChannel.CH1)
                        continue;
                    try { await _power.SetOutputEnabledAsync(ch, false, token).ConfigureAwait(false); } catch { }
                }
                await Task.Delay(200, token).ConfigureAwait(false);
            }
            catch
            {
            }
            finally
            {
                PostToUi(() =>
                {
                    if (!SkipMainPowerOff)
                    {
                        IsPowerOn = false;
                        PowerStatus = "未供电";
                    }
                });
            }
        }

        private const int MeasureTimeoutMs = 1200;
        private const int SamplesPerMeasure = 5;
        private const int AoSettleDelayMs = 80;
        private const int AfterClickCollectDelayMs = 1000;

        private const int ArincRxChannelIndex = 0;
        private const int ArincTxChannelIndex = 1;
        private const double ArincRate = 100000.0;
        private const int ArincPollIntervalMs = 10;
        private const byte ArincExpectedSdi = 1;

        // 注意：此处Label为“已按需求做过bit反转后的接收端Label(十进制)”，不再进行二次反转。
        // 文档：电压Label(oct)=155 -> 182；压力Label(oct)=156 -> 118
        private const byte VoltageLabelRxDec = 182;
        private const byte PressureLabelRxDec = 118;

        private const uint AtpSsmDataSdi = 0xC10001u;
        private const byte AtpLabelOctal174Dec = 124;

        private const int SignedMagnitudeBitCount = 18;
        private const int SignBitIndexInData19 = 18;
        private const uint MagnitudeMask = (1u << SignedMagnitudeBitCount) - 1u;

        private const double VoltageResolution = 0.0001;
        private const double PressureResolutionPsi = 0.0005;

        private const byte ArincSsmNormal = 2;

        private static double DecodeSignedMagnitude(uint data19, double resolution)
        {
            var sign = ((data19 >> SignBitIndexInData19) & 0x1u) != 0;
            var magnitude = data19 & MagnitudeMask;
            var value = magnitude * resolution;
            return sign ? -value : value;
        }

        private bool IsExpectedLabel(byte labelRaw, byte expectedLabelRxDec)
            => labelRaw == expectedLabelRxDec;

        private void ResetArincTelemetryCache()
        {
            lock (_arincTelemetryLock)
            {
                _voltageTelemetry.Clear();
                _pressureTelemetry.Clear();
            }
        }

        private void StartArincRxLoopIfNeeded(CancellationToken token)
        {
            if (_arinc == null || !_arinc.IsConnected)
                return;

            if (_arincRxLoopTask != null && !_arincRxLoopTask.IsCompleted)
                return;

            _arincRxLoopTask = Task.Run(() => ArincRxLoopAsync(token), token);
        }

        private async Task ArincRxLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (_arinc == null || !_arinc.IsConnected)
                    {
                        await Task.Delay(ArincPollIntervalMs, token).ConfigureAwait(false);
                        continue;
                    }

                    var words = await _arinc.ReadRxWordsAsync(ArincRxChannelIndex, maxCount: 128, enableTimeTag: false, enableRateAdaption: false, cancellationToken: token).ConfigureAwait(false);
                    if (words.Count > 0)
                    {
                        for (int i = words.Count - 1; i >= 0; i--)
                        {
                            ParseAndCacheTelemetry(words[i].Data429);
                        }
                    }

                    await Task.Delay(ArincPollIntervalMs, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    try { await Task.Delay(100, token).ConfigureAwait(false); } catch { break; }
                }
            }
        }

        private void ParseAndCacheTelemetry(uint rawWord)
        {
            if (_arinc == null)
                return;

            _arinc.ParseRawWord(rawWord, out var labelRaw, out var sdi, out var data19, out var ssm);

           
            var isV = IsExpectedLabel(labelRaw, VoltageLabelRxDec);
            var isP = IsExpectedLabel(labelRaw, PressureLabelRxDec);
            if (labelRaw==118)
                Console.WriteLine("labelRaw:" + labelRaw);
            if (!isV && !isP)
                return;

            if (sdi != ArincExpectedSdi)
                return;

            if (!_arinc.VerifyOddParity(rawWord))
                return;

            //if (ssm != ArincSsmNormal)
            //    return;

            double? lastV = null;
            double? lastP = null;
            lock (_arincTelemetryLock)
            {
                const int maxKeep = 64;
                if (isV)
                {
                    lastV = DecodeSignedMagnitude(data19, VoltageResolution);
                    _voltageTelemetry.Add(lastV.Value);
                    if (_voltageTelemetry.Count > maxKeep)
                        _voltageTelemetry.RemoveRange(0, _voltageTelemetry.Count - maxKeep);
                }

                if (isP)
                {
                    lastP = DecodeSignedMagnitude(data19, PressureResolutionPsi);
                    _pressureTelemetry.Add(lastP.Value);
                    if (_pressureTelemetry.Count > maxKeep)
                        _pressureTelemetry.RemoveRange(0, _pressureTelemetry.Count - maxKeep);
                }
            }

            if (lastV.HasValue)
            {
                var v = lastV.Value;
                if (labelRaw == 118)
                    Console.WriteLine("电压labelRaw:" + v);
                PostToUi(() =>
                {
                    RealtimeVoltageText = v.ToString("F4", CultureInfo.InvariantCulture);
                    if (IsManualTestRunning || IsAutoTestRunning)
                    {
                        foreach (var item in Items)
                            item.SetRealtimeVoltage(v);
                    }
                });
            }

            if (lastP.HasValue)
            {
                var p = lastP.Value;
                if (labelRaw == 118)
                    Console.WriteLine("压力labelRaw:" + p);
                PostToUi(() =>
                {
                    RealtimePressureText = p.ToString("F3", CultureInfo.InvariantCulture);
                    if (IsManualTestRunning || IsAutoTestRunning)
                    {
                        foreach (var item in Items)
                            item.SetRealtimePressure(p);
                    }
                });
            }
        }

        private async Task<(double? VoltageV, double? PressurePsi)> WaitVoltageAndPressureFromCacheAsync(CancellationToken token)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(MeasureTimeoutMs);
            while (!token.IsCancellationRequested && DateTime.UtcNow <= deadline)
            {
                double[] v;
                double[] p;
                lock (_arincTelemetryLock)
                {
                    v = _voltageTelemetry.Count >= SamplesPerMeasure
                        ? _voltageTelemetry.Skip(_voltageTelemetry.Count - SamplesPerMeasure).Take(SamplesPerMeasure).ToArray()
                        : null;

                    p = _pressureTelemetry.Count >= SamplesPerMeasure
                        ? _pressureTelemetry.Skip(_pressureTelemetry.Count - SamplesPerMeasure).Take(SamplesPerMeasure).ToArray()
                        : null;
                }

                if (v != null && p != null)
                    return (v.Average(), p.Average());

                await Task.Delay(ArincPollIntervalMs, token).ConfigureAwait(false);
            }

            lock (_arincTelemetryLock)
            {
                return (
                    _voltageTelemetry.Count > 0 ? _voltageTelemetry.Average() : (double?)null,
                    _pressureTelemetry.Count > 0 ? _pressureTelemetry.Average() : (double?)null);
            }
        }

        internal async Task<(double? MeasuredVoltageV, double? MeasuredPressurePsi, string Result)> MeasurePointAsync(double targetVoltageV, double voltageToleranceV, double expectedPressurePsi, double pressureTolerancePsi)
        {
            if (!IsManualTestRunning && !IsAutoTestRunning)
            {
                Log("请先启动手动测试，再进行采集判定。");
                return (null, null, "--");
            }

            await _opLock.WaitAsync().ConfigureAwait(false);
            try
            {
                var token = _cts?.Token ?? CancellationToken.None;

                var ok532 = await EnsureMtx532ReadyAsync(token).ConfigureAwait(false);
                var ok429 = await EnsureArincRxReadyAsync(token).ConfigureAwait(false);
                UpdateConnectionText();
                if (!ok532 || !ok429)
                {
                    Log("板卡未就绪：无法输出/采集");
                    return (null, null, "FAIL");
                }

                // 产品实物测试：禁用模拟发送（仅保留接收）
                // if (IsSimulateEnabled)
                // {
                //     var okTx = await EnsureArincTxReadyAsync(token).ConfigureAwait(false);
                //     if (okTx)
                //     {
                //         await SendSimulatedPressureAndVoltageWordsOnceAsync(expectedPressurePsi, targetVoltageV, token).ConfigureAwait(false);
                //         await Task.Delay(40, token).ConfigureAwait(false);
                //     }
                //     else
                //     {
                //         Log("模拟信号发送未就绪：4229发送通道未打开");
                //     }
                // }

                // 使用持续输出模式，避免一次性写入仅输出很短时间导致万用表难以稳定测到
                if (!_mtx532.IsOutputRunning)
                {
                    try { await _mtx532.StartOutputAsync(token).ConfigureAwait(false); } catch { }
                }

                var outputVoltageV = targetVoltageV;
                if (!string.IsNullOrWhiteSpace(ManualOutputVoltageText))
                {
                    var raw = ManualOutputVoltageText.Trim();
                    if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var vInv) ||
                        double.TryParse(raw, NumberStyles.Float, CultureInfo.CurrentCulture, out vInv))
                    {
                        outputVoltageV = vInv;
                    }
                }

                ResetArincTelemetryCache();
                await _mtx532.SetDcAsync("AO2", outputVoltageV, enable: true, cancellationToken: token).ConfigureAwait(false);

                await Task.Delay(AfterClickCollectDelayMs, token).ConfigureAwait(false);

                ResetArincTelemetryCache();

                await Task.Delay(AoSettleDelayMs, token).ConfigureAwait(false);

                var (measuredV, measuredP) = await WaitVoltageAndPressureFromCacheAsync(token).ConfigureAwait(false);
                if (!measuredV.HasValue || !measuredP.HasValue)
                {
                    Log($"采集超时：未同时获取到电压与压力数据（电压Label={VoltageLabelRxDec} 压力Label={PressureLabelRxDec} SDI={ArincExpectedSdi}）");
                    return (measuredV, measuredP, "FAIL");
                }

                var vMin = outputVoltageV - voltageToleranceV;
                var vMax = outputVoltageV + voltageToleranceV;
                var vPass = measuredV.Value >= vMin && measuredV.Value <= vMax;

                var pMin = expectedPressurePsi - pressureTolerancePsi;
                var pMax = expectedPressurePsi + pressureTolerancePsi;
                var pPass = measuredP.Value >= pMin && measuredP.Value <= pMax;

                var pass = vPass && pPass;

                Log($"采集结果：AO2={outputVoltageV.ToString("F3", CultureInfo.InvariantCulture)}V 电压={measuredV.Value.ToString("F4", CultureInfo.InvariantCulture)}(期望{outputVoltageV.ToString("F4", CultureInfo.InvariantCulture)}±{voltageToleranceV.ToString("F4", CultureInfo.InvariantCulture)}) 压力={measuredP.Value.ToString("F3", CultureInfo.InvariantCulture)}psi(期望{expectedPressurePsi.ToString("F3", CultureInfo.InvariantCulture)}±{pressureTolerancePsi.ToString("F3", CultureInfo.InvariantCulture)}) -> {(pass ? "PASS" : "FAIL")}");
                return (measuredV, measuredP, pass ? "PASS" : "FAIL");
            }
            catch (OperationCanceledException)
            {
                return (null, null, "--");
            }
            catch (Exception ex)
            {
                Log($"采集异常：{ex.Message}");
                return (null, null, "FAIL");
            }
            finally
            {
                _opLock.Release();
            }
        }

        private async Task<bool> EnsureMtx532ReadyAsync(CancellationToken token)
        {
            if (_mtx532 != null && _mtx532.IsConnected)
                return true;

            DeviceBase dev = null;
            try
            {
                var pxiService = ContainerLocator.Container.Resolve<IPxiChassisService>();
                var chassisList = pxiService?.GetAllChassis();
                if (chassisList != null)
                {
                    foreach (var chassis in chassisList)
                    {
                        if (chassis?.Devices == null) continue;
                        dev = chassis.Devices.FirstOrDefault(d =>
                            (d?.Model?.IndexOf("532", StringComparison.OrdinalIgnoreCase) >= 0) ||
                            (d?.Name?.IndexOf("532", StringComparison.OrdinalIgnoreCase) >= 0) ||
                            (d?.Model?.IndexOf("MTX", StringComparison.OrdinalIgnoreCase) >= 0) ||
                            (d?.Name?.IndexOf("MTX", StringComparison.OrdinalIgnoreCase) >= 0));
                        if (dev != null)
                            break;
                    }
                }
            }
            catch
            {
            }

            if (dev == null)
                return false;

            static int? TryParseSlotFromText(string text)
            {
                if (string.IsNullOrWhiteSpace(text))
                    return null;
                var digits = new string(text.Where(char.IsDigit).ToArray());
                if (int.TryParse(digits, out var n) && n > 0)
                    return n;
                return null;
            }

            int? preferredSlot = null;
            if (dev is PxiDeviceBase pxiDev && pxiDev.SlotIndex > 0)
                preferredSlot = pxiDev.SlotIndex;

            preferredSlot ??= TryParseSlotFromText(dev.SlotPosition);
            preferredSlot ??= TryParseSlotFromText(dev.Name);
            preferredSlot ??= TryParseSlotFromText(dev.CardName);

            var slotCandidates = new List<int>();
            if (preferredSlot.HasValue)
                slotCandidates.Add(preferredSlot.Value);
            for (int s = 2; s <= 18; s++)
            {
                if (!slotCandidates.Contains(s))
                    slotCandidates.Add(s);
            }
            if (!slotCandidates.Contains(7))
                slotCandidates.Add(7);

            Exception lastEx = null;
            foreach (var slot in slotCandidates)
            {
                token.ThrowIfCancellationRequested();

                IMtx532Api api = null;
                try
                {
                    api = new Mtx532Api(dev, options: new Mtx532Options { SampleRateHz = 1000.0, UseOneBasedAoChannelNumbering = true }, slotNumber: slot);
                    await api.ConnectAsync(token, enabledAoChannels: new[] { "AO2" }).ConfigureAwait(false);
                    await api.SetDcAsync("AO2", 0.0, enable: true, cancellationToken: token).ConfigureAwait(false);

                    // 直流输出需要持续运行，确保万用表可稳定测到
                    try { await api.StartOutputAsync(token).ConfigureAwait(false); } catch { }

                    try { await api.ResetAllToZeroAsync(disableAfterReset: false, cancellationToken: token).ConfigureAwait(false); } catch { }

                    _mtx532 = api;
                    _connectedSlot = slot;
                    UpdateConnectionText();
                    Log($"532连接成功：SLOT={slot}");
                    return true;
                }
                catch (Exception ex)
                {
                    lastEx = ex;
                    try { if (api != null) await api.DisposeAsync().ConfigureAwait(false); } catch { }
                }
            }

            if (lastEx != null)
                Log($"532连接失败：{lastEx.Message}");

            return false;
        }

        private async Task<bool> EnsureArincRxReadyAsync(CancellationToken token)
        {
            if (_arinc != null && _arinc.IsConnected)
                return true;

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
                _atpTxOpened = false;
                _atpModeEntered = false;
            }

            if (_arinc == null)
            {
                Log("未找到ART4229(ARINC429)板卡，无法采集压力/电压回读");
                return false;
            }

            try
            {
                if (!_arinc.IsConnected)
                    await _arinc.ConnectAsync(token).ConfigureAwait(false);

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

                await EnsureAtpModeAsync(token).ConfigureAwait(false);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static byte ReverseLabelBits(byte label)
        {
            byte reversed = 0;
            for (int i = 0; i < 8; i++)
            {
                reversed = (byte)((reversed << 1) | ((label >> i) & 0x01));
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
            Log($"测试信息-ATP发送准备: TX通道{ArincTxChannelIndex}, SSM/Data/SDI=0x{AtpSsmDataSdi:X6}, Label(oct174)=0x{AtpLabelOctal174Dec:X2}, Label反转后=0x{txLabel:X2}, Word=0x{word:X8}");
            try
            {
                await _arinc.SendWordsSingleAsync(ArincTxChannelIndex, new[] { word }, Art4229Parity.None, token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log($"测试信息-ATP发送失败: TX通道{ArincTxChannelIndex}, Word=0x{word:X8}, 异常={ex.Message}");
                throw;
            }
            Log($"测试信息-ATP发送完成: TX通道{ArincTxChannelIndex}, Word=0x{word:X8}");
            _atpModeEntered = true;
        }

        // private async Task<bool> EnsureArincTxReadyAsync(CancellationToken token)
        // {
        //     await EnsureArincRxReadyAsync(token).ConfigureAwait(false);
        //     if (_arinc == null || !_arinc.IsConnected)
        //         return false;
        //
        //     if (_arincTxOpened)
        //         return true;
        //
        //     try
        //     {
        //         await _arinc.OpenTxAsync(ArincTxChannelIndex, token).ConfigureAwait(false);
        //         await _arinc.ConfigureTxAsync(
        //             ArincTxChannelIndex,
        //             rate: ArincRate,
        //             mode: Art4229TxMode.Single,
        //             parity: Art4229Parity.Odd,
        //             wordFormat: Art4229WordFormat.Standard429,
        //             cancellationToken: token).ConfigureAwait(false);
        //         _arincTxOpened = true;
        //         return true;
        //     }
        //     catch
        //     {
        //         _arincTxOpened = false;
        //         return false;
        //     }
        // }
        //
        // private async Task SendSimulatedPressureAndVoltageWordsOnceAsync(double pressurePsi, double voltageV, CancellationToken token)
        // {
        //     if (_arinc == null || !_arinc.IsConnected)
        //         return;
        //
        //     static uint EncodeSignedMagnitude(double value, double resolution)
        //     {
        //         var sign = value < 0;
        //         var magnitude = (uint)Math.Round(Math.Abs(value) / resolution);
        //         if (magnitude > MagnitudeMask)
        //             magnitude = MagnitudeMask;
        //         return magnitude | (sign ? (1u << SignBitIndexInData19) : 0u);
        //     }
        //
        //     var voltageData19 = EncodeSignedMagnitude(voltageV, VoltageResolution);
        //     var pressureData19 = EncodeSignedMagnitude(pressurePsi, PressureResolutionPsi);
        //
        //     var wordV = _arinc.BuildRawWord(VoltageLabelDec, sdi: ArincExpectedSdi, data19: voltageData19, ssm: ArincSsmNormal, applyOddParity: true);
        //     var wordP = _arinc.BuildRawWord(PressureLabelDec, sdi: ArincExpectedSdi, data19: pressureData19, ssm: ArincSsmNormal, applyOddParity: true);
        //
        //     await _arinc.SendWordsSingleAsync(ArincTxChannelIndex, new[] { wordV, wordP }, Art4229Parity.Odd, token).ConfigureAwait(false);
        // }

        private void Log(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            PostToUi(() => Logs.Add($"[{DateTime.Now:HH:mm:ss}] {message}"));
        }

        public void Dispose()
        {
            try { _cts?.Cancel(); } catch { }
            try { _cts?.Dispose(); } catch { }
            try { _opLock?.Dispose(); } catch { }
        }

        public sealed class PressurePointItemViewModel : BindableBase
        {
            private readonly PressureSensorSignalAcquisitionTestViewModel _owner;
            private string _measuredVoltageText = "--";
            private string _measuredPressureText = "--";
            private string _result = "--";

            public PressurePointItemViewModel(
                string indexText,
                string voltageText,
                double targetVoltageV,
                double voltageToleranceV,
                double expectedPressurePsi,
                double pressureTolerancePsi,
                PressureSensorSignalAcquisitionTestViewModel owner)
            {
                IndexText = indexText;
                VoltageText = voltageText;
                TargetVoltageV = targetVoltageV;
                VoltageToleranceV = voltageToleranceV;
                ExpectedPressurePsi = expectedPressurePsi;
                PressureTolerancePsi = pressureTolerancePsi;
                _owner = owner;
                MeasureCommand = new DelegateCommand(async () => await MeasureAsync());
            }

            public string IndexText { get; }
            public string VoltageText { get; }
            public double TargetVoltageV { get; }
            public double VoltageToleranceV { get; }
            public double ExpectedPressurePsi { get; }
            public double PressureTolerancePsi { get; }

            public string MeasuredVoltageText
            {
                get => _measuredVoltageText;
                private set => SetProperty(ref _measuredVoltageText, value);
            }

            public string MeasuredPressureText
            {
                get => _measuredPressureText;
                private set => SetProperty(ref _measuredPressureText, value);
            }

            internal void SetRealtimeVoltage(double v)
            {
                if (Result == "--")
                    MeasuredVoltageText = v.ToString("F4", CultureInfo.InvariantCulture);
            }

            internal void SetRealtimePressure(double p)
            {
                if (Result == "--")
                    MeasuredPressureText = p.ToString("F3", CultureInfo.InvariantCulture);
            }

            public string Result
            {
                get => _result;
                private set => SetProperty(ref _result, value);
            }

            public DelegateCommand MeasureCommand { get; }

            internal void Reset()
            {
                MeasuredVoltageText = "--";
                MeasuredPressureText = "--";
                Result = "--";
            }

            public async Task MeasureAsync()
            {
                Reset();
                if (_owner == null)
                    return;

                var (measuredV, measuredP, result) = await _owner.MeasurePointAsync(TargetVoltageV, VoltageToleranceV, ExpectedPressurePsi, PressureTolerancePsi).ConfigureAwait(false);
                _owner.PostToUi(() =>
                {
                    MeasuredVoltageText = measuredV.HasValue
                        ? measuredV.Value.ToString("F4", CultureInfo.InvariantCulture)
                        : "--";
                    MeasuredPressureText = measuredP.HasValue
                        ? measuredP.Value.ToString("F3", CultureInfo.InvariantCulture)
                        : "--";
                    Result = result;
                });

                if (_owner.IsAutoTestRunning || _owner.IsManualTestRunning)
                {
                    _owner.LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    if (_owner.Items.All(x => x.Result == "--"))
                        _owner.LastTestResult = "--";
                    else if (_owner.Items.Any(x => x.Result == "FAIL"))
                        _owner.LastTestResult = "FAIL";
                    else if (_owner.Items.All(x => x.Result == "PASS"))
                        _owner.LastTestResult = "PASS";
                }
            }
        }
    }
}
