using System;
using System.Collections.ObjectModel;
using System.Linq;
using MeasureControl.Models.Devices.DeviceCategories;

namespace MeasureControl.Models.Devices
{
    #region 枚举定义
    /// <summary>
    /// 波形类型
    /// </summary>
    public enum WaveformType
    {
        Sine,       // 正弦波
        Square,     // 方波
        Ramp,       // 锯齿波
        Pulse,      // 脉冲波
        Noise,      // 噪声
        Arbitrary,  // 任意波
        Harmonic,   // 谐波
        DC          // 直流
    }

    /// <summary>
    /// 调制类型
    /// </summary>
    public enum ModulationType
    {
        None,   // 无调制
        AM,     // 幅度调制
        FM,     // 频率调制
        PM,     // 相位调制
        ASK,    // 幅移键控
        FSK,    // 频移键控
        PSK,    // 相移键控
        PWM     // 脉宽调制
    }

    /// <summary>
    /// 扫频类型
    /// </summary>
    public enum SweepType
    {
        Linear,     // 线性扫频
        Logarithmic, // 对数扫频
        Step        // 步进扫频
    }

    /// <summary>
    /// 突发模式
    /// </summary>
    public enum BurstMode
    {
        NCycle,     // N周期突发
        Infinite,   // 无限突发
        Gated       // 门控突发
    }

    /// <summary>
    /// 触发源
    /// </summary>
    public enum TriggerSource
    {
        Internal,   // 内部触发
        External,   // 外部触发
        Manual      // 手动触发
    }

    /// <summary>
    /// 输出负载
    /// </summary>
    public enum OutputLoad
    {
        HighZ,      // 高阻（>10kΩ）
        Fifty       // 50Ω
    }
    #endregion

    /// <summary>
    /// 信号发生器设备类（如：普源DG1032Z）
    /// </summary>
    public class SignalGeneratorDevice : InstrumentDeviceBase
    {
        #region 私有字段
        private int _channelCount;
        private string _frequencyRange;
        private string _amplitudeRange;
        private string _waveforms;
        private double _sampleRate;

        // 基本波形参数
        private string _maxFrequency;
        private string _frequencyResolution;
        private string _amplitudeAccuracy;
        private string _waveformLength;
        private string _sampleRateStr;
        private string _verticalResolution;

        // 调制功能
        private string _supportedModulations;
        private string _amModulationDepth;
        private string _fmFrequencyDeviation;
        private string _pmPhaseDeviation;

        // 扫频功能
        private string _sweepStartFreq;
        private string _sweepStopFreq;
        private string _sweepTime;
        private bool _sweepEnabled;

        // 突发功能
        private string _burstCycles;
        private string _burstGatePolarity;
        private bool _burstEnabled;

        // 频率计功能
        private bool _counterEnabled;
        private string _counterRange;
        private string _counterSensitivity;
        private string _counterResolution;

        // 通信接口
        private bool _interfaceUSB;
        private bool _interfaceLAN;
        private bool _interfaceUSBTMC;

        // 其他功能
        private bool _channelCopy;
        private bool _channelTracking;
        private bool _channelCoupling;
        private string _harmonicOrder;
        private string _arbWaveformMemory;

        // 频率特性（新增）
        private string _frequencyAccuracy;
        private string _frequencyStability;
        private string _phaseNoise;
        private string _jitterRms;

        // 正弦波规格（新增）
        private string _sineHarmonicDistortion;
        private string _sineTotalHarmonicDistortion;
        private string _sineNonHarmonicSpurious;

        // 方波规格（新增）
        private string _squareRiseFallTime;
        private string _squareOvershoot;
        private string _squareDutyCycleRange;
        private string _squareAsymmetry;

        // 锯齿波规格（新增）
        private string _rampLinearity;
        private string _rampSymmetryRange;

        // 脉冲波规格（新增）
        private string _pulseWidthRange;
        private string _pulseRiseFallEdge;
        private string _pulseOvershoot;

        // 噪声规格（新增）
        private string _noiseBandwidth;

        // 任意波规格（新增）
        private int _arbBuiltInWaveforms;
        private string _arbWaveformLengthRange;
        private string _arbMinRiseFallTime;
        private string _arbEditModes;

        // 谐波规格（新增）
        private string _harmonicTypes;

        // 输出特性（新增）
        private string _amplitudeRangeLowFreq;
        private string _amplitudeRangeHighFreq;
        private string _amplitudeFlatness;
        private string _offsetRange;
        private string _offsetAccuracy;
        private string _outputImpedance;
        private string _outputProtection;

        // 调制特性详细参数（新增）
        private string _modulationFreqRange;
        private string _externalModulationInput;
        private string _externalModulationBandwidth;
        private string _externalModulationImpedance;

        // Sweep特性详细（新增）
        private string _sweepDirection;
        private string _sweepHoldTime;
        private string _sweepReturnTime;
        private string _sweepTriggerSource;
        private string _sweepMarker;

        // Burst特性详细（新增）
        private string _burstStartStopPhase;
        private string _burstInternalPeriod;
        private string _burstTriggerDelay;
        private string _burstTriggerSource;

        // 频率计详细特性（新增）
        private string _counterFunctions;
        private string _counterPeriodRange;
        private string _counterPulseWidthMin;
        private string _counterPulseWidthResolution;
        private string _counterDutyCycleRange;
        private string _counterVoltageDC;
        private string _counterVoltageAC;
        private string _counterInputImpedance;
        private string _counterBreakdownVoltage;
        private string _counterCouplingMode;
        private string _counterHfReject;
        private string _counterTriggerLevel;
        private string _counterTriggerSensitivity;
        private string _counterGateTime;

        // 双通道特性（新增）
        private string _phaseDeviationRange;
        private string _phaseResolution;

        // 参考时钟（新增）
        private string _extRefLockRange;
        private string _extRefLevel;
        private string _extRefLockTime;
        private string _extRefImpedance;
        private string _intRefFrequency;
        private string _intRefLevel;
        private string _intRefImpedance;

