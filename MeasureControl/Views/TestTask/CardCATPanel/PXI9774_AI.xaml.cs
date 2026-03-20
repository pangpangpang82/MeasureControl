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
using ScottPlot.WPF;
using Prism.Regions;

namespace MeasureControl.Views.TestTask.CardCATPanel
{
    /// <summary>
    /// AnalogInputConfigPanel.xaml 的交互逻辑 - 模拟量通道配置面板
    /// </summary>
    public partial class PXI9774_AI : UserControl, IRegionMemberLifetime
    {
        private string _originalCardName;

        public PXI9774_AI()
        {
            InitializeComponent();
            
            // 当TextBox获得焦点时，保存原始名称
            CardNameTextBox.GotFocus += (s, e) =>
            {
                if (DataContext is PXI9774_AIViewModel viewModel)
                {
                    _originalCardName = viewModel.CardName;
                }
            };

            // 当DataContext设置后，传递Canvas和LegendPanel引用给ViewModel
            this.DataContextChanged += (s, e) =>
            {
                if (e.NewValue is PXI9774_AIViewModel viewModel)
                {
                    var plot = this.FindName("WaveformPlot") as WpfPlot;
                    var legend = this.FindName("LegendPanel") as System.Windows.Controls.StackPanel;
                    if (plot != null)
                    {
                        viewModel.SetWaveformPlot(plot, legend);
                    }
                }
            };

            // 延迟查找Canvas（在Loaded事件中）
            this.Loaded += (s, e) =>
            {
                if (DataContext is PXI9774_AIViewModel viewModel)
                {
                    var plot = this.FindName("WaveformPlot") as WpfPlot;
                    var legend = this.FindName("LegendPanel") as System.Windows.Controls.StackPanel;
                    if (plot != null)
                    {
                        viewModel.SetWaveformPlot(plot, legend);
                    }
                }
            };
        }

        public bool KeepAlive
        {
            get
            {
                if (DataContext is PXI9774_AIViewModel vm)
                {
                    return vm.IsBusy || vm.IsDeviceConnected || vm.IsAcquisitionRunning;
                }

                return true;
            }
        }

        /// <summary>
        /// 处理板卡名称TextBox失去焦点事件
        /// </summary>
        private void CardNameTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (DataContext is PXI9774_AIViewModel viewModel)
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
            if (sender is Border border)
            {
                border.Focus();
                e.Handled = true;
            }
        }
    }
}
