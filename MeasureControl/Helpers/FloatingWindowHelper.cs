using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using MeasureControl.Events;
using MeasureControl.Services;
using MeasureControl.ViewModels;
using MeasureControl.ViewModels.Common;
using MeasureControl.Views;
using MeasureControl.Views.Common;
using Prism.Events;
using Prism.Regions;

namespace MeasureControl.Helpers
{
    /// <summary>
    /// 浮动窗口管理助手 - 使用Prism Region API管理页面浮动
    /// </summary>
    public static class FloatingWindowHelper
    {
        #region Win32 API Declarations

        /// <summary>
        /// 将窗口置于前台并激活
        /// </summary>
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        /// <summary>
        /// 显示或隐藏窗口
        /// </summary>
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const int SW_RESTORE = 9;

        #endregion

        #region Private Fields

        /// <summary>
        /// 浮动窗口信息字典 - 支持多个浮动窗口
        /// </summary>
        private static Dictionary<string, FloatingWindowInfo> _floatingWindows = new Dictionary<string, FloatingWindowInfo>();

        #endregion

        #region Public Methods

        /// <summary>
        /// 浮动页面到独立窗口（推荐：传入调用方所在的View实例，避免依赖ActiveViews）
        /// </summary>
        /// <param name="pageName">页面名称</param>
        /// <param name="sourceView">当前页面的View实例（通常为this）</param>
        /// <param name="regionManager">主Region管理器</param>
        /// <param name="eventAggregator">事件聚合器</param>
        /// <param name="navigationState">导航状态服务</param>
        /// <param name="navigateAction">导航回调</param>
        /// <param name="navigationService">导航服务（用于静默导航，不触发按钮高亮）</param>
        /// <param name="explicitPageKey">可选：明确指定的PageKey。如果提供，将跳过查找和生成逻辑</param>
        /// <returns>成功时返回生成的pageKey，失败时返回null</returns>
        public static string FloatPage(string pageName, FrameworkElement sourceView, IRegionManager regionManager, IEventAggregator eventAggregator, INavigationStateService navigationState, Action<string> navigateAction, INavigationService navigationService = null, string explicitPageKey = null)
        {
            try
            {
                // 1. 获取MainRegion
                var mainRegion = regionManager.Regions["MainRegion"];
                if (mainRegion == null)
                {
                    return null;
                }

                // 2. 找到当前的View：优先使用传入的View，其次回退到ActiveViews
                var activeView = (object)sourceView ?? mainRegion.ActiveViews.FirstOrDefault();
                if (activeView == null)
                {
                    return null;
                }

                // 3. 清理子区域 - 防止区域重复注册
                if (activeView is FrameworkElement viewElement)
                {
                    ClearChildRegions(viewElement, regionManager);
                }

                // 3.5. 解析PageKey：优先使用明确指定的PageKey，否则从激活历史中查找，最后回退到基于View生成
                string pageKey = null;
                
                if (!string.IsNullOrEmpty(explicitPageKey))
                {
                    // 使用明确指定的PageKey
                    pageKey = explicitPageKey;
                }
                else
                {
                    // 必须基于当前激活的View生成PageKey，避免多实例页面在浮动时取错Key，导致MainRegion出现重复页面。
                    pageKey = GeneratePageKey(pageName, activeView);
                    if (string.IsNullOrEmpty(pageKey))
                    {
                        pageKey = pageName;
                    }
                }
                
                // 先获取下一个可导航的页面，排除当前页面（但不要先标记为浮动）
                // 注意：这里不能先调用MarkFloating，因为GetNextPageOrFallback会排除浮动页面
                // 如果先标记为浮动，GetNextPageOrFallback就会排除它，但此时它还在MainRegion中
                string nextPageName = navigationState?.GetNextPageOrFallback("HomePage", pageKey);

                // 添加调试日志

                // 这样在导航过程中调用SetActiveButton时，浮动页面按钮会保持高亮
                navigationState?.MarkFloating(pageKey, pageName);

                // 执行导航（在移除视图之前，避免MainRegion出现空白或导航错误）
                // 总是导航到nextPageName（可能是HomePage）
                // 使用等待机制确保导航完成后再移除View
                bool navigationCompleted = false;
                
                // 确保nextPageName不为空，如果为空则使用HomePage
                if (string.IsNullOrEmpty(nextPageName))
                {
                    nextPageName = "HomePage";
                }
                
                // 记录导航前的ActiveView
                var viewBeforeNav = activeView;
                
                // 验证nextPageName不是已经浮动的页面
                // 虽然GetNextPageOrFallback应该已经排除浮动页面，但这里再次验证以确保安全
                bool isNextPageFloating = navigationState?.IsFloating(nextPageName) ?? false;
                if (isNextPageFloating)
                {
                    // 这种情况理论上不应该发生，但如果发生了，尝试导航到HomePage
                    nextPageName = "HomePage";
                }
                
                // 使用 NavigationService.NavigateByPageKeySilent 进行静默导航
                // 这样不会触发按钮高亮更新
                bool navigationAttempted = false;
                if (navigationService != null)
                {
                    try
                    {
                        navigationService.NavigateByPageKeySilent(nextPageName);
                        navigationAttempted = true;
                    }
                    catch
                    {
                        // 如果静默导航失败，使用降级方案
                        navigateAction?.Invoke(nextPageName);
                        navigationAttempted = true;
                    }
                }
                else
                {
                    // 降级方案：如果没有传入NavigationService，使用原有的navigateAction
                    navigateAction?.Invoke(nextPageName);
                    navigationAttempted = true;
                }
                
                // 等待导航完成（简单轮询检查ActiveView是否改变）
                // 注意：这是临时解决方案，更好的方式是使用异步/回调
                if (navigationAttempted)
                {
                    var startTime = DateTime.Now;
                    while ((DateTime.Now - startTime).TotalMilliseconds < 800) // 增加等待时间到800ms
                    {
                        System.Windows.Application.Current.Dispatcher.Invoke(() => {}, System.Windows.Threading.DispatcherPriority.Background);
                        
                        var currentActiveView = mainRegion.ActiveViews.FirstOrDefault();
                        if (currentActiveView != null && currentActiveView != viewBeforeNav)
                        {
                            navigationCompleted = true;
                            break;
                        }
                        // 让出UI消息循环一次，避免阻塞而不使用Thread.Sleep
                        var frame = new System.Windows.Threading.DispatcherFrame();
                        System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() => frame.Continue = false), System.Windows.Threading.DispatcherPriority.Background);
                        System.Windows.Threading.Dispatcher.PushFrame(frame);
                    }
                }
                
