using System;
using System.Threading;
using System.Threading.Tasks;
using MeasureControl.Services;

namespace MeasureControl.Simulations.FuelController
{
    /// <summary>
    /// ============================================================================
    /// 温度采集功能测试仿真类 (TemperatureAcquisitionSimulation)
    /// ============================================================================
    /// 
    /// 【功能概述】
    /// 本类用于模拟"温度采集功能"测试中的硬件操作，包括：
    /// 1. 28V供电控制 - 模拟组件28V供电状态
    /// 2. 矩阵开关控制 - 配置测试通路
    /// 3. 温度采集 - 模拟DS18B20U+T&amp;R温度传感器信号解析
    /// 
    /// 【测试背景】
    /// 温度采集功能测试用于验证加放油控制器的温度采集功能是否正常。
    /// 组件28V供电状态下，按照DS18B20U+T&amp;R规格书解析CRM_PIN7的信号，
    /// 提示并记录温度值。
    /// 
    /// 【测量点说明】
    /// - CRM_PIN7: POWER_TEMP（温度传感器信号）
    /// - 信号通过IO57连接到INT_IO57（D35）
    /// 
    /// 【判定标准】
    /// 温度值处于[15℃, 45℃]区间内表示合格（PASS）
    /// 
    /// 【硬件连接】
    /// - 矩阵开关IP: 192.168.1.3
    /// - IO57 -> INT_IO57 (D35, 2槽179通道)
    /// </summary>
    public sealed class TemperatureAcquisitionSimulation : IDisposable
    {
        private readonly Random _rand = new Random();
        private readonly SemaphoreSlim _matrixSwitchLock = new SemaphoreSlim(1, 1);
        
        private bool _disposed;
        private bool _powerOn;           // 28V供电状态
        private bool _matrixConnected;   // 矩阵开关连接状态

        private const string MatrixIpAddress = "192.168.1.3";
        private const int MatrixSlotTemp = 7;

        /// <summary>
        /// 温度判定下限（℃）
        /// </summary>
        public double TemperatureLowerLimit { get; set; } = 15.0;

        /// <summary>
        /// 温度判定上限（℃）
        /// </summary>
        public double TemperatureUpperLimit { get; set; } = 45.0;

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
        /// 模拟开启28V供电（组件28V供电状态）
        /// </summary>
        public async Task ApplyComponent28VStateAsync(Action<string> log, CancellationToken token = default)
        {
            await Task.Delay(100, token);
            _powerOn = true;
            log?.Invoke("[SIM] 组件28V供电状态已设置");
        }

        /// <summary>
        /// 模拟关闭28V供电
        /// </summary>
        public async Task ApplyComponentDownStateAsync(Action<string> log, CancellationToken token = default)
        {
            await Task.Delay(50, token);
            _powerOn = false;
            log?.Invoke("[SIM] 组件下电状态已设置");
        }

        public Task SimulatePowerOnAsync(Action<string> log, CancellationToken token = default)
        {
            return ApplyComponent28VStateAsync(log, token);
        }

        public Task SimulatePowerOffAsync(Action<string> log, CancellationToken token = default)
        {
            return ApplyComponentDownStateAsync(log, token);
        }

        #endregion

        #region 矩阵开关仿真

        /// <summary>
        /// 模拟连接矩阵开关并配置温度采集通路
        /// IO57 -> INT_IO57
        /// </summary>
        public async Task<bool> ConnectMatrixAsync(Action<string> log, CancellationToken token = default)
        {
            if (_matrixConnected)
            {
                log?.Invoke("[SIM] 矩阵开关已连接，跳过");
                return true;
            }

            await _matrixSwitchLock.WaitAsync(token);
            try
            {
                if (_matrixConnected)
                    return true;

                log?.Invoke("[SIM] 正在配置矩阵开关通路（温度采集）...");

                // TODO: 根据实际硬件配置调整矩阵开关通路
                // IO57 -> INT_IO57 (D35)
                bool ok = await MatrixControlService.Instance.ConnectNodesAsync("I7", "O35", MatrixSlotTemp, MatrixIpAddress);
                log?.Invoke($"[SIM] 矩阵开关通路(TEMP): I7->O35 slot={MatrixSlotTemp} ip={MatrixIpAddress}, ok={ok}");

                _matrixConnected = ok;
                if (ok)
                {
                    log?.Invoke("[SIM] 矩阵开关通路配置完成");
                    return true;
                }

                log?.Invoke("[SIM] 矩阵开关通路配置失败");
                return false;
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

                bool ok = await MatrixControlService.Instance.DisconnectNodesAsync("I7", "O35", MatrixSlotTemp, MatrixIpAddress);
                log?.Invoke($"[SIM] 矩阵开关断开(TEMP): I7->O35 slot={MatrixSlotTemp}, ok={ok}");

                _matrixConnected = false;
                log?.Invoke("[SIM] 矩阵开关通路已断开");
            }
            finally
            {
                _matrixSwitchLock.Release();
            }
        }

        #endregion

        #region 温度采集仿真

        /// <summary>
        /// 模拟DS18B20U+T&amp;R温度传感器读取
        /// 返回一个在正常范围内的模拟温度值（约25℃左右）
        /// </summary>
        public async Task<double> SimulateReadTemperatureAsync(CancellationToken token = default)
        {
            await Task.Delay(500, token);

            // 模拟正常的室温
            // 基准值25℃，加上±5℃的随机波动
            double baseTemperature = 25.0;
            double noise = (_rand.NextDouble() - 0.5) * 10.0; // -5℃ ~ +5℃
            double temperature = baseTemperature + noise;

            // 确保在合理范围内
            temperature = Math.Max(10.0, Math.Min(50.0, temperature));

            return Math.Round(temperature, 1);
        }

        /// <summary>
        /// 模拟解析DS18B20温度传感器原始数据
        /// </summary>
        public async Task<double> SimulateParseDS18B20DataAsync(byte[] rawData, CancellationToken token = default)
        {
            await Task.Delay(100, token);
            
            // 模拟解析，实际实现需要按照DS18B20规格书解析
            return await SimulateReadTemperatureAsync(token);
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
