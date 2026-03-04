 using MeasureControl.Drivers;
using MeasureControl.Events;
using MeasureControl.Models;
using MeasureControl.Models.Devices;
using MeasureControl.Services;
using MeasureControl.Views;
using MeasureControl.Views.Dialogs;
using MeasureControl.Constants;
using MeasureControl.Helpers;
using Prism.Commands;
using Prism.Events;
using Prism.Mvvm;
using Sys = System;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace MeasureControl.ViewModels.TestTask.CardCATPanel
{
    /// <summary>
    /// 1553B总线配置面板的ViewModel（支持多总线接口）
    /// </summary>
    public class ART1553BConfigPanelViewModel : BindableBase, IDisposable, ICloseGuard
    {
        private static readonly bool UseMultiChannelRT = false;

        private DeviceBase _device;
        private string _chassisName;
        private string _cardModel;
        private string _cardName;
        private IDeviceDriver _driver;

        private bool _ownsDriverLifecycle; // 是否拥有驱动生命周期
        private bool _isDeviceConnected;
        private string _connectionStatus;
        private readonly ProjectService _projectService;
        private readonly IPxiChassisService _pxiChassisService;
        private readonly IEventAggregator _eventAggregator;

        // 测试任务相关属性
        private readonly ObservableCollection<string> _availableTestTasks = new ObservableCollection<string>();
        private string _selectedTestTask;
        private bool _hasPendingChanges;
        private bool _isLoadingTaskOptions;
        private bool _isConfigurationLocked;
        private SubscriptionToken _testTaskCreatedToken;

        // 总线接口相关属性
        private ObservableCollection<Mil1553BBusInterface> _busInterfaces;
        private Mil1553BBusInterface _selectedBusInterface;

        // 1553B配置属性（基于选中的总线接口）
        private string _selectedTerminalMode = "RT - 远程终端"; // BC/RT/BM，默认RT
        private string _selectedTransmissionRate; // 传输速率
        private ObservableCollection<MessageConfigItem> _messageConfigs;// 消息配置列表
        private ObservableCollection<ChannelConfigItem> _channelConfigs;  // 通道配置列表
        
        // 模式显示属性
        private bool _isBCMode;
        private bool _isRTMode;
        private bool _isBMMode;
        private bool _canSendData;

        // 缓存每个总线接口的配置
        private Dictionary<int, BusInterfaceConfig> _busInterfaceConfigs = new Dictionary<int, BusInterfaceConfig>();

        // 通道相关属性（支持2个物理通道：Channel 0和Channel 1）
        // 注意：通道0/1是物理通道，与通道A/B（双冗余总线）是不同的概念
        // - 通道0/1：两个独立的物理通道，每个通道都有独立的配置（BC/RT/BM模式、RT列表等）
        // - 通道A/B：1553B协议的双冗余总线（A是主通道，B是备份通道），在消息配置的ChannelSelect中选择
        private int _selectedChannel = 0;
        private readonly List<int> _availableChannels = new List<int> { 0, 1 };
        
        // 通道配置ViewModel（通道0和通道1共享同一个ViewModel实例，但使用不同的配置数据）
        private ART1553BConfigPanelViewModel _channel0Config;
        private ART1553BConfigPanelViewModel _channel1Config;

        // BC模式配置
        private ushort _bcResponseTimeout = 4000; // 响应超时（0.5us单位）
        private int _bcFrameGap = 10; // 帧间隔（1us单位）
        private int _bcRetryCount = 0; // 重试次数
        private int _bcTargetRTAddress = 1; // BC发送目标RT地址（0-31）
        private int _bcTargetSubAddress = 1; // BC发送目标子地址（1-30）

        // RT模式配置
        private int _rtAddress = 1;
        private int _subAddress = 1;
        private ushort _rtResponseTime = 500; // RT响应时间（0.5us单位）
        private bool _rtEnabled = true; // RT使能
        private string _rtTxMode = "SingleBuffer"; // RT发送模式：SingleBuffer/CircularBuffer
        
        // RT配置列表（支持0-31个RT）
        private ObservableCollection<RTConfigItem> _rtConfigs;
        private RTConfigItem _selectedRTConfig;

        // RT内部子地址配置（1-30）
        private ObservableCollection<RTSubAddressConfigItem> _rtSubAddressConfigs;

        // 发送接收数据
        private int _sendByteCount = 2;
        private string _sendDataHex = "00 00";

        private int _receivedByteCount;
        private string _receivedDataHex;

        private readonly object _rtReceiveLock = new object();
        private readonly Dictionary<int, (int ByteCount, string DataHex)> _receivedByChannel = new Dictionary<int, (int ByteCount, string DataHex)>();

        // BM模式监控消息相关
        private ObservableCollection<BMMessageItem> _bmMessages;

        // 运行状态标志（参考官方例程）- 按通道独立存储
        private Dictionary<int, bool> _isBCRunningByChannel = new Dictionary<int, bool> { { 0, false }, { 1, false } };
        private Dictionary<int, bool> _isRTRunningByChannel = new Dictionary<int, bool> { { 0, false }, { 1, false } };
        private Dictionary<int, bool> _isBMRunningByChannel = new Dictionary<int, bool> { { 0, false }, { 1, false } };
        
        // BC循环发送线程（参考官方例程：循环BC_Start -> BC_IsMsgOver -> Sleep）
        private Dictionary<int, Thread> _bcLoopThreads = new Dictionary<int, Thread> { { 0, null }, { 1, null } };
        private Dictionary<int, CancellationTokenSource> _bcLoopCts = new Dictionary<int, CancellationTokenSource> { { 0, null }, { 1, null } };
        
        // RT接收统计（参考官方例程）
        private Dictionary<int, ulong> _rtReceiveCountByChannel = new Dictionary<int, ulong> { { 0, 0 }, { 1, 0 } };
        private Dictionary<int, ulong> _rtErrorCountByChannel = new Dictionary<int, ulong> { { 0, 0 }, { 1, 0 } };
        
        // BC发送统计（参考官方例程）
        private Dictionary<int, ulong> _bcSendCountByChannel = new Dictionary<int, ulong> { { 0, 0 }, { 1, 0 } };
        
        // 发送间隔（毫秒）
        private int _bcSendInterval = 1000;
        
        // 当前通道的运行状态属性（用于UI绑定）
        private bool _isBCRunning = false;
        private bool _isRTRunning = false;
        private bool _isBMRunning = false;
        private readonly object _bmMessagesLock = new object();
        private int _bmFilterRTAddress = -1; // -1表示不过滤
        private string _bmFilterMessageType = "全部"; // 全部/BC->RT/RT->BC/RT->RT/广播/模式码
        private int _bmTotalMessageCount;
        private int _bmErrorMessageCount;
        private bool _bmAutoScroll = true;
        private const int MaxBMMessages = 10000; // 最大保存消息数

        #region Properties

        /// <summary>
        /// 设备对象（1553B设备）
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
        /// 总线接口列表
        /// </summary>
        public ObservableCollection<Mil1553BBusInterface> BusInterfaces
        {
            get => _busInterfaces;
            set => SetProperty(ref _busInterfaces, value);
        }

        /// <summary>
        /// 选中的总线接口
        /// </summary>
        public Mil1553BBusInterface SelectedBusInterface
        {
            get => _selectedBusInterface;
            set
            {
                if (SetProperty(ref _selectedBusInterface, value))
                {
                    // 切换总线接口时，加载对应的配置
                    LoadConfigForSelectedBusInterface();

                    RefreshReceivedForSelectedInterface();
                }
            }
        }

        /// <summary>
        /// 终端模式候选项
        /// </summary>
        public List<string> TerminalModes { get; } = new List<string>
        {
            "BC - 总线控制器",
            "RT - 远程终端",
            "BM - 总线监控器"
        };

        /// <summary>
        /// 选中的终端模式
        /// </summary>
        public string SelectedTerminalMode
        {
            get => _selectedTerminalMode;
            set
            {
                if (SetProperty(ref _selectedTerminalMode, value))
                {
                    // 更新模式显示属性
                    UpdateModeVisibility();
                    
                    // 更新工作模式
                    UpdateWorkModeForSelectedInterface(value);
                    
                    // 更新到当前选中的总线接口
                    if (_selectedBusInterface != null)
                    {
                        UpdateWorkModeForSelectedInterface(value);
                    }
                    MarkDirty();
                }
            }
        }

        /// <summary>
        /// 是否为BC模式
        /// </summary>
        public bool IsBCMode
        {
            get => _isBCMode;
            private set => SetProperty(ref _isBCMode, value);
        }

        /// <summary>
        /// 是否为RT模式
        /// </summary>
        public bool IsRTMode
        {
            get => _isRTMode;
            private set => SetProperty(ref _isRTMode, value);
        }

        /// <summary>
        /// 是否为BM模式
        /// </summary>
        public bool IsBMMode
        {
            get => _isBMMode;
            private set => SetProperty(ref _isBMMode, value);
        }

        /// <summary>
        /// 是否可以发送数据（BC和RT模式可以发送）
        /// </summary>
        public bool CanSendData
        {
            get => _canSendData;
            private set => SetProperty(ref _canSendData, value);
        }

        /// <summary>
        /// 可用测试任务列表
        /// </summary>
        public ObservableCollection<string> AvailableTestTasks => _availableTestTasks;

        /// <summary>
        /// 选中的测试任务
        /// </summary>
        public string SelectedTestTask
        {
            get => _selectedTestTask;
            set => ChangeSelectedTestTask(value);
        }

        /// <summary>
        /// 是否有测试任务选项
        /// </summary>
        public bool HasTestTaskOptions => AvailableTestTasks.Count > 0;

        /// <summary>
        /// 配置是否锁定（设备连接时锁定）
        /// </summary>
        public bool IsConfigurationLocked
        {
            get => _isConfigurationLocked;
            private set => SetProperty(ref _isConfigurationLocked, value);
        }

        /// <summary>
        /// 可用通道列表（Channel 0和Channel 1）
        /// </summary>
        public List<int> AvailableChannels => _availableChannels;

        /// <summary>
        /// 通道0配置（独立的物理通道0）
        /// 注意：通道0/1是物理通道，与通道A/B（双冗余总线）是不同的概念
        /// </summary>
        public ART1553BConfigPanelViewModel Channel0Config
        {
            get => _channel0Config ?? (_channel0Config = this);
            set => SetProperty(ref _channel0Config, value);
        }

        /// <summary>
        /// 通道1配置（独立的物理通道1）
        /// 注意：通道0/1是物理通道，与通道A/B（双冗余总线）是不同的概念
        /// </summary>
        public ART1553BConfigPanelViewModel Channel1Config
        {
            get => _channel1Config ?? (_channel1Config = this);
            set => SetProperty(ref _channel1Config, value);
        }

        /// <summary>
        /// 选中的通道
        /// </summary>
        public int SelectedChannel
        {
            get => _selectedChannel;
            set
            {
                if (_selectedChannel != value)
                {
                    // 保存当前通道的配置
                    SaveCurrentChannelConfig();
                    
                    // 切换通道
                    _selectedChannel = value;
                    RaisePropertyChanged();
                    
                    // 更新当前通道的运行状态显示
                    UpdateChannelRunningState();
                    
                    // 加载目标通道的配置
                    LoadConfigForChannel(value);
                    
                    // 通知 UI 更新与通道相关的显示（选中 RT + 子地址配置）
                    RaisePropertyChanged(nameof(SelectedRTForConfigWithChannel));
                    RaisePropertyChanged(nameof(SelectedRTSubAddressConfigs));
                }
            }
        }
        
        /// <summary>
        /// 保存当前通道的配置到缓存
        /// </summary>
        private void SaveCurrentChannelConfig()
        {
            if (SelectedBusInterface == null) return;
            
            var interfaceId = SelectedBusInterface.InterfaceNumber;
            if (!_busInterfaceConfigs.ContainsKey(interfaceId))
            {
                _busInterfaceConfigs[interfaceId] = new BusInterfaceConfig();
            }
            
            var config = _busInterfaceConfigs[interfaceId];
            var channelConfig = _selectedChannel == 0 ? config.Channel0Config : config.Channel1Config;
            
            // 保存BC模式配置
            channelConfig.BCResponseTimeout = BCResponseTimeout;
            channelConfig.BCFrameGap = BCFrameGap;
            channelConfig.BCRetryCount = BCRetryCount;
            channelConfig.BCTargetRTAddress = BCTargetRTAddress;
            channelConfig.BCTargetSubAddress = BCTargetSubAddress;
            channelConfig.MessageConfigs = MessageConfigs?.Select(msg => new MessageConfigItem
            {
                MessageId = msg.MessageId,
                MessageName = msg.MessageName,
                MessageType = msg.MessageType,
                RTAddress = msg.RTAddress,
                SubAddress = msg.SubAddress,
                DataLength = msg.DataLength,
                IsEnabled = msg.IsEnabled,
                MessageGap = msg.MessageGap,
                ChannelSelect = msg.ChannelSelect,
                RetryEnable = msg.RetryEnable,
                ModeCode = msg.ModeCode,
                RTAddress2 = msg.RTAddress2,
                SubAddress2 = msg.SubAddress2,
                DataHex = msg.DataHex
            }).ToList() ?? new List<MessageConfigItem>();
            
            // 保存RT模式配置
            channelConfig.RTAddress = RTAddress;
            channelConfig.SubAddress = SubAddress;
            channelConfig.RTResponseTime = RTResponseTime;
            channelConfig.RTTxMode = RTTxMode;
            channelConfig.RTConfigs = RTConfigs?.Select(rt => new RTConfigItem
            {
                RTAddress = rt.RTAddress,
                SubAddress = rt.SubAddress,
                ResponseTime = rt.ResponseTime,
                IsEnabled = rt.IsEnabled,
                TxMode = rt.TxMode
            }).ToList() ?? new List<RTConfigItem>();
            
            // 保存RT内部子地址配置
            channelConfig.RTSubAddressConfigs = RTSubAddressConfigs?.Select(sub => new RTSubAddressConfigItem
            {
                SubAddress = sub.SubAddress,
                ReceiveEnabled = sub.ReceiveEnabled,
                TransmitEnabled = sub.TransmitEnabled,
                DataLength = sub.DataLength,
                SendDataHex = sub.SendDataHex
            }).ToList() ?? new List<RTSubAddressConfigItem>();
            
            // 保存当前通道的工作模式（BC/RT/BM）
            channelConfig.TerminalMode = SelectedTerminalMode;
        }
        
        /// <summary>
        /// 加载指定通道的配置
        /// </summary>
        private void LoadConfigForChannel(int channel)
        {
            if (SelectedBusInterface == null) return;
            
            var interfaceId = SelectedBusInterface.InterfaceNumber;
            if (!_busInterfaceConfigs.ContainsKey(interfaceId))
            {
                _busInterfaceConfigs[interfaceId] = new BusInterfaceConfig();
            }
            
            var config = _busInterfaceConfigs[interfaceId];
            config.SelectedChannel = channel;
            
            var channelConfig = channel == 0 ? config.Channel0Config : config.Channel1Config;
            
            // 加载通道级保存的工作模式（如果存在则覆盖全局选择）
            if (!string.IsNullOrEmpty(channelConfig.TerminalMode))
            {
                SelectedTerminalMode = channelConfig.TerminalMode;
            }
            
            // 加载BC模式配置
            BCResponseTimeout = channelConfig.BCResponseTimeout;
            BCFrameGap = channelConfig.BCFrameGap;
            BCRetryCount = channelConfig.BCRetryCount;
            BCTargetRTAddress = channelConfig.BCTargetRTAddress;
            BCTargetSubAddress = channelConfig.BCTargetSubAddress;
            
            // 加载消息配置
            if (channelConfig.MessageConfigs != null && channelConfig.MessageConfigs.Any())
            {
                MessageConfigs.Clear();
                foreach (var msg in channelConfig.MessageConfigs)
                {
                    MessageConfigs.Add(new MessageConfigItem
                    {
                        MessageId = msg.MessageId,
                        MessageName = msg.MessageName,
                        MessageType = msg.MessageType,
                        RTAddress = msg.RTAddress,
                        SubAddress = msg.SubAddress,
                        DataLength = msg.DataLength,
                        IsEnabled = msg.IsEnabled,
                        MessageGap = msg.MessageGap,
                        ChannelSelect = msg.ChannelSelect,
                        RetryEnable = msg.RetryEnable,
                        ModeCode = msg.ModeCode,
                        RTAddress2 = msg.RTAddress2,
                        SubAddress2 = msg.SubAddress2,
                        DataHex = msg.DataHex
                    });
                }
            }
            else
            {
                // 如果没有保存的配置，初始化空列表
                if (MessageConfigs == null)
                {
                    MessageConfigs = new ObservableCollection<MessageConfigItem>();
                }
                else
                {
                    MessageConfigs.Clear();
                }
            }
            
            // 加载RT模式配置
            RTAddress = channelConfig.RTAddress;
            SubAddress = channelConfig.SubAddress;
            RTResponseTime = channelConfig.RTResponseTime;
            RTTxMode = channelConfig.RTTxMode;
            
            // 加载RT配置列表
            if (RTConfigs == null)
            {
                RTConfigs = new ObservableCollection<RTConfigItem>();
            }
            else
            {
                RTConfigs.Clear();
            }
            
            if (channelConfig.RTConfigs != null && channelConfig.RTConfigs.Any())
            {
                // 从保存的配置恢复RT列表
                foreach (var rt in channelConfig.RTConfigs)
                {
                    RTConfigs.Add(new RTConfigItem
                    {
                        RTAddress = rt.RTAddress,
                        SubAddress = rt.SubAddress,
                        ResponseTime = rt.ResponseTime,
                        IsEnabled = rt.IsEnabled,
                        TxMode = rt.TxMode
                    });
                }
                
                // 确保有完整的0-31个RT（如果保存的配置不完整）
                for (int i = 0; i <= 31; i++)
                {
                    var existingRT = RTConfigs.FirstOrDefault(rt => rt.RTAddress == i);
                    if (existingRT == null)
                    {
                        RTConfigs.Add(new RTConfigItem
                        {
                            RTAddress = i,
                            SubAddress = 1,
                            ResponseTime = 500,
                            IsEnabled = false,
                            TxMode = "SingleBuffer"
                        });
                    }
                }
                
                // 按RT地址排序
                var sortedRTs = RTConfigs.OrderBy(rt => rt.RTAddress).ToList();
                RTConfigs.Clear();
                foreach (var rt in sortedRTs)
                {
                    RTConfigs.Add(rt);
                }
            }
            else
            {
                // 如果没有保存的配置，初始化完整的0-31个RT配置
                InitializeRTConfigs();
            }
            
            // 加载RT内部子地址配置
            if (channelConfig.RTSubAddressConfigs != null && channelConfig.RTSubAddressConfigs.Any())
            {
                if (RTSubAddressConfigs == null)
                {
                    RTSubAddressConfigs = new ObservableCollection<RTSubAddressConfigItem>();
                }
                else
                {
                    RTSubAddressConfigs.Clear();
                }
                foreach (var sub in channelConfig.RTSubAddressConfigs)
                {
                    RTSubAddressConfigs.Add(new RTSubAddressConfigItem
                    {
                        SubAddress = sub.SubAddress,
                        ReceiveEnabled = sub.ReceiveEnabled,
                        TransmitEnabled = sub.TransmitEnabled,
                        DataLength = sub.DataLength,
                        SendDataHex = sub.SendDataHex
                    });
                }
            }
            else
            {
                // 如果没有保存的配置，初始化默认子地址配置
                if (RTSubAddressConfigs == null || RTSubAddressConfigs.Count == 0)
                {
                    InitializeRTSubAddressConfigs();
                }
            }
            
            // 更新选中RT配置
            if (RTConfigs != null && RTConfigs.Any())
            {
                SelectedRTConfig = RTConfigs.FirstOrDefault(rt => rt.IsEnabled) ?? RTConfigs.FirstOrDefault();
            }
        }

        /// <summary>
        /// BC响应超时（0.5us单位）
        /// </summary>
        public ushort BCResponseTimeout
        {
            get => _bcResponseTimeout;
            set
            {
                if (SetProperty(ref _bcResponseTimeout, value))
                {
                    MarkDirty();
                }
            }
        }

        /// <summary>
        /// BC帧间隔（1us单位）
        /// </summary>
        public int BCFrameGap
        {
            get => _bcFrameGap;
            set
            {
                if (SetProperty(ref _bcFrameGap, value))
                {
                    MarkDirty();
                }
            }
        }

        /// <summary>
        /// BC重试次数
        /// </summary>
        public int BCRetryCount
        {
            get => _bcRetryCount;
            set
            {
                if (SetProperty(ref _bcRetryCount, value))
                {
                    MarkDirty();
                }
            }
        }

        /// <summary>
        /// BC发送间隔（毫秒）- 参考官方例程，循环发送时的间隔
        /// </summary>
        public int BCSendInterval
        {
            get => _bcSendInterval;
            set
            {
                if (value < 1) value = 1;
                if (value > 10000) value = 10000;
                if (SetProperty(ref _bcSendInterval, value))
                {
                    MarkDirty();
                }
            }
        }

        /// <summary>
        /// BC发送统计（当前通道）
        /// </summary>
        public ulong BCSendCount => _bcSendCountByChannel.TryGetValue(SelectedChannel, out var count) ? count : 0;

        /// <summary>
        /// RT接收统计（当前通道）
        /// </summary>
        public ulong RTReceiveCount => _rtReceiveCountByChannel.TryGetValue(SelectedChannel, out var count) ? count : 0;

        /// <summary>
        /// RT错误统计（当前通道）
        /// </summary>
        public ulong RTErrorCount => _rtErrorCountByChannel.TryGetValue(SelectedChannel, out var count) ? count : 0;

        /// <summary>
        /// BC发送目标RT地址（0-31）
        /// </summary>
        public int BCTargetRTAddress
        {
            get => _bcTargetRTAddress;
            set
            {
                if (value < 0) value = 0;
                if (value > 31) value = 31;
                if (SetProperty(ref _bcTargetRTAddress, value))
                {
                    MarkDirty();
                }
            }
        }

        /// <summary>
        /// BC发送目标子地址（1-30）
        /// </summary>
        public int BCTargetSubAddress
        {
            get => _bcTargetSubAddress;
            set
            {
                if (value < 1) value = 1;
                if (value > 30) value = 30;
                if (SetProperty(ref _bcTargetSubAddress, value))
                {
                    MarkDirty();
                }
            }
        }

        /// <summary>
        /// RT响应时间（0.5us单位）
        /// </summary>
        public ushort RTResponseTime
        {
            get => _rtResponseTime;
            set
            {
                if (SetProperty(ref _rtResponseTime, value))
                {
                    // 更新选中的RT配置
                    if (SelectedRTConfig != null)
                    {
                        SelectedRTConfig.ResponseTime = value;
                    }
                    MarkDirty();
                }
            }
        }

        /// <summary>
        /// RT使能
        /// </summary>
        public bool RTEnabled
        {
            get => _rtEnabled;
            set
            {
                if (SetProperty(ref _rtEnabled, value))
                {
                    // 更新选中的RT配置
                    if (SelectedRTConfig != null)
                    {
                        SelectedRTConfig.IsEnabled = value;
                    }
                    MarkDirty();
                }
            }
        }

        /// <summary>
        /// RT发送模式候选项
        /// </summary>
        public List<string> RTTxModes { get; } = new List<string>
        {
            "SingleBuffer",
            "CircularBuffer"
        };

        /// <summary>
        /// RT发送模式
        /// </summary>
        public string RTTxMode
        {
            get => _rtTxMode;
            set
            {
                if (SetProperty(ref _rtTxMode, value))
                {
                    // 更新选中的RT配置
                    if (SelectedRTConfig != null)
                    {
                        SelectedRTConfig.TxMode = value;
                    }
                    MarkDirty();
                }
            }
        }

        /// <summary>
        /// RT配置列表（0-31个RT）
        /// </summary>
        public ObservableCollection<RTConfigItem> RTConfigs
        {
            get => _rtConfigs;
            set => SetProperty(ref _rtConfigs, value);
        }

        /// <summary>
        /// 选中的RT配置
        /// </summary>
        /// <summary>
        /// RT内部子地址配置列表（1-30）
        /// </summary>
        public ObservableCollection<RTSubAddressConfigItem> RTSubAddressConfigs
        {
            get => _rtSubAddressConfigs;
            set => SetProperty(ref _rtSubAddressConfigs, value);
        }

        /// <summary>
        /// 当前右键选中用于配置的RT
        /// </summary>
        private RTConfigItem _selectedRTForConfig;
        public RTConfigItem SelectedRTForConfig
        {
            get => _selectedRTForConfig;
            set
            {
                if (SetProperty(ref _selectedRTForConfig, value))
                {
                    RaisePropertyChanged(nameof(HasSelectedRTForConfig));
                    RaisePropertyChanged(nameof(SelectedRTSubAddressConfigs));
                    RaisePropertyChanged(nameof(SelectedRTForConfigWithChannel));
                }
            }
        }

        /// <summary>
        /// 用于 UI 显示：包含当前通道与选中 RT 名称的组合文本
        /// </summary>
        public string SelectedRTForConfigWithChannel
        {
            get
            {
                if (SelectedRTForConfig == null)
                    return "未选择";
                return $"通道{SelectedChannel} - {SelectedRTForConfig.DisplayName}";
            }
        }

        /// <summary>
        /// 是否有选中的RT用于配置
        /// </summary>
        public bool HasSelectedRTForConfig => SelectedRTForConfig != null;

        /// <summary>
        /// 当前选中RT的子地址配置（每个通道的每个RT独立的子地址配置）
        /// 参考官方例程：每个RT有0-31个子地址
        /// </summary>
        // Keyed by channel -> (rtAddr -> subaddress configs)
        private Dictionary<int, Dictionary<int, ObservableCollection<RTSubAddressConfigItem>>> _rtSubAddressConfigsMapByChannel =
            new Dictionary<int, Dictionary<int, ObservableCollection<RTSubAddressConfigItem>>>();
        
        public ObservableCollection<RTSubAddressConfigItem> SelectedRTSubAddressConfigs
        {
            get
            {
                if (SelectedRTForConfig == null)
                    return null;

                int channel = SelectedChannel;
                int rtAddr = SelectedRTForConfig.RTAddress;

                if (!_rtSubAddressConfigsMapByChannel.ContainsKey(channel))
                {
                    _rtSubAddressConfigsMapByChannel[channel] = new Dictionary<int, ObservableCollection<RTSubAddressConfigItem>>();
                }

                var channelMap = _rtSubAddressConfigsMapByChannel[channel];
                if (!channelMap.ContainsKey(rtAddr))
                {
                    // 如果当前 ViewModel 的 RTSubAddressConfigs（加载自通道配置）存在，使用其内容作为该通道该RT的初始值（深拷贝）
                    if (RTSubAddressConfigs != null && RTSubAddressConfigs.Any())
                    {
                        var copied = new ObservableCollection<RTSubAddressConfigItem>(
                            RTSubAddressConfigs.Select(s => new RTSubAddressConfigItem
                            {
                                SubAddress = s.SubAddress,
                                ReceiveEnabled = true, // 默认开启，UI不可修改
                                TransmitEnabled = true,
                                DataLength = s.DataLength,
                                SendDataHex = s.SendDataHex
                            }));
                        channelMap[rtAddr] = copied;
                    }
                    else
                    {
                        // 为该通道/RT初始化默认子地址配置
                        InitializeRTSubAddressConfigsForRT(channel, rtAddr);
                    }
                }

                return channelMap[rtAddr];
            }
        }

        /// <summary>
        /// 右键选择RT进行配置的命令
        /// </summary>
        public ICommand SelectRTForConfigCommand { get; private set; }

        public RTConfigItem SelectedRTConfig
        {
            get => _selectedRTConfig;
            set
            {
                if (SetProperty(ref _selectedRTConfig, value))
                {
                    // 更新当前RT地址和子地址
                    if (value != null)
                    {
                        RTAddress = value.RTAddress;
                        SubAddress = value.SubAddress;
                        RTResponseTime = value.ResponseTime;
                        RTEnabled = value.IsEnabled;
                        RTTxMode = value.TxMode;
                    }
                }
            }
        }

        /// <summary>
        /// 传输速率候选项
        /// </summary>
        public List<string> TransmissionRates { get; } = new List<string>
        {
            "1 Mbps",
            "2 Mbps",
            "4 Mbps"
        };

        /// <summary>
        /// 选中的传输速率
        /// </summary>
        public string SelectedTransmissionRate
        {
            get => _selectedTransmissionRate;
            set
            {
                if (SetProperty(ref _selectedTransmissionRate, value))
                {
                    MarkDirty();
                }
            }
        }

        /// <summary>
        /// 消息配置列表（当前选中总线接口的）
        /// </summary>
        public ObservableCollection<MessageConfigItem> MessageConfigs
        {
            get => _messageConfigs;
            set => SetProperty(ref _messageConfigs, value);
        }

        /// <summary>
        /// 通道配置列表（当前选中总线接口的）
        /// </summary>
        public ObservableCollection<ChannelConfigItem> ChannelConfigs
        {
            get => _channelConfigs;
            set => SetProperty(ref _channelConfigs, value);
        }

        /// <summary>
        /// 设备是否已连接
        /// </summary>
        public bool IsDeviceConnected
        {
            get => _isDeviceConnected;
            set
            {
                if (SetProperty(ref _isDeviceConnected, value))
                {
                    (ApplyRTModeCommand as DelegateCommand)?.RaiseCanExecuteChanged();
                    // SendDataCommand已移除
                    // (SendDataCommand as DelegateCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        // BM模式监控消息属性

        /// <summary>
        /// BM监控消息列表
        /// </summary>
        public ObservableCollection<BMMessageItem> BMMessages
        {
            get => _bmMessages;
            set => SetProperty(ref _bmMessages, value);
        }

        /// <summary>
        /// BM过滤RT地址（-1表示不过滤）
        /// </summary>
        public int BMFilterRTAddress
        {
            get => _bmFilterRTAddress;
            set
            {
                if (SetProperty(ref _bmFilterRTAddress, value))
                {
                    FilterBMMessages();
                }
            }
        }

        /// <summary>
        /// BM过滤消息类型候选项
        /// </summary>
        public List<string> BMFilterMessageTypes { get; } = new List<string>
        {
            "全部",
            "BC->RT",
            "RT->BC",
            "RT->RT",
            "广播",
            "模式码"
        };

        /// <summary>
        /// BC消息类型列表（用于消息编辑）
        /// </summary>
        public List<string> MessageTypes { get; } = new List<string>
        {
            "BC->RT",
            "RT->BC",
            "RT->RT",
            "Mode Code",
            "Broadcast",
            "RT->RTs",
            "Broadcast Mode Code"
        };

        /// <summary>
        /// BM过滤消息类型
        /// </summary>
        public string BMFilterMessageType
        {
            get => _bmFilterMessageType;
            set
            {
                if (SetProperty(ref _bmFilterMessageType, value))
                {
                    FilterBMMessages();
                }
            }
        }

        /// <summary>
        /// BM总消息数
        /// </summary>
        public int BMTotalMessageCount
        {
            get => _bmTotalMessageCount;
            set => SetProperty(ref _bmTotalMessageCount, value);
        }

        /// <summary>
        /// BM错误消息数
        /// </summary>
        public int BMErrorMessageCount
        {
            get => _bmErrorMessageCount;
            set => SetProperty(ref _bmErrorMessageCount, value);
        }

        /// <summary>
        /// BM自动滚动
        /// </summary>
        public bool BMAutoScroll
        {
            get => _bmAutoScroll;
            set => SetProperty(ref _bmAutoScroll, value);
        }

        public int RTAddress
        {
            get => _rtAddress;
            set
            {
                if (SetProperty(ref _rtAddress, value))
                {
                    // 更新选中的RT配置
                    if (SelectedRTConfig != null)
                    {
                        SelectedRTConfig.RTAddress = value;
                    }
                    MarkDirty();
                }
            }
        }

        public int SubAddress
        {
            get => _subAddress;
            set
            {
                if (SetProperty(ref _subAddress, value))
                {
                    // 更新选中的RT配置
                    if (SelectedRTConfig != null)
                    {
                        SelectedRTConfig.SubAddress = value;
                    }
                    MarkDirty();
                }
            }
        }

        public int SendByteCount
        {
            get => _sendByteCount;
            set => SetProperty(ref _sendByteCount, value);
        }

        public string SendDataHex
        {
            get => _sendDataHex;
            set => SetProperty(ref _sendDataHex, value);
        }

        public int ReceivedByteCount
        {
            get => _receivedByteCount;
            private set => SetProperty(ref _receivedByteCount, value);
        }

        public string ReceivedDataHex
        {
            get => _receivedDataHex;
            private set => SetProperty(ref _receivedDataHex, value);
        }

        /// <summary>
        /// BC运行状态（当前通道）
        /// </summary>
        public bool IsBCRunning
        {
            get => _isBCRunning;
            set => SetProperty(ref _isBCRunning, value);
        }

        /// <summary>
        /// RT运行状态（当前通道）
        /// </summary>
        public bool IsRTRunning
        {
            get => _isRTRunning;
            set => SetProperty(ref _isRTRunning, value);
        }

        /// <summary>
        /// BM运行状态（当前通道）
        /// </summary>
        public bool IsBMRunning
        {
            get => _isBMRunning;
            set => SetProperty(ref _isBMRunning, value);
        }

        /// <summary>
        /// 连接状态文本
        /// </summary>
        public string ConnectionStatus
        {
            get => _connectionStatus;
            set => SetProperty(ref _connectionStatus, value);
        }

        /// <summary>
        /// 是否存在未保存的更改
        /// </summary>
        public bool HasPendingChanges
        {
            get => _hasPendingChanges;
            private set
            {
                if (SetProperty(ref _hasPendingChanges, value))
                {
                    (SaveConfigCommand as DelegateCommand)?.RaiseCanExecuteChanged();
                    (ReloadConfigCommand as DelegateCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        #endregion

        #region Commands

        public ICommand SaveConfigCommand { get; }
        public ICommand ReloadConfigCommand { get; }
        public ICommand ToggleDeviceCommand { get; }
        public ICommand AddMessageCommand { get; }
        public ICommand EditMessageCommand { get; }
        public ICommand RemoveMessageCommand { get; }
        
        // BC消息列表相关属性
        private MessageConfigItem _selectedMessage;
        
        /// <summary>
        /// 选中的消息（用于编辑）
        /// </summary>
        public MessageConfigItem SelectedMessage
        {
            get => _selectedMessage;
            set
            {
                if (SetProperty(ref _selectedMessage, value))
                {
                    // 当SelectedMessage变化时，更新命令的可执行状态
                    ((Prism.Commands.DelegateCommand)EditMessageCommand)?.RaiseCanExecuteChanged();
                    ((Prism.Commands.DelegateCommand)RemoveMessageCommand)?.RaiseCanExecuteChanged();
                }
            }
        }
        public ICommand StartTestCommand { get; }
        public ICommand StopTestCommand { get; }
        public ICommand SelectAllBusInterfacesCommand { get; }
        public ICommand DeselectAllBusInterfacesCommand { get; }
        public ICommand ApplyRTModeCommand { get; }
        public ICommand BCRunCommand { get; }
        public ICommand RTRunCommand { get; }
        public ICommand BMRunCommand { get; }
        public ICommand BCStopCommand { get; }
        public ICommand RTStopCommand { get; }
        public ICommand BMStopCommand { get; }
        public ICommand AddRTConfigCommand { get; }
        public ICommand RemoveRTConfigCommand { get; }
        public ICommand SelectAllRTCommand { get; }
        public ICommand DeselectAllRTCommand { get; }
        public ICommand ToggleRTEnableCommand { get; }
        public ICommand ClearBMMessagesCommand { get; }

        #endregion

        #region Constructor

        public ART1553BConfigPanelViewModel()
        {
            BusInterfaces = new ObservableCollection<Mil1553BBusInterface>();
            MessageConfigs = new ObservableCollection<MessageConfigItem>();
            ChannelConfigs = new ObservableCollection<ChannelConfigItem>();

            SaveConfigCommand = new DelegateCommand(SaveConfig, () => HasPendingChanges);
            ReloadConfigCommand = new DelegateCommand(ReloadConfig, () => HasPendingChanges);
            ToggleDeviceCommand = new DelegateCommand(async () => await ToggleDeviceAsync());
            AddMessageCommand = new DelegateCommand(AddMessageConfig);
            EditMessageCommand = new DelegateCommand(() => EditMessageConfig(SelectedMessage), () => SelectedMessage != null);
            RemoveMessageCommand = new DelegateCommand(() => RemoveMessageConfig(SelectedMessage), () => SelectedMessage != null);
            StartTestCommand = new DelegateCommand(async () => await StartTestAsync(), () => IsDeviceConnected && !_isTesting);
            StopTestCommand = new DelegateCommand(async () => await StopTestAsync(), () => _isTesting);
            SelectAllBusInterfacesCommand = new DelegateCommand(SelectAllBusInterfaces);
            DeselectAllBusInterfacesCommand = new DelegateCommand(DeselectAllBusInterfaces);
            ApplyRTModeCommand = new DelegateCommand(async () => await ApplyRTModeAsync(), () => IsDeviceConnected);
            // BC/RT/BM运行命令（参考官方例程）
            // BC/RT/BM运行/停止命令 - 使用带通道参数的版本，确保每个Tab独立操作
            BCRunCommand = new DelegateCommand<object>(async (param) => await BCRunAsync(GetChannelFromParam(param)), (param) => IsDeviceConnected && IsBCMode && !GetChannelBCRunning(GetChannelFromParam(param)));
            RTRunCommand = new DelegateCommand<object>(async (param) => await RTRunAsync(GetChannelFromParam(param)), (param) => IsDeviceConnected && IsRTMode && !GetChannelRTRunning(GetChannelFromParam(param)));
            BMRunCommand = new DelegateCommand<object>(async (param) => await BMRunAsync(GetChannelFromParam(param)), (param) => IsDeviceConnected && IsBMMode && !GetChannelBMRunning(GetChannelFromParam(param)));
            BCStopCommand = new DelegateCommand<object>(async (param) => await BCStopAsync(GetChannelFromParam(param)), (param) => IsDeviceConnected && IsBCMode && GetChannelBCRunning(GetChannelFromParam(param)));
            RTStopCommand = new DelegateCommand<object>(async (param) => await RTStopAsync(GetChannelFromParam(param)), (param) => IsDeviceConnected && IsRTMode && GetChannelRTRunning(GetChannelFromParam(param)));
            BMStopCommand = new DelegateCommand<object>(async (param) => await BMStopAsync(GetChannelFromParam(param)), (param) => IsDeviceConnected && IsBMMode && GetChannelBMRunning(GetChannelFromParam(param)));
            AddRTConfigCommand = new DelegateCommand(AddRTConfig);
            RemoveRTConfigCommand = new DelegateCommand<RTConfigItem>(RemoveRTConfig);
            SelectAllRTCommand = new DelegateCommand(SelectAllRT);
            DeselectAllRTCommand = new DelegateCommand(DeselectAllRT);
            ToggleRTEnableCommand = new DelegateCommand<RTConfigItem>(ToggleRTEnable);
            SelectRTForConfigCommand = new DelegateCommand<RTConfigItem>(SelectRTForConfig);
            ClearBMMessagesCommand = new DelegateCommand(ClearBMMessages);

            _connectionStatus = "离线";
            
            // 初始化BM消息列表
            BMMessages = new ObservableCollection<BMMessageItem>();
            
            // 初始化模式显示
            UpdateModeVisibility();
            
            // 初始化时设置CanSendData为false（默认状态）
            CanSendData = false;
        }

        /// <summary>
        /// 使用指定的设备初始化ViewModel
        /// </summary>
        public ART1553BConfigPanelViewModel(DeviceBase device, string chassisName,
            IPxiChassisService pxiChassisService = null, IEventAggregator eventAggregator = null,
            ProjectService projectService = null) : this()
        {
            Device = device;
            ChassisName = chassisName;
            CardModel = device?.Model ?? "";
            CardName = !string.IsNullOrEmpty(device?.CardName) ? device.CardName : device?.Model ?? "";
            _pxiChassisService = pxiChassisService;
            _eventAggregator = eventAggregator;
            _projectService = projectService;

            // 初始化总线接口列表
            InitializeBusInterfaces();

            // 初始化默认配置
            InitializeMessageConfigs();
            InitializeChannelConfigs();
            InitializeRTConfigs();
            InitializeRTSubAddressConfigs();

            // 加载测试任务选项
            LoadTestTaskOptions();

            // 加载设备配置
            LoadConfigFromDevice();

            // 订阅测试任务创建事件
            if (_eventAggregator != null)
            {
                _testTaskCreatedToken = _eventAggregator.GetEvent<TestTaskCreatedEvent>()?.Subscribe(OnTestTaskCreated);
            }
        }

        /// <summary>
        /// 处理测试任务创建事件，更新可用测试任务列表
        /// </summary>
        private void OnTestTaskCreated(ProjectItem testTask)
        {
            LoadTestTaskOptions();
        }

        /// <summary>
        /// 改变选中的测试任务
        /// </summary>
        private void ChangeSelectedTestTask(string taskName)
        {
            if (_selectedTestTask == taskName)
                return;

            if (!_isLoadingTaskOptions)
            {
                if (!EnsurePendingChangesHandled())
                {
                    RaisePropertyChanged(nameof(SelectedTestTask));
                    return;
                }
            }

            _selectedTestTask = taskName;
            RaisePropertyChanged(nameof(SelectedTestTask));
            (SaveConfigCommand as DelegateCommand)?.RaiseCanExecuteChanged();
            (ReloadConfigCommand as DelegateCommand)?.RaiseCanExecuteChanged();
            if (!_isLoadingTaskOptions)
            {
                LoadConfigForTask(taskName);
            }
        }

        /// <summary>
        /// 加载测试任务选项
        /// </summary>
        private void LoadTestTaskOptions()
        {
            _isLoadingTaskOptions = true;
            try
            {
                AvailableTestTasks.Clear();
                AvailableTestTasks.Add("默认测试任务");

                string initialTask = "默认测试任务";

                _selectedTestTask = initialTask;
                RaisePropertyChanged(nameof(SelectedTestTask));
                RaisePropertyChanged(nameof(HasTestTaskOptions));
                (SaveConfigCommand as DelegateCommand)?.RaiseCanExecuteChanged();
                (ReloadConfigCommand as DelegateCommand)?.RaiseCanExecuteChanged();
                LoadConfigForTask(initialTask);
            }
            finally
            {
                _isLoadingTaskOptions = false;
            }
        }

        /// <summary>
        /// 从项目中获取测试任务名称列表
        /// </summary>
        private List<string> GetTestTaskNamesFromProject()
        {
            var result = new List<string>();

            var globalTasks = _projectService?.GetGlobalTestTaskNames();
            if (globalTasks != null && globalTasks.Count > 0)
            {
                return globalTasks;
            }

            if (_projectService?.CurrentProjectRoot?.Children == null || string.IsNullOrEmpty(ChassisName))
            {
                return result;
            }

            var chassisNode = _projectService.CurrentProjectRoot.Children
                .FirstOrDefault(c => c.Name == ChassisName && c.Type == AppConstants.NodeTypePxiChassis);
            if (chassisNode?.Children == null)
            {
                return result;
            }

            var taskConfigNode = chassisNode.Children.FirstOrDefault(c => c.Type == AppConstants.NodeTypeTaskConfig);
            if (taskConfigNode?.Children == null)
            {
                return result;
            }

            foreach (var testTask in taskConfigNode.Children.Where(c => c.Type == AppConstants.NodeTypeTestTask))
            {
                result.Add(testTask.Name);
            }

            return result;
        }

        /// <summary>
        /// 加载指定测试任务的配置
        /// </summary>
        private void LoadConfigForTask(string taskName)
        {
            var cardConfig = EnsureCardConfig();
            if (cardConfig == null)
            {
                _hasPendingChanges = false;
                RaisePropertyChanged(nameof(HasPendingChanges));
                return;
            }

            // 保存最后选中的测试任务
            cardConfig.LastSelectedTestTask = taskName;

            // 加载该测试任务的配置（如果有）
            var taskConfig = cardConfig.TestTaskConfigs?.FirstOrDefault(t => t.TestTaskName == taskName);
            if (taskConfig != null)
            {
                // 应用任务配置到当前界面
                ApplyTaskConfig(taskConfig);
            }
            else
            {
                // 如果没有该任务的配置，使用默认配置
                LoadConfigFromDevice();
            }

            _hasPendingChanges = false;
            RaisePropertyChanged(nameof(HasPendingChanges));
        }

        /// <summary>
        /// 应用任务配置到界面
        /// </summary>
        private void ApplyTaskConfig(ART1553BTestTaskConfig taskConfig)
        {
            // 这里可以根据任务配置恢复界面状态
            // 当前实现中，任务配置主要保存的是消息配置，所以这里可以恢复消息配置
            if (taskConfig.MessageConfigs != null && taskConfig.MessageConfigs.Any())
            {
                // 恢复消息配置（如果需要）
                // MessageConfigs.Clear();
                // foreach (var msg in taskConfig.MessageConfigs)
                // {
                //     MessageConfigs.Add(new MessageConfigItem { ... });
                // }
            }
        }

        /// <summary>
        /// 确保待处理更改已处理
        /// </summary>
        private bool EnsurePendingChangesHandled()
        {
            if (!HasPendingChanges)
                return true;

            var message = $"切换测试任务将放弃对 \"{SelectedTestTask}\" 的未保存修改，是否继续？";
            var result = ReMessageBox.Show(message, "提示",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                _hasPendingChanges = false;
                RaisePropertyChanged(nameof(HasPendingChanges));
                return true;
            }

            return false;
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// 初始化总线接口列表
        /// </summary>
        private void InitializeBusInterfaces()
        {
            BusInterfaces.Clear();

            if (Device == null || Device.Children == null)
                return;

            // 从设备中获取所有BusInterface子节点
            foreach (var child in Device.Children.OfType<Mil1553BBusInterface>())
            {
                BusInterfaces.Add(child);
            }

            // 默认选择第一个总线接口
            if (BusInterfaces.Any())
            {
                SelectedBusInterface = BusInterfaces.First();
            }
        }

        /// <summary>
        /// 加载选中总线接口的配置
        /// </summary>
        private void LoadConfigForSelectedBusInterface()
        {
            if (_selectedBusInterface == null)
                return;

            // 从缓存或设备加载该接口的配置
            var interfaceId = _selectedBusInterface.InterfaceNumber;

            if (_busInterfaceConfigs.TryGetValue(interfaceId, out var config))
            {
                // 从缓存加载
                SelectedTerminalMode = config.TerminalMode ?? GetWorkModeString(_selectedBusInterface.WorkMode);
                SelectedTransmissionRate = config.TransmissionRate ?? "1 Mbps";
                ChannelConfigs = new ObservableCollection<ChannelConfigItem>(config.ChannelConfigs ?? new List<ChannelConfigItem>());
                
                // 加载通道配置（先设置SelectedChannel，这会触发LoadConfigForChannel）
                if (config.SelectedChannel >= 0 && config.SelectedChannel <= 1)
                {
                    // 先初始化默认配置，然后加载保存的配置
                    _selectedChannel = config.SelectedChannel;
                    LoadConfigForChannel(config.SelectedChannel);
                }
                else
                {
                    // 如果没有保存的通道选择，默认使用通道0
                    _selectedChannel = 0;
                    LoadConfigForChannel(0);
                }
                
                // 更新模式显示
                UpdateModeVisibility();
            }
            else
            {
                // 加载默认配置
                SelectedTerminalMode = GetWorkModeString(_selectedBusInterface.WorkMode);
                SelectedTransmissionRate = "1 Mbps";
                InitializeMessageConfigs();
                InitializeChannelConfigs();
            }
        }

        /// <summary>
        /// 初始化消息配置
        /// </summary>
        private void InitializeMessageConfigs()
        {
            MessageConfigs.Clear();

            // 添加默认的一条BC->RT消息配置（索引从0开始，参考官方例程）
            MessageConfigs.Add(new MessageConfigItem
            {
                MessageId = 0, // 消息索引从0开始
                MessageName = "BC->RT消息",
                MessageType = "BC->RT",
                RTAddress = 1,
                SubAddress = 1,
                DataLength = 1, // 1个字
                ChannelSelect = 1, // Channel A
                MessageGap = 20,
                IsEnabled = true,
                DataHex = "00 00" // 默认数据
            });
        }

        /// <summary>
        /// 初始化通道配置
        /// </summary>
        private void InitializeChannelConfigs()
        {
            ChannelConfigs.Clear();

            // 1553B通常有A/B双通道
            ChannelConfigs.Add(new ChannelConfigItem
            {
                ChannelName = "通道A",
                IsEnabled = true,
                IsPrimary = true
            });

            ChannelConfigs.Add(new ChannelConfigItem
            {
                ChannelName = "通道B",
                IsEnabled = true,
                IsPrimary = false
            });
        }

        /// <summary>
        /// 初始化RT配置列表（0-31个RT，共32个RT）
        /// </summary>
        private void InitializeRTConfigs()
        {
            // 如果RTConfigs为null，创建新集合
            if (RTConfigs == null)
            {
                RTConfigs = new ObservableCollection<RTConfigItem>();
            }
            else
            {
                // 清空现有配置，确保重新初始化
                RTConfigs.Clear();
            }
            
            // 初始化0-31个RT配置（共32个RT），默认都不使能
            for (int i = 0; i <= 31; i++)
            {
                var rtConfig = new RTConfigItem
                {
                    RTAddress = i,
                    SubAddress = 1,
                    ResponseTime = 500,
                    IsEnabled = false,
                    TxMode = "SingleBuffer"
                };
                
                // 订阅IsEnabled变化事件，用于更新选中状态
                rtConfig.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(RTConfigItem.IsEnabled) && rtConfig.IsEnabled)
                    {
                        SelectedRTConfig = rtConfig;
                    }
                };
                
                RTConfigs.Add(rtConfig);
            }
            
            // 默认选中RT地址1并使其使能
            if (RTConfigs.Count >= 32 && RTConfigs.Count > 1)
            {
                SelectedRTConfig = RTConfigs[1];
                SelectedRTConfig.IsEnabled = true;
            }
            else if (RTConfigs.Count > 0)
            {
                // 如果只有RT 0，选中它
                SelectedRTConfig = RTConfigs[0];
            }
        }

        /// <summary>
        /// 初始化RT内部子地址配置列表（1-30）
        /// </summary>
        private void InitializeRTSubAddressConfigs()
        {
            RTSubAddressConfigs = new ObservableCollection<RTSubAddressConfigItem>();
            
            // 初始化1-30个子地址配置
            for (int i = 1; i <= 30; i++)
            {
                RTSubAddressConfigs.Add(new RTSubAddressConfigItem
                {
                    SubAddress = i,
                    ReceiveEnabled = false,
                    TransmitEnabled = false,
                    DataLength = 0, // 0表示32个字
                    SendDataHex = "00 00"
                });
            }
        }

        /// <summary>
        /// 为指定通道的指定RT初始化子地址配置（0-31）
        /// 参考官方例程：每个RT有独立的子地址配置
        /// </summary>
        private void InitializeRTSubAddressConfigsForRT(int channel, int rtAddress)
        {
            var configs = new ObservableCollection<RTSubAddressConfigItem>();
            
            // 初始化0-31个子地址配置（参考官方例程）
            for (int i = 0; i <= 31; i++)
            {
                configs.Add(new RTSubAddressConfigItem
                {
                    SubAddress = i,
                    ReceiveEnabled = true, // 默认全部使能（UI 已移除，后续不会修改）
                    TransmitEnabled = true,
                    DataLength = 32, // 默认32个字
                    SendDataHex = ""
                });
            }

            if (!_rtSubAddressConfigsMapByChannel.ContainsKey(channel))
            {
                _rtSubAddressConfigsMapByChannel[channel] = new Dictionary<int, ObservableCollection<RTSubAddressConfigItem>>();
            }
            _rtSubAddressConfigsMapByChannel[channel][rtAddress] = configs;
        }

        /// <summary>
        /// 右键选中RT进行内部配置
        /// </summary>
        private void SelectRTForConfig(RTConfigItem rtConfig)
        {
            if (rtConfig == null)
                return;
            
            SelectedRTForConfig = rtConfig;
            System.Diagnostics.Debug.WriteLine($"[ART1553B] 选中RT{rtConfig.RTAddress}进行内部配置");
        }

        /// <summary>
        /// 切换RT使能状态
        /// </summary>
        private void ToggleRTEnable(RTConfigItem rtConfig)
        {
            if (rtConfig != null)
            {
                rtConfig.IsEnabled = !rtConfig.IsEnabled;
                System.Diagnostics.Debug.WriteLine($"[ART1553B] 当前通道: {SelectedChannel}, RT{rtConfig.RTAddress} 使能状态: {rtConfig.IsEnabled}");
                if (rtConfig.IsEnabled)
                {
                    SelectedRTConfig = rtConfig;
                }
                MarkDirty();
            }
        }

        /// <summary>
        /// 添加RT配置（如果不存在）
        /// </summary>
        private void AddRTConfig()
        {
            // 查找第一个未使能的RT
            var availableRT = RTConfigs.FirstOrDefault(rt => !rt.IsEnabled);
            if (availableRT != null)
            {
                availableRT.IsEnabled = true;
                SelectedRTConfig = availableRT;
                MarkDirty();
            }
            else
            {
                ReMessageBox.Show("所有RT地址（0-31）都已配置", "提示",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        /// <summary>
        /// 移除RT配置（禁用）
        /// </summary>
        private void RemoveRTConfig(RTConfigItem rtConfig)
        {
            if (rtConfig != null && RTConfigs.Contains(rtConfig))
            {
                rtConfig.IsEnabled = false;
                if (SelectedRTConfig == rtConfig)
                {
                    // 选择另一个使能的RT，如果没有则选择第一个
                    SelectedRTConfig = RTConfigs.FirstOrDefault(rt => rt.IsEnabled) ?? RTConfigs.FirstOrDefault();
                }
                MarkDirty();
            }
        }

        /// <summary>
        /// 全选RT（使能所有RT）
        /// </summary>
        private void SelectAllRT()
        {
            foreach (var rt in RTConfigs)
            {
                rt.IsEnabled = true;
            }
            MarkDirty();
        }

        /// <summary>
        /// 取消全选RT（禁用所有RT）
        /// </summary>
        private void DeselectAllRT()
        {
            foreach (var rt in RTConfigs)
            {
                rt.IsEnabled = false;
            }
            SelectedRTConfig = RTConfigs.FirstOrDefault();
            MarkDirty();
        }

        /// <summary>
        /// 从设备加载已保存的配置
        /// </summary>
        private void LoadConfigFromDevice()
        {
            if (Device == null) return;

            // 加载设备全局配置
            if (!string.IsNullOrEmpty(Device.Name))
            {
                _cardName = Device.Name;
                RaisePropertyChanged(nameof(CardName));
            }

            // 加载每个总线接口的配置
            foreach (var busInterface in BusInterfaces)
            {
                LoadBusInterfaceConfig(busInterface);
            }

            // 加载当前选中接口的配置
            if (SelectedBusInterface != null)
            {
                LoadConfigForSelectedBusInterface();
            }
        }

        /// <summary>
        /// 加载单个总线接口的配置
        /// </summary>
        private void LoadBusInterfaceConfig(Mil1553BBusInterface busInterface)
        {
            var interfaceId = busInterface.InterfaceNumber;

            // 检查是否有保存的配置
            var cardConfig = Device.CardConfigData as CardConfigDataBase;
            if (cardConfig is ART1553BCardConfig art1553bCardConfig)
            {
                // 优先从当前选中的测试任务加载配置
                if (art1553bCardConfig.TestTaskConfigs?.Any() == true && !string.IsNullOrEmpty(SelectedTestTask))
                {
                    // 查找当前测试任务的配置
                    var taskConfig = art1553bCardConfig.TestTaskConfigs
                        .FirstOrDefault(t => t.TestTaskName == SelectedTestTask);

                    if (taskConfig != null)
                    {
                        var config = new BusInterfaceConfig
                        {
                            TerminalMode = art1553bCardConfig.TerminalMode ?? GetWorkModeString(busInterface.WorkMode),
                            TransmissionRate = art1553bCardConfig.TransmissionRate ?? "1 Mbps",
                            ChannelConfigs = new List<ChannelConfigItem>(),
                            SelectedChannel = 0, // 默认通道0
                            Channel0Config = new ChannelConfig(),
                            Channel1Config = new ChannelConfig()
                        };

                        // 加载消息配置到通道0（默认通道）
                        if (taskConfig.MessageConfigs != null)
                        {
                            foreach (var msgConfig in taskConfig.MessageConfigs)
                            {
                                config.Channel0Config.MessageConfigs.Add(new MessageConfigItem
                                {
                                    MessageId = msgConfig.MessageId,
                                    MessageName = msgConfig.MessageName ?? $"Msg-{msgConfig.MessageId}",
                                    MessageType = msgConfig.MessageType ?? "BC->RT",
                                    RTAddress = msgConfig.RTAddress,
                                    SubAddress = msgConfig.SubAddress,
                                    DataLength = msgConfig.DataLength,
                                    IsEnabled = msgConfig.IsEnabled,
                                    MessageGap = msgConfig.MessageGap > 0 ? msgConfig.MessageGap : 4,
                                    ChannelSelect = msgConfig.ChannelSelect >= 0 ? msgConfig.ChannelSelect : 1,
                                    RetryEnable = msgConfig.RetryEnable,
                                    ModeCode = msgConfig.ModeCode,
                                    RTAddress2 = msgConfig.RTAddress2,
                                    SubAddress2 = msgConfig.SubAddress2,
                                    DataHex = msgConfig.DataHex ?? "00 00"
                                });
                            }
                        }
                        
                        // 初始化通道0的BC和RT配置
                        config.Channel0Config.BCResponseTimeout = 4000;
                        config.Channel0Config.BCFrameGap = 10;
                        config.Channel0Config.BCRetryCount = 0;
                        config.Channel0Config.BCTargetRTAddress = 1;
                        config.Channel0Config.BCTargetSubAddress = 1;
                        config.Channel0Config.RTAddress = 1;
                        config.Channel0Config.SubAddress = 1;
                        config.Channel0Config.RTResponseTime = 500;
                        config.Channel0Config.RTTxMode = "SingleBuffer";
                        
                        // 初始化通道1的默认配置
                        config.Channel1Config.BCResponseTimeout = 4000;
                        config.Channel1Config.BCFrameGap = 10;
                        config.Channel1Config.BCRetryCount = 0;
                        config.Channel1Config.BCTargetRTAddress = 1;
                        config.Channel1Config.BCTargetSubAddress = 1;
                        config.Channel1Config.RTAddress = 1;
                        config.Channel1Config.SubAddress = 1;
                        config.Channel1Config.RTResponseTime = 500;
                        config.Channel1Config.RTTxMode = "SingleBuffer";

                        // 加载通道配置
                        if (taskConfig.ChannelConfigs != null)
                        {
                            foreach (var chConfig in taskConfig.ChannelConfigs)
                            {
                                config.ChannelConfigs.Add(new ChannelConfigItem
                                {
                                    ChannelName = chConfig.ChannelName,
                                    IsEnabled = chConfig.IsEnabled,
                                    IsPrimary = chConfig.IsPrimary
                                });
                            }
                        }

                        _busInterfaceConfigs[interfaceId] = config;
                        return;
                    }
                }
                
                // 如果没有测试任务配置，使用全局配置
                if (!string.IsNullOrEmpty(art1553bCardConfig.TerminalMode))
                {
                    var config = new BusInterfaceConfig
                    {
                        TerminalMode = art1553bCardConfig.TerminalMode,
                        TransmissionRate = art1553bCardConfig.TransmissionRate ?? "1 Mbps",
                        ChannelConfigs = new List<ChannelConfigItem>(),
                        SelectedChannel = 0,
                        Channel0Config = new ChannelConfig
                        {
                            BCResponseTimeout = 4000,
                            BCFrameGap = 10,
                            BCRetryCount = 0,
                            BCTargetRTAddress = 1,
                            BCTargetSubAddress = 1,
                            RTAddress = 1,
                            SubAddress = 1,
                            RTResponseTime = 500,
                            RTTxMode = "SingleBuffer"
                        },
                        Channel1Config = new ChannelConfig
                        {
                            BCResponseTimeout = 4000,
                            BCFrameGap = 10,
                            BCRetryCount = 0,
                            BCTargetRTAddress = 1,
                            BCTargetSubAddress = 1,
                            RTAddress = 1,
                            SubAddress = 1,
                            RTResponseTime = 500,
                            RTTxMode = "SingleBuffer"
                        }
                    };
                    
                    _busInterfaceConfigs[interfaceId] = config;
                }
            }
        }

        /// <summary>
        /// 重新加载配置（读取配置）
        /// </summary>
        private void ReloadConfig()
        {
            if (HasPendingChanges)
            {
                var confirm = ReMessageBox.Show("存在未保存的更改，重新加载将丢失当前修改，是否继续？",
                    "提示",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Question);

                if (confirm != System.Windows.MessageBoxResult.Yes)
                    return;
            }

            try
            {
                // 重新加载所有配置
                LoadConfigFromDevice();
                
                // 如果有选中的测试任务，加载该任务的配置
                if (!string.IsNullOrEmpty(SelectedTestTask))
                {
                    LoadConfigForTask(SelectedTestTask);
                }
                
                // 加载当前选中接口的配置
                if (SelectedBusInterface != null)
                {
                    LoadConfigForSelectedBusInterface();
                }
                
                HasPendingChanges = false;
                
                // 显示成功消息
                ReMessageBox.Show("配置读取成功", "提示",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                ReMessageBox.Show($"读取配置失败: {ex.Message}", "错误",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 获取或创建测试任务配置
        /// </summary>
        private ART1553BTestTaskConfig GetOrCreateTaskConfig(ART1553BCardConfig cardConfig, string taskName)
        {
            taskName ??= string.Empty;
            var config = cardConfig.TestTaskConfigs?.FirstOrDefault(c => c.TestTaskName == taskName);
            if (config == null)
            {
                config = new ART1553BTestTaskConfig { TestTaskName = taskName };
                if (cardConfig.TestTaskConfigs == null)
                    cardConfig.TestTaskConfigs = new ObservableCollection<ART1553BTestTaskConfig>();
                cardConfig.TestTaskConfigs.Add(config);
            }
            return config;
        }

        /// <summary>
        /// 保存配置到设备
        /// </summary>
        private void SaveConfig()
        {
            if (Device == null) return;

            // 保存当前接口配置到缓存
            if (SelectedBusInterface != null)
            {
                SaveCurrentInterfaceConfig();
            }

            // 保存所有接口配置到设备
            var cardConfig = EnsureCardConfig();
            if (cardConfig == null) return;

            // 保存基本配置
            cardConfig.CardId = Device.Id;
            cardConfig.CardName = CardName;
            cardConfig.CardModel = CardModel;
            cardConfig.ChassisName = ChassisName;
            cardConfig.TerminalMode = SelectedTerminalMode; // 保存当前选中接口的模式作为默认
            cardConfig.TransmissionRate = SelectedTransmissionRate; // 保存当前选中接口的速率作为默认

            // 清空旧的测试任务配置
            cardConfig.TestTaskConfigs.Clear();

            // 为每个总线接口保存独立的配置
            foreach (var busInterface in BusInterfaces)
            {
                var interfaceId = busInterface.InterfaceNumber;
                if (_busInterfaceConfigs.TryGetValue(interfaceId, out var config))
                {
                    var testTaskConfig = new ART1553BTestTaskConfig
                    {
                        TestTaskName = $"BusInterface{interfaceId}"
                    };

                    // 保存消息配置（保存当前选中通道的消息配置，保持向后兼容）
                    // 注意：这里只保存当前选中通道的消息配置
                    var currentChannelConfig = config.SelectedChannel == 0 ? config.Channel0Config : config.Channel1Config;
                    if (currentChannelConfig?.MessageConfigs != null)
                    {
                        foreach (var msg in currentChannelConfig.MessageConfigs)
                        {
                            testTaskConfig.MessageConfigs.Add(new ART1553BMessageConfig
                            {
                                MessageId = msg.MessageId,
                                MessageName = msg.MessageName,
                                MessageType = msg.MessageType,
                                RTAddress = msg.RTAddress,
                                SubAddress = msg.SubAddress,
                                DataLength = msg.DataLength,
                                IsEnabled = msg.IsEnabled,
                                MessageGap = msg.MessageGap,
                                ChannelSelect = msg.ChannelSelect,
                                RetryEnable = msg.RetryEnable,
                                ModeCode = msg.ModeCode,
                                RTAddress2 = msg.RTAddress2,
                                SubAddress2 = msg.SubAddress2,
                                DataHex = msg.DataHex
                            });
                        }
                    }

                    // 保存通道配置
                    foreach (var channel in config.ChannelConfigs)
                    {
                        testTaskConfig.ChannelConfigs.Add(new ART1553BChannelConfig
                        {
                            ChannelName = channel.ChannelName,
                            IsEnabled = channel.IsEnabled,
                            IsPrimary = channel.IsPrimary
                        });
                    }

                    cardConfig.TestTaskConfigs.Add(testTaskConfig);
                }
            }

            // 更新服务
            _pxiChassisService?.UpdateDeviceCardConfig(Device.Id, cardConfig);

            // 发布事件
            _eventAggregator?.GetEvent<ProjectModifiedEvent>()?.Publish(new ProjectModifiedEventArgs
            {
                ModificationType = "1553BConfig",
                Description = $"1553B配置已更新"
            });

            HasPendingChanges = false;

            // 显示成功消息
            ReMessageBox.Show("保存成功", "提示",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
        }

        /// <summary>
        /// 保存当前接口的配置到缓存
        /// </summary>
        private void SaveCurrentInterfaceConfig()
        {
            if (SelectedBusInterface == null) return;

            // 保存当前通道的配置
            SaveCurrentChannelConfig();

            var interfaceId = SelectedBusInterface.InterfaceNumber;
            if (!_busInterfaceConfigs.ContainsKey(interfaceId))
            {
                _busInterfaceConfigs[interfaceId] = new BusInterfaceConfig();
            }
            
            var config = _busInterfaceConfigs[interfaceId];
            config.TerminalMode = SelectedTerminalMode;
            config.TransmissionRate = SelectedTransmissionRate;
            config.ChannelConfigs = ChannelConfigs.ToList();
            config.SelectedChannel = SelectedChannel;
            
            // 通道0和通道1的配置已经通过SaveCurrentChannelConfig保存
        }

        /// <summary>
        /// 确保卡片配置对象存在
        /// </summary>
        private ART1553BCardConfig EnsureCardConfig()
        {
            if (Device == null) return null;

            var cardConfig = Device.CardConfigData as ART1553BCardConfig;
            if (cardConfig == null)
            {
                cardConfig = new ART1553BCardConfig();
                Device.CardConfigData = cardConfig;
            }

            return cardConfig;
        }

        /// <summary>
        /// 标记为有更改
        /// </summary>
        private void MarkDirty()
        {
            HasPendingChanges = true;
        }

        /// <summary>
        /// 添加消息配置
        /// </summary>
        /// <summary>
        /// 添加BC消息配置（参考官方例程）
        /// </summary>
        private void AddMessageConfig()
        {
            // 消息索引从0开始（参考官方例程）
            var newId = MessageConfigs.Any() ? MessageConfigs.Max(m => m.MessageId) + 1 : 0;
            var newMessage = new MessageConfigItem
            {
                MessageId = newId,
                MessageName = $"Msg-{newId}",
                MessageType = "BC->RT", // 默认BC->RT消息
                RTAddress = 1,
                SubAddress = 1,
                DataLength = 0, // 0表示32个字
                IsEnabled = true,
                MessageGap = 4, // 最小4us
                ChannelSelect = 1, // 默认Channel A
                RetryEnable = false,
                ModeCode = 0,
                DataHex = "00 00 00 00" // 默认数据
            };
            MessageConfigs.Add(newMessage);
            SelectedMessage = newMessage; // 选中新添加的消息
            MarkDirty();
        }

        /// <summary>
        /// 编辑消息配置（打开编辑对话框）
        /// </summary>
        private void EditMessageConfig(MessageConfigItem message)
        {
            if (message == null)
            {
                ReMessageBox.Show("请先选择要编辑的消息", "提示",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }
            
            try
            {
                // 创建编辑对话框
                var dialogViewModel = new ViewModels.Dialogs.BCMessageEditDialogViewModel(message);
                var dialog = new Views.Dialogs.BCMessageEditDialog(dialogViewModel)
                {
                    Owner = Application.Current?.MainWindow
                };

                var result = dialog.ShowDialog();
                if (result == true)
                {
                    // 更新消息配置
                    dialogViewModel.UpdateMessageConfig(message);
                    SelectedMessage = message;
                    MarkDirty();
                }
            }
            catch (Exception ex)
            {
                ReMessageBox.Show($"打开编辑对话框失败: {ex.Message}", "错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 移除消息配置
        /// </summary>
        private void RemoveMessageConfig(MessageConfigItem message)
        {
            if (message != null && MessageConfigs.Contains(message))
            {
                MessageConfigs.Remove(message);
                MarkDirty();
            }
        }

        /// <summary>
        /// 选择所有总线接口
        /// </summary>
        private void SelectAllBusInterfaces()
        {
            // 这里可以实现批量选择功能
            // 例如：将所有接口的工作模式设置为相同值
            if (BusInterfaces.Any())
            {
                // 批量设置所有接口为当前选中的工作模式
                var workMode = GetWorkModeFromString(SelectedTerminalMode);
                foreach (var busInterface in BusInterfaces)
                {
                    busInterface.WorkMode = workMode;
                }
                ReMessageBox.Show($"已将{BusInterfaces.Count}个总线接口设置为{SelectedTerminalMode}模式",
                    "批量设置",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
            }
        }

        /// <summary>
        /// 取消选择所有总线接口
        /// </summary>
        private void DeselectAllBusInterfaces()
        {
            // 重置所有接口为默认工作模式
            foreach (var busInterface in BusInterfaces)
            {
                busInterface.WorkMode = Mil1553BWorkMode.BC; // 默认BC模式
            }
            // 更新当前选中的接口配置
            if (SelectedBusInterface != null)
            {
                SelectedTerminalMode = GetWorkModeString(SelectedBusInterface.WorkMode);
            }
        }

        /// <summary>
        /// 更新选中接口的工作模式
        /// </summary>
        private void UpdateWorkModeForSelectedInterface(string terminalMode)
        {
            if (_selectedBusInterface == null) return;

            // 将字符串转换为枚举
            var workMode = GetWorkModeFromString(terminalMode);
            _selectedBusInterface.WorkMode = workMode;
        }

        /// <summary>
        /// 将工作模式枚举转换为字符串
        /// </summary>
        private string GetWorkModeString(Mil1553BWorkMode workMode)
        {
            switch (workMode)
            {
                case Mil1553BWorkMode.BC: return "BC - 总线控制器";
                case Mil1553BWorkMode.RT: return "RT - 远程终端";
                case Mil1553BWorkMode.BM: return "BM - 总线监控器";
                default: return "BC - 总线控制器";
            }
        }

        /// <summary>
        /// 将字符串转换为工作模式枚举
        /// </summary>
        private Mil1553BWorkMode GetWorkModeFromString(string terminalMode)
        {
            if (string.IsNullOrEmpty(terminalMode)) return Mil1553BWorkMode.BC;

            if (terminalMode.Contains("BC")) return Mil1553BWorkMode.BC;
            if (terminalMode.Contains("RT")) return Mil1553BWorkMode.RT;
            if (terminalMode.Contains("BM")) return Mil1553BWorkMode.BM;
            return Mil1553BWorkMode.BC;
        }

        /// <summary>
        /// 切换设备连接状态
        /// </summary>
        private async Task ToggleDeviceAsync()
        {
            if (!IsDeviceConnected)
            {
                await ConnectDeviceAsync();
            }
            else
            {
                await DisconnectDeviceAsync();
            }
        }

        /// <summary>
        /// 连接设备
        /// </summary>
        private async Task ConnectDeviceAsync()
        {
            if (Device == null) return;

            try
            {
                ConnectionStatus = "连接中...";

                // 创建驱动
                _driver = DriverFactory.CreateDriver(Device);
                _ownsDriverLifecycle = true;// 标记驱动由当前ViewModel管理

                // 连接设备
                bool connected = await Task.Run(async () => await _driver.ConnectAsync());

                if (connected)
                {
                    IsDeviceConnected = true;
                    ConnectionStatus = "已连接";

                    if (_driver is ART1553BDriver art1553bDriver)
                    {
                        art1553bDriver.MessageReceived += OnDriverMessageReceived;
                    }

                    // 启动采集
                    await Task.Run(async () => await _driver.StartAcquisitionAsync());
                    
                    // 更新按钮状态
                    // ApplyModeCommand已移除
                    // (ApplyModeCommand as DelegateCommand)?.RaiseCanExecuteChanged();
                    // SendDataCommand已移除
                    // (SendDataCommand as DelegateCommand)?.RaiseCanExecuteChanged();
                }
                else
                {
                    ConnectionStatus = "连接失败";
                    ReMessageBox.Show("设备连接失败", "错误",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                ConnectionStatus = "连接异常";
                IsDeviceConnected = false;
                _driver = null;
                _ownsDriverLifecycle = false;

                ReMessageBox.Show($"设备连接异常: {ex.Message}", "错误",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 断开设备连接
        /// </summary>
        private async Task DisconnectDeviceAsync()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("[ART1553B] 开始断开设备连接...");

                // 1. 首先停止所有运行中的BC/RT/BM线程（在关闭设备前必须先停止）
                await StopAllRunningModesAsync();

                if (_driver != null && _ownsDriverLifecycle)
                {
                    if (_driver is ART1553BDriver art1553bDriver)
                    {
                        art1553bDriver.MessageReceived -= OnDriverMessageReceived;
                    }

                    // 停止采集
                    await Task.Run(async () => await _driver.StopAcquisitionAsync());

                    // 断开连接
                    await Task.Run(async () => await _driver.DisconnectAsync());
                }

                _driver = null;
                _ownsDriverLifecycle = false;
                IsDeviceConnected = false;
                ConnectionStatus = "离线";

                // 停止测试
                if (_isTesting)
                {
                    await StopTestAsync();
                }

                System.Diagnostics.Debug.WriteLine("[ART1553B] 设备连接已断开");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"断开设备连接异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 停止所有运行中的BC/RT/BM模式（在断开设备前调用）
        /// </summary>
        private async Task StopAllRunningModesAsync()
        {
            System.Diagnostics.Debug.WriteLine("[ART1553B] 停止所有运行中的模式...");

            // 停止所有通道的BC循环发送线程
            foreach (var channel in new[] { 0, 1 })
            {
                // 停止BC线程
                if (_bcLoopCts.ContainsKey(channel) && _bcLoopCts[channel] != null)
                {
                    _bcLoopCts[channel].Cancel();
                    _bcLoopCts[channel] = null;
                }
                if (_bcLoopThreads.ContainsKey(channel) && _bcLoopThreads[channel] != null)
                {
                    try
                    {
                        _bcLoopThreads[channel].Join(500); // 等待最多500ms
                    }
                    catch { }
                    _bcLoopThreads[channel] = null;
                }

                _isBCRunningByChannel[channel] = false;
                _isRTRunningByChannel[channel] = false;
                _isBMRunningByChannel[channel] = false;
            }

            // 停止驱动中的监控
            if (_driver is ART1553BDriver art1553bDriver)
            {
                try
                {
                    try { art1553bDriver.StopBM(0); } catch { }
                    try { art1553bDriver.StopBM(1); } catch { }
                    try { await art1553bDriver.StopMonitoringAsync(); } catch { }
                }
                catch { }
            }

            // 更新UI状态
            Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
            {
                RaisePropertyChanged(nameof(IsBCRunning));
                RaisePropertyChanged(nameof(IsRTRunning));
                RaisePropertyChanged(nameof(IsBMRunning));
                (BCRunCommand as DelegateCommand<object>)?.RaiseCanExecuteChanged();
                (BCStopCommand as DelegateCommand<object>)?.RaiseCanExecuteChanged();
                (RTRunCommand as DelegateCommand<object>)?.RaiseCanExecuteChanged();
                (RTStopCommand as DelegateCommand<object>)?.RaiseCanExecuteChanged();
                (BMRunCommand as DelegateCommand<object>)?.RaiseCanExecuteChanged();
                (BMStopCommand as DelegateCommand<object>)?.RaiseCanExecuteChanged();
            }));

            // 重置选中的RT配置
            SelectedRTForConfig = null;

            System.Diagnostics.Debug.WriteLine("[ART1553B] 所有运行模式已停止");
        }

        /// <summary>
        /// 开始测试
        /// </summary>
        private async Task StartTestAsync()
        {
            try
            {
                if (_driver == null || !IsDeviceConnected)
                    return;

                _isTesting = true;
                (StartTestCommand as DelegateCommand)?.RaiseCanExecuteChanged();
                (StopTestCommand as DelegateCommand)?.RaiseCanExecuteChanged();

                // 应用配置到驱动
                await ApplyConfigToDriverAsync();

                // 开始1553B测试
                // await _driver.StartTestAsync();

                System.Diagnostics.Debug.WriteLine("[ART1553B] 测试已开始");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ART1553B] 开始测试失败: {ex.Message}");
                _isTesting = false;
            }
        }

        /// <summary>
        /// 停止测试
        /// </summary>
        private async Task StopTestAsync()
        {
            try
            {
                if (_driver == null || !IsDeviceConnected)
                    return;

                _isTesting = false;
                (StartTestCommand as DelegateCommand)?.RaiseCanExecuteChanged();
                (StopTestCommand as DelegateCommand)?.RaiseCanExecuteChanged();

                // 停止1553B测试
                // await _driver.StopTestAsync();

                System.Diagnostics.Debug.WriteLine("[ART1553B] 测试已停止");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ART1553B] 停止测试失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 应用所有总线接口的配置
        /// </summary>
        private async Task ApplyConfigToDriverAsync()
        {
            if (_driver is ART1553BDriver art1553bDriver && IsDeviceConnected)
            {
                // 应用所有总线接口的配置

                foreach (var busInterface in BusInterfaces)
                {
                    if (_busInterfaceConfigs.TryGetValue(busInterface.InterfaceNumber, out var config))
                    {
                        // 应用配置到实际驱动
                        // await art1553bDriver.ConfigureBusInterfaceAsync(
                        //     busInterface.InterfaceNumber, 
                        //     config.TerminalMode, 
                        //     config.TransmissionRate);

                        System.Diagnostics.Debug.WriteLine($"[ART1553B] 配置总线接口{busInterface.InterfaceNumber}: {config.TerminalMode}, {config.TransmissionRate}");
                    }
                }
            }
        }

        /// <summary>
        /// 更新当前通道的运行状态显示
        /// </summary>
        private void UpdateChannelRunningState()
        {
            // 对于 BM：根据需求保持两个通道一致，如果任一通道处于 BM 运行，则视为 BM 运行
            IsBCRunning = _isBCRunningByChannel.ContainsKey(SelectedChannel) && _isBCRunningByChannel[SelectedChannel];
            IsRTRunning = _isRTRunningByChannel.ContainsKey(SelectedChannel) && _isRTRunningByChannel[SelectedChannel];
            IsBMRunning = _isBMRunningByChannel.Values.Any(v => v);
            
            // 刷新命令状态
            (BCRunCommand as DelegateCommand<object>)?.RaiseCanExecuteChanged();
            (BCStopCommand as DelegateCommand<object>)?.RaiseCanExecuteChanged();
            (RTRunCommand as DelegateCommand<object>)?.RaiseCanExecuteChanged();
            (RTStopCommand as DelegateCommand<object>)?.RaiseCanExecuteChanged();
            (BMRunCommand as DelegateCommand<object>)?.RaiseCanExecuteChanged();
            (BMStopCommand as DelegateCommand<object>)?.RaiseCanExecuteChanged();
        }
        
        /// <summary>
        /// 从命令参数获取通道号（0或1）
        /// </summary>
        private int GetChannelFromParam(object param)
        {
            if (param is int channel)
                return channel;
            if (param is string str && int.TryParse(str, out int ch))
                return ch;
            return SelectedChannel; // 默认使用当前选中的通道
        }
        
        /// <summary>
        /// 获取指定通道的BC运行状态
        /// </summary>
        private bool GetChannelBCRunning(int channel)
        {
            return _isBCRunningByChannel.ContainsKey(channel) && _isBCRunningByChannel[channel];
        }
        
        /// <summary>
        /// 获取指定通道的RT运行状态
        /// </summary>
        private bool GetChannelRTRunning(int channel)
        {
            return _isRTRunningByChannel.ContainsKey(channel) && _isRTRunningByChannel[channel];
        }
        
        /// <summary>
        /// 获取指定通道的BM运行状态
        /// </summary>
        private bool GetChannelBMRunning(int channel)
        {
            return _isBMRunningByChannel.ContainsKey(channel) && _isBMRunningByChannel[channel];
        }
        
        /// <summary>
        /// 更新模式显示属性
        /// </summary>
        private void UpdateModeVisibility()
        {
            if (string.IsNullOrEmpty(_selectedTerminalMode))
            {
                IsBCMode = false;
                IsRTMode = false;
                IsBMMode = false;
                CanSendData = false;
                return;
            }

            IsBCMode = _selectedTerminalMode.Contains("BC");
            IsRTMode = _selectedTerminalMode.Contains("RT") && !_selectedTerminalMode.Contains("BC");
            IsBMMode = _selectedTerminalMode.Contains("BM") && !_selectedTerminalMode.Contains("BC");
            
            // BC和RT模式可以发送数据，BM模式只能监控
            CanSendData = IsBCMode || IsRTMode;
            
            // 更新运行命令的可执行状态（带参数的命令）
            (BCRunCommand as DelegateCommand<object>)?.RaiseCanExecuteChanged();
            (RTRunCommand as DelegateCommand<object>)?.RaiseCanExecuteChanged();
            (BMRunCommand as DelegateCommand<object>)?.RaiseCanExecuteChanged();
            (BCStopCommand as DelegateCommand<object>)?.RaiseCanExecuteChanged();
            (RTStopCommand as DelegateCommand<object>)?.RaiseCanExecuteChanged();
            (BMStopCommand as DelegateCommand<object>)?.RaiseCanExecuteChanged();
        }


        /// <summary>
        /// 应用选中的模式配置
        /// </summary>
        private async Task ApplyModeAsync()
        {
            var art1553bDriver = _driver as ART1553BDriver;
            if (art1553bDriver == null || !IsDeviceConnected)
                return;

            int channel = SelectedChannel;

            try
            {
                if (IsBCMode)
                {
                    // BC模式配置
                    bool success = await art1553bDriver.ConfigureBCModeAsync(channel, BCResponseTimeout, BCFrameGap);
                    if (success)
                    {
                        ReMessageBox.Show("BC模式配置成功", "提示",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    }
                    else
                    {
                        ReMessageBox.Show("BC模式配置失败", "错误",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                    }
                }
                else if (IsRTMode)
                {
                    // RT模式配置
                    if (!UseMultiChannelRT)
                    {
                        bool success = await art1553bDriver.ConfigureRTModeAsync(channel, RTAddress, RTResponseTime);
                        if (success)
                        {
                            ReMessageBox.Show("RT模式配置成功", "提示",
                                MessageBoxButton.OK,
                                MessageBoxImage.Information);
                        }
                        else
                        {
                            ReMessageBox.Show("RT模式配置失败", "错误",
                                MessageBoxButton.OK,
                                MessageBoxImage.Error);
                        }
                    }
                    else
                    {
                        foreach (var busInterface in BusInterfaces)
                        {
                            int ch = Math.Max(0, busInterface.InterfaceNumber - 1);
                            bool setAsCurrent = ch == channel;
                            await art1553bDriver.ConfigureRTModeAsync(ch, RTAddress, 500, setAsCurrent);
                        }
                    }
                }
                else if (IsBMMode)
                {
                    // BM模式配置
                    bool success = await art1553bDriver.ConfigureBMModeAsync(channel);
                    if (success)
                    {
                        ReMessageBox.Show("BM模式配置成功", "提示",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    }
                    else
                    {
                        ReMessageBox.Show("BM模式配置失败", "错误",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                ReMessageBox.Show($"模式配置异常: {ex.Message}", "错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 配置所有使能的RT
        /// </summary>
        private async Task<bool> ConfigureAllEnabledRTsAsync(ART1553BDriver driver, int channel)
        {
            try
            {
                // 计算RT使能位掩码
                int rtEnableMask = 0;
                foreach (var rt in RTConfigs.Where(r => r.IsEnabled))
                {
                    rtEnableMask |= (1 << rt.RTAddress);
                }

                if (rtEnableMask == 0)
                {
                    ReMessageBox.Show("请至少使能一个RT地址", "提示",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return false;
                }

                // 使用反射获取设备句柄
                var deviceHandleField = typeof(ART1553BDriver).GetField("_deviceHandle", 
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (deviceHandleField == null)
                {
                    System.Diagnostics.Debug.WriteLine("[ART1553B] 无法访问驱动设备句柄");
                    return false;
                }

                IntPtr deviceHandle = (IntPtr)deviceHandleField.GetValue(driver);
                if (deviceHandle == IntPtr.Zero)
                {
                    System.Diagnostics.Debug.WriteLine("[ART1553B] 设备句柄无效");
                    return false;
                }

                // 初始化RT
                int ret = MeasureControl.Drivers.ART1553B.RT_Init(deviceHandle, channel);
                if (ret != MeasureControl.Drivers.ART1553B.ART1553Success)
                {
                    System.Diagnostics.Debug.WriteLine($"[ART1553B] RT初始化失败，错误码: {ret}");
                    return false;
                }

                // 设置响应时间（使用第一个使能RT的响应时间，或使用默认值）
                ushort responseTime = RTConfigs.FirstOrDefault(r => r.IsEnabled)?.ResponseTime ?? RTResponseTime;
                ret = MeasureControl.Drivers.ART1553B.RT_SetRespTime(deviceHandle, channel, responseTime);
                if (ret != MeasureControl.Drivers.ART1553B.ART1553Success)
                {
                    System.Diagnostics.Debug.WriteLine($"[ART1553B] 设置RT响应时间失败，错误码: {ret}");
                    return false;
                }

                // 使能所有RT
                System.Diagnostics.Debug.WriteLine($"[ART1553B] RT使能掩码: 0x{rtEnableMask:X8}，通道: {channel}");
                ret = MeasureControl.Drivers.ART1553B.RT_Select(deviceHandle, channel, rtEnableMask);
                if (ret != MeasureControl.Drivers.ART1553B.ART1553Success)
                {
                    System.Diagnostics.Debug.WriteLine($"[ART1553B] RT使能失败，错误码: {ret}");
                    return false;
                }
                System.Diagnostics.Debug.WriteLine($"[ART1553B] RT使能成功");

                // 设置接收非法指令数据（参考官方例程：使所有SA都合法）
                ret = MeasureControl.Drivers.ART1553B.RT_RevIllegalData(deviceHandle, channel, true);
                if (ret != MeasureControl.Drivers.ART1553B.ART1553Success)
                {
                    System.Diagnostics.Debug.WriteLine($"[ART1553B] 设置RT_RevIllegalData失败，错误码: {ret}");
                }
                
                // 允许接收非法指令（使所有SA都能接收）
                ret = MeasureControl.Drivers.ART1553B.RT_IllegalCmd(deviceHandle, channel, false);
                if (ret != MeasureControl.Drivers.ART1553B.ART1553Success)
                {
                    System.Diagnostics.Debug.WriteLine($"[ART1553B] 设置RT_IllegalCmd失败，错误码: {ret}");
                }

                // 为每个使能的RT设置非法命令表（将所有SA设为合法）
                foreach (var rt in RTConfigs.Where(r => r.IsEnabled))
                {
                    // 创建合法命令表（全部为0表示合法）
                    var cmdTable = new MeasureControl.Drivers.ART1553B.RT_Illegal_CMD_TABLE_STRUCT();
                    cmdTable.CmdTable = new int[32, 2 * 32]; // 初始化为0，表示所有命令都合法
                    
                    ret = MeasureControl.Drivers.ART1553B.RT_SetIllegalCmdTable(deviceHandle, channel, rt.RTAddress, ref cmdTable);
                    if (ret != MeasureControl.Drivers.ART1553B.ART1553Success)
                    {
                        System.Diagnostics.Debug.WriteLine($"[ART1553B] RT{rt.RTAddress}设置合法命令表失败，错误码: {ret}");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"[ART1553B] RT{rt.RTAddress}所有SA已设为合法");
                    }
                }

                // 为每个使能的RT配置发送模式
                foreach (var rt in RTConfigs.Where(r => r.IsEnabled))
                {
                    var txMode = new MeasureControl.Drivers.ART1553B.RT_TX_MODE_STRUCT();
                    txMode.TxMode = new byte[32, 32]; // 初始化发送模式（0=单缓冲区）
                    ret = MeasureControl.Drivers.ART1553B.RT_TxMode(deviceHandle, channel, rt.RTAddress, ref txMode);
                    if (ret != MeasureControl.Drivers.ART1553B.ART1553Success)
                    {
                        System.Diagnostics.Debug.WriteLine($"[ART1553B] RT{rt.RTAddress}设置TxMode失败，错误码: {ret}");
                    }
                }

                // 为每个使能的RT配置子地址发送数据
                // 重要：RT→BC 和 RT→RT 传输需要预先将数据写入RT的发送缓冲区
                int mapCount = _rtSubAddressConfigsMapByChannel.ContainsKey(channel) ? _rtSubAddressConfigsMapByChannel[channel].Count : 0;
                System.Diagnostics.Debug.WriteLine($"[ART1553B] 开始配置RT子地址发送数据，当前通道 {_selectedChannel} 的 _rtSubAddressConfigsMap 包含 {mapCount} 个RT");
                foreach (var rt in RTConfigs.Where(r => r.IsEnabled))
                {
                    int rtAddr = rt.RTAddress;
                    System.Diagnostics.Debug.WriteLine($"[ART1553B] 检查RT{rtAddr}的子地址配置...");

                    // 获取该通道该RT的子地址配置
                    if (_rtSubAddressConfigsMapByChannel.ContainsKey(channel) &&
                        _rtSubAddressConfigsMapByChannel[channel].ContainsKey(rtAddr))
                    {
                        var saConfigs = _rtSubAddressConfigsMapByChannel[channel][rtAddr];
                        System.Diagnostics.Debug.WriteLine($"[ART1553B] RT{rtAddr}有 {saConfigs.Count} 个子地址配置（通道 {channel}）");

                        // 列出所有有发送数据的子地址（TransmitEnabled 在 UI 已移除，但后台仍默认 true）
                        var txConfigs = saConfigs.Where(s => s.TransmitEnabled && !string.IsNullOrEmpty(s.SendDataHex)).ToList();
                        System.Diagnostics.Debug.WriteLine($"[ART1553B] RT{rtAddr}有 {txConfigs.Count} 个子地址配置了发送数据");

                        foreach (var saConfig in txConfigs)
                        {
                            // 解析发送数据
                            if (TryParseHexWords(saConfig.SendDataHex, out var sendData) && sendData.Length > 0)
                            {
                                int dataLen = saConfig.DataLength > 0 ? saConfig.DataLength : sendData.Length;
                                if (dataLen > 32) dataLen = 32;

                                // 确保数据长度正确
                                ushort[] paddedData = new ushort[dataLen];
                                for (int i = 0; i < dataLen && i < sendData.Length; i++)
                                {
                                    paddedData[i] = sendData[i];
                                }

                                // 使用 RT_SendMsg 将数据写入RT的发送缓冲区
                                ret = MeasureControl.Drivers.ART1553B.RT_SendMsg(deviceHandle, channel, rtAddr, saConfig.SubAddress, (uint)dataLen, paddedData);
                                if (ret != MeasureControl.Drivers.ART1553B.ART1553Success)
                                {
                                    System.Diagnostics.Debug.WriteLine($"[ART1553B] RT{rtAddr} SA{saConfig.SubAddress} 写入发送数据失败，错误码: {ret}");
                                }
                                else
                                {
                                    System.Diagnostics.Debug.WriteLine($"[ART1553B] RT{rtAddr} SA{saConfig.SubAddress} 写入发送数据成功: {saConfig.SendDataHex}");
                                }
                            }
                        }
                    }
                }

                // 注册RT监控通道，确保RT接收监控在正确的通道上运行
                driver.RegisterRTMonitoringChannel(channel);
                System.Diagnostics.Debug.WriteLine($"[ART1553B] 已注册RT监控通道: {channel}");

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ART1553B] 配置RT失败: {ex.Message}");
                return false;
            }
        }

        private async Task ApplyRTModeAsync()
        {
            var art1553bDriver = _driver as ART1553BDriver;
            if (art1553bDriver == null || !IsDeviceConnected)
                return;

            int channel = SelectedChannel;
            await ConfigureAllEnabledRTsAsync(art1553bDriver, channel);
        }

        private async Task SendDataAsync()
        {
            var art1553bDriver = _driver as ART1553BDriver;
            if (art1553bDriver == null || !IsDeviceConnected)
                return;

            // BM模式不能发送数据
            if (IsBMMode)
            {
                ReMessageBox.Show("BM模式只能监控消息，不能发送数据", "提示",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            // 使用16位字解析（格式: "1111 2222 3333"）
            if (!TryParseHexWords(SendDataHex, out var words))
            {
                ReMessageBox.Show("发送数据格式错误，请输入16位十六进制字，例如: 1111 2222 3333", "错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            if (words.Length > 32)
            {
                words = words.Take(32).ToArray();
            }
            int channel = SelectedChannel;

            try
            {
                if (IsBCMode)
                {
                    // BC模式发送消息，使用BC模式配置的目标RT地址和子地址
                    bool success = await SendBCMessageAsync(art1553bDriver, channel, BCTargetRTAddress, BCTargetSubAddress, words);
                    if (success)
                    {
                        ReMessageBox.Show("BC消息发送成功", "提示",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    }
                    else
                    {
                        ReMessageBox.Show("BC消息发送失败，请检查设备状态和配置", "错误",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                    }
                }
                else if (IsRTMode)
                {
                    // RT模式发送消息
                    // 确保RT模式已配置
                    await art1553bDriver.ConfigureRTModeAsync(channel, RTAddress, RTResponseTime);
                    
                    bool ok = art1553bDriver.SendRTMessage(RTAddress, SubAddress, words);
                    if (!ok)
                    {
                        ReMessageBox.Show("RT消息发送失败，请检查设备状态/RT配置", "错误",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                    }
                    else
                    {
                        ReMessageBox.Show("RT消息发送成功", "提示",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                ReMessageBox.Show($"发送数据异常: {ex.Message}", "错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// BC模式发送消息
        /// </summary>
        private async Task<bool> SendBCMessageAsync(ART1553BDriver driver, int channel, int rtAddress, int subAddress, ushort[] data)
        {
            try
            {
                // 确保BC模式已配置
                if (!await driver.ConfigureBCModeAsync(channel, BCResponseTimeout, BCFrameGap))
                {
                    ReMessageBox.Show("BC模式配置失败，请检查设备状态", "错误",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return false;
                }

                // 使用消息列表中的第一个消息，如果没有则创建临时消息
                MessageConfigItem messageToSend = null;
                if (MessageConfigs != null && MessageConfigs.Count > 0)
                {
                    // 查找匹配的消息，或使用第一个启用的消息
                    messageToSend = MessageConfigs.FirstOrDefault(m => m.IsEnabled && m.RTAddress == rtAddress && m.SubAddress == subAddress);
                    if (messageToSend == null)
                    {
                        messageToSend = MessageConfigs.FirstOrDefault(m => m.IsEnabled);
                    }
                }

                ushort messageId = 0;
                int channelSelect = 1; // 默认Channel A
                int messageGap = 20;
                bool retryEnable = false;

                if (messageToSend != null)
                {
                    messageId = (ushort)messageToSend.MessageId;
                    channelSelect = messageToSend.ChannelSelect;
                    messageGap = messageToSend.MessageGap;
                    retryEnable = messageToSend.RetryEnable;
                    
                    // 使用16位字解析消息中的数据
                    if (!string.IsNullOrEmpty(messageToSend.DataHex) && TryParseHexWords(messageToSend.DataHex, out var msgWords) && msgWords.Length > 0)
                    {
                        data = msgWords;
                    }
                }

                // 发送BC消息到RT（参考官方例程）
                bool success = driver.SendBCMessageToRT(channel, messageId, rtAddress, subAddress, data, channelSelect, messageGap, retryEnable);
                if (!success)
                {
                    ReMessageBox.Show("BC消息写入失败，请检查设备状态和配置", "错误",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return false;
                }

                // 启动BC并等待消息完成
                success = driver.BCStartAndWait(channel, BCResponseTimeout);
                if (!success)
                {
                    ReMessageBox.Show("BC消息执行失败或超时", "错误",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return false;
                }

                // 读取RT返回的消息（如果有）
                if (driver.BCReadMessage(channel, messageId, out var recvMsg))
                {
                    // 解析接收到的数据
                    int dataCount = recvMsg.MsgBlock.CmdWord1 & 0x1F;
                    if (dataCount == 0) dataCount = 32;

                    var recvData = new List<ushort>();
                    for (int i = 0; i < dataCount && i < 32; i++)
                    {
                        recvData.Add(recvMsg.MsgBlock.Datablk[i]);
                    }

                    // 更新接收数据显示
                    var recvHex = string.Join(" ", recvData.Select(w => w.ToString("X4")));
                    ReceivedDataHex = recvHex;
                    ReceivedByteCount = recvData.Count * 2;

                    // 检查状态字
                    ushort statusWord = recvMsg.MsgBlock.StatusWord1;
                    int recvRTAddress = (statusWord >> 11) & 0x1F;
                    if (recvRTAddress != rtAddress)
                    {
                        System.Diagnostics.Debug.WriteLine($"[ART1553B] RT地址不匹配，期望: {rtAddress}, 实际: {recvRTAddress}");
                    }
                }

                // 停止BC
                driver.BCStop(channel);

                await Task.CompletedTask;
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ART1553B] BC发送消息失败: {ex.Message}");
                ReMessageBox.Show($"BC发送消息失败: {ex.Message}", "错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return false;
            }
        }

        /// <summary>
        /// BC运行（参考官方例程 - 循环发送）
        /// </summary>
        /// <param name="channel">通道号（0或1），从命令参数获取</param>
        private async Task BCRunAsync(int channel)
        {
            var art1553bDriver = _driver as ART1553BDriver;
            if (art1553bDriver == null || !IsDeviceConnected)
                return;

            System.Diagnostics.Debug.WriteLine($"[ART1553B] BCRunAsync 启动，通道: {channel}");

            // 如果已经在运行，不重复启动
            if (_isBCRunningByChannel[channel] && _bcLoopThreads[channel] != null && _bcLoopThreads[channel].IsAlive)
            {
                ReMessageBox.Show($"通道{channel} BC已在运行", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                // 1. 配置BC模式（参考官方例程：BC_Init, BC_SetFrameGap, BC_SetRespTimeout）
                bool success = await art1553bDriver.ConfigureBCModeAsync(channel, BCResponseTimeout, BCFrameGap);
                if (!success)
                {
                    ReMessageBox.Show("BC模式配置失败", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // 2. 写入BC消息（参考官方例程：BC_WriteMsg）
                success = WriteBCMessages(art1553bDriver, channel);
                if (!success)
                {
                    ReMessageBox.Show("BC消息写入失败", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // 3. 更新运行状态
                _isBCRunningByChannel[channel] = true;
                _bcSendCountByChannel[channel] = 0;
                UpdateChannelRunningState();
                (BCRunCommand as DelegateCommand<object>)?.RaiseCanExecuteChanged();
                (BCStopCommand as DelegateCommand<object>)?.RaiseCanExecuteChanged();

                // 4. 启动BC循环发送线程（参考官方例程：循环 BC_Start -> BC_IsMsgOver -> Sleep）
                _bcLoopCts[channel] = new CancellationTokenSource();
                var cts = _bcLoopCts[channel];
                int sendInterval = BCSendInterval;
                var enabledMessages = MessageConfigs?.Where(m => m.IsEnabled).ToList() ?? new List<MessageConfigItem>();

                _bcLoopThreads[channel] = new Thread(() => BCLoopThread(art1553bDriver, channel, sendInterval, enabledMessages, cts.Token));
                _bcLoopThreads[channel].IsBackground = true;
                _bcLoopThreads[channel].Start();

                System.Diagnostics.Debug.WriteLine($"[ART1553B] 通道{channel} BC循环发送已启动，间隔: {sendInterval}ms");
                ReMessageBox.Show($"通道{channel} BC已启动循环发送", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ART1553B] BC运行失败: {ex.Message}");
                ReMessageBox.Show($"BC运行失败: {ex.Message}", "错误", MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// BC停止（参考官方例程）
        /// </summary>
        /// <param name="channel">通道号（0或1），从命令参数获取</param>
        private async Task BCStopAsync(int channel)
        {
            var art1553bDriver = _driver as ART1553BDriver;
            if (art1553bDriver == null || !IsDeviceConnected)
                return;

            System.Diagnostics.Debug.WriteLine($"[ART1553B] BCStopAsync 停止，通道: {channel}");

            try
            {
                // 1. 停止循环发送线程
                if (_bcLoopCts.ContainsKey(channel) && _bcLoopCts[channel] != null)
                {
                    _bcLoopCts[channel].Cancel();
                    _bcLoopCts[channel] = null;
                }

                // 等待线程结束
                if (_bcLoopThreads.ContainsKey(channel) && _bcLoopThreads[channel] != null)
                {
                    _bcLoopThreads[channel].Join(1000);
                    _bcLoopThreads[channel] = null;
                }

                // 2. 停止BC硬件
                bool success = art1553bDriver.BCStop(channel);
                
                // 3. 更新运行状态
                _isBCRunningByChannel[channel] = false;
                UpdateChannelRunningState();
                (BCRunCommand as DelegateCommand<object>)?.RaiseCanExecuteChanged();
                (BCStopCommand as DelegateCommand<object>)?.RaiseCanExecuteChanged();
                
                // 4. 更新统计显示
                RaisePropertyChanged(nameof(BCSendCount));
                
                System.Diagnostics.Debug.WriteLine($"[ART1553B] 通道{channel} BC已停止，共发送: {_bcSendCountByChannel[channel]}");
                ReMessageBox.Show($"通道{channel} BC已停止\n发送次数: {_bcSendCountByChannel[channel]}", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ART1553B] BC停止失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 写入BC消息（参考官方例程）
        /// </summary>
        private bool WriteBCMessages(ART1553BDriver driver, int channel)
        {
            if (MessageConfigs == null || MessageConfigs.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine($"[ART1553B] 没有消息配置");
                return false;
            }

            var enabledMessages = MessageConfigs.Where(m => m.IsEnabled).ToList();
            if (enabledMessages.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine($"[ART1553B] 没有启用的消息");
                return false;
            }

            int enabledCount = enabledMessages.Count;
            for (int idx = 0; idx < enabledCount; idx++)
            {
                var msgConfig = enabledMessages[idx];
                // 为保证板卡在硬件端按添加顺序触发，使用 InitPeriod 作为相位偏移
                // 官方建议使用 InitPeriod（单位 ms）设置消息的初始触发相位
                // 官方做法：在板卡端通过 InitPeriod 设置相位，均分整个周期以保证按序触发
                msgConfig.InitPeriod = 0;
                System.Diagnostics.Debug.WriteLine($"[ART1553B] 设置消息ID={msgConfig.MessageId} InitPeriod={msgConfig.InitPeriod}ms (idx={idx})");
                int dataLength = msgConfig.DataLength > 0 ? msgConfig.DataLength : 32;
                bool success = false;

                // 根据消息类型调用不同的驱动方法
                string msgType = msgConfig.MessageType;
                System.Diagnostics.Debug.WriteLine($"[ART1553B] 处理消息: ID={msgConfig.MessageId}, 类型={msgType}, RT={msgConfig.RTAddress}, SA={msgConfig.SubAddress}");

                if (msgType == "BC->RT")
                {
                    // BC→RT：BC发送数据给RT（数据在BC消息中配置）
                    ushort[] data = null;

                    // 使用16位字解析（格式: "1111 2222 3333"，每个字4个十六进制字符）
                    if (!string.IsNullOrEmpty(msgConfig.DataHex))
                    {
                        if (TryParseHexWords(msgConfig.DataHex, out var words) && words.Length > 0)
                        {
                            data = words;
                            System.Diagnostics.Debug.WriteLine($"[ART1553B] BC->RT 解析数据: {string.Join(" ", data.Select(w => w.ToString("X4")))}");
                        }
                    }

                    // 如果没有数据，使用默认填充
                    if (data == null || data.Length == 0)
                    {
                        data = new ushort[dataLength];
                        for (int i = 0; i < dataLength; i++)
                        {
                            data[i] = (ushort)i; // 默认填充索引
                        }
                        System.Diagnostics.Debug.WriteLine($"[ART1553B] BC->RT 使用默认数据填充: {dataLength}个字");
                    }

                    // 确保周期和运行标志：若消息配置未设置周期则使用全局发送间隔
                    int periodToUse = msgConfig.Period > 0 ? msgConfig.Period : BCSendInterval;
                    if (msgConfig.Run == false) msgConfig.Run = true;
                    success = driver.SendBCMessageToRT(channel, (ushort)msgConfig.MessageId,
                        msgConfig.RTAddress, msgConfig.SubAddress, data,
                        msgConfig.ChannelSelect, msgConfig.MessageGap, msgConfig.RetryEnable,
                        (ushort)periodToUse, (ushort)msgConfig.InitPeriod, msgConfig.Run);

                    System.Diagnostics.Debug.WriteLine($"[ART1553B] BC->RT: RT{msgConfig.RTAddress} SA{msgConfig.SubAddress}, 数据: {string.Join(" ", data.Take(Math.Min(data.Length, 8)).Select(w => w.ToString("X4")))}...");
                }
                else if (msgType == "RT->BC")
                {
                    // RT→BC：BC命令RT发送数据给BC（数据在RT的SA中预配置）
                    int periodToUse = msgConfig.Period > 0 ? msgConfig.Period : BCSendInterval;
                    if (msgConfig.Run == false) msgConfig.Run = true;
                    success = driver.SendRTToBCMessage(channel, (ushort)msgConfig.MessageId,
                        msgConfig.RTAddress, msgConfig.SubAddress, dataLength,
                        msgConfig.ChannelSelect, msgConfig.MessageGap, msgConfig.RetryEnable,
                        (ushort)periodToUse, (ushort)msgConfig.InitPeriod, msgConfig.Run);

                    System.Diagnostics.Debug.WriteLine($"[ART1553B] RT->BC: RT{msgConfig.RTAddress} SA{msgConfig.SubAddress} -> BC, 期望{dataLength}字");
                }
                else if (msgType == "RT->RT")
                {
                    // RT→RT：BC命令源RT发送数据给目标RT（数据在源RT的SA中预配置）
                    // RTAddress/SubAddress = 源RT（发送方）
                    // RTAddress2/SubAddress2 = 目标RT（接收方）
                    int periodToUse = msgConfig.Period > 0 ? msgConfig.Period : BCSendInterval;
                    if (msgConfig.Run == false) msgConfig.Run = true;
                    success = driver.SendRTToRTMessage(channel, (ushort)msgConfig.MessageId,
                        msgConfig.RTAddress, msgConfig.SubAddress,   // 源RT
                        msgConfig.RTAddress2, msgConfig.SubAddress2, // 目标RT
                        dataLength, msgConfig.ChannelSelect, msgConfig.MessageGap, msgConfig.RetryEnable,
                        (ushort)periodToUse, (ushort)msgConfig.InitPeriod, msgConfig.Run);

                    System.Diagnostics.Debug.WriteLine($"[ART1553B] RT->RT: RT{msgConfig.RTAddress} SA{msgConfig.SubAddress} -> RT{msgConfig.RTAddress2} SA{msgConfig.SubAddress2}, {dataLength}字");
                }
                else
                {
                    // 其他类型（Broadcast、Mode Code等）暂不支持，跳过
                    System.Diagnostics.Debug.WriteLine($"[ART1553B] 暂不支持消息类型: {msgType}，跳过消息{msgConfig.MessageId}");
                    continue;
                }

                if (!success)
                {
                    System.Diagnostics.Debug.WriteLine($"[ART1553B] BC消息{msgConfig.MessageId}({msgType})写入失败");
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// BC循环发送线程（参考官方例程 bcrtbmdemo）
        /// </summary>
        private void BCLoopThread(ART1553BDriver driver, int channel, int sendInterval, List<MessageConfigItem> messages, CancellationToken token)
        {
            try
            {
                // 获取设备句柄
                var deviceHandleField = typeof(ART1553BDriver).GetField("_deviceHandle",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (deviceHandleField == null)
                    return;

                IntPtr deviceHandle = (IntPtr)deviceHandleField.GetValue(driver);
                if (deviceHandle == IntPtr.Zero)
                    return;

                System.Diagnostics.Debug.WriteLine($"[ART1553B] 通道{channel} BC循环发送线程启动");

                while (!token.IsCancellationRequested)
                {
                    // 1. BC_Start（参考官方例程）
                    int ret = ART1553B.BC_Start(deviceHandle, channel);
                    if (ret != ART1553B.ART1553Success)
                    {
                        System.Diagnostics.Debug.WriteLine($"[ART1553B] 通道{channel} BC启动失败: {ret}");
                        Thread.Sleep(100);
                        continue;
                    }

                    // 2. 等待BC_IsMsgOver（参考官方例程）
                    int timeout = 0;
                    while (!token.IsCancellationRequested && timeout < 1000)
                    {
                        ret = ART1553B.BC_IsMsgOver(deviceHandle, channel);
                        if (ret == ART1553B.ART1553Success)
                            break;
                        Thread.Sleep(1);
                        timeout++;
                    }

                    if (token.IsCancellationRequested)
                        break;

                    // 3. 读取并处理返回消息
                    foreach (var msg in messages)
                    {
                        var recvMsg = new ART1553B.RMSG_STRUCT();
                        recvMsg.MsgBlock.Datablk = new ushort[32];
                        ret = ART1553B.BC_ReadMsg(deviceHandle, channel, (ushort)msg.MessageId, ref recvMsg);
                        if (ret == ART1553B.ART1553Success)
                        {
                            // 解析状态字
                            ushort statusWord = recvMsg.MsgBlock.StatusWord1;
                            int rtAddr = (statusWord >> 11) & 0x1F;
                            
                            // 更新接收数据显示
                            ushort cmdWord = recvMsg.MsgBlock.CmdWord1;
                            int wordCount = cmdWord & 0x1F;
                            if (wordCount == 0) wordCount = 32;

                            if (recvMsg.MsgBlock.Datablk != null)
                            {
                                var recvData = new ushort[wordCount];
                                for (int i = 0; i < wordCount && i < 32; i++)
                                {
                                    recvData[i] = recvMsg.MsgBlock.Datablk[i];
                                }

                                // 直接格式化16位字（不拆成字节）
                                string hex = FormatHexWords(recvData, wordCount);

                                // 更新UI
                                Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                                {
                                    ReceivedByteCount = wordCount * 2;
                                    ReceivedDataHex = hex;
                                }));
                            }
                        }
                    }

                    // 4. 更新发送统计
                    _bcSendCountByChannel[channel] += (ulong)messages.Count;

                    // 更新UI统计
                    Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                    {
                        RaisePropertyChanged(nameof(BCSendCount));
                    }));

                    // 5. 延时（参考官方例程：Thread.Sleep(10)）
                    Thread.Sleep(sendInterval);
                }

                System.Diagnostics.Debug.WriteLine($"[ART1553B] 通道{channel} BC循环发送线程结束");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ART1553B] 通道{channel} BC循环发送线程异常: {ex.Message}");
            }
        }

        /// <summary>
        /// RT运行（参考官方例程）
        /// </summary>
        /// <param name="channel">通道号（0或1），从命令参数获取</param>
        private async Task RTRunAsync(int channel)
        {
            var art1553bDriver = _driver as ART1553BDriver;
            if (art1553bDriver == null || !IsDeviceConnected)
                return;

            System.Diagnostics.Debug.WriteLine($"[ART1553B] RTRunAsync 启动，通道: {channel}");
            
            // 列出所有使能的RT
            var enabledRTs = RTConfigs?.Where(r => r.IsEnabled).ToList();
            System.Diagnostics.Debug.WriteLine($"[ART1553B] 已使能的RT数量: {enabledRTs?.Count ?? 0}");
            if (enabledRTs != null)
            {
                foreach (var rt in enabledRTs)
                {
                    System.Diagnostics.Debug.WriteLine($"[ART1553B]   - RT{rt.RTAddress} 已使能");
                }
            }

            try
            {
                // 1. 配置RT模式（参考官方例程：RT_Init, RT_SetRespTime, RT_Select）
                bool success = await ConfigureAllEnabledRTsAsync(art1553bDriver, channel);
                if (!success)
                {
                    return;
                }

                // 2. 启动RT（参考官方例程：RT_Start(hDevice, chno, true)）
                // 使用反射获取设备句柄
                var deviceHandleField = typeof(ART1553BDriver).GetField("_deviceHandle",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (deviceHandleField == null)
                {
                    ReMessageBox.Show("无法访问驱动设备句柄", "错误",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }

                IntPtr deviceHandle = (IntPtr)deviceHandleField.GetValue(art1553bDriver);
                if (deviceHandle == IntPtr.Zero)
                {
                    ReMessageBox.Show("设备句柄无效", "错误",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }

                int ret = ART1553B.RT_Start(deviceHandle, channel, true);
                if (ret == ART1553B.ART1553Success)
                {
                    // 更新当前通道的运行状态
                    _isRTRunningByChannel[channel] = true;
                    UpdateChannelRunningState();
                    (RTRunCommand as DelegateCommand<object>)?.RaiseCanExecuteChanged();
                    (RTStopCommand as DelegateCommand<object>)?.RaiseCanExecuteChanged();

                    // 关键：同步驱动当前模式/通道，否则 StartMonitoringAsync 的 MonitorLoop 不会轮询 RT
                    art1553bDriver.SetCurrentMode(ART1553BDriver.DeviceMode.RT, channel);
                    
                    // 启动RT接收监控线程
                    await art1553bDriver.StartMonitoringAsync();
                    
                    ReMessageBox.Show("RT运行成功", "提示",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                else
                {
                    ReMessageBox.Show($"RT启动失败，错误码: {ret}", "错误",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ART1553B] RT运行失败: {ex.Message}");
                ReMessageBox.Show($"RT运行失败: {ex.Message}", "错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// RT停止（参考官方例程：RT_Start(hDevice, chno, false)）
        /// </summary>
        /// <param name="channel">通道号（0或1），从命令参数获取</param>
        private async Task RTStopAsync(int channel)
        {
            var art1553bDriver = _driver as ART1553BDriver;
            if (art1553bDriver == null || !IsDeviceConnected)
                return;

            System.Diagnostics.Debug.WriteLine($"[ART1553B] RTStopAsync 停止，通道: {channel}");

            try
            {
                // 使用反射获取设备句柄
                var deviceHandleField = typeof(ART1553BDriver).GetField("_deviceHandle",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (deviceHandleField == null)
                    return;

                IntPtr deviceHandle = (IntPtr)deviceHandleField.GetValue(art1553bDriver);
                if (deviceHandle == IntPtr.Zero)
                    return;

                int ret = ART1553B.RT_Start(deviceHandle, channel, false);
                if (ret == ART1553B.ART1553Success)
                {
                    // 取消注册RT监控通道
                    art1553bDriver.UnregisterRTMonitoringChannel(channel);
                    
                    // 更新当前通道的运行状态
                    _isRTRunningByChannel[channel] = false;
                    UpdateChannelRunningState();
                    (RTRunCommand as DelegateCommand<object>)?.RaiseCanExecuteChanged();
                    (RTStopCommand as DelegateCommand<object>)?.RaiseCanExecuteChanged();
                    
                    // 停止RT接收监控线程
                    await art1553bDriver.StopMonitoringAsync();
                    
                    ReMessageBox.Show("RT已停止", "提示",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ART1553B] RT停止失败: {ex.Message}");
            }
        }

        /// <summary>
        /// BM运行（参考官方例程）
        /// </summary>
        /// <param name="channel">通道号（0或1），从命令参数获取</param>
        private async Task BMRunAsync(int channel)
        {
            var art1553bDriver = _driver as ART1553BDriver;
            if (art1553bDriver == null || !IsDeviceConnected)
                return;

            System.Diagnostics.Debug.WriteLine($"[ART1553B] BMRunAsync 启动，通道: {channel}");

            try
            {
                bool bmOk = art1553bDriver.StartBMWithFilter(channel);
                if (bmOk)
                {
                    // 仅更新当前通道的运行状态（BM 线程为单通道，避免跨通道状态不同步）
                    _isBMRunningByChannel[channel] = true;
                    UpdateChannelRunningState();
                    (BMRunCommand as DelegateCommand<object>)?.RaiseCanExecuteChanged();
                    (BMStopCommand as DelegateCommand<object>)?.RaiseCanExecuteChanged();

                    ReMessageBox.Show("BM运行成功", "提示",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                else
                {
                    ReMessageBox.Show("BM启动失败", "错误",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ART1553B] BM运行失败: {ex.Message}");

                ReMessageBox.Show($"BM运行失败: {ex.Message}", "错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// BM停止（参考官方例程：BM_Start(hDevice, chno, false)）
        /// </summary>
        /// <param name="channel">通道号（0或1），从命令参数获取</param>
        private async Task BMStopAsync(int channel)
        {
            var art1553bDriver = _driver as ART1553BDriver;
            if (art1553bDriver == null || !IsDeviceConnected)
                return;

            System.Diagnostics.Debug.WriteLine($"[ART1553B] BMStopAsync 停止，通道: {channel}");

            try
            {
                bool ok = art1553bDriver.StopBM(channel);
                if (ok)
                {
                    // 更新当前通道的运行状态
                    _isBMRunningByChannel[channel] = false;
                    UpdateChannelRunningState();
                    (BMRunCommand as DelegateCommand<object>)?.RaiseCanExecuteChanged();
                    (BMStopCommand as DelegateCommand<object>)?.RaiseCanExecuteChanged();

                    ReMessageBox.Show("BM已停止", "提示",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ART1553B] BM停止失败: {ex.Message}");
            }
        }

        private void OnDriverMessageReceived(object sender, ART1553BDriver.MessageReceivedEventArgs e)
        {
            int selectedChannel = SelectedChannel;

            ushort cmdWord = e.Message.MsgBlock.CmdWord1;
            int wordCount = cmdWord & 0x1F;
            if (wordCount == 0)
                wordCount = 32;

            // 直接格式化16位字（不拆成字节）
            string hex = FormatHexWords(e.Message.MsgBlock.Datablk, wordCount);

            // 处理RT接收消息统计（参考官方例程）
            if (e.MessageType == "RT_Receive")
            {
                // 更新RT接收统计
                if (_rtReceiveCountByChannel.ContainsKey(e.Channel))
                {
                    _rtReceiveCountByChannel[e.Channel]++;
                }
                
                // 验证数据正确性（参考官方例程：数据应该等于RT地址）
                bool dataError = false;
                if (e.Message.MsgBlock.Datablk != null)
                {
                    int rtAddr = e.RTAddress;
                    for (int i = 0; i < wordCount && i < 32; i++)
                    {
                        if (e.Message.MsgBlock.Datablk[i] != rtAddr)
                        {
                            dataError = true;
                            break;
                        }
                    }
                }
                if (dataError && _rtErrorCountByChannel.ContainsKey(e.Channel))
                {
                    _rtErrorCountByChannel[e.Channel]++;
                }
                
                System.Diagnostics.Debug.WriteLine($"[ART1553B] RT{e.RTAddress}接收数据，通道: {e.Channel}，字数: {wordCount}，数据: {hex}");
            }

            // 处理BM模式监控消息
            if (e.MessageType == "BM_Receive" && IsBMMode)
            {
                AddBMMessage(e);
            }

            // 字节数 = 字数 * 2
            int byteCount = wordCount * 2;
            
            Action update = () =>
            {
                lock (_rtReceiveLock)
                {
                    _receivedByChannel[e.Channel] = (byteCount, hex);
                }

                if (!UseMultiChannelRT || e.Channel == selectedChannel)
                {
                    ReceivedByteCount = byteCount;
                    ReceivedDataHex = hex;
                }
                
                // 更新统计显示
                RaisePropertyChanged(nameof(RTReceiveCount));
                RaisePropertyChanged(nameof(RTErrorCount));
            };

            if (Application.Current != null && Application.Current.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
            {
                Application.Current.Dispatcher.Invoke(update);
            }
            else
            {
                update();
            }
        }

        private void RefreshReceivedForSelectedInterface()
        {
            if (!UseMultiChannelRT)
                return;

            int channel = SelectedChannel;
            (int ByteCount, string DataHex) cached;

            lock (_rtReceiveLock)
            {
                if (!_receivedByChannel.TryGetValue(channel, out cached))
                    return;
            }

            ReceivedByteCount = cached.ByteCount;
            ReceivedDataHex = cached.DataHex;
        }

        private int GetChannelForSelectedInterface()
        {
            if (SelectedBusInterface == null)
                return 0;

            return Math.Max(0, SelectedBusInterface.InterfaceNumber - 1);
        }

        private static bool TryParseHexBytes(string text, out byte[] bytes)
        {
            bytes = Array.Empty<byte>();
            if (string.IsNullOrWhiteSpace(text))
                return true;

            var cleaned = text
                .Replace("0x", "")
                .Replace("0X", "")
                .Replace(",", " ")
                .Replace(";", " ");

            var parts = cleaned.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var list = new List<byte>();

            foreach (var p in parts)
            {
                if (!byte.TryParse(p, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var b))
                    return false;
                list.Add(b);
            }

            bytes = list.ToArray();
            return true;
        }

        /// <summary>
        /// 解析16位字（支持输入如 "1111 2222 3333" 格式）
        /// </summary>
        private static bool TryParseHexWords(string text, out ushort[] words)
        {
            words = Array.Empty<ushort>();
            if (string.IsNullOrWhiteSpace(text))
                return true;

            var cleaned = text
                .Replace("0x", "")
                .Replace("0X", "")
                .Replace(",", " ")
                .Replace(";", " ");

            var parts = cleaned.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var list = new List<ushort>();

            foreach (var p in parts)
            {
                // 支持16位字（最多4个十六进制字符）
                if (ushort.TryParse(p, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var w))
                {
                    list.Add(w);
                }
                else
                {
                    return false;
                }
            }

            words = list.ToArray();
            return true;
        }

        private static ushort[] PackBytesToWords(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                return Array.Empty<ushort>();

            int wordCount = (bytes.Length + 1) / 2;
            if (wordCount > 32)
                wordCount = 32;

            var words = new ushort[wordCount];
            for (int i = 0; i < wordCount; i++)
            {
                int index = i * 2;
                byte hi = index < bytes.Length ? bytes[index] : (byte)0;
                byte lo = (index + 1) < bytes.Length ? bytes[index + 1] : (byte)0;
                words[i] = (ushort)((hi << 8) | lo);
            }
            return words;
        }

        private static byte[] UnpackWordsToBytes(ushort[] words, int wordCount)
        {
            int count = Math.Min(wordCount, Math.Min(words == null ? 0 : words.Length, 32));
            var bytes = new byte[count * 2];
            for (int i = 0; i < count; i++)
            {
                bytes[i * 2] = (byte)((words[i] >> 8) & 0xFF);
                bytes[i * 2 + 1] = (byte)(words[i] & 0xFF);
            }
            return bytes;
        }

        private static string FormatHexBytes(byte[] bytes, int maxBytes)
        {
            if (bytes == null || bytes.Length == 0 || maxBytes <= 0)
                return string.Empty;

            int count = Math.Min(maxBytes, bytes.Length);
            var sb = new StringBuilder(count * 3);
            for (int i = 0; i < count; i++)
            {
                if (i > 0)
                    sb.Append(' ');
                sb.Append(bytes[i].ToString("X2"));
            }
            return sb.ToString();
        }

        /// <summary>
        /// 将16位字数组格式化为16进制字符串（每个字4个16进制字符，空格分隔）
        /// 例如：1111 2222 3333
        /// </summary>
        private static string FormatHexWords(ushort[] words, int wordCount)
        {
            if (words == null || words.Length == 0 || wordCount <= 0)
                return string.Empty;

            int count = Math.Min(wordCount, Math.Min(words.Length, 32));
            var sb = new StringBuilder(count * 5);
            for (int i = 0; i < count; i++)
            {
                if (i > 0)
                    sb.Append(' ');
                sb.Append(words[i].ToString("X4"));
            }
            return sb.ToString();
        }

        #endregion

        #region BM模式消息处理方法

        /// <summary>
        /// 添加BM监控消息
        /// </summary>
        private void AddBMMessage(ART1553BDriver.MessageReceivedEventArgs e)
        {
            var msg = e.Message;
            ushort cmdWord1 = msg.MsgBlock.CmdWord1;
            ushort cmdWord2 = msg.MsgBlock.CmdWord2;  // 第二个命令字（RT→RT时使用）
            ushort statusWord1 = msg.MsgBlock.StatusWord1;

            // 解析命令字（1553B命令字格式：RT地址[15:11] | T/R[10] | 子地址[9:5] | 字数/模式码[4:0]）
            int rtAddress = (cmdWord1 >> 11) & 0x1F;  // RT地址（5位）
            bool isTx = ((cmdWord1 >> 10) & 0x01) == 1;  // T/R位：1=发送（RT→BC），0=接收（BC→RT）
            int subAddress = (cmdWord1 >> 5) & 0x1F;  // 子地址（5位）
            int wordCount = cmdWord1 & 0x1F;  // 字数（5位）
            if (wordCount == 0)
                wordCount = 32;

            // 调试日志
            System.Diagnostics.Debug.WriteLine($"[BM消息解析] CmdWord1=0x{cmdWord1:X4}, CmdWord2=0x{cmdWord2:X4}, RTRT={msg.RTRT}, RTRTs={msg.RTRTs}");
            System.Diagnostics.Debug.WriteLine($"[BM消息解析] RT地址={rtAddress}, T/R={isTx}, 子地址={subAddress}, 字数={wordCount}");

            // 判断消息类型
            // 1553B协议规定：
            // - RT地址=31 为广播地址
            // - 子地址=0或31 为模式码
            // - T/R=0 表示BC→RT（接收命令）
            // - T/R=1 表示RT→BC（发送命令）
            // - RTRT标志或CmdWord2非零表示RT→RT传输
            string messageType = "未知";
            
            // 首先检查是否为RT→RT传输（根据RTRT标志或CmdWord2非零）
            if (msg.RTRT == 1 || (cmdWord2 != 0 && cmdWord1 != cmdWord2))
            {
                messageType = "RT->RT";
                // 解析第二个命令字（发送方RT）
                int srcRTAddress = (cmdWord2 >> 11) & 0x1F;
                int srcSubAddress = (cmdWord2 >> 5) & 0x1F;
                System.Diagnostics.Debug.WriteLine($"[BM消息解析] RT->RT: 接收方RT{rtAddress} SA{subAddress} <- 发送方RT{srcRTAddress} SA{srcSubAddress}");
            }
            // 检查是否为广播（RT地址=31）
            else if (rtAddress == 31)
            {
                messageType = "广播";
            }
            // 检查是否为模式码（子地址=0或31）
            else if (subAddress == 0 || subAddress == 31)
            {
                messageType = "模式码";
            }
            // 根据T/R位判断方向
            else if (isTx)
            {
                // T/R=1：RT发送数据给BC
                messageType = "RT->BC";
            }
            else
            {
                // T/R=0：BC发送数据给RT
                messageType = "BC->RT";
            }
            
            System.Diagnostics.Debug.WriteLine($"[BM消息解析] 最终消息类型: {messageType}");

            // 检查错误
            bool hasError = false;
            string errorInfo = "";
            if (statusWord1 != 0)
            {
                if ((statusWord1 & 0x0001) != 0)
                {
                    hasError = true;
                    errorInfo += "消息错误 ";
                }
                if ((statusWord1 & 0x0002) != 0)
                {
                    hasError = true;
                    errorInfo += "终端标志 ";
                }
                if ((statusWord1 & 0x0004) != 0)
                {
                    hasError = true;
                    errorInfo += "子系统标志 ";
                }
                if ((statusWord1 & 0x0008) != 0)
                {
                    hasError = true;
                    errorInfo += "忙 ";
                }
                if ((statusWord1 & 0x0010) != 0)
                {
                    hasError = true;
                    errorInfo += "服务请求 ";
                }
            }

            // 提取数据（直接格式化16位字，不拆成字节）
            string dataHex = string.Empty;
            try
            {
                if (msg.MsgBlock.Datablk != null)
                {
                    dataHex = FormatHexWords(msg.MsgBlock.Datablk, wordCount);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[BM消息解析] MsgBlock或Datablk为空，跳过数据格式化");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[BM消息解析] FormatHexWords 异常: {ex.Message}");
                dataHex = string.Empty;
            }

            var bmMessage = new BMMessageItem
            {
                Timestamp = e.Timestamp,
                Channel = e.Channel,
                RTAddress = rtAddress,
                SubAddress = subAddress,
                MessageType = messageType,
                CommandWord = $"0x{cmdWord1:X4}",
                StatusWord = $"0x{statusWord1:X4}",
                DataHex = dataHex,
                DataWordCount = wordCount,
                HasError = hasError,
                ErrorInfo = errorInfo.Trim()
            };

            // 应用过滤
            if (ShouldShowBMMessage(bmMessage))
            {
                Action addMessage = () =>
                {
                    try
                    {
                        lock (_bmMessagesLock)
                        {
                            BMMessages.Add(bmMessage);
                            BMTotalMessageCount++;

                            if (hasError)
                            {
                                BMErrorMessageCount++;
                            }

                            // 限制消息数量（在 UI 线程上安全地移除最老消息）
                            if (BMMessages.Count > MaxBMMessages)
                            {
                                try
                                {
                                    BMMessages.RemoveAt(0);
                                }
                                catch (ArgumentOutOfRangeException)
                                {
                                    // 竞争情况下忽略
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[BM消息解析] 添加BM消息异常: {ex.Message}");
                    }
                };

                if (Application.Current != null && Application.Current.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
                {
                    Application.Current.Dispatcher.Invoke(addMessage);
                }
                else
                {
                    addMessage();
                }
            }
        }

        /// <summary>
        /// 判断是否应该显示BM消息（根据过滤条件）
        /// </summary>
        private bool ShouldShowBMMessage(BMMessageItem msg)
        {
            // RT地址过滤
            if (BMFilterRTAddress >= 0 && msg.RTAddress != BMFilterRTAddress)
                return false;

            // 消息类型过滤
            if (BMFilterMessageType != "全部" && msg.MessageType != BMFilterMessageType)
                return false;

            return true;
        }

        /// <summary>
        /// 过滤BM消息列表
        /// </summary>
        private void FilterBMMessages()
        {
            // 注意：这里我们不在UI线程上过滤，而是让AddBMMessage时应用过滤
            // 如果需要重新过滤已存在的消息，可以在这里实现
        }

        /// <summary>
        /// 清空BM消息列表
        /// </summary>
        private void ClearBMMessages()
        {
            Action clearAction = () =>
            {
                lock (_bmMessagesLock)
                {
                    BMMessages.Clear();
                    BMTotalMessageCount = 0;
                    BMErrorMessageCount = 0;
                }
            };

            if (Application.Current != null && Application.Current.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
            {
                Application.Current.Dispatcher.Invoke(clearAction);
            }
            else
            {
                clearAction();
            }
        }

        #endregion

        #region 辅助类

        /// <summary>
        /// 通道配置（每个通道的独立配置）
        /// </summary>
        private class ChannelConfig
        {
            // BC模式配置
            public ushort BCResponseTimeout { get; set; } = 4000;
            public int BCFrameGap { get; set; } = 10;
            public int BCRetryCount { get; set; } = 0;
            public int BCTargetRTAddress { get; set; } = 1;
            public int BCTargetSubAddress { get; set; } = 1;
            public List<MessageConfigItem> MessageConfigs { get; set; } = new List<MessageConfigItem>();
            
            // RT模式配置
            public int RTAddress { get; set; } = 1;
            public int SubAddress { get; set; } = 1;
            public ushort RTResponseTime { get; set; } = 500;
            public string RTTxMode { get; set; } = "SingleBuffer";
            public List<RTConfigItem> RTConfigs { get; set; } = new List<RTConfigItem>();
            
            // RT内部子地址配置
            public List<RTSubAddressConfigItem> RTSubAddressConfigs { get; set; } = new List<RTSubAddressConfigItem>();
            
            // 通道工作模式（BC/RT/BM） - 每个通道单独保存
            public string TerminalMode { get; set; }
        }

        /// <summary>
        /// 总线接口配置（缓存用）
        /// </summary>
        private class BusInterfaceConfig
        {
            public string TerminalMode { get; set; }
            public string TransmissionRate { get; set; }
            public List<ChannelConfigItem> ChannelConfigs { get; set; }
            
            // 当前选中的通道
            public int SelectedChannel { get; set; }
            
            // 通道0和通道1的独立配置
            public ChannelConfig Channel0Config { get; set; } = new ChannelConfig();
            public ChannelConfig Channel1Config { get; set; } = new ChannelConfig();
        }

        /// <summary>
        /// 消息配置项
        /// </summary>
        /// <summary>
        /// BC消息配置项（参考官方例程的消息结构）
        /// </summary>
        public class MessageConfigItem : BindableBase
        {
            private int _messageId;
            private string _messageName;
            private string _messageType; // BC->RT, RT->BC, RT->RT, Mode Code, Broadcast等
            private int _rtAddress; // RT地址（0-31）
            private int _subAddress; // 子地址（0-31，0和31为特殊用途）
            private int _dataLength; // 数据字数（0-32，0表示32个字）
            private bool _isEnabled;
            private int _messageGap; // 消息间隔（单位1us，最小4us）
            private int _channelSelect; // 通道选择（0:Channel B, 1:Channel A）
            private bool _retryEnable; // 是否重试
            private int _modeCode; // Mode Code值（仅Mode Code消息使用）
            private int _rtAddress2; // RT->RT消息的第二个RT地址
            private int _subAddress2; // RT->RT消息的第二个子地址
            private string _dataHex; // 数据内容（Hex格式）
            private int _period = 1000; // 周期 ms，默认1000ms
            private int _initPeriod = 0; // 初始延时 ms
            private bool _run = true; // 是否运行

            public int MessageId
            {
                get => _messageId;
                set => SetProperty(ref _messageId, value);
            }

            public string MessageName
            {
                get => _messageName;
                set => SetProperty(ref _messageName, value);
            }

            public string MessageType
            {
                get => _messageType;
                set => SetProperty(ref _messageType, value);
            }

            public int RTAddress
            {
                get => _rtAddress;
                set => SetProperty(ref _rtAddress, value);
            }

            public int SubAddress
            {
                get => _subAddress;
                set => SetProperty(ref _subAddress, value);
            }

            public int DataLength
            {
                get => _dataLength;
                set => SetProperty(ref _dataLength, value);
            }

            public bool IsEnabled
            {
                get => _isEnabled;
                set => SetProperty(ref _isEnabled, value);
            }

            public int MessageGap
            {
                get => _messageGap;
                set => SetProperty(ref _messageGap, value);
            }

            public int ChannelSelect
            {
                get => _channelSelect;
                set => SetProperty(ref _channelSelect, value);
            }

            public bool RetryEnable
            {
                get => _retryEnable;
                set => SetProperty(ref _retryEnable, value);
            }

            public int ModeCode
            {
                get => _modeCode;
                set => SetProperty(ref _modeCode, value);
            }

            public int RTAddress2
            {
                get => _rtAddress2;
                set => SetProperty(ref _rtAddress2, value);
            }

            public int SubAddress2
            {
                get => _subAddress2;
                set => SetProperty(ref _subAddress2, value);
            }

            public string DataHex
            {
                get => _dataHex;
                set => SetProperty(ref _dataHex, value);
            }

            public int Period
            {
                get => _period;
                set => SetProperty(ref _period, value);
            }

            public int InitPeriod
            {
                get => _initPeriod;
                set => SetProperty(ref _initPeriod, value);
            }

            public bool Run
            {
                get => _run;
                set => SetProperty(ref _run, value);
            }

            /// <summary>
            /// 显示摘要信息
            /// </summary>
            public string Summary => $"{MessageName} ({MessageType}) RT{RTAddress} SA{SubAddress} Len{DataLength}";
        }

        /// <summary>
        /// 通道配置项
        /// </summary>
        public class ChannelConfigItem : BindableBase
        {
            private string _channelName;
            private bool _isEnabled;
            private bool _isPrimary;

            public string ChannelName
            {
                get => _channelName;
                set => SetProperty(ref _channelName, value);
            }

            public bool IsEnabled
            {
                get => _isEnabled;
                set => SetProperty(ref _isEnabled, value);
            }

            public bool IsPrimary
            {
                get => _isPrimary;
                set => SetProperty(ref _isPrimary, value);
            }
        }

        #endregion

        #region ICloseGuard Implementation

        public bool CanClose()
        {
            if (HasPendingChanges)
            {
                var result = ReMessageBox.Show("存在未保存的更改，是否保存？",
                    "提示",
                    System.Windows.MessageBoxButton.YesNoCancel,
                    System.Windows.MessageBoxImage.Question);

                if (result == System.Windows.MessageBoxResult.Yes)
                {
                    SaveConfig();
                    return true;
                }
                else if (result == System.Windows.MessageBoxResult.No)
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }

            return true;
        }

        #endregion

        #region IDisposable

        private bool _disposed = false;
        private bool _isTesting;

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
                // 取消订阅事件
                if (_testTaskCreatedToken != null)
                {
                    _eventAggregator?.GetEvent<TestTaskCreatedEvent>()?.Unsubscribe(_testTaskCreatedToken);
                    _testTaskCreatedToken = null;
                }

                // 断开设备连接
                _ = Task.Run(async () => await DisconnectDeviceAsync());
            }

            _disposed = true;
        }

        #endregion
    }

    // ========== 配置类定义（直接放在同一个文件中）==========

    /// <summary>
    /// MIL-1553B板卡配置
    /// </summary>
    public class ART1553BCardConfig : CardConfigDataBase
    {
        public override string CardType => "MIL-1553B";

        private string _terminalMode; // BC/RT/BM
        private string _transmissionRate; // 传输速率
        private ObservableCollection<ART1553BMessageConfig> _messageConfigs;
        private ObservableCollection<ART1553BChannelConfig> _channelConfigs;
        private ObservableCollection<ART1553BTestTaskConfig> _testTaskConfigs;
        private string _lastSelectedTestTask;

        /// <summary>终端模式</summary>
        public string TerminalMode
        {
            get => _terminalMode;
            set => SetProperty(ref _terminalMode, value);
        }

        /// <summary>传输速率</summary>
        public string TransmissionRate
        {
            get => _transmissionRate;
            set => SetProperty(ref _transmissionRate, value);
        }

        /// <summary>消息配置列表</summary>
        public ObservableCollection<ART1553BMessageConfig> MessageConfigs
        {
            get => _messageConfigs ??= new ObservableCollection<ART1553BMessageConfig>();
            set => SetProperty(ref _messageConfigs, value);
        }

        /// <summary>通道配置列表</summary>
        public ObservableCollection<ART1553BChannelConfig> ChannelConfigs
        {
            get => _channelConfigs ??= new ObservableCollection<ART1553BChannelConfig>();
            set => SetProperty(ref _channelConfigs, value);
        }

        /// <summary>不同测试任务的独立配置</summary>
        public ObservableCollection<ART1553BTestTaskConfig> TestTaskConfigs
        {
            get => _testTaskConfigs ??= new ObservableCollection<ART1553BTestTaskConfig>();
            set => SetProperty(ref _testTaskConfigs, value);
        }

        /// <summary>上次选中的测试任务</summary>
        public string LastSelectedTestTask
        {
            get => _lastSelectedTestTask;
            set => SetProperty(ref _lastSelectedTestTask, value);
        }

        public ART1553BCardConfig()
        {
            TerminalMode = "BC - 总线控制器";
            TransmissionRate = "1 Mbps";
        }
    }

    /// <summary>
    /// 1553B消息配置（为避免命名冲突，添加ART1553B前缀）
    /// </summary>
    /// <summary>
    /// ART1553B消息配置（用于序列化保存）
    /// </summary>
    public class ART1553BMessageConfig : BindableBase
    {
        private int _messageId;
        private string _messageName;
        private string _messageType;
        private int _rtAddress;
        private int _subAddress;
        private int _dataLength;
        private bool _isEnabled;
        private int _messageGap;
        private int _channelSelect;
        private bool _retryEnable;
        private int _modeCode;
        private int _rtAddress2;
        private int _subAddress2;
        private string _dataHex;

        public int MessageId
        {
            get => _messageId;
            set => SetProperty(ref _messageId, value);
        }

        public string MessageName
        {
            get => _messageName;
            set => SetProperty(ref _messageName, value);
        }

        public string MessageType
        {
            get => _messageType;
            set => SetProperty(ref _messageType, value);
        }

        public int RTAddress
        {
            get => _rtAddress;
            set => SetProperty(ref _rtAddress, value);
        }

        public int SubAddress
        {
            get => _subAddress;
            set => SetProperty(ref _subAddress, value);
        }

        public int DataLength
        {
            get => _dataLength;
            set => SetProperty(ref _dataLength, value);
        }

        public bool IsEnabled
        {
            get => _isEnabled;
            set => SetProperty(ref _isEnabled, value);
        }

        public int MessageGap
        {
            get => _messageGap;
            set => SetProperty(ref _messageGap, value);
        }

        public int ChannelSelect
        {
            get => _channelSelect;
            set => SetProperty(ref _channelSelect, value);
        }

        public bool RetryEnable
        {
            get => _retryEnable;
            set => SetProperty(ref _retryEnable, value);
        }

        public int ModeCode
        {
            get => _modeCode;
            set => SetProperty(ref _modeCode, value);
        }

        public int RTAddress2
        {
            get => _rtAddress2;
            set => SetProperty(ref _rtAddress2, value);
        }

        public int SubAddress2
        {
            get => _subAddress2;
            set => SetProperty(ref _subAddress2, value);
        }

        public string DataHex
        {
            get => _dataHex;
            set => SetProperty(ref _dataHex, value);
        }
    }

    /// <summary>
    /// RT内部子地址配置项（1-30）
    /// </summary>
    public class RTSubAddressConfigItem : BindableBase
    {
        private int _subAddress;
        private bool _receiveEnabled;
        private bool _transmitEnabled;
        private int _dataLength;
        private string _sendDataHex;

        public int SubAddress
        {
            get => _subAddress;
            set => SetProperty(ref _subAddress, value);
        }

        public bool ReceiveEnabled
        {
            get => _receiveEnabled;
            set => SetProperty(ref _receiveEnabled, value);
        }

        public bool TransmitEnabled
        {
            get => _transmitEnabled;
            set => SetProperty(ref _transmitEnabled, value);
        }

        public int DataLength
        {
            get => _dataLength;
            set => SetProperty(ref _dataLength, value);
        }

        public string SendDataHex
        {
            get => _sendDataHex;
            set => SetProperty(ref _sendDataHex, value);
        }
    }

    /// <summary>
    /// RT配置项（0-31个RT）
    /// </summary>
    public class RTConfigItem : BindableBase
    {
        private int _rtAddress;
        private int _subAddress;
        private ushort _responseTime;
        private bool _isEnabled;
        private string _txMode;

        /// <summary>
        /// RT地址（0-31）
        /// </summary>
        public int RTAddress
        {
            get => _rtAddress;
            set => SetProperty(ref _rtAddress, value);
        }

        /// <summary>
        /// 子地址（1-30）
        /// </summary>
        public int SubAddress
        {
            get => _subAddress;
            set => SetProperty(ref _subAddress, value);
        }

        /// <summary>
        /// 响应时间（0.5us单位）
        /// </summary>
        public ushort ResponseTime
        {
            get => _responseTime;
            set => SetProperty(ref _responseTime, value);
        }

        /// <summary>
        /// 是否使能
        /// </summary>
        public bool IsEnabled
        {
            get => _isEnabled;
            set => SetProperty(ref _isEnabled, value);
        }

        /// <summary>
        /// 发送模式（SingleBuffer/CircularBuffer）
        /// </summary>
        public string TxMode
        {
            get => _txMode;
            set => SetProperty(ref _txMode, value);
        }

        /// <summary>
        /// 显示名称
        /// </summary>
        public string DisplayName => $"RT {RTAddress}";
    }

    /// <summary>
    /// 1553B通道配置（为避免命名冲突，添加ART1553B前缀）
    /// </summary>
    public class ART1553BChannelConfig : BindableBase
    {
        private string _channelName;
        private bool _isEnabled;
        private bool _isPrimary;

        public string ChannelName
        {
            get => _channelName;
            set => SetProperty(ref _channelName, value);
        }

        public bool IsEnabled
        {
            get => _isEnabled;
            set => SetProperty(ref _isEnabled, value);
        }

        public bool IsPrimary
        {
            get => _isPrimary;
            set => SetProperty(ref _isPrimary, value);
        }

    }

    /// <summary>
    /// 1553B测试任务配置
    /// </summary>
    public class ART1553BTestTaskConfig : BindableBase
    {
        private string _testTaskName;
        private ObservableCollection<ART1553BMessageConfig> _messageConfigs;
        private ObservableCollection<ART1553BChannelConfig> _channelConfigs;

        public string TestTaskName
        {
            get => _testTaskName;
            set => SetProperty(ref _testTaskName, value);
        }

        public ObservableCollection<ART1553BMessageConfig> MessageConfigs
        {
            get => _messageConfigs ??= new ObservableCollection<ART1553BMessageConfig>();
            set => SetProperty(ref _messageConfigs, value);
        }

        public ObservableCollection<ART1553BChannelConfig> ChannelConfigs
        {
            get => _channelConfigs ??= new ObservableCollection<ART1553BChannelConfig>();
            set => SetProperty(ref _channelConfigs, value);
        }

        public ART1553BTestTaskConfig()
        {
        }
    }

    /// <summary>
    /// BM监控消息项
    /// </summary>
    public class BMMessageItem : BindableBase
    {
        private DateTime _timestamp;
        private int _channel;
        private int _rtAddress;
        private int _subAddress;
        private string _messageType;
        private string _commandWord;
        private string _statusWord;
        private string _dataHex;
        private int _dataWordCount;
        private bool _hasError;
        private string _errorInfo;

        /// <summary>
        /// 时间戳
        /// </summary>
        public DateTime Timestamp
        {
            get => _timestamp;
            set => SetProperty(ref _timestamp, value);
        }

        /// <summary>
        /// 通道
        /// </summary>
        public int Channel
        {
            get => _channel;
            set => SetProperty(ref _channel, value);
        }

        /// <summary>
        /// RT地址
        /// </summary>
        public int RTAddress
        {
            get => _rtAddress;
            set => SetProperty(ref _rtAddress, value);
        }

        /// <summary>
        /// 子地址
        /// </summary>
        public int SubAddress
        {
            get => _subAddress;
            set => SetProperty(ref _subAddress, value);
        }

        /// <summary>
        /// 消息类型（BC->RT/RT->BC/RT->RT/广播/模式码）
        /// </summary>
        public string MessageType
        {
            get => _messageType;
            set => SetProperty(ref _messageType, value);
        }

        /// <summary>
        /// 命令字（十六进制）
        /// </summary>
        public string CommandWord
        {
            get => _commandWord;
            set => SetProperty(ref _commandWord, value);
        }

        /// <summary>
        /// 状态字（十六进制）
        /// </summary>
        public string StatusWord
        {
            get => _statusWord;
            set => SetProperty(ref _statusWord, value);
        }

        /// <summary>
        /// 数据（十六进制）
        /// </summary>
        public string DataHex
        {
            get => _dataHex;
            set => SetProperty(ref _dataHex, value);
        }

        /// <summary>
        /// 数据字数
        /// </summary>
        public int DataWordCount
        {
            get => _dataWordCount;
            set => SetProperty(ref _dataWordCount, value);
        }

        /// <summary>
        /// 是否有错误
        /// </summary>
        public bool HasError
        {
            get => _hasError;
            set => SetProperty(ref _hasError, value);
        }

        /// <summary>
        /// 错误信息
        /// </summary>
        public string ErrorInfo
        {
            get => _errorInfo;
            set => SetProperty(ref _errorInfo, value);
        }

        /// <summary>
        /// 时间戳显示字符串
        /// </summary>
        public string TimestampString => Timestamp.ToString("HH:mm:ss.fff");

        /// <summary>
        /// 消息摘要（用于列表显示）
        /// </summary>
        public string Summary => $"[{TimestampString}] Ch{Channel} RT{RTAddress} SA{SubAddress} {MessageType}";
    }
}