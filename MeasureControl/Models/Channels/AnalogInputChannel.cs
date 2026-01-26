using System;

namespace MeasureControl.Models.Channels
{
    /// <summary>
    /// 模拟量输入通道
    /// </summary>
    public class AnalogInputChannel : ChannelBase
    {
        private double _minRange;
        private double _maxRange;
        private double _sampleRate;
        private int _resolution;
        private string _couplingMode;
        private string _inputMode;

        /// <summary>
        /// 最小量程 (V)
        /// </summary>
        public double MinRange
        {
            get => _minRange;
            set => SetProperty(ref _minRange, value);
        }

        /// <summary>
        /// 最大量程 (V)
        /// </summary>
        public double MaxRange
        {
            get => _maxRange;
            set => SetProperty(ref _maxRange, value);
        }

        /// <summary>
        /// 采样率 (Hz)
        /// </summary>
        public double SampleRate
        {
            get => _sampleRate;
            set => SetProperty(ref _sampleRate, value);
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
        /// 耦合模式（AC/DC）
        /// </summary>
        public string CouplingMode
        {
            get => _couplingMode;
            set => SetProperty(ref _couplingMode, value);
        }

        /// <summary>
        /// 输入模式（Differential/RSE/NRSE）
        /// </summary>
        public string InputMode
        {
            get => _inputMode;
            set => SetProperty(ref _inputMode, value);
        }

        public AnalogInputChannel()
        {
            ChannelType = "AI";
            MinRange = -10.0;
            MaxRange = 10.0;
            SampleRate = 1000;
            Resolution = 16;
            CouplingMode = "DC";
            InputMode = "Differential";
        }

        public override bool ValidateConfiguration()
        {
            return base.ValidateConfiguration() &&
                   MinRange < MaxRange &&
                   SampleRate > 0 &&
                   Resolution > 0;
        }

        /// <summary>
        /// 获取量程字符串
        /// </summary>
        public string GetRangeString()
        {
            if (MinRange == -MaxRange)
            {
                return $"±{MaxRange}V";
            }
            return $"{MinRange}V ~ {MaxRange}V";
        }
    }
}

