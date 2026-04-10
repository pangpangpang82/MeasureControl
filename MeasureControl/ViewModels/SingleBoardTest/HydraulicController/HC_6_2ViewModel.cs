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
using MeasureControl.Models.Devices;
using MeasureControl.Models.Devices.DeviceCategories;
using MeasureControl.Services;
using MeasureControl.Services.HardwareApis;
using MeasureControl.Views.Dialogs;
using Prism.Events;
using Prism.Ioc;

namespace MeasureControl.ViewModels.SingleBoardTest.HydraulicController
{
    public class HC_6_2ViewModel : BindableBase
    {
        private const int CanRxChannelIndex = 0;
        private const uint CanBaudRate = 500000; // 波特率
        private const int CanReceiveTimeoutMs = 5000;
        private const int PowerStabilizeDelayMs = 1200;
        private const int PowerOffHoldDelayMs = 1500;
        private const int CanFlushWindowMs = 120;
        private const int PostSwitchRxFlushDelayMs = 200;
        
        private const int RelayDo24Index = 24;
        private const int RelayDo25Index = 25;
        private const int Relay485ChannelIndex = 6;
        
        private const uint ExpectedCanIdChannelA = 0x100;
        private const uint ExpectedCanIdChannelB = 0x103;
        private const byte ExpectedByteIndexChannelA = 1;
        private const byte ExpectedByteIndexChannelB = 1;
        private const byte ExpectedValueChannelA = 0x01;
        private const byte ExpectedValueChannelB = 0x02;
        
        private const string TestItemName = "通道ID测试";
        
        private readonly SemaphoreSlim _measureLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _relayLock = new SemaphoreSlim(1, 1);
        private CancellationTokenSource _manualCts;
        private CancellationTokenSource _autoCts;
        
        private readonly IPxiChassisService _pxiChassisService;
        private readonly ISingleBoardTestContextService _singleBoardTestContext;
        private readonly IHydraulicPowerService _hydraulicPowerService;
        private IPxi4004CanApi _canApi;
        private IJy7131Api _jy7131;
        private bool _isRelay485On;
        private bool _canChannelOpened;
        
        private bool _isManualTestRunning;
        private bool _isAutoTestRunning;
        private bool _isManualTestInitializing;
        private bool _isAutoTestInitializing;
        private bool _isManualTestStopping;
        private bool _isAutoTestStopping;
        private bool _canMeasure14;
        private bool _canMeasure182;
        
        private string _lastTestTime = "--";
        private string _lastTestResult = "--";
        private string _previousTestTime = "--";
        private string _previousTestResult = "--";
        private string _currentTestResult = "--";
        
        private string _resistance14Text = "--";
        private string _resistance182Text = "--";
        
        private string _channelAResult;
        private string _channelBResult;
        
        public HC_6_2ViewModel(IPxiChassisService pxiChassisService, ISingleBoardTestContextService singleBoardTestContext, IHydraulicPowerService hydraulicPowerService)
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
            var node = _singleBoardTestContext?.GetCurrentTestItemNode(TestItemName);
            if (node == null) return;
            
            if (!string.IsNullOrWhiteSpace(node.LastTestTime))
            {
                _previousTestTime = node.LastTestTime;
                RaisePropertyChanged(nameof(PreviousTestTime));
            }
            if (!string.IsNullOrWhiteSpace(node.LastTestResult))
            {
                _previousTestResult = node.LastTestResult;
                RaisePropertyChanged(nameof(PreviousTestResult));
            }
        }
        
        private void SaveTestResultToProject()
        {
            var node = _singleBoardTestContext?.GetCurrentTestItemNode(TestItemName);
            if (node == null) return;
            node.LastTestTime = PreviousTestTime;
            node.LastTestResult = PreviousTestResult;
            
            var eventAggregator = ContainerLocator.Container?.Resolve(typeof(IEventAggregator)) as IEventAggregator;
            eventAggregator?.GetEvent<ProjectModifiedEvent>()?.Publish(new ProjectModifiedEventArgs
            {
                ModificationType = "SingleBoardTestResult",
                Description = $"单板测试结果已更新: {TestItemName}"
            });
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
                    RaisePropertyChanged(nameof(CanMeasure14));
                    RaisePropertyChanged(nameof(CanMeasure182));
                    RaisePropertyChanged(nameof(CanStartManualTest));
                    RaisePropertyChanged(nameof(CanStartAutoTest));
                    Measure14Command?.RaiseCanExecuteChanged();
                    Measure182Command?.RaiseCanExecuteChanged();
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
        
        public bool IsManualTestInitializing
        {
            get => _isManualTestInitializing;
            private set
            {
                if (SetProperty(ref _isManualTestInitializing, value))
                {
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
                    RaisePropertyChanged(nameof(CanStartManualTest));
                    RaisePropertyChanged(nameof(CanStartAutoTest));
                }
            }
        }
        
        public bool CanMeasure14
        {
            get => _canMeasure14;
            private set
            {
                if (SetProperty(ref _canMeasure14, value))
                {
                    Measure14Command?.RaiseCanExecuteChanged();
                }
            }
        }
        
        public bool CanMeasure182
        {
            get => _canMeasure182;
            private set
            {
                if (SetProperty(ref _canMeasure182, value))
                {
                    Measure182Command?.RaiseCanExecuteChanged();
                }
            }
        }
        
        public bool CanStartManualTest => !IsManualTestInitializing && !IsAutoTestInitializing && !IsManualTestStopping && !IsAutoTestStopping && !IsAutoTestRunning;
        public bool CanStartAutoTest => !IsManualTestInitializing && !IsAutoTestInitializing && !IsManualTestStopping && !IsAutoTestStopping && !IsManualTestRunning;
        
        public string CurrentTestResult
        {
            get => _currentTestResult;
            private set => SetProperty(ref _currentTestResult, value);
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
            IsManualTestInitializing = true;
            IsManualTestStopping = false;
            ResetMeasurementState();
            
            _manualCts?.Cancel();
            _manualCts?.Dispose();
            _manualCts = new CancellationTokenSource();
            
            Log("开始手动测试");
            Log("正在初始化设备...");
            
            try
            {
                await EnsureRelay485Async(true, _manualCts.Token).ConfigureAwait(false);
                await EnsureCanAsync(_manualCts.Token).ConfigureAwait(false);
                
                IsManualTestInitializing = false;
                IsManualTestRunning = true;
                CanMeasure14 = true;
                CanMeasure182 = true;
                Log("手动测试初始化完成，可开始测量通道A和通道B");
            }
            catch (Exception ex)
            {
                Log($"手动测试初始化失败: {ex.Message}");
                await StopManualTestAsync().ConfigureAwait(false);
            }
        }
        
        private async Task OnMeasure14Async()
        {
            if (!IsManualTestRunning) return;
            
            Log("开始测试通道A识别（针脚99接地，针脚100开路）");
            
            try
            {
                await SetChannelAConfigAsync(_manualCts.Token).ConfigureAwait(false);

                await RestartBoardPowerAsync(_manualCts.Token).ConfigureAwait(false);
                await FlushCanRxBufferAsync(_manualCts.Token).ConfigureAwait(false);
                await Task.Delay(PostSwitchRxFlushDelayMs, _manualCts.Token).ConfigureAwait(false);
                
                var result = await ReceiveAndCheckChannelAsync(ExpectedCanIdChannelA, ExpectedByteIndexChannelA, ExpectedValueChannelA, _manualCts.Token).ConfigureAwait(false);
                
                _channelAResult = result ? "通道A" : "识别失败";
                Resistance14Text = _channelAResult;
                
                Log($"通道A测试结果: {_channelAResult}");
                CanMeasure14 = false;
            }
            catch (Exception ex)
            {
                Log($"通道A测试异常: {ex.Message}");
                Resistance14Text = "异常";
            }
        }
        
        private async Task OnMeasure182Async()
        {
            if (!IsManualTestRunning) return;
            
            Log("开始测试通道B识别（针脚99开路，针脚100接地）");
            
            try
            {
                await SetChannelBConfigAsync(_manualCts.Token).ConfigureAwait(false);

                await RestartBoardPowerAsync(_manualCts.Token).ConfigureAwait(false);
                await FlushCanRxBufferAsync(_manualCts.Token).ConfigureAwait(false);
                await Task.Delay(PostSwitchRxFlushDelayMs, _manualCts.Token).ConfigureAwait(false);
                
                var result = await ReceiveAndCheckChannelAsync(ExpectedCanIdChannelB, ExpectedByteIndexChannelB, ExpectedValueChannelB, _manualCts.Token).ConfigureAwait(false);
                
                _channelBResult = result ? "通道B" : "识别失败";
                Resistance182Text = _channelBResult;
                
                Log($"通道B测试结果: {_channelBResult}");
                CanMeasure182 = false;
                
                if (!string.IsNullOrEmpty(_channelAResult) && !string.IsNullOrEmpty(_channelBResult))
                {
                    await FinalizeTestAsync().ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                Log($"通道B测试异常: {ex.Message}");
                Resistance182Text = "异常";
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
                await StopManualTestAsync().ConfigureAwait(false);
            
            IsAutoTestInitializing = true;
            IsAutoTestStopping = false;
            
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
            Log("判据: 通道A识别正确，通道B识别正确");
            
            try
            {
                await EnsureRelay485Async(true, cancellationToken).ConfigureAwait(false);
                await EnsureCanAsync(cancellationToken).ConfigureAwait(false);
                
                IsAutoTestInitializing = false;
                IsAutoTestRunning = true;
                
                Log("测试通道A识别（针脚99接地，针脚100开路）");
                await SetChannelAConfigAsync(cancellationToken).ConfigureAwait(false);

                await RestartBoardPowerAsync(cancellationToken).ConfigureAwait(false);
                await FlushCanRxBufferAsync(cancellationToken).ConfigureAwait(false);
                await Task.Delay(PostSwitchRxFlushDelayMs, cancellationToken).ConfigureAwait(false);
                
                var resultA = await ReceiveAndCheckChannelAsync(ExpectedCanIdChannelA, ExpectedByteIndexChannelA, ExpectedValueChannelA, cancellationToken).ConfigureAwait(false);
                _channelAResult = resultA ? "通道A" : "识别失败";
                Resistance14Text = _channelAResult;
                Log($"通道A测试结果: {_channelAResult}");
                
                await Task.Delay(300, cancellationToken).ConfigureAwait(false);
                
                Log("测试通道B识别（针脚99开路，针脚100接地）");
                await SetChannelBConfigAsync(cancellationToken).ConfigureAwait(false);

                await RestartBoardPowerAsync(cancellationToken).ConfigureAwait(false);
                await FlushCanRxBufferAsync(cancellationToken).ConfigureAwait(false);
                await Task.Delay(PostSwitchRxFlushDelayMs, cancellationToken).ConfigureAwait(false);
                
                var resultB = await ReceiveAndCheckChannelAsync(ExpectedCanIdChannelB, ExpectedByteIndexChannelB, ExpectedValueChannelB, cancellationToken).ConfigureAwait(false);
                _channelBResult = resultB ? "通道B" : "识别失败";
                Resistance182Text = _channelBResult;
                Log($"通道B测试结果: {_channelBResult}");
                
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
        
        private async Task<bool> ReceiveAndCheckChannelAsync(uint expectedCanId, byte expectedByteIndex, byte expectedValue, CancellationToken cancellationToken)
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
                        if (frame.FrameId == expectedCanId && frame.DataLength > expectedByteIndex)
                        {
                            var byteValue = frame.Data[expectedByteIndex];
                            Log($"收到CAN帧 ID=0x{frame.FrameId:X3}, Byte[{expectedByteIndex}]=0x{byteValue:X2}");
                            
                            if (byteValue == expectedValue)
                            {
                                return true;
                            }
                        }
                    }
                    
                    await Task.Delay(50, cancellationToken).ConfigureAwait(false);
                }
                
                Log($"超时: 未在{CanReceiveTimeoutMs}ms内接收到预期的CAN消息");
                return false;
            }
            catch (Exception ex)
            {
                Log($"CAN接收异常: {ex.Message}");
                return false;
            }
            finally
            {
                _measureLock.Release();
            }
        }

        private async Task RestartBoardPowerAsync(CancellationToken cancellationToken)
        {
            if (_hydraulicPowerService?.IsHydraulicPowered == true)
            {
                Log($"准备重新上电: 先下电并保持 {PowerOffHoldDelayMs}ms");
                await _hydraulicPowerService.PowerOffAsync(cancellationToken).ConfigureAwait(false);
                await Task.Delay(PowerOffHoldDelayMs, cancellationToken).ConfigureAwait(false);
            }

            Log($"准备重新上电: 上电后等待稳定 {PowerStabilizeDelayMs}ms");
            await _hydraulicPowerService.PowerOnAsync(null, cancellationToken: cancellationToken).ConfigureAwait(false);
            await Task.Delay(PowerStabilizeDelayMs, cancellationToken).ConfigureAwait(false);
        }

        private async Task FlushCanRxBufferAsync(CancellationToken cancellationToken)
        {
            if (_canApi == null)
            {
                return;
            }

            await _measureLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var drainedCount = 0;
                var deadline = DateTime.UtcNow.AddMilliseconds(CanFlushWindowMs);

                while (!cancellationToken.IsCancellationRequested && DateTime.UtcNow <= deadline)
                {
                    var frames = await _canApi.ReceiveFramesBatchAsync(CanRxChannelIndex, maxFrames: 100, timeout: 0.01, cancellationToken: cancellationToken).ConfigureAwait(false);
                    if (frames == null || frames.Count == 0)
                    {
                        break;
                    }

                    drainedCount += frames.Count;
                }

                if (drainedCount > 0)
                {
                    Log($"已清空CAN接收缓存，丢弃历史帧 {drainedCount} 条");
                }
            }
            finally
            {
                _measureLock.Release();
            }
        }
        
