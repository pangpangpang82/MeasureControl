using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using MeasureControl.Constants;
using MeasureControl.Helpers;
using MeasureControl.Models;
using MeasureControl.Models.Channels;
using Prism.Mvvm;

namespace MeasureControl.Models.Devices
{
    /// <summary>
    /// 设备功能类型枚举
    /// </summary>
    public enum DeviceCapability
    {
        /// <summary>
        /// 输入设备：主要用于数据采集和读取
        /// </summary>
        Input,

        /// <summary>
        /// 输出设备：主要用于数据输出和控制
        /// </summary>
        Output,

        /// <summary>
        /// 双向设备：既可以输入也可以输出
        /// </summary>
        Bidirectional,

        /// <summary>
        /// 通信设备：用于数据通信（如CAN、ARINC429等）
        /// </summary>
        Communication,

        /// <summary>
        /// 其他设备：特殊功能设备
        /// </summary>
        Other
    }

    /// <summary>
    /// 设备基类，定义所有设备的通用属性和方法
    /// </summary>
    public abstract class DeviceBase : BindableBase
    {
        private string _name;
        private string _displayName;
        private string _cardName;
        private string _manufacturer;
        private string _model;
        private string _slotPosition;
        private string _status;
        private string _description;
        private string _deviceType;
        private string _id;
        private bool _isSelected;
        private bool _isExpanded;
        private string _connectionMethod;
        private string _parentNode;
        private string _details;
        private ObservableCollection<DeviceBase> _children;
        private ObservableCollection<ChannelBase> _channels;
        private List<ChannelCalibrationRecord> _calibrationRecords;
        private List<ChannelConfig> _channelConfigs;
        private CardConfigDataBase _cardConfigData;

        /// <summary>
        /// 设备名称
        /// </summary>
        public string Name
        {
            get => _name;
            set
            {
                if (SetProperty(ref _name, value))
                {
                    RaisePropertyChanged(nameof(PrimaryDisplayName));
                }
            }
        }

        public string DisplayName
        {
            get => _displayName;
            set
            {
                if (SetProperty(ref _displayName, value))
                {
                    RaisePropertyChanged(nameof(PrimaryDisplayName));
                }
            }
        }

        /// <summary>
        /// 制造商
        /// </summary>
        public string Manufacturer
        {
            get => _manufacturer;
            set => SetProperty(ref _manufacturer, value);
        }

        /// <summary>
        /// 型号
        /// </summary>
        public string Model
        {
            get => _model;
            set => SetProperty(ref _model, value);
        }

        /// <summary>
        /// 插槽位置
        /// </summary>
        public string SlotPosition
        {
            get => _slotPosition;
            set => SetProperty(ref _slotPosition, value);
        }

        /// <summary>
        /// 状态
        /// </summary>
        public string Status
        {
            get => _status;
            set => SetProperty(ref _status, value);
        }

        /// <summary>
        /// 描述
        /// </summary>
        public string Description
        {
            get => _description;
            set => SetProperty(ref _description, value);
        }

        /// <summary>
        /// 设备类型（Card、Chassis等）
        /// </summary>
        public string DeviceType
        {
            get => _deviceType;
            set
            {
                if (SetProperty(ref _deviceType, value))
                {
                    RaisePropertyChanged(nameof(PrimaryDisplayName));
                }
            }
        }

        /// <summary>
        /// 设备唯一标识
        /// </summary>
        public string Id
        {
            get => _id;
            set => SetProperty(ref _id, value);
        }

        /// <summary>
        /// 是否选中
        /// </summary>
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        /// <summary>
        /// 是否展开
        /// </summary>
        public bool IsExpanded
        {
            get => _isExpanded;
            set => SetProperty(ref _isExpanded, value);
        }

        /// <summary>
        /// 子设备集合
        /// </summary>
        public ObservableCollection<DeviceBase> Children
        {
            get => _children;
            set => SetProperty(ref _children, value);
        }

        /// <summary>
        /// 连接方式
        /// </summary>
        public string ConnectionMethod
        {
            get => _connectionMethod;
            set => SetProperty(ref _connectionMethod, value);
        }

        /// <summary>
        /// 父节点名称
        /// </summary>
        public string ParentNode
        {
            get => _parentNode;
            set
            {
                if (SetProperty(ref _parentNode, value))
                {
                    RaisePropertyChanged(nameof(PrimaryDisplayName));
                }
            }
        }

        /// <summary>
        /// 详细信息
        /// </summary>
        public string Details
        {
            get => _details;
            set => SetProperty(ref _details, value);
        }

        ///// <summary>
        ///// 设备规格（详细技术参数）
        ///// </summary>
        //public DeviceSpecification Specifications
        //{
        //    get => _specifications;
        //    set => SetProperty(ref _specifications, value);
        //}

        /// <summary>
        /// 设备手册的外部链接（可为空），用于在UI中通过系统默认浏览器打开
        /// </summary>
        public string ManualUrl { get; set; }

