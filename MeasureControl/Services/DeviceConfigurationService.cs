using System;
using System.Diagnostics;
using System.Threading.Tasks;
using MeasureControl.Drivers;
using MeasureControl.Models.Devices;
using MeasureControl.Views.Dialogs;

namespace MeasureControl.Services
{
    /// <summary>
    /// 设备配置服务
    /// 负责执行各种设备的配置加载逻辑
    /// </summary>
    public class DeviceConfigurationService
    {
        /// <summary>
        /// 执行设备特定的配置
        /// </summary>
        /// <param name="device">设备实例</param>
        /// <param name="driver">设备驱动</param>
        /// <returns>配置任务</returns>
        public async Task ExecuteDeviceConfiguration(DeviceBase device, IDeviceDriver driver)
        {
            // 根据设备名称或型号直接识别设备类型并执行对应配置
            string deviceName = device.Name?.ToLower() ?? "";
            string deviceModel = device.Model?.ToLower() ?? "";

            // Art9774 模拟量采集卡
            if (deviceModel.Contains("art9774") || deviceName.Contains("art9774"))
            {
                await ConfigureArt9774Device(device, driver);
            }
            // JY7131 数字I/O卡
            else if (deviceModel.Contains("jy7131") || deviceName.Contains("jy7131"))
            {
                await ConfigureJY7131Device(device, driver);
            }
            // MTX532 模拟量输出卡
            else if (deviceModel.Contains("mtx532") || deviceName.Contains("mtx532"))
            {
                await ConfigureMTX532Device(device, driver);
            }
            // MTX970 LVDS通信卡
            else if (deviceModel.Contains("mtx970") || deviceName.Contains("mtx970"))
            {
                await ConfigureMTX970Device(device, driver);
            }
            // HZ1394B 1394B通信卡
            else if (deviceModel.Contains("hz1394b") || deviceName.Contains("hz1394b"))
            {
                await ConfigureHZ1394BDevice(device, driver);
            }
            // ArtSwitch 网络切换系统
            else if (deviceModel.Contains("artswitch") || deviceName.Contains("artswitch"))
            {
                await ConfigureArtSwitchDevice(device, driver);
            }
            // ACTS6010 可编程电阻
            else if (deviceModel.Contains("acts6010") || deviceName.Contains("acts6010"))
            {
                await ConfigureACTS6010Device(device, driver);
            }
            // 其他设备
            else
            {
                await ConfigureGenericDevice(device, driver);
            }
        }

