using Prism.Commands;
using Prism.Ioc;
using Prism.Events;
using Prism.Mvvm;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using MeasureControl.Models.Devices;
using MeasureControl.Services;
using MeasureControl.Services.HardwareApis;

namespace MeasureControl.ViewModels.SingleBoardTest.InertController
{
    public class ControlBoardDiscreteInputModuleTestViewModel : BindableBase, IDisposable
    {
        public enum DiscreteInputState
        {
            Gnd,
            Open,
            V28
        }

        private const string PowerSupplyIpAddress = "192.168.1.15";
        private const double InputVoltageV = 28.0;
        private const double InputCurrentA = 3.0;

        public bool SkipMainPowerOff { get; set; }

        // 继电器动作稳定延时
        private const int RelaySettleDelayMs = 120;

        // DO输出驱动继电器：项目中已有用法（如 PowerImpedanceTestViewModel）为 active-low
        // 即：false=吸合/有效，true=断开/无效
        private const bool ActiveLowRelay = true;
        private const bool SinkingOutputMode = true;

        private const int ArincRxChannelIndex = 0;
        private const int ArincTxChannelIndex = 1;
        private const uint AtpSsmDataSdi = 0xC10001u;
        private const byte AtpLabelOctal174Dec = 124;
        private const byte DiscreteStatusLabelOctal151Dec = 105;
        private const byte PowerInputLabelOctal152Dec = 106;
        private const byte ProgEnableLabelOctal153Dec = 107;
        private const double ArincRate = 100000.0;
        private const int ArincAfterRxStartSettleDelayMs = 1000;
        private const int ArincPollIntervalMs = 10;
        private const int ArincReadTimeoutMs = 700;

        private const byte ArincExpectedSdi = 1;
        private byte _arincExpectedLabelRaw;

        private readonly SemaphoreSlim _opLock = new SemaphoreSlim(1, 1);
        private CancellationTokenSource _cts;

        private readonly IEventAggregator _eventAggregator;
        private readonly IComponentPowerStateApi _componentPowerStateApi;

        private IPowerSupplyApi _power;
        private IJy7131Api _jy7131;
        private IArt4229Api _arinc;

        private bool _atpTxOpened;
        private bool _atpModeEntered;

        private bool _isManualTestRunning;
        private bool _isAutoTestRunning;
        private bool _isPowerOn;
        private string _powerStatus = "未供电";
        private string _lastTestTime = "--";
        private string _lastTestResult = "--";
        private string _overallResult = "--";

