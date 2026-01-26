using System;
using System.Collections.Generic;
using System.Linq;

namespace MeasureControl.Services
{
    /// <summary>
    /// 浮动页面状态信息
    /// </summary>
    public class FloatingPageState
    {
        /// <summary>
        /// 页面实例唯一标识（PageKey）
        /// </summary>
        public string PageKey { get; set; }

        /// <summary>
        /// 页面类型名称（PageName）
        /// </summary>
        public string PageName { get; set; }

        /// <summary>
        /// 是否最小化
        /// </summary>
        public bool IsMinimized { get; set; }
    }

    /// <summary>
    /// 最小化页面状态信息
    /// </summary>
    public class MinimizedPageState
    {
        /// <summary>
        /// 页面实例唯一标识（PageKey）
        /// </summary>
        public string PageKey { get; set; }

        /// <summary>
        /// 页面类型名称（PageName）
        /// </summary>
        public string PageName { get; set; }
    }

    /// <summary>
    /// 导航状态服务 - 统一管理页面状态（打开/最小化/浮动）和激活历史
    /// </summary>
    public interface INavigationStateService
    {
        /// <summary>
        /// 打开页面（添加到打开集合，并推到激活历史最前）
        /// </summary>
        void OpenPage(string pageName);

        /// <summary>
        /// 关闭页面（从所有集合和历史中移除）
        /// </summary>
        void ClosePage(string pageName);

        /// <summary>
        /// 推到激活历史最前（去重）
        /// </summary>
        void PushActivated(string pageName);

        /// <summary>
        /// 标记页面为最小化
        /// </summary>
        void MarkMinimized(string pageName);

        /// <summary>
        /// 取消最小化
        /// </summary>
        void Unminimize(string pageName);

        /// <summary>
        /// 标记页面为浮动（多实例支持）
        /// </summary>
        /// <param name="pageKey">页面实例唯一标识</param>
        /// <param name="pageName">页面类型名称</param>
        void MarkFloating(string pageKey, string pageName);

        /// <summary>
        /// 标记浮动窗口为最小化
        /// </summary>
        /// <param name="pageKey">页面实例唯一标识</param>
        void MarkFloatingMinimized(string pageKey);

        /// <summary>
        /// 标记浮动窗口恢复（取消最小化）
        /// </summary>
        /// <param name="pageKey">页面实例唯一标识</param>
        void MarkFloatingRestored(string pageKey);

        /// <summary>
        /// 取消浮动（移除浮动窗口实例）
        /// </summary>
        /// <param name="pageKey">页面实例唯一标识</param>
        void Unfloat(string pageKey);

        /// <summary>
        /// 检查页面是否打开
        /// </summary>
        bool IsPageOpen(string pageName);

        /// <summary>
        /// 检查页面是否最小化
        /// </summary>
        bool IsPageMinimized(string pageName);

        /// <summary>
        /// 检查特定浮动窗口实例是否存在
        /// </summary>
        /// <param name="pageKey">页面实例唯一标识</param>
        bool IsFloating(string pageKey);

        /// <summary>
        /// 检查页面类型是否有浮动实例
        /// </summary>
        /// <param name="pageName">页面类型名称</param>
        bool IsPageTypeFloating(string pageName);

        /// <summary>
        /// 检查页面是否浮动（向后兼容）
        /// </summary>
        bool IsPageFloating(string pageName);

        /// <summary>
        /// 获取当前激活的浮动窗口 PageKey
        /// </summary>
        string GetActiveFloatingPageKey();

        /// <summary>
        /// 设置当前激活的浮动窗口 PageKey
        /// </summary>
        void SetActiveFloatingPageKey(string pageKey);

        /// <summary>
        /// 获取下一个可导航的页面（不在最小化和浮动集合中）
        /// 如果找不到，返回 fallbackPage
        /// </summary>
        /// <param name="fallbackPage">找不到时返回的默认页面</param>
        /// <param name="excludePage">要排除的页面（通常是当前正在操作的页面）</param>
        string GetNextPageOrFallback(string fallbackPage, string excludePage = null);

        /// <summary>
        /// 获取所有打开的页面
        /// </summary>
        IEnumerable<string> GetOpenPages();

        /// <summary>
        /// 获取所有最小化的页面
        /// </summary>
        IEnumerable<string> GetMinimizedPages();

        /// <summary>
        /// 获取所有浮动的页面（返回 PageKey 列表）
        /// </summary>
        IEnumerable<string> GetFloatingPages();

