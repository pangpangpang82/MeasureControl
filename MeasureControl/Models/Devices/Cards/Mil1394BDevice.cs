using System;
using System.Collections.ObjectModel;
using System.Linq;
using MeasureControl.Models.Devices.DeviceCategories;
using MeasureControl.Models.Channels;
using MeasureControl.Constants;

namespace MeasureControl.Models.Devices
{
    /// <summary>
    /// MIL-1394B 设备角色
    /// </summary>
    public enum Mil1394BDeviceRole
    {
        BusController,      // 总线控制器
        RemoteNode,         // 远程节点
        Monitor             // 监视器
    }

    /// <summary>
    /// MIL-1394B 传输模式
    /// </summary>
    public enum Mil1394BTransferMode
    {
        PeerToPeer,         // 点对点传输
        Isochronous,        // 等时传输
        Asynchronous        // 异步传输
    }

    /// <summary>
    /// MIL-1394B 时钟源
    /// </summary>
    public enum Mil1394BClockSource
    {
        Internal,           // 内部时钟
        PXIe_CLK100,        // PXI 时钟 (100 MHz)
        External            // 外部触发
    }

    /// <summary>
    /// MIL-1394B 拓扑结构
    /// </summary>
    public enum Mil1394BTopology
    {
        DaisyChain,         // 菊花链
        Star                // 星形
    }

    /// <summary>
    /// MIL-1394B 传输介质
    /// </summary>
    public enum Mil1394BMediaType
    {
        Copper,             // 双绞线（铜缆）
        Fiber               // 光纤
    }

    /// <summary>
    /// MIL-1394B 速率模式
    /// </summary>
    public enum Mil1394BSpeedMode
    {
        S100,               // 100 Mbps
        S200,               // 200 Mbps
        S400,               // 400 Mbps
        S800                // 800 Mbps
    }

    /// <summary>
    /// IEEE 1394B设备类（如：怀智HZ-MIL1394B-PXIe-4N）
    /// </summary>
    public class Mil1394BDevice : PxiDeviceBase
    {
        private int _nodeCount;
        private int _maxSpeed;
        private string _protocol;
        private bool _supportsBeta;
        private string _cableType;

        // ========== 基本规格参数组 ==========
        private string _productName;
        private string _protocolStandard;
        private int _physicalLayerSpeed;
        private int _portCount;

        // ========== 传输功能参数组 ==========
        private Mil1394BTransferMode _transferMode;
        private Mil1394BDeviceRole _deviceRole;
        private Mil1394BTopology _supportedTopology;
        private Mil1394BMediaType _mediaType;
        private bool _hotPlugSupport;

        // ========== 同步功能参数组 ==========
        private Mil1394BClockSource _clockSource;
        private bool _syncWithPXIeClock;
        private bool _triggerSupport;
        private bool _multiCardSync;

        // ========== 通道隔离参数组 ==========
        private bool _channelIsolation;
        private string _isolationVoltage;

        // ========== 板载资源参数组 ==========
        private int _onboardMemorySize;
        private bool _dmaSupport;
        private bool _interruptSupport;

        /// <summary>
        /// 1394B节点数
        /// </summary>
        public int NodeCount
        {
            get => _nodeCount;
            set => SetProperty(ref _nodeCount, value);
        }

        /// <summary>
        /// 最大速度 (Mbps)
        /// </summary>
        public int MaxSpeed
        {
            get => _maxSpeed;
            set => SetProperty(ref _maxSpeed, value);
        }

        /// <summary>
        /// 协议标准（S100/S200/S400/S800）
        /// </summary>
        public string Protocol
        {
            get => _protocol;
            set => SetProperty(ref _protocol, value);
        }

        /// <summary>
        /// 支持Beta模式
        /// </summary>
        public bool SupportsBeta
        {
            get => _supportsBeta;
            set => SetProperty(ref _supportsBeta, value);
        }

        /// <summary>
        /// 线缆类型（Copper/Fiber）
        /// </summary>
        public string CableType
        {
            get => _cableType;
            set => SetProperty(ref _cableType, value);
        }


        // ========== 基本规格参数属性 ==========
        
        /// <summary>
        /// 产品名称
        /// </summary>
        public string ProductName
        {
            get => _productName;
            set => SetProperty(ref _productName, value);
        }

        /// <summary>
        /// 协议标准
        /// </summary>
        public string ProtocolStandard
        {
            get => _protocolStandard;
            set => SetProperty(ref _protocolStandard, value);
        }

