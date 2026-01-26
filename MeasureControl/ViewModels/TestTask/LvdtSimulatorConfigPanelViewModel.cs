using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using MeasureControl.Helpers.OKAIPXIDevice;
using MeasureControl.Models;
using MeasureControl.Models.Devices;
using MeasureControl.Models.Channels;
using MeasureControl.Services;
using MeasureControl.Events;
using Prism.Commands;
using Prism.Mvvm;
using Prism.Events;
using MeasureControl.Views;
using MeasureControl.Views.Dialogs;

namespace MeasureControl.ViewModels.TestTask
{
    /// <summary>
    /// LVDT/RVDT模拟器配置面板ViewModel，实现4087输出功能
    /// 支持LVDT/RVDT和旋转变压器两种模式
    /// </summary>
    public class LvdtSimulatorConfigPanelViewModel : BindableBase, IDisposable
    {
        // LVDT/RVDT板卡ID分配器（线程安全）
        private static readonly object _lvdtIdLock = new object();
        private static readonly HashSet<int> _lvdtAllocatedIds = new HashSet<int>();
        private static readonly Dictionary<string, UIntPtr> _backgroundRunningDevices = new Dictionary<string, UIntPtr>();
        private static readonly Dictionary<string, int> _backgroundRunningDeviceIds = new Dictionary<string, int>();
        private static readonly Dictionary<string, bool> _backgroundRunningOutputStates = new Dictionary<string, bool>();
        private int _currentAllocatedId = 0; // 当前实例分配的ID

        /// <summary>
        /// 分配LVDT/RVDT板卡ID（寻找最小的未被占用的正整数）
        /// </summary>
        private int AllocateLvdtId()
        {
            lock (_lvdtIdLock)
            {
                int id = 1;
                while (_lvdtAllocatedIds.Contains(id))
                {
                    id++;
                }
                _lvdtAllocatedIds.Add(id);
                return id;
            }
        }

        private LvdtSimulatorCardConfig EnsureLvdtSimulatorCardConfig()
        {
            if (Device == null)
            {
                return null;
            }

            var cardConfig = Device.CardConfigData as LvdtSimulatorCardConfig;
            if (cardConfig == null)
            {
                cardConfig = new LvdtSimulatorCardConfig();
                Device.CardConfigData = cardConfig;
            }

            cardConfig.CardId = Device.Id;
            cardConfig.CardName = CardName;
            cardConfig.CardModel = CardModel;
            cardConfig.ChassisName = ChassisName;
            return cardConfig;
        }

        private bool SaveCardConfig(bool showMessages = true)
        {
            if (Device == null)
            {
                if (showMessages) ReMessageBox.Show("设备未初始化，无法保存配置", "提示");
                return false;
            }

            var cardConfig = EnsureLvdtSimulatorCardConfig();
            if (cardConfig == null)
            {
                if (showMessages) ReMessageBox.Show("保存配置失败：卡配置未初始化", "错误");
                return false;
            }

            cardConfig.Channels.Clear();
            foreach (var ch in ChannelConfigs)
            {
                if (ch == null) continue;
                cardConfig.Channels.Add(new LvdtSimulatorChannelConfig
                {
                    ChannelIndex = ch.ChannelIndex,
                    ChannelName = ch.ChannelName,
                    IsEnabled = ch.IsEnabled,
                    WorkMode = ch.WorkMode,
                    SensorType = ch.SensorType,
                    OutputMode = ch.OutputMode,
                    UseInternalExcitation = ch.UseInternalExcitation,
                    ExcitationVoltage = ch.ExcitationVoltage,
                    ExcitationFrequency = ch.ExcitationFrequency,
                    TransmissionRatio = ch.TransmissionRatio,
                    PhaseDelay = ch.PhaseDelay,
                    AdcRangeIndex = ch.AdcRangeIndex,
                    Position = ch.Position,
                    VaVoltage = ch.VaVoltage,
                    VbVoltage = ch.VbVoltage,
                    Vsum = ch.Vsum,
                    Vdiff = ch.Vdiff,
                    VaInverse = ch.VaInverse,
                    VbInverse = ch.VbInverse,
                    IsDynamicOutput = ch.IsDynamicOutput,
                    DynamicStartPosition = ch.DynamicStartPosition,
                    DynamicEndPosition = ch.DynamicEndPosition,
                    DynamicPointFreq = ch.DynamicPointFreq,
                    DynamicWaveformLength = ch.DynamicWaveformLength,
                    DynamicOutputCount = ch.DynamicOutputCount,
                    GoBackOutput = ch.GoBackOutput,
                    UseResolverAngleOutput = ch.UseResolverAngleOutput,
                    ResolverPhaseDiff = ch.ResolverPhaseDiff,
                    ResolverOutputAngle = ch.ResolverOutputAngle,
                    ResolverMotorSpeed = ch.ResolverMotorSpeed,
                    AutoLoadResolverWave = ch.AutoLoadResolverWave,
                    ResolverWaveformLength = ch.ResolverWaveformLength,
                    ResolverStartAngle = ch.ResolverStartAngle,
                    ResolverEndAngle = ch.ResolverEndAngle,
                    WaveformOutputCount = ch.WaveformOutputCount
                });
            }

            if (Device.CardName != CardName)
            {
                Device.CardName = CardName;
            }

            _pxiChassisService?.UpdateDeviceCardConfig(Device.Id, cardConfig);
            _eventAggregator?.GetEvent<ProjectModifiedEvent>()?.Publish(new ProjectModifiedEventArgs
            {
                ModificationType = "LvdtSimulatorConfig",
                Description = $"LVDT模拟配置已保存: {CardName}"
            });

            if (showMessages) ReMessageBox.Show("保存成功", "提示");
            return true;
        }

        private void ReloadCardConfig()
        {
            if (Device == null)
            {
                ReMessageBox.Show("设备未初始化，无法读取配置", "提示");
                return;
            }

            if (Device.CardConfigData is not LvdtSimulatorCardConfig cardConfig || cardConfig.Channels == null || cardConfig.Channels.Count == 0)
            {
                ReMessageBox.Show("没有找到已保存的配置", "提示");
                return;
            }

            var confirm = ReMessageBox.Show("读取配置会覆盖当前通道参数，是否继续？", "提示",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Question);
            if (confirm != System.Windows.MessageBoxResult.Yes)
            {
                return;
            }

            var savedByIndex = cardConfig.Channels.ToDictionary(c => c.ChannelIndex);
            foreach (var ch in ChannelConfigs)
            {
                if (ch == null) continue;
                if (!savedByIndex.TryGetValue(ch.ChannelIndex, out var saved) || saved == null) continue;

                ch.ChannelName = saved.ChannelName ?? ch.ChannelName;
                ch.IsEnabled = saved.IsEnabled;
                ch.WorkMode = saved.WorkMode ?? ch.WorkMode;
                ch.SensorType = saved.SensorType ?? ch.SensorType;
                ch.OutputMode = saved.OutputMode ?? ch.OutputMode;

                ch.UseInternalExcitation = saved.UseInternalExcitation;
                ch.ExcitationVoltage = saved.ExcitationVoltage;
                ch.ExcitationFrequency = saved.ExcitationFrequency;
                ch.TransmissionRatio = saved.TransmissionRatio;
                ch.PhaseDelay = saved.PhaseDelay;
                ch.AdcRangeIndex = saved.AdcRangeIndex;

                ch.Position = saved.Position;
                ch.VaVoltage = saved.VaVoltage;
                ch.VbVoltage = saved.VbVoltage;
                ch.Vsum = saved.Vsum;
                ch.Vdiff = saved.Vdiff;
                ch.VaInverse = saved.VaInverse;
                ch.VbInverse = saved.VbInverse;

                ch.IsDynamicOutput = saved.IsDynamicOutput;
                ch.DynamicStartPosition = saved.DynamicStartPosition;
                ch.DynamicEndPosition = saved.DynamicEndPosition;
                ch.DynamicPointFreq = saved.DynamicPointFreq;
                ch.DynamicWaveformLength = saved.DynamicWaveformLength;
                ch.DynamicOutputCount = saved.DynamicOutputCount;
                ch.GoBackOutput = saved.GoBackOutput;

                ch.UseResolverAngleOutput = saved.UseResolverAngleOutput;
                ch.ResolverPhaseDiff = saved.ResolverPhaseDiff;
                ch.ResolverOutputAngle = saved.ResolverOutputAngle;
                ch.ResolverMotorSpeed = saved.ResolverMotorSpeed;
                ch.AutoLoadResolverWave = saved.AutoLoadResolverWave;
                ch.ResolverWaveformLength = saved.ResolverWaveformLength;
                ch.ResolverStartAngle = saved.ResolverStartAngle;
                ch.ResolverEndAngle = saved.ResolverEndAngle;
                ch.WaveformOutputCount = saved.WaveformOutputCount;
            }

            ReMessageBox.Show("读取成功", "提示");
        }

        /// <summary>
        /// 释放LVDT/RVDT板卡ID
        /// </summary>
        private void ReleaseLvdtId(int id)
        {
            lock (_lvdtIdLock)
            {
                _lvdtAllocatedIds.Remove(id);
            }
        }

        private readonly IPxiChassisService _pxiChassisService;
        private readonly IEventAggregator _eventAggregator;
        private readonly ProjectService _projectService;

        /// <summary>
        /// 是否保持后台运行（切换其他板卡时不关闭设备）
        /// </summary>
        public bool KeepRunningInBackground { get; set; } = true; // 4087A默认保持后台运行

        private DeviceBase _device;
        private string _chassisName;
        private string _cardModel;
        private string _cardName;
        // 是否支持旋变（Resolver）功能
        private bool _isResolverCapable = false;
        private bool _isConnected;
        private bool _isOutputRunning;
        private string _connectionStatus;
        private UIntPtr _deviceHandle = UIntPtr.Zero;
        // 通道配置
        private ObservableCollection<LvdtChannelConfigViewModel> _channelConfigs;
        private LvdtChannelConfigViewModel _selectedChannel;
        private ObservableCollection<LvdtChannelConfigViewModel> _topChannelButtons;

        // 全局激励信号配置
        private double _excitationVoltage = 7.0;  // 默认7Vrms
        private double _excitationFrequency = 3300.0;  // 默认3300Hz
        private bool _useInternalExcitation = true;  // 使用内部激励

        // 测量数据
        private double _measuredExcitationVoltage;  // 测量的激励电压
        private double _measuredExcitationFrequency;  // 测量的激励频率
        private bool _isMeasuringEnabled = false;  // 是否启用测量

        // 高级配置
        private double _transmissionRatio = 1.0;  // 传输比 (0.1-10.0)
        private double _phaseDelay = 0;  // 相位延迟 (0-65535, 单位: 100ns)
        private int _adcRangeIndex = 3;  // ADC范围索引 (0-3)

        // 波形输出参数
        private double _scanFrequency = 1000.0;  // 扫描频率 (Hz)
        private double _scanPeriod = 0.001;  // 扫描周期 (秒)
        private int _waveformLength = 100;  // 波形长度 (1-2048)
        private int _waveformOutputCount = 0;  // 波形输出次数 (0=连续)

        // 校准参数
        private double _calibrationScaleA = 1.0;  // 校准参数A
        private double _calibrationScaleB = 0.0;  // 校准参数B
        private double _calibrationScaleC = 0.0;  // 校准参数C
        private int _calibrationGroupIndex = 0;  // 校准组索引 (0-7)

        public DeviceBase Device
        {
            get => _device;
            set => SetProperty(ref _device, value);
        }

        public string ChassisName
        {
            get => _chassisName;
            set => SetProperty(ref _chassisName, value);
        }

        public string CardModel
        {
            get => _cardModel;
            set => SetProperty(ref _cardModel, value);
        }

        public string CardName
        {
            get => _cardName;
            set => SetProperty(ref _cardName, value);
        }

        public bool IsResolverCapable
        {
            get => _isResolverCapable;
            private set => SetProperty(ref _isResolverCapable, value);
        }

        public bool IsConnected
        {
            get => _isConnected;
            private set
            {
                if (SetProperty(ref _isConnected, value))
                {
                    RaisePropertyChanged(nameof(CanStartOutput));
                    RaisePropertyChanged(nameof(CanStopOutput));
                }
            }
        }

        public bool IsOutputRunning
        {
            get => _isOutputRunning;
            private set
            {
                if (SetProperty(ref _isOutputRunning, value))
                {
                    RaisePropertyChanged(nameof(CanStartOutput));
                    RaisePropertyChanged(nameof(CanStopOutput));
                }
            }
        }

        public string ConnectionStatus
        {
            get => _connectionStatus;
            set => SetProperty(ref _connectionStatus, value);
        }

        public ObservableCollection<LvdtChannelConfigViewModel> ChannelConfigs
        {
            get => _channelConfigs;
            set => SetProperty(ref _channelConfigs, value);
        }

        public LvdtChannelConfigViewModel SelectedChannel
        {
            get => _selectedChannel;
            set
            {
                if (SetProperty(ref _selectedChannel, value))
                {
                    // 当选中通道改变时，确保显示单通道详情视图并默认到仿真输出视图
                    if (_selectedChannel != null)
                    {
                        ActiveChannelViewMode = "Simulate";
                        IsChannelListVisible = false;
                        ShowChannelDetail = true;
                    }
                }
            }
        }

        public ObservableCollection<LvdtChannelConfigViewModel> TopChannelButtons
        {
            get => _topChannelButtons;
            set => SetProperty(ref _topChannelButtons, value);
        }

        // UI 控制：顶部通道列表显示与当前通道视图模式
        private bool _isChannelListVisible = true;
        public bool IsChannelListVisible
        {
            get => _isChannelListVisible;
            set
            {
                if (SetProperty(ref _isChannelListVisible, value))
                {
                    // 当显示通道列表时，关闭单通道详情视图，避免内容残留
                    if (_isChannelListVisible)
                    {
                        ShowChannelDetail = false;
                    }
                }
            }
        }

        private string _activeChannelViewMode = "Simulate"; // "Simulate" or "Test"
        public string ActiveChannelViewMode
        {
            get => _activeChannelViewMode;
            set => SetProperty(ref _activeChannelViewMode, value);
        }

        private bool _showChannelDetail = false;
        public bool ShowChannelDetail
        {
            get => _showChannelDetail;
            set
            {
                if (SetProperty(ref _showChannelDetail, value))
                {
                    // 当显示单通道详情时，隐藏通道列表
                    if (_showChannelDetail)
                    {
                        IsChannelListVisible = false;
                    }
                }
            }
        }

        // 顶部按钮命令：选择通道 / 打开仿真输出 / 打开测试输入
        public ICommand SelectChannelCommand { get; }
        public ICommand OpenSimulateOutputCommand { get; }
        public ICommand OpenTestInputCommand { get; }
        public ICommand ExpandChannelCommand { get; }

        // 激励信号配置
        public double ExcitationVoltage
        {
            get => _excitationVoltage;
            set => SetProperty(ref _excitationVoltage, value);
        }

