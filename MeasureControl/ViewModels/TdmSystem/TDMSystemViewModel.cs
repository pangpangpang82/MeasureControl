using System;
using MeasureControl.Views.Dialogs;
using Prism.Commands;
using Prism.Events;
using Prism.Mvvm;
using Prism.Regions;

namespace MeasureControl.ViewModels.TdmSystem
{
    /// <summary>
    /// TDM系统页面ViewModel
    /// </summary>
    public class TDMSystemViewModel : BindableBase, INavigationAware, IDisposable
    {
        private readonly IEventAggregator _eventAggregator;
        private IRegionNavigationJournal _journal;
        private bool _disposed = false;

        public string DisplayPath => "远程接口";

        /// <summary>
        /// 关闭在区域中的命令
        /// </summary>
        public DelegateCommand CloseInRegionCommand { get; }

        public TDMSystemViewModel(IEventAggregator eventAggregator)
        {
            _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));

            // 初始化命令
            CloseInRegionCommand = new DelegateCommand(OnCloseInRegion);
        }

        #region INavigationAware Implementation

        public void OnNavigatedTo(NavigationContext navigationContext)
        {
            // 缓存导航日志用于关闭时回退
            _journal = navigationContext?.NavigationService?.Journal;
        }

        public bool IsNavigationTarget(NavigationContext navigationContext)
        {
            // TDMSystem 是单例，重用同一个实例
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
            var result = ReMessageBox.Show("确定要关闭远程接口吗？", "确认", 
                System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
            if (result == System.Windows.MessageBoxResult.Yes)
            {
                // 发布释放当前页面事件，传递页面名称
                _eventAggregator.GetEvent<Events.ReleaseCurrentPageEvent>().Publish("TDMSystem");
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

