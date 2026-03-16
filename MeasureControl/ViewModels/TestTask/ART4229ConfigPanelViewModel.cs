using MeasureControl.Drivers;
using MeasureControl.Drivers.ART4229;
using MeasureControl.Models.Devices;
using MeasureControl.Services;
using MeasureControl.Views.Dialogs;
using Prism.Commands;
using Prism.Events;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace MeasureControl.ViewModels.TestTask
{
    /// <summary>
    /// 发送数据项模型
    /// </summary>
    public class TxDataItem : INotifyPropertyChanged
    {
        private int _index;
        private string _data429Hex;  // 429数据(Hex)
        private string _label;       // Label八进制
        private int _sendPeriod;     // 发送周期
        private int _sendCount;      // 发送次数
        private int _wordInterval;   // 字间隔
        private int _parity;         // 校验

        public int Index { get => _index; set { _index = value; OnPropertyChanged(); } }
        public string Data429Hex { get => _data429Hex; set { _data429Hex = value; OnPropertyChanged(); } }
        public string Label { get => _label; set { _label = value; OnPropertyChanged(); } }
        public int SendPeriod { get => _sendPeriod; set { _sendPeriod = value; OnPropertyChanged(); } }
        public int SendCount { get => _sendCount; set { _sendCount = value; OnPropertyChanged(); } }
        public int WordInterval { get => _wordInterval; set { _wordInterval = value; OnPropertyChanged(); } }
        public int Parity { get => _parity; set { _parity = value; OnPropertyChanged(); } }
        
        public string ParityText => Parity == 0 ? "None" : (Parity == 1 ? "ODD" : "EVEN");

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>
    /// 接收数据项模型
    /// </summary>
    public class RxDataItem : INotifyPropertyChanged
    {
        private int _index;
        private string _data429Hex;  // 原始数据(Hex)
        private string _label;       // Label八进制
        private string _sdi;         // SDI (2位)
        private string _data;        // Data (19位二进制)
        private string _ssm;         // SSM (2位)
        private string _parityBit;   // 校验位
        private string _parityCheck; // 校验状态(OK/ERR/-)
        private double _rate;        // 码率
        private string _timeStamp;   // 时标

        public int Index { get => _index; set { _index = value; OnPropertyChanged(); } }
        public string Data429Hex { get => _data429Hex; set { _data429Hex = value; OnPropertyChanged(); } }
        public string Label { get => _label; set { _label = value; OnPropertyChanged(); } }
        public string SDI { get => _sdi; set { _sdi = value; OnPropertyChanged(); } }
        public string Data { get => _data; set { _data = value; OnPropertyChanged(); } }
        public string SSM { get => _ssm; set { _ssm = value; OnPropertyChanged(); } }
        public string ParityBit { get => _parityBit; set { _parityBit = value; OnPropertyChanged(); } }
        public string ParityCheck { get => _parityCheck; set { _parityCheck = value; OnPropertyChanged(); } }
        public double Rate { get => _rate; set { _rate = value; OnPropertyChanged(); } }
        public string TimeStamp { get => _timeStamp; set { _timeStamp = value; OnPropertyChanged(); } }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>
    /// 通道配置数据（TX/RX各一套）
    /// </summary>
    public class ChannelConfigData : INotifyPropertyChanged
    {
        // 通用配置
        private double _rate = 100000;
        private int _parity = 0;  // 0=None, 1=ODD, 2=EVEN
        private int _wordFormat = 0;  // 0=标准429
        
        // 接收配置
        private bool _enableTimeTag = false;
        private bool _enableInterrupt = false;
        private int _interruptDepth = 10;

        private static readonly string[] InterruptModeOptions = { "关闭", "打开" };
        private static readonly int[] InterruptDepthOptionValues = { 10, 20, 50, 100 };
        
        // 发送配置
        private int _sendMode = 0;  // 0=Single, 1=Period
        private int _sendPeriod = 200;  // 发送周期(ms)
        private int _sendCount = 1;  // 发送次数
        private int _wordInterval = 4;  // 字间隔[4,64]
        
        // 运行状态
        private bool _isRunning = false;

        // 码率选项数组
        private static readonly double[] RateOptions = { 100000, 50000, 12500 };
        
        // 通用配置属性
        public double Rate { get => _rate; set { _rate = value; OnPropertyChanged(); OnPropertyChanged(nameof(RateIndex)); } }
        public int Parity { get => _parity; set { _parity = value; OnPropertyChanged(); } }
        public int WordFormat { get => _wordFormat; set { _wordFormat = value; OnPropertyChanged(); } }
        
        // 码率索引（用于ComboBox绑定）
        public int RateIndex
        {
            get
            {
                for (int i = 0; i < RateOptions.Length; i++)
                    if (Math.Abs(RateOptions[i] - _rate) < 0.1) return i;
                return 0;
            }
            set
            {
                if (value >= 0 && value < RateOptions.Length)
                {
                    _rate = RateOptions[value];
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(Rate));
                }
            }
        }
        
        // 接收配置属性
        public bool EnableTimeTag { get => _enableTimeTag; set { _enableTimeTag = value; OnPropertyChanged(); } }
        public bool EnableInterrupt { get => _enableInterrupt; set { _enableInterrupt = value; OnPropertyChanged(); OnPropertyChanged(nameof(InterruptIndex)); } }
        public int InterruptDepth { get => _interruptDepth; set { _interruptDepth = value; OnPropertyChanged(); } }

        public string[] InterruptOptions => InterruptModeOptions;
        public int[] InterruptDepthOptions => InterruptDepthOptionValues;

        public int InterruptIndex
        {
            get => _enableInterrupt ? 1 : 0;
            set
            {
                bool newEnable = value == 1;
                if (_enableInterrupt != newEnable)
                {
                    _enableInterrupt = newEnable;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(EnableInterrupt));
                }
            }
        }
        
        // 发送周期选项数组
        private static readonly int[] SendPeriodOptions = { 100, 200, 500, 1000 };
        
        // 发送配置属性
        public int SendMode { get => _sendMode; set { _sendMode = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsPeriodMode)); } }
        public int SendPeriod { get => _sendPeriod; set { _sendPeriod = value; OnPropertyChanged(); OnPropertyChanged(nameof(SendPeriodIndex)); } }
        public int SendCount { get => _sendCount; set { _sendCount = value; OnPropertyChanged(); } }
        public int WordInterval { get => _wordInterval; set { _wordInterval = value; OnPropertyChanged(); } }
        
        // 发送周期索引（用于ComboBox绑定）
        public int SendPeriodIndex
        {
            get
            {
                for (int i = 0; i < SendPeriodOptions.Length; i++)
                    if (SendPeriodOptions[i] == _sendPeriod) return i;
                return 1;  // 默认200ms
            }
            set
            {
                if (value >= 0 && value < SendPeriodOptions.Length)
                {
                    _sendPeriod = SendPeriodOptions[value];
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(SendPeriod));
                }
            }
        }
        
        // 是否为周期发送模式
        public bool IsPeriodMode => SendMode == 1;
        
        // 运行状态属性
        public bool IsRunning { get => _isRunning; set { _isRunning = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusText)); } }
        public string StatusText => IsRunning ? "正在运行" : "未运行";

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>
    /// ART4229 通道状态模型
    /// </summary>
    public class Art4229ChannelStatus : INotifyPropertyChanged
    {
        private int _channelIndex;
        private string _channelName;
        private bool _isActive;
        private bool _isSelected;
        private bool _isTx;
        private ChannelConfigData _txConfig;
        private ChannelConfigData _rxConfig;
        private ObservableCollection<TxDataItem> _txDataList;
        private ObservableCollection<RxDataItem> _rxDataList;

        public Art4229ChannelStatus()
        {
            _txConfig = new ChannelConfigData();
            _rxConfig = new ChannelConfigData();
            _txDataList = new ObservableCollection<TxDataItem>();
            _rxDataList = new ObservableCollection<RxDataItem>();
        }

        /// <summary>
        /// 该通道的发送数据列表
        /// </summary>
        public ObservableCollection<TxDataItem> TxDataList
        {
            get => _txDataList;
            set => SetProperty(ref _txDataList, value);
        }

        /// <summary>
        /// 该通道的接收数据列表
        /// </summary>
        public ObservableCollection<RxDataItem> RxDataList
        {
            get => _rxDataList;
            set => SetProperty(ref _rxDataList, value);
        }

        public int ChannelIndex
        {
            get => _channelIndex;
            set => SetProperty(ref _channelIndex, value);
        }

        public string ChannelName
        {
            get => _channelName;
            set => SetProperty(ref _channelName, value);
        }

        public bool IsActive
        {
            get => _isActive;
            set => SetProperty(ref _isActive, value);
        }

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        public bool IsTx
        {
            get => _isTx;
            set
            {
                if (SetProperty(ref _isTx, value))
                {
                    OnPropertyChanged(nameof(CurrentConfig));
                    OnPropertyChanged(nameof(DirectionText));
                }
            }
        }

        /// <summary>
        /// 发送配置
        /// </summary>
        public ChannelConfigData TxConfig
        {
            get => _txConfig;
            set => SetProperty(ref _txConfig, value);
        }

        /// <summary>
        /// 接收配置
        /// </summary>
        public ChannelConfigData RxConfig
        {
            get => _rxConfig;
            set => SetProperty(ref _rxConfig, value);
        }

        /// <summary>
        /// 当前方向对应的配置
        /// </summary>
        public ChannelConfigData CurrentConfig => IsTx ? TxConfig : RxConfig;

        /// <summary>
        /// 方向文本
        /// </summary>
        public string DirectionText => IsTx ? "发送" : "接收";

        public event PropertyChangedEventHandler PropertyChanged;

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// ART4229 板卡配置面板 ViewModel
    /// 专注于 ARINC 429 硬件连接功能
    /// </summary>
    public class ART4229ConfigPanelViewModel : INotifyPropertyChanged, IDisposable
    {
        #region 私有字段

        private DeviceBase _device;
        private string _chassisName;
        private string _cardModel;
        private string _cardName;
        private bool _isConnected;
        private string _connectionStatus;
        private ART4229Driver _driver;
        private Art4229ChannelStatus _selectedChannel;
        private string _selectedChannelInfo;
        private string _channelConfigInfo;

        private readonly IPxiChassisService _pxiChassisService;
        private readonly IEventAggregator _eventAggregator;

        // 字结构输入
        private string _labelSsmDataSdi = "6C532A";  // SSM/Data/SDI
        private int _labelOctalIndex = 20;  // 八进制标签索引(对应024)
        private string _data429Hex;  // 拼接后的429数据
        private bool _isDataIncrement;  // 数据递增勾选状态
        
        // 发送数据列表
        private TxDataItem _selectedTxDataItem;
        
        // 接收数据列表
        private RxDataItem _selectedRxDataItem;
        
        // 发送/接收状态
        private bool _isSending;
        private bool _isReceiving;
        private System.Threading.CancellationTokenSource _receiveCts;

        #endregion

        private static int ComputeParityBit(uint word31to0)
        {
            // 返回 bit31 应该为 0/1，使得整个32bit满足“奇/偶校验”
            // 统计 bit0..30 中的1个数
            int ones = 0;
            uint v = word31to0 & 0x7FFFFFFF;
            while (v != 0)
            {
                v &= (v - 1);
                ones++;
            }
            // 偶校验：总1数为偶数 => parityBit = ones%2
            // 奇校验：总1数为奇数 => parityBit = (ones%2)==0 ? 1 : 0
            return (ones & 1);
        }

        private static uint ApplyParity(uint rawWord, int parityMode)
        {
            // parityMode: 0=None, 1=ODD, 2=EVEN
            if (parityMode == 0)
                return rawWord;

            int evenParityBit = ComputeParityBit(rawWord);
            int parityBit = parityMode == 2 ? evenParityBit : (evenParityBit == 0 ? 1 : 0);

            uint cleared = rawWord & 0x7FFFFFFF;
            return cleared | ((uint)parityBit << 31);
        }

        private static bool ValidateParity(uint rawWord, int parityMode)
        {
            if (parityMode == 0)
                return true;

            int actual = (int)((rawWord >> 31) & 0x01);
            int evenParityBit = ComputeParityBit(rawWord);
            int expected = parityMode == 2 ? evenParityBit : (evenParityBit == 0 ? 1 : 0);
            return actual == expected;
        }

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
        public bool IsConnected
        {
            get => _isConnected;
            set
            {
                if (SetProperty(ref _isConnected, value))
                {
                    // 更新连接状态文本
                    ConnectionStatus = value ? "在线" : "离线";
                    
                    // 通知命令执行状态变化
                    CommandManager.InvalidateRequerySuggested();
                    
                    // 更新所有通道状态
                    UpdateAllChannelsState();
                }
            }
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
        /// 设备信息文本
        /// </summary>
        private string _deviceInfoText;
        public string DeviceInfoText
        {
            get => _deviceInfoText;
            set => SetProperty(ref _deviceInfoText, value);
        }

        /// <summary>
        /// 通道集合（16个通道，0-15）
        /// </summary>
        public ObservableCollection<Art4229ChannelStatus> Channels { get; private set; }

        /// <summary>
        /// 选中的通道
        /// </summary>
        public Art4229ChannelStatus SelectedChannel
        {
            get => _selectedChannel;
            set
            {
                if (SetProperty(ref _selectedChannel, value))
                {
                    UpdateSelectedChannelInfo();
                    // 更新选中状态
                    foreach (var channel in Channels)
                    {
                        channel.IsSelected = (channel == value);
                    }
                }
            }
        }

        /// <summary>
        /// 选中通道信息
        /// </summary>
        public string SelectedChannelInfo
        {
            get => _selectedChannelInfo;
            set => SetProperty(ref _selectedChannelInfo, value);
        }

        /// <summary>
        /// 通道配置信息
        /// </summary>
        public string ChannelConfigInfo
        {
            get => _channelConfigInfo;
            set => SetProperty(ref _channelConfigInfo, value);
        }

        /// <summary>
        /// Label的SSM/Data/SDI部分（6位十六进制）
        /// </summary>
        public string LabelSsmDataSdi
        {
            get => _labelSsmDataSdi;
            set
            {
                if (SetProperty(ref _labelSsmDataSdi, value))
                    UpdateData429Hex();
            }
        }

        /// <summary>
        /// 八进制标签索引（ComboBox选中项索引）
        /// </summary>
        public int LabelOctalIndex
        {
            get => _labelOctalIndex;
            set
            {
                if (SetProperty(ref _labelOctalIndex, value))
                    UpdateData429Hex();
            }
        }

        /// <summary>
        /// 八进制标签选项列表(000~377，共256个)
        /// </summary>
        public string[] OctalLabelOptions { get; } = GenerateOctalLabels();

        private static string[] GenerateOctalLabels()
        {
            var labels = new string[256];
            for (int i = 0; i < 256; i++)
                labels[i] = Convert.ToString(i, 8).PadLeft(3, '0');
            return labels;
        }

        /// <summary>
        /// 拼接后的429数据（只读）
        /// </summary>
        public string Data429Hex
        {
            get => _data429Hex;
            private set => SetProperty(ref _data429Hex, value);
        }

        /// <summary>
        /// 发送数据列表
        /// </summary>
        public ObservableCollection<TxDataItem> TxDataList { get; } = new ObservableCollection<TxDataItem>();

        /// <summary>
        /// 选中的发送数据项
        /// </summary>
        public TxDataItem SelectedTxDataItem
        {
            get => _selectedTxDataItem;
            set => SetProperty(ref _selectedTxDataItem, value);
        }

        /// <summary>
        /// 接收数据列表
        /// </summary>
        public ObservableCollection<RxDataItem> RxDataList { get; } = new ObservableCollection<RxDataItem>();

        /// <summary>
        /// 选中的接收数据项
        /// </summary>
        public RxDataItem SelectedRxDataItem
        {
            get => _selectedRxDataItem;
            set => SetProperty(ref _selectedRxDataItem, value);
        }

        /// <summary>
        /// 是否正在发送
        /// </summary>
        public bool IsSending
        {
            get => _isSending;
            set => SetProperty(ref _isSending, value);
        }

        /// <summary>
        /// 是否正在接收
        /// </summary>
        public bool IsReceiving
        {
            get => _isReceiving;
            set => SetProperty(ref _isReceiving, value);
        }

        /// <summary>
        /// 数据递增勾选状态
        /// </summary>
        public bool IsDataIncrement
        {
            get => _isDataIncrement;
            set => SetProperty(ref _isDataIncrement, value);
        }

        #endregion

        #region 命令

        /// <summary>
        /// 切换设备连接状态命令
        /// </summary>
        public ICommand ToggleDeviceCommand { get; private set; }

        /// <summary>
        /// 选择通道命令
        /// </summary>
        public ICommand SelectChannelCommand { get; private set; }

        /// <summary>
        /// 打开通道命令
        /// </summary>
        public ICommand OpenChannelCommand { get; private set; }

        /// <summary>
        /// 关闭通道命令
        /// </summary>
        public ICommand CloseChannelCommand { get; private set; }

        /// <summary>
        /// 切换通道方向命令
        /// </summary>
        public ICommand SwitchChannelDirectionCommand { get; private set; }

        /// <summary>
        /// 配置通道命令
        /// </summary>
        public ICommand ConfigureChannelCommand { get; private set; }

        /// <summary>
        /// 添加数据命令
        /// </summary>
        public ICommand AddTxDataCommand { get; private set; }

        /// <summary>
        /// 修改数据命令
        /// </summary>
        public ICommand ModifyTxDataCommand { get; private set; }

        /// <summary>
        /// 删除选定数据命令
        /// </summary>
        public ICommand DeleteTxDataCommand { get; private set; }

        /// <summary>
        /// 开始发送命令
        /// </summary>
        public ICommand StartSendCommand { get; private set; }

        /// <summary>
        /// 停止发送命令
        /// </summary>
        public ICommand StopSendCommand { get; private set; }

        /// <summary>
        /// 开始接收命令
        /// </summary>
        public ICommand StartReceiveCommand { get; private set; }

        /// <summary>
        /// 停止接收命令
        /// </summary>
        public ICommand StopReceiveCommand { get; private set; }

        /// <summary>
        /// 清空接收数据命令
        /// </summary>
        public ICommand ClearRxDataCommand { get; private set; }

        /// <summary>
        /// 打开所有通道命令
        /// </summary>
        public ICommand OpenAllChannelsCommand { get; private set; }

        /// <summary>
        /// 关闭所有通道命令
        /// </summary>
        public ICommand CloseAllChannelsCommand { get; private set; }

        #endregion

        #region 构造函数

        /// <summary>
        /// 默认构造函数
        /// </summary>
        public ART4229ConfigPanelViewModel()
        {
            InitializeChannels();
            InitializeCommands();
            ConnectionStatus = "离线";
            _deviceInfoText = "未连接设备";
            SelectedChannelInfo = "未选择通道";
            ChannelConfigInfo = "请选择通道";
            UpdateData429Hex();  // 初始化429数据
        }

        /// <summary>
        /// 使用指定的设备初始化 ViewModel
        /// </summary>
        public ART4229ConfigPanelViewModel(DeviceBase device, string chassisName,
            IPxiChassisService pxiChassisService = null, IEventAggregator eventAggregator = null) : this()
        {
            Device = device;
            ChassisName = chassisName;
            CardModel = device?.Model ?? "ART4229";
            CardName = !string.IsNullOrEmpty(device?.CardName) ? device.CardName : device?.Model ?? "ART4229";
            _pxiChassisService = pxiChassisService;
            _eventAggregator = eventAggregator;

            // 尝试恢复缓存的驱动
            TryRestoreCachedDriver();
        }

        #endregion

        #region 初始化方法

        /// <summary>
        /// 初始化通道集合（16个通道，0-15）
        /// </summary>
        private void InitializeChannels()
        {
            Channels = new ObservableCollection<Art4229ChannelStatus>();
            
            // 创建16个通道（8个TX + 8个RX）
            for (int i = 0; i < 16; i++)
            {
                bool isTx = i < 8;  // 0-7为TX，8-15为RX
                var channel = new Art4229ChannelStatus
                {
                    ChannelIndex = i,
                    ChannelName = $"通道{i}",
                    IsActive = false,
                    IsSelected = false,
                    IsTx = isTx
                };
                Channels.Add(channel);
            }
        }

        /// <summary>
        /// 初始化命令
        /// </summary>
        private void InitializeCommands()
        {
            ToggleDeviceCommand = new DelegateCommand(async () =>
            {
                if (!IsConnected)
                {
                    await OnOpenDeviceAsync();
                }
                else
                {
                    await OnCloseDeviceAsync();
                }
            });

            SelectChannelCommand = new DelegateCommand<Art4229ChannelStatus>(channel =>
            {
                if (channel != null)
                {
                    SelectedChannel = channel;
                    Debug.WriteLine($"[ART4229] 选中通道: {channel.ChannelName}");
                }
            });

            OpenChannelCommand = new DelegateCommand<Art4229ChannelStatus>(async channel =>
            {
                if (channel != null && IsConnected)
                {
                    await OpenChannelAsync(channel);
                    Debug.WriteLine($"[ART4229 VeiwModel] 正在调用驱动打开通道");
                }
                else
                {
                    Debug.WriteLine($"[ART4229 VeiwModel] 通道为空{channel}或板卡未连接{IsConnected}");
                    ReMessageBox.Show(
                       $"通道打开失败，通道为空或板卡未连接",
                       "打开失败",
                       System.Windows.MessageBoxButton.OK,
                       System.Windows.MessageBoxImage.Error);
                }
            });

            CloseChannelCommand = new DelegateCommand<Art4229ChannelStatus>(async channel =>
            {
                if (channel != null && IsConnected)
                {
                    await CloseChannelAsync(channel);
                }
            });

            SwitchChannelDirectionCommand = new DelegateCommand(async () =>
            {
                if (SelectedChannel != null && IsConnected)
                {
                    await SwitchChannelDirectionAsync(SelectedChannel);
                }
            });

            ConfigureChannelCommand = new DelegateCommand(async () =>
            {
                if (SelectedChannel != null && IsConnected)
                {
                    await ConfigureChannelAsync(SelectedChannel);
                }
                else
                {
                    ReMessageBox.Show(
                        "请先选择通道并连接设备",
                        "配置失败",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Warning);
                }
            });

            // 添加数据命令
            AddTxDataCommand = new DelegateCommand(() =>
            {
                if (string.IsNullOrEmpty(Data429Hex) || SelectedChannel == null) return;
                var config = SelectedChannel.CurrentConfig;
                var channelTxList = SelectedChannel.TxDataList;
                var item = new TxDataItem
                {
                    Index = channelTxList.Count + 1,
                    Data429Hex = Data429Hex,
                    Label = OctalLabelOptions[LabelOctalIndex],
                    SendPeriod = config?.SendPeriod ?? 200,
                    SendCount = config?.SendCount ?? 1,
                    WordInterval = config?.WordInterval ?? 4,
                    Parity = config?.Parity ?? 0
                };
                channelTxList.Add(item);
                
                // 如果勾选了"数据递增"，则八进制标签自动进一位
                if (IsDataIncrement && LabelOctalIndex < OctalLabelOptions.Length - 1)
                {
                    LabelOctalIndex++;
                    UpdateData429Hex();
                }
            });

            // 修改数据命令
            ModifyTxDataCommand = new DelegateCommand(() =>
            {
                if (SelectedTxDataItem == null || string.IsNullOrEmpty(Data429Hex) || SelectedChannel == null) return;
                var config = SelectedChannel.CurrentConfig;
                SelectedTxDataItem.Data429Hex = Data429Hex;
                SelectedTxDataItem.Label = OctalLabelOptions[LabelOctalIndex];
                SelectedTxDataItem.SendPeriod = config?.SendPeriod ?? 200;
                SelectedTxDataItem.SendCount = config?.SendCount ?? 1;
                SelectedTxDataItem.WordInterval = config?.WordInterval ?? 4;
                SelectedTxDataItem.Parity = config?.Parity ?? 0;
            });

            // 删除选定数据命令
            DeleteTxDataCommand = new DelegateCommand(() =>
            {
                if (SelectedTxDataItem == null || SelectedChannel == null) return;
                var channelTxList = SelectedChannel.TxDataList;
                channelTxList.Remove(SelectedTxDataItem);
                // 重新编号
                for (int i = 0; i < channelTxList.Count; i++)
                    channelTxList[i].Index = i + 1;
            });

            // 开始发送命令
            StartSendCommand = new DelegateCommand(async () =>
            {
                if (SelectedChannel == null || !IsConnected || _driver == null || !SelectedChannel.IsTx)
                {
                    ReMessageBox.Show("请先选择发送通道并连接设备", "发送失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (SelectedChannel.TxDataList.Count == 0)
                {
                    ReMessageBox.Show("发送数据列表为空，请先添加数据", "发送失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                await StartSendAsync();
            });

            // 停止发送命令
            StopSendCommand = new DelegateCommand(async () =>
            {
                if (SelectedChannel != null && _driver != null)
                {
                    await StopSendAsync();
                }
            });

            // 开始接收命令
            StartReceiveCommand = new DelegateCommand(async () =>
            {
                if (SelectedChannel == null || !IsConnected || _driver == null || SelectedChannel.IsTx)
                {
                    ReMessageBox.Show("请先选择接收通道并连接设备", "接收失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                await StartReceiveAsync();
            });

            // 停止接收命令
            StopReceiveCommand = new DelegateCommand(async () =>
            {
                if (SelectedChannel != null && _driver != null)
                {
                    await StopReceiveAsync();
                }
            });

            // 清空接收数据命令
            ClearRxDataCommand = new DelegateCommand(() =>
            {
                if (SelectedChannel == null) return;

                if (SelectedChannel.IsTx)
                {
                    SelectedChannel.TxDataList.Clear();
                    SelectedTxDataItem = null;
                }
                else
                {
                    SelectedChannel.RxDataList.Clear();
                    SelectedRxDataItem = null;
                }
            });

            // 打开所有通道命令
            OpenAllChannelsCommand = new DelegateCommand(async () =>
            {
                if (!IsConnected || _driver == null)
                {
                    ReMessageBox.Show("请先连接设备", "操作失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                await OpenAllChannelsAsync();
            });

            // 关闭所有通道命令
            CloseAllChannelsCommand = new DelegateCommand(async () =>
            {
                if (!IsConnected || _driver == null)
                {
                    ReMessageBox.Show("请先连接设备", "操作失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                await CloseAllChannelsAsync();
            });
        }

        /// <summary>
        /// 尝试恢复缓存的驱动
        /// </summary>
        private void TryRestoreCachedDriver()
        {
            if (Device == null) return;

            var cachedDriver = DriverFactory.GetCachedDriver(Device.Id) as ART4229Driver;
            if (cachedDriver != null && cachedDriver.IsConnected)
            {
                _driver = cachedDriver;
                IsConnected = true;
                UpdateDeviceInfoText();
                Debug.WriteLine($"[ART4229] 已恢复缓存的驱动实例");
            }
        }

        /// <summary>
        /// 更新设备信息文本
        /// </summary>
        private void UpdateDeviceInfoText()
        {
            if (_driver != null && _driver.IsConnected)
            {
                var info = _driver.DeviceInfo;
                DeviceInfoText = $"通道数: {info.nChannelCount} | 主时钟: {info.fMainClock / 1000000:F1}MHz | 速率范围: {info.fMinRate:F0}-{info.fMaxRate:F0} bps";
            }
            else
            {
                DeviceInfoText = "未连接设备";
            }
        }

        /// <summary>
        /// 更新选中通道信息
        /// </summary>
        private void UpdateSelectedChannelInfo()
        {
            if (SelectedChannel != null)
            {
                string direction = SelectedChannel.IsTx ? "发送(TX)" : "接收(RX)";
                string status = SelectedChannel.IsActive ? "已打开" : "未打开";
                SelectedChannelInfo = $"通道: {SelectedChannel.ChannelName}\n方向: {direction}\n状态: {status}";
                
                ChannelConfigInfo = $"通道编号: {SelectedChannel.ChannelIndex}\n" +
                                   $"方向: {direction}\n" +
                                   $"速率: 100Kbps / 12.5Kbps\n" +
                                   $"校验: 奇校验";
            }
            else
            {
                SelectedChannelInfo = "未选择通道";
                ChannelConfigInfo = "请选择通道";
            }
        }

        /// <summary>
        /// 更新所有通道状态
        /// </summary>
        private void UpdateAllChannelsState()
        {
            foreach (var channel in Channels)
            {
                channel.IsActive = false;
            }
        }

        #endregion

        #region 设备连接方法

        /// <summary>
        /// 打开设备
        /// </summary>
        private async Task OnOpenDeviceAsync()
        {
            try
            {
                ConnectionStatus = "检测中...";

                // 创建驱动实例
                _driver = DriverFactory.CreateDriver(Device) as ART4229Driver;
                if (_driver == null)
                {
                    throw new InvalidOperationException("无法创建 ART4229 驱动实例");
                }

                // 连接设备（检测板卡）
                bool connected = await _driver.ConnectAsync();

                if (connected)
                {
                    IsConnected = true;
                    UpdateDeviceInfoText();

                    Debug.WriteLine($"[ART4229 ViewModel] 板卡检测成功: {Device?.Name}");
                }
                else
                {
                    IsConnected = false;
                    ConnectionStatus = "离线";
                    _driver = null;

                    ReMessageBox.Show(
                        $"板卡连接失败，请检查板卡位置及驱动",
                        "连接失败",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                IsConnected = false;
                ConnectionStatus = "离线";
                _driver = null;

                Debug.WriteLine($"[ART4229] 板卡检测异常: {ex.Message}");

                ReMessageBox.Show(
                    $"板卡连接失败：{ex.Message}\n\n建议检查：\n- 设备是否已正确连接\n- ART4229 驱动/DLL 是否已安装\n- 确认设备已上电",
                    "连接失败",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 关闭设备
        /// </summary>
        private async Task OnCloseDeviceAsync()
        {
            try
            {
                Debug.WriteLine($"[ART4229] 正在关闭设备: {Device?.Name}");

                // 先关闭所有通道
                foreach (var channel in Channels.ToList())
                {
                    if (channel.IsActive)
                    {
                        await CloseChannelAsync(channel);
                    }
                }

                if (_driver != null)
                {
                    await _driver.DisconnectAsync();
                    _driver = null;
                }

                IsConnected = false;
                UpdateDeviceInfoText();

                Debug.WriteLine($"[ART4229] 设备已关闭");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ART4229] 关闭设备异常: {ex.Message}");
            }
        }

        #endregion

        #region 通道操作方法

        /// <summary>
        /// 打开通道
        /// </summary>
        private async Task OpenChannelAsync(Art4229ChannelStatus channel)
        {
            try
            {
                if (_driver == null || !_driver.IsConnected)
                {
                    Debug.WriteLine($"[ART4229] 设备未连接，无法打开通道 {channel.ChannelName}");
                    return;
                }

                bool result;
                if (channel.IsTx)
                {
                    result = await _driver.OpenTxChannelAsync(channel.ChannelIndex);
                }
                else
                {
                    result = await _driver.OpenRxChannelAsync(channel.ChannelIndex);
                }

                if (result)
                {
                    channel.IsActive = true;
                    Debug.WriteLine($"[ART4229] 通道 {channel.ChannelName} 打开成功");
                    
                    // 打开通道后自动初始化配置，避免之前的设置残留
                    await InitializeChannelConfigAsync(channel);
                    
                    UpdateSelectedChannelInfo();
                }
                else
                {
                    Debug.WriteLine($"[ART4229] 通道 {channel.ChannelName} 打开失败");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ART4229] 打开通道 {channel.ChannelName} 异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 初始化通道配置（打开通道时调用，避免之前的设置残留）
        /// </summary>
        private async Task InitializeChannelConfigAsync(Art4229ChannelStatus channel)
        {
            try
            {
                var config = channel.CurrentConfig;
                bool configResult;

                if (channel.IsTx)
                {
                    Debug.WriteLine($"[ART4229] 初始化发送通道 {channel.ChannelIndex} 配置");
                    configResult = await _driver.ConfigureTxChannelAsync(
                        channel.ChannelIndex,
                        config.Rate,
                        config.SendMode,
                        config.Parity,
                        config.WordFormat);
                }
                else
                {
                    Debug.WriteLine($"[ART4229] 初始化接收通道 {channel.ChannelIndex} 配置");
                    configResult = await _driver.ConfigureRxChannelAsync(
                        channel.ChannelIndex,
                        config.Rate,
                        config.Parity,
                        config.WordFormat,
                        config.EnableInterrupt,
                        config.InterruptDepth,
                        config.EnableTimeTag);
                }

                if (configResult)
                {
                    Debug.WriteLine($"[ART4229] 通道 {channel.ChannelIndex} 初始化配置成功");
                }
                else
                {
                    Debug.WriteLine($"[ART4229] 通道 {channel.ChannelIndex} 初始化配置失败");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ART4229] 初始化通道配置异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 关闭通道
        /// </summary>
        private async Task CloseChannelAsync(Art4229ChannelStatus channel)
        {
            try
            {
                if (_driver == null || !_driver.IsConnected)
                {
                    Debug.WriteLine($"[ART4229] 设备未连接，无法关闭通道 {channel.ChannelName}");
                    return;
                }

                bool result;
                if (channel.IsTx)
                {
                    result = await _driver.CloseTxChannelAsync(channel.ChannelIndex);
                }
                else
                {
                    result = await _driver.CloseRxChannelAsync(channel.ChannelIndex);
                }

                if (result)
                {
                    channel.IsActive = false;
                    Debug.WriteLine($"[ART4229] 通道 {channel.ChannelName} 已关闭");
                    UpdateSelectedChannelInfo();
                }
                else
                {
                    Debug.WriteLine($"[ART4229] 通道 {channel.ChannelName} 关闭失败");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ART4229] 关闭通道 {channel.ChannelName} 异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 打开所有通道
        /// </summary>
        private async Task OpenAllChannelsAsync()
        {
            try
            {
                Debug.WriteLine($"[ART4229] 开始打开所有通道");
                int successCount = 0;
                
                foreach (var channel in Channels)
                {
                    if (!channel.IsActive)
                    {
                        await OpenChannelAsync(channel);
                        if (channel.IsActive)
                            successCount++;
                    }
                    else
                    {
                        successCount++;
                    }
                }
                
                Debug.WriteLine($"[ART4229] 打开所有通道完成，成功: {successCount}/{Channels.Count}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ART4229] 打开所有通道异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 关闭所有通道
        /// </summary>
        private async Task CloseAllChannelsAsync()
        {
            try
            {
                Debug.WriteLine($"[ART4229] 开始关闭所有通道");
                
                // 先停止所有正在运行的发送/接收
                foreach (var channel in Channels)
                {
                    if (channel.CurrentConfig?.IsRunning == true)
                    {
                        if (channel.IsTx)
                            await _driver.StopSendAsync(channel.ChannelIndex);
                        else
                            await _driver.StopReceiveAsync(channel.ChannelIndex);
                        channel.CurrentConfig.IsRunning = false;
                    }
                }
                
                // 取消接收轮询
                _receiveCts?.Cancel();
                _receiveCts = null;
                IsSending = false;
                IsReceiving = false;
                
                int successCount = 0;
                foreach (var channel in Channels)
                {
                    if (channel.IsActive)
                    {
                        await CloseChannelAsync(channel);
                        if (!channel.IsActive)
                            successCount++;
                    }
                    else
                    {
                        successCount++;
                    }
                }
                
                Debug.WriteLine($"[ART4229] 关闭所有通道完成，成功: {successCount}/{Channels.Count}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ART4229] 关闭所有通道异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 切换通道方向（TX 和 RX 互换）
        /// </summary>
        private async Task SwitchChannelDirectionAsync(Art4229ChannelStatus channel)
        {
            try
            {
                if (_driver == null || !_driver.IsConnected)
                {
                    Debug.WriteLine($"[ART4229] 设备未连接，无法切换通道方向");
                    return;
                }

                Debug.WriteLine($"[ART4229] 开始切换通道 {channel.ChannelIndex} 方向: {(channel.IsTx ? "TX->RX" : "RX->TX")}");

                bool result = await _driver.SwitchChannelDirectionAsync(channel.ChannelIndex, channel.IsTx);

                if (result)
                {
                    // 切换方向（通道名称保持"通道X"格式不变）
                    channel.IsTx = !channel.IsTx;
                    channel.IsActive = true;

                    // 方向切换后必须重新初始化通道（对齐厂家例程：Open + SetWordFormat + Init + ResetFIFO）
                    await InitializeChannelConfigAsync(channel);

                    Debug.WriteLine($"[ART4229] 通道 {channel.ChannelIndex} 方向切换成功，现为 {channel.DirectionText}");
                    UpdateSelectedChannelInfo();
                }
                else
                {
                    Debug.WriteLine($"[ART4229] 通道 {channel.ChannelIndex} 方向切换失败");
                    ReMessageBox.Show(
                        $"通道收发方向切换失败",
                        "切换失败",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ART4229] 切换通道方向异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 配置通道参数
        /// </summary>
        private async Task ConfigureChannelAsync(Art4229ChannelStatus channel)
        {
            try
            {
                if (_driver == null || !_driver.IsConnected)
                {
                    Debug.WriteLine($"[ART4229] 设备未连接，无法配置通道");
                    ReMessageBox.Show("设备未连接", "配置失败", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                    return;
                }

                if (!channel.IsActive)
                {
                    Debug.WriteLine($"[ART4229] 通道 {channel.ChannelIndex} 未打开，无法配置");
                    ReMessageBox.Show("请先打开通道", "配置失败", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    return;
                }

                var config = channel.CurrentConfig;
                bool result;

                if (channel.IsTx)
                {
                    // 配置发送通道
                    Debug.WriteLine($"[ART4229] 配置发送通道 {channel.ChannelIndex}: 码率={config.Rate}, 模式={config.SendMode}");
                    result = await _driver.ConfigureTxChannelAsync(
                        channel.ChannelIndex,
                        config.Rate,
                        config.SendMode,
                        config.Parity,
                        config.WordFormat);
                }
                else
                {
                    // 配置接收通道
                    Debug.WriteLine($"[ART4229] 配置接收通道 {channel.ChannelIndex}: 码率={config.Rate}, 中断={config.EnableInterrupt}");
                    result = await _driver.ConfigureRxChannelAsync(
                        channel.ChannelIndex,
                        config.Rate,
                        config.Parity,
                        config.WordFormat,
                        config.EnableInterrupt,
                        config.InterruptDepth,
                        config.EnableTimeTag);
                }

                if (result)
                {
                    Debug.WriteLine($"[ART4229] 通道 {channel.ChannelIndex} 配置成功");
                    ReMessageBox.Show(
                        $"通道 {channel.ChannelName} 配置成功",
                        "配置成功",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Information);
                }
                else
                {
                    Debug.WriteLine($"[ART4229] 通道 {channel.ChannelIndex} 配置失败");
                    ReMessageBox.Show(
                        $"通道 {channel.ChannelName} 配置失败",
                        "配置失败",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ART4229] 配置通道异常: {ex.Message}");
                ReMessageBox.Show($"配置异常: {ex.Message}", "配置失败", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 更新429数据（八进制标签转十六进制后拼接）
        /// </summary>
        private void UpdateData429Hex()
        {
            try
            {
                if (string.IsNullOrEmpty(LabelSsmDataSdi) || LabelOctalIndex < 0 || LabelOctalIndex >= OctalLabelOptions.Length)
                {
                    Data429Hex = string.Empty;
                    return;
                }

                // 获取八进制标签字符串
                string octalLabel = OctalLabelOptions[LabelOctalIndex];
                
                // 八进制转十进制再转十六进制
                int decimalValue = Convert.ToInt32(octalLabel, 8);
                string hexLabel = decimalValue.ToString("X2");
                
                // 拼接：SSM/Data/SDI + 十六进制标签
                Data429Hex = LabelSsmDataSdi.ToUpper() + hexLabel;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ART4229] 更新429数据异常: {ex.Message}");
                Data429Hex = string.Empty;
            }
        }

        /// <summary>
        /// 开始发送数据
        /// </summary>
        private async Task StartSendAsync()
        {
            try
            {
                if (_driver == null || SelectedChannel == null) return;

                if (!SelectedChannel.IsTx)
                {
                    ReMessageBox.Show("当前通道不是发送方向(TX)，请先切换为发送通道", "发送失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (!SelectedChannel.IsActive)
                {
                    ReMessageBox.Show("请先打开通道", "发送失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var config = SelectedChannel.CurrentConfig;
                int channelIndex = SelectedChannel.ChannelIndex;

                // 发送前再初始化一次TX通道（对齐厂家例程：TX_Open + SetWordFormat + TX_Init + ResetFIFO 后再写入）
                bool configOk = await _driver.ConfigureTxChannelAsync(
                    channelIndex,
                    config.Rate,
                    config.SendMode,
                    config.Parity,
                    config.WordFormat);

                if (!configOk)
                {
                    ReMessageBox.Show("发送通道配置失败，请检查通道是否已正确打开/方向是否正确", "发送失败", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // 准备发送数据数组（使用通道独立的TxDataList）
                var channelTxList = SelectedChannel.TxDataList;
                int count = channelTxList.Count;
                uint[] data429 = new uint[count];
                uint[] parity = new uint[count];
                uint[] period = new uint[count];
                uint[] sendCount = new uint[count];
                uint[] interval = new uint[count];

                for (int i = 0; i < count; i++)
                {
                    var item = channelTxList[i];
                    // 将十六进制字符串转换为uint
                    uint raw = Convert.ToUInt32(item.Data429Hex, 16);
                    // 统一按通道“校验”配置生成校验位，并把校验模式传给驱动
                    data429[i] = ApplyParity(raw, config.Parity);
                    parity[i] = (uint)config.Parity;
                    period[i] = (uint)item.SendPeriod;
                    sendCount[i] = (uint)item.SendCount;
                    interval[i] = (uint)item.WordInterval;
                }

                bool result;
                if (config.SendMode == 0)
                {
                    // Single模式
                    result = await _driver.SendDataSingleAsync(channelIndex, data429, parity);
                }
                else
                {
                    // Period模式
                    result = await _driver.SendDataPeriodAsync(channelIndex, data429, period, sendCount, interval, parity);
                }

                if (result)
                {
                    IsSending = true;
                    config.IsRunning = true;
                    Debug.WriteLine($"[ART4229] 通道 {channelIndex} 开始发送，模式={config.SendMode}");

                    // Single模式：发送完成后自动回到“未发送”
                    if (config.SendMode == 0)
                    {
                        bool completed = false;
                        try
                        {
                            completed = await _driver.WaitForTxCompleteAsync(channelIndex, timeoutMs: 2000);
                        }
                        catch { }

                        await Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            // 只在仍然是同一个通道同一个模式时回落状态，避免用户切换面板造成错置
                            if (SelectedChannel != null && SelectedChannel.ChannelIndex == channelIndex && SelectedChannel.CurrentConfig == config)
                            {
                                IsSending = false;
                                config.IsRunning = false;
                            }
                        }, System.Windows.Threading.DispatcherPriority.Background);

                        Debug.WriteLine($"[ART4229] 通道 {channelIndex} Single发送完成状态: {completed}");
                    }
                }
                else
                {
                    ReMessageBox.Show("发送启动失败", "发送失败", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ART4229] 发送异常: {ex.Message}");
                ReMessageBox.Show($"发送异常: {ex.Message}", "发送失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 停止发送数据
        /// </summary>
        private async Task StopSendAsync()
        {
            try
            {
                if (_driver == null || SelectedChannel == null) return;

                await _driver.StopSendAsync(SelectedChannel.ChannelIndex);
                IsSending = false;
                if (SelectedChannel.CurrentConfig != null)
                    SelectedChannel.CurrentConfig.IsRunning = false;
                Debug.WriteLine($"[ART4229] 通道 {SelectedChannel.ChannelIndex} 停止发送");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ART4229] 停止发送异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 开始接收数据
        /// </summary>
        private async Task StartReceiveAsync()
        {
            try
            {
                if (_driver == null || SelectedChannel == null) return;

                int channelIndex = SelectedChannel.ChannelIndex;
                var config = SelectedChannel.CurrentConfig;

                if (SelectedChannel.IsTx)
                {
                    ReMessageBox.Show("当前通道不是接收方向(RX)，请先切换为接收通道", "接收失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (!SelectedChannel.IsActive)
                {
                    ReMessageBox.Show("请先打开通道", "接收失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // 接收前再初始化一次RX通道（对齐厂家例程：RX_Open + SetWordFormat + RX_Init + ResetFIFO 后再Start）
                bool configOk = await _driver.ConfigureRxChannelAsync(
                    channelIndex,
                    config.Rate,
                    config.Parity,
                    config.WordFormat,
                    config.EnableInterrupt,
                    config.InterruptDepth,
                    config.EnableTimeTag);

                if (!configOk)
                {
                    ReMessageBox.Show("接收通道配置失败，请检查通道是否已正确打开/方向是否正确", "接收失败", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // 启动接收
                bool result = await _driver.StartReceiveAsync(channelIndex);
                if (!result)
                {
                    ReMessageBox.Show("接收启动失败", "接收失败", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                IsReceiving = true;
                config.IsRunning = true;
                Debug.WriteLine($"[ART4229] 通道 {channelIndex} 开始接收");

                // 启动接收轮询任务
                _receiveCts = new System.Threading.CancellationTokenSource();
                var currentChannel = SelectedChannel;  // 捕获当前通道引用
                _ = Task.Run(async () => await ReceiveDataLoopAsync(currentChannel, config.EnableTimeTag, config.Rate == 0, config.Parity, config.EnableInterrupt, _receiveCts.Token));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ART4229] 启动接收异常: {ex.Message}");
                ReMessageBox.Show($"接收异常: {ex.Message}", "接收失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 停止接收数据
        /// </summary>
        private async Task StopReceiveAsync()
        {
            try
            {
                // 取消接收轮询
                _receiveCts?.Cancel();
                _receiveCts = null;

                if (_driver == null || SelectedChannel == null) return;

                await _driver.StopReceiveAsync(SelectedChannel.ChannelIndex);
                IsReceiving = false;
                if (SelectedChannel.CurrentConfig != null)
                    SelectedChannel.CurrentConfig.IsRunning = false;
                Debug.WriteLine($"[ART4229] 通道 {SelectedChannel.ChannelIndex} 停止接收");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ART4229] 停止接收异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 接收数据轮询循环
        /// </summary>
        private async Task ReceiveDataLoopAsync(Art4229ChannelStatus channel, bool enableTimeTag, bool enableRateAdaption, int parityMode, bool enableInterrupt, System.Threading.CancellationToken token)
        {
            try
            {
                double mainClock = _driver?.DeviceInfo.fMainClock ?? 0;
                int channelIndex = channel.ChannelIndex;
                var channelRxList = channel.RxDataList;
                
                while (!token.IsCancellationRequested)
                {
                    if (enableInterrupt)
                    {
                        bool hasInt = await _driver.WaitForRxInterruptAsync(channelIndex, timeout: 0.5);
                        if (!hasInt)
                        {
                            await Task.Delay(5, token);
                            continue;
                        }
                    }

                    var dataList = await _driver.ReadReceiveDataAsync(channelIndex, 256, enableTimeTag, enableRateAdaption);
                    
                    if (dataList.Count > 0)
                    {
                        // 在后台线程解析数据，减少UI线程负担
                        var parsedItems = new System.Collections.Generic.List<RxDataItem>(dataList.Count);
                        string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
                        
                        foreach (var (data429, rate, timeHigh, timeLow) in dataList)
                        {
                            // 解析429数据结构 (32位):
                            // Bit 0-7: Label (8位)
                            // Bit 8-9: SDI (2位)
                            // Bit 10-28: Data (19位)
                            // Bit 29-30: SSM (2位)
                            // Bit 31: Parity (1位)
                            
                            byte labelByte = (byte)(data429 & 0xFF);
                            string labelOctal = Convert.ToString(labelByte, 8).PadLeft(3, '0');
                            
                            int sdi = (int)((data429 >> 8) & 0x03);
                            string sdiStr = Convert.ToString(sdi, 2).PadLeft(2, '0');
                            
                            uint dataField = (data429 >> 10) & 0x7FFFF;  // 19位
                            string dataStr = Convert.ToString(dataField, 2).PadLeft(19, '0');
                            
                            int ssm = (int)((data429 >> 29) & 0x03);
                            string ssmStr = Convert.ToString(ssm, 2).PadLeft(2, '0');
                            
                            int parityBit = (int)((data429 >> 31) & 0x01);
                            string parityStr = parityBit.ToString();

                            string parityCheck = "-";
                            try
                            {
                                parityCheck = ValidateParity(data429, parityMode) ? "OK" : "ERR";
                            }
                            catch { }

                            parsedItems.Add(new RxDataItem
                            {
                                Data429Hex = data429.ToString("X8"),
                                Label = labelOctal,
                                SDI = sdiStr,
                                Data = dataStr,
                                SSM = ssmStr,
                                ParityBit = parityStr,
                                ParityCheck = parityCheck,
                                Rate = rate > 0 ? mainClock / rate : 0,
                                TimeStamp = timestamp
                            });
                        }
                        
                        // 批量更新UI，减少Dispatcher调用次数
                        await Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            foreach (var item in parsedItems)
                            {
                                item.Index = channelRxList.Count + 1;
                                channelRxList.Add(item);
                            }
                            
                            // 限制列表最大数量，避免内存溢出
                            while (channelRxList.Count > 10000)
                                channelRxList.RemoveAt(0);
                        }, System.Windows.Threading.DispatcherPriority.Background);
                    }

                    await Task.Delay(20, token);  // 轮询间隔
                }
            }
            catch (OperationCanceledException)
            {
                // 正常取消
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ART4229] 接收轮询异常: {ex.Message}");
            }
        }

        #endregion

        #region INotifyPropertyChanged 实现

        public event PropertyChangedEventHandler PropertyChanged;

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion

        #region IDisposable 实现

        private bool _disposed = false;

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
                // 取消接收轮询
                _receiveCts?.Cancel();
                _receiveCts?.Dispose();
                _receiveCts = null;
                
                // 断开连接
                Task.Run(async () => await OnCloseDeviceAsync()).Wait(TimeSpan.FromSeconds(2));
            }

            _disposed = true;
        }

        #endregion
    }
}
