using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MeasureControl.ViewModels.SingleBoardTest.FuelController
{
    /// <summary>
    /// RS422串口通信客户端，支持异步接收功能
    /// 用于与产品通过422串口进行通信（COM14/COM15）
    /// </summary>
    internal sealed class Rs422SerialClient : IDisposable
    {
        public const string DefaultPortName1 = "COM14"; // 第1路422串口
        public const string DefaultPortName2 = "COM15"; // 第2路422串口
        public const int DefaultBaudRate = 115200;
        public const Parity DefaultParity = Parity.Odd;
        public const int DefaultDataBits = 8;
        public const StopBits DefaultStopBits = StopBits.One;

        private readonly string _portName;
        private readonly int _baudRate;
        private readonly Parity _parity;
        private readonly int _dataBits;
        private readonly StopBits _stopBits;

        private SerialPort _serialPort;
        private readonly object _lock = new object();

        // 异步接收相关字段
        private CancellationTokenSource _asyncReceiveCts;
        private Task _asyncReceiveTask;
        private readonly object _receivedDataLock = new object();
        private readonly List<ReceivedSerialData> _receivedDataList = new List<ReceivedSerialData>();
        private DateTime _lastClearTime = DateTime.UtcNow;
        private Action<string> _asyncReceiveLogger;
        private const int AsyncReceiveClearIntervalMs = 10000; // 10秒清理一次缓存

        /// <summary>异步接收是否正在运行</summary>
        public bool IsAsyncReceiveRunning => _asyncReceiveTask != null && !_asyncReceiveTask.IsCompleted;

        /// <summary>串口是否已打开</summary>
        public bool IsOpen => _serialPort?.IsOpen == true;

        /// <summary>串口名称</summary>
        public string PortName => _portName;

        public Rs422SerialClient(string portName, int baudRate = DefaultBaudRate, 
            Parity parity = DefaultParity, int dataBits = DefaultDataBits, StopBits stopBits = DefaultStopBits)
        {
            _portName = portName;
            _baudRate = baudRate;
            _parity = parity;
            _dataBits = dataBits;
            _stopBits = stopBits;
        }

        /// <summary>
        /// 打开串口连接
        /// </summary>
        public void Open()
        {
            if (IsOpen) return;

            lock (_lock)
            {
                if (IsOpen) return;

                try { _serialPort?.Dispose(); } catch { }

                _serialPort = new SerialPort(_portName, _baudRate, _parity, _dataBits, _stopBits)
                {
                    ReadTimeout = 1000,
                    WriteTimeout = 1000,
                    ReadBufferSize = 4096,
                    WriteBufferSize = 4096
                };

                _serialPort.Open();
                System.Diagnostics.Debug.WriteLine($"[Rs422SerialClient] 串口 {_portName} 已打开");
            }
        }

        /// <summary>
        /// 关闭串口连接
        /// </summary>
        public void Close()
        {
            StopAsyncReceive();

            lock (_lock)
            {
                try
                {
                    if (_serialPort?.IsOpen == true)
                    {
                        _serialPort.Close();
                    }
                }
                catch { }
                finally
                {
                    try { _serialPort?.Dispose(); } catch { }
                    _serialPort = null;
                }
            }
            System.Diagnostics.Debug.WriteLine($"[Rs422SerialClient] 串口 {_portName} 已关闭");
        }

        /// <summary>
        /// 发送数据
        /// </summary>
        public async Task SendAsync(byte[] data, CancellationToken token = default)
        {
            if (!IsOpen)
                throw new InvalidOperationException($"串口 {_portName} 未打开");

            if (data == null || data.Length == 0)
                throw new ArgumentException("发送数据不能为空");

            await Task.Run(() =>
            {
                lock (_lock)
                {
                    _serialPort.Write(data, 0, data.Length);
                }
            }, token);

            System.Diagnostics.Debug.WriteLine($"[Rs422SerialClient] {_portName} 发送: {BitConverter.ToString(data).Replace("-", " ")}");
        }

        /// <summary>
        /// 同步接收数据（阻塞等待）
        /// </summary>
        public async Task<byte[]> ReceiveAsync(int expectedLength, int timeoutMs = 2000, CancellationToken token = default)
        {
            if (!IsOpen)
                throw new InvalidOperationException($"串口 {_portName} 未打开");

            var buffer = new byte[expectedLength];
            int totalRead = 0;
            var startTime = DateTime.Now;

            while (totalRead < expectedLength)
            {
                token.ThrowIfCancellationRequested();

                if ((DateTime.Now - startTime).TotalMilliseconds > timeoutMs)
                    throw new TimeoutException($"串口 {_portName} 接收超时");

                int bytesToRead = Math.Min(_serialPort.BytesToRead, expectedLength - totalRead);
                if (bytesToRead > 0)
                {
                    int read = _serialPort.Read(buffer, totalRead, bytesToRead);
                    totalRead += read;
                }
                else
                {
                    await Task.Delay(10, token);
                }
            }

            System.Diagnostics.Debug.WriteLine($"[Rs422SerialClient] {_portName} 接收: {BitConverter.ToString(buffer).Replace("-", " ")}");
            return buffer;
        }

        #region 异步接收功能

        /// <summary>
        /// 启动异步接收任务
        /// </summary>
        public void StartAsyncReceive(Action<string> logger = null)
        {
            if (IsAsyncReceiveRunning) return;

            _asyncReceiveLogger = logger;
            _asyncReceiveCts = new CancellationTokenSource();
            _asyncReceiveTask = Task.Run(() => AsyncReceiveLoop(_asyncReceiveCts.Token));
            System.Diagnostics.Debug.WriteLine($"[Rs422SerialClient] {_portName} 异步接收已启动");
        }

        /// <summary>
        /// 停止异步接收任务
        /// </summary>
        public void StopAsyncReceive()
        {
            try
            {
                _asyncReceiveCts?.Cancel();
                _asyncReceiveTask?.Wait(500);
            }
            catch { }
            finally
            {
                _asyncReceiveCts?.Dispose();
                _asyncReceiveCts = null;
                _asyncReceiveTask = null;
            }
            System.Diagnostics.Debug.WriteLine($"[Rs422SerialClient] {_portName} 异步接收已停止");
        }

        /// <summary>
        /// 异步接收循环
        /// </summary>
        private async Task AsyncReceiveLoop(CancellationToken token)
        {
            var buffer = new byte[256];

            while (!token.IsCancellationRequested)
            {
                try
                {
                    // 定期清理旧数据
                    if ((DateTime.UtcNow - _lastClearTime).TotalMilliseconds > AsyncReceiveClearIntervalMs)
                    {
                        ClearOldReceivedData();
                        _lastClearTime = DateTime.UtcNow;
                    }

                    if (!IsOpen)
                    {
                        await Task.Delay(100, token);
                        continue;
                    }

                    int bytesToRead = _serialPort.BytesToRead;
                    if (bytesToRead <= 0)
                    {
                        await Task.Delay(50, token);
                        continue;
                    }

                    int readCount = Math.Min(bytesToRead, buffer.Length);
                    int actualRead = _serialPort.Read(buffer, 0, readCount);

                    if (actualRead > 0)
                    {
                        var data = new byte[actualRead];
                        Buffer.BlockCopy(buffer, 0, data, 0, actualRead);

                        var receivedData = new ReceivedSerialData
                        {
                            Data = data,
                            ReceivedTime = DateTime.UtcNow,
                            RawHex = BitConverter.ToString(data).Replace("-", " ")
                        };

                        lock (_receivedDataLock)
                        {
                            _receivedDataList.Add(receivedData);
                        }

                        _asyncReceiveLogger?.Invoke($"[{_portName}异步] 收到数据: len={actualRead}, hex={receivedData.RawHex}");
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Rs422SerialClient] {_portName} 异步接收异常: {ex.Message}");
                    await Task.Delay(100, token);
                }
            }
        }

        /// <summary>
        /// 清空缓存的接收数据
        /// </summary>
        public void ClearReceivedData()
        {
            lock (_receivedDataLock)
            {
                int count = _receivedDataList.Count;
                _receivedDataList.Clear();
                if (count > 0)
                    _asyncReceiveLogger?.Invoke($"[{_portName}] 已清理 {count} 条缓存消息");
            }
        }

        /// <summary>
        /// 清理超过10秒的旧数据
        /// </summary>
        private void ClearOldReceivedData()
        {
            lock (_receivedDataLock)
            {
                var cutoff = DateTime.UtcNow.AddMilliseconds(-AsyncReceiveClearIntervalMs);
                int removed = _receivedDataList.RemoveAll(d => d.ReceivedTime < cutoff);
                if (removed > 0)
                    System.Diagnostics.Debug.WriteLine($"[Rs422SerialClient] {_portName} 清理了 {removed} 条过期数据");
            }
        }

        /// <summary>
        /// 获取指定时间之后收到的所有数据
        /// </summary>
        public List<ReceivedSerialData> GetReceivedDataAfter(DateTime afterTime)
        {
            lock (_receivedDataLock)
            {
                return _receivedDataList.Where(d => d.ReceivedTime > afterTime).ToList();
            }
        }

        /// <summary>
        /// 获取最新的接收数据
        /// </summary>
        public ReceivedSerialData GetLatestReceivedData()
        {
            lock (_receivedDataLock)
            {
                return _receivedDataList.LastOrDefault();
            }
        }

        /// <summary>
        /// 获取所有缓存数据的数量
        /// </summary>
        public int ReceivedDataCount
        {
            get
            {
                lock (_receivedDataLock)
                {
                    return _receivedDataList.Count;
                }
            }
        }

        /// <summary>
        /// 等待并获取指定时间后收到的数据
        /// </summary>
        public async Task<byte[]> WaitForDataAfterAsync(DateTime afterTime, int expectedLength, int timeoutMs = 3000, CancellationToken token = default)
        {
            var startTime = DateTime.Now;

            while ((DateTime.Now - startTime).TotalMilliseconds < timeoutMs)
            {
                token.ThrowIfCancellationRequested();

                var dataList = GetReceivedDataAfter(afterTime);
                if (dataList.Count > 0)
                {
                    // 合并所有收到的数据
                    var allData = dataList.SelectMany(d => d.Data).ToArray();
                    if (allData.Length >= expectedLength)
                    {
                        return allData.Take(expectedLength).ToArray();
                    }
                }

                await Task.Delay(50, token);
            }

            return null;
        }

        #endregion

        public void Dispose()
        {
            Close();
        }
    }

    /// <summary>
    /// 接收到的串口数据结构
    /// </summary>
    internal class ReceivedSerialData
    {
        /// <summary>数据内容</summary>
        public byte[] Data { get; set; }

        /// <summary>接收时间（UTC）</summary>
        public DateTime ReceivedTime { get; set; }

        /// <summary>原始十六进制字符串</summary>
        public string RawHex { get; set; }
    }
}
