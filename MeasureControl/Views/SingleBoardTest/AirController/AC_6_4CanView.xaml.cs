using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MeasureControl.ViewModels.SingleBoardTest.AirController;

namespace MeasureControl.Views.SingleBoardTest.AirController
{
    public partial class AC_6_4CanView : UserControl
    {
        public AC_6_4CanView()
        {
            InitializeComponent();
            DataContext = new AC_6_4CanViewModel();
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

        private void ComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {

        }
    }
}
