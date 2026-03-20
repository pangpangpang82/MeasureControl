using System;
using System.Collections.ObjectModel;
using System.Linq;
using MeasureControl.Constants;
using MeasureControl.Helpers;
using MeasureControl.Models;
using MeasureControl.Models.Devices.DeviceCategories;

namespace MeasureControl.Models.Devices
{
    /// <summary>
    /// 模拟量输出设备类
    /// </summary>
    public class AnalogOutputDevice : PxiDeviceBase
    {
        private int _channelCount;
        private double _sampleRate;
        private string _deviceTypeName;
        public override string DeviceTypeName => _deviceTypeName ?? "模拟量输出";

        /// <summary>
        /// 模拟量输出设备为输出型设备
        /// </summary>
        public override DeviceCapability Capability => DeviceCapability.Output;

        private AnalogOutputNode _analogOutputNode;
        private string _maxsampleRate;

        /// <summary>
        /// 通道数量
        /// </summary>
        public int ChannelCount
        {
            get => _channelCount;
            set => SetProperty(ref _channelCount, value);
        }

        /// <summary>
        /// 采样率 (Hz)
        /// </summary>
        public double SampleRate
        {
            get => _sampleRate;
            set => SetProperty(ref _sampleRate, value);
        }

        /// <summary>
        /// 最大采样率 (Hz)
        /// </summary>
        public string MaxSampleRate
        {
            get => _maxsampleRate;
            set => SetProperty(ref _maxsampleRate, value);
        }

        /// <summary>
        /// 模拟量输出子节点
        /// </summary>
        public AnalogOutputNode AoNode
        {
            get => _analogOutputNode;
            set => SetProperty(ref _analogOutputNode, value);
        }


        public AnalogOutputDevice() : base()
        {
            DeviceType = DeviceConstants.Type.Card;
            ParentNode = "模拟量输出";
            ChannelCount = 32;
            SampleRate = 1000;
            _deviceTypeName = "模拟量输出";
            InitializeChildren();
        }

        public AnalogOutputDevice(string name, string slotPosition) : base()
        {
            DeviceType = DeviceConstants.Type.Card;
            ParentNode = "模拟量输出";
            ChannelCount = 32;
            MaxSampleRate = "500k";
            Model = "MT-X532";
            SampleRate = 1000;
            _deviceTypeName = "模拟量输出";
            
            // 使用基类方法解析设备名称
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
            
            // 创建模拟量输出子节点
            AoNode = new AnalogOutputNode
            {
                Name = "模拟量输出",
                ParentNode = "模拟量输出",
                Model = $"{ChannelCount}通道",
                ChannelCount = ChannelCount,
                SampleRate = SampleRate,
                SlotPosition = $"AO0–AO{ChannelCount - 1}",  
                Status = DeviceConstants.Status.Normal
            };
            
            Children.Add(AoNode);
        }

        /// <summary>
        /// 初始化设备的通道集合
        /// </summary>
        public override void InitializeChannels()
        {
            base.InitializeChannels(); // 清空通道集合
            
            // 使用ChannelFactory创建模拟输出通道
            var channels = ChannelFactory.CreateAnalogOutputChannels(
                Id, 
                Name, 
                ChannelCount
            );
            
            // 添加到设备的通道集合
            foreach (var channel in channels)
            {
                Channels.Add(channel);
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
        /// 获取设备连接字符串
        /// </summary>
        public override string GetConnectionString()
        {
            return $"AnalogOutput::{Manufacturer}::{Model}::{SlotPosition}";
        }

        /// <summary>
        /// 验证设备配置
        /// </summary>
        public override bool ValidateConfiguration()
        {
            return base.ValidateConfiguration() &&
                   ChannelCount > 0 &&
                   SampleRate > 0;
        }
    }

    /// <summary>
    /// 模拟量输出子节点
    /// </summary>
    public class AnalogOutputNode : DeviceBase
    {
        private int _channelCount;
        private double _sampleRate;
        public override string DeviceTypeName => "模拟量输出";

        /// <summary>
        /// 通道数量
        /// </summary>
        public int ChannelCount
        {
            get => _channelCount;
            set => SetProperty(ref _channelCount, value);
        }

        /// <summary>
        /// 采样率
        /// </summary>
        public double SampleRate
        {
            get => _sampleRate;
            set => SetProperty(ref _sampleRate, value);
        }

        public AnalogOutputNode()
        {
            DeviceType = Constants.DeviceConstants.Type.SubNode;
            ParentNode = "模拟量输出";
            Model = "32通道";
            ChannelCount = 32;
            SampleRate = 1000;
            SlotPosition = "N/A";
            Status = "N/A";
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
            return $"AnalogOutput::{ChannelCount}::{SampleRate}";
        }
    }
}

