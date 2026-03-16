using System;
using System.Collections.ObjectModel;
using System.Linq;
using MeasureControl.Models;
using MeasureControl.Models.Devices.DeviceCategories;

namespace MeasureControl.Models.Devices
{
    /// <summary>
    /// 频率计测量模式枚举
    /// </summary>
    public enum FrequencyCounterMeasurementMode
    {
        Frequency,          // 频率测量
        Period,             // 周期测量
        TimeInterval,       // 时间间隔测量
        PulseWidth,         // 脉冲宽度测量
        DutyCycle,          // 占空比测量
        RiseTime,           // 上升时间测量
        FallTime,           // 下降时间测量
        Phase,              // 相位测量
        Ratio,              // 频率比测量
        TotalizeCount       // 累计计数
    }

    /// <summary>
    /// 触发方式枚举
    /// </summary>
    public enum FrequencyCounterTriggerMode
    {
        Auto,               // 自动触发
        Manual,             // 手动触发
        External,           // 外部触发
        Bus,                // 总线触发
        Gated               // 门控触发
    }

    /// <summary>
    /// 触发电平类型枚举
    /// </summary>
    public enum TriggerLevelType
    {
        Auto,               // 自动电平
        Manual,             // 手动电平
        TTL,                // TTL电平
        ECL,                // ECL电平
        CMOS,               // CMOS电平
        NIM                 // NIM电平
    }

    /// <summary>
    /// 输入耦合方式枚举
    /// </summary>
    public enum FrequencyCounterCoupling
    {
        DC,                 // DC耦合
        AC,                 // AC耦合
        LowPass,            // 低通滤波
        HighPass            // 高通滤波
    }

    /// <summary>
    /// 输入阻抗枚举
    /// </summary>
    public enum FrequencyCounterImpedance
    {
        Ohm50,              // 50Ω
        Ohm1M               // 1MΩ
    }

    /// <summary>
    /// 统计分析功能枚举
    /// </summary>
    public enum StatisticsFunction
    {
        Mean,               // 平均值
        StdDev,             // 标准差
        Min,                // 最小值
        Max,                // 最大值
        AllanDeviation,     // Allan偏差
        Jitter,             // 抖动
        Histogram           // 直方图
    }

    /// <summary>
    /// 频率计数器/定时器设备类（基于 Keysight 53200A 系列）
    /// </summary>
    public class FrequencyCounterDevice : InstrumentDeviceBase
    {
        #region 私有字段

        // 基本参数
        private int _channelCount;
        private string _maxFrequency;
        private string _timeIntervalResolution;
        private int _frequencyResolution;
        private string _gateTime;

        // 测量功能
        private FrequencyCounterMeasurementMode _measurementMode;
        private bool _singleShotCapable;
        private bool _continuousMeasurement;
        private int _measurementSpeed;

        // 分辨率参数
        private string _singleShotResolution;
        private string _continuousResolution;
        private int _digitsPerSecond;

        // 频率测量
        private string _frequencyRange;
        private string _frequencyAccuracy;
        private string _frequencySensitivity;
        private string _rfFrequencyRange;

        // 时间间隔测量
        private string _timeIntervalRange;
        private string _timeIntervalAccuracy;
        private string _timeIntervalJitter;
        private bool _singleShotTimeInterval;

        // 触发设置
        private FrequencyCounterTriggerMode _triggerMode;
        private TriggerLevelType _triggerLevelType;
        private double _triggerLevel;
        private string _triggerSlope;
        private double _triggerHysteresis;

        // 输入特性
        private FrequencyCounterCoupling _inputCoupling;
        private FrequencyCounterImpedance _inputImpedance;
        private string _inputVoltageRange;
        private string _inputSensitivity;
        private bool _inputAttenuator;

        // 门控和采样
        private string _minGateTime;
        private string _maxGateTime;
        private double _gateTimeValue;
        private int _samplesPerMeasurement;
        private int _bufferSize;

        // 分析功能
        private bool _builtInAnalysis;
        private bool _statisticsSupport;
        private bool _trendPlotting;
        private bool _histogramAnalysis;
        private bool _allanDeviation;
        private string _analysisTypes;

        // 显示和绘图
        private bool _colorDisplay;
        private bool _graphicalDisplay;
        private string _displayResolution;
        private bool _realTimePlotting;

        // 时基和参考
        private string _timeBaseType;
        private string _timeBaseAccuracy;
        private string _timeBaseStability;
        private bool _externalRefInput;
        private string _extRefFrequency;
        private bool _internalOvenOscillator;

        // 接口和通信
        private bool _gpibInterface;
        private bool _lanInterface;
        private bool _usbInterface;
        private bool _digitalIO;
        private bool _scpiProgramming;
        private string _remoteInterfaces;

        // 系统参数
        private string _powerRequirement;
        private string _operatingTemp;
        private string _storageTemp;
        private string _humidity;
        private string _altitude;

        // 物理尺寸
        private string _dimensions;
        private double _weight;
        private bool _rackMountabel;
        private string _formFactor;

        // 扩展功能
        private bool _mathFunctions;
        private bool _limitTesting;
        private bool _passFailTest;
        private string _dataLogging;
        private int _memoryDepth;

        // 测量功能扩展
        private string _timeStampResolution;
        private bool _timeStampSupport;

        // 通道配置
        private bool _ch3Available;
        private string _ch3FrequencyRange;

        // 输入特性补充
        private string _inputDamageLevel;
        private string _ch1Ch2InputRange;
        private string _ch3InputRange;

        // 触发特性补充
        private string _autoTriggerLevel;
        private string _externalGateDelay;

        // 通信接口补充
        private string _lxiCompliance;
        private bool _webInterface;
        private bool _usbTmcSupport;

        // 软件支持
        private bool _benchVueSupport;
        private string _dataExportFormats;

        // 保修与支持
        private string _warrantyPeriod;
        private string _calibrationInterval;

        // 选配件
        private bool _ocxoOption;
        private bool _batteryOption;

        #endregion

        #region 公共属性

        /// <summary>
        /// 通道数量
        /// </summary>
        public int ChannelCount
        {
            get => _channelCount;
            set => SetProperty(ref _channelCount, value);
        }

        /// <summary>
        /// 最大频率 (例如: "350 MHz", "15 GHz")
        /// </summary>
        public string MaxFrequency
        {
            get => _maxFrequency;
            set => SetProperty(ref _maxFrequency, value);
        }

        /// <summary>
        /// 时间间隔分辨率 (例如: "20 ps", "100 ps")
        /// </summary>
        public string TimeIntervalResolution
        {
            get => _timeIntervalResolution;
            set => SetProperty(ref _timeIntervalResolution, value);
        }

