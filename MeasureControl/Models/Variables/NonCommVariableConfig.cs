using System;

namespace MeasureControl.Models.Variables
{
    public enum VariableSourceType
    {
        Manual,
        Expression,
        ChannelBinding
    }

    public enum VariableDataType
    {
        Double,
        Int,
        Bool,
        Enum,
        String
    }

    public enum WriteMode
    {
        Immediate,
        OnApply
    }

    /// <summary>
    /// 非通讯变量的配置（可持久化）
    /// </summary>
    public sealed class NonCommVariableConfig
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; }
        public string Description { get; set; }
        public VariableDataType DataType { get; set; } = VariableDataType.Double;
        public string Unit { get; set; }
        public double? Min { get; set; }
        public double? Max { get; set; }
        public double? Step { get; set; }
        public object DefaultValue { get; set; }

        public VariableSourceType SourceType { get; set; } = VariableSourceType.Manual;
        public string Expression { get; set; }

        // 绑定到硬件通道时使用
        public string DeviceId { get; set; }
        public string ChannelId { get; set; }
        public double? Gain { get; set; }
        public double? Offset { get; set; }

        public WriteMode WriteMode { get; set; } = WriteMode.OnApply;
        public bool SaveToProject { get; set; } = true;
    }
}

