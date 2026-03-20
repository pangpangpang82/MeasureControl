using System.Windows.Controls;
using MeasureControl.ViewModels.TestTask;
using Prism.Mvvm;

namespace MeasureControl.Views.TestTask
{
    /// <summary>
    /// OscilloscopeTestPanelView.xaml 的交互逻辑
    /// </summary>
    public partial class OscilloscopeTestPanelView : UserControl
    {
        public OscilloscopeTestPanelView()
        {
            InitializeComponent();
            
            // 设置ViewModelLocator
            if (ViewModelLocator.GetAutoWireViewModel(this) == null)
            {
                ViewModelLocator.SetAutoWireViewModel(this, true);
            }
        }

        private void CardNameTextBox_LostFocus(object sender, System.Windows.RoutedEventArgs e)
        {
            // 处理失去焦点事件（如果需要）
        }
    }
}

