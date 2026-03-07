using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using MeasureControl.Models;
using MeasureControl.Models.Devices;
using JY7131;
using MeasureControl.Helpers;

namespace MeasureControl.Drivers
{
    /// <summary>
    /// 简仪 JY7131 数字量输入输出驱动
    /// 支持 32路DI + 32路DO
    /// </summary>
    public class JY7131Driver : IDeviceDriver
    {
        /// <summary>
        /// 采集状态改变事件（此驱动不支持，始终为空实现）
        /// </summary>
        public event EventHandler<AcquisitionStatusChangedEventArgs> AcquisitionStatusChanged
        {
            add { }
            remove { }
        }
        #region 私有字段

        private readonly DeviceBase _device;
        private readonly int _slotNumber;
        private bool _isConnected;
        private bool _isAcquisitionRunning;

        // ========== 临时模拟模式（调试用，正式使用时设为 false）==========
        private const bool USE_SIMULATION = false;  // 
        // ================================================================

        // JY7131 DI/DO Task 对象
        private JY7131DITask _diTask;
        private JY7131DOTask _doTask;

        // 通道数据存储（模拟模式下也用于存储 DO 值，DI 读取时回环）
        private readonly Dictionary<string, double> _channelValues = new Dictionary<string, double>();

        // 通道配置
        private readonly Dictionary<string, ChannelConfig> _channelConfigs = new Dictionary<string, ChannelConfig>();

        //外部电源控制（ASCII，需填写实际串口/命令）
        private const string PowerControlComPort = "COM24"; // 实际电源串口 第一套
        //private const string PowerControlComPort = "COM11"; // 实际电源串口 第二套
        //private const string PowerControlComPort = "COM9"; // 实际电源串口 第三套
        private const string PowerSetOutputOnBody = "w12=1,";    // 开启输出
        private const string PowerSetOutputOffBody = "w12=0,";  // 关闭输出
        private static readonly byte[] PowerGroupAddresses = { 7, 8, 9, 10 };
        private double _currentPowerVoltage;

        private void TrySendPowerCommand(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return;
            try
            {
                using (SerialPortMutex.AcquireAsync(PowerControlComPort).GetAwaiter().GetResult())
                {
                    using var client = new Dpm8600Client(PowerControlComPort, PowerSupplyProtocol.AsciiCustom);
                    client.SendAscii(command, expectReply: false);
                }
                Debug.WriteLine($"[JY7131Driver] Power command sent: {command}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[JY7131Driver] Power command failed: {ex.Message}");
            }
        }
        #endregion

        #region 属性

        /// <summary>
        /// 设备ID
        /// </summary>
        public string DeviceId => _device?.Id ?? string.Empty;

        /// <summary>
        /// 设备名称
        /// </summary>
        public string DeviceName => _device?.Name ?? "JY7131";

        /// <summary>
        /// 是否已连接
        /// </summary>
        public bool IsConnected => _isConnected;

        /// <summary>
        /// 是否为模拟驱动（JY7131 不支持模拟，始终为 false）
        /// </summary>
        public bool IsSimulated => false;

        /// <summary>
        /// JY7131是数字I/O设备，支持双向通信
        /// </summary>
        public DeviceCapability Capability => DeviceCapability.Bidirectional;

        /// <summary>
        /// 插槽号
        /// </summary>
        public int SlotNumber => _slotNumber;

        #endregion

        #region 构造函数

        /// <summary>
        /// 创建 JY7131 驱动实例
        /// </summary>
        /// <param name="device">设备实例</param>
        /// <param name="slotNumber">PXI插槽号（默认0）</param>
        public JY7131Driver(DeviceBase device, int slotNumber = 0)
        {
            _device = device ?? throw new ArgumentNullException(nameof(device));
            _slotNumber = slotNumber;
            _isConnected = false;
            _isAcquisitionRunning = false;

            // 初始化通道值
            InitializeChannels();
        }

