using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MeasureControl.Models;
using MeasureControl.Models.Devices;
using MeasureControl.Helpers;
using static MeasureControl.Helpers.HZ1394Interface;

namespace MeasureControl.Drivers
{
    /// <summary>
    /// 怀智 HZ-MIL1394B-PXIe-4N 驱动
    /// 支持 4 通道 MIL-STD-1394B 总线通信
    /// </summary>
    public class HZ1394BDriver : IDeviceDriver
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
        private bool _isConnected;
        private bool _isAcquisitionRunning;
        private IntPtr _cardHandle = IntPtr.Zero;
        private uint _cardNumber = 0;
        private uint _nodeNumber = 0;
        private string _nodeType = "CC"; // CC: 总线控制器, RN: 远程节点, BM: 总线监视器

        // 通道配置：通道ID -> 配置信息
        private readonly Dictionary<string, ChannelConfig> _channelConfigs = new Dictionary<string, ChannelConfig>();

        // 实时数据：通道ID -> 当前值
        private readonly Dictionary<string, double> _channelValues = new Dictionary<string, double>();
        private readonly object _dataLock = new object();

        // 接收线程
        private IntPtr _receiveThreadHandle = IntPtr.Zero;
        private CancellationTokenSource _acquisitionCancellationTokenSource;

        #endregion

        #region 属性

        public string DeviceId => _device?.Id ?? string.Empty;

        public string DeviceName => _device?.Name ?? "HZ1394B";

        public bool IsConnected => _isConnected;

        public bool IsSimulated => false;

        /// <summary>
        /// HZ1394B是通信设备
        /// </summary>
        public DeviceCapability Capability => DeviceCapability.Communication;

        /// <summary>
        /// 卡号
        /// </summary>
        public uint CardNumber => _cardNumber;

        /// <summary>
        /// 节点号
        /// </summary>
        public uint NodeNumber => _nodeNumber;

        /// <summary>
        /// 节点类型
        /// </summary>
        public string NodeType => _nodeType;

        #endregion

        #region 构造函数

        public HZ1394BDriver(DeviceBase device)
        {
            _device = device ?? throw new ArgumentNullException(nameof(device));
            _isConnected = false;
            _isAcquisitionRunning = false;

            // 从设备配置中提取卡号和节点号
            ExtractDeviceInfo();
            
            // 初始化通道配置（4通道）
            InitializeChannels();
        }

        private void ExtractDeviceInfo()
        {
            // 尝试从设备名称或配置中提取卡号和节点号
            if (_device != null)
            {
                // 默认值
                _cardNumber = 0;
                _nodeNumber = 0;
                _nodeType = "BM"; // 默认总线监视器（与1394B板卡例程一致）

                // 如果设备是Mil1394BDevice类型，可以获取更多信息
                if (_device is Mil1394BDevice mil1394Device)
                {
                    // 可以从设备属性中获取配置信息
                    // 这里可以根据实际需求扩展
                }

                // 尝试从SlotPosition或其他属性中解析
                if (!string.IsNullOrEmpty(_device.SlotPosition))
                {
                    // 解析插槽位置，例如 "Slot3" -> 卡号3
                    var slotStr = _device.SlotPosition.Replace("Slot", "").Trim();
                    if (uint.TryParse(slotStr, out uint slotNum))
                    {
                        _cardNumber = slotNum;
                    }
                }
            }
        }

        private void InitializeChannels()
        {
            // 初始化4个通道（Port1-Port4）
            for (int i = 1; i <= 4; i++)
            {
                string channelId = $"Port{i}";
                _channelConfigs[channelId] = new ChannelConfig
                {
                    ChannelId = channelId,
                    PhysicalChannel = $"Port{i}",
                    IsEnabled = false,
                    Range = "N/A",
                    MinValue = 0.0,
                    MaxValue = 1.0
                };
                _channelValues[channelId] = 0.0;
            }
        }

        #endregion

        #region IDeviceDriver 实现

