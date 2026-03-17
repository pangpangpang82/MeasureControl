using System.Windows.Controls;
using MeasureControl.ViewModels.SingleBoardTest.AirController;
using Prism.Ioc;

namespace MeasureControl.Views.SingleBoardTest.AirController
{
    public partial class A_C_7_1View : UserControl
    {
        public A_C_7_1View()
        {
            InitializeComponent();
            DataContext = ContainerLocator.Container.Resolve<A_C_7_1ViewModel>();
        }
    }
}
