using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using MeasureControl.ViewModels;
using MeasureControl.ViewModels.Dialogs;

namespace MeasureControl.Views.Dialogs
{
    /// <summary>
    /// AddChassisDialog.xaml 的交互逻辑
    /// </summary>
    public partial class AddChassisDialog : Window
    {
        private AddChassisDialogViewModel _viewModel;
        private bool _isClosing = false;

        public AddChassisDialog(AddChassisDialogViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            DataContext = _viewModel;
            
            _viewModel.DialogClosed += OnDialogClosed;
            
            // 添加键盘事件处理
            this.PreviewKeyDown += OnPreviewKeyDown;
            this.Loaded += OnLoaded;
        }

        private void OnDialogClosed(bool result)
        {
            if (_isClosing) return; // 防止重复关闭
            
            _isClosing = true;
            DialogResult = result;
            
            // 取消事件订阅，防止重复触发
            if (_viewModel != null)
            {
                _viewModel.DialogClosed -= OnDialogClosed;
            }
            
            Close();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                Activate();
                Dispatcher.BeginInvoke(new System.Action(() =>
                {
                    ChassisNameTextBox.Focus();
                }), DispatcherPriority.Input);
            }
            catch
            {
                ChassisNameTextBox.Focus();
            }
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (_viewModel?.ConfirmCommand?.CanExecute() == true)
                {
                    _viewModel.ConfirmCommand.Execute();
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.Escape)
            {
                if (_viewModel?.CancelCommand?.CanExecute() == true)
                {
                    _viewModel.CancelCommand.Execute();
                    e.Handled = true;
                }
            }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        /// <summary>
        /// 获取机箱名称结果
        /// </summary>
        public string ChassisNameResult => _viewModel?.ChassisName;

        /// <summary>
        /// 获取IP地址结果
        /// </summary>
        public string IpAddressResult => _viewModel?.IpAddress;

        /// <summary>
        /// 获取子网掩码结果
        /// </summary>
        public string SubnetMaskResult => _viewModel?.SubnetMask;
    }
}
