using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MeasureControl.ViewModels.SingleBoardTest.HydraulicController;
using Prism.Ioc;

namespace MeasureControl.Views.SingleBoardTest.HydraulicController
{
    /// <summary>
    /// HC_6_2.xaml 的交互逻辑
    /// </summary>
    public partial class HC_6_2 : UserControl
    {
        public HC_6_2()
        {
            InitializeComponent();
            DataContext = ContainerLocator.Container.Resolve<HC_6_2ViewModel>();
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
    }
}