        public ControlBoardDiscreteInputModuleTestViewModel(IEventAggregator eventAggregator, IComponentPowerStateApi componentPowerStateApi = null)
        {
            _eventAggregator = eventAggregator;
            _componentPowerStateApi = componentPowerStateApi;

            ManualTestCommand = new DelegateCommand(OnManualTest);
            AutoTestCommand = new DelegateCommand(async () => await OnAutoTestAsync());
            ClearLogCommand = new DelegateCommand(() => Logs.Clear());

            Items = new ObservableCollection<DiscreteInputItemViewModel>
            {
                new DiscreteInputItemViewModel("a)", "J40", doChannel: "DO0", labelOctalDec: DiscreteStatusLabelOctal151Dec, bitIndex: 14, primary: DiscreteInputState.Gnd, secondary: DiscreteInputState.Open, this),
                new DiscreteInputItemViewModel("b)", "J41", doChannel: "DO1", labelOctalDec: DiscreteStatusLabelOctal151Dec, bitIndex: 13, primary: DiscreteInputState.Gnd, secondary: DiscreteInputState.Open, this),
                new DiscreteInputItemViewModel("c)", "J42", doChannel: "DO2", labelOctalDec: DiscreteStatusLabelOctal151Dec, bitIndex: 15, primary: DiscreteInputState.Gnd, secondary: DiscreteInputState.Open, this),
                new DiscreteInputItemViewModel("d)", "J43", doChannel: "DO3", labelOctalDec: DiscreteStatusLabelOctal151Dec, bitIndex: 16, primary: DiscreteInputState.Gnd, secondary: DiscreteInputState.Open, this),
                new DiscreteInputItemViewModel("e)", "J44", doChannel: "DO4", labelOctalDec: DiscreteStatusLabelOctal151Dec, bitIndex: 11, primary: DiscreteInputState.Gnd, secondary: DiscreteInputState.Open, this),
                new DiscreteInputItemViewModel("f)", "J45", doChannel: "DO5", labelOctalDec: DiscreteStatusLabelOctal151Dec, bitIndex: 12, primary: DiscreteInputState.Gnd, secondary: DiscreteInputState.Open, this),

                new DiscreteInputItemViewModel("g)", "J75", doChannel: "DO6", labelOctalDec: DiscreteStatusLabelOctal151Dec, bitIndex: 19, primary: DiscreteInputState.Gnd, secondary: DiscreteInputState.Open, this),
                new DiscreteInputItemViewModel("h)", "J76", doChannel: "DO7", labelOctalDec: DiscreteStatusLabelOctal151Dec, bitIndex: 20, primary: DiscreteInputState.Gnd, secondary: DiscreteInputState.Open, this),
                new DiscreteInputItemViewModel("i)", "J77", doChannel: "DO8", labelOctalDec: DiscreteStatusLabelOctal151Dec, bitIndex: 21, primary: DiscreteInputState.Gnd, secondary: DiscreteInputState.Open, this),
                new DiscreteInputItemViewModel("j)", "J78", doChannel: "DO9", labelOctalDec: ProgEnableLabelOctal153Dec, bitIndex: 24, primary: DiscreteInputState.Gnd, secondary: DiscreteInputState.Open, this),
                new DiscreteInputItemViewModel("k)", "J79", doChannel: "DO10", labelOctalDec: DiscreteStatusLabelOctal151Dec, bitIndex: 22, primary: DiscreteInputState.Gnd, secondary: DiscreteInputState.Open, this),
                new DiscreteInputItemViewModel("l)", "J80", doChannel: "DO11", labelOctalDec: DiscreteStatusLabelOctal151Dec, bitIndex: 23, primary: DiscreteInputState.Gnd, secondary: DiscreteInputState.Open, this),
                new DiscreteInputItemViewModel("m)", "J81", doChannel: "DO12", labelOctalDec: DiscreteStatusLabelOctal151Dec, bitIndex: 24, primary: DiscreteInputState.Gnd, secondary: DiscreteInputState.Open, this),
                new DiscreteInputItemViewModel("n)", "J82", doChannel: "DO13", labelOctalDec: DiscreteStatusLabelOctal151Dec, bitIndex: 25, primary: DiscreteInputState.Gnd, secondary: DiscreteInputState.Open, this),
                new DiscreteInputItemViewModel("o)", "J83", doChannel: "DO14", labelOctalDec: DiscreteStatusLabelOctal151Dec, bitIndex: 26, primary: DiscreteInputState.Gnd, secondary: DiscreteInputState.Open, this),

                new DiscreteInputItemViewModel("p)", "J84", doChannel: "DO16", labelOctalDec: PowerInputLabelOctal152Dec, bitIndex: 26, primary: DiscreteInputState.V28, secondary: DiscreteInputState.Open, this),
                new DiscreteInputItemViewModel("q)", "J85", doChannel: "DO17", labelOctalDec: PowerInputLabelOctal152Dec, bitIndex: 27, primary: DiscreteInputState.V28, secondary: DiscreteInputState.Open, this),
            };
        }

        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();

        public ObservableCollection<DiscreteInputItemViewModel> Items { get; }

        public DelegateCommand ManualTestCommand { get; }
        public DelegateCommand AutoTestCommand { get; }
        public DelegateCommand ClearLogCommand { get; }

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
            set => SetProperty(ref _isManualTestRunning, value);
        }

