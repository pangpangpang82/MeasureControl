using Prism.Commands;
using Prism.Mvvm;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using MeasureControl.Views.Dialogs;
using MeasureControl.Drivers;
using MeasureControl.Events;
using MeasureControl.Models.Devices;
using MeasureControl.Models.Devices.DeviceCategories;
using MeasureControl.Services;
using MeasureControl.Services.HardwareApis;
using Prism.Events;
using Prism.Ioc;

namespace MeasureControl.ViewModels.SingleBoardTest.HydraulicController
{
    /// <summary>
    /// HC_6_5 测试项：压力信号测试（压力传感器校验）
    /// 测试目的：通过 MTX532 模拟量输出板卡输出特定电压，模拟压力传感器信号，
    ///          再从 ARINC429 总线接收压力数据，验证三路压力系统（SDI0/1/2）是否正确。
    /// 测试方法：
    /// 1) 给被测板供电 28V。
    /// 2) MTX532 的 AO1/AO2/AO3 同时输出指定电压（点1: 0.5V, 点2: 7.17V, 点3: 3.0V）。
    /// 3) 从 ARINC429 接收压力 Label=174(oct)（十进制 124）数据，分别统计 SDI0/1/2（对应 SYS1/2/3）。
    /// 4) 每路采集 5 帧有效数据取平均值，并与阈值范围比对，给出“PASS/FAIL”。
    /// </summary>
    public class HC_6_5ViewModel : BindableBase
    {
        // 电源配置（给被测板供电）
        private const string PowerSupplyIpAddress = "192.168.1.15";
        private const double InputVoltageV = 28.0;
        private const double InputCurrentA = 1;

        // ARINC429 接收配置
        private const int RxChannelIndex = 2;
        private const double ArincRate = 100000.0;

        // 压力数据定义（Label=174(oct) 即十进制 124）
        private const byte PressLabelDec = 124; // 174(oct)
        private const string TestItemName = "压力信号测试";
        private const int PressureBitLength = 12;

        // 协议规定 SSM=0 为正常数据
        private const byte SsmNormal = 3;

        // 采样参数
        private const int SamplesPerMeasure = 1;      // 每路采集 3 帧取平均
        private const int SampleTimeoutMs = 3000;     // 采样超时 5 秒
        private const int AoSettleMs = 800;            // 模拟量输出稳定等待时间
        private const int Mtx532ReadyTimeoutMs = 6000;
        private const int Mtx532ReadyPollMs = 200;
        private const int PostSwitchRxFlushMs = 120;

        // 三个测试点对应的模拟电压（由 MTX532 输出到压力传感器模拟通道）
        private const double Point1VoltageV = 0.5;    // 点1: 0.5V
        private const double Point2VoltageV = 7.17;   // 点2: 7.17V
        private const double Point3VoltageV = 3.0;    // 点3: 3.0V

        private readonly SemaphoreSlim _measureLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _relayLock = new SemaphoreSlim(1, 1);
        private readonly IPxiChassisService _pxiChassisService;
        private readonly ISingleBoardTestContextService _singleBoardTestContext;
        private readonly IBoardPowerService _boardPowerService;

        private CancellationTokenSource _manualCts;
        private CancellationTokenSource _autoCts;

        private IPowerSupplyApi _power;
        private IArt4229Api _arinc;
        private IMtx532Api _mtx532;
        private Jy7131Api _jy7131;
        private bool _isRelay485On;

        private const int Relay485ChannelIndex = 6;
        private const int RelayAuxDoIndex = 25;
        //private const int RelayGroundDoIndex = 26;

        private bool _measured1;
        private bool _measured2;
        private bool _measured3;
        private bool _manualAborted;
        private bool _historyLoaded;

        private bool _isManualTestRunning;
        private bool _isAutoTestRunning;
        private bool _isManualTestInitializing;
        private bool _isAutoTestInitializing;
        private bool _isManualTestStopping;
        private bool _isAutoTestStopping;
        private bool _canMeasure;

        private string _lastTestTime = "--";
        private string _lastTestResult = "--";
        private string _previousTestTime = "--";
        private string _previousTestResult = "--";
        private string _currentTestResult = "--";

        private string _p1Sys1Text = "--";
        private string _p1Sys2Text = "--";
        private string _p1Sys3Text = "--";

        private string _p2Sys1Text = "--";
        private string _p2Sys2Text = "--";
        private string _p2Sys3Text = "--";

        private string _p3Sys1Text = "--";
        private string _p3Sys2Text = "--";
        private string _p3Sys3Text = "--";
        private string _customSys1Text = "--";
        private string _customSys2Text = "--";
        private string _customSys3Text = "--";
        private string _customVoltageInput = "3.0";

        private double? _p1Sys1;
        private double? _p1Sys2;
        private double? _p1Sys3;
        private double? _scriptPressureSys1;
        private double? _scriptPressureSys2;
        private double? _scriptPressureSys3;
        private double? _p2Sys1;
        private double? _p2Sys2;
        private double? _p2Sys3;
        private double? _p3Sys1;
        private double? _p3Sys2;
        private double? _p3Sys3;

        public HC_6_5ViewModel(IPxiChassisService pxiChassisService, ISingleBoardTestContextService singleBoardTestContext, IBoardPowerService hydraulicPowerService)
        {
            _pxiChassisService = pxiChassisService;
            _singleBoardTestContext = singleBoardTestContext;
            _boardPowerService = hydraulicPowerService;

            ManualTestCommand = new DelegateCommand(async () => await OnManualTestAsync());
            AutoTestCommand = new DelegateCommand(async () => await OnAutoTestAsync());

            MeasurePoint1Command = new DelegateCommand(async () => await OnMeasurePoint1Async(), () => CanMeasurePoint1);
            MeasurePoint2Command = new DelegateCommand(async () => await OnMeasurePoint2Async(), () => CanMeasurePoint2);
            MeasurePoint3Command = new DelegateCommand(async () => await OnMeasurePoint3Async(), () => CanMeasurePoint3);
            MeasureCustomPointCommand = new DelegateCommand(async () => await OnMeasureCustomPointAsync(), () => CanMeasureCustomPoint);

            ClearLogCommand = new DelegateCommand(() => Logs.Clear());

            LoadLastTestResultFromProject();
            SubscribeStopEvent();
        }

