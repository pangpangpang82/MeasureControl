using System;
using System.Collections.ObjectModel;
using System.Linq;
using MeasureControl.Models;
using MeasureControl.Models.Devices.DeviceCategories;

namespace MeasureControl.Models.Devices
{
    #region 示波器枚举类型

    /// <summary>
    /// 采集模式
    /// </summary>
    public enum AcquisitionMode
    {
        RealTime,           // 实时采样
        Normal,             // 普通模式
        PeakDetect,         // 峰值检测
        Average,            // 平均
        HighResolution,     // 高分辨率
        UltraAcquire       // 超高速采集
    }

    /// <summary>
    /// 触发类型
    /// </summary>
    public enum TriggerType
    {
        Edge,               // 边沿触发
        Pulse,              // 脉冲宽度
        Slope,              // 斜率
        Video,              // 视频
        Pattern,            // 码型
        Duration,           // 持续时间
        Timeout,            // 超时
        Runt,               // 欠幅脉冲
        Window,             // 窗口
        Delay,              // 延迟
        SetupHold,          // 建立保持
        NthEdge,            // 第N边沿
        I2C,                // I2C协议
        SPI,                // SPI协议
        UART,               // UART/RS232
        CAN,                // CAN总线
        CANFD,              // CAN-FD
        LIN,                // LIN总线
        FlexRay,            // FlexRay
        I2S,                // I2S
        MIL1553             // MIL-STD-1553
    }

    /// <summary>
    /// 水平模式
    /// </summary>
    public enum HorizontalMode
    {
        YT,                 // YT模式（默认）
        XY,                 // XY模式
        Scan,               // 扫描模式
        Roll                // 滚动模式
    }

    /// <summary>
    /// 输入阻抗
    /// </summary>
    public enum InputImpedance
    {
        OneM,               // 1 MΩ
        Fifty               // 50 Ω
    }

    /// <summary>
    /// 示波器系列
    /// </summary>
    public enum OscilloscopeSeries
    {
        DHO4000,            // DHO4000 系列
        MSO5000,            // MSO5000 系列
        Other               // 其他
    }

    #endregion
    /// <summary>
    /// 示波器设备类
    /// </summary>
    public class OscilloscopeDevice : InstrumentDeviceBase
    {
        private int _channelCount;
        private double _bandwidth;
        private double _samplingRate;
        private int _memoryDepth;
        private OscilloscopeSeries _series;
        private string _productName;
        
        // 采集系统
        private double _maxSamplingRate;
        private int _standardMemoryDepth;
        private int _optionalMemoryDepth;
        private int _waveformCaptureRate;
        private int _ultraAcquireCaptureRate;
        private int _verticalResolution;
        private int _highResolutionBits;
        private AcquisitionMode _acquisitionMode;
        
        // 通道参数
        private InputImpedance _inputImpedance;
        private double _inputCapacitance;
        private string _maxInputVoltage;
        private double _verticalSensitivityMin;
        private double _verticalSensitivityMax;
        private string _offsetRange;
        private double _dcGainAccuracy;
        private string _dcOffsetAccuracy;
        private string _noiseFloor;
        private string _bandwidthLimit;
        private double _esdTolerance;
        
        // 时基系统
        private string _timebaseRange;
        private double _timebaseResolution;
        private double _timebaseAccuracy;
        private double _channelDelay;
        private HorizontalMode _horizontalMode;
        
        // 触发系统
        private TriggerType _triggerType;
        private string _triggerSource;
        private double _triggerSensitivity;
        private double _triggerJitter;
        private string _triggerHoldoffRange;
        private bool _noiseRejection;
        
        // 显示与界面
        private string _displaySize;
        private string _displayResolution;
        private bool _touchScreen;
        
        // 接口
        private int _usbHostPorts;
        private int _usbDevicePorts;
        private bool _lanSupport;
        private bool _hdmiSupport;
        private bool _auxOutSupport;
        private bool _trigOutSupport;
        private bool _refClockSupport;
        
        // 数字通道（MSO5000）
        private int _digitalChannelCount;
        private double _digitalSamplingRate;
        private int _digitalMemoryDepth;
        private string _digitalMinPulseWidth;
        