        public double ExcitationFrequency
        {
            get => _excitationFrequency;
            set => SetProperty(ref _excitationFrequency, value);
        }

        public bool UseInternalExcitation
        {
            get => _useInternalExcitation;
            set => SetProperty(ref _useInternalExcitation, value);
        }

        // 测量数据属性
        public double MeasuredExcitationVoltage
        {
            get => _measuredExcitationVoltage;
            set => SetProperty(ref _measuredExcitationVoltage, value);
        }

        public double MeasuredExcitationFrequency
        {
            get => _measuredExcitationFrequency;
            set => SetProperty(ref _measuredExcitationFrequency, value);
        }

        public bool IsMeasuringEnabled
        {
            get => _isMeasuringEnabled;
            set => SetProperty(ref _isMeasuringEnabled, value);
        }

        // 高级配置属性
        public double TransmissionRatio
        {
            get => _transmissionRatio;
            set => SetProperty(ref _transmissionRatio, Math.Max(0.1, Math.Min(10.0, value)));
        }

        public double PhaseDelay
        {
            get => _phaseDelay;
            set => SetProperty(ref _phaseDelay, Math.Max(0, Math.Min(65535, value)));
        }

        public int AdcRangeIndex
        {
            get => _adcRangeIndex;
            set => SetProperty(ref _adcRangeIndex, Math.Max(0, Math.Min(3, value)));
        }

        // 校准参数属性
        public double CalibrationScaleA
        {
            get => _calibrationScaleA;
            set => SetProperty(ref _calibrationScaleA, value);
        }

        public double CalibrationScaleB
        {
            get => _calibrationScaleB;
            set => SetProperty(ref _calibrationScaleB, value);
        }

        public double CalibrationScaleC
        {
            get => _calibrationScaleC;
            set => SetProperty(ref _calibrationScaleC, value);
        }

        public int CalibrationGroupIndex
        {
            get => _calibrationGroupIndex;
            set => SetProperty(ref _calibrationGroupIndex, Math.Max(0, Math.Min(7, value)));
        }
        
        

        public bool CanStartOutput => IsConnected && !IsOutputRunning && HasValidConfig();
        public bool CanStopOutput => IsConnected && IsOutputRunning;

        public ICommand OpenDeviceCommand { get; }
        public ICommand CloseDeviceCommand { get; }
        public ICommand StartOutputCommand { get; }
        public ICommand StopOutputCommand { get; }
        public ICommand StartChannelOutputCommand { get; }
        public ICommand StopChannelOutputCommand { get; }
        public ICommand ApplyChannelConfigCommand { get; }
        public ICommand ReadExternalExcitationCommand { get; }
        // Waveform display commands
        public ICommand StartWaveformReadCommand { get; }
        public ICommand StopWaveformReadCommand { get; }
        public ICommand SetChannelInternalExcCommand { get; }
        public ICommand StartMeasurementCommand { get; }
        public ICommand StopMeasurementCommand { get; }
        public ICommand StartChannelMeasurementCommand { get; }
        public ICommand StopChannelMeasurementCommand { get; }
        public ICommand MeasureExcitationSignalCommand { get; }
        public ICommand ResetDeviceCommand { get; }
        public ICommand SaveConfigCommand { get; }
        public ICommand ReloadConfigCommand { get; }
        public ICommand SaveCalibrationCommand { get; }
        public ICommand LoadCalibrationCommand { get; }
        public ICommand ToggleDeviceCommand { get; }
        // Dual channel output helpers
        // Device name for UI display
        public string DeviceName => CardName;

        public System.Threading.Tasks.Task StartDualChannelOutputTask { get; private set; }
        public ICommand StartDualOutputCommand { get; }
        public ICommand StopDualOutputCommand { get; }

        public LvdtSimulatorConfigPanelViewModel(DeviceBase device, string chassisName,
            IPxiChassisService pxiChassisService = null,
            IEventAggregator eventAggregator = null,
            ProjectService projectService = null)
        {
            Device = device;
            ChassisName = chassisName;
            CardModel = device?.Model ?? string.Empty;
            CardName = !string.IsNullOrEmpty(device?.CardName) ? device.CardName : device?.Model ?? string.Empty;
            _pxiChassisService = pxiChassisService;
            _eventAggregator = eventAggregator;
            _projectService = projectService;

            ChannelConfigs = new ObservableCollection<LvdtChannelConfigViewModel>();
            ConnectionStatus = "离线";

            OpenDeviceCommand = new DelegateCommand(async () => await OnOpenDeviceAsync(), () => !IsConnected)
                .ObservesProperty(() => IsConnected);
            CloseDeviceCommand = new DelegateCommand(async () => await OnCloseDeviceAsync(), () => IsConnected)
                .ObservesProperty(() => IsConnected);
            ToggleDeviceCommand = new DelegateCommand(async () =>
            {
                if (IsConnected)
                {
                    await OnCloseDeviceAsync();
                }
                else
                {
                    await OnOpenDeviceAsync();
                }
            });
            StartOutputCommand = new DelegateCommand(async () => await OnStartOutputAsync(), () => CanStartOutput)
                .ObservesProperty(() => CanStartOutput);
            StopOutputCommand = new DelegateCommand(async () => await OnStopOutputAsync(), () => CanStopOutput)
                .ObservesProperty(() => CanStopOutput);
            // 允许在未连接时也保存通道配置（仅在已连接时会下发到硬件）
            ApplyChannelConfigCommand = new DelegateCommand<LvdtChannelConfigViewModel>(
                async (ch) => await OnApplyChannelConfigAsync(ch),
                (ch) => ch != null);
            StartMeasurementCommand = new DelegateCommand(async () => await OnStartMeasurementAsync(), () => IsConnected && !IsMeasuringEnabled);
            StopMeasurementCommand = new DelegateCommand(async () => await OnStopMeasurementAsync(), () => IsConnected && IsMeasuringEnabled);
            MeasureExcitationSignalCommand = new DelegateCommand(async () => await OnMeasureExcitationSignalAsync(), () => IsConnected);
            ResetDeviceCommand = new DelegateCommand(async () => await OnResetDeviceAsync(), () => IsConnected);
            SaveConfigCommand = new DelegateCommand(() => SaveCardConfig());
            ReloadConfigCommand = new DelegateCommand(() => ReloadCardConfig());
            SaveCalibrationCommand = new DelegateCommand(async () => await OnSaveCalibrationAsync(), () => IsConnected);
            LoadCalibrationCommand = new DelegateCommand(async () => await OnLoadCalibrationAsync(), () => IsConnected);

            // 顶部通道选择命令
            SelectChannelCommand = new DelegateCommand<LvdtChannelConfigViewModel>(
                (ch) => {
                    if (ch == null) return;
                    Debug.WriteLine($"[LVDT板卡] 选择通道 {ch.ChannelIndex}（顶部按钮） 打开详情");
                    SelectedChannel = ch;
                    ActiveChannelViewMode = "Simulate";
                    IsChannelListVisible = false; // 切换到单通道详情视图
                    ShowChannelDetail = true;
                },
                (ch) => ch != null);

            ExpandChannelCommand = new DelegateCommand<LvdtChannelConfigViewModel>(
                (ch) =>
                {
                    if (ch == null) return;
                    ch.IsExpanded = !ch.IsExpanded;
                    Debug.WriteLine($"[LVDT板卡] 切换通道展开状态 - CH{ch.ChannelIndex}, IsExpanded={ch.IsExpanded}");
                },
                (ch) => ch != null);

            OpenSimulateOutputCommand = new DelegateCommand(
                () => { ActiveChannelViewMode = "Simulate"; })
                .ObservesProperty(() => SelectedChannel);

            OpenTestInputCommand = new DelegateCommand(
                () => { ActiveChannelViewMode = "Test"; })
                .ObservesProperty(() => SelectedChannel);

            ReadExternalExcitationCommand = new DelegateCommand<LvdtChannelConfigViewModel>(
                async (ch) => await OnReadExternalExcitationAsync(ch),
                (ch) => ch != null && IsConnected)
                .ObservesProperty(() => IsConnected)
                .ObservesProperty(() => SelectedChannel);

            StartWaveformReadCommand = new DelegateCommand(async () => await OnStartWaveformReadAsync(), () => IsConnected)
                .ObservesProperty(() => IsConnected);

            // 波形显示命令：采集1秒的波形数据并显示
            StopWaveformReadCommand = new DelegateCommand(async () => await OnStopWaveformReadAsync(), () => true);
            SetChannelInternalExcCommand = new DelegateCommand<LvdtChannelConfigViewModel>(
                async (ch) => await OnSetChannelInternalExcAsync(ch),
                (ch) => ch != null && IsConnected)
                .ObservesProperty(() => IsConnected)
                .ObservesProperty(() => SelectedChannel);

            StartChannelOutputCommand = new DelegateCommand<LvdtChannelConfigViewModel>(
                async (ch) => await OnStartChannelOutputAsync(ch),
                (ch) => ch != null && IsConnected)
                .ObservesProperty(() => IsConnected)
                .ObservesProperty(() => SelectedChannel);

            StopChannelOutputCommand = new DelegateCommand<LvdtChannelConfigViewModel>(
                async (ch) => await OnStopChannelOutputAsync(ch),
                (ch) => ch != null && IsConnected)
                .ObservesProperty(() => IsConnected)
                .ObservesProperty(() => SelectedChannel);

            StartChannelMeasurementCommand = new DelegateCommand<LvdtChannelConfigViewModel>(
                async (ch) => await OnStartChannelMeasurementAsync(ch),
                (ch) => ch != null && IsConnected)
                .ObservesProperty(() => IsConnected)
                .ObservesProperty(() => SelectedChannel);

            StopChannelMeasurementCommand = new DelegateCommand<LvdtChannelConfigViewModel>(
                async (ch) => await OnStopChannelMeasurementAsync(ch),
                (ch) => ch != null && IsConnected)
                .ObservesProperty(() => IsConnected)
                .ObservesProperty(() => SelectedChannel);

            // 双通道输出命令（默认控制 CH0 & CH1）
            StartDualOutputCommand = new DelegateCommand(async () => await StartDualChannelOutputAsync(0, 1), () => IsConnected)
                .ObservesProperty(() => IsConnected);
            StopDualOutputCommand = new DelegateCommand(async () => await StopDualChannelOutputAsync(0, 1), () => IsConnected)
                .ObservesProperty(() => IsConnected);

            InitializeChannels();

            TryRestoreBackgroundConnection();
        }

        private void TryRestoreBackgroundConnection()
        {
            try
            {
                int slotIndex = -1;
                if (Device is MeasureControl.Models.Devices.DeviceCategories.PxiDeviceBase pxiDevice)
                {
                    slotIndex = pxiDevice.SlotIndex;
                }

                string deviceKey = !string.IsNullOrEmpty(Device?.Id)
                    ? $"{Device.Id}_Slot{slotIndex}"
                    : $"{CardModel}_Slot{slotIndex}";

                if (!_backgroundRunningDevices.TryGetValue(deviceKey, out UIntPtr existingHandle) || existingHandle == UIntPtr.Zero)
                {
                    return;
                }

                _deviceHandle = existingHandle;
                if (_backgroundRunningDeviceIds.TryGetValue(deviceKey, out int existingId) && existingId > 0)
                {
                    _currentAllocatedId = existingId;
                }

                ushort backgroundSlot;
                int backgroundStatus = OKAIDaqNative.DAQDevice_getSlot(_deviceHandle, out backgroundSlot);
                if (backgroundStatus != 0)
                {
                    _backgroundRunningDevices.Remove(deviceKey);
                    _backgroundRunningDeviceIds.Remove(deviceKey);
                    _backgroundRunningOutputStates.Remove(deviceKey);
                    _deviceHandle = UIntPtr.Zero;
                    return;
                }

                bool isOutputRunning = _backgroundRunningOutputStates.TryGetValue(deviceKey, out bool running) && running;
                IsOutputRunning = isOutputRunning;
                IsConnected = true;
                ConnectionStatus = isOutputRunning
                    ? $"已连接 (槽号: {backgroundSlot}, 后台恢复, 输出中)"
                    : $"已连接 (槽号: {backgroundSlot}, 后台恢复)";
            }
            catch
            {
            }
        }

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

            if (!_pxiChassisService.ValidateCardName(ChassisName, newName, Device.Id))
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

        /// <summary>
        /// 查找指定索引的通道配置
        /// </summary>
        private LvdtChannelConfigViewModel GetChannelConfigByIndex(ushort index)
        {
            return ChannelConfigs?.FirstOrDefault(c => c.ChannelIndex == index);
        }

