using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using MeasureControl.Models;
using MeasureControl.ViewModels.Dialogs;
using Prism.Services.Dialogs;

namespace MeasureControl.Views.Dialogs
{
    /// <summary>
    /// AddIcdMappingDialog.xaml 的交互逻辑
    /// </summary>
    public partial class AddIcdMappingDialog : Window, IDialogWindow
    {
        public IDialogResult Result { get; set; }

        public AddIcdMappingDialog()
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
                var viewModel = DataContext as AddIcdMappingDialogViewModel;
                if (viewModel?.SaveCommand?.CanExecute(null) == true)
                {
                    viewModel.SaveCommand.Execute(null);
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.Escape)
            {
                var viewModel = DataContext as AddIcdMappingDialogViewModel;
                if (viewModel?.CancelCommand?.CanExecute(null) == true)
                {
                    viewModel.CancelCommand.Execute(null);
                    e.Handled = true;
                }
            }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as AddIcdMappingDialogViewModel;
            viewModel?.CancelCommand.Execute(null);
        }

        private void FrameComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox comboBox && comboBox.SelectedItem is IcdFrameItem selectedFrame)
            {
                var viewModel = DataContext as AddIcdMappingDialogViewModel;
                viewModel?.OnFrameSelected(selectedFrame);
            }
        }

        private void DataTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var viewModel = DataContext as AddIcdMappingDialogViewModel;
            viewModel?.OnDataTypeChanged();
        }
    }
}