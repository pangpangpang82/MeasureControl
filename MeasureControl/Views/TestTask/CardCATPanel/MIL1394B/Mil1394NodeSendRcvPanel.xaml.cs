using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using MeasureControl.Drivers;
using MeasureControl.Helpers;
using MeasureControl.ViewModels.TestTask;
using MeasureControl.ViewModels.TestTask.CardCATPanel.MIL1394B;
using MeasureControl.Views.Dialogs;
using BMDataItem = MeasureControl.ViewModels.TestTask.CardCATPanel.MIL1394B.BMDataItem;

namespace MeasureControl.Views.TestTask.CardCATPanel.Mil1394B
{
    /// <summary>
    /// Mil1394NodeSendRcvPanel.xaml 的交互逻辑
    /// WPF版本的数据收发界面，替代WinForms的NodeSendRcvForm
    /// 
    /// 
    /// </summary>
    public partial class Mil1394NodeSendRcvPanel : UserControl, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        private readonly uint _cardNumber;
        private readonly uint _nodeNumber;
        private readonly IntPtr[] _pnode;
        private readonly Mil1394NodeSendRcvPanelViewModel _viewModel;
        private readonly System.Windows.Threading.DispatcherTimer _refreshTimer;
        private Mil1394TestPanelViewModel _parentViewModel;
        private bool _isBMDataMonitorRunning = false;
        public bool IsBMDataMonitorRunning
        {
            get => _isBMDataMonitorRunning;
            set
            {
                if (_isBMDataMonitorRunning != value)
                {
                    _isBMDataMonitorRunning = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsBMDataMonitorRunning)));
                }
            }
        }
        private int _lastBMDataCount = 0; // 记录上次显示的数据数量，用于避免重复添加
        private const int MAX_DISPLAY_ITEMS = 2000; // 最大显示数据量，只显示最新2000条（可根据需要调整）
        private bool _isUpdatingUI = false; // UI更新标志，防止重复更新
        private const int MAX_APPEND_PER_REFRESH = 200; // 每次刷新最多追加条数，避免UI一次性处理过多数据

        public void SetParentViewModel(Mil1394TestPanelViewModel parentViewModel)
        {
            _parentViewModel = parentViewModel;
        }

        public Mil1394NodeSendRcvPanel(uint cardNum, uint nodeNumber, IntPtr[] pnode, Mil1394NodeSendRcvPanelViewModel viewModel)
        {
            InitializeComponent();
            _cardNumber = cardNum;
            _nodeNumber = nodeNumber;
            _pnode = pnode ?? throw new ArgumentNullException(nameof(pnode));
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));

            DataContext = _viewModel;

            // 初始化数据表格
            DgvBMData.ItemsSource = new ObservableCollection<BMDataItem>();
            LvwCheckData.ItemsSource = new ObservableCollection<CheckDataItem>();

            // 初始化刷新定时器 - 大幅降低刷新频率以减少CPU占用和UI卡顿
            _refreshTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(1000) // 改为1秒刷新一次，大幅减少UI更新频率
            };
            _refreshTimer.Tick += RefreshTimer_Tick;

            // 初始化按钮状态颜色（符合项目风格）
            InitializeButtonStates();
        }

        private bool _isSending = false;
        public bool IsSending
        {
            get => _isSending;
            set
            {
                if (_isSending != value)
                {
                    _isSending = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSending)));
                }
            }
        }

        private bool _isReceiving = false;
        public bool IsReceiving
        {
            get => _isReceiving;
            set
            {
                if (_isReceiving != value)
                {
                    _isReceiving = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsReceiving)));
                }
            }
        }

        private void InitializeButtonStates()
        {
            // 初始状态：开始按钮使用绿色
            BtnStart.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(82, 196, 26)); // #52c41a 绿色
            BtnRcv.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(82, 196, 26)); // #52c41a 绿色
            BtnBMStart.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(82, 196, 26)); // #52c41a 绿色
        }

        private async void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!IsSending)
                {
                    // 开始发送
                    IntPtr handle = _pnode[_nodeNumber];
                    if (handle == IntPtr.Zero && _parentViewModel != null)
                    {
                        handle = await _parentViewModel.EnsureNodeOpenedAsync(_nodeNumber);
                    }
                    _viewModel?.StartSend(handle);
                    BtnStart.Content = "停止发送";
                    BtnStart.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 77, 79)); // #ff4d4f 红色
                    IsSending = true;
                }
                else
                {
                    // 停止发送
                    if (_pnode[_nodeNumber] != IntPtr.Zero)
                    {
                        _viewModel?.StopSend(_pnode[_nodeNumber]);
                    }
                    BtnStart.Content = "开始发送";
                    BtnStart.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(82, 196, 26)); // #52c41a 绿色
                    IsSending = false;
                }
            }
            catch (Exception ex)
            {
                ReMessageBox.Show($"操作失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnRcv_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!IsReceiving)
                {
                    // 开始接收
                    IntPtr handle = _pnode[_nodeNumber];
                    if (handle == IntPtr.Zero && _parentViewModel != null)
                    {
                        handle = await _parentViewModel.EnsureNodeOpenedAsync(_nodeNumber);
                    }
                    _viewModel?.StartReceive(handle);
                    BtnRcv.Content = "停止接收";
                    BtnRcv.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 77, 79)); // #ff4d4f 红色
                    IsReceiving = true;
                    _refreshTimer.Start();
                }
                else
                {
                    // 停止接收
                    if (_pnode[_nodeNumber] != IntPtr.Zero)
                    {
                        _viewModel?.StopReceive(_pnode[_nodeNumber]);
                    }
                    BtnRcv.Content = "开始接收";
                    BtnRcv.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(82, 196, 26)); // #52c41a 绿色
                    IsReceiving = false;
                    _refreshTimer.Stop();
                }
            }
            catch (Exception ex)
            {
                ReMessageBox.Show($"操作失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnBMStart_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!IsBMDataMonitorRunning)
                {
                    // 启动数据监控
                    IntPtr handle = _pnode[_nodeNumber];
                    if (handle == IntPtr.Zero && _parentViewModel != null)
                    {
                        handle = await _parentViewModel.EnsureNodeOpenedAsync(_nodeNumber);
                    }
                    _viewModel?.StartBMDataMonitor(handle);

                    // 启动刷新定时器
                    if (!_refreshTimer.IsEnabled)
                    {
                        _refreshTimer.Start();
                    }

                    IsBMDataMonitorRunning = true;
                }
                else
                {
                    // 暂停数据监控
                    if (_pnode[_nodeNumber] != IntPtr.Zero)
                    {
                        _viewModel?.StopBMDataMonitor(_pnode[_nodeNumber]);
                    }

                    // 停止刷新定时器
                    _refreshTimer.Stop();

                    IsBMDataMonitorRunning = false;
                }
            }
            catch (Exception ex)
            {
                ReMessageBox.Show($"数据监控操作失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnBMDataClear_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 清空ViewModel中的数据（会同时重置所有计数）
                _viewModel?.ClearBMData();

                // 清空显示的数据
                var items = DgvBMData.ItemsSource as ObservableCollection<BMDataItem>;
                if (items != null)
                {
                    items.Clear();
                }

                // 重置计数
                _lastBMDataCount = 0;

                // 清空校验数据
                var checkItems = LvwCheckData.ItemsSource as ObservableCollection<CheckDataItem>;
                if (checkItems != null)
                {
                    checkItems.Clear();
                }
            }
            catch (Exception ex)
            {
                ReMessageBox.Show($"清空数据失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                System.Diagnostics.Debug.WriteLine($"清空数据失败: {ex.Message}");
            }
        }

        private void BtnSaveData_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _viewModel?.SaveData();
                // SaveData方法内部已经显示成功/失败消息，这里不需要再显示
            }
            catch (Exception ex)
            {
                ReMessageBox.Show($"保存数据失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                System.Diagnostics.Debug.WriteLine($"BtnSaveData_Click异常: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private void BtnDataReadFromFile_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _viewModel?.LoadDataFromFile();
                RefreshDataDisplay();
                ReMessageBox.Show("数据加载成功", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                ReMessageBox.Show($"加载数据失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RefreshTimer_Tick(object sender, EventArgs e)
        {
            // 如果正在更新UI，跳过本次刷新，避免堆积
            if (_isUpdatingUI || !IsBMDataMonitorRunning)
            {
                return;
            }
            
            RefreshDataDisplay();
        }

        private void RefreshDataDisplay()
        {
            try
            {
                if (!IsBMDataMonitorRunning || _isUpdatingUI)
                {
                    return;
                }

                _isUpdatingUI = true;

                var items = DgvBMData.ItemsSource as ObservableCollection<BMDataItem>;
                if (items == null)
                {
                    return;
                }

                var newData = _viewModel?.GetNewBMData(MAX_APPEND_PER_REFRESH);
                if (newData == null || newData.Count == 0)
                {
                    return;
                }

                for (int i = 0; i < newData.Count; i++)
                {
                    items.Add(newData[i]);
                }

                if (items.Count > MAX_DISPLAY_ITEMS)
                {
                    int removeCount = items.Count - MAX_DISPLAY_ITEMS;
                    for (int i = 0; i < removeCount; i++)
                    {
                        items.RemoveAt(0);
                    }

                    _viewModel?.AdjustDisplayCount(removeCount);
                }

                _lastBMDataCount = items.Count;

                // 可选：自动滚动到底部（如果用户需要）
                // 注释掉自动滚动，避免按钮被滚动出视野
                // 用户可以通过滚动条手动查看最新数据
                /*
                if (items.Count > 10)
                {
                    // 延迟滚动，避免阻塞UI
                    System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
                        System.Windows.Threading.DispatcherPriority.Loaded,
                        new Action(() =>
                        {
                            try
                            {
                                if (items.Count > 0)
                                {
                                    // 只在DataGrid内部滚动，不影响外层布局
                                    DgvBMData.ScrollIntoView(items[items.Count - 1]);
                                }
                            }
                            catch { }
                        }));
                }
                */
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"RefreshDataDisplay异常: {ex.Message}");
            }
            finally
            {
                _isUpdatingUI = false;
            }
        }

        // BMDataItem类已移动到ViewModel中

        /// <summary>
        /// 校验数据项
        /// </summary>
        public class CheckDataItem
        {
            public int Index { get; set; }
            public string Time { get; set; }
            public string Data { get; set; }
        }

        private void DgvBMData_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (DgvBMData.SelectedItem is BMDataItem selectedItem)
                {
                    // 根据选中的Num获取原始数据包
                    var packet = _viewModel?.GetPacketByNum(selectedItem.Num);
                    if (packet.HasValue)
                    {
                        UpdateCheckData(packet.Value);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"DgvBMData_SelectionChanged异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 更新校验数据显示
        /// </summary>
        private void UpdateCheckData(TNF_RECV_PACKET_Struct packet)
        {
            var checkItems = LvwCheckData.ItemsSource as ObservableCollection<CheckDataItem>;
            if (checkItems == null)
            {
                checkItems = new ObservableCollection<CheckDataItem>();
                LvwCheckData.ItemsSource = checkItems;
            }

            checkItems.Clear();

            if (packet.MessageTYPE == 0) // STOF包
            {
                checkItems.Add(new CheckDataItem { Index = 0, Time = "", Data = "1394 Header" });
                checkItems.Add(new CheckDataItem { Index = 1, Time = "", Data = "1394 Header CRC" });
                checkItems.Add(new CheckDataItem { Index = 2, Time = "0x" + packet.STOFPayload0.ToString("X8"), Data = "STOF Payload 0" });
                checkItems.Add(new CheckDataItem { Index = 3, Time = "0x" + packet.STOFPayload1.ToString("X8"), Data = "STOF Payload 1" });
                checkItems.Add(new CheckDataItem { Index = 4, Time = "0x" + packet.STOFPayload2.ToString("X8"), Data = "STOF Payload 2" });
                checkItems.Add(new CheckDataItem { Index = 5, Time = "0x" + packet.STOFPayload3.ToString("X8"), Data = "STOF Payload 3" });
                checkItems.Add(new CheckDataItem { Index = 6, Time = "0x" + packet.STOFPayload4.ToString("X8"), Data = "STOF Payload 4" });
                checkItems.Add(new CheckDataItem { Index = 7, Time = "0x" + packet.STOFPayload5.ToString("X8"), Data = "STOF Payload 5" });
                checkItems.Add(new CheckDataItem { Index = 8, Time = "0x" + packet.STOFPayload6.ToString("X8"), Data = "STOF Payload 6" });
                checkItems.Add(new CheckDataItem { Index = 9, Time = "0x" + packet.STOFPayload7.ToString("X8"), Data = "STOF Payload 7" });
                checkItems.Add(new CheckDataItem { Index = 10, Time = "0x" + packet.STOFPayload8.ToString("X8"), Data = "STOF Payload 8" });
                checkItems.Add(new CheckDataItem { Index = 11, Time = "0x" + packet.STOFVPC.ToString("X8"), Data = "STOFVPC" });
                checkItems.Add(new CheckDataItem { Index = 12, Time = "", Data = "1394 Data CRC" });
            }
            else if (packet.MessageTYPE == 3) // BusReset消息
            {
                checkItems.Add(new CheckDataItem { Index = 0, Time = "0x" + packet.MessageTYPE.ToString("X8"), Data = "Type" });
                checkItems.Add(new CheckDataItem { Index = 1, Time = "0x" + packet.RTC.ToString("X8"), Data = "RTC" });
                checkItems.Add(new CheckDataItem { Index = 2, Time = "0x" + packet.MsgSpeed.ToString("X8"), Data = "speed" });
            }
            else // ASYNC异步消息
            {
                checkItems.Add(new CheckDataItem { Index = 0, Time = "", Data = "1394 Header" });
                checkItems.Add(new CheckDataItem { Index = 1, Time = "", Data = "1394 Header CRC" });
                checkItems.Add(new CheckDataItem { Index = 2, Time = "0x" + packet.MessageID.ToString("X8"), Data = "Message ID" });
                checkItems.Add(new CheckDataItem { Index = 3, Time = "0x" + packet.Security.ToString("X8"), Data = "Security" });
                checkItems.Add(new CheckDataItem { Index = 4, Time = "0x" + packet.NodeID.ToString("X8"), Data = "Node ID" });
                checkItems.Add(new CheckDataItem { Index = 5, Time = "0x" + packet.Priority.ToString("X2"), Data = "Priority" });
                checkItems.Add(new CheckDataItem { Index = 6, Time = "0x" + packet.PayloadDataLength.ToString("X6"), Data = "Payload Data Length" });
                checkItems.Add(new CheckDataItem { Index = 7, Time = "0x" + packet.HealthStatusWord.ToString("X8"), Data = "Health Status Word" });
                checkItems.Add(new CheckDataItem { Index = 8, Time = "0x" + packet.HeartBeatWord.ToString("X8"), Data = "Health Beat Word" });

                // 显示Payload数据
                int dataCount = 0;
                if (packet.MessageData != null)
                {
                    for (int i = 0; i < packet.MessageData.Length && (i * 4 + 8) < packet.PayloadDataLength && i < 0x1f4; i++)
                    {
                        checkItems.Add(new CheckDataItem 
                        { 
                            Index = 9 + i, 
                            Time = "0x" + packet.MessageData[i].ToString("X8"), 
                            Data = $"data[{i}]" 
                        });
                        dataCount = i + 1;
                    }
                }

                checkItems.Add(new CheckDataItem { Index = 9 + dataCount, Time = "0x" + packet.STOFTransmitOffset.ToString("X8"), Data = "STOF Transmit Offset" });
                dataCount++;
                checkItems.Add(new CheckDataItem { Index = 9 + dataCount, Time = "0x" + packet.STOFReceiveOffset.ToString("X8"), Data = "STOF Receive Offset" });
                dataCount++;
                checkItems.Add(new CheckDataItem { Index = 9 + dataCount, Time = "0x" + packet.STOFPHMOffset.ToString("X8"), Data = "STOF PHM Offset" });
                dataCount++;
                checkItems.Add(new CheckDataItem { Index = 9 + dataCount, Time = "0x" + packet.VPCASYNC.ToString("X8"), Data = "VPC" });
                dataCount++;
                checkItems.Add(new CheckDataItem { Index = 9 + dataCount, Time = "", Data = "1394 Data CRC" });
            }
        }

        /// <summary>
        /// 处理ScrollViewer的鼠标滚轮事件，确保可以滚动
        /// </summary>
        private void ScrollViewer_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            if (sender is System.Windows.Controls.ScrollViewer scrollViewer)
            {
                // 使用PageUp/PageDown实现更流畅的滚动
                if (e.Delta > 0)
                {
                    scrollViewer.PageUp();
                }
                else
                {
                    scrollViewer.PageDown();
                }
                e.Handled = true;
            }
        }
    }
}
