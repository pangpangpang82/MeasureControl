using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.IO;
using System.Text;
using System.Windows;
using Microsoft.Win32;
using MeasureControl.Drivers;
using MeasureControl.Helpers;
using Prism.Mvvm;
using MeasureControl.Views.Dialogs;

namespace MeasureControl.ViewModels.TestTask.CardCATPanel.MIL1394B
{
    /// <summary>
    /// 数据收发面板ViewModel
    /// </summary>
    public class Mil1394NodeSendRcvPanelViewModel : BindableBase
    {
        private readonly HZ1394DriverInterface _driverInterface;
        private readonly List<BMDataItem> _bmDataList = new List<BMDataItem>();
        private readonly List<TNF_RECV_PACKET_Struct> _packetList = new List<TNF_RECV_PACKET_Struct>(); // 保存原始数据包
        private readonly Dictionary<int, TNF_RECV_PACKET_Struct> _packetByNum = new Dictionary<int, TNF_RECV_PACKET_Struct>();
        private Thread _bmDataThread;
        private volatile bool _isBMDataMonitorRunning = false;
        private IntPtr _currentNodeHandle = IntPtr.Zero;
        private int _bmCount = 0;
        private readonly object _bmDataLock = new object();
        private int _lastReturnedCount = 0; // 记录上次返回的数据数量，用于增量获取
        private int _displayRemovedCount = 0; // 记录UI中删除的数据数量，用于调整计数

        public bool IsBMDataMonitorRunning => _isBMDataMonitorRunning;

        public Mil1394NodeSendRcvPanelViewModel(HZ1394DriverInterface driverInterface)
        {
            _driverInterface = driverInterface ?? throw new ArgumentNullException(nameof(driverInterface));
        }

