using System;
using System.Collections.Generic;
using System.Linq;
using MeasureControl.Models.Devices;

namespace MeasureControl.Helpers
{
    /// <summary>
    /// 设备工厂类
    /// </summary>
    public static class DeviceFactory
    {
        /// <summary>
        /// 设备创建规则
        /// </summary>
        private class DeviceCreationRule
        {
            public string Name { get; set; }
            public Func<string, bool> Matcher { get; set; }
            public Func<string, string, DeviceBase> Creator { get; set; }
            public int Priority { get; set; }

            public DeviceCreationRule(string name, Func<string, bool> matcher, 
                Func<string, string, DeviceBase> creator, int priority)
            {
                Name = name;
                Matcher = matcher;
                Creator = creator;
                Priority = priority;
            }
        }

        private static readonly List<DeviceCreationRule> _creationRules;

        /// <summary>
        /// 静态构造函数，初始化设备创建规则
        /// </summary>
        static DeviceFactory()
        {
            _creationRules = new List<DeviceCreationRule>();
            
            // 按优先级注册设备创建规则
            RegisterChassisDevices();           // 优先级 1
            RegisterControllerDevices();        // 优先级 2
            RegisterSpecificModelDevices();     // 优先级 3
            RegisterKeywordBasedDevices();      // 优先级 4
            RegisterPxiDevices();               // 优先级 5
            RegisterDefaultDevices();           // 优先级 6

            // 按优先级排序（数字越小优先级越高）
            _creationRules.Sort((a, b) => a.Priority.CompareTo(b.Priority));
        }

        #region 优先级 1: 机箱设备（最高优先级）

        private static void RegisterChassisDevices()
        {
            _creationRules.Add(new DeviceCreationRule(
                "Chassis",
                deviceName => DeviceRegistry.ContainsAny(deviceName, DeviceRegistry.ChassisModelPrefixes),
                (deviceName, slotPosition) => new ChassisDevice(deviceName),
                priority: 1
            ));
        }

        #endregion

        #region 优先级 2: 控制器设备

        private static void RegisterControllerDevices()
        {
            _creationRules.Add(new DeviceCreationRule(
                "Controller",
                deviceName => DeviceRegistry.ContainsAny(deviceName, DeviceRegistry.ControllerModelPrefixes),
                (deviceName, slotPosition) => new ControllerDevice(deviceName, slotPosition),
                priority: 2
            ));
        }

        #endregion

        #region 优先级 3: 特定型号设备

