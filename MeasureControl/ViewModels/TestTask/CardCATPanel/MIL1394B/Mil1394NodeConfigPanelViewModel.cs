using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using MeasureControl.Drivers;
using MeasureControl.Events;
using MeasureControl.Helpers;
using MeasureControl.Models;
using MeasureControl.Models.Devices;
using MeasureControl.Services;
using MeasureControl.Views.Dialogs;
using MeasureControl.Views.TestTask;
using MeasureControl.Views.TestTask.CardCATPanel.Mil1394B;
using Prism.Events;
using Prism.Mvvm;

namespace MeasureControl.ViewModels.TestTask.CardCATPanel.MIL1394B
{
    /// <summary>
    /// 节点配置面板ViewModel
    /// </summary>
    public class Mil1394NodeConfigPanelViewModel : BindableBase
    {
        private readonly uint _cardNumber;
        private readonly uint _nodeNumber;
        private readonly IntPtr[] _pnode;
        private readonly HZ1394DriverInterface _driverInterface;
        private readonly Mil1394BDevice _device;
        private readonly string _chassisName;
        private readonly IPxiChassisService _pxiChassisService;
        private readonly IEventAggregator _eventAggregator;

        public Mil1394NodeConfigPanelViewModel(uint cardNumber, uint nodeNumber, IntPtr[] pnode,
            HZ1394DriverInterface driverInterface, Mil1394BDevice device, string chassisName,
            IPxiChassisService pxiChassisService = null, IEventAggregator eventAggregator = null)
        {
            _cardNumber = cardNumber;
            _nodeNumber = nodeNumber;
            _pnode = pnode ?? throw new ArgumentNullException(nameof(pnode));
            _driverInterface = driverInterface ?? throw new ArgumentNullException(nameof(driverInterface));
            _device = device ?? throw new ArgumentNullException(nameof(device));
            _chassisName = chassisName ?? throw new ArgumentNullException(nameof(chassisName));
            _pxiChassisService = pxiChassisService;
            _eventAggregator = eventAggregator;
        }

