using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MeasureControl.Drivers;
using MeasureControl.Drivers.ART4229;
using MeasureControl.Models.Devices;

namespace MeasureControl.Simulations.Common
{
    public abstract class ARINC429SimulationBase : IDisposable
    {
        protected ART4229Driver _arincDriver;
        protected readonly SemaphoreSlim _arincIoLock = new SemaphoreSlim(1, 1);

        protected int _benchTxChannelIndex = -1;
        protected int _benchRxChannelIndex = -1;
        protected bool _benchRxStarted;

        protected readonly List<int> _extraBenchTxChannelIndices = new List<int>();
        protected readonly List<int> _extraBenchRxChannelIndices = new List<int>();
        protected readonly HashSet<int> _benchRxStartedIndices = new HashSet<int>();

        protected CancellationTokenSource _simCts;
        protected Task _rxLoopTask;

        protected bool _started;

        public bool EnableFrameLogging { get; set; } = true;

        public bool IsRealProduct { get; set; }
        public double ArincRate { get; set; } = 100000.0;
        public double SimProductArincRate { get; set; } = 100000.0;
        public int SimProductRxChannelIndex { get; set; } = 4;
        public int SimProductTxChannelIndex { get; set; } = 5;
        public int SimProduct2RxChannelIndex { get; set; } = -1;
        public int SimProduct2TxChannelIndex { get; set; } = -1;
        public int ArincDeviceIndex { get; set; } = 0;

        public async Task StartAsync(string benchTxChannel, string benchRxChannel, Action<string> log)
        {
            if (_started) return;
            if (log == null) log = _ => { };

            _simCts = new CancellationTokenSource();

            await OpenArincDeviceAsync(log);

            int benchTxIndex = ParseChannelIndex(benchTxChannel);//429_CH0对应的0/1
            int benchRxIndex = ParseChannelIndex(benchRxChannel);

            ValidateChannelIndices(benchTxIndex, benchRxIndex);

            _benchTxChannelIndex = benchTxIndex;
            _benchRxChannelIndex = benchRxIndex;

            _extraBenchTxChannelIndices.Clear();
            _extraBenchRxChannelIndices.Clear();
            _benchRxStartedIndices.Clear();

            await ConfigureArincChannelsAsync(benchTxIndex, benchRxIndex, log);

            await StartBenchRxAsync(log);

            if (!IsRealProduct)
            {
                await StartSimProductRxAsync(log, _simCts.Token);
            }

            _started = true;
        }

        public async Task StartAsync(string benchTx1, string benchRx1, string benchTx2, string benchRx2, Action<string> log)
        {
            if (_started) return;
            if (log == null) log = _ => { };

            _simCts = new CancellationTokenSource();

            await OpenArincDeviceAsync(log);

            int tx1 = ParseChannelIndex(benchTx1);
            int rx1 = ParseChannelIndex(benchRx1);
            int tx2 = ParseChannelIndex(benchTx2);
            int rx2 = ParseChannelIndex(benchRx2);

            ValidateChannelIndices(new[] { tx1, tx2 }, new[] { rx1, rx2 });

            _benchTxChannelIndex = tx1;
            _benchRxChannelIndex = rx1;

            _extraBenchTxChannelIndices.Clear();
            _extraBenchRxChannelIndices.Clear();
            _benchRxStartedIndices.Clear();

            _extraBenchTxChannelIndices.Add(tx2);
            _extraBenchRxChannelIndices.Add(rx2);

            await ConfigureArincChannelsAsyncMulti(new[] { tx1, tx2 }, new[] { rx1, rx2 }, log);

            await StartBenchRxForIndexAsync(rx1, log);
            await StartBenchRxForIndexAsync(rx2, log);

            if (!IsRealProduct)
            {
                await StartSimProductRxAsync(log, _simCts.Token);
            }

            _started = true;
        }

