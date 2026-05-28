using MeasureControl.ViewModels.SingleBoardTest.AirController;
using System.Windows.Controls;
using System.Windows.Input;

namespace MeasureControl.Views.SingleBoardTest.AirController
{
    public partial class A_C_6_6_2View : UserControl
    {
        public A_C_6_6_2View()
        {
            InitializeComponent();
            DataContext = new A_C_6_6_2ViewModel();
        }

        private void RootGrid_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            RootGrid.Focus();
            Keyboard.ClearFocus();
        }
    }
}
