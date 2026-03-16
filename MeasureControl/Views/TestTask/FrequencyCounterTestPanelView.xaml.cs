using System.Windows.Controls;
using Prism.Mvvm;
using Prism.Regions;

namespace MeasureControl.Views.TestTask
{
    /// <summary>
    /// FrequencyCounterTestPanelView.xaml 的交互逻辑
    /// </summary>
    public partial class FrequencyCounterTestPanelView : UserControl, IRegionMemberLifetime
    {
        public bool KeepAlive => true;

        public FrequencyCounterTestPanelView()
        {
            InitializeComponent();
            ViewModelLocator.SetAutoWireViewModel(this, true);
        }

        private void CardNameTextBox_LostFocus(object sender, System.Windows.RoutedEventArgs e)
        {
            // 处理失去焦点事件（如果需要）
        }
    }
}

