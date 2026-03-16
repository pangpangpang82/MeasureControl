using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Globalization;
using MeasureControl.ViewModels.SingleBoardTest.HydraulicController;

namespace MeasureControl.Views.SingleBoardTest.HydraulicController
{
    /// <summary>
    /// HC_6_5.xaml 的交互逻辑
    /// </summary>
    public partial class HC_6_5 : UserControl
    {
        private bool _isUpdatingCustomInput;

        public HC_6_5()
        {
            InitializeComponent();
        }

        private void RootGrid_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e?.OriginalSource is DependencyObject source && FindAncestor<ComboBox>(source) != null)
            {
                return;
            }

            if (sender is not Grid rootGrid)
            {
                return;
            }

            if (e?.OriginalSource is DependencyObject origin && !IsDescendantOf(rootGrid, origin))
            {
                return;
            }

            Keyboard.ClearFocus();
            rootGrid.Focus();
        }

        private static T FindAncestor<T>(DependencyObject current) where T : DependencyObject
        {
            while (current != null)
            {
                if (current is T target)
                {
                    return target;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return null;
        }

        private static bool IsDescendantOf(DependencyObject ancestor, DependencyObject current)
        {
            while (current != null)
            {
                if (ReferenceEquals(current, ancestor))
                {
                    return true;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return false;
        }

        private void TextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingCustomInput || sender is not TextBox textBox)
            {
                return;
            }

            var sanitized = SanitizeCurrentText(textBox.Text);
            if (string.Equals(textBox.Text, sanitized, System.StringComparison.Ordinal))
            {
                return;
            }

            try
            {
                _isUpdatingCustomInput = true;
                var caretIndex = textBox.CaretIndex;
                textBox.Text = sanitized;
                textBox.CaretIndex = System.Math.Min(caretIndex, sanitized.Length);
            }
            finally
            {
                _isUpdatingCustomInput = false;
            }
        }

        private void TextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingCustomInput || sender is not TextBox textBox)
            {
                return;
            }

            var formatted = FormatCurrentText(textBox.Text);
            if (string.Equals(textBox.Text, formatted, System.StringComparison.Ordinal))
            {
                return;
            }

            try
            {
                _isUpdatingCustomInput = true;
                textBox.Text = formatted;
                textBox.CaretIndex = formatted.Length;
            }
            finally
            {
                _isUpdatingCustomInput = false;
            }

            if (DataContext is HC_6_5ViewModel viewModel)
            {
                viewModel.CustomCurrentInput = formatted;
            }
        }

        private static string SanitizeCurrentText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var raw = text.Replace("mA", string.Empty).Replace("MA", string.Empty).Replace("ma", string.Empty).Trim();
            raw = raw.Replace(',', '.');

            var chars = new System.Collections.Generic.List<char>(raw.Length);
            var hasDot = false;
            var decimalCount = 0;
            foreach (var ch in raw)
            {
                if (char.IsDigit(ch))
                {
                    if (hasDot)
                    {
                        if (decimalCount >= 1)
                        {
                            continue;
                        }

                        decimalCount++;
                    }

                    chars.Add(ch);
                    continue;
                }

                if (ch == '.' && !hasDot)
                {
                    hasDot = true;
                    chars.Add(ch);
                }
            }

            return new string(chars.ToArray());
        }

        private static string FormatCurrentText(string text)
        {
            var sanitized = SanitizeCurrentText(text);
            if (string.IsNullOrWhiteSpace(sanitized) || sanitized.EndsWith(".", System.StringComparison.Ordinal))
            {
                sanitized = sanitized.TrimEnd('.');
            }

            if (!double.TryParse(sanitized, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
            {
                return sanitized;
            }

            value = System.Math.Truncate(value * 10d) / 10d;
            return value.ToString("0.0", CultureInfo.InvariantCulture);
        }
    }
}
