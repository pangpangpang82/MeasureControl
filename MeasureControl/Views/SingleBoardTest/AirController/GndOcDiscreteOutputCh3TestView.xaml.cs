using System.Windows.Controls;
using MeasureControl.ViewModels.SingleBoardTest.AirController;

namespace MeasureControl.Views.SingleBoardTest.AirController
{
    public partial class GndOcDiscreteOutputCh3TestView : UserControl
    {
        public GndOcDiscreteOutputCh3TestView()
        {
            InitializeComponent();
            DataContext = new GndOcDiscreteOutputCh3TestViewModel();
        }
    }
}
