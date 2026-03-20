using System;

namespace MeasureControl.Models.Channels
{
    /// <summary>
    /// CAN总线通道
    /// </summary>
    public class CanChannel : ChannelBase
    {
        private int _maxBaudRate;
        private string _protocol;
        private bool _supportsExtendedFrame;
        private bool _supportsCanFD;
        private string _transceiverType;
        private string _termination;

        /// <summary>
        /// 最大波特率 (bps)
        /// </summary>
        public int MaxBaudRate
        {
            get => _maxBaudRate;
            set => SetProperty(ref _maxBaudRate, value);
        }

        /// <summary>
        /// 协议版本（CAN2.0A/CAN2.0B/CANFD）
        /// </summary>
        public string Protocol
        {
            get => _protocol;
            set => SetProperty(ref _protocol, value);
        }

        /// <summary>
        /// 支持扩展帧（29位ID）
        /// </summary>
        public bool SupportsExtendedFrame
        {
            get => _supportsExtendedFrame;
            set => SetProperty(ref _supportsExtendedFrame, value);
        }

        /// <summary>
        /// 支持CAN FD
        /// </summary>
        public bool SupportsCanFD
        {
            get => _supportsCanFD;
            set => SetProperty(ref _supportsCanFD, value);
        }

        /// <summary>
        /// 收发器类型（HighSpeed/LowSpeed/SingleWire）
        /// </summary>
        public string TransceiverType
        {
            get => _transceiverType;
            set => SetProperty(ref _transceiverType, value);
        }

        /// <summary>
        /// 终端电阻（120Ω/None）
        /// </summary>
        public string Termination
        {
            get => _termination;
            set => SetProperty(ref _termination, value);
        }

        public CanChannel()
        {
            ChannelType = "CAN";
            MaxBaudRate = 1000000; // 1Mbps
            Protocol = "CAN2.0B";
            SupportsExtendedFrame = true;
            SupportsCanFD = false;
            TransceiverType = "HighSpeed";
            Termination = "120Ω";
        }

        public override bool ValidateConfiguration()
        {
            return base.ValidateConfiguration() &&
                   MaxBaudRate > 0 &&
                   !string.IsNullOrEmpty(Protocol);
        }
    }
}

