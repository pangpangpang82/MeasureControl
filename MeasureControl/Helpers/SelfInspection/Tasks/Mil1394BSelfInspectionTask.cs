using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using MeasureControl.Drivers;
using MeasureControl.Helpers;
using MeasureControl.Models.Devices;
using MeasureControl.Models.Devices.DeviceCategories;

namespace MeasureControl.Helpers.SelfInspection
{
    internal sealed class Mil1394BSelfInspectionTask : ISelfInspectionTask
    {
        private const uint TargetChannel = 0;
        private const uint TargetMessageId = 0;
        private const int TargetPrintCount = 5;
        private const int TimeoutMs = 10000;

        public bool CanHandle(DeviceBase device)
        {
            if (device == null)
            {
                return false;
            }

            if (device is Mil1394BDevice)
            {
                return true;
            }

            var name = device.Name ?? string.Empty;
            var model = device.Model ?? string.Empty;
            var parentNode = device.ParentNode ?? string.Empty;

            var key = (name + " " + model + " " + parentNode).ToUpperInvariant();
            return key.Contains("1394B") || key.Contains("MIL1394") || key.Contains("MIL-1394") || key.Contains("1394");
        }

        public async Task RunAsync(DeviceBase device, SelfInspectionContext context, CancellationToken cancellationToken)
        {
            if (device == null) throw new ArgumentNullException(nameof(device));
            if (context == null) throw new ArgumentNullException(nameof(context));

            if (!CanHandle(device))
            {
                context.Log($"跳过：非1394B板卡 {device.Name} Model={device.Model}");
                return;
            }

            var slotIndex = (device as PxiDeviceBase)?.SlotIndex ?? -1;
            var cached = DriverFactory.GetCachedDriver(device.Id, slotIndex);
            if (cached != null && cached.IsConnected)
            {
                context.Log("检测到板卡已连接，取消自检以避免影响面板。");
                throw new InvalidOperationException("板卡已连接，无法自检。");
            }

            IntPtr node0 = IntPtr.Zero;
            IntPtr node1 = IntPtr.Zero;

            var di0 = new HZ1394DriverInterface(0)
            {
                CardNumber = 0,
                NodeNumber = 0,
                TmpnodeType = "CC",
                ComboBoxNodeRateDriver = "400M"
            };

            var di1 = new HZ1394DriverInterface(1)
            {
                CardNumber = 0,
                NodeNumber = 1,
                TmpnodeType = "RN",
                ComboBoxNodeRateDriver = "400M"
            };

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                var devInfo = new PCI_DEV_FOUND
                {
                    DevNum = 0,
                    DevType = new uint[32],
                    DevNodeNum = new uint[32],
                    DevSN = new uint[32]
                };

                int found = HZ1394Interface.Mil1394_Found(ref devInfo);
                if (found < 0 || devInfo.DevNum == 0)
                {
                    context.Log("未检测到1394B设备");
                    throw new InvalidOperationException("未检测到1394B设备");
                }

                uint cardIndex = 0;
                context.Log($"检测到 {devInfo.DevNum} 个1394B设备，使用设备索引 {cardIndex}");

                di0.CardNumber = cardIndex;
                di1.CardNumber = cardIndex;

                cancellationToken.ThrowIfCancellationRequested();

                context.Log("打开节点0: CC");
                node0 = di0.HZ1394_OPEN("CC", IntPtr.Zero, cardIndex, 0);
                if (node0 == IntPtr.Zero)
                {
                    throw new InvalidOperationException("打开节点0(CC)失败");
                }

                context.Log("打开节点1: RN");
                node1 = di1.HZ1394_OPEN("RN", IntPtr.Zero, cardIndex, 1);
                if (node1 == IntPtr.Zero)
                {
                    throw new InvalidOperationException("打开节点1(RN)失败");
                }

                cancellationToken.ThrowIfCancellationRequested();

                context.Log("配置节点0(CC)：STOF按次数=5，STOF Payload=全0，异步发送CH0 MsgID=00");

                int res = 0;
                res |= di0.HZ1394_SetSpeed(di0.ComboBoxNodeRateDriver, node0);
                res |= di0.HZ1394_CC_BM_ENABLE(node0, 0);
                res |= di0.HZ1394_CRB_LRTC_ENABLE(node0, 1);
                res |= di0.HZ1394_SetPeriod_Style_EN(node0, stofStyle: 1, period: 15, times: 5);

                var stofData = new TNF_Stof_Struct
                {
                    STOFPayload0 = 0,
                    STOFPayload1 = 0,
                    STOFPayload2 = 0,
                    STOFPayload3 = 0,
                    STOFPayload4 = 0,
                    STOFPayload5 = 0,
                    STOFPayload6 = 0,
                    STOFPayload7 = 0,
                    STOFPayload8 = 0,
                    STOFVPC = 0
                };

                res |= di0.HZ1394_CC_MSG_STOF_Data_Set(node0, 1, ref stofData);

                res |= di0.ASYNC_SEND_SYNSel_Set(node0);

                uint[] messageData = new uint[500];
                messageData[0] = 0x01234567;
                messageData[1] = 0x89ABCDEF;

                var asyncPackets = new TNF_ASYNC_Struct[1];
                asyncPackets[0] = new TNF_ASYNC_Struct
                {
                    MessageID = TargetMessageId,
                    Channel = TargetChannel,
                    MessageType = 0,
                    HeartBeatWord = 0,
                    HealthStatusWord = 0,
                    HeartBeatStyle = 1,
                    HeartBeatEnable = 1,
                    HeartBeatStep = 0,
                    STOFTransmitOffset = 0,
                    STOFReceiveOffset = 0,
                    STOFPHMOffset = 0,
                    STOFCCSendOffset = 0,
                    PayloadDataLength = 64,
                    MessageDataLength = 64,
                    MessageData = messageData,
                    Security = 0,
                    NodeID = 0,
                    Priority = 0,
                    SoftVPCenable = 0,
                    VPCASYNC = 0,
                    VPCErrorEnable = 0,
                    ErrMode = 0,
                    ErrNum = 0,
                    CRCASYNC = 0
                };

                res |= di0.HZ1394_CC_MSG_ASYNC_Data_Set(node0, 1, 0, asyncPackets, 1);
                di0.AsyncPktNum = 1;

                res |= di0.HZ1394_CC_SIM_ERR_Start(node0, 1);

                if (res != 0)
                {
                    throw new InvalidOperationException($"节点0(CC)配置失败，错误码: {res}");
                }

                context.Log("配置节点1(RN)：接收CH0 MsgID=00，RCVdatalength=96字节");

                res = 0;
                res |= di1.HZ1394_SetSpeed(di1.ComboBoxNodeRateDriver, node1);
                res |= di1.HZ1394_CC_BM_ENABLE(node1, 0);
                res |= di1.HZ1394_CC_MSG_RCV_STOF_ENABLE(node1, 1);

                uint[] msgIds = { TargetMessageId };
                uint[] msgLens = { 96 };
                res |= di1.ASYNC_RECV_CFG(node1, TargetChannel, 1, msgIds, msgLens);

                res |= di1.HZ1394_CC_SIM_ERR_Start(node1, 1);

                if (res != 0)
                {
                    throw new InvalidOperationException($"节点1(RN)配置失败，错误码: {res}");
                }

                cancellationToken.ThrowIfCancellationRequested();

                context.Log("启动节点1接收（ASYNC_RECV_Start + StartRecvThd）");
                res = di1.HZ1394_CC_MSG_ASYNC_RECV_Start(node1);
                if (res != 0)
                {
                    throw new InvalidOperationException($"启动节点1接收失败，错误码: {res}");
                }
                di1.HZStartRecvThd(node1);

                await Task.Delay(100, cancellationToken);

                context.Log("启动节点0发送（ASYNC_SEND_Start + STOF_Start）");
                res = di0.HZ1394_CC_MSG_ASYNC_SEND_Start(node0);
                if (res != 0)
                {
                    throw new InvalidOperationException($"启动节点0异步发送失败，错误码: {res}");
                }

                res = di0.HZ1394_CC_MSG_STOF_Start(node0);
                if (res != 0)
                {
                    throw new InvalidOperationException($"启动节点0 STOF发送失败，错误码: {res}");
                }

                context.Log("开始接收并打印5条数据（混合STOF+ASYNC），要求至少包含1条符合条件的ASYNC");

                var stopwatch = Stopwatch.StartNew();
                IntPtr msgPtr = IntPtr.Zero;

                int printed = 0;
                bool asyncSeen = false;
                uint? asyncPayload0 = null;
                uint? asyncPayload1 = null;

                while (stopwatch.ElapsedMilliseconds < TimeoutMs)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    int count = HZ1394Interface.Mil1394_CC_Packet_Get(node1, ref msgPtr);
                    if (count > 0 && msgPtr != IntPtr.Zero)
                    {
                        for (int i = 0; i < count; i++)
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            var offset = new IntPtr(msgPtr.ToInt64() + i * Marshal.SizeOf(typeof(TNF_RECV_PACKET_Struct)));
                            var pkt = (TNF_RECV_PACKET_Struct)Marshal.PtrToStructure(offset, typeof(TNF_RECV_PACKET_Struct));

                            bool isStof = pkt.MessageTYPE == 0;
                            bool isAsync = pkt.MessageTYPE == 1 || pkt.MessageTYPE == 2;

                            if (!isStof && !isAsync)
                            {
                                continue;
                            }

                            if (printed >= TargetPrintCount && asyncSeen)
                            {
                                break;
                            }

                            if (isAsync)
                            {
                                bool match = pkt.Channel == TargetChannel && pkt.MessageID == TargetMessageId;
                                if (!match)
                                {
                                    continue;
                                }

                                uint p0 = (pkt.MessageData != null && pkt.MessageData.Length > 0) ? pkt.MessageData[0] : 0;
                                uint p1 = (pkt.MessageData != null && pkt.MessageData.Length > 1) ? pkt.MessageData[1] : 0;

                                asyncPayload0 = p0;
                                asyncPayload1 = p1;

                                asyncSeen = true;

                                if (printed < TargetPrintCount)
                                {
                                    printed++;
                                    context.Log($"RX#{printed} ASYNC: CH={pkt.Channel} MsgID=0x{pkt.MessageID:X2} Len={pkt.length} PayloadLen={pkt.PayloadDataLength} Data0=0x{p0:X8} Data1=0x{p1:X8}");
                                }

                                continue;
                            }

                            if (isStof)
                            {
                                if (!asyncSeen && printed >= (TargetPrintCount - 1))
                                {
                                    continue;
                                }

                                if (printed < TargetPrintCount)
                                {
                                    printed++;
                                    context.Log($"RX#{printed} STOF: LRTC={pkt.LRTC} RTC={pkt.RTC} VPC={pkt.STOFVPC} Payload0={pkt.STOFPayload0}");
                                }
                            }
                        }
                    }
                    else
                    {
                        await Task.Delay(10, cancellationToken);
                    }

