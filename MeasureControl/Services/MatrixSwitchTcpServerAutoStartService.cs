using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using MeasureControl.Drivers;
using MeasureControl.Drivers.ArtSwitch;
using MeasureControl.Drivers.PXI3022;
using MeasureControl.Events;
using MeasureControl.Helpers;
using MeasureControl.Models;
using MeasureControl.Models.Devices;
using Prism.Events;

namespace MeasureControl.Services
{
    public sealed class MatrixSwitchTcpServerAutoStartService : IDisposable
    {
        private const int TcpBasePort2601 = 50200;
        private const int TcpBasePort3022 = 50300;
        private const string LocalChassisIpAddress = "192.168.1.3";
        private const string RemoteClientIpAddress = "192.168.1.2";

        private readonly IPxiChassisService _pxiChassisService;
        private readonly IEventAggregator _eventAggregator;

        private readonly HashSet<string> _ownedServerIdentifiers = new HashSet<string>();

        public MatrixSwitchTcpServerAutoStartService(IPxiChassisService pxiChassisService, IEventAggregator eventAggregator)
        {
            _pxiChassisService = pxiChassisService ?? throw new ArgumentNullException(nameof(pxiChassisService));
            _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
        }

        public void StartForLocalChassis(string chassisName)
        {
            if (!IsLocalChassisByIp())
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(chassisName))
            {
                return;
            }

            var chassis = _pxiChassisService.GetChassisByName(chassisName);
            var chassisDevice = chassis?.Devices?.OfType<ChassisDevice>()?.FirstOrDefault();
            var switches = chassisDevice?.Children?.OfType<SwitchDevice>()?.Where(d => d != null && d.SlotIndex > 0).ToList() ?? new List<SwitchDevice>();

            foreach (var sw in switches)
            {
                try
                {
                    var basePort = ResolveTcpBasePort(sw);
                    var port = basePort + sw.SlotIndex;
                    // 与现有 PxiChassisViewModel 的命名保持一致，避免后续页面打开时重复绑定端口。
                    var identifier = $"PXI2601_{port}";

                    if (TcpServerManager.Instance.IsRunning(identifier))
                    {
                        continue;
                    }

                    var ok = TcpServerManager.Instance.Start(port, identifier, (client, serverInfo, token) => HandleClientAsync(client, serverInfo, chassisName, token));
                    if (ok)
                    {
                        _ownedServerIdentifiers.Add(identifier);
                        Debug.WriteLine($"[MatrixSwitchTcpServerAutoStartService] Started TCP server: {identifier}, Port={port}, Chassis={chassisName}");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[MatrixSwitchTcpServerAutoStartService] Start server failed: {ex.Message}");
                }
            }
        }

        private async Task HandleClientAsync(TcpClient client, TcpServerInfo serverInfo, string chassisName, CancellationToken token)
        {
            try
            {
                if (client == null) return;

                using (var stream = client.GetStream())
                {
                    var cmd = new byte[3];
                    while (!token.IsCancellationRequested)
                    {
                        int read = await ReadExactAsync(stream, cmd, 0, cmd.Length, token).ConfigureAwait(false);
                        if (read != cmd.Length) continue;

                        byte inputIndex = cmd[0];
                        byte outputIndex = cmd[1];
                        byte state = cmd[2];

                        int port = serverInfo?.Port ?? TcpBasePort2601;
                        int basePort = port >= TcpBasePort3022 && port < TcpBasePort3022 + 200 ? TcpBasePort3022 : TcpBasePort2601;
                        int slotIndex = port - basePort;

                        bool ok;
                        if (inputIndex == 0xFF)
                        {
                            ok = await ExecuteDriverControlAsync(chassisName, slotIndex, state).ConfigureAwait(false);
                        }
                        else
                        {
                            string inputNodeId = $"r{inputIndex}";
                            string outputNodeId = $"c{outputIndex}";
                            ok = await ExecuteMatrixCommandInChassisAsync(chassisName, slotIndex, inputNodeId, outputNodeId, state).ConfigureAwait(false);
                        }

                        var ack = ok ? cmd : new[] { cmd[0], cmd[1], (byte)(cmd[2] ^ 0xFF) };
                        await stream.WriteAsync(ack, 0, ack.Length, token).ConfigureAwait(false);
                        await stream.FlushAsync(token).ConfigureAwait(false);
                    }
                }
            }
            catch
            {
            }
            finally
            {
                try { client?.Close(); } catch { }
            }
        }

