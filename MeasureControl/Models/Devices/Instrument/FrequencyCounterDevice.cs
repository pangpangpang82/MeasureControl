using System;
using System.Collections.ObjectModel;
using System.Linq;
using MeasureControl.Models;
using MeasureControl.Models.Devices.DeviceCategories;

namespace MeasureControl.Models.Devices
{
    /// <summary>
    /// Ƶ�ʼƲ���ģʽö��
    /// </summary>
    public enum FrequencyCounterMeasurementMode
    {
        Frequency,          // Ƶ�ʲ���
        Period,             // ���ڲ���
        TimeInterval,       // ʱ��������
        PulseWidth,         // ������Ȳ���
        DutyCycle,          // ռ�ձȲ���
        RiseTime,           // ����ʱ�����
        FallTime,           // �½�ʱ�����
        Phase,              // ��λ����
        Ratio,              // Ƶ�ʱȲ���
        TotalizeCount       // �ۼƼ���
    }

    /// <summary>
    /// ������ʽö��
    /// </summary>
    public enum FrequencyCounterTriggerMode
    {
        Auto,               // �Զ�����
        Manual,             // �ֶ�����
        External,           // �ⲿ����
        Bus,                // ���ߴ���
        Gated               // �ſش���
    }

    /// <summary>
    /// ������ƽ����ö��
    /// </summary>
    public enum TriggerLevelType
    {
        Auto,               // �Զ���ƽ
        Manual,             // �ֶ���ƽ
        TTL,                // TTL��ƽ
        ECL,                // ECL��ƽ
        CMOS,               // CMOS��ƽ
        NIM                 // NIM��ƽ
    }

    /// <summary>
    /// ������Ϸ�ʽö��
    /// </summary>
    public enum FrequencyCounterCoupling
    {
        DC,                 // DC���
        AC,                 // AC���
        LowPass,            // ��ͨ�˲�
        HighPass            // ��ͨ�˲�
    }

    /// <summary>
    /// �����迹ö��
    /// </summary>
    public enum FrequencyCounterImpedance
    {
        Ohm50,              // 50��
        Ohm1M               // 1M��
    }

    /// <summary>
    /// ͳ�Ʒ�������ö��
    /// </summary>
    public enum StatisticsFunction
    {
        Mean,               // ƽ��ֵ
        StdDev,             // ��׼��
        Min,                // ��Сֵ
        Max,                // ���ֵ
        AllanDeviation,     // Allanƫ��
        Jitter,             // ����
        Histogram           // ֱ��ͼ
    }

    /// <summary>
    /// Ƶ�ʼ�����/��ʱ���豸�ࣨ���� Keysight 53200A ϵ�У�
    /// </summary>
    public class FrequencyCounterDevice : InstrumentDeviceBase
    {
        #region ˽���ֶ�

        // ��������
        private int _channelCount;
        private string _maxFrequency;
        private string _timeIntervalResolution;
        private int _frequencyResolution;
        private string _gateTime;

        // ��������
        private FrequencyCounterMeasurementMode _measurementMode;
        private bool _singleShotCapable;
        private bool _continuousMeasurement;
        private int _measurementSpeed;

        // �ֱ��ʲ���
        private string _singleShotResolution;
        private string _continuousResolution;
        private int _digitsPerSecond;

        // Ƶ�ʲ���
        private string _frequencyRange;
        private string _frequencyAccuracy;
        private string _frequencySensitivity;
        private string _rfFrequencyRange;

        // ʱ��������
        private string _timeIntervalRange;
        private string _timeIntervalAccuracy;
        private string _timeIntervalJitter;
        private bool _singleShotTimeInterval;

        // ��������
        private FrequencyCounterTriggerMode _triggerMode;
        private TriggerLevelType _triggerLevelType;
        private double _triggerLevel;
        private string _triggerSlope;
        private double _triggerHysteresis;

        // ��������
        private FrequencyCounterCoupling _inputCoupling;
        private FrequencyCounterImpedance _inputImpedance;
        private string _inputVoltageRange;
        private string _inputSensitivity;
        private bool _inputAttenuator;

        // �ſغͲ���
        private string _minGateTime;
        private string _maxGateTime;
        private double _gateTimeValue;
        private int _samplesPerMeasurement;
        private int _bufferSize;

        // ��������
        private bool _builtInAnalysis;
        private bool _statisticsSupport;
        private bool _trendPlotting;
        private bool _histogramAnalysis;
        private bool _allanDeviation;
        private string _analysisTypes;

        // ��ʾ�ͻ�ͼ
        private bool _colorDisplay;
        private bool _graphicalDisplay;
        private string _displayResolution;
        private bool _realTimePlotting;

        // ʱ���Ͳο�
        private string _timeBaseType;
        private string _timeBaseAccuracy;
        private string _timeBaseStability;
        private bool _externalRefInput;
        private string _extRefFrequency;
        private bool _internalOvenOscillator;

        // �ӿں�ͨ��
        private bool _gpibInterface;
        private bool _lanInterface;
        private bool _usbInterface;
        private bool _digitalIO;
        private bool _scpiProgramming;
        private string _remoteInterfaces;

        // ϵͳ����
        private string _powerRequirement;
        private string _operatingTemp;
        private string _storageTemp;
        private string _humidity;
        private string _altitude;

        // �����ߴ�
        private string _dimensions;
        private double _weight;
        private bool _rackMountabel;
        private string _formFactor;

        // ��չ����
        private bool _mathFunctions;
        private bool _limitTesting;
        private bool _passFailTest;
        private string _dataLogging;
        private int _memoryDepth;

        // ����������չ
        private string _timeStampResolution;
        private bool _timeStampSupport;

        // ͨ������
        private bool _ch3Available;
        private string _ch3FrequencyRange;

        // �������Բ���
        private string _inputDamageLevel;
        private string _ch1Ch2InputRange;
        private string _ch3InputRange;

        // �������Բ���
        private string _autoTriggerLevel;
        private string _externalGateDelay;

        // ͨ�Žӿڲ���
        private string _lxiCompliance;
        private bool _webInterface;
        private bool _usbTmcSupport;

        // ����֧��
        private bool _benchVueSupport;
        private string _dataExportFormats;

        // ������֧��
        private string _warrantyPeriod;
        private string _calibrationInterval;

        // ѡ���
        private bool _ocxoOption;
        private bool _batteryOption;

        #endregion

        #region ��������

        /// <summary>
        /// ͨ������
        /// </summary>
        public int ChannelCount
        {
            get => _channelCount;
            set => SetProperty(ref _channelCount, value);
        }