        // 同步输出（新增）
        private string _syncOutputLevel;
        private string _syncOutputImpedance;

        // 触发特性（新增）
        private string _triggerInputLevel;
        private string _triggerSlope;
        private string _triggerPulseWidth;
        private string _triggerDelay;
        private string _triggerOutputLevel;
        private string _triggerOutputPulseWidth;
        private string _triggerMaxFrequency;

        // 环境参数（新增）
        private string _operatingTemperature;
        private string _storageTemperature;
        private string _operatingHumidity;
        private string _coolingMethod;
        private string _calibrationInterval;
        private string _powerConsumption;

        // 技术特点（新增）
        private string _sifiTechnology;
        private string _lxiCompliance;
        #endregion

        /// <summary>
        /// 通道数
        /// </summary>
        public int ChannelCount
        {
            get => _channelCount;
            set => SetProperty(ref _channelCount, value);
        }

        /// <summary>
        /// 频率范围（如：1μHz-30MHz）
        /// </summary>
        public string FrequencyRange
        {
            get => _frequencyRange;
            set => SetProperty(ref _frequencyRange, value);
        }

        /// <summary>
        /// 幅度范围（如：1mVpp-10Vpp）
        /// </summary>
        public string AmplitudeRange
        {
            get => _amplitudeRange;
            set => SetProperty(ref _amplitudeRange, value);
        }

        /// <summary>
        /// 支持的波形（如：正弦/方波/三角/锯齿/噪声/任意波）
        /// </summary>
        public string Waveforms
        {
            get => _waveforms;
            set => SetProperty(ref _waveforms, value);
        }

        /// <summary>
        /// 采样率 (MSa/s)
        /// </summary>
        public double SampleRate
        {
            get => _sampleRate;
            set => SetProperty(ref _sampleRate, value);
        }

        #region 基本波形参数属性
        /// <summary>
        /// 最大频率（如：30MHz）
        /// </summary>
        public string MaxFrequency
        {
            get => _maxFrequency;
            set => SetProperty(ref _maxFrequency, value);
        }

        /// <summary>
        /// 频率分辨率
        /// </summary>
        public string FrequencyResolution
        {
            get => _frequencyResolution;
            set => SetProperty(ref _frequencyResolution, value);
        }

        /// <summary>
        /// 幅度精度
        /// </summary>
        public string AmplitudeAccuracy
        {
            get => _amplitudeAccuracy;
            set => SetProperty(ref _amplitudeAccuracy, value);
        }

        /// <summary>
        /// 波形长度
        /// </summary>
        public string WaveformLength
        {
            get => _waveformLength;
            set => SetProperty(ref _waveformLength, value);
        }

        /// <summary>
        /// 采样率（字符串）
        /// </summary>
        public string SampleRateStr
        {
            get => _sampleRateStr;
            set => SetProperty(ref _sampleRateStr, value);
        }

        /// <summary>
        /// 垂直分辨率
        /// </summary>
        public string VerticalResolution
        {
            get => _verticalResolution;
            set => SetProperty(ref _verticalResolution, value);
        }
        #endregion

        #region 调制功能属性
        /// <summary>
        /// 支持的调制类型
        /// </summary>
        public string SupportedModulations
        {
            get => _supportedModulations;
            set => SetProperty(ref _supportedModulations, value);
        }

        /// <summary>
        /// AM调制深度
        /// </summary>
        public string AmModulationDepth
        {
            get => _amModulationDepth;
            set => SetProperty(ref _amModulationDepth, value);
        }

        /// <summary>
        /// FM频偏范围
        /// </summary>
        public string FmFrequencyDeviation
        {
            get => _fmFrequencyDeviation;
            set => SetProperty(ref _fmFrequencyDeviation, value);
        }

        /// <summary>
        /// PM相偏范围
        /// </summary>
        public string PmPhaseDeviation
        {
            get => _pmPhaseDeviation;
            set => SetProperty(ref _pmPhaseDeviation, value);
        }
        #endregion

        #region 扫频功能属性
        /// <summary>
        /// 扫频起始频率
        /// </summary>
        public string SweepStartFreq
        {
            get => _sweepStartFreq;
            set => SetProperty(ref _sweepStartFreq, value);
        }

        /// <summary>
        /// 扫频终止频率
        /// </summary>
        public string SweepStopFreq
        {
            get => _sweepStopFreq;
            set => SetProperty(ref _sweepStopFreq, value);
        }

        /// <summary>
        /// 扫频时间
        /// </summary>
        public string SweepTime
        {
            get => _sweepTime;
            set => SetProperty(ref _sweepTime, value);
        }

        /// <summary>
        /// 扫频使能
        /// </summary>
        public bool SweepEnabled
        {
            get => _sweepEnabled;
            set => SetProperty(ref _sweepEnabled, value);
        }
        #endregion

        #region 突发功能属性
        /// <summary>
        /// 突发周期数
        /// </summary>
        public string BurstCycles
        {
            get => _burstCycles;
            set => SetProperty(ref _burstCycles, value);
        }

        /// <summary>
        /// 门控极性
        /// </summary>
        public string BurstGatePolarity
        {
            get => _burstGatePolarity;
            set => SetProperty(ref _burstGatePolarity, value);
        }

        /// <summary>
        /// 突发使能
        /// </summary>
        public bool BurstEnabled
        {
            get => _burstEnabled;
            set => SetProperty(ref _burstEnabled, value);
        }
        #endregion

        #region 频率计功能属性
        /// <summary>
        /// 频率计启用
        /// </summary>
        public bool CounterEnabled
        {
            get => _counterEnabled;
            set => SetProperty(ref _counterEnabled, value);
        }

        /// <summary>
        /// 频率计范围
        /// </summary>
        public string CounterRange
        {
            get => _counterRange;
            set => SetProperty(ref _counterRange, value);
        }

