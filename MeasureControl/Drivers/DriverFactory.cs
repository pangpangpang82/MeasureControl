using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using MeasureControl.Models;
using MeasureControl.Models.Devices;
using MeasureControl.Models.Devices.DeviceCategories;
using MeasureControl.Services;
using MeasureControl.Drivers;
using MeasureControl.Drivers.PXI3022;
using MeasureControl.Drivers.ArtSwitch;
using MeasureControl.Drivers.ART4229;
using Prism.Ioc;
using Sys = System;

namespace MeasureControl.Drivers
{
    /// <summary>
    /// 驱动工厂：根据设备类型/型号创建对应驱动实例并缓存
    /// 目标：合并各分支变更，保证所有板卡类型可用
    /// </summary>
    public class DriverFactory
    {
        private static readonly Dictionary<string, IDeviceDriver> _driverCache = new Dictionary<string, IDeviceDriver>();
        private static readonly object _lock = new object();
        // 跟踪所有ProgrammableResistorDevice的SlotIndex，用于logicalID分配
        private static readonly SortedSet<int> _resistorSlotIndices = new SortedSet<int>();

        private static string TryMapSlotIndexToDevAddress(int slotIndex)
        {
            Debug.WriteLine($"[TryMapSlotIndexToDevAddress] Input slotIndex={slotIndex}");
            switch (slotIndex)
            {
                case 4:
                    Debug.WriteLine($"[TryMapSlotIndexToDevAddress] Slot {slotIndex} -> Dev2");
                    return "Dev2";
                case 6:
                    Debug.WriteLine($"[TryMapSlotIndexToDevAddress] Slot {slotIndex} -> Dev3");
                    return "Dev3";
                case 9:
                    Debug.WriteLine($"[TryMapSlotIndexToDevAddress] Slot {slotIndex} -> Dev4");
                    return "Dev4";
                case 8:
                    Debug.WriteLine($"[TryMapSlotIndexToDevAddress] Slot {slotIndex} -> Dev5");
                    return "Dev5";
                case 7:
                    Debug.WriteLine($"[TryMapSlotIndexToDevAddress] Slot {slotIndex} -> Dev6");
                    return "Dev6";
                default:
                    Debug.WriteLine($"[TryMapSlotIndexToDevAddress] Slot {slotIndex} -> null (default)");
                    return null;
            }
        }

        private static ushort GetPxi3022DeviceId(DeviceBase device)
        {
            try
            {
                if (device == null)
                    return 1;

                var pxiChassisService = ContainerLocator.Container?.Resolve(typeof(IPxiChassisService)) as IPxiChassisService;
                if (pxiChassisService == null)
                    return 1;

                var allChassis = pxiChassisService.GetAllChassis();
                if (allChassis == null)
                    return 1;

                var chassis = allChassis.FirstOrDefault(c => c?.Devices != null && c.Devices.Any(d => d?.Id == device.Id));
                if (chassis?.Devices == null)
                    return 1;

                var pxi3022Devices = chassis.Devices
                    .OfType<SwitchDevice>()
                    .Where(d => (d.Model ?? string.Empty).ToUpperInvariant().Contains("3022"))
                    .ToList();

                int index = pxi3022Devices.FindIndex(d => d.Id == device.Id);
                if (index >= 0)
                    return (ushort)(index + 1);

                return 1;
            }
            catch
            {
                return 1;
            }
        }

