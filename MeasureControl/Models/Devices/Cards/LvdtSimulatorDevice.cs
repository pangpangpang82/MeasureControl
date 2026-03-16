using System;
using System.Collections.ObjectModel;
using System.Linq;
using MeasureControl.Helpers;
using MeasureControl.Models.Devices.DeviceCategories;

namespace MeasureControl.Models.Devices
{
    /// <summary>
    /// 工作模式枚举
    /// </summary>
    public enum OperationMode
    {
        /// <summary>仿真模式</summary>
        Simulation,
        /// <summary>测量模式</summary>
        Measurement,
        /// <summary>双向模式（同时仿真和测量）</summary>
        Bidirectional
    }

    /// <summary>
    /// LVDT/RVDT模拟测量设备类（如：欧开PXI-4087A/B）
    /// 支持仿真和测量双模式，适用于位移传感器测试
    /// 注意：PXI-4087A/B为LVDT/RVDT专用，PXI-4087C为旋转变压器专用
    /// </summary>
    public class LvdtSimulatorDevice : PxiDeviceBase
    {
        private int _channelCount;
        private string _frequencyRange;
        private double _outputRange;
        private double _accuracy;
        private string _sensorType;
        private LvdtSimulatorNode _lvdtSimulatorNode;

        // 工作模式
        private OperationMode _workMode;
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
        /// 通道数（PXI-4087A: 8通道）
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
        /// 输出范围 (V)
        /// </summary>
        public double OutputRange
        {
            get => _outputRange;
            set => SetProperty(ref _outputRange, value);
        }

        /// <summary>
        /// 精度 (%)
        /// </summary>
        public double Accuracy
        {
            get => _accuracy;
            set => SetProperty(ref _accuracy, value);
        }

        /// <summary>
        /// 传感器类型（LVDT/RVDT）
        /// </summary>
        public string SensorType
        {
            get => _sensorType;
            set => SetProperty(ref _sensorType, value);
        }

        /// <summary>
        /// LVDT子节点
        /// </summary>
        public LvdtSimulatorNode LvdtSimulatorNode
        {
            get => _lvdtSimulatorNode;
            set => SetProperty(ref _lvdtSimulatorNode, value);
        }

        /// <summary>
        /// 工作模式（仿真/测量/双向）
        /// </summary>
        public OperationMode WorkMode
        {
            get => _workMode;
            set => SetProperty(ref _workMode, value);
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

        public override string DeviceTypeName => "LVDT/RVDT模拟测量";

        public LvdtSimulatorDevice() : base()
        {
            DeviceType = "Card";
            ParentNode = "LVDT模拟";
            InitializeDefaultParameters();
            InitializeChildren();
        }

        public LvdtSimulatorDevice(string name, string slotPosition) : base()
        {
            DeviceType = "Card";
            ParentNode = "LVDT模拟";
            Model = "PXI-4087A";
            ParseDeviceName(name);
            SlotPosition = slotPosition;
            
            InitializeDefaultParameters();
            InitializeChildren();
        }

        /// <summary>
        /// 初始化默认参数（基于PXI-4087A/B规格）
        /// </summary>
        private void InitializeDefaultParameters()
        {
            // 基本参数
            ChannelCount = 8;  // PXI-4087A: 8通道
            FrequencyRange = "0-4kHz";
            OutputRange = 10.0;
            Accuracy = 0.001526;  // 16-Bit分辨率对应精度
            SensorType = "LVDT/RVDT";

            // 工作模式
            WorkMode = OperationMode.Bidirectional;
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

            LvdtSimulatorNode = new LvdtSimulatorNode
            {
                Name = "LVDT",
                ParentNode = "LVDT",
                Model = $"{ChannelCount}通道",
                ChannelCount = ChannelCount,
                FrequencyRange = FrequencyRange,
                OutputRange = OutputRange,
                Accuracy = Accuracy,
                WorkMode = WorkMode,
                WiringType = WiringType,
                Resolution = Resolution,
                SlotPosition = $"CH0-CH{ChannelCount-1}",  // 功能标识
                Status = "正常"
            };

            Children.Add(LvdtSimulatorNode);
        }

        /// <summary>
        /// 初始化设备的通道集合（传感器接口通道）
        /// </summary>
        public override void InitializeChannels()
        {
            base.InitializeChannels(); // 清空通道集合

            for (int i = 0; i < ChannelCount; i++)
            {
                // LVDT/RVDT模式：创建LVDT通道
                var channel = new Models.Channels.LvdtChannel
                {
                    Name = $"LVDT{i}",
                    DeviceId = Id,
                    DeviceName = Name,
                    Description = $"LVDT/RVDT 传感器通道 {i}",
                    // 将设备级硬件能力边界映射为通道的默认能力
                    ExcitationFrequency = (ExcitationFrequencyMin + ExcitationFrequencyMax) / 2.0,
                    ExcitationVoltage = (ExcitationVoltageMin + ExcitationVoltageMax) / 2.0,
                    OutputRange = OutputRange,
                    Accuracy = Accuracy,
                    SensorType = SensorType,
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

            if (LvdtSimulatorNode != null)
            {
                var subNodeInfo = DeviceInfoItem.FromDevice(LvdtSimulatorNode, true);
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
            return $"LVDT::{Manufacturer}::{Model}::{SlotPosition}";
        }

        public override bool ValidateConfiguration()
        {
            return base.ValidateConfiguration() &&
                   ChannelCount > 0 &&
                   OutputRange > 0;
        }
    }

    /// <summary>
    /// LVDT模拟测量子节点
    /// </summary>
    public class LvdtSimulatorNode : DeviceBase
    {
        private int _channelCount;
        private string _frequencyRange;
        private double _outputRange;
        private double _accuracy;
        private OperationMode _workMode;
        private string _wiringType;
        private int _resolution;

        public int ChannelCount
        {
            get => _channelCount;
            set => SetProperty(ref _channelCount, value);
        }

        public string FrequencyRange
        {
            get => _frequencyRange;
            set => SetProperty(ref _frequencyRange, value);
        }

        public double OutputRange
        {
            get => _outputRange;
            set => SetProperty(ref _outputRange, value);
        }

        public double Accuracy
        {
            get => _accuracy;
            set => SetProperty(ref _accuracy, value);
        }

        public OperationMode WorkMode
        {
            get => _workMode;
            set => SetProperty(ref _workMode, value);
        }

        public string WiringType
        {
            get => _wiringType;
            set => SetProperty(ref _wiringType, value);
        }

        public int Resolution
        {
            get => _resolution;
            set => SetProperty(ref _resolution, value);
        }

        public override string DeviceTypeName => "LVDT";

        public LvdtSimulatorNode()
        {
            DeviceType = "SubNode";
            ParentNode = "LVDT";
            ChannelCount = 8;
            FrequencyRange = "0-4kHz";
            OutputRange = 10.0;
            Accuracy = 0.001526;
            WorkMode = OperationMode.Bidirectional;
            WiringType = "4/5/6线制";
            Resolution = 16;
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
            return $"LVDTNode::{ChannelCount}CH::{OutputRange}V";
        }
    }
}

