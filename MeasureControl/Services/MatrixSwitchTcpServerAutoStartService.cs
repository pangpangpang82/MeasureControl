using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using MeasureControl.Events;
using MeasureControl.Helpers;
using MeasureControl.Models;
using Prism.Events;

namespace MeasureControl.Services
{
    public sealed class MatrixSwitchTcpServerAutoStartService : IDisposable
    {
        private const string LocalChassisIpAddress = "192.168.1.3";
        private const string RemoteClientIpAddress = "192.168.1.2";

        private readonly IPxiChassisService _pxiChassisService;
        private readonly IEventAggregator _eventAggregator;

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

            // 开机时 SlotIndex 还是默认值-1，自己启动TCP Server会因端口计算错误而失败。
            // 改为导航到机箱页面，让 PxiChassisViewModel.OnNavigatedTo 执行完整初始化：
            //   LoadChassisDevices → UpdateAllSlotPositions（设置正确的SlotIndex）→ StartTcpServerForPort
            // 用户会看到机箱2页面被打开，这是预期行为。
            // 此方法由 NavigateToHomePageOnStartup 调用，此时窗口已Loaded，Region已就绪。
            try
            {
                var chassis = _pxiChassisService.GetChassisByName(chassisName);
                if (chassis != null)
                {
                    Debug.WriteLine($"[MatrixSwitchTcpServerAutoStartService] 导航到机箱页面以启动TCP Server: {chassisName}");
                    _eventAggregator.GetEvent<PxiChassisSelectedEvent>().Publish(new PxiChassisSelectedEventArgs
                    {
                        ChassisName = chassisName,
                        ChassisId = chassis.Id
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MatrixSwitchTcpServerAutoStartService] 导航到机箱页面失败: {ex.Message}");
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

        public void Dispose()
        {
            // TCP Server 由 PxiChassisViewModel 管理，此处无需清理
        }
    }
}
