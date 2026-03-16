using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using MeasureControl.Models;
using MeasureControl.Models.Channels;
using MeasureControl.Models.Devices;

namespace MeasureControl.Services
{
    /// <summary>
    /// 通道管理器服务，负责全局通道的注册、查询和管理
    /// </summary>
    public class ChannelManager
    {
        private readonly Dictionary<string, DeviceBase> _registeredDevices;
        private readonly ObservableCollection<ChannelBase> _allChannels;
        private readonly object _lock = new object();

        /// <summary>
        /// 所有注册的通道集合
        /// </summary>
        public ObservableCollection<ChannelBase> AllChannels
        {
            get
            {
                lock (_lock)
                {
                    return _allChannels;
                }
            }
        }

        public ChannelManager()
        {
            _registeredDevices = new Dictionary<string, DeviceBase>();
            _allChannels = new ObservableCollection<ChannelBase>();
        }

        /// <summary>
        /// 注册设备及其通道
        /// </summary>
        /// <param name="device">要注册的设备</param>
        public void RegisterDevice(DeviceBase device)
        {
            if (device == null)
                throw new ArgumentNullException(nameof(device));

            lock (_lock)
            {
                // 如果设备已注册，先注销
                if (_registeredDevices.ContainsKey(device.Id))
                {
                    UnregisterDevice(device.Id);
                }

                // 注册设备
                _registeredDevices[device.Id] = device;

                // 注册设备的所有通道
                if (device.Channels != null)
                {
                    foreach (var channel in device.Channels)
                    {
                        _allChannels.Add(channel);
                    }
                }
            }
        }

        /// <summary>
        /// 注销设备及其通道
        /// </summary>
        /// <param name="deviceId">设备ID</param>
        public void UnregisterDevice(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId))
                return;

            lock (_lock)
            {
                if (!_registeredDevices.ContainsKey(deviceId))
                    return;

                // 移除该设备的所有通道
                var channelsToRemove = _allChannels
                    .Where(c => c.DeviceId == deviceId)
                    .ToList();

                foreach (var channel in channelsToRemove)
                {
                    _allChannels.Remove(channel);
                }

                // 移除设备
                _registeredDevices.Remove(deviceId);
            }
        }

        /// <summary>
        /// 获取所有通道
        /// </summary>
        /// <returns>所有通道的集合</returns>
        public ObservableCollection<ChannelBase> GetAllChannels()
        {
            lock (_lock)
            {
                return new ObservableCollection<ChannelBase>(_allChannels);
            }
        }

        /// <summary>
        /// 按通道类型筛选通道
        /// </summary>
        /// <param name="channelType">通道类型（如：AI, AO, DI, DO, CAN等）</param>
        /// <returns>指定类型的通道集合</returns>
        public ObservableCollection<ChannelBase> GetChannelsByType(string channelType)
        {
            if (string.IsNullOrEmpty(channelType))
                return new ObservableCollection<ChannelBase>();

            lock (_lock)
            {
                var channels = _allChannels
                    .Where(c => c.ChannelType != null && 
                               c.ChannelType.Equals(channelType, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                return new ObservableCollection<ChannelBase>(channels);
            }
        }

        /// <summary>
        /// 按通道ID查找通道
        /// </summary>
        /// <param name="channelId">通道ID</param>
        /// <returns>找到的通道，未找到返回null</returns>
        public ChannelBase GetChannelById(string channelId)
        {
            if (string.IsNullOrEmpty(channelId))
                return null;

            lock (_lock)
            {
                return _allChannels.FirstOrDefault(c => c.Id == channelId);
            }
        }

        /// <summary>
        /// 获取某个设备的所有通道
        /// </summary>
        /// <param name="deviceId">设备ID</param>
        /// <returns>设备的通道集合</returns>
        public ObservableCollection<ChannelBase> GetDeviceChannels(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId))
                return new ObservableCollection<ChannelBase>();

            lock (_lock)
            {
                var channels = _allChannels
                    .Where(c => c.DeviceId == deviceId)
                    .ToList();

                return new ObservableCollection<ChannelBase>(channels);
            }
        }

        /// <summary>
        /// 获取可用的（未绑定的）通道
        /// </summary>
        /// <returns>可用通道集合</returns>
        public ObservableCollection<ChannelBase> GetAvailableChannels()
        {
            lock (_lock)
            {
                var availableChannels = _allChannels
                    .Where(c => c.Status == "可用")
                    .ToList();

                return new ObservableCollection<ChannelBase>(availableChannels);
            }
        }

        /// <summary>
        /// 获取已绑定的通道
        /// </summary>
        /// <returns>已绑定通道集合</returns>
        public ObservableCollection<ChannelBase> GetBoundChannels()
        {
            lock (_lock)
            {
                var boundChannels = _allChannels
                    .Where(c => c.Status == "已绑定")
                    .ToList();

                return new ObservableCollection<ChannelBase>(boundChannels);
            }
        }

        /// <summary>
        /// 从机箱刷新通道
        /// 遍历机箱中的所有设备，注册它们的通道
        /// </summary>
        /// <param name="chassis">机箱模型</param>
        public void RefreshChannelsFromChassis(ChassisModel chassis)
        {
            if (chassis == null)
                return;

            // 遍历机箱中的所有设备
            foreach (var device in chassis.Devices)
            {
                // 确保设备通道已初始化
                if (device.Channels == null || device.Channels.Count == 0)
                {
                    device.InitializeChannels();
                }

                // 注册设备
                RegisterDevice(device);

                // 递归处理子设备
                RefreshChannelsFromDeviceChildren(device);
            }
        }

        /// <summary>
        /// 递归刷新设备子节点的通道
        /// </summary>
        /// <param name="device">设备</param>
        private void RefreshChannelsFromDeviceChildren(DeviceBase device)
        {
            if (device == null || device.Children == null || device.Children.Count == 0)
                return;

            foreach (var child in device.Children)
            {
                // 确保子设备通道已初始化
                if (child.Channels == null || child.Channels.Count == 0)
                {
                    child.InitializeChannels();
                }

                // 注册子设备（如果有ID）
                if (!string.IsNullOrEmpty(child.Id))
                {
                    RegisterDevice(child);
                }

                // 递归处理
                RefreshChannelsFromDeviceChildren(child);
            }
        }

        /// <summary>
        /// 清空所有通道和设备注册
        /// </summary>
        public void Clear()
        {
            lock (_lock)
            {
                _allChannels.Clear();
                _registeredDevices.Clear();
            }
        }

        /// <summary>
        /// 获取通道统计信息
        /// </summary>
        /// <returns>通道统计字典（通道类型 -> 数量）</returns>
        public Dictionary<string, int> GetChannelStatistics()
        {
            lock (_lock)
            {
                var stats = new Dictionary<string, int>();

                foreach (var channel in _allChannels)
                {
                    var type = channel.ChannelType ?? "Unknown";
                    if (stats.ContainsKey(type))
                    {
                        stats[type]++;
                    }
                    else
                    {
                        stats[type] = 1;
                    }
                }

                return stats;
            }
        }

        /// <summary>
        /// 更新通道状态
        /// </summary>
        /// <param name="channelId">通道ID</param>
        /// <param name="newStatus">新状态</param>
        public void UpdateChannelStatus(string channelId, string newStatus)
        {
            lock (_lock)
            {
                var channel = _allChannels.FirstOrDefault(c => c.Id == channelId);
                if (channel != null)
                {
                    channel.Status = newStatus;
                }
            }
        }

        /// <summary>
        /// 批量更新通道状态
        /// </summary>
        /// <param name="channelIds">通道ID列表</param>
        /// <param name="newStatus">新状态</param>
        public void UpdateChannelStatusBatch(IEnumerable<string> channelIds, string newStatus)
        {
            if (channelIds == null)
                return;

            lock (_lock)
            {
                foreach (var channelId in channelIds)
                {
                    var channel = _allChannels.FirstOrDefault(c => c.Id == channelId);
                    if (channel != null)
                    {
                        channel.Status = newStatus;
                    }
                }
            }
        }

        /// <summary>
        /// 检查通道是否已注册
        /// </summary>
        /// <param name="channelId">通道ID</param>
        /// <returns>是否已注册</returns>
        public bool IsChannelRegistered(string channelId)
        {
            if (string.IsNullOrEmpty(channelId))
                return false;

            lock (_lock)
            {
                return _allChannels.Any(c => c.Id == channelId);
            }
        }

        /// <summary>
        /// 获取已注册设备数量
        /// </summary>
        /// <returns>设备数量</returns>
        public int GetDeviceCount()
        {
            lock (_lock)
            {
                return _registeredDevices.Count;
            }
        }

        /// <summary>
        /// 获取通道总数
        /// </summary>
        /// <returns>通道数量</returns>
        public int GetChannelCount()
        {
            lock (_lock)
            {
                return _allChannels.Count;
            }
        }
    }
}

