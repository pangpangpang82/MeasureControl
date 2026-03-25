using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MeasureControl.ViewModels.SingleBoardTest.InertController;
using Prism.Ioc;

namespace MeasureControl.Views.SingleBoardTest.InertController
{
    public partial class TemperatureSensorSignalAcquisitionTestView : UserControl
    {
        private const double MinOhm = 350.0;

        private const double MaxOhm = 1700.0;

        public TemperatureSensorSignalAcquisitionTestView()
        {
            InitializeComponent();
            DataContext = ContainerLocator.Container.Resolve<TemperatureSensorSignalAcquisitionTestViewModel>();
        }



        private void ResistanceTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)

        {

            if (sender is not TextBox tb)

                return;

            e.Handled = !WillBeValidInput(tb, e.Text);

        }



        private void ResistanceTextBox_Pasting(object sender, DataObjectPastingEventArgs e)

        {

            if (sender is not TextBox tb)

                return;

            if (!e.DataObject.GetDataPresent(DataFormats.Text))

            {

                e.CancelCommand();

                return;

            }

            var pasteText = e.DataObject.GetData(DataFormats.Text) as string ?? string.Empty;

            if (!WillBeValidInput(tb, pasteText))

                e.CancelCommand();

        }



        private void ResistanceTextBox_LostFocus(object sender, RoutedEventArgs e)

        {

            if (sender is not TextBox tb)

                return;

            var text = tb.Text?.Trim();

            if (string.IsNullOrEmpty(text))

                return;

            if (!TryParseDouble(text, out var value))

            {

                tb.Text = string.Empty;

                return;

            }

            if (value < MinOhm)

                tb.Text = MinOhm.ToString("F0", CultureInfo.InvariantCulture);

        }



        private static bool TryParseDouble(string text, out double value)

        {

            text = (text ?? string.Empty).Trim();

            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)

                   || double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);

        }



        private static bool IsTextAllowed(string text)

        {

            if (string.IsNullOrEmpty(text))

                return true;

            if (text.Count(c => c == '.') > 1)

                return false;

            foreach (var c in text)

            {

                if (!(char.IsDigit(c) || c == '.'))

                    return false;

            }

            return true;

        }



        private static string GetProspectiveText(TextBox tb, string newText)

        {

            var old = tb.Text ?? string.Empty;

            var start = tb.SelectionStart;

            var length = tb.SelectionLength;

            if (start < 0) start = 0;

            if (start > old.Length) start = old.Length;

            if (length < 0) length = 0;

            if (start + length > old.Length) length = old.Length - start;

            return old.Remove(start, length).Insert(start, newText);

        }



        private static bool WillBeValidInput(TextBox tb, string input)

        {

            var next = GetProspectiveText(tb, input).Trim();

            if (!IsTextAllowed(next))

                return false;

            if (next == string.Empty || next == ".")

                return true;

            if (!TryParseDouble(next, out var value))

                return false;

            if (value > MaxOhm)

                return false;

            return true;

        }
    }
}
