using System;
using System.Collections.ObjectModel;
using Prism.Mvvm;

namespace MeasureControl.Models.Channels
{
    /// <summary>
    /// 通道基类，定义所有通道的通用属性和方法
    /// </summary>
    public abstract class ChannelBase : BindableBase
    {
        private string _id;
        private string _name;
        private string _deviceId;
        private string _deviceName;
        private string _channelType;
        private string _status;
        private string _description;
        private bool _isPreviewEnabled;

        /// <summary>
        /// 通道唯一标识
        /// </summary>
        public string Id
        {
            get => _id;
            set => SetProperty(ref _id, value);
        }

        /// <summary>
        /// 通道名称（如：AI0, CAN1, DO5）
        /// </summary>
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        /// <summary>
        /// 所属设备ID
        /// </summary>
        public string DeviceId
        {
            get => _deviceId;
            set => SetProperty(ref _deviceId, value);
        }

        /// <summary>
        /// 所属设备名称
        /// </summary>
        public string DeviceName
        {
            get => _deviceName;
            set => SetProperty(ref _deviceName, value);
        }

        /// <summary>
        /// 通道类型（AI/AO/DI/DO/CAN/ARINC429等）
        /// </summary>
        public string ChannelType
        {
            get => _channelType;
            set => SetProperty(ref _channelType, value);
        }

        /// <summary>
        /// 状态（可用/已绑定/故障）
        /// </summary>
        public string Status
        {
            get => _status;
            set => SetProperty(ref _status, value);
        }

        /// <summary>
        /// 描述信息
        /// </summary>
        public string Description
        {
            get => _description;
            set => SetProperty(ref _description, value);
        }

        /// <summary>
        /// 是否启用实时预览（默认为false，避免大量通道时卡顿）
        /// </summary>
        public bool IsPreviewEnabled
        {
            get => _isPreviewEnabled;
            set => SetProperty(ref _isPreviewEnabled, value);
        }

        protected ChannelBase()
        {
            Id = Guid.NewGuid().ToString();
            Status = "可用";
            IsPreviewEnabled = false; // 默认不启用预览
        }

        /// <summary>
        /// 获取通道的完整显示名称
        /// </summary>
        /// <returns>格式：设备名/通道名</returns>
        public virtual string GetFullName()
        {
            return $"{DeviceName}/{Name}";
        }

        /// <summary>
        /// 验证通道配置是否正确
        /// </summary>
        /// <returns>验证结果</returns>
        public virtual bool ValidateConfiguration()
        {
            return !string.IsNullOrEmpty(Name) &&
                   !string.IsNullOrEmpty(DeviceId) &&
                   !string.IsNullOrEmpty(ChannelType);
        }
    }
}

