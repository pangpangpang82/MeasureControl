using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using MeasureControl.Models;
using MeasureControl.Models.Channels;

namespace MeasureControl.Services
{
    /// <summary>
    /// 通道绑定服务接口
    /// </summary>
    public interface IChannelBindingService
    {
        /// <summary>
        /// 获取设备的所有通道
        /// </summary>
        /// <param name="deviceId">设备ID</param>
        /// <returns>通道列表</returns>
        ObservableCollection<ChannelBase> GetDeviceChannels(string deviceId);

        /// <summary>
        /// 获取所有信号变量
        /// </summary>
        /// <returns>信号变量列表</returns>
        ObservableCollection<SignalVariable> GetAllSignals();

        /// <summary>
        /// 获取指定分组的信号变量
        /// </summary>
        /// <param name="group">分组名称</param>
        /// <returns>信号变量列表</returns>
        ObservableCollection<SignalVariable> GetSignalsByGroup(string group);

        /// <summary>
        /// 创建绑定
        /// </summary>
        /// <param name="signalId">信号变量ID</param>
        /// <param name="channelId">通道ID</param>
        /// <returns>绑定对象</returns>
        ChannelBinding CreateBinding(string signalId, string channelId);

        /// <summary>
        /// 删除绑定
        /// </summary>
        /// <param name="bindingId">绑定ID</param>
        /// <returns>是否成功</returns>
        bool RemoveBinding(string bindingId);

        /// <summary>
        /// 更新绑定
        /// </summary>
        /// <param name="binding">绑定对象</param>
        /// <returns>是否成功</returns>
        bool UpdateBinding(ChannelBinding binding);

        /// <summary>
        /// 获取信号的绑定
        /// </summary>
        /// <param name="signalId">信号ID</param>
        /// <returns>绑定对象</returns>
        ChannelBinding GetSignalBinding(string signalId);

        /// <summary>
        /// 获取通道的绑定
        /// </summary>
        /// <param name="channelId">通道ID</param>
        /// <returns>绑定对象</returns>
        ChannelBinding GetChannelBinding(string channelId);

        /// <summary>
        /// 获取所有绑定
        /// </summary>
        /// <returns>绑定列表</returns>
        ObservableCollection<ChannelBinding> GetAllBindings();

        /// <summary>
        /// 验证绑定合法性
        /// </summary>
        /// <param name="signalId">信号ID</param>
        /// <param name="channelId">通道ID</param>
        /// <returns>是否合法</returns>
        bool ValidateBinding(string signalId, string channelId);

        /// <summary>
        /// 添加信号变量
        /// </summary>
        /// <param name="signal">信号变量</param>
        /// <returns>是否成功</returns>
        bool AddSignal(SignalVariable signal);

        /// <summary>
        /// 删除信号变量
        /// </summary>
        /// <param name="signalId">信号ID</param>
        /// <returns>是否成功</returns>
        bool RemoveSignal(string signalId);

        /// <summary>
        /// 更新信号变量
        /// </summary>
        /// <param name="signal">信号变量</param>
        /// <returns>是否成功</returns>
        bool UpdateSignal(SignalVariable signal);

        /// <summary>
        /// 获取可用通道（未绑定的通道）
        /// </summary>
        /// <param name="deviceId">设备ID</param>
        /// <returns>可用通道列表</returns>
        ObservableCollection<ChannelBase> GetAvailableChannels(string deviceId);

        /// <summary>
        /// 根据通道类型获取信号变量
        /// </summary>
        /// <param name="channelType">通道类型</param>
        /// <returns>匹配的信号变量列表</returns>
        ObservableCollection<SignalVariable> GetSignalsByChannelType(string channelType);

        /// <summary>
        /// 检查信号是否已绑定
        /// </summary>
        /// <param name="signalId">信号ID</param>
        /// <returns>是否已绑定</returns>
        bool IsSignalBound(string signalId);

        /// <summary>
        /// 检查通道是否已绑定
        /// </summary>
        /// <param name="channelId">通道ID</param>
        /// <returns>是否已绑定</returns>
        bool IsChannelBound(string channelId);

        /// <summary>
        /// 清除所有绑定
        /// </summary>
        void ClearAllBindings();

        /// <summary>
        /// 加载绑定配置
        /// </summary>
        /// <param name="filePath">配置文件路径</param>
        /// <returns>是否成功</returns>
        bool LoadBindings(string filePath);

        /// <summary>
        /// 保存绑定配置
        /// </summary>
        /// <param name="filePath">配置文件路径</param>
        /// <returns>是否成功</returns>
        bool SaveBindings(string filePath);

        /// <summary>
        /// 清空所有数据（项目关闭时调用）
        /// </summary>
        void ClearAll();

        /// <summary>
        /// 写入绑定通道（用于非通讯变量下发）；最小实现：值会转换为double（bool=>0/1）
        /// </summary>
        /// <param name="deviceId">设备ID</param>
        /// <param name="channelId">通道ID</param>
        /// <param name="value">要写入的值</param>
        /// <param name="ct">取消令牌</param>
        /// <returns>是否成功</returns>
        Task<bool> WriteAsync(string deviceId, string channelId, object value, CancellationToken ct);
    }
}

