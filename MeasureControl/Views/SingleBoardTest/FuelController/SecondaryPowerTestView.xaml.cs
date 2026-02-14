using System.Windows.Controls;
using MeasureControl.ViewModels.SingleBoardTest.FuelController;
using Prism.Ioc;

namespace MeasureControl.Views.SingleBoardTest.FuelController
{
    /// <summary>
    /// SecondaryPowerTestView.xaml 的交互逻辑
    /// 二次电源测试视图
    /// </summary>
    public partial class SecondaryPowerTestView : UserControl
    {
        public SecondaryPowerTestView()
        {
            InitializeComponent();
            DataContext = ContainerLocator.Container.Resolve<SecondaryPowerTestViewModel>();
        }
    }
}
