using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MeasureControl.Simulations.Common;

namespace MeasureControl.Simulations.S_C_8_7_1
{
    public sealed class S_C_8_7_1Simulation : ARINC429SimulationBase
    {
        private readonly MultiLabelCommandAssembler _rxLabelAssembler = new MultiLabelCommandAssembler(BenchTxFragmentLabels);
        private readonly Random _rand = new Random();

        private static readonly byte[] EnterAtpCommand8 = { 0x30, 0x01, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] EnterAtpOk8 = { 0x30, 0x01, 0x01, 0x02, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] ExitAtpCommand8 = { 0x30, 0x02, 0x02, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] ExitAtpOk8 = { 0x30, 0x02, 0x02, 0x02, 0x00, 0x00, 0x00, 0x00 };

        private static readonly byte[] SFwdAventsMea018 = { 0x15, 0x02, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] TelemetryPrefix4 = { 0x15, 0x02, 0x01, 0x02 };
        private static readonly byte[] TelemetryRawPrefix4 = { 0x15, 0x02, 0x01, 0x03 };

        private static readonly byte[] BenchTxFragmentLabels = { 0x8C, 0x4C, 0xCC, 0x2C };
        private static readonly byte[] ProductTxFragmentLabels = { 0x90, 0x50, 0xD0, 0x30 };

        public Func<int> GetCurrentGearIndex { get; set; }

        private CancellationTokenSource _telemetryCts;
        private Task _telemetryTask;

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
                        var telemetry = BuildTelemetryPayload8();
                        await SendMultiLabelFrameOnChannelAsync(SimProductTxChannelIndex, ProductTxFragmentLabels, SwapPairs8(telemetry), null, token);
                        var rawTelemetry = BuildRawTelemetryPayload8();
                        await Task.Delay(50, token);
                        await SendMultiLabelFrameOnChannelAsync(SimProductTxChannelIndex, ProductTxFragmentLabels, SwapPairs8(rawTelemetry), null, token);
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
            int targetLabelCount = 0;
            int assembleSuccessCount = 0;
            int assembleFailureCount = 0;

            while (!token.IsCancellationRequested && DateTime.UtcNow <= deadline)
            {
                var list = await _arincDriver.ReadReceiveDataAsync(rxIndex, maxCount: 1024, enableTimeTag: false, enableRateAdaption: false);
                if (list != null && list.Count > 0)
                {
                    foreach (var item in list)
                    {
                        if (!TryParseWord(item.Data429, out var rxLabel, out var sdi, out var payload))
                            continue;

                        if (ProductTxFragmentLabels.Contains(rxLabel))
                            targetLabelCount++;

                        if (labelAssembler.TryAddFragment(rxLabel, payload, DateTime.UtcNow, out var resp8) && resp8 != null)
                        {
                            if (isExpected == null || isExpected(resp8))
                            {
                                assembleSuccessCount++;
                                log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] benchRX={rxIndex} 拼包完成 labels={string.Join("/", ProductTxFragmentLabels.Select(b => $"0x{b:X2}"))} targetLabels={targetLabelCount}, success={assembleSuccessCount}, failure={assembleFailureCount}, resp8={FormatBytes(resp8)}");
                                return resp8;
                            }

                            assembleFailureCount++;
                            log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] benchRX={rxIndex} 拼包完成但内容不匹配 resp8={FormatBytes(resp8)}");
                        }
                    }
                }

