using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace MeasureControl.Drivers.ART4229
{
    public partial class ART4229_32
    {
        // ############## 设备参数 ######################
        public const Int32 ART4229_MAX_CHANNELS = 40;       // 本设备最多支持40路发送和接收通道(发送和接收通道的最大总和)

        // ############# 发送通道参数设置 #############
        public struct ART4229_TX_CH_PARAM
        {
            public Int32 nChannel;			    // 发送通道号
            public UInt32 nDataLength;		    // 数据长度,参考下面定义;
            public UInt32 nReserved0;		    // 保留0
            public UInt32 nReserved1;		    // 保留1
            public Double fTranRate;			// 发送速率

            public UInt32 nReserved2;			// 保留2
            public UInt32 nReserved3;			// 保留3
            public UInt32 nReserved4;			// 保留4
            public UInt32 nReserved5;			// 保留5
            public UInt32 nReserved6;			// 保留6
            public UInt32 nReserved7;			// 保留7	
            public UInt32 nSendMode;			// 发送模式，参看下面定义
            public UInt32 nReserved8;			// 保留8
            public UInt32 nReserved9;			// 保留9
            public UInt32 nReserved10;			// 保留10

        };

        // 硬件参数结构体ART4229_TX_CH_PARAM中的nSendMode参数所使用的选项
        public const Int32 ART4229_TX_MODE_SINGLE = 0;		// 单次发送
        public const Int32 ART4229_TX_MODE_PERIOD = 1;		// 周期发送

        // ############# 接收通道参数设置 #############
        public struct ART4229_RX_CH_PARAM
        {
            public Int32 nChannel;			// 接收通道号
            public UInt32 nDataLength;		// 数据长度,参考下面定义
            public UInt32 nReserved0;		// 保留0
            public UInt32 bInterrupt;		// 通道缓存中断是否打开 参看下面定义
            public UInt32 nReserved1;		// 保留1
            public UInt32 nParity;			// 数据校验,参看下面定义
            public UInt32 nReserved2;		// 保留2
            public UInt32 bCVTInterrupt;	// CVT中断 1:打开, 0:关闭 (暂不支持)
            public UInt32 nReserved3;	    // 保留3
            public UInt32 bRateAdaption;	// 接收码率是否自适应,参看下面定义
            public Double fRecvRate;		// 接收码率
            public UInt32 nInterruptDepth;	// 中断深度
            public UInt32 nReserved4;		// 保留4
        }

        // 硬件参数结构体ART4229_TX_CH_PARAM和ART4229_RX_CH_PARAM中的nDataLength参数所使用的中断是否打开选项
        public const Int32 ART4229_DATALEN_32BITS = 0;		// 数据长度32Bits
        public const Int32 ART4229_DATALEN_25BITS = 1;		// 数据长度25Bits

        // 硬件参数结构体ART4229_TX_CH_PARAM的nTranSequence和ART4229_RX_CH_PARAM中的nRcvSequence参数所使用的数据传输顺序选项
        public const Int32 ART4229_SEQUENCE_HIGH = 0;		// 先接收高位
        public const Int32 ART4229_SEQUENCE_LOW = 1;		// 先接收低位

        // 硬件参数结构体ART4229_TX_CH_PARAM和ART4229_RX_CH_PARAM中的nParity参数所使用的检验选项
        public const Int32 ART4229_PARITY_NONE = 0;		// 无校验
        public const Int32 ART4229_PARITY_ODD = 1;		// 奇检验
        public const Int32 ART4229_PARITY_EVEN = 2;     // 偶校验

        // 硬件参数结构体ART4229_RX_CH_PARAM中的bInterrupt参数所使用的中断是否打开选项
        public const Int32 ART4229_RX_INTERRUPT_CLOSE = 0;		// 中断关闭
        public const Int32 ART4229_RX_INTERRUPT_OPEN = 1;		// 中断打开

        // 硬件参数结构体ART4229_RX_CH_PARAM中的bRateAdaption参数所使用的接收码率自适应选项
        public const Int32 ART4229_RX_RATE_FIXED = 0;		// 固定接收码率
        public const Int32 ART4229_RX_RATE_SELFADAPTION = 1;		// 接收码率自适应

        // 过滤SDI值
        public const Int32 ART4229_FILTER_SDI_00 = 0;		// 接收SDI为00的数据
        public const Int32 ART4229_FILTER_SDI_01 = 1;		// 接收SDI为01的数据
        public const Int32 ART4229_FILTER_SDI_10 = 2;		// 接收SDI为10的数据
        public const Int32 ART4229_FILTER_SDI_11 = 3;		// 接收SDI为11的数据

        // 字格式(具体参看说明书)
        public const Int32 ART4229_WORD_FORMAT1 = 0;		// 格式1
        public const Int32 ART4229_WORD_FORMAT2 = 1;		// 格式2(429标准格式)

        // 获取设备信息
        public struct ART4229_MAIN_INFO
        {
            public UInt32 nDeviceType;							    // 总线类型\设备类型0x20215620
            public UInt32 nRevision;                                // Revision ID
            public UInt32 nReserved0;                               // 保留0
            public UInt32 nChannelCount;							// 通道数
            public Double fMainClock;								// 主时钟
            public Double fMaxRate;                                 // 最大传输速率
            public Double fMinRate;                                 // 最小传输速率
            public UInt32 nReserved1;                               // 保留1
            public UInt32 nReserved2;                               // 保留2
        };

        // 获取设备列表使用结构体
        [StructLayoutAttribute(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
        public struct ART4229_DEV_INFO
        {
            public UInt32 nSerialCode;                                        // 设备的序列号
            public UInt32 nDeviceType;                                        // 识别设备类别 返回类似为0x1E428811或0x30638814          
            public UInt32 bUsed;                                              // 设备是否已被使用
            public UInt32 nRevision;		                                   // Revision ID		;
        }

        // 接收数据文件头
        public struct RX_FILEHEAD
        {
            public UInt32 nFileHead;        // 文件头0xAA55AA55
            public UInt32 nReserved0;       // 保留
            public Int64 nDataCount;        // 数据字个数
            public UInt32 nChannelNo;       // 通道号
            public UInt32 nWordFormat;      // 数据字格式 格式一/格式二
            public UInt32 nPktStructure;    // 包结构 四种:时标+码率+字/时标+字/码率+字/字
            public UInt32 nDataLength;      // 数据长度 32bits/25bits
            public Double fMainClock;       // 主时钟
            public UInt32 nReserved2;       // 保留1
            public UInt32 nFileEnd;         // 文件尾0x55AA55AA
        };

        // 函数FILE_Create()的参数nOptMode所用的文件操作方式(支持"或"指令实现多种方式并行操作)
        public const Int32 ART4229_FILE_OPTMODE_CREATE_NEW = 1;	// 创建文件,如果文件存在则会出错
        public const Int32 ART4229_FILE_OPTMODE_CREATE_ALWAYS = 2;	// 不管文件是否存在，总是要被创建(即可能改写前一个文件)
        public const Int32 ART4229_FILE_OPTMODE_OPEN_EXISTING = 3;	// 打开必须已经存在的文件
        public const Int32 ART4229_FILE_OPTMODE_OPEN_ALWAYS = 4;	// 打开文件，若该文件不在，则创建它

        // 函数FILE_SetOffset()的参数nBaseMode所用的文件指针移动参考基点
        public const Int32 ART4229_FILE_BASEMODE_BEGIN = 0;	// 以文件起点作为参考点往右偏移
        public const Int32 ART4229_FILE_BASEMODE_CURRENT = 1;	// 以文件的当前位置作为参考点往左或往右偏移(nOffsetBytes<0时往左偏移，>0时往右偏移)
        public const Int32 ART4229_FILE_BASEMODE_END = 2;	// 以文件的尾部作为参考点往左偏移

        public const Int32 INVALID_HANDLE_VALUE = -1;

        // 时标结构
        public struct TIMETAG
        {
            public UInt16 nYear;				// 年
            public UInt16 nMonth;				// 月
            public UInt16 nDay;				    // 日
            public UInt16 nHour;				// 时
            public UInt16 nMinute;			    // 分
            public UInt16 nSecond;			    // 秒
            public UInt16 nMillSec;			    // 毫秒
        };

        // ################################ 返回代码 ################################	
        public const Int32 ART4229_ERROR_HANDLE_INVALID = -1;           // 句柄非法
        public const Int32 ART4229_ERROR_POINTER_INVALID = -2;		    // 指针非法
        public const Int32 ART4229_ERROR_CHAN_INVALID = -3;           // 通道非法

        public const Int32 ART4229_ERROR_KEY_INVALID = -4;           // 密码错误

        public const Int32 ART4229_SUCCESS = 1;            // 成功
        public const Int32 ART4229_FAIL = 0;            // 失败

        //// 发送返回码
        public const Int32 ART4229_TX_ERROR_BASE = 0xF000;

        public const Int32 ART4229_ERROR_TX_FAIL = (ART4229_TX_ERROR_BASE + 1);	// 发送失败
        public const Int32 ART4229_ERROR_TX_WRITE_DATALEN_ERROR = (ART4229_TX_ERROR_BASE + 2);	// 发送数据长度错误
        public const Int32 ART4229_ERROR_OPEN_TXCHAN_FAIL = (ART4229_TX_ERROR_BASE + 3);  // 打开发送通道失败
        public const Int32 ART4229_ERROR_INIT_TXCHAN_FAIL = (ART4229_TX_ERROR_BASE + 4);	// 初始发送通道失败
        public const Int32 ART4229_ERROR_START_TXCHAN_FAIL = (ART4229_TX_ERROR_BASE + 5);	// 开始发送失败
        public const Int32 ART4229_ERROR_GETSPACE_TXCHAN_FAIL = (ART4229_TX_ERROR_BASE + 6);	// 得到发送空间失败
        public const Int32 ART4229_ERROR_TX_BUFRRER_NOSPACE = (ART4229_TX_ERROR_BASE + 7);  // 发送缓冲区没有空间
        public const Int32 ART4229_ERROR_RESETFIFO_TXCHAN_FAIL = (ART4229_TX_ERROR_BASE + 8);	// 复位发送FIFO失败
        public const Int32 ART4229_ERROR_STOP_TXCHAN_FAIL = (ART4229_TX_ERROR_BASE + 9);	// 停止发送通道失败
        public const Int32 ART4229_ERROR_CLOSE_TXCHAN_FAIL = (ART4229_TX_ERROR_BASE + 10);	// 关闭发送通道失败
        public const Int32 ART4229_ERROE_NONE_DATA_TXCHAN = (ART4229_TX_ERROR_BASE + 11); // 没有要发送的数据
        public const Int32 ART4229_ERROR_TX_BUF_LENG_OVERRANGE = (ART4229_TX_ERROR_BASE + 12);	// 发送数据长度超过缓存的大小
        public const Int32 ART4229_ERROR_TX_GETTXCOMPLETE_FAIL = (ART4229_TX_ERROR_BASE + 13);	// 得到发送完成标志失败
        public const Int32 ART4229_ERROR_TX_WRITEWORDCNT_FAIL = (ART4229_TX_ERROR_BASE + 14);	// 写数据个数失败
        public const Int32 ART4229_ERROR_TX_TXSENDPKS_OVERRANGE = (ART4229_TX_ERROR_BASE + 15);	// 写数据包超范围

        //// 接收返回码
        public const Int32 ART4229_RX_ERROR_BASE = 0xF200;

        public const Int32 ART4229_ERROR_RX_FAIL = (ART4229_RX_ERROR_BASE + 1);    // 接收失败	
        public const Int32 ART4229_ERROR_RX_RCV_RATE_FAIL = (ART4229_RX_ERROR_BASE + 2);    // 读取接收速率失败
        public const Int32 ART4229_ERROR_RX_RCV_HT_FAIL = (ART4229_RX_ERROR_BASE + 3);    // 读取高时间位失败
        public const Int32 ART4229_ERROR_RX_RCV_LT_FAIL = (ART4229_RX_ERROR_BASE + 4);    // 读取低时间位失败
        public const Int32 ART4229_ERROR_RX_RCV_DATA_FAIL = (ART4229_RX_ERROR_BASE + 5);    // 读取数据失败
        public const Int32 ART4229_ERROR_RX_RCV_PARITY_ERROR = (ART4229_RX_ERROR_BASE + 6);    // 校验错误
        public const Int32 ART4229_ERROR_RX_RCV_GETCOUNT_FAIL = (ART4229_RX_ERROR_BASE + 7);    // 得到接收数据个数失败
        public const Int32 ART4229_ERROR_RX_RCV_DATECOUNT_LE0 = (ART4229_RX_ERROR_BASE + 8);    // 接收数据格式小于等于0
        public const Int32 ART4229_ERROR_RX_CLR_RCVINT_FAIL = (ART4229_RX_ERROR_BASE + 9);    // 清除接收中断失败
        public const Int32 ART4229_ERROR_RX_CLR_CVTINT_FAIL = (ART4229_RX_ERROR_BASE + 10);   // 清除CVT中断失败
        public const Int32 ART4229_ERROR_RX_RCV_NONEED_SDI = (ART4229_RX_ERROR_BASE + 11);   // 不需要的SDI
        public const Int32 ART4229_ERROR_RX_RCV_NONEED_LABEL = (ART4229_RX_ERROR_BASE + 12);   // 不需要的Label
        public const Int32 ART4229_ERROR_RX_SET_CHANFTR_FAIL = (ART4229_RX_ERROR_BASE + 13);	  // 设置通道过滤失败

        public const Int32 ART4229_ERROR_OPEN_RXCHAN_FAIL = (ART4229_RX_ERROR_BASE + 14);	   // 打开接收通道失败
        public const Int32 ART4229_ERROR_INIT_RXCHAN_FAIL = (ART4229_RX_ERROR_BASE + 15);	   // 初始化接收通道失败
        public const Int32 ART4229_ERROR_ENFILTER_RXCHAN_FAIL = (ART4229_RX_ERROR_BASE + 16);    // 使能过滤失败
        public const Int32 ART4229_ERROR_SDIFILTER_RXCHAN_FAIL = (ART4229_RX_ERROR_BASE + 17);	   // SDI过滤失败
        public const Int32 ART4229_ERROR_LABELFILTER_RXCHAN_FAIL = (ART4229_RX_ERROR_BASE + 18);	   // Label过滤失败
        public const Int32 ART4229_ERROR_SETINTDEPTH_RXCHAN_FAIL = (ART4229_RX_ERROR_BASE + 19);	   // 设置中断深度失败
        public const Int32 ART4229_ERROR_RESETFIFO_RXCHAN_FAIL = (ART4229_RX_ERROR_BASE + 20);	   // 等待中断失败
        public const Int32 ART4229_ERROR_START_RXCHAN_FAIL = (ART4229_RX_ERROR_BASE + 21);	   // 启动接收通道失败
        public const Int32 ART4229_ERROR_WAITFORINT_RXCHAN_FAIL = (ART4229_RX_ERROR_BASE + 22);	   // 等待中断失败		
        public const Int32 ART4229_ERROR_GETDATACOUNT_RXCHAN_FAIL = (ART4229_RX_ERROR_BASE + 23);	   // 当前接收的缓冲区包个数失败
        public const Int32 ART4229_ERROR_GETRATE_RXCHAN_FAIL = (ART4229_RX_ERROR_BASE + 24);	   // 得到接收速率失败
        public const Int32 ART4229_ERROR_STOP_RXCHAN_FAIL = (ART4229_RX_ERROR_BASE + 25);	   // 停止接收通道失败
        public const Int32 ART4229_ERROR_CLOSE_RXCHAN_FAIL = (ART4229_RX_ERROR_BASE + 26);	   // 关闭接收通道失败

        public const Int32 ART4229_ERROR_SET_TIMETAG_INITVAL_FAIL = (ART4229_RX_ERROR_BASE + 28);	   // 设置时标初始值时标
        public const Int32 ART4229_ERROR_GET_TIMETAG_VAL_FAIL = (ART4229_RX_ERROR_BASE + 29);	   // 得到当前时标值失败
        public const Int32 ART4229_ERROR_START_TIMETAG_FAIL = (ART4229_RX_ERROR_BASE + 30);	   // 开启时标计数失败
        public const Int32 ART4229_ERROR_STOP_TIMETAG_FAIL = (ART4229_RX_ERROR_BASE + 31);	   // 停止时标计数失败
        public const Int32 ART4229_ERROR_RX_SET_READLEN_FAIL = (ART4229_RX_ERROR_BASE + 32);	   // 设置要读取的数据长度失败
        public const Int32 ART4229_ERROR_RX_EP_NULL = (ART4229_RX_ERROR_BASE + 33);	   // RxEP为NULL

        // ################################ DEV设备对象管理函数 ################################
        [DllImport("ART4229_64.DLL")]
        public static extern Int32 ART4229_DeviceList(		    // 枚举设备信息
            IntPtr devInfo,                                     // 设备信息 ART4229_DEV_INFO
            Byte nArraySize,                                    // 设备信息数组大小
            ref Byte pDeviceNum);                               // 返回的实际设备数

        [DllImport("ART4229_64.DLL")]
        public static extern IntPtr ART4229_DEV_Create(         // 创建设备并返回设备句柄
            UInt32 nIndex,                                      // 设备硬件序号
            UInt32 nIndexType);                                 // 序号类型 0:物理ID,1:序列号 USB设备只支持序列号创建

        [DllImport("ART4229_64.DLL")]
        public static extern Int32 ART4229_DEV_Release(			// 释放设备
            IntPtr hDevice);                                    // 设备对象句柄,它由DEV_Create()函数创建

        [DllImport("ART4229_64.DLL")]
        public static extern Int32 ART4229_DEV_Reset(		    // 复位设备
            IntPtr hDevice);					                // 设备对象句柄,它由DEV_Create()函数创建	

        [DllImport("ART4229_64.DLL")]
        public static extern Int32 ART4229_DEV_GetSerialNumber(	// 得到设备系列号
            IntPtr hDevice,					                    // 设备对象句柄,它由DEV_Create()函数创建
            ref UInt32 nSerialNumber);				            // 序列号

        [DllImport("ART4229_64.DLL")]
        public static extern Int32 ART4229_DEV_GetMainInfo(    // 获得设备主要参数
            IntPtr hDevice,                                    // 设备对象句柄,它由DEV_Create()函数创建
            ref ART4229_MAIN_INFO pMainInfo);                  // 设备主要参数

        [DllImport("ART4229_64.DLL")]
        public static extern Int32 ART4229_DEV_GetVersion(	    // 获得设备版本信息
            IntPtr hDevice,					                    // 设备对象句柄,它由DEV_Create()函数创建
            ref UInt32 pDllVer,					                // 返回的动态库(.dll)版本号
            ref UInt32 pDriverVer,					            // 返回的驱动(.sys)版本号
            ref UInt32 pFirmwareVer);				            // 返回的固件版本号

        [DllImport("ART4229_64.DLL")]
        public static extern Int32 ART4229_DEV_GetTemperature(		// 得到设备温度
            IntPtr hDevice,                                 // 设备对象句柄,它由DEV_Create()函数创建
            ref Double fTemperature);				                // 设备温度

        // ################################ DEV公共函数 ################################
        [DllImport("ART4229_64.DLL")]
        public static extern Int32 ART4229_SetTimeTag(		    // 设置时标计时器的初值
            IntPtr hDevice);					                // 设备对象句柄,它由DEV_Create()函数创建

        [DllImport("ART4229_64.DLL")]
        public static extern Int32 ART4229_SetCustomTimeTag(	// 设置自定义时标计时器的初值
            IntPtr hDevice,                                     // 设备对象句柄,它由DEV_Create()函数创建
            UInt64 nTimeTagVal);                                // 	时标计数器值 基准10nS

        [DllImport("ART4229_64.DLL")]
        public static extern Int32 ART4229_GetTimeTag(		// 得到时标计数器的值
            IntPtr hDevice,					                // 设备对象句柄,它由DEV_Create()函数创建				
            ref UInt64 pTimeTagVal);				        // 时标计数器值

        [DllImport("ART4229_64.DLL")]
        public static extern Int32 ART4229_TransformTimeTag(	// 转换时标值到当前时间值,从读取数据中解析
            IntPtr hDevice,	                                    // 设备对象句柄,它由DEV_Create()函数创建
            UInt32 nTimeTagHigh,				                // 时标高
            UInt32 nTimeTagLow,				                    // 时标低
            ref TIMETAG pTimeTag);					            // 时标,年月日时分秒毫秒

        [DllImport("ART4229_64.DLL")]
        public static extern Int32 ART4229_EnableTimeTag(		// 开启时标计数器
            IntPtr hDevice,                                     // 设备对象句柄,它由DEV_Create()函数创建
            UInt32 bEnable);                                    // 使能时标计数器, 1:使能；0:不使能              

        // ################################ 通道功能函数 ################################
        [DllImport("ART4229_64.DLL")]
        public static extern Int32 ART4229_Channel_SetWordFormat(		// 设置字格式
        IntPtr hDevice,					            // 设备对象句柄,它由DEV_Create()函数创建
        Int32 nChannel,					            // 通道号
        UInt32 nWordFormat);				            // 1:格式2(总线数据模式)，0:格式1

        [DllImport("ART4229_64.DLL")]
        public static extern Int32 ART4229_Channel_SetMode(			    // 设置自检使能
        IntPtr hDevice,					                // 设备对象句柄,它由DEV_Create()函数创建
        Int32 nChannel,					                // 需要自检的通道号
        UInt32 nMode);						                // 0:正常模式，1:自检模式

        // ################################ 发送实现函数 ################################
        [DllImport("ART4229_64.DLL")]
        public static extern Int32 ART4229_TX_OpenChannel(	// 打开发送通道
            IntPtr hDevice,			                        // 设备对象句柄,它由DEV_Create()函数创建
            Int32 nChannel);					            // 通道号

        [DllImport("ART4229_64.DLL")]
        public static extern Int32 ART4229_TX_InitChannel(							// 初始化发送通道
            IntPtr hDevice,					// 设备对象句柄,它由DEV_Create()函数创建
            Int32 nChannel,					// 通道号
            ref ART4229_TX_CH_PARAM pTXChParam);				// 发送通道配置参数

        [DllImport("ART4229_64.DLL")]
        public static extern Int32 ART4229_TX_ResetChannel(						// 复位发送通道
        IntPtr hDevice,					// 设备对象句柄,它由DEV_Create()函数创建
        Int32 nChannel);					// 通道号

        [DllImport("ART4229_64.DLL")]
        public static extern Int32 ART4229_TX_Start(								// 开始发送
            IntPtr hDevice,					// 设备对象句柄,它由DEV_Create()函数创建
            Int32 nChannel);					// 通道号

        [DllImport("ART4229_64.DLL")]
        public static extern Int32 ART4229_TX_GetStatus(							// 得到发送状态(暂不用)
            IntPtr hDevice,					// 设备对象句柄,它由DEV_Create()函数创建
            Int32 nChannel,					// 通道号
            ref Int32 pTXStatus);					// 发送状态	0:空闲，1:发送中，2:发送完成

        [DllImport("ART4229_64.DLL")]
        public static extern Int32 ART4229_TX_GetBufSurplusSpace(					// 得到发送缓冲区剩余空间大小
            IntPtr hDevice,					// 设备对象句柄,它由DEV_Create()函数创建
            Int32 nChannel,					// 通道号
            ref Int32 pSurplusSize);				// 剩余空间

        [DllImport("ART4229_64.DLL")]
        public static extern Int32 ART4229_TX_IsComplete(							// 判断发送是否完成
            IntPtr hDevice,					// 设备对象句柄,它由DEV_Create()函数创建
            Int32 nChannel,					// 通道号
            ref Int32 pIsComplete);			// 发送完成标志,0:未完成，1:已完成

        [DllImport("ART4229_64.DLL")]
        public static extern Int32 ART4229_TX_ResetFIFO(							// 复位发送缓冲区				
            IntPtr hDevice,					// 设备对象句柄,它由DEV_Create()函数创建
            Int32 nChannel);					// 通道号

        [DllImport("ART4229_64.DLL")]
        public static extern Int32 ART4229_TX_WriteData(                            // 将429数据写入到发送通道
            IntPtr hDevice,                 // 设备对象句柄,它由DEV_Create()函数创建					
            Int32 nChannel,					// 通道号
            UInt32[] pTxBuf,					// 数据缓存
            UInt32[] pTxPeriod,					// 发送周期缓存   周期发送有效 单次可为NULL
            UInt32[] pTxCount,					// 发送次数缓存   周期发送有效 单次可为NULL
            UInt32[] pTxInterval,				// 发送数据字间隔缓存    周期发送有效 单次可为NULL
            UInt32[] pTxParity,					// 发送字校验缓存
            UInt32 nLengthToTx,				    // 各缓存需要发送的大小
            ref UInt32 pRealTx);					// 实际发送的缓存大小		


        [DllImport("ART4229_64.DLL")]
        public static extern Int32 ART4229_TX_Stop(								// 停止发送
            IntPtr hDevice,					// 设备对象句柄,它由DEV_Create()函数创建
            Int32 nChannel);					// 通道号

        [DllImport("ART4229_64.DLL")]
        public static extern Int32 ART4229_TX_CloseChannel(
            IntPtr hDevice,					// 设备对象句柄,它由DEV_Create()函数创建	
            Int32 nChannel);					// 通道号


        // ################################ 接收实现函数 ################################
        [DllImport("ART4229_64.DLL")]
        public static extern Int32 ART4229_RX_OpenChannel(							// 打开接收通道
            IntPtr hDevice,					// 设备对象句柄,它由DEV_Create()函数创建
            Int32 nChannel);					// 通道号

        [DllImport("ART4229_64.DLL")]
        public static extern Int32 ART4229_RX_InitChannel(							// 初始化接收通道
            IntPtr hDevice,					// 设备对象句柄,它由DEV_Create()函数创建
            Int32 nChannel,					// 通道号
            ref ART4229_RX_CH_PARAM pRXChParam);				// 接收通道配置参数

        [DllImport("ART4229_64.DLL")]
        public static extern Int32 ART4229_RX_EnableFilter( // 使能过滤
            IntPtr hDevice,					// 设备对象句柄,它由DEV_Create()函数创建
            Int32 nChannel,					// 通道号
            Int32 bEnable);					// 使能过滤 0:不过滤，1：过滤

        [DllImport("ART4229_64.DLL")]
        public static extern Int32 ART4229_RX_AddFilter(							// 设置S/D标号过滤
            IntPtr hDevice,					                // 设备对象句柄,它由DEV_Create()函数创建
            Int32 nChannel,					                // 	需要过滤的通道号				
            Byte nSD,						                // S/D的值 范围[0, 3]
            Byte nLabel,						            //  Label值,范围[0, 255]
            Byte bEnable);					                // 标号过使能,1:接收,0:不接收	

        [DllImport("ART4229_64.DLL")]
        public static extern Int32 ART4229_RX_SetFilters(       // 设置通道的SD/标号过滤
        IntPtr hDevice,                 // 设备对象句柄,它由DEV_Create()函数创建
        Int32 nChannel,					// 需要过滤的通道号
        Byte[] nLabelFilter);                    // 各SD下需要过滤的标号值，数组下标0--255为SD:00的标识号，256--511为SD：01的标识号，512--767为SD:10的标识号，768--1023为SD:11的标识号

        [DllImport("ART4229_64.DLL")]
        public static extern Int32 ART4229_RX_ResetFIFO(			// 复位接收缓冲区				
            IntPtr hDevice,					                        // 设备对象句柄,它由DEV_Create()函数创建
            Int32 nChannel);					                    // 通道号

        [DllImport("ART4229_64.DLL")]
        public static extern Int32 ART4229_RX_Start(								// 启动读								
            IntPtr hDevice,					// 设备对象句柄,它由DEV_Create()函数创建
            Int32 nChannel);					// 通道号

        [DllImport("ART4229_64.DLL")]
        public static extern Int32 ART4229_RX_WaitForInterrupt(					// 等待中断
            IntPtr hDevice,					                                    // 设备对象句柄,它由DEV_Create()函数创建
            Int32 nChannel,					                                    // 通道号
            Double fTimeOut);					                                // 超时时间

        [DllImport("ART4229_64.DLL")]
        public static extern Int32 ART4229_RX_GetCountOfRecvData(					// 当前接收的缓冲区包个数
            IntPtr hDevice,					// 设备对象句柄,它由DEV_Create()函数创建F:\429\ARINC429_221215\Sys_single\TXCfgFrm.cs
            Int32 nChannel,					// 通道号
            ref UInt32 pCount);					// 包个数

        [DllImport("ART4229_64.DLL")]
        public static extern Int32 ART4229_RX_ReadData(								// 读取429数据
            IntPtr hDevice,					// 设备对象句柄,它由DEV_Create()函数创建	
            Int32 nChannel,					// 通道号								
            UInt32[] pRXData,				// 429数据 四种:码率(U32)+时标高(U32)+时标低(U32)+字(U32)/时标高(U32)+时标低(U32)+字(U32)/码率(U32)+ 字(U32)/字(U32) 
            UInt32 nRXLen,					// 待接收的数据量 通常为得到缓冲区的包个数
            ref UInt32 pRealLen);			// 实际读取长度

        [DllImport("ART4229_64.DLL")]
        public static extern Int32 ART4229_RX_Stop(								// 停止读
            IntPtr hDevice,					// 设备对象句柄,它由DEV_Create()函数创建
            Int32 nChannel);					// 通道号

        [DllImport("ART4229_64.DLL")]
        public static extern Int32 ART4229_RX_CloseChannel( // 关闭通道
            IntPtr hDevice,					    // 设备对象句柄,它由DEV_Create()函数创建	
            Int32 nChannel);					// 通道号

        // ========================= 辅助操作函数 =========================
        [DllImport("ART4229_64.DLL")]
        public static extern Int32 ART4229_DEV_GetFLHSerialNum(						// 得到设备系列号
            IntPtr hDevice,					// 设备对象句柄,它由DEV_Create()函数创建
            ref UInt32 pSerialNum);				// 返回设备的序列号			

        [DllImport("ART4229_64.DLL")]
        public static extern Int32 ART4229_DEV_ConfigSerialNum(						// 配置设备序列号
            IntPtr hDevice,					// 设备对象句柄,它由DEV_Create()函数创建
            UInt32 nKey,				    // 输入对应密码，密码正确后才可以进行配置								
            UInt32 nSerialNum);				// 设备序列号

        [DllImport("ART4229_64.DLL")]
        public static extern Int32 ART4229_DEV_GetPhysIdx(  // 得到设备物理ID
            IntPtr hDevice,                                 // 设备对象句柄,它由DEV_Create()函数创建
            ref UInt32 pPhysIdx);                              // 物理ID

        [DllImport("ART4229_64.DLL")]
        public static extern Int32 ART4229_DEV_ConfigPhysIdx(   // 配置设备物理ID
            IntPtr hDevice,                                     // 设备对象句柄,它由DEV_Create()函数创建
            UInt32 nPhysIdx);                                  // 设备物理序号

        // ################################ 文件函数(以下文件函数只是特殊额外的服务,本公司不提供额外的支持) ################################
        [DllImport("ART4229_64.DLL")]
        public static extern IntPtr ART4229_FILE_Create(    // 根据指定文件名创建文件句柄(hFile),如果失败，则返回值为INVALID_HANDLE_VALUE(-1)
            String strFile,                                 // 路径及文件名
            Int32 nOptMode);                                // 文件操作模式，见上面相关常量定义

        [DllImport("ART4229_64.DLL")]
        public static extern UInt32 ART4229_FILE_Read(      // 从指定文件中读取数据,返回实际读取的字节数, 成功时返回值大于0,否则返回值等于0,
            IntPtr hFile,                                    // 文件句柄,由FILE_Create()函数创建
            IntPtr pDataBuffer,                             // 数据缓冲区，存放从文件读取的数据
            UInt32 nSizeBytes);                             // 请求读取数据的字节数

        [DllImport("ART4229_64.DLL")]
        public static extern UInt32 ART4229_FILE_Read(      // 从指定文件中读取数据,返回实际读取的字节数, 成功时返回值大于0,否则返回值等于0,
            IntPtr hFile,                                    // 文件句柄,由FILE_Create()函数创建
            UInt32[] pDataBuffer,                         // 数据缓冲区，存放从文件读取的数据
            UInt32 nSizeBytes);                             // 请求读取数据的字节数

        [DllImport("ART4229_64.DLL")]
        public static extern UInt32 ART4229_FILE_Write( // 向指定文件写入数据,返回实际写入的字节数, 成功时返回值大于0,否则返回值等于0,
            IntPtr hFile,                                // 文件句柄,由FILE_Create()函数创建
            IntPtr pDataBuffer,                          // 数据缓冲区，存放要写入文件的数据                      
            UInt32 nSizeBytes);                         // 请求写入数据的字节数

        [DllImport("ART4229_64.DLL")]
        public static extern UInt32 ART4229_FILE_Write( // 向指定文件写入数据,返回实际写入的字节数, 成功时返回值大于0,否则返回值等于0,
            IntPtr hFile,                                // 文件句柄,由FILE_Create()函数创建
            UInt32[] pDataBuffer,                       // 数据缓冲区，存放要写入文件的数据                      
            UInt32 nSizeBytes);                         // 请求写入数据的字节数

        [DllImport("ART4229_64.DLL")]
        public static extern UInt64 ART4229_FILE_GetLength(  // 获取指定文件的长度(字节数), 成功时返回值大于0,否则返回值等于0
            IntPtr hFile);                                     // 文件句柄,由FILE_Create()函数创建

        [DllImport("ART4229_64.DLL")]
        public static extern Int32 ART4229_FILE_SetOffset(    // 设置读写文件的偏移位置, 成功时返回true,否则返回false
            IntPtr hFile,                                       // 文件句柄,由FILE_Create()函数创建
            Int64 nOffsetBytes,                                 // 偏移位置(字节)
            Int32 nBaseMode);                                   // 参考基点模式，具体请参考上面的相关常量定义

        [DllImport("ART4229_64.DLL")]
        public static extern UInt64 ART4229_FILE_GetDiskFreeBytes(  // 获取指定磁盘的剩余空间（字节数）,成功时返回值大于0,否则返回值等于0
            String strDiskName);                                // 磁盘名称，如"C:\\", "D:\\"                   

        [DllImport("ART4229_64.DLL")]
        public static extern Int32 ART4229_FILE_Release(      // 释放文件句柄
               IntPtr hFile);                                   // 文件句柄,由FILE_Create()函数创建
    }
}
