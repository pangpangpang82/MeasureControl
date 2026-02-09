using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using MeasureControl.Drivers.ART4229;
using MeasureControl.Models.Devices;

namespace MeasureControl.Services.HardwareApis
{
    public sealed class Art4229RxTempApi : IAsyncDisposable
    {
        private const double DefaultRateBps = 100000;

        private readonly DeviceBase _device;
        private readonly int _deviceIndex;
        private readonly ART4229Driver _driver;
        private readonly SemaphoreSlim _ioLock;

        private bool _connected;
        private bool _disposed;

        public Art4229RxTempApi(DeviceBase device, int deviceIndex = 0)
        {
            _device = device ?? throw new ArgumentNullException(nameof(device));
            _deviceIndex = deviceIndex;
            _driver = new ART4229Driver(_device, _deviceIndex);
            _ioLock = new SemaphoreSlim(1, 1);
        }

        public bool IsConnected => _connected;

        public async Task ConnectAsync()
        {
            await _ioLock.WaitAsync().ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();

                if (_connected)
                    return;

                if (!await _driver.ConnectAsync().ConfigureAwait(false))
                    throw new InvalidOperationException("ART4229 connect failed.");

                _connected = true;
            }
            finally
            {
                _ioLock.Release();
            }
        }

        public async Task DisconnectAsync()
        {
            await _ioLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!_connected)
                    return;

                await _driver.DisconnectAsync().ConfigureAwait(false);
                _connected = false;
            }
            finally
            {
                _ioLock.Release();
            }
        }

        public async Task ConfigureRxAsync(
            int channelNumber,
            double rateBps = DefaultRateBps,
            int parity = 0,
            int wordFormat = 0,
            bool enableInterrupt = false,
            int interruptDepth = 10,
            bool enableTimeTag = false)
        {
            int channelIndex = NormalizeChannelNumber(channelNumber);

            await _ioLock.WaitAsync().ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                EnsureConnected();

                if (!await _driver.OpenRxChannelAsync(channelIndex).ConfigureAwait(false))
                    throw new InvalidOperationException($"Open RX channel failed: CH{channelNumber}");

                if (!await _driver.ConfigureRxChannelAsync(channelIndex, rateBps, parity, wordFormat, enableInterrupt, interruptDepth, enableTimeTag)
                        .ConfigureAwait(false))
                    throw new InvalidOperationException($"Configure RX channel failed: CH{channelNumber}");
            }
            finally
            {
                _ioLock.Release();
            }
        }

        public async Task StartRxAsync(int channelNumber)
        {
            int channelIndex = NormalizeChannelNumber(channelNumber);

            await _ioLock.WaitAsync().ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                EnsureConnected();

                if (!await _driver.StartReceiveAsync(channelIndex).ConfigureAwait(false))
                    throw new InvalidOperationException($"Start RX failed: CH{channelNumber}");
            }
            finally
            {
                _ioLock.Release();
            }
        }

        public async Task StopRxAsync(int channelNumber)
        {
            int channelIndex = NormalizeChannelNumber(channelNumber);

            await _ioLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!_connected)
                    return;

                await _driver.StopReceiveAsync(channelIndex).ConfigureAwait(false);
            }
            finally
            {
                _ioLock.Release();
            }
        }

        public async Task<Arinc429Frame?> ReadOneFrameAsync(int channelNumber, uint maxCount = 1024)
        {
            int channelIndex = NormalizeChannelNumber(channelNumber);

            await _ioLock.WaitAsync().ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                EnsureConnected();

                // 你要求：固定 rate(100k) + enableTimeTag=false
                // 所以这里 enableRateAdaption=false，确保 pktLen=1（只读 word）
                var items = await _driver.ReadReceiveDataAsync(channelIndex, maxCount, enableTimeTag: false, enableRateAdaption: false)
                                         .ConfigureAwait(false);

                if (items == null || items.Count == 0)
                    return null;

                var it = items[0];
                return Arinc429Frame.FromRaw(it.Data429);
            }
            finally
            {
                _ioLock.Release();
            }
        }

        private static int NormalizeChannelNumber(int channelNumber)
        {
            if (channelNumber < 1 || channelNumber > 40)
                throw new ArgumentOutOfRangeException(nameof(channelNumber), "channelNumber must be 1..40");

            return channelNumber - 1;
        }

        private void EnsureConnected()
        {
            if (!_connected)
                throw new InvalidOperationException("Not connected. Call ConnectAsync first.");
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(Art4229RxTempApi));
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
                return;

            _disposed = true;

            try
            {
                await DisconnectAsync().ConfigureAwait(false);
            }
            catch
            {
            }
            finally
            {
                try { _ioLock.Dispose(); } catch { }
            }
        }
    }

    public sealed class Arinc429Frame
    {
        public uint Raw { get; }
        public byte Label { get; }
        public byte SDI { get; }
        public uint Data19 { get; }
        public byte SSM { get; }
        public byte ParityBit { get; }

        private Arinc429Frame(uint raw, byte label, byte sdi, uint data19, byte ssm, byte parityBit)
        {
            Raw = raw;
            Label = label;
            SDI = sdi;
            Data19 = data19;
            SSM = ssm;
            ParityBit = parityBit;
        }

        public static Arinc429Frame FromRaw(uint raw)
        {
            byte label = (byte)(raw & 0xFF);
            byte sdi = (byte)((raw >> 8) & 0x03);
            uint data19 = (raw >> 10) & 0x7FFFF;
            byte ssm = (byte)((raw >> 29) & 0x03);
            byte parityBit = (byte)((raw >> 31) & 0x01);

            return new Arinc429Frame(raw, label, sdi, data19, ssm, parityBit);
        }

        public string RawHex => Raw.ToString("X8", CultureInfo.InvariantCulture);
        public string LabelOctal => Convert.ToString(Label, 8).PadLeft(3, '0');
    }
}
