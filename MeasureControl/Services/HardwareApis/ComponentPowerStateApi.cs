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

    public sealed class ComponentPowerStateApi : IComponentPowerStateApi
    {
        private bool _disposed;
        private ComponentPowerState _currentState = ComponentPowerState.ComponentDown;

        public ComponentPowerState CurrentState => _currentState;

        public async Task ApplyComponentDownStateAsync(CancellationToken cancellationToken = default)
        {
            EnsureNotDisposed();

            if (_currentState == ComponentPowerState.ComponentDown)
                return;

            await Task.Delay(80, cancellationToken).ConfigureAwait(false);
            _currentState = ComponentPowerState.ComponentDown;
        }

        public async Task ApplyComponent28VStateAsync(CancellationToken cancellationToken = default)
        {
            EnsureNotDisposed();

            if (_currentState == ComponentPowerState.Component28VOn)
                return;

            await Task.Delay(120, cancellationToken).ConfigureAwait(false);
            _currentState = ComponentPowerState.Component28VOn;
        }

        public ValueTask DisposeAsync()
        {
            _disposed = true;
            return default;
        }

        private void EnsureNotDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ComponentPowerStateApi));
        }
    }
}
