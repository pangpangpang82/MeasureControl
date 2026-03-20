using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using MeasureControl.ViewModels;
using MeasureControl.ViewModels.Common;

namespace MeasureControl.Views.Common
{
    public partial class FloatingWindow : Window
    {
        private WindowState _previousWindowState;

        public FloatingWindow()
        {
            InitializeComponent();
            
            // 订阅窗口事件
            this.StateChanged += FloatingWindow_StateChanged;
            this.Activated += FloatingWindow_Activated;
            this.Loaded += FloatingWindow_Loaded;
            
            // 初始化窗口状态
            _previousWindowState = this.WindowState;
        }

        // 注意：现在使用Prism Region管理内容，不再需要手动设置内容
        // Region会自动管理View的添加和移除

        /// <summary>
        /// 浮动窗口拖动事件处理
        /// </summary>
        private void FloatingWindow_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                this.DragMove();
            }
        }

        /// <summary>
        /// 窗口加载事件
        /// </summary>
        private void FloatingWindow_Loaded(object sender, RoutedEventArgs e)
        {
            _previousWindowState = this.WindowState;
        }

        /// <summary>
        /// 窗口状态变化事件（监听最小化和恢复）
        /// </summary>
        private void FloatingWindow_StateChanged(object sender, System.EventArgs e)
        {
            var currentState = this.WindowState;
            
            // 通知ViewModel处理状态变化
            if (this.DataContext is FloatingWindowViewModel vm)
            {
                vm.OnWindowStateChanged(currentState, _previousWindowState);
            }
            
            _previousWindowState = currentState;
        }

        /// <summary>
        /// 窗口激活事件（获得焦点）
        /// </summary>
        private void FloatingWindow_Activated(object sender, System.EventArgs e)
        {
            // 通知ViewModel处理激活事件
            if (this.DataContext is FloatingWindowViewModel vm)
            {
                vm.OnWindowActivated();
            }
        }
    }
}

