using System;
using System.Windows;
using System.Windows.Controls;

namespace MeasureControl.Views.TestControl
{
    /// <summary>
    /// 开关控件 - 只能绑定数字量
    /// </summary>
    public partial class SwitchControl : UserControl
    {
        #region 依赖属性

        /// <summary>
        /// 开关状态
        /// </summary>
        public static readonly DependencyProperty IsOnProperty =
            DependencyProperty.Register("IsOn", typeof(bool), typeof(SwitchControl),
                new PropertyMetadata(false, OnIsOnChanged));

        public bool IsOn
        {
            get => (bool)GetValue(IsOnProperty);
            set => SetValue(IsOnProperty, value);
        }

        /// <summary>
        /// 绑定的变量名（数字量）
        /// </summary>
        public static readonly DependencyProperty BoundVariableProperty =
            DependencyProperty.Register("BoundVariable", typeof(string), typeof(SwitchControl),
                new PropertyMetadata(null));

        public string BoundVariable
        {
            get => (string)GetValue(BoundVariableProperty);
            set => SetValue(BoundVariableProperty, value);
        }

        #endregion

        #region 事件

        public event EventHandler<SwitchChangedEventArgs> SwitchChanged;

        #endregion

        #region 私有字段

        private bool _isSilentUpdate = false;  // 静默更新标志，不触发 SwitchChanged 事件

        #endregion

        public SwitchControl()
        {
            InitializeComponent();
        }

        private static void OnIsOnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SwitchControl control)
            {
                control.MainToggle.IsChecked = (bool)e.NewValue;
            }
        }

        private void MainToggle_Checked(object sender, RoutedEventArgs e)
        {
            IsOn = true;
            // 静默更新时不触发事件
            if (!_isSilentUpdate)
            {
                SwitchChanged?.Invoke(this, new SwitchChangedEventArgs
                {
                    VariableName = BoundVariable,
                    IsOn = true,
                    Value = 1
                });
            }
        }

        private void MainToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            IsOn = false;
            // 静默更新时不触发事件
            if (!_isSilentUpdate)
            {
                SwitchChanged?.Invoke(this, new SwitchChangedEventArgs
                {
                    VariableName = BoundVariable,
                    IsOn = false,
                    Value = 0
                });
            }
        }

        /// <summary>
        /// 设置开关状态（供外部调用，会触发事件）
        /// </summary>
        public void SetState(bool isOn)
        {
            IsOn = isOn;
        }

        /// <summary>
        /// 静默设置开关状态（供轮询更新使用，不触发 SwitchChanged 事件）
        /// </summary>
        public void SetValueSilent(bool isOn)
        {
            if (IsOn == isOn)
                return;  // 值相同则不更新
                
            _isSilentUpdate = true;
            IsOn = isOn;
            _isSilentUpdate = false;
        }
    }

    /// <summary>
    /// 开关状态变化事件参数
    /// </summary>
    public class SwitchChangedEventArgs : EventArgs
    {
        public string VariableName { get; set; }
        public bool IsOn { get; set; }
        public int Value { get; set; }
    }
}
