using Prism.Mvvm;

namespace MeasureControl.Models
{
    /// <summary>
    /// 通道配置（用于保存通道的使能状态和量程设置）
    /// </summary>
    public class ChannelConfig : BindableBase
    {
        private string _channelName;
        private bool _isEnabled;
        private string _range;

        /// <summary>
        /// 通道名称（如AI0、DI1等）
        /// </summary>
        public string ChannelName
        {
            get => _channelName;
            set => SetProperty(ref _channelName, value);
        }

        /// <summary>
        /// 是否启用
        /// </summary>
        public bool IsEnabled
        {
            get => _isEnabled;
            set => SetProperty(ref _isEnabled, value);
        }

        /// <summary>
        /// 量程（如±10V、±5V等）
        /// </summary>
        public string Range
        {
            get => _range;
            set => SetProperty(ref _range, value);
        }

        public ChannelConfig()
        {
            IsEnabled = true;
            Range = "±10V";
        }

        public ChannelConfig(string channelName, bool isEnabled, string range) : this()
        {
            ChannelName = channelName;
            IsEnabled = isEnabled;
            Range = range;
        }
    }
}
