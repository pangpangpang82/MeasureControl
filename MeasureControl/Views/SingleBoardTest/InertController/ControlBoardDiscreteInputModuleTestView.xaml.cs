using System.Windows.Controls;
using MeasureControl.ViewModels.SingleBoardTest.InertController;
using Prism.Ioc;

namespace MeasureControl.Views.SingleBoardTest.InertController
{
    public partial class ControlBoardDiscreteInputModuleTestView : UserControl
    {
        public ControlBoardDiscreteInputModuleTestView()
        {
            InitializeComponent();
            DataContext = ContainerLocator.Container.Resolve<ControlBoardDiscreteInputModuleTestViewModel>();
        }
    }
}
