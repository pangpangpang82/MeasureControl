using System;
using System.Threading;
using System.Threading.Tasks;
using MeasureControl.Services.HardwareApis;
using Prism.Mvvm;

namespace MeasureControl.Services
{
    public interface IHydraulicPowerService
    {
        bool IsHydraulicPowered { get; }
        /// <summary>
        /// 当前已上电的单板类型（null 表示未上电），与 IsHydraulicPowered 同步更新
        /// </summary>
        string PoweredBoardType { get; }
        double PoweredVoltage { get; }
        event EventHandler IsHydraulicPoweredChanged;
        Task PowerOnAsync(string boardType = null, double voltage = 28.0, CancellationToken cancellationToken = default);
        Task PowerOffAsync(CancellationToken cancellationToken = default);
        /// <summary>
        /// 由测试项直接管理硬件调用后调用，仅更新状态标志，不发起网络操作
        /// </summary>
        void SetPoweredState(bool powered);
    }

    public sealed class HydraulicPowerService : BindableBase, IHydraulicPowerService
    {
        private const string IpAddress = "192.168.1.15";
        private const double DefaultVoltage = 28.0;
        private const double Current1A = 1.0;

        private const string HydraulicBoardTypeName = "液压单板";

        private bool _isHydraulicPowered;
        private string _poweredBoardType;
        private double _poweredVoltage;

        public bool IsHydraulicPowered
        {
            get => _isHydraulicPowered;
            private set
            {
                if (_isHydraulicPowered == value) return;
                _isHydraulicPowered = value;
                RaisePropertyChanged();
                IsHydraulicPoweredChanged?.Invoke(this, EventArgs.Empty);
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

        public event EventHandler IsHydraulicPoweredChanged;

        public async Task PowerOnAsync(string boardType = null, double voltage = DefaultVoltage, CancellationToken cancellationToken = default)
        {
            var api = new PowerSupplySocketApi();
            try
            {
                await api.ConnectAsync(IpAddress, cancellationToken).ConfigureAwait(false);
                await api.ApplyAsync(PowerSupplyChannel.CH1, voltage, Current1A, cancellationToken).ConfigureAwait(false);
                await api.SetOutputEnabledAsync(PowerSupplyChannel.CH1, true, cancellationToken).ConfigureAwait(false);
                IsHydraulicPowered = true;
                PoweredBoardType = boardType ?? HydraulicBoardTypeName;
                PoweredVoltage = voltage;
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
                IsHydraulicPowered = false;
                PoweredBoardType = null;
                PoweredVoltage = 0;
            }
            finally
            {
                try { await api.DisconnectAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                try { await api.DisposeAsync().ConfigureAwait(false); } catch { }
            }
        }

        public void SetPoweredState(bool powered)
        {
            IsHydraulicPowered = powered;
            PoweredBoardType = powered ? HydraulicBoardTypeName : null;
            PoweredVoltage = powered ? DefaultVoltage : 0;
        }
    }
}
