using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MeasureControl.Helpers.OKAIPXIDevice;

namespace MeasureControl.Services.HardwareApis
{
    public sealed class LvdtReading
    {
        public double ExcRms { get; set; }
        public double ExcFreqHz { get; set; }
        public double VaRms { get; set; }
        public double VbRms { get; set; }
        public double Ratio { get; set; }
    }

    public sealed class LvdtSimulationConfig
    {
        public bool UseInternalExcitation { get; set; }
        public double ExcitationVoltage { get; set; } = 7.0;
        public double ExcitationFrequency { get; set; } = 3200.0;
        public double TransmissionRatio { get; set; } = 1.0;
        public ushort PhaseDelay { get; set; }
        public ushort AdcRangeIndex { get; set; } = 3;
    }

    public sealed class LvdtOutputCalibration
    {
        public double VaSlope { get; set; } = 1.0;
        public double VaIntercept { get; set; }
        public bool IsVaCalibrated { get; set; }
        public double VbSlope { get; set; } = 1.0;
        public double VbIntercept { get; set; }
        public bool IsVbCalibrated { get; set; }
    }

    public interface IPxi4087LvdtApi : IAsyncDisposable
    {
        bool IsConnected { get; }

        Task ConnectAsync(int slotIndex = 1, CancellationToken cancellationToken = default);
        Task DisconnectAsync(CancellationToken cancellationToken = default);

        Task ConfigureOutputCalibrationAsync(int channel, LvdtOutputCalibration calibration, CancellationToken cancellationToken = default);
        Task ClearOutputCalibrationAsync(int channel, CancellationToken cancellationToken = default);
        Task ConfigureTestChannelAsync(int channel, bool useExternalExcitation = true, CancellationToken cancellationToken = default);
        Task ConfigureSimulationChannelAsync(int channel, LvdtSimulationConfig config, CancellationToken cancellationToken = default);
        Task SetVaVbAsync(int channel, double vaRms, double vbRms, CancellationToken cancellationToken = default);
        Task ResetAsync(CancellationToken cancellationToken = default);
        Task StartAsync(int channel, CancellationToken cancellationToken = default);
        Task StopAsync(int channel, CancellationToken cancellationToken = default);

        Task<LvdtReading> ReadOnceAsync(int channel, int settleMs = 300, int retryCount = 3, bool restartBeforeRead = true, CancellationToken cancellationToken = default);
    }

    public sealed class Pxi4087LvdtApi : IPxi4087LvdtApi
    {
        private readonly SemaphoreSlim _lifecycleLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _ioLock = new SemaphoreSlim(1, 1);
        private readonly Dictionary<int, LvdtOutputCalibration> _outputCalibrations = new Dictionary<int, LvdtOutputCalibration>();

        private UIntPtr _handle = UIntPtr.Zero;
        private int _slotIndex = 1;
        private bool _disposed;

        public bool IsConnected => _handle != UIntPtr.Zero;

        public async Task ConnectAsync(int slotIndex = 1, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (IsConnected)
                    return;

                _slotIndex = slotIndex;

                UIntPtr vi = UIntPtr.Zero;

                for (ushort id = 1; id <= 32; id++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    UIntPtr h = UIntPtr.Zero;
                    try
                    {
                        h = PXI4087Native.pxi4087_openDevice(id);
                        if (h == UIntPtr.Zero)
                            continue;

                        ushort actualSlot;
                        int slotStatus = OKAIDaqNative.DAQDevice_getSlot(h, out actualSlot);
                        if (slotStatus != 0)
                        {
                            try { PXI4087Native.pxi4087_releaseDevice(h); } catch { }
                            continue;
                        }

                        if (_slotIndex <= 0 || actualSlot == (ushort)_slotIndex)
                        {
                            vi = h;
                            break;
                        }

                        try { PXI4087Native.pxi4087_releaseDevice(h); } catch { }
                    }
                    catch
                    {
                        try { if (h != UIntPtr.Zero) PXI4087Native.pxi4087_releaseDevice(h); } catch { }
                    }
                }

                if (vi == UIntPtr.Zero)
                    throw new InvalidOperationException($"PXI4087 connect failed: slot={_slotIndex}");

                try { PXI4087Native.pxi4087_reset(vi); } catch { }

                _handle = vi;
            }
            finally
            {
                _lifecycleLock.Release();
            }
        }

        public async Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!IsConnected)
                    return;

                await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    try { PXI4087Native.pxi4087_releaseDevice(_handle); } catch { }
                    _handle = UIntPtr.Zero;
                }
                finally
                {
                    _ioLock.Release();
                }
            }
            finally
            {
                _lifecycleLock.Release();
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
                return;

            _disposed = true;

            try { await DisconnectAsync().ConfigureAwait(false); } catch { }

            _lifecycleLock.Dispose();
            _ioLock.Dispose();
        }

        public async Task ConfigureOutputCalibrationAsync(int channel, LvdtOutputCalibration calibration, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            if (calibration == null)
                throw new ArgumentNullException(nameof(calibration));

            await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ValidateChannel(channel);
                _outputCalibrations[channel] = new LvdtOutputCalibration
                {
                    VaSlope = calibration.VaSlope,
                    VaIntercept = calibration.VaIntercept,
                    IsVaCalibrated = calibration.IsVaCalibrated,
                    VbSlope = calibration.VbSlope,
                    VbIntercept = calibration.VbIntercept,
                    IsVbCalibrated = calibration.IsVbCalibrated
                };
            }
            finally
            {
                _ioLock.Release();
            }
        }

        public async Task ClearOutputCalibrationAsync(int channel, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ValidateChannel(channel);
                _outputCalibrations.Remove(channel);
            }
            finally
            {
                _ioLock.Release();
            }
        }

        public async Task ConfigureTestChannelAsync(int channel, bool useExternalExcitation = true, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                EnsureConnected();

                ushort chIndex = ToChIndex(channel);

                int status = PXI4087Native.pxi4087_setMode(
                    _handle,
                    chIndex,
                    (ushort)PXI4087Constants.pxi4087_Ch_Mode_Test,
                    (ushort)(useExternalExcitation ? PXI4087Constants.pxi4087_Ch_Exc_Sour_Ext : PXI4087Constants.pxi4087_Ch_Exc_Sour_Int),
                    0,
                    0);

                if (status != 0)
                    throw new InvalidOperationException($"pxi4087_setMode failed: {status}");
            }
            finally
            {
                _ioLock.Release();
            }
        }

        public async Task ConfigureSimulationChannelAsync(int channel, LvdtSimulationConfig config, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            if (config == null)
                throw new ArgumentNullException(nameof(config));

            await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                EnsureConnected();

                ushort chIndex = ToChIndex(channel);

                int status = PXI4087Native.pxi4087_setMode(
                    _handle,
                    chIndex,
                    (ushort)PXI4087Constants.pxi4087_Ch_Mode_Sim,
                    (ushort)(config.UseInternalExcitation ? PXI4087Constants.pxi4087_Ch_Exc_Sour_Int : PXI4087Constants.pxi4087_Ch_Exc_Sour_Ext),
                    0,
                    0);
                if (status != 0)
                    throw new InvalidOperationException($"pxi4087_setMode(sim) failed: {status}");

                if (config.UseInternalExcitation)
                {
                    status = PXI4087Native.pxi4087_setIntExcSig(_handle, chIndex, config.ExcitationVoltage, config.ExcitationFrequency);
                    if (status != 0)
                        throw new InvalidOperationException($"pxi4087_setIntExcSig failed: {status}");
                }

                status = PXI4087Native.pxi4087_setTransRatio(_handle, chIndex, config.TransmissionRatio);
                if (status != 0)
                    throw new InvalidOperationException($"pxi4087_setTransRatio failed: {status}");

                status = PXI4087Native.pxi4087_setLvdtPhaseDelay(_handle, chIndex, config.PhaseDelay);
                if (status != 0)
                    throw new InvalidOperationException($"pxi4087_setLvdtPhaseDelay failed: {status}");

                status = PXI4087Native.pxi4087_setLvdtAdcRange(_handle, chIndex, config.AdcRangeIndex);
                if (status != 0)
                    throw new InvalidOperationException($"pxi4087_setLvdtAdcRange failed: {status}");

                status = PXI4087Native.pxi4087_setLvdtDataOutMode(_handle, chIndex, (ushort)PXI4087Constants.pxi4087_Lvdt_Data_Out_Fix);
                if (status != 0)
                    throw new InvalidOperationException($"pxi4087_setLvdtDataOutMode failed: {status}");
            }
            finally
            {
                _ioLock.Release();
            }
        }

        public async Task SetVaVbAsync(int channel, double vaRms, double vbRms, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                EnsureConnected();
                ushort chIndex = ToChIndex(channel);

                var (calibratedVa, calibratedVb) = ApplyOutputCalibration(channel, vaRms, vbRms);

                int status = PXI4087Native.pxi4087_setLvdtVaVb(_handle, chIndex, calibratedVa, calibratedVb);
                if (status != 0)
                    throw new InvalidOperationException($"pxi4087_setLvdtVaVb failed: {status}");
            }
            finally
            {
                _ioLock.Release();
            }
        }

        public async Task ResetAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                EnsureConnected();

                int status = PXI4087Native.pxi4087_reset(_handle);
                if (status != 0)
                    throw new InvalidOperationException($"pxi4087_reset failed: {status}");
            }
            finally
            {
                _ioLock.Release();
            }
        }

        public async Task StartAsync(int channel, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                EnsureConnected();
                ushort chIndex = ToChIndex(channel);

                int status = PXI4087Native.pxi4087_lvdtStart(_handle, chIndex);
                if (status != 0)
                    throw new InvalidOperationException($"pxi4087_lvdtStart failed: {status}");
            }
            finally
            {
                _ioLock.Release();
            }
        }

        public async Task StopAsync(int channel, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!IsConnected)
                    return;

                ushort chIndex = ToChIndex(channel);

                int status = PXI4087Native.pxi4087_lvdtStop(_handle, chIndex);
                if (status != 0)
                    throw new InvalidOperationException($"pxi4087_lvdtStop failed: {status}");
            }
            finally
            {
                _ioLock.Release();
            }
        }

        public async Task<LvdtReading> ReadOnceAsync(int channel, int settleMs = 300, int retryCount = 3, bool restartBeforeRead = true, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            if (retryCount < 1)
                throw new ArgumentOutOfRangeException(nameof(retryCount));

            await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                EnsureConnected();
                ushort chIndex = ToChIndex(channel);

                int status;
                if (restartBeforeRead)
                {
                    status = PXI4087Native.pxi4087_lvdtStart(_handle, chIndex);
                    if (status != 0)
                        throw new InvalidOperationException($"pxi4087_lvdtStart failed: {status}");
                }

                if (settleMs > 0)
                    await Task.Delay(settleMs, cancellationToken).ConfigureAwait(false);

                Exception last = null;

                for (int i = 0; i < retryCount; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        double excRms;
                        status = PXI4087Native.pxi4087_getLvdtExcSigRms(_handle, chIndex, out excRms);
                        if (status != 0)
                            throw new InvalidOperationException($"pxi4087_getLvdtExcSigRms failed: {status}");

                        double excFreqHz;
                        status = PXI4087Native.pxi4087_getLvdtExcSigFreq(_handle, chIndex, out excFreqHz);
                        if (status != 0)
                            throw new InvalidOperationException($"pxi4087_getLvdtExcSigFreq failed: {status}");

                        double vaRms;
                        double vbRms;
                        double ratio;
                        status = PXI4087Native.pxi4087_getLvdtRmsVol(_handle, chIndex, out vaRms, out vbRms, out ratio);
                        if (status != 0)
                            throw new InvalidOperationException($"pxi4087_getLvdtRmsVol failed: {status}");

                        return new LvdtReading
                        {
                            ExcRms = excRms,
                            ExcFreqHz = excFreqHz,
                            VaRms = vaRms,
                            VbRms = vbRms,
                            Ratio = ratio
                        };
                    }
                    catch (Exception ex)
                    {
                        last = ex;
                        await Task.Delay(50, cancellationToken).ConfigureAwait(false);
                    }
                }

                throw new InvalidOperationException("PXI4087 LVDT read failed after retries", last);
            }
            finally
            {
                _ioLock.Release();
            }
        }

        private void EnsureConnected()
        {
            if (!IsConnected)
                throw new InvalidOperationException("PXI4087 is not connected");
        }

        private (double vaRms, double vbRms) ApplyOutputCalibration(int channel, double vaRms, double vbRms)
        {
            if (!_outputCalibrations.TryGetValue(channel, out var calibration) || calibration == null)
                return (vaRms, vbRms);

            var calibratedVa = calibration.IsVaCalibrated ? vaRms * calibration.VaSlope + calibration.VaIntercept : vaRms;
            var calibratedVb = calibration.IsVbCalibrated ? vbRms * calibration.VbSlope + calibration.VbIntercept : vbRms;
            return (calibratedVa, calibratedVb);
        }

        private static void ValidateChannel(int channel)
        {
            if (channel < 1 || channel > 8)
                throw new ArgumentOutOfRangeException(nameof(channel), "channel must be 1..8");
        }

        private static ushort ToChIndex(int channel)
        {
            ValidateChannel(channel);

            return (ushort)(channel - 1);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(Pxi4087LvdtApi));
        }
    }
}