                if (!navigationCompleted)
                {
                    // 再检查一次当前的ActiveView是否已经改变
                    var finalActiveView = mainRegion.ActiveViews.FirstOrDefault();
                    if (finalActiveView != null && finalActiveView != viewBeforeNav)
                    {
                        navigationCompleted = true;
                    }
                    else
                    {
                        // 如果导航仍未完成，强制导航到HomePage
                        try
                        {
                            if (navigationService != null)
                            {
                                navigationService.NavigateByPageKeySilent("HomePage");
                            }
                            else
                            {
                                navigateAction?.Invoke("HomePage");
                            }
                            // 等待HomePage导航完成
                            System.Threading.Thread.Sleep(100);
                        }
                        catch
                        {
                            // 忽略导航错误，继续执行浮动操作
                        }
                    }
                }
                
                // 注意：已经在导航之前标记为浮动状态（见上方第107行）
                // 这样可以确保导航过程中浮动页面按钮保持高亮
                
                // 确保导航已完成：如果导航没有完成，强制等待并再次尝试
                if (!navigationCompleted)
                {
                    // 再次等待一小段时间，确保导航完成
                    System.Threading.Thread.Sleep(100);
                    
                    // 再次检查ActiveView
                    var checkActiveView = mainRegion.ActiveViews.FirstOrDefault();
                    if (checkActiveView != null && checkActiveView != viewBeforeNav)
                    {
                        navigationCompleted = true;
                    }
                    else
                    {
                        // 如果仍然没有导航，强制导航到HomePage
                        try
                        {
                            if (navigationService != null)
                            {
                                navigationService.NavigateByPageKeySilent("HomePage");
                            }
                            else
                            {
                                navigateAction?.Invoke("HomePage");
                            }
                            // 再等待一下
                            System.Threading.Thread.Sleep(100);
                        }
                        catch
                        {
                            // 忽略错误
                        }
                    }
                }

                // 4. 从MainRegion移除（Prism会处理视觉树）
                // 注意：必须在导航完成后才移除，否则原区域会显示空白或错误内容
                // 先检查当前ActiveView是否已经是新View（导航成功）
                var currentActiveViewAfterNav = mainRegion.ActiveViews.FirstOrDefault();
                
