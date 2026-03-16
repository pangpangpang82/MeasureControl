using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using MeasureControl.ViewModels.Dialogs;

namespace MeasureControl.Views.Dialogs
{
    /// <summary>
    /// BC消息编辑对话框
    /// </summary>
    public partial class BCMessageEditDialog : Window
    {
        public BCMessageEditDialog()
        {
            InitializeComponent();
        }

        public BCMessageEditDialog(BCMessageEditDialogViewModel viewModel) : this()
        {
            DataContext = viewModel;
            
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
                OKButton_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                CancelButton_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void OKButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is BCMessageEditDialogViewModel viewModel)
            {
                if (viewModel.Validate())
                {
                    DialogResult = true;
                    Close();
                }
            }
        }

        private void TextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {

        }
    }
}

