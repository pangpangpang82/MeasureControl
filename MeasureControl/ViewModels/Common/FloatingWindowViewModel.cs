using System;
using System.Windows;
using Prism.Commands;
using Prism.Events;
using Prism.Mvvm;
using Prism.Regions;
using MeasureControl.Events;
using MeasureControl.Helpers;
using MeasureControl.Services;
using MeasureControl.Views;
using MeasureControl.Views.Dialogs;

namespace MeasureControl.ViewModels.Common
{
    public class FloatingWindowViewModel : BindableBase
    {
        private readonly IEventAggregator _eventAggregator;
        private readonly IRegionManager _regionManager;
        private readonly INavigationStateService _navigationState;
        private string _windowTitle;
        private string _pageKey;
        private Action _onRestore;
        private Action<string> _navigateAction;

        public string WindowTitle
        {
            get => _windowTitle;
            set => SetProperty(ref _windowTitle, value);
        }

        public DelegateCommand RestoreCommand { get; }
        public DelegateCommand MinimizeCommand { get; }
        public DelegateCommand CloseCommand { get; }

        public FloatingWindowViewModel(IEventAggregator eventAggregator, IRegionManager regionManager, INavigationStateService navigationState)
        {
            _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
            _regionManager = regionManager ?? throw new ArgumentNullException(nameof(regionManager));
            _navigationState = navigationState ?? throw new ArgumentNullException(nameof(navigationState));
            
            RestoreCommand = new DelegateCommand(OnRestore);
            MinimizeCommand = new DelegateCommand(OnMinimize);
            CloseCommand = new DelegateCommand(OnClose);
        }

        public void Initialize(string title, string pageKey, Action onRestore)
        {
            WindowTitle = title;
            _pageKey = pageKey;
            _onRestore = onRestore;
        }

        /// <summary>
        /// 设置导航回调函数
        /// </summary>
        public void SetNavigateAction(Action<string> navigateAction)
        {
            _navigateAction = navigateAction;
        }

        private void OnRestore()
        {
            // "嵌入"按钮：将浮动窗口嵌入回MainRegion
            _onRestore?.Invoke();
        }

        private void OnMinimize()
        {
            // 调用 FloatingWindowHelper 处理最小化逻辑
            FloatingWindowHelper.MinimizeFloatingWindow(_pageKey, _navigationState, _eventAggregator, _navigateAction);
        }

        private void OnClose()
        {
            var result = ReMessageBox.Show("确定要关闭当前窗口吗？", "确认", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                // 发布页面关闭事件，通知MainWindowViewModel移除导航按钮
                // 注意：传递完整的pageKey，而不是提取的pageName
                // 因为导航按钮的Name属性存储的是完整的pageKey
                if (!string.IsNullOrEmpty(_pageKey))
                {
                    _eventAggregator?.GetEvent<ReleaseCurrentPageEvent>().Publish(_pageKey);
                }
                
                if (Application.Current.Windows.Count > 0)
                {
                    foreach (Window window in Application.Current.Windows)
                    {
                        if (window.DataContext == this)
                        {
                            window.Close();
                            break;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 窗口状态变化处理（从最小化恢复）
        /// </summary>
        public void OnWindowStateChanged(WindowState newState, WindowState oldState)
        {
            // 从最小化恢复到正常状态
            if (oldState == WindowState.Minimized && newState == WindowState.Normal)
            {
                FloatingWindowHelper.RestoreFloatingWindowFromMinimized(_pageKey, _navigationState, _eventAggregator);
            }
        }

        /// <summary>
        /// 窗口激活处理
        /// </summary>
        public void OnWindowActivated()
        {
            FloatingWindowHelper.OnFloatingWindowActivated(_pageKey, _navigationState, _eventAggregator);
        }
    }
}
