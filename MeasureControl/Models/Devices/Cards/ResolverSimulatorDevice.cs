using System;
using System.Collections.ObjectModel;
using System.Linq;
using MeasureControl.Helpers;
using MeasureControl.Models.Devices.DeviceCategories;

namespace MeasureControl.Models.Devices
{
    /// <summary>
    /// 旋转变压器模拟测量设备类（如：欧开PXI-4087C）
    /// 支持仿真和测量双模式，适用于旋转变压器测试
    /// </summary>
    public class ResolverSimulatorDevice : PxiDeviceBase
    {
        private int _channelCount;
        private string _frequencyRange;
        private double _outputVoltage;
        private double _angleAccuracy;
        private int _polePairs;
        private ResolverSimulatorNode _resolverSimulatorNode;
        private string _wiringType;

        // 激励输入特性（测量模式）
        private double _excitationVoltageMin;
        private double _excitationVoltageMax;
        private double _excitationFrequencyMin;
        private double _excitationFrequencyMax;
        private double _inputImpedance;

        // 激励输出特性（仿真模式）
        private double _excitationOutputVoltageMin;
        private double _excitationOutputVoltageMax;
        private double _outputImpedance;
        private double _driveCurrentMax;
        private double _frequencyError;

        // 测量输入特性
        private int _resolution;
        private double _linearError;
        private string _inputFormat;

        // 仿真输出特性
        private string _phaseConfiguration;
        private double _initialPhaseDelay;
        private double _phaseDelayResolution;

        // 触发特性
        private string _triggerSource;
        private string _triggerMode;
        private string _triggerSignal;

        // 时钟特性
        private string _clockSource;
        private double _localClockAccuracy;

        /// <summary>
        /// 通道数（旋变传感器接口数量，例如 PXI-4087C: 8 通道）
        /// </summary>
        public int ChannelCount
        {
            get => _channelCount;
            set => SetProperty(ref _channelCount, value);
        }

