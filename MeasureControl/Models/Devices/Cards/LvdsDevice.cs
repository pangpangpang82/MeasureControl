using System;
using System.Collections.ObjectModel;
using System.Linq;
using MeasureControl.Helpers;
using MeasureControl.Models.Devices.DeviceCategories;
using MeasureControl.Models.Channels;
using MeasureControl.Constants;

namespace MeasureControl.Models.Devices
{
    /// <summary>
    /// LVDS设备类（如：芒果树 MT-X970）
    /// 支持高速差分数字信号传输
    /// </summary>
    public class LvdsDevice : PxiDeviceBase
    {
        private int _channelCount;  // 仅用于规格展示，不参与建模
        private int _dataInCount;
        private int _dataOutCount;
        private int _pfiCount;
        private string _dataRate;
        private string _voltageLevel;
        private double _outputVoltage;
        private string _inputImpedance;
        private string _outputImpedance;

        // LVDS特性
        private string _signalType;
        private string _clockMode;
        private int _parallelChannels;
        private string _triggerSource;
        private string _clockSource;

        /// <summary>
        /// 通道数量（仅用于规格展示，不参与建模）
        /// </summary>
        public int ChannelCount
        {
            get => _channelCount;
            set => SetProperty(ref _channelCount, value);
        }

        /// <summary>
        /// 数据输入通道数量
        /// </summary>
        public int DataInCount
        {
            get => _dataInCount;
            set => SetProperty(ref _dataInCount, value);
        }

        /// <summary>
        /// 数据输出通道数量
        /// </summary>
        public int DataOutCount
        {
            get => _dataOutCount;
            set => SetProperty(ref _dataOutCount, value);
        }

        /// <summary>
        /// PFI通道数量
        /// </summary>
        public int PfiCount
        {
            get => _pfiCount;
            set => SetProperty(ref _pfiCount, value);
        }

        /// <summary>
        /// 数据传输速率
        /// </summary>
        public string DataRate
        {
            get => _dataRate;
            set => SetProperty(ref _dataRate, value);
        }

        /// <summary>
        /// 电压等级
        /// </summary>
        public string VoltageLevel
        {
            get => _voltageLevel;
            set => SetProperty(ref _voltageLevel, value);
        }

        /// <summary>
        /// 输出电压 (V)
        /// </summary>
        public double OutputVoltage
        {
            get => _outputVoltage;
            set => SetProperty(ref _outputVoltage, value);
        }

        /// <summary>
        /// 输入阻抗
        /// </summary>
        public string InputImpedance
        {
            get => _inputImpedance;
            set => SetProperty(ref _inputImpedance, value);
        }

        /// <summary>
        /// 输出阻抗
        /// </summary>
        public string OutputImpedance
        {
            get => _outputImpedance;
            set => SetProperty(ref _outputImpedance, value);
        }

        /// <summary>
        /// 信号类型
        /// </summary>
        public string SignalType
        {
            get => _signalType;
            set => SetProperty(ref _signalType, value);
        }

        /// <summary>
        /// 时钟模式
        /// </summary>
        public string ClockMode
        {
            get => _clockMode;
            set => SetProperty(ref _clockMode, value);
        }

        /// <summary>
        /// 并行通道数
        /// </summary>
        public int ParallelChannels
        {
            get => _parallelChannels;
            set => SetProperty(ref _parallelChannels, value);
        }

        /// <summary>
        /// 触发源
        /// </summary>
        public string TriggerSource
        {
            get => _triggerSource;
            set => SetProperty(ref _triggerSource, value);
        }

        /// <summary>
        /// 时钟源
        /// </summary>
        public new string ClockSource
        {
            get => _clockSource;
            set => SetProperty(ref _clockSource, value);
        }

        public override string DeviceTypeName => "LVDS 高速IO";

        public LvdsDevice() : base()
        {
            DeviceType = "Card";
            ParentNode = "LVDS";
            InitializeDefaultParameters();
            InitializeChildren();
        }

        public LvdsDevice(string name, string slotPosition) : base()
        {
            DeviceType = "Card";
            ParentNode = "LVDS";
            Model = "MT-X970";
            ParseDeviceName(name);
            SlotPosition = slotPosition;
            
            InitializeDefaultParameters();
            InitializeChildren();
        }

        /// <summary>
        /// 初始化默认参数（基于MT-X970规格）
        /// </summary>
        private void InitializeDefaultParameters()
        {
            // 通道数量（分别设置）
            DataInCount = 16;
            DataOutCount = 16;
            PfiCount = 7;
            ChannelCount = DataInCount + DataOutCount + PfiCount;  // 仅用于规格展示

            // 基本参数
            DataRate = "655 Mbps";
            VoltageLevel = "LVDS";
            OutputVoltage = 1.2;  // 典型LVDS输出电压
            InputImpedance = "100Ω";
            OutputImpedance = "100Ω";

            // LVDS特性
            SignalType = "差分信号";
            ClockMode = "源同步/嵌入式时钟";
            ParallelChannels = 8;
            TriggerSource = "软件/PXI背板/外部";
            ClockSource = "本地/PXI背板/外部";

            // 设备通用属性
            Status = "正常";
            Description = "LVDS高速差分数字信号传输设备";
        }

