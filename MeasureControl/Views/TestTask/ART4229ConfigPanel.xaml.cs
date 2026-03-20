using MeasureControl.ViewModels.TestTask;
using MeasureControl.Helpers;
using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MeasureControl.Views.TestTask
{
    /// <summary>
    /// ART4229ConfigPanel.xaml 的交互逻辑
    /// </summary>
    public partial class ART4229ConfigPanel : UserControl
    {
        public ART4229ConfigPanel()
        {
            InitializeComponent();
        }

        private void MenuOpenChannel_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var menuItem = sender as MenuItem;
                if (menuItem == null) return;
                
                var ctx = menuItem.Parent as ContextMenu ?? (menuItem.CommandTarget as ContextMenu);
                if (ctx == null) return;
                
                var rb = ctx.PlacementTarget as RadioButton;
                if (rb == null) return;
                
                var channelData = rb.DataContext;
                if (channelData == null) return;
                
                // 获取通道索引
                var idxProp = channelData.GetType().GetProperty("ChannelIndex");
                if (idxProp == null) return;
                var channelIndex = idxProp.GetValue(channelData);

                // 获取 IsTx 属性
                var isTxProp = channelData.GetType().GetProperty("IsTx");
                var isTx = isTxProp != null && (bool)isTxProp.GetValue(channelData);

                Debug.WriteLine($"[ART4229 UI] 打开通道命令: Index={channelIndex}, IsTx={isTx}");

                // 获取 ViewModel 并执行命令
                if (this.DataContext is ART4229ConfigPanelViewModel vm)
                {
                    var channelStatus = channelData as Art4229ChannelStatus;
                    if (channelStatus != null)
                    {
                        vm.OpenChannelCommand.Execute(channelStatus);
                        Debug.WriteLine($"[ART4229 UI] 调用VeiwModel中的打开通道命令");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ART4229 UI] 打开通道异常: {ex.Message}");
            }
        }

        private void MenuCloseChannel_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var menuItem = sender as MenuItem;
                if (menuItem == null) return;
                
                var ctx = menuItem.Parent as ContextMenu ?? (menuItem.CommandTarget as ContextMenu);
                if (ctx == null) return;
                
                var rb = ctx.PlacementTarget as RadioButton;
                if (rb == null) return;
                
                var channelData = rb.DataContext;
                if (channelData == null) return;
                
                // 获取通道索引
                var idxProp = channelData.GetType().GetProperty("ChannelIndex");
                if (idxProp == null) return;
                var channelIndex = idxProp.GetValue(channelData);

                Debug.WriteLine($"[ART4229 UI] 关闭通道命令: Index={channelIndex}");

                // 获取 ViewModel 并执行命令
                if (this.DataContext is ART4229ConfigPanelViewModel vm)
                {
                    var channelStatus = channelData as Art4229ChannelStatus;
                    if (channelStatus != null)
                    {
                        vm.CloseChannelCommand.Execute(channelStatus);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ART4229 UI] 关闭通道异常: {ex.Message}");
            }
        }
    }
}