                // 如果导航成功，新View已经显示，可以安全移除旧View
                // 如果导航没有成功，也需要移除旧View，让新View显示
                if (currentActiveViewAfterNav == activeView || currentActiveViewAfterNav == null)
                {
                    // 导航没有成功或View仍然是旧的，先移除旧View，然后再次尝试导航
                    // 这样可以确保原区域不会显示和浮动窗口一样的内容
                    mainRegion.Deactivate(activeView);
                    mainRegion.Remove(activeView);
                    
                    // 再次尝试导航（确保原区域显示新内容）
                    try
                    {
                        if (navigationService != null)
                        {
                            navigationService.NavigateByPageKeySilent(nextPageName);
                        }
                        else
                        {
                            navigateAction?.Invoke(nextPageName);
                        }
                        // 等待导航完成
                        System.Threading.Thread.Sleep(100);
                    }
                    catch
                    {
                        // 忽略错误，但确保原区域至少不会显示旧View
                    }
                }
                else
                {
                    // 导航成功，新View已经显示，可以安全移除旧View
                    mainRegion.Deactivate(activeView);
                    mainRegion.Remove(activeView);
                }
                
                // 4.5. 确保浮动页面按钮保持高亮（在View移除后）
                // 注意：这里只设置按钮高亮，不导航
                if (navigationService != null)
                {
                    navigationService.SetActiveButton(pageKey);
                }

                // 5. 创建浮动窗口（带独立RegionManager）
                var floatingWindow = new FloatingWindow();
                var scopedRegionManager = regionManager.CreateRegionManager();
                RegionManager.SetRegionManager(floatingWindow, scopedRegionManager);
                
                // 5.5. 添加浮动窗口 Activated 事件处理
                // 当用户点击浮动窗口时，自动高亮对应的导航按钮
                floatingWindow.Activated += (s, e) =>
                {
                    
                    // 立即高亮对应的导航按钮
                    navigationService?.SetActiveButton(pageKey);
                    
                    // 发布浮动窗口激活事件，通知 MainWindowViewModel 记录激活时间
                    OnFloatingWindowActivated(pageKey, navigationState, eventAggregator);
                };
                
                // 6. 初始化FloatingWindowViewModel
                if (floatingWindow.DataContext is FloatingWindowViewModel vm)
                {
                    // 从ViewModel获取DisplayPath作为标题，如果没有则从属性构建
                    string windowTitle = pageName;
                    if (activeView is FrameworkElement fe && fe.DataContext != null)
                    {
                        var dataContext = fe.DataContext;
                        var displayPathProperty = dataContext.GetType().GetProperty("DisplayPath");
                        if (displayPathProperty != null)
                        {
                            var displayPath = displayPathProperty.GetValue(dataContext) as string;
                            if (!string.IsNullOrEmpty(displayPath))
                            {
                                // 清理DisplayPath中的空值（避免出现"//？"）
                                windowTitle = displayPath.Replace("//", "/").Trim('/');
                                if (string.IsNullOrEmpty(windowTitle))
                                {
                                    windowTitle = BuildDisplayPathFromViewModel(dataContext, pageName);
                                }
                            }
                            else
                            {
                                // 如果DisplayPath为空，尝试从ViewModel属性构建
                                windowTitle = BuildDisplayPathFromViewModel(dataContext, pageName);
                            }
                        }
                        else
                        {
                            // 如果没有DisplayPath属性，尝试从ViewModel属性构建
                            windowTitle = BuildDisplayPathFromViewModel(dataContext, pageName);
                        }
                    }
                    
                    // 如果仍然无法构建标题，使用pageKey
                    if (string.IsNullOrEmpty(windowTitle) || windowTitle == pageName)
                    {
                        var pathFromPageKey = BuildDisplayPathFromPageKey(pageKey, pageName);
                        if (!string.IsNullOrEmpty(pathFromPageKey) && pathFromPageKey != pageName)
                        {
                            windowTitle = pathFromPageKey;
                        }
                    }
                    
                    vm.Initialize(
                        windowTitle, 
                        pageKey,
                        () => EmbedPage(pageKey, regionManager, eventAggregator, navigationState)
                    );
                    vm.SetNavigateAction(navigateAction);
                }

                // 8. 将View添加到浮动窗口的Region
                var floatingRegion = scopedRegionManager.Regions["FloatingRegion"];
                if (floatingRegion == null)
                {
                    return null;
                }

                floatingRegion.Add(activeView);
                floatingRegion.Activate(activeView);

