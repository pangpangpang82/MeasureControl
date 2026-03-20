using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MeasureControl.Drivers;
using MeasureControl.Drivers.ART4229;
using MeasureControl.Models.Devices;

namespace MeasureControl.Simulations.S_C_8_6_1
{
    public sealed class S_C_8_6_1Simulation : IDisposable
    {
        private ART4229Driver _arincDriver;
        private readonly SemaphoreSlim _arincIoLock = new SemaphoreSlim(1, 1);

        private int _benchTxChannelIndex = -1;
        private int _benchRxChannelIndex = -1;
        private bool _benchRxStarted;

        private CancellationTokenSource _simCts;
        private Task _rxLoopTask;
        private Task _telemetryTask;
        private readonly MultiFrameCommandAssembler _rxAssembler = new MultiFrameCommandAssembler();
        private readonly MultiLabelCommandAssembler _rxLabelAssembler = new MultiLabelCommandAssembler(BenchTxFragmentLabels);

        private volatile bool _telemetryEnabled;
        private readonly Random _rand = new Random();

        private bool _started;

        // WAITS1 协议命令定义 (与MIXTS相同，使用07 01 02前缀)
        private static readonly byte[] EnterAtpCommand = { 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] EnterAtpOk = { 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x02 };
        private static readonly byte[] ExitAtpCommand = { 0x00, 0x02, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01 };
        private static readonly byte[] ExitAtpOk = { 0x00, 0x02, 0x00, 0x01, 0x00, 0x00, 0x00, 0x03 };
        private static readonly byte[] TemperatureTestCommand = { 0x07, 0x01, 0x02, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] TelemetryTemperaturePrefix = { 0x07, 0x01, 0x02, 0x02 };
        private static readonly byte[] TelemetryRawPrefix = { 0x07, 0x01, 0x02, 0x03 };

        private static readonly byte[] BenchTxFragmentLabels = { 0x31, 0x32, 0x33, 0x34 };
        private static readonly byte[] ProductTxFragmentLabels = { 0x09, 0x0A, 0x0B, 0x0C };

        public bool EnableFrameLogging { get; set; } = true;

        public bool IsRealProduct { get; set; }

        public bool UseMultiLabelFragmentation { get; set; } = true;

        public double ArincRate { get; set; } = 100000.0;

        public double SimProductArincRate { get; set; } = 100000.0;

        public int SimProductRxChannelIndex { get; set; } = 4;
        public int SimProductTxChannelIndex { get; set; } = 5;

        public int ArincDeviceIndex { get; set; } = 0;

        public Func<string> GetCurrentResistorGear { get; set; }

        public Func<string> GetCurrentAmbientTemperatureSelection { get; set; }

        public void StopTelemetryOutput()
        {
            _telemetryEnabled = false;
        }

        public async Task StartAsync(string benchTxChannel, string benchRxChannel, Action<string> log)
        {
            if (_started) return;
            if (log == null) log = _ => { };

            log($"[{DateTime.Now:HH:mm:ss}] [SIM] WAITS1 仿真初始化开始");

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
            log($"[{DateTime.Now:HH:mm:ss}] [SIM] WAITS1 仿真初始化完成");
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

            bool readyOk = await EnsureBenchChannelsAsync(benchTxChannel, benchRxChannel, log);
            if (!readyOk)
                throw new InvalidOperationException($"[SIM] bench通道未就绪：TX={benchTxChannel}, RX={benchRxChannel}");

            int txIndex = ParseChannelIndex(benchTxChannel);
            int rxIndex = ParseChannelIndex(benchRxChannel);

            if (UseMultiLabelFragmentation)
                log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] bench发送: tx={txIndex}, rx={rxIndex}, labels={string.Join("/", BenchTxFragmentLabels.Select(b => $"0x{b:X2}"))}, payload8={FormatBytes(command8)}");
            else
                log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] bench发送: tx={txIndex}, rx={rxIndex}, label=0x{label:X2}, payload8={FormatBytes(command8)}");

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

