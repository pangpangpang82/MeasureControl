using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Linq;
using MeasureControl.ViewModels.SingleBoardTest.HydraulicController;
using Prism.Ioc;

namespace MeasureControl.Views.SingleBoardTest.HydraulicController
{
    /// <summary>
    /// HC_6_7.xaml 的交互逻辑
    /// </summary>
    public partial class HC_6_7 : UserControl
    {
        private bool _isUpdatingIntegerInput;

        public HC_6_7()
        {
            InitializeComponent();
            DataContext = ContainerLocator.Container.Resolve<HC_6_7ViewModel>();
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

        private void IntegerTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingIntegerInput || sender is not TextBox textBox)
                return;

            var sanitized = SanitizeIntegerText(textBox.Text);
            if (textBox.Text == sanitized)
                return;

            try
            {
                _isUpdatingIntegerInput = true;
                var caretIndex = textBox.CaretIndex;
                textBox.Text = sanitized;
                textBox.CaretIndex = System.Math.Min(caretIndex, sanitized.Length);
            }
            finally
            {
                _isUpdatingIntegerInput = false;
            }
        }

        private void IntegerTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingIntegerInput || sender is not TextBox textBox)
                return;

            var formatted = SanitizeIntegerText(textBox.Text);
            var tag = textBox.Tag as string;

            try
            {
                _isUpdatingIntegerInput = true;
                textBox.Text = formatted;
                textBox.CaretIndex = formatted.Length;
            }
            finally
            {
                _isUpdatingIntegerInput = false;
            }

            if (DataContext is HC_6_7ViewModel viewModel)
            {
                if (string.Equals(tag, "Low", System.StringComparison.Ordinal))
                    viewModel.ManualRangeLowInput = formatted;
                else if (string.Equals(tag, "High", System.StringComparison.Ordinal))
                    viewModel.ManualRangeHighInput = formatted;

                viewModel.NormalizeManualRangeInputs(tag);

                try
                {
                    _isUpdatingIntegerInput = true;
                    if (string.Equals(tag, "Low", System.StringComparison.Ordinal))
                    {
                        textBox.Text = viewModel.ManualRangeLowInput;
                    }
                    else if (string.Equals(tag, "High", System.StringComparison.Ordinal))
                    {
                        textBox.Text = viewModel.ManualRangeHighInput;
                    }

                    textBox.CaretIndex = textBox.Text.Length;
                }
                finally
                {
                    _isUpdatingIntegerInput = false;
                }
            }
        }

        private static string SanitizeIntegerText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var chars = text.Where(char.IsDigit).ToArray();
            return new string(chars);
        }
    }
}