        /// <summary>
        /// 初始化通道
        /// </summary>
        private void InitializeChannels()
        {
            // 初始化 32 路 DI 通道
            for (int i = 0; i < 32; i++)
            {
                string channelId = $"DI{i}";
                _channelValues[channelId] = 0;
                _channelConfigs[channelId] = new ChannelConfig
                {
                    ChannelId = channelId,
                    ChannelType = "DI",
                    PortIndex = i / 8,      // Port 0-3
                    ChannelIndex = i % 8    // Channel 0-7
                };
            }

            // 初始化 32 路 DO 通道
            for (int i = 0; i < 32; i++)
            {
                string channelId = $"DO{i}";
                _channelValues[channelId] = 0;
                _channelConfigs[channelId] = new ChannelConfig
                {
                    ChannelId = channelId,
                    ChannelType = "DO",
                    PortIndex = i / 8,      // Port 0-3
                    ChannelIndex = i % 8    // Channel 0-7
                };
            }
        }

        /// <summary>
        /// 重新配置 DO 输出模式（Sourcing / Sinking / Push_Pull）
        /// 仅影响 DO 任务，不影响 DI
        /// </summary>
        /// <param name="mode">输出模式字符串</param>
        public Task<bool> ReconfigureDoOutputModeAsync(string mode)
        {
            if (!_isConnected)
            {
                Debug.WriteLine($"[JY7131Driver] 设备未连接，无法重新配置 DO 输出模式");
                return Task.FromResult(false);
            }

            DOTerminal terminalMode = DOTerminal.Push_Pull;

            switch (mode)
            {
                case "Sourcing":
                    terminalMode = DOTerminal.Sourcing;
                    break;
                case "Sinking":
                    terminalMode = DOTerminal.Sinking;
                    break;
                case "Push_Pull":
                default:
                    terminalMode = DOTerminal.Push_Pull;
                    break;
            }

            try
            {
                Debug.WriteLine($"[JY7131Driver] 重新配置 DO 输出模式为 {terminalMode}");

                if (_doTask != null)
                {
                    try
                    {
                        _doTask.Stop();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[JY7131Driver] 停止旧 DO 任务时发生异常: {ex.Message}");
                    }

                    try
                    {
                        _doTask.Channels.Clear();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[JY7131Driver] 清除旧 DO 通道时发生异常: {ex.Message}");
                    }
                }

                // 使用新的输出模式重新创建 DO Task
                _doTask = new JY7131DOTask(_slotNumber);
                for (int port = 0; port < 4; port++)
                {
                    _doTask.AddChannel(port, terminalMode);
                }

                // 如果采集标记为运行中，则重新启动 DO 任务
                if (_isAcquisitionRunning)
                {
                    _doTask.Start();
                }

                Debug.WriteLine($"[JY7131Driver] DO 输出模式重新配置完成");
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[JY7131Driver] 重新配置 DO 输出模式失败: {ex.Message}");
                return Task.FromResult(false);
            }
        }

        #endregion

        #region IDeviceDriver 实现

