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
using System.Windows.Threading;
using MeasureControl.Helpers;
using MeasureControl.Models;
using MeasureControl.Models.Devices;
using MeasureControl.Services;
using MeasureControl.ViewModels;
using MeasureControl.ViewModels.Common;
using MeasureControl.ViewModels.SingleBoardTest;
using MeasureControl.Views.Dialogs;
using static MeasureControl.ViewModels.SingleBoardTest.BoardTestViewModel;

namespace MeasureControl.Views.SingleBoardTest
{
    /// <summary>
    /// BoardTest.xaml 的交互逻辑
    /// </summary>
    public partial class BoardTest : UserControl
    {
        private bool _isFloating = false;
        private Image _floatButtonImage;
        private string _currentPageKey = null;

        private Prism.Events.IEventAggregator _eventAggregator;
        private Prism.Events.SubscriptionToken _pageFloatedToken;
        private Prism.Events.SubscriptionToken _pageEmbeddedToken;

        private Point _cardDragStartPoint;
        private DeviceBase _cardDragSourceDevice;
        private InsertLineAdorner _insertLineAdorner;
        private AdornerLayer _insertLineAdornerLayer;
        private UIElement _insertLineAdornerTarget;
        private bool _insertLineIsBottom;

        private static string GetCardDisplayName(DeviceBase device)
        {
            if (device == null) return string.Empty;

            if (!string.IsNullOrWhiteSpace(device.CardName)) return device.CardName;
            if (!string.IsNullOrWhiteSpace(device.DisplayName)) return device.DisplayName;
            if (!string.IsNullOrWhiteSpace(device.Name)) return device.Name;
            if (!string.IsNullOrWhiteSpace(device.Model)) return device.Model;
            return string.Empty;
        }

        public BoardTest()
        {
            InitializeComponent();
            Loaded += PxiChassis_Loaded;
            Unloaded += BoardTest_Unloaded;
        }

        private void TestSequenceScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (!(sender is ScrollViewer scrollViewer))
            {
                return;
            }

