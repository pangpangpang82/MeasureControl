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
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace MeasureControl.Views.TestTask.CardCATPanel.PXIe7131
{
    /// <summary>
    /// DM8600_485.xaml 的交互逻辑
    /// </summary>
    public partial class DM8600_485 : Window
    {
        public DM8600_485()
        {
            InitializeComponent();
        }

        private void FormatNumericTextOnLostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is not TextBox tb)
            {
                return;
            }

            var binding = BindingOperations.GetBindingExpression(tb, TextBox.TextProperty);
            binding?.UpdateSource();

            var propertyName = binding?.ParentBinding?.Path?.Path;
            if (!string.IsNullOrWhiteSpace(propertyName)
                && tb.DataContext is MeasureControl.ViewModels.TestTask.CardCATPanel.PXIe7131_DIDOViewModel vm)
            {
                vm.NormalizeNumericInput(propertyName);
            }

            binding?.UpdateTarget();
        }

        private void ClearFocusOnBlank(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is DependencyObject source)
            {
                if (FindAncestor<TextBox>(source) != null || FindAncestor<Button>(source) != null || FindAncestor<CheckBox>(source) != null)
                {
                    return;
                }
            }

            Keyboard.ClearFocus();

            Dispatcher.BeginInvoke(new Action(() =>
            {
                FocusManager.SetFocusedElement(this, this);
                Keyboard.Focus(this);
            }), DispatcherPriority.Input);
        }

        private void DragWindow(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is DependencyObject source && FindAncestor<Button>(source) != null)
            {
                return;
            }

            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private static T FindAncestor<T>(DependencyObject current) where T : DependencyObject
        {
            while (current != null)
            {
                if (current is T match)
                {
                    return match;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return null;
        }

        private void MinimizeWindow(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void CloseWindow(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
