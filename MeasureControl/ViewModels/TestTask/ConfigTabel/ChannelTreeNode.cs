using System.Collections.ObjectModel;
using Prism.Mvvm;

namespace MeasureControl.ViewModels
{
    /// <summary>
    /// 通道树节点模型
    /// 用于在ChannelConfigTabel中显示PXI机箱、板卡和通道的树形结构
    /// </summary>
    public class ChannelTreeNode : BindableBase
    {
        private string _displayName;
        private string _nodeType;
        private bool _isExpanded;
        private bool _isSelected;
        private ObservableCollection<ChannelTreeNode> _children;
        private object _tag;

        /// <summary>
        /// 显示名称
        /// </summary>
        public string DisplayName
        {
            get => _displayName;
            set => SetProperty(ref _displayName, value);
        }

        /// <summary>
        /// 节点类型："Chassis" | "Card" | "Node" | "Channel"
        /// 对于1394B板卡，使用三级结构：Chassis -> Card -> Node -> Channel
        /// </summary>
        public string NodeType
        {
            get => _nodeType;
            set => SetProperty(ref _nodeType, value);
        }

        /// <summary>
        /// 是否展开
        /// </summary>
        public bool IsExpanded
        {
            get => _isExpanded;
            set => SetProperty(ref _isExpanded, value);
        }

        /// <summary>
        /// 是否选中
        /// </summary>
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        /// <summary>
        /// 子节点集合
        /// </summary>
        public ObservableCollection<ChannelTreeNode> Children
        {
            get => _children;
            set => SetProperty(ref _children, value);
        }

        /// <summary>
        /// 存储原始对象（ChassisModel/DeviceBase/ChannelInfo等）
        /// </summary>
        public object Tag
        {
            get => _tag;
            set => SetProperty(ref _tag, value);
        }

        public ChannelTreeNode()
        {
            Children = new ObservableCollection<ChannelTreeNode>();
            IsExpanded = false;
        }

        public ChannelTreeNode(string displayName, string nodeType, object tag = null) : this()
        {
            DisplayName = displayName;
            NodeType = nodeType;
            Tag = tag;
        }
    }
}

