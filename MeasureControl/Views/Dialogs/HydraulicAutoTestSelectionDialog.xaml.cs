using System.Windows;
using System.Windows.Input;
using MeasureControl.ViewModels.Dialogs;

namespace MeasureControl.Views.Dialogs
{
    public partial class HydraulicAutoTestSelectionDialog : Window
    {
        public HydraulicAutoTestSelectionDialog()
        {
            InitializeComponent();
        }

        public string[] SelectedItems => (DataContext as HydraulicAutoTestSelectionDialogViewModel)?.SelectedItems;

        public void Initialize(string[] names, string[] mandatoryNames = null)
        {
            var vm = new HydraulicAutoTestSelectionDialogViewModel();
            vm.Initialize(names, mandatoryNames);
            DataContext = vm;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is HydraulicAutoTestSelectionDialogViewModel vm)
            {
                vm.ValidateSelection();
                if (!vm.CanConfirm)
                {
                    ReMessageBox.Show(vm.ValidationMessage ?? "请至少勾选一个测试项", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
            }

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void SelectAllButton_Click(object sender, RoutedEventArgs e)
        {
            (DataContext as HydraulicAutoTestSelectionDialogViewModel)?.SelectAllCommand.Execute();
        }

        private void ClearAllButton_Click(object sender, RoutedEventArgs e)
        {
            (DataContext as HydraulicAutoTestSelectionDialogViewModel)?.ClearAllCommand.Execute();
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