        /// <summary>
        /// 频率分辨率 (位/秒)
        /// </summary>
        public int FrequencyResolution
        {
            get => _frequencyResolution;
            set => SetProperty(ref _frequencyResolution, value);
        }

        /// <summary>
        /// 门时间 (例如: "1 s", "100 ms")
        /// </summary>
        public string GateTime
        {
            get => _gateTime;
            set => SetProperty(ref _gateTime, value);
        }

        /// <summary>
        /// 测量模式
        /// </summary>
        public FrequencyCounterMeasurementMode MeasurementMode
        {
            get => _measurementMode;
            set => SetProperty(ref _measurementMode, value);
        }

        /// <summary>
        /// 单次测量能力
        /// </summary>
        public bool SingleShotCapable
        {
            get => _singleShotCapable;
            set => SetProperty(ref _singleShotCapable, value);
        }

        /// <summary>
        /// 连续测量
        /// </summary>
        public bool ContinuousMeasurement
        {
            get => _continuousMeasurement;
            set => SetProperty(ref _continuousMeasurement, value);
        }

        /// <summary>
        /// 测量速度 (测量/秒)
        /// </summary>
        public int MeasurementSpeed
        {
            get => _measurementSpeed;
            set => SetProperty(ref _measurementSpeed, value);
        }

        /// <summary>
        /// 单次分辨率
        /// </summary>
        public string SingleShotResolution
        {
            get => _singleShotResolution;
            set => SetProperty(ref _singleShotResolution, value);
        }

        /// <summary>
        /// 连续分辨率
        /// </summary>
        public string ContinuousResolution
        {
            get => _continuousResolution;
            set => SetProperty(ref _continuousResolution, value);
        }

        /// <summary>
        /// 位/秒
        /// </summary>
        public int DigitsPerSecond
        {
            get => _digitsPerSecond;
            set => SetProperty(ref _digitsPerSecond, value);
        }

        /// <summary>
        /// 频率范围
        /// </summary>
        public string FrequencyRange
        {
            get => _frequencyRange;
            set => SetProperty(ref _frequencyRange, value);
        }

        /// <summary>
        /// 频率精度
        /// </summary>
        public string FrequencyAccuracy
        {
            get => _frequencyAccuracy;
            set => SetProperty(ref _frequencyAccuracy, value);
        }

        /// <summary>
        /// 频率灵敏度
        /// </summary>
        public string FrequencySensitivity
        {
            get => _frequencySensitivity;
            set => SetProperty(ref _frequencySensitivity, value);
        }

        /// <summary>
        /// RF频率范围
        /// </summary>
        public string RfFrequencyRange
        {
            get => _rfFrequencyRange;
            set => SetProperty(ref _rfFrequencyRange, value);
        }

        /// <summary>
        /// 时间间隔范围
        /// </summary>
        public string TimeIntervalRange
        {
            get => _timeIntervalRange;
            set => SetProperty(ref _timeIntervalRange, value);
        }

        /// <summary>
        /// 时间间隔精度
        /// </summary>
        public string TimeIntervalAccuracy
        {
            get => _timeIntervalAccuracy;
            set => SetProperty(ref _timeIntervalAccuracy, value);
        }

        /// <summary>
        /// 时间间隔抖动
        /// </summary>
        public string TimeIntervalJitter
        {
            get => _timeIntervalJitter;
            set => SetProperty(ref _timeIntervalJitter, value);
        }

        /// <summary>
        /// 单次时间间隔测量
        /// </summary>
        public bool SingleShotTimeInterval
        {
            get => _singleShotTimeInterval;
            set => SetProperty(ref _singleShotTimeInterval, value);
        }

        /// <summary>
        /// 触发模式
        /// </summary>
        public FrequencyCounterTriggerMode TriggerMode
        {
            get => _triggerMode;
            set => SetProperty(ref _triggerMode, value);
        }

        /// <summary>
        /// 触发电平类型
        /// </summary>
        public TriggerLevelType TriggerLevelType
        {
            get => _triggerLevelType;
            set => SetProperty(ref _triggerLevelType, value);
        }

        /// <summary>
        /// 触发电平 (V)
        /// </summary>
        public double TriggerLevel
        {
            get => _triggerLevel;
            set => SetProperty(ref _triggerLevel, value);
        }

        /// <summary>
        /// 触发斜率 (Positive/Negative)
        /// </summary>
        public string TriggerSlope
        {
            get => _triggerSlope;
            set => SetProperty(ref _triggerSlope, value);
        }

        /// <summary>
        /// 触发迟滞 (V)
        /// </summary>
        public double TriggerHysteresis
        {
            get => _triggerHysteresis;
            set => SetProperty(ref _triggerHysteresis, value);
        }

        /// <summary>
        /// 输入耦合
        /// </summary>
        public FrequencyCounterCoupling InputCoupling
        {
            get => _inputCoupling;
            set => SetProperty(ref _inputCoupling, value);
        }

        /// <summary>
        /// 输入阻抗
        /// </summary>
        public FrequencyCounterImpedance InputImpedance
        {
            get => _inputImpedance;
            set => SetProperty(ref _inputImpedance, value);
        }

        /// <summary>
        /// 输入电压范围
        /// </summary>
        public string InputVoltageRange
        {
            get => _inputVoltageRange;
            set => SetProperty(ref _inputVoltageRange, value);
        }

        /// <summary>
        /// 输入灵敏度
        /// </summary>
        public string InputSensitivity
        {
            get => _inputSensitivity;
            set => SetProperty(ref _inputSensitivity, value);
        }

        /// <summary>
        /// 输入衰减器
        /// </summary>
        public bool InputAttenuator
        {
            get => _inputAttenuator;
            set => SetProperty(ref _inputAttenuator, value);
        }

        /// <summary>
        /// 最小门时间
        /// </summary>
        public string MinGateTime
        {
            get => _minGateTime;
            set => SetProperty(ref _minGateTime, value);
        }

        /// <summary>
        /// 最大门时间
        /// </summary>
        public string MaxGateTime
        {
            get => _maxGateTime;
            set => SetProperty(ref _maxGateTime, value);
        }

        /// <summary>
        /// 门时间值 (秒)
        /// </summary>
        public double GateTimeValue
        {
            get => _gateTimeValue;
            set => SetProperty(ref _gateTimeValue, value);
        }

        /// <summary>
        /// 每次测量采样数
        /// </summary>
        public int SamplesPerMeasurement
        {
            get => _samplesPerMeasurement;
            set => SetProperty(ref _samplesPerMeasurement, value);
        }

        /// <summary>
        /// 缓冲区大小
        /// </summary>
        public int BufferSize
        {
            get => _bufferSize;
            set => SetProperty(ref _bufferSize, value);
        }

