using System;
using System.Collections.ObjectModel;
using System.Linq;
using MeasureControl.Constants;
using MeasureControl.Models;
using MeasureControl.Models.Devices.DeviceCategories;
using MeasureControl.Models.Devices.Configurators;

namespace MeasureControl.Models.Devices
{
    /// <summary>
    /// 操作模式枚举
    /// </summary>
    public enum PowerSupplyOperationMode
    {
        /// <summary>
        /// 独立模式 - 三个通道独立输出
        /// </summary>
        Independent,
        
        /// <summary>
        /// 串联模式 - 电压叠加
        /// </summary>
        Series,
        
        /// <summary>
        /// 并联模式 - 电流叠加
        /// </summary>
        Parallel,
        
        /// <summary>
        /// 同步模式 - 参数变化成比例同步
        /// </summary>
        Tracking
    }

    /// <summary>
    /// CC/CV优先权模式枚举
    /// </summary>
    public enum PowerSupplyPriorityMode
    {
        /// <summary>
        /// CV优先模式
        /// </summary>
        CV_Priority,
        
        /// <summary>
        /// CC优先模式
        /// </summary>
        CC_Priority,
        
        /// <summary>
        /// CV高速模式
        /// </summary>
        CV_High,
        
        /// <summary>
        /// CC低速模式
        /// </summary>
        CC_Low
    }

    /// <summary>
    /// 程控电源设备类
    /// </summary>
    public class PowerSupplyDevice : InstrumentDeviceBase
    {
        private int _channelCount;
        private double _maxVoltage;
        private double _maxCurrent;
        private double _powerRating;
        
        // 运行时配置属性
        private PowerSupplyOperationMode _operationMode;
        private string _channelCombination;
        private double _seriesVoltageLimit;
        private double _parallelCurrentLimit;
        
        // 通信接口属性
        private bool _interfaceRS232;
        private string _interfaceUSB;
        private bool _interfaceGPIB;
        private bool _interfaceLAN;
        private string _gpibAddressRange;
        
        // IT6300系列特定属性
        private string _loadRegulation;
        private string _lineRegulation;
        private string _overVoltageProtection;
        
        // CH3独立规格支持（IT-N6332B等型号）
        private double _ch3MaxVoltage;
        private double _ch3MaxCurrent;
        private double _ch3PowerRating;
        private string _ch3OverVoltageProtection;

        // IT-M3900D系列特定属性 - 并联配置
        private bool _isMasterUnit;
        private int _slaveCount;
        private bool _fiberConnectionEnabled;
        private int _parallelMaxUnits;
        
        // IT-M3900D系列特定属性 - 输出控制
        private PowerSupplyPriorityMode _priorityMode;
        private double _seriesResistance;
        private double _senseCompensationVoltage;
        
        // IT-M3900D系列特定属性 - 输入电源
        private string _inputVoltageType;
        private double _maxACApparentPower;
        private double _powerFactor;
        private double _maxEfficiency;
        
        // IT-M3900D系列特定属性 - 功能特性
        private bool _supportListFunction;
        private int _maxListSteps;
        private bool _supportArbitraryWaveform;
        private bool _builtInWebServer;
        
        // IT-M3900D系列特定属性 - 设定值精确度
        private string _setVoltageAccuracy;
        private string _setCurrentAccuracy;
        private string _setPowerAccuracy;
        private string _setResistanceAccuracy;
        
        // IT-M3900D系列特定属性 - 回读值精确度
        private string _readbackVoltageAccuracy;
        private string _readbackCurrentAccuracy;
        private string _readbackPowerAccuracy;
        
        // IT-M3900D系列特定属性 - 解析度
        private string _voltageResolution;
        private string _currentResolution;
        private string _powerResolution;
        private string _resistanceResolution;
        
        // IT-M3900D系列特定属性 - 温度系数
        private string _setVoltageTempCoeff;
        private string _setCurrentTempCoeff;
        private string _readbackVoltageTempCoeff;
        private string _readbackCurrentTempCoeff;
        
