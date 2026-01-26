using System;
using System.Collections.ObjectModel;
using System.Linq;
using MeasureControl.Models;

namespace MeasureControl.Models.Devices
{
    /// <summary>
    /// 通用设备类（用于未明确分类的设备，作为兜底类）
    /// </summary>
    public class GenericDevice : DeviceBase
    {
        private string _deviceTypeName;

        public override string DeviceTypeName => _deviceTypeName ?? Name ?? "通用设备";

        public GenericDevice() : base()
        {
            DeviceType = "Card";
            _deviceTypeName = "通用设备";
            InitializeChildren();
        }

        public GenericDevice(string name, string slotPosition) : base()
        {
            DeviceType = "Card";
            ParseDeviceName(name);
            SlotPosition = slotPosition;
            InitializeChildren();
        }

        /// <summary>
        /// 设置设备类型名称（用于从DeviceBase的ParentNode获取）
        /// </summary>
        public void SetDeviceTypeName(string typeName)
        {
            _deviceTypeName = typeName;
        }

        public override void InitializeChildren()
        {
            Children.Clear();
            // 通用设备默认没有子节点，因为它只是一个兜底类
            // 具体的设备类型应该创建专门的设备类
        }

        public override ObservableCollection<DeviceInfoItem> GetDeviceInfoItems()
        {
            var items = new ObservableCollection<DeviceInfoItem>();

            // 使用FromDevice静态方法创建设备信息项,确保与deviceListBorder显示一致
            var deviceInfo = DeviceInfoItem.FromDevice(this, false);
            if (deviceInfo != null)
            {
                items.Add(deviceInfo);
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
                _deviceTypeName = "通用设备";
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
                // 没有空格时，直接使用原名称作为型号
                Name = deviceName;
                Manufacturer = "N/A";
                Model = deviceName;  // 使用完整名称作为型号，而不是"N/A"
            }

            // 根据设备名称推断设备类型名称
            _deviceTypeName = InferDeviceTypeName(deviceName);
        }

        /// <summary>
        /// 根据设备名称推断设备类型名称
        /// </summary>
        private string InferDeviceTypeName(string deviceName)
        {
            var lowerName = deviceName.ToLower();

            if (lowerName.Contains("控制器") || lowerName.Contains("controller")) return "控制器";
            if (lowerName.Contains("矩阵开关") || lowerName.Contains("matrix")) return "矩阵开关";
            if (lowerName.Contains("离散量") || lowerName.Contains("discrete")) return "离散量输入输出";
            if (lowerName.Contains("电阻输出") || lowerName.Contains("resistance")) return "电阻输出";
            if (lowerName.Contains("lvdt") || lowerName.Contains("rvdt")) return "LVDT/RVDT模拟";
            if (lowerName.Contains("旋转变压器") || lowerName.Contains("resolver")) return "旋转变压器模拟";
            if (lowerName.Contains("can")) return "CAN";
            if (lowerName.Contains("arinc429")) return "ARINC429";
            if (lowerName.Contains("1553b")) return "1553B";
            if (lowerName.Contains("1394b")) return "1394B";
            if (lowerName.Contains("lvds")) return "LVDS";
            if (lowerName.Contains("模拟量输出") || lowerName.Contains("analog output")) return "模拟量输出";
            if (lowerName.Contains("数字多用表") || lowerName.Contains("dmm")) return "数字多用表";
            if (lowerName.Contains("信号发生器") || lowerName.Contains("signal generator")) return "信号发生器";
            if (lowerName.Contains("频率计") || lowerName.Contains("frequency counter")) return "频率计";
            if (lowerName.Contains("串口") || lowerName.Contains("serial")) return "串口";

            return "通用设备";
        }

        public override string GetConnectionString()
        {
            return $"Generic::{Manufacturer}::{Model}::{SlotPosition}";
        }
    }
}
