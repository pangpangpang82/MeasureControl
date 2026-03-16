using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using MeasureControl.Models;
using MeasureControl.ViewModels;
using MeasureControl.ViewModels.Dialogs;

namespace MeasureControl.Views.Dialogs
{
    public partial class AddSignalDialog : Window
    {
        private readonly AddSignalDialogViewModel _viewModel;

        public SignalConfigItem SignalResult { get; private set; }

        public AddSignalDialog(AddSignalDialogViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            DataContext = _viewModel;

            // 订阅ViewModel事件
            _viewModel.RequestClose += OnRequestClose;

            // 添加键盘事件处理（使用 PreviewKeyDown 更稳定）
            this.PreviewKeyDown += AddSignalDialog_KeyDown;
            this.Loaded += AddSignalDialog_Loaded;
            this.Closing += AddSignalDialog_Closing;

            // 设置为Topmost确保对话框始终在最前面
            this.Topmost = true;
        }

        private void OnRequestClose()
        {
            try
            {
                SignalResult = _viewModel.Result;
                base.DialogResult = SignalResult != null;
                Close();
            }
            catch (Exception)
            {
            }
        }

        private void AddSignalDialog_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                // 取消事件订阅
                if (_viewModel != null)
                {
                    _viewModel.RequestClose -= OnRequestClose;
                }

                this.PreviewKeyDown -= AddSignalDialog_KeyDown;
                this.Loaded -= AddSignalDialog_Loaded;
                this.Closing -= AddSignalDialog_Closing;
            }
            catch (Exception)
            {
            }
        }

        private void DragWindow(object sender, MouseButtonEventArgs e)
        {
            this.DragMove();
        }

        private void AddSignalDialog_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                Activate();
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    SignalNameTextBox.Focus();
                }), DispatcherPriority.Input);
            }
            catch
            {
                SignalNameTextBox.Focus();
            }
        }

        private void AddSignalDialog_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                _viewModel?.CancelCommand.Execute(null);
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
            {
                // 如果焦点在 TextBox 中，只确认输入（失去焦点），不触发 OK
                var focusedElement = Keyboard.FocusedElement;
                if (focusedElement is System.Windows.Controls.TextBox textBox)
                {
                    // 移动焦点到其他控件，确认输入
                    textBox.MoveFocus(new System.Windows.Input.TraversalRequest(System.Windows.Input.FocusNavigationDirection.Next));
                    e.Handled = true;
                }
                else
                {
                    // 不在 TextBox 中，触发 OK 命令
                    if (_viewModel?.OkCommand.CanExecute(null) == true)
                    {
                        _viewModel.OkCommand.Execute(null);
                        e.Handled = true;
                    }
                }
            }
        }

        private void SignalNameTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
            {
                // 确认输入，移动焦点
                var textBox = sender as System.Windows.Controls.TextBox;
                textBox?.MoveFocus(new System.Windows.Input.TraversalRequest(System.Windows.Input.FocusNavigationDirection.Next));
                e.Handled = true;
            }
        }
    }
}


