using System;
using System.Collections.ObjectModel;
using System.Windows.Threading;
using MeasureControl.Models;
using MeasureControl.Models.Devices;

namespace MeasureControl.Helpers
{
    /// <summary>
    /// 资源清理辅助类，统一管理资源清理逻辑
    /// </summary>
    public static class ResourceCleanupHelper
    {
        /// <summary>
        /// 清理项目数据
        /// </summary>
        public static void CleanupProjectData(ref ObservableCollection<ProjectItem> currentProject, ref string currentProjectPath)
        {
            try
            {
                if (currentProject != null)
                {
                    currentProject.Clear();
                    currentProject = null;
                }
                
                currentProjectPath = null;
            }
            catch (Exception)
            {
                // 忽略清理异常
            }
        }

        /// <summary>
        /// 清理设备集合
        /// </summary>
        public static void CleanupDeviceCollection(ObservableCollection<DeviceBase> devices)
        {
            try
            {
                if (devices == null) return;

                // 递归清理所有设备的子设备
                foreach (var device in devices)
                {
                    CleanupDevice(device);
                }

                devices.Clear();
            }
            catch (Exception)
            {
                // 忽略清理异常
            }
        }

        /// <summary>
        /// 清理单个设备及其子设备
        /// </summary>
        public static void CleanupDevice(DeviceBase device)
        {
            try
            {
                if (device == null) return;

                // 递归清理子设备
                if (device.Children != null && device.Children.Count > 0)
                {
                    foreach (var child in device.Children)
                    {
                        CleanupDevice(child);
                    }
                    device.Children.Clear();
                }

                // 清理设备属性
                device.IsSelected = false;
                device.IsExpanded = false;
            }
            catch (Exception)
            {
                // 忽略清理异常
            }
        }

        /// <summary>
        /// 清理机箱集合
        /// </summary>
        public static void CleanupChassisCollection(ObservableCollection<ChassisModel> chassisList)
        {
            try
            {
                if (chassisList == null) return;

                // 清理每个机箱的设备
                foreach (var chassis in chassisList)
                {
                    if (chassis.Devices != null)
                    {
                        CleanupDeviceCollection(chassis.Devices);
                    }
                    chassis.IsSelected = false;
                }

                chassisList.Clear();
            }
            catch (Exception)
            {
                // 忽略清理异常
            }
        }

        /// <summary>
        /// 清理定时器
        /// </summary>
        public static void CleanupTimer(ref DispatcherTimer timer)
        {
            try
            {
                if (timer != null)
                {
                    timer.Stop();
                    timer = null;
                }
            }
            catch (Exception)
            {
                // 忽略清理异常
            }
        }

        /// <summary>
        /// 释放定时器（取消订阅事件并停止）
        /// </summary>
        public static void DisposeTimer(ref DispatcherTimer timer, EventHandler tickHandler)
        {
            try
            {
                if (timer != null)
                {
                    timer.Stop();
                    if (tickHandler != null)
                    {
                        timer.Tick -= tickHandler;
                    }
                    timer = null;
                }
            }
            catch (Exception)
            {
                // 忽略清理异常
            }
        }

        /// <summary>
        /// 清理集合
        /// </summary>
        public static void CleanupCollection<T>(ObservableCollection<T> collection)
        {
            try
            {
                if (collection != null)
                {
                    collection.Clear();
                }
            }
            catch (Exception)
            {
                // 忽略清理异常
            }
        }

        /// <summary>
        /// 尝试执行清理操作（如果失败会记录日志但不抛出异常）
        /// </summary>
        /// <param name="cleanupAction">清理操作</param>
        /// <param name="operationName">操作名称（用于日志）</param>
        public static void TryCleanup(Action cleanupAction, string operationName)
        {
            try
            {
                cleanupAction?.Invoke();
            }
            catch (Exception)
            {
                // 忽略清理异常
            }
        }

        /// <summary>
        /// 安全执行清理操作（已弃用，请使用TryCleanup）
        /// </summary>
        [Obsolete("请使用TryCleanup方法，该方法名称更准确")]
        public static void SafeCleanup(Action cleanupAction, string operationName)
        {
            TryCleanup(cleanupAction, operationName);
        }

        /// <summary>
        /// 安全释放ViewModel资源
        /// </summary>
        public static void DisposeViewModel(object viewModel, string viewModelName = null)
        {
            TryCleanup(() =>
            {
                if (viewModel is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }, $"释放ViewModel资源: {viewModelName ?? viewModel?.GetType().Name}");
        }
    }
}

