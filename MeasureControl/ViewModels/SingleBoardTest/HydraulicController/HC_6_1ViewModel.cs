using Prism.Commands;
using Prism.Mvvm;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Windows;
using MeasureControl.Events;
using MeasureControl.Models.Devices;
using MeasureControl.Services;
using MeasureControl.Services.HardwareApis;
using Prism.Events;
using Prism.Ioc;

namespace MeasureControl.ViewModels.SingleBoardTest.HydraulicController
{
    /// <summary>
    /// HC_6_1 测试项：RS-485 通信线路绝缘电阻测试
    /// 测试目的：验证 RS-485 通信线路的绝缘性能
    /// 测试方法：使用万用表测量 485 线路对地的绝缘电阻，要求 ≥500Ω
    /// </summary>
    public class HC_6_1ViewModel : BindableBase
    {
        // 硬件设备 IP 地址
        private const string DmmIpAddress = "192.168.1.13";        // 万用表 IP
        private const string MatrixIpAddress = "192.168.1.3";      // 矩阵开关 IP

        // 程控电源（额外 24V 供电）
        private const string AuxPowerSupplyIpAddress = "192.168.1.16";
        private const double AuxPowerVoltageV = 24.0;
        private const double AuxPowerCurrentA = 1;

        // 矩阵开关槽位配置（用于信号路由）
        private const int MatrixSlotResistanceCh1 = 6;   // 485 线路 1-4 通道
        private const int MatrixSlotResistanceCh2 = 6;   // 485 线路 18-2 通道
        private const int MatrixSlotCommon = 4;          // 公共端（地）

        // 485 继电器控制引脚（PXIe-7131 的 DO29）
        //private const int RelayGroundDoIndex = 26;
        private const int Relay485DoIndex = 28;

        // 测试通过阈值：绝缘电阻 ≥ 500Ω 为合格
        private const double PassThresholdOhm = 500.0;

        private const string DmmTriggerDelayCommand= "TRIG:DEL 0.01";

		private readonly SemaphoreSlim _measureLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _relayLock = new SemaphoreSlim(1, 1);
        private CancellationTokenSource _manualCts;
        private CancellationTokenSource _autoCts;

        private IDmmApi _dmm;
        private IPowerSupplyApi _auxPower;

        private readonly IPxiChassisService _pxiChassisService;
        private readonly ISingleBoardTestContextService _singleBoardTestContext;
        private readonly IHydraulicPowerService _hydraulicPowerService;
        private IJy7131Api _jy7131;
        private bool _isRelay485On;

        private const string TestItemName = "电源阻抗测试";

        private bool _isManualTestRunning;
        private bool _isAutoTestRunning;
        private bool _isManualTestInitializing;
        private bool _isAutoTestInitializing;
        private bool _isManualTestStopping;
        private bool _isAutoTestStopping;
        private bool _canMeasure;
        private bool _isManualTestBusy;
        private bool _isAutoTestBusy;

        private bool _measured14;
        private bool _measured182;
        private bool _manualAborted;

        private string _resistance14Text = "--";
        private string _resistance182Text = "--";

        private double? _resistance14;
        private double? _resistance182;

        private string _lastTestTime = "--";
        private string _lastTestResult = "--";
        private string _previousTestTime = "--";
        private string _previousTestResult = "--";
        private string _currentTestResult = "--";

        public HC_6_1ViewModel(IPxiChassisService pxiChassisService, ISingleBoardTestContextService singleBoardTestContext, IHydraulicPowerService hydraulicPowerService)
        {
            _pxiChassisService = pxiChassisService;
            _singleBoardTestContext = singleBoardTestContext;
            _hydraulicPowerService = hydraulicPowerService;
            ManualTestCommand = new DelegateCommand(async () => await OnManualTestAsync());
            AutoTestCommand = new DelegateCommand(async () => await OnAutoTestAsync());
            Measure14Command = new DelegateCommand(async () => await OnMeasure14Async(), () => CanMeasure14);
            Measure182Command = new DelegateCommand(async () => await OnMeasure182Async(), () => CanMeasure182);
            ClearLogCommand = new DelegateCommand(() => Logs.Clear());

            LoadLastTestResultFromProject();
        }

