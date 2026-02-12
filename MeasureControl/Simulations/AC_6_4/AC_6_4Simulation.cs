using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MeasureControl.Drivers;
using MeasureControl.Drivers.ART4229;
using MeasureControl.Models.Devices;

namespace MeasureControl.Simulations.AC_6_4
{
    public sealed class AC_6_4Simulation : IDisposable
    {
        private TcpClient _powerSupplyClient;
        private NetworkStream _powerSupplyStream;
        private readonly SemaphoreSlim _powerSupplyIoLock = new SemaphoreSlim(1, 1);

        private ART4229Driver _arincDriver;
        private readonly SemaphoreSlim _arincIoLock = new SemaphoreSlim(1, 1);

        private int _benchTxChannelIndex = -1;
        private int _benchRxChannelIndex = -1;
        private bool _benchRxStarted;

        private CancellationTokenSource _simCts;
        private Task _rxLoopTask;
        private readonly MultiFrameCommandAssembler _rxAssembler = new MultiFrameCommandAssembler();
        private readonly MultiLabelCommandAssembler _rxLabelAssembler = new MultiLabelCommandAssembler(CanBenchCommandFragLabels);

        private Task _telemetryTask;
        private volatile bool _outputEnabled;
        private readonly Random _rand = new Random();

        private bool _started;

        private static readonly byte[] EnterAtpCommand = { 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] EnterAtpOk = { 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x02 };
        private static readonly byte[] ExitAtpCommand = { 0x00, 0x02, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01 };
        private static readonly byte[] ExitAtpOk = { 0x00, 0x02, 0x00, 0x01, 0x00, 0x00, 0x00, 0x03 };

        private static readonly byte[] EnableOutputCommand = { 0x01, 0x04, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] EnableOutputAck = { 0x01, 0x04, 0x01, 0x01, 0x00, 0x00, 0x00, 0x01 };

