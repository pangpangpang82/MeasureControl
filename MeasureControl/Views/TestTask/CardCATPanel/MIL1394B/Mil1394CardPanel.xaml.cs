using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MeasureControl.Drivers;
using MeasureControl.ViewModels.TestTask;
using MeasureControl.ViewModels.TestTask.CardCATPanel.MIL1394B;

namespace MeasureControl.Views.TestTask.CardCATPanel.Mil1394B
{
    /// <summary>
    /// Mil1394CardPanel.xaml 的交互逻辑
    /// WPF版本的1394B板卡界面，替代WinForms的Form_Card_Num
    /// </summary>
    public partial class Mil1394CardPanel : UserControl
    {
        private readonly uint _cardNum;
        private readonly uint _nodeCount;
        private readonly IntPtr[] _pnode;
        private readonly Mil1394CardPanelViewModel _viewModel;
        private readonly DispatcherTimer _dataCountRefreshTimer;
        private readonly List<Mil1394NodeDataCountPanelViewModel> _nodeDataCountViewModels;

        /// <summary>
        /// 切换显示节点配置或数据收发界面
        /// </summary>
        public void SwitchToTab(int tabIndex)
        {
            if (tabIndex == 0) // 节点配置
            {
                NodeConfigContent.Visibility = Visibility.Visible;
                DataTransferContent.Visibility = Visibility.Collapsed;
            }
            else if (tabIndex == 1) // 数据收发
            {
                NodeConfigContent.Visibility = Visibility.Collapsed;
                DataTransferContent.Visibility = Visibility.Visible;
            }
        }