        // 集成功能
        private bool _spectrumAnalyzer;
        private bool _arbitraryWaveformGenerator;
        private bool _digitalVoltmeter;
        private bool _frequencyCounter;
        private bool _protocolAnalyzer;
        private bool _powerAnalysis;
        private bool _bodeTest;
        
        // 测量与分析
        private int _autoMeasurementCount;
        private bool _histogramSupport;
        private bool _mathFunctions;
        
        public override string DeviceTypeName => "示波器";

        #region 基础属性

        /// <summary>
        /// 通道数量
        /// </summary>
        public int ChannelCount
        {
            get => _channelCount;
            set => SetProperty(ref _channelCount, value);
        }

        /// <summary>
        /// 带宽 (MHz)
        /// </summary>
        public double Bandwidth
        {
            get => _bandwidth;
            set => SetProperty(ref _bandwidth, value);
        }

        /// <summary>
        /// 采样率 (GS/s)
        /// </summary>
        public double SamplingRate
        {
            get => _samplingRate;
            set => SetProperty(ref _samplingRate, value);
        }

        /// <summary>
        /// 存储深度 (Mpts)
        /// </summary>
        public int MemoryDepth
        {
            get => _memoryDepth;
            set => SetProperty(ref _memoryDepth, value);
        }

        /// <summary>
        /// 示波器系列
        /// </summary>
        public OscilloscopeSeries Series
        {
            get => _series;
            set => SetProperty(ref _series, value);
        }

        /// <summary>
        /// 产品名称
        /// </summary>
        public string ProductName
        {
            get => _productName;
            set => SetProperty(ref _productName, value);
        }

        #endregion

        #region 采集系统属性

        /// <summary>
        /// 最大采样率 (GSa/s)
        /// </summary>
        public double MaxSamplingRate
        {
            get => _maxSamplingRate;
            set => SetProperty(ref _maxSamplingRate, value);
        }

        /// <summary>
        /// 标准存储深度 (Mpts)
        /// </summary>
        public int StandardMemoryDepth
        {
            get => _standardMemoryDepth;
            set => SetProperty(ref _standardMemoryDepth, value);
        }

        /// <summary>
        /// 可选存储深度 (Mpts)
        /// </summary>
        public int OptionalMemoryDepth
        {
            get => _optionalMemoryDepth;
            set => SetProperty(ref _optionalMemoryDepth, value);
        }

        /// <summary>
        /// 波形捕获率 (wfms/s)
        /// </summary>
        public int WaveformCaptureRate
        {
            get => _waveformCaptureRate;
            set => SetProperty(ref _waveformCaptureRate, value);
        }

        /// <summary>
        /// UltraAcquire 模式捕获率 (wfms/s)
        /// </summary>
        public int UltraAcquireCaptureRate
        {
            get => _ultraAcquireCaptureRate;
            set => SetProperty(ref _ultraAcquireCaptureRate, value);
        }

        /// <summary>
        /// 垂直分辨率 (位)
        /// </summary>
        public int VerticalResolution
        {
            get => _verticalResolution;
            set => SetProperty(ref _verticalResolution, value);
        }

        /// <summary>
        /// 高分辨率模式位数
        /// </summary>
        public int HighResolutionBits
        {
            get => _highResolutionBits;
            set => SetProperty(ref _highResolutionBits, value);
        }

        /// <summary>
        /// 采集模式
        /// </summary>
        public AcquisitionMode AcquisitionMode
        {
            get => _acquisitionMode;
            set => SetProperty(ref _acquisitionMode, value);
        }

        #endregion

        #region 通道参数属性

        /// <summary>
        /// 输入阻抗
        /// </summary>
        public InputImpedance InputImpedance
        {
            get => _inputImpedance;
            set => SetProperty(ref _inputImpedance, value);
        }

        /// <summary>
        /// 输入电容 (pF)
        /// </summary>
        public double InputCapacitance
        {
            get => _inputCapacitance;
            set => SetProperty(ref _inputCapacitance, value);
        }

