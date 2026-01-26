using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using MeasureControl.Models;
using MeasureControl.ViewModels.Hardware;

namespace MeasureControl.Views.Hardware
{
    public class ChassisConnectionCanvas : Canvas
    {
        private class ConnectionPathContext
        {
            public ChassisConnection Connection { get; set; }
            public Path VisualPath { get; set; }
        }

        private readonly Dictionary<string, FrameworkElement> _chassisElements = new Dictionary<string, FrameworkElement>();
        private List<ChassisConnection> _connections = new List<ChassisConnection>();
        private DispatcherTimer _redrawTimer;
        private Size _lastCanvasSize = new Size(0, 0);
        private readonly Dictionary<string, int> _retryCounts = new Dictionary<string, int>();
        // 重试相关字段
        private bool _needsRetry;
        private DispatcherTimer _retryTimer;

        public ChassisConnectionCanvas()
        {
            // 设置背景为透明但可命中测试
            Background = Brushes.Transparent;

            // 只对Path元素进行命中测试，让其他区域穿透
            IsHitTestVisible = true;

            _redrawTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(50)
            };

            // 初始化重试定时器
            _retryTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            _retryTimer.Tick += OnRetryTimerTick;

            LayoutUpdated += OnLayoutUpdated;

            Loaded += (s, e) => {
            };
        }

        // 重写HitTestCore，只让Path元素可点击
        protected override HitTestResult HitTestCore(PointHitTestParameters hitTestParameters)
        {
            var point = hitTestParameters.HitPoint;


            // 遍历所有子元素，检查是否点击了Path
            foreach (UIElement child in Children)
            {
                if (child is Path path)
                {
                    try
                    {
                        // 使用更可靠的边界检测方法
                        var pathBounds = GetPathBounds(path);
                        

                        // 只有当边界有效且包含点击点时才返回命中结果
                        if (!pathBounds.IsEmpty && pathBounds.Contains(point))
                        {
                            return new PointHitTestResult(path, point);
                        }
                    }
                    catch (Exception)
                    {
                        // 忽略命中测试异常
                    }
                }
                else if (child is TextBlock textBlock)
                {
                    // TextBlock也需要命中测试（用于显示连接类型）
                    var textBounds = new Rect(
                        Canvas.GetLeft(textBlock),
                        Canvas.GetTop(textBlock),
                        textBlock.ActualWidth,
                        textBlock.ActualHeight);

                    if (textBounds.Contains(point))
                    {
                        return new PointHitTestResult(textBlock, point);
                    }
                }
            }

            // 其他区域不响应点击，让事件穿透到下层
            return null;
        }

        private Rect GetPathBounds(Path path)
        {
            try
            {
                // 方法1：直接使用PathGeometry的边界（不需要变换，因为PathGeometry坐标已经是Canvas的绝对坐标）
                if (path.Data is PathGeometry pathGeometry)
                {
                    var bounds = pathGeometry.Bounds;
                    if (!bounds.IsEmpty)
                    {
                        // 扩展边界以便更容易点击（加上描边宽度的一半）
                        var strokePadding = path.StrokeThickness / 2 + 5;
                        return new Rect(
                            bounds.X - strokePadding,
                            bounds.Y - strokePadding,
                            bounds.Width + strokePadding * 2,
                            bounds.Height + strokePadding * 2);
                    }
                }

                // 方法2：尝试使用RenderedGeometry
                if (path.RenderedGeometry != null && !path.RenderedGeometry.IsEmpty())
                {
                    var bounds = path.RenderedGeometry.GetRenderBounds(new Pen(path.Stroke, path.StrokeThickness + 10));
                    if (!bounds.IsEmpty)
                    {
                        return bounds; // 直接返回，不需要变换
                    }
                }

                // 方法3：使用Path的Data边界（通用方法）
                if (path.Data != null)
                {
                    var bounds = path.Data.Bounds;
                    if (!bounds.IsEmpty)
                    {
                        var strokePadding = path.StrokeThickness / 2 + 5;
                        return new Rect(
                            bounds.X - strokePadding,
                            bounds.Y - strokePadding,
                            bounds.Width + strokePadding * 2,
                            bounds.Height + strokePadding * 2);
                    }
                }

                // 方法4：如果以上都失败，返回一个无效区域，避免误触发
                return new Rect(-1, -1, 0, 0);
            }
            catch (Exception)
            {
                return new Rect(-1, -1, 0, 0);
            }
        }

