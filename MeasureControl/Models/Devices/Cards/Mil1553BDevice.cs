using System;
using System.Collections.ObjectModel;
using System.Linq;
using MeasureControl.Helpers;
using MeasureControl.Models.Devices.DeviceCategories;
using MeasureControl.Constants;

namespace MeasureControl.Models.Devices
{
    /// <summary>
    /// MIL-STD-1553B 工作模式
    /// </summary>
    public enum Mil1553BWorkMode
    {
        BC,         // 总线控制器
        RT,         // 远程终端
        BM,         // 总线监视器
        FullFunction // 全功能（BC+31RT+BM）
    }

    /// <summary>
    /// MIL-STD-1553B 通信速率
    /// </summary>
    public enum Mil1553BSpeedMode
    {
        Speed1Mbps,  // 1 Mbps
        Speed2Mbps,  // 2 Mbps
        Speed4Mbps   // 4 Mbps
    }

    /// <summary>
    /// MIL-STD-1553B 耦合方式
    /// </summary>
    public enum Mil1553BCouplingType
    {
        Direct25,       // 直接耦合 25Ω
        Transformer179  // 变压器耦合 179Ω
    }

    /// <summary>
    /// MIL-STD-1553B 总线标识
    /// </summary>
    public enum Mil1553BBusType
    {
        BusA,           // BUSA主总线
        BusB,           // BUSB辅助总线
        DualRedundant   // 双冗余
    }

    /// <summary>
    /// MIL-STD-1553B 接插件类型
    /// </summary>
    public enum Mil1553BConnectorType
    {
        DB62    // DB62母座
    }

    /// <summary>
    /// MIL-STD-1553B 发送模式
    /// </summary>
    public enum Mil1553BTransmitMode
    {
        SingleBuffer,   // 单缓冲
        CircularBuffer  // 循环缓冲
    }

    /// <summary>
    /// MIL-STD-1553B总线设备类（如：阿尔泰PXI-4332）
    /// </summary>
    public class Mil1553BDevice : PxiDeviceBase
    {
        private int _busInterfaceCount;
        private Mil1553BWorkMode _workMode;
        private Mil1553BBusType _busType;
        private bool _supportsBcRtMt;

        // ========== 基本参数组 ==========
        private string _productName;
        private string _busTypeDescription;
        private int _maxChannels;
        private Mil1553BSpeedMode _communicationSpeed;
        private string _protocolStandard;
        private Mil1553BWorkMode _workModeType;
        private int _onboardMemorySize;

        // ========== 通道功能参数组 ==========
        private string _channelStructure;
        private Mil1553BCouplingType _couplingType;
        private int _timestampBits;
        private string _timestampResolution;
        private bool _hardwareInterruptSupport;
        private bool _temperatureMonitorSupport;

        // ========== BC功能参数组 ==========
        private bool _messageSchedulingSupport;
        private bool _retryMechanismSupport;
        private bool _dataBufferSupport;

        // ========== RT功能参数组 ==========
        private bool _responseTimeProgrammable;
        private bool _statusWordProgrammable;
        private bool _illegalCommandSupport;
        private Mil1553BTransmitMode _transmitMode;
        private bool _dataProtectionSupport;

        // ========== BM功能参数组 ==========
        private string _monitorRange;
        private bool _filterFunctionSupport;

        // ========== 接口与连接参数组 ==========
        private Mil1553BConnectorType _connectorType;
        private int _pinCount;
        private string _couplingSignalNaming;

        // ========== 板卡外形参数组 ==========
        private string _dimensions;
        private string _connectorTypeDescription;
        private string _ledIndicators;

        // ========== 环境与工作条件参数组 ==========
        private string _operatingTemp;
        private string _operatingHumidity;
        private string _storageTemp;
        private string _storageHumidity;

        // ========== 软件支持参数组 ==========
        private string _supportedOS;

        /// <summary>
        /// 总线接口数量（4套双冗余总线接口）
        /// </summary>
        public int BusInterfaceCount
        {
            get => _busInterfaceCount;
            set => SetProperty(ref _busInterfaceCount, value);
        }

        /// <summary>
        /// 工作模式（BC/RT/BM/FullFunction）
        /// </summary>
        public Mil1553BWorkMode WorkMode
        {
            get => _workMode;
            set => SetProperty(ref _workMode, value);
        }

        /// <summary>
        /// 总线类型（BusA/BusB/DualRedundant）
        /// </summary>
        public new Mil1553BBusType BusType
        {
            get => _busType;
            set => SetProperty(ref _busType, value);
        }