        /// <summary>
        /// 内置分析功能
        /// </summary>
        public bool BuiltInAnalysis
        {
            get => _builtInAnalysis;
            set => SetProperty(ref _builtInAnalysis, value);
        }

        /// <summary>
        /// 统计支持
        /// </summary>
        public bool StatisticsSupport
        {
            get => _statisticsSupport;
            set => SetProperty(ref _statisticsSupport, value);
        }

        /// <summary>
        /// 趋势绘图
        /// </summary>
        public bool TrendPlotting
        {
            get => _trendPlotting;
            set => SetProperty(ref _trendPlotting, value);
        }

        /// <summary>
        /// 直方图分析
        /// </summary>
        public bool HistogramAnalysis
        {
            get => _histogramAnalysis;
            set => SetProperty(ref _histogramAnalysis, value);
        }

        /// <summary>
        /// Allan偏差分析
        /// </summary>
        public bool AllanDeviation
        {
            get => _allanDeviation;
            set => SetProperty(ref _allanDeviation, value);
        }

        /// <summary>
        /// 分析类型 (逗号分隔)
        /// </summary>
        public string AnalysisTypes
        {
            get => _analysisTypes;
            set => SetProperty(ref _analysisTypes, value);
        }

        /// <summary>
        /// 彩色显示屏
        /// </summary>
        public bool ColorDisplay
        {
            get => _colorDisplay;
            set => SetProperty(ref _colorDisplay, value);
        }

        /// <summary>
        /// 图形化显示
        /// </summary>
        public bool GraphicalDisplay
        {
            get => _graphicalDisplay;
            set => SetProperty(ref _graphicalDisplay, value);
        }

        /// <summary>
        /// 显示分辨率
        /// </summary>
        public string DisplayResolution
        {
            get => _displayResolution;
            set => SetProperty(ref _displayResolution, value);
        }

        /// <summary>
        /// 实时绘图
        /// </summary>
        public bool RealTimePlotting
        {
            get => _realTimePlotting;
            set => SetProperty(ref _realTimePlotting, value);
        }

        /// <summary>
        /// 时基类型 (OCXO, TCXO, Rubidium, etc.)
        /// </summary>
        public string TimeBaseType
        {
            get => _timeBaseType;
            set => SetProperty(ref _timeBaseType, value);
        }

        /// <summary>
        /// 时基精度
        /// </summary>
        public string TimeBaseAccuracy
        {
            get => _timeBaseAccuracy;
            set => SetProperty(ref _timeBaseAccuracy, value);
        }

        /// <summary>
        /// 时基稳定性
        /// </summary>
        public string TimeBaseStability
        {
            get => _timeBaseStability;
            set => SetProperty(ref _timeBaseStability, value);
        }

        /// <summary>
        /// 外部参考输入
        /// </summary>
        public bool ExternalRefInput
        {
            get => _externalRefInput;
            set => SetProperty(ref _externalRefInput, value);
        }

        /// <summary>
        /// 外部参考频率
        /// </summary>
        public string ExtRefFrequency
        {
            get => _extRefFrequency;
            set => SetProperty(ref _extRefFrequency, value);
        }

        /// <summary>
        /// 内置恒温晶振
        /// </summary>
        public bool InternalOvenOscillator
        {
            get => _internalOvenOscillator;
            set => SetProperty(ref _internalOvenOscillator, value);
        }

        /// <summary>
        /// GPIB接口
        /// </summary>
        public bool GpibInterface
        {
            get => _gpibInterface;
            set => SetProperty(ref _gpibInterface, value);
        }

        /// <summary>
        /// LAN接口
        /// </summary>
        public bool LanInterface
        {
            get => _lanInterface;
            set => SetProperty(ref _lanInterface, value);
        }

        /// <summary>
        /// USB接口
        /// </summary>
        public bool UsbInterface
        {
            get => _usbInterface;
            set => SetProperty(ref _usbInterface, value);
        }

        /// <summary>
        /// 数字I/O
        /// </summary>
        public bool DigitalIO
        {
            get => _digitalIO;
            set => SetProperty(ref _digitalIO, value);
        }

        /// <summary>
        /// SCPI编程支持
        /// </summary>
        public bool ScpiProgramming
        {
            get => _scpiProgramming;
            set => SetProperty(ref _scpiProgramming, value);
        }

        /// <summary>
        /// 远程接口 (逗号分隔)
        /// </summary>
        public string RemoteInterfaces
        {
            get => _remoteInterfaces;
            set => SetProperty(ref _remoteInterfaces, value);
        }

        /// <summary>
        /// 电源要求
        /// </summary>
        public string PowerRequirement
        {
            get => _powerRequirement;
            set => SetProperty(ref _powerRequirement, value);
        }

        /// <summary>
        /// 工作温度
        /// </summary>
        public string OperatingTemp
        {
            get => _operatingTemp;
            set => SetProperty(ref _operatingTemp, value);
        }

        /// <summary>
        /// 存储温度
        /// </summary>
        public string StorageTemp
        {
            get => _storageTemp;
            set => SetProperty(ref _storageTemp, value);
        }

        /// <summary>
        /// 湿度
        /// </summary>
        public string Humidity
        {
            get => _humidity;
            set => SetProperty(ref _humidity, value);
        }

        /// <summary>
        /// 海拔高度
        /// </summary>
        public string Altitude
        {
            get => _altitude;
            set => SetProperty(ref _altitude, value);
        }

        /// <summary>
        /// 尺寸 (W×H×D)
        /// </summary>
        public string Dimensions
        {
            get => _dimensions;
            set => SetProperty(ref _dimensions, value);
        }

        /// <summary>
        /// 重量 (kg)
        /// </summary>
        public double Weight
        {
            get => _weight;
            set => SetProperty(ref _weight, value);
        }

        /// <summary>
        /// 可机架安装
        /// </summary>
        public bool RackMountabel
        {
            get => _rackMountabel;
            set => SetProperty(ref _rackMountabel, value);
        }

        /// <summary>
        /// 外形因子 (例如: "1U Rack")
        /// </summary>
        public string FormFactor
        {
            get => _formFactor;
            set => SetProperty(ref _formFactor, value);
        }

        /// <summary>
        /// 数学函数
        /// </summary>
        public bool MathFunctions
        {
            get => _mathFunctions;
            set => SetProperty(ref _mathFunctions, value);
        }

        /// <summary>
        /// 限值测试
        /// </summary>
        public bool LimitTesting
        {
            get => _limitTesting;
            set => SetProperty(ref _limitTesting, value);
        }

