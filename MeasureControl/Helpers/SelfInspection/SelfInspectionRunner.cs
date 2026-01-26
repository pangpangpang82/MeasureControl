using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MeasureControl.Models.Devices;
using MeasureControl.Models.Devices.DeviceCategories;
using MeasureControl.Services;

namespace MeasureControl.Helpers.SelfInspection
{
    internal static class SelfInspectionRunner
    {
        public static async Task RunChassisAsync(
            string chassisName,
            IPxiChassisService pxiChassisService,
            string logFilePath,
            Action<string> logToUi,
            CancellationToken cancellationToken,
            Action<int, string> reportProgress = null)
        {
            if (string.IsNullOrWhiteSpace(chassisName))
            {
                throw new ArgumentException("chassisName不能为空", nameof(chassisName));
            }

            if (string.IsNullOrWhiteSpace(logFilePath))
            {
                throw new ArgumentException("logFilePath不能为空", nameof(logFilePath));
            }

            if (pxiChassisService == null)
            {
                throw new ArgumentNullException(nameof(pxiChassisService));
            }

            // 覆盖模式：每次自检开始前先清空旧日志
            try
            {
                var dir = System.IO.Path.GetDirectoryName(logFilePath);
                if (!string.IsNullOrWhiteSpace(dir) && !System.IO.Directory.Exists(dir))
                {
                    System.IO.Directory.CreateDirectory(dir);
                }

                using (var fs = new System.IO.FileStream(logFilePath, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.ReadWrite))
                {
                }
            }
            catch
            {
            }

            var ctx = new SelfInspectionContext(chassisName, logFilePath, logToUi);

            void LogChassisBanner(string title)
            {
                ctx.Log("=========================");
                ctx.Log(title);
                ctx.Log("=========================");
            }

            void LogDeviceSection(string title)
            {
                ctx.Log("---------------------------------------------------------------------------");
                ctx.Log(title);
                ctx.Log("---------------------------------------------------------------------------");
            }

            void LogDeviceSectionEnd()
            {
                ctx.Log("---------------------------------------------------------------------------");
            }

            LogChassisBanner($"开始自检，机箱：{chassisName}");

            // 优先使用“设备序列”（chassisDevice.Children）作为权威执行顺序
            var chassis = pxiChassisService.GetAllChassis()?.FirstOrDefault(c =>
                string.Equals(c?.Name, chassisName, StringComparison.OrdinalIgnoreCase));

            List<DeviceBase> orderedCards = null;
            try
            {
                var chassisDevice = chassis?.Devices?.FirstOrDefault(d =>
                    string.Equals(d?.DeviceType, "Chassis", StringComparison.OrdinalIgnoreCase));

                if (chassisDevice?.Children != null && chassisDevice.Children.Count > 0)
                {
                    orderedCards = chassisDevice.Children
                        .Where(d => d != null)
                        .Where(d => string.Equals(d.DeviceType, "Card", StringComparison.OrdinalIgnoreCase))
                        .Where(d => d is not ControllerDevice)
                        .ToList();
                }
            }
            catch
            {
            }

            if (orderedCards == null)
            {
                var devices = pxiChassisService.GetChassisDevices(chassisName) ?? new List<DeviceBase>();
                orderedCards = devices
                    .Where(d => d != null)
                    .Where(d => string.Equals(d.DeviceType, "Card", StringComparison.OrdinalIgnoreCase))
                    .Where(d => d is not ControllerDevice)
                    .OrderBy(d => (d as PxiDeviceBase)?.SlotIndex ?? int.MaxValue)
                    .ThenBy(d => d.Name)
                    .ToList();
            }

            // 计算执行步骤数（9774+532算一个步骤）
            var totalSteps = CalculateTotalSteps(orderedCards);
            int stepIndex = 0;

            void UpdateProgress(string status)
            {
                if (totalSteps <= 0)
                {
                    reportProgress?.Invoke(0, status);
                    return;
                }

                int progress = (int)Math.Round((stepIndex * 100.0) / totalSteps);
                if (progress < 0) progress = 0;
                if (progress > 100) progress = 100;
                reportProgress?.Invoke(progress, status);
            }

            var processed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var device in orderedCards)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (string.Equals(device?.Name, "空槽", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (device?.Id != null && processed.Contains(device.Id))
                {
                    continue;
                }

                // 9774 + 532 成对自检
                if (Pxi9774Mtx532PairSelfInspection.Is9774Or532(device))
                {
                    var ai = Pxi9774Mtx532PairSelfInspection.Find9774(orderedCards);
                    var ao = Pxi9774Mtx532PairSelfInspection.Find532(orderedCards);

                    if (ai != null && ao != null)
                    {
                        try
                        {
                            stepIndex++;
                            UpdateProgress($"自检 模拟量(9774+532) ({stepIndex}/{totalSteps})");

                            LogDeviceSection($"开始设备自检：模拟量 9774+532 SlotAI={(ai as PxiDeviceBase)?.SlotIndex} SlotAO={(ao as PxiDeviceBase)?.SlotIndex}");
                            await Pxi9774Mtx532PairSelfInspection.RunPairAsync(ai, ao, ctx, cancellationToken);
                            ctx.Log("设备自检完成：模拟量 9774+532");
                        }
                        catch (OperationCanceledException)
                        {
                            ctx.Log("自检被取消");
                            throw;
                        }
                        catch (Exception ex)
                        {
                            ctx.Log($"设备自检异常：模拟量 9774+532，{ex.GetType().Name}: {ex.Message}");
                        }
                        finally
                        {
                            LogDeviceSectionEnd();
                        }

                        if (!string.IsNullOrWhiteSpace(ai.Id)) processed.Add(ai.Id);
                        if (!string.IsNullOrWhiteSpace(ao.Id)) processed.Add(ao.Id);
                        continue;
                    }

                    // 如果存在其中一块但缺另一块，记录并继续
                    if (ai != null || ao != null)
                    {
                        ctx.Log("跳过设备（模拟量 9774+532 未配对完整）：需要同时存在 9774 与 532");
                        if (!string.IsNullOrWhiteSpace(ai?.Id)) processed.Add(ai.Id);
                        if (!string.IsNullOrWhiteSpace(ao?.Id)) processed.Add(ao.Id);
                        continue;
                    }
                }

                stepIndex++;
                UpdateProgress($"自检 {device?.Name} ({stepIndex}/{totalSteps})");

                var task = SelfInspectionTaskRegistry.Resolve(device);
                if (task == null)
                {
                    LogDeviceSection($"开始设备自检：{device?.Name} Model={device?.Model} Slot={(device as PxiDeviceBase)?.SlotIndex}");
                    string typeName;
                    try { typeName = device?.DeviceTypeName; } catch { typeName = "<DeviceTypeName异常>"; }
                    ctx.Log($"跳过设备（未实现自检）：{device?.Name} Model={device?.Model} Slot={(device as PxiDeviceBase)?.SlotIndex}");
                    ctx.Log($"跳过原因信息：RuntimeType={device?.GetType().Name} ParentNode={device?.ParentNode} DeviceTypeName={typeName}");
                    LogDeviceSectionEnd();
                    continue;
                }

                try
                {
                    LogDeviceSection($"开始设备自检：{device?.Name} Model={device?.Model} Slot={(device as PxiDeviceBase)?.SlotIndex}");
                    await task.RunAsync(device, ctx, cancellationToken);
                    ctx.Log($"设备自检完成：{device?.Name}");

                    if (!string.IsNullOrWhiteSpace(device?.Id))
                    {
                        processed.Add(device.Id);
                    }
                }
                catch (OperationCanceledException)
                {
                    ctx.Log("自检被取消");
                    throw;
                }
                catch (Exception ex)
                {
                    ctx.Log($"设备自检异常：{device?.Name}，{ex.GetType().Name}: {ex.Message}");
                }
                finally
                {
                    LogDeviceSectionEnd();
                }
            }

            ctx.Log("机箱自检结束");
            ctx.Log("==================================================");
            reportProgress?.Invoke(100, "自检完成");
        }

        private static int CalculateTotalSteps(List<DeviceBase> orderedCards)
        {
            if (orderedCards == null || orderedCards.Count == 0)
            {
                return 0;
            }

            int steps = 0;
            bool has9774 = orderedCards.Any(Pxi9774Mtx532PairSelfInspection.Is9774);
            bool has532 = orderedCards.Any(Pxi9774Mtx532PairSelfInspection.Is532);

            foreach (var d in orderedCards)
            {
                if (d == null) continue;
                if (string.Equals(d.Name, "空槽", StringComparison.OrdinalIgnoreCase)) continue;
                if (Pxi9774Mtx532PairSelfInspection.Is9774Or532(d))
                {
                    continue; // 成对算一个步骤，下面统一加
                }

                steps++;
            }

            if (has9774 && has532)
            {
                steps++;
            }

            return steps;
        }
    }
}
