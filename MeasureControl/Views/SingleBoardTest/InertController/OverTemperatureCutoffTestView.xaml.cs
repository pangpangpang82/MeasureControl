using System.Windows.Controls;
using MeasureControl.ViewModels.SingleBoardTest.InertController;
using Prism.Ioc;

namespace MeasureControl.Views.SingleBoardTest.InertController
{
    public partial class OverTemperatureCutoffTestView : UserControl
    {
        public OverTemperatureCutoffTestView()
        {
            InitializeComponent();
            DataContext = ContainerLocator.Container.Resolve<OverTemperatureCutoffTestViewModel>();
        }
    }
}
