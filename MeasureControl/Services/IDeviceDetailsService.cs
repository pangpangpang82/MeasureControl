using System.Collections.Generic;
using MeasureControl.Models.Devices;

namespace MeasureControl.Services
{
    /// <summary>
    /// 设备详情服务接口 - 统一处理设备详情显示逻辑
    /// </summary>
    public interface IDeviceDetailsService
    {
        /// <summary>
        /// 获取设备详情字典
        /// </summary>
        /// <param name="device">设备对象</param>
        /// <returns>键值对字典，键为字段名，值为字段值</returns>
        Dictionary<string, string> GetDeviceDetails(DeviceBase device);

        /// <summary>
        /// 格式化设备信息标题
        /// </summary>
        /// <param name="device">设备对象</param>
        /// <returns>格式化后的标题</returns>
        string FormatDeviceTitle(DeviceBase device);

        /// <summary>
        /// 获取设备详情字段数组（用于绑定到 UI）
        /// </summary>
        /// <param name="device">设备对象</param>
        /// <param name="maxFields">最大字段数量（默认6个）</param>
        /// <returns>字段值数组</returns>
        string[] GetDeviceFieldsArray(DeviceBase device, int maxFields = 6);
    }
}

