using System.Windows.Controls;
using System.Windows.Input;
using MeasureControl.ViewModels.SingleBoardTest.AirController;

namespace MeasureControl.Views.SingleBoardTest.AirController
{
    public partial class A28vOc400mACh1TestView : UserControl
    {
        public A28vOc400mACh1TestView()
        {
            InitializeComponent();
            DataContext = new A28vOc400mACh1TestViewModel();
        }

        private void RootGrid_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            RootGrid.Focus();
        }
    }
}