        public async Task StopAsync(Action<string> log)
        {
            if (log == null) log = _ => { };

            if (!_started)
            {
                await CleanupAsync();
                return;
            }

            _started = false;

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

            await CleanupAsync();
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

        protected virtual void ValidateChannelIndices(int benchTxIndex, int benchRxIndex)
        {
            ValidateChannelIndices(new[] { benchTxIndex }, new[] { benchRxIndex });
        }

        protected virtual void ValidateChannelIndices(IReadOnlyList<int> benchTxIndices, IReadOnlyList<int> benchRxIndices)
        {
            if (benchTxIndices == null || benchRxIndices == null)
                throw new InvalidOperationException("[SIM] 通道索引为空");
            if (benchTxIndices.Count != benchRxIndices.Count)
                throw new InvalidOperationException("[SIM] bench TX/RX 数量不一致");
            if (benchTxIndices.Count == 0)
                throw new InvalidOperationException("[SIM] bench TX/RX 不能为空");

            var allBench = new HashSet<int>();

            for (int i = 0; i < benchTxIndices.Count; i++)
            {
                int tx = benchTxIndices[i];
                int rx = benchRxIndices[i];
                if (tx < 0 || rx < 0)
                    throw new InvalidOperationException($"[SIM] 通道索引无效：TX={tx}, RX={rx}");
                if (tx == rx)
                    throw new InvalidOperationException($"[SIM] bench TX/RX 通道冲突：TX={tx}, RX={rx}");

                if (!allBench.Add(tx))
                    throw new InvalidOperationException($"[SIM] bench通道重复：{tx}");
                if (!allBench.Add(rx))
                    throw new InvalidOperationException($"[SIM] bench通道重复：{rx}");
            }

            var simChannels = new HashSet<int>();
            if (!IsRealProduct)
            {
                simChannels.Add(SimProductRxChannelIndex);
                simChannels.Add(SimProductTxChannelIndex);
                if (SimProduct2RxChannelIndex >= 0) simChannels.Add(SimProduct2RxChannelIndex);
                if (SimProduct2TxChannelIndex >= 0) simChannels.Add(SimProduct2TxChannelIndex);
            }

            if (simChannels.Count > 0)
            {
                foreach (var b in allBench)
                {
                    if (simChannels.Contains(b))
                    {
                        throw new InvalidOperationException($"[SIM] bench与产品侧通道冲突：bench={b}, sim={string.Join(",", simChannels.OrderBy(x => x))}");
                    }
                }
            }
        }

        protected abstract Task StartSimProductRxAsync(Action<string> log, CancellationToken token);

        protected async Task StartBenchRxAsync(Action<string> log)
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

        protected async Task StartBenchRxForIndexAsync(int rxIndex, Action<string> log)
        {
            if (_arincDriver == null)
                throw new InvalidOperationException("ARINC driver not initialized");
            if (rxIndex < 0)
                return;
            if (_benchRxStartedIndices.Contains(rxIndex))
                return;

            bool ok = await _arincDriver.StartReceiveAsync(rxIndex);
            if (!ok)
                throw new InvalidOperationException($"[SIM] 启动bench接收失败: RX={rxIndex}");

            _benchRxStartedIndices.Add(rxIndex);
            log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] bench侧接收已启动: benchRX={rxIndex}");
        }

