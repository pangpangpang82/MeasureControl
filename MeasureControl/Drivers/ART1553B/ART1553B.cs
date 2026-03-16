using System;
using System.Runtime.InteropServices;

namespace MeasureControl.Drivers
{
    // Minimal stub of ART1553B native wrapper to allow compilation.
    // This file provides basic types, constants and stubbed APIs.
    public static class ART1553B
    {
        // ########################## 设备功能指标 ########################
        // BC消息类型定义
        public const Int32 BC_MSGTYPE_BCRT			=	0x00;		// BC->RT or RT->BC
        public const Int32 BC_MSGTYPE_RTRT			=	0x01;		// RT->RT
        public const Int32 BC_MSGTYPE_BROADCAST		=   0x02;		// BC->RTs广播
        public const Int32 BC_MSGTYPE_RTRTS			=   0x03;		// RT->RTs
        public const Int32 BC_MSGTYPE_MODECODE		=	0x04;		// Mode Code
        public const Int32 BC_MSGTYPE_BROADCASTMODE	=   0x06;		// Broadcast Mode Code

        // 方式代码(Mode Code)定义
        public const Int32 MODECODE_DYNCBUSCONTROL	=			0x00;	// 动态总线控制 发/不带数据字/不允许广播
        public const Int32 MODECODE_SYNC_TX			=		    0x01;	// 同步 发/不带数据字/允许广播
        public const Int32 MODECODE_TXPREVSTATUS	=			0x02;	// 发送上一状态字 发/不带数据字/不允许广播
        public const Int32 MODECODE_SELFTEST		=			0x03;	// 启动自测试 发/不带数据字/允许广播
        public const Int32 MODECODE_XOFF			=			0x04;	// 发送器关闭 发/不带数据字/允许广播
        public const Int32 MODECODE_CANCELXOFF		=			0x05;	// 取消发送器关闭 发/不带数据字/允许广播
        public const Int32 MODECODE_DISABLETERMINALFLAG	=	    0x06;	// 禁止终端标志 发/不带数据字/允许广播
        public const Int32 MODECODE_CANCELDISABLETERMINALFLAG=	0x07;	// 取消禁止终端标志 发/不带数据字/允许广播
        public const Int32 MODECODE_RESETTERMINAL			=	0x08;	// 复位终端标志 发/不带数据字/允许广播
        public const Int32 MODECODE_TXVECTOR				=	0x10;	// 发送矢量字 发/带数据字/不允许广播
        public const Int32 MODECODE_SYNC_RX					=   0x11;	// 同步 收/带数据字/不允许广播
        public const Int32 MODECODE_TXPREVCOMMANDWORD		=	0x12;	// 发送上一指令字 发/带数据字/不允许广播
        public const Int32 MODECODE_TXSELFDETECTWORD		=	0x13;	// 发送自检字 发/带数据字/不允许广播
        public const Int32 MODECODE_SELECTXOFF				=	0x14;	// 选定的发送器关闭 收/带数据字/允许广播
        public const Int32 MODECODE_CANCELSELECTXOFF		=	0x15;	// 取消选定的发送器关闭 收/带数据字/允许广播

        // BC Block Status每个bit位含义
        public const Int32 BC_BLOCK_STATUS_BIT0_WD_ERR		=	0x0001;	// 同步字 曼彻斯特编码 校验或者位长度有误
        public const Int32 BC_BLOCK_STATUS_BIT1_SYN_ERR		=   0x0002;	// 同步字头有误
        public const Int32 BC_BLOCK_STATUS_BIT2_LEN_ERR		=   0x0004;	// 字长有误
        public const Int32 BC_BLOCK_STATUS_BIT3_AD_ERR		=	0x0008;	// RT状态字地址有误
        public const Int32 BC_BLOCK_STATUS_BIT4_GOOD		=	0x0010;	// 消息传输正常
        public const Int32 BC_BLOCK_STATUS_BIT5_RETRYCNT1	=	0x0020;	// 消息重试1次
        public const Int32 BC_BLOCK_STATUS_BIT56_RETRYCNT2	=	0x0060;	// 消息重试2次
        public const Int32 BC_BLOCK_STATUS_BIT7_MSKSTSSET	=	0x0080;	// RT状态字中屏蔽的位有非0的位
        public const Int32 BC_BLOCK_STATUS_BIT8_CMD_ERR		=   0x0100;	// 发送的命令字有误
        public const Int32 BC_BLOCK_STATUS_BIT9_TIMEOUT		=   0x0200;	// RT响应超时
        public const Int32 BC_BLOCK_STATUS_BIT10_FMT_ERR	=	0x0400;// 消息传输中同步字 曼彻斯特编码 校验 位长度 字长度或RT状态字地址存在错误
        public const Int32 BC_BLOCK_STATUS_BIT11_STS_SET	=	0x0800;	// RT的状态字的低11位存在非0的位并且这个位在消息控制字中没有屏蔽
        public const Int32 BC_BLOCK_STATUS_BIT12_ERR_FLAG	=	0x1000;	// 消息错误
        public const Int32 BC_BLOCK_STATUS_BIT13_CHNB		=	0x2000;	// B通道
        public const Int32 BC_BLOCK_STATUS_BIT14_SOM		=	0x4000;	// 消息开始
        public const Int32 BC_BLOCK_STATUS_BIT15_EOM		=	0x8000;	// 消息结束

        // RT Block Status每个bit位含义
        public const Int32 RT_BLOCK_STATUS_BIT0_CMD_ERR		=   0x0001;	// 命令字错误标志
        public const Int32 RT_BLOCK_STATUS_BIT1_CMD2_ERR	=	0x0002;	// RT到RT传输中第二个命令字错误标志
        public const Int32 RT_BLOCK_STATUS_BIT2_AD_ERR		=	0x0004;	// 状态字地址错误标志
        public const Int32 RT_BLOCK_STATUS_BIT3_WD_ERR		=	0x0008;	// 数据错误标志
        public const Int32 RT_BLOCK_STATUS_BIT4_SYN_ERR		=   0x0010;	// 同步字头错误标志
        public const Int32 RT_BLOCK_STATUS_BIT5_WL_ERR		=	0x0020;	// 数据长度错误标志
        public const Int32 RT_BLOCK_STATUS_BIT6_CMDILL		=	0x0040;	// 非法指令标志
        public const Int32 RT_BLOCK_STATUS_BIT7_ROB			=   0x0080;	// 循环缓冲模式下缓冲区溢出标志
        public const Int32 RT_BLOCK_STATUS_BIT9_TIMEOUT		=   0x0200;	// RT->RT传输超时
        public const Int32 RT_BLOCK_STATUS_BIT10_FMT_ERR	=	0x0400;	// BOT0~BIT5只要有一个位是1则此位是1
        public const Int32 RT_BLOCK_STATUS_BIT11_RT_RT		=	0x0800;	// RT->RT传输超时
        public const Int32 RT_BLOCK_STATUS_BIT12_ERR_FLAG	=	0x1000;	// BIT9或BIT10至少1个为1
        public const Int32 RT_BLOCK_STATUS_BIT13_CHNB		=	0x2000;	// B通道 此位为0表示A通道

        // BM Block Status每个bit位含义
        public const Int32 BM_BLOCK_STATUS_BIT0_CMD_ERR		=   0x0001;	// 命令字错误
        public const Int32 BM_BLOCK_STATUS_BIT1_CMD2_ERR	=	0x0002;	// RT->RT传输中第二个命令字错误
        public const Int32 BM_BLOCK_STATUS_BIT2_AD_ERR		=	0x0004;	// 状态字地址错误标志
        public const Int32 BM_BLOCK_STATUS_BIT3_WD_ERR		=	0x0008;	// 数据错误标志
        public const Int32 BM_BLOCK_STATUS_BIT4_SYN_ERR		=   0x0010;	// 同步字头错误标志
        public const Int32 BM_BLOCK_STATUS_BIT5_LEN_ERR		=   0x0020;	// 数据长度错误标志
        public const Int32 BM_BLOCK_STATUS_BIT6_ROB			=   0x0040;	// 循环缓冲模式下缓冲区溢出标志
        public const Int32 BM_BLOCK_STATUS_BIT7_GOOD		=	0x0080;	// 消息传输正常
        public const Int32 BM_BLOCK_STATUS_BIT9_TIMEOUT		=   0x0200;	// RT->RT传输超时
     
        // 获取设备列表使用结构体
        public  struct ART1553B_DEV_INFO
        {
	        public UInt32 nSerialCode;					// 设备序列号
	        public UInt32 nDeviceType;					// 设备类别
	        public Int32  bUsed;						// 设备是否已被使用
	        public UInt32 nReserved;					// 预留
        }

        // 定义时标结构体
        public  struct TimeTag
        {
	        public UInt16 nYear;		// 年
	        public UInt16 nMonth;		// 月
	        public UInt16 nDay;		// 日
	        public UInt16 nHour;		// 时
	        public UInt16 nMinute;		// 分
	        public UInt16 nSecond;		// 秒
	        public UInt16 nMillSec;	// 毫秒
        }

        //Self structure definition
        public  struct INTERRUPT_STRUCT
        {
	        public Int32 BC_MSGInt;		// BC消息中断请求
	        public Int32 BC_FRMInt;		// BC消息帧结束中断请求
	        public Int32 RT_Int;		// RT消息中断请求
	        public Int32 BM_Int;		// BM消息监视中断请求
        }

        public  struct INTERRUPT_MASK_REGISTER_STRUCT
        {
	        public Int32 BC_MsgOver;			// BC消息结束中断使能
	        public Int32 BC_STOP;				// BC消息出错停止中断使能
	        public Int32 BC_EndOfList;			// BC消息帧结束中断使能
	        public Int32 BC_EndOfMinorFrame;
	        public Int32 BC_SoftInt;
	        public Int32 RT_RMsg;				// RT接收到一条接收数据中断使能
	        public Int32 RT_TMsg;				// RT接收到一条发送数据中断使能
       }

        public  struct STOP_ON_ERR_STRUCT
        {
	        public Int32 MSG_STOP_ON_ERR;		// TRUE-消息出错(包括字错误、帧格式错误、超时错误)时停止消息处理 但如果重试使能 那么先重试 重试还有错误再停止
	        public Int32 FRAME_STOP_ON_ERR;		// TRUE-在自动重发模式下 出错时消息帧停止
        }

        public  struct STATUS_SET_STRUCT
        {
	        public Int32 Stop_On_MSG;		// TRUE-如果RT状态字中STATUS_SET置位 在处理完本条消息后将停止消息处理
	        public Int32 Stop_On_Frame;		// TRUE-如果RT状态字中STATUS_SET置位，在处理完本帧后将停止帧处理
        }

        public  struct RETRY_CASE_STRUCT
        {
	        public Int32 Retry_IF_MSGErr;		// RT状态字中的Message Error位为1
	        public Int32 Retry_IF_StatusSet;	// RT的状态字被置位
        }

        public  struct RETRY_CHANNEL_SEL_STRUCT
        {
	        public Int32 Alter_Chan_On_Busy1;
	        public Int32 Alter_Chan_On_Busy2;
        }

        public  struct CONTROL_WORD_STRUCT
        {
	        public Int32 Retry;			// 消息重试允许位 TRUE：允许消息重试
	        public Byte  ChanSel;		// 消息发送时的通道选择 0:Channel B 1:Channel A
	        public Byte MsgFmt;		// 设置消息的类型 参照以下BC消息类型定义 0x05:保留 0x07:保留
        }

        public  struct MSG_DESCRIPTOR_STRUCT
        {
	        public UInt16 CmdWord1;		// 命令字1
	        public UInt16 CmdWord2;		// 命令字2，在RT-RT的消息类型时此命令字为发送命令字，此时命令字1就为接收命令字
	        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
            public UInt16[] Datablk;	// 消息的数据字
	        public UInt16 StatusWord1;	// 状态字1
	        public UInt16  StatusWord2;	// 状态字2，在RT-RT的消息类型时此状态字为接收状态字，此时状态字1为发送状态字
        }

        public  struct SMSG_STRUCT
        {
	       public CONTROL_WORD_STRUCT CtlWord;		// BC控制字结构变量
	       public MSG_DESCRIPTOR_STRUCT MsgBlock;		// BC消息描述结构变量
	       public Int32  MsgGap;						// 消息间间隔，分辨率为1us 最小为4us
	       public UInt16  Period;						// 消息发送周期，分辨率为1ms（Period=0为事件消息，否则为周期消息） 未使用
	       public UInt16  InitPeriod;					// 消息发送周期的初始值，单位1ms，可以调整消息运行的初始点 未使用
	       public Int32 Run;							// 消息的初始状态 未使用
        }

        public  struct RMSG_STRUCT
        {
	        public UInt16  BSW;						// Block Status Word，BC消息块状态描述字 BC_BLK_STATUS
	        public UInt16  RTRT;						// 是否是RT->RT 0-否 1-是
	        public UInt16  RTRTs;						// 是否是RT->RTS 0-否 1-是
	        public TimeTag TimeTag;				// 时标，分辨率20us,在非时标模式下，该项无意义，为0
	        public MSG_DESCRIPTOR_STRUCT MsgBlock;	// 消息描述符结构变量，用来存放消息的命令字、状态字、数据字
        }

        public  struct RT_STATUS_WORD_STRUCT
        {
	        public Int32 TerminalFlag;		// 终端标志 1-远程终端有错误
	        public Int32 DBusCtl;			// 动态总线控制接受位
	        public Int32 SubSystemFlag;		// 子系统标志
	        public Int32 Busy;				// 忙位
	        public Int32 ServiceReq;		// 服务请求位
	        public Int32 TestFlag;			// 测试状态位
	        public Int32 ErrorFlag;			// 消息差错位
        }

        public  struct RT_Illegal_CMD_TABLE_STRUCT
        {
             [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32 * 2 * 32)]
	        public Int32[,] CmdTable;	// CmdTable[I][J][K] I-RT地址 J-发送或接收 K-子地址
        }	// CmdTable[3][1][20] = 0x00000001表示RT地址为3，子地址为20，发送32个数据的命令为非法

        public  struct RT_TX_MODE_STRUCT
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32 *32)]
	        public Byte[,] TxMode;//1:Circular buffer 0:Single buffer
        }

        public  struct BM_CMD_FILTER_TABLE_STRUCT
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32 *2)]
	        public Int32[,] Filter; // Filter[I][J]:I代表监测的远程终端地址，J=0接收 J=1发送 数组值：DO位和D31位表示方式代码是否被监测 D1位表示子地址为01的远程终端是否被监测
        }
        // ####################接口函数返回错误码定义##########################
        public const Int32 ART1553Success				=				(0);		// 无错误
            
        public const Int32 ART1553Error_InvalidIntPtr		=			(-1000);
        public const Int32 ART1553Error_NULLPtr				=		(-1001);
        public const Int32 ART1553Error_ExceedLimit			=		(-1002);
        public const Int32 ART1553Error_GetPhysicalID		=			(-1003);
        public const Int32 ART1553Error_SetPhysicalID		=			(-1004);
        public const Int32 ART1553Error_GetSerialNumber		=		(-1005);
        public const Int32 ART1553Error_SetSerialNumber		=		(-1006);
        public const Int32 ART1553Error_OpenDevice			=			(-2000);
        public const Int32 ART1553Error_CloseDevice			=		(-2001);
        public const Int32 ART1553Error_GetVersion			=			(-2002);
        public const Int32 ART1553Error_Reset				=			(-2003);
        public const Int32 ART1553Error_TimeTagEnable		=			(-2004);
        public const Int32 ART1553Error_TimeTagDisable		=			(-2005);
        public const Int32 ART1553Error_SetTimeout			=			(-2006);
        public const Int32 ART1553Error_Timeout				=		(-2007);
        public const Int32 ART1553Error_Address				=		(-2008);
        public const Int32 ART1553Error_Rate				=			(-2009);
            
        public const Int32 ART1553Error_SetINT				=			(-3000);
        public const Int32 ART1553Error_CreateINT			=			(-3001);
        public const Int32 ART1553Error_GetINT				=			(-3002);
        public const Int32 ART1553Error_ClearINT			=			(-3003);

        public const Int32 ART1553Error_SetPStack			=			(-4000);
        public const Int32 ART1553Error_SetInitPStack		=			(-4001);
        public const Int32 ART1553Error_SetEndPStack		=			(-4002);
        public const Int32 ART1553Error_SetPData			=			(-4003);
        public const Int32 ART1553Error_SetPEndData			=		(-4004);
        public const Int32 ART1553Error_GetPStack			=			(-4005);
        public const Int32 ART1553Error_GetInitPStack		=			(-4006);
        public const Int32 ART1553Error_GetEndPStack		=			(-4007);
        public const Int32 ART1553Error_WriteData			=			(-4008);
        public const Int32 ART1553Error_ReadData			=			(-4009);
        public const Int32 ART1553Error_TxRxBit				=		(-4010);
        public const Int32 ART1553Error_MsgType				=		(-4011);
        public const Int32 ART1553Error_ModeCodeType		=			(-4012);
        public const Int32 ART1553Error_GetStatus			=			(-4013);
        public const Int32 ART1553Error_ClearTag			=			(-4014);
        public const Int32 ART1553Error_LoadTag				=		(-4015);

        public const Int32 ART1553Error_BCSetMsgGap			=		(-5000);
        public const Int32 ART1553Error_BCSetFrameGap		=			(-5001);
        public const Int32 ART1553Error_BCMsgCount			=			(-5002);
        public const Int32 ART1553Error_BCInitMsgCount		=		(-5003);
        public const Int32 ART1553Error_BCAutoFrameReTxCount =			(-5004);
        public const Int32 ART1553Error_BCGetCfg			=			(-5005);
        public const Int32 ART1553Error_BCSetCfg			=			(-5006);
        public const Int32 ART1553Error_BCReadMsg			=			(-5007);
        public const Int32 ART1553Error_BCMsgDone			=			(-5008);
        public const Int32 ART1553Error_BCRun				=			(-5009);
        public const Int32 ART1553Error_BCStop				=			(-5010);
        public const Int32 ART1553Error_BCEnable			=			(-5011);
        public const Int32 ART1553Error_BCDisable			=			(-5012);
        public const Int32 ART1553Error_BCNoNewMsg			=			(-5013);

        public const Int32 ART1553Error_RTSetCfg			=			(-6000);
        public const Int32 ART1553Error_RTSetStatus			=		(-6001);
        public const Int32 ART1553Error_RTSetTxTab			=			(-6002);
        public const Int32 ART1553Error_RTCodeTab			=			(-6003);
        public const Int32 ART1553Error_RTIllegalTab		=			(-6004);
        public const Int32 ART1553Error_RTMultiEnable		=			(-6005);
        public const Int32 ART1553Error_RTGetTxTab			=			(-6006);
        public const Int32 ART1553Error_RTMultiAddr			=		(-6007);
        public const Int32 ART1553Error_RTAddr				=			(-6008);
        public const Int32 ART1553Error_RTEnable			=			(-6009);
        public const Int32 ART1553Error_RTStart				=		(-6010);
        public const Int32 ART1553Error_RTStop				=			(-6011);
        public const Int32 ART1553Error_RTDisable			=			(-6012);
        public const Int32 ART1553Error_RTGetStatus			=		(-6013);
        public const Int32 ART1553Error_RTGetCfg			=			(-6014);
        public const Int32 ART1553Error_RTSetVectorWord		=		(-6015);
        public const Int32 ART1553Error_RTSetBitWord		=			(-6016);
        public const Int32 ART1553Error_RTGetMsg			=			(-6017);
        public const Int32 ART1553Error_RTNoNewMsg			=			(-6018);
            
        public const Int32 ART1553Error_BMFilterTab			=		(-7000);
        public const Int32 ART1553Error_BMEnable			=		(-7001);
        public const Int32 ART1553Error_BMDisable			=		(-7002);
        public const Int32 ART1553Error_BMNoMsg				=		(-7003);

        //######################## 常规通用函数 #################################
        // 枚举设备信息 devInfo-设备信息数组 nArraySize-设备信息数组大小 pDeviceNum-返回的实际设备数
        [DllImport("ART1553B_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 ART1553B_DeviceList(ART1553B_DEV_INFO[] devInfo, Byte nArraySize, ref Byte pDeviceNum);

        // 创建设备 使用序列号
         [DllImport("ART1553B_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 ART1553B_Open(ref IntPtr  hDevice, UInt32 nSerialNumber);

        // 创建设备 逻辑号或者物理号
         [DllImport("ART1553B_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 ART1553B_OpenEx(ref IntPtr hDevice, UInt32 nID, Boolean bPhysical);

        // 获取物理ID
         [DllImport("ART1553B_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 ART1553B_GetPhysicalID(IntPtr hDevice, ref UInt32 physicalID);

        // 设置物理ID
         [DllImport("ART1553B_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 ART1553B_SetPhysicalID(IntPtr hDevice, UInt32 physicalID, UInt32 password);

        // 获取板卡系列号
         [DllImport("ART1553B_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 ART1553B_GetSerialNum(IntPtr hDevice, ref  UInt32 pSN);

        // 设置板卡系列号
        [DllImport("ART1553B_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 ART1553B_SetSerialNum(IntPtr hDevice, UInt32 SN, UInt32 password);

        // 获取总线信息 总线号 功能号 设备号 Revision ID
        [DllImport("ART1553B_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 ART1553B_GetBusInfo(IntPtr hDevice, ref  UInt32 pBusNumber, ref  UInt32 pFunctionNumber, ref  UInt32 pDeviceNumber, ref  UInt32 pRevisionID);

        // 获取版本信息 固件版本 驱动版本
        [DllImport("ART1553B_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 ART1553B_GetDevVersion(IntPtr hDevice, ref  UInt64 pulFmwVersion, ref  UInt32 pulDriverVersion);

        // 关闭设备
        [DllImport("ART1553B_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 ART1553B_Close(IntPtr hDevice);

        // 设备复位
        [DllImport("ART1553B_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 ART1553B_Reset(IntPtr hDevice);

        // 通道复位
        [DllImport("ART1553B_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 ART1553B_ChannelReset(IntPtr hDevice,Int32 nChanNo);

        // 启动或停止时标功能
        [DllImport("ART1553B_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 ART1553B_TimeTagStart(IntPtr hDevice, Int32 nChanNo, Boolean bEnable);

        // 设置中断屏蔽寄存器
        [DllImport("ART1553B_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 ART1553B_SetIntMaskReg(IntPtr hDevice, Int32 nChanNo, ref INTERRUPT_MASK_REGISTER_STRUCT pIntReg);

        // 设置中断
        [DllImport("ART1553B_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 ART1553B_SetINT(IntPtr hDevice, Int32 nChanNo, ref INTERRUPT_STRUCT pIntReg);

        // 创建中断触发事件
        [DllImport("ART1553B_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 ART1553B_CreateIntEvt(IntPtr hDevice, ref IntPtr hEvent);

        // 获取最近被处理消息中断事件 驱动通过该事件来通知应用程序
        [DllImport("ART1553B_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 ART1553B_WaitIntEvt(IntPtr hDevice, Int32 nChanNo, IntPtr hEvent);

        // 关闭中断触发事件
        [DllImport("ART1553B_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 ART1553B_CloseIntEvt(IntPtr hDevice, IntPtr hEvent);

        // 设置速率
        [DllImport("ART1553B_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 ART1553B_SetRate(IntPtr hDevice, Int32 nRate);

        // 获取速率
        [DllImport("ART1553B_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 ART1553B_GetRate(IntPtr hDevice, ref Int32 pRate);

        // ##############BC MODE FUNCTION################
        // BC初始化
        [DllImport("ART1553B_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 BC_Init(IntPtr hDevice, Int32 nChanNo);

        // 设置应答超时 TimeOut0
        [DllImport("ART1553B_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 BC_SetRespTimeout(IntPtr hDevice, Int32 nChanNo, UInt16 lTimeout);

        // 获取BC支持的最大消息数量
        [DllImport("ART1553B_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 BC_GetMaxMsgCnt(IntPtr hDevice, Int32 nChanNo, ref Int32 msgCount);

        // 设置BC帧间隔时间
        [DllImport("ART1553B_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 BC_SetFrameGap(IntPtr hDevice, Int32 nChanNo, Int32 gap);

        // 设置BC重试的次数
        [DllImport("ART1553B_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 BC_SetRetryNum(IntPtr hDevice, Int32 nChanNo, Int32 num);

        // 设置BC重试通道选择
        [DllImport("ART1553B_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 BC_RetryChanSel(IntPtr hDevice, Int32 nChanNo, ref RETRY_CHANNEL_SEL_STRUCT ChanSel);

        // BC配置消息：包括消息块的控制字、命令字、数据字、消息间的间隔、消息的格式、消息的类型(周期消息和事件消息)及消息的周期等
        [DllImport("ART1553B_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 BC_WriteMsg(IntPtr hDevice, Int32 nChanNo, UInt16 MsgId, SMSG_STRUCT[] Msg);

        // 获取消息链表中消息配置信息
        [DllImport("ART1553B_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 BC_GetMsgInfo(IntPtr hDevice, Int32 nChanNo, UInt16 MsgId, ref SMSG_STRUCT Msg);

        // 修改运行中的BC发送的数据字
        [DllImport("ART1553B_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 BC_WriteDataBlock(IntPtr hDevice, Int32 nChanNo, UInt16 MsgId, Byte WordCnt, ref UInt16 DataBuf);

        // 启动BC
        [DllImport("ART1553B_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 BC_Start(IntPtr hDevice, Int32 nChanNo);

        // 停止BC
        [DllImport("ART1553B_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 BC_Stop(IntPtr hDevice, Int32 nChanNo);

        // 获取最近被处理的消息块号
        [DllImport("ART1553B_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 BC_GetLastMsgId(IntPtr hDevice, Int32 nChanNo, ref UInt16 MsgId);

        // 查询BC是否有新消息
        [DllImport("ART1553B_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 BC_IsMsgOver(IntPtr hDevice, Int32 nChanNo);

        // 读取BC消息
        [DllImport("ART1553B_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 BC_ReadMsg(IntPtr hDevice, Int32 nChanNo, UInt16 MsgId, ref RMSG_STRUCT pMsg);

        // BC是否运行
        [DllImport("ART1553B_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 BC_IsRunning(IntPtr hDevice, Int32 nChanNo);

        // #############REMOTE TERMINAL MODE FUNCTION##################
        // RT初始化
        [DllImport("ART1553B_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 RT_Init(IntPtr hDevice, Int32 nChanNo);

        // RT设置响应时间

        [DllImport("ART1553B_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 RT_SetRespTime(IntPtr hDevice, Int32 nChanNo, UInt16 wTimeout);

        // RT数据发送模式:单缓冲或循环缓冲
        [DllImport("ART1553B_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 RT_TxMode(IntPtr hDevice, Int32 nChanNo, Int32 nRTAddr, ref  RT_TX_MODE_STRUCT pTxMode);

        // RT设置地址并使能 lRTEnable每个bit位代表一个远程终端 bit0=1表示地址为0的远程终端使能 bit0=0表示地址为0的远程终端不使能
        [DllImport("ART1553B_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 RT_Select(IntPtr hDevice, Int32 nChanNo, Int32 lRTEnable);

        // RT开始
        [DllImport("ART1553B_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 RT_Start(IntPtr hDevice, Int32 nChanNo, Boolean bEnable);

        // 清置时标
        [DllImport("ART1553B_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 RT_ClearTTagOnSync(IntPtr hDevice, Int32 nChanNo, Boolean bEnable);

        // 加载时标
        [DllImport("ART1553B_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 RT_LoadTTagOnSync(IntPtr hDevice, Int32 nChanNo, Boolean bEnable);

        // 设置RT的状态字 nRTAddr:远程终端地址0~31
        [DllImport("ART1553B_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 RT_Status_Set(IntPtr hDevice, Int32 nChanNo, Int32 nRTAddr, ref RT_STATUS_WORD_STRUCT pStatusWord);

        // RT非法指令接收数据使能
        [DllImport("ART1553B_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 RT_RevIllegalData(IntPtr hDevice, Int32 nChanNo, Boolean bEnable);

        // RT非法指令表使能设置
        [DllImport("ART1553B_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 RT_IllegalCmd(IntPtr hDevice, Int32 nChanNo, Boolean bEnable);

        // RT非法指令表设置 nRTAddr:远程终端地址0~31
        [DllImport("ART1553B_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 RT_SetIllegalCmdTable(IntPtr hDevice, Int32 nChanNo, Int32 nRTAddr, ref RT_Illegal_CMD_TABLE_STRUCT pCmdTable);

        // RT设置矢量字 nRTAddr:远程终端地址0~31
        [DllImport("ART1553B_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 RT_SetVectorWord(IntPtr hDevice, Int32 nChanNo, Int32 nRTAddr, UInt16 lVectorWord);

        // RT设置自检字(Build In Test) nRTAddr:远程终端地址0~31
        [DllImport("ART1553B_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 RT_SetBITWord(IntPtr hDevice, Int32 nChanNo, Int32 nRTAddr, UInt16 lBITWord);

        // 发送消息函数 nRTAddr:远程终端地址0~31 SA:1~30  MsgLen:发送数据字的数量 最大为32
        [DllImport("ART1553B_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 RT_SendMsg(IntPtr hDevice, Int32 nChanNo, Int32 nRTAddr, Int32 nSubAddr, UInt32 lMsgLen, UInt16[] pMsg);

        // 读取发送消息函数 nRTAddr:远程终端地址0~31
        [DllImport("ART1553B_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 RT_ReadLastMsg(IntPtr hDevice, Int32 nChanNo, Int32 nRTAddr, ref RMSG_STRUCT pMsg);

        // RT获取最新消息个数 nRTAddr:远程终端地址0~31
        [DllImport("ART1553B_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 RT_GetMsgNum_Newly(IntPtr hDevice, Int32 nChanNo, Int32 nRTAddr, ref Int32 msgNum);

        // 读取RT消息 nRTAddr:远程终端地址0~31
        [DllImport("ART1553B_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 RT_ReadMsg(IntPtr hDevice, Int32 nChanNo, Int32 nRTAddr, ref RMSG_STRUCT pMsg, ref Int32 readedMsgCount, Int32 msgCountToRead);

        // 获取RT接收到的最新数据(此功能在中断方式下有效) nRTAddr:远程终端地址0~31
        [DllImport("ART1553B_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern void RT_Get_Newly_RData(IntPtr hDevice, Int32 nChanNo, Byte bRTAddr, Byte bSubAddr, Byte bWordCnt, ref UInt16 pDataBuf);  
        
        // ###############MONITOR TERMINAL MODE FUNCTION####################
        // BM初始化
         [DllImport("ART1553B_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 BM_Init(IntPtr hDevice, Int32 nChanNo);

        // 设置待监控的消息
         [DllImport("ART1553B_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 BM_SetCmdFilterTable(IntPtr hDevice, int nChanNo, ref BM_CMD_FILTER_TABLE_STRUCT pFTable);

        // BM获取消息总个数
         [DllImport("ART1553B_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 BM_GetMsgCount(IntPtr hDevice, Int32 nChanNo, ref Int32 msgCount);

        // BM获取新消息个数(用户还未来得及接收的新消息)
         [DllImport("ART1553B_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 BM_GetMsgCount_Newly(IntPtr hDevice, Int32 nChanNo, ref  Int32 msgCount);

        // BM顺序读取消息
         [DllImport("ART1553B_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 BM_ReadNextMsg(IntPtr hDevice, Int32 nChanNo, ref RMSG_STRUCT pMsg, ref  Int32 readedMsgCount, Int32 msgCountToRead );

        // BM读取消息 从当前位置向前读取需要的消息个数
        // 消息可以被重复读出 从当前位置向前读取设置的个数 只有当不够需要的个数时才会返回实际有的个数
         [DllImport("ART1553B_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 BM_ReadMsg(IntPtr hDevice, Int32 nChanNo, ref RMSG_STRUCT pMsg, ref Int32 readedMsgCount, Int32 msgCountToRead );

        // BM读取消息 从当前位置向前读取最新的消息个数
        // 当新消息个数小于设置的读取个数时 返回实际的新消息个数
         [DllImport("ART1553B_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 BM_ReadMsg_Newly(IntPtr hDevice, Int32 nChanNo, [In, Out] RMSG_STRUCT[] pMsg, ref Int32 readedMsgCount, Int32 msgCountToRead );

        // BM读取最后一条消息
         [DllImport("ART1553B_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 BM_ReadLastMsg(IntPtr hDevice, Int32 nChanNo, ref RMSG_STRUCT pMsg);

        // BM使能 开始工作
         [DllImport("ART1553B_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 BM_Start(IntPtr hDevice, Int32 nChanNo, Boolean bEnable);

        // #################辅助函数############################################
        // 构造命令字
        // msgType-消息类型
        // rt-RT地址 0~31
        // rxtx-收或者发 0-收 1-发
        // sa-子地址 0~31
        // dataCount-数据字个数 当消息类型是ModeCode时此参数表示模式代码
         [DllImport("ART1553B_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern UInt16 ART1553B_SetCommandWord(Int32 rt, Int32 rxtx, Int32 sa, Int32 dataCount);

        // Helper constant placeholder
        public const int BC_MSGTYPE_RESERVED = 0;
    }
}