        /// <summary>
        /// ���Ƶ�� (����: "350 MHz", "15 GHz")
        /// </summary>
        public string MaxFrequency
        {
            get => _maxFrequency;
            set => SetProperty(ref _maxFrequency, value);
        }

        /// <summary>
        /// ʱ�����ֱ��� (����: "20 ps", "100 ps")
        /// </summary>
        public string TimeIntervalResolution
        {
            get => _timeIntervalResolution;
            set => SetProperty(ref _timeIntervalResolution, value);
        }

        /// <summary>
        /// Ƶ�ʷֱ��� (λ/��)
        /// </summary>
        public int FrequencyResolution
        {
            get => _frequencyResolution;
            set => SetProperty(ref _frequencyResolution, value);
        }

        /// <summary>
        /// ��ʱ�� (����: "1 s", "100 ms")
        /// </summary>
        public string GateTime
        {
            get => _gateTime;
            set => SetProperty(ref _gateTime, value);
        }

        /// <summary>
        /// ����ģʽ
        /// </summary>
        public FrequencyCounterMeasurementMode MeasurementMode
        {
            get => _measurementMode;
            set => SetProperty(ref _measurementMode, value);
        }

        /// <summary>
        /// ���β�������
        /// </summary>
        public bool SingleShotCapable
        {
            get => _singleShotCapable;
            set => SetProperty(ref _singleShotCapable, value);
        }

        /// <summary>
        /// ��������
        /// </summary>
        public bool ContinuousMeasurement
        {
            get => _continuousMeasurement;
            set => SetProperty(ref _continuousMeasurement, value);
        }

        /// <summary>
        /// �����ٶ� (����/��)
        /// </summary>
        public int MeasurementSpeed
        {
            get => _measurementSpeed;
            set => SetProperty(ref _measurementSpeed, value);
        }

        /// <summary>
        /// ���ηֱ���
        /// </summary>
        public string SingleShotResolution
        {
            get => _singleShotResolution;
            set => SetProperty(ref _singleShotResolution, value);
        }

        /// <summary>
        /// �����ֱ���
        /// </summary>
        public string ContinuousResolution
        {
            get => _continuousResolution;
            set => SetProperty(ref _continuousResolution, value);
        }

        /// <summary>
        /// λ/��
        /// </summary>
        public int DigitsPerSecond
        {
            get => _digitsPerSecond;
            set => SetProperty(ref _digitsPerSecond, value);
        }

        /// <summary>
        /// Ƶ�ʷ�Χ
        /// </summary>
        public string FrequencyRange
        {
            get => _frequencyRange;
            set => SetProperty(ref _frequencyRange, value);
        }

        /// <summary>
        /// Ƶ�ʾ���
        /// </summary>
        public string FrequencyAccuracy
        {
            get => _frequencyAccuracy;
            set => SetProperty(ref _frequencyAccuracy, value);
        }

        /// <summary>
        /// Ƶ��������
        /// </summary>
        public string FrequencySensitivity
        {
            get => _frequencySensitivity;
            set => SetProperty(ref _frequencySensitivity, value);
        }

        /// <summary>
        /// RFƵ�ʷ�Χ
        /// </summary>
        public string RfFrequencyRange
        {
            get => _rfFrequencyRange;
            set => SetProperty(ref _rfFrequencyRange, value);
        }

        /// <summary>
        /// ʱ������Χ
        /// </summary>
        public string TimeIntervalRange
        {
            get => _timeIntervalRange;
            set => SetProperty(ref _timeIntervalRange, value);
        }

        /// <summary>
        /// ʱ��������
        /// </summary>
        public string TimeIntervalAccuracy
        {
            get => _timeIntervalAccuracy;
            set => SetProperty(ref _timeIntervalAccuracy, value);
        }

        /// <summary>
        /// ʱ��������
        /// </summary>
        public string TimeIntervalJitter
        {
            get => _timeIntervalJitter;
            set => SetProperty(ref _timeIntervalJitter, value);
        }

        /// <summary>
        /// ����ʱ��������
        /// </summary>
        public bool SingleShotTimeInterval
        {
            get => _singleShotTimeInterval;
            set => SetProperty(ref _singleShotTimeInterval, value);
        }

        /// <summary>
        /// ����ģʽ
        /// </summary>
        public FrequencyCounterTriggerMode TriggerMode
        {
            get => _triggerMode;
            set => SetProperty(ref _triggerMode, value);
        }

        /// <summary>
        /// ������ƽ����
        /// </summary>
        public TriggerLevelType TriggerLevelType
        {
            get => _triggerLevelType;
            set => SetProperty(ref _triggerLevelType, value);
        }

        /// <summary>
        /// ������ƽ (V)
        /// </summary>
        public double TriggerLevel
        {
            get => _triggerLevel;
            set => SetProperty(ref _triggerLevel, value);
        }

        /// <summary>
        /// ����б�� (Positive/Negative)
        /// </summary>
        public string TriggerSlope
        {
            get => _triggerSlope;
            set => SetProperty(ref _triggerSlope, value);
        }

        /// <summary>
        /// �������� (V)
        /// </summary>
        public double TriggerHysteresis
        {
            get => _triggerHysteresis;
            set => SetProperty(ref _triggerHysteresis, value);
        }

        /// <summary>
        /// �������
        /// </summary>
        public FrequencyCounterCoupling InputCoupling
        {
            get => _inputCoupling;
            set => SetProperty(ref _inputCoupling, value);
        }

        /// <summary>
        /// �����迹
        /// </summary>
        public FrequencyCounterImpedance InputImpedance
        {
            get => _inputImpedance;
            set => SetProperty(ref _inputImpedance, value);
        }

        /// <summary>
        /// �����ѹ��Χ
        /// </summary>
        public string InputVoltageRange
        {
            get => _inputVoltageRange;
            set => SetProperty(ref _inputVoltageRange, value);
        }

        /// <summary>
        /// ����������
        /// </summary>
        public string InputSensitivity
        {
            get => _inputSensitivity;
            set => SetProperty(ref _inputSensitivity, value);
        }

        /// <summary>
        /// ����˥����
        /// </summary>
        public bool InputAttenuator
        {
            get => _inputAttenuator;
            set => SetProperty(ref _inputAttenuator, value);
        }

        /// <summary>
        /// ��С��ʱ��
        /// </summary>
        public string MinGateTime
        {
            get => _minGateTime;
            set => SetProperty(ref _minGateTime, value);
        }

        /// <summary>
        /// �����ʱ��
        /// </summary>
        public string MaxGateTime
        {
            get => _maxGateTime;
            set => SetProperty(ref _maxGateTime, value);
        }

