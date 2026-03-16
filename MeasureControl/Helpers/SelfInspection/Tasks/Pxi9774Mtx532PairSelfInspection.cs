using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MeasureControl.Drivers;
using MeasureControl.Models.Devices;

namespace MeasureControl.Helpers.SelfInspection
{
    internal static class Pxi9774Mtx532PairSelfInspection
    {
        private const double AoSampleRate = 10000.0;
        private const double AiSampleRate = 10000.0;
        private const int AiBufferSamplesPerChannel = 1000;

        private static readonly IReadOnlyList<string> AllAoChannels = Enumerable.Range(0, 32).Select(i => $"AO{i}").ToList();
        private static readonly IReadOnlyList<string> AllAiChannels = Enumerable.Range(0, 32).Select(i => $"AI{i}").ToList();

        public static bool Is9774(DeviceBase device)
        {
            var model = (device?.Model ?? string.Empty).ToUpperInvariant();
            return model.Contains("9774") || model.Contains("PXIE-9774") || model.Contains("PXI-9774");
        }

        public static bool Is532(DeviceBase device)
        {
            var model = (device?.Model ?? string.Empty).ToUpperInvariant();
            return model.Contains("MT-X532") || model.Contains("X532") || model.Contains("532");
        }

        public static bool Is9774Or532(DeviceBase device) => Is9774(device) || Is532(device);

        public static DeviceBase Find9774(IEnumerable<DeviceBase> devices) => devices?.FirstOrDefault(Is9774);

        public static DeviceBase Find532(IEnumerable<DeviceBase> devices) => devices?.FirstOrDefault(Is532);

        public static async Task RunPairAsync(DeviceBase aiDevice, DeviceBase aoDevice, SelfInspectionContext context, CancellationToken cancellationToken)
        {
            if (aiDevice == null) throw new ArgumentNullException(nameof(aiDevice));
            if (aoDevice == null) throw new ArgumentNullException(nameof(aoDevice));
            if (context == null) throw new ArgumentNullException(nameof(context));

            var aoDriver = DriverFactory.CreateDriver(aoDevice) as MTX532Driver;
            var aiDriver = DriverFactory.CreateDriver(aiDevice) as Art9774Driver;

            if (aoDriver == null)
            {
                context.Log($"未找到 MT-X532 输出驱动：{aoDevice.Name} Model={aoDevice.Model}");
                throw new InvalidOperationException("未找到 532 输出驱动");
            }

            if (aiDriver == null)
            {
                context.Log($"未找到 PXI-9774 采集驱动：{aiDevice.Name} Model={aiDevice.Model}");
                throw new InvalidOperationException("未找到 9774 采集驱动");
            }

            bool aoConnected = false;
            bool aiConnected = false;
            bool aoRunning = false;
            bool aiRunning = false;

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                context.Log("连接板卡 MT-X532");
                aoConnected = await aoDriver.ConnectAsync();
                aoConnected = aoConnected && aoDriver.IsConnected;
                if (!aoConnected)
                {
                    context.Log("连接板卡 MT-X532 失败 ");
                    throw new InvalidOperationException("连接板卡 MT-X532 失败");
                }
                context.Log("连接板卡 MT-X532 成功");

                context.Log("初始化板卡 MT-X532 配置：32路使能 +10V DC 10kHz");
                await ConfigureAoAsync(aoDriver, offset: 10.0);
                context.Log("初始化板卡 MT-X532 配置 成功");

                context.Log("开启32路模拟量DC输出");
                aoRunning = await aoDriver.StartAcquisitionAsync();
                if (!aoRunning)
                {
                    context.Log("模拟量输出失败");
                    throw new InvalidOperationException("模拟量输出失败");
                }
                context.Log("开启32路模拟量输出 成功");

                context.Log("连接板卡 PXI-9774");
                aiConnected = await aiDriver.ConnectAsync();
                aiConnected = aiConnected && aiDriver.IsConnected;
                if (!aiConnected)
                {
                    context.Log("连接板卡 PXI-9774 失败");
                    throw new InvalidOperationException("连接板卡 PXI-9774 失败");
                }
                context.Log("连接板卡 PXI-9774 成功");

                context.Log("初始化 PXI-9774 板卡参数：EnableAll + 10kHz + 连续 + 采样数=1000");
                await EnableAiAllAsync(aiDriver);

                context.Log("开始模拟量采集");
                aiRunning = await aiDriver.StartContinuousAcquisitionAsync(AiSampleRate, AiBufferSamplesPerChannel);
                if (!aiRunning)
                {
                    context.Log("开始模拟量采集失败");
                    throw new InvalidOperationException("开始模拟量采集失败");
                }
                context.Log("开始模拟量采集 成功");

                var valuesPos = await CaptureOneBlockAsync(aiDriver, cancellationToken);
                LogAiValues(context, "采集值(DC+10V)", valuesPos, 10.0);

                context.Log("切换32路模拟量DC输出-10V");
                await ConfigureAoAsync(aoDriver, offset: -10.0);

                await Task.Delay(150);
                var valuesNeg = await CaptureOneBlockAsync(aiDriver, cancellationToken);
                LogAiValues(context, "采集值(DC-10V)", valuesNeg, -10.0);
            }
            finally
            {
                try
                {
                    if (aoDriver.IsConnected)
                    {
                        context.Log("停止模拟量输出");
                        try
                        {
                            await aoDriver.WriteChannelsBatchAsync(AllAoChannels.ToDictionary(ch => ch, _ => 0.0));
                        }
                        catch { }

                        try { await aoDriver.StopAcquisitionAsync(); } catch { }
                    }
                }
                catch { }

                try
                {
                    if (aiDriver.IsConnected)
                    {
                        context.Log("停止模拟量采集");
                        try { await aiDriver.StopAcquisitionAsync(); } catch { }
                    }
                }
                catch { }

                try
                {
                    if (aiDriver.IsConnected)
                    {
                        context.Log("断开板卡 PXI-9774");
                        try { await aiDriver.DisconnectAsync(); } catch { }
                    }
                }
                catch { }

                try
                {
                    if (aoDriver.IsConnected)
                    {
                        context.Log("断开板卡 MT-X532");
                        try { await aoDriver.DisconnectAsync(); } catch { }
                    }
                }
                catch { }
            }
        }

