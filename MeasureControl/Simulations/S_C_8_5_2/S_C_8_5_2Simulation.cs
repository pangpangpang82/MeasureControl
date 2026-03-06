using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MeasureControl.Simulations.Common;

namespace MeasureControl.Simulations.S_C_8_5_2
{
    public sealed class S_C_8_5_2Simulation : ARINC429SimulationBase
    {
        private static readonly byte[] BenchTxFragmentLabels = { 0x31, 0x32, 0x33, 0x34 };
        private static readonly byte[] ProductTxFragmentLabels = { 0x09, 0x0A, 0x0B, 0x0C };

        private static readonly byte[] EnterAtpOk8 = { 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x02 };
        private static readonly byte[] ExitAtpOk8 = { 0x00, 0x02, 0x00, 0x01, 0x00, 0x00, 0x00, 0x03 };

        private static readonly byte[] SArinc825InCommand8 = { 0x14, 0x02, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] SArinc825InOk8 = { 0x14, 0x02, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01 };

        private readonly MultiLabelCommandAssembler _cmdAssembler = new MultiLabelCommandAssembler(BenchTxFragmentLabels);

        public async Task SendBenchCommandOnlyAsync(string benchTxChannel, byte[] command8, Action<string> log, CancellationToken token)
        {
            if (!_started || _arincDriver == null)
                throw new InvalidOperationException("Simulation not started");
            if (command8 == null || command8.Length != 8)
                throw new ArgumentException("command8 must be 8 bytes", nameof(command8));

            int txIndex = ParseChannelIndex(benchTxChannel);

            await EnsureBenchTxChannelReadyAsync(txIndex);
            await SendMultiLabelFrameOnChannelAsync(txIndex, BenchTxFragmentLabels, command8, log, token);
        }

        public async Task<byte[]> WaitBenchResponse8Async(string benchRxChannel, Func<byte[], bool> isExpected, int timeoutMs, Action<string> log, CancellationToken token)
        {
            if (!_started || _arincDriver == null)
                throw new InvalidOperationException("Simulation not started");

            int rxIndex = ParseChannelIndex(benchRxChannel);
            try
            {
                await _arincDriver.StartReceiveAsync(rxIndex);
            }
            catch
            {
            }

            var labelAssembler = new MultiLabelCommandAssembler(ProductTxFragmentLabels);
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
                            if (isExpected == null || isExpected(resp8))
                            {
                                return resp8;
                            }
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
                            if (!TryParseWord(item.Data429, out byte label, out byte sdi, out ushort payload))
                                continue;

                            if (_cmdAssembler.TryAddFragment(label, payload, DateTime.UtcNow, out var cmd8) && cmd8 != null)
                            {
                                if (cmd8[0] == 0x00 && cmd8[1] == 0x01)
                                {
                                    await SendMultiLabelFrameOnChannelAsync(SimProductTxChannelIndex, ProductTxFragmentLabels, EnterAtpOk8, log, token);
                                }
                                else if (cmd8[0] == 0x00 && cmd8[1] == 0x02)
                                {
                                    await SendMultiLabelFrameOnChannelAsync(SimProductTxChannelIndex, ProductTxFragmentLabels, ExitAtpOk8, log, token);
                                }
                                else if (cmd8.SequenceEqual(SArinc825InCommand8))
                                {
                                    await SendMultiLabelFrameOnChannelAsync(SimProductTxChannelIndex, ProductTxFragmentLabels, SArinc825InOk8, log, token);
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
                    try { await Task.Delay(100, token); } catch { break; }
                }
            }
        }

        protected override void OnAfterCleanup()
        {
            _cmdAssembler.Reset();
        }

        private async Task EnsureBenchTxChannelReadyAsync(int txIndex)
        {
            if (_arincDriver == null)
                throw new InvalidOperationException("ARINC driver not initialized");

            bool openTxOk = await _arincDriver.OpenTxChannelAsync(txIndex);
            if (!openTxOk)
                throw new InvalidOperationException($"[SIM] TX通道打开失败: tx={txIndex}");

            await _arincDriver.ConfigureTxChannelAsync(txIndex, ArincRate, sendMode: 0, parity: 1, wordFormat: 0);
        }

        private sealed class MultiLabelCommandAssembler
        {
            private readonly ushort[] _parts = new ushort[4];
            private int _mask;
            private DateTime _firstSeenUtc;
            private readonly System.Collections.Generic.Dictionary<byte, int> _labelToIndex;

            private static readonly TimeSpan AssemblyTimeout = TimeSpan.FromMilliseconds(200);

            public MultiLabelCommandAssembler(byte[] fragLabels)
            {
                _labelToIndex = new System.Collections.Generic.Dictionary<byte, int>();
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

                if (!_labelToIndex.TryGetValue(label, out var idx))
                    return false;

                if (_mask == 0)
                {
                    _firstSeenUtc = nowUtc;
                }
                else
                {
                    if ((nowUtc - _firstSeenUtc) > AssemblyTimeout)
                    {
                        Reset();
                        _firstSeenUtc = nowUtc;
                    }
                }

                _parts[idx] = payload16;
                _mask |= (1 << idx);

                if (_mask == 0b1111)
                {
                    cmd8 = new byte[8];
                    for (int i = 0; i < 4; i++)
                    {
                        cmd8[i * 2] = (byte)((_parts[i] >> 8) & 0xFF);
                        cmd8[i * 2 + 1] = (byte)(_parts[i] & 0xFF);
                    }

                    Reset();
                    return true;
                }

                return false;
            }

            public void Reset()
            {
                _mask = 0;
                for (int i = 0; i < 4; i++)
                    _parts[i] = 0;
            }
        }
    }
}
