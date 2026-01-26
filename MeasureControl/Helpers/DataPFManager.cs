using System;
using System.Collections.Generic;
using System.Linq;

namespace MeasureControl.Helpers
{
    /// <summary>
    /// 数据处理器
    /// 处理物理层标定：对采集的原始数据应用标定系数进行信号调理
    /// </summary>
    public class DataProcessor
        {
            /// <summary>
            /// 处理通道数据：应用标定转换
            /// CalibratedValue = Slope * RawValue + Intercept
            /// </summary>
            /// <param name="channelId">通道ID（如"AI0", "AI1"等）</param>
            /// <param name="rawValue">原始采集值</param>
            /// <returns>标定后的值</returns>
            public static double ProcessChannelData(string channelId, double rawValue)
            {
                return ProcessChannelData(string.Empty, channelId, rawValue);
            }

            /// <summary>
            /// 处理通道数据：应用标定转换（按板卡隔离）
            /// CalibratedValue = Slope * RawValue + Intercept
            /// </summary>
            /// <param name="deviceId">设备ID（板卡Id）</param>
            /// <param name="channelId">通道ID（如"AI0", "AI1"等）</param>
            /// <param name="rawValue">原始采集值</param>
            /// <returns>标定后的值</returns>
            public static double ProcessChannelData(string deviceId, string channelId, double rawValue)
            {
                if (string.IsNullOrEmpty(channelId))
                    return rawValue;

                var scopedKey = string.IsNullOrWhiteSpace(deviceId)
                    ? channelId
                    : $"{deviceId}/{channelId}";

                // 获取标定参数
                (double slope, double intercept, bool isCalibrated) = MeasureControl.Services.CalibrationService.Instance.GetCalibrationParams(scopedKey);

                // 如果未标定，直接返回原始值
                if (!isCalibrated)
                    return rawValue;

                // 应用标定公式
                return slope * rawValue + intercept;
            }

            /// <summary>
            /// 批量处理多个通道的数据
            /// </summary>
            /// <param name="channelData">通道ID与原始值的字典</param>
            /// <returns>通道ID与标定后值的字典</returns>
            public static Dictionary<string, double> ProcessChannelsData(Dictionary<string, double> channelData)
            {
                if (channelData == null || channelData.Count == 0)
                    return new Dictionary<string, double>();

                var result = new Dictionary<string, double>();

                foreach (var kvp in channelData)
                {
                    result[kvp.Key] = ProcessChannelData(kvp.Key, kvp.Value);
                }

                return result;
            }
        }

        /// <summary>
        /// 数据滤波管理器
        /// 提供多种滤波算法，用于处理实时采集的数据
        /// </summary>
        public class DataFilterManager
    {
        /// <summary>
        /// 滤波算法类型
        /// 通过修改代码中的 FilterType 来选择使用哪种滤波方式
        /// </summary>
        public enum FilterType
        {
            /// <summary>
            /// 移动平均滤波：滑动窗口平均
            /// </summary>
            MovingAverage,

            /// <summary>
            /// 中位数滤波：取中位数
            /// </summary>
            Median,

            /// <summary>
            /// 指数移动平均（EMA）：加权平均
            /// </summary>
            ExponentialMovingAverage
        }

        // ========== 配置区域：修改这里来选择滤波方式 ==========
        /// <summary>
        /// 当前使用的滤波类型
        /// 修改此值来选择不同的滤波算法
        /// </summary>
        private static FilterType CurrentFilterType = FilterType.MovingAverage;

        /// <summary>
        /// 移动平均滤波窗口大小（样本数）
        /// </summary>
        private const int MovingAverageWindowSize = 10;

        /// <summary>
        /// 中位数滤波窗口大小（样本数）
        /// </summary>
        private const int MedianWindowSize = 10;

        /// <summary>
        /// EMA滤波的平滑系数 α (0-1)
        /// α 越大，对最新数据的权重越大
        /// </summary>
        private const double EmaAlpha = 0.3;
        // ======================================================

        /// <summary>
        /// 每个通道的数据缓冲区
        /// </summary>
        private readonly Dictionary<string, Queue<double>> _channelBuffers = new Dictionary<string, Queue<double>>();

        /// <summary>
        /// 每个通道的EMA上一次值
        /// </summary>
        private readonly Dictionary<string, double?> _channelEmaValues = new Dictionary<string, double?>();