        /// <summary>
        /// 灵敏度
        /// </summary>
        public string CounterSensitivity
        {
            get => _counterSensitivity;
            set => SetProperty(ref _counterSensitivity, value);
        }

        /// <summary>
        /// 频率计分辨率
        /// </summary>
        public string CounterResolution
        {
            get => _counterResolution;
            set => SetProperty(ref _counterResolution, value);
        }
        #endregion

        #region 通信接口属性
        /// <summary>
        /// USB接口
        /// </summary>
        public bool InterfaceUSB
        {
            get => _interfaceUSB;
            set => SetProperty(ref _interfaceUSB, value);
        }

        /// <summary>
        /// LAN接口
        /// </summary>
        public bool InterfaceLAN
        {
            get => _interfaceLAN;
            set => SetProperty(ref _interfaceLAN, value);
        }

        /// <summary>
        /// USB-TMC支持
        /// </summary>
        public bool InterfaceUSBTMC
        {
            get => _interfaceUSBTMC;
            set => SetProperty(ref _interfaceUSBTMC, value);
        }
        #endregion

        #region 其他功能属性
        /// <summary>
        /// 通道复制
        /// </summary>
        public bool ChannelCopy
        {
            get => _channelCopy;
            set => SetProperty(ref _channelCopy, value);
        }

        /// <summary>
        /// 通道跟踪
        /// </summary>
        public bool ChannelTracking
        {
            get => _channelTracking;
            set => SetProperty(ref _channelTracking, value);
        }

        /// <summary>
        /// 通道耦合
        /// </summary>
        public bool ChannelCoupling
        {
            get => _channelCoupling;
            set => SetProperty(ref _channelCoupling, value);
        }

        /// <summary>
        /// 谐波阶数
        /// </summary>
        public string HarmonicOrder
        {
            get => _harmonicOrder;
            set => SetProperty(ref _harmonicOrder, value);
        }

        /// <summary>
        /// 任意波形存储
        /// </summary>
        public string ArbWaveformMemory
        {
            get => _arbWaveformMemory;
            set => SetProperty(ref _arbWaveformMemory, value);
        }
        #endregion

        #region 频率特性属性（新增）
        /// <summary>
        /// 频率精度
        /// </summary>
        public string FrequencyAccuracy
        {
            get => _frequencyAccuracy;
            set => SetProperty(ref _frequencyAccuracy, value);
        }

        /// <summary>
        /// 频率稳定性
        /// </summary>
        public string FrequencyStability
        {
            get => _frequencyStability;
            set => SetProperty(ref _frequencyStability, value);
        }

        /// <summary>
        /// 相位噪声
        /// </summary>
        public string PhaseNoise
        {
            get => _phaseNoise;
            set => SetProperty(ref _phaseNoise, value);
        }

        /// <summary>
        /// 抖动（RMS）
        /// </summary>
        public string JitterRms
        {
            get => _jitterRms;
            set => SetProperty(ref _jitterRms, value);
        }
        #endregion

        #region 正弦波规格属性（新增）
        /// <summary>
        /// 正弦波谐波失真
        /// </summary>
        public string SineHarmonicDistortion
        {
            get => _sineHarmonicDistortion;
            set => SetProperty(ref _sineHarmonicDistortion, value);
        }

        /// <summary>
        /// 正弦波总谐波失真
        /// </summary>
        public string SineTotalHarmonicDistortion
        {
            get => _sineTotalHarmonicDistortion;
            set => SetProperty(ref _sineTotalHarmonicDistortion, value);
        }

        /// <summary>
        /// 正弦波非谐波杂散
        /// </summary>
        public string SineNonHarmonicSpurious
        {
            get => _sineNonHarmonicSpurious;
            set => SetProperty(ref _sineNonHarmonicSpurious, value);
        }
        #endregion

        #region 方波规格属性（新增）
        /// <summary>
        /// 方波上升/下降时间
        /// </summary>
        public string SquareRiseFallTime
        {
            get => _squareRiseFallTime;
            set => SetProperty(ref _squareRiseFallTime, value);
        }

        /// <summary>
        /// 方波过冲
        /// </summary>
        public string SquareOvershoot
        {
            get => _squareOvershoot;
            set => SetProperty(ref _squareOvershoot, value);
        }

        /// <summary>
        /// 方波占空比范围
        /// </summary>
        public string SquareDutyCycleRange
        {
            get => _squareDutyCycleRange;
            set => SetProperty(ref _squareDutyCycleRange, value);
        }

        /// <summary>
        /// 方波不对称性
        /// </summary>
        public string SquareAsymmetry
        {
            get => _squareAsymmetry;
            set => SetProperty(ref _squareAsymmetry, value);
        }
        #endregion

        #region 锯齿波规格属性（新增）
        /// <summary>
        /// 锯齿波线性度
        /// </summary>
        public string RampLinearity
        {
            get => _rampLinearity;
            set => SetProperty(ref _rampLinearity, value);
        }

        /// <summary>
        /// 锯齿波对称度范围
        /// </summary>
        public string RampSymmetryRange
        {
            get => _rampSymmetryRange;
            set => SetProperty(ref _rampSymmetryRange, value);
        }
        #endregion

        #region 脉冲波规格属性（新增）
        /// <summary>
        /// 脉冲宽度范围
        /// </summary>
        public string PulseWidthRange
        {
            get => _pulseWidthRange;
            set => SetProperty(ref _pulseWidthRange, value);
        }

        /// <summary>
        /// 脉冲上升/下降沿
        /// </summary>
        public string PulseRiseFallEdge
        {
            get => _pulseRiseFallEdge;
            set => SetProperty(ref _pulseRiseFallEdge, value);
        }

        /// <summary>
        /// 脉冲过冲
        /// </summary>
        public string PulseOvershoot
        {
            get => _pulseOvershoot;
            set => SetProperty(ref _pulseOvershoot, value);
        }
        #endregion

