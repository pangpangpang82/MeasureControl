using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Diagnostics;
using MeasureControl.Constants;
using Newtonsoft.Json;
using MeasureControl.Drivers;
using MeasureControl.Events;
using MeasureControl.Helpers;
using MeasureControl.Models;
using MeasureControl.Models.Devices;
using MeasureControl.Services;
using MeasureControl.Views;
using Prism.Commands;
using Prism.Events;
using Prism.Ioc;
using Prism.Mvvm;
using Prism.Regions;
using Prism.Services.Dialogs;
using MeasureControl.Views.Dialogs;
using MeasureControl.ViewModels.TestTask.ConfigTabel;
using MeasureControl.ViewModels;
using ScottPlot.WPF;

namespace MeasureControl.ViewModels.TestTask.CardCATPanel
{
    /// <summary>
    /// 模拟量通道配置面板 ViewModel
    /// </summary>
    public class PXI9774_AIViewModel : BindableBase, INavigationAware, IConfirmNavigationRequest, ICloseGuard
    {
        private readonly IPxiChassisService _pxiChassisService;
        private readonly IEventAggregator _eventAggregator;
        private readonly ProjectService _projectService;
        private readonly ObservableCollection<string> _availableTestTasks = new ObservableCollection<string>();
        private readonly ObservableCollection<string> _acquisitionModes = new ObservableCollection<string> { "有限点", "连续" };
        private readonly DelegateCommand _saveConfigCommand;
        private readonly DelegateCommand _reloadConfigCommand;
        private readonly DelegateCommand _startAcquisitionCommand;
        private readonly DelegateCommand _stopAcquisitionCommand;
        private readonly DelegateCommand _toggleAcquisitionCommand;
        private readonly DelegateCommand _openDeviceCommand;
        private readonly DelegateCommand _closeDeviceCommand;
        private readonly DelegateCommand _toggleDeviceCommand;
        private readonly DelegateCommand _navigateToCalibrationCommand;
        private readonly DelegateCommand<RealTimeDataItem> _toggleChannelDcCurrentValueCommand;
        private readonly string[] _supportedRangeOptions = { "\u00B110V", "\u00B15V", "\u00B12V", "\u00B11V" };
        private SubscriptionToken _projectModifiedToken;
        private SubscriptionToken _projectSavingToken;

        // 驱动相关
        private IDeviceDriver _driver;
        private bool _ownsDriverLifecycle;

        // ??????
        private System.Windows.Controls.Canvas _waveformCanvas;
        private System.Windows.Controls.StackPanel _legendPanel;
        private WpfPlot _waveformPlot;
        private readonly Dictionary<string, RingBuffer<double>> _waveformData = new Dictionary<string, RingBuffer<double>>();
        private readonly Dictionary<string, Brush> _channelColors = new Dictionary<string, Brush>();
        private DispatcherTimer _waveformUpdateTimer;  
        internal bool _hasPendingWaveformUpdate;
        private int _lastWaveformUpdateTick;
        private const int MaxWaveformPoints = 1000; // ??????
        private const int DisplayPointCount = 500;
        private const double WaveformWindowSeconds = 0.1;

        internal bool HasWaveformCanvas => _waveformPlot != null;

        private DeviceBase _device;
        private string _chassisName;
        private string _cardModel;
        private string _cardName;

        private class SampleBlock
        {
            public string ChannelId { get; set; }
            public double[] Samples { get; set; }
            public double SampleRate { get; set; }
        }

        private readonly object _sampleProcessingLock = new object();
        private BlockingCollection<SampleBlock> _sampleQueue;
        private CancellationTokenSource _sampleProcessingCts;
        private Task _sampleProcessingTask;

        private const double FrequencyWindowSeconds = 0.25;
        private const int RmsUiIntervalMs = 100;
        private const int FreqUiIntervalMs = 100;
        private const double MaxFreqAnalysisSampleRate = 100000.0;
        private const double SampleQueueBacklogSeconds = 0.5;

        private readonly ConcurrentDictionary<string, SlidingWindowBuffer> _freqWindows = new ConcurrentDictionary<string, SlidingWindowBuffer>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, FreqEdgeTracker> _freqTrackers = new ConcurrentDictionary<string, FreqEdgeTracker>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, int> _lastRmsUiTick = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, int> _lastFreqUiTick = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, double> _lastStableFreqHz = new ConcurrentDictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, int> _lastDiagTick = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, int> _lastConsumeDiagTick = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, UiPendingUpdate> _pendingUiUpdates = new ConcurrentDictionary<string, UiPendingUpdate>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, bool> _dcModeByChannel = new ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private int _uiFlushScheduled;
        private int _lastLoopStartTick;

        private struct UiPendingUpdate
        {
            public double? CurrentValue;
            public double? FrequencyHz;
        }

        private static string NormalizeChannelId(string channelId)
        {
            if (string.IsNullOrWhiteSpace(channelId))
                return channelId;
            return channelId.Trim().ToUpperInvariant();
        }

        private class FreqEdgeTracker
        {
            public long SampleIndex;
            public bool Armed;
            public bool HasLastCross;
            public double LastCrossIndex;
            public double LastFreqHz;

            public void Reset()
            {
                SampleIndex = 0;
                Armed = false;
                HasLastCross = false;
                LastCrossIndex = 0;
                LastFreqHz = 0;
            }
        }

        private class SlidingWindowBuffer
        {
            private double[] _buffer;
            private int _head;
            private int _count;
            private readonly object _lockObj = new object();

            public SlidingWindowBuffer(int capacity)
            {
                _buffer = new double[Math.Max(1, capacity)];
            }

            public void Reset(int capacity)
            {
                lock (_lockObj)
                {
                    _buffer = new double[Math.Max(1, capacity)];
                    _head = 0;
                    _count = 0;
                }
            }

            public void AddRange(double[] samples, int step)
            {
                if (samples == null || samples.Length == 0)
                    return;
                if (step <= 1)
                {
                    AddRange(samples);
                    return;
                }

                lock (_lockObj)
                {
                    for (int i = 0; i < samples.Length; i += step)
                    {
                        _buffer[_head] = samples[i];
                        _head = (_head + 1) % _buffer.Length;
                        if (_count < _buffer.Length) _count++;
                    }
                }
            }

            public void AddRange(double[] samples)
            {
                if (samples == null || samples.Length == 0)
                    return;

                lock (_lockObj)
                {
                    for (int i = 0; i < samples.Length; i++)
                    {
                        _buffer[_head] = samples[i];
                        _head = (_head + 1) % _buffer.Length;
                        if (_count < _buffer.Length) _count++;
                    }
                }
            }

            public double[] Snapshot()
            {
                lock (_lockObj)
                {
                    var result = new double[_count];
                    for (int i = 0; i < _count; i++)
                    {
                        int idx = (_head - _count + i + _buffer.Length) % _buffer.Length;
                        result[i] = _buffer[idx];
                    }
                    return result;
                }
            }
        }

        private void ResetFrequencyWindows(double sampleRate)
        {
            int step = GetFreqDecimationStep(sampleRate);
            double freqFs = sampleRate / step;
            int windowSamples = (int)Math.Max(64.0, freqFs * FrequencyWindowSeconds);

            _freqWindows.Clear();
            _freqTrackers.Clear();
            _lastRmsUiTick.Clear();
            _lastFreqUiTick.Clear();
            _lastStableFreqHz.Clear();

            int now = Environment.TickCount;

            foreach (var ch in Channels)
            {
                if (ch?.IsEnabled != true)
                    continue;
                var id = NormalizeChannelId(ch.ChannelName);
                _freqWindows[id] = new SlidingWindowBuffer(windowSamples);
                _freqTrackers[id] = new FreqEdgeTracker();
                _lastRmsUiTick[id] = now - RmsUiIntervalMs - 1;
                _lastFreqUiTick[id] = now - FreqUiIntervalMs - 1;
            }
        }

        private void StartSampleProcessing()
        {
            lock (_sampleProcessingLock)
            {
                if (_sampleProcessingTask != null)
                    return;

                int enabledCount = 0;
                try
                {
                    enabledCount = Channels?.Count(c => c?.IsEnabled == true) ?? 0;
                }
                catch
                {
                    enabledCount = 0;
                }

                if (enabledCount <= 0) enabledCount = 1;

                double sampleRate = GetCurrentSampleRate();
                int sampleCount = 0;
                int.TryParse(SampleCountText, out sampleCount);
                if (sampleCount <= 0) sampleCount = MaxWaveformPoints;

                double blockTime = (sampleRate > 0 && sampleCount > 0) ? (sampleCount / sampleRate) : 0.01;
                if (blockTime < 0.001) blockTime = 0.001;

                int queueCapacity = (int)Math.Ceiling((enabledCount * SampleQueueBacklogSeconds) / blockTime);
                if (queueCapacity < 64) queueCapacity = 64;
                if (queueCapacity > 1024) queueCapacity = 1024;

                _sampleQueue = new BlockingCollection<SampleBlock>(new ConcurrentQueue<SampleBlock>(), queueCapacity);
                _sampleProcessingCts = new CancellationTokenSource();
                var token = _sampleProcessingCts.Token;

                _sampleProcessingTask = Task.Run(() => SampleProcessingLoop(token), token);
                System.Diagnostics.Debug.WriteLine($"[AnalogInputConfig] StartSampleProcessing: queueCapacity={queueCapacity}");
            }
        }

        private async Task StopSampleProcessingAsync()
        {
            Task taskToWait = null;
            CancellationTokenSource ctsToCancel = null;
            BlockingCollection<SampleBlock> queueToComplete = null;

            lock (_sampleProcessingLock)
            {
                taskToWait = _sampleProcessingTask;
                ctsToCancel = _sampleProcessingCts;
                queueToComplete = _sampleQueue;
                _sampleProcessingTask = null;
                _sampleProcessingCts = null;
                _sampleQueue = null;
            }

            if (queueToComplete != null)
            {
                try { queueToComplete.CompleteAdding(); } catch { }
            }

            if (ctsToCancel != null)
            {
                try { ctsToCancel.Cancel(); } catch { }
            }

            if (taskToWait != null)
            {
                try
                {
                    await taskToWait.ConfigureAwait(false);
                }
                catch
                {
                }
            }

            if (queueToComplete != null)
            {
                try { queueToComplete.Dispose(); } catch { }
            }

            try
            {
                _pendingUiUpdates.Clear();
            }
            catch
            {
            }

            if (ctsToCancel != null)
            {
                try { ctsToCancel.Dispose(); } catch { }
            }
        }

        private void SampleProcessingLoop(CancellationToken token)
        {
            int startNow = Environment.TickCount;
            if ((startNow - _lastLoopStartTick) >= 1000)
            {
                _lastLoopStartTick = startNow;
                System.Diagnostics.Debug.WriteLine("[AnalogInputConfig] SampleProcessingLoop started");
            }

            while (!token.IsCancellationRequested)
            {
                SampleBlock block;
                try
                {
                    if (_sampleQueue == null)
                        return;
                    block = _sampleQueue.Take(token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (InvalidOperationException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[AnalogInputConfig] SampleProcessingLoop Take exception: {ex.Message}");
                    if (token.IsCancellationRequested)
                        return;
                    continue;
                }

                if (block != null)
                {
                    int now = Environment.TickCount;
                    var ch = NormalizeChannelId(block.ChannelId);
                    if (!_lastConsumeDiagTick.TryGetValue(ch, out var lastC) || (now - lastC) >= 1000)
                    {
                        _lastConsumeDiagTick[ch] = now;
                        System.Diagnostics.Debug.WriteLine($"[AnalogInputConfig] SampleProcessingLoop Take: channel={ch}, samples={block.Samples?.Length ?? 0}, sampleRate={block.SampleRate}");
                    }

                    var latestByChannel = new Dictionary<string, SampleBlock>(StringComparer.OrdinalIgnoreCase);

                    void AddLatest(SampleBlock b)
                    {
                        if (b?.Samples == null || b.Samples.Length == 0) return;
                        var id = NormalizeChannelId(b.ChannelId);
                        if (string.IsNullOrEmpty(id)) return;
                        b.ChannelId = id;
                        latestByChannel[id] = b;
                    }

                    AddLatest(block);

                    while (_sampleQueue != null && _sampleQueue.TryTake(out var next))
                    {
                        AddLatest(next);
                    }

                    foreach (var b in latestByChannel.Values)
                    {
                        try
                        {
                            ProcessSampleBlock(b);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[AnalogInputConfig] ProcessSampleBlock exception: {ex.Message}");
                        }
                    }
                }
            }
        }

        private void ProcessSampleBlock(SampleBlock block)
        {
            var channelId = NormalizeChannelId(block.ChannelId);
            var samples = block.Samples;
            if (string.IsNullOrEmpty(channelId) || samples == null || samples.Length == 0)
                return;

            var rawLastValue = samples[samples.Length - 1];

            // ===== 频率相关：使用原始采样数据（不受标定的直流偏置影响） =====
            int step = GetFreqDecimationStep(block.SampleRate);
            double freqFs = block.SampleRate / step;

            double dcRaw = 0;
            double maxAbsRaw = 0;
            for (int i = 0; i < samples.Length; i++)
            {
                double v = samples[i];
                dcRaw += v;
                double av = Math.Abs(v);
                if (av > maxAbsRaw) maxAbsRaw = av;
            }
            dcRaw /= samples.Length;

            var rmsAcRaw = CalcRms(samples);
            var freqFast = QuickFreqSchmittStepped(samples, block.SampleRate, step);

            double freqEdge = 0;
            if (_freqTrackers.TryGetValue(channelId, out var tracker))
            {
                double H = Math.Max(maxAbsRaw * 0.15, 0.01);
                long baseIdx = tracker.SampleIndex;

                int decCount = 0;
                for (int i = step; i < samples.Length; i += step)
                {
                    decCount++;
                    double a = samples[i - step] - dcRaw;
                    double b = samples[i] - dcRaw;

                    if (!tracker.Armed)
                    {
                        if (b <= -H) tracker.Armed = true;
                        continue;
                    }

                    if (a < H && b >= H)
                    {
                        double denom = b - a;
                        if (Math.Abs(denom) < 1e-12) denom = 1e-12;
                        double t = (H - a) / denom;
                        t = Math.Max(0.0, Math.Min(1.0, t));
                        double crossIdx = (baseIdx + (decCount - 1)) + t;

                        if (tracker.HasLastCross)
                        {
                            double delta = crossIdx - tracker.LastCrossIndex;
                            if (delta > 0)
                            {
                                freqEdge = freqFs / delta;
                                tracker.LastFreqHz = freqEdge;
                            }
                        }

                        tracker.LastCrossIndex = crossIdx;
                        tracker.HasLastCross = true;
                        tracker.Armed = false;
                    }
                }

                if (decCount <= 0) decCount = 1;
                tracker.SampleIndex = baseIdx + decCount;
            }

            if (_freqWindows.TryGetValue(channelId, out var win))
            {
                win.AddRange(samples, step);
            }

            // ===== 波形/当前值：使用标定后的数据（UI显示补偿后值） =====
            // 标定只影响软件侧的显示/上层处理，不影响硬件采集时钟。
            var deviceId = Device?.Id ?? string.Empty;
            var scopedKey = string.IsNullOrWhiteSpace(deviceId) ? channelId : $"{deviceId}/{channelId}";
            var (slope, intercept, isCalibrated) = Services.CalibrationService.Instance.GetCalibrationParams(scopedKey);
            if (isCalibrated)
            {
                // 标定校正：显示数据 = 原始数据 * k + b
                // 说明：标定系数直接作用在采集数据上进行校正
                for (int i = 0; i < samples.Length; i++)
                {
                    samples[i] = samples[i] * slope + intercept;
                }
            }

            if (_waveformPlot != null)
            {
                UpdateWaveformData(channelId, samples, block.SampleRate);
                _hasPendingWaveformUpdate = true;
            }

            // 重新计算标定后的 DC / RMS，用于当前值显示
            double dc = 0;
            for (int i = 0; i < samples.Length; i++)
            {
                dc += samples[i];
            }
            dc /= samples.Length;

            var rmsAc = CalcRms(samples);

            int now = Environment.TickCount;

            if (!_lastDiagTick.TryGetValue(channelId, out var lastDiag) || (now - lastDiag) >= 1000)
            {
                _lastDiagTick[channelId] = now;
                System.Diagnostics.Debug.WriteLine($"[AnalogInputConfig] Calc channel={channelId}, dcRaw={dcRaw:F4}, dcCal={dc:F4}, rmsRaw={rmsAcRaw:F4}, rmsCal={rmsAc:F4}, freqFast={freqFast:F2}, freqEdge={freqEdge:F2}");
            }

            bool doRmsUi = true;
            if (_lastRmsUiTick.TryGetValue(channelId, out var lastRms))
            {
                doRmsUi = unchecked((uint)(now - lastRms) >= (uint)RmsUiIntervalMs);
            }

            if (doRmsUi)
            {
                _lastRmsUiTick[channelId] = now;

                double valueForDisplay;
                valueForDisplay = IsDcCurrentValueForChannel(channelId) ? dc : rmsAc;

                var lastValue = samples[samples.Length - 1];
                if (_driver is MeasureControl.Drivers.Art9774Driver artDriver)
                {
                    try
                    {
                        artDriver.SetChannelValue(channelId, rawLastValue);
                    }
                    catch
                    {
                    }
                }

                StorePendingUiUpdate(channelId, valueForDisplay, null);
            }

            bool doFreqUi = true;
            if (_lastFreqUiTick.TryGetValue(channelId, out var lastFreq))
            {
                doFreqUi = unchecked((uint)(now - lastFreq) >= (uint)FreqUiIntervalMs);
            }

            if (!doFreqUi)
                return;

            _lastFreqUiTick[channelId] = now;

            // 直流显示模式：频率固定显示 0，并且不再更新交流频率
            if (IsDcCurrentValueForChannel(channelId))
            {
                _lastStableFreqHz[channelId] = 0.0;
                StorePendingUiUpdate(channelId, null, 0.0);
                return;
            }

            double freqStable = 0;
            if (freqEdge > 0)
            {
                freqStable = freqEdge;
            }
            else if (_freqWindows.TryGetValue(channelId, out var freqWindow))
            {
                var snapshot = freqWindow.Snapshot();
                if (snapshot != null && snapshot.Length >= 10)
                {
                    freqStable = QuickFreqSchmitt(snapshot, freqFs);
                }
            }

            if (freqStable > 0)
            {
                _lastStableFreqHz[channelId] = freqStable;
                StorePendingUiUpdate(channelId, null, freqStable);
            }
            else
            {
                double freqToStore = (rmsAcRaw < 1e-6) ? 0.0 : double.NaN;
                _lastStableFreqHz[channelId] = freqToStore;

                StorePendingUiUpdate(channelId, null, freqToStore);
            }
        }

        private class RingBuffer<T>
        {
            private readonly T[] _buffer;
            private readonly object _lockObj = new object();
            private int _head;
            private int _count;

            public RingBuffer(int capacity)
            {
                _buffer = new T[capacity];
                _head = 0;
                _count = 0;
            }

            public int Count
            {
                get
                {
                    lock (_lockObj)
                    {
                        return _count;
                    }
                }
            }

            public int Capacity => _buffer.Length;

            public void Add(T item)
            {
                lock (_lockObj)
                {
                    _buffer[_head] = item;
                    _head = (_head + 1) % _buffer.Length;
                    if (_count < _buffer.Length) _count++;
                }
            }

            public IEnumerable<T> Items()
            {
                var snapshot = new List<T>();
                lock (_lockObj)
                {
                    for (int i = 0; i < _count; i++)
                    {
                        snapshot.Add(_buffer[(_head - _count + i + _buffer.Length) % _buffer.Length]);
                    }
                }
                return snapshot;
            }
        }
        private ObservableCollection<ChannelInfo> _channels;
        private ObservableCollection<RealTimeDataItem> _realTimeDataItems;
        private bool _isAllEnabled;
        private string _selectedTestTask;
        private bool _hasPendingChanges;
        private bool _isApplyingTaskConfig;
        private bool _isLoadingTaskOptions;
        private bool _isConfigurationLocked;
        private string _sampleRateText = "10000";
        private string _sampleCountText = "1000";
        private string _selectedAcquisitionMode;
        private bool _isRealTimeDisplayEnabled = true;
        private bool _isDeviceConnected;
        private bool _isAcquisitionRunning;
        private bool _isBusy;
        private string _connectionStatus = "离线";
        private bool _suppressChannelChangeNotifications;

        public PXI9774_AIViewModel()
        {
            Channels = new ObservableCollection<ChannelInfo>();
            RealTimeDataItems = new ObservableCollection<RealTimeDataItem>();
            _selectedAcquisitionMode = "连续";
            _availableTestTasks.CollectionChanged += OnAvailableTestTasksChanged;

            _saveConfigCommand = new DelegateCommand(
                () => SaveCurrentTaskConfig(),
                () => HasPendingChanges && !string.IsNullOrEmpty(SelectedTestTask) && HasTestTaskOptions && !IsConfigurationLocked);

            _reloadConfigCommand = new DelegateCommand(
                () => ReloadCurrentTaskConfig(),
                () => HasPendingChanges && !string.IsNullOrEmpty(SelectedTestTask) && HasTestTaskOptions && !IsConfigurationLocked);

            _startAcquisitionCommand = new DelegateCommand(async () => await OnStartAcquisitionAsync(), () => CanStartAcquisition);
            _stopAcquisitionCommand = new DelegateCommand(async () => await OnStopAcquisitionAsync(), () => CanStopAcquisition);
            _toggleAcquisitionCommand = new DelegateCommand(
                async () =>
                {
                    if (IsAcquisitionRunning)
                    {
                        await OnStopAcquisitionAsync();
                    }
                    else
                    {
                        await OnStartAcquisitionAsync();
                    }
                },
                () => CanToggleAcquisition);

            _openDeviceCommand = new DelegateCommand(async () => await OnOpenDeviceAsync(), () => !IsBusy && !IsDeviceConnected)
                .ObservesProperty(() => IsBusy)
                .ObservesProperty(() => IsDeviceConnected);
            _closeDeviceCommand = new DelegateCommand(async () => await OnCloseDeviceAsync(), () => !IsBusy && IsDeviceConnected)
                .ObservesProperty(() => IsBusy)
                .ObservesProperty(() => IsDeviceConnected);
            _toggleDeviceCommand = new DelegateCommand(
                    async () =>
                    {
                        if (IsDeviceConnected)
                        {
                            await OnCloseDeviceAsync();
                        }
                        else
                        {
                            await OnOpenDeviceAsync();
                        }
                    },
                    () => !IsBusy)
                .ObservesProperty(() => IsBusy);

            _navigateToCalibrationCommand = new DelegateCommand(OnNavigateToCalibration, () => CanNavigateToCalibration)
                .ObservesProperty(() => IsBusy)
                .ObservesProperty(() => IsDeviceConnected)
                .ObservesProperty(() => IsAcquisitionRunning);

            _toggleChannelDcCurrentValueCommand = new DelegateCommand<RealTimeDataItem>(OnToggleChannelDcCurrentValue);
        }

        private void OnToggleChannelDcCurrentValue(RealTimeDataItem item)
        {
            if (item == null)
                return;

            var ch = NormalizeChannelId(item.ChannelName);
            if (string.IsNullOrEmpty(ch))
                return;

            bool newMode = !item.IsDcCurrentValue;
            item.IsDcCurrentValue = newMode;
            _dcModeByChannel[ch] = newMode;

            if (newMode)
            {
                // 切到直流：频率立刻清零，避免保留上一次交流频率
                _lastStableFreqHz[ch] = 0.0;
                StorePendingUiUpdate(ch, null, 0.0);
            }
            else
            {
                // 切回交流：让下一次数据块能立刻刷新频率（不必等待 100ms）
                int now = Environment.TickCount;
                _lastFreqUiTick[ch] = now - FreqUiIntervalMs - 1;
            }
        }

        private bool IsDcCurrentValueForChannel(string channelId)
        {
            channelId = NormalizeChannelId(channelId);
            if (string.IsNullOrEmpty(channelId))
                return true;

            if (_dcModeByChannel.TryGetValue(channelId, out var isDc))
                return isDc;

            return true;
        }

        public PXI9774_AIViewModel(DeviceBase device, string chassisName,
            IPxiChassisService pxiChassisService = null,
            IEventAggregator eventAggregator = null,
            CardConfigDataBase cardConfigData = null,
            ProjectService projectService = null) : this()
        {
            _pxiChassisService = pxiChassisService;
            _eventAggregator = eventAggregator;
            _projectService = projectService;
            _projectModifiedToken = _eventAggregator?.GetEvent<ProjectModifiedEvent>()?.Subscribe(OnProjectModified);
            _projectSavingToken = _eventAggregator?.GetEvent<ProjectSavingEvent>()?.Subscribe(OnProjectSaving);

            Device = device;
            ChassisName = chassisName;
            CardModel = device?.Model ?? string.Empty;
            CardName = !string.IsNullOrEmpty(device?.CardName) ? device.CardName : device?.Model ?? string.Empty;

            if (Device != null)
            {
                var cachedDriver = DriverFactory.GetCachedDriver(Device.Id);
                if (cachedDriver != null)
                {
                    _driver = cachedDriver;
                    if (_driver.IsConnected)
                    {
                        _ownsDriverLifecycle = false;
                        IsDeviceConnected = true;
                        ConnectionStatus = "在线";
                    }
                }
            }

            //EnsureCalibrationFileInitialized();

            if (device != null && cardConfigData is AnalogInputCardConfig providedConfig)
            {
                device.CardConfigData = providedConfig;
            }

            LoadCardMetadata();
            InitializeChannels();
            LoadChannelConfigsFromDevice();
            LoadRealTimeData();
            LoadTestTaskOptions();

            _eventAggregator?.GetEvent<TestTaskCreatedEvent>()?.Subscribe(OnTestTaskCreated);
        }

        public DeviceBase Device
        {
            get => _device;
            set => SetProperty(ref _device, value);
        }

        public string ChassisName
        {
            get => _chassisName;
            set => SetProperty(ref _chassisName, value);
        }

        public string CardModel
        {
            get => _cardModel;
            set => SetProperty(ref _cardModel, value);
        }

        public string CardName
        {
            get => _cardName;
            set => SetProperty(ref _cardName, value);
        }

        public ObservableCollection<ChannelInfo> Channels
        {
            get => _channels;
            set => SetProperty(ref _channels, value);
        }

        public ObservableCollection<RealTimeDataItem> RealTimeDataItems
        {
            get => _realTimeDataItems;
            set => SetProperty(ref _realTimeDataItems, value);
        }

        public bool IsAllEnabled
        {
            get => _isAllEnabled;
            set
            {
                if (SetProperty(ref _isAllEnabled, value))
                {
                    SetAllChannelsEnabled(value);
                }
            }
        }

        public bool IsAllPreviewEnabled
        {
            get => RealTimeDataItems != null && RealTimeDataItems.Count > 0 && RealTimeDataItems.All(c => c.IsPreviewEnabled);
            set
            {
                if (RealTimeDataItems == null) return;
                if (value)
                {
                    foreach (var item in RealTimeDataItems)
                    {
                        item.IsPreviewEnabled = true;
                    }
                }
                else
                {
                    foreach (var item in RealTimeDataItems)
                    {
                        item.IsPreviewEnabled = false;
                    }
                }
                RaisePropertyChanged();
            }
        }

        public ObservableCollection<string> AvailableTestTasks => _availableTestTasks;

        public bool HasTestTaskOptions => AvailableTestTasks.Count > 0;

        public string SelectedTestTask
        {
            get => _selectedTestTask;
            set => ChangeSelectedTestTask(value);
        }

        public bool HasPendingChanges
        {
            get => _hasPendingChanges;
            private set
            {
                if (SetProperty(ref _hasPendingChanges, value))
                {
                    UpdateSaveReloadCanExecute();
                }
            }
        }

        /// <summary>
        /// 配置是否被锁定
        /// 当设备处于连接中、断开中或采集运行中时锁定配置
        /// </summary>
        public bool IsConfigurationLocked
        {
            get => _isConfigurationLocked;
            private set
            {
                if (SetProperty(ref _isConfigurationLocked, value))
                {
                    RaisePropertyChanged(nameof(IsLeftConfigLocked));
                    UpdateSaveReloadCanExecute();
                }
            }
        }

        private void UpdateConfigurationLock()
        {
            IsConfigurationLocked = IsBusy || IsAcquisitionRunning;
        }

        public bool IsLeftConfigLocked => IsAcquisitionRunning || (IsBusy && IsDeviceConnected);

        public string SampleRateText
        {
            get => _sampleRateText;
            set
            {
                if (SetProperty(ref _sampleRateText, value) && !_isApplyingTaskConfig)
                {
                    MarkDirty();
                    UpdateWaveformTimerInterval();
                    ResetFrequencyWindows(GetCurrentSampleRate());
                }
            }
        }

        public string SampleCountText
        {
            get => _sampleCountText;
            set
            {
                if (SetProperty(ref _sampleCountText, value) && !_isApplyingTaskConfig)
                {
                    MarkDirty();
                    UpdateWaveformTimerInterval();
                    ResetFrequencyWindows(GetCurrentSampleRate());
                }
            }
        }

        public ObservableCollection<string> AcquisitionModes => _acquisitionModes;

        public string SelectedAcquisitionMode
        {
            get => _selectedAcquisitionMode;
            set
            {
                if (SetProperty(ref _selectedAcquisitionMode, value) && !_isApplyingTaskConfig)
                {
                    MarkDirty();
                }
            }
        }

        public bool IsRealTimeDisplayEnabled
        {
            get => _isRealTimeDisplayEnabled;
            set
            {
                if (SetProperty(ref _isRealTimeDisplayEnabled, value))
                {
                    if (!_isApplyingTaskConfig)
                    {
                        MarkDirty();
                    }
                    RaisePropertyChanged(nameof(CanShowRealTimePanel));
                }
            }
        }

        public bool CanShowRealTimePanel => IsRealTimeDisplayEnabled && IsAcquisitionRunning;

        public bool IsDeviceConnected
        {
            get => _isDeviceConnected;
            private set
            {
                if (SetProperty(ref _isDeviceConnected, value))
                {
                    RefreshAcquisitionCommandStates();
                    RaisePropertyChanged(nameof(IsLeftConfigLocked));
                }
            }
        }

        public bool IsAcquisitionRunning
        {
            get => _isAcquisitionRunning;
            private set
            {
                if (SetProperty(ref _isAcquisitionRunning, value))
                {
                    UpdateConfigurationLock();
                    RefreshAcquisitionCommandStates();
                    RaisePropertyChanged(nameof(CanShowRealTimePanel));
                }
            }
        }

        public string ConnectionStatus
        {
            get => _connectionStatus;
            private set => SetProperty(ref _connectionStatus, value);
        }

        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (SetProperty(ref _isBusy, value))
                {
                    UpdateConfigurationLock();
                    RefreshAcquisitionCommandStates();
                    RaisePropertyChanged(nameof(IsLeftConfigLocked));
                }
            }
        }

        public bool CanStartAcquisition => !IsBusy && IsDeviceConnected && !IsAcquisitionRunning && Channels.Any(c => c.IsEnabled);

        public bool CanStopAcquisition => !IsBusy && IsDeviceConnected && IsAcquisitionRunning;

        public bool CanToggleAcquisition => CanStartAcquisition || CanStopAcquisition;

        private bool CanNavigateToCalibration => !IsBusy && !IsAcquisitionRunning;

        public Func<double, double> RealTimeValueFilter { get; set; }

        public ICommand SaveConfigCommand => _saveConfigCommand;
        public ICommand ReloadConfigCommand => _reloadConfigCommand;
        public ICommand StartAcquisitionCommand => _startAcquisitionCommand;
        public ICommand StopAcquisitionCommand => _stopAcquisitionCommand;
        public ICommand ClearDisplayCommand => _stopAcquisitionCommand;
        public ICommand ToggleAcquisitionCommand => _toggleAcquisitionCommand;
        public ICommand OpenDeviceCommand => _openDeviceCommand;
        public ICommand CloseDeviceCommand => _closeDeviceCommand;
        public ICommand ToggleDeviceCommand => _toggleDeviceCommand;
        public ICommand NavigateToCalibrationCommand => _navigateToCalibrationCommand;
        public ICommand ToggleChannelDcCurrentValueCommand => _toggleChannelDcCurrentValueCommand;

        /// <summary>
        /// 设置波形Canvas和LegendPanel引用
        /// </summary>
        public void SetWaveformCanvas(System.Windows.Controls.Canvas canvas, System.Windows.Controls.StackPanel legendPanel)
        {
            _waveformCanvas = canvas;
            _legendPanel = legendPanel;

            // 初始化时显示坐标轴
            _hasPendingWaveformUpdate = true;
            UpdateWaveformDisplay();
        }

        public void SetWaveformPlot(WpfPlot plot, System.Windows.Controls.StackPanel legendPanel)
        {
            _waveformPlot = plot;
            _legendPanel = legendPanel;

            if (_waveformPlot != null)
            {
                var plt = _waveformPlot.Plot;

                string fontName;
                try
                {
                    fontName = ScottPlot.Fonts.Detect("测试");
                }
                catch
                {
                    fontName = "Microsoft YaHei";
                }

                try
                {
                    plt.Font.Set(fontName);
                }
                catch
                {
                }

                plt.Axes.Title.Label.Text = string.Empty;
                plt.XLabel("时间 (s)");
                plt.YLabel("电压 (V)");

                plt.Axes.Bottom.Label.FontName = fontName;
                plt.Axes.Left.Label.FontName = fontName;

                plt.Axes.Bottom.Label.FontSize = 12;
                plt.Axes.Left.Label.FontSize = 12;
                plt.Axes.Bottom.Label.Bold = false;
                plt.Axes.Left.Label.Bold = false;

                TrySetTickLabelFont(plt.Axes.Bottom, fontName, 10);
                TrySetTickLabelFont(plt.Axes.Left, fontName, 10);

                plt.Axes.SetLimits(-WaveformWindowSeconds, 0, -10, 10);
                _waveformPlot.Refresh();
            }

            _hasPendingWaveformUpdate = true;
            UpdateWaveformDisplay();
        }

        private static void TrySetTickLabelFont(object axis, string fontName, float fontSize)
        {
            if (axis == null)
                return;

            try
            {
                var tickLabelStyleProp = axis.GetType().GetProperty("TickLabelStyle");
                var tickLabelStyle = tickLabelStyleProp?.GetValue(axis);
                if (tickLabelStyle == null)
                    return;

                var fontNameProp = tickLabelStyle.GetType().GetProperty("FontName");
                if (fontNameProp != null && fontNameProp.CanWrite)
                    fontNameProp.SetValue(tickLabelStyle, fontName);

                var fontSizeProp = tickLabelStyle.GetType().GetProperty("FontSize");
                if (fontSizeProp != null && fontSizeProp.CanWrite)
                    fontSizeProp.SetValue(tickLabelStyle, fontSize);
            }
            catch
            {
            }
        }
        private void OnSignalTabelChanged(SignalTabelChangedEventArgs args)
        {
            LoadRealTimeData();
        }

        private void OnAvailableTestTasksChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            RaisePropertyChanged(nameof(HasTestTaskOptions));
            UpdateSaveReloadCanExecute();
        }

        private void OnProjectModified(ProjectModifiedEventArgs args)
        {
            if (args?.ModificationType != null &&
                args.ModificationType.IndexOf("TestTask", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                LoadTestTaskOptions();
            }
        }

        private void OnProjectSaving()
        {
            if (!HasPendingChanges ||
                string.IsNullOrEmpty(SelectedTestTask) ||
                IsConfigurationLocked)
            {
                return;
            }

            SaveCurrentTaskConfig(false);
        }

        private void InitializeChannels()
        {
            Channels.Clear();

            if (Device == null)
                return;

            if (Device is AnalogAcquisitionDevice aiDevice && aiDevice.AiNode != null)
            {
                var (startIndex, endIndex) = GetAiChannelRange(aiDevice.AiNode.SlotPosition);
                for (int i = startIndex; i <= endIndex; i++)
                {
                        var channel = new ChannelInfo
                        {
                            ChannelName = $"AI{i}",
                            IsEnabled = false,
                            Range = _supportedRangeOptions[0],
                            AvailableRanges = new ObservableCollection<string>(_supportedRangeOptions),
                            CurrentValue = "0.000",
                            Unit = "V",
                            Status = "正常"
                        };
                    channel.PropertyChanged += OnChannelPropertyChanged;
                    Channels.Add(channel);
                }
            }
            
            UpdateAllEnabledState();
            RefreshAcquisitionCommandStates();
        }

        private void LoadChannelConfigsFromDevice()
        {
            if (Device?.CardConfigData is AnalogInputCardConfig inputConfig)
            {
                foreach (var channelInfo in Channels)
                {
                    var savedConfig = inputConfig.Channels?.FirstOrDefault(c => c.ChannelName == channelInfo.ChannelName);
                    if (savedConfig != null)
                    {
                        _suppressChannelChangeNotifications = true;
                        channelInfo.IsEnabled = savedConfig.IsEnabled;
                        channelInfo.Range = NormalizeRange(savedConfig.Range);
                        _suppressChannelChangeNotifications = false;
                    }
                }
                UpdateAllEnabledState();
            }
        }

        private void OnChannelPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (_suppressChannelChangeNotifications)
                return;

            if (e.PropertyName == nameof(ChannelInfo.IsEnabled))
            {
                UpdateAllEnabledState();
                RefreshAcquisitionCommandStates();
                MarkDirty();
                LoadRealTimeData();
            }
            else if (e.PropertyName == nameof(ChannelInfo.Range))
            {
                MarkDirty();
            }
        }

        private void SetAllChannelsEnabled(bool enabled)
        {
            _suppressChannelChangeNotifications = true;
            foreach (var channel in Channels)
            {
                channel.IsEnabled = enabled;
            }
            _suppressChannelChangeNotifications = false;
            UpdateAllEnabledState();
            RefreshAcquisitionCommandStates();
            MarkDirty();
            LoadRealTimeData();
        }

        private void UpdateAllEnabledState()
        {
            _isAllEnabled = Channels.Count > 0 && Channels.All(c => c.IsEnabled);
            RaisePropertyChanged(nameof(IsAllEnabled));
        }

        private void LoadRealTimeData()
        {
            var previewState = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

            // 优先使用当前 UI 的预览勾选状态（避免开始采集/刷新列表时把未保存的勾选覆盖掉）
            if (RealTimeDataItems != null)
            {
                foreach (var item in RealTimeDataItems)
                {
                    if (item == null || string.IsNullOrWhiteSpace(item.ChannelName))
                        continue;
                    previewState[NormalizeChannelId(item.ChannelName)] = item.IsPreviewEnabled;
                }
            }

            var cardConfig = EnsureAnalogInputCardConfig();
            var taskName = SelectedTestTask;
            if (cardConfig != null && !string.IsNullOrEmpty(taskName))
            {
                var taskConfig = GetOrCreateTaskConfig(cardConfig, taskName);
                if (taskConfig?.Channels != null && taskConfig.Channels.Count > 0)
                {
                    foreach (var cfg in taskConfig.Channels)
                    {
                        if (cfg == null || string.IsNullOrWhiteSpace(cfg.ChannelName))
                            continue;

                        // 仅在 UI 里还没有这个通道的状态时，才用已保存配置作为兜底
                        var id = NormalizeChannelId(cfg.ChannelName);
                        if (!previewState.ContainsKey(id))
                        {
                            previewState[id] = cfg.IsPreviewEnabled;
                        }
                    }
                }
            }

            RealTimeDataItems.Clear();

            if (Device == null || Channels == null)
                return;

            var enabledChannels = Channels.Where(c => c.IsEnabled).ToList();
            if (enabledChannels.Count == 0)
                return;

            var (channelPrefix, startChannel, endChannel) = GetDeviceChannelRange();
            if (string.IsNullOrEmpty(channelPrefix))
                return;

            var signalChannelMap = new Dictionary<string, SignalConfigItem>(StringComparer.OrdinalIgnoreCase);
            var allSignalTabelItems = SignalConfigTabelViewModel.GetAllSignalTabelItems();
            if (allSignalTabelItems != null && allSignalTabelItems.Count > 0)
            {
                foreach (var tabel in allSignalTabelItems.Values)
                {
                    if (tabel == null)
                        continue;

                    foreach (var signal in tabel)
                    {
                        if (signal == null || string.IsNullOrEmpty(signal.ActualChannel))
                            continue;

                        var parts = signal.ActualChannel.Split(new[] { ':' }, 2);
                        if (parts.Length != 2)
                            continue;

                        string mappedChannel = parts[1];
                        if (!signalChannelMap.ContainsKey(mappedChannel))
                        {
                            signalChannelMap[mappedChannel] = signal;
                        }
                    }
                }
            }

            foreach (var channel in enabledChannels)
            {
                if (!IsChannelInRange(channel.ChannelName, channelPrefix, startChannel, endChannel))
                    continue;

                if (signalChannelMap.TryGetValue(channel.ChannelName, out var mappedSignal))
                {
                    var channelId = NormalizeChannelId(channel.ChannelName);
                    bool restorePreview = previewState.TryGetValue(channelId, out var prevPreview) && prevPreview;
                    RealTimeDataItems.Add(new RealTimeDataItem(this)
                    {
                        ChannelName = channel.ChannelName,
                        SignalName = string.IsNullOrWhiteSpace(mappedSignal.SignalName) ? channel.ChannelName : mappedSignal.SignalName,
                        CurrentValue = FormatRealTimeValue(mappedSignal.CurrentValue),
                        Frequency = FormatFrequency(0),
                        Unit = mappedSignal.RawValueUnit ?? channel.Unit ?? "V",
                        Status = mappedSignal.Status ?? channel.Status ?? "正常",
                        IsDcCurrentValue = true,
                        IsPreviewEnabled = restorePreview  // 默认不启用预览，避免大量通道时卡顿
                    });
                }
                else
                {
                    var channelId = NormalizeChannelId(channel.ChannelName);
                    bool restorePreview = previewState.TryGetValue(channelId, out var prevPreview) && prevPreview;
                    RealTimeDataItems.Add(new RealTimeDataItem(this)
                    {
                        ChannelName = channel.ChannelName,
                        SignalName = channel.ChannelName,
                        CurrentValue = FormatRealTimeValue(channel.CurrentValue),
                        Frequency = FormatFrequency(0),
                        Unit = channel.Unit ?? "V",
                        Status = channel.Status ?? "正常",
                        IsDcCurrentValue = true,
                        IsPreviewEnabled = restorePreview  // 默认不启用预览，避免大量通道时卡顿
                    });
                }
            }

            // 恢复（或初始化）每通道交直流模式
            try
            {
                foreach (var item in RealTimeDataItems)
                {
                    var id = NormalizeChannelId(item?.ChannelName);
                    if (string.IsNullOrEmpty(id))
                        continue;

                    if (_dcModeByChannel.TryGetValue(id, out var isDc))
                    {
                        item.IsDcCurrentValue = isDc;
                    }
                    else
                    {
                        _dcModeByChannel[id] = item.IsDcCurrentValue;
                    }

                    if (item.IsDcCurrentValue)
                    {
                        _lastStableFreqHz[id] = 0.0;
                        StorePendingUiUpdate(id, null, 0.0);
                    }
                }
            }
            catch
            {
            }
        }

        private (string prefix, int start, int end) GetDeviceChannelRange()
        {
            if (Device is AnalogAcquisitionDevice aiDevice && aiDevice.AiNode != null)
            {
                return ParseChannelRange(aiDevice.AiNode.SlotPosition, "AI");
            }

            return (null, 0, 0);
        }