        /// <summary>
        /// 支持BC/RT/BM多模式
        /// </summary>
        public bool SupportsBcRtMt
        {
            get => _supportsBcRtMt;
            set => SetProperty(ref _supportsBcRtMt, value);
        }

        // ========== 基本参数组公共属性 ==========

        /// <summary>
        /// 产品名称
        /// </summary>
        public string ProductName
        {
            get => _productName;
            set => SetProperty(ref _productName, value);
        }

        /// <summary>
        /// 总线类型描述
        /// </summary>
        public string BusTypeDescription
        {
            get => _busTypeDescription;
            set => SetProperty(ref _busTypeDescription, value);
        }

        /// <summary>
        /// 最大通道数
        /// </summary>
        public int MaxChannels
        {
            get => _maxChannels;
            set => SetProperty(ref _maxChannels, value);
        }

        /// <summary>
        /// 通信速率
        /// </summary>
        public Mil1553BSpeedMode CommunicationSpeed
        {
            get => _communicationSpeed;
            set => SetProperty(ref _communicationSpeed, value);
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
        /// 工作模式类型
        /// </summary>
        public Mil1553BWorkMode WorkModeType
        {
            get => _workModeType;
            set => SetProperty(ref _workModeType, value);
        }

        /// <summary>
        /// 板载缓存大小 (MB)
        /// </summary>
        public int OnboardMemorySize
        {
            get => _onboardMemorySize;
            set => SetProperty(ref _onboardMemorySize, value);
        }

        // ========== 通道功能参数组公共属性 ==========

        /// <summary>
        /// 通道结构说明
        /// </summary>
        public string ChannelStructure
        {
            get => _channelStructure;
            set => SetProperty(ref _channelStructure, value);
        }

        /// <summary>
        /// 耦合类型
        /// </summary>
        public Mil1553BCouplingType CouplingType
        {
            get => _couplingType;
            set => SetProperty(ref _couplingType, value);
        }

        /// <summary>
        /// 时间标签位数
        /// </summary>
        public int TimestampBits
        {
            get => _timestampBits;
            set => SetProperty(ref _timestampBits, value);
        }

        /// <summary>
        /// 时间标签精度
        /// </summary>
        public string TimestampResolution
        {
            get => _timestampResolution;
            set => SetProperty(ref _timestampResolution, value);
        }

        /// <summary>
        /// 硬件中断支持
        /// </summary>
        public bool HardwareInterruptSupport
        {
            get => _hardwareInterruptSupport;
            set => SetProperty(ref _hardwareInterruptSupport, value);
        }

        /// <summary>
        /// 温度监控支持
        /// </summary>
        public bool TemperatureMonitorSupport
        {
            get => _temperatureMonitorSupport;
            set => SetProperty(ref _temperatureMonitorSupport, value);
        }

        // ========== BC功能参数组公共属性 ==========

        /// <summary>
        /// 消息调度支持
        /// </summary>
        public bool MessageSchedulingSupport
        {
            get => _messageSchedulingSupport;
            set => SetProperty(ref _messageSchedulingSupport, value);
        }

        /// <summary>
        /// 重试机制支持
        /// </summary>
        public bool RetryMechanismSupport
        {
            get => _retryMechanismSupport;
            set => SetProperty(ref _retryMechanismSupport, value);
        }

        /// <summary>
        /// 数据缓冲支持
        /// </summary>
        public bool DataBufferSupport
        {
            get => _dataBufferSupport;
            set => SetProperty(ref _dataBufferSupport, value);
        }

        // ========== RT功能参数组公共属性 ==========

        /// <summary>
        /// 响应时间可编程
        /// </summary>
        public bool ResponseTimeProgrammable
        {
            get => _responseTimeProgrammable;
            set => SetProperty(ref _responseTimeProgrammable, value);
        }

        /// <summary>
        /// 状态字可编程
        /// </summary>
        public bool StatusWordProgrammable
        {
            get => _statusWordProgrammable;
            set => SetProperty(ref _statusWordProgrammable, value);
        }

        /// <summary>
        /// 非法指令支持
        /// </summary>
        public bool IllegalCommandSupport
        {
            get => _illegalCommandSupport;
            set => SetProperty(ref _illegalCommandSupport, value);
        }

        /// <summary>
        /// 发送模式
        /// </summary>
        public Mil1553BTransmitMode TransmitMode
        {
            get => _transmitMode;
            set => SetProperty(ref _transmitMode, value);
        }

        /// <summary>
        /// 数据保护支持
        /// </summary>
        public bool DataProtectionSupport
        {
            get => _dataProtectionSupport;
            set => SetProperty(ref _dataProtectionSupport, value);
        }

        // ========== BM功能参数组公共属性 ==========

        /// <summary>
        /// 监控范围
        /// </summary>
        public string MonitorRange
        {
            get => _monitorRange;
            set => SetProperty(ref _monitorRange, value);
        }

        /// <summary>
        /// 过滤功能支持
        /// </summary>
        public bool FilterFunctionSupport
        {
            get => _filterFunctionSupport;
            set => SetProperty(ref _filterFunctionSupport, value);
        }

        // ========== 接口与连接参数组公共属性 ==========

        /// <summary>
        /// 接插件类型
        /// </summary>
        public Mil1553BConnectorType ConnectorType
        {
            get => _connectorType;
            set => SetProperty(ref _connectorType, value);
        }

        /// <summary>
        /// 引脚数量
        /// </summary>
        public int PinCount
        {
            get => _pinCount;
            set => SetProperty(ref _pinCount, value);
        }

        /// <summary>
        /// 耦合信号命名规则
        /// </summary>
        public string CouplingSignalNaming
        {
            get => _couplingSignalNaming;
            set => SetProperty(ref _couplingSignalNaming, value);
        }

        // ========== 板卡外形参数组公共属性 ==========

        /// <summary>
        /// 尺寸规格
        /// </summary>
        public string Dimensions
        {
            get => _dimensions;
            set => SetProperty(ref _dimensions, value);
        }

        /// <summary>
        /// 连接器类型描述
        /// </summary>
        public string ConnectorTypeDescription
        {
            get => _connectorTypeDescription;
            set => SetProperty(ref _connectorTypeDescription, value);
        }

        /// <summary>
        /// LED指示灯配置
        /// </summary>
        public string LedIndicators
        {
            get => _ledIndicators;
            set => SetProperty(ref _ledIndicators, value);
        }

        // ========== 环境与工作条件参数组公共属性 ==========

        /// <summary>
        /// 工作温度范围
        /// </summary>
        public string OperatingTemp
        {
            get => _operatingTemp;
            set => SetProperty(ref _operatingTemp, value);
        }

        /// <summary>
        /// 工作湿度范围
        /// </summary>
        public string OperatingHumidity
        {
            get => _operatingHumidity;
            set => SetProperty(ref _operatingHumidity, value);
        }

        /// <summary>
        /// 存储温度范围
        /// </summary>
        public string StorageTemp
        {
            get => _storageTemp;
            set => SetProperty(ref _storageTemp, value);
        }

        /// <summary>
        /// 存储湿度范围
        /// </summary>
        public string StorageHumidity
        {
            get => _storageHumidity;
            set => SetProperty(ref _storageHumidity, value);
        }

        // ========== 软件支持参数组公共属性 ==========

        /// <summary>
        /// 支持的操作系统
        /// </summary>
        public string SupportedOS
        {
            get => _supportedOS;
            set => SetProperty(ref _supportedOS, value);
        }

        public override string DeviceTypeName => "1553B";

        public Mil1553BDevice() : base()
        {
            DeviceType = "Card";
            ParentNode = "1553B";
            InitializeDefaultParameters();
            InitializeChildren();
        }

        public Mil1553BDevice(string name, string slotPosition) : base()
        {
            DeviceType = "Card";
            ParentNode = "1553B";
            Model = "PXI-4332";
            ParseDeviceName(name);
            SlotPosition = slotPosition;
            
            InitializeDefaultParameters();
            InitializeByModel(name);
            
            InitializeChildren();
        }

        /// <summary>
        /// 初始化默认参数（ART-MILSTD-1553B系列通用参数）
        /// </summary>
        private void InitializeDefaultParameters()
        {
            // 基本参数
            ProductName = "ART-MILSTD-1553B";
            BusTypeDescription = "PXI";
            MaxChannels = 4;
            CommunicationSpeed = Mil1553BSpeedMode.Speed1Mbps;
            ProtocolStandard = "GJB 289A-1997 / MIL-STD-1553B";
            WorkModeType = Mil1553BWorkMode.FullFunction;
            OnboardMemorySize = 8;
            BusInterfaceCount = 4;  // 4套双冗余总线接口
            WorkMode = Mil1553BWorkMode.FullFunction;
            BusType = Mil1553BBusType.DualRedundant;
            SupportsBcRtMt = true;

            // 通道功能参数
            ChannelStructure = "4套双冗余总线接口 (BusA+BusB)";
            CouplingType = Mil1553BCouplingType.Transformer179;
            TimestampBits = 48;
            TimestampResolution = "1μs";
            HardwareInterruptSupport = true;
            TemperatureMonitorSupport = true;

            // BC功能参数
            MessageSchedulingSupport = true;
            RetryMechanismSupport = true;
            DataBufferSupport = true;

            // RT功能参数
            ResponseTimeProgrammable = true;
            StatusWordProgrammable = true;
            IllegalCommandSupport = true;
            TransmitMode = Mil1553BTransmitMode.CircularBuffer;
            DataProtectionSupport = true;

            // BM功能参数
            MonitorRange = "全总线监控";
            FilterFunctionSupport = true;

            // 接口与连接参数
            ConnectorType = Mil1553BConnectorType.DB62;
            PinCount = 62;
            CouplingSignalNaming = "XA+/XA-、XB+/XB- (变压器耦合)";

            // 板卡外形参数
            Dimensions = "160mm × 100mm";
            ConnectorTypeDescription = "DB62母座";
            LedIndicators = "电源LED、状态LED、总线活动LED";

            // 环境与工作条件
            OperatingTemp = "0°C ~ 55°C";
            OperatingHumidity = "10% ~ 90% (无凝结)";
            StorageTemp = "-40°C ~ 85°C";
            StorageHumidity = "5% ~ 95% (无凝结)";

            // 软件支持
            SupportedOS = "Windows XP/7/10/11, Linux, VxWorks, QNX, RTX";
        }

        /// <summary>
        /// 根据型号初始化特定参数
        /// </summary>
        private void InitializeByModel(string modelName)
        {
            if (string.IsNullOrEmpty(modelName))
                return;

            var lowerName = modelName.ToLower();

            // PXI-4332 系列型号识别
            if (lowerName.Contains("4332") || lowerName.Contains("pxi-4332") || 
                lowerName.Contains("pxie-4332") || lowerName.Contains("art4332"))
            {
                // PXI-4332: 4套双冗余总线接口
                BusInterfaceCount = 4;
                MaxChannels = 4;
                ConnectorType = Mil1553BConnectorType.DB62;
                PinCount = 62;
                Dimensions = "160mm × 100mm";
                BusTypeDescription = "PXI";
                ProductName = "ART-MILSTD-1553B (PXI-4332)";
                WorkModeType = Mil1553BWorkMode.FullFunction;
                ChannelStructure = "4套双冗余总线接口 (BusA+BusB)";
            }
            else
            {
                // 默认配置：4套双冗余总线接口
                BusInterfaceCount = 4;
                MaxChannels = 4;
                ConnectorType = Mil1553BConnectorType.DB62;
                PinCount = 62;
            }
        }

        public override void InitializeChildren()
        {
            Children.Clear();

            // 为每个总线接口创建BusInterface子节点（最多两层：Device → BusInterface）
            for (int i = 1; i <= BusInterfaceCount; i++)
            {
                Children.Add(new Mil1553BBusInterface
                {
                    Name = $"BusInterface{i}",
                    ParentNode = "1553B",
                    Model = "双冗余总线接口",
                    InterfaceNumber = i,
                    BusType = BusType,
                    CouplingType = CouplingType,
                    WorkMode = WorkMode,
                    SlotPosition = $"BusInterface{i}",
                    Status = Constants.DeviceConstants.Status.Normal
                });
            }
        }

        /// <summary>
        /// 初始化设备的通道集合
        /// BC、RT、BM属于运行角色而非物理通道，不应作为DeviceChannel创建
        /// </summary>
        public override void InitializeChannels()
        {
            base.InitializeChannels(); // 清空通道集合
            // 1553B设备不创建Channel，BC/RT/BM是运行角色配置，不是物理通道
        }

        public override ObservableCollection<DeviceInfoItem> GetDeviceInfoItems()
        {
            var items = new ObservableCollection<DeviceInfoItem>();
            var mainDeviceInfo = DeviceInfoItem.FromDevice(this, false);
            if (mainDeviceInfo != null)
            {
                items.Add(mainDeviceInfo);
            }

            // 添加所有BusInterface子节点信息
            foreach (var busInterface in Children.OfType<Mil1553BBusInterface>())
            {
                var busInterfaceInfo = DeviceInfoItem.FromDevice(busInterface, true);
                if (busInterfaceInfo != null)
                {
                    items.Add(busInterfaceInfo);
                }
            }

            return items;
        }

        // 辅助方法：获取枚举的描述文本
        private string GetWorkModeDescription(Mil1553BWorkMode mode)
        {
            switch (mode)
            {
                case Mil1553BWorkMode.BC: return "BC (总线控制器)";
                case Mil1553BWorkMode.RT: return "RT (远程终端)";
                case Mil1553BWorkMode.BM: return "BM (总线监视器)";
                case Mil1553BWorkMode.FullFunction: return "全功能 (BC+31RT+BM)";
                default: return mode.ToString();
            }
        }

        private string GetSpeedModeDescription(Mil1553BSpeedMode mode)
        {
            switch (mode)
            {
                case Mil1553BSpeedMode.Speed1Mbps: return "1 Mbps";
                case Mil1553BSpeedMode.Speed2Mbps: return "2 Mbps";
                case Mil1553BSpeedMode.Speed4Mbps: return "4 Mbps";
                default: return mode.ToString();
            }
        }

        private string GetCouplingTypeDescription(Mil1553BCouplingType type)
        {
            switch (type)
            {
                case Mil1553BCouplingType.Direct25: return "直接耦合 25Ω";
                case Mil1553BCouplingType.Transformer179: return "变压器耦合 179Ω";
                default: return type.ToString();
            }
        }

        private string GetConnectorTypeDescription(Mil1553BConnectorType type)
        {
            switch (type)
            {
                case Mil1553BConnectorType.DB62: return "DB62母座";
                default: return type.ToString();
            }
        }

        private string GetTransmitModeDescription(Mil1553BTransmitMode mode)
        {
            switch (mode)
            {
                case Mil1553BTransmitMode.SingleBuffer: return "单缓冲";
                case Mil1553BTransmitMode.CircularBuffer: return "循环缓冲";
                default: return mode.ToString();
            }
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
            return $"1553B::{Manufacturer}::{Model}::{SlotPosition}";
        }

        public override bool ValidateConfiguration()
        {
            return base.ValidateConfiguration() &&
                   BusInterfaceCount > 0;
        }
    }

