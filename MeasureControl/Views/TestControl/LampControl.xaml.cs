using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace MeasureControl.Views.TestControl
{
    /// <summary>
    /// 指示灯控件 - 只能绑定数字量，绿色表示通，红色表示灭
    /// </summary>
    public partial class LampControl : UserControl
    {
        // 颜色定义
        private static readonly Color OnColorLight = Color.FromRgb(0x4a, 0xde, 0x80);  // 亮绿色
        private static readonly Color OnColorDark = Color.FromRgb(0x22, 0xc5, 0x5e);   // 深绿色
        private static readonly Color OffColorLight = Color.FromRgb(0xff, 0x6b, 0x6b); // 亮红色
        private static readonly Color OffColorDark = Color.FromRgb(0xdc, 0x26, 0x26);  // 深红色

        #region 依赖属性

        /// <summary>
        /// 指示灯状态（true=亮/绿色，false=灭/红色）
        /// </summary>
        public static readonly DependencyProperty IsOnProperty =
            DependencyProperty.Register("IsOn", typeof(bool), typeof(LampControl),
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
            DependencyProperty.Register("BoundVariable", typeof(string), typeof(LampControl),
                new PropertyMetadata(null));

        public string BoundVariable
        {
            get => (string)GetValue(BoundVariableProperty);
            set => SetValue(BoundVariableProperty, value);
        }

        #endregion

        public LampControl()
        {
            InitializeComponent();
            UpdateLampColor(false); // 默认红色（灭）
        }

        private static void OnIsOnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is LampControl control)
            {
                control.UpdateLampColor((bool)e.NewValue);
            }
        }

        private void UpdateLampColor(bool isOn)
        {
            Color lightColor, darkColor;

            if (isOn)
            {
                lightColor = OnColorLight;
                darkColor = OnColorDark;
            }
            else
            {
                lightColor = OffColorLight;
                darkColor = OffColorDark;
            }

            // 更新渐变填充颜色
            LampColorLight.Color = lightColor;
            LampColorDark.Color = darkColor;

            // 更新发光效果颜色
            GlowEffect.Color = darkColor;
        }

        /// <summary>
        /// 设置指示灯状态（供外部调用）
        /// </summary>
        public void SetState(bool isOn)
        {
            IsOn = isOn;
        }

        /// <summary>
        /// 根据数字量值设置状态（0=灭，非0=亮）
        /// </summary>
        public void SetValue(int value)
        {
            IsOn = value != 0;
        }
    }
}