        public void RegisterChassis(string chassisId, FrameworkElement element)
        {
            if (element == null)
            {
                return;
            }

            _chassisElements[chassisId] = element;

            element.LayoutUpdated += (s, e) => {
                if (_connections.Any())
                {
                    _redrawTimer.Stop();
                    _redrawTimer.Start();
                }
            };
        }

        public void UnregisterChassis(string chassisId)
        {
            _chassisElements.Remove(chassisId);
        }

        public void UpdateConnections(IEnumerable<ChassisConnection> connections)
        {
            _connections = connections?.ToList() ?? new List<ChassisConnection>();
            // 打印已注册的机箱ID
            foreach (var kvp in _chassisElements)
            {
            }

            ClearConnections();

            if (_connections.Count > 0)
            {
                Dispatcher.BeginInvoke(new Action(() => {
                    DrawChassisConnections();
                }), DispatcherPriority.Loaded);
            }
            else
            {
            }
        }

        private void OnLayoutUpdated(object sender, EventArgs e)
        {
            var currentSize = new Size(ActualWidth, ActualHeight);
            if (_lastCanvasSize != currentSize && currentSize.Width > 0 && currentSize.Height > 0)
            {
                _lastCanvasSize = currentSize;

                if (_connections != null && _connections.Count > 0)
                {
                    Dispatcher.BeginInvoke(new Action(() => {
                        DrawChassisConnections();
                    }), DispatcherPriority.Loaded);
                }
            }
        }

        private void DrawChassisConnections()
        {
            if (_connections == null || _connections.Count == 0)
            {
                return;
            }


            foreach (var connection in _connections)
            {
                DrawChassisConnection(connection);
            }
        }


        private void OnRetryTimerTick(object sender, EventArgs e)
        {
            if (_needsRetry)
            {
                _needsRetry = false;
                _retryTimer.Stop();
                // 使用最新的connections集合重新绘制所有连接
                DrawChassisConnections();
            }
        }

        private void DrawChassisConnection(ChassisConnection connection)
        {
            try
            {
                var sourceElement = GetChassisElementById(connection.SourceChassisId);
                var targetElement = GetChassisElementById(connection.TargetChassisId);

                if (sourceElement == null || targetElement == null)
                {
                    // 检查重试次数，避免无限重试
                    var connectionKey = $"{connection.SourceChassisId}-{connection.TargetChassisId}";
                    if (!_retryCounts.ContainsKey(connectionKey))
                    {
                        _retryCounts[connectionKey] = 0;
                    }
                    
                    if (_retryCounts[connectionKey] < 10) // 增加重试次数到10次
                    {
                        _retryCounts[connectionKey]++;
                        _needsRetry = true;
                        // 确保重试定时器正在运行
                        if (!_retryTimer.IsEnabled)
                        {
                            _retryTimer.Start();
                        }
                    }
                    else
                    {
                    }
                    return;
                }

                var sourceCenter = GetChassisCenter(sourceElement);
                var targetCenter = GetChassisCenter(targetElement);
                

                DrawSimpleConnection(sourceCenter, targetCenter, connection);
                // 成功绘制后清除重试计数
                var successConnectionKey = $"{connection.SourceChassisId}-{connection.TargetChassisId}";
                if (_retryCounts.ContainsKey(successConnectionKey))
                {
                    _retryCounts.Remove(successConnectionKey);
                }
            }
            catch (Exception)
            {
                // 忽略重试计数清理异常
            }
        }

        private FrameworkElement GetChassisElementById(string chassisId)
        {
            if (_chassisElements.TryGetValue(chassisId, out var element))
            {
                return element;
            }

            return null;
        }