        /// <summary>
        /// 配置Art9774模拟量采集卡
        /// </summary>
        private async Task ConfigureArt9774Device(DeviceBase device, IDeviceDriver driver)
        {
            Debug.WriteLine($"[DeviceConfigurationService] TODO: 配置Art9774设备 {device.Name}");

            // TODO: 实现Art9774设备的连接和配置逻辑
            // 例如：连接设备、设置采集参数、配置通道等
            var connected = await driver.ConnectAsync();
            if (!connected)
            {
                System.Diagnostics.Debug.WriteLine($"[DeviceConfigurationService] Art9774设备连接失败，跳过 {device?.Name}");
                ReMessageBox.Show(
                    $"Art9774板卡连接失败，请检查板卡及驱动",
                    "连接失败",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
                return;
            }

            await Task.CompletedTask;
        }

        /// <summary>
        /// 配置JY7131数字I/O卡
        /// </summary>
        private async Task ConfigureJY7131Device(DeviceBase device, IDeviceDriver driver)
        {
            Debug.WriteLine($"[DeviceConfigurationService] TODO: 配置JY7131设备 {device.Name}");

            // TODO: 实现JY7131设备的连接和配置逻辑
            // 例如：连接设备、设置I/O方向、配置通道等
            var connected = await driver.ConnectAsync();
            if (!connected)
            {
                System.Diagnostics.Debug.WriteLine($"[DeviceConfigurationService] JY7131设备连接失败，跳过 {device?.Name}");
                ReMessageBox.Show(
                    $"JY7131板卡连接失败，请检查板卡及驱动",
                    "连接失败",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
                return;
            }

            await Task.CompletedTask;
        }

        /// <summary>
        /// 配置MTX532模拟量输出卡
        /// </summary>
        private async Task ConfigureMTX532Device(DeviceBase device, IDeviceDriver driver)
        {
            Debug.WriteLine($"[DeviceConfigurationService] TODO: 配置MTX532设备 {device.Name}");

            // TODO: 实现MTX532设备的连接和配置逻辑
            // 例如：连接设备、设置输出参数、配置通道等
            var connected = await driver.ConnectAsync();
            if (!connected)
            {
                System.Diagnostics.Debug.WriteLine($"[DeviceConfigurationService] MTX532设备连接失败，跳过 {device?.Name}");
                ReMessageBox.Show(
                    $"MTX532板卡连接失败，请检查板卡及驱动",
                    "连接失败",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
                return;
            }

            await Task.CompletedTask;
        }

        /// <summary>
        /// 配置MTX970 LVDS通信卡
        /// </summary>
        private async Task ConfigureMTX970Device(DeviceBase device, IDeviceDriver driver)
        {
            Debug.WriteLine($"[DeviceConfigurationService] TODO: 配置MTX970设备 {device.Name}");

            // TODO: 实现MTX970设备的连接和配置逻辑
            // 例如：连接设备、设置通信参数、配置数据格式等
            var connected = await driver.ConnectAsync();
            if (!connected)
            {
                System.Diagnostics.Debug.WriteLine($"[DeviceConfigurationService] MTX970设备连接失败，跳过 {device?.Name}");
                ReMessageBox.Show(
                    $"MTX970板卡连接失败，请检查板卡及驱动",
                    "连接失败",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
                return;
            }

            await Task.CompletedTask;
        }

        /// <summary>
        /// 配置HZ1394B 1394B通信卡
        /// </summary>
        private async Task ConfigureHZ1394BDevice(DeviceBase device, IDeviceDriver driver)
        {
            Debug.WriteLine($"[DeviceConfigurationService] TODO: 配置HZ1394B设备 {device.Name}");

            // TODO: 实现HZ1394B设备的连接和配置逻辑
            // 例如：连接设备、设置通信参数、配置协议等
            var connected = await driver.ConnectAsync();
            if (!connected)
            {
                System.Diagnostics.Debug.WriteLine($"[DeviceConfigurationService] HZ1394B设备连接失败，跳过 {device?.Name}");
                ReMessageBox.Show(
                    $"HZ1394B板卡连接失败，请检查板卡及驱动",
                    "连接失败",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
                return;
            }

            await Task.CompletedTask;
        }

        /// <summary>
        /// 配置ArtSwitch网络切换系统
        /// </summary>
        private async Task ConfigureArtSwitchDevice(DeviceBase device, IDeviceDriver driver)
        {
            Debug.WriteLine($"[DeviceConfigurationService] TODO: 配置ArtSwitch设备 {device.Name}");

            // TODO: 实现ArtSwitch设备的连接和配置逻辑
            // 例如：连接设备、设置网络参数、配置切换逻辑等
            var connected = await driver.ConnectAsync();
            if (!connected)
            {
                System.Diagnostics.Debug.WriteLine($"[DeviceConfigurationService] ArtSwitch设备连接失败，跳过 {device?.Name}");
                ReMessageBox.Show(
                    $"ArtSwitch板卡连接失败，请检查板卡及驱动",
                    "连接失败",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
                return;
            }

            await Task.CompletedTask;
        }

        /// <summary>
        /// 配置ACTS6010可编程电阻
        /// </summary>
        private async Task ConfigureACTS6010Device(DeviceBase device, IDeviceDriver driver)
        {
            Debug.WriteLine($"[DeviceConfigurationService] TODO: 配置ACTS6010设备 {device.Name}");

            // TODO: 实现ACTS6010设备的连接和配置逻辑
            // 例如：连接设备、设置电阻参数、配置通道等
            var connected = await driver.ConnectAsync();
            if (!connected)
            {
                System.Diagnostics.Debug.WriteLine($"[DeviceConfigurationService] ACTS6010设备连接失败，跳过 {device?.Name}");
                ReMessageBox.Show(
                    $"ACTS6010板卡连接失败，请检查板卡及驱动",
                    "连接失败",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
                return;
            }

            await Task.CompletedTask;
        }

        /// <summary>
        /// 配置通用设备
        /// </summary>
        private async Task ConfigureGenericDevice(DeviceBase device, IDeviceDriver driver)
        {
            Debug.WriteLine($"[DeviceConfigurationService] TODO: 配置通用设备 {device.Name}");

            // TODO: 实现通用设备的连接和配置逻辑
            // 对于未识别的设备类型，提供基本的连接功能
            var connected = await driver.ConnectAsync();
            if (!connected)
            {
                System.Diagnostics.Debug.WriteLine($"[DeviceConfigurationService] 通用设备连接失败，跳过 {device?.Name}");
                ReMessageBox.Show(
                    $"{device?.Name ?? "未知设备"}连接失败，请检查板卡及驱动",
                    "连接失败",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
                return;
            }

            await Task.CompletedTask;
        }

        /// <summary>
        /// 根据矩阵配置获取开关拓扑
        /// </summary>
        public string GetSwitchTopology(string matrixConfiguration)
        {
            // 根据矩阵配置返回对应的拓扑字符串
            if (string.IsNullOrEmpty(matrixConfiguration))
                return "DEFAULT_TOPOLOGY";

            if (matrixConfiguration.Contains("4×64") || matrixConfiguration.Contains("4x64"))
                return "4x64_MATRIX";
            else if (matrixConfiguration.Contains("8×32") || matrixConfiguration.Contains("8x32"))
                return "8x32_MATRIX";
            else if (matrixConfiguration.Contains("4×32") || matrixConfiguration.Contains("4x32"))
                return "4x32_MATRIX";
            else if (matrixConfiguration.Contains("8×16") || matrixConfiguration.Contains("8x16"))
                return "8x16_MATRIX";
            else
                return "DEFAULT_TOPOLOGY";
        }
    }
}