        private void SubscribeStopEvent()
        {
            var ea = ContainerLocator.Container?.Resolve(typeof(IEventAggregator)) as IEventAggregator;
            ea?.GetEvent<RequestStopHydraulicTestsEvent>().Subscribe(OnRequestStopAllTests);
        }

        private void OnRequestStopAllTests(RequestStopHydraulicTestsEventArgs args)
        {
            if (_isManualTestRunning || _isManualTestInitializing)
                args.StopTasks.Add(StopManualTestAsync());
            if (_isAutoTestRunning || _isAutoTestInitializing)
                args.StopTasks.Add(StopAutoTestAsync());
        }

        private void LoadLastTestResultFromProject()
        {
            if (_historyLoaded)
                return;

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

                _historyLoaded = true;
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

        public string CurrentTestResult
        {
            get => _currentTestResult;
            private set => SetProperty(ref _currentTestResult, value);
        }

        public DelegateCommand ManualTestCommand { get; }
        public DelegateCommand AutoTestCommand { get; }

        public DelegateCommand MeasurePoint1Command { get; }
        public DelegateCommand MeasurePoint2Command { get; }
        public DelegateCommand MeasurePoint3Command { get; }
        public DelegateCommand MeasureCustomPointCommand { get; }

        public DelegateCommand ClearLogCommand { get; }

        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();

        public bool IsManualTestBusy => IsManualTestInitializing || IsManualTestStopping;

        public bool IsAutoTestBusy => IsAutoTestInitializing || IsAutoTestStopping;

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

        public bool IsManualTestRunning
        {
            get => _isManualTestRunning;
            private set
            {
                if (SetProperty(ref _isManualTestRunning, value))
                {
                    RaisePropertyChanged(nameof(CanStartManualTest));
                    RaisePropertyChanged(nameof(CanStartAutoTest));
                    RefreshMeasureCommands();
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
                    RaisePropertyChanged(nameof(CanStartManualTest));
                    RaisePropertyChanged(nameof(CanStartAutoTest));
                    RefreshMeasureCommands();
                }
            }
        }

        public bool CanMeasure
        {
            get => _canMeasure;
            private set
            {
                if (SetProperty(ref _canMeasure, value))
                    RefreshMeasureCommands();
            }
        }

        public bool CanMeasurePoint1 => IsManualTestRunning && CanMeasure;
        public bool CanMeasurePoint2 => IsManualTestRunning && CanMeasure;
        public bool CanMeasurePoint3 => IsManualTestRunning && CanMeasure;
        public bool CanMeasureCustomPoint => IsManualTestRunning && CanMeasure && TryGetValidatedCustomVoltage(out _);
        public bool CanStartManualTest => !IsManualTestBusy && !IsAutoTestBusy && !IsAutoTestRunning;
        public bool CanStartAutoTest => !IsManualTestBusy && !IsAutoTestBusy && !IsManualTestRunning;

        private void RefreshMeasureCommands()
        {
            RaisePropertyChanged(nameof(CanMeasurePoint1));
            RaisePropertyChanged(nameof(CanMeasurePoint2));
            RaisePropertyChanged(nameof(CanMeasurePoint3));
            RaisePropertyChanged(nameof(CanMeasureCustomPoint));
            MeasurePoint1Command?.RaiseCanExecuteChanged();
            MeasurePoint2Command?.RaiseCanExecuteChanged();
            MeasurePoint3Command?.RaiseCanExecuteChanged();
            MeasureCustomPointCommand?.RaiseCanExecuteChanged();
        }

        /// <summary>
        /// 整板串行自动测试入口。
        /// 由外部(整板自动测试)调用，支持 await 等待完成，并通过 CancellationToken 实现“立即停止当前测量”。
        /// 返回值仅为“PASS/FAIL”。
        /// </summary>
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
                _autoCts?.Dispose();
                _autoCts = null;
            }
        }

        public async Task RunWithScriptVoltagesAsync(double v1, double v2, double v3, CancellationToken cancellationToken)
        {
            if (IsAutoTestRunning)
                await StopAutoTestAsync().ConfigureAwait(false);
            if (IsManualTestRunning)
                await StopManualTestAsync().ConfigureAwait(false);

            _autoCts?.Cancel();
            _autoCts?.Dispose();
            _autoCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            try
            {
                await ExecuteScriptVoltagesTestAsync(v1, v2, v3, _autoCts.Token).ConfigureAwait(false);
            }
            finally
            {
                IsAutoTestInitializing = false;
                _autoCts?.Dispose();
                _autoCts = null;
            }
        }

        private async Task ExecuteScriptVoltagesTestAsync(double v1, double v2, double v3, CancellationToken cancellationToken)
        {
            _scriptPressureSys1 = null;
            _scriptPressureSys2 = null;
            _scriptPressureSys3 = null;
            IsAutoTestInitializing = true;
            IsAutoTestStopping = false;
            CanMeasure = false;
            Log($"脚本压力测试: SYS1={v1:0.##}V, SYS2={v2:0.##}V, SYS3={v3:0.##}V");

            try
            {
                await EnsureRelay485Async(on: true, cancellationToken: cancellationToken).ConfigureAwait(false);
                await EnsureGroundDoAsync(on: true, cancellationToken: cancellationToken).ConfigureAwait(false);
                await EnsureArincRxAsync(cancellationToken).ConfigureAwait(false);
                await EnsureMtx532Async(cancellationToken).ConfigureAwait(false);
                await EnsurePowerAsync(cancellationToken).ConfigureAwait(false);
                IsAutoTestInitializing = false;
                IsAutoTestRunning = true;

                CustomPressureSys1Text = "--";
                CustomPressureSys2Text = "--";
                CustomPressureSys3Text = "--";
                await MeasurePointAllSystemsIndependentAsync(v1, v2, v3,
                    setSys1: t => CustomPressureSys1Text = t,
                    setSys2: t => CustomPressureSys2Text = t,
                    setSys3: t => CustomPressureSys3Text = t,
                    setV1: val => _scriptPressureSys1 = val,
                    setV2: val => _scriptPressureSys2 = val,
                    setV3: val => _scriptPressureSys3 = val,
                    cancellationToken).ConfigureAwait(false);

                await StopAutoTestAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Log("脚本压力测试已停止");
                await StopAutoTestAsync().ConfigureAwait(false);
                throw;
            }
            catch (Exception ex)
            {
                Log($"脚本压力测试异常: {ex.Message}");
                await StopAutoTestAsync().ConfigureAwait(false);
                throw;
            }
        }

