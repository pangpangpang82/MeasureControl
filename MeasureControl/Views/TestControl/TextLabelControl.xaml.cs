using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MeasureControl.Views.TestControl
{
    /// <summary>
    /// 标签控件 - 纯文本显示，不绑定任何数据源
    /// </summary>
    public partial class TextLabelControl : UserControl
    {
        #region 依赖属性

        /// <summary>
        /// 标签文字
        /// </summary>
        public static readonly DependencyProperty TextProperty =
            DependencyProperty.Register("Text", typeof(string), typeof(TextLabelControl),
                new PropertyMetadata("标签", OnTextChanged));

        public string Text
        {
            get => (string)GetValue(TextProperty);
            set => SetValue(TextProperty, value);
        }

        /// <summary>
        /// 文字颜色
        /// </summary>
        public static readonly DependencyProperty TextColorProperty =
            DependencyProperty.Register("TextColor", typeof(Color), typeof(TextLabelControl),
                new PropertyMetadata(Colors.Black, OnTextColorChanged));

        public Color TextColor
        {
            get => (Color)GetValue(TextColorProperty);
            set => SetValue(TextColorProperty, value);
        }

        #endregion

        public TextLabelControl()
        {
            InitializeComponent();
        }

        private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TextLabelControl control)
            {
                control.LabelText.Text = e.NewValue as string ?? "标签";
            }
        }

        private static void OnTextColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TextLabelControl control && e.NewValue is Color color)
            {
                control.LabelText.Foreground = new SolidColorBrush(color);
            }
        }

        /// <summary>
        /// 设置标签文字
        /// </summary>
        public void SetText(string text)
        {
            Text = text;
        }
    }
}
