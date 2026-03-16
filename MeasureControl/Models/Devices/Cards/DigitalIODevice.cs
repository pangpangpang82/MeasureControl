using System;
using System.Collections.ObjectModel;
using System.Linq;
using MeasureControl.Constants;
using MeasureControl.Helpers;
using MeasureControl.Models.Devices.DeviceCategories;

namespace MeasureControl.Models.Devices
{
    /// <summary>
    /// 离散量输入输出类
    /// </summary>
    public class DigitalIODevice : PxiDeviceBase
    {
        private int _inputChannels;
        private int _outputChannels;
        private string _deviceTypeName;
        public override string DeviceTypeName => _deviceTypeName ?? "离散量输入输出";

        /// <summary>
        /// 数字I/O设备为双向设备
        /// </summary>
        public override DeviceCapability Capability => DeviceCapability.Bidirectional;

        private DigitalInputNode _digitalInputNode;
        private DigitalOutputNode _digitalOutputNode;
        


        /// <summary>
        /// 输入通道数
        /// </summary>
        public int InputChannels
        {
            get => _inputChannels;
            set => SetProperty(ref _inputChannels, value);
        }

        /// <summary>
        /// 输出通道数
        /// </summary>
        public int OutputChannels
        {
            get => _outputChannels;
            set => SetProperty(ref _outputChannels, value);
        }

        /// <summary>
        /// 离散量输入子节点
        /// </summary>
        public DigitalInputNode DiNode
        {
            get => _digitalInputNode;
            set => SetProperty(ref _digitalInputNode, value);
        }

        /// <summary>
        /// 离散量输出子节点
        /// </summary>
        public DigitalOutputNode DoNode
        {
            get => _digitalOutputNode;
            set => SetProperty(ref _digitalOutputNode, value);
        }

        public DigitalIODevice() : base()
        {
            DeviceType = DeviceConstants.Type.Card;
            InputChannels = 32;
            OutputChannels = 32;
            InitializeChildren();
        }

        public DigitalIODevice(string name, string slotPosition) : base()
        {
            DeviceType = DeviceConstants.Type.Card;
            Model = "PXIe-7131";
            InputChannels = 32;
            OutputChannels = 32;

            ParseDeviceName(name);
            SlotPosition = slotPosition;

            InitializeChildren();
        }

        /// <summary>
        /// 设置设备类型名称
        /// </summary>
        public void SetDeviceTypeName(string typeName)
        {
            _deviceTypeName = typeName;
        }

        public override void InitializeChildren()
        {
            Children.Clear();

            // 创建DI子节点
            DiNode = new DigitalInputNode
            {
                Name = "离散量输入",
                ParentNode = "离散量IO",
                ChannelCount = InputChannels,
                Model = $"{InputChannels}通道",
                SlotPosition = $"DI0–DI{InputChannels - 1}",
                Status = DeviceConstants.Status.Normal
            };
            Children.Add(DiNode);

            // 创建DO子节点
            DoNode = new DigitalOutputNode
            {
                Name = "离散量输出",
                ParentNode = "离散量IO",
                ChannelCount = OutputChannels,
                Model = $"{OutputChannels}通道",
                SlotPosition = $"DO0–DO{OutputChannels - 1}",
                Status = DeviceConstants.Status.Normal
            };
            Children.Add(DoNode);
        }

        /// <summary>
        /// 初始化设备的通道集合
        /// </summary>
        public override void InitializeChannels()
        {
            base.InitializeChannels(); // 清空通道集合
            
            // 创建数字输入通道
            if (InputChannels > 0)
            {
                var diChannels = ChannelFactory.CreateDigitalInputChannels(
                    Id, 
                    Name, 
                    InputChannels
                );
                
                foreach (var channel in diChannels)
                {
                    Channels.Add(channel);
                }
            }
            
            // 创建数字输出通道
            if (OutputChannels > 0)
            {
                var doChannels = ChannelFactory.CreateDigitalOutputChannels(
                    Id, 
                    Name, 
                    OutputChannels
                );
                
                foreach (var channel in doChannels)
                {
                    Channels.Add(channel);
                }
            }
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
        /// 获取设备连接字符串
        /// </summary>
        public override string GetConnectionString()
        {
            return $"DigitalIO::{Manufacturer}::{Model}::{SlotPosition}";
        }

        /// <summary>
        /// 验证设备配置
        /// </summary>
        public override bool ValidateConfiguration()
        {
            return base.ValidateConfiguration() &&
                   InputChannels > 0 &&
                   OutputChannels > 0;
        }
    }

    /// <summary>
    /// 离散量输入子节点
    /// </summary>
    public class DigitalInputNode : DeviceBase
    {
        private int _channelCount;

        public int ChannelCount
        {
            get => _channelCount;
            set => SetProperty(ref _channelCount, value);
        }

        public override string DeviceTypeName => "离散量输入";

        public DigitalInputNode()
        {
            DeviceType = "SubNode";
            ParentNode = "离散量IO";
            ChannelCount = 16;
            SlotPosition = "DI0–DI15";
            Status = "正常";
        }

        public override void InitializeChildren()
        {
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
            return $"DigitalInput::{ChannelCount}CH";
        }
    }

    /// <summary>
    /// 离散量输出子节点
    /// </summary>
    public class DigitalOutputNode : DeviceBase
    {
        private int _channelCount;

        public int ChannelCount
        {
            get => _channelCount;
            set => SetProperty(ref _channelCount, value);
        }

        public override string DeviceTypeName => "离散量输出";

        public DigitalOutputNode()
        {
            DeviceType = "SubNode";
            ParentNode = "离散量IO";
            ChannelCount = 16;
            SlotPosition = "DO0–DO15";
            Status = "正常";
        }

        public override void InitializeChildren()
        {
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
            return $"DigitalOutput::{ChannelCount}CH";
        }
    }
}

