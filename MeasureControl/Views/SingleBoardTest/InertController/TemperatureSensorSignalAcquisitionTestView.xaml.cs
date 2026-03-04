using System.Windows.Controls;
using MeasureControl.ViewModels.SingleBoardTest.InertController;
using Prism.Ioc;

namespace MeasureControl.Views.SingleBoardTest.InertController
{
    public partial class TemperatureSensorSignalAcquisitionTestView : UserControl
    {
        public TemperatureSensorSignalAcquisitionTestView()
        {
            InitializeComponent();
            DataContext = ContainerLocator.Container.Resolve<TemperatureSensorSignalAcquisitionTestViewModel>();
        }
    }
}
