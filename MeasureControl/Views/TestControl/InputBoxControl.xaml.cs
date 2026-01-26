using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MeasureControl.Views.TestControl
{
    /// <summary>
    /// 输入框控件 - 绑定模拟量输出(AO)，可编辑设置值
    /// </summary>
    public partial class InputBoxControl : UserControl
    {
        #region 依赖属性

        /// <summary>
        /// 控件名称（显示在上方）
        /// </summary>
        public static readonly DependencyProperty ControlNameProperty =
            DependencyProperty.Register("ControlName", typeof(string), typeof(InputBoxControl),
                new PropertyMetadata("输入框1", OnControlNameChanged));

        public string ControlName
        {
            get => (string)GetValue(ControlNameProperty);
            set => SetValue(ControlNameProperty, value);
        }

        /// <summary>
        /// 显示/设置的值
        /// </summary>
        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register("Value", typeof(double), typeof(InputBoxControl),
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
            DependencyProperty.Register("Unit", typeof(string), typeof(InputBoxControl),
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
            DependencyProperty.Register("BoundVariable", typeof(string), typeof(InputBoxControl),
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
            DependencyProperty.Register("DecimalPlaces", typeof(int), typeof(InputBoxControl),
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
            DependencyProperty.Register("BackgroundColor", typeof(Color), typeof(InputBoxControl),
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
            DependencyProperty.Register("TextColor", typeof(Color), typeof(InputBoxControl),
                new PropertyMetadata(Colors.Black, OnTextColorChanged));

        public Color TextColor
        {
            get => (Color)GetValue(TextColorProperty);
            set => SetValue(TextColorProperty, value);
        }

        /// <summary>
        /// 值变化事件（编辑后触发）
        /// </summary>
        public event EventHandler<double> ValueChanged;

        #endregion

        public InputBoxControl()
        {
            InitializeComponent();
        }

        private static void OnControlNameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is InputBoxControl control)
            {
                control.ControlNameText.Text = e.NewValue as string ?? "输入框";
            }
        }

        private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is InputBoxControl control)
            {
                control.UpdateValueDisplay();
            }
        }

        private static void OnUnitChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is InputBoxControl control)
            {
                control.UnitText.Text = e.NewValue as string ?? "";
            }
        }

        private static void OnBackgroundColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is InputBoxControl control && e.NewValue is Color color)
            {
                control.MainBorder.Background = new SolidColorBrush(color);
            }
        }

        private static void OnTextColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is InputBoxControl control && e.NewValue is Color color)
            {
                var brush = new SolidColorBrush(color);
                control.ValueText.Foreground = brush;
                control.UnitText.Foreground = brush;
                control.EditTextBox.Foreground = brush;
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

        /// <summary>
        /// 点击控件 - 进入编辑模式
        /// </summary>
        private void MainBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 进入编辑模式
            DisplayPanel.Visibility = Visibility.Collapsed;
            EditTextBox.Visibility = Visibility.Visible;
            EditTextBox.Text = Value.ToString($"F{DecimalPlaces}");
            EditTextBox.SelectAll();
            EditTextBox.Focus();
            e.Handled = true;
        }

        /// <summary>
        /// 编辑框失去焦点 - 提交修改
        /// </summary>
        private void EditTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            CommitEdit();
        }

        /// <summary>
        /// 编辑框按键 - Enter确认，Escape取消
        /// </summary>
        private void EditTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                CommitEdit();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                CancelEdit();
                e.Handled = true;
            }
        }

        /// <summary>
        /// 提交编辑
        /// </summary>
        private void CommitEdit()
        {
            if (EditTextBox.Visibility != Visibility.Visible) return;

            if (double.TryParse(EditTextBox.Text, out double newValue))
            {
                Value = newValue;
                ValueChanged?.Invoke(this, newValue);
            }

            // 退出编辑模式
            EditTextBox.Visibility = Visibility.Collapsed;
            DisplayPanel.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// 取消编辑
        /// </summary>
        private void CancelEdit()
        {
            EditTextBox.Visibility = Visibility.Collapsed;
            DisplayPanel.Visibility = Visibility.Visible;
        }
    }
}