        public Mil1394CardPanel(uint cardNum, uint[] cardNodeNum, Mil1394CardPanelViewModel viewModel)
        {
            InitializeComponent();
            _cardNum = cardNum;
            _nodeCount = cardNodeNum[cardNum];
            _pnode = new IntPtr[_nodeCount];
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            _nodeDataCountViewModels = new List<Mil1394NodeDataCountPanelViewModel>();

            DataContext = _viewModel;

            // 初始化数据计数刷新定时器
            _dataCountRefreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100) // 100ms刷新一次
            };
            _dataCountRefreshTimer.Tick += DataCountRefreshTimer_Tick;
            _dataCountRefreshTimer.Start();
        }

        private void DataCountRefreshTimer_Tick(object sender, EventArgs e)
        {
            RefreshNodeDataCounts();
        }

        private void RefreshNodeDataCounts()
        {
            try
            {
                for (uint i = 0; i < _nodeCount && i < _nodeDataCountViewModels.Count && i < _pnode.Length; i++)
                {
                    if (_pnode[i] == IntPtr.Zero) continue;

                    var data = _nodeDataCountViewModels[(int)i]?.GetDataCounts(_pnode[i]);
                    if (data != null && data.Length >= 18)
                    {
                        UpdateNodeDataCountUI(i, data);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"刷新节点数据计数失败: {ex.Message}");
            }
        }

        private void UpdateNodeDataCountUI(uint nodeIndex, uint[] data)
        {
            try
            {
                switch (nodeIndex)
                {
                    case 0:
                        Node0DataBusResetCount.Text = data[0].ToString();
                        Node0DataSTOFSendCount.Text = data[1].ToString();
                        Node0DataSTOFReceiveCount.Text = data[2].ToString();
                        Node0DataSendMessageCount.Text = data[3].ToString();
                        Node0DataReceiveMessageCount.Text = data[4].ToString();
                        Node0DataReceiveHCRCErrorCount.Text = data[5].ToString();
                        Node0DataReceiveMSGIDErrorCount.Text = data[6].ToString();
                        Node0DataReceiveDCRCErrorCount.Text = data[7].ToString();
                        Node0DataReceiveVPCErrorCount.Text = data[8].ToString();
                        Node0DataReceiveSTOFVPCErrorCount.Text = data[14].ToString();
                        Node0DataReceiveSTOFDataCRCError.Text = data[15].ToString();
                        Node0DataAsyncStreamMessageVPCErrorCount.Text = data[16].ToString();
                        Node0DataMessageLengthErrorCount.Text = data[17].ToString();
                        break;
                    case 1:
                        Node1DataBusResetCount.Text = data[0].ToString();
                        Node1DataSTOFSendCount.Text = data[1].ToString();
                        Node1DataSTOFReceiveCount.Text = data[2].ToString();
                        Node1DataSendMessageCount.Text = data[3].ToString();
                        Node1DataReceiveMessageCount.Text = data[4].ToString();
                        Node1DataReceiveHCRCErrorCount.Text = data[5].ToString();
                        Node1DataReceiveMSGIDErrorCount.Text = data[6].ToString();
                        Node1DataReceiveDCRCErrorCount.Text = data[7].ToString();
                        Node1DataReceiveVPCErrorCount.Text = data[8].ToString();
                        Node1DataReceiveSTOFVPCErrorCount.Text = data[14].ToString();
                        Node1DataReceiveSTOFDataCRCError.Text = data[15].ToString();
                        Node1DataAsyncStreamMessageVPCErrorCount.Text = data[16].ToString();
                        Node1DataMessageLengthErrorCount.Text = data[17].ToString();
                        break;
                    case 2:
                        Node2DataBusResetCount.Text = data[0].ToString();
                        Node2DataSTOFSendCount.Text = data[1].ToString();
                        Node2DataSTOFReceiveCount.Text = data[2].ToString();
                        Node2DataSendMessageCount.Text = data[3].ToString();
                        Node2DataReceiveMessageCount.Text = data[4].ToString();
                        Node2DataReceiveHCRCErrorCount.Text = data[5].ToString();
                        Node2DataReceiveMSGIDErrorCount.Text = data[6].ToString();
                        Node2DataReceiveDCRCErrorCount.Text = data[7].ToString();
                        Node2DataReceiveVPCErrorCount.Text = data[8].ToString();
                        Node2DataReceiveSTOFVPCErrorCount.Text = data[14].ToString();
                        Node2DataReceiveSTOFDataCRCError.Text = data[15].ToString();
                        Node2DataAsyncStreamMessageVPCErrorCount.Text = data[16].ToString();
                        Node2DataMessageLengthErrorCount.Text = data[17].ToString();
                        break;
                    case 3:
                        Node3DataBusResetCount.Text = data[0].ToString();
                        Node3DataSTOFSendCount.Text = data[1].ToString();
                        Node3DataSTOFReceiveCount.Text = data[2].ToString();
                        Node3DataSendMessageCount.Text = data[3].ToString();
                        Node3DataReceiveMessageCount.Text = data[4].ToString();
                        Node3DataReceiveHCRCErrorCount.Text = data[5].ToString();
                        Node3DataReceiveMSGIDErrorCount.Text = data[6].ToString();
                        Node3DataReceiveDCRCErrorCount.Text = data[7].ToString();
                        Node3DataReceiveVPCErrorCount.Text = data[8].ToString();
                        Node3DataReceiveSTOFVPCErrorCount.Text = data[14].ToString();
                        Node3DataReceiveSTOFDataCRCError.Text = data[15].ToString();
                        Node3DataAsyncStreamMessageVPCErrorCount.Text = data[16].ToString();
                        Node3DataMessageLengthErrorCount.Text = data[17].ToString();
                        break;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"更新节点{nodeIndex}数据计数UI失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 初始化节点配置Tab
        /// </summary>
        public void InitializeNodeConfigTabs(List<UserControl> nodeConfigPanels)
        {
            // 优先操作 ItemsSource（如果被绑定到集合），以避免在 ItemsSource 正在使用时直接操作 Items 导致的 InvalidOperationException
            var nodeConfigItems = NodeConfigTabControl.ItemsSource as System.Collections.IList;
            if (nodeConfigItems != null)
            {
                nodeConfigItems.Clear();
                for (uint i = 0; i < nodeConfigPanels.Count && i < _nodeCount; i++)
                {
                    var tabItem = new TabItem
                    {
                        Header = $"节点 {i}",
                        Content = nodeConfigPanels[(int)i]
                    };
                    nodeConfigItems.Add(tabItem);
                }
            }
            else
            {
                NodeConfigTabControl.Items.Clear();
                for (uint i = 0; i < nodeConfigPanels.Count && i < _nodeCount; i++)
                {
                    var tabItem = new TabItem
                    {
                        Header = $"节点 {i}",
                        Content = nodeConfigPanels[(int)i]
                    };
                    NodeConfigTabControl.Items.Add(tabItem);
                }
            }

            // 添加Tab切换事件，保存和恢复配置状态
            NodeConfigTabControl.SelectionChanged += NodeConfigTabControl_SelectionChanged;
        }

        /// <summary>
        /// 节点配置Tab切换事件处理
        /// </summary>
        private void NodeConfigTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                // 保存之前Tab的配置状态
                if (e.RemovedItems != null && e.RemovedItems.Count > 0)
                {
                    var previousTabItem = e.RemovedItems[0] as TabItem;
                    if (previousTabItem?.Content is Mil1394NodeConfigPanel previousPanel)
                    {
                        previousPanel.SaveConfigState();
                    }
                }

                // 恢复当前Tab的配置状态
                if (e.AddedItems != null && e.AddedItems.Count > 0)
                {
                    var currentTabItem = e.AddedItems[0] as TabItem;
                    if (currentTabItem?.Content is Mil1394NodeConfigPanel currentPanel)
                    {
                        // 如果已经加载过，直接恢复配置状态
                        if (currentPanel.IsLoaded)
                        {
                            currentPanel.RestoreConfigState();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"NodeConfigTabControl_SelectionChanged异常: {ex}");
            }
        }

        /// <summary>
        /// 初始化数据收发Tab
        /// </summary>
        public void InitializeDataTransferTabs(List<UserControl> nodeSendRcvPanels, List<Mil1394NodeDataCountPanelViewModel> nodeDataCountViewModels)
        {
            // 初始化节点收发Tab
            // 优先操作 ItemsSource（如果被绑定到集合），以避免在 ItemsSource 正在使用时直接操作 Items 导致的 InvalidOperationException
            var dataTransferItems = NodeSendRcvTabControl.ItemsSource as System.Collections.IList;
            if (dataTransferItems != null)
            {
                dataTransferItems.Clear();
                for (uint i = 0; i < nodeSendRcvPanels.Count && i < _nodeCount; i++)
                {
                    var tabItem = new TabItem
                    {
                        Header = $"节点 {i}",
                        Content = nodeSendRcvPanels[(int)i]
                    };
                    dataTransferItems.Add(tabItem);
                }
            }
            else
            {
                NodeSendRcvTabControl.Items.Clear();
                for (uint i = 0; i < nodeSendRcvPanels.Count && i < _nodeCount; i++)
                {
                    var tabItem = new TabItem
                    {
                        Header = $"节点 {i}",
                        Content = nodeSendRcvPanels[(int)i]
                    };
                    NodeSendRcvTabControl.Items.Add(tabItem);
                }
            }

            // 保存节点数据计数ViewModel引用
            _nodeDataCountViewModels.Clear();
            _nodeDataCountViewModels.AddRange(nodeDataCountViewModels);
        }


        /// <summary>
        /// 设置节点句柄
        /// </summary>
        public void SetNodeHandle(uint nodeIndex, IntPtr handle)
        {
            if (nodeIndex < _pnode.Length)
            {
                _pnode[nodeIndex] = handle;
            }
        }

        /// <summary>
        /// 停止数据计数刷新定时器
        /// </summary>
        public void StopDataCountRefreshTimer()
        {
            try
            {
                _dataCountRefreshTimer?.Stop();
                System.Diagnostics.Debug.WriteLine($"[Mil1394CardPanel] 数据计数刷新定时器已停止");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Mil1394CardPanel] 停止定时器失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 左侧节点数据区域鼠标滚轮事件处理
        /// 确保鼠标滚轮可以滚动该区域，即使鼠标在内层ScrollViewer上
        /// </summary>
        private void LeftNodeDataScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is ScrollViewer scrollViewer)
            {
                // 如果内容可以滚动，则处理滚轮事件
                if (scrollViewer.ComputedVerticalScrollBarVisibility == Visibility.Visible)
                {
                    // 计算滚动偏移量（e.Delta > 0 向上滚动，< 0 向下滚动）
                    double offset = scrollViewer.VerticalOffset - (e.Delta / 120.0 * 20); // 每次滚动20像素
                    
                    // 限制滚动范围
                    if (offset < 0)
                        offset = 0;
                    else if (offset > scrollViewer.ScrollableHeight)
                        offset = scrollViewer.ScrollableHeight;
                    
                    // 执行滚动
                    scrollViewer.ScrollToVerticalOffset(offset);
                    
                    // 标记事件已处理，防止事件继续向上冒泡或被内层ScrollViewer处理
                    e.Handled = true;
                }
            }
        }
    }
}
