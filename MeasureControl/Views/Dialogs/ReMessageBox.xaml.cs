using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using MeasureControl.ViewModels;
using MeasureControl.ViewModels.Dialogs;

namespace MeasureControl.Views.Dialogs
{
    public partial class ReMessageBox : Window
    {
        private readonly ReMessageBoxViewModel _viewModel;

        public MessageBoxResult Result => _viewModel?.Result ?? MessageBoxResult.None;

        public ReMessageBox(ReMessageBoxViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            DataContext = _viewModel;

            // 订阅ViewModel事件
            _viewModel.RequestClose += OnRequestClose;
            
            // 添加键盘事件处理
            this.PreviewKeyDown += ReMessageBox_KeyDown;
            this.Loaded += ReMessageBox_Loaded;
            this.Closing += ReMessageBox_Closing;
        }

        /// <summary>
        /// 处理ViewModel的关闭请求
        /// </summary>
        private void OnRequestClose()
        {
            try
            {
                Close();
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// 窗口关闭时清理事件订阅
        /// </summary>
        private void ReMessageBox_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                // 取消事件订阅
                if (_viewModel != null)
                {
                    _viewModel.RequestClose -= OnRequestClose;
                }
                
                this.PreviewKeyDown -= ReMessageBox_KeyDown;
                this.Loaded -= ReMessageBox_Loaded;
                this.Closing -= ReMessageBox_Closing;
            }
            catch (Exception)
            {
            }
        }


        private void DragWindow(object sender, MouseButtonEventArgs e)
        {
            this.DragMove();
        }

        /// <summary>
        /// 窗口加载完成后设置默认焦点
        /// </summary>
        private void ReMessageBox_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                Activate();
                Dispatcher.BeginInvoke(new Action(() => SetDefaultFocus()), DispatcherPriority.Input);
            }
            catch
            {
                SetDefaultFocus();
            }
        }

        /// <summary>
        /// 键盘事件处理
        /// </summary>
        private void ReMessageBox_KeyDown(object sender, KeyEventArgs e)
        {
            _viewModel?.HandleKeyDown(e.Key);
            e.Handled = e.Key == Key.Enter || e.Key == Key.Escape;
        }

        /// <summary>
        /// 设置默认焦点到第一个可见按钮
        /// </summary>
        private void SetDefaultFocus()
        {
            // 按照优先级设置默认焦点
            if (_viewModel.IsYesButtonVisible)
            {
                YesButton.Focus();
            }
            else if (_viewModel.IsOkButtonVisible)
            {
                OkButton.Focus();
            }
            else if (_viewModel.IsNoButtonVisible)
            {
                NoButton.Focus();
            }
            else if (_viewModel.IsCancelButtonVisible)
            {
                CancelButton.Focus();
            }
        }

        public static MessageBoxResult Show(string message, string caption = "提示",
            MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage icon = MessageBoxImage.None)
        {
            var viewModel = new ReMessageBoxViewModel(message, caption, buttons, icon);
            var messageBox = new ReMessageBox(viewModel);
            
            // 设置为Topmost确保对话框始终在最前面，不会被浮动窗口遮挡
            messageBox.Topmost = true;
            
            messageBox.ShowDialog();
            return messageBox.Result;
        }

        internal static void Show(string v, MessageBoxButton oK, MessageBoxImage error)
        {
            throw new NotImplementedException();
        }
    }
}