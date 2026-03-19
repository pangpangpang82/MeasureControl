using System.Windows.Controls;
using MeasureControl.ViewModels.SingleBoardTest.InertController;
using Prism.Ioc;

namespace MeasureControl.Views.SingleBoardTest.InertController
{
    public partial class OxygenSensorSignalAcquisitionTestView : UserControl
    {
        public OxygenSensorSignalAcquisitionTestView()
        {
            InitializeComponent();
            DataContext = ContainerLocator.Container.Resolve<OxygenSensorSignalAcquisitionTestViewModel>();
        }
    }
}
