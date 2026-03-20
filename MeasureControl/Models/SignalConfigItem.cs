using System;
using Newtonsoft.Json;
using Prism.Mvvm;

namespace MeasureControl.Models
{
    /// <summary>
    /// 信号配置项（用于信号配置表视图）
    /// </summary>
    public class SignalConfigItem : BindableBase
    {
        private string _id;
        private int _;
        private string _signalName;
        private string _signalType;
        private string _channelId;
        private string _actualChannel;
        private string _rawValueUnit;
        private string _realTimeValueUnit;
        private double _rawValue;
        private double _realTimeValue;
        private string _remarks;
        private bool _isEmpty;
        private double _slope = 1.0;
        private double _intercept = 0.0;
        private bool _isCalibrated;

        /// <summary>
        /// 变量唯一标识（用于稳定引用，避免依赖名称）
        /// </summary>
        public string Id
        {
            get => _id;
            set => SetProperty(ref _id, value);
        }

        public int Index
        {
            get => _;
            set => SetProperty(ref _, value);
        }

        /// <summary>
        /// 关联的通道Id（新字段，替代 ActualChannel 字符串引用）
        /// </summary>
        public string ChannelId
        {
            get => _channelId;
            set => SetProperty(ref _channelId, value);
        }

        public string SignalName
        {
            get => _signalName;
            set => SetProperty(ref _signalName, value);
        }

        public string SignalType
        {
            get => _signalType;
            set => SetProperty(ref _signalType, value);
        }

        public string ActualChannel
        {
            get => _actualChannel;
            set => SetProperty(ref _actualChannel, value);
        }

        public string RawValueUnit
        {
            get => _rawValueUnit;
            set => SetProperty(ref _rawValueUnit, value);
        }

        public string RealTimeValueUnit
        {
            get => _realTimeValueUnit;
            set => SetProperty(ref _realTimeValueUnit, value);
        }

        /// <summary>运行时原始值（不序列化到配置文件）</summary>
        [JsonIgnore]
        public double RawValue
        {
            get => _rawValue;
            set => SetProperty(ref _rawValue, value);
        }

        /// <summary>运行时实时值（不序列化到配置文件）</summary>
        [JsonIgnore]
        public double RealTimeValue
        {
            get => _realTimeValue;
            set => SetProperty(ref _realTimeValue, value);
        }

        public string Remarks
        {
            get => _remarks;
            set => SetProperty(ref _remarks, value);
        }

        public bool IsEmpty
        {
            get => _isEmpty;
            set => SetProperty(ref _isEmpty, value);
        }

        /// <summary>校准斜率（默认1.0）</summary>
        public double Slope
        {
            get => _slope;
            set => SetProperty(ref _slope, value);
        }

        /// <summary>校准截距（默认0.0）</summary>
        public double Intercept
        {
            get => _intercept;
            set => SetProperty(ref _intercept, value);
        }

        /// <summary>是否已校准</summary>
        public bool IsCalibrated
        {
            get => _isCalibrated;
            set => SetProperty(ref _isCalibrated, value);
        }

        /// <summary>当前值（格式化后的实时值，用于显示）</summary>
        [JsonIgnore]
        public string CurrentValue
        {
            get => RealTimeValue.ToString("F3");
        }

        /// <summary>状态（用于显示通道状态）</summary>
        [JsonIgnore]
        public string Status
        {
            get => "正常"; // 默认状态，可以根据需要扩展
        }

        /// <summary>
        /// 应用校准转换：RealTimeValue = Slope * RawValue + Intercept
        /// </summary>
        public void ApplyCalibration()
        {
            if (SignalType != "数字量")
            {
                RealTimeValue = Slope * RawValue + Intercept;
            }
        }

        /// <summary>
        /// 构造函数 - 自动生成唯一Id
        /// </summary>
        public SignalConfigItem()
        {
            _id = Guid.NewGuid().ToString("N");
        }

        public SignalConfigItem Clone() => new SignalConfigItem
        {
            Id = Id, // 保留原Id用于跟踪
            Index = Index, SignalName = SignalName, SignalType = SignalType, 
            ChannelId = ChannelId, ActualChannel = ActualChannel,
            RawValueUnit = RawValueUnit, RealTimeValueUnit = RealTimeValueUnit, 
            RawValue = RawValue, RealTimeValue = RealTimeValue, 
            Remarks = Remarks, IsEmpty = IsEmpty,
            Slope = Slope, Intercept = Intercept, IsCalibrated = IsCalibrated
        };
    }
}


