using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MeasureControl.Drivers;
using MeasureControl.Drivers.ART4229;
using MeasureControl.Models.Devices;

namespace MeasureControl.Simulations.R_6_8_2
{
    public sealed class R_6_8_2Simulation : IDisposable
    {
        private ART4229Driver _arincDriver;
        private readonly SemaphoreSlim _arincIoLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _benchRxReadLock = new SemaphoreSlim(1, 1);

        private int _benchRxActiveReaders;

        private int _benchTxChannelIndex = -1;
        private int _benchRxChannelIndex = -1;
        private bool _benchRxStarted;

        private CancellationTokenSource _simCts;
        private Task _rxLoopTask;
        private Task _telemetryTask;
        private readonly MultiLabelCommandAssembler _rxLabelAssembler = new MultiLabelCommandAssembler(BenchTxFragmentLabels);

        private volatile bool _telemetryEnabled;
        private readonly Random _rand = new Random();

        private bool _started;

        // BTS 协议命令定义
        private static readonly byte[] EnterAtpCommand = { 0x30, 0x01, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] ExitAtpCommand = { 0x30, 0x02, 0x02, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] TemperatureTestCommand = { 0x07, 0x01, 0x02, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] TemperatureTelemetryCommand = { 0x07, 0x01, 0x02, 0x02, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] TelemetryTemperaturePrefix = { 0x07, 0x01, 0x02, 0x02 };
        private static readonly byte[] TelemetryRawPrefix = { 0x07, 0x01, 0x02, 0x03 };

        private static readonly byte[] BenchTxFragmentLabels = { 0x8C, 0x4C, 0xCC, 0x2C };
        private static readonly byte[] ProductTxFragmentLabels = { 0x90, 0x50, 0xD0, 0x30 };

        public bool EnableFrameLogging { get; set; } = true;

        public bool IsRealProduct { get; set; }

        public double ArincRate { get; set; } = 100000.0;

        public double SimProductArincRate { get; set; } = 100000.0;

        public int SimProductRxChannelIndex { get; set; } = 4;
        public int SimProductTxChannelIndex { get; set; } = 5;

        public int ArincDeviceIndex { get; set; } = 0;

        /// <summary>
        /// 由 ViewModel 设置，用于仿真遥测时获取当前电阻档位以生成对应温度
        /// </summary>
        public Func<string> GetCurrentResistorGear { get; set; }

        /// <summary>
        /// 由 ViewModel 设置，用于仿真遥测时获取当前环境温度选择(10~50℃/其他)
        /// </summary>
        public Func<string> GetCurrentAmbientTemperatureSelection { get; set; }

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

        public void StopTelemetryOutput()
        {
            _telemetryEnabled = false;
        }

        public async Task StartAsync(string benchTxChannel, string benchRxChannel, Action<string> log)
        {
            if (_started) return;
            if (log == null) log = _ => { };

            log($"[{DateTime.Now:HH:mm:ss}] [SIM] BTS 测试初始化开始");

            _simCts = new CancellationTokenSource();

            await OpenArincDeviceAsync(log);

            int benchTxIndex = ParseChannelIndex(benchTxChannel);
            int benchRxIndex = ParseChannelIndex(benchRxChannel);

            if (benchTxIndex == benchRxIndex)
                throw new InvalidOperationException($"[SIM] bench TX/RX 通道冲突：TX={benchTxIndex}, RX={benchRxIndex}");
            if (!IsRealProduct)
            {
                if (benchTxIndex == SimProductRxChannelIndex || benchTxIndex == SimProductTxChannelIndex)
                    throw new InvalidOperationException($"[SIM] benchTX 与产品侧通道冲突：benchTX={benchTxIndex}, simRX={SimProductRxChannelIndex}, simTX={SimProductTxChannelIndex}");
                if (benchRxIndex == SimProductRxChannelIndex || benchRxIndex == SimProductTxChannelIndex)
                    throw new InvalidOperationException($"[SIM] benchRX 与产品侧通道冲突：benchRX={benchRxIndex}, simRX={SimProductRxChannelIndex}, simTX={SimProductTxChannelIndex}");
            }

            _benchTxChannelIndex = benchTxIndex;
            _benchRxChannelIndex = benchRxIndex;

            await ConfigureArincChannelsAsync(benchTxIndex, benchRxIndex, log);

            await StartBenchRxAsync(log);

            if (!IsRealProduct)
            {
                await StartSimProductRxAsync(log, _simCts.Token);
            }

            _started = true;
            log($"[{DateTime.Now:HH:mm:ss}] [SIM] BTS 测试初始化完成");
        }

        private async Task StartBenchRxAsync(Action<string> log)
        {
            if (_arincDriver == null)
                throw new InvalidOperationException("ARINC driver not initialized");
            if (_benchRxChannelIndex < 0)
                return;
            if (_benchRxStarted)
                return;

            bool ok = await _arincDriver.StartReceiveAsync(_benchRxChannelIndex);
            if (!ok)
                throw new InvalidOperationException($"[SIM] 启动bench接收失败: RX={_benchRxChannelIndex}");

            _benchRxStarted = true;
            log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] bench侧接收已启动: benchRX={_benchRxChannelIndex}");
        }

