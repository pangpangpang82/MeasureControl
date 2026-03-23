using System.Windows.Controls;
using MeasureControl.ViewModels.SingleBoardTest.AirController;

namespace MeasureControl.Views.SingleBoardTest.AirController
{
    public partial class S_C_8_6_1View : UserControl
    {
        public S_C_8_6_1View()
        {
            InitializeComponent();
            DataContext = new S_C_8_6_1ViewModel();
        }

        private void RootGrid_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
        }
    }
}