        /// <summary>
        /// 最大输入电压
        /// </summary>
        public string MaxInputVoltage
        {
            get => _maxInputVoltage;
            set => SetProperty(ref _maxInputVoltage, value);
        }

        /// <summary>
        /// 最小垂直灵敏度 (V/div)
        /// </summary>
        public double VerticalSensitivityMin
        {
            get => _verticalSensitivityMin;
            set => SetProperty(ref _verticalSensitivityMin, value);
        }

        /// <summary>
        /// 最大垂直灵敏度 (V/div)
        /// </summary>
        public double VerticalSensitivityMax
        {
            get => _verticalSensitivityMax;
            set => SetProperty(ref _verticalSensitivityMax, value);
        }

        /// <summary>
        /// 偏移范围
        /// </summary>
        public string OffsetRange
        {
            get => _offsetRange;
            set => SetProperty(ref _offsetRange, value);
        }

        /// <summary>
        /// DC增益精度 (%)
        /// </summary>
        public double DcGainAccuracy
        {
            get => _dcGainAccuracy;
            set => SetProperty(ref _dcGainAccuracy, value);
        }

        /// <summary>
        /// DC偏移精度
        /// </summary>
        public string DcOffsetAccuracy
        {
            get => _dcOffsetAccuracy;
            set => SetProperty(ref _dcOffsetAccuracy, value);
        }

        /// <summary>
        /// 噪声底
        /// </summary>
        public string NoiseFloor
        {
            get => _noiseFloor;
            set => SetProperty(ref _noiseFloor, value);
        }

        /// <summary>
        /// 带宽限制
        /// </summary>
        public string BandwidthLimit
        {
            get => _bandwidthLimit;
            set => SetProperty(ref _bandwidthLimit, value);
        }

        /// <summary>
        /// ESD耐受 (kV)
        /// </summary>
        public double EsdTolerance
        {
            get => _esdTolerance;
            set => SetProperty(ref _esdTolerance, value);
        }

        #endregion

        #region 时基系统属性

        /// <summary>
        /// 时基范围
        /// </summary>
        public string TimebaseRange
        {
            get => _timebaseRange;
            set => SetProperty(ref _timebaseRange, value);
        }

        /// <summary>
        /// 时基分辨率 (ps)
        /// </summary>
        public double TimebaseResolution
        {
            get => _timebaseResolution;
            set => SetProperty(ref _timebaseResolution, value);
        }

        /// <summary>
        /// 时基精度 (ppm)
        /// </summary>
        public double TimebaseAccuracy
        {
            get => _timebaseAccuracy;
            set => SetProperty(ref _timebaseAccuracy, value);
        }

        /// <summary>
        /// 通道间延迟 (ps)
        /// </summary>
        public double ChannelDelay
        {
            get => _channelDelay;
            set => SetProperty(ref _channelDelay, value);
        }

        /// <summary>
        /// 水平模式
        /// </summary>
        public HorizontalMode HorizontalMode
        {
            get => _horizontalMode;
            set => SetProperty(ref _horizontalMode, value);
        }

        #endregion

        #region 触发系统属性

