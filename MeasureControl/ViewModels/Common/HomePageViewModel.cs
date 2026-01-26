using System;
using Prism.Events;
using Prism.Mvvm;
using Prism.Regions;

namespace MeasureControl.ViewModels.Common
{
    /// <summary>
    /// HomePage的ViewModel
    /// </summary>
    public class HomePageViewModel : BindableBase, INavigationAware
    {
        private readonly IRegionManager _regionManager;
        private readonly IEventAggregator _eventAggregator;

        public HomePageViewModel(IRegionManager regionManager, IEventAggregator eventAggregator)
        {
            _regionManager = regionManager ?? throw new ArgumentNullException(nameof(regionManager));
            _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
        }

        #region INavigationAware Implementation

        public void OnNavigatedTo(NavigationContext navigationContext)
        {
            // 不需要在这里发布ClearNavigationButtonsEvent
            // 因为NavigateToHomePage方法已经直接清空了导航按钮
            // 发布事件会导致递归调用：HomePage导航 -> 发布事件 -> 处理事件 -> 再次导航到HomePage
        }

        public bool IsNavigationTarget(NavigationContext navigationContext)
        {
            return false;
        }

        public void OnNavigatedFrom(NavigationContext navigationContext)
        {
            // 导航离开时的清理工作
        }

        #endregion
    }
}
