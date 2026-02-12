using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MeasureControl.Drivers.ART4229;
using MeasureControl.Models.Devices;

namespace MeasureControl.Services.HardwareApis
{
    /// <summary>
    /// ARINC429 校验模式
    /// </summary>
    public enum Art4229Parity
    {
        None = 0,
        Odd = 1,
        Even = 2
    }

    /// <summary>
    /// ARINC429 发送模式：Single=单次发送，Period=周期发送
    /// </summary>
    public enum Art4229TxMode
    {
        Single = 0,
        Period = 1
    }
    /// <summary>
    /// ARINC429 字格式：Standard429=标准32位格式，Format1=25位格式
    /// </summary>
    public enum Art4229WordFormat
    {
        Standard429 = 0,
        Format1 = 1
    }

    /// <summary>
    /// 接收到的 ARINC429 数据字（含时标和码率信息）
    /// </summary>
    public readonly struct Art4229RxWord
    {
        public Art4229RxWord(uint data429, uint rate, uint timeHigh, uint timeLow)
        {
            Data429 = data429;
            Rate = rate;
            TimeHigh = timeHigh;
            TimeLow = timeLow;
        }

        public uint Data429 { get; }
        public uint Rate { get; }
        public uint TimeHigh { get; }
        public uint TimeLow { get; }
    }

    /// <summary>
    /// ART4229 板卡高层 API 接口（线程安全，支持 8 字节多帧拼装）
    /// </summary>
    public interface IArt4229Api : IAsyncDisposable
    {
        /// <summary>是否已连接</summary>
        bool IsConnected { get; }

        /// <summary>连接设备</summary>
        Task ConnectAsync(CancellationToken cancellationToken = default);
        /// <summary>断开设备</summary>
        Task DisconnectAsync(CancellationToken cancellationToken = default);

        /// <summary>打开发送通道</summary>
        Task OpenTxAsync(int txChannelIndex, CancellationToken cancellationToken = default);
        /// <summary>关闭发送通道</summary>
        Task CloseTxAsync(int txChannelIndex, CancellationToken cancellationToken = default);
        /// <summary>配置发送通道（码率、模式、校验、字格式）</summary>
        Task ConfigureTxAsync(
            int txChannelIndex,
            double rate,
            Art4229TxMode mode = Art4229TxMode.Single,
            Art4229Parity parity = Art4229Parity.Odd,
            Art4229WordFormat wordFormat = Art4229WordFormat.Standard429,
            CancellationToken cancellationToken = default);

        /// <summary>打开接收通道</summary>
        Task OpenRxAsync(int rxChannelIndex, CancellationToken cancellationToken = default);
        /// <summary>关闭接收通道</summary>
        Task CloseRxAsync(int rxChannelIndex, CancellationToken cancellationToken = default);
        /// <summary>配置接收通道（码率、校验、字格式、中断、时标）</summary>
        Task ConfigureRxAsync(
            int rxChannelIndex,
            double rate,
            Art4229Parity parity = Art4229Parity.Odd,
            Art4229WordFormat wordFormat = Art4229WordFormat.Standard429,
            bool enableInterrupt = false,
            int interruptDepth = 512,
            bool enableTimeTag = false,
            CancellationToken cancellationToken = default);

        /// <summary>启动接收</summary>
        Task StartRxAsync(int rxChannelIndex, CancellationToken cancellationToken = default);
        /// <summary>停止接收</summary>
        Task StopRxAsync(int rxChannelIndex, CancellationToken cancellationToken = default);

        /// <summary>发送原始 429 字（不拼帧）</summary>
        Task SendWordsSingleAsync(int txChannelIndex, IReadOnlyList<uint> words, Art4229Parity parity = Art4229Parity.Odd, CancellationToken cancellationToken = default);
        /// <summary>发送 8 字节 payload（自动拆为 4 帧，SDI=0..3）</summary>
        Task SendPayload8Async(int txChannelIndex, byte label, byte[] payload8, Art4229Parity parity = Art4229Parity.Odd, CancellationToken cancellationToken = default);

        /// <summary>读取接收缓冲区中的原始 429 字</summary>
        Task<IReadOnlyList<Art4229RxWord>> ReadRxWordsAsync(int rxChannelIndex, uint maxCount = 1024, bool enableTimeTag = false, bool enableRateAdaption = false, CancellationToken cancellationToken = default);
        /// <summary>等待并拼装 8 字节 payload（按 label+SDI 组帧）</summary>
        Task<byte[]> WaitPayload8Async(
            int rxChannelIndex,
            byte expectedLabel,
            Func<byte[], bool> accept,
            int timeoutMs,
            CancellationToken cancellationToken = default);

        /// <summary>解析通道字符串（如 "429_CH0"）为通道索引</summary>
        int ParseChannelIndex(string channel);

        /// <summary>将4字节转换为单精度浮点数（Big-Endian）</summary>
        float BytesToFloat(byte[] bytes, int offset = 0);

        /// <summary>将单精度浮点数转换为4字节（Big-Endian）</summary>
        byte[] FloatToBytes(float value);

        /// <summary>从8字节payload中提取后4字节并转换为浮点数</summary>
        float ExtractFloatFromPayload8(byte[] payload8);

        /// <summary>构建8字节payload：前4字节为指令，后4字节为浮点数</summary>
        byte[] BuildPayload8WithFloat(byte[] cmd4, float value);

        /// <summary>构建原始429字（含Label、SDI、19bit数据、SSM、奇偶校验）</summary>
        uint BuildRawWord(byte label, byte sdi, uint data19, byte ssm = 0, bool applyOddParity = true);

        /// <summary>解析原始429字（提取Label、SDI、19bit数据、SSM）</summary>
        void ParseRawWord(uint word, out byte label, out byte sdi, out uint data19, out byte ssm);

        /// <summary>构建BNR格式数据（有符号定点数）</summary>
        uint EncodeBnr(double value, int bitLength, double resolution, int msbPosition = 28);

        /// <summary>解析BNR格式数据（有符号定点数）</summary>
        double DecodeBnr(uint data19, int bitLength, double resolution, int msbPosition = 28);

        /// <summary>构建UBNR格式数据（无符号定点数）</summary>
        uint EncodeUbnr(double value, int bitLength, double resolution, int msbPosition = 28);

        /// <summary>解析UBNR格式数据（无符号定点数）</summary>
        double DecodeUbnr(uint data19, int bitLength, double resolution, int msbPosition = 28);

        /// <summary>验证429字的奇校验是否正确</summary>
        bool VerifyOddParity(uint word);

        /// <summary>反转Label字节的位序（ARINC429标准要求Label低LSB先发）</summary>
        byte ReverseLabel(byte label);
    }

    /// <summary>
    /// ART4229 板卡 API 实现（线程安全，内部使用 SemaphoreSlim 锁）
    /// </summary>
    public sealed class Art4229Api : IArt4229Api
    {
        private readonly DeviceBase _device;
        private readonly int _deviceIndex;
        private readonly SemaphoreSlim _lifecycleLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _ioLock = new SemaphoreSlim(1, 1);
        private ART4229Driver _driver;
        private bool _disposed;

        public Art4229Api(DeviceBase device, int deviceIndex = 0)
        {
            _device = device ?? throw new ArgumentNullException(nameof(device));
            _deviceIndex = deviceIndex;
        }

        public bool IsConnected => _driver?.IsConnected == true;

        public async Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_disposed)
                    throw new ObjectDisposedException(nameof(Art4229Api));

                if (IsConnected)
                    return;

                _driver = new ART4229Driver(_device, _deviceIndex);
                var ok = await _driver.ConnectAsync().ConfigureAwait(false);
                if (!ok)
                    throw new InvalidOperationException("ART4229 connect returned false");
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
                if (_driver == null)
                    return;

                try
                {
                    await _driver.DisconnectAsync().ConfigureAwait(false);
                }
                finally
                {
                    _driver = null;
                }
            }
            finally
            {
                _lifecycleLock.Release();
            }
        }

        public async Task OpenTxAsync(int txChannelIndex, CancellationToken cancellationToken = default)
        {
            EnsureConnected();
            await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var ok = await _driver.OpenTxChannelAsync(txChannelIndex).ConfigureAwait(false);
                if (!ok)
                    throw new InvalidOperationException($"Open TX channel {txChannelIndex} failed");
            }
            finally
            {
                _ioLock.Release();
            }
        }

        public async Task CloseTxAsync(int txChannelIndex, CancellationToken cancellationToken = default)
        {
            EnsureConnected();
            await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await _driver.CloseTxChannelAsync(txChannelIndex).ConfigureAwait(false);
            }
            finally
            {
                _ioLock.Release();
            }
        }

        public async Task ConfigureTxAsync(
            int txChannelIndex,
            double rate,
            Art4229TxMode mode = Art4229TxMode.Single,
            Art4229Parity parity = Art4229Parity.Odd,
            Art4229WordFormat wordFormat = Art4229WordFormat.Standard429,
            CancellationToken cancellationToken = default)
        {
            EnsureConnected();

            await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var ok = await _driver.ConfigureTxChannelAsync(
                    txChannelIndex,
                    rate,
                    (int)mode,
                    (int)parity,
                    (int)wordFormat).ConfigureAwait(false);

                if (!ok)
                    throw new InvalidOperationException($"Configure TX channel {txChannelIndex} failed");
            }
            finally
            {
                _ioLock.Release();
            }
        }

        public async Task OpenRxAsync(int rxChannelIndex, CancellationToken cancellationToken = default)
        {
            EnsureConnected();
            await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var ok = await _driver.OpenRxChannelAsync(rxChannelIndex).ConfigureAwait(false);
                if (!ok)
                    throw new InvalidOperationException($"Open RX channel {rxChannelIndex} failed");
            }
            finally
            {
                _ioLock.Release();
            }
        }

        public async Task CloseRxAsync(int rxChannelIndex, CancellationToken cancellationToken = default)
        {
            EnsureConnected();
            await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await _driver.CloseRxChannelAsync(rxChannelIndex).ConfigureAwait(false);
            }
            finally
            {
                _ioLock.Release();
            }
        }

        public async Task ConfigureRxAsync(
            int rxChannelIndex,
            double rate,
            Art4229Parity parity = Art4229Parity.Odd,
            Art4229WordFormat wordFormat = Art4229WordFormat.Standard429,
            bool enableInterrupt = false,
            int interruptDepth = 512,
            bool enableTimeTag = false,
            CancellationToken cancellationToken = default)
        {
            EnsureConnected();

            await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var ok = await _driver.ConfigureRxChannelAsync(
                    rxChannelIndex,
                    rate,
                    (int)parity,
                    (int)wordFormat,
                    enableInterrupt,
                    interruptDepth,
                    enableTimeTag).ConfigureAwait(false);

                if (!ok)
                    throw new InvalidOperationException($"Configure RX channel {rxChannelIndex} failed");
            }
            finally
            {
                _ioLock.Release();
            }
        }

        public async Task StartRxAsync(int rxChannelIndex, CancellationToken cancellationToken = default)
        {
            EnsureConnected();
            await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var ok = await _driver.StartReceiveAsync(rxChannelIndex).ConfigureAwait(false);
                if (!ok)
                    throw new InvalidOperationException($"Start RX channel {rxChannelIndex} failed");
            }
            finally
            {
                _ioLock.Release();
            }
        }

        public async Task StopRxAsync(int rxChannelIndex, CancellationToken cancellationToken = default)
        {
            EnsureConnected();
            await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await _driver.StopReceiveAsync(rxChannelIndex).ConfigureAwait(false);
            }
            finally
            {
                _ioLock.Release();
            }
        }

        public async Task SendWordsSingleAsync(int txChannelIndex, IReadOnlyList<uint> words, Art4229Parity parity = Art4229Parity.Odd, CancellationToken cancellationToken = default)
        {
            EnsureConnected();
            if (words == null)
                throw new ArgumentNullException(nameof(words));

            var data = words.ToArray();
            if (data.Length == 0)
                return;

            var parityArr = new uint[data.Length];
            for (int i = 0; i < parityArr.Length; i++)
                parityArr[i] = (uint)parity;

            await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var ok = await _driver.SendDataSingleAsync(txChannelIndex, data, parityArr).ConfigureAwait(false);
                if (!ok)
                    throw new InvalidOperationException($"SendDataSingleAsync failed (tx={txChannelIndex}, count={data.Length})");
            }
            finally
            {
                _ioLock.Release();
            }
        }

        public async Task SendPayload8Async(int txChannelIndex, byte label, byte[] payload8, Art4229Parity parity = Art4229Parity.Odd, CancellationToken cancellationToken = default)
        {
            EnsureConnected();
            if (payload8 == null)
                throw new ArgumentNullException(nameof(payload8));
            if (payload8.Length != 8)
                throw new ArgumentException("payload8 must be 8 bytes", nameof(payload8));

            var data429 = new uint[4];
            for (byte frag = 0; frag < 4; frag++)
            {
                ushort part = (ushort)((payload8[frag * 2] << 8) | payload8[frag * 2 + 1]);
                data429[frag] = BuildWord(label, frag, part);
            }

            await SendWordsSingleAsync(txChannelIndex, data429, parity, cancellationToken).ConfigureAwait(false);
        }

        public async Task<IReadOnlyList<Art4229RxWord>> ReadRxWordsAsync(int rxChannelIndex, uint maxCount = 1024, bool enableTimeTag = false, bool enableRateAdaption = false, CancellationToken cancellationToken = default)
        {
            EnsureConnected();

            await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var list = await _driver.ReadReceiveDataAsync(rxChannelIndex, maxCount, enableTimeTag, enableRateAdaption).ConfigureAwait(false);
                if (list == null || list.Count == 0)
                    return Array.Empty<Art4229RxWord>();

                return list.Select(x => new Art4229RxWord(x.Data429, x.Rate, x.TimeHigh, x.TimeLow)).ToList();
            }
            finally
            {
                _ioLock.Release();
            }
        }

        public async Task<byte[]> WaitPayload8Async(
            int rxChannelIndex,
            byte expectedLabel,
            Func<byte[], bool> accept,
            int timeoutMs,
            CancellationToken cancellationToken = default)
        {
            EnsureConnected();
            if (timeoutMs <= 0)
                throw new ArgumentOutOfRangeException(nameof(timeoutMs));

            var assembler = new MultiFrameCommandAssembler();
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);

            while (!cancellationToken.IsCancellationRequested && DateTime.UtcNow <= deadline)
            {
                var words = await ReadRxWordsAsync(rxChannelIndex, maxCount: 256, enableTimeTag: false, enableRateAdaption: false, cancellationToken: cancellationToken).ConfigureAwait(false);
                if (words.Count > 0)
                {
                    foreach (var w in words)
                    {
                        if (!TryParseWord(w.Data429, out var label, out var sdi, out var payload16))
                            continue;

                        if (label != expectedLabel)
                            continue;

                        if (assembler.TryAddFragment(label, sdi, payload16, DateTime.UtcNow, out var cmd8) && cmd8 != null)
                        {
                            if (accept == null || accept(cmd8))
                                return cmd8;
                        }
                    }
                }

                await Task.Delay(10, cancellationToken).ConfigureAwait(false);
            }

            return null;
        }

        public int ParseChannelIndex(string channel)
        {
            if (string.IsNullOrWhiteSpace(channel))
                throw new ArgumentException("Channel is required", nameof(channel));

            var raw = channel.Trim();
            if (raw.StartsWith("429_CH", StringComparison.OrdinalIgnoreCase))
            {
                var idxText = raw.Substring("429_CH".Length);
                if (!int.TryParse(idxText, out var idx))
                    throw new ArgumentException("Invalid channel", nameof(channel));
                return idx;
            }

            if (int.TryParse(raw, out var asInt))
                return asInt;

            throw new ArgumentException("Channel must be like '429_CH0' or an integer index", nameof(channel));
        }

        /// <summary>将4字节转换为单精度浮点数（Big-Endian）</summary>
        public float BytesToFloat(byte[] bytes, int offset = 0)
        {
            if (bytes == null || bytes.Length < offset + 4)
                throw new ArgumentException("bytes must have at least 4 bytes from offset", nameof(bytes));

            // Big-Endian: bytes[0] is MSB
            byte[] leBytes = new byte[4];
            if (BitConverter.IsLittleEndian)
            {
                leBytes[0] = bytes[offset + 3];
                leBytes[1] = bytes[offset + 2];
                leBytes[2] = bytes[offset + 1];
                leBytes[3] = bytes[offset + 0];
            }
            else
            {
                Array.Copy(bytes, offset, leBytes, 0, 4);
            }
            return BitConverter.ToSingle(leBytes, 0);
        }

        /// <summary>将单精度浮点数转换为4字节（Big-Endian）</summary>
        public byte[] FloatToBytes(float value)
        {
            byte[] leBytes = BitConverter.GetBytes(value);
            byte[] beBytes = new byte[4];
            if (BitConverter.IsLittleEndian)
            {
                beBytes[0] = leBytes[3];
                beBytes[1] = leBytes[2];
                beBytes[2] = leBytes[1];
                beBytes[3] = leBytes[0];
            }
            else
            {
                Array.Copy(leBytes, beBytes, 4);
            }
            return beBytes;
        }

        /// <summary>从8字节payload中提取后4字节并转换为浮点数</summary>
        public float ExtractFloatFromPayload8(byte[] payload8)
        {
            if (payload8 == null || payload8.Length != 8)
                throw new ArgumentException("payload8 must be 8 bytes", nameof(payload8));

            return BytesToFloat(payload8, 4);
        }

        /// <summary>构建8字节payload：前4字节为指令，后4字节为浮点数</summary>
        public byte[] BuildPayload8WithFloat(byte[] cmd4, float value)
        {
            if (cmd4 == null || cmd4.Length != 4)
                throw new ArgumentException("cmd4 must be 4 bytes", nameof(cmd4));

            byte[] payload8 = new byte[8];
            Array.Copy(cmd4, 0, payload8, 0, 4);
            byte[] floatBytes = FloatToBytes(value);
            Array.Copy(floatBytes, 0, payload8, 4, 4);
            return payload8;
        }

        /// <summary>构建原始429字（含Label、SDI、19bit数据、SSM、奇偶校验）</summary>
        public uint BuildRawWord(byte label, byte sdi, uint data19, byte ssm = 0, bool applyOddParity = true)
        {
            // ARINC429 32-bit word layout:
            // Bit 0-7:   Label (LSB first, reversed)
            // Bit 8-9:   SDI
            // Bit 10-28: Data (19 bits)
            // Bit 29-30: SSM
            // Bit 31:    Parity
            uint word = 0;
            word |= label;                          // Bit 0-7
            word |= (uint)(sdi & 0x3) << 8;         // Bit 8-9
            word |= (data19 & 0x7FFFF) << 10;       // Bit 10-28
            word |= (uint)(ssm & 0x3) << 29;        // Bit 29-30

            if (applyOddParity)
            {
                word = ApplyOddParity(word);
            }
            return word;
        }

        /// <summary>解析原始429字（提取Label、SDI、19bit数据、SSM）</summary>
        public void ParseRawWord(uint word, out byte label, out byte sdi, out uint data19, out byte ssm)
        {
            label = (byte)(word & 0xFF);
            sdi = (byte)((word >> 8) & 0x3);
            data19 = (word >> 10) & 0x7FFFF;
            ssm = (byte)((word >> 29) & 0x3);
        }

        /// <summary>构建BNR格式数据（有符号定点数）</summary>
        public uint EncodeBnr(double value, int bitLength, double resolution, int msbPosition = 28)
        {
            // BNR: Binary Number Representation (signed)
            // MSB is sign bit, remaining bits are magnitude
            int maxVal = (1 << (bitLength - 1)) - 1;
            int minVal = -(1 << (bitLength - 1));
            int scaled = (int)Math.Round(value / resolution);
            scaled = Math.Max(minVal, Math.Min(maxVal, scaled));

            uint encoded;
            if (scaled < 0)
            {
                // Two's complement
                encoded = (uint)((1 << bitLength) + scaled);
            }
            else
            {
                encoded = (uint)scaled;
            }

            // Shift to correct position (MSB at msbPosition, data grows downward)
            int shift = msbPosition - bitLength + 1 - 10; // -10 because data starts at bit 10
            if (shift > 0)
                encoded <<= shift;
            else if (shift < 0)
                encoded >>= -shift;

            return encoded & 0x7FFFF;
        }

        /// <summary>解析BNR格式数据（有符号定点数）</summary>
        public double DecodeBnr(uint data19, int bitLength, double resolution, int msbPosition = 28)
        {
            // Extract the relevant bits
            int shift = msbPosition - bitLength + 1 - 10;
            uint extracted;
            if (shift > 0)
                extracted = data19 >> shift;
            else if (shift < 0)
                extracted = data19 << -shift;
            else
                extracted = data19;

            uint mask = (1u << bitLength) - 1;
            extracted &= mask;

            // Check sign bit and convert from two's complement
            int signBit = 1 << (bitLength - 1);
            int value;
            if ((extracted & signBit) != 0)
            {
                value = (int)extracted - (1 << bitLength);
            }
            else
            {
                value = (int)extracted;
            }

            return value * resolution;
        }

        /// <summary>构建UBNR格式数据（无符号定点数）</summary>
        public uint EncodeUbnr(double value, int bitLength, double resolution, int msbPosition = 28)
        {
            // UBNR: Unsigned Binary Number Representation
            uint maxVal = (1u << bitLength) - 1;
            uint scaled = (uint)Math.Round(Math.Max(0, value) / resolution);
            scaled = Math.Min(maxVal, scaled);

            int shift = msbPosition - bitLength + 1 - 10;
            if (shift > 0)
                scaled <<= shift;
            else if (shift < 0)
                scaled >>= -shift;

            return scaled & 0x7FFFF;
        }

        /// <summary>解析UBNR格式数据（无符号定点数）</summary>
        public double DecodeUbnr(uint data19, int bitLength, double resolution, int msbPosition = 28)
        {
            int shift = msbPosition - bitLength + 1 - 10;
            uint extracted;
            if (shift > 0)
                extracted = data19 >> shift;
            else if (shift < 0)
                extracted = data19 << -shift;
            else
                extracted = data19;

            uint mask = (1u << bitLength) - 1;
            extracted &= mask;

            return extracted * resolution;
        }

        /// <summary>应用奇校验到429字</summary>
        private static uint ApplyOddParity(uint word)
        {
            int count = 0;
            uint temp = word & 0x7FFFFFFF; // Exclude parity bit
            while (temp != 0)
            {
                count += (int)(temp & 1);
                temp >>= 1;
            }
            // Set parity bit to make total number of 1s odd
            if ((count & 1) == 0)
                word |= 0x80000000;
            else
                word &= 0x7FFFFFFF;
            return word;
        }

        /// <summary>验证429字的奇校验是否正确</summary>
        public bool VerifyOddParity(uint word)
        {
            int count = 0;
            uint temp = word;
            while (temp != 0)
            {
                count += (int)(temp & 1);
                temp >>= 1;
            }
            // Odd parity: total number of 1s should be odd
            return (count & 1) == 1;
        }

        /// <summary>反转Label字节的位序（ARINC429标准要求Label低LSB先发）</summary>
        public byte ReverseLabel(byte label)
        {
            // Reverse bits in a byte
            byte result = 0;
            for (int i = 0; i < 8; i++)
            {
                result <<= 1;
                result |= (byte)(label & 1);
                label >>= 1;
            }
            return result;
        }

        private void EnsureConnected()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(Art4229Api));

            if (_driver == null || !_driver.IsConnected)
                throw new InvalidOperationException("ART4229 is not connected");
        }

        /// <summary>解析 429 字：提取 label、SDI、16bit payload</summary>
        private static bool TryParseWord(uint raw, out byte label, out byte sdi, out ushort payload16)
        {
            label = (byte)(raw & 0xFF);
            sdi = (byte)((raw >> 8) & 0x3);
            uint data19 = (raw >> 10) & 0x1FFFF;
            payload16 = (ushort)(data19 & 0xFFFF);
            return true;
        }

        /// <summary>构建 429 字：label + SDI + 16bit payload（用于8字节拼帧，自动应用奇校验）</summary>
        private static uint BuildWord(byte label, byte sdi, ushort payload16)
        {
            uint word = 0;
            word |= label;
            word |= (uint)(sdi & 0x3) << 8;
            word |= (uint)payload16 << 10;
            return ApplyOddParity(word);
        }

        /// <summary>多帧组装器：按 label+SDI(0..3) 拼成 8 字节</summary>
        private sealed class MultiFrameCommandAssembler
        {
            private readonly ushort[] _parts = new ushort[4];
            private int _mask;
            private byte _label;
            private DateTime _firstSeenUtc;
            private static readonly TimeSpan AssemblyTimeout = TimeSpan.FromMilliseconds(200);

            public bool TryAddFragment(byte label, byte sdi, ushort payload16, DateTime nowUtc, out byte[] cmd8)
            {
                cmd8 = null;
                if (sdi > 3)
                    return false;

                if (_mask == 0 || label != _label || (nowUtc - _firstSeenUtc) > AssemblyTimeout)
                {
                    _label = label;
                    _mask = 0;
                    _firstSeenUtc = nowUtc;
                }

                _parts[sdi] = payload16;
                _mask |= (1 << sdi);

                if (_mask != 0b1111)
                    return false;

                cmd8 = new byte[8];
                for (int i = 0; i < 4; i++)
                {
                    cmd8[i * 2] = (byte)((_parts[i] >> 8) & 0xFF);
                    cmd8[i * 2 + 1] = (byte)(_parts[i] & 0xFF);
                }

                _mask = 0;
                return true;
            }
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
                try { _lifecycleLock.Dispose(); } catch { }
                try { _ioLock.Dispose(); } catch { }
            }
        }
    }
}
