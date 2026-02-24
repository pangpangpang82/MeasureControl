using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MeasureControl.Services;

namespace MeasureControl.Simulations.FuelController
{
    public sealed class RS422SelfCheckSimulation : IDisposable
    {
        private readonly SemaphoreSlim _matrixSwitchLock = new SemaphoreSlim(1, 1);
        private bool _disposed;
        private bool _matrixConnected;

        private const string MatrixIpAddress = "192.168.1.3";
        private const int MatrixSlotRs422 = 7;

        public async Task SetPinsToLowAsync(string[] pinNames, Action<string> log, CancellationToken token = default)
        {
            if (pinNames == null || pinNames.Length == 0)
                return;

            await Task.Delay(80, token);
            log?.Invoke($"[SIM] 设置引脚为0: {string.Join(",", pinNames)}");
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

                log?.Invoke("[SIM] 正在配置矩阵开关通路(RS422自检占位)...");
                bool ok = await MatrixControlService.Instance.ConnectNodesAsync("I3", "O30", MatrixSlotRs422, MatrixIpAddress);
                log?.Invoke($"[SIM] 矩阵开关通路(RS422自检): I3->O30 slot={MatrixSlotRs422} ip={MatrixIpAddress}, ok={ok}");

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

        public async Task DisconnectMatrixAsync(Action<string> log, CancellationToken token = default)
        {
            await _matrixSwitchLock.WaitAsync(token);
            try
            {
                if (!_matrixConnected)
                    return;

                log?.Invoke("[SIM] 正在断开矩阵开关通路...");

                bool ok = await MatrixControlService.Instance.DisconnectNodesAsync("I3", "O30", MatrixSlotRs422, MatrixIpAddress);
                log?.Invoke($"[SIM] 矩阵开关断开(RS422自检): I3->O30 slot={MatrixSlotRs422}, ok={ok}");

                _matrixConnected = false;
                log?.Invoke("[SIM] 矩阵开关通路已断开");
            }
            finally
            {
                _matrixSwitchLock.Release();
            }
        }

        public async Task<byte[]> SendAndReceiveAsync(string stepName, string txPinName, string rxPinName, byte[] txData, Action<string> log, CancellationToken token = default)
        {
            if (txData == null)
                txData = Array.Empty<byte>();

            await Task.Delay(120, token);

            var hex = string.Join(" ", txData.Select(b => b.ToString("X2")));
            log?.Invoke($"[SIM] {stepName}: {txPinName} 发送 0x{hex}");

            await Task.Delay(120, token);

            var rx = txData.ToArray();
            log?.Invoke($"[SIM] {stepName}: {rxPinName} 回读 0x{hex}");
            return rx;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _matrixConnected = false;
            _matrixSwitchLock?.Dispose();
        }
    }
}
