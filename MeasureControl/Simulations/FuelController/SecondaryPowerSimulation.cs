using System;
using System.Threading;
using System.Threading.Tasks;
using MeasureControl.Services;

namespace MeasureControl.Simulations.FuelController
{
    /// <summary>
    /// ============================================================================
    /// 二次电源测试仿真类 (SecondaryPowerSimulation)
    /// ============================================================================
    /// 
    /// 【功能概述】
    /// 本类用于模拟"二次电源测试"中的硬件操作，包括：
    /// 1. 28V供电控制 - 模拟通过J3和J4提供28V供电
    /// 2. 矩阵开关控制 - 配置测试通路（万用表通路）
    /// 3. 电压测量 - 模拟万用表读取直流电压值
    /// 
    /// 【测试背景】
    /// 二次电源测试用于验证加放油控制器的+5V电源输出是否正常。
    /// 测试时需要给组件提供28V供电（通过J3和J4），继电器保持NC状态。
    /// 然后测量CRM_PIN1（+5V）对CRM_PIN18（GND）之间的电压。
    /// 电压值在[4.5V, 5.5V]区间内表示合格（PASS）。
    /// 
    /// 【测量点说明】
    /// - CRM_PIN1: +5V电源输出（电源板组件向导光板组件提供电源）
    /// - CRM_PIN18: GND（地）
    /// 
    /// 【硬件连接】
    /// - 矩阵开关IP: 192.168.1.3
    /// - 万用表通路: 用于直流电压测量
    /// - 供电: J3和J4提供28V，继电器不动作（NC状态）
    /// </summary>
    public sealed class SecondaryPowerSimulation : IDisposable
    {
        private readonly Random _rand = new Random();
        private readonly SemaphoreSlim _matrixSwitchLock = new SemaphoreSlim(1, 1);
        
        private bool _disposed;
        private bool _powerOn;           // 28V供电状态
        private bool _matrixConnected;   // 矩阵开关连接状态

        private const string MatrixIpAddress = "192.168.1.3";
        private const int MatrixSlotDmm = 7;

        /// <summary>
        /// 电压判定下限（V）
        /// </summary>
        public double VoltageLowerLimit { get; set; } = 4.5;

        /// <summary>
        /// 电压判定上限（V）
        /// </summary>
        public double VoltageUpperLimit { get; set; } = 5.5;

        /// <summary>
        /// 28V供电是否已开启
        /// </summary>
        public bool IsPowerOn => _powerOn;

        /// <summary>
        /// 矩阵开关是否已连接
        /// </summary>
        public bool IsMatrixConnected => _matrixConnected;

        #region 供电控制仿真

        /// <summary>
        /// 模拟开启28V供电（通过J3和J4）
        /// </summary>
        public async Task SimulatePowerOnAsync(Action<string> log, CancellationToken token = default)
        {
            await Task.Delay(100, token);
            _powerOn = true;
            log?.Invoke("[SIM] 28V供电已开启（J3-J4）");
        }

        /// <summary>
        /// 模拟关闭28V供电
        /// </summary>
        public async Task SimulatePowerOffAsync(Action<string> log, CancellationToken token = default)
        {
            await Task.Delay(50, token);
            _powerOn = false;
            log?.Invoke("[SIM] 28V供电已关闭");
        }

        #endregion

        #region 矩阵开关仿真

        /// <summary>
        /// 模拟连接矩阵开关并配置万用表通路
        /// </summary>
        public async Task<bool> ConnectMatrixAsync(Action<string> log, CancellationToken token = default)
        {
            await _matrixSwitchLock.WaitAsync(token);
            try
            {
                log?.Invoke($"[SIM] 正在配置矩阵开关通路...");
                await Task.Delay(100, token);

                // 模拟配置万用表通路
                log?.Invoke($"[SIM] 矩阵开关通路(DMM): slot={MatrixSlotDmm}, ip={MatrixIpAddress}");
                await Task.Delay(50, token);

                _matrixConnected = true;
                log?.Invoke("[SIM] 矩阵开关通路配置完成");
                return true;
            }
            catch (Exception ex)
            {
                log?.Invoke($"[SIM] 矩阵开关配置失败: {ex.Message}");
                return false;
            }
            finally
            {
                _matrixSwitchLock.Release();
            }
        }

        /// <summary>
        /// 模拟断开矩阵开关
        /// </summary>
        public async Task DisconnectMatrixAsync(Action<string> log, CancellationToken token = default)
        {
            await _matrixSwitchLock.WaitAsync(token);
            try
            {
                log?.Invoke("[SIM] 正在断开矩阵开关通路...");
                await Task.Delay(50, token);
                _matrixConnected = false;
                log?.Invoke("[SIM] 矩阵开关通路已断开");
            }
            finally
            {
                _matrixSwitchLock.Release();
            }
        }

        #endregion

        #region 电压测量仿真

        /// <summary>
        /// 模拟万用表测量直流电压
        /// 返回一个在正常范围内的模拟电压值（约5V左右）
        /// </summary>
        public async Task<double> SimulateMeasureVoltageAsync(CancellationToken token = default)
        {
            await Task.Delay(300, token);

            // 模拟正常的+5V电源输出
            // 基准值5.0V，加上±0.3V的随机波动
            double baseVoltage = 5.0;
            double noise = (_rand.NextDouble() - 0.5) * 0.6; // -0.3V ~ +0.3V
            double voltage = baseVoltage + noise;

            // 确保在合理范围内
            voltage = Math.Max(4.0, Math.Min(6.0, voltage));

            return Math.Round(voltage, 3);
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _powerOn = false;
            _matrixConnected = false;
            _matrixSwitchLock?.Dispose();
        }

        #endregion
    }
}
