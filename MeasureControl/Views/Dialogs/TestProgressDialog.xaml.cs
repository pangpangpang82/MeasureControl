using System.Windows;
using System.ComponentModel;
using System.Windows.Input;
using MeasureControl.ViewModels.Dialogs;

namespace MeasureControl.Views.Dialogs
{
    public partial class TestProgressDialog : Window
    {
        public TestProgressDialog()
        {
            InitializeComponent();
            Closing += OnClosing;
        }

        private void OnClosing(object sender, CancelEventArgs e)
        {
            if (DataContext is not TestProgressDialogViewModel vm)
            {
                return;
            }

            if (!vm.ConfirmStopOnClose)
            {
                return;
            }

            var header = string.IsNullOrWhiteSpace(vm.HeaderText) ? "当前单板" : vm.HeaderText;
            var result = ReMessageBox.Show($"是否终止{header}的测试？", "确认", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes)
            {
                e.Cancel = true;
                return;
            }

            vm.RequestCancel?.Invoke();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState != MouseButtonState.Pressed)
            {
                return;
            }

            try
            {
                DragMove();
            }
            catch
            {
            }
        }
    }
}
