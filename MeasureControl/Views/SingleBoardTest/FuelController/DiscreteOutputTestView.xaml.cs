using System.Windows.Controls;
using MeasureControl.ViewModels.SingleBoardTest.FuelController;
using Prism.Ioc;

namespace MeasureControl.Views.SingleBoardTest.FuelController
{
    public partial class DiscreteOutputTestView : UserControl
    {
        public DiscreteOutputTestView()
        {
            InitializeComponent();
            DataContext = ContainerLocator.Container.Resolve<DiscreteOutputTestViewModel>();
        }
    }
}