        #region 噪声规格属性（新增）
        /// <summary>
        /// 噪声带宽
        /// </summary>
        public string NoiseBandwidth
        {
            get => _noiseBandwidth;
            set => SetProperty(ref _noiseBandwidth, value);
        }
        #endregion

        #region 任意波规格属性（新增）
        /// <summary>
        /// 任意波内置波形数量
        /// </summary>
        public int ArbBuiltInWaveforms
        {
            get => _arbBuiltInWaveforms;
            set => SetProperty(ref _arbBuiltInWaveforms, value);
        }

        /// <summary>
        /// 任意波波形长度范围
        /// </summary>
        public string ArbWaveformLengthRange
        {
            get => _arbWaveformLengthRange;
            set => SetProperty(ref _arbWaveformLengthRange, value);
        }

        /// <summary>
        /// 任意波最小上升/下降时间
        /// </summary>
        public string ArbMinRiseFallTime
        {
            get => _arbMinRiseFallTime;
            set => SetProperty(ref _arbMinRiseFallTime, value);
        }

        /// <summary>
        /// 任意波编辑模式
        /// </summary>
        public string ArbEditModes
        {
            get => _arbEditModes;
            set => SetProperty(ref _arbEditModes, value);
        }
        #endregion

        #region 谐波规格属性（新增）
        /// <summary>
        /// 谐波类型
        /// </summary>
        public string HarmonicTypes
        {
            get => _harmonicTypes;
            set => SetProperty(ref _harmonicTypes, value);
        }
        #endregion

        #region 输出特性属性（新增）
        /// <summary>
        /// 幅值范围（低频）
        /// </summary>
        public string AmplitudeRangeLowFreq
        {
            get => _amplitudeRangeLowFreq;
            set => SetProperty(ref _amplitudeRangeLowFreq, value);
        }

        /// <summary>
        /// 幅值范围（高频）
        /// </summary>
        public string AmplitudeRangeHighFreq
        {
            get => _amplitudeRangeHighFreq;
            set => SetProperty(ref _amplitudeRangeHighFreq, value);
        }

        /// <summary>
        /// 幅值平坦度
        /// </summary>
        public string AmplitudeFlatness
        {
            get => _amplitudeFlatness;
            set => SetProperty(ref _amplitudeFlatness, value);
        }

        /// <summary>
        /// 偏置范围
        /// </summary>
        public string OffsetRange
        {
            get => _offsetRange;
            set => SetProperty(ref _offsetRange, value);
        }

