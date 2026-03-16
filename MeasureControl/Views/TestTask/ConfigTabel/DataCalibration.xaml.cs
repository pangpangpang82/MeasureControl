using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using MeasureControl.ViewModels.TestTask.ConfigTabel;
using System.Text.RegularExpressions;
using MeasureControl.Views.Dialogs;

namespace MeasureControl.Views.ConfigTabel
{
    /// <summary>
    /// DataCalibration.xaml 的交互逻辑
    /// </summary>
    public partial class DataCalibration : UserControl
    {
        public DataCalibration()
        {
            System.Diagnostics.Debug.WriteLine("[DataCalibration] DataCalibration UserControl constructor called");
            InitializeComponent();
            System.Diagnostics.Debug.WriteLine($"[DataCalibration] DataCalibration UserControl initialized. DataContext type: {DataContext?.GetType()?.Name ?? "null"}");

            // 监听DataContext变化
            DataContextChanged += DataCalibration_DataContextChanged;

            // 监听Loaded事件，确保界面每次显示时都加载AI0数据
            Loaded += DataCalibration_Loaded;
        }

        private void DataCalibration_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"[DataCalibration] DataContext changed from {e.OldValue?.GetType()?.Name ?? "null"} to {e.NewValue?.GetType()?.Name ?? "null"}");
        }

        private void DataCalibration_Loaded(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("[DataCalibration] DataCalibration_Loaded called");

            // 确保每次界面显示时都加载当前选中通道的数据到UI（默认为AI0）
            if (DataContext is DataCalibrationViewModel viewModel)
            {
                System.Diagnostics.Debug.WriteLine("[DataCalibration] Loading current channel data from Loaded event");
                // 使用Dispatcher延迟调用，确保UI完全加载后再执行
                Dispatcher.InvokeAsync(() =>
                {
                    viewModel.LoadCurrentChannelData();
                }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }
        }

        private void MeasurementPointCountTextBox_LostFocus(object sender, System.Windows.RoutedEventArgs e)
        {
            // 当测量点数失去焦点时，ViewModel会自动更新输入框数量
            // 这里不需要额外处理，因为绑定已经设置了UpdateSourceTrigger=LostFocus
        }

        /// <summary>
        /// 点击空白区域时让当前焦点元素失去焦点
        /// </summary>
        private void Border_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // 将焦点移到Border上，使当前输入框失去焦点
            if (sender is Border border)
            {
                border.Focus();
            }
        }

        private static readonly Regex _numericRegex = new Regex(@"^-?\d*(\.\d*)?$", RegexOptions.Compiled);

        private void NumericTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            if (sender is not TextBox textBox)
                return;

            // 预测输入后的文本
            var current = textBox.Text ?? string.Empty;
            var selectionStart = textBox.SelectionStart;
            var selectionLength = textBox.SelectionLength;
            var newText = current.Remove(selectionStart, selectionLength).Insert(selectionStart, e.Text);

            e.Handled = !_numericRegex.IsMatch(newText);
        }

        private void NumericTextBox_Pasting(object sender, DataObjectPastingEventArgs e)
        {
            if (sender is not TextBox textBox)
                return;

            if (!e.DataObject.GetDataPresent(DataFormats.UnicodeText))
            {
                e.CancelCommand();
                return;
            }

            var pasteText = e.DataObject.GetData(DataFormats.UnicodeText) as string ?? string.Empty;

            var current = textBox.Text ?? string.Empty;
            var selectionStart = textBox.SelectionStart;
            var selectionLength = textBox.SelectionLength;
            var newText = current.Remove(selectionStart, selectionLength).Insert(selectionStart, pasteText);

            if (!_numericRegex.IsMatch(newText))
            {
                e.CancelCommand();
            }
        }

        private void NumericTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is not TextBox textBox)
                return;

            var text = textBox.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(text))
                return;

            if (double.TryParse(text, out _))
                return;

            ReMessageBox.Show("存在不合法输入", "提示",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);

            textBox.Text = string.Empty;
            textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        }
    }
}