        private async Task<bool> MeasurePointAllSystemsIndependentAsync(
            double v1, double v2, double v3,
            Action<string> setSys1, Action<string> setSys2, Action<string> setSys3,
            Action<double?> setV1, Action<double?> setV2, Action<double?> setV3,
            CancellationToken cancellationToken)
        {
            if (!IsAutoTestRunning && !IsManualTestRunning)
            {
                Log("脚本分系统压力: 当前未处于测试状态");
                return false;
            }

            await _measureLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await SetAo012IndependentAsync(v1, v2, v3, cancellationToken).ConfigureAwait(false);
                await Task.Delay(AoSettleMs, cancellationToken).ConfigureAwait(false);
                _ = await _arinc.ReadRxWordsAsync(RxChannelIndex, maxCount: 4096, enableTimeTag: false, enableRateAdaption: false, cancellationToken: cancellationToken).ConfigureAwait(false);
                await Task.Delay(PostSwitchRxFlushMs, cancellationToken).ConfigureAwait(false);

                return await MeasureAllSystemsAsync(
                    "脚本分系统",
                    setSys1, setSys2, setSys3,
                    setV1, setV2, setV3,
                    cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _measureLock.Release();
            }
        }

        private async Task SetAo012IndependentAsync(double v1, double v2, double v3, CancellationToken cancellationToken)
        {
            if (_mtx532 == null || !_mtx532.IsConnected)
                throw new InvalidOperationException("MTX532未连接");

            await _mtx532.WriteOnceDcAsync(new System.Collections.Generic.Dictionary<string, double>
            {
                ["AO0"] = v1,
                ["AO1"] = 0.0,
                ["AO2"] = v2,
                ["AO3"] = 0.0,
                ["AO4"] = v3,
                ["AO5"] = 0.0,
            }, cancellationToken).ConfigureAwait(false);
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

        public string PressurePoint1Sys1Text
        {
            get => _p1Sys1Text;
            private set => SetProperty(ref _p1Sys1Text, value);
        }

        public string PressurePoint1Sys2Text
        {
            get => _p1Sys2Text;
            private set => SetProperty(ref _p1Sys2Text, value);
        }

        public string PressurePoint1Sys3Text
        {
            get => _p1Sys3Text;
            private set => SetProperty(ref _p1Sys3Text, value);
        }

        public string PressurePoint2Sys1Text
        {
            get => _p2Sys1Text;
            private set => SetProperty(ref _p2Sys1Text, value);
        }

        public string PressurePoint2Sys2Text
        {
            get => _p2Sys2Text;
            private set => SetProperty(ref _p2Sys2Text, value);
        }

        public string PressurePoint2Sys3Text
        {
            get => _p2Sys3Text;
            private set => SetProperty(ref _p2Sys3Text, value);
        }

        public string PressurePoint3Sys1Text
        {
            get => _p3Sys1Text;
            private set => SetProperty(ref _p3Sys1Text, value);
        }

        public string PressurePoint3Sys2Text
        {
            get => _p3Sys2Text;
            private set => SetProperty(ref _p3Sys2Text, value);
        }

        public string PressurePoint3Sys3Text
        {
            get => _p3Sys3Text;
            private set => SetProperty(ref _p3Sys3Text, value);
        }

        public string CustomPressureSys1Text
        {
            get => _customSys1Text;
            private set => SetProperty(ref _customSys1Text, value);
        }

        public string CustomPressureSys2Text
        {
            get => _customSys2Text;
            private set => SetProperty(ref _customSys2Text, value);
        }

        public string CustomPressureSys3Text
        {
            get => _customSys3Text;
            private set => SetProperty(ref _customSys3Text, value);
        }

        public double? ScriptPressureSys1Value => _scriptPressureSys1;

        public double? ScriptPressureSys2Value => _scriptPressureSys2;

        public double? ScriptPressureSys3Value => _scriptPressureSys3;

        public string CustomVoltageInput
        {
            get => _customVoltageInput;
            set
            {
                var normalized = NormalizeVoltageInput(value);
                if (SetProperty(ref _customVoltageInput, normalized))
                {
                    RaisePropertyChanged(nameof(CanMeasureCustomPoint));
                    MeasureCustomPointCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        public double? PressurePoint1Sys1Value => _p1Sys1;

        public double? PressurePoint1Sys2Value => _p1Sys2;

        public double? PressurePoint1Sys3Value => _p1Sys3;

        public double? PressurePoint2Sys1Value => _p2Sys1;

        public double? PressurePoint2Sys2Value => _p2Sys2;

        public double? PressurePoint2Sys3Value => _p2Sys3;

        public double? PressurePoint3Sys1Value => _p3Sys1;

        public double? PressurePoint3Sys2Value => _p3Sys2;

        public double? PressurePoint3Sys3Value => _p3Sys3;

        public bool IsPressurePoint1Sys1Pass => IsPressureWithinRange(_p1Sys1, 0.0, 85.0);

        public bool IsPressurePoint1Sys2Pass => IsPressureWithinRange(_p1Sys2, 0.0, 85.0);

        public bool IsPressurePoint1Sys3Pass => IsPressureWithinRange(_p1Sys3, 0.0, 85.0);

        public bool IsPressurePoint2Sys1Pass => IsPressureWithinRange(_p2Sys1, 3915.0, 4000.0);

        public bool IsPressurePoint2Sys2Pass => IsPressureWithinRange(_p2Sys2, 3915.0, 4000.0);

        public bool IsPressurePoint2Sys3Pass => IsPressureWithinRange(_p2Sys3, 3915.0, 4000.0);

        public bool IsPressurePoint3Sys1Pass => IsPressureWithinRange(_p3Sys1, 1414.0, 1585.0);

        public bool IsPressurePoint3Sys2Pass => IsPressureWithinRange(_p3Sys2, 1414.0, 1585.0);

        public bool IsPressurePoint3Sys3Pass => IsPressureWithinRange(_p3Sys3, 1414.0, 1585.0);

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

            if (IsAutoTestRunning)
            {
                await StopAutoTestAsync().ConfigureAwait(false);
            }

            IsManualTestInitializing = true;
            IsManualTestStopping = false;
            CurrentTestResult = "--";
            PreviousTestTime = "--";
            CanMeasure = false;
            _manualAborted = false;

            _measured1 = false;
            _measured2 = false;
            _measured3 = false;

            ResetPointDisplays();

            _manualCts?.Cancel();
            _manualCts?.Dispose();
            _manualCts = new CancellationTokenSource();

            Log("开始手动测试");
            Log("正在初始化设备...");


            try
            {
                await EnsureRelay485Async(on: true, cancellationToken: _manualCts.Token).ConfigureAwait(false);
                await EnsureGroundDoAsync(on: true, cancellationToken: _manualCts.Token).ConfigureAwait(false);
                await EnsureArincRxAsync(_manualCts.Token).ConfigureAwait(false);
                await EnsureMtx532Async(_manualCts.Token).ConfigureAwait(false);
                await EnsurePowerAsync(_manualCts.Token).ConfigureAwait(false);

                IsManualTestInitializing = false;
                IsManualTestRunning = true;
                CanMeasure = true;
                Log("手动测试初始化完成，可点击三档固定电压或输入自定义电压测量压力");
            }
            catch (Exception ex)
            {
                await AbortManualTestAsync($"手动测试初始化失败，中止: {ex.Message}").ConfigureAwait(false);
            }
        }

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

            if (IsManualTestRunning)
            {
                await StopManualTestAsync().ConfigureAwait(false);
            }

            IsAutoTestInitializing = true;
            IsAutoTestStopping = false;
            CurrentTestResult = "--";
            PreviousTestTime = "--";
            CanMeasure = false;
            _manualAborted = false;

            _measured1 = false;
            _measured2 = false;
            _measured3 = false;

            ResetPointDisplays();

            _autoCts?.Cancel();
            _autoCts?.Dispose();
            _autoCts = new CancellationTokenSource();

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
                _autoCts?.Dispose();
                _autoCts = null;
            }
        }

        private async Task<string> ExecuteAutoTestAsync(CancellationToken cancellationToken)
        {
            PreviousTestTime = "--";
            CanMeasure = false;
            _manualAborted = false;

            _measured1 = false;
            _measured2 = false;
            _measured3 = false;

            ResetPointDisplays();

            Log("开始自动测试");

            try
            {
                await EnsureRelay485Async(on: true, cancellationToken: cancellationToken).ConfigureAwait(false);
                await EnsureGroundDoAsync(on: true, cancellationToken: cancellationToken).ConfigureAwait(false);
                await EnsureArincRxAsync(cancellationToken).ConfigureAwait(false);
                await EnsureMtx532Async(cancellationToken).ConfigureAwait(false);
                await EnsurePowerAsync(cancellationToken).ConfigureAwait(false);

                IsAutoTestInitializing = false;
                IsAutoTestRunning = true;

                var ok1 = await MeasurePointAllSystemsAsync("0.5V", Point1VoltageV,
                    setSys1: t => PressurePoint1Sys1Text = t,
                    setSys2: t => PressurePoint1Sys2Text = t,
                    setSys3: t => PressurePoint1Sys3Text = t,
                    setV1: v => _p1Sys1 = v,
                    setV2: v => _p1Sys2 = v,
                    setV3: v => _p1Sys3 = v,
                    cancellationToken).ConfigureAwait(false);
                if (!IsAutoTestRunning)
                    return CurrentTestResult ?? "--";

                _measured1 = true;

                await Task.Delay(80, cancellationToken).ConfigureAwait(false);

                var ok2 = await MeasurePointAllSystemsAsync("7.17V", Point2VoltageV,
                    setSys1: t => PressurePoint2Sys1Text = t,
                    setSys2: t => PressurePoint2Sys2Text = t,
                    setSys3: t => PressurePoint2Sys3Text = t,
                    setV1: v => _p2Sys1 = v,
                    setV2: v => _p2Sys2 = v,
                    setV3: v => _p2Sys3 = v,
                    cancellationToken).ConfigureAwait(false);
                if (!IsAutoTestRunning)
                    return CurrentTestResult ?? "--";

                _measured2 = true;

                await Task.Delay(80, cancellationToken).ConfigureAwait(false);

                var ok3 = await MeasurePointAllSystemsAsync("3.0V", Point3VoltageV,
                    setSys1: t => PressurePoint3Sys1Text = t,
                    setSys2: t => PressurePoint3Sys2Text = t,
                    setSys3: t => PressurePoint3Sys3Text = t,
                    setV1: v => _p3Sys1 = v,
                    setV2: v => _p3Sys2 = v,
                    setV3: v => _p3Sys3 = v,
                    cancellationToken).ConfigureAwait(false);
                if (!IsAutoTestRunning)
                    return CurrentTestResult ?? "--";

                _measured3 = true;

                await TryFinalizeIfAllMeasuredAsync().ConfigureAwait(false);
                await StopAutoTestAsync().ConfigureAwait(false);

                return LastTestResult;
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

        private async Task OnMeasurePoint1Async()
        {
            PressurePoint1Sys1Text = "--";
            PressurePoint1Sys2Text = "--";
            PressurePoint1Sys3Text = "--";
            _p1Sys1 = null;
            _p1Sys2 = null;
            _p1Sys3 = null;
            CanMeasure = false;
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(delegate { }, System.Windows.Threading.DispatcherPriority.Background);
            var ok = await MeasurePointAllSystemsAsync("0.5V", Point1VoltageV,
                setSys1: t => PressurePoint1Sys1Text = t,
                setSys2: t => PressurePoint1Sys2Text = t,
                setSys3: t => PressurePoint1Sys3Text = t,
                setV1: v => _p1Sys1 = v,
                setV2: v => _p1Sys2 = v,
                setV3: v => _p1Sys3 = v,
                _manualCts?.Token ?? CancellationToken.None).ConfigureAwait(false);
            CanMeasure = IsManualTestRunning;
            if (!IsManualTestRunning || _manualAborted) return;
            _measured1 = true;
            RefreshMeasureCommands();
            await TryFinalizeIfAllMeasuredAsync().ConfigureAwait(false);
        }

        private async Task OnMeasurePoint2Async()
        {
            PressurePoint2Sys1Text = "--";
            PressurePoint2Sys2Text = "--";
            PressurePoint2Sys3Text = "--";
            _p2Sys1 = null;
            _p2Sys2 = null;
            _p2Sys3 = null;
            CanMeasure = false;
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(delegate { }, System.Windows.Threading.DispatcherPriority.Background);
            var ok = await MeasurePointAllSystemsAsync("7.17V", Point2VoltageV,
                setSys1: t => PressurePoint2Sys1Text = t,
                setSys2: t => PressurePoint2Sys2Text = t,
                setSys3: t => PressurePoint2Sys3Text = t,
                setV1: v => _p2Sys1 = v,
                setV2: v => _p2Sys2 = v,
                setV3: v => _p2Sys3 = v,
                _manualCts?.Token ?? CancellationToken.None).ConfigureAwait(false);
            CanMeasure = IsManualTestRunning;
            if (!IsManualTestRunning || _manualAborted) return;
            _measured2 = true;
            RefreshMeasureCommands();
            await TryFinalizeIfAllMeasuredAsync().ConfigureAwait(false);
        }

        private async Task OnMeasurePoint3Async()
        {
            PressurePoint3Sys1Text = "--";
            PressurePoint3Sys2Text = "--";
            PressurePoint3Sys3Text = "--";
            _p3Sys1 = null;
            _p3Sys2 = null;
            _p3Sys3 = null;
            CanMeasure = false;
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(delegate { }, System.Windows.Threading.DispatcherPriority.Background);
            var ok = await MeasurePointAllSystemsAsync("3.0V", Point3VoltageV,
                setSys1: t => PressurePoint3Sys1Text = t,
                setSys2: t => PressurePoint3Sys2Text = t,
                setSys3: t => PressurePoint3Sys3Text = t,
                setV1: v => _p3Sys1 = v,
                setV2: v => _p3Sys2 = v,
                setV3: v => _p3Sys3 = v,
                _manualCts?.Token ?? CancellationToken.None).ConfigureAwait(false);
            CanMeasure = IsManualTestRunning;
            if (!IsManualTestRunning || _manualAborted) return;
            _measured3 = true;
            RefreshMeasureCommands();
            await TryFinalizeIfAllMeasuredAsync().ConfigureAwait(false);
        }

        private async Task OnMeasureCustomPointAsync()
        {
            if (!TryGetValidatedCustomVoltage(out var voltage))
            {
                Log("自定义电压输入无效，请输入 0~7.17V，最多 2 位小数");
                RefreshMeasureCommands();
                return;
            }

            CustomPressureSys1Text = "--";
            CustomPressureSys2Text = "--";
            CustomPressureSys3Text = "--";
            CanMeasure = false;
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(delegate { }, System.Windows.Threading.DispatcherPriority.Background);
            var ok = await MeasurePointAllSystemsAsync($"自定义点({voltage:0.##}V)", voltage,
                setSys1: t => CustomPressureSys1Text = t,
                setSys2: t => CustomPressureSys2Text = t,
                setSys3: t => CustomPressureSys3Text = t,
                setV1: v => { },
                setV2: v => { },
                setV3: v => { },
                _manualCts?.Token ?? CancellationToken.None).ConfigureAwait(false);
            CanMeasure = IsManualTestRunning;

            if (!IsManualTestRunning || _manualAborted || !ok)
                return;

            Log($"自定义电压测量完成: {voltage:0.##}V，可继续测量");
        }

        private async Task<bool> MeasurePointAllSystemsAsync(
            string title,
            double aoVoltage,
            Action<string> setSys1,
            Action<string> setSys2,
            Action<string> setSys3,
            Action<double?> setV1,
            Action<double?> setV2,
            Action<double?> setV3,
            CancellationToken cancellationToken)
        {
            if (!IsAutoTestRunning && !IsManualTestRunning)
            {
                Log($"{title}: 当前未处于测试状态");
                return false;
            }

            await _measureLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await SetAo012Async(aoVoltage, cancellationToken).ConfigureAwait(false);
                await Task.Delay(AoSettleMs, cancellationToken).ConfigureAwait(false);
                _ = await _arinc.ReadRxWordsAsync(RxChannelIndex, maxCount: 4096, enableTimeTag: false, enableRateAdaption: false, cancellationToken: cancellationToken).ConfigureAwait(false);
                await Task.Delay(PostSwitchRxFlushMs, cancellationToken).ConfigureAwait(false);

                return await MeasureAllSystemsAsync(
                    title,
                    setSys1,
                    setSys2,
                    setSys3,
                    setV1,
                    setV2,
                    setV3,
                    cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _measureLock.Release();
            }
        }

        private sealed class PressureMeasureState
        {
            public PressureMeasureState(string title, byte sdi, Action<string> setText, Action<double?> setValue)
            {
                Title = title;
                Sdi = sdi;
                SetText = setText;
                SetValue = setValue;
            }

            public string Title { get; }
            public byte Sdi { get; }
            public Action<string> SetText { get; }
            public Action<double?> SetValue { get; }
            public List<double> Samples { get; } = new List<double>(SamplesPerMeasure);
            public bool Completed { get; set; }
        }

        private async Task<bool> MeasureAllSystemsAsync(
            string title,
            Action<string> setSys1,
            Action<string> setSys2,
            Action<string> setSys3,
            Action<double?> setV1,
            Action<double?> setV2,
            Action<double?> setV3,
            CancellationToken cancellationToken)
        {
            var states = new[]
            {
                new PressureMeasureState($"{title}-SYS1", 1, setSys1, setV1),
                new PressureMeasureState($"{title}-SYS2", 2, setSys2, setV2),
                new PressureMeasureState($"{title}-SYS3", 3, setSys3, setV3),
            };

            foreach (var state in states)
                Log($"{state.Title}: 开始接收压力数据");

            var stateBySdi = states.ToDictionary(x => x.Sdi);
            var deadline = DateTime.UtcNow.AddMilliseconds(SampleTimeoutMs);

            while (!cancellationToken.IsCancellationRequested && DateTime.UtcNow <= deadline)
            {
                var words = await _arinc.ReadRxWordsAsync(RxChannelIndex, maxCount: 512, enableTimeTag: false, enableRateAdaption: false, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (words.Count > 0)
                {
                    foreach (var w in words)
                    {
                        _arinc.ParseRawWord(w.Data429, out var label, out var wordSdi, out var data19, out var ssm);

                        if (!IsExpectedLabel(label))
                            continue;

                        if (!stateBySdi.TryGetValue(wordSdi, out var state) || state.Completed)
                            continue;

                        if (ssm != SsmNormal)
                            continue;

                        var value = DecodePressure(data19);
                        state.Samples.Add(value);

                        var avg = state.Samples.Average();
                        state.SetText($"{value:0.0}");

                        if (state.Samples.Count >= SamplesPerMeasure)
                        {
                            state.SetValue(avg);
                            state.SetText($"{avg:0.0}");
                            state.Completed = true;
                            Log($"{state.Title}: 完成，压力={avg:0.###}");
                        }
                    }

                    if (states.All(x => x.Completed))
                        return true;
                }

                await Task.Delay(10, cancellationToken).ConfigureAwait(false);
            }

            foreach (var state in states.Where(x => !x.Completed))
            {
                state.SetText("超时");
                state.SetValue(null);

                if (IsManualTestRunning)
                {
                    Log($"{state.Title}: 接收超时，未获取到{SamplesPerMeasure}帧有效压力数据");
                }
                else if (IsAutoTestRunning)
                {
                    Log($"{state.Title}: 接收超时，未获取到{SamplesPerMeasure}帧有效压力数据");
                }
            }

            return states.All(x => x.Completed);
        }

        private bool IsExpectedLabel(byte label)
        {
            return _arinc.ReverseLabel(label) == PressLabelDec;
        }

        private double DecodePressure(uint data19)
        {
            var value = _arinc.DecodeUbnr(data19, bitLength: PressureBitLength, resolution: 1.0, msbPosition: 27);
            return Math.Round(value, 1, MidpointRounding.AwayFromZero);
        }

        private static bool IsPressureWithinRange(double? value, double min, double max)
        {
            return value.HasValue && value.Value >= min && value.Value <= max;
        }

        private async Task TryFinalizeIfAllMeasuredAsync()
        {
            if (!(_measured1 && _measured2 && _measured3))
                return;

            var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            const double P1Min = 0.0;
            const double P1Max = 85.0;
            const double P2Min = 3915.0;
            const double P2Max = 4000.0;
            const double P3Min = 1414.0;
            const double P3Max = 1585.0;

            var failures = new List<string>();

            void Check(string name, double? value, double min, double max)
            {
                if (!value.HasValue)
                {
                    failures.Add($"{name}=--");
                    return;
                }

                var v = value.Value;
                if (v < min || v > max)
                {
                    failures.Add($"{name}={v:0.###}psi not in [{min:0.###},{max:0.###}]");
                }
            }

            Check("P1-SYS1", _p1Sys1, P1Min, P1Max);
            Check("P1-SYS2", _p1Sys2, P1Min, P1Max);
            Check("P1-SYS3", _p1Sys3, P1Min, P1Max);

            Check("P2-SYS1", _p2Sys1, P2Min, P2Max);
            Check("P2-SYS2", _p2Sys2, P2Min, P2Max);
            Check("P2-SYS3", _p2Sys3, P2Min, P2Max);

            Check("P3-SYS1", _p3Sys1, P3Min, P3Max);
            Check("P3-SYS2", _p3Sys2, P3Min, P3Max);
            Check("P3-SYS3", _p3Sys3, P3Min, P3Max);

            var resultText = failures.Count == 0 ? "PASS" : "FAIL";
            CurrentTestResult = resultText;
            PreviousTestTime = now;
            PreviousTestResult = resultText;
            LastTestTime = now;
            LastTestResult = resultText;

            if (failures.Count == 0)
            {
                Log("自动判定: PASS (三档位三路压力均在范围内)");
            }
            else
            {
                Log("自动判定: FAIL");
                foreach (var f in failures)
                {
                    Log($"判据不满足: {f}");
                }
            }

            SaveTestResultToProject();
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

            IsManualTestStopping = true;
            IsManualTestInitializing = false;
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
                await CleanupIoAsync().ConfigureAwait(false);
            }
            finally
            {
                IsManualTestInitializing = false;
                IsManualTestRunning = false;
                IsManualTestStopping = false;
                RaisePropertyChanged(nameof(CanStartManualTest));
                RaisePropertyChanged(nameof(CanStartAutoTest));
                Log("手动测试已结束");
            }
        }

        private async Task StopAutoTestAsync()
        {
            if (IsAutoTestStopping)
            {
                return;
            }

            IsAutoTestStopping = true;
            IsAutoTestInitializing = false;
            try
            {
                _autoCts?.Cancel();
            }
            catch
            {
            }

            Log("自动测试停止/结束，正在断开设备...");
            try
            {
                await CleanupIoAsync().ConfigureAwait(false);
            }
            finally
            {
                IsAutoTestInitializing = false;
                IsAutoTestRunning = false;
                IsAutoTestStopping = false;
                RaisePropertyChanged(nameof(CanStartManualTest));
                RaisePropertyChanged(nameof(CanStartAutoTest));
                Log("自动测试已结束");
            }
        }

        private async Task CleanupIoAsync()
        {
            try
            {
                if (_power != null)
                {
                    try { await _power.DisconnectAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _power.DisposeAsync().ConfigureAwait(false); } catch { }
                }
            }
            catch
            {
            }
            finally
            {
                _power = null;
            }

            try
            {
                if (_mtx532 != null)
                {
                    try { await _mtx532.StopOutputAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _mtx532.ResetAllToZeroAsync(disableAfterReset: true, cancellationToken: CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _mtx532.DisconnectAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _mtx532.DisposeAsync().ConfigureAwait(false); } catch { }
                }
            }
            catch
            {
            }
            finally
            {
                _mtx532 = null;
            }

            try
            {
                if (_arinc != null)
                {
                    try { await _arinc.StopRxAsync(RxChannelIndex, CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _arinc.CloseRxAsync(RxChannelIndex, CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _arinc.DisconnectAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _arinc.DisposeAsync().ConfigureAwait(false); } catch { }
                }
            }
            catch
            {
            }
            finally
            {
                _arinc = null;
            }

            try
            {
                if (_jy7131 != null)
                {
                    try { await WriteInitDosAsync(false, CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _jy7131.SetRelayAsync(Relay485ChannelIndex, false, CancellationToken.None).ConfigureAwait(false); } catch { }
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

        private void ResetPointDisplays()
        {
            _p1Sys1 = null;
            _p1Sys2 = null;
            _p1Sys3 = null;
            _p2Sys1 = null;
            _p2Sys2 = null;
            _p2Sys3 = null;
            _p3Sys1 = null;
            _p3Sys2 = null;
            _p3Sys3 = null;

            PressurePoint1Sys1Text = "--";
            PressurePoint1Sys2Text = "--";
            PressurePoint1Sys3Text = "--";
            PressurePoint2Sys1Text = "--";
            PressurePoint2Sys2Text = "--";
            PressurePoint2Sys3Text = "--";
            PressurePoint3Sys1Text = "--";
            PressurePoint3Sys2Text = "--";
            PressurePoint3Sys3Text = "--";
            CustomPressureSys1Text = "--";
            CustomPressureSys2Text = "--";
            CustomPressureSys3Text = "--";
        }

        private string NormalizeVoltageInput(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var sanitized = value.Replace("V", string.Empty).Replace("v", string.Empty).Trim();
            sanitized = sanitized.Replace(',', '.');

            var chars = new List<char>(sanitized.Length);
            var hasDot = false;
            var decimalCount = 0;
            foreach (var ch in sanitized)
            {
                if (char.IsDigit(ch))
                {
                    if (hasDot)
                    {
                        if (decimalCount >= 2)
                            continue;

                        decimalCount++;
                    }

                    chars.Add(ch);
                    continue;
                }

                if (ch == '.' && !hasDot)
                {
                    hasDot = true;
                    chars.Add(ch);
                }
            }

            return new string(chars.ToArray());
        }

        private bool TryGetValidatedCustomVoltage(out double voltage)
        {
            voltage = 0;
            var text = NormalizeVoltageInput(CustomVoltageInput);
            if (string.IsNullOrWhiteSpace(text))
                return false;

            if (text.EndsWith(".", StringComparison.Ordinal))
                text = text.TrimEnd('.');

            if (!double.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out voltage))
                return false;

            voltage = Math.Truncate(voltage * 100d) / 100d;
            return voltage >= 0d && voltage <= 7.17d;
        }

        private async Task EnsurePowerAsync(CancellationToken cancellationToken)
        {
            if (!_boardPowerService.IsPowered)
            {
                var (confirmed, _) = PowerOnPromptDialog.ShowPrompt("液压单板", showVoltage: false);
                if (!confirmed) throw new OperationCanceledException("用户取消上电");
                await _boardPowerService.PowerOnAsync("液压单板", cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            await Task.Delay(300, cancellationToken).ConfigureAwait(false);
        }

        private async Task EnsureMtx532Async(CancellationToken cancellationToken)
        {
            if (_mtx532 != null && _mtx532.IsConnected)
                return;

            var device = FindFirstMtx532Device();
            if (device == null)
                throw new InvalidOperationException("未找到MTX532(模拟量输出)板卡");

            var slot = device is PxiDeviceBase pxi ? pxi.SlotIndex : 7;
            _mtx532 = new Mtx532Api(device, options: new Mtx532Options { SampleRateHz = 20000.0 }, slotNumber: slot);

            await _mtx532.ConnectAsync(cancellationToken, new[] { "AO0", "AO1", "AO2", "AO3", "AO4", "AO5" }).ConfigureAwait(false);
            await SetAo012Async(0.0, cancellationToken).ConfigureAwait(false);
            await Task.Delay(300, cancellationToken).ConfigureAwait(false);
            await WaitForMtx532ReadyAsync(cancellationToken).ConfigureAwait(false);
            await _mtx532.StartOutputAsync(cancellationToken).ConfigureAwait(false);
            await Task.Delay(300, cancellationToken).ConfigureAwait(false);
        }

        private async Task EnsureRelay485Async(bool on, CancellationToken cancellationToken)
        {
            await _relayLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (on)
                {
                    if (_isRelay485On)
                    {
                        return;
                    }

                    var device = FindFirstJy7131Device();
                    if (device == null)
                    {
                        throw new InvalidOperationException("未找到PXIe-7131(JY7131)板卡，无法开启485继电器第7路");
                    }

                    if (_jy7131 == null)
                    {
                        var slot = device is DigitalIODevice dio ? dio.SlotIndex : 0;
                        _jy7131 = new Jy7131Api(device, slot);
                    }

                    if (!_jy7131.IsConnected)
                    {
                        await _jy7131.ConnectAsync(cancellationToken).ConfigureAwait(false);
                    }

                    if (!_jy7131.IsRunning)
                    {
                        await _jy7131.SetOutputModeAsync(Jy7131OutputMode.Sinking, cancellationToken).ConfigureAwait(false);
                        await _jy7131.StartAsync(cancellationToken).ConfigureAwait(false);
                    }

                    await _jy7131.SetRelayAsync(Relay485ChannelIndex, true, cancellationToken).ConfigureAwait(false);

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
                            await _jy7131.SetRelayAsync(Relay485ChannelIndex, false, cancellationToken).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            Log($"关闭485继电器板 第{Relay485ChannelIndex + 1}路失败: {ex.Message}");
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

        private async Task EnsureGroundDoAsync(bool on, CancellationToken cancellationToken)
        {
            var device = FindFirstJy7131Device();
            if (device == null)
            {
                throw new InvalidOperationException("未找到PXIe-7131(JY7131)板卡，无法控制DO27");
            }

            if (_jy7131 == null)
            {
                var slot = device is DigitalIODevice dio ? dio.SlotIndex : 0;
                _jy7131 = new Jy7131Api(device, slot);
            }

            if (!_jy7131.IsConnected)
            {
                await _jy7131.ConnectAsync(cancellationToken).ConfigureAwait(false);
            }

            if (!_jy7131.IsRunning)
            {
                await _jy7131.SetOutputModeAsync(Jy7131OutputMode.Sinking, cancellationToken).ConfigureAwait(false);
                await _jy7131.StartAsync(cancellationToken).ConfigureAwait(false);
            }

            await WriteInitDosAsync(on, cancellationToken).ConfigureAwait(false);
        }

        private async Task WriteInitDosAsync(bool on, CancellationToken cancellationToken)
        {
            await _jy7131.WriteDoAsync($"DO{RelayAuxDoIndex}", on, cancellationToken).ConfigureAwait(false);

        }

        private async Task WaitForMtx532ReadyAsync(CancellationToken cancellationToken)
        {
            if (_mtx532 == null || !_mtx532.IsConnected)
                throw new InvalidOperationException("MTX532未连接");

            var deadline = DateTime.UtcNow.AddMilliseconds(Mtx532ReadyTimeoutMs);
            while (DateTime.UtcNow <= deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (await _mtx532.CanStartOutputAsync(cancellationToken).ConfigureAwait(false))
                    return;

                await Task.Delay(Mtx532ReadyPollMs, cancellationToken).ConfigureAwait(false);
            }

            throw new InvalidOperationException("MTX532已连接，但在等待超时前仍未准备好输出");
        }

        private async Task EnsureArincRxAsync(CancellationToken cancellationToken)
        {
            if (_arinc == null)
            {
                var device = FindFirstArincDevice();
                if (device == null)
                    throw new InvalidOperationException("未找到ART4227/ART4229(ARINC429)板卡，无法接收429数据");
                _arinc = new Art4229Api(device, deviceIndex: 0);
            }

            if (!_arinc.IsConnected)
            {
                await _arinc.ConnectAsync(cancellationToken).ConfigureAwait(false);
            }

            await _arinc.OpenRxAsync(RxChannelIndex, cancellationToken).ConfigureAwait(false);
            await _arinc.ConfigureRxAsync(
                RxChannelIndex,
                rate: ArincRate,
                parity: Art4229Parity.Odd,
                wordFormat: Art4229WordFormat.Standard429,
                enableInterrupt: false,
                interruptDepth: 512,
                enableTimeTag: false,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            await _arinc.StartRxAsync(RxChannelIndex, cancellationToken).ConfigureAwait(false);
            _ = await _arinc.ReadRxWordsAsync(RxChannelIndex, maxCount: 4096, enableTimeTag: false, enableRateAdaption: false, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        private async Task SetAo012Async(double voltageV, CancellationToken cancellationToken)
        {
            if (_mtx532 == null || !_mtx532.IsConnected)
                throw new InvalidOperationException("MTX532未连接");

            var halfVoltage = voltageV;
            await _mtx532.WriteOnceDcAsync(new Dictionary<string, double>
            {
                ["AO0"] = halfVoltage,
                ["AO1"] = 0.0,
                ["AO2"] = halfVoltage,
                ["AO3"] = 0.0,
                ["AO4"] = halfVoltage,
                ["AO5"] = 0.0,

            }, cancellationToken).ConfigureAwait(false);
        }

        private DeviceBase FindFirstArincDevice()
        {
            var chassisList = _pxiChassisService?.GetAllChassis();
            if (chassisList == null)
                return null;

            foreach (var chassis in chassisList)
            {
                var device = chassis?.Devices?.FirstOrDefault(d =>
                    (d?.Model?.IndexOf("4227", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.Model?.IndexOf("4229", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.Model?.IndexOf("ARINC", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.Model?.IndexOf("429", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.Name?.IndexOf("4227", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.Name?.IndexOf("4229", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.Name?.IndexOf("429", StringComparison.OrdinalIgnoreCase) >= 0));

                if (device != null)
                    return device;
            }

            return null;
        }

        private DeviceBase FindFirstJy7131Device()
        {
            var chassisList = _pxiChassisService?.GetAllChassis();
            if (chassisList == null)
                return null;

            foreach (var chassis in chassisList)
            {
                var device = chassis?.Devices?.FirstOrDefault(d =>
                    d is DigitalIODevice ||
                    (d?.Model?.IndexOf("7131", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.DeviceTypeName?.IndexOf("离散量", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.DeviceTypeName?.IndexOf("数字量", StringComparison.OrdinalIgnoreCase) >= 0));

                if (device != null)
                    return device;
            }

            return null;
        }

        private DeviceBase FindFirstMtx532Device()
        {
            var chassisList = _pxiChassisService?.GetAllChassis();
            if (chassisList == null)
                return null;

            foreach (var chassis in chassisList)
            {
                var device = chassis?.Devices?.FirstOrDefault(d =>
                    (d?.Model?.IndexOf("X532", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.Name?.IndexOf("mtx532", StringComparison.OrdinalIgnoreCase) >= 0));

                if (device != null)
                    return device;
            }

            return null;
        }

        private void Log(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke(new Action(() => Logs.Add(line)));
                return;
            }

            Logs.Add(line);
        }
    }
}
