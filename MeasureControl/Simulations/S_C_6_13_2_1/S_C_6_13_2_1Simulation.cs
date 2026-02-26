using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MeasureControl.Simulations.Common;

namespace MeasureControl.Simulations.S_C_6_13_2_1
{
    public sealed class S_C_6_13_2_1Simulation : ARINC429SimulationBase
    {
        private readonly MultiLabelCommandAssembler _rxLabelAssembler = new MultiLabelCommandAssembler(BenchTxFragmentLabels);
        private readonly Random _rand = new Random();

        private static readonly byte[] EnterAtpCommand8 = { 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] EnterAtpOk8 = { 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x02 };
        private static readonly byte[] ExitAtpCommand8 = { 0x00, 0x02, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01 };
        private static readonly byte[] ExitAtpOk8 = { 0x00, 0x02, 0x00, 0x01, 0x00, 0x00, 0x00, 0x03 };

        private static readonly byte[] ScpMeaCommand8 = { 0x16, 0x01, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] ScpMeaResponsePrefix4 = { 0x16, 0x01, 0x01, 0x01 };

        private static readonly byte[] BenchTxFragmentLabels = { 0x31, 0x32, 0x33, 0x34 };
        private static readonly byte[] ProductTxFragmentLabels = { 0x09, 0x0A, 0x0B, 0x0C };

        public Func<int> GetCurrentTestPointIndex { get; set; }

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

        public async Task<byte[]> WaitBenchResponse8Async(string benchRxChannel, Func<byte[], bool> isExpected, int timeoutMs, Action<string> log, CancellationToken token)
        {
            if (!_started || _arincDriver == null)
                throw new InvalidOperationException("Simulation not started");

            int rxIndex = ParseChannelIndex(benchRxChannel);
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
                            if (!TryParseWord(item.Data429, out byte label, out byte sdi, out ushort payload))
                                continue;

                            if (_rxLabelAssembler.TryAddFragment(label, payload, DateTime.UtcNow, out var cmd8) && cmd8 != null)
                            {
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
                                else if (cmd8.SequenceEqual(ScpMeaCommand8))
                                {
                                    log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] 产品侧收到S_CP_MEA -> 回复一次压力值");
                                    var resp8 = BuildScpMeaResponsePayload8();
                                    await SendMultiLabelFrameOnChannelAsync(SimProductTxChannelIndex, ProductTxFragmentLabels, resp8, log, token);
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

        private byte[] BuildScpMeaResponsePayload8()
        {
            int pointIndex = 1;
            try { pointIndex = GetCurrentTestPointIndex?.Invoke() ?? 1; } catch { pointIndex = 1; }

            var (min, max) = GetPressureQualifiedRange(pointIndex);
            double v;
            lock (_rand)
            {
                v = min + _rand.NextDouble() * (max - min);
            }

            var payload = new byte[8];
            payload[0] = ScpMeaResponsePrefix4[0];
            payload[1] = ScpMeaResponsePrefix4[1];
            payload[2] = ScpMeaResponsePrefix4[2];
            payload[3] = ScpMeaResponsePrefix4[3];

            var f = (float)v;
            var fbytes = BitConverter.GetBytes(f);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(fbytes);

            payload[4] = fbytes[0];
            payload[5] = fbytes[1];
            payload[6] = fbytes[2];
            payload[7] = fbytes[3];

            return payload;
        }

        private static (double Min, double Max) GetPressureQualifiedRange(int pointIndex)
        {
            return pointIndex switch
            {
                1 => (1082, 1118),
                2 => (1482, 1518),
                3 => (1982, 2018),
                _ => (1082, 1118)
            };
        }
    }
}
