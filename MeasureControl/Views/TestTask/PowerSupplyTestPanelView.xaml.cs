using System.Windows.Controls;

namespace MeasureControl.Views.TestTask
{
    /// <summary>
    /// PowerSupplyTestPanelView.xaml 的交互逻辑
    /// </summary>
    public partial class PowerSupplyTestPanelView : UserControl
    {
        public PowerSupplyTestPanelView()
        {
            InitializeComponent();
        }

        private void CardNameTextBox_LostFocus(object sender, System.Windows.RoutedEventArgs e)
        {
            // 处理失去焦点事件（如果需要）
        }
    }
}
