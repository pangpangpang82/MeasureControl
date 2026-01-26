using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using MeasureControl.ViewModels;
using MeasureControl.Models; // OutputWaveformType
using Prism.Regions;
using MeasureControl.ViewModels.TestTask.CardCATPanel;

namespace MeasureControl.Views.TestTask.CardCATPanel
{
    /// <summary>
    /// AnalogOutputConfigPanel.xaml 的交互逻辑
    /// </summary>
    public partial class MT532_AO : UserControl, IRegionMemberLifetime
    {
        private string _originalCardName;

        private DispatcherOperation _pendingRedraw;

        public MT532_AO()
        {
            InitializeComponent();

            Loaded += (s, e) =>
            {
                RequestRedrawWaveforms();
            };

            if (WaveformCanvas != null)
            {
                WaveformCanvas.SizeChanged += WaveformCanvas_SizeChanged;
            }

            CardNameTextBox.GotFocus += (s, e) =>
            {
                if (DataContext is MT532_AOViewModel vm)
                {
                    _originalCardName = vm.CardName;
                }
            };

            DataContextChanged += AnalogOutputConfigPanel_DataContextChanged;
        }

        public bool KeepAlive
        {
            get
            {
                if (DataContext is MT532_AOViewModel vm)
                {
                    return vm.IsBusy || vm.IsDeviceConnected || vm.IsOutputRunning;
                }

                return true;
            }
        }

        private void WaveformCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            RequestRedrawWaveforms();
        }

