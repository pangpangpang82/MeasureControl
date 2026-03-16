using System;
using System.Collections.ObjectModel;
using System.Linq;
using MeasureControl.Models;
using MeasureControl.Models.Devices.DeviceCategories;

namespace MeasureControl.Models.Devices
{
    /// <summary>
    /// 电子负载操作模式
    /// </summary>
    public enum LoadOperationMode
    {
        CC,  // 定电流
        CR,  // 定电阻
        CV,  // 定电压
        CP,  // 定功率
        LED  // LED模拟
    }

    /// <summary>
    /// 动态负载模式
    /// </summary>
    public enum LoadDynamicMode
    {
        Continuous,  // 连续
        Pulse,       // 脉冲
        Toggle,      // 翻转
        Switch       // 切换
    }

    /// <summary>
    /// 负载模组型号
    /// </summary>
    public enum LoadModuleType
    {
        M63102A,  // 100W x2通道
        M63103A,  // 300W
        M63105A,  // 500W
        M63106A,  // 600W
        M63123A   // 1200W (其他型号根据需要添加)
    }

    /// <summary>
    /// 主机框型号
    /// </summary>
    public enum LoadMainframeType
    {
        M6314A,  // 4插槽主机框
        M6312A   // 2插槽主机框
    }

    /// <summary>
    /// 电子负载设备类
    /// </summary>
    public class ElectronicLoadDevice : InstrumentDeviceBase
    {
        private int _channelCount;
        private double _maxVoltage;
        private double _maxCurrent;
        private double _maxPower;
        private ElectronicLoadChannelNode _electronicLoadChannelNode;

        // 操作模式与范围
        private string _supportedModes;
        private LoadOperationMode _currentOperationMode;
        private double _minOperatingVoltage;
        private string _currentRange;
        private string _resistanceRange;

        // 精度参数
        private string _currentAccuracy;
        private string _voltageAccuracy;
        private string _powerAccuracy;
        private string _resistanceAccuracy;

        // 动态模拟参数
        private LoadDynamicMode _dynamicMode;
        private string _dynamicFrequency;
        private string _currentSlewRate;
        private string _minRiseTime;
        private string _dynamicCycle;

        // 测量与监控
        private string _measurementResolution;
        private string _measurementAccuracy;
        private string _temperatureCoefficient;
        private string _inputImpedance;

        // 保护功能
        private string _overPowerProtection;
        private string _overCurrentProtection;
        private bool _overTemperatureProtection;
        private string _reverseVoltageProtection;

        // 模组化配置
        private LoadMainframeType? _mainframeType;
        private int _slotCount;
        private string _installedModules;

        // 通信接口
        private bool _interfaceGPIB;
        private string _gpibAddress;
        private bool _interfaceRS232;
        private bool _interfaceUSB;
        private bool _interfaceDigitalIO;

        /// <summary>
        /// 通道数量
        /// </summary>
        public int ChannelCount
        {
            get => _channelCount;
            set => SetProperty(ref _channelCount, value);
        }

        /// <summary>
        /// 最大电压 (V)
        /// </summary>
        public double MaxVoltage
        {
            get => _maxVoltage;
            set => SetProperty(ref _maxVoltage, value);
        }

        /// <summary>
        /// 最大电流 (A)
        /// </summary>
        public double MaxCurrent
        {
            get => _maxCurrent;
            set => SetProperty(ref _maxCurrent, value);
        }

        /// <summary>
        /// 最大功率 (W)
        /// </summary>
        public double MaxPower
        {
            get => _maxPower;
            set => SetProperty(ref _maxPower, value);
        }

        /// <summary>
        /// 电子负载通道子节点
        /// </summary>
        public ElectronicLoadChannelNode ElectronicLoadChannelNode
        {
            get => _electronicLoadChannelNode;
            set => SetProperty(ref _electronicLoadChannelNode, value);
        }

        #region 操作模式与范围属性
        /// <summary>
        /// 支持的操作模式
        /// </summary>
        public string SupportedModes
        {
            get => _supportedModes;
            set => SetProperty(ref _supportedModes, value);
        }

        /// <summary>
        /// 当前操作模式
        /// </summary>
        public LoadOperationMode CurrentOperationMode
        {
            get => _currentOperationMode;
            set => SetProperty(ref _currentOperationMode, value);
        }

        /// <summary>
        /// 最小操作电压 (V)
        /// </summary>
        public double MinOperatingVoltage
        {
            get => _minOperatingVoltage;
            set => SetProperty(ref _minOperatingVoltage, value);
        }

        /// <summary>
        /// 电流档位范围
        /// </summary>
        public string CurrentRange
        {
            get => _currentRange;
            set => SetProperty(ref _currentRange, value);
        }

        /// <summary>
        /// 电阻范围
        /// </summary>
        public string ResistanceRange
        {
            get => _resistanceRange;
            set => SetProperty(ref _resistanceRange, value);
        }
        #endregion

        #region 精度参数属性
        /// <summary>
        /// 电流精度
        /// </summary>
        public string CurrentAccuracy
        {
            get => _currentAccuracy;
            set => SetProperty(ref _currentAccuracy, value);
        }

        /// <summary>
        /// 电压精度
        /// </summary>
        public string VoltageAccuracy
        {
            get => _voltageAccuracy;
            set => SetProperty(ref _voltageAccuracy, value);
        }

        /// <summary>
        /// 功率精度
        /// </summary>
        public string PowerAccuracy
        {
            get => _powerAccuracy;
            set => SetProperty(ref _powerAccuracy, value);
        }

        /// <summary>
        /// 电阻精度
        /// </summary>
        public string ResistanceAccuracy
        {
            get => _resistanceAccuracy;
            set => SetProperty(ref _resistanceAccuracy, value);
        }
        #endregion

        #region 动态模拟参数属性
        /// <summary>
        /// 动态模式
        /// </summary>
        public LoadDynamicMode DynamicMode
        {
            get => _dynamicMode;
            set => SetProperty(ref _dynamicMode, value);
        }

        /// <summary>
        /// 动态频率范围
        /// </summary>
        public string DynamicFrequency
        {
            get => _dynamicFrequency;
            set => SetProperty(ref _dynamicFrequency, value);
        }

        /// <summary>
        /// 电流变化率
        /// </summary>
        public string CurrentSlewRate
        {
            get => _currentSlewRate;
            set => SetProperty(ref _currentSlewRate, value);
        }

        /// <summary>
        /// 最小上升时间
        /// </summary>
        public string MinRiseTime
        {
            get => _minRiseTime;
            set => SetProperty(ref _minRiseTime, value);
        }

        /// <summary>
        /// 动态周期范围
        /// </summary>
        public string DynamicCycle
        {
            get => _dynamicCycle;
            set => SetProperty(ref _dynamicCycle, value);
        }
        #endregion

        #region 测量与监控属性
        /// <summary>
        /// 测量分辨率
        /// </summary>
        public string MeasurementResolution
        {
            get => _measurementResolution;
            set => SetProperty(ref _measurementResolution, value);
        }

        /// <summary>
        /// 测量精度
        /// </summary>
        public string MeasurementAccuracy
        {
            get => _measurementAccuracy;
            set => SetProperty(ref _measurementAccuracy, value);
        }

        /// <summary>
        /// 温度系数
        /// </summary>
        public string TemperatureCoefficient
        {
            get => _temperatureCoefficient;
            set => SetProperty(ref _temperatureCoefficient, value);
        }

        /// <summary>
        /// 输入阻抗
        /// </summary>
        public string InputImpedance
        {
            get => _inputImpedance;
            set => SetProperty(ref _inputImpedance, value);
        }
        #endregion

        #region 保护功能属性
        /// <summary>
        /// 过功率保护 (OPP)
        /// </summary>
        public string OverPowerProtection
        {
            get => _overPowerProtection;
            set => SetProperty(ref _overPowerProtection, value);
        }

        /// <summary>
        /// 过流保护 (OCP)
        /// </summary>
        public string OverCurrentProtection
        {
            get => _overCurrentProtection;
            set => SetProperty(ref _overCurrentProtection, value);
        }

        /// <summary>
        /// 过温保护 (OTP)
        /// </summary>
        public bool OverTemperatureProtection
        {
            get => _overTemperatureProtection;
            set => SetProperty(ref _overTemperatureProtection, value);
        }

        /// <summary>
        /// 反压保护 (RVP)
        /// </summary>
        public string ReverseVoltageProtection
        {
            get => _reverseVoltageProtection;
            set => SetProperty(ref _reverseVoltageProtection, value);
        }
        #endregion

        #region 模组化配置属性
        /// <summary>
        /// 主机框型号
        /// </summary>
        public LoadMainframeType? MainframeType
        {
            get => _mainframeType;
            set => SetProperty(ref _mainframeType, value);
        }

        /// <summary>
        /// 插槽数量
        /// </summary>
        public int SlotCount
        {
            get => _slotCount;
            set => SetProperty(ref _slotCount, value);
        }

        /// <summary>
        /// 已安装模组信息
        /// </summary>
        public string InstalledModules
        {
            get => _installedModules;
            set => SetProperty(ref _installedModules, value);
        }
        #endregion

        #region 通信接口属性
        /// <summary>
        /// GPIB接口
        /// </summary>
        public bool InterfaceGPIB
        {
            get => _interfaceGPIB;
            set => SetProperty(ref _interfaceGPIB, value);
        }

        /// <summary>
        /// GPIB地址范围
        /// </summary>
        public string GPIBAddress
        {
            get => _gpibAddress;
            set => SetProperty(ref _gpibAddress, value);
        }

        /// <summary>
        /// RS232接口
        /// </summary>
        public bool InterfaceRS232
        {
            get => _interfaceRS232;
            set => SetProperty(ref _interfaceRS232, value);
        }

        /// <summary>
        /// USB接口
        /// </summary>
        public bool InterfaceUSB
        {
            get => _interfaceUSB;
            set => SetProperty(ref _interfaceUSB, value);
        }

        /// <summary>
        /// 数字I/O接口
        /// </summary>
        public bool InterfaceDigitalIO
        {
            get => _interfaceDigitalIO;
            set => SetProperty(ref _interfaceDigitalIO, value);
        }
        #endregion

        public override string DeviceTypeName => "电子负载";

        public ElectronicLoadDevice() : base()
        {
            DeviceType = "Instrument";
            ParentNode = "电子负载";  // 设置ParentNode，确保与左侧列表显示一致
            ChannelCount = 2;
            MaxVoltage = 150;
            MaxCurrent = 30;
            MaxPower = 300;
            InitializeChildren();
        }

        public ElectronicLoadDevice(string name, string slotPosition) : base()
        {
            DeviceType = "Instrument";
            ParentNode = "电子负载";  // 设置ParentNode，确保与左侧列表显示一致
            Model = "6314A";
            ChannelCount = 2;
            MaxVoltage = 150;
            MaxCurrent = 30;
            MaxPower = 300;
            slotPosition = "LAN";
            ParseDeviceName(name);
            ConfigureByModel(name);
            SlotPosition = slotPosition;

            InitializeChildren();
        }

