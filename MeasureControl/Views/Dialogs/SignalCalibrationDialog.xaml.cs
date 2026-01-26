using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using MeasureControl.Models;
using MeasureControl.ViewModels.Dialogs;

namespace MeasureControl.Views.Dialogs
{
    public partial class SignalCalibrationDialog : Window
    {
        private readonly SignalCalibrationDialogViewModel _viewModel;

        public SignalConfigItem CalibrationResult { get; private set; }

        public SignalCalibrationDialog(SignalCalibrationDialogViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            DataContext = _viewModel;

            // 订阅ViewModel事件
            _viewModel.RequestClose += OnRequestClose;

            // 添加键盘事件处理（使用 PreviewKeyDown 更稳定）
            this.PreviewKeyDown += SignalCalibrationDialog_KeyDown;
            this.Loaded += SignalCalibrationDialog_Loaded;

            // 设置为Topmost确保对话框始终在最前面
            this.Topmost = true;
        }

        private void OnRequestClose()
        {
            try
            {
                CalibrationResult = _viewModel.Result;
                base.DialogResult = CalibrationResult != null;
                Close();
            }
            catch (Exception)
            {
            }
        }

        private void SignalCalibrationDialog_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                Activate();
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    this.Focus();
                }), DispatcherPriority.Input);
            }
            catch
            {
                this.Focus();
            }
        }

        private void SignalCalibrationDialog_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                _viewModel?.CancelCommand.Execute(null);
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                if (_viewModel?.OkCommand.CanExecute(null) == true)
                {
                    _viewModel.OkCommand.Execute(null);
                    e.Handled = true;
                }
            }
        }

        private void DragWindow(object sender, MouseButtonEventArgs e)
        {
            this.DragMove();
        }
    }
}
