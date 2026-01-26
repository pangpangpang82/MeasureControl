using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Globalization;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using MeasureControl.ViewModels;
using MeasureControl.ViewModels.TestTask.CardCATPanel;

namespace MeasureControl.Views.TestTask.CardCATPanel
{
    /// <summary>
    /// ResistanceOutputConfigPanel.xaml 的交互逻辑 - 电阻输出通道配置面板
    /// </summary>
    public partial class PXI7012_RO : UserControl
    {
        private string _originalCardName;
        private const double ResistanceMin = 2.0;
        private const double ResistanceMax = 6700.0;

        private static void CoerceOffsetWithTarget(ResistanceChannelInfo channel)
        {
            if (channel == null) return;

            var target = channel.TargetResistance;
            var offset = channel.Offset;
            var sum = offset + target;

            if (sum < ResistanceMin)
            {
                offset = ResistanceMin - target;
            }
            else if (sum > ResistanceMax)
            {
                offset = ResistanceMax - target;
            }

            channel.Offset = offset;
        }

        public PXI7012_RO()
        {
            InitializeComponent();

            // 当TextBox获得焦点时，保存原始名称
            CardNameTextBox.GotFocus += (s, e) =>
            {
                if (DataContext is PXI7012_ROViewModel viewModel)
                {
                    _originalCardName = viewModel.CardName;
                }
            };
        }

        /// <summary>
        /// 处理板卡名称TextBox失去焦点事件
        /// </summary>
        private void CardNameTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (DataContext is PXI7012_ROViewModel viewModel)
            {
                viewModel.OnCardNameChanged(_originalCardName);
            }
        }

        /// <summary>
        /// 处理Border鼠标点击事件，用于转移焦点
        /// </summary>
        private void Border_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // 点击空白区域时，将焦点转移到Border，使TextBox失去焦点
            if (sender is Border border)
            {
                Keyboard.ClearFocus();
                border.Focus();
                e.Handled = true;
            }
        }

        private void ResistanceValueTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                textBox.Tag = textBox.Text;
            }
        }

        private void ResistanceValueTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is not TextBox textBox)
            {
                return;
            }

            var originalText = textBox.Tag as string;
            var bindingExpression = BindingOperations.GetBindingExpression(textBox, TextBox.TextProperty);
            var bindingPath = bindingExpression?.ParentBinding?.Path?.Path;

            if (string.IsNullOrWhiteSpace(bindingPath) || textBox.DataContext is not ResistanceChannelInfo channel)
            {
                return;
            }

            if (!TryParseFlexibleDouble(textBox.Text, out var value))
            {
                RestoreOriginal(textBox, originalText, channel, bindingPath);
                return;
            }

            value = Truncate(value, 3);

            if (string.Equals(bindingPath, nameof(ResistanceChannelInfo.Offset), StringComparison.Ordinal))
            {
                channel.Offset = value;
                CoerceOffsetWithTarget(channel);
            }
            else if (string.Equals(bindingPath, nameof(ResistanceChannelInfo.TargetResistance), StringComparison.Ordinal))
            {
                channel.TargetResistance = Truncate(Clamp(value, ResistanceMin, ResistanceMax), 3);
                CoerceOffsetWithTarget(channel);
            }

            bindingExpression?.UpdateTarget();
        }

        private static void RestoreOriginal(TextBox textBox, string originalText, ResistanceChannelInfo channel, string bindingPath)
        {
            if (TryParseFlexibleDouble(originalText, out var originalValue))
            {
                if (string.Equals(bindingPath, nameof(ResistanceChannelInfo.Offset), StringComparison.Ordinal))
                {
                    channel.Offset = originalValue;
                }
                else if (string.Equals(bindingPath, nameof(ResistanceChannelInfo.TargetResistance), StringComparison.Ordinal))
                {
                    channel.TargetResistance = originalValue;
                }
            }

            CoerceOffsetWithTarget(channel);

            var bindingExpression = BindingOperations.GetBindingExpression(textBox, TextBox.TextProperty);
            bindingExpression?.UpdateTarget();
        }

        private static bool TryParseFlexibleDouble(string input, out double value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(input))
            {
                return false;
            }

            var s = input.Trim();

            if (double.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out value))
            {
                return true;
            }

            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                return true;
            }

            var swapped = s.Contains(',') ? s.Replace(',', '.') : s.Replace('.', ',');
            if (double.TryParse(swapped, NumberStyles.Float, CultureInfo.CurrentCulture, out value))
            {
                return true;
            }

            return false;
        }

        private static double Clamp(double value, double min, double max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private static double Truncate(double value, int decimals)
        {
            var factor = Math.Pow(10, decimals);
            return Math.Truncate(value * factor) / factor;
        }
    }
}

