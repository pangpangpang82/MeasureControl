using System;
using MeasureControl.Models.Devices;

namespace MeasureControl.Models.Channels
{
    /// <summary>
    /// LVDT/RVDT模拟通道
    /// </summary>
    public class LvdtChannel : ChannelBase
    {
        private double _excitationFrequency;
        private double _excitationVoltage;
        private double _outputRange;
        private double _accuracy;
        private string _sensorType;
        private OperationMode _operationMode;

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
        /// 通道工作模式（仿真/测量/双向），可独立配置
        /// </summary>
        public OperationMode OperationMode
        {
            get => _operationMode;
            set => SetProperty(ref _operationMode, value);
        }

        public LvdtChannel()
        {
            ChannelType = "LVDT";
            ExcitationFrequency = 5000; // 5kHz
            ExcitationVoltage = 3.0;
            OutputRange = 5.0;
            Accuracy = 0.1;
            SensorType = "LVDT";
            OperationMode = OperationMode.Bidirectional;
        }

        public override bool ValidateConfiguration()
        {
            return base.ValidateConfiguration() &&
                   ExcitationFrequency > 0 &&
                   ExcitationVoltage > 0 &&
                   OutputRange > 0;
        }

        /// <summary>
        /// 获取励磁频率范围字符串
        /// </summary>
        public string GetFrequencyRangeString()
        {
            return $"{ExcitationFrequency / 1000}kHz";
        }
    }
}

