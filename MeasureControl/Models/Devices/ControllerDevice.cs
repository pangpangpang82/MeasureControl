using System;
using System.Collections.ObjectModel;
using System.Linq;
using MeasureControl.Models;
using MeasureControl.Models.Devices.DeviceCategories;

namespace MeasureControl.Models.Devices
{
    /// <summary>
    /// 系统控制器设备类
    /// </summary>
    public class ControllerDevice : PxiDeviceBase
    {
        private int _slotsOccupied;
        private string _cpuModel;
        private string _memory;
        private string _storage;
        private string _operatingSystem;
        private string _interfaces;
        private string _pxieVersion;
        private int _pxieSlots;
        private double _maxPower;

        /// <summary>
        /// 占用槽位数
        /// </summary>
        public int SlotsOccupied
        {
            get => _slotsOccupied;
            set => SetProperty(ref _slotsOccupied, value);
        }

        /// <summary>
        /// CPU型号
        /// </summary>
        public string CpuModel
        {
            get => _cpuModel;
            set => SetProperty(ref _cpuModel, value);
        }

        /// <summary>
        /// 内存
        /// </summary>
        public string Memory
        {
            get => _memory;
            set => SetProperty(ref _memory, value);
        }

        /// <summary>
        /// 存储
        /// </summary>
        public string Storage
        {
            get => _storage;
            set => SetProperty(ref _storage, value);
        }

        /// <summary>
        /// 操作系统
        /// </summary>
        public string OperatingSystem
        {
            get => _operatingSystem;
            set => SetProperty(ref _operatingSystem, value);
        }

        /// <summary>
        /// 接口信息
        /// </summary>
        public string Interfaces
        {
            get => _interfaces;
            set => SetProperty(ref _interfaces, value);
        }

        /// <summary>
        /// PXIe版本
        /// </summary>
        public string PxieVersion
        {
            get => _pxieVersion;
            set => SetProperty(ref _pxieVersion, value);
        }

        /// <summary>
        /// PXIe插槽数
        /// </summary>
        public int PxieSlots
        {
            get => _pxieSlots;
            set => SetProperty(ref _pxieSlots, value);
        }

        /// <summary>
        /// 最大功耗 (W)
        /// </summary>
        public double MaxPower
        {
            get => _maxPower;
            set => SetProperty(ref _maxPower, value);
        }

        private string _deviceTypeName;

        public override string DeviceTypeName => _deviceTypeName ?? "系统控制器";

        public ControllerDevice() : base()
        {
            DeviceType = "Controller";
            ParentNode = "系统控制器";
            _deviceTypeName = "系统控制器";
            SlotsOccupied = 1; // 默认占用1个槽位（系统槽位）
            SlotIndex = 1; // 控制器固定为槽位1
            
            // 默认规格
            CpuModel = "Intel Core i7";
            Memory = "16GB DDR4";
            Storage = "512GB SSD";
            OperatingSystem = "Windows 10";
            Interfaces = "USB 3.0, GbE, RS-232";
            PxieVersion = "PXIe Gen3";
            PxieSlots = 3;
            MaxPower = 100;
            
            InitializeChildren();
        }

        public ControllerDevice(string name, string slotPosition) : base()
        {
            DeviceType = "Controller";
            ParentNode = "系统控制器";
            Model = "PXIe-3987";
            _deviceTypeName = "系统控制器";
            SlotsOccupied = 1; // 默认占用1个槽位
            SlotIndex = 1; // 控制器固定为槽位1
            
            ParseDeviceName(name);
            SlotPosition = slotPosition;
            
            // 根据型号设置规格
            SetSpecificationsByModel(name);
            
            InitializeChildren();
        }

        /// <summary>
        /// 设置设备类型名称（用于从DeviceBase的ParentNode获取）
        /// </summary>
        public void SetDeviceTypeName(string typeName)
        {
            _deviceTypeName = typeName;
        }

        /// <summary>
        /// 根据型号设置规格参数
        /// </summary>
        private void SetSpecificationsByModel(string modelName)
        {
            var lowerName = modelName?.ToLower() ?? "";

            // PXIe-3987 规格（凌华嵌入式控制器，占用1个系统槽位）
            if (lowerName.Contains("pxie-3987") || lowerName.Contains("pxi-3987"))
            {
                SlotsOccupied = 1; // 占用1个系统槽位
                CpuModel = "Intel Core i7-9700TE (8核, 1.8-3.8GHz)";
                Memory = "16GB DDR4-2666";
                Storage = "512GB M.2 SSD";
                OperatingSystem = "Windows 10 IoT Enterprise";
                Interfaces = "2x GbE, 4x USB 3.2, 2x USB 2.0, 2x RS-232, 2x DisplayPort 1.4, 1x VGA";
                PxieVersion = "PXIe Gen3";
                PxieSlots = 3;
                MaxPower = 100;
            }
            else
            {
                // 默认值
                CpuModel = "Intel Core i7";
                Memory = "16GB DDR4";
                Storage = "512GB SSD";
                OperatingSystem = "Windows 10";
                Interfaces = "USB 3.0, GbE, RS-232";
                PxieVersion = "PXIe Gen3";
                PxieSlots = 3;
                MaxPower = 100;
            }
        }

        public override void InitializeChildren()
        {
            Children.Clear();
            
            // 控制器设备可以添加子节点（如果需要的话）
            // 目前暂不添加子节点
        }

        public override ObservableCollection<DeviceInfoItem> GetDeviceInfoItems()
        {
            var items = new ObservableCollection<DeviceInfoItem>();

            // 使用FromDevice静态方法创建主设备信息项,确保与deviceListBorder显示一致
            var mainDeviceInfo = DeviceInfoItem.FromDevice(this, false);
            if (mainDeviceInfo != null)
            {
                items.Add(mainDeviceInfo);
            }

            // 控制器设备通常不需要显示详细规格（规格已在属性中体现）
            // 如果需要显示规格，可以使用规格系统

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
            return $"Controller::{Manufacturer}::{Model}::{SlotPosition}";
        }

        public override bool ValidateConfiguration()
        {
            return base.ValidateConfiguration() && 
                   SlotsOccupied > 0 && 
                   !string.IsNullOrEmpty(CpuModel);
        }
    }
}