        /// <summary>
        /// 合格/不合格测试
        /// </summary>
        public bool PassFailTest
        {
            get => _passFailTest;
            set => SetProperty(ref _passFailTest, value);
        }

        /// <summary>
        /// 数据记录
        /// </summary>
        public string DataLogging
        {
            get => _dataLogging;
            set => SetProperty(ref _dataLogging, value);
        }

        /// <summary>
        /// 存储深度
        /// </summary>
        public int MemoryDepth
        {
            get => _memoryDepth;
            set => SetProperty(ref _memoryDepth, value);
        }

        /// <summary>
        /// 时间戳分辨率
        /// </summary>
        public string TimeStampResolution
        {
            get => _timeStampResolution;
            set => SetProperty(ref _timeStampResolution, value);
        }

        /// <summary>
        /// 时间戳功能支持
        /// </summary>
        public bool TimeStampSupport
        {
            get => _timeStampSupport;
            set => SetProperty(ref _timeStampSupport, value);
        }

        /// <summary>
        /// CH3可选通道
        /// </summary>
        public bool Ch3Available
        {
            get => _ch3Available;
            set => SetProperty(ref _ch3Available, value);
        }

        /// <summary>
        /// CH3频率范围
        /// </summary>
        public string Ch3FrequencyRange
        {
            get => _ch3FrequencyRange;
            set => SetProperty(ref _ch3FrequencyRange, value);
        }

        /// <summary>
        /// 输入损伤电平
        /// </summary>
        public string InputDamageLevel
        {
            get => _inputDamageLevel;
            set => SetProperty(ref _inputDamageLevel, value);
        }

        /// <summary>
        /// CH1/CH2输入范围
        /// </summary>
        public string Ch1Ch2InputRange
        {
            get => _ch1Ch2InputRange;
            set => SetProperty(ref _ch1Ch2InputRange, value);
        }

        /// <summary>
        /// CH3输入范围
        /// </summary>
        public string Ch3InputRange
        {
            get => _ch3InputRange;
            set => SetProperty(ref _ch3InputRange, value);
        }

        /// <summary>
        /// 自动触发电平范围
        /// </summary>
        public string AutoTriggerLevel
        {
            get => _autoTriggerLevel;
            set => SetProperty(ref _autoTriggerLevel, value);
        }

        /// <summary>
        /// 外部门控延迟
        /// </summary>
        public string ExternalGateDelay
        {
            get => _externalGateDelay;
            set => SetProperty(ref _externalGateDelay, value);
        }

        /// <summary>
        /// LXI兼容性
        /// </summary>
        public string LxiCompliance
        {
            get => _lxiCompliance;
            set => SetProperty(ref _lxiCompliance, value);
        }

        /// <summary>
        /// Web界面支持
        /// </summary>
        public bool WebInterface
        {
            get => _webInterface;
            set => SetProperty(ref _webInterface, value);
        }

        /// <summary>
        /// USBTMC协议支持
        /// </summary>
        public bool UsbTmcSupport
        {
            get => _usbTmcSupport;
            set => SetProperty(ref _usbTmcSupport, value);
        }

        /// <summary>
        /// BenchVue软件支持
        /// </summary>
        public bool BenchVueSupport
        {
            get => _benchVueSupport;
            set => SetProperty(ref _benchVueSupport, value);
        }

        /// <summary>
        /// 数据导出格式
        /// </summary>
        public string DataExportFormats
        {
            get => _dataExportFormats;
            set => SetProperty(ref _dataExportFormats, value);
        }

        /// <summary>
        /// 保修期
        /// </summary>
        public string WarrantyPeriod
        {
            get => _warrantyPeriod;
            set => SetProperty(ref _warrantyPeriod, value);
        }

        /// <summary>
        /// 校准间隔
        /// </summary>
        public string CalibrationInterval
        {
            get => _calibrationInterval;
            set => SetProperty(ref _calibrationInterval, value);
        }

        /// <summary>
        /// OCXO高稳定时基选配
        /// </summary>
        public bool OcxoOption
        {
            get => _ocxoOption;
            set => SetProperty(ref _ocxoOption, value);
        }

        /// <summary>
        /// 电池便携电源选配
        /// </summary>
        public bool BatteryOption
        {
            get => _batteryOption;
            set => SetProperty(ref _batteryOption, value);
        }

        public override string DeviceTypeName => "频率计数器";

        #endregion

        #region 构造函数

        public FrequencyCounterDevice() : base()
        {
            DeviceType = "频率计数器";
            Name = "频率计数器";
            Model = "Keysight 53200A";
        }

        public FrequencyCounterDevice(string deviceName, string slotPosition)
            : base(deviceName, "", deviceName, slotPosition)
        {
            DeviceType = "频率计数器";

            Model = "53220A";
            // 默认参数
            ChannelCount = 2;
            MaxFrequency = "350 MHz";
            TimeIntervalResolution = "20 ps";
            FrequencyResolution = 12;
            GateTime = "1 s";

            // 测量功能
            MeasurementMode = FrequencyCounterMeasurementMode.Frequency;
            SingleShotCapable = true;
            ContinuousMeasurement = true;
            MeasurementSpeed = 1000;

            // 分析功能
            BuiltInAnalysis = true;
            StatisticsSupport = true;
            TrendPlotting = true;
            HistogramAnalysis = true;
            AllanDeviation = false;

            // 显示
            ColorDisplay = true;
            GraphicalDisplay = true;
            RealTimePlotting = true;

            // 接口
            GpibInterface = true;
            LanInterface = true;
            UsbInterface = true;
            ScpiProgramming = true;
            RemoteInterfaces = "GPIB, LAN, USB";

            // 物理参数
            Dimensions = "213 mm × 88 mm × 348 mm";
            Weight = 4.0;
            RackMountabel = true;
            FormFactor = "1U Rack";

            // 环境参数
            PowerRequirement = "AC 100-240 V, 50/60 Hz";
            OperatingTemp = "0°C ~ 55°C";
            StorageTemp = "-40°C ~ 70°C";
            Humidity = "5% ~ 95% RH (非冷凝)";
            Altitude = "< 3000 m";

            // 触发
            TriggerMode = FrequencyCounterTriggerMode.Auto;
            TriggerLevelType = TriggerLevelType.Auto;
            TriggerSlope = "Positive";

            // 输入
            InputCoupling = FrequencyCounterCoupling.DC;
            InputImpedance = FrequencyCounterImpedance.Ohm1M;

            Status = "正常";
        }

        #endregion

        #region 配置方法

