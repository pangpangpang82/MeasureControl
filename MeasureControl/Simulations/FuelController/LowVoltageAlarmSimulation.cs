using System;
using System.Threading;
using System.Threading.Tasks;
using MeasureControl.Services;

namespace MeasureControl.Simulations.FuelController
{
    /// <summary>
    /// ============================================================================
    /// 低电压告警功能测试仿真类 (LowVoltageAlarmSimulation)
    /// ============================================================================
    /// 
    /// 【功能概述】
    /// 本类用于模拟"低电压告警功能测试"中的硬件操作，包括：
    /// 1. 可调电压供电控制 - 模拟从17V开始以0.2V梯度递减至12V
    /// 2. 矩阵开关控制 - 配置9774板卡AD采集通路
    /// 3. 电平监测 - 模拟监测CRM_PIN3(INT_AD2)的电平状态
    /// 
    /// 【测试背景】
    /// 低电压告警功能测试用于验证加放油控制器在供电电压降低时的告警功能。
    /// 测试时从17V开始，以0.2V梯度递减供电电压，同时监测CRM_PIN3的电平状态。
    /// 当供电电压低于15V之前，CRM_PIN3的电平应发生翻转，表示低电压告警功能正常。
    /// 
    /// 【测量点说明】
    /// - CRM_PIN3: 低电压告警输出信号（对应INT_AD2）
    /// - 通过9774板卡AD采集通道监测电平
    /// 
    /// 【硬件连接】
    /// - 矩阵开关IP: 192.168.1.3
    /// - 9774板卡: 槽位2，AD采集通道（通道38-41对应AD1+/AD1-/AD2+/AD2-）
    /// - 程控电源: 提供可调17V~12V供电
    /// </summary>
    public sealed class LowVoltageAlarmSimulation : IDisposable
    {
        private readonly Random _rand = new Random();
        private readonly SemaphoreSlim _matrixSwitchLock = new SemaphoreSlim(1, 1);
        
        private bool _disposed;
        private bool _powerOn;           // 供电状态
        private bool _matrixConnected;   // 矩阵开关连接状态
        private double _currentVoltage;  // 当前供电电压
        private bool _alarmTriggered;    // 告警是否已触发

        private const string MatrixIpAddress = "192.168.1.3";
        private const int MatrixSlot2601_1 = 4;   // 2601(1) slotindex=4
        private const int MatrixSlot3022_1 = 2;   // 3022(1) slotindx=2
        private const int MatrixTcpBasePort3022 = 50300;

        // 6.3 低电压告警功能测试矩阵映射（截图）
        // CRM_PIN3（DI10）: 2601(1) 4/0  + 3022(1) 0/41
        private static readonly (string In, string Out, int Slot) Matrix2601 = ("I4", "O0", MatrixSlot2601_1);
        private static readonly (string In, string Out, int Slot, int BasePort) Matrix3022 = ("I0", "O41", MatrixSlot3022_1, MatrixTcpBasePort3022);

        /// <summary>
        /// 起始电压（V）
        /// </summary>
        public double StartVoltage { get; set; } = 17.0;

        /// <summary>
        /// 结束电压（V）
        /// </summary>
        public double EndVoltage { get; set; } = 12.0;

        /// <summary>
        /// 电压递减步长（V）
        /// </summary>
        public double VoltageStep { get; set; } = 0.2;

        /// <summary>
        /// 告警触发阈值电压（V）- 电平应在此电压之前翻转
        /// </summary>
        public double AlarmThresholdVoltage { get; set; } = 15.0;

        /// <summary>
        /// 供电是否已开启
        /// </summary>
        public bool IsPowerOn => _powerOn;

        /// <summary>
        /// 矩阵开关是否已连接
        /// </summary>
        public bool IsMatrixConnected => _matrixConnected;

        /// <summary>
        /// 当前供电电压
        /// </summary>
        public double CurrentVoltage => _currentVoltage;

        /// <summary>
        /// 告警是否已触发
        /// </summary>
        public bool IsAlarmTriggered => _alarmTriggered;

        #region 供电控制仿真

        /// <summary>
        /// 模拟设置供电电压
        /// </summary>
        public async Task SetSupplyVoltageAsync(double voltage, Action<string> log, CancellationToken token = default)
        {
            await Task.Delay(100, token);
            _currentVoltage = voltage;
            _powerOn = voltage > 0;
            log?.Invoke($"[SIM] 供电电压已设置为 {voltage:F1}V");
        }

        /// <summary>
        /// 模拟开启供电（初始17V）
        /// </summary>
        public async Task ApplyComponent28VStateAsync(Action<string> log, CancellationToken token = default)
        {
            await Task.Delay(100, token);
            _currentVoltage = StartVoltage;
            _powerOn = true;
            _alarmTriggered = false;
            log?.Invoke($"[SIM] 组件供电已开启，初始电压 {StartVoltage:F1}V");
        }