    /// <summary>
    /// 1553B总线接口（BusInterface）
    /// 表示一套双冗余总线接口（BusA + BusB）
    /// </summary>
    public class Mil1553BBusInterface : DeviceBase
    {
        private int _interfaceNumber;
        private Mil1553BBusType _busType;
        private Mil1553BCouplingType _couplingType;
        private Mil1553BWorkMode _workMode;

        /// <summary>
        /// 接口编号（1-4）
        /// </summary>
        public int InterfaceNumber
        {
            get => _interfaceNumber;
            set => SetProperty(ref _interfaceNumber, value);
        }

        /// <summary>
        /// 总线类型（BusA/BusB/DualRedundant）
        /// </summary>
        public Mil1553BBusType BusType
        {
            get => _busType;
            set => SetProperty(ref _busType, value);
        }

        /// <summary>
        /// 耦合方式（Direct25/Transformer179）
        /// </summary>
        public Mil1553BCouplingType CouplingType
        {
            get => _couplingType;
            set => SetProperty(ref _couplingType, value);
        }

        /// <summary>
        /// 工作模式（BC/RT/BM/FullFunction）
        /// </summary>
        public Mil1553BWorkMode WorkMode
        {
            get => _workMode;
            set => SetProperty(ref _workMode, value);
        }

        public override string DeviceTypeName => "1553B BusInterface";

        public Mil1553BBusInterface()
        {
            DeviceType = Constants.DeviceConstants.Type.SubNode;
            ParentNode = "1553B";
            InterfaceNumber = 1;
            BusType = Mil1553BBusType.DualRedundant;
            CouplingType = Mil1553BCouplingType.Transformer179;
            WorkMode = Mil1553BWorkMode.FullFunction;
            SlotPosition = "N/A";
            Status = Constants.DeviceConstants.Status.Normal;
        }

        public override void InitializeChildren()
        {
            // BusInterface不再有子节点（避免引入无实际硬件意义的Node）
            Children.Clear();
        }

        public override ObservableCollection<DeviceInfoItem> GetDeviceInfoItems()
        {
            var items = new ObservableCollection<DeviceInfoItem>();
            items.Add(new DeviceInfoItem(DeviceTypeName, $"BusInterface{InterfaceNumber}", SlotPosition, Status, true, "Card"));
            return items;
        }

        public override string GetConnectionString()
        {
            return $"1553BBusInterface::{InterfaceNumber}::{BusType}::{CouplingType}";
        }
    }
}

