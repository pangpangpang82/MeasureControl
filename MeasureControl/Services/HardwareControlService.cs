using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using MeasureControl.Drivers;
using MeasureControl.Models;
using MeasureControl.Models.Devices;
using Prism.Events;

namespace MeasureControl.Services
{
    /// <summary>
    /// 硬件控制服务
    /// 管理变量与硬件通道的绑定，处理数据读写
    /// </summary>
    public class HardwareControlService : IDisposable
    {
        #region 单例模式

        private static readonly Lazy<HardwareControlService> _instance =
            new Lazy<HardwareControlService>(() => new HardwareControlService());

        /// <summary>
        /// 获取单例实例
        /// </summary>
        public static HardwareControlService Instance => _instance.Value;

        #endregion

        #region 私有字段

        private readonly Dictionary<string, IDeviceDriver> _drivers = new Dictionary<string, IDeviceDriver>();
        private readonly Dictionary<string, VariableBinding> _variableBindings = new Dictionary<string, VariableBinding>();
        private readonly Dictionary<string, double> _variableValues = new Dictionary<string, double>();


        private DispatcherTimer _pollingTimer;
        private bool _isRunning;
        private readonly object _lock = new object();

        // 事件聚合器（用于发布变量值变化事件）
        private IEventAggregator _eventAggregator;

        #endregion

        #region 事件

        /// <summary>
        /// 变量值变化事件
        /// </summary>
        public event EventHandler<VariableValueChangedEventArgs> VariableValueChanged;

        #endregion

        #region 属性

        /// <summary>
        /// 是否正在运行
        /// </summary>
        public bool IsRunning => _isRunning;

        /// <summary>
        /// 轮询间隔（毫秒）
        /// </summary>
        public int PollingIntervalMs { get; set; } = 100;

        #endregion

        #region 构造函数

        private HardwareControlService()
        {
            _isRunning = false;
        }

        /// <summary>
        /// 初始化服务
        /// </summary>
        /// <param name="eventAggregator">事件聚合器</param>
        public void Initialize(IEventAggregator eventAggregator)
        {
            _eventAggregator = eventAggregator;
        }

        #endregion

        #region 驱动管理

        /// <summary>
        /// 注册驱动
        /// </summary>
        /// <param name="deviceId">设备ID</param>
        /// <param name="driver">驱动实例</param>
        public void RegisterDriver(string deviceId, IDeviceDriver driver)
        {
            lock (_lock)
            {
                _drivers[deviceId] = driver;
                Debug.WriteLine($"[HardwareControlService] 注册驱动: {deviceId}");
            }
        }

        /// <summary>
        /// 获取驱动
        /// </summary>
        /// <param name="deviceId">设备ID</param>
        /// <returns>驱动实例</returns>
        public IDeviceDriver GetDriver(string deviceId)
        {
            lock (_lock)
            {
                return _drivers.ContainsKey(deviceId) ? _drivers[deviceId] : null;
            }
        }

        /// <summary>
        /// 移除驱动
        /// </summary>
        /// <param name="deviceId">设备ID</param>
        public void UnregisterDriver(string deviceId)
        {
            lock (_lock)
            {
                if (_drivers.ContainsKey(deviceId))
                {
                    _drivers.Remove(deviceId);
                    Debug.WriteLine($"[HardwareControlService] 移除驱动: {deviceId}");
                }
            }
        }

        #endregion

        #region 变量绑定

        /// <summary>
        /// 绑定变量到通道
        /// </summary>
        /// <param name="variablePath">变量路径（如：测试任务1/变量表1/压力开关）</param>
        /// <param name="deviceId">设备ID</param>
        /// <param name="channelId">通道ID（如：DI0, DO0）</param>
        /// <param name="isOutput">是否为输出变量</param>
        public void BindVariable(string variablePath, string deviceId, string channelId, bool isOutput)
        {
            lock (_lock)
            {
                var binding = new VariableBinding
                {
                    VariablePath = variablePath,
                    DeviceId = deviceId,
                    ChannelId = channelId,
                    IsOutput = isOutput
                };

                _variableBindings[variablePath] = binding;
                _variableValues[variablePath] = 0;

                Debug.WriteLine($"[HardwareControlService] 绑定变量: {variablePath} -> {deviceId}/{channelId} (杈撳嚭: {isOutput})");
            }
        }