        /// <summary>
        /// 频率范围（如：0-4kHz）
        /// </summary>
        public string FrequencyRange
        {
            get => _frequencyRange;
            set => SetProperty(ref _frequencyRange, value);
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
        /// 角度精度 (°)
        /// </summary>
        public double AngleAccuracy
        {
            get => _angleAccuracy;
            set => SetProperty(ref _angleAccuracy, value);
        }

        /// <summary>
        /// 极对数
        /// </summary>
        public int PolePairs
        {
            get => _polePairs;
            set => SetProperty(ref _polePairs, value);
        }

        /// <summary>
        /// 旋变子节点（功能分组/通道容器）
        /// </summary>
        public ResolverSimulatorNode ResolverSimulatorNode
        {
            get => _resolverSimulatorNode;
            set => SetProperty(ref _resolverSimulatorNode, value);
        }

        /// <summary>
        /// 线制类型（4/5/6线制）
        /// </summary>
        public string WiringType
        {
            get => _wiringType;
            set => SetProperty(ref _wiringType, value);
        }

        /// <summary>
        /// 激励电压最小值 (V) - 测量模式
        /// </summary>
        public double ExcitationVoltageMin
        {
            get => _excitationVoltageMin;
            set => SetProperty(ref _excitationVoltageMin, value);
        }

        /// <summary>
        /// 激励电压最大值 (V) - 测量模式
        /// </summary>
        public double ExcitationVoltageMax
        {
            get => _excitationVoltageMax;
            set => SetProperty(ref _excitationVoltageMax, value);
        }

        /// <summary>
        /// 激励频率最小值 (Hz) - 测量模式
        /// </summary>
        public double ExcitationFrequencyMin
        {
            get => _excitationFrequencyMin;
            set => SetProperty(ref _excitationFrequencyMin, value);
        }

        /// <summary>
        /// 激励频率最大值 (Hz) - 测量模式
        /// </summary>
        public double ExcitationFrequencyMax
        {
            get => _excitationFrequencyMax;
            set => SetProperty(ref _excitationFrequencyMax, value);
        }

        /// <summary>
        /// 输入阻抗 (kΩ)
        /// </summary>
        public double InputImpedance
        {
            get => _inputImpedance;
            set => SetProperty(ref _inputImpedance, value);
        }

        /// <summary>
        /// 激励输出电压最小值 (V) - 仿真模式
        /// </summary>
        public double ExcitationOutputVoltageMin
        {
            get => _excitationOutputVoltageMin;
            set => SetProperty(ref _excitationOutputVoltageMin, value);
        }

        /// <summary>
        /// 激励输出电压最大值 (V) - 仿真模式
        /// </summary>
        public double ExcitationOutputVoltageMax
        {
            get => _excitationOutputVoltageMax;
            set => SetProperty(ref _excitationOutputVoltageMax, value);
        }

        /// <summary>
        /// 输出阻抗 (Ω)
        /// </summary>
        public double OutputImpedance
        {
            get => _outputImpedance;
            set => SetProperty(ref _outputImpedance, value);
        }

        /// <summary>
        /// 最大驱动电流 (mA)
        /// </summary>
        public double DriveCurrentMax
        {
            get => _driveCurrentMax;
            set => SetProperty(ref _driveCurrentMax, value);
        }

        /// <summary>
        /// 频率误差 (% FS)
        /// </summary>
        public double FrequencyError
        {
            get => _frequencyError;
            set => SetProperty(ref _frequencyError, value);
        }

        /// <summary>
        /// 分辨率 (Bit)
        /// </summary>
        public int Resolution
        {
            get => _resolution;
            set => SetProperty(ref _resolution, value);
        }

        /// <summary>
        /// 线性误差 (% FS)
        /// </summary>
        public double LinearError
        {
            get => _linearError;
            set => SetProperty(ref _linearError, value);
        }

        /// <summary>
        /// 输入格式
        /// </summary>
        public string InputFormat
        {
            get => _inputFormat;
            set => SetProperty(ref _inputFormat, value);
        }

        /// <summary>
        /// 相位配置
        /// </summary>
        public string PhaseConfiguration
        {
            get => _phaseConfiguration;
            set => SetProperty(ref _phaseConfiguration, value);
        }

        /// <summary>
        /// 初始相位延迟 (°)
        /// </summary>
        public double InitialPhaseDelay
        {
            get => _initialPhaseDelay;
            set => SetProperty(ref _initialPhaseDelay, value);
        }

        /// <summary>
        /// 相位延迟分辨率 (°)
        /// </summary>
        public double PhaseDelayResolution
        {
            get => _phaseDelayResolution;
            set => SetProperty(ref _phaseDelayResolution, value);
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
        /// 触发模式
        /// </summary>
        public string TriggerMode
        {
            get => _triggerMode;
            set => SetProperty(ref _triggerMode, value);
        }

        /// <summary>
        /// 触发信号
        /// </summary>
        public string TriggerSignal
        {
            get => _triggerSignal;
            set => SetProperty(ref _triggerSignal, value);
        }

        /// <summary>
        /// 时钟源
        /// </summary>
        public new string ClockSource
        {
            get => _clockSource;
            set => SetProperty(ref _clockSource, value);
        }

        /// <summary>
        /// 本地时钟精度 (ppm)
        /// </summary>
        public double LocalClockAccuracy
        {
            get => _localClockAccuracy;
            set => SetProperty(ref _localClockAccuracy, value);
        }

        public override string DeviceTypeName => "旋变模拟测量";

        public ResolverSimulatorDevice() : base()
        {
            DeviceType = "Card";
            ParentNode = "旋变模拟";
            InitializeDefaultParameters();
            InitializeChildren();
        }

        public ResolverSimulatorDevice(string name, string slotPosition) : base()
        {
            DeviceType = "Card";
            ParentNode = "旋变模拟";
            Model = "PXI-4087C";
            
            ParseDeviceName(name);
            SlotPosition = slotPosition;
            
            InitializeDefaultParameters();
            InitializeChildren();
        }

        /// <summary>
        /// 初始化默认参数（基于PXI-4087C规格）
        /// </summary>
        private void InitializeDefaultParameters()
        {
            // 基本参数
            ChannelCount = 8;  // PXI-4087C: 8通道
            FrequencyRange = "0-4kHz";
            OutputVoltage = 10.0;
            AngleAccuracy = 0.001526;  // 16-Bit分辨率对应精度
            PolePairs = 1;

            // 支持的线制类型（硬件能力边界）
            WiringType = "4/5/6线制";

            // 激励输入特性（测量模式）
            ExcitationVoltageMin = -10.0;
            ExcitationVoltageMax = 10.0;
            ExcitationFrequencyMin = 0.0;
            ExcitationFrequencyMax = 4000.0;
            InputImpedance = 90.0;  // 90kΩ

            // 激励输出特性（仿真模式）
            ExcitationOutputVoltageMin = -10.0;
            ExcitationOutputVoltageMax = 10.0;
            OutputImpedance = 50.0;  // 50Ω
            DriveCurrentMax = 20.0;  // 测量20mA, 仿真10mA
            FrequencyError = 0.05;  // 0.05% FS

            // 测量输入特性
            Resolution = 16;  // 16-Bit
            LinearError = 0.1;  // 0.1% FS
            InputFormat = "4/5/6线制";

            // 仿真输出特性
            PhaseConfiguration = "可配置";
            InitialPhaseDelay = 0.0;
            PhaseDelayResolution = 0.1;  // 0.1°

            // 触发特性
            TriggerSource = "软件/PXI背板/外部";
            TriggerMode = "边沿/门控";
            TriggerSignal = "PFI0-PFI7";

            // 时钟特性
            ClockSource = "本地10MHz/PXI 10MHz/外部5-10MHz";
            LocalClockAccuracy = 25.0;  // ±25ppm
        }

        public override void InitializeChildren()
        {
            Children.Clear();

            ResolverSimulatorNode = new ResolverSimulatorNode
            {
                Name = "旋变",
                ParentNode = "旋变",
                Model = $"{ChannelCount}通道",
                ChannelCount = ChannelCount,
                SlotPosition = $"CH0-CH{ChannelCount-1}",  // 设备内部传感器通道范围，仅用于 UI 标识
                Status = "正常"
            };

            Children.Add(ResolverSimulatorNode);
        }

        /// <summary>
        /// 初始化设备的通道集合（旋变传感器接口通道）
        /// </summary>
        public override void InitializeChannels()
        {
            base.InitializeChannels(); // 清空通道集合

            for (int i = 0; i < ChannelCount; i++)
            {
                var channel = new Models.Channels.ResolverChannel
                {
                    Name = $"Resolver{i}",
                    DeviceId = Id,
                    DeviceName = Name,
                    Description = $"旋变传感器通道 {i}",
                    // 默认能力由设备级硬件边界推导
                    ExcitationFrequency = (ExcitationFrequencyMin + ExcitationFrequencyMax) / 2.0,
                    ExcitationVoltage = (ExcitationVoltageMin + ExcitationVoltageMax) / 2.0,
                    OutputVoltage = OutputVoltage,
                    AngleAccuracy = AngleAccuracy,
                    ResolverType = "Resolver",
                    PolePairs = PolePairs,
                    OperationMode = OperationMode.Bidirectional
                };

                Channels.Add(channel);
            }
        }

        public override ObservableCollection<DeviceInfoItem> GetDeviceInfoItems()
        {
            var items = new ObservableCollection<DeviceInfoItem>();
            var mainDeviceInfo = DeviceInfoItem.FromDevice(this, false);
            if (mainDeviceInfo != null)
            {
                items.Add(mainDeviceInfo);
            }

            if (ResolverSimulatorNode != null)
            {
                var subNodeInfo = DeviceInfoItem.FromDevice(ResolverSimulatorNode, true);
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
            return $"Resolver::{Manufacturer}::{Model}::{SlotPosition}";
        }

        public override bool ValidateConfiguration()
        {
            return base.ValidateConfiguration() &&
                   ChannelCount > 0 &&
                   OutputVoltage > 0 &&
                   PolePairs > 0;
        }
    }

    /// <summary>
    /// 旋变模拟测量子节点
    /// </summary>
    public class ResolverSimulatorNode : DeviceBase
    {
        private int _channelCount;

        public int ChannelCount
        {
            get => _channelCount;
            set => SetProperty(ref _channelCount, value);
        }

        public override string DeviceTypeName => "旋变";

        public ResolverSimulatorNode()
        {
            DeviceType = "SubNode";
            ParentNode = "旋变";
            ChannelCount = 8;
            SlotPosition = "N/A";
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
            return $"ResolverNode::{ChannelCount}CH";
        }
    }
}

