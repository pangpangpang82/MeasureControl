using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace MeasureControl.ViewModels.SingleBoardTest.FuelController
{
    /// <summary>
    /// 加放油高速 IO 板卡（FPGA）TCP 通信客户端。
    /// FPGA端TCP服务器，静态IP: 192.168.1.10，端口: 5001。
    /// 帧格式： 帧头(0xAA,0x55) + 长度(1B) + 命令(1B) + 数据(长度-1 B)
    /// </summary>
    internal sealed class FpgaIoClient : IDisposable
    {
        public const string DefaultIpAddress = "192.168.1.10";
        public const int DefaultPort = 5001;
        private const int AsyncReceiveClearIntervalMs = 10000; // 10秒清理一次缓存

        private static readonly byte[] FrameHeader = { 0xAA, 0x55 };

        /// <summary>
        /// FPGA初始化状态：1=需要初始化，0=已初始化无需再次初始化
        /// 首次连接成功时发送命令0x04初始化FPGA，初始化后延迟500ms后置为0
        /// </summary>
        private static int _fpgaInitRequired = 1;

        /// <summary>获取或设置FPGA是否需要初始化</summary>
        public static bool FpgaInitRequired
        {
            get => Interlocked.CompareExchange(ref _fpgaInitRequired, 0, 0) == 1;
            set => Interlocked.Exchange(ref _fpgaInitRequired, value ? 1 : 0);
        }

        private readonly string _ip;
        private readonly int _port;
        private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);

        private TcpClient _client;
        private NetworkStream _stream;

        // 异步接收相关字段
        private CancellationTokenSource _asyncReceiveCts;
        private Task _asyncReceiveTask;
        private readonly object _receivedFramesLock = new object();
        private readonly List<ReceivedFrame> _receivedFrames = new List<ReceivedFrame>();
        private DateTime _lastClearTime = DateTime.UtcNow;
        private Action<string> _asyncReceiveLogger;

        /// <summary>异步接收是否正在运行</summary>
        public bool IsAsyncReceiveRunning => _asyncReceiveTask != null && !_asyncReceiveTask.IsCompleted;

        public bool IsConnected => _client?.Connected == true && _stream != null;

        public FpgaIoClient(string ip = DefaultIpAddress, int port = DefaultPort)
        {
            _ip = ip;
            _port = port;
        }

        public async Task ConnectAsync(CancellationToken token = default)
        {
            if (IsConnected) return;

            try { _client?.Dispose(); } catch { }
            _client = null;
            _stream = null;

            var client = new TcpClient { NoDelay = true };
            try
            {
                using var timeoutCts = new System.Threading.CancellationTokenSource(2000);
                using var linkedCts = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCts.Token);

                var connectTask = client.ConnectAsync(_ip, _port);
                var cancelTask = Task.Delay(Timeout.Infinite, linkedCts.Token);

                var completed = await Task.WhenAny(connectTask, cancelTask);
                if (completed != connectTask)
                {
                    try { client.Close(); } catch { }
                    token.ThrowIfCancellationRequested();
                    throw new TimeoutException($"FPGA连接超时（2s），IP={_ip}:{_port}");
                }

                await connectTask;

                _client = client;
                _stream = _client.GetStream();

                // 首次连接成功时发送初始化命令0x04
                if (FpgaInitRequired)
                {
                    await SendInitCommandAsync(token);
                }
            }
            catch
            {
                try { client?.Close(); } catch { }
                throw;
            }
        }

        /// <summary>
        /// 发送FPGA初始化命令（0x04），初始化后延迟500ms后将FpgaInitRequired置为false
        /// 帧格式: AA 55 01 04
        /// </summary>
        private async Task SendInitCommandAsync(CancellationToken token)
        {
            try
            {
                var frame = BuildFrame(0x04);
                await _stream.WriteAsync(frame, 0, frame.Length, token);
                await _stream.FlushAsync(token);
                System.Diagnostics.Debug.WriteLine($"[FpgaIoClient] 发送FPGA初始化命令: {BitConverter.ToString(frame).Replace("-", " ")}");

                // 延迟500ms后将初始化标志置为0
                await Task.Delay(500, token);
                FpgaInitRequired = false;
                System.Diagnostics.Debug.WriteLine("[FpgaIoClient] FPGA初始化完成，后续连接无需再初始化");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FpgaIoClient] FPGA初始化命令发送失败: {ex.Message}");
            }
        }

        public void Disconnect()
        {
            try { _stream?.Close(); } catch { }
            try { _client?.Close(); } catch { }
            _stream = null;
            _client = null;
        }

        private static byte[] BuildFrame(byte command, byte[] data = null)
        {
            int dataLen = data?.Length ?? 0;
            byte lengthField = (byte)(1 + dataLen);
            var frame = new byte[2 + 1 + 1 + dataLen];
            frame[0] = FrameHeader[0];
            frame[1] = FrameHeader[1];
            frame[2] = lengthField;
            frame[3] = command;
            if (dataLen > 0)
                Buffer.BlockCopy(data, 0, frame, 4, dataLen);
            return frame;
        }

        private async Task<(byte cmd, byte[] payload)> ReadFrameAsync(CancellationToken token)
        {
            var header = await ReadExactAsync(2, token);
            if (header[0] != 0xAA || header[1] != 0x55)
                throw new InvalidOperationException($"FPGA帧头校验失败: 0x{header[0]:X2} 0x{header[1]:X2}");

            var lenBuf = await ReadExactAsync(1, token);
            int totalLen = lenBuf[0];

            var body = await ReadExactAsync(totalLen, token);
            byte cmd = body[0];
            byte[] payload = new byte[totalLen - 1];
            if (payload.Length > 0)
                Buffer.BlockCopy(body, 1, payload, 0, payload.Length);

            return (cmd, payload);
        }

        private async Task<byte[]> ReadExactAsync(int count, CancellationToken token)
        {
            var buf = new byte[count];
            int received = 0;
            while (received < count)
            {
                int n = await _stream.ReadAsync(buf, received, count - received, token);
                if (n == 0) throw new InvalidOperationException("FPGA连接已断开（读取0字节）");
                received += n;
            }
            return buf;
        }

        /// <summary>
        /// 发送温度采集命令（0x07），不等待响应。
        /// 帧格式: AA 55 02 07 00
        /// 配合异步接收使用，响应会被异步接收任务捕获。
        /// </summary>
        public async Task SendTemperatureCommandAsync(CancellationToken token = default)
        {
            await _lock.WaitAsync(token);
            try
            {
                await EnsureConnectedAsync(token);
                // 帧格式: AA 55 02 07 00 (长度=2, 命令=07, 数据=00)
                var frame = BuildFrame(0x07, new byte[] { 0x00 });
                await _stream.WriteAsync(frame, 0, frame.Length, token);
                await _stream.FlushAsync(token);
                System.Diagnostics.Debug.WriteLine($"[FpgaIoClient] 发送温度采集命令: {BitConverter.ToString(frame).Replace("-", " ")}");
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>0x07 读取DS18B20温度，返回单精度浮点数（小端，单位℃）</summary>
        public async Task<float> ReadDs18B20TemperatureAsync(CancellationToken token = default)
        {
            await _lock.WaitAsync(token);
            try
            {
                await EnsureConnectedAsync(token);
                
                // 先清空接收缓冲区，防止残留帧干扰
                await FlushReceiveBufferAsync();
                
                var frame = BuildFrame(0x07);
                await _stream.WriteAsync(frame, 0, frame.Length, token);
                await _stream.FlushAsync(token);

                // 读取响应帧，可能需要跳过非0x07的帧（如残留的0x00帧）
                int maxRetries = 3;
                for (int i = 0; i < maxRetries; i++)
                {
                    var (cmd, payload) = await ReadFrameAsync(token);
                    if (cmd == 0x07)
                    {
                        if (payload.Length < 4)
                            throw new InvalidOperationException($"DS18B20温度读取：应答数据长度不足 {payload.Length} bytes，期望 4");
                        return BitConverter.ToSingle(payload, 0);
                    }
                    System.Diagnostics.Debug.WriteLine($"[FpgaIoClient] DS18B20 跳过非预期帧: cmd=0x{cmd:X2}");
                }
                throw new InvalidOperationException($"DS18B20温度读取：连续{maxRetries}帧均非0x07命令");
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>0x00 写GPIO输出 (IO11-32 对应 bit0-21，uint32小端)，并消费FPGA返回的0x00响应帧</summary>
        public async Task WriteGpioAsync(uint ioMask, CancellationToken token = default)
        {
            await _lock.WaitAsync(token);
            try
            {
                await EnsureConnectedAsync(token);
                
                // 先清空接收缓冲区，防止残留帧干扰
                await FlushReceiveBufferAsync();
                
                var frame = BuildFrame(0x00, BitConverter.GetBytes(ioMask));
                await _stream.WriteAsync(frame, 0, frame.Length, token);
                await _stream.FlushAsync(token);
                
                // 协议：发送0x00后FPGA会返回一个0x00帧(GPIO输入读值)，必须消费否则后续帧错位
                try
                {
                    using var ackCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                    ackCts.CancelAfter(200);
                    var (cmd, _) = await ReadFrameAsync(ackCts.Token);
                    System.Diagnostics.Debug.WriteLine($"[FpgaIoClient] WriteGpio 应答: cmd=0x{cmd:X2}");
                }
                catch (OperationCanceledException)
                {
                    System.Diagnostics.Debug.WriteLine("[FpgaIoClient] WriteGpio 无应答帧（超时）");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[FpgaIoClient] WriteGpio 应答读取异常: {ex.Message}");
                }
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>0x00 读GPIO输入 (IO43-64 对应 bit0-21，uint32小端)</summary>
        public async Task<uint> ReadGpioAsync(CancellationToken token = default)
        {
            await _lock.WaitAsync(token);
            try
            {
                await EnsureConnectedAsync(token);
                var frame = BuildFrame(0x00, new byte[] { 0, 0, 0, 0 });
                await _stream.WriteAsync(frame, 0, frame.Length, token);
                await _stream.FlushAsync(token);

                var (cmd, payload) = await ReadFrameAsync(token);
                if (cmd != 0x00)
                    throw new InvalidOperationException($"GPIO读取：应答命令错误 0x{cmd:X2}，期望 0x00");
                if (payload.Length < 4)
                    throw new InvalidOperationException($"GPIO读取：应答数据长度不足 {payload.Length} bytes");

                return BitConverter.ToUInt32(payload, 0);
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>0x04 初始化HI8435，等待FPGA应答帧</summary>
        public async Task InitHi8435Async(CancellationToken token = default)
        {
            await _lock.WaitAsync(token);
            try
            {
                await EnsureConnectedAsync(token);
                
                // 先清空接收缓冲区，防止残留帧干扰
                await FlushReceiveBufferAsync();
                
                var frame = BuildFrame(0x04);
                await _stream.WriteAsync(frame, 0, frame.Length, token);
                await _stream.FlushAsync(token);
                
                // 消费FPGA应答帧（若有），防止后续帧错位
                // 缩短超时时间到100ms，避免初始化过慢
                try
                {
                    using var ackCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                    ackCts.CancelAfter(100);
                    //var (cmd, _) = await ReadFrameAsync(ackCts.Token);
                    //System.Diagnostics.Debug.WriteLine($"[FpgaIoClient] InitHi8435 应答: cmd=0x{cmd:X2}");
                }
                catch (OperationCanceledException) 
                { 
                    System.Diagnostics.Debug.WriteLine("[FpgaIoClient] InitHi8435 无应答帧（超时）");
                }
                catch (Exception ex)
                { 
                    System.Diagnostics.Debug.WriteLine($"[FpgaIoClient] InitHi8435 应答读取异常: {ex.Message}");
                }
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>0x06 读HI8435 BANK3-0状态，返回4字节 byte0-3对应bank3-0</summary>
        public async Task<byte[]> ReadHi8435Async(CancellationToken token = default)
        {
            await _lock.WaitAsync(token);
            try
            {
                await EnsureConnectedAsync(token);
                var frame = BuildFrame(0x06);
                await _stream.WriteAsync(frame, 0, frame.Length, token);
                await _stream.FlushAsync(token);

                var (cmd, payload) = await ReadFrameAsync(token);
                if (cmd != 0x06)
                    throw new InvalidOperationException($"HI8435读取：应答命令错误 0x{cmd:X2}，期望 0x06");
                if (payload.Length < 4)
                    throw new InvalidOperationException($"HI8435读取：应答数据长度不足 {payload.Length} bytes");

                return payload;
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>
        /// 发送HI8435读取命令（0x06），不等待响应。
        /// 帧格式: AA 55 02 06 00
        /// 配合异步接收使用，响应会被异步接收任务捕获。
        /// </summary>
        public async Task SendReadHi8435CommandAsync(CancellationToken token = default)
        {
            await _lock.WaitAsync(token);
            try
            {
                await EnsureConnectedAsync(token);
                var frame = BuildFrame(0x06, new byte[] { 0x00 });
                await _stream.WriteAsync(frame, 0, frame.Length, token);
                await _stream.FlushAsync(token);
                System.Diagnostics.Debug.WriteLine($"[FpgaIoClient] 发送HI8435读取命令: {BitConverter.ToString(frame).Replace("-", " ")}");
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>
        /// 0x01/02/03 UART TX+RX 一体：发送数据后等待FPGA回传回环数据。
        /// 用于自检（RS422内部回环）：TX发出后FPGA将收到的回环数据作为同命令帧返回。
        /// uartIndex: 0=SCI1(UART0), 1=SCI2(UART1), 2=UART2
        /// </summary>
        public async Task<byte[]> UartTxRxAsync(int uartIndex, byte[] data, CancellationToken token = default)
        {
            if (uartIndex < 0 || uartIndex > 2) throw new ArgumentOutOfRangeException(nameof(uartIndex));
            if (data == null || data.Length == 0 || data.Length > 201) throw new ArgumentException("数据长度需在1~201字节内");

            await _lock.WaitAsync(token);
            try
            {
                await EnsureConnectedAsync(token);
                var frame = BuildFrame((byte)(0x01 + uartIndex), data);
                await _stream.WriteAsync(frame, 0, frame.Length, token);
                await _stream.FlushAsync(token);

                byte expectedCmd = (byte)(0x01 + uartIndex);
                var (cmd, payload) = await ReadFrameAsync(token);
                if (cmd != expectedCmd)
                    throw new InvalidOperationException($"UART{uartIndex} TX/RX：应答命令错误 0x{cmd:X2}，期望 0x{expectedCmd:X2}");
                return payload;
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>
        /// 0x01/02/03 仅发送 UART TX（外部通信模式，不等待回环应答）。
        /// 发送后FPGA不会立即返回帧，外部设备收到数据后可能发回数据由 UartRxWaitAsync 接收。
        /// </summary>
        public async Task UartTxOnlyAsync(int uartIndex, byte[] data, CancellationToken token = default)
        {
            if (uartIndex < 0 || uartIndex > 2) throw new ArgumentOutOfRangeException(nameof(uartIndex));
            if (data == null || data.Length == 0 || data.Length > 201) throw new ArgumentException("数据长度需在1~201字节内");

            await _lock.WaitAsync(token);
            try
            {
                await EnsureConnectedAsync(token);
                var frame = BuildFrame((byte)(0x01 + uartIndex), data);
                await _stream.WriteAsync(frame, 0, frame.Length, token);
                await _stream.FlushAsync(token);
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>
        /// 等待并接收 UART RX 帧（外部设备主动发回的数据）。
        /// uartIndex: 0=SCI1, 1=SCI2, 2=UART2
        /// </summary>
        public async Task<byte[]> UartRxWaitAsync(int uartIndex, CancellationToken token = default)
        {
            if (uartIndex < 0 || uartIndex > 2) throw new ArgumentOutOfRangeException(nameof(uartIndex));

            await _lock.WaitAsync(token);
            try
            {
                await EnsureConnectedAsync(token);
                byte expectedCmd = (byte)(0x01 + uartIndex);
                var (cmd, payload) = await ReadFrameAsync(token);
                if (cmd != expectedCmd)
                    throw new InvalidOperationException($"UART{uartIndex} RX等待：应答命令错误 0x{cmd:X2}，期望 0x{expectedCmd:X2}");
                return payload;
            }
            finally
            {
                _lock.Release();
            }
        }

        private async Task EnsureConnectedAsync(CancellationToken token)
        {
            if (!IsConnected)
                await ConnectAsync(token);
        }

        /// <summary>
        /// 清空接收缓冲区，防止残留帧干扰后续通信
        /// </summary>
        private async Task FlushReceiveBufferAsync()
        {
            if (_stream == null || !_client.Connected)
                return;

            try
            {
                // 设置短超时，快速检查是否有残留数据
                _client.ReceiveTimeout = 10;
                var buffer = new byte[256];
                int totalFlushed = 0;
                
                while (_stream.DataAvailable && totalFlushed < 1024)
                {
                    int n = await _stream.ReadAsync(buffer, 0, buffer.Length);
                    if (n == 0) break;
                    totalFlushed += n;
                    System.Diagnostics.Debug.WriteLine($"[FpgaIoClient] 清空残留数据: {n} bytes");
                }
                
                if (totalFlushed > 0)
                    System.Diagnostics.Debug.WriteLine($"[FpgaIoClient] 共清空残留数据: {totalFlushed} bytes");
            }
            catch { }
            finally
            {
                _client.ReceiveTimeout = 0; // 恢复无限等待
            }
        }

        #region 异步接收功能

        /// <summary>
        /// 启动异步接收任务。连接成功后调用，持续监听FPGA发送的消息并缓存。
        /// </summary>
        /// <param name="logger">日志回调（可选）</param>
        public void StartAsyncReceive(Action<string> logger = null)
        {
            if (IsAsyncReceiveRunning) return;
            if (!IsConnected) return;

            _asyncReceiveLogger = logger;
            _asyncReceiveCts = new CancellationTokenSource();
            _asyncReceiveTask = Task.Run(() => AsyncReceiveLoopAsync(_asyncReceiveCts.Token));
            _asyncReceiveLogger?.Invoke("[FPGA] 异步接收已启动");
        }

        /// <summary>
        /// 停止异步接收任务
        /// </summary>
        public void StopAsyncReceive()
        {
            if (_asyncReceiveCts != null)
            {
                try { _asyncReceiveCts.Cancel(); } catch { }
                try { _asyncReceiveCts.Dispose(); } catch { }
                _asyncReceiveCts = null;
            }
            _asyncReceiveTask = null;
            _asyncReceiveLogger?.Invoke("[FPGA] 异步接收已停止");
        }

        /// <summary>
        /// 异步接收循环
        /// </summary>
        private async Task AsyncReceiveLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested && IsConnected)
            {
                try
                {
                    // 检查是否需要清理旧消息（每10秒）
                    if ((DateTime.UtcNow - _lastClearTime).TotalMilliseconds >= AsyncReceiveClearIntervalMs)
                    {
                        ClearReceivedFrames();
                        _lastClearTime = DateTime.UtcNow;
                    }

                    // 检查是否有数据可读
                    if (_stream == null || !_client.Connected)
                        break;

                    if (!_stream.DataAvailable)
                    {
                        await Task.Delay(50, token);
                        continue;
                    }

                    // 尝试读取帧（不使用锁，因为这是独立的接收任务）
                    var frame = await TryReadFrameForAsyncAsync(token);
                    if (frame.HasValue)
                    {
                        var (cmd, payload) = frame.Value;
                        var receivedFrame = new ReceivedFrame
                        {
                            Command = cmd,
                            Payload = payload,
                            ReceivedTime = DateTime.UtcNow,
                            RawHex = $"AA 55 {(1 + payload.Length):X2} {cmd:X2} {BitConverter.ToString(payload).Replace("-", " ")}"
                        };

                        lock (_receivedFramesLock)
                        {
                            _receivedFrames.Add(receivedFrame);
                        }

                        _asyncReceiveLogger?.Invoke($"[FPGA异步] 收到帧: cmd=0x{cmd:X2}, len={payload.Length}, hex={receivedFrame.RawHex}");
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[FpgaIoClient] 异步接收异常: {ex.Message}");
                    await Task.Delay(100, token);
                }
            }
        }

        /// <summary>
        /// 尝试读取一帧（用于异步接收，不加锁）
        /// </summary>
        private async Task<(byte cmd, byte[] payload)?> TryReadFrameForAsyncAsync(CancellationToken token)
        {
            try
            {
                // 读取帧头
                var header = new byte[2];
                int headerRead = 0;
                while (headerRead < 2)
                {
                    if (!_stream.DataAvailable && headerRead == 0)
                        return null;
                    int n = await _stream.ReadAsync(header, headerRead, 2 - headerRead, token);
                    if (n == 0) return null;
                    headerRead += n;
                }

                if (header[0] != 0xAA || header[1] != 0x55)
                {
                    System.Diagnostics.Debug.WriteLine($"[FpgaIoClient] 异步接收帧头错误: 0x{header[0]:X2} 0x{header[1]:X2}");
                    return null;
                }

                // 读取长度
                var lenBuf = new byte[1];
                int lenRead = await _stream.ReadAsync(lenBuf, 0, 1, token);
                if (lenRead == 0) return null;
                int totalLen = lenBuf[0];

                // 读取命令+数据
                var body = new byte[totalLen];
                int bodyRead = 0;
                while (bodyRead < totalLen)
                {
                    int n = await _stream.ReadAsync(body, bodyRead, totalLen - bodyRead, token);
                    if (n == 0) return null;
                    bodyRead += n;
                }

                byte cmd = body[0];
                byte[] payload = new byte[totalLen - 1];
                if (payload.Length > 0)
                    Buffer.BlockCopy(body, 1, payload, 0, payload.Length);

                return (cmd, payload);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 清空缓存的接收帧
        /// </summary>
        public void ClearReceivedFrames()
        {
            lock (_receivedFramesLock)
            {
                int count = _receivedFrames.Count;
                _receivedFrames.Clear();
                if (count > 0)
                    _asyncReceiveLogger?.Invoke($"[FPGA] 已清理 {count} 条缓存消息");
            }
        }

        /// <summary>
        /// 获取指定命令的所有缓存帧
        /// </summary>
        /// <param name="command">命令字节</param>
        /// <returns>匹配的帧列表</returns>
        public List<ReceivedFrame> GetReceivedFramesByCommand(byte command)
        {
            lock (_receivedFramesLock)
            {
                return _receivedFrames.Where(f => f.Command == command).ToList();
            }
        }

        /// <summary>
        /// 获取指定命令的最新一帧
        /// </summary>
        /// <param name="command">命令字节</param>
        /// <returns>最新的帧，如果没有则返回null</returns>
        public ReceivedFrame GetLatestFrameByCommand(byte command)
        {
            lock (_receivedFramesLock)
            {
                return _receivedFrames.LastOrDefault(f => f.Command == command);
            }
        }

        /// <summary>
        /// 获取指定时间之后收到的指定命令的帧
        /// </summary>
        /// <param name="command">命令字节</param>
        /// <param name="afterTime">时间点</param>
        /// <returns>匹配的帧列表</returns>
        public List<ReceivedFrame> GetReceivedFramesByCommandAfter(byte command, DateTime afterTime)
        {
            lock (_receivedFramesLock)
            {
                return _receivedFrames.Where(f => f.Command == command && f.ReceivedTime > afterTime).ToList();
            }
        }

        /// <summary>
        /// 获取所有缓存帧的数量
        /// </summary>
        public int ReceivedFrameCount
        {
            get
            {
                lock (_receivedFramesLock)
                {
                    return _receivedFrames.Count;
                }
            }
        }

        #endregion

        public void Dispose()
        {
            StopAsyncReceive();
            _lock?.Dispose();
            Disconnect();
        }
    }

    /// <summary>
    /// 接收到的FPGA帧结构
    /// </summary>
    internal class ReceivedFrame
    {
        /// <summary>命令字节</summary>
        public byte Command { get; set; }

        /// <summary>数据载荷</summary>
        public byte[] Payload { get; set; }

        /// <summary>接收时间（UTC）</summary>
        public DateTime ReceivedTime { get; set; }

        /// <summary>原始十六进制字符串</summary>
        public string RawHex { get; set; }
    }
}