        private (string prefix, int start, int end) ParseChannelRange(string slotPosition, string prefix)
        {
            try
            {
                if (string.IsNullOrEmpty(slotPosition))
                    return (prefix, 0, 31);

                var parts = slotPosition.Replace(" ", string.Empty).Split(new[] { '–', '-' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2 &&
                    int.TryParse(parts[0].Replace(prefix, string.Empty), out int start) &&
                    int.TryParse(parts[1].Replace(prefix, string.Empty), out int end))
                {
                    return (prefix, start, end);
                }
            }
            catch
            {
            }

            return (prefix, 0, 31);
        }

        private bool IsChannelInRange(string channelName, string prefix, int start, int end)
        {
            if (string.IsNullOrEmpty(channelName) || !channelName.StartsWith(prefix))
                return false;

            string numStr = channelName.Substring(prefix.Length);
            if (int.TryParse(numStr, out int channelNum))
            {
                return channelNum >= start && channelNum <= end;
            }
            return false;
        }

        private (int startIndex, int endIndex) ParseSlotPosition(string slotPosition, string prefix)
        {
            try
            {
                var parts = slotPosition?.Replace(" ", string.Empty).Split(new[] { '–', '-' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts == null || parts.Length != 2)
                    return (0, 31);

                var startStr = parts[0].Replace(prefix, string.Empty);
                var endStr = parts[1].Replace(prefix, string.Empty);

                if (int.TryParse(startStr, out int start) && int.TryParse(endStr, out int end))
                {
                    return (start, end);
                }
            }
            catch
            {
            }

            return (0, 31);
        }

        private (int startIndex, int endIndex) GetAiChannelRange(string slotPosition)
        {
            if (!string.IsNullOrEmpty(Device?.Model) &&
                Device.Model.IndexOf("9774", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return (0, 31);
            }
            return ParseSlotPosition(slotPosition, "AI");
        }

        private async Task OnOpenDeviceAsync()
        {
            if (Device == null)
                return;

            if (IsBusy)
                return;

            IsBusy = true;
            ConnectionStatus = "连接中";

            try
            {
                // 检查是否已有连接的驱动（可能是缓存的）
                var cachedDriver = DriverFactory.GetCachedDriver(Device.Id);
                if (cachedDriver != null)
                {
                    _driver = cachedDriver;
                    if (_driver.IsConnected)
                    {
                        _ownsDriverLifecycle = false;
                        IsDeviceConnected = true;
                        ConnectionStatus = "在线";
                        return;
                    }
                }

                // 检查缓存驱动
                if (_driver != null && _driver.IsConnected)
                {
                    _ownsDriverLifecycle = false;
                    IsDeviceConnected = true;
                    IsConfigurationLocked = false;
                    ConnectionStatus = "在线";

                    // 订阅驱动的采集状态改变事件
                    _driver.AcquisitionStatusChanged += OnDriverAcquisitionStatusChanged;

                    return;
                }

                _driver = DriverFactory.CreateDriver(Device);

                if (_driver == null)
                {
                    _ownsDriverLifecycle = false;
                    IsDeviceConnected = false;
                    IsConfigurationLocked = false;
                    ConnectionStatus = "离线";
                    ReMessageBox.Show(
                        $"板卡连接失败，请检查板卡位置及驱动",
                        "连接失败",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Error);
                    return;
                }

                // 如果驱动已连接（可能是从缓存获取的）
                if (_driver.IsConnected)
                {
                    _ownsDriverLifecycle = false;
                    IsDeviceConnected = true;
                    IsConfigurationLocked = false;
                    ConnectionStatus = "在线";

                    // 订阅驱动的采集状态改变事件
                    _driver.AcquisitionStatusChanged += OnDriverAcquisitionStatusChanged;

                    return;
                }

                // 连接设备
                bool connected = await _driver.ConnectAsync();
                if (connected)
                {
                    _ownsDriverLifecycle = true;
                    IsDeviceConnected = true;
                    ConnectionStatus = "在线";

                    // 订阅驱动的采集状态改变事件
                    _driver.AcquisitionStatusChanged += OnDriverAcquisitionStatusChanged;

                    System.Diagnostics.Debug.WriteLine($"[AnalogInputConfig] 板卡检测成功: {Device?.Name}");
                }
                else
                {
                    _ownsDriverLifecycle = false;
                    IsDeviceConnected = false;
                    IsConfigurationLocked = false;
                    ConnectionStatus = "离线";
                    ReMessageBox.Show(
                        $"板卡连接失败，请检查板卡位置及驱动",
                        "连接失败",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                _ownsDriverLifecycle = false;
                IsDeviceConnected = false;
                ConnectionStatus = "离线";
                // 不将 _driver 设为 null，保留引用以便后续使用


                ReMessageBox.Show(
                    $"板卡连接失败，请检查板卡位置及驱动",
                    "连接失败",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);

                System.Diagnostics.Debug.WriteLine($"[AnalogInputConfig] 板卡检测异常: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task OnCloseDeviceAsync()
        {
            if (IsBusy)
                return;

            try
            {
                ConnectionStatus = "断开中";
                IsBusy = true;

                bool wasAcquisitionRunning = IsAcquisitionRunning;

                if (wasAcquisitionRunning)
                {
                    await StopAcquisitionInternalAsync();
                }

                //StopDataPolling();
                //StopFilterTimer();
                if (!wasAcquisitionRunning)
                {
                    StopWaveformUpdateTimer();
                    await StopSampleProcessingAsync();
                }

                if (_driver != null)
                {
                    // 取消事件订阅
                    _driver.AcquisitionStatusChanged -= OnDriverAcquisitionStatusChanged;

                    if (_ownsDriverLifecycle)
                    {
                        try
                        {
                            await _driver.StopAcquisitionAsync();
                        }
                        catch { }

                        try
                        {
                            await _driver.DisconnectAsync();
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AnalogInputConfig] 关闭设备异常: {ex.Message}");
            }
            finally
            {
                _driver = null;
                _ownsDriverLifecycle = false;
                IsDeviceConnected = false;
                ConnectionStatus = "离线";
                IsBusy = false;

            }
        }

        private async Task OnStartAcquisitionAsync()
        {
            if (_driver == null || !IsDeviceConnected)
            {
                System.Diagnostics.Debug.WriteLine("[AnalogInputConfig] 无法开始采集：设备未连接");
                return;
            }

            if (IsBusy)
                return;

            if (IsAcquisitionRunning)
                return;

            try
            {
                IsBusy = true;
                var swTotal = Stopwatch.StartNew();

                // 配置通道启用状态到驱动
                var swCfg = Stopwatch.StartNew();
                foreach (var channel in Channels)
                {
                    var config = new Dictionary<string, object>
                    {
                        ["IsEnabled"] = channel.IsEnabled,
                        ["Range"] = channel.Range
                    };
                    await _driver.ConfigureChannelAsync(channel.ChannelName, config);
                }
                swCfg.Stop();
                System.Diagnostics.Debug.WriteLine($"[AnalogInputConfig] Start: ConfigureChannelAsync elapsed={swCfg.ElapsedMilliseconds}ms, enabled={Channels.Count(c => c.IsEnabled)}/{Channels.Count}");

                // 读取采样参数
                double sampleRate = 0;
                int sampleCount = 0;
                double.TryParse(SampleRateText, out sampleRate);
                int.TryParse(SampleCountText, out sampleCount);

                bool started = false;

                if (SelectedAcquisitionMode == "有限点")
                {
                    // 清空波形缓存并重置索引
                    _waveformData.Clear();
                    _channelColors.Clear();

                    // 启动有限点采样
                    if (_driver is MeasureControl.Drivers.Art9774Driver artDrv)
                    {
                        started = await artDrv.StartFiniteAcquisitionAsync(sampleRate > 0 ? sampleRate : 1000.0, sampleCount > 0 ? sampleCount : 1000);
                    }
                    else
                    {
                        started = await _driver.StartAcquisitionAsync();
                    }
                }
                else
                {
                    // 连续采样：重置缓存，启动驱动连续采样（buffer size 使用 sampleCount）
                    _waveformData.Clear();
                    _channelColors.Clear();

                    System.Diagnostics.Debug.WriteLine($"[AnalogInputConfig] 选择连续采集模式，SelectedAcquisitionMode={SelectedAcquisitionMode}");

                    if (_driver is MeasureControl.Drivers.Art9774Driver artDrv)
                    {
                        started = await artDrv.StartContinuousAcquisitionAsync(sampleRate > 0 ? sampleRate : 1000.0, sampleCount > 0 ? sampleCount : MaxWaveformPoints);
                    }
                    else
                    {
                        started = await _driver.StartAcquisitionAsync();
                    }
                }

                System.Diagnostics.Debug.WriteLine($"[AnalogInputConfig] Start: Driver start elapsed={swTotal.ElapsedMilliseconds}ms (since begin)");

                if (started)
                {
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        IsAcquisitionRunning = true;
                    }, DispatcherPriority.Normal);

                    LoadRealTimeData();

                    if (_driver is MeasureControl.Drivers.Art9774Driver artDrv && artDrv.SupportsEveryNSamples)
                    {
                        var fs = sampleRate > 0 ? sampleRate : 1000.0;
                        var swInit = Stopwatch.StartNew();
                        await Task.Run(() =>
                        {
                            ResetFrequencyWindows(fs);
                            StartSampleProcessing();
                        });
                        swInit.Stop();
                        System.Diagnostics.Debug.WriteLine($"[AnalogInputConfig] Start: ResetFrequencyWindows+StartSampleProcessing elapsed={swInit.ElapsedMilliseconds}ms");
                        if (_waveformPlot != null)
                        {
                            StartWaveformUpdateTimer();
                        }
                        artDrv.SamplesAvailable += OnSamplesAvailable;
                    }

                    swTotal.Stop();
                    System.Diagnostics.Debug.WriteLine($"[AnalogInputConfig] Start: total elapsed={swTotal.ElapsedMilliseconds}ms");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AnalogInputConfig] 开始采集失败: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task OnStopAcquisitionAsync()
        {
            if (IsBusy)
                return;

            IsBusy = true;
            try
            {
                await StopAcquisitionInternalAsync();
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task StopAcquisitionInternalAsync()
        {
            if (!IsDeviceConnected || _driver == null)
            {
                IsAcquisitionRunning = false;
                StopWaveformUpdateTimer();
                await StopSampleProcessingAsync();
                return;
            }

            try
            {
                if (_driver is MeasureControl.Drivers.Art9774Driver artDrv && artDrv.SupportsEveryNSamples)
                {
                    artDrv.SamplesAvailable -= OnSamplesAvailable;
                }

                await _driver.StopAcquisitionAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AnalogInputConfig] 停止采集失败: {ex.Message}");
            }
            finally
            {
                IsAcquisitionRunning = false;
                StopWaveformUpdateTimer();
                _freqWindows.Clear();
                _lastRmsUiTick.Clear();
                _lastFreqUiTick.Clear();
                _lastStableFreqHz.Clear();
                await StopSampleProcessingAsync();
            }
        }


        private string FormatRealTimeValue(string rawValue)
        {
            if (!double.TryParse(rawValue, out var parsed))
            {
                parsed = 0.0;
            }

            var filtered = RealTimeValueFilter?.Invoke(parsed) ?? parsed;
            return filtered.ToString("F3");
        }

        private string FormatFrequency(double freqHz)
        {
            if (double.IsNaN(freqHz) || double.IsInfinity(freqHz))
            {
                return "0.000";
            }
            return freqHz.ToString("F3");
        }

        private void MarkDirty()
        {
            if (!_isApplyingTaskConfig)
            {
                HasPendingChanges = true;
            }
        }

        private void UpdateSaveReloadCanExecute()
        {
            _saveConfigCommand?.RaiseCanExecuteChanged();
            _reloadConfigCommand?.RaiseCanExecuteChanged();
        }

        private void RefreshAcquisitionCommandStates()
        {
            _startAcquisitionCommand?.RaiseCanExecuteChanged();
            _stopAcquisitionCommand?.RaiseCanExecuteChanged();
            _toggleAcquisitionCommand?.RaiseCanExecuteChanged();
            RaisePropertyChanged(nameof(CanStartAcquisition));
            RaisePropertyChanged(nameof(CanStopAcquisition));
            RaisePropertyChanged(nameof(CanToggleAcquisition));
        }
        private void LoadCardMetadata()
        {
            if (Device?.CardConfigData is AnalogInputCardConfig config)
            {
                if (!string.IsNullOrEmpty(config.CardName))
                {
                    _cardName = config.CardName;
                    RaisePropertyChanged(nameof(CardName));
                }
            }
        }

        private void ChangeSelectedTestTask(string taskName)
        {
            if (_selectedTestTask == taskName)
                return;

            if (!_isLoadingTaskOptions)
            {
                if (!EnsurePendingChangesHandled())
                {
                    RaisePropertyChanged(nameof(SelectedTestTask));
                    return;
                }
            }

            _selectedTestTask = taskName;
            RaisePropertyChanged(nameof(SelectedTestTask));
            UpdateSaveReloadCanExecute();

            if (!_isLoadingTaskOptions)
            {
                LoadConfigForTask(taskName);
            }
        }

        /// <summary>
        /// 处理测试任务创建事件，更新可用测试任务列表
        /// </summary>
        private void OnTestTaskCreated(ProjectItem testTask)
        {
            LoadTestTaskOptions();
        }

        private void LoadTestTaskOptions()
        {
            _isLoadingTaskOptions = true;
            try
            {
                AvailableTestTasks.Clear();
                var taskNames = GetTestTaskNamesFromProject();
                foreach (var task in taskNames)
                {
                    AvailableTestTasks.Add(task);
                }

                var cardConfig = EnsureAnalogInputCardConfig();
                if (cardConfig != null)
                {
                    EnsureTaskConfigsExist(cardConfig, taskNames);
                }

                string initialTask = null;
                if (Device?.CardConfigData is AnalogInputCardConfig cardConfig &&
                    !string.IsNullOrEmpty(cardConfig.LastSelectedTestTask) &&
                    AvailableTestTasks.Contains(cardConfig.LastSelectedTestTask))
                {
                    initialTask = cardConfig.LastSelectedTestTask;
                }
                else
                {
                    initialTask = AvailableTestTasks.FirstOrDefault();
                }

                _selectedTestTask = initialTask;
                RaisePropertyChanged(nameof(SelectedTestTask));
                UpdateSaveReloadCanExecute();

                if (!string.IsNullOrEmpty(initialTask))
                {
                    LoadConfigForTask(initialTask);
                }
                else
                {
                    HasPendingChanges = false;
                }
            }
            finally
            {
                _isLoadingTaskOptions = false;
            }
        }

        private void EnsureTaskConfigsExist(AnalogInputCardConfig cardConfig, IEnumerable<string> taskNames)
        {
            if (cardConfig == null || taskNames == null)
            {
                return;
            }

            foreach (var taskName in taskNames.Where(t => !string.IsNullOrWhiteSpace(t)))
            {
                _ = GetOrCreateTaskConfig(cardConfig, taskName);
            }
        }

        private List<string> GetTestTaskNamesFromProject()
        {
            var result = new List<string>();

            var globalTasks = _projectService?.GetGlobalTestTaskNames();
            if (globalTasks != null && globalTasks.Count > 0)
            {
                return globalTasks;
            }

            if (_projectService?.CurrentProjectRoot?.Children == null || string.IsNullOrEmpty(ChassisName))
            {
                return result;
            }

            var chassisNode = _projectService.CurrentProjectRoot.Children
                .FirstOrDefault(c => c.Name == ChassisName && c.Type == AppConstants.NodeTypePxiChassis);
            if (chassisNode?.Children == null)
            {
                return result;
            }

            var taskConfigNode = chassisNode.Children.FirstOrDefault(c => c.Type == AppConstants.NodeTypeTaskConfig);
            if (taskConfigNode?.Children == null)
            {
                return result;
            }

            foreach (var testTask in taskConfigNode.Children.Where(c => c.Type == AppConstants.NodeTypeTestTask))
            {
                result.Add(testTask.Name);
            }

            return result;
        }

        private void LoadConfigForTask(string taskName)
        {
            var cardConfig = EnsureAnalogInputCardConfig();
            if (cardConfig == null)
            {
                HasPendingChanges = false;
                return;
            }

            var config = GetOrCreateTaskConfig(cardConfig, taskName ?? string.Empty);
            cardConfig.LastSelectedTestTask = taskName;
            ApplyTaskConfig(config);
            HasPendingChanges = false;
        }

        private void ApplyTaskConfig(AnalogInputTestTaskConfig config)
        {
            _isApplyingTaskConfig = true;
            try
            {
                SampleRateText = config?.SampleRate > 0 ? config.SampleRate.ToString("G") : "10000";
                SampleCountText = config?.SampleCount > 0 ? config.SampleCount.ToString() : "1000";
                SelectedAcquisitionMode = !string.IsNullOrEmpty(config?.AcquisitionMode) && _acquisitionModes.Contains(config.AcquisitionMode)
                    ? config.AcquisitionMode
                    : "连续";
                IsRealTimeDisplayEnabled = config?.IsRealTimeEnabled ?? true;

                if (config?.Channels != null && config.Channels.Count > 0)
                {
                    var saved = config.Channels.ToDictionary(c => c.ChannelName, c => c, StringComparer.OrdinalIgnoreCase);
                    foreach (var channel in Channels)
                    {
                        _suppressChannelChangeNotifications = true;
                        if (saved.TryGetValue(channel.ChannelName, out var entry))
                        {
                            channel.IsEnabled = entry.IsEnabled;
                            channel.Range = NormalizeRange(entry.Range);
                        }
                        else
                        {
                            channel.IsEnabled = false;
                        }
                        _suppressChannelChangeNotifications = false;
                    }
                    UpdateAllEnabledState();
                    RefreshAcquisitionCommandStates();
                }
            }
            finally
            {
                _isApplyingTaskConfig = false;
            }

            LoadRealTimeData();
        }

        private AnalogInputTestTaskConfig GetOrCreateTaskConfig(AnalogInputCardConfig cardConfig, string taskName)
        {
            taskName ??= string.Empty;
            var config = cardConfig.TestTaskConfigs?.FirstOrDefault(c => c.TestTaskName == taskName);
            if (config == null)
            {
                config = new AnalogInputTestTaskConfig { TestTaskName = taskName };
                InitializeTaskConfigChannels(config, cardConfig);
                cardConfig.TestTaskConfigs.Add(config);
            }
            return config;
        }

        private void InitializeTaskConfigChannels(AnalogInputTestTaskConfig targetConfig, AnalogInputCardConfig sourceConfig)
        {
            targetConfig.Channels.Clear();
            foreach (var channel in Channels)
            {
                var saved = sourceConfig?.Channels?.FirstOrDefault(c => c.ChannelName == channel.ChannelName);
                targetConfig.Channels.Add(new AnalogChannelConfig
                {
                    ChannelName = channel.ChannelName,
                    IsEnabled = saved?.IsEnabled ?? channel.IsEnabled,
                    Range = NormalizeRange(saved?.Range ?? channel.Range),
                    AvailableRanges = saved?.AvailableRanges ?? _supportedRangeOptions.ToList(),
                    CurrentValue = saved?.CurrentValue ?? 0,
                    Unit = channel.Unit,
                    Status = channel.Status
                });
            }
        }

        private bool SaveCurrentTaskConfig(bool showMessages = true)
        {
            if (Device == null || !HasTestTaskOptions || string.IsNullOrEmpty(SelectedTestTask))
            {
                if (showMessages)
                {
                    ReMessageBox.Show(
                        "请选择一个测试任务再保存配置",
                        "提示",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Information);
                }
                return false;
            }

            var cardConfig = EnsureAnalogInputCardConfig();
            if (cardConfig == null)
            {
                return false;
            }

            var taskConfig = GetOrCreateTaskConfig(cardConfig, SelectedTestTask);
            taskConfig.SampleRate = TryParseDouble(SampleRateText, out var rate) ? rate : 0;
            taskConfig.SampleCount = TryParseInt(SampleCountText, out var count) ? count : 0;
            taskConfig.AcquisitionMode = SelectedAcquisitionMode;
            taskConfig.IsRealTimeEnabled = IsRealTimeDisplayEnabled;

            var previewByChannel = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            if (RealTimeDataItems != null)
            {
                foreach (var item in RealTimeDataItems)
                {
                    if (item == null || string.IsNullOrWhiteSpace(item.ChannelName))
                        continue;
                    previewByChannel[NormalizeChannelId(item.ChannelName)] = item.IsPreviewEnabled;
                }
            }

            taskConfig.Channels.Clear();
            foreach (var channel in Channels)
            {
                var channelId = NormalizeChannelId(channel.ChannelName);
                bool preview = previewByChannel.TryGetValue(channelId, out var p) && p;
                taskConfig.Channels.Add(new AnalogChannelConfig
                {
                    ChannelName = channel.ChannelName,
                    IsEnabled = channel.IsEnabled,
                    Range = channel.Range,
                    AvailableRanges = channel.AvailableRanges?.ToList() ?? _supportedRangeOptions.ToList(),
                    CurrentValue = 0,
                    Unit = channel.Unit,
                    Status = channel.Status,
                    IsPreviewEnabled = preview
                });
            }

            SaveToCardConfigData(cardConfig, taskConfig);
            HasPendingChanges = false;
            if (showMessages)
            {
                ReMessageBox.Show(
                    "配置已保存",
                    "提示",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
            }
            return true;
        }

        private void ReloadCurrentTaskConfig()
        {
            if (string.IsNullOrEmpty(SelectedTestTask))
            {
                ReMessageBox.Show(
                    "请选择一个测试任务再读取配置",
                    "提示",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
                return;
            }

            if (HasPendingChanges)
            {
                var result = ReMessageBox.Show(
                    $"读取配置会覆盖对 \"{SelectedTestTask}\" 的修改，是否继续？",
                    "提示",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Question);
                if (result != System.Windows.MessageBoxResult.Yes)
                {
                    return;
                }
            }

            LoadConfigForTask(SelectedTestTask);
        }

        private void SaveToCardConfigData(AnalogInputCardConfig cardConfig, AnalogInputTestTaskConfig taskConfig)
        {
            if (Device == null || cardConfig == null || taskConfig == null)
                return;

            cardConfig.CardId = Device.Id;
            cardConfig.CardName = CardName;
            cardConfig.CardModel = CardModel;
            cardConfig.ChassisName = ChassisName;
            cardConfig.LastSelectedTestTask = SelectedTestTask;

            cardConfig.Channels.Clear();
            foreach (var ch in taskConfig.Channels)
            {
                cardConfig.Channels.Add(new AnalogChannelConfig
                {
                    ChannelName = ch.ChannelName,
                    IsEnabled = ch.IsEnabled,
                    Range = ch.Range,
                    AvailableRanges = ch.AvailableRanges?.ToList() ?? _supportedRangeOptions.ToList(),
                    CurrentValue = ch.CurrentValue,
                    Unit = ch.Unit,
                    Status = ch.Status
                });
            }

            if (Device.CardName != CardName)
            {
                Device.CardName = CardName;
            }

            _pxiChassisService?.UpdateDeviceCardConfig(Device.Id, cardConfig);

            _eventAggregator?.GetEvent<ProjectModifiedEvent>()?.Publish(new ProjectModifiedEventArgs
            {
                ModificationType = "AnalogInputConfig",
                Description = $"模拟量采集配置已保存: {SelectedTestTask}"
            });

            _eventAggregator?.GetEvent<ChannelEnableChangedEvent>()?.Publish(new ChannelEnableChangedEventArgs
            {
                DeviceId = Device.Id,
                CardName = CardName,
                ChassisName = ChassisName
            });
        }

        private AnalogInputCardConfig EnsureAnalogInputCardConfig()
        {
            if (Device == null)
                return null;

            if (!(Device.CardConfigData is AnalogInputCardConfig config))
            {
                config = new AnalogInputCardConfig();
                Device.CardConfigData = config;
            }

            config.CardId = Device.Id;
            config.CardName = CardName;
            config.CardModel = CardModel;
            config.ChassisName = ChassisName;
            return config;
        }

        private bool EnsurePendingChangesHandled()
        {
            if (!HasPendingChanges || _isLoadingTaskOptions)
            {
                return true;
            }

            var message = string.IsNullOrEmpty(SelectedTestTask)
                ? "模拟量采集配置尚未保存，是否现在保存？"
                : $"{CardName}\"{SelectedTestTask}\" 的配置尚未保存，是否保存？";

            var result = ReMessageBox.Show(
                message,
                "提示",
                System.Windows.MessageBoxButton.YesNoCancel,
                System.Windows.MessageBoxImage.Question);

            if (result == System.Windows.MessageBoxResult.Yes)
            {
                return SaveCurrentTaskConfig();
            }

            if (result == System.Windows.MessageBoxResult.No)
            {
                HasPendingChanges = false;
                return true;
            }

            return false;
        }

        private string NormalizeRange(string range)
        {
            if (string.IsNullOrWhiteSpace(range))
                return _supportedRangeOptions[0];

            var candidate = range.Replace("?±", "±").Replace("+/-", "±");
            if (_supportedRangeOptions.Contains(candidate))
            {
                return candidate;
            }
            return _supportedRangeOptions[0];
        }

        private bool TryParseDouble(string text, out double value)
        {
            return double.TryParse(text, out value);
        }

        private bool TryParseInt(string text, out int value)
        {
            return int.TryParse(text, out value);
        }

        public void OnCardNameChanged(string originalName)
        {
            if (_pxiChassisService == null || Device == null)
            {
                return;
            }

            string newName = CardName?.Trim();

            if (newName == originalName)
                return;

            if (string.IsNullOrWhiteSpace(newName))
            {
                CardName = originalName;
                return;
            }

            if (!_pxiChassisService.ValidateCardName(ChassisName, Device.Id, newName))
            {
                CardName = originalName;
                return;
            }

            bool success = _pxiChassisService.RenameCard(ChassisName, Device.Id, newName);
            if (!success)
            {
                CardName = originalName;
            }
        }

        #region 数据轮询和滤波
        private void UpdateRealTimeDisplay(string channelName, double filteredValue, RealTimeDataItem existingItem = null, bool updateWaveform = true, double? frequencyHz = null)
        {
            channelName = NormalizeChannelId(channelName);
            var item = existingItem ?? RealTimeDataItems?.FirstOrDefault(i => string.Equals(NormalizeChannelId(i.ChannelName), channelName, StringComparison.OrdinalIgnoreCase));
            if (item != null)
            {
                item.CurrentValue = filteredValue.ToString("F3");
                if (frequencyHz.HasValue)
                {
                    item.Frequency = FormatFrequency(frequencyHz.Value);
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[AnalogInputConfig] RealTimeDataItem not found for channel={channelName}");
            }
            // 波形数据由事件数据块统一更新，不在此处叠加
        }

        private void ScheduleUiFlush()
        {
            if (Interlocked.Exchange(ref _uiFlushScheduled, 1) != 0)
                return;

            Application.Current.Dispatcher.BeginInvoke(new Action(FlushPendingUiUpdates), DispatcherPriority.Background);
        }

        private void FlushPendingUiUpdates()
        {
            Interlocked.Exchange(ref _uiFlushScheduled, 0);

            try
            {
                var items = RealTimeDataItems;
                if (items == null || items.Count == 0)
                    return;

                var itemMap = new Dictionary<string, RealTimeDataItem>(items.Count, StringComparer.OrdinalIgnoreCase);
                foreach (var it in items)
                {
                    var id = NormalizeChannelId(it?.ChannelName);
                    if (!string.IsNullOrEmpty(id))
                        itemMap[id] = it;
                }

                foreach (var kv in _pendingUiUpdates.ToArray())
                {
                    if (!_pendingUiUpdates.TryRemove(kv.Key, out var pending))
                        continue;

                    if (!itemMap.TryGetValue(kv.Key, out var item) || item == null)
                        continue;

                    if (pending.CurrentValue.HasValue)
                        item.CurrentValue = pending.CurrentValue.Value.ToString("F3");

                    if (pending.FrequencyHz.HasValue)
                        item.Frequency = FormatFrequency(pending.FrequencyHz.Value);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AnalogInputConfig] FlushPendingUiUpdates exception: {ex.Message}");
            }

            if (!_pendingUiUpdates.IsEmpty)
            {
                ScheduleUiFlush();
            }
        }

        private void StorePendingUiUpdate(string channelId, double? currentValue, double? frequencyHz)
        {
            channelId = NormalizeChannelId(channelId);
            if (string.IsNullOrEmpty(channelId))
                return;

            _pendingUiUpdates.AddOrUpdate(
                channelId,
                _ => new UiPendingUpdate { CurrentValue = currentValue, FrequencyHz = frequencyHz },
                (_, existing) => new UiPendingUpdate
                {
                    CurrentValue = currentValue ?? existing.CurrentValue,
                    FrequencyHz = frequencyHz ?? existing.FrequencyHz
                });

            ScheduleUiFlush();
        }

        /// <summary>
        /// 计算去直流后的 RMS（有效值），输入为空或长度0时返回0。
        /// </summary>
        private static double CalcRms(double[] x)
        {
            if (x == null || x.Length == 0) return 0;

            double mean = 0;
            for (int i = 0; i < x.Length; i++) mean += x[i];
            mean /= x.Length;

            double sumSq = 0;
            for (int i = 0; i < x.Length; i++)
            {
                double v = x[i] - mean;
                sumSq += v * v;
            }
            return Math.Sqrt(sumSq / x.Length);
        }

        /// <summary>
        /// 交叉频率估计
        /// </summary>
        public static double QuickFreqSchmitt(double[] samples, double fs,
                                              double hysteresisRatio = 0.15,
                                              double minH = 0.01)
        {
            if (samples == null || samples.Length < 10 || fs <= 0) return 0;

            // 1) 去直流
            double mean = 0;
            for (int i = 0; i < samples.Length; i++) mean += samples[i];
            mean /= samples.Length;

            // 2) 估幅值，给滞回阈值 H
            double maxAbs = 0;
            for (int i = 0; i < samples.Length; i++)
            {
                double v = Math.Abs(samples[i] - mean);
                if (v > maxAbs) maxAbs = v;
            }
            if (maxAbs < 1e-12) return 0;
            double H = Math.Max(maxAbs * hysteresisRatio, minH);

            // 3) 施密特状态机：先进入负区(<-H) 上穿 +H触发
            bool armed = (samples[0] - mean) <= -H;

            var idxs = new System.Collections.Generic.List<double>();
            for (int i = 1; i < samples.Length; i++)
            {
                double a = samples[i - 1] - mean;
                double b = samples[i] - mean;

                if (!armed)
                {
                    if (b <= -H) armed = true; 
                    continue;
                }

                // armed: 等待上穿 +H
                if (a < H && b >= H)
                {
                    double denom = b - a;
                    if (Math.Abs(denom) < 1e-12) denom = 1e-12;
                    double t = (H - a) / denom;
                    t = Math.Max(0.0, Math.Min(1.0, t));

                    idxs.Add((i - 1) + t);
                    armed = false;
                }
            }

            if (idxs.Count < 2) return 0;

            // 4) 用相邻触发间隔估周期
            var periods = new System.Collections.Generic.List<double>(idxs.Count - 1);
            for (int k = 1; k < idxs.Count; k++)
                periods.Add((idxs[k] - idxs[k - 1]) / fs);

            if (periods.Count == 0) return 0;
            periods.Sort();

            double medianT = periods[periods.Count / 2];

            // 过滤异常周期
            if (periods.Count >= 3)
            {
                var validPeriods = periods
                    .Where(p => p > medianT * 0.5 && p < medianT * 2.0)
                    .ToList();

                if (validPeriods.Count >= 2)
                {
                    validPeriods.Sort();
                    medianT = validPeriods[validPeriods.Count / 2];
                }
            }

            if (medianT <= 0) return 0;
            return 1.0 / medianT;
        }

        private static int GetFreqDecimationStep(double sampleRate)
        {
            if (sampleRate <= 0)
                return 1;

            if (sampleRate <= MaxFreqAnalysisSampleRate)
                return 1;

            int step = (int)Math.Ceiling(sampleRate / MaxFreqAnalysisSampleRate);
            if (step < 1) step = 1;
            return step;
        }

        private static double QuickFreqSchmittStepped(double[] samples, double fs, int step,
                                                      double hysteresisRatio = 0.15,
                                                      double minH = 0.01)
        {
            if (samples == null || fs <= 0)
                return 0;

            if (step <= 1)
                return QuickFreqSchmitt(samples, fs, hysteresisRatio, minH);

            int n = (samples.Length + step - 1) / step;
            if (n < 10)
                return 0;

            double sum = 0;
            int count = 0;
            for (int i = 0; i < samples.Length; i += step)
            {
                sum += samples[i];
                count++;
            }
            if (count < 10)
                return 0;

            double mean = sum / count;

            double maxAbs = 0;
            for (int i = 0; i < samples.Length; i += step)
            {
                double v = Math.Abs(samples[i] - mean);
                if (v > maxAbs) maxAbs = v;
            }
            if (maxAbs < 1e-12)
                return 0;

            double H = Math.Max(maxAbs * hysteresisRatio, minH);

            bool armed = (samples[0] - mean) <= -H;
            var idxs = new System.Collections.Generic.List<double>();

            double fsEff = fs / step;
            if (fsEff <= 0)
                return 0;

            int k = 1;
            for (int i = step; i < samples.Length; i += step, k++)
            {
                double a = samples[i - step] - mean;
                double b = samples[i] - mean;

                if (!armed)
                {
                    if (b <= -H) armed = true;
                    continue;
                }

                if (a < H && b >= H)
                {
                    double denom = b - a;
                    if (Math.Abs(denom) < 1e-12) denom = 1e-12;
                    double t = (H - a) / denom;
                    t = Math.Max(0.0, Math.Min(1.0, t));
                    idxs.Add((k - 1) + t);
                    armed = false;
                }
            }

            if (idxs.Count < 2)
                return 0;

            var periods = new System.Collections.Generic.List<double>(idxs.Count - 1);
            for (int p = 1; p < idxs.Count; p++)
            {
                periods.Add((idxs[p] - idxs[p - 1]) / fsEff);
            }
            if (periods.Count == 0)
                return 0;

            periods.Sort();
            double medianT = periods[periods.Count / 2];

            if (periods.Count >= 3)
            {
                var validPeriods = periods
                    .Where(pp => pp > medianT * 0.5 && pp < medianT * 2.0)
                    .ToList();

                if (validPeriods.Count >= 2)
                {
                    validPeriods.Sort();
                    medianT = validPeriods[validPeriods.Count / 2];
                }
            }

            if (medianT <= 0)
                return 0;
            return 1.0 / medianT;
        }

        /// <summary>
        /// 计算频率：方波化 -> 窗口平均去抖 -> 上升沿间隔，返回 Hz。
        /// </summary>
        // private static double CalcFrequencyFromSamples(double[] x, double sampleRate, int avgWindow = 5)
        // {
        //     if (x == null || x.Length < 2 || sampleRate <= 0) return 0;
        //     if (avgWindow <= 0) avgWindow = 5;

        //     // 1) 转为 ±1 方波（按 0 阈值）
        //     var square = new double[x.Length];
        //     for (int i = 0; i < x.Length; i++)
        //     {
        //         if (x[i] > 0) square[i] = 1;
        //         else if (x[i] < 0) square[i] = -1;
        //         else square[i] = 0;
        //     }

        //     // 2) 每 n 点取平均，抑制抖动
        //     var averaged = new List<double>();
        //     for (int i = 0; i < square.Length; i += avgWindow)
        //     {
        //         var len = Math.Min(avgWindow, square.Length - i);
        //         int sum = 0;
        //         for (int j = 0; j < len; j++) sum += (int)square[i + j];
        //         averaged.Add(sum / avgWindow);
        //     }

        //     // 3) 去除中间值，拉回 ±1
        //     for (int i = 0; i < averaged.Count; i++)
        //     {
        //         if (averaged[i] != 1 && averaged[i] != -1)
        //         {
        //             if (i != 0) averaged[i] = averaged[i - 1];
        //             else if (averaged.Count > 1) averaged[i] = averaged[i + 1];
        //         }
        //     }

        //     // 4) 找两个上升沿索引
        //     double rise1 = 0;
        //     double rise2 = 0;
        //     for (int i = 10; i < averaged.Count; i++)
        //     {
        //         if (i != averaged.Count - 1)
        //         {
        //             if (averaged[i] == -1 && averaged[i + 1] == 1)
        //             {
        //                 if (rise1 == 0)
        //                 {
        //                     rise1 = i;
        //                 }
        //                 else
        //                 {
        //                     if (i > rise1)
        //                     {
        //                         rise2 = i;
        //                         break;
        //                     }
        //                 }
        //             }
        //         }
        //     }

        //     if (rise1 == 0 || rise2 == 0) return 0;

        //     // 每个 averaged 点覆盖 avgWindow 原始点
        //     double samplesBetween = (rise2 - rise1) * avgWindow;
        //     if (samplesBetween <= 0) return 0;
        //     double periodSec = samplesBetween / sampleRate;
        //     if (periodSec <= 0) return 0;
        //     return 1.0 / periodSec;
        // }

        private double GetCurrentSampleRate()
        {
            if (!double.TryParse(SampleRateText, out var sr) || sr <= 0)
            {
                sr = 1000;
            }
            return sr;
        }

        #endregion

        #region 波形显示

        private static readonly Color[] ChannelColors = new[]
        {  //TODO 设计颜色
            Colors.Red, Colors.Blue, Colors.Green, Colors.Orange, Colors.Purple,
            Colors.Brown, Colors.Pink, Colors.Cyan, Colors.Magenta, Colors.Yellow,
            Colors.Lime, Colors.Aqua, Colors.Gold, Colors.Coral, Colors.Violet, Colors.Teal
        };

        /// <summary>
        /// 启动波形更新定时器
        /// </summary>
        private void StartWaveformUpdateTimer()
        {
            double interval = Math.Min(33.0, CalculateWaveformIntervalMs());
            if (interval < 5.0) interval = 5.0;

            _waveformUpdateTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(interval)
            };
            _waveformUpdateTimer.Tick += (s, e) =>
            {
                if (_hasPendingWaveformUpdate)
                {
                    UpdateWaveformDisplay();
                }
            };
            _waveformUpdateTimer.Start();
        }

        /// <summary>
        /// 计算波形重绘的间隔（毫秒），基于当前 SampleCountText / SampleRateText。
        /// interval = (sampleCount / sampleRate) * 1000 + margin，最小 5ms，最大 1000ms。
        /// </summary>
        private double CalculateWaveformIntervalMs()
        {
            double sampleRate = 0;
            int sampleCount = 0;
            double.TryParse(SampleRateText, out sampleRate);
            int.TryParse(SampleCountText, out sampleCount);
            if (sampleRate <= 0) sampleRate = 1000.0;
            double intervalMs = (sampleCount / sampleRate) * 1000.0;
            const double marginMs = 10.0;
            intervalMs += marginMs;
            if (intervalMs < 5.0) intervalMs = 5.0;
            if (intervalMs > 1000.0) intervalMs = 1000.0;
            return intervalMs;
        }

        /// <summary>
        /// 更新波形重绘定时器的间隔（当 SampleRateText 或 SampleCountText 改变时调用）
        /// </summary>
        private void UpdateWaveformTimerInterval()
        {
            double interval = Math.Min(33.0, CalculateWaveformIntervalMs());
            if (interval < 5.0) interval = 5.0;
            if (_waveformUpdateTimer != null)
            {
                _waveformUpdateTimer.Interval = TimeSpan.FromMilliseconds(interval);
            }
        }

        private void StopWaveformUpdateTimer()
        {
            _hasPendingWaveformUpdate = false;
            _waveformUpdateTimer?.Stop();
            _waveformUpdateTimer = null;
        }


        /// <summary>
        /// 处理驱动传来的整块样本（事件驱动），直接计算 RMS/频率并派发到 UI 线程。
        /// </summary>
        /// <param name="blocks">Key=ChannelId, Value=sample array</param>
        private void OnSamplesAvailable(Dictionary<string, double[]> blocks)
        {
            if (blocks == null || blocks.Count == 0) return;
            double sampleRate = GetCurrentSampleRate();

            var queue = _sampleQueue;
            if (queue == null)
            {
                System.Diagnostics.Debug.WriteLine("[AnalogInputConfig] OnSamplesAvailable ignored: _sampleQueue is null");
                return;
            }

            System.Diagnostics.Debug.WriteLine($"[AnalogInputConfig] OnSamplesAvailable: channels={blocks.Count}, sampleRate={sampleRate}");

            foreach (var kv in blocks)
            {
                var channelId = NormalizeChannelId(kv.Key);
                var samples = kv.Value;
                if (string.IsNullOrEmpty(channelId) || samples == null || samples.Length == 0)
                    continue;

                int now = Environment.TickCount;
                if (!_lastConsumeDiagTick.TryGetValue(channelId, out var lastEnq) || (now - lastEnq) >= 1000)
                {
                    _lastConsumeDiagTick[channelId] = now;
                    System.Diagnostics.Debug.WriteLine($"[AnalogInputConfig] Enqueue block: channel={channelId}, samples={samples.Length}");
                }

                var block = new SampleBlock
                {
                    ChannelId = channelId,
                    Samples = samples,
                    SampleRate = sampleRate
                };

                if (!queue.TryAdd(block))
                {
                    while (!queue.TryAdd(block))
                    {
                        if (!queue.TryTake(out _))
                            break;
                    }
                }
            }
        }
        /// <summary>
        /// 更新波形数据
        /// </summary>
        private void UpdateWaveformData(string channelId, double[] samples, double sampleRate)
        {
            if (sampleRate <= 0) sampleRate = GetCurrentSampleRate();

            int capacity = (int)Math.Ceiling(sampleRate * WaveformWindowSeconds);
            if (capacity < 1) capacity = 1;
            if (capacity < 16) capacity = 16;

            if (!_waveformData.TryGetValue(channelId, out var buffer) || buffer == null || buffer.Capacity != capacity)
            {
                buffer = new RingBuffer<double>(capacity);
                _waveformData[channelId] = buffer;
            }

            for (int i = 0; i < samples.Length; i++)
            {
                buffer.Add(samples[i]);
            }
        }

        /// <summary>
        /// 更新波形显示
        /// </summary>
        internal void UpdateWaveformDisplay()
        {
            if (!_hasPendingWaveformUpdate) return;

            var previewItems = RealTimeDataItems?.Where(item => item.IsPreviewEnabled).ToList()
                               ?? new List<RealTimeDataItem>();

            int now = Environment.TickCount;
            if (now - _lastWaveformUpdateTick < 33) return;
            _lastWaveformUpdateTick = now;

            if (_waveformPlot == null)
            {
                _hasPendingWaveformUpdate = false;
                return;
            }
            _hasPendingWaveformUpdate = false;

            var plot = _waveformPlot.Plot;
            plot.Clear();

            var previewItemsLimited = previewItems;
            if (_legendPanel != null)
            {
                _legendPanel.Children.Clear();
            }

            double sampleRate = GetCurrentSampleRate();
            plot.Axes.SetLimits(-WaveformWindowSeconds, 0, -10, 10);

            int colorIndex = 0;
            foreach (var item in previewItemsLimited)
            {
                var channelName = NormalizeChannelId(item.ChannelName);
                if (!_waveformData.TryGetValue(channelName, out var buffer) || buffer == null)
                {
                    continue;
                }

                var raw = buffer.Items().ToList();
                if (raw.Count < 2)
                {
                    continue;
                }

                if (!_channelColors.ContainsKey(channelName))
                {
                    _channelColors[channelName] = new SolidColorBrush(ChannelColors[colorIndex % ChannelColors.Length]);
                }

                var mediaColor = ((SolidColorBrush)_channelColors[channelName]).Color;
                var drawColor = new ScottPlot.Color(mediaColor.R, mediaColor.G, mediaColor.B, mediaColor.A);

                var ys = raw.ToArray();
                if (ys.Length < 2)
                {
                    continue;
                }

                double sr = sampleRate;
                if (sr <= 0) sr = 1;
                double period = 1.0 / sr;

                var sig = plot.Add.Signal(ys);
                sig.Color = drawColor;
                sig.LineWidth = 1;
                sig.MaximumMarkerSize = 0;

                sig.Data.XOffset = -(ys.Length - 1) * period;

                object dataObj = sig.Data;
                var dataType = dataObj.GetType();
                var srProp = dataType.GetProperty("SampleRate");
                if (srProp != null && srProp.CanWrite)
                {
                    srProp.SetValue(dataObj, sr);
                }
                else
                {
                    var periodProp = dataType.GetProperty("Period");
                    if (periodProp != null && periodProp.CanWrite)
                    {
                        periodProp.SetValue(dataObj, period);
                    }
                }

                if (_legendPanel != null)
                {
                    var legendItem = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(5, 2, 5, 2) };
                    var colorBox = new System.Windows.Shapes.Rectangle
                    {
                        Width = 16,
                        Height = 16,
                        Fill = _channelColors[channelName],
                        Margin = new Thickness(0, 0, 5, 0)
                    };

                    var label = new System.Windows.Controls.TextBlock
                    {
                        Text = channelName,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    legendItem.Children.Add(colorBox);
                    legendItem.Children.Add(label);
                    _legendPanel.Children.Add(legendItem);
                }

                colorIndex++;
            }

            _waveformPlot.Refresh();
        }

        private void DrawAxes(double width, double height, double minY, double maxY, double minX, double maxX)
        {
            // Y轴（电压）
            var yAxis = new Line
            {
                X1 = 40,
                Y1 = 0,
                X2 = 40,
                Y2 = height - 20,
                Stroke = Brushes.Black,
                StrokeThickness = 1
            };
            _waveformCanvas.Children.Add(yAxis);

            // X轴（时间）
            var xAxis = new Line
            {
                X1 = 40,
                Y1 = height - 20,
                X2 = width,
                Y2 = height - 20,
                Stroke = Brushes.Black,
                StrokeThickness = 1
            };
            _waveformCanvas.Children.Add(xAxis);

            // Y轴标签
            var yLabel = new System.Windows.Controls.TextBlock
            {
                Text = "电压 (V)",
                FontSize = 10,
                RenderTransform = new RotateTransform(-90),
                Margin = new Thickness(0, height / 2, 0, 0)
            };
            _waveformCanvas.Children.Add(yLabel);

            // X轴标签
            var xLabel = new System.Windows.Controls.TextBlock
            {
                Text = "样点",
                FontSize = 10,
                Margin = new Thickness(width / 2, height - 15, 0, 0)
            };
            _waveformCanvas.Children.Add(xLabel);

            // Y轴刻度
            int yTicks = 5;
            for (int i = 0; i <= yTicks; i++)
            {
                double y = height - 20 - (height - 20) * i / yTicks;
                double value = minY + (maxY - minY) * i / yTicks;
                
                var tick = new Line
                {
                    X1 = 38,
                    Y1 = y,
                    X2 = 42,
                    Y2 = y,
                    Stroke = Brushes.Black,
                    StrokeThickness = 1
                };
                _waveformCanvas.Children.Add(tick);

                var tickLabel = new System.Windows.Controls.TextBlock
                {
                    Text = value.ToString("F2"),
                    FontSize = 9,
                    Margin = new Thickness(0, y - 8, 0, 0),
                    Width = 35,
                    TextAlignment = TextAlignment.Right
                };
                _waveformCanvas.Children.Add(tickLabel);
            }

            // X轴只显示最左与最右两个标签（样点起始与结束）
            var leftLabel = new System.Windows.Controls.TextBlock
            {
                Text = FormatSampleLabel(minX),
                FontSize = 10,
                Margin = new Thickness(40, height - 15, 0, 0),
                Width = 60,
                TextAlignment = TextAlignment.Left
            };
            _waveformCanvas.Children.Add(leftLabel);

            var rightLabel = new System.Windows.Controls.TextBlock
            {
                Text = FormatSampleLabel(maxX),
                FontSize = 10,
                Margin = new Thickness(width - 60, height - 15, 0, 0),
                Width = 60,
                TextAlignment = TextAlignment.Right
            };
            _waveformCanvas.Children.Add(rightLabel);
        }

        private string FormatSampleLabel(double sampleValue)
        {
            if (sampleValue >= 1000.0)
            {
                double k = sampleValue / 1000.0;
                // Show one decimal if not integer
                if (Math.Abs(k - Math.Round(k)) < 0.0001)
                    return $"{(int)Math.Round(k)}k";
                return $"{k:F1}k";
            }
            return sampleValue.ToString("F0");
        }

        #endregion

        #region 标定导航

        /// <summary>
        /// 导航到标定页面
        /// </summary>
        private void OnNavigateToCalibration()
        {
            if (Device == null)
                return;

            try
            {
                var container = (System.Windows.Application.Current as App)?.Container;
                var regionManager = container?.Resolve(typeof(IRegionManager)) as IRegionManager;
                if (regionManager == null || !regionManager.Regions.ContainsRegionWithName(AppConstants.MainRegionName))
                {
                    ReMessageBox.Show("导航服务不可用，无法打开标定界面", "错误",
                        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                    return;
                }

                var navigationService = regionManager.Regions[AppConstants.MainRegionName].NavigationService;
                if (navigationService == null)
                {
                    ReMessageBox.Show("导航服务不可用，无法打开标定界面", "错误",
                        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                    return;
                }

                var navParams = new NavigationParameters
                {
                    { "ChassisName", ChassisName },
                    { "CardName", CardName ?? CardModel ?? "" },
                    { "ChannelName", null },
                    { "ChannelType", "AI" },
                    { "SignalName", null },
                    { "ConfigTabelName", null },
                    { "IsCalibrationNavigation", true }
                };

                navigationService.RequestNavigate(new Uri("PxiChassis", UriKind.Relative), navParams);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AnalogInputConfig] 导航到标定页面失败: {ex.Message}");
                ReMessageBox.Show($"导航到标定页面失败: {ex.Message}", "错误",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        #endregion

        #region INavigationAware Implementation

        public bool IsNavigationTarget(NavigationContext navigationContext)
        {
            return true;
        }

        public void OnNavigatedTo(NavigationContext navigationContext)
        {
            // 导航到页面时初始化波形显示
            _hasPendingWaveformUpdate = true;
            UpdateWaveformDisplay();
        }

        public void ConfirmNavigationRequest(NavigationContext navigationContext, Action<bool> continuationCallback)
        {
            if (IsBusy)
            {
                var opText = IsDeviceConnected ? "关闭" : "打开";
                ReMessageBox.Show(
                    $"正在{opText}板卡，请稍候...",
                    "提示",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
                continuationCallback(false);
                return;
            }

            if (IsDeviceConnected)
            {
                continuationCallback(true);
                return;
            }

            // 检查是否有未保存的更改
            if (HasPendingChanges)
            {
                var result = ReMessageBox.Show(
                    "配置有未保存的更改，是否要保存？",
                    "保存确认",
                    System.Windows.MessageBoxButton.YesNoCancel,
                    System.Windows.MessageBoxImage.Question);

                switch (result)
                {
                    case System.Windows.MessageBoxResult.Yes:
                        // 用户选择保存
                        if (SaveCurrentTaskConfig())
                        {
                            continuationCallback(true); // 允许导航
                        }
                        else
                        {
                            continuationCallback(false); // 保存失败（例如未选择测试任务），阻止导航
                        }
                        break;
                    case System.Windows.MessageBoxResult.Cancel:
                        // 用户取消，阻止导航
                        continuationCallback(false); // 取消导航
                        break;
                    case System.Windows.MessageBoxResult.No:
                        // 用户选择不保存，允许导航
                        HasPendingChanges = false;
                        continuationCallback(true); // 允许导航
                        break;
                }
            }
            else
            {
                // 没有未保存的更改，直接允许导航
                continuationCallback(true);
            }
        }

        public bool CanClose()
        {
            if (IsBusy)
            {
                var opText = IsDeviceConnected ? "关闭" : "打开";
                ReMessageBox.Show(
                    $"正在{opText}板卡，请稍候...",
                    "提示",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
                return false;
            }

            if (IsDeviceConnected)
            {
                return true;
            }

            return EnsurePendingChangesHandled();
        }

        public void OnNavigatedFrom(NavigationContext navigationContext)
        {
            // 取消事件订阅，避免内存泄漏
            _eventAggregator?.GetEvent<TestTaskCreatedEvent>()?.Unsubscribe(OnTestTaskCreated);
        }

        #endregion

        /// <summary>
        /// 处理驱动采集状态改变事件
        /// </summary>
        private void OnDriverAcquisitionStatusChanged(object sender, AcquisitionStatusChangedEventArgs e)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                IsAcquisitionRunning = e.IsRunning;
                System.Diagnostics.Debug.WriteLine($"[AnalogInputConfig] 驱动采集状态改变: IsRunning={e.IsRunning}, Mode={e.AcquisitionMode}");
            });
        }

        /// <summary>
        /// 供嵌套类调用的公共方法，用于触发属性变更通知
        /// </summary>
        public void NotifyAllPreviewEnabledChanged()
        {
            RaisePropertyChanged(nameof(IsAllPreviewEnabled));
        }

    }

    public class ChannelInfo : BindableBase
    {
        private string _channelName;
        private bool _isEnabled;
        private string _range;
        private ObservableCollection<string> _availableRanges;
        private string _currentValue;
        private string _unit;
        private string _status;

        public string ChannelName
        {
            get => _channelName;
            set => SetProperty(ref _channelName, value);
        }

        public bool IsEnabled
        {
            get => _isEnabled;
            set => SetProperty(ref _isEnabled, value);
        }

        public string Range
        {
            get => _range;
            set => SetProperty(ref _range, value);
        }

        public ObservableCollection<string> AvailableRanges
        {
            get => _availableRanges;
            set => SetProperty(ref _availableRanges, value);
        }

        public string CurrentValue
        {
            get => _currentValue;
            set => SetProperty(ref _currentValue, value);
        }

        public string Unit
        {
            get => _unit;
            set => SetProperty(ref _unit, value);
        }

        public string Status
        {
            get => _status;
            set => SetProperty(ref _status, value);
        }

        public ChannelInfo()
        {
            AvailableRanges = new ObservableCollection<string>();
        }
    }

    public class RealTimeDataItem : BindableBase
    {
        private readonly PXI9774_AIViewModel _parent;
        private string _channelName;
        private string _signalName;
        private string _currentValue;
        private string _frequency;
        private string _unit;
        private string _status;
        private bool _isPreviewEnabled;
        private bool _isDcCurrentValue = true;

        public RealTimeDataItem(PXI9774_AIViewModel parent)
        {
            _parent = parent;
        }

        public string ChannelName
        {
            get => _channelName;
            set => SetProperty(ref _channelName, value);
        }

        public string SignalName
        {
            get => _signalName;
            set => SetProperty(ref _signalName, value);
        }

        public string CurrentValue
        {
            get => _currentValue;
            set => SetProperty(ref _currentValue, value);
        }

        public string Frequency
        {
            get => _frequency;
            set => SetProperty(ref _frequency, value);
        }

        public string Unit
        {
            get => _unit;
            set => SetProperty(ref _unit, value);
        }

        public string Status
        {
            get => _status;
            set => SetProperty(ref _status, value);
        }

        public bool IsPreviewEnabled
        {
            get => _isPreviewEnabled;
            set
            {
                if (SetProperty(ref _isPreviewEnabled, value))
                {
                    // 预览状态变化时立即更新波形显示
                    Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (_parent.HasWaveformCanvas)
                        {
                            _parent._hasPendingWaveformUpdate = true;
                            _parent.UpdateWaveformDisplay();
                        }
                        // 更新全勾选状态
                        _parent.NotifyAllPreviewEnabledChanged();
                    }), DispatcherPriority.Normal);
                }
            }
        }

        public bool IsDcCurrentValue
        {
            get => _isDcCurrentValue;
            set => SetProperty(ref _isDcCurrentValue, value);
        }

    }

}
