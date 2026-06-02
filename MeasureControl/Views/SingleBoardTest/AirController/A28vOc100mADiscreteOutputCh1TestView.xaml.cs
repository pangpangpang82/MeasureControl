using System.Windows.Controls;
using MeasureControl.ViewModels.SingleBoardTest.AirController;

namespace MeasureControl.Views.SingleBoardTest.AirController
{
    public partial class A28vOc100mADiscreteOutputCh1TestView : UserControl
    {
        public A28vOc100mADiscreteOutputCh1TestView()
        {
            InitializeComponent();
            DataContext = new A28vOc100mADiscreteOutputCh1TestViewModel();
        }

        private void RootGrid_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            RootGrid.Focus();
        }
    }
}
