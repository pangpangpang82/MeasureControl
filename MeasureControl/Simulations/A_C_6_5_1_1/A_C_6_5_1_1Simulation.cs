using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MeasureControl.Simulations.Common;

namespace MeasureControl.Simulations.A_C_6_5_1_1
{
    public sealed class A_C_6_5_1_1Simulation : ARINC429SimulationBase
    {
        private readonly MultiLabelCommandAssembler _rxLabelAssembler = new MultiLabelCommandAssembler(BenchTxFragmentLabels);

        private static readonly byte[] EnterAtpCommand8 = { 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] EnterAtpOk8 = { 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x02 };
        private static readonly byte[] ExitAtpCommand8 = { 0x00, 0x02, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01 };
        private static readonly byte[] ExitAtpOk8 = { 0x00, 0x02, 0x00, 0x01, 0x00, 0x00, 0x00, 0x03 };

        private static readonly byte[] AbA429Tx0TransmitCommand8 = { 0x04, 0x01, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] FixedSendData4 = { 0x7F, 0x00, 0xAA, 0x55 };
        private static readonly byte[] FixedSendData8 = { 0x7F, 0x00, 0xAA, 0x55, 0x00, 0x00, 0x00, 0x00 };

        private static readonly byte[] BenchTxFragmentLabels = { 0x31, 0x32, 0x33, 0x34 };
        private static readonly byte[] ProductTxFragmentLabels = { 0x09, 0x0A, 0x0B, 0x0C };

        public async Task SendBenchCommandOnlyAsync(string benchTxChannel, byte[] command8, Action<string> log, CancellationToken token)
        {
            if (!_started || _arincDriver == null)
                throw new InvalidOperationException("Simulation not started");
            if (command8 == null || command8.Length != 8)
                throw new ArgumentException("command8 must be 8 bytes", nameof(command8));

            int txIndex = ParseChannelIndex(benchTxChannel);

            log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] bench发送: tx={txIndex}, labels={string.Join("/", BenchTxFragmentLabels.Select(b => $"0x{b:X2}"))}, payload8={FormatBytes(command8)}");

            await SendMultiLabelFrameOnChannelAsync(txIndex, BenchTxFragmentLabels, command8, log, token);
        }

        public async Task<byte[]> WaitBenchData4Async(string benchRxChannel, byte label0, byte label1, int timeoutMs, Action<string> log, CancellationToken token)
        {
            if (!_started || _arincDriver == null)
                throw new InvalidOperationException("Simulation not started");

            int rxIndex = ParseChannelIndex(benchRxChannel);
            var deadline = DateTime.UtcNow.AddMilliseconds(Math.Max(100, timeoutMs));

            ushort? part0 = null;
            ushort? part1 = null;
            var firstSeen = DateTime.UtcNow;
            var assemblyTimeout = TimeSpan.FromMilliseconds(200);

            while (!token.IsCancellationRequested && DateTime.UtcNow <= deadline)
            {
                var list = await _arincDriver.ReadReceiveDataAsync(rxIndex, maxCount: 256, enableTimeTag: false, enableRateAdaption: false);
                if (list != null && list.Count > 0)
                {
                    foreach (var item in list)
                    {
                        if (!TryParseWord(item.Data429, out var rxLabel, out var sdi, out var payload))
                            continue;

                        if ((DateTime.UtcNow - firstSeen) > assemblyTimeout)
                        {
                            part0 = null;
                            part1 = null;
                            firstSeen = DateTime.UtcNow;
                        }

                        if (rxLabel == label0)
                        {
                            part0 = payload;
                        }
                        else if (rxLabel == label1)
                        {
                            part1 = payload;
                        }

                        if (part0.HasValue && part1.HasValue)
                        {
                            var b = new byte[4];
                            b[0] = (byte)((part0.Value >> 8) & 0xFF);
                            b[1] = (byte)(part0.Value & 0xFF);
                            b[2] = (byte)((part1.Value >> 8) & 0xFF);
                            b[3] = (byte)(part1.Value & 0xFF);

                            log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] benchRX={rxIndex} 4字节拼包完成 labels=0x{label0:X2}/0x{label1:X2} data4={FormatBytes(b)}");
                            return b;
                        }
                    }
                }

                await Task.Delay(10, token);
            }

