using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace MeasureControl.Views.SingleBoardTest.HydraulicController
{
    /// <summary>
    /// HC_6_2.xaml 的交互逻辑
    /// </summary>
    public partial class HC_6_2 : UserControl
    {
        public HC_6_2()
        {
            InitializeComponent();
        }
        
        private void RootGrid_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            RootGrid.Focus();
        }
    }
}