        /// <summary>
        /// 连接设备（带重试机制）
        /// </summary>
        public async Task<bool> ConnectAsync()
        {
            const int maxRetries = 2;
            const int retryDelayMs = 500;
            Exception lastException = null;

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    Debug.WriteLine($"[JY7131Driver] 正在连接设备 {DeviceName}, 插槽号: {_slotNumber}, 尝试 {attempt}/{maxRetries}");

                    // 确保先清理旧资源
                    CleanupTasks();
                    await Task.Delay(50);

                    // 创建 DI Task
                    _diTask = new JY7131DITask(_slotNumber);
                    // 添加所有 4 个端口
                    for (int port = 0; port < 4; port++)
                    {
                        _diTask.AddChannel(port);
                    }

                    // 创建 DO Task
                    _doTask = new JY7131DOTask(_slotNumber);
                    // 添加所有 4 个端口（使用推挽输出模式）
                    for (int port = 0; port < 4; port++)
                    {
                        _doTask.AddChannel(port, DOTerminal.Push_Pull);
                    }

                    // 增加稳定延时
                    await Task.Delay(200);

                    _isConnected = true;
                    Debug.WriteLine($"[JY7131Driver] 设备连接成功");

                    //// 打开板卡时
                    //TrySendPowerCommand(PowerSetVoltageCommand);
                    //TrySendPowerCommand(PowerSetOutputOnCommand);
                    return true;
                }
                catch (JYDriverException ex)
                {
                    Debug.WriteLine($"[JY7131Driver] 连接失败 (尝试 {attempt}/{maxRetries}): {ex.Message}");
                    lastException = ex;
                    _isConnected = false;
                    CleanupTasks();

                    if (attempt < maxRetries)
                    {
                        Debug.WriteLine($"[JY7131Driver] 等待 {retryDelayMs}ms 后重试...");
                        await Task.Delay(retryDelayMs);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[JY7131Driver] 连接失败 (尝试 {attempt}/{maxRetries}): {ex.Message}");
                    lastException = ex;
                    _isConnected = false;
                    CleanupTasks();

                    if (attempt < maxRetries)
                    {
                        Debug.WriteLine($"[JY7131Driver] 等待 {retryDelayMs}ms 后重试...");
                        await Task.Delay(retryDelayMs);
                    }
                }
            }

            // 所有重试都失败
            Debug.WriteLine($"[JY7131Driver] 连接失败，已重试 {maxRetries} 次");
            throw lastException ?? new InvalidOperationException("JY7131 连接失败");
        }

        /// <summary>
        /// 清理 DI/DO Task 资源
        /// </summary>
        private void CleanupTasks()
        {
            try
            {
                if (_diTask != null)
                {
                    try { _diTask.Stop(); } catch { }
                    try { _diTask.Channels.Clear(); } catch { }
                    _diTask = null;
                }
            }
            catch { }

            try
            {
                if (_doTask != null)
                {
                    try { _doTask.Stop(); } catch { }
                    try { _doTask.Channels.Clear(); } catch { }
                    _doTask = null;
                }
            }
            catch { }
        }

