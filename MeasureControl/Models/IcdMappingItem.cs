using Prism.Mvvm;

namespace MeasureControl.Models
{
    /// <summary>
    /// ICD映射项
    /// </summary>
    public class IcdMappingItem : BindableBase
    {
        private string _signalId;
        private string _description;
        private string _icdTabelId;
        private string _frameId;
        private string _dataType;
        private int _bitLength;
        private string _direction;
        private string _cycle;
        private string _messageId;
        private string _channel;
        private int _dlc;
        private string _calibrationFormula;
        private int _concatCount;
        private int _doubleWordBits;
        private int _positionInWord;

        /// <summary>
        /// 信号标识
        /// </summary>
        public string SignalId
        {
            get => _signalId;
            set => SetProperty(ref _signalId, value);
        }

        /// <summary>
        /// 信号说明
        /// </summary>
        public string Description
        {
            get => _description;
            set => SetProperty(ref _description, value);
        }

        /// <summary>
        /// 绑定ICD配置表ID
        /// </summary>
        public string IcdTabelId
        {
            get => _icdTabelId;
            set => SetProperty(ref _icdTabelId, value);
        }

        /// <summary>
        /// 绑定帧ID
        /// </summary>
        public string FrameId
        {
            get => _frameId;
            set => SetProperty(ref _frameId, value);
        }

        /// <summary>
        /// 数据类型
        /// </summary>
        public string DataType
        {
            get => _dataType;
            set => SetProperty(ref _dataType, value);
        }

        /// <summary>
        /// 位长
        /// </summary>
        public int BitLength
        {
            get => _bitLength;
            set => SetProperty(ref _bitLength, value);
        }

        /// <summary>
        /// 信号方向
        /// </summary>
        public string Direction
        {
            get => _direction;
            set => SetProperty(ref _direction, value);
        }

        /// <summary>
        /// 周期
        /// </summary>
        public string Cycle
        {
            get => _cycle;
            set => SetProperty(ref _cycle, value);
        }

        /// <summary>
        /// 消息ID（十六进制格式）
        /// </summary>
        public string MessageId
        {
            get => _messageId;
            set => SetProperty(ref _messageId, value);
        }

        /// <summary>
        /// 通道
        /// </summary>
        public string Channel
        {
            get => _channel;
            set => SetProperty(ref _channel, value);
        }

        /// <summary>
        /// 数据包总大小（DLC）
        /// </summary>
        public int Dlc
        {
            get => _dlc;
            set => SetProperty(ref _dlc, value);
        }

        /// <summary>
        /// 标定公式
        /// </summary>
        public string CalibrationFormula
        {
            get => _calibrationFormula;
            set => SetProperty(ref _calibrationFormula, value);
        }

        /// <summary>
        /// 拼接个数
        /// </summary>
        public int ConcatCount
        {
            get => _concatCount;
            set => SetProperty(ref _concatCount, value);
        }

        /// <summary>
        /// 双字位数
        /// </summary>
        public int DoubleWordBits
        {
            get => _doubleWordBits;
            set => SetProperty(ref _doubleWordBits, value);
        }

        /// <summary>
        /// 字内位置
        /// </summary>
        public int PositionInWord
        {
            get => _positionInWord;
            set => SetProperty(ref _positionInWord, value);
        }

        /// <summary>
        /// 默认构造函数
        /// </summary>
        public IcdMappingItem()
        {
            CalibrationFormula = "/";
            ConcatCount = 0;
            DoubleWordBits = 0;
            PositionInWord = 0;
        }
    }
}