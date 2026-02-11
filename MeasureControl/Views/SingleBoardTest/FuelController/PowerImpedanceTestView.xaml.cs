using System.Windows.Controls;
using MeasureControl.ViewModels.SingleBoardTest.FuelController;
using Prism.Ioc;

namespace MeasureControl.Views.SingleBoardTest.FuelController
{
    public partial class PowerImpedanceTestView : UserControl
    {
        public PowerImpedanceTestView()
        {
            InitializeComponent();
            DataContext = ContainerLocator.Container.Resolve<PowerImpedanceTestViewModel>();
        }
    }
}
