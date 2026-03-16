using System.Windows.Controls;
using MeasureControl.ViewModels.SingleBoardTest.AirController;

namespace MeasureControl.Views.SingleBoardTest.AirController
{
    public partial class RS422CommunicationCheckView : UserControl
    {
        public RS422CommunicationCheckView()
        {
            InitializeComponent();
            DataContext = new RS422CommunicationCheckViewModel();
        }
    }
}
