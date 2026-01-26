using System;
using MeasureControl.Drivers;
using MeasureControl.Helpers;

namespace MeasureControl.Drivers
{
    /// <summary>
    /// 1394B驱动接口包装类
    /// 提供与怀智例程DriverInterface兼容的接口
    /// </summary>
    public class HZ1394DriverInterface
    {
        public uint Node { get; set; }
        public IntPtr Tmpnote { get; set; }
        public bool HasChangeTopo { get; set; }
        public bool SaveDataToText { get; set; }
        public bool RcvFlag { get; set; }

        // 界面按钮值
        public string ComboBoxNodeTypeDriver { get; set; }
        public string ComboBoxNodeRateDriver { get; set; }
        public uint SendStyleDriver { get; set; }
        public double PeriodDriver { get; set; }
        public double TimesDriver { get; set; }
        public uint ChannelDriver { get; set; }
        public uint PackNumDriver { get; set; }
        public uint[] MessageIDDriver { get; set; }
        public uint[] MessageLenDriver { get; set; }

        private string _tmpnodeType = "";
        private uint _nodeNumber;
        private uint _cardNumber;

        public uint CardNumber
        {
            get => _cardNumber;
            set => _cardNumber = value;
        }

        public uint NodeNumber
        {
            get => _nodeNumber;
            set => _nodeNumber = value;
        }

        public string TmpnodeType
        {
            get => _tmpnodeType;
            set => _tmpnodeType = value;
        }

        public bool BM_CC_MSG_Cnt_Get { get; set; }

        public byte AsyncPktNum { get; set; }

        public HZ1394DriverInterface(uint node)
        {
            Node = node;
            Tmpnote = IntPtr.Zero;
            MessageIDDriver = new uint[120];
            MessageLenDriver = new uint[120];
        }

        /// <summary>
        /// 打开1394节点
        /// </summary>
        public IntPtr HZ1394_OPEN(string nodeType, IntPtr tmpnode, uint cardNumber, uint nodeNumber)
        {
            _cardNumber = cardNumber;
            _nodeNumber = nodeNumber;
            
            switch (nodeType)
            {
                case "CC":
                    tmpnode = HZ1394Interface.Mil1394_CC_OPEN(cardNumber, nodeNumber);
                    break;
                case "RN":
                    tmpnode = HZ1394Interface.Mil1394_RN_OPEN(cardNumber, nodeNumber);
                    break;
                case "BM":
                    tmpnode = HZ1394Interface.Mil1394_CC_OPEN(cardNumber, nodeNumber);
                    break;
            }
            Tmpnote = tmpnode;
            return tmpnode;
        }

        /// <summary>
        /// 关闭1394节点
        /// </summary>
        public void HZ1394_CC_Close(IntPtr tmpnode)
        {
            if (tmpnode == IntPtr.Zero)
            {
                System.Diagnostics.Debug.WriteLine("[HZ1394_CC_Close] 句柄为空，跳过关闭");
                return;
            }
            HZ1394Interface.Mil1394_CC_Close(tmpnode);
        }

        /// <summary>
        /// 复位节点
        /// </summary>
        public void HZ1394_CC_RESET(IntPtr tmpnode)
        {
            if (tmpnode == IntPtr.Zero)
            {
                System.Diagnostics.Debug.WriteLine("[HZ1394_CC_RESET] 句柄为空，跳过复位");
                return;
            }
            HZ1394Interface.Mil1394_CC_RESET(tmpnode);
        }

        /// <summary>
        /// 启动STOF发送
        /// </summary>
        public int HZ1394_CC_MSG_STOF_Start(IntPtr tmpnode)
        {
            return HZ1394Interface.Mil1394_CC_MSG_STOF_Start(tmpnode);
        }

        /// <summary>
        /// 启动异步流包发送
        /// </summary>
        public int HZ1394_CC_MSG_ASYNC_SEND_Start(IntPtr tmpnode)
        {
            return HZ1394Interface.Mil1394_CC_MSG_ASYNC_SEND_Start(tmpnode);
        }
        
        /// <summary>
        /// 停止STOF发送
        /// </summary>
        public int HZ1394_CC_MSG_STOF_Stop(IntPtr tmpnode)
        {
            if (tmpnode == IntPtr.Zero) return 0;
            return HZ1394Interface.Mil1394_CC_MSG_STOF_Stop(tmpnode);
        }
        
        /// <summary>
        /// 停止异步流包发送
        /// </summary>
        public int HZ1394_CC_MSG_ASYNC_SEND_Stop(IntPtr tmpnode)
        {
            if (tmpnode == IntPtr.Zero) return 0;
            return HZ1394Interface.Mil1394_CC_MSG_ASYNC_SEND_Stop(tmpnode);
        }
        
        /// <summary>
        /// 启动异步流包接收
        /// </summary>
        public int HZ1394_CC_MSG_ASYNC_RECV_Start(IntPtr tmpnode)
        {
            return HZ1394Interface.Mil1394_CC_MSG_ASYNC_RECV_Start(tmpnode);
        }
        
        /// <summary>
        /// 停止异步流包接收
        /// </summary>
        public int HZ1394_CC_MSG_ASYNC_RECV_Stop(IntPtr tmpnode)
        {
            if (tmpnode == IntPtr.Zero) return 0;
            return HZ1394Interface.Mil1394_CC_MSG_ASYNC_RECV_Stop(tmpnode);
        }

        /// <summary>
        /// 获取消息计数
        /// </summary>
        public int HZ1394_CC_MSG_Cnt_Get(IntPtr tmpnode, uint type, out uint pdata)
        {
            return HZ1394Interface.Mil1394_CC_MSG_Cnt_Get(tmpnode, type, out pdata);
        }

        /// <summary>
        /// BM使能
        /// </summary>
        public int HZ1394_CC_BM_ENABLE(IntPtr tmpnode, uint enable)
        {
            return HZ1394Interface.Mil1394_CC_BM_ENABLE(tmpnode, enable);
        }

        /// <summary>
        /// 设置速度
        /// </summary>
        public int HZ1394_SetSpeed(string speed, IntPtr tmpnode)
        {
            int res = 0;
            switch (speed)
            {
                case "100M":
                    res |= HZ1394Interface.Mil1394_CC_MSG_Speed_Set(tmpnode, 0);
                    break;
                case "200M":
                    res |= HZ1394Interface.Mil1394_CC_MSG_Speed_Set(tmpnode, 1);
                    break;
                case "400M":
                    res |= HZ1394Interface.Mil1394_CC_MSG_Speed_Set(tmpnode, 2);
                    break;
            }
            return res;
        }

        /// <summary>
        /// CRB LRTC使能
        /// </summary>
        public int HZ1394_CRB_LRTC_ENABLE(IntPtr tmpnode, uint enable)
        {
            return HZ1394Interface.Mil1394_CRB_LRTC_ENABLE(tmpnode, enable);
        }

        /// <summary>
        /// STOF接收使能
        /// </summary>
        public int HZ1394_CC_MSG_RCV_STOF_ENABLE(IntPtr tmpnode, uint enable)
        {
            return HZ1394Interface.Mil1394_CC_MSG_RCV_STOF_ENABLE(tmpnode, enable);
        }