        /// <summary>
        /// 断开连接
        /// </summary>
        public async Task<bool> DisconnectAsync()
        {
            try
            {
                Debug.WriteLine($"[JY7131Driver] 正在断开设备 {DeviceName}");

                // 停止采集
                if (_isAcquisitionRunning)
                {
                    await StopAcquisitionAsync();
                }

                // 清理硬件资源
                try
                {
                    _diTask?.Stop();
                    _diTask?.Channels.Clear();
                    _diTask = null;

                    _doTask?.Stop();
                    _doTask?.Channels.Clear();
                    _doTask = null;
                }
                catch (Exception cleanupEx)
                {
                    Debug.WriteLine($"[JY7131Driver] 清理资源时出错: {cleanupEx.Message}");
                }

                await Task.Delay(50);

                // 断开板卡时关闭外部电源输出
                await StopPowerOutputAsync();

                _isConnected = false;
                Debug.WriteLine($"[JY7131Driver] 设备断开成功");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[JY7131Driver] 断开失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 读取单个通道（DI）
        /// </summary>
        public Task<double> ReadChannelAsync(string channelId)
        {
            if (!_isConnected)
            {
                Debug.WriteLine($"[JY7131Driver] 设备未连接，无法读取通道 {channelId}");
                return Task.FromResult(0.0);
            }

            // 使用 Task.Run 将阻塞的硬件读取操作移到线程池线程，避免阻塞UI线程
            return Task.Run(() =>
            {
                try
                {
                    if (!_channelConfigs.TryGetValue(channelId, out var config))
                    {
                        Debug.WriteLine($"[JY7131Driver] 无效的通道ID: {channelId}");
                        return 0.0;
                    }

                    double value;

                    // ========== 模拟模式：DI 读取对应 DO 的值（回环） ==========
                    //if (USE_SIMULATION)
                    //{
                    //    if (config.ChannelType == "DI")
                    //    {
                    //        // DI0 读取 DO0 的值，DI5 读取 DO5 的值...
                    //        string doChannelId = channelId.Replace("DI", "DO");
                    //        value = _channelValues.ContainsKey(doChannelId) ? _channelValues[doChannelId] : 0;
                    //        Debug.WriteLine($"[JY7131Driver] 【模拟】{channelId} 读取 {doChannelId} 的值: {value}");
                    //    }
                    //    else
                    //    {
                    //        // DO 通道返回上次写入的值
                    //        value = _channelValues.ContainsKey(channelId) ? _channelValues[channelId] : 0;
                    //    }
                    //    return value;
                    //}
                    // ========================================================

                    // 读取 DI 通道（阻塞操作，在线程池线程执行）
                    if (config.ChannelType == "DI" && _diTask != null)
                    {
                        bool[] readValue = new bool[8];
                        _diTask.ReadSinglePoint(ref readValue, config.PortIndex);
                        value = readValue[config.ChannelIndex] ? 1 : 0;
                        lock (_channelValues)
                        {
                            _channelValues[channelId] = value;
                        }
                    }
                    else
                    {
                        // DO 通道返回上次写入的值
                        lock (_channelValues)
                        {
                            value = _channelValues.ContainsKey(channelId) ? _channelValues[channelId] : 0;
                        }
                    }

                    return value;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[JY7131Driver] 读取通道 {channelId} 失败: {ex.Message}");
                    return 0.0;
                }
            });
        }

        /// <summary>
        /// 批量读取通道（按端口分组优化，避免重复读取同一端口）
        /// </summary>
        public Task<Dictionary<string, double>> ReadChannelsBatchAsync(IEnumerable<string> channelIds)
        {
            var results = new Dictionary<string, double>();

            if (!_isConnected)
            {
                // 未连接时返回默认值
                foreach (var channelId in channelIds)
                {
                    results[channelId] = 0;
                }
                return Task.FromResult(results);
            }

            // ========== 模拟模式：DI 读取对应 DO 的值（回环） ==========
            //if (USE_SIMULATION)
            //{
            //    foreach (var channelId in channelIds)
            //    {
            //        if (!_channelConfigs.TryGetValue(channelId, out var config))
            //        {
            //            results[channelId] = 0;
            //            continue;
            //        }

            //        if (config.ChannelType == "DI")
            //        {
            //            // DI0 读取 DO0 的值，DI5 读取 DO5 的值...
            //            string doChannelId = channelId.Replace("DI", "DO");
            //            results[channelId] = _channelValues.ContainsKey(doChannelId) ? _channelValues[doChannelId] : 0;
            //        }
            //        else
            //        {
            //            // DO 通道返回上次写入的值
            //            results[channelId] = _channelValues.ContainsKey(channelId) ? _channelValues[channelId] : 0;
            //        }
            //    }
            //    return results;
            //}
            // ========================================================

            if (_diTask == null)
            {
                foreach (var channelId in channelIds)
                {
                    results[channelId] = 0;
                }
                return Task.FromResult(results);
            }

            // 使用 Task.Run 将阻塞的硬件读取操作移到线程池线程，避免阻塞UI线程
            return Task.Run(() =>
            {
                try
                {
                    // 按端口分组，避免对同一端口多次调用 ReadSinglePoint
                    var diChannelsByPort = new Dictionary<int, List<(string ChannelId, int ChannelIndex)>>();
                    var doChannels = new List<string>();

                    foreach (var channelId in channelIds)
                    {
                        if (!_channelConfigs.TryGetValue(channelId, out var config))
                        {
                            results[channelId] = 0;
                            continue;
                        }

                        if (config.ChannelType == "DI")
                        {
                            // DI 通道按端口分组
                            if (!diChannelsByPort.ContainsKey(config.PortIndex))
                            {
                                diChannelsByPort[config.PortIndex] = new List<(string, int)>();
                            }
                            diChannelsByPort[config.PortIndex].Add((channelId, config.ChannelIndex));
                        }
                        else
                        {
                            // DO 通道返回上次写入的值
                            doChannels.Add(channelId);
                        }
                    }

                    // 按端口读取 DI（每个端口只调用一次 ReadSinglePoint，阻塞操作）
                    foreach (var portGroup in diChannelsByPort)
                    {
                        int portIndex = portGroup.Key;
                        bool[] portValues = new bool[8];
                        _diTask.ReadSinglePoint(ref portValues, portIndex);

                        // 从读取的 8 个值中提取所需通道
                        foreach (var (channelId, channelIndex) in portGroup.Value)
                        {
                            double value = portValues[channelIndex] ? 1 : 0;
                            results[channelId] = value;
                            lock (_channelValues)
                            {
                                _channelValues[channelId] = value;
                            }
                        }
                    }

                    // DO 通道返回缓存值
                    lock (_channelValues)
                    {
                        foreach (var channelId in doChannels)
                        {
                            results[channelId] = _channelValues.ContainsKey(channelId) ? _channelValues[channelId] : 0;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[JY7131Driver] 批量读取失败: {ex.Message}");
                    // 出错时返回默认值
                    foreach (var channelId in channelIds)
                    {
                        if (!results.ContainsKey(channelId))
                        {
                            results[channelId] = 0;
                        }
                    }
                }

                return results;
            });
        }

        /// <summary>
        /// 写入单个通道（DO）
        /// </summary>
        public Task<bool> WriteChannelAsync(string channelId, double value)
        {
            if (!_isConnected)
            {
                Debug.WriteLine($"[JY7131Driver] 设备未连接，无法写入通道 {channelId}");
                return Task.FromResult(false);
            }

            try
            {
                if (!_channelConfigs.TryGetValue(channelId, out var config))
                {
                    Debug.WriteLine($"[JY7131Driver] 无效的通道ID: {channelId}");
                    return Task.FromResult(false);
                }

                if (config.ChannelType != "DO")
                {
                    Debug.WriteLine($"[JY7131Driver] 通道 {channelId} 不是输出通道");
                    return Task.FromResult(false);
                }

                bool boolValue = value > 0.5;

                // ========== 模拟模式：直接更新内存值，不调用硬件 ==========
                //if (USE_SIMULATION)
                //{
                //    _channelValues[channelId] = boolValue ? 1 : 0;
                //    Debug.WriteLine($"[JY7131Driver] 【模拟】写入 {channelId} = {(boolValue ? 1 : 0)}");
                //    return true;
                //}
                // ========================================================

                // 写入 DO 通道
                if (_doTask != null)
                {
                    _doTask.WriteSinglePoint(boolValue, config.PortIndex, config.ChannelIndex);
                    _channelValues[channelId] = boolValue ? 1 : 0;
                }

                Debug.WriteLine($"[JY7131Driver] 写入通道 {channelId} = {value} (bool: {boolValue})");
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[JY7131Driver] 写入通道 {channelId} 失败: {ex.Message}");
                return Task.FromResult(false);
            }
        }

        /// <summary>
        /// 批量写入通道
        /// </summary>
        public async Task<bool> WriteChannelsBatchAsync(Dictionary<string, double> channelValues)
        {
            bool allSuccess = true;

            foreach (var kvp in channelValues)
            {
                if (!await WriteChannelAsync(kvp.Key, kvp.Value))
                {
                    allSuccess = false;
                }
            }

            return allSuccess;
        }

        /// <summary>
        /// 配置通道
        /// </summary>
        public Task<bool> ConfigureChannelAsync(string channelId, Dictionary<string, object> config)
        {
            Debug.WriteLine($"[JY7131Driver] 配置通道 {channelId}");
            // JY7131 数字量通道配置较简单，主要在初始化时完成
            return Task.FromResult(true);
        }

        /// <summary>
        /// 启动采集
        /// </summary>
        public Task<bool> StartAcquisitionAsync()
        {
            if (!_isConnected)
            {
                Debug.WriteLine($"[JY7131Driver] 设备未连接，无法启动采集");
                return Task.FromResult(false);
            }

            // 如果采集已经在运行，直接返回成功，避免重复 Start 造成异常
            if (_isAcquisitionRunning)
            {
                Debug.WriteLine($"[JY7131Driver] 采集已在运行，忽略重复启动请求");
                return Task.FromResult(true);
            }

            try
            {
                Debug.WriteLine($"[JY7131Driver] 启动数据采集");

                _diTask?.Start();
                _doTask?.Start();

                _isAcquisitionRunning = true;
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[JY7131Driver] 启动采集失败: {ex.Message}");
                return Task.FromResult(false);
            }
        }

        /// <summary>
        /// 停止采集
        /// </summary>
        public Task<bool> StopAcquisitionAsync()
        {
            try
            {
                Debug.WriteLine($"[JY7131Driver] 停止数据采集");

                // 模拟模式下直接返回
                //if (USE_SIMULATION)
                //{
                //    _isAcquisitionRunning = false;
                //    return true;
                //}

                _diTask?.Stop();
                _doTask?.Stop();

                _isAcquisitionRunning = false;
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[JY7131Driver] 停止采集失败: {ex.Message}");
                return Task.FromResult(false);
            }
        }

        /// <summary>
        /// 获取设备状态
        /// </summary>
        public Task<Dictionary<string, object>> GetStatusAsync()
        {
            var status = new Dictionary<string, object>
            {
                { "IsConnected", _isConnected },
                { "IsSimulated", false },
                { "IsAcquisitionRunning", _isAcquisitionRunning },
                { "SlotNumber", _slotNumber },
                { "DIChannels", 32 },
                { "DOChannels", 32 }
            };

            return Task.FromResult(status);
        }

        /// <summary>
        /// 重置设备
        /// </summary>
        public async Task<bool> ResetAsync()
        {
            Debug.WriteLine($"[JY7131Driver] 重置设备");

            // 重置所有 DO 输出为 0
            for (int i = 0; i < 32; i++)
            {
                await WriteChannelAsync($"DO{i}", 0);
            }

            return true;
        }

        /// <summary>
        /// 自检
        /// </summary>
        public Task<bool> SelfTestAsync()
        {
            Debug.WriteLine($"[JY7131Driver] 执行自检");

            // 简单的自检：检查连接状态
            return Task.FromResult(_isConnected);
        }

        #endregion

        #region 辅助类

        public async Task SendPowerPresetCommandsAsync()
        {
            await EnsurePowerOutputAsync(_currentPowerVoltage);
        }

        public Task EnsurePowerOutputAsync(double voltage)
        {
            return Task.Run(() =>
            {
                try
                {
                    var clamped = ClampVoltage(voltage);
                    var formatted = FormatVoltageForCommand(clamped);
                    using (SerialPortMutex.AcquireAsync(PowerControlComPort).GetAwaiter().GetResult())
                    {
                        using var client = new Dpm8600Client(PowerControlComPort, PowerSupplyProtocol.AsciiCustom);
                        client.SendAsciiCommand(PowerGroupAddresses[0], $"w10={formatted},", expectReply: false); // 设置电压
                        client.SendAsciiCommand(PowerGroupAddresses[0], PowerSetOutputOnBody, expectReply: false); // 打开输出
                        _currentPowerVoltage = clamped;
                    }
                    Debug.WriteLine($"[JY7131Driver] Power output on, voltage={clamped}V (cmd={formatted})");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[JY7131Driver] Power output failed: {ex.Message}");
                }
            });
        }

        public Task EnsurePowerOutputsAsync(double group1Voltage, double group2Voltage, double group3Voltage, double group4Voltage)
        {
            return Task.Run(() =>
            {
                try
                {
                    var values = new[] { group1Voltage, group2Voltage, group3Voltage, group4Voltage };
                    using (SerialPortMutex.AcquireAsync(PowerControlComPort).GetAwaiter().GetResult())
                    {
                        using var client = new Dpm8600Client(PowerControlComPort, PowerSupplyProtocol.AsciiCustom);

                        for (int i = 0; i < PowerGroupAddresses.Length && i < values.Length; i++)
                        {
                            var clamped = ClampVoltage(values[i]);
                            var formatted = FormatVoltageForCommand(clamped);
                            client.SendAsciiCommand(PowerGroupAddresses[i], $"w10={formatted},", expectReply: false);
                            client.SendAsciiCommand(PowerGroupAddresses[i], PowerSetOutputOnBody, expectReply: false);
                        }

                        _currentPowerVoltage = ClampVoltage(group1Voltage);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[JY7131Driver] Power output failed: {ex.Message}");
                }
            });
        }

        public Task SetPowerVoltagesAsync(double group1Voltage, double group2Voltage, double group3Voltage, double group4Voltage)
        {
            return Task.Run(() =>
            {
                try
                {
                    var values = new[] { group1Voltage, group2Voltage, group3Voltage, group4Voltage };
                    using (SerialPortMutex.AcquireAsync(PowerControlComPort).GetAwaiter().GetResult())
                    {
                        using var client = new Dpm8600Client(PowerControlComPort, PowerSupplyProtocol.AsciiCustom);

                        for (int i = 0; i < PowerGroupAddresses.Length && i < values.Length; i++)
                        {
                            var clamped = ClampVoltage(values[i]);
                            var formatted = FormatVoltageForCommand(clamped);
                            client.SendAsciiCommand(PowerGroupAddresses[i], $"w10={formatted},", expectReply: false);
                        }

                        _currentPowerVoltage = ClampVoltage(group1Voltage);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[JY7131Driver] Power preset failed: {ex.Message}");
                }
            });
        }

        public Task StopPowerOutputAsync()
        {
            return Task.Run(() =>
            {
                using (SerialPortMutex.AcquireAsync(PowerControlComPort).GetAwaiter().GetResult())
                {
                    try
                    {
                        using var client = new Dpm8600Client(PowerControlComPort, PowerSupplyProtocol.AsciiCustom);
                        foreach (var addr in PowerGroupAddresses)
                        {
                            Debug.WriteLine($"[JY7131Driver] Power output off: addr={addr}");
                            client.SendAsciiCommand(addr, PowerSetOutputOffBody, expectReply: false);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[JY7131Driver] Power output off failed: {ex.Message}");
                    }
                }
            });
        }

        private static double ClampVoltage(double voltage)
        {
            if (double.IsNaN(voltage) || double.IsInfinity(voltage))
                return 0;
            return Math.Max(0, Math.Min(voltage, 32));
        }

        private static string FormatVoltageForCommand(double voltage)
        {
            var u16 = (ushort)Math.Round(voltage * 100, MidpointRounding.AwayFromZero);
            return u16.ToString("D4"); // 确保始终为4位数字，不足前面补0
        }

        /// <summary>
        /// 通道配置
        /// </summary>
        private class ChannelConfig
        {
            public string ChannelId { get; set; }
            public string ChannelType { get; set; }  // "DI" 或 "DO"
            public int PortIndex { get; set; }       // 端口号 0-3
            public int ChannelIndex { get; set; }    // 通道号 0-7
        }

        #endregion
    }
}
