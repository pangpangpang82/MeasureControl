using System.Collections.Generic;
using System.Windows;

namespace MeasureControl.Views.Dialogs
{
    public partial class PowerBoardSelectDialog : Window
    {
        public static readonly IReadOnlyList<string> AvailableBoardTypes = new List<string>
        {
            "液压单板",
            "惰化模拟板",
            "惰化控制板",
        };

        public string SelectedBoardType { get; private set; }

        public PowerBoardSelectDialog()
        {
            InitializeComponent();
            BoardTypeComboBox.ItemsSource = AvailableBoardTypes;
            BoardTypeComboBox.SelectedIndex = 0;
        }

        private void OnOkClick(object sender, RoutedEventArgs e)
        {
            SelectedBoardType = BoardTypeComboBox.SelectedItem as string;
            if (string.IsNullOrEmpty(SelectedBoardType)) return;
            DialogResult = true;
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void DragWindow(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
                DragMove();
        }
    }
}
