using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MeasureControl.Simulations.FuelController
{
    public sealed class RS422SelfCheckSimulation : IDisposable
    {
        private readonly SemaphoreSlim _matrixSwitchLock = new SemaphoreSlim(1, 1);
        private bool _disposed;
        private bool _matrixConnected;


        public async Task SetPinsToLowAsync(string[] pinNames, Action<string> log, CancellationToken token = default)
        {
            if (pinNames == null || pinNames.Length == 0)
                return;

            await Task.Delay(80, token);
            log?.Invoke($"[SIM] 设置引脚为0: {string.Join(",", pinNames)}");
        }

        public async Task<bool> ConnectMatrixAsync(Action<string> log, CancellationToken token = default)
        {
            await Task.Delay(30, token);
            _matrixConnected = true;
            log?.Invoke("[SIM] 矩阵开关通路已配置（仿真）");
            return true;
        }

        public async Task DisconnectMatrixAsync(Action<string> log, CancellationToken token = default)
        {
            await Task.Delay(20, token);
            _matrixConnected = false;
            log?.Invoke("[SIM] 矩阵开关通路已断开（仿真）");
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
