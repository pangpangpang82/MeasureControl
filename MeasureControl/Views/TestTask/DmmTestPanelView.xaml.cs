using System.Windows.Controls;
using MeasureControl.ViewModels.TestTask;
using Prism.Mvvm;

namespace MeasureControl.Views.TestTask
{
    /// <summary>
    /// DmmTestPanelView.xaml 的交互逻辑
    /// </summary>
    public partial class DmmTestPanelView : UserControl
    {
        public DmmTestPanelView()
        {
            InitializeComponent();

            Loaded += (_, __) => (DataContext as DmmTestPanelViewModel)?.OnViewLoaded();
            
            // 设置ViewModelLocator
            if (ViewModelLocator.GetAutoWireViewModel(this) == null)
            {
                ViewModelLocator.SetAutoWireViewModel(this, true);
            }
        }
    }
}

