using System;
using System.Collections.ObjectModel;
using MeasureControl.Models.Channels;

namespace MeasureControl.Helpers
{
    /// <summary>
    /// 通道工厂类，用于创建各种类型设备的标准通道
    /// </summary>
    public static class ChannelFactory
    {
        /// <summary>通用通道创建方法</summary>
        private static ObservableCollection<ChannelBase> CreateChannels<T>(
            string deviceId, string deviceName, int count, string prefix, string descPrefix) where T : ChannelBase, new()
        {
            var channels = new ObservableCollection<ChannelBase>();
            bool useZeroIndex = prefix == "AI" || prefix == "AO" || prefix == "DI" || prefix == "DO";
            for (int i = 0; i < count; i++)
            {
                var idx = useZeroIndex ? i : i + 1;
                var channel = new T
                {
                    Name = $"{prefix}{idx}",
                    DeviceId = deviceId,
                    DeviceName = deviceName,
                    Description = $"{descPrefix} {idx}"
                };
                channels.Add(channel);
            }
            return channels;
        }

        /// <summary>创建模拟输入通道集合</summary>
        public static ObservableCollection<ChannelBase> CreateAnalogInputChannels(
            string deviceId, string deviceName,  int count)
            => CreateChannels<AnalogInputChannel>(deviceId, deviceName, count, "AI", "模拟输入通道");

        /// <summary>创建模拟输出通道集合</summary>
        public static ObservableCollection<ChannelBase> CreateAnalogOutputChannels(
            string deviceId, string deviceName,  int count)
            => CreateChannels<AnalogOutputChannel>(deviceId, deviceName, count, "AO", "模拟输出通道");

        /// <summary>创建数字输入通道集合</summary>
        public static ObservableCollection<ChannelBase> CreateDigitalInputChannels(
            string deviceId, string deviceName, int count)
            => CreateChannels<DigitalInputChannel>(deviceId, deviceName, count, "DI", "数字输入通道");

        /// <summary>创建数字输出通道集合</summary>
        public static ObservableCollection<ChannelBase> CreateDigitalOutputChannels(
            string deviceId, string deviceName, int count)
            => CreateChannels<DigitalOutputChannel>(deviceId, deviceName, count, "DO", "数字输出通道");

        /// <summary>创建CAN总线通道集合</summary>
        public static ObservableCollection<ChannelBase> CreateCanChannels(
            string deviceId, string deviceName, int count)
            => CreateChannels<CanChannel>(deviceId, deviceName, count, "CAN", "CAN通道");

        /// <summary>
        /// 创建ARINC429通道集合
        /// </summary>
        /// <param name="deviceId">设备ID</param>
        /// <param name="deviceName">设备名称</param>
        /// <param name="slotPosition">插槽位置</param>
        /// <param name="txCount">发送通道数</param>
        /// <param name="rxCount">接收通道数</param>
        /// <param name="chassisNumber">机箱编号（用于PXI地址，默认为1）</param>
        /// <returns>ARINC429通道集合</returns>
        public static ObservableCollection<ChannelBase> CreateArinc429Channels(
            string deviceId, 
            string deviceName, 
            string slotPosition, 
            int txCount,
            int rxCount,
            int chassisNumber = 1)
        {
            var channels = new ObservableCollection<ChannelBase>();
            
            // 创建发送通道
            for (int i = 0; i < txCount; i++)
            {
                var channel = new Arinc429Channel
                {
                    Name = $"TX{i + 1}",
                    DeviceId = deviceId,
                    DeviceName = deviceName,
                    Description = $"ARINC429发送通道 {i + 1}",
                    BaudRate = "100K",
                    Direction = "TX",
                    Parity = "Odd",
                    Voltage = "HighVoltage",
                    SupportsLabel = true
                };
                
                channels.Add(channel);
            }
            
            // 创建接收通道
            for (int i = 0; i < rxCount; i++)
            {
                var channel = new Arinc429Channel
                {
                    Name = $"RX{i + 1}",
                    DeviceId = deviceId,
                    DeviceName = deviceName,
                    Description = $"ARINC429接收通道 {i + 1}",
                    BaudRate = "100K",
                    Direction = "RX",
                    Parity = "Odd",
                    Voltage = "HighVoltage",
                    SupportsLabel = true
                };
                
                channels.Add(channel);
            }
            
            return channels;
        }

