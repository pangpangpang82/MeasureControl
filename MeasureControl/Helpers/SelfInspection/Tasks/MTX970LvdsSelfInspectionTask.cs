using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MeasureControl.Drivers;
using MeasureControl.Models.Devices;
using MeasureControl.Models.Devices.DeviceCategories;

namespace MeasureControl.Helpers.SelfInspection
{
    internal sealed class MTX970LvdsSelfInspectionTask : ISelfInspectionTask
    {
        private const double ClockFrequencyHz = 45_000_000.0;
        private const ushort LvdsDataSampleWr = 1234;
        private const ushort PatternMatch = 1234;
        private const ushort NumSamples = 100;

        private static bool IsMtx970(DeviceBase device)
        {
            var model = (device?.Model ?? string.Empty).ToUpperInvariant();
            return model.Contains("MT-X970") || model.Contains("X970");
        }

        private static int GetSlotIndex(DeviceBase device)
        {
            if (device is PxiDeviceBase pxi)
            {
                return pxi.SlotIndex;
            }
            return -1;
        }

        public bool CanHandle(DeviceBase device)
        {
            return device != null && IsMtx970(device);
        }

        public async Task RunAsync(DeviceBase device, SelfInspectionContext context, CancellationToken cancellationToken)
        {
            if (device == null) throw new ArgumentNullException(nameof(device));
            if (context == null) throw new ArgumentNullException(nameof(context));

            if (!IsMtx970(device))
            {
                context.Log($"跳过：非MT-X970板卡 {device.Name} Model={device.Model}");
                return;
            }

            var slotIndex = GetSlotIndex(device);
            var cached = DriverFactory.GetCachedDriver(device.Id, slotIndex);
            if (cached != null && cached.IsConnected)
            {
                context.Log("检测到板卡已连接，取消自检以避免影响面板。原因：面板与自检共用驱动缓存。");
                throw new InvalidOperationException("板卡已连接，无法自检。");
            }

            var driver = DriverFactory.CreateDriver(device) as MTX970LvdsDriver;
            if (driver == null)
            {
                context.Log($"未找到驱动，无法自检：{device.Name} Model={device.Model}");
                return;
            }

            bool connected = false;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                context.Log("连接板卡 MT-X970（校验/加载 SharedLib.dll）");
                var connectOk = await driver.ConnectAsync();
                connected = connectOk && driver.IsConnected;
                if (!connected)
                {
                    context.Log("连接板卡 MT-X970 失败：请检查 SharedLib.dll 是否存在、LabVIEW Runtime、DLL 位数是否匹配");
                    throw new InvalidOperationException("连接板卡 MT-X970 失败");
                }

                context.Log("连接板卡 MT-X970 成功");

                // 如果槽位不可用，使用通配符（与面板固定值策略一致）
                var slotText = slotIndex > 0 ? slotIndex.ToString() : "*";

                _ = await driver.RunLoopbackAsync(
                    configOsc: true,
                    clockFrequencyHz: ClockFrequencyHz,
                    staticTCountUpF: true,
                    lvdsDataSampleWr: LvdsDataSampleWr,
                    patternMatch: PatternMatch,
                    numSamples: NumSamples,
                    devConditionModel: "*",
                    devConditionId: "*",
                    devConditionPxiSlot: slotText);

                cancellationToken.ThrowIfCancellationRequested();

                context.Log($"执行LVDS回环测试：Clock={ClockFrequencyHz}Hz SampleWr={LvdsDataSampleWr} Pattern={PatternMatch} NumSamples={NumSamples} Slot={slotText}");
                var result = await driver.RunLoopbackAsync(
                    configOsc: true,
                    clockFrequencyHz: ClockFrequencyHz,
                    staticTCountUpF: true,
                    lvdsDataSampleWr: LvdsDataSampleWr,
                    patternMatch: PatternMatch,
                    numSamples: NumSamples,
                    devConditionModel: "*",
                    devConditionId: "*",
                    devConditionPxiSlot: slotText);

                context.Log($"回环结果：ReturnCode={result.ReturnCode} IndexOfElement={result.IndexOfElement} TriggerSampleLocation={result.TriggerSampleLocation}");
                if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
                {
                    context.Log($"回环错误信息：{result.ErrorMessage}");
                }

                if (result.ArrayWSubsetDeleted != null && result.ArrayWSubsetDeleted.Length > 0)
                {
                    var preview = string.Join(",", result.ArrayWSubsetDeleted.Take(16).Select(v => v.ToString()));
                    context.Log($"回环数据预览(前16个)：{preview}");
                }

                if (result.ReturnCode != 0)
                {
                    throw new InvalidOperationException($"MT-X970 回环测试失败：ReturnCode={result.ReturnCode}");
                }

                context.Log("MT-X970 回环测试通过");
            }
            finally
            {
                if (connected)
                {
                    try
                    {
                        context.Log("断开板卡");
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
