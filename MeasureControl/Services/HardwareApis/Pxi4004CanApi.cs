using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using MeasureControl.Drivers;
using MeasureControl.Drivers.PXI4004CAN;
using MeasureControl.Models;
using MeasureControl.Models.Devices;

namespace MeasureControl.Services.HardwareApis
{
    /// <summary>
    /// CAN 帧数据
    /// </summary>
    public sealed class CanFrame
    {
        public uint FrameId { get; set; }
        public bool IsExtendedId { get; set; }
        public bool IsRemoteFrame { get; set; }
        public byte DataLength { get; set; }
        public byte[] Data { get; set; }
        public ulong Timestamp { get; set; }
    }

    /// <summary>
    /// CAN 通道参数
    /// </summary>
    public sealed class CanChannelParams
    {
        public uint BaudRate { get; set; } = 500000;
        public byte WorkMode { get; set; } = 0;
        public bool EnableTimestamp { get; set; } = true;
        public bool AcceptExtendedId { get; set; } = false;
        public byte AcceptanceFilterCount { get; set; } = 0;
        public uint AcceptanceCodeA { get; set; } = 0x00000000;
        public uint AcceptanceCodeB { get; set; } = 0x00000000;
        public uint AcceptanceMaskA { get; set; } = 0xFFFFFFFF;
        public uint AcceptanceMaskB { get; set; } = 0xFFFFFFFF;
        public uint FrameInterval { get; set; } = 0;
    }

    /// <summary>
    /// CAN 通道状态
    /// </summary>
    public sealed class CanChannelStatus
    {
        public uint Channel { get; set; }
        public bool TaskDone { get; set; }
        public bool Triggered { get; set; }
        public uint TaskState { get; set; }
        public uint CanState { get; set; }
        public uint ReceivedFrameCount { get; set; }
        public uint RemainingFrameCount { get; set; }
        public uint LostFrameCount { get; set; }
        public uint RecvFifoOverflowCount { get; set; }
        public uint RecvBufferOverflowCount { get; set; }
    }

    /// <summary>
    /// PXI4004 CAN 卡 API 接口
    /// </summary>
    public interface IPxi4004CanApi : IAsyncDisposable
    {
        bool IsConnected { get; }
        int SlotNumber { get; }

        Task<bool> ConnectAsync(int slotNumber = 0, CancellationToken cancellationToken = default);
        Task<bool> DisconnectAsync(CancellationToken cancellationToken = default);

        Task<bool> OpenChannelAsync(int channelIndex, CancellationToken cancellationToken = default);
        Task<bool> OpenChannelAsync(int channelIndex, CanChannelParams parameters, CancellationToken cancellationToken = default);
        Task<bool> CloseChannelAsync(int channelIndex, CancellationToken cancellationToken = default);
        bool IsChannelOpen(int channelIndex);

        Task<bool> SendFrameAsync(int channelIndex, CanFrame frame, double timeout = 0.2, CancellationToken cancellationToken = default);
        Task<CanFrame> ReceiveFrameAsync(int channelIndex, double timeout = 0.01, CancellationToken cancellationToken = default);
        Task<List<CanFrame>> ReceiveFramesBatchAsync(int channelIndex, int maxFrames = 100, double timeout = 0.01, CancellationToken cancellationToken = default);

        Task<CanChannelStatus> GetChannelStatusAsync(int channelIndex, CancellationToken cancellationToken = default);
        Task<bool> ResetAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// PXI4004 CAN 卡 API 实现
    /// 基于 PXI4004Driver 封装
    /// </summary>
    public sealed class Pxi4004CanApi : IPxi4004CanApi
    {
        private PXI4004Driver _driver;
        private DeviceBase _device;
        private int _slotNumber;
        private bool _disposed;
        private readonly HashSet<int> _openedChannels = new HashSet<int>();
        private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);

        private sealed class Pxi4004CanDevice : DeviceBase
        {
            public override string DeviceTypeName => "CAN";

            public override ObservableCollection<DeviceInfoItem> GetDeviceInfoItems()
            {
                return new ObservableCollection<DeviceInfoItem>();
            }

            public override void InitializeChildren()
            {
                Children?.Clear();
            }
        }

        public bool IsConnected => _driver?.IsConnected ?? false;
        public int SlotNumber => _slotNumber;

