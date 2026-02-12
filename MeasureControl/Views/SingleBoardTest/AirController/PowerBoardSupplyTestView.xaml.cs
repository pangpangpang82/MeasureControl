using MeasureControl.ViewModels.SingleBoardTest.AirController;
using System.Windows.Controls;

namespace MeasureControl.Views.SingleBoardTest.AirController
{
    public partial class PowerBoardSupplyTestView : UserControl
    {
        private readonly PowerBoardSupplyTestViewModel _viewModel;

        public PowerBoardSupplyTestView()
        {
            InitializeComponent();
            _viewModel = new PowerBoardSupplyTestViewModel();
            DataContext = _viewModel;
        }

        public PowerBoardSupplyTestView(string channel, string title) : this()
        {
            _viewModel.Configure(channel, title);
        }
    }
}
