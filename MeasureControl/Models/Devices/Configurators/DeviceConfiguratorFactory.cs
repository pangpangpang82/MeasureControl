using System;
using System.Collections.Generic;
using System.Linq;
using MeasureControl.Models.Devices.Configurators.PowerSupply;
using MeasureControl.Models.Devices.Configurators.Dmm;

namespace MeasureControl.Models.Devices.Configurators
{
    /// <summary>
    /// 设备配置器工厂
    /// 根据设备型号自动选择合适的配置器
    /// </summary>
    public static class DeviceConfiguratorFactory
    {
        private static readonly List<IDeviceConfigurator> _configurators;

        static DeviceConfiguratorFactory()
        {
            _configurators = new List<IDeviceConfigurator>();

            // 注册所有配置器
            RegisterPowerSupplyConfigurators();
            RegisterDmmConfigurators();
            // 其他设备配置器可以在这里注册
            // 自动扫描并注册未显式注册的配置器，避免每次添加新配置器都要修改此工厂
            AutoRegisterConfigurators();
        }

        /// <summary>
        /// 注册程控电源配置器
        /// </summary>
        private static void RegisterPowerSupplyConfigurators()
        {
            _configurators.Add(new ITM3912DConfigurator());
            _configurators.Add(new IT6332Configurator());
        }

        /// <summary>
        /// 注册DMM配置器
        /// </summary>
        private static void RegisterDmmConfigurators()
        {
            _configurators.Add(new DM3068Configurator());
            // 其他DMM配置器可以在这里添加
        }

        /// <summary>
        /// 获取设备配置器
        /// </summary>
        /// <param name="modelName">设备型号名称</param>
        /// <returns>匹配的配置器，如果没有匹配则返回null</returns>
        public static IDeviceConfigurator GetConfigurator(string modelName)
        {
            if (string.IsNullOrEmpty(modelName))
                return null;

            return _configurators.FirstOrDefault(c => c.CanConfigure(modelName));
        }

        /// <summary>
        /// 尝试配置设备
        /// </summary>
        /// <param name="device">要配置的设备</param>
        /// <param name="modelName">设备型号名称</param>
        /// <returns>是否成功配置</returns>
        public static bool TryConfigure(DeviceBase device, string modelName)
        {
            var configurator = GetConfigurator(modelName);
            if (configurator != null)
            {
                configurator.Configure(device);
                return true;
            }
            return false;
        }

        /// <summary>
        /// 注册自定义配置器
        /// </summary>
        /// <param name="configurator">配置器实例</param>
        public static void RegisterConfigurator(IDeviceConfigurator configurator)
        {
            if (configurator != null && !_configurators.Contains(configurator))
            {
                _configurators.Add(configurator);
            }
        }

        /// <summary>
        /// 获取所有已注册的配置器
        /// </summary>
        public static IEnumerable<IDeviceConfigurator> GetAllConfigurators()
        {
            return _configurators.AsReadOnly();
        }

        /// <summary>
        /// 自动扫描当前程序集，查找实现 IDeviceConfigurator 的类型并注册它们（如果尚未注册）。
        /// 这样新增配置器只需添加类文件，无需修改本工厂代码。
        /// </summary>
        private static void AutoRegisterConfigurators()
        {
            try
            {
                var asm = typeof(DeviceConfiguratorFactory).Assembly;
                var configuratorTypes = asm.GetTypes()
                    .Where(t => typeof(IDeviceConfigurator).IsAssignableFrom(t) && !t.IsAbstract && t.GetConstructor(Type.EmptyTypes) != null);

                foreach (var ct in configuratorTypes)
                {
                    if (!_configurators.Any(c => c.GetType() == ct))
                    {
                        var instance = (IDeviceConfigurator)Activator.CreateInstance(ct);
                        _configurators.Add(instance);
                    }
                }
            }
            catch
            {
                // 安全降级：如果反射失败，不阻止程序运行，手动注册仍然有效
            }
        }
    }
}