            return null;
        }

        public async Task<byte[]> WaitBenchData8Async(string benchRxChannel, byte label0, byte label1, byte label2, byte label3, int timeoutMs, Action<string> log, CancellationToken token)
        {
            if (!_started || _arincDriver == null)
                throw new InvalidOperationException("Simulation not started");

            int rxIndex = ParseChannelIndex(benchRxChannel);
            var labelAssembler = new MultiLabelCommandAssembler(new[] { label0, label1, label2, label3 });
            var deadline = DateTime.UtcNow.AddMilliseconds(Math.Max(100, timeoutMs));

            while (!token.IsCancellationRequested && DateTime.UtcNow <= deadline)
            {
                var list = await _arincDriver.ReadReceiveDataAsync(rxIndex, maxCount: 256, enableTimeTag: false, enableRateAdaption: false);
                if (list != null && list.Count > 0)
                {
                    foreach (var item in list)
                    {
                        if (!TryParseWord(item.Data429, out var rxLabel, out var sdi, out var payload))
                            continue;

                        if (labelAssembler.TryAddFragment(rxLabel, payload, DateTime.UtcNow, out var resp8) && resp8 != null)
                        {
                            log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] benchRX={rxIndex} 4包拼包完成 labels=0x{label0:X2}/0x{label1:X2}/0x{label2:X2}/0x{label3:X2} data8={FormatBytes(resp8)}");
                            return resp8;
                        }
                    }
                }

                await Task.Delay(10, token);
            }

            return null;
        }

        public async Task<byte[]> WaitBenchResponse8Async(string benchRxChannel, Func<byte[], bool> isExpected, int timeoutMs, Action<string> log, CancellationToken token)
        {
            if (!_started || _arincDriver == null)
                throw new InvalidOperationException("Simulation not started");

            int rxIndex = ParseChannelIndex(benchRxChannel);
            var labelAssembler = new MultiLabelCommandAssembler(ProductTxFragmentLabels);
            var deadline = DateTime.UtcNow.AddMilliseconds(Math.Max(100, timeoutMs));
            int rxLogCount = 0;
            const int maxRxLog = 32;

            while (!token.IsCancellationRequested && DateTime.UtcNow <= deadline)
            {
                var list = await _arincDriver.ReadReceiveDataAsync(rxIndex, maxCount: 256, enableTimeTag: false, enableRateAdaption: false);
                if (list != null && list.Count > 0)
                {
                    foreach (var item in list)
                    {
                        if (!TryParseWord(item.Data429, out var rxLabel, out var sdi, out var payload))
                            continue;

                        if (EnableFrameLogging && rxLogCount < maxRxLog)
                        {
                            log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] benchRX={rxIndex} recv raw=0x{item.Data429:X8} label=0x{rxLabel:X2} sdi={sdi} payload=0x{payload:X4}");
                            rxLogCount++;
                            if (rxLogCount == maxRxLog)
                            {
                                log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] benchRX={rxIndex} recv日志已达上限({maxRxLog})，后续帧不再打印");
                            }
                        }

                        if (labelAssembler.TryAddFragment(rxLabel, payload, DateTime.UtcNow, out var resp8) && resp8 != null)
                        {
                            if (isExpected == null || isExpected(resp8))
                            {
                                log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] benchRX={rxIndex} 拼包完成 labels={string.Join("/", ProductTxFragmentLabels.Select(b => $"0x{b:X2}"))} resp8={FormatBytes(resp8)}");
                                return resp8;
                            }
                        }
                    }
                }

                await Task.Delay(10, token);
            }

            return null;
        }

        public async Task<byte[]> WaitBenchResponse4Async(string benchRxChannel, int timeoutMs, Action<string> log, CancellationToken token)
        {
            if (!_started || _arincDriver == null)
                throw new InvalidOperationException("Simulation not started");

            int rxIndex = ParseChannelIndex(benchRxChannel);
            var deadline = DateTime.UtcNow.AddMilliseconds(Math.Max(100, timeoutMs));

            ushort? part0 = null;
            ushort? part1 = null;
            var firstSeen = DateTime.UtcNow;
            var assemblyTimeout = TimeSpan.FromMilliseconds(200);

            while (!token.IsCancellationRequested && DateTime.UtcNow <= deadline)
            {
                var list = await _arincDriver.ReadReceiveDataAsync(rxIndex, maxCount: 256, enableTimeTag: false, enableRateAdaption: false);
                if (list != null && list.Count > 0)
                {
                    foreach (var item in list)
                    {
                        if (!TryParseWord(item.Data429, out var rxLabel, out var sdi, out var payload))
                            continue;

                        if ((DateTime.UtcNow - firstSeen) > assemblyTimeout)
                        {
                            part0 = null;
                            part1 = null;
                            firstSeen = DateTime.UtcNow;
                        }

                        if (rxLabel == ProductTxFragmentLabels[0])
                        {
                            part0 = payload;
                        }
                        else if (rxLabel == ProductTxFragmentLabels[1])
                        {
                            part1 = payload;
                        }

                        if (part0.HasValue && part1.HasValue)
                        {
                            var b = new byte[4];
                            b[0] = (byte)((part0.Value >> 8) & 0xFF);
                            b[1] = (byte)(part0.Value & 0xFF);
                            b[2] = (byte)((part1.Value >> 8) & 0xFF);
                            b[3] = (byte)(part1.Value & 0xFF);

                            log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] benchRX={rxIndex} 4字节拼包完成 labels=0x{ProductTxFragmentLabels[0]:X2}/0x{ProductTxFragmentLabels[1]:X2} data4={FormatBytes(b)}");
                            return b;
                        }
                    }
                }

                await Task.Delay(10, token);
            }

            return null;
        }

        protected override async Task StartSimProductRxAsync(Action<string> log, CancellationToken token)
        {
            if (_arincDriver == null)
                throw new InvalidOperationException("ARINC driver not initialized");

            bool ok = await _arincDriver.StartReceiveAsync(SimProductRxChannelIndex);
            if (!ok)
                throw new InvalidOperationException($"[SIM] 启动接收失败: RX={SimProductRxChannelIndex}");

            log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] 产品侧接收已启动: simRX={SimProductRxChannelIndex} (将回包到simTX={SimProductTxChannelIndex})");

            _rxLoopTask = Task.Run(() => SimProductRxLoopAsync(log, token), token);
        }

        private async Task SimProductRxLoopAsync(Action<string> log, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var list = await _arincDriver.ReadReceiveDataAsync(SimProductRxChannelIndex, maxCount: 1024, enableTimeTag: false, enableRateAdaption: false);
                    if (list != null && list.Count > 0)
                    {
                        foreach (var item in list)
                        {
                            if (TryParseWord(item.Data429, out byte label, out byte sdi, out ushort payload))
                            {
                                if (_rxLabelAssembler.TryAddFragment(label, payload, DateTime.UtcNow, out var cmd8) && cmd8 != null)
                                {
                                    log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] simRX={SimProductRxChannelIndex} 拼包完成 cmd8={FormatBytes(cmd8)}");

                                    if (cmd8.SequenceEqual(EnterAtpCommand8))
                                    {
                                        log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] 产品侧收到进入ATP -> 回复OK");
                                        await SendMultiLabelFrameOnChannelAsync(SimProductTxChannelIndex, ProductTxFragmentLabels, EnterAtpOk8, log, token);
                                    }
                                    else if (cmd8.SequenceEqual(ExitAtpCommand8))
                                    {
                                        log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] 产品侧收到退出ATP -> 回复OK");
                                        await SendMultiLabelFrameOnChannelAsync(SimProductTxChannelIndex, ProductTxFragmentLabels, ExitAtpOk8, log, token);
                                    }
                                    else if (cmd8.SequenceEqual(AbA429Tx0TransmitCommand8))
                                    {
                                        log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] 产品侧收到AB_A429TX0_TRANSMIT -> 发送测试信息");
                                        await Task.Delay(30, token);
                                        await SendMultiLabelFrameOnChannelAsync(SimProductTxChannelIndex, ProductTxFragmentLabels, FixedSendData8, log, token);
                                    }
                                }
                            }
                        }
                    }

                    await Task.Delay(20, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    try
                    {
                        await Task.Delay(100, token);
                    }
                    catch
                    {
                        break;
                    }
                }
            }
        }

        protected override void OnAfterCleanup()
        {
            _rxLabelAssembler.Reset();
        }

        private sealed class MultiLabelCommandAssembler
        {
            private readonly ushort[] _parts = new ushort[4];
            private int _mask;
            private DateTime _firstSeenUtc;
            private readonly Dictionary<byte, int> _labelToIndex;

            private static readonly TimeSpan AssemblyTimeout = TimeSpan.FromMilliseconds(200);

            public MultiLabelCommandAssembler(byte[] fragLabels)
            {
                _labelToIndex = new Dictionary<byte, int>();
                if (fragLabels != null)
                {
                    for (int i = 0; i < fragLabels.Length && i < 4; i++)
                    {
                        _labelToIndex[fragLabels[i]] = i;
                    }
                }
            }

            public bool TryAddFragment(byte label, ushort payload16, DateTime nowUtc, out byte[] cmd8)
            {
                cmd8 = null;

                if (!_labelToIndex.TryGetValue(label, out var index))
                    return false;

                if (_mask == 0 || (nowUtc - _firstSeenUtc) > AssemblyTimeout)
                {
                    _mask = 0;
                    _firstSeenUtc = nowUtc;
                }

                _parts[index] = payload16;
                _mask |= (1 << index);

                if (_mask != 0b1111)
                    return false;

                cmd8 = new byte[8];
                for (int j = 0; j < 4; j++)
                {
                    cmd8[j * 2] = (byte)((_parts[j] >> 8) & 0xFF);
                    cmd8[j * 2 + 1] = (byte)(_parts[j] & 0xFF);
                }

                _mask = 0;
                return true;
            }

            public void Reset()
            {
                _mask = 0;
                _firstSeenUtc = default;
            }
        }
    }
}
