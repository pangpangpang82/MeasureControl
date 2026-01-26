using System.Collections.ObjectModel;
using Prism.Mvvm;

namespace MeasureControl.Models
{
    /// <summary>
    /// 控件配置项 - 用于测试界面下方的控件参数配置
    /// </summary>
    public class ControlConfigItem : BindableBase
    {
        private string _propertyName;
        /// <summary>
        /// 属性名称（用于更新控件属性）
        /// </summary>
        public string PropertyName
        {
            get => _propertyName;
            set => SetProperty(ref _propertyName, value);
        }

        private string _label;
        /// <summary>
        /// 显示标签
        /// </summary>
        public string Label
        {
            get => _label;
            set => SetProperty(ref _label, value);
        }

        private string _value;
        /// <summary>
        /// 当前值
        /// </summary>
        public string Value
        {
            get => _value;
            set => SetProperty(ref _value, value);
        }

        private string _configType;
        /// <summary>
        /// 配置类型：TextBox, ColorPicker, ComboBox
        /// </summary>
        public string ConfigType
        {
            get => _configType;
            set => SetProperty(ref _configType, value);
        }

        private bool _isEnabled = true;
        /// <summary>
        /// 是否启用
        /// </summary>
        public bool IsEnabled
        {
            get => _isEnabled;
            set => SetProperty(ref _isEnabled, value);
        }

        private ObservableCollection<VariableItem> _options;
        /// <summary>
        /// 下拉框选项（仅ComboBox类型使用，用于变量选择）
        /// </summary>
        public ObservableCollection<VariableItem> Options
        {
            get => _options;
            set => SetProperty(ref _options, value);
        }

        private ObservableCollection<string> _simpleOptions;
        /// <summary>
        /// 简单下拉框选项（用于固定选项如刷新频率）
        /// </summary>
        public ObservableCollection<string> SimpleOptions
        {
            get => _simpleOptions;
            set => SetProperty(ref _simpleOptions, value);
        }

        private VariableItem _selectedVariableItem;
        /// <summary>
        /// 选中的变量项（用于ComboBox的SelectedItem绑定）
        /// </summary>
        public VariableItem SelectedVariableItem
        {
            get => _selectedVariableItem;
            set
            {
                if (SetProperty(ref _selectedVariableItem, value))
                {
                    // 当选中项变化时，更新Value为FullPath
                    if (value != null)
                    {
                        Value = value.FullPath;
                    }
                    else
                    {
                        Value = "";
                    }
                }
            }
        }
    }

    /// <summary>
    /// 变量项（用于下拉框选项）
    /// </summary>
    public class VariableItem
    {
        /// <summary>
        /// 变量名称
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 变量类型
        /// </summary>
        public string Type { get; set; }

        /// <summary>
        /// 单位
        /// </summary>
        public string Unit { get; set; }

        /// <summary>
        /// 完整路径
        /// </summary>
        public string FullPath { get; set; }

        /// <summary>
        /// 重写ToString以便在ComboBox选中项区域正确显示名称
        /// </summary>
        public override string ToString() => Name ?? string.Empty;
    }
}
