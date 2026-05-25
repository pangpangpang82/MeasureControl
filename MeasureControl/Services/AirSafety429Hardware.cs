using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MeasureControl.Models.Devices;
using MeasureControl.Services.HardwareApis;

namespace MeasureControl.Services
{
    public sealed class AirSafety429Hardware : IDisposable
    {
        private static readonly byte[] AirBenchTxFragmentLabels = { 0x8C, 0x4C, 0xCC, 0x2C };
        private static readonly byte[] AirProductTxFragmentLabels = { 0x90, 0x50, 0xD0, 0x30 };

        private IArt4229Api _arinc;
        private int _txChannelIndex = -1;
        private int _rxChannelIndex = -1;
        private bool _txOpened;
        private bool _rxOpened;
        private bool _started;

        public double ArincRate { get; set; } = 100000.0;
        public int ArincDeviceIndex { get; set; } = 0;

        public async Task StartAsync(string txChannel, string rxChannel, Action<string> log)
        {
            if (_started)
                return;

            if (log == null) log = _ => { };

            int txIndex = ParseChannelIndex(txChannel);
            int rxIndex = ParseChannelIndex(rxChannel);
            if (txIndex == rxIndex)
                throw new InvalidOperationException($"AIR429 TX/RX 通道冲突：TX={txIndex}, RX={rxIndex}");

            _arinc = new Art4229Api(new Arinc429Device("PXIe-4227", "Slot0")
            {
                Model = "PXIe-4227",
                Name = "PXIe-4227",
                SlotIndex = ArincDeviceIndex
            }, ArincDeviceIndex);

            await _arinc.ConnectAsync().ConfigureAwait(false);
            await OpenAndConfigureTxAsync(txIndex, CancellationToken.None).ConfigureAwait(false);
            await OpenAndConfigureRxAsync(rxIndex, CancellationToken.None).ConfigureAwait(false);

            _started = true;
            log($"[{DateTime.Now:HH:mm:ss}] [AIR429] 硬件429已就绪：TX={txIndex}(parity=Odd), RX={rxIndex}(parity=Odd), wordFormat=Standard429");
        }

        public async Task StopAsync(Action<string> log)
        {
            if (log == null) log = _ => { };
            await CleanupAsync().ConfigureAwait(false);
            log($"[{DateTime.Now:HH:mm:ss}] [AIR429] 硬件429已关闭");
        }

        public async Task ClearRxFifoAsync(string rxChannel)
        {
            if (_arinc == null || !_arinc.IsConnected)
                return;

            int rxIndex = ParseChannelIndex(rxChannel);
            try
            {
                await _arinc.ReadRxWordsAsync(rxIndex, maxCount: 4096, enableTimeTag: false, enableRateAdaption: false).ConfigureAwait(false);
            }
            catch
            {
            }
        }

        public async Task SendAirCommandOnlyAsync(string txChannel, byte[] command8, Action<string> log, CancellationToken token)
        {
            if (!_started || _arinc == null || !_arinc.IsConnected)
                throw new InvalidOperationException("AIR429 hardware not started");
            if (command8 == null || command8.Length != 8)
                throw new ArgumentException("command8 must be 8 bytes", nameof(command8));

            int txIndex = ParseChannelIndex(txChannel);
            if (!_txOpened || _txChannelIndex != txIndex)
                await OpenAndConfigureTxAsync(txIndex, token).ConfigureAwait(false);

            var words = BuildAirMultiFrameWordsByApi(command8);
            log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [AIR429] 发送：TX={txIndex}, parity=Odd, labels={string.Join("/", AirBenchTxFragmentLabels.Select(x => $"0x{x:X2}"))}, payload8={FormatBytes(command8)}");
            for (int i = 0; i < words.Count; i++)
                log?.Invoke($"  word[{i}]=0x{words[i]:X8}  bit31={(words[i] >> 31)}  oddOk={_arinc.VerifyOddParity(words[i])}");
            await _arinc.SendWordsSingleAsync(txIndex, words, Art4229Parity.Odd, token).ConfigureAwait(false);
            log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [AIR429] SendWordsSingleAsync 已完成 (pTxParity=ODD)");
        }

        public async Task<byte[]> WaitAirResponseAsync(string rxChannel, Func<byte[], bool> isExpectedResponse, int timeoutMs, Action<string> log, CancellationToken token)
        {
            if (!_started || _arinc == null || !_arinc.IsConnected)
                throw new InvalidOperationException("AIR429 hardware not started");

            int rxIndex = ParseChannelIndex(rxChannel);
            if (!_rxOpened || _rxChannelIndex != rxIndex)
                await OpenAndConfigureRxAsync(rxIndex, token).ConfigureAwait(false);

            var assembler = new MultiLabelCommandAssembler(AirProductTxFragmentLabels, reversePayloadBytes: true);
            var deadline = DateTime.UtcNow.AddMilliseconds(Math.Max(100, timeoutMs));
            var rxLogCount = 0;
            const int maxRxLog = 32;

            while (!token.IsCancellationRequested && DateTime.UtcNow <= deadline)
            {
                var words = await _arinc.ReadRxWordsAsync(rxIndex, maxCount: 256, enableTimeTag: false, enableRateAdaption: false, cancellationToken: token).ConfigureAwait(false);
                if (words != null && words.Count > 0)
                {
                    foreach (var word in words)
                    {
                        TryParseWord(word.Data429, out var label, out var sdi, out var payload16);

                        if (log != null && rxLogCount < maxRxLog)
                        {
                            rxLogCount++;
                            log($"[{DateTime.Now:HH:mm:ss}] [AIR429] RX word=0x{word.Data429:X8}, label=0x{label:X2}, sdi={sdi}, payload16=0x{payload16:X4}");
                        }

                        if (assembler.TryAddFragment(label, payload16, DateTime.UtcNow, out var payload8) && payload8 != null)
                        {
                            if (isExpectedResponse == null || isExpectedResponse(payload8))
                            {
                                log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [AIR429] 接收拼包完成：RX={rxIndex}, labels={string.Join("/", AirProductTxFragmentLabels.Select(x => $"0x{x:X2}"))}, payload8={FormatBytes(payload8)}");
                                return payload8;
                            }
                        }
                    }
                }

                await Task.Delay(10, token).ConfigureAwait(false);
            }

            return null;
        }

        public Task<byte[]> SendBenchCommandAndWaitAsync(string txChannel, string rxChannel, byte label, byte[] command8, Func<byte[], bool> isExpectedResponse, int timeoutMs, Action<string> log, CancellationToken token)
            => throw new NotSupportedException("AirSafety429Hardware 只支持空气安全板硬件429协议，不支持旧 bench/PT500 协议");

        public Task SendBenchCommandOnlyAsync(string txChannel, byte label, byte[] command8, Action<string> log, CancellationToken token)
            => throw new NotSupportedException("AirSafety429Hardware 只支持空气安全板硬件429协议，不支持旧 bench/PT500 协议");

        public Task<byte[]> WaitBenchResponseAsync(string rxChannel, byte label, Func<byte[], bool> isExpectedResponse, int timeoutMs, Action<string> log, CancellationToken token)
            => throw new NotSupportedException("AirSafety429Hardware 只支持空气安全板硬件429协议，不支持旧 bench/PT500 协议");

        private async Task OpenAndConfigureTxAsync(int txIndex, CancellationToken token)
        {
            await _arinc.OpenTxAsync(txIndex, token).ConfigureAwait(false);
            try { await _arinc.StopTxAsync(txIndex, token).ConfigureAwait(false); } catch { }
            await _arinc.ConfigureTxAsync(txIndex, ArincRate, Art4229TxMode.Single, Art4229Parity.Odd, Art4229WordFormat.Standard429, token).ConfigureAwait(false);
            _txChannelIndex = txIndex;
            _txOpened = true;
        }

        private async Task OpenAndConfigureRxAsync(int rxIndex, CancellationToken token)
        {
            await _arinc.OpenRxAsync(rxIndex, token).ConfigureAwait(false);
            await _arinc.ConfigureRxAsync(rxIndex, ArincRate, Art4229Parity.Odd, Art4229WordFormat.Standard429, enableInterrupt: false, interruptDepth: 512, enableTimeTag: false, cancellationToken: token).ConfigureAwait(false);
            await _arinc.StartRxAsync(rxIndex, token).ConfigureAwait(false);
            _rxChannelIndex = rxIndex;
            _rxOpened = true;
        }

        private async Task CleanupAsync()
        {
            if (_arinc != null)
            {
                try { if (_rxOpened && _rxChannelIndex >= 0) await _arinc.StopRxAsync(_rxChannelIndex, CancellationToken.None).ConfigureAwait(false); } catch { }
                try { if (_rxOpened && _rxChannelIndex >= 0) await _arinc.CloseRxAsync(_rxChannelIndex, CancellationToken.None).ConfigureAwait(false); } catch { }
                try { if (_txOpened && _txChannelIndex >= 0) await _arinc.StopTxAsync(_txChannelIndex, CancellationToken.None).ConfigureAwait(false); } catch { }
                try { if (_txOpened && _txChannelIndex >= 0) await _arinc.CloseTxAsync(_txChannelIndex, CancellationToken.None).ConfigureAwait(false); } catch { }
                try { await _arinc.DisconnectAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                try { await _arinc.DisposeAsync().ConfigureAwait(false); } catch { }
            }

            _arinc = null;
            _txChannelIndex = -1;
            _rxChannelIndex = -1;
            _txOpened = false;
            _rxOpened = false;
            _started = false;
        }

        private IReadOnlyList<uint> BuildAirMultiFrameWordsByApi(byte[] payload8)
        {
            if (_arinc == null)
                throw new InvalidOperationException("AIR429 not connected");

            // 与 S_C_8_3_1Simulation 保持一致：先交换字节对，再用 (high << 8) | low 构建
            var swapped = SwapPairs8(payload8);
            var words = new uint[4];
            for (byte i = 0; i < 4; i++)
            {
                ushort part = (ushort)((swapped[i * 2] << 8) | swapped[i * 2 + 1]);
                words[i] = BuildWord(AirBenchTxFragmentLabels[i], 0, part);
            }

            return words;
        }

        private static byte[] SwapPairs8(byte[] data8)
        {
            if (data8 == null || data8.Length != 8)
                return data8;

            var b = new byte[8];
            for (int i = 0; i < 8; i += 2)
            {
                b[i] = data8[i + 1];
                b[i + 1] = data8[i];
            }
            return b;
        }

        private static uint BuildWord(byte label, byte sdi, ushort payload16)
        {
            uint word = 0;
            word |= label;
            word |= (uint)(sdi & 0x3) << 8;
            word |= (uint)payload16 << 10;
            return ApplyOddParity(word);
        }

        private static uint ApplyOddParity(uint word)
        {
            uint data = word & 0x7FFFFFFF;
            int ones = 0;
            uint temp = data;
            while (temp != 0)
            {
                temp &= temp - 1;
                ones++;
            }

            return (ones % 2 == 0) ? (data | 0x80000000) : data;
        }

        private static void TryParseWord(uint raw, out byte label, out byte sdi, out ushort payload16)
        {
            label = (byte)(raw & 0xFF);
            sdi = (byte)((raw >> 8) & 0x3);
            uint data19 = (raw >> 10) & 0x1FFFF;
            payload16 = (ushort)(data19 & 0xFFFF);
        }

        private static int ParseChannelIndex(string channel)
        {
            if (string.IsNullOrWhiteSpace(channel))
                throw new ArgumentException("channel is empty", nameof(channel));

            var text = channel.Trim();
            const string prefix = "429_CH";
            if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                text = text.Substring(prefix.Length);
            else if (text.StartsWith("CH", StringComparison.OrdinalIgnoreCase))
                text = text.Substring(2);

            if (!int.TryParse(text, out var index))
                throw new ArgumentException($"无法解析429通道: {channel}", nameof(channel));
            return index;
        }

        private static string FormatBytes(byte[] data)
            => data == null ? "" : string.Join(" ", data.Select(b => b.ToString("X2")));

        public void Dispose()
        {
            try { CleanupAsync().GetAwaiter().GetResult(); } catch { }
        }

        private sealed class MultiLabelCommandAssembler
        {
            private readonly ushort[] _parts = new ushort[4];
            private readonly bool[] _received = new bool[4];
            private readonly byte[] _labels;
            private readonly bool _reversePayloadBytes;
            private DateTime _firstSeenUtc;
            private static readonly TimeSpan AssemblyTimeout = TimeSpan.FromMilliseconds(200);

            public MultiLabelCommandAssembler(byte[] labels, bool reversePayloadBytes)
            {
                _labels = labels ?? throw new ArgumentNullException(nameof(labels));
                _reversePayloadBytes = reversePayloadBytes;
            }

            public bool TryAddFragment(byte label, ushort payload16, DateTime timestamp, out byte[] payload8)
            {
                payload8 = null;
                if (_received.All(x => !x))
                    _firstSeenUtc = timestamp;
                else if ((timestamp - _firstSeenUtc) > AssemblyTimeout)
                {
                    Array.Clear(_received, 0, _received.Length);
                    Array.Clear(_parts, 0, _parts.Length);
                    _firstSeenUtc = timestamp;
                }

                int index = Array.IndexOf(_labels, label);
                if (index < 0 || index >= 4)
                    return false;

                _parts[index] = payload16;
                _received[index] = true;

                for (int i = 0; i < 4; i++)
                {
                    if (!_received[i])
                        return false;
                }

                payload8 = new byte[8];
                for (int i = 0; i < 4; i++)
                {
                    if (_reversePayloadBytes)
                    {
                        payload8[i * 2] = (byte)(_parts[i] & 0xFF);
                        payload8[i * 2 + 1] = (byte)((_parts[i] >> 8) & 0xFF);
                    }
                    else
                    {
                        payload8[i * 2] = (byte)((_parts[i] >> 8) & 0xFF);
                        payload8[i * 2 + 1] = (byte)(_parts[i] & 0xFF);
                    }
                }

                Array.Clear(_received, 0, _received.Length);
                Array.Clear(_parts, 0, _parts.Length);
                return true;
            }
        }
    }
}
