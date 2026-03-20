using System;
using System.Windows;
using System.Windows.Threading;
using Prism.Events;
using MeasureControl.Events;

namespace MeasureControl.Services
{
    /// <summary>
    /// 窗口管理器服务实现
    /// </summary>
    public class WindowManagerService : IWindowManagerService
    {
        #region Private Fields

        private readonly IEventAggregator _eventAggregator;
        
        // 全屏最大化模式标志
        private bool _isFullScreenMaximized = false;
        
        // 存储窗口的原始位置和尺寸
        private double _originalLeft;
        private double _originalTop;
        private double _originalWidth;
        private double _originalHeight;
        private WindowState _originalWindowState;

        #endregion

        #region Events

        public event EventHandler<WindowStateChangedEventArgs> WindowStateChanged;
        public event EventHandler<WindowClosedEventArgs> WindowClosed;

        #endregion

        #region Constructor

        public WindowManagerService(IEventAggregator eventAggregator)
        {
            _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// 最大化窗口（全屏）
        /// </summary>
        public void MaximizeWindow(Window window)
        {
            if (window == null) return;

            var oldState = window.WindowState;
            
            // 如果当前不是全屏最大化状态，保存原始状态
            if (!_isFullScreenMaximized)
            {
                _originalLeft = window.Left;
                _originalTop = window.Top;
                _originalWidth = window.Width;
                _originalHeight = window.Height;
                _originalWindowState = window.WindowState;
            }
            
            // 设置窗口为全屏最大化
            window.WindowState = WindowState.Maximized;
            _isFullScreenMaximized = true;
            
            OnWindowStateChanged(window, oldState, WindowState.Maximized);
        }

        /// <summary>
        /// 最小化窗口（隐藏但保留导航按钮）
        /// </summary>
        public void MinimizeWindow(Window window)
        {
            if (window == null) return;

            var oldState = window.WindowState;
            window.WindowState = WindowState.Minimized;
            
            // 发布最小化事件，通知其他组件保留导航按钮
            _eventAggregator.GetEvent<WindowMinimizedEvent>().Publish(new WindowMinimizedEventArgs 
            { 
                Window = window,
                KeepNavigationButtons = true 
            });
            
            OnWindowStateChanged(window, oldState, WindowState.Minimized);
        }

        /// <summary>
        /// 关闭窗口（释放内容）
        /// </summary>
        public void CloseWindow(Window window)
        {
            if (window == null) return;

            // 发布窗口关闭事件，通知其他组件释放内容
            _eventAggregator.GetEvent<WindowClosingEvent>().Publish(new WindowClosingEventArgs 
            { 
                Window = window,
                ReleaseContent = true 
            });

            // 执行关闭操作
            window.Close();
            
            OnWindowClosed(window, true);
        }

        /// <summary>
        /// 恢复窗口到正常状态
        /// </summary>
        public void RestoreWindow(Window window)
        {
            if (window == null) return;

            var oldState = window.WindowState;
            
            // 如果当前是全屏最大化状态，恢复到原始状态
            if (_isFullScreenMaximized)
            {
                window.WindowState = _originalWindowState;
                window.Left = _originalLeft;
                window.Top = _originalTop;
                window.Width = _originalWidth;
                window.Height = _originalHeight;
                _isFullScreenMaximized = false;
            }
            else
            {
                window.WindowState = WindowState.Normal;
            }
            
            OnWindowStateChanged(window, oldState, window.WindowState);
        }

        /// <summary>
        /// 切换窗口最大化状态
        /// </summary>
        public void ToggleMaximizeWindow(Window window)
        {
            if (window == null) return;

            if (_isFullScreenMaximized)
            {
                RestoreWindow(window);
            }
            else
            {
                MaximizeWindow(window);
            }
        }

        /// <summary>
        /// 检查窗口是否最大化
        /// </summary>
        public bool IsMaximized(Window window)
        {
            return _isFullScreenMaximized;
        }

        /// <summary>
        /// 检查窗口是否最小化
        /// </summary>
        public bool IsMinimized(Window window)
        {
            return window?.WindowState == WindowState.Minimized;
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// 触发窗口状态改变事件
        /// </summary>
        private void OnWindowStateChanged(Window window, WindowState oldState, WindowState newState)
        {
            WindowStateChanged?.Invoke(this, new WindowStateChangedEventArgs
            {
                Window = window,
                OldState = oldState,
                NewState = newState
            });
        }

        /// <summary>
        /// 触发窗口关闭事件
        /// </summary>
        private void OnWindowClosed(Window window, bool contentReleased)
        {
            WindowClosed?.Invoke(this, new WindowClosedEventArgs
            {
                Window = window,
                ContentReleased = contentReleased
            });
        }

        /// <summary>
        /// 最小化主窗口
        /// </summary>
        public void MinimizeMainWindow()
        {
            var mainWindow = Application.Current.MainWindow;
            if (mainWindow != null)
            {
                MinimizeWindow(mainWindow);
            }
        }

        /// <summary>
        /// 切换主窗口最大化状态
        /// </summary>
        public void ToggleMaximizeMainWindow()
        {
            var mainWindow = Application.Current.MainWindow;
            if (mainWindow != null)
            {
                ToggleMaximizeWindow(mainWindow);
            }
        }

        /// <summary>
        /// 关闭主窗口
        /// </summary>
        public void CloseMainWindow()
        {
            var mainWindow = Application.Current.MainWindow;
            if (mainWindow != null)
            {
                CloseWindow(mainWindow);
            }
        }

        #endregion
    }
}
