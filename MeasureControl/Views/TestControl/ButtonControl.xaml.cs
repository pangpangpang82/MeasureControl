using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MeasureControl.Views.TestControl
{
    /// <summary>
    /// 按钮控件 - 只能绑定数字量
    /// </summary>
    public partial class ButtonControl : UserControl
    {
        #region 依赖属性

        /// <summary>
        /// 按钮文本
        /// </summary>
        public static readonly DependencyProperty TextProperty =
            DependencyProperty.Register("Text", typeof(string), typeof(ButtonControl),
                new PropertyMetadata("按钮", OnTextChanged));

        public string Text
        {
            get => (string)GetValue(TextProperty);
            set => SetValue(TextProperty, value);
        }

        /// <summary>
        /// 绑定的变量名（数字量）
        /// </summary>
        public static readonly DependencyProperty BoundVariableProperty =
            DependencyProperty.Register("BoundVariable", typeof(string), typeof(ButtonControl),
                new PropertyMetadata(null));

        public string BoundVariable
        {
            get => (string)GetValue(BoundVariableProperty);
            set => SetValue(BoundVariableProperty, value);
        }

        /// <summary>
        /// 按钮点击时设置的值（0或1）
        /// </summary>
        public static readonly DependencyProperty OutputValueProperty =
            DependencyProperty.Register("OutputValue", typeof(int), typeof(ButtonControl),
                new PropertyMetadata(1));

        public int OutputValue
        {
            get => (int)GetValue(OutputValueProperty);
            set => SetValue(OutputValueProperty, value);
        }

        /// <summary>
        /// 背景颜色
        /// </summary>
        public static readonly DependencyProperty BackgroundColorProperty =
            DependencyProperty.Register("BackgroundColor", typeof(Color), typeof(ButtonControl),
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
            DependencyProperty.Register("TextColor", typeof(Color), typeof(ButtonControl),
                new PropertyMetadata(Colors.Black, OnTextColorChanged));

        public Color TextColor
        {
            get => (Color)GetValue(TextColorProperty);
            set => SetValue(TextColorProperty, value);
        }

        #endregion

        #region 事件

        public event EventHandler<ButtonClickEventArgs> ButtonClicked;

        #endregion

        public ButtonControl()
        {
            InitializeComponent();
        }

        private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ButtonControl control)
            {
                control.ButtonText.Text = e.NewValue as string ?? "按钮";
            }
        }

        private static void OnBackgroundColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ButtonControl control && e.NewValue is Color color)
            {
                control.MainButton.Background = new SolidColorBrush(color);
            }
        }

        private static void OnTextColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ButtonControl control && e.NewValue is Color color)
            {
                control.ButtonText.Foreground = new SolidColorBrush(color);
            }
        }

        private void MainButton_Click(object sender, RoutedEventArgs e)
        {
            // 触发按钮点击事件，传递绑定的变量和输出值
            ButtonClicked?.Invoke(this, new ButtonClickEventArgs
            {
                VariableName = BoundVariable,
                Value = OutputValue
            });
        }
    }

    /// <summary>
    /// 按钮点击事件参数
    /// </summary>
    public class ButtonClickEventArgs : EventArgs
    {
        public string VariableName { get; set; }
        public int Value { get; set; }
    }
}
