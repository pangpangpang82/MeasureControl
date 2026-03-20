using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using MeasureControl.ViewModels.Dialogs;

namespace MeasureControl.Views.Dialogs
{
    public partial class FreqVoltageRangeDialog : Window
    {
        public FreqVoltageRangeDialog()
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
                    this.Focus();
                }), DispatcherPriority.Input);
            }
            catch
            {
                this.Focus();
            }
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                OkButton_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                CancelDialog();
                e.Handled = true;
            }
        }

        public int? SelectedIndex => (DataContext as FreqVoltageRangeDialogViewModel)?.SelectedOption?.Index;

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            CancelDialog();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is FreqVoltageRangeDialogViewModel vm)
            {
                if (vm.SelectedOption == null)
                {
                    DialogResult = false;
                    Close();
                    return;
                }
            }

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            CancelDialog();
        }

        private void CancelDialog()
        {
            DialogResult = false;
            Close();
        }
    }
}
