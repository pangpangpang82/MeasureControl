using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MeasureControl.Views.TestTask.CardCATPanel
{
    /// <summary>
    /// ART1553BConfigPanel.xaml 的交互逻辑
    /// </summary>
    public partial class ART1553BConfigPanel : System.Windows.Controls.UserControl
    {
        public ART1553BConfigPanel()
        {
            InitializeComponent();
        }

        /// <summary>
        /// 点击空白区域时清除焦点
        /// </summary>
        private void Border_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is System.Windows.Controls.Border)
            {
                Keyboard.ClearFocus();
            }
        }

        /// <summary>
        /// 板卡名称文本框失去焦点时更新
        /// </summary>
        private void CardNameTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.TextBox textBox)
            {
                // 触发绑定更新
                var binding = textBox.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty);
                binding?.UpdateSource();
            }
        }

        /// <summary>
        /// 通道Tab切换事件处理
        /// </summary>
        private void ChannelTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 只处理顶级TabControl的切换，忽略嵌套TabControl的事件
            if (e.Source != sender) return;
            
            if (sender is TabControl tabControl && DataContext is ViewModels.TestTask.CardCATPanel.ART1553BConfigPanelViewModel viewModel)
            {
                var selectedTab = tabControl.SelectedItem as TabItem;
                if (selectedTab != null)
                {
                    // 根据Tab标题确定通道号
                    int newChannel = -1;
                    if (selectedTab.Header.ToString().Contains("通道0"))
                    {
                        newChannel = 0;
                    }
                    else if (selectedTab.Header.ToString().Contains("通道1"))
                    {
                        newChannel = 1;
                    }
                    
                    if (newChannel >= 0 && viewModel.SelectedChannel != newChannel)
                    {
                        System.Diagnostics.Debug.WriteLine($"[ART1553B UI] 通道切换: {viewModel.SelectedChannel} -> {newChannel}");
                        viewModel.SelectedChannel = newChannel;
                    }
                }
            }
        }

        private void BCMessageDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {

        }
    }
}