        private static void RegisterSpecificModelDevices()
        {
            // 矩阵开关设备
            _creationRules.Add(new DeviceCreationRule(
                "Switch",
                deviceName => DeviceRegistry.ContainsAny(deviceName, DeviceRegistry.SwitchDeviceModels),
                (deviceName, slotPosition) => new SwitchDevice(deviceName, slotPosition),
                priority: 3
            ));

            // 同步模拟量采集设备
            _creationRules.Add(new DeviceCreationRule(
                "SynchronousAnalog",
                deviceName => DeviceRegistry.ContainsAny(deviceName, DeviceRegistry.SynchronousAnalogModels),
                (deviceName, slotPosition) => {
                    var device = new AnalogAcquisitionDevice(deviceName, slotPosition);
                    return device;
                },
                priority: 3
            ));

            // 可编程电阻设备
            _creationRules.Add(new DeviceCreationRule(
                "ProgrammableResistor",
                deviceName => DeviceRegistry.ContainsAny(deviceName, DeviceRegistry.ProgrammableResistorModels),
                (deviceName, slotPosition) => new ProgrammableResistorDevice(deviceName, slotPosition),
                priority: 3
            ));

            // 离散量输入输出设备
            _creationRules.Add(new DeviceCreationRule(
                "DigitalIO",
                deviceName => DeviceRegistry.ContainsAny(deviceName, DeviceRegistry.DigitalIOModels),
                (deviceName, slotPosition) => new DigitalIODevice(deviceName, slotPosition),
                priority: 3
            ));

            // LVDT/RVDT 模拟测量设备 (PXI-4087A/B)
            _creationRules.Add(new DeviceCreationRule(
                "LVDT_4087A",
                deviceName => DeviceRegistry.ContainsAny(deviceName, DeviceRegistry.LvdtRvdtModels) &&
                             deviceName.ToLower().Contains("4087a"),
                (deviceName, slotPosition) => new LvdtSimulatorDevice(deviceName, slotPosition),
                priority: 3
            ));

            // 旋变模拟测量设备 (PXI-4087C)
            _creationRules.Add(new DeviceCreationRule(
                "Resolver_4087C",
                deviceName => deviceName.ToLower().Contains("4087c") ||
                             (DeviceRegistry.ContainsAny(deviceName, DeviceRegistry.LvdtRvdtModels) &&
                              deviceName.ToLower().Contains("旋变")),
                (deviceName, slotPosition) => new ResolverSimulatorDevice(deviceName, slotPosition),
                priority: 3
            ));


            // CAN总线设备
            _creationRules.Add(new DeviceCreationRule(
                "CanBus",
                deviceName => DeviceRegistry.ContainsAny(deviceName, DeviceRegistry.CanBusModels),
                (deviceName, slotPosition) => new CanBusDevice(deviceName, slotPosition),
                priority: 3
            ));

            // ARINC429设备
            _creationRules.Add(new DeviceCreationRule(
                "Arinc429",
                deviceName => DeviceRegistry.ContainsAny(deviceName, DeviceRegistry.Arinc429Models),
                (deviceName, slotPosition) => new Arinc429Device(deviceName, slotPosition),
                priority: 3
            ));

            // 1553B总线设备
            _creationRules.Add(new DeviceCreationRule(
                "Mil1553B",
                deviceName => DeviceRegistry.ContainsAny(deviceName, DeviceRegistry.Mil1553BModels),
                (deviceName, slotPosition) => new Mil1553BDevice(deviceName, slotPosition),
                priority: 3
            ));

            // 1394B总线设备
            _creationRules.Add(new DeviceCreationRule(
                "Mil1394B",
                deviceName => DeviceRegistry.ContainsAny(deviceName, DeviceRegistry.Mil1394BModels),
                (deviceName, slotPosition) => new Mil1394BDevice(deviceName, slotPosition),
                priority: 3
            ));

            // 模拟量输出设备（MT-X532）
            _creationRules.Add(new DeviceCreationRule(
                "AnalogOutput",
                deviceName => DeviceRegistry.ContainsAny(deviceName, DeviceRegistry.AnalogOutputModels),
                (deviceName, slotPosition) => new AnalogOutputDevice(deviceName, slotPosition),
                priority: 3
            ));

            // LVDS设备（MT-X970）
            _creationRules.Add(new DeviceCreationRule(
                "LVDS",
                deviceName => DeviceRegistry.ContainsAny(deviceName, DeviceRegistry.LvdsModels),
                (deviceName, slotPosition) => new LvdsDevice(deviceName, slotPosition),
                priority: 3
            ));

            // 程控电源设备
            _creationRules.Add(new DeviceCreationRule(
                "PowerSupply",
                deviceName => DeviceRegistry.ContainsAny(deviceName, DeviceRegistry.PowerSupplyModels),
                (deviceName, slotPosition) => new PowerSupplyDevice(deviceName, slotPosition),
                priority: 3
            ));

            // 电子负载设备
            _creationRules.Add(new DeviceCreationRule(
                "ElectronicLoad",
                deviceName => DeviceRegistry.ContainsAny(deviceName, DeviceRegistry.ElectronicLoadModels),
                (deviceName, slotPosition) => new ElectronicLoadDevice(deviceName, slotPosition),
                priority: 3
            ));

            // 数字多用表设备
            _creationRules.Add(new DeviceCreationRule(
                "DMM",
                deviceName => DeviceRegistry.ContainsAny(deviceName, DeviceRegistry.DmmModels),
                (deviceName, slotPosition) => new DmmDevice(deviceName, slotPosition),
                priority: 3
            ));

            // 信号发生器设备
            _creationRules.Add(new DeviceCreationRule(
                "SignalGenerator",
                deviceName => DeviceRegistry.ContainsAny(deviceName, DeviceRegistry.SignalGeneratorModels),
                (deviceName, slotPosition) => new SignalGeneratorDevice(deviceName, slotPosition),
                priority: 3
            ));

            // 示波器设备
            _creationRules.Add(new DeviceCreationRule(
                "Oscilloscope",
                deviceName => DeviceRegistry.ContainsAny(deviceName, DeviceRegistry.OscilloscopeModels),
                (deviceName, slotPosition) => new OscilloscopeDevice(deviceName, slotPosition),
                priority: 3
            ));

            // 频率计设备
            _creationRules.Add(new DeviceCreationRule(
                "FrequencyCounter",
                deviceName => DeviceRegistry.ContainsAny(deviceName, DeviceRegistry.FrequencyCounterModels),
                (deviceName, slotPosition) => new FrequencyCounterDevice(deviceName, slotPosition),
                priority: 3
            ));

            // 自定义设备：USB转RS422模块
            _creationRules.Add(new DeviceCreationRule(
                "UsbToRs422",
                deviceName => DeviceRegistry.ContainsAllKeywords(deviceName, DeviceRegistry.Keywords.UsbToRs422),
                (deviceName, slotPosition) => new GenericDevice(deviceName, slotPosition),
                priority: 3
            ));

            // 自定义设备：FPGA高速IO板
            _creationRules.Add(new DeviceCreationRule(
                "FpgaIO",
                deviceName => {
                    var lower = deviceName.ToLower();
                    return lower.Contains("fpga") && (lower.Contains("io") || lower.Contains("高速"));
                },
                (deviceName, slotPosition) => new GenericDevice(deviceName, slotPosition),
                priority: 3
            ));
        }

