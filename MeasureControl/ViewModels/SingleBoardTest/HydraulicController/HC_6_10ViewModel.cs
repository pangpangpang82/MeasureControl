using Prism.Commands;
using Prism.Mvvm;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using MeasureControl.Events;
using MeasureControl.Models;
using MeasureControl.Models.Devices;
using MeasureControl.Models.Devices.DeviceCategories;
using MeasureControl.Services;
using MeasureControl.Services.HardwareApis;
using Prism.Events;
using Prism.Ioc;

namespace MeasureControl.ViewModels.SingleBoardTest.HydraulicController
{
    public class HC_6_10ViewModel : BindableBase
    {
        private const int CanRxChannelIndex = 0;
        private const uint CanBaudRate = 500000;
        private const int CanReceiveTimeoutMs = 5000;
        private const int PowerStabilizeDelayMs = 1200;
        private const int PowerOffHoldDelayMs = 1500;
        private const int CanFlushWindowMs = 120;
        private const int PostSwitchRxFlushDelayMs = 200;
        private const int RelayDo24Index = 24;
        private const int RelayDo25Index = 25;
        private const int Relay485ChannelIndex = 6;
        private const int ArincRxChannelIndex = 2;
        private const double ArincRate = 100000.0;
        private const byte QtyLabelDec = 123;
        private const byte SsmNormal = 3;
        private const int QtyBitLength = 8;
        private const int QtyMsbPosition = 27;
        private const double QtyResolution = 1.0;
        private const uint ExpectedCanIdTank2 = 0x104;
        private const byte ExpectedByteIndexTank2 = 3;
        private const byte ExpectedTank2ZeroValue = 0x00;
        private const uint ControlBoardSetTank1CanId = 0x101;
        private const byte ExpectedTank1Sdi = 1;
        private const int ExpectedTank1Quantity = 30;
        private const int LvdtSlotIndex = 2;
        private const double SimulationSumVrms = 6.0;
        private const int LvdtSys1Channel = 1;
        private const int LvdtSys2Channel = 2;
        private const string TestItemName = "通讯模块测试";
        private const string LvdtVaSuffix = "_VA";
        private const string LvdtVbSuffix = "_VB";

        private static readonly byte[] ControlBoardSetTank1Payload = { 0xA2, 0xC8, 0x00, 0x1E, 0x00, 0x1C, 0x54, 0x00 };

        private readonly SemaphoreSlim _measureLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _relayLock = new SemaphoreSlim(1, 1);
        private readonly IPxiChassisService _pxiChassisService;
        private readonly ISingleBoardTestContextService _singleBoardTestContext;
        private readonly IBoardPowerService _boardPowerService;

        private CancellationTokenSource _manualCts;
        private CancellationTokenSource _autoCts;
        private IPxi4004CanApi _canApi;
        private IArt4229Api _arinc;
        private IJy7131Api _jy7131;
        private IPxi4087LvdtApi _lvdt;
        private bool _historyLoaded;
        private bool _isRelay485On;
        private bool _arincRxOpened;
        private bool _canChannelOpened;
        private bool _isManualTestRunning;
        private bool _isAutoTestRunning;
        private bool _isManualTestInitializing;
        private bool _isAutoTestInitializing;
        private bool _isManualTestStopping;
        private bool _isAutoTestStopping;
        private double _currentQuantityPercent;
        private bool _testBenchPassed;
        private bool _controlBoardPassed;
        private string _testBenchTank2Text = "---";
        private string _controlBoardTank1Text = "---";
        private string _lastTestTime = "--";
        private string _lastTestResult = "--";
        private string _previousTestTime = "--";
        private string _previousTestResult = "--";
        private string _currentTestResult = "--";

