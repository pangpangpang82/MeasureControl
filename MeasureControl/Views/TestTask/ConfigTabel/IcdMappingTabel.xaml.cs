using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MeasureControl.Helpers;
using MeasureControl.Services;
using MeasureControl.ViewModels;
using MeasureControl.ViewModels.Common;

namespace MeasureControl.Views.ConfigTabel
{
    /// <summary>
    /// ICD映射表视图（沿用通讯变量表逻辑）
    /// </summary>
    public partial class IcdMappingTabel : UserControl
    {
        private bool _isFloating;
        private Image _floatButtonImage;
        private string _currentPageKey;

        // 通讯变量列宽与滚动管理
        private ScrollBar _horizontalScrollBar;
        private ScrollBar _verticalScrollBar;
        private bool _isUpdatingScrollBar;
        private bool _isInitialScrollPositionSet;
        private bool _isTabelInitialized;
        private const string ColumnWidthsFileName = "CommVariableColumnWidths.json";
        private Grid _headerGrid;
        private readonly List<Grid> _dataRowGrids = new List<Grid>();

        public IcdMappingTabel()
        {
            InitializeComponent();
            Loaded += CommunicatingSignalConfigTabel_Loaded;
        }

        private void CommunicatingSignalConfigTabel_Loaded(object sender, RoutedEventArgs e)
        {
            _floatButtonImage = FindName("FloatImage") as Image;

            if (_isTabelInitialized) return;
            _isTabelInitialized = true;

            // 初始化列宽管理
            _headerGrid = HeaderGrid;
            FindDataRowGrids();
            RestoreColumnWidths();
            SyncColumnWidths();
            AttachGridSplitterEvents();

            if (SignalsItemsControl != null)
            {
                SignalsItemsControl.ItemContainerGenerator.StatusChanged += ItemContainerGenerator_StatusChanged;
            }

            if (MainScrollViewer != null)
            {
                MainScrollViewer.ScrollChanged += MainScrollViewer_ScrollChanged;
                MainScrollViewer.Dispatcher.BeginInvoke(new Action(InitializeScrollBars), System.Windows.Threading.DispatcherPriority.Loaded);
            }
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
            var vm = DataContext as IcdMappingTabelViewModel;
            const string pageName = "IcdMappingTabel";

            var containerProvider = (Application.Current as App)?.Container;
            var regionManager = containerProvider?.Resolve(typeof(Prism.Regions.IRegionManager)) as Prism.Regions.IRegionManager;
            var eventAggregator = containerProvider?.Resolve(typeof(Prism.Events.IEventAggregator)) as Prism.Events.IEventAggregator;
            var navigationState = containerProvider?.Resolve(typeof(INavigationStateService)) as INavigationStateService;
            var navigationService = containerProvider?.Resolve(typeof(INavigationService)) as INavigationService;
            var mainViewModel = containerProvider?.Resolve(typeof(MainWindowViewModel)) as MainWindowViewModel;

            if (regionManager != null && eventAggregator != null && navigationState != null && mainViewModel != null)
            {
                string explicitPageKey = null;
                if (vm != null && !string.IsNullOrEmpty(vm.TestTaskName) && !string.IsNullOrEmpty(vm.ConfigTabelName))
                {
                    explicitPageKey = $"{pageName}_{vm.TestTaskName}-{vm.ConfigTabelName}";
                }

                _currentPageKey = FloatingWindowHelper.FloatPage(
                    pageName,
                    this,
                    regionManager,
                    eventAggregator,
                    navigationState,
                    nextPage => mainViewModel.NavigateToPage(nextPage),
                    navigationService,
                    explicitPageKey);

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

            var containerProvider = (Application.Current as App)?.Container;
            var regionManager = containerProvider?.Resolve(typeof(Prism.Regions.IRegionManager)) as Prism.Regions.IRegionManager;
            var eventAggregator = containerProvider?.Resolve(typeof(Prism.Events.IEventAggregator)) as Prism.Events.IEventAggregator;
            var navigationState = containerProvider?.Resolve(typeof(INavigationStateService)) as INavigationStateService;

            if (regionManager != null && eventAggregator != null)
            {
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
            Window window = Window.GetWindow(this);
            if (window != null)
            {
                if (window.GetType().Name == "FloatingWindow")
                {
                    var floatingVM = window.DataContext as FloatingWindowViewModel;
                    floatingVM?.MinimizeCommand.Execute();
                }
                else
                {
                    var eventAggregator = (Application.Current as App)?.Container?.Resolve(typeof(Prism.Events.IEventAggregator)) as Prism.Events.IEventAggregator;
                    eventAggregator?.GetEvent<MeasureControl.Events.HideCurrentPageEvent>().Publish(new MeasureControl.Events.HideCurrentPageEventArgs { IsMinimize = true });
                }
            }
        }

        public void OnCloseButtonClick(object sender, RoutedEventArgs e)
        {
            Window window = Window.GetWindow(this);
            if (window != null && window.GetType().Name == "FloatingWindow")
            {
                var floatingVM = window.DataContext as FloatingWindowViewModel;
                floatingVM?.CloseCommand.Execute();
            }
            else
            {
                var vm = DataContext as IcdMappingTabelViewModel;
                vm?.CloseInRegionCommand.Execute();
            }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            Window window = Window.GetWindow(this);
            if (window != null && window.GetType().Name == "FloatingWindow")
            {
                if (e.ChangedButton == MouseButton.Left)
                {
                    window.DragMove();
                }
            }
        }

        private void UpdateFloatIcon()
        {
            if (_floatButtonImage != null)
            {
                if (_isFloating)
                {
                    _floatButtonImage.Source = new BitmapImage(new Uri("/Resources/Logo/embed.png", UriKind.Relative));
                    _floatButtonImage.Width = 15;
                }
                else
                {
                    _floatButtonImage.Source = new BitmapImage(new Uri("/Resources/Logo/float.png", UriKind.Relative));
                    _floatButtonImage.Width = 15;
                }
            }
        }

        #region 通讯变量表布局逻辑（列宽+滚动同步）

        private void InitializeScrollBars()
        {
            _horizontalScrollBar = GetScrollBar(MainScrollViewer, Orientation.Horizontal);
            _verticalScrollBar = GetScrollBar(MainScrollViewer, Orientation.Vertical);

            if (_horizontalScrollBar != null)
            {
                _horizontalScrollBar.Value = 0;
                _horizontalScrollBar.ValueChanged += HorizontalScrollBar_ValueChanged;
            }

            if (_verticalScrollBar != null)
            {
                _verticalScrollBar.Value = 0;
                _verticalScrollBar.ValueChanged += VerticalScrollBar_ValueChanged;
            }

            // 确保初始位置在最左侧
            EnsureInitialScrollPosition();
        }

        private void EnsureInitialScrollPosition()
        {
            if (_isInitialScrollPositionSet) return;
            _isInitialScrollPositionSet = true;

            MainScrollViewer.ScrollToHorizontalOffset(0);
            MainScrollViewer.ScrollToVerticalOffset(0);

            _isUpdatingScrollBar = true;
            try
            {
                if (_horizontalScrollBar != null)
                {
                    _horizontalScrollBar.Value = 0;
                }
                if (_verticalScrollBar != null)
                {
                    _verticalScrollBar.Value = 0;
                }
            }
            finally
            {
                _isUpdatingScrollBar = false;
            }

            if (MainScrollViewer.ScrollableWidth > 0)
            {
                MainScrollViewer.LayoutUpdated += MainScrollViewer_LayoutUpdated;
            }
        }

        private void MainScrollViewer_LayoutUpdated(object sender, EventArgs e)
        {
            MainScrollViewer.LayoutUpdated -= MainScrollViewer_LayoutUpdated;

            if (MainScrollViewer.HorizontalOffset != 0)
            {
                MainScrollViewer.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (MainScrollViewer.HorizontalOffset != 0)
                    {
                        MainScrollViewer.ScrollToHorizontalOffset(0);
                        if (_horizontalScrollBar != null)
                        {
                            _isUpdatingScrollBar = true;
                            try
                            {
                                _horizontalScrollBar.Value = 0;
                            }
                            finally
                            {
                                _isUpdatingScrollBar = false;
                            }
                        }
                    }
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        private void HorizontalScrollBar_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isUpdatingScrollBar) return;
            MainScrollViewer.ScrollToHorizontalOffset(e.NewValue);
        }

        private void VerticalScrollBar_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isUpdatingScrollBar) return;
            MainScrollViewer.ScrollToVerticalOffset(e.NewValue);
        }

        private void MainScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (_isUpdatingScrollBar) return;

            _isUpdatingScrollBar = true;
            try
            {
                if (_horizontalScrollBar != null && e.HorizontalChange != 0)
                {
                    _horizontalScrollBar.Value = e.HorizontalOffset;
                }
                if (_verticalScrollBar != null && e.VerticalChange != 0)
                {
                    _verticalScrollBar.Value = e.VerticalOffset;
                }
            }
            finally
            {
                _isUpdatingScrollBar = false;
            }
        }

        private static ScrollBar GetScrollBar(ScrollViewer scrollViewer, Orientation orientation)
        {
            return scrollViewer.Template?.FindName(
                orientation == Orientation.Horizontal ? "PART_HorizontalScrollBar" : "PART_VerticalScrollBar",
                scrollViewer) as ScrollBar;
        }

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
                if (child is Grid grid && grid != _headerGrid && grid.ColumnDefinitions.Count == 27)
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

            var existingSplitters = new List<GridSplitter>();
            FindGridSplittersRecursive(_headerGrid, splitter =>
            {
                existingSplitters.Add(splitter);
                splitter.DragCompleted += GridSplitter_DragCompleted;
            });

            int columnCount = _headerGrid.ColumnDefinitions.Count;
            var columnsWithSplitters = existingSplitters.Select(s => Grid.GetColumn(s)).ToHashSet();

            for (int i = 0; i < columnCount - 1; i++)
            {
                if (!columnsWithSplitters.Contains(i))
                {
                    var splitter = new GridSplitter
                    {
                        Width = 3,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        VerticalAlignment = VerticalAlignment.Stretch,
                        Background = Brushes.Transparent,
                        ResizeDirection = GridResizeDirection.Columns,
                        ShowsPreview = true
                    };
                    Grid.SetColumn(splitter, i);
                    splitter.DragCompleted += GridSplitter_DragCompleted;
                    _headerGrid.Children.Add(splitter);
                }
            }
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

                var json = Newtonsoft.Json.JsonConvert.SerializeObject(settings, Newtonsoft.Json.Formatting.Indented);
                var filePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ColumnWidthsFileName);
                System.IO.File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"保存列宽失败: {ex.Message}");
            }
        }

        private void RestoreColumnWidths()
        {
            if (_headerGrid == null) return;

            try
            {
                var filePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ColumnWidthsFileName);
                if (!System.IO.File.Exists(filePath)) return;

                var json = System.IO.File.ReadAllText(filePath);
                var settings = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(json);

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
                Debug.WriteLine($"恢复列宽失败: {ex.Message}");
            }
        }

        #endregion
    }
}

