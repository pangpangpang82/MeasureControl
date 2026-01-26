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
using MeasureControl.Helpers;
using MeasureControl.Services;
using MeasureControl.ViewModels;
using MeasureControl.ViewModels.Common;

namespace MeasureControl.Views.ConfigTabel
{
    /// <summary>
    /// ChannelConfigTabel.xaml 的交互逻辑
    /// </summary>
    public partial class ChannelConfigTabel : UserControl
    {
        private bool _isFloating = false;
        private Image _floatButtonImage;
        private string _currentPageKey = null;

        public ChannelConfigTabel()
        {
            InitializeComponent();
            Loaded += ChannelConfigTabel_Loaded;
        }

        private void ChannelConfigTabel_Loaded(object sender, RoutedEventArgs e)
        {
            _floatButtonImage = FindName("FloatImage") as Image;
        }

        public void OnFloatButtonClick(object sender, RoutedEventArgs e)
        {
            if (_isFloating)
            {
                EmbedWindow();
            }
            else
            {
                FloatWindow();
            }
        }

        private void FloatWindow()
        {
            var vm = DataContext as ChannelConfigTabelViewModel;
            // 使用PageType，GeneratePageKey会自动从ViewModel.ConfigId获取实例标识
            string pageName = "ChannelConfigTabel";

            // 获取RegionManager、EventAggregator、NavigationStateService、NavigationService和MainWindowViewModel
            var containerProvider = (Application.Current as App)?.Container;
            var regionManager = containerProvider?.Resolve(typeof(Prism.Regions.IRegionManager)) as Prism.Regions.IRegionManager;
            var eventAggregator = containerProvider?.Resolve(typeof(Prism.Events.IEventAggregator)) as Prism.Events.IEventAggregator;
            var navigationState = containerProvider?.Resolve(typeof(INavigationStateService)) as INavigationStateService;
            var navigationService = containerProvider?.Resolve(typeof(INavigationService)) as INavigationService;
            var mainViewModel = containerProvider?.Resolve(typeof(MainWindowViewModel)) as MainWindowViewModel;

            if (regionManager != null && eventAggregator != null && navigationState != null && mainViewModel != null)
            {
                // 诊断日志：检查 ViewModel 属性
                
                // 从 ViewModel 构建正确的 PageKey：PageType_任务名-配置表名
                string explicitPageKey = null;
                if (vm != null && !string.IsNullOrEmpty(vm.TestTaskName) && !string.IsNullOrEmpty(vm.ConfigTabelName))
                {
                    explicitPageKey = $"{pageName}_{vm.TestTaskName}-{vm.ConfigTabelName}";
                }
                else
                {
                }
                
                // 通过Helper浮动整个页面，传递明确的PageKey
                _currentPageKey = FloatingWindowHelper.FloatPage(
                    pageName,
                    this,
                    regionManager,
                    eventAggregator,
                    navigationState,
                    (nextPage) => mainViewModel.NavigateToPage(nextPage),
                    navigationService,
                    explicitPageKey
                );
                
                if (!string.IsNullOrEmpty(_currentPageKey))
                {
                    _isFloating = true;
                    UpdateFloatIcon();
                }
            }
        }

        private void EmbedWindow()
        {
            if (string.IsNullOrEmpty(_currentPageKey))
            {
                return;
            }

            // 获取RegionManager、EventAggregator和NavigationStateService
            var containerProvider = (Application.Current as App)?.Container;
            var regionManager = containerProvider?.Resolve(typeof(Prism.Regions.IRegionManager)) as Prism.Regions.IRegionManager;
            var eventAggregator = containerProvider?.Resolve(typeof(Prism.Events.IEventAggregator)) as Prism.Events.IEventAggregator;
            var navigationState = containerProvider?.Resolve(typeof(INavigationStateService)) as INavigationStateService;

            if (regionManager != null && eventAggregator != null)
            {
                // 通过Helper嵌入页面，使用保存的pageKey
                if (FloatingWindowHelper.EmbedPage(_currentPageKey, regionManager, eventAggregator, navigationState))
                {
                    _currentPageKey = null;
                    _isFloating = false;
                    UpdateFloatIcon();
                }
            }
        }

        public void OnMinimizeButtonClick(object sender, RoutedEventArgs e)
        {
            // 查找当前所在的窗口（可能是主窗口或浮动窗口）
            Window window = Window.GetWindow(this);
            if (window != null)
            {
                // 在浮动窗口中执行最小化
                if (window.GetType().Name == "FloatingWindow")
                {
                    // 调用FloatingWindowViewModel的MinimizeCommand处理最小化逻辑
                    var floatingVM = window.DataContext as FloatingWindowViewModel;
                    floatingVM?.MinimizeCommand.Execute();
                }
                else
                {
                    // 在嵌入模式下，隐藏当前视图（不需要提示框）
                    var eventAggregator = (Application.Current as App)?.Container?.Resolve(typeof(Prism.Events.IEventAggregator)) as Prism.Events.IEventAggregator;
                    eventAggregator?.GetEvent<MeasureControl.Events.HideCurrentPageEvent>().Publish(new MeasureControl.Events.HideCurrentPageEventArgs { IsMinimize = true });
                }
            }
        }