        public bool IsAutoTestRunning
        {
            get => _isAutoTestRunning;
            set => SetProperty(ref _isAutoTestRunning, value);
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

        public string OverallResult
        {
            get => _overallResult;
            set => SetProperty(ref _overallResult, value);
        }

        private void OnManualTest()
        {
            if (IsManualTestRunning)
            {
                _ = StopAsync();
                return;
            }

            // 检查是否已总上电
            var _hps = ContainerLocator.Container.Resolve<IHydraulicPowerService>();
            if (_hps == null || !_hps.IsHydraulicPowered)
            {
                MessageBox.Show("请先点击左上角组件上电按钮进行总上电，再进行测试。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _ = StartAsync();
        }

        private async Task StartAsync()
        {
            await _opLock.WaitAsync();
            try
            {
                if (IsManualTestRunning)
                    return;

                IsManualTestRunning = true;

                PublishNavigationLock(isLocked: true, source: "ControlBoardDiscreteInput");

                LastTestTime = "--";
                LastTestResult = "--";
                OverallResult = "--";

                _cts?.Cancel();
                _cts?.Dispose();
                _cts = new CancellationTokenSource();

                _atpTxOpened = false;
                _atpModeEntered = false;

                AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试启动：离散输入模块测试");

                await EnsurePowerAsync(_cts.Token).ConfigureAwait(false);
                var ok7131 = await Ensure7131ReadyAsync(_cts.Token).ConfigureAwait(false);
                await EnsureArincRxReadyAsync(_cts.Token).ConfigureAwait(false);

                if (!ok7131)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 7131板卡初始化失败，手动测试终止");
                    await StopAsync().ConfigureAwait(false);
                    return;
                }

                foreach (var item in Items)
                {
                    await item.SetStateAsync(item.PrimaryState, _cts.Token, log: false).ConfigureAwait(false);
                }

                AddLog($"[{DateTime.Now:HH:mm:ss}] 已下发手动测试默认配置（不逐项输出），可通过每项‘切换’按钮单独切换开闭");
            }
            catch
            {
                IsManualTestRunning = false;
                PublishNavigationLock(isLocked: false, source: "ControlBoardDiscreteInput");
                throw;
            }
            finally
            {
                _opLock.Release();
            }
        }

        private async Task StopAsync()
        {
            await _opLock.WaitAsync();
            try
            {
                if (!IsManualTestRunning)
                    return;

                try { _cts?.Cancel(); } catch { }

                try { await CloseArincAsync().ConfigureAwait(false); } catch { }

                await Disable485AndExternalPowerAsync(CancellationToken.None).ConfigureAwait(false);

                try { await ResetAndClose7131Async(CancellationToken.None).ConfigureAwait(false); } catch { }

                await DisablePowerAsync(CancellationToken.None).ConfigureAwait(false);

                IsManualTestRunning = false;
                PublishNavigationLock(isLocked: false, source: "ControlBoardDiscreteInput");
                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试停止");
            }
            finally
            {
                _opLock.Release();
            }
        }

        public async Task<string> RunOnceAsync(CancellationToken cancellationToken)
        {
            await _opLock.WaitAsync(cancellationToken);
            try
            {
                if (IsAutoTestRunning)
                    return OverallResult;

                IsAutoTestRunning = true;

                PublishNavigationLock(isLocked: true, source: "ControlBoardDiscreteInput");

                LastTestTime = "--";
                LastTestResult = "--";
                OverallResult = "--";

                AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试开始：将依次执行表 7-1 的配置与采集检查");

                _cts?.Cancel();
                _cts?.Dispose();
                _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

                _atpTxOpened = false;
                _atpModeEntered = false;

                await EnsurePowerAsync(_cts.Token).ConfigureAwait(false);

                await Ensure7131ReadyAsync(_cts.Token).ConfigureAwait(false);
                await EnsureArincRxReadyAsync(_cts.Token).ConfigureAwait(false);

                AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试初始化完成：已下发ATP进入指令，等待模式稳定...");
                await Task.Delay(100, _cts.Token).ConfigureAwait(false);

                bool allPass = true;
                foreach (var item in Items)
                    await item.SetStateAsync(item.PrimaryState, _cts.Token).ConfigureAwait(false);

                foreach (var item in Items)
                    allPass &= await item.MeasureAsync(stateTag: "第一轮", token: _cts.Token).ConfigureAwait(false);

                foreach (var item in Items)
                    await item.SetStateAsync(item.SecondaryState, _cts.Token).ConfigureAwait(false);

                foreach (var item in Items)
                    allPass &= await item.MeasureAsync(stateTag: "第二轮", token: _cts.Token).ConfigureAwait(false);

                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                LastTestResult = allPass ? "PASS" : "FAIL";
                OverallResult = LastTestResult;

                AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试结束：汇总结果={LastTestResult}");
                if (allPass)
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 项目测试通过");

                return OverallResult;
            }
            catch (OperationCanceledException)
            {
                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                LastTestResult = "FAIL";
                OverallResult = "FAIL";
                AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试已取消");
                throw;
            }
            catch (Exception ex)
            {
                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                LastTestResult = "FAIL";
                OverallResult = "FAIL";
                AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试异常：{ex.Message}");
                return "FAIL";
            }
            finally
            {
                await Disable485AndExternalPowerAsync(CancellationToken.None).ConfigureAwait(false);
                try { await ResetAndClose7131Async(CancellationToken.None).ConfigureAwait(false); } catch { }
                await DisablePowerAsync(CancellationToken.None).ConfigureAwait(false);
                try { await CloseArincAsync().ConfigureAwait(false); } catch { }
                IsAutoTestRunning = false;
                PublishNavigationLock(isLocked: false, source: "ControlBoardDiscreteInput");
                _opLock.Release();
            }
        }

        private async Task OnAutoTestAsync()
        {
            // 检查是否已总上电
            var _hps = ContainerLocator.Container.Resolve<IHydraulicPowerService>();
            if (_hps == null || !_hps.IsHydraulicPowered)
            {
                MessageBox.Show("请先点击左上角组件上电按钮进行总上电，再进行测试。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            await _opLock.WaitAsync();
            try
            {
                if (IsAutoTestRunning)
                    return;

                IsAutoTestRunning = true;

                PublishNavigationLock(isLocked: true, source: "ControlBoardDiscreteInput");

                LastTestTime = "--";
                LastTestResult = "--";
                OverallResult = "--";

                AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试开始：将依次执行表 7-1 的配置与采集检查");

                _cts?.Cancel();
                _cts?.Dispose();
                _cts = new CancellationTokenSource();

                _atpTxOpened = false;
                _atpModeEntered = false;

                await EnsurePowerAsync(_cts.Token).ConfigureAwait(false);

                var ok7131 = await Ensure7131ReadyAsync(_cts.Token).ConfigureAwait(false);
                await EnsureArincRxReadyAsync(_cts.Token).ConfigureAwait(false);

                if (!ok7131)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 7131板卡初始化失败，自动测试终止");
                    OverallResult = "FAIL";
                    LastTestResult = "FAIL";
                    LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    return;
                }

                AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试初始化完成：已下发ATP进入指令，等待模式稳定...");
                await Task.Delay(100, _cts.Token).ConfigureAwait(false);

                bool allPass = true;
                foreach (var item in Items)
                    await item.SetStateAsync(item.PrimaryState, _cts.Token).ConfigureAwait(false);

                foreach (var item in Items)
                    allPass &= await item.MeasureAsync(stateTag: "第一轮", token: _cts.Token).ConfigureAwait(false);

                foreach (var item in Items)
                    await item.SetStateAsync(item.SecondaryState, _cts.Token).ConfigureAwait(false);

                foreach (var item in Items)
                    allPass &= await item.MeasureAsync(stateTag: "第二轮", token: _cts.Token).ConfigureAwait(false);

                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                LastTestResult = allPass ? "PASS" : "FAIL";
                OverallResult = LastTestResult;

                AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试结束：汇总结果={LastTestResult}");
                if (allPass)
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 项目测试通过");
            }
            catch (Exception ex)
            {
                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                LastTestResult = "FAIL";
                OverallResult = "FAIL";
                AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试异常：{ex.Message}");
            }
            finally
            {
                await Disable485AndExternalPowerAsync(CancellationToken.None).ConfigureAwait(false);
                try { await ResetAndClose7131Async(CancellationToken.None).ConfigureAwait(false); } catch { }
                await DisablePowerAsync(CancellationToken.None).ConfigureAwait(false);
                try { await CloseArincAsync().ConfigureAwait(false); } catch { }
                IsAutoTestRunning = false;
                PublishNavigationLock(isLocked: false, source: "ControlBoardDiscreteInput");
                _opLock.Release();
            }
        }

        private async Task ResetAndClose7131Async(CancellationToken token)
        {
            var api = _jy7131;
            if (api == null)
                return;

            try
            {
                if (api.IsConnected)
                {
                    try { await api.ResetAllDoAsync(token).ConfigureAwait(false); } catch { }
                    try { await api.SetAllRelaysAsync(false, token).ConfigureAwait(false); } catch { }
                    try { await api.DisablePowerOutputsAsync(token).ConfigureAwait(false); } catch { }

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
                if (ReferenceEquals(_jy7131, api))
                {
                    _jy7131 = null;
                }
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
            _atpTxOpened = false;
            _atpModeEntered = false;
        }

        private async Task EnsurePowerAsync(CancellationToken token)
        {
            // 192.168.1.15 CH1 不再由本测试控制上电，由总上电统一管理
            await Task.Delay(100, token).ConfigureAwait(false);

            Application.Current?.Dispatcher?.Invoke(() =>
            {
                IsPowerOn = true;
                PowerStatus = $"已供电(CH1 {InputVoltageV:0.###}V)";
            });
        }

        private async Task DisablePowerAsync(CancellationToken token)
        {
            // 192.168.1.15 CH1 不再由本测试控制下电
            try
            {
                if (_power == null)
                    return;

                if (!_power.IsConnected)
                    await _power.ConnectAsync(PowerSupplyIpAddress, token).ConfigureAwait(false);

                await _power.SetOutputEnabledAsync(PowerSupplyChannel.CH2, false, token).ConfigureAwait(false);
                await _power.SetOutputEnabledAsync(PowerSupplyChannel.CH3, false, token).ConfigureAwait(false);
                await Task.Delay(200, token).ConfigureAwait(false);
            }
            catch
            {
            }
            finally
            {
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    IsPowerOn = false;
                    PowerStatus = "未就绪";
                });
            }
        }

        private async Task<bool> Ensure7131ReadyAsync(CancellationToken token)
        {
            if (_jy7131 != null && _jy7131.IsConnected && _jy7131.IsRunning)
                return true;

            if (_jy7131 == null)
            {
                // 尝试从容器里拿到 PXI 服务（如果有）用于定位 7131 设备
                // 这里保持“尽力而为”：找不到就只记录日志，避免页面直接崩
                try
                {
                    var pxi = ContainerLocator.Container.Resolve<IPxiChassisService>();
                    var chassisList = pxi?.GetAllChassis();
                    if (chassisList != null)
                    {
                        foreach (var chassis in chassisList)
                        {
                            if (chassis?.Devices == null) continue;
                            var dev = chassis.Devices.FirstOrDefault(d =>
                                (d?.Model?.IndexOf("7131", StringComparison.OrdinalIgnoreCase) >= 0) ||
                                (d?.DeviceTypeName?.IndexOf("离散量", StringComparison.OrdinalIgnoreCase) >= 0) ||
                                (d?.DeviceTypeName?.IndexOf("数字量", StringComparison.OrdinalIgnoreCase) >= 0));
                            if (dev != null)
                            {
                                _jy7131 = new Jy7131Api(dev);
                                break;
                            }
                        }
                    }
                }
                catch
                {
                }
            }

            if (_jy7131 == null)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 未找到7131板卡，继电器固定输出无法下发");
                return false;
            }

            if (!_jy7131.IsConnected)
                await _jy7131.ConnectAsync(token).ConfigureAwait(false);
            if (!_jy7131.IsRunning)
            {
                await _jy7131.SetOutputModeAsync(Jy7131OutputMode.Sinking, token).ConfigureAwait(false);
                await _jy7131.StartAsync(token).ConfigureAwait(false);
            }
            
            return _jy7131.IsConnected && _jy7131.IsRunning;
        }

        internal async Task ApplyItemStateAsync(string doChannel, DiscreteInputState state, CancellationToken token, bool log = true)
        {
            if (token.IsCancellationRequested)
                return;

            await Ensure7131ReadyAsync(token).ConfigureAwait(false);

            if (_jy7131 == null || !_jy7131.IsConnected)
                return;

            ////**********
            //try
            //{
            //    await _jy7131.SetPowerVoltagesAsync(0, 0, 22, 0, token).ConfigureAwait(false);
            //    await _jy7131.EnablePowerOutputsAsync(0, 0, 22, 0, token).ConfigureAwait(false);
            //    await _jy7131.WriteDoAsync("DO16", Level(relayClosed: true), token).ConfigureAwait(false);
            //    await _jy7131.WriteDoAsync("DO17", Level(relayClosed: false), token).ConfigureAwait(false);
            //}
            //catch
            //{
            //}
            //*********************



            // 7131 外部电源输出：按工装约定 (0,0,28,0)
            // 485 继电器：按工装约定通过继电器选择“低信号(地)”。
            // 说明：28V 来源为 7131 外部电源输出第三路(对应 DO16-DO23)，通过 DO 闭合/断开实现 28V/开路，
            // 因此 state=28V 时不需要操作 485。
            try
            {
                await _jy7131.SetPowerVoltagesAsync(0, 0, 28, 0, token).ConfigureAwait(false);
                await _jy7131.EnablePowerOutputsAsync(0, 0, 28, 0, token).ConfigureAwait(false);

                if (state == DiscreteInputState.Gnd)
                {
                    await _jy7131.SetRelayAsync(1, true, token).ConfigureAwait(false);
                    await _jy7131.SetRelayAsync(2, true, token).ConfigureAwait(false);
                    await _jy7131.SetRelayAsync(3, true, token).ConfigureAwait(false);
                }
            }
            catch
            {
            }

            bool Level(bool relayClosed)
            {
                // relayClosed: true=继电器吸合(输出有效)，false=继电器断开(开路)
                if (SinkingOutputMode)
                    return relayClosed;
                if (!ActiveLowRelay)
                    return relayClosed;
                return !relayClosed;
            }

            bool closed = state != DiscreteInputState.Open;
            var writeValue = Level(relayClosed: closed);
            try
            {
                await _jy7131.WriteDoAsync(doChannel, writeValue, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            int doIndex = -1;
            if (!string.IsNullOrWhiteSpace(doChannel) && doChannel.Length > 2)
                int.TryParse(doChannel.Substring(2), out doIndex);

            int? readBack = null;
            try
            {
                if (doIndex >= 0 && doIndex < 32)
                {
                    var mask = await _jy7131.ReadDoBitmaskAsync(token).ConfigureAwait(false);
                    readBack = ((mask >> doIndex) & 0x01u) != 0 ? 1 : 0;
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
            }

            if (RelaySettleDelayMs > 0)
            {
                try { await Task.Delay(RelaySettleDelayMs, token).ConfigureAwait(false); } catch { return; }
            }

            if (log)
                AddLog($"[{DateTime.Now:HH:mm:ss}] 已切换输出：{doChannel}={GetStateText(state)} 开闭={(closed ? "闭" : "开")} 写值={(writeValue ? 1 : 0)} 读回={(readBack.HasValue ? readBack.Value.ToString() : "--")}");
        }

        internal static string GetStateText(DiscreteInputState state)
        {
            return state switch
            {
                DiscreteInputState.Gnd => "GND",
                DiscreteInputState.Open => "开路",
                DiscreteInputState.V28 => "28V",
                _ => "--"
            };
        }

        private async Task Disable485AndExternalPowerAsync(CancellationToken token)
        {
            try
            {
                if (_jy7131 == null)
                    return;

                if (!_jy7131.IsConnected)
                    await _jy7131.ConnectAsync(token).ConfigureAwait(false);

                await _jy7131.SetAllRelaysAsync(false, token).ConfigureAwait(false);
                await _jy7131.DisablePowerOutputsAsync(token).ConfigureAwait(false);
            }
            catch
            {
            }
        }

        private async Task EnsureArincRxReadyAsync(CancellationToken token)
        {
            if (_arinc != null && _arinc.IsConnected)
            {
                // 进入手动/自动测试时会重置 _atpModeEntered=false，需要在“已连接”场景下也确保下发ATP进入指令
                try
                {
                    try
                    {
                        // 已连接并不代表RX通道仍处于start状态，且某些情况下需要先Stop/Close后再Open才能恢复接收。
                        try { await _arinc.StopRxAsync(ArincRxChannelIndex, token).ConfigureAwait(false); } catch { }
                        try { await _arinc.CloseRxAsync(ArincRxChannelIndex, token).ConfigureAwait(false); } catch { }

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
                    }
                    catch (Exception ex)
                    {
                        AddLog($"[{DateTime.Now:HH:mm:ss}] RX重启失败(已连接场景)：{ex.Message}");
                    }

                    _arincExpectedLabelRaw = _arinc.ReverseLabel(DiscreteStatusLabelOctal151Dec);

                    try { await Task.Delay(ArincAfterRxStartSettleDelayMs, token).ConfigureAwait(false); } catch { }

                    await EnsureAtpModeAsync(token).ConfigureAwait(false);

                    try { await Task.Delay(50, token).ConfigureAwait(false); } catch { }
                }
                catch (Exception ex)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] ATP发送异常(已连接场景)：{ex.Message}");
                    throw;
                }
                return;
            }

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
                AddLog($"[{DateTime.Now:HH:mm:ss}] 未找到ART4229(ARINC429)板卡，无法采集通信结果");
                return;
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

                _arincExpectedLabelRaw = _arinc.ReverseLabel(DiscreteStatusLabelOctal151Dec);

                try { await Task.Delay(ArincAfterRxStartSettleDelayMs, token).ConfigureAwait(false); } catch { }

                await EnsureAtpModeAsync(token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 429初始化/ATP进入失败: RX通道{ArincRxChannelIndex} TX通道{ArincTxChannelIndex} 异常={ex.Message}");
                throw;
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

        private byte GetLabelRawForRx(byte labelOctalDec)
        {
            if (_arinc != null)
                return _arinc.ReverseLabel(labelOctalDec);

            return ReverseLabelBits(labelOctalDec);
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
            AddLog($"[{DateTime.Now:HH:mm:ss}] ATP发送准备: TX通道{ArincTxChannelIndex}, SSM/Data/SDI=0x{AtpSsmDataSdi:X6}, Label(oct174)=0x{AtpLabelOctal174Dec:X2}, Label反转后=0x{txLabel:X2}, Word=0x{word:X8}");

            try
            {
                // 经验上部分设备需要重复进入指令才会稳定切入ATP
                for (int i = 0; i < 3; i++)
                {
                    await _arinc.SendWordsSingleAsync(ArincTxChannelIndex, new[] { word }, Art4229Parity.None, token).ConfigureAwait(false);
                    if (i < 2)
                        await Task.Delay(30, token).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] ATP发送失败: TX通道{ArincTxChannelIndex}, Word=0x{word:X8}, 异常={ex.Message}");
                throw;
            }

            AddLog($"[{DateTime.Now:HH:mm:ss}] ATP发送完成: TX通道{ArincTxChannelIndex}, Word=0x{word:X8}");
            _atpModeEntered = true;
        }

        private async Task FlushArincRxBufferAsync(CancellationToken token)
        {
            if (_arinc == null || !_arinc.IsConnected)
                return;

            try
            {
                while (!token.IsCancellationRequested)
                {
                    var words = await _arinc.ReadRxWordsAsync(
                        ArincRxChannelIndex,
                        maxCount: 512,
                        enableTimeTag: false,
                        enableRateAdaption: false,
                        cancellationToken: token).ConfigureAwait(false);

                    if (words == null || words.Count == 0)
                        break;
                }
            }
            catch
            {
            }
        }

        private static int GetBit(uint word, int bitIndex)
        {
            if (bitIndex < 0 || bitIndex > 31)
                return -1;
            return (int)((word >> bitIndex) & 0x01);
        }

        private async Task<int?> ReadArincBitAsync(int bitIndex, byte labelOctalDec, CancellationToken token)
        {
            if (bitIndex < 0 || bitIndex > 31)
                return null;

            await EnsureArincRxReadyAsync(token).ConfigureAwait(false);
            if (_arinc == null || !_arinc.IsConnected)
                return null;

            await FlushArincRxBufferAsync(token).ConfigureAwait(false);

            var expectedLabelRaw = GetLabelRawForRx(labelOctalDec);

            var deadline = DateTime.UtcNow.AddMilliseconds(ArincReadTimeoutMs);
            while (!token.IsCancellationRequested && DateTime.UtcNow <= deadline)
            {
                var words = await _arinc.ReadRxWordsAsync(ArincRxChannelIndex, maxCount: 128, enableTimeTag: false, enableRateAdaption: false, cancellationToken: token).ConfigureAwait(false);
                if (words.Count > 0)
                {
                    for (int i = words.Count - 1; i >= 0; i--)
                    {
                        var w = words[i].Data429;
                        var label = (byte)(w & 0xFF);
                        if (label != expectedLabelRaw)
                            continue;

                        var sdi = (byte)((w >> 8) & 0x03);
                        if (sdi != ArincExpectedSdi)
                            continue;

                        return GetBit(w, bitIndex);
                    }
                }

                if (ArincPollIntervalMs > 0)
                    await Task.Delay(ArincPollIntervalMs, token).ConfigureAwait(false);
            }

            return null;
        }

        internal void AddLog(string msg)
        {
            if (string.IsNullOrWhiteSpace(msg))
                return;

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null)
            {
                Logs.Add(msg);
                return;
            }

            if (dispatcher.CheckAccess())
            {
                Logs.Add(msg);
                return;
            }

            dispatcher.BeginInvoke(new Action(() => Logs.Add(msg)));
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

        public void Dispose()
        {
            try { _cts?.Cancel(); } catch { }
            try { _cts?.Dispose(); } catch { }
            _opLock?.Dispose();

            try { ResetAndClose7131Async(CancellationToken.None).GetAwaiter().GetResult(); } catch { }
            try { CloseArincAsync().GetAwaiter().GetResult(); } catch { }

            try { _power?.DisposeAsync().AsTask().GetAwaiter().GetResult(); } catch { }
        }

        public class DiscreteInputItemViewModel : BindableBase
        {
            private readonly ControlBoardDiscreteInputModuleTestViewModel _owner;

            private string _actualResult = "--";
            private string _result = "--";
            private string _primaryActualResult = "--";
            private string _primaryResult = "--";
            private string _secondaryActualResult = "--";
            private string _secondaryResult = "--";

            public DiscreteInputItemViewModel(
                string indexText,
                string pins,
                string doChannel,
                byte labelOctalDec,
                int bitIndex,
                DiscreteInputState primary,
                DiscreteInputState secondary,
                ControlBoardDiscreteInputModuleTestViewModel owner,
                int openStateBitValue = 0)
            {
                Pins = pins;
                IndexText = indexText;

                DoChannel = doChannel;
                LabelOctalDec = labelOctalDec;
                BitIndex = bitIndex;
                OpenStateBitValue = openStateBitValue;

                PrimaryState = primary;
                SecondaryState = secondary;
                _currentState = primary;
                ExpectedResult = GetStateText(_currentState);

                _owner = owner;

                MeasureCommand = new DelegateCommand(async () => await MeasureAsync());
                ToggleStateCommand = new DelegateCommand(async () => await ToggleAsync());
            }

            public string IndexText { get; }

            public string Pins { get; }

            public string DoChannel { get; }

            public byte LabelOctalDec { get; }

            public int BitIndex { get; }

            public int OpenStateBitValue { get; }

            public DiscreteInputState PrimaryState { get; }

            public DiscreteInputState SecondaryState { get; }

            private DiscreteInputState _currentState;
            public DiscreteInputState CurrentState
            {
                get => _currentState;
                private set
                {
                    if (SetProperty(ref _currentState, value))
                        ExpectedResult = GetStateText(value);
                }
            }

            private string _expectedResult;
            public string ExpectedResult
            {
                get => _expectedResult;
                set => SetProperty(ref _expectedResult, value);
            }

            public string ActualResult
            {
                get => _actualResult;
                set => SetProperty(ref _actualResult, value);
            }

            public string Result
            {
                get => _result;
                set => SetProperty(ref _result, value);
            }

            public string PrimaryActualResult
            {
                get => _primaryActualResult;
                private set => SetProperty(ref _primaryActualResult, value);
            }

            public string PrimaryResult
            {
                get => _primaryResult;
                private set => SetProperty(ref _primaryResult, value);
            }

            public string SecondaryActualResult
            {
                get => _secondaryActualResult;
                private set => SetProperty(ref _secondaryActualResult, value);
            }

            public string SecondaryResult
            {
                get => _secondaryResult;
                private set => SetProperty(ref _secondaryResult, value);
            }

            public DelegateCommand MeasureCommand { get; }

            public DelegateCommand ToggleStateCommand { get; }

            private async Task ToggleAsync()
            {
                if (_owner == null)
                    return;

                var token = _owner._cts?.Token ?? CancellationToken.None;
                if (token.IsCancellationRequested)
                    return;

                var next = CurrentState == PrimaryState ? SecondaryState : PrimaryState;
                await SetStateAsync(next, token).ConfigureAwait(false);
            }

            public async Task SetStateAsync(DiscreteInputState state, CancellationToken token, bool log = true)
            {
                if (_owner == null)
                    return;

                if (token.IsCancellationRequested)
                    return;

                try
                {
                    await _owner.ApplyItemStateAsync(DoChannel, state, token, log: log).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                Application.Current?.Dispatcher?.Invoke(() => { CurrentState = state; });
            }

            public async Task<bool> MeasureAsync(string stateTag = null, CancellationToken token = default)
            {
                ActualResult = "--";
                Result = "--";

                if (_owner == null)
                    return false;

                if (BitIndex < 0)
                {
                    _owner.AddLog($"[{DateTime.Now:HH:mm:ss}] {Pins} 未配置通讯bit位，无法采集（请补充表格中该针脚对应bit）");
                    return false;
                }

                try
                {
                    if (token == default)
                        token = _owner._cts?.Token ?? CancellationToken.None;

                    var bit = await _owner.ReadArincBitAsync(BitIndex, LabelOctalDec, token).ConfigureAwait(false);
                    int expectedBit;
                    if (string.Equals(ExpectedResult, "开路", StringComparison.OrdinalIgnoreCase))
                        expectedBit = OpenStateBitValue;
                    else
                        expectedBit = OpenStateBitValue == 0 ? 1 : 0;
                    string resultText;
                    if (!bit.HasValue)
                        resultText = "FAIL";
                    else
                        resultText = bit.Value == expectedBit ? "PASS" : "FAIL";

                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        ActualResult = bit.HasValue ? bit.Value.ToString() : "--";
                        Result = resultText;
                        
                        // Save to primary or secondary based on current state
                        if (CurrentState == PrimaryState)
                        {
                            PrimaryActualResult = bit.HasValue ? bit.Value.ToString() : "--";
                            PrimaryResult = resultText;
                        }
                        else if (CurrentState == SecondaryState)
                        {
                            SecondaryActualResult = bit.HasValue ? bit.Value.ToString() : "--";
                            SecondaryResult = resultText;
                        }
                    });

                    _owner.AddLog($"[{DateTime.Now:HH:mm:ss}] 通讯采集：{Pins}({DoChannel}) {(stateTag ?? "")} 配置={ExpectedResult} bit{BitIndex}={(bit.HasValue ? bit.Value.ToString() : "--")} 期望={expectedBit} 结果={resultText}");

                    return string.Equals(resultText, "PASS", StringComparison.OrdinalIgnoreCase);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    _owner.AddLog($"[{DateTime.Now:HH:mm:ss}] 通讯采集异常：{Pins} {ex.Message}");
                }

                return false;
            }
        }
    }
}