        /// <summary>
        /// 获取所有浮动页面状态信息
        /// </summary>
        IEnumerable<FloatingPageState> GetFloatingPageStates();

        /// <summary>
        /// 获取激活历史（从最近到最旧）
        /// </summary>
        IEnumerable<string> GetActivationHistory();

        /// <summary>
        /// 清空所有状态
        /// </summary>
        void Clear();
    }

    /// <summary>
    /// 导航状态服务实现
    /// </summary>
    public class NavigationStateService : INavigationStateService
    {
        private readonly HashSet<string> _openPages = new HashSet<string>();
        private readonly Dictionary<string, MinimizedPageState> _minimizedPages = new Dictionary<string, MinimizedPageState>();
        private readonly Dictionary<string, FloatingPageState> _floatingPages = new Dictionary<string, FloatingPageState>();
        private readonly LinkedList<string> _activationHistory = new LinkedList<string>();
        private string _activeFloatingPageKey = null;
        private readonly object _lock = new object();

        public void OpenPage(string pageName)
        {
            if (string.IsNullOrWhiteSpace(pageName))
                return;

            lock (_lock)
            {
                bool wasOpen = _openPages.Contains(pageName);
                _openPages.Add(pageName);
                PushActivatedInternal(pageName);
                
                if (!wasOpen)
                {
                }
            }
        }

        public void ClosePage(string pageName)
        {
            if (string.IsNullOrWhiteSpace(pageName))
                return;

            lock (_lock)
            {
                bool wasOpen = _openPages.Contains(pageName);
                _openPages.Remove(pageName);
                _minimizedPages.Remove(pageName);
                
                // 移除与该名称匹配的浮动窗口实例（兼容 pageName 与 pageKey 两种传入）
                var floatingKeysToRemove = _floatingPages.Values
                    .Where(state => state.PageName == pageName || state.PageKey == pageName)
                    .Select(state => state.PageKey)
                    .ToList();
                
                foreach (var key in floatingKeysToRemove)
                {
                    _floatingPages.Remove(key);
                    
                    // 如果关闭的是当前激活的浮动窗口，清除激活标记
                    if (_activeFloatingPageKey == key)
                    {
                        _activeFloatingPageKey = null;
                    }
                }
                
                // 从激活历史中移除
                var node = _activationHistory.Find(pageName);
                if (node != null)
                {
                    _activationHistory.Remove(node);
                }
                
                if (wasOpen)
                {
                }
            }
        }

        public void PushActivated(string pageName)
        {
            if (string.IsNullOrWhiteSpace(pageName))
                return;

            lock (_lock)
            {
                PushActivatedInternal(pageName);
            }
        }

        private void PushActivatedInternal(string pageName)
        {
            // 从历史中移除旧位置（如果存在）
            var node = _activationHistory.Find(pageName);
            if (node != null)
            {
                _activationHistory.Remove(node);
            }

            // 推到最前面
            _activationHistory.AddFirst(pageName);
        }

        public void MarkMinimized(string pageName)
        {
            if (string.IsNullOrWhiteSpace(pageName))
                return;

            lock (_lock)
            {
                if (_openPages.Contains(pageName))
                {
                    bool wasMinimized = _minimizedPages.ContainsKey(pageName);
                    
                    if (!wasMinimized)
                    {
                        _minimizedPages[pageName] = new MinimizedPageState
                        {
                            PageKey = pageName,
                            PageName = pageName
                        };
                        
                    }
                }
            }
        }

        public void Unminimize(string pageName)
        {
            if (string.IsNullOrWhiteSpace(pageName))
                return;

            lock (_lock)
            {
                _minimizedPages.Remove(pageName);
            }
        }

        public void MarkFloating(string pageKey, string pageName)
        {
            if (string.IsNullOrWhiteSpace(pageKey) || string.IsNullOrWhiteSpace(pageName))
                return;

            lock (_lock)
            {
                // 检查pageKey是否在打开列表中（而不是pageName）
                // 因为_openPages存储的是PageKey（完整标识）
                if (_openPages.Contains(pageKey))
                {
                    bool wasFloating = _floatingPages.ContainsKey(pageKey);
                    _floatingPages[pageKey] = new FloatingPageState
                    {
                        PageKey = pageKey,
                        PageName = pageName,
                        IsMinimized = false
                    };
                    
                    if (!wasFloating)
                    {
                    }
                }
                else
                {
                }
            }
        }

