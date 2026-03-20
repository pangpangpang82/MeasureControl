using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
using MeasureControl.ViewModels.TdmSystem;

namespace MeasureControl.Views.TdmSystem
{
    /// <summary>
    /// TDMSystem.xaml 的交互逻辑
    /// </summary>
    public partial class TDMSystem : UserControl
    {
        private bool _isFloating = false;
        private Image _floatButtonImage;
        private string _currentPageKey = null;  // 保存当前浮动窗口的pageKey

        public TDMSystem()
        {
            InitializeComponent();
            Loaded += TDMSystem_Loaded;
        }

        private void TDMSystem_Loaded(object sender, RoutedEventArgs e)
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
            string pageName = "TDMSystem";  // 使用PageType而非中文名

            // 获取RegionManager、EventAggregator、NavigationStateService、NavigationService和MainWindowViewModel
            var containerProvider = (Application.Current as App)?.Container;
            var regionManager = containerProvider?.Resolve(typeof(Prism.Regions.IRegionManager)) as Prism.Regions.IRegionManager;
            var eventAggregator = containerProvider?.Resolve(typeof(Prism.Events.IEventAggregator)) as Prism.Events.IEventAggregator;
            var navigationState = containerProvider?.Resolve(typeof(INavigationStateService)) as INavigationStateService;
            var navigationService = containerProvider?.Resolve(typeof(INavigationService)) as INavigationService;
            var mainViewModel = containerProvider?.Resolve(typeof(MainWindowViewModel)) as MainWindowViewModel;

            if (regionManager != null && eventAggregator != null && navigationState != null && mainViewModel != null)
            {
                // 通过Helper浮动整个页面
                _currentPageKey = FloatingWindowHelper.FloatPage(
                    pageName,
                    this,
                    regionManager,
                    eventAggregator,
                    navigationState,
                    (nextPage) => mainViewModel.NavigateToPage(nextPage),
                    navigationService
                );
                
                if (!string.IsNullOrEmpty(_currentPageKey))
                {
                    _isFloating = true;
                    UpdateFloatIcon();
                }
                else
                {
                }
            }
            else
            {
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
                
                // 使用保存的pageKey
                if (FloatingWindowHelper.EmbedPage(_currentPageKey, regionManager, eventAggregator, navigationState))
                {
                    _currentPageKey = null;  // 清空pageKey
                    _isFloating = false;
                    UpdateFloatIcon();
                }
                else
                {
                }
            }
            else
            {
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
                var vm = DataContext as TDMSystemViewModel;
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

        private void TextBox_TextChanged(object sender, TextChangedEventArgs e)
        {

        }

        /// <summary>
        /// 点击非输入区域时清除焦点
        /// </summary>
        private void ClearFocusOnOutsideClick(object sender, MouseButtonEventArgs e)
        {
            // 获取点击的原始元素
            var clickedElement = e.OriginalSource as DependencyObject;

            // 判断是否点击在输入控件上
            if (IsClickOnInputControl(clickedElement))
            {
                return; // 如果点击在输入控件上，不做任何处理
            }

            // 清除焦点：将焦点移到最外层Border上
            var border = sender as Border;
            if (border != null)
            {
                border.Focusable = true;
                Keyboard.ClearFocus();
                border.Focus();
                border.Focusable = false;
            }

            // 关闭所有打开的ComboBox下拉框
            CloseAllComboBoxDropdowns(this);
        }

        /// <summary>
        /// 判断点击是否在输入控件上
        /// </summary>
        private bool IsClickOnInputControl(DependencyObject element)
        {
            while (element != null)
            {
                // 检查是否是输入控件
                if (element is TextBox || 
                    element is ComboBox || 
                    element is ComboBoxItem ||
                    element is PasswordBox ||
                    element is RichTextBox)
                {
                    return true;
                }

                // 检查是否是ComboBox的下拉框部分（Popup）
                if (element is Popup popup && popup.IsOpen)
                {
                    return true;
                }

                // 检查是否是ToggleButton（ComboBox的下拉按钮）
                if (element is ToggleButton)
                {
                    // 检查父级是否是ComboBox
                    var parent = VisualTreeHelper.GetParent(element);
                    while (parent != null)
                    {
                        if (parent is ComboBox)
                        {
                            return true;
                        }
                        parent = VisualTreeHelper.GetParent(parent);
                    }
                }

                // 检查是否是ScrollBar或ScrollViewer（输入控件的滚动条）
                if (element is ScrollBar || element is ScrollViewer)
                {
                    var parent = VisualTreeHelper.GetParent(element);
                    while (parent != null)
                    {
                        if (parent is TextBox || parent is ComboBox)
                        {
                            return true;
                        }
                        parent = VisualTreeHelper.GetParent(parent);
                    }
                }

                // 向上遍历可视树
                element = VisualTreeHelper.GetParent(element);
            }

            return false;
        }

        /// <summary>
        /// 关闭所有ComboBox的下拉框
        /// </summary>
        private void CloseAllComboBoxDropdowns(DependencyObject parent)
        {
            if (parent == null) return;

            int childCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                // 如果是ComboBox且下拉框是打开的，关闭它
                if (child is ComboBox comboBox && comboBox.IsDropDownOpen)
                {
                    comboBox.IsDropDownOpen = false;
                }

                // 递归检查子元素
                CloseAllComboBoxDropdowns(child);
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