        /// <summary>
        /// ��ʱ��ֵ (��)
        /// </summary>
        public double GateTimeValue
        {
            get => _gateTimeValue;
            set => SetProperty(ref _gateTimeValue, value);
        }

        /// <summary>
        /// ÿ�β���������
        /// </summary>
        public int SamplesPerMeasurement
        {
            get => _samplesPerMeasurement;
            set => SetProperty(ref _samplesPerMeasurement, value);
        }

        /// <summary>
        /// ��������С
        /// </summary>
        public int BufferSize
        {
            get => _bufferSize;
            set => SetProperty(ref _bufferSize, value);
        }

        /// <summary>
        /// ���÷�������
        /// </summary>
        public bool BuiltInAnalysis
        {
            get => _builtInAnalysis;
            set => SetProperty(ref _builtInAnalysis, value);
        }

        /// <summary>
        /// ͳ��֧��
        /// </summary>
        public bool StatisticsSupport
        {
            get => _statisticsSupport;
            set => SetProperty(ref _statisticsSupport, value);
        }

        /// <summary>
        /// ���ƻ�ͼ
        /// </summary>
        public bool TrendPlotting
        {
            get => _trendPlotting;
            set => SetProperty(ref _trendPlotting, value);
        }

        /// <summary>
        /// ֱ��ͼ����
        /// </summary>
        public bool HistogramAnalysis
        {
            get => _histogramAnalysis;
            set => SetProperty(ref _histogramAnalysis, value);
        }

        /// <summary>
        /// Allanƫ�����
        /// </summary>
        public bool AllanDeviation
        {
            get => _allanDeviation;
            set => SetProperty(ref _allanDeviation, value);
        }

        /// <summary>
        /// �������� (���ŷָ�)
        /// </summary>
        public string AnalysisTypes
        {
            get => _analysisTypes;
            set => SetProperty(ref _analysisTypes, value);
        }

        /// <summary>
        /// ��ɫ��ʾ��
        /// </summary>
        public bool ColorDisplay
        {
            get => _colorDisplay;
            set => SetProperty(ref _colorDisplay, value);
        }

        /// <summary>
        /// ͼ�λ���ʾ
        /// </summary>
        public bool GraphicalDisplay
        {
            get => _graphicalDisplay;
            set => SetProperty(ref _graphicalDisplay, value);
        }

        /// <summary>
        /// ��ʾ�ֱ���
        /// </summary>
        public string DisplayResolution
        {
            get => _displayResolution;
            set => SetProperty(ref _displayResolution, value);
        }

        /// <summary>
        /// ʵʱ��ͼ
        /// </summary>
        public bool RealTimePlotting
        {
            get => _realTimePlotting;
            set => SetProperty(ref _realTimePlotting, value);
        }

        /// <summary>
        /// ʱ������ (OCXO, TCXO, Rubidium, etc.)
        /// </summary>
        public string TimeBaseType
        {
            get => _timeBaseType;
            set => SetProperty(ref _timeBaseType, value);
        }

        /// <summary>
        /// ʱ������
        /// </summary>
        public string TimeBaseAccuracy
        {
            get => _timeBaseAccuracy;
            set => SetProperty(ref _timeBaseAccuracy, value);
        }

        /// <summary>
        /// ʱ���ȶ���
        /// </summary>
        public string TimeBaseStability
        {
            get => _timeBaseStability;
            set => SetProperty(ref _timeBaseStability, value);
        }

        /// <summary>
        /// �ⲿ�ο�����
        /// </summary>
        public bool ExternalRefInput
        {
            get => _externalRefInput;
            set => SetProperty(ref _externalRefInput, value);
        }

        /// <summary>
        /// �ⲿ�ο�Ƶ��
        /// </summary>
        public string ExtRefFrequency
        {
            get => _extRefFrequency;
            set => SetProperty(ref _extRefFrequency, value);
        }

        /// <summary>
        /// ���ú��¾���
        /// </summary>
        public bool InternalOvenOscillator
        {
            get => _internalOvenOscillator;
            set => SetProperty(ref _internalOvenOscillator, value);
        }

        /// <summary>
        /// GPIB�ӿ�
        /// </summary>
        public bool GpibInterface
        {
            get => _gpibInterface;
            set => SetProperty(ref _gpibInterface, value);
        }

        /// <summary>
        /// LAN�ӿ�
        /// </summary>
        public bool LanInterface
        {
            get => _lanInterface;
            set => SetProperty(ref _lanInterface, value);
        }

        /// <summary>
        /// USB�ӿ�
        /// </summary>
        public bool UsbInterface
        {
            get => _usbInterface;
            set => SetProperty(ref _usbInterface, value);
        }

        /// <summary>
        /// ����I/O
        /// </summary>
        public bool DigitalIO
        {
            get => _digitalIO;
            set => SetProperty(ref _digitalIO, value);
        }

        /// <summary>
        /// SCPI���֧��
        /// </summary>
        public bool ScpiProgramming
        {
            get => _scpiProgramming;
            set => SetProperty(ref _scpiProgramming, value);
        }

        /// <summary>
        /// Զ�̽ӿ� (���ŷָ�)
        /// </summary>
        public string RemoteInterfaces
        {
            get => _remoteInterfaces;
            set => SetProperty(ref _remoteInterfaces, value);
        }

        /// <summary>
        /// ��ԴҪ��
        /// </summary>
        public string PowerRequirement
        {
            get => _powerRequirement;
            set => SetProperty(ref _powerRequirement, value);
        }

        /// <summary>
        /// �����¶�
        /// </summary>
        public string OperatingTemp
        {
            get => _operatingTemp;
            set => SetProperty(ref _operatingTemp, value);
        }

        /// <summary>
        /// �洢�¶�
        /// </summary>
        public string StorageTemp
        {
            get => _storageTemp;
            set => SetProperty(ref _storageTemp, value);
        }

        /// <summary>
        /// ʪ��
        /// </summary>
        public string Humidity
        {
            get => _humidity;
            set => SetProperty(ref _humidity, value);
        }

        /// <summary>
        /// ���θ߶�
        /// </summary>
        public string Altitude
        {
            get => _altitude;
            set => SetProperty(ref _altitude, value);
        }

        /// <summary>
        /// �ߴ� (W��H��D)
        /// </summary>
        public string Dimensions
        {
            get => _dimensions;
            set => SetProperty(ref _dimensions, value);
        }

        /// <summary>
        /// ���� (kg)
        /// </summary>
        public double Weight
        {
            get => _weight;
            set => SetProperty(ref _weight, value);
        }

