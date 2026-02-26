using System;
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

    public interface IPxi4087LvdtApi : IAsyncDisposable
    {
        bool IsConnected { get; }

        Task ConnectAsync(int slotIndex = 1, CancellationToken cancellationToken = default);
        Task DisconnectAsync(CancellationToken cancellationToken = default);

        Task ConfigureTestChannelAsync(int channel, bool useExternalExcitation = true, CancellationToken cancellationToken = default);
        Task StartAsync(int channel, CancellationToken cancellationToken = default);
        Task StopAsync(int channel, CancellationToken cancellationToken = default);

        Task<LvdtReading> ReadOnceAsync(int channel, int settleMs = 300, int retryCount = 3, CancellationToken cancellationToken = default);
    }

    public sealed class Pxi4087LvdtApi : IPxi4087LvdtApi
    {
        private readonly SemaphoreSlim _lifecycleLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _ioLock = new SemaphoreSlim(1, 1);

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

        public async Task<LvdtReading> ReadOnceAsync(int channel, int settleMs = 300, int retryCount = 3, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            if (retryCount < 1)
                throw new ArgumentOutOfRangeException(nameof(retryCount));

            await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                EnsureConnected();
                ushort chIndex = ToChIndex(channel);

                int status = PXI4087Native.pxi4087_lvdtStart(_handle, chIndex);
                if (status != 0)
                    throw new InvalidOperationException($"pxi4087_lvdtStart failed: {status}");

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

        private static ushort ToChIndex(int channel)
        {
            if (channel < 1 || channel > 8)
                throw new ArgumentOutOfRangeException(nameof(channel), "channel must be 1..8");

            return (ushort)(channel - 1);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(Pxi4087LvdtApi));
        }
    }
}
