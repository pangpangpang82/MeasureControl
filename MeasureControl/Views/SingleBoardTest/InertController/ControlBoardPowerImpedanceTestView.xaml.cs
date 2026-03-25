using System.Windows.Controls;
using MeasureControl.ViewModels.SingleBoardTest.InertController;
using Prism.Ioc;

namespace MeasureControl.Views.SingleBoardTest.InertController
{
    public partial class ControlBoardPowerImpedanceTestView : UserControl
    {
        public ControlBoardPowerImpedanceTestView()
        {
            InitializeComponent();
            DataContext = ContainerLocator.Container.Resolve<ControlBoardPowerImpedanceTestViewModel>();
        }
    }
}