        /// <summary>
        /// 触发类型
        /// </summary>
        public TriggerType TriggerType
        {
            get => _triggerType;
            set => SetProperty(ref _triggerType, value);
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
        /// 触发灵敏度 (mV)
        /// </summary>
        public double TriggerSensitivity
        {
            get => _triggerSensitivity;
            set => SetProperty(ref _triggerSensitivity, value);
        }

        /// <summary>
        /// 触发抖动 (ns)
        /// </summary>
        public double TriggerJitter
        {
            get => _triggerJitter;
            set => SetProperty(ref _triggerJitter, value);
        }

        /// <summary>
        /// 触发保持范围
        /// </summary>
        public string TriggerHoldoffRange
        {
            get => _triggerHoldoffRange;
            set => SetProperty(ref _triggerHoldoffRange, value);
        }

        /// <summary>
        /// 噪声抑制
        /// </summary>
        public bool NoiseRejection
        {
            get => _noiseRejection;
            set => SetProperty(ref _noiseRejection, value);
        }

        #endregion

        #region 显示与界面属性

        /// <summary>
        /// 显示屏尺寸
        /// </summary>
        public string DisplaySize
        {
            get => _displaySize;
            set => SetProperty(ref _displaySize, value);
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
        /// 触摸屏支持
        /// </summary>
        public bool TouchScreen
        {
            get => _touchScreen;
            set => SetProperty(ref _touchScreen, value);
        }

        #endregion

        #region 接口属性

        /// <summary>
        /// USB Host端口数
        /// </summary>
        public int UsbHostPorts
        {
            get => _usbHostPorts;
            set => SetProperty(ref _usbHostPorts, value);
        }

        /// <summary>
        /// USB Device端口数
        /// </summary>
        public int UsbDevicePorts
        {
            get => _usbDevicePorts;
            set => SetProperty(ref _usbDevicePorts, value);
        }

        /// <summary>
        /// LAN支持
        /// </summary>
        public bool LanSupport
        {
            get => _lanSupport;
            set => SetProperty(ref _lanSupport, value);
        }

        /// <summary>
        /// HDMI支持
        /// </summary>
        public bool HdmiSupport
        {
            get => _hdmiSupport;
            set => SetProperty(ref _hdmiSupport, value);
        }

        /// <summary>
        /// AUX输出支持
        /// </summary>
        public bool AuxOutSupport
        {
            get => _auxOutSupport;
            set => SetProperty(ref _auxOutSupport, value);
        }

        /// <summary>
        /// TRIG输出支持
        /// </summary>
        public bool TrigOutSupport
        {
            get => _trigOutSupport;
            set => SetProperty(ref _trigOutSupport, value);
        }

        /// <summary>
        /// 参考时钟支持
        /// </summary>
        public bool RefClockSupport
        {
            get => _refClockSupport;
            set => SetProperty(ref _refClockSupport, value);
        }

        #endregion

        #region 数字通道属性（MSO5000）

        /// <summary>
        /// 数字通道数
        /// </summary>
        public int DigitalChannelCount
        {
            get => _digitalChannelCount;
            set => SetProperty(ref _digitalChannelCount, value);
        }

        /// <summary>
        /// 数字通道采样率 (GSa/s)
        /// </summary>
        public double DigitalSamplingRate
        {
            get => _digitalSamplingRate;
            set => SetProperty(ref _digitalSamplingRate, value);
        }

        /// <summary>
        /// 数字通道存储深度 (Mpts)
        /// </summary>
        public int DigitalMemoryDepth
        {
            get => _digitalMemoryDepth;
            set => SetProperty(ref _digitalMemoryDepth, value);
        }

        /// <summary>
        /// 数字通道最小检测脉冲宽度
        /// </summary>
        public string DigitalMinPulseWidth
        {
            get => _digitalMinPulseWidth;
            set => SetProperty(ref _digitalMinPulseWidth, value);
        }

        #endregion

        #region 集成功能属性

        /// <summary>
        /// 频谱分析仪
        /// </summary>
        public bool SpectrumAnalyzer
        {
            get => _spectrumAnalyzer;
            set => SetProperty(ref _spectrumAnalyzer, value);
        }

        /// <summary>
        /// 任意波形发生器
        /// </summary>
        public bool ArbitraryWaveformGenerator
        {
            get => _arbitraryWaveformGenerator;
            set => SetProperty(ref _arbitraryWaveformGenerator, value);
        }

        /// <summary>
        /// 数字电压表
        /// </summary>
        public bool DigitalVoltmeter
        {
            get => _digitalVoltmeter;
            set => SetProperty(ref _digitalVoltmeter, value);
        }

        /// <summary>
        /// 频率计
        /// </summary>
        public bool FrequencyCounter
        {
            get => _frequencyCounter;
            set => SetProperty(ref _frequencyCounter, value);
        }

        /// <summary>
        /// 协议分析仪
        /// </summary>
        public bool ProtocolAnalyzer
        {
            get => _protocolAnalyzer;
            set => SetProperty(ref _protocolAnalyzer, value);
        }

        /// <summary>
        /// 电源分析
        /// </summary>
        public bool PowerAnalysis
        {
            get => _powerAnalysis;
            set => SetProperty(ref _powerAnalysis, value);
        }

        /// <summary>
        /// 伯德图测试
        /// </summary>
        public bool BodeTest
        {
            get => _bodeTest;
            set => SetProperty(ref _bodeTest, value);
        }

        #endregion

        #region 测量与分析属性

        /// <summary>
        /// 自动测量参数数量
        /// </summary>
        public int AutoMeasurementCount
        {
            get => _autoMeasurementCount;
            set => SetProperty(ref _autoMeasurementCount, value);
        }

        /// <summary>
        /// 直方图支持
        /// </summary>
        public bool HistogramSupport
        {
            get => _histogramSupport;
            set => SetProperty(ref _histogramSupport, value);
        }

        /// <summary>
        /// 数学函数支持
        /// </summary>
        public bool MathFunctions
        {
            get => _mathFunctions;
            set => SetProperty(ref _mathFunctions, value);
        }

        #endregion

        public OscilloscopeDevice() : base()
        {
            DeviceType = "Instrument";
            ParentNode = "示波器";  // 设置ParentNode，确保与左侧列表显示一致
            ChannelCount = 4;
            Bandwidth = 100; // 100MHz
            SamplingRate = 1; // 1GS/s
            MemoryDepth = 1; // 1Mpts
            InitializeChildren();
        }

        public OscilloscopeDevice(string name, string slotPosition) : base()
        {
            DeviceType = "Instrument";
            ParentNode = "示波器";  // 设置ParentNode，确保与左侧列表显示一致
            
            ChannelCount = 4;
            Bandwidth = 100;
            SamplingRate = 1;
            MemoryDepth = 1;

            ParseDeviceName(name);
            SlotPosition = slotPosition;

            InitializeChildren();
            
            // 根据型号自动配置
            ConfigureByModel();
        }

        #region 配置方法

        /// <summary>
        /// 根据型号自动配置设备
        /// </summary>
        private void ConfigureByModel()
        {
            if (string.IsNullOrEmpty(Model))
                return;

            if (Model.Contains("DHO4804"))
            {
                ConfigureAsDHO4804();
            }
            else if (Model.Contains("MSO5000"))
            {
                ConfigureAsMSO5000();
            }
        }

        /// <summary>
        /// 配置为 DHO4804 (800 MHz)
        /// </summary>
        private void ConfigureAsDHO4804()
        {
            // 基本信息
            Series = OscilloscopeSeries.DHO4000;
            Model = "DH04804";
            ProductName = "DHO4804";
            Manufacturer = "RIGOL";
            ChannelCount = 4;
            Bandwidth = 800; // 800 MHz

            // 采集系统
            MaxSamplingRate = 4.0; // 4 GSa/s (单通道)
            SamplingRate = 4.0;
            StandardMemoryDepth = 250; // 250 Mpts (单通道)
            MemoryDepth = 250;
            OptionalMemoryDepth = 500; // 可选 500 Mpts
            WaveformCaptureRate = 50000; // 50,000 wfms/s
            UltraAcquireCaptureRate = 1500000; // 1,500,000 wfms/s
            VerticalResolution = 12; // 12-bit
            HighResolutionBits = 16; // 高分辨率模式 16-bit
            AcquisitionMode = AcquisitionMode.RealTime;

            // 通道参数
            InputImpedance = InputImpedance.OneM;
            InputCapacitance = 19; // 19 pF ±3 pF
            MaxInputVoltage = "CAT I 300 Vrms / 400 Vpk (1MΩ); 5 Vrms (50Ω)";
            VerticalSensitivityMin = 0.0001; // 100 μV/div
            VerticalSensitivityMax = 10; // 10 V/div (1MΩ) / 1 V/div (50Ω)
            OffsetRange = "±1 V ~ ±100 V (1MΩ); ±1 V ~ ±4 V (50Ω)";
            DcGainAccuracy = 2.0; // ±2%
            DcOffsetAccuracy = "±0.1 div ±2 mV ±1.5% offset";
            NoiseFloor = "117 μV rms (800 MHz, 1 mV/div, 50Ω)";
            BandwidthLimit = "20 MHz / 250 MHz / FULL";
            EsdTolerance = 8; // ±8 kV

            // 时基系统
            TimebaseRange = "500 ps/div ~ 1 ks/div";
            TimebaseResolution = 100; // 100 ps
            TimebaseAccuracy = 1.5; // ±1.5 ppm
            ChannelDelay = 500; // ≤500 ps
            HorizontalMode = HorizontalMode.YT;

            // 触发系统
            TriggerType = TriggerType.Edge;
            TriggerSource = "CH1~CH4 / EXT TRIG / AC Line";
            TriggerSensitivity = 200; // 200 mVpp (外部)
            TriggerJitter = 1; // <1 ns rms
            TriggerHoldoffRange = "8 ns ~ 10 s";
            NoiseRejection = true;

            // 显示与界面
            DisplaySize = "10.1英寸";
            DisplayResolution = "1280×800";
            TouchScreen = true;

            // 接口
            UsbHostPorts = 2;
            UsbDevicePorts = 1;
            LanSupport = true;
            HdmiSupport = true;
            AuxOutSupport = true;
            TrigOutSupport = true;
            RefClockSupport = true;

            // 数字通道（DHO4000无）
            DigitalChannelCount = 0;

            // 集成功能（DHO4000基础功能）
            SpectrumAnalyzer = false;
            ArbitraryWaveformGenerator = false;
            DigitalVoltmeter = false;
            FrequencyCounter = false;
            ProtocolAnalyzer = true;
            PowerAnalysis = false;
            BodeTest = false;

            // 测量与分析
            AutoMeasurementCount = 30;
            HistogramSupport = true;
            MathFunctions = true;
        }

        /// <summary>
        /// 配置为 DHO4404 (400 MHz)
        /// </summary>
        private void ConfigureAsDHO4404()
        {
            ConfigureAsDHO4804(); // 继承800MHz配置
            
            // 修改带宽相关参数
            ProductName = "DHO4404";
            Bandwidth = 400; // 400 MHz
            NoiseFloor = "81 μV rms (400 MHz, 1 mV/div, 50Ω)";
        }

        /// <summary>
        /// 配置为 DHO4204 (200 MHz)
        /// </summary>
        private void ConfigureAsDHO4204()
        {
            ConfigureAsDHO4804(); // 继承800MHz配置
            
            // 修改带宽相关参数
            ProductName = "DHO4204";
            Bandwidth = 200; // 200 MHz
            NoiseFloor = "56 μV rms (200 MHz, 1 mV/div, 50Ω)";
        }

        /// <summary>
        /// 配置为 MSO5000 系列
        /// </summary>
        private void ConfigureAsMSO5000()
        {
            // 基本信息
            Series = OscilloscopeSeries.MSO5000;
            ProductName = "DS1104";
            Model = "DS1104";
            Manufacturer = "RIGOL";
            ChannelCount = 4;
            Bandwidth = 350; // 默认 350 MHz

            // 采集系统
            MaxSamplingRate = 8.0; // 8 GSa/s (单通道)
            SamplingRate = 8.0;
            StandardMemoryDepth = 100; // 100 Mpts
            MemoryDepth = 100;
            OptionalMemoryDepth = 200; // 可选 200 Mpts
            WaveformCaptureRate = 500000; // >500,000 wfms/s
            UltraAcquireCaptureRate = 0; // 不支持 UltraAcquire
            VerticalResolution = 8; // 8-bit
            HighResolutionBits = 0; // 无高分辨率模式
            AcquisitionMode = AcquisitionMode.Normal;

            // 通道参数
            InputImpedance = InputImpedance.OneM;
            InputCapacitance = 16; // 典型值
            MaxInputVoltage = "CAT I 300 Vrms (1MΩ)";
            VerticalSensitivityMin = 0.001; // 1 mV/div
            VerticalSensitivityMax = 10; // 10 V/div
            OffsetRange = "±5 V ~ ±100 V";
            DcGainAccuracy = 2.0; // ±2%
            DcOffsetAccuracy = "±0.1 div ±2 mV";
            NoiseFloor = "< 500 μV rms";
            BandwidthLimit = "20 MHz / FULL";
            EsdTolerance = 8; // ±8 kV

            // 时基系统
            TimebaseRange = "1 ns/div ~ 1 ks/div";
            TimebaseResolution = 10; // 10 ps
            TimebaseAccuracy = 10; // ±10 ppm
            ChannelDelay = 100; // ±100 ns
            HorizontalMode = HorizontalMode.YT;

            // 触发系统
            TriggerType = TriggerType.Edge;
            TriggerSource = "CH1~CH4 / EXT TRIG / AC Line / D0~D15";
            TriggerSensitivity = 200; // 0.50 div
            TriggerJitter = 1; // 典型
            TriggerHoldoffRange = "8 ns ~ 10 s";
            NoiseRejection = true;

            // 显示与界面
            DisplaySize = "9英寸";
            DisplayResolution = "800×480";
            TouchScreen = true;

            // 接口
            UsbHostPorts = 2;
            UsbDevicePorts = 1;
            LanSupport = true;
            HdmiSupport = true;
            AuxOutSupport = false;
            TrigOutSupport = true;
            RefClockSupport = false;

            // 数字通道（MSO5000标配）
            DigitalChannelCount = 16;
            DigitalSamplingRate = 1.0; // 1 GSa/s
            DigitalMemoryDepth = 25; // 25 Mpts
            DigitalMinPulseWidth = "2 ns";

            // 集成功能（MSO5000丰富功能）
            SpectrumAnalyzer = true;
            ArbitraryWaveformGenerator = true; // 选件
            DigitalVoltmeter = true;
            FrequencyCounter = true;
            ProtocolAnalyzer = true;
            PowerAnalysis = true; // 选件
            BodeTest = true; // 选件

            // 测量与分析
            AutoMeasurementCount = 41;
            HistogramSupport = true;
            MathFunctions = true;
        }

        #endregion

        public override void InitializeChildren()
        {
            Children.Clear();
            
            // 简化为单个采样通道节点
            var samplingNode = new OscilloscopeSamplingNode
            {
                Name = "采样通道",
                ParentNode = "示波器",
                ChannelCount = ChannelCount,
                Model = $"CH1–CH{ChannelCount}",
                SlotPosition = "SCOPE",
                Status = "正常"
            };
            Children.Add(samplingNode);
        }

        // 保留原有的详细初始化方法（如果需要切换回来）
        private void InitializeDetailedChildren()
        {
            Children.Clear();
            
            // 创建通道节点
            for (int i = 1; i <= ChannelCount; i++)
            {
                var channelNode = new OscilloscopeChannelNode(i, Bandwidth, Model)
                {
                    SlotPosition = SlotPosition ?? "N/A"
                };
                Children.Add(channelNode);
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
                // 如果没有空格，整个名称就是型号
                Name = deviceName;
                Manufacturer = "N/A";
                Model = deviceName;  // 直接使用完整名称作为型号
            }
        }

        public override string GetConnectionString()
        {
            return $"Oscilloscope::{Manufacturer}::{Model}::{SlotPosition}";
        }


        public override bool ValidateConfiguration()
        {
            return base.ValidateConfiguration() && 
                   ChannelCount > 0 && 
                   Bandwidth > 0 && 
                   SamplingRate > 0 && 
                   MemoryDepth > 0;
        }

        #region 辅助方法

        /// <summary>
        /// 获取系列描述
        /// </summary>
        private string GetSeriesDescription(OscilloscopeSeries series)
        {
            switch (series)
            {
                case OscilloscopeSeries.DHO4000:
                    return "DHO4000 系列";
                case OscilloscopeSeries.MSO5000:
                    return "MSO5000 系列";
                default:
                    return "其他";
            }
        }

        /// <summary>
        /// 获取输入阻抗描述
        /// </summary>
        private string GetInputImpedanceDescription(InputImpedance impedance)
        {
            switch (impedance)
            {
                case InputImpedance.OneM:
                    return "1 MΩ / 50 Ω 可选";
                case InputImpedance.Fifty:
                    return "50 Ω";
                default:
                    return impedance.ToString();
            }
        }

        /// <summary>
        /// 获取水平模式描述
        /// </summary>
        private string GetHorizontalModeDescription(HorizontalMode mode)
        {
            switch (mode)
            {
                case HorizontalMode.YT:
                    return "YT 模式";
                case HorizontalMode.XY:
                    return "XY 模式";
                case HorizontalMode.Scan:
                    return "扫描模式";
                case HorizontalMode.Roll:
                    return "滚动模式";
                default:
                    return mode.ToString();
            }
        }

        /// <summary>
        /// 获取触发类型描述
        /// </summary>
        private string GetTriggerTypeDescription(TriggerType type)
        {
            switch (type)
            {
                case TriggerType.Edge:
                    return "边沿触发及多种高级触发";
                case TriggerType.Pulse:
                    return "脉冲宽度";
                case TriggerType.Slope:
                    return "斜率";
                default:
                    return type.ToString();
            }
        }

        #endregion
    }

    /// <summary>
    /// 示波器采样节点（简化版）
    /// </summary>
    public class OscilloscopeSamplingNode : DeviceBase
    {
        private int _channelCount;

        public int ChannelCount
        {
            get => _channelCount;
            set => SetProperty(ref _channelCount, value);
        }

        public override string DeviceTypeName => "采样通道";

        public OscilloscopeSamplingNode()
        {
            DeviceType = "SubNode";
            ParentNode = "示波器";
            ChannelCount = 4;
            SlotPosition = "SCOPE";
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
            return $"Oscilloscope::Sampling::{ChannelCount}CH";
        }

        public override bool ValidateConfiguration()
        {
            return base.ValidateConfiguration() && ChannelCount > 0;
        }
    }

    /// <summary>
    /// 示波器通道节点（详细版）
    /// </summary>
    public class OscilloscopeChannelNode : DeviceBase
    {
        private int _channelNumber;
        private double _bandwidth;
        private string _coupling;
        private string _probeAttenuation;
        private bool _isEnabled;

        /// <summary>
        /// 通道编号 (1-4)
        /// </summary>
        public int ChannelNumber
        {
            get => _channelNumber;
            set => SetProperty(ref _channelNumber, value);
        }

        /// <summary>
        /// 带宽 (MHz)
        /// </summary>
        public double Bandwidth
        {
            get => _bandwidth;
            set => SetProperty(ref _bandwidth, value);
        }

        /// <summary>
        /// 耦合方式 (DC / AC / GND)
        /// </summary>
        public string Coupling
        {
            get => _coupling;
            set => SetProperty(ref _coupling, value);
        }

        /// <summary>
        /// 探头衰减 (1X, 10X, 100X, etc.)
        /// </summary>
        public string ProbeAttenuation
        {
            get => _probeAttenuation;
            set => SetProperty(ref _probeAttenuation, value);
        }

        /// <summary>
        /// 通道是否启用
        /// </summary>
        public bool IsEnabled
        {
            get => _isEnabled;
            set => SetProperty(ref _isEnabled, value);
        }

        public override string DeviceTypeName => "示波器通道";

        public OscilloscopeChannelNode() : base()
        {
            DeviceType = "Channel";
            Coupling = "DC";
            ProbeAttenuation = "10X";
            IsEnabled = true;
        }

        public OscilloscopeChannelNode(int channelNumber, double bandwidth, string parentModel) : this()
        {
            ChannelNumber = channelNumber;
            Bandwidth = bandwidth;
            Name = $"CH{channelNumber}";
            Model = parentModel;
            ParentNode = "示波器";
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
            return $"Oscilloscope::Channel::{ChannelNumber}";
        }

        public override bool ValidateConfiguration()
        {
            return base.ValidateConfiguration() &&
                   ChannelNumber > 0 &&
                   ChannelNumber <= 4 &&
                   Bandwidth > 0;
        }
    }

}