        public HC_6_10ViewModel(IPxiChassisService pxiChassisService, ISingleBoardTestContextService singleBoardTestContext, IBoardPowerService hydraulicPowerService)
        {
            _pxiChassisService = pxiChassisService;
            _singleBoardTestContext = singleBoardTestContext;
            _boardPowerService = hydraulicPowerService;

            ManualTestCommand = new DelegateCommand(async () => await OnManualTestAsync());
            AutoTestCommand = new DelegateCommand(async () => await OnAutoTestAsync());
            ClearLogCommand = new DelegateCommand(() => Logs.Clear());

            LoadLastTestResultFromProject();
        }

        public DelegateCommand ManualTestCommand { get; }
        public DelegateCommand AutoTestCommand { get; }
        public DelegateCommand ClearLogCommand { get; }
        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();
        public bool IsManualTestBusy => IsManualTestInitializing || IsManualTestStopping;
        public bool IsAutoTestBusy => IsAutoTestInitializing || IsAutoTestStopping;
        public bool CanStartManualTest => !IsManualTestBusy && !IsAutoTestBusy && !IsAutoTestRunning;
        public bool CanStartAutoTest => !IsManualTestBusy && !IsAutoTestBusy && !IsManualTestRunning;

        public bool IsManualTestRunning
        {
            get => _isManualTestRunning;
            private set
            {
                if (SetProperty(ref _isManualTestRunning, value))
                {
                    RaisePropertyChanged(nameof(CanStartManualTest));
                    RaisePropertyChanged(nameof(CanStartAutoTest));
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
                }
            }
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

        public string TestBenchTank2Text
        {
            get => _testBenchTank2Text;
            private set => SetProperty(ref _testBenchTank2Text, value);
        }

        public string ControlBoardTank1Text
        {
            get => _controlBoardTank1Text;
            private set => SetProperty(ref _controlBoardTank1Text, value);
        }

        public string CurrentTestResult
        {
            get => _currentTestResult;
            private set => SetProperty(ref _currentTestResult, value);
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

        public string PreviousTestTime
        {
            get => _previousTestTime;
            private set => SetProperty(ref _previousTestTime, value);
        }

        public string PreviousTestResult
        {
            get => _previousTestResult;
            private set => SetProperty(ref _previousTestResult, value);
        }

        public async Task<string> RunOnceAsync(CancellationToken cancellationToken)
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

        private void LoadLastTestResultFromProject()
        {
            if (_historyLoaded)
                return;

            var testItemNode = _singleBoardTestContext?.GetCurrentTestItemNode(TestItemName);
            if (testItemNode == null)
                return;

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

        private void SaveTestResultToProject()
        {
            var testItemNode = _singleBoardTestContext?.GetCurrentTestItemNode(TestItemName);
            if (testItemNode == null)
                return;

            testItemNode.LastTestTime = PreviousTestTime;
            testItemNode.LastTestResult = PreviousTestResult;

            var eventAggregator = ContainerLocator.Container?.Resolve(typeof(IEventAggregator)) as IEventAggregator;
            eventAggregator?.GetEvent<ProjectModifiedEvent>()?.Publish(new ProjectModifiedEventArgs
            {
                ModificationType = "SingleBoardTestResult",
                Description = $"单板测试结果已更新: {TestItemName}"
            });
        }

        private async Task OnManualTestAsync()
        {
            if (IsManualTestRunning || IsManualTestInitializing)
            {
                await StopManualTestAsync().ConfigureAwait(false);
                return;
            }

            Log("当前任务仅支持自动测试");
        }

        private async Task OnAutoTestAsync()
        {
            if (IsAutoTestStopping)
                return;

            if (IsAutoTestRunning || IsAutoTestInitializing)
            {
                await StopAutoTestAsync().ConfigureAwait(false);
                return;
            }

            if (IsManualTestRunning)
                await StopManualTestAsync().ConfigureAwait(false);

            IsAutoTestInitializing = true;
            IsAutoTestStopping = false;
            ResetMeasurementState();

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
                IsAutoTestInitializing = false;
                _autoCts?.Dispose();
                _autoCts = null;
            }
        }

        private async Task<string> ExecuteAutoTestAsync(CancellationToken cancellationToken)
        {
            ResetMeasurementState();
            Log("开始自动测试");
            Log("测试台接收: 2号油箱油量设置为0，进入B通道接收CAN帧ID=0x104并判断Byte[3]");
            Log("控制板接收: 进入B通道通过CAN0发送ID=0x101固定帧，并接收429 Label173 SDI=01的1号油量=30");

            try
            {
                await EnsureRelay485Async(true, cancellationToken).ConfigureAwait(false);
                await EnsureCanAsync(cancellationToken).ConfigureAwait(false);
                await EnsureArincRxAsync(cancellationToken).ConfigureAwait(false);
                await EnsureLvdtAsync(cancellationToken).ConfigureAwait(false);
                await ApplyQuantityOutputsAsync(0.0, cancellationToken).ConfigureAwait(false);
                await SetChannelBConfigAsync(cancellationToken).ConfigureAwait(false);
                await RestartBoardPowerAsync(cancellationToken).ConfigureAwait(false);
                await FlushCanRxBufferAsync(cancellationToken).ConfigureAwait(false);
                await Task.Delay(PostSwitchRxFlushDelayMs, cancellationToken).ConfigureAwait(false);

                IsAutoTestInitializing = false;
                IsAutoTestRunning = true;

                _testBenchPassed = await ReceiveAndCheckTank2ZeroAsync(cancellationToken).ConfigureAwait(false);
                TestBenchTank2Text = _testBenchPassed ? "pass" : "fail";
                Log($"测试台接收结果: {(_testBenchPassed ? "pass" : "fail")}");

                await DrainArincBufferAsync(cancellationToken).ConfigureAwait(false);
                await SendControlBoardTank1SetCommandAsync(cancellationToken).ConfigureAwait(false);
                await Task.Delay(PostSwitchRxFlushDelayMs, cancellationToken).ConfigureAwait(false);
                _controlBoardPassed = await ReceiveAndCheckTank1Quantity30Async(cancellationToken).ConfigureAwait(false);
                ControlBoardTank1Text = _controlBoardPassed ? "pass" : "fail";
                Log($"控制板接收结果: {(_controlBoardPassed ? "pass" : "fail")}");

                await FinalizeTestAsync().ConfigureAwait(false);
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

        private async Task<bool> ReceiveAndCheckTank2ZeroAsync(CancellationToken cancellationToken)
        {
            await _measureLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var deadline = DateTime.UtcNow.AddMilliseconds(CanReceiveTimeoutMs);
                while (!cancellationToken.IsCancellationRequested && DateTime.UtcNow <= deadline)
                {
                    var frames = await _canApi.ReceiveFramesBatchAsync(CanRxChannelIndex, maxFrames: 100, timeout: 0.1, cancellationToken: cancellationToken).ConfigureAwait(false);
                    foreach (var frame in frames)
                    {
                        if (frame.FrameId != ExpectedCanIdTank2 || frame.DataLength <= ExpectedByteIndexTank2)
                            continue;

                        var byteValue = frame.Data[ExpectedByteIndexTank2];
                        Log($"收到CAN帧 ID=0x{frame.FrameId:X3}, Byte[{ExpectedByteIndexTank2}]=0x{byteValue:X2}");
                        if (byteValue == ExpectedTank2ZeroValue)
                            return true;
                    }

                    await Task.Delay(50, cancellationToken).ConfigureAwait(false);
                }

                Log($"超时: 未在{CanReceiveTimeoutMs}ms内接收到ID=0x{ExpectedCanIdTank2:X3}且Byte[{ExpectedByteIndexTank2}]=0的CAN消息");
                return false;
            }
            finally
            {
                _measureLock.Release();
            }
        }

        private async Task SendControlBoardTank1SetCommandAsync(CancellationToken cancellationToken)
        {
            if (_canApi == null)
                throw new InvalidOperationException("CAN板卡未初始化，无法发送1号油箱设定帧");

            var frame = new CanFrame
            {
                FrameId = ControlBoardSetTank1CanId,
                IsExtendedId = false,
                IsRemoteFrame = false,
                DataLength = (byte)ControlBoardSetTank1Payload.Length,
                Data = ControlBoardSetTank1Payload.ToArray()
            };

            var sent = await _canApi.SendFrameAsync(CanRxChannelIndex, frame, 0.2, cancellationToken).ConfigureAwait(false);
            if (!sent)
                throw new InvalidOperationException($"CAN发送失败: ID=0x{ControlBoardSetTank1CanId:X3}");

            Log($"已发送CAN帧 ID=0x{ControlBoardSetTank1CanId:X3}, Data={BitConverter.ToString(ControlBoardSetTank1Payload).Replace("-", " ")}");
        }

        private async Task<bool> ReceiveAndCheckTank1Quantity30Async(CancellationToken cancellationToken)
        {
            if (_arinc == null)
                throw new InvalidOperationException("ARINC429板卡未初始化，无法校验1号油箱油量");

            await _measureLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var deadline = DateTime.UtcNow.AddMilliseconds(CanReceiveTimeoutMs);
                while (!cancellationToken.IsCancellationRequested && DateTime.UtcNow <= deadline)
                {
                    var words = await _arinc.ReadRxWordsAsync(ArincRxChannelIndex, maxCount: 512, enableTimeTag: false, enableRateAdaption: false, cancellationToken: cancellationToken).ConfigureAwait(false);
                    foreach (var word in words)
                    {
                        _arinc.ParseRawWord(word.Data429, out var label, out var sdi, out var data19, out var ssm);
                        if (!IsExpectedQuantityLabel(label) || ssm != SsmNormal || sdi != ExpectedTank1Sdi)
                            continue;

                        var quantity = DecodeQuantity(data19);
                        if (!quantity.HasValue)
                            continue;

                        Log($"收到429油量: Label={QtyLabelDec}, SDI={sdi:00}, Value={quantity.Value:0}");
                        if (Math.Abs(quantity.Value - ExpectedTank1Quantity) < 0.5)
                            return true;
                    }

                    await Task.Delay(50, cancellationToken).ConfigureAwait(false);
                }

                Log($"超时: 未在{CanReceiveTimeoutMs}ms内接收到429 Label173 SDI=01 且1号油量={ExpectedTank1Quantity}");
                return false;
            }
            finally
            {
                _measureLock.Release();
            }
        }

        private async Task FinalizeTestAsync()
        {
            var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            var resultText = _testBenchPassed && _controlBoardPassed ? "合格" : "不合格";

            CurrentTestResult = resultText;
            PreviousTestTime = now;
            PreviousTestResult = resultText;
            LastTestTime = now;
            LastTestResult = resultText;
            SaveTestResultToProject();
            Log($"测试结果: {resultText}");
        }

        private void ResetMeasurementState()
        {
            CurrentTestResult = "--";
            TestBenchTank2Text = "---";
            ControlBoardTank1Text = "---";
            _testBenchPassed = false;
            _controlBoardPassed = false;
        }

        private async Task StopManualTestAsync()
        {
            if (IsManualTestStopping)
                return;

            IsManualTestStopping = true;
            IsManualTestInitializing = false;
            try
            {
                _manualCts?.Cancel();
            }
            catch
            {
            }

            Log("手动测试停止/结束，正在断开设备...");
            await CleanupIoAsync().ConfigureAwait(false);
            IsManualTestRunning = false;
            IsManualTestStopping = false;
            Log("手动测试已结束");
        }

        private async Task StopAutoTestAsync()
        {
            if (IsAutoTestStopping)
                return;

            IsAutoTestInitializing = false;
            IsAutoTestStopping = true;
            try
            {
                _autoCts?.Cancel();
            }
            catch
            {
            }

            Log("自动测试停止/结束，正在断开设备...");
            await CleanupIoAsync().ConfigureAwait(false);
            IsAutoTestRunning = false;
            IsAutoTestStopping = false;
            Log("自动测试已结束");
        }

        private async Task CleanupIoAsync()
        {
            try
            {
                if (_lvdt != null)
                {
                    try { await _lvdt.StopAsync(LvdtSys1Channel, CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _lvdt.StopAsync(LvdtSys2Channel, CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _lvdt.ResetAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _lvdt.DisconnectAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _lvdt.DisposeAsync().ConfigureAwait(false); } catch { }
                }
            }
            finally
            {
                _lvdt = null;
            }

            try
            {
                if (_canApi != null && _canChannelOpened)
                {
                    try { await _canApi.CloseChannelAsync(CanRxChannelIndex, CancellationToken.None).ConfigureAwait(false); } catch { }
                    _canChannelOpened = false;
                }

                if (_canApi != null)
                {
                    try { await _canApi.DisconnectAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _canApi.DisposeAsync().ConfigureAwait(false); } catch { }
                }
            }
            finally
            {
                _canApi = null;
            }

            try
            {
                if (_arinc != null)
                {
                    if (_arincRxOpened)
                    {
                        try { await _arinc.StopRxAsync(ArincRxChannelIndex, CancellationToken.None).ConfigureAwait(false); } catch { }
                        try { await _arinc.CloseRxAsync(ArincRxChannelIndex, CancellationToken.None).ConfigureAwait(false); } catch { }
                        _arincRxOpened = false;
                    }

                    try { await _arinc.DisconnectAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _arinc.DisposeAsync().ConfigureAwait(false); } catch { }
                }
            }
            finally
            {
                _arinc = null;
            }

            try
            {
                await EnsureRelay485Async(false, CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
            }

            try
            {
                if (_jy7131 != null)
                {
                    try { await _jy7131.StopAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _jy7131.DisconnectAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _jy7131.DisposeAsync().ConfigureAwait(false); } catch { }
                }
            }
            finally
            {
                _jy7131 = null;
                _isRelay485On = false;
            }
        }

        private async Task EnsureRelay485Async(bool on, CancellationToken cancellationToken)
        {
            await _relayLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (on)
                {
                    if (_isRelay485On)
                        return;

                    var device = FindFirstJy7131Device();
                    if (device == null)
                        throw new InvalidOperationException("未找到PXIe-7131(JY7131)板卡，无法开启485继电器");

                    if (_jy7131 == null)
                    {
                        var slot = device is DigitalIODevice dio ? dio.SlotIndex : 0;
                        _jy7131 = new Jy7131Api(device, slot);
                    }

                    if (!_jy7131.IsConnected)
                        await _jy7131.ConnectAsync(cancellationToken).ConfigureAwait(false);

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
                        return;

                    if (_jy7131 != null)
                    {
                        try { await _jy7131.WriteDoAsync($"DO{RelayDo24Index}", false, cancellationToken).ConfigureAwait(false); } catch { }
                        try { await _jy7131.WriteDoAsync($"DO{RelayDo25Index}", false, cancellationToken).ConfigureAwait(false); } catch { }
                        try { await _jy7131.SetRelayAsync(Relay485ChannelIndex, false, cancellationToken).ConfigureAwait(false); } catch { }
                    }

                    _isRelay485On = false;
                }
            }
            finally
            {
                _relayLock.Release();
            }
        }

        private async Task SetChannelBConfigAsync(CancellationToken cancellationToken)
        {
            await _relayLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_jy7131 == null)
                    throw new InvalidOperationException("JY7131未初始化，无法切换到B通道");

                await _jy7131.WriteDoAsync($"DO{RelayDo24Index}", false, cancellationToken).ConfigureAwait(false);
                await _jy7131.WriteDoAsync($"DO{RelayDo25Index}", true, cancellationToken).ConfigureAwait(false);
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                Log("已设置通道B配置: DO24=0, DO25=1");
            }
            finally
            {
                _relayLock.Release();
            }
        }

        private async Task EnsureCanAsync(CancellationToken cancellationToken)
        {
            if (_canApi == null)
            {
                var device = FindFirstCanDevice();
                if (device == null)
                    throw new InvalidOperationException("未找到PXI4004 CAN板卡");

                _canApi = new Pxi4004CanApi();
            }

            if (!_canApi.IsConnected)
                await _canApi.ConnectAsync(0, cancellationToken).ConfigureAwait(false);

            if (!_canChannelOpened)
            {
                var canParams = new CanChannelParams
                {
                    BaudRate = CanBaudRate,
                    WorkMode = 0,
                    EnableTimestamp = true,
                    AcceptExtendedId = false
                };

                await _canApi.OpenChannelAsync(CanRxChannelIndex, canParams, cancellationToken).ConfigureAwait(false);
                _canChannelOpened = true;
                Log($"已打开CAN通道{CanRxChannelIndex}, 波特率={CanBaudRate}");
            }
        }

        private async Task EnsureArincRxAsync(CancellationToken cancellationToken)
        {
            if (_arinc == null)
            {
                var device = FindFirstArincDevice();
                if (device == null)
                    throw new InvalidOperationException("未找到ART4227/ART4229(ARINC429)板卡，无法接收429油量数据");

                _arinc = new Art4229Api(device, deviceIndex: 0);
            }

            if (!_arinc.IsConnected)
                await _arinc.ConnectAsync(cancellationToken).ConfigureAwait(false);

            if (_arincRxOpened)
                return;

            await _arinc.OpenRxAsync(ArincRxChannelIndex, cancellationToken).ConfigureAwait(false);
            await _arinc.ConfigureRxAsync(ArincRxChannelIndex, ArincRate, Art4229Parity.Odd, Art4229WordFormat.Standard429, false, 512, false, cancellationToken).ConfigureAwait(false);
            await _arinc.StartRxAsync(ArincRxChannelIndex, cancellationToken).ConfigureAwait(false);
            _ = await _arinc.ReadRxWordsAsync(ArincRxChannelIndex, 4096, false, false, cancellationToken).ConfigureAwait(false);
            _arincRxOpened = true;
            Log($"已打开ARINC429接收通道{ArincRxChannelIndex}, 波特率={ArincRate:0}");
        }

        private async Task DrainArincBufferAsync(CancellationToken cancellationToken)
        {
            if (_arinc == null || !_arincRxOpened)
                return;

            for (int i = 0; i < 100; i++)
            {
                var batch = await _arinc.ReadRxWordsAsync(ArincRxChannelIndex, maxCount: 4096, enableTimeTag: false, enableRateAdaption: false, cancellationToken: cancellationToken).ConfigureAwait(false);
                if (batch == null || batch.Count == 0)
                    break;
            }
        }

        private async Task FlushCanRxBufferAsync(CancellationToken cancellationToken)
        {
            if (_canApi == null)
                return;

            await _measureLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var drainedCount = 0;
                var deadline = DateTime.UtcNow.AddMilliseconds(CanFlushWindowMs);
                while (!cancellationToken.IsCancellationRequested && DateTime.UtcNow <= deadline)
                {
                    var frames = await _canApi.ReceiveFramesBatchAsync(CanRxChannelIndex, maxFrames: 100, timeout: 0.01, cancellationToken: cancellationToken).ConfigureAwait(false);
                    if (frames == null || frames.Count == 0)
                        break;

                    drainedCount += frames.Count;
                }

                if (drainedCount > 0)
                    Log($"已清空CAN接收缓存，丢弃历史帧 {drainedCount} 条");
            }
            finally
            {
                _measureLock.Release();
            }
        }

        private async Task RestartBoardPowerAsync(CancellationToken cancellationToken)
        {
            if (_boardPowerService?.IsPowered == true)
            {
                Log($"准备重新上电: 先下电并保持 {PowerOffHoldDelayMs}ms");
                await _boardPowerService.PowerOffAsync(cancellationToken).ConfigureAwait(false);
                await Task.Delay(PowerOffHoldDelayMs, cancellationToken).ConfigureAwait(false);
            }

            Log($"准备重新上电: 上电后等待稳定 {PowerStabilizeDelayMs}ms");
            await _boardPowerService.PowerOnAsync("液压单板", cancellationToken: cancellationToken).ConfigureAwait(false);
            await Task.Delay(PowerStabilizeDelayMs, cancellationToken).ConfigureAwait(false);
        }

        private async Task EnsureLvdtAsync(CancellationToken cancellationToken)
        {
            if (_lvdt == null)
                _lvdt = new Pxi4087LvdtApi();

            if (!_lvdt.IsConnected)
                await _lvdt.ConnectAsync(LvdtSlotIndex, cancellationToken).ConfigureAwait(false);

            await ConfigureLvdtOutputCalibrationAsync(LvdtSys1Channel, cancellationToken).ConfigureAwait(false);
            await ConfigureLvdtOutputCalibrationAsync(LvdtSys2Channel, cancellationToken).ConfigureAwait(false);

            var config = CreateSimulationConfig();
            await _lvdt.ConfigureSimulationChannelAsync(LvdtSys1Channel, config, cancellationToken).ConfigureAwait(false);
            await _lvdt.ConfigureSimulationChannelAsync(LvdtSys2Channel, config, cancellationToken).ConfigureAwait(false);
            await _lvdt.StartAsync(LvdtSys1Channel, cancellationToken).ConfigureAwait(false);
            await _lvdt.StartAsync(LvdtSys2Channel, cancellationToken).ConfigureAwait(false);
            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
        }

        private async Task ApplyQuantityOutputsAsync(double quantityPercent, CancellationToken cancellationToken)
        {
            _currentQuantityPercent = quantityPercent;
            var (s1, s2) = CalculateSecondaryVoltages(quantityPercent);
            await _lvdt.SetVaVbAsync(LvdtSys1Channel, s1, s2, cancellationToken).ConfigureAwait(false);
            await _lvdt.SetVaVbAsync(LvdtSys2Channel, s1, s2, cancellationToken).ConfigureAwait(false);
            await _lvdt.StartAsync(LvdtSys1Channel, cancellationToken).ConfigureAwait(false);
            await _lvdt.StartAsync(LvdtSys2Channel, cancellationToken).ConfigureAwait(false);
            Log($"LVDT输出: 目标油量={quantityPercent:0.###}%, S1(Va)={s1:0.00}Vrms, S2(Vb)={s2:0.00}Vrms, Sum={SimulationSumVrms:0.00}Vrms");
            await Task.Delay(2000, cancellationToken).ConfigureAwait(false);
        }

        private async Task ConfigureLvdtOutputCalibrationAsync(int channel, CancellationToken cancellationToken)
        {
            var calibration = ResolveLvdtOutputCalibration(channel);
            if (calibration == null)
            {
                await _lvdt.ClearOutputCalibrationAsync(channel, cancellationToken).ConfigureAwait(false);
                return;
            }

            await _lvdt.ConfigureOutputCalibrationAsync(channel, calibration, cancellationToken).ConfigureAwait(false);
        }

        private LvdtOutputCalibration ResolveLvdtOutputCalibration(int channel)
        {
            var device = FindFirstLvdtDevice();
            if (device == null)
                return null;

            var records = _singleBoardTestContext?.GetCurrentTestItemNode(TestItemName)?.CalibrationRecords;
            if (records == null || records.Count == 0)
                return null;

            var vaRecord = TryGetCalibrationRecord(records, device.Id, $"CH{channel - 1}{LvdtVaSuffix}");
            var vbRecord = TryGetCalibrationRecord(records, device.Id, $"CH{channel - 1}{LvdtVbSuffix}");
            if (vaRecord == null && vbRecord == null)
                return null;

            return new LvdtOutputCalibration
            {
                VaSlope = vaRecord?.Slope ?? 1.0,
                VaIntercept = vaRecord?.Intercept ?? 0.0,
                IsVaCalibrated = vaRecord?.IsCalibrated ?? false,
                VbSlope = vbRecord?.Slope ?? 1.0,
                VbIntercept = vbRecord?.Intercept ?? 0.0,
                IsVbCalibrated = vbRecord?.IsCalibrated ?? false
            };
        }

        private static ChannelCalibrationRecord TryGetCalibrationRecord(Dictionary<string, ChannelCalibrationRecord> records, string deviceId, string signalAddress)
        {
            if (records == null || string.IsNullOrWhiteSpace(signalAddress))
                return null;

            if (!string.IsNullOrWhiteSpace(deviceId))
            {
                var scopedKey = $"{deviceId}/{signalAddress}";
                if (records.TryGetValue(scopedKey, out var scopedRecord))
                    return scopedRecord;
            }

            if (records.TryGetValue(signalAddress, out var record))
                return record;

            return null;
        }

        private (double s1, double s2) CalculateSecondaryVoltages(double quantityPercent)
        {
            var boundedQuantity = Math.Max(0.0, Math.Min(100.0, quantityPercent));
            var diff = (boundedQuantity / 100.0 - 0.5) * SimulationSumVrms;
            var s1 = (SimulationSumVrms + diff) / 2.0;
            var s2 = (SimulationSumVrms - diff) / 2.0;
            return (s1, s2);
        }

        private LvdtSimulationConfig CreateSimulationConfig()
        {
            return new LvdtSimulationConfig
            {
                UseInternalExcitation = true,
                ExcitationVoltage = SimulationSumVrms,
                ExcitationFrequency = 3200.0,
                TransmissionRatio = 1.0,
                PhaseDelay = 0,
                AdcRangeIndex = 3
            };
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
                    (d?.Name?.IndexOf("7131", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.Model?.IndexOf("JY7131", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.Name?.IndexOf("JY7131", StringComparison.OrdinalIgnoreCase) >= 0));
                if (device != null)
                    return device;
            }

            return null;
        }

        private DeviceBase FindFirstCanDevice()
        {
            var chassisList = _pxiChassisService?.GetAllChassis();
            if (chassisList == null)
                return null;

            foreach (var chassis in chassisList)
            {
                var device = chassis?.Devices?.FirstOrDefault(d =>
                    (d?.Model?.IndexOf("4004", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.Model?.IndexOf("CAN", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.Name?.IndexOf("4004", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.Name?.IndexOf("CAN", StringComparison.OrdinalIgnoreCase) >= 0));
                if (device != null)
                    return device;
            }

            return null;
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

        private DeviceBase FindFirstLvdtDevice()
        {
            var chassisList = _pxiChassisService?.GetAllChassis();
            if (chassisList == null)
                return null;

            foreach (var chassis in chassisList)
            {
                var device = chassis?.Devices?.FirstOrDefault(d =>
                    (d?.Model?.IndexOf("4087", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.Model?.IndexOf("LVDT", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.Name?.IndexOf("4087", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.Name?.IndexOf("LVDT", StringComparison.OrdinalIgnoreCase) >= 0));
                if (device != null)
                    return device;
            }

            return null;
        }

        private bool IsExpectedQuantityLabel(byte label)
        {
            return _arinc != null && _arinc.ReverseLabel(label) == QtyLabelDec;
        }

        private double? DecodeQuantity(uint data19)
        {
            if (_arinc == null)
                return null;

            try
            {
                var value = _arinc.DecodeUbnr(data19, QtyBitLength, QtyResolution, QtyMsbPosition);
                return Math.Round(value, 0, MidpointRounding.AwayFromZero);
            }
            catch
            {
                return null;
            }
        }

        private void Log(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            var line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.Invoke(() => Logs.Add(line));
                return;
            }

            Logs.Add(line);
        }
    }
}
