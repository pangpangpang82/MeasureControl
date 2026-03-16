using System;
using System.Collections.Generic;

namespace MeasureControl.Helpers
{
    /// <summary>
    /// 设备注册表
    /// 维护设备型号与类型的映射关系，支持运行时扩展
    /// </summary>
    public static class DeviceRegistry
    {
        /// <summary>
        /// 设备识别优先级
        /// </summary>
        public enum RecognitionPriority
        {
            /// <summary>机箱设备（最高优先级）</summary>
            Chassis = 1,
            /// <summary>系统控制器设备</summary>
            Controller = 2,
            /// <summary>特定型号精确识别</summary>
            SpecificModel = 3,
            /// <summary>特定关键字识别</summary>
            KeywordMatch = 4,
            /// <summary>多功能设备识别</summary>
            MultiFunctionDevice = 5,
            /// <summary>通用PXI板卡识别</summary>
            GenericPxi = 6,
            /// <summary>默认识别</summary>
            Default = 99
        }

        /// <summary>
        /// 机箱设备型号前缀
        /// </summary>
        public static readonly HashSet<string> ChassisModelPrefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "pxie-2722", "pxi-2722",
            "pxie-2519", "pxi-2519"
        };

        /// <summary>
        /// 系统控制器型号前缀
        /// </summary>
        public static readonly HashSet<string> ControllerModelPrefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "pxie-3987", "pxi-3987"
        };

        /// <summary>
        /// 矩阵开关设备型号
        /// </summary>
        public static readonly HashSet<string> SwitchDeviceModels = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "pxi-3022", "pxie-3022",
            "pxi-2601", "pxie-2601"
        };

        /// <summary>
        /// 同步采样模拟量采集设备型号
        /// </summary>
        public static readonly HashSet<string> SynchronousAnalogModels = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "pxie-9774", "pxi-9774", "9774"
        };

        /// <summary>
        /// 程控电阻设备型号
        /// </summary>
        public static readonly HashSet<string> ProgrammableResistorModels = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "pxi-7012", "pxie-7012"
        };

        /// <summary>
        /// 数字IO设备型号
        /// </summary>
        public static readonly HashSet<string> DigitalIOModels = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "pxie-7131", "pxi-7131"
        };

        /// <summary>
        /// LVDT/RVDT模拟测量设备型号
        /// </summary>
        public static readonly HashSet<string> LvdtRvdtModels = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "pxi-4087a", "pxie-4087a",
            "pxi-", "pxie-"
        };

        /// <summary>
        /// CAN总线设备型号
        /// </summary>
        public static readonly HashSet<string> CanBusModels = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "pxi-4004", "pxie-4004"
        };

        /// <summary>
        /// ARINC429设备型号
        /// </summary>
        public static readonly HashSet<string> Arinc429Models = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "px1e-4227", "pxie-4227", "pxi-4227",
            "art4227", "art4229", "art4228",
            "art4226", "art4223", "art4222"
        };

        /// <summary>
        /// MIL-STD-1553B总线设备型号
        /// </summary>
        public static readonly HashSet<string> Mil1553BModels = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "px1-4332", "pxi-4332", "pxie-4332", "4332",
            "art4332", "milstd1553b", "mil-std-1553b"
        };

        /// <summary>
        /// MIL-STD-1394B总线设备型号（怀智HZ-MIL1394B-PXIe-4N）
        /// </summary>
        public static readonly HashSet<string> Mil1394BModels = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "hz-mil1394b-pxie-4n", "hz-mil1394b", 
            "hz-mil1394b-px1e-4n", 
            "mil1394b-pxie-4n", "mil1394b-pxie",
            "mil1394b-px1e-4n", "mil1394b-px1e",
            "mil1394b", "1394b"
        };

        /// <summary>
        /// 模拟量输出设备型号（芒果树 MT-X532）
        /// </summary>
        public static readonly HashSet<string> AnalogOutputModels = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "mt-x532", "mtx532", "x532"
        };

        /// <summary>
        /// LVDS设备型号（芒果树 MT-X970）
        /// </summary>
        public static readonly HashSet<string> LvdsModels = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "mt-x970", "mtx970", "x970"
        };

        /// <summary>
        /// 程控电源设备型号
        /// </summary>
        public static readonly HashSet<string> PowerSupplyModels = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "it6322", "it-6322", "it-n6322",
            "it6332", "it-6332", "it-n6332",
            "it6333", "it-6333", "it-n6333",
            "it-m3912d", "m3912d", "it-m3912d-500-72",
            "艾泰克 m3912d", "itech m3912d",
            "dpm8605"
        };

        /// <summary>
        /// 电子负载设备型号
        /// </summary>
        public static readonly HashSet<string> ElectronicLoadModels = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "6314a", "6312a",
            "chroma 6314", "chroma 6312",
            "chroma 6310a", "致茂 6310a",
            "63102a", "63103a", "63105a", "63106a", "63123a"
        };

        /// <summary>
        /// 数字万用表设备型号
        /// </summary>
        public static readonly HashSet<string> DmmModels = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "dm3068"
        };

        /// <summary>
        /// 信号发生器设备型号
        /// </summary>
        public static readonly HashSet<string> SignalGeneratorModels = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "dg1032z", "dg1032"
        };

        /// <summary>
        /// 示波器设备型号
        /// </summary>
        public static readonly HashSet<string> OscilloscopeModels = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "dh04804", "ds1104"
        };

        /// <summary>
        /// 频率计设备型号
        /// </summary>
        public static readonly HashSet<string> FrequencyCounterModels = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "53220a", "53220"
        };

        /// <summary>
        /// 设备关键字映射（按功能分类）
        /// </summary>
        public static class Keywords
        {
            /// <summary>串口设备关键字</summary>
            public static readonly string[] Serial = { "串口", "serial" };

            /// <summary>矩阵开关设备关键字</summary>
            public static readonly string[] Switch = { "矩阵开关", "matrix switch", "矩阵", "开关", "switch", "matrix" };

            /// <summary>模拟量设备关键字</summary>
            public static readonly string[] Analog = { "模拟量采集", "模拟量输出", "模拟", "analog" };

            /// <summary>数字万用表设备关键字</summary>
            public static readonly string[] Dmm = { "数字万用表", "数字多用表", "dmm", "多用表" };

            /// <summary>程控电源设备关键字</summary>
            public static readonly string[] PowerSupply = { "程控电源", "电源", "power supply", "power" };

            /// <summary>示波器设备关键字</summary>
            public static readonly string[] Oscilloscope = { "示波器", "oscilloscope" };

            /// <summary>电子负载设备关键字</summary>
            public static readonly string[] ElectronicLoad = { "电子负载", "electronic load", "6310a" };

            /// <summary>信号发生器关键字</summary>
            public static readonly string[] SignalGenerator = { "信号发生器", "signal generator" };

            /// <summary>频率计关键字</summary>
            public static readonly string[] FrequencyCounter = { "频率计", "frequency counter" };

            /// <summary>USB转RS422关键字</summary>
            public static readonly string[] UsbToRs422 = { "usb", "rs422" };

            /// <summary>FPGA高速IO关键字</summary>
            public static readonly string[] FpgaHighSpeedIo = { "fpga", "io", "高速" };
        }

        /// <summary>
        /// PXI设备前缀
        /// </summary>
        public static readonly string[] PxiPrefixes = { "pxie-", "pxi-", "px1e-", "px1-" };

        /// <summary>
        /// 通用PXI设备子关键字映射
        /// </summary>
        public static class PxiKeywords
        {
            public static readonly string[] Controller = { "控制器", "controller" };
            public static readonly string[] DigitalIO = { "离散量", "digital io", "数字io" };
            public static readonly string[] Resistor = { "电阻输出", "resistance", "programmable resistor" };
            public static readonly string[] Lvdt = { "lvdt", "rvdt" };
            public static readonly string[] Can = { "can" };
            public static readonly string[] Arinc = { "arinc429", "arinc" };
            public static readonly string[] Mil1553B = { "1553b", "1553", "milstd1553" };
            public static readonly string[] Mil1394B = { "1394b", "1394" };
            public static readonly string[] Lvds = { "lvds" };
        }

        /// <summary>
        /// 检查型号是否匹配指定的型号集合
        /// </summary>
        public static bool ContainsAny(string deviceName, HashSet<string> models)
        {
            if (string.IsNullOrEmpty(deviceName) || models == null)
                return false;

            var lowerName = deviceName.ToLower();
            foreach (var model in models)
            {
                if (lowerName.Contains(model.ToLower()))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 检查设备名称是否包含指定关键字数组中的任意一个
        /// </summary>
        public static bool ContainsKeyword(string deviceName, string[] keywords)
        {
            if (string.IsNullOrEmpty(deviceName) || keywords == null)
                return false;

            var lowerName = deviceName.ToLower();
            foreach (var keyword in keywords)
            {
                if (lowerName.Contains(keyword.ToLower()))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 检查设备名称是否包含所有指定的关键字
        /// </summary>
        public static bool ContainsAllKeywords(string deviceName, string[] keywords)
        {
            if (string.IsNullOrEmpty(deviceName) || keywords == null)
                return false;

            var lowerName = deviceName.ToLower();
            foreach (var keyword in keywords)
            {
                if (!lowerName.Contains(keyword.ToLower()))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// 检查设备名称是否以PXI前缀开始
        /// </summary>
        public static bool IsPxiDevice(string deviceName)
        {
            if (string.IsNullOrEmpty(deviceName))
                return false;

            var lowerName = deviceName.ToLower();
            foreach (var prefix in PxiPrefixes)
            {
                if (lowerName.Contains(prefix))
                    return true;
            }
            return false;
        }
    }
}