        /// <summary>
        /// 物理层速率 (Mbps)
        /// </summary>
        public int PhysicalLayerSpeed
        {
            get => _physicalLayerSpeed;
            set => SetProperty(ref _physicalLayerSpeed, value);
        }

        /// <summary>
        /// 端口数
        /// </summary>
        public int PortCount
        {
            get => _portCount;
            set => SetProperty(ref _portCount, value);
        }

        // ========== 传输功能参数属性 ==========

        /// <summary>
        /// 传输模式
        /// </summary>
        public Mil1394BTransferMode TransferMode
        {
            get => _transferMode;
            set => SetProperty(ref _transferMode, value);
        }

        /// <summary>
        /// 设备角色
        /// </summary>
        public Mil1394BDeviceRole DeviceRole
        {
            get => _deviceRole;
            set => SetProperty(ref _deviceRole, value);
        }

        /// <summary>
        /// 支持的拓扑结构
        /// </summary>
        public Mil1394BTopology SupportedTopology
        {
            get => _supportedTopology;
            set => SetProperty(ref _supportedTopology, value);
        }

        /// <summary>
        /// 传输介质类型
        /// </summary>
        public Mil1394BMediaType MediaType
        {
            get => _mediaType;
            set => SetProperty(ref _mediaType, value);
        }

        /// <summary>
        /// 热插拔支持
        /// </summary>
        public bool HotPlugSupport
        {
            get => _hotPlugSupport;
            set => SetProperty(ref _hotPlugSupport, value);
        }

        // ========== 同步功能参数属性 ==========

        /// <summary>
        /// 时钟源选择
        /// </summary>
        public new Mil1394BClockSource ClockSource
        {
            get => _clockSource;
            set => SetProperty(ref _clockSource, value);
        }

        /// <summary>
        /// PXIe_CLK100 同步
        /// </summary>
        public bool SyncWithPXIeClock
        {
            get => _syncWithPXIeClock;
            set => SetProperty(ref _syncWithPXIeClock, value);
        }

        /// <summary>
        /// TRIG 触发支持
        /// </summary>
        public bool TriggerSupport
        {
            get => _triggerSupport;
            set => SetProperty(ref _triggerSupport, value);
        }

        /// <summary>
        /// 多卡同步支持
        /// </summary>
        public bool MultiCardSync
        {
            get => _multiCardSync;
            set => SetProperty(ref _multiCardSync, value);
        }

        // ========== 通道隔离参数属性 ==========

        /// <summary>
        /// 通道电气隔离
        /// </summary>
        public bool ChannelIsolation
        {
            get => _channelIsolation;
            set => SetProperty(ref _channelIsolation, value);
        }

        /// <summary>
        /// 隔离电压
        /// </summary>
        public string IsolationVoltage
        {
            get => _isolationVoltage;
            set => SetProperty(ref _isolationVoltage, value);
        }

        // ========== 板载资源参数属性 ==========

        /// <summary>
        /// 板载内存大小 (MB)
        /// </summary>
        public int OnboardMemorySize
        {
            get => _onboardMemorySize;
            set => SetProperty(ref _onboardMemorySize, value);
        }

        /// <summary>
        /// DMA 支持
        /// </summary>
        public bool DmaSupport
        {
            get => _dmaSupport;
            set => SetProperty(ref _dmaSupport, value);
        }

        /// <summary>
        /// 中断支持
        /// </summary>
        public bool InterruptSupport
        {
            get => _interruptSupport;
            set => SetProperty(ref _interruptSupport, value);
        }

        public override string DeviceTypeName => "1394B";

        public Mil1394BDevice() : base()
        {
            DeviceType = DeviceConstants.Type.Card;
            ParentNode = "1394B";
            NodeCount = 4;
            MaxSpeed = 800;
            Protocol = "S800";
            SupportsBeta = true;
            CableType = "Copper";
            InitializeChildren();
        }

        public Mil1394BDevice(string name, string slotPosition) : base()
        {
            DeviceType = DeviceConstants.Type.Card;
            ParentNode = "1394B";
            Model = "MIL-1394B";
            ParseDeviceName(name);
            SlotPosition = slotPosition;
            
            // 根据型号配置设备
            ConfigureByModel(name);

            InitializeChildren();
        }

