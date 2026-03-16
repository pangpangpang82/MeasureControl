using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using MeasureControl.Events;
using MeasureControl.Helpers;
using MeasureControl.Models;
using Prism.Commands;
using Prism.Events;
using Prism.Mvvm;
using Prism.Regions;

namespace MeasureControl.ViewModels.Common
{
    /// <summary>
    /// 导航管理ViewModel - 负责页面导航和导航历史管理
    /// </summary>
    public class NavigationViewModel : BindableBase, IDisposable
    {
        #region Private Fields

        private readonly IRegionManager _regionManager;
        private readonly IEventAggregator _eventAggregator;

        private ObservableCollection<NavigationButton> _navigationButtons;
        private string _currentPageName;
        private System.Collections.Generic.Stack<string> _navigationHistory = new System.Collections.Generic.Stack<string>();

        #endregion

        #region Public Properties

        /// <summary>
        /// 导航按钮集合
        /// </summary>
        public ObservableCollection<NavigationButton> NavigationButtons
        {
            get => _navigationButtons;
            set => SetProperty(ref _navigationButtons, value);
        }

        /// <summary>
        /// 当前页面名称
        /// </summary>
        public string CurrentPageName
        {
            get => _currentPageName;
            set => SetProperty(ref _currentPageName, value);
        }

        #endregion

        #region Commands

        public ICommand NavigateCommand { get; private set; }
        public ICommand NavigationButtonClickCommand { get; private set; }
        public ICommand TreeItemDoubleClickCommand { get; private set; }

        #endregion

        #region Constructor

        public NavigationViewModel(IRegionManager regionManager, IEventAggregator eventAggregator)
        {
            _regionManager = regionManager ?? throw new ArgumentNullException(nameof(regionManager));
            _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));

            InitializeCollections();
            InitializeCommands();
            SubscribeToEvents();
        }

        #endregion

        #region Private Methods

        private void InitializeCollections()
        {
            NavigationButtons = new ObservableCollection<NavigationButton>();
        }

        private void InitializeCommands()
        {
            NavigateCommand = new DelegateCommand<string>(OnNavigate);
            NavigationButtonClickCommand = new DelegateCommand<NavigationButton>(OnNavigationButtonClick);
            TreeItemDoubleClickCommand = new DelegateCommand<ProjectItem>(OnTreeItemDoubleClick);
        }

        private void SubscribeToEvents()
        {
            _eventAggregator.GetEvent<AddNavigationButtonEvent>().Subscribe(OnAddNavigationButton);
            _eventAggregator.GetEvent<ClearNavigationButtonsEvent>().Subscribe(OnClearNavigationButtons);
        }

        #endregion

        #region Command Implementations

        private void OnNavigate(string pageName)
        {
            if (string.IsNullOrEmpty(pageName)) return;

            try
            {
                // 添加到导航历史
                if (!string.IsNullOrEmpty(_currentPageName))
                {
                    _navigationHistory.Push(_currentPageName);
                }

                // 执行导航
                _regionManager.RequestNavigate("MainRegion", pageName);
                CurrentPageName = pageName;

                // 发布导航事件
                _eventAggregator.GetEvent<NavigationCompletedEvent>().Publish(pageName);
            }
            catch (Exception)
            {
                // 记录错误但不抛出异常，避免应用崩溃
            }
        }

        private void OnNavigationButtonClick(NavigationButton button)
        {
            if (button?.PageName != null)
            {
                OnNavigate(button.PageName);
            }
        }

        private void OnTreeItemDoubleClick(ProjectItem item)
        {
            if (item == null) return;

            // 根据项目类型导航到相应页面
            string pageName = GetPageNameByType(item.Type);
            if (!string.IsNullOrEmpty(pageName))
            {
                OnNavigate(pageName);
            }
        }

        #endregion

        #region Event Handlers

        private void OnAddNavigationButton(string pageName)
        {
            if (string.IsNullOrEmpty(pageName)) return;

            // 检查是否已存在
            if (NavigationButtons.Any(b => b.PageName == pageName))
                return;

            var button = new NavigationButton
            {
                PageName = pageName,
                DisplayName = GetDisplayNameByPageName(pageName),
                Command = NavigationButtonClickCommand
            };

            NavigationButtons.Add(button);
        }

        private void OnClearNavigationButtons()
        {
            NavigationButtons.Clear();
        }

        #endregion

        #region Helper Methods

        private string GetPageNameByType(string type)
        {
            return type switch
            {
                "Hardware_config" => "HardwareConfig",
                "task_config" => "TaskConfig",
                "data_analysis" => "DataAnalysis",
                "database_management" => "DatabaseManagement",
                "remote_interface" => "RemoteInterface",
                "channel_config" => "ChannelConfig",
                "signal_config" => "SignalConfig",
                "test_ui" => "TestUI",
                "test_sequence" => "TestSequence",
                "test_script" => "TestScript",
                "report" => "Report",
                "monitor" => "Monitor",
                _ => null
            };
        }

        private string GetDisplayNameByPageName(string pageName)
        {
            return pageName switch
            {
                "HardwareConfig" => "硬件配置",
                "TaskConfig" => "任务配置",
                "DataAnalysis" => "数据分析",
                "DatabaseManagement" => "数据库管理",
                "RemoteInterface" => "远程接口",
                "ChannelConfig" => "通道配置",
                "SignalConfig" => "信号配置",
                "TestUI" => "测试界面",
                "TestSequence" => "测试序列",
                "TestScript" => "测试脚本",
                "Report" => "报表",
                "Monitor" => "监控与回放",
                "HomePage" => "首页",
                _ => pageName
            };
        }

        /// <summary>
        /// 导航到首页
        /// </summary>
        public void NavigateToHomePage()
        {
            OnNavigate("HomePage");
        }

        /// <summary>
        /// 返回上一页
        /// </summary>
        public void NavigateBack()
        {
            if (_navigationHistory.Count > 0)
            {
                var previousPage = _navigationHistory.Pop();
                _regionManager.RequestNavigate("MainRegion", previousPage);
                CurrentPageName = previousPage;
            }
        }

        /// <summary>
        /// 清空导航历史
        /// </summary>
        public void ClearNavigationHistory()
        {
            _navigationHistory.Clear();
        }

        #endregion

        #region IDisposable

        private bool _disposed;

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                // 使用 ResourceCleanupHelper 清理集合
                ResourceCleanupHelper.CleanupCollection(_navigationButtons);
                _navigationHistory?.Clear();
            }

            _disposed = true;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion
    }
}
