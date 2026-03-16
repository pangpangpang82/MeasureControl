using System;
using System.Windows.Controls;
using System.Windows.Forms.Integration;
using System.Windows.Media;
using MeasureControl.ViewModels.TestTask;
using MeasureControl.ViewModels.TestTask.CardCATPanel.MIL1394B;
using Prism.Mvvm;

namespace MeasureControl.Views.TestTask.CardCATPanel.Mil1394B
{
    /// <summary>
    /// Mil1394TestPanel.xaml 的交互逻辑
    /// </summary>
    public partial class Mil1394TestPanel : UserControl
    {
        private Mil1394TestPanelViewModel _viewModel;

        private string _originalCardName;

        public Mil1394TestPanel()
        {
            InitializeComponent();
            this.DataContextChanged += Mil1394TestPanel_DataContextChanged;
            this.Loaded += Mil1394TestPanel_Loaded;
        }

        private void SetupCardNameTextBox()
        {
            // 当TextBox获得焦点时，保存原始名称
            if (CardNameTextBox != null)
            {
                CardNameTextBox.GotFocus += (sender, args) =>
                {
                    if (DataContext is Mil1394TestPanelViewModel viewModel)
                    {
                        _originalCardName = viewModel.CardName;
                    }
                };
            }
        }

        private void CardNameTextBox_LostFocus(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext is Mil1394TestPanelViewModel viewModel)
            {
                viewModel.OnCardNameChanged(_originalCardName);
            }
        }

        private void Mil1394TestPanel_Loaded(object sender, System.Windows.RoutedEventArgs e)
        {
            // 确保UserControl限制在父容器内，不超出边界
            this.ClipToBounds = true;
            this.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
            this.VerticalAlignment = System.Windows.VerticalAlignment.Stretch;

            // 确保所有容器都限制内容
            if (HostBorder != null)
            {
                HostBorder.ClipToBounds = true;
            }

            if (ContentGrid != null)
            {
                ContentGrid.ClipToBounds = true;
            }

            // 监听HostBorder大小变化
            if (HostBorder != null)
            {
                HostBorder.SizeChanged += HostBorder_SizeChanged;
                // 初始应用裁剪
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    ApplyClipping();
                }), System.Windows.Threading.DispatcherPriority.Loaded);
            }

            // 设置CardNameTextBox事件处理
            SetupCardNameTextBox();
        }

        private void HostBorder_SizeChanged(object sender, System.Windows.SizeChangedEventArgs e)
        {
            // 使用显式的几何裁剪来强制限制内容不会超出边界
            ApplyClipping();
        }

        private void ApplyClipping()
        {
            // 为所有容器应用显式的几何裁剪，确保内容不会超出边界
            if (HostBorder != null)
            {
                var borderRect = new System.Windows.Rect(0, 0, HostBorder.ActualWidth, HostBorder.ActualHeight);
                HostBorder.Clip = new System.Windows.Media.RectangleGeometry(borderRect);
            }

            if (ContentGrid != null)
            {
                var gridRect = new System.Windows.Rect(0, 0, ContentGrid.ActualWidth, ContentGrid.ActualHeight);
                ContentGrid.Clip = new System.Windows.Media.RectangleGeometry(gridRect);
            }
        }

        private void Mil1394TestPanel_DataContextChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
        {
            // 取消之前的订阅
            if (_viewModel != null)
            {
                _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
            }

            // 订阅新的ViewModel
            _viewModel = e.NewValue as Mil1394TestPanelViewModel;
            if (_viewModel != null)
            {
                _viewModel.PropertyChanged += ViewModel_PropertyChanged;

                // 先设置一次（可能为null）
                UpdateWpfContent();

                // 然后初始化（这会创建控件并触发PropertyChanged）
                _viewModel.Initialize();
            }
        }

        private void ViewModel_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Mil1394TestPanelViewModel.WpfContent))
            {
                // 使用Dispatcher.BeginInvoke避免在属性更改期间更新UI
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    UpdateWpfContent();
                }), System.Windows.Threading.DispatcherPriority.Normal);
            }
        }

        private void UpdateWpfContent()
        {
            if (_viewModel == null || ContentGrid == null)
                return;

            var newContent = _viewModel.WpfContent;

            // 清除旧内容
            ContentGrid.Children.Clear();

            // 设置新内容
            if (newContent != null)
            {
                ContentGrid.Children.Add(newContent);
            }
        }

        protected override void OnInitialized(EventArgs e)
        {
            base.OnInitialized(e);
        }
    }
}