        /// <summary>
        /// 根据型号配置设备参数
        /// </summary>
        private void ConfigureByModel(string deviceName)
        {
            if (string.IsNullOrEmpty(deviceName))
            {
                ConfigureAsHZMIL1394B();
                return;
            }

            string modelLower = deviceName.ToLower();

            // 识别 HZ-MIL1394B-PXIe-4N 型号
            if (modelLower.Contains("mil1394b") || modelLower.Contains("mil-1394b") || modelLower.Contains("1394b"))
            {
                ConfigureAsHZMIL1394B();
            }
            // 预留其他型号识别
            else
            {
                // 默认配置
                ConfigureAsHZMIL1394B();
            }
        }

        /// <summary>
        /// 配置为 HZ-MIL1394B-PXIe-4N 型号
        /// </summary>
        private void ConfigureAsHZMIL1394B()
        {
            // 基本参数
            ProductName = "HZ-MIL1394B-PXIe-4N";
            NodeCount = 4;
            MaxSpeed = 800;
            Protocol = "S800";
            SupportsBeta = true;
            CableType = "Copper";

            // 基本规格参数
            ProtocolStandard = "IEEE-1394B / MIL-STD-1394B";
            PhysicalLayerSpeed = 800;  // 800 Mbps
            PortCount = 4;

            // 传输功能参数
            TransferMode = Mil1394BTransferMode.Asynchronous;  // 默认异步传输
            DeviceRole = Mil1394BDeviceRole.RemoteNode;  // 默认远程节点
            SupportedTopology = Mil1394BTopology.DaisyChain;  // 支持菊花链
            MediaType = Mil1394BMediaType.Copper;  // 双绞线
            HotPlugSupport = true;  // 支持热插拔

            // 同步功能参数
            ClockSource = Mil1394BClockSource.PXIe_CLK100;  // 默认使用 PXI 时钟
            SyncWithPXIeClock = true;  // 支持 PXIe_CLK100 同步
            TriggerSupport = true;  // 支持 TRIG 触发
            MultiCardSync = true;  // 支持多卡同步

            // 通道隔离参数
            ChannelIsolation = true;  // 通道电气隔离
            IsolationVoltage = "1000V";  // 隔离电压

            // 板载资源参数
            OnboardMemorySize = 512;  // 512 MB
            DmaSupport = true;  // 支持 DMA
            InterruptSupport = true;  // 支持中断

        }

        public override void InitializeChildren()
        {
            Children.Clear();

            // 创建 4 个独立节点（不再创建Channel子节点）
            for (int i = 0; i < NodeCount; i++)
            {
                Children.Add(new Mil1394BNode
                {
                    Name = $"节点{i}",
                    ParentNode = "1394B",
                    Model = "1394B节点",
                    NodeNumber = i,
                    DeviceRole = Mil1394BDeviceRole.RemoteNode,
                    TransferMode = Mil1394BTransferMode.Asynchronous,
                    SpeedMode = Mil1394BSpeedMode.S800,
                    MediaType = MediaType,
                    NodeEnabled = true,
                    ConnectionStatus = "未连接",
                    DataRate = 0,
                    ErrorCount = 0,
                    SlotPosition = $"CH0-CH63",
                    Status = Constants.DeviceConstants.Status.Normal
                });
            }
        }

        public override ObservableCollection<DeviceInfoItem> GetDeviceInfoItems()
        {
            var items = new ObservableCollection<DeviceInfoItem>();
            var mainDeviceInfo = DeviceInfoItem.FromDevice(this, false);
            if (mainDeviceInfo != null)
            {
                items.Add(mainDeviceInfo);
            }

            // 添加所有Node子节点信息（不包含Channel）
            foreach (var node in Children.OfType<Mil1394BNode>())
            {
                var nodeInfo = DeviceInfoItem.FromDevice(node, true);
                if (nodeInfo != null)
                {
                    items.Add(nodeInfo);
                }
            }

            return items;
        }

        private new void ParseDeviceName(string deviceName)
        {
            if (string.IsNullOrEmpty(deviceName))
            {
                Name = "N/A";
                Manufacturer = "N/A";
                Model = "N/A";
                return;
            }

            var parts = deviceName.Split(' ');
            if (parts.Length >= 2)
            {
                Manufacturer = parts[0];
                Model = string.Join(" ", parts.Skip(1));
                Name = deviceName;
            }
            else
            {
                Name = deviceName;
                Manufacturer = "N/A";
                Model = "N/A";
            }
        }

