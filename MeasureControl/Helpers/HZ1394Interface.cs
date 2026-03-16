using System;
using System.Runtime.InteropServices;

namespace MeasureControl.Helpers
{
    /// <summary>
    /// 怀智1394B板卡DLL接口
    /// </summary>
    public static class HZ1394Interface
    {
        const string DllName = "CC_RN_BM_DLL.dll";

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int Mil1394_Found(ref PCI_DEV_FOUND pDevInfo);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr Mil1394_CC_OPEN(uint Card, uint node);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr Mil1394_RN_OPEN(uint Card, uint node);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int Mil1394_CC_RESET(IntPtr pTNF);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int Mil1394_CC_Close(IntPtr pTNF);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int Mil1394_CC_MSG_STOF_Start(IntPtr pTNF);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int Mil1394_CC_MSG_STOF_Stop(IntPtr pTNF);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int Mil1394_CC_MSG_ASYNC_SEND_Start(IntPtr pTNF);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int Mil1394_CC_MSG_ASYNC_SEND_Stop(IntPtr pTNF);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int Mil1394_CC_MSG_ASYNC_RECV_Start(IntPtr pTNF);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int Mil1394_CC_MSG_ASYNC_RECV_Stop(IntPtr pTNF);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int Mil1394_CC_MSG_ASYNC_RECV_CHANNEL(IntPtr pTNF, uint channel);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int Mil1394_CC_MSG_Speed_Set(IntPtr pTNF, uint SpeedSel);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int Mil1394_CC_MSG_ASYNC_RECV_CFG_SET(IntPtr pTNF, uint[] pCFG_RX_MessageID, uint[] pCFG_RX_MessageLen, uint[] pCFG_RX_BufferPoint, uint RX_Count);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int Mil1394_CC_SYSCFG_STOF_Period_Get(IntPtr pTNF, out uint value);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int Mil1394_CC_BM_ENABLE(IntPtr pTNF, uint Enable);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int Mil1394_CC_SYSCFG_STOF_Period_Set(IntPtr pTNF, uint value);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int Mil1394_CC_SIM_Node_STOFCNT_Clr(IntPtr pTNF);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int Mil1394_CC_MSG_RCV_STOF_ENABLE(IntPtr pTNF, uint Enable);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int Mil1394_CC_SYSCFG_STOF_SEND_STYLE_Set(IntPtr pTNF, uint style, uint count);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int Mil1394_CC_MSG_Cnt_Get(IntPtr pTNF, uint type, out uint pdata);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int Mil1394_CC_MSG_STOF_Data_Set(IntPtr pTNF, uint SysCntType, ref TNF_Stof_Struct pstof);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int Mil1394_CC_MSG_ASYNC_Data_Set(IntPtr pTNF, uint SndMode, uint ID, TNF_ASYNC_Struct[] pASYNC, uint len);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int Mil1394_CC_Packet_Get(IntPtr pTNF, ref IntPtr msg);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int Mil1394_CC_TYPOLOGY_Get(IntPtr pTNF, out uint Refresh, uint Length, uint[] data);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int Mil1394_CC_SIM_Node_NodeID_Get(IntPtr pTNF, out uint nodeId);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int Mil1394_CC_SIM_BusReset_Drv(IntPtr pTNF, uint type);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr StartRecvThd(IntPtr pTNF);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int StopRecvThd(IntPtr pTNF);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int Mil1394_CRB_LRTC_ENABLE(IntPtr pTNF, uint enable);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int Mil1394_RN_MSG_ASYNC_SEND_Stop(IntPtr pTNF);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int Mil1394_CC_RN_SYNSel_Set(IntPtr pTNF, uint ctrl_cmd, uint SelfPeriod);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int Mil1394_CC_SIM_ERR_Start(IntPtr pTNF, uint ctrl_cmd);
    }

    /// <summary>
    /// PCI设备信息结构
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct PCI_DEV_FOUND
    {
        public uint DevNum;              // 设备数
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public uint[] DevType;           // 设备类型
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public uint[] DevNodeNum;        // 每个设备节点数
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public uint[] DevSN;             // 每个设备序号
    }

    /// <summary>
    /// 异步流结构
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct TNF_ASYNC_Struct
    {
        public uint Channel;
        public uint MessageID;
        public uint Security;
        public uint NodeID;
        public uint Priority;
        public uint PayloadDataLength;
        public uint HealthStatusWord;
        public uint HeartBeatWord;
        public uint HeartBeatStyle;      // 1--auto 0--musual
        public uint HeartBeatEnable;
        public uint HeartBeatStep;
        public uint STOFTransmitOffset;
        public uint STOFReceiveOffset;
        public uint STOFPHMOffset;
        public uint STOFCCSendOffset;
        public uint CRCASYNC;            // CRC故障是否注入
        public uint VPCASYNC;            // VPC错误取反位
        public uint VPCErrorEnable;      // 单包VPC错误使能
        public uint VPCASYNCValue;       // VPC的值
        public uint ErrMode;
        public uint ErrNum;
        public uint SoftVPCenable;       // 软件VPC写1使能
        public uint MessageType;         // 0为异步流，1--PHM消息
        public uint MessageDataLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 500)]
        public uint[] MessageData;       // 消息数据
    }

    /// <summary>
    /// STOF结构
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct TNF_Stof_Struct
    {
        public uint STOFPayload0;
        public uint STOFPayload1;
        public uint STOFPayload2;
        public uint STOFPayload3;
        public uint STOFPayload4;
        public uint STOFPayload5;
        public uint STOFPayload6;
        public uint STOFPayload7;
        public uint STOFPayload8;
        public uint STOFVPC;
    }

    /// <summary>
    /// 接收数据包结构
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct TNF_RECV_PACKET_Struct
    {
        public ulong LRTC;
        public uint RTC;
        public uint length;
        public uint MessageTYPE;         // 0 为STOF包  1 异步流包  2 事件 3 总线复位
        public IntPtr Next;
        // STOF包
        public uint STOFPayload0;
        public uint STOFPayload1;
        public uint STOFPayload2;
        public uint STOFPayload3;
        public uint STOFPayload4;
        public uint STOFPayload5;
        public uint STOFPayload6;
        public uint STOFPayload7;
        public uint STOFPayload8;
        public uint STOFVPC;
        public uint CRCErrSTOF;
        public uint VPCErrSTOF;
        // 异步流包
        public uint Channel;
        public uint MessageID;
        public uint Security;
        public uint NodeID;
        public uint Priority;
        public uint PayloadDataLength;
        public uint HealthStatusWord;
        public uint HeartBeatWord;
        public uint STOFTransmitOffset;
        public uint STOFReceiveOffset;
        public uint STOFPHMOffset;
        public uint CRCErrASYNC;         // CRC故障是否有 0--包头 1--数据包头
        public uint VPCASYNC;            // VPC值，0--该位正常 1--该位取反
        public uint VPCErrASYNC;        // VPC错 0----无错 1--有错
        public uint MsgSpeed;
        public uint PacketFlag;          // 收发标志，0是发送
        public uint STOFLIMITErr;
        public uint SOFTVPCErr;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 500)]
        public uint[] MessageData;
    }
}