        /// <summary>
        /// 模拟关闭供电
        /// </summary>
        public async Task ApplyComponentDownStateAsync(Action<string> log, CancellationToken token = default)
        {
            await Task.Delay(50, token);
            _currentVoltage = 0;
            _powerOn = false;
            log?.Invoke("[SIM] 组件供电已关闭");
        }

        #endregion

        #region 矩阵开关仿真

        /// <summary>
        /// 模拟连接矩阵开关并配置9774 AD采集通路
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

                log?.Invoke("[SIM] 正在配置低电压告警矩阵通路（CRM_PIN3）...");
                var svc = MatrixControlService.Instance;

                bool ok2601 = await svc.ConnectNodesAsync(Matrix2601.In, Matrix2601.Out, Matrix2601.Slot, MatrixIpAddress);
                log?.Invoke($"[SIM] 2601(1): {Matrix2601.In}->{Matrix2601.Out} slot={Matrix2601.Slot}, ok={ok2601}");

                bool ok3022 = await svc.ConnectNodesAsync(Matrix3022.In, Matrix3022.Out, Matrix3022.Slot, MatrixIpAddress, Matrix3022.BasePort);
                log?.Invoke($"[SIM] 3022(1): {Matrix3022.In}->{Matrix3022.Out} slot={Matrix3022.Slot}, basePort={Matrix3022.BasePort}, ok={ok3022}");

                _matrixConnected = ok2601 && ok3022;
                return _matrixConnected;
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
                if (!_matrixConnected)
                    return;

                log?.Invoke("[SIM] 正在断开低电压告警矩阵通路...");
                var svc = MatrixControlService.Instance;

                await svc.DisconnectNodesAsync(Matrix2601.In, Matrix2601.Out, Matrix2601.Slot, MatrixIpAddress);
                await svc.DisconnectNodesAsync(Matrix3022.In, Matrix3022.Out, Matrix3022.Slot, MatrixIpAddress, Matrix3022.BasePort);

                _matrixConnected = false;
                log?.Invoke("[SIM] 矩阵开关通路已断开");
            }
            finally
            {
                _matrixSwitchLock.Release();
            }
        }

        #endregion

        #region 电平监测仿真

        /// <summary>
        /// 模拟读取CRM_PIN3(INT_AD2)的电平状态
        /// 返回值：true=高电平，false=低电平
        /// 
        /// 【仿真逻辑】
        /// - 当供电电压大于等于15.5V时，返回初始电平（假设为高电平）
        /// - 当供电电压在15.0V到15.5V之间时，有概率翻转（模拟告警触发点）
        /// - 当供电电压小于15V时，返回翻转后的电平（低电平）
        /// </summary>
        public async Task<bool> ReadPinLevelAsync(CancellationToken token = default)
        {
            await Task.Delay(50, token);

            // 模拟电平翻转逻辑
            if (_currentVoltage >= 15.5)
            {
                // 高于15.5V，保持初始高电平
                return true;
            }
            else if (_currentVoltage >= 15.0)
            {
                // 15.0V ~ 15.5V之间，模拟告警触发点
                // 随机决定是否翻转（模拟实际硬件的不确定性）
                if (!_alarmTriggered && _rand.NextDouble() > 0.3)
                {
                    _alarmTriggered = true;
                }
                return !_alarmTriggered;
            }
            else
            {
                // 低于15V，必定已翻转
                _alarmTriggered = true;
                return false;
            }
        }

        /// <summary>
        /// 模拟读取AD电压值（用于更精确的电平判断）
        /// </summary>
        public async Task<double> ReadAdVoltageAsync(CancellationToken token = default)
        {
            await Task.Delay(100, token);

            // 根据当前供电电压模拟AD读数
            if (_currentVoltage >= 15.5)
            {
                // 高电平，约3.3V
                return 3.3 + (_rand.NextDouble() - 0.5) * 0.2;
            }
            else if (_currentVoltage >= 15.0)
            {
                // 过渡区域
                double ratio = (_currentVoltage - 15.0) / 0.5;
                double baseV = 0.5 + ratio * 2.8;
                return baseV + (_rand.NextDouble() - 0.5) * 0.3;
            }
            else
            {
                // 低电平，约0.3V
                return 0.3 + (_rand.NextDouble() - 0.5) * 0.2;
            }
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _powerOn = false;
            _matrixConnected = false;
            _currentVoltage = 0;
            _alarmTriggered = false;
            _matrixSwitchLock?.Dispose();
        }

        #endregion
    }
}