        protected async Task SendTwoFrameOnChannelAsync(int txChannelIndex, byte label0, byte label1, byte[] data4, Action<string> log, CancellationToken token)
        {
            if (_arincDriver == null)
                return;
            if (data4 == null || data4.Length != 4)
                return;

            ushort part0 = (ushort)((data4[0] << 8) | data4[1]);
            ushort part1 = (ushort)((data4[2] << 8) | data4[3]);

            uint[] data429 = new uint[2];
            uint[] parity = new uint[2];

            data429[0] = ApplyParity(BuildWord(label0, 0, part0));
            parity[0] = 1;
            data429[1] = ApplyParity(BuildWord(label1, 0, part1));
            parity[1] = 1;

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
                log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] 4字节发送失败: tx={txChannelIndex}");
            }
            else if (EnableFrameLogging)
            {
                log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] tx={txChannelIndex} send labels=0x{label0:X2}/0x{label1:X2} data4={FormatBytes(data4)}");
            }
        }

        protected async Task SendMultiLabelFrameOnChannelAsync(int txChannelIndex, byte[] fragLabels, byte[] payload8, Action<string> log, CancellationToken token)
        {
            if (_arincDriver == null)
                return;
            if (payload8 == null || payload8.Length != 8)
                return;
            if (fragLabels == null || fragLabels.Length < 4)
                return;

            uint[] data429 = new uint[4];
            uint[] parity = new uint[4];

            for (byte frag = 0; frag < 4; frag++)
            {
                ushort part = (ushort)((payload8[frag * 2] << 8) | payload8[frag * 2 + 1]);
                byte fragLabel = fragLabels[frag];
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
                log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] 8字节发送失败: tx={txChannelIndex}");
            }
            else if (EnableFrameLogging)
            {
                log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] tx={txChannelIndex} send labels={string.Join("/", fragLabels.Select(b => $"0x{b:X2}"))} payload8={FormatBytes(payload8)}");
            }
        }

        protected async Task OpenArincDeviceAsync(Action<string> log)
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

        protected async Task ConfigureArincChannelsAsync(int benchTxIndex, int benchRxIndex, Action<string> log)
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

            bool openSim2Rx = true;
            bool openSim2Tx = true;
            if (!IsRealProduct)
            {
                if (SimProduct2RxChannelIndex >= 0)
                    openSim2Rx = await _arincDriver.OpenRxChannelAsync(SimProduct2RxChannelIndex);
                if (SimProduct2TxChannelIndex >= 0)
                    openSim2Tx = await _arincDriver.OpenTxChannelAsync(SimProduct2TxChannelIndex);
            }

            if (!openBenchTx || !openBenchRx || !openSimRx || !openSimTx || !openSim2Rx || !openSim2Tx)
                throw new InvalidOperationException($"[SIM] Open通道失败: benchTX={benchTxIndex}({openBenchTx}), benchRX={benchRxIndex}({openBenchRx}), simRX={SimProductRxChannelIndex}({openSimRx}), simTX={SimProductTxChannelIndex}({openSimTx}), sim2RX={SimProduct2RxChannelIndex}({openSim2Rx}), sim2TX={SimProduct2TxChannelIndex}({openSim2Tx})");

            await _arincDriver.ConfigureTxChannelAsync(benchTxIndex, benchTxRate, sendMode: 0, parity: parity, wordFormat: wordFormat);
            await _arincDriver.ConfigureRxChannelAsync(benchRxIndex, benchRxRate, parity: parity, wordFormat: wordFormat,
                enableInterrupt: false, interruptDepth: 0, enableTimeTag: false);

            if (!IsRealProduct)
            {
                await _arincDriver.ConfigureRxChannelAsync(SimProductRxChannelIndex, simRxRate, parity: parity, wordFormat: wordFormat,
                    enableInterrupt: false, interruptDepth: 0, enableTimeTag: false);
                await _arincDriver.ConfigureTxChannelAsync(SimProductTxChannelIndex, simTxRate, sendMode: 0, parity: parity, wordFormat: wordFormat);

                if (SimProduct2RxChannelIndex >= 0)
                {
                    await _arincDriver.ConfigureRxChannelAsync(SimProduct2RxChannelIndex, simRxRate, parity: parity, wordFormat: wordFormat,
                        enableInterrupt: false, interruptDepth: 0, enableTimeTag: false);
                }
                if (SimProduct2TxChannelIndex >= 0)
                {
                    await _arincDriver.ConfigureTxChannelAsync(SimProduct2TxChannelIndex, simTxRate, sendMode: 0, parity: parity, wordFormat: wordFormat);
                }
            }

            log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] ARINC429 通道已配置: benchTX={benchTxIndex}, benchRX={benchRxIndex}, simRX={SimProductRxChannelIndex}, simTX={SimProductTxChannelIndex}");
        }//当 IsRealProduct == true 时，打开配置一对接收/发送通道

        protected async Task ConfigureArincChannelsAsyncMulti(IReadOnlyList<int> benchTxIndices, IReadOnlyList<int> benchRxIndices, Action<string> log)
        {
            const int wordFormat = 0;
            const int parity = 1;
            double benchTxRate = ArincRate;
            double benchRxRate = ArincRate;
            double simTxRate = SimProductArincRate;
            double simRxRate = SimProductArincRate;

            for (int i = 0; i < benchTxIndices.Count; i++)
            {
                int tx = benchTxIndices[i];
                int rx = benchRxIndices[i];

                bool openTx = await _arincDriver.OpenTxChannelAsync(tx);
                bool openRx = await _arincDriver.OpenRxChannelAsync(rx);
                if (!openTx || !openRx)
                    throw new InvalidOperationException($"[SIM] Open通道失败: benchTX={tx}({openTx}), benchRX={rx}({openRx})");

                await _arincDriver.ConfigureTxChannelAsync(tx, benchTxRate, sendMode: 0, parity: parity, wordFormat: wordFormat);
                await _arincDriver.ConfigureRxChannelAsync(rx, benchRxRate, parity: parity, wordFormat: wordFormat,
                    enableInterrupt: false, interruptDepth: 0, enableTimeTag: false);
            }

            if (!IsRealProduct)
            {
                bool openSimRx = await _arincDriver.OpenRxChannelAsync(SimProductRxChannelIndex);
                bool openSimTx = await _arincDriver.OpenTxChannelAsync(SimProductTxChannelIndex);

                bool openSim2Rx = true;
                bool openSim2Tx = true;
                if (SimProduct2RxChannelIndex >= 0)
                    openSim2Rx = await _arincDriver.OpenRxChannelAsync(SimProduct2RxChannelIndex);
                if (SimProduct2TxChannelIndex >= 0)
                    openSim2Tx = await _arincDriver.OpenTxChannelAsync(SimProduct2TxChannelIndex);

                if (!openSimRx || !openSimTx || !openSim2Rx || !openSim2Tx)
                    throw new InvalidOperationException($"[SIM] Open通道失败: simRX={SimProductRxChannelIndex}({openSimRx}), simTX={SimProductTxChannelIndex}({openSimTx}), sim2RX={SimProduct2RxChannelIndex}({openSim2Rx}), sim2TX={SimProduct2TxChannelIndex}({openSim2Tx})");

                await _arincDriver.ConfigureRxChannelAsync(SimProductRxChannelIndex, simRxRate, parity: parity, wordFormat: wordFormat,
                    enableInterrupt: false, interruptDepth: 0, enableTimeTag: false);
                await _arincDriver.ConfigureTxChannelAsync(SimProductTxChannelIndex, simTxRate, sendMode: 0, parity: parity, wordFormat: wordFormat);

                if (SimProduct2RxChannelIndex >= 0)
                {
                    await _arincDriver.ConfigureRxChannelAsync(SimProduct2RxChannelIndex, simRxRate, parity: parity, wordFormat: wordFormat,
                        enableInterrupt: false, interruptDepth: 0, enableTimeTag: false);
                }
                if (SimProduct2TxChannelIndex >= 0)
                {
                    await _arincDriver.ConfigureTxChannelAsync(SimProduct2TxChannelIndex, simTxRate, sendMode: 0, parity: parity, wordFormat: wordFormat);
                }
            }

            log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SIM] ARINC429 通道已配置: benchPairs={benchTxIndices.Count}, simRX={SimProductRxChannelIndex}, simTX={SimProductTxChannelIndex}, sim2RX={SimProduct2RxChannelIndex}, sim2TX={SimProduct2TxChannelIndex}");
        }

        protected virtual void OnAfterCleanup()
        {
        }

        protected async Task CleanupAsync()
        {
            try
            {
                if (_arincDriver != null)
                {
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

                    if (SimProduct2RxChannelIndex >= 0)
                    {
                        try
                        {
                            if (!IsRealProduct)
                            {
                                await _arincDriver.StopReceiveAsync(SimProduct2RxChannelIndex);
                            }
                        }
                        catch
                        {
                        }
                    }

                    try
                    {
                        if (_benchRxChannelIndex >= 0)
                            await _arincDriver.StopReceiveAsync(_benchRxChannelIndex);
                    }
                    catch
                    {
                    }

                    if (_extraBenchRxChannelIndices.Count > 0)
                    {
                        foreach (var rx in _extraBenchRxChannelIndices.Distinct())
                        {
                            try
                            {
                                await _arincDriver.StopReceiveAsync(rx);
                            }
                            catch
                            {
                            }
                        }
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
                _benchRxStarted = false;
                _benchRxStartedIndices.Clear();
                _benchTxChannelIndex = -1;
                _benchRxChannelIndex = -1;
                _extraBenchTxChannelIndices.Clear();
                _extraBenchRxChannelIndices.Clear();
                OnAfterCleanup();
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

        protected static bool TryParseWord(uint raw, out byte label, out byte sdi, out ushort payload16)
        {
            label = (byte)(raw & 0xFF);
            sdi = (byte)((raw >> 8) & 0x3);
            uint data19 = (raw >> 10) & 0x1FFFF;
            payload16 = (ushort)(data19 & 0xFFFF);
            return true;
        }

        protected static uint BuildWord(byte label, byte sdi, ushort payload16)
        {
            uint word = 0;
            word |= label;
            word |= (uint)(sdi & 0x3) << 8;
            word |= (uint)payload16 << 10;
            return word;
        }

        protected static uint ApplyParity(uint word)
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

        protected static string FormatBytes(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                return string.Empty;
            return string.Join(" ", bytes.Select(b => b.ToString("X2")));
        }
    }
}
