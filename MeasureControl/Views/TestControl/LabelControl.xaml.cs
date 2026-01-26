using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MeasureControl.Views.TestControl
{
    /// <summary>
    /// 标签控件 - 绑定模拟量，显示实时值和单位
    /// AI(模拟量输入)只读，AO(模拟量输出)可编辑
    /// </summary>
    public partial class LabelControl : UserControl
    {
        #region 依赖属性

        /// <summary>
        /// 显示的值
        /// </summary>
        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register("Value", typeof(double), typeof(LabelControl),
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
            DependencyProperty.Register("Unit", typeof(string), typeof(LabelControl),
                new PropertyMetadata("", OnUnitChanged));

        public string Unit
        {
            get => (string)GetValue(UnitProperty);
            set => SetValue(UnitProperty, value);
        }

        /// <summary>
        /// 绑定的变量名（模拟量）
        /// </summary>
        public static readonly DependencyProperty BoundVariableProperty =
            DependencyProperty.Register("BoundVariable", typeof(string), typeof(LabelControl),
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
            DependencyProperty.Register("DecimalPlaces", typeof(int), typeof(LabelControl),
                new PropertyMetadata(2, OnValueChanged));

        public int DecimalPlaces
        {
            get => (int)GetValue(DecimalPlacesProperty);
            set => SetValue(DecimalPlacesProperty, value);
        }

        /// <summary>
        /// 输入输出类型（AI=模拟量输入只读, AO=模拟量输出可编辑）
        /// </summary>
        public static readonly DependencyProperty InputOutputTypeProperty =
            DependencyProperty.Register("InputOutputType", typeof(string), typeof(LabelControl),
                new PropertyMetadata("AI"));

        public string InputOutputType
        {
            get => (string)GetValue(InputOutputTypeProperty);
            set => SetValue(InputOutputTypeProperty, value);
        }

        /// <summary>
        /// 是否可编辑（只有AO类型可编辑）
        /// </summary>
        public bool IsEditabel => InputOutputType == "AO";

        /// <summary>
        /// 值变化事件（AO编辑后触发）
        /// </summary>
        public event EventHandler<double> ValueChanged;

        #endregion

        public LabelControl()
        {
            InitializeComponent();
        }

        private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is LabelControl control)
            {
                control.UpdateValueDisplay();
            }
        }

        private static void OnUnitChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is LabelControl control)
            {
                control.UnitText.Text = e.NewValue as string ?? "";
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
        /// 点击控件 - 只有AO类型才进入编辑模式
        /// </summary>
        private void MainBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!IsEditabel) return;

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
