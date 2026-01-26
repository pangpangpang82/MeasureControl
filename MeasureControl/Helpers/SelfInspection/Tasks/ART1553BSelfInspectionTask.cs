using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MeasureControl.Drivers;
using MeasureControl.Models.Devices;
using MeasureControl.Models.Devices.DeviceCategories;

namespace MeasureControl.Helpers.SelfInspection
{
    internal sealed class ART1553BSelfInspectionTask : ISelfInspectionTask
    {
        // 自检参数
        private const int BC_Channel = 0;           // BC使用通道0
        private const int RT_Channel = 1;           // RT使用通道1
        private const int BM_Channel = 1;           // BM监控通道
        private const int RT_Address = 1;           // RT地址为1
        private const int SubAddress = 1;           // 子地址为1
        private const int ExpectedMessageCount = 5; // 期望收到5条消息
        private const int TimeoutMs = 10000;        // 超时10秒

        // 测试数据: 0x0123, 0x4567, 0x89AB, 0xCDEF
        private static readonly ushort[] TestData = new ushort[] { 0x0123, 0x4567, 0x89AB, 0xCDEF };

        private static bool Is1553B(DeviceBase device)
        {
            if (device == null)
            {
                return false;
            }

            if (device is Mil1553BDevice)
            {
                return true;
            }

            var name = (device.Name ?? string.Empty).ToUpperInvariant();
            var model = (device.Model ?? string.Empty).ToUpperInvariant();
            var parentNode = (device.ParentNode ?? string.Empty).ToUpperInvariant();

            if (name.Contains("1553") || model.Contains("1553") || model.Contains("ART1553B") || model.Contains("4332") || parentNode.Contains("1553"))
            {
                return true;
            }

            try
            {
                var typeName = (device.DeviceTypeName ?? string.Empty).ToUpperInvariant();
                if (typeName.Contains("1553"))
                {
                    return true;
                }
            }
            catch
            {
                // ignore
            }

            return false;
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
            return device != null && Is1553B(device);
        }

