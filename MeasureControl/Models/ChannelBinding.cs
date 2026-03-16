using System;
using System.Collections.Generic;
using Prism.Mvvm;

namespace MeasureControl.Models
{
    /// <summary>
    /// 通道绑定（信号变量 → 物理通道映射）
    /// </summary>
    public class ChannelBinding : BindableBase
    {
        private string _id;
        private string _signalVariableId;
        private string _signalVariableName;
        private string _channelId;
        private string _channelAddress;
        private DateTime _createTime;
        private DateTime? _lastModifiedTime;
        private string _status;
        private Dictionary<string, object> _extendedConfig;
        private string _notes;

        /// <summary>
        /// 绑定唯一标识
        /// </summary>
        public string Id
        {
            get => _id;
            set => SetProperty(ref _id, value);
        }

        /// <summary>
        /// 信号变量ID
        /// </summary>
        public string SignalVariableId
        {
            get => _signalVariableId;
            set => SetProperty(ref _signalVariableId, value);
        }

        /// <summary>
        /// 信号变量名称（冗余字段，便于显示）
        /// </summary>
        public string SignalVariableName
        {
            get => _signalVariableName;
            set => SetProperty(ref _signalVariableName, value);
        }

        /// <summary>
        /// 物理通道ID
        /// </summary>
        public string ChannelId
        {
            get => _channelId;
            set => SetProperty(ref _channelId, value);
        }

        /// <summary>
        /// 通道地址（完整路径，如：PXI1::3::AI0）
        /// </summary>
        public string ChannelAddress
        {
            get => _channelAddress;
            set => SetProperty(ref _channelAddress, value);
        }

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreateTime
        {
            get => _createTime;
            set => SetProperty(ref _createTime, value);
        }

        /// <summary>
        /// 最后修改时间
        /// </summary>
        public DateTime? LastModifiedTime
        {
            get => _lastModifiedTime;
            set => SetProperty(ref _lastModifiedTime, value);
        }

        /// <summary>
        /// 状态（Active/Inactive/Error）
        /// </summary>
        public string Status
        {
            get => _status;
            set => SetProperty(ref _status, value);
        }

        /// <summary>
        /// 扩展配置（用于存储特殊参数）
        /// 例如：滤波器设置、触发条件、特殊换算参数等
        /// </summary>
        public Dictionary<string, object> ExtendedConfig
        {
            get => _extendedConfig;
            set => SetProperty(ref _extendedConfig, value);
        }

        /// <summary>
        /// 备注说明
        /// </summary>
        public string Notes
        {
            get => _notes;
            set => SetProperty(ref _notes, value);
        }

        public ChannelBinding()
        {
            Id = Guid.NewGuid().ToString();
            CreateTime = DateTime.Now;
            Status = "Active";
            ExtendedConfig = new Dictionary<string, object>();
        }

        public ChannelBinding(string signalVariableId, string signalVariableName, string channelId)
            : this()
        {
            SignalVariableId = signalVariableId;
            SignalVariableName = signalVariableName;
            ChannelId = channelId;
        }

        /// <summary>
        /// 验证绑定配置是否正确
        /// </summary>
        public bool ValidateConfiguration()
        {
            return !string.IsNullOrEmpty(SignalVariableId) &&
                   !string.IsNullOrEmpty(ChannelId) &&
                   !string.IsNullOrEmpty(ChannelAddress);
        }

        /// <summary>
        /// 更新最后修改时间
        /// </summary>
        public void UpdateModifiedTime()
        {
            LastModifiedTime = DateTime.Now;
        }

        /// <summary>
        /// 添加扩展配置项
        /// </summary>
        public void AddExtendedConfig(string key, object value)
        {
            if (ExtendedConfig == null)
            {
                ExtendedConfig = new Dictionary<string, object>();
            }
            ExtendedConfig[key] = value;
            UpdateModifiedTime();
        }

        /// <summary>
        /// 获取扩展配置项
        /// </summary>
        public T GetExtendedConfig<T>(string key, T defaultValue = default)
        {
            if (ExtendedConfig != null && ExtendedConfig.ContainsKey(key))
            {
                try
                {
                    return (T)ExtendedConfig[key];
                }
                catch
                {
                    return defaultValue;
                }
            }
            return defaultValue;
        }

        /// <summary>
        /// 获取绑定的完整描述
        /// </summary>
        public string GetFullDescription()
        {
            return $"{SignalVariableName} → {ChannelAddress} ({Status})";
        }
    }
}

