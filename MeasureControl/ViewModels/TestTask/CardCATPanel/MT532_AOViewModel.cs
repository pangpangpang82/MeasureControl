using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media;
using MeasureControl.Constants;
using MeasureControl.Drivers;
using MeasureControl.Models;
using MeasureControl.Models.Devices;
using MeasureControl.Services;
using MeasureControl.Events;
using Prism.Commands;
using Prism.Mvvm;
using Prism.Events;
using Prism.Regions;
using MeasureControl.Views;
using MeasureControl.Helpers;
using MeasureControl.ViewModels.TestTask.CardCATPanel;
using MeasureControl.Views.Dialogs;
using Prism.Ioc;
using System.Windows.Threading;

namespace MeasureControl.ViewModels.TestTask.CardCATPanel
{
    /// <summary>
    /// MT-X532 模拟量输出配置面板的 ViewModel，负责通道使能/波形参数、测试任务配置切换以及驱动连接控制。
    /// </summary>
    public class MT532_AOViewModel : BindableBase, IDisposable, ICloseGuard, IConfirmNavigationRequest
    {
        /// <summary>
        /// 波形参数快照类，用于跟踪参数变化和版本控制
        /// </summary>
        private class WaveformParameters
        {
            public OutputWaveformType WaveformType { get; set; }
            public double Amplitude { get; set; }
            public double Frequency { get; set; }
            public double Offset { get; set; }
            public double DutyCycle { get; set; }
            public int Version { get; set; }

            public WaveformParameters Clone()
            {
                return new WaveformParameters
                {
                    WaveformType = this.WaveformType,
                    Amplitude = this.Amplitude,
                    Frequency = this.Frequency,
                    Offset = this.Offset,
                    DutyCycle = this.DutyCycle,
                    Version = this.Version
                };
            }

            public bool HasChanged(WaveformParameters other)
            {
                if (other == null) return true;
                return WaveformType != other.WaveformType ||
                       Amplitude != other.Amplitude ||
                       Frequency != other.Frequency ||
                       Offset != other.Offset ||
                       DutyCycle != other.DutyCycle;
            }
        }
        private readonly IPxiChassisService _pxiChassisService;
        private readonly IEventAggregator _eventAggregator;
        private readonly ProjectService _projectService;
        private SubscriptionToken _projectModifiedToken;

        private DeviceBase _device;
        private string _chassisName;
        private string _cardModel;
        private string _cardName;
        private ObservableCollection<ChannelInfo> _channels;
        private ObservableCollection<AnalogOutputChannelConfigViewModel> _outputChannelConfigs;
        private double _sampleRate;

        // 设备状态
        private bool _isDeviceConnected; // 在线/离线
        private bool _isBusy; // 连接中/断开中
        private bool _isOutputRunning; // 输出运行中

        // 配置锁定
        private bool _isConfigurationLocked;

        private string _connectionStatus; // 设备连接状态字
        private bool _ownsDriverLifecycle;
      
        private readonly ObservableCollection<string> _availableTestTasks = new ObservableCollection<string>();
        private string _selectedTestTask;
        private bool _hasPendingChanges;
        private bool _isApplyingTaskConfig;
        private bool _isLoadingTaskOptions;

        private bool _isValidatingChannelConfig;

        private readonly SemaphoreSlim _driverStateSyncLock = new SemaphoreSlim(1, 1);

        private IDeviceDriver _driver;

        private readonly HashSet<string> _usedPreviewColors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // 记录每个通道的最新配置，便于切换任务或启停预览时保持幅值/频率等状态
        private readonly Dictionary<string, AnalogOutputExtendedChannelConfig> _channelStateCache = new Dictionary<string, AnalogOutputExtendedChannelConfig>(StringComparer.OrdinalIgnoreCase);

        // 参数快照字典，用于跟踪参数变化和版本控制
        private readonly Dictionary<string, WaveformParameters> _parameterSnapshots = new Dictionary<string, WaveformParameters>(StringComparer.OrdinalIgnoreCase);

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

        public ObservableCollection<ChannelInfo> Channels
        {
            get => _channels;
            set => SetProperty(ref _channels, value);
        }

        public bool IsAllEnabled
        {
            get => Channels != null && Channels.Count > 0 && Channels.All(c => c.IsEnabled);
            set
            {
                if (Channels == null) return;
                foreach (var ch in Channels)
                {
                    SyncChannelStateFromChannelInfo(ch);
                    ch.IsEnabled = value;
                }
                RaisePropertyChanged();
            }
        }

        public bool IsAllPreviewEnabled
        {
            get => OutputChannelConfigs != null && OutputChannelConfigs.Count > 0 && OutputChannelConfigs.All(c => c.IsPreviewEnabled);
            set
            {
                if (OutputChannelConfigs == null) return;
                foreach (var config in OutputChannelConfigs)
                {
                    config.IsPreviewEnabled = value;
                }
                RaisePropertyChanged();
            }
        }

        public ObservableCollection<AnalogOutputChannelConfigViewModel> OutputChannelConfigs
        {
            get => _outputChannelConfigs;
            set => SetProperty(ref _outputChannelConfigs, value);
        }

        public double SampleRate
        {
            get => _sampleRate;
            set
            {
                if (SetProperty(ref _sampleRate, value) && !_isApplyingTaskConfig)
                {
                    MarkDirty();
                }
            }
        }

