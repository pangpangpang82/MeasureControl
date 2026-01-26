using System;
using Prism.Mvvm;
using MeasureControl.Models.Devices;

namespace MeasureControl.Models
{
    /// <summary>
    /// 设备详细信息项，用于在deviceinfo区域显示设备信息
    /// </summary>
    public class DeviceInfoItem : BindableBase
    {
        private string _label;
        private string _value;
        private string _name;
        private string _deviceName;
        private string _model;
        private string _slotPosition;
        private string _status;
        private string _cardType;
        private bool _isSubNode;
        private string _height;
        private string _deviceType;
        private string _ipAddress;

        /// <summary>
        /// 标签（如"设备名称"、"型号"等）
        /// </summary>
        public string Label
        {
            get => _label;
            set => SetProperty(ref _label, value);
        }

        /// <summary>
        /// 值（如具体的设备名称、型号等）
        /// </summary>
        public string Value
        {
            get => _value;
            set => SetProperty(ref _value, value);
        }

        /// <summary>
        /// 展示名称（左侧第一列）
        /// </summary>
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        /// <summary>
        /// 设备名称
        /// </summary>
        public string DeviceName
        {
            get => _deviceName;
            set => SetProperty(ref _deviceName, value);
        }

        /// <summary>
        /// 型号
        /// </summary>
        public string Model
        {
            get => _model;
            set => SetProperty(ref _model, value);
        }

        /// <summary>
        /// 插槽位置
        /// </summary>
        public string SlotPosition
        {
            get => _slotPosition;
            set => SetProperty(ref _slotPosition, value);
        }

        /// <summary>
        /// 状态
        /// </summary>
        public string Status
        {
            get => _status;
            set => SetProperty(ref _status, value);
        }

        /// <summary>
        /// 板卡类型（如"模拟量采集"、"数字量采集"等）
        /// </summary>
        public string CardType
        {
            get => _cardType;
            set => SetProperty(ref _cardType, value);
        }

        /// <summary>
        /// 是否为子节点
        /// </summary>
        public bool IsSubNode
        {
            get => _isSubNode;
            set => SetProperty(ref _isSubNode, value);
        }

        /// <summary>
        /// 高度设置（如"ChassisHeight"、"CardHeight"等）
        /// </summary>
        public string Height
        {
            get => _height;
            set => SetProperty(ref _height, value);
        }

        /// <summary>
        /// 设备类型（如"Chassis"、"Card"、"Instrument"等）
        /// </summary>
        public string DeviceType
        {
            get => _deviceType;
            set => SetProperty(ref _deviceType, value);
        }

        /// <summary>
        /// IP地址（用于机箱设备）
        /// </summary>
        public string IpAddress
        {
            get => _ipAddress;
            set => SetProperty(ref _ipAddress, value);
        }

        public DeviceInfoItem()
        {
        }

        public DeviceInfoItem(string label, string value, bool isSubNode = false)
        {
            Label = label;
            Value = value;
            Name = label;
            IsSubNode = isSubNode;
        }

        public DeviceInfoItem(string label, string value, bool isSubNode, string deviceType)
        {
            Label = label;
            Value = value;
            Name = label;
            IsSubNode = isSubNode;
            DeviceType = deviceType;
        }

        /// <summary>
        /// 创建板卡信息项
        /// </summary>
        /// <param name="cardType">板卡类型（如"模拟量采集"）</param>
        /// <param name="model">板卡型号</param>
        /// <param name="slotPosition">板卡插槽位置</param>
        /// <param name="status">板卡状态</param>
        public DeviceInfoItem(string cardType, string model, string slotPosition, string status)
        {
            CardType = cardType;
            Name = cardType;
            Model = model;
            SlotPosition = slotPosition;
            Status = status;
            IsSubNode = false;
        }

        /// <summary>
        /// 创建板卡信息项（用于新的设备类系统）
        /// </summary>
        /// <param name="cardType">板卡类型</param>
        /// <param name="model">板卡型号</param>
        /// <param name="slotPosition">板卡插槽位置</param>
        /// <param name="status">板卡状态</param>
        /// <param name="isSubNode">是否为子节点</param>
        /// <param name="deviceType">设备类型</param>
        public DeviceInfoItem(string cardType, string model, string slotPosition, string status, bool isSubNode, string deviceType)
        {
            CardType = cardType;
            Name = cardType;
            Model = model;
            SlotPosition = slotPosition;
            Status = status;
            IsSubNode = isSubNode;
            DeviceType = deviceType;
        }

        /// <summary>
        /// 从DeviceBase创建设备信息项（确保与deviceListBorder显示一致）
        /// </summary>
        /// <param name="device">设备对象</param>
        /// <param name="isSubNode">是否为子节点</param>
        /// <returns>设备信息项</returns>
        public static DeviceInfoItem FromDevice(DeviceBase device, bool isSubNode = false)
        {
            if (device == null) return null;

            string cardType;
            string name;

            if (!isSubNode)
            {
                // 主设备行
                if (device.DeviceType == "Chassis")
                {
                    // 机箱设备：CardType显示设备类型，Name显示机箱名称
                    cardType = device.DeviceTypeName;
                    name = device.PrimaryDisplayName;
                }
                else
                {
                    // 其他设备：使用设备类型名称（板卡类型）
                    // 例如 "模拟量采集"、"CAN总线"、"LVDT/RVDT模拟测量" 等
                    cardType = device.CardName;
                    name = device.PrimaryDisplayName;
                }
            }
            else
            {
                // 子节点行：使用节点自身的 Name（功能/分组名称）
                // 例如 "模拟量输入"、"CAN总线"、"LVDT"、"旋变" 等
                cardType = !string.IsNullOrWhiteSpace(device.Name)
                    ? device.Name
                    : device.DeviceTypeName;
                name = device.Name;
            }

            return new DeviceInfoItem
            {
                Name = name,
                CardType = cardType,
                Model = device.Model,
                SlotPosition = string.Equals(device.DeviceType, "Instrument", StringComparison.Ordinal)
                    ? device.ConnectionMethod
                    : device.SlotPosition,
                Status = device.Status,
                IsSubNode = isSubNode,
                DeviceType = device.DeviceType
            };
        }

        /// <summary>
        /// 创建设备属性信息项
        /// </summary>
        public static DeviceInfoItem CreateDeviceProperty(string label, string value)
        {
            return new DeviceInfoItem(label, value, false, "Card");
        }

        /// <summary>
        /// 创建子节点信息项
        /// </summary>
        public static DeviceInfoItem CreateSubNodeProperty(string label, string value)
        {
            return new DeviceInfoItem(label, value, true, "Card");
        }

        /// <summary>
        /// 创建板卡信息项
        /// </summary>
        /// <param name="cardType">板卡类型（如"模拟量采集"）</param>
        /// <param name="model">板卡型号</param>
        /// <param name="slotPosition">板卡插槽位置</param>
        /// <param name="status">板卡状态</param>
        public static DeviceInfoItem CreateCardInfo(string cardType, string model, string slotPosition, string status)
        {
            return new DeviceInfoItem(cardType, model, slotPosition, status, false, "Card");
        }
    }
}
