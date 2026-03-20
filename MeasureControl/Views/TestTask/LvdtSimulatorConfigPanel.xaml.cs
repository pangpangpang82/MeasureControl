using System;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows;
using MeasureControl.ViewModels.TestTask;

namespace MeasureControl.Views.TestTask
{
    public partial class LvdtSimulatorConfigPanel : UserControl
    {
        private LvdtSimulatorConfigPanelViewModel Vm => DataContext as LvdtSimulatorConfigPanelViewModel;
        private string _originalCardName;

        public LvdtSimulatorConfigPanel()
        {
            InitializeComponent();
            this.DataContextChanged += LvdtSimulatorConfigPanel_DataContextChanged;

            CardNameTextBox.GotFocus += (s, e) =>
            {
                if (Vm != null)
                {
                    _originalCardName = Vm.CardName;
                }
            };
        }

        private void CardNameTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            Vm?.OnCardNameChanged(_originalCardName);
        }

        private void Border_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is Border border)
            {
                border.Focus();
                e.Handled = true;
            }
        }

        private void LvdtSimulatorConfigPanel_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is LvdtSimulatorConfigPanelViewModel oldVm)
            {
                oldVm.WaveformUpdated -= OnWaveformUpdated;
                oldVm.WaveformUpdatedVaVb -= OnWaveformUpdatedVaVb;
                System.Diagnostics.Debug.WriteLine("[UI波形] 取消订阅波形更新事件");
            }
            if (e.NewValue is LvdtSimulatorConfigPanelViewModel newVm)
            {
                newVm.WaveformUpdated += OnWaveformUpdated;
                newVm.WaveformUpdatedVaVb += OnWaveformUpdatedVaVb;
                System.Diagnostics.Debug.WriteLine("[UI波形] 已订阅波形更新事件");
            }
        }

        private void OnWaveformUpdated(double[] samples)
        {
            // Dispatch to UI thread
            Dispatcher.Invoke(() =>
            {
                var canvas = FindOutputCanvas();
                if (canvas != null)
                {
                    System.Diagnostics.Debug.WriteLine($"[UI波形] 找到输出Canvas，正在绘制波形，样本数量: {samples?.Length ?? 0}");
                    DrawWaveformOnCanvas(canvas, samples);
                    var placeholder = FindChildByName<TextBlock>(this, "OutputPlaceholder");
                    if (placeholder != null) placeholder.Visibility = Visibility.Collapsed;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("[UI波形] 未找到输出Canvas，无法绘制波形");
                }
            });
        }

        private void DrawWaveformOnCanvas(Canvas canvas, double[] samples)
        {
            if (canvas == null) return;
            canvas.Children.Clear();
            if (samples == null || samples.Length == 0) return;

            double w = canvas.ActualWidth;
            double h = canvas.ActualHeight;
            if (w <= 0 || h <= 0) return;

            var poly = new Polyline
            {
                Stroke = Brushes.Lime,
                StrokeThickness = 1
            };

            double min = samples.Min();
            double max = samples.Max();
            double range = Math.Max(1e-6, max - min);

            for (int i = 0; i < samples.Length; i++)
            {
                double x = (double)i / (samples.Length - 1) * w;
                double yNorm = (samples[i] - min) / range;
                double y = h - yNorm * h;
                poly.Points.Add(new Point(x, y));
            }

            canvas.Children.Add(poly);
        }
        private void OnWaveformUpdatedVaVb(double[] vaSamples, double[] vbSamples)
        {
            Dispatcher.Invoke(() =>
            {
                var canvas = FindExcitationCanvas();
                if (canvas != null)
                {
                    DrawWaveformVaVbOnCanvas(canvas, vaSamples, vbSamples);
                    var placeholder = FindChildByName<TextBlock>(this, "ExcitationPlaceholder");
                    if (placeholder != null) placeholder.Visibility = Visibility.Collapsed;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("[UI波形] 未找到激励Canvas，无法绘制VaVb波形");
                }
            });
        }

        private void DrawWaveformVaVbOnCanvas(Canvas canvas, double[] va, double[] vb)
        {
            if (canvas == null) return;
            canvas.Children.Clear();
            if ((va == null || va.Length == 0) && (vb == null || vb.Length == 0)) return;

            double w = canvas.ActualWidth;
            double h = canvas.ActualHeight;
            if (w <= 0 || h <= 0) return;

            void DrawPolyline(double[] samples, Brush color)
            {
                if (samples == null || samples.Length == 0) return;
                var poly = new Polyline { Stroke = color, StrokeThickness = 1 };
                double min = samples.Min();
                double max = samples.Max();
                double range = Math.Max(1e-6, max - min);
                for (int i = 0; i < samples.Length; i++)
                {
                    double x = (double)i / (samples.Length - 1) * w;
                    double y = h - (samples[i] - min) / range * h;
                    poly.Points.Add(new Point(x, y));
                }
                canvas.Children.Add(poly);
            }

            // interpret first array as internal (synthesized), second as external (measured)
            string source = Vm?.WaveformExcitationSource ?? "External";
            if (source == "Internal")
            {
                DrawPolyline(va, Brushes.Lime);
            }
            else if (source == "External")
            {
                DrawPolyline(vb, Brushes.Yellow);
            }
            else // Both
            {
                DrawPolyline(va, Brushes.Lime);
                DrawPolyline(vb, Brushes.Yellow);
            }
        }
        private Canvas FindExcitationCanvas()
        {
            var namedCanvas = FindVisualChild<Canvas>(this, "WaveformCanvas_Excitation");
            if (namedCanvas != null && namedCanvas.IsVisible) return namedCanvas;
            return FindVisibleWaveformCanvas(this);
        }

        private Canvas FindOutputCanvas()
        {
            var namedCanvas = FindVisualChild<Canvas>(this, "WaveformCanvas_Output");
            if (namedCanvas != null && namedCanvas.IsVisible) return namedCanvas;
            return FindVisibleWaveformCanvas(this);
        }

        private Canvas FindVisibleWaveformCanvas(DependencyObject parent)
        {
            if (parent == null) return null;

            // 首先尝试通过Name查找
            var namedCanvas = FindVisualChild<Canvas>(parent, "WaveformCanvas");
            if (namedCanvas != null && namedCanvas.IsVisible)
            {
                return namedCanvas;
            }

            // 如果没找到，查找所有Canvas并返回第一个可见的
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is Canvas canvas && canvas.IsVisible && canvas.Background == Brushes.Black)
                {
                    // 通过背景色判断是否是波形Canvas
                    return canvas;
                }

                var result = FindVisibleWaveformCanvas(child);
                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }

        private T FindVisualChild<T>(DependencyObject parent, string name) where T : FrameworkElement
        {
            if (parent == null) return null;

            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T element && element.Name == name)
                {
                    return element;
                }

                var result = FindVisualChild<T>(child, name);
                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }

        private T FindChildByName<T>(DependencyObject parent, string name) where T : FrameworkElement
        {
            if (parent == null) return null;
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T fe && fe.Name == name) return fe;
                var result = FindChildByName<T>(child, name);
                if (result != null) return result;
            }
            return null;
        }
    }
}