        public bool IsDeviceConnected
        {
            get => _isDeviceConnected;
            private set
            {
                if (SetProperty(ref _isDeviceConnected, value))
                {
                    UpdateConfigurationLock();
                    UpdateWaveformTypeLock();
                    RaisePropertyChanged(nameof(CanStartOutput));
                    RaisePropertyChanged(nameof(CanStopOutput));
                    RaisePropertyChanged(nameof(CanToggleOutput));
                }
            }
        }

        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (SetProperty(ref _isBusy, value))
                {
                    UpdateConfigurationLock();
                    RaisePropertyChanged(nameof(CanStartOutput));
                    RaisePropertyChanged(nameof(CanStopOutput));
                    RaisePropertyChanged(nameof(CanToggleOutput));
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
                    UpdateConfigurationLock();
                    UpdateWaveformTypeLock();
                    RaisePropertyChanged(nameof(CanStartOutput));
                    RaisePropertyChanged(nameof(CanStopOutput));
                    RaisePropertyChanged(nameof(CanToggleOutput));
                }
            }
        }

        public string ConnectionStatus
        {
            get => _connectionStatus;
            set => SetProperty(ref _connectionStatus, value);
        }

        public ObservableCollection<string> AvailableTestTasks => _availableTestTasks;

        public bool HasTestTaskOptions => AvailableTestTasks.Count > 0;

        public string SelectedTestTask
        {
            get => _selectedTestTask;
            set => ChangeSelectedTestTask(value);
        }

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

        /// <summary>
        /// 配置是否被锁定
        /// 当设备处于连接中、断开中或输出运行中时锁定配置
        /// </summary>
        public bool IsConfigurationLocked
        {
            get => _isConfigurationLocked;
            private set
            {
                if (SetProperty(ref _isConfigurationLocked, value))
                {
                    (SaveConfigCommand as DelegateCommand)?.RaiseCanExecuteChanged();
                    (ReloadConfigCommand as DelegateCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        /// <summary>
        /// 更新配置锁定状态：连接中、在线、输出中都锁定，只有离线状态才允许编辑
        /// </summary>
        private void UpdateConfigurationLock()
        {
            IsConfigurationLocked = IsBusy || IsDeviceConnected || IsOutputRunning;
        }

        private bool _isWaveformTypeLocked;
        /// <summary>
        /// 输出类型是否被锁定
        /// 当输出运行中时锁定输出类型
        /// </summary>
        public bool IsWaveformTypeLocked
        {
            get => _isWaveformTypeLocked;
            private set => SetProperty(ref _isWaveformTypeLocked, value);
        }

        /// <summary>
        /// 更新输出类型锁定状态：连接中、在线、输出中都锁定，只有离线且非输出状态才允许编辑
        /// </summary>
        private void UpdateWaveformTypeLock()
        {
            IsWaveformTypeLocked = IsBusy || IsDeviceConnected || IsOutputRunning;
        }

        public bool CanStartOutput => IsDeviceConnected && !IsBusy && !IsOutputRunning && HasValidOutputConfigs();
        public bool CanStopOutput => IsDeviceConnected && !IsBusy && IsOutputRunning;
        public bool CanToggleOutput => CanStartOutput || CanStopOutput;

        private bool CanNavigateToCalibration => !IsBusy && !IsOutputRunning;

        public ICommand SaveConfigCommand { get; }
        public ICommand ReloadConfigCommand { get; }
        public ICommand OpenDeviceCommand { get; }
        public ICommand CloseDeviceCommand { get; }
        public ICommand ToggleDeviceCommand { get; }
        public ICommand StartAcquisitionCommand { get; }
        public ICommand ClearDisplayCommand { get; }
        public ICommand ToggleOutputCommand { get; }
        public ICommand NavigateToCalibrationCommand => _navigateToCalibrationCommand;

        private readonly DelegateCommand _navigateToCalibrationCommand;

        public MT532_AOViewModel()
        {
            Channels = new ObservableCollection<ChannelInfo>();
            OutputChannelConfigs = new ObservableCollection<AnalogOutputChannelConfigViewModel>();
            SampleRate = 1000;
            _connectionStatus = "离线";
            _availableTestTasks.CollectionChanged += OnAvailableTestTasksChanged;
            _isConfigurationLocked = false;

            SaveConfigCommand = new DelegateCommand(
                    () => SaveCurrentTaskConfig(),
                    () => HasPendingChanges && !string.IsNullOrEmpty(SelectedTestTask) && HasTestTaskOptions && !IsConfigurationLocked)
                .ObservesProperty(() => HasPendingChanges)
                .ObservesProperty(() => IsConfigurationLocked)
                .ObservesProperty(() => SelectedTestTask);
            ReloadConfigCommand = new DelegateCommand(
                    () => ReloadCurrentTaskConfig(),
                    () => HasPendingChanges && !string.IsNullOrEmpty(SelectedTestTask) && HasTestTaskOptions && !IsConfigurationLocked)
                .ObservesProperty(() => HasPendingChanges)
                .ObservesProperty(() => IsConfigurationLocked)
                .ObservesProperty(() => SelectedTestTask);
            OpenDeviceCommand = new DelegateCommand(async () => await OnOpenDeviceAsync(), () => !IsDeviceConnected)
                .ObservesProperty(() => IsDeviceConnected);
            CloseDeviceCommand = new DelegateCommand(async () => await StopDebugAsync(), () => IsDeviceConnected)
                .ObservesProperty(() => IsDeviceConnected);
            ToggleDeviceCommand = new DelegateCommand(
                    async () =>
                    {
                        if (!IsDeviceConnected)
                        {
                            await OnOpenDeviceAsync();
                        }
                        else
                        {
                            await StopDebugAsync();
                        }
                    },
                    () => !IsBusy)
                .ObservesProperty(() => IsBusy);
            StartAcquisitionCommand = new DelegateCommand(async () => await OnStartOutputAsync(), () => CanStartOutput)
                .ObservesProperty(() => CanStartOutput);
            ClearDisplayCommand = new DelegateCommand(async () => await OnStopOutputAsync(), () => CanStopOutput)
                .ObservesProperty(() => CanStopOutput);
            ToggleOutputCommand = new DelegateCommand(
                    async () =>
                    {
                        if (IsOutputRunning)
                        {
                            await OnStopOutputAsync();
                        }
                        else
                        {
                            await OnStartOutputAsync();
                        }
                    },
                    () => CanToggleOutput)
                .ObservesProperty(() => IsOutputRunning)
                .ObservesProperty(() => IsDeviceConnected)
                .ObservesProperty(() => IsBusy);

            _navigateToCalibrationCommand = new DelegateCommand(OnNavigateToCalibration, () => CanNavigateToCalibration)
                .ObservesProperty(() => IsBusy)
                .ObservesProperty(() => IsDeviceConnected)
                .ObservesProperty(() => IsOutputRunning);
        }

        private void OnNavigateToCalibration()
        {
            if (Device == null)
                return;

            if (IsBusy)
            {
                return;
            }

            if (IsOutputRunning)
            {
                return;
            }

            try
            {
                var container = (System.Windows.Application.Current as App)?.Container;
                var regionManager = container?.Resolve(typeof(IRegionManager)) as IRegionManager;
                if (regionManager == null || !regionManager.Regions.ContainsRegionWithName(AppConstants.MainRegionName))
                {
                    ReMessageBox.Show("导航服务不可用，无法打开标定界面", "错误",
                        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                    return;
                }

                var navigationService = regionManager.Regions[AppConstants.MainRegionName].NavigationService;
                if (navigationService == null)
                {
                    ReMessageBox.Show("导航服务不可用，无法打开标定界面", "错误",
                        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                    return;
                }

                var navParams = new NavigationParameters
                {
                    { "ChassisName", ChassisName },
                    { "CardName", CardName ?? CardModel ?? "" },
                    { "ChannelName", null },
                    { "ChannelType", "AO" },
                    { "SignalName", null },
                    { "ConfigTabelName", null },
                    { "IsCalibrationNavigation", true }
                };

                navigationService.RequestNavigate(new Uri("PxiChassis", UriKind.Relative), navParams);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AnalogOutputConfig] 导航到标定页面失败: {ex.Message}");
                ReMessageBox.Show($"导航到标定页面失败: {ex.Message}", "错误",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        public MT532_AOViewModel(DeviceBase device, string chassisName,
            IPxiChassisService pxiChassisService = null,
            IEventAggregator eventAggregator = null,
            ProjectService projectService = null) : this()
        {
            Device = device;
            ChassisName = chassisName;
            CardModel = device?.Model ?? string.Empty;
            CardName = !string.IsNullOrEmpty(device?.CardName) ? device.CardName : device?.Model ?? string.Empty;
            _pxiChassisService = pxiChassisService;
            _eventAggregator = eventAggregator;
            _projectService = projectService;

            if (Device != null)
            {
                var cachedDriver = DriverFactory.GetCachedDriver(Device.Id);
                if (cachedDriver != null)
                {
                    _driver = cachedDriver;
                    if (_driver.IsConnected)
                    {
                        IsDeviceConnected = true;
                        ConnectionStatus = "在线";
                    }
                }
            }

            _projectModifiedToken = _eventAggregator?.GetEvent<ProjectModifiedEvent>()?.Subscribe(OnProjectModified);

            InitializeChannelsFromDevice();
            LoadCardMetadata();
            LoadTestTaskOptions();
        }

        private void InitializeChannelsFromDevice()
        {
            Channels.Clear();
            if (Device is AnalogOutputDevice aoDevice && aoDevice.AoNode != null)
            {
                var aoNode = aoDevice.AoNode;
                var slotPosition = aoNode.SlotPosition;

                var (startIndex, endIndex) = ParseSlotPosition(slotPosition, "AO");

                for (int i = startIndex; i <= endIndex; i++)
                {
                    Channels.Add(new ChannelInfo
                    {
                        ChannelName = $"AO{i}",
                        IsEnabled = false,

                        Range = "直流",
                        AvailableRanges = new ObservableCollection<string>
                        {
                            "直流",
                            "正弦",
                            "方波"
                        },
                        CurrentValue = "0.000",
                        Unit = "V",
                        Status = "正常"
                    });
                }

                _channelStateCache.Clear();
                foreach (var ch in Channels)
                {
                    SyncChannelStateFromChannelInfo(ch);
                    ch.PropertyChanged += (s, e) =>
                    {
                        if (e.PropertyName == nameof(ChannelInfo.IsEnabled))
                        {
                            RebuildOutputChannelConfigs();
                            MarkDirty();
                            RaisePropertyChanged(nameof(IsAllEnabled));
                            RaisePropertyChanged(nameof(CanStartOutput));
                            RaisePropertyChanged(nameof(CanToggleOutput));
                            SyncEnabledChannelsToDriverState();
                        }
                        else if (e.PropertyName == nameof(ChannelInfo.Range))
                        {
                            var cfgVm = OutputChannelConfigs.FirstOrDefault(c => c.ChannelName == ch.ChannelName);
                            if (cfgVm != null)
                            {
                                cfgVm.WaveformType = MapRangeToWaveform(ch.Range);
                                ValidateChannelConfig(cfgVm, nameof(AnalogOutputChannelConfigViewModel.WaveformType), showMessage: false);
                                UpdateChannelStateFromViewModel(cfgVm);
                                MarkDirty();
                            }
                            else
                            {
                                var state = GetOrCreateChannelState(ch.ChannelName);
                                state.Range = ch.Range;
                                state.WaveformType = MapRangeToWaveform(ch.Range);
                                MarkDirty();
                            }
                        }
                    };
                }
            }

            RebuildOutputChannelConfigs();
        }

        private void SyncEnabledChannelsToDriverState()
        {
            _ = SyncEnabledChannelsToDriverStateInternalAsync();
        }

        private Task SyncEnabledChannelsToDriverStateBeforeConnectAsync()
        {
            return SyncEnabledChannelsToDriverStateInternalAsync();
        }

        private async Task SyncEnabledChannelsToDriverStateInternalAsync()
        {
            if (_driver is not MTX532Driver)
            {
                return;
            }

            if (Channels == null || Channels.Count == 0)
            {
                return;
            }

            await _driverStateSyncLock.WaitAsync();
            try
            {
                foreach (var ch in Channels)
                {
                    if (ch == null || string.IsNullOrWhiteSpace(ch.ChannelName))
                    {
                        continue;
                    }

                    var dict = new Dictionary<string, object>
                    {
                        ["Enabled"] = ch.IsEnabled,
                        ["SampleRate"] = SampleRate
                    };

                    await _driver.ConfigureChannelAsync(ch.ChannelName, dict);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AO] 预同步通道使能状态失败: {ex.Message}");
            }
            finally
            {
                _driverStateSyncLock.Release();
            }
        }

        private void RebuildOutputChannelConfigs()
        {
            var previousConfigs = OutputChannelConfigs?.ToDictionary(c => c.ChannelName);
            if (previousConfigs != null)
            {
                foreach (var kv in previousConfigs)
                {
                    if (!(Channels.Any(ch => ch.ChannelName == kv.Key && ch.IsEnabled)) &&
                        !string.IsNullOrWhiteSpace(kv.Value.PreviewColorHex))
                    {
                        _usedPreviewColors.Remove(kv.Value.PreviewColorHex);
                    }
                }
            }

            OutputChannelConfigs.Clear();

            foreach (var ch in Channels)
            {
                var state = GetOrCreateChannelState(ch.ChannelName);
                var wasEnabled = state.IsEnabled;
                state.IsEnabled = ch.IsEnabled;
                state.Range = ch.Range;
                state.WaveformType = MapRangeToWaveform(ch.Range);

                if (!ch.IsEnabled)
                {
                    continue;
                }

                var vm = new AnalogOutputChannelConfigViewModel
                {
                    ChannelName = ch.ChannelName,
                    WaveformType = state.WaveformType,
                    AmplitudeText = FormatChannelValue(state.Amplitude, state.WaveformType == OutputWaveformType.Dc),
                    FrequencyText = FormatChannelValue(state.Frequency, state.WaveformType == OutputWaveformType.Dc),
                    OffsetText = state.Offset.ToString("G"),
                    DutyCycleText = state.WaveformType == OutputWaveformType.Square ? state.DutyCycle.ToString("G") : "50",
                    IsAmplitudeReadOnly = true,
                    IsFrequencyReadOnly = true,
                    IsOffsetReadOnly = false,
                    IsDutyCycleReadOnly = true,
                    IsPreviewEnabled = state.IsPreviewEnabled
                };

                if (vm.IsPreviewEnabled && !string.IsNullOrWhiteSpace(state.PreviewColorHex))
                {
                    vm.PreviewColorHex = state.PreviewColorHex;
                    _usedPreviewColors.Add(state.PreviewColorHex);
                }

                ValidateChannelConfig(vm, string.Empty, showMessage: false);

                if (vm.IsPreviewEnabled && string.IsNullOrWhiteSpace(vm.PreviewColorHex))
                {
                    UpdatePreviewColor(vm);
                }

                UpdateChannelStateFromViewModel(vm);

                vm.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(AnalogOutputChannelConfigViewModel.WaveformType))
                    {
                        if (IsOutputRunning)
                        {
                            // 正在输出时不允许切换波形类型，恢复旧值并提示
                            vm.WaveformType = MapRangeToWaveform(GetOrCreateChannelState(vm.ChannelName).Range);
                            ReMessageBox.Show("正在输出，无法切换波形类型。请先停止输出。", "提示", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                            return;
                        }
                        ValidateChannelConfig(vm, e.PropertyName, showMessage: false);
                        UpdateChannelStateFromViewModel(vm);
                        MarkDirty();
                        RaisePropertyChanged(nameof(CanStartOutput));
                        RaisePropertyChanged(nameof(CanToggleOutput));
                    }
                    else if (e.PropertyName == nameof(AnalogOutputChannelConfigViewModel.AmplitudeText) ||
                             e.PropertyName == nameof(AnalogOutputChannelConfigViewModel.FrequencyText) ||
                             e.PropertyName == nameof(AnalogOutputChannelConfigViewModel.OffsetText) ||
                             e.PropertyName == nameof(AnalogOutputChannelConfigViewModel.DutyCycleText))
                    {
                        ValidateChannelConfig(vm, e.PropertyName, showMessage: true);
                        UpdateChannelStateFromViewModel(vm);
                        MarkDirty();

                        // 如果正在输出，触发动态参数更新
                        if (IsOutputRunning)
                        {
                            UpdateOutputParameters(vm);
                        }

                        RaisePropertyChanged(nameof(CanStartOutput));
                        RaisePropertyChanged(nameof(CanToggleOutput));
                    }
                    else if (e.PropertyName == nameof(AnalogOutputChannelConfigViewModel.IsPreviewEnabled))
                    {
                        UpdatePreviewColor(vm);
                        UpdateChannelStateFromViewModel(vm);
                        MarkDirty();
                        RaisePropertyChanged(nameof(IsAllPreviewEnabled));
                    }
                };

                OutputChannelConfigs.Add(vm);
            }

            RaisePropertyChanged(nameof(CanStartOutput));
            RaisePropertyChanged(nameof(CanToggleOutput));
        }

        private string FormatChannelValue(double value, bool useDash)
        {
            if (useDash)
            {
                return "-";
            }
            return value.ToString("G");
        }

        /// <summary>
        /// 根据波形类型调整可编辑字段，规范缺省值，并限制幅值/偏置组合不超过 10V。
        /// </summary>
        private void ValidateChannelConfig(AnalogOutputChannelConfigViewModel vm, string changedPropertyName, bool showMessage)
        {
            if (_isValidatingChannelConfig)
            {
                return;
            }

            _isValidatingChannelConfig = true;
            try
            {
                vm.IsWaveformTypeReadOnly = IsOutputRunning || IsWaveformTypeLocked;

                string RoundFormat(double v, int decimals)
                {
                    var rounded = Math.Round(v, decimals, MidpointRounding.AwayFromZero);
                    return rounded.ToString("F" + decimals, CultureInfo.InvariantCulture);
                }

                void EnsureLastValidCachesInitialized()
                {
                    if (string.IsNullOrWhiteSpace(vm.LastValidAmplitudeText))
                    {
                        vm.LastValidAmplitudeText = "0.000";
                    }
                    if (string.IsNullOrWhiteSpace(vm.LastValidFrequencyText))
                    {
                        vm.LastValidFrequencyText = "0.00";
                    }
                    if (string.IsNullOrWhiteSpace(vm.LastValidOffsetText))
                    {
                        vm.LastValidOffsetText = "0.000";
                    }
                    if (string.IsNullOrWhiteSpace(vm.LastValidDutyCycleText))
                    {
                        vm.LastValidDutyCycleText = "50";
                    }
                }

                void ShowValidationMessage(string message)
                {
                    if (!showMessage)
                    {
                        return;
                    }

                    ReMessageBox.Show(
                        message,
                        "提示",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Warning);
                }

                void RevertText(string propertyName, string reason)
                {
                    if (propertyName == nameof(AnalogOutputChannelConfigViewModel.AmplitudeText))
                    {
                        vm.AmplitudeText = vm.LastValidAmplitudeText;
                        ShowValidationMessage($"通道 {vm.ChannelName}：幅度输入无效，已回退。\n{reason}");
                    }
                    else if (propertyName == nameof(AnalogOutputChannelConfigViewModel.FrequencyText))
                    {
                        vm.FrequencyText = vm.LastValidFrequencyText;
                        ShowValidationMessage($"通道 {vm.ChannelName}：频率输入无效，已回退。\n{reason}");
                    }
                    else if (propertyName == nameof(AnalogOutputChannelConfigViewModel.OffsetText))
                    {
                        vm.OffsetText = vm.LastValidOffsetText;
                        ShowValidationMessage($"通道 {vm.ChannelName}：偏置输入无效，已回退。\n{reason}");
                    }
                    else if (propertyName == nameof(AnalogOutputChannelConfigViewModel.DutyCycleText))
                    {
                        vm.DutyCycleText = vm.LastValidDutyCycleText;
                        ShowValidationMessage($"通道 {vm.ChannelName}：占空比输入无效，已回退。\n{reason}");
                    }
                }

                EnsureLastValidCachesInitialized();

                if (vm.WaveformType == OutputWaveformType.Dc)
                {
                    vm.IsAmplitudeReadOnly = true;
                    vm.IsFrequencyReadOnly = true;
                    vm.IsOffsetReadOnly = false;
                    vm.IsDutyCycleReadOnly = true;

                    vm.AmplitudeText = "-";
                    vm.FrequencyText = "-";
                    vm.DutyCycleText = "-";

                    if (string.IsNullOrWhiteSpace(vm.OffsetText) || vm.OffsetText.Trim() == "-")
                    {
                        vm.OffsetText = vm.LastValidOffsetText;
                    }

                    if (!TryParseDouble(vm.OffsetText, out var offset))
                    {
                        RevertText(nameof(AnalogOutputChannelConfigViewModel.OffsetText), "格式不正确");
                        return;
                    }

                    if (offset > 10.0 || offset < -10.0)
                    {
                        RevertText(nameof(AnalogOutputChannelConfigViewModel.OffsetText), $"偏置必须在 [-10, 10]V 内，当前={offset}V");
                        return;
                    }

                    vm.OffsetText = RoundFormat(offset, 3);
                    vm.LastValidOffsetText = vm.OffsetText;
                    return;
                }

                vm.IsAmplitudeReadOnly = false;
                vm.IsFrequencyReadOnly = false;
                vm.IsOffsetReadOnly = false;
                vm.IsDutyCycleReadOnly = vm.WaveformType != OutputWaveformType.Square;

                if (string.IsNullOrWhiteSpace(vm.AmplitudeText) || vm.AmplitudeText.Trim() == "-")
                {
                    vm.AmplitudeText = vm.LastValidAmplitudeText;
                }

                if (string.IsNullOrWhiteSpace(vm.FrequencyText) || vm.FrequencyText.Trim() == "-")
                {
                    vm.FrequencyText = vm.LastValidFrequencyText;
                }

                if (string.IsNullOrWhiteSpace(vm.OffsetText) || vm.OffsetText.Trim() == "-")
                {
                    vm.OffsetText = vm.LastValidOffsetText;
                }

                if (vm.WaveformType == OutputWaveformType.Square)
                {
                    if (string.IsNullOrWhiteSpace(vm.DutyCycleText) || vm.DutyCycleText.Trim() == "-")
                    {
                        vm.DutyCycleText = vm.LastValidDutyCycleText;
                    }
                }
                else
                {
                    vm.DutyCycleText = "-";
                }

                if (!TryParseDouble(vm.AmplitudeText, out var amp))
                {
                    RevertText(nameof(AnalogOutputChannelConfigViewModel.AmplitudeText), "格式不正确");
                    return;
                }

                if (amp < 0)
                {
                    RevertText(nameof(AnalogOutputChannelConfigViewModel.AmplitudeText), "幅度不能为负");
                    return;
                }

                if (!TryParseDouble(vm.FrequencyText, out var freq))
                {
                    RevertText(nameof(AnalogOutputChannelConfigViewModel.FrequencyText), "格式不正确");
                    return;
                }

                if (freq < 0)
                {
                    RevertText(nameof(AnalogOutputChannelConfigViewModel.FrequencyText), "频率不能为负");
                    return;
                }

                if (!TryParseDouble(vm.OffsetText, out var offset2))
                {
                    RevertText(nameof(AnalogOutputChannelConfigViewModel.OffsetText), "格式不正确");
                    return;
                }

                if (offset2 > 10.0 || offset2 < -10.0)
                {
                    RevertText(nameof(AnalogOutputChannelConfigViewModel.OffsetText), $"偏置必须在 [-10, 10]V 内，当前={offset2}V");
                    return;
                }

                var highLevel = offset2 + amp;
                var lowLevel = offset2 - amp;
                if (highLevel > 10.0 || lowLevel < -10.0)
                {
                    var reason = $"输出范围超限：范围=[{lowLevel:F3}V, {highLevel:F3}V]，要求=[-10V, 10V]";

                    if (string.Equals(changedPropertyName, nameof(AnalogOutputChannelConfigViewModel.OffsetText), StringComparison.Ordinal))
                    {
                        RevertText(nameof(AnalogOutputChannelConfigViewModel.OffsetText), reason);
                    }
                    else
                    {
                        RevertText(nameof(AnalogOutputChannelConfigViewModel.AmplitudeText), reason);
                    }

                    return;
                }

                if (vm.WaveformType == OutputWaveformType.Square)
                {
                    if (!TryParseDouble(vm.DutyCycleText, out var duty))
                    {
                        RevertText(nameof(AnalogOutputChannelConfigViewModel.DutyCycleText), "格式不正确");
                        return;
                    }

                    if (duty <= 0 || duty >= 100)
                    {
                        RevertText(nameof(AnalogOutputChannelConfigViewModel.DutyCycleText), $"占空比必须在 (0, 100) 内，当前={duty}%");
                        return;
                    }

                    vm.DutyCycleText = ((int)Math.Round(duty, 0, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture);
                    vm.LastValidDutyCycleText = vm.DutyCycleText;
                }

                vm.AmplitudeText = RoundFormat(amp, 3);
                vm.LastValidAmplitudeText = vm.AmplitudeText;

                vm.FrequencyText = RoundFormat(freq, 2);
                vm.LastValidFrequencyText = vm.FrequencyText;

                vm.OffsetText = RoundFormat(offset2, 3);
                vm.LastValidOffsetText = vm.OffsetText;
            }
            finally
            {
                _isValidatingChannelConfig = false;
            }
        }

        /// <summary>
        /// 修正后的配置验证方法 - 使用统一的范围检查逻辑
        /// </summary>
        private bool HasValidOutputConfigs()
        {
            if (Channels == null || OutputChannelConfigs == null)
                return false;

            var enabledChannels = Channels.Where(c => c.IsEnabled).ToList();
            if (enabledChannels.Count == 0)
                return false;

            foreach (var ch in enabledChannels)
            {
                var cfg = OutputChannelConfigs.FirstOrDefault(c => c.ChannelName == ch.ChannelName);
                if (cfg == null)
                    return false;

                if (cfg.WaveformType == OutputWaveformType.Dc)
                {
                    // 直流模式：只检查偏置
                    if (!TryParseDouble(cfg.OffsetText, out var offset))
                        return false;

                    if (offset > 10.0 || offset < -10.0)
                        return false;
                }
                else if (cfg.WaveformType == OutputWaveformType.Sine ||
                         cfg.WaveformType == OutputWaveformType.Square)
                {
                    // 正弦波和方波使用相同的检查逻辑
                    if (!TryParseDouble(cfg.OffsetText, out var offset))
                        return false;
                    if (!TryParseDouble(cfg.AmplitudeText, out var amp))
                        return false;
                    if (!TryParseDouble(cfg.FrequencyText, out var freq))
                        return false;

                    // ✅ 正确的范围检查：
                    // 高电平 = offset + amp，必须 ≤ 10V
                    // 低电平 = offset - amp，必须 ≥ -10V
                    var highLevel = offset + amp;
                    var lowLevel = offset - amp;

                    if (highLevel > 10.0 || lowLevel < -10.0)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[AO] 通道 {cfg.ChannelName} 范围超限: " +
                            $"偏置={offset}V, 幅值={amp}V, " +
                            $"范围=[{lowLevel}V, {highLevel}V], 要求=[-10V, 10V]");
                        return false;
                    }

                    // 检查频率是否有效
                    if (freq < 0)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[AO] 通道 {cfg.ChannelName} 频率无效: {freq}Hz");
                        return false;
                    }

                    // 方波额外检查占空比
                    if (cfg.WaveformType == OutputWaveformType.Square)
                    {
                        if (!TryParseDouble(cfg.DutyCycleText, out var duty))
                            return false;

                        if (duty <= 0 || duty >= 100)
                        {
                            System.Diagnostics.Debug.WriteLine(
                                $"[AO] 通道 {cfg.ChannelName} 占空比无效: {duty}%");
                            return false;
                        }
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// 添加更详细的配置诊断方法
        /// </summary>
        private string DiagnoseChannelConfig(AnalogOutputChannelConfigViewModel cfg)
        {
            var issues = new List<string>();

            if (cfg.WaveformType == OutputWaveformType.Dc)
            {
                if (!TryParseDouble(cfg.OffsetText, out var offset))
                {
                    issues.Add("偏置值无效");
                }
                else if (offset > 10.0)
                {
                    issues.Add($"偏置 {offset}V 超过最大值 10V");
                }
                else if (offset < -10.0)
                {
                    issues.Add($"偏置 {offset}V 低于最小值 -10V");
                }
            }
            else
            {
                if (!TryParseDouble(cfg.AmplitudeText, out var amp))
                {
                    issues.Add("幅值无效");
                }
                else if (amp < 0)
                {
                    issues.Add("幅值不能为负");
                }

                if (!TryParseDouble(cfg.FrequencyText, out var freq))
                {
                    issues.Add("频率无效");
                }
                else if (freq < 0)
                {
                    issues.Add("频率不能为负");
                }

                if (!TryParseDouble(cfg.OffsetText, out var offset))
                {
                    issues.Add("偏置无效");
                }
                else if (TryParseDouble(cfg.AmplitudeText, out var validAmp))
                {
                    var highLevel = offset + validAmp;
                    var lowLevel = offset - validAmp;

                    if (highLevel > 10.0)
                    {
                        issues.Add($"高电平 {highLevel}V (偏置{offset}V + 幅值{validAmp}V) 超过 10V");
                    }
                    if (lowLevel < -10.0)
                    {
                        issues.Add($"低电平 {lowLevel}V (偏置{offset}V - 幅值{validAmp}V) 低于 -10V");
                    }
                }

                if (cfg.WaveformType == OutputWaveformType.Square)
                {
                    if (!TryParseDouble(cfg.DutyCycleText, out var duty))
                    {
                        issues.Add("占空比无效");
                    }
                    else if (duty <= 0 || duty >= 100)
                    {
                        issues.Add($"占空比 {duty}% 必须在 (0, 100) 范围内");
                    }
                }
            }

            return issues.Count > 0
                ? $"通道 {cfg.ChannelName}:\n  • " + string.Join("\n  • ", issues)
                : null;
        }

        private void MarkDirty()
        {
            if (!_isApplyingTaskConfig)
            {
                HasPendingChanges = true;
            }
        }

        /// <summary>
        /// 切换任务或关闭界面前，提示并处理未保存的更改。
        /// </summary>
        private bool EnsurePendingChangesHandled()
        {
            if (!HasPendingChanges || _isLoadingTaskOptions)
            {
                return true;
            }

            var message = string.IsNullOrEmpty(SelectedTestTask)
                ? "模拟量输出配置尚未保存，是否现在保存？"
                : $"\"{SelectedTestTask}\" 的{CardName}配置尚未保存，是否保存？";

            var result = ReMessageBox.Show(
                message,
                "提示",
                System.Windows.MessageBoxButton.YesNoCancel,
                System.Windows.MessageBoxImage.Question);

            if (result == System.Windows.MessageBoxResult.Yes)
            {
                return SaveCurrentTaskConfig();
            }

            if (result == System.Windows.MessageBoxResult.No)
            {
                HasPendingChanges = false;
                return true;
            }

            return false;
        }

        private void OnAvailableTestTasksChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            RaisePropertyChanged(nameof(HasTestTaskOptions));
        }

        /// <summary>
        /// 切换当前选中的测试任务，必要时先处理未保存修改，然后加载新任务配置。
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
        /// 读取工程中的测试任务列表，设置初始选项并触发对应配置加载。
        /// </summary>
        private void LoadTestTaskOptions()
        {
            _isLoadingTaskOptions = true;
            try
            {
                AvailableTestTasks.Clear();
                var taskNames = GetTestTaskNamesFromProject();
                foreach (var task in taskNames)
                {
                    AvailableTestTasks.Add(task);
                }

                var cardConfig = EnsureAnalogOutputCardConfig();
                if (cardConfig != null)
                {
                    EnsureTaskConfigsExist(cardConfig, taskNames);
                }

                string initialTask = null;
                if (Device?.CardConfigData is AnalogOutputCardConfig existingConfig &&
                    !string.IsNullOrEmpty(existingConfig.LastSelectedTestTask) &&
                    AvailableTestTasks.Contains(existingConfig.LastSelectedTestTask))
                {
                    initialTask = existingConfig.LastSelectedTestTask;
                }
                else
                {
                    initialTask = AvailableTestTasks.FirstOrDefault();
                }

                _selectedTestTask = initialTask;
                RaisePropertyChanged(nameof(SelectedTestTask));
                (SaveConfigCommand as DelegateCommand)?.RaiseCanExecuteChanged();
                (ReloadConfigCommand as DelegateCommand)?.RaiseCanExecuteChanged();

                if (!string.IsNullOrEmpty(initialTask))
                {
                    LoadConfigForTask(initialTask);
                }
                else
                {
                    // 没有测试任务时仍允许通道勾选并更新右侧
                    RebuildOutputChannelConfigs();
                    HasPendingChanges = false;
                }
            }
            finally
            {
                _isLoadingTaskOptions = false;
            }
        }

        private void EnsureTaskConfigsExist(AnalogOutputCardConfig cardConfig, IEnumerable<string> taskNames)
        {
            if (cardConfig == null || taskNames == null)
            {
                return;
            }

            foreach (var taskName in taskNames.Where(t => !string.IsNullOrWhiteSpace(t)))
            {
                _ = GetOrCreateTaskConfig(cardConfig, taskName);
            }
        }

        private void ReloadCurrentTaskConfig()
        {
            if (string.IsNullOrEmpty(SelectedTestTask))
            {
                ReMessageBox.Show(
                    "Please select a test task before reloading",
                    "Info",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
                return;
            }

            if (HasPendingChanges)
            {
                var message = string.Format("Reloading will discard unsaved changes for \"{0}\". Continue?", SelectedTestTask);
                var confirm = ReMessageBox.Show(
                    message,
                    "Prompt",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Question);
                if (confirm != System.Windows.MessageBoxResult.Yes)
                {
                    return;
                }
            }

            LoadConfigForTask(SelectedTestTask);
        }

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
        /// 按任务名加载或创建对应的任务配置并应用到界面。
        /// </summary>
        private void LoadConfigForTask(string taskName)
        {
            var cardConfig = EnsureAnalogOutputCardConfig();
            if (cardConfig == null)
            {
                HasPendingChanges = false;
                return;
            }

            var config = GetOrCreateTaskConfig(cardConfig, taskName ?? string.Empty);
            cardConfig.LastSelectedTestTask = taskName;
            ApplyTaskConfig(config);
            HasPendingChanges = false;
        }

        /// <summary>
        /// 将任务配置应用到通道/采样率，并重建界面状态。
        /// </summary>
        private void ApplyTaskConfig(AnalogOutputTestTaskConfig config)
        {
            _isApplyingTaskConfig = true;
            try
            {
                SampleRate = config?.SampleRate > 0 ? config.SampleRate : 1000;

                foreach (var ch in Channels)
                {
                    var saved = config?.Channels.FirstOrDefault(c => c.ChannelName == ch.ChannelName);
                    ch.IsEnabled = saved?.IsEnabled ?? false;
                    ch.Range = saved?.Range ?? "直流";

                    var state = GetOrCreateChannelState(ch.ChannelName);
                    state.IsEnabled = ch.IsEnabled;
                    state.Range = ch.Range;
                    state.WaveformType = MapRangeToWaveform(ch.Range);
                    state.Amplitude = saved?.Amplitude ?? state.Amplitude;
                    state.Frequency = saved?.Frequency ?? state.Frequency;
                    state.Offset = saved?.Offset ?? state.Offset;
                    state.DutyCycle = saved?.DutyCycle ?? state.DutyCycle;
                    state.IsPreviewEnabled = saved?.IsPreviewEnabled ?? false;
                    state.PreviewColorHex = saved?.PreviewColorHex;
                }

                RebuildOutputChannelConfigs();
                SyncEnabledChannelsToDriverState();
                HasPendingChanges = false;

                // 配置恢复后更新UI状态
                RaisePropertyChanged(nameof(CanStartOutput));
                RaisePropertyChanged(nameof(CanToggleOutput));
            }
            finally
            {
                _isApplyingTaskConfig = false;
            }
        }

        private void OnProjectModified(ProjectModifiedEventArgs args)
        {
            if (args?.ModificationType != null &&
                args.ModificationType.IndexOf("TestTask", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                LoadTestTaskOptions();
            }
        }

        /// <summary>
        /// 获取或创建指定任务的配置，默认复制板卡通道元数据。
        /// </summary>
        private AnalogOutputTestTaskConfig GetOrCreateTaskConfig(AnalogOutputCardConfig cardConfig, string taskName)
        {
            taskName ??= string.Empty;
            var config = cardConfig.TestTaskConfigs.FirstOrDefault(c => c.TestTaskName == taskName);
            if (config == null)
            {
                config = new AnalogOutputTestTaskConfig { TestTaskName = taskName };
                InitializeTaskConfigChannels(config, cardConfig);
                cardConfig.TestTaskConfigs.Add(config);
            }
            return config;
        }

        /// <summary>
        /// 按当前板卡通道信息初始化新任务配置的通道条目。
        /// </summary>
        private void InitializeTaskConfigChannels(AnalogOutputTestTaskConfig targetConfig, AnalogOutputCardConfig sourceConfig)
        {
            targetConfig.Channels.Clear();
            foreach (var ch in Channels)
            {
                var source = (sourceConfig.Channels ?? new ObservableCollection<AnalogChannelConfig>())
                    .OfType<AnalogOutputExtendedChannelConfig>()
                    .FirstOrDefault(c => c.ChannelName == ch.ChannelName);

                targetConfig.Channels.Add(new AnalogOutputExtendedChannelConfig
                {
                    ChannelName = ch.ChannelName,
                    IsEnabled = false,
                    Range = source?.Range ?? ch.Range,
                    AvailableRanges = source?.AvailableRanges ?? ch.AvailableRanges.ToList(),
                    CurrentValue = 0,
                    Unit = ch.Unit,
                    Status = ch.Status,
                    WaveformType = source?.WaveformType ?? MapRangeToWaveform(ch.Range),
                    Amplitude = source?.Amplitude ?? 0,
                    Frequency = source?.Frequency ?? 0,
                    Offset = source?.Offset ?? 0,
                    DutyCycle = source?.DutyCycle ?? 50,
                    IsPreviewEnabled = source?.IsPreviewEnabled ?? false,
                    PreviewColorHex = source?.PreviewColorHex
                });
            }
        }

        /// <summary>
        /// 确保板卡配置对象存在并填充基础标识字段。
        /// </summary>
        private AnalogOutputCardConfig EnsureAnalogOutputCardConfig()
        {
            if (Device == null)
            {
                return null;
            }

            var cardConfig = Device.CardConfigData as AnalogOutputCardConfig;
            if (cardConfig == null)
            {
                cardConfig = new AnalogOutputCardConfig();
                Device.CardConfigData = cardConfig;
            }

            cardConfig.CardId = Device.Id;
            cardConfig.CardName = CardName;
            cardConfig.CardModel = CardModel;
            cardConfig.ChassisName = ChassisName;
            return cardConfig;
        }

        /// <summary>
        /// 获取指定通道的缓存状态，不存在时初始化默认值。
        /// </summary>
        private AnalogOutputExtendedChannelConfig GetOrCreateChannelState(string channelName)
        {
            if (!_channelStateCache.TryGetValue(channelName, out var state))
            {
                state = new AnalogOutputExtendedChannelConfig
                {
                    ChannelName = channelName,
                    Range = "直流",
                    AvailableRanges = new List<string> { "直流", "正弦", "方波" },
                    Amplitude = 0,
                    Frequency = 0,
                    Offset = 0,
                    DutyCycle = 50,
                    IsEnabled = false,
                    WaveformType = OutputWaveformType.Dc,
                    IsPreviewEnabled = false
                };
                _channelStateCache[channelName] = state;
            }

            return state;
        }

        private void SyncChannelStateFromChannelInfo(ChannelInfo ch)
        {
            var state = GetOrCreateChannelState(ch.ChannelName);
            state.IsEnabled = ch.IsEnabled;
            state.Range = ch.Range;
            state.WaveformType = MapRangeToWaveform(ch.Range);
        }

        private void UpdateChannelStateFromViewModel(AnalogOutputChannelConfigViewModel vm)
        {
            var state = GetOrCreateChannelState(vm.ChannelName);
            state.WaveformType = vm.WaveformType;
            state.Range = MapWaveformToRange(vm.WaveformType);
            state.IsPreviewEnabled = vm.IsPreviewEnabled;
            state.PreviewColorHex = vm.PreviewColorHex;
            state.Amplitude = TryParseDouble(vm.AmplitudeText, out var a) ? a : 0;
            state.Frequency = TryParseDouble(vm.FrequencyText, out var f) ? f : 0;
            state.Offset = TryParseDouble(vm.OffsetText, out var o) ? o : 0;
            state.DutyCycle = TryParseDouble(vm.DutyCycleText, out var d) ? d : 50;
            state.IsEnabled = Channels.FirstOrDefault(c => c.ChannelName == vm.ChannelName)?.IsEnabled ?? state.IsEnabled;

            // 创建或更新参数快照，用于版本控制和变化检测
            var currentParams = new WaveformParameters
            {
                WaveformType = vm.WaveformType,
                Amplitude = state.Amplitude,
                Frequency = state.Frequency,
                Offset = state.Offset,
                DutyCycle = state.DutyCycle,
                Version = _parameterSnapshots.TryGetValue(vm.ChannelName, out var existing) ? existing.Version + 1 : 1
            };

            _parameterSnapshots[vm.ChannelName] = currentParams;
        }

        /// <summary>
        /// 将当前界面配置写回任务配置并保存到板卡配置数据。
        /// </summary>
        private bool SaveCurrentTaskConfig()
        {
            if (Device == null)
            {
                return false;
            }

            if (string.IsNullOrEmpty(SelectedTestTask))
            {
                ReMessageBox.Show(
                    "Please select a test task before saving",
                    "Info",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
                return false;
            }

            var cardConfig = EnsureAnalogOutputCardConfig();
            if (cardConfig == null)
            {
                return false;
            }

            var taskConfig = GetOrCreateTaskConfig(cardConfig, SelectedTestTask);
            taskConfig.SampleRate = SampleRate;
            taskConfig.Channels.Clear();
            foreach (var ch in Channels)
            {
                var state = GetOrCreateChannelState(ch.ChannelName);
                state.IsEnabled = ch.IsEnabled;
                state.Range = ch.Range;
                state.WaveformType = MapRangeToWaveform(ch.Range);

                taskConfig.Channels.Add(new AnalogOutputExtendedChannelConfig
                {
                    ChannelName = state.ChannelName,
                    IsEnabled = state.IsEnabled,
                    Range = state.Range,
                    AvailableRanges = state.AvailableRanges?.ToList() ?? new List<string>(),
                    CurrentValue = 0,
                    Unit = ch.Unit,
                    Status = ch.Status,
                    WaveformType = state.WaveformType,
                    Amplitude = state.Amplitude,
                    Frequency = state.Frequency,
                    Offset = state.Offset,
                    DutyCycle = state.DutyCycle,
                    IsPreviewEnabled = state.IsPreviewEnabled,
                    PreviewColorHex = state.PreviewColorHex
                });
            }

            cardConfig.LastSelectedTestTask = SelectedTestTask;
            SaveToCardConfigData(cardConfig, taskConfig);
            HasPendingChanges = false;
            ReMessageBox.Show(
                "保存成功",
                "提示",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            return true;
        }

        private bool TryParseDouble(string text, out double value)
        {
            if (string.IsNullOrWhiteSpace(text) || text == "-")
            {
                value = 0;
                return false;
            }

            text = text.Trim();
            return double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) ||
                   double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private static double ClampAoVoltage(double v)
        {
            if (v > 10.0) return 10.0;
            if (v < -10.0) return -10.0;
            return v;
        }

        private void ApplyAoCalibration(string channelName, OutputWaveformType waveformType, ref double amplitude, ref double offset)
        {
            if (string.IsNullOrWhiteSpace(channelName) || Device == null)
                return;

            var scopedKey = string.IsNullOrWhiteSpace(Device.Id) ? channelName : $"{Device.Id}/{channelName}";
            var (slope, intercept, isCalibrated) = CalibrationService.Instance.GetCalibrationParams(scopedKey);
            if (!isCalibrated)
                return;

            // 标定补偿：直接使用补偿系数
            // command = target * k + b
            if (waveformType == OutputWaveformType.Dc)
            {
                amplitude = 0;
                offset = offset * slope + intercept;
            }
            else
            {
                amplitude = amplitude * slope;
                offset = offset * slope + intercept;
            }

            if (amplitude < 0) amplitude = 0;

            offset = ClampAoVoltage(offset);
            amplitude = Math.Max(0.0, amplitude);

            if (waveformType != OutputWaveformType.Dc)
            {
                // Keep within ±10V by shrinking amplitude if needed
                if (offset + amplitude > 10.0) amplitude = Math.Max(0.0, 10.0 - offset);
                if (offset - amplitude < -10.0) amplitude = Math.Max(0.0, offset + 10.0);
            }
        }

        /// <summary>
        /// 解析形如 AO0-AO3 的槽位范围，返回起止索引。
        /// </summary>
        private (int startIndex, int endIndex) ParseSlotPosition(string slotPosition, string prefix)
        {
            try
            {
                var delimiters = new[] { '-' };
                var parts = slotPosition?.Replace(" ", string.Empty)
                    .Split(delimiters, StringSplitOptions.RemoveEmptyEntries);
                if (parts == null || parts.Length != 2)
                    return (0, 31);

                var startStr = parts[0].Replace(prefix, string.Empty);
                var endStr = parts[1].Replace(prefix, string.Empty);

                if (int.TryParse(startStr, out int start) && int.TryParse(endStr, out int end))
                {
                    return (start, end);
                }
            }
            catch
            {
            }

            return (0, 31);
        }

        private OutputWaveformType MapRangeToWaveform(string range)
        {
            range = range?.Trim();
            if (string.Equals(range, "正弦", StringComparison.OrdinalIgnoreCase) || string.Equals(range, "Sine", StringComparison.OrdinalIgnoreCase))
            {
                return OutputWaveformType.Sine;
            }
            if (string.Equals(range, "方波", StringComparison.OrdinalIgnoreCase) || string.Equals(range, "Square", StringComparison.OrdinalIgnoreCase))
            {
                return OutputWaveformType.Square;
            }
            return OutputWaveformType.Dc;
        }

        private string MapWaveformToRange(OutputWaveformType waveform)
        {
            if (waveform == OutputWaveformType.Sine)
            {
                return "正弦";
            }
            if (waveform == OutputWaveformType.Square)
            {
                return "方波";
            }
            return "直流";
        }

        private void LoadCardMetadata()
        {
            if (Device?.CardConfigData is AnalogOutputCardConfig aoConfig)
            {
                if (!string.IsNullOrEmpty(aoConfig.CardName))
                {
                    _cardName = aoConfig.CardName;
                    RaisePropertyChanged(nameof(CardName));
                }
            }
        }

        private void SaveToCardConfigData(AnalogOutputCardConfig cardConfig, AnalogOutputTestTaskConfig taskConfig)
        {
            if (Device == null || cardConfig == null || taskConfig == null)
                return;

            cardConfig.CardId = Device.Id;
            cardConfig.CardName = CardName;
            cardConfig.CardModel = CardModel;
            cardConfig.ChassisName = ChassisName;

            cardConfig.Channels.Clear();
            foreach (var ch in taskConfig.Channels)
            {
                cardConfig.Channels.Add(new AnalogOutputExtendedChannelConfig
                {
                    ChannelName = ch.ChannelName,
                    IsEnabled = ch.IsEnabled,
                    Range = ch.Range,
                    AvailableRanges = ch.AvailableRanges?.ToList() ?? new List<string>(),
                    CurrentValue = ch.CurrentValue,
                    Unit = ch.Unit,
                    Status = ch.Status,
                    WaveformType = ch.WaveformType,
                    Amplitude = ch.Amplitude,
                    Frequency = ch.Frequency,
                    Offset = ch.Offset,
                    DutyCycle = ch.DutyCycle,
                    IsPreviewEnabled = ch.IsPreviewEnabled,
                    PreviewColorHex = ch.PreviewColorHex
                });
            }

            if (Device.CardName != CardName)
            {
                Device.CardName = CardName;
            }

            _pxiChassisService?.UpdateDeviceCardConfig(Device.Id, cardConfig);

            _eventAggregator?.GetEvent<ProjectModifiedEvent>()?.Publish(new ProjectModifiedEventArgs
            {
                ModificationType = "AnalogOutputConfig",
                Description = $"Analog output config saved for {SelectedTestTask}"
            });

            _eventAggregator?.GetEvent<ChannelEnableChangedEvent>()?.Publish(new ChannelEnableChangedEventArgs
            {
                DeviceId = Device.Id,
                CardName = CardName,
                ChassisName = ChassisName
            });
        }

        private void UpdatePreviewColor(AnalogOutputChannelConfigViewModel vm)
        {
            if (vm == null)
                return;

            if (vm.IsPreviewEnabled)
            {

                if (!string.IsNullOrWhiteSpace(vm.PreviewColorHex))
                {
                    _usedPreviewColors.Add(vm.PreviewColorHex);
                    return;
                }


                var random = new Random(Guid.NewGuid().GetHashCode());
                for (int i = 0; i < 1000; i++)
                {
                    byte r = (byte)random.Next(32, 224);
                    byte g = (byte)random.Next(32, 224);
                    byte b = (byte)random.Next(32, 224);
                    string hex = $"#{r:X2}{g:X2}{b:X2}";

                    if (_usedPreviewColors.Add(hex))
                    {
                        vm.PreviewColorHex = hex;
                        break;
                    }
                }
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(vm.PreviewColorHex))
                {
                    _usedPreviewColors.Remove(vm.PreviewColorHex);
                }
            }
        }

        /// <summary>
        /// 连接并检测设备，若成功则启动采集，失败时更新在线状态。
        /// </summary>
        private async Task OnOpenDeviceAsync()
        {
            if (Device == null) return;

            if (IsBusy)
            {
                return;
            }

            IsBusy = true;

            try
            {
                // 检查是否已有连接的驱动（可能是缓存的）
                if (_driver != null && _driver.IsConnected)
                {
                    _ownsDriverLifecycle = false;
                    IsDeviceConnected = true;
                    ConnectionStatus = "在线";
                    return;
                }

                // 检查缓存驱动
                var cachedDriver = DriverFactory.GetCachedDriver(Device.Id);
                if (cachedDriver != null && cachedDriver.IsConnected)
                {
                    _driver = cachedDriver;
                    _ownsDriverLifecycle = false;
                    IsDeviceConnected = true;
                    ConnectionStatus = "在线";
                    return;
                }

                ConnectionStatus = "检测中";

                _driver = DriverFactory.CreateDriver(Device);

                if (_driver == null)
                {
                    _ownsDriverLifecycle = false;
                    IsDeviceConnected = false;
                    ConnectionStatus = "离线";
                    ReMessageBox.Show(
                        $"板卡连接失败，请检查板卡位置及驱动",
                        "连接失败",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Error);
                    return;
                }

                // 如果驱动已连接（可能是从缓存获取的）
                if (_driver.IsConnected)
                {
                    _ownsDriverLifecycle = false;
                    IsDeviceConnected = true;
                    ConnectionStatus = "在线";
                    return;
                }

                await SyncEnabledChannelsToDriverStateBeforeConnectAsync();

                // 连接设备
                bool connected = await _driver.ConnectAsync();

                if (connected)
                {
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        _ownsDriverLifecycle = true;
                        IsDeviceConnected = true;
                        ConnectionStatus = "在线";
                    }, DispatcherPriority.Normal);
                    System.Diagnostics.Debug.WriteLine($"[AnalogOutputConfig] 板卡检测成功 {Device.Name}");

                    // 打开板卡只检测连接，不自动启动连续输出
                    // 开始输出的执行完全由开始输出按钮控制
                    System.Diagnostics.Debug.WriteLine("[AnalogOutputConfig] 板卡已连接，开始输出请点击开始输出按钮");
                }
                else
                {
                    _ownsDriverLifecycle = false;
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
                _ownsDriverLifecycle = false;
                IsDeviceConnected = false;
                ConnectionStatus = "离线";
                // 不将 _driver 设为 null，保留引用以便后续使用

                ReMessageBox.Show(
                    $"板卡连接失败，请检查板卡位置及驱动",
                    "连接失败",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);

                System.Diagnostics.Debug.WriteLine($"[AnalogOutputConfig] 板卡检测异常: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// 停止调试：先停输出并复位，再按需断开驱动连接。
        /// </summary>
        public async Task StopDebugAsync()
        {
            if (IsBusy)
            {
                return;
            }

            IsBusy = true;
            ConnectionStatus = "断开中";
            var swTotal = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                System.Diagnostics.Debug.WriteLine($"[AnalogOutputConfig] StopDebug: {Device?.Name}");

                // 如果正在输出，先自动停止输出
                if (IsOutputRunning)
                {
                    System.Diagnostics.Debug.WriteLine("[AnalogOutputConfig] 自动停止输出中...");
                    ConnectionStatus = "断开中";
                    await StopOutputOnlyAsync();
                }

                ConnectionStatus = "断开中";

                if (_driver != null && IsDeviceConnected)
                {
                    if (_ownsDriverLifecycle)
                    {
                        var swReset = System.Diagnostics.Stopwatch.StartNew();
                        await ResetAllAnalogOutputsAsync();
                        swReset.Stop();
                        System.Diagnostics.Debug.WriteLine($"[AnalogOutputConfig] ResetAllAnalogOutputsAsync elapsed={swReset.ElapsedMilliseconds}ms");
                    }
                }

                if (_driver != null && _ownsDriverLifecycle)
                {
                    var swStop = System.Diagnostics.Stopwatch.StartNew();
                    await _driver.StopAcquisitionAsync();
                    swStop.Stop();
                    System.Diagnostics.Debug.WriteLine($"[AnalogOutputConfig] Driver.StopAcquisitionAsync elapsed={swStop.ElapsedMilliseconds}ms");

                    var swDisconnect = System.Diagnostics.Stopwatch.StartNew();
                    await _driver.DisconnectAsync();
                    swDisconnect.Stop();
                    System.Diagnostics.Debug.WriteLine($"[AnalogOutputConfig] Driver.DisconnectAsync elapsed={swDisconnect.ElapsedMilliseconds}ms");
                    _driver = null;
                    IsDeviceConnected = false;
                    ConnectionStatus = "离线";
                }

                foreach (var vm in OutputChannelConfigs ?? Enumerable.Empty<AnalogOutputChannelConfigViewModel>())
                {
                    ValidateChannelConfig(vm, string.Empty, showMessage: false);
                }
                System.Diagnostics.Debug.WriteLine("[AnalogOutputConfig] Debug stopped");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AnalogOutputConfig] Stop debug exception: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }

            swTotal.Stop();
            System.Diagnostics.Debug.WriteLine($"[AnalogOutputConfig] StopDebug total elapsed={swTotal.ElapsedMilliseconds}ms");
        }
        /// <summary>
        /// 仅停止输出，不处理驱动断开，用于释放前的快速收尾。
        /// </summary>
        private async Task StopOutputOnlyAsync()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[AnalogOutputConfig] StopOutputOnlyAsync: {Device?.Name}");


                if (_driver != null && IsDeviceConnected && IsOutputRunning)
                {
                    await _driver.StopAcquisitionAsync();
                    IsOutputRunning = false;
                }

                foreach (var vm in OutputChannelConfigs ?? Enumerable.Empty<AnalogOutputChannelConfigViewModel>())
                {
                    ValidateChannelConfig(vm, string.Empty, showMessage: false);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AnalogOutputConfig] StopOutputOnlyAsync 寮傚父: {ex.Message}");
            }
        }

        /// <summary>
        /// 在启动输出前进行完整验证
        /// </summary>
        private async Task OnStartOutputAsync()
        {
            if (_driver == null || !IsDeviceConnected)
            {
                System.Diagnostics.Debug.WriteLine("[AnalogOutputConfig] 无法启动输出：驱动未连接");
                return;
            }

            if (IsBusy)
            {
                return;
            }

            IsBusy = true;
            try
            {
                if (OutputChannelConfigs == null || OutputChannelConfigs.Count == 0)
                {
                    ReMessageBox.Show(
                        "没有启用的通道，请先勾选至少一个通道后再开始输出。",
                        "提示",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Warning);
                    return;
                }

                var enabledChannelNames = new HashSet<string>(
                    Channels?.Where(c => c.IsEnabled).Select(c => c.ChannelName) ?? Enumerable.Empty<string>());

                if (enabledChannelNames.Count == 0)
                {
                    ReMessageBox.Show(
                        "没有启用的通道，请先勾选至少一个通道后再开始输出。",
                        "提示",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Warning);
                    return;
                }

                var enabledOutputConfigs = OutputChannelConfigs
                    .Where(cfg => cfg != null && enabledChannelNames.Contains(cfg.ChannelName))
                    .ToList();

                if (enabledOutputConfigs.Count == 0)
                {
                    ReMessageBox.Show(
                        "没有启用的通道配置，请先勾选通道并检查配置后再开始输出。",
                        "提示",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Warning);
                    return;
                }

                // 诊断所有通道配置
                var allIssues = enabledOutputConfigs
                    .Select(cfg => DiagnoseChannelConfig(cfg))
                    .Where(issue => issue != null)
                    .ToList();

                if (allIssues.Count > 0)
                {
                    var errorMsg = "配置错误，无法启动输出：\n\n" + string.Join("\n\n", allIssues);
                    ReMessageBox.Show(errorMsg, "配置错误",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Error);
                    return;
                }

                if (IsOutputRunning)
                {
                    await _driver.StopAcquisitionAsync();
                    IsOutputRunning = false;
                }

                foreach (var cfg in enabledOutputConfigs)
                {
                    var ampTarget = TryParseDouble(cfg.AmplitudeText, out var a) ? a : 0;
                    var offsetTarget = TryParseDouble(cfg.OffsetText, out var o) ? o : 0;
                    ApplyAoCalibration(cfg.ChannelName, cfg.WaveformType, ref ampTarget, ref offsetTarget);

                    var dict = new Dictionary<string, object>
                    {
                        ["Enabled"] = true,
                        ["SampleRate"] = SampleRate,
                        ["Waveform"] = cfg.WaveformType == OutputWaveformType.Dc
                            ? MTX532Driver.WaveformType.Dc
                            : cfg.WaveformType == OutputWaveformType.Sine
                                ? MTX532Driver.WaveformType.Sine
                                : MTX532Driver.WaveformType.Square,
                        ["Amplitude"] = ampTarget,
                        ["Offset"] = offsetTarget,
                        ["Frequency"] = TryParseDouble(cfg.FrequencyText, out var f) ? f : 0,
                        ["DutyCycle"] = TryParseDouble(cfg.DutyCycleText, out var d) ? d : 50
                    };

                    await _driver.ConfigureChannelAsync(cfg.ChannelName, dict);

                    System.Diagnostics.Debug.WriteLine(
                        $"[AO] {cfg.ChannelName}: {cfg.WaveformType}, " +
                        $"范围=[{(double)dict["Offset"] - (double)dict["Amplitude"]:F1}V, " +
                        $"{(double)dict["Offset"] + (double)dict["Amplitude"]:F1}V], " +
                        $"频率={dict["Frequency"]}Hz");
                }

                var started = await _driver.StartAcquisitionAsync();
                if (!started)
                {
                    ReMessageBox.Show(
                        "启动输出失败：未检测到已启用通道或驱动未就绪。请确认通道已勾选并重试。",
                        "输出错误",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Warning);
                    return;
                }
                IsOutputRunning = true;

                System.Diagnostics.Debug.WriteLine("[AO] 输出已启动");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AO] 启动失败: {ex.Message}");
                ReMessageBox.Show($"启动输出失败: {ex.Message}",
                    "输出错误",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// 停止波形输出，必要时执行 AO 复位。
        /// </summary>
        private async Task OnStopOutputAsync()
        {
            if (_driver == null || !IsDeviceConnected)
            {
                System.Diagnostics.Debug.WriteLine("[AnalogOutputConfig] 无法停止输出：驱动未连接");
                return;
            }

            if (IsBusy)
            {
                return;
            }

            IsBusy = true;

            try
            {
                System.Diagnostics.Debug.WriteLine("[AnalogOutputConfig] 停止输出");
                
                if (_ownsDriverLifecycle)
                {
                    // 先配置所有通道为 0V
                    await ResetAllAnalogOutputsAsync();
                    
                    // MTX532 连续输出存在缓冲区：ResetAllAnalogOutputsAsync 会触发队列刷新，
                    // 0V 配置需要等待旧缓冲区消耗后才会在硬件上体现。
                    await Task.Delay(500);
                }
                
                // 最后停止输出
                await _driver.StopAcquisitionAsync();

                IsOutputRunning = false;
                System.Diagnostics.Debug.WriteLine("[AnalogOutputConfig] 输出已停止并复位");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AnalogOutputConfig] 停止输出失败: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// 将所有启用通道复位为 0V 直流输出，避免残留输出。
        /// </summary>
        private async Task ResetAllAnalogOutputsAsync()
        {
            if (_driver == null || !IsDeviceConnected || !_ownsDriverLifecycle) return;

            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                System.Diagnostics.Debug.WriteLine("[AnalogOutputConfig] 复位所有 AO 输出到 0V");

                foreach (var ch in Channels.Where(c => c.IsEnabled))
                {
                    double ampCmd = 0.0;
                    double offsetCmd = 0.0;
                    ApplyAoCalibration(ch.ChannelName, OutputWaveformType.Dc, ref ampCmd, ref offsetCmd);

                    var dict = new System.Collections.Generic.Dictionary<string, object>
                    {
                        ["Enabled"] = true,
                        ["SampleRate"] = SampleRate,
                        ["Waveform"] = MTX532Driver.WaveformType.Dc,
                        ["Amplitude"] = ampCmd,
                        ["Offset"] = offsetCmd,
                        ["Frequency"] = 0.0
                    };

                    var swCh = System.Diagnostics.Stopwatch.StartNew();
                    await _driver.ConfigureChannelAsync(ch.ChannelName, dict);
                    swCh.Stop();
                    System.Diagnostics.Debug.WriteLine($"[AnalogOutputConfig] Reset {ch.ChannelName} elapsed={swCh.ElapsedMilliseconds}ms");
                }

                sw.Stop();
                System.Diagnostics.Debug.WriteLine($"[AnalogOutputConfig] ResetAllAnalogOutputsAsync total elapsed={sw.ElapsedMilliseconds}ms");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AnalogOutputConfig] 复位 AO 失败: {ex.Message}");
            }
        }


        /// <summary>
        /// 在输出运行时动态更新波形参数（带版本控制和变化检测）
        /// </summary>
        private async void UpdateOutputParameters(AnalogOutputChannelConfigViewModel cfg)
        {
            if (_driver == null || !IsDeviceConnected)
            {
                System.Diagnostics.Debug.WriteLine("[AO] 无法更新参数：驱动未连接");
                return;
            }

            // 获取当前参数快照
            if (!_parameterSnapshots.TryGetValue(cfg.ChannelName, out var currentSnapshot))
            {
                System.Diagnostics.Debug.WriteLine($"[AO] 警告：通道 {cfg.ChannelName} 没有参数快照");
                return;
            }

            // 版本控制：避免重复更新相同参数
            if (_parameterSnapshots.TryGetValue(cfg.ChannelName + "_last", out var lastSnapshot))
            {
                if (currentSnapshot.Version == lastSnapshot.Version &&
                    !currentSnapshot.HasChanged(lastSnapshot))
                {
                    // 参数没有变化，无需更新
                    return;
                }
            }

            var ampTarget = TryParseDouble(cfg.AmplitudeText, out var a) ? a : 0;
            var offsetTarget = TryParseDouble(cfg.OffsetText, out var o) ? o : 0;
            ApplyAoCalibration(cfg.ChannelName, cfg.WaveformType, ref ampTarget, ref offsetTarget);

            var dict = new Dictionary<string, object>
            {
                ["Waveform"] = cfg.WaveformType == OutputWaveformType.Dc ? MTX532Driver.WaveformType.Dc :
                              cfg.WaveformType == OutputWaveformType.Sine ? MTX532Driver.WaveformType.Sine :
                              MTX532Driver.WaveformType.Square,
                ["Amplitude"] = ampTarget,
                ["Offset"] = offsetTarget,
                ["Frequency"] = TryParseDouble(cfg.FrequencyText, out var f) ? f : 0,
                ["DutyCycle"] = TryParseDouble(cfg.DutyCycleText, out var d) ? d : 50
            };

            try
            {
                // 使用 ConfigureChannelAsync 直接下发到驱动，驱动会在下一次缓冲生成时生效
                await _driver.ConfigureChannelAsync(cfg.ChannelName, dict);

                // 更新最后版本记录
                _parameterSnapshots[cfg.ChannelName + "_last"] = currentSnapshot.Clone();

                System.Diagnostics.Debug.WriteLine($"[AO] 动态下发参数 {cfg.ChannelName} v{currentSnapshot.Version}: Amp={dict["Amplitude"]}, Freq={dict["Frequency"]}, Offset={dict["Offset"]}, Duty={dict["DutyCycle"]}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AO] 动态下发参数失败: {ex.Message}");
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
        public bool CanClose()
        {
            if (IsBusy)
            {
                var opText = IsDeviceConnected ? "关闭" : "打开";
                ReMessageBox.Show(
                    $"正在{opText}板卡，请稍候...",
                    "提示",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
                return false;
            }

            if (IsDeviceConnected)
            {
                return true;
            }

            return EnsurePendingChangesHandled();
        }

        public bool IsNavigationTarget(NavigationContext navigationContext)
        {
            return true;
        }

        public void OnNavigatedTo(NavigationContext navigationContext)
        {
        }

        public void OnNavigatedFrom(NavigationContext navigationContext)
        {
        }

        public void ConfirmNavigationRequest(NavigationContext navigationContext, Action<bool> continuationCallback)
        {
            if (IsBusy)
            {
                var opText = IsDeviceConnected ? "关闭" : "打开";
                ReMessageBox.Show(
                    $"正在{opText}板卡，请稍候...",
                    "提示",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
                continuationCallback(false);
                return;
            }

            if (IsDeviceConnected)
            {
                continuationCallback(true);
                return;
            }

            continuationCallback(EnsurePendingChangesHandled());
        }

        #region IDisposable

        private bool _disposed = false;

        /// <summary>
        /// 释放时确保输出停止并释放资源。
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// 受控释放模式，避免重复释放。
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                if (_projectModifiedToken != null)
                {
                    _eventAggregator?.GetEvent<ProjectModifiedEvent>()?.Unsubscribe(_projectModifiedToken);
                    _projectModifiedToken = null;
                }
            }

            _disposed = true;
        }

        #endregion
    }

    public class AnalogOutputChannelConfigViewModel : BindableBase
    {
        private string _channelName;
        private OutputWaveformType _waveformType;
        private bool _isWaveformTypeReadOnly;
        private string _amplitudeText;
        private string _frequencyText;
        private string _offsetText;
        private bool _isAmplitudeReadOnly;
        private bool _isFrequencyReadOnly;
        private bool _isOffsetReadOnly;
        private string _dutyCycleText;
        private bool _isDutyCycleReadOnly;
        private bool _isPreviewEnabled;
        private string _previewColorHex;

        public string LastValidAmplitudeText { get; set; }
        public string LastValidFrequencyText { get; set; }
        public string LastValidOffsetText { get; set; }
        public string LastValidDutyCycleText { get; set; }

        public string ChannelName
        {
            get => _channelName;
            set => SetProperty(ref _channelName, value);
        }

        public OutputWaveformType WaveformType
        {
            get => _waveformType;
            set => SetProperty(ref _waveformType, value);
        }

        public bool IsWaveformTypeReadOnly
        {
            get => _isWaveformTypeReadOnly;
            set => SetProperty(ref _isWaveformTypeReadOnly, value);
        }

        public string AmplitudeText
        {
            get => _amplitudeText;
            set => SetProperty(ref _amplitudeText, value);
        }

        public string FrequencyText
        {
            get => _frequencyText;
            set => SetProperty(ref _frequencyText, value);
        }

        public string OffsetText
        {
            get => _offsetText;
            set => SetProperty(ref _offsetText, value);
        }

        public bool IsAmplitudeReadOnly
        {
            get => _isAmplitudeReadOnly;
            set => SetProperty(ref _isAmplitudeReadOnly, value);
        }

        public bool IsFrequencyReadOnly
        {
            get => _isFrequencyReadOnly;
            set => SetProperty(ref _isFrequencyReadOnly, value);
        }

        public bool IsOffsetReadOnly
        {
            get => _isOffsetReadOnly;
            set => SetProperty(ref _isOffsetReadOnly, value);
        }

        public string DutyCycleText
        {
            get => _dutyCycleText;
            set => SetProperty(ref _dutyCycleText, value);
        }

        public bool IsDutyCycleReadOnly
        {
            get => _isDutyCycleReadOnly;
            set => SetProperty(ref _isDutyCycleReadOnly, value);
        }

        public bool IsPreviewEnabled
        {
            get => _isPreviewEnabled;
            set => SetProperty(ref _isPreviewEnabled, value);
        }

        public string PreviewColorHex
        {
            get => _previewColorHex;
            set => SetProperty(ref _previewColorHex, value);
        }
    }

}
