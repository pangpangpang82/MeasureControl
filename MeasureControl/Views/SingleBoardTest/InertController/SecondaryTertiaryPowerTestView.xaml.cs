using System.Windows.Controls;
using MeasureControl.ViewModels.SingleBoardTest.InertController;
using Prism.Ioc;

namespace MeasureControl.Views.SingleBoardTest.InertController
{
    public partial class SecondaryTertiaryPowerTestView : UserControl
    {
        public SecondaryTertiaryPowerTestView()
        {
            InitializeComponent();
            DataContext = ContainerLocator.Container.Resolve<SecondaryTertiaryPowerTestViewModel>();
        }
    }
}