        public override string GetConnectionString()
        {
            return $"1394B::{Manufacturer}::{Model}::{SlotPosition}";
        }

        
        /// <summary>
        /// 初始化设备的通道集合（用于通信配置，不作为设备树节点）
        /// </summary>
        public override void InitializeChannels()
        {
            base.InitializeChannels(); // 清空通道集合
            
            const int channelsPerNode = 64;  // 每个节点64个逻辑通道
            
            // 为每个节点创建逻辑通道（仅用于通信配置）
            foreach (var node in Children.OfType<Mil1394BNode>())
            {
                for (int i = 0; i < channelsPerNode; i++)
                {
                    var channel = new Mil1394BChannel
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        Name = $"节点{node.NodeNumber}-Ch{i + 1}",
                        DeviceId = Id,
                        DeviceName = Name,
                        Description = $"1394B节点{node.NodeNumber}通道{i + 1}",
                        ChannelType = "1394B",
                        NodeNumber = node.NodeNumber,
                        ChannelNumber = i + 1,
                        DeviceRole = node.DeviceRole,
                        TransferMode = node.TransferMode,
                        SpeedMode = node.SpeedMode,
                        MediaType = node.MediaType
                    };
                    Channels.Add(channel);
                }
            }
        }

        public override bool ValidateConfiguration()
        {
            return base.ValidateConfiguration() &&
                   NodeCount > 0 &&
                   MaxSpeed > 0 &&
                   PortCount > 0;
        }

        #region 枚举转换辅助方法

        /// <summary>
        /// 获取传输模式描述
        /// </summary>
        private string GetTransferModeDescription(Mil1394BTransferMode mode)
        {
            switch (mode)
            {
                case Mil1394BTransferMode.PeerToPeer:
                    return "点对点传输 (Peer-to-Peer)";
                case Mil1394BTransferMode.Isochronous:
                    return "等时传输 (Isochronous)";
                case Mil1394BTransferMode.Asynchronous:
                    return "异步传输 (Asynchronous)";
                default:
                    return mode.ToString();
            }
        }

        /// <summary>
        /// 获取设备角色描述
        /// </summary>
        private string GetDeviceRoleDescription(Mil1394BDeviceRole role)
        {
            switch (role)
            {
                case Mil1394BDeviceRole.BusController:
                    return "总线控制器 (Bus Controller)";
                case Mil1394BDeviceRole.RemoteNode:
                    return "远程节点 (Remote Node)";
                case Mil1394BDeviceRole.Monitor:
                    return "监视器 (Monitor)";
                default:
                    return role.ToString();
            }
        }

        /// <summary>
        /// 获取拓扑结构描述
        /// </summary>
        private string GetTopologyDescription(Mil1394BTopology topology)
        {
            switch (topology)
            {
                case Mil1394BTopology.DaisyChain:
                    return "菊花链 (Daisy Chain)";
                case Mil1394BTopology.Star:
                    return "星形 (Star)";
                default:
                    return topology.ToString();
            }
        }

        /// <summary>
        /// 获取介质类型描述
        /// </summary>
        private string GetMediaTypeDescription(Mil1394BMediaType mediaType)
        {
            switch (mediaType)
            {
                case Mil1394BMediaType.Copper:
                    return "双绞线 (Copper)";
                case Mil1394BMediaType.Fiber:
                    return "光纤 (Fiber)";
                default:
                    return mediaType.ToString();
            }
        }

        /// <summary>
        /// 获取时钟源描述
        /// </summary>
        private string GetClockSourceDescription(Mil1394BClockSource clockSource)
        {
            switch (clockSource)
            {
                case Mil1394BClockSource.Internal:
                    return "内部时钟 (Internal)";
                case Mil1394BClockSource.PXIe_CLK100:
                    return "PXIe_CLK100 (100 MHz)";
                case Mil1394BClockSource.External:
                    return "外部触发 (External)";
                default:
                    return clockSource.ToString();
            }
        }

        /// <summary>
        /// 获取速率模式描述
        /// </summary>
        private string GetSpeedModeDescription(Mil1394BSpeedMode speedMode)
        {
            switch (speedMode)
            {
                case Mil1394BSpeedMode.S100:
                    return "S100 (100 Mbps)";
                case Mil1394BSpeedMode.S200:
                    return "S200 (200 Mbps)";
                case Mil1394BSpeedMode.S400:
                    return "S400 (400 Mbps)";
                case Mil1394BSpeedMode.S800:
                    return "S800 (800 Mbps)";
                default:
                    return speedMode.ToString();
            }
        }

