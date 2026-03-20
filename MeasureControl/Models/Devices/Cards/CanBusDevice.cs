using System;
using System.Collections.ObjectModel;
using System.Linq;
using MeasureControl.Helpers;
using MeasureControl.Models.Devices.DeviceCategories;
using MeasureControl.Models.Channels;

namespace MeasureControl.Models.Devices
{
    /// <summary>
    /// CAN 帧 ID 类型
    /// </summary>
    public enum CanFrameType
    {
        Standard,   // 标准帧（11位 ID）
        Extended    // 扩展帧（29位 ID）
    }

    /// <summary>
    /// CAN 数据帧类型
    /// </summary>
    public enum CanDataFrameType
    {
        DataFrame,   // 数据帧
        RemoteFrame  // 远程帧
    }

    /// <summary>
    /// CAN 滤波器模式
    /// </summary>
    public enum CanFilterMode
    {
        SingleFilter,  // 单滤波
        DualFilter     // 双滤波
    }

    /// <summary>
    /// CAN 发送模式
    /// </summary>
    public enum CanTransmitMode
    {
        Normal,          // 正常发送
        TriggerSend,     // 触发发送
        SelfTestMode     // 自发自收模式
    }

    /// <summary>
    /// 数字 I/O 工作模式
    /// </summary>
    public enum DioMode
    {
        Normal,       // 普通模式
        Synchronous   // 同步模式
    }

    /// <summary>
    /// 数字 I/O 方向
    /// </summary>
    public enum DioDirection
    {
        Input,   // 输入
        Output   // 输出
    }

    /// <summary>
    /// CAN总线设备类（如：阿尔泰PXI-4004）
    /// </summary>
    public class CanBusDevice : PxiDeviceBase
    {
        // ChannelCount 语义：本设备上的 CAN Port 数量（端口资源数），而非信号通道数
        private int _channelCount;
        private int _maxBaudRate;
        private string _protocol;
        private string _transceiverType;
        private bool _supportsCanFD;
        private CanBusNode _canBusNode;

        // 波特率相关属性
        private int _minBaudRate;
        private string _baudRateRange;
        private string _supportedBaudRates;

        // 接口与连接属性
        private string _interfaceType;
        private int _pinCount;
        private bool _hasTerminalResistor;
        private int _terminalResistorValue;
        private bool _channelIsolation;

        // 数字 I/O 属性
        private int _dioChannelCount;
        private int _dioTriggerChannels;
        private DioMode _dioMode;
        private DioDirection _dioDirection;

        // 触发与时标属性
        private bool _supportsTrigger;
        private bool _supportsTimestamp;
        private int _timestampResolution;

        // 帧与滤波属性
        private CanFrameType _frameType;
        private CanDataFrameType _dataFrameType;
        private CanFilterMode _filterMode;
        private bool _supportsIdMask;
        private CanTransmitMode _transmitMode;

        // 环境与工作条件
        private string _operatingTemp;
        private string _storageTemp;
        private string _humidity;
        private string _dimensions;
        private string _busType;
        private string _supportedProtocols;

        /// <summary>
        /// CAN 端口数量（Port 数量，而非信号通道数量）
        /// </summary>
        public int ChannelCount
        {
            get => _channelCount;
            set => SetProperty(ref _channelCount, value);
        }

        /// <summary>
        /// 最大波特率 (bps)
        /// </summary>
        public int MaxBaudRate
        {
            get => _maxBaudRate;
            set => SetProperty(ref _maxBaudRate, value);
        }

        /// <summary>
        /// 协议版本（CAN2.0A/CAN2.0B/CANFD）
        /// </summary>
        public string Protocol
        {
            get => _protocol;
            set => SetProperty(ref _protocol, value);
        }

        /// <summary>
        /// 收发器类型（HighSpeed/LowSpeed）
        /// </summary>
        public string TransceiverType
        {
            get => _transceiverType;
            set => SetProperty(ref _transceiverType, value);
        }

        /// <summary>
        /// 支持CAN FD
        /// </summary>
        public bool SupportsCanFD
        {
            get => _supportsCanFD;
            set => SetProperty(ref _supportsCanFD, value);
        }

        /// <summary>
        /// CAN总线子节点
        /// </summary>
        public CanBusNode CanBusNode
        {
            get => _canBusNode;
            set => SetProperty(ref _canBusNode, value);
        }

        /// <summary>
        /// 最小波特率 (bps)
        /// </summary>
        public int MinBaudRate
        {
            get => _minBaudRate;
            set => SetProperty(ref _minBaudRate, value);
        }

        /// <summary>
        /// 波特率范围描述
        /// </summary>
        public string BaudRateRange
        {
            get => _baudRateRange;
            set => SetProperty(ref _baudRateRange, value);
        }

        /// <summary>
        /// 支持的标准波特率列表
        /// </summary>
        public string SupportedBaudRates
        {
            get => _supportedBaudRates;
            set => SetProperty(ref _supportedBaudRates, value);
        }

