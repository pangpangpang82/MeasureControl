using System.Windows.Controls;
using MeasureControl.ViewModels.TestTask.CardCATPanel;
using Prism.Regions;

namespace MeasureControl.Views.TestTask.CardCATPanel
{
    public partial class MTX970_LVDS : UserControl, IRegionMemberLifetime
    {
        public MTX970_LVDS()
        {
            InitializeComponent();
        }

        public bool KeepAlive
        {
            get
            {
                if (DataContext is MTX970_LVDSViewModel vm)
                {
                    return vm.IsBusy || vm.IsConnected || vm.IsTesting;
                }

                return true;
            }
        }
    }
}