        public void MarkFloatingMinimized(string pageKey)
        {
            if (string.IsNullOrWhiteSpace(pageKey))
                return;

            lock (_lock)
            {
                if (_floatingPages.ContainsKey(pageKey))
                {
                    _floatingPages[pageKey].IsMinimized = true;
                }
            }
        }

        public void MarkFloatingRestored(string pageKey)
        {
            if (string.IsNullOrWhiteSpace(pageKey))
                return;

            lock (_lock)
            {
                if (_floatingPages.ContainsKey(pageKey))
                {
                    _floatingPages[pageKey].IsMinimized = false;
                }
            }
        }

        public void Unfloat(string pageKey)
        {
            if (string.IsNullOrWhiteSpace(pageKey))
                return;

            lock (_lock)
            {
                if (_floatingPages.ContainsKey(pageKey))
                {
                    var pageName = _floatingPages[pageKey].PageName;
                    _floatingPages.Remove(pageKey);
                    
                    // 如果关闭的是当前激活的浮动窗口，清除激活标记
                    if (_activeFloatingPageKey == pageKey)
                    {
                        _activeFloatingPageKey = null;
                    }
                    
                }
            }
        }

        public bool IsPageOpen(string pageName)
        {
            if (string.IsNullOrWhiteSpace(pageName))
                return false;

            lock (_lock)
            {
                return _openPages.Contains(pageName);
            }
        }

        public bool IsPageMinimized(string pageName)
        {
            if (string.IsNullOrWhiteSpace(pageName))
                return false;

            lock (_lock)
            {
                return _minimizedPages.ContainsKey(pageName);
            }
        }

        public bool IsFloating(string pageKey)
        {
            if (string.IsNullOrWhiteSpace(pageKey))
                return false;

            lock (_lock)
            {
                return _floatingPages.ContainsKey(pageKey);
            }
        }

        public bool IsPageTypeFloating(string pageName)
        {
            if (string.IsNullOrWhiteSpace(pageName))
                return false;

            lock (_lock)
            {
                return _floatingPages.Values.Any(state => state.PageName == pageName);
            }
        }

        public bool IsPageFloating(string pageName)
        {
            // 向后兼容方法，检查页面类型是否有浮动实例
            return IsPageTypeFloating(pageName);
        }

        public string GetActiveFloatingPageKey()
        {
            lock (_lock)
            {
                return _activeFloatingPageKey;
            }
        }

        public void SetActiveFloatingPageKey(string pageKey)
        {
            lock (_lock)
            {
                _activeFloatingPageKey = pageKey;
            }
        }

        public string GetNextPageOrFallback(string fallbackPage = "HomePage", string excludePage = null)
        {
            lock (_lock)
            {
                // 从激活历史中找第一个同时满足以下条件的页面：
                // 1. 在 openPages 中（按钮还在）
                // 2. 不在 minimizedPages 中（未被最小化）
                // 3. 不在浮动窗口中（pageKey不在_floatingPages的键中）
                // 4. 不是被排除的页面（excludePage）
                foreach (var pageKey in _activationHistory)
                {
                    if (_openPages.Contains(pageKey) &&
                        !_minimizedPages.ContainsKey(pageKey) &&
                        !_floatingPages.ContainsKey(pageKey) &&
                        (string.IsNullOrEmpty(excludePage) || pageKey != excludePage))
                    {
                        return pageKey;
                    }
                }

                // 找不到符合条件的页面，返回 fallback（默认为HomePage）
                return fallbackPage;
            }
        }

        public IEnumerable<string> GetOpenPages()
        {
            lock (_lock)
            {
                return _openPages.ToList();
            }
        }

        public IEnumerable<string> GetMinimizedPages()
        {
            lock (_lock)
            {
                return _minimizedPages.Keys.ToList();
            }
        }

        public IEnumerable<string> GetFloatingPages()
        {
            lock (_lock)
            {
                return _floatingPages.Keys.ToList();
            }
        }

        public IEnumerable<FloatingPageState> GetFloatingPageStates()
        {
            lock (_lock)
            {
                return _floatingPages.Values.ToList();
            }
        }

        public IEnumerable<string> GetActivationHistory()
        {
            lock (_lock)
            {
                return _activationHistory.ToList();
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _openPages.Clear();
                _minimizedPages.Clear();
                _floatingPages.Clear();
                _activationHistory.Clear();
            }
        }
    }
}

