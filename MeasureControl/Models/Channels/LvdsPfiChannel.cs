using System;

namespace MeasureControl.Models.Channels
{
    /// <summary>
    /// LVDS PFI（可编程功能接口）通道
    /// </summary>
    public class LvdsPfiChannel : ChannelBase
    {
        private string _voltageLevel;
        private string _function;
        private bool _supportsInput;
        private bool _supportsOutput;
        private double _maxFrequency;

        /// <summary>
        /// 电压等级
        /// </summary>
        public string VoltageLevel
        {
            get => _voltageLevel;
            set => SetProperty(ref _voltageLevel, value);
        }

        /// <summary>
        /// 功能（Trigger/Clock/GPIO等）
        /// </summary>
        public string Function
        {
            get => _function;
            set => SetProperty(ref _function, value);
        }

        /// <summary>
        /// 支持输入
        /// </summary>
        public bool SupportsInput
        {
            get => _supportsInput;
            set => SetProperty(ref _supportsInput, value);
        }

        /// <summary>
        /// 支持输出
        /// </summary>
        public bool SupportsOutput
        {
            get => _supportsOutput;
            set => SetProperty(ref _supportsOutput, value);
        }

        /// <summary>
        /// 最大频率 (Hz)
        /// </summary>
        public double MaxFrequency
        {
            get => _maxFrequency;
            set => SetProperty(ref _maxFrequency, value);
        }

        public LvdsPfiChannel()
        {
            ChannelType = "LVDS_PFI";
            VoltageLevel = "LVDS";
            Function = "Trigger";
            SupportsInput = true;
            SupportsOutput = true;
            MaxFrequency = 100000000; // 100 MHz
        }

        public override bool ValidateConfiguration()
        {
            return base.ValidateConfiguration() &&
                   !string.IsNullOrEmpty(VoltageLevel) &&
                   (SupportsInput || SupportsOutput) &&
                   MaxFrequency > 0;
        }
    }
}

