using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using MeasureControl.Drivers;
using MeasureControl.Drivers.PXI4004CAN;
using MeasureControl.Views.Dialogs;
using System.Windows;
using MeasureControl.Events;
using MeasureControl.Models;
using MeasureControl.Models.Devices;
using MeasureControl.Services;
using MeasureControl.Views;
using Prism.Commands;
using Prism.Events;
using Prism.Mvvm;

namespace MeasureControl.ViewModels
{
    /// <summary>
    /// PXI4004 CAN 卡配置面板 ViewModel
    /// 专注于 CAN 硬件连接功能
    /// </summary>
    public class PXI4004CANConfigPanelViewModel : BindableBase, IDisposable
    {
        #region 私有字段

        private DeviceBase _device;
        private string _chassisName;
        private string _cardModel;
        private string _cardName;
        private bool _isDeviceConnected;
        private string _connectionStatus;
        private PXI4004Driver _driver;


        private readonly IPxiChassisService _pxiChassisService;
        private readonly IEventAggregator _eventAggregator;
        // 跳过空帧日志节流：记录每个通道上次打印跳过空帧的时间
        private readonly System.Collections.Generic.Dictionary<int, DateTime> _lastSkipEmptyFrameLog = new System.Collections.Generic.Dictionary<int, DateTime>();
        private static readonly TimeSpan SkipEmptyFrameLogInterval = TimeSpan.FromSeconds(20);
        // 接收日志节流：记录每个通道上次打印接收摘要的时间，避免频繁在输出窗口打印大量调试信息
        private readonly System.Collections.Generic.Dictionary<int, DateTime> _lastReceiveLogTime = new System.Collections.Generic.Dictionary<int, DateTime>();
        // 每通道批量接收计数器（用于汇总统计并定期打印）
        private readonly System.Collections.Generic.Dictionary<int, int> _receiveBatchCounter = new System.Collections.Generic.Dictionary<int, int>();

        #endregion

        #region 属性

        /// <summary>
        /// 设备对象
        /// </summary>
        public DeviceBase Device
        {
            get => _device;
            set => SetProperty(ref _device, value);
        }

        /// <summary>
        /// 机箱名称
        /// </summary>
        public string ChassisName
        {
            get => _chassisName;
            set => SetProperty(ref _chassisName, value);
        }

        /// <summary>
        /// 板卡型号
        /// </summary>
        public string CardModel
        {
            get => _cardModel;
            set => SetProperty(ref _cardModel, value);
        }

        /// <summary>
        /// 板卡名称
        /// </summary>
        public string CardName
        {
            get => _cardName;
            set => SetProperty(ref _cardName, value);
        }

        /// <summary>
        /// 设备是否已连接
        /// </summary>
        public bool IsDeviceConnected
        {
            get => _isDeviceConnected;
            set => SetProperty(ref _isDeviceConnected, value);
        }

        /// <summary>
        /// 连接状态文本
        /// </summary>
        public string ConnectionStatus
        {
            get => _connectionStatus;
            set => SetProperty(ref _connectionStatus, value);
        }

        #endregion

        #region 命令

        public ICommand OpenDeviceCommand { get; }
        public ICommand CloseDeviceCommand { get; }
        public ICommand ToggleDeviceCommand { get; }
        // 通道操作命令
        public ICommand OpenChannelCommand { get; private set; }
        public ICommand CloseChannelCommand { get; private set; }
        public ICommand OpenAllChannelsCommand { get; private set; }
        public ICommand CloseAllChannelsCommand { get; private set; }
        // 操作按钮命令
        public ICommand StartCommand { get; private set; }
        public ICommand PauseCommand { get; private set; }
        public ICommand PreviousPageCommand { get; private set; }
        public ICommand NextPageCommand { get; private set; }
        public ICommand ClearListCommand { get; private set; }
        public ICommand OpenBusSettingsCommand { get; private set; }
        public ICommand StartReceiveCommand { get; private set; }
        public ICommand StopReceiveCommand { get; private set; }

        /// <summary>
        /// 启动按钮文字内容
        /// </summary>
        public string StartButtonText
        {
            get
            {
                if (!SelectedChannelIsStarted)
                {
                    // 如果通道从未启动过，显示"启动"
                    if (!SelectedChannelWasPaused)
                        return "启 动";
                    // 如果是从暂停状态恢复，显示"继续"
                    else
                        return "继 续";
                }
                else
                    // 已启动状态下，按钮不可用时仍显示"已启动"
                    return "已启动";
            }
        }


        /// <summary>
        /// 当前选中通道是否已启动
        /// </summary>
        public bool SelectedChannelIsStarted
        {
            get
            {
                if (SelectedChannelIndex >= 0 && SelectedChannelIndex < Channels.Count)
                    return Channels[SelectedChannelIndex].IsStarted;
                return false;
            }
        }

        /// <summary>
        /// 当前选中通道是否曾经被暂停过
        /// </summary>
        public bool SelectedChannelWasPaused
        {
            get
            {
                if (SelectedChannelIndex >= 0 && SelectedChannelIndex < Channels.Count)
                {
                    var ch = Channels[SelectedChannelIndex];
                    // 检查通道是否有暂停标记
                    return ch.GetType().GetProperty("WasPaused")?.GetValue(ch) as bool? ?? false;
                }
                return false;
            }
        }

        /// <summary>
        /// 底部任务区是否可用：当所选通道已启动并且已打开时可用
        /// </summary>
        public bool IsTaskAreaEnabled => SelectedChannelIsStarted && SelectedChannelIsOpen;

        // 接收统计已移除（按需接收）。

        #endregion

        #region 构造函数

        public PXI4004CANConfigPanelViewModel()
        {
            // 启用批量UI更新，每100ms批量更新一次，减少线程切换
            _batchUpdateTimer = new System.Threading.Timer(BatchUpdateUI, null, 100, 100);

            OpenDeviceCommand = new DelegateCommand(async () => await OnOpenDeviceAsync(), () => !IsDeviceConnected)
                .ObservesProperty(() => IsDeviceConnected);
            CloseDeviceCommand = new DelegateCommand(async () => await StopDebugAsync(), () => IsDeviceConnected)
                .ObservesProperty(() => IsDeviceConnected);
            ToggleDeviceCommand = new DelegateCommand(async () =>
            {
                if (!IsDeviceConnected)
                {
                    await OnOpenDeviceAsync();
                }
                else
                {
                    await StopDebugAsync();
                }
            });
            // 初始化通道命令（使用 object 参数以兼容 XAML CommandParameter 类型）
            OpenChannelCommand = new DelegateCommand<object>(async (param) =>
            {
                System.Diagnostics.Debug.WriteLine($"[ViewModel] ===== OpenChannelCommand 被调用 =====");
                System.Diagnostics.Debug.WriteLine($"[ViewModel] OpenChannelCommand 执行，参数: {param}, 类型: {param?.GetType()}");
                if (param == null)
                {
                    System.Diagnostics.Debug.WriteLine($"[ViewModel] OpenChannelCommand 参数为空");
                    return;
                }

                try
                {
                    int channelIndex = Convert.ToInt32(param);
                    System.Diagnostics.Debug.WriteLine($"[ViewModel] 转换后通道索引: {channelIndex}");
                    System.Diagnostics.Debug.WriteLine($"[ViewModel] IsDeviceConnected: {IsDeviceConnected}, _driver != null: {_driver != null}");

                    await OnOpenChannelAsync(channelIndex);
                    System.Diagnostics.Debug.WriteLine($"[ViewModel] ===== OpenChannelCommand 执行完成 =====");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[ViewModel] OpenChannelCommand 异常: {ex.Message}");
                    System.Diagnostics.Debug.WriteLine($"[ViewModel] 异常堆栈: {ex.StackTrace}");
                }
            });
            CloseChannelCommand = new DelegateCommand<object>(async (param) =>
            {
                if (param == null) return;
                int channelIndex = Convert.ToInt32(param);
                await OnCloseChannelAsync(channelIndex);
            });

            // 初始化批量通道操作命令
            OpenAllChannelsCommand = new DelegateCommand(async () => await OnOpenAllChannelsAsync(), () => IsDeviceConnected)
                .ObservesProperty(() => IsDeviceConnected);
            CloseAllChannelsCommand = new DelegateCommand(async () => await OnCloseAllChannelsAsync(), () => IsDeviceConnected)
                .ObservesProperty(() => IsDeviceConnected);

            // 初始化操作按钮命令
            StartCommand = new DelegateCommand(OnStart, () => IsDeviceConnected && SelectedChannelIsOpen && SelectedChannelHasSettingsApplied && !SelectedChannelIsStarted)
                .ObservesProperty(() => IsDeviceConnected)
                .ObservesProperty(() => SelectedChannelIsOpen)
                .ObservesProperty(() => SelectedChannelHasSettingsApplied)
                .ObservesProperty(() => SelectedChannelIsStarted);
            PauseCommand = new DelegateCommand(OnPause, () => IsDeviceConnected && SelectedChannelIsOpen && SelectedChannelHasSettingsApplied && SelectedChannelIsStarted)
                .ObservesProperty(() => IsDeviceConnected)
                .ObservesProperty(() => SelectedChannelIsOpen)
                .ObservesProperty(() => SelectedChannelHasSettingsApplied)
                .ObservesProperty(() => SelectedChannelIsStarted);
            ClearListCommand = new DelegateCommand(OnClearList, () => IsDeviceConnected && SelectedChannelIsOpen)
                .ObservesProperty(() => IsDeviceConnected)
                .ObservesProperty(() => SelectedChannelIsOpen);

            // 总线设置命令：仅在设备已连接、所选通道已打开、未启动且尚未应用设置时允许打开
            OpenBusSettingsCommand = new DelegateCommand(OnOpenBusSettings, () => IsDeviceConnected && SelectedChannelIsOpen && !SelectedChannelIsStarted && !SelectedChannelHasSettingsApplied)
                .ObservesProperty(() => IsDeviceConnected)
                .ObservesProperty(() => SelectedChannelIsOpen)
                .ObservesProperty(() => SelectedChannelIsStarted)
                .ObservesProperty(() => SelectedChannelHasSettingsApplied);

            ConnectionStatus = "离线";

            // 初始化通道列表（默认 20 个）
            InitializeChannels(20);
            // 初始化任务命令（添加/删除任务）与发送命令
            InitializeTaskCommands();
            InitializeMessageCommands();
        }

        

        /// <summary>
        /// 使用指定的设备初始化ViewModel
        /// </summary>
        public PXI4004CANConfigPanelViewModel(DeviceBase device, string chassisName,
            IPxiChassisService pxiChassisService = null, IEventAggregator eventAggregator = null) : this()
        {
            Device = device;
            ChassisName = chassisName;
            CardModel = device?.Model ?? "";
            CardName = !string.IsNullOrEmpty(device?.CardName) ? device.CardName : device?.Model ?? "";
            _pxiChassisService = pxiChassisService;
            _eventAggregator = eventAggregator;

            // 尝试恢复缓存的驱动
            TryRestoreCachedDriver();
        }

        #endregion

        #region 私有方法

        /// <summary>
        /// 尝试恢复缓存的驱动
        /// </summary>
        private void TryRestoreCachedDriver()
        {
            if (Device == null) return;

            var cachedDriver = DriverFactory.GetCachedDriver(Device.Id) as PXI4004Driver;
            if (cachedDriver != null && cachedDriver.IsConnected)
            {
                _driver = cachedDriver;
                IsDeviceConnected = true;
                ConnectionStatus = "在线";
            }
        }

        #region Channel 管理