        /// <summary>
        /// 创建MIL-1553B通道集合
        /// </summary>
        /// <param name="deviceId">设备ID</param>
        /// <param name="deviceName">设备名称</param>
        /// <param name="slotPosition">插槽位置</param>
        /// <param name="bcCount">总线控制器通道数</param>
        /// <param name="rtCount">远程终端通道数</param>
        /// <param name="bmCount">总线监视器通道数</param>
        /// <param name="chassisNumber">机箱编号（用于PXI地址，默认为1）</param>
        /// <returns>MIL-1553B通道集合</returns>
        public static ObservableCollection<ChannelBase> CreateMil1553BChannels(
            string deviceId, 
            string deviceName, 
            string slotPosition, 
            int bcCount = 1,
            int rtCount = 0,
            int bmCount = 0,
            int chassisNumber = 1)
        {
            var channels = new ObservableCollection<ChannelBase>();
            
            // 创建BC通道
            for (int i = 0; i < bcCount; i++)
            {
                var channel = new Mil1553BChannel
                {
                    Name = $"BC{i + 1}",
                    DeviceId = deviceId,
                    DeviceName = deviceName,
                    Description = $"1553B总线控制器 {i + 1}",
                    NodeType = "BC",
                    BusType = "Dual",
                    SupportsCoupling = true,
                    Voltage = "Transformer"
                };
                
                channels.Add(channel);
            }
            
            // 创建RT通道
            for (int i = 0; i < rtCount; i++)
            {
                var channel = new Mil1553BChannel
                {
                    Name = $"RT{i + 1}",
                    DeviceId = deviceId,
                    DeviceName = deviceName,
                    Description = $"1553B远程终端 {i + 1}",
                    NodeType = "RT",
                    BusType = "Dual",
                    RtAddress = i + 1,
                    SupportsCoupling = true,
                    Voltage = "Transformer"
                };
               
                channels.Add(channel);
            }
            
            // 创建BM通道
            for (int i = 0; i < bmCount; i++)
            {
                var channel = new Mil1553BChannel
                {
                    Name = $"BM{i + 1}",
                    DeviceId = deviceId,
                    DeviceName = deviceName,
                    Description = $"1553B总线监视器 {i + 1}",
                    NodeType = "BM",
                    BusType = "Dual",
                    SupportsCoupling = true,
                    Voltage = "Transformer"
                };
                
                channels.Add(channel);
            }
            
            return channels;
        }

        /// <summary>创建LVDT模拟通道集合</summary>
        public static ObservableCollection<ChannelBase> CreateLvdtChannels(
            string deviceId, string deviceName, int count, int chassisNumber = 1)
            => CreateChannels<LvdtChannel>(deviceId, deviceName,  count, "LVDT", "LVDT模拟通道");

        /// <summary>创建旋变模拟通道集合</summary>
        public static ObservableCollection<ChannelBase> CreateResolverChannels(
            string deviceId, string deviceName, int count, int chassisNumber = 1)
            => CreateChannels<ResolverChannel>(deviceId, deviceName, count,  "Resolver", "旋变模拟通道");

        /// <summary>
        /// 从插槽位置字符串提取槽位编号
        /// </summary>
        /// <param name="slotPosition">插槽位置（如"Slot 3"、"3"）</param>
        /// <returns>槽位编号，如果无法解析返回0</returns>
        public static int ExtractSlotNumber(string slotPosition)
        {
            if (string.IsNullOrEmpty(slotPosition))
                return 0;

            // 尝试直接解析数字
            if (int.TryParse(slotPosition, out int slotNum))
                return slotNum;

            // 尝试从"Slot 3"格式解析
            var parts = slotPosition.Split(' ');
            if (parts.Length >= 2 && int.TryParse(parts[1], out slotNum))
                return slotNum;

            // 尝试从其他格式提取数字
            foreach (var part in parts)
            {
                if (int.TryParse(part, out slotNum))
                    return slotNum;
            }

            return 0;
        }

        /// <summary>创建LVDS通道集合</summary>
        public static ObservableCollection<ChannelBase> CreateLvdsChannels(
            string deviceId, string deviceName, int count, int chassisNumber = 1)
            => CreateChannels<DigitalInputChannel>(deviceId, deviceName, count, "LVDS", "LVDS差分通道");
    }
}