        /// <summary>
        /// 偏置精度
        /// </summary>
        public string OffsetAccuracy
        {
            get => _offsetAccuracy;
            set => SetProperty(ref _offsetAccuracy, value);
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
        /// 输出保护
        /// </summary>
        public string OutputProtection
        {
            get => _outputProtection;
            set => SetProperty(ref _outputProtection, value);
        }
        #endregion

        #region 调制特性详细属性（新增）
        /// <summary>
        /// 调制频率范围
        /// </summary>
        public string ModulationFreqRange
        {
            get => _modulationFreqRange;
            set => SetProperty(ref _modulationFreqRange, value);
        }

        /// <summary>
        /// 外部调制输入
        /// </summary>
        public string ExternalModulationInput
        {
            get => _externalModulationInput;
            set => SetProperty(ref _externalModulationInput, value);
        }

        /// <summary>
        /// 外部调制带宽
        /// </summary>
        public string ExternalModulationBandwidth
        {
            get => _externalModulationBandwidth;
            set => SetProperty(ref _externalModulationBandwidth, value);
        }

        /// <summary>
        /// 外部调制阻抗
        /// </summary>
        public string ExternalModulationImpedance
        {
            get => _externalModulationImpedance;
            set => SetProperty(ref _externalModulationImpedance, value);
        }
        #endregion

        #region Sweep特性详细属性（新增）
        /// <summary>
        /// 扫频方向
        /// </summary>
        public string SweepDirection
        {
            get => _sweepDirection;
            set => SetProperty(ref _sweepDirection, value);
        }

        /// <summary>
        /// 扫频保持时间
        /// </summary>
        public string SweepHoldTime
        {
            get => _sweepHoldTime;
            set => SetProperty(ref _sweepHoldTime, value);
        }

        /// <summary>
        /// 扫频返回时间
        /// </summary>
        public string SweepReturnTime
        {
            get => _sweepReturnTime;
            set => SetProperty(ref _sweepReturnTime, value);
        }

        /// <summary>
        /// 扫频触发源
        /// </summary>
        public string SweepTriggerSource
        {
            get => _sweepTriggerSource;
            set => SetProperty(ref _sweepTriggerSource, value);
        }

        /// <summary>
        /// 扫频标记
        /// </summary>
        public string SweepMarker
        {
            get => _sweepMarker;
            set => SetProperty(ref _sweepMarker, value);
        }
        #endregion

        #region Burst特性详细属性（新增）
        /// <summary>
        /// Burst起始/停止相位
        /// </summary>
        public string BurstStartStopPhase
        {
            get => _burstStartStopPhase;
            set => SetProperty(ref _burstStartStopPhase, value);
        }

        /// <summary>
        /// Burst内部周期
        /// </summary>
        public string BurstInternalPeriod
        {
            get => _burstInternalPeriod;
            set => SetProperty(ref _burstInternalPeriod, value);
        }

        /// <summary>
        /// Burst触发延迟
        /// </summary>
        public string BurstTriggerDelay
        {
            get => _burstTriggerDelay;
            set => SetProperty(ref _burstTriggerDelay, value);
        }

        /// <summary>
        /// Burst触发源
        /// </summary>
        public string BurstTriggerSource
        {
            get => _burstTriggerSource;
            set => SetProperty(ref _burstTriggerSource, value);
        }
        #endregion

        #region 频率计详细特性属性（新增）
        /// <summary>
        /// 频率计功能
        /// </summary>
        public string CounterFunctions
        {
            get => _counterFunctions;
            set => SetProperty(ref _counterFunctions, value);
        }

        /// <summary>
        /// 频率计周期范围
        /// </summary>
        public string CounterPeriodRange
        {
            get => _counterPeriodRange;
            set => SetProperty(ref _counterPeriodRange, value);
        }

        /// <summary>
        /// 频率计脉宽最小值
        /// </summary>
        public string CounterPulseWidthMin
        {
            get => _counterPulseWidthMin;
            set => SetProperty(ref _counterPulseWidthMin, value);
        }

        /// <summary>
        /// 频率计脉宽分辨率
        /// </summary>
        public string CounterPulseWidthResolution
        {
            get => _counterPulseWidthResolution;
            set => SetProperty(ref _counterPulseWidthResolution, value);
        }

        /// <summary>
        /// 频率计占空比范围
        /// </summary>
        public string CounterDutyCycleRange
        {
            get => _counterDutyCycleRange;
            set => SetProperty(ref _counterDutyCycleRange, value);
        }

        /// <summary>
        /// 频率计电压范围（DC耦合）
        /// </summary>
        public string CounterVoltageDC
        {
            get => _counterVoltageDC;
            set => SetProperty(ref _counterVoltageDC, value);
        }

        /// <summary>
        /// 频率计电压范围（AC耦合）
        /// </summary>
        public string CounterVoltageAC
        {
            get => _counterVoltageAC;
            set => SetProperty(ref _counterVoltageAC, value);
        }

        /// <summary>
        /// 频率计输入阻抗
        /// </summary>
        public string CounterInputImpedance
        {
            get => _counterInputImpedance;
            set => SetProperty(ref _counterInputImpedance, value);
        }

        /// <summary>
        /// 频率计击穿电压
        /// </summary>
        public string CounterBreakdownVoltage
        {
            get => _counterBreakdownVoltage;
            set => SetProperty(ref _counterBreakdownVoltage, value);
        }

        /// <summary>
        /// 频率计耦合模式
        /// </summary>
        public string CounterCouplingMode
        {
            get => _counterCouplingMode;
            set => SetProperty(ref _counterCouplingMode, value);
        }

        /// <summary>
        /// 频率计高频抑制
        /// </summary>
        public string CounterHfReject
        {
            get => _counterHfReject;
            set => SetProperty(ref _counterHfReject, value);
        }

        /// <summary>
        /// 频率计触发电平
        /// </summary>
        public string CounterTriggerLevel
        {
            get => _counterTriggerLevel;
            set => SetProperty(ref _counterTriggerLevel, value);
        }

        /// <summary>
        /// 频率计触发灵敏度
        /// </summary>
        public string CounterTriggerSensitivity
        {
            get => _counterTriggerSensitivity;
            set => SetProperty(ref _counterTriggerSensitivity, value);
        }

        /// <summary>
        /// 频率计门时间
        /// </summary>
        public string CounterGateTime
        {
            get => _counterGateTime;
            set => SetProperty(ref _counterGateTime, value);
        }
        #endregion

        #region 双通道特性属性（新增）
        /// <summary>
        /// 相位偏差范围
        /// </summary>
        public string PhaseDeviationRange
        {
            get => _phaseDeviationRange;
            set => SetProperty(ref _phaseDeviationRange, value);
        }

        /// <summary>
        /// 相位分辨率
        /// </summary>
        public string PhaseResolution
        {
            get => _phaseResolution;
            set => SetProperty(ref _phaseResolution, value);
        }
        #endregion

        #region 参考时钟属性（新增）
        /// <summary>
        /// 外部参考锁定范围
        /// </summary>
        public string ExtRefLockRange
        {
            get => _extRefLockRange;
            set => SetProperty(ref _extRefLockRange, value);
        }

        /// <summary>
        /// 外部参考电平
        /// </summary>
        public string ExtRefLevel
        {
            get => _extRefLevel;
            set => SetProperty(ref _extRefLevel, value);
        }

        /// <summary>
        /// 外部参考锁定时间
        /// </summary>
        public string ExtRefLockTime
        {
            get => _extRefLockTime;
            set => SetProperty(ref _extRefLockTime, value);
        }

        /// <summary>
        /// 外部参考阻抗
        /// </summary>
        public string ExtRefImpedance
        {
            get => _extRefImpedance;
            set => SetProperty(ref _extRefImpedance, value);
        }

        /// <summary>
        /// 内部参考频率
        /// </summary>
        public string IntRefFrequency
        {
            get => _intRefFrequency;
            set => SetProperty(ref _intRefFrequency, value);
        }

        /// <summary>
        /// 内部参考电平
        /// </summary>
        public string IntRefLevel
        {
            get => _intRefLevel;
            set => SetProperty(ref _intRefLevel, value);
        }

        /// <summary>
        /// 内部参考阻抗
        /// </summary>
        public string IntRefImpedance
        {
            get => _intRefImpedance;
            set => SetProperty(ref _intRefImpedance, value);
        }
        #endregion

        #region 同步输出属性（新增）
        /// <summary>
        /// 同步输出电平
        /// </summary>
        public string SyncOutputLevel
        {
            get => _syncOutputLevel;
            set => SetProperty(ref _syncOutputLevel, value);
        }

        /// <summary>
        /// 同步输出阻抗
        /// </summary>
        public string SyncOutputImpedance
        {
            get => _syncOutputImpedance;
            set => SetProperty(ref _syncOutputImpedance, value);
        }
        #endregion

        #region 触发特性属性（新增）
        /// <summary>
        /// 触发输入电平
        /// </summary>
        public string TriggerInputLevel
        {
            get => _triggerInputLevel;
            set => SetProperty(ref _triggerInputLevel, value);
        }

        /// <summary>
        /// 触发斜率
        /// </summary>
        public string TriggerSlope
        {
            get => _triggerSlope;
            set => SetProperty(ref _triggerSlope, value);
        }

        /// <summary>
        /// 触发脉宽
        /// </summary>
        public string TriggerPulseWidth
        {
            get => _triggerPulseWidth;
            set => SetProperty(ref _triggerPulseWidth, value);
        }

        /// <summary>
        /// 触发延迟
        /// </summary>
        public string TriggerDelay
        {
            get => _triggerDelay;
            set => SetProperty(ref _triggerDelay, value);
        }

        /// <summary>
        /// 触发输出电平
        /// </summary>
        public string TriggerOutputLevel
        {
            get => _triggerOutputLevel;
            set => SetProperty(ref _triggerOutputLevel, value);
        }

        /// <summary>
        /// 触发输出脉宽
        /// </summary>
        public string TriggerOutputPulseWidth
        {
            get => _triggerOutputPulseWidth;
            set => SetProperty(ref _triggerOutputPulseWidth, value);
        }

        /// <summary>
        /// 触发最大频率
        /// </summary>
        public string TriggerMaxFrequency
        {
            get => _triggerMaxFrequency;
            set => SetProperty(ref _triggerMaxFrequency, value);
        }
        #endregion

        #region 环境参数属性（新增）
        /// <summary>
        /// 工作温度
        /// </summary>
        public string OperatingTemperature
        {
            get => _operatingTemperature;
            set => SetProperty(ref _operatingTemperature, value);
        }

        /// <summary>
        /// 存储温度
        /// </summary>
        public string StorageTemperature
        {
            get => _storageTemperature;
            set => SetProperty(ref _storageTemperature, value);
        }

        /// <summary>
        /// 工作湿度
        /// </summary>
        public string OperatingHumidity
        {
            get => _operatingHumidity;
            set => SetProperty(ref _operatingHumidity, value);
        }

        /// <summary>
        /// 冷却方式
        /// </summary>
        public string CoolingMethod
        {
            get => _coolingMethod;
            set => SetProperty(ref _coolingMethod, value);
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
        /// 功耗
        /// </summary>
        public string PowerConsumption
        {
            get => _powerConsumption;
            set => SetProperty(ref _powerConsumption, value);
        }
        #endregion

        #region 技术特点属性（新增）
        /// <summary>
        /// SiFi技术
        /// </summary>
        public string SifiTechnology
        {
            get => _sifiTechnology;
            set => SetProperty(ref _sifiTechnology, value);
        }

        /// <summary>
        /// LXI兼容性
        /// </summary>
        public string LxiCompliance
        {
            get => _lxiCompliance;
            set => SetProperty(ref _lxiCompliance, value);
        }
        #endregion

        public override string DeviceTypeName => "信号发生器";

        public SignalGeneratorDevice() : base()
        {
            DeviceType = "Instrument";
            ParentNode = "信号发生器";
            ConfigureAsDG1032Z();
            InitializeChildren();
        }

        public SignalGeneratorDevice(string name, string slotPosition) : base()
        {
            DeviceType = "Instrument";
            ParentNode = "信号发生器";
            Model = "DG1032Z";
            slotPosition = "LAN";
            ParseDeviceName(name);
            SlotPosition = slotPosition;

            ConfigureAsDG1032Z();
            InitializeChildren();
        }

        /// <summary>
        /// 配置为 DG1032Z 规格
        /// </summary>
        private void ConfigureAsDG1032Z()
        {
            // 基本参数
            ChannelCount = 2;
            FrequencyRange = "1μHz-30MHz";
            AmplitudeRange = "1mVpp-10Vpp";
            Waveforms = "正弦/方波/锯齿/脉冲/噪声/任意波/谐波";
            SampleRate = 200; // 修正：500 → 200

            if (string.IsNullOrWhiteSpace(IpAddress))
                IpAddress = "192.168.1.12";
            if (LanPort <= 0)
                LanPort = 5555;

            // 基本波形参数
            MaxFrequency = "30MHz";
            FrequencyResolution = "1μHz";
            AmplitudeAccuracy = "±(1% of setting + 1mV)";
            WaveformLength = "8Mpts（标配）/16Mpts（选配）";
            SampleRateStr = "200MSa/s"; // 修正：500MSa/s → 200MSa/s
            VerticalResolution = "14位";

            // 调制功能
            SupportedModulations = "AM/FM/PM/ASK/FSK/PSK/PWM";
            AmModulationDepth = "0%-120%";
            FmFrequencyDeviation = "2mHz-1MHz";
            PmPhaseDeviation = "0°-360°";

            // 扫频功能
            SweepStartFreq = "1μHz";
            SweepStopFreq = "30MHz";
            SweepTime = "1ms-500s";
            SweepEnabled = true;

            // 突发功能
            BurstCycles = "1-1000000/无限";
            BurstGatePolarity = "正/负";
            BurstEnabled = true;

            // 频率计功能
            CounterEnabled = true;
            CounterRange = "1μHz-200MHz"; // 修正：100mHz-200MHz → 1μHz-200MHz
            CounterSensitivity = "50mVRMS-±2.5V (DC耦合)"; // 修正
            CounterResolution = "7位/s"; // 修正：8位/秒 → 7位/s

            // 通信接口
            InterfaceUSB = true;
            InterfaceLAN = true;
            InterfaceUSBTMC = true;

            // 其他功能
            ChannelCopy = true;
            ChannelTracking = true;
            ChannelCoupling = true;
            HarmonicOrder = "最多8阶"; // 修正：1-16阶 → 最多8阶
            ArbWaveformMemory = "标配8Mpts/选配16Mpts";

            // 频率特性
            FrequencyAccuracy = "±1ppm (18°C~28°C)";
            FrequencyStability = "±1ppm";
            PhaseNoise = "-125dBc/Hz @ 10kHz偏移（典型，0dBm）";
            JitterRms = "≤5MHz: 2ppm+200ps; >5MHz: 200ps";

            // 正弦波规格
            SineHarmonicDistortion = "DC~10MHz: <-65dBc; 10~30MHz: <-55dBc（典型，0dBm）";
            SineTotalHarmonicDistortion = "<0.075% (10Hz~20kHz, 0dBm)";
            SineNonHarmonicSpurious = "≤10MHz: <-70dBc; >10MHz: <-70dBc+6dB/倍频程（典型，0dBm）";

            // 方波规格
            SquareRiseFallTime = "<10ns（典型，1Vpp）";
            SquareOvershoot = "≤5%（典型，100kHz，1Vpp）";
            SquareDutyCycleRange = "0.01%-99.99%（频率相关）";
            SquareAsymmetry = "1% 周期 + 5ns";

            // 锯齿波规格
            RampLinearity = "≤1% 峰值输出（典型，1kHz，1Vpp，100%对称）";
            RampSymmetryRange = "0%-100%";

            // 脉冲波规格
            PulseWidthRange = "16ns ~ 999.999982118ks（频率相关）";
            PulseRiseFallEdge = "≥10ns（频率和宽度相关）";
            PulseOvershoot = "≤5%（典型，1Vpp）";

            // 噪声规格
            NoiseBandwidth = "30MHz (-3dB)";

            // 任意波规格
            ArbBuiltInWaveforms = 160;
            ArbWaveformLengthRange = "8pts ~ 8Mpts（标配）/16Mpts（选配）";
            ArbMinRiseFallTime = "<10ns（典型，1Vpp）";
            ArbEditModes = "点编辑/块编辑/插入波形";

            // 谐波规格
            HarmonicTypes = "偶谐波/奇谐波/所有谐波/用户定义";

            // 输出特性
            AmplitudeRangeLowFreq = "1.0mVpp ~ 10Vpp（≤10MHz）";
            AmplitudeRangeHighFreq = "1.0mVpp ~ 5.0Vpp（≤30MHz）";
            AmplitudeFlatness = "≤10MHz: ±0.1dB; ≤30MHz: ±0.2dB（典型，正弦，2.5Vpp）";
            OffsetRange = "±5Vpk (AC+DC)";
            OffsetAccuracy = "±(1% of setting + 5mV + 幅值的0.5%)";
            OutputImpedance = "50Ω（典型）";
            OutputProtection = "短路保护（过载自动关闭输出）";

            // 调制特性详细参数
            ModulationFreqRange = "2mHz ~ 1MHz";
            ExternalModulationInput = "75mVRMS ~ ±5V (AC+DC)";
            ExternalModulationBandwidth = "50kHz";
            ExternalModulationImpedance = "10kΩ";

            // Sweep特性详细
            SweepDirection = "上行/下行";
            SweepHoldTime = "0ms ~ 500s";
            SweepReturnTime = "0ms ~ 500s";
            SweepTriggerSource = "内部/外部/手动";
            SweepMarker = "同步信号下降沿（可编程）";

            // Burst特性详细
            BurstStartStopPhase = "0° ~ 360°（0.1°分辨率）";
            BurstInternalPeriod = "1μs ~ 500s";
            BurstTriggerDelay = "0ns ~ 100s";
            BurstTriggerSource = "内部/外部/手动";

            // 频率计详细特性
            CounterFunctions = "频率/周期/脉宽/占空比";
            CounterPeriodRange = "5ns ~ 16天";
            CounterPulseWidthMin = "≥20ns";
            CounterPulseWidthResolution = "5ns";
            CounterDutyCycleRange = "0% ~ 100%";
            CounterVoltageDC = "1μHz~100MHz: 50mVRMS~±2.5V; 100~200MHz: 100mVRMS~±2.5V";
            CounterVoltageAC = "1~100MHz: 50mVRMS~±2.5Vpp; 100~200MHz: 100mVRMS~±2.5Vpp";
            CounterInputImpedance = "1MΩ";
            CounterBreakdownVoltage = "±7V (AC+DC)";
            CounterCouplingMode = "AC/DC";
            CounterHfReject = "开: 250kHz带宽; 关: 200MHz带宽";
            CounterTriggerLevel = "-2.5V ~ +2.5V";
            CounterTriggerSensitivity = "0% (140mV迟滞) ~ 100% (2mV迟滞)";
            CounterGateTime = "1.310ms, 10.48ms, 166.7ms, 1.342s, 10.73s, >10s";

            // 双通道特性
            PhaseDeviationRange = "0° ~ 360°";
            PhaseResolution = "0.03°";

            // 参考时钟
            ExtRefLockRange = "10MHz ±50Hz";
            ExtRefLevel = "250mVpp ~ 5Vpp";
            ExtRefLockTime = "<2s";
            ExtRefImpedance = "1kΩ（AC耦合）";
            IntRefFrequency = "10MHz ±50Hz";
            IntRefLevel = "3.3Vpp";
            IntRefImpedance = "50Ω（AC耦合）";

            // 同步输出
            SyncOutputLevel = "TTL兼容";
            SyncOutputImpedance = "50Ω（标称）";

            // 触发特性
            TriggerInputLevel = "TTL兼容";
            TriggerSlope = "上升沿/下降沿（可选）";
            TriggerPulseWidth = ">100ns";
            TriggerDelay = "Sweep: <100ns（典型）; Burst: <300ns（典型）";
            TriggerOutputLevel = "TTL兼容";
            TriggerOutputPulseWidth = ">60ns（典型）";
            TriggerMaxFrequency = "1MHz";

            // 环境参数
            OperatingTemperature = "0°C ~ 50°C";
            StorageTemperature = "-20°C ~ 60°C";
            OperatingHumidity = "<80% RH (0~40°C，非冷凝); <50% RH (40~50°C，非冷凝)";
            CoolingMethod = "风扇冷却";
            CalibrationInterval = "推荐1年";
            PowerConsumption = "<40W";

            // 技术特点
            SifiTechnology = "SiFi (Signal Fidelity) 技术，逐点波形生成，低抖动200ps";
            LxiCompliance = "LXI Core 2011兼容";

        }

        public override void InitializeChildren()
        {
            Children.Clear();

            // 简化为单个输出通道节点
            var outputNode = new SignalGeneratorOutputNode
            {
                Name = "输出通道",
                ParentNode = "信号发生器",
                ChannelCount = ChannelCount,
                Model = ChannelCount == 2 ? "CH1, CH2" : $"CH1–CH{ChannelCount}",
                SlotPosition = "AWG",
                Status = "正常"
            };
            Children.Add(outputNode);
        }

        // 保留原有的详细初始化方法（如果需要切换回来）
        private void InitializeDetailedChildren()
        {
            Children.Clear();

            // 创建通道1
            Children.Add(new SignalGeneratorChannelNode
            {
                Name = "CH1",
                ParentNode = "信号发生器",
                ChannelNumber = 1,
                WaveformType = WaveformType.Sine,
                Frequency = "1kHz",
                Amplitude = "5Vpp",
                Offset = "0V",
                Phase = "0°",
                DutyCycle = "50%",
                Symmetry = "50%",
                OutputEnabled = false,
                OutputLoad = OutputLoad.HighZ,
                SlotPosition = "N/A",
                Status = "正常"
            });

            // 创建通道2
            Children.Add(new SignalGeneratorChannelNode
            {
                Name = "CH2",
                ParentNode = "信号发生器",
                ChannelNumber = 2,
                WaveformType = WaveformType.Sine,
                Frequency = "1kHz",
                Amplitude = "5Vpp",
                Offset = "0V",
                Phase = "0°",
                DutyCycle = "50%",
                Symmetry = "50%",
                OutputEnabled = false,
                OutputLoad = OutputLoad.HighZ,
                SlotPosition = "N/A",
                Status = "正常"
            });
        }

        public override ObservableCollection<DeviceInfoItem> GetDeviceInfoItems()
        {
            var items = new ObservableCollection<DeviceInfoItem>();
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
                Name = deviceName;
                Manufacturer = "N/A";
                Model = "N/A";
            }
        }

        public override string GetConnectionString()
        {
            return $"SignalGenerator::{Manufacturer}::{Model}::{SlotPosition}";
        }

        public override bool ValidateConfiguration()
        {
            return base.ValidateConfiguration() &&
                   ChannelCount > 0;
        }
    }

