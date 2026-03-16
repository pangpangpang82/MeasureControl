using System.Collections.ObjectModel;
using MeasureControl.Constants;
using MeasureControl.Models.Devices;

namespace MeasureControl.Models.Devices.DeviceCategories
{
    /// <summary>
    /// 设备子节点基类
    /// 用于统一管理所有设备的子节点（通道组、功能模块等）
    /// </summary>
    public abstract class SubNodeBase : DeviceBase
    {
        public SubNodeBase()
        {
            DeviceType = DeviceConstants.Type.SubNode;
            Status = DeviceConstants.Status.Normal;
            SlotPosition = DeviceConstants.Default.NA;
        }

        public SubNodeBase(string name, string parentNode) : this()
        {
            Name = name;
            ParentNode = parentNode;
        }

        /// <summary>
        /// 子节点默认不包含子节点，子类如需要可重写
        /// </summary>
        public override void InitializeChildren()
        {
            Children.Clear();
        }

        /// <summary>
        /// 子节点默认返回简单的设备信息项，子类如需要可重写
        /// </summary>
        public override ObservableCollection<DeviceInfoItem> GetDeviceInfoItems()
        {
            var items = new ObservableCollection<DeviceInfoItem>();
            items.Add(new DeviceInfoItem(
                DeviceTypeName, 
                Model, 
                SlotPosition, 
                Status, 
                true, 
                DeviceConstants.Type.Instrument));
            return items;
        }

        /// <summary>
        /// 默认连接字符串格式，子类可重写以提供更具体的信息
        /// </summary>
        public override string GetConnectionString()
        {
            return $"{DeviceTypeName}::{Name}::{Model}";
        }
    }
}