        public class ChannelItem : BindableBase
        {
            private bool _isOpen;
            private bool _isSelected;
            private bool _hasBusSettingsApplied;
            private bool _isStarted;
            private bool _wasPaused;

            public int Index { get; set; }
            public string Name { get; set; }

            public bool IsOpen
            {
                get => _isOpen;
                set => SetProperty(ref _isOpen, value);
            }

            public bool IsSelected
            {
                get => _isSelected;
                set => SetProperty(ref _isSelected, value);
            }

            public bool HasBusSettingsApplied
            {
                get => _hasBusSettingsApplied;
                set => SetProperty(ref _hasBusSettingsApplied, value);
            }

            public bool IsStarted
            {
                get => _isStarted;
                set => SetProperty(ref _isStarted, value);
            }

            public bool WasPaused
            {
                get => _wasPaused;
                set => SetProperty(ref _wasPaused, value);
            }
            
            // 已应用到该通道的 CAN 参数（用于接收时的软件验收过滤判断）
            // Nullable to indicate "no applied param"
            public PXI4004.ARTCANX1_CAN_PARAM? AppliedParam { get; set; }
        }

        private System.Collections.ObjectModel.ObservableCollection<ChannelItem> _channels = new System.Collections.ObjectModel.ObservableCollection<ChannelItem>();
        public System.Collections.ObjectModel.ObservableCollection<ChannelItem> Channels => _channels;

        private int _selectedChannelIndex = -1;
        public int SelectedChannelIndex
        {
            get => _selectedChannelIndex;
            set
            {
                // 仅设置选中索引，不在此自动打开通道。
                // 通道的打开应通过右键菜单的“打开通道”操作由用户触发。
                SetSelectedChannelIndex(value);
            }
        }

        private void SetSelectedChannelIndex(int value)
        {
            if (SetProperty(ref _selectedChannelIndex, value))
            {
                // 重置UI更新节流计数器，允许新通道立即更新
                ResetUIUpdateThrottle();

                // 更新 Channels 的 IsSelected 标志
                for (int i = 0; i < Channels.Count; i++)
                {
                    Channels[i].IsSelected = (i == _selectedChannelIndex);
                }
                RaisePropertyChanged(nameof(SelectedChannelIsOpen));
                RaisePropertyChanged(nameof(SelectedChannelIsStarted));
                RaisePropertyChanged(nameof(SelectedChannelHasSettingsApplied));
                RaisePropertyChanged(nameof(SelectedChannelIsReceiving));
                RaisePropertyChanged(nameof(SelectedChannelWasPaused));
                RaisePropertyChanged(nameof(StartButtonText));

                // 更新显示的消息为选中通道的消息
                UpdateMessagesForSelectedChannel();
                // 更新显示的任务为选中通道的任务
                UpdateTasksForSelectedChannel();
                // 更新底部任务区是否可用
                RaisePropertyChanged(nameof(IsTaskAreaEnabled));
            }
        }

        public bool SelectedChannelIsOpen
        {
            get
            {
                if (SelectedChannelIndex >= 0 && SelectedChannelIndex < Channels.Count)
                    return Channels[SelectedChannelIndex].IsOpen;
                return false;
            }
        }

        public bool SelectedChannelHasSettingsApplied
        {
            get
            {
                if (SelectedChannelIndex >= 0 && SelectedChannelIndex < Channels.Count)
                    return Channels[SelectedChannelIndex].HasBusSettingsApplied;
                return false;
            }
        }

        /// <summary>
        /// 当前选中通道是否正在接收数据
        /// </summary>
        public bool SelectedChannelIsReceiving
        {
            get
            {
                lock (_receiveLoopLock)
                {
                    return _receiveLoopCts.ContainsKey(SelectedChannelIndex) && !_receiveLoopCts[SelectedChannelIndex].IsCancellationRequested;
                }
            }
        }

        private void InitializeChannels(int count = 20)
        {
            Channels.Clear();
            for (int i = 0; i < count; i++)
            {
                var ch = new ChannelItem { Index = i, Name = $"CAN{i}", IsOpen = false, IsSelected = false, HasBusSettingsApplied = false, IsStarted = false, WasPaused = false };
                ch.PropertyChanged += Channel_PropertyChanged;
                Channels.Add(ch);
            }
        }