            // 左侧“测试序列”区域强制使用纵向滚动，并阻止事件继续冒泡到其他可能启用横向滚轮映射的ScrollViewer
            double scrollAmount = e.Delta > 0 ? -50 : 50;
            scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset + scrollAmount);
            e.Handled = true;
        }

        private void TestSequenceItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not ListBoxItem item)
            {
                return;
            }

            if (item.DataContext is not TestSequenceItem nextItem)
            {
                return;
            }

            if (DataContext is not BoardTestViewModel vm)
            {
                return;
            }

            if (!vm.TryHandlePreviewSelection(nextItem))
            {
                e.Handled = true;
            }
        }

        private void PxiChassis_Loaded(object sender, RoutedEventArgs e)
        {
            _floatButtonImage = FindName("FloatImage") as Image;

            if (_eventAggregator == null)
            {
                var containerProvider = (Application.Current as App)?.Container;
                _eventAggregator = containerProvider?.Resolve(typeof(Prism.Events.IEventAggregator)) as Prism.Events.IEventAggregator;
            }

            if (_eventAggregator != null)
            {
                _pageFloatedToken ??= _eventAggregator.GetEvent<MeasureControl.Events.PageFloatedEvent>()
                    ?.Subscribe(OnPageFloated, Prism.Events.ThreadOption.UIThread);
                _pageEmbeddedToken ??= _eventAggregator.GetEvent<MeasureControl.Events.PageEmbeddedEvent>()
                    ?.Subscribe(OnPageEmbedded, Prism.Events.ThreadOption.UIThread);
            }

            // 初始状态刷新一次图标
            UpdateFloatIcon();
        }

        private void BoardTest_Unloaded(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_eventAggregator != null)
                {
                    if (_pageFloatedToken != null)
                    {
                        _eventAggregator.GetEvent<MeasureControl.Events.PageFloatedEvent>()?.Unsubscribe(_pageFloatedToken);
                        _pageFloatedToken = null;
                    }

                    if (_pageEmbeddedToken != null)
                    {
                        _eventAggregator.GetEvent<MeasureControl.Events.PageEmbeddedEvent>()?.Unsubscribe(_pageEmbeddedToken);
                        _pageEmbeddedToken = null;
                    }
                }
            }
            catch
            {
            }
        }

        private void OnPageFloated(MeasureControl.Events.PageFloatedEventArgs args)
        {
            var vm = DataContext as BoardTestViewModel;
            var expectedKey = vm?.PageKey;
            if (string.IsNullOrEmpty(expectedKey))
            {
                return;
            }

            if (!string.Equals(args?.PageName, expectedKey, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _currentPageKey = args.PageName;
            _isFloating = true;
            UpdateFloatIcon();
        }

        private void OnPageEmbedded(MeasureControl.Events.PageEmbeddedEventArgs args)
        {
            var vm = DataContext as BoardTestViewModel;
            var expectedKey = vm?.PageKey;
            if (string.IsNullOrEmpty(expectedKey))
            {
                return;
            }

            if (!string.Equals(args?.PageName, expectedKey, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _currentPageKey = null;
            _isFloating = false;
            UpdateFloatIcon();
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
            var vm = DataContext as BoardTestViewModel;
            string pageName = "BoardTest";

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

                // 基于 ViewModel 构建正确的 PageKey（与其他配置表页面保持一致）
                string explicitPageKey = vm?.PageKey;

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
                var vm = DataContext as BoardTestViewModel;
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

        private void TreeViewItem_Selected(object sender, RoutedEventArgs e)
        {
            if (sender is TreeViewItem tvi)
            {
                tvi.IsSelected = true;
                e.Handled = true;
            }
        }

        private void Border_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            // 处理拖拽开始
            if (sender is Border border && border.DataContext is ProjectItem projectItem)
            {
                if (projectItem.Children.Count == 0) // 只有叶子节点可以拖拽
                {
                    var dragData = new DataObject("ProjectItem", projectItem);
                    DragDrop.DoDragDrop(border, dragData, DragDropEffects.Copy);
                }
            }
        }

        private void Header_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            // 处理拖拽开始
            if (sender is ContentPresenter presenter && presenter.DataContext is ProjectItem projectItem)
            {
                if (projectItem.Children.Count == 0) // 只有叶子节点可以拖拽
                {
                    var dragData = new DataObject("ProjectItem", projectItem);
                    DragDrop.DoDragDrop(presenter, dragData, DragDropEffects.Copy);
                }
            }
        }

        private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 处理拖拽开始和展开/折叠
            if (sender is Border border && border.DataContext is ProjectItem projectItem)
            {
                // 查找对应的TreeViewItem
                var treeViewItem = FindParentTreeViewItem(border);
                if (treeViewItem != null)
                {
                    // 设置选中状态以保持高亮
                    treeViewItem.IsSelected = true;
                }

                // 如果有子节点，单击时切换展开/折叠状态
                if (projectItem.Children != null && projectItem.Children.Count > 0)
                {
                    if (treeViewItem != null)
                    {
                        // 切换展开/折叠状态
                        treeViewItem.IsExpanded = !treeViewItem.IsExpanded;
                        e.Handled = true;
                    }
                }
                else // 只有叶子节点可以拖拽
                {
                    var dragData = new DataObject("ProjectItem", projectItem);
                    DragDrop.DoDragDrop(border, dragData, DragDropEffects.Copy);
                }
            }
        }

        /// <summary>
        /// 查找Border所属的TreeViewItem
        /// </summary>
        private TreeViewItem FindParentTreeViewItem(DependencyObject child)
        {
            DependencyObject parent = VisualTreeHelper.GetParent(child);
            while (parent != null)
            {
                if (parent is TreeViewItem treeViewItem)
                {
                    return treeViewItem;
                }
                parent = VisualTreeHelper.GetParent(parent);
            }
            return null;
        }

        private void ChassisDevices_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent("ProjectItem"))
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void ChassisDevices_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent("ProjectItem"))
            {
                var projectItem = e.Data.GetData("ProjectItem") as ProjectItem;
                if (projectItem != null)
                {
                    var viewModel = DataContext as PxiChassisViewModel;
                    viewModel?.AddDeviceCommand?.Execute(projectItem);
                }
            }
        }

        private void ChassisDevices_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is ListBox listBox && listBox.SelectedItem is DeviceBase selectedDevice)
            {
                var viewModel = DataContext as PxiChassisViewModel;
                viewModel?.DeviceDoubleClickCommand?.Execute(selectedDevice);
            }
        }

        private void TreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (sender is TreeView treeView && treeView.SelectedItem is DeviceBase selectedDevice)
            {
                var viewModel = DataContext as PxiChassisViewModel;
                if (viewModel != null)
                {
                    viewModel.SelectedDevice = selectedDevice;
                }
            }
        }

        private void DeviceListBorder_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent("ProjectItem"))
            {
                e.Effects = DragDropEffects.Copy;
                // 高亮显示拖放区域 - 使用与HardwareConfig一致的半透明高亮色
                if (sender is Border border)
                {
                    border.Background = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromArgb(30, 100, 150, 200)); // 使用与HardwareConfig一致的半透明高亮色
                }
            }
            else if (e.Data.GetDataPresent("ChassisCard"))
            {
                e.Effects = DragDropEffects.Move;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void DeviceListBorder_DragLeave(object sender, DragEventArgs e)
        {
            // 取消高亮显示，恢复透明背景
            if (sender is Border border)
            {
                border.Background = System.Windows.Media.Brushes.Transparent;
            }

            ClearInsertLineAdorner();
        }

        private void DeviceListBorder_Drop(object sender, DragEventArgs e)
        {
            // 取消高亮显示，恢复透明背景
            if (sender is Border border)
            {
                border.Background = System.Windows.Media.Brushes.Transparent;
            }

            ClearInsertLineAdorner();

            if (e.Data.GetDataPresent("ProjectItem"))
            {
                var projectItem = e.Data.GetData("ProjectItem") as ProjectItem;
                if (projectItem != null)
                {
                    var viewModel = DataContext as PxiChassisViewModel;
                    viewModel?.AddDeviceCommand?.Execute(projectItem);
                }
            }
            else if (e.Data.GetDataPresent("ChassisCard"))
            {
                var dragged = e.Data.GetData("ChassisCard") as DeviceBase;
                if (dragged != null)
                {
                    var viewModel = DataContext as PxiChassisViewModel;
                    if (viewModel != null)
                    {
                        var slotPrompt = viewModel.GetSlotPromptAfterMoveToEnd(dragged);
                        if (!string.IsNullOrWhiteSpace(slotPrompt))
                        {
                            var cardName = GetCardDisplayName(dragged);
                            var result = ReMessageBox.Show($"是否移动{cardName}到 {slotPrompt}？", "确认", MessageBoxButton.YesNo, MessageBoxImage.Question);
                            if (result != MessageBoxResult.Yes)
                            {
                                e.Handled = true;
                                return;
                            }
                        }

                        viewModel.MoveChassisCardToEnd(dragged);
                    }
                }
            }
        }

        private void DeviceBorder_MouseLeftButtonDownForTripleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.Tag is DeviceBase device)
            {
                var viewModel = DataContext as PxiChassisViewModel;
                viewModel?.DeviceClickCommand?.Execute(device);

                // 点击主设备行（如"xx槽机箱"）时，清空板卡拖拽源，避免误触发拖拽
                _cardDragSourceDevice = null;
            }
        }

        private void CardRow_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.Tag is DeviceBase device)
            {
                _cardDragStartPoint = e.GetPosition(this);
                // 控制器永远不允许作为拖拽源（但仍允许作为放置目标）
                _cardDragSourceDevice = device is ControllerDevice ? null : device;

                var viewModel = DataContext as PxiChassisViewModel;
                viewModel?.DeviceClickCommand?.Execute(device);
            }
        }

        private void CardRow_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            if (_cardDragSourceDevice == null)
            {
                return;
            }

            if (_cardDragSourceDevice is ControllerDevice)
            {
                return;
            }

            var current = e.GetPosition(this);
            if (Math.Abs(current.X - _cardDragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(current.Y - _cardDragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            var dragData = new DataObject("ChassisCard", _cardDragSourceDevice);
            try
            {
                DragDrop.DoDragDrop((DependencyObject)sender as DependencyObject, dragData, DragDropEffects.Move);
            }
            finally
            {
                // 拖拽结束后清理，避免拖拽源残留
                _cardDragSourceDevice = null;
                ClearInsertLineAdorner();
            }
        }

        private void CardRow_DragOver(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent("ChassisCard"))
            {
                return;
            }

            var targetBorder = sender as Border;
            if (targetBorder == null)
            {
                return;
            }

            var targetDevice = targetBorder.Tag as DeviceBase;
            var dragged = e.Data.GetData("ChassisCard") as DeviceBase;
            if (targetDevice == null || dragged == null)
            {
                return;
            }

            e.Effects = DragDropEffects.Move;

            var pos = e.GetPosition(targetBorder);
            bool insertAfter = pos.Y >= targetBorder.ActualHeight / 2.0;
            if (targetDevice is ControllerDevice)
            {
                insertAfter = true;
            }

            ShowInsertLineAdorner(targetBorder, insertAfter);
            e.Handled = true;
        }

        private void CardRow_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent("ChassisCard"))
            {
                return;
            }

            var targetBorder = sender as Border;
            if (targetBorder == null)
            {
                return;
            }

            var targetDevice = targetBorder.Tag as DeviceBase;
            var dragged = e.Data.GetData("ChassisCard") as DeviceBase;
            if (targetDevice == null || dragged == null)
            {
                return;
            }

            var pos = e.GetPosition(targetBorder);
            bool insertAfter = pos.Y >= targetBorder.ActualHeight / 2.0;
            if (targetDevice is ControllerDevice)
            {
                insertAfter = true;
            }

            var viewModel = DataContext as PxiChassisViewModel;
            if (viewModel != null)
            {
                var slotPrompt = viewModel.GetSlotPromptAfterMove(dragged, targetDevice, insertAfter);
                if (!string.IsNullOrWhiteSpace(slotPrompt))
                {
                    var cardName = GetCardDisplayName(dragged);
                    var result = ReMessageBox.Show($"是否移动{cardName}到 {slotPrompt}？", "确认", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (result != MessageBoxResult.Yes)
                    {
                        ClearInsertLineAdorner();
                        e.Handled = true;
                        return;
                    }
                }

                viewModel.MoveChassisCard(dragged, targetDevice, insertAfter);
            }

            ClearInsertLineAdorner();
            e.Handled = true;
        }

        private void CardRow_DragLeave(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent("ChassisCard"))
            {
                return;
            }

            if (_insertLineAdornerTarget == sender)
            {
                ClearInsertLineAdorner();
            }
        }

        private void ShowInsertLineAdorner(UIElement target, bool isBottom)
        {
            if (target == null)
            {
                return;
            }

            if (_insertLineAdorner != null && _insertLineAdornerTarget == target && _insertLineIsBottom == isBottom)
            {
                return;
            }

            ClearInsertLineAdorner();

            var layer = AdornerLayer.GetAdornerLayer(target);
            if (layer == null)
            {
                return;
            }

            _insertLineAdorner = new InsertLineAdorner(target, isBottom);
            _insertLineAdornerLayer = layer;
            _insertLineAdornerTarget = target;
            _insertLineIsBottom = isBottom;
            layer.Add(_insertLineAdorner);
        }

        private void ClearInsertLineAdorner()
        {
            if (_insertLineAdorner != null && _insertLineAdornerLayer != null)
            {
                _insertLineAdornerLayer.Remove(_insertLineAdorner);
            }

            _insertLineAdorner = null;
            _insertLineAdornerLayer = null;
            _insertLineAdornerTarget = null;
            _insertLineIsBottom = false;
        }

        private sealed class InsertLineAdorner : Adorner
        {
            private readonly bool _isBottom;

            public InsertLineAdorner(UIElement adornedElement, bool isBottom) : base(adornedElement)
            {
                _isBottom = isBottom;
                IsHitTestVisible = false;
            }

            protected override void OnRender(DrawingContext drawingContext)
            {
                var rect = new Rect(AdornedElement.RenderSize);
                double y = _isBottom ? rect.Bottom : rect.Top;
                var pen = new Pen(Brushes.Red, 2);
                drawingContext.DrawLine(pen, new Point(rect.Left, y), new Point(rect.Right, y));
            }
        }

        /// <summary>
        /// 处理设备行右键点击事件
        /// </summary>
        private void DeviceBorder_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.DataContext is DeviceBase device)
            {
                var viewModel = DataContext as PxiChassisViewModel;
                if (viewModel != null)
                {
                    bool isEmptySlotPlaceholder = device?.Name == "空槽";

                    // 选中当前设备
                    viewModel.SelectedDevice = device;

                    // 创建右键菜单
                    var contextMenu = new ContextMenu();

                    // 应用自定义样式（从MainWindow资源中获取）
                    var mainWindow = Application.Current.MainWindow;
                    if (mainWindow?.Resources["CustomContextMenuStyle"] is Style contextMenuStyle)
                    {
                        contextMenu.Style = contextMenuStyle;
                    }

                    // 获取菜单项样式（只获取一次，避免重复定义）
                    Style menuItemStyle = null;
                    if (mainWindow?.Resources["CustomMenuItemStyle"] is Style style)
                    {
                        menuItemStyle = style;
                    }

                    // 空槽占位符：仅允许删除
                    if (isEmptySlotPlaceholder)
                    {
                        var deleteMenuItem = new MenuItem { Header = "删除" };
                        if (menuItemStyle != null) deleteMenuItem.Style = menuItemStyle;
                        deleteMenuItem.Click += (s, args) => viewModel.DeleteDeviceCommand?.Execute(device);
                        contextMenu.Items.Add(deleteMenuItem);
                    }
                    // 板卡（Card且不是ControllerDevice）：可以打开配置、重命名和删除
                    else if (device.DeviceType == "Card" && !(device is ControllerDevice))
                    {
                        // 添加"打开配置"菜单项（所有板卡统一使用打开配置，包括1394B）
                        var openConfigMenuItem = new MenuItem { Header = "打开配置" };
                        if (menuItemStyle != null) openConfigMenuItem.Style = menuItemStyle;
                        openConfigMenuItem.Click += (s, args) => viewModel.DeviceDoubleClickCommand?.Execute(device);
                        contextMenu.Items.Add(openConfigMenuItem);

                        // 添加"重命名"菜单项
                        var renameMenuItem = new MenuItem { Header = "重命名" };
                        if (menuItemStyle != null) renameMenuItem.Style = menuItemStyle;
                        renameMenuItem.Click += (s, args) => viewModel.RenameCardCommand?.Execute(device);
                        contextMenu.Items.Add(renameMenuItem);

                        // 添加"删除"菜单项
                        var deleteMenuItem = new MenuItem { Header = "删除" };
                        if (menuItemStyle != null) deleteMenuItem.Style = menuItemStyle;
                        deleteMenuItem.Click += (s, args) => viewModel.DeleteDeviceCommand?.Execute(device);
                        contextMenu.Items.Add(deleteMenuItem);
                    }
                    // Instrument 设备（DMM/FrequencyCounter/Oscilloscope/SignalGenerator）：可以打开配置和删除
                    else if (device.DeviceType == "Instrument" && !(device is ControllerDevice))
                    {
                        // 添加"打开配置"菜单项
                        var openConfigMenuItem = new MenuItem { Header = "打开配置" };
                        if (menuItemStyle != null) openConfigMenuItem.Style = menuItemStyle;
                        openConfigMenuItem.Click += (s, args) => viewModel.DeviceDoubleClickCommand?.Execute(device);
                        contextMenu.Items.Add(openConfigMenuItem);

                        // 添加"删除"菜单项
                        var deleteMenuItem = new MenuItem { Header = "删除" };
                        if (menuItemStyle != null) deleteMenuItem.Style = menuItemStyle;
                        deleteMenuItem.Click += (s, args) => viewModel.DeleteDeviceCommand?.Execute(device);
                        contextMenu.Items.Add(deleteMenuItem);
                    }
                    // 控制器（ControllerDevice）：只能删除（删除前会检查是否还有板卡），不能重命名
                    else if (device is ControllerDevice)
                    {
                        // 添加"删除"菜单项
                        var deleteMenuItem = new MenuItem { Header = "删除" };
                        if (menuItemStyle != null) deleteMenuItem.Style = menuItemStyle;
                        deleteMenuItem.Click += (s, args) => viewModel.DeleteDeviceCommand?.Execute(device);
                        contextMenu.Items.Add(deleteMenuItem);
                    }
                    // 机箱设备：允许删除（触发与非机箱区域相同的删除逻辑）
                    else if (device.DeviceType == "Chassis")
                    {
                        var deleteMenuItem = new MenuItem { Header = "删除" };
                        if (menuItemStyle != null) deleteMenuItem.Style = menuItemStyle;
                        deleteMenuItem.Click += (s, args) => viewModel.DeleteDeviceCommand?.Execute(device);
                        contextMenu.Items.Add(deleteMenuItem);
                    }

                    // 显示右键菜单
                    if (contextMenu.Items.Count > 0)
                    {
                        border.ContextMenu = contextMenu;
                        contextMenu.IsOpen = true;
                    }
                }

                e.Handled = true;
            }
        }

        /// <summary>
        /// 展开PXI工具树的一级目录
        /// </summary>
        public void ExpandPxiToolsTreeLevel2()
        {
            var treeView = FindName("PxiToolsTreeView") as TreeView;
            if (treeView == null)
            {
                // 如果找不到命名的TreeView，尝试通过遍历找到
                treeView = FindTreeViewInVisualTree(this);
            }

            if (treeView != null)
            {
                ExpandLevel2ItemsRecursive(treeView);
            }
        }

        /// <summary>
        /// 在可视化树中查找TreeView
        /// </summary>
        private TreeView FindTreeViewInVisualTree(DependencyObject parent)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is TreeView treeView)
                {
                    return treeView;
                }
                var found = FindTreeViewInVisualTree(child);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }

        /// <summary>
        /// 递归展开一级目录（只展开一级节点）
        /// </summary>
        private void ExpandLevel2ItemsRecursive(TreeView treeView)
        {
            foreach (var item in treeView.Items)
            {
                var treeViewItem = treeView.ItemContainerGenerator.ContainerFromItem(item) as TreeViewItem;
                if (treeViewItem != null)
                {
                    // 只展开一级节点，不展开二级节点
                    treeViewItem.IsExpanded = true;
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

    }
}

