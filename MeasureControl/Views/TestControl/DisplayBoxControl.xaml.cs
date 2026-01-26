using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MeasureControl.Views.TestControl
{
    /// <summary>
    /// 显示框控件 - 绑定模拟量输入(AI)，只读显示实时值
    /// </summary>
    public partial class DisplayBoxControl : UserControl
    {
        #region 依赖属性

        /// <summary>
        /// 控件名称（显示在上方）
        /// </summary>
        public static readonly DependencyProperty ControlNameProperty =
            DependencyProperty.Register("ControlName", typeof(string), typeof(DisplayBoxControl),
                new PropertyMetadata("显示框1", OnControlNameChanged));

        public string ControlName
        {
            get => (string)GetValue(ControlNameProperty);
            set => SetValue(ControlNameProperty, value);
        }

        /// <summary>
        /// 显示的值
        /// </summary>
        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register("Value", typeof(double), typeof(DisplayBoxControl),
                new PropertyMetadata(0.0, OnValueChanged));

        public double Value
        {
            get => (double)GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        /// <summary>
        /// 单位
        /// </summary>
        public static readonly DependencyProperty UnitProperty =
            DependencyProperty.Register("Unit", typeof(string), typeof(DisplayBoxControl),
                new PropertyMetadata("", OnUnitChanged));

        public string Unit
        {
            get => (string)GetValue(UnitProperty);
            set => SetValue(UnitProperty, value);
        }

        /// <summary>
        /// 绑定的变量名
        /// </summary>
        public static readonly DependencyProperty BoundVariableProperty =
            DependencyProperty.Register("BoundVariable", typeof(string), typeof(DisplayBoxControl),
                new PropertyMetadata(null));

        public string BoundVariable
        {
            get => (string)GetValue(BoundVariableProperty);
            set => SetValue(BoundVariableProperty, value);
        }

        /// <summary>
        /// 小数位数
        /// </summary>
        public static readonly DependencyProperty DecimalPlacesProperty =
            DependencyProperty.Register("DecimalPlaces", typeof(int), typeof(DisplayBoxControl),
                new PropertyMetadata(2, OnValueChanged));

        public int DecimalPlaces
        {
            get => (int)GetValue(DecimalPlacesProperty);
            set => SetValue(DecimalPlacesProperty, value);
        }

        /// <summary>
        /// 背景颜色
        /// </summary>
        public static readonly DependencyProperty BackgroundColorProperty =
            DependencyProperty.Register("BackgroundColor", typeof(Color), typeof(DisplayBoxControl),
                new PropertyMetadata(Color.FromRgb(0xe8, 0xeb, 0xed), OnBackgroundColorChanged));

        public Color BackgroundColor
        {
            get => (Color)GetValue(BackgroundColorProperty);
            set => SetValue(BackgroundColorProperty, value);
        }

        /// <summary>
        /// 文字颜色
        /// </summary>
        public static readonly DependencyProperty TextColorProperty =
            DependencyProperty.Register("TextColor", typeof(Color), typeof(DisplayBoxControl),
                new PropertyMetadata(Colors.Black, OnTextColorChanged));

        public Color TextColor
        {
            get => (Color)GetValue(TextColorProperty);
            set => SetValue(TextColorProperty, value);
        }

        #endregion

        public DisplayBoxControl()
        {
            InitializeComponent();
        }

        private static void OnControlNameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is DisplayBoxControl control)
            {
                control.ControlNameText.Text = e.NewValue as string ?? "显示框";
            }
        }

        private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is DisplayBoxControl control)
            {
                control.UpdateValueDisplay();
            }
        }

        private static void OnUnitChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is DisplayBoxControl control)
            {
                control.UnitText.Text = e.NewValue as string ?? "";
            }
        }

        private static void OnBackgroundColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is DisplayBoxControl control && e.NewValue is Color color)
            {
                control.MainBorder.Background = new SolidColorBrush(color);
            }
        }

        private static void OnTextColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is DisplayBoxControl control && e.NewValue is Color color)
            {
                var brush = new SolidColorBrush(color);
                control.ValueText.Foreground = brush;
                control.UnitText.Foreground = brush;
            }
        }

        private void UpdateValueDisplay()
        {
            string format = $"F{DecimalPlaces}";
            ValueText.Text = Value.ToString(format);
        }

        /// <summary>
        /// 更新实时值（供外部调用）
        /// </summary>
        public void UpdateValue(double newValue)
        {
            Value = newValue;
        }
    }
}
