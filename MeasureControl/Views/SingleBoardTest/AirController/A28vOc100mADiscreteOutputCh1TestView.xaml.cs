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

        private void InitializeComponent()
        {
            System.Uri resourceLocator = new System.Uri("/MeasureControl;component/Views/SingleBoardTest/AirController/A28vOc100mADiscreteOutputCh1TestView.xaml", System.UriKind.Relative);
            System.Windows.Application.LoadComponent(this, resourceLocator);
        }
    }
}