        private void DrawSimpleConnection(Point sourceCenter, Point targetCenter, ChassisConnection connection)
        {
            var distance = Math.Abs(targetCenter.X - sourceCenter.X);
            var verticalLength = Math.Max(25, distance * 0.2); // 减小竖线长度20像素：从60改为40

            // 定义向下平移的距离（从机箱中心开始）
            var downwardOffset = 20; // 从机箱中心向下平移20像素
            
            // 源机箱：从中心向下平移后的起点
            var sourceVerticalStart = new Point(sourceCenter.X, sourceCenter.Y + downwardOffset);
            var sourceVerticalEnd = new Point(sourceCenter.X, sourceVerticalStart.Y + verticalLength);
            
            // 目标机箱：从中心向下平移后的起点
            var targetVerticalStart = new Point(targetCenter.X, targetCenter.Y + downwardOffset);
            var targetVerticalEnd = new Point(targetCenter.X, targetVerticalStart.Y + verticalLength);

            // 创建连接线路径几何
            var pathGeometry = new PathGeometry();
            var pathFigure = new PathFigure { StartPoint = sourceVerticalStart }; // 从平移后的起点开始绘制

            // 绘制源机箱的竖线（从平移后的起点开始）
            pathFigure.Segments.Add(new LineSegment(sourceVerticalEnd, true));
            // 水平连接到目标机箱的竖线终点
            pathFigure.Segments.Add(new LineSegment(targetVerticalEnd, true));
            // 绘制目标机箱的竖线（向上）
            pathFigure.Segments.Add(new LineSegment(targetVerticalStart, true));

            pathGeometry.Figures.Add(pathFigure);

            var visualPath = new Path
            {
                Data = pathGeometry,
                Stroke = new SolidColorBrush(Color.FromRgb(76, 175, 80)),
                StrokeThickness = 8,
                StrokeLineJoin = PenLineJoin.Round,
                IsHitTestVisible = false
            };

            Panel.SetZIndex(visualPath, 50);
            Canvas.SetLeft(visualPath, 0);
            Canvas.SetTop(visualPath, 0);
            Children.Add(visualPath);

            var hitPath = new Path
            {
                Data = pathGeometry.Clone(),
                Stroke = Brushes.Transparent,
                StrokeThickness = 24,
                Cursor = Cursors.Hand,
                IsHitTestVisible = true,
                Tag = new ConnectionPathContext
                {
                    Connection = connection,
                    VisualPath = visualPath
                }
            };

            hitPath.MouseLeftButtonDown += OnConnectionLineClicked;
            hitPath.MouseRightButtonDown += OnConnectionLineRightClicked;
            hitPath.MouseEnter += OnConnectionLineMouseEnter;
            hitPath.MouseLeave += OnConnectionLineMouseLeave;

            Panel.SetZIndex(hitPath, 200);
            Canvas.SetLeft(hitPath, 0);
            Canvas.SetTop(hitPath, 0);
            Children.Add(hitPath);
            AddConnectionLabel(string.IsNullOrWhiteSpace(connection.ConnectionName) ? GetConnectionTypeDisplayName(connection.ConnectionType.ToString()) : connection.ConnectionName, sourceVerticalEnd, targetVerticalEnd);
        }

        private void AddConnectionLabel(string labelText, Point startPoint, Point endPoint)
        {
            var labelX = (startPoint.X + endPoint.X) / 2;
            var labelY = startPoint.Y - 15;

            var displayText = string.IsNullOrWhiteSpace(labelText)
                ? GetConnectionTypeDisplayName(null)
                : labelText;

            var textBlock = new TextBlock
            {
                Text = displayText,
                FontSize = 14,
                FontWeight = FontWeights.Normal,
                Foreground = new SolidColorBrush(Colors.Black),
                Background = Brushes.Transparent,
                Padding = new Thickness(4, 2, 4, 2),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false // 标签不响应点击
            };

            textBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            textBlock.Arrange(new Rect(textBlock.DesiredSize));

            Canvas.SetLeft(textBlock, labelX - textBlock.DesiredSize.Width / 2);
            Canvas.SetTop(textBlock, labelY - textBlock.DesiredSize.Height / 2);

            Panel.SetZIndex(textBlock, 11); // 位于连接线上方但低于机箱

            Children.Add(textBlock);
        }

        public void ForceRefreshConnections()
        {
            if (_connections != null && _connections.Count > 0)
            {
                ClearConnections();
                DrawChassisConnections();
            }
        }

        /// <summary>
        /// 强制重新绘制所有连接线（用于测试竖线长度变化）
        /// </summary>
        public void ForceRedrawConnections()
        {
            
            if (_connections != null && _connections.Count > 0)
            {
                ClearConnections();
                
                // 延迟一点时间确保清除完成
                Dispatcher.BeginInvoke(new Action(() => {
                    DrawChassisConnections();
                }), DispatcherPriority.Loaded);
            }
        }

        private string GetConnectionTypeDisplayName(string connectionType)
        {
            return connectionType switch
            {
                "Ethernet" => "以太网",
                "USB" => "USB",
                "Serial" => "串口",
                _ => connectionType ?? "连接"
            };
        }

        private Point GetChassisCenter(FrameworkElement element)
        {
            try
            {
                if (ActualWidth <= 0 || ActualHeight <= 0)
                {
                    return new Point(0, 0);
                }

                var transform = element.TransformToVisual(this);
                var bounds = transform.TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));

                var centerPoint = new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
                

                return centerPoint;
            }
            catch (Exception)
            {
                return new Point(0, 0);
            }
        }

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

        private void ClearConnections()
        {
            var elementsToRemove = new List<UIElement>();
            foreach (UIElement child in Children)
            {
                if (child is Path || child is TextBlock)
                {
                    elementsToRemove.Add(child);
                }
            }

            foreach (var element in elementsToRemove)
            {
                Children.Remove(element);
            }

        }

        private void OnConnectionLineClicked(object sender, MouseButtonEventArgs e)
        {
            if (sender is Path path && path.Tag is ConnectionPathContext ctx && ctx.Connection != null)
            {
                e.Handled = true;
                
                HighlightConnectionLine(path);
                
                // 调用ViewModel的连接线点击命令
                if (DataContext is HardwareConfigViewModel viewModel)
                {
                    viewModel.ConnectionLineClickCommand?.Execute(ctx.Connection);
                }
            }
        }

        private void OnConnectionLineRightClicked(object sender, MouseButtonEventArgs e)
        {
            if (sender is Path path && path.Tag is ConnectionPathContext ctx && ctx.Connection != null)
            {
                e.Handled = true;
                
                // 创建右键菜单
                var contextMenu = new ContextMenu();
                
                // 从父窗口获取自定义样式
                var parentWindow = FindParentWindow(this);
                if (parentWindow?.Resources["CustomContextMenuStyle"] is Style contextMenuStyle)
                {
                    contextMenu.Style = contextMenuStyle;
                }
                
                // 获取菜单项样式（只获取一次，避免重复定义）
                Style menuItemStyle = null;
                if (parentWindow?.Resources["CustomMenuItemStyle"] is Style style)
                {
                    menuItemStyle = style;
                }
                
                // 重命名连接菜单项
                var renameMenuItem = new MenuItem
                {
                    Header = "重命名连接",
                    Tag = ctx.Connection
                };
                if (menuItemStyle != null)
                {
                    renameMenuItem.Style = menuItemStyle;
                }
                renameMenuItem.Click += (s, args) =>
                {
                    if (DataContext is HardwareConfigViewModel vm)
                    {
                        vm.RenameConnectionCommand?.Execute(ctx.Connection);
                    }
                };

                // 断开连接菜单项
                var disconnectMenuItem = new MenuItem 
                { 
                    Header = "断开连接",
                    Tag = ctx.Connection
                };
                
                // 应用自定义菜单项样式
                if (menuItemStyle != null)
                {
                    disconnectMenuItem.Style = menuItemStyle;
                }
                
                disconnectMenuItem.Click += (s, args) => 
                {
                    if (DataContext is HardwareConfigViewModel viewModel)
                    {
                        viewModel.DisconnectConnectionLineCommand?.Execute(ctx.Connection);
                    }
                };
                
                contextMenu.Items.Add(renameMenuItem);
                contextMenu.Items.Add(disconnectMenuItem);
                
                // 显示右键菜单
                contextMenu.IsOpen = true;
            }
        }

        /// <summary>
        /// 查找父窗口
        /// </summary>
        private Window FindParentWindow(DependencyObject child)
        {
            DependencyObject parentObject = VisualTreeHelper.GetParent(child);
            
            if (parentObject == null)
                return null;
                
            if (parentObject is Window parentWindow)
                return parentWindow;
            else
                return FindParentWindow(parentObject);
        }

        private void OnConnectionLineMouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is Path path && path.Tag is ConnectionPathContext ctx && ctx.VisualPath != null)
            {
                // 鼠标悬停时线条颜色变深
                ctx.VisualPath.Stroke = new SolidColorBrush(Color.FromRgb(46, 125, 50));
            }
        }

        private void OnConnectionLineMouseLeave(object sender, MouseEventArgs e)
        {
            if (sender is Path path && path.Tag is ConnectionPathContext ctx && ctx.VisualPath != null)
            {
                // 鼠标离开时恢复原始颜色
                ctx.VisualPath.Stroke = new SolidColorBrush(Color.FromRgb(76, 175, 80));
            }
        }

        private void HighlightConnectionLine(Path selectedPath)
        {
            foreach (UIElement child in Children)
            {
                if (child is Path path && path.Tag is ConnectionPathContext ctx && ctx.VisualPath != null)
                {
                    ctx.VisualPath.StrokeThickness = 8;
                    ctx.VisualPath.Stroke = new SolidColorBrush(Color.FromRgb(76, 175, 80));
                }
            }

            if (selectedPath?.Tag is ConnectionPathContext selectedCtx && selectedCtx.VisualPath != null)
            {
                selectedCtx.VisualPath.StrokeThickness = 8;
                selectedCtx.VisualPath.Stroke = new SolidColorBrush(Color.FromRgb(46, 125, 50));
            }
        }

        public void ClearSelection()
        {
            foreach (UIElement child in Children)
            {
                if (child is Path path && path.Tag is ConnectionPathContext ctx && ctx.VisualPath != null)
                {
                    ctx.VisualPath.StrokeThickness = 8;
                    ctx.VisualPath.Stroke = new SolidColorBrush(Color.FromRgb(76, 175, 80));
                }
            }
        }

        // 测试方法：添加一个简单的可点击Path
        public void AddTestPath()
        {
            var testPath = new Path
            {
                Data = new LineGeometry(new Point(100, 100), new Point(200, 200)),
                Stroke = new SolidColorBrush(Colors.Red),
                StrokeThickness = 10,
                Cursor = Cursors.Hand,
                Tag = "测试连接",
                IsHitTestVisible = true
            };

            testPath.MouseLeftButtonDown += OnConnectionLineClicked;
            testPath.MouseEnter += OnConnectionLineMouseEnter;
            testPath.MouseLeave += OnConnectionLineMouseLeave;

            Panel.SetZIndex(testPath, 10);
            Canvas.SetLeft(testPath, 0);
            Canvas.SetTop(testPath, 0);

            Children.Add(testPath);
        }
        
        // 测试方法：添加一个绿色连接线
        public void AddTestGreenLine()
        {
            var testPath = new Path
            {
                Data = new LineGeometry(new Point(50, 50), new Point(150, 150)),
                Stroke = new SolidColorBrush(Color.FromRgb(76, 175, 80)), // 绿色
                StrokeThickness = 8,
                StrokeLineJoin = PenLineJoin.Round,
                Cursor = Cursors.Hand,
                Tag = "测试绿线",
                IsHitTestVisible = true
            };

            testPath.MouseLeftButtonDown += OnConnectionLineClicked;
            testPath.MouseEnter += OnConnectionLineMouseEnter;
            testPath.MouseLeave += OnConnectionLineMouseLeave;

            Panel.SetZIndex(testPath, 10);
            Canvas.SetLeft(testPath, 0);
            Canvas.SetTop(testPath, 0);

            Children.Add(testPath);
        }

    }
}