        /// <summary>
        /// 解除变量绑定
        /// </summary>
        /// <param name="variablePath">变量路径</param>
        public void UnbindVariable(string variablePath)
        {
            lock (_lock)
            {
                if (_variableBindings.ContainsKey(variablePath))
                {
                    _variableBindings.Remove(variablePath);
                    Debug.WriteLine($"[HardwareControlService] 解除绑定: {variablePath}");
                }
            }
        }

        /// <summary>
        /// 获取变量当前值
        /// </summary>
        /// <param name="variablePath">变量路径</param>
        /// <returns>变量值</returns>
        public double GetVariableValue(string variablePath)
        {
            lock (_lock)
            {
                return _variableValues.ContainsKey(variablePath) ? _variableValues[variablePath] : 0;
            }
        }

        /// <summary>
        /// 设置变量值（用于输出变量）
        /// </summary>
        /// <param name="variablePath">变量路径</param>
        /// <param name="value">要设置的值</param>
        /// <returns>是否成功</returns>
        public async Task<bool> SetVariableValueAsync(string variablePath, double value)
        {
            VariableBinding binding;
            IDeviceDriver driver;

            lock (_lock)
            {
                if (!_variableBindings.TryGetValue(variablePath, out binding))
                {
                    Debug.WriteLine($"[HardwareControlService] 变量未绑定: {variablePath}");
                    return false;
                }

                if (!binding.IsOutput)
                {
                    Debug.WriteLine($"[HardwareControlService] 变量 {variablePath} 不是输出变量");
                    return false;
                }

                if (!_drivers.TryGetValue(binding.DeviceId, out driver))
                {
                    Debug.WriteLine($"[HardwareControlService] 驱动未找到:{binding.DeviceId}");
                    return false;
                }
            }

            try
            {
                // 写入通道
                bool success = await driver.WriteChannelAsync(binding.ChannelId, value);

                if (success)
                {
                    lock (_lock)
                    {
                        _variableValues[variablePath] = value;
                    }

                    // 触发值变化事件
                    OnVariableValueChanged(variablePath, value);
                    Debug.WriteLine($"[HardwareControlService] 写入变量 {variablePath} = {value}");
                }

                return success;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HardwareControlService] 写入变量失败: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region 轮询控制

        /// <summary>
        /// 启动轮询
        /// </summary>
        public async Task StartAsync()
        {
            if (_isRunning)
                return;

            Debug.WriteLine("[HardwareControlService] 启动硬件数据轮询");

            // 创建轮询定时器 - 只负责轮询已连接的设备数据
            _pollingTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(PollingIntervalMs)
            };
            _pollingTimer.Tick += async (s, e) => await PollInputsAsync();
            _pollingTimer.Start();

            _isRunning = true;

            // 立即执行一次读取，初始化变量表的值
            await PollInputsAsync();

            Debug.WriteLine($"[HardwareControlService] 轮询定时器已启动，间隔: {PollingIntervalMs}ms");
        }

        /// <summary>
        /// 停止轮询并断开驱动（完全停止）
        /// </summary>
        public async Task StopAsync()
        {
            if (!_isRunning)
                return;

            Debug.WriteLine("[HardwareControlService] 停止硬件轮询并断开驱动");

            _pollingTimer?.Stop();
            _pollingTimer = null;

            // 停止所有驱动
            foreach (var driver in _drivers.Values)
            {
                if (driver.IsConnected)
                {
                    await driver.StopAcquisitionAsync();
                    await driver.DisconnectAsync();
                }
            }

            _isRunning = false;
        }

        /// <summary>
        /// 暂停轮询（保持驱动连接，数据冻结）
        /// </summary>
        public void Pause()
        {
            if (!_isRunning)
                return;
                
            Debug.WriteLine("[HardwareControlService] 暂停轮询（保持连接）");
            
            _pollingTimer?.Stop();
            _isRunning = false;
        }