        /// <summary>
        /// 设备通道集合（所有物理通道）
        /// </summary>
        public ObservableCollection<ChannelBase> Channels
        {
            get => _channels;
            set => SetProperty(ref _channels, value);
        }

        /// <summary>
        /// 板卡名称（用于PXI板卡的自定义命名，如"板卡1"、"板卡2"等）
        /// </summary>
        public string CardName
        {
            get => _cardName;
            set
            {
                if (SetProperty(ref _cardName, value))
                {
                    RaisePropertyChanged(nameof(PrimaryDisplayName));
                }
            }
        }

        /// <summary>
        /// 校准记录列表
        /// </summary>
        public List<ChannelCalibrationRecord> CalibrationRecords
        {
            get => _calibrationRecords;
            set => SetProperty(ref _calibrationRecords, value);
        }

        /// <summary>
        /// 通道配置列表（使能状态、量程）
        /// </summary>
        public List<ChannelConfig> ChannelConfigs
        {
            get => _channelConfigs;
            set => SetProperty(ref _channelConfigs, value);
        }

        /// <summary>
        /// 板卡配置数据（包含板卡名称、通道使能状态等，用于持久化）
        /// </summary>
        public CardConfigDataBase CardConfigData
        {
            get => _cardConfigData;
            set => SetProperty(ref _cardConfigData, value);
        }

        /// <summary>
        /// 设备类型名称（如"模拟量采集"、"数字万用表"等）
        /// </summary>
        public abstract string DeviceTypeName { get; }

        public string PrimaryDisplayName
        {
            get
            {
                if (DeviceType == "Chassis")
                {
                    return DeviceTypeName;
                }

                if (DeviceType == "Card")
                {
                    if (!string.IsNullOrWhiteSpace(CardName)) return CardName;
                    if (!string.IsNullOrWhiteSpace(ParentNode)) return ParentNode;
                    return Name;
                }

                if (!string.IsNullOrWhiteSpace(DisplayName)) return DisplayName;
                if (!string.IsNullOrWhiteSpace(ParentNode)) return ParentNode;
                return Name;
            }
        }

        /// <summary>
        /// 设备功能类型（输入/输出/双向/通信/其他）
        /// </summary>
        public virtual DeviceCapability Capability { get; protected set; } = DeviceCapability.Other;

        protected DeviceBase()
        {
            Id = Guid.NewGuid().ToString();
            Status = Constants.DeviceConstants.Status.Normal;
            Children = new ObservableCollection<DeviceBase>();
            Channels = new ObservableCollection<ChannelBase>();
            ConnectionMethod = Constants.DeviceConstants.Default.Empty;
            //Specifications = new DeviceSpecification();
            CardName = string.Empty;
            CalibrationRecords = new List<ChannelCalibrationRecord>();
            ChannelConfigs = new List<ChannelConfig>();
        }

        protected DeviceBase(string name, string manufacturer, string model, string slotPosition) : this()
        {
            Name = name;
            Manufacturer = manufacturer;
            Model = model;
            SlotPosition = slotPosition;
        }

        /// <summary>
        /// 获取设备信息项列表，用于在DeviceInfo区域显示
        /// </summary>
        /// <returns>设备信息项列表</returns>
        public abstract ObservableCollection<DeviceInfoItem> GetDeviceInfoItems();

        /// <summary>
        /// 初始化设备的子节点
        /// </summary>
        public abstract void InitializeChildren();

        /// <summary>
        /// 初始化设备的通道集合
        /// 子类应重写此方法，根据设备类型创建对应的通道
        /// </summary>
        public virtual void InitializeChannels()
        {
            // 默认实现：清空通道集合
            // 子类可以重写此方法来创建具体的通道
            Channels.Clear();
        }

        /// <summary>
        /// 根据通道名称获取通道
        /// </summary>
        /// <param name="channelName">通道名称（如：AI0, CAN1）</param>
        /// <returns>找到的通道，未找到返回null</returns>
        public virtual ChannelBase GetChannel(string channelName)
        {
            if (string.IsNullOrEmpty(channelName))
                return null;

            return Channels.FirstOrDefault(c => c.Name == channelName);
        }

        /// <summary>
        /// 获取用于“设备详细信息”区域的动态字段集合（Label/Value/Format）。
        /// </summary>
        /// <returns>显示字段集合</returns>
        public virtual ObservableCollection<DeviceDisplayField> GetDisplayFields()
        {
            DeviceDisplayFieldRegistry.TryGetDisplayFields(this, out var fields);
            return fields ?? new ObservableCollection<DeviceDisplayField>();
        }

        /// <summary>
        /// 验证设备配置是否正确
        /// </summary>
        /// <returns>验证结果</returns>
        public virtual bool ValidateConfiguration()
        {
            return !string.IsNullOrEmpty(Name) &&
                   !string.IsNullOrEmpty(Model) &&
                   !string.IsNullOrEmpty(SlotPosition);
        }