        private async Task<bool> ExecuteDriverControlAsync(string chassisName, int slotIndex, byte state)
        {
            try
            {
                var targetSwitch = FindSwitchDevice(chassisName, slotIndex);
                if (targetSwitch == null) return false;

                var driverObj = DriverFactory.GetCachedDriver(targetSwitch.Id, slotIndex) ?? DriverFactory.CreateDriver(targetSwitch);
                if (driverObj == null) return false;

                if (state == 0)
                {
                    if (driverObj is ArtSwitchDriver artDriver)
                    {
                        string topology = ResolveArtSwitchTopology(targetSwitch, slotIndex);
                        artDriver.CurrentTopology = topology;
                        return await artDriver.ConnectAsync(topology).ConfigureAwait(false);
                    }

                    if (driverObj is PXI3022Driver pxi3022)
                    {
                        return await pxi3022.ConnectAsync().ConfigureAwait(false);
                    }

                    return await driverObj.ConnectAsync().ConfigureAwait(false);
                }

                return await driverObj.DisconnectAsync().ConfigureAwait(false);
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> ExecuteMatrixCommandInChassisAsync(string chassisName, int slotIndex, string inputNodeId, string outputNodeId, byte state)
        {
            try
            {
                var targetSwitch = FindSwitchDevice(chassisName, slotIndex);
                if (targetSwitch == null) return false;

                var driverObj = DriverFactory.GetCachedDriver(targetSwitch.Id, slotIndex) ?? DriverFactory.CreateDriver(targetSwitch);

                if (driverObj is ArtSwitchDriver artDriver)
                {
                    string topology = ResolveArtSwitchTopology(targetSwitch, slotIndex);
                    if (!artDriver.IsConnected)
                    {
                        artDriver.CurrentTopology = topology;
                        var connected = await artDriver.ConnectAsync(topology).ConfigureAwait(false);
                        if (!connected) return false;
                    }

                    bool result;
                    if (state == 0)
                        result = await artDriver.ConnectChannelsWithoutDisconnectAsync(outputNodeId, inputNodeId).ConfigureAwait(false);
                    else
                        result = await artDriver.DisconnectSingleConnectionAsync(outputNodeId, inputNodeId).ConfigureAwait(false);

                    UpdateSwitchConfigAndNotify(chassisName, targetSwitch, inputNodeId, outputNodeId, state);
                    return result;
                }

                if (driverObj is PXI3022Driver pxi3022Driver)
                {
                    if (!pxi3022Driver.IsConnected)
                    {
                        var connected = await pxi3022Driver.ConnectAsync().ConfigureAwait(false);
                        if (!connected) return false;
                    }

                    if (!int.TryParse(inputNodeId?.TrimStart('r', 'R'), out int parsedInputIndex)) return false;
                    if (!int.TryParse(outputNodeId?.TrimStart('c', 'C'), out int parsedOutputIndex)) return false;

                    int row = parsedInputIndex;
                    int col = parsedOutputIndex;

                    if (row < 0 || row > 3 || col < 0 || col > 63) return false;

                    string channelId = $"R{row}C{col}";
                    bool result = state == 0
                        ? await pxi3022Driver.WriteChannelAsync(channelId, 1.0).ConfigureAwait(false)
                        : await pxi3022Driver.WriteChannelAsync(channelId, 0.0).ConfigureAwait(false);

                    UpdateSwitchConfigAndNotify(chassisName, targetSwitch, inputNodeId, outputNodeId, state);
                    return result;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private void UpdateSwitchConfigAndNotify(string chassisName, SwitchDevice targetSwitch, string inputNodeId, string outputNodeId, byte state)
        {
            try
            {
                if (targetSwitch?.CardConfigData is SwitchMatrixCardConfig cardConfig)
                {
                    var newState = state == 0 ? SwitchConnectionState.Connected : SwitchConnectionState.Disconnected;
                    cardConfig.SetConnection(inputNodeId, outputNodeId, newState);
                    try { _pxiChassisService.UpdateDeviceCardConfig(targetSwitch.Id, cardConfig); } catch { }
                }

                try
                {
                    _eventAggregator.GetEvent<DeviceModifiedEvent>()?.Publish(new DeviceModifiedEventArgs
                    {
                        ChassisName = chassisName,
                        ModificationType = "RemoteCommand",
                        Device = targetSwitch
                    });
                }
                catch
                {
                }
            }
            catch
            {
            }
        }

        private SwitchDevice FindSwitchDevice(string chassisName, int slotIndex)
        {
            try
            {
                var chassis = _pxiChassisService.GetChassisByName(chassisName);
                var chassisDevice = chassis?.Devices?.OfType<ChassisDevice>()?.FirstOrDefault();
                return chassisDevice?.Children?.OfType<SwitchDevice>()?.FirstOrDefault(d => d.SlotIndex == slotIndex);
            }
            catch
            {
                return null;
            }
        }

        private static int ResolveTcpBasePort(SwitchDevice device)
        {
            var model = device?.Model ?? string.Empty;
            if (model.Contains("3022") || model.Contains("PXI3022") || model.Contains("PXI-3022"))
            {
                return TcpBasePort3022;
            }

            return TcpBasePort2601;
        }

        private static string ResolveArtSwitchTopology(SwitchDevice device, int slotIndex)
        {
            try
            {
                if (device?.CardConfigData is SwitchMatrixCardConfig cfg && !string.IsNullOrWhiteSpace(cfg.Topology))
                {
                    return cfg.Topology;
                }
            }
            catch
            {
            }

            return TryMapSlotIndexToTopology(slotIndex);
        }

        private static string TryMapSlotIndexToTopology(int slotIndex)
        {
            switch (slotIndex)
            {
                case 4:
                    return artSwitchTopologies.ARTSWITCH_TOPOLOGY_2601_2_WIRE_8X16_MATRIX;
                case 6:
                case 7:
                case 8:
                case 9:
                    return artSwitchTopologies.ARTSWITCH_TOPOLOGY_2601_2_WIRE_4X32_MATRIX;
                default:
                    return artSwitchTopologies.ARTSWITCH_TOPOLOGY_2601_2_WIRE_4X32_MATRIX;
            }
        }

        private static string[] GetLocalIpv4Addresses()
        {
            try
            {
                return Dns.GetHostAddresses(Dns.GetHostName())
                    .Where(a => a.AddressFamily == AddressFamily.InterNetwork)
                    .Where(a => !IPAddress.IsLoopback(a))
                    .Select(a => a.ToString())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Distinct()
                    .ToArray();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        private static bool IsLocalChassisByIp()
        {
            var ips = GetLocalIpv4Addresses();
            if (ips.Contains(LocalChassisIpAddress)) return true;
            if (ips.Contains(RemoteClientIpAddress)) return false;
            return false;
        }

        private static async Task<int> ReadExactAsync(NetworkStream stream, byte[] buffer, int offset, int count, CancellationToken token)
        {
            int totalRead = 0;
            while (totalRead < count && !token.IsCancellationRequested)
            {
                int read = await stream.ReadAsync(buffer, offset + totalRead, count - totalRead, token).ConfigureAwait(false);
                if (read <= 0)
                    break;
                totalRead += read;
            }

            return totalRead;
        }

        public void Dispose()
        {
            try
            {
                foreach (var id in _ownedServerIdentifiers.ToArray())
                {
                    try { TcpServerManager.Instance.Stop(id); } catch { }
                }
                _ownedServerIdentifiers.Clear();
            }
            catch
            {
            }
        }
    }
}