        /// <summary>
        /// �ɻ��ܰ�װ
        /// </summary>
        public bool RackMountabel
        {
            get => _rackMountabel;
            set => SetProperty(ref _rackMountabel, value);
        }

        /// <summary>
        /// �������� (����: "1U Rack")
        /// </summary>
        public string FormFactor
        {
            get => _formFactor;
            set => SetProperty(ref _formFactor, value);
        }

        /// <summary>
        /// ��ѧ����
        /// </summary>
        public bool MathFunctions
        {
            get => _mathFunctions;
            set => SetProperty(ref _mathFunctions, value);
        }

        /// <summary>
        /// ��ֵ����
        /// </summary>
        public bool LimitTesting
        {
            get => _limitTesting;
            set => SetProperty(ref _limitTesting, value);
        }

        /// <summary>
        /// �ϸ�/���ϸ����
        /// </summary>
        public bool PassFailTest
        {
            get => _passFailTest;
            set => SetProperty(ref _passFailTest, value);
        }

        /// <summary>
        /// ���ݼ�¼
        /// </summary>
        public string DataLogging
        {
            get => _dataLogging;
            set => SetProperty(ref _dataLogging, value);
        }

        /// <summary>
        /// �洢���
        /// </summary>
        public int MemoryDepth
        {
            get => _memoryDepth;
            set => SetProperty(ref _memoryDepth, value);
        }

        /// <summary>
        /// ʱ����ֱ���
        /// </summary>
        public string TimeStampResolution
        {
            get => _timeStampResolution;
            set => SetProperty(ref _timeStampResolution, value);
        }

        /// <summary>
        /// ʱ�������֧��
        /// </summary>
        public bool TimeStampSupport
        {
            get => _timeStampSupport;
            set => SetProperty(ref _timeStampSupport, value);
        }

        /// <summary>
        /// CH3��ѡͨ��
        /// </summary>
        public bool Ch3Available
        {
            get => _ch3Available;
            set => SetProperty(ref _ch3Available, value);
        }

        /// <summary>
        /// CH3Ƶ�ʷ�Χ
        /// </summary>
        public string Ch3FrequencyRange
        {
            get => _ch3FrequencyRange;
            set => SetProperty(ref _ch3FrequencyRange, value);
        }

        /// <summary>
        /// �������˵�ƽ
        /// </summary>
        public string InputDamageLevel
        {
            get => _inputDamageLevel;
            set => SetProperty(ref _inputDamageLevel, value);
        }

        /// <summary>
        /// CH1/CH2���뷶Χ
        /// </summary>
        public string Ch1Ch2InputRange
        {
            get => _ch1Ch2InputRange;
            set => SetProperty(ref _ch1Ch2InputRange, value);
        }

        /// <summary>
        /// CH3���뷶Χ
        /// </summary>
        public string Ch3InputRange
        {
            get => _ch3InputRange;
            set => SetProperty(ref _ch3InputRange, value);
        }

        /// <summary>
        /// �Զ�������ƽ��Χ
        /// </summary>
        public string AutoTriggerLevel
        {
            get => _autoTriggerLevel;
            set => SetProperty(ref _autoTriggerLevel, value);
        }

        /// <summary>
        /// �ⲿ�ſ��ӳ�
        /// </summary>
        public string ExternalGateDelay
        {
            get => _externalGateDelay;
            set => SetProperty(ref _externalGateDelay, value);
        }

        /// <summary>
        /// LXI������
        /// </summary>
        public string LxiCompliance
        {
            get => _lxiCompliance;
            set => SetProperty(ref _lxiCompliance, value);
        }

        /// <summary>
        /// Web����֧��
        /// </summary>
        public bool WebInterface
        {
            get => _webInterface;
            set => SetProperty(ref _webInterface, value);
        }

        /// <summary>
        /// USBTMCЭ��֧��
        /// </summary>
        public bool UsbTmcSupport
        {
            get => _usbTmcSupport;
            set => SetProperty(ref _usbTmcSupport, value);
        }

        /// <summary>
        /// BenchVue����֧��
        /// </summary>
        public bool BenchVueSupport
        {
            get => _benchVueSupport;
            set => SetProperty(ref _benchVueSupport, value);
        }

        /// <summary>
        /// ���ݵ�����ʽ
        /// </summary>
        public string DataExportFormats
        {
            get => _dataExportFormats;
            set => SetProperty(ref _dataExportFormats, value);
        }

        /// <summary>
        /// ������
        /// </summary>
        public string WarrantyPeriod
        {
            get => _warrantyPeriod;
            set => SetProperty(ref _warrantyPeriod, value);
        }

        /// <summary>
        /// У׼���
        /// </summary>
        public string CalibrationInterval
        {
            get => _calibrationInterval;
            set => SetProperty(ref _calibrationInterval, value);
        }

        /// <summary>
        /// OCXO���ȶ�ʱ��ѡ��
        /// </summary>
        public bool OcxoOption
        {
            get => _ocxoOption;
            set => SetProperty(ref _ocxoOption, value);
        }

        /// <summary>
        /// ��ر�Я��Դѡ��
        /// </summary>
        public bool BatteryOption
        {
            get => _batteryOption;
            set => SetProperty(ref _batteryOption, value);
        }

        public override string DeviceTypeName => "Ƶ�ʼ�����";

        #endregion

        #region ���캯��

        public FrequencyCounterDevice() : base()
        {
            DeviceType = "Ƶ�ʼ�����";
            Name = "Ƶ�ʼ�����";
            Model = "Keysight 53200A";
        }

        public FrequencyCounterDevice(string deviceName, string slotPosition)
            : base()
        {
            DeviceType = "Ƶ�ʼ�����";

            ParseDeviceName(deviceName);
            SlotPosition = slotPosition;

            // 默认参数
            ChannelCount = 2;
            MaxFrequency = "350 MHz";
            TimeIntervalResolution = "20 ps";
            FrequencyResolution = 12;
            GateTime = "1 s";

            // ��������
            MeasurementMode = FrequencyCounterMeasurementMode.Frequency;
            SingleShotCapable = true;
            ContinuousMeasurement = true;
            MeasurementSpeed = 1000;

            // ��������
            BuiltInAnalysis = true;
            StatisticsSupport = true;
            TrendPlotting = true;
            HistogramAnalysis = true;
            AllanDeviation = false;

            // ��ʾ
            ColorDisplay = true;
            GraphicalDisplay = true;
            RealTimePlotting = true;

            // �ӿ�
            GpibInterface = true;
            LanInterface = true;
            UsbInterface = true;
            ScpiProgramming = true;
            RemoteInterfaces = "GPIB, LAN, USB";

            // ��������
            Dimensions = "213 mm �� 88 mm �� 348 mm";
            Weight = 4.0;
            RackMountabel = true;
            FormFactor = "1U Rack";

            // ��������
            PowerRequirement = "AC 100-240 V, 50/60 Hz";
            OperatingTemp = "0��C ~ 55��C";
            StorageTemp = "-40��C ~ 70��C";
            Humidity = "5% ~ 95% RH (������)";
            Altitude = "< 3000 m";

            // ����
            TriggerMode = FrequencyCounterTriggerMode.Auto;
            TriggerLevelType = TriggerLevelType.Auto;
            TriggerSlope = "Positive";

            // ����
            InputCoupling = FrequencyCounterCoupling.DC;
            InputImpedance = FrequencyCounterImpedance.Ohm1M;

            Status = "����";
        }