        /// <summary>
        /// 设置STOF周期和发送方式
        /// </summary>
        /// <param name="tmpnode">节点句柄</param>
        /// <param name="stofStyle">发送方式：0=按周期，1=按次数</param>
        /// <param name="period">周期值(ms)，用于按周期模式</param>
        /// <param name="times">次数值，用于按次数模式</param>
        public int HZ1394_SetPeriod_Style_EN(IntPtr tmpnode, uint stofStyle, double period, double times)
        {
            int res = 0;
            // 配置发送方式和次数
            // stofStyle == 0: 按周期，count参数传入period值（与原始代码保持一致，原始代码中times就是周期值）
            // stofStyle == 1: 按次数，count参数是发送次数
            uint count = stofStyle == 0 ? (uint)period : (uint)times;
            res |= HZ1394Interface.Mil1394_CC_SYSCFG_STOF_SEND_STYLE_Set(tmpnode, stofStyle, count);
            
            // 设置周期（单位：微秒）
            // - 按周期模式(stofStyle==0)：使用period值（单位ms），转换为微秒需要*1000
            // - 按次数模式(stofStyle==1)：周期固定为15ms（15000微秒）
            uint periodInMicroseconds = stofStyle == 0 ? (uint)(period * 1000) : 15000;
            res |= HZ1394Interface.Mil1394_CC_SYSCFG_STOF_Period_Set(tmpnode, periodInMicroseconds);
            res |= HZ1394Interface.Mil1394_CC_MSG_RCV_STOF_ENABLE(tmpnode, 0);
            return res;
        }

        /// <summary>
        /// 设置STOF数据
        /// </summary>
        public int HZ1394_CC_MSG_STOF_Data_Set(IntPtr tmpnode, uint SysCntType, ref Helpers.TNF_Stof_Struct pstof)
        {
            return HZ1394Interface.Mil1394_CC_MSG_STOF_Data_Set(tmpnode, SysCntType, ref pstof);
        }

        /// <summary>
        /// 异步流包接收配置
        /// </summary>
        public int ASYNC_RECV_CFG(IntPtr tmpnode, uint channel, uint packNum, uint[] MessageID, uint[] MessageLen)
        {
            uint[] BufferPoint = new uint[120];
            int res = 0;
            res |= HZ1394Interface.Mil1394_CC_MSG_ASYNC_RECV_CHANNEL(tmpnode, channel);
            res |= HZ1394Interface.Mil1394_CC_MSG_ASYNC_RECV_CFG_SET(tmpnode, MessageID, MessageLen, BufferPoint, packNum);
            return res;
        }

        /// <summary>
        /// 设置异步流包数据
        /// </summary>
        public int HZ1394_CC_MSG_ASYNC_Data_Set(IntPtr tmpnode, uint SndMode, uint ID, Helpers.TNF_ASYNC_Struct[] pASYNC, uint len)
        {
            return HZ1394Interface.Mil1394_CC_MSG_ASYNC_Data_Set(tmpnode, SndMode, ID, pASYNC, len);
        }

        /// <summary>
        /// RN消息发送停止
        /// </summary>
        public int HZ1394_RN_MSG_SEND_Stop(IntPtr tmpnode)
        {
            return HZ1394Interface.Mil1394_RN_MSG_ASYNC_SEND_Stop(tmpnode);
        }

        /// <summary>
        /// 异步发送同步选择设置
        /// </summary>
        public int ASYNC_SEND_SYNSel_Set(IntPtr tmpnode)
        {
            return HZ1394Interface.Mil1394_CC_RN_SYNSel_Set(tmpnode, 0, 10000);
        }

        /// <summary>
        /// 启动接收线程
        /// </summary>
        public IntPtr HZStartRecvThd(IntPtr tmpnode)
        {
            return HZ1394Interface.StartRecvThd(tmpnode);
        }

        /// <summary>
        /// 停止接收线程
        /// </summary>
        public void HZStopRecvThd(IntPtr tmpnode)
        {
            if (tmpnode == IntPtr.Zero)
            {
                System.Diagnostics.Debug.WriteLine("[HZStopRecvThd] 句柄为空，跳过停止接收线程");
                return;
            }
            HZ1394Interface.StopRecvThd(tmpnode);
        }

        /// <summary>
        /// 启动模拟错误
        /// </summary>
        public int HZ1394_CC_SIM_ERR_Start(IntPtr tmpnode, uint ctrl_cmd)
        {
            return HZ1394Interface.Mil1394_CC_SIM_ERR_Start(tmpnode, ctrl_cmd);
        }
    }
}