    /// <summary>
    /// 信号发生器输出节点（简化版）
    /// </summary>
    public class SignalGeneratorOutputNode : DeviceBase
    {
        private int _channelCount;

        public int ChannelCount
        {
            get => _channelCount;
            set => SetProperty(ref _channelCount, value);
        }

        public override string DeviceTypeName => "输出通道";

        public SignalGeneratorOutputNode()
        {
            DeviceType = "SubNode";
            ParentNode = "信号发生器";
            ChannelCount = 2;
            SlotPosition = "AWG";
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
            return $"SignalGenerator::Output::{ChannelCount}CH";
        }
    }

    /// <summary>
    /// 信号发生器通道节点（详细版）
    /// </summary>
    public class SignalGeneratorChannelNode : DeviceBase
    {
        private int _channelNumber;
        private WaveformType _waveformType;
        private string _frequency;
        private string _amplitude;
        private string _offset;
        private string _phase;
        private string _dutyCycle;
        private string _symmetry;
        private bool _outputEnabled;
        private OutputLoad _outputLoad;

        /// <summary>
        /// 通道编号（1或2）
        /// </summary>
        public int ChannelNumber
        {
            get => _channelNumber;
            set => SetProperty(ref _channelNumber, value);
        }

        /// <summary>
        /// 波形类型
        /// </summary>
        public WaveformType WaveformType
        {
            get => _waveformType;
            set => SetProperty(ref _waveformType, value);
        }

