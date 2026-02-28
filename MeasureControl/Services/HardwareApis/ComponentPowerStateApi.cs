using System;
using System.Threading;
using System.Threading.Tasks;

namespace MeasureControl.Services.HardwareApis
{
    public enum ComponentPowerState
    {
        ComponentDown = 0,
        Component28VOn = 1
    }

    public interface IComponentPowerStateApi : IAsyncDisposable
    {
        ComponentPowerState CurrentState { get; }

        Task ApplyComponentDownStateAsync(CancellationToken cancellationToken = default);
        Task ApplyComponent28VStateAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// 组件28V供电控制实现。
    /// 硬件映射：程控电源 192.168.1.15 CH1 = 组件28V供电（3A限流）
    /// </summary>
    public sealed class ComponentPowerStateApi : IComponentPowerStateApi
    {
        private const string PowerSupplyIpAddress = "192.168.1.15";
        private const PowerSupplyChannel ComponentChannel = PowerSupplyChannel.CH1;
        private const double ComponentVoltage = 28.0;
        private const double ComponentCurrentLimit = 3.0;

        private bool _disposed;
        private ComponentPowerState _currentState = ComponentPowerState.ComponentDown;
        private IPowerSupplyApi _power;

        public ComponentPowerState CurrentState => _currentState;

        public async Task ApplyComponentDownStateAsync(CancellationToken cancellationToken = default)
        {
            EnsureNotDisposed();

            await EnsurePowerConnectedAsync(cancellationToken).ConfigureAwait(false);
            await _power.SetOutputEnabledAsync(ComponentChannel, false, cancellationToken).ConfigureAwait(false);
            _currentState = ComponentPowerState.ComponentDown;
        }

        public async Task ApplyComponent28VStateAsync(CancellationToken cancellationToken = default)
        {
            EnsureNotDisposed();

            await EnsurePowerConnectedAsync(cancellationToken).ConfigureAwait(false);
            await _power.ApplyAsync(ComponentChannel, ComponentVoltage, ComponentCurrentLimit, cancellationToken).ConfigureAwait(false);
            await _power.SetOutputEnabledAsync(ComponentChannel, true, cancellationToken).ConfigureAwait(false);
            await Task.Delay(300, cancellationToken).ConfigureAwait(false);
            _currentState = ComponentPowerState.Component28VOn;
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;
            if (_power != null)
            {
                try { await _power.SetOutputEnabledAsync(ComponentChannel, false, CancellationToken.None).ConfigureAwait(false); } catch { }
                try { await _power.DisconnectAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                try { await _power.DisposeAsync().ConfigureAwait(false); } catch { }
                _power = null;
            }
        }

        private async Task EnsurePowerConnectedAsync(CancellationToken cancellationToken)
        {
            _power ??= new PowerSupplySocketApi();
            if (!_power.IsConnected)
                await _power.ConnectAsync(PowerSupplyIpAddress, cancellationToken).ConfigureAwait(false);
        }

        private void EnsureNotDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ComponentPowerStateApi));
        }
    }
}