        /// <summary>
        /// 配置为 Keysight 53220A (通用型)
        /// </summary>
        public void ConfigureAs53220A()
        {
            Model = "Keysight 53220A";
            ChannelCount = 2;
            MaxFrequency = "350 MHz";
            TimeIntervalResolution = "20 ps";  // 修正: 500 ps → 20 ps (单次)
            FrequencyResolution = 12;  // 12位/秒 (1秒门时间); 10位 (100ms)
            SingleShotResolution = "20 ps";  // 修正: 500 ps → 20 ps
            ContinuousResolution = "100 ps";  // 新增

            FrequencyRange = "DC ~ 350 MHz";
            FrequencyAccuracy = "±(精度 + 时间基准误差); 时间基准 ±1.5 ppm";  // 修正
            FrequencySensitivity = "20 mVrms (典型, <100 MHz); 40 mVrms (<350 MHz)";  // 修正

            TimeIntervalRange = "2 ns ~ 1000 s";
            TimeIntervalAccuracy = "±20 ps + 时基误差";  // 修正
            SingleShotTimeInterval = true;

            // 输入特性 - 修正
            InputVoltageRange = "±51 Vpk (1 MΩ); ±2.4 Vpk (50 Ω)";  // 修正
            InputSensitivity = "20 mVrms (典型, <100 MHz); 40 mVrms (<350 MHz)";  // 修正
            InputAttenuator = false;

            // 新增字段
            InputDamageLevel = "+27 V (1 MΩ); 5 Vrms (50 Ω)";
            Ch1Ch2InputRange = "±51 Vpk (1 MΩ); ±2.4 Vpk (50 Ω)";

            // 时间戳功能
            TimeStampResolution = "100 ps";
            TimeStampSupport = true;

            // CH3可选
            Ch3Available = true;
            Ch3FrequencyRange = "100 MHz ~ 350 MHz";
            Ch3InputRange = "±2.4 Vpk";

            // 时基参数 - 修正
            TimeBaseType = "标配: TCXO; 可选: OCXO";
            TimeBaseAccuracy = "±1.5 ppm (标配); ±50 ppb (可选OCXO)";  // 修正
            TimeBaseStability = "±0.5 ppm/年";
            InternalOvenOscillator = false;  // 标配无，可选有
            ExternalRefInput = true;
            ExtRefFrequency = "10 MHz (参考输入/输出)";
            OcxoOption = true;  // 可选配

            // 触发特性
            AutoTriggerLevel = "10% ~ 90% (频率 >10 Hz)";
            ExternalGateDelay = "<200 ns";

            // 门控 - 修正
            MinGateTime = "1 ms";  // 修正: 10 ms → 1 ms
            MaxGateTime = "1000 s";
            GateTimeValue = 1.0;
            BufferSize = 1000000;  // 1M 阅读/通道

            // 分析功能
            AllanDeviation = true;  // 53220A 支持 Allan 偏差
            AnalysisTypes = "统计(平均、标准差、Allan偏差), 趋势图, 直方图";

            // 显示 - 修正
            DisplayResolution = "4.3英寸彩色 TFT";  // 修正: "320 × 240" → "4.3英寸彩色 TFT"

            // 接口
            DigitalIO = false;
            LxiCompliance = "LXI Class C";
            WebInterface = true;  // Web 界面
            UsbTmcSupport = true;  // USBTMC 协议

            // 软件支持
            BenchVueSupport = true;
            DataExportFormats = "CSV/USB";
            MathFunctions = true;  // 平滑、缩放、滤波
            LimitTesting = true;
            PassFailTest = true;
            DataLogging = "1M 阅读/通道 (趋势/直方图)";
            MemoryDepth = 1000000;  // 1M 阅读/通道

            // 物理参数 - 修正
            Dimensions = "212.6 mm × 88.3 mm × 348.3 mm (半机架)";  // 修正
            Weight = 3.8;  // 修正: 4.0 → 3.8
            FormFactor = "半机架";  // 修正

            // 环境参数 - 修正
            PowerRequirement = "AC 100-240 V, 50/60 Hz, <30 W";  // 补充功耗
            StorageTemp = "-30°C ~ 70°C";  // 修正: -40°C → -30°C

            // 保修与支持
            WarrantyPeriod = "3年 (材料及制造，出厂起)";
            CalibrationInterval = "推荐 1 年";

            // 选配件
            BatteryOption = true;  // 电池便携电源（可选）

            BuildSpecifications();
        }

        /// <summary>
        /// 配置为 Keysight 53230A (高性能型)
        /// </summary>
        public void ConfigureAs53230A()
        {
            Model = "Keysight 53230A";
            ChannelCount = 2;
            MaxFrequency = "350 MHz";
            TimeIntervalResolution = "20 ps";
            FrequencyResolution = 12;
            SingleShotResolution = "20 ps";

            FrequencyRange = "DC ~ 350 MHz";
            FrequencyAccuracy = "±0.05 ppm";
            FrequencySensitivity = "15 mVrms (典型)";

            TimeIntervalRange = "2 ns ~ 1000 s";
            TimeIntervalAccuracy = "±20 ps + 时基误差";
            TimeIntervalJitter = "< 20 ps RMS";
            SingleShotTimeInterval = true;

            InputVoltageRange = "±5 V";
            InputSensitivity = "15 mVrms (DC ~ 100 MHz)";
            InputAttenuator = true;

            TimeBaseType = "OCXO";
            TimeBaseAccuracy = "±0.05 ppm (0°C ~ 55°C)";
            TimeBaseStability = "±0.05 ppm/年";
            InternalOvenOscillator = true;
            ExternalRefInput = true;
            ExtRefFrequency = "1, 5, 10 MHz";

            MinGateTime = "1 ms";
            MaxGateTime = "1000 s";
            GateTimeValue = 1.0;
            BufferSize = 10000000;

            AllanDeviation = true;
            AnalysisTypes = "统计, 趋势, 直方图, Allan偏差, 抖动";
            DisplayResolution = "640 × 480";

            DigitalIO = true;
            MathFunctions = true;
            LimitTesting = true;
            PassFailTest = true;
            DataLogging = "USB存储, 网络";
            MemoryDepth = 10000000;

            BuildSpecifications();
        }

