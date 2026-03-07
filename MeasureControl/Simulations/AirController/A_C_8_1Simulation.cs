using System;
using System.Threading;
using System.Threading.Tasks;

namespace MeasureControl.Simulations.AirController
{
    public sealed class A_C_8_1Simulation : IDisposable
    {
        private readonly Random _rand = new Random();
        private bool _disposed;
        private bool _relayActivated;
        private bool _componentDown;

        public double ImpedanceThreshold { get; set; } = 200.0;

        public async Task ApplyComponentDownStateAsync(Action<string> log, CancellationToken token = default)
        {
            await Task.Delay(50, token);
            _componentDown = true;
            log?.Invoke("[SIM] 组件下电状态已设置");
        }

        public async Task SimulateRelayActivateAsync(CancellationToken token = default)
        {
            await Task.Delay(100, token);
            _relayActivated = true;
        }

        public async Task SimulateRelayDeactivateAsync(CancellationToken token = default)
        {
            await Task.Delay(100, token);
            _relayActivated = false;
        }

        public async Task<double> SimulateMeasureResistanceAsync(string testPoint, CancellationToken token = default)
        {
            await Task.Delay(300, token);

            if (!_componentDown)
            {
                return _rand.NextDouble() * 50 + 20;
            }

            if (!_relayActivated)
            {
                return _rand.NextDouble() * 120 + 50;
            }

            double baseValue = testPoint switch
            {
                "A" => 800.0,
                "B" => 600.0,
                "C" => 650.0,
                "D" => 500.0,
                "E" => 450.0,
                "F" => 420.0,
                _ => 500.0
            };

            double noise = (_rand.NextDouble() - 0.5) * 120;
            return Math.Max(0, baseValue + noise);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _relayActivated = false;
            _componentDown = false;
        }
    }
}
