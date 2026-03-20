using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using MeasureControl.Models;
using MeasureControl.Services;
using MeasureControl.ViewModels;
using MeasureControl.ViewModels.Dialogs;

namespace MeasureControl.Views.Dialogs
{
    public partial class AddChannelDialog : Window
    {
        private readonly AddChannelDialogViewModel _viewModel;

        public ChannelTabelItem ChannelResult { get; private set; }

        public AddChannelDialog(AddChannelDialogViewModel viewModel, IPxiChassisService pxiChassisService)
        {
            InitializeComponent();
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            DataContext = _viewModel;

            // 订阅ViewModel事件
            _viewModel.RequestClose += OnRequestClose;

            // 添加键盘事件处理（使用 PreviewKeyDown 更稳定）
            this.PreviewKeyDown += AddChannelDialog_KeyDown;
            this.Loaded += AddChannelDialog_Loaded;
            this.Closing += AddChannelDialog_Closing;

            // 设置为Topmost确保对话框始终在最前面
            this.Topmost = true;
        }

        private void OnRequestClose()
        {
            try
            {
                ChannelResult = _viewModel.Result;
                base.DialogResult = ChannelResult != null;
                Close();
            }
            catch (Exception)
            {
            }
        }

        private void AddChannelDialog_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                // 取消事件订阅
                if (_viewModel != null)
                {
                    _viewModel.RequestClose -= OnRequestClose;
                }

                this.PreviewKeyDown -= AddChannelDialog_KeyDown;
                this.Loaded -= AddChannelDialog_Loaded;
                this.Closing -= AddChannelDialog_Closing;
            }
            catch (Exception)
            {
            }
        }

        private void DragWindow(object sender, MouseButtonEventArgs e)
        {
            this.DragMove();
        }

        private void AddChannelDialog_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // 激活窗口并延迟设置焦点，确保窗口完全加载后再设置焦点
                Activate();
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    // 设置焦点到通道名称输入框，方便用户直接输入
                    ChannelNameTextBox.Focus();
                }), DispatcherPriority.Input);
            }
            catch
            {
                // 降级方案：直接设置焦点
                ChannelNameTextBox.Focus();
            }
        }

        private void AddChannelDialog_KeyDown(object sender, KeyEventArgs e)
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
                    // 标记为用户已编辑
                    _viewModel?.MarkChannelNameAsUserEdited();
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

        private void ChannelNameTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
            {
                // 标记为用户已编辑
                _viewModel?.MarkChannelNameAsUserEdited();
                // 确认输入，移动焦点
                var textBox = sender as System.Windows.Controls.TextBox;
                textBox?.MoveFocus(new System.Windows.Input.TraversalRequest(System.Windows.Input.FocusNavigationDirection.Next));
                e.Handled = true;
            }
        }

        private void ChannelNameTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            // 当 TextBox 失焦时（包括点击空白区域），标记为用户已编辑
            // 这样可以确保用户输入的内容不会被自动覆盖
            _viewModel?.MarkChannelNameAsUserEdited();
        }

    }
}

