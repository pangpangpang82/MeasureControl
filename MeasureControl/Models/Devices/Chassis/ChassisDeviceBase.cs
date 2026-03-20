using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace MeasureControl.Models.Devices.DeviceCategories
{
    /// <summary>
    /// 机箱设备抽象基类
    /// </summary>
    public abstract class ChassisDeviceBase : DeviceBase
    {
        private int _slotCount;
        //private string _genVersion;
        //private double _systemBandwidth;
        private bool _hasController;

        /// <summary>
        /// 槽位数量
        /// </summary>
        public int SlotCount
        {
            get => _slotCount;
            set => SetProperty(ref _slotCount, value);
        }

        ///// <summary>
        ///// 总线版本（Gen2、Gen3等）
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

        /// <summary>
        /// 是否包含系统控制器
        /// </summary>
        public bool HasController
        {
            get => _hasController;
            set => SetProperty(ref _hasController, value);
        }

        /// <summary>
        /// 默认构造函数
        /// </summary>
        protected ChassisDeviceBase() : base()
        {
            DeviceType = "Chassis";
            SlotCount = 18;
            //MaxPower = 1500;
            //GenVersion = "Gen3";
            //SystemBandwidth = 8.0;
            HasController = false;
        }

        /// <summary>
        /// 带参数构造函数
        /// </summary>
        protected ChassisDeviceBase(string name, string manufacturer, string model, string slotPosition) 
            : base(name, manufacturer, model, slotPosition)
        {
            DeviceType = "Chassis";
            SlotCount = 18;
            //MaxPower = 1500;
            //GenVersion = "Gen3";
            //SystemBandwidth = 8.0;
            HasController = false;
        }

        ///// <summary>
        ///// 获取可用槽位数量
        ///// </summary>
        //public virtual int GetAvailableSlotCount()
        //{
        //    if (Children == null)
        //        return SlotCount;

        //    int usedSlots = Children.Count(c => c.DeviceType == "Card");
        //    return SlotCount - usedSlots;
        //}

        ///// <summary>
        ///// 检查是否有可用槽位
        ///// </summary>
        //public virtual bool HasAvailableSlot()
        //{
        //    return GetAvailableSlotCount() > 0;
        //}

        ///// <summary>
        ///// 检查槽位是否被占用
        ///// </summary>
        //public virtual bool IsSlotOccupied(string slotPosition)
        //{
        //    if (Children == null || string.IsNullOrEmpty(slotPosition))
        //        return false;

        //    return Children.Any(c => c.SlotPosition == slotPosition && c.DeviceType == "Card");
        //}

        /// <summary>
        /// 检查是否包含系统控制器板卡
        /// </summary>
        public virtual bool ContainsController()
        {
            if (Children == null)
                return false;

            return Children.Any(c => c is ControllerDevice);
        }

        /// <summary>
        /// 验证机箱约束条件
        /// </summary>
        public virtual bool ValidateControllerConstraint()
        {
            if (Children == null || Children.Count == 0)
                return true; // 空机箱，无约束

            bool hasController = ContainsController();
            bool hasOtherCards = Children.Any(c => c.DeviceType == "Card" && !(c is ControllerDevice));

            // 如果有其他板卡，必须有控制器
            if (hasOtherCards && !hasController)
                return false;

            return true;
        }

        /// <summary>
        /// 验证机箱配置
        /// </summary>
        public override bool ValidateConfiguration()
        {
            if (!base.ValidateConfiguration())
                return false;

            // 验证槽位数量
            if (SlotCount <= 0)
                return false;

            // 验证控制器约束
            if (!ValidateControllerConstraint())
                return false;

            return true;
        }

        /// <summary>
        /// 获取机箱连接字符串
        /// </summary>
        public override string GetConnectionString()
        {
            return $"Chassis::{Manufacturer}::{Model}::{SlotCount}";
        }
    }
}