        #endregion

        #region 优先级 4: 关键字识别设备

        private static void RegisterKeywordBasedDevices()
        {
            // 串口设备（优先识别，避免被误识别）
            _creationRules.Add(new DeviceCreationRule(
                "Serial",
                deviceName => DeviceRegistry.ContainsKeyword(deviceName, DeviceRegistry.Keywords.Serial),
                (deviceName, slotPosition) => new GenericDevice(deviceName, slotPosition),
                priority: 4
            ));

            // 矩阵开关设备（关键字）
            _creationRules.Add(new DeviceCreationRule(
                "Switch_Keyword",
                deviceName => DeviceRegistry.ContainsKeyword(deviceName, DeviceRegistry.Keywords.Switch),
                (deviceName, slotPosition) => new SwitchDevice(deviceName, slotPosition),
                priority: 4
            ));

            // 模拟量设备（关键字）
            _creationRules.Add(new DeviceCreationRule(
                "Analog_Keyword",
                deviceName => DeviceRegistry.ContainsKeyword(deviceName, DeviceRegistry.Keywords.Analog),
                (deviceName, slotPosition) => new AnalogAcquisitionDevice(deviceName, slotPosition),
                priority: 4
            ));

            // 数字多用表设备（关键字）
            _creationRules.Add(new DeviceCreationRule(
                "DMM_Keyword",
                deviceName => DeviceRegistry.ContainsKeyword(deviceName, DeviceRegistry.Keywords.Dmm),
                (deviceName, slotPosition) => new DmmDevice(deviceName, slotPosition),
                priority: 4
            ));

            // 程控电源设备（关键字）
            _creationRules.Add(new DeviceCreationRule(
                "PowerSupply_Keyword",
                deviceName => DeviceRegistry.ContainsKeyword(deviceName, DeviceRegistry.Keywords.PowerSupply),
                (deviceName, slotPosition) => new PowerSupplyDevice(deviceName, slotPosition),
                priority: 4
            ));

            // 示波器设备（关键字）
            _creationRules.Add(new DeviceCreationRule(
                "Oscilloscope_Keyword",
                deviceName => DeviceRegistry.ContainsKeyword(deviceName, DeviceRegistry.Keywords.Oscilloscope),
                (deviceName, slotPosition) => new OscilloscopeDevice(deviceName, slotPosition),
                priority: 4
            ));

            // 电子负载设备（关键字）
            _creationRules.Add(new DeviceCreationRule(
                "ElectronicLoad_Keyword",
                deviceName => DeviceRegistry.ContainsKeyword(deviceName, DeviceRegistry.Keywords.ElectronicLoad),
                (deviceName, slotPosition) => new ElectronicLoadDevice(deviceName, slotPosition),
                priority: 4
            ));

            // 信号发生器（关键字）
            _creationRules.Add(new DeviceCreationRule(
                "SignalGenerator_Keyword",
                deviceName => DeviceRegistry.ContainsKeyword(deviceName, DeviceRegistry.Keywords.SignalGenerator),
                (deviceName, slotPosition) => new SignalGeneratorDevice(deviceName, slotPosition),
                priority: 4
            ));

            // 频率计（关键字）
            _creationRules.Add(new DeviceCreationRule(
                "FrequencyCounter_Keyword",
                deviceName => DeviceRegistry.ContainsKeyword(deviceName, DeviceRegistry.Keywords.FrequencyCounter),
                (deviceName, slotPosition) => new FrequencyCounterDevice(deviceName, slotPosition),
                priority: 4
            ));
        }

