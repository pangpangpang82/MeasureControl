using System;
using MeasureControl.Models.Devices;

namespace MeasureControl.Models.Channels
{
    /// <summary>
    /// IEEE 1394B (FireWire) 总线通道
    /// </summary>
    public class Mil1394BChannel : ChannelBase
    {
        private int _nodeNumber;
        private int _channelNumber;
        private int _maxSpeed;
        private string _protocol;
        private bool _supportsBeta;
        private string _cableType;
        private Mil1394BDeviceRole _deviceRole;
        private Mil1394BTransferMode _transferMode;
        private Mil1394BSpeedMode _speedMode;
        private Mil1394BMediaType _mediaType;

        /// <summary>
        /// 节点编号
        /// </summary>
        public int NodeNumber
        {
            get => _nodeNumber;
            set => SetProperty(ref _nodeNumber, value);
        }

        /// <summary>
        /// 通道编号（1-64）
        /// </summary>
        public int ChannelNumber
        {
            get => _channelNumber;
            set => SetProperty(ref _channelNumber, value);
        }

        /// <summary>
        /// 最大速度 (Mbps: 100/200/400/800)
        /// </summary>
        public int MaxSpeed
        {
            get => _maxSpeed;
            set => SetProperty(ref _maxSpeed, value);
        }

        /// <summary>
        /// 协议标准（S100/S200/S400/S800）
        /// </summary>
        public string Protocol
        {
            get => _protocol;
            set => SetProperty(ref _protocol, value);
        }

        /// <summary>
        /// 支持Beta模式
        /// </summary>
        public bool SupportsBeta
        {
            get => _supportsBeta;
            set => SetProperty(ref _supportsBeta, value);
        }

        /// <summary>
        /// 线缆类型（Copper/Fiber）
        /// </summary>
        public string CableType
        {
            get => _cableType;
            set => SetProperty(ref _cableType, value);
        }

        /// <summary>
        /// 设备角色
        /// </summary>
        public Mil1394BDeviceRole DeviceRole
        {
            get => _deviceRole;
            set => SetProperty(ref _deviceRole, value);
        }

        /// <summary>
        /// 传输模式
        /// </summary>
        public Mil1394BTransferMode TransferMode
        {
            get => _transferMode;
            set => SetProperty(ref _transferMode, value);
        }

        /// <summary>
        /// 速率模式
        /// </summary>
        public Mil1394BSpeedMode SpeedMode
        {
            get => _speedMode;
            set => SetProperty(ref _speedMode, value);
        }

        /// <summary>
        /// 介质类型
        /// </summary>
        public Mil1394BMediaType MediaType
        {
            get => _mediaType;
            set => SetProperty(ref _mediaType, value);
        }

        public Mil1394BChannel()
        {
            ChannelType = "1394B";
            NodeNumber = 1;
            ChannelNumber = 1;
            MaxSpeed = 800;
            Protocol = "S800";
            SupportsBeta = true;
            CableType = "Copper";
            DeviceRole = Mil1394BDeviceRole.RemoteNode;
            TransferMode = Mil1394BTransferMode.Asynchronous;
            SpeedMode = Mil1394BSpeedMode.S800;
            MediaType = Mil1394BMediaType.Copper;
        }

        public override bool ValidateConfiguration()
        {
            return base.ValidateConfiguration() &&
                   MaxSpeed > 0 &&
                   !string.IsNullOrEmpty(Protocol);
        }
    }
}

