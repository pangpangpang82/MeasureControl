using System;
using System.Collections.Generic;
using System.Windows;

namespace MeasureControl.Views.Dialogs
{
    public partial class PowerOnPromptDialog : Window
    {
        public static readonly IReadOnlyList<string> AvailableVoltages = new List<string>
        {
            "18V",
            "28V",
            "32.2V",
        };

        public double SelectedVoltage { get; private set; } = 28.0;

        public PowerOnPromptDialog(string boardType, bool showVoltage)
        {
            InitializeComponent();
            MessageText.Text = $"{boardType} 尚未上电，是否现在为其上电？";
            if (showVoltage)
            {
                VoltageComboBox.ItemsSource = AvailableVoltages;
                VoltageComboBox.SelectedItem = "28V";
                VoltageRowSpacer.Visibility = Visibility.Visible;
                VoltageLabelText.Visibility = Visibility.Visible;
                VoltageComboBox.Visibility = Visibility.Visible;
            }
        }

        /// <summary>
        /// 在 UI 线程上显示对话框并返回确认结果和选择的电压。
        /// 可从任意线程调用（内部使用 Dispatcher.Invoke）。
        /// </summary>
        public static (bool Confirmed, double SelectedVoltage) ShowPrompt(string boardType, bool showVoltage)
        {
            bool confirmed = false;
            double voltage = 28.0;
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                var dlg = new PowerOnPromptDialog(boardType, showVoltage)
                {
                    Owner = Application.Current?.MainWindow
                };
                confirmed = dlg.ShowDialog() == true;
                if (confirmed)
                    voltage = dlg.SelectedVoltage;
            });
            return (confirmed, voltage);
        }

        private void OnOkClick(object sender, RoutedEventArgs e)
        {
            if (VoltageComboBox.Visibility == Visibility.Visible &&
                VoltageComboBox.SelectedItem is string vs &&
                double.TryParse(vs.TrimEnd('V'), out double v))
                SelectedVoltage = v;
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
