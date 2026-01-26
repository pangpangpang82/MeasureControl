using System;
using System.Collections.ObjectModel;
using System.Linq;
using MeasureControl.Models;
using MeasureControl.Models.Devices.DeviceCategories;

namespace MeasureControl.Models.Devices
{
    /// <summary>
    /// 机箱设备类
    /// </summary>
    public class ChassisDevice : ChassisDeviceBase
    {
        private int _slotCount;
        private string _chassisModel;

        public override string DeviceTypeName => $"{SlotCount}槽机箱";

        /// <summary>
        /// 插槽数量
        /// </summary>
        public new int SlotCount
        {
            get => _slotCount;
            set => SetProperty(ref _slotCount, value);
        }

        /// <summary>
        /// 机箱型号
        /// </summary>
        public string ChassisModel
        {
            get => _chassisModel;
            set => SetProperty(ref _chassisModel, value);
        }

        public ChassisDevice() : base()
        {
            DeviceType = "Chassis"; 
            InitializeChildren();
        }

        public ChassisDevice(string name) : base()
        {
            ChassisModel = name;
            Description = "";

            var config = Helpers.ChassisFactory.GetChassisConfig(name);
            if (config != null)
            {
                Manufacturer = config.Manufacturer;
                Model = config.Model;
                CardName = ""; // 暂时设置为空，后续在服务中设置为机箱名称
                SlotCount = config.SlotCount;
            }
            else
            {
                SlotCount = DetermineSlotCount(name);
                ParseDeviceName(name);
            }

            ParentNode = DeviceTypeName;

            InitializeChildren();
        }

        /// <summary>
        /// 根据机箱名称判断槽位数
        /// </summary>
        private int DetermineSlotCount(string chassisName)
        {
            return Helpers.ChassisFactory.GetSlotCount(chassisName);
        }

        public override void InitializeChildren()
        {
            // 机箱设备初始化时没有子设备，子设备通过拖拽添加
            Children.Clear();
        }

        public override ObservableCollection<DeviceInfoItem> GetDeviceInfoItems()
        {
            var items = new ObservableCollection<DeviceInfoItem>();

            var mainDeviceInfo = DeviceInfoItem.FromDevice(this, false);
            if (mainDeviceInfo != null)
            {
                items.Add(mainDeviceInfo);
            }

            return items;
        }

        // 解析设备名称以提取制造商和型号
        private new void ParseDeviceName(string deviceName)
        {
            var parts = deviceName.Split(' ');
            if (parts.Length >= 2)
            {
                Manufacturer = parts[0];
                Model = string.Join(" ", parts.Skip(1));
                Name = deviceName;
            }
            
            Name = deviceName;
            Manufacturer = "N/A";
            Model = "N/A";
        }

        /// <summary>
        /// 获取机箱连接字符串
        /// </summary>
        public override string GetConnectionString()
        {
            return $"Chassis::{Manufacturer}::{Model}::{SlotCount}";
        }

        /// <summary>
        /// 验证机箱配置
        /// </summary>
        public override bool ValidateConfiguration()
        {
            return base.ValidateConfiguration() && 
                   SlotCount > 0 && 
                   !string.IsNullOrEmpty(ChassisModel);
        }
    }
}
