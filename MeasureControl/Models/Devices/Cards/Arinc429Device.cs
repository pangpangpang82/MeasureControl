using System;
using System.Collections.ObjectModel;
using System.Linq;
using MeasureControl.Helpers;
using MeasureControl.Models.Devices.DeviceCategories;

namespace MeasureControl.Models.Devices
{
    /// <summary>
    /// ARINC429 工作模式
    /// </summary>
    public enum Arinc429WorkMode
    {
        Receive,        // 接收
        Transmit,       // 发送
        ReceiveTransmit // 收发
    }

    /// <summary>
    /// ARINC429 速率模式
    /// </summary>
    public enum Arinc429SpeedMode
    {
        Fixed,      // 固定码率
        Adaptive    // 自适应码率
    }

    /// <summary>
    /// ARINC429 校验位
    /// </summary>
    public enum Arinc429Parity
    {
        Odd,    // 奇校验
        Even,   // 偶校验
        None    // 无校验
    }

    /// <summary>
    /// ARINC429 发送模式
    /// </summary>
    public enum Arinc429TransmitMode
    {
        Single,     // Single单次发送
        Period      // Period周期发送
    }

    /// <summary>
    /// ARINC429 接收方式
    /// </summary>
    public enum Arinc429ReceiveMode
    {
        Interrupt,  // 中断
        Polling     // 查询
    }

    /// <summary>
    /// ARINC429 过滤方式
    /// </summary>
    public enum Arinc429FilterMode
    {
        Label,      // Label过滤
        SDI,        // SDI过滤
        LabelAndSDI // Label+SDI过滤
    }

    /// <summary>
    /// ARINC429 总线类型
    /// </summary>
    public enum Arinc429BusType
    {
        PXIe,   // PXIe总线
        PXI,    // PXI总线
        PCIe,   // PCIe总线
        PCI,    // PCI总线
        USB     // USB总线
    }

    /// <summary>
    /// ARINC429 数据格式
    /// </summary>
    public enum Arinc429DataFormat
    {
        Standard32Bit,  // 32bit标准格式
        Format25Bit     // 25bit格式
    }

    /// <summary>
    /// ARINC429 接插件类型
    /// </summary>
    public enum Arinc429ConnectorType
    {
        SCSI100,    // SCSI100针
        SCSI50      // SCSI50针
    }

    /// <summary>
    /// ARINC429总线设备类（如：阿尔泰PXIe-4227）
    /// </summary>
    public class Arinc429Device : PxiDeviceBase
    {
        private int _channelCount;
        private int _txChannelCount;
        private int _rxChannelCount;
        private string _baudRate;
        private string _protocol;
        private bool _supportsHighSpeed;

        // ========== 基本参数组 ==========
        private Arinc429BusType _busType;
        private int _onboardMemorySize;
        private int _maxChannels;
        private string _dataRateRange;
        private int _lowSpeedRate;
        private int _highSpeedRate;

        // ========== 通道功能参数组 ==========
        private Arinc429WorkMode _workMode;
        private Arinc429SpeedMode _speedMode;
        private Arinc429Parity _parity;
        private bool _supportsLabelFilter;
        private bool _supportsSDIFilter;
        private int _timestampResolution;
        private int _interruptTriggerDepth;

        // ========== 发送功能参数组 ==========
        private string _outputVoltage;
        private int _transmitFIFOSize;
        private Arinc429TransmitMode _transmitMode;
        private string _messageInterval;
        private bool _supportsSelfTest;

        // ========== 接收功能参数组 ==========
        private string _inputVoltageRange;
        private int _receiveFIFOSize;
        private Arinc429ReceiveMode _receiveMode;
        private int _receiveInterruptDepth;
        private Arinc429FilterMode _filterMode;
        private bool _supportsAdaptiveBaudRate;

        // ========== 接口与连接参数组 ==========
        private Arinc429ConnectorType _connectorType;
        private int _pinCount;
        private string _cableRequirements;

        // ========== 数据格式参数组 ==========
        private Arinc429DataFormat _dataFormat;
        private string _bitOrder;

        // ========== 板卡外形参数组 ==========
        private string _dimensions;
        private string _ledIndicators;