        private static async Task ConfigureAoAsync(MTX532Driver driver, double offset)
        {
            var cfg = new Dictionary<string, object>
            {
                { "Enabled", true },
                { "SampleRate", AoSampleRate },
                { "Waveform", MTX532Driver.WaveformType.Dc },
                { "Amplitude", 0.0 },
                { "Offset", offset }
            };

            foreach (var ch in AllAoChannels)
            {
                var ok = await driver.ConfigureChannelAsync(ch, cfg);
                if (!ok)
                {
                    throw new InvalidOperationException($"配置输出通道失败：{ch}");
                }
            }

            var writeOk = await driver.WriteChannelsBatchAsync(AllAoChannels.ToDictionary(ch => ch, _ => offset));
            if (!writeOk)
            {
                throw new InvalidOperationException("写入输出电压失败");
            }
        }

        private static async Task EnableAiAllAsync(Art9774Driver driver)
        {
            var cfg = new Dictionary<string, object>
            {
                { "IsEnabled", true }
            };

            foreach (var ch in AllAiChannels)
            {
                var ok = await driver.ConfigureChannelAsync(ch, cfg);
                if (!ok)
                {
                    throw new InvalidOperationException($"配置采集通道失败：{ch}");
                }
            }
        }

        private static async Task<Dictionary<string, double>> CaptureOneBlockAsync(Art9774Driver driver, CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<Dictionary<string, double>>(TaskCreationOptions.RunContinuationsAsynchronously);

            void Handler(Dictionary<string, double[]> samples)
            {
                try
                {
                    if (samples == null)
                    {
                        return;
                    }

                    var values = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                    foreach (var kv in samples)
                    {
                        var arr = kv.Value;
                        if (arr == null || arr.Length == 0)
                        {
                            values[kv.Key] = 0.0;
                            continue;
                        }

                        values[kv.Key] = arr[arr.Length - 1];
                    }

                    tcs.TrySetResult(values);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            }

            driver.SamplesAvailable += Handler;
            try
            {
                using (cancellationToken.Register(() => tcs.TrySetCanceled()))
                {
                    var completed = await Task.WhenAny(tcs.Task, Task.Delay(3000, cancellationToken));
                    if (completed != tcs.Task)
                    {
                        return new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                    }
                    return await tcs.Task;
                }
            }
            finally
            {
                driver.SamplesAvailable -= Handler;
            }
        }

        private static void LogAiValues(SelfInspectionContext context, string title, Dictionary<string, double> values, double aoSetValue)
        {
            if (context == null) return;
            if (values == null)
            {
                context.Log($"{title}：无数据");
                return;
            }

            context.Log(title);
            for (int i = 0; i < AllAiChannels.Count; i++)
            {
                var aiChannel = AllAiChannels[i];
                var aoChannel = AllAoChannels[i];
                if (values.TryGetValue(aiChannel, out var aiValue))
                {
                    context.Log($"{aoChannel}={aoSetValue:F1}V -> {aiChannel}={aiValue:F6}V");
                }
            }
        }
    }
}
