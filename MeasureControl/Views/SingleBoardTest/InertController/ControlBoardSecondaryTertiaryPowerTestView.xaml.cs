using System.Windows.Controls;
using MeasureControl.ViewModels.SingleBoardTest.InertController;
using Prism.Ioc;

namespace MeasureControl.Views.SingleBoardTest.InertController
{
    public partial class ControlBoardSecondaryTertiaryPowerTestView : UserControl
    {
        public ControlBoardSecondaryTertiaryPowerTestView()
        {
            InitializeComponent();
            DataContext = ContainerLocator.Container.Resolve<ControlBoardSecondaryTertiaryPowerTestViewModel>();
        }
    }
}
