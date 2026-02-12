using System.Windows.Controls;
using MeasureControl.ViewModels.SingleBoardTest.AirController;

namespace MeasureControl.Views.SingleBoardTest.AirController
{
    public partial class GndOcDiscreteOutputCh2TestView : UserControl
    {
        public GndOcDiscreteOutputCh2TestView()
        {
            InitializeComponent();
            DataContext = new GndOcDiscreteOutputCh2TestViewModel();
        }
    }
}