                    if (printed >= TargetPrintCount && asyncSeen)
                    {
                        break;
                    }
                }

                if (!asyncSeen)
                {
                    throw new InvalidOperationException("未收到符合条件的ASYNC包（CH0 MsgID=00）");
                }

                if (asyncPayload0 != 0x01234567 || asyncPayload1 != 0x89ABCDEF)
                {
                    throw new InvalidOperationException($"ASYNC数据校验失败: Data0=0x{asyncPayload0:X8}, Data1=0x{asyncPayload1:X8}");
                }

                if (printed < TargetPrintCount)
                {
                    throw new InvalidOperationException($"接收打印条数不足: {printed}/{TargetPrintCount}");
                }

                context.Log("1394B自检完成：收发验证通过");
            }
            finally
            {
                try
                {
                    if (node0 != IntPtr.Zero)
                    {
                        try { di0.HZ1394_CC_MSG_STOF_Stop(node0); } catch { }
                        try { di0.HZ1394_CC_MSG_ASYNC_SEND_Stop(node0); } catch { }
                        try { di0.HZ1394_CC_BM_ENABLE(node0, 0); } catch { }
                        try { di0.HZ1394_CC_MSG_RCV_STOF_ENABLE(node0, 0); } catch { }
                        try { di0.HZ1394_CRB_LRTC_ENABLE(node0, 0); } catch { }
                    }

                    if (node1 != IntPtr.Zero)
                    {
                        try { di1.HZ1394_CC_MSG_ASYNC_RECV_Stop(node1); } catch { }
                        try { di1.HZStopRecvThd(node1); } catch { }
                        try { di1.HZ1394_CC_BM_ENABLE(node1, 0); } catch { }
                        try { di1.HZ1394_CC_MSG_RCV_STOF_ENABLE(node1, 0); } catch { }
                        try { di1.HZ1394_CRB_LRTC_ENABLE(node1, 0); } catch { }
                    }
                }
                catch
                {
                }

                try
                {
                    DriverFactory.RemoveCachedDriver(device.Id);
                }
                catch
                {
                }
            }
        }
    }
}
