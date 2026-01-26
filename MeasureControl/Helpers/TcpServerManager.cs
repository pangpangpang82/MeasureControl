using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;

namespace MeasureControl.Helpers
{
    /// <summary>
    /// 公共的 TCP Server 管理器（单例），负责端口绑定、复用与 Accept 循环，
    /// 并在接收到客户端连接时调用注册的回调以处理客户端连接。
    /// </summary>
    public sealed class TcpServerManager
    {
        private static readonly object _lock = new object();
        private readonly Dictionary<string, TcpServerInfo> _servers = new Dictionary<string, TcpServerInfo>();
        private static readonly TcpServerManager _instance = new TcpServerManager();

        private TcpServerManager() { }

        public static TcpServerManager Instance => _instance;

        /// <summary>
        /// 启动 TCP Server（如果已存在则增加 RefCount 并复用）。
        /// handler: 当接收到客户端时调用，调用方负责处理连接生命周期（可以为 null）。
        /// 返回 true 表示调用成功（已启动或复用），false 表示启动失败。
        /// </summary>
        public bool Start(int port, string boardIdentifier, Func<TcpClient, TcpServerInfo, CancellationToken, Task> handler)
        {
            lock (_lock)
            {
                if (_servers.TryGetValue(boardIdentifier, out var existing))
                {
                    existing.RefCount++;
                    if (handler != null)
                    {
                        existing.Handlers.Add(handler);
                    }
                    Debug.WriteLine($"[TcpServerManager] Reuse server: {boardIdentifier}, RefCount={existing.RefCount}");
                    return true;
                }

                var serverInfo = new TcpServerInfo
                {
                    Port = port,
                    BoardIdentifier = boardIdentifier,
                    Cts = new CancellationTokenSource(),
                    RefCount = 1,
                    Handlers = new List<Func<TcpClient, TcpServerInfo, CancellationToken, Task>>()
                };
                if (handler != null) serverInfo.Handlers.Add(handler);

                try
                {
                    serverInfo.Listener = new TcpListener(IPAddress.Any, port);
                    serverInfo.Listener.Start();
                    Debug.WriteLine($"[TcpServerManager] Listener started: {boardIdentifier} -> {port}");

                    var token = serverInfo.Cts.Token;
                    serverInfo.AcceptTask = Task.Run(() => AcceptLoopAsync(serverInfo, token), token);

                    _servers[boardIdentifier] = serverInfo;
                    return true;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[TcpServerManager] Start failed: {ex.Message}");
                    try
                    {
                        serverInfo.Cts?.Cancel();
                        serverInfo.Listener?.Stop();
                    }
                    catch { }
                    return false;
                }
            }
        }

        /// <summary>
        /// 注册一个额外的客户端处理回调。如果 server 不存在则返回 false。
        /// </summary>
        public bool RegisterHandler(string boardIdentifier, Func<TcpClient, TcpServerInfo, CancellationToken, Task> handler)
        {
            if (handler == null) return false;
            lock (_lock)
            {
                if (!_servers.TryGetValue(boardIdentifier, out var serverInfo))
                    return false;
                serverInfo.Handlers.Add(handler);
                Debug.WriteLine($"[TcpServerManager] Handler registered for {boardIdentifier}. Handlers={serverInfo.Handlers.Count}");
                return true;
            }
        }

        /// <summary>
        /// 取消注册回调（若存在）。
        /// </summary>
        public bool UnregisterHandler(string boardIdentifier, Func<TcpClient, TcpServerInfo, CancellationToken, Task> handler)
        {
            if (handler == null) return false;
            lock (_lock)
            {
                if (!_servers.TryGetValue(boardIdentifier, out var serverInfo))
                    return false;
                bool removed = serverInfo.Handlers.Remove(handler);
                Debug.WriteLine($"[TcpServerManager] Handler unregistered for {boardIdentifier}. Removed={removed}");
                return removed;
            }
        }

        /// <summary>
        /// 停止指定 boardIdentifier 的 TCP server（考虑 RefCount）。
        /// </summary>
        public void Stop(string boardIdentifier)
        {
            lock (_lock)
            {
                if (!_servers.TryGetValue(boardIdentifier, out var serverInfo))
                    return;

                if (serverInfo.RefCount > 1)
                {
                    serverInfo.RefCount--;
                    Debug.WriteLine($"[TcpServerManager] Decrement RefCount: {boardIdentifier}, RefCount={serverInfo.RefCount}");
                    return;
                }

                try
                {
                    serverInfo.Cts?.Cancel();
                }
                catch { }

                try
                {
                    serverInfo.Listener?.Stop();
                }
                catch { }

                _servers.Remove(boardIdentifier);
                Debug.WriteLine($"[TcpServerManager] Stopped and removed server: {boardIdentifier}");
            }
        }

        public bool IsRunning(string boardIdentifier)
        {
            lock (_lock)
            {
                return _servers.ContainsKey(boardIdentifier);
            }
        }

        private static async Task AcceptLoopAsync(TcpServerInfo serverInfo, CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    TcpClient client = null;
                    try
                    {
                        var acceptTask = serverInfo.Listener.AcceptTcpClientAsync();
                        var completed = await Task.WhenAny(acceptTask, Task.Delay(Timeout.Infinite, token));
                        if (completed != acceptTask)
                            break;

                        client = acceptTask.Result;
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (ObjectDisposedException)
                    {
                        break;
                    }
                    catch (InvalidOperationException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[TcpServerManager] AcceptLoop exception: {ex.Message}");
                        continue;
                    }

                    // 交给所有注册回调处理连接（逐个分发）
                    try
                    {
                        var handlers = serverInfo.Handlers;
                        if (handlers != null && handlers.Count > 0)
                        {
                            foreach (var handler in handlers.ToArray())
                            {
                                try
                                {
                                    _ = Task.Run(() => handler(client, serverInfo, token));
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine($"[TcpServerManager] Handler dispatch exception: {ex.Message}");
                                }
                            }
                        }
                        else
                        {
                            // 没有注册回调则关闭连接
                            client.Close();
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[TcpServerManager] Client dispatch exception: {ex.Message}");
                        try { client?.Close(); } catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TcpServerManager] AcceptLoop fatal: {ex.Message}");
            }
        }
    }

    public class TcpServerInfo
    {
        public TcpListener Listener { get; set; }
        public CancellationTokenSource Cts { get; set; }
        public Task AcceptTask { get; set; }
        public int Port { get; set; }
        public string BoardIdentifier { get; set; }
        public int RefCount { get; set; }
        public List<Func<TcpClient, TcpServerInfo, CancellationToken, Task>> Handlers { get; set; }
    }
}