                // 8.5. 手动触发INavigationAware.OnNavigatedTo
                // 因为使用Region.Add()不会自动触发INavigationAware接口
                // 需要手动通知ViewModel已进入新的RegionManager上下文
                var navigationAware = GetNavigationAware(activeView);
                if (navigationAware != null)
                {
                    try
                    {
                        // 构建NavigationContext，传递当前的RegionNavigationService
                        var navigationContext = new NavigationContext(
                            floatingRegion.NavigationService,
                            new Uri(pageKey, UriKind.Relative)
                        );
                        
                        // 调用OnNavigatedTo，让ViewModel更新其内部的RegionManager引用
                        navigationAware.OnNavigatedTo(navigationContext);
                    }
                    catch (Exception)
                    {
                        // 忽略OnNavigatedTo中的异常，不影响浮动窗口的创建
                    }
                }

                // 9. 保存信息
                _floatingWindows[pageKey] = new FloatingWindowInfo
                {
                    Window = floatingWindow,
                    View = activeView,
                    RegionManager = scopedRegionManager,
                    PageName = pageName  // 保留原始页面名称
                };

                // 10. 发布事件通知MainWindowViewModel（传递 PageKey）
                eventAggregator?.GetEvent<PageFloatedEvent>().Publish(new PageFloatedEventArgs { PageName = pageKey });

                // 11. 显示浮动窗口
                floatingWindow.Show();
                floatingWindow.Activate();  // 激活窗口并获得焦点
                floatingWindow.Focus();      // 确保窗口获得键盘焦点

                return pageKey;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// 兼容旧签名：未传入sourceView时回退到ActiveViews（不推荐）
        /// </summary>
        public static string FloatPage(string pageName, IRegionManager regionManager, IEventAggregator eventAggregator, INavigationStateService navigationState, Action<string> navigateAction)
        {
            // 回退：sourceView传null，内部将使用ActiveViews
            return FloatPage(pageName, null, regionManager, eventAggregator, navigationState, navigateAction, null);
        }

