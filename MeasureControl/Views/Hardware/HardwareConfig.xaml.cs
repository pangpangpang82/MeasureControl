using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using MeasureControl.ViewModels;
using MeasureControl.Services;
using MeasureControl.Models;
using MeasureControl.Helpers;
using MeasureControl.ViewModels.Hardware;
using MeasureControl.ViewModels.Common;

namespace MeasureControl.Views.Hardware
{
    public partial class HardwareConfig : UserControl
    {
        private const bool FixedDemoMode = true;

        private HardwareConfigViewModel _viewModel;
        private IDragDropService _dragDropService;
        // 连接线绘制Canvas
        private ChassisConnectionCanvas _connectionCanvas;
        // 机箱图片覆盖层（单独放到 MainCanvas，使图片可以独立设置 ZIndex）
        private readonly System.Collections.Generic.Dictionary<string, UIElement> _chassisImageOverlays = new System.Collections.Generic.Dictionary<string, UIElement>();
        private bool _isFloating = false;
        private Image _floatButtonImage;
        private string _currentPageKey = null;
        
        public HardwareConfig()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
            Loaded += OnLoaded;
        }

        private void HardwareConfig_Loaded(object sender, RoutedEventArgs e)
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
            string pageName = "HardwareConfig";  // 使用PageType而非中文显示名

            // 获取RegionManager、EventAggregator、NavigationStateService、NavigationService和MainWindowViewModel
            var containerProvider = (Application.Current as App)?.Container;
            var regionManager = containerProvider?.Resolve(typeof(Prism.Regions.IRegionManager)) as Prism.Regions.IRegionManager;
            var eventAggregator = containerProvider?.Resolve(typeof(Prism.Events.IEventAggregator)) as Prism.Events.IEventAggregator;
            var navigationState = containerProvider?.Resolve(typeof(INavigationStateService)) as INavigationStateService;
            var navigationService = containerProvider?.Resolve(typeof(INavigationService)) as INavigationService;
            var mainViewModel = containerProvider?.Resolve(typeof(MainWindowViewModel)) as MainWindowViewModel;

            if (regionManager != null && eventAggregator != null && navigationState != null && mainViewModel != null)
            {
                // 通过Helper浮动整个页面
                _currentPageKey = FloatingWindowHelper.FloatPage(
                    pageName,
                    this,
                    regionManager,
                    eventAggregator,
                    navigationState,
                    (nextPage) => mainViewModel.NavigateToPage(nextPage),
                    navigationService
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
                var vm = DataContext as HardwareConfigViewModel;
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

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _connectionCanvas = ConnectionCanvas;
            if (_connectionCanvas != null)
            {
                // 订阅ViewModel的连接线更新事件
                if (ViewModel != null)
                {
                    ViewModel.ConnectionLinesUpdateRequested += OnConnectionLinesUpdateRequested;
                }
            }
            
            // 初始化浮动按钮图片引用
            _floatButtonImage = FindName("FloatImage") as Image;
            
            // 初始化Canvas布局
            InitializeCanvasLayout();
            
            // 监听Canvas大小变化
            MainCanvas.SizeChanged += OnMainCanvasSizeChanged;
        }

        private HardwareConfigViewModel ViewModel => DataContext as HardwareConfigViewModel;
        
        /// <summary>
        /// 初始化Canvas布局，设置2*5网格的位置和大小
        /// </summary>
        private void InitializeCanvasLayout()
        {
            if (MainCanvas == null) return;
            
            // 等待布局完成
            Dispatcher.BeginInvoke(new Action(() => {
                var canvasWidth = MainCanvas.ActualWidth;
                var canvasHeight = MainCanvas.ActualHeight;
                
                if (canvasWidth <= 0 || canvasHeight <= 0) return;
                
                // 计算每个单元格的大小
                var cellWidth = canvasWidth / 5;
                var cellHeight = canvasHeight / 2;
                // 设置连接线Canvas的大小
                ConnectionCanvas.Width = canvasWidth;
                ConnectionCanvas.Height = canvasHeight;
                
                // 设置第一行单元格的位置和大小
                for (int col = 0; col < 5; col++)
                {
                    var cell = MainCanvas.FindName($"Cell_0_{col}") as Border;
                    if (cell != null)
                    {
                        Canvas.SetLeft(cell, col * cellWidth);
                        Canvas.SetTop(cell, 0);
                        cell.Width = cellWidth;
                        cell.Height = cellHeight;
                    }
                }
                
                // 设置第二行单元格的位置和大小
                for (int col = 0; col < 5; col++)
                {
                    var cell = MainCanvas.FindName($"Cell_1_{col}") as Border;
                    if (cell != null)
                    {
                        Canvas.SetLeft(cell, col * cellWidth);
                        Canvas.SetTop(cell, cellHeight);
                        cell.Width = cellWidth;
                        cell.Height = cellHeight;
                    }
                }
                
                // 重新定位所有机箱图片覆盖层（如果存在），确保覆盖图与单元格居中对齐
                try
                {
                    if (_chassisImageOverlays != null && ViewModel?.PxiChassisList != null)
                    {
                        foreach (var chassis in ViewModel.PxiChassisList)
                        {
                            if (string.IsNullOrEmpty(chassis.Id)) continue;
                            if (!_chassisImageOverlays.TryGetValue(chassis.Id, out var overlay)) continue;

                            var cellName = $"Cell_{chassis.GridRow}_{chassis.GridColumn}";
                            var cell = MainCanvas.FindName(cellName) as Border;
                            if (cell == null) continue;

                            if (overlay is FrameworkElement fe)
                            {
                                double overlayWidth = double.IsNaN(fe.Width) ? fe.ActualWidth : fe.Width;
                                double overlayHeight = double.IsNaN(fe.Height) ? fe.ActualHeight : fe.Height;
                                // 如果尚未测量实际大小，尝试使用默认值防止放到 (0,0)
                                if (overlayWidth <= 0) overlayWidth = 200;
                                if (overlayHeight <= 0) overlayHeight = 140;

                                double left = Canvas.GetLeft(cell) + cell.Width / 2 - overlayWidth / 2;
                                double top = Canvas.GetTop(cell) + cell.Height / 2 - overlayHeight / 2;
                                Canvas.SetLeft(fe, left);
                                Canvas.SetTop(fe, top);
                            }
                        }
                    }
                }
                catch
                {
                    // 忽略定位错误，避免影响布局初始化
                }
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }
        
        /// <summary>
        /// Canvas大小变化时的处理
        /// </summary>
        private void OnMainCanvasSizeChanged(object sender, SizeChangedEventArgs e)
        {
            InitializeCanvasLayout();
        }
        
        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            // 取消订阅旧的ViewModel事件
            if (e.OldValue is HardwareConfigViewModel oldViewModel)
            {
                oldViewModel.ChassisControlsRefreshRequested -= RefreshAllChassisControls;
                oldViewModel.ChassisStatusUpdateRequested -= UpdateSingleChassisStatus;
                oldViewModel.PropertyChanged -= OnViewModelPropertyChanged;
                oldViewModel.ConnectionLinesUpdateRequested -= OnConnectionLinesUpdateRequested;
            }
            
            // 订阅新的ViewModel事件
            if (e.NewValue is HardwareConfigViewModel newViewModel)
            {
                _viewModel = newViewModel;
                _dragDropService = newViewModel.GetType().GetField("_dragDropService", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(newViewModel) as IDragDropService;
                
                if (_dragDropService != null)
                {
                    // 传递两个Border：PxiSourceBorder2722和PxiSourceBorder2519
                    _dragDropService.Initialize(MainCanvas, PxiSourceBorder2722, PxiSourceBorder2519);
                    _dragDropService.UpdateChassisList(_viewModel.PxiChassisList);
                }
                
                newViewModel.ChassisControlsRefreshRequested += RefreshAllChassisControls;
                newViewModel.ChassisStatusUpdateRequested += UpdateSingleChassisStatus;
                newViewModel.PropertyChanged += OnViewModelPropertyChanged;
                newViewModel.ConnectionLinesUpdateRequested += OnConnectionLinesUpdateRequested;
            }
        }

        // ViewModel属性变化处理
        private void OnViewModelPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(HardwareConfigViewModel.ChassisConnections))
            {
                UpdateConnectionLines();
            }
        }

        // PXI机箱拖动开始
        private void PxiSource_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (FixedDemoMode)
            {
                return;
            }

            if (e.LeftButton == MouseButtonState.Pressed && sender is FrameworkElement element)
            {
                _viewModel?.StartPxiChassisDragCommand?.Execute(element);
            }
        }

        // 双击机箱图片添加机箱（记录点击时间用于检测双击）
        private DateTime _lastPxiSourceClickTime = DateTime.MinValue;
        private object _lastPxiSourceClickSender = null;

        private void PxiSource_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (FixedDemoMode)
            {
                return;
            }

            var currentTime = DateTime.Now;
            var timeSinceLastClick = currentTime - _lastPxiSourceClickTime;

            // 如果是同一个元素且两次点击间隔小于300ms，认为是双击
            if (_lastPxiSourceClickSender == sender && timeSinceLastClick.TotalMilliseconds < 300)
            {
                e.Handled = true;
                
                // 获取机箱类型
                string chassisType = null;
                if (sender is FrameworkElement element && element.Tag is string tag)
                {
                    chassisType = tag;
                }

                // 调用 ViewModel 添加机箱命令
                if (!string.IsNullOrEmpty(chassisType))
                {
                    _viewModel?.AddChassisFromDoubleClickCommand?.Execute(chassisType);
                }

                _lastPxiSourceClickTime = DateTime.MinValue;
                _lastPxiSourceClickSender = null;
            }
            else
            {
                _lastPxiSourceClickTime = currentTime;
                _lastPxiSourceClickSender = sender;
            }
        }

        // 主Canvas鼠标点击事件 - 点击空白区域取消机箱选择
        private void MainCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 如果了Ctrl键，不处理清除选择（让机箱选择逻辑处理）
            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                return;
            }

            // 检查点击的是否是空白区域（不是机箱）
            var hitTestResult = VisualTreeHelper.HitTest(MainCanvas, e.GetPosition(MainCanvas));
            if (hitTestResult?.VisualHit != null)
            {
                // 检查点击的元素是否是机箱的Border
                var border = FindParent<Border>(hitTestResult.VisualHit);
                if (border != null && border.Tag is ChassisModel)
                {
                    // 点击的是机箱，不处理（让机箱自己的事件处理）
                    return;
                }
                
                // 检查点击的元素是否是连接线（Path元素）
                if (hitTestResult.VisualHit is Path path && path.Tag != null)
                {
                    // 点击的是连接线，不处理（让连接线自己的事件处理）
                    return;
                }
            }

            // 点击的是空白区域，清除机箱选择
            _viewModel?.ClearChassisSelectionCommand?.Execute(null);
        }

        // 查找父级Border元素
        private T FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            DependencyObject parentObject = VisualTreeHelper.GetParent(child);
            
            if (parentObject == null) return null;
            
            T parent = parentObject as T;
            if (parent != null)
                return parent;
            else
                return FindParent<T>(parentObject);
        }

        // PXI机箱鼠标进入
        private void PxiSource_MouseEnter(object sender, MouseEventArgs e)
        {
            // sender 直接就是 Border
            if (sender is Border border)
            {
                // 使用浅蓝色高亮（30%不透明度）
                _dragDropService?.HighlightPxiSource(border, true, "#4D9ED9F2");
            }
        }

        // PXI机箱鼠标离开
        private void PxiSource_MouseLeave(object sender, MouseEventArgs e)
        {
            // sender 直接就是 Border
            if (sender is Border border)
            {
                // 取消高亮
                _dragDropService?.HighlightPxiSource(border, false);
            }
        }

        // Canvas区域拖入 - 整个机箱区域高亮
        private void CanvasCell_DragEnter(object sender, DragEventArgs e)
        {
            if (FixedDemoMode)
            {
                return;
            }

            _dragDropService?.HandlePxiChassisDragEnter(e);
        }

        // Canvas区域拖出 - 取消整个机箱区域高亮
        private void CanvasCell_DragLeave(object sender, DragEventArgs e)
        {
            if (FixedDemoMode)
            {
                return;
            }

            _dragDropService?.HandlePxiChassisDragLeave(e);
        }

        // Canvas区域放置 - 自动放置到下一个可用位置
        private void CanvasCell_Drop(object sender, DragEventArgs e)
        {
            if (FixedDemoMode)
            {
                return;
            }

            _dragDropService?.HandlePxiChassisDrop(e);
        }

        // 更新单个机箱的状态
        private void UpdateSingleChassisStatus(ChassisModel chassis)
        {
            if (chassis == null) return;
            // 查找对应的Canvas单元格中的机箱Border
            Border targetCell = null;
            var cellName = $"Cell_{chassis.GridRow}_{chassis.GridColumn}";
            var cell = MainCanvas.FindName(cellName) as Border;
            if (cell != null && cell.Child != null)
            {
                // 检查这个Border是否包含机箱控件（通过Tag属性识别）
                if (cell.Tag is ChassisModel tagChassis && tagChassis == chassis)
                {
                    targetCell = cell;
                }
                
                // 如果Border的Child是机箱控件，也要检查
                if (cell.Child is Border childBorder && childBorder.Tag is ChassisModel childTagChassis && childTagChassis == chassis)
                {
                    targetCell = childBorder;
                }
            }
                
            if (targetCell != null)
            {
                // 更新StackPanel的背景色以反映选择状态
                if (targetCell.Child is StackPanel stackPanel)
                {
                    stackPanel.Background = chassis.IsSelected 
                        ? new SolidColorBrush(Color.FromArgb(60, 100, 150, 200)) 
                        : Brushes.Transparent;
                }
            }
            else
            {
                
                // 如果没找到，尝试重新创建机箱控件
                RefreshAllChassisControls();
            }
        }

        // 刷新所有机箱控件的位置
        private void RefreshAllChassisControls()
        {
            if (ViewModel?.PxiChassisList == null) 
            {
                return;
            }
            // 更新拖拽服务中的机箱列表
            _dragDropService?.UpdateChassisList(ViewModel.PxiChassisList);

            // 清除所有现有的机箱控件
            ClearAllChassisControls();

            // 重新创建所有机箱控件
            foreach (var chassis in ViewModel.PxiChassisList)
            {
                CreateChassisControl(chassis.GridRow, chassis.GridColumn);
            }

            // 更新连接线
            UpdateConnectionLines();
        }

        // 清除所有机箱控件
        private void ClearAllChassisControls()
        {
            // 取消注册所有机箱控件
            if (_connectionCanvas is ChassisConnectionCanvas connectionCanvas)
            {
                foreach (var chassis in ViewModel?.PxiChassisList ?? new System.Collections.ObjectModel.ObservableCollection<ChassisModel>())
                {
                    connectionCanvas.UnregisterChassis(chassis.Id);
                }
            }
            
            // 清除所有Canvas单元格中的机箱控件
            for (int row = 0; row < 2; row++)
            {
                for (int col = 0; col < 5; col++)
                {
                    var cellName = $"Cell_{row}_{col}";
                    var cell = MainCanvas.FindName(cellName) as Border;
                    if (cell != null)
                    {
                        cell.Child = null;
                    }
                }
            }
        // 移除所有机箱图片覆盖层
        if (_chassisImageOverlays?.Count > 0)
        {
            foreach (var overlay in _chassisImageOverlays.Values.ToList())
            {
                if (MainCanvas.Children.Contains(overlay))
                {
                    MainCanvas.Children.Remove(overlay);
                }
            }
            _chassisImageOverlays.Clear();
        }
        }

        // 动态创建机箱控件
        private void CreateChassisControl(int row, int column)
        {
            // 获取对应的机箱数据
            var chassis = ViewModel?.PxiChassisList?.FirstOrDefault(c => c.GridRow == row && c.GridColumn == column);
            if (chassis == null) return;

            // 找到对应的Canvas单元格
            var cellName = $"Cell_{row}_{column}";
            var targetCell = MainCanvas.FindName(cellName) as Border;

            if (targetCell == null) return;

            // 创建机箱控件
            var chassisControl = CreateChassisUIElement(chassis);
            targetCell.Child = chassisControl;

            // 将机箱单元格设置为较低的ZIndex，使连接线（在 ConnectionCanvas 中）可以在单元格上方绘制
            Panel.SetZIndex(targetCell, 10);

            // 如果机箱UI中包含Image，我们将把图片单独放到 MainCanvas 作为覆盖层，图片可设置为更高的 ZIndex
            if (chassisControl is Border chassisBorder && chassisBorder.Child is StackPanel sp)
            {
                void UpdateHoverVisual(bool isHovered)
                {
                    if (sp == null) return;

                    try
                    {
                        if (isHovered)
                        {
                            bool isCtrlPressed = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
                            var hoverColor = isCtrlPressed
                                ? Color.FromArgb(50, 80, 140, 200)
                                : Color.FromArgb(40, 100, 160, 220);
                            sp.Background = new SolidColorBrush(hoverColor);
                        }
                        else
                        {
                            sp.Background = chassis.IsSelected
                                ? new SolidColorBrush(Color.FromArgb(50, 80, 140, 200))
                                : Brushes.Transparent;
                        }
                    }
                    catch
                    {
                    }
                }

                sp.MouseEnter += (s, e) => UpdateHoverVisual(true);
                sp.MouseLeave += (s, e) => UpdateHoverVisual(false);

                var innerImage = sp.Children.OfType<Image>().FirstOrDefault();
                if (innerImage != null)
                {
                    // 保留原始占位（隐藏原图以保持布局），但在 MainCanvas 上创建一个覆盖用的 Image
                    innerImage.Visibility = Visibility.Hidden;

                    var overlay = new Image
                    {
                        Source = innerImage.Source,
                        Width = innerImage.Width,
                        Height = innerImage.Height,
                        Stretch = innerImage.Stretch,
                        IsHitTestVisible = true, // 允许点击 overlay，并转发事件到 ViewModel
                        Cursor = Cursors.Hand,
                        Tag = chassis
                    };

                    // 点击 overlay 时转发到 ViewModel，确保点击图片也能选中机箱并显示详细信息
                    // 同时支持双击（与 Border 的双击行为一致）
                    DateTime lastOverlayClickTime = DateTime.MinValue;
                    object lastOverlayClickSender = null;
                    overlay.MouseLeftButtonDown += (s, e) =>
                    {
                        try
                        {
                            var currentTime = DateTime.Now;
                            var timeSinceLastClick = currentTime - lastOverlayClickTime;
                            if (lastOverlayClickSender == s && timeSinceLastClick.TotalMilliseconds < 300)
                            {
                                // 双击
                                _viewModel?.PxiChassisDoubleClickCommand?.Execute(chassis);
                                e.Handled = true;
                                lastOverlayClickTime = DateTime.MinValue;
                                lastOverlayClickSender = null;
                            }
                            else
                            {
                                // 单击
                                _viewModel?.PxiChassisClickCommand?.Execute(chassis);
                                lastOverlayClickTime = currentTime;
                                lastOverlayClickSender = s;
                                e.Handled = true;
                            }
                        }
                        catch { }
                    };
                    // 鼠标移入/移出 overlay 时触发高亮，确保图片上悬停也能高亮机箱
                    overlay.MouseEnter += (s, e) => UpdateHoverVisual(true);
                    overlay.MouseLeave += (s, e) => UpdateHoverVisual(false);

                    overlay.ContextMenu = null;

                    // 将覆盖图片添加到 MainCanvas，并居中对齐到该单元格
                    var left = Canvas.GetLeft(targetCell) + targetCell.Width / 2 - overlay.Width / 2;
                    var top = Canvas.GetTop(targetCell) + targetCell.Height / 2 - overlay.Height / 2;
                    Canvas.SetLeft(overlay, left);
                    Canvas.SetTop(overlay, top);
                    Panel.SetZIndex(overlay, 100);
                    MainCanvas.Children.Add(overlay);
                    _chassisImageOverlays[chassis.Id] = overlay;

                    // 将覆盖图片注册为机箱元素，连接线的定位以覆盖图片为参考（使连线中心保持与原来一致）
                    if (_connectionCanvas is ChassisConnectionCanvas connectionCanvas)
                    {
                        connectionCanvas.RegisterChassis(chassis.Id, overlay);
                    }
                }
                else
                {
                    // 若没有图片，则按原方式注册整个控件
                    if (_connectionCanvas is ChassisConnectionCanvas connectionCanvas && chassisControl is FrameworkElement frameworkElement)
                    {
                        connectionCanvas.RegisterChassis(chassis.Id, frameworkElement);
                    }
                }
            }
            else
            {
                // 默认注册控件到连接线Canvas
                if (_connectionCanvas is ChassisConnectionCanvas connectionCanvas && chassisControl is FrameworkElement frameworkElement)
                {
                    connectionCanvas.RegisterChassis(chassis.Id, frameworkElement);
                }
            }

            // 注册完成后，立即更新连接线绘制
            Dispatcher.BeginInvoke(new Action(() => {
                UpdateConnectionLines();
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        // 创建机箱UI元素
        private UIElement CreateChassisUIElement(ChassisModel chassis)
        {
            var border = new Border
            {
                Margin = new Thickness(5), // 添加小Margin，让Border响应区域小于StackPanel
                Background = Brushes.Transparent, // Border保持透明
                Cursor = Cursors.Hand,
                BorderBrush = Brushes.Transparent, // 去掉边框
                BorderThickness = new Thickness(0), // 去掉边框厚度
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(0), // 保持Padding为0
                Tag = chassis // 设置Tag为机箱对象，用于识别点击的是机箱
            };

            // 不需要发光效果，只使用背景高亮

            // 添加双击检测（使用MouseLeftButtonDown事件检测双击）
            var lastClickTime = DateTime.MinValue;
            border.MouseLeftButtonDown += (s, e) => 
            {
                var currentTime = DateTime.Now;
                var timeSinceLastClick = currentTime - lastClickTime;
                
                // 如果两次点击间隔小于300ms，认为是双击
                if (timeSinceLastClick.TotalMilliseconds < 300)
                {
                    e.Handled = true; // 阻止事件继续传播
                    ViewModel?.PxiChassisDoubleClickCommand?.Execute(chassis);
                    lastClickTime = DateTime.MinValue; // 重置时间，避免连续触发
                }
                else
                {
                    // 单击处理（Ctrl+左键选择功能）
                    ViewModel?.PxiChassisClickCommand?.Execute(chassis);
                    lastClickTime = currentTime;
                }
            };

            // 添加右键菜单
            border.ContextMenu = null;

            // 创建内容
            var stackPanel = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Orientation = Orientation.Vertical,
                Margin = new Thickness(0, 2, 0, 0), // 进一步减少边距，减小响应区域
                Background = chassis.IsSelected ? new SolidColorBrush(Color.FromArgb(50, 80, 140, 200)) : Brushes.Transparent // 选中状态与Ctrl悬停颜色一致
            };

            var textBlock = new TextBlock
            {
                Text = chassis.Name,
                Margin = new Thickness(0, 2, 0, 2), // 减少上下边距，从(0,5,0,0)改为(0,2,0,2)
                HorizontalAlignment = HorizontalAlignment.Center,
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromRgb(45, 53, 77))
            };

            // 根据机箱型号选择不同的图片
            string imagePath = "/Resources/Hardware/PXI-2722.png"; // 默认9槽机箱图片
            double imageWidth = 200;
            double imageHeight = 140;
            
            // 检查机箱的ChassisType属性来确定机箱型号
            string chassisType = chassis.ChassisType ?? "";
            if (chassisType.Contains("2519") || chassisType.Contains("5槽"))
            {
                imagePath = "/Resources/Hardware/PXI-2519.png";
                imageWidth = 200;
                imageHeight = 140;
            }
            else if (chassisType.Contains("2722") || chassisType.Contains("9槽"))
            {
                imagePath = "/Resources/Hardware/PXI-2722.png";
                imageWidth = 200;
                imageHeight = 140;
            }
            
            var image = new Image
            {
                Width = imageWidth,
                Height = imageHeight,
                Margin = new Thickness(10, 0, 10, 0), // 左右边距10像素，上边距5像素，下边距10像素
                Source = new BitmapImage(new Uri(imagePath, UriKind.Relative)),
                Stretch = Stretch.Uniform
            };

            stackPanel.Children.Add(textBlock);
            stackPanel.Children.Add(image);
            
            border.Child = stackPanel;

            return border;
        }

        /// <summary>
        /// 更新连接线显示 - 统一使用ChassisConnectionCanvas处理
        /// </summary>
        private void UpdateConnectionLines()
        {
            if (_connectionCanvas == null || ViewModel?.ChassisConnections == null)
            {
                return;
            }

            // 使用ChassisConnectionCanvas的统一绘制方法
            _connectionCanvas.UpdateConnections(ViewModel.ChassisConnections);
        }
        
        /// <summary>
        /// 测试连接线绘制
        /// </summary>
        public void TestConnectionLineDrawing()
        {
            if (_connectionCanvas != null)
            {
                // 添加测试绿线
                _connectionCanvas.AddTestGreenLine();
                
                // 添加测试红线
                _connectionCanvas.AddTestPath();
            }
            else
            {
            }
        }

        /// <summary>
        /// 测试连接线数据传递
        /// </summary>
        public void TestConnectionDataTransfer()
        {
            if (ViewModel != null)
            {
                var connections = ViewModel.ChassisConnections;
                if (_connectionCanvas != null)
                {
                    _connectionCanvas.UpdateConnections(connections);
                }
            }
        }

        /// <summary>
        /// 获取指定位置的机箱元素
        /// </summary>
        private FrameworkElement GetChassisElement(int row, int column)
        {
            
            var cellName = $"Cell_{row}_{column}";
            var cell = MainCanvas.FindName(cellName) as Border;
            
            if (cell != null && cell.Child != null)
            {
                // 返回Border内部的机箱UserControl，而不是Border本身
                var chassisControl = cell.Child as FrameworkElement;
                return chassisControl;
            }
            
            return null;
        }

        // 连接线绘制相关方法 - 统一使用ChassisConnectionCanvas处理
        private void OnConnectionLinesUpdateRequested(object sender, EventArgs e)
        {
            if (_connectionCanvas is ChassisConnectionCanvas connectionCanvas)
            {
                // 使用ChassisConnectionCanvas的统一绘制方法
                connectionCanvas.UpdateConnections(ViewModel?.ChassisConnections);
            }
        }

        /// <summary>
        /// 强制刷新连接线（用于测试）
        /// </summary>
        public void ForceRefreshConnectionLines()
        {
            if (_connectionCanvas is ChassisConnectionCanvas connectionCanvas)
            {
                // 使用ChassisConnectionCanvas的强制刷新方法
                connectionCanvas.ForceRefreshConnections();
            }
        }

        /// <summary>
        /// 强制重新绘制连接线（用于测试竖线长度变化）
        /// </summary>
        public void ForceRedrawConnectionLines()
        {
            if (_connectionCanvas is ChassisConnectionCanvas connectionCanvas)
            {
                // 使用ChassisConnectionCanvas的强制重绘方法
                connectionCanvas.ForceRedrawConnections();
            }
        }

        /// <summary>
        /// 测试文字位置（在Loaded事件中调用）
        /// </summary>
        public void TestTextPosition()
        {
            ForceRefreshConnectionLines();
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
