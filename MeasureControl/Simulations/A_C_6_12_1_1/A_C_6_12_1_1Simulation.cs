using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MeasureControl.Simulations.Common;

namespace MeasureControl.Simulations.A_C_6_12_1_1
{
    public sealed class A_C_6_12_1_1Simulation : ARINC429SimulationBase
    {
        private readonly MultiLabelCommandAssembler _rxLabelAssembler = new MultiLabelCommandAssembler(BenchTxFragmentLabels);

        private static byte[] SwapPairs8(byte[] data8)
        {
            if (data8 == null || data8.Length != 8)
                return data8;

            return new byte[]
            {
                data8[1], data8[0],
                data8[3], data8[2],
                data8[5], data8[4],
                data8[7], data8[6]
            };
        }

        private static bool IsPrefixSafe(byte[] data, byte[] prefix, out byte[] matchedData)
        {
            matchedData = null;
            if (data == null || prefix == null)
                return false;
            if (IsPrefix(data, prefix))
            {
                matchedData = data;
                return true;
            }
            var swapped = SwapPairs8(data);
            if (swapped != null && IsPrefix(swapped, prefix))
            {
                matchedData = swapped;
                return true;
            }
            return false;
        }

        private static bool SequenceEqualSafe(byte[] actual, byte[] expected)
        {
            if (actual == null || expected == null)
                return false;
            if (actual.SequenceEqual(expected))
                return true;
            var swapped = SwapPairs8(actual);
            return swapped != null && swapped.SequenceEqual(expected);
        }

        private static readonly byte[] EnterAtpCommand8 = { 0x30, 0x01, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] ExitAtpCommand8 = { 0x30, 0x02, 0x02, 0x01, 0x00, 0x00, 0x00, 0x00 };

        private static readonly byte[] AbOfvtrvFinger8 = { 0x07, 0x05, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] OfvtrvFingerTelemetryPrefix4 = { 0x07, 0x05, 0x01, 0x03 };

        private static readonly byte[] BenchTxFragmentLabels = { 0x8C, 0x4C, 0xCC, 0x2C };
        private static readonly byte[] ProductTxFragmentLabels = { 0x90, 0x50, 0xD0, 0x30 };

        public Func<int> GetCurrentGearIndex { get; set; }

        private CancellationTokenSource _telemetryCts;
        private Task _telemetryTask;

        public void StartTelemetryOutput()
        {
            StopTelemetryOutput();
            _telemetryCts = new CancellationTokenSource();
            var token = _telemetryCts.Token;
            _telemetryTask = Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        var telemetry = BuildFingerTelemetryPayload8();
                        await SendMultiLabelFrameOnChannelAsync(SimProductTxChannelIndex, ProductTxFragmentLabels, SwapPairs8(telemetry), null, token);
                        await Task.Delay(100, token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch
                    {
                        await Task.Delay(100, token);
                    }
                }
            }, token);
        }

        public void StopTelemetryOutput()
        {
            try { _telemetryCts?.Cancel(); } catch { }
            try { _telemetryCts?.Dispose(); } catch { }
            _telemetryCts = null;
            _telemetryTask = null;
        }

        public new async Task StopAsync(Action<string> log)
        {
            StopTelemetryOutput();
            await base.StopAsync(log);
        }

        public async Task SendBenchCommandOnlyAsync(string benchTxChannel, byte[] command8, Action<string> log, CancellationToken token)
        {
            if (!_started || _arincDriver == null)
                throw new InvalidOperationException("Simulation not started");
            if (command8 == null || command8.Length != 8)
                throw new ArgumentException("command8 must be 8 bytes", nameof(command8));

            int txIndex = ParseChannelIndex(benchTxChannel);

            log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] bench发送: tx={txIndex}, labels={string.Join("/", BenchTxFragmentLabels.Select(b => $"0x{b:X2}"))}, payload8={FormatBytes(command8)}");

            await SendMultiLabelFrameOnChannelAsync(txIndex, BenchTxFragmentLabels, SwapPairs8(command8), log, token);
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
                            var swapped = SwapPairs8(resp8);
                            if (isExpected == null || isExpected(resp8) || (swapped != null && isExpected(swapped)))
                            {
                                var matched = (isExpected != null && swapped != null && isExpected(swapped)) ? swapped : resp8;
                                log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] benchRX={rxIndex} 拼包完成 labels={string.Join("/", ProductTxFragmentLabels.Select(b => $"0x{b:X2}"))} resp8={FormatBytes(matched)}");
                                return matched;
                            }
                        }
                    }
                }

                await Task.Delay(10, token);
            }

            return null;
        }

        public async Task<byte[]> WaitFingerTelemetryAsync(string benchRxChannel, int timeoutMs, Action<string> log, CancellationToken token)
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
                            if (resp8.Length == 8 && IsPrefixSafe(resp8, OfvtrvFingerTelemetryPrefix4, out var matchedSwapped))
                            {
                                log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] OFVTRV选气楔遥测拼包完成：{FormatBytes(matchedSwapped)}");
                                return matchedSwapped;
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
                                if (SequenceEqualSafe(cmd8, EnterAtpCommand8))
                                {
                                    log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] 产品侧收到进入ATP");
                                }
                                else if (SequenceEqualSafe(cmd8, ExitAtpCommand8))
                                {
                                    log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] 产品侧收到退出ATP，停止周期遥测");
                                    StopTelemetryOutput();
                                }
                                else if (SequenceEqualSafe(cmd8, AbOfvtrvFinger8))
                                {
                                    log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] 产品侧收到OFV/TRV选气楔测试命令 -> 回传遥测并启动周期遥测发送");

                                    var telemetry = BuildFingerTelemetryPayload8();
                                    await SendMultiLabelFrameOnChannelAsync(SimProductTxChannelIndex, ProductTxFragmentLabels, SwapPairs8(telemetry), log, token);
                                    StartTelemetryOutput();
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

        private byte[] BuildFingerTelemetryPayload8()
        {
            int gearIndex = 1;
            try { gearIndex = GetCurrentGearIndex?.Invoke() ?? 1; } catch { gearIndex = 1; }

            uint stateCode = GetQualifiedStateCode(gearIndex);

            var payload = new byte[8];
            payload[0] = OfvtrvFingerTelemetryPrefix4[0];
            payload[1] = OfvtrvFingerTelemetryPrefix4[1];
            payload[2] = OfvtrvFingerTelemetryPrefix4[2];
            payload[3] = OfvtrvFingerTelemetryPrefix4[3];

            payload[4] = (byte)((stateCode >> 24) & 0xFF);
            payload[5] = (byte)((stateCode >> 16) & 0xFF);
            payload[6] = (byte)((stateCode >> 8) & 0xFF);
            payload[7] = (byte)(stateCode & 0xFF);

            return payload;
        }

        private static uint GetQualifiedStateCode(int gearIndex)
        {
            return gearIndex switch
            {
                1 => 0x00005555,
                2 => 0x00000000,
                3 => 0x0000AAAA,
                _ => 0x0000AAAA
            };
        }

        private static bool IsPrefix(byte[] data, byte[] prefix)
        {
            if (data == null || prefix == null)
                return false;
            if (data.Length < prefix.Length)
                return false;
            for (int i = 0; i < prefix.Length; i++)
            {
                if (data[i] != prefix[i])
                    return false;
            }
            return true;
        }
    }
}
