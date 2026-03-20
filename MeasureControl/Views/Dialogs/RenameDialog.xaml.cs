using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using MeasureControl.ViewModels;

namespace MeasureControl.Views.Dialogs
{
    /// <summary>
    /// RenameDialog.xaml 的交互逻辑
    /// </summary>
    public partial class RenameDialog : Window
    {
        public RenameDialog()
        {
            InitializeComponent();
            
            // 添加键盘事件处理
            this.PreviewKeyDown += OnPreviewKeyDown;
            this.Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                Activate();
                Dispatcher.BeginInvoke(new System.Action(() =>
                {
                    NewNameTextBox.Focus();
                }), DispatcherPriority.Input);
            }
            catch
            {
                NewNameTextBox.Focus();
            }
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                var viewModel = DataContext as dynamic;
                if (viewModel?.OkCommand?.CanExecute(null) == true)
                {
                    viewModel.OkCommand.Execute(null);
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.Escape)
            {
                var viewModel = DataContext as dynamic;
                if (viewModel?.CancelCommand?.CanExecute(null) == true)
                {
                    viewModel.CancelCommand.Execute(null);
                    e.Handled = true;
                }
            }
        }

        /// <summary>
        /// 拖动窗口
        /// </summary>
        private void DragWindow(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }
    }
}

