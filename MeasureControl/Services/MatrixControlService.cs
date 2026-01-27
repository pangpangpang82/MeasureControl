using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace MeasureControl.Services
{
    /// <summary>
    /// 矩阵开关控制服务
    /// 提供直接的TCP通信接口来控制PXI-2601矩阵开关
    /// 2601(1) slotindex4 ,2601(2) slotindex 6,2601(3) slotindex 7,2601(4) slotindex 8,2601(5) slotindex9
    /// 3022 (1) slotindx 2,3022(2) slotindex 3
    /// 对应的使用方法在 MatrixSwitchDemo中，3022需要await svc.ConnectNodesAsync(op.inNode, op.outNode, op.slot, op.ip)多传递一个参数50300
    /// </summary>
    public class MatrixControlService : IDisposable
    {
        #region 单例模式

        private static readonly Lazy<MatrixControlService> _instance =
            new Lazy<MatrixControlService>(() => new MatrixControlService());

        /// <summary>
        /// 获取单例实例
        /// </summary>
        public static MatrixControlService Instance => _instance.Value;

        #endregion

        #region 常量定义

        private const int TcpBasePort = 50200;
        private const string DefaultIpAddress = "192.168.1.3";
        private const byte RemoteCommandConnect = 0;
        private const byte RemoteCommandDisconnect = 1;

        #endregion

        #region 私有字段

        private TcpClient _tcpClient;
        private NetworkStream _tcpStream;
        private DateTime _tcpLastActivityTime;
        private readonly TimeSpan _tcpInactivityTimeout = TimeSpan.FromSeconds(30);
        // Lightweight semaphore kept for backward compatibility; per-endpoint locks are preferred.
        private readonly System.Threading.SemaphoreSlim _sendLock = new System.Threading.SemaphoreSlim(1, 1);

        // Per-endpoint connection pool: key = "ip:port"
        private class ConnectionEntry
        {
            public TcpClient Client;
            public NetworkStream Stream;
            public SemaphoreSlim Lock = new SemaphoreSlim(1, 1);
            public DateTime LastActivity = DateTime.UtcNow;
        }

        private readonly ConcurrentDictionary<string, ConnectionEntry> _connections = new ConcurrentDictionary<string, ConnectionEntry>();

        #endregion

        #region 构造函数

        private MatrixControlService()
        {
            // 私有构造函数，防止外部实例化
        }

        #endregion

        #region 公共方法

        /// <summary>
        /// 发送矩阵命令
        /// </summary>
        /// <param name="inputNodeId">输入节点ID，如"I1", "I2"等</param>
        /// <param name="outputNodeId">输出节点ID，如"O1", "O2"等</param>
        /// <param name="state">状态：0=连接, 1=断开</param>
        /// <param name="slotIndex">插槽索引，端口=50200+slotIndex</param>
        /// <param name="ipAddress">目标IP地址，默认192.168.1.3</param>
        /// <returns>操作是否成功</returns>
        public async Task<bool> SendMatrixCommandAsync(
            string inputNodeId,
            string outputNodeId,
            byte state,
            int slotIndex,
            string ipAddress = DefaultIpAddress,
            int tcpBasePort = TcpBasePort) // 可选参数：基端口，默认为50200（PXI-2601）
        {
            if (!TryParseNodeIndex(inputNodeId, out var inputIndex) ||
                !TryParseNodeIndex(outputNodeId, out var outputIndex))
            {
                Debug.WriteLine($"[MatrixControlService] 节点解析失败: {inputNodeId} -> {outputNodeId}");
                return false;
            }

            int port = tcpBasePort + slotIndex;
            Debug.WriteLine($"[MatrixControlService] 发送矩阵命令: {inputNodeId}({inputIndex})->{outputNodeId}({outputIndex}), state={state}, IP={ipAddress}, Port={port}, BasePort={tcpBasePort}");

            return await SendRemoteCommandWithRetryAsync(ipAddress, port, inputIndex, outputIndex, state);
        }

        /// <summary>
        /// 连接矩阵节点
        /// </summary>
        /// <param name="inputNodeId">输入节点ID</param>
        /// <param name="outputNodeId">输出节点ID</param>
        /// <param name="slotIndex">插槽索引</param>
        /// <param name="ipAddress">目标IP地址</param>
        /// <returns>操作是否成功</returns>
        public async Task<bool> ConnectNodesAsync(
            string inputNodeId,
            string outputNodeId,
            int slotIndex,
            string ipAddress = DefaultIpAddress,
            int tcpBasePort = TcpBasePort)
        {
            return await SendMatrixCommandAsync(inputNodeId, outputNodeId, RemoteCommandConnect, slotIndex, ipAddress, tcpBasePort);
        }

        /// <summary>
        /// 断开矩阵节点连接
        /// </summary>
        /// <param name="inputNodeId">输入节点ID</param>
        /// <param name="outputNodeId">输出节点ID</param>
        /// <param name="slotIndex">插槽索引</param>
        /// <param name="ipAddress">目标IP地址</param>
        /// <returns>操作是否成功</returns>
        public async Task<bool> DisconnectNodesAsync(
            string inputNodeId,
            string outputNodeId,
            int slotIndex,
            string ipAddress = DefaultIpAddress,
            int tcpBasePort = TcpBasePort)
        {
            return await SendMatrixCommandAsync(inputNodeId, outputNodeId, RemoteCommandDisconnect, slotIndex, ipAddress, tcpBasePort);
        }

        /// <summary>
        /// 直接按目标 IP:Port 发送矩阵命令（无需 slotIndex），适用于直接传入端口的场景
        /// </summary>
        /// <param name="inputNodeId">输入节点ID</param>
        /// <param name="outputNodeId">输出节点ID</param>
        /// <param name="state">状态：0=连接,1=断开</param>
        /// <param name="ipAddress">目标 IP 地址</param>
        /// <param name="port">目标端口（完整端口号）</param>
        /// <returns>操作是否成功</returns>
        public async Task<bool> SendMatrixCommandByPortAsync(
            string inputNodeId,
            string outputNodeId,
            byte state,
            string ipAddress,
            int port)
        {
            if (!TryParseNodeIndex(inputNodeId, out var inputIndex) ||
                !TryParseNodeIndex(outputNodeId, out var outputIndex))
            {
                Debug.WriteLine($"[MatrixControlService] 节点解析失败: {inputNodeId} -> {outputNodeId}");
                return false;
            }

            Debug.WriteLine($"[MatrixControlService] 直接按端口发送矩阵命令: {inputNodeId}({inputIndex})->{outputNodeId}({outputIndex}), state={state}, IP={ipAddress}, Port={port}");
            return await SendRemoteCommandWithRetryAsync(ipAddress, port, inputIndex, outputIndex, state);
        }

        #endregion

        #region 私有方法

        private static bool TryParseNodeIndex(string nodeId, out byte index)
        {
            index = 0;
            if (string.IsNullOrWhiteSpace(nodeId) || nodeId.Length < 2) return false;
            if (!int.TryParse(nodeId.Substring(1), out int value)) return false;
            if (value < 0 || value > byte.MaxValue) return false;
            index = (byte)value;
            return true;
        }

        private async Task<bool> SendRemoteCommandWithRetryAsync(string ipAddress, int port, byte inputIndex, byte outputIndex, byte state, int maxRetries = 2)
        {
            for (int retry = 0; retry < maxRetries; retry++)
            {
                try
                {
                    if (retry > 0)
                    {
                        Debug.WriteLine($"[MatrixControlService] 第 {retry + 1} 次重试");
                        await Task.Delay(50 * retry);
                        // remove possibly-broken connection for this endpoint so next attempt recreates it
                        TryRemoveConnection($"{ipAddress}:{port}");
                    }

                    bool success = await SendRemoteCommandAsync(ipAddress, port, inputIndex, outputIndex, state);
                    if (success) return true;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[MatrixControlService] 重试 {retry + 1} 失败: {ex.Message}");
                    if (retry == maxRetries - 1) return false;
                }
            }

            return false;
        }

        private async Task<bool> SendRemoteCommandAsync(string ipAddress, int port, byte inputIndex, byte outputIndex, byte state)
        {
            var startTime = DateTime.Now;
            try
            {
                // Use per-endpoint connection entry
                var key = $"{ipAddress}:{port}";
                var entry = await GetOrCreateConnectionAsync(ipAddress, port);

                // Serialize operations per-connection to avoid interleaving
                bool acquired = await entry.Lock.WaitAsync(TimeSpan.FromSeconds(5));
                if (!acquired)
                {
                    Debug.WriteLine("[MatrixControlService] 连接条目锁等待超时");
                    return false;
                }

                try
                {
                    entry.LastActivity = DateTime.UtcNow;

                    var buffer = new[] { inputIndex, outputIndex, state };
                    Debug.WriteLine($"[MatrixControlService] TX({ipAddress}:{port}): {BitConverter.ToString(buffer)}");
                    await entry.Stream.WriteAsync(buffer, 0, buffer.Length);
                    await entry.Stream.FlushAsync();

                    var sendTime = DateTime.Now - startTime;
                    Debug.WriteLine($"[MatrixControlService] 发送耗时: {(int)sendTime.TotalMilliseconds}ms");

                    var ack = new byte[3];
                    int timeoutMs = 2000;

                    using (var cts = new CancellationTokenSource(timeoutMs))
                    {
                        try
                        {
                            int totalRead = 0;
                            while (totalRead < ack.Length)
                            {
                                int read = await entry.Stream.ReadAsync(ack, totalRead, ack.Length - totalRead, cts.Token);
                                if (read <= 0)
                                {
                                    Debug.WriteLine("[MatrixControlService] 连接中断（读取到0）");
                                    TryRemoveConnection(key);
                                    return false;
                                }
                                totalRead += read;
                            }

                            var totalTime = DateTime.Now - startTime;
                            bool success = ack[0] == inputIndex && ack[1] == outputIndex && ack[2] == state;
                            if (!success)
                            {
                                Debug.WriteLine("[MatrixControlService] 响应验证失败");
                                TryRemoveConnection(key);
                            }

                            Debug.WriteLine($"[MatrixControlService] RX({ipAddress}:{port}): {BitConverter.ToString(ack)}, 总耗时: {(int)totalTime.TotalMilliseconds}ms");
                            entry.LastActivity = DateTime.UtcNow;
                            return success;
                        }
                        catch (OperationCanceledException)
                        {
                            Debug.WriteLine("[MatrixControlService] 接收超时");
                            TryRemoveConnection(key);
                            return false;
                        }
                    }
                }
                finally
                {
                    try { entry.Lock.Release(); } catch { }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MatrixControlService] 异常: {ex.Message}");
                TryRemoveConnection($"{ipAddress}:{port}");
                return false;
            }
        }

        private async Task<bool> EnsureTcpConnectedAsync(string ipAddress, int port)
        {
            try
            {
                if (_tcpClient != null && _tcpClient.Connected)
                {
                    if (DateTime.Now - _tcpLastActivityTime < _tcpInactivityTimeout)
                    {
                        return true;
                    }
                    else
                    {
                        CleanupTcpConnection();
                    }
                }

                Debug.WriteLine($"[MatrixControlService] 创建新TCP连接到 {ipAddress}:{port}");

                var client = new TcpClient();
                client.NoDelay = true;
                client.SendTimeout = 3000;
                client.ReceiveTimeout = 3000;

                client.SendBufferSize = 8192;
                client.ReceiveBufferSize = 8192;

                client.LingerState = new LingerOption(true, 0);

                await client.ConnectAsync(ipAddress, port);

                _tcpClient = client;
                _tcpStream = client.GetStream();
                _tcpLastActivityTime = DateTime.Now;

                Debug.WriteLine($"[MatrixControlService] TCP连接建立成功 Local={client.Client?.LocalEndPoint} Remote={client.Client?.RemoteEndPoint}");
                return true;
            }
            catch (Exception ex)
            {
                if (ex is SocketException se)
                {
                    Debug.WriteLine($"[MatrixControlService] 连接失败(Socket): {se.SocketErrorCode}, {se.Message}");
                }
                else
                {
                    Debug.WriteLine($"[MatrixControlService] 连接失败: {ex.Message}");
                }
                CleanupTcpConnection();
                return false;
            }
        }

        private async Task<ConnectionEntry> GetOrCreateConnectionAsync(string ipAddress, int port)
        {
            var key = $"{ipAddress}:{port}";
            if (_connections.TryGetValue(key, out var existing))
            {
                // If existing client appears connected use it
                try
                {
                    if (existing?.Client != null && existing.Client.Connected)
                    {
                        return existing;
                    }
                }
                catch { }
                // otherwise remove and recreate
                TryRemoveConnection(key);
            }

            var entry = new ConnectionEntry();
            try
            {
                var client = new TcpClient();
                client.NoDelay = true;
                client.SendTimeout = 3000;
                client.ReceiveTimeout = 3000;
                client.SendBufferSize = 8192;
                client.ReceiveBufferSize = 8192;
                client.LingerState = new LingerOption(true, 0);

                await client.ConnectAsync(ipAddress, port);

                entry.Client = client;
                entry.Stream = client.GetStream();
                entry.LastActivity = DateTime.UtcNow;

                _connections[key] = entry;
                Debug.WriteLine($"[MatrixControlService] 创建连接条目 {key}");
                return entry;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MatrixControlService] 创建连接失败 {key}: {ex.Message}");
                try { entry.Stream?.Dispose(); } catch { }
                try { entry.Client?.Close(); } catch { }
                return null;
            }
        }

        private void TryRemoveConnection(string key)
        {
            if (_connections.TryRemove(key, out var entry))
            {
                try { entry.Stream?.Dispose(); } catch { }
                try { entry.Client?.Close(); } catch { }
                try { entry.Lock?.Dispose(); } catch { }
                Debug.WriteLine($"[MatrixControlService] 连接条目已移除 {key}");
            }
        }

        private void CleanupTcpConnection()
        {
            try
            {
                _tcpStream?.Dispose();
                _tcpStream = null;

                _tcpClient?.Close();
                _tcpClient?.Dispose();
                _tcpClient = null;

                Debug.WriteLine("[MatrixControlService] TCP连接已清理");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MatrixControlService] 清理失败: {ex.Message}");
            }
        }

        #endregion

        #region IDisposable 实现

        public void Dispose()
        {
            // cleanup the legacy single-client fields
            CleanupTcpConnection();

            // cleanup all pooled connections
            foreach (var key in _connections.Keys)
            {
                TryRemoveConnection(key);
            }
        }

        #endregion
    }
}