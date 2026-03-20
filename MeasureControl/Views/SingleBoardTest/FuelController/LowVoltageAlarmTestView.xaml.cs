using System.Windows.Controls;
using MeasureControl.ViewModels.SingleBoardTest.FuelController;
using Prism.Ioc;

namespace MeasureControl.Views.SingleBoardTest.FuelController
{
    /// <summary>
    /// LowVoltageAlarmTestView.xaml 的交互逻辑
    /// </summary>
    public partial class LowVoltageAlarmTestView : UserControl
    {
        public LowVoltageAlarmTestView()
        {
            InitializeComponent();

            // 通过Prism容器解析ViewModel
            var container = ContainerLocator.Container;
            if (container != null)
            {
                DataContext = container.Resolve<LowVoltageAlarmTestViewModel>();
            }
        }
    }
}
