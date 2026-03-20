namespace MeasureControl.Constants
{
    /// <summary>
    /// 设备相关常量定义
    /// </summary>
    public static class DeviceConstants
    {
        /// <summary>
        /// 设备状态常量
        /// </summary>
        public static class Status
        {
            /// <summary>
            /// 正常状态
            /// </summary>
            public const string Normal = "正常";

            /// <summary>
            /// 错误状态
            /// </summary>
            public const string Error = "错误";

            /// <summary>
            /// 离线状态
            /// </summary>
            public const string Offline = "离线";

            /// <summary>
            /// 未知状态
            /// </summary>
            public const string Unknown = "未知";

            /// <summary>
            /// 不可用
            /// </summary>
            public const string NA = "N/A";
        }

        /// <summary>
        /// 设备类型常量
        /// </summary>
        public static class Type
        {
            /// <summary>
            /// PXI/PXIe板卡
            /// </summary>
            public const string Card = "Card";

            /// <summary>
            /// 子节点
            /// </summary>
            public const string SubNode = "SubNode";

            /// <summary>
            /// 机箱
            /// </summary>
            public const string Chassis = "Chassis";

            /// <summary>
            /// 仪器
            /// </summary>
            public const string Instrument = "Instrument";

            /// <summary>
            /// 控制器
            /// </summary>
            public const string Controller = "Controller";
        }

        /// <summary>
        /// 默认值常量
        /// </summary>
        public static class Default
        {
            /// <summary>
            /// 默认字符串值
            /// </summary>
            public const string NA = "N/A";

            /// <summary>
            /// 默认未知值
            /// </summary>
            public const string Unknown = "未知";

            /// <summary>
            /// 默认空值
            /// </summary>
            public const string Empty = "";
        }

        /// <summary>
        /// 总线类型常量
        /// </summary>
        public static class BusType
        {
            /// <summary>
            /// PXI总线
            /// </summary>
            public const string PXI = "PXI";

            /// <summary>
            /// PXIe总线
            /// </summary>
            public const string PXIe = "PXIe";

            /// <summary>
            /// 混合槽
            /// </summary>
            public const string Hybrid = "Hybrid";
        }

        /// <summary>
        /// 数据速率单位常量
        /// </summary>
        public static class DataRateUnit
        {
            public const string SamplesPerSecond = "S/s";
            public const string KiloSamplesPerSecond = "kS/s";
            public const string MegaSamplesPerSecond = "MS/s";
            public const string GigaSamplesPerSecond = "GS/s";
        }

        /// <summary>
        /// 制造商品牌常量
        /// </summary>
        public static class Manufacturer
        {
            public static readonly string[] Brands = new[]
            {
                "简仪", "凌华", "欧开", "阿尔泰", "芒果树", "普源", "是德",
                "NI", "Keysight", "Tektronix", "Rohde", "Agilent", "艾德克斯", "Chroma"
            };
        }
    }
}
