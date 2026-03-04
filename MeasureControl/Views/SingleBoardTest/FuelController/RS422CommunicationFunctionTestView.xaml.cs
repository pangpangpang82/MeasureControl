using System.Windows.Controls;
using MeasureControl.ViewModels.SingleBoardTest.FuelController;
using Prism.Ioc;

namespace MeasureControl.Views.SingleBoardTest.FuelController
{
    public partial class RS422CommunicationFunctionTestView : UserControl
    {
        public RS422CommunicationFunctionTestView()
        {
            InitializeComponent();
            DataContext = ContainerLocator.Container.Resolve<RS422CommunicationFunctionTestViewModel>();
        }
    }
}