        /// <summary>
        /// 应用配置
        /// </summary>
        /// <param name="nodeType">节点类型</param>
        /// <param name="nodeRate">节点速率</param>
        /// <param name="enableBM">是否启用BM</param>
        /// <param name="sendStyle">发送样式</param>
        /// <param name="period">周期</param>
        /// <param name="times">次数</param>
        /// <param name="channel">通道</param>
        /// <param name="stofPayload">STOF负载</param>
        /// <param name="stofVpc">STOF VPC</param>
        /// <param name="recvConfig">接收配置</param>
        /// <param name="sendConfig">发送配置</param>
        /// <param name="autoRestartIfSending">如果发送正在运行，是否自动重新启动（默认false，避免打开板卡时自动启动）</param>
        public void ApplyConfiguration(
            string nodeType,
            string nodeRate,
            bool enableBM,
            int sendStyle,
            string period,
            string times,
            string channel,
            uint[] stofPayload,
            string stofVpc,
            ObservableCollection<Mil1394NodeConfigPanel.AsyncReceiveConfigItem> recvConfig,
            ObservableCollection<Mil1394NodeConfigPanel.AsyncSendConfigItem> sendConfig,
            bool autoRestartIfSending = false)
        {
            try
            {
                // 首先保存配置到DriverInterface（无论节点是否连接都要设置，这样在节点连接后就能使用）
                _driverInterface.ComboBoxNodeTypeDriver = nodeType;
                _driverInterface.ComboBoxNodeRateDriver = nodeRate;
                _driverInterface.SendStyleDriver = (uint)sendStyle;
                _driverInterface.PeriodDriver = double.Parse(period);
                _driverInterface.TimesDriver = double.Parse(times);
                _driverInterface.ChannelDriver = uint.Parse(channel);
                _driverInterface.TmpnodeType = nodeType; // 重要：即使节点未连接也要设置节点类型
                _driverInterface.Tmpnote = _pnode[_nodeNumber]; // 如果节点未连接，这里是IntPtr.Zero

                // 如果节点未连接，只设置配置参数，不进行硬件初始化
                if (_pnode[_nodeNumber] == IntPtr.Zero)
                {
                    System.Diagnostics.Debug.WriteLine($"[Mil1394NodeConfig] 节点{_nodeNumber}未连接，仅保存配置参数，不进行硬件初始化");
                    return; // 提前返回，不执行硬件初始化
                }

                // 节点已连接，进行硬件初始化
                // 根据节点类型初始化
                switch (nodeType)
                {
                    case "CC":
                        InitCC(enableBM);
                        break;
                    case "RN":
                        InitRN(); // RN节点总是禁用BM，不受enableBM参数影响
                        break;
                    case "BM":
                        InitBM(enableBM);
                        break;
                }

                // 2. STOF配置
                if (nodeType == "CC" || nodeType == "BM")
                {
                    try
                    {
                        uint stofStyle = (uint)sendStyle;
                        double periodValue = double.Parse(period);
                        double timesValue = double.Parse(times);
                        // 调用StofCfg：按周期模式传入period，按次数模式传入times
                        // 使用autoRestartIfSending参数控制是否自动重启发送
                        StofCfg(stofStyle, periodValue, timesValue, stofPayload, stofVpc, autoRestartIfSending);
                    }
                    catch (Exception ex)
                    {
                        throw new Exception($"STOF配置失败: {ex.Message}", ex);
                    }
                }

                // 3. 异步流包接收配置
                if (nodeType == "RN")
                {
                    try
                    {
                        uint channelValue = uint.Parse(channel);
                        AsyncRecvCfg(channelValue, recvConfig);
                    }
                    catch (Exception ex)
                    {
                        throw new Exception($"异步流包接收配置失败: {ex.Message}", ex);
                    }
                }

                // 4. 异步流包发送配置
                uint loadASYNCPagNum = (uint)sendConfig.Count;
                if (loadASYNCPagNum > 0)
                {
                    AsyncSendCfg(loadASYNCPagNum, sendConfig);
                }

                // 5. 启动模拟错误（节点初始化完成后的必要步骤）
                int simErrRes = _driverInterface.HZ1394_CC_SIM_ERR_Start(_pnode[_nodeNumber], 1);
                if (simErrRes != 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[Mil1394NodeConfig] 启动模拟错误失败，错误码: {simErrRes}");
                    // 不抛出异常，因为这不是致命错误
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"应用配置失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// STOF配置
        /// </summary>
        /// <param name="stofStyle">发送方式：0=按周期，1=按次数</param>
        /// <param name="period">周期值(ms)，用于按周期模式</param>
        /// <param name="times">次数值，用于按次数模式</param>
        /// <param name="stofPayload">STOF负载数据</param>
        /// <param name="stofVpc">STOF VPC值</param>
        /// <param name="autoRestartIfSending">如果发送正在运行，是否自动重新启动（默认false，避免打开板卡时自动启动）</param>
        private void StofCfg(uint stofStyle, double period, double times, uint[] stofPayload, string stofVpc, bool autoRestartIfSending = false)
        {
            int res = 0;
            IntPtr nodeHandle = _pnode[_nodeNumber];
            
            // 如果autoRestartIfSending为true，且STOF发送正在运行，先停止发送，应用配置后再重新启动
            // 通过尝试停止STOF发送来检测是否正在运行（如果停止失败，说明可能没在运行）
            bool wasSending = false;
            if (autoRestartIfSending)
            {
                try
                {
                    int stopRes = _driverInterface.HZ1394_CC_MSG_STOF_Stop(nodeHandle);
                    if (stopRes == 0)
                    {
                        wasSending = true;
                        System.Diagnostics.Debug.WriteLine($"[Mil1394NodeConfig] STOF发送正在运行，已停止以应用新配置");
                        // 短暂延迟，确保停止操作完成
                        System.Threading.Thread.Sleep(50);
                    }
                }
                catch
                {
                    // 停止失败，可能发送没在运行，继续配置
                    wasSending = false;
                }
            }
            else
            {
                // 不自动重启模式：只确保发送已停止，不重新启动
                try
                {
                    _driverInterface.HZ1394_CC_MSG_STOF_Stop(nodeHandle);
                }
                catch
                {
                    // 忽略停止失败的错误
                }
            }

            // 配置周期和发送方式
            res = _driverInterface.HZ1394_SetPeriod_Style_EN(nodeHandle, stofStyle, period, times);

            // 配置STOF数据
            var stofData = new TNF_Stof_Struct
            {
                STOFPayload0 = stofPayload[0],
                STOFPayload1 = stofPayload[1],
                STOFPayload2 = stofPayload[2],
                STOFPayload3 = stofPayload[3],
                STOFPayload4 = stofPayload[4],
                STOFPayload5 = stofPayload[5],
                STOFPayload6 = stofPayload[6],
                STOFPayload7 = stofPayload[7],
                STOFPayload8 = stofPayload[8],
                STOFVPC = Convert.ToUInt32(stofVpc, 16)
            };

            res |= _driverInterface.HZ1394_CC_MSG_STOF_Data_Set(nodeHandle, 1, ref stofData);

            if (res != 0)
            {
                throw new Exception($"STOF配置失败，错误码: {res}");
            }
            
            // 如果autoRestartIfSending为true且之前发送正在运行，重新启动发送以应用新配置
            if (autoRestartIfSending && wasSending)
            {
                System.Diagnostics.Debug.WriteLine($"[Mil1394NodeConfig] 重新启动STOF发送以应用新配置（周期: {period}ms）");
                res = _driverInterface.HZ1394_CC_MSG_STOF_Start(nodeHandle);
                if (res != 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[Mil1394NodeConfig] 重新启动STOF发送失败，错误码: {res}");
                    // 不抛出异常，只记录日志，让用户知道需要手动重启
                }
            }
        }

        /// <summary>
        /// 异步流包接收配置
        /// </summary>
        private void AsyncRecvCfg(uint channel, ObservableCollection<Mil1394NodeConfigPanel.AsyncReceiveConfigItem> recvConfig)
        {
            IntPtr nodeHandle = _pnode[_nodeNumber];
            bool wasReceiving = false;

            // 参考官方例程：如果接收正在运行，先停止接收，应用配置后再重新启动
            try
            {
                // 尝试停止接收，如果成功说明接收正在运行
                int stopRes = _driverInterface.HZ1394_CC_MSG_ASYNC_RECV_Stop(nodeHandle);
                if (stopRes == 0)
                {
                    wasReceiving = true;
                    System.Diagnostics.Debug.WriteLine($"[Mil1394NodeConfig] 节点{_nodeNumber}接收正在运行，已停止以应用新配置");
                    // 短暂延迟，确保停止操作完成
                    System.Threading.Thread.Sleep(100);
                }
            }
            catch
            {
                // 停止失败，可能接收没在运行，继续配置
                wasReceiving = false;
            }

            uint packNum = 0;

            // 统计选中的配置项数量（按照官方例程的逻辑，严格按照选中的顺序提取）
            System.Diagnostics.Debug.WriteLine($"[Mil1394NodeConfig] 节点{_nodeNumber}开始统计接收配置，总配置项数: {recvConfig?.Count ?? 0}");
            foreach (var item in recvConfig ?? new ObservableCollection<Mil1394NodeConfigPanel.AsyncReceiveConfigItem>())
            {
                if (item.IsSelected)
                {
                    packNum++;
                    System.Diagnostics.Debug.WriteLine($"[Mil1394NodeConfig] 节点{_nodeNumber}选中项: MsgID=0x{item.MsgID}, IsSelected={item.IsSelected}");
                }
            }
            System.Diagnostics.Debug.WriteLine($"[Mil1394NodeConfig] 节点{_nodeNumber}统计完成，选中项数: {packNum}");

            if (packNum == 0)
            {
                // 如果没有选中的配置项，清空接收配置
                _driverInterface.PackNumDriver = 0;
                _driverInterface.MessageIDDriver = new uint[0];
                _driverInterface.MessageLenDriver = new uint[0];
                System.Diagnostics.Debug.WriteLine($"[Mil1394NodeConfig] 节点{_nodeNumber}接收配置：没有选中的MessageID，清空接收配置");
                
                // 如果之前接收正在运行，重新启动接收（使用空配置）
                if (wasReceiving)
                {
                    try
                    {
                        _driverInterface.HZ1394_CC_MSG_ASYNC_RECV_Start(nodeHandle);
                        System.Diagnostics.Debug.WriteLine($"[Mil1394NodeConfig] 节点{_nodeNumber}接收已重新启动（空配置）");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Mil1394NodeConfig] 节点{_nodeNumber}重新启动接收失败: {ex.Message}");
                    }
                }
                return;
            }

            uint[] MessageID = new uint[packNum];
            uint[] MessageLen = new uint[packNum];
            uint jcnt = 0;

            // 提取选中的配置项（按照官方例程的逻辑，严格按照选中的顺序提取，不排序）
            // 参考官方例程：MessageID[jcnt] = Convert.ToUInt32(dgvRecvAsync.Rows[icnt].Cells[1].Value.ToString(), 16);
            foreach (var item in recvConfig)
            {
                if (item.IsSelected)
                {
                    try
                    {
                        // 严格按照官方例程的方式转换：从16进制字符串转换为uint
                        uint msgId = Convert.ToUInt32(item.MsgID, 16);
                        MessageID[jcnt] = msgId;
                        MessageLen[jcnt] = (uint)item.DataLength;
                        
                        System.Diagnostics.Debug.WriteLine($"[Mil1394NodeConfig] 节点{_nodeNumber}接收配置：添加MessageID=0x{msgId:X2}({msgId}), DataLength={item.DataLength}");
                        
                        System.Diagnostics.Debug.WriteLine($"[Mil1394NodeConfig] 节点{_nodeNumber}接收配置：添加MessageID=0x{msgId:X2}({msgId}), DataLength={item.DataLength}");
                        jcnt++;
                    }
                    catch (Exception ex)
                    {
                        throw new Exception($"异步流包接收配置项解析失败: MsgID={item.MsgID}, {ex.Message}", ex);
                    }
                }
            }

            // 保存到DriverInterface（按照官方例程的方式）
            _driverInterface.PackNumDriver = packNum;
            _driverInterface.MessageIDDriver = new uint[packNum];
            _driverInterface.MessageLenDriver = new uint[packNum];
            for (int i = 0; i < MessageID.Length; i++)
            {
                _driverInterface.MessageIDDriver[i] = MessageID[i];
                _driverInterface.MessageLenDriver[i] = MessageLen[i];
            }

            System.Diagnostics.Debug.WriteLine($"[Mil1394NodeConfig] 节点{_nodeNumber}接收配置：Channel={channel}, PackNum={packNum}, MessageIDs=[{string.Join(", ", MessageID.Select(m => $"0x{m:X2}"))}]");

            // 应用配置
            int res = _driverInterface.ASYNC_RECV_CFG(nodeHandle, channel, packNum, MessageID, MessageLen);
            if (res != 0)
            {
                throw new Exception($"异步流包接收配置失败，错误码: {res}");
            }
            
            System.Diagnostics.Debug.WriteLine($"[Mil1394NodeConfig] 节点{_nodeNumber}接收配置应用成功");

            // 如果之前接收正在运行，重新启动接收以应用新配置
            if (wasReceiving)
            {
                try
                {
                    System.Threading.Thread.Sleep(50); // 短暂延迟，确保配置应用完成
                    res = _driverInterface.HZ1394_CC_MSG_ASYNC_RECV_Start(nodeHandle);
                    if (res != 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Mil1394NodeConfig] 节点{_nodeNumber}重新启动接收失败，错误码: {res}");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"[Mil1394NodeConfig] 节点{_nodeNumber}接收已重新启动，新配置已生效");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Mil1394NodeConfig] 节点{_nodeNumber}重新启动接收异常: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 异步流包发送配置
        /// </summary>
        private void AsyncSendCfg(uint loadASYNCPagNum, ObservableCollection<Mil1394NodeConfigPanel.AsyncSendConfigItem> sendConfig)
        {
            int res = 0;
            IntPtr nodeHandle = _pnode[_nodeNumber];

            // 设置同步选择
            res = _driverInterface.ASYNC_SEND_SYNSel_Set(nodeHandle);

            // 构建异步流包数据结构（按照官方例程的逻辑）
            var tas = new TNF_ASYNC_Struct[loadASYNCPagNum];

            for (int i = 0; i < loadASYNCPagNum && i < sendConfig.Count; i++)
            {
                var item = sendConfig[i];

                // 复制PayloadData，确保数组长度正确
                uint[] messageData = new uint[500];
                if (item.PayloadData != null && item.PayloadData.Length > 0)
                {
                    int copyLength = Math.Min(item.PayloadData.Length, 500);
                    Array.Copy(item.PayloadData, messageData, copyLength);
                }
                
                // 按照官方例程：tas[i].MessageID = asp.MessageID;
                // MessageID直接使用item.MessageID的值（整数，对应16进制的值）
                uint messageId = (uint)item.MessageID;
                
                System.Diagnostics.Debug.WriteLine($"[Mil1394NodeConfig] 节点{_nodeNumber}发送配置[{i}]：MessageID=0x{messageId:X2}({messageId}), Channel={item.Channel}, PayloadLength={item.PayloadLength}");
                
                tas[i] = new TNF_ASYNC_Struct
                {
                    MessageID = messageId, // 直接使用MessageID值，与官方例程一致
                    Channel = (uint)item.Channel,
                    MessageType = 0,
                    HeartBeatWord = (uint)item.Heartbeat,
                    HealthStatusWord = (uint)item.Health,
                    HeartBeatStyle = 1, // 自动
                    HeartBeatEnable = 1,
                    HeartBeatStep = (uint)item.HeartbeatStep,
                    STOFTransmitOffset = item.TransmitOffset,
                    STOFReceiveOffset = item.ReceiveOffset,
                    STOFPHMOffset = item.PHMOffset,
                    STOFCCSendOffset = (uint)item.SendOffset,
                    PayloadDataLength = (uint)item.PayloadLength,
                    MessageDataLength = (uint)item.PayloadLength,
                    MessageData = messageData,
                    Security = item.Security,
                    NodeID = 0,
                    Priority = item.Priority,
                    SoftVPCenable = item.VPC ? (uint)1 : (uint)0,
                    VPCASYNC = (uint)item.VPCAsync,
                    VPCErrorEnable = 0,
                    ErrMode = 0,
                    ErrNum = 0,
                    CRCASYNC = 0
                };
                
                System.Diagnostics.Debug.WriteLine($"[Mil1394NodeConfig] 节点{_nodeNumber}发送配置：MessageID=0x{messageId:X2}({messageId}), Channel={item.Channel}");
            }

            // 设置异步流包数据（按照官方例程：HZ1394_CC_MSG_ASYNC_Data_Set(tmpnode, SndMode, ID, pASYNC, len)）
            res |= _driverInterface.HZ1394_CC_MSG_ASYNC_Data_Set(nodeHandle, 1, 0, tas, loadASYNCPagNum);
            _driverInterface.AsyncPktNum = (byte)loadASYNCPagNum;

            System.Diagnostics.Debug.WriteLine($"[Mil1394NodeConfig] 节点{_nodeNumber}发送配置：PackNum={loadASYNCPagNum}, MessageIDs=[{string.Join(", ", sendConfig.Select(s => $"0x{s.MessageID:X2}"))}]");

            if (res != 0)
            {
                throw new Exception($"异步流包发送配置失败，错误码: {res}");
            }
            
            System.Diagnostics.Debug.WriteLine($"[Mil1394NodeConfig] 节点{_nodeNumber}发送配置应用成功");
        }

        private void InitCC(bool enableBM)
        {
            // 初始化CC节点
            int res = 0;
            IntPtr nodeHandle = _pnode[_nodeNumber];

            if (nodeHandle == IntPtr.Zero)
            {
                ReMessageBox.Show($"创建机箱失败，请检查机箱型号", "提示",MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            // 设置速度
            res |= _driverInterface.HZ1394_SetSpeed(_driverInterface.ComboBoxNodeRateDriver, nodeHandle);
            
            // BM使能（使用传入的参数）
            res |= _driverInterface.HZ1394_CC_BM_ENABLE(nodeHandle, enableBM ? 1u : 0u);
            
            // CRB LRTC使能
            res |= _driverInterface.HZ1394_CRB_LRTC_ENABLE(nodeHandle, 1);

            if (res != 0)
            {
                throw new Exception($"CC节点初始化失败，错误码: {res}");
            }
        }

        private void InitRN()
        {
            // 初始化RN节点
            int res = 0;
            IntPtr nodeHandle = _pnode[_nodeNumber];

            if (nodeHandle == IntPtr.Zero)
            {
                ReMessageBox.Show($"创建机箱失败，请检查机箱型号", "提示",MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            // 设置速度
            res |= _driverInterface.HZ1394_SetSpeed(_driverInterface.ComboBoxNodeRateDriver, nodeHandle);

            // BM使能（RN节点禁用BM）
            res |= _driverInterface.HZ1394_CC_BM_ENABLE(nodeHandle, 0);

            // STOF接收使能
            res |= _driverInterface.HZ1394_CC_MSG_RCV_STOF_ENABLE(nodeHandle, 1);

            if (res != 0)
            {
                throw new Exception($"RN节点初始化失败，错误码: {res}");
            }
        }

        private void InitBM(bool enableBM)
        {
            // BM节点使用CC初始化逻辑，但BM节点通常应该启用BM功能
            // 如果用户取消BM使能，则禁用BM功能
            InitCC(enableBM);
        }

        /// <summary>
        /// 保存节点配置到设备（按测试任务保存）
        /// </summary>
        public bool SaveNodeConfig(
            string testTaskName,
            string nodeType,
            string nodeRate,
            bool bmEnabled,
            int stofSendStyleIndex,
            string stofPeriod,
            string stofSendTimes,
            string stofVpc,
            uint[] stofPayload,
            string recvAsyncChannel,
            ObservableCollection<Mil1394NodeConfigPanel.AsyncReceiveConfigItem> asyncReceiveConfig,
            ObservableCollection<Mil1394NodeConfigPanel.AsyncSendConfigItem> asyncSendConfig)
        {
            try
            {
                if (_device == null)
                {
                    System.Diagnostics.Debug.WriteLine("[Mil1394NodeConfig] Device == null，跳过保存");
                    return false;
                }

                if (string.IsNullOrEmpty(testTaskName))
                {
                    System.Diagnostics.Debug.WriteLine("[Mil1394NodeConfig] 测试任务名称为空，跳过保存");
                    return false;
                }

                var cardConfig = EnsureMil1394BCardConfig();
                if (cardConfig == null)
                {
                    return false;
                }

                // 获取或创建测试任务配置
                var taskConfig = GetOrCreateTaskConfig(cardConfig, testTaskName);

                // 获取或创建节点配置（在测试任务配置中）
                var nodeConfig = GetOrCreateNodeConfig(taskConfig, _nodeNumber);

                // 保存节点初始化配置
                nodeConfig.NodeType = nodeType ?? "BM";
                nodeConfig.NodeRate = nodeRate ?? "400M";
                nodeConfig.BmEnabled = bmEnabled;

                // 保存STOF配置
                nodeConfig.StofSendStyleIndex = stofSendStyleIndex;
                nodeConfig.StofPeriod = stofPeriod ?? "15";
                nodeConfig.StofSendTimes = stofSendTimes ?? "100";
                nodeConfig.StofVpc = stofVpc ?? "0";
                if (stofPayload != null && stofPayload.Length >= 9)
                {
                    nodeConfig.StofPayload = new uint[9];
                    Array.Copy(stofPayload, nodeConfig.StofPayload, Math.Min(stofPayload.Length, 9));
                }

                // 保存异步流包接收配置
                nodeConfig.RecvAsyncChannel = recvAsyncChannel ?? "0";
                nodeConfig.AsyncReceiveConfig.Clear();
                if (asyncReceiveConfig != null)
                {
                    foreach (var item in asyncReceiveConfig)
                    {
                        nodeConfig.AsyncReceiveConfig.Add(new Mil1394BAsyncReceiveConfigItem
                        {
                            IsSelected = item.IsSelected,
                            MsgID = item.MsgID ?? "00",
                            DataLength = item.DataLength
                        });
                    }
                }

                // 保存异步流包发送配置
                nodeConfig.AsyncSendConfig.Clear();
                if (asyncSendConfig != null)
                {
                    foreach (var item in asyncSendConfig)
                    {
                        nodeConfig.AsyncSendConfig.Add(new Mil1394BAsyncSendConfigItem
                        {
                            MessageID = item.MessageID,
                            Channel = item.Channel,
                            Heartbeat = item.Heartbeat,
                            Health = item.Health,
                            HeartbeatStep = item.HeartbeatStep,
                            PayloadLength = item.PayloadLength,
                            SendOffset = item.SendOffset,
                            VPC = item.VPC,
                            VPCAsync = item.VPCAsync,
                            Security = item.Security,
                            Priority = item.Priority,
                            PayloadData = item.PayloadData != null ? (uint[])item.PayloadData.Clone() : new uint[500],
                            TransmitOffset = item.TransmitOffset,
                            ReceiveOffset = item.ReceiveOffset,
                            PHMOffset = item.PHMOffset
                        });
                    }
                }

                // 更新设备配置
                _pxiChassisService?.UpdateDeviceCardConfig(_device.Id, cardConfig);

                // 触发项目修改事件
                _eventAggregator?.GetEvent<ProjectModifiedEvent>()?.Publish(new ProjectModifiedEventArgs
                {
                    ModificationType = "NodeConfig",
                    Description = $"1394B节点{_nodeNumber}配置已保存（测试任务: {testTaskName}）"
                });

                System.Diagnostics.Debug.WriteLine($"[Mil1394NodeConfig] 节点{_nodeNumber}配置保存成功（测试任务: {testTaskName}）");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Mil1394NodeConfig] 保存节点配置失败: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[Mil1394NodeConfig] 异常堆栈: {ex.StackTrace}");
                return false;
            }
        }

        /// <summary>
        /// 读取节点配置从设备（按测试任务读取）
        /// </summary>
        public Mil1394BNodeConfig LoadNodeConfig(string testTaskName)
        {
            try
            {
                if (_device == null)
                {
                    System.Diagnostics.Debug.WriteLine("[Mil1394NodeConfig] Device == null，返回默认配置");
                    return new Mil1394BNodeConfig { NodeNumber = _nodeNumber };
                }

                if (string.IsNullOrEmpty(testTaskName))
                {
                    System.Diagnostics.Debug.WriteLine("[Mil1394NodeConfig] 测试任务名称为空，返回默认配置");
                    return new Mil1394BNodeConfig { NodeNumber = _nodeNumber };
                }

                var cardConfig = _device.CardConfigData as Mil1394BCardConfig;
                if (cardConfig == null)
                {
                    System.Diagnostics.Debug.WriteLine("[Mil1394NodeConfig] CardConfigData不是Mil1394BCardConfig类型，返回默认配置");
                    return new Mil1394BNodeConfig { NodeNumber = _nodeNumber };
                }

                // 从测试任务配置中读取
                var taskConfig = cardConfig.TestTaskConfigs?.FirstOrDefault(t => t.TestTaskName == testTaskName);
                if (taskConfig != null)
                {
                    var nodeConfig = taskConfig.NodeConfigs?.FirstOrDefault(n => n.NodeNumber == _nodeNumber);
                    if (nodeConfig != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Mil1394NodeConfig] 节点{_nodeNumber}配置读取成功（测试任务: {testTaskName}）");
                        return nodeConfig;
                    }
                }

                // 兼容旧版本：从NodeConfigs中读取（如果没有测试任务配置）
                var legacyNodeConfig = cardConfig.NodeConfigs?.FirstOrDefault(n => n.NodeNumber == _nodeNumber);
                if (legacyNodeConfig != null)
                {
                    System.Diagnostics.Debug.WriteLine($"[Mil1394NodeConfig] 从旧版本配置读取节点{_nodeNumber}配置");
                    return legacyNodeConfig;
                }

                System.Diagnostics.Debug.WriteLine($"[Mil1394NodeConfig] 节点{_nodeNumber}配置不存在，返回默认配置");
                return new Mil1394BNodeConfig { NodeNumber = _nodeNumber };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Mil1394NodeConfig] 读取节点配置失败: {ex.Message}");
                return new Mil1394BNodeConfig { NodeNumber = _nodeNumber };
            }
        }

        /// <summary>
        /// 确保Mil1394BCardConfig存在
        /// </summary>
        private Mil1394BCardConfig EnsureMil1394BCardConfig()
        {
            if (_device == null)
            {
                return null;
            }

            var cardConfig = _device.CardConfigData as Mil1394BCardConfig;
            if (cardConfig == null)
            {
                cardConfig = new Mil1394BCardConfig();
                _device.CardConfigData = cardConfig;
            }

            cardConfig.CardId = _device.Id;
            cardConfig.CardName = _device.CardName ?? _device.Model ?? "1394B";
            cardConfig.CardModel = _device.Model ?? "";
            cardConfig.ChassisName = _chassisName;
            return cardConfig;
        }

        /// <summary>
        /// 获取或创建测试任务配置
        /// </summary>
        private Mil1394BTestTaskConfig GetOrCreateTaskConfig(Mil1394BCardConfig cardConfig, string testTaskName)
        {
            testTaskName ??= string.Empty;
            var taskConfig = cardConfig.TestTaskConfigs?.FirstOrDefault(t => t.TestTaskName == testTaskName);
            if (taskConfig == null)
            {
                taskConfig = new Mil1394BTestTaskConfig { TestTaskName = testTaskName };
                cardConfig.TestTaskConfigs.Add(taskConfig);
            }
            return taskConfig;
        }

        /// <summary>
        /// 获取或创建节点配置（在测试任务配置中）
        /// </summary>
        private Mil1394BNodeConfig GetOrCreateNodeConfig(Mil1394BTestTaskConfig taskConfig, uint nodeNumber)
        {
            var nodeConfig = taskConfig.NodeConfigs?.FirstOrDefault(n => n.NodeNumber == nodeNumber);
            if (nodeConfig == null)
            {
                nodeConfig = new Mil1394BNodeConfig { NodeNumber = nodeNumber };
                taskConfig.NodeConfigs.Add(nodeConfig);
            }
            return nodeConfig;
        }
    }
}