        /// <summary>
        /// 同时启动两路通道的仿真输出（并下发各自配置）。对每路按顺序设置模式/激励/输出并启动采样。
        /// </summary>
        public async Task StartDualChannelOutputAsync(ushort chAIndex, ushort chBIndex)
        {
            if (!IsConnected || _deviceHandle == UIntPtr.Zero)
            {
                Debug.WriteLine($"[LVDT板卡] StartDualChannelOutput 请求被拒绝：设备未连接");
                return;
            }

            var chA = GetChannelConfigByIndex(chAIndex);
            var chB = GetChannelConfigByIndex(chBIndex);
            if (chA == null || chB == null)
            {
                Debug.WriteLine($"[LVDT板卡] StartDualChannelOutput 参数错误：CH{chAIndex} 或 CH{chBIndex} 未找到");
                return;
            }

            Debug.WriteLine($"[LVDT板卡] 开始双通道输出：CH{chAIndex} & CH{chBIndex}");

            // 本方法会对每个通道单独下发配置并启动采样
            async Task<bool> SetupAndStartChannel(LvdtChannelConfigViewModel ch)
            {
                try
                {
                    ushort idx = ch.ChannelIndex;
                    Debug.WriteLine($"[LVDT板卡] DualOutput 设置通道 {idx} 模式 -> Simulate");
                    int st = PXI4087Native.pxi4087_setMode(_deviceHandle, idx,
                        (ushort)PXI4087Constants.pxi4087_Ch_Mode_Sim,
                        (ushort)(ch.UseInternalExcitation ? PXI4087Constants.pxi4087_Ch_Exc_Sour_Int : PXI4087Constants.pxi4087_Ch_Exc_Sour_Ext),
                        0, 0);
                    if (st != 0)
                    {
                        Debug.WriteLine($"[LVDT板卡] DualOutput: pxi4087_setMode 返回 {st} (CH{idx})");
                        return false;
                    }

                    // 如果使用内部激励，设置激励信号
                    if (ch.UseInternalExcitation)
                    {
                        st = PXI4087Native.pxi4087_setIntExcSig(_deviceHandle, idx, ch.ExcitationVoltage, ch.ExcitationFrequency);
                        if (st != 0)
                        {
                            Debug.WriteLine($"[LVDT板卡] DualOutput: pxi4087_setIntExcSig 返回 {st} (CH{idx})");
                            return false;
                        }
                    }

                    // 根据输出模式下发对应输出
                    if (ch.OutputMode == "VaVb")
                    {
                        // 修正硬件映射：第一个参数对应物理 VA，第二个参数对应物理 VB
                        // 硬件映射：param1对应物理VA，param2对应物理VB
                        double param1 = ch.VaVoltage;
                        double param2 = ch.VbVoltage;

                        if (ch.VaInverse) param1 = -param1;
                        if (ch.VbInverse) param2 = -param2;

                        Debug.WriteLine($"[LVDT板卡] DualOutput: CH{idx} 下发参数 param1={param1:F6} (->物理VA), param2={param2:F6} (->物理VB), invA={ch.VaInverse}, invB={ch.VbInverse}");
                        st = PXI4087Native.pxi4087_setLvdtVaVb(_deviceHandle, idx, param1, param2);
                        if (st != 0)
                        {
                            Debug.WriteLine($"[LVDT板卡] DualOutput: pxi4087_setLvdtVaVb 返回 {st} (CH{idx})");
                            return false;
                        }
                    }
                    else if (ch.OutputMode == "Position")
                    {
                        st = PXI4087Native.pxi4087_setLvdtOutPos(_deviceHandle, idx, ch.Position);
                        if (st != 0)
                        {
                            Debug.WriteLine($"[LVDT板卡] DualOutput: pxi4087_setLvdtOutPos 返回 {st} (CH{idx})");
                            return false;
                        }
                    }
                    else if (ch.OutputMode == "SumDiff")
                    {
                        // Sum/Diff 无法独立实现 VA/VB 单独反相；当需要反相(或交换)时转换为 Va/Vb 下发。
                        if (ch.VaInverse || ch.VbInverse)
                        {
                            double va = (ch.Vsum + ch.Vdiff) / 2.0;
                            double vb = (ch.Vsum - ch.Vdiff) / 2.0;
                            if (ch.VaInverse) va = -va;
                            if (ch.VbInverse) vb = -vb;
                            st = PXI4087Native.pxi4087_setLvdtVaVb(_deviceHandle, idx, va, vb);
                            if (st != 0)
                            {
                                Debug.WriteLine($"[LVDT板卡] DualOutput: SumDiff->VaVb 下发失败 {st} (CH{idx}, Vsum={ch.Vsum:F6}, Vdiff={ch.Vdiff:F6} -> Va={va:F6}, Vb={vb:F6}, invA={ch.VaInverse}, invB={ch.VbInverse})");
                                return false;
                            }
                        }
                        else
                        {
                            st = PXI4087Native.pxi4087_setLvdtSumDiff(_deviceHandle, idx, ch.Vsum, ch.Vdiff);
                            if (st != 0)
                            {
                                Debug.WriteLine($"[LVDT板卡] DualOutput: pxi4087_setLvdtSumDiff 返回 {st} (CH{idx})");
                                return false;
                            }
                        }
                    }

                    // 启动该通道输出采样
                    st = PXI4087Native.pxi4087_lvdtStart(_deviceHandle, idx);
                    if (st != 0)
                    {
                        Debug.WriteLine($"[LVDT板卡] DualOutput: pxi4087_lvdtStart 返回 {st} (CH{idx})");
                        return false;
                    }
                    Debug.WriteLine($"[LVDT板卡] DualOutput: CH{idx} 已启动输出");
                    return true;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[LVDT板卡] DualOutput: CH{ch.ChannelIndex} 设置异常 - {ex.Message}");
                    return false;
                }
            }

            // 并行设置并启动两路
            var t1 = SetupAndStartChannel(chA);
            var t2 = SetupAndStartChannel(chB);
            bool[] results = await Task.WhenAll(t1, t2);
            if (results.All(r => r))
            {
                Debug.WriteLine($"[LVDT板卡] DualOutput: CH{chAIndex} & CH{chBIndex} 同步启动成功");
            }
            else
            {
                Debug.WriteLine($"[LVDT板卡] DualOutput: 部分通道启动失败 (CH{chAIndex} & CH{chBIndex})");
            }
        }

        /// <summary>
        /// 停止两路通道的输出采样
        /// </summary>
        public async Task StopDualChannelOutputAsync(ushort chAIndex, ushort chBIndex)
        {
            if (!IsConnected || _deviceHandle == UIntPtr.Zero)
            {
                Debug.WriteLine($"[LVDT板卡] StopDualChannelOutput 请求被拒绝：设备未连接");
                return;
            }
            var chA = GetChannelConfigByIndex(chAIndex);
            var chB = GetChannelConfigByIndex(chBIndex);
            Debug.WriteLine($"[LVDT板卡] 停止双通道输出：CH{chAIndex} & CH{chBIndex}");
            try
            {
                int stA = PXI4087Native.pxi4087_lvdtStop(_deviceHandle, chAIndex);
                int stB = PXI4087Native.pxi4087_lvdtStop(_deviceHandle, chBIndex);
                Debug.WriteLine($"[LVDT板卡] DualStop: lvdtStop 返回 CH{chAIndex}={stA}, CH{chBIndex}={stB}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LVDT板卡] DualStop 异常 - {ex.Message}");
            }
            await Task.CompletedTask;
        }

        // Waveform reading state
        private System.Threading.CancellationTokenSource _waveformCts;
        // Measurement polling state for external excitation
        private System.Threading.CancellationTokenSource _measCts;
        private readonly object _waveformLock = new object();
        private readonly System.Collections.Generic.List<double> _waveformBuffer = new System.Collections.Generic.List<double>();
        private const int MaxWaveformPoints = 2048;

        // Waveform view mode: "Excitation" or "Dynamic"
        private string _waveformViewMode = "Excitation";
        public string WaveformViewMode
        {
            get => _waveformViewMode;
            set => SetProperty(ref _waveformViewMode, value);
        }

        // Waveform signal selection: "Va" or "Vb"
        private string _waveformSignal = "Va";
        public string WaveformSignal
        {
            get => _waveformSignal;
            set => SetProperty(ref _waveformSignal, value);
        }

        // Excitation source: Internal / External / Both
        private string _waveformExcitationSource = "External";
        public string WaveformExcitationSource
        {
            get => _waveformExcitationSource;
            set => SetProperty(ref _waveformExcitationSource, value);
        }

        // Events to notify UI of new waveform samples (snapshot)
        public event Action<double[]> WaveformUpdated;
        // For excitation mode support returning internal (Va) and external (Vb) samples concurrently
        public event Action<double[], double[]> WaveformUpdatedVaVb;

        // Helper: log channel context for detailed debugging when API calls fail
        private void LogChannelContext(LvdtChannelConfigViewModel ch, string prefix)
        {
            if (ch == null)
            {
                Debug.WriteLine($"{prefix}: channel is null");
                return;
            }

            Debug.WriteLine($"{prefix}: CH{ch.ChannelIndex} IsEnabled={ch.IsEnabled} SensorType={ch.SensorType} WorkMode={ch.WorkMode} OutputMode={ch.OutputMode} UseInternalExcitation={ch.UseInternalExcitation} ExcitationVoltage={ch.ExcitationVoltage:F6} ExcitationFrequency={ch.ExcitationFrequency:F1} TransmissionRatio={ch.TransmissionRatio:F6} PhaseDelay={ch.PhaseDelay} DataOutputMode={ch.DataOutputMode} ScanFrequency={ch.ScanFrequency:F1} WaveformLength={ch.WaveformLength} WaveformOutputCount={ch.WaveformOutputCount}");
        }