                await Task.Delay(10, token);
            }

            log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] benchRX={rxIndex} 拼包等待结束 targetLabels={targetLabelCount}, success={assembleSuccessCount}, failure={assembleFailureCount}");
            return null;
        }

        public async Task<(byte[] Temperature, byte[] Raw)> WaitTelemetryAsync(string benchRxChannel, int timeoutMs, Action<string> log, CancellationToken token)
        {
            if (!_started || _arincDriver == null)
                throw new InvalidOperationException("Simulation not started");

            int rxIndex = ParseChannelIndex(benchRxChannel);
            var labelAssembler = new MultiLabelCommandAssembler(ProductTxFragmentLabels);
            var deadline = DateTime.UtcNow.AddMilliseconds(Math.Max(100, timeoutMs));
            byte[] temperature = null;
            byte[] raw = null;
            int targetLabelCount = 0;
            int assembleSuccessCount = 0;
            int assembleFailureCount = 0;

            while (!token.IsCancellationRequested && DateTime.UtcNow <= deadline)
            {
                var list = await _arincDriver.ReadReceiveDataAsync(rxIndex, maxCount: 1024, enableTimeTag: false, enableRateAdaption: false);
                if (list != null && list.Count > 0)
                {
                    foreach (var item in list)
                    {
                        if (!TryParseWord(item.Data429, out var rxLabel, out var sdi, out var payload))
                            continue;

                        if (ProductTxFragmentLabels.Contains(rxLabel))
                            targetLabelCount++;

                        if (labelAssembler.TryAddFragment(rxLabel, payload, DateTime.UtcNow, out var resp8) && resp8 != null)
                        {
                            if (resp8.Length == 8 && temperature == null && IsPrefix(resp8, TelemetryPrefix4))
                            {
                                assembleSuccessCount++;
                                log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] 温度遥测拼包完成 targetLabels={targetLabelCount}, success={assembleSuccessCount}, failure={assembleFailureCount}：{FormatBytes(resp8)}");
                                temperature = resp8;
                            }
                            else if (resp8.Length == 8 && raw == null && IsPrefix(resp8, TelemetryRawPrefix4))
                            {
                                assembleSuccessCount++;
                                log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] 原始数据拼包完成 targetLabels={targetLabelCount}, success={assembleSuccessCount}, failure={assembleFailureCount}：{FormatBytes(resp8)}");
                                raw = resp8;
                            }
                            else
                            {
                                assembleFailureCount++;
                            }
                        }
                    }
                }

                if (temperature != null && raw != null)
                    return (temperature, raw);

                await Task.Delay(10, token);
            }

            log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] 温度遥测拼包等待结束 targetLabels={targetLabelCount}, success={assembleSuccessCount}, failure={assembleFailureCount}");
            return (temperature, raw);
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
                                    await SendMultiLabelFrameOnChannelAsync(SimProductTxChannelIndex, ProductTxFragmentLabels, SwapPairs8(EnterAtpOk8), log, token);
                                }
                                else if (cmd8.SequenceEqual(ExitAtpCommand8))
                                {
                                    log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] 产品侧收到退出ATP -> 回复OK");
                                    await SendMultiLabelFrameOnChannelAsync(SimProductTxChannelIndex, ProductTxFragmentLabels, SwapPairs8(ExitAtpOk8), log, token);
                                }
                                else if (cmd8.SequenceEqual(SFwdAventsMea018))
                                {
                                    log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] 产品侧收到S_FWDAVENTS_MEA01 -> 启动周期遥测发送");
                                    StartTelemetryOutput();
                                    await SendMultiLabelFrameOnChannelAsync(SimProductTxChannelIndex, ProductTxFragmentLabels, SwapPairs8(SFwdAventsMea018), log, token);
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

        private byte[] BuildTelemetryPayload8()
        {
            int gearIndex = 1;
            try { gearIndex = GetCurrentGearIndex?.Invoke() ?? 1; } catch { gearIndex = 1; }

            var (min, max) = GetQualifiedRange(gearIndex);
            double v;
            lock (_rand)
            {
                v = min + _rand.NextDouble() * (max - min);
            }

            var raw = (short)Math.Round(v / 0.01, MidpointRounding.AwayFromZero);

            var payload = new byte[8];
            payload[0] = TelemetryPrefix4[0];
            payload[1] = TelemetryPrefix4[1];
            payload[2] = TelemetryPrefix4[2];
            payload[3] = TelemetryPrefix4[3];
            payload[4] = 0xFF;
            payload[5] = 0xFF;
            payload[6] = (byte)((raw >> 8) & 0xFF);
            payload[7] = (byte)(raw & 0xFF);

            return payload;
        }

        private byte[] BuildRawTelemetryPayload8()
        {
            var payload = new byte[8];
            payload[0] = TelemetryRawPrefix4[0];
            payload[1] = TelemetryRawPrefix4[1];
            payload[2] = TelemetryRawPrefix4[2];
            payload[3] = TelemetryRawPrefix4[3];

            int rawValue;
            lock (_rand)
            {
                rawValue = _rand.Next(0, 46656);
            }

            for (int i = 7; i >= 4; i--)
            {
                int lo = rawValue % 6;
                rawValue /= 6;
                int hi = rawValue % 6;
                rawValue /= 6;
                payload[i] = (byte)((hi << 4) | lo);
            }

            return payload;
        }

        private static (double Min, double Max) GetQualifiedRange(int gearIndex)
        {
            return gearIndex switch
            {
                1 => (-65.98, -64.02),
                2 => (25.12, 28.88),
                3 => (134.02, 137.98),
                _ => (-65.98, -64.02)
            };
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
                        _labelToIndex[fragLabels[i]] = i;
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
                    cmd8[j * 2] = (byte)(_parts[j] & 0xFF);
                    cmd8[j * 2 + 1] = (byte)((_parts[j] >> 8) & 0xFF);
                }

                _mask = 0;
                return true;
            }
        }

        private static string FormatBytes(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                return string.Empty;
            return string.Join(" ", bytes.Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));
        }

        private static bool IsPrefix(byte[] data, byte[] prefix)
        {
            if (data == null || prefix == null || data.Length < prefix.Length)
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
