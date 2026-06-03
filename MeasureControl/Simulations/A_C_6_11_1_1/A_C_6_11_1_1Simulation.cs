using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MeasureControl.Simulations.Common;

namespace MeasureControl.Simulations.A_C_6_11_1_1
{
    public sealed class A_C_6_11_1_1Simulation : ARINC429SimulationBase
    {
        private readonly MultiLabelCommandAssembler _rxLabelAssembler = new MultiLabelCommandAssembler(BenchTxFragmentLabels);
        private readonly Random _rand = new Random();

        // ATP 进入/退出编码与 6.9.1 保持一致（仅记录，不回传 OK 帧）
        private static readonly byte[] EnterAtpCommand8 = { 0x30, 0x01, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] ExitAtpCommand8 = { 0x30, 0x02, 0x02, 0x01, 0x00, 0x00, 0x00, 0x00 };

        // 6.11.1 OFV/TRV 角度测试指令与遥测/原始编码模板
        private static readonly byte[] AbOfvtrvAngle8 = { 0x07, 0x04, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] OfvtrvAngleTelemetryTemplate8 = { 0x07, 0x04, 0x01, 0x02, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] OfvtrvAngleTelemetryRawTemplate8 = { 0x07, 0x04, 0x01, 0x03, 0x00, 0x00, 0x00, 0x00 };

        private static readonly byte[] OfvtrvAngleTelemetryPrefix4 = { 0x07, 0x04, 0x01, 0x02 };
        private static readonly byte[] OfvtrvAngleTelemetryRawPrefix4 = { 0x07, 0x04, 0x01, 0x03 };

        // 与 6.9.1 相同的多 label 编码
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
                        // 周期发送角度遥测与原始编码，两者均采用 pair-swap 发送
                        var telemetry = BuildAngleTelemetryPayload8();
                        await SendMultiLabelFrameOnChannelAsync(
                            SimProductTxChannelIndex,
                            ProductTxFragmentLabels,
                            SwapPairs8(telemetry),
                            null,
                            token);

                        var rawTelemetry = BuildAngleRawTelemetryPayload8();
                        await Task.Delay(50, token);
                        await SendMultiLabelFrameOnChannelAsync(
                            SimProductTxChannelIndex,
                            ProductTxFragmentLabels,
                            SwapPairs8(rawTelemetry),
                            null,
                            token);

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

            // bench 侧发送采用 pair-swap，产品侧使用 NormalizeFrame 容忍字节对调
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

        public async Task<byte[]> WaitAngleTelemetryAsync(string benchRxChannel, int timeoutMs, Action<string> log, CancellationToken token)
        {
            var (angle, _) = await WaitTelemetryAsync(benchRxChannel, timeoutMs, log, token);
            return angle;
        }

