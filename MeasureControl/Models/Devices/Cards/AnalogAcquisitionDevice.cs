using System;
using System.Collections.ObjectModel;
using System.Linq;
using JY7131;
using MeasureControl.Constants;
using MeasureControl.Helpers;
using MeasureControl.Models;
using MeasureControl.Models.Devices.DeviceCategories;

namespace MeasureControl.Models.Devices
{
    /// <summary>
    /// 模拟量采集设备类
    /// </summary>
    public class AnalogAcquisitionDevice : PxiDeviceBase
    {
        private int _channelCount;
        private double _sampleRate;
        private string _inputRange;
        private string _maxsampleRate;


        private string _deviceTypeName;
        public override string DeviceTypeName => _deviceTypeName ?? "模拟量采集";

        /// <summary>
        /// 模拟量采集设备为输入型设备
        /// </summary>
        public override DeviceCapability Capability => DeviceCapability.Input;

        private AnalogInputNode _analogInputNode;
        

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
        /// 输入范围
        /// </summary>
        public string InputRange
        {
            get => _inputRange;
            set => SetProperty(ref _inputRange, value);
        }

        /// <summary>
        /// 模拟量输入子节点
        /// </summary>
        public AnalogInputNode AiNode
        {
            get => _analogInputNode;
            set => SetProperty(ref _analogInputNode, value);
        }

        public AnalogAcquisitionDevice() : base()
        {
            DeviceType = DeviceConstants.Type.Card;
            ChannelCount = 32;
            SampleRate = 10000;
            InputRange = "±10V";
            _deviceTypeName = "模拟量采集";
            InitializeChildren();
        }

        public AnalogAcquisitionDevice(string name, string slotPosition) : base()
        {
            DeviceType = DeviceConstants.Type.Card;
            ChannelCount = 32;
            Model = "PXIe-9774";
            SampleRate = 10000;
            MaxSampleRate = "500k";
            InputRange = "±10V";
            _deviceTypeName = "模拟量采集";
            
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

            // 创建模拟量输入子节点
            AiNode = new AnalogInputNode
            {
                Name = "模拟量输入",
                ParentNode = "模拟量输入",  
                Model = $"{ChannelCount}通道",  
                ChannelCount = ChannelCount,
                SampleRate = SampleRate,
                InputRange = InputRange,
                SlotPosition = $"AI0–AI{ChannelCount - 1}",  
                Status = DeviceConstants.Status.Normal
            };
            
            Children.Add(AiNode);
        }

        /// <summary>
        /// 初始化设备的通道集合
        /// </summary>
        public override void InitializeChannels()
        {
            base.InitializeChannels(); // 清空通道集合
            
            // 使用ChannelFactory创建模拟输入通道
            var channels = ChannelFactory.CreateAnalogInputChannels(
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
            return $"AnalogAcquisition::{Manufacturer}::{Model}::{SlotPosition}";
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
    /// 模拟量输入子节点
    /// </summary>
    public class AnalogInputNode : DeviceBase
    {
        private int _channelCount;
        private double _sampleRate;
        private string _inputRange;

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
        /// 输入范围
        /// </summary>
        public string InputRange
        {
            get => _inputRange;
            set => SetProperty(ref _inputRange, value);
        }

        public override string DeviceTypeName => "模拟量输入";

        public AnalogInputNode()
        {
            DeviceType = Constants.DeviceConstants.Type.SubNode;
            ParentNode = "模拟量输入";  // 第一个字段显示"模拟量输入"
            Model = "32通道";  // 第二个字段显示"32通道"
            ChannelCount = 32;
            SampleRate = 100;
            InputRange = "±10V";
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
            return $"AnalogInput::{ChannelCount}::{SampleRate}";
        }
    }
}