            bool openTxOk = await _arincDriver.OpenTxChannelAsync(txIndex);
            if (!openTxOk)
                throw new InvalidOperationException($"[SIM] TX通道打开失败: tx={txIndex}");

            await _arincDriver.ConfigureTxChannelAsync(txIndex, ArincRate, sendMode: 0, parity: 1, wordFormat: 0);

            if (UseMultiLabelFragmentation)
                log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] bench发送(仅发送): tx={txIndex}, labels={string.Join("/", BenchTxFragmentLabels.Select(b => $"0x{b:X2}"))}, payload8={FormatBytes(command8)}");
            else
                log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] bench发送(仅发送): tx={txIndex}, label=0x{label:X2}, payload8={FormatBytes(command8)}");

            await SendMultiFrameOnChannelAsync(txIndex, label, command8, log, token);
        }

        public async Task ClearRxFifoAsync(string benchRxChannel)
        {
            if (_arincDriver == null)
                return;

            int rxIndex = ParseChannelIndex(benchRxChannel);
            try
            {
                await _arincDriver.ReadReceiveDataAsync(rxIndex, maxCount: 4096, enableTimeTag: false, enableRateAdaption: false);
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

            int rxIndex = ParseChannelIndex(benchRxChannel);
            var assemblers = new Dictionary<byte, MultiFrameCommandAssembler>();
            MultiLabelCommandAssembler labelAssembler = UseMultiLabelFragmentation ? new MultiLabelCommandAssembler(ProductTxFragmentLabels) : null;
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

                        if (UseMultiLabelFragmentation)
                        {
                            if (labelAssembler.TryAddFragment(rxLabel, payload, DateTime.UtcNow, out var resp8) && resp8 != null)
                            {
                                if (isExpectedResponse == null || isExpectedResponse(resp8))
                                {
                                    log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] benchRX={rxIndex} 拼包完成 labels={string.Join("/", ProductTxFragmentLabels.Select(b => $"0x{b:X2}"))} resp8={FormatBytes(resp8)}");
                                    return resp8;
                                }
                            }
                        }
                        else
                        {
                            if (!assemblers.TryGetValue(rxLabel, out var assembler))
                            {
                                assembler = new MultiFrameCommandAssembler();
                                assemblers[rxLabel] = assembler;
                            }

                            if (assembler.TryAddFragment(rxLabel, sdi, payload, DateTime.UtcNow, out var resp8) && resp8 != null)
                            {
                                if (isExpectedResponse == null || isExpectedResponse(resp8))
                                {
                                    log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] benchRX={rxIndex} 拼包完成 label=0x{rxLabel:X2} resp8={FormatBytes(resp8)}");
                                    return resp8;
                                }
                            }
                        }
                    }
                }

                await Task.Delay(10, token);
            }

            return null;
        }

        public async Task<(byte[] Temperature, byte[] Raw)> WaitTelemetryAsync(
            string benchRxChannel,
            int timeoutMs,
            Action<string> log,
            CancellationToken token)
        {
            if (!_started || _arincDriver == null)
                throw new InvalidOperationException("Simulation not started");

            int rxIndex = ParseChannelIndex(benchRxChannel);
            byte[] temperature = null;
            byte[] raw = null;
            var assemblers = new Dictionary<byte, MultiFrameCommandAssembler>();
            MultiLabelCommandAssembler labelAssembler = UseMultiLabelFragmentation ? new MultiLabelCommandAssembler(ProductTxFragmentLabels) : null;
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

                        if (UseMultiLabelFragmentation)
                        {
                            if (labelAssembler.TryAddFragment(rxLabel, payload, DateTime.UtcNow, out var resp8) && resp8 != null)
                            {
                                if (temperature == null && IsPrefix(resp8, TelemetryTemperaturePrefix))
                                {
                                    temperature = resp8;
                                    log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] 温度采集值拼包完成：{FormatBytes(resp8)}");
                                }
                                else if (raw == null && IsPrefix(resp8, TelemetryRawPrefix))
                                {
                                    raw = resp8;
                                    log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] 原始数据拼包完成：{FormatBytes(resp8)}");
                                }
                            }
                        }
                        else
                        {
                            if (!assemblers.TryGetValue(rxLabel, out var assembler))
                            {
                                assembler = new MultiFrameCommandAssembler();
                                assemblers[rxLabel] = assembler;
                            }

                            if (assembler.TryAddFragment(rxLabel, sdi, payload, DateTime.UtcNow, out var resp8) && resp8 != null)
                            {
                                if (temperature == null && IsPrefix(resp8, TelemetryTemperaturePrefix))
                                {
                                    temperature = resp8;
                                    log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] 温度采集值拼包完成：{FormatBytes(resp8)}");
                                }
                                else if (raw == null && IsPrefix(resp8, TelemetryRawPrefix))
                                {
                                    raw = resp8;
                                    log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] 原始数据拼包完成：{FormatBytes(resp8)}");
                                }
                            }
                        }
                    }
                }

                if (temperature != null && raw != null)
                    return (temperature, raw);

                await Task.Delay(10, token);
            }

            return (temperature, raw);
        }

        public async Task StopAsync(Action<string> log)
        {
            if (log == null) log = _ => { };

            if (!_started)
            {
                await CleanupAsync();
                return;
            }

            log($"[{DateTime.Now:HH:mm:ss}] [SIM] WAITS1 仿真停止：释放设备资源");

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
                                bool assembled;
                                byte[] cmd8;

                                if (UseMultiLabelFragmentation)
                                {
                                    assembled = _rxLabelAssembler.TryAddFragment(label, payload, DateTime.UtcNow, out cmd8);
                                }
                                else
                                {
                                    assembled = _rxAssembler.TryAddFragment(label, sdi, payload, DateTime.UtcNow, out cmd8);
                                }

                                if (assembled)
                                {
                                    log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] simRX={SimProductRxChannelIndex} 拼包完成 cmd8={FormatBytes(cmd8)}");

                                    if (cmd8.SequenceEqual(EnterAtpCommand))
                                    {
                                        log($"[{DateTime.Now:HH:mm:ss}] [SIM] 产品侧收到进入ATP指令 -> 回复 ATP OK");
                                        await SendMultiFrameResponseAsync(label, EnterAtpOk, log, token);
                                    }
                                    else if (cmd8.SequenceEqual(ExitAtpCommand))
                                    {
                                        log($"[{DateTime.Now:HH:mm:ss}] [SIM] 产品侧收到退出ATP指令 -> 回复 EXIT OK");
                                        _telemetryEnabled = false;
                                        await SendMultiFrameResponseAsync(label, ExitAtpOk, log, token);
                                    }
                                    else if (cmd8.SequenceEqual(TemperatureTestCommand))
                                    {
                                        log($"[{DateTime.Now:HH:mm:ss}] [SIM] 产品侧收到温度测试指令 -> 回复确认并开启遥测");
                                        await SendMultiFrameResponseAsync(label, TemperatureTestCommand, log, token);
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

                    string gear = GetCurrentResistorGear?.Invoke() ?? "1挡";
                    string ambientSelection = GetCurrentAmbientTemperatureSelection?.Invoke() ?? "10~50℃";
                    double temperature = GenerateSimulatedTemperature(gear, ambientSelection);

                    var tempPayload = new byte[8];
                    tempPayload[0] = TelemetryTemperaturePrefix[0];
                    tempPayload[1] = TelemetryTemperaturePrefix[1];
                    tempPayload[2] = TelemetryTemperaturePrefix[2];
                    tempPayload[3] = TelemetryTemperaturePrefix[3];

                    int intPart = (int)temperature;
                    int fracPart = (int)(Math.Abs(temperature - intPart) * 10000);
                    tempPayload[4] = (byte)((intPart >> 8) & 0xFF);
                    tempPayload[5] = (byte)(intPart & 0xFF);
                    tempPayload[6] = (byte)((fracPart >> 8) & 0xFF);
                    tempPayload[7] = (byte)(fracPart & 0xFF);

                    await SendMultiFrameResponseAsync(label, tempPayload, log, token);

                    var rawPayload = new byte[8];
                    rawPayload[0] = TelemetryRawPrefix[0];
                    rawPayload[1] = TelemetryRawPrefix[1];
                    rawPayload[2] = TelemetryRawPrefix[2];
                    rawPayload[3] = TelemetryRawPrefix[3];

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

        private double GenerateSimulatedTemperature(string gear, string ambientSelection)
        {
            var (min, max) = GetQualifiedTemperatureRange(gear, ambientSelection);
            lock (_rand)
            {
                return min + _rand.NextDouble() * (max - min);
            }
        }

        private static bool IsAmbientTemperatureBetween10And50(string ambientSelection)
            => string.Equals(ambientSelection, "10~50℃", StringComparison.Ordinal);

        private static (double Min, double Max) GetQualifiedTemperatureRange(string gear, string ambientSelection)
        {
            var ambient = IsAmbientTemperatureBetween10And50(ambientSelection);
            return gear switch
            {
                "1挡" => ambient ? (-65.93, -64.07) : (-69.05, -60.95),
                "2挡" => ambient ? (24.75, 26.61) : (21.63, 29.73),
                "3挡" => ambient ? (134.06, 135.94) : (130.94, 139.06),
                _ => ambient ? (-65.93, -64.07) : (-69.05, -60.95)
            };
        }

        private async Task SendMultiFrameResponseAsync(byte label, byte[] payload8, Action<string> log, CancellationToken token)
        {
            if (payload8 == null || payload8.Length != 8)
                return;

            uint[] data429 = new uint[4];
            uint[] parity = new uint[4];

            for (byte frag = 0; frag < 4; frag++)
            {
                ushort part = (ushort)((payload8[frag * 2] << 8) | payload8[frag * 2 + 1]);
                byte fragLabel = UseMultiLabelFragmentation ? ProductTxFragmentLabels[frag] : label;
                byte sdi = UseMultiLabelFragmentation ? (byte)0 : frag;
                uint word = BuildWord(fragLabel, sdi, part);
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
                if (UseMultiLabelFragmentation)
                    log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] simTX={SimProductTxChannelIndex} send labels={string.Join("/", ProductTxFragmentLabels.Select(b => $"0x{b:X2}"))} payload8={FormatBytes(payload8)}");
                else
                    log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] simTX={SimProductTxChannelIndex} send label=0x{label:X2} payload8={FormatBytes(payload8)}");
            }
        }

        private async Task SendMultiFrameOnChannelAsync(int txChannelIndex, byte label, byte[] payload8, Action<string> log, CancellationToken token)
        {
            if (_arincDriver == null)
                return;
            if (payload8 == null || payload8.Length != 8)
                return;

            uint[] data429 = new uint[4];
            uint[] parity = new uint[4];

            for (byte frag = 0; frag < 4; frag++)
            {
                ushort part = (ushort)((payload8[frag * 2] << 8) | payload8[frag * 2 + 1]);
                byte fragLabel = UseMultiLabelFragmentation ? BenchTxFragmentLabels[frag] : label;
                byte sdi = UseMultiLabelFragmentation ? (byte)0 : frag;
                uint word = BuildWord(fragLabel, sdi, part);
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
                if (UseMultiLabelFragmentation)
                    log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] benchTX={txChannelIndex} send labels={string.Join("/", BenchTxFragmentLabels.Select(b => $"0x{b:X2}"))} payload8={FormatBytes(payload8)}");
                else
                    log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] benchTX={txChannelIndex} send label=0x{label:X2} payload8={FormatBytes(payload8)}");
            }
        }

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
                _rxAssembler.Reset();
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
                _label = 0;
                _firstSeenUtc = default;
            }
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
