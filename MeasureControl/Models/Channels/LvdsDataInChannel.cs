using System;

namespace MeasureControl.Models.Channels
{
    /// <summary>
    /// LVDS数据输入通道
    /// </summary>
    public class LvdsDataInChannel : ChannelBase
    {
        private string _dataRate;
        private string _voltageLevel;
        private string _inputImpedance;
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
        /// 输入阻抗
        /// </summary>
        public string InputImpedance
        {
            get => _inputImpedance;
            set => SetProperty(ref _inputImpedance, value);
        }

        /// <summary>
        /// 最大频率 (Hz)
        /// </summary>
        public double MaxFrequency
        {
            get => _maxFrequency;
            set => SetProperty(ref _maxFrequency, value);
        }

        public LvdsDataInChannel()
        {
            ChannelType = "LVDS_DIN";
            DataRate = "655 Mbps";
            VoltageLevel = "LVDS";
            InputImpedance = "100Ω";
            MaxFrequency = 655000000; // 655 MHz
        }

        public override bool ValidateConfiguration()
        {
            return base.ValidateConfiguration() &&
                   !string.IsNullOrEmpty(VoltageLevel) &&
                   !string.IsNullOrEmpty(DataRate) &&
                   MaxFrequency > 0;
        }
    }
}

