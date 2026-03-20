using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MeasureControl.Behaviors
{
    /// <summary>
    /// ScrollViewer滚轮横向滚动附加属性
    /// 可以通过附加属性在XAML中全局启用
    /// </summary>
    public static class ScrollViewerExtensions
    {
        /// <summary>
        /// 是否强制将鼠标滚轮映射为横向滚动（忽略纵向逻辑）
        /// </summary>
        public static readonly DependencyProperty ForceHorizontalWheelScrollProperty =
            DependencyProperty.RegisterAttached(
                "ForceHorizontalWheelScroll",
                typeof(bool),
                typeof(ScrollViewerExtensions),
                new PropertyMetadata(false, OnForceHorizontalWheelScrollChanged));

        public static bool GetForceHorizontalWheelScroll(ScrollViewer scrollViewer)
        {
            return (bool)scrollViewer.GetValue(ForceHorizontalWheelScrollProperty);
        }

        public static void SetForceHorizontalWheelScroll(ScrollViewer scrollViewer, bool value)
        {
            scrollViewer.SetValue(ForceHorizontalWheelScrollProperty, value);
        }

        /// <summary>
        /// 是否启用滚轮横向滚动的附加属性
        /// </summary>
        public static readonly DependencyProperty EnableHorizontalWheelScrollProperty =
            DependencyProperty.RegisterAttached(
                "EnableHorizontalWheelScroll",
                typeof(bool),
                typeof(ScrollViewerExtensions),
                new PropertyMetadata(false, OnEnableHorizontalWheelScrollChanged));

        /// <summary>
        /// 获取是否启用滚轮横向滚动
        /// </summary>
        public static bool GetEnableHorizontalWheelScroll(ScrollViewer scrollViewer)
        {
            return (bool)scrollViewer.GetValue(EnableHorizontalWheelScrollProperty);
        }

        /// <summary>
        /// 设置是否启用滚轮横向滚动
        /// </summary>
        public static void SetEnableHorizontalWheelScroll(ScrollViewer scrollViewer, bool value)
        {
            scrollViewer.SetValue(EnableHorizontalWheelScrollProperty, value);
        }

        private static void OnEnableHorizontalWheelScrollChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ScrollViewer scrollViewer)
            {
                if ((bool)e.NewValue)
                {
                    scrollViewer.PreviewMouseWheel += OnScrollViewerPreviewMouseWheel;
                }
                else
                {
                    scrollViewer.PreviewMouseWheel -= OnScrollViewerPreviewMouseWheel;
                }
            }
        }

        private static void OnScrollViewerPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var scrollViewer = sender as ScrollViewer;
            if (scrollViewer == null)
                return;

            // 检查是否按下了Shift键
            bool isShiftPressed = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

            // 检查横向滚动条是否可见
            bool isHorizontalScrollBarVisible = scrollViewer.ComputedHorizontalScrollBarVisibility == Visibility.Visible;
            
            // 检查是否可以横向滚动
            bool canScrollHorizontally = scrollViewer.ScrollableWidth > 0;

            // 检查纵向滚动条是否可见或是否在顶部/底部
            bool canScrollVertically = scrollViewer.ScrollableHeight > 0;
            bool isAtTop = scrollViewer.VerticalOffset <= 0;
            bool isAtBottom = scrollViewer.VerticalOffset >= scrollViewer.ScrollableHeight;
            bool isVerticalScrollBarVisible = scrollViewer.ComputedVerticalScrollBarVisibility == Visibility.Visible;

            // 如果按下了Shift键，总是进行横向滚动
            if (isShiftPressed && canScrollHorizontally)
            {
                // 计算滚动量
                double scrollAmount = e.Delta > 0 ? -50 : 50;
                scrollViewer.ScrollToHorizontalOffset(scrollViewer.HorizontalOffset + scrollAmount);
                e.Handled = true;
                return;
            }

            // 如果横向滚动条可见，且纵向滚动不可用或已到顶部/底部，则进行横向滚动
            if (isHorizontalScrollBarVisible && canScrollHorizontally)
            {
                // 如果纵向滚动条不可见，或者已到顶部/底部，则进行横向滚动
                if (!isVerticalScrollBarVisible || isAtTop || isAtBottom || !canScrollVertically)
                {
                    // 计算滚动量
                    double scrollAmount = e.Delta > 0 ? -50 : 50;
                    scrollViewer.ScrollToHorizontalOffset(scrollViewer.HorizontalOffset + scrollAmount);
                    e.Handled = true;
                }
            }
        }

        private static void OnForceHorizontalWheelScrollChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ScrollViewer sv)
            {
                if ((bool)e.NewValue)
                {
                    sv.PreviewMouseWheel += OnScrollViewerPreviewMouseWheelHorizontalOnly;
                }
                else
                {
                    sv.PreviewMouseWheel -= OnScrollViewerPreviewMouseWheelHorizontalOnly;
                }
            }
        }

        private static void OnScrollViewerPreviewMouseWheelHorizontalOnly(object sender, MouseWheelEventArgs e)
        {
            if (sender is ScrollViewer scrollViewer)
            {
                // 将鼠标滚轮强制映射为横向滚动
                double scrollAmount = e.Delta > 0 ? -50 : 50;
                scrollViewer.ScrollToHorizontalOffset(scrollViewer.HorizontalOffset + scrollAmount);
                e.Handled = true; // 阻止默认纵向滚动
            }
        }
    }
}