        private static string GetPxi2601ResourceName(DeviceBase device)
        {
            try
            {
                Debug.WriteLine($"[GetPxi2601ResourceName] Start, DeviceId={device?.Id}, Model={device?.Model}");

                if (device is not SwitchDevice currentSwitchDevice)
                {
                    Debug.WriteLine($"[GetPxi2601ResourceName] Device is not SwitchDevice");
                    return "Dev1";
                }

                if (currentSwitchDevice.SlotIndex <= 0)
                {
                    Debug.WriteLine($"[GetPxi2601ResourceName] SlotIndex <= 0: {currentSwitchDevice.SlotIndex}");
                    return "Dev1";
                }

                var mapped = TryMapSlotIndexToDevAddress(currentSwitchDevice.SlotIndex);
                Debug.WriteLine($"[GetPxi2601ResourceName] SlotIndex={currentSwitchDevice.SlotIndex}, Mapped={mapped ?? "null"}");
                if (!string.IsNullOrWhiteSpace(mapped))
                    return mapped;

                var pxiChassisService = ContainerLocator.Container?.Resolve(typeof(IPxiChassisService)) as IPxiChassisService;
                Debug.WriteLine($"[GetPxi2601ResourceName] PxiChassisService resolved: {pxiChassisService != null}");
                if (pxiChassisService == null)
                    return $"Dev{currentSwitchDevice.SlotIndex + 2}";

                var allChassis = pxiChassisService.GetAllChassis();
                Debug.WriteLine($"[GetPxi2601ResourceName] AllChassis count: {allChassis?.Count ?? 0}");
                if (allChassis == null)
                    return $"Dev{currentSwitchDevice.SlotIndex + 2}";

                var chassis = allChassis.FirstOrDefault(c => c?.Devices != null && c.Devices.Any(d => d?.Id == device.Id));
                Debug.WriteLine($"[GetPxi2601ResourceName] Found chassis: {chassis?.Name}, Devices count: {chassis?.Devices?.Count ?? 0}");
                if (chassis?.Devices == null)
                    return $"Dev{currentSwitchDevice.SlotIndex + 2}";

                var pxi2601Cards = chassis.Devices
                    .OfType<SwitchDevice>()
                    .Where(d => (d.Model ?? string.Empty).ToUpperInvariant().Contains("2601"))
                    .Where(d => d.SlotIndex > 0)
                    .OrderBy(d => d.SlotIndex)
                    .ThenBy(d => d.Id)
                    .ToList();

                Debug.WriteLine($"[GetPxi2601ResourceName] PXI2601 cards count: {pxi2601Cards.Count}");
                foreach (var card in pxi2601Cards)
                {
                    Debug.WriteLine($"[GetPxi2601ResourceName] Card: ID={card.Id}, Model={card.Model}, SlotIndex={card.SlotIndex}");
                }

                int index = pxi2601Cards.FindIndex(d => d.Id == device.Id);
                Debug.WriteLine($"[GetPxi2601ResourceName] FindIndex result: {index}, Target DeviceId={device.Id}");
                if (index < 0)
                {
                    var result = $"Dev{currentSwitchDevice.SlotIndex + 2}";
                    Debug.WriteLine($"[GetPxi2601ResourceName] Using fallback: {result}");
                    return result;
                }

                var finalResult = $"Dev{3 + index}";
                Debug.WriteLine($"[GetPxi2601ResourceName] Final result: {finalResult}");
                return finalResult;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GetPxi2601ResourceName] Exception: {ex.Message}");
                return "Dev1";
            }
        }

        /// <summary>
        /// 创建设备驱动（仅真实硬件，不支持模拟）
        /// </summary>
        /// <param name="device">设备实例</param>
        /// <param name="useSimulation">已废弃，保留兼容性</param>
        /// <param name="simulationConfig">已废弃，保留兼容性</param>
        /// <returns>设备驱动实例</returns>
        public static IDeviceDriver CreateDriver(
            DeviceBase device,
            bool useSimulation = false,
            SimulationConfig simulationConfig = null)
        {
            if (device == null) throw new ArgumentNullException(nameof(device));

            string cacheKey = GetCacheKey(device);

            // 从缓存中获取
            lock (_lock)
            {
                if (_driverCache.ContainsKey(cacheKey))
                {
                    Debug.WriteLine($"[DriverFactory] 从缓存返回驱动: {cacheKey}");
                    return _driverCache[cacheKey];
                }
            }

            var driver = CreateRealDriver(device);
            if (driver != null)
            {
                lock (_lock)
                {
                    _driverCache[cacheKey] = driver;
                }
            }
            return driver;
        }

