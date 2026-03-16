using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using MeasureControl.Constants;
using MeasureControl.Helpers;
using MeasureControl.Models;
using Prism.Events;
using Prism.Regions;
using System.Diagnostics;

namespace MeasureControl.Services
{
    /// <summary>
    /// 导航服务接口
    /// </summary>
    public interface INavigationService
    {
        /// <summary>
        /// 导航按钮集合（用于UI绑定）
        /// </summary>
        ObservableCollection<NavigationButton> NavigationButtons { get; }

        /// <summary>
        /// 当前页面名称
        /// </summary>
        string CurrentPageName { get; }

        /// <summary>
        /// 导航到指定页面
        /// </summary>
        /// <param name="pageType">页面类型</param>
        /// <param name="instanceId">实例ID（多例页面必须提供）</param>
        /// <param name="navigationParams">导航参数</param>
        void NavigateToPage(string pageType, string instanceId = null, Dictionary<string, object> navigationParams = null);

        /// <summary>
        /// 通过PageKey导航
        /// </summary>
        void NavigateByPageKey(string pageKey, Dictionary<string, object> navigationParams = null);

        /// <summary>
        /// 导航到指定页面，不触发按钮高亮更新（用于浮动窗口内部导航）
        /// </summary>
        void NavigateByPageKeySilent(string pageKey, Dictionary<string, object> navigationParams = null);

        /// <summary>
        /// 移除导航按钮并关闭对应页面
        /// </summary>
        void RemoveNavigationButton(string pageKey);

        /// <summary>
        /// 设置激活的导航按钮
        /// </summary>
        void SetActiveButton(string pageKey);

        /// <summary>
        /// 生成PageKey
        /// </summary>
        string GetPageKey(string pageType, string instanceId = null);

        /// <summary>
        /// 从PageKey解析页面类型
        /// </summary>
        string GetPageTypeFromKey(string pageKey);

        /// <summary>
        /// 检查页面是否已打开
        /// </summary>
        bool IsPageOpen(string pageKey);

        /// <summary>
        /// 获取下一个导航目标（用于关闭页面时）
        /// </summary>
        string GetNextNavigationTarget();

        /// <summary>
        /// 导航到首页
        /// </summary>
        void NavigateToHomePage();

        /// <summary>
        /// 清空所有导航按钮
        /// </summary>
        void ClearAllButtons();

        /// <summary>
        /// 获取按钮优先级（用于排序）
        /// </summary>
        int GetButtonPriority(string pageKey);
    }

    /// <summary>
    /// 导航服务实现 - 集中管理所有导航逻辑
    /// 参考博图软件的标签页导航模式
    /// </summary>
    public class NavigationService : INavigationService
    {
        private readonly IRegionManager _regionManager;
        private readonly INavigationStateService _navigationState;
        private readonly NavigationRegistry _registry;
        private readonly IPxiChassisService _pxiChassisService;
        private readonly IEventAggregator _eventAggregator;

        private readonly ObservableCollection<NavigationButton> _navigationButtons;
        private readonly Stack<string> _navigationHistory;
        private string _currentPageName;

        // PageKey分隔符
        private const string KEY_SEPARATOR = "_";

        public ObservableCollection<NavigationButton> NavigationButtons => _navigationButtons;
        public string CurrentPageName => _currentPageName;

        public NavigationService(
            IRegionManager regionManager,
            INavigationStateService navigationState,
            NavigationRegistry registry,
            IPxiChassisService pxiChassisService,
            IEventAggregator eventAggregator)
        {
            _regionManager = regionManager ?? throw new ArgumentNullException(nameof(regionManager));
            _navigationState = navigationState ?? throw new ArgumentNullException(nameof(navigationState));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _pxiChassisService = pxiChassisService ?? throw new ArgumentNullException(nameof(pxiChassisService));
            _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));