        private void RootBorder_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is DependencyObject dep)
            {
                if (FindAncestor<TextBoxBase>(dep) != null || FindAncestor<ComboBox>(dep) != null || FindAncestor<ComboBoxItem>(dep) != null)
                {
                    return;
                }
            }

            Keyboard.ClearFocus();
            RootBorder.Focus();
        }

        private static T FindAncestor<T>(DependencyObject current) where T : DependencyObject
        {
            while (current != null)
            {
                if (current is T matched)
                    return matched;
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        private void CardNameTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (DataContext is MT532_AOViewModel vm)
            {
                var newName = CardNameTextBox.Text?.Trim();
                if (!string.IsNullOrEmpty(newName) && newName != _originalCardName)
                {
                    vm.OnCardNameChanged(_originalCardName);
                }
            }
        }

        private void AnalogOutputConfigPanel_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is MT532_AOViewModel oldVm)
            {
                UnsubscribeFromViewModel(oldVm);
            }

            if (e.NewValue is MT532_AOViewModel newVm)
            {
                SubscribeToViewModel(newVm);
                //RedrawWaveforms();
                RequestRedrawWaveforms();
            }
        }

        private void SubscribeToViewModel(MT532_AOViewModel vm)
        {
            if (vm?.OutputChannelConfigs is INotifyCollectionChanged ncc)
            {
                ncc.CollectionChanged += OutputChannelConfigs_CollectionChanged;
            }

            foreach (var ch in vm.OutputChannelConfigs)
            {
                ch.PropertyChanged += ChannelConfig_PropertyChanged;
            }

            RequestRedrawWaveforms();
        }

        private void UnsubscribeFromViewModel(MT532_AOViewModel vm)
        {
            if (vm?.OutputChannelConfigs is INotifyCollectionChanged ncc)
            {
                ncc.CollectionChanged -= OutputChannelConfigs_CollectionChanged;
            }

            foreach (var ch in vm?.OutputChannelConfigs ?? Enumerable.Empty<AnalogOutputChannelConfigViewModel>())
            {
                ch.PropertyChanged -= ChannelConfig_PropertyChanged;
            }
        }

        private void OutputChannelConfigs_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (AnalogOutputChannelConfigViewModel vm in e.OldItems)
                {
                    vm.PropertyChanged -= ChannelConfig_PropertyChanged;
                }
            }

            if (e.NewItems != null)
            {
                foreach (AnalogOutputChannelConfigViewModel vm in e.NewItems)
                {
                    vm.PropertyChanged += ChannelConfig_PropertyChanged;
                }
            }

            //RedrawWaveforms();
            RequestRedrawWaveforms();
        }

        private void ChannelConfig_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AnalogOutputChannelConfigViewModel.IsPreviewEnabled) ||
                e.PropertyName == nameof(AnalogOutputChannelConfigViewModel.WaveformType) ||
                e.PropertyName == nameof(AnalogOutputChannelConfigViewModel.AmplitudeText) ||
                e.PropertyName == nameof(AnalogOutputChannelConfigViewModel.FrequencyText) ||
                e.PropertyName == nameof(AnalogOutputChannelConfigViewModel.OffsetText) ||
                e.PropertyName == nameof(AnalogOutputChannelConfigViewModel.DutyCycleText) ||
                e.PropertyName == nameof(AnalogOutputChannelConfigViewModel.PreviewColorHex))
            {
                //RedrawWaveforms();
                RequestRedrawWaveforms();
            }
        }

        private void RedrawWaveforms()
        {
            RedrawWaveformsInternal();
            return;
            /*
            if (!(DataContext is MT532_AOViewModel vm))
                return;

            if (WaveformCanvas == null || LegendPanel == null)
                return;

            WaveformCanvas.Children.Clear();
            LegendPanel.Children.Clear();

            var previewChannels = vm.OutputChannelConfigs
                .Where(c => c.IsPreviewEnabled)
                .ToList();

            if (previewChannels.Count == 0)
                return;

            double width = WaveformCanvas.ActualWidth;
            double height = WaveformCanvas.ActualHeight;

            if (width <= 0 || height <= 0)
            {
                // 还没布局完成时，延迟一次
                WaveformCanvas.Loaded -= WaveformCanvas_Loaded;
                WaveformCanvas.Loaded += WaveformCanvas_Loaded;
                return;
            }

            // 以“周期最长”的通道为基准，X 轴覆盖其一个周期时间
            // 周期 = 1 / f，最长周期 = 1 / minFreq
            double minFreq = previewChannels
                .Select(ch => TryParseDouble(ch.FrequencyText, 0))
                .Where(f => f > 0)
                .DefaultIfEmpty(0)
                .Min();

            double timeWindow;
            if (minFreq > 0)
            {
                timeWindow = 1.0 / minFreq; // 秒
            }
            else
            {
                // 所有通道频率都无效时，用任意 1 秒窗口，只会画成直流线
                timeWindow = 1.0;
            }

            int sampleCount = 200;

            foreach (var ch in previewChannels)
            {
                var brush = ParseBrushFromHex(ch.PreviewColorHex) ?? Brushes.SteelBlue;

                var polyline = new Polyline
                {
                    Stroke = brush,
                    StrokeThickness = 1.5
                };

                for (int i = 0; i <= sampleCount; i++)
                {
                    double t = (double)i / sampleCount * timeWindow; // 当前绝对时间（秒）
                    double x = (double)i / sampleCount * width;

                    double v = CalculatePreviewValue(ch, t);
                    if (v > 10) v = 10;
                    if (v < -10) v = -10;

                    // 电压 10V 映射到顶部，-10V 到底部
                    double y = (10 - (v + 10) / 2) / 10 * height; // 简单线性映射

                    polyline.Points.Add(new Point(x, y));
                }

                WaveformCanvas.Children.Add(polyline);

                // 图例
                var legendItem = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(0, 2, 0, 2)
                };

                var rect = new Rectangle
                {
                    Width = 14,
                    Height = 14,
                    Fill = brush,
                    Stroke = Brushes.Gray,
                    StrokeThickness = 0.5,
                    Margin = new Thickness(0, 0, 6, 0)
                };

                string waveName;
                switch (ch.WaveformType)
                {
                    case OutputWaveformType.Sine:
                        waveName = "正弦";
                        break;
                    case OutputWaveformType.Square:
                        waveName = "方波";
                        break;
                    case OutputWaveformType.Dc:
                        waveName = "直流";
                        break;
                    default:
                        waveName = ch.WaveformType.ToString();
                        break;
                }

                var text = new TextBlock
                {
                    Text = $"{ch.ChannelName} - {waveName}",
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 12
                };

                legendItem.Children.Add(rect);
                legendItem.Children.Add(text);
                LegendPanel.Children.Add(legendItem);
            }

            // 画大致坐标轴和电压范围标记
            DrawAxis(height, width);
            */
        }

        private void RequestRedrawWaveforms()
        {
            if (!IsLoaded)
            {
                return;
            }

            if (_pendingRedraw != null && _pendingRedraw.Status == DispatcherOperationStatus.Pending)
            {
                return;
            }

            _pendingRedraw = Dispatcher.InvokeAsync(RedrawWaveformsInternal, DispatcherPriority.Background);
        }

        private void RedrawWaveformsInternal()
        {
            if (!(DataContext is MT532_AOViewModel vm))
                return;

            if (WaveformCanvas == null || LegendPanel == null)
                return;

            double width = WaveformCanvas.ActualWidth;
            double height = WaveformCanvas.ActualHeight;
            if (width <= 0 || height <= 0)
            {
                WaveformCanvas.Loaded -= WaveformCanvas_Loaded;
                WaveformCanvas.Loaded += WaveformCanvas_Loaded;
                return;
            }

            WaveformCanvas.Children.Clear();
            LegendPanel.Children.Clear();

            var previewChannels = vm.OutputChannelConfigs
                .Where(c => c != null && c.IsPreviewEnabled)
                .ToList();

            if (previewChannels.Count == 0)
                return;

            double minFreq = previewChannels
                .Select(ch => TryParseDouble(ch.FrequencyText, 0))
                .Where(f => f > 0)
                .DefaultIfEmpty(0)
                .Min();

            double timeWindow = minFreq > 0 ? 1.0 / minFreq : 1.0;
            int sampleCount = 200;

            foreach (var ch in previewChannels)
            {
                var brush = ParseBrushFromHex(ch.PreviewColorHex) ?? Brushes.SteelBlue;

                var polyline = new Polyline
                {
                    Stroke = brush,
                    StrokeThickness = 1.5
                };

                for (int i = 0; i <= sampleCount; i++)
                {
                    double t = (double)i / sampleCount * timeWindow;
                    double x = (double)i / sampleCount * width;

                    double v = CalculatePreviewValue(ch, t);
                    if (v > 10) v = 10;
                    if (v < -10) v = -10;

                    double y = ((10 - v) / 20.0) * height;
                    polyline.Points.Add(new Point(x, y));
                }

                WaveformCanvas.Children.Add(polyline);

                var legendItem = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(0, 2, 0, 2)
                };

                var rect = new Rectangle
                {
                    Width = 14,
                    Height = 14,
                    Fill = brush,
                    Stroke = Brushes.Gray,
                    StrokeThickness = 0.5,
                    Margin = new Thickness(0, 0, 6, 0)
                };

                string waveName;
                switch (ch.WaveformType)
                {
                    case OutputWaveformType.Sine:
                        waveName = "正弦";
                        break;
                    case OutputWaveformType.Square:
                        waveName = "方波";
                        break;
                    case OutputWaveformType.Dc:
                        waveName = "直流";
                        break;
                    default:
                        waveName = ch.WaveformType.ToString();
                        break;
                }

                var text = new TextBlock
                {
                    Text = $"{ch.ChannelName} - {waveName}",
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 12
                };

                legendItem.Children.Add(rect);
                legendItem.Children.Add(text);
                LegendPanel.Children.Add(legendItem);
            }

            DrawAxis(height, width);
        }

        private void WaveformCanvas_Loaded(object sender, RoutedEventArgs e)
        {
            WaveformCanvas.Loaded -= WaveformCanvas_Loaded;
            //RedrawWaveforms();
            RequestRedrawWaveforms();
        }

        private static Brush ParseBrushFromHex(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex))
                return null;

            try
            {
                return (SolidColorBrush)new BrushConverter().ConvertFromString(hex);
            }
            catch
            {
                return null;
            }
        }

        private static double CalculatePreviewValue(AnalogOutputChannelConfigViewModel ch, double timeSeconds)
        {
            double amp = TryParseDouble(ch.AmplitudeText, 0);
            double offset = TryParseDouble(ch.OffsetText, 0);
            double duty = TryParseDouble(ch.DutyCycleText, 50);
            double freq = TryParseDouble(ch.FrequencyText, 0);

            if (freq <= 0)
            {
                return offset;
            }

            switch (ch.WaveformType)
            {
                case OutputWaveformType.Sine:
                    return offset + amp * Math.Sin(2 * Math.PI * freq * timeSeconds);

                case OutputWaveformType.Square:
                    double dutyFrac = Math.Max(0.01, Math.Min(0.99, duty / 100.0));

                    // 方波定义：
                    // - 偏置：信号中心位置
                    // - 幅值：偏离中心的幅度
                    // - 高电平 = 偏置 + 幅值
                    // - 低电平 = 偏置 - 幅值
                    double highLevel = offset + amp;  
                    double lowLevel = offset - amp;  

                    double period = 1.0 / freq;
                    double phaseTime = timeSeconds % period;
                    return phaseTime < dutyFrac * period ? highLevel : lowLevel;

                case OutputWaveformType.Dc:
                default:
                    return offset;
            }
        }

        private static double TryParseDouble(string text, double defaultValue)
        {
            if (string.IsNullOrWhiteSpace(text) || text == "-")
                return defaultValue;

            return double.TryParse(text, out var v) ? v : defaultValue;
        }

        private void DrawAxis(double height, double width)
        {
            // 画中线（0V）和上下边界的大致提示线
            var zeroLine = new Line
            {
                X1 = 0,
                X2 = width,
                Y1 = height / 2,
                Y2 = height / 2,
                Stroke = Brushes.LightGray,
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 2, 4 }
            };
            WaveformCanvas.Children.Add(zeroLine);

            // 简单的 -10V / +10V 标记
            var topText = new TextBlock
            {
                Text = "+10V",
                FontSize = 10,
                Foreground = Brushes.Gray
            };
            Canvas.SetLeft(topText, 2);
            Canvas.SetTop(topText, 2);
            WaveformCanvas.Children.Add(topText);

            var bottomText = new TextBlock
            {
                Text = "-10V",
                FontSize = 10,
                Foreground = Brushes.Gray
            };
            Canvas.SetLeft(bottomText, 2);
            Canvas.SetTop(bottomText, height - 14);
            WaveformCanvas.Children.Add(bottomText);
        }

        private void IntegerOnlyTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                e.Handled = !WillTextBeValidInteger(textBox, e.Text);
            }
        }

        private void IntegerOnlyTextBox_OnPaste(object sender, DataObjectPastingEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                if (!e.DataObject.GetDataPresent(DataFormats.Text))
                {
                    e.CancelCommand();
                    return;
                }

                var pasteText = e.DataObject.GetData(DataFormats.Text) as string ?? string.Empty;
                if (!IsValidIntegerText(GetProposedText(textBox, pasteText)))
                {
                    e.CancelCommand();
                }
            }
        }

        private static bool WillTextBeValidInteger(TextBox textBox, string input)
        {
            if (string.IsNullOrEmpty(input))
                return true;

            var proposed = GetProposedText(textBox, input);
            return IsValidIntegerText(proposed);
        }

        private static string GetProposedText(TextBox textBox, string input)
        {
            var text = textBox.Text ?? string.Empty;
            int selectionStart = textBox.SelectionStart;
            if (selectionStart < 0)
                selectionStart = 0;
            if (selectionStart > text.Length)
                selectionStart = text.Length;

            int selectionLength = textBox.SelectionLength;
            if (selectionLength < 0)
                selectionLength = 0;
            if (selectionStart + selectionLength > text.Length)
                selectionLength = text.Length - selectionStart;

            if (selectionLength > 0)
            {
                text = text.Remove(selectionStart, selectionLength);
            }

            return text.Insert(selectionStart, input);
        }

        private static bool IsValidIntegerText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return true;

            if (text == "-")
                return true;

            return int.TryParse(text, out _);
        }

        private void NumericTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                e.Handled = !WillTextBeValidNumeric(textBox, e.Text);
            }
        }

        private void NumericTextBox_OnPaste(object sender, DataObjectPastingEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                if (!e.DataObject.GetDataPresent(DataFormats.Text))
                {
                    e.CancelCommand();
                    return;
                }

                var pasteText = e.DataObject.GetData(DataFormats.Text) as string ?? string.Empty;
                if (!IsValidNumericText(GetProposedText(textBox, pasteText)))
                {
                    e.CancelCommand();
                }
            }
        }

        private static bool WillTextBeValidNumeric(TextBox textBox, string input)
        {
            if (string.IsNullOrEmpty(input))
                return true;

            var proposed = GetProposedText(textBox, input);
            return IsValidNumericText(proposed);
        }

        private static bool IsValidNumericText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return true;

            // 允许输入单个"-"或单个"."
            if (text == "-" || text == ".")
                return true;

            // 允许以"-"或"."开头的数字
            if (text.StartsWith("-") || text.StartsWith("."))
            {
                if (text.Length == 1) return true;
                text = text.Substring(1);
            }

            // 检查剩余部分是否为有效数字（整数或小数）
            return double.TryParse(text, out _);
        }
    }
}