        /// <summary>
        /// 接口类型（如：SCSI68）
        /// </summary>
        public string InterfaceType
        {
            get => _interfaceType;
            set => SetProperty(ref _interfaceType, value);
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
        /// 是否有终端电阻
        /// </summary>
        public bool HasTerminalResistor
        {
            get => _hasTerminalResistor;
            set => SetProperty(ref _hasTerminalResistor, value);
        }

        /// <summary>
        /// 终端电阻值（Ω）
        /// </summary>
        public int TerminalResistorValue
        {
            get => _terminalResistorValue;
            set => SetProperty(ref _terminalResistorValue, value);
        }

        /// <summary>
        /// 通道电气隔离
        /// </summary>
        public bool ChannelIsolation
        {
            get => _channelIsolation;
            set => SetProperty(ref _channelIsolation, value);
        }

        /// <summary>
        /// DIO 通道数
        /// </summary>
        public int DioChannelCount
        {
            get => _dioChannelCount;
            set => SetProperty(ref _dioChannelCount, value);
        }

        /// <summary>
        /// 可用于触发的 DIO 数量
        /// </summary>
        public int DioTriggerChannels
        {
            get => _dioTriggerChannels;
            set => SetProperty(ref _dioTriggerChannels, value);
        }

        /// <summary>
        /// DIO 工作模式
        /// </summary>
        public DioMode DioMode
        {
            get => _dioMode;
            set => SetProperty(ref _dioMode, value);
        }

        /// <summary>
        /// DIO 方向
        /// </summary>
        public DioDirection DioDirection
        {
            get => _dioDirection;
            set => SetProperty(ref _dioDirection, value);
        }

        /// <summary>
        /// 支持触发功能
        /// </summary>
        public bool SupportsTrigger
        {
            get => _supportsTrigger;
            set => SetProperty(ref _supportsTrigger, value);
        }

        /// <summary>
        /// 支持时标功能
        /// </summary>
        public bool SupportsTimestamp
        {
            get => _supportsTimestamp;
            set => SetProperty(ref _supportsTimestamp, value);
        }

        /// <summary>
        /// 时标分辨率（微秒）
        /// </summary>
        public int TimestampResolution
        {
            get => _timestampResolution;
            set => SetProperty(ref _timestampResolution, value);
        }

        /// <summary>
        /// 帧类型
        /// </summary>
        public CanFrameType FrameType
        {
            get => _frameType;
            set => SetProperty(ref _frameType, value);
        }

        /// <summary>
        /// 数据帧类型
        /// </summary>
        public CanDataFrameType DataFrameType
        {
            get => _dataFrameType;
            set => SetProperty(ref _dataFrameType, value);
        }

        /// <summary>
        /// 滤波模式
        /// </summary>
        public CanFilterMode FilterMode
        {
            get => _filterMode;
            set => SetProperty(ref _filterMode, value);
        }

        /// <summary>
        /// 支持标识符掩码
        /// </summary>
        public bool SupportsIdMask
        {
            get => _supportsIdMask;
            set => SetProperty(ref _supportsIdMask, value);
        }

        /// <summary>
        /// 发送模式
        /// </summary>
        public CanTransmitMode TransmitMode
        {
            get => _transmitMode;
            set => SetProperty(ref _transmitMode, value);
        }

        /// <summary>
        /// 工作温度范围
        /// </summary>
        public string OperatingTemp
        {
            get => _operatingTemp;
            set => SetProperty(ref _operatingTemp, value);
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
        /// 湿度范围
        /// </summary>
        public string Humidity
        {
            get => _humidity;
            set => SetProperty(ref _humidity, value);
        }

        /// <summary>
        /// 板卡尺寸
        /// </summary>
        public string Dimensions
        {
            get => _dimensions;
            set => SetProperty(ref _dimensions, value);
        }

        /// <summary>
        /// 总线类型（PXI）
        /// </summary>
        public new string BusType
        {
            get => _busType;
            set => SetProperty(ref _busType, value);
        }

        /// <summary>
        /// 支持的协议（CANopen、DeviceNet等）
        /// </summary>
        public string SupportedProtocols
        {
            get => _supportedProtocols;
            set => SetProperty(ref _supportedProtocols, value);
        }

        public override string DeviceTypeName => "CAN总线";

        /// <summary>
        /// CAN总线设备为通信型设备
        /// </summary>
        public override DeviceCapability Capability => DeviceCapability.Communication;

        public CanBusDevice() : base()
        {
            DeviceType = "Card";
            ParentNode = "CAN总线";
            InitializePxi4004Parameters();
            InitializeChildren();
        }

        public CanBusDevice(string name, string slotPosition) : base()
        {
            DeviceType = "Card";
            ParentNode = "CAN总线";
            Model = "PXI-4004";
            InitializePxi4004Parameters();
            ParseDeviceName(name);
            SlotPosition = slotPosition;
            InitializeChildren();
        }

        /// <summary>
        /// 初始化 PXI-4004 的默认参数
        /// </summary>
        private void InitializePxi4004Parameters()
        {
            // 基本参数
            ChannelCount = 20;
            MaxBaudRate = 1000000;  // 1 Mbps
            MinBaudRate = 10000;    // 10 Kbps
            BaudRateRange = "10 Kbps ～ 1 Mbps";
            SupportedBaudRates = "10, 20, 50, 100, 125, 200, 250, 500, 1000 Kbps";

            // 协议与收发器
            Protocol = "CAN 2.0A / 2.0B";
            TransceiverType = "HighSpeed";
            SupportsCanFD = false;
            SupportedProtocols = "CANopen, DeviceNet";

            // 接口与连接
            InterfaceType = "SCSI68";
            PinCount = 68;
            HasTerminalResistor = true;
            TerminalResistorValue = 120;
            ChannelIsolation = true;

            // 数字 I/O
            DioChannelCount = 16;
            DioTriggerChannels = 8;
            DioMode = DioMode.Normal;
            DioDirection = DioDirection.Input;

            // 触发与时标
            SupportsTrigger = true;
            SupportsTimestamp = true;
            TimestampResolution = 1;  // 1 μs

            // 帧与滤波
            FrameType = CanFrameType.Standard;
            DataFrameType = CanDataFrameType.DataFrame;
            FilterMode = CanFilterMode.SingleFilter;
            SupportsIdMask = true;
            TransmitMode = CanTransmitMode.Normal;

            // 环境条件
            OperatingTemp = "−20 ℃ ～ +70 ℃";
            StorageTemp = "−40 ℃ ～ +85 ℃";
            Humidity = "< 90%RH（无结露）";
            Dimensions = "162 mm × 100 mm";
            BusType = "PXI (32位 / 33 MHz)";
        }

        public override void InitializeChildren()
        {
            Children.Clear();

            // 创建 CAN Bus 容器节点（仅描述总线能力和端口数量）
            CanBusNode = new CanBusNode
            {
                Name = "CAN总线",
                ParentNode = "CAN",
                Model = $"{ChannelCount}端口",
                ChannelCount = ChannelCount,
                MaxBaudRate = MaxBaudRate,
                Protocol = Protocol,
                TransceiverType = TransceiverType,
                SlotPosition = $"CAN0–CAN{ChannelCount - 1}",  // 设备内部端口范围，仅用于 UI 标识
                Status = "正常"
            };

            Children.Add(CanBusNode);
        }

        /// <summary>
        /// 初始化设备的通道集合
        /// 仅注册 CAN Port 资源，不创建信号级“帧通道”
        /// </summary>
        public override void InitializeChannels()
        {
            base.InitializeChannels(); // 清空通道集合

            // 为每个 CAN Port 注册一个端口资源通道（Channel 表示 Port 能力，而非具体帧）
            for (int i = 0; i < ChannelCount; i++)
            {
                var portChannel = new CanChannel
                {
                    Name = $"CAN{i}",
                    DeviceId = Id,
                    DeviceName = Name,
                    Description = $"CAN 端口 {i}",
                    MaxBaudRate = MaxBaudRate,
                    Protocol = Protocol,
                    SupportsExtendedFrame = FrameType == CanFrameType.Extended,
                    SupportsCanFD = SupportsCanFD,
                    TransceiverType = TransceiverType,
                    Termination = HasTerminalResistor ? $"{TerminalResistorValue}Ω" : "None"
                };


                Channels.Add(portChannel);
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

            // 添加 CAN Bus 容器节点信息（端口集合由 Channels 表达）
            if (CanBusNode != null)
            {
                var busInfo = DeviceInfoItem.FromDevice(CanBusNode, true);
                if (busInfo != null)
                {
                    items.Add(busInfo);
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
            return $"CAN::{Manufacturer}::{Model}::{SlotPosition}";
        }

        public override bool ValidateConfiguration()
        {
            return base.ValidateConfiguration() &&
                   ChannelCount > 0 &&
                   MaxBaudRate > 0 &&
                   MinBaudRate > 0 &&
                   MinBaudRate < MaxBaudRate &&
                   DioChannelCount >= 0;
        }
    }

    /// <summary>
    /// CAN 总线容器节点（描述总线能力与端口范围）
    /// </summary>
    public class CanBusNode : DeviceBase
    {
        private int _channelCount;
        private int _maxBaudRate;
        private string _protocol;
        private string _transceiverType;

        public int ChannelCount
        {
            get => _channelCount;
            set => SetProperty(ref _channelCount, value);
        }

        public int MaxBaudRate
        {
            get => _maxBaudRate;
            set => SetProperty(ref _maxBaudRate, value);
        }

        public string Protocol
        {
            get => _protocol;
            set => SetProperty(ref _protocol, value);
        }

        public string TransceiverType
        {
            get => _transceiverType;
            set => SetProperty(ref _transceiverType, value);
        }

        public override string DeviceTypeName => "CAN";

        public CanBusNode()
        {
            DeviceType = "SubNode";
            ParentNode = "CAN";
            ChannelCount = 2;
            MaxBaudRate = 1000000;
            Protocol = "CAN2.0B";
            TransceiverType = "HighSpeed";
            SlotPosition = "N/A";
            Status = "正常";
        }

        public override void InitializeChildren()
        {
            // 不再挂端口子节点，端口资源由 Channels 表达
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
            return $"CANNode::{ChannelCount}Ports::{MaxBaudRate}bps";
        }
    }
}