        #endregion

        #region ���÷���

        /// <summary>
        /// ����Ϊ Keysight 53220A (ͨ����)
        /// </summary>
        public void ConfigureAs53220A()
        {
            Model = "Keysight 53220A";
            ChannelCount = 2;
            MaxFrequency = "350 MHz";
            TimeIntervalResolution = "20 ps";  // ����: 500 ps �� 20 ps (����)
            FrequencyResolution = 12;  // 12λ/�� (1����ʱ��); 10λ (100ms)
            SingleShotResolution = "20 ps";  // ����: 500 ps �� 20 ps
            ContinuousResolution = "100 ps";  // ����

            FrequencyRange = "DC ~ 350 MHz";
            FrequencyAccuracy = "��(���� + ʱ���׼���); ʱ���׼ ��1.5 ppm";  // ����
            FrequencySensitivity = "20 mVrms (����, <100 MHz); 40 mVrms (<350 MHz)";  // ����

            TimeIntervalRange = "2 ns ~ 1000 s";
            TimeIntervalAccuracy = "��20 ps + ʱ�����";  // ����
            SingleShotTimeInterval = true;

            // �������� - ����
            InputVoltageRange = "��51 Vpk (1 M��); ��2.4 Vpk (50 ��)";  // ����
            InputSensitivity = "20 mVrms (����, <100 MHz); 40 mVrms (<350 MHz)";  // ����
            InputAttenuator = false;

            // �����ֶ�
            InputDamageLevel = "+27 V (1 M��); 5 Vrms (50 ��)";
            Ch1Ch2InputRange = "��51 Vpk (1 M��); ��2.4 Vpk (50 ��)";

            // ʱ�������
            TimeStampResolution = "100 ps";
            TimeStampSupport = true;

            // CH3��ѡ
            Ch3Available = true;
            Ch3FrequencyRange = "100 MHz ~ 350 MHz";
            Ch3InputRange = "��2.4 Vpk";

            // ʱ������ - ����
            TimeBaseType = "����: TCXO; ��ѡ: OCXO";
            TimeBaseAccuracy = "��1.5 ppm (����); ��50 ppb (��ѡOCXO)";  // ����
            TimeBaseStability = "��0.5 ppm/��";
            InternalOvenOscillator = false;  // �����ޣ���ѡ��
            ExternalRefInput = true;
            ExtRefFrequency = "10 MHz (�ο�����/���)";
            OcxoOption = true;  // ��ѡ��

            // ��������
            AutoTriggerLevel = "10% ~ 90% (Ƶ�� >10 Hz)";
            ExternalGateDelay = "<200 ns";

            // �ſ� - ����
            MinGateTime = "1 ms";  // ����: 10 ms �� 1 ms
            MaxGateTime = "1000 s";
            GateTimeValue = 1.0;
            BufferSize = 1000000;  // 1M �Ķ�/ͨ��

            // ��������
            AllanDeviation = true;  // 53220A ֧�� Allan ƫ��
            AnalysisTypes = "ͳ��(ƽ������׼�Allanƫ��), ����ͼ, ֱ��ͼ";

            // ��ʾ - ����
            DisplayResolution = "4.3Ӣ���ɫ TFT";  // ����: "320 �� 240" �� "4.3Ӣ���ɫ TFT"

            // �ӿ�
            DigitalIO = false;
            LxiCompliance = "LXI Class C";
            WebInterface = true;  // Web ����
            UsbTmcSupport = true;  // USBTMC Э��

            // ����֧��
            BenchVueSupport = true;
            DataExportFormats = "CSV/USB";
            MathFunctions = true;  // ƽ�������š��˲�
            LimitTesting = true;
            PassFailTest = true;
            DataLogging = "1M �Ķ�/ͨ�� (����/ֱ��ͼ)";
            MemoryDepth = 1000000;  // 1M �Ķ�/ͨ��

            // �������� - ����
            Dimensions = "212.6 mm �� 88.3 mm �� 348.3 mm (�����)";  // ����
            Weight = 3.8;  // ����: 4.0 �� 3.8
            FormFactor = "�����";  // ����

            // �������� - ����
            PowerRequirement = "AC 100-240 V, 50/60 Hz, <30 W";  // ���书��
            StorageTemp = "-30��C ~ 70��C";  // ����: -40��C �� -30��C

            // ������֧��
            WarrantyPeriod = "3�� (���ϼ����죬������)";
            CalibrationInterval = "�Ƽ� 1 ��";

            // ѡ���
            BatteryOption = true;  // ��ر�Я��Դ����ѡ��

            BuildSpecifications();
        }

        /// <summary>
        /// ����Ϊ Keysight 53230A (��������)
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
            FrequencyAccuracy = "��0.05 ppm";
            FrequencySensitivity = "15 mVrms (����)";

            TimeIntervalRange = "2 ns ~ 1000 s";
            TimeIntervalAccuracy = "��20 ps + ʱ�����";
            TimeIntervalJitter = "< 20 ps RMS";
            SingleShotTimeInterval = true;

            InputVoltageRange = "��5 V";
            InputSensitivity = "15 mVrms (DC ~ 100 MHz)";
            InputAttenuator = true;

            TimeBaseType = "OCXO";
            TimeBaseAccuracy = "��0.05 ppm (0��C ~ 55��C)";
            TimeBaseStability = "��0.05 ppm/��";
            InternalOvenOscillator = true;
            ExternalRefInput = true;
            ExtRefFrequency = "1, 5, 10 MHz";

            MinGateTime = "1 ms";
            MaxGateTime = "1000 s";
            GateTimeValue = 1.0;
            BufferSize = 10000000;

            AllanDeviation = true;
            AnalysisTypes = "ͳ��, ����, ֱ��ͼ, Allanƫ��, ����";
            DisplayResolution = "640 �� 480";

