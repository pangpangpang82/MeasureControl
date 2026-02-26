using System.Windows.Controls;
using MeasureControl.ViewModels.SingleBoardTest.FuelController;
using Prism.Ioc;

namespace MeasureControl.Views.SingleBoardTest.FuelController
{
    public partial class RS422SelfCheckTestView : UserControl
    {
        public RS422SelfCheckTestView()
        {
            InitializeComponent();
            DataContext = ContainerLocator.Container.Resolve<RS422SelfCheckTestViewModel>();
        }
    }
}
