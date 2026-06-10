using System;
using System.Threading;
using System.Threading.Tasks;
using MeasureControl.Services.HardwareApis;
using Prism.Mvvm;

namespace MeasureControl.Services
{
    /// <summary>
    /// 管理 192.168.1.15 CH1 共用电源的全局状态。
    /// 所有板卡（液压单板 / 加放油单板 / 惰化模拟板 / 惰化控制板）均通过此服务共享同一路输出。
    /// </summary>
    public interface IBoardPowerService
    {
        /// <summary>192.168.1.15 CH1 当前是否有输出</summary>
        bool IsPowered { get; }
        /// <summary>当前已上电的单板类型（null 表示未上电）</summary>
        string PoweredBoardType { get; }
        /// <summary>当前已上电的电压（V），0 表示未上电</summary>
        double PoweredVoltage { get; }
        event EventHandler IsPoweredChanged;
        /// <summary>通过服务发起网络操作并更新全局状态</summary>
        Task PowerOnAsync(string boardType, double voltage = 28.0, CancellationToken cancellationToken = default);
        Task PowerOffAsync(CancellationToken cancellationToken = default);
        /// <summary>
        /// 由测试项自行管理硬件后调用，仅同步状态标志，不发起网络操作
        /// </summary>
        void SetPoweredState(bool powered, string boardType = null, double voltage = 0);
    }

    public sealed class BoardPowerService : BindableBase, IBoardPowerService
    {
        private const string IpAddress = "192.168.1.15";
        private const double DefaultVoltage = 28.0;
        private const double Current1A = 1.0;
        private const double AirSafetyCurrent2A = 2.0;

        private bool _isPowered;
        private string _poweredBoardType;
        private double _poweredVoltage;

        public bool IsPowered
        {
            get => _isPowered;
            private set
            {
                if (_isPowered == value) return;
                _isPowered = value;
                RaisePropertyChanged();
                IsPoweredChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public string PoweredBoardType
        {
            get => _poweredBoardType;
            private set => SetProperty(ref _poweredBoardType, value);
        }

        public double PoweredVoltage
        {
            get => _poweredVoltage;
            private set => SetProperty(ref _poweredVoltage, value);
        }

        public event EventHandler IsPoweredChanged;

        public async Task PowerOnAsync(string boardType, double voltage = DefaultVoltage, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(boardType))
                throw new ArgumentException("必须指定单板类型", nameof(boardType));
            var api = new PowerSupplySocketApi();
            try
            {
                await api.ConnectAsync(IpAddress, cancellationToken).ConfigureAwait(false);
                var currentLimit = string.Equals(boardType, "空气安全板", StringComparison.OrdinalIgnoreCase)
                    ? AirSafetyCurrent2A
                    : Current1A;
                await api.ApplyAsync(PowerSupplyChannel.CH1, voltage, currentLimit, cancellationToken).ConfigureAwait(false);
                await api.SetOutputEnabledAsync(PowerSupplyChannel.CH1, true, cancellationToken).ConfigureAwait(false);
                PoweredBoardType = boardType;
                PoweredVoltage = voltage;
                IsPowered = true;
            }
            finally
            {
                try { await api.DisconnectAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                try { await api.DisposeAsync().ConfigureAwait(false); } catch { }
            }
        }

        public async Task PowerOffAsync(CancellationToken cancellationToken = default)
        {
            var api = new PowerSupplySocketApi();
            try
            {
                await api.ConnectAsync(IpAddress, cancellationToken).ConfigureAwait(false);
                await api.SetOutputEnabledAsync(PowerSupplyChannel.CH1, false, cancellationToken).ConfigureAwait(false);
                PoweredBoardType = null;
                PoweredVoltage = 0;
                IsPowered = false;
            }
            finally
            {
                try { await api.DisconnectAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                try { await api.DisposeAsync().ConfigureAwait(false); } catch { }
            }
        }

        public void SetPoweredState(bool powered, string boardType = null, double voltage = 0)
        {
            PoweredBoardType = powered ? boardType : null;
            PoweredVoltage = powered ? (voltage > 0 ? voltage : DefaultVoltage) : 0;
            IsPowered = powered;
        }
    }
}
