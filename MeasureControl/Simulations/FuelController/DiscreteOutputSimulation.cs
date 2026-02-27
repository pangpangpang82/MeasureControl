using System;
using System.Threading;
using System.Threading.Tasks;
using MeasureControl.Services;

namespace MeasureControl.Simulations.FuelController
{
    public sealed class DiscreteOutputSimulation : IDisposable
    {
        private readonly Random _rand = new Random();
        private readonly SemaphoreSlim _matrixSwitchLock = new SemaphoreSlim(1, 1);

        private bool _disposed;
        private bool _matrixConnected;
        private bool _component28vOn;
        private bool _doGrounded;

        private const string MatrixIpAddress = "192.168.1.3";
        private const int MatrixSlotDmm = 7;

        public async Task ApplyComponentDownStateAsync(Action<string> log, CancellationToken token = default)
        {
            await Task.Delay(60, token);
            _component28vOn = false;
            log?.Invoke("[SIM] 组件下电状态已设置");
        }

        public async Task ApplyComponent28VStateAsync(Action<string> log, CancellationToken token = default)
        {
            await Task.Delay(120, token);
            _component28vOn = true;
            log?.Invoke("[SIM] 组件28V供电状态已设置");
        }

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

                log?.Invoke("[SIM] 正在配置矩阵开关通路...");
                bool ok = await MatrixControlService.Instance.ConnectNodesAsync("I3", "O30", MatrixSlotDmm, MatrixIpAddress);
                log?.Invoke($"[SIM] 矩阵开关通路(DMM): I3->O30 slot={MatrixSlotDmm} ip={MatrixIpAddress}, ok={ok}");
                _matrixConnected = ok;
                return ok;
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

        public async Task DisconnectMatrixAsync(Action<string> log, CancellationToken token = default)
        {
            await _matrixSwitchLock.WaitAsync(token);
            try
            {
                if (!_matrixConnected)
                    return;

                log?.Invoke("[SIM] 正在断开矩阵开关通路...");
                bool ok = await MatrixControlService.Instance.DisconnectNodesAsync("I3", "O30", MatrixSlotDmm, MatrixIpAddress);
                log?.Invoke($"[SIM] 矩阵开关断开(DMM): I3->O30 slot={MatrixSlotDmm}, ok={ok}");
                _matrixConnected = false;
                log?.Invoke("[SIM] 矩阵开关通路已断开");
            }
            finally
            {
                _matrixSwitchLock.Release();
            }
        }

        public async Task SetDoGroundedAsync(Action<string> log, CancellationToken token = default)
        {
            await Task.Delay(80, token);
            _doGrounded = true;
            log?.Invoke("[SIM] DO已设置为接地/低电平(模拟0~2V输入)");
        }

        public async Task SetDoOpenAsync(Action<string> log, CancellationToken token = default)
        {
            await Task.Delay(80, token);
            _doGrounded = false;
            log?.Invoke("[SIM] DO已设置为开路");
        }

        public async Task<double> MeasureImpedanceToGroundAsync(Action<string> log, CancellationToken token = default)
        {
            await Task.Delay(300, token);

            if (_component28vOn)
            {
                log?.Invoke("[SIM] 当前为上电状态，阻抗测量仅用于占位仿真");
            }

            if (_doGrounded)
            {
                double v = 3.0 + _rand.NextDouble() * 4.0;
                return Math.Round(v, 3);
            }

            double high = 120000.0 + _rand.NextDouble() * 80000.0;
            return Math.Round(high, 0);
        }

        public async Task<double> MeasureJ14VoltageAsync(Action<string> log, CancellationToken token = default)
        {
            await Task.Delay(300, token);

            if (!_component28vOn)
            {
                log?.Invoke("[SIM] 当前为下电状态，电压测量将返回0(占位仿真)");
                return 0.0;
            }

            double v = 24.0 + (_rand.NextDouble() - 0.5) * 4.0;
            v = Math.Max(0.0, Math.Min(32.0, v));
            return Math.Round(v, 3);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _matrixConnected = false;
            _component28vOn = false;
            _matrixSwitchLock?.Dispose();
        }
    }
}