            DigitalIO = true;
            MathFunctions = true;
            LimitTesting = true;
            PassFailTest = true;
            DataLogging = "USB�洢, ����";
            MemoryDepth = 10000000;

            BuildSpecifications();
        }

        /// <summary>
        /// ����Ϊ Keysight 53231A (��RFͨ��)
        /// </summary>
        public void ConfigureAs53231A()
        {
            Model = "Keysight 53231A";
            ChannelCount = 3; // 2����׼ͨ�� + 1��RFͨ��
            MaxFrequency = "350 MHz";
            TimeIntervalResolution = "20 ps";
            FrequencyResolution = 12;
            SingleShotResolution = "20 ps";

            FrequencyRange = "DC ~ 350 MHz (��׼), DC ~ 20 GHz (RF)";
            RfFrequencyRange = "DC ~ 20 GHz";
            FrequencyAccuracy = "��0.05 ppm";
            FrequencySensitivity = "15 mVrms (��׼), -20 dBm (RF)";

            TimeIntervalRange = "2 ns ~ 1000 s";
            TimeIntervalAccuracy = "��20 ps + ʱ�����";
            TimeIntervalJitter = "< 20 ps RMS";
            SingleShotTimeInterval = true;

            InputVoltageRange = "��5 V (��׼), -30 ~ +20 dBm (RF)";
            InputSensitivity = "15 mVrms (��׼), -20 dBm (RF)";
            InputAttenuator = true;

            TimeBaseType = "OCXO";
            TimeBaseAccuracy = "��0.05 ppm (0��C ~ 55��C)";
            TimeBaseStability = "��0.05 ppm/��";
            InternalOvenOscillator = true;
            ExternalRefInput = true;
            ExtRefFrequency = "1, 5, 10 MHz";

            MinGateTime = "1 ms";
            MaxGateTime = "1000 s";
            GateTimeValue = 1.0;
            BufferSize = 10000000;

            AllanDeviation = true;
            AnalysisTypes = "ͳ��, ����, ֱ��ͼ, Allanƫ��, ����";
            DisplayResolution = "640 �� 480";

            DigitalIO = true;
            MathFunctions = true;
            LimitTesting = true;
            PassFailTest = true;
            DataLogging = "USB�洢, ����";
            MemoryDepth = 10000000;

            BuildSpecifications();
        }

        #endregion

        #region �������÷���

        /// <summary>
        /// ����Ƶ�ʲ���
        /// </summary>
        public void ConfigureFrequencyMeasurement(double gateTime = 1.0)
        {
            MeasurementMode = FrequencyCounterMeasurementMode.Frequency;
            GateTimeValue = gateTime;
            GateTime = $"{gateTime} s";
            ContinuousMeasurement = true;
        }

        /// <summary>
        /// ����ʱ��������
        /// </summary>
        public void ConfigureTimeIntervalMeasurement(bool singleShot = false)
        {
            MeasurementMode = FrequencyCounterMeasurementMode.TimeInterval;
            SingleShotCapable = singleShot;
            SingleShotTimeInterval = singleShot;
        }

        /// <summary>
        /// ���ô���
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
        /// ��������
        /// </summary>
        public void ConfigureInput(FrequencyCounterCoupling coupling, FrequencyCounterImpedance impedance)
        {
            InputCoupling = coupling;
            InputImpedance = impedance;
        }

        /// <summary>
        /// ����ͳ�Ʒ���
        /// </summary>
        public void EnableStatistics(bool enable = true)
        {
            StatisticsSupport = enable;
            HistogramAnalysis = enable;
            TrendPlotting = enable;
        }

        /// <summary>
        /// ����Allanƫ����� (���߶��ͺ�)
        /// </summary>
        public void EnableAllanDeviation(bool enable = true)
        {
            if (Model.Contains("53230A") || Model.Contains("53231A"))
            {
                AllanDeviation = enable;
            }
        }

        #endregion

        #region ��д����

        public override void InitializeChildren()
        {
            Children.Clear();

            // ��Ϊ��������ͨ���ڵ�
            var counterNode = new FrequencyCounterInputNode
            {
                Name = "����ͨ��",
                ParentNode = "Ƶ�ʼ�����",
                Model = "����A",  // 53220A ��Ҫ��������
                SlotPosition = "COUNTER",
                Status = "����"
            };
            Children.Add(counterNode);
        }

        // ����ԭ�е���ϸ��ʼ�������������Ҫ�л�������
        private void InitializeDetailedChildren()
        {
            Children.Clear();

            // ����ͨ���ڵ�
            for (int i = 1; i <= ChannelCount; i++)
            {
                string channelType = "��׼";
                string maxFreq = MaxFrequency;

                // �����53231A�ĵ�3��ͨ�������ΪRFͨ��
                if (Model.Contains("53231A") && i == 3)
                {
                    channelType = "RF";
                    maxFreq = RfFrequencyRange ?? "20 GHz";
                }

                var channelNode = new FrequencyCounterChannelNode(i, channelType, maxFreq, Model)
                {
                    SlotPosition = SlotPosition ?? "N/A",
                    Status = "����"
                };
                Children.Add(channelNode);
            }
        }