        /// <summary>
        /// 配置为 Keysight 53231A (带RF通道)
        /// </summary>
        public void ConfigureAs53231A()
        {
            Model = "Keysight 53231A";
            ChannelCount = 3; // 2个标准通道 + 1个RF通道
            MaxFrequency = "350 MHz";
            TimeIntervalResolution = "20 ps";
            FrequencyResolution = 12;
            SingleShotResolution = "20 ps";

            FrequencyRange = "DC ~ 350 MHz (标准), DC ~ 20 GHz (RF)";
            RfFrequencyRange = "DC ~ 20 GHz";
            FrequencyAccuracy = "±0.05 ppm";
            FrequencySensitivity = "15 mVrms (标准), -20 dBm (RF)";

            TimeIntervalRange = "2 ns ~ 1000 s";
            TimeIntervalAccuracy = "±20 ps + 时基误差";
            TimeIntervalJitter = "< 20 ps RMS";
            SingleShotTimeInterval = true;

            InputVoltageRange = "±5 V (标准), -30 ~ +20 dBm (RF)";
            InputSensitivity = "15 mVrms (标准), -20 dBm (RF)";
            InputAttenuator = true;

            TimeBaseType = "OCXO";
            TimeBaseAccuracy = "±0.05 ppm (0°C ~ 55°C)";
            TimeBaseStability = "±0.05 ppm/年";
            InternalOvenOscillator = true;
            ExternalRefInput = true;
            ExtRefFrequency = "1, 5, 10 MHz";

            MinGateTime = "1 ms";
            MaxGateTime = "1000 s";
            GateTimeValue = 1.0;
            BufferSize = 10000000;

            AllanDeviation = true;
            AnalysisTypes = "统计, 趋势, 直方图, Allan偏差, 抖动";
            DisplayResolution = "640 × 480";

            DigitalIO = true;
            MathFunctions = true;
            LimitTesting = true;
            PassFailTest = true;
            DataLogging = "USB存储, 网络";
            MemoryDepth = 10000000;

            BuildSpecifications();
        }

        #endregion

        #region 测量配置方法

        /// <summary>
        /// 配置频率测量
        /// </summary>
        public void ConfigureFrequencyMeasurement(double gateTime = 1.0)
        {
            MeasurementMode = FrequencyCounterMeasurementMode.Frequency;
            GateTimeValue = gateTime;
            GateTime = $"{gateTime} s";
            ContinuousMeasurement = true;
        }

        /// <summary>
        /// 配置时间间隔测量
        /// </summary>
        public void ConfigureTimeIntervalMeasurement(bool singleShot = false)
        {
            MeasurementMode = FrequencyCounterMeasurementMode.TimeInterval;
            SingleShotCapable = singleShot;
            SingleShotTimeInterval = singleShot;
        }

        /// <summary>
        /// 配置触发
        /// </summary>
        public void ConfigureTrigger(FrequencyCounterTriggerMode mode, double level = 0.0, string slope = "Positive")
        {
            TriggerMode = mode;
            TriggerLevel = level;
            TriggerSlope = slope;

            if (mode == FrequencyCounterTriggerMode.Auto)
            {
                TriggerLevelType = TriggerLevelType.Auto;
            }
            else
            {
                TriggerLevelType = TriggerLevelType.Manual;
            }
        }

        /// <summary>
        /// 配置输入
        /// </summary>
        public void ConfigureInput(FrequencyCounterCoupling coupling, FrequencyCounterImpedance impedance)
        {
            InputCoupling = coupling;
            InputImpedance = impedance;
        }

        /// <summary>
        /// 启用统计分析
        /// </summary>
        public void EnableStatistics(bool enable = true)
        {
            StatisticsSupport = enable;
            HistogramAnalysis = enable;
            TrendPlotting = enable;
        }

        /// <summary>
        /// 启用Allan偏差分析 (仅高端型号)
        /// </summary>
        public void EnableAllanDeviation(bool enable = true)
        {
            if (Model.Contains("53230A") || Model.Contains("53231A"))
            {
                AllanDeviation = enable;
            }
        }

        #endregion

        #region 重写方法

        public override void InitializeChildren()
        {
            Children.Clear();

            // 简化为单个计数通道节点
            var counterNode = new FrequencyCounterInputNode
            {
                Name = "计数通道",
                ParentNode = "频率计数器",
                Model = "输入A",  // 53220A 主要计数输入
                SlotPosition = "COUNTER",
                Status = "正常"
            };
            Children.Add(counterNode);
        }

        // 保留原有的详细初始化方法（如果需要切换回来）
        private void InitializeDetailedChildren()
        {
            Children.Clear();

            // 创建通道节点
            for (int i = 1; i <= ChannelCount; i++)
            {
                string channelType = "标准";
                string maxFreq = MaxFrequency;

                // 如果是53231A的第3个通道，标记为RF通道
                if (Model.Contains("53231A") && i == 3)
                {
                    channelType = "RF";
                    maxFreq = RfFrequencyRange ?? "20 GHz";
                }

                var channelNode = new FrequencyCounterChannelNode(i, channelType, maxFreq, Model)
                {
                    SlotPosition = SlotPosition ?? "N/A",
                    Status = "正常"
                };
                Children.Add(channelNode);
            }
        }