        private void LoadLastTestResultFromProject()
        {
            var testItemNode = _singleBoardTestContext?.GetCurrentTestItemNode(TestItemName);
            if (testItemNode != null)
            {
                if (!string.IsNullOrWhiteSpace(testItemNode.LastTestTime))
                {
                    _previousTestTime = testItemNode.LastTestTime;
                    RaisePropertyChanged(nameof(PreviousTestTime));
                }

                if (!string.IsNullOrWhiteSpace(testItemNode.LastTestResult))
                {
                    _previousTestResult = testItemNode.LastTestResult;
                    RaisePropertyChanged(nameof(PreviousTestResult));
                }
            }
        }

        public bool IsManualTestBusy
        {
            get => IsManualTestInitializing || IsManualTestStopping;
        }

        public bool IsAutoTestBusy
        {
            get => IsAutoTestInitializing || IsAutoTestStopping;
        }

        public bool IsManualTestInitializing
        {
            get => _isManualTestInitializing;
            private set
            {
                if (SetProperty(ref _isManualTestInitializing, value))
                {
                    RaisePropertyChanged(nameof(IsManualTestBusy));
                    RaisePropertyChanged(nameof(CanStartManualTest));
                    RaisePropertyChanged(nameof(CanStartAutoTest));
                }
            }
        }

        public bool IsAutoTestInitializing
        {
            get => _isAutoTestInitializing;
            private set
            {
                if (SetProperty(ref _isAutoTestInitializing, value))
                {
                    RaisePropertyChanged(nameof(IsAutoTestBusy));
                    RaisePropertyChanged(nameof(CanStartManualTest));
                    RaisePropertyChanged(nameof(CanStartAutoTest));
                }
            }
        }

        public bool IsManualTestStopping
        {
            get => _isManualTestStopping;
            private set
            {
                if (SetProperty(ref _isManualTestStopping, value))
                {
                    RaisePropertyChanged(nameof(IsManualTestBusy));
                    RaisePropertyChanged(nameof(CanStartManualTest));
                    RaisePropertyChanged(nameof(CanStartAutoTest));
                }
            }
        }

        public bool IsAutoTestStopping
        {
            get => _isAutoTestStopping;
            private set
            {
                if (SetProperty(ref _isAutoTestStopping, value))
                {
                    RaisePropertyChanged(nameof(IsAutoTestBusy));
                    RaisePropertyChanged(nameof(CanStartManualTest));
                    RaisePropertyChanged(nameof(CanStartAutoTest));
                }
            }
        }

        private void SaveTestResultToProject()
        {
            var testItemNode = _singleBoardTestContext?.GetCurrentTestItemNode(TestItemName);
            if (testItemNode != null)
            {
                testItemNode.LastTestTime = PreviousTestTime;
                testItemNode.LastTestResult = PreviousTestResult;

                var eventAggregator = ContainerLocator.Container?.Resolve(typeof(IEventAggregator)) as IEventAggregator;
                eventAggregator?.GetEvent<ProjectModifiedEvent>()?.Publish(new ProjectModifiedEventArgs
                {
                    ModificationType = "SingleBoardTestResult",
                    Description = $"单板测试结果已更新: {TestItemName}"
                });
            }
        }

