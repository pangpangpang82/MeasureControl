using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;

namespace MeasureControl.Models
{
    /// <summary>
    /// 测试界面控件项 - 用于保存和加载控件数据
    /// </summary>
    public class TestInterfaceControlItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        /// <summary>
        /// 控件唯一标识
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// 控件名称（显示在控件上方）
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 控件类型（Button/Switch/Indicator/TextLabel/DisplayBox/InputBox/CircularGauge/VerticalGauge）
        /// </summary>
        public string ControlType { get; set; }

        /// <summary>
        /// 按钮文字（仅 Button 类型有效）
        /// </summary>
        public string ButtonText { get; set; }

        /// <summary>
        /// 背景颜色（十六进制，如 #e8ebed）
        /// </summary>
        public string BackgroundColor { get; set; }

        /// <summary>
        /// 文字颜色（十六进制，如 #000000）
        /// </summary>
        public string TextColor { get; set; }

        /// <summary>
        /// 绑定的变量名称
        /// </summary>
        public string BoundVariableName { get; set; }

        /// <summary>
        /// 绑定的变量路径（旧字段，保留兼容）
        /// </summary>
        public string BoundVariablePath { get; set; }

        /// <summary>
        /// 绑定的变量Id（新字段，用于稳定引用，替代 BoundVariablePath）
        /// </summary>
        public string BindingVariableId { get; set; }

        /// <summary>
        /// 绑定的变量类型（数字量/模拟量）
        /// </summary>
        public string BoundVariableType { get; set; }

        /// <summary>
        /// 单位（模拟量专用）
        /// </summary>
        public string Unit { get; set; }

        /// <summary>
        /// 最大值（用于环形仪表等需要范围显示的控件）
        /// </summary>
        public double MaxValue { get; set; } = 100.0;

        /// <summary>
        /// 手动设置的值（优先级高于绑定变量，用于环形仪表和竖形仪表）
        /// </summary>
        public double? ManualValue { get; set; }

        /// <summary>
        /// 刷新频率（Hz），用于显示框和指示灯实时刷新
        /// 可选值：10, 50, 100, 500
        /// </summary>
        public int RefreshRate { get; set; } = 10;

        /// <summary>
        /// 在 Canvas 上的 X 坐标
        /// </summary>
        public double PositionX { get; set; }

        /// <summary>
        /// 在 Canvas 上的 Y 坐标
        /// </summary>
        public double PositionY { get; set; }

        /// <summary>
        /// 控件宽度
        /// </summary>
        public double Width { get; set; }

        /// <summary>
        /// 控件高度
        /// </summary>
        public double Height { get; set; }

        /// <summary>
        /// 小数位数（0-5，用于DisplayBox和InputBox）
        /// </summary>
        public int DecimalPlaces { get; set; } = 2;

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreatedTime { get; set; }

        private double _currentValue;
        /// <summary>
        /// 当前实时值（运行时数据，不序列化保存）
        /// </summary>
        [JsonIgnore]
        public double CurrentValue
        {
            get => _currentValue;
            set
            {
                if (_currentValue != value)
                {
                    _currentValue = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsOn));
                    OnPropertyChanged(nameof(DisplayValue));
                }
            }
        }
        
        /// <summary>
        /// 指示灯状态（用于 Indicator 控件绑定）
        /// </summary>
        [JsonIgnore]
        public bool IsOn => CurrentValue > 0.5;
        
        /// <summary>
        /// 显示值（用于 DisplayBox 控件绑定）
        /// </summary>
        [JsonIgnore]
        public string DisplayValue => CurrentValue.ToString("F2");

        public TestInterfaceControlItem()
        {
            Id = Guid.NewGuid().ToString("N");
            CreatedTime = DateTime.Now;
        }
    }
}