        private async Task OnStartWaveformReadAsync()
        {
            if (!IsConnected || _deviceHandle == UIntPtr.Zero)
            {
                ReMessageBox.Show("设备未连接！", "错误");
                return;
            }

            Debug.WriteLine($"[LVDT板卡] 开始波形采集 - Mode={WaveformViewMode}, Signal={WaveformSignal}, ExcSource={WaveformExcitationSource}, SelectedChannel={(SelectedChannel != null ? SelectedChannel.ChannelIndex.ToString() : "null")}");

            // 防止重复执行
            lock (_waveformLock)
            {
                if (_waveformCts != null) return;
                _waveformCts = new System.Threading.CancellationTokenSource();
            }

            var token = _waveformCts.Token;

            // 采集参数
            const double captureDuration = 1.0; // 采集1秒的数据
            const int sampleRate = 100; // 每秒100个采样点
            const int totalSamples = (int)(captureDuration * sampleRate);

            await Task.Run(async () =>
            {
                try
                {
                    var samples = new System.Collections.Generic.List<double>();
                    var startTime = System.Diagnostics.Stopwatch.StartNew();

                    // 采集固定时间的数据
                    for (int i = 0; i < totalSamples && !token.IsCancellationRequested; i++)
                    {
                        try
                        {
                            double sample = 0.0;

                            if (WaveformViewMode == "Excitation")
                            {
                                // 激励信号模式：合成正弦波
                                double t = startTime.Elapsed.TotalSeconds;
                                double amp = SelectedChannel != null ? SelectedChannel.ExcitationVoltage : ExcitationVoltage;
                                double freq = SelectedChannel != null ? SelectedChannel.ExcitationFrequency : ExcitationFrequency;

                                // 转换为峰值电压用于正弦波
                                double vPeak = amp * Math.Sqrt(2.0);
                                sample = vPeak * Math.Sin(2.0 * Math.PI * freq * t);

                                Debug.WriteLine($"[LVDT板卡] 激励波形采样 {i+1}/{totalSamples} - 时间:{t:F3}s, 幅度:{sample:F6}V");
                            }
                            else
                            {
                                // 动态输出模式：读取实际的Va/Vb信号
                                if (SelectedChannel != null)
                                {
                                    double va = 0.0, vb = 0.0, ratio = 0.0;
                                    int st = PXI4087Native.pxi4087_getLvdtRmsVol(_deviceHandle, SelectedChannel.ChannelIndex, out va, out vb, out ratio);
                                    if (st == 0)
                                    {
                                        sample = WaveformSignal == "Va" ? va : vb;
                                        Debug.WriteLine($"[LVDT板卡] 输出波形采样 {i+1}/{totalSamples} - {WaveformSignal}={sample:F6}V, Va={va:F6}, Vb={vb:F6}");
                                    }
                                    else
                                    {
                                        Debug.WriteLine($"[LVDT板卡] 读取通道 {SelectedChannel.ChannelIndex} RMS 失败，状态码: {st}");
                                        sample = 0.0; // 使用默认值
                                    }
                                }
                            }

                            samples.Add(sample);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[LVDT板卡] 波形采样异常: {ex.Message}");
                            samples.Add(0.0); // 添加默认值避免中断
                        }

                        await Task.Delay(1000 / sampleRate, token); // 控制采样间隔
                    }

                    // 采集完成后，在UI线程更新显示
                    if (!token.IsCancellationRequested && samples.Count > 0)
                    {
                        lock (_waveformLock)
                        {
                            if (WaveformViewMode == "Excitation")
                            {
                                // 激励模式：显示内部激励波形
                                WaveformUpdatedVaVb?.Invoke(samples.ToArray(), samples.ToArray()); // 两个相同的波形用于兼容
                            }
                            else
                            {
                                // 输出模式：显示Va/Vb波形
                                WaveformUpdated?.Invoke(samples.ToArray());
                            }
                        }

                        Debug.WriteLine($"[LVDT板卡] 波形采集完成，共采集 {samples.Count} 个采样点");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[LVDT板卡] 波形采集异常: {ex.Message}");
                }
            }, token);

            // 采集完成后立即停止
            await OnStopWaveformReadAsync();
        }

        private async Task OnStopWaveformReadAsync()
        {
            lock (_waveformLock)
            {
                if (_waveformCts != null)
                {
                    _waveformCts.Cancel();
                    _waveformCts.Dispose();
                    _waveformCts = null;
                }
            }
            Debug.WriteLine("[LVDT板卡] 停止波形读取请求已处理");
            await Task.CompletedTask;
        }

        private async Task OnSetChannelInternalExcAsync(LvdtChannelConfigViewModel channelConfig)
        {
            if (channelConfig == null) return;
            if (!IsConnected || _deviceHandle == UIntPtr.Zero)
            {
                ReMessageBox.Show("设备未连接！", "错误");
                return;
            }

            Debug.WriteLine($"[LVDT板卡] 设置内部激励 - 通道:{channelConfig.ChannelIndex}, Vrms={channelConfig.ExcitationVoltage}, Freq={channelConfig.ExcitationFrequency}");
            
            try
            {
                int status = PXI4087Native.pxi4087_setIntExcSig(_deviceHandle, channelConfig.ChannelIndex, channelConfig.ExcitationVoltage, channelConfig.ExcitationFrequency);
                if (status != 0)
                {
                    ReMessageBox.Show($"通道 {channelConfig.ChannelIndex} 设置内部激励失败，状态码: {status}", "错误");
                    Debug.WriteLine($"[LVDT板卡] 设置内部激励失败 - 通道:{channelConfig.ChannelIndex}, 状态码:{status}");
                    Debug.WriteLine($"[LVDT板卡] API: pxi4087_setIntExcSig returned {status} for CH{channelConfig.ChannelIndex}");
                    LogChannelContext(channelConfig, "[LVDT板卡] 设置内部激励失败时通道上下文");
                    return;
                }

                // 外部激励共享场景：允许选择 CH0 的外部激励作为参考激励
                // （与 4088 用法一致：setMode 后配置 setSelExcCh0Flag）
                status = PXI4087Native.pxi4087_setSelExcCh0Flag(_deviceHandle, channelConfig.ChannelIndex, 0);
                if (status != 0)
                {
                    ReMessageBox.Show($"通道 {channelConfig.ChannelIndex} 设置激励通道标志失败，状态码: {status}", "警告");
                }

                // 外部激励接线场景：可能只在 CH0 接了外部激励，其它通道需要选择 CH0 外部激励作为激励参考
                // 注意：即便是“读取外部激励/采集”，驱动也可能依赖该标志去选择参考源
                try
                {
                    int selExcStatus = PXI4087Native.pxi4087_setSelExcCh0Flag(
                        _deviceHandle,
                        channelConfig.ChannelIndex,
                        0);
                    Debug.WriteLine($"[LVDT板卡] pxi4087_setSelExcCh0Flag CH{channelConfig.ChannelIndex} flag=0 returned {selExcStatus}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[LVDT板卡] 调用 pxi4087_setSelExcCh0Flag 异常: {ex.Message}");
                }
                ReMessageBox.Show($"通道 {channelConfig.ChannelIndex} 内部激励已设置", "成功");
                Debug.WriteLine($"[LVDT板卡] 设置内部激励成功 - 通道:{channelConfig.ChannelIndex}");
            }
            catch (Exception ex)
            {
                ReMessageBox.Show($"设置内部激励时发生错误: {ex.Message}", "错误");
                Debug.WriteLine($"[LVDT板卡] 设置内部激励异常 - {ex.Message}");
            }
        }

        private void InitializeChannels()
        {
            ChannelConfigs.Clear();
            if (Device is LvdtSimulatorDevice lvdtDevice)
            {
                // LVDT/RVDT模式：创建LVDT通道配置
                for (int i = 0; i < lvdtDevice.ChannelCount; i++)
                {
                    var channelConfig = new LvdtChannelConfigViewModel
                    {
                        ChannelIndex = (ushort)i,
                        ChannelName = $"CH{i}",
                        IsEnabled = false,
                        SensorType = "LVDT",  // LVDT or RVDT
                        WorkMode = "SimulateOutput",  // TestInput, SimulateOutput
                        OutputMode = "Position",  // Position, VaVb, SumDiff
                        Position = 0.0,  // -1.0 to 1.0
                        VaVoltage = 3.0,
                        VbVoltage = 2.0,
                        Vsum = 5.0,
                        Vdiff = 1.0,

                        // Waveform output defaults
                        DataOutputMode = 0,  // Position mode
                        ScanFrequency = 1000.0,  // 1kHz
                        ScanPeriod = 0.001,  // 1ms
                        WaveformLength = 100,
                        WaveformOutputCount = 1,
                        WaveformData = null,

                        // RVDT/Resolver defaults
                        ResolverPhaseDiff = 0.0,
                        ResolverOutputAngle = 0.0,
                        ResolverMotorSpeed = 6.0,
                        ResolverWaveformData = null,
                        AutoLoadResolverWave = false,
                        ResolverWaveformLength = 100,
                        ResolverStartAngle = 0.0,
                        ResolverEndAngle = 360.0,

                        // Excitation channel defaults
                        UseExcCh0Flag = false
                    };
                    // initialize per-channel defaults from panel defaults
                    channelConfig.UseInternalExcitation = _useInternalExcitation;
                    channelConfig.ExcitationVoltage = _excitationVoltage;
                    channelConfig.ExcitationFrequency = _excitationFrequency;
                    channelConfig.TransmissionRatio = _transmissionRatio;
                    channelConfig.PhaseDelay = _phaseDelay;
                    channelConfig.AdcRangeIndex = _adcRangeIndex;
                    ChannelConfigs.Add(channelConfig);
                }

                if (ChannelConfigs.Count > 0)
                {
                    SelectedChannel = ChannelConfigs[0];
                }
                // Top buttons: first 7 channels (or less)
                TopChannelButtons = new ObservableCollection<LvdtChannelConfigViewModel>(ChannelConfigs.Take(8));
            }
        }

        private bool HasValidConfig()
        {
            return ChannelConfigs.Any(ch => ch.IsEnabled);
        }

        private int ApplyDynamicBufferConfig(LvdtChannelConfigViewModel channelConfig)
        {
            if (channelConfig == null) return -1;
            if (_deviceHandle == UIntPtr.Zero) return -1;

            ushort chIndex = channelConfig.ChannelIndex;

            // 设置数据输出模式为缓冲区(动态)输出
            int status = PXI4087Native.pxi4087_setLvdtDataOutMode(_deviceHandle, chIndex, (ushort)PXI4087Constants.pxi4087_Lvdt_Data_Out_Buffer);
            Debug.WriteLine($"[LVDT板卡] ApplyDynamicBufferConfig: setLvdtDataOutMode(Buffer) CH{chIndex} returned {status}");
            if (status != 0) return status;

            // 以每点时间间隔为准（界面绑定 DynamicPointPeriod）
            // 注意：驱动要求周期是 1us 的整数倍
            double scanPeriod = channelConfig.DynamicPointPeriod > 0 ? channelConfig.DynamicPointPeriod : channelConfig.ScanPeriod;
            scanPeriod = Math.Max(1e-6, Math.Round(scanPeriod / 1e-6) * 1e-6);
            double scanFrequency = 1.0 / scanPeriod;

            status = PXI4087Native.pxi4087_setLvdtScanFreq(_deviceHandle, chIndex, scanFrequency);
            Debug.WriteLine($"[LVDT板卡] ApplyDynamicBufferConfig: setLvdtScanFreq CH{chIndex} returned {status} (freq={scanFrequency:F6})");
            if (status != 0) return status;

            int setPeriodStatus = -1;
            try
            {
                setPeriodStatus = PXI4087Native.pxi4087_setLvdtScanPeriod(_deviceHandle, chIndex, scanPeriod);
            }
            catch
            {
            }
            Debug.WriteLine($"[LVDT板卡] ApplyDynamicBufferConfig: setLvdtScanPeriod CH{chIndex} returned {setPeriodStatus} (period={scanPeriod:F6}s)");
            if (setPeriodStatus != 0) return setPeriodStatus;

            // 重要：界面“波形个数(0连续输出)”绑定的是 DynamicOutputCount
            ushort waveOut = (ushort)Math.Max(0, channelConfig.DynamicOutputCount);
            status = PXI4087Native.pxi4087_setLvdtWaveOut(_deviceHandle, chIndex, waveOut);
            Debug.WriteLine($"[LVDT板卡] ApplyDynamicBufferConfig: setLvdtWaveOut CH{chIndex} returned {status} (waveOut={waveOut})");
            if (status != 0) return status;

            // 生成/上载位置波形数据
            int waveformLength = Math.Max(1, Math.Min(2048, channelConfig.DynamicWaveformLength));
            double[] waveformData = channelConfig.WaveformData;
            if (waveformData == null || waveformData.Length != waveformLength)
            {
                double startPos = channelConfig.DynamicStartPosition;
                double endPos = channelConfig.DynamicEndPosition;

                waveformData = new double[waveformLength];
                if (waveformLength == 1)
                {
                    waveformData[0] = Math.Max(-1.0, Math.Min(1.0, startPos));
                }
                else
                {
                    for (int i = 0; i < waveformLength; i++)
                    {
                        double t = (double)i / (waveformLength - 1);
                        waveformData[i] = Math.Max(-1.0, Math.Min(1.0, startPos + t * (endPos - startPos)));
                    }
                }
                channelConfig.WaveformData = waveformData;
            }

            status = PXI4087Native.pxi4087_setLvdtWaveData(_deviceHandle, chIndex, (uint)waveformData.Length, waveformData);
            Debug.WriteLine($"[LVDT板卡] ApplyDynamicBufferConfig: setLvdtWaveData CH{chIndex} returned {status} (len={waveformData.Length})");
            return status;
        }

        private async Task OnOpenDeviceAsync()
        {
            try
            {
                ConnectionStatus = "连接中";

                int slotIndex = -1;
                if (Device is MeasureControl.Models.Devices.DeviceCategories.PxiDeviceBase pxiDevice)
                {
                    slotIndex = pxiDevice.SlotIndex;
                }

                string deviceKey = !string.IsNullOrEmpty(Device?.Id)
                    ? $"{Device.Id}_Slot{slotIndex}"
                    : $"{CardModel}_Slot{slotIndex}";

                // 首先检查设备是否已经在后台运行（用于页面切换后的恢复）
                if (_backgroundRunningDevices.TryGetValue(deviceKey, out UIntPtr existingHandle) && existingHandle != UIntPtr.Zero)
                {
                    // 设备已经在后台运行，直接使用现有的句柄
                    _deviceHandle = existingHandle;
                    if (_backgroundRunningDeviceIds.TryGetValue(deviceKey, out int existingId) && existingId > 0)
                    {
                        _currentAllocatedId = existingId;
                    }
                    Debug.WriteLine($"[LVDT板卡] 发现后台运行的设备 {deviceKey}, 直接使用现有连接");

                    // 验证连接是否仍然有效
                    ushort backgroundSlot;
                    int backgroundStatus = OKAIDaqNative.DAQDevice_getSlot(_deviceHandle, out backgroundSlot);
                    if (backgroundStatus == 0)
                    {
                        bool isOutputRunning = _backgroundRunningOutputStates.TryGetValue(deviceKey, out bool running) && running;
                        IsOutputRunning = isOutputRunning;
                        IsConnected = true;
                        ConnectionStatus = isOutputRunning
                            ? $"已连接 (槽号: {backgroundSlot}, 后台恢复, 输出中)"
                            : $"已连接 (槽号: {backgroundSlot}, 后台恢复)";
                        Debug.WriteLine($"[LVDT板卡] 后台设备连接验证成功 - 槽号: {backgroundSlot}");
                        return;
                    }
                    else
                    {
                        // 后台设备连接已失效，清理并重新连接
                        Debug.WriteLine($"[LVDT板卡] 后台设备连接失效，清理并重新连接");
                        _backgroundRunningDevices.Remove(deviceKey);
                        _backgroundRunningDeviceIds.Remove(deviceKey);
                        _backgroundRunningOutputStates.Remove(deviceKey);
                        _deviceHandle = UIntPtr.Zero;
                    }
                }

                // 为LVDT/RVDT板卡分配唯一的设备ID（线程安全）
                if (_currentAllocatedId == 0)
                {
                    _currentAllocatedId = AllocateLvdtId();
                }

                // 打开设备
                _deviceHandle = PXI4087Native.pxi4087_openDevice((ushort)_currentAllocatedId);
                
                if (_deviceHandle == UIntPtr.Zero)
                {
                    ConnectionStatus = "连接失败";
                    ReMessageBox.Show($"4087设备连接失败！(ID: {_currentAllocatedId})", "错误");
                    return;
                }

                // 获取槽号验证连接
                ushort slot;



                int status = OKAIDaqNative.DAQDevice_getSlot(_deviceHandle, out slot);
                if (status != 0)
                {
                    PXI4087Native.pxi4087_releaseDevice(_deviceHandle);
                    _deviceHandle = UIntPtr.Zero;
                    ConnectionStatus = "连接失败";
                    ReMessageBox.Show($"获取设备槽号失败，状态码: {status}", "错误");
                    return;
                }

                IsConnected = true;
                ConnectionStatus = $"已连接 (槽号: {slot})";

                // 根据 CardName/CardModel 判断是否支持旋变功能
                string modelId = !string.IsNullOrEmpty(CardName) ? CardName : CardModel;
                IsResolverCapable = (modelId != null) &&
                                     (modelId.IndexOf("4088", StringComparison.OrdinalIgnoreCase) >= 0
                                      || modelId.IndexOf("resolver", StringComparison.OrdinalIgnoreCase) >= 0);

                // DEBUG: 板卡连接成功
                Debug.WriteLine($"[LVDT板卡] 连接成功 - 设备ID: {_currentAllocatedId}, 槽号: {slot}, 句柄: {_deviceHandle}");
                Debug.WriteLine($"[LVDT板卡] 通道配置状态 - 总通道数: {ChannelConfigs.Count}, 启用通道数: {ChannelConfigs.Count(ch => ch.IsEnabled)}");
                Debug.WriteLine($"[LVDT板卡] 输出控制状态 - CanStartOutput: {CanStartOutput}, CanStopOutput: {CanStopOutput}, IsOutputRunning: {IsOutputRunning}");
                Debug.WriteLine($"[LVDT板卡] IsResolverCapable = {IsResolverCapable} (Card: {modelId})");
            }
            catch (Exception ex)
            {
                ConnectionStatus = "连接失败";
                ReMessageBox.Show($"连接设备时发生错误: {ex.Message}", "错误");
            }
        }

        private async Task OnCloseDeviceAsync()
        {
            try
            {
                int slotIndex = -1;
                if (Device is MeasureControl.Models.Devices.DeviceCategories.PxiDeviceBase pxiDevice)
                {
                    slotIndex = pxiDevice.SlotIndex;
                }

                string deviceKey = !string.IsNullOrEmpty(Device?.Id)
                    ? $"{Device.Id}_Slot{slotIndex}"
                    : $"{CardModel}_Slot{slotIndex}";

                // 显式关闭设备：清理后台缓存，避免下次打开误用旧句柄/旧ID
                _backgroundRunningDevices.Remove(deviceKey);
                _backgroundRunningDeviceIds.Remove(deviceKey);
                _backgroundRunningOutputStates.Remove(deviceKey);

                if (IsOutputRunning)
                {
                    await OnStopOutputAsync();
                }

                if (_deviceHandle != UIntPtr.Zero)
                {
                    PXI4087Native.pxi4087_releaseDevice(_deviceHandle);
                    _deviceHandle = UIntPtr.Zero;
                }

                // 释放分配的ID（断开时释放，允许重连时重用ID）
                if (_currentAllocatedId != 0)
                {
                    ReleaseLvdtId(_currentAllocatedId);
                    _currentAllocatedId = 0;
                }

                IsConnected = false;
                ConnectionStatus = "离线";
            }
            catch (Exception ex)
            {
                ReMessageBox.Show($"断开设备时发生错误: {ex.Message}", "错误");
            }
        }

        private async Task OnStartOutputAsync()
        {
            try
            {
                if (_deviceHandle == UIntPtr.Zero)
                {
                    ReMessageBox.Show("设备未连接！", "错误");
                    return;
                }

                // 对每个启用的通道进行配置
                foreach (var channelConfig in ChannelConfigs.Where(ch => ch.IsEnabled))
                {
                    ushort chIndex = channelConfig.ChannelIndex;
                    int outputMode = PXI4087Constants.pxi4087_Ch_Out_Mode_Rvdt_Lvdt;

                    // 1. 设置通道工作模式：仿真模式
                    int status = PXI4087Native.pxi4087_setMode(
                        _deviceHandle,
                        chIndex,
                        (ushort)PXI4087Constants.pxi4087_Ch_Mode_Sim,  // 仿真模式
                        (ushort)(UseInternalExcitation ? PXI4087Constants.pxi4087_Ch_Exc_Sour_Int : PXI4087Constants.pxi4087_Ch_Exc_Sour_Ext),  // 激励源
                        0,  // Va正向输出
                        0   // Vb正向输出
                    );

                    if (status != 0)
                    {
                        ReMessageBox.Show($"通道 {chIndex} 设置模式失败，状态码: {status}", "错误");
                        continue;
                    }

                    // 2. 如果使用内部激励，配置激励信号
                    if (UseInternalExcitation)
                    {
                        status = PXI4087Native.pxi4087_setIntExcSig(
                            _deviceHandle,
                            chIndex,
                            ExcitationVoltage,  // 电压有效值 1-10Vrms
                            ExcitationFrequency  // 频率 360-20000Hz
                        );

                        if (status != 0)
                        {
                            ReMessageBox.Show($"通道 {chIndex} 设置激励信号失败，状态码: {status}", "错误");
                            continue;
                        }
                    }

                    // 3. 应用高级配置
                    // 设置传输比
                    status = PXI4087Native.pxi4087_setTransRatio(_deviceHandle, chIndex, TransmissionRatio);
                    if (status != 0)
                    {
                        ReMessageBox.Show($"通道 {chIndex} 设置传输比失败，状态码: {status}", "警告");
                    }

                    // 设置相位延迟
                    status = PXI4087Native.pxi4087_setLvdtPhaseDelay(_deviceHandle, chIndex, (ushort)PhaseDelay);
                    if (status != 0)
                    {
                        ReMessageBox.Show($"通道 {chIndex} 设置相位延迟失败，状态码: {status}", "警告");
                    }

                    // 设置ADC范围
                    status = PXI4087Native.pxi4087_setLvdtAdcRange(_deviceHandle, chIndex, (ushort)AdcRangeIndex);
                    if (status != 0)
                    {
                        ReMessageBox.Show($"通道 {chIndex} 设置ADC范围失败，状态码: {status}", "警告");
                    }

                    // 3. 设置输出值
                    status = await ApplyLvdtChannelOutputAsync(channelConfig);

                    if (status != 0)
                    {
                        ReMessageBox.Show($"通道 {chIndex} 设置输出值失败，状态码: {status}", "错误");
                        continue;
                    }

                    // 4. 动态输出：只要 IsDynamicOutput==true，就必须配置缓冲区参数并下发波形数据
                    if (status == 0 && channelConfig.IsDynamicOutput)
                    {
                        status = ApplyDynamicBufferConfig(channelConfig);
                        if (status != 0)
                        {
                            ReMessageBox.Show($"通道 {chIndex} 配置动态缓冲输出失败，状态码: {status}", "警告");
                        }
                    }

                    // 5. 启动LVDT输出
                    status = PXI4087Native.pxi4087_lvdtStart(_deviceHandle, chIndex);
                    if (status != 0)
                    {
                        ReMessageBox.Show($"通道 {chIndex} 启动失败，状态码: {status}", "错误");
                    }
                    else
                    {
                        // 立即读取并记录 Va/Vb RMS 及激励 RMS 以便调试
                        try
                        {
                            double vaRms = 0.0, vbRms = 0.0, sumRatio = 0.0;
                            int meas = PXI4087Native.pxi4087_getLvdtRmsVol(_deviceHandle, chIndex, out vaRms, out vbRms, out sumRatio);
                            Debug.WriteLine($"[LVDT板卡] 调试测量: pxi4087_getLvdtRmsVol CH{chIndex} returned {meas} -> VaRms={vaRms:F6}, VbRms={vbRms:F6}, sumRatio={sumRatio:F6}");

                            double excRms = 0.0;
                            int excSt = PXI4087Native.pxi4087_getLvdtExcSigRms(_deviceHandle, chIndex, out excRms);
                            Debug.WriteLine($"[LVDT板卡] 调试测量: pxi4087_getLvdtExcSigRms CH{chIndex} returned {excSt} -> ExcRms={excRms:F6}");
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[LVDT板卡] 调试测量异常: {ex.Message}");
                        }
                    }
                }

                IsOutputRunning = true;

                // DEBUG: 输出启动成功
                var enabledChannels = ChannelConfigs.Where(ch => ch.IsEnabled).Select(ch => ch.ChannelIndex).ToList();
                Debug.WriteLine($"[LVDT板卡] 输出启动成功 - 启用通道: {string.Join(", ", enabledChannels)}");
                Debug.WriteLine($"[LVDT板卡] 输出配置 - 激励源: {(UseInternalExcitation ? "内部" : "外部")}, 电压: {ExcitationVoltage:F2}V, 频率: {ExcitationFrequency:F2}Hz");

                ReMessageBox.Show("4087输出启动成功！", "成功");
            }
            catch (Exception ex)
            {
                ReMessageBox.Show($"启动输出时发生错误: {ex.Message}", "错误");
            }
        }

        private async Task OnStopOutputAsync()
        {
            try
            {
                if (_deviceHandle == UIntPtr.Zero)
                {
                    return;
                }

                // 停止所有启用的通道
                foreach (var channelConfig in ChannelConfigs.Where(ch => ch.IsEnabled))
                {
                    int status = PXI4087Native.pxi4087_lvdtStop(_deviceHandle, channelConfig.ChannelIndex);
                    if (status != 0)
                    {
                        ReMessageBox.Show($"通道 {channelConfig.ChannelIndex} 停止失败，状态码: {status}", "警告");
                    }
                }

                IsOutputRunning = false;
            }
            catch (Exception ex)
            {
                ReMessageBox.Show($"停止输出时发生错误: {ex.Message}", "错误");
            }
        }

        private async Task<int> ApplyLvdtChannelOutputAsync(LvdtChannelConfigViewModel channelConfig)
        {
            return await Task.Run(() =>
            {
                ushort chIndex = channelConfig.ChannelIndex;
                int status = 0;

                switch (channelConfig.WorkMode)
                {
                    case "SimulateOutput":
                        // 仿真输出模式：设置工作模式为仿真模式，并配置输出值
                        status = PXI4087Native.pxi4087_setMode(
                            _deviceHandle,
                            chIndex,
                            (ushort)PXI4087Constants.pxi4087_Ch_Mode_Sim,  // 仿真模式
                            (ushort)(channelConfig.UseInternalExcitation ? PXI4087Constants.pxi4087_Ch_Exc_Sour_Int : PXI4087Constants.pxi4087_Ch_Exc_Sour_Ext),
                            0, 0
                        );

                        if (status == 0)
                        {
                            // 根据传感器类型和输出模式设置输出值
                            Debug.WriteLine($"[LVDT板卡] 开始设置输出值 - 传感器类型:{channelConfig.SensorType}, 输出模式:{channelConfig.OutputMode}, 角度:{channelConfig.ResolverOutputAngle:F1}°, 相位差:{channelConfig.ResolverPhaseDiff:F1}°");

                            if (channelConfig.SensorType == "RVDT")
                            {
                                // RVDT模式：根据硬件型号选择不同的实现方式
                                if (channelConfig.OutputMode == "Resolver")
                                {
                                    Debug.WriteLine($"[LVDT板卡] 检测到Resolver角度输出模式，准备设置角度参数");
                                    // 角度输出模式：将角度转换为RVDT位置值
                                    // 对于4087A硬件，Resolver模式实际上是通过RVDT实现的
                                    double angle = channelConfig.ResolverOutputAngle;

                                    // 将角度转换为RVDT位置值 (-90°到+90° 对应 -1.0到1.0)
                                    double positionValue;
                                    if (angle >= -90 && angle <= 90)
                                    {
                                        positionValue = angle / 90.0;
                                    }
                                    else
                                    {
                                        // 超出范围的角度进行归一化处理
                                        positionValue = Math.Max(-1.0, Math.Min(1.0, angle / 90.0));
                                        Debug.WriteLine($"[LVDT板卡] 角度 {angle:F1}° 超出RVDT典型范围，已限制到 {positionValue:F3}");
                                    }

                                    Debug.WriteLine($"[LVDT板卡] Resolver角度模式 - 角度:{angle:F1}° → 位置值:{positionValue:F3}");

                                    // 验证位置值是否在有效范围内
                                    if (positionValue < -1.0 || positionValue > 1.0)
                                    {
                                        Debug.WriteLine($"[LVDT板卡] 警告: 位置值 {positionValue:F3} 超出RVDT有效范围 [-1.0, 1.0]");
                                    }

                                    // 对于4087A硬件，使用位置输出函数代替Resolver函数
                                    if (channelConfig.IsDynamicOutput)
                                    {
                                        // 动态角度输出：需要生成角度波形并转换为位置波形
                                        Debug.WriteLine($"[LVDT板卡] 动态Resolver输出 - 使用位置波形模拟角度波形");
                                        // 这里可以扩展为动态角度到位置的转换
                                        status = PXI4087Native.pxi4087_setLvdtOutPos(_deviceHandle, chIndex, positionValue);
                                    }
                                    else
                                    {
                                        // 静态角度输出：直接设置位置值
                                        status = PXI4087Native.pxi4087_setLvdtOutPos(_deviceHandle, chIndex, positionValue);

                                        if (status == 0)
                                        {
                                            Debug.WriteLine($"[LVDT板卡] Resolver角度输出设置成功 - 角度:{angle:F1}°, 位置值:{positionValue:F3}");

                                            // 对于4087A，尝试设置相位差（4087A的相位差功能可能只影响VA不影响VB）
                                            Debug.WriteLine($"[LVDT板卡] 尝试设置相位差: {channelConfig.ResolverPhaseDiff:F1}° (注意: 4087A相位差可能只影响VA)");
                                            int phaseStatus = PXI4087Native.pxi4087_setResolverPhaseDiff(_deviceHandle, chIndex, channelConfig.ResolverPhaseDiff);
                                            if (phaseStatus == 0)
                                            {
                                                Debug.WriteLine($"[LVDT板卡] 相位差设置成功 (VA会变化，VB可能不变 - 这是4087A的正常行为)");
                                            }
                                            else
                                            {
                                                Debug.WriteLine($"[LVDT板卡] 相位差设置失败({phaseStatus}) - 4087A可能不支持此功能");
                                            }

                                            // 为了确保新角度立即生效，强制重新启动输出
                                            Debug.WriteLine($"[LVDT板卡] 强制重新启动输出以应用新角度设置 (IsOutputRunning: {IsOutputRunning})");

                                            // 先停止当前输出（不管是否正在运行）
                                            int stopStatus = PXI4087Native.pxi4087_lvdtStop(_deviceHandle, chIndex);
                                            Debug.WriteLine($"[LVDT板卡] 停止输出结果: {stopStatus} (0=成功)");

                                            // 短暂延迟确保停止完成
                                            System.Threading.Thread.Sleep(100);

                                            // 重新启动输出
                                            int startStatus = PXI4087Native.pxi4087_lvdtStart(_deviceHandle, chIndex);
                                            if (startStatus == 0)
                                            {
                                                Debug.WriteLine($"[LVDT板卡] 输出重新启动成功，新角度已生效");
                                                // 更新UI状态
                                                IsOutputRunning = true;
                                            }
                                            else
                                            {
                                                Debug.WriteLine($"[LVDT板卡] 输出重新启动失败: {startStatus}");
                                            }
                                        }
                                    }
                                }
                                else
                                {
                                    // 传统RVDT模式：直接使用Resolver函数（如果硬件支持）
                                if (channelConfig.IsDynamicOutput)
                                {
                                    // 动态角度输出
                                    status = PXI4087Native.pxi4087_setResolverOutAngle(_deviceHandle, chIndex, channelConfig.ResolverOutputAngle);
                                }
                                else
                                {
                                    // 静态角度输出
                                    status = PXI4087Native.pxi4087_setResolverOutAngle(_deviceHandle, chIndex, channelConfig.ResolverOutputAngle);
                                    if (status == 0)
                                    {
                                        // 设置相位差
                                        status = PXI4087Native.pxi4087_setResolverPhaseDiff(_deviceHandle, chIndex, channelConfig.ResolverPhaseDiff);
                                        }
                                    }
                                }
                            }
                            else
                            {
                                // LVDT模式：使用传统函数
                                if (channelConfig.IsDynamicOutput)
                                {
                                    // 动态输出：生成波形数据
                                    int waveformLength = channelConfig.DynamicWaveformLength;
                                    double startPos = channelConfig.DynamicStartPosition;
                                    double endPos = channelConfig.DynamicEndPosition;

                                    // 生成线性位置序列
                                    channelConfig.WaveformData = new double[waveformLength];
                                    for (int i = 0; i < waveformLength; i++)
                                    {
                                        // 线性插值：从起始位置到结束位置
                                        double t = (double)i / (waveformLength - 1);
                                        channelConfig.WaveformData[i] = startPos + t * (endPos - startPos);
                                    }

                                    // 设置输出次数：如果为0则设为1（连续输出）
                                    channelConfig.WaveformOutputCount = Math.Max(0, channelConfig.DynamicOutputCount);

                                    Debug.WriteLine($"[LVDT板卡] 动态输出波形生成 - 长度:{waveformLength}, 起始:{startPos:F3}, 结束:{endPos:F3}, 输出次数:{channelConfig.WaveformOutputCount}");
                                }
                                else
                                {
                                    // 静态输出根据具体模式设置
                switch (channelConfig.OutputMode)
                {
                    case "Position":
                        status = PXI4087Native.pxi4087_setLvdtOutPos(_deviceHandle, chIndex, channelConfig.Position);
                        break;
                    case "VaVb":
                        {
                            // 修正硬件映射：第一个参数对应物理VA，第二个参数对应物理VB
                            double param1 = channelConfig.VaVoltage; // 下发到硬件第一个参数（物理VA）
                            double param2 = channelConfig.VbVoltage; // 下发到硬件第二个参数（物理VB）

                            if (channelConfig.VaInverse) param1 = -param1;
                            if (channelConfig.VbInverse) param2 = -param2;

                            Debug.WriteLine($"[LVDT板卡] ApplyOutput: CH{chIndex} 下发 param1={param1:F6} (->VA), param2={param2:F6} (->VB), invA={channelConfig.VaInverse}, invB={channelConfig.VbInverse}");
                            status = PXI4087Native.pxi4087_setLvdtVaVb(_deviceHandle, chIndex, param1, param2);
                            Debug.WriteLine($"[LVDT板卡] pxi4087_setLvdtVaVb CH{chIndex} returned {status}");
                        }
                        break;
                    case "SumDiff":
                        // Sum/Diff 无法独立实现 VA/VB 单独反相；当需要反相(或交换)时转换为 Va/Vb 下发。
                        if (channelConfig.VaInverse || channelConfig.VbInverse)
                        {
                            double va = (channelConfig.Vsum + channelConfig.Vdiff) / 2.0;
                            double vb = (channelConfig.Vsum - channelConfig.Vdiff) / 2.0;
                            if (channelConfig.VaInverse) va = -va;
                            if (channelConfig.VbInverse) vb = -vb;
                            status = PXI4087Native.pxi4087_setLvdtVaVb(_deviceHandle, chIndex, va, vb);
                            Debug.WriteLine($"[LVDT板卡] SumDiff->VaVb CH{chIndex} returned {status} (Vsum={channelConfig.Vsum:F6}, Vdiff={channelConfig.Vdiff:F6} -> Va={va:F6}, Vb={vb:F6}, invA={channelConfig.VaInverse}, invB={channelConfig.VbInverse})");
                        }
                        else
                        {
                            status = PXI4087Native.pxi4087_setLvdtSumDiff(_deviceHandle, chIndex, channelConfig.Vsum, channelConfig.Vdiff);
                            Debug.WriteLine($"[LVDT板卡] pxi4087_setLvdtSumDiff CH{chIndex} returned {status} (Vsum={channelConfig.Vsum:F6}, Vdiff={channelConfig.Vdiff:F6})");
                        }
                        break;
                                    }
                                }
                            }
                        }
                        break;

                    case "TestInput":
                        // 测试输入模式：设置工作模式为测试模式，用于读取输入信号
                        status = PXI4087Native.pxi4087_setMode(
                            _deviceHandle,
                            chIndex,
                            (ushort)PXI4087Constants.pxi4087_Ch_Mode_Test,  // 测试模式
                            (ushort)(channelConfig.UseInternalExcitation ? PXI4087Constants.pxi4087_Ch_Exc_Sour_Int : PXI4087Constants.pxi4087_Ch_Exc_Sour_Ext),
                            0, 0
                        );
                        break;
                }

                return status;
            });
        }

        private async Task OnStartChannelOutputAsync(LvdtChannelConfigViewModel channelConfig)
        {
            if (channelConfig == null)
            {
                return;
            }

            Debug.WriteLine($"[LVDT板卡] 请求启动通道输出 - CH{channelConfig.ChannelIndex}, WorkMode={channelConfig.WorkMode}, IsConnected={IsConnected}");

            if (!IsConnected || _deviceHandle == UIntPtr.Zero)
            {
                ReMessageBox.Show("设备未连接！", "错误");
                Debug.WriteLine($"[LVDT板卡] 启动通道输出失败 - 设备未连接 (CH{channelConfig.ChannelIndex})");
                return;
            }

            try
            {
                ushort chIndex = channelConfig.ChannelIndex;

                if (channelConfig.WorkMode == "TestInput")
                {
                    // 测试输入模式：设置模式并启动测量
                    await OnStartChannelMeasurementAsync(channelConfig);
                    return;
                }

                // 仿真输出模式：设置工作模式为仿真模式
                int status = PXI4087Native.pxi4087_setMode(
                    _deviceHandle,
                    chIndex,
                    (ushort)PXI4087Constants.pxi4087_Ch_Mode_Sim,
                    (ushort)(channelConfig.UseInternalExcitation ? PXI4087Constants.pxi4087_Ch_Exc_Sour_Int : PXI4087Constants.pxi4087_Ch_Exc_Sour_Ext),
                    0,
                    0
                );

                if (status != 0)
                {
                    ReMessageBox.Show($"通道 {chIndex} 设置仿真模式失败，状态码: {status}", "错误");
                    Debug.WriteLine($"[LVDT板卡] 通道 {chIndex} 设置仿真模式失败 - 状态码: {status}");
                    return;
                }

                // 如果使用内部激励，按通道级参数设置激励信号
                if (channelConfig.UseInternalExcitation)
                {
                    status = PXI4087Native.pxi4087_setIntExcSig(
                        _deviceHandle,
                        chIndex,
                        channelConfig.ExcitationVoltage,
                        channelConfig.ExcitationFrequency
                    );

                    if (status != 0)
                    {
                        ReMessageBox.Show($"通道 {chIndex} 设置激励信号失败，状态码: {status}", "错误");
                        Debug.WriteLine($"[LVDT板卡] 通道 {chIndex} 设置激励信号失败 - 状态码: {status}");
                        return;
                    }
                }

                // 应用高级配置（通道级）
                status = PXI4087Native.pxi4087_setTransRatio(_deviceHandle, chIndex, channelConfig.TransmissionRatio);
                if (status != 0)
                {
                    ReMessageBox.Show($"通道 {chIndex} 设置传输比失败，状态码: {status}", "警告");
                }

                status = PXI4087Native.pxi4087_setLvdtPhaseDelay(_deviceHandle, chIndex, (ushort)channelConfig.PhaseDelay);
                if (status != 0)
                {
                    ReMessageBox.Show($"通道 {chIndex} 设置相位延迟失败，状态码: {status}", "警告");
                }

                status = PXI4087Native.pxi4087_setLvdtAdcRange(_deviceHandle, chIndex, (ushort)channelConfig.AdcRangeIndex);
                if (status != 0)
                {
                    ReMessageBox.Show($"通道 {chIndex} 设置ADC范围失败，状态码: {status}", "警告");
                }

                // 应用输出值
                status = await ApplyLvdtChannelOutputAsync(channelConfig);
                if (status != 0)
                {
                    ReMessageBox.Show($"通道 {chIndex} 应用输出值失败，状态码: {status}", "错误");
                    Debug.WriteLine($"[LVDT板卡] 通道 {chIndex} 应用输出值失败 - 状态码: {status}");
                    return;
                }

                // 4087A 位置动态输出：调用方式与 4088 一致，动态时一次性下发 Buffer/Scan/WaveOut/WaveData
                if (channelConfig.IsDynamicOutput)
                {
                    status = ApplyDynamicBufferConfig(channelConfig);
                    if (status != 0)
                    {
                        ReMessageBox.Show($"通道 {chIndex} 配置动态缓冲输出失败，状态码: {status}", "错误");
                        Debug.WriteLine($"[LVDT板卡] 通道 {chIndex} 配置动态缓冲输出失败 - 状态码: {status}");
                        return;
                    }
                }
                else
                {
                    status = PXI4087Native.pxi4087_setLvdtDataOutMode(_deviceHandle, chIndex, (ushort)PXI4087Constants.pxi4087_Lvdt_Data_Out_Fix);
                    if (status != 0)
                    {
                        ReMessageBox.Show($"通道 {chIndex} 设置数据输出模式为静态模式失败，状态码: {status}", "警告");
                    }
                }

                // 如果是RVDT/Resolver模式，设置相关参数
                if (channelConfig.WorkMode == "SimulateOutput" && channelConfig.OutputMode.Contains("Resolver"))
                {
                    // 设置旋变相位差
                    status = PXI4087Native.pxi4087_setResolverPhaseDiff(_deviceHandle, chIndex, channelConfig.ResolverPhaseDiff);
                    if (status != 0)
                    {
                        ReMessageBox.Show($"通道 {chIndex} 设置旋变相位差失败，状态码: {status}", "警告");
                    }

                    // 设置旋变输出角度
                    status = PXI4087Native.pxi4087_setResolverOutAngle(_deviceHandle, chIndex, channelConfig.ResolverOutputAngle);
                    if (status != 0)
                    {
                        ReMessageBox.Show($"通道 {chIndex} 设置旋变输出角度失败，状态码: {status}", "警告");
                    }

                    // 设置旋变电机速度
                    status = PXI4087Native.pxi4087_setResolverMotorSpeed(_deviceHandle, chIndex, channelConfig.ResolverMotorSpeed);
                    if (status != 0)
                    {
                        ReMessageBox.Show($"通道 {chIndex} 设置旋变电机速度失败，状态码: {status}", "警告");
                    }

                    // 如果启用了自动加载旋变波形
                    if (channelConfig.AutoLoadResolverWave)
                    {
                        // 使用 GoBackOutput 作为 goBackFlag，AutoLoadResolverWave 控制是否调用自动加载函数
                        status = PXI4087Native.pxi4087_autoLoadResolverWave(_deviceHandle, chIndex,
                            (ushort)(channelConfig.GoBackOutput ? 1 : 0),
                            (uint)channelConfig.ResolverWaveformLength,
                            channelConfig.ResolverStartAngle,
                            channelConfig.ResolverEndAngle);
                        if (status != 0)
                        {
                            ReMessageBox.Show($"通道 {chIndex} 自动加载旋变波形失败，状态码: {status}", "警告");
                        }
                    }
                    // 否则如果有旋变波形数据，手动设置
                    else if (channelConfig.ResolverWaveformData != null && channelConfig.ResolverWaveformData.Length > 0)
                    {
                        status = PXI4087Native.pxi4087_setResolverWaveData(_deviceHandle, chIndex,
                            (uint)channelConfig.ResolverWaveformData.Length, channelConfig.ResolverWaveformData);
                        if (status != 0)
                        {
                            ReMessageBox.Show($"通道 {chIndex} 设置旋变波形数据失败，状态码: {status}", "警告");
                        }
                    }
                }

                // 启动该通道输出
                status = PXI4087Native.pxi4087_lvdtStart(_deviceHandle, chIndex);
                if (status != 0)
                {
                    ReMessageBox.Show($"通道 {chIndex} 启动失败，状态码: {status}", "错误");
                    Debug.WriteLine($"[LVDT板卡] 通道 {chIndex} 启动失败 - 状态码: {status}");
                    return;
                }

                ReMessageBox.Show($"通道 {chIndex} 输出已启动", "成功");
                Debug.WriteLine($"[LVDT板卡] 通道 {chIndex} 输出已启动");
            }
            catch (Exception ex)
            {
                ReMessageBox.Show($"启动通道输出时发生错误: {ex.Message}", "错误");
            }
        }

        private async Task OnStopChannelOutputAsync(LvdtChannelConfigViewModel channelConfig)
        {
            if (channelConfig == null) return;
            if (!IsConnected || _deviceHandle == UIntPtr.Zero)
            {
                ReMessageBox.Show("设备未连接！", "错误");
                return;
            }

            Debug.WriteLine($"[LVDT板卡] 请求停止通道输出 - CH{channelConfig.ChannelIndex}, IsConnected={IsConnected}");

            try
            {
                int status = PXI4087Native.pxi4087_lvdtStop(_deviceHandle, channelConfig.ChannelIndex);
                if (status != 0)
                {
                    ReMessageBox.Show($"通道 {channelConfig.ChannelIndex} 停止失败，状态码: {status}", "警告");
                    Debug.WriteLine($"[LVDT板卡] 通道 {channelConfig.ChannelIndex} 停止失败 - 状态码: {status}");
                    return;
                }
                ReMessageBox.Show($"通道 {channelConfig.ChannelIndex} 已停止", "成功");
                Debug.WriteLine($"[LVDT板卡] 通道 {channelConfig.ChannelIndex} 已停止");
            }
            catch (Exception ex)
            {
                ReMessageBox.Show($"停止通道输出时发生错误: {ex.Message}", "错误");
                Debug.WriteLine($"[LVDT板卡] 停止通道输出异常 - {ex.Message}");
            }
        }

        private async Task OnStartChannelMeasurementAsync(LvdtChannelConfigViewModel channelConfig)
        {
            if (channelConfig == null) return;
            if (!IsConnected || _deviceHandle == UIntPtr.Zero)
            {
                ReMessageBox.Show("设备未连接！", "错误");
                return;
            }

            Debug.WriteLine($"[LVDT板卡] 请求启动通道测量 - CH{channelConfig.ChannelIndex}, UseInternalExcitation={channelConfig.UseInternalExcitation}");

            try
            {
                // 确保通道处于测试模式以进行测量
                int status = PXI4087Native.pxi4087_setMode(
                    _deviceHandle,
                    channelConfig.ChannelIndex,
                    (ushort)PXI4087Constants.pxi4087_Ch_Mode_Test,  // 测试模式
                    (ushort)(channelConfig.UseInternalExcitation ? PXI4087Constants.pxi4087_Ch_Exc_Sour_Int : PXI4087Constants.pxi4087_Ch_Exc_Sour_Ext),
                    0, 0
                );

                if (status != 0)
                {
                    ReMessageBox.Show($"通道 {channelConfig.ChannelIndex} 设置测试模式失败，状态码: {status}", "错误");
                    Debug.WriteLine($"[LVDT板卡] 通道 {channelConfig.ChannelIndex} 设置测试模式失败 - 状态码: {status}");
                    LogChannelContext(channelConfig, "[LVDT板卡] 测量前通道上下文（SetMode失败）");
                    return;
                }

                IsMeasuringEnabled = true;
                // 立即测量该通道的 Va/Vb/Ratio 并更新通道显示
                double vaRms = 0.0, vbRms = 0.0, sumRatio = 0.0;
                Debug.WriteLine($"[LVDT板卡] 调用 API: pxi4087_getLvdtRmsVol (CH{channelConfig.ChannelIndex})");
                LogChannelContext(channelConfig, "[LVDT板卡] 测量前通道上下文");

                // Some PXI drivers require the channel to be started before reading RMS values,
                // and a longer settle time after enabling excitation or switching mode. We'll:
                // 1) start the channel sampling,
                // 2) if internal excitation is used, read excitation RMS to confirm it exists,
                // 3) wait for stabilization (400ms),
                // 4) attempt getLvdtRmsVol with a few retries.
                try
                {
                    int startStatus = PXI4087Native.pxi4087_lvdtStart(_deviceHandle, channelConfig.ChannelIndex);
                    if (startStatus != 0)
                    {
                        Debug.WriteLine($"[LVDT板卡] 警告：pxi4087_lvdtStart 返回 {startStatus} for CH{channelConfig.ChannelIndex}");
                    }
                    else
                    {
                        Debug.WriteLine($"[LVDT板卡] 已启动通道采样 (pxi4087_lvdtStart) CH{channelConfig.ChannelIndex}");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[LVDT板卡] 调用 pxi4087_lvdtStart 异常: {ex.Message}");
                }

                // 如果使用内部激励，先读激励 RMS 以确认激励已生效
                if (channelConfig.UseInternalExcitation)
                {
                    try
                    {
                        double excRms = 0.0;
                        int excStatus = PXI4087Native.pxi4087_getLvdtExcSigRms(_deviceHandle, channelConfig.ChannelIndex, out excRms);
                        if (excStatus == 0)
                        {
                            Debug.WriteLine($"[LVDT板卡] Internal excitation Vrms={excRms:F6} (CH{channelConfig.ChannelIndex})");
                        }
                        else
                        {
                            Debug.WriteLine($"[LVDT板卡] 读取内部激励 Vrms 返回 {excStatus} (CH{channelConfig.ChannelIndex})");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[LVDT板卡] 调用 pxi4087_getLvdtExcSigRms 异常: {ex.Message}");
                    }
                }

                // 等待硬件/激励稳定（延长为400ms）
                await Task.Delay(400);

                // 尝试多次读取 RMS（最多3次，间隔150ms）
                int maxAttempts = 3;
                status = -1;
                for (int attempt = 1; attempt <= maxAttempts; attempt++)
                {
                    Debug.WriteLine($"[LVDT板卡] 尝试读取 RMS (尝试 {attempt}/{maxAttempts}) CH{channelConfig.ChannelIndex}");
                    status = PXI4087Native.pxi4087_getLvdtRmsVol(_deviceHandle, channelConfig.ChannelIndex, out vaRms, out vbRms, out sumRatio);
                    if (status == 0) break;
                    Debug.WriteLine($"[LVDT板卡] pxi4087_getLvdtRmsVol 返回 {status} (CH{channelConfig.ChannelIndex}), 等待并重试");
                    await Task.Delay(150);
                }
                if (status == 0)
                {
                    channelConfig.MeasuredVaVoltage = vaRms;
                    channelConfig.MeasuredVbVoltage = vbRms;
                    channelConfig.MeasuredVdiff = vaRms - vbRms;
                    channelConfig.MeasuredRatio = sumRatio;
                    ReMessageBox.Show($"通道 {channelConfig.ChannelIndex} 测量完成", "测量结果");
                    Debug.WriteLine($"[LVDT板卡] 通道 {channelConfig.ChannelIndex} 测量结果 - Va={vaRms:F6}, Vb={vbRms:F6}, Vdiff={(vaRms-vbRms):F6}, Ratio={sumRatio:F6}");
                    LogChannelContext(channelConfig, "[LVDT板卡] 测量后通道上下文");
                }
                else
                {
                    ReMessageBox.Show($"通道 {channelConfig.ChannelIndex} 测量失败，状态码: {status}", "错误");
                    Debug.WriteLine($"[LVDT板卡] 通道 {channelConfig.ChannelIndex} 测量失败 - 状态码: {status}");
                    Debug.WriteLine($"[LVDT板卡] API: pxi4087_getLvdtRmsVol returned {status} for CH{channelConfig.ChannelIndex}");
                    LogChannelContext(channelConfig, "[LVDT板卡] 测量失败时通道上下文");
                }
                // 尝试停止通道采样（若驱动需要显式停止）
                try
                {
                    int stopStatus = PXI4087Native.pxi4087_lvdtStop(_deviceHandle, channelConfig.ChannelIndex);
                    if (stopStatus != 0)
                    {
                        Debug.WriteLine($"[LVDT板卡] 警告：pxi4087_lvdtStop 返回 {stopStatus} for CH{channelConfig.ChannelIndex}");
                    }
                    else
                    {
                        Debug.WriteLine($"[LVDT板卡] 已停止通道采样 (pxi4087_lvdtStop) CH{channelConfig.ChannelIndex}");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[LVDT板卡] 调用 pxi4087_lvdtStop 异常: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                ReMessageBox.Show($"通道测量时发生错误: {ex.Message}", "错误");
                Debug.WriteLine($"[LVDT板卡] 通道测量异常 - {ex.Message}");
            }
        }

        private async Task OnStopChannelMeasurementAsync(LvdtChannelConfigViewModel channelConfig)
        {
            if (channelConfig == null) return;
            try
            {
                Debug.WriteLine($"[LVDT板卡] 请求停止通道测量 - CH{channelConfig.ChannelIndex}");
                IsMeasuringEnabled = false;
                ReMessageBox.Show($"通道 {channelConfig.ChannelIndex} 停止测量", "成功");
                Debug.WriteLine($"[LVDT板卡] 通道 {channelConfig.ChannelIndex} 测量已停止");
            }
            catch (Exception ex)
            {
                ReMessageBox.Show($"停止通道测量时发生错误: {ex.Message}", "错误");
                Debug.WriteLine($"[LVDT板卡] 停止通道测量异常 - {ex.Message}");
            }
        }

        private async Task OnReadExternalExcitationAsync(LvdtChannelConfigViewModel channelConfig)
        {
            if (channelConfig == null) return;
            if (!IsConnected || _deviceHandle == UIntPtr.Zero)
            {
                ReMessageBox.Show("设备未连接！", "错误");
                return;
            }

            Debug.WriteLine($"[LVDT板卡] 读取外部激励 - 通道:{channelConfig.ChannelIndex}");

            try
            {
                // 为避免“读到的不是当前接线/当前通道的外部激励”，先显式把通道配置为外部激励，并应用激励通道选择标志。
                // （驱动文档：excSelCh0flag=1 选择 CH0 外部激励作为激励参考；=0 选择本通道外部激励）
                try
                {
                    int modeStatus = PXI4087Native.pxi4087_setMode(
                        _deviceHandle,
                        channelConfig.ChannelIndex,
                        (ushort)PXI4087Constants.pxi4087_Ch_Mode_Test,
                        (ushort)PXI4087Constants.pxi4087_Ch_Exc_Sour_Ext,
                        0,
                        0);
                    Debug.WriteLine($"[LVDT板卡] ReadExternalExcitation: setMode(Test,Ext) CH{channelConfig.ChannelIndex} returned {modeStatus}");

                    int selExcStatus = PXI4087Native.pxi4087_setSelExcCh0Flag(
                        _deviceHandle,
                        channelConfig.ChannelIndex,
                        0);
                    Debug.WriteLine($"[LVDT板卡] ReadExternalExcitation: setSelExcCh0Flag CH{channelConfig.ChannelIndex} flag=0 returned {selExcStatus}");

                    // 启动一次采样并等待稳定，有助于某些驱动正确锁定频率/有效值
                    int startStatus = PXI4087Native.pxi4087_lvdtStart(_deviceHandle, channelConfig.ChannelIndex);
                    Debug.WriteLine($"[LVDT板卡] ReadExternalExcitation: lvdtStart CH{channelConfig.ChannelIndex} returned {startStatus}");
                    await Task.Delay(200);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[LVDT板卡] ReadExternalExcitation 预配置异常: {ex.Message}");
                }

                double excRms = 0.0;
                int status = PXI4087Native.pxi4087_getLvdtExcSigRms(_deviceHandle, channelConfig.ChannelIndex, out excRms);
                if (status == 0)
                {
                    channelConfig.MeasuredExcitationVoltage = excRms;
                    Debug.WriteLine($"[LVDT板卡] 通道 {channelConfig.ChannelIndex} 外部激励 Vrms={excRms:F6}");
                }
                else
                {
                    ReMessageBox.Show($"读取外部激励有效值失败，状态码: {status}", "错误");
                    Debug.WriteLine($"[LVDT板卡] API: pxi4087_getLvdtExcSigRms returned {status} for CH{channelConfig.ChannelIndex}");
                    LogChannelContext(channelConfig, "[LVDT板卡] 读取外部激励失败时通道上下文");
                }

                double excFreq = 0.0;
                status = PXI4087Native.pxi4087_getLvdtExcSigFreq(_deviceHandle, channelConfig.ChannelIndex, out excFreq);
                if (status == 0)
                {
                    channelConfig.MeasuredExcitationFrequency = excFreq;
                    Debug.WriteLine($"[LVDT板卡] 通道 {channelConfig.ChannelIndex} 外部激励 Freq={excFreq:F1}Hz");
                }
                else
                {
                    ReMessageBox.Show($"读取外部激励频率失败，状态码: {status}", "错误");
                    Debug.WriteLine($"[LVDT板卡] API: pxi4087_getLvdtExcSigFreq returned {status} for CH{channelConfig.ChannelIndex}");
                    LogChannelContext(channelConfig, "[LVDT板卡] 读取外部激励频率失败时通道上下文");
                }

                // 尝试停止通道采样（避免影响后续仿真输出/采集状态）
                try
                {
                    int stopStatus = PXI4087Native.pxi4087_lvdtStop(_deviceHandle, channelConfig.ChannelIndex);
                    Debug.WriteLine($"[LVDT板卡] ReadExternalExcitation: lvdtStop CH{channelConfig.ChannelIndex} returned {stopStatus}");
                }
                catch { }
            }
            catch (Exception ex)
            {
                ReMessageBox.Show($"读取外部激励时发生错误: {ex.Message}", "错误");
                Debug.WriteLine($"[LVDT板卡] 读取外部激励异常 - {ex.Message}");
            }
        }


        private async Task OnApplyChannelConfigAsync(LvdtChannelConfigViewModel channelConfig)
        {
            if (channelConfig == null) return;

            // 始终保存绑定的数据到内存（UI 已与 channelConfig 双向绑定）
            Debug.WriteLine($"[LVDT板卡] 已保存通道 {channelConfig.ChannelIndex} 本地配置 (IsEnabled={channelConfig.IsEnabled})");

            // 如果已连接则下发到硬件，否则仅提示已保存（便于离线配置）
            if (_deviceHandle != UIntPtr.Zero && IsConnected)
            {
                int status = await ApplyLvdtChannelOutputAsync(channelConfig);
                if (status == 0)
                {
                    // 动态输出模式下，点频/波形长度/波形个数需要一并下发
                    if (channelConfig.IsDynamicOutput)
                    {
                        int dynStatus = ApplyDynamicBufferConfig(channelConfig);
                        if (dynStatus != 0)
                        {
                            ReMessageBox.Show($"通道 {channelConfig.ChannelIndex} 动态参数下发失败，状态码: {dynStatus}", "警告");
                        }
                        else
                        {
                            // 为确保立即生效：如果正在输出则重启该通道
                            if (IsOutputRunning)
                            {
                                try
                                {
                                    PXI4087Native.pxi4087_lvdtStop(_deviceHandle, channelConfig.ChannelIndex);
                                    PXI4087Native.pxi4087_lvdtStart(_deviceHandle, channelConfig.ChannelIndex);
                                }
                                catch { }
                            }
                        }
                    }

                    ReMessageBox.Show($"通道 {channelConfig.ChannelIndex} 配置应用成功", "成功");
                    Debug.WriteLine($"[LVDT板卡] 通道 {channelConfig.ChannelIndex} 配置下发成功");
                }
                else
                {
                    ReMessageBox.Show($"通道 {channelConfig.ChannelIndex} 配置应用失败，状态码: {status}", "错误");
                    Debug.WriteLine($"[LVDT板卡] 通道 {channelConfig.ChannelIndex} 配置下发失败 - 状态码: {status}");
                }
            }
            else
            {
                // 未连接，仅在UI/内存中生效
                ReMessageBox.Show($"通道 {channelConfig.ChannelIndex} 配置已保存（未连接，稍后连接时生效）", "信息");
            }
        }

        private async Task OnStartMeasurementAsync()
        {
            if (!IsConnected)
            {
                ReMessageBox.Show("设备未连接！", "错误");
                return;
            }

            try
            {
                // cancel previous if any
                _measCts?.Cancel();
                _measCts?.Dispose();
                _measCts = new System.Threading.CancellationTokenSource();

                IsMeasuringEnabled = true;
                // start background polling loop (non-blocking)
                _ = Task.Run(() => MeasurementLoopAsync(_measCts.Token));
                ReMessageBox.Show("测量功能已启用", "成功");
            }
            catch (Exception ex)
            {
                ReMessageBox.Show($"启用测量功能时发生错误: {ex.Message}", "错误");
            }
        }

        private async Task OnStopMeasurementAsync()
        {
            try
            {
                // cancel polling
                if (_measCts != null)
                {
                    _measCts.Cancel();
                    _measCts.Dispose();
                    _measCts = null;
                }

                IsMeasuringEnabled = false;
                ReMessageBox.Show("测量功能已停止", "成功");
            }
            catch (Exception ex)
            {
                ReMessageBox.Show($"停止测量功能时发生错误: {ex.Message}", "错误");
            }
        }

        private async Task OnMeasureExcitationSignalAsync(bool showMessage = true)
        {
            if (!IsConnected || _deviceHandle == UIntPtr.Zero)
            {
                if (showMessage) ReMessageBox.Show("设备未连接！", "错误");
                return;
            }

            try
            {
                // 测量激励信号RMS值
                double excitationRms = 0.0;
                ushort excCh = (SelectedChannel != null) ? SelectedChannel.ChannelIndex : (ushort)0;
                int status = PXI4087Native.pxi4087_getLvdtExcSigRms(_deviceHandle, excCh, out excitationRms);
                if (status != 0)
                {
                    if (showMessage) ReMessageBox.Show($"测量激励电压失败，状态码: {status}", "错误");
                    return;
                }
                MeasuredExcitationVoltage = excitationRms;

                // 测量激励信号频率
                double excitationFreq = 0.0;
                status = PXI4087Native.pxi4087_getLvdtExcSigFreq(_deviceHandle, excCh, out excitationFreq);
                if (status != 0)
                {
                    if (showMessage) ReMessageBox.Show($"测量激励频率失败，状态码: {status}", "错误");
                    return;
                }
                MeasuredExcitationFrequency = excitationFreq;

                // 如果有选中的通道，测量输出信号
                if (SelectedChannel != null)
                {
                    double vaRms = 0.0, vbRms = 0.0, sumRatio = 0.0;
                    int measureStatus = PXI4087Native.pxi4087_getLvdtRmsVol(_deviceHandle, SelectedChannel.ChannelIndex, out vaRms, out vbRms, out sumRatio);
                    if (measureStatus == 0)
                    {
                        SelectedChannel.MeasuredVaVoltage = vaRms;
                        SelectedChannel.MeasuredVbVoltage = vbRms;
                        SelectedChannel.MeasuredRatio = sumRatio;
                    }
                }

                // DEBUG: 激励信号测量结果
                Debug.WriteLine($"[LVDT板卡] 激励信号测量完成 - 电压: {MeasuredExcitationVoltage:F3} Vrms, 频率: {MeasuredExcitationFrequency:F1} Hz");

                if (SelectedChannel != null)
                {
                    Debug.WriteLine($"[LVDT板卡] 通道 {SelectedChannel.ChannelIndex} 输出测量 - Va: {SelectedChannel.MeasuredVaVoltage:F3}V, Vb: {SelectedChannel.MeasuredVbVoltage:F3}V, 比值: {SelectedChannel.MeasuredRatio:F6}");
                }

                if (showMessage) ReMessageBox.Show($"激励信号测量完成\n电压: {MeasuredExcitationVoltage:F3} Vrms\n频率: {MeasuredExcitationFrequency:F1} Hz", "测量结果");
            }
            catch (Exception ex)
            {
                if (showMessage) ReMessageBox.Show($"测量激励信号时发生错误: {ex.Message}", "错误");
            }
        }

        /// <summary>
        /// 后台周期性测量激励信号循环
        /// </summary>
        private async Task MeasurementLoopAsync(System.Threading.CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    await OnMeasureExcitationSignalAsync(false);
                    await Task.Delay(500, token); // 500ms 周期，可按需调整
                }
            }
            catch (OperationCanceledException)
            {
                // 正常取消
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LVDT板卡] MeasurementLoop 异常: {ex.Message}");
            }
        }

        private async Task OnResetDeviceAsync()
        {
            if (!IsConnected || _deviceHandle == UIntPtr.Zero)
            {
                ReMessageBox.Show("设备未连接！", "错误");
                return;
            }

            try
            {
                // 先停止所有输出
                if (IsOutputRunning)
                {
                    await OnStopOutputAsync();
                }

                // 重置设备到初始状态
                int status = PXI4087Native.pxi4087_reset(_deviceHandle);
                if (status != 0)
                {
                    ReMessageBox.Show($"设备重置失败，状态码: {status}", "错误");
                    return;
                }

                // 清除测量数据
                MeasuredExcitationVoltage = 0.0;
                MeasuredExcitationFrequency = 0.0;
                foreach (var channel in ChannelConfigs)
                {
                    channel.MeasuredVaVoltage = 0.0;
                    channel.MeasuredVbVoltage = 0.0;
                    channel.MeasuredRatio = 0.0;
                }

                ReMessageBox.Show("设备已重置到初始状态", "成功");
            }
            catch (Exception ex)
            {
                ReMessageBox.Show($"重置设备时发生错误: {ex.Message}", "错误");
            }
        }

        private async Task OnSaveCalibrationAsync()
        {
            if (!IsConnected || _deviceHandle == UIntPtr.Zero)
            {
                ReMessageBox.Show("设备未连接！", "错误");
                return;
            }

            try
            {
                // 保存校准数据到设备
                int status = PXI4087Native.pxi4087_saveUserGainBaisToISF(
                    _deviceHandle,
                    0,  // 通道索引
                    (ushort)CalibrationGroupIndex,
                    CalibrationScaleA,
                    CalibrationScaleB,
                    CalibrationScaleC
                );

                if (status != 0)
                {
                    ReMessageBox.Show($"保存校准数据失败，状态码: {status}", "错误");
                    return;
                }

                ReMessageBox.Show($"校准数据已保存到组{CalibrationGroupIndex}", "成功");
            }
            catch (Exception ex)
            {
                ReMessageBox.Show($"保存校准数据时发生错误: {ex.Message}", "错误");
            }
        }

        private async Task OnLoadCalibrationAsync()
        {
            if (!IsConnected || _deviceHandle == UIntPtr.Zero)
            {
                ReMessageBox.Show("设备未连接！", "错误");
                return;
            }

            try
            {
                // 从设备加载校准数据
                double scaleA = 0.0, scaleB = 0.0, scaleC = 0.0;
                int status = PXI4087Native.pxi4087_readUserGainBaisFromISF(
                    _deviceHandle,
                    0,  // 通道索引
                    (ushort)CalibrationGroupIndex,
                    out scaleA,
                    out scaleB,
                    out scaleC
                );

                if (status != 0)
                {
                    ReMessageBox.Show($"加载校准数据失败，状态码: {status}", "错误");
                    return;
                }

                // 更新UI显示
                CalibrationScaleA = scaleA;
                CalibrationScaleB = scaleB;
                CalibrationScaleC = scaleC;

                ReMessageBox.Show($"已从组{CalibrationGroupIndex}加载校准数据\nA: {scaleA:F6}\nB: {scaleB:F6}\nC: {scaleC:F6}", "校准数据加载成功");
            }
            catch (Exception ex)
            {
                ReMessageBox.Show($"加载校准数据时发生错误: {ex.Message}", "错误");
            }
        }

        public void Dispose()
        {
            if (IsConnected)
            {
                // stop background tasks
                try
                {
                    if (_waveformCts != null)
                    {
                        _waveformCts.Cancel();
                        _waveformCts.Dispose();
                        _waveformCts = null;
                    }
                    if (_measCts != null)
                    {
                        _measCts.Cancel();
                        _measCts.Dispose();
                        _measCts = null;
                    }
                }
                catch { }

                // 如果需要保持后台运行，只停止输出但不关闭设备
                if (KeepRunningInBackground)
                {
                    Debug.WriteLine($"[LVDT板卡] {DeviceName} 保持后台运行 - 保持输出与设备连接");
                    try
                    {
                        // 保存设备句柄到后台运行设备集合
                        int slotIndex = -1;
                        if (Device is MeasureControl.Models.Devices.DeviceCategories.PxiDeviceBase pxiDevice)
                        {
                            slotIndex = pxiDevice.SlotIndex;
                        }

                        string deviceKey = !string.IsNullOrEmpty(Device?.Id)
                            ? $"{Device.Id}_Slot{slotIndex}"
                            : $"{CardModel}_Slot{slotIndex}";
                        _backgroundRunningDevices[deviceKey] = _deviceHandle;
                        _backgroundRunningDeviceIds[deviceKey] = _currentAllocatedId;
                        _backgroundRunningOutputStates[deviceKey] = IsOutputRunning;
                        Debug.WriteLine($"[LVDT板卡] 设备 {deviceKey} 已保存到后台运行集合");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[LVDT板卡] 停止输出时发生错误: {ex.Message}");
                    }
                }
                else
                {
                    // 正常关闭设备
                OnCloseDeviceAsync().Wait();
                }
            }
        }
    }


    /// <summary>
    /// LVDT通道配置ViewModel
    /// </summary>
    public class LvdtChannelConfigViewModel : BindableBase
    {
        private ushort _channelIndex;
        private string _channelName;
        private bool _isEnabled;
        private string _workMode;  // TestInput, SimulateOutput
        private string _sensorType = "LVDT";  // LVDT or RVDT
        private string _outputMode;  // Position, VaVb, SumDiff
        private double _position;  // -1.0 to 1.0
        private double _vaVoltage;
        private double _vbVoltage;
        private double _vsum;
        private double _vdiff;

        // 测量数据
        private double _measuredVaVoltage;
        private double _measuredVbVoltage;
        private double _measuredRatio;
        private double _measuredVdiff;
        // ... existing fields ...

        // 校准参数
        private double _calibrationScaleA = 1.0;  // 校准参数A
        private double _calibrationScaleB = 0.0;  // 校准参数B
        private double _calibrationScaleC = 0.0;  // 校准参数C
        private int _calibrationGroupIndex = 0;  // 校准组索引 (0-7)
        // per-channel excitation and advanced config
        private bool _useInternalExcitation = true;
        private double _excitationVoltage = 7.0;
        private double _excitationFrequency = 3300.0;
        private double _transmissionRatio = 1.0;
        private double _phaseDelay = 0.0;
        private int _adcRangeIndex = 3;
        // dynamic output and delay
        private bool _isDynamicOutput = false;
        private double _dynamicStartPosition = -1.0;
        private double _dynamicEndPosition = 1.0;
        private double _dynamicPointFreq = 1.0;
        private int _dynamicWaveformLength = 100;
        private int _dynamicOutputCount = 0;
        private bool _goBackOutput = false; // 往返输出（用于自动加载波形时的goBackFlag）
        private bool _useResolverAngleOutput = false; // 是否使用旋变角度输出（动态模式下）
        // measured excitation (per-channel)
        private double _measuredExcitationVoltage;
        private double _measuredExcitationFrequency;

        // waveform output settings
        private int _dataOutputMode = 0; // 0=Position mode, 1=Buffer mode
        private double _scanFrequency = 1000.0; // Hz
        private double _scanPeriod = 0.001; // seconds
        private int _waveformLength = 100; // number of points
        private int _waveformOutputCount = 1; // 0=continuous, >0=finite count
        private double[] _waveformData; // position values for waveform

        // RVDT/Resolver settings
        private double _resolverPhaseDiff = 0.0; // degrees
        private double _resolverOutputAngle = 0.0; // degrees
        private double _resolverMotorSpeed = 0.0; // speed setting
        private double[] _resolverWaveformData; // angle values for resolver waveform
        private bool _autoLoadResolverWave = false; // auto load waveform
        private int _resolverWaveformLength = 100;
        private double _resolverStartAngle = 0.0;
        private double _resolverEndAngle = 360.0;

        // excitation channel selection
        private bool _useExcCh0Flag = false; // excitation channel 0 flag

        public ushort ChannelIndex
        {
            get => _channelIndex;
            set => SetProperty(ref _channelIndex, value);
        }

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

        public string WorkMode
        {
            get => _workMode;
            set => SetProperty(ref _workMode, value);
        }

        public string SensorType
        {
            get => _sensorType;
            set => SetProperty(ref _sensorType, value);
        }

        public string OutputMode
        {
            get => _outputMode;
            set => SetProperty(ref _outputMode, value);
        }

        public double Position
        {
            get => _position;
            set => SetProperty(ref _position, Math.Max(-1.0, Math.Min(1.0, value)));
        }

        public double VaVoltage
        {
            get => _vaVoltage;
            set => SetProperty(ref _vaVoltage, value);
        }

        public double VbVoltage
        {
            get => _vbVoltage;
            set => SetProperty(ref _vbVoltage, value);
        }

        public double Vsum
        {
            get => _vsum;
            set => SetProperty(ref _vsum, value);
        }

        public double Vdiff
        {
            get => _vdiff;
            set => SetProperty(ref _vdiff, value);
        }

        // 测量数据属性
        public double MeasuredVaVoltage
        {
            get => _measuredVaVoltage;
            set => SetProperty(ref _measuredVaVoltage, value);
        }

        public double MeasuredVbVoltage
        {
            get => _measuredVbVoltage;
            set => SetProperty(ref _measuredVbVoltage, value);
        }

        public double MeasuredVdiff
        {
            get => _measuredVdiff;
            set => SetProperty(ref _measuredVdiff, value);
        }

        public double MeasuredRatio
        {
            get => _measuredRatio;
            set => SetProperty(ref _measuredRatio, value);
        }

        // 波形输出参数属性
        

        // 校准参数属性
        public double CalibrationScaleA
        {
            get => _calibrationScaleA;
            set => SetProperty(ref _calibrationScaleA, value);
        }

        public double CalibrationScaleB
        {
            get => _calibrationScaleB;
            set => SetProperty(ref _calibrationScaleB, value);
        }

        public double CalibrationScaleC
        {
            get => _calibrationScaleC;
            set => SetProperty(ref _calibrationScaleC, value);
        }

        public int CalibrationGroupIndex
        {
            get => _calibrationGroupIndex;
            set => SetProperty(ref _calibrationGroupIndex, Math.Max(0, Math.Min(7, value)));
        }
        // per-channel properties
        public bool UseInternalExcitation
        {
            get => _useInternalExcitation;
            set => SetProperty(ref _useInternalExcitation, value);
        }

        public double ExcitationVoltage
        {
            get => _excitationVoltage;
            set => SetProperty(ref _excitationVoltage, value);
        }

        public double ExcitationFrequency
        {
            get => _excitationFrequency;
            set => SetProperty(ref _excitationFrequency, value);
        }

        public double TransmissionRatio
        {
            get => _transmissionRatio;
            set => SetProperty(ref _transmissionRatio, Math.Max(0.0001, Math.Min(1000.0, value)));
        }

        public double PhaseDelay
        {
            get => _phaseDelay;
            set => SetProperty(ref _phaseDelay, Math.Max(0.0, Math.Min(65535.0, value)));
        }

        public int AdcRangeIndex
        {
            get => _adcRangeIndex;
            set => SetProperty(ref _adcRangeIndex, Math.Max(0, Math.Min(3, value)));
        }
        
        public bool IsDynamicOutput
        {
            get => _isDynamicOutput;
            set => SetProperty(ref _isDynamicOutput, value);
        }

        public double DynamicStartPosition
        {
            get => _dynamicStartPosition;
            set
            {
                double v = Math.Max(-1.0, Math.Min(1.0, value));
                if (SetProperty(ref _dynamicStartPosition, v))
                {
                    WaveformData = null;
                }
            }
        }

        public double DynamicEndPosition
        {
            get => _dynamicEndPosition;
            set
            {
                double v = Math.Max(-1.0, Math.Min(1.0, value));
                if (SetProperty(ref _dynamicEndPosition, v))
                {
                    WaveformData = null;
                }
            }
        }

        public double DynamicPointFreq
        {
            get => _dynamicPointFreq;
            set
            {
                double v = Math.Max(0.0001, value);
                if (SetProperty(ref _dynamicPointFreq, v))
                {
                    RaisePropertyChanged(nameof(DynamicPointPeriod));
                }
            }
        }

        public int DynamicWaveformLength
        {
            get => _dynamicWaveformLength;
            set
            {
                int v = Math.Max(1, Math.Min(2048, value));
                if (SetProperty(ref _dynamicWaveformLength, v))
                {
                    WaveformData = null;
                }
            }
        }

        public int DynamicOutputCount
        {
            get => _dynamicOutputCount;
            set
            {
                int v = Math.Max(0, value);
                if (SetProperty(ref _dynamicOutputCount, v))
                {
                    RaisePropertyChanged(nameof(WaveformOutputCount));
                }
            }
        }

        /// <summary>
        /// 往返输出（用于自动加载旋变波形时的 goBackFlag）
        /// </summary>
        public bool GoBackOutput
        {
            get => _goBackOutput;
            set => SetProperty(ref _goBackOutput, value);
        }

        /// <summary>
        /// 是否在动态输出中使用旋变角度输出（勾选后使用 ResolverWaveformData/角度相关字段）
        /// </summary>
        public bool UseResolverAngleOutput
        {
            get => _useResolverAngleOutput;
            set => SetProperty(ref _useResolverAngleOutput, value);
        }

        /// <summary>
        /// 每点时间间隔 (s)，与 DynamicPointFreq 互为倒数（在界面显示为时间间隔）
        /// </summary>
        public double DynamicPointPeriod
        {
            get => _dynamicPointFreq <= 0 ? 0.0 : 1.0 / _dynamicPointFreq;
            set
            {
                double newFreq = Math.Max(0.0001, 1.0 / Math.Max(1e-9, value));
                if (Math.Abs(_dynamicPointFreq - newFreq) > 1e-12)
                {
                    SetProperty(ref _dynamicPointFreq, newFreq);
                    RaisePropertyChanged(nameof(DynamicPointPeriod));
                }
            }
        }

        public double MeasuredExcitationVoltage
        {
            get => _measuredExcitationVoltage;
            set => SetProperty(ref _measuredExcitationVoltage, value);
        }

        public double MeasuredExcitationFrequency
        {
            get => _measuredExcitationFrequency;
            set => SetProperty(ref _measuredExcitationFrequency, value);
        }

        // Waveform output properties
        public int DataOutputMode
        {
            get => _dataOutputMode;
            set => SetProperty(ref _dataOutputMode, Math.Max(0, Math.Min(1, value)));
        }

        public double ScanFrequency
        {
            get => _scanFrequency;
            set => SetProperty(ref _scanFrequency, Math.Max(0.1, Math.Min(20000.0, value)));
        }

        public double ScanPeriod
        {
            get => _scanPeriod;
            set => SetProperty(ref _scanPeriod, Math.Max(0.00005, Math.Min(10.0, value)));
        }

        public int WaveformLength
        {
            get => _waveformLength;
            set => SetProperty(ref _waveformLength, Math.Max(1, Math.Min(2048, value)));
        }

        public int WaveformOutputCount
        {
            get => _waveformOutputCount;
            set => SetProperty(ref _waveformOutputCount, Math.Max(0, value));
        }

        public double[] WaveformData
        {
            get => _waveformData;
            set => SetProperty(ref _waveformData, value);
        }

        // RVDT/Resolver properties
        public double ResolverPhaseDiff
        {
            get => _resolverPhaseDiff;
            set => SetProperty(ref _resolverPhaseDiff, Math.Max(-180.0, Math.Min(180.0, value)));
        }

        public double ResolverOutputAngle
        {
            get => _resolverOutputAngle;
            set => SetProperty(ref _resolverOutputAngle, Math.Max(-90.0, Math.Min(90.0, value)));
        }

        /// <summary>
        /// 根据角度计算对应的RVDT位置值 (用于4087A硬件)
        /// </summary>
        public double ResolverAngleToPosition => ResolverOutputAngle / 90.0;

        public double ResolverMotorSpeed
        {
            get => _resolverMotorSpeed;
            set => SetProperty(ref _resolverMotorSpeed, Math.Max(0.0, Math.Min(10000.0, value)));
        }

        public double[] ResolverWaveformData
        {
            get => _resolverWaveformData;
            set => SetProperty(ref _resolverWaveformData, value);
        }

        public bool AutoLoadResolverWave
        {
            get => _autoLoadResolverWave;
            set => SetProperty(ref _autoLoadResolverWave, value);
        }

        public int ResolverWaveformLength
        {
            get => _resolverWaveformLength;
            set => SetProperty(ref _resolverWaveformLength, Math.Max(1, Math.Min(2048, value)));
        }

        public double ResolverStartAngle
        {
            get => _resolverStartAngle;
            set => SetProperty(ref _resolverStartAngle, Math.Max(0.0, Math.Min(360.0, value)));
        }

        public double ResolverEndAngle
        {
            get => _resolverEndAngle;
            set => SetProperty(ref _resolverEndAngle, Math.Max(0.0, Math.Min(360.0, value)));
        }

        // Excitation channel properties
        public bool UseExcCh0Flag
        {
            get => _useExcCh0Flag;
            set => SetProperty(ref _useExcCh0Flag, false);
        }
        
        // UI controls per-channel
        private bool _isExpanded;
        private bool _vaInverse;
        private bool _vbInverse;

        public bool IsExpanded
        {
            get => _isExpanded;
            set => SetProperty(ref _isExpanded, value);
        }

        public bool VaInverse
        {
            get => _vaInverse;
            set => SetProperty(ref _vaInverse, value);
        }

        public bool VbInverse
        {
            get => _vbInverse;
            set => SetProperty(ref _vbInverse, value);
        }
    }
}