        #endregion

        #region 优先级 5: 通用PXI设备

        private static void RegisterPxiDevices()
        {
            // PXI控制器
            _creationRules.Add(new DeviceCreationRule(
                "PXI_Controller",
                deviceName => DeviceRegistry.IsPxiDevice(deviceName) && 
                             DeviceRegistry.ContainsKeyword(deviceName, DeviceRegistry.PxiKeywords.Controller),
                (deviceName, slotPosition) => new ControllerDevice(deviceName, slotPosition),
                priority: 5
            ));

            // PXI数字IO
            _creationRules.Add(new DeviceCreationRule(
                "PXI_DigitalIO",
                deviceName => DeviceRegistry.IsPxiDevice(deviceName) && 
                             DeviceRegistry.ContainsKeyword(deviceName, DeviceRegistry.PxiKeywords.DigitalIO),
                (deviceName, slotPosition) => new DigitalIODevice(deviceName, slotPosition),
                priority: 5
            ));

            // PXI可编程电阻
            _creationRules.Add(new DeviceCreationRule(
                "PXI_Resistor",
                deviceName => DeviceRegistry.IsPxiDevice(deviceName) && 
                             DeviceRegistry.ContainsKeyword(deviceName, DeviceRegistry.PxiKeywords.Resistor),
                (deviceName, slotPosition) => new ProgrammableResistorDevice(deviceName, slotPosition),
                priority: 5
            ));

            // PXI LVDT
            _creationRules.Add(new DeviceCreationRule(
                "PXI_LVDT",
                deviceName => DeviceRegistry.IsPxiDevice(deviceName) && 
                             DeviceRegistry.ContainsKeyword(deviceName, DeviceRegistry.PxiKeywords.Lvdt),
                (deviceName, slotPosition) => new GenericDevice(deviceName, slotPosition),
                priority: 5
            ));

            // PXI CAN总线
            _creationRules.Add(new DeviceCreationRule(
                "PXI_CAN",
                deviceName => DeviceRegistry.IsPxiDevice(deviceName) && 
                             DeviceRegistry.ContainsKeyword(deviceName, DeviceRegistry.PxiKeywords.Can),
                (deviceName, slotPosition) => new CanBusDevice(deviceName, slotPosition),
                priority: 5
            ));

            // PXI ARINC429
            _creationRules.Add(new DeviceCreationRule(
                "PXI_ARINC",
                deviceName => DeviceRegistry.IsPxiDevice(deviceName) && 
                             DeviceRegistry.ContainsKeyword(deviceName, DeviceRegistry.PxiKeywords.Arinc),
                (deviceName, slotPosition) => new Arinc429Device(deviceName, slotPosition),
                priority: 5
            ));

            // PXI 1553B
            _creationRules.Add(new DeviceCreationRule(
                "PXI_1553B",
                deviceName => DeviceRegistry.IsPxiDevice(deviceName) && 
                             DeviceRegistry.ContainsKeyword(deviceName, DeviceRegistry.PxiKeywords.Mil1553B),
                (deviceName, slotPosition) => new Mil1553BDevice(deviceName, slotPosition),
                priority: 5
            ));

            // PXI 1394B
            _creationRules.Add(new DeviceCreationRule(
                "PXI_1394B",
                deviceName => DeviceRegistry.IsPxiDevice(deviceName) && 
                             DeviceRegistry.ContainsKeyword(deviceName, DeviceRegistry.PxiKeywords.Mil1394B),
                (deviceName, slotPosition) => new Mil1394BDevice(deviceName, slotPosition),
                priority: 5
            ));

            // PXI LVDS
            _creationRules.Add(new DeviceCreationRule(
                "PXI_LVDS",
                deviceName => DeviceRegistry.IsPxiDevice(deviceName) && 
                             DeviceRegistry.ContainsKeyword(deviceName, DeviceRegistry.PxiKeywords.Lvds),
                (deviceName, slotPosition) => new GenericDevice(deviceName, slotPosition),
                priority: 5
            ));

            // 未知PXI设备（兜底）
            _creationRules.Add(new DeviceCreationRule(
                "PXI_Generic",
                deviceName => DeviceRegistry.IsPxiDevice(deviceName),
                (deviceName, slotPosition) => new GenericDevice(deviceName, slotPosition),
                priority: 5
            ));
        }

