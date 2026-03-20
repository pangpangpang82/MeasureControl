using System;

namespace MeasureControl.Models
{
    /// <summary>
    /// 模拟波形类型枚举
    /// </summary>
    public enum SimulationWaveformType
    {
        /// <summary>正弦波</summary>
        Sine,
        /// <summary>方波</summary>
        Square,
        /// <summary>三角波</summary>
        Triangle,
        /// <summary>锯齿波</summary>
        Sawtooth,
        /// <summary>随机噪声</summary>
        Random,
        /// <summary>常数值</summary>
        Constant
    }

    /// <summary>
    /// 模拟数据配置类
    /// 用于配置模拟驱动生成数据的参数
    /// </summary>
    public class SimulationConfig
    {
        /// <summary>
        /// 波形类型
        /// </summary>
        public SimulationWaveformType WaveformType { get; set; }

        /// <summary>
        /// 频率 (Hz)，用于周期波形
        /// </summary>
        public double Frequency { get; set; }

        /// <summary>
        /// 幅度
        /// </summary>
        public double Amplitude { get; set; }

        /// <summary>
        /// 偏移量（直流偏移）
        /// </summary>
        public double Offset { get; set; }

        /// <summary>
        /// 相位 (度)
        /// </summary>
        public double Phase { get; set; }

        /// <summary>
        /// 噪声水平（0-1之间）
        /// </summary>
        public double NoiseLevel { get; set; }

        /// <summary>
        /// 是否启用趋势（漂移）
        /// </summary>
        public bool EnableTrend { get; set; }

        /// <summary>
        /// 趋势速率（每秒变化量）
        /// </summary>
        public double TrendRate { get; set; }

        /// <summary>
        /// 数据最小值（用于限幅）
        /// </summary>
        public double MinValue { get; set; }

        /// <summary>
        /// 数据最大值（用于限幅）
        /// </summary>
        public double MaxValue { get; set; }

        /// <summary>
        /// 采样延迟（毫秒），模拟真实采集延迟
        /// </summary>
        public int SamplingDelay { get; set; }

        /// <summary>
        /// 随机种子（用于可重复的随机数据）
        /// </summary>
        public int? RandomSeed { get; set; }

        /// <summary>
        /// 默认构造函数，使用默认配置
        /// </summary>
        public SimulationConfig()
        {
            WaveformType = SimulationWaveformType.Sine;
            Frequency = 1.0;
            Amplitude = 1.0;
            Offset = 0.0;
            Phase = 0.0;
            NoiseLevel = 0.01;
            EnableTrend = false;
            TrendRate = 0.0;
            MinValue = -10.0;
            MaxValue = 10.0;
            SamplingDelay = 10;
            RandomSeed = null;
        }

        /// <summary>
        /// 创建正弦波配置
        /// </summary>
        public static SimulationConfig CreateSineWave(double frequency = 1.0, double amplitude = 1.0, double offset = 0.0)
        {
            return new SimulationConfig
            {
                WaveformType = SimulationWaveformType.Sine,
                Frequency = frequency,
                Amplitude = amplitude,
                Offset = offset
            };
        }

        /// <summary>
        /// 创建方波配置
        /// </summary>
        public static SimulationConfig CreateSquareWave(double frequency = 1.0, double amplitude = 1.0, double offset = 0.0)
        {
            return new SimulationConfig
            {
                WaveformType = SimulationWaveformType.Square,
                Frequency = frequency,
                Amplitude = amplitude,
                Offset = offset
            };
        }

        /// <summary>
        /// 创建随机噪声配置
        /// </summary>
        public static SimulationConfig CreateRandomNoise(double amplitude = 1.0, double offset = 0.0)
        {
            return new SimulationConfig
            {
                WaveformType = SimulationWaveformType.Random,
                Amplitude = amplitude,
                Offset = offset,
                NoiseLevel = 1.0
            };
        }

        /// <summary>
        /// 创建常数值配置
        /// </summary>
        public static SimulationConfig CreateConstant(double value)
        {
            return new SimulationConfig
            {
                WaveformType = SimulationWaveformType.Constant,
                Amplitude = 0.0,
                Offset = value
            };
        }

        /// <summary>
        /// 克隆配置
        /// </summary>
        public SimulationConfig Clone()
        {
            return new SimulationConfig
            {
                WaveformType = this.WaveformType,
                Frequency = this.Frequency,
                Amplitude = this.Amplitude,
                Offset = this.Offset,
                Phase = this.Phase,
                NoiseLevel = this.NoiseLevel,
                EnableTrend = this.EnableTrend,
                TrendRate = this.TrendRate,
                MinValue = this.MinValue,
                MaxValue = this.MaxValue,
                SamplingDelay = this.SamplingDelay,
                RandomSeed = this.RandomSeed
            };
        }
    }
}