        /// <summary>
        /// 频率设置
        /// </summary>
        public string Frequency
        {
            get => _frequency;
            set => SetProperty(ref _frequency, value);
        }

        /// <summary>
        /// 幅度设置（1mVpp-10Vpp）
        /// </summary>
        public string Amplitude
        {
            get => _amplitude;
            set => SetProperty(ref _amplitude, value);
        }

        /// <summary>
        /// 直流偏置（±5V）
        /// </summary>
        public string Offset
        {
            get => _offset;
            set => SetProperty(ref _offset, value);
        }

        /// <summary>
        /// 相位（0-360°）
        /// </summary>
        public string Phase
        {
            get => _phase;
            set => SetProperty(ref _phase, value);
        }

        /// <summary>
        /// 占空比（用于方波/脉冲）
        /// </summary>
        public string DutyCycle
        {
            get => _dutyCycle;
            set => SetProperty(ref _dutyCycle, value);
        }

        /// <summary>
        /// 对称性（用于锯齿波）
        /// </summary>
        public string Symmetry
        {
            get => _symmetry;
            set => SetProperty(ref _symmetry, value);
        }

        /// <summary>
        /// 输出使能
        /// </summary>
        public bool OutputEnabled
        {
            get => _outputEnabled;
            set => SetProperty(ref _outputEnabled, value);
        }

        /// <summary>
        /// 输出负载设置
        /// </summary>
        public OutputLoad OutputLoad
        {
            get => _outputLoad;
            set => SetProperty(ref _outputLoad, value);
        }

        public override string DeviceTypeName => $"通道{ChannelNumber}";

        public SignalGeneratorChannelNode()
        {
            DeviceType = "SubNode";
        }

        public override void InitializeChildren()
        {
            Children.Clear();
        }

        public override ObservableCollection<DeviceInfoItem> GetDeviceInfoItems()
        {
            var items = new ObservableCollection<DeviceInfoItem>();
            items.Add(new DeviceInfoItem(
                DeviceTypeName,
                $"{WaveformType} {Frequency} {Amplitude}",
                SlotPosition,
                Status,
                true,
                "Instrument"));
            return items;
        }

        public override string GetConnectionString()
        {
            return $"SignalGenerator::CH{ChannelNumber}::{WaveformType}::{Frequency}";
        }
    }
}