        public async Task<bool> EnsureBenchChannelsAsync(string benchTxChannel, string benchRxChannel, Action<string> log)
        {
            if (_arincDriver == null)
                return false;

            int tx = ParseChannelIndex(benchTxChannel);
            int rx = ParseChannelIndex(benchRxChannel);

            const int wordFormat = 0;
            const int parity = 1;
            double txRate = ArincRate;
            double rxRate = ArincRate;

            try
            {
                if (tx == rx)
                {
                    log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] bench TX/RX 通道冲突：TX={tx}, RX={rx}");
                    return false;
                }
                if (!IsRealProduct)
                {
                    if (tx == SimProductRxChannelIndex || tx == SimProductTxChannelIndex)
                    {
                        log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] benchTX 与产品侧通道冲突：benchTX={tx}");
                        return false;
                    }
                    if (rx == SimProductRxChannelIndex || rx == SimProductTxChannelIndex)
                    {
                        log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] benchRX 与产品侧通道冲突：benchRX={rx}");
                        return false;
                    }
                }

                bool openTxOk = await _arincDriver.OpenTxChannelAsync(tx);
                bool openRxOk = await _arincDriver.OpenRxChannelAsync(rx);
                if (!openTxOk || !openRxOk)
                {
                    log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] bench通道打开失败: openTX={openTxOk} (tx={tx}), openRX={openRxOk} (rx={rx})");
                    return false;
                }

                await _arincDriver.ConfigureTxChannelAsync(tx, txRate, sendMode: 0, parity: parity, wordFormat: wordFormat);
                await _arincDriver.ConfigureRxChannelAsync(rx, rxRate, parity: parity, wordFormat: wordFormat,
                    enableInterrupt: false, interruptDepth: 0, enableTimeTag: false);

                bool ok = await _arincDriver.StartReceiveAsync(rx);
                if (ok)
                {
                    log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] bench通道已就绪: benchTX={tx}, benchRX={rx}");
                }
                else
                {
                    log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] bench通道启动接收失败: benchRX={rx}");
                }

                return ok;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> EnsureBenchRxChannelAsync(string benchRxChannel, Action<string> log)
        {
            if (_arincDriver == null)
                return false;

            int rx = ParseChannelIndex(benchRxChannel);

            const int wordFormat = 0;
            const int parity = 1;
            double rxRate = ArincRate;

            try
            {
                if (!IsRealProduct)
                {
                    if (rx == SimProductRxChannelIndex || rx == SimProductTxChannelIndex)
                    {
                        log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] benchRX 与产品侧通道冲突：benchRX={rx}");
                        return false;
                    }
                }

                bool openRxOk = await _arincDriver.OpenRxChannelAsync(rx);
                if (!openRxOk)
                {
                    log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] bench接收通道打开失败: benchRX={rx}");
                    return false;
                }

                await _arincDriver.ConfigureRxChannelAsync(rx, rxRate, parity: parity, wordFormat: wordFormat,
                    enableInterrupt: false, interruptDepth: 0, enableTimeTag: false);

                bool ok = await _arincDriver.StartReceiveAsync(rx);
                if (ok)
                {
                    log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] bench接收通道已就绪: benchRX={rx}");
                }
                else
                {
                    log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] bench接收通道启动失败: benchRX={rx}");
                }

                return ok;
            }
            catch
            {
                return false;
            }
        }

        public async Task<byte[]> SendBenchCommandAndWaitAsync(
            string benchTxChannel,
            string benchRxChannel,
            byte label,
            byte[] command8,
            Func<byte[], bool> isExpectedResponse,
            int timeoutMs,
            Action<string> log,
            CancellationToken token)
        {
            if (!_started || _arincDriver == null)
                throw new InvalidOperationException("Simulation not started");
            if (command8 == null || command8.Length != 8)
                throw new ArgumentException("command8 must be 8 bytes", nameof(command8));

            int txIndex = ParseChannelIndex(benchTxChannel);
            int rxIndex = ParseChannelIndex(benchRxChannel);

            if (txIndex != _benchTxChannelIndex || rxIndex != _benchRxChannelIndex)
                throw new InvalidOperationException($"[SIM] bench通道与StartAsync配置不一致：TX={txIndex}(expected={_benchTxChannelIndex}), RX={rxIndex}(expected={_benchRxChannelIndex})");

            await StartBenchRxAsync(log);

            log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] bench发送: tx={txIndex}, rx={rxIndex}, labels={string.Join("/", BenchTxFragmentLabels.Select(b => $"0x{b:X2}"))}, payload8={FormatBytes(command8)}");

            await SendMultiFrameOnChannelAsync(txIndex, label, command8, log, token);

            return await WaitBenchResponseAsync(
                benchRxChannel,
                label,
                isExpectedResponse,
                timeoutMs,
                log,
                token);
        }

        public async Task SendBenchCommandOnlyAsync(
            string benchTxChannel,
            byte label,
            byte[] command8,
            Action<string> log,
            CancellationToken token)
        {
            if (!_started || _arincDriver == null)
                throw new InvalidOperationException("Simulation not started");
            if (command8 == null || command8.Length != 8)
                throw new ArgumentException("command8 must be 8 bytes", nameof(command8));

            int txIndex = ParseChannelIndex(benchTxChannel);

            if (txIndex != _benchTxChannelIndex)
                throw new InvalidOperationException($"[SIM] benchTX通道与StartAsync配置不一致：TX={txIndex}(expected={_benchTxChannelIndex})");

            log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] bench发送(仅发送): tx={txIndex}, labels={string.Join("/", BenchTxFragmentLabels.Select(b => $"0x{b:X2}"))}, payload8={FormatBytes(command8)}");

            await SendMultiFrameOnChannelAsync(txIndex, label, command8, log, token);
        }

        public async Task ClearRxFifoAsync(string benchRxChannel)
        {
            if (_arincDriver == null)
                return;

            int rxIndex = ParseChannelIndex(benchRxChannel);
            try
            {
                int readers = Interlocked.Increment(ref _benchRxActiveReaders);
                try
                {
                    await _benchRxReadLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
                    try
                    {
                        await _arincDriver.ReadReceiveDataAsync(rxIndex, maxCount: 4096, enableTimeTag: false, enableRateAdaption: false);
                    }
                    finally
                    {
                        _benchRxReadLock.Release();
                    }
                }
                finally
                {
                    Interlocked.Decrement(ref _benchRxActiveReaders);
                }
            }
            catch
            {
            }
        }

        public async Task<byte[]> WaitBenchResponseAsync(
            string benchRxChannel,
            byte label,
            Func<byte[], bool> isExpectedResponse,
            int timeoutMs,
            Action<string> log,
            CancellationToken token)
        {
            if (!_started || _arincDriver == null)
                throw new InvalidOperationException("Simulation not started");

            int readers = Interlocked.Increment(ref _benchRxActiveReaders);
            if (readers > 1)
                log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] 警告：benchRX 读取存在并发(WaitBenchResponse)，activeReaders={readers}，可能发生抢帧");
            try
            {
            int rxIndex = ParseChannelIndex(benchRxChannel);
            MultiLabelCommandAssembler labelAssembler = new MultiLabelCommandAssembler(ProductTxFragmentLabels);
            MultiLabelCommandAssembler labelAssemblerStable = new MultiLabelCommandAssembler(new byte[] { 0x90, 0x50, 0xD0, 0x30 });
            MultiLabelCommandAssembler labelAssemblerBench = new MultiLabelCommandAssembler(BenchTxFragmentLabels);
            var deadline = DateTime.UtcNow.AddMilliseconds(Math.Max(100, timeoutMs));
            int rxLogCount = 0;
            const int maxRxLog = 2048;
            int rxBatchId = 0;
            var lastNoDataLogUtc = DateTime.MinValue;
            var labelHistogram = new Dictionary<byte, int>();

            while (!token.IsCancellationRequested && DateTime.UtcNow <= deadline)
            {
                await _benchRxReadLock.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    var list = await _arincDriver.ReadReceiveDataAsync(rxIndex, maxCount: 1024, enableTimeTag: false, enableRateAdaption: false);

                    if (list != null && list.Count > 0)
                    {
                        foreach (var item in list)
                        {
                            if (!TryParseWord(item.Data429, out var rxLabel, out var sdi, out var payload))
                                continue;

                            if (labelHistogram.TryGetValue(rxLabel, out var cnt))
                                labelHistogram[rxLabel] = cnt + 1;
                            else
                                labelHistogram[rxLabel] = 1;

                            if (EnableFrameLogging)
                            {
                                if (rxLogCount < maxRxLog)
                                {
                                    log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] benchRX={rxIndex} recv raw=0x{item.Data429:X8} label=0x{rxLabel:X2} sdi={sdi} payload=0x{payload:X4}");
                                    rxLogCount++;
                                    if (rxLogCount == maxRxLog)
                                    {
                                        log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] benchRX={rxIndex} recv日志已达上限({maxRxLog})，后续帧不再打印");
                                    }
                                }
                            }

                            if (labelAssembler.TryAddFragment(rxLabel, payload, DateTime.UtcNow, out var resp8) && resp8 != null)
                            {
                                if (isExpectedResponse == null || isExpectedResponse(resp8))
                                {
                                    log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] benchRX={rxIndex} 拼包完成 labels={string.Join("/", ProductTxFragmentLabels.Select(b => $"0x{b:X2}"))} resp8={FormatBytes(resp8)}");
                                    return resp8;
                                }
                            }

                            if (labelAssemblerStable.TryAddFragment(rxLabel, payload, DateTime.UtcNow, out var resp8Stable) && resp8Stable != null)
                            {
                                if (isExpectedResponse == null || isExpectedResponse(resp8Stable))
                                {
                                    log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] benchRX={rxIndex} 拼包完成 labels=0x90/0x50/0xD0/0x30 resp8={FormatBytes(resp8Stable)}");
                                    return resp8Stable;
                                }
                            }

                            if (labelAssemblerBench.TryAddFragment(rxLabel, payload, DateTime.UtcNow, out var resp8Bench) && resp8Bench != null)
                            {
                                if (isExpectedResponse == null || isExpectedResponse(resp8Bench))
                                {
                                    log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] benchRX={rxIndex} 拼包完成 labels={string.Join("/", BenchTxFragmentLabels.Select(b => $"0x{b:X2}"))} resp8={FormatBytes(resp8Bench)}");
                                    return resp8Bench;
                                }
                            }
                        }
                    }
                }
                finally
                {
                    _benchRxReadLock.Release();
                }

                await Task.Delay(5, token);
            }

            if (labelHistogram.Count > 0)
            {
                var summary = string.Join(", ", labelHistogram.OrderByDescending(kv => kv.Value).Take(12).Select(kv => $"0x{kv.Key:X2}:{kv.Value}"));
                log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] benchRX={rxIndex} 等待响应超时，label统计(top)={summary}");
            }

            return null;
            }
            finally
            {
                Interlocked.Decrement(ref _benchRxActiveReaders);
            }
        }

        /// <summary>
        /// 等待温度遥测帧和原始数据帧
        /// </summary>
        public async Task<(byte[] Temperature, byte[] Raw)> WaitTelemetryAsync(
            string benchRxChannel,
            int timeoutMs,
            Action<string> log,
            CancellationToken token)
        {
            if (!_started || _arincDriver == null)
                throw new InvalidOperationException("Simulation not started");

            int readers = Interlocked.Increment(ref _benchRxActiveReaders);
            if (readers > 1)
                log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] 警告：benchRX 读取存在并发(WaitTelemetry)，activeReaders={readers}，可能发生抢帧");
            try
            {

            int rxIndex = ParseChannelIndex(benchRxChannel);
            byte[] temperature = null;
            byte[] raw = null;
            var deadline = DateTime.UtcNow.AddMilliseconds(Math.Max(100, timeoutMs));
            int rxLogCount = 0;
            const int maxRxLog = 2048;
            int rxBatchId = 0;
            var lastNoDataLogUtc = DateTime.MinValue;
            var labelHistogram = new Dictionary<byte, int>();

            var stableLabels = new byte[] { 0x90, 0x50, 0xD0, 0x30 };
            var legacyLabels = new byte[] { 0x09, 0x0A, 0x0B, 0x0C };
            var windowFragments = new List<TelemetryFragment>(64);
            var windowKeep = TimeSpan.FromMilliseconds(350);

            while (!token.IsCancellationRequested && DateTime.UtcNow <= deadline)
            {
                await _benchRxReadLock.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    var list = await _arincDriver.ReadReceiveDataAsync(rxIndex, maxCount: 1024, enableTimeTag: false, enableRateAdaption: false);

                    var nowUtc = DateTime.UtcNow;
                    if (EnableFrameLogging)
                    {
                        rxBatchId++;
                        int cnt = list?.Count ?? 0;
                        if (cnt > 0)
                        {
                            log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] benchRX={rxIndex} recvBatch#{rxBatchId} count={cnt}");
                        }
                        else if ((nowUtc - lastNoDataLogUtc) > TimeSpan.FromMilliseconds(200))
                        {
                            lastNoDataLogUtc = nowUtc;
                            log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] benchRX={rxIndex} recvBatch#{rxBatchId} count=0");
                        }
                    }

                    if (windowFragments.Count > 0)
                    {
                        windowFragments.RemoveAll(f => (nowUtc - f.SeenUtc) > windowKeep);
                        if (windowFragments.Count > 64)
                            windowFragments.RemoveRange(0, windowFragments.Count - 64);
                    }

                    var burstFragments = new List<TelemetryFragment>(list?.Count ?? 0);

                    if (list != null && list.Count > 0)
                    {
                        foreach (var item in list)
                        {
                            if (!TryParseWord(item.Data429, out var rxLabel, out var sdi, out var payload))
                                continue;

                            var frag = new TelemetryFragment(rxLabel, payload, item.Data429, nowUtc);
                            burstFragments.Add(frag);
                            windowFragments.Add(frag);

                            if (labelHistogram.TryGetValue(rxLabel, out var cnt))
                                labelHistogram[rxLabel] = cnt + 1;
                            else
                                labelHistogram[rxLabel] = 1;

                            if (EnableFrameLogging)
                            {
                                if (rxLogCount < maxRxLog)
                                {
                                    log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] benchRX={rxIndex} recvBatch#{rxBatchId} raw=0x{item.Data429:X8} label=0x{rxLabel:X2} sdi={sdi} payload=0x{payload:X4}");
                                    rxLogCount++;
                                    if (rxLogCount == maxRxLog)
                                        log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] benchRX={rxIndex} recv日志已达上限({maxRxLog})，后续帧不再打印");
                                }
                            }

                        }
                    }

                    if (burstFragments.Count > 0)
                    {
                        TryExtractTelemetryFromFragments(burstFragments, ProductTxFragmentLabels, BenchTxFragmentLabels, stableLabels, legacyLabels, ref temperature, ref raw, log);
                    }

                    if (temperature == null || raw == null)
                    {
                        TryExtractTelemetryFromFragments(windowFragments, ProductTxFragmentLabels, BenchTxFragmentLabels, stableLabels, legacyLabels, ref temperature, ref raw, log);
                    }
                }
                finally
                {
                    _benchRxReadLock.Release();
                }

                if (temperature != null && raw != null)
                    return (temperature, raw);

                await Task.Delay(5, token);
            }

            if ((temperature == null || raw == null) && labelHistogram.Count > 0)
            {
                var summary = string.Join(", ", labelHistogram.OrderByDescending(kv => kv.Value).Take(12).Select(kv => $"0x{kv.Key:X2}:{kv.Value}"));
                log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] benchRX={rxIndex} 遥测等待超时，label统计(top)={summary}");
            }
            return (temperature, raw);
            }
            finally
            {
                Interlocked.Decrement(ref _benchRxActiveReaders);
            }
        }

        private readonly struct TelemetryFragment
        {
            public TelemetryFragment(byte label, ushort payload16, uint rawWord, DateTime seenUtc)
            {
                Label = label;
                Payload16 = payload16;
                RawWord = rawWord;
                SeenUtc = seenUtc;
            }

            public byte Label { get; }
            public ushort Payload16 { get; }
            public uint RawWord { get; }
            public DateTime SeenUtc { get; }
        }

        private static byte[] BuildCmd8FromParts(ushort[] parts)
        {
            if (parts == null || parts.Length < 4)
                return null;
            var cmd8 = new byte[8];
            for (int j = 0; j < 4; j++)
            {
                cmd8[j * 2] = (byte)(parts[j] & 0xFF);
                cmd8[j * 2 + 1] = (byte)((parts[j] >> 8) & 0xFF);
            }
            return cmd8;
        }

        private void TryExtractTelemetryFromFragments(
            List<TelemetryFragment> fragments,
            byte[] productLabels,
            byte[] benchLabels,
            byte[] stableLabels,
            byte[] legacyLabels,
            ref byte[] temperature,
            ref byte[] raw,
            Action<string> log)
        {
            if (fragments == null || fragments.Count == 0)
                return;

            if (temperature == null && TryFindTemperatureFrame(fragments, productLabels, out var tFrame, out var tParts, out var tC))
            {
                temperature = tFrame;
                log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] 温度采集值拼包完成(labels={string.Join("/", productLabels.Select(b => $"0x{b:X2}"))}) frags={FormatFragments(productLabels, tParts)} Temp={tC:0.####}℃：{FormatBytes(tFrame)}");
            }
            if (raw == null && TryFindRawFrame(fragments, productLabels, out var rFrame, out var rParts))
            {
                raw = rFrame;
                log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] 原始数据拼包完成(labels={string.Join("/", productLabels.Select(b => $"0x{b:X2}"))}) frags={FormatFragments(productLabels, rParts)}：{FormatBytes(rFrame)}");
            }

            if (temperature == null && TryFindTemperatureFrame(fragments, stableLabels, out tFrame, out tParts, out tC))
            {
                temperature = tFrame;
                log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] 温度采集值拼包完成(labels=0x90/0x50/0xD0/0x30) frags={FormatFragments(stableLabels, tParts)} Temp={tC:0.####}℃：{FormatBytes(tFrame)}");
            }
            if (raw == null && TryFindRawFrame(fragments, stableLabels, out rFrame, out rParts))
            {
                raw = rFrame;
                log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] 原始数据拼包完成(labels=0x90/0x50/0xD0/0x30) frags={FormatFragments(stableLabels, rParts)}：{FormatBytes(rFrame)}");
            }

            if (temperature == null && TryFindTemperatureFrame(fragments, benchLabels, out tFrame, out tParts, out tC))
            {
                temperature = tFrame;
                log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] 温度采集值拼包完成(labels={string.Join("/", benchLabels.Select(b => $"0x{b:X2}"))}) frags={FormatFragments(benchLabels, tParts)} Temp={tC:0.####}℃：{FormatBytes(tFrame)}");
            }
            if (raw == null && TryFindRawFrame(fragments, benchLabels, out rFrame, out rParts))
            {
                raw = rFrame;
                log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] 原始数据拼包完成(labels={string.Join("/", benchLabels.Select(b => $"0x{b:X2}"))}) frags={FormatFragments(benchLabels, rParts)}：{FormatBytes(rFrame)}");
            }

            if (temperature == null && TryFindTemperatureFrame(fragments, legacyLabels, out tFrame, out tParts, out tC))
            {
                temperature = tFrame;
                log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] 温度采集值拼包完成(labels=0x09/0x0A/0x0B/0x0C) frags={FormatFragments(legacyLabels, tParts)} Temp={tC:0.####}℃：{FormatBytes(tFrame)}");
            }
            if (raw == null && TryFindRawFrame(fragments, legacyLabels, out rFrame, out rParts))
            {
                raw = rFrame;
                log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] 原始数据拼包完成(labels=0x09/0x0A/0x0B/0x0C) frags={FormatFragments(legacyLabels, rParts)}：{FormatBytes(rFrame)}");
            }
        }

        private bool TryFindTemperatureFrame(List<TelemetryFragment> fragments, byte[] labels, out byte[] frame8, out ushort[] partsSnapshot, out double tC)
        {
            frame8 = null;
            partsSnapshot = null;
            tC = 0;

            if (!TryGetCandidates(fragments, labels, out var candidates))
                return false;

            for (int a = 0; a < candidates[0].Length; a++)
            for (int b = 0; b < candidates[1].Length; b++)
            for (int c = 0; c < candidates[2].Length; c++)
            for (int d = 0; d < candidates[3].Length; d++)
            {
                var parts = new ushort[4];
                parts[0] = candidates[0][a];
                parts[1] = candidates[1][b];
                parts[2] = candidates[2][c];
                parts[3] = candidates[3][d];

                var cmd8 = BuildCmd8FromParts(parts);
                if (cmd8 == null)
                    continue;

                if (IsValidTemperatureTelemetryFrame(cmd8, out var tmp))
                {
                    frame8 = cmd8;
                    partsSnapshot = parts;
                    tC = tmp;
                    return true;
                }
            }

            return false;
        }

        private bool TryFindRawFrame(List<TelemetryFragment> fragments, byte[] labels, out byte[] frame8, out ushort[] partsSnapshot)
        {
            frame8 = null;
            partsSnapshot = null;

            if (!TryGetCandidates(fragments, labels, out var candidates))
                return false;

            for (int a = 0; a < candidates[0].Length; a++)
            for (int b = 0; b < candidates[1].Length; b++)
            for (int c = 0; c < candidates[2].Length; c++)
            for (int d = 0; d < candidates[3].Length; d++)
            {
                var parts = new ushort[4];
                parts[0] = candidates[0][a];
                parts[1] = candidates[1][b];
                parts[2] = candidates[2][c];
                parts[3] = candidates[3][d];

                var cmd8 = BuildCmd8FromParts(parts);
                if (cmd8 == null)
                    continue;

                if (IsValidRawTelemetryFrame(cmd8))
                {
                    frame8 = cmd8;
                    partsSnapshot = parts;
                    return true;
                }
            }

            return false;
        }

        private static bool TryGetCandidates(List<TelemetryFragment> fragments, byte[] labels, out ushort[][] candidates)
        {
            candidates = null;
            if (labels == null || labels.Length < 4)
                return false;
            if (fragments == null || fragments.Count == 0)
                return false;

            candidates = new ushort[4][];
            for (int i = 0; i < 4; i++)
            {
                var label = labels[i];
                var payloads = fragments
                    .Where(f => f.Label == label)
                    .Select(f => f.Payload16)
                    .ToList();

                if (payloads.Count == 0)
                    return false;

                int start = Math.Max(0, payloads.Count - 4);
                var recent = payloads.GetRange(start, payloads.Count - start);
                var uniq = new List<ushort>(recent.Count);
                var seen = new HashSet<ushort>();
                foreach (var p in recent)
                {
                    if (seen.Add(p))
                        uniq.Add(p);
                }

                candidates[i] = uniq.ToArray();
            }

            return true;
        }

        public async Task StopAsync(Action<string> log)
        {
            if (log == null) log = _ => { };

            if (!_started)
            {
                await CleanupAsync();
                return;
            }

            log($"[{DateTime.Now:HH:mm:ss}] [SIM] BTS 测试停止：释放设备资源");

            _started = false;
            _telemetryEnabled = false;

            try
            {
                _simCts?.Cancel();
            }
            catch
            {
            }

            try
            {
                if (_rxLoopTask != null)
                    await _rxLoopTask.ConfigureAwait(false);
            }
            catch
            {
            }

            try
            {
                if (_telemetryTask != null)
                    await _telemetryTask.ConfigureAwait(false);
            }
            catch
            {
            }

            await CleanupAsync();
        }

        // ========== 产品侧仿真 ==========

        private async Task StartSimProductRxAsync(Action<string> log, CancellationToken token)
        {
            if (_arincDriver == null)
                throw new InvalidOperationException("ARINC driver not initialized");

            bool ok = await _arincDriver.StartReceiveAsync(SimProductRxChannelIndex);
            if (!ok)
                throw new InvalidOperationException($"[SIM] 启动接收失败: RX={SimProductRxChannelIndex}");

            log($"[{DateTime.Now:HH:mm:ss}] [SIM] 产品侧接收已启动: simRX={SimProductRxChannelIndex} (将监听进入/退出ATP并回OK到simTX={SimProductTxChannelIndex})");

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

                                    if (cmd8.SequenceEqual(EnterAtpCommand))
                                    {
                                        log($"[{DateTime.Now:HH:mm:ss}] [SIM] 产品侧收到进入ATP指令");
                                    }
                                    else if (cmd8.SequenceEqual(ExitAtpCommand))
                                    {
                                        log($"[{DateTime.Now:HH:mm:ss}] [SIM] 产品侧收到退出ATP指令");
                                        _telemetryEnabled = false;
                                    }
                                    else if (cmd8.SequenceEqual(TemperatureTestCommand))
                                    {
                                        log($"[{DateTime.Now:HH:mm:ss}] [SIM] 产品侧收到温度测试指令 -> 回复确认并开启遥测");
                                        await SendMultiFrameResponseAsync(label, TemperatureTestCommand, log, token);
                                        _telemetryEnabled = true;
                                        StartTelemetryLoopIfNeeded(label, log);
                                    }
                                    else if (cmd8.SequenceEqual(TemperatureTelemetryCommand))
                                    {
                                        log($"[{DateTime.Now:HH:mm:ss}] [SIM] 产品侧收到温度回采指令 -> 开启遥测");
                                        _telemetryEnabled = true;
                                        StartTelemetryLoopIfNeeded(label, log);
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

        private void StartTelemetryLoopIfNeeded(byte label, Action<string> log)
        {
            if (_telemetryTask != null && !_telemetryTask.IsCompleted)
                return;

            if (_simCts == null)
                return;

            _telemetryTask = Task.Run(() => TelemetryLoopAsync(label, log, _simCts.Token), _simCts.Token);
        }

        private async Task TelemetryLoopAsync(byte label, Action<string> log, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (!_telemetryEnabled)
                    {
                        await Task.Delay(100, token);
                        continue;
                    }

                    // 基于当前电阻档位生成模拟温度
                    string gear = GetCurrentResistorGear?.Invoke() ?? "1挡";
                    double temperature = GenerateSimulatedTemperature(gear);

                    // 构造温度遥测帧: 07 01 07 02 + 温度数据
                    var tempPayload = new byte[8];
                    tempPayload[0] = TelemetryTemperaturePrefix[0];
                    tempPayload[1] = TelemetryTemperaturePrefix[1];
                    tempPayload[2] = TelemetryTemperaturePrefix[2];
                    tempPayload[3] = TelemetryTemperaturePrefix[3];

                    short tRaw = (short)Math.Round(temperature / 0.01, MidpointRounding.AwayFromZero);
                    tempPayload[6] = (byte)((tRaw >> 8) & 0xFF);
                    tempPayload[7] = (byte)(tRaw & 0xFF);

                    await SendMultiFrameResponseAsync(label, tempPayload, log, token);

                    // 构造原始数据遥测帧: 07 01 07 03 + 6进制编码
                    var rawPayload = new byte[8];
                    rawPayload[0] = TelemetryRawPrefix[0];
                    rawPayload[1] = TelemetryRawPrefix[1];
                    rawPayload[2] = TelemetryRawPrefix[2];
                    rawPayload[3] = TelemetryRawPrefix[3];

                    int rawValue;
                    lock (_rand)
                    {
                        rawValue = _rand.Next(0, 46656); // 6^6
                    }

                    // 编码为 4 字节 6 进制 nibbles (每字节高4位+低4位各一个6进制位)
                    for (int i = 7; i >= 4; i--)
                    {
                        int lo = rawValue % 6;
                        rawValue /= 6;
                        int hi = rawValue % 6;
                        rawValue /= 6;
                        rawPayload[i] = (byte)((hi << 4) | lo);
                    }

                    await Task.Delay(50, token);
                    await SendMultiFrameResponseAsync(label, rawPayload, log, token);

                    await Task.Delay(200, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    try
                    {
                        await Task.Delay(200, token);
                    }
                    catch
                    {
                        break;
                    }
                }
            }
        }

        private double GenerateSimulatedTemperature(string gear)
        {
            string ambient = GetCurrentAmbientTemperatureSelection?.Invoke() ?? "10~50℃";
            var (min, max) = GetQualifiedTemperatureRange(gear, ambient);
            lock (_rand)
            {
                return min + _rand.NextDouble() * (max - min);
            }
        }

        private static (double Min, double Max) GetQualifiedTemperatureRange(string gear, string ambientSelection)
        {
            bool isNormalAmbient = string.Equals(ambientSelection, "10~50℃", StringComparison.OrdinalIgnoreCase);
            return gear switch
            {
                "1挡" => isNormalAmbient ? (-65.93, -64.07) : (-69.05, -60.95),
                "2挡" => isNormalAmbient ? (24.75, 26.61) : (21.63, 29.73),
                "3挡" => isNormalAmbient ? (134.06, 135.94) : (130.94, 139.06),
                _ => (-65.93, -64.07)
            };
        }

        // ========== ARINC429 底层方法 ==========

        private async Task SendMultiFrameResponseAsync(byte label, byte[] payload8, Action<string> log, CancellationToken token)
        {
            if (payload8 == null || payload8.Length != 8)
                return;

            var swapped = SwapPairs8(payload8);

            uint[] data429 = new uint[4];
            uint[] parity = new uint[4];

            for (byte frag = 0; frag < 4; frag++)
            {
                ushort part = (ushort)((swapped[frag * 2] << 8) | swapped[frag * 2 + 1]);
                byte fragLabel = ProductTxFragmentLabels[frag];
                uint word = BuildWord(fragLabel, 0, part);
                data429[frag] = ApplyParity(word);
                parity[frag] = 1;
            }

            await _arincIoLock.WaitAsync(token).ConfigureAwait(false);
            bool ok;
            try
            {
                ok = await _arincDriver.SendDataSingleAsync(SimProductTxChannelIndex, data429, parity);
            }
            finally
            {
                _arincIoLock.Release();
            }

            if (!ok)
            {
                log($"[{DateTime.Now:HH:mm:ss}] [SIM] 产品侧回包发送失败: simTX={SimProductTxChannelIndex}");
            }
            else if (EnableFrameLogging)
            {
                log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] simTX={SimProductTxChannelIndex} send labels={string.Join("/", ProductTxFragmentLabels.Select(b => $"0x{b:X2}"))} payload8={FormatBytes(payload8)}");
            }
        }

        private async Task SendMultiFrameOnChannelAsync(int txChannelIndex, byte label, byte[] payload8, Action<string> log, CancellationToken token)
        {
            if (_arincDriver == null)
                return;
            if (payload8 == null || payload8.Length != 8)
                return;

            var swapped = SwapPairs8(payload8);

            uint[] data429 = new uint[4];
            uint[] parity = new uint[4];

            for (byte frag = 0; frag < 4; frag++)
            {
                ushort part = (ushort)((swapped[frag * 2] << 8) | swapped[frag * 2 + 1]);
                byte fragLabel = BenchTxFragmentLabels[frag];
                uint word = BuildWord(fragLabel, 0, part);
                data429[frag] = ApplyParity(word);
                parity[frag] = 1;
            }

            await _arincIoLock.WaitAsync(token).ConfigureAwait(false);
            bool ok;
            try
            {
                ok = await _arincDriver.SendDataSingleAsync(txChannelIndex, data429, parity);
            }
            finally
            {
                _arincIoLock.Release();
            }

            if (!ok)
            {
                log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] bench侧发送失败: tx={txChannelIndex}");
            }
            else if (EnableFrameLogging)
            {
                log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] benchTX={txChannelIndex} send labels={string.Join("/", BenchTxFragmentLabels.Select(b => $"0x{b:X2}"))} payload8={FormatBytes(payload8)}");
            }
        }

        // ========== 板卡管理 ==========

        private async Task OpenArincDeviceAsync(Action<string> log)
        {
            try
            {
                var device = new Arinc429Device("PXIe-4227", "Slot0")
                {
                    Model = "PXIe-4227",
                    Name = "PXIe-4227",
                    SlotIndex = ArincDeviceIndex
                };

                _arincDriver = DriverFactory.CreateDriver(device) as ART4229Driver;
                if (_arincDriver == null)
                    throw new InvalidOperationException("无法创建 ART4229Driver");

                var ok = await _arincDriver.ConnectAsync();
                if (!ok)
                    throw new InvalidOperationException("ARINC429 板卡连接失败");

                log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] ARINC429 板卡已连接 (deviceIndex={ArincDeviceIndex})");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"[SIM] 打开 ARINC429 板卡失败: {ex.Message}", ex);
            }
        }

        private async Task ConfigureArincChannelsAsync(int benchTxIndex, int benchRxIndex, Action<string> log)
        {
            const int wordFormat = 0;
            const int parity = 1;
            double benchTxRate = ArincRate;
            double benchRxRate = ArincRate;
            double simTxRate = SimProductArincRate;
            double simRxRate = SimProductArincRate;

            bool openBenchTx = await _arincDriver.OpenTxChannelAsync(benchTxIndex);
            bool openBenchRx = await _arincDriver.OpenRxChannelAsync(benchRxIndex);
            bool openSimRx = true;
            bool openSimTx = true;
            if (!IsRealProduct)
            {
                openSimRx = await _arincDriver.OpenRxChannelAsync(SimProductRxChannelIndex);
                openSimTx = await _arincDriver.OpenTxChannelAsync(SimProductTxChannelIndex);
            }

            if (!openBenchTx || !openBenchRx || !openSimRx || !openSimTx)
                throw new InvalidOperationException($"[SIM] Open通道失败: benchTX={benchTxIndex}({openBenchTx}), benchRX={benchRxIndex}({openBenchRx}), simRX={SimProductRxChannelIndex}({openSimRx}), simTX={SimProductTxChannelIndex}({openSimTx})");

            await _arincDriver.ConfigureTxChannelAsync(benchTxIndex, benchTxRate, sendMode: 0, parity: parity, wordFormat: wordFormat);
            await _arincDriver.ConfigureRxChannelAsync(benchRxIndex, benchRxRate, parity: parity, wordFormat: wordFormat,
                enableInterrupt: false, interruptDepth: 0, enableTimeTag: false);

            if (!IsRealProduct)
            {
                await _arincDriver.ConfigureRxChannelAsync(SimProductRxChannelIndex, simRxRate, parity: parity, wordFormat: wordFormat,
                    enableInterrupt: false, interruptDepth: 0, enableTimeTag: false);
                await _arincDriver.ConfigureTxChannelAsync(SimProductTxChannelIndex, simTxRate, sendMode: 0, parity: parity, wordFormat: wordFormat);
            }

            log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] ARINC429 通道已配置: benchTX={benchTxIndex}, benchRX={benchRxIndex}, simRX={SimProductRxChannelIndex}, simTX={SimProductTxChannelIndex}");
        }

        private async Task CleanupAsync()
        {
            try
            {
                if (_arincDriver != null)
                {
                    _telemetryEnabled = false;
                    try
                    {
                        if (!IsRealProduct)
                        {
                            await _arincDriver.StopReceiveAsync(SimProductRxChannelIndex);
                        }
                    }
                    catch
                    {
                    }

                    try
                    {
                        if (_benchRxChannelIndex >= 0)
                            await _arincDriver.StopReceiveAsync(_benchRxChannelIndex);
                    }
                    catch
                    {
                    }

                    await _arincDriver.DisconnectAsync();
                    _arincDriver = null;
                }
            }
            catch
            {
            }

            try
            {
                _simCts?.Dispose();
            }
            catch
            {
            }
            finally
            {
                _simCts = null;
                _rxLoopTask = null;
                _telemetryTask = null;
                _rxLabelAssembler.Reset();
                _benchRxStarted = false;
                _benchTxChannelIndex = -1;
                _benchRxChannelIndex = -1;
            }
        }

        public void Dispose()
        {
            try
            {
                CleanupAsync().GetAwaiter().GetResult();
            }
            catch
            {
            }
        }

        // ========== 工具方法 ==========

        private static bool TryParseWord(uint raw, out byte label, out byte sdi, out ushort payload16)
        {
            label = (byte)(raw & 0xFF);
            sdi = (byte)((raw >> 8) & 0x3);
            uint data19 = (raw >> 10) & 0x1FFFF;
            payload16 = (ushort)(data19 & 0xFFFF);
            return true;
        }

        private static uint BuildWord(byte label, byte sdi, ushort payload16)
        {
            uint word = 0;
            word |= label;
            word |= (uint)(sdi & 0x3) << 8;
            word |= (uint)payload16 << 10;
            return word;
        }

        private static uint ApplyParity(uint word)
        {
            uint data = word & 0x7FFFFFFF;
            int ones = 0;
            uint tmp = data;
            while (tmp != 0)
            {
                tmp &= (tmp - 1);
                ones++;
            }

            bool needParityBit = (ones % 2 == 0);
            return needParityBit ? (data | 0x80000000) : data;
        }

        private static bool IsPrefix(byte[] data, byte[] prefix)
        {
            if (data == null || prefix == null) return false;
            if (data.Length < prefix.Length) return false;
            for (int i = 0; i < prefix.Length; i++)
            {
                if (data[i] != prefix[i]) return false;
            }
            return true;
        }

        private static bool TryParseTemperatureC(byte[] frameData, out double temperatureC)
        {
            temperatureC = 0;
            if (frameData == null || frameData.Length < 8)
                return false;
            if (!IsPrefix(frameData, TelemetryTemperaturePrefix))
                return false;

            var raw32 = (frameData[4] << 24) | (frameData[5] << 16) | (frameData[6] << 8) | frameData[7];
            temperatureC = raw32 * 0.01;
            return true;
        }

        private bool IsValidTemperatureTelemetryFrame(byte[] frameData, out double temperatureC)
        {
            temperatureC = 0;
            if (!TryParseTemperatureC(frameData, out temperatureC))
                return false;

            // 只验证帧格式，不验证温度值范围，避免实际产品返回的温度超出预期范围时被误判为无效帧
            return true;
        }

        private static bool IsValidRawTelemetryFrame(byte[] frameData)
        {
            if (frameData == null || frameData.Length < 8)
                return false;
            if (!IsPrefix(frameData, TelemetryRawPrefix))
                return false;

            for (int i = 4; i <= 7; i++)
            {
                var b = frameData[i];
                var hi = (b >> 4) & 0xF;
                var lo = b & 0xF;
                if (hi > 5 || lo > 5)
                    return false;
            }

            return true;
        }

        public static int ParseChannelIndex(string channel)
        {
            if (string.IsNullOrWhiteSpace(channel)) return -1;

            var trimmed = channel.Trim();

            const string prefix1 = "ARINC429_";
            if (trimmed.StartsWith(prefix1, StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed.Substring(prefix1.Length);
                if (int.TryParse(trimmed, out var idx1))
                    return idx1;
                return -1;
            }

            const string prefix2 = "429_CH";
            if (trimmed.StartsWith(prefix2, StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed.Substring(prefix2.Length);
                if (int.TryParse(trimmed, out var idx2))
                    return idx2;
                return -1;
            }

            const string prefix3 = "CH";
            var i = trimmed.LastIndexOf(prefix3, StringComparison.OrdinalIgnoreCase);
            if (i >= 0)
            {
                var numberPart = trimmed.Substring(i + prefix3.Length).Trim();
                return int.TryParse(numberPart, out var idx3) ? idx3 : -1;
            }

            if (int.TryParse(trimmed, out var idx))
            {
                return idx;
            }

            return -1;
        }

        private static string FormatBytes(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                return string.Empty;
            return string.Join(" ", bytes.Select(b => b.ToString("X2")));
        }

        private static string FormatFragments(byte[] fragLabels, ushort[] parts)
        {
            if (fragLabels == null || parts == null)
                return string.Empty;
            var count = Math.Min(4, Math.Min(fragLabels.Length, parts.Length));
            if (count <= 0)
                return string.Empty;

            return string.Join(", ", Enumerable.Range(0, count).Select(i =>
            {
                var p = parts[i];
                var lo = (byte)(p & 0xFF);
                var hi = (byte)((p >> 8) & 0xFF);
                return $"0x{fragLabels[i]:X2}=0x{p:X4}({lo:X2} {hi:X2})";
            }));
        }

        private sealed class MultiLabelCommandAssembler
        {
            private readonly ushort[] _parts = new ushort[4];
            private int _mask;
            private DateTime _firstSeenUtc;
            private readonly Dictionary<byte, int> _labelToIndex;

            private static readonly TimeSpan AssemblyTimeout = TimeSpan.FromMilliseconds(800);

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
                return TryAddFragment(label, payload16, nowUtc, out cmd8, out _);
            }

            public bool TryAddFragment(byte label, ushort payload16, DateTime nowUtc, out byte[] cmd8, out ushort[] partsSnapshot)
            {
                cmd8 = null;
                partsSnapshot = null;

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

                partsSnapshot = (ushort[])_parts.Clone();

                cmd8 = new byte[8];
                for (int j = 0; j < 4; j++)
                {
                    cmd8[j * 2] = (byte)(_parts[j] & 0xFF);
                    cmd8[j * 2 + 1] = (byte)((_parts[j] >> 8) & 0xFF);
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