        // ========== 环境与工作条件参数组 ==========
        private string _operatingTemp;
        private string _operatingHumidity;
        private string _storageTemp;
        private string _storageHumidity;

        // ========== 软件支持参数组 ==========
        private string _supportedOS;
        
        /// <summary>
        /// ARINC429 总通道数（仅用于规格展示 = TxChannelCount + RxChannelCount）
        /// </summary>
        public int ChannelCount
        {
            get => _channelCount;
            private set => SetProperty(ref _channelCount, value);
        }

        /// <summary>
        /// 发送通道数（TX 方向单向物理总线数量）
        /// </summary>
        public int TxChannelCount
        {
            get => _txChannelCount;
            set
            {
                if (SetProperty(ref _txChannelCount, value))
                {
                    ChannelCount = _txChannelCount + _rxChannelCount;
                }
            }
        }

        /// <summary>
        /// 接收通道数（RX 方向单向物理总线数量）
        /// </summary>
        public int RxChannelCount
        {
            get => _rxChannelCount;
            set
            {
                if (SetProperty(ref _rxChannelCount, value))
                {
                    ChannelCount = _txChannelCount + _rxChannelCount;
                }
            }
        }

        /// <summary>
        /// 波特率（100K/12.5K）
        /// </summary>
        public string BaudRate
        {
            get => _baudRate;
            set => SetProperty(ref _baudRate, value);
        }

        /// <summary>
        /// 协议版本
        /// </summary>
        public string Protocol
        {
            get => _protocol;
            set => SetProperty(ref _protocol, value);
        }

        /// <summary>
        /// 支持高速模式
        /// </summary>
        public bool SupportsHighSpeed
        {
            get => _supportsHighSpeed;
            set => SetProperty(ref _supportsHighSpeed, value);
        }

        // ========== 基本参数组公共属性 ==========
        
        /// <summary>
        /// 总线类型
        /// </summary>
        public new Arinc429BusType BusType
        {
            get => _busType;
            set => SetProperty(ref _busType, value);
        }

        /// <summary>
        /// 板载缓存大小 (MB)
        /// </summary>
        public int OnboardMemorySize
        {
            get => _onboardMemorySize;
            set => SetProperty(ref _onboardMemorySize, value);
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
        /// 数据速率范围描述
        /// </summary>
        public string DataRateRange
        {
            get => _dataRateRange;
            set => SetProperty(ref _dataRateRange, value);
        }

        /// <summary>
        /// 低速速率 (kbps)
        /// </summary>
        public int LowSpeedRate
        {
            get => _lowSpeedRate;
            set => SetProperty(ref _lowSpeedRate, value);
        }

        /// <summary>
        /// 高速速率 (kbps)
        /// </summary>
        public int HighSpeedRate
        {
            get => _highSpeedRate;
            set => SetProperty(ref _highSpeedRate, value);
        }

        // ========== 通道功能参数组公共属性 ==========

        /// <summary>
        /// 工作模式
        /// </summary>
        public Arinc429WorkMode WorkMode
        {
            get => _workMode;
            set => SetProperty(ref _workMode, value);
        }

        /// <summary>
        /// 速率模式
        /// </summary>
        public Arinc429SpeedMode SpeedMode
        {
            get => _speedMode;
            set => SetProperty(ref _speedMode, value);
        }

        /// <summary>
        /// 校验位
        /// </summary>
        public Arinc429Parity Parity
        {
            get => _parity;
            set => SetProperty(ref _parity, value);
        }

        /// <summary>
        /// 支持Label过滤
        /// </summary>
        public bool SupportsLabelFilter
        {
            get => _supportsLabelFilter;
            set => SetProperty(ref _supportsLabelFilter, value);
        }

        /// <summary>
        /// 支持SDI过滤
        /// </summary>
        public bool SupportsSDIFilter
        {
            get => _supportsSDIFilter;
            set => SetProperty(ref _supportsSDIFilter, value);
        }

        /// <summary>
        /// 时间标签精度 (微秒)
        /// </summary>
        public int TimestampResolution
        {
            get => _timestampResolution;
            set => SetProperty(ref _timestampResolution, value);
        }

        /// <summary>
        /// 中断触发深度
        /// </summary>
        public int InterruptTriggerDepth
        {
            get => _interruptTriggerDepth;
            set => SetProperty(ref _interruptTriggerDepth, value);
        }

        // ========== 发送功能参数组公共属性 ==========

        /// <summary>
        /// 输出电平
        /// </summary>
        public string OutputVoltage
        {
            get => _outputVoltage;
            set => SetProperty(ref _outputVoltage, value);
        }

        /// <summary>
        /// 发送FIFO大小
        /// </summary>
        public int TransmitFIFOSize
        {
            get => _transmitFIFOSize;
            set => SetProperty(ref _transmitFIFOSize, value);
        }

        /// <summary>
        /// 发送模式
        /// </summary>
        public Arinc429TransmitMode TransmitMode
        {
            get => _transmitMode;
            set => SetProperty(ref _transmitMode, value);
        }

        /// <summary>
        /// 消息间隔配置
        /// </summary>
        public string MessageInterval
        {
            get => _messageInterval;
            set => SetProperty(ref _messageInterval, value);
        }

        /// <summary>
        /// 支持自检模式
        /// </summary>
        public bool SupportsSelfTest
        {
            get => _supportsSelfTest;
            set => SetProperty(ref _supportsSelfTest, value);
        }

        // ========== 接收功能参数组公共属性 ==========

        /// <summary>
        /// 输入电平范围
        /// </summary>
        public string InputVoltageRange
        {
            get => _inputVoltageRange;
            set => SetProperty(ref _inputVoltageRange, value);
        }

        /// <summary>
        /// 接收FIFO大小
        /// </summary>
        public int ReceiveFIFOSize
        {
            get => _receiveFIFOSize;
            set => SetProperty(ref _receiveFIFOSize, value);
        }

        /// <summary>
        /// 接收方式
        /// </summary>
        public Arinc429ReceiveMode ReceiveMode
        {
            get => _receiveMode;
            set => SetProperty(ref _receiveMode, value);
        }

        /// <summary>
        /// 接收中断深度
        /// </summary>
        public int ReceiveInterruptDepth
        {
            get => _receiveInterruptDepth;
            set => SetProperty(ref _receiveInterruptDepth, value);
        }

        /// <summary>
        /// 过滤方式
        /// </summary>
        public Arinc429FilterMode FilterMode
        {
            get => _filterMode;
            set => SetProperty(ref _filterMode, value);
        }

        /// <summary>
        /// 支持自适应波特率
        /// </summary>
        public bool SupportsAdaptiveBaudRate
        {
            get => _supportsAdaptiveBaudRate;
            set => SetProperty(ref _supportsAdaptiveBaudRate, value);
        }

        // ========== 接口与连接参数组公共属性 ==========

        /// <summary>
        /// 接插件类型
        /// </summary>
        public Arinc429ConnectorType ConnectorType
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
        /// 线缆要求
        /// </summary>
        public string CableRequirements
        {
            get => _cableRequirements;
            set => SetProperty(ref _cableRequirements, value);
        }

        // ========== 数据格式参数组公共属性 ==========

        /// <summary>
        /// 数据格式
        /// </summary>
        public Arinc429DataFormat DataFormat
        {
            get => _dataFormat;
            set => SetProperty(ref _dataFormat, value);
        }

        /// <summary>
        /// 位顺序说明
        /// </summary>
        public string BitOrder
        {
            get => _bitOrder;
            set => SetProperty(ref _bitOrder, value);
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

        public override string DeviceTypeName => "ARINC429";

        public Arinc429Device() : base()
        {
            DeviceType = "Card";
            ParentNode = "ARINC429";
            InitializeDefaultParameters();
            InitializeChildren();
        }

        public Arinc429Device(string name, string slotPosition) : base()
        {
            DeviceType = "Card";
            ParentNode = "ARINC429";
            Model = "PXIe-4227";
            ParseDeviceName(name);
            SlotPosition = slotPosition;
            
            InitializeDefaultParameters();
            InitializeByModel(name);
            
            InitializeChildren();
        }

        /// <summary>
        /// 初始化默认参数（ART4229系列通用参数）
        /// </summary>
        private void InitializeDefaultParameters()
        {
            // 基本参数
            BusType = Arinc429BusType.PXIe;
            OnboardMemorySize = 8;
            MaxChannels = 40;
            LowSpeedRate = 12;      // 12.5 kbps
            HighSpeedRate = 100;    // 100 kbps
            DataRateRange = "12.5 / 100 Kbps";
            BaudRate = "100K";
            Protocol = "ARINC429";
            SupportsHighSpeed = true;
            // 默认 TX/RX 通道数量（单向物理总线），总数仅用于规格展示
            TxChannelCount = 8;
            RxChannelCount = 8;

            // 通道功能参数
            WorkMode = Arinc429WorkMode.ReceiveTransmit;
            SpeedMode = Arinc429SpeedMode.Fixed;
            Parity = Arinc429Parity.Odd;
            SupportsLabelFilter = true;
            SupportsSDIFilter = true;
            TimestampResolution = 1;    // 1μs
            InterruptTriggerDepth = 512;

            // 发送功能参数
            OutputVoltage = "10 Vp-p";
            TransmitFIFOSize = 4096;
            TransmitMode = Arinc429TransmitMode.Period;
            MessageInterval = "可配置 (1~65535 us)";
            SupportsSelfTest = true;

            // 接收功能参数
            InputVoltageRange = "±15V";
            ReceiveFIFOSize = 4096;
            ReceiveMode = Arinc429ReceiveMode.Interrupt;
            ReceiveInterruptDepth = 512;
            FilterMode = Arinc429FilterMode.LabelAndSDI;
            SupportsAdaptiveBaudRate = true;

            // 接口与连接参数
            ConnectorType = Arinc429ConnectorType.SCSI100;
            PinCount = 100;
            CableRequirements = "屏蔽双绞线";

            // 数据格式参数
            DataFormat = Arinc429DataFormat.Standard32Bit;
            BitOrder = "LSB first (位1~位32)";

            // 板卡外形参数
            Dimensions = "3U PXI";
            LedIndicators = "电源LED、状态LED";

            // 环境与工作条件
            OperatingTemp = "0°C ~ 55°C";
            OperatingHumidity = "10% ~ 90% (无凝结)";
            StorageTemp = "-20°C ~ 70°C";
            StorageHumidity = "5% ~ 95% (无凝结)";

            // 软件支持
            SupportedOS = "Windows 10/11, Linux";
        }

        /// <summary>
        /// 根据型号初始化特定参数
        /// </summary>
        private void InitializeByModel(string modelName)
        {
            if (string.IsNullOrEmpty(modelName))
                return;

            var lowerName = modelName.ToLower();

            // ART4229 系列型号识别
            if (lowerName.Contains("4229"))
            {
                // ART4229: 40 个单向物理通道，示例分配为 20TX + 20RX
                TxChannelCount = 20;
                RxChannelCount = 20;
                MaxChannels = 40;
                ConnectorType = Arinc429ConnectorType.SCSI100;
                PinCount = 100;
                Dimensions = "3U PXI (162mm × 100mm)";
            }
            else if (lowerName.Contains("4228"))
            {
                // ART4228: 32 个单向物理通道，示例分配为 16TX + 16RX
                TxChannelCount = 16;
                RxChannelCount = 16;
                MaxChannels = 32;
                ConnectorType = Arinc429ConnectorType.SCSI100;
                PinCount = 100;
                Dimensions = "3U PXI (162mm × 100mm)";
            }
            else if (lowerName.Contains("4227"))
            {
                // ART4227 / PXIe-4227: 16 个单向物理通道，示例分配为 8TX + 8RX
                TxChannelCount = 8;
                RxChannelCount = 8;
                MaxChannels = 16;
                ConnectorType = Arinc429ConnectorType.SCSI50;
                PinCount = 50;
                Dimensions = "3U PXI (162mm × 100mm)";
            }
            else if (lowerName.Contains("4226"))
            {
                // ART4226: 8 个单向物理通道，示例分配为 4TX + 4RX
                TxChannelCount = 4;
                RxChannelCount = 4;
                MaxChannels = 8;
                ConnectorType = Arinc429ConnectorType.SCSI50;
                PinCount = 50;
                Dimensions = "3U PXI (162mm × 100mm)";
            }
            else if (lowerName.Contains("4223"))
            {
                // ART4223: 4 个单向物理通道，示例分配为 2TX + 2RX
                TxChannelCount = 2;
                RxChannelCount = 2;
                MaxChannels = 4;
                ConnectorType = Arinc429ConnectorType.SCSI50;
                PinCount = 50;
                Dimensions = "3U PXI (162mm × 100mm)";
            }
            else if (lowerName.Contains("4222"))
            {
                // ART4222: 2 个单向物理通道，示例分配为 1TX + 1RX
                TxChannelCount = 1;
                RxChannelCount = 1;
                MaxChannels = 2;
                ConnectorType = Arinc429ConnectorType.SCSI50;
                PinCount = 50;
                Dimensions = "3U PXI (162mm × 100mm)";
            }
            else
            {
                // 默认配置：16 个单向物理通道，示例分配为 8TX + 8RX
                TxChannelCount = 8;
                RxChannelCount = 8;
                MaxChannels = 16;
                ConnectorType = Arinc429ConnectorType.SCSI50;
                PinCount = 50;
            }
        }

        public override void InitializeChildren()
        {
            Children.Clear();

            // 按方向对物理通道分组，仅做功能分组，不参与地址或编号计算
            if (TxChannelCount > 0)
            {
                Children.Add(new Arinc429TxNode
                {
                    Name = "ARINC429 TX",
                    ParentNode = "ARINC429",
                    ChannelCount = TxChannelCount,
                    Model = $"{TxChannelCount}通道",
                    SlotPosition = $"TX1–TX{TxChannelCount}",  // 设备内发送通道范围
                    Status = "正常"
                });
            }

            if (RxChannelCount > 0)
            {
                Children.Add(new Arinc429RxNode
                {
                    Name = "ARINC429 RX",
                    ParentNode = "ARINC429",
                    ChannelCount = RxChannelCount,
                    Model = $"{RxChannelCount}通道",
                    SlotPosition = $"RX1–RX{RxChannelCount}",  // 设备内接收通道范围
                    Status = "正常"
                });
            }
        }

        /// <summary>
        /// 初始化设备的通道集合
        /// </summary>
        public override void InitializeChannels()
        {
            base.InitializeChannels(); // 清空通道集合
            
            // 使用明确的 TX/RX 通道数量创建逻辑通道
            var channels = ChannelFactory.CreateArinc429Channels(
                Id, 
                Name, 
                SlotPosition, 
                TxChannelCount,
                RxChannelCount,
                1  // 默认机箱编号为1
            );
            
            foreach (var channel in channels)
            {
                Channels.Add(channel);
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

            // 添加TX/RX分组子节点信息（仅用于功能分组）
            foreach (var node in Children.OfType<Arinc429TxNode>())
            {
                var subNodeInfo = DeviceInfoItem.FromDevice(node, true);
                if (subNodeInfo != null)
                {
                    items.Add(subNodeInfo);
                }
            }

            foreach (var node in Children.OfType<Arinc429RxNode>())
            {
                var subNodeInfo = DeviceInfoItem.FromDevice(node, true);
                if (subNodeInfo != null)
                {
                    items.Add(subNodeInfo);
                }
            }

            return items;
        }

        // 辅助方法：获取枚举的描述文本
        private string GetWorkModeDescription(Arinc429WorkMode mode)
        {
            switch (mode)
            {
                case Arinc429WorkMode.Receive: return "接收";
                case Arinc429WorkMode.Transmit: return "发送";
                case Arinc429WorkMode.ReceiveTransmit: return "收发";
                default: return mode.ToString();
            }
        }

        private string GetSpeedModeDescription(Arinc429SpeedMode mode)
        {
            switch (mode)
            {
                case Arinc429SpeedMode.Fixed: return "固定码率";
                case Arinc429SpeedMode.Adaptive: return "自适应码率";
                default: return mode.ToString();
            }
        }

        private string GetParityDescription(Arinc429Parity parity)
        {
            switch (parity)
            {
                case Arinc429Parity.Odd: return "奇校验";
                case Arinc429Parity.Even: return "偶校验";
                case Arinc429Parity.None: return "无校验";
                default: return parity.ToString();
            }
        }

        private string GetTransmitModeDescription(Arinc429TransmitMode mode)
        {
            switch (mode)
            {
                case Arinc429TransmitMode.Single: return "单次发送";
                case Arinc429TransmitMode.Period: return "周期发送";
                default: return mode.ToString();
            }
        }

        private string GetReceiveModeDescription(Arinc429ReceiveMode mode)
        {
            switch (mode)
            {
                case Arinc429ReceiveMode.Interrupt: return "中断";
                case Arinc429ReceiveMode.Polling: return "查询";
                default: return mode.ToString();
            }
        }

        private string GetFilterModeDescription(Arinc429FilterMode mode)
        {
            switch (mode)
            {
                case Arinc429FilterMode.Label: return "Label过滤";
                case Arinc429FilterMode.SDI: return "SDI过滤";
                case Arinc429FilterMode.LabelAndSDI: return "Label+SDI过滤";
                default: return mode.ToString();
            }
        }

        private string GetConnectorTypeDescription(Arinc429ConnectorType type)
        {
            switch (type)
            {
                case Arinc429ConnectorType.SCSI100: return "SCSI-100针";
                case Arinc429ConnectorType.SCSI50: return "SCSI-50针";
                default: return type.ToString();
            }
        }

        private string GetDataFormatDescription(Arinc429DataFormat format)
        {
            switch (format)
            {
                case Arinc429DataFormat.Standard32Bit: return "32bit标准格式";
                case Arinc429DataFormat.Format25Bit: return "25bit格式";
                default: return format.ToString();
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
            return $"ARINC429::{Manufacturer}::{Model}::{SlotPosition}";
        }

        public override bool ValidateConfiguration()
        {
            return base.ValidateConfiguration() &&
                   TxChannelCount >= 0 &&
                   RxChannelCount >= 0 &&
                   (TxChannelCount + RxChannelCount) > 0;
        }
    }

    /// <summary>
    /// ARINC429 发送通道分组节点（TX）
    /// </summary>
    public class Arinc429TxNode : DeviceBase
    {
        private int _channelCount;

        public int ChannelCount
        {
            get => _channelCount;
            set => SetProperty(ref _channelCount, value);
        }

        public override string DeviceTypeName => "ARINC429 TX";

        public Arinc429TxNode()
        {
            DeviceType = "SubNode";
            ParentNode = "ARINC429";
            ChannelCount = 1;
            SlotPosition = "TX1–TX1";
            Status = "正常";
        }

        public override void InitializeChildren()
        {
            Children.Clear();
        }

        public override ObservableCollection<DeviceInfoItem> GetDeviceInfoItems()
        {
            var items = new ObservableCollection<DeviceInfoItem>();
            items.Add(new DeviceInfoItem(DeviceTypeName, Model, SlotPosition, Status, true, "Card"));
            return items;
        }

        public override string GetConnectionString()
        {
            return $"ARINC429_TX::{ChannelCount}CH";
        }
    }

    /// <summary>
    /// ARINC429 接收通道分组节点（RX）
    /// </summary>
    public class Arinc429RxNode : DeviceBase
    {
        private int _channelCount;

        public int ChannelCount
        {
            get => _channelCount;
            set => SetProperty(ref _channelCount, value);
        }

        public override string DeviceTypeName => "ARINC429 RX";

        public Arinc429RxNode()
        {
            DeviceType = "SubNode";
            ParentNode = "ARINC429";
            ChannelCount = 1;
            SlotPosition = "RX1–RX1";
            Status = "正常";
        }

        public override void InitializeChildren()
        {
            Children.Clear();
        }

        public override ObservableCollection<DeviceInfoItem> GetDeviceInfoItems()
        {
            var items = new ObservableCollection<DeviceInfoItem>();
            items.Add(new DeviceInfoItem(DeviceTypeName, Model, SlotPosition, Status, true, "Card"));
            return items;
        }

        public override string GetConnectionString()
        {
            return $"ARINC429_RX::{ChannelCount}CH";
        }
    }
}

