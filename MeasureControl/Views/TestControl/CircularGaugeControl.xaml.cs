using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace MeasureControl.Views.TestControl
{
    /// <summary>
    /// 环形仪表控件 - 半圆形仪表，显示模拟量值，通过指针旋转指示
    /// 仪表为半圆形（180度），包含颜色分段：绿色(0-20%)、蓝灰色(20-80%)、红色(80-100%)
    /// </summary>
    public partial class CircularGaugeControl : UserControl
    {
        #region 依赖属性

        /// <summary>
        /// 控件名称（显示在上方）
        /// </summary>
        public static readonly DependencyProperty ControlNameProperty =
            DependencyProperty.Register("ControlName", typeof(string), typeof(CircularGaugeControl),
                new PropertyMetadata("环形仪表", OnControlNameChanged));

        public string ControlName
        {
            get => (string)GetValue(ControlNameProperty);
            set => SetValue(ControlNameProperty, value);
        }

        /// <summary>
        /// 显示的值
        /// </summary>
        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register("Value", typeof(double), typeof(CircularGaugeControl),
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
            DependencyProperty.Register("MaxValue", typeof(double), typeof(CircularGaugeControl),
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
            DependencyProperty.Register("Unit", typeof(string), typeof(CircularGaugeControl),
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
            DependencyProperty.Register("BoundVariable", typeof(string), typeof(CircularGaugeControl),
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
            DependencyProperty.Register("DecimalPlaces", typeof(int), typeof(CircularGaugeControl),
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
            DependencyProperty.Register("ManualValue", typeof(double?), typeof(CircularGaugeControl),
                new PropertyMetadata(null, OnManualValueChanged));

        public double? ManualValue
        {
            get => (double?)GetValue(ManualValueProperty);
            set => SetValue(ManualValueProperty, value);
        }

        #endregion

        // 半圆形仪表角度范围：180度，从180度（9点钟，左侧）到360度（3点钟，右侧），开口向下
        // 标准坐标系（WPF）：0度在3点钟方向，顺时针为正，Y轴向下
        // 9点钟方向 = 180度，12点钟方向 = 270度，3点钟方向 = 0度/360度
        // 指针起点在270度（12点钟，顶部中心），旋转范围从180度到360度
        private const double StartAngle = 180.0;   // 0值位置（9点钟，左侧，180度）
        private const double EndAngle = 360.0;      // 最大值位置（3点钟，右侧，360度）
        private const double TotalAngle = 180.0;    // 总角度范围（从180度顺时针到360度，共180度）
        private const double NeedleStartAngle = 270.0; // 指针起点角度（12点钟，顶部中心）
        
        // 颜色分段：绿色(0-20%)、蓝灰色(20-80%)、红色(80-100%)
        private const double GreenEndRatio = 0.2;   // 绿色段结束比例（20%）
        private const double RedStartRatio = 0.8;    // 红色段开始比例（80%）
        
        // 刻度线和标签集合
        private List<Line> _tickLines = new List<Line>();
        private List<TextBlock> _tickLabels = new List<TextBlock>();
        private List<Path> _colorSegments = new List<Path>();

        public CircularGaugeControl()
        {
            InitializeComponent();
            this.Loaded += CircularGaugeControl_Loaded;
        }

        private void CircularGaugeControl_Loaded(object sender, RoutedEventArgs e)
        {
            // 控件加载完成后初始化
            UpdateColorSegments();
            UpdateTicks();
            UpdateNeedleAngle();
            UpdateValueDisplay();
        }

        private static void OnControlNameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is CircularGaugeControl control && control.ControlNameText != null)
            {
                control.ControlNameText.Text = e.NewValue as string ?? "环形仪表";
            }
        }

        private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is CircularGaugeControl control)
            {
                control.UpdateNeedleAngle();
                control.UpdateValueDisplay();
            }
        }

        private static void OnMaxValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is CircularGaugeControl control)
            {
                control.UpdateColorSegments();
                control.UpdateTicks();
                control.UpdateNeedleAngle();
            }
        }

        private static void OnUnitChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is CircularGaugeControl control && control.UnitText != null)
            {
                control.UnitText.Text = e.NewValue as string ?? "";
            }
        }

        private static void OnManualValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is CircularGaugeControl control)
            {
                // 如果设置了手动值，使用手动值；否则使用Value
                if (control.ManualValue.HasValue)
                {
                    control.Value = control.ManualValue.Value;
                }
            }
        }

        /// <summary>
        /// 更新颜色分段弧形
        /// </summary>
        private void UpdateColorSegments()
        {
            // 清除旧的颜色分段
            foreach (var segment in _colorSegments)
            {
                if (GaugeCanvas != null)
                {
                    GaugeCanvas.Children.Remove(segment);
                }
            }
            _colorSegments.Clear();

            if (MaxValue <= 0 || GaugeCanvas == null) return;

            // 半圆开口向下，中心点在下方
            double centerX = 100;
            double centerY = 120; // 中心点位置
            double radius = 80;

            // 绿色段：0-20%（左侧，9点钟到10点钟）
            CreateColorSegment(centerX, centerY, radius, 0.0, GreenEndRatio, Color.FromRgb(0x86, 0xef, 0xac)); // 浅绿色

            // 蓝灰色段：20-80%（中间，10点钟到2点钟）
            CreateColorSegment(centerX, centerY, radius, GreenEndRatio, RedStartRatio, Color.FromRgb(0x6b, 0x72, 0x80)); // 蓝灰色

            // 红色段：80-100%（右侧，2点钟到3点钟）
            CreateColorSegment(centerX, centerY, radius, RedStartRatio, 1.0, Color.FromRgb(0xef, 0x44, 0x44)); // 红色
        }

        /// <summary>
        /// 创建颜色分段弧形
        /// </summary>
        private void CreateColorSegment(double centerX, double centerY, double radius, double startRatio, double endRatio, Color color)
        {
            // 计算起始和结束角度（从180度顺时针到360度，开口向下）
            // 0值在9点钟（180度），最大值在3点钟（360度）
            double startAngle = StartAngle + (TotalAngle * startRatio);
            double endAngle = StartAngle + (TotalAngle * endRatio);

            // 转换为弧度
            double startAngleRad = startAngle * Math.PI / 180.0;
            double endAngleRad = endAngle * Math.PI / 180.0;

            // 计算起始和结束点
            double startX = centerX + radius * Math.Cos(startAngleRad);
            double startY = centerY + radius * Math.Sin(startAngleRad);
            double endX = centerX + radius * Math.Cos(endAngleRad);
            double endY = centerY + radius * Math.Sin(endAngleRad);

            // 创建弧形路径
            var path = new Path
            {
                Stroke = new SolidColorBrush(color),
                StrokeThickness = 12,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            };

            var geometry = new PathGeometry();
            var figure = new PathFigure
            {
                StartPoint = new Point(startX, startY)
            };

            var arcSegment = new ArcSegment
            {
                Point = new Point(endX, endY),
                Size = new Size(radius, radius),
                SweepDirection = SweepDirection.Clockwise,
                IsLargeArc = false
            };

            figure.Segments.Add(arcSegment);
            geometry.Figures.Add(figure);
            path.Data = geometry;

            // 插入到背景弧形之后
            int insertIndex = GaugeCanvas.Children.IndexOf(GaugeBackgroundArc) + 1;
            GaugeCanvas.Children.Insert(insertIndex, path);
            _colorSegments.Add(path);
        }

        /// <summary>
        /// 更新刻度线和标签
        /// </summary>
        private void UpdateTicks()
        {
            // 清除旧刻度线和标签
            if (GaugeCanvas != null)
            {
                foreach (var tick in _tickLines)
                {
                    GaugeCanvas.Children.Remove(tick);
                }
                foreach (var label in _tickLabels)
                {
                    GaugeCanvas.Children.Remove(label);
                }
            }
            _tickLines.Clear();
            _tickLabels.Clear();

            if (MaxValue <= 0 || GaugeCanvas == null) return;

            // 半圆开口向下，中心点在下方
            double centerX = 100;
            double centerY = 120; // 中心点位置
            double radius = 80;
            double tickLength = 8; // 刻度线长度
            double labelRadius = radius + tickLength + 12; // 标签位置半径（更靠近仪表）

            // 生成11个刻度（0到10，共11个）：0, MaxValue/10, 2*MaxValue/10, ..., MaxValue
            for (int i = 0; i < 11; i++)
            {
                // 计算刻度值比例（0到1）
                double ratio = i / 10.0;

                // 计算角度（从StartAngle到EndAngle，顺时针）
                // 从180度（9点钟，0值）顺时针到360度（3点钟，最大值），开口向下
                double angle = StartAngle + (TotalAngle * ratio);
                // 规范化角度到0-360度范围
                if (angle >= 360) angle -= 360;
                if (angle < 0) angle += 360;

                // 转换为弧度
                double angleRad = angle * Math.PI / 180.0;

                // 计算刻度线起点（在弧上）
                double tickStartX = centerX + radius * Math.Cos(angleRad);
                double tickStartY = centerY + radius * Math.Sin(angleRad);

                // 计算刻度线终点（向外延伸）
                double tickEndX = centerX + (radius + tickLength) * Math.Cos(angleRad);
                double tickEndY = centerY + (radius + tickLength) * Math.Sin(angleRad);

                // 创建刻度线
                var tickLine = new Line
                {
                    X1 = tickStartX,
                    Y1 = tickStartY,
                    X2 = tickEndX,
                    Y2 = tickEndY,
                    Stroke = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)),
                    StrokeThickness = 2,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round
                };

                // 插入到指针之前
                int insertIndex = GaugeCanvas.Children.IndexOf(Needle);
                if (insertIndex < 0) insertIndex = GaugeCanvas.Children.Count;
                GaugeCanvas.Children.Insert(insertIndex, tickLine);
                _tickLines.Add(tickLine);

                // 创建刻度标签（显示整数，如0, 10, 20, ..., 100）
                double tickValue = MaxValue * ratio;
                var label = new TextBlock
                {
                    Text = tickValue.ToString("F0"), // 显示整数
                    FontSize = 12, // 字体增大2点
                    Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)),
                    TextAlignment = TextAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };

                // 根据比例确定标签颜色（80-100%为红色）
                if (ratio >= RedStartRatio)
                {
                    label.Foreground = new SolidColorBrush(Color.FromRgb(0xef, 0x44, 0x44)); // 红色
                }

                // 计算标签位置（均匀分布在仪表外围）
                double labelX = centerX + labelRadius * Math.Cos(angleRad);
                double labelY = centerY + labelRadius * Math.Sin(angleRad);

                Canvas.SetLeft(label, labelX - 12); // 居中调整
                Canvas.SetTop(label, labelY - 8); // 居中调整
                GaugeCanvas.Children.Insert(insertIndex, label);
                _tickLabels.Add(label);
            }
        }

        /// <summary>
        /// 更新指针角度
        /// </summary>
        private void UpdateNeedleAngle()
        {
            if (MaxValue <= 0 || NeedleRotate == null || Needle == null)
            {
                // 最大值无效，指针指向最小值位置（StartAngle，即0值位置）
                if (NeedleRotate != null)
                    NeedleRotate.Angle = StartAngle - NeedleStartAngle;
                return;
            }

            // 计算当前值在0到MaxValue之间的比例
            double ratio = Math.Max(0, Math.Min(1, Value / MaxValue));

            // 计算指针应该指向的角度（从StartAngle到EndAngle，顺时针）
            // 从180度（9点钟，0值）顺时针到360度（3点钟，最大值），开口向下
            double targetAngle = StartAngle + (TotalAngle * ratio);
            // 规范化角度到0-360度范围
            if (targetAngle >= 360) targetAngle -= 360;
            if (targetAngle < 0) targetAngle += 360;

            // 指针起点在NeedleStartAngle（270度，12点钟方向），需要旋转到targetAngle
            // 旋转角度 = targetAngle - NeedleStartAngle，但需要考虑跨越0度的情况
            double rotationAngle = targetAngle - NeedleStartAngle;
            if (rotationAngle < 0) rotationAngle += 360;
            if (rotationAngle >= 360) rotationAngle -= 360;

            // 更新指针旋转角度
            NeedleRotate.Angle = rotationAngle;
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
    }
}
