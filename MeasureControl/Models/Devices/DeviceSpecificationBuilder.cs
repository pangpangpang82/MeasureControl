using System;

namespace MeasureControl.Models.Devices
{
    /// <summary>
    /// 设备规格构建器，使用流式API简化规格信息构建
    /// </summary>
    public class DeviceSpecificationBuilder
    {
        private readonly DeviceSpecification _specification;
        private string _currentGroup;

        public DeviceSpecificationBuilder()
        {
            _specification = new DeviceSpecification();
            _currentGroup = null;
        }

        /// <summary>
        /// 添加规格项到指定分组
        /// </summary>
        public DeviceSpecificationBuilder Add(string key, string value, string group = null)
        {
            var targetGroup = group ?? _currentGroup ?? "基本信息";
            _specification.Add(key, value, targetGroup);
            return this;
        }

        /// <summary>
        /// 设置当前分组（后续Add调用将使用此分组）
        /// </summary>
        public DeviceSpecificationBuilder AddGroup(string groupName)
        {
            _currentGroup = groupName;
            return this;
        }

        /// <summary>
        /// 添加基本信息（快捷方法）
        /// </summary>
        public DeviceSpecificationBuilder AddBasicInfo(string key, string value)
        {
            return Add(key, value, "基本信息");
        }

        /// <summary>
        /// 批量添加基本信息
        /// </summary>
        public DeviceSpecificationBuilder AddBasicInfoBatch(params (string key, string value)[] items)
        {
            foreach (var (key, value) in items)
            {
                if (!string.IsNullOrEmpty(value))
                {
                    AddBasicInfo(key, value);
                }
            }
            return this;
        }

        /// <summary>
        /// 添加通用环境参数
        /// </summary>
        public DeviceSpecificationBuilder AddCommonEnvironment(
            string operatingTemp = "0 ~ 40℃",
            string storageTemp = "-10 ~ 70℃",
            string humidity = "20% ~ 80% (非凝结)",
            string altitude = "≤ 2000m")
        {
            AddGroup("环境参数");
            if (!string.IsNullOrEmpty(operatingTemp))
                Add("工作温度", operatingTemp);
            if (!string.IsNullOrEmpty(storageTemp))
                Add("存储温度", storageTemp);
            if (!string.IsNullOrEmpty(humidity))
                Add("相对湿度", humidity);
            if (!string.IsNullOrEmpty(altitude))
                Add("海拔高度", altitude);
            return this;
        }

        /// <summary>
        /// 添加通用保护功能
        /// </summary>
        public DeviceSpecificationBuilder AddCommonProtection(
            bool ovp = true,
            bool ocp = true,
            bool opp = true,
            bool otp = true)
        {
            AddGroup("保护功能");
            if (ovp) Add("过压保护", "OVP");
            if (ocp) Add("过流保护", "OCP");
            if (opp) Add("过功率保护", "OPP");
            if (otp) Add("过温保护", "OTP");
            return this;
        }

        /// <summary>
        /// 添加通信接口（标配）
        /// </summary>
        public DeviceSpecificationBuilder AddCommunicationInterface(
            string usb = null,
            bool lan = false,
            bool rs232 = false,
            bool gpib = false,
            bool can = false,
            bool digitalIO = false,
            string protocol = null)
        {
            AddGroup("通信接口");
            
            if (!string.IsNullOrEmpty(usb))
                Add("USB", $"支持 ({usb})");
            if (lan)
                Add("LAN", "支持 (10/100M自适应)");
            if (rs232)
                Add("RS-232", "支持 (DB9, 9600/8/N/1)");
            if (gpib)
                Add("GPIB", "支持 (IEEE488.2)");
            if (can)
                Add("CAN", "支持 (CAN2.0A/B)");
            if (digitalIO)
                Add("数字I/O", "支持");
            if (!string.IsNullOrEmpty(protocol))
                Add("协议", protocol);
            
            return this;
        }

        /// <summary>
        /// 条件添加（仅当条件为true时添加）
        /// </summary>
        public DeviceSpecificationBuilder AddIf(bool condition, string key, string value, string group = null)
        {
            if (condition)
            {
                Add(key, value, group);
            }
            return this;
        }

        /// <summary>
        /// 条件添加（仅当值不为空时添加）
        /// </summary>
        public DeviceSpecificationBuilder AddIfNotEmpty(string key, string value, string group = null)
        {
            if (!string.IsNullOrEmpty(value))
            {
                Add(key, value, group);
            }
            return this;
        }

        /// <summary>
        /// 构建并返回DeviceSpecification对象
        /// </summary>
        public DeviceSpecification Build()
        {
            return _specification;
        }

        /// <summary>
        /// 隐式转换为DeviceSpecification
        /// </summary>
        public static implicit operator DeviceSpecification(DeviceSpecificationBuilder builder)
        {
            return builder.Build();
        }
    }
}

