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
    }
}