        public override void InitializeChildren()
        {
            Children.Clear();
            
            // 对于Chroma 6314A等主机框，显示4个模块槽位
            if (MainframeType == LoadMainframeType.M6314A)
            {
                // 显示4个模块槽位
                for (int i = 1; i <= 4; i++)
                {
                    // 这里简化处理，实际应该有LoadedModules列表
                    // 假设前2个槽位已安装模块，后2个为空槽
                    if (i <= 2)
                    {
                        var moduleNode = new ElectronicLoadChannelNode
                        {
                            Name = $"模块{i}",
                            ParentNode = "电子负载",
                            Model = "DC Load 模块",
                            SlotPosition = $"0–{MaxVoltage}V/{MaxCurrent}A",
                            Status = "正常"
                        };
                        Children.Add(moduleNode);
                    }
                    else
                    {
                        var emptySlot = new ElectronicLoadChannelNode
                        {
                            Name = $"模块{i}",
                            ParentNode = "电子负载",
                            Model = "空槽",
                            SlotPosition = "",
                            Status = "--"
                        };
                        Children.Add(emptySlot);
                    }
                }
            }
            else
            {
                // 单模块或其他配置：只显示一个子节点
                ElectronicLoadChannelNode = new ElectronicLoadChannelNode
                {
                    Name = "负载模块",
                    ParentNode = "电子负载",
                    ChannelCount = ChannelCount,
                    MaxVoltage = MaxVoltage,
                    MaxCurrent = MaxCurrent,
                    MaxPower = MaxPower,
                    Model = $"{MaxPower}W",
                    SlotPosition = $"0–{MaxVoltage}V/{MaxCurrent}A",
                    Status = "正常"
                };
                
                Children.Add(ElectronicLoadChannelNode);
            }
        }

