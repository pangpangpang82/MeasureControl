using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace MeasureControl.Helpers
{
    public sealed class FpgaTcpClient : IDisposable
    {
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
        private TcpClient _client;
        private NetworkStream _stream;

        public bool IsConnected => _client?.Connected ?? false;

        public async Task ConnectAsync(string ipAddress, int port, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(ipAddress))
                throw new ArgumentException("IP address is required.", nameof(ipAddress));
            if (port <= 0 || port > 65535)
                throw new ArgumentOutOfRangeException(nameof(port), port, "Port must be between 1 and 65535.");

            Disconnect();

            var client = new TcpClient
            {
                NoDelay = true
            };

            using (cancellationToken.Register(() =>
                   {
                       try { client.Dispose(); } catch { }
                   }))
            {
                await client.ConnectAsync(ipAddress, port).ConfigureAwait(false);
            }

            _client = client;
            _stream = _client.GetStream();
        }

        public void Disconnect()
        {
            try { _stream?.Dispose(); } catch { }
            _stream = null;

            try { _client?.Close(); } catch { }
            try { _client?.Dispose(); } catch { }
            _client = null;
        }

        public async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken = default)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (_stream == null) throw new InvalidOperationException("Not connected.");

            await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await _stream.WriteAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
                await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _sendLock.Release();
            }
        }

        public async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken = default)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (_stream == null) throw new InvalidOperationException("Not connected.");

            return await _stream.ReadAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
        }

        public async Task<byte[]> RequestReplyAsync(
            byte[] request,
            int expectedReplyBytes,
            int timeoutMs = 1000,
            CancellationToken cancellationToken = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (expectedReplyBytes <= 0) throw new ArgumentOutOfRangeException(nameof(expectedReplyBytes));
            if (timeoutMs <= 0) throw new ArgumentOutOfRangeException(nameof(timeoutMs));

            using (var timeoutCts = new CancellationTokenSource(timeoutMs))
            using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token))
            {
                await WriteAsync(request, 0, request.Length, linkedCts.Token).ConfigureAwait(false);

                var reply = new byte[expectedReplyBytes];
                int totalRead = 0;
                while (totalRead < expectedReplyBytes)
                {
                    int read = await ReadAsync(reply, totalRead, expectedReplyBytes - totalRead, linkedCts.Token)
                        .ConfigureAwait(false);
                    if (read <= 0)
                        throw new SocketException((int)SocketError.ConnectionReset);
                    totalRead += read;
                }

                return reply;
            }
        }

        public void Dispose()
        {
            Disconnect();
            _sendLock.Dispose();
        }
    }
}