        public async Task RunAsync(DeviceBase device, SelfInspectionContext context, CancellationToken cancellationToken)
        {
            if (device == null) throw new ArgumentNullException(nameof(device));
            if (context == null) throw new ArgumentNullException(nameof(context));

            if (!Is1553B(device))
            {
                context.Log($"跳过：非1553B板卡 {device.Name} Model={device.Model}");
                return;
            }

            var slotIndex = GetSlotIndex(device);
            var cached = DriverFactory.GetCachedDriver(device.Id, slotIndex);
            if (cached != null && cached.IsConnected)
            {
                context.Log("检测到板卡已连接，取消自检以避免影响面板。");
                throw new InvalidOperationException("板卡已连接，无法自检。");
            }

            var driver = DriverFactory.CreateDriver(device) as ART1553BDriver;
            if (driver == null)
            {
                context.Log($"未找到ART1553B驱动，无法自检：{device.Name} Model={device.Model}");
                return;
            }

            bool connected = false;
            bool bcRunning = false;
            bool rtRunning = false;
            bool bmRunning = false;
            int receivedCount = 0;

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                // 1. 连接板卡
                context.Log("连接板卡 ART1553B");
                var connectOk = await driver.ConnectAsync();
                connected = connectOk && driver.IsConnected;
                if (!connected)
                {
                    context.Log("连接板卡 ART1553B 失败");
                    throw new InvalidOperationException("连接板卡 ART1553B 失败");
                }
                context.Log("连接板卡 ART1553B 成功");

                // 2. 配置通道1为RT模式（RT地址=1）
                context.Log($"配置通道{RT_Channel}为RT模式，RT地址={RT_Address}");
                var rtConfigOk = await driver.ConfigureRTModeAsync(RT_Channel, RT_Address, responseTime: 500, setAsCurrent: true);
                if (!rtConfigOk)
                {
                    context.Log("配置RT模式失败");
                    throw new InvalidOperationException("配置RT模式失败");
                }
                rtRunning = true;
                context.Log("配置RT模式成功");
                context.Log($"RT{RT_Address}已配置，启动RT运行");

                // 注意：驱动的 ConfigureRTModeAsync 仅在 _isRunning==true 时才会调用 RT_Start。
                // 这里显式启动采集以确保 RT_Start 被执行。
                var rtStartOk = await driver.StartAcquisitionAsync();
                if (!rtStartOk)
                {
                    context.Log("启动RT运行失败");
                    throw new InvalidOperationException("启动RT运行失败");
                }
                await Task.Delay(200, cancellationToken);
                context.Log($"RT{RT_Address}已启动，等待BC发送数据");

                // 3. 配置通道0为BC模式
                context.Log($"配置通道{BC_Channel}为BC模式");
                var bcConfigOk = await driver.ConfigureBCModeAsync(BC_Channel, responseTime: 4000, frameGap: 10);
                if (!bcConfigOk)
                {
                    context.Log("配置BC模式失败");
                    throw new InvalidOperationException("配置BC模式失败");
                }
                context.Log("配置BC模式成功");

                // 4. 配置BM监控（在通道0上监控）
                context.Log($"配置通道{BM_Channel}为BM监控模式");
                bmRunning = driver.StartBMWithFilter(BM_Channel, filter: null); // 全通过滤
                if (!bmRunning)
                {
                    context.Log("启动BM监控失败");
                    throw new InvalidOperationException("启动BM监控失败");
                }
                context.Log("BM监控已启动");
                await Task.Delay(100, cancellationToken);

                // 6. 监控BM接收，等待5条消息
                context.Log($"开始监控，等待接收{ExpectedMessageCount}条消息...");

                var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

                void OnMessageReceived(object sender, ART1553BDriver.MessageReceivedEventArgs e)
                {
                    if (e.MessageType == "BM_Receive" && e.Channel == BM_Channel && e.RTAddress == RT_Address)
                    {
                        receivedCount++;
                        var recvDataStr = string.Join(" ", e.Message.MsgBlock.Datablk?.Take(4).Select(d => $"0x{d:X4}") ?? Array.Empty<string>());
                        context.Log($"BM收到消息 #{receivedCount}: RT={e.RTAddress}, Data=[{recvDataStr}]");

                        if (receivedCount >= ExpectedMessageCount)
                        {
                            tcs.TrySetResult(true);
                        }
                    }
                }

                driver.MessageReceived += OnMessageReceived;

                try
                {
                    using (cancellationToken.Register(() => tcs.TrySetCanceled()))
                    {
                        for (int i = 0; i < ExpectedMessageCount; i++)
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            var dataStr = string.Join(", ", TestData.Select(d => $"0x{d:X4}"));
                            context.Log($"BC写入测试消息 ({i + 1}/{ExpectedMessageCount}): RT={RT_Address}, SA={SubAddress}, Data=[{dataStr}]");

                            var writeOk = driver.SendBCMessageToRT(
                                channel: BC_Channel,
                                messageId: 0,
                                rtAddress: RT_Address,
                                subAddress: SubAddress,
                                data: TestData,
                                channelSelect: 1,
                                messageGap: 20,
                                retryEnable: false,
                                period: 0,
                                initPeriod: 0,
                                run: true
                            );
                            if (!writeOk)
                            {
                                context.Log("BC写入消息失败");
                                throw new InvalidOperationException("BC写入消息失败");
                            }

                            context.Log($"启动BC发送 ({i + 1}/{ExpectedMessageCount})");
                            var bcStartOk = driver.BCStartAndWait(BC_Channel, timeoutMs: 5000);
                            if (!bcStartOk)
                            {
                                context.Log("BC启动失败或执行超时");
                                throw new InvalidOperationException("BC启动失败或执行超时");
                            }
                            bcRunning = true;
                            try { driver.BCStop(BC_Channel); } catch { }
                            await Task.Delay(50, cancellationToken);
                        }

                        var timeoutTask = Task.Delay(TimeoutMs, cancellationToken);
                        var completedTask = await Task.WhenAny(tcs.Task, timeoutTask);

                        if (completedTask == timeoutTask)
                        {
                            context.Log($"超时！仅收到{receivedCount}条消息，期望{ExpectedMessageCount}条");
                        }
                        else if (tcs.Task.Status == TaskStatus.RanToCompletion)
                        {
                            context.Log($"自检成功！共收到{receivedCount}条消息");
                        }
                    }
                }
                finally
                {
                    driver.MessageReceived -= OnMessageReceived;
                }

                // 验证结果
                if (receivedCount >= ExpectedMessageCount)
                {
                    context.Log("1553B板卡自检通过：BC→RT通信正常，BM监控正常");
                }
                else
                {
                    context.Log($"1553B板卡自检警告：仅收到{receivedCount}/{ExpectedMessageCount}条消息");
                }
            }
            finally
            {
                // 7. 停止并断开
                if (connected)
                {
                    try
                    {
                        context.Log("停止BC");
                        if (bcRunning)
                        {
                            try { driver.BCStop(BC_Channel); } catch { }
                        }
                    }
                    catch { }

                    try
                    {
                        context.Log("停止BM监控");
                        if (bmRunning)
                        {
                            try { driver.StopBM(BM_Channel); } catch { }
                        }
                    }
                    catch { }

                    try
                    {
                        context.Log("停止RT");
                        if (rtRunning)
                        {
                            try { await driver.ConfigureRTModeAsync(RT_Channel, RT_Address, responseTime: 500, setAsCurrent: true); } catch { }
                            try { await driver.StopAcquisitionAsync(); } catch { }
                        }
                    }
                    catch { }

                    try
                    {
                        context.Log("断开板卡 ART1553B");
                        if (driver.IsConnected)
                        {
                            try { await driver.DisconnectAsync(); } catch { }
                        }
                    }
                    catch { }
                }
            }
        }
    }
}