        #endregion
    }

    /// <summary>
    /// 1394B节点类（统一使用单一Node抽象）
    /// </summary>
    public class Mil1394BNode : DeviceBase
    {
        private int _nodeNumber;
        private Mil1394BDeviceRole _deviceRole;
        private Mil1394BTransferMode _transferMode;
        private Mil1394BSpeedMode _speedMode;
        private Mil1394BMediaType _mediaType;
        private bool _nodeEnabled;
        private string _connectionStatus;
        private int _dataRate;
        private int _errorCount;

        /// <summary>
        /// 节点编号
        /// </summary>
        public int NodeNumber
        {
            get => _nodeNumber;
            set => SetProperty(ref _nodeNumber, value);
        }

        /// <summary>
        /// 节点角色配置
        /// </summary>
        public Mil1394BDeviceRole DeviceRole
        {
            get => _deviceRole;
            set => SetProperty(ref _deviceRole, value);
        }

        /// <summary>
        /// 节点传输模式
        /// </summary>
        public Mil1394BTransferMode TransferMode
        {
            get => _transferMode;
            set => SetProperty(ref _transferMode, value);
        }

        /// <summary>
        /// 节点速率设置
        /// </summary>
        public Mil1394BSpeedMode SpeedMode
        {
            get => _speedMode;
            set => SetProperty(ref _speedMode, value);
        }

        /// <summary>
        /// 节点介质类型
        /// </summary>
        public Mil1394BMediaType MediaType
        {
            get => _mediaType;
            set => SetProperty(ref _mediaType, value);
        }

        /// <summary>
        /// 节点启用状态
        /// </summary>
        public bool NodeEnabled
        {
            get => _nodeEnabled;
            set => SetProperty(ref _nodeEnabled, value);
        }

        /// <summary>
        /// 连接状态
        /// </summary>
        public string ConnectionStatus
        {
            get => _connectionStatus;
            set => SetProperty(ref _connectionStatus, value);
        }

        /// <summary>
        /// 当前数据速率 (Mbps)
        /// </summary>
        public int DataRate
        {
            get => _dataRate;
            set => SetProperty(ref _dataRate, value);
        }

        /// <summary>
        /// 错误计数
        /// </summary>
        public int ErrorCount
        {
            get => _errorCount;
            set => SetProperty(ref _errorCount, value);
        }

        public override string DeviceTypeName => "1394B Node";

        public Mil1394BNode()
        {
            DeviceType = Constants.DeviceConstants.Type.SubNode;
            ParentNode = "1394B";
            NodeNumber = 1;
            DeviceRole = Mil1394BDeviceRole.RemoteNode;
            TransferMode = Mil1394BTransferMode.Asynchronous;
            SpeedMode = Mil1394BSpeedMode.S800;
            MediaType = Mil1394BMediaType.Copper;
            NodeEnabled = true;
            ConnectionStatus = "未连接";
            DataRate = 0;
            ErrorCount = 0;
            SlotPosition = "N/A";
            Status = Constants.DeviceConstants.Status.Normal;
        }

        public override void InitializeChildren()
        {
            // Node不再有子节点（Channel不作为设备树节点）
            Children.Clear();
        }

        public override ObservableCollection<DeviceInfoItem> GetDeviceInfoItems()
        {
            var items = new ObservableCollection<DeviceInfoItem>();
            items.Add(new DeviceInfoItem(DeviceTypeName, $"节点{NodeNumber}", SlotPosition, Status, true, "Card"));
            return items;
        }

        public override string GetConnectionString()
        {
            return $"1394BNode::Node{NodeNumber}::{GetSpeedModeDescription(SpeedMode)}::{GetDeviceRoleDescription(DeviceRole)}";
        }

        private string GetSpeedModeDescription(Mil1394BSpeedMode mode)
        {
            switch (mode)
            {
                case Mil1394BSpeedMode.S100: return "S100 (100Mbps)";
                case Mil1394BSpeedMode.S200: return "S200 (200Mbps)";
                case Mil1394BSpeedMode.S400: return "S400 (400Mbps)";
                case Mil1394BSpeedMode.S800: return "S800 (800Mbps)";
                default: return mode.ToString();
            }
        }

        private string GetDeviceRoleDescription(Mil1394BDeviceRole role)
        {
            switch (role)
            {
                case Mil1394BDeviceRole.BusController: return "总线控制器";
                case Mil1394BDeviceRole.RemoteNode: return "远程节点";
                case Mil1394BDeviceRole.Monitor: return "监视器";
                default: return role.ToString();
            }
        }
    }
}

