using System;
using System.Linq;
using System.Windows.Input;
using MeasureControl.Views.Dialogs;
using Prism.Commands;
using Prism.Events;
using Prism.Mvvm;
using Prism.Regions;

namespace MeasureControl.ViewModels.Database
{
    /// <summary>
    /// 数据库配置页面ViewModel
    /// </summary>
    public class DatabaseConfigViewModel : BindableBase, INavigationAware, IDisposable
    {
        private readonly IRegionManager _regionManager;
        private readonly IEventAggregator _eventAggregator;
        private IRegionNavigationJournal _journal;
        private IRegionManager _currentRegionManager; // 当前实际使用的RegionManager（可能是主窗口的或浮动窗口的）
        private bool _isTaskConfigSelected;
        private bool _isTestDataSelected;
        private bool _isFirstNavigation = true; // 标记是否是第一次导航
        private bool _isNavigating = false; // 标记是否正在导航中，防止循环调用
        private string _displayPath = "数据库管理";
        private bool _disposed = false;

        /// <summary>
        /// 显示路径（用于界面标题）
        /// </summary>
        public string DisplayPath
        {
            get => _displayPath;
            set => SetProperty(ref _displayPath, value);
        }

        /// <summary>
        /// 是否选中任务配置数据库
        /// </summary>
        public bool IsTaskConfigSelected
        {
            get => _isTaskConfigSelected;
            set => SetProperty(ref _isTaskConfigSelected, value);
        }

        /// <summary>
        /// 是否选中测试数据数据库
        /// </summary>
        public bool IsTestDataSelected
        {
            get => _isTestDataSelected;
            set => SetProperty(ref _isTestDataSelected, value);
        }

        /// <summary>
        /// 切换到任务配置数据库命令
        /// </summary>
        public ICommand SwitchToTaskConfigCommand { get; }

        /// <summary>
        /// 切换到测试数据数据库命令
        /// </summary>
        public ICommand SwitchToTestDataCommand { get; }

        /// <summary>
        /// 关闭在区域中的命令
        /// </summary>
        public DelegateCommand CloseInRegionCommand { get; }

        public DatabaseConfigViewModel(IRegionManager regionManager, IEventAggregator eventAggregator)
        {
            _regionManager = regionManager;
            _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));

            // 初始化命令
            SwitchToTaskConfigCommand = new DelegateCommand(SwitchToTaskConfig);
            SwitchToTestDataCommand = new DelegateCommand(SwitchToTestData);
            CloseInRegionCommand = new DelegateCommand(OnCloseInRegion);
        }

        /// <summary>
        /// 切换到任务配置数据库
        /// </summary>
        private void SwitchToTaskConfig()
        {
            IsTaskConfigSelected = true;
            IsTestDataSelected = false;
            DisplayPath = "数据库管理/任务数据库";
            NavigateToTaskConfig();
        }

        /// <summary>
        /// 切换到测试数据数据库
        /// </summary>
        private void SwitchToTestData()
        {
            IsTaskConfigSelected = false;
            IsTestDataSelected = true;
            DisplayPath = "数据库管理/测试数据库";
            NavigateToTestData();
        }

        /// <summary>
        /// 导航到任务配置数据库页面
        /// </summary>
        private void NavigateToTaskConfig()
        {
            // 使用Dispatcher异步执行导航，避免在OnNavigatedTo中同步调用导致的栈溢出
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                // 使用当前的RegionManager（可能是主窗口的或浮动窗口的）
                var regionManagerToUse = _currentRegionManager ?? _regionManager;
                
                regionManagerToUse.RequestNavigate("DatabaseRegion", "TaskConfigDatabase");
            }));
        }

        /// <summary>
        /// 导航到测试数据数据库页面
        /// </summary>
        private void NavigateToTestData()
        {
            // 使用Dispatcher异步执行导航，避免在OnNavigatedTo中同步调用导致的栈溢出
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                // 使用当前的RegionManager（可能是主窗口的或浮动窗口的）
                var regionManagerToUse = _currentRegionManager ?? _regionManager;
                
                regionManagerToUse.RequestNavigate("DatabaseRegion", "TestDataBase");
            }));
        }

        #region INavigationAware Implementation

        public void OnNavigatedTo(NavigationContext navigationContext)
        {
            // 缓存导航日志用于关闭时回退
            _journal = navigationContext?.NavigationService?.Journal;
            
            // 这样在浮动窗口中也能正确导航到DatabaseRegion
            if (navigationContext?.NavigationService != null)
            {
                var region = navigationContext.NavigationService.Region;
                if (region?.RegionManager != null)
                {
                    _currentRegionManager = region.RegionManager;
                }
            }
            
            // 防止重入导致的栈溢出
            if (_isNavigating)
            {
                return;
            }

            try
            {
                _isNavigating = true;

                // 从导航参数中获取数据库类型
                if (navigationContext.Parameters.ContainsKey("DatabaseType"))
                {
                    var databaseType = navigationContext.Parameters["DatabaseType"] as string;
                    
                    if (databaseType == "TaskDatabase")
                    {
                        // 选中任务配置数据库并导航
                        IsTaskConfigSelected = true;
                        IsTestDataSelected = false;
                        DisplayPath = "数据库管理/任务数据库";
                        NavigateToTaskConfig();
                    }
                    else if (databaseType == "TestDatabase")
                    {
                        // 选中测试数据数据库并导航
                        IsTaskConfigSelected = false;
                        IsTestDataSelected = true;
                        DisplayPath = "数据库管理/测试数据库";
                        NavigateToTestData();
                    }
                    _isFirstNavigation = false;
                }
                else if (_isFirstNavigation)
                {
                    // 只在第一次导航且没有参数时，默认显示任务配置数据库
                    IsTaskConfigSelected = true;
                    IsTestDataSelected = false;
                    DisplayPath = "数据库管理/任务数据库";
                    NavigateToTaskConfig();
                    _isFirstNavigation = false;
                }
                // 如果不是第一次导航且没有参数，则保持当前选中的标签页不变
            }
            finally
            {
                _isNavigating = false;
            }
        }

        public bool IsNavigationTarget(NavigationContext navigationContext)
        {
            // 重用同一个实例，避免DatabaseRegion重复注册
            return true;
        }

        public void OnNavigatedFrom(NavigationContext navigationContext)
        {
            // 不需要特殊处理
        }

        #endregion

        /// <summary>
        /// 关闭在区域中的视图
        /// </summary>
        private void OnCloseInRegion()
        {
            var result = ReMessageBox.Show("确定要关闭数据库配置吗？", "确认", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
            if (result == System.Windows.MessageBoxResult.Yes)
            {
                // 传递页面类型名称
                _eventAggregator.GetEvent<Events.ReleaseCurrentPageEvent>().Publish("DatabaseConfig");
            }
        }

        #region IDisposable Implementation

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                // 清理资源
                _journal = null;
                _disposed = true;
            }
        }

        #endregion
    }
}