        public override ObservableCollection<DeviceInfoItem> GetDeviceInfoItems()
        {
            var items = new ObservableCollection<DeviceInfoItem>();

            // 使用FromDevice静态方法创建主设备信息项
            var mainDeviceInfo = DeviceInfoItem.FromDevice(this, false);
            if (mainDeviceInfo != null)
            {
                items.Add(mainDeviceInfo);
            }

            // 添加所有子节点信息
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

        private void BuildSpecifications()
        {
            var specs = new DeviceSpecification();

            // 基本参数
            specs.Add("型号", Model, "基本参数");
            specs.Add("制造商", "Keysight Technologies", "基本参数");
            specs.Add("通道数", $"{ChannelCount} 输入通道 (CH1、CH2)", "基本参数");
            if (Ch3Available)
            {
                specs.Add("可选通道", $"CH3 ({Ch3FrequencyRange})", "基本参数");
            }
            specs.Add("最大频率", MaxFrequency, "基本参数");
            specs.Add("频率分辨率", $"{FrequencyResolution} 位/秒 (1秒门时间); 10位 (100ms)", "基本参数");

            // 测量分辨率与精度
            specs.Add("频率范围", FrequencyRange, "测量分辨率与精度");
            specs.Add("RF频率范围", RfFrequencyRange, "测量分辨率与精度");
            specs.Add("频率精度", FrequencyAccuracy, "测量分辨率与精度");
            specs.Add("时间间隔分辨率", $"{TimeIntervalResolution} (单次)", "测量分辨率与精度");
            if (!string.IsNullOrEmpty(ContinuousResolution))
            {
                specs.Add("连续测量分辨率", ContinuousResolution, "测量分辨率与精度");
            }
            specs.Add("时间间隔范围", TimeIntervalRange, "测量分辨率与精度");
            specs.Add("时间间隔精度", TimeIntervalAccuracy, "测量分辨率与精度");

            if (SingleShotTimeInterval)
            {
                specs.Add("单次时间间隔分辨率", SingleShotResolution, "测量分辨率与精度");
            }

            if (TimeStampSupport)
            {
                specs.Add("时间戳分辨率", TimeStampResolution, "测量分辨率与精度");
            }

            // 输入通道规格
            specs.Add("CH1/CH2频率范围", FrequencyRange, "输入通道规格");
            if (Ch3Available)
            {
                specs.Add("CH3频率范围", Ch3FrequencyRange, "输入通道规格");
            }
            specs.Add("输入阻抗", "1 MΩ (CH1/CH2)", "输入通道规格");
            specs.Add("CH1/CH2输入范围", Ch1Ch2InputRange, "输入通道规格");
            if (Ch3Available && !string.IsNullOrEmpty(Ch3InputRange))
            {
                specs.Add("CH3输入范围", Ch3InputRange, "输入通道规格");
            }
            specs.Add("输入灵敏度", InputSensitivity, "输入通道规格");
            specs.Add("输入耦合", "AC/DC (CH1/CH2); AC (CH3)", "输入通道规格");
            specs.Add("触发斜率", "正/负", "输入通道规格");
            if (!string.IsNullOrEmpty(AutoTriggerLevel))
            {
                specs.Add("自动电平", AutoTriggerLevel, "输入通道规格");
            }
            specs.Add("损伤电平", InputDamageLevel, "输入通道规格");

            // 触发与门控
            if (!string.IsNullOrEmpty(AutoTriggerLevel))
            {
                specs.Add("自动触发电平范围", AutoTriggerLevel, "触发与门控");
            }
            specs.Add("触发源", "内部、外部、总线 (GPIB/LAN/USB)、手动", "触发与门控");
            specs.Add("门控模式", "时间、数字、外部", "触发与门控");
            specs.Add("外部门控输入", "TTL 兼容", "触发与门控");
            if (!string.IsNullOrEmpty(ExternalGateDelay))
            {
                specs.Add("外部门控延迟", ExternalGateDelay, "触发与门控");
            }
            specs.Add("高级触发", "限值、滞后、保持", "触发与门控");
            specs.Add("最小门时间", MinGateTime, "触发与门控");
            specs.Add("最大门时间", MaxGateTime, "触发与门控");

            // 时基参考
            specs.Add("时基类型", TimeBaseType, "时基参考");
            specs.Add("时基精度", TimeBaseAccuracy, "时基参考");
            specs.Add("时基稳定性", TimeBaseStability, "时基参考");
            specs.Add("内置恒温晶振", InternalOvenOscillator ? "有 (OCXO)" : "标配无 (可选OCXO)", "时基参考");
            specs.Add("外部参考输入", ExternalRefInput ? "支持" : "不支持", "时基参考");
            specs.Add("参考频率", ExtRefFrequency, "时基参考");

            // 数据记录与分析
            specs.Add("数据记录", DataLogging, "数据记录与分析");
            specs.Add("缓冲区大小", $"1M 阅读/通道", "数据记录与分析");
            specs.Add("存储深度", $"{MemoryDepth} 点/通道", "数据记录与分析");
            specs.Add("内置分析", BuiltInAnalysis ? "支持" : "不支持", "数据记录与分析");
            specs.Add("统计分析", StatisticsSupport ? "支持 (平均、标准差、Allan偏差)" : "不支持", "数据记录与分析");
            specs.Add("趋势绘图", TrendPlotting ? "支持" : "不支持", "数据记录与分析");
            specs.Add("直方图分析", HistogramAnalysis ? "支持" : "不支持", "数据记录与分析");
            specs.Add("Allan偏差分析", AllanDeviation ? "支持 (长期稳定性)" : "不支持", "数据记录与分析");
            specs.Add("分析类型", AnalysisTypes, "数据记录与分析");

            // 数学函数
            if (MathFunctions)
            {
                specs.Add("数学函数", "平滑、缩放、滤波", "数学函数");
                specs.Add("限值测试", LimitTesting ? "支持" : "不支持", "数学函数");
                specs.Add("合格/不合格测试", PassFailTest ? "支持" : "不支持", "数学函数");
            }

            // 显示屏
            specs.Add("显示屏", DisplayResolution, "显示屏");
            specs.Add("显示类型", ColorDisplay ? "彩色 TFT" : "单色LCD", "显示屏");
            specs.Add("图形化显示", GraphicalDisplay ? "支持 (趋势图/直方图)" : "不支持", "显示屏");
            specs.Add("实时绘图", RealTimePlotting ? "支持" : "不支持", "显示屏");

            // 接口与通信
            specs.Add("GPIB接口", GpibInterface ? "标配 (IEEE-488.2)" : "可选", "接口与通信");
            specs.Add("LAN接口", LanInterface ? "标配" : "无", "接口与通信");
            if (!string.IsNullOrEmpty(LxiCompliance))
            {
                specs.Add("LXI兼容性", LxiCompliance, "接口与通信");
            }
            specs.Add("USB接口", UsbInterface ? "标配" : "无", "接口与通信");
            if (UsbTmcSupport)
            {
                specs.Add("USB协议", "USBTMC", "接口与通信");
            }
            specs.Add("数字I/O", DigitalIO ? "有" : "无", "接口与通信");
            specs.Add("SCPI编程", ScpiProgramming ? "支持" : "不支持", "接口与通信");
            if (WebInterface)
            {
                specs.Add("Web界面", "支持 (趋势图实时显示)", "接口与通信");
            }
            specs.Add("I/O接口", "参考输入/输出 (10 MHz)、外部门控", "接口与通信");

            // 软件支持
            if (BenchVueSupport || !string.IsNullOrEmpty(DataExportFormats))
            {
                specs.Add("远程编程", "SCPI 命令，支持所有测量", "软件支持");
                if (BenchVueSupport)
                {
                    specs.Add("配套软件", "BenchVue、Keysight IO Libraries", "软件支持");
                }
                specs.Add("驱动支持", "VISA、IVI-COM", "软件支持");
                if (!string.IsNullOrEmpty(DataExportFormats))
                {
                    specs.Add("数据导出", DataExportFormats, "软件支持");
                }
            }

            // 物理参数
            specs.Add("尺寸 (W×H×D)", Dimensions, "物理参数");
            specs.Add("重量", $"{Weight} kg", "物理参数");
            specs.Add("外形因子", FormFactor, "物理参数");
            specs.Add("机架安装", RackMountabel ? "支持" : "不支持", "物理参数");

            // 环境参数
            specs.Add("电源输入", PowerRequirement, "环境参数");
            specs.Add("工作温度", OperatingTemp, "环境参数");
            specs.Add("存储温度", StorageTemp, "环境参数");
            specs.Add("工作湿度", Humidity, "环境参数");
            specs.Add("海拔高度", Altitude, "环境参数");

            // 保修与支持
            if (!string.IsNullOrEmpty(WarrantyPeriod))
            {
                specs.Add("保修期", WarrantyPeriod, "保修与支持");
            }
            if (!string.IsNullOrEmpty(CalibrationInterval))
            {
                specs.Add("校准间隔", CalibrationInterval, "保修与支持");
            }
            specs.Add("技术支持", "Keysight Technologies; www.keysight.com", "保修与支持");

            // 选配件
            if (Ch3Available || OcxoOption || BatteryOption)
            {
                if (Ch3Available)
                {
                    specs.Add("可选CH3", "350 MHz RF 通道", "选配件");
                }
                if (OcxoOption)
                {
                    specs.Add("可选OCXO", "高稳定时基准 (±50 ppb)", "选配件");
                }
                if (BatteryOption)
                {
                    specs.Add("可选电池", "便携电源", "选配件");
                }
                if (GpibInterface == false)
                {
                    specs.Add("可选GPIB", "额外接口", "选配件");
                }
            }

        }


        public override string GetConnectionString()
        {
            return $"FreqCounter::{Model}::GPIB[{SlotPosition}]";
        }

        public override bool ValidateConfiguration()
        {
            if (!base.ValidateConfiguration())
                return false;

            if (ChannelCount < 1)
                return false;

            if (string.IsNullOrEmpty(MaxFrequency))
                return false;

            if (FrequencyResolution < 1)
                return false;

            return true;
        }

        #endregion

        #region 辅助方法

        /// <summary>
        /// 获取测量模式描述
        /// </summary>
        public string GetMeasurementModeDescription()
        {
            return GetMeasurementModeDescription(MeasurementMode);
        }

        /// <summary>
        /// 获取测量模式描述（静态）
        /// </summary>
        public static string GetMeasurementModeDescription(FrequencyCounterMeasurementMode mode)
        {
            switch (mode)
            {
                case FrequencyCounterMeasurementMode.Frequency:
                    return "频率测量";
                case FrequencyCounterMeasurementMode.Period:
                    return "周期测量";
                case FrequencyCounterMeasurementMode.TimeInterval:
                    return "时间间隔测量";
                case FrequencyCounterMeasurementMode.PulseWidth:
                    return "脉冲宽度测量";
                case FrequencyCounterMeasurementMode.DutyCycle:
                    return "占空比测量";
                case FrequencyCounterMeasurementMode.RiseTime:
                    return "上升时间测量";
                case FrequencyCounterMeasurementMode.FallTime:
                    return "下降时间测量";
                case FrequencyCounterMeasurementMode.Phase:
                    return "相位测量";
                case FrequencyCounterMeasurementMode.Ratio:
                    return "频率比测量";
                case FrequencyCounterMeasurementMode.TotalizeCount:
                    return "累计计数";
                default:
                    return mode.ToString();
            }
        }

        #endregion
    }

