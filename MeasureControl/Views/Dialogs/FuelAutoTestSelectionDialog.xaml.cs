using System.Windows;
using System.Windows.Input;
using MeasureControl.ViewModels.Dialogs;

namespace MeasureControl.Views.Dialogs
{
    public partial class FuelAutoTestSelectionDialog : Window
    {
        public FuelAutoTestSelectionDialog()
        {
            InitializeComponent();
        }

        public string[] SelectedItems => (DataContext as FuelAutoTestSelectionDialogViewModel)?.SelectedItems;

        public void Initialize(string[] names)
        {
            var vm = new FuelAutoTestSelectionDialogViewModel();
            vm.Initialize(names);
            DataContext = vm;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is FuelAutoTestSelectionDialogViewModel vm)
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
            (DataContext as FuelAutoTestSelectionDialogViewModel)?.SelectAllCommand.Execute();
        }

        private void ClearAllButton_Click(object sender, RoutedEventArgs e)
        {
            (DataContext as FuelAutoTestSelectionDialogViewModel)?.ClearAllCommand.Execute();
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
