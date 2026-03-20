using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MeasureControl.Models;
using MeasureControl.Models.Devices;
using static MeasureControl.Helpers.ArtDAQ;

namespace MeasureControl.Drivers
{
    /// <summary>
    /// 阿尔泰 PXIe-9774 同步采样模拟量输入驱动
    /// 支持 32 通道同步采样，最高 2 MS/s
    /// </summary>
    public class Art9774Driver : IDeviceDriver
    {
        #region 事件

        /// <summary>
        /// 采集状态改变事件
        /// </summary>
        public event EventHandler<AcquisitionStatusChangedEventArgs> AcquisitionStatusChanged;

        #endregion

        #region 私有字段

        private readonly DeviceBase _device;
        private bool _isConnected;
        private bool _isAcquisitionRunning;
        private IntPtr _taskHandle = IntPtr.Zero;
        private string _deviceName = "Dev2"; // 第一套
        //private string _deviceName = "Dev1"; // 第二套
        //private string _deviceName = "Dev2"; // 第三套
        private Task _acquisitionTask;
        private CancellationTokenSource _acquisitionCancellationTokenSource;
        // Event-driven samples processing
        private ArtDAQ_EveryNSamplesEventCallbackPtr _everyCallback;
        private readonly System.Collections.Concurrent.ConcurrentQueue<Dictionary<string, double[]>> _samplesQueue = new System.Collections.Concurrent.ConcurrentQueue<Dictionary<string, double[]>>();
        private const int MaxSamplesQueueDepth = 8;
        private int _samplesQueueDepth;
        private int _callbackInProgress;
        private CancellationTokenSource _samplesProcessingCts;
        private Task _samplesProcessingTask;
        private List<string> _lastEnabledChannelIds = new List<string>();

        // 采样参数
        private double _sampleRate = 1000.0; // Hz
        private string _acquisitionMode = "有限点"; // 有限点 或 连续采样
        private int _sampleCount = 1000; // 有限点模式下的采样数
        // 9774 硬件限制
        private const double Art9774_MaxSampleRate = 500_000.0; // 500 kS/s
        private const int Art9774_MaxBufferPerChannel = 16_384; // 建议的每通道暂存上限（16K）

        // 通道配置：通道ID -> 配置信息
        private readonly Dictionary<string, ChannelConfig> _channelConfigs = new Dictionary<string, ChannelConfig>();

        // 实时数据：通道ID -> 当前值
        private readonly Dictionary<string, double> _channelValues = new Dictionary<string, double>();
        private readonly object _dataLock = new object();

        // 量程映射
        private static readonly Dictionary<string, (double min, double max)> RangeMap = new Dictionary<string, (double, double)>
        {
            { "±10V", (-10.0, 10.0) },
            { "±5V", (-5.0, 5.0) },
            { "±2V", (-2.0, 2.0) },
            { "±1V", (-1.0, 1.0) }
        };

        // 终端配置（默认使用驱动默认配置）
        private Int32 _terminalConfig = ArtDAQ_Val_Cfg_Default;

        #endregion

        #region 属性

        public string DeviceId => _device?.Id ?? string.Empty;

        public string DeviceName => _device?.Name ?? "ART9774";

        public bool IsConnected => _isConnected;

        public bool IsSimulated => false;

        /// <summary>
        /// Art9774是数据采集设备
        /// </summary>
        public DeviceCapability Capability => DeviceCapability.Input;
        public bool SupportsEveryNSamples => true;

        /// <summary>
        /// 当驱动读取到一块样本时触发（每通道数组），Key=ChannelId, Value=array of samples for that channel
        /// 订阅者应注意线程上下文（事件在驱动线程或处理任务线程触发，UI 订阅者需在 Dispatcher 上处理）。
        /// </summary>
        public event Action<Dictionary<string, double[]>> SamplesAvailable;

        #endregion

        #region 构造函数

        public Art9774Driver(DeviceBase device)
        {
            _device = device ?? throw new ArgumentNullException(nameof(device));
            _isConnected = false;
            _isAcquisitionRunning = false;

            // 从设备名称中提取设备标识（如 "Dev1"）
            ExtractDeviceName();

            // 初始化通道配置（32通道）
            InitializeChannels();
        }