        /// <summary>
        /// 恢复轮询（从暂停状态恢复）
        /// </summary>
        public void Resume()
        {
            if (_isRunning)
                return;

            // 检查是否有驱动连接
            bool hasConnectedDriver = _drivers.Values.Any(d => d.IsConnected);
            if (!hasConnectedDriver)
            {
                Debug.WriteLine("[HardwareControlService] 无已连接驱动，无法恢复");
                return;
            }
            
            Debug.WriteLine("[HardwareControlService] 恢复轮询");

            // 重新启动轮询定时器
            if (_pollingTimer == null)
            {
                _pollingTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(PollingIntervalMs)
                };
                _pollingTimer.Tick += async (s, e) => await PollInputsAsync();
            }
            _pollingTimer.Start();
            
            _isRunning = true;
        }

        /// <summary>
        /// 清理所有绑定和驱动（停止测试时调用）
        /// </summary>
        public void ClearAll()
        {
            lock (_lock)
            {
                // 清理变量绑定
                _variableBindings.Clear();
                _variableValues.Clear();

                // 清理驱动注册（驱动实例会被 DriverFactory 缓存，这里只清除引用）
                _drivers.Clear();
                
                Debug.WriteLine("[HardwareControlService] 已清理所有绑定和驱动引用");
            }
        }