        /// <summary>
        /// 确保 485 继电器处于指定状态
        /// 需要两步操作：1) 开启 PXIe-7131 的 DO29  2) 开启外部 485 继电器板的 K8（第8路）
        /// </summary>
        /// <param name="on">true=开启，false=关闭</param>
        private async Task EnsureRelay485Async(bool on, CancellationToken cancellationToken)
        {
            await _relayLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (on)
                {
                    // 如果已经开启，直接返回
                    if (_isRelay485On)
                    {
                        return;
                    }

                    // 查找 PXIe-7131 板卡
                    var device = FindFirstJy7131Device();
                    if (device == null)
                    {
                        throw new InvalidOperationException("未找到PXIe-7131(JY7131)板卡，无法开启485继电器");
                    }

                    // 创建 7131 API 实例（如果尚未创建）
                    if (_jy7131 == null)
                    {
                        var slot = device is DigitalIODevice dio ? dio.SlotIndex : 0;
                        _jy7131 = new Jy7131Api(device, slot);
                    }

                    // 确保板卡已连接
                    if (!_jy7131.IsConnected)
                    {
                        await _jy7131.ConnectAsync(cancellationToken).ConfigureAwait(false);
                    }

                    // 确保板卡已启动
                    if (!_jy7131.IsRunning)
                    {
                        await _jy7131.SetOutputModeAsync(Jy7131OutputMode.Sinking, cancellationToken).ConfigureAwait(false);
                        await _jy7131.StartAsync(cancellationToken).ConfigureAwait(false);
                    }

                    await _jy7131.SetRelayAsync(7, true, cancellationToken).ConfigureAwait(false);

                    await _jy7131.WriteDoAsync($"DO{Relay485DoIndex}", true, cancellationToken).ConfigureAwait(false);

                    await _jy7131.WriteDoAsync($"DO{29}", true, cancellationToken).ConfigureAwait(false);

                    // 等待继电器吸合稳定
                    await Task.Delay(100, cancellationToken).ConfigureAwait(false);

                    _isRelay485On = true;
                }
                else
                {
                    if (!_isRelay485On)
                    {
                        return;
                    }

                    if (_jy7131 != null)
                    {
                        try
                        {
                            await _jy7131.WriteDoAsync($"DO{Relay485DoIndex}", false, cancellationToken).ConfigureAwait(false);
                            await _jy7131.WriteDoAsync($"DO{29}", false, cancellationToken).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            Log($"复位7131 DO{Relay485DoIndex}失败: {ex.Message}");
                        }

                        try
                        {
                            await _jy7131.SetRelayAsync(7, false, cancellationToken).ConfigureAwait(false);
                            Log($"485继电器板 第8路已关闭");
                        }
                        catch (Exception ex)
                        {
                            Log($"关闭继电器板 第8路失败: {ex.Message}");
                        }
                    }

                    _isRelay485On = false;
                }
            }
            finally
            {
                _relayLock.Release();
            }
        }

