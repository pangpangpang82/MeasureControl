using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Controls.Primitives;
using MeasureControl.Helpers;
using MeasureControl.Services;
using MeasureControl.ViewModels;
using System.IO;
using Newtonsoft.Json;
using MeasureControl.ViewModels.Common;
using MeasureControl.ViewModels.TestTask.ConfigTabel;

namespace MeasureControl.Views.ConfigTabel
{
    /// <summary>
    /// SignalConfigTabel.xaml 的交互逻辑
    /// </summary>
    public partial class SignalConfigTabel : UserControl
    {
        private bool _isFloating = false;
        private Image _floatButtonImage;
        private string _currentPageKey = null;

        // 非通讯变量列宽管理
        private const string ColumnWidthsFileName = "NonCommVariableColumnWidths.json";
        private Grid _headerGrid;
        private readonly List<Grid> _dataRowGrids = new List<Grid>();
        private bool _tabelInitialized;

        public SignalConfigTabel()
        {
            InitializeComponent();
            Loaded += SignalConfigTabel_Loaded;
        }

        private void SignalConfigTabel_Loaded(object sender, RoutedEventArgs e)
        {
            // 查找浮动按钮的Image，用于切换图标
            _floatButtonImage = FindName("FloatImage") as Image;

            if (_tabelInitialized) return;
            _tabelInitialized = true;

            _headerGrid = HeaderGrid;
            FindDataRowGrids();
            RestoreColumnWidths();
            SyncColumnWidths();
            AttachGridSplitterEvents();

            if (SignalsItemsControl != null)
            {
                SignalsItemsControl.ItemContainerGenerator.StatusChanged += ItemContainerGenerator_StatusChanged;
            }
        }

        /// <summary>
        /// 浮动按钮点击事件
        /// </summary>
        public void OnFloatButtonClick(object sender, RoutedEventArgs e)
        {
            if (_isFloating)
            {
                // 当前是浮动状态，执行嵌入操作
                EmbedWindow();
            }
            else
            {
                // 当前是嵌入状态，执行浮动操作
                FloatWindow();
            }
        }

        /// <summary>
        /// 浮动窗口
        /// </summary>
        private void FloatWindow()
        {
            var vm = DataContext as SignalConfigTabelViewModel;
            // 使用PageType，GeneratePageKey会自动从ViewModel.ConfigId获取实例标识
            string pageName = "SignalConfigTabel";

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

        /// <summary>
        /// 嵌入窗口
        /// </summary>
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

        /// <summary>
        /// 最小化按钮点击事件
        /// </summary>
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

        /// <summary>
        /// 关闭按钮点击事件
        /// </summary>

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
                var vm = DataContext as SignalConfigTabelViewModel;
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
                var vm = DataContext as SignalConfigTabelViewModel;
                if (vm?.AddSignalFromTreeCommand?.CanExecute(node) == true)
                {
                    vm.AddSignalFromTreeCommand.Execute(node);
                    e.Handled = true;
                }
            }
        }

        #region 非通讯变量表列宽同步

        private void ItemContainerGenerator_StatusChanged(object sender, EventArgs e)
        {
            if (SignalsItemsControl?.ItemContainerGenerator.Status == System.Windows.Controls.Primitives.GeneratorStatus.ContainersGenerated)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    _dataRowGrids.Clear();
                    FindGridsRecursive(this, _dataRowGrids);
                    SyncColumnWidths();
                }), System.Windows.Threading.DispatcherPriority.Loaded);
            }
        }

        private void FindDataRowGrids()
        {
            _dataRowGrids.Clear();
            Dispatcher.BeginInvoke(new Action(() =>
            {
                FindGridsRecursive(this, _dataRowGrids);
                SyncColumnWidths();
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void FindGridsRecursive(DependencyObject parent, List<Grid> grids)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is Grid grid && grid != _headerGrid && grid.ColumnDefinitions.Count == 10)
                {
                    if (!grids.Contains(grid))
                    {
                        grids.Add(grid);
                    }
                }
                FindGridsRecursive(child, grids);
            }
        }

        private void AttachGridSplitterEvents()
        {
            if (_headerGrid == null) return;
            FindGridSplittersRecursive(_headerGrid, splitter => { splitter.DragCompleted += GridSplitter_DragCompleted; });
        }

        private void FindGridSplittersRecursive(DependencyObject parent, Action<GridSplitter> action)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is GridSplitter splitter)
                {
                    action(splitter);
                }
                FindGridSplittersRecursive(child, action);
            }
        }

        private void GridSplitter_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            SyncColumnWidths();
            SaveColumnWidths();
        }

        private void SyncColumnWidths()
        {
            if (_headerGrid == null || _headerGrid.ColumnDefinitions.Count == 0) return;

            foreach (var dataGrid in _dataRowGrids)
            {
                if (dataGrid.ColumnDefinitions.Count != _headerGrid.ColumnDefinitions.Count) continue;

                for (int i = 0; i < _headerGrid.ColumnDefinitions.Count; i++)
                {
                    var headerCol = _headerGrid.ColumnDefinitions[i];
                    var dataCol = dataGrid.ColumnDefinitions[i];

                    dataCol.Width = headerCol.Width;
                    if (headerCol.MinWidth > 0)
                    {
                        dataCol.MinWidth = headerCol.MinWidth;
                    }
                }
            }
        }

        private void SaveColumnWidths()
        {
            if (_headerGrid == null) return;

            try
            {
                var widths = new List<double>();
                foreach (var colDef in _headerGrid.ColumnDefinitions)
                {
                    if (colDef.Width.IsStar)
                    {
                        widths.Add(colDef.Width.Value);
                    }
                    else if (colDef.Width.IsAbsolute)
                    {
                        widths.Add(colDef.Width.Value);
                    }
                    else
                    {
                        widths.Add(-1); // Auto
                    }
                }

                var settings = new
                {
                    Widths = widths,
                    UnitTypes = _headerGrid.ColumnDefinitions.Select(c =>
                        c.Width.IsStar ? "Star" :
                        c.Width.IsAbsolute ? "Pixel" : "Auto").ToArray()
                };

                var json = JsonConvert.SerializeObject(settings, Formatting.Indented);
                var filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ColumnWidthsFileName);
                File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"保存列宽失败: {ex.Message}");
            }
        }

        private void RestoreColumnWidths()
        {
            if (_headerGrid == null) return;

            try
            {
                var filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ColumnWidthsFileName);
                if (!File.Exists(filePath)) return;

                var json = File.ReadAllText(filePath);
                var settings = JsonConvert.DeserializeObject<dynamic>(json);

                if (settings?.Widths == null) return;

                var widths = ((Newtonsoft.Json.Linq.JArray)settings.Widths).ToObject<double[]>();
                var unitTypes = ((Newtonsoft.Json.Linq.JArray)settings.UnitTypes).ToObject<string[]>();

                if (widths == null || unitTypes == null || widths.Length != _headerGrid.ColumnDefinitions.Count) return;

                for (int i = 0; i < _headerGrid.ColumnDefinitions.Count && i < widths.Length; i++)
                {
                    var colDef = _headerGrid.ColumnDefinitions[i];
                    var width = widths[i];
                    var unitType = unitTypes[i];

                    if (width < 0) continue;

                    if (unitType == "Star")
                    {
                        colDef.Width = new GridLength(width, GridUnitType.Star);
                    }
                    else if (unitType == "Pixel")
                    {
                        colDef.Width = new GridLength(width, GridUnitType.Pixel);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"恢复列宽失败: {ex.Message}");
            }
        }

        #endregion
    }
}
