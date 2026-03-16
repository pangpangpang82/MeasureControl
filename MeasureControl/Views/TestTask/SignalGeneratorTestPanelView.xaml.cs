using System.Windows.Controls;
using MeasureControl.ViewModels.TestTask;
using Prism.Mvvm;

namespace MeasureControl.Views.TestTask
{
    /// <summary>
    /// SignalGeneratorTestPanelView.xaml 的交互逻辑
    /// </summary>
    public partial class SignalGeneratorTestPanelView : UserControl
    {
        public SignalGeneratorTestPanelView()
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