        private async Task SetChannelAConfigAsync(CancellationToken cancellationToken)
        {
            await _relayLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_jy7131 != null)
                {
                    await _jy7131.WriteDoAsync($"DO{RelayDo24Index}", true, cancellationToken).ConfigureAwait(false);
                    await _jy7131.WriteDoAsync($"DO{RelayDo25Index}", false, cancellationToken).ConfigureAwait(false);
                    await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                    Log("已设置通道A配置: DO24=1, DO25=0");
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
                if (_jy7131 != null)
                {
                    await _jy7131.WriteDoAsync($"DO{RelayDo24Index}", false, cancellationToken).ConfigureAwait(false);
                    await _jy7131.WriteDoAsync($"DO{RelayDo25Index}", true, cancellationToken).ConfigureAwait(false);
                    await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                    Log("已设置通道B配置: DO24=0, DO25=1");
                }
            }
            finally
            {
                _relayLock.Release();
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
                    {
                        return;
                    }
                    
                    var device = FindFirstJy7131Device();
                    if (device == null)
                    {
                        throw new InvalidOperationException("未找到PXIe-7131(JY7131)板卡，无法开启485继电器");
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
                            await _jy7131.WriteDoAsync($"DO{RelayDo24Index}", false, cancellationToken).ConfigureAwait(false);
                            await _jy7131.WriteDoAsync($"DO{RelayDo25Index}", false, cancellationToken).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            Log($"复位7131 DO失败: {ex.Message}");
                        }
                        
                        try
                        {
                            await _jy7131.SetRelayAsync(Relay485ChannelIndex, false, cancellationToken).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            Log($"关闭485继电器板失败: {ex.Message}");
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
            {
                var slot = 0;
                await _canApi.ConnectAsync(slot, cancellationToken).ConfigureAwait(false);
            }
            
            if (!_canChannelOpened)
            {
                var canParams = new CanChannelParams
                {
                    BaudRate = CanBaudRate,
                    WorkMode = 0,
                    EnableTimestamp = true,
                    AcceptExtendedId = false // 标准帧
                };
                
                await _canApi.OpenChannelAsync(CanRxChannelIndex, canParams, cancellationToken).ConfigureAwait(false);
                _canChannelOpened = true;
                Log($"已打开CAN通道{CanRxChannelIndex}, 波特率={CanBaudRate}");
            }
        }
        
        private async Task FinalizeTestAsync()
        {
            var passA = _channelAResult == "通道A";
            var passB = _channelBResult == "通道B";
            var pass = passA && passB;
            
            var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            var resultText = pass ? "合格" : "不合格";
            
            CurrentTestResult = resultText;
            PreviousTestTime = now;
            PreviousTestResult = resultText;
            LastTestTime = now;
            LastTestResult = resultText;
            
            SaveTestResultToProject();
            
            Log($"通道A识别: {(passA ? "合格" : "不合格")} ({_channelAResult})");
            Log($"通道B识别: {(passB ? "合格" : "不合格")} ({_channelBResult})");
            Log($"测试结果: {resultText}");
            
            if (IsManualTestRunning)
            {
                await StopManualTestAsync().ConfigureAwait(false);
            }
        }
        
        private void ResetMeasurementState()
        {
            CurrentTestResult = "--";
            _channelAResult = null;
            _channelBResult = null;
            Resistance14Text = "--";
            Resistance182Text = "--";
        }
        
        private async Task StopManualTestAsync()
        {
            if (IsManualTestStopping)
                return;
            
            IsManualTestInitializing = false;
            IsManualTestStopping = true;
            try { CanMeasure14 = false; CanMeasure182 = false; _manualCts?.Cancel(); } catch { }
            Log("手动测试停止/结束，正在断开设备...");
            await CleanupIoAsync(CancellationToken.None).ConfigureAwait(false);
            IsManualTestRunning = false;
            IsManualTestInitializing = false;
            IsManualTestStopping = false;
            Log("手动测试已结束");
        }
        
        private async Task StopAutoTestAsync()
        {
            if (IsAutoTestStopping)
                return;
            
            IsAutoTestInitializing = false;
            IsAutoTestStopping = true;
            try { _autoCts?.Cancel(); } catch { }
            Log("自动测试停止/结束，正在断开设备...");
            await CleanupIoAsync(CancellationToken.None).ConfigureAwait(false);
            IsAutoTestRunning = false;
            IsAutoTestInitializing = false;
            IsAutoTestStopping = false;
            Log("自动测试已结束");
        }
        
        private async Task CleanupIoAsync(CancellationToken cancellationToken)
        {
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
            catch { }
            finally { _canApi = null; }
            
            try
            {
                await EnsureRelay485Async(false, CancellationToken.None).ConfigureAwait(false);
            }
            catch { }
            
            try
            {
                if (_jy7131 != null)
                {
                    try { await _jy7131.StopAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _jy7131.DisconnectAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _jy7131.DisposeAsync().ConfigureAwait(false); } catch { }
                }
            }
            catch { }
            finally
            {
                _jy7131 = null;
                _isRelay485On = false;
            }
        }
        
        private DeviceBase FindFirstJy7131Device()
        {
            var chassisList = _pxiChassisService?.GetAllChassis();
            if (chassisList == null) return null;
            
            foreach (var chassis in chassisList)
            {
                var device = chassis?.Devices?.FirstOrDefault(d =>
                    d is DigitalIODevice ||
                    (d?.Model?.IndexOf("7131", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.DeviceTypeName?.IndexOf("离散量", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.DeviceTypeName?.IndexOf("数字量", StringComparison.OrdinalIgnoreCase) >= 0));
                
                if (device != null) return device;
            }
            
            return null;
        }
        
        private DeviceBase FindFirstCanDevice()
        {
            var chassisList = _pxiChassisService?.GetAllChassis();
            if (chassisList == null) return null;
            
            foreach (var chassis in chassisList)
            {
                var device = chassis?.Devices?.FirstOrDefault(d =>
                    (d?.Model?.IndexOf("4004", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.Model?.IndexOf("CAN", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.Name?.IndexOf("4004", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.Name?.IndexOf("CAN", StringComparison.OrdinalIgnoreCase) >= 0));
                
                if (device != null) return device;
            }
            
            return null;
        }
        
        private void Log(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            
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
