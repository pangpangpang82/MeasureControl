using System.Windows.Controls;
using MeasureControl.ViewModels.SingleBoardTest.InertController;
using Prism.Ioc;

namespace MeasureControl.Views.SingleBoardTest.InertController
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
