using System.Windows.Controls;
using MeasureControl.ViewModels.SingleBoardTest.AirController;

namespace MeasureControl.Views.SingleBoardTest.AirController
{
    public partial class RS422Control422ReceiveTestView : UserControl
    {
        public RS422Control422ReceiveTestView()
        {
            InitializeComponent();
            InnerView.DataContext = new RS422CommunicationCheckViewModel(RS422CommunicationCheckViewModel.Rs422CommTestMode.ReceiveOnly);
        }
    }
}