        public async Task<bool> ConnectAsync(int slotNumber = 0, CancellationToken cancellationToken = default)
        {
            await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (IsConnected)
                {
                    return true;
                }

                _slotNumber = slotNumber;
                _device = new Pxi4004CanDevice
                {
                    Id = $"PXI4004-CAN-{slotNumber}",
                    Name = "PXI4004 CAN",
                    Model = "PXI4004"
                };

                _driver = new PXI4004Driver(_device, slotNumber);
                var result = await _driver.ConnectAsync().ConfigureAwait(false);
                if (!result)
                {
                    _driver = null;
                    _device = null;
                }
                return result;
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task<bool> DisconnectAsync(CancellationToken cancellationToken = default)
        {
            await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_driver == null)
                {
                    return true;
                }

                var result = await _driver.DisconnectAsync().ConfigureAwait(false);
                _driver = null;
                _device = null;
                _openedChannels.Clear();
                return result;
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task<bool> OpenChannelAsync(int channelIndex, CancellationToken cancellationToken = default)
        {
            if (_driver == null || !IsConnected)
            {
                return false;
            }

            var result = await _driver.OpenChannelAsync(channelIndex).ConfigureAwait(false);
            if (result)
            {
                _openedChannels.Add(channelIndex);
            }
            return result;
        }

        public async Task<bool> OpenChannelAsync(int channelIndex, CanChannelParams parameters, CancellationToken cancellationToken = default)
        {
            if (_driver == null || !IsConnected)
            {
                return false;
            }

            var param = ConvertToNativeParams(parameters);
            var result = await _driver.OpenChannelAsync(channelIndex, param).ConfigureAwait(false);
            if (result)
            {
                _openedChannels.Add(channelIndex);
            }
            return result;
        }

        public async Task<bool> CloseChannelAsync(int channelIndex, CancellationToken cancellationToken = default)
        {
            if (_driver == null || !IsConnected)
            {
                return false;
            }

            var result = await _driver.CloseChannelAsync(channelIndex).ConfigureAwait(false);
            if (result)
            {
                _openedChannels.Remove(channelIndex);
            }
            return result;
        }

        public bool IsChannelOpen(int channelIndex)
        {
            return _openedChannels.Contains(channelIndex);
        }

        public async Task<bool> SendFrameAsync(int channelIndex, CanFrame frame, double timeout = 0.2, CancellationToken cancellationToken = default)
        {
            if (_driver == null || !IsConnected)
            {
                return false;
            }

            var nativeFrame = ConvertToNativeFrame(frame);
            return await _driver.SendFrameAsync(channelIndex, nativeFrame, timeout).ConfigureAwait(false);
        }

        public async Task<CanFrame> ReceiveFrameAsync(int channelIndex, double timeout = 0.01, CancellationToken cancellationToken = default)
        {
            if (_driver == null || !IsConnected)
            {
                return null;
            }

            var nativeFrame = await _driver.ReceiveFrameAsync(channelIndex, timeout).ConfigureAwait(false);
            if (nativeFrame == null)
            {
                return null;
            }

            return ConvertFromNativeFrame(nativeFrame.Value);
        }

        public async Task<List<CanFrame>> ReceiveFramesBatchAsync(int channelIndex, int maxFrames = 100, double timeout = 0.01, CancellationToken cancellationToken = default)
        {
            var result = new List<CanFrame>();

            if (_driver == null || !IsConnected)
            {
                return result;
            }

            var nativeFrames = await _driver.ReceiveFramesBatchAsync(channelIndex, maxFrames, timeout).ConfigureAwait(false);
            if (nativeFrames != null)
            {
                foreach (var nf in nativeFrames)
                {
                    result.Add(ConvertFromNativeFrame(nf));
                }
            }

            return result;
        }

        public async Task<CanChannelStatus> GetChannelStatusAsync(int channelIndex, CancellationToken cancellationToken = default)
        {
            if (_driver == null || !IsConnected)
            {
                return null;
            }

            return await Task.Run(() =>
            {
                try
                {
                    var nativeStatus = PXI4004.GetCANStatus(_driver.DeviceHandle, (uint)channelIndex);
                    return new CanChannelStatus
                    {
                        Channel = nativeStatus.nChannel,
                        TaskDone = nativeStatus.bTaskDone != 0,
                        Triggered = nativeStatus.bTriggered != 0,
                        TaskState = nativeStatus.nTaskState,
                        CanState = nativeStatus.nCANState,
                        ReceivedFrameCount = nativeStatus.nRecvedFrameCnt,
                        RemainingFrameCount = nativeStatus.nRecvFrameRemainCnt,
                        LostFrameCount = nativeStatus.nRecvFrameLostCnt,
                        RecvFifoOverflowCount = nativeStatus.nRecvFifoOverflowCnt,
                        RecvBufferOverflowCount = nativeStatus.nRecvBufOverflowCnt
                    };
                }
                catch
                {
                    return null;
                }
            }, cancellationToken).ConfigureAwait(false);
        }

        public async Task<bool> ResetAsync(CancellationToken cancellationToken = default)
        {
            if (_driver == null || !IsConnected)
            {
                return false;
            }

            return await _driver.ResetAsync().ConfigureAwait(false);
        }

        #region 辅助方法

        private static PXI4004.ARTCANX1_CAN_PARAM ConvertToNativeParams(CanChannelParams parameters)
        {
            var param = new PXI4004.ARTCANX1_CAN_PARAM
            {
                nBaudRate = parameters.BaudRate,
                nWorkMode = parameters.WorkMode,
                bRecvTimestampEn = (byte)(parameters.EnableTimestamp ? 1 : 0),
                bAccExtID = (byte)(parameters.AcceptExtendedId ? 1 : 0),
                nAccFilterCnt = parameters.AcceptanceFilterCount,
                nAccCodeA = parameters.AcceptanceCodeA,
                nAccCodeB = parameters.AcceptanceCodeB,
                nAccMaskA = parameters.AcceptanceMaskA,
                nAccMaskB = parameters.AcceptanceMaskB,
                nFrameInterval = parameters.FrameInterval,
                nReserved1 = new uint[7],
                nReserved2 = new uint[32],
                SendTrig = new PXI4004.ARTCANX1_TRIG_PARAM
                {
                    nTriggerType = PXI4004.ARTCANX1_TRIGTYPE_NONE,
                    nReserved = new uint[20]
                }
            };
            return param;
        }

        private static PXI4004.ARTCANX1_CAN_FRAME ConvertToNativeFrame(CanFrame frame)
        {
            var nativeFrame = new PXI4004.ARTCANX1_CAN_FRAME
            {
                nFrameID = frame.FrameId,
                bExtendedID = (byte)(frame.IsExtendedId ? 1 : 0),
                nFrameType = (byte)(frame.IsRemoteFrame ? 1 : 0),
                nDataLength = frame.DataLength,
                DataBuf = new byte[8]
            };

            if (frame.Data != null && frame.Data.Length > 0)
            {
                Array.Copy(frame.Data, nativeFrame.DataBuf, Math.Min(frame.Data.Length, 8));
            }

            return nativeFrame;
        }

        private static CanFrame ConvertFromNativeFrame(PXI4004.ARTCANX1_CAN_FRAME nativeFrame)
        {
            var frame = new CanFrame
            {
                FrameId = nativeFrame.nFrameID,
                IsExtendedId = nativeFrame.bExtendedID == 1,
                IsRemoteFrame = nativeFrame.nFrameType == 1,
                DataLength = nativeFrame.nDataLength,
                Timestamp = nativeFrame.nRecvTimestamp,
                Data = new byte[8]
            };

            if (nativeFrame.DataBuf != null)
            {
                Array.Copy(nativeFrame.DataBuf, frame.Data, Math.Min(nativeFrame.DataBuf.Length, 8));
            }

            return frame;
        }

        #endregion

        #region 静态工厂方法

        /// <summary>
        /// 创建标准数据帧
        /// </summary>
        public static CanFrame CreateDataFrame(uint frameId, byte[] data)
        {
            var frame = new CanFrame
            {
                FrameId = frameId,
                IsExtendedId = false,
                IsRemoteFrame = false,
                Data = new byte[8]
            };

            if (data != null && data.Length > 0)
            {
                frame.DataLength = (byte)Math.Min(data.Length, 8);
                Array.Copy(data, frame.Data, frame.DataLength);
            }

            return frame;
        }

        /// <summary>
        /// 创建扩展数据帧
        /// </summary>
        public static CanFrame CreateExtendedDataFrame(uint frameId, byte[] data)
        {
            var frame = CreateDataFrame(frameId, data);
            frame.IsExtendedId = true;
            return frame;
        }

        /// <summary>
        /// 创建远程帧
        /// </summary>
        public static CanFrame CreateRemoteFrame(uint frameId, bool isExtended = false)
        {
            return new CanFrame
            {
                FrameId = frameId,
                IsExtendedId = isExtended,
                IsRemoteFrame = true,
                DataLength = 0,
                Data = new byte[8]
            };
        }

        #endregion

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            try
            {
                await DisconnectAsync().ConfigureAwait(false);
            }
            catch
            {
            }
            finally
            {
                try { _lock.Dispose(); } catch { }
            }
        }
    }
}