        #endregion

        #region 优先级 6: 默认设备（最低优先级）

        private static void RegisterDefaultDevices()
        {
            // 默认创建为通用设备
            _creationRules.Add(new DeviceCreationRule(
                "Default",
                deviceName => true,  // 总是匹配
                (deviceName, slotPosition) => new GenericDevice(deviceName, slotPosition),
                priority: 6
            ));
        }

        #endregion

        #region 公共接口

        /// <summary>
        /// 从设备名称创建设备实例
        /// </summary>
        public static DeviceBase CreateDevice(string deviceName, string slotPosition, string parentContext)
        {
            if (string.IsNullOrEmpty(deviceName))
            {
                return null;
            }

            // 特殊处理：根据父节点上下文判断多功能设备
            if (DeviceRegistry.ContainsAny(deviceName, DeviceRegistry.AnalogOutputModels))
            {
                return new AnalogOutputDevice(deviceName, slotPosition);
            }

            // 其他设备使用默认逻辑
            return CreateDevice(deviceName, slotPosition);
        }

        /// <summary>
        /// 从设备名称创建设备实例
        /// </summary>
        public static DeviceBase CreateDevice(string deviceName, string slotPosition = "")
        {
            if (string.IsNullOrEmpty(deviceName))
            {
                return null;
            }

            // 遍历规则链，找到第一个匹配的规则
            foreach (var rule in _creationRules)
            {
                try
                {
                    if (rule.Matcher(deviceName))
                    {
                        return rule.Creator(deviceName, slotPosition);
                    }
                }
                catch (Exception)
                {
                    // 记录异常但继续尝试下一个规则
                }
            }

            // 理论上不会到这里
            return new GenericDevice(deviceName, slotPosition);
        }

        /// <summary>
        /// 注册自定义设备创建规则
        /// </summary>
        public static void RegisterRule(string name, Func<string, bool> matcher, 
            Func<string, string, DeviceBase> creator, int priority = 3)
        {
            var rule = new DeviceCreationRule(name, matcher, creator, priority);
            _creationRules.Add(rule);
            _creationRules.Sort((a, b) => a.Priority.CompareTo(b.Priority));
        }

        /// <summary>
        /// 从设备名称创建简单的设备项
        /// </summary>
        //public static DeviceBase CreateSimpleDevice(string deviceName, string slotPosition = "")
        //{
        //    var device = new GenericDevice();
        //    device.SlotPosition = slotPosition;

        //    if (string.IsNullOrEmpty(deviceName))
        //    {
        //        device.Name = "N/A";
        //        device.Manufacturer = "N/A";
        //        device.Model = "N/A";
        //        device.DeviceType = "Card";
        //        return device;
        //    }

        //    // 解析设备名称，格式：制造商 型号
        //    var parts = deviceName.Split(' ');
        //    if (parts.Length >= 2)
        //    {
        //        device.Manufacturer = parts[0];
        //        device.Model = string.Join(" ", System.Linq.Enumerable.Skip(parts, 1));
        //        device.Name = deviceName;
        //    }
        //    else
        //    {
        //        device.Name = deviceName;
        //        device.Manufacturer = "N/A";
        //        device.Model = "N/A";
        //    }

        //    // 根据设备名称判断设备类型
        //    var lowerName = deviceName.ToLower();

        //    // 机箱设备
        //    if (lowerName.Contains("机箱") || lowerName.Contains("chassis"))
        //    {
        //        device.DeviceType = "Chassis";
        //    }
        //    // PXI板卡设备
        //    else if (DeviceRegistry.IsPxiDevice(deviceName) ||
        //             lowerName.Contains("控制器") || lowerName.Contains("controller") ||
        //             lowerName.Contains("模拟") || lowerName.Contains("analog") ||
        //             lowerName.Contains("数字") || lowerName.Contains("digital") ||
        //             lowerName.Contains("矩阵") || lowerName.Contains("matrix") ||
        //             lowerName.Contains("开关") || lowerName.Contains("switch") ||
        //             lowerName.Contains("离散量") || lowerName.Contains("lvdt") ||
        //             lowerName.Contains("rvdt") || lowerName.Contains("旋转变压器") ||
        //             lowerName.Contains("can") || lowerName.Contains("arinc429") ||
        //             lowerName.Contains("1553b") || lowerName.Contains("1394b") ||
        //             lowerName.Contains("lvds"))
        //    {
        //        device.DeviceType = "Card";
        //    }
        //    // 程控设备
        //    else if (lowerName.Contains("电源") || lowerName.Contains("power") ||
        //             lowerName.Contains("程控电源") || lowerName.Contains("示波器") ||
        //             lowerName.Contains("oscilloscope") || lowerName.Contains("电子负载") ||
        //             lowerName.Contains("electronic load") || lowerName.Contains("串口") ||
        //             lowerName.Contains("serial") || lowerName.Contains("6314a") ||
        //             lowerName.Contains("chroma") || lowerName.Contains("dg1032z") ||
        //             lowerName.Contains("信号发生器") || lowerName.Contains("dm3068") ||
        //             lowerName.Contains("数字多用表") || lowerName.Contains("dh04804") ||
        //             lowerName.Contains("ms05000") || lowerName.Contains("53220a") ||
        //             lowerName.Contains("频率计"))
        //    {
        //        device.DeviceType = "Instrument";
        //    }
        //    // 默认为板卡设备
        //    else
        //    {
        //        device.DeviceType = "Card";
        //    }

        //    return device;
        //}

        #endregion
    }
}