        /// <summary>
        /// 将浮动页面嵌入回主窗口
        /// </summary>
        /// <param name="pageKey">页面唯一键</param>
        /// <param name="mainRegionManager">主Region管理器</param>
        /// <param name="eventAggregator">事件聚合器</param>
        /// <param name="navigationState">导航状态服务</param>
        /// <returns>是否成功嵌入</returns>
        public static bool EmbedPage(string pageKey, IRegionManager mainRegionManager, IEventAggregator eventAggregator, INavigationStateService navigationState = null)
        {
            try
            {
                
                if (!_floatingWindows.TryGetValue(pageKey, out var info))
                {
                    return false;
                }


                // 1. 清理子区域 - 防止区域重复注册
                if (info.View is FrameworkElement viewElement)
                {
                    ClearChildRegions(viewElement, info.RegionManager);
                }

                // 2. 从浮动Region移除
                var floatingRegion = info.RegionManager.Regions["FloatingRegion"];
                floatingRegion.Deactivate(info.View);
                floatingRegion.Remove(info.View);

                // 3. 清理主RegionManager中可能存在的旧子区域引用
                if (info.View is FrameworkElement viewElement2)
                {
                    ClearChildRegions(viewElement2, mainRegionManager);
                }

                // 4. 添加回MainRegion
                var mainRegion = mainRegionManager.Regions["MainRegion"];
                mainRegion.Add(info.View);
                mainRegion.Activate(info.View);

                // 5. 不释放ViewModel资源
                // 注意：当View从浮动窗口移回MainRegion时，不应该释放ViewModel资源
                // View只是在不同的Region之间移动，ViewModel应该保持完整
                // 真正的资源释放应该在页面关闭（ReleaseCurrentPage）时进行

                // 6. 关闭浮动窗口
                info.Window.Close();

                // 7. 清理记录
                _floatingWindows.Remove(pageKey);

                // 8. 更新导航状态服务
                navigationState?.Unfloat(pageKey);

                // 9. 发布事件通知MainWindowViewModel（传递 PageKey）
                eventAggregator?.GetEvent<PageEmbeddedEvent>().Publish(new PageEmbeddedEventArgs { PageName = pageKey });

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// 最小化浮动窗口
        /// </summary>
        /// <param name="pageKey">页面唯一键</param>
        /// <param name="navigationState">导航状态服务</param>
        /// <param name="eventAggregator">事件聚合器</param>
        /// <param name="navigateAction">导航回调</param>
        /// <returns>是否成功最小化</returns>
        public static bool MinimizeFloatingWindow(string pageKey, INavigationStateService navigationState, IEventAggregator eventAggregator, Action<string> navigateAction)
        {
            try
            {
                if (!_floatingWindows.TryGetValue(pageKey, out var info))
                {
                    return false;
                }

                // 1. 标记为最小化状态
                navigationState?.MarkFloatingMinimized(pageKey);

                // 2. 提取 PageName
                string pageName = info.PageName;

                // 3. 最小化窗口，不手动改变高亮
                // Windows会自动将焦点给MainWindow，触发Activated事件
                info.Window.WindowState = WindowState.Minimized;

                // 4. 发布浮动窗口最小化事件
                eventAggregator?.GetEvent<FloatingWindowMinimizedEvent>().Publish(new FloatingWindowMinimizedEventArgs 
                { 
                    PageKey = pageKey,
                    PageName = pageName
                });

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// 从最小化状态恢复浮动窗口（只恢复窗口显示，不嵌入）
        /// </summary>
        /// <param name="pageKey">页面唯一键</param>
        /// <param name="navigationState">导航状态服务</param>
        /// <param name="eventAggregator">事件聚合器</param>
        /// <returns>是否成功恢复</returns>
        public static bool RestoreFloatingWindowFromMinimized(string pageKey, INavigationStateService navigationState, IEventAggregator eventAggregator)
        {
            try
            {
                if (!_floatingWindows.TryGetValue(pageKey, out var info))
                {
                    return false;
                }

                // 1. 标记为恢复状态（取消最小化）
                navigationState?.MarkFloatingRestored(pageKey);

                // 2. 恢复窗口到正常状态并激活
                info.Window.WindowState = WindowState.Normal;
                info.Window.Activate();
                info.Window.Focus();

                // 3. 发布浮动窗口恢复事件
                eventAggregator?.GetEvent<FloatingWindowRestoredEvent>().Publish(new FloatingWindowRestoredEventArgs 
                { 
                    PageKey = pageKey,
                    PageName = info.PageName
                });

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// 处理浮动窗口激活事件
        /// </summary>
        /// <param name="pageKey">页面唯一键</param>
        /// <param name="navigationState">导航状态服务</param>
        /// <param name="eventAggregator">事件聚合器</param>
        /// <returns>是否成功处理</returns>
        public static bool OnFloatingWindowActivated(string pageKey, INavigationStateService navigationState, IEventAggregator eventAggregator)
        {
            try
            {
                if (!_floatingWindows.TryGetValue(pageKey, out var info))
                {
                    return false;
                }

                // 1. 提取 PageName
                string pageName = info.PageName;

                // 2. 设置为当前激活的浮动窗口
                navigationState?.SetActiveFloatingPageKey(pageKey);

                // 3. 发布浮动窗口激活事件
                eventAggregator?.GetEvent<FloatingWindowActivatedEvent>().Publish(new FloatingWindowActivatedEventArgs 
                { 
                    PageKey = pageKey,
                    PageName = pageName
                });

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// 激活指定的浮动窗口（通过PageKey）
        /// </summary>
        /// <param name="pageKey">页面唯一键</param>
        /// <returns>是否成功激活</returns>
        public static bool ActivateFloatingWindow(string pageKey)
        {
            // ========== 调试日志：方法入口 ==========
            
            if (_floatingWindows.TryGetValue(pageKey, out var info))
            {
                try
                {
                    if (info.Window.WindowState == WindowState.Minimized)
                    {
                        info.Window.WindowState = WindowState.Normal;
                    }
                    
                    
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        
                        // 获取窗口句柄
                        var windowHelper = new System.Windows.Interop.WindowInteropHelper(info.Window);
                        IntPtr hWnd = windowHelper.Handle;
                        
                        // 使用 Win32 API 强制恢复窗口（如果最小化）
                        ShowWindow(hWnd, SW_RESTORE);
                        
                        // 使用 Win32 API 强制置顶窗口（先于WPF方法调用）
                        SetForegroundWindow(hWnd);
                        
                        // WPF 方法激活窗口
                        info.Window.Show();
                        
                        info.Window.Activate();
                        
                        info.Window.Focus();
                        
                        // 再次使用 Win32 API 强制置顶（确保在WPF方法之后仍然在最前面）
                        SetForegroundWindow(hWnd);
                        
                        // 使用 Topmost 作为最后的保障（延迟取消以确保窗口完全显示）
                        info.Window.Topmost = true;
                        
                        // 使用异步延迟取消 Topmost，避免立即被主窗口抢回焦点
                        System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            info.Window.Topmost = false;
                            
                            // 再次调用 SetForegroundWindow 确保窗口仍在最前面
                            SetForegroundWindow(hWnd);
                        }), System.Windows.Threading.DispatcherPriority.Background);
                        
                    }, System.Windows.Threading.DispatcherPriority.Send);
                    
                    return true;
                }
                catch (Exception)
                {
                }
            }
            else
            {
            }
            return false;
        }

        /// <summary>
        /// 检查页面是否正在浮动
        /// </summary>
        /// <param name="pageKey">页面唯一键</param>
        /// <returns>是否正在浮动</returns>
        public static bool IsPageFloating(string pageKey)
        {
            return _floatingWindows.ContainsKey(pageKey);
        }

        /// <summary>
        /// 检查指定页面类型是否有浮动窗口
        /// </summary>
        /// <param name="pageName">页面名称</param>
        /// <returns>是否有该类型的浮动窗口</returns>
        public static bool HasFloatingWindow(string pageName)
        {
            return _floatingWindows.Keys.Any(k => k.StartsWith($"{pageName}_"));
        }

        /// <summary>
        /// 关闭指定页面类型的所有浮动窗口
        /// </summary>
        /// <param name="pageName">页面名称</param>
        public static void CloseAllFloatingWindowsByPageName(string pageName)
        {
            var keysToRemove = _floatingWindows.Keys
                .Where(k => k.StartsWith($"{pageName}_"))
                .ToList();
            
            foreach (var key in keysToRemove)
            {
                if (_floatingWindows.TryGetValue(key, out var info))
                {
                    try
                    {
                        // 释放ViewModel资源
                        if (info.View is FrameworkElement fe && fe.DataContext is IDisposable disposable)
                        {
                            disposable.Dispose();
                        }
                        
                        // 关闭窗口
                        info.Window.Close();
                        
                        // 从字典中移除
                        _floatingWindows.Remove(key);
                        
                    }
                    catch (Exception)
                    {
                    }
                }
            }
        }

        /// <summary>
        /// 获取所有浮动页面的名称
        /// </summary>
        /// <returns>浮动页面名称列表</returns>
        public static IEnumerable<string> GetFloatingPageNames()
        {
            return _floatingWindows.Keys.ToList();
        }

        /// <summary>
        /// 关闭指定PageKey的浮动窗口
        /// </summary>
        /// <param name="pageKey">页面唯一键</param>
        /// <param name="regionManager">主Region管理器</param>
        /// <param name="eventAggregator">事件聚合器</param>
        /// <param name="navigationState">导航状态服务</param>
        /// <returns>是否成功关闭</returns>
        public static bool CloseFloatingWindow(string pageKey, IRegionManager regionManager, IEventAggregator eventAggregator, INavigationStateService navigationState)
        {
            if (!_floatingWindows.TryGetValue(pageKey, out var info))
            {
                return false;
            }

            try
            {
                // 释放ViewModel资源
                if (info.View is FrameworkElement fe && fe.DataContext is IDisposable disposable)
                {
                    disposable.Dispose();
                }

                // 从浮动Region移除
                var floatingRegion = info.RegionManager.Regions["FloatingRegion"];
                if (floatingRegion != null)
                {
                    floatingRegion.Deactivate(info.View);
                    floatingRegion.Remove(info.View);
                }

                // 清理子区域
                if (info.View is FrameworkElement viewElement)
                {
                    ClearChildRegions(viewElement, info.RegionManager);
                }

                // 关闭窗口
                info.Window.Close();

                // 清理记录
                _floatingWindows.Remove(pageKey);

                // 更新导航状态服务
                navigationState?.Unfloat(pageKey);

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// 关闭所有浮动窗口
        /// </summary>
        public static void CloseAllFloatingWindows()
        {
            var pageNames = _floatingWindows.Keys.ToList();
            foreach (var pageName in pageNames)
            {
                if (_floatingWindows.TryGetValue(pageName, out var info))
                {
                    try
                    {
                        // 释放ViewModel资源
                        if (info.View is FrameworkElement fe && fe.DataContext is IDisposable disposable)
                        {
                            disposable.Dispose();
                        }
                        
                        info.Window.Close();
                    }
                    catch (Exception)
                    {
                    }
                }
            }
            _floatingWindows.Clear();
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// 清理子区域 - 防止区域重复注册
        /// 注意：为了支持子Region跟随父页面浮动（如DatabaseConfig中的DatabaseRegion），
        /// 此方法已改为不清理子Region，让它们自然地随父页面在不同RegionManager之间迁移
        /// </summary>
        /// <param name="element">要清理的元素</param>
        /// <param name="regionManager">区域管理器</param>
        private static void ClearChildRegions(FrameworkElement element, IRegionManager regionManager)
        {
            try
            {
                // 先递归处理子元素（深度优先，从叶子节点开始清理）
                var childCount = System.Windows.Media.VisualTreeHelper.GetChildrenCount(element);
                for (int i = 0; i < childCount; i++)
                {
                    var child = System.Windows.Media.VisualTreeHelper.GetChild(element, i);
                    if (child is FrameworkElement childElement)
                    {
                        ClearChildRegions(childElement, regionManager);
                    }
                }

                // 获取元素上的RegionName
                var regionName = RegionManager.GetRegionName(element);
                if (!string.IsNullOrEmpty(regionName))
                {
                    // 跳过DatabaseRegion的清理，让它的内容跟随DatabaseConfig页面一起浮动
                    // 注意：不用return，只是跳过清理逻辑，但子元素的递归已经完成了
                    if (regionName != "DatabaseRegion")
                    {
                        // 如果该区域已注册，则移除
                        if (regionManager.Regions.ContainsRegionWithName(regionName))
                        {
                            var region = regionManager.Regions[regionName];
                            
                            // 清空区域中的所有视图
                            var views = region.Views.ToList();
                            foreach (var view in views)
                            {
                                region.Remove(view);
                            }
                            
                            // 从RegionManager中移除区域
                            regionManager.Regions.Remove(regionName);
                        }
                    }
                }
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// 生成唯一页面标识键
        /// </summary>
        /// <param name="pageName">页面名称</param>
        /// <param name="view">View实例</param>
        /// <returns>唯一页面键</returns>
        private static string GeneratePageKey(string pageName, object view)
        {
            
            // 从View的DataContext获取标识参数
            if (view is FrameworkElement fe && fe.DataContext != null)
            {
                var vm = fe.DataContext;
                
                // 1. 优先检查ChassisName（PxiChassis, TDMSystem）
                var chassisProperty = vm.GetType().GetProperty("ChassisName");
                if (chassisProperty != null)
                {
                    var chassisName = chassisProperty.GetValue(vm);
                    return $"{pageName}_{chassisName}";
                }
                
                // 2. 检查配置表页面（TestTaskName + ConfigTabelName）
                var testTaskProperty = vm.GetType().GetProperty("TestTaskName");
                var configTabelProperty = vm.GetType().GetProperty("ConfigTabelName");
                if (testTaskProperty != null && configTabelProperty != null)
                {
                    var testTask = testTaskProperty.GetValue(vm);
                    var configTabel = configTabelProperty.GetValue(vm);
                    return $"{pageName}_{testTask}_{configTabel}";
                }
                
                // 3. 回退到其他标识属性
                var idProperty = vm.GetType().GetProperty("ConfigId") 
                              ?? vm.GetType().GetProperty("Id");
                if (idProperty != null)
                {
                    var idValue = idProperty.GetValue(vm);
                    return $"{pageName}_{idValue}";
                }
            }
            
            // 4. 单例页面：直接返回pageName（无后缀）
            return pageName;
        }

        /// <summary>
        /// 获取View或ViewModel中实现的INavigationAware接口
        /// </summary>
        /// <param name="view">View实例</param>
        /// <returns>INavigationAware实例，如果未实现则返回null</returns>
        private static INavigationAware GetNavigationAware(object view)
        {
            if (view == null)
                return null;

            // 1. 检查View本身是否实现INavigationAware
            if (view is INavigationAware viewNavigationAware)
            {
                return viewNavigationAware;
            }

            // 2. 检查ViewModel是否实现INavigationAware
            if (view is FrameworkElement frameworkElement)
            {
                if (frameworkElement.DataContext is INavigationAware viewModelNavigationAware)
                {
                    return viewModelNavigationAware;
                }
            }

            return null;
        }

        /// <summary>
        /// 从ViewModel属性构建显示路径
        /// </summary>
        private static string BuildDisplayPathFromViewModel(object viewModel, string pageName = null)
        {
            if (viewModel == null) return null;

            var pathParts = new List<string>();
            var vmType = viewModel.GetType();

            // 获取ChassisName
            var chassisProperty = vmType.GetProperty("ChassisName");
            if (chassisProperty != null)
            {
                var chassisName = chassisProperty.GetValue(viewModel) as string;
                if (!string.IsNullOrEmpty(chassisName) && chassisName != string.Empty)
                {
                    pathParts.Add(chassisName);
                }
            }

            // 获取TestTaskName
            var testTaskProperty = vmType.GetProperty("TestTaskName");
            if (testTaskProperty != null)
            {
                var testTaskName = testTaskProperty.GetValue(viewModel) as string;
                if (!string.IsNullOrEmpty(testTaskName))
                {
                    pathParts.Add(testTaskName);
                }
            }

            // 获取ParentType并转换为显示名称
            var parentTypeProperty = vmType.GetProperty("ParentType");
            string parentDisplayName = null;
            if (parentTypeProperty != null)
            {
                var parentType = parentTypeProperty.GetValue(viewModel) as string;
                if (!string.IsNullOrEmpty(parentType))
                {
                    parentDisplayName = GetParentDisplayNameFromType(parentType);
                }
            }
            
            // 如果没有ParentType，尝试从pageName推断
            if (string.IsNullOrEmpty(parentDisplayName) && !string.IsNullOrEmpty(pageName))
            {
                parentDisplayName = GetParentDisplayNameFromPageName(pageName);
            }
            
            if (!string.IsNullOrEmpty(parentDisplayName))
            {
                pathParts.Add(parentDisplayName);
            }

            // 获取ConfigTabelName
            var configTabelProperty = vmType.GetProperty("ConfigTabelName");
            if (configTabelProperty != null)
            {
                var configTabelName = configTabelProperty.GetValue(viewModel) as string;
                if (!string.IsNullOrEmpty(configTabelName))
                {
                    pathParts.Add(configTabelName);
                }
            }

            return pathParts.Count > 0 ? string.Join("/", pathParts) : null;
        }

        /// <summary>
        /// 从pageKey构建显示路径
        /// </summary>
        private static string BuildDisplayPathFromPageKey(string pageKey, string pageName)
        {
            if (string.IsNullOrEmpty(pageKey) || pageKey == pageName)
            {
                return pageName;
            }

            // pageKey格式通常是：PageName_TestTaskName-ConfigTabelName 或 PageName_ChassisName
            // 例如：IcdConfigTabel_测试任务1-ICD配置表1-CAN
            
            if (pageKey.StartsWith($"{pageName}_"))
            {
                var suffix = pageKey.Substring(pageName.Length + 1);
                // 尝试解析后缀
                // 对于配置表：TestTaskName-ConfigTabelName
                // 对于机箱：ChassisName
                if (suffix.Contains("-"))
                {
                    var parts = suffix.Split(new[] { '-' }, 2);
                    if (parts.Length == 2)
                    {
                        var testTaskName = parts[0];
                        var configTabelName = parts[1];
                        var parentName = GetParentDisplayNameFromPageName(pageName);
                        return $"{testTaskName}/{parentName}/{configTabelName}";
                    }
                }
                else
                {
                    // 可能是机箱名称
                    return suffix;
                }
            }

            return pageKey;
        }

        /// <summary>
        /// 从ParentType获取父节点显示名称
        /// </summary>
        private static string GetParentDisplayNameFromType(string parentType)
        {
            return parentType switch
            {
                "channel_config" => "通道配置",
                "signal_config" => "信号配置",
                "icd_config" => "ICD配置",
                "test_sequence" => "测试序列",
                "report" => "报表模板",
                _ => parentType
            };
        }

        /// <summary>
        /// 从页面名称推断父节点显示名称
        /// </summary>
        private static string GetParentDisplayNameFromPageName(string pageName)
        {
            if (string.IsNullOrEmpty(pageName))
                return null;

            return pageName switch
            {
                "IcdConfigTabel" => "ICD配置",
                "ChannelConfigTabel" => "通道配置",
                "SignalConfigTabel" => "信号配置",
                "NCommunicatingSignalConfigTabel" => "信号配置",
                "CommunicatingSignalConfigTabel" => "信号配置",
                "IcdMappingTabel" => "ICD映射",
                _ => null
            };
        }

        #endregion
    }

    #region Helper Classes

    /// <summary>
    /// 浮动窗口信息
    /// </summary>
    internal class FloatingWindowInfo
    {
        /// <summary>
        /// 浮动窗口实例
        /// </summary>
        public FloatingWindow Window { get; set; }

        /// <summary>
        /// 浮动窗口中的View实例
        /// </summary>
        public object View { get; set; }

        /// <summary>
        /// 浮动窗口的Region管理器
        /// </summary>
        public IRegionManager RegionManager { get; set; }

        /// <summary>
        /// 原始页面名称（保留用于标识页面类型）
        /// </summary>
        public string PageName { get; set; }
    }

    #endregion
}