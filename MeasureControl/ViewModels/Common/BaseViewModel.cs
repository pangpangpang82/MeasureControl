using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using MeasureControl.Helpers;
using Prism.Mvvm;

namespace MeasureControl.ViewModels.Common
{
    /// <summary>
    /// ViewModel 基类 - 提供统一的初始化和资源清理模式
    /// </summary>
    public abstract class BaseViewModel : BindableBase, IDisposable
    {
        #region Private Fields

        private bool _disposed;
        private readonly List<Action> _eventUnsubscribeActions = new List<Action>();

        #endregion

        #region Constructor

        protected BaseViewModel()
        {
            // 不在构造函数中自动调用初始化方法
            // 子类需要在构造函数最后调用 Initialize()
        }

        #endregion

        #region Initialization

        /// <summary>
        /// 初始化 ViewModel - 子类必须在构造函数最后调用此方法
        /// </summary>
        protected void Initialize()
        {
            InitializeCollections();
            InitializeCommands();
            SubscribeToEvents();
        }

        #endregion

        #region Abstract/Virtual Methods

        /// <summary>
        /// 初始化集合 - 子类重写此方法来初始化 ObservableCollection 等集合
        /// </summary>
        protected virtual void InitializeCollections()
        {
        }

        /// <summary>
        /// 初始化命令 - 子类重写此方法来初始化 ICommand
        /// </summary>
        protected virtual void InitializeCommands()
        {
        }

        /// <summary>
        /// 订阅事件 - 子类重写此方法来订阅事件
        /// </summary>
        protected virtual void SubscribeToEvents()
        {
        }

        /// <summary>
        /// 释放资源 - 子类重写此方法来清理特定资源
        /// </summary>
        protected virtual void OnDisposing()
        {
        }

        #endregion

        #region Event Subscription Management

        /// <summary>
        /// 跟踪事件订阅，以便在 Dispose 时自动取消订阅
        /// </summary>
        /// <param name="unsubscribeAction">取消订阅的操作</param>
        protected void TrackEventSubscription(Action unsubscribeAction)
        {
            if (unsubscribeAction != null)
            {
                _eventUnsubscribeActions.Add(unsubscribeAction);
            }
        }

        /// <summary>
        /// 取消所有跟踪的事件订阅
        /// </summary>
        private void UnsubscribeAllEvents()
        {
            foreach (var unsubscribe in _eventUnsubscribeActions)
            {
                try
                {
                    unsubscribe?.Invoke();
                }
                catch (Exception)
                {
                    // 忽略取消订阅时的异常
                }
            }
            _eventUnsubscribeActions.Clear();
        }

        #endregion

        #region Collection Cleanup Helpers

        /// <summary>
        /// 清理 ObservableCollection 集合
        /// </summary>
        protected void CleanupCollection<T>(ObservableCollection<T> collection)
        {
            ResourceCleanupHelper.CleanupCollection(collection);
        }

        /// <summary>
        /// 清空集合（别名方法）
        /// </summary>
        protected void ClearCollection<T>(ObservableCollection<T> collection)
        {
            ResourceCleanupHelper.CleanupCollection(collection);
        }

        /// <summary>
        /// 清理多个集合
        /// </summary>
        protected void CleanupCollections(params object[] collections)
        {
            foreach (var collection in collections)
            {
                if (collection is ObservableCollection<object> obsCollection)
                {
                    ResourceCleanupHelper.CleanupCollection(obsCollection);
                }
            }
        }

        #endregion

        #region IDisposable Implementation

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// 释放资源（保护方法）
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                // 使用 ResourceCleanupHelper 统一清理资源
                ResourceCleanupHelper.TryCleanup(() =>
                {
                    // 取消所有事件订阅
                    UnsubscribeAllEvents();

                    // 调用子类的清理方法
                    OnDisposing();

                }, GetType().Name + "资源清理");
            }

            _disposed = true;
        }

        /// <summary>
        /// 检查对象是否已释放
        /// </summary>
        protected void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(GetType().Name);
            }
        }

        #endregion
    }
}

