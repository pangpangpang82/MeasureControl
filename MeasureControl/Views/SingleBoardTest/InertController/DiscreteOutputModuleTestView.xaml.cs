using Prism.Ioc;
using System.Windows.Controls;

namespace MeasureControl.Views.SingleBoardTest.InertController
{
    public partial class DiscreteOutputModuleTestView : UserControl
    {
        public DiscreteOutputModuleTestView()
        {
            InitializeComponent();
            DataContext = ContainerLocator.Container.Resolve<MeasureControl.ViewModels.SingleBoardTest.InertController.DiscreteOutputModuleTestViewModel>();
        }
    }
}
