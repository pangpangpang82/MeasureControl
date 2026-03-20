using System;
using System.Threading;
using System.Threading.Tasks;

namespace MeasureControl.Services.HardwareApis
{
    public enum ComponentPowerState
    {
        ComponentDown = 0,
        Component28VOn = 1,
        RelayPowerOn = 2,
        FullPowerOn = 3
    }

    public interface IComponentPowerStateApi : IAsyncDisposable
    {
        ComponentPowerState CurrentState { get; }

        Task ApplyComponentDownStateAsync(CancellationToken cancellationToken = default);
        Task ApplyComponent28VStateAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 开启继电器供电（24V）
        /// </summary>
        Task ApplyRelayPowerAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 关闭继电器供电
        /// </summary>
        Task DisableRelayPowerAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// 加放油单板组件供电控制实现。
    /// 
    /// 【硬件配置】根据接线图和原理图：
    /// - 低压电源1 (IT-N6332B) 192.168.1.15: 4路32V供电（继电器供电）
    ///   - CH1: 32V1 (组件28V供电，实际输出28V/3A)
    ///   - CH2: 32V2 (继电器24V供电，实际输出24V/1A)
    ///   - CH3: 32V3 (备用)
    /// - 低压电源2 (IT-N6332B) 192.168.1.16: 备用
    /// - 低压电源3 (IT-N6332B) 192.168.1.17: 
    ///   - CH1: ±15V供电
    ///   - CH2: 远端补偿
    ///   - CH3: ±5V供电
    /// 
    /// 【接口定义】根据接口节点对应表：
    /// - 3槽:151-162 → 32V1+/32V1-/32V2+/32V2- (电源输出 [0-32]V [0-6]A)
    /// - 继电器供电: INT_32V1(D11), +24V(D12-D15), 24VGND(D16-D18), 24V3+(D19-D20)
    /// </summary>
    public sealed class ComponentPowerStateApi : IComponentPowerStateApi
    {
        // 电源1：组件供电和继电器供电
        private const string PowerSupply1IpAddress = "192.168.1.15";
        private const PowerSupplyChannel ComponentChannel = PowerSupplyChannel.CH1;  // 组件28V供电
        private const PowerSupplyChannel RelayChannel = PowerSupplyChannel.CH2;      // 继电器24V供电
        private const double ComponentVoltage = 28.0;
        private const double ComponentCurrentLimit = 3.0;
        private const double RelayVoltage = 24.0;
        private const double RelayCurrentLimit = 1.0;

        private bool _disposed;
        private ComponentPowerState _currentState = ComponentPowerState.ComponentDown;
        private IPowerSupplyApi _power1;
        private bool _relayPowerOn;

        public ComponentPowerState CurrentState => _currentState;

        public async Task ApplyComponentDownStateAsync(CancellationToken cancellationToken = default)
        {
            EnsureNotDisposed();

            await EnsurePower1ConnectedAsync(cancellationToken).ConfigureAwait(false);

            // 先关闭组件供电
            await _power1.SetOutputEnabledAsync(ComponentChannel, false, cancellationToken).ConfigureAwait(false);

            // 如果继电器供电也开着，一并关闭
            if (_relayPowerOn)
            {
                await _power1.SetOutputEnabledAsync(RelayChannel, false, cancellationToken).ConfigureAwait(false);
                _relayPowerOn = false;
            }

            _currentState = ComponentPowerState.ComponentDown;
        }

        public async Task ApplyComponent28VStateAsync(CancellationToken cancellationToken = default)
        {
            EnsureNotDisposed();

            await EnsurePower1ConnectedAsync(cancellationToken).ConfigureAwait(false);

            // 设置组件28V供电参数并开启
            await _power1.ApplyAsync(ComponentChannel, ComponentVoltage, ComponentCurrentLimit, cancellationToken).ConfigureAwait(false);
            await _power1.SetOutputEnabledAsync(ComponentChannel, true, cancellationToken).ConfigureAwait(false);
            await Task.Delay(300, cancellationToken).ConfigureAwait(false);

            _currentState = _relayPowerOn ? ComponentPowerState.FullPowerOn : ComponentPowerState.Component28VOn;
        }

        public async Task ApplyRelayPowerAsync(CancellationToken cancellationToken = default)
        {
            EnsureNotDisposed();

            await EnsurePower1ConnectedAsync(cancellationToken).ConfigureAwait(false);

            // 设置继电器24V供电参数并开启
            await _power1.ApplyAsync(RelayChannel, RelayVoltage, RelayCurrentLimit, cancellationToken).ConfigureAwait(false);
            await _power1.SetOutputEnabledAsync(RelayChannel, true, cancellationToken).ConfigureAwait(false);
            await Task.Delay(200, cancellationToken).ConfigureAwait(false);

            _relayPowerOn = true;
            if (_currentState == ComponentPowerState.Component28VOn)
                _currentState = ComponentPowerState.FullPowerOn;
            else if (_currentState == ComponentPowerState.ComponentDown)
                _currentState = ComponentPowerState.RelayPowerOn;
        }

        public async Task DisableRelayPowerAsync(CancellationToken cancellationToken = default)
        {
            EnsureNotDisposed();

            if (!_relayPowerOn)
                return;

            await EnsurePower1ConnectedAsync(cancellationToken).ConfigureAwait(false);
            await _power1.SetOutputEnabledAsync(RelayChannel, false, cancellationToken).ConfigureAwait(false);

            _relayPowerOn = false;
            if (_currentState == ComponentPowerState.FullPowerOn)
                _currentState = ComponentPowerState.Component28VOn;
            else if (_currentState == ComponentPowerState.RelayPowerOn)
                _currentState = ComponentPowerState.ComponentDown;
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;

            if (_power1 != null)
            {
                try { await _power1.SetOutputEnabledAsync(ComponentChannel, false, CancellationToken.None).ConfigureAwait(false); } catch { }
                try { await _power1.SetOutputEnabledAsync(RelayChannel, false, CancellationToken.None).ConfigureAwait(false); } catch { }
                try { await _power1.DisconnectAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                try { await _power1.DisposeAsync().ConfigureAwait(false); } catch { }
                _power1 = null;
            }
        }

        private async Task EnsurePower1ConnectedAsync(CancellationToken cancellationToken)
        {
            _power1 ??= new PowerSupplySocketApi();
            if (!_power1.IsConnected)
                await _power1.ConnectAsync(PowerSupply1IpAddress, cancellationToken).ConfigureAwait(false);
        }

        private void EnsureNotDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ComponentPowerStateApi));
        }
    }
}
