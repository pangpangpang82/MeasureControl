using System.Windows.Controls;
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

            // 从Prism容器解析ViewModel
            var container = (System.Windows.Application.Current as App)?.Container;
            if (container != null)
            {
                DataContext = container.Resolve<ViewModels.SingleBoardTest.FuelController.DiscreteInputTestViewModel>();
            }
        }
    }
}