        /// <summary>
        /// 开始发送
        /// </summary>
        public void StartSend(IntPtr nodeHandle)
        {
            if (nodeHandle == IntPtr.Zero)
            {
                ReMessageBox.Show($"创建机箱失败，请检查机箱型号", "提示",MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            try
            {
                switch (_driverInterface.TmpnodeType)
                {
                    case "CC":
                        SendCC(nodeHandle);
                        break;
                    case "RN":
                        SendRN(nodeHandle);
                        break;
                    case "BM":
                        SendCC(nodeHandle); // BM模式使用CC发送
                        break;
                    default:
                        ReMessageBox.Show($"创建机箱失败，请检查机箱型号", "提示",MessageBoxButton.OK, MessageBoxImage.Warning);
                        break;
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"开始发送失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 停止发送
        /// </summary>
        public void StopSend(IntPtr nodeHandle)
        {
            if (nodeHandle == IntPtr.Zero)
            {
                ReMessageBox.Show($"创建机箱失败，请检查机箱型号", "提示",MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            try
            {
                switch (_driverInterface.TmpnodeType)
                {
                    case "CC":
                    case "BM":
                        StopCC(nodeHandle);
                        break;
                    case "RN":
                        StopRN(nodeHandle);
                        break;
                    default:
                        ReMessageBox.Show($"创建机箱失败，请检查机箱型号", "提示",MessageBoxButton.OK, MessageBoxImage.Warning);
                        break;
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"停止发送失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 开始接收
        /// </summary>
        public void StartReceive(IntPtr nodeHandle)
        {
            if (nodeHandle == IntPtr.Zero)
            {
                ReMessageBox.Show($"创建机箱失败，请检查机箱型号", "提示",MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            try
            {
                // 移除BM_CC_MSG_Cnt_Get的设置，该标志只用于BM数据监控功能
                // _driverInterface.BM_CC_MSG_Cnt_Get = true;
                int res = _driverInterface.HZ1394_CC_MSG_ASYNC_RECV_Start(nodeHandle);
                if (res != 0)
                {
                    throw new Exception($"启动接收失败，错误码: {res}");
                }
                _driverInterface.HZStartRecvThd(nodeHandle);
            }
            catch (Exception ex)
            {
                throw new Exception($"开始接收失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 停止接收
        /// </summary>
        public void StopReceive(IntPtr nodeHandle)
        {
            if (nodeHandle == IntPtr.Zero)
            {
                ReMessageBox.Show($"创建机箱失败，请检查机箱型号", "提示",MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            try
            {
                // 移除BM_CC_MSG_Cnt_Get的设置，该标志只用于BM数据监控功能
                // _driverInterface.BM_CC_MSG_Cnt_Get = false;
                int res = _driverInterface.HZ1394_CC_MSG_ASYNC_RECV_Stop(nodeHandle);
                if (res != 0)
                {
                    throw new Exception($"停止接收失败，错误码: {res}");
                }

                _driverInterface.HZStopRecvThd(nodeHandle);
            }
            catch (Exception ex)
            {
                throw new Exception($"停止接收失败: {ex.Message}", ex);
            }
        }

        private void StopCC(IntPtr nodeHandle)
        {
            _driverInterface.HZ1394_CC_MSG_STOF_Stop(nodeHandle);
            _driverInterface.HZ1394_CC_MSG_ASYNC_SEND_Stop(nodeHandle);
        }

        private void StopRN(IntPtr nodeHandle)
        {
            _driverInterface.HZ1394_RN_MSG_SEND_Stop(nodeHandle);
        }

        /// <summary>
        /// 启动BM数据监控
        /// </summary>
        public void StartBMDataMonitor(IntPtr nodeHandle)
        {
            if (nodeHandle == IntPtr.Zero)
            {
                ReMessageBox.Show($"创建机箱失败，请检查机箱型号", "提示",MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            if (_isBMDataMonitorRunning)
            {
                return; // 已经在运行
            }

            try
            {
                _currentNodeHandle = nodeHandle;
                _driverInterface.BM_CC_MSG_Cnt_Get = true;

                lock (_bmDataLock)
                {
                    _lastReturnedCount = 0;
                    _displayRemovedCount = 0;
                }

                _isBMDataMonitorRunning = true;
                RaisePropertyChanged(nameof(IsBMDataMonitorRunning));

                // 启动后台线程获取BM数据
                _bmDataThread = new Thread(BMDataMonitorThread)
                {
                    IsBackground = true,
                    Name = "BMDataMonitorThread"
                };
                _bmDataThread.Start();
            }
            catch (Exception ex)
            {
                _isBMDataMonitorRunning = false;
                RaisePropertyChanged(nameof(IsBMDataMonitorRunning));
                throw new Exception($"启动数据监控失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 停止BM数据监控
        /// </summary>
        public void StopBMDataMonitor(IntPtr nodeHandle)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("[StopBMDataMonitor] 开始停止BM数据监控...");

                // 1. 首先设置停止标志和清空句柄，让用户线程知道要退出
                _isBMDataMonitorRunning = false;
                _currentNodeHandle = IntPtr.Zero; // 先清空句柄，让线程循环检测到并退出
                _driverInterface.BM_CC_MSG_Cnt_Get = false;
                RaisePropertyChanged(nameof(IsBMDataMonitorRunning));

                // 2. 参考官方例程：先停止异步接收，再停止接收线程
                // 这样可以让Packet_Get不再阻塞，用户线程才能正常退出
                if (nodeHandle != IntPtr.Zero)
                {
                    try
                    {
                        // 停止异步接收
                        _driverInterface.HZ1394_CC_MSG_ASYNC_RECV_Stop(nodeHandle);
                        System.Diagnostics.Debug.WriteLine("[StopBMDataMonitor] 已停止异步接收");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[StopBMDataMonitor] 停止异步接收失败: {ex.Message}");
                    }

                    try
                    {
                        // 停止接收线程（DLL内部线程）
                        _driverInterface.HZStopRecvThd(nodeHandle);
                        System.Diagnostics.Debug.WriteLine("[StopBMDataMonitor] 已停止接收线程");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[StopBMDataMonitor] 停止接收线程失败: {ex.Message}");
                    }
                }

                // 3. 等待用户线程结束（减少等待时间，避免阻塞UI）
                if (_bmDataThread != null && _bmDataThread.IsAlive)
                {
                    // 只等待500ms，如果线程还没退出就放弃等待
                    // 线程已设置为后台线程，进程退出时会自动终止
                    bool joined = _bmDataThread.Join(500);
                    if (!joined)
                    {
                        System.Diagnostics.Debug.WriteLine("[StopBMDataMonitor] BM数据监控线程未在预期时间内退出，继续（后台线程会自动清理）");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("[StopBMDataMonitor] BM数据监控线程已正常退出");
                    }
                }

                _bmDataThread = null;

                // 4. 清理缓存数据，释放内存
                lock (_bmDataLock)
                {
                    _bmDataList.Clear();
                    _packetList.Clear();
                    _packetByNum.Clear();
                    _lastReturnedCount = 0;
                    _displayRemovedCount = 0;
                }

                System.Diagnostics.Debug.WriteLine("[StopBMDataMonitor] BM数据监控已停止，缓存已清理");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[StopBMDataMonitor] 停止数据监控失败: {ex.Message}");
            }
        }

        /// <summary>
        /// BM数据监控线程
        /// </summary>
        private void BMDataMonitorThread()
        {
            IntPtr msgPtr = IntPtr.Zero;
            int consecutiveEmptyReads = 0; // 连续空读取计数，用于自适应延迟
            int consecutiveErrors = 0; // 连续错误计数，用于检测异常退出

            System.Diagnostics.Debug.WriteLine("[BMDataMonitorThread] 线程已启动");

            try
            {
                while (_isBMDataMonitorRunning && _currentNodeHandle != IntPtr.Zero)
                {
                    try
                    {
                        // 再次检查退出标志（在可能的阻塞调用前）
                        if (!_isBMDataMonitorRunning || _currentNodeHandle == IntPtr.Zero)
                        {
                            System.Diagnostics.Debug.WriteLine("[BMDataMonitorThread] 检测到停止标志，退出循环");
                            break;
                        }

                        // 调用DLL接口获取BM数据包
                        int res = HZ1394Interface.Mil1394_CC_Packet_Get(_currentNodeHandle, ref msgPtr);

                        // 成功获取数据，重置错误计数
                        consecutiveErrors = 0;

                        if (res > 0 && msgPtr != IntPtr.Zero)
                        {
                            consecutiveEmptyReads = 0; // 重置空读取计数

                            // 解析数据包数组
                            TNF_RECV_PACKET_Struct[] packets = new TNF_RECV_PACKET_Struct[res];

                            // 批量处理数据包，减少锁竞争
                            List<BMDataItem> batchItems = new List<BMDataItem>(res);
                            List<TNF_RECV_PACKET_Struct> batchPackets = new List<TNF_RECV_PACKET_Struct>(res);

                            for (int i = 0; i < res; i++)
                            {
                                // 计算每个结构体的偏移量
                                IntPtr offset = new IntPtr(msgPtr.ToInt64() + i * Marshal.SizeOf(typeof(TNF_RECV_PACKET_Struct)));
                                packets[i] = (TNF_RECV_PACKET_Struct)Marshal.PtrToStructure(offset, typeof(TNF_RECV_PACKET_Struct));

                                // 转换为BMDataItem
                                var bmItem = ConvertToBMDataItem(packets[i]);
                                batchItems.Add(bmItem);
                                batchPackets.Add(packets[i]);
                            }

                            // 批量添加到列表，减少锁持有时间
                            lock (_bmDataLock)
                            {
                                for (int i = 0; i < batchItems.Count; i++)
                                {
                                    _bmDataList.Add(batchItems[i]);
                                    _packetList.Add(batchPackets[i]);
                                    _packetByNum[batchItems[i].Num] = batchPackets[i];
                                }

                                // 限制列表大小，避免内存溢出
                                const int MAX_MEMORY_ITEMS = 20000;
                                if (_bmDataList.Count > MAX_MEMORY_ITEMS)
                                {
                                    int removeCount = Math.Min(5000, _bmDataList.Count - (MAX_MEMORY_ITEMS - 5000));
                                    for (int i = 0; i < removeCount; i++)
                                    {
                                        _packetByNum.Remove(_bmDataList[i].Num);
                                    }
                                    _bmDataList.RemoveRange(0, removeCount);
                                    _packetList.RemoveRange(0, removeCount);
                                    _lastReturnedCount = Math.Max(0, _lastReturnedCount - removeCount);
                                }
                            }

                            // 有数据时短暂延迟，让出CPU时间片
                            Thread.Sleep(1);
                        }
                        else
                        {
                            // 没有数据时，使用自适应延迟
                            consecutiveEmptyReads++;
                            if (consecutiveEmptyReads < 10)
                            {
                                Thread.Sleep(1);
                            }
                            else if (consecutiveEmptyReads < 100)
                            {
                                Thread.Sleep(5);
                            }
                            else
                            {
                                Thread.Sleep(10);
                            }
                        }
                    }
                    catch (ThreadInterruptedException)
                    {
                        System.Diagnostics.Debug.WriteLine("[BMDataMonitorThread] 线程被中断，正常退出");
                        break;
                    }
                    catch (Exception ex)
                    {
                        consecutiveErrors++;
                        System.Diagnostics.Debug.WriteLine($"[BMDataMonitorThread] 异常: {ex.Message}");
                        
                        // 如果连续出错超过10次，可能是底层驱动已关闭，退出线程
                        if (consecutiveErrors > 10)
                        {
                            System.Diagnostics.Debug.WriteLine("[BMDataMonitorThread] 连续错误过多，退出线程");
                            break;
                        }
                        
                        Thread.Sleep(10);
                        consecutiveEmptyReads = 0;
                    }
                }
            }
            catch (ThreadInterruptedException)
            {
                System.Diagnostics.Debug.WriteLine("[BMDataMonitorThread] 线程被外部中断");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[BMDataMonitorThread] 线程异常退出: {ex.Message}");
            }
            finally
            {
                System.Diagnostics.Debug.WriteLine("[BMDataMonitorThread] 线程已退出");
            }
        }

        /// <summary>
        /// 将TNF_RECV_PACKET_Struct转换为BMDataItem
        /// </summary>
        private BMDataItem ConvertToBMDataItem(TNF_RECV_PACKET_Struct packet)
        {
            var item = new BMDataItem
            {
                Num = ++_bmCount,
                LRTC = packet.LRTC,
                RTC = packet.RTC
            };

            // 消息类型
            switch (packet.MessageTYPE)
            {
                case 0:
                    item.MsgType = "STOF";
                    break;
                case 1:
                case 2:
                    item.MsgType = "ASYNC";
                    break;
                case 3:
                    item.MsgType = "BusReset";
                    break;
                default:
                    item.MsgType = "";
                    break;
            }

            // VPC
            if (packet.MessageTYPE == 0) // STOF
            {
                if ((packet.VPCErrSTOF & 0x7FFFFFFF) == 1)
                {
                    item.VPC = "1";
                }
                else
                {
                    item.VPC = "0";
                }
            }
            else if (packet.MessageTYPE == 1 || packet.MessageTYPE == 2) // ASYNC
            {
                if ((packet.VPCErrASYNC & 0x7FFFFFFF) == 1)
                {
                    item.VPC = "1";
                }
                else
                {
                    item.VPC = "0";
                }
            }
            else
            {
                item.VPC = "0";
            }

            // CRC
            if (packet.MessageTYPE == 0) // STOF
            {
                item.CRC = packet.CRCErrSTOF == 1 ? "1" : "0";
            }
            else if (packet.MessageTYPE == 1 || packet.MessageTYPE == 2) // ASYNC
            {
                item.CRC = packet.CRCErrASYNC == 1 ? "1" : "0";
            }
            else
            {
                item.CRC = "0";
            }

            // Len_err
            if (packet.MessageTYPE == 1 || packet.MessageTYPE == 2) // ASYNC
            {
                if ((packet.VPCErrASYNC & 0x80000000) == 0x80000000)
                {
                    item.LenErr = "1";
                }
                else
                {
                    item.LenErr = "0";
                }
            }
            else if (packet.MessageTYPE == 0) // STOF
            {
                item.LenErr = packet.STOFLIMITErr == 1 ? "1" : "0";
            }
            else
            {
                item.LenErr = "0";
            }

            // MsgId
            if (packet.MessageTYPE == 1 || packet.MessageTYPE == 2)
            {
                item.MsgId = packet.MessageID.ToString("X");
            }
            else
            {
                item.MsgId = "------";
            }

            // ChnnelID
            item.ChnnelID = packet.Channel.ToString();

            return item;
        }

        /// <summary>
        /// 保存数据到Excel文件
        /// </summary>
        public void SaveData()
        {
            try
            {
                lock (_bmDataLock)
                {
                    if (_bmDataList == null || _bmDataList.Count == 0)
                    {
                        ReMessageBox.Show("没有数据可保存", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }

                    // 弹出保存文件对话框
                    var saveFileDialog = new SaveFileDialog
                    {
                        Filter = "Excel文件 (*.xlsx)|*.xlsx|CSV文件 (*.csv)|*.csv|所有文件 (*.*)|*.*",
                        Title = "保存BM数据",
                        FileName = $"1394B_BM数据_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx",
                        DefaultExt = "xlsx"
                    };

                    if (saveFileDialog.ShowDialog() == true)
                    {
                        string filePath = saveFileDialog.FileName;
                        string extension = Path.GetExtension(filePath).ToLower();

                        if (extension == ".csv")
                        {
                            // 保存为CSV格式（Excel兼容）
                            SaveToCsv(filePath);
                        }
                        else
                        {
                            // 保存为Excel格式（使用CSV格式，Excel可以打开）
                            // 注意：如果需要真正的Excel格式，需要安装EPPlus NuGet包
                            SaveToExcelCsv(filePath);
                        }

                        ReMessageBox.Show($"数据已成功保存到：{filePath}", "保存成功",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                ReMessageBox.Show($"保存数据失败：{ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                System.Diagnostics.Debug.WriteLine($"SaveData异常: {ex.Message}\n{ex.StackTrace}");
            }
        }

        /// <summary>
        /// 保存为CSV格式
        /// </summary>
        private void SaveToCsv(string filePath)
        {
            using (var writer = new StreamWriter(filePath, false, Encoding.UTF8))
            {
                // 写入BOM以支持Excel正确识别UTF-8编码
                writer.Write('\uFEFF');

                // 写入表头
                writer.WriteLine("Num,MsgType,Msg id,Chnnel ID,LRTC,RTC,VPC,CRC,Len_err");

                // 写入数据行
                foreach (var item in _bmDataList)
                {
                    writer.WriteLine($"{item.Num},{EscapeCsvField(item.MsgType)},{EscapeCsvField(item.MsgId)}," +
                                   $"{EscapeCsvField(item.ChnnelID)},{item.LRTC},{item.RTC}," +
                                   $"{EscapeCsvField(item.VPC)},{EscapeCsvField(item.CRC)},{EscapeCsvField(item.LenErr)}");
                }
            }
        }

        /// <summary>
        /// 保存为Excel兼容格式（CSV格式，但使用.xlsx扩展名时Excel会自动识别）
        /// </summary>
        private void SaveToExcelCsv(string filePath)
        {
            // 如果文件扩展名是.xlsx，但项目中没有EPPlus库，则保存为CSV格式
            // Excel可以打开CSV文件，用户只需在打开时选择CSV格式即可
            // 或者将扩展名改为.csv
            if (Path.GetExtension(filePath).ToLower() == ".xlsx")
            {
                // 提示用户：由于没有Excel库，将保存为CSV格式
                var result = ReMessageBox.Show(
                    "当前项目未安装Excel库（EPPlus），将保存为CSV格式（Excel可以打开）。\n\n是否继续？",
                    "提示",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (result == MessageBoxResult.No)
                {
                    return;
                }

                // 将扩展名改为.csv
                filePath = Path.ChangeExtension(filePath, ".csv");
            }

            SaveToCsv(filePath);
        }

        /// <summary>
        /// 转义CSV字段（处理包含逗号、引号或换行符的字段）
        /// </summary>
        private string EscapeCsvField(string field)
        {
            if (string.IsNullOrEmpty(field))
                return string.Empty;

            // 如果字段包含逗号、引号或换行符，需要用引号括起来，并转义引号
            if (field.Contains(",") || field.Contains("\"") || field.Contains("\n") || field.Contains("\r"))
            {
                return "\"" + field.Replace("\"", "\"\"") + "\"";
            }

            return field;
        }

        /// <summary>
        /// 从文件加载数据
        /// </summary>
        public void LoadDataFromFile()
        {
            // TODO: 实现从文件加载数据逻辑
        }

        /// <summary>
        /// 获取BM数据（返回所有数据，不清空内部列表）
        /// </summary>
        public List<BMDataItem> GetBMData()
        {
            try
            {
                lock (_bmDataLock)
                {
                    // 返回当前所有数据的副本
                    return new List<BMDataItem>(_bmDataList);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetBMData异常: {ex.Message}");
                return new List<BMDataItem>();
            }
        }

        /// <summary>
        /// 获取新增的BM数据（增量获取，提高性能）
        /// </summary>
        /// <returns>新增的数据项列表</returns>
        public List<BMDataItem> GetNewBMData()
        {
            return GetNewBMData(int.MaxValue);
        }

        public List<BMDataItem> GetNewBMData(int maxCount)
        {
            try
            {
                lock (_bmDataLock)
                {
                    if (_bmDataList.Count <= _lastReturnedCount)
                    {
                        return new List<BMDataItem>(); // 没有新数据
                    }

                    // 只返回新增的数据
                    int startIndex = _lastReturnedCount;
                    int count = Math.Min(maxCount, _bmDataList.Count - _lastReturnedCount);
                    var newData = _bmDataList.GetRange(startIndex, count);
                    _lastReturnedCount += count;

                    return newData;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetNewBMData异常: {ex.Message}");
                return new List<BMDataItem>();
            }
        }

        /// <summary>
        /// 清空BM数据
        /// </summary>
        public void ClearBMData()
        {
            lock (_bmDataLock)
            {
                _bmDataList.Clear();
                _packetList.Clear();
                _packetByNum.Clear();
                _bmCount = 0; // 重置计数
                _lastReturnedCount = 0; // 重置返回计数
                _displayRemovedCount = 0; // 重置显示删除计数
            }
        }

        /// <summary>
        /// 调整显示计数（当UI删除旧数据时调用）
        /// </summary>
        public void AdjustDisplayCount(int removedCount)
        {
            lock (_bmDataLock)
            {
                _displayRemovedCount += removedCount;
                // 调整_lastReturnedCount，使其与UI显示的数据量保持一致
                _lastReturnedCount = Math.Max(0, _lastReturnedCount - removedCount);
            }
        }

        /// <summary>
        /// 根据Num获取原始数据包
        /// </summary>
        public TNF_RECV_PACKET_Struct? GetPacketByNum(int num)
        {
            lock (_bmDataLock)
            {
                if (_packetByNum.TryGetValue(num, out var packet))
                    return packet;

                return null;
            }
        }

        private void SendCC(IntPtr nodeHandle)
        {
            if (_driverInterface.AsyncPktNum != 0)
            {
                _driverInterface.HZ1394_CC_MSG_ASYNC_SEND_Start(nodeHandle);
            }
            _driverInterface.HZ1394_CC_MSG_STOF_Start(nodeHandle);
        }

        private void SendRN(IntPtr nodeHandle)
        {
            if (nodeHandle == IntPtr.Zero)
            {
                ReMessageBox.Show($"创建机箱失败，请检查机箱型号", "提示",MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            // 如果异步流包为空，功能不启动
            if (_driverInterface.AsyncPktNum != 0)
            {
                _driverInterface.HZ1394_CC_MSG_ASYNC_SEND_Start(nodeHandle);
            }
        }
    }

    /// <summary>
    /// BM数据项
    /// </summary>
    public class BMDataItem
    {
        public int Num { get; set; }
        public string MsgType { get; set; }
        public string MsgId { get; set; }
        public string ChnnelID { get; set; }
        public ulong LRTC { get; set; }
        public uint RTC { get; set; }
        public string VPC { get; set; }
        public string CRC { get; set; }
        public string LenErr { get; set; }
    }
}
