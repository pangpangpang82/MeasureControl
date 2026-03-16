using System;

namespace MeasureControl.Models.Channels
{
    /// <summary>
    /// 模拟量输出通道
    /// </summary>
    public class AnalogOutputChannel : ChannelBase
    {
        private double _minOutput;
        private double _maxOutput;
        private double _updateRate;
        private int _resolution;
        private double _maxCurrent;

        /// <summary>
        /// 最小输出 (V)
        /// </summary>
        public double MinOutput
        {
            get => _minOutput;
            set => SetProperty(ref _minOutput, value);
        }

        /// <summary>
        /// 最大输出 (V)
        /// </summary>
        public double MaxOutput
        {
            get => _maxOutput;
            set => SetProperty(ref _maxOutput, value);
        }

        /// <summary>
        /// 更新速率 (Hz)
        /// </summary>
        public double UpdateRate
        {
            get => _updateRate;
            set => SetProperty(ref _updateRate, value);
        }

        /// <summary>
        /// 分辨率 (位)
        /// </summary>
        public int Resolution
        {
            get => _resolution;
            set => SetProperty(ref _resolution, value);
        }

        /// <summary>
        /// 最大输出电流 (mA)
        /// </summary>
        public double MaxCurrent
        {
            get => _maxCurrent;
            set => SetProperty(ref _maxCurrent, value);
        }

        public AnalogOutputChannel()
        {
            ChannelType = "AO";
            MinOutput = -10.0;
            MaxOutput = 10.0;
            UpdateRate = 1000;
            Resolution = 16;
            MaxCurrent = 20.0;
        }

        public override bool ValidateConfiguration()
        {
            return base.ValidateConfiguration() &&
                   MinOutput < MaxOutput &&
                   UpdateRate > 0 &&
                   Resolution > 0;
        }

        /// <summary>
        /// 获取输出范围字符串
        /// </summary>
        public string GetOutputRangeString()
        {
            if (MinOutput == -MaxOutput)
            {
                return $"±{MaxOutput}V";
            }
            return $"{MinOutput}V ~ {MaxOutput}V";
        }
    }
}

