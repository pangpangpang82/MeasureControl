using System.Windows.Controls;
using MeasureControl.ViewModels.SingleBoardTest.InertController;
using Prism.Ioc;

namespace MeasureControl.Views.SingleBoardTest.InertController
{
    public partial class LatchModuleCircuitTestView : UserControl
    {
        public LatchModuleCircuitTestView()
        {
            InitializeComponent();
            DataContext = ContainerLocator.Container.Resolve<LatchModuleCircuitTestViewModel>();
        }
    }
}
