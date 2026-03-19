using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MeasureControl.Models;
using MeasureControl.Models.Devices;
using MeasureControl.Models.Devices.DeviceCategories;
using MT;

namespace MeasureControl.Drivers
{
    /// <summary>
    /// 芒果树 MT-X532 模拟量输出驱动
    /// 
    /// 功能说明：
    /// - 支持最多 32 路模拟量输出通道（AO0 ~ AO31）
    /// - 所有通道共用一个采样率（最大 200 kS/s per channel）
    /// - 支持直流（DC）和正弦波（Sine）两种波形类型
    /// - 输出电压范围：-10V ~ +10V
    /// - 支持单次写入和连续输出两种模式
    /// 
    /// 工作原理：
    /// - 单次写入：调用 WriteChannelsBatchAsync 时，生成 1 秒的数据并一次性写入硬件
    /// - 连续输出：调用 StartAcquisitionAsync 后，在后台循环生成并发送数据，实现连续输出
    /// - 数据格式：交错格式，即 [ch0_sample0, ch1_sample0, ..., chN_sample0, ch0_sample1, ...]
    /// </summary>
    public class MTX532Driver : IDeviceDriver
    {
        /// <summary>
        /// 采集状态改变事件（此驱动不支持，始终为空实现）
        /// </summary>
        public event EventHandler<AcquisitionStatusChangedEventArgs> AcquisitionStatusChanged
        {
            add { }
            remove { }
        }

        /// <summary>
        /// 设备功能类型
        /// </summary>
        public DeviceCapability Capability => DeviceCapability.Communication;

        // 当为 true 时，在调用厂商 SDK（MTDAQ）前做严格校验并避免直接调用可能弹出原生对话框的 API
        private readonly bool _suppressNativeDialogs;

        private readonly int? _slotNumberOverride;
        /// <summary>设备基础信息对象，包含设备 ID、名称、型号等</summary>
        private readonly DeviceBase _device;

        /// <summary>设备连接状态标志，true 表示已通过 MTDAQ.Open() 成功连接</summary>
        private bool _isConnected;

        /// <summary>连续输出运行状态标志，true 表示正在执行连续 AO 输出任务</summary>
        private bool _isAcquisitionRunning;

        /// <summary>缓冲区队列，用于生产者-消费者模式</summary>
        private readonly System.Collections.Concurrent.ConcurrentQueue<float[]> _bufferQueue = new System.Collections.Concurrent.ConcurrentQueue<float[]>();

        /// <summary>生产者线程，用于生成缓冲区</summary>
        private Task _producerTask;

        /// <summary>消费者线程，用于提交缓冲区到硬件</summary>
        private Task _consumerTask;

        // 连续输出运行时的固定参数（用于稳定消费者节拍）
        private int _runningSamplesPerChannel;
        private int _runningChannelCount;
        private IReadOnlyList<string> _runningEnabledChannelIds;

        private const double _continuousBufferSeconds = 0.05;

        private const int _aoFifoCapacitySamples = 100000;
        private const int _aoWriteTimeoutMs = 2500;
        private const double _continuousTargetBufferedSeconds = 0.1;
        private const int _continuousMaxQueueDepth = 50;
        private const int _continuousMinQueueDepth = 2;

        private const double _consumerPacingFactor = 0.9;

        /// <summary>设备引用句柄，由 MTDAQ.Open() 返回，用于后续所有硬件操作</summary>
        private byte[] _deviceRef;

        /// <summary>设备引用句柄的字节数组长度，用于 MTDAQ.Open() 调用</summary>
        private int _deviceRefLen;

        /// <summary>
        /// 全卡统一采样率（Hz），由外部配置或默认值决定
        /// 所有通道共享此采样率，最大支持 200 kS/s per channel
        /// 默认值：100 kS/s（100000 Hz）
        /// </summary>
        private double _sampleRate = 100000; // 默认 100 kS/s

        /// <summary>
        /// 通道配置字典，键为通道 ID（如 "AO0", "AO1"），值为通道配置对象
        /// 每个通道配置包含：是否启用、波形类型、幅度、偏移、频率等参数
        /// </summary>
        private readonly Dictionary<string, Mtx532AoChannelConfig> _channelConfigs = new Dictionary<string, Mtx532AoChannelConfig>();

        /// <summary>
        /// 通道最后输出值字典，用于记录每个通道最后一次输出的电压值
        /// 主要用于 ReadChannelAsync 等读取操作，返回上次写入的值
        /// </summary>
        private readonly Dictionary<string, double> _lastOutputValues = new Dictionary<string, double>();

        /// <summary>
        /// AO 连续输出后台任务
        /// 当调用 StartAcquisitionAsync() 时，会启动此任务在后台循环发送数据
        /// 当调用 StopAcquisitionAsync() 时，会停止此任务
        /// </summary>
        private Task _aoTask;

        /// <summary>获取设备唯一标识符，从设备对象中提取，如果为空则返回空字符串</summary>
        public string DeviceId => _device?.Id ?? string.Empty;

        /// <summary>获取设备名称，从设备对象中提取，如果为空则返回默认值 "MT-X532"</summary>
        public string DeviceName => _device?.Name ?? "MT-X532";

        /// <summary>获取设备连接状态，true 表示已成功连接到硬件设备</summary>
        public bool IsConnected => _isConnected;

        /// <summary>获取是否为模拟设备，MT-X532 是真实硬件设备，始终返回 false</summary>
        public bool IsSimulated => false;

        /// <summary>
        /// 每个通道的相位累加器，用于保证跨缓冲区边界的相位连续性
        /// Key: 通道ID (如"AO0"), Value: 当前累积相位（弧度，0~2π循环）
        /// </summary>
        private readonly Dictionary<string, double> _phaseAccumulators =
            new Dictionary<string, double>();

        /// <summary>
        /// 用于同步访问相位累加器的锁对象
        /// </summary>
        private readonly object _phaseLock = new object();

        /// <summary>
        /// 构造函数，初始化 MT-X532 驱动实例
        /// </summary>
        public MTX532Driver(DeviceBase device, bool suppressNativeDialogs = true, int? slotNumberOverride = null)
        {
            _device = device ?? throw new ArgumentNullException(nameof(device));
            _suppressNativeDialogs = suppressNativeDialogs;
            _slotNumberOverride = slotNumberOverride;

            // 初始化所有通道的默认配置
            InitializeChannelConfigs();
        }

        /// <summary>
        /// 初始化所有通道的默认配置
        /// 
        /// 功能说明：
        /// - 为 AO0 ~ AO31 共 32 个通道创建默认配置对象
        /// - 所有通道初始状态为：未启用、直流波形、幅度和偏移均为 0V、频率 1Hz
        /// - 同时初始化每个通道的最后输出值为 0V
        /// 
        /// 注意：通道数量可根据实际硬件调整，MT-X532 通常支持 32 路输出
        /// </summary>
        private void InitializeChannelConfigs()
        {
            // 预创建 AO0..AO31 的默认配置（根据实际通道数可调整）
            for (int i = 0; i < 32; i++)
            {
                string channelId = $"AO{i}";
                _channelConfigs[channelId] = new Mtx532AoChannelConfig
                {
                    ChannelId = channelId,
                    Enabled = false,              // 初始状态：未启用
                    Waveform = WaveformType.Dc,  // 初始波形：直流
                    Amplitude = 0.0,             // 初始幅度：0V
                    Offset = 0.0,                // 初始偏移：0V
                    Frequency = 1.0,            // 初始频率：1Hz（仅对正弦波有效）
                };
                _lastOutputValues[channelId] = 0.0; // 初始输出值：0V
            }
        }

        public void SetEnabledChannels(IEnumerable<string> enabledAoChannels)
        {
            foreach (var key in _channelConfigs.Keys.ToList())
            {
                _channelConfigs[key].Enabled = false;
            }

            if (enabledAoChannels == null)
                return;

            foreach (var rawChannel in enabledAoChannels)
            {
                if (string.IsNullOrWhiteSpace(rawChannel))
                    continue;

                var channelId = rawChannel.Trim();
                if (_channelConfigs.TryGetValue(channelId, out var config))
                {
                    config.Enabled = true;
                }
            }
        }

