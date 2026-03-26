using System.Windows.Controls;
using MeasureControl.ViewModels.SingleBoardTest.InertController;
using Prism.Ioc;

namespace MeasureControl.Views.SingleBoardTest.InertController
{
    public partial class PowerMonitorTestView : UserControl
    {
        public PowerMonitorTestView()
        {
            InitializeComponent();
            DataContext = ContainerLocator.Container.Resolve<PowerMonitorTestViewModel>();
        }
    }
}
