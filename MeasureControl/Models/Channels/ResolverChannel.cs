using System;
using MeasureControl.Models.Devices;

namespace MeasureControl.Models.Channels
{
    /// <summary>
    /// 旋转变压器（Resolver）模拟通道
    /// </summary>
    public class ResolverChannel : ChannelBase
    {
        private double _excitationFrequency;
        private double _excitationVoltage;
        private double _outputVoltage;
        private double _angleAccuracy;
        private string _resolverType;
        private int _polePairs;
        private OperationMode _operationMode;

        // Resolver特有属性
        private double _phaseDifference;
        private double _outputAngle;
        private double _motorSpeed;
        private double[] _waveformData;
        private bool _autoLoadWaveform;
        private double _waveformStartAngle;
        private double _waveformEndAngle;

        /// <summary>
        /// 励磁频率 (Hz)
        /// </summary>
        public double ExcitationFrequency
        {
            get => _excitationFrequency;
            set => SetProperty(ref _excitationFrequency, value);
        }

        /// <summary>
        /// 励磁电压 (V)
        /// </summary>
        public double ExcitationVoltage
        {
            get => _excitationVoltage;
            set => SetProperty(ref _excitationVoltage, value);
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
        /// 旋变类型（Resolver/Inductosyn）
        /// </summary>
        public string ResolverType
        {
            get => _resolverType;
            set => SetProperty(ref _resolverType, value);
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
        /// 工作模式（仿真/测量/双向），通道级配置
        /// </summary>
        public OperationMode OperationMode
        {
            get => _operationMode;
            set => SetProperty(ref _operationMode, value);
        }

        /// <summary>
        /// 相位差（度），用于Resolver输出信号间的相位调整
        /// </summary>
        public double PhaseDifference
        {
            get => _phaseDifference;
            set => SetProperty(ref _phaseDifference, Math.Max(-180.0, Math.Min(180.0, value)));
        }

        /// <summary>
        /// 输出角度（度），Resolver的旋转角度输出
        /// </summary>
        public double OutputAngle
        {
            get => _outputAngle;
            set => SetProperty(ref _outputAngle, Math.Max(0.0, Math.Min(360.0, value)));
        }

        /// <summary>
        /// 电机速度（RPM），用于Resolver仿真
        /// </summary>
        public double MotorSpeed
        {
            get => _motorSpeed;
            set => SetProperty(ref _motorSpeed, Math.Max(0.0, value));
        }

        /// <summary>
        /// 波形数据，用于自定义Resolver波形输出
        /// </summary>
        public double[] WaveformData
        {
            get => _waveformData;
            set => SetProperty(ref _waveformData, value);
        }

        /// <summary>
        /// 是否自动加载波形
        /// </summary>
        public bool AutoLoadWaveform
        {
            get => _autoLoadWaveform;
            set => SetProperty(ref _autoLoadWaveform, value);
        }

        /// <summary>
        /// 波形起始角度（度）
        /// </summary>
        public double WaveformStartAngle
        {
            get => _waveformStartAngle;
            set => SetProperty(ref _waveformStartAngle, Math.Max(0.0, Math.Min(360.0, value)));
        }

        /// <summary>
        /// 波形结束角度（度）
        /// </summary>
        public double WaveformEndAngle
        {
            get => _waveformEndAngle;
            set => SetProperty(ref _waveformEndAngle, Math.Max(0.0, Math.Min(360.0, value)));
        }

        public ResolverChannel()
        {
            ChannelType = "Resolver";
            ExcitationFrequency = 5000; // 5kHz
            ExcitationVoltage = 7.0;
            OutputVoltage = 14.0;
            AngleAccuracy = 0.05;
            ResolverType = "Resolver";
            PolePairs = 1;
            OperationMode = OperationMode.Bidirectional;
        }

        public override bool ValidateConfiguration()
        {
            return base.ValidateConfiguration() &&
                   ExcitationFrequency > 0 &&
                   ExcitationVoltage > 0 &&
                   OutputVoltage > 0 &&
                   PolePairs > 0;
        }
    }
}

