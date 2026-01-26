namespace MeasureControl.Models.Devices.Configurators
{
    /// <summary>
    /// 设备配置器接口
    /// 用于将设备型号配置逻辑从设备类中分离出来
    /// </summary>
    public interface IDeviceConfigurator
    {
        /// <summary>
        /// 检查配置器是否支持指定的设备型号
        /// </summary>
        /// <param name="modelName">设备型号名称</param>
        /// <returns>是否支持</returns>
        bool CanConfigure(string modelName);

        /// <summary>
        /// 配置设备参数
        /// </summary>
        /// <param name="device">要配置的设备</param>
        void Configure(DeviceBase device);
    }

    /// <summary>
    /// 设备配置器基类
    /// 提供通用的配置逻辑
    /// </summary>
    public abstract class DeviceConfiguratorBase : IDeviceConfigurator
    {
        /// <summary>
        /// 支持的型号关键字列表
        /// </summary>
        protected abstract string[] SupportedModelKeywords { get; }

        /// <summary>
        /// 检查配置器是否支持指定的设备型号
        /// </summary>
        public virtual bool CanConfigure(string modelName)
        {
            if (string.IsNullOrEmpty(modelName))
                return false;

            var lowerName = modelName.ToLower();
            foreach (var keyword in SupportedModelKeywords)
            {
                if (lowerName.Contains(keyword.ToLower()))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 配置设备参数
        /// </summary>
        public abstract void Configure(DeviceBase device);

        /// <summary>
        /// 辅助方法：检查型号名称是否包含任一关键字
        /// </summary>
        protected bool ContainsAnyKeyword(string modelName, params string[] keywords)
        {
            if (string.IsNullOrEmpty(modelName))
                return false;

            var lowerName = modelName.ToLower();
            foreach (var keyword in keywords)
            {
                if (lowerName.Contains(keyword.ToLower()))
                    return true;
            }
            return false;
        }
    }
}

