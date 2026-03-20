using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using MeasureControl.Models;
using MeasureControl.Models.Channels;
using MeasureControl.Models.Devices;
using MeasureControl.Drivers;
using System.Threading;
using System.Threading.Tasks;

namespace MeasureControl.Services
{
    /// <summary>
    /// 通道绑定服务实现
    /// </summary>
    public class ChannelBindingService : IChannelBindingService
    {
        private ObservableCollection<ChannelBase> _allChannels;
        private ObservableCollection<SignalVariable> _allSignals;
        private ObservableCollection<ChannelBinding> _allBindings;
        private readonly ChannelManager _channelManager;

        public ChannelBindingService(ChannelManager channelManager)
        {
            _channelManager = channelManager ?? throw new ArgumentNullException(nameof(channelManager));
            _allChannels = new ObservableCollection<ChannelBase>();
            _allSignals = new ObservableCollection<SignalVariable>();
            _allBindings = new ObservableCollection<ChannelBinding>();
        }

        public ObservableCollection<ChannelBase> GetDeviceChannels(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId))
                return new ObservableCollection<ChannelBase>();

            return new ObservableCollection<ChannelBase>(
                _allChannels.Where(c => c.DeviceId == deviceId));
        }

        public ObservableCollection<SignalVariable> GetAllSignals()
        {
            return _allSignals;
        }

        public ObservableCollection<SignalVariable> GetSignalsByGroup(string group)
        {
            if (string.IsNullOrEmpty(group))
                return new ObservableCollection<SignalVariable>();

            return new ObservableCollection<SignalVariable>(
                _allSignals.Where(s => s.Group == group));
        }

        public ChannelBinding CreateBinding(string signalId, string channelId)
        {
            if (string.IsNullOrEmpty(signalId) || string.IsNullOrEmpty(channelId))
                return null;

            // 验证绑定合法性
            if (!ValidateBinding(signalId, channelId))
                return null;

            // 检查是否已存在绑定
            if (IsSignalBound(signalId) || IsChannelBound(channelId))
                return null;

            var signal = _allSignals.FirstOrDefault(s => s.Id == signalId);
            var channel = _allChannels.FirstOrDefault(c => c.Id == channelId);

            if (signal == null || channel == null)
                return null;

            var binding = new ChannelBinding(signalId, signal.Name, channelId);
            _allBindings.Add(binding);

            // 更新通道状态
            channel.Status = "已绑定";

            return binding;
        }

        public bool RemoveBinding(string bindingId)
        {
            if (string.IsNullOrEmpty(bindingId))
                return false;

            var binding = _allBindings.FirstOrDefault(b => b.Id == bindingId);
            if (binding == null)
                return false;

            // 更新通道状态
            var channel = _allChannels.FirstOrDefault(c => c.Id == binding.ChannelId);
            if (channel != null)
            {
                channel.Status = "可用";
            }

            return _allBindings.Remove(binding);
        }

        public bool UpdateBinding(ChannelBinding binding)
        {
            if (binding == null || !binding.ValidateConfiguration())
                return false;

            var existingBinding = _allBindings.FirstOrDefault(b => b.Id == binding.Id);
            if (existingBinding == null)
                return false;

            existingBinding.SignalVariableId = binding.SignalVariableId;
            existingBinding.SignalVariableName = binding.SignalVariableName;
            existingBinding.ChannelId = binding.ChannelId;
            existingBinding.ChannelAddress = binding.ChannelAddress;
            existingBinding.Status = binding.Status;
            existingBinding.Notes = binding.Notes;
            existingBinding.UpdateModifiedTime();

            return true;
        }

        public ChannelBinding GetSignalBinding(string signalId)
        {
            if (string.IsNullOrEmpty(signalId))
                return null;

            return _allBindings.FirstOrDefault(b => b.SignalVariableId == signalId);
        }

        public ChannelBinding GetChannelBinding(string channelId)
        {
            if (string.IsNullOrEmpty(channelId))
                return null;

            return _allBindings.FirstOrDefault(b => b.ChannelId == channelId);
        }

        public ObservableCollection<ChannelBinding> GetAllBindings()
        {
            return _allBindings;
        }

        public bool ValidateBinding(string signalId, string channelId)
        {
            if (string.IsNullOrEmpty(signalId) || string.IsNullOrEmpty(channelId))
                return false;

            var signal = _allSignals.FirstOrDefault(s => s.Id == signalId);
            var channel = _allChannels.FirstOrDefault(c => c.Id == channelId);

            if (signal == null || channel == null)
                return false;

            // 验证信号类型与通道类型是否匹配
            return IsTypeCompatible(signal.SignalType, channel.ChannelType);
        }

        private bool IsTypeCompatible(string signalType, string channelType)
        {
            if (string.IsNullOrEmpty(signalType) || string.IsNullOrEmpty(channelType))
                return false;

            // 简单的类型匹配规则
            switch (signalType)
            {
                case "Analog":
                    return channelType == "AI" || channelType == "AO";
                case "Digital":
                    return channelType == "DI" || channelType == "DO";
                case "CAN":
                    return channelType == "CAN";
                case "ARINC429":
                    return channelType == "ARINC429";
                case "1553B":
                    return channelType == "1553B";
                case "1394B":
                    return channelType == "1394B";
                case "LVDT":
                    return channelType == "LVDT";
                default:
                    return false;
            }
        }

        public bool AddSignal(SignalVariable signal)
        {
            if (signal == null || !signal.ValidateConfiguration())
                return false;

            if (_allSignals.Any(s => s.Id == signal.Id || s.Name == signal.Name))
                return false;

            _allSignals.Add(signal);
            return true;
        }

        public bool RemoveSignal(string signalId)
        {
            if (string.IsNullOrEmpty(signalId))
                return false;

            // 先删除相关的绑定
            var bindings = _allBindings.Where(b => b.SignalVariableId == signalId).ToList();
            foreach (var binding in bindings)
            {
                RemoveBinding(binding.Id);
            }

            var signal = _allSignals.FirstOrDefault(s => s.Id == signalId);
            if (signal == null)
                return false;

            return _allSignals.Remove(signal);
        }

        public bool UpdateSignal(SignalVariable signal)
        {
            if (signal == null || !signal.ValidateConfiguration())
                return false;

            var existingSignal = _allSignals.FirstOrDefault(s => s.Id == signal.Id);
            if (existingSignal == null)
                return false;

            existingSignal.Name = signal.Name;
            existingSignal.Description = signal.Description;
            existingSignal.SignalType = signal.SignalType;
            existingSignal.DataType = signal.DataType;
            existingSignal.Unit = signal.Unit;
            existingSignal.MinValue = signal.MinValue;
            existingSignal.MaxValue = signal.MaxValue;
            existingSignal.Group = signal.Group;
            existingSignal.ConversionFormula = signal.ConversionFormula;
            existingSignal.Scale = signal.Scale;
            existingSignal.Offset = signal.Offset;
            existingSignal.MessageId = signal.MessageId;
            existingSignal.ByteOffset = signal.ByteOffset;
            existingSignal.BitOffset = signal.BitOffset;
            existingSignal.BitLength = signal.BitLength;
            existingSignal.Endianness = signal.Endianness;
            existingSignal.Direction = signal.Direction;

            return true;
        }

        public ObservableCollection<ChannelBase> GetAvailableChannels(string deviceId)
        {
            var deviceChannels = GetDeviceChannels(deviceId);
            return new ObservableCollection<ChannelBase>(
                deviceChannels.Where(c => c.Status == "可用"));
        }

        public ObservableCollection<SignalVariable> GetSignalsByChannelType(string channelType)
        {
            if (string.IsNullOrEmpty(channelType))
                return new ObservableCollection<SignalVariable>();

            return new ObservableCollection<SignalVariable>(
                _allSignals.Where(s => IsTypeCompatible(s.SignalType, channelType)));
        }

        public bool IsSignalBound(string signalId)
        {
            return _allBindings.Any(b => b.SignalVariableId == signalId && b.Status == "Active");
        }

        public bool IsChannelBound(string channelId)
        {
            return _allBindings.Any(b => b.ChannelId == channelId && b.Status == "Active");
        }

        public void ClearAllBindings()
        {
            // 更新所有通道状态
            foreach (var binding in _allBindings)
            {
                var channel = _allChannels.FirstOrDefault(c => c.Id == binding.ChannelId);
                if (channel != null)
                {
                    channel.Status = "可用";
                }
            }

            _allBindings.Clear();
        }

        public bool LoadBindings(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                    return false;

                var json = File.ReadAllText(filePath);
                var data = JsonConvert.DeserializeObject<BindingConfigData>(json);

                if (data == null)
                    return false;

                // 清除现有数据
                ClearAllBindings();
                _allSignals.Clear();

                // 加载信号变量
                foreach (var signal in data.Signals)
                {
                    _allSignals.Add(signal);
                }

                // 加载绑定
                foreach (var binding in data.Bindings)
                {
                    _allBindings.Add(binding);
                    
                    // 更新通道状态
                    var channel = _allChannels.FirstOrDefault(c => c.Id == binding.ChannelId);
                    if (channel != null)
                    {
                        channel.Status = "已绑定";
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool SaveBindings(string filePath)
        {
            try
            {
                var data = new BindingConfigData
                {
                    Signals = _allSignals.ToList(),
                    Bindings = _allBindings.ToList()
                };

                var json = JsonConvert.SerializeObject(data, Formatting.Indented);
                File.WriteAllText(filePath, json);

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 注册通道（供设备管理器调用）
        /// </summary>
        public void RegisterChannel(ChannelBase channel)
        {
            if (channel != null && !_allChannels.Any(c => c.Id == channel.Id))
            {
                _allChannels.Add(channel);
            }
        }

        /// <summary>
        /// 注销通道
        /// </summary>
        public void UnregisterChannel(string channelId)
        {
            var channel = _allChannels.FirstOrDefault(c => c.Id == channelId);
            if (channel != null)
            {
                // 删除相关绑定
                var bindings = _allBindings.Where(b => b.ChannelId == channelId).ToList();
                foreach (var binding in bindings)
                {
                    RemoveBinding(binding.Id);
                }

                _allChannels.Remove(channel);
            }
        }

        /// <summary>
        /// 从ChannelManager同步通道数据
        /// </summary>
        public void SyncChannelsFromManager()
        {
            // 清空现有通道
            _allChannels.Clear();

            // 从ChannelManager获取所有通道
            var channels = _channelManager.GetAllChannels();
            foreach (var channel in channels)
            {
                _allChannels.Add(channel);
            }
        }

        /// <summary>
        /// 刷新可用通道列表
        /// </summary>
        public void RefreshAvailableChannels()
        {
            SyncChannelsFromManager();
        }

        /// <summary>
        /// 清空所有数据（项目关闭时调用）
        /// </summary>
        public void ClearAll()
        {
            _allChannels.Clear();
            _allSignals.Clear();
            _allBindings.Clear();
        }

        public async Task<bool> WriteAsync(string deviceId, string channelId, object value, CancellationToken ct)
        {
            try
            {
                if (string.IsNullOrEmpty(deviceId) || string.IsNullOrEmpty(channelId))
                    return false;

                // 定位通道，确认其属于该设备
                var channel = _allChannels.FirstOrDefault(c => c.Id == channelId);
                if (channel == null || !string.Equals(channel.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase))
                    return false;

                // 将值收敛为 double（数字/布尔）
                double dv = ConvertToDouble(value);

                // 获取或复用驱动
                var driver = DriverFactory.GetCachedDriver(deviceId);
                if (driver == null)
                {
                    // 最小实现：找不到驱动则失败（后续可从 ChannelManager/设备注册表创建）
                    return false;
                }

                // 若未连接，尝试连接（带简单超时）
                if (!driver.IsConnected)
                {
                    var connectTask = driver.ConnectAsync();
                    var completed = await Task.WhenAny(connectTask, Task.Delay(2000, ct)).ConfigureAwait(false);
                    if (completed != connectTask || !await connectTask.ConfigureAwait(false))
                        return false;
                }

                // 写入（带简单超时）
                var writeTask = driver.WriteChannelAsync(channelId, dv);
                var finished = await Task.WhenAny(writeTask, Task.Delay(2000, ct)).ConfigureAwait(false);
                if (finished != writeTask)
                    return false;

                return await writeTask.ConfigureAwait(false);
            }
            catch
            {
                return false;
            }
        }

        private static double ConvertToDouble(object value)
        {
            if (value == null) return 0d;
            if (value is double dd) return dd;
            if (value is float f) return f;
            if (value is int i) return i;
            if (value is long l) return l;
            if (value is bool b) return b ? 1d : 0d;
            if (value is string s && double.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var rs))
                return rs;
            return System.Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// 绑定配置数据结构（用于序列化）
        /// </summary>
        private class BindingConfigData
        {
            public List<SignalVariable> Signals { get; set; }
            public List<ChannelBinding> Bindings { get; set; }
        }
    }
}