        // IT-M3900D系列特定属性 - 时间参数
        private string _riseTimeNoLoad;
        private string _riseTimeFullLoad;
        private string _fallTimeNoLoad;
        private string _fallTimeFullLoad;
        private string _dynamicResponseTime;
        private string _programmingResponseTime;
        
        // IT-M3900D系列特定属性 - 纹波
        private string _voltageRipplePeak;
        private string _voltageRippleRms;
        
        // IT-M3900D系列特定属性 - 调节率
        private string _lineRegulationVoltage;
        private string _lineRegulationCurrent;
        private string _loadRegulationVoltage;
        private string _loadRegulationCurrent;
        
        // IT-M3900D系列特定属性 - 其他性能
        private string _currentHarmonic;
        private string _overCurrentProtection;
        private string _overPowerProtection;
        
        // IT-M3900D系列特定属性 - 外部模拟量接口（选配）
        private string _externalAnalogCurrentProgramming;
        private string _externalAnalogCurrentMonitoring;
        private string _externalAnalogVoltageProgramming;
        private string _externalAnalogVoltageMonitoring;
        
        // IT-M3900D系列特定属性 - 环境参数
        private string _operatingTemperature;
        private string _storageTemperature;
        private string _operatingHumidity;
        private string _altitudeLimit;
        private string _protectionRating;
        
        // IT-M3900D系列特定属性 - 物理尺寸
        private string _dimensionsOverall;
        private string _dimensionsBare;
        private double _netWeight;
        private string _coolingMethod;
        
        // 其他通信接口
        private bool _interfaceCAN;
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
        /// 功率等级 (W)
        /// </summary>
        public double PowerRating
        {
            get => _powerRating;
            set => SetProperty(ref _powerRating, value);
        }

        /// <summary>
        /// 操作模式
        /// </summary>
        public PowerSupplyOperationMode OperationMode
        {
            get => _operationMode;
            set => SetProperty(ref _operationMode, value);
        }

        /// <summary>
        /// 通道组合（如 "CH1+CH2", "CH2+CH3", "ALL"）
        /// </summary>
        public string ChannelCombination
        {
            get => _channelCombination;
            set => SetProperty(ref _channelCombination, value);
        }

        /// <summary>
        /// 串联电压限制 (V)
        /// </summary>
        public double SeriesVoltageLimit
        {
            get => _seriesVoltageLimit;
            set => SetProperty(ref _seriesVoltageLimit, value);
        }

        /// <summary>
        /// 并联电流限制 (A)
        /// </summary>
        public double ParallelCurrentLimit
        {
            get => _parallelCurrentLimit;
            set => SetProperty(ref _parallelCurrentLimit, value);
        }

        /// <summary>
        /// RS232接口支持
        /// </summary>
        public bool InterfaceRS232
        {
            get => _interfaceRS232;
            set => SetProperty(ref _interfaceRS232, value);
        }

        /// <summary>
        /// USB接口类型（如 "USBTMC", "VCP"）
        /// </summary>
        public string InterfaceUSB
        {
            get => _interfaceUSB;
            set => SetProperty(ref _interfaceUSB, value);
        }

        /// <summary>
        /// GPIB接口支持
        /// </summary>
        public bool InterfaceGPIB
        {
            get => _interfaceGPIB;
            set => SetProperty(ref _interfaceGPIB, value);
        }

        /// <summary>
        /// LAN接口支持（C系列）
        /// </summary>
        public bool InterfaceLAN
        {
            get => _interfaceLAN;
            set => SetProperty(ref _interfaceLAN, value);
        }

        /// <summary>
        /// GPIB地址范围
        /// </summary>
        public string GpibAddressRange
        {
            get => _gpibAddressRange;
            set => SetProperty(ref _gpibAddressRange, value);
        }

        /// <summary>
        /// 负载调节率
        /// </summary>
        public string LoadRegulation
        {
            get => _loadRegulation;
            set => SetProperty(ref _loadRegulation, value);
        }

        /// <summary>
        /// 电源调节率
        /// </summary>
        public string LineRegulation
        {
            get => _lineRegulation;
            set => SetProperty(ref _lineRegulation, value);
        }