        private void Channel_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ChannelItem.IsSelected))
            {
                var ch = sender as ChannelItem;
                if (ch != null && ch.IsSelected)
                {
                    SelectedChannelIndex = ch.Index;
                }
            }
            else if (e.PropertyName == nameof(ChannelItem.IsOpen))
            {
                // 如果当前所选通道状态改变，也触发 SelectedChannelIsOpen 更新通知
                RaisePropertyChanged(nameof(SelectedChannelIsOpen));

                // 如果当前选中的通道打开或关闭状态改变，更新消息显示
                var ch = sender as ChannelItem;
                if (ch != null && ch.Index == SelectedChannelIndex)
                {
                    UpdateMessagesForSelectedChannel();
                }
                // 更新底部任务区是否可用
                RaisePropertyChanged(nameof(IsTaskAreaEnabled));
            }
            else if (e.PropertyName == nameof(ChannelItem.IsStarted))
            {
                // 当某个通道的启动状态改变，触发相关属性更新
                var ch = sender as ChannelItem;
                if (ch != null && ch.Index == SelectedChannelIndex)
                {
                    // 如果是当前选中通道，更新按钮状态和任务区状态
                    RaisePropertyChanged(nameof(SelectedChannelIsStarted));
                    RaisePropertyChanged(nameof(StartButtonText));
                    RaisePropertyChanged(nameof(IsTaskAreaEnabled));
                }
            }
            else if (e.PropertyName == nameof(ChannelItem.HasBusSettingsApplied))
            {
                // 如果当前所选通道的设置应用状态改变，触发 SelectedChannelHasSettingsApplied 更新通知
                RaisePropertyChanged(nameof(SelectedChannelHasSettingsApplied));
            }
            else if (e.PropertyName == nameof(ChannelItem.WasPaused))
            {
                // 如果当前所选通道的暂停状态改变，触发 SelectedChannelWasPaused 更新通知
                var ch = sender as ChannelItem;
                if (ch != null && ch.Index == SelectedChannelIndex)
                {
                    RaisePropertyChanged(nameof(SelectedChannelWasPaused));
                    RaisePropertyChanged(nameof(StartButtonText));
                }
            }
        }

        #endregion

        /// <summary>
        /// 重置所有通道状态（用于板卡连接时）
        /// </summary>
        private void ResetAllChannelsState()
        {
            foreach (var channel in Channels)
            {
                channel.IsOpen = false;
                channel.IsStarted = false;
                channel.WasPaused = false;
                channel.HasBusSettingsApplied = false;
                channel.AppliedParam = null;
            }
            System.Diagnostics.Debug.WriteLine($"[ViewModel] 已重置所有 {Channels.Count} 个通道的状态");
        }

        /// <summary>
        /// 批量更新UI，将缓冲区的消息添加到消息列表
        /// </summary>
        private void BatchUpdateUI(object state)
        {
            var now = DateTime.UtcNow;

            // UI更新节流：检查是否超过更新频率限制
            if ((now - _lastUIUpdateTime).TotalMilliseconds < MIN_UI_UPDATE_INTERVAL_MS)
            {
                // 频率太高，跳过这次更新，但继续累积缓冲区
                return;
            }

            // 只处理当前选中通道的缓冲区，避免不必要的UI更新
            if (SelectedChannelIndex >= 0 && _uiUpdateBuffer.TryGetValue(SelectedChannelIndex, out var buffer))
            {
                List<MessageItem> messagesToProcess = null;

                // 线程安全地获取并清空缓冲区
                lock (buffer)
                {
                    if (buffer.Count > 0)
                    {
                        messagesToProcess = new List<MessageItem>(buffer);
                        buffer.Clear();
                    }
                }

                if (messagesToProcess != null && messagesToProcess.Count > 0)
                {
                    // 更新节流计数器
                    _lastUIUpdateTime = now;
                    _uiUpdateCount++;

                    // 在UI线程中批量更新消息列表，使用BeginInvoke避免阻塞
                    System.Windows.Application.Current.Dispatcher.BeginInvoke(
                        new Action(() =>
                        {
                            BatchUpdateMessages(messagesToProcess);
                        }),
                        System.Windows.Threading.DispatcherPriority.Background);
                }
            }
        }

        /// <summary>
        /// 批量更新UI消息列表
        /// </summary>
        private void BatchUpdateMessages(List<MessageItem> messages)
        {
            // 限制UI显示的消息数量
            const int MAX_UI_MESSAGES = 500;

            // 计算需要添加的消息数量
            int messagesToAdd = messages.Count;
            int currentCount = _messages.Count;

            // 如果添加后会超过限制，需要先移除一些旧消息
            if (currentCount + messagesToAdd > MAX_UI_MESSAGES)
            {
                int removeCount = Math.Min(currentCount, (currentCount + messagesToAdd) - MAX_UI_MESSAGES);
                for (int i = 0; i < removeCount; i++)
                {
                    _messages.RemoveAt(0);
                }

                // 重新编号剩余消息
                for (int i = 0; i < _messages.Count; i++)
                {
                    _messages[i].Index = i + 1;
                }
            }

            // 批量添加新消息
            foreach (var message in messages)
            {
                message.Index = _messages.Count + 1;
                _messages.Add(message);
            }
        }

        #region 消息管理

        public class MessageItem : BindableBase
        {
            public int Index { get; set; }
            public string Time { get; set; }
            public string Direction { get; set; }
            public string Id { get; set; }
            public string FrameType { get; set; }
            public string FrameFormat { get; set; }
            public string Length { get; set; }
            public string Data { get; set; }
            public string SendStatus { get; set; }
        }

        // 为每个通道维护消息列表
        private System.Collections.Generic.Dictionary<int, System.Collections.ObjectModel.ObservableCollection<MessageItem>> _channelMessages =
            new System.Collections.Generic.Dictionary<int, System.Collections.ObjectModel.ObservableCollection<MessageItem>>();

        // 当前显示的消息（选中通道的消息）
        private System.Collections.ObjectModel.ObservableCollection<MessageItem> _messages =
            new System.Collections.ObjectModel.ObservableCollection<MessageItem>();
        public System.Collections.ObjectModel.ObservableCollection<MessageItem> Messages => _messages;

        // 为每个通道维护任务列表
        private System.Collections.Generic.Dictionary<int, System.Collections.ObjectModel.ObservableCollection<SendTaskItem>> _channelTasks =
            new System.Collections.Generic.Dictionary<int, System.Collections.ObjectModel.ObservableCollection<SendTaskItem>>();

        // 当前显示的任务（选中通道的任务）
        private System.Collections.ObjectModel.ObservableCollection<SendTaskItem> _taskList =
            new System.Collections.ObjectModel.ObservableCollection<SendTaskItem>();
        public System.Collections.ObjectModel.ObservableCollection<SendTaskItem> TaskList => _taskList;

        // 消息索引计数器（使用 Interlocked 增量以保证线程安全）
        private int _messageIndexCounter = 0;

        // 发送循环控制
        private System.Threading.CancellationTokenSource _sendLoopCts;
        private System.Threading.CancellationTokenSource _sendTaskCts;
        private readonly object _sendLoopLock = new object();
        private readonly object _sendTaskLock = new object();
        // 接收循环控制 已移除，改为按需接收



        /// <summary>
        /// 获取指定通道的消息列表，如果不存在则创建
        /// </summary>
        private System.Collections.ObjectModel.ObservableCollection<MessageItem> GetChannelMessages(int channelIndex)
        {
            if (!_channelMessages.ContainsKey(channelIndex))
            {
                _channelMessages[channelIndex] = new System.Collections.ObjectModel.ObservableCollection<MessageItem>();
            }
            return _channelMessages[channelIndex];
        }

        /// <summary>
        /// 获取指定通道的任务列表，如果不存在则创建
        /// </summary>
        private System.Collections.ObjectModel.ObservableCollection<SendTaskItem> GetChannelTasks(int channelIndex)
        {
            if (!_channelTasks.ContainsKey(channelIndex))
            {
                _channelTasks[channelIndex] = new System.Collections.ObjectModel.ObservableCollection<SendTaskItem>();
            }
            return _channelTasks[channelIndex];
        }

        // 确保接收循环正在运行；如果没有则启动（接收循环相关方法已移除，改为按需接收，使用驱动的 ReceiveFrameAsync 在需要时调用）。

        /// <summary>
        /// 向指定通道添加消息
        /// </summary>
        public void AddMessageToChannel(int channelIndex, MessageItem message)
        {
            var channelMessages = GetChannelMessages(channelIndex);

            // 限制每个通道最多保留1000条消息，避免内存溢出
            const int MAX_MESSAGES_PER_CHANNEL = 1000;
            if (channelMessages.Count >= MAX_MESSAGES_PER_CHANNEL)
            {
                // 移除最旧的消息
                channelMessages.RemoveAt(0);
                // 重新编号
                for (int i = 0; i < channelMessages.Count; i++)
                {
                    channelMessages[i].Index = i + 1;
                }
            }

            message.Index = channelMessages.Count + 1;
            channelMessages.Add(message);

            // 使用批量UI更新缓冲区，避免频繁线程切换
            if (channelIndex == SelectedChannelIndex)
            {
                // 获取或创建该通道的缓冲区
                var buffer = _uiUpdateBuffer.GetOrAdd(channelIndex, _ => new System.Collections.Generic.List<MessageItem>());

                // 线程安全地添加到缓冲区
                lock (buffer)
                {
                    buffer.Add(message);
                }
            }
        }

        /// <summary>
        /// 清空指定通道的消息
        /// </summary>
        public void ClearChannelMessages(int channelIndex)
        {
            if (_channelMessages.ContainsKey(channelIndex))
            {
                _channelMessages[channelIndex].Clear();
            }

            // 如果是当前选中的通道，清空显示
            if (channelIndex == SelectedChannelIndex)
            {
                _messages.Clear();
            }
        }

        #endregion

        #region 任务列表管理

        public class SendTaskItem : BindableBase
        {
            public string SendFormat { get; set; }
            public string FrameType { get; set; }
            public string FrameFormat { get; set; }
            public string IdMode { get; set; }
            public string Id { get; set; }
            public string DataMode { get; set; }
            public string Data { get; set; }
            public int Interval { get; set; }
            public int Count { get; set; }
            public bool Loop { get; set; }
            public uint? BaudRate { get; set; }
        }


        private SendTaskItem _selectedTask;
        public SendTaskItem SelectedTask
        {
            get => _selectedTask;
            set
            {
                SetProperty(ref _selectedTask, value);
            }
        }

        // Exposed property for command CanExecute to observe task count changes
        public int CurrentChannelTaskCount => GetCurrentChannelTaskCount();

        // New task definition backing properties (bound to left-side inputs)
        private string _newTask_FrameType = "数据帧";
        private string _newTask_FrameFormat = "标准帧";
        private string _newTask_IdMode = "固定";
        private string _newTask_Id = "1";
        private string _newTask_DataMode = "固定";
        private string _newTask_Data = "01 02 03 04 05 06 07 08";
        private int _newTask_Interval = 1000;
        private int _newTask_Count = 1;
        private bool _newTask_Loop = false;
        private int _sendFrameCount = 0;
        private int _receiveFrameCount = 0;
        private long _sendTimeElapsed = 0;

        /// <summary>
        /// UI更新缓冲区，用于批量更新以减少UI卡顿
        /// Key: 通道索引, Value: 该通道的待处理消息列表
        /// </summary>
        private System.Collections.Concurrent.ConcurrentDictionary<int, System.Collections.Generic.List<MessageItem>> _uiUpdateBuffer = new System.Collections.Concurrent.ConcurrentDictionary<int, System.Collections.Generic.List<MessageItem>>();

        /// <summary>
        /// UI批量更新定时器
        /// </summary>
        private System.Threading.Timer _batchUpdateTimer;

        /// <summary>
        /// UI更新节流控制
        /// </summary>
        private DateTime _lastUIUpdateTime = DateTime.MinValue;
        private int _uiUpdateCount = 0;
        private const int MAX_UI_UPDATES_PER_SECOND = 10; // 每秒最多更新10次
        private const double MIN_UI_UPDATE_INTERVAL_MS = 1000.0 / MAX_UI_UPDATES_PER_SECOND;

        /// <summary>
        /// 重置UI更新节流计数器
        /// </summary>
        private void ResetUIUpdateThrottle()
        {
            _lastUIUpdateTime = DateTime.MinValue;
            _uiUpdateCount = 0;
        }

        public string NewTask_FrameType
        {
            get => _newTask_FrameType;
            set
            {
                SetProperty(ref _newTask_FrameType, value);
            }
        }
        public string NewTask_FrameFormat
        {
            get => _newTask_FrameFormat;
            set
            {
                SetProperty(ref _newTask_FrameFormat, value);
            }
        }
        public string NewTask_IdMode
        {
            get => _newTask_IdMode;
            set
            {
                SetProperty(ref _newTask_IdMode, value);
            }
        }
        public string NewTask_Id
        {
            get => _newTask_Id;
            set
            {
                SetProperty(ref _newTask_Id, value);
            }
        }
        public string NewTask_DataMode
        {
            get => _newTask_DataMode;
            set
            {
                SetProperty(ref _newTask_DataMode, value);
            }
        }
        public string NewTask_Data
        {
            get => _newTask_Data;
            set
            {
                SetProperty(ref _newTask_Data, value);
            }
        }
        public int NewTask_Interval
        {
            get => _newTask_Interval;
            set
            {
                SetProperty(ref _newTask_Interval, value);
            }
        }
        public int NewTask_Count
        {
            get => _newTask_Count;
            set
            {
                SetProperty(ref _newTask_Count, value);
            }
        }
        public bool NewTask_Loop
        {
            get => _newTask_Loop;
            set
            {
                SetProperty(ref _newTask_Loop, value);
            }
        }

        public int SendFrameCount
        {
            get => _sendFrameCount;
            set
            {
                SetProperty(ref _sendFrameCount, value);
            }
        }

        public int ReceiveFrameCount
        {
            get => _receiveFrameCount;
            set
            {
                SetProperty(ref _receiveFrameCount, value);
            }
        }

        public long SendTimeElapsed
        {
            get => _sendTimeElapsed;
            set
            {
                SetProperty(ref _sendTimeElapsed, value);
            }
        }

        // Command to add task
        public ICommand AddTaskCommand { get; private set; }
        public ICommand DeleteTaskCommand { get; private set; }
        public ICommand ClearTaskListCommand { get; private set; }
        public ICommand ClearSendFrameCountCommand { get; private set; }
        public ICommand ClearReceiveFrameCountCommand { get; private set; }
        public ICommand ClearSendTimeElapsedCommand { get; private set; }
        public ICommand SendCurrentTaskCommand { get; private set; }
        public ICommand SendAllTasksCommand { get; private set; }

        private void InitializeTaskCommands()
        {
            AddTaskCommand = new DelegateCommand(AddTask);
            DeleteTaskCommand = new DelegateCommand(DeleteSelectedTask, () => SelectedTask != null)
                .ObservesProperty(() => SelectedTask);
            ClearTaskListCommand = new DelegateCommand(ClearTaskList, () => IsDeviceConnected && SelectedChannelIsOpen && GetCurrentChannelTaskCount() > 0)
                .ObservesProperty(() => IsDeviceConnected)
                .ObservesProperty(() => SelectedChannelIsOpen);
            ClearSendFrameCountCommand = new DelegateCommand(() => SendFrameCount = 0);
            ClearReceiveFrameCountCommand = new DelegateCommand(() => ReceiveFrameCount = 0);
            ClearSendTimeElapsedCommand = new DelegateCommand(() => SendTimeElapsed = 0);
        }

        private void AddTask()
        {
            try
            {
                if (SelectedChannelIndex < 0 || SelectedChannelIndex >= Channels.Count)
                {
                    ReMessageBox.Show("请先选择一个通道", "提示", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    return;
                }

                var task = new SendTaskItem
                {
                    SendFormat = "默认",
                    FrameType = NewTask_FrameType,
                    FrameFormat = NewTask_FrameFormat,
                    IdMode = NewTask_IdMode,
                    Id = NewTask_Id,
                    DataMode = NewTask_DataMode,
                    Data = NewTask_Data,
                    Interval = NewTask_Interval,
                    Count = NewTask_Count,
                    Loop = NewTask_Loop,
                    BaudRate = null
                };

                // If selected channel has applied param, include baudrate
                if (SelectedChannelIndex >= 0 && SelectedChannelIndex < Channels.Count)
                {
                    var ch = Channels[SelectedChannelIndex];
                    // try to get applied param if exists (ChannelItem may expose AppliedParam)
                    var applied = typeof(ChannelItem).GetProperty("AppliedParam")?.GetValue(ch);
                    if (applied != null)
                    {
                        try
                        {
                            var baudProp = applied.GetType().GetField("nBaudRate");
                            if (baudProp != null)
                            {
                                object v = baudProp.GetValue(applied);
                                if (v is uint u) task.BaudRate = u;
                            }
                        }
                        catch { }
                    }
                }

                // 将任务添加到选中通道的任务列表
                var channelTasks = GetChannelTasks(SelectedChannelIndex);
                channelTasks.Add(task);

                // 更新UI显示
                UpdateTasksForSelectedChannel();

                // 通知命令 CanExecute 状态更新（任务数已变更）
                RaisePropertyChanged(nameof(CurrentChannelTaskCount));

                // Auto-select the newly added task
                SelectedTask = task;

                System.Diagnostics.Debug.WriteLine($"[ViewModel] 任务已添加到通道 {SelectedChannelIndex}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ViewModel] 添加任务失败: {ex.Message}");
            }
        }

        private void DeleteSelectedTask()
        {
            try
            {
                if (SelectedTask != null && SelectedChannelIndex >= 0 && SelectedChannelIndex < Channels.Count)
                {
                    var channelTasks = GetChannelTasks(SelectedChannelIndex);
                    if (channelTasks.Contains(SelectedTask))
                    {
                        channelTasks.Remove(SelectedTask);
                        // 更新UI显示
                        UpdateTasksForSelectedChannel();
                        // 更新命令 CanExecute
                        RaisePropertyChanged(nameof(CurrentChannelTaskCount));
                        SelectedTask = null;
                        System.Diagnostics.Debug.WriteLine($"[ViewModel] 任务已从通道 {SelectedChannelIndex} 删除");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ViewModel] 删除任务失败: {ex.Message}");
            }
        }

        private void ClearTaskList()
        {
            try
            {
                if (SelectedChannelIndex >= 0 && SelectedChannelIndex < Channels.Count)
                {
                    ClearChannelTasks(SelectedChannelIndex);
                    SelectedTask = null;
                    System.Diagnostics.Debug.WriteLine($"[ViewModel] 通道 {SelectedChannelIndex} 的任务列表已清空");
                    ReMessageBox.Show($"已清空通道 {SelectedChannelIndex} 的任务列表", "成功", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                    // 更新命令 CanExecute
                    RaisePropertyChanged(nameof(CurrentChannelTaskCount));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ViewModel] 清空任务列表失败: {ex.Message}");
                ReMessageBox.Show($"清空任务列表失败: {ex.Message}", "错误", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private void SendCurrentTask()
        {
            if (SelectedTask == null || !SelectedChannelIsOpen)
                return;

            // Add send message record
            var message = new MessageItem
            {
                Time = DateTime.Now.ToString("HH:mm:ss.fff"),
                Direction = "发送",
                Id = SelectedTask.Id,
                FrameType = SelectedTask.FrameType,
                FrameFormat = SelectedTask.FrameFormat,
                Length = (SelectedTask.Data.Length / 3 + 1).ToString(), // Estimate length
                Data = SelectedTask.Data,
                SendStatus = "成功"
            };
            AddMessageToChannel(SelectedChannelIndex, message);

            System.Diagnostics.Debug.WriteLine($"Send current task: {SelectedTask.Id} to channel {SelectedChannelIndex}");
        }

        private void SendAllTasks()
        {
            if (GetCurrentChannelTaskCount() == 0 || !SelectedChannelIsOpen)
                return;

            foreach (var task in GetChannelTasks(SelectedChannelIndex))
            {
                // Add send message record
                var message = new MessageItem
                {
                    Time = DateTime.Now.ToString("HH:mm:ss.fff"),
                    Direction = "发送",
                    Id = task.Id,
                    FrameType = task.FrameType,
                    FrameFormat = task.FrameFormat,
                    Length = (task.Data.Length / 3 + 1).ToString(), // Estimate length
                    Data = task.Data,
                    SendStatus = "成功"
                };
                AddMessageToChannel(SelectedChannelIndex, message);
            }

            System.Diagnostics.Debug.WriteLine($"Send all tasks to channel {SelectedChannelIndex}");
        }

        #endregion

        #region 消息日志与发送任务

        /// <summary>
        /// 更新当前显示的消息为选中通道的消息
        /// </summary>
        private void UpdateMessagesForSelectedChannel()
        {
            _messages.Clear();
            if (SelectedChannelIndex >= 0 && SelectedChannelIndex < Channels.Count && Channels[SelectedChannelIndex].IsOpen)
            {
                var channelMessages = GetChannelMessages(SelectedChannelIndex);
                foreach (var message in channelMessages)
                {
                    _messages.Add(message);
                }
            }

            // 处理缓冲区中可能存在的待更新消息
            if (_uiUpdateBuffer.TryGetValue(SelectedChannelIndex, out var buffer))
            {
                List<MessageItem> pendingMessages = null;
                lock (buffer)
                {
                    if (buffer.Count > 0)
                    {
                        pendingMessages = new List<MessageItem>(buffer);
                        buffer.Clear();
                    }
                }

                if (pendingMessages != null && pendingMessages.Count > 0)
                {
                    BatchUpdateMessages(pendingMessages);
                }
            }
        }

        /// <summary>
        /// 更新当前显示的任务为选中通道的任务
        /// </summary>
        private void UpdateTasksForSelectedChannel()
        {
            _taskList.Clear();
            if (SelectedChannelIndex >= 0 && SelectedChannelIndex < Channels.Count)
            {
                var channelTasks = GetChannelTasks(SelectedChannelIndex);
                foreach (var task in channelTasks)
                {
                    _taskList.Add(task);
                }
            }
            // notify commands that depend on task count
            RaisePropertyChanged(nameof(CurrentChannelTaskCount));
        }

        /// <summary>
        /// 获取当前选中通道的任务数量
        /// </summary>
        private int GetCurrentChannelTaskCount()
        {
            if (SelectedChannelIndex >= 0 && SelectedChannelIndex < Channels.Count)
            {
                return GetChannelTasks(SelectedChannelIndex).Count;
            }
            return 0;
        }

        /// <summary>
        /// 清空指定通道的任务列表
        /// </summary>
        public void ClearChannelTasks(int channelIndex)
        {
            if (_channelTasks.ContainsKey(channelIndex))
            {
                _channelTasks[channelIndex].Clear();
            }

            // 如果是当前选中的通道，清空显示
            if (channelIndex == SelectedChannelIndex)
            {
                _taskList.Clear();
            }
        }

        private void InitializeMessageCommands()
        {
            SendCurrentTaskCommand = new DelegateCommand(async () => await OnSendCurrentTaskAsync(), () => IsDeviceConnected && SelectedChannelIsOpen && SelectedChannelIsStarted && SelectedChannelHasSettingsApplied && SelectedTask != null)
                .ObservesProperty(() => IsDeviceConnected)
                .ObservesProperty(() => SelectedChannelIsOpen)
                .ObservesProperty(() => SelectedChannelIsStarted)
                .ObservesProperty(() => SelectedChannelHasSettingsApplied)
                .ObservesProperty(() => SelectedTask);
            SendAllTasksCommand = new DelegateCommand(async () => await OnSendAllTasksAsync(), () => IsDeviceConnected && SelectedChannelIsOpen && SelectedChannelIsStarted && SelectedChannelHasSettingsApplied && CurrentChannelTaskCount > 0)
                .ObservesProperty(() => IsDeviceConnected)
                .ObservesProperty(() => SelectedChannelIsOpen)
                .ObservesProperty(() => SelectedChannelIsStarted)
                .ObservesProperty(() => SelectedChannelHasSettingsApplied)
                .ObservesProperty(() => CurrentChannelTaskCount);
            StartReceiveCommand = new DelegateCommand(async () => await OnStartReceiveAsync(), () => IsDeviceConnected && SelectedChannelIsOpen && SelectedChannelHasSettingsApplied && SelectedChannelIsStarted && !SelectedChannelIsReceiving)
                .ObservesProperty(() => IsDeviceConnected)
                .ObservesProperty(() => SelectedChannelIsOpen)
                .ObservesProperty(() => SelectedChannelHasSettingsApplied)
                .ObservesProperty(() => SelectedChannelIsStarted)
                .ObservesProperty(() => SelectedChannelIsReceiving);
            StopReceiveCommand = new DelegateCommand(async () => await OnStopReceiveAsync(), () => IsDeviceConnected && SelectedChannelIsOpen && SelectedChannelIsReceiving)
                .ObservesProperty(() => IsDeviceConnected)
                .ObservesProperty(() => SelectedChannelIsOpen)
                .ObservesProperty(() => SelectedChannelIsReceiving);
        }

        private async System.Threading.Tasks.Task OnSendCurrentTaskAsync()
        {
            try
            {
                if (!IsDeviceConnected || _driver == null)
                {
                    ReMessageBox.Show("请先连接板卡", "提示", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    return;
                }

                if (SelectedChannelIndex < 0 || SelectedChannelIndex >= Channels.Count || !Channels[SelectedChannelIndex].IsOpen)
                {
                    ReMessageBox.Show("请先选择并打开一个通道", "提示", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    return;
                }

                if (!SelectedChannelHasSettingsApplied)
                {
                    ReMessageBox.Show("请先应用总线设置", "提示", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    return;
                }

                // Use the selected task
                var task = SelectedTask;
                if (task == null)
                {
                    ReMessageBox.Show("请先选择一个任务", "提示", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    return;
                }

                // Manual send respects Count and Loop flags - 使用独立的取消控制
                lock (_sendTaskLock)
                {
                    if (_sendTaskCts != null)
                    {
                        try { _sendTaskCts.Cancel(); } catch { }
                        try { _sendTaskCts.Dispose(); } catch { }
                    }
                    _sendTaskCts = new System.Threading.CancellationTokenSource();
                }
                var ct = _sendTaskCts.Token;
                // 捕获启动发送时的通道索引，确保循环发送任务不会随着用户切换选中通道而改变目标通道
                int manualChannelIndex = SelectedChannelIndex;

                if (task.Loop)
                {
                    // send continuously until cancelled, always use captured manualChannelIndex
                    while (!ct.IsCancellationRequested)
                    {
                        await SendTaskOnceAsync(task, manualChannelIndex);
                        try { await System.Threading.Tasks.Task.Delay(Math.Max(1, task.Interval), ct); } catch { break; }
                    }
                }
                else
                {
                    int times = Math.Max(1, task.Count);
                    for (int i = 0; i < times && !ct.IsCancellationRequested; i++)
                    {
                        await SendTaskOnceAsync(task, manualChannelIndex);
                        try { await System.Threading.Tasks.Task.Delay(Math.Max(1, task.Interval), ct); } catch { break; }
                    }
                }

                lock (_sendTaskLock)
                {
                    try { _sendTaskCts?.Dispose(); } catch { }
                    _sendTaskCts = null;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ViewModel] 发送当前任务失败: {ex.Message}");
            }
        }

        private async System.Threading.Tasks.Task OnSendAllTasksAsync()
        {
            try
            {
                if (!IsDeviceConnected || _driver == null)
                {
                    ReMessageBox.Show("请先连接板卡", "提示", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    return;
                }

                if (SelectedChannelIndex < 0 || SelectedChannelIndex >= Channels.Count || !Channels[SelectedChannelIndex].IsOpen)
                {
                    ReMessageBox.Show("请先选择并打开一个通道", "提示", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    return;
                }

                if (!SelectedChannelHasSettingsApplied)
                {
                    ReMessageBox.Show("请先应用总线设置", "提示", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    return;
                }

                var tasksToSend = GetChannelTasks(SelectedChannelIndex).ToList();
                if (tasksToSend.Count == 0)
                {
                    ReMessageBox.Show("任务列表为空", "提示", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    return;
                }

                // Manual send-all: if any task has Loop==true, loop entire list until cancelled;
                // otherwise send each task Count times. - 使用独立的取消控制
                lock (_sendTaskLock)
                {
                    if (_sendTaskCts != null)
                    {
                        try { _sendTaskCts.Cancel(); } catch { }
                        try { _sendTaskCts.Dispose(); } catch { }
                    }
                    _sendTaskCts = new System.Threading.CancellationTokenSource();
                }
                var ct = _sendTaskCts.Token;
                // 捕获启动发送时的通道索引，确保 send-all 操作中的循环任务不会随 UI 切换通道而改变目标通道
                int manualAllChannelIndex = SelectedChannelIndex;

                bool anyLoop = tasksToSend.Any(t => t.Loop);
                if (anyLoop)
                {
                    // loop entire list until cancelled
                    while (!ct.IsCancellationRequested)
                    {
                        foreach (var task in tasksToSend)
                        {
                            if (ct.IsCancellationRequested) break;
                            await SendTaskOnceAsync(task, manualAllChannelIndex);
                            try { await System.Threading.Tasks.Task.Delay(Math.Max(1, task.Interval), ct); } catch { break; }
                        }
                    }
                }
                else
                {
                    foreach (var task in tasksToSend)
                    {
                        int times = Math.Max(1, task.Count);
                        for (int i = 0; i < times && !ct.IsCancellationRequested; i++)
                        {
                            await SendTaskOnceAsync(task, manualAllChannelIndex);
                            try { await System.Threading.Tasks.Task.Delay(Math.Max(1, task.Interval), ct); } catch { break; }
                        }
                    }
                }

                lock (_sendTaskLock)
                {
                    try { _sendTaskCts?.Dispose(); } catch { }
                    _sendTaskCts = null;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ViewModel] 发送任务列表失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 发送单个任务一次（构建帧、发送、记录消息）
        /// </summary>
        private async System.Threading.Tasks.Task SendTaskOnceAsync(SendTaskItem task, int channelIndex)
        {
            if (task == null) return;
            try
            {
                // 使用Driver的统一发送方法
                var taskParams = new MeasureControl.Drivers.PXI4004Driver.SendTaskParams
                {
                    Id = task.Id,
                    FrameType = task.FrameType,
                    FrameFormat = task.FrameFormat,
                    Data = task.Data
                };

                var result = await _driver.SendTaskAsync(channelIndex, taskParams, 0.2);

                // 如果发送成功，累加耗时
                if (result.Success)
                {
                    SendTimeElapsed += result.ElapsedMs;
                }

                // 使用结果中的帧信息
                var frame = result.Frame;

                var msg = new MessageItem
                {
                    Index = System.Threading.Interlocked.Increment(ref _messageIndexCounter),
                    Time = DateTime.Now.ToString("HH:mm:ss.fff"),
                    Direction = "发送",
                    Id = $"0x{frame.nFrameID:X}",
                    FrameType = (frame.nFrameType == 0) ? "数据帧" : "远程帧",
                    FrameFormat = (frame.bExtendedID == 0) ? "标准帧" : "扩展帧",
                    Length = frame.nDataLength.ToString(),
                    Data = string.Join(" ", frame.DataBuf.Take(frame.nDataLength).Select(b => b.ToString("X2"))),
                    SendStatus = result.Success ? "成功" : "Error"
                };

                // 如果发送成功，增加发送帧数统计
                if (result.Success)
                {
                    SendFrameCount++;
                }

                // 使用AddMessageToChannel方法来确保UI正确更新
                AddMessageToChannel(channelIndex, msg);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ViewModel] SendTaskOnceAsync 异常: {ex.Message}");
            }
            await System.Threading.Tasks.Task.Yield();
        }

        #endregion

        /// <summary>
        /// 打开单个通道（命令处理）
        /// </summary>
        private async Task OnOpenChannelAsync(int channelIndex)
        {
            System.Diagnostics.Debug.WriteLine($"[ViewModel] ===== OnOpenChannelAsync 开始执行，通道: {channelIndex} =====");
            if (!IsDeviceConnected || _driver == null)
            {
                System.Diagnostics.Debug.WriteLine($"[ViewModel] 设备未连接或驱动为空: IsDeviceConnected={IsDeviceConnected}, _driver==null={_driver == null}");
                return;
            }

            System.Diagnostics.Debug.WriteLine($"[ViewModel] 调用驱动的OpenChannelAsync...");
            try
            {
                bool ok = await _driver.OpenChannelAsync(channelIndex);
                System.Diagnostics.Debug.WriteLine($"[ViewModel] 驱动返回结果: {ok}");

                if (ok)
                {
                    System.Diagnostics.Debug.WriteLine($"[ViewModel] 设置UI状态，通道 {channelIndex} 设为打开");
                    if (channelIndex >= 0 && channelIndex < Channels.Count)
                    {
                        var channel = Channels[channelIndex];

                        // 打开通道时完全重置通道状态
                        channel.IsOpen = true;
                        channel.IsStarted = false;
                        channel.WasPaused = false;
                        channel.HasBusSettingsApplied = false;
                        channel.AppliedParam = null;  // 清除之前应用的设置

                        System.Diagnostics.Debug.WriteLine($"[ViewModel] 通道 {channelIndex} 状态已重置：IsOpen=true, IsStarted=false, HasBusSettingsApplied=false");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"[ViewModel] 通道索引超出范围: {channelIndex}, 总数: {Channels.Count}");
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[ViewModel] 驱动报告打开通道失败");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ViewModel] 打开通道异常: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[ViewModel] 异常详情: {ex.StackTrace}");
            }
            System.Diagnostics.Debug.WriteLine($"[ViewModel] ===== OnOpenChannelAsync 执行完成 =====");
        }


        /// <summary>
        /// 关闭单个通道（命令处理）
        /// </summary>
        private async Task OnCloseChannelAsync(int channelIndex)
        {
            System.Diagnostics.Debug.WriteLine($"[ViewModel] 请求关闭通道 {channelIndex}");
            if (!IsDeviceConnected || _driver == null)
            {
                ReMessageBox.Show("请先连接板卡", "提示", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            try
            {
                // 先暂停该通道（停止发送循环并停止接收）以保证安全关闭
                await PauseChannelBeforeCloseAsync(channelIndex);

                // 如果关闭的是当前选中通道，需要更新接收状态显示
                if (channelIndex == SelectedChannelIndex)
                {
                    RaisePropertyChanged(nameof(SelectedChannelIsReceiving));
                }

                bool ok = await _driver.CloseChannelAsync(channelIndex);
                if (ok)
                {
                    System.Diagnostics.Debug.WriteLine($"[PXI4004] 通道 {channelIndex} 关闭成功");
                    if (channelIndex >= 0 && channelIndex < Channels.Count)
                    {
                        Channels[channelIndex].IsOpen = false;
                        Channels[channelIndex].IsStarted = false; // 关闭通道时自动停止启动状态
                        Channels[channelIndex].WasPaused = false; // 重置暂停标记
                        Channels[channelIndex].HasBusSettingsApplied = false; // 关闭时清除已应用标记，需重新应用总线设置
                    }
                    ReMessageBox.Show($"通道 {channelIndex} 已关闭", "成功", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[PXI4004] 通道 {channelIndex} 关闭失败");
                    ReMessageBox.Show($"关闭通道 {channelIndex} 失败，请查看日志", "失败", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PXI4004] 关闭通道异常: {ex.Message}");
                ReMessageBox.Show($"关闭通道失败: {ex.Message}", "错误", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 启动/暂停/继续命令处理
        /// </summary>
        private void OnStart()
        {
            if (!IsDeviceConnected || !SelectedChannelIsOpen)
            {
                ReMessageBox.Show("请先连接板卡并打开通道", "提示", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            // 启动当前选中通道
            if (SelectedChannelIndex >= 0 && SelectedChannelIndex < Channels.Count)
            {
                var channel = Channels[SelectedChannelIndex];
                if (!channel.IsStarted)
                {
                    // 启动操作
                    channel.IsStarted = true;
                    // 重置暂停标记，因为这是新的启动操作
                    channel.WasPaused = false;

                    System.Diagnostics.Debug.WriteLine($"[PXI4004] 启动通道 {SelectedChannelIndex}");
                    ReMessageBox.Show($"启动通道 {SelectedChannelIndex}", "成功", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                }
                else
                {
                    // 此分支不应该被执行，因为在已启动状态下，启动按钮应该是不可用的
                    System.Diagnostics.Debug.WriteLine($"[PXI4004] 警告：尝试在已启动状态下再次启动通道 {SelectedChannelIndex}");
                    return;
                }
            }

            // 通知UI更新
            RaisePropertyChanged(nameof(StartButtonText));
        }

        /// <summary>
        /// 暂停命令处理
        /// </summary>
        private async void OnPause()
        {
            if (!IsDeviceConnected || !SelectedChannelIsOpen)
            {
                ReMessageBox.Show("请先连接板卡并打开通道", "提示", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            // 停止后台发送循环
            lock (_sendLoopLock)
            {
                try
                {
                    _sendLoopCts?.Cancel();
                }
                catch { }
                try
                {
                    _sendLoopCts?.Dispose();
                }
                catch { }
                _sendLoopCts = null;
            }

            // 同时停止发送任务循环
            lock (_sendTaskLock)
            {
                try
                {
                    _sendTaskCts?.Cancel();
                }
                catch { }
                try
                {
                    _sendTaskCts?.Dispose();
                }
                catch { }
                _sendTaskCts = null;
            }

            // 如果当前选中通道正在接收，则只停止 UI 层的接收循环（不调用驱动 StopCAN）
            if (SelectedChannelIndex >= 0 && SelectedChannelIndex < Channels.Count)
            {
                // 停止仅 UI 的接收循环，保留硬件接收状态以便后续继续可以直接接收
                await StopReceiveLoopAsync(SelectedChannelIndex);
                // 通知UI更新接收状态
                RaisePropertyChanged(nameof(SelectedChannelIsReceiving));
            }

            // 设置当前选中通道为停止状态
            if (SelectedChannelIndex >= 0 && SelectedChannelIndex < Channels.Count)
            {
                Channels[SelectedChannelIndex].IsStarted = false;
                // 标记通道已被暂停，用于按钮文本显示
                Channels[SelectedChannelIndex].WasPaused = true;
            }

            // 通知UI更新按钮文本
            RaisePropertyChanged(nameof(StartButtonText));

            System.Diagnostics.Debug.WriteLine($"[PXI4004] 暂停通道 {SelectedChannelIndex}");
            ReMessageBox.Show($"暂停通道 {SelectedChannelIndex}", "成功", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        }


        /// <summary>
        /// 清空列表命令处理
        /// </summary>
        private void OnClearList()
        {
            if (!IsDeviceConnected || !SelectedChannelIsOpen)
            {
                ReMessageBox.Show("请先连接板卡并打开通道", "提示", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            // 清空当前选中通道的消息
            ClearChannelMessages(SelectedChannelIndex);
            System.Diagnostics.Debug.WriteLine($"[PXI4004] 清空通道 {SelectedChannelIndex} 消息列表");
            ReMessageBox.Show($"已清空通道 {SelectedChannelIndex} 的消息列表", "成功", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        }


        /// <summary>
        /// 打开所有通道
        /// </summary>
        private async Task OnOpenAllChannelsAsync()
        {
            if (!IsDeviceConnected || _driver == null)
            {
                ReMessageBox.Show("请先连接板卡", "提示", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            try
            {
                System.Diagnostics.Debug.WriteLine($"[PXI4004] 开始打开所有通道");

                int successCount = 0;
                int failCount = 0;

                for (int i = 0; i < Channels.Count; i++)
                {
                    try
                    {
                        bool ok = await _driver.OpenChannelAsync(i);
                        if (ok)
                        {
                            Channels[i].IsOpen = true;
                            successCount++;
                            System.Diagnostics.Debug.WriteLine($"[PXI4004] 通道 {i} 打开成功");
                        }
                        else
                        {
                            failCount++;
                            System.Diagnostics.Debug.WriteLine($"[PXI4004] 通道 {i} 打开失败");
                        }
                    }
                    catch (Exception ex)
                    {
                        failCount++;
                        System.Diagnostics.Debug.WriteLine($"[PXI4004] 通道 {i} 打开异常: {ex.Message}");
                    }
                }

                string message = $"批量操作完成\n成功打开: {successCount} 个通道\n失败: {failCount} 个通道";
                ReMessageBox.Show(message, "批量打开通道",
                    System.Windows.MessageBoxButton.OK,
                    failCount > 0 ? System.Windows.MessageBoxImage.Warning : System.Windows.MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PXI4004] 批量打开通道异常: {ex.Message}");
                ReMessageBox.Show($"批量打开通道失败: {ex.Message}", "错误", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 关闭所有通道
        /// </summary>
        private async Task OnCloseAllChannelsAsync()
        {
            if (!IsDeviceConnected || _driver == null)
            {
                ReMessageBox.Show("请先连接板卡", "提示", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            try
            {
                System.Diagnostics.Debug.WriteLine($"[PXI4004] 开始关闭所有通道");

                int successCount = 0;
                int failCount = 0;

                for (int i = 0; i < Channels.Count; i++)
                {
                    try
                    {
                        // 先暂停该通道（停止发送并停止接收），然后再关闭
                        await PauseChannelBeforeCloseAsync(i);

                        bool ok = await _driver.CloseChannelAsync(i);
                        if (ok)
                        {
                            Channels[i].IsOpen = false;
                            successCount++;
                            System.Diagnostics.Debug.WriteLine($"[PXI4004] 通道 {i} 关闭成功");
                        }
                        else
                        {
                            failCount++;
                            System.Diagnostics.Debug.WriteLine($"[PXI4004] 通道 {i} 关闭失败");
                        }
                        // ensure flags cleared
                        Channels[i].HasBusSettingsApplied = false;
                    }
                    catch (Exception ex)
                    {
                        failCount++;
                        System.Diagnostics.Debug.WriteLine($"[PXI4004] 通道 {i} 关闭异常: {ex.Message}");
                    }
                }

                // 更新接收状态显示（当前选中通道已被关闭）
                RaisePropertyChanged(nameof(SelectedChannelIsReceiving));

                string message = $"批量操作完成\n成功关闭: {successCount} 个通道\n失败: {failCount} 个通道";
                ReMessageBox.Show(message, "批量关闭通道",
                    System.Windows.MessageBoxButton.OK,
                    failCount > 0 ? System.Windows.MessageBoxImage.Warning : System.Windows.MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PXI4004] 批量关闭通道异常: {ex.Message}");
                ReMessageBox.Show($"批量关闭通道失败: {ex.Message}", "错误", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 打开总线设置窗口
        /// </summary>
        private async void OnOpenBusSettings()
        {
            if (!IsDeviceConnected || !SelectedChannelIsOpen || SelectedChannelIndex < 0 || SelectedChannelIndex >= Channels.Count)
            {
                ReMessageBox.Show("请先连接板卡并选择已打开的通道", "提示", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            var ch = Channels[SelectedChannelIndex];

            try
            {
                // 创建对话框实例
                var dlg = new Views.Dialogs.ChannelBusConfigWindow
                {
                    Owner = Application.Current?.MainWindow
                };

                // 设置数据上下文为当前通道的 CAN 参数模型，供对话框绑定使用
                try
                {
                    // 设置默认参数值
                    PXI4004.ARTCANX1_CAN_PARAM initParam = new PXI4004.ARTCANX1_CAN_PARAM
                    {
                        nBaudRate = 500000, // 500Kbps - 默认值，对应ComboBox的500k选项
                        nWorkMode = 0, // 正常模式 - 默认值，对应ComboBox的"正常发送"选项
                        bRecvTimestampEn = 1,
                        bAccExtID = 0,
                        nAccFilterCnt = 0, // 不参与滤波 - 默认值，对应ComboBox的"不参与滤波"选项
                        nAccCodeA = 0x00000000,
                        nAccCodeB = 0x00000000,
                        nAccMaskA = 0x00000000,  // 0表示接收所有帧
                        nAccMaskB = 0x00000000,
                        nFrameInterval = 0,
                        nReserved1 = new uint[7],
                        nReserved2 = new uint[32],
                        SendTrig = new PXI4004.ARTCANX1_TRIG_PARAM()
                    };
                    initParam.SendTrig.nTriggerType = 0; // 无触发

                    // 如果通道还没有应用总线设置，使用默认参数；否则从已应用的参数恢复
                    if (ch.HasBusSettingsApplied && ch.AppliedParam.HasValue)
                    {
                        // 使用已应用的参数，让用户看到当前设置
                        initParam = ch.AppliedParam.Value;
                        System.Diagnostics.Debug.WriteLine($"[ViewModel] 使用已应用的总线设置参数打开对话框 - 通道 {ch.Index}");
                    }
                    else
                    {
                        // 通道还没有应用设置，使用默认参数
                        // 只有在这种情况下才尝试从硬件获取参数
                        if (_driver != null)
                        {
                            try
                            {
                                var native = PXI4004.GetDefaultCANParam(_driver.DeviceHandle, (uint)ch.Index);
                                // 使用硬件参数，但保留合理的默认值作为fallback
                                initParam = native;
                                // ensure arrays are non-null
                                initParam.nReserved1 = initParam.nReserved1 ?? new uint[7];
                                initParam.nReserved2 = initParam.nReserved2 ?? new uint[32];

                                // 确保关键参数有合理的默认值，以防硬件返回无效值
                                if (initParam.nBaudRate == 0 || initParam.nBaudRate > 1000000) initParam.nBaudRate = 500000;
                                if (initParam.nWorkMode > 1) initParam.nWorkMode = 0;
                                if (initParam.nAccFilterCnt > 1) initParam.nAccFilterCnt = 0;
                            }
                            catch
                            {
                                // 如果获取硬件参数失败，保持默认值
                            }
                        }
                        System.Diagnostics.Debug.WriteLine($"[ViewModel] 使用默认参数打开总线设置对话框 - 通道 {ch.Index}");
                    }

                    dlg.Tag = initParam;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[ViewModel] 设置对话框数据上下文异常: {ex.Message}");
                    // 如果出现异常，显示错误信息但仍允许用户操作
                    ReMessageBox.Show($"设置总线参数时出现错误: {ex.Message}", "错误", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                    return; // 不显示对话框
                }

                // 显示对话框
                var result = dlg.ShowDialog();

                if (result == true)
                {
                    // 如果通道已打开且连接成功，重新应用设置
                    if (IsDeviceConnected && _driver != null && ch.IsOpen)
                    {
                        try
                        {
                            // 如果对话框生成了自定义参数（保存在 Tag），优先使用
                            bool applyOk = false;
                            PXI4004.ARTCANX1_CAN_PARAM? tagParam = null;

                            // 如果对话框生成了自定义参数（保存在 Tag），优先使用并保存到临时变量
                            if (dlg.Tag is PXI4004.ARTCANX1_CAN_PARAM p)
                            {
                                tagParam = p;
                                applyOk = await _driver.OpenChannelAsync(ch.Index, p);
                            }
                            else
                            {
                                // 否则使用驱动默认参数作为基点
                                var native = PXI4004.GetDefaultCANParam(_driver.DeviceHandle, (uint)ch.Index);
                                applyOk = await _driver.OpenChannelAsync(ch.Index, native);
                            }

                            if (applyOk)
                            {
                                // mark that this channel has its bus settings applied so Start/Pause can be enabled
                                ch.HasBusSettingsApplied = true;
                                // store applied parameters on channel for software-side receive filtering
                                try
                                {
                                    if (tagParam.HasValue)
                                    {
                                        ch.AppliedParam = tagParam.Value;
                                    }
                                    else
                                    {
                                        var nativeParam = PXI4004.GetDefaultCANParam(_driver.DeviceHandle, (uint)ch.Index);
                                        ch.AppliedParam = nativeParam;
                                    }
                                }
                                catch { ch.AppliedParam = null; }

                                // Explicitly set acceptance filter on hardware and verify result
                                try
                                {
                                    PXI4004.ARTCANX1_CAN_PARAM usedParam;
                                    if (tagParam.HasValue)
                                        usedParam = tagParam.Value;
                                    else
                                        usedParam = PXI4004.GetDefaultCANParam(_driver.DeviceHandle, (uint)ch.Index);

                                    // Only call SetAcceptanceFilterAsync if we did not already open channel with customParam
                                    // AND only if acceptance filtering is enabled (nAccFilterCnt > 0)
                                    if (!tagParam.HasValue && usedParam.nAccFilterCnt > 0)
                                    {
                                        bool hwOk = await _driver.SetAcceptanceFilterAsync(ch.Index, usedParam.nAccCodeA, usedParam.nAccMaskA);
                                        if (!hwOk)
                                        {
                                            System.Diagnostics.Debug.WriteLine($"[PXI4004] 硬件设置验收过滤器失败，通道 {ch.Index}");
                                            ReMessageBox.Show($"通道 {ch.Index} 的验收过滤器未成功下发到硬件，请检查设备和参数。", "警告", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                                        }
                                        else
                                        {
                                            System.Diagnostics.Debug.WriteLine($"[PXI4004] 硬件验收过滤器设置成功，通道 {ch.Index}");
                                        }
                                    }
                                    else if (usedParam.nAccFilterCnt == 0)
                                    {
                                        System.Diagnostics.Debug.WriteLine($"[PXI4004] 通道 {ch.Index} 不参与滤波，跳过硬件验收过滤器设置");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    System.Diagnostics.Debug.WriteLine($"[PXI4004] 下发验收过滤器到硬件时发生异常: {ex.Message}");
                                }

                                System.Diagnostics.Debug.WriteLine($"[PXI4004] 通道 {ch.Index} 设置已应用");
                            }
                            else
                            {
                                System.Diagnostics.Debug.WriteLine($"[PXI4004] 通道 {ch.Index} 设置应用失败");
                                ReMessageBox.Show($"应用通道设置失败，请检查设备或参数。", "错误", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                            }
                        }
                        catch (Exception ex)
                        {
                            ReMessageBox.Show($"应用通道设置失败: {ex.Message}", "错误", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ReMessageBox.Show($"打开总线设置窗口失败: {ex.Message}", "错误", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 打开板卡 - 检测板卡是否在线
        /// </summary>
        private async Task OnOpenDeviceAsync()
        {
            if (Device == null) return;

            try
            {
                ConnectionStatus = "检测中";

                // 创建驱动实例
                _driver = DriverFactory.CreateDriver(Device) as PXI4004Driver;
                if (_driver == null)
                {
                    throw new InvalidOperationException("无法创建 PXI4004 驱动实例");
                }

                // 连接设备（检测板卡）
                bool connected = await _driver.ConnectAsync();

                if (connected)
                {
                    IsDeviceConnected = true;
                    ConnectionStatus = "在线";

                    // 连接板卡成功后，重置所有通道状态
                    ResetAllChannelsState();

                    System.Diagnostics.Debug.WriteLine($"[PXI4004] 板卡检测成功: {Device.Name}，已重置所有通道状态");
                }
                else
                {
                    IsDeviceConnected = false;
                    ConnectionStatus = "离线";
                    ReMessageBox.Show(
                        $"板卡连接失败，请检查板卡位置及驱动",
                        "连接失败",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                IsDeviceConnected = false;
                ConnectionStatus = "离线";
                _driver = null;

                ReMessageBox.Show(
                    $"板卡连接失败，请检查板卡位置及驱动",
                    "连接失败",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);

                System.Diagnostics.Debug.WriteLine($"[PXI4004] 板卡检测异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 停止调试 - 用于以下场景：
        /// 1. 用户点击"停止读取"
        /// 2. 用户切换到其他板卡/页面
        /// 3. 项目关闭、应用退出
        /// </summary>
        public async Task StopDebugAsync()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[PXI4004] 停止调试: {Device?.Name}");

                // 停止接收循环
                await StopReceiveLoopAsync();

                // 断开连接
                if (_driver != null)
                {
                    await _driver.DisconnectAsync();
                    _driver = null;
                }

                // 重置 UI 状态
                IsDeviceConnected = false;
                ConnectionStatus = "离线";

                // 重置所有通道的启动状态和暂停标记
                foreach (var channel in Channels)
                {
                    channel.IsStarted = false;
                    channel.WasPaused = false;
                }

                System.Diagnostics.Debug.WriteLine($"[PXI4004] 调试已停止");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PXI4004] 停止调试异常: {ex.Message}");
            }
        }


        /// <summary>
        /// 后台发送循环：循环发送 TaskList 中的任务到当前选中通道，直到取消
        /// </summary>
        private async System.Threading.Tasks.Task RunSendLoopAsync(System.Threading.CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    // 在循环开始处捕获当前选中通道索引，避免用户在发送过程中切换选中通道导致发送/记录到错误的通道
                    int channelIndex = SelectedChannelIndex;

                    if (!IsDeviceConnected || _driver == null || channelIndex < 0 || channelIndex >= Channels.Count || !Channels[channelIndex].IsOpen)
                    {
                        await System.Threading.Tasks.Task.Delay(500, ct).ContinueWith(_ => { });
                        continue;
                    }

                    var tasksToSend = GetChannelTasks(channelIndex).ToList();
                    foreach (var task in tasksToSend)
                    {
                        if (ct.IsCancellationRequested) break;

                        // build frame
                        try
                        {
                            PXI4004.ARTCANX1_CAN_FRAME frame = new PXI4004.ARTCANX1_CAN_FRAME();
                            frame.DataBuf = new byte[8];
                            uint idVal = 0;
                            try
                            {
                                string s = task.Id?.Trim() ?? "0";
                                if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                                    idVal = Convert.ToUInt32(s.Substring(2), 16);
                                else if (s.EndsWith("h", StringComparison.OrdinalIgnoreCase))
                                    idVal = Convert.ToUInt32(s.Substring(0, s.Length - 1), 16);
                                else
                                    idVal = Convert.ToUInt32(s);
                            }
                            catch { idVal = 0; }
                            frame.nFrameID = idVal;
                            frame.bExtendedID = (byte)((task.FrameFormat?.Contains("扩展") == true) ? 1 : 0);
                            frame.nFrameType = (byte)((task.FrameType?.Contains("远程") == true) ? 1 : 0);

                            var dataParts = (task.Data ?? "").Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                            int len = Math.Min(8, dataParts.Length);
                            frame.nDataLength = (byte)len;
                            for (int i = 0; i < 8; i++) frame.DataBuf[i] = 0;
                            for (int i = 0; i < len; i++)
                            {
                                try
                                {
                                    frame.DataBuf[i] = Convert.ToByte(dataParts[i], 16);
                                }
                                catch
                                {
                                    frame.DataBuf[i] = 0;
                                }
                            }

                            bool ok = false;
                            try
                            {
                                ok = await _driver.SendFrameAsync(channelIndex, frame, 0.2);
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"[PXI4004] 发送帧异常: {ex.Message}");
                                ok = false;
                            }

                            var msg = new MessageItem
                            {
                                Index = System.Threading.Interlocked.Increment(ref _messageIndexCounter),
                                Time = DateTime.Now.ToString("HH:mm:ss.fff"),
                                Direction = "发送",
                                Id = $"0x{frame.nFrameID:X}",
                                FrameType = (frame.nFrameType == 0) ? "数据帧" : "远程帧",
                                FrameFormat = (frame.bExtendedID == 0) ? "标准帧" : "扩展帧",
                                Length = frame.nDataLength.ToString(),
                                Data = string.Join(" ", frame.DataBuf.Take(frame.nDataLength).Select(b => b.ToString("X2"))),
                                SendStatus = ok ? "成功" : "Error"
                            };

                            // 使用AddMessageToChannel方法来确保UI正确更新（使用捕获的 channelIndex，避免用户切换选中通道导致错乱）
                            AddMessageToChannel(channelIndex, msg);
                        }
                        catch (OperationCanceledException) { break; }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[ViewModel] 发送循环异常: {ex.Message}");
                        }

                        // wait interval (task.Interval in ms). ensure minimum delay
                        int delayMs = Math.Max(1, task.Interval);
                        try
                        {
                            await System.Threading.Tasks.Task.Delay(delayMs, ct);
                        }
                        catch (TaskCanceledException) { break; }
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ViewModel] 发送循环顶层异常: {ex.Message}");
            }
        }


        // 后台接收循环：轮询所有已打开的通道接收帧并写入消息列表（持续接收循环已移除，改为按需调用驱动的 ReceiveFrameAsync(channelIndex) 在需要时接收）。

        /// <summary>
        /// 开始接收命令处理
        /// </summary>
        private async Task OnStartReceiveAsync()
        {
            if (!IsDeviceConnected || _driver == null)
            {
                ReMessageBox.Show("请先连接板卡", "提示", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            if (SelectedChannelIndex < 0 || SelectedChannelIndex >= Channels.Count || !Channels[SelectedChannelIndex].IsOpen)
            {
                ReMessageBox.Show("请先选择并打开一个通道", "提示", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            if (!SelectedChannelHasSettingsApplied)
            {
                ReMessageBox.Show("请先应用总线设置", "提示", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            try
            {
                System.Diagnostics.Debug.WriteLine($"[ViewModel] 开始通道 {SelectedChannelIndex} 接收任务");

                // 重新应用总线配置到硬件，确保接收任务能正确工作
                var channel = Channels[SelectedChannelIndex];
                if (channel.AppliedParam.HasValue)
                {
                        System.Diagnostics.Debug.WriteLine($"[ViewModel] 重新应用总线配置到硬件，通道 {SelectedChannelIndex}");
                        try
                        {
                            var param = channel.AppliedParam.Value;

                            // 如果通道已经打开并且之前已经成功应用过总线设置，则无需再次调用 OpenChannelAsync，
                            // 因为驱动中的 OpenChannelAsync 会在通道已打开时先 Stop 再 Release 再 Init，从而导致“配置两遍”的现象。
                            // 这里避免重复初始化；仅在通道未打开或未应用设置时才重新初始化。
                            if (!channel.IsOpen || !channel.HasBusSettingsApplied)
                            {
                                // 重新应用完整的通道参数（包括波特率、工作模式等）
                                bool channelReconfigured = await _driver.OpenChannelAsync(SelectedChannelIndex, param);
                                if (channelReconfigured)
                                {
                                    System.Diagnostics.Debug.WriteLine($"[ViewModel] 通道 {SelectedChannelIndex} 重新配置成功");
                                }
                                else
                                {
                                    System.Diagnostics.Debug.WriteLine($"[ViewModel] 通道 {SelectedChannelIndex} 重新配置失败");
                                }
                            }
                            else
                            {
                                System.Diagnostics.Debug.WriteLine($"[ViewModel] 通道 {SelectedChannelIndex} 已打开且已应用总线设置，跳过重复初始化");
                            }

                            // 单独重新设置验收过滤器（仅在参与滤波时）
                            if (param.nAccFilterCnt > 0)
                            {
                                // 如果通道已打开且参数已应用，则无需单独重复下发；否则执行下发以确保硬件配置正确。
                                if (!channel.IsOpen || !channel.HasBusSettingsApplied)
                                {
                                    bool filterSet = await _driver.SetAcceptanceFilterAsync(SelectedChannelIndex, param.nAccCodeA, param.nAccMaskA);
                                    if (!filterSet)
                                    {
                                        System.Diagnostics.Debug.WriteLine($"[ViewModel] 重新设置验收过滤器失败，通道 {SelectedChannelIndex}");
                                    }
                                    else
                                    {
                                        System.Diagnostics.Debug.WriteLine($"[ViewModel] 重新设置验收过滤器成功，通道 {SelectedChannelIndex}");
                                    }
                                }
                                else
                                {
                                    System.Diagnostics.Debug.WriteLine($"[ViewModel] 通道 {SelectedChannelIndex} 已应用验收过滤器，跳过重复下发");
                                }
                            }
                            else
                            {
                                System.Diagnostics.Debug.WriteLine($"[ViewModel] 通道 {SelectedChannelIndex} 不参与滤波，跳过验收过滤器设置");
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[ViewModel] 重新应用总线配置异常: {ex.Message}");
                        }
                }

                bool success = await _driver.StartReceiveTaskAsync(SelectedChannelIndex);
                if (success)
                {
                    System.Diagnostics.Debug.WriteLine($"[ViewModel] 通道 {SelectedChannelIndex} 接收任务启动成功");
                    ReMessageBox.Show($"通道 {SelectedChannelIndex} 开始接收", "成功", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);

                    // 在开始接收前清理接收统计与缓冲，避免旧状态影响新轮次的接收速率
                    try
                    {
                        _receiveBatchCounter[SelectedChannelIndex] = 0;
                    }
                    catch { }
                    try { _lastReceiveLogTime.Remove(SelectedChannelIndex); } catch { }
                    try { _uiUpdateBuffer.TryRemove(SelectedChannelIndex, out _); } catch { }
                    // 让驱动层也清理其接收统计，避免驱动的自适应超时/批量大小基于旧状态
                    try { _driver?.ResetReceiveStats(SelectedChannelIndex); } catch { }

                    // 启动后台接收循环（异步，不等待），立即更新接收状态显示
                    _ = StartReceiveLoopAsync(SelectedChannelIndex);
                    RaisePropertyChanged(nameof(SelectedChannelIsReceiving));
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[ViewModel] 通道 {SelectedChannelIndex} 接收任务启动失败");
                    ReMessageBox.Show($"启动通道 {SelectedChannelIndex} 接收失败，请查看日志", "失败", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ViewModel] 启动接收任务异常: {ex.Message}");
                ReMessageBox.Show($"启动接收失败: {ex.Message}", "错误", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 停止接收命令处理
        /// </summary>
        private async Task OnStopReceiveAsync()
        {
            if (!IsDeviceConnected || _driver == null)
            {
                ReMessageBox.Show("请先连接板卡", "提示", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            if (SelectedChannelIndex < 0 || SelectedChannelIndex >= Channels.Count || !Channels[SelectedChannelIndex].IsOpen)
            {
                ReMessageBox.Show("请先选择并打开一个通道", "提示", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            try
            {
                System.Diagnostics.Debug.WriteLine($"[ViewModel] 停止通道 {SelectedChannelIndex} 接收任务");

                // 停止后台接收循环
                await StopReceiveLoopAsync(SelectedChannelIndex);

                bool success = await _driver.StopReceiveTaskAsync(SelectedChannelIndex);
                if (success)
                {
                    System.Diagnostics.Debug.WriteLine($"[ViewModel] 通道 {SelectedChannelIndex} 接收任务停止成功");
                    // 驱动已停止并释放通道资源，更新 UI 状态以反映硬件当前未打开
                    if (SelectedChannelIndex >= 0 && SelectedChannelIndex < Channels.Count)
                    {
                        Channels[SelectedChannelIndex].IsOpen = false;
                        Channels[SelectedChannelIndex].HasBusSettingsApplied = false;
                        Channels[SelectedChannelIndex].AppliedParam = null;
                    }
                    ReMessageBox.Show($"通道 {SelectedChannelIndex} 已停止接收", "成功", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[ViewModel] 通道 {SelectedChannelIndex} 接收任务停止失败");
                    ReMessageBox.Show($"停止通道 {SelectedChannelIndex} 接收失败，请查看日志", "失败", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                }

                // 更新接收状态显示
                RaisePropertyChanged(nameof(SelectedChannelIsReceiving));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ViewModel] 停止接收任务异常: {ex.Message}");
                ReMessageBox.Show($"停止接收失败: {ex.Message}", "错误", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 接收循环控制 - 为每个通道维护独立的控制
        /// </summary>
        private System.Collections.Generic.Dictionary<int, System.Threading.CancellationTokenSource> _receiveLoopCts = new System.Collections.Generic.Dictionary<int, System.Threading.CancellationTokenSource>();
        private readonly object _receiveLoopLock = new object();

        /// <summary>
        /// 启动后台接收循环
        /// </summary>
        private async Task StartReceiveLoopAsync(int channelIndex)
        {
            // 使用Task.Run将接收循环放到后台线程中运行，避免阻塞UI线程
            await Task.Run(async () =>
            {
                lock (_receiveLoopLock)
                {
                    // 如果该通道已经有接收循环在运行，先停止它
                    if (_receiveLoopCts.ContainsKey(channelIndex))
                    {
                        try { _receiveLoopCts[channelIndex].Cancel(); } catch { }
                        try { _receiveLoopCts[channelIndex].Dispose(); } catch { }
                        _receiveLoopCts.Remove(channelIndex);
                    }

                    // 为该通道创建新的接收循环控制
                    _receiveLoopCts[channelIndex] = new System.Threading.CancellationTokenSource();
                }

                var ct = _receiveLoopCts[channelIndex].Token;

                try
                {
                    while (!ct.IsCancellationRequested)
                    {
                        // 检查设备连接状态 - 需要在线程安全的方式访问UI属性
                        bool isDeviceConnected = false;
                        bool isChannelOpen = false;

                        // 在UI线程中检查状态
                        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            isDeviceConnected = IsDeviceConnected;
                            isChannelOpen = (channelIndex >= 0 && channelIndex < Channels.Count && Channels[channelIndex].IsOpen);
                        });

                        if (!isDeviceConnected || _driver == null || channelIndex < 0 || !isChannelOpen)
                        {
                            await System.Threading.Tasks.Task.Delay(500, ct).ConfigureAwait(false);
                            continue;
                        }

                        try
                        {
                            // 读取接收到的帧
                            var frames = await _driver.ReceiveFramesBatchAsync(channelIndex, 5, 0.01).ConfigureAwait(false);

                            // 将帧添加到消息列表 - 需要在UI线程中执行
                            if (frames.Count > 0)
                            {
                                // 节流：对于接收到的帧数量摘要，每个通道每隔 SkipEmptyFrameLogInterval 打印一次，
                                // 避免在输出窗口产生大量重复的调试信息（尤其是高帧率场景）。
                                DateTime nowLog = DateTime.UtcNow;
                                bool shouldLogReceive = false;
                                if (!_lastReceiveLogTime.TryGetValue(channelIndex, out var lastRecv))
                                {
                                    shouldLogReceive = true;
                                }
                                else if ((nowLog - lastRecv) > SkipEmptyFrameLogInterval)
                                {
                                    shouldLogReceive = true;
                                }

                                if (shouldLogReceive)
                                {
                                    _lastReceiveLogTime[channelIndex] = nowLog;
                                    // 汇总统计：打印自上次摘要以来收到的总帧数（并重置计数）
                                    int totalSinceLast = 0;
                                    if (_receiveBatchCounter.TryGetValue(channelIndex, out var existing))
                                    {
                                        totalSinceLast = existing + frames.Count;
                                    }
                                    else
                                    {
                                        totalSinceLast = frames.Count;
                                    }
                                    _receiveBatchCounter[channelIndex] = 0; // reset after logging
                                    System.Diagnostics.Debug.WriteLine($"[ViewModel] 从通道 {channelIndex} 共接收 {totalSinceLast} 帧 (摘要，每{SkipEmptyFrameLogInterval.TotalSeconds}s 打印一次)");
                                }
                                else
                                {
                                    // 累计统计以便下一次摘要打印
                                    if (!_receiveBatchCounter.ContainsKey(channelIndex)) _receiveBatchCounter[channelIndex] = 0;
                                    _receiveBatchCounter[channelIndex] += frames.Count;
                                }

                            }
                            // 处理每帧并加入消息列表（UI 显示）
                            if (frames.Count > 0)
                            {
                                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                                {
                                        foreach (var frame in frames)
                                        {
                                            if (ct.IsCancellationRequested) break;

                                            // 过滤掉空帧（长度为0或无有效数据）的显示，避免在消息列表打印没有数据的条目
                                            if (frame.nDataLength == 0 || frame.DataBuf == null || frame.DataBuf.Take(frame.nDataLength).All(b => b == 0))
                                        {
                                            // 节流日志：每个通道每隔 SkipEmptyFrameLogInterval 打印一次跳过空帧的信息，避免输出过多
                                            DateTime now = DateTime.UtcNow;
                                            bool shouldLog = false;
                                            if (!_lastSkipEmptyFrameLog.TryGetValue(channelIndex, out var last))
                                            {
                                                shouldLog = true;
                                            }
                                            else if ((now - last) > SkipEmptyFrameLogInterval)
                                            {
                                                shouldLog = true;
                                            }

                                            if (shouldLog)
                                            {
                                                _lastSkipEmptyFrameLog[channelIndex] = now;
                                                System.Diagnostics.Debug.WriteLine($"[ViewModel] 从通道 {channelIndex} 跳过空帧 (ID=0x{frame.nFrameID:X}, Len={frame.nDataLength})");
                                            }
                                            continue;
                                        }

                                        // 软件端验收过滤（基于已应用的通道参数），使用Driver的统一过滤方法
                                        bool acceptedByFilter = true;
                                        try
                                        {
                                            var applied = Channels[channelIndex].AppliedParam;
                                            if (applied.HasValue)
                                            {
                                                acceptedByFilter = _driver.ApplySoftwareAcceptanceFilter(channelIndex, frame, applied.Value);
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            System.Diagnostics.Debug.WriteLine($"[ViewModel] 验收过滤异常: {ex.Message}");
                                            // 出错时保守策略：接收帧以便不丢失数据
                                            acceptedByFilter = true;
                                        }

                                        if (!acceptedByFilter)
                                        {
                                            // 过滤此帧（不加入消息列表）
                                            continue;
                                        }

                                    var msg = new MessageItem
                                    {
                                        Index = System.Threading.Interlocked.Increment(ref _messageIndexCounter),
                                        Time = DateTime.Now.ToString("HH:mm:ss.fff"),
                                        Direction = "接收",
                                        Id = $"0x{frame.nFrameID:X}",
                                        FrameType = (frame.nFrameType == 0) ? "数据帧" : "远程帧",
                                        FrameFormat = (frame.bExtendedID == 0) ? "标准帧" : "扩展帧",
                                        Length = frame.nDataLength.ToString(),
                                        Data = string.Join(" ", frame.DataBuf.Take(frame.nDataLength).Select(b => b.ToString("X2"))),
                                        SendStatus = "成功"
                                    };

                                    AddMessageToChannel(channelIndex, msg);

                                    // 成功接收帧后增加接收帧数统计
                                    ReceiveFrameCount++;
                                }
                                });
                            }

                            // 如果没有收到帧，稍微延迟一下避免CPU占用过高
                            if (frames.Count == 0)
                            {
                                await System.Threading.Tasks.Task.Delay(10, ct).ConfigureAwait(false);
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[ViewModel] 接收循环异常: {ex.Message}");
                            await System.Threading.Tasks.Task.Delay(100, ct).ConfigureAwait(false);
                        }
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[ViewModel] 接收循环顶层异常: {ex.Message}");
                }

                lock (_receiveLoopLock)
                {
                    // 清理该通道的接收循环控制
                    if (_receiveLoopCts.ContainsKey(channelIndex))
                    {
                        try { _receiveLoopCts[channelIndex].Dispose(); } catch { }
                        _receiveLoopCts.Remove(channelIndex);
                    }
                }
            });
        }

        /// <summary>
        /// 停止指定通道的接收功能（如果正在接收）
        /// </summary>
        private async Task StopChannelReceivingIfNeededAsync(int channelIndex)
        {
            if (channelIndex < 0 || channelIndex >= Channels.Count)
                return;

            // 检查该通道是否正在接收
            lock (_receiveLoopLock)
            {
                if (!_receiveLoopCts.ContainsKey(channelIndex) || _receiveLoopCts[channelIndex].IsCancellationRequested)
                    return; // 该通道没有在接收
            }

            try
            {
                System.Diagnostics.Debug.WriteLine($"[ViewModel] 自动停止通道 {channelIndex} 的接收任务");

                // 停止后台接收循环
                await StopReceiveLoopAsync(channelIndex);

                // 停止驱动的接收任务
                if (_driver != null)
                {
                    await _driver.StopReceiveTaskAsync(channelIndex);
                }

                System.Diagnostics.Debug.WriteLine($"[ViewModel] 通道 {channelIndex} 接收任务已自动停止");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ViewModel] 自动停止通道 {channelIndex} 接收任务异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 在关闭通道前暂停该通道：停止发送循环并停止接收任务，更新通道状态
        /// </summary>
        private async Task PauseChannelBeforeCloseAsync(int channelIndex)
        {
            if (channelIndex < 0 || channelIndex >= Channels.Count) return;

            // 停止发送循环（如果有）
            lock (_sendLoopLock)
            {
                try
                {
                    _sendLoopCts?.Cancel();
                }
                catch { }
                try
                {
                    _sendLoopCts?.Dispose();
                }
                catch { }
                _sendLoopCts = null;
            }

            // 停止发送任务循环（如果有）
            lock (_sendTaskLock)
            {
                try
                {
                    _sendTaskCts?.Cancel();
                }
                catch { }
                try
                {
                    _sendTaskCts?.Dispose();
                }
                catch { }
                _sendTaskCts = null;
            }

            // 停止接收循环和驱动接收任务
            await StopChannelReceivingIfNeededAsync(channelIndex);

            // 标记通道为已停止（暂停）
            if (channelIndex >= 0 && channelIndex < Channels.Count)
            {
                Channels[channelIndex].IsStarted = false;
                Channels[channelIndex].WasPaused = true;
            }

            // 更新 UI 状态
            RaisePropertyChanged(nameof(SelectedChannelIsStarted));
            RaisePropertyChanged(nameof(SelectedChannelWasPaused));
            RaisePropertyChanged(nameof(SelectedChannelIsReceiving));
        }

        /// <summary>
        /// 停止后台接收循环
        /// </summary>
        private Task StopReceiveLoopAsync(int channelIndex = -1)
        {
            lock (_receiveLoopLock)
            {
                if (channelIndex >= 0)
                {
                    // 停止指定通道的接收循环
                    if (_receiveLoopCts.ContainsKey(channelIndex))
                    {
                        try { _receiveLoopCts[channelIndex].Cancel(); } catch { }
                        try { _receiveLoopCts[channelIndex].Dispose(); } catch { }
                        _receiveLoopCts.Remove(channelIndex);
                    }

                    // 清空该通道的UI更新缓冲区
                    _uiUpdateBuffer.TryRemove(channelIndex, out _);
                }
                else
                {
                    // 停止所有通道的接收循环
                    foreach (var kvp in _receiveLoopCts)
                    {
                        try { kvp.Value.Cancel(); } catch { }
                        try { kvp.Value.Dispose(); } catch { }
                    }
                    _receiveLoopCts.Clear();

                    // 清空所有通道的UI更新缓冲区
                    _uiUpdateBuffer.Clear();
                }
            }

            return System.Threading.Tasks.Task.CompletedTask;
        }

        /// <summary>
        /// 处理板卡名称变更
        /// </summary>
        public void OnCardNameChanged(string originalName)
        {
            if (_pxiChassisService == null || Device == null)
                return;

            string newName = CardName?.Trim();

            if (newName == originalName)
                return;

            if (string.IsNullOrWhiteSpace(newName))
            {
                CardName = originalName;
                return;
            }

            if (!_pxiChassisService.ValidateCardName(ChassisName, Device.Id, newName))
            {
                CardName = originalName;
                return;
            }

            bool success = _pxiChassisService.RenameCard(ChassisName, Device.Id, newName);
            if (!success)
            {
                CardName = originalName;
            }
        }

        #endregion

        #region IDisposable

        private bool _disposed = false;

        /// <summary>
        /// 释放资源 - 在页面关闭/切换时调用
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                _batchUpdateTimer?.Dispose();
                _uiUpdateBuffer.Clear();

                try
                {
                    _ = StopReceiveLoopAsync();
                }
                catch
                {
                }

                try
                {
                    lock (_sendLoopLock)
                    {
                        try { _sendLoopCts?.Cancel(); } catch { }
                        try { _sendLoopCts?.Dispose(); } catch { }
                        _sendLoopCts = null;
                    }
                }
                catch
                {
                }

                try
                {
                    lock (_sendTaskLock)
                    {
                        try { _sendTaskCts?.Cancel(); } catch { }
                        try { _sendTaskCts?.Dispose(); } catch { }
                        _sendTaskCts = null;
                    }
                }
                catch
                {
                }
            }

            _disposed = true;
        }

        #endregion
    }
}