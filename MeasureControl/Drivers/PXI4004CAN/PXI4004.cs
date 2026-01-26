using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.InteropServices;

namespace MeasureControl.Drivers.PXI4004CAN
{
    public partial class PXI4004
    {
        // ########################## 数据类型定义 ##########################
        // 8位有符号整型数据
        //public typedef char I8;
        // 8位无符号整型数据
        //public typedef byte U8;
        // 16位有符号整型数据
        //public typedef short I16;
        // 16位无符号整型数据
        //public typedef ushort U16;
        // 32位有符号整型数据
        //public typedef int I32;
        // 32位无符号整型数据
        //public typedef uint U32;
        // 64位有符号整型数据
        //public typedef long I64;
        // 64位无符号整型数据
        //public typedef ulong U64;
        // 32位浮点数据
        //public typedef float F32;
        // 64位浮点数据
        //public typedef double F64;

        // ########################## CAN帧数据结构 ##########################
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct ARTCANX1_CAN_FRAME
        {
            public uint nFrameID;           // 帧ID
            public byte bExtendedID;         // 是否为扩展帧ID，0表示标准帧，1表示扩展帧
            public byte nFrameType;          // 帧格式，0表示数据帧，1表示远程帧
            public byte nReserved;           // 保留
            public byte nDataLength;         // 数据长度，取值范围[0,8]
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            public byte[] DataBuf;           // 报文数据缓冲
            public ulong nRecvTimestamp;     // 接收帧时间戳，0表示未使用时间戳
        }

        // ########################## CAN参数结构 ##########################
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct ARTCANX1_TRIG_PARAM
        {
            public uint nTriggerType;       // 触发类型
            public uint nTriggerSource;     // 触发源
            public uint nTriggerDir;        // 触发方向
            public uint nReserved0;         // 保留字段
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
            public uint[] nReserved;        // 保留
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct ARTCANX1_CAN_PARAM
        {
            public uint nBaudRate;          // 用户指定波特率，单位:bps
            public uint nActBaudRate;       // CAN工作时真实波特率
            public byte nWorkMode;           // 工作模式
            public byte bRecvTimestampEn;    // 接收数据时标使能
            public byte bAccExtID;           // 是否验收扩展帧ID
            public byte nAccFilterCnt;       // 验收过滤器数量
            public uint nAccCodeA;          // 验收码A
            public uint nAccCodeB;          // 验收码B
            public uint nAccMaskA;          // 屏蔽码A
            public uint nAccMaskB;          // 屏蔽码B
            public uint nFrameInterval;     // 帧发送间隔，单位ms
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 7)]
            public uint[] nReserved1;       // 保留
            public ARTCANX1_TRIG_PARAM SendTrig; // 发送触发参数
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
            public uint[] nReserved2;       // 保留
        }