        /// <summary>
        /// 轮询输入变量
        /// 根据信号/通道类型执行不同的数据获取策略
        /// </summary>
        private async Task PollInputsAsync()
        {
            List<VariableBinding> inputBindings;

            lock (_lock)
            {
                inputBindings = _variableBindings.Values
                    .Where(b => !b.IsOutput)
                    .ToList();
            }

            foreach (var binding in inputBindings)
            {
                try
                {
                    IDeviceDriver driver;
                    lock (_lock)
                    {
                        if (!_drivers.TryGetValue(binding.DeviceId, out driver))
                            continue;
                    }

                    // 根据通道类型执行不同的数据获取策略
                    double value = await GetChannelValueByStrategy(binding, driver);

                    double oldValue;
                    lock (_lock)
                    {
                        oldValue = _variableValues.ContainsKey(binding.VariablePath)
                            ? _variableValues[binding.VariablePath]
                            : 0;
                        _variableValues[binding.VariablePath] = value;
                    }

                    // 如果值发生变化，触发事件
                    if (Math.Abs(value - oldValue) > 0.001)
                    {
                        OnVariableValueChanged(binding.VariablePath, value);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[HardwareControlService] 轮询变量 {binding.VariablePath} 异常: {ex.Message}");
                }
            }
        }

        #region 数据获取策略

        /// <summary>
        /// 根据通道类型执行不同的数据获取策略
        /// 注意：AI通道的数据是ViewModel处理数据块后缓存的，不需要额外滤波
        /// </summary>
        /// <param name="binding">变量绑定信息</param>
        /// <param name="driver">设备驱动</param>
        /// <returns>获取到的数值</returns>
        private async Task<double> GetChannelValueByStrategy(VariableBinding binding, IDeviceDriver driver)
        {
            // 从通道ID推断通道类型
            string channelPrefix = GetChannelPrefix(binding.ChannelId);

            switch (channelPrefix.ToUpper())
            {
                case "AI":
                    // 模拟量输入通道：数据来自ViewModel处理后的数据块，直接返回当前值
                    return await GetAnalogInputValue(binding, driver);

                case "DI":
                    // 数字量输入通道：直接读取
                    return await GetDigitalInputValue(binding, driver);

                case "RO":
                    // 电阻输出通道：可能需要角度计算
                    return await GetResolverValue(binding, driver);

                case "CAN":
                case "ARINC429":
                case "1553B":
                    // 通讯通道：需要协议解析
                    return await GetCommunicationValue(binding, driver);

                default:
                    // 默认策略：直接读取
                    return await GetDirectReadValue(binding, driver);
            }
        }

        /// <summary>
        /// 从通道ID提取前缀（如"AI0" -> "AI"）
        /// </summary>
        private string GetChannelPrefix(string channelId)
        {
            if (string.IsNullOrEmpty(channelId))
                return string.Empty;

            return new string(channelId.TakeWhile(c => !char.IsDigit(c)).ToArray());
        }

        /// <summary>
        /// 获取模拟量输入通道值
        /// 注意：模拟量采集返回的是数组数据块，ReadChannelAsync()返回的是ViewModel处理后的当前值
        /// 不需要在HardwareControlService层面再次滤波
        /// </summary>
        private async Task<double> GetAnalogInputValue(VariableBinding binding, IDeviceDriver driver)
        {
            // 对于AI通道，直接返回驱动的当前值
            // 真正的滤波处理在ViewModel层面处理数据块时完成
            double currentValue = await driver.ReadChannelAsync(binding.ChannelId);

            Debug.WriteLine($"[HardwareControlService] AI通道 {binding.ChannelId} 当前值: {currentValue:F6}");
            return currentValue;
        }


        /// <summary>
        /// 获取数字量输入通道值（直接读取）
        /// </summary>
        private async Task<double> GetDigitalInputValue(VariableBinding binding, IDeviceDriver driver)
        {
            double value = await driver.ReadChannelAsync(binding.ChannelId);
            Debug.WriteLine($"[HardwareControlService] DI通道 {binding.ChannelId} 值: {value}");
            return value;
        }

        /// <summary>
        /// 获取电阻输出通道值
        /// </summary>
        private async Task<double> GetResolverValue(VariableBinding binding, IDeviceDriver driver)
        {
            // 读取原始角度值（
            double angleValue = await driver.ReadChannelAsync(binding.ChannelId);

            // 确保角度在0-360度范围内
            while (angleValue < 0) angleValue += 360;
            while (angleValue >= 360) angleValue -= 360;

            Debug.WriteLine($"[HardwareControlService] RO通道 {binding.ChannelId} 角度值: {angleValue:F2}°");
            return angleValue;
        }

        /// <summary>
        /// 获取通讯通道值（协议解析）
        /// </summary>
        private async Task<double> GetCommunicationValue(VariableBinding binding, IDeviceDriver driver)
        {
            try
            {
                // 尝试从通讯数据中提取数值
                // 这里可以根据通讯协议（CAN、ARINC429、1553B）解析数据帧
                double value = await driver.ReadChannelAsync(binding.ChannelId);

                Debug.WriteLine($"[HardwareControlService] 通讯通道 {binding.ChannelId} 解析值: {value}");

                // TODO: 根据具体协议实现数据帧解析
                // 例如：从CAN帧的特定字节提取数值
                // 或者从ARINC429标签数据中提取参数

                return value;
            }
            catch (Exception ex)
            {
                // 如果通讯通道不支持直接读取或解析失败
                Debug.WriteLine($"[HardwareControlService] 通讯通道 {binding.ChannelId} 解析失败: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// 获取直接读取通道值（默认策略）
        /// </summary>
        private async Task<double> GetDirectReadValue(VariableBinding binding, IDeviceDriver driver)
        {
            double value = await driver.ReadChannelAsync(binding.ChannelId);
            Debug.WriteLine($"[HardwareControlService] 直接读取通道 {binding.ChannelId} 值: {value}");
            return value;
        }

        #endregion

        /// <summary>
        /// 触发变量值变化事件
        /// </summary>
        private void OnVariableValueChanged(string variablePath, double value)
        {
            VariableValueChanged?.Invoke(this, new VariableValueChangedEventArgs(variablePath, value));
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            StopAsync().Wait();
            
            lock (_lock)
            {
                _drivers.Clear();
                _variableBindings.Clear();
                _variableValues.Clear();
            }
        }

        #endregion

        #region 辅助类

        /// <summary>
        /// 变量绑定信息
        /// </summary>
        public class VariableBinding
        {
            public string VariablePath { get; set; }
            public string DeviceId { get; set; }
            public string ChannelId { get; set; }
            public bool IsOutput { get; set; }
        }

        #endregion
    }

    /// <summary>
    /// 变量值变化事件参数
    /// </summary>
    public class VariableValueChangedEventArgs : EventArgs
    {
        public string VariablePath { get; }
        public double Value { get; }

        public VariableValueChangedEventArgs(string variablePath, double value)
        {
            VariablePath = variablePath;
            Value = value;
        }
    }
}
