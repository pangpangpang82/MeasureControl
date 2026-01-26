using System.Windows;

namespace MeasureControl.Views.Dialogs
{
    public partial class TestProgressDialog : Window
    {
        public TestProgressDialog()
        {
            InitializeComponent();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