        public async Task<(byte[] Angle, byte[] Raw)> WaitTelemetryAsync(string benchRxChannel, int timeoutMs, Action<string> log, CancellationToken token)
        {
            if (!_started || _arincDriver == null)
                throw new InvalidOperationException("Simulation not started");

            int rxIndex = ParseChannelIndex(benchRxChannel);
            var labelAssembler = new MultiLabelCommandAssembler(ProductTxFragmentLabels);
            var deadline = DateTime.UtcNow.AddMilliseconds(Math.Max(100, timeoutMs));

            byte[] angle = null;
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
                            var normalizedAngle = NormalizeFrame(resp8, OfvtrvAngleTelemetryPrefix4);
                            var normalizedRaw = NormalizeFrame(resp8, OfvtrvAngleTelemetryRawPrefix4);

                            if (normalizedAngle != null && angle == null)
                            {
                                assembleSuccessCount++;
                                log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] OFVTRV角度遥测拼包完成 targetLabels={targetLabelCount}, success={assembleSuccessCount}, failure={assembleFailureCount}：{FormatBytes(normalizedAngle)}");
                                angle = normalizedAngle;
                            }
                            else if (normalizedRaw != null && raw == null)
                            {
                                assembleSuccessCount++;
                                log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] OFVTRV角度原始数据拼包完成 targetLabels={targetLabelCount}, success={assembleSuccessCount}, failure={assembleFailureCount}：{FormatBytes(normalizedRaw)}");
                                raw = normalizedRaw;
                            }
                            else
                            {
                                assembleFailureCount++;
                                log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] OFVTRV拼包完成但前缀不匹配 failure={assembleFailureCount}：{FormatBytes(resp8)}");
                            }
                        }
                    }
                }

                if (angle != null && raw != null)
                    return (angle, raw);

                await Task.Delay(10, token);
            }

            log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] OFVTRV角度遥测拼包等待结束 targetLabels={targetLabelCount}, success={assembleSuccessCount}, failure={assembleFailureCount}");
            return (angle, raw);
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
                                if (IsSameFrame(cmd8, EnterAtpCommand8))
                                {
                                    log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] 产品侧收到进入ATP");
                                }
                                else if (IsSameFrame(cmd8, ExitAtpCommand8))
                                {
                                    log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] 产品侧收到退出ATP -> 停止角度遥测");
                                    StopTelemetryOutput();
                                }
                                else if (IsSameFrame(cmd8, AbOfvtrvAngle8))
                                {
                                    log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] 产品侧收到OFV/TRV角度测试命令 -> 回传角度遥测并启动周期遥测发送");

                                    var telemetry = BuildAngleTelemetryPayload8();
                                    // 首帧也采用 pair-swap 发送，bench 侧 NormalizeFrame 负责还原
                                    await SendMultiLabelFrameOnChannelAsync(
                                        SimProductTxChannelIndex,
                                        ProductTxFragmentLabels,
                                        SwapPairs8(telemetry),
                                        log,
                                        token);

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

        private byte[] BuildAngleTelemetryPayload8()
        {
            int gearIndex = 1;
            try { gearIndex = GetCurrentGearIndex?.Invoke() ?? 1; } catch { gearIndex = 1; }

            var (min, max) = GetAngleQualifiedRange(gearIndex);
            double v;
            lock (_rand)
            {
                v = min + _rand.NextDouble() * (max - min);
            }

            short intPart = (short)Math.Floor(v);
            int frac = (int)Math.Round(Math.Abs(v - intPart) * 10000.0, MidpointRounding.AwayFromZero);
            frac = Math.Max(0, Math.Min(9999, frac));

            var payload = OfvtrvAngleTelemetryTemplate8.ToArray();

            payload[4] = (byte)((intPart >> 8) & 0xFF);
            payload[5] = (byte)(intPart & 0xFF);
            payload[6] = (byte)((frac >> 8) & 0xFF);
            payload[7] = (byte)(frac & 0xFF);

            return payload;
        }

        private byte[] BuildAngleRawTelemetryPayload8()
        {
            var payload = OfvtrvAngleTelemetryRawTemplate8.ToArray();

            int rawValue;
            lock (_rand)
            {
                rawValue = _rand.Next(0, 46656); // 6^6
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

        private static (double Min, double Max) GetAngleQualifiedRange(int gearIndex)
        {
            return gearIndex switch
            {
                1 => (-6.7500, -4.5000),
                2 => (43.8750, 46.1250),
                3 => (94.5000, 96.7500),
                _ => (-6.7500, -4.5000)
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

        private static bool IsSameFrame(byte[] actual, byte[] expected)
        {
            if (actual == null || expected == null || actual.Length != expected.Length)
                return false;
            if (actual.SequenceEqual(expected))
                return true;
            var swapped = SwapPairs8(actual);
            return swapped != null && swapped.SequenceEqual(expected);
        }

        private static byte[] NormalizeFrame(byte[] frame, byte[] prefix)
        {
            if (frame == null || frame.Length != 8)
                return null;
            if (IsPrefix(frame, prefix))
                return frame;
            var swapped = SwapPairs8(frame);
            if (swapped != null && IsPrefix(swapped, prefix))
                return swapped;
            return null;
        }
    }
}