        private void ExtractDeviceName()
        {
            // 尝试从设备名称或CardName中提取设备标识
            // 例如：如果CardName包含"Dev1"或类似标识
            if (_device != null)
            {
                string name = _device.CardName ?? _device.Name ?? "";
                // 简单提取：查找"Dev"开头的标识
                var parts = name.Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var part in parts)
                {
                    if (part.StartsWith("Dev", StringComparison.OrdinalIgnoreCase))
                    {
                        _deviceName = part;
                        break;
                    }
                }
            }
        }

        private void InitializeChannels()
        {
            // 初始化32个通道（AI0-AI31）
            for (int i = 0; i < 32; i++)
            {
                string channelId = $"AI{i}";
                _channelConfigs[channelId] = new ChannelConfig
                {
                    ChannelId = channelId,
                    PhysicalChannel = $"{_deviceName}/ai{i}",
                    IsEnabled = false,
                    Range = "±10V",
                    MinValue = -10.0,
                    MaxValue = 10.0
                };
                _channelValues[channelId] = 0.0;
            }
        }

        #endregion

        #region IDeviceDriver 实现

        public async Task<bool> ConnectAsync()
        {
            if (_isConnected)
                return true;

            const int maxRetries = 2;
            const int retryDelayMs = 500;

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    Debug.WriteLine($"[Art9774Driver] Attempting to connect {DeviceName}, DeviceName={_deviceName}, 尝试 {attempt}/{maxRetries}");

                    DEVICE_EEP_INFO devInfo = new DEVICE_EEP_INFO();
                    Int32 devInfoErr = ArtDAQ_GetDeviceEEPInfo(_deviceName, ref devInfo);
                    if (devInfoErr != ArtDAQSuccess)
                    {
                        GetErrorString(devInfoErr);
                        Debug.WriteLine($"[Art9774Driver] Device check failed: {_deviceName}, Err={devInfoErr}");
                        _isConnected = false;
                        
                        if (attempt < maxRetries)
                        {
                            Debug.WriteLine($"[Art9774Driver] 等待 {retryDelayMs}ms 后重试...");
                            await Task.Delay(retryDelayMs);
                            continue;
                        }
                        return false;
                    }

                    // 进行一次轻量的创建任务/创建通道自检，尽早发现 CreateTask/CreateChannel/Timing 等错误
                    IntPtr tmpTask = IntPtr.Zero;
                    bool selfTestFailed = false;
                    try
                    {
                        Int32 err = ArtDAQ_CreateTask("selftest_temp_task", out tmpTask);
                        if (err < 0)
                        {
                            GetErrorString(err);
                            Debug.WriteLine($"[Art9774Driver] Self-test: CreateTask failed: {err}");
                            selfTestFailed = true;
                        }
                        else
                        {
                            // 选择一个默认物理通道进行创建测试（Dev?/ai0）
                            string physChannel = $"{_deviceName}/ai0";
                            err = ArtDAQ_CreateAIVoltageChan(tmpTask, physChannel, "", ArtDAQ_Val_Cfg_Default, -10.0, 10.0, ArtDAQ_Val_Volts, null);
                            if (err < 0)
                            {
                                GetErrorString(err);
                                Debug.WriteLine($"[Art9774Driver] Self-test: CreateAIVoltageChan failed for {physChannel}: {err}");
                                selfTestFailed = true;
                            }
                            else
                            {
                                // 尝试配置时钟（短暂配置用于检测参数兼容性）
                                err = ArtDAQ_CfgSampClkTiming(tmpTask, "", Math.Max(1000.0, _sampleRate), ArtDAQ_Val_Rising, ArtDAQ_Val_FiniteSamps, 10);
                                if (err < 0)
                                {
                                    GetErrorString(err);
                                    Debug.WriteLine($"[Art9774Driver] Self-test: CfgSampClkTiming failed: {err}");
                                    selfTestFailed = true;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[Art9774Driver] Self-test exception: {ex.Message}");
                        selfTestFailed = true;
                    }
                    finally
                    {
                        try
                        {
                            if (tmpTask != IntPtr.Zero)
                            {
                                ArtDAQ_ClearTask(tmpTask);
                                tmpTask = IntPtr.Zero;
                            }
                        }
                        catch { }
                    }

                    if (selfTestFailed)
                    {
                        Debug.WriteLine($"[Art9774Driver] Self-test failed (尝试 {attempt}/{maxRetries})");
                        _isConnected = false;
                        
                        if (attempt < maxRetries)
                        {
                            Debug.WriteLine($"[Art9774Driver] 等待 {retryDelayMs}ms 后重试...");
                            await Task.Delay(retryDelayMs);
                            continue;
                        }
                        return false;
                    }

                    _isConnected = true;
                    await Task.Delay(100); // 增加稳定延时
                    Debug.WriteLine($"[Art9774Driver] 连接成功");
                    return true;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Art9774Driver] Connect exception (尝试 {attempt}/{maxRetries}): {ex.Message}");
                    _isConnected = false;
                    
                    if (attempt < maxRetries)
                    {
                        Debug.WriteLine($"[Art9774Driver] 等待 {retryDelayMs}ms 后重试...");
                        await Task.Delay(retryDelayMs);
                    }
                }
            }

            Debug.WriteLine($"[Art9774Driver] 连接失败，已重试 {maxRetries} 次");
            return false;
        }

