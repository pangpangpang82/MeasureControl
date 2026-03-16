using Prism.Mvvm;
using System;

namespace MeasureControl.Models
{
    /// <summary>
    /// 动态显示的设备字段（用于设备详细信息区域）
    /// Label: 字段名（如 "分辨率"）
    /// Value: 字段值（如 "16-bit"）
    /// Format: 可选的显示格式，支持一个占位符 {0} 表示 Value（例如 "分辨率: {0}"）
    /// DisplayText: 根据 Format 或默认规则生成要显示的文本
    /// </summary>
    public class DeviceDisplayField : BindableBase
    {
        private string _label;
        private string _value;
        private string _format;

        public string Label
        {
            get => _label;
            set => SetProperty(ref _label, value);
        }

        public string Value
        {
            get => _value;
            set
            {
                if (SetProperty(ref _value, value))
                {
                    RaisePropertyChanged(nameof(DisplayText));
                }
            }
        }

        /// <summary>
        /// 格式字符串，支持单个占位符 {0} 表示 Value。
        /// 如果为空，则默认使用 "Label: Value" 格式。
        /// </summary>
        public string Format
        {
            get => _format;
            set
            {
                if (SetProperty(ref _format, value))
                {
                    RaisePropertyChanged(nameof(DisplayText));
                }
            }
        }

        public string DisplayText
        {
            get
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(Format))
                    {
                        return string.Format(Format, Value ?? string.Empty);
                    }
                }
                catch
                {
                    // ignore format errors
                }
                return $"{Label}: {Value}";
            }
        }

        public DeviceDisplayField()
        {
        }

        public DeviceDisplayField(string label, string value, string format = null)
        {
            Label = label;
            Value = value;
            Format = format;
        }
    }
}