        public async Task<bool> ConnectAsync()
        {
            try
            {
                Debug.WriteLine($"[HZ1394BDriver] 连接设备 {DeviceName}, 初始CardNumber={_cardNumber}, NodeNumber={_nodeNumber}");

                // 检查设备是否存在，并从扫描结果中获取正确的卡号
                PCI_DEV_FOUND devInfo = new PCI_DEV_FOUND
                {
                    DevNum = 0,
                    DevType = new uint[32],
                    DevNodeNum = new uint[32],
                    DevSN = new uint[32]
                };

                int result = Mil1394_Found(ref devInfo);
                if (result < 0 || devInfo.DevNum == 0)
                {
                    Debug.WriteLine($"[HZ1394BDriver] 未找到1394B设备");
                    return false;
                }

                // 从扫描结果中获取正确的卡号（设备索引，从0开始）
                // 1394B板卡的卡号是扫描结果中的设备索引，不是PXI插槽号
                uint actualCardNumber = 0; // 默认使用第一个设备
                
                // 如果有多个设备，可以根据设备ID或其他信息匹配
                // 目前单个板卡场景，使用第一个设备（索引0）
                if (devInfo.DevNum > 0)
                {
                    actualCardNumber = 0; // 使用第一个设备
                    Debug.WriteLine($"[HZ1394BDriver] 检测到 {devInfo.DevNum} 个设备，使用设备索引 {actualCardNumber}");
                    
                    // 检查节点数量
                    if (devInfo.DevNodeNum[actualCardNumber] == 0)
                    {
                        Debug.WriteLine($"[HZ1394BDriver] 设备 {actualCardNumber} 没有可用节点");
                        return false;
                    }
                }
                else
                {
                    Debug.WriteLine($"[HZ1394BDriver] 未检测到设备");
                    return false;
                }

                // 更新卡号
                _cardNumber = actualCardNumber;
                _nodeNumber = 0; // 默认使用第一个节点

                Debug.WriteLine($"[HZ1394BDriver] 使用卡号={_cardNumber}, 节点号={_nodeNumber}, 节点类型={_nodeType}");

                // 打开设备节点
                if (_nodeType == "CC" || _nodeType == "BM")
                {
                    _cardHandle = Mil1394_CC_OPEN(_cardNumber, _nodeNumber);
                }
                else if (_nodeType == "RN")
                {
                    _cardHandle = Mil1394_RN_OPEN(_cardNumber, _nodeNumber);
                }
                else
                {
                    Debug.WriteLine($"[HZ1394BDriver] 不支持的节点类型: {_nodeType}");
                    return false;
                }

                if (_cardHandle == IntPtr.Zero)
                {
                    Debug.WriteLine($"[HZ1394BDriver] 打开设备失败，CardNumber={_cardNumber}, NodeNumber={_nodeNumber}");
                    return false;
                }

                _isConnected = true;
                Debug.WriteLine($"[HZ1394BDriver] 设备连接成功, CardHandle={_cardHandle}, CardNumber={_cardNumber}, NodeNumber={_nodeNumber}");
                
                await Task.Delay(50); // 等待设备初始化
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HZ1394BDriver] 连接失败: {ex.Message}");
                Debug.WriteLine($"[HZ1394BDriver] 异常堆栈: {ex.StackTrace}");
                _isConnected = false;
                return false;
            }
        }

        public async Task<bool> DisconnectAsync()
        {
            try
            {
                Debug.WriteLine($"[HZ1394BDriver] 断开设备 {DeviceName}");

                // 如果正在采集，先停止
                if (_isAcquisitionRunning)
                {
                    await StopAcquisitionAsync();
                }

                // 确保接收链路已停止
                if (_cardHandle != IntPtr.Zero)
                {
                    Mil1394_CC_MSG_ASYNC_RECV_Stop(_cardHandle);
                    StopRecvThd(_cardHandle);
                }

                if (_receiveThreadHandle != IntPtr.Zero)
                {
                    _receiveThreadHandle = IntPtr.Zero;
                }

                // 关闭设备
                if (_cardHandle != IntPtr.Zero)
                {
                    Mil1394_CC_Close(_cardHandle);
                    _cardHandle = IntPtr.Zero;
                }

                _isConnected = false;
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HZ1394BDriver] 断开失败: {ex.Message}");
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
            // 1394B是通信总线，写入操作需要通过消息发送实现
            // 这里提供基本框架，具体实现需要根据消息格式
            lock (_dataLock)
            {
                if (_channelValues.ContainsKey(channelId))
                {
                    _channelValues[channelId] = value;
                    return Task.FromResult(true);
                }
            }
            return Task.FromResult(false);
        }

        public Task<bool> WriteChannelsBatchAsync(Dictionary<string, double> channelValues)
        {
            lock (_dataLock)
            {
                foreach (var kvp in channelValues)
                {
                    if (_channelValues.ContainsKey(kvp.Key))
                    {
                        _channelValues[kvp.Key] = kvp.Value;
                    }
                }
            }
            return Task.FromResult(true);
        }

        public Task<bool> ConfigureChannelAsync(string channelId, Dictionary<string, object> config)
        {
            if (!_channelConfigs.ContainsKey(channelId))
                return Task.FromResult(false);

            var channelConfig = _channelConfigs[channelId];

            // 更新配置
            if (config.ContainsKey("IsEnabled") && config["IsEnabled"] is bool enabled)
            {
                channelConfig.IsEnabled = enabled;
            }

            if (config.ContainsKey("NodeType") && config["NodeType"] is string nodeType)
            {
                _nodeType = nodeType;
            }

            if (config.ContainsKey("Speed") && config["Speed"] is string speed)
            {
                // 设置速度：100M, 200M, 400M
                if (_cardHandle != IntPtr.Zero)
                {
                    uint speedSel = speed switch
                    {
                        "100M" => 0,
                        "200M" => 1,
                        "400M" => 2,
                        _ => 0
                    };
                    Mil1394_CC_MSG_Speed_Set(_cardHandle, speedSel);
                }
            }

            return Task.FromResult(true);
        }

        public Task<bool> StartAcquisitionAsync()
        {
            if (!_isConnected)
            {
                Debug.WriteLine("[HZ1394BDriver] 设备未连接，无法启动采集");
                return Task.FromResult(false);
            }

            if (_isAcquisitionRunning)
            {
                Debug.WriteLine("[HZ1394BDriver] 采集已在运行");
                return Task.FromResult(true);
            }

            try
            {
                if (_cardHandle == IntPtr.Zero)
                {
                    Debug.WriteLine("[HZ1394BDriver] 设备句柄无效");
                    return Task.FromResult(false);
                }

                // 启动接收线程
                _receiveThreadHandle = StartRecvThd(_cardHandle);
                if (_receiveThreadHandle == IntPtr.Zero)
                {
                    Debug.WriteLine("[HZ1394BDriver] 启动接收线程失败");
                    return Task.FromResult(false);
                }

                // 启动异步接收
                int result = Mil1394_CC_MSG_ASYNC_RECV_Start(_cardHandle);
                if (result < 0)
                {
                    Debug.WriteLine($"[HZ1394BDriver] 启动异步接收失败: {result}");
                    StopRecvThd(_cardHandle);
                    _receiveThreadHandle = IntPtr.Zero;
                    return Task.FromResult(false);
                }

                _acquisitionCancellationTokenSource = new CancellationTokenSource();
                _isAcquisitionRunning = true;

                Debug.WriteLine("[HZ1394BDriver] 采集已启动");
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HZ1394BDriver] 启动采集失败: {ex.Message}");
                _isAcquisitionRunning = false;
                return Task.FromResult(false);
            }
        }

        public Task<bool> StopAcquisitionAsync()
        {
            if (!_isAcquisitionRunning)
                return Task.FromResult(true);

            try
            {
                // 停止异步接收
                if (_cardHandle != IntPtr.Zero)
                {
                    Mil1394_CC_MSG_ASYNC_RECV_Stop(_cardHandle);
                }

                // 停止接收线程
                if (_cardHandle != IntPtr.Zero)
                {
                    StopRecvThd(_cardHandle);
                }

                if (_receiveThreadHandle != IntPtr.Zero)
                {
                    _receiveThreadHandle = IntPtr.Zero;
                }

                // 取消后台任务
                _acquisitionCancellationTokenSource?.Cancel();
                _acquisitionCancellationTokenSource?.Dispose();
                _acquisitionCancellationTokenSource = null;

                _isAcquisitionRunning = false;
                Debug.WriteLine("[HZ1394BDriver] 采集已停止");
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HZ1394BDriver] 停止采集失败: {ex.Message}");
                return Task.FromResult(false);
            }
        }

        public Task<Dictionary<string, object>> GetStatusAsync()
        {
            var status = new Dictionary<string, object>
            {
                { "IsConnected", _isConnected },
                { "IsAcquisitionRunning", _isAcquisitionRunning },
                { "CardNumber", _cardNumber },
                { "NodeNumber", _nodeNumber },
                { "NodeType", _nodeType },
                { "EnabledChannels", _channelConfigs.Values.Count(c => c.IsEnabled) }
            };

            return Task.FromResult(status);
        }

        public async Task<bool> ResetAsync()
        {
            await StopAcquisitionAsync();
            await DisconnectAsync();
            
            // 复位设备
            if (_cardHandle != IntPtr.Zero)
            {
                Mil1394_CC_RESET(_cardHandle);
            }
            
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
        /// 设置节点类型
        /// </summary>
        public void SetNodeType(string nodeType)
        {
            _nodeType = nodeType;
        }

        /// <summary>
        /// 设置卡号和节点号
        /// </summary>
        public void SetCardAndNode(uint cardNumber, uint nodeNumber)
        {
            _cardNumber = cardNumber;
            _nodeNumber = nodeNumber;
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