        // ########################## CAN状态结构 ##########################
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct ARTCANX1_CAN_STATUS
        {
            public uint nChannel;               // 当前通道号
            public uint bTaskDone;              // 采集任务完成标志
            public uint bTriggered;             // 触发标志
            public uint nTaskState;             // 采集任务状态
            public uint nCANState;              // CAN控制器状态
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 15)]
            public uint[] nReserved0;           // 保留

            public uint nRecvedFrameCnt;        // 已接收到的帧数量
            public uint nRecvFrameRemainCnt;    // 接收帧剩余帧数
            public uint nRecvFrameLostCnt;      // 接收帧丢失的帧数
            public uint nRecvFifoOverflowCnt;   // 接收FIFO溢出计数
            public uint nRecvBufOverflowCnt;    // 接收缓冲溢出计数
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 15)]
            public uint[] nReserved1;           // 保留

            public uint nSendFifoUnderflowCnt;  // 发送FIFO下溢计数
            public uint nSendBufUnderflowCnt;   // 发送缓冲下溢计数
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 18)]
            public uint[] nReserved2;           // 保留

            public uint nInitTaskCnt;           // 初始化任务次数
            public uint nReleaseTaskCnt;        // 释放任务次数
            public uint nStartTaskCnt;          // 开始任务次数
            public uint nStopTaskCnt;           // 停止任务次数
            public uint nTransRate;             // 传输速率

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 19)]
            public uint[] nReserved3;           // 保留字段
        }

        // ########################## 常量定义 ##########################
        // CAN工作模式
        public const uint ARTCANX1_CAN_WORKMODE_NORMAL = 0;        // 正常发送
        public const uint ARTCANX1_CAN_WORKMODE_TRIGGER = 1;       // 触发定时发送
        public const uint ARTCANX1_CAN_WORKMODE_LOOPBACK = 2;      // 回环模式

        // CAN帧类型
        public const uint ARTCANX1_CAN_FRAME_TYPE_DATA_FRM = 0x00;     // 数据帧
        public const uint ARTCANX1_CAN_FRAME_TYPE_REMOTE_FRM = 0x01;   // 远程帧

        // CAN验收过滤器数量
        public const uint ARTCANX1_CAN_ACC_NUM_NONE = 0;    // 禁止滤波
        public const uint ARTCANX1_CAN_ACC_NUM_SINGLE = 1;  // 单个验收滤波器
        public const uint ARTCANX1_CAN_ACC_NUM_DOUBLE = 2;  // 两个验收滤波器

        // CAN控制器状态
        public enum CAN_STATE
        {
            ERR_CAN_NONE = 0x00000000,     // 未发现错误
            ERR_CAN_CRC = 0x00000001,      // CRC校验错误
            ERR_CAN_FORM = 0x00000002,     // 表单错误
            ERR_CAN_STUFF = 0x00000004,    // 填充错误
            ERR_CAN_BIT = 0x00000008,      // 位错误
            ERR_CAN_ACK = 0x00000010,      // 确认错误
        }

        // 触发类型
        public const uint ARTCANX1_TRIGTYPE_NONE = 0;               // 无触发
        public const uint ARTCANX1_TRIGTYPE_ANALOG_EDGE = 1;        // 模拟边沿触发
        public const uint ARTCANX1_TRIGTYPE_ANALOG_WIN = 2;         // 模拟窗触发
        public const uint ARTCANX1_TRIGTYPE_DIGIT_EDGE = 3;         // 数字边沿触发
        public const uint ARTCANX1_TRIGTYPE_DIGIT_PATTERN = 4;      // 数字模式触发

        // 触发源
        public const uint ARTCANX1_TRIGSRC_PXI0 = 0;    // PXI0
        public const uint ARTCANX1_TRIGSRC_PXI1 = 1;    // PXI1
        public const uint ARTCANX1_TRIGSRC_PXI2 = 2;    // PXI2
        public const uint ARTCANX1_TRIGSRC_PXI3 = 3;    // PXI3
        public const uint ARTCANX1_TRIGSRC_PXI4 = 4;    // PXI4
        public const uint ARTCANX1_TRIGSRC_PXI5 = 5;    // PXI5
        public const uint ARTCANX1_TRIGSRC_PXI6 = 6;    // PXI6
        public const uint ARTCANX1_TRIGSRC_PXI7 = 7;    // PXI7

        // 触发方向
        public const uint ARTCANX1_TRIGDIR_FALLING = 0;     // 下降沿/低电平
        public const uint ARTCANX1_TRIGDIR_RISING = 1;      // 上升沿/高电平
        public const uint ARTCANX1_TRIGDIR_CHANGING = 2;    // 变化

        // 波特率定义
        public const uint CAN_BAUD_10K = 10000;      // 10Kbps
        public const uint CAN_BAUD_20K = 20000;      // 20Kbps
        public const uint CAN_BAUD_50K = 50000;      // 50Kbps
        public const uint CAN_BAUD_100K = 100000;    // 100Kbps
        public const uint CAN_BAUD_125K = 125000;    // 125Kbps
        public const uint CAN_BAUD_250K = 250000;    // 250Kbps
        public const uint CAN_BAUD_500K = 500000;    // 500Kbps
        public const uint CAN_BAUD_800K = 800000;    // 800Kbps
        public const uint CAN_BAUD_1M = 1000000;     // 1Mbps

        // ########################## DLL导入函数 ##########################
        // 设备管理函数
        [DllImport("ARTCANX1_64.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern IntPtr ARTCANX1_DEV_Create(ulong nProductSerialNum);

        [DllImport("ARTCANX1_64.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern bool ARTCANX1_DEV_Release(IntPtr hDevice);

        // 辅助函数：获取 DLL 版本与最后错误码
        [DllImport("ARTCANX1_64.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern ulong ARTCANX1_AUX_GetDllVersion();

        [DllImport("ARTCANX1_64.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern uint ARTCANX1_AUX_GetLastError();

        [DllImport("ARTCANX1_64.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern uint ARTCANX1_DEV_GetCount(uint nBusType, uint nDeviceId);

        // CAN通信函数
        [DllImport("ARTCANX1_64.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern bool ARTCANX1_CAN_GetParam(IntPtr hDevice, uint nChannel, ref ARTCANX1_CAN_PARAM pCANParam);

        [DllImport("ARTCANX1_64.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern bool ARTCANX1_CAN_InitTask(IntPtr hDevice, uint nChannel, ref ARTCANX1_CAN_PARAM pCANParam, IntPtr pSampEvent);

        [DllImport("ARTCANX1_64.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern bool ARTCANX1_CAN_StartTask(IntPtr hDevice, uint nChannel);

        [DllImport("ARTCANX1_64.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern bool ARTCANX1_CAN_WriteFrame(IntPtr hDevice, uint nChannel, ref ARTCANX1_CAN_FRAME pFrameBuf, uint nFrameCount, ref uint pRetFrameCount, uint[] StatusBuf, double fTimeout);

        [DllImport("ARTCANX1_64.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern bool ARTCANX1_CAN_ReadFrame(IntPtr hDevice, uint nChannel, ref ARTCANX1_CAN_FRAME pFrameBuf, uint nFrameCount, ref uint pRetFrameCount, ref uint pRecvFrameRemainCnt, double fTimeout);

        [DllImport("ARTCANX1_64.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern bool ARTCANX1_CAN_GetStatus(IntPtr hDevice, uint nChannel, ref ARTCANX1_CAN_STATUS pCANStatus);

        [DllImport("ARTCANX1_64.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern bool ARTCANX1_CAN_StopTask(IntPtr hDevice, uint nChannel);

        [DllImport("ARTCANX1_64.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern bool ARTCANX1_CAN_ReleaseTask(IntPtr hDevice, uint nChannel);

        [DllImport("ARTCANX1_64.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern bool ARTCANX1_CAN_VerifyParam(IntPtr hDevice, uint nChannel, ref ARTCANX1_CAN_PARAM pCANParam);

        // ########################## 辅助方法 ##########################
        /// <summary>
        /// 创建设备句柄
        /// </summary>
        /// <param name="serialNum">设备序列号，0表示第一个设备</param>
        /// <returns>设备句柄，失败返回IntPtr.Zero</returns>
        public static IntPtr CreateDevice(ulong serialNum = 0)
        {
            IntPtr h = ARTCANX1_DEV_Create(serialNum);
            // 原生库在失败时返回 INVALID_HANDLE_VALUE (-1)，而非 IntPtr.Zero
            if (h == new IntPtr(-1))
            {
                // 不在此处抛出异常，改为返回 IntPtr.Zero，调用方可通过 ARTCANX1_AUX_GetLastError() 获取错误码
                return IntPtr.Zero;
            }
            return h;
        }

        /// <summary>
        /// 释放设备句柄
        /// </summary>
        /// <param name="hDevice">设备句柄</param>
        /// <returns>是否成功</returns>
        public static bool ReleaseDevice(IntPtr hDevice)
        {
            // 把 INVALID_HANDLE_VALUE(-1) 和 IntPtr.Zero 都视为无效句柄
            if (hDevice == IntPtr.Zero || hDevice == new IntPtr(-1))
                return false;
            return ARTCANX1_DEV_Release(hDevice);
        }

        /// <summary>
        /// 初始化CAN参数为默认值
        /// </summary>
        /// <param name="hDevice">设备句柄</param>
        /// <param name="channel">CAN通道号</param>
        /// <returns>CAN参数结构体</returns>
        public static ARTCANX1_CAN_PARAM GetDefaultCANParam(IntPtr hDevice, uint channel)
        {
            ARTCANX1_CAN_PARAM param = new ARTCANX1_CAN_PARAM();
            if (ARTCANX1_CAN_GetParam(hDevice, channel, ref param))
            {
                return param;
            }
            throw new Exception("获取CAN默认参数失败");
        }

        /// <summary>
        /// 初始化CAN任务
        /// </summary>
        /// <param name="hDevice">设备句柄</param>
        /// <param name="channel">CAN通道号</param>
        /// <param name="param">CAN参数</param>
        /// <returns>是否成功</returns>
        public static bool InitCAN(IntPtr hDevice, uint channel, ref ARTCANX1_CAN_PARAM param)
        {
            // 验证参数
            if (!ARTCANX1_CAN_VerifyParam(hDevice, channel, ref param))
            {
                throw new Exception("CAN参数验证失败");
            }

            return ARTCANX1_CAN_InitTask(hDevice, channel, ref param, IntPtr.Zero);
        }

        /// <summary>
        /// 启动CAN任务
        /// </summary>
        /// <param name="hDevice">设备句柄</param>
        /// <param name="channel">CAN通道号</param>
        /// <returns>是否成功</returns>
        public static bool StartCAN(IntPtr hDevice, uint channel)
        {
            return ARTCANX1_CAN_StartTask(hDevice, channel);
        }


        /// <summary>
        /// 发送CAN帧
        /// </summary>
        /// <param name="hDevice">设备句柄</param>
        /// <param name="channel">CAN通道号</param>
        /// <param name="frame">CAN帧数据</param>
        /// <param name="timeout">超时时间(秒)</param>
        /// <returns>是否成功</returns>
        public static bool SendFrame(IntPtr hDevice, uint channel, ref ARTCANX1_CAN_FRAME frame, double timeout = 0.1)
        {
            uint retFrameCount = 0;
            uint[] statusBuf = new uint[1];
            return ARTCANX1_CAN_WriteFrame(hDevice, channel, ref frame, 1, ref retFrameCount, statusBuf, timeout);
        }

        /// <summary>
        /// 接收CAN帧
        /// </summary>
        /// <param name="hDevice">设备句柄</param>
        /// <param name="channel">CAN通道号</param>
        /// <param name="frame">接收到的CAN帧</param>
        /// <param name="timeout">超时时间(秒)</param>
        /// <returns>是否成功</returns>
        public static bool ReceiveFrame(IntPtr hDevice, uint channel, ref ARTCANX1_CAN_FRAME frame, double timeout = 0.1)
        {
            uint retFrameCount = 0;
            uint remainCount = 0;
            return ARTCANX1_CAN_ReadFrame(hDevice, channel, ref frame, 1, ref retFrameCount, ref remainCount, timeout);
        }

        /// <summary>
        /// 获取CAN状态
        /// </summary>
        /// <param name="hDevice">设备句柄</param>
        /// <param name="channel">CAN通道号</param>
        /// <returns>CAN状态信息</returns>
        public static ARTCANX1_CAN_STATUS GetCANStatus(IntPtr hDevice, uint channel)
        {
            ARTCANX1_CAN_STATUS status = new ARTCANX1_CAN_STATUS();
            if (ARTCANX1_CAN_GetStatus(hDevice, channel, ref status))
            {
                return status;
            }
            throw new Exception("获取CAN状态失败");
        }

        /// <summary>
        /// 停止CAN任务
        /// </summary>
        /// <param name="hDevice">设备句柄</param>
        /// <param name="channel">CAN通道号</param>
        /// <returns>是否成功</returns>
        public static bool StopCAN(IntPtr hDevice, uint channel)
        {
            return ARTCANX1_CAN_StopTask(hDevice, channel);
        }

        /// <summary>
        /// 释放CAN任务
        /// </summary>
        /// <param name="hDevice">设备句柄</param>
        /// <param name="channel">CAN通道号</param>
        /// <returns>是否成功</returns>
        public static bool ReleaseCAN(IntPtr hDevice, uint channel)
        {
            return ARTCANX1_CAN_ReleaseTask(hDevice, channel);
        }

        /// <summary>
        /// 创建标准数据帧
        /// </summary>
        /// <param name="frameId">帧ID</param>
        /// <param name="data">数据内容</param>
        /// <returns>CAN帧结构体</returns>
        public static ARTCANX1_CAN_FRAME CreateDataFrame(uint frameId, byte[] data)
        {
            ARTCANX1_CAN_FRAME frame = new ARTCANX1_CAN_FRAME();
            frame.nFrameID = frameId;
            frame.bExtendedID = 0; // 标准帧
            frame.nFrameType = (byte)ARTCANX1_CAN_FRAME_TYPE_DATA_FRM;
            frame.nReserved = 0;

            if (data != null && data.Length > 0)
            {
                frame.nDataLength = (byte)Math.Min(data.Length, 8);
                frame.DataBuf = new byte[8];
                Array.Copy(data, frame.DataBuf, frame.nDataLength);
            }
            else
            {
                frame.nDataLength = 0;
                frame.DataBuf = new byte[8];
            }

            frame.nRecvTimestamp = 0;
            return frame;
        }

        /// <summary>
        /// 创建扩展数据帧
        /// </summary>
        /// <param name="frameId">帧ID</param>
        /// <param name="data">数据内容</param>
        /// <returns>CAN帧结构体</returns>
        public static ARTCANX1_CAN_FRAME CreateExtendedDataFrame(uint frameId, byte[] data)
        {
            ARTCANX1_CAN_FRAME frame = CreateDataFrame(frameId, data);
            frame.bExtendedID = 1; // 扩展帧
            return frame;
        }

        /// <summary>
        /// 创建远程帧
        /// </summary>
        /// <param name="frameId">帧ID</param>
        /// <param name="isExtended">是否为扩展帧</param>
        /// <returns>CAN帧结构体</returns>
        public static ARTCANX1_CAN_FRAME CreateRemoteFrame(uint frameId, bool isExtended = false)
        {
            ARTCANX1_CAN_FRAME frame = new ARTCANX1_CAN_FRAME();
            frame.nFrameID = frameId;
            frame.bExtendedID = (byte)(isExtended ? 1 : 0);
            frame.nFrameType = (byte)ARTCANX1_CAN_FRAME_TYPE_REMOTE_FRM;
            frame.nReserved = 0;
            frame.nDataLength = 0; // 远程帧数据长度为0
            frame.DataBuf = new byte[8];
            frame.nRecvTimestamp = 0;
            return frame;
        }
    }
}
