using System;
using Prism.Mvvm;

namespace MeasureControl.Models
{
    /// <summary>
    /// 通道表格配置项（用于通道配置表视图）
    /// </summary>
    public class ChannelTabelItem : BindableBase
    {
        private string _id;
        private int _;
        private string _channelName;
        private string _cardName;
        private string _chassisName;
        private string _remarks;
        private string _channelType;
        private string _inputOutputType;
        private string _associatedChannel;
        private bool _isEmpty;

        /// <summary>
        /// 通道唯一标识（用于稳定引用，避免依赖名称）
        /// </summary>
        public string Id
        {
            get => _id;
            set => SetProperty(ref _id, value);
        }

        public int Index
        {
            get => _;
            set => SetProperty(ref _, value);
        }

        public bool IsEmpty
        {
            get => _isEmpty;
            set => SetProperty(ref _isEmpty, value);
        }

        public string ChannelName
        {
            get => _channelName;
            set => SetProperty(ref _channelName, value);
        }

        public string CardName
        {
            get => _cardName;
            set
            {
                if (SetProperty(ref _cardName, value))
                {
                    RaisePropertyChanged(nameof(PhysicalChannel));
                }
            }
        }

        public string ChassisName
        {
            get => _chassisName;
            set => SetProperty(ref _chassisName, value);
        }

        public string Remarks
        {
            get => _remarks;
            set => SetProperty(ref _remarks, value);
        }

        public string ChannelType
        {
            get => _channelType;
            set => SetProperty(ref _channelType, value);
        }

        public string InputOutputType
        {
            get => _inputOutputType;
            set => SetProperty(ref _inputOutputType, value);
        }

        public string AssociatedChannel
        {
            get => _associatedChannel;
            set
            {
                if (SetProperty(ref _associatedChannel, value))
                {
                    RaisePropertyChanged(nameof(PhysicalChannel));
                }
            }
        }

        public string PhysicalChannel
        {
            get
            {
                if (string.IsNullOrEmpty(CardName) && string.IsNullOrEmpty(AssociatedChannel))
                {
                    return string.Empty;
                }

                if (string.IsNullOrEmpty(CardName))
                {
                    return AssociatedChannel ?? string.Empty;
                }

                if (string.IsNullOrEmpty(AssociatedChannel))
                {
                    return CardName;
                }

                return $"{CardName}-{AssociatedChannel}";
            }
        }

        /// <summary>
        /// 构造函数 - 自动生成唯一Id
        /// </summary>
        public ChannelTabelItem()
        {
            _id = Guid.NewGuid().ToString("N");
        }

        public ChannelTabelItem Clone() => new ChannelTabelItem
        {
            Id = Id, // 保留原Id用于跟踪
            Index = Index, ChannelName = ChannelName, CardName = CardName, ChassisName = ChassisName,
            Remarks = Remarks, ChannelType = ChannelType, InputOutputType = InputOutputType,
            AssociatedChannel = AssociatedChannel, IsEmpty = IsEmpty
        };
    }
}

