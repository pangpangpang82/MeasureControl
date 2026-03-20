using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace MeasureControl.Views
{
    /// <summary>
    /// MatrixSwitchConfigTable.xaml 的交互逻辑
    /// </summary>
    public partial class MatrixSwitchConfigTable : UserControl
    {
        private double _floatingHeight;
        private double _floatingWidth;
        private Point _floatingPosition;
        private bool _isFloating = false;
        private Window _floatingWindow;
        private bool _isMinimized = false;

        public MatrixSwitchConfigTable()
        {
            InitializeComponent();
            this.SizeChanged += OnSizeChanged;
        }

        private void OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (!_isFloating)
            {
                _floatingHeight = this.ActualHeight;
                _floatingWidth = this.ActualWidth;
            }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_isFloating && _floatingWindow != null)
            {
                _floatingWindow.DragMove();
            }
        }

        private void OnMinimizeButtonClick(object sender, RoutedEventArgs e)
        {
            if (_isFloating && _floatingWindow != null)
            {
                _isMinimized = !_isMinimized;
                if (_isMinimized)
                {
                    _floatingWindow.WindowState = WindowState.Minimized;
                }
                else
                {
                    _floatingWindow.WindowState = WindowState.Normal;
                }
            }
        }

        private void OnFloatButtonClick(object sender, RoutedEventArgs e)
        {
            if (_isFloating)
            {
                // 从浮动窗口返回
                if (_floatingWindow != null)
                {
                    this.Content = _floatingWindow.Content;
                    _floatingWindow.Close();
                    _floatingWindow = null;
                    _isFloating = false;
                }
            }
            else
            {
                // 浮动到新窗口
                _floatingWindow = new Window
                {
                    Content = this.Content,
                    Height = _floatingHeight,
                    Width = _floatingWidth,
                    WindowStyle = WindowStyle.None,
                    ResizeMode = ResizeMode.CanResize,
                    Background = Brushes.White,
                    Topmost = true,
                    ShowInTaskbar = true
                };

                this.Content = null;
                _floatingWindow.Closed += (s, args) =>
                {
                    if (this.Content == null)
                    {
                        this.Content = _floatingWindow.Content;
                    }
                    _floatingWindow = null;
                    _isFloating = false;
                };

                _floatingWindow.Show();
                _isFloating = true;
            }
        }

        private void OnCloseButtonClick(object sender, RoutedEventArgs e)
        {
            // 触发关闭命令，如果有绑定的话
            var dataContext = this.DataContext as ViewModels.MatrixSwitchConfigTableViewModel;
            dataContext?.CloseCommand.Execute(null);
        }

        private void GridSplitter_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            // 保存列宽设置
            // 这里可以添加保存列宽的逻辑
        }
    }
}