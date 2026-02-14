using System.Windows.Controls;
using MeasureControl.ViewModels.SingleBoardTest.AirController;

namespace MeasureControl.Views.SingleBoardTest.AirController
{
    public partial class A28vOc400mADiscreteOutputTestView : UserControl
    {
        public A28vOc400mADiscreteOutputTestView()
        {
            InitializeComponent();
            DataContext = new A28vOc400mADiscreteOutputTestViewModel(1, "A控制通道28V/OC型400mA离散输出通道1输出测试");
        }

        public A28vOc400mADiscreteOutputTestView(int channelNumber, string pageTitle)
        {
            InitializeComponent();
            DataContext = new A28vOc400mADiscreteOutputTestViewModel(channelNumber, pageTitle);
        }
        
        private void InitializeComponent()
        {
            System.Uri resourceLocator = new System.Uri("/MeasureControl;component/Views/SingleBoardTest/AirController/A28vOc400mADiscreteOutputTestView.xaml", System.UriKind.Relative);
            System.Windows.Application.LoadComponent(this, resourceLocator);
        }
    }
}
