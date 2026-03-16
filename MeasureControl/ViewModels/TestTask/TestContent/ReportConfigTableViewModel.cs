using System;
using System.Linq;
using MeasureControl.Helpers;
using MeasureControl.Views.Dialogs;
using Prism.Commands;
using Prism.Events;
using Prism.Mvvm;
using Prism.Regions;

namespace MeasureControl.ViewModels
{
    /// <summary>
    /// 报表模板的ViewModel
    /// </summary>
    public class ReportConfigTabelViewModel : BindableBase, INavigationAware, IDisposable
    {
        private readonly IRegionManager _regionManager;
        private readonly IEventAggregator _eventAggregator;

        #region Properties

        private string _chassisName;
        /// <summary>
        /// 机箱名称
        /// </summary>
        public string ChassisName
        {
            get => _chassisName;
            set => SetProperty(ref _chassisName, value);
        }

        private string _testTaskName;
        /// <summary>
        /// 测试任务名称
        /// </summary>
        public string TestTaskName
        {
            get => _testTaskName;
            set => SetProperty(ref _testTaskName, value);
        }

        private string _configTabelName;
        /// <summary>
        /// 配置表名称
        /// </summary>
        public string ConfigTabelName
        {
            get => _configTabelName;
            set => SetProperty(ref _configTabelName, value);
        }

        private string _parentType;
        private bool _disposed = false;
        /// <summary>
        /// 父节点类型
        /// </summary>
        public string ParentType
        {
            get => _parentType;
            set => SetProperty(ref _parentType, value);
        }

        private string _displayPath;
        /// <summary>
        /// 显示路径（用于界面标题）
        /// </summary>
        public string DisplayPath
        {
            get => _displayPath;
            set => SetProperty(ref _displayPath, value);
        }

        #endregion

        #region Commands

        // 浮动窗口命令
        public DelegateCommand FloatWindowCommand { get; }
        public DelegateCommand MinimizeInRegionCommand { get; }
        public DelegateCommand CloseInRegionCommand { get; }

        #endregion

        #region Constructor

        public ReportConfigTabelViewModel(IRegionManager regionManager, IEventAggregator eventAggregator)
        {
            _regionManager = regionManager ?? throw new ArgumentNullException(nameof(regionManager));
            _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
            
            // 浮动窗口命令
            FloatWindowCommand = new DelegateCommand(OnFloatWindow);
            MinimizeInRegionCommand = new DelegateCommand(OnMinimizeInRegion);
            CloseInRegionCommand = new DelegateCommand(OnCloseInRegion);
            
            DisplayPath = "报表模板";
        }

        #endregion

        #region INavigationAware Implementation

        public void OnNavigatedTo(NavigationContext navigationContext)
        {
            // 从导航参数中获取信息
            if (navigationContext.Parameters.ContainsKey("ChassisName"))
            {
                ChassisName = navigationContext.Parameters["ChassisName"] as string;
            }

            if (navigationContext.Parameters.ContainsKey("TestTaskName"))
            {
                TestTaskName = navigationContext.Parameters["TestTaskName"] as string;
            }

            if (navigationContext.Parameters.ContainsKey("ConfigTabelName"))
            {
                ConfigTabelName = navigationContext.Parameters["ConfigTabelName"] as string;
            }

            if (navigationContext.Parameters.ContainsKey("ParentType"))
            {
                ParentType = navigationContext.Parameters["ParentType"] as string;
            }

            // 生成显示路径，包含机箱名称
            string parentName = GetParentDisplayName(ParentType);
            if (!string.IsNullOrEmpty(ChassisName))
            {
                DisplayPath = $"{ChassisName}/{TestTaskName}/{parentName}/{ConfigTabelName}";
            }
            else
            {
                DisplayPath = $"{TestTaskName}/{parentName}/{ConfigTabelName}";
            }
        }

        public bool IsNavigationTarget(NavigationContext navigationContext)
        {
            // 每次创建新实例，支持多个相同类型页面
            return false;
        }

        public void OnNavigatedFrom(NavigationContext navigationContext)
        {
            // 导航离开时的清理工作
        }

        #endregion

        #region IDisposable Implementation

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                // 清理资源
                _disposed = true;
            }
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// 获取父节点显示名称
        /// </summary>
        private string GetParentDisplayName(string parentType)
        {
            return parentType switch
            {
                "channel_config" => "通道配置",
                "icd_config" => "ICD配置",
                "signal_config" => "信号配置",
                "test_sequence" => "测试序列",
                "report" => "报表",
                _ => parentType
            };
        }

        #endregion

        #region Command Handlers

        private void OnFloatWindow()
        {
            ReMessageBox.Show("浮动功能需要在View中实现");
        }

        private void OnMinimizeInRegion()
        {
            ReMessageBox.Show("最小化功能待实现");
        }

        private void OnCloseInRegion()
        {
            var result = ReMessageBox.Show("确定要关闭当前配置表吗？", "确认", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
            if (result == System.Windows.MessageBoxResult.Yes)
            {
                // 构建完整的pageKey: ReportConfigTabel_任务名-配置表名
                string pageKey = $"ReportConfigTabel_{TestTaskName}-{ConfigTabelName}";
                
                // 传递完整的pageKey，这样MainWindowViewModel可以正确识别和关闭该页面
                _eventAggregator.GetEvent<Events.ReleaseCurrentPageEvent>().Publish(pageKey);
            }
        }

        #endregion
    }
}

