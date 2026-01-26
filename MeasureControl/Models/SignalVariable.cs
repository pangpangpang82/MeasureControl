using System;
using Prism.Mvvm;

namespace MeasureControl.Models
{
    /// <summary>
    /// 信号变量（逻辑变量）- ICD配置中的信号定义
    /// </summary>
    public class SignalVariable : BindableBase
    {
        private string _id;
        private string _name;
        private string _description;
        private string _signalType;
        private string _dataType;
        private string _unit;
        private double _minValue;
        private double _maxValue;
        private string _group;
        private string _conversionFormula;
        private double _scale;
        private double _offset;
        private int? _messageId;
        private int? _byteOffset;
        private int? _bitOffset;
        private int? _bitLength;
        private string _endianness;
        private string _direction;

        /// <summary>
        /// 变量唯一标识
        /// </summary>
        public string Id
        {
            get => _id;
            set => SetProperty(ref _id, value);
        }

        /// <summary>
        /// 变量名称（如：Throttle_Angle, Engine_Speed）
        /// </summary>
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        /// <summary>
        /// 描述信息
        /// </summary>
        public string Description
        {
            get => _description;
            set => SetProperty(ref _description, value);
        }

        /// <summary>
        /// 信号类型（Analog/Digital/CAN/ARINC429/1553B/LVDT）
        /// </summary>
        public string SignalType
        {
            get => _signalType;
            set => SetProperty(ref _signalType, value);
        }

        /// <summary>
        /// 数据类型（Double/Int/Bool/Bytes）
        /// </summary>
        public string DataType
        {
            get => _dataType;
            set => SetProperty(ref _dataType, value);
        }

        /// <summary>
        /// 单位（V/A/rpm/°C/mm/%）
        /// </summary>
        public string Unit
        {
            get => _unit;
            set => SetProperty(ref _unit, value);
        }

        /// <summary>
        /// 最小值
        /// </summary>
        public double MinValue
        {
            get => _minValue;
            set => SetProperty(ref _minValue, value);
        }

        /// <summary>
        /// 最大值
        /// </summary>
        public double MaxValue
        {
            get => _maxValue;
            set => SetProperty(ref _maxValue, value);
        }

        /// <summary>
        /// 分组（动力系统/传感器/控制器/通信）
        /// </summary>
        public string Group
        {
            get => _group;
            set => SetProperty(ref _group, value);
        }

        /// <summary>
        /// 换算公式（如：(x+5)/10*100）
        /// </summary>
        public string ConversionFormula
        {
            get => _conversionFormula;
            set => SetProperty(ref _conversionFormula, value);
        }

        /// <summary>
        /// 比例因子
        /// </summary>
        public double Scale
        {
            get => _scale;
            set => SetProperty(ref _scale, value);
        }

        /// <summary>
        /// 偏移量
        /// </summary>
        public double Offset
        {
            get => _offset;
            set => SetProperty(ref _offset, value);
        }

        /// <summary>
        /// 报文ID（CAN/ARINC429特有）
        /// </summary>
        public int? MessageId
        {
            get => _messageId;
            set => SetProperty(ref _messageId, value);
        }

        /// <summary>
        /// 字节偏移
        /// </summary>
        public int? ByteOffset
        {
            get => _byteOffset;
            set => SetProperty(ref _byteOffset, value);
        }

        /// <summary>
        /// 位偏移
        /// </summary>
        public int? BitOffset
        {
            get => _bitOffset;
            set => SetProperty(ref _bitOffset, value);
        }

        /// <summary>
        /// 位长度
        /// </summary>
        public int? BitLength
        {
            get => _bitLength;
            set => SetProperty(ref _bitLength, value);
        }

        /// <summary>
        /// 字节序（Big/Little）
        /// </summary>
        public string Endianness
        {
            get => _endianness;
            set => SetProperty(ref _endianness, value);
        }

        /// <summary>
        /// 方向（Input/Output/Bidirectional）
        /// </summary>
        public string Direction
        {
            get => _direction;
            set => SetProperty(ref _direction, value);
        }

        public SignalVariable()
        {
            Id = Guid.NewGuid().ToString();
            Scale = 1.0;
            Offset = 0.0;
            Endianness = "Big";
            Direction = "Input";
        }

        /// <summary>
        /// 验证信号配置是否正确
        /// </summary>
        public bool ValidateConfiguration()
        {
            if (string.IsNullOrEmpty(Name) || string.IsNullOrEmpty(SignalType))
                return false;

            if (MinValue >= MaxValue)
                return false;

            // CAN/ARINC429信号必须有MessageId
            if ((SignalType == "CAN" || SignalType == "ARINC429") && !MessageId.HasValue)
                return false;

            return true;
        }

        /// <summary>
        /// 将物理值转换为工程值
        /// </summary>
        public double ConvertToEngineering(double physicalValue)
        {
            return physicalValue * Scale + Offset;
        }

        /// <summary>
        /// 将工程值转换为物理值
        /// </summary>
        public double ConvertToPhysical(double engineeringValue)
        {
            return (engineeringValue - Offset) / Scale;
        }

        /// <summary>
        /// 获取完整的信号描述
        /// </summary>
        public string GetFullDescription()
        {
            var desc = $"{Name} ({SignalType})";
            if (!string.IsNullOrEmpty(Unit))
            {
                desc += $" - {MinValue} ~ {MaxValue} {Unit}";
            }
            if (!string.IsNullOrEmpty(Description))
            {
                desc += $"\n{Description}";
            }
            return desc;
        }
    }
}

