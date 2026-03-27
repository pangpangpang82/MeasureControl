using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace MeasureControl.Views.Dialogs
{
    /// <summary>
    /// CreateSingleBoardTestTaskDialog.xaml 的交互逻辑
    /// </summary>
    public partial class CreateSingleBoardTestTaskDialog : Window
    {
        public string SelectedBoardType { get; private set; }
        public string TaskName { get; private set; }

        private double _compactHeight;
        private double _expandedHeight;
        private const double CustomRowHeightDelta = 40;
        private const double MinCompactHeight = 122;

        public CreateSingleBoardTestTaskDialog()
        {
            InitializeComponent();

            PreviewKeyDown += OnPreviewKeyDown;
            Loaded += OnLoaded;

            BoardTypeComboBox.ItemsSource = new List<string>
            {
                "空气控制板",
                "空气功率板",
                "空气安全板",
                "液压单板",
                "惰化模拟板",
                "惰化控制板",
                "加放油单板",
                "自定义单板"
            };

            BoardTypeComboBox.SelectedIndex = 0;

            _compactHeight = Height;
            _compactHeight = Math.Max(_compactHeight, MinCompactHeight);
            _expandedHeight = _compactHeight + CustomRowHeightDelta;

            MinHeight = _compactHeight;
            if (Height < _compactHeight)
            {
                Height = _compactHeight;
            }

            UpdateCustomNameVisibility();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                Activate();
                Dispatcher.BeginInvoke(new System.Action(() =>
                {
                    BoardTypeComboBox.Focus();
                }), DispatcherPriority.Input);
            }
            catch
            {
                BoardTypeComboBox.Focus();
            }
        }

        public void OnBoardTypeChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            UpdateCustomNameVisibility();
        }

        private void UpdateCustomNameVisibility()
        {
            var selected = BoardTypeComboBox.SelectedItem as string;
            var isCustom = selected == "自定义单板";

            if (CustomNameRow != null)
            {
                CustomNameRow.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;
            }

            Height = isCustom ? _expandedHeight : _compactHeight;

            if (!isCustom && CustomNameTextBox != null)
            {
                CustomNameTextBox.Text = string.Empty;
            }

            if (isCustom && CustomNameTextBox != null)
            {
                Dispatcher.BeginInvoke(new System.Action(() =>
                {
                    CustomNameTextBox.Focus();
                    CustomNameTextBox.SelectAll();
                }), DispatcherPriority.Input);
            }
        }

        public void OnOkClick(object sender, RoutedEventArgs e)
        {
            var selected = BoardTypeComboBox.SelectedItem as string;
            if (string.IsNullOrWhiteSpace(selected))
            {
                return;
            }

            var name = selected == "自定义单板" ? CustomNameTextBox.Text?.Trim() : selected;
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            SelectedBoardType = selected;
            TaskName = name;

            DialogResult = true;
            Close();
        }

        public void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        public void DragWindow(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                OnOkClick(sender, e);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                OnCancelClick(sender, e);
                e.Handled = true;
            }
        }
    }
}
