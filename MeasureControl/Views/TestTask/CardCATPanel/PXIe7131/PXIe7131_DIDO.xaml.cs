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
using MeasureControl.ViewModels;
using MeasureControl.ViewModels.TestTask.CardCATPanel;
using Prism.Regions;

namespace MeasureControl.Views.TestTask.CardCATPanel.PXIe7131
{
    /// <summary>
    /// DiscreteIOConfigPanel.xaml 的交互逻辑 - 离散量通道配置面板
    /// </summary>
    public partial class PXIe7131_DIDO : UserControl, IRegionMemberLifetime
    {
        private string _originalCardName;

        public PXIe7131_DIDO()
        {
            InitializeComponent();

            // 当TextBox获得焦点时，保存原始名称
            CardNameTextBox.GotFocus += (s, e) =>
            {
                if (DataContext is PXIe7131_DIDOViewModel viewModel)
                {
                    _originalCardName = viewModel.CardName;
                }
            };
        }

        public bool KeepAlive
        {
            get
            {
                if (DataContext is PXIe7131_DIDOViewModel vm)
                {
                    return vm.IsBusy || vm.IsDeviceConnected || vm.IsOutputRunning;
                }

                return true;
            }
        }

        /// <summary>
        /// 处理板卡名称TextBox失去焦点事件
        /// </summary>
        private void CardNameTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (DataContext is PXIe7131_DIDOViewModel viewModel)
            {
                viewModel.OnCardNameChanged(_originalCardName);
            }
        }

        /// <summary>
        /// 处理Border鼠标点击事件，用于转移焦点
        /// </summary>
        private void Border_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // 点击空白区域时，将焦点转移到Border，使TextBox失去焦点
            // 注意：不要拦截子控件的点击（按钮/滚动等），否则会影响正常交互
            if (sender is Border border && ReferenceEquals(e.OriginalSource, border))
            {
                border.Focus();
                e.Handled = true;
            }
        }
    }
}