        private static readonly byte[] CanCommTestCommand = { 0x05, 0x01, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private const int CanCommTestExpectedValue = 12700;

        private static readonly byte[] CanCommReceiveCommand = { 0x05, 0x02, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] CanCommReceiveResponse = { 0x04, 0x01, 0x02, 0x03, 0x01, 0x01, 0x01, 0x01 };

        private static readonly int[] DsiChannels =
        {
            0, 1, 2, 3, 5, 6, 7, 8, 9, 12,
            13, 14, 15, 16, 17, 18, 19, 20, 21, 22,
            23, 24, 25, 26, 27, 28, 29, 35, 36
        };

        private static readonly byte[] CanBenchCommandFragLabels = { 0x31, 0x32, 0x33, 0x34 };
        private static readonly byte[] CanBenchResponseFragLabels = { 0x09, 0x0A, 0x0B, 0x0C };

        public bool EnableFrameLogging { get; set; } = true;

        public double ArincRate { get; set; } = 100000.0;

        public double SimProductArincRate { get; set; } = 100000.0;

        public int SimProductRxChannelIndex { get; set; } = 4;
        public int SimProductTxChannelIndex { get; set; } = 5;

        public GndOcState DsiSimInputMode { get; set; } = GndOcState.Oc;

        public string PowerSupplyIpAddress { get; set; } = "192.168.1.15";
        public int PowerSupplyPort { get; set; } = 30000;

        public int ArincDeviceIndex { get; set; } = 0;

        public Func<Action<string>, CancellationToken, Task> OnOutputEnabledAsync { get; set; }

        /// <summary>
        /// 停止遥测输出（不停止整个仿真），用于测试完成后停止遥测循环打印日志
        /// </summary>
        public void StopTelemetryOutput()
        {
            _outputEnabled = false;
        }

        public async Task StartAsync(string benchTxChannel, string benchRxChannel, Action<string> log)
        {
            if (_started) return;
            if (log == null) log = _ => { };

            log($"[{DateTime.Now:HH:mm:ss}] [SIM] 6_4 仿真初始化开始");

            _simCts = new CancellationTokenSource();

            await ConnectPowerSupplyAsync(log);//连接程控电源

            await OpenArincDeviceAsync(log);//打开ARINC429板卡

            int benchTxIndex = ParseChannelIndex(benchTxChannel);//解析Bench侧TX通道
            int benchRxIndex = ParseChannelIndex(benchRxChannel);//解析Bench侧RX通道

            // 通道冲突保护：同一物理通道不能同时被当作 TX/RX，也不能与仿真产品侧通道冲突
            if (benchTxIndex == benchRxIndex)
                throw new InvalidOperationException($"[SIM] bench TX/RX 通道冲突：TX={benchTxIndex}, RX={benchRxIndex}");
            if (benchTxIndex == SimProductRxChannelIndex || benchTxIndex == SimProductTxChannelIndex)
                throw new InvalidOperationException($"[SIM] benchTX 与产品侧通道冲突：benchTX={benchTxIndex}, simRX={SimProductRxChannelIndex}, simTX={SimProductTxChannelIndex}");
            if (benchRxIndex == SimProductRxChannelIndex || benchRxIndex == SimProductTxChannelIndex)
                throw new InvalidOperationException($"[SIM] benchRX 与产品侧通道冲突：benchRX={benchRxIndex}, simRX={SimProductRxChannelIndex}, simTX={SimProductTxChannelIndex}");

            _benchTxChannelIndex = benchTxIndex;
            _benchRxChannelIndex = benchRxIndex;

            await ConfigureArincChannelsAsync(benchTxIndex, benchRxIndex, log);

            await StartBenchRxAsync(log);

            await StartSimProductRxAsync(log, _simCts.Token);

            // TODO: 在此处加入矩阵开关通路配置（由你后续实现）

            _started = true;
            log($"[{DateTime.Now:HH:mm:ss}] [SIM] 6_4 仿真初始化完成");
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
                if (tx == SimProductRxChannelIndex || tx == SimProductTxChannelIndex)
                {
                    log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] benchTX 与产品侧通道冲突：benchTX={tx}, simRX={SimProductRxChannelIndex}, simTX={SimProductTxChannelIndex}");
                    return false;
                }
                if (rx == SimProductRxChannelIndex || rx == SimProductTxChannelIndex)
                {
                    log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] benchRX 与产品侧通道冲突：benchRX={rx}, simRX={SimProductRxChannelIndex}, simTX={SimProductTxChannelIndex}");
                    return false;
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

        private async Task SendMultiFrameResponseWithLabelsAsync(byte[] labels4, byte[] payload8, Action<string> log, CancellationToken token)
        {
            if (labels4 == null || labels4.Length != 4)
                return;
            if (payload8 == null || payload8.Length != 8)
                return;

            uint[] data429 = new uint[4];
            uint[] parity = new uint[4];

            for (byte frag = 0; frag < 4; frag++)
            {
                ushort part = (ushort)((payload8[frag * 2] << 8) | payload8[frag * 2 + 1]);
                uint word = BuildWord(labels4[frag], 0, part);
                data429[frag] = ApplyParity(word);
                parity[frag] = 1;
            }

            bool ok = await _arincDriver.SendDataSingleAsync(SimProductTxChannelIndex, data429, parity);
            if (!ok)
            {
                log($"[{DateTime.Now:HH:mm:ss}] [SIM] 产品侧回包发送失败: simTX={SimProductTxChannelIndex}");
            }
            else if (EnableFrameLogging)
            {
                log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] simTX={SimProductTxChannelIndex} send labels={FormatBytes(labels4)} payload8={FormatBytes(payload8)}");
                for (int i = 0; i < data429.Length; i++)
                {
                    TryParseWord(data429[i], out var txLabel, out var sdi, out var payload);
                    log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] simTX={SimProductTxChannelIndex} send[{i}] raw=0x{data429[i]:X8} label=0x{txLabel:X2} sdi={sdi} payload=0x{payload:X4}");
                }
            }
        }

