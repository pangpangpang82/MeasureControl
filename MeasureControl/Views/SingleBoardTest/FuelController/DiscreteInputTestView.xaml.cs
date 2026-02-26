using System.Windows.Controls;
using MeasureControl.ViewModels.SingleBoardTest.FuelController;
using Prism.Ioc;

namespace MeasureControl.Views.SingleBoardTest.FuelController
{
    /// <summary>
    /// DiscreteInputTestView.xaml 的交互逻辑
    /// </summary>
    public partial class DiscreteInputTestView : UserControl
    {
        public DiscreteInputTestView()
        {
            InitializeComponent();
            DataContext = ContainerLocator.Container.Resolve<DiscreteInputTestViewModel>();
        }
    }
}
