using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace MeasureControl.Views.Dialogs
{
    public partial class PowerBoardSelectDialog : Window
    {
        public static readonly IReadOnlyList<string> AvailableBoardTypes = new List<string>
        {
            "液压单板",
            "加放油单板",
            "惰化模拟板",
            "惰化控制板",
        };

        public static readonly IReadOnlyList<string> AvailableVoltages = new List<string>
        {
            "18V",
            "28V",
            "32.2V",
        };

        public string SelectedBoardType { get; private set; }
        public double SelectedVoltage { get; private set; } = 28.0;

        public PowerBoardSelectDialog()
        {
            InitializeComponent();
            BoardTypeComboBox.ItemsSource = AvailableBoardTypes;
            VoltageComboBox.ItemsSource = AvailableVoltages;
            VoltageComboBox.SelectedItem = "28V";
            BoardTypeComboBox.SelectedIndex = 0;
        }

        private void BoardTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            bool showVoltage = string.Equals(
                BoardTypeComboBox.SelectedItem as string,
                "加放油单板",
                StringComparison.OrdinalIgnoreCase);
            var vis = showVoltage ? Visibility.Visible : Visibility.Collapsed;
            VoltageRowSpacer.Visibility = vis;
            VoltageLabelText.Visibility = vis;
            VoltageComboBox.Visibility = vis;
            if (!showVoltage)
                SelectedVoltage = 28.0;
        }

        private void OnOkClick(object sender, RoutedEventArgs e)
        {
            SelectedBoardType = BoardTypeComboBox.SelectedItem as string;
            if (string.IsNullOrEmpty(SelectedBoardType)) return;
            if (VoltageComboBox.SelectedItem is string voltageStr &&
                double.TryParse(voltageStr.TrimEnd('V'), out double voltage))
                SelectedVoltage = voltage;
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