        /// <summary>
        /// 获取设备连接字符串（用于实际通信）
        /// </summary>
        /// <returns>连接字符串</returns>
        public virtual string GetConnectionString()
        {
            return $"{Manufacturer}::{Model}::{SlotPosition}";
        }

        ///// <summary>
        ///// 复制设备属性到当前设备
        ///// </summary>
        ///// <param name="sourceDevice">源设备</param>
        //public virtual void CopyFrom(DeviceBase sourceDevice)
        //{
        //    if (sourceDevice == null) return;

        //    Name = sourceDevice.Name;
        //    Manufacturer = sourceDevice.Manufacturer;
        //    Model = sourceDevice.Model;
        //    SlotPosition = sourceDevice.SlotPosition;
        //    Status = sourceDevice.Status;
        //    Description = sourceDevice.Description;
        //    DeviceType = sourceDevice.DeviceType;
        //    Id = sourceDevice.Id;
        //    IsSelected = sourceDevice.IsSelected;
        //    IsExpanded = sourceDevice.IsExpanded;
        //    ConnectionMethod = sourceDevice.ConnectionMethod;
        //    ParentNode = sourceDevice.ParentNode;
        //    Details = sourceDevice.Details;
        //    CardName = sourceDevice.CardName;
        //    if (sourceDevice.CalibrationRecords != null)
        //    {
        //        CalibrationRecords = new List<ChannelCalibrationRecord>(sourceDevice.CalibrationRecords);
        //    }
        //    if (sourceDevice.ChannelConfigs != null)
        //    {
        //        ChannelConfigs = new List<ChannelConfig>(sourceDevice.ChannelConfigs);
        //    }
        //    CardConfigData = sourceDevice.CardConfigData;
        //}

        /// <summary>
        /// 获取设备的完整类型信息（用于序列化）
        /// </summary>
        /// <returns>类型全名</returns>
        public string GetFullTypeName()
        {
            return GetType().FullName;
        }

        #region 共用工具方法

        /// <summary>
        /// 解析设备名称，提取制造商和型号
        /// </summary>
        /// <param name="deviceName">设备名称（格式：制造商 型号）</param>
        protected void ParseDeviceName(string deviceName)
        {
            if (string.IsNullOrEmpty(deviceName))
            {
                Name = "N/A";
                Manufacturer = "N/A";
                Model = "N/A";
                return;
            }

            var parts = deviceName.Split(' ');
            if (parts.Length >= 2)
            {
                Manufacturer = parts[0];
                Model = string.Join(" ", parts.Skip(1));
                Name = deviceName;
            }
            else
            {
                Name = deviceName;
                Manufacturer = "N/A";
                Model = "N/A";
            }
        }

        /// <summary>
        /// 格式化数据速率字符串（采样率/更新率）
        /// </summary>
        /// <param name="rate">速率值（单位：S/s）</param>
        /// <returns>格式化后的字符串（如：10kS/s, 1MS/s）</returns>
        protected string FormatDataRateString(int rate)
        {
            if (rate <= 0)
                return "N/A";

            if (rate >= 1_000_000_000)
                return $"{rate / 1_000_000_000.0:F2}{DeviceConstants.DataRateUnit.GigaSamplesPerSecond}";
            else if (rate >= 1_000_000)
                return $"{rate / 1_000_000.0:F2}{DeviceConstants.DataRateUnit.MegaSamplesPerSecond}";
            else if (rate >= 1_000)
                return $"{rate / 1_000.0:F2}{DeviceConstants.DataRateUnit.KiloSamplesPerSecond}";
            else
                return $"{rate}{DeviceConstants.DataRateUnit.SamplesPerSecond}";
        }

        /// <summary>
        /// 从多段式字符串中提取指定标签的值
        /// 例如：从 "33V (CH1/CH2), 16V (CH3)" 中提取 "CH3" 对应的 "16V"
        /// </summary>
        /// <param name="sourceString">源字符串</param>
        /// <param name="label">标签（如 "CH3"）</param>
        /// <returns>提取的值，未找到返回N/A</returns>
        protected string ExtractLabeledValue(string sourceString, string label)
        {
            if (string.IsNullOrEmpty(sourceString) || string.IsNullOrEmpty(label))
                return Constants.DeviceConstants.Default.NA;

            // 按逗号分割各个部分
            var parts = sourceString.Split(',');
            foreach (var part in parts)
            {
                var trimmedPart = part.Trim();
                // 检查是否包含目标标签
                if (trimmedPart.Contains(label))
                {
                    // 提取值（在括号前面）
                    var bracketIndex = trimmedPart.IndexOf('(');
                    if (bracketIndex > 0)
                    {
                        return trimmedPart.Substring(0, bracketIndex).Trim();
                    }
                    return trimmedPart;
                }
            }

            return Constants.DeviceConstants.Default.NA;
        }

        #endregion
    }
}
