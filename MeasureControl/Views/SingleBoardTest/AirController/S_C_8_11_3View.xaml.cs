using System.Windows.Controls;
using System.Windows.Input;
using MeasureControl.ViewModels.SingleBoardTest.AirController;

namespace MeasureControl.Views.SingleBoardTest.AirController
{
    public partial class S_C_8_11_3View : UserControl
    {
        public S_C_8_11_3View()
        {
            InitializeComponent();
            DataContext = new S_C_8_11_3ViewModel();
        }

        private void RootGrid_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            RootGrid.Focus();
        }
    }
}