        public void OnCloseButtonClick(object sender, RoutedEventArgs e)
        {
            // 检查是否在浮动窗口中
            Window window = Window.GetWindow(this);
            if (window != null && window.GetType().Name == "FloatingWindow")
            {
                // 在浮动窗口中，直接关闭窗口（FloatingWindowViewModel会处理确认对话框）
                var floatingVM = window.DataContext as FloatingWindowViewModel;
                floatingVM?.CloseCommand.Execute();
            }
            else
            {
                // 在嵌入模式下，调用ViewModel的关闭命令
                var vm = DataContext as ChannelConfigTabelViewModel;
                vm?.CloseInRegionCommand.Execute();
            }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 只在浮动窗口时支持拖动
            Window window = Window.GetWindow(this);
            if (window != null && window.GetType().Name == "FloatingWindow")
            {
                if (e.ChangedButton == MouseButton.Left)
                {
                    window.DragMove();
                }
            }
        }

        /// <summary>
        /// 更新浮动按钮图标
        /// </summary>
        private void UpdateFloatIcon()
        {
            if (_floatButtonImage != null)
            {
                if (_isFloating)
                {
                    // 浮动时显示嵌入图标
                    _floatButtonImage.Source = new BitmapImage(new Uri("/Resources/Logo/embed.png", UriKind.Relative));
                    _floatButtonImage.Width = 15;
                }
                else
                {
                    // 嵌入时显示浮动图标
                    _floatButtonImage.Source = new BitmapImage(new Uri("/Resources/Logo/float.png", UriKind.Relative));
                    _floatButtonImage.Width = 15;
                }
            }
        }

        /// <summary>
        /// 通道树节点双击事件处理程序
        /// </summary>
        private void ChannelTreeViewItem_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // 防止事件冒泡到父节点
            if (e.Handled) return;

            var treeViewItem = sender as TreeViewItem;
            if (treeViewItem == null) return;

            var node = treeViewItem.DataContext as ChannelTreeNode;
            if (node == null) return;

            // 只有叶子节点（通道）才能双击添加
            if (node.NodeType == "Channel" && (node.Children == null || node.Children.Count == 0))
            {
                var vm = DataContext as ChannelConfigTabelViewModel;
                if (vm?.AddChannelFromTreeCommand?.CanExecute(node) == true)
                {
                    vm.AddChannelFromTreeCommand.Execute(node);
                    e.Handled = true;
                }
            }
        }

        /// <summary>
        /// 通道树节点Border单击事件处理程序 - 用于展开/折叠非叶子节点（参考主界面项目树的逻辑）
        /// </summary>
        private void ChannelTreeViewItem_Border_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border)
            {
                var treeViewItem = FindAncestor<TreeViewItem>(border);
                if (treeViewItem?.HasItems == true)
                {
                    var node = treeViewItem.DataContext as ChannelTreeNode;
                    if (node != null && node.Children != null && node.Children.Count > 0)
                    {
                        var vm = DataContext as ChannelConfigTabelViewModel;
                        if (vm?.ToggleTreeNodeCommand?.CanExecute(node) == true)
                        {
                            vm.ToggleTreeNodeCommand.Execute(node);
                            e.Handled = true;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 通道树节点单击事件处理程序 - 用于展开/折叠非叶子节点
        /// </summary>
        private void ChannelTreeViewItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var treeViewItem = sender as TreeViewItem;
            if (treeViewItem == null) return;

            var node = treeViewItem.DataContext as ChannelTreeNode;
            if (node == null) return;

            // 检查点击位置是否在展开/折叠箭头区域
            // 通过检查鼠标位置相对于TreeViewItem的位置来判断
            var position = e.GetPosition(treeViewItem);
            var header = treeViewItem.Template.FindName("PART_Header", treeViewItem) as FrameworkElement;

            if (header != null)
            {
                // 如果点击位置在左侧16像素内（箭头区域），则处理展开/折叠
                if (position.X <= 16 && node.Children != null && node.Children.Count > 0)
                {
                    var vm = DataContext as ChannelConfigTabelViewModel;
                    if (vm?.ToggleTreeNodeCommand?.CanExecute(node) == true)
                    {
                        vm.ToggleTreeNodeCommand.Execute(node);
                        e.Handled = true;
                    }
                }
            }
        }

        /// <summary>
        /// 查找可视树中的祖先元素
        /// </summary>
        private static T FindAncestor<T>(DependencyObject current) where T : DependencyObject
        {
            while (current != null && !(current is T))
            {
                current = VisualTreeHelper.GetParent(current);
            }
            return current as T;
        }
    }
}
