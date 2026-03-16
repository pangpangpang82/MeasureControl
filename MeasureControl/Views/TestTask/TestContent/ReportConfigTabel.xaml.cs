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
using MeasureControl.Helpers;
using MeasureControl.Services;
using MeasureControl.ViewModels;
using MeasureControl.ViewModels.Common;

namespace MeasureControl.Views.TestContent
{
    /// <summary>
    /// ReportConfigTabel.xaml 的交互逻辑
    /// </summary>
    public partial class ReportConfigTabel : UserControl
    {
        private bool _isFloating = false;
        private Image _floatButtonImage;
        private string _currentPageKey = null;

        public ReportConfigTabel()
        {
            InitializeComponent();
            Loaded += ReportConfigTabel_Loaded;
        }

        private void ReportConfigTabel_Loaded(object sender, RoutedEventArgs e)
        {
            _floatButtonImage = FindName("FloatImage") as Image;
        }

        public void OnFloatButtonClick(object sender, RoutedEventArgs e)
        {
            if (_isFloating)
            {
                EmbedWindow();
            }
            else
            {
                FloatWindow();
            }
        }

        private void FloatWindow()
        {
            var vm = DataContext as ReportConfigTabelViewModel;
            // 使用PageType，GeneratePageKey会自动从ViewModel.ConfigId获取实例标识
            string pageName = "ReportConfigTabel";

            // 获取RegionManager、EventAggregator、NavigationStateService、NavigationService和MainWindowViewModel
            var containerProvider = (Application.Current as App)?.Container;
            var regionManager = containerProvider?.Resolve(typeof(Prism.Regions.IRegionManager)) as Prism.Regions.IRegionManager;
            var eventAggregator = containerProvider?.Resolve(typeof(Prism.Events.IEventAggregator)) as Prism.Events.IEventAggregator;
            var navigationState = containerProvider?.Resolve(typeof(INavigationStateService)) as INavigationStateService;
            var navigationService = containerProvider?.Resolve(typeof(INavigationService)) as INavigationService;
            var mainViewModel = containerProvider?.Resolve(typeof(MainWindowViewModel)) as MainWindowViewModel;

            if (regionManager != null && eventAggregator != null && navigationState != null && mainViewModel != null)
            {
                // 诊断日志：检查 ViewModel 属性
                
                // 从 ViewModel 构建正确的 PageKey：PageType_任务名-配置表名
                string explicitPageKey = null;
                if (vm != null && !string.IsNullOrEmpty(vm.TestTaskName) && !string.IsNullOrEmpty(vm.ConfigTabelName))
                {
                    explicitPageKey = $"{pageName}_{vm.TestTaskName}-{vm.ConfigTabelName}";
                }
                else
                {
                }
                
                // 通过Helper浮动整个页面，传递明确的PageKey
                _currentPageKey = FloatingWindowHelper.FloatPage(
                    pageName,
                    this,
                    regionManager,
                    eventAggregator,
                    navigationState,
                    (nextPage) => mainViewModel.NavigateToPage(nextPage),
                    navigationService,
                    explicitPageKey
                );
                
                if (!string.IsNullOrEmpty(_currentPageKey))
                {
                    _isFloating = true;
                    UpdateFloatIcon();
                }
            }
        }

        private void EmbedWindow()
        {
            if (string.IsNullOrEmpty(_currentPageKey))
            {
                return;
            }

            // 获取RegionManager、EventAggregator和NavigationStateService
            var containerProvider = (Application.Current as App)?.Container;
            var regionManager = containerProvider?.Resolve(typeof(Prism.Regions.IRegionManager)) as Prism.Regions.IRegionManager;
            var eventAggregator = containerProvider?.Resolve(typeof(Prism.Events.IEventAggregator)) as Prism.Events.IEventAggregator;
            var navigationState = containerProvider?.Resolve(typeof(INavigationStateService)) as INavigationStateService;

            if (regionManager != null && eventAggregator != null)
            {
                // 通过Helper嵌入页面，使用保存的pageKey
                if (FloatingWindowHelper.EmbedPage(_currentPageKey, regionManager, eventAggregator, navigationState))
                {
                    _currentPageKey = null;
                    _isFloating = false;
                    UpdateFloatIcon();
                }
            }
        }

        public void OnMinimizeButtonClick(object sender, RoutedEventArgs e)
        {
            // 查找当前所在的窗口（可能是主窗口或浮动窗口）
            Window window = Window.GetWindow(this);
            if (window != null)
            {
                // 在浮动窗口中执行最小化
                if (window.GetType().Name == "FloatingWindow")
                {
                    // 调用FloatingWindowViewModel的MinimizeCommand处理最小化逻辑
                    var floatingVM = window.DataContext as FloatingWindowViewModel;
                    floatingVM?.MinimizeCommand.Execute();
                }
                else
                {
                    // 在嵌入模式下，隐藏当前视图（不需要提示框）
                    var eventAggregator = (Application.Current as App)?.Container?.Resolve(typeof(Prism.Events.IEventAggregator)) as Prism.Events.IEventAggregator;
                    eventAggregator?.GetEvent<MeasureControl.Events.HideCurrentPageEvent>().Publish(new MeasureControl.Events.HideCurrentPageEventArgs { IsMinimize = true });
                }
            }
        }

        public void OnCloseButtonClick(object sender, RoutedEventArgs e)
        {
            // 检查是否在浮动窗口中
            Window window = Window.GetWindow(this);
            if (window != null && window.GetType().Name == "FloatingWindow")
            {
                // 在浮动窗口中，直接关闭窗口（FloatingWindowViewModel会处理确认对话框）
                var floatingVM = window.DataContext as FloatingWindowViewModel;
                floatingVM?.CloseCommand.Execute();
            }
            else
            {
                // 在嵌入模式下，调用ViewModel的关闭命令
                var vm = DataContext as ReportConfigTabelViewModel;
                vm?.CloseInRegionCommand.Execute();
            }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 只在浮动窗口时支持拖动
            Window window = Window.GetWindow(this);
            if (window != null && window.GetType().Name == "FloatingWindow")
            {
                if (e.ChangedButton == MouseButton.Left)
                {
                    window.DragMove();
                }
            }
        }

        /// <summary>
        /// 更新浮动按钮图标
        /// </summary>
        private void UpdateFloatIcon()
        {
            if (_floatButtonImage != null)
            {
                if (_isFloating)
                {
                    // 浮动时显示嵌入图标
                    _floatButtonImage.Source = new BitmapImage(new Uri("/Resources/Logo/embed.png", UriKind.Relative));
                    _floatButtonImage.Width = 15;
                }
                else
                {
                    // 嵌入时显示浮动图标
                    _floatButtonImage.Source = new BitmapImage(new Uri("/Resources/Logo/float.png", UriKind.Relative));
                    _floatButtonImage.Width = 15;
                }
            }
        }
    }
}