        private static IDeviceDriver CreateRealDriver(DeviceBase device)
        {
            // 优先按设备具体类型判断
            if (device is Mil1394BDevice) return new HZ1394BDriver(device);

            string model = device.Model?.ToUpperInvariant() ?? string.Empty;

            // JY7131 数字量输入输出板卡
            if (model.Contains("7131") || model.Contains("PXIE-7131"))
            {
                int slot = 0;
                if (device is DigitalIODevice dio) slot = dio.SlotIndex > 0 ? dio.SlotIndex : 0;
                Debug.WriteLine($"[DriverFactory] 创建 JY7131Driver Slot={slot}");
                return new JY7131Driver(device, slot);
            }

            // PXI4004 CAN 卡
            if (model.Contains("4004") || model.Contains("PXI-4004") || model.Contains("PXI4004"))
            {
                int slot = 0;
                if (device is CanBusDevice c) slot = c.SlotIndex > 0 ? c.SlotIndex : 0;
                else if (device is PxiDeviceBase p) slot = p.SlotIndex > 0 ? p.SlotIndex : 0;
                else if (!string.IsNullOrEmpty(device.ConnectionMethod) && device.ConnectionMethod.StartsWith("Slot"))
                    int.TryParse(device.ConnectionMethod.Substring(4), out slot);
                Debug.WriteLine($"[DriverFactory] 创建 PXI4004Driver Slot={slot}");
                return new PXI4004Driver(device, slot);
            }

            // ARINC429 (ART4229 / PXIE-4227 / PXIE-4229)
            if (model.Contains("4227") || model.Contains("4229") || 
                model.Contains("PXIE-4227") || model.Contains("PXIE-4229") || 
                model.Contains("ART4229") || model.Contains("429") || 
                model.Contains("ARINC") || model.Contains("PXI-429"))
            {
                int slot = 0;
                if (device is Arinc429Device a) slot = a.SlotIndex > 0 ? a.SlotIndex : 0;
                else if (device is PxiDeviceBase p) slot = p.SlotIndex > 0 ? p.SlotIndex : 0;
                else if (!string.IsNullOrEmpty(device.ConnectionMethod) && device.ConnectionMethod.StartsWith("Slot"))
                    int.TryParse(device.ConnectionMethod.Substring(4), out slot);
                Debug.WriteLine($"[DriverFactory] 创建 ART422Driver Slot={slot}");
                return new ART4229Driver(device, slot);
            }

            // ArtSwitch / PXI-2601 矩阵开关
            if (model.Contains("2601") || model.Contains("PXI-2601") || model.Contains("矩阵开关"))
            {
                // 调试：清除PXI-2601缓存以确保使用最新的映射逻辑
                ClearPxi2601Cache();

                // 从设备的 SlotIndex 属性获取插槽号
                int slotNumber = 0;
                if (device is SwitchDevice switchDevice)
                {
                    slotNumber = switchDevice.SlotIndex > 0 ? switchDevice.SlotIndex : 0;
                    Debug.WriteLine($"[DriverFactory] 创建 ArtSwitchDriver, DeviceId={device.Id}, SlotIndex={slotNumber}");
                }

                string resourceName = GetPxi2601ResourceName(device);
                var driver = new ArtSwitchDriver(device, resourceName, slotNumber);

                return driver;
            }

            // PXI-3022 矩阵开关
            if (model.Contains("3022") || model.Contains("PXI-3022") || model.Contains("PXI3022"))
            {
                ushort deviceId = GetPxi3022DeviceId(device);
                var driver = new PXI3022.PXI3022Driver(device, deviceId);
                return driver;
            }

            // MTX 系列
            if (model.Contains("MT-X532") || model.Contains("X532"))
            {
                Debug.WriteLine($"[DriverFactory] 创建 MTX532Driver");
                return new MTX532Driver(device, suppressNativeDialogs: true);
            }
            if (model.Contains("MT-X970") || model.Contains("X970"))
            {
                Debug.WriteLine($"[DriverFactory] 创建 MTX970LvdsDriver");
                return new MTX970LvdsDriver(device);
            }

            // PXIe-9774
            if (model.Contains("9774") || model.Contains("PXIE-9774") || model.Contains("PXI-9774"))
            {
                Debug.WriteLine($"[DriverFactory] 创建 Art9774Driver");
                return new Art9774Driver(device);
            }

            // ACTS6010 可编程电阻设备（对应程控电阻板卡）
            if (device is ProgrammableResistorDevice resistorDevice)
            {
                //// 记录该板卡的SlotIndex
                //lock (_lock)
                //{
                //    _resistorSlotIndices.Add(resistorDevice.SlotIndex);
                //}

                //// 根据SlotIndex在所有电阻板卡中的排序位置分配logicalID
                //// 槽位号最小的 → logicalID = 0，次小的 → logicalID = 1
                //
                //lock (_lock)
                //{
                //    var sortedSlots = _resistorSlotIndices.ToList();
                //    int index = sortedSlots.IndexOf(resistorDevice.SlotIndex);
                //    logicalId = (UInt32)(index == 0 ? 1 : 0);
                UInt32 logicalId;
                switch (resistorDevice.SlotIndex)
                {
                    case 5:
                        logicalId = 1;
                        break;
                    case 6:
                        logicalId = 0;
                        break;
                    default:
                        logicalId = 0;
                        break;
                }
            

                System.Diagnostics.Debug.WriteLine(
                    $"[DriverFactory] 创建 ACTS6010Driver, DeviceId={device.Id}, SlotIndex={resistorDevice.SlotIndex}, LogicalId={logicalId}");

                return new ACTS6010Driver(device, logicalId);
            }


            // MIL-STD-1553B (ART1553B)
            if (device is Mil1553BDevice || model.Contains("1553B") || model.Contains("阿尔泰PXI-4332") || model.Contains("PXI-4332") ||
                model.Contains("4332"))
            {
                int slotNumber = 0;
                uint serialNumber = 0;

                if (device is Mil1553BDevice mil1553bDevice)
                {
                    // 从 Mil1553BDevice 获取 SlotIndex（如果存在）
                    slotNumber = mil1553bDevice.SlotIndex > 0 ? mil1553bDevice.SlotIndex : 0;

                    // 尝试从设备配置中获取序列号
                    // 如果设备有序列号属性，可以从CardConfigData或其他配置中获取
                    // 这里先使用默认值，实际使用时需要根据设备配置获取
                    serialNumber = GetSerialNumberFromDevice(mil1553bDevice);

                    System.Diagnostics.Debug.WriteLine($"[DriverFactory] 创建 ART1553BDriver, DeviceId={device.Id}, Model={device.Model}, SlotIndex={slotNumber}, SerialNumber={serialNumber}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[DriverFactory] 创建 ART1553BDriver, DeviceId={device.Id}, Model={device.Model}");
                    // 尝试从系统中获取设备列表，找到第一个可用设备
                    serialNumber = GetFirstAvailableSerialNumber();
                }

                // 如果序列号为0，使用默认序列号
                if (serialNumber == 0)
                {
                    serialNumber = 1020404001; // 默认序列号，实际使用时需要从设备配置中获取
                    System.Diagnostics.Debug.WriteLine($"[DriverFactory] 使用默认序列号: {serialNumber}");
                }

                return new ART1553BDriver(device, serialNumber, slotNumber);
            }

            // 其他型号的驱动可以在这里添加
            // if (model.Contains("OTHER_MODEL"))
            // {
            //     return new OtherDriver(device);
            // }

            // 未识别的设备类型，暂时跳过
            Debug.WriteLine($"[DriverFactory] 未找到驱动，跳过设备: {device.Name}, Model={model}, Type={device.GetType().Name}");
            return null;
        }

