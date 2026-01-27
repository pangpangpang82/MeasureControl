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

namespace MeasureControl.Views.SingleBoardTest.AirController
{
    /// <summary>
    /// PT500TemperatureSensorTestView.xaml 的交互逻辑
    /// </summary>
    public partial class PT500TemperatureSensorTestView : UserControl
    {
        public PT500TemperatureSensorTestView()
        {
            InitializeComponent();
        }

        private void RootGrid_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e?.OriginalSource is DependencyObject source && FindAncestor<ComboBox>(source) != null)
            {
                return;
            }

            if (e?.OriginalSource is DependencyObject origin && !IsDescendantOf(RootGrid, origin))
            {
                return;
            }

            Keyboard.ClearFocus();
            RootGrid.Focus();
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
