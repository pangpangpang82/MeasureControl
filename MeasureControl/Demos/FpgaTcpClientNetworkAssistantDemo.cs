using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MeasureControl.Helpers;

namespace MeasureControl.Demos
{
    public static class FpgaTcpClientNetworkAssistantDemo
    {
        public static void Run(
            string ipAddress = "127.0.0.1",
            int port = 9000,
            bool readReply = false,
            int readTimeoutMs = 2000)
        {
            RunAsync(ipAddress, port, readReply, readTimeoutMs).GetAwaiter().GetResult();
        }

        public static async Task RunAsync(
            string ipAddress = "127.0.0.1",
            int port = 9000,
            bool readReply = false,
            int readTimeoutMs = 2000,
            CancellationToken cancellationToken = default)
        {
            Trace.WriteLine("FpgaTcpClient -> TCP Server (Network Assistant) Demo");
            Trace.WriteLine("==============================================");
            Trace.WriteLine($"Target: {ipAddress}:{port}");
            Trace.WriteLine(readReply ? "Read reply: ON" : "Read reply: OFF");

            using (var client = new FpgaTcpClient())
            {
                await client.ConnectAsync(ipAddress, port, cancellationToken).ConfigureAwait(false);
                Trace.WriteLine("Connected.");

                byte[] asciiPayload = Encoding.ASCII.GetBytes(
                    $"HELLO FROM FPGA TCP CLIENT {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}\r\n");
                await client.WriteAsync(asciiPayload, 0, asciiPayload.Length, cancellationToken).ConfigureAwait(false);
                Trace.WriteLine($"Sent ASCII ({asciiPayload.Length} bytes)." );

                byte[] binaryPayload = { 0xAA, 0x55, 0x05, 0x01, 0x00, 0x00, 0x00, 0x00 };
                await client.WriteAsync(binaryPayload, 0, binaryPayload.Length, cancellationToken).ConfigureAwait(false);
                Trace.WriteLine("Sent HEX: AA 55 05 01 00 00 00 00");

                if (readReply)
                {
                    var buffer = new byte[1024];
                    bool anyReceived = false;
                    var sw = Stopwatch.StartNew();

                    while (sw.ElapsedMilliseconds < readTimeoutMs)
                    {
                        int remaining = readTimeoutMs - (int)sw.ElapsedMilliseconds;
                        int perReadTimeoutMs = remaining > 1000 ? 1000 : remaining;

                        using (var timeoutCts = new CancellationTokenSource(perReadTimeoutMs))
                        using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token))
                        {
                            try
                            {
                                int n = await client.ReadAsync(buffer, 0, buffer.Length, linkedCts.Token)
                                    .ConfigureAwait(false);

                                if (n <= 0)
                                {
                                    Trace.WriteLine("Remote closed connection." );
                                    break;
                                }

                                anyReceived = true;
                                Trace.WriteLine($"Received {n} bytes.");
                                Trace.WriteLine("ASCII view:");
                                Trace.WriteLine(Encoding.ASCII.GetString(buffer, 0, n));
                                Trace.WriteLine("HEX view:");
                                Trace.WriteLine(BitConverter.ToString(buffer, 0, n));
                            }
                            catch (OperationCanceledException)
                            {
                                // keep waiting until total readTimeoutMs elapsed
                            }
                        }
                    }

                    if (!anyReceived)
                        Trace.WriteLine($"Read finished: no data received within {readTimeoutMs}ms." );
                }

                client.Disconnect();
                Trace.WriteLine("Disconnected." );
            }

            Trace.WriteLine("Demo finished." );
        }

        public static async Task RunFromArgsAsync(string[] args, CancellationToken cancellationToken = default)
        {
            string ip = args != null && args.Length >= 1 ? args[0] : "127.0.0.1";
            int port = 9000;

            if (args != null && args.Length >= 2 && int.TryParse(args[1], out int parsedPort))
                port = parsedPort;

            bool readReply = args != null && args.Any(a => string.Equals(a, "read", StringComparison.OrdinalIgnoreCase));

            await RunAsync(ip, port, readReply, 2000, cancellationToken).ConfigureAwait(false);
        }
    }
}
