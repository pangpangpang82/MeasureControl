using System;
using System.Collections.ObjectModel;
using MeasureControl.Models.Devices;
using Prism.Mvvm;

namespace MeasureControl.Models
{
    /// <summary>
    /// 机箱模型类
    /// 通过配置数据驱动不同型号的参数
    /// </summary>
    public class ChassisModel : BindableBase
    {
        private string _name;
        private int _gridRow;
        private int _gridColumn;
        private bool _isSelected;
        private ObservableCollection<DeviceBase> _devices;
        private string _ipAddress;
        private string _subnetMask;
        private string _connectionStatus;
        private int _slotCount;
        private string _manufacturer;
        private string _model;
        private string _chassisType;
        private string _df1;
        private string _df2;
        //private string _genVersion;
        //private double _systemBandwidth;
        //private double _slotPowerCapacity;

        /// <summary>
        /// 机箱名称
        /// </summary>
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        /// <summary>
        /// 网格行位置
        /// </summary>
        public int GridRow
        {
            get => _gridRow;
            set => SetProperty(ref _gridRow, value);
        }

        /// <summary>
        /// 网格列位置
        /// </summary>
        public int GridColumn
        {
            get => _gridColumn;
            set => SetProperty(ref _gridColumn, value);
        }

        /// <summary>
        /// 是否选中
        /// </summary>
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        /// <summary>
        /// 设备集合
        /// </summary>
        public ObservableCollection<DeviceBase> Devices
        {
            get => _devices;
            set => SetProperty(ref _devices, value);
        }

        /// <summary>
        /// 机箱唯一标识
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// IP地址
        /// </summary>
        public string IpAddress
        {
            get => _ipAddress;
            set => SetProperty(ref _ipAddress, value);
        }

        /// <summary>
        /// 子网掩码
        /// </summary>
        public string SubnetMask
        {
            get => _subnetMask;
            set => SetProperty(ref _subnetMask, value);
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
        /// 槽位数量
        /// </summary>
        public int SlotCount
        {
            get => _slotCount;
            set => SetProperty(ref _slotCount, value);
        }

        /// <summary>
        /// 制造商
        /// </summary>
        public string Manufacturer
        {
            get => _manufacturer;
            set => SetProperty(ref _manufacturer, value);
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
        /// 机箱类型
        /// </summary>
        public string ChassisType
        {
            get => _chassisType;
            set => SetProperty(ref _chassisType, value);
        }

        ///// <summary>
        ///// Gen版本（如：Gen2、Gen3）
        ///// </summary>
        //public string GenVersion
        //{
        //    get => _genVersion;
        //    set => SetProperty(ref _genVersion, value);
        //}

        ///// <summary>
        ///// 系统带宽（GB/s）
        ///// </summary>
        //public double SystemBandwidth
        //{
        //    get => _systemBandwidth;
        //    set => SetProperty(ref _systemBandwidth, value);
        //}

        ///// <summary>
        ///// 单槽功率容量（W）
        ///// </summary>
        //public double SlotPowerCapacity
        //{
        //    get => _slotPowerCapacity;
        //    set => SetProperty(ref _slotPowerCapacity, value);
        //}

        /// <summary>
        /// 占位符1
        /// </summary>
        public string DF1
        {
            get => _df1;
            set => SetProperty(ref _df1, value);
        }

        /// <summary>
        /// 占位符2
        /// </summary>
        public string DF2
        {
            get => _df2;
            set => SetProperty(ref _df2, value);
        }

        /// <summary>
        /// 默认构造函数
        /// </summary>
        public ChassisModel()
        {
            Id = Guid.NewGuid().ToString();
            Devices = new ObservableCollection<DeviceBase>();
            ConnectionStatus = "未连接";
        }

        /// <summary>
        /// 简化构造函数
        /// </summary>
        public ChassisModel(string name, int row, int column) : this()
        {
            Name = name;
            GridRow = row;
            GridColumn = column;
        }

        /// <summary>
        /// 完整构造函数
        /// </summary>
        public ChassisModel(string name, int row, int column, string manufacturer, string model,
            int slotCount, string df1, string df2) : this(name, row, column)
        {
            Manufacturer = manufacturer;
            Model = model;
            ChassisType = model; // ChassisType 与 Model 保持一致
            SlotCount = slotCount;
            DF1 = df1;
            DF2 = df2;
            //GenVersion = genVersion;
            //SystemBandwidth = systemBandwidth;
            //SlotPowerCapacity = slotPowerCapacity;
        }

        ///// <summary>
        ///// 获取可用槽位数
        ///// </summary>
        //public int GetAvailableSlots()
        //{
        //    int usedSlots = 0;
        //    foreach (var device in Devices)
        //    {
        //        if (device.DeviceType == "Card")
        //        {
        //            usedSlots++;
        //        }
        //    }
        //    return SlotCount - usedSlots;
        //}

        ///// <summary>
        ///// 检查是否有可用槽位
        ///// </summary>
        //public bool HasAvailableSlot()
        //{
        //    return GetAvailableSlots() > 0;
        //}

        /// <summary>
        /// 验证配置
        /// </summary>
        public virtual bool ValidateConfiguration()
        {
            return !string.IsNullOrEmpty(Name) &&
                   !string.IsNullOrEmpty(Manufacturer) &&
                   !string.IsNullOrEmpty(Model) &&
                   SlotCount > 0;
        }

        /// <summary>
        /// 检查机箱是否有系统控制器
        /// </summary>
        public bool HasController()
        {
            if (Devices == null) return false;

            foreach (var device in Devices)
            {
                // 直接检查设备类型
                if (device is ControllerDevice)
                    return true;

                // 检查机箱设备的子设备
                if (device is ChassisDevice chassisDevice && chassisDevice.Children != null)
                {
                    foreach (var child in chassisDevice.Children)
                    {
                        if (child is ControllerDevice)
                            return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// 获取系统控制器设备
        /// </summary>
        public ControllerDevice GetController()
        {
            if (Devices == null) return null;

            foreach (var device in Devices)
            {
                if (device is ControllerDevice controller)
                    return controller;

                if (device is ChassisDevice chassisDevice && chassisDevice.Children != null)
                {
                    foreach (var child in chassisDevice.Children)
                    {
                        if (child is ControllerDevice childController)
                            return childController;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// 检查是否有除控制器外的其他板卡
        /// </summary>
        public bool HasOtherCards()
        {
            if (Devices == null) return false;

            foreach (var device in Devices)
            {
                if (device is ChassisDevice chassisDevice && chassisDevice.Children != null)
                {
                    foreach (var child in chassisDevice.Children)
                    {
                        // 如果是板卡设备但不是控制器
                        if (child.DeviceType == "Card" && !(child is ControllerDevice))
                            return true;
                    }
                }
            }

            return false;
        }
    }
}

