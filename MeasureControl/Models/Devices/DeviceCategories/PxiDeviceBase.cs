using System;
using System.Collections.ObjectModel;

namespace MeasureControl.Models.Devices.DeviceCategories
{
    /// <summary>
    /// PXI/PXIe板卡设备抽象基类
    /// 所有插入PXI机箱的板卡设备继承此类
    /// </summary>
    public abstract class PxiDeviceBase : DeviceBase
    {
        private string _busType;
        private int _busBandwidth;
        private bool _requiresController;
        private string _triggerLines;
        private string _clockSource;
        private int _slotIndex = -1;

        /// <summary>
        /// 总线类型（PXI、PXIe等）
        /// </summary>
        public string BusType
        {
            get => _busType;
            set => SetProperty(ref _busType, value);
        }

        /// <summary>
        /// 总线带宽（MB/s）
        /// </summary>
        public int BusBandwidth
        {
            get => _busBandwidth;
            set => SetProperty(ref _busBandwidth, value);
        }

        /// <summary>
        /// 是否需要系统控制器
        /// </summary>
        public bool RequiresController
        {
            get => _requiresController;
            set => SetProperty(ref _requiresController, value);
        }

        /// <summary>
        /// 支持的触发线
        /// </summary>
        public string TriggerLines
        {
            get => _triggerLines;
            set => SetProperty(ref _triggerLines, value);
        }

        /// <summary>
        /// 时钟源类型
        /// </summary>
        public string ClockSource
        {
            get => _clockSource;
            set => SetProperty(ref _clockSource, value);
        }

        /// <summary>
        /// 机箱中的槽位编号（1开始，控制器固定为1，其他板卡从2开始）
        /// </summary>
        public int SlotIndex
        {
            get => _slotIndex;
            set => SetProperty(ref _slotIndex, value);
        }

        /// <summary>
        /// 默认构造函数
        /// </summary>
        protected PxiDeviceBase() : base()
        {
            DeviceType = "Card";
            BusType = "PXIe";
            BusBandwidth = 132; // PXIe默认带宽132 MB/s
            RequiresController = true;
            TriggerLines = "PXI_TRIG<0..7>";
            ClockSource = "Internal";
        }

        /// <summary>
        /// 带参数构造函数
        /// </summary>
        protected PxiDeviceBase(string name, string manufacturer, string model, string slotPosition) 
            : base(name, manufacturer, model, slotPosition)
        {
            DeviceType = "Card";
            BusType = "PXIe";
            BusBandwidth = 132;
            RequiresController = true;
            TriggerLines = "PXI_TRIG<0..7>";
            ClockSource = "Internal";
        }

        /// <summary>
        /// 验证PXI设备配置
        /// </summary>
        public override bool ValidateConfiguration()
        {
            if (!base.ValidateConfiguration())
                return false;

            // PXI设备必须有槽位位置
            if (string.IsNullOrEmpty(SlotPosition) || SlotPosition == "N/A")
                return false;

            return true;
        }

        /// <summary>
        /// 获取PXI设备连接字符串
        /// </summary>
        public override string GetConnectionString()
        {
            return $"PXI::{BusType}::{SlotPosition}::{Manufacturer}::{Model}";
        }

        /// <summary>
        /// 判断是否为PXIe混合插槽
        /// </summary>
        public virtual bool IsHybridSlotCompatible()
        {
            // PXIe设备默认兼容混合插槽
            return BusType == "PXIe";
        }

        /// <summary>
        /// 获取设备功耗估算（W）
        /// </summary>
        public virtual double GetPowerConsumption()
        {
            // 默认估算值，子类应根据实际情况覆盖
            return 15.0;
        }
    }
}