    /// <summary>
    /// 频率计输入节点（简化版）
    /// </summary>
    public class FrequencyCounterInputNode : DeviceBase
    {
        public override string DeviceTypeName => "计数通道";

        public FrequencyCounterInputNode()
        {
            DeviceType = "SubNode";
            ParentNode = "频率计数器";
            SlotPosition = "COUNTER";
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
            return $"FrequencyCounter::Input::A";
        }
    }

    /// <summary>
    /// 频率计通道节点（详细版）
    /// </summary>
    public class FrequencyCounterChannelNode : DeviceBase
    {
        private int _channelNumber;
        private string _channelType;
        private string _maxFrequency;
        private bool _isEnabled;
        private FrequencyCounterCoupling _coupling;
        private FrequencyCounterImpedance _impedance;
        private double _triggerLevel;
        private string _triggerSlope;

        /// <summary>
        /// 通道编号
        /// </summary>
        public int ChannelNumber
        {
            get => _channelNumber;
            set => SetProperty(ref _channelNumber, value);
        }

        /// <summary>
        /// 通道类型 (标准/RF)
        /// </summary>
        public string ChannelType
        {
            get => _channelType;
            set => SetProperty(ref _channelType, value);
        }

        /// <summary>
        /// 最大频率
        /// </summary>
        public string MaxFrequency
        {
            get => _maxFrequency;
            set => SetProperty(ref _maxFrequency, value);
        }

        /// <summary>
        /// 通道是否启用
        /// </summary>
        public bool IsEnabled
        {
            get => _isEnabled;
            set => SetProperty(ref _isEnabled, value);
        }

        /// <summary>
        /// 耦合方式
        /// </summary>
        public FrequencyCounterCoupling Coupling
        {
            get => _coupling;
            set => SetProperty(ref _coupling, value);
        }

        /// <summary>
        /// 输入阻抗
        /// </summary>
        public FrequencyCounterImpedance Impedance
        {
            get => _impedance;
            set => SetProperty(ref _impedance, value);
        }

        /// <summary>
        /// 触发电平 (V)
        /// </summary>
        public double TriggerLevel
        {
            get => _triggerLevel;
            set => SetProperty(ref _triggerLevel, value);
        }

        /// <summary>
        /// 触发斜率
        /// </summary>
        public string TriggerSlope
        {
            get => _triggerSlope;
            set => SetProperty(ref _triggerSlope, value);
        }

        public override string DeviceTypeName => "频率计通道";

        public FrequencyCounterChannelNode() : base()
        {
            DeviceType = "Channel";
            IsEnabled = true;
            Coupling = FrequencyCounterCoupling.DC;
            Impedance = FrequencyCounterImpedance.Ohm1M;
            TriggerLevel = 0.0;
            TriggerSlope = "Positive";
        }

        public FrequencyCounterChannelNode(int channelNumber, string channelType, string maxFrequency, string parentModel) : this()
        {
            ChannelNumber = channelNumber;
            ChannelType = channelType;
            MaxFrequency = maxFrequency;
            Name = channelType == "RF" ? $"CH{channelNumber} (RF)" : $"CH{channelNumber}";
            Model = parentModel;
            ParentNode = "频率计数器";
            Status = "正常";
        }

        public override void InitializeChildren()
        {
            // 通道节点没有子节点
            Children.Clear();
        }

        public override ObservableCollection<DeviceInfoItem> GetDeviceInfoItems()
        {
            var items = new ObservableCollection<DeviceInfoItem>();
            var info = DeviceInfoItem.FromDevice(this, true);
            if (info != null)
            {
                items.Add(info);
            }
            return items;
        }

        public override string GetConnectionString()
        {
            return $"FreqCounter::Channel::{ChannelNumber}";
        }

        public override bool ValidateConfiguration()
        {
            return base.ValidateConfiguration() &&
                   ChannelNumber > 0 &&
                   !string.IsNullOrEmpty(MaxFrequency);
        }
    }

}