        /// <summary>
        /// 从 PXI 机箱中查找第一个 PXIe-7131 板卡
        /// </summary>
        private DeviceBase FindFirstJy7131Device()
        {
            var chassisList = _pxiChassisService?.GetAllChassis();
            if (chassisList == null)
            {
                return null;
            }

            foreach (var chassis in chassisList)
            {
                var device = chassis?.Devices?.FirstOrDefault(d =>
                    d is DigitalIODevice ||
                    (d?.Model?.IndexOf("7131", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.DeviceTypeName?.IndexOf("离散量", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.DeviceTypeName?.IndexOf("数字量", StringComparison.OrdinalIgnoreCase) >= 0));

                if (device != null)
                {
                    return device;
                }
            }

            return null;
        }

        public DelegateCommand ManualTestCommand { get; }
        public DelegateCommand AutoTestCommand { get; }

        public DelegateCommand Measure14Command { get; }

        public DelegateCommand Measure182Command { get; }

        public DelegateCommand ClearLogCommand { get; }

        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();

        public bool IsManualTestRunning
        {
            get => _isManualTestRunning;
            set
            {
                if (SetProperty(ref _isManualTestRunning, value))
                {
                    Measure14Command?.RaiseCanExecuteChanged();
                    Measure182Command?.RaiseCanExecuteChanged();
                    RaisePropertyChanged(nameof(CanMeasure14));
                    RaisePropertyChanged(nameof(CanMeasure182));
                    RaisePropertyChanged(nameof(CanStartManualTest));
                    RaisePropertyChanged(nameof(CanStartAutoTest));
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
                    RaisePropertyChanged(nameof(CanStartManualTest));
                    RaisePropertyChanged(nameof(CanStartAutoTest));
                }
            }
        }

        public bool CanMeasure
        {
            get => _canMeasure;
            private set
            {
                if (SetProperty(ref _canMeasure, value))
                {
                    Measure14Command?.RaiseCanExecuteChanged();
                    Measure182Command?.RaiseCanExecuteChanged();
                    RaisePropertyChanged(nameof(CanMeasure14));
                    RaisePropertyChanged(nameof(CanMeasure182));
                }
            }
        }

        public bool CanMeasure14 => IsManualTestRunning && CanMeasure && !_measured14;
        public bool CanMeasure182 => IsManualTestRunning && CanMeasure && !_measured182;
        public bool CanStartManualTest => !IsManualTestBusy && !IsAutoTestBusy && !IsAutoTestRunning;
        public bool CanStartAutoTest => !IsManualTestBusy && !IsAutoTestBusy && !IsManualTestRunning;

        public async Task<string> RunOnceAsync(CancellationToken cancellationToken)
        {
            if (IsAutoTestRunning)
            {
                await StopAutoTestAsync().ConfigureAwait(false);
            }

            if (IsManualTestRunning)
            {
                await StopManualTestAsync().ConfigureAwait(false);
            }

            _autoCts?.Cancel();
            _autoCts?.Dispose();
            _autoCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            try
            {
                return await ExecuteAutoTestAsync(_autoCts.Token).ConfigureAwait(false);
            }
            finally
            {
                IsAutoTestInitializing = false;
                IsAutoTestStopping = false;
                _autoCts?.Dispose();
                _autoCts = null;
            }
        }

        /// <summary>
        /// 自动测试流程
        /// 自动依次测量两个通道的电阻并判断结果
        /// </summary>
        private async Task OnAutoTestAsync()
        {
            if (IsAutoTestStopping)
            {
                return;
            }

            if (IsAutoTestRunning || IsAutoTestInitializing)
            {
                await StopAutoTestAsync().ConfigureAwait(false);
                return;
            }

            // stop manual mode if it was running
            if (IsManualTestRunning)
            {
                await StopManualTestAsync().ConfigureAwait(false);
            }

            IsAutoTestInitializing = true;
            IsAutoTestStopping = false;
            IsAutoTestRunning = true;
            CanMeasure = false;

            CurrentTestResult = "--";

            _resistance14 = null;
            _resistance182 = null;
            Resistance14Text = "--";
            Resistance182Text = "--";

            _autoCts?.Cancel();
            _autoCts?.Dispose();
            _autoCts = new CancellationTokenSource();

            Log("开始自动测试");
            Log("正在初始化设备...");         

            try
            {
                _ = await ExecuteAutoTestAsync(_autoCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Log("自动测试已停止");
                await StopAutoTestAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log($"自动测试异常: {ex.Message}");
                await StopAutoTestAsync().ConfigureAwait(false);
            }
            finally
            {
                if (IsAutoTestInitializing)
                {
                    IsAutoTestRunning = false;
                }
                IsAutoTestInitializing = false;
                _autoCts?.Dispose();
                _autoCts = null;
            }
        }

        private async Task<string> ExecuteAutoTestAsync(CancellationToken cancellationToken)
        {
            IsAutoTestRunning = true;
            CanMeasure = false;

            CurrentTestResult = "--";

            _resistance14 = null;
            _resistance182 = null;
            Resistance14Text = "--";
            Resistance182Text = "--";

            Log("开始自动测试");
            Log("正在初始化设备...");
            Log($"判据: R14>{PassThresholdOhm:0}Ω && R182>{PassThresholdOhm:0}Ω");

            try
            {
                if (_hydraulicPowerService?.IsHydraulicPowered == true)
                {
                    await _hydraulicPowerService.PowerOffAsync(cancellationToken).ConfigureAwait(false);
                }

                await EnsureAuxPowerAsync(cancellationToken).ConfigureAwait(false);

                await EnsureRelay485Async(on: true, cancellationToken: cancellationToken).ConfigureAwait(false);

                _dmm ??= new DmmSocketApi();
                await _dmm.ConnectAsync(DmmIpAddress, cancellationToken).ConfigureAwait(false);
                await ConfigureDmmAsync(cancellationToken).ConfigureAwait(false);

                IsAutoTestInitializing = false;

                var ok14 = await MeasureResistanceAsync(
                        name: "针脚1-4",
                        connect1: ("I1", "O8", MatrixSlotResistanceCh1),
                        connect2: ("I4", "O2", MatrixSlotCommon),
                        afterSetText: (v, text) => { _resistance14 = v; Resistance14Text = text; },
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (!IsAutoTestRunning)
                {
                    return CurrentTestResult ?? "--";
                }

                _measured14 = true;

                await Task.Delay(120, cancellationToken).ConfigureAwait(false);

                var ok182 = await MeasureResistanceAsync(
                        name: "针脚1-82",
                        connect1: ("I1", "O9", MatrixSlotResistanceCh2),
                        connect2: ("I4", "O2", MatrixSlotCommon),
                        afterSetText: (v, text) => { _resistance182 = v; Resistance182Text = text; },
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (!IsAutoTestRunning)
                {
                    return CurrentTestResult ?? "--";
                }

                _measured182 = true;

                var pass14 = _resistance14 > PassThresholdOhm;
                var pass182 = _resistance182 > PassThresholdOhm;
                var pass = pass14 && pass182;

                await FinalizeIfBothMeasuredAsync(stopAfterFinalize: true, isAutoMode: true).ConfigureAwait(false);
                return pass ? "合格" : "不合格";
            }
            catch (OperationCanceledException)
            {
                Log("自动测试已停止");
                await StopAutoTestAsync().ConfigureAwait(false);
                throw;
            }
            catch (Exception ex)
            {
                Log($"自动测试异常: {ex.Message}");
                await StopAutoTestAsync().ConfigureAwait(false);
                throw;
            }
        }

        public string Resistance14Text
        {
            get => _resistance14Text;
            private set => SetProperty(ref _resistance14Text, value);
        }

        public string Resistance182Text
        {
            get => _resistance182Text;
            private set => SetProperty(ref _resistance182Text, value);
        }

        public bool IsResistance14Pass => _resistance14.HasValue && _resistance14.Value > PassThresholdOhm;

        public bool IsResistance182Pass => _resistance182.HasValue && _resistance182.Value > PassThresholdOhm;

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

        public string CurrentTestResult
        {
            get => _currentTestResult;
            set => SetProperty(ref _currentTestResult, value);
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

        /// <summary>
        /// 手动测试流程
        /// 用户可以手动点击按钮分别测量两个通道的电阻
        /// </summary>
        private async Task OnManualTestAsync()
        {
            if (IsManualTestStopping)
            {
                return;
            }

            if (IsManualTestRunning || IsManualTestInitializing)
            {
                await StopManualTestAsync().ConfigureAwait(false);
                return;
            }

            IsAutoTestRunning = false;
            IsManualTestRunning = true;
            IsManualTestInitializing = true;
            IsManualTestStopping = false;
            CanMeasure = false;
            CurrentTestResult = "--";

            _manualAborted = false;
            _measured14 = false;
            _measured182 = false;
            RaisePropertyChanged(nameof(CanMeasure14));
            RaisePropertyChanged(nameof(CanMeasure182));

            _resistance14 = null;
            _resistance182 = null;
            Resistance14Text = "--";
            Resistance182Text = "--";

            _manualCts?.Cancel();
            _manualCts?.Dispose();
            _manualCts = new CancellationTokenSource();

            if (_hydraulicPowerService?.IsHydraulicPowered == true)
            {
                await _hydraulicPowerService.PowerOffAsync(_manualCts.Token).ConfigureAwait(false);
            }

            Log("开始手动测试");

            try
            {
                await EnsureAuxPowerAsync(_manualCts.Token).ConfigureAwait(false);

                await EnsureRelay485Async(on: true, cancellationToken: _manualCts.Token).ConfigureAwait(false);

                _dmm ??= new DmmSocketApi();
                await _dmm.ConnectAsync(DmmIpAddress, _manualCts.Token).ConfigureAwait(false);
                await ConfigureDmmAsync(_manualCts.Token).ConfigureAwait(false);

                IsManualTestInitializing = false;
                CanMeasure = true;
            }
            catch (Exception ex)
            {
                await AbortManualTestAsync($"万用表连接失败，手动测试中止: {ex.Message}").ConfigureAwait(false);
            }
        }

        private async Task AbortManualTestAsync(string reason)
        {
            _manualAborted = true;
            if (!string.IsNullOrWhiteSpace(reason))
            {
                Log(reason);
            }

            await StopManualTestAsync().ConfigureAwait(false);
        }

        private async Task AbortAutoTestAsync(string reason)
        {
            if (!string.IsNullOrWhiteSpace(reason))
            {
                Log(reason);
            }

            await StopAutoTestAsync().ConfigureAwait(false);
        }

        private async Task StopManualTestAsync()
        {
            if (IsManualTestStopping)
            {
                return;
            }

            IsManualTestInitializing = false;
            IsManualTestStopping = true;
            try
            {
                CanMeasure = false;
                _manualCts?.Cancel();
            }
            catch
            {
            }

            Log("手动测试停止/结束，正在断开设备...");

            try
            {
                if (_dmm != null)
                {
                    await _dmm.DisconnectAsync().ConfigureAwait(false);
                }
            }
            catch
            {
            }

            try
            {
                await EnsureRelay485Async(on: false, cancellationToken: CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
            }

            try
            {
                await DisconnectAllMatrixRoutesAsync().ConfigureAwait(false);
            }
            catch
            {
            }

            try
            {
                await CleanupJy7131Async().ConfigureAwait(false);
            }
            catch
            {
            }

            try
            {
                await CleanupAuxPowerAsync().ConfigureAwait(false);
            }
            catch
            {
            }

            IsManualTestRunning = false;
            IsManualTestInitializing = false;
            IsManualTestStopping = false;
            Log("手动测试已结束");
        }

        private async Task StopAutoTestAsync()
        {
            if (IsAutoTestStopping)
            {
                return;
            }

            IsAutoTestInitializing = false;
            IsAutoTestStopping = true;
            try
            {
                _autoCts?.Cancel();
            }
            catch
            {
            }

            Log("自动测试停止/结束，正在设备...");

            try
            {
                if (_dmm != null)
                {
                    await _dmm.DisconnectAsync().ConfigureAwait(false);
                }
            }
            catch
            {
            }

            try
            {
                await EnsureRelay485Async(on: false, cancellationToken: CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
            }

            try
            {
                await DisconnectAllMatrixRoutesAsync().ConfigureAwait(false);
            }
            catch
            {
            }

            await CleanupJy7131Async().ConfigureAwait(false);

            try
            {
                await CleanupAuxPowerAsync().ConfigureAwait(false);
            }
            catch
            {
            }

            IsAutoTestRunning = false;
            IsAutoTestInitializing = false;
            IsAutoTestStopping = false;
            Log("自动测试已结束");
        }

        private async Task EnsureAuxPowerAsync(CancellationToken cancellationToken)
        {
            _auxPower ??= new PowerSupplySocketApi();
            if (!_auxPower.IsConnected)
            {
                await _auxPower.ConnectAsync(AuxPowerSupplyIpAddress, cancellationToken).ConfigureAwait(false);
            }

            await _auxPower.ApplyAsync(PowerSupplyChannel.CH1, AuxPowerVoltageV, AuxPowerCurrentA, cancellationToken).ConfigureAwait(false);
            await _auxPower.SetOutputEnabledAsync(PowerSupplyChannel.CH1, true, cancellationToken).ConfigureAwait(false);
            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        }

        private async Task CleanupAuxPowerAsync()
        {
            try
            {
                if (_auxPower != null)
                {
                    try { await _auxPower.SetOutputEnabledAsync(PowerSupplyChannel.CH1, false, CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _auxPower.DisconnectAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _auxPower.DisposeAsync().ConfigureAwait(false); } catch { }
                }
            }
            finally
            {
                _auxPower = null;
            }
        }

        private async Task CleanupJy7131Async()
        {
            try
            {
                if (_jy7131 != null)
                {
                    try { await _jy7131.StopAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _jy7131.DisconnectAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _jy7131.DisposeAsync().ConfigureAwait(false); } catch { }
                }
            }
            catch
            {
            }
            finally
            {
                _jy7131 = null;
                _isRelay485On = false;
            }
        }

        /// <summary>
        /// 测量 1-4 通道的绝缘电阻（手动模式）
        /// </summary>
        private async Task OnMeasure14Async()
        {
            var ok = await MeasureResistanceAsync(
                name: "针脚1-4",
                connect1: ("I1", "O8", MatrixSlotResistanceCh1),
                connect2: ("I4", "O2", MatrixSlotCommon),
                afterSetText: (v, text) => { _resistance14 = v; Resistance14Text = text; },
                cancellationToken: _manualCts?.Token ?? CancellationToken.None).ConfigureAwait(false);

            if (!IsManualTestRunning || _manualAborted)
            {
                return;
            }

            _measured14 = true;
            RaisePropertyChanged(nameof(CanMeasure14));
            Measure14Command?.RaiseCanExecuteChanged();

            await TryFinalizeIfBothMeasuredAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 测量 18-2 通道的绝缘电阻（手动模式）
        /// </summary>
        private async Task OnMeasure182Async()
        {
            var ok = await MeasureResistanceAsync(
                name: "针脚1-82",
                connect1: ("I1", "O9", MatrixSlotResistanceCh2),
                connect2: ("I4", "O2", MatrixSlotCommon),
                afterSetText: (v, text) => { _resistance182 = v; Resistance182Text = text; },
                cancellationToken: _manualCts?.Token ?? CancellationToken.None).ConfigureAwait(false);

            if (!IsManualTestRunning || _manualAborted)
            {
                return;
            }

            _measured182 = true;
            RaisePropertyChanged(nameof(CanMeasure182));
            Measure182Command?.RaiseCanExecuteChanged();

            await TryFinalizeIfBothMeasuredAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 测量指定通道的绝缘电阻（核心测量方法）
        /// 流程：1) 配置矩阵开关连接信号路径  2) 使用万用表测量电阻  3) 判断结果
        /// </summary>
        /// <param name="name">通道名称（用于日志）</param>
        /// <param name="connect1">矩阵连接1（信号端）</param>
        /// <param name="connect2">矩阵连接2（地端）</param>
        /// <param name="afterSetText">测量完成后的回调（更新界面显示）</param>
        /// <returns>true=测量成功，false=测量失败</returns>
        private async Task<bool> MeasureResistanceAsync(
            string name,
            (string In, string Out, int Slot) connect1,
            (string In, string Out, int Slot) connect2,
            Action<double?, string> afterSetText,
            CancellationToken cancellationToken)
        {
            if (!(IsAutoTestRunning || (IsManualTestRunning && CanMeasure)))
            {
                Log($"{name}: 当前未处于测试状态");
                return false;
            }

            await _measureLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                Log($"{name}: 开始测量阻抗");

                // 配置矩阵开关，连接测量信号路径
                var matrix = MatrixControlService.Instance;

                var ok1 = await matrix.ConnectNodesAsync(connect1.In, connect1.Out, connect1.Slot, MatrixIpAddress).ConfigureAwait(false);
                var ok2 = await matrix.ConnectNodesAsync(connect2.In, connect2.Out, connect2.Slot, MatrixIpAddress).ConfigureAwait(false);
                if (!ok1 || !ok2)
                {
                    afterSetText(null, "--");
                    if (IsManualTestRunning)
                    {
                        await AbortManualTestAsync($"{name}: 矩阵连接失败，手动测试中止").ConfigureAwait(false);
                    }
                    else if (IsAutoTestRunning)
                    {
                        await AbortAutoTestAsync($"{name}: 矩阵连接失败，自动测试中止").ConfigureAwait(false);
                    }

                    return false;
                }

                await Task.Delay(500, cancellationToken).ConfigureAwait(false);

                // 使用万用表测量电阻（超时时间 8 秒）
                DmmReading reading = null;
                try
                {
                    reading = await _dmm.ReadOnceAsync(DmmMeasureMode.RES, new DmmReadOptions { TimeoutMilliseconds = 8000 }, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Log($"{name}: 电阻采集异常: {ex.Message}");
                    if (IsManualTestRunning)
                    {
                        await AbortManualTestAsync($"{name}: 电阻采集异常，手动测试中止").ConfigureAwait(false);
                    }
                    else if (IsAutoTestRunning)
                    {
                        await AbortAutoTestAsync($"{name}: 电阻采集异常，自动测试中止").ConfigureAwait(false);
                    }

                    return false;
                }

                if (reading == null)
                {
                    afterSetText(null, "--");
                    if (IsManualTestRunning)
                    {
                        await AbortManualTestAsync($"{name}: 电阻读数为空，手动测试中止").ConfigureAwait(false);
                    }
                    else if (IsAutoTestRunning)
                    {
                        await AbortAutoTestAsync($"{name}: 电阻读数为空，自动测试中止").ConfigureAwait(false);
                    }

                    return false;
                }

                // 检查是否超量程（OL = Over Load）
                if (reading.IsOverrange)
                {
                    afterSetText(double.PositiveInfinity, "OL");
                    Log($"{name}: 读数为OL(过量程)");
                    return true;
                }

                if (reading.Value == null)
                {
                    afterSetText(null, "--");
                    if (IsManualTestRunning)
                    {
                        await AbortManualTestAsync($"{name}: 电阻读数无效，手动测试中止").ConfigureAwait(false);
                    }
                    else if (IsAutoTestRunning)
                    {
                        await AbortAutoTestAsync($"{name}: 电阻读数无效，自动测试中止").ConfigureAwait(false);
                    }

                    return false;
                }

                // 提取并格式化测量结果
                var value = reading.Value;
                var text = FormatOhmText(reading);
                afterSetText(value, text);

                Log($"{name}: 阻抗={text}");

                return true;
            }
            finally
            {
                try
                {
                    await DisconnectAllMatrixRoutesAsync().ConfigureAwait(false);
                }
                catch
                {
                }

                _measureLock.Release();
            }
        }

        private static string FormatOhmText(DmmReading reading)
        {
            if (reading == null)
                return "--";

            if (reading.IsOverrange)
                return "OL";

            if (reading.Value == null)
                return "--";

            var value = reading.Value.Value;
            if (Math.Abs(value) >= 1_000_000.0)
                return $"{value / 1_000_000.0:0.000} MΩ";

            if (Math.Abs(value) >= 1000.0)
                return $"{value / 1000.0:0.000} kΩ";

            return $"{value:0.000} Ω";
        }

        private async Task ConfigureDmmAsync(CancellationToken cancellationToken)
        {
            if (_dmm == null)
                return;

            await _dmm.SendAsync(DmmTriggerDelayCommand, cancellationToken).ConfigureAwait(false);
        }

        private async Task DisconnectAllMatrixRoutesAsync()
        {
            var matrix = MatrixControlService.Instance;

            // disconnect all nodes that might be used by this test
            _ = await matrix.DisconnectNodesAsync("I1", "O8", MatrixSlotResistanceCh1, MatrixIpAddress).ConfigureAwait(false);
            _ = await matrix.DisconnectNodesAsync("I1", "O9", MatrixSlotResistanceCh2, MatrixIpAddress).ConfigureAwait(false);
            _ = await matrix.DisconnectNodesAsync("I4", "O2", MatrixSlotCommon, MatrixIpAddress).ConfigureAwait(false);
        }

        private async Task TryFinalizeIfBothMeasuredAsync()
        {
            await FinalizeIfBothMeasuredAsync(stopAfterFinalize: true, isAutoMode: false).ConfigureAwait(false);
        }

        private async Task FinalizeIfBothMeasuredAsync(bool stopAfterFinalize, bool isAutoMode)
        {
            if (!isAutoMode && _manualAborted)
            {
                return;
            }

            if (_resistance14 == null || _resistance182 == null)
            {
                return;
            }

            var pass14 = _resistance14 > PassThresholdOhm;
            var pass182 = _resistance182 > PassThresholdOhm;
            var pass = pass14 && pass182;

            Log($"判据: R14>{PassThresholdOhm:0}Ω && R182>{PassThresholdOhm:0}Ω");
            Log($"针脚1-4={Resistance14Text} => {(pass14 ? "合格" : "不合格")}");
            Log($"针脚1-82={Resistance182Text} => {(pass182 ? "合格" : "不合格")}");

            var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            var resultText = pass ? "合格" : "不合格";
            CurrentTestResult = resultText;
            PreviousTestTime = now;
            PreviousTestResult = resultText;
            Log($"测试结果: {resultText}");

            SaveTestResultToProject();

            if (!stopAfterFinalize)
            {
                return;
            }

            if (isAutoMode)
            {
                await StopAutoTestAsync().ConfigureAwait(false);
            }
            else
            {
                await StopManualTestAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// 记录日志到界面
        /// </summary>
        private void Log(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            var text = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.Invoke(() => Logs.Add(text));
                return;
            }

            Logs.Add(text);
        }
    }
}