        /// <summary>
        /// 连接到 MT-X532 硬件设备
        /// 
        /// 工作流程：
        /// 1. 构建芒果树设备配置字符串（包含设备型号、槽位、通道列表、采样率等）
        /// 2. 将配置字符串转换为字节数组（ASCII 编码）
        /// 3. 分配设备引用句柄缓冲区（20000 字节）
        /// 4. 调用 MTDAQ.Open() 打开设备并获取设备引用句柄
        /// 5. 设置连接状态标志并等待 50ms 确保设备初始化完成
        /// 
        /// 异常处理：
        /// - 如果连接失败，会设置 _isConnected = false 并重新抛出异常
        /// </summary>
        /// <returns>连接成功返回 true，失败则抛出异常</returns>
        public async Task<bool> ConnectAsync()
        {
            try
            {
                var swTotal = Stopwatch.StartNew();
                Debug.WriteLine($"[MTX532Driver] 连接设备 {DeviceName}");

                var swBuild = Stopwatch.StartNew();
                string configString = BuildConfigString();
                swBuild.Stop();
                Debug.WriteLine($"[MTX532Driver] BuildConfigString elapsed={swBuild.ElapsedMilliseconds}ms");
                byte[] config = System.Text.Encoding.ASCII.GetBytes(configString);

                _deviceRefLen = 20000;
                _deviceRef = new byte[_deviceRefLen];
                // 在某些情况下厂商 SDK 会弹出原生对话框（不可控），当启用 suppressNativeDialogs 时先做参数校验，避免调用 SDK
                if (_suppressNativeDialogs)
                {
                    // 简单校验：型号不能为空；如果是 PXI 设备需要有有效槽位（>0）
                    if (string.IsNullOrWhiteSpace(_device?.Model))
                    {
                        Debug.WriteLine("[MTX532Driver] 跳过 MTDAQ.Open：设备型号为空，已启用 suppressNativeDialogs");
                        _isConnected = false;
                        return false;
                    }

                    var slot = GetSlotIndexForConfig();
                    if (_device is PxiDeviceBase && slot <= 0)
                    {
                        Debug.WriteLine("[MTX532Driver] 跳过 MTDAQ.Open：PXI 槽位无效或未设置，已启用 suppressNativeDialogs");
                        _isConnected = false;
                        return false;
                    }
                }

                await Task.Run(() =>
                {
                    var swOpen = Stopwatch.StartNew();
                    Debug.WriteLine($"[MTX532Driver] Call MTDAQ.Open ConfigLength={config.Length}");
                    MTDAQ.Open(config, _deviceRef, ref _deviceRefLen);
                    swOpen.Stop();
                    Debug.WriteLine($"[MTX532Driver] MTDAQ.Open elapsed={swOpen.ElapsedMilliseconds}ms, DeviceRefLen={_deviceRefLen}");
                });

                // 保护性校验：如果长度无效，认为连接失败
                if (_deviceRef == null || _deviceRefLen <= 0)
                {
                    Debug.WriteLine("[MTX532Driver] 连接失败，设备引用无效");
                    _isConnected = false;
                    return false;
                }

                _isConnected = true;
                Debug.WriteLine($"[MTX532Driver] Device connected DeviceRefLen={_deviceRefLen}");
                await Task.Delay(50);
                swTotal.Stop();
                Debug.WriteLine($"[MTX532Driver] ConnectAsync total elapsed={swTotal.ElapsedMilliseconds}ms");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MTX532Driver] 连接失败: {ex.Message}");
                _isConnected = false;
                return false;
            }
        }

