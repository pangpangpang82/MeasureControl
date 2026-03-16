using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using MeasureControl.Models;
using MeasureControl.Models.Devices;
using MeasureControl.ViewModels.Dialogs;

namespace MeasureControl.Views.Dialogs
{
    public partial class SelfInspectionDialog : Window
    {
        public SelfInspectionDialog()
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

        public void Initialize(ObservableCollection<ProjectItem> currentProject, SelfInspectionDialogViewModel viewModel)
        {
            DataContext = viewModel;
            viewModel?.Initialize(currentProject);
        }

        public ChassisModel SelectedChassis => (DataContext as SelfInspectionDialogViewModel)?.SelectedChassis;

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            CancelDialog();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is SelfInspectionDialogViewModel vm)
            {
                vm.ValidateSelection();
                if (!vm.IsStartEnabled)
                {
                    ReMessageBox.Show(vm.ValidationMessage ?? "请选择机箱", "提示",
                        MessageBoxButton.OK, MessageBoxImage.Information);
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
