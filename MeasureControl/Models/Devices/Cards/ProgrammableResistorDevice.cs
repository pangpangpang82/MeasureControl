using System;
using System.Collections.ObjectModel;
using System.Linq;
using MeasureControl.Constants;
using MeasureControl.Models.Devices.DeviceCategories;

namespace MeasureControl.Models.Devices
{
    
    /// <summary>
    /// 可编程电阻设备类
    /// </summary>
    public class ProgrammableResistorDevice : PxiDeviceBase
    {

        private int _channelCount;
        private int _minResistance;
        private int _maxResistance;
        private ProgrammableResistorNode _programmableResistorNode;
        public override string DeviceTypeName => "可编程电阻";

        /// <summary>
        /// 通道数量
        /// </summary>
        public int ChannelCount
        {
            get => _channelCount;
            set => SetProperty(ref _channelCount, value);
        }

        /// <summary>
        /// 最小电阻值
        /// </summary>
        public int MinResistance { 
            get => _minResistance; 
            set => SetProperty(ref _minResistance, value);
        }

        /// <summary>
        /// 最大电阻值
        /// </summary>
        public int MaxResistance
        {
            get => _maxResistance;
            set => SetProperty(ref _maxResistance, value);
        }

        /// <summary>
        /// 可编程电阻子节点
        /// </summary>
        public ProgrammableResistorNode PRNode
        {
            get => _programmableResistorNode;
            private set => SetProperty(ref _programmableResistorNode, value);
        }

        /// <summary>
        /// 默认构造函数
        /// </summary>
        public ProgrammableResistorDevice() : base()
        {
            DeviceType = DeviceConstants.Type.Card;
            InitializeChildren();
        }

        /// <summary>
        /// 带参数的构造函数
        /// </summary>
        public ProgrammableResistorDevice(string name, string slotPosition) : base()
        {
            DeviceType = DeviceConstants.Type.Card;
            MinResistance = 2;
            ChannelCount = 9;
            MaxResistance = 6700;
            Model = "PXI-7012";
            
            ParseDeviceName(name);
            SlotPosition = slotPosition;
            
            InitializeChildren();
        }

        /// <summary>
        /// 初始化子节点
        /// </summary>
        public override void InitializeChildren()
        {
            Children.Clear();

            // 创建可编程电阻子节点
            PRNode = new ProgrammableResistorNode
            {
                Name = "可编程电阻",
                ParentNode = "可编程电阻",
                Model = $"{ChannelCount}通道",
                ChannelCount = ChannelCount,
                SlotPosition = $"RO0–RO{ChannelCount - 1}",
                Status = DeviceConstants.Status.Normal
            };
            
            Children.Add(PRNode);
        }

        /// <summary>
        /// 获取设备信息项列表
        /// </summary>
        public override ObservableCollection<DeviceInfoItem> GetDeviceInfoItems()
        {
            var items = new ObservableCollection<DeviceInfoItem>();
            var mainDeviceInfo = DeviceInfoItem.FromDevice(this, false);
            if (mainDeviceInfo != null)
            {
                items.Add(mainDeviceInfo);
            }

            // 模拟量输入子节点信息
            foreach (var child in Children)
            {
                var subNodeInfo = DeviceInfoItem.FromDevice(child, true);
                if (subNodeInfo != null)
                {
                    items.Add(subNodeInfo);
                }
            }

            return items;
        }

        /// <summary>
        /// 获取连接字符串
        /// </summary>
        public override string GetConnectionString()
        {
            return $"ProgrammableResistor::{Manufacturer}::{Model}::{SlotPosition}";
        }



        /// <summary>
        /// 验证配置是否有效
        /// </summary>
        /// <returns>配置是否有效</returns>
        public override bool ValidateConfiguration()
        {
            return base.ValidateConfiguration() &&
                   ChannelCount > 0;
        }

    }

    /// <summary>
    /// 可编程电阻子节点
    /// </summary>
    public class ProgrammableResistorNode : DeviceBase
    {
        private int _channelCount;

        /// <summary>
        /// 通道数量
        /// </summary>
        public int ChannelCount
        {
            get => _channelCount;
            set => SetProperty(ref _channelCount, value);
        }

        public override string DeviceTypeName => "可编程电阻";

        public ProgrammableResistorNode()
        {
            DeviceType = Constants.DeviceConstants.Type.SubNode;
            ParentNode = "可编程电阻";
            Model = "9通道";
            ChannelCount = 9;
            Status = DeviceConstants.Status.Normal;
        }

        public override void InitializeChildren()
        {
            Children.Clear();
        }

        public override ObservableCollection<DeviceInfoItem> GetDeviceInfoItems()
        {
            var items = new ObservableCollection<DeviceInfoItem>();
            var subNodeInfo = DeviceInfoItem.FromDevice(this, true);
            if (subNodeInfo != null)
            {
                items.Add(subNodeInfo);
            }
            return items;
        }

        public override string GetConnectionString()
        {
            return $"ProgrammableResistor::{ChannelCount}";
        }
    }
}