        public override ObservableCollection<DeviceInfoItem> GetDeviceInfoItems()
        {
            var items = new ObservableCollection<DeviceInfoItem>();

            // 使用FromDevice静态方法创建主设备信息项,确保与deviceListBorder显示一致
            var mainDeviceInfo = DeviceInfoItem.FromDevice(this, false);
            if (mainDeviceInfo != null)
            {
                items.Add(mainDeviceInfo);
            }

            // 添加所有子节点（模块槽位）
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

        /// <summary>
        /// 根据型号配置设备参数
        /// </summary>
        private void ConfigureByModel(string deviceName)
        {
            if (string.IsNullOrEmpty(deviceName))
                return;

            string modelLower = deviceName.ToLower();

            // 识别 Chroma 6310A 系列
            if (modelLower.Contains("6314a") || modelLower.Contains("6312a") || 
                modelLower.Contains("6310a") || modelLower.Contains("63103a") ||
                modelLower.Contains("63102a") || modelLower.Contains("63105a") ||
                modelLower.Contains("63106a") || modelLower.Contains("63123a"))
            {
                ConfigureAsChroma6314A(modelLower);
            }
        }

        /// <summary>
        /// 配置为 Chroma 6314A 系列电子负载
        /// </summary>
        private void ConfigureAsChroma6314A(string modelLower)
        {
            // 识别主机框型号
            if (modelLower.Contains("6314a"))
            {
                MainframeType = LoadMainframeType.M6314A;
                SlotCount = 4;
                ChannelCount = 8; // 最多8通道
                InstalledModules = "4槽主机框";
            }
            else if (modelLower.Contains("6312a"))
            {
                MainframeType = LoadMainframeType.M6312A;
                SlotCount = 2;
                ChannelCount = 4; // 最多4通道
                InstalledModules = "2槽主机框";
            }
            else
            {
                // 单个模组配置
                SlotCount = 1;
                if (modelLower.Contains("63102a"))
                {
                    ChannelCount = 2;
                    MaxPower = 100;
                    InstalledModules = "63102A (100W x2通道)";
                }
                else if (modelLower.Contains("63103a"))
                {
                    ChannelCount = 1;
                    MaxPower = 300;
                    InstalledModules = "63103A (300W)";
                }
                else if (modelLower.Contains("63105a"))
                {
                    ChannelCount = 1;
                    MaxPower = 500;
                    InstalledModules = "63105A (500W)";
                }
                else if (modelLower.Contains("63106a"))
                {
                    ChannelCount = 1;
                    MaxPower = 600;
                    InstalledModules = "63106A (600W)";
                }
                else if (modelLower.Contains("63123a"))
                {
                    ChannelCount = 1;
                    MaxPower = 1200;
                    InstalledModules = "63123A (1200W)";
                }
            }

            // 基本范围（以63103A模组为例）
            MaxVoltage = 80;
            MaxCurrent = 60;
            MinOperatingVoltage = 0.8;

            // 操作模式
            SupportedModes = "CC/CR/CV/CP/LED";
            CurrentOperationMode = LoadOperationMode.CC;
            CurrentRange = "0~6A / 0~60A (高低档)";
            ResistanceRange = "0.025Ω~5kΩ";

            // 精度
            CurrentAccuracy = "0.1% + 0.1%FS / 0.1% + 0.2%FS";
            VoltageAccuracy = "0.05% ± 0.1%FS";
            PowerAccuracy = "0.5% ± 0.5%FS";
            ResistanceAccuracy = "0.2% + 0.2%FS";

            // 动态模拟
            DynamicMode = LoadDynamicMode.Continuous;
            DynamicFrequency = "最高20kHz";
            CurrentSlewRate = "0.001~2.5A/μs";
            MinRiseTime = "10μs (典型)";
            DynamicCycle = "0.025ms~50s";

            // 测量
            MeasurementResolution = "16-bit A/D";
            MeasurementAccuracy = "V: 0.025%+0.025%FS / I: 0.05%+0.05%FS";
            TemperatureCoefficient = "100ppm/°C";
            InputImpedance = "≥100kΩ";

            // 保护
            OverPowerProtection = "OPP (可程式)";
            OverCurrentProtection = "OCP (可程式)";
            OverTemperatureProtection = true;
            ReverseVoltageProtection = "RVP";

            // 通信接口
            InterfaceGPIB = true;
            GPIBAddress = "0-30";
            InterfaceRS232 = true;
            InterfaceUSB = true;
            InterfaceDigitalIO = true;

        }

        public override string GetConnectionString()
        {
            return $"ElectronicLoad::{Manufacturer}::{Model}::{SlotPosition}";
        }

        public override bool ValidateConfiguration()
        {
            return base.ValidateConfiguration() && 
                   ChannelCount > 0 && 
                   MaxVoltage > 0 && 
                   MaxCurrent > 0 && 
                   MaxPower > 0;
        }
    }

    /// <summary>
    /// 电子负载通道子节点
    /// </summary>
    public class ElectronicLoadChannelNode : DeviceBase
    {
        private int _channelCount;
        private double _maxVoltage;
        private double _maxCurrent;
        private double _maxPower;

        public int ChannelCount
        {
            get => _channelCount;
            set => SetProperty(ref _channelCount, value);
        }

        public double MaxVoltage
        {
            get => _maxVoltage;
            set => SetProperty(ref _maxVoltage, value);
        }

        public double MaxCurrent
        {
            get => _maxCurrent;
            set => SetProperty(ref _maxCurrent, value);
        }

        public double MaxPower
        {
            get => _maxPower;
            set => SetProperty(ref _maxPower, value);
        }

        public override string DeviceTypeName => Name ?? "负载模块";

        public ElectronicLoadChannelNode()
        {
            DeviceType = "SubNode";
            ParentNode = "电子负载";
            ChannelCount = 2;
            MaxVoltage = 150;
            MaxCurrent = 30;
            MaxPower = 300;
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
            items.Add(new DeviceInfoItem(DeviceTypeName, Model, SlotPosition, Status, true, "Instrument"));
            return items;
        }

        public override string GetConnectionString()
        {
            return $"ElectronicLoadChannel::{ChannelCount}::{MaxVoltage}V::{MaxCurrent}A::{MaxPower}W";
        }
    }
}