        /// <summary>
        /// 过压保护值
        /// </summary>
        public string OverVoltageProtection
        {
            get => _overVoltageProtection;
            set => SetProperty(ref _overVoltageProtection, value);
        }

        // IT-M3900D系列属性 - 并联配置
        /// <summary>
        /// 是否为主机（并联配置）
        /// </summary>
        public bool IsMasterUnit
        {
            get => _isMasterUnit;
            set => SetProperty(ref _isMasterUnit, value);
        }

        /// <summary>
        /// 从机数量（并联配置）
        /// </summary>
        public int SlaveCount
        {
            get => _slaveCount;
            set => SetProperty(ref _slaveCount, value);
        }

        /// <summary>
        /// 光纤连接是否启用（并联配置）
        /// </summary>
        public bool FiberConnectionEnabled
        {
            get => _fiberConnectionEnabled;
            set => SetProperty(ref _fiberConnectionEnabled, value);
        }

        /// <summary>
        /// 最大并联台数
        /// </summary>
        public int ParallelMaxUnits
        {
            get => _parallelMaxUnits;
            set => SetProperty(ref _parallelMaxUnits, value);
        }

        // IT-M3900D系列属性 - 输出控制
        /// <summary>
        /// CC/CV优先权模式
        /// </summary>
        public PowerSupplyPriorityMode PriorityMode
        {
            get => _priorityMode;
            set => SetProperty(ref _priorityMode, value);
        }

        /// <summary>
        /// 可编程串联内阻 (0-0.7Ω)
        /// </summary>
        public double SeriesResistance
        {
            get => _seriesResistance;
            set => SetProperty(ref _seriesResistance, value);
        }

        /// <summary>
        /// Sense补偿电压 (≤10V)
        /// </summary>
        public double SenseCompensationVoltage
        {
            get => _senseCompensationVoltage;
            set => SetProperty(ref _senseCompensationVoltage, value);
        }

        // IT-M3900D系列属性 - 输入电源
        /// <summary>
        /// 输入电压类型（三相/单相）
        /// </summary>
        public string InputVoltageType
        {
            get => _inputVoltageType;
            set => SetProperty(ref _inputVoltageType, value);
        }

        /// <summary>
        /// 最大AC视在功率 (kVA)
        /// </summary>
        public double MaxACApparentPower
        {
            get => _maxACApparentPower;
            set => SetProperty(ref _maxACApparentPower, value);
        }

        /// <summary>
        /// 功率因素
        /// </summary>
        public double PowerFactor
        {
            get => _powerFactor;
            set => SetProperty(ref _powerFactor, value);
        }

        /// <summary>
        /// 最大效率 (%)
        /// </summary>
        public double MaxEfficiency
        {
            get => _maxEfficiency;
            set => SetProperty(ref _maxEfficiency, value);
        }

        // IT-M3900D系列属性 - 功能特性
        /// <summary>
        /// 支持List功能
        /// </summary>
        public bool SupportListFunction
        {
            get => _supportListFunction;
            set => SetProperty(ref _supportListFunction, value);
        }

        /// <summary>
        /// 最大List步骤数
        /// </summary>
        public int MaxListSteps
        {
            get => _maxListSteps;
            set => SetProperty(ref _maxListSteps, value);
        }

        /// <summary>
        /// 支持任意波形发生
        /// </summary>
        public bool SupportArbitraryWaveform
        {
            get => _supportArbitraryWaveform;
            set => SetProperty(ref _supportArbitraryWaveform, value);
        }

        /// <summary>
        /// 内置Web服务器
        /// </summary>
        public bool BuiltInWebServer
        {
            get => _builtInWebServer;
            set => SetProperty(ref _builtInWebServer, value);
        }

        // 其他通信接口
        /// <summary>
        /// CAN接口支持
        /// </summary>
        public bool InterfaceCAN
        {
            get => _interfaceCAN;
            set => SetProperty(ref _interfaceCAN, value);
        }

