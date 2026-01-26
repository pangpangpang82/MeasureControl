using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MeasureControl.Models.Devices;

namespace MeasureControl.Drivers
{
    /// <summary>
    /// 采集状态改变事件参数
    /// </summary>
    public class AcquisitionStatusChangedEventArgs : EventArgs
    {
        /// <summary>
        /// 是否正在采集
        /// </summary>
        public bool IsRunning { get; set; }

        /// <summary>
        /// 采集模式
        /// </summary>
        public string AcquisitionMode { get; set; }
    }

    /// <summary>
    /// 设备驱动接口
    /// 定义所有设备驱动必须实现的标准操作
    /// </summary>
    public interface IDeviceDriver
    {
        /// <summary>
        /// 采集状态改变事件
        /// 当采集状态发生变化时触发
        /// </summary>
        event EventHandler<AcquisitionStatusChangedEventArgs> AcquisitionStatusChanged;

        /// <summary>
        /// 设备ID
        /// </summary>
        string DeviceId { get; }

        /// <summary>
        /// 设备名称
        /// </summary>
        string DeviceName { get; }

        /// <summary>
        /// 驱动是否已连接
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// 是否为模拟驱动
        /// </summary>
        bool IsSimulated { get; }

        /// <summary>
        /// 设备功能类型（输入/输出/双向/通信/其他）
        /// </summary>
        DeviceCapability Capability { get; }

        /// <summary>
        /// 连接设备
        /// </summary>
        /// <returns>连接是否成功</returns>
        Task<bool> ConnectAsync();

        /// <summary>
        /// 断开设备连接
        /// </summary>
        /// <returns>断开是否成功</returns>
        Task<bool> DisconnectAsync();

        /// <summary>
        /// 读取通道数据
        /// </summary>
        /// <param name="channelId">通道ID</param>
        /// <returns>读取的数据值</returns>
        Task<double> ReadChannelAsync(string channelId);

        /// <summary>
        /// 批量读取多个通道数据
        /// </summary>
        /// <param name="channelIds">通道ID列表</param>
        /// <returns>通道ID与数据值的字典</returns>
        Task<Dictionary<string, double>> ReadChannelsBatchAsync(IEnumerable<string> channelIds);

        /// <summary>
        /// 写入通道数据
        /// </summary>
        /// <param name="channelId">通道ID</param>
        /// <param name="value">要写入的数据值</param>
        /// <returns>写入是否成功</returns>
        Task<bool> WriteChannelAsync(string channelId, double value);

        /// <summary>
        /// 批量写入多个通道数据
        /// </summary>
        /// <param name="channelValues">通道ID与数据值的字典</param>
        /// <returns>写入是否成功</returns>
        Task<bool> WriteChannelsBatchAsync(Dictionary<string, double> channelValues);

        /// <summary>
        /// 配置通道参数
        /// </summary>
        /// <param name="channelId">通道ID</param>
        /// <param name="config">配置参数字典</param>
        /// <returns>配置是否成功</returns>
        Task<bool> ConfigureChannelAsync(string channelId, Dictionary<string, object> config);

        /// <summary>
        /// 启动数据采集
        /// </summary>
        /// <returns>启动是否成功</returns>
        Task<bool> StartAcquisitionAsync();

        /// <summary>
        /// 停止数据采集
        /// </summary>
        /// <returns>停止是否成功</returns>
        Task<bool> StopAcquisitionAsync();

        /// <summary>
        /// 获取设备状态
        /// </summary>
        /// <returns>状态信息字典</returns>
        Task<Dictionary<string, object>> GetStatusAsync();

        /// <summary>
        /// 重置设备
        /// </summary>
        /// <returns>重置是否成功</returns>
        Task<bool> ResetAsync();

        /// <summary>
        /// 执行自检
        /// </summary>
        /// <returns>自检结果</returns>
        Task<bool> SelfTestAsync();


    }
}

