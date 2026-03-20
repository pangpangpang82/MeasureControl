using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace MeasureControl.Views.TestControl
{
    /// <summary>
    /// 竖形仪表控件 - 显示液位等液体高度数据
    /// 液位从底部向上填充，根据绑定数据大小和最大值进行显示
    /// </summary>
    public partial class VerticalGaugeControl : UserControl
    {
        #region 依赖属性

        /// <summary>
        /// 控件名称（显示在上方）
        /// </summary>
        public static readonly DependencyProperty ControlNameProperty =
            DependencyProperty.Register("ControlName", typeof(string), typeof(VerticalGaugeControl),
                new PropertyMetadata("竖形仪表", OnControlNameChanged));

        public string ControlName
        {
            get => (string)GetValue(ControlNameProperty);
            set => SetValue(ControlNameProperty, value);
        }

        /// <summary>
        /// 显示的值
        /// </summary>
        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register("Value", typeof(double), typeof(VerticalGaugeControl),
                new PropertyMetadata(0.0, OnValueChanged));

        public double Value
        {
            get => (double)GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        /// <summary>
        /// 最大值
        /// </summary>
        public static readonly DependencyProperty MaxValueProperty =
            DependencyProperty.Register("MaxValue", typeof(double), typeof(VerticalGaugeControl),
                new PropertyMetadata(100.0, OnMaxValueChanged));

        public double MaxValue
        {
            get => (double)GetValue(MaxValueProperty);
            set => SetValue(MaxValueProperty, value);
        }

        /// <summary>
        /// 单位
        /// </summary>
        public static readonly DependencyProperty UnitProperty =
            DependencyProperty.Register("Unit", typeof(string), typeof(VerticalGaugeControl),
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
            DependencyProperty.Register("BoundVariable", typeof(string), typeof(VerticalGaugeControl),
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
            DependencyProperty.Register("DecimalPlaces", typeof(int), typeof(VerticalGaugeControl),
                new PropertyMetadata(2, OnValueChanged));

        public int DecimalPlaces
        {
            get => (int)GetValue(DecimalPlacesProperty);
            set => SetValue(DecimalPlacesProperty, value);
        }

        /// <summary>
        /// 手动设置的值（优先级高于绑定变量）
        /// </summary>
        public static readonly DependencyProperty ManualValueProperty =
            DependencyProperty.Register("ManualValue", typeof(double?), typeof(VerticalGaugeControl),
                new PropertyMetadata(null, OnManualValueChanged));

        public double? ManualValue
        {
            get => (double?)GetValue(ManualValueProperty);
            set => SetValue(ManualValueProperty, value);
        }

        #endregion

        // 刻度线集合（11个刻度）
        private List<Line> _tickLines = new List<Line>();
        private List<TextBlock> _tickLabels = new List<TextBlock>();

        public VerticalGaugeControl()
        {
            InitializeComponent();
            // 延迟到控件加载完成后再更新
            this.Loaded += VerticalGaugeControl_Loaded;
        }

        private void VerticalGaugeControl_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateTicks();
            UpdateLiquidLevel();
            UpdateValueDisplay();
        }

        private static void OnControlNameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is VerticalGaugeControl control && control.ControlNameText != null)
            {
                control.ControlNameText.Text = e.NewValue as string ?? "竖形仪表";
            }
        }

        private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is VerticalGaugeControl control)
            {
                control.UpdateLiquidLevel();
                control.UpdateValueDisplay();
            }
        }

        private static void OnMaxValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is VerticalGaugeControl control)
            {
                control.UpdateTicks();
                control.UpdateLiquidLevel();
            }
        }

        private static void OnUnitChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is VerticalGaugeControl control && control.UnitText != null)
            {
                control.UnitText.Text = e.NewValue as string ?? "";
            }
        }

        private static void OnManualValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is VerticalGaugeControl control)
            {
                // 如果设置了手动值，使用手动值；否则使用Value
                if (control.ManualValue.HasValue)
                {
                    control.Value = control.ManualValue.Value;
                }
            }
        }

        /// <summary>
        /// 更新刻度线（11个等分刻度：0, MaxValue/10, 2*MaxValue/10, ..., MaxValue）
        /// </summary>
        private void UpdateTicks()
        {
            // 清除旧刻度线和标签
            if (TickCanvas != null)
            {
                foreach (var tick in _tickLines)
                {
                    TickCanvas.Children.Remove(tick);
                }
            }
            _tickLines.Clear();

            if (LabelsCanvas != null)
            {
                LabelsCanvas.Children.Clear();
            }
            _tickLabels.Clear();

            if (MaxValue <= 0 || GaugeBody == null || TickCanvas == null || LabelsCanvas == null) return;

            double gaugeHeight = GaugeBody.ActualHeight > 0 ? GaugeBody.ActualHeight : 150;
            double tickLength = 6; // 刻度线长度
            double gaugeWidth = GaugeBody.ActualWidth > 0 ? GaugeBody.ActualWidth : 30;

            // 生成11个刻度（0到10，共11个）：0, MaxValue/10, 2*MaxValue/10, ..., MaxValue
            for (int i = 0; i < 11; i++)
            {
                // 计算刻度值比例（0到1）
                double ratio = i / 10.0;

                // 计算Y位置（从底部到顶部）
                // ratio=0时：yPos = gaugeHeight（底部，0值）
                // ratio=1时：yPos = 0（顶部，最大值）
                double yPos = gaugeHeight - (gaugeHeight * ratio);

                // 创建刻度线（在仪表内部左侧）
                var tickLine = new Line
                {
                    X1 = 0,
                    Y1 = 0,
                    X2 = tickLength,
                    Y2 = 0,
                    Stroke = new SolidColorBrush(Color.FromRgb(0xff, 0xff, 0xff)),
                    StrokeThickness = 1.5,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round
                };

                Canvas.SetLeft(tickLine, 0);
                Canvas.SetTop(tickLine, yPos);
                TickCanvas.Children.Add(tickLine);
                _tickLines.Add(tickLine);

                // 创建刻度标签（在仪表左侧，均匀分布，不遮挡仪表）
                double tickValue = MaxValue * ratio;
                var label = new TextBlock
                {
                    Text = tickValue.ToString("F0"), // 显示整数
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)),
                    TextAlignment = TextAlignment.Right,
                    Width = 30
                };

                // 标签位置（右对齐，与刻度线对齐）
                Canvas.SetLeft(label, 0);
                Canvas.SetTop(label, yPos - 7); // 居中对齐
                LabelsCanvas.Children.Add(label);
                _tickLabels.Add(label);
            }
        }

        /// <summary>
        /// 更新液位高度
        /// </summary>
        private void UpdateLiquidLevel()
        {
            if (LiquidFill == null || GaugeBody == null) return;

            if (MaxValue <= 0)
            {
                LiquidFill.Height = 0;
                return;
            }

            // 计算当前值在0到MaxValue之间的比例
            double ratio = Math.Max(0, Math.Min(1, Value / MaxValue));

            // 计算液位高度
            double gaugeHeight = GaugeBody.ActualHeight > 0 ? GaugeBody.ActualHeight : 150;
            double liquidHeight = gaugeHeight * ratio;

            // 更新液位填充高度
            LiquidFill.Height = liquidHeight;
        }

        /// <summary>
        /// 更新数值显示
        /// </summary>
        private void UpdateValueDisplay()
        {
            if (ValueText == null) return;
            string format = $"F{DecimalPlaces}";
            ValueText.Text = Value.ToString(format);
        }

        /// <summary>
        /// 更新实时值（供外部调用）
        /// </summary>
        public void UpdateValue(double newValue)
        {
            // 只有在没有手动设置值时才更新
            if (!ManualValue.HasValue)
            {
                Value = newValue;
            }
        }

        /// <summary>
        /// 控件大小变化时更新刻度
        /// </summary>
        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            if (sizeInfo.HeightChanged && IsLoaded)
            {
                UpdateTicks();
                UpdateLiquidLevel();
            }
        }
    }
}