        /// <summary>
        /// 数字I/O接口支持
        /// </summary>
        public bool InterfaceDigitalIO
        {
            get => _interfaceDigitalIO;
            set => SetProperty(ref _interfaceDigitalIO, value);
        }

        // CH3独立规格属性
        /// <summary>
        /// CH3最大电压 (V)
        /// </summary>
        public double Ch3MaxVoltage
        {
            get => _ch3MaxVoltage;
            set => SetProperty(ref _ch3MaxVoltage, value);
        }

        /// <summary>
        /// CH3最大电流 (A)
        /// </summary>
        public double Ch3MaxCurrent
        {
            get => _ch3MaxCurrent;
            set => SetProperty(ref _ch3MaxCurrent, value);
        }

        /// <summary>
        /// CH3功率等级 (W)
        /// </summary>
        public double Ch3PowerRating
        {
            get => _ch3PowerRating;
            set => SetProperty(ref _ch3PowerRating, value);
        }

        /// <summary>
        /// CH3过压保护值
        /// </summary>
        public string Ch3OverVoltageProtection
        {
            get => _ch3OverVoltageProtection;
            set => SetProperty(ref _ch3OverVoltageProtection, value);
        }

        // IT-M3900D 系列新增属性
        
        /// <summary>
        /// 设定值电压精确度
        /// </summary>
        public string SetVoltageAccuracy
        {
            get => _setVoltageAccuracy;
            set => SetProperty(ref _setVoltageAccuracy, value);
        }

        /// <summary>
        /// 设定值电流精确度
        /// </summary>
        public string SetCurrentAccuracy
        {
            get => _setCurrentAccuracy;
            set => SetProperty(ref _setCurrentAccuracy, value);
        }

        /// <summary>
        /// 设定值功率精确度
        /// </summary>
        public string SetPowerAccuracy
        {
            get => _setPowerAccuracy;
            set => SetProperty(ref _setPowerAccuracy, value);
        }

        /// <summary>
        /// 设定值电阻精确度
        /// </summary>
        public string SetResistanceAccuracy
        {
            get => _setResistanceAccuracy;
            set => SetProperty(ref _setResistanceAccuracy, value);
        }

        /// <summary>
        /// 回读值电压精确度
        /// </summary>
        public string ReadbackVoltageAccuracy
        {
            get => _readbackVoltageAccuracy;
            set => SetProperty(ref _readbackVoltageAccuracy, value);
        }

        /// <summary>
        /// 回读值电流精确度
        /// </summary>
        public string ReadbackCurrentAccuracy
        {
            get => _readbackCurrentAccuracy;
            set => SetProperty(ref _readbackCurrentAccuracy, value);
        }

        /// <summary>
        /// 回读值功率精确度
        /// </summary>
        public string ReadbackPowerAccuracy
        {
            get => _readbackPowerAccuracy;
            set => SetProperty(ref _readbackPowerAccuracy, value);
        }

        /// <summary>
        /// 电压解析度
        /// </summary>
        public string VoltageResolution
        {
            get => _voltageResolution;
            set => SetProperty(ref _voltageResolution, value);
        }

        /// <summary>
        /// 电流解析度
        /// </summary>
        public string CurrentResolution
        {
            get => _currentResolution;
            set => SetProperty(ref _currentResolution, value);
        }

        /// <summary>
        /// 功率解析度
        /// </summary>
        public string PowerResolution
        {
            get => _powerResolution;
            set => SetProperty(ref _powerResolution, value);
        }

        /// <summary>
        /// 电阻解析度
        /// </summary>
        public string ResistanceResolution
        {
            get => _resistanceResolution;
            set => SetProperty(ref _resistanceResolution, value);
        }

        /// <summary>
        /// 设定值电压温度系数
        /// </summary>
        public string SetVoltageTempCoeff
        {
            get => _setVoltageTempCoeff;
            set => SetProperty(ref _setVoltageTempCoeff, value);
        }

        /// <summary>
        /// 设定值电流温度系数
        /// </summary>
        public string SetCurrentTempCoeff
        {
            get => _setCurrentTempCoeff;
            set => SetProperty(ref _setCurrentTempCoeff, value);
        }

        /// <summary>
        /// 回读值电压温度系数
        /// </summary>
        public string ReadbackVoltageTempCoeff
        {
            get => _readbackVoltageTempCoeff;
            set => SetProperty(ref _readbackVoltageTempCoeff, value);
        }

        /// <summary>
        /// 回读值电流温度系数
        /// </summary>
        public string ReadbackCurrentTempCoeff
        {
            get => _readbackCurrentTempCoeff;
            set => SetProperty(ref _readbackCurrentTempCoeff, value);
        }

        /// <summary>
        /// 上升时间（空载）
        /// </summary>
        public string RiseTimeNoLoad
        {
            get => _riseTimeNoLoad;
            set => SetProperty(ref _riseTimeNoLoad, value);
        }

        /// <summary>
        /// 上升时间（满载）
        /// </summary>
        public string RiseTimeFullLoad
        {
            get => _riseTimeFullLoad;
            set => SetProperty(ref _riseTimeFullLoad, value);
        }

        /// <summary>
        /// 下降时间（空载）
        /// </summary>
        public string FallTimeNoLoad
        {
            get => _fallTimeNoLoad;
            set => SetProperty(ref _fallTimeNoLoad, value);
        }

        /// <summary>
        /// 下降时间（满载）
        /// </summary>
        public string FallTimeFullLoad
        {
            get => _fallTimeFullLoad;
            set => SetProperty(ref _fallTimeFullLoad, value);
        }

        /// <summary>
        /// 动态响应时间
        /// </summary>
        public string DynamicResponseTime
        {
            get => _dynamicResponseTime;
            set => SetProperty(ref _dynamicResponseTime, value);
        }

        /// <summary>
        /// 编程响应时间
        /// </summary>
        public string ProgrammingResponseTime
        {
            get => _programmingResponseTime;
            set => SetProperty(ref _programmingResponseTime, value);
        }

        /// <summary>
        /// 电压纹波峰值
        /// </summary>
        public string VoltageRipplePeak
        {
            get => _voltageRipplePeak;
            set => SetProperty(ref _voltageRipplePeak, value);
        }

        /// <summary>
        /// 电压纹波RMS
        /// </summary>
        public string VoltageRippleRms
        {
            get => _voltageRippleRms;
            set => SetProperty(ref _voltageRippleRms, value);
        }

        /// <summary>
        /// 电源调节率（电压）
        /// </summary>
        public string LineRegulationVoltage
        {
            get => _lineRegulationVoltage;
            set => SetProperty(ref _lineRegulationVoltage, value);
        }

        /// <summary>
        /// 电源调节率（电流）
        /// </summary>
        public string LineRegulationCurrent
        {
            get => _lineRegulationCurrent;
            set => SetProperty(ref _lineRegulationCurrent, value);
        }

        /// <summary>
        /// 负载调节率（电压）
        /// </summary>
        public string LoadRegulationVoltage
        {
            get => _loadRegulationVoltage;
            set => SetProperty(ref _loadRegulationVoltage, value);
        }

        /// <summary>
        /// 负载调节率（电流）
        /// </summary>
        public string LoadRegulationCurrent
        {
            get => _loadRegulationCurrent;
            set => SetProperty(ref _loadRegulationCurrent, value);
        }

        /// <summary>
        /// 电流谐波
        /// </summary>
        public string CurrentHarmonic
        {
            get => _currentHarmonic;
            set => SetProperty(ref _currentHarmonic, value);
        }

        /// <summary>
        /// 过流保护值
        /// </summary>
        public string OverCurrentProtection
        {
            get => _overCurrentProtection;
            set => SetProperty(ref _overCurrentProtection, value);
        }

        /// <summary>
        /// 过功率保护值
        /// </summary>
        public string OverPowerProtection
        {
            get => _overPowerProtection;
            set => SetProperty(ref _overPowerProtection, value);
        }

        /// <summary>
        /// 外部模拟量电流编程
        /// </summary>
        public string ExternalAnalogCurrentProgramming
        {
            get => _externalAnalogCurrentProgramming;
            set => SetProperty(ref _externalAnalogCurrentProgramming, value);
        }

        /// <summary>
        /// 外部模拟量电流监视
        /// </summary>
        public string ExternalAnalogCurrentMonitoring
        {
            get => _externalAnalogCurrentMonitoring;
            set => SetProperty(ref _externalAnalogCurrentMonitoring, value);
        }

        /// <summary>
        /// 外部模拟量电压编程
        /// </summary>
        public string ExternalAnalogVoltageProgramming
        {
            get => _externalAnalogVoltageProgramming;
            set => SetProperty(ref _externalAnalogVoltageProgramming, value);
        }

        /// <summary>
        /// 外部模拟量电压监视
        /// </summary>
        public string ExternalAnalogVoltageMonitoring
        {
            get => _externalAnalogVoltageMonitoring;
            set => SetProperty(ref _externalAnalogVoltageMonitoring, value);
        }

        /// <summary>
        /// 工作温度范围
        /// </summary>
        public string OperatingTemperature
        {
            get => _operatingTemperature;
            set => SetProperty(ref _operatingTemperature, value);
        }

        /// <summary>
        /// 存储温度范围
        /// </summary>
        public string StorageTemperature
        {
            get => _storageTemperature;
            set => SetProperty(ref _storageTemperature, value);
        }

        /// <summary>
        /// 工作湿度范围
        /// </summary>
        public string OperatingHumidity
        {
            get => _operatingHumidity;
            set => SetProperty(ref _operatingHumidity, value);
        }

        /// <summary>
        /// 海拔高度限制
        /// </summary>
        public string AltitudeLimit
        {
            get => _altitudeLimit;
            set => SetProperty(ref _altitudeLimit, value);
        }

        /// <summary>
        /// 防护等级
        /// </summary>
        public string ProtectionRating
        {
            get => _protectionRating;
            set => SetProperty(ref _protectionRating, value);
        }

        /// <summary>
        /// 整机尺寸
        /// </summary>
        public string DimensionsOverall
        {
            get => _dimensionsOverall;
            set => SetProperty(ref _dimensionsOverall, value);
        }

        /// <summary>
        /// 裸机尺寸
        /// </summary>
        public string DimensionsBare
        {
            get => _dimensionsBare;
            set => SetProperty(ref _dimensionsBare, value);
        }

        /// <summary>
        /// 净重 (kg)
        /// </summary>
        public double NetWeight
        {
            get => _netWeight;
            set => SetProperty(ref _netWeight, value);
        }

        /// <summary>
        /// 冷却方式
        /// </summary>
        public string CoolingMethod
        {
            get => _coolingMethod;
            set => SetProperty(ref _coolingMethod, value);
        }

        public override string DeviceTypeName => "程控电源";

        public PowerSupplyDevice() : base()
        {
            DeviceType = Constants.DeviceConstants.Type.Instrument;
            ParentNode = "程控电源";
            ChannelCount = 3;
            MaxVoltage = 30;
            MaxCurrent = 6;
            PowerRating = 180;
            OperationMode = PowerSupplyOperationMode.Independent;
            ChannelCombination = "独立";
            InitializeChildren();
        }

        public PowerSupplyDevice(string name, string slotPosition) : base()
        {
            DeviceType = Constants.DeviceConstants.Type.Instrument;
            ParentNode = "程控电源";
            ChannelCount = 3;
            MaxVoltage = 30;
            MaxCurrent = 6;
            PowerRating = 180;
            OperationMode = PowerSupplyOperationMode.Independent;
            ChannelCombination = "独立";

            ParseDeviceName(name); // 使用基类方法
            SlotPosition = slotPosition;
            
            // 根据型号配置设备
            ConfigureByModel(name);

            InitializeChildren();
        }

        public override void InitializeChildren()
        {
            Children.Clear();
            
            // 单通道设备（IT-M3912D）
            if (ChannelCount == 1)
            {
                var outputNode = new PowerSupplyChannelNode
                {
                    Name = "输出通道",
                    ParentNode = "程控电源",
                    ChannelNumber = 1,
                    Model = "单通道",
                    SlotPosition = $"CH1（0–{MaxVoltage}V/{MaxCurrent}A）",
                    Status = Constants.DeviceConstants.Status.Normal
                };
                Children.Add(outputNode);
            }
            // 多通道设备（IT-N6332B等）
            else if (ChannelCount > 1)
            {
                var outputNode = new PowerSupplyChannelNode
                {
                    Name = "输出通道",
                    ParentNode = "程控电源",
                    ChannelNumber = ChannelCount,
                    Model = $"{ChannelCount}通道",
                    SlotPosition = $"CH1–CH{ChannelCount}",
                    Status = Constants.DeviceConstants.Status.Normal
                };
                Children.Add(outputNode);
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

            // 添加通道子节点信息
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
        /// 根据型号配置设备规格
        /// </summary>
        private void ConfigureByModel(string deviceName)
        {
            if (string.IsNullOrEmpty(deviceName))
                return;

            // 尝试使用配置器工厂自动配置
            bool configured = DeviceConfiguratorFactory.TryConfigure(this, deviceName);
            
            // 如果没有找到专用配置器，使用默认配置
            if (!configured)
            {
                // 保持默认值（在构造函数中已设置）
            }
            
        }
        public override string GetConnectionString()
        {
            return $"PowerSupply::{Manufacturer}::{Model}::{SlotPosition}";
        }

        public override bool ValidateConfiguration()
        {
            return base.ValidateConfiguration() && 
                   ChannelCount > 0 && 
                   MaxVoltage > 0 && 
                   MaxCurrent > 0 && 
                   PowerRating > 0;
        }
    }

    /// <summary>
    /// 程控电源通道子节点类
    /// </summary>
    public class PowerSupplyChannelNode : SubNodeBase
    {
        private int _channelNumber;
        private double _maxVoltage;
        private double _maxCurrent;
        private double _powerRating;
        private string _overVoltageProtection;
        private string _loadRegulation;
        private string _lineRegulation;

        /// <summary>
        /// 通道编号（1-3）
        /// </summary>
        public int ChannelNumber
        {
            get => _channelNumber;
            set => SetProperty(ref _channelNumber, value);
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
        /// 功率等级 (W)
        /// </summary>
        public double PowerRating
        {
            get => _powerRating;
            set => SetProperty(ref _powerRating, value);
        }

        /// <summary>
        /// 过压保护值
        /// </summary>
        public string OverVoltageProtection
        {
            get => _overVoltageProtection;
            set => SetProperty(ref _overVoltageProtection, value);
        }

        /// <summary>
        /// 负载调节率
        /// </summary>
        public string LoadRegulation
        {
            get => _loadRegulation;
            set => SetProperty(ref _loadRegulation, value);
        }

        /// <summary>
        /// 电源调节率
        /// </summary>
        public string LineRegulation
        {
            get => _lineRegulation;
            set => SetProperty(ref _lineRegulation, value);
        }

        public override string DeviceTypeName => $"CH{ChannelNumber}";

        public PowerSupplyChannelNode() : base()
        {
            ChannelNumber = 1;
            MaxVoltage = 30;
            MaxCurrent = 6;
            PowerRating = 180;
            OverVoltageProtection = "31V";
            LoadRegulation = "≤0.01%+3mV (V), ≤0.01%+3mA (A)";
            LineRegulation = "≤0.01%+3mV (V), ≤0.01%+3mA (A)";
        }

        public override string GetConnectionString()
        {
            return $"PowerSupplyChannel::CH{ChannelNumber}::{MaxVoltage}V::{MaxCurrent}A::{PowerRating}W";
        }

        public override bool ValidateConfiguration()
        {
            return base.ValidateConfiguration() && 
                   ChannelNumber > 0 && 
                   MaxVoltage > 0 && 
                   MaxCurrent > 0 && 
                   PowerRating > 0;
        }
    }
}