        /// <summary>
        /// 从设备配置中获取序列号
        /// </summary>
        private static uint GetSerialNumberFromDevice(Mil1553BDevice device)
        {
            // TODO: 从设备配置中获取序列号
            // 可以从CardConfigData或其他配置属性中获取
            // 目前返回0，表示使用默认值
            return 0;
        }

        /// <summary>
        /// 获取系统中第一个可用的1553B设备序列号
        /// </summary>
        private static uint GetFirstAvailableSerialNumber()
        {
            try
            {
                // 获取设备列表
                ART1553B.ART1553B_DEV_INFO[] devInfo = new ART1553B.ART1553B_DEV_INFO[32];
                byte deviceCount = 0;

                int ret = ART1553B.ART1553B_DeviceList(devInfo, 32, ref deviceCount);
                if (ret == ART1553B.ART1553Success && deviceCount > 0)
                {
                    // 找到第一个未使用的设备
                    for (int i = 0; i < deviceCount; i++)
                    {
                        if (devInfo[i].bUsed == 0)
                        {
                            Debug.WriteLine($"[DriverFactory] 找到可用设备，序列号: {devInfo[i].nSerialCode}");
                            return devInfo[i].nSerialCode;
                        }
                    }

                    // 如果所有设备都被使用，返回第一个设备的序列号
                    if (deviceCount > 0)
                    {
                        Debug.WriteLine($"[DriverFactory] 所有设备都被使用，使用第一个设备序列号: {devInfo[0].nSerialCode}");
                        return devInfo[0].nSerialCode;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DriverFactory] 获取设备列表失败: {ex.Message}");
            }

            return 0;
        }

        // 工厂辅助方法（便于外部直接创建特定驱动）
        public static JY7131Driver CreateJY7131Driver(DeviceBase device, int slotNumber = 0)
        {
            if (device == null) throw new ArgumentNullException(nameof(device));
            return new JY7131Driver(device, slotNumber);
        }

        public static PXI4004Driver CreatePXI4004Driver(DeviceBase device, int slotNumber = 0)
        {
            if (device == null) throw new ArgumentNullException(nameof(device));
            return new PXI4004Driver(device, slotNumber);
        }

        public static ArtSwitchDriver CreateArtSwitchDriver(DeviceBase device, string address = "Dev1")
        {
            // 补充方法体：校验参数 + 创建实例
            if (device == null)
                throw new ArgumentNullException(nameof(device));

            if (address == "Dev1" && device is SwitchDevice switchDevice)
            {
                var mapped = TryMapSlotIndexToDevAddress(switchDevice.SlotIndex);
                if (!string.IsNullOrWhiteSpace(mapped))
                {
                    address = mapped;
                }

                if (!string.IsNullOrWhiteSpace(device.Model) && device.Model.ToUpperInvariant().Contains("2601"))
                {
                    address = GetPxi2601ResourceName(device);
                }
            }

            // 给ArtSwitchDriver传参并返回
            return new ArtSwitchDriver(device, address);
        }

        public static HZ1394BDriver CreateHZ1394BDriver(DeviceBase device)
        {
            if (device == null) throw new ArgumentNullException(nameof(device));
            return new HZ1394BDriver(device);
        }

        /// <summary>
        /// 获取缓存的驱动实例
        /// </summary>
        /// <param name="deviceId">设备ID</param>
        /// <param name="slotIndex">槽位索引（可选，用于区分同一类型的不同板卡）</param>
        /// <returns>驱动实例，未找到返回null</returns>
        public static IDeviceDriver GetCachedDriver(string deviceId, int slotIndex = -1)
        {
            if (string.IsNullOrEmpty(deviceId))
                return null;

            // 尝试多个可能的缓存键
            string[] possibleKeys = slotIndex >= 0
                ? new[] { $"{deviceId}_Slot{slotIndex}", deviceId }
                : new[] { deviceId };

            lock (_lock)
            {
                foreach (var key in possibleKeys)
                {
                    if (_driverCache.ContainsKey(key))
                    {
                        return _driverCache[key];
                    }
                }
                return null;
            }
        }

        public static bool RemoveCachedDriver(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId)) return false;
            lock (_lock)
            {
                bool removed = false;

                removed |= _driverCache.Remove(deviceId);

                var keysToRemove = _driverCache.Keys
                    .Where(k => k != null && k.StartsWith(deviceId + "_Slot", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                for (int i = 0; i < keysToRemove.Count; i++)
                {
                    removed |= _driverCache.Remove(keysToRemove[i]);
                }

                return removed;
            }
        }

        public static void ClearCache()
        {
            lock (_lock) { _driverCache.Clear(); }
        }

        /// <summary>
        /// 调试方法：清除PXI-2601设备的缓存（用于测试映射修改）
        /// </summary>
        public static void ClearPxi2601Cache()
        {
            lock (_lock)
            {
                var keysToRemove = _driverCache.Keys.Where(k => k.Contains("2601") || k.Contains("矩阵开关")).ToList();
                foreach (var key in keysToRemove)
                {
                    _driverCache.Remove(key);
                    Debug.WriteLine($"[DriverFactory] 清除PXI-2601缓存: {key}");
                }
            }
        }

        public static IReadOnlyList<IDeviceDriver> GetCachedDrivers()
        {
            lock (_lock)
            {
                return _driverCache.Values.ToList();
            }
        }

        public static async Task ShutdownAllAsync()
        {
            List<IDeviceDriver> drivers;
            lock (_lock)
            {
                drivers = _driverCache.Values.ToList();
            }

            foreach (var driver in drivers)
            {
                if (driver == null)
                {
                    continue;
                }

                try
                {
                    if (driver.IsConnected)
                    {
                        await driver.StopAcquisitionAsync();
                        await driver.DisconnectAsync();
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[DriverFactory] ShutdownAllAsync error: {ex.Message}");
                }
            }

            ClearCache();
        }
        public static int GetCachedDriverCount()
        {
            lock (_lock) { return _driverCache.Count; }
        }

        /// <summary>
        /// 生成设备的缓存键
        /// </summary>
        /// <param name="device">设备实例</param>
        /// <returns>缓存键</returns>
        private static string GetCacheKey(DeviceBase device)
        {
            if (device == null)
                return string.Empty;

            // 对于PXI设备，使用设备ID和槽位信息的组合作为缓存键
            if (device is PxiDeviceBase pxiDevice)
            {
                return $"{device.Id}_Slot{pxiDevice.SlotIndex}";
            }

            // 对于其他设备，使用设备ID作为缓存键
            return device.Id;
        }

        /// <summary>
        /// 检查设备是否有缓存的驱动
        /// </summary>
        /// <param name="deviceId">设备ID</param>
        /// <returns>是否有缓存</returns>
        public static bool HasCachedDriver(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId)) return false;
            lock (_lock) { return _driverCache.ContainsKey(deviceId); }
        }
    }
}
