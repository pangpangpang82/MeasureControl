using System.Windows.Controls;
using MeasureControl.ViewModels.SingleBoardTest.InertController;
using Prism.Ioc;

namespace MeasureControl.Views.SingleBoardTest.InertController
{
    public partial class PressureSensorSignalAcquisitionTestView : UserControl
    {
        public PressureSensorSignalAcquisitionTestView()
        {
            InitializeComponent();
            DataContext = ContainerLocator.Container.Resolve<PressureSensorSignalAcquisitionTestViewModel>();
        }
    }
}