        public override void InitializeChildren()
        {
            Children.Clear();

            // 创建LVDS Data In子节点
            var dataInNode = new LvdsDataInNode
            {
                Name = "LVDS Data In",
                ParentNode = "LVDS",
                ChannelCount = DataInCount,
                Model = $"{DataInCount}路",
                SlotPosition = $"DIN0–DIN{DataInCount - 1}",
                Status = Constants.DeviceConstants.Status.Normal
            };
            Children.Add(dataInNode);

            // 创建LVDS Data Out子节点
            var dataOutNode = new LvdsDataOutNode
            {
                Name = "LVDS Data Out",
                ParentNode = "LVDS",
                ChannelCount = DataOutCount,
                Model = $"{DataOutCount}路",
                SlotPosition = $"DOUT0–DOUT{DataOutCount - 1}",
                Status = Constants.DeviceConstants.Status.Normal
            };
            Children.Add(dataOutNode);

            // 创建PFI子节点
            var pfiNode = new LvdsPfiNode
            {
                Name = "PFI",
                ParentNode = "LVDS",
                ChannelCount = PfiCount,
                Model = $"{PfiCount}路",
                SlotPosition = $"PFI0–PFI{PfiCount - 1}",
                Status = Constants.DeviceConstants.Status.Normal
            };
            Children.Add(pfiNode);
        }

        /// <summary>
        /// 初始化设备的通道集合
        /// </summary>
        public override void InitializeChannels()
        {
            base.InitializeChannels(); // 清空通道集合
            
            // 创建数据输入通道（DIN0-DIN15）
            for (int i = 0; i < DataInCount; i++)
            {
                var channel = new LvdsDataInChannel
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Name = $"DIN{i}",
                    DeviceId = Id,
                    DeviceName = Name,
                    Description = $"LVDS数据输入通道 {i}",
                    ChannelType = "LVDS_DIN",
                    DataRate = DataRate,
                    VoltageLevel = VoltageLevel,
                    InputImpedance = InputImpedance
                };
                Channels.Add(channel);
            }
            
            // 创建数据输出通道（DOUT0-DOUT15）
            for (int i = 0; i < DataOutCount; i++)
            {
                var channel = new LvdsDataOutChannel
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Name = $"DOUT{i}",
                    DeviceId = Id,
                    DeviceName = Name,
                    Description = $"LVDS数据输出通道 {i}",
                    ChannelType = "LVDS_DOUT",
                    DataRate = DataRate,
                    VoltageLevel = VoltageLevel,
                    OutputVoltage = OutputVoltage,
                    OutputImpedance = OutputImpedance
                };
                Channels.Add(channel);
            }
            
            // 创建PFI通道（PFI0-PFI6）
            for (int i = 0; i < PfiCount; i++)
            {
                var channel = new LvdsPfiChannel
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Name = $"PFI{i}",
                    DeviceId = Id,
                    DeviceName = Name,
                    Description = $"LVDS PFI通道 {i}",
                    ChannelType = "LVDS_PFI",
                    VoltageLevel = VoltageLevel
                };
                Channels.Add(channel);
            }
        }

        public override ObservableCollection<DeviceInfoItem> GetDeviceInfoItems()
        {
            var items = new ObservableCollection<DeviceInfoItem>();
            
            // 第一行：设备主信息
            var mainDeviceInfo = DeviceInfoItem.FromDevice(this, false);
            if (mainDeviceInfo != null)
            {
                items.Add(mainDeviceInfo);
            }

            // 添加所有子节点（LVDS输入和输出）
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
            return $"LVDS::{Manufacturer}::{Model}::{SlotPosition}";
        }

        public override bool ValidateConfiguration()
        {
            return base.ValidateConfiguration() &&
                   DataInCount > 0 &&
                   DataOutCount > 0 &&
                   PfiCount >= 0;
        }
    }

    /// <summary>
    /// LVDS Data In子节点
    /// </summary>
    public class LvdsDataInNode : DeviceBase
    {
        private int _channelCount;

        public int ChannelCount
        {
            get => _channelCount;
            set => SetProperty(ref _channelCount, value);
        }

        public override string DeviceTypeName => "LVDS Data In";

        public LvdsDataInNode()
        {
            DeviceType = Constants.DeviceConstants.Type.SubNode;
            ParentNode = "LVDS";
            ChannelCount = 16;
            SlotPosition = "DIN0–DIN15";
            Status = Constants.DeviceConstants.Status.Normal;
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
            return $"LVDS::DataIn::{ChannelCount}CH";
        }
    }

    /// <summary>
    /// LVDS Data Out子节点
    /// </summary>
    public class LvdsDataOutNode : DeviceBase
    {
        private int _channelCount;

        public int ChannelCount
        {
            get => _channelCount;
            set => SetProperty(ref _channelCount, value);
        }

        public override string DeviceTypeName => "LVDS Data Out";

        public LvdsDataOutNode()
        {
            DeviceType = Constants.DeviceConstants.Type.SubNode;
            ParentNode = "LVDS";
            ChannelCount = 16;
            SlotPosition = "DOUT0–DOUT15";
            Status = Constants.DeviceConstants.Status.Normal;
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
            return $"LVDS::DataOut::{ChannelCount}CH";
        }
    }

    /// <summary>
    /// LVDS PFI子节点
    /// </summary>
    public class LvdsPfiNode : DeviceBase
    {
        private int _channelCount;

        public int ChannelCount
        {
            get => _channelCount;
            set => SetProperty(ref _channelCount, value);
        }

        public override string DeviceTypeName => "PFI";

        public LvdsPfiNode()
        {
            DeviceType = Constants.DeviceConstants.Type.SubNode;
            ParentNode = "LVDS";
            ChannelCount = 7;
            SlotPosition = "PFI0–PFI6";
            Status = Constants.DeviceConstants.Status.Normal;
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
            return $"LVDS::PFI::{ChannelCount}CH";
        }
    }
}

