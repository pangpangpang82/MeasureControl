using System.Windows.Controls;
using MeasureControl.ViewModels.SingleBoardTest.AirController;

namespace MeasureControl.Views.SingleBoardTest.AirController
{
    public partial class S_C_8_6_2View : UserControl
    {
        public S_C_8_6_2View()
        {
            InitializeComponent();
            DataContext = new S_C_8_6_2ViewModel();
        }

        private void RootGrid_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // Keep behavior consistent with other AirController test views:
            // do not steal focus here, otherwise ComboBox clicks can be interrupted.
        }
    }
}
