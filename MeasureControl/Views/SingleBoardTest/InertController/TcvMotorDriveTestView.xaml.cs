using System.Windows.Controls;
using MeasureControl.ViewModels.SingleBoardTest.InertController;
using Prism.Ioc;

namespace MeasureControl.Views.SingleBoardTest.InertController
{
    public partial class TcvMotorDriveTestView : UserControl
    {
        public TcvMotorDriveTestView()
        {
            InitializeComponent();
            DataContext = ContainerLocator.Container.Resolve<TcvMotorDriveTestViewModel>();
        }
    }
}
