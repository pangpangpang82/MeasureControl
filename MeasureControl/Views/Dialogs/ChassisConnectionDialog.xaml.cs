using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using MeasureControl.ViewModels;

namespace MeasureControl.Views.Dialogs
{
    /// <summary>
    /// ChassisConnectionDialog.xaml 的交互逻辑
    /// </summary>
    public partial class ChassisConnectionDialog : Window
    {
        public ChassisConnectionDialog()
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
                    var connectionNameTextBox = FindName("ConnectionNameTextBox") as System.Windows.Controls.TextBox;
                    if (connectionNameTextBox != null)
                    {
                        connectionNameTextBox.Focus();
                    }
                }), DispatcherPriority.Input);
            }
            catch
            {
                var connectionNameTextBox = FindName("ConnectionNameTextBox") as System.Windows.Controls.TextBox;
                connectionNameTextBox?.Focus();
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

        // 窗口拖动
        private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            this.DragMove();
        }
    }
}
