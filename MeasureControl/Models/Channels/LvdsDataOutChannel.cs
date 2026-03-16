using System;

namespace MeasureControl.Models.Channels
{
    /// <summary>
    /// LVDS数据输出通道
    /// </summary>
    public class LvdsDataOutChannel : ChannelBase
    {
        private string _dataRate;
        private string _voltageLevel;
        private double _outputVoltage;
        private string _outputImpedance;
        private double _maxFrequency;

        /// <summary>
        /// 数据传输速率
        /// </summary>
        public string DataRate
        {
            get => _dataRate;
            set => SetProperty(ref _dataRate, value);
        }

        /// <summary>
        /// 电压等级
        /// </summary>
        public string VoltageLevel
        {
            get => _voltageLevel;
            set => SetProperty(ref _voltageLevel, value);
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
        /// 输出阻抗
        /// </summary>
        public string OutputImpedance
        {
            get => _outputImpedance;
            set => SetProperty(ref _outputImpedance, value);
        }

        /// <summary>
        /// 最大频率 (Hz)
        /// </summary>
        public double MaxFrequency
        {
            get => _maxFrequency;
            set => SetProperty(ref _maxFrequency, value);
        }

        public LvdsDataOutChannel()
        {
            ChannelType = "LVDS_DOUT";
            DataRate = "655 Mbps";
            VoltageLevel = "LVDS";
            OutputVoltage = 1.2;
            OutputImpedance = "100Ω";
            MaxFrequency = 655000000; // 655 MHz
        }

        public override bool ValidateConfiguration()
        {
            return base.ValidateConfiguration() &&
                   !string.IsNullOrEmpty(VoltageLevel) &&
                   !string.IsNullOrEmpty(DataRate) &&
                   OutputVoltage > 0 &&
                   MaxFrequency > 0;
        }
    }
}

