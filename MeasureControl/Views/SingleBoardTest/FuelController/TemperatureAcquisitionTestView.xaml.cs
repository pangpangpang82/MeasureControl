using System.Windows.Controls;
using MeasureControl.ViewModels.SingleBoardTest.FuelController;
using Prism.Ioc;

namespace MeasureControl.Views.SingleBoardTest.FuelController
{
    /// <summary>
    /// TemperatureAcquisitionTestView.xaml 的交互逻辑
    /// </summary>
    public partial class TemperatureAcquisitionTestView : UserControl
    {
        public TemperatureAcquisitionTestView()
        {
            InitializeComponent();
            DataContext = ContainerLocator.Container.Resolve<TemperatureAcquisitionTestViewModel>();
        }
    }
}