        public async Task<bool> DisconnectAsync()
        {
            try
            {
                Debug.WriteLine($"[Art9774Driver] 断开设备 {DeviceName}");

                // 如果正在采集，先停止
                if (_isAcquisitionRunning)
                {
                    await StopAcquisitionAsync();
                }

                // 清理任务
                if (_taskHandle != IntPtr.Zero)
                {
                    ArtDAQ_StopTask(_taskHandle);
                    ArtDAQ_ClearTask(_taskHandle);
                    _taskHandle = IntPtr.Zero;
                }

                _isConnected = false;
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Art9774Driver] 断开失败: {ex.Message}");
                return false;
            }
        }

        public Task<double> ReadChannelAsync(string channelId)
        {
            lock (_dataLock)
            {
                return Task.FromResult(_channelValues.ContainsKey(channelId) ? _channelValues[channelId] : 0.0);
            }
        }

        /// <summary>
        /// 设置通道的当前值（由ViewModel在处理数据块后调用）
        /// </summary>
        /// <param name="channelId">通道ID</param>
        /// <param name="value">当前值</param>
        public void SetChannelValue(string channelId, double value)
        {
            lock (_dataLock)
            {
                _channelValues[channelId] = value;
            }
        }

        public Task<Dictionary<string, double>> ReadChannelsBatchAsync(IEnumerable<string> channelIds)
        {
            var result = new Dictionary<string, double>();
            lock (_dataLock)
            {
                foreach (var id in channelIds)
                {
                    result[id] = _channelValues.ContainsKey(id) ? _channelValues[id] : 0.0;
                }
            }
            return Task.FromResult(result);
        }

        public Task<bool> WriteChannelAsync(string channelId, double value)
        {
            // ART9774是输入设备，不支持写入
            return Task.FromResult(false);
        }

        public Task<bool> WriteChannelsBatchAsync(Dictionary<string, double> channelValues)
        {
            // ART9774是输入设备，不支持写入
            return Task.FromResult(false);
        }

        public Task<bool> ConfigureChannelAsync(string channelId, Dictionary<string, object> config)
        {
            if (!_channelConfigs.ContainsKey(channelId))
                return Task.FromResult(false);

            var channelConfig = _channelConfigs[channelId];

            // 更新配置
            if (config.ContainsKey("Range") && config["Range"] is string range)
            {
                channelConfig.Range = range;
                if (RangeMap.ContainsKey(range))
                {
                    var (min, max) = RangeMap[range];
                    channelConfig.MinValue = min;
                    channelConfig.MaxValue = max;
                }
            }

            if (config.ContainsKey("IsEnabled") && config["IsEnabled"] is bool enabled)
            {
                channelConfig.IsEnabled = enabled;
            }

            return Task.FromResult(true);
        }

        public Task<bool> StartAcquisitionAsync()
        {
            if (!_isConnected)
            {
                Debug.WriteLine("[Art9774Driver] 设备未连接，无法启动采集");
                return Task.FromResult(false);
            }

            if (_isAcquisitionRunning)
            {
                Debug.WriteLine("[Art9774Driver] 采集已在运行");
                return Task.FromResult(true);
            }

            try
            {
                // 配置通道
                var enabledChannels = _channelConfigs.Values.Where(c => c.IsEnabled).ToList();
                if (enabledChannels.Count == 0)
                {
                    Debug.WriteLine("[Art9774Driver] 没有启用的通道");
                    return Task.FromResult(false);
                }

                // 清除之前的通道配置（如果任务已存在）
                if (_taskHandle != IntPtr.Zero)
                {
                    ArtDAQ_StopTask(_taskHandle);
                    ArtDAQ_ClearTask(_taskHandle);
                    _taskHandle = IntPtr.Zero;
                }

                // 重新创建任务
                Int32 error = ArtDAQ_CreateTask("testpanel", out _taskHandle);
                if (error < 0)
                {
                    GetErrorString(error);
                    return Task.FromResult(false);
                }

                // 创建所有启用的通道
                foreach (var channel in enabledChannels)
                {
                    error = ArtDAQ_CreateAIVoltageChan(
                        _taskHandle,
                        channel.PhysicalChannel,
                        "",
                        _terminalConfig,
                        channel.MinValue,
                        channel.MaxValue,
                        ArtDAQ_Val_Volts,
                        null);

                    if (error < 0)
                    {
                        GetErrorString(error);
                        ArtDAQ_ClearTask(_taskHandle);
                        _taskHandle = IntPtr.Zero;
                        return Task.FromResult(false);
                    }
                }

                // 配置采样时钟
                Int32 sampleMode = _acquisitionMode == "连续采样"
                    ? ArtDAQ_Val_ContSamps
                    : ArtDAQ_Val_FiniteSamps;

                error = ArtDAQ_CfgSampClkTiming(
                    _taskHandle,
                    "", // 内部时钟
                    _sampleRate,
                    ArtDAQ_Val_Rising,
                    sampleMode,
                    _sampleCount);

                if (error < 0)
                {
                    GetErrorString(error);
                    ArtDAQ_ClearTask(_taskHandle);
                    _taskHandle = IntPtr.Zero;
                    return Task.FromResult(false);
                }

                // 对于连续采集，在启动任务前注册事件回调
                if (_acquisitionMode == "连续采样")
                {
                    Debug.WriteLine("[Art9774Driver] 注册连续采集事件回调");

                    // 设置启用的通道ID列表（用于回调函数）
                    _lastEnabledChannelIds = enabledChannels.Select(c => c.ChannelId).ToList();

                    // 注册每 _sampleCount 个样本的事件回调
                    _everyCallback = new ArtDAQ_EveryNSamplesEventCallbackPtr(EveryNSamplesCallback);
                    Int32 regErr = ArtDAQ_RegisterEveryNSamplesEvent(_taskHandle, ArtDAQ_Val_Acquired_Into_Buffer, (UInt32)_sampleCount, 0, _everyCallback, IntPtr.Zero);
                    if (regErr < 0)
                    {
                        GetErrorString(regErr);
                        Debug.WriteLine($"[Art9774Driver] 注册事件回调失败: {regErr}");
                        ArtDAQ_ClearTask(_taskHandle);
                        _taskHandle = IntPtr.Zero;
                        return Task.FromResult(false);
                    }

                    // 启动样本处理任务
                    _samplesProcessingCts = new CancellationTokenSource();
                    _samplesProcessingTask = Task.Run(() => ProcessSamplesQueue(_samplesProcessingCts.Token));
                }
                else if (_acquisitionMode == "有限点")
                {
                    // 有限点模式：启动后台采集任务
                    _acquisitionCancellationTokenSource = new CancellationTokenSource();
                    _acquisitionTask = Task.Run(() => FiniteAcquisitionAsync(_acquisitionCancellationTokenSource.Token));
                }

                // 启动任务
                error = ArtDAQ_StartTask(_taskHandle);
                if (error < 0)
                {
                    GetErrorString(error);
                    ArtDAQ_ClearTask(_taskHandle);
                    _taskHandle = IntPtr.Zero;
                    return Task.FromResult(false);
                }

                // 设置采集运行状态
                _isAcquisitionRunning = true;

                // 触发采集状态改变事件，通知ViewModel采集已开始
                AcquisitionStatusChanged?.Invoke(this, new AcquisitionStatusChangedEventArgs
                {
                    IsRunning = true,
                    AcquisitionMode = _acquisitionMode
                });

                // 设置采集运行状态
                _isAcquisitionRunning = true;
                Debug.WriteLine($"[Art9774Driver] 采集已启动，采样率={_sampleRate}Hz, 模式={_acquisitionMode}, 通道数={enabledChannels.Count}");
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Art9774Driver] 启动采集失败: {ex.Message}");
                _isAcquisitionRunning = false;
                return Task.FromResult(false);
            }
        }

        public async Task<bool> StopAcquisitionAsync()
        {
            if (!_isAcquisitionRunning)
                return true;

            try
            {
                // 停止后台任务（轮询或事件处理）
                _acquisitionCancellationTokenSource?.Cancel();
                if (_acquisitionTask != null)
                {
                    await _acquisitionTask;
                    _acquisitionTask = null;
                }
                _acquisitionCancellationTokenSource?.Dispose();
                _acquisitionCancellationTokenSource = null;

                // 停止事件驱动的样本处理
                if (_samplesProcessingCts != null)
                {
                    _samplesProcessingCts.Cancel();
                    try
                    {
                        _samplesProcessingTask?.Wait(1000);
                    }
                    catch { }
                    _samplesProcessingTask = null;
                    _samplesProcessingCts.Dispose();
                    _samplesProcessingCts = null;
                }

                // 清空样本队列，避免残留旧数据
                while (_samplesQueue.TryDequeue(out _)) { }
                _samplesQueueDepth = 0;

                // 停止硬件任务
                if (_taskHandle != IntPtr.Zero)
                {
                    ArtDAQ_StopTask(_taskHandle);
                    ArtDAQ_ClearTask(_taskHandle);
                    _taskHandle = IntPtr.Zero;
                }

                _isAcquisitionRunning = false;

                // 触发采集状态改变事件，通知ViewModel采集已停止
                AcquisitionStatusChanged?.Invoke(this, new AcquisitionStatusChangedEventArgs
                {
                    IsRunning = false,
                    AcquisitionMode = _acquisitionMode
                });

                Debug.WriteLine("[Art9774Driver] 采集已停止");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Art9774Driver] 停止采集失败: {ex.Message}");
                // 即使出现异常也要重置状态，避免下次启动失败
                _isAcquisitionRunning = false;
                return false;
            }
        }

        public Task<Dictionary<string, object>> GetStatusAsync()
        {
            // 同步计算状态并封装为Task，避免无await的async警告
            var status = new Dictionary<string, object>
            {
                { "IsConnected", _isConnected },
                { "IsAcquisitionRunning", _isAcquisitionRunning },
                { "SampleRate", _sampleRate },
                { "AcquisitionMode", _acquisitionMode },
                { "SampleCount", _sampleCount },
                { "EnabledChannels", _channelConfigs.Values.Count(c => c.IsEnabled) }
            };

            return Task.FromResult(status);
        }

        public async Task<bool> ResetAsync()
        {
            await StopAcquisitionAsync();
            await DisconnectAsync();
            return await ConnectAsync();
        }

        public async Task<bool> SelfTestAsync()
        {
            // 简单的自检：检查设备是否可连接
            if (!_isConnected)
            {
                return await ConnectAsync();
            }
            return true;
        }

        #endregion

        #region 私有方法

        /// <summary>
        /// 配置采样参数（由ViewModel调用）
        /// </summary>
        public void ConfigureAcquisition(double sampleRate, string acquisitionMode, int sampleCount)
        {
            // 限制采样率到硬件上限
            if (sampleRate > Art9774_MaxSampleRate)
            {
                Debug.WriteLine($"[Art9774Driver] Requested sampleRate {sampleRate} exceeds hardware max {Art9774_MaxSampleRate}. Clamping.");
                sampleRate = Art9774_MaxSampleRate;
            }

            // 限制每通道缓冲/采样点数到安全上限
            if (sampleCount > Art9774_MaxBufferPerChannel)
            {
                Debug.WriteLine($"[Art9774Driver] Requested sampleCount {sampleCount} exceeds per-channel buffer max {Art9774_MaxBufferPerChannel}. Clamping.");
                sampleCount = Art9774_MaxBufferPerChannel;
            }

            _sampleRate = sampleRate;
            _acquisitionMode = acquisitionMode;
            _sampleCount = sampleCount;
        }

        private int GetSamplesPerReadCount()
        {
            // Ensure read count not exceed hardware per-channel buffer and at least 1
            return Math.Max(1, Math.Min(_sampleCount, Art9774_MaxBufferPerChannel));
        }

        private int CalculateReadIntervalMilliseconds(int samplesPerChannel)
        {
            if (_sampleRate <= 0)
            {
                return 50;
            }

            double blockDurationMs = (samplesPerChannel / _sampleRate) * 1000.0;
            return blockDurationMs < 1.0 ? 1 : (int)Math.Round(blockDurationMs);
        }

        /// <summary>
        /// 对外调用：启动有限点采样（将参数写入驱动并启动）
        /// </summary>
        public Task<bool> StartFiniteAcquisitionAsync(double sampleRate, int sampleCount, Int32 terminalConfig = ArtDAQ_Val_Cfg_Default)
        {
            // 将采样参数设置到驱动内
            ConfigureAcquisition(sampleRate, "有限点", sampleCount);
            _terminalConfig = terminalConfig;
            return StartAcquisitionAsync();
        }

        /// <summary>
        /// 对外调用：启动连续采样（将参数写入驱动并启动）
        /// </summary>
        public Task<bool> StartContinuousAcquisitionAsync(double sampleRate, int bufferSizePerChannel, Int32 terminalConfig = ArtDAQ_Val_Cfg_Default)
        {
            // 对连续模式，sampleCount 字段用于表示每通道缓冲区大小
            ConfigureAcquisition(sampleRate, "连续采样", bufferSizePerChannel);
            _terminalConfig = terminalConfig;
            return StartAcquisitionAsync();
        }

        /// <summary>
        /// 有限点采集：一次性执行采集和读取
        /// </summary>
        private void FiniteAcquisitionAsync(CancellationToken cancellationToken)
        {
            try
            {
                Debug.WriteLine($"[Art9774Driver] 有限点模式开始，样本数={_sampleCount}, 采样率={_sampleRate}");

                // 计算预计采集时间（毫秒）
                double expectedDurationMs = (_sampleCount / _sampleRate) * 1000.0;
                int timeoutMs = (int)(expectedDurationMs * 2.0) + 1000; // 给双倍时间加上1秒缓冲

                Debug.WriteLine($"[Art9774Driver] 预计采集时间: {expectedDurationMs}ms, 等待超时: {timeoutMs}ms");

                // 等待任务完成
                Int32 waitError = ArtDAQ_WaitUntilTaskDone(_taskHandle, timeoutMs / 1000.0);
                if (waitError < 0)
                {
                    GetErrorString(waitError);
                    Debug.WriteLine($"[Art9774Driver] WaitUntilTaskDone failed: {waitError}");
                    return;
                }

                Debug.WriteLine("[Art9774Driver] WaitUntilTaskDone 成功，准备读取数据");

                // 获取启用的通道
                var enabledChannels = _channelConfigs.Values
                    .Where(c => c.IsEnabled)
                    .OrderBy(c => c.ChannelId)
                    .ToList();
                int channelCount = enabledChannels.Count;

                if (channelCount == 0)
                {
                    Debug.WriteLine("[Art9774Driver] 有限点模式：没有启用的通道");
                    return;
                }

                // 一次性读取所有样本
                double[] data = new double[_sampleCount * channelCount];
                Int32 readError = ArtDAQ_ReadAnalogF64(
                    _taskHandle,
                    _sampleCount,
                    10.0, // 较长的超时时间确保读取完成
                    ArtDAQ_Val_GroupByChannel,
                    data,
                    (UInt32)data.Length,
                    out Int32 samplesRead,
                    IntPtr.Zero);

                if (readError >= 0 && samplesRead > 0)
                {
                    // 将样本按通道拆分 (GroupByChannel模式：先是通道0的所有样本，然后是通道1的所有样本...)
                    var dict = new Dictionary<string, double[]>();
                    for (int i = 0; i < channelCount; i++)
                    {
                        double[] arr = new double[samplesRead];
                        for (int j = 0; j < samplesRead; j++)
                        {
                            arr[j] = data[i * samplesRead + j];
                        }
                        dict[enabledChannels[i].ChannelId] = arr;
                    }

                    // 有限点模式直接触发 SamplesAvailable，避免依赖队列线程
                    try
                    {
                        SamplesAvailable?.Invoke(dict);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[Art9774Driver] SamplesAvailable handler exception (finite): {ex.Message}");
                    }

                    Debug.WriteLine($"[Art9774Driver] 成功读取 {samplesRead} 个样本，通道数={channelCount}");
                }
                else if (readError < 0)
                {
                    GetErrorString(readError);
                    Debug.WriteLine($"[Art9774Driver] Read error: {readError}");
                }

                // 停止任务
                try
                {
                    if (_taskHandle != IntPtr.Zero)
                    {
                        ArtDAQ_StopTask(_taskHandle);
                        ArtDAQ_ClearTask(_taskHandle);
                        _taskHandle = IntPtr.Zero;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Art9774Driver] 停止有限点任务失败: {ex.Message}");
                }

                _isAcquisitionRunning = false;

                // 触发采集状态改变事件，通知ViewModel采集已完成
                AcquisitionStatusChanged?.Invoke(this, new AcquisitionStatusChangedEventArgs
                {
                    IsRunning = false,
                    AcquisitionMode = "有限点"
                });

                Debug.WriteLine("[Art9774Driver] 有限点采集完成，已停止任务");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Art9774Driver] FiniteAcquisitionAsync 异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 采集循环 - 连续模式使用
        /// </summary>
        private void AcquisitionLoop(CancellationToken cancellationToken)
        {
            try
            {
                Debug.WriteLine("[Art9774Driver] AcquisitionLoop 启动");
                while (!cancellationToken.IsCancellationRequested)
                {
                    if (_taskHandle == IntPtr.Zero)
                    {
                        Debug.WriteLine("[Art9774Driver] AcquisitionLoop: 任务句柄为空，退出循环");
                        break;
                    }

                    var enabledChannels = _channelConfigs.Values
                        .Where(c => c.IsEnabled)
                        .OrderBy(c => c.ChannelId)
                        .ToList();
                    int channelCount = enabledChannels.Count;
                    if (channelCount == 0)
                    {
                        Thread.Sleep(50);
                        continue;
                    }

                    // 连续模式的逻辑
                    int samplesPerChannelPerRead = GetSamplesPerReadCount();
                    double[] data = new double[samplesPerChannelPerRead * channelCount];
                    Int32 error = ArtDAQ_ReadAnalogF64(
                        _taskHandle,
                        samplesPerChannelPerRead,
                        1.0,
                        ArtDAQ_Val_GroupByChannel,
                        data,
                        (UInt32)data.Length,
                        out Int32 samplesRead,
                        IntPtr.Zero);

                    int actualSamples = samplesRead > 0 ? samplesRead : samplesPerChannelPerRead;

                    if (error >= 0 && samplesRead > 0)
                    {
                        // 将整块样本按通道拆分并入队 (GroupByChannel模式：先是通道0的所有样本，然后是通道1的所有样本...)
                        var dict = new Dictionary<string, double[]>();
                        for (int i = 0; i < channelCount; i++)
                        {
                            double[] arr = new double[samplesRead];
                            for (int j = 0; j < samplesRead; j++)
                            {
                                arr[j] = data[i * samplesRead + j];
                            }
                            dict[enabledChannels[i].ChannelId] = arr;
                        }

                        // Enqueue 到 samples queue，后台任务会触发 SamplesAvailable 事件
                        _samplesQueue.Enqueue(dict);
                        int depth = Interlocked.Increment(ref _samplesQueueDepth);
                        while (depth > MaxSamplesQueueDepth && _samplesQueue.TryDequeue(out _))
                        {
                            depth = Interlocked.Decrement(ref _samplesQueueDepth);
                        }
                        Debug.WriteLine($"[Art9774Driver] 连续模式读取 {samplesRead} 个样本，通道数={channelCount}");
                    }
                    else if (error < 0)
                    {
                        GetErrorString(error);
                        Debug.WriteLine($"[Art9774Driver] 连续模式读取错误: {error}");
                    }

                    Thread.Sleep(CalculateReadIntervalMilliseconds(actualSamples));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Art9774Driver] Acquisition loop exception: {ex.Message}");
            }
        }

        /// <summary>
        /// 每 N 样本事件回调（注册给底层 SDK）
        /// 回调中读取整块数据并放入队列，由后台任务转发为 SamplesAvailable 事件。
        /// </summary>
        private Int32 EveryNSamplesCallback(IntPtr taskHandle, Int32 everyNsamplesEventType, UInt32 nSamples, IntPtr callbackData)
        {
            if (Interlocked.Exchange(ref _callbackInProgress, 1) != 0)
                return 0;

            try
            {
                Debug.WriteLine($"[Art9774Driver] 回调函数触发: samples={nSamples}, channels={_lastEnabledChannelIds.Count}");

                if (_taskHandle == IntPtr.Zero) return 0;
                int samplesPerChannel = (int)nSamples;
                int channelCount = _lastEnabledChannelIds.Count;
                if (channelCount == 0) return 0;

                double[] data = new double[samplesPerChannel * channelCount];
                Int32 read = 0;
                Int32 err = ArtDAQ_ReadAnalogF64(taskHandle, samplesPerChannel, 0.1, ArtDAQ_Val_GroupByChannel, data, (UInt32)data.Length, out read, IntPtr.Zero);
                if (err < 0)
                {
                    GetErrorString(err);
                    return 0;
                }

                int actualSamples = read > 0 ? read : samplesPerChannel;
                var dict = new Dictionary<string, double[]>();
                for (int i = 0; i < channelCount; i++)
                {
                    double[] arr = new double[actualSamples];
                    for (int j = 0; j < actualSamples; j++)
                    {
                        arr[j] = data[i * actualSamples + j];
                    }
                    dict[_lastEnabledChannelIds[i]] = arr;
                }

                _samplesQueue.Enqueue(dict);
                int depth = Interlocked.Increment(ref _samplesQueueDepth);
                while (depth > MaxSamplesQueueDepth && _samplesQueue.TryDequeue(out _))
                {
                    depth = Interlocked.Decrement(ref _samplesQueueDepth);
                }
                Debug.WriteLine($"[Art9774Driver] 回调数据已入队: {actualSamples} 个样本，{channelCount} 个通道");
                return 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Art9774Driver] EveryNSamplesCallback exception: {ex.Message}");
                return 0;
            }
            finally
            {
                Interlocked.Exchange(ref _callbackInProgress, 0);
            }
        }

        private void ProcessSamplesQueue(CancellationToken token)
        {
            try
            {
                int lastDataTick = Environment.TickCount;
                int lastWarnTick = lastDataTick;
                while (!token.IsCancellationRequested)
                {
                    if (_samplesQueue.TryDequeue(out var dict))
                    {
                        Interlocked.Decrement(ref _samplesQueueDepth);
                        lastDataTick = Environment.TickCount;
                        try
                        {
                            SamplesAvailable?.Invoke(dict);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[Art9774Driver] SamplesAvailable event handler exception: {ex.Message}");
                        }
                    }
                    else
                    {
                        int now = Environment.TickCount;
                        if (unchecked((uint)(now - lastDataTick)) >= 2000u && unchecked((uint)(now - lastWarnTick)) >= 2000u)
                        {
                            lastWarnTick = now;
                            Debug.WriteLine("[Art9774Driver] 警告: 2秒未收到采样数据 (SamplesQueue empty)");
                        }
                        Thread.Sleep(1);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Art9774Driver] ProcessSamplesQueue exception: {ex.Message}");
            }
        }

        private void GetErrorString(Int32 errorCode)
        {
            if (errorCode < 0)
            {
                byte[] errorInfo = new byte[2048];
                ArtDAQ_GetExtendedErrorInfo(errorInfo, 2048);
                string str = System.Text.Encoding.Default.GetString(errorInfo);
                Debug.WriteLine($"[Art9774Driver] 错误信息: {str}");
            }
        }

        #endregion

        #region 内部类

        private class ChannelConfig
        {
            public string ChannelId { get; set; }
            public string PhysicalChannel { get; set; }
            public bool IsEnabled { get; set; }
            public string Range { get; set; }
            public double MinValue { get; set; }
            public double MaxValue { get; set; }
        }

        #endregion
    }
}

