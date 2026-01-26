using System;

namespace MeasureControl.Models.Channels
{
    /// <summary>
    /// ARINC429总线通道
    /// </summary>
    public class Arinc429Channel : ChannelBase
    {
        private string _baudRate;
        private string _direction;
        private string _parity;
        private string _voltage;
        private bool _supportsLabel;

        /// <summary>
        /// 波特率（100K/12.5K）
        /// </summary>
        public string BaudRate
        {
            get => _baudRate;
            set => SetProperty(ref _baudRate, value);
        }

        /// <summary>
        /// 方向（TX/RX/TXRX）
        /// </summary>
        public string Direction
        {
            get => _direction;
            set => SetProperty(ref _direction, value);
        }

        /// <summary>
        /// 校验方式（Odd/Even）
        /// </summary>
        public string Parity
        {
            get => _parity;
            set => SetProperty(ref _parity, value);
        }

        /// <summary>
        /// 电压标准（HighVoltage/LowVoltage）
        /// </summary>
        public string Voltage
        {
            get => _voltage;
            set => SetProperty(ref _voltage, value);
        }

        /// <summary>
        /// 支持标签过滤
        /// </summary>
        public bool SupportsLabel
        {
            get => _supportsLabel;
            set => SetProperty(ref _supportsLabel, value);
        }

        public Arinc429Channel()
        {
            ChannelType = "ARINC429";
            BaudRate = "100K";
            Direction = "TXRX";
            Parity = "Odd";
            Voltage = "HighVoltage";
            SupportsLabel = true;
        }

        public override bool ValidateConfiguration()
        {
            return base.ValidateConfiguration() &&
                   !string.IsNullOrEmpty(BaudRate) &&
                   !string.IsNullOrEmpty(Direction);
        }
    }
}