        private async Task<bool> EnsureBenchRxChannelAsync(string benchRxChannel, Action<string> log)
        {
            if (_arincDriver == null)
                return false;

            int rx = ParseChannelIndex(benchRxChannel);

            const int wordFormat = 0;
            const int parity = 1;
            double rxRate = ArincRate;

            try
            {
                if (rx == SimProductRxChannelIndex || rx == SimProductTxChannelIndex)
                {
                    log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] benchRX 与产品侧通道冲突：benchRX={rx}, simRX={SimProductRxChannelIndex}, simTX={SimProductTxChannelIndex}");
                    return false;
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

        public async Task<byte[]> SendBenchCommandAndWaitWithFragmentLabelsAsync(
            string benchTxChannel,
            string benchRxChannel,
            byte[] command8,
            Func<byte[], bool> isExpectedResponse,
            int timeoutMs,
            Action<string> log,
            CancellationToken token)
        {
            if (command8 == null || command8.Length != 8)
                throw new ArgumentException("command8必须为8字节", nameof(command8));

            bool readyOk = await EnsureBenchChannelsAsync(benchTxChannel, benchRxChannel, log);
            if (!readyOk)
                throw new InvalidOperationException($"[SIM] bench通道未就绪：TX={benchTxChannel}, RX={benchRxChannel}");

            int txIndex = ParseChannelIndex(benchTxChannel);
            int rxIndex = ParseChannelIndex(benchRxChannel);

            log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] bench发送(分片label): tx={txIndex}, rx={rxIndex}, labels={FormatBytes(CanBenchCommandFragLabels)}, payload8={FormatBytes(command8)}");
            await SendMultiFrameOnChannelWithLabelsAsync(txIndex, CanBenchCommandFragLabels, command8, log, token);

            return await WaitBenchResponseWithLabelsAsync(
                benchRxChannel,
                CanBenchResponseFragLabels,
                isExpectedResponse,
                timeoutMs,
                log,
                token);
        }

        private async Task SendMultiFrameOnChannelWithLabelsAsync(int txChannelIndex, byte[] labels4, byte[] payload8, Action<string> log, CancellationToken token)
        {
            if (_arincDriver == null)
                return;
            if (labels4 == null || labels4.Length != 4)
                return;
            if (payload8 == null || payload8.Length != 8)
                return;

            uint[] data429 = new uint[4];
            uint[] parity = new uint[4];

            for (byte frag = 0; frag < 4; frag++)
            {
                ushort part = (ushort)((payload8[frag * 2] << 8) | payload8[frag * 2 + 1]);
                uint word = BuildWord(labels4[frag], 0, part);
                data429[frag] = ApplyParity(word);
                parity[frag] = 1;
            }

            bool ok = await _arincDriver.SendDataSingleAsync(txChannelIndex, data429, parity);
            if (!ok)
            {
                log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] bench侧发送失败: tx={txChannelIndex}");
            }
            else if (EnableFrameLogging)
            {
                log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] benchTX={txChannelIndex} send labels={FormatBytes(labels4)} payload8={FormatBytes(payload8)}");
                for (int i = 0; i < data429.Length; i++)
                {
                    TryParseWord(data429[i], out var txLabel, out var sdi, out var payload);
                    log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] benchTX={txChannelIndex} send[{i}] raw=0x{data429[i]:X8} label=0x{txLabel:X2} sdi={sdi} payload=0x{payload:X4}");
                }
            }
        }

        private async Task<byte[]> WaitBenchResponseWithLabelsAsync(
            string benchRxChannel,
            byte[] labels4,
            Func<byte[], bool> isExpectedResponse,
            int timeoutMs,
            Action<string> log,
            CancellationToken token)
        {
            if (!_started || _arincDriver == null)
                throw new InvalidOperationException("Simulation not started");
            if (labels4 == null || labels4.Length != 4)
                throw new ArgumentException("labels4必须为4字节", nameof(labels4));

            int rxIndex = ParseChannelIndex(benchRxChannel);
            await EnsureBenchRxChannelAsync(benchRxChannel, log);

            var assembler = new MultiLabelCommandAssembler(labels4);
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
                                log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] benchRX={rxIndex} recv(raw) raw=0x{item.Data429:X8} label=0x{rxLabel:X2} sdi={sdi} payload=0x{payload:X4}");
                                rxLogCount++;
                                if (rxLogCount == maxRxLog)
                                {
                                    log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] benchRX={rxIndex} recv日志已达上限({maxRxLog})，后续帧不再打印(避免刷屏)");
                                }
                            }
                        }

                        if (assembler.TryAddFragment(rxLabel, payload, DateTime.UtcNow, out var resp8) && resp8 != null)
                        {
                            log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] benchRX={rxIndex} 拼包完成(labels={FormatBytes(labels4)}) resp8={FormatBytes(resp8)}");
                            if (isExpectedResponse == null || isExpectedResponse(resp8))
                                return resp8;
                        }
                    }
                }

                await Task.Delay(10, token);
            }

            return null;
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
            // 注意：不要在这里重新配置接收通道，因为SendBenchCommandAndWaitAsync已经配置过了
            // 重新配置会清空已接收的数据，导致响应丢失

            // 注意：现场设备可能不会使用我们发送时的固定 label，
            // 这里不要强依赖 label 过滤，改为按“收到的 label”分别组包。
            // 这样即使 label 不一致，也能正确组出 8 字节响应并由 isExpectedResponse 判定。
            var assemblers = new Dictionary<byte, MultiFrameCommandAssembler>();// 使用字典管理不同标签的组包器
            var deadline = DateTime.UtcNow.AddMilliseconds(Math.Max(100, timeoutMs));
            int rxLogCount = 0;
            const int maxRxLog = 32;

            while (!token.IsCancellationRequested && DateTime.UtcNow <= deadline)
            {
                var list = await _arincDriver.ReadReceiveDataAsync(rxIndex, maxCount: 256, enableTimeTag: false, enableRateAdaption: false);//读取接收数据
                if (list != null && list.Count > 0)
                {
                    foreach (var item in list)
                    {
                        if (!TryParseWord(item.Data429, out var rxLabel, out var sdi, out var payload))//解析接收到的429数据，对每个接收到的字进行解析
                            continue;

                        if (EnableFrameLogging)
                        {
                            if (rxLogCount < maxRxLog)//日志打印数量限制
                            {
                                log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] benchRX={rxIndex} recv raw=0x{item.Data429:X8} label=0x{rxLabel:X2} sdi={sdi} payload=0x{payload:X4}");
                                rxLogCount++;
                                if (rxLogCount == maxRxLog)
                                {
                                    log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] benchRX={rxIndex} recv日志已达上限({maxRxLog})，后续帧不再打印(避免刷屏)");
                                }
                            }
                        }

                        // 仅用于排查：当你传入固定 label 时，如果一直超时，可打开日志观察 rxLabel 是否不同。
                        // 这里不打印每一帧，避免刷屏；只在首次看到某个 label 时打印一次。
                        if (log != null && !assemblers.ContainsKey(rxLabel))
                            log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] benchRX={rxIndex} 收到新label=0x{rxLabel:X2} (expected=0x{label:X2})");

                        if (!assemblers.TryGetValue(rxLabel, out var assembler))
                        {
                            assembler = new MultiFrameCommandAssembler();
                            assemblers[rxLabel] = assembler;
                        }

                        // 尝试添加分片并检测是否完成组包
                        if (assembler.TryAddFragment(rxLabel, sdi, payload, DateTime.UtcNow, out var resp8) && resp8 != null)
                        {
                            if (isExpectedResponse == null || isExpectedResponse(resp8))
                            {
                                log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] benchRX={rxIndex} 拼包完成 label=0x{rxLabel:X2} resp8={FormatBytes(resp8)}");
                                return resp8;//返回拼包完成的8字节响应
                            }
                        }
                    }
                }

                await Task.Delay(10, token);
            }

            return null;
        }

        public async Task StopAsync(Action<string> log)
        {
            if (log == null) log = _ => { };

            if (!_started)
            {
                await CleanupAsync();
                return;
            }

            log($"[{DateTime.Now:HH:mm:ss}] [SIM] 6_4 仿真停止：释放设备资源");

            // 先设置_started为false，防止重入
            _started = false;
            _outputEnabled = false;

            try
            {
                _simCts?.Cancel();
            }
            catch
            {
            }

            // 等待产品侧RX循环结束
            try
            {
                if (_rxLoopTask != null)
                    await _rxLoopTask.ConfigureAwait(false);
            }
            catch
            {
            }

            // 等待遥测循环结束
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
            // 协议假设（后续可按真实产品协议调整）：
            // - 8字节命令通过4帧ARINC429承载，每帧携带2字节payload
            // - 同一条命令的4帧使用同一个label
            // - SDI(2bit) = 分片序号 0..3
            // - Data(19bit) 的低16bit存放payload两字节
            // - 429 word 位域按常见定义解析：label[1..8]在最低8bit，SDI[9..10]在bit8..9，data[11..29]在bit10..28，SSM[30..31]在bit29..30
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
                                if (_rxLabelAssembler.TryAddFragment(label, payload, DateTime.UtcNow, out var cmd8ByLabel) && cmd8ByLabel != null)
                                {
                                    log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] simRX={SimProductRxChannelIndex} 拼包完成(labels={FormatBytes(CanBenchCommandFragLabels)}) cmd8={FormatBytes(cmd8ByLabel)}");
                                    if (cmd8ByLabel.SequenceEqual(CanCommTestCommand))
                                    {
                                        var payloadResp = new byte[8];
                                        payloadResp[0] = CanCommTestCommand[0];
                                        payloadResp[1] = CanCommTestCommand[1];
                                        payloadResp[2] = CanCommTestCommand[2];
                                        payloadResp[3] = CanCommTestCommand[3];
                                        payloadResp[4] = (byte)((CanCommTestExpectedValue >> 24) & 0xFF);
                                        payloadResp[5] = (byte)((CanCommTestExpectedValue >> 16) & 0xFF);
                                        payloadResp[6] = (byte)((CanCommTestExpectedValue >> 8) & 0xFF);
                                        payloadResp[7] = (byte)(CanCommTestExpectedValue & 0xFF);

                                        log($"[{DateTime.Now:HH:mm:ss}] [SIM] 产品侧收到CAN发送测试指令(分片label) -> 回包 labels={FormatBytes(CanBenchResponseFragLabels)} payload8={FormatBytes(payloadResp)}");
                                        await SendMultiFrameResponseWithLabelsAsync(CanBenchResponseFragLabels, payloadResp, log, token);
                                    }
                                    else if (cmd8ByLabel.SequenceEqual(CanCommReceiveCommand))
                                    {
                                        log($"[{DateTime.Now:HH:mm:ss}] [SIM] 产品侧收到CAN接收测试指令(分片label) -> 回包 labels={FormatBytes(CanBenchResponseFragLabels)} payload8={FormatBytes(CanCommReceiveResponse)}");
                                        await SendMultiFrameResponseWithLabelsAsync(CanBenchResponseFragLabels, CanCommReceiveResponse, log, token);
                                    }
                                }

                                if (_rxAssembler.TryAddFragment(label, sdi, payload, DateTime.UtcNow, out var cmd8))
                                {
                                    log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] simRX={SimProductRxChannelIndex} 拼包完成 label=0x{label:X2} cmd8={FormatBytes(cmd8)}");
                                    if (cmd8.SequenceEqual(EnterAtpCommand))
                                    {
                                        log($"[{DateTime.Now:HH:mm:ss}] [SIM] 产品侧收到进入ATP指令 -> 回复 ATP OK");
                                        await SendMultiFrameResponseAsync(label, EnterAtpOk, log, token);
                                    }
                                    else if (cmd8.SequenceEqual(ExitAtpCommand))
                                    {
                                        log($"[{DateTime.Now:HH:mm:ss}] [SIM] 产品侧收到退出ATP指令 -> 回复 EXIT OK");
                                        _outputEnabled = false;
                                        try
                                        {
                                            await SendPowerSupplyScpiAsync("OUTP OFF,(@1)", 5000, token);
                                        }
                                        catch
                                        {
                                        }
                                        await SendMultiFrameResponseAsync(label, ExitAtpOk, log, token);
                                    }
                                    else if (cmd8.SequenceEqual(EnableOutputCommand))
                                    {
                                        log($"[{DateTime.Now:HH:mm:ss}] [SIM] 产品侧收到开启输出指令 -> 直接开启程控电源输出");

                                        double outputVoltage = NextDouble(13.5, 16.5);//设置输出电压
                                        double outputCurrent = NextDouble(0.1, 0.2);//设置输出电流
                                        try
                                        {
                                            await SendPowerSupplyScpiAsync($"VOLT {outputVoltage:F3},(@1)", 5000, token);
                                            await SendPowerSupplyScpiAsync($"CURR {outputCurrent:F3},(@1)", 5000, token);
                                            await SendPowerSupplyScpiAsync("OUTP:PROT:CLE", 5000, token);//清除保护
                                            await SendPowerSupplyScpiAsync("OUTP ON,(@1)", 5000, token);//开启输出
                                        }
                                        catch
                                        {
                                        }

                                        _outputEnabled = true;

                                        try
                                        {
                                            var cb = OnOutputEnabledAsync;
                                            if (cb != null)
                                                await cb(log, token);
                                        }
                                        catch
                                        {
                                        }

                                        StartTelemetryLoopIfNeeded(label, log);//开启遥测循环
                                    }
                                    else if (cmd8.SequenceEqual(CanCommTestCommand))
                                    {
                                        var respPayload8 = new byte[8];
                                        respPayload8[0] = CanCommTestCommand[0];
                                        respPayload8[1] = CanCommTestCommand[1];
                                        respPayload8[2] = CanCommTestCommand[2];
                                        respPayload8[3] = CanCommTestCommand[3];

                                        respPayload8[4] = (byte)((CanCommTestExpectedValue >> 24) & 0xFF);
                                        respPayload8[5] = (byte)((CanCommTestExpectedValue >> 16) & 0xFF);
                                        respPayload8[6] = (byte)((CanCommTestExpectedValue >> 8) & 0xFF);
                                        respPayload8[7] = (byte)(CanCommTestExpectedValue & 0xFF);

                                        log($"[{DateTime.Now:HH:mm:ss}] [SIM] 产品侧收到CAN发送测试指令 -> 回包 payload8={FormatBytes(respPayload8)}");
                                        await SendMultiFrameResponseAsync(label, respPayload8, log, token);
                                    }
                                    else if (cmd8.SequenceEqual(CanCommReceiveCommand))
                                    {
                                        log($"[{DateTime.Now:HH:mm:ss}] [SIM] 产品侧收到CAN接收测试指令 -> 回包 payload8={FormatBytes(CanCommReceiveResponse)}");
                                        await SendMultiFrameResponseAsync(label, CanCommReceiveResponse, log, token);
                                    }
                                    else if (TryBuildDsiResponse(cmd8, DsiSimInputMode, out var dsiResp8))
                                    {
                                        log($"[{DateTime.Now:HH:mm:ss}] [SIM] 产品侧收到离散输入测试指令 -> 回包 payload8={FormatBytes(dsiResp8)}");
                                        await SendMultiFrameResponseAsync(label, dsiResp8, log, token);
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

        private async Task SendMultiFrameResponseAsync(byte label, byte[] payload8, Action<string> log, CancellationToken token)
        {
            if (payload8 == null || payload8.Length != 8)
                return;

            uint[] data429 = new uint[4];
            uint[] parity = new uint[4];

            // 每帧2字节
            for (byte frag = 0; frag < 4; frag++)
            {
                ushort part = (ushort)((payload8[frag * 2] << 8) | payload8[frag * 2 + 1]);
                uint word = BuildWord(label, frag, part);
                data429[frag] = ApplyParity(word);
                parity[frag] = 1; // Odd
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
                log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] simTX={SimProductTxChannelIndex} send label=0x{label:X2} payload8={FormatBytes(payload8)}");
                for (int i = 0; i < data429.Length; i++)
                {
                    TryParseWord(data429[i], out var txLabel, out var sdi, out var payload);
                    log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] simTX={SimProductTxChannelIndex} send[{i}] raw=0x{data429[i]:X8} label=0x{txLabel:X2} sdi={sdi} payload=0x{payload:X4}");
                }
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
                uint word = BuildWord(label, frag, part);
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
                log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] benchTX={txChannelIndex} send label=0x{label:X2} payload8={FormatBytes(payload8)}");
                for (int i = 0; i < data429.Length; i++)
                {
                    TryParseWord(data429[i], out var txLabel, out var sdi, out var payload);
                    log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] benchTX={txChannelIndex} send[{i}] raw=0x{data429[i]:X8} label=0x{txLabel:X2} sdi={sdi} payload=0x{payload:X4}");
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
            // 协议假设（可调整）：
            // - 产品开启输出后，周期性上报“传感器供电电压回采并上传”
            // - 上报8字节包：01 04 01 02 ff ff ff ff
            //   其中 ff ff ff ff 为回采电压的 IEEE754 float(4字节)，固定字节序：big-endian
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (!_outputEnabled)
                    {
                        await Task.Delay(100, token);
                        continue;
                    }

                    double sense = NextDouble(2.25, 2.75);
                    float senseF = (float)sense;
                    var fbytes = BitConverter.GetBytes(senseF);
                    if (BitConverter.IsLittleEndian)
                        Array.Reverse(fbytes);

                    var payload = new byte[8];
                    payload[0] = 0x01;
                    payload[1] = 0x04;
                    payload[2] = 0x01;
                    payload[3] = 0x02;
                    payload[4] = fbytes[0];
                    payload[5] = fbytes[1];
                    payload[6] = fbytes[2];
                    payload[7] = fbytes[3];

                    await SendMultiFrameResponseAsync(label, payload, log, token);

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

        private double NextDouble(double min, double max)
        {
            lock (_rand)
            {
                return min + _rand.NextDouble() * (max - min);
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
            // 位域假设见 SimProductRxLoopAsync 注释
            uint word = 0;
            word |= label;
            word |= (uint)(sdi & 0x3) << 8;
            word |= (uint)payload16 << 10;
            return word;
        }

        private static uint ApplyParity(uint word)
        {
            // 奇校验：统计 bit0..30 的1数量，使得(包含parity位bit31)整体为奇数
            // 注意：此处假设最高位(bit31)为校验位。若厂家驱动/库对校验位处理不同，可在此处调整。
            uint data = word & 0x7FFFFFFF;
            int ones = 0;
            uint tmp = data;
            while (tmp != 0)
            {
                tmp &= (tmp - 1);
                ones++;
            }

            bool needParityBit = (ones % 2 == 0); // 现有为偶数 -> 置1变奇数
            return needParityBit ? (data | 0x80000000) : data;
        }

        private async Task OpenArincDeviceAsync(Action<string> log)
        {
            try
            {
                var device = new Arinc429Device("PXIe-4227", "Slot0")
                {
                    // DriverFactory 依赖 Model 字段来判断具体板卡类型
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
            // 这里的配置参数参考 ART4229ConfigPanelViewModel 的常用默认值
            // wordFormat: 0 表示标准429
            const int wordFormat = 0;
            const int parity = 1; // Odd奇校验
            double benchTxRate = ArincRate;
            double benchRxRate = ArincRate;
            double simTxRate = SimProductArincRate;
            double simRxRate = SimProductArincRate;

            bool openBenchTx = await _arincDriver.OpenTxChannelAsync(benchTxIndex);
            bool openBenchRx = await _arincDriver.OpenRxChannelAsync(benchRxIndex);
            bool openSimRx = await _arincDriver.OpenRxChannelAsync(SimProductRxChannelIndex);
            bool openSimTx = await _arincDriver.OpenTxChannelAsync(SimProductTxChannelIndex);

            if (!openBenchTx || !openBenchRx || !openSimRx || !openSimTx)
                throw new InvalidOperationException($"[SIM] Open通道失败: benchTX={benchTxIndex}({openBenchTx}), benchRX={benchRxIndex}({openBenchRx}), simRX={SimProductRxChannelIndex}({openSimRx}), simTX={SimProductTxChannelIndex}({openSimTx})");

            await _arincDriver.ConfigureTxChannelAsync(benchTxIndex, benchTxRate, sendMode: 0, parity: parity, wordFormat: wordFormat);
            await _arincDriver.ConfigureRxChannelAsync(benchRxIndex, benchRxRate, parity: parity, wordFormat: wordFormat,
                enableInterrupt: false, interruptDepth: 0, enableTimeTag: false);

            // 仿真产品侧：固定占用通道
            await _arincDriver.ConfigureRxChannelAsync(SimProductRxChannelIndex, simRxRate, parity: parity, wordFormat: wordFormat,
                enableInterrupt: false, interruptDepth: 0, enableTimeTag: false);
            await _arincDriver.ConfigureTxChannelAsync(SimProductTxChannelIndex, simTxRate, sendMode: 0, parity: parity, wordFormat: wordFormat);

            log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] ARINC429 通道已配置: benchTX={benchTxIndex}, benchRX={benchRxIndex}, simRX={SimProductRxChannelIndex}, simTX={SimProductTxChannelIndex}");
        }

        private static string FormatBytes(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                return string.Empty;
            return string.Join(" ", bytes.Select(b => b.ToString("X2")));
        }

        private static int ParseChannelIndex(string channel)
        {
            if (string.IsNullOrWhiteSpace(channel)) return -1;

            var trimmed = channel.Trim();

            // 支持 ARINC429_0 / arinc429_15 格式
            const string prefix1 = "ARINC429_";
            if (trimmed.StartsWith(prefix1, StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed.Substring(prefix1.Length);
                if (int.TryParse(trimmed, out var idx1))
                    return idx1;
                return -1;
            }

            // 支持 429_CH0 / 429_CH15 格式
            const string prefix2 = "429_CH";
            if (trimmed.StartsWith(prefix2, StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed.Substring(prefix2.Length);
                if (int.TryParse(trimmed, out var idx2))
                    return idx2;
                return -1;
            }

            // 尝试直接解析数字
            if (int.TryParse(trimmed, out var idx))
            {
                return idx;
            }

            return -1;
        }

        private async Task ConnectPowerSupplyAsync(Action<string> log)
        {
            try
            {
                _powerSupplyClient = new TcpClient();
                await _powerSupplyClient.ConnectAsync(PowerSupplyIpAddress, PowerSupplyPort);// 默认 192.168.1.15:30000
                _powerSupplyStream = _powerSupplyClient.GetStream();

                var idn = await QueryPowerSupplyScpiAsync("*IDN?", 5000, CancellationToken.None);
                log($"[{DateTime.Now:HH:mm:ss}] [SIM] 程控电源已连接: {idn}");

                await SendPowerSupplyScpiAsync("SYST:REM", 5000, CancellationToken.None);// 远程控制模式
                await SendPowerSupplyScpiAsync("OUTP OFF,(@1)", 5000, CancellationToken.None);// 关闭输出
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"[SIM] 连接程控电源失败: {PowerSupplyIpAddress}:{PowerSupplyPort}, {ex.Message}", ex);
            }
        }

        private async Task<string> QueryPowerSupplyScpiAsync(string command, int timeoutMs, CancellationToken token)
        {
            await _powerSupplyIoLock.WaitAsync(token);
            try
            {
                await WriteLineAsync(_powerSupplyStream, command, timeoutMs, token);
                return await ReadLineAsync(_powerSupplyStream, timeoutMs, token);
            }
            finally
            {
                _powerSupplyIoLock.Release();
            }
        }

        private async Task SendPowerSupplyScpiAsync(string command, int timeoutMs, CancellationToken token)
        {
            await _powerSupplyIoLock.WaitAsync(token);
            try
            {
                await WriteLineAsync(_powerSupplyStream, command, timeoutMs, token);
            }
            finally
            {
                _powerSupplyIoLock.Release();
            }
        }

        private static async Task WriteLineAsync(NetworkStream stream, string command, int timeoutMs, CancellationToken token)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            cts.CancelAfter(timeoutMs);
            string payload = command.EndsWith("\n", StringComparison.Ordinal) ? command : command + "\n";
            byte[] bytes = Encoding.ASCII.GetBytes(payload);
            await stream.WriteAsync(bytes, 0, bytes.Length, cts.Token);
            await stream.FlushAsync(cts.Token);
        }

        private static async Task<string> ReadLineAsync(NetworkStream stream, int timeoutMs, CancellationToken token)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            cts.CancelAfter(timeoutMs);

            var buffer = new byte[1];
            var sb = new StringBuilder();

            while (true)
            {
                int read = await stream.ReadAsync(buffer, 0, 1, cts.Token);
                if (read <= 0)
                    throw new InvalidOperationException("Connection closed by remote host.");

                char ch = (char)buffer[0];
                if (ch == '\n')
                    break;
                if (ch != '\r')
                    sb.Append(ch);
            }

            return sb.ToString().Trim();
        }

        private async Task CleanupAsync()
        {
            try
            {
                if (_arincDriver != null)
                {
                    _outputEnabled = false;
                    try
                    {
                        await _arincDriver.StopReceiveAsync(SimProductRxChannelIndex);
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

            try
            {
                _powerSupplyStream?.Dispose();
            }
            catch
            {
            }
            finally
            {
                _powerSupplyStream = null;
            }

            try
            {
                _powerSupplyClient?.Close();
            }
            catch
            {
            }
            finally
            {
                _powerSupplyClient = null;
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

            _powerSupplyIoLock.Dispose();
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
                for (int i = 0; i < 4; i++)
                {
                    cmd8[i * 2] = (byte)((_parts[i] >> 8) & 0xFF);
                    cmd8[i * 2 + 1] = (byte)(_parts[i] & 0xFF);
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
            private readonly byte[] _labels4;
            private readonly ushort[] _parts = new ushort[4];
            private int _mask;
            private DateTime _firstSeenUtc;

            private static readonly TimeSpan AssemblyTimeout = TimeSpan.FromMilliseconds(200);

            public MultiLabelCommandAssembler(byte[] labels4)
            {
                if (labels4 == null || labels4.Length != 4)
                    throw new ArgumentException("labels4必须为4字节", nameof(labels4));
                _labels4 = labels4.ToArray();
            }

            public bool TryAddFragment(byte label, ushort payload16, DateTime nowUtc, out byte[] cmd8)
            {
                cmd8 = null;

                int fragIndex = Array.IndexOf(_labels4, label);
                if (fragIndex < 0)
                    return false;

                if (_mask == 0)
                {
                    _firstSeenUtc = nowUtc;
                }
                else if ((nowUtc - _firstSeenUtc) > AssemblyTimeout)
                {
                    _mask = 0;
                    _firstSeenUtc = nowUtc;
                }

                _parts[fragIndex] = payload16;
                _mask |= (1 << fragIndex);

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

            public void Reset()
            {
                _mask = 0;
                _firstSeenUtc = default;
            }
        }

        private static bool TryBuildDsiResponse(byte[] cmd8, GndOcState inputMode, out byte[] resp8)
        {
            resp8 = null;
            if (cmd8 == null || cmd8.Length != 8)
                return false;

            if (cmd8[0] != 0x08 || cmd8[1] != 0x01)
                return false;

            // 测试指令：0x08 0x01 <seq> 0x01 00 00 00 00
            if (cmd8[3] != 0x01)
                return false;

            byte seq = cmd8[2];
            if (seq <= 0 || seq > DsiChannels.Length)
                return false;

            int channelNumber = DsiChannels[seq - 1];
            bool isGnd;
            if (inputMode == GndOcState.Gnd)
            {
                isGnd = channelNumber != 28;
            }
            else
            {
                isGnd = channelNumber == 29;
            }

            resp8 = new byte[8];
            resp8[0] = cmd8[0];
            resp8[1] = cmd8[1];
            resp8[2] = cmd8[2];
            // 回包：采集离散值并上传
            resp8[3] = 0x02;
            // value32: 0=GND, 1=OC
            resp8[4] = 0x00;
            resp8[5] = 0x00;
            resp8[6] = 0x00;
            resp8[7] = isGnd ? (byte)0x00 : (byte)0x01;
            return true;
        }
    }

    public enum GndOcState
    {
        Gnd = 0,
        Oc = 1
    }
}