        /// <summary>
        /// 断开与 MT-X532 硬件设备的连接
        /// 
        /// 工作流程：
        /// 1. 如果正在运行连续输出任务，先停止该任务
        /// 2. 如果设备引用句柄不为空，调用 MTDAQ.Close() 关闭设备
        /// 3. 重置连接状态和运行状态标志
        /// 
        /// 异常处理：
        /// - 关闭设备时的异常会被捕获并记录，不会影响断开流程
        /// - 如果整体断开失败，返回 false 但不抛出异常
        /// </summary>
        /// <returns>断开成功返回 true，失败返回 false</returns>
        public async Task<bool> DisconnectAsync()
        {
            try
            {
                var swTotal = Stopwatch.StartNew();
                Debug.WriteLine($"[MTX532Driver] 断开设备 {DeviceName}");

                // 如果正在运行连续输出，先停止后台任务
                if (_isAcquisitionRunning)
                {
                    var swStop = Stopwatch.StartNew();
                    await StopAcquisitionAsync();
                    swStop.Stop();
                    Debug.WriteLine($"[MTX532Driver] StopAcquisitionAsync (from Disconnect) elapsed={swStop.ElapsedMilliseconds}ms");
                }

                // 如果设备引用句柄不为空，调用 SDK 关闭设备
                if (_deviceRef != null)
                {
                    try
                    {
                        // 调用芒果树 SDK 关闭设备，释放硬件资源
                        var swClose = Stopwatch.StartNew();
                        MTDAQ.Close(_deviceRef);
                        swClose.Stop();
                        Debug.WriteLine($"[MTX532Driver] MTDAQ.Close elapsed={swClose.ElapsedMilliseconds}ms");
                    }
                    catch (Exception ex)
                    {
                        // 关闭设备时的异常不影响断开流程，只记录日志
                        Debug.WriteLine($"[MTX532Driver] 关闭设备失败: {ex.Message}");
                    }
                }

                // 重置状态标志
                _isConnected = false;
                _isAcquisitionRunning = false;
                swTotal.Stop();
                Debug.WriteLine($"[MTX532Driver] DisconnectAsync total elapsed={swTotal.ElapsedMilliseconds}ms");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MTX532Driver] 断开失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 读取单个通道的最后输出值
        /// 
        /// 注意：MT-X532 是输出设备，不支持硬件读取，此方法返回的是上次写入的值
        /// 该值在每次 WriteChannelAsync 或 WriteChannelsBatchAsync 调用时更新
        /// </summary>
        /// <param name="channelId">通道 ID，格式如 "AO0", "AO1" 等</param>
        /// <returns>该通道的最后输出电压值（伏特），如果通道不存在则返回 0.0</returns>
        public async Task<double> ReadChannelAsync(string channelId)
        {
            // 如果通道不存在于输出值字典中，返回默认值 0.0
            if (!_lastOutputValues.ContainsKey(channelId))
            {
                return 0.0;
            }
            // 返回该通道的最后输出值
            return await Task.FromResult(_lastOutputValues[channelId]);
        }

        /// <summary>
        /// 批量读取多个通道的最后输出值
        /// 
        /// 注意：MT-X532 是输出设备，不支持硬件读取，此方法返回的是上次写入的值
        /// </summary>
        /// <param name="channelIds">要读取的通道 ID 集合</param>
        /// <returns>通道 ID 到电压值的字典，不存在的通道返回 0.0</returns>
        public async Task<Dictionary<string, double>> ReadChannelsBatchAsync(IEnumerable<string> channelIds)
        {
            var result = new Dictionary<string, double>();
            // 遍历所有请求的通道 ID，从输出值字典中获取对应的值
            foreach (var id in channelIds)
            {
                // 如果通道存在于输出值字典中，返回其值；否则返回默认值 0.0
                result[id] = _lastOutputValues.ContainsKey(id) ? _lastOutputValues[id] : 0.0;
            }
            return await Task.FromResult(result);
        }

        /// <summary>
        /// 写入单个通道的电压值（单次写入模式）
        /// 
        /// 功能说明：
        /// - 将指定通道设置为直流输出，电压值为指定值
        /// - 内部调用 WriteChannelsBatchAsync 实现批量写入
        /// - 此方法适用于单点更新场景，写入后不会持续输出
        /// </summary>
        /// <param name="channelId">通道 ID，格式如 "AO0", "AO1" 等</param>
        /// <param name="value">要输出的电压值（伏特），范围 [-10.0, +10.0]，超出范围会被限制</param>
        /// <returns>写入成功返回 true，失败返回 false</returns>
        public async Task<bool> WriteChannelAsync(string channelId, double value)
        {
            // 构造单通道字典，调用批量写入方法
            var dict = new Dictionary<string, double>
            {
                { channelId, value }
            };
            return await WriteChannelsBatchAsync(dict);
        }

        /// <summary>
        /// 批量写入多个通道的电压值（单次写入模式）
        /// 
        /// 工作流程：
        /// 1. 检查设备连接状态
        /// 2. 更新所有指定通道的配置：启用通道、设置为直流波形、设置偏移为指定值、幅度为 0
        /// 3. 根据当前采样率计算缓冲区大小（1 秒的数据量）
        /// 4. 生成交错格式的 AO 缓冲区
        /// 5. 调用 MTDAQ.AnalogWrite() 一次性写入硬件
        /// 
        /// 数据格式：
        /// - 缓冲区大小 = 采样率 × 通道数（例如：1000Hz × 2通道 = 2000 个浮点数）
        /// - 数据格式为交错排列：[ch0_sample0, ch1_sample0, ch0_sample1, ch1_sample1, ...]
        /// 
        /// 注意：
        /// - 此方法是一次性写入，写入后不会持续输出
        /// - 如果需要连续输出，应使用 StartAcquisitionAsync() 方法
        /// </summary>
        /// <param name="channelValues">通道 ID 到电压值的字典，键为通道 ID，值为电压值（伏特）</param>
        /// <returns>写入成功返回 true，失败返回 false</returns>
        public async Task<bool> WriteChannelsBatchAsync(Dictionary<string, double> channelValues)
        {
            // 检查设备连接状态
            if (!_isConnected)
            {
                Debug.WriteLine("[MTX532Driver] 设备未连接，无法写入");
                return false;
            }

            // 更新所有指定通道的配置
            foreach (var kv in channelValues)
            {
                if (_channelConfigs.TryGetValue(kv.Key, out var cfg))
                {
                    cfg.Enabled = true;                    // 启用通道
                    cfg.Waveform = WaveformType.Dc;        // 设置为直流波形
                    cfg.Offset = kv.Value;                  // 设置偏移为指定值（允许超出±10V范围）
                    cfg.Amplitude = 0.0;                   // 幅度为 0（直流信号）
                    _lastOutputValues[kv.Key] = cfg.Offset; // 记录最后输出值
                }
            }

            try
            {
                // 根据当前采样率动态计算缓冲区大小
                // 与例程保持一致：一次发送 1 秒的数据（样点数 = 采样率）
                int samplesPerChannel = CalculateOptimalBufferSize();

                // 根据当前通道配置生成一次性 AO 缓冲区
                // 缓冲区格式：交错排列，即 [ch0_sample0, ch1_sample0, ..., chN_sample0, ch0_sample1, ...]
                float[] buffer = GenerateAoBuffer(samplesPerChannel);
                if (buffer.Length == 0)
                {
                    Debug.WriteLine("[MTX532Driver] 当前没有已使能通道，跳过一次性写入");
                    return await Task.FromResult(false);
                }

                // 一次性写入（非持续输出），用于单点/单次更新场景
                // timeout: 2500ms，表示如果 2.5 秒内无法完成写入则超时
                uint written;
                MTDAQ.AnalogWrite(_deviceRef, ref buffer[0], (uint)buffer.Length, timeout: 2500, out written);

                Debug.WriteLine($"[MTX532Driver] 一次性 AO 写入完成，样点数: {written}");
                return await Task.FromResult(true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MTX532Driver] 写入失败: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> ConfigureChannelAsync(string channelId, Dictionary<string, object> config)
        {
            Debug.WriteLine($"[MTX532Driver] ConfigureChannelAsync 开始: channelId={channelId}");
            if (config != null)
            {
                var configStr = string.Join(", ", config.Select(kv => $"{kv.Key}={kv.Value}"));
                Debug.WriteLine($"[MTX532Driver] 配置参数: {configStr}");
            }

            if (!_channelConfigs.TryGetValue(channelId, out var cfg))
            {
                Debug.WriteLine($"[MTX532Driver] ❌ 无效通道: {channelId}");
                return await Task.FromResult(false);
            }

            if (config == null)
            {
                Debug.WriteLine($"[MTX532Driver] 配置字典为 null，跳过配置");
                return await Task.FromResult(true);
            }

            // 记录配置前的状态，用于失败时恢复
            var oldWaveform = cfg.Waveform;
            var oldAmplitude = cfg.Amplitude;
            var oldOffset = cfg.Offset;
            var oldFrequency = cfg.Frequency;
            var oldDutyCycle = cfg.DutyCycle;
            var oldEnabled = cfg.Enabled;

            bool shouldFlushQueue = false;

            // 先解析所有参数，但不立即更新配置
            double? newAmplitude = null;
            double? newOffset = null;
            double? newFrequency = null;
            double? newDutyCycle = null;
            WaveformType? newWaveform = null;
            bool? newEnabled = null;

            if (config.TryGetValue("Enabled", out var enabledObj) && enabledObj is bool enabled)
            {
                newEnabled = enabled;
            }

            if (config.TryGetValue("SampleRate", out var srObj) && double.TryParse(srObj.ToString(), out var sr))
            {
                if (sr > 0 && sr <= 200000)
                {
                    _sampleRate = sr;
                    Debug.WriteLine($"[MTX532Driver] 采样率更新: {_sampleRate} Hz");
                }
            }

            if (config.TryGetValue("Waveform", out var wfObj) && wfObj is WaveformType wf)
            {
                newWaveform = wf;
            }

            if (config.TryGetValue("Amplitude", out var ampObj) && double.TryParse(ampObj.ToString(), out var amp))
            {
                newAmplitude = Math.Abs(amp);
            }

            if (config.TryGetValue("Offset", out var offObj) && double.TryParse(offObj.ToString(), out var off))
            {
                newOffset = off;
            }

            if (config.TryGetValue("Frequency", out var freqObj) && double.TryParse(freqObj.ToString(), out var freq))
            {
                newFrequency = Math.Max(0, freq);
            }

            if (config.TryGetValue("DutyCycle", out var dutyObj) && double.TryParse(dutyObj.ToString(), out var duty))
            {
                if (duty < 1) duty = 1;
                if (duty > 99) duty = 99;
                newDutyCycle = duty;
            }

            // 计算新的电压值（用于验证和调整）
            double testOffset = newOffset ?? cfg.Offset;
            double testAmplitude = newAmplitude ?? cfg.Amplitude;
            WaveformType testWaveform = newWaveform ?? cfg.Waveform;

            // 根据波形类型计算最大允许的幅值
            double maxAllowedAmplitude = CalculateMaxAllowedAmplitude(testOffset, testWaveform);

            Debug.WriteLine($"[MTX532Driver] 通道 {channelId} 电压范围检查:");
            Debug.WriteLine($"[MTX532Driver]     波形类型: {testWaveform}, 偏置: {testOffset}V, 幅值: {testAmplitude}V");
            Debug.WriteLine($"[MTX532Driver]     最大允许幅值: {maxAllowedAmplitude}V");

            // 如果请求的幅值超过允许范围，自动调整为最大允许值
            if (testAmplitude > maxAllowedAmplitude)
            {
                testAmplitude = maxAllowedAmplitude;
                if (newAmplitude.HasValue)
                {
                    newAmplitude = testAmplitude;
                }
                Debug.WriteLine($"[MTX532Driver] ⚠️ 幅值超出范围，已自动调整为: {testAmplitude}V");
            }

            // 验证通过，更新配置
            if (newEnabled.HasValue)
            {
                cfg.Enabled = newEnabled.Value;
                Debug.WriteLine($"[MTX532Driver] 通道 {channelId} Enabled: {cfg.Enabled}");
            }

            if (newWaveform.HasValue)
            {
                cfg.Waveform = newWaveform.Value;
                Debug.WriteLine($"[MTX532Driver] 通道 {channelId} 波形类型: {oldWaveform} -> {cfg.Waveform}");
                shouldFlushQueue = true;
            }

            if (newAmplitude.HasValue)
            {
                cfg.Amplitude = newAmplitude.Value;
                Debug.WriteLine($"[MTX532Driver]  参数已更新（将在下次缓冲区生效）");
                shouldFlushQueue = true;
            }

            if (newOffset.HasValue)
            {
                cfg.Offset = newOffset.Value;
                Debug.WriteLine($"[MTX532Driver] 通道 {channelId} 偏置: {oldOffset} -> {cfg.Offset} V");
                shouldFlushQueue = true;
            }

            if (newFrequency.HasValue)
            {
                cfg.Frequency = newFrequency.Value;
                Debug.WriteLine($"[MTX532Driver] 通道 {channelId} 频率: {oldFrequency} -> {cfg.Frequency} Hz");

                shouldFlushQueue = true;

                // 诊断日志：计算并显示相位增量
                double dt = 1.0 / (_sampleRate > 0 ? _sampleRate : 1000.0);
                double phaseIncrement = 2 * Math.PI * cfg.Frequency * dt;
                Debug.WriteLine($"[MTX532Driver] 频率诊断 - 采样率: {_sampleRate}Hz, dt: {dt:F9}s, 相位增量: {phaseIncrement:F12} rad/样点");
                Debug.WriteLine($"[MTX532Driver] 预期周期: {1.0/cfg.Frequency:F6}s ({cfg.Frequency:F3}Hz), 样点数: {(1.0/cfg.Frequency)/dt:F1} 样点/周期");
            }

            if (newDutyCycle.HasValue)
            {
                cfg.DutyCycle = newDutyCycle.Value;
                Debug.WriteLine($"[MTX532Driver] 通道 {channelId} 占空比: {oldDutyCycle} -> {cfg.DutyCycle}%");
                shouldFlushQueue = true;
            }

            if (_isAcquisitionRunning && shouldFlushQueue)
            {
                int removed = 0;
                while (_bufferQueue.TryDequeue(out _)) { removed++; }
                
                Debug.WriteLine($"[MTX532Driver] ⚡ 参数变更触发队列刷新：已清空 {removed} 个旧缓冲区");

                if (_runningEnabledChannelIds != null && _runningEnabledChannelIds.Count > 0 && _runningSamplesPerChannel > 0)
                {
                    // 立即生成3个新缓冲区，确保播放连续且新参数快速生效
                    // 每个缓冲区约0.05秒，3个缓冲区共0.15秒延迟
                    for (int i = 0; i < 3 && _isAcquisitionRunning; i++)
                    {
                        float[] newBuffer = GenerateAoBufferWithPhase(_runningSamplesPerChannel, _runningEnabledChannelIds);
                        if (newBuffer.Length > 0)
                        {
                            _bufferQueue.Enqueue(newBuffer);
                            Debug.WriteLine($"[MTX532Driver] 已生成新缓冲区 {i + 1}/3 (使用新参数)");
                        }
                    }
                    Debug.WriteLine($"[MTX532Driver] ✅ 新参数将在约 {_continuousBufferSeconds * 3:F3} 秒后在示波器上显示");
                }
            }

            // 对于正弦波和方波，验证频率和幅值是否有效
            if (cfg.Waveform == WaveformType.Sine || cfg.Waveform == WaveformType.Square)
            {
                if (cfg.Frequency <= 0)
                {
                    Debug.WriteLine($"[MTX532Driver] ⚠️ 警告：通道 {channelId} 波形类型为 {cfg.Waveform}，但频率为 {cfg.Frequency} Hz（应为正数）");
                }
                if (cfg.Amplitude <= 0)
                {
                    Debug.WriteLine($"[MTX532Driver] ⚠️ 警告：通道 {channelId} 波形类型为 {cfg.Waveform}，但幅值为 {cfg.Amplitude} V（应为正数）");
                }
            }

            // 对于方波，验证高电平和低电平是否在有效范围内（-10V 到 +10V）
            if (cfg.Waveform == WaveformType.Square)
            {
                double highLevel = cfg.Offset + cfg.Amplitude;
                double lowLevel = cfg.Offset - cfg.Amplitude;

                if (highLevel > 10.0 || lowLevel < -10.0)
                {
                    Debug.WriteLine($"[MTX532Driver] ⚠️ 警告：通道 {channelId} 方波超出电压范围");
                    Debug.WriteLine($"[MTX532Driver]     偏置: {cfg.Offset}V, 幅值: {cfg.Amplitude}V");
                    Debug.WriteLine($"[MTX532Driver]     高电平: {highLevel}V, 低电平: {lowLevel}V");
                    Debug.WriteLine($"[MTX532Driver]     有效范围: -10V 到 +10V");
                }
            }

            Debug.WriteLine($"[MTX532Driver] ✅ 通道 {channelId} 配置成功: Waveform={cfg.Waveform}, Offset={cfg.Offset}V, Amplitude={cfg.Amplitude}V, Frequency={cfg.Frequency}Hz");
            return await Task.FromResult(true);
        }

        /// <summary>
        /// 启动连续模拟量输出任务
        ///
        /// 功能说明：
        /// - 启动生产者-消费者模式的后台任务，实现连续波形输出
        /// - 适用于需要持续输出波形的场景（如正弦波、连续变化的信号等）
        /// - 通过双线程并行处理，实现无缝连续输出
        ///
        /// 工作流程：
        /// 1. 检查设备连接状态和运行状态
        /// 2. 设置运行状态标志为 true
        /// 3. 启动生产者线程：持续生成数据缓冲区并放入队列
        /// 4. 启动消费者线程：从队列取缓冲区并提交到硬件
        /// 5. 两个线程并行工作，确保播放连续性
        ///
        /// 数据生成：
        /// - 缓冲区大小 = 采样率（例如：1000Hz → 1000 个样点/通道）
        /// - 数据格式：交错排列 [ch0_sample0, ch1_sample0, ..., chN_sample0, ch0_sample1, ...]
        /// - 波形生成：根据每个通道的配置（波形类型、幅度、偏移、频率）实时计算采样值
        ///
        /// 并行处理：
        /// - 生产者线程：生成缓冲区，避免队列为空
        /// - 消费者线程：提交缓冲区到硬件，保证播放连续
        /// - 通过 ConcurrentQueue 实现线程安全通信
        ///
        /// 停止方式：
        /// - 调用 StopAcquisitionAsync() 停止连续输出
        /// - 设置 _isAcquisitionRunning = false 后，两个后台线程会在当前操作结束后退出
        /// </summary>
        /// <returns>启动成功返回 true，设备未连接或已在运行返回 false</returns>
        public async Task<bool> StartAcquisitionAsync()
        {
            if (!_isConnected || _isAcquisitionRunning)
                return false;

            // 检查是否有启用的通道
            var enabledChannels = _channelConfigs.Where(kv => kv.Value.Enabled).ToList();
            if (enabledChannels.Count == 0)
            {
                Debug.WriteLine("[MTX532Driver] 没有启用通道，无法启动连续输出");
                return false;
            }

            // 重置相位累加器
            lock (_phaseLock)
            {
                _phaseAccumulators.Clear();
                foreach (var channelId in _channelConfigs.Keys)
                {
                    _phaseAccumulators[channelId] = 0.0;
                }
            }

            // 清空缓冲区队列
            while (_bufferQueue.TryDequeue(out _)) { }

            // 短暂延迟确保配置完全同步（低时延方案）
            await Task.Delay(50);

            _isAcquisitionRunning = true;

            int runningChannelCount = enabledChannels.Count;
            if (runningChannelCount <= 0)
            {
                _isAcquisitionRunning = false;
                return false;
            }

            _runningEnabledChannelIds = enabledChannels.Select(kv => kv.Key).ToList();

            double sampleRate = _sampleRate;
            if (sampleRate <= 0)
            {
                sampleRate = 1000;
            }

            double totalRate = sampleRate * runningChannelCount;
            double fifoSeconds = totalRate > 0 ? (_aoFifoCapacitySamples / totalRate) : 0;
            double packetSeconds = Math.Min(_continuousBufferSeconds, fifoSeconds > 0 ? fifoSeconds * 0.8 : _continuousBufferSeconds);
            if (packetSeconds <= 0)
            {
                packetSeconds = _continuousBufferSeconds;
            }

            int desiredSamplesPerChannel = (int)Math.Round(sampleRate * packetSeconds);
            if (desiredSamplesPerChannel < 2)
            {
                desiredSamplesPerChannel = 2;
            }

            int maxSamplesPerChannel = Math.Max(2, _aoFifoCapacitySamples / runningChannelCount);
            int samplesPerChannel = Math.Min(desiredSamplesPerChannel, maxSamplesPerChannel);
            if (samplesPerChannel < 2)
            {
                samplesPerChannel = 2;
            }

            int totalSamples = samplesPerChannel * runningChannelCount;
            if (totalSamples > _aoFifoCapacitySamples)
            {
                samplesPerChannel = Math.Max(2, _aoFifoCapacitySamples / runningChannelCount);
                totalSamples = samplesPerChannel * runningChannelCount;
            }

            packetSeconds = sampleRate > 0 ? (samplesPerChannel / sampleRate) : _continuousBufferSeconds;
            int targetQueueDepth = (int)Math.Ceiling(_continuousTargetBufferedSeconds / Math.Max(1e-6, packetSeconds));
            targetQueueDepth = Math.Max(_continuousMinQueueDepth, Math.Min(_continuousMaxQueueDepth, targetQueueDepth));
            int prefillCount = targetQueueDepth;

            // 在高通道数+高采样率时，限制单次写入chunk不超过FIFO的20%，避免写入阻塞导致频率失真
            // 例如：200kHz×32路时，FIFO只能缓存15.6ms，chunk必须更小才能保证流畅
            int chunkMaxTotalSamples = Math.Min(_aoFifoCapacitySamples / 5, totalSamples);
            int chunkMaxFrames = Math.Max(1, chunkMaxTotalSamples / runningChannelCount);
            chunkMaxTotalSamples = chunkMaxFrames * runningChannelCount;

            _runningSamplesPerChannel = samplesPerChannel;
            _runningChannelCount = runningChannelCount;
            Debug.WriteLine($"[MTX532Driver] 启动连续输出：通道数={runningChannelCount}, 采样率={sampleRate:F0}Hz, 缓冲区={samplesPerChannel}样点/通道, 总点数={totalSamples}, 包时长={packetSeconds * 1000:F2}ms, 预填充/目标队列={prefillCount}, chunkMax={chunkMaxTotalSamples}, FIFO容量={_aoFifoCapacitySamples}, FIFO可缓存时长={fifoSeconds * 1000:F1}ms");

            // 启动生产者线程：生成缓冲区
            _producerTask = Task.Run(() =>
            {
                Debug.WriteLine("[MTX532Driver] 生产者线程启动");

                try
                {
                    // 预填充队列以确保有足够的缓冲区
                    for (int i = 0; i < prefillCount && _isAcquisitionRunning; i++)
                    {
                        float[] buffer = GenerateAoBufferWithPhase(samplesPerChannel, _runningEnabledChannelIds);

                        // 检查缓冲区是否为空，如果为空说明没有启用通道，停止预填充
                        if (buffer.Length == 0)
                        {
                            Debug.WriteLine($"[MTX532Driver] 预填充缓冲区 {i + 1}/{prefillCount} 失败：没有启用通道");
                            _isAcquisitionRunning = false;
                            break;
                        }

                        _bufferQueue.Enqueue(buffer);
                        Debug.WriteLine($"[MTX532Driver] 预填充缓冲区 {i + 1}/{prefillCount}");
                    }

                    // 持续生成缓冲区，确保播放连续
                    while (_isAcquisitionRunning)
                    {
                        // 主动监控队列状态，及时补充缓冲区
                        int currentDepth = _bufferQueue.Count;
                        if (currentDepth < targetQueueDepth)
                        {
                            float[] buffer = GenerateAoBufferWithPhase(samplesPerChannel, _runningEnabledChannelIds);
                            if (buffer.Length > 0)
                            {
                                _bufferQueue.Enqueue(buffer);
                                Debug.WriteLine(currentDepth == 0
                                    ? "[MTX532Driver] 紧急生成缓冲区 - 队列为空"
                                    : $"[MTX532Driver] 补充缓冲区 - 当前队列长度: {_bufferQueue.Count}");
                                
                                // 队列较空时立即继续生成，不等待
                                if (currentDepth < targetQueueDepth / 2)
                                {
                                    continue;
                                }
                            }
                            else
                            {
                                Debug.WriteLine("[MTX532Driver] 生成缓冲区为空，跳过入队");
                                Thread.Sleep(5);
                            }
                        }
                        else
                        {
                            // 队列充足时短暂等待，避免CPU占用过高
                            Thread.Sleep(5);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[MTX532Driver] 生产者线程异常: {ex.Message}");
                    _isAcquisitionRunning = false;
                }

                Debug.WriteLine("[MTX532Driver] 生产者线程退出");
            });

            // 启动消费者线程：提交缓冲区到硬件
            _consumerTask = Task.Run(() =>
            {
                Debug.WriteLine("[MTX532Driver] 消费者线程启动");

                try
                {
                    uint fifoWritableAfter = 0;
                    while (_isAcquisitionRunning)
                    {
                        if (_bufferQueue.TryDequeue(out float[] buffer))
                        {
                            // 检查缓冲区是否为空（当没有启用通道时可能出现）
                            if (buffer.Length == 0)
                            {
                                Debug.WriteLine("[MTX532Driver] 缓冲区为空，跳过提交（可能没有启用通道）");
                                continue;
                            }

                            int channelCount = _runningChannelCount > 0 ? _runningChannelCount : 1;
                            if (buffer.Length % channelCount != 0)
                            {
                                Debug.WriteLine($"[MTX532Driver] 警告：缓冲区长度与通道数不匹配 length={buffer.Length}, channelCount={channelCount}");
                            }

                            int offset = 0;
                            int writeCalls = 0;
                            var swTotal = Stopwatch.StartNew();
                            while (_isAcquisitionRunning && offset < buffer.Length)
                            {
                                int remaining = buffer.Length - offset;
                                int chunk = Math.Min(remaining, chunkMaxTotalSamples);
                                int frames = chunk / channelCount;
                                if (frames <= 0)
                                {
                                    frames = 1;
                                }
                                chunk = frames * channelCount;
                                if (chunk > remaining)
                                {
                                    chunk = (remaining / channelCount) * channelCount;
                                    if (chunk <= 0)
                                    {
                                        break;
                                    }
                                }

                                var swWrite = Stopwatch.StartNew();
                                MTDAQ.AnalogWrite(_deviceRef, ref buffer[offset], (uint)chunk, timeout: _aoWriteTimeoutMs, out fifoWritableAfter);
                                swWrite.Stop();

                                // 节拍控制：使用0.9系数略快于实时播放速度，避免硬件FIFO积累过多数据
                                // 这样可以将参数变更延迟从约2秒降低到约0.15秒
                                try
                                {
                                    double sr = _sampleRate > 0 ? _sampleRate : 1000.0;
                                    double chunkDurationMs = frames * 1000.0 / sr;
                                    int sleepMs = (int)Math.Round(chunkDurationMs * _consumerPacingFactor - swWrite.Elapsed.TotalMilliseconds);
                                    if (sleepMs > 0)
                                    {
                                        Thread.Sleep(sleepMs);
                                    }
                                }
                                catch
                                {
                                }

                                writeCalls++;
                                offset += chunk;

                                if (swWrite.ElapsedMilliseconds >= _aoWriteTimeoutMs * 8 / 10)
                                {
                                    Debug.WriteLine($"[MTX532Driver] 警告：AnalogWrite耗时接近timeout elapsed={swWrite.ElapsedMilliseconds}ms, chunk={chunk}, fifoWritableAfter={fifoWritableAfter}");
                                }
                            }
                            swTotal.Stop();

                            Debug.WriteLine(writeCalls > 0
                                ? $"[MTX532Driver] 连续输出提交完成 total={buffer.Length}, calls={writeCalls}, fifoWritableAfter={fifoWritableAfter}, elapsed={swTotal.ElapsedMilliseconds}ms"
                                : $"[MTX532Driver] 连续输出提交完成 total={buffer.Length}, calls={writeCalls}, elapsed={swTotal.ElapsedMilliseconds}ms");
                        }
                        else
                        {
                            // 队列为空，短暂等待生产者
                            Thread.Sleep(5);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[MTX532Driver] 消费者线程异常: {ex.Message}");
                    _isAcquisitionRunning = false;
                }

                Debug.WriteLine("[MTX532Driver] 消费者线程退出");
            });

            return await Task.FromResult(true);
        }

        /// <summary>
        /// 生成带相位连续性的模拟量输出缓冲区
        ///
        /// 关键改进：
        /// - 使用相位累加器，确保每个缓冲区从上一个缓冲区结束的相位继续
        /// - 避免缓冲区边界处的相位跳变
        /// - 实现真正无缝的连续波形输出
        /// </summary>
        /// <param name="samplesPerChannel">每个通道的采样点数</param>
        /// <returns>交错格式的浮点数组</returns>
        private float[] GenerateAoBufferWithPhase(int samplesPerChannel)
        {
            if (samplesPerChannel <= 0)
            {
                samplesPerChannel = CalculateContinuousBufferSize();
            }

            // 收集所有已启用的通道
            var enabledChannelIds = _channelConfigs
                .Where(kv => kv.Value.Enabled)
                .Select(kv => kv.Key)
                .ToList();

            if (enabledChannelIds.Count == 0)
            {
                return new float[0];
            }

            int channelCount = enabledChannelIds.Count;
            float[] buffer = new float[samplesPerChannel * channelCount];
            double dt = 1.0 / (_sampleRate > 0 ? _sampleRate : 1000.0);

            // 为每个通道生成采样数据
            for (int chIndex = 0; chIndex < channelCount; chIndex++)
            {
                string channelId = enabledChannelIds[chIndex];
                var cfg = _channelConfigs[channelId];

                // 初始化相位累加器（如果不存在）
                lock (_phaseLock)
                {
                    if (!_phaseAccumulators.ContainsKey(channelId))
                    {
                        _phaseAccumulators[channelId] = 0.0;
                    }
                }

                // 为当前通道生成所有采样点
                for (int n = 0; n < samplesPerChannel; n++)
                {
                    double value;

                    switch (cfg.Waveform)
                    {
                        case WaveformType.Sine:
                            // ✅ 使用相位累加器，保证相位连续
                            lock (_phaseLock)
                            {
                                value = cfg.Offset + cfg.Amplitude *
                                        Math.Sin(_phaseAccumulators[channelId]);

                                // 更新相位（每个样点增加相位增量）
                                _phaseAccumulators[channelId] += 2 * Math.PI * cfg.Frequency * dt;

                                // 相位归一化到 [0, 2π)，避免数值累积误差
                                if (_phaseAccumulators[channelId] >= 2 * Math.PI)
                                {
                                    _phaseAccumulators[channelId] -= 2 * Math.PI;
                                }
                            }
                            break;

                        case WaveformType.Square:
                            if (cfg.Frequency <= 0)
                            {
                                value = cfg.Offset;
                            }
                            else
                            {
                                lock (_phaseLock)
                                {
                                    double dutyFraction = Math.Max(0.01, Math.Min(0.99, cfg.DutyCycle / 100.0));
                                    // 将相位转换为周期内的位置 [0, 1)
                                    double phase = (_phaseAccumulators[channelId] / (2 * Math.PI)) % 1.0;

                                    // 方波参数模型：幅值表示偏离中心的幅度
                                    double highLevel = cfg.Offset + cfg.Amplitude;  // 高电平 = 偏置 + 幅值
                                    double lowLevel = cfg.Offset - cfg.Amplitude;  // 低电平 = 偏置 - 幅值
                                    value = phase < dutyFraction ? highLevel : lowLevel;

                                    // 更新相位
                                    _phaseAccumulators[channelId] += 2 * Math.PI * cfg.Frequency * dt;
                                    if (_phaseAccumulators[channelId] >= 2 * Math.PI)
                                    {
                                        _phaseAccumulators[channelId] -= 2 * Math.PI;
                                    }
                                }
                            }
                            break;

                        case WaveformType.Dc:
                        default:
                            value = cfg.Offset;
                            break;
                    }

                    // 限幅并写入缓冲区
                    buffer[n * channelCount + chIndex] = (float)ClampVoltage(value);
                }
            }

            return buffer;
        }

        /// <summary>
        /// 生成模拟量输出缓冲区（一次性写入模式）
        ///
        /// 功能说明：
        /// - 生成指定采样点数的 AO 缓冲区
        /// - 缓冲区格式为交错排列： [ch0_sample0, ch1_sample0, ..., chN_sample0, ch0_sample1, ...]
        /// - 用于单次/一次性 AO 写入场景，不保证相位连续性
        /// </summary>
        /// <param name="samplesPerChannel">每个通道的采样点数</param>
        /// <returns>交错格式的浮点数组，如果没有启用通道则返回空数组</returns>
        private float[] GenerateAoBuffer(int samplesPerChannel)
        {
            // 收集所有已启用的通道
            var enabledChannelIds = _channelConfigs
                .Where(kv => kv.Value.Enabled)
                .Select(kv => kv.Key)
                .ToList();

            if (enabledChannelIds.Count == 0)
            {
                return new float[0];
            }

            int channelCount = enabledChannelIds.Count;
            float[] buffer = new float[samplesPerChannel * channelCount];

            // 为每个通道生成采样数据
            for (int chIndex = 0; chIndex < channelCount; chIndex++)
            {
                string channelId = enabledChannelIds[chIndex];
                var cfg = _channelConfigs[channelId];

                // 为当前通道生成所有采样点
                for (int n = 0; n < samplesPerChannel; n++)
                {
                    double value;

                    switch (cfg.Waveform)
                    {
                        case WaveformType.Sine:
                            // 使用时间索引计算正弦波（不保证相位连续）
                            double time = n / (_sampleRate > 0 ? _sampleRate : 1000.0);
                            value = cfg.Offset + cfg.Amplitude * Math.Sin(2 * Math.PI * cfg.Frequency * time);
                            break;

                        case WaveformType.Square:
                            if (cfg.Frequency <= 0)
                            {
                                value = cfg.Offset;
                            }
                            else
                            {
                                double period = 1.0 / cfg.Frequency;
                                double t = n / (_sampleRate > 0 ? _sampleRate : 1000.0);
                                double dutyFraction = Math.Max(0.01, Math.Min(0.99, cfg.DutyCycle / 100.0));
                                double phaseTime = t % period;

                                // 方波参数模型：幅值表示偏离中心的幅度
                                // 高电平 = 偏置 + 幅值，低电平 = 偏置 - 幅值
                                double highLevel = cfg.Offset + cfg.Amplitude;
                                double lowLevel = cfg.Offset - cfg.Amplitude;
                                value = phaseTime < dutyFraction * period ? highLevel : lowLevel;
                            }
                            break;

                        case WaveformType.Dc:
                        default:
                            value = cfg.Offset;
                            break;
                    }

                    // 限幅并写入缓冲区
                    buffer[n * channelCount + chIndex] = (float)ClampVoltage(value);
                }
            }

            return buffer;
        }

        /// <summary>
        /// 原地生成缓冲区（避免重复分配内存）
        /// </summary>
        private void GenerateAoBufferInPlace(ref float[] buffer, int samplesPerChannel)
        {
            var enabledChannelIds = _channelConfigs
                .Where(kv => kv.Value.Enabled)
                .Select(kv => kv.Key)
                .ToList();

            if (enabledChannelIds.Count == 0)
                return;

            int channelCount = enabledChannelIds.Count;
            int requiredSize = samplesPerChannel * channelCount;

            // 确保buffer大小正确
            if (buffer == null || buffer.Length != requiredSize)
            {
                buffer = new float[requiredSize];
            }

            double dt = 1.0 / (_sampleRate > 0 ? _sampleRate : 1000.0);

            for (int chIndex = 0; chIndex < channelCount; chIndex++)
            {
                string channelId = enabledChannelIds[chIndex];
                var cfg = _channelConfigs[channelId];

                lock (_phaseLock)
                {
                    if (!_phaseAccumulators.ContainsKey(channelId))
                    {
                        _phaseAccumulators[channelId] = 0.0;
                    }
                }

                for (int n = 0; n < samplesPerChannel; n++)
                {
                    double value;

                    switch (cfg.Waveform)
                    {
                        case WaveformType.Sine:
                            lock (_phaseLock)
                            {
                                value = cfg.Offset + cfg.Amplitude *
                                        Math.Sin(_phaseAccumulators[channelId]);

                                _phaseAccumulators[channelId] += 2 * Math.PI * cfg.Frequency * dt;

                                if (_phaseAccumulators[channelId] >= 2 * Math.PI)
                                {
                                    _phaseAccumulators[channelId] -= 2 * Math.PI;
                                }
                            }
                            break;

                        case WaveformType.Square:
                            if (cfg.Frequency <= 0)
                            {
                                value = cfg.Offset;
                            }
                            else
                            {
                                lock (_phaseLock)
                                {
                                    double dutyFraction = Math.Max(0.01, Math.Min(0.99, cfg.DutyCycle / 100.0));
                                    double phase = (_phaseAccumulators[channelId] / (2 * Math.PI)) % 1.0;

                                    // 方波参数模型：幅值表示偏离中心的幅度
                                    double highLevel = cfg.Offset + cfg.Amplitude;  // 高电平 = 偏置 + 幅值
                                    double lowLevel = cfg.Offset - cfg.Amplitude;  // 低电平 = 偏置 - 幅值
                                    value = phase < dutyFraction ? highLevel : lowLevel;

                                    _phaseAccumulators[channelId] += 2 * Math.PI * cfg.Frequency * dt;
                                    if (_phaseAccumulators[channelId] >= 2 * Math.PI)
                                    {
                                        _phaseAccumulators[channelId] -= 2 * Math.PI;
                                    }
                                }
                            }
                            break;

                        case WaveformType.Dc:
                        default:
                            value = cfg.Offset;
                            break;
                    }

                    buffer[n * channelCount + chIndex] = (float)ClampVoltage(value);
                }
            }
        }

        /// <summary>
        /// 停止连续模拟量输出任务
        /// 
        /// 工作流程：
        /// 1. 设置运行状态标志为 false，通知后台任务退出循环
        /// 2. 等待后台任务自然退出（最多等待 500ms，避免长时间阻塞 UI）
        /// 
        /// 注意：
        /// - 后台任务会在当前循环结束后检查 _isAcquisitionRunning 标志并退出
        /// - 如果任务正在执行 AnalogWrite，会等待该操作完成后再退出
        /// - 最多等待 500ms，超时后不再等待（避免 UI 阻塞）
        /// </summary>
        /// <returns>停止成功返回 true</returns>
        public async Task<bool> StopAcquisitionAsync()
        {
            var sw = Stopwatch.StartNew();
            _isAcquisitionRunning = false;

            _runningSamplesPerChannel = 0;
            _runningChannelCount = 0;

            try
            {
                // 等待生产者和消费者线程结束
                var tasks = new List<Task>();

                if (_producerTask != null && !_producerTask.IsCompleted)
                {
                    tasks.Add(_producerTask);
                }

                if (_consumerTask != null && !_consumerTask.IsCompleted)
                {
                    tasks.Add(_consumerTask);
                }

                if (_aoTask != null && !_aoTask.IsCompleted)
                {
                    tasks.Add(_aoTask);
                }

                if (tasks.Any())
                {
                    var swWait = Stopwatch.StartNew();
                    await Task.WhenAny(Task.WhenAll(tasks), Task.Delay(2000));
                    swWait.Stop();
                    Debug.WriteLine($"[MTX532Driver] StopAcquisitionAsync waited elapsed={swWait.ElapsedMilliseconds}ms");
                }

                // 清空缓冲区队列
                while (_bufferQueue.TryDequeue(out _)) { }

                // ✅ 清理相位累加器
                lock (_phaseLock)
                {
                    _phaseAccumulators.Clear();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MTX532Driver] 停止 AO 任务时异常: {ex.Message}");
            }

            sw.Stop();
            Debug.WriteLine($"[MTX532Driver] 连续输出已停止 StopAcquisitionAsync total elapsed={sw.ElapsedMilliseconds}ms");
            return true;
        }

        /// <summary>
        /// 获取设备当前状态信息
        /// 
        /// 返回的状态信息包括：
        /// - IsConnected: 设备连接状态
        /// - IsAcquisitionRunning: 连续输出运行状态
        /// - SampleRate: 当前采样率（Hz）
        /// </summary>
        /// <returns>包含设备状态信息的字典</returns>
        public async Task<Dictionary<string, object>> GetStatusAsync()
        {
            var dict = new Dictionary<string, object>
            {
                { "IsConnected", _isConnected },              // 设备连接状态
                { "IsAcquisitionRunning", _isAcquisitionRunning }, // 连续输出运行状态
                { "SampleRate", _sampleRate }                 // 当前采样率（Hz）
            };
            return await Task.FromResult(dict);
        }

        /// <summary>
        /// 重置所有通道配置到默认状态
        /// 
        /// 功能说明：
        /// - 将所有通道设置为：未启用、直流波形、偏移和幅度均为 0V
        /// - 清除所有通道的最后输出值记录
        /// - 此操作不会停止正在运行的连续输出任务
        /// </summary>
        /// <returns>重置成功返回 true</returns>
        public async Task<bool> ResetAsync()
        {
            // 遍历所有通道，重置为默认配置
            foreach (var key in _channelConfigs.Keys)
            {
                _channelConfigs[key].Enabled = false;         // 禁用通道
                _channelConfigs[key].Waveform = WaveformType.Dc; // 设置为直流波形
                _channelConfigs[key].Offset = 0.0;            // 偏移为 0V
                _channelConfigs[key].Amplitude = 0.0;        // 幅度为 0V
                _lastOutputValues[key] = 0.0;                // 清除最后输出值
            }
            return await Task.FromResult(true);
        }

        /// <summary>
        /// 设备自检
        /// 
        /// 功能说明：
        /// - 检查设备连接状态
        /// - 如果设备已连接，返回 true；否则返回 false
        /// </summary>
        /// <returns>设备已连接返回 true，否则返回 false</returns>
        public async Task<bool> SelfTestAsync()
        {
            return await Task.FromResult(_isConnected);
        }

        /// <summary>
        /// 构建芒果树设备配置字符串
        /// 
        /// 功能说明：
        /// - 该字符串用于 MTDAQ.Open() 调用，包含设备模型、槽位、采样模式等信息
        /// - 配置字符串采用 XML 格式，包含多个配置段
        /// - 芒果树 SDK 会根据此配置字符串识别设备并初始化硬件
        /// 
        /// 配置字符串结构：
        /// 1. 版权标识头：----------MangoTreeCopyright----------
        /// 2. 设备信息段（&lt;Device&gt;）：
        ///    - Device-Model: 设备型号（如 "X532"）
        ///    - Device-IP: IP 地址（PXI 设备通常为空）
        ///    - Device-ID: 设备 ID（* 表示自动识别）
        ///    - Device-Slot: 槽位编号（PXI 机箱中的槽位位置）
        /// 3. DAQ 模式段（&lt;DAQMode&gt;）：
        ///    - DAQMode: 0 = 实时模式，1 = 文件模式
        ///    - Path: 文件模式时的路径（实时模式为空）
        /// 4. AO 配置段（&lt;AO&gt;）：
        ///    - AO-Channel: 已启用通道列表（如 "0,1,2"）
        ///    - AO-SampleRate: 采样率（如 "100000Hz"）
        /// 5. 版权标识尾：----------www.mangotree.cn----------
        /// 
        /// 注意：
        /// - 配置字符串必须严格按照芒果树 SDK 要求的格式，否则设备无法识别
        /// - 每个配置项必须以分号（;）结尾
        /// - 换行符使用 \n
        /// </summary>
        /// <returns>符合芒果树配置格式的字符串</returns>
        private string BuildConfigString()
        {
            // TODO: 将 IP、ID 等从 _device 或外部配置中获取，这里先占位
            // 获取设备型号，如果为空则使用默认值 "X532"
            string deviceModel = string.IsNullOrEmpty(_device?.Model) ? "X532" : _device.Model;
            var modelUpper = deviceModel.ToUpperInvariant();
            if (modelUpper.Contains("MT-X532") || modelUpper.Contains("MTX532"))
                deviceModel = "X532";

            // 从 PXI 设备基类获取槽位编号（在机箱视图中拖拽后由 SlotIndex 设置）
            // 槽位编号用于在 PXI 机箱中定位设备，从 1 开始编号
            // 如果设备不是 PXI 设备或未设置槽位，默认使用 0
            int slotIndex = GetSlotIndexForConfig();

            // 构建完整的配置字符串，包含：
            // 1. 版权标识头
            // 2. 设备信息段（型号、IP、ID、槽位）
            // 3. DAQ 模式段（模式=0 表示实时模式）
            // 4. AO 配置段（由 BuildAoConfigSection() 生成）
            // 5. 版权标识尾
            string config =
                "----------MangoTreeCopyright----------\n" +
                "<Device>\n" +
                $"Device-Model={deviceModel};\n" +           // 设备型号，如 "X532"
                "Device-IP=;\n" +                           // IP 地址（PXI 设备通常为空）
                "Device-ID=*;\n" +                           // 设备 ID（* 表示自动识别）
                $"Device-Slot={slotIndex};\n" +             // 槽位编号（PXI 机箱中的位置）
                "</Device>\n" +
                "<DAQMode>\n" +
                "DAQMode=0;\n" +                            // 0 = 实时模式，1 = 文件模式
                "Path=;\n" +                                // 文件模式时的路径（实时模式为空）
                "</DAQMode>\n" +
                BuildAoConfigSection() +                    // 生成 AO 通道和采样率配置
                "----------www.mangotree.cn----------";

            return config;
        }

        private int GetSlotIndexForConfig()
        {
            if (_slotNumberOverride.HasValue)
                return _slotNumberOverride.Value;

            if (_device is PxiDeviceBase pxiDevice && pxiDevice.SlotIndex > 0)
                return pxiDevice.SlotIndex;

            return 0;
        }

        /// <summary>
        /// 构建 AO（模拟量输出）配置段
        /// 
        /// 功能说明：
        /// - 生成包含已启用通道列表和采样率的配置字符串
        /// - 该配置段会被插入到 BuildConfigString() 生成的完整配置字符串中
        /// - 芒果树 SDK 会根据此配置初始化 AO 通道和采样率
        /// 
        /// 配置内容：
        /// - AO-Channel: 已启用通道的索引列表，格式如 "0,1,2,5"（逗号分隔）
        /// - AO-SampleRate: 采样率，格式如 "100000Hz"（必须包含 "Hz" 后缀）
        /// 
        /// 通道索引提取：
        /// - 从通道 ID（如 "AO0", "AO1"）中提取数字部分作为通道索引
        /// - 只包含已启用（Enabled = true）的通道
        /// - 如果没有启用通道，默认使用通道 0（避免配置错误导致 SDK 报错）
        /// 
        /// 采样率格式：
        /// - 必须为整数，不能有小数部分
        /// - 必须包含 "Hz" 后缀
        /// - 如果采样率无效（小于 0），默认使用 1000Hz
        /// </summary>
        /// <returns>AO 配置段的 XML 格式字符串</returns>
        private string BuildAoConfigSection()
        {
            // 收集所有已启用的通道索引（从 "AO0", "AO1" 等字符串中提取数字）
            var enabledChannels = new List<int>();
            foreach (var kv in _channelConfigs)
            {
                if (kv.Value.Enabled)
                {
                    // 从 "AO0" 格式的通道 ID 中提取数字索引
                    // 例如："AO0" → "0" → 0, "AO15" → "15" → 15
                    if (int.TryParse(kv.Key.Replace("AO", string.Empty), out int channelIndex))
                    {
                        enabledChannels.Add(channelIndex);
                    }
                }
            }

            // 生成通道列表字符串，格式如 "0,1,2,5"（逗号分隔）
            // 如果没有启用通道，默认打开全通道（避免仅打开 AO0 导致后续通道无法输出）
            string channelList = enabledChannels.Count > 0
                ? string.Join(",", enabledChannels)
                : string.Join(",", Enumerable.Range(0, 32));

            // 格式化采样率字符串，确保为整数且至少为 1000 Hz
            // 采样率必须为整数，不能有小数部分（使用 "F0" 格式化为整数）
            // 如果采样率无效（<= 0），默认使用 1000Hz
            string srString = _sampleRate <= 0 ? "1000" : _sampleRate.ToString("F0");

            // 构建 AO 配置段，包含通道列表和采样率
            // 注意：每个配置项必须以分号（;）结尾，采样率必须包含 "Hz" 后缀
            string ao =
                "<AO>\n" +
                $"AO-Channel={channelList};\n" +      // 通道列表，如 "0,1,2"
                $"AO-SampleRate={srString}Hz;\n" +    // 采样率，如 "100000Hz"（必须包含 "Hz" 后缀）
                "</AO>\n";

            return ao;
        }

        /// <summary>
        /// 根据当前采样率计算缓冲区大小（每通道样点数）
        /// 与例程保持一致：一次发送 1 秒的数据（样点数 = 采样率）
        /// 物理上：1秒 ÷ 采样率 = 每个采样点的时间间隔
        /// </summary>
        private int CalculateOptimalBufferSize()
        {
            if (_sampleRate <= 0)
            {
                return 1000; // 默认值：假设采样率为 1000Hz
            }

            // 与例程保持一致：一次发送 1 秒的数据
            // 样点数 = 采样率（采样点/秒）× 1秒 = 采样率
            // 例如：采样率 1000Hz → 发送 1000 个点 = 1秒的数据
            int samples = (int)_sampleRate;

            return samples;
        }

        private int CalculateContinuousBufferSize()
        {
            if (_sampleRate <= 0)
            {
                return 200;
            }

            int channelCount = _runningChannelCount > 0
                ? _runningChannelCount
                : Math.Max(1, _channelConfigs.Count(kv => kv.Value.Enabled));

            int maxSamplesPerChannel = Math.Max(2, _aoFifoCapacitySamples / Math.Max(1, channelCount));
            int samples = (int)Math.Round(_sampleRate * _continuousBufferSeconds);
            if (samples > maxSamplesPerChannel)
            {
                samples = maxSamplesPerChannel;
            }
            if (samples < 2)
            {
                samples = 2;
            }

            return samples;
        }

        private float[] GenerateAoBufferWithPhase(int samplesPerChannel, IReadOnlyList<string> enabledChannelIds)
        {
            if (samplesPerChannel <= 0)
            {
                samplesPerChannel = CalculateContinuousBufferSize();
            }

            if (enabledChannelIds == null || enabledChannelIds.Count == 0)
            {
                return new float[0];
            }

            int channelCount = enabledChannelIds.Count;
            float[] buffer = new float[samplesPerChannel * channelCount];
            double dt = 1.0 / (_sampleRate > 0 ? _sampleRate : 1000.0);

            for (int chIndex = 0; chIndex < channelCount; chIndex++)
            {
                string channelId = enabledChannelIds[chIndex];
                if (!_channelConfigs.TryGetValue(channelId, out var cfg) || cfg == null)
                {
                    continue;
                }

                lock (_phaseLock)
                {
                    if (!_phaseAccumulators.ContainsKey(channelId))
                    {
                        _phaseAccumulators[channelId] = 0.0;
                    }
                }

                for (int n = 0; n < samplesPerChannel; n++)
                {
                    double value;

                    switch (cfg.Waveform)
                    {
                        case WaveformType.Sine:
                            lock (_phaseLock)
                            {
                                value = cfg.Offset + cfg.Amplitude * Math.Sin(_phaseAccumulators[channelId]);
                                _phaseAccumulators[channelId] += 2 * Math.PI * cfg.Frequency * dt;
                                if (_phaseAccumulators[channelId] >= 2 * Math.PI)
                                {
                                    _phaseAccumulators[channelId] -= 2 * Math.PI;
                                }
                            }
                            break;

                        case WaveformType.Square:
                            if (cfg.Frequency <= 0)
                            {
                                value = cfg.Offset;
                            }
                            else
                            {
                                lock (_phaseLock)
                                {
                                    double dutyFraction = Math.Max(0.01, Math.Min(0.99, cfg.DutyCycle / 100.0));
                                    double phase = (_phaseAccumulators[channelId] / (2 * Math.PI)) % 1.0;
                                    double highLevel = cfg.Offset + cfg.Amplitude;
                                    double lowLevel = cfg.Offset - cfg.Amplitude;
                                    value = phase < dutyFraction ? highLevel : lowLevel;

                                    _phaseAccumulators[channelId] += 2 * Math.PI * cfg.Frequency * dt;
                                    if (_phaseAccumulators[channelId] >= 2 * Math.PI)
                                    {
                                        _phaseAccumulators[channelId] -= 2 * Math.PI;
                                    }
                                }
                            }
                            break;

                        case WaveformType.Dc:
                        default:
                            value = cfg.Offset;
                            break;
                    }

                    buffer[n * channelCount + chIndex] = (float)ClampVoltage(value);
                }
            }

            return buffer;
        }


        /// <summary>
        /// 根据通道配置和当前时间生成单个采样点的电压值
        /// 
        /// 功能说明：
        /// - 根据通道的波形类型、幅度、偏移、频率等参数，计算指定时刻的电压值
        /// - 支持直流（DC）和正弦波（Sine）两种波形类型
        /// - 生成的电压值会在后续步骤中被限制在有效范围内（-10V ~ +10V）
        /// 
        /// 波形计算公式：
        /// - 直流（DC）：V(t) = Offset（恒定电压，不随时间变化）
        /// - 正弦波（Sine）：V(t) = Offset + Amplitude * sin(2π * Frequency * t)
        ///   - Offset: 直流偏移（伏特），决定波形的中心位置
        ///   - Amplitude: 幅值（伏特），决定波形的峰值幅度
        ///   - Frequency: 频率（Hz），决定波形的周期
        ///   - t: 时间（秒），从波形开始时刻算起
        /// 
        /// 示例：
        /// - 直流：Offset = 5V → 输出恒定的 5V
        /// - 正弦波：Offset = 0V, Amplitude = 3V, Frequency = 100Hz
        ///   → 输出 -3V ~ +3V 的正弦波，频率为 100Hz
        /// - 正弦波：Offset = 2V, Amplitude = 1V, Frequency = 50Hz
        ///   → 输出 1V ~ 3V 的正弦波（中心在 2V），频率为 50Hz
        /// </summary>
        /// <param name="cfg">通道配置（包含波形类型、幅度、偏移、频率等）</param>
        /// <param name="t">当前采样时间（秒），从波形开始时刻算起</param>
        /// <returns>计算得到的电压值（伏特），范围可能超出 [-10V, +10V]，后续会被限制</returns>
        private static double GenerateSample(Mtx532AoChannelConfig cfg, double t)
        {
            switch (cfg.Waveform)
            {
                case WaveformType.Sine:
                    // 正弦波：Offset + Amplitude * sin(2π * Frequency * t)
                    // Offset 为直流偏移（伏特），决定波形的中心位置
                    // Amplitude 为幅值（伏特），决定波形的峰值幅度
                    // Frequency 为频率（Hz），决定波形的周期（周期 = 1 / Frequency）
                    // t 为时间（秒），从波形开始时刻算起
                    // 2π * Frequency * t 计算当前时刻的相位（弧度）
                    return cfg.Offset + cfg.Amplitude * Math.Sin(2 * Math.PI * cfg.Frequency * t);
                case WaveformType.Square:
                    // 方波：一个周期内前面的部分为高电平，后面的部分为低电平
                    // 占空比以百分比表示高电平所占周期比例
                    if (cfg.Frequency <= 0)
                    {
                        // 没有频率信息时，退化为直流
                        return cfg.Offset;
                    }

                    double duty = cfg.DutyCycle <= 0 ? 50.0 : cfg.DutyCycle;
                    double dutyFraction = Math.Max(0.01, Math.Min(0.99, duty / 100.0));

                    double period = 1.0 / cfg.Frequency;
                    double phaseTime = t % period;

                    // 方波参数模型：幅值表示偏离中心的幅度
                    // 高电平 = 偏置 + 幅值，低电平 = 偏置 - 幅值
                    // 方波沿着偏置水平轴对称，不能超出±10V范围
                    double highLevel = cfg.Offset + cfg.Amplitude;
                    double lowLevel = cfg.Offset - cfg.Amplitude;

                    return phaseTime < dutyFraction * period ? highLevel : lowLevel;
                case WaveformType.Dc:
                default:
                    // 直流信号：直接返回偏移值（恒定电压，不随时间变化）
                    // 无论 t 为何值，都返回相同的 Offset 值
                    return cfg.Offset;
            }
        }

        /// <summary>
        /// 根据偏置和波形类型计算最大允许的幅值
        /// 确保波形输出不会超出 MT-X532 的有效范围 (-10V 到 +10V)
        /// </summary>
        /// <param name="offset">偏置电压（伏特），可以是任意值</param>
        /// <param name="waveform">波形类型</param>
        /// <returns>最大允许的幅值（伏特）</returns>
        private static double CalculateMaxAllowedAmplitude(double offset, WaveformType waveform)
        {
            switch (waveform)
            {
                case WaveformType.Sine:
                case WaveformType.Square:
                    // 正弦波和方波：V = Offset ± Amplitude
                    // 最大幅值受限于距离±10V的最近边界
                    double distanceToUpper = 10.0 - offset;
                    double distanceToLower = offset - (-10.0);
                    return Math.Min(distanceToUpper, distanceToLower);

                case WaveformType.Dc:
                default:
                    // 直流波形没有幅值
                    return 0.0;
            }
        }

        /// <summary>
        /// 将电压值限制在 MT-X532 的有效输出范围内
        /// MT-X532 的输出范围为 -10V 到 +10V
        /// </summary>
        /// <param name="v">原始电压值（伏特）</param>
        /// <returns>限制后的电压值，范围 [-10.0, +10.0]</returns>
        private static double ClampVoltage(double v)
        {
            // 限制上限：最大输出 +10V
            if (v > 10.0) return 10.0;
            // 限制下限：最小输出 -10V
            if (v < -10.0) return -10.0;
            // 在有效范围内，直接返回
            return v;
        }

        /// <summary>
        /// 分析缓冲区中的波形频率（用于诊断）
        /// 通过零交叉点计数估算频率
        /// </summary>
        /// <param name="buffer">包含交错数据的缓冲区</param>
        /// <param name="channelIndex">通道索引（0-based）</param>
        /// <param name="channelCount">总通道数</param>
        /// <param name="sampleRate">采样率（Hz）</param>
        /// <returns>估算的频率（Hz）</returns>
        private double AnalyzeBufferFrequency(float[] buffer, int channelIndex, int channelCount, double sampleRate)
        {
            if (buffer == null || buffer.Length == 0 || channelCount <= 0)
                return 0.0;

            // 提取指定通道的数据
            var channelData = new List<double>();
            for (int i = channelIndex; i < buffer.Length; i += channelCount)
            {
                channelData.Add(buffer[i]);
            }

            if (channelData.Count < 4) // 需要至少4个点来检测频率
                return 0.0;

            // 寻找零交叉点
            int zeroCrossings = 0;
            for (int i = 1; i < channelData.Count; i++)
            {
                if ((channelData[i - 1] >= 0 && channelData[i] < 0) ||
                    (channelData[i - 1] <= 0 && channelData[i] > 0))
                {
                    zeroCrossings++;
                }
            }

            // 频率估算：交叉点数 / 2 / 时间
            // 每个完整周期有两个零交叉点
            double timeSpan = channelData.Count / sampleRate; // 缓冲区持续时间
            double estimatedFrequency = zeroCrossings / (2.0 * timeSpan);

            Debug.WriteLine($"[MTX532Driver] 频率分析 - 通道{channelIndex}, 样点数: {channelData.Count}, 零交叉点: {zeroCrossings}, 时间: {timeSpan:F3}s, 估算频率: {estimatedFrequency:F3}Hz");

            return estimatedFrequency;
        }

        /// <summary>
        /// 诊断指定通道的生成波形频率
        /// 生成一个缓冲区并分析其中的频率
        /// </summary>
        /// <param name="channelId">通道ID（如"AO0"）</param>
        /// <returns>估算的频率（Hz），失败返回0</returns>
        public double DiagnoseGeneratedBufferFrequency(string channelId)
        {
            try
            {
                if (!_channelConfigs.TryGetValue(channelId, out var cfg) || !cfg.Enabled)
                {
                    Debug.WriteLine($"[MTX532Driver] 诊断失败：通道 {channelId} 不存在或未启用");
                    return 0.0;
                }

                // 生成一个缓冲区进行分析
                int samplesPerChannel = CalculateOptimalBufferSize();
                float[] testBuffer = GenerateAoBufferWithPhase(samplesPerChannel);

                // 分析频率
                int channelIndex = 0; // 假设是第一个通道，实际应该根据channelId计算
                var enabledChannels = _channelConfigs.Where(kv => kv.Value.Enabled).OrderBy(kv => kv.Key).ToList();
                for (int i = 0; i < enabledChannels.Count; i++)
                {
                    if (enabledChannels[i].Key == channelId)
                    {
                        channelIndex = i;
                        break;
                    }
                }

                double estimatedFreq = AnalyzeBufferFrequency(testBuffer, channelIndex, enabledChannels.Count, _sampleRate);

                Debug.WriteLine($"[MTX532Driver] 通道 {channelId} 诊断完成 - 配置频率: {cfg.Frequency}Hz, 估算频率: {estimatedFreq:F3}Hz");

                return estimatedFreq;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MTX532Driver] 频率诊断异常: {ex.Message}");
                return 0.0;
            }
        }

        /// <summary>
        /// MT-X532 模拟量输出通道配置类
        /// 存储每个通道的输出参数，包括波形类型、幅度、偏移、频率等
        /// </summary>
        private class Mtx532AoChannelConfig
        {
            /// <summary>通道标识符，格式如 "AO0", "AO1" 等</summary>
            public string ChannelId { get; set; }
            /// <summary>通道是否启用，只有启用的通道才会参与输出</summary>
            public bool Enabled { get; set; }
            /// <summary>波形类型：DC（直流）或 Sine（正弦波）</summary>
            public WaveformType Waveform { get; set; }
            /// <summary>波形幅值（伏特），仅对正弦波有效</summary>
            public double Amplitude { get; set; }
            /// <summary>直流偏移（伏特），所有波形类型都有效</summary>
            public double Offset { get; set; }
            /// <summary>波形频率（Hz），仅对正弦波和方波有效</summary>
            public double Frequency { get; set; }
            /// <summary>方波占空比（百分比 1-99），仅对方波有效</summary>
            public double DutyCycle { get; set; }
        }

        /// <summary>
        /// 波形类型枚举
        /// 定义 MT-X532 支持的输出波形类型
        /// </summary>
        public enum WaveformType
        {
            /// <summary>直流信号，输出恒定电压（等于 Offset）</summary>
            Dc,
            /// <summary>正弦波信号，输出 Offset + Amplitude * sin(2π * Frequency * t)</summary>
            Sine,
            /// <summary>方波信号，根据占空比在高电平和低电平之间切换</summary>
            Square
        }
    }
}