        public override ObservableCollection<DeviceInfoItem> GetDeviceInfoItems()
        {
            var items = new ObservableCollection<DeviceInfoItem>();

            // ʹ��FromDevice��̬�����������豸��Ϣ��
            var mainDeviceInfo = DeviceInfoItem.FromDevice(this, false);
            if (mainDeviceInfo != null)
            {
                items.Add(mainDeviceInfo);
            }

            // ���������ӽڵ���Ϣ
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

            // ��������
            specs.Add("�ͺ�", Model, "��������");
            specs.Add("������", "Keysight Technologies", "��������");
            specs.Add("ͨ����", $"{ChannelCount} ����ͨ�� (CH1��CH2)", "��������");
            if (Ch3Available)
            {
                specs.Add("��ѡͨ��", $"CH3 ({Ch3FrequencyRange})", "��������");
            }
            specs.Add("���Ƶ��", MaxFrequency, "��������");
            specs.Add("Ƶ�ʷֱ���", $"{FrequencyResolution} λ/�� (1����ʱ��); 10λ (100ms)", "��������");

            // �����ֱ����뾫��
            specs.Add("Ƶ�ʷ�Χ", FrequencyRange, "�����ֱ����뾫��");
            specs.Add("RFƵ�ʷ�Χ", RfFrequencyRange, "�����ֱ����뾫��");
            specs.Add("Ƶ�ʾ���", FrequencyAccuracy, "�����ֱ����뾫��");
            specs.Add("ʱ�����ֱ���", $"{TimeIntervalResolution} (����)", "�����ֱ����뾫��");
            if (!string.IsNullOrEmpty(ContinuousResolution))
            {
                specs.Add("���������ֱ���", ContinuousResolution, "�����ֱ����뾫��");
            }
            specs.Add("ʱ������Χ", TimeIntervalRange, "�����ֱ����뾫��");
            specs.Add("ʱ��������", TimeIntervalAccuracy, "�����ֱ����뾫��");

            if (SingleShotTimeInterval)
            {
                specs.Add("����ʱ�����ֱ���", SingleShotResolution, "�����ֱ����뾫��");
            }

            if (TimeStampSupport)
            {
                specs.Add("ʱ����ֱ���", TimeStampResolution, "�����ֱ����뾫��");
            }

            // ����ͨ�����
            specs.Add("CH1/CH2Ƶ�ʷ�Χ", FrequencyRange, "����ͨ�����");
            if (Ch3Available)
            {
                specs.Add("CH3Ƶ�ʷ�Χ", Ch3FrequencyRange, "����ͨ�����");
            }
            specs.Add("�����迹", "1 M�� (CH1/CH2)", "����ͨ�����");
            specs.Add("CH1/CH2���뷶Χ", Ch1Ch2InputRange, "����ͨ�����");
            if (Ch3Available && !string.IsNullOrEmpty(Ch3InputRange))
            {
                specs.Add("CH3���뷶Χ", Ch3InputRange, "����ͨ�����");
            }
            specs.Add("����������", InputSensitivity, "����ͨ�����");
            specs.Add("�������", "AC/DC (CH1/CH2); AC (CH3)", "����ͨ�����");
            specs.Add("����б��", "��/��", "����ͨ�����");
            if (!string.IsNullOrEmpty(AutoTriggerLevel))
            {
                specs.Add("�Զ���ƽ", AutoTriggerLevel, "����ͨ�����");
            }
            specs.Add("���˵�ƽ", InputDamageLevel, "����ͨ�����");

            // �������ſ�
            if (!string.IsNullOrEmpty(AutoTriggerLevel))
            {
                specs.Add("�Զ�������ƽ��Χ", AutoTriggerLevel, "�������ſ�");
            }
            specs.Add("����Դ", "�ڲ����ⲿ������ (GPIB/LAN/USB)���ֶ�", "�������ſ�");
            specs.Add("�ſ�ģʽ", "ʱ�䡢���֡��ⲿ", "�������ſ�");
            specs.Add("�ⲿ�ſ�����", "TTL ����", "�������ſ�");
            if (!string.IsNullOrEmpty(ExternalGateDelay))
            {
                specs.Add("�ⲿ�ſ��ӳ�", ExternalGateDelay, "�������ſ�");
            }
            specs.Add("�߼�����", "��ֵ���ͺ󡢱���", "�������ſ�");
            specs.Add("��С��ʱ��", MinGateTime, "�������ſ�");
            specs.Add("�����ʱ��", MaxGateTime, "�������ſ�");

            // ʱ���ο�
            specs.Add("ʱ������", TimeBaseType, "ʱ���ο�");
            specs.Add("ʱ������", TimeBaseAccuracy, "ʱ���ο�");
            specs.Add("ʱ���ȶ���", TimeBaseStability, "ʱ���ο�");
            specs.Add("���ú��¾���", InternalOvenOscillator ? "�� (OCXO)" : "������ (��ѡOCXO)", "ʱ���ο�");
            specs.Add("�ⲿ�ο�����", ExternalRefInput ? "֧��" : "��֧��", "ʱ���ο�");
            specs.Add("�ο�Ƶ��", ExtRefFrequency, "ʱ���ο�");

            // ���ݼ�¼�����
            specs.Add("���ݼ�¼", DataLogging, "���ݼ�¼�����");
            specs.Add("��������С", $"1M �Ķ�/ͨ��", "���ݼ�¼�����");
            specs.Add("�洢���", $"{MemoryDepth} ��/ͨ��", "���ݼ�¼�����");
            specs.Add("���÷���", BuiltInAnalysis ? "֧��" : "��֧��", "���ݼ�¼�����");
            specs.Add("ͳ�Ʒ���", StatisticsSupport ? "֧�� (ƽ������׼�Allanƫ��)" : "��֧��", "���ݼ�¼�����");
            specs.Add("���ƻ�ͼ", TrendPlotting ? "֧��" : "��֧��", "���ݼ�¼�����");
            specs.Add("ֱ��ͼ����", HistogramAnalysis ? "֧��" : "��֧��", "���ݼ�¼�����");
            specs.Add("Allanƫ�����", AllanDeviation ? "֧�� (�����ȶ���)" : "��֧��", "���ݼ�¼�����");
            specs.Add("��������", AnalysisTypes, "���ݼ�¼�����");

            // ��ѧ����
            if (MathFunctions)
            {
                specs.Add("��ѧ����", "ƽ�������š��˲�", "��ѧ����");
                specs.Add("��ֵ����", LimitTesting ? "֧��" : "��֧��", "��ѧ����");
                specs.Add("�ϸ�/���ϸ����", PassFailTest ? "֧��" : "��֧��", "��ѧ����");
            }

            // ��ʾ��
            specs.Add("��ʾ��", DisplayResolution, "��ʾ��");
            specs.Add("��ʾ����", ColorDisplay ? "��ɫ TFT" : "��ɫLCD", "��ʾ��");
            specs.Add("ͼ�λ���ʾ", GraphicalDisplay ? "֧�� (����ͼ/ֱ��ͼ)" : "��֧��", "��ʾ��");
            specs.Add("ʵʱ��ͼ", RealTimePlotting ? "֧��" : "��֧��", "��ʾ��");

            // �ӿ���ͨ��
            specs.Add("GPIB�ӿ�", GpibInterface ? "���� (IEEE-488.2)" : "��ѡ", "�ӿ���ͨ��");
            specs.Add("LAN�ӿ�", LanInterface ? "����" : "��", "�ӿ���ͨ��");
            if (!string.IsNullOrEmpty(LxiCompliance))
            {
                specs.Add("LXI������", LxiCompliance, "�ӿ���ͨ��");
            }
            specs.Add("USB�ӿ�", UsbInterface ? "����" : "��", "�ӿ���ͨ��");
            if (UsbTmcSupport)
            {
                specs.Add("USBЭ��", "USBTMC", "�ӿ���ͨ��");
            }
            specs.Add("����I/O", DigitalIO ? "��" : "��", "�ӿ���ͨ��");
            specs.Add("SCPI���", ScpiProgramming ? "֧��" : "��֧��", "�ӿ���ͨ��");
            if (WebInterface)
            {
                specs.Add("Web����", "֧�� (����ͼʵʱ��ʾ)", "�ӿ���ͨ��");
            }
            specs.Add("I/O�ӿ�", "�ο�����/��� (10 MHz)���ⲿ�ſ�", "�ӿ���ͨ��");

            // ����֧��
            if (BenchVueSupport || !string.IsNullOrEmpty(DataExportFormats))
            {
                specs.Add("Զ�̱��", "SCPI ���֧�����в���", "����֧��");
                if (BenchVueSupport)
                {
                    specs.Add("��������", "BenchVue��Keysight IO Libraries", "����֧��");
                }
                specs.Add("����֧��", "VISA��IVI-COM", "����֧��");
                if (!string.IsNullOrEmpty(DataExportFormats))
                {
                    specs.Add("���ݵ���", DataExportFormats, "����֧��");
                }
            }

            // ��������
            specs.Add("�ߴ� (W��H��D)", Dimensions, "��������");
            specs.Add("����", $"{Weight} kg", "��������");
            specs.Add("��������", FormFactor, "��������");
            specs.Add("���ܰ�װ", RackMountabel ? "֧��" : "��֧��", "��������");

            // ��������
            specs.Add("��Դ����", PowerRequirement, "��������");
            specs.Add("�����¶�", OperatingTemp, "��������");
            specs.Add("�洢�¶�", StorageTemp, "��������");
            specs.Add("����ʪ��", Humidity, "��������");
            specs.Add("���θ߶�", Altitude, "��������");

            // ������֧��
            if (!string.IsNullOrEmpty(WarrantyPeriod))
            {
                specs.Add("������", WarrantyPeriod, "������֧��");
            }
            if (!string.IsNullOrEmpty(CalibrationInterval))
            {
                specs.Add("У׼���", CalibrationInterval, "������֧��");
            }
            specs.Add("����֧��", "Keysight Technologies; www.keysight.com", "������֧��");

            // ѡ���
            if (Ch3Available || OcxoOption || BatteryOption)
            {
                if (Ch3Available)
                {
                    specs.Add("��ѡCH3", "350 MHz RF ͨ��", "ѡ���");
                }
                if (OcxoOption)
                {
                    specs.Add("��ѡOCXO", "���ȶ�ʱ��׼ (��50 ppb)", "ѡ���");
                }
                if (BatteryOption)
                {
                    specs.Add("��ѡ���", "��Я��Դ", "ѡ���");
                }
                if (GpibInterface == false)
                {
                    specs.Add("��ѡGPIB", "����ӿ�", "ѡ���");
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

        #region ��������

        /// <summary>
        /// ��ȡ����ģʽ����
        /// </summary>
        public string GetMeasurementModeDescription()
        {
            return GetMeasurementModeDescription(MeasurementMode);
        }

        /// <summary>
        /// ��ȡ����ģʽ��������̬��
        /// </summary>
        public static string GetMeasurementModeDescription(FrequencyCounterMeasurementMode mode)
        {
            switch (mode)
            {
                case FrequencyCounterMeasurementMode.Frequency:
                    return "Ƶ�ʲ���";
                case FrequencyCounterMeasurementMode.Period:
                    return "���ڲ���";
                case FrequencyCounterMeasurementMode.TimeInterval:
                    return "ʱ��������";
                case FrequencyCounterMeasurementMode.PulseWidth:
                    return "������Ȳ���";
                case FrequencyCounterMeasurementMode.DutyCycle:
                    return "ռ�ձȲ���";
                case FrequencyCounterMeasurementMode.RiseTime:
                    return "����ʱ�����";
                case FrequencyCounterMeasurementMode.FallTime:
                    return "�½�ʱ�����";
                case FrequencyCounterMeasurementMode.Phase:
                    return "��λ����";
                case FrequencyCounterMeasurementMode.Ratio:
                    return "Ƶ�ʱȲ���";
                case FrequencyCounterMeasurementMode.TotalizeCount:
                    return "�ۼƼ���";
                default:
                    return mode.ToString();
            }
        }

        #endregion
    }

    /// <summary>
    /// Ƶ�ʼ�����ڵ㣨�򻯰棩
    /// </summary>
    public class FrequencyCounterInputNode : DeviceBase
    {
        public override string DeviceTypeName => "����ͨ��";

        public FrequencyCounterInputNode()
        {
            DeviceType = "SubNode";
            ParentNode = "Ƶ�ʼ�����";
            SlotPosition = "COUNTER";
            Status = "����";
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
    /// Ƶ�ʼ�ͨ���ڵ㣨��ϸ�棩
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
        /// ͨ�����
        /// </summary>
        public int ChannelNumber
        {
            get => _channelNumber;
            set => SetProperty(ref _channelNumber, value);
        }

        /// <summary>
        /// ͨ������ (��׼/RF)
        /// </summary>
        public string ChannelType
        {
            get => _channelType;
            set => SetProperty(ref _channelType, value);
        }

        /// <summary>
        /// ���Ƶ��
        /// </summary>
        public string MaxFrequency
        {
            get => _maxFrequency;
            set => SetProperty(ref _maxFrequency, value);
        }

        /// <summary>
        /// ͨ���Ƿ�����
        /// </summary>
        public bool IsEnabled
        {
            get => _isEnabled;
            set => SetProperty(ref _isEnabled, value);
        }

        /// <summary>
        /// ��Ϸ�ʽ
        /// </summary>
        public FrequencyCounterCoupling Coupling
        {
            get => _coupling;
            set => SetProperty(ref _coupling, value);
        }

        /// <summary>
        /// �����迹
        /// </summary>
        public FrequencyCounterImpedance Impedance
        {
            get => _impedance;
            set => SetProperty(ref _impedance, value);
        }

        /// <summary>
        /// ������ƽ (V)
        /// </summary>
        public double TriggerLevel
        {
            get => _triggerLevel;
            set => SetProperty(ref _triggerLevel, value);
        }

        /// <summary>
        /// ����б��
        /// </summary>
        public string TriggerSlope
        {
            get => _triggerSlope;
            set => SetProperty(ref _triggerSlope, value);
        }

        public override string DeviceTypeName => "Ƶ�ʼ�ͨ��";

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
            ParentNode = "Ƶ�ʼ�����";
            Status = "����";
        }

        public override void InitializeChildren()
        {
            // ͨ���ڵ�û���ӽڵ�
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