            _navigationButtons = new ObservableCollection<NavigationButton>();
            _navigationHistory = new Stack<string>();
        }

        #region Public Methods

        /// <summary>
        /// 导航到指定页面
        /// </summary>
        public void NavigateToPage(string pageType, string instanceId = null, Dictionary<string, object> navigationParams = null)
        {
            if (string.IsNullOrEmpty(pageType))
            {
                throw new ArgumentNullException(nameof(pageType));
            }

            // 获取页面定义
            var pageDefinition = _registry.GetPageDefinition(pageType);
            if (pageDefinition == null)
            {
                Debug.WriteLine($"[NavigationService] 未找到 PageDefinition, pageType={pageType}");
                return;
            }
            else
            {
                Debug.WriteLine($"[NavigationService] PageDefinition found: pageType={pageType}, viewName={pageDefinition.ViewName}, singleton={pageDefinition.IsSingleton}");
            }

            // 生成PageKey
            string pageKey = GetPageKey(pageType, instanceId);
            Debug.WriteLine($"[NavigationService] NavigateToPage pageType={pageType}, instanceId={instanceId}, pageKey={pageKey}, view={pageDefinition.ViewName}, params={(navigationParams?.Count ?? 0)}");

            // 检查是否已打开
            var existingButton = _navigationButtons.FirstOrDefault(b => b.Name == pageKey);
            if (existingButton != null)
            {
                
                if (_navigationState.IsFloating(pageKey))
                {
                    
                    // 检查是否最小化
                    var floatingStates = _navigationState.GetFloatingPageStates();
                    var pageState = floatingStates.FirstOrDefault(s => s.PageKey == pageKey);
                    
                    if (pageState?.IsMinimized == true)
                    {
                        FloatingWindowHelper.RestoreFloatingWindowFromMinimized(pageKey, _navigationState, _eventAggregator);
                    }
                    else
                    {
                        var activateResult = FloatingWindowHelper.ActivateFloatingWindow(pageKey);
                    }
                    
                    // 设置按钮高亮
                    SetActiveButton(pageKey);
                    return;
                }
                
                
                // 页面已存在且未浮动，直接激活（传递新的导航参数）
                ActivateExistingPage(pageKey, navigationParams);
                return;
            }

            // 创建新的导航按钮
            CreateNavigationButton(pageKey, pageType, instanceId, pageDefinition, navigationParams);

            // 执行Prism导航
            PerformNavigation(pageDefinition.ViewName, pageKey, navigationParams);
        }

        /// <summary>
        /// 通过PageKey导航
        /// </summary>
        public void NavigateByPageKey(string pageKey, Dictionary<string, object> navigationParams = null)
        {
            if (string.IsNullOrEmpty(pageKey))
            {
                throw new ArgumentNullException(nameof(pageKey));
            }

            // 特殊处理HomePage（HomePage没有导航按钮）
            if (pageKey == "HomePage")
            {
                _currentPageName = "HomePage";
                _regionManager.RequestNavigate(AppConstants.MainRegionName, "HomePage", result =>
                {
                    if (result.Result == true)
                    {
                    }
                    else
                    {
                    }
                }, navigationParams != null ? CreateNavigationParameters(navigationParams) : null);
                return;
            }

            if (_navigationState.IsFloating(pageKey))
            {
                
                // 检查是否最小化
                var floatingStates = _navigationState.GetFloatingPageStates();
                var pageState = floatingStates.FirstOrDefault(s => s.PageKey == pageKey);
                
                // 先激活浮动窗口（最高优先级）
                if (pageState?.IsMinimized == true)
                {
                    // 从最小化恢复
                    FloatingWindowHelper.RestoreFloatingWindowFromMinimized(pageKey, _navigationState, _eventAggregator);
                }
                else
                {
                    // 直接激活
                    FloatingWindowHelper.ActivateFloatingWindow(pageKey);
                }
                
                // 延迟设置按钮高亮，避免主窗口获得焦点
                System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    SetActiveButton(pageKey);
                }), System.Windows.Threading.DispatcherPriority.Background);
                
                return;
            }

            // 查找对应的导航按钮
            var button = _navigationButtons.FirstOrDefault(b => b.Name == pageKey);
            if (button == null)
            {
                return;
            }

            // 使用按钮存储的ViewName和参数进行导航
            string viewName = button.ViewName;
            var navParams = navigationParams ?? button.NavigationParams;

            // 激活页面
            ActivateExistingPage(pageKey);

            // 执行导航
            Debug.WriteLine($"[NavigationService] Navigate existing pageKey={pageKey}, view={viewName}");
            PerformNavigation(viewName, pageKey, navParams);
        }

        /// <summary>
        /// 导航到指定页面，不触发按钮高亮更新（用于浮动窗口内部导航）
        /// </summary>
        public void NavigateByPageKeySilent(string pageKey, Dictionary<string, object> navigationParams = null)
        {
            if (string.IsNullOrEmpty(pageKey))
            {
                throw new ArgumentNullException(nameof(pageKey));
            }

            // 特殊处理HomePage
            if (pageKey == "HomePage")
            {
                _currentPageName = "HomePage";
                _regionManager.RequestNavigate(AppConstants.MainRegionName, "HomePage");
                return;
            }

            // 查找对应的导航按钮
            var button = _navigationButtons.FirstOrDefault(b => b.Name == pageKey);
            if (button == null)
            {
                return;
            }

            // 使用按钮存储的ViewName和参数进行导航
            string viewName = button.ViewName;
            var navParams = navigationParams ?? button.NavigationParams;

            // 注意：不调用 ActivateExistingPage (它会触发 SetActiveButton)
            // 直接执行导航
            PerformNavigation(viewName, pageKey, navParams);
        }

        /// <summary>
        /// 移除导航按钮
        /// </summary>
        public void RemoveNavigationButton(string pageKey)
        {
            var button = _navigationButtons.FirstOrDefault(b => b.Name == pageKey);
            if (button == null) return;

            // 从集合中移除
            _navigationButtons.Remove(button);

            // 从状态服务中移除
            _navigationState.ClosePage(pageKey);

            // 如果移除的是当前页面，需要导航到其他页面
            if (_currentPageName == pageKey)
            {
                NavigateAfterRemoval();
            }
        }

        /// <summary>
        /// 设置激活的导航按钮
        /// </summary>
        public void SetActiveButton(string pageKey)
        {
            // ========== 调试日志：方法入口 ==========
            
            foreach (var button in _navigationButtons)
            {
                // 只有当前激活的页面按钮才高亮
                // 焦点驱动：焦点在哪个窗口，哪个窗口对应的页面按钮就高亮
                bool wasActive = button.IsActive;
                button.IsActive = (button.Name == pageKey);
                
                if (wasActive != button.IsActive)
                {
                }
            }

            _navigationState.PushActivated(pageKey);
            
        }

        /// <summary>
        /// 生成PageKey
        /// </summary>
        public string GetPageKey(string pageType, string instanceId = null)
        {
            if (string.IsNullOrEmpty(pageType))
            {
                throw new ArgumentNullException(nameof(pageType));
            }

            // 检查是否为单例页面
            bool isSingleton = _registry.IsSingleton(pageType);

            if (isSingleton)
            {
                // 单例页面直接使用PageType作为Key
                return pageType;
            }
            else
            {
                // 多例页面需要实例ID
                if (string.IsNullOrEmpty(instanceId))
                {
                    throw new ArgumentException($"多例页面 {pageType} 必须提供实例ID", nameof(instanceId));
                }

                return $"{pageType}{KEY_SEPARATOR}{instanceId}";
            }
        }

        /// <summary>
        /// 从PageKey解析页面类型
        /// </summary>
        public string GetPageTypeFromKey(string pageKey)
        {
            if (string.IsNullOrEmpty(pageKey))
            {
                return null;
            }

            int separatorIndex = pageKey.IndexOf(KEY_SEPARATOR);
            if (separatorIndex > 0)
            {
                return pageKey.Substring(0, separatorIndex);
            }

            return pageKey;
        }

        /// <summary>
        /// 检查页面是否已打开
        /// </summary>
        public bool IsPageOpen(string pageKey)
        {
            return _navigationButtons.Any(b => b.Name == pageKey);
        }

        /// <summary>
        /// 获取下一个导航目标
        /// </summary>
        public string GetNextNavigationTarget()
        {
            // 优先使用激活历史，默认fallback为HomePage
            string nextPage = _navigationState.GetNextPageOrFallback("HomePage", _currentPageName);
            return nextPage;
        }

        /// <summary>
        /// 导航到首页
        /// </summary>
        public void NavigateToHomePage()
        {
            
            // 清空所有导航按钮（HomePage不显示按钮）
            _navigationButtons.Clear();
            _navigationHistory.Clear();
            
            // 清空NavigationStateService
            _navigationState.Clear();
            
            // 导航到HomePage
            _currentPageName = "HomePage";
            _regionManager.RequestNavigate(AppConstants.MainRegionName, "HomePage", result =>
            {
                if (result.Result == true)
                {
                }
                else
                {
                }
            });
        }

        /// <summary>
        /// 清空所有导航按钮
        /// </summary>
        public void ClearAllButtons()
        {
            _navigationButtons.Clear();
            _navigationHistory.Clear();
            _currentPageName = null;
        }

        /// <summary>
        /// 获取按钮优先级
        /// </summary>
        public int GetButtonPriority(string pageKey)
        {
            // 解析页面类型
            string pageType = GetPageTypeFromKey(pageKey);

            // 优先级0：硬件与配置（最左边）
            if (pageKey == "HardwareConfig" || pageType == "HardwareConfig")
            {
                return 0;
            }

            // 优先级1：PXI机箱（第二位）
            if (pageType == "PxiChassis")
            {
                return 1;
            }

            // 特殊处理：测试任务相关（包含多级分隔符）
            if (pageKey.Contains("-") || pageType == "ChannelConfigTabel" || 
                pageType == "SignalConfigTabel" || pageType == "IcdConfigTabel" ||
                pageType == "TestSequence" || pageType == "ReportConfigTabel")
            {
                return 2;
            }

            // 使用注册表中的优先级
            int priority = _registry.GetPriority(pageType);
            return priority;
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// 激活已存在的页面
        /// </summary>
        private void ActivateExistingPage(string pageKey, Dictionary<string, object> navigationParams = null)
        {
            
            var button = _navigationButtons.FirstOrDefault(b => b.Name == pageKey);
            if (button != null)
            {
                
                // 优先使用新传入的参数，如果没有则使用按钮存储的参数
                var paramsToUse = navigationParams ?? button.NavigationParams;
                
                // 执行导航
                _regionManager.RequestNavigate(AppConstants.MainRegionName, button.ViewName, result =>
                {
                    if (result.Result == true)
                    {
                    }
                    else
                    {
                    }
                }, paramsToUse != null ? CreateNavigationParameters(paramsToUse) : null);
            }
            else
            {
            }
            
            _navigationState.OpenPage(pageKey);
            SetActiveButton(pageKey);
            AddToNavigationHistory(pageKey);
            
        }

        /// <summary>
        /// 创建导航按钮
        /// </summary>
        private void CreateNavigationButton(string pageKey, string pageType, string instanceId, 
            PageDefinition pageDefinition, Dictionary<string, object> navigationParams)
        {
            // 生成显示名称
            string displayName = GenerateDisplayName(pageType, instanceId, navigationParams);

            // 生成Tooltip路径
            string tooltipPath = GenerateTooltipPath(pageType, instanceId, navigationParams);

            var navigationButton = new NavigationButton
            {
                Name = pageKey,
                DisplayName = displayName,
                Tag = pageKey,
                ViewName = pageDefinition.ViewName,
                NavigationParams = navigationParams,
                IsActive = false,
                TooltipPath = tooltipPath
            };

            // 按优先级插入到正确位置
            InsertNavigationButtonInOrder(navigationButton);

            // 添加到状态服务
            _navigationState.OpenPage(pageKey);

            // 设置为激活状态
            SetActiveButton(pageKey);

            // 添加到历史
            AddToNavigationHistory(pageKey);
        }

        /// <summary>
        /// 生成显示名称
        /// </summary>
        private string GenerateDisplayName(string pageType, string instanceId, Dictionary<string, object> navigationParams)
        {
            // 对于有instanceId的情况，优先使用instanceId作为显示名
            if (!string.IsNullOrEmpty(instanceId))
            {
                // 如果instanceId本身包含测试任务名（如"Task1-通道配置表1"），提取后半部分
                if (instanceId.Contains("-"))
                {
                    return instanceId.Split('-').Last();
                }
                return instanceId;
            }

            // 对于单例页面，使用按钮名称
            string buttonName = _registry.GetButtonName(pageType);
            return buttonName ?? pageType;
        }

        /// <summary>
        /// 生成Tooltip路径
        /// </summary>
        private string GenerateTooltipPath(string pageType, string instanceId, Dictionary<string, object> navigationParams)
        {
            // 如果导航参数中包含路径信息
            if (navigationParams != null)
            {
                if (navigationParams.ContainsKey("TestTaskName"))
                {
                    var testTaskName = navigationParams["TestTaskName"] as string;
                    var parentType = navigationParams.ContainsKey("ParentType") ? navigationParams["ParentType"] as string : "";
                    var pageName = GenerateDisplayName(pageType, instanceId, navigationParams);

                    string parentName = GetParentDisplayName(parentType);
                    return $"{testTaskName}/{parentName}/{pageName}";
                }
            }

            // 默认返回显示名称
            return GenerateDisplayName(pageType, instanceId, navigationParams);
        }

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
                _ => ""
            };
        }

        /// <summary>
        /// 按优先级插入导航按钮
        /// </summary>
        private void InsertNavigationButtonInOrder(NavigationButton newButton)
        {
            if (_navigationButtons.Count == 0)
            {
                _navigationButtons.Add(newButton);
                return;
            }

            int newButtonPriority = GetButtonPriority(newButton.Name);

            // 找到合适的插入位置
            int insertIndex = _navigationButtons.Count;
            for (int i = 0; i < _navigationButtons.Count; i++)
            {
                int existingPriority = GetButtonPriority(_navigationButtons[i].Name);
                if (newButtonPriority < existingPriority)
                {
                    insertIndex = i;
                    break;
                }
            }

            _navigationButtons.Insert(insertIndex, newButton);
        }

        /// <summary>
        /// 执行Prism导航
        /// </summary>
        private void PerformNavigation(string viewName, string pageKey, Dictionary<string, object> navigationParams)
        {
            try
            {
                Debug.WriteLine($"[NavigationService] PerformNavigation view={viewName}, pageKey={pageKey}, paramsCount={(navigationParams?.Count ?? 0)}");
                _currentPageName = pageKey;

                if (!_regionManager.Regions.ContainsRegionWithName("MainRegion"))
                {
                    Debug.WriteLine("[NavigationService] MainRegion not found, navigation aborted");
                    return;
                }

                // 调试区域和视图注册情况
                var region = _regionManager.Regions["MainRegion"];
                Debug.WriteLine($"[NavigationService] MainRegion activeViews={region.ActiveViews.Count()}, viewsCount={region.Views.Count()}");

                var parameters = new NavigationParameters();
                if (navigationParams != null)
                {
                    foreach (var param in navigationParams)
                    {
                        parameters.Add(param.Key, param.Value);
                        Debug.WriteLine($"[NavigationService]   param: {param.Key}={(param.Value ?? "null")}");
                    }
                }

                _regionManager.RequestNavigate("MainRegion", viewName, result =>
                {
                    Debug.WriteLine($"[NavigationService] RequestNavigate view={viewName}, pageKey={pageKey}, result={result.Result}, error={result.Error}");
                }, parameters);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NavigationService] PerformNavigation exception: {ex.Message}");
                Debug.WriteLine(ex.StackTrace);
            }
        }

        /// <summary>
        /// 创建NavigationParameters对象
        /// </summary>
        private NavigationParameters CreateNavigationParameters(Dictionary<string, object> parameters)
        {
            var navParams = new NavigationParameters();
            if (parameters != null)
            {
                foreach (var param in parameters)
                {
                    navParams.Add(param.Key, param.Value);
                }
            }
            return navParams;
        }

        /// <summary>
        /// 添加到导航历史
        /// </summary>
        private void AddToNavigationHistory(string pageKey)
        {
            if (!string.IsNullOrEmpty(_currentPageName) && _currentPageName != pageKey)
            {
                _navigationHistory.Push(_currentPageName);
            }
            _currentPageName = pageKey;
        }

        /// <summary>
        /// 移除页面后导航到合适的目标
        /// </summary>
        private void NavigateAfterRemoval()
        {
            try
            {
                if (_navigationButtons.Count > 0)
                {
                    // 导航到最后一个按钮
                    var lastButton = _navigationButtons.Last();
                    NavigateByPageKey(lastButton.Name, lastButton.NavigationParams);
                }
                else
                {
                    // 没有按钮时，MainRegion显示空白
                }
            }
            catch (Exception)
            {
            }
        }

        #endregion
    }
}

