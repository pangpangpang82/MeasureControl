using System;

namespace MeasureControl.Models.Channels
{
    /// <summary>
    /// MIL-STD-1553B总线通道
    /// </summary>
    public class Mil1553BChannel : ChannelBase
    {
        private string _nodeType;
        private string _busType;
        private int _rtAddress;
        private bool _supportsCoupling;
        private string _voltage;

        /// <summary>
        /// 节点类型（BC-总线控制器/RT-远程终端/MT-监视器）
        /// </summary>
        public string NodeType
        {
            get => _nodeType;
            set => SetProperty(ref _nodeType, value);
        }

        /// <summary>
        /// 总线类型（BusA/BusB/Dual）
        /// </summary>
        public string BusType
        {
            get => _busType;
            set => SetProperty(ref _busType, value);
        }

        /// <summary>
        /// RT地址（0-31，仅RT模式有效）
        /// </summary>
        public int RtAddress
        {
            get => _rtAddress;
            set => SetProperty(ref _rtAddress, value);
        }

        /// <summary>
        /// 支持耦合变压器
        /// </summary>
        public bool SupportsCoupling
        {
            get => _supportsCoupling;
            set => SetProperty(ref _supportsCoupling, value);
        }

        /// <summary>
        /// 电压标准（Direct/Transformer）
        /// </summary>
        public string Voltage
        {
            get => _voltage;
            set => SetProperty(ref _voltage, value);
        }

        public Mil1553BChannel()
        {
            ChannelType = "1553B";
            NodeType = "BC";
            BusType = "Dual";
            RtAddress = 1;
            SupportsCoupling = true;
            Voltage = "Transformer";
        }

        public override bool ValidateConfiguration()
        {
            return base.ValidateConfiguration() &&
                   !string.IsNullOrEmpty(NodeType) &&
                   !string.IsNullOrEmpty(BusType) &&
                   (NodeType != "RT" || (RtAddress >= 0 && RtAddress <= 31));
        }
    }
}

