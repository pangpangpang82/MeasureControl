using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MeasureControl.Drivers;
using MeasureControl.Helpers;
using MeasureControl.Models.Devices;
using MeasureControl.Models.Devices.DeviceCategories;

namespace MeasureControl.Helpers.SelfInspection
{
    internal sealed class PXIe7131DidoSelfInspectionTask : ISelfInspectionTask
    {
        private const string ThresholdComPort = "COM14"; // 第一套
        //private const string ThresholdComPort = "COM10"; // 第二套
        //private const string ThresholdComPort = "COM8"; // 第三套
        private const int ThresholdBaudRate = 115200;
        private const double ThresholdVoltage = 10.0;
        private const double PowerVoltage = 32.0;

        private static readonly IReadOnlyList<string> AllDiChannels = Enumerable.Range(0, 32).Select(i => $"DI{i}").ToList();
        private static readonly IReadOnlyList<string> AllDoChannels = Enumerable.Range(0, 32).Select(i => $"DO{i}").ToList();

        private static Dictionary<string, double> BuildDoMap(double value)
        {
            var map = new Dictionary<string, double>(capacity: 32, comparer: StringComparer.OrdinalIgnoreCase);
            foreach (var ch in AllDoChannels)
            {
                map[ch] = value;
            }
            return map;
        }

        private static string FormatBits(Dictionary<string, double> values, string prefix)
        {
            if (values == null) return string.Empty;

            var bits = new char[32];
            for (int i = 0; i < 32; i++)
            {
                var key = $"{prefix}{i}";
                bits[i] = (values.TryGetValue(key, out var v) && v != 0) ? '1' : '0';
            }

            return new string(bits);
        }

        private static async Task RecordOnceAsync(IDeviceDriver driver, SelfInspectionContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var di = await driver.ReadChannelsBatchAsync(AllDiChannels);
            var doVals = await driver.ReadChannelsBatchAsync(AllDoChannels);

            context.Log($"DI值：{FormatBits(di, "DI")}");
            context.Log($"DO值：{FormatBits(doVals, "DO")}");
        }

        private static int GetSlotIndex(DeviceBase device)
        {
            if (device is PxiDeviceBase pxi)
            {
                return pxi.SlotIndex;
            }
            return -1;
        }

        private static bool Is7131(DeviceBase device)
        {
            var model = (device?.Model ?? string.Empty).ToUpperInvariant();
            return model.Contains("7131") || model.Contains("PXIE-7131");
        }

        private static void ApplyThresholds10V()
        {
            using var cli = new DacGroupsSerialClient(ThresholdComPort, ThresholdBaudRate, dtrEnable: false, rtsEnable: false);
            cli.Send8Groups(
                ThresholdVoltage,
                ThresholdVoltage,
                ThresholdVoltage,
                ThresholdVoltage,
                ThresholdVoltage,
                ThresholdVoltage,
                ThresholdVoltage,
                ThresholdVoltage);
        }

        private static Task ApplyPower32VAsync(IDeviceDriver driver)
        {
            if (driver is JY7131Driver jy)
            {
                return jy.EnsurePowerOutputsAsync(PowerVoltage, PowerVoltage, PowerVoltage, PowerVoltage);
            }

            return Task.CompletedTask;
        }

        public bool CanHandle(DeviceBase device)
        {
            return device != null && Is7131(device);
        }

        public async Task RunAsync(DeviceBase device, SelfInspectionContext context, CancellationToken cancellationToken)
        {
            if (device == null) throw new ArgumentNullException(nameof(device));
            if (context == null) throw new ArgumentNullException(nameof(context));

            if (!Is7131(device))
            {
                context.Log($"跳过：非7131板卡 {device.Name} Model={device.Model}");
                return;
            }

            var slotIndex = GetSlotIndex(device);
            var cached = DriverFactory.GetCachedDriver(device.Id, slotIndex);
            if (cached != null && cached.IsConnected)
            {
                context.Log("检测到板卡已连接，取消自检以避免影响面板。");
                throw new InvalidOperationException("板卡已连接，无法自检。");
            }

            var driver = DriverFactory.CreateDriver(device);
            if (driver == null)
            {
                context.Log($"未找到驱动，无法自检：{device.Name} Model={device.Model}");
                return;
            }

            bool acquisitionStarted = false;
            bool connected = false;

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                context.Log("连接板卡 PXIe-7131");
                var connectOk = await driver.ConnectAsync();
                connected = connectOk && driver.IsConnected;
                if (!connected)
                {
                    context.Log("连接板卡 PXIe-7131 失败");
                    throw new InvalidOperationException("连接板卡 PXIe-7131 失败");
                }
                context.Log("连接板卡 PXIe-7131 成功");

                if (driver is JY7131Driver jy7131)
                {
                    context.Log("设置DO输出模式：Sinking");
                    var modeOk = await jy7131.ReconfigureDoOutputModeAsync("Sinking");
                    if (!modeOk)
                    {
                        context.Log("设置DO输出模式失败");
                        throw new InvalidOperationException("设置DO输出模式失败");
                    }
                    context.Log("设置DO输出模式 成功");
                }

                context.Log("初始化 PXIe-7131 参数");
                try
                {
                    using (await SerialPortMutex.AcquireAsync(ThresholdComPort))
                    {
                        ApplyThresholds10V();
                    }

                    context.Log("设置输入阈值 成功");
                }
                catch (Exception ex)
                {
                    context.Log($"设置输入阈值失败：{ex.Message}");
                }

                try
                {
                    await ApplyPower32VAsync(driver);

                    context.Log("设置输出电压 成功");
                }
                catch (Exception ex)
                {
                    context.Log($"设置输出电压失败：{ex.Message}");
                }

                context.Log("开始采集和输出");
                var started = await driver.StartAcquisitionAsync();
                if (!started)
                {
                    context.Log("开始采集和输出 失败");
                    throw new InvalidOperationException("开始采集和输出失败");
                }

                acquisitionStarted = true;
                context.Log("开始采集和输出 成功");

                context.Log("复位所有DO");
                var write0Ok = await driver.WriteChannelsBatchAsync(BuildDoMap(0));
                if (!write0Ok)
                {
                    context.Log("复位所有DO 失败");
                    throw new InvalidOperationException("复位所有DO失败");
                }

                context.Log("复位所有DO 成功");

                await RecordOnceAsync(driver, context, cancellationToken);

                context.Log("开启所有DO");
                var write1Ok = await driver.WriteChannelsBatchAsync(BuildDoMap(1));
                if (!write1Ok)
                {
                    context.Log("开启所有DO 失败");
                    throw new InvalidOperationException("开启所有DO失败");
                }

                context.Log("开启所有DO 成功");

                await Task.Delay(150, cancellationToken);

                await RecordOnceAsync(driver, context, cancellationToken);
            }
            finally
            {
                if (connected)
                {
                    try
                    {
                        context.Log("停止采集和输出");
                        if (driver.IsConnected)
                        {
                            try { await driver.WriteChannelsBatchAsync(BuildDoMap(0)); } catch { }
                            if (acquisitionStarted)
                            {
                                try { await driver.StopAcquisitionAsync(); } catch { }
                            }

                            if (driver is JY7131Driver jy)
                            {
                                try { await jy.StopPowerOutputAsync(); } catch { }
                            }
                        }
                    }
                    catch
                    {
                    }
                }

                if (connected)
                {
                    try
                    {
                        context.Log("断开板卡 PXIe-7131");
                        if (driver.IsConnected)
                        {
                            try { await driver.DisconnectAsync(); } catch { }
                        }
                    }
                    catch
                    {
                    }
                }
            }
        }
    }
}