        /// <summary>
        /// 计算滤波后的值
        /// </summary>
        /// <param name="channelId">通道ID</param>
        /// <param name="rawValue">原始值</param>
        /// <returns>滤波后的值</returns>
        public double Filter(string channelId, double rawValue)
        {
            if (string.IsNullOrEmpty(channelId))
                return rawValue;

#pragma warning disable CS0162 // 无法访问的代码 - 这是设计上的选择，保留 switch 以便将来修改 CurrentFilterType
            switch (CurrentFilterType)
            {
                case FilterType.MovingAverage:
                    return MovingAverageFilter(channelId, rawValue);

                case FilterType.Median:
                    return MedianFilter(channelId, rawValue);

                case FilterType.ExponentialMovingAverage:
                    return ExponentialMovingAverageFilter(channelId, rawValue);

                default:
                    return rawValue;
            }
#pragma warning restore CS0162
        }

        /// <summary>
        /// 移动平均滤波
        /// 使用滑动窗口计算平均值
        /// </summary>
        private double MovingAverageFilter(string channelId, double rawValue)
        {
            if (!_channelBuffers.ContainsKey(channelId))
            {
                _channelBuffers[channelId] = new Queue<double>();
            }

            var buffer = _channelBuffers[channelId];

            // 添加新值
            buffer.Enqueue(rawValue);

            // 保持窗口大小
            while (buffer.Count > MovingAverageWindowSize)
            {
                buffer.Dequeue();
            }

            // 计算平均值
            if (buffer.Count == 0)
                return rawValue;

            return buffer.Average();
        }

        /// <summary>
        /// 中位数滤波
        /// 取窗口内的中位数
        /// </summary>
        private double MedianFilter(string channelId, double rawValue)
        {
            if (!_channelBuffers.ContainsKey(channelId))
            {
                _channelBuffers[channelId] = new Queue<double>();
            }

            var buffer = _channelBuffers[channelId];

            // 添加新值
            buffer.Enqueue(rawValue);

            // 保持窗口大小
            while (buffer.Count > MedianWindowSize)
            {
                buffer.Dequeue();
            }

            // 计算中位数
            if (buffer.Count == 0)
                return rawValue;

            var sorted = buffer.OrderBy(x => x).ToArray();
            int mid = sorted.Length / 2;

            if (sorted.Length % 2 == 0)
            {
                // 偶数个元素，取中间两个的平均值
                return (sorted[mid - 1] + sorted[mid]) / 2.0;
            }
            else
            {
                // 奇数个元素，取中间值
                return sorted[mid];
            }
        }

        /// <summary>
        /// 指数移动平均滤波（EMA）
        /// 使用加权平均，对最新数据给予更高权重
        /// </summary>
        private double ExponentialMovingAverageFilter(string channelId, double rawValue)
        {
            if (!_channelEmaValues.ContainsKey(channelId))
            {
                // 第一次，直接使用原始值
                _channelEmaValues[channelId] = rawValue;
                return rawValue;
            }

            double previousEma = _channelEmaValues[channelId].Value;
            
            // EMA公式：EMA = α * 新值 + (1 - α) * 上一次EMA
            double newEma = EmaAlpha * rawValue + (1 - EmaAlpha) * previousEma;
            
            _channelEmaValues[channelId] = newEma;
            return newEma;
        }

        /// <summary>
        /// 清除指定通道的滤波缓冲区
        /// </summary>
        public void ClearChannel(string channelId)
        {
            if (_channelBuffers.ContainsKey(channelId))
            {
                _channelBuffers[channelId].Clear();
            }

            if (_channelEmaValues.ContainsKey(channelId))
            {
                _channelEmaValues[channelId] = null;
            }
        }

        /// <summary>
        /// 清除所有通道的滤波缓冲区
        /// </summary>
        public void ClearAll()
        {
            _channelBuffers.Clear();
            _channelEmaValues.Clear();
        }

        /// <summary>
        /// 获取当前使用的滤波类型名称
        /// </summary>
        public string GetCurrentFilterTypeName()
        {
            return CurrentFilterType switch
            {
                FilterType.MovingAverage => $"移动平均（窗口={MovingAverageWindowSize}）",
                FilterType.Median => $"中位数滤波（窗口={MedianWindowSize}）",
                FilterType.ExponentialMovingAverage => $"指数移动平均（α={EmaAlpha}）",
                _ => "无滤波"
            };
        }
    }
}

