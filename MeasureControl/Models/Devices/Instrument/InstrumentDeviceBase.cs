using System;
using System.Collections.ObjectModel;

namespace MeasureControl.Models.Devices.DeviceCategories
{
    /// <summary>
    /// 通信接口类型枚举
    /// </summary>
    [Flags]
    public enum CommunicationInterface
    {
        None = 0,
        RS232 = 1,
        USB = 2,
        GPIB = 4,
        LAN = 8,
        CAN = 16,
        DigitalIO = 32
    }

    /// <summary>
    /// 仪器仪表设备抽象基类
    /// 所有独立的台式仪器继承此类
    /// </summary>
    public abstract class InstrumentDeviceBase : DeviceBase
    {
        private CommunicationInterface _supportedInterfaces;
        private string _activeInterface;
        private string _gpibAddress;
        private string _ipAddress;
        private int _lanPort;
        private string _serialPort;
        private int _timeout;
        private bool _isRemoteControlled;

        /// <summary>
        /// 支持的通信接口（可多选）
        /// </summary>
        public CommunicationInterface SupportedInterfaces
        {
            get => _supportedInterfaces;
            set => SetProperty(ref _supportedInterfaces, value);
        }

        /// <summary>
        /// 当前激活的通信接口
        /// </summary>
        public string ActiveInterface
        {
            get => _activeInterface;
            set => SetProperty(ref _activeInterface, value);
        }

        /// <summary>
        /// GPIB地址（如果使用GPIB接口）
        /// </summary>
        public string GpibAddress
        {
            get => _gpibAddress;
            set => SetProperty(ref _gpibAddress, value);
        }

        /// <summary>
        /// IP地址（如果使用LAN接口）
        /// </summary>
        public string IpAddress
        {
            get => _ipAddress;
            set => SetProperty(ref _ipAddress, value);
        }
        public int LanPort
        {
            get => _lanPort;
            set => SetProperty(ref _lanPort, value);
        }

        /// <summary>
        /// 串口端口号（如果使用RS232接口）
        /// </summary>
        public string SerialPort
        {
            get => _serialPort;
            set => SetProperty(ref _serialPort, value);
        }

        /// <summary>
        /// 通信超时时间（ms）
        /// </summary>
        public int Timeout
        {
            get => _timeout;
            set => SetProperty(ref _timeout, value);
        }

        /// <summary>
        /// 是否处于远程控制状态
        /// </summary>
        public bool IsRemoteControlled
        {
            get => _isRemoteControlled;
            set => SetProperty(ref _isRemoteControlled, value);
        }

        /// <summary>
        /// 默认构造函数
        /// </summary>
        protected InstrumentDeviceBase() : base()
        {
            DeviceType = "Instrument";
            SupportedInterfaces = CommunicationInterface.USB | CommunicationInterface.LAN;
            ActiveInterface = "USB";
            LanPort = 0;
            Timeout = 5000;
            IsRemoteControlled = false;
            SlotPosition = "External"; // 仪器仪表通常不在机箱插槽中
        }

        /// <summary>
        /// 带参数构造函数
        /// </summary>
        protected InstrumentDeviceBase(string name, string manufacturer, string model, string slotPosition) 
            : base(name, manufacturer, model, slotPosition)
        {
            DeviceType = "Instrument";
            SupportedInterfaces = CommunicationInterface.USB | CommunicationInterface.LAN;
            ActiveInterface = "USB";
            LanPort = 0;
            Timeout = 5000;
            IsRemoteControlled = false;
            if (string.IsNullOrEmpty(slotPosition))
            {
                SlotPosition = "External";
            }
        }

        /// <summary>
        /// 验证仪器设备配置
        /// </summary>
        public override bool ValidateConfiguration()
        {
            if (!base.ValidateConfiguration())
                return false;

            // 验证当前激活接口是否被支持
            if (!string.IsNullOrEmpty(ActiveInterface))
            {
                var activeInterfaceEnum = ParseInterface(ActiveInterface);
                if (activeInterfaceEnum != CommunicationInterface.None && 
                    !SupportedInterfaces.HasFlag(activeInterfaceEnum))
                {
                    return false;
                }
            }

            // 如果使用GPIB，必须有地址
            if (ActiveInterface == "GPIB" && string.IsNullOrEmpty(GpibAddress))
                return false;

            // 如果使用LAN，必须有IP地址
            if (ActiveInterface == "LAN" && string.IsNullOrEmpty(IpAddress))
                return false;

            return true;
        }

        /// <summary>
        /// 获取仪器设备连接字符串
        /// </summary>
        public override string GetConnectionString()
        {
            switch (ActiveInterface)
            {
                case "GPIB":
                    return $"GPIB::{GpibAddress}::INSTR";
                case "LAN":
                    return $"TCPIP::{IpAddress}::INSTR";
                case "RS232":
                    return $"ASRL::{SerialPort}::INSTR";
                case "USB":
                    return $"USB::{Manufacturer}::{Model}::INSTR";
                default:
                    return base.GetConnectionString();
            }
        }

        /// <summary>
        /// 设置通信接口
        /// </summary>
        public virtual bool SetCommunicationInterface(string interfaceName, string address = "")
        {
            var interfaceEnum = ParseInterface(interfaceName);
            if (interfaceEnum == CommunicationInterface.None || !SupportedInterfaces.HasFlag(interfaceEnum))
                return false;

            ActiveInterface = interfaceName;

            switch (interfaceName)
            {
                case "GPIB":
                    GpibAddress = address;
                    break;
                case "LAN":
                    IpAddress = address;
                    break;
                case "RS232":
                    SerialPort = address;
                    break;
            }

            return true;
        }

        /// <summary>
        /// 解析接口字符串为枚举
        /// </summary>
        private CommunicationInterface ParseInterface(string interfaceName)
        {
            if (Enum.TryParse<CommunicationInterface>(interfaceName, true, out var result))
                return result;
            return CommunicationInterface.None;
        }

        /// <summary>
        /// 检查是否支持特定接口
        /// </summary>
        public bool SupportsInterface(CommunicationInterface interfaceType)
        {
            return SupportedInterfaces.HasFlag(interfaceType);
        }
    }
}

