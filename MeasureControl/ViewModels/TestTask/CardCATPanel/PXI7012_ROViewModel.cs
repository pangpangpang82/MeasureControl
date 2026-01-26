using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;
using MeasureControl.Drivers;
using MeasureControl.Models;
using MeasureControl.Models.Devices;
using MeasureControl.Services;
using MeasureControl.Events;
using MeasureControl.Helpers;
using Prism.Commands;
using Prism.Mvvm;
using Prism.Events;
using Prism.Regions;
using MeasureControl.Views;
using MeasureControl.Views.Dialogs;
using MeasureControl.ViewModels;
using MeasureControl.Models.Devices.DeviceCategories;

namespace MeasureControl.ViewModels.TestTask.CardCATPanel
{
    /// <summary>
    /// 电阻输出通道配置面板的ViewModel - 用于配置可编程电阻输出板卡
    /// </summary>
    public class PXI7012_ROViewModel : BindableBase, IDisposable, IConfirmNavigationRequest, ICloseGuard
    {
        private enum RelayOutputMode
        {
            NoWait,             // 无等待，禁止建立时间
            BreakBeforeMake,    // 先断后连
            MakeBeforeBreak,    // 先通后断
            ImmediateWithWait   // 立即执行后，等待建立时间
        }
        private DeviceBase _device;
        private string _chassisName;
        private string _cardModel;
        private string _cardName;
        private ObservableCollection<ResistanceChannelInfo> _channels;
        private readonly ObservableCollection<string> _availableTestTasks = new ObservableCollection<string>();
        private readonly IPxiChassisService _pxiChassisService;
        private readonly IEventAggregator _eventAggregator;
        private readonly ProjectService _projectService;
        private SubscriptionToken _projectModifiedToken;
        private SubscriptionToken _projectSavingToken;
        private DispatcherTimer _readTimer;
        private IDeviceDriver _driver;
        private bool _isDeviceConnected;
        private bool _isDeviceConnecting;
        private bool _isBusy;
        private string _connectionStatus;
        private string _outputMode;
        private string _selectedTestTask;
        private bool _isApplyingTaskConfig;
        private bool _isLoadingTaskOptions;
        private bool _hasPendingChanges;
        private bool _isConfigurationLocked;
        private const int RelaySettleTimeMs = 50; // 建立/断开等待时间（毫秒），可按需调整
        private const string DefaultTestTaskName = "默认测试任务";
        private const string DefaultOutputModeText = "无等待，禁止建立时间";

        #region Properties

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
        /// 通道列表（9个通道）
        /// </summary>
        public ObservableCollection<ResistanceChannelInfo> Channels
        {
            get => _channels;
            set => SetProperty(ref _channels, value);
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
                    RaisePropertyChanged(nameof(IsConnectionIndicatorOn));
                    RaisePropertyChanged(nameof(CanEditChannelEnable));
                }
            }
        }

        public bool IsDeviceConnecting
        {
            get => _isDeviceConnecting;
            private set
            {
                if (SetProperty(ref _isDeviceConnecting, value))
                {
                    RaisePropertyChanged(nameof(IsConnectionIndicatorOn));
                    RaisePropertyChanged(nameof(CanEditChannelEnable));
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
                    RaisePropertyChanged(nameof(CanEditChannelEnable));
                }
            }
        }

        public bool IsConnectionIndicatorOn => IsDeviceConnected || IsDeviceConnecting;

        public bool CanEditChannelEnable => !IsDeviceConnected && !IsDeviceConnecting && !IsBusy;

        /// <summary>
        /// 连接状态文本
        /// </summary>
        public string ConnectionStatus
        {
            get => _connectionStatus;
            set => SetProperty(ref _connectionStatus, value);
        }

        /// <summary>
        /// 输出模式
        /// </summary>
        public string OutputMode
        {
            get => _outputMode;
            set
            {
                if (SetProperty(ref _outputMode, value) && !_isApplyingTaskConfig)
                {
                    MarkDirty();
                }
            }
        }

        /// <summary>
        /// 可选择的测试任务列表
        /// </summary>
        public ObservableCollection<string> AvailableTestTasks => _availableTestTasks;

        /// <summary>
        /// 是否存在测试任务选项
        /// </summary>
        public bool HasTestTaskOptions => AvailableTestTasks.Count > 0;

        /// <summary>
        /// 当前选择的测试任务
        /// </summary>
        public string SelectedTestTask
        {
            get => _selectedTestTask;
            set => ChangeSelectedTestTask(value);
        }

        /// <summary>
        /// 是否存在未保存变更
        /// </summary>
        public bool HasPendingChanges
        {
            get => _hasPendingChanges;
            private set
            {
                if (SetProperty(ref _hasPendingChanges, value))
                {
                    UpdateSaveReloadCanExecute();
                }
            }
        }

        /// <summary>
        /// 是否锁定配置（连接中锁定，连接成功/失败后解除）
        /// </summary>
        public bool IsConfigurationLocked
        {
            get => _isConfigurationLocked;
            private set
            {
                if (SetProperty(ref _isConfigurationLocked, value))
                {
                    RaisePropertyChanged(nameof(CanEditConfiguration));
                    UpdateSaveReloadCanExecute();
                }
            }
        }

        private void UpdateConfigurationLock()
        {
            IsConfigurationLocked = IsBusy;
        }

        public bool CanEditConfiguration => !IsConfigurationLocked;

        /// <summary>
        /// 左侧通道使能是否全选
        /// </summary>
        public bool IsAllEnabled
        {
            get => Channels != null && Channels.Count > 0 && Channels.All(c => c.IsEnabled);
            set
            {
                if (Channels == null)
                    return;

                foreach (var channel in Channels)
                {
                    channel.IsEnabled = value;
                }
                RaisePropertyChanged(nameof(IsAllEnabled));
            }
        }

        /// <summary>
        /// 将输出模式字符串映射为内部继电器模式
        /// </summary>
        private RelayOutputMode GetRelayMode()
        {
            return OutputMode switch
            {
                "先断后连" => RelayOutputMode.BreakBeforeMake,
                "先通后断" => RelayOutputMode.MakeBeforeBreak,
                "立即执行后，等待建立时间" => RelayOutputMode.ImmediateWithWait,
                _ => RelayOutputMode.NoWait
            };
        }

        #endregion

        #region Commands

        public ICommand SaveConfigCommand { get; }
        public ICommand ReloadConfigCommand { get; }
        public ICommand OpenDeviceCommand { get; }
        public ICommand CloseDeviceCommand { get; }
        public ICommand ToggleDeviceCommand { get; }

        #endregion

        #region Constructor

        public PXI7012_ROViewModel(ProjectService projectService = null)
        {
            _projectService = projectService;
            Channels = new ObservableCollection<ResistanceChannelInfo>();
            _availableTestTasks.CollectionChanged += OnAvailableTestTasksChanged;

            SaveConfigCommand = new DelegateCommand(() => SaveCurrentTaskConfig(), CanSaveConfig);
            ReloadConfigCommand = new DelegateCommand(
                    () => ReloadCurrentTaskConfig(),
                    () => HasPendingChanges && !IsConfigurationLocked && !string.IsNullOrEmpty(SelectedTestTask) && HasTestTaskOptions)
                .ObservesProperty(() => HasPendingChanges)
                .ObservesProperty(() => IsConfigurationLocked)
                .ObservesProperty(() => SelectedTestTask);

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
            }, () => !IsBusy && !IsDeviceConnecting)
                .ObservesProperty(() => IsBusy)
                .ObservesProperty(() => IsDeviceConnecting);

            _connectionStatus = "离线";
            _outputMode = DefaultOutputModeText;
        }

        /// <summary>
        /// 使用指定的设备初始化ViewModel
        /// </summary>
        public PXI7012_ROViewModel(DeviceBase device, string chassisName,
            IPxiChassisService pxiChassisService = null, IEventAggregator eventAggregator = null, ProjectService projectService = null) : this(projectService)
        {
            Device = device;
            ChassisName = chassisName;
            CardModel = device?.Model ?? "";
            CardName = !string.IsNullOrEmpty(device?.CardName) ? device.CardName : device?.Model ?? "";
            _pxiChassisService = pxiChassisService;
            _eventAggregator = eventAggregator;
            _projectService = projectService;

            // 如果驱动已经在 DriverFactory 中缓存，并且仍处于连接状态，则直接复用连接
            if (Device != null)
            {
                int slotIndex = (Device as PxiDeviceBase)?.SlotIndex ?? -1;
                var cachedDriver = DriverFactory.GetCachedDriver(Device.Id, slotIndex);
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
            _projectSavingToken = _eventAggregator?.GetEvent<ProjectSavingEvent>()?.Subscribe(OnProjectSaving);

            InitializeChannels();
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// 初始化通道信息（固定9个通道）
        /// </summary>
        private void InitializeChannels()
        {
            if (Channels != null)
            {
                foreach (var existing in Channels)
                {
                    existing.PropertyChanged -= OnChannelPropertyChanged;
                }
            }

            Channels.Clear();

            if (Device == null)
            {
                LoadChannelConfigsFromDevice();
                return;
            }

            // 可编程电阻设备 (ProgrammableResistorDevice)
            if (Device is ProgrammableResistorDevice resistorDevice)
            {
                int totalChannelCount = resistorDevice.ChannelCount; // 硬件总通道数
                int usedChannelCount = 6; // 实际使用的通道数（前6个）
                double minResistance = resistorDevice.MinResistance;
                double maxResistance = resistorDevice.MaxResistance;

                for (int i = 0; i < usedChannelCount; i++)
                {
                    var channel = new ResistanceChannelInfo
                    {
                        ChannelName = $"RO{i}",
                        ChannelIndex = i,
                        Offset = 0.000,
                        Resistance = 2.000,
                        CurrentResistance = 2.000,
                        TargetResistance = 2.000,
                        MinResistance = minResistance,
                        MaxResistance = maxResistance,
                        IsPathRelayClosed = false,  // 通路继电器：默认断开
                        IsShortCircuitClosed = false, // 短路继电器：默认断开
                        IsEnabled = true              // 通道默认使能
                    };

                    // 设置命令
                    // 读取/设置阻值需要板卡已连接
                    channel.GetResistanceCommand = new DelegateCommand(async () => await GetResistanceAsync(channel),
                        () => IsDeviceConnected).ObservesProperty(() => IsDeviceConnected);
                    channel.SetResistanceCommand = new DelegateCommand(async () => await SetResistanceAsync(channel),
                        () => IsDeviceConnected).ObservesProperty(() => IsDeviceConnected);

                    // 断路 / 短路开关：即使未连接也允许点击，以驱动 UI 动画和本地状态；
                    // 真正的继电器控制在方法内部再检查 IsDeviceConnected
                    channel.TogglePathRelayCommand = new DelegateCommand(async () => await TogglePathRelayAsync(channel));
                    channel.ToggleShortCircuitCommand = new DelegateCommand(async () => await ToggleShortCircuitAsync(channel));

                    channel.PropertyChanged += OnChannelPropertyChanged;
                    Channels.Add(channel);
                }
            }

            // 从设备的配置中加载测试任务及通道参数
            LoadChannelConfigsFromDevice();
        }

        /// <summary>
        /// 获取指定通道的电阻值
        /// </summary>
        private async Task GetResistanceAsync(ResistanceChannelInfo channel)
        {
            if (_driver == null || !IsDeviceConnected)
            {
                System.Diagnostics.Debug.WriteLine($"[ResistanceConfig] 获取阻值失败：驱动未连接");
                return;
            }

            try
            {
                // 读取通道电阻值
                var channelName = channel.ChannelName;
                var values = await _driver.ReadChannelsBatchAsync(new List<string> { channelName });
                
                if (values.TryGetValue(channelName, out double value))
                {
                    // 显示硬件读取的真实阻值（偏移值用于补偿设定时的写入值）
                    channel.CurrentResistance = value;
                    System.Diagnostics.Debug.WriteLine($"[ResistanceConfig] 获取 {channelName} 阻值: {channel.CurrentResistance:F4}Ω (设定值: {channel.TargetResistance:F4}Ω, 偏移: {channel.Offset:F4}Ω)");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ResistanceConfig] 获取 {channel.ChannelName} 阻值失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 设置指定通道的电阻值（保留命令入口，内部统一走 ApplyChannelResistanceAsync）
        /// </summary>
        private Task SetResistanceAsync(ResistanceChannelInfo channel)
        {
            return ApplyChannelResistanceAsync(channel);
        }

        /// <summary>
        /// 实际执行设置电阻的逻辑：
        /// - 仅在通道使能、未断路、未短路、设备已连接时生效
        /// - 实际写入值 = TargetResistance - Offset（偏移值用于补偿硬件误差）
        /// - 成功后立即获取真实硬件阻值更新显示
        /// </summary>
        private async Task ApplyChannelResistanceAsync(ResistanceChannelInfo channel)
        {
            if (_driver == null || !IsDeviceConnected)
            {
                System.Diagnostics.Debug.WriteLine($"[ResistanceConfig] 设置阻值失败：驱动未连接");
                return;
            }

            try
            {
                // 未使能的通道不下发阻值
                if (!channel.IsEnabled)
                {
                    System.Diagnostics.Debug.WriteLine($"[ResistanceConfig] 设置阻值跳过：{channel.ChannelName} 未使能");
                    return;
                }

                if (!channel.CanAdjustResistance)
                {
                    System.Diagnostics.Debug.WriteLine($"[ResistanceConfig] 设置阻值失败：{channel.ChannelName} 当前处于断路或短路状态");
                    ReMessageBox.Show(
                        $"{channel.ChannelName} 当前为断路或短路状态，请关闭断路开关并关闭短路开关后再设置阻值。",
                        "无法设置阻值",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Warning);
                    return;
                }

                // 从修改值框读取目标阻值
                double targetResistance = channel.TargetResistance;

                // 实际写入值 = 设定值 + 偏移值（偏移值用于补偿硬件误差）
                double actualResistance = targetResistance + channel.Offset;
                
                // 限制在有效范围内
                if (actualResistance < channel.MinResistance)
                    actualResistance = channel.MinResistance;
                if (actualResistance > channel.MaxResistance)
                    actualResistance = channel.MaxResistance;

                // 写入通道电阻值
                await _driver.WriteChannelAsync(channel.ChannelName, actualResistance);

                // 设置成功后，立即获取一次真实阻值更新显示
                await Task.Delay(100); // 等待硬件稳定
                await GetResistanceAsync(channel);

                System.Diagnostics.Debug.WriteLine($"[ResistanceConfig] 设置 {channel.ChannelName} 阻值: {targetResistance:F4}Ω (实际写入: {actualResistance:F4}Ω, 偏移: {channel.Offset:F4}Ω)");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ResistanceConfig] 设置 {channel.ChannelName} 阻值失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 切换通路继电器状态
        /// </summary>
        private async Task TogglePathRelayAsync(ResistanceChannelInfo channel)
        {
            if (_driver == null || !IsDeviceConnected)
            {
                System.Diagnostics.Debug.WriteLine($"[ResistanceConfig] 切换通路继电器失败：驱动未连接");
                return;
            }

            try
            {
                // 记录切换前状态，用于模式策略
                bool prevPath = channel.PreviousPathRelayClosed;
                bool prevShort = channel.PreviousShortCircuitClosed;

                await ApplyRelayWithModeAsync(channel, channel.IsPathRelayClosed, channel.IsShortCircuitClosed, prevPath, prevShort);
                System.Diagnostics.Debug.WriteLine($"[ResistanceConfig] {channel.ChannelName} 断路开关: {(channel.IsBreakEnabled ? "断开" : "导通")}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ResistanceConfig] 切换通路继电器失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 切换短路继电器状态
        /// </summary>
        private async Task ToggleShortCircuitAsync(ResistanceChannelInfo channel)
        {
            if (_driver == null || !IsDeviceConnected)
            {
                System.Diagnostics.Debug.WriteLine($"[ResistanceConfig] 切换短路继电器失败：驱动未连接");
                return;
            }

            try
            {
                bool prevPath = channel.PreviousPathRelayClosed;
                bool prevShort = channel.PreviousShortCircuitClosed;

                await ApplyRelayWithModeAsync(channel, channel.IsPathRelayClosed, channel.IsShortCircuitClosed, prevPath, prevShort);
                System.Diagnostics.Debug.WriteLine($"[ResistanceConfig] {channel.ChannelName} 短路继电器: {(channel.IsShortCircuitClosed ? "闭合" : "断开")}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ResistanceConfig] 切换短路继电器失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 按输出模式设置继电器状态
        /// </summary>
        private async Task ApplyRelayWithModeAsync(ResistanceChannelInfo channel, bool targetPathRelayClosed, bool targetShortCircuitClosed, bool previousPathRelayClosed, bool previousShortCircuitClosed)
        {
            if (_driver == null || !IsDeviceConnected) return;

            try
            {
                var mode = GetRelayMode();

                // 统一封装实际写继电器的动作
                async Task<bool> WriteAsync(bool path, bool @short)
                {
                    if (_driver is ACTS6010Driver acts6010Driver)
                    {
                        bool ok = await acts6010Driver.SetRelayStateAsync(channel.ChannelName, path, @short);
                        System.Diagnostics.Debug.WriteLine($"[ResistanceConfig] 模式[{mode}] 设置 {channel.ChannelName} 通路:{(path ? "闭合" : "断开")} 短路:{(@short ? "闭合" : "断开")} 结果:{(ok ? "成功" : "失败")}");
                        return ok;
                    }
                    System.Diagnostics.Debug.WriteLine($"[ResistanceConfig] 驱动不支持设置继电器状态");
                    return false;
                }

                switch (mode)
                {
                    case RelayOutputMode.BreakBeforeMake: // 先断后连
                        await WriteAsync(false, false);
                        await Task.Delay(RelaySettleTimeMs);
                        await WriteAsync(targetPathRelayClosed, targetShortCircuitClosed);
                        break;
                    case RelayOutputMode.MakeBeforeBreak: // 先通后断
                        // 先确保闭合目标涉及的继电器，再断开不需要的
                        bool prePath = targetPathRelayClosed || previousPathRelayClosed;
                        bool preShort = targetShortCircuitClosed || previousShortCircuitClosed;
                        await WriteAsync(prePath, preShort);
                        await Task.Delay(RelaySettleTimeMs);
                        await WriteAsync(targetPathRelayClosed, targetShortCircuitClosed);
                        break;
                    case RelayOutputMode.ImmediateWithWait: // 立即执行后等待建立时间
                        await WriteAsync(targetPathRelayClosed, targetShortCircuitClosed);
                        await Task.Delay(RelaySettleTimeMs);
                        break;
                    case RelayOutputMode.NoWait:
                    default:
                        await WriteAsync(targetPathRelayClosed, targetShortCircuitClosed);
                        break;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ResistanceConfig] 设置继电器状态失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 从设备加载已保存的通道配置
        /// </summary>
        private void LoadChannelConfigsFromDevice()
        {
            LoadTestTaskOptions();
        }

        private void LoadTestTaskOptions()
        {
            _isLoadingTaskOptions = true;
            try
            {
                AvailableTestTasks.Clear();
                var projectTasks = GetTestTaskNamesFromProject();
                foreach (var name in projectTasks)
                {
                    if (!string.IsNullOrWhiteSpace(name) && !AvailableTestTasks.Contains(name))
                    {
                        AvailableTestTasks.Add(name);
                    }
                }

                var ensuredConfig = EnsureResistanceOutputCardConfig();
                if (ensuredConfig != null)
                {
                    EnsureTaskConfigsExist(ensuredConfig, AvailableTestTasks);
                }

                var cardConfig = Device?.CardConfigData as ResistanceOutputCardConfig;
                if (cardConfig?.TestTaskConfigs != null)
                {
                    foreach (var config in cardConfig.TestTaskConfigs)
                    {
                        if (string.IsNullOrWhiteSpace(config?.TestTaskName))
                            continue;
                        if (!AvailableTestTasks.Contains(config.TestTaskName))
                        {
                            AvailableTestTasks.Add(config.TestTaskName);
                        }
                    }
                }

                if (AvailableTestTasks.Count == 0)
                {
                    AvailableTestTasks.Add(DefaultTestTaskName);
                }

                string initialTask = null;
                if (!string.IsNullOrWhiteSpace(cardConfig?.LastSelectedTestTask) &&
                    AvailableTestTasks.Contains(cardConfig.LastSelectedTestTask))
                {
                    initialTask = cardConfig.LastSelectedTestTask;
                }
                else
                {
                    initialTask = AvailableTestTasks.FirstOrDefault();
                }

                _selectedTestTask = initialTask;
                RaisePropertyChanged(nameof(SelectedTestTask));
                UpdateSaveReloadCanExecute();

                if (!string.IsNullOrWhiteSpace(initialTask))
                {
                    LoadConfigForTask(initialTask);
                }
                else
                {
                    HasPendingChanges = false;
                    RaisePropertyChanged(nameof(IsAllEnabled));
                }
            }
            finally
            {
                _isLoadingTaskOptions = false;
            }
        }

        private void EnsureTaskConfigsExist(ResistanceOutputCardConfig cardConfig, IEnumerable<string> taskNames)
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
            UpdateSaveReloadCanExecute();

            if (!_isLoadingTaskOptions && !string.IsNullOrWhiteSpace(taskName))
            {
                LoadConfigForTask(taskName);
            }
        }

        private bool EnsurePendingChangesHandled()
        {
            if (IsDeviceConnecting || IsDeviceConnected)
            {
                return true;
            }

            if (!HasPendingChanges || _isLoadingTaskOptions)
            {
                return true;
            }

            var message = string.IsNullOrEmpty(SelectedTestTask)
                ? "电阻输出配置尚未保存，是否现在保存？"
                : $"{CardName}\"{SelectedTestTask}\" 的配置尚未保存，是否保存？";

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

        private void LoadConfigForTask(string taskName)
        {
            if (string.IsNullOrWhiteSpace(taskName))
            {
                ApplyTaskConfig(null);
                return;
            }

            var cardConfig = EnsureResistanceOutputCardConfig();
            if (cardConfig == null)
            {
                HasPendingChanges = false;
                return;
            }

            var config = cardConfig.TestTaskConfigs
                .FirstOrDefault(c => string.Equals(c.TestTaskName, taskName, StringComparison.OrdinalIgnoreCase));

            if (config == null)
            {
                config = new ResistanceOutputTestTaskConfig
                {
                    TestTaskName = taskName,
                    OutputMode = DefaultOutputModeText
                };
                InitializeTaskConfigChannels(config);
                cardConfig.TestTaskConfigs.Add(config);
            }

            cardConfig.LastSelectedTestTask = taskName;
            ApplyTaskConfig(config);
        }



        private void ApplyTaskConfig(ResistanceOutputTestTaskConfig config)
        {
            _isApplyingTaskConfig = true;
            try
            {
                var modeToApply = string.IsNullOrWhiteSpace(config?.OutputMode)
                    ? DefaultOutputModeText
                    : config.OutputMode;
                OutputMode = modeToApply;

                if (Channels != null)
                {
                    foreach (var channel in Channels)
                    {
                        var saved = config?.Channels?.FirstOrDefault(c =>
                            string.Equals(c.ChannelName, channel.ChannelName, StringComparison.OrdinalIgnoreCase));
                        if (saved != null)
                        {
                            channel.IsEnabled = saved.IsEnabled;
                            channel.Offset = saved.Offset;
                            channel.TargetResistance = saved.TargetResistance;
                        }
                        else
                        {
                            channel.IsEnabled = false;
                            channel.Offset = 0.000;
                            channel.TargetResistance = 2.000;
                        }
                    }
                }
            }
            finally
            {
                _isApplyingTaskConfig = false;
            }

            RaisePropertyChanged(nameof(IsAllEnabled));
            HasPendingChanges = false;
        }

        private ResistanceOutputTestTaskConfig GetCurrentTaskConfig(string taskName)
        {
            var cardConfig = EnsureResistanceOutputCardConfig();
            if (cardConfig == null)
                return null;

            return cardConfig.TestTaskConfigs
                .FirstOrDefault(c => string.Equals(c.TestTaskName, taskName, StringComparison.OrdinalIgnoreCase));
        }

        private List<string> GetTestTaskNamesFromProject()
        {
            var result = new List<string>();

            var globalTasks = _projectService?.GetGlobalTestTaskNames();
            if (globalTasks != null && globalTasks.Count > 0)
            {
                return globalTasks;
            }

            if (_projectService?.CurrentProjectRoot?.Children == null || string.IsNullOrWhiteSpace(ChassisName))
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

        private ResistanceOutputCardConfig EnsureResistanceOutputCardConfig()
        {
            if (Device == null)
            {
                return null;
            }

            var cardConfig = Device.CardConfigData as ResistanceOutputCardConfig;
            if (cardConfig == null)
            {
                cardConfig = new ResistanceOutputCardConfig();
                Device.CardConfigData = cardConfig;
            }

            cardConfig.CardId = Device.Id;
            cardConfig.CardName = CardName;
            cardConfig.CardModel = CardModel;
            cardConfig.ChassisName = ChassisName;
            return cardConfig;
        }

        private ResistanceOutputTestTaskConfig GetOrCreateTaskConfig(ResistanceOutputCardConfig cardConfig, string taskName)
        {
            taskName ??= string.Empty;
            var config = cardConfig.TestTaskConfigs
                .FirstOrDefault(c => string.Equals(c.TestTaskName, taskName, StringComparison.OrdinalIgnoreCase));
            if (config == null)
            {
                config = new ResistanceOutputTestTaskConfig
                {
                    TestTaskName = taskName,
                    OutputMode = DefaultOutputModeText
                };
                InitializeTaskConfigChannels(config);
                cardConfig.TestTaskConfigs.Add(config);
            }

            return config;
        }

        private void UpdateTaskConfigChannels(ResistanceOutputTestTaskConfig config)
        {
            if (config == null)
                return;

            var lookup = config.Channels.ToDictionary(c => c.ChannelName, StringComparer.OrdinalIgnoreCase);
            foreach (var channel in Channels)
            {
                if (!lookup.TryGetValue(channel.ChannelName, out var saved))
                {
                    saved = new ResistanceChannelConfigData { ChannelName = channel.ChannelName };
                    config.Channels.Add(saved);
                }

                saved.IsEnabled = channel.IsEnabled;
                saved.Offset = channel.Offset;
                saved.TargetResistance = channel.TargetResistance;
            }

            for (int i = config.Channels.Count - 1; i >= 0; i--)
            {
                if (!Channels.Any(c => string.Equals(c.ChannelName, config.Channels[i].ChannelName, StringComparison.OrdinalIgnoreCase)))
                {
                    config.Channels.RemoveAt(i);
                }
            }
        }

        private void InitializeTaskConfigChannels(ResistanceOutputTestTaskConfig config)
        {
            if (config == null)
                return;

            if (config.Channels == null)
            {
                config.Channels = new ObservableCollection<ResistanceChannelConfigData>();
            }
            else
            {
                config.Channels.Clear();
            }

            if (Channels == null)
                return;

            foreach (var channel in Channels)
            {
                config.Channels.Add(new ResistanceChannelConfigData
                {
                    ChannelName = channel.ChannelName,
                    IsEnabled = false,
                    Offset = 0.000,
                    TargetResistance = 2.000
                });
            }
        }


        private bool SaveCurrentTaskConfig(bool showMessages = true)
        {
            if (!CanSaveConfig())
            {
                if (showMessages)
                {
                    ReMessageBox.Show(
                        "请先选择测试任务后再保存配置。",
                        "提示",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Information);
                }
                return false;
            }

            var cardConfig = EnsureResistanceOutputCardConfig();
            if (cardConfig == null)
            {
                return false;
            }

            var taskConfig = GetOrCreateTaskConfig(cardConfig, SelectedTestTask);
            taskConfig.OutputMode = OutputMode;
            UpdateTaskConfigChannels(taskConfig);
            cardConfig.LastSelectedTestTask = SelectedTestTask;

            SaveChannelConfigsToDevice(cardConfig);
            HasPendingChanges = false;
            var taskName = string.IsNullOrWhiteSpace(SelectedTestTask) ? DefaultTestTaskName : SelectedTestTask;
            if (showMessages)
            {
                ReMessageBox.Show(
                    $"\"{taskName}\" 的配置已保存。",
                    "保存成功",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
            }
            return true;
        }

        private void ReloadCurrentTaskConfig()
        {
            if (!CanReloadConfig())
            {
                ReMessageBox.Show(
                    "请选择测试任务后再读取配置。",
                    "提示",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
                return;
            }

            if (HasPendingChanges)
            {
                var confirm = ReMessageBox.Show(
                    $"将放弃当前对 \"{SelectedTestTask}\" 的修改，是否继续？",
                    "确认",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Question);
                if (confirm != System.Windows.MessageBoxResult.Yes)
                {
                    return;
                }
            }

            LoadConfigForTask(SelectedTestTask);
        }

        private bool CanSaveConfig()
        {
            return HasPendingChanges &&
                   !IsConfigurationLocked &&
                   HasTestTaskOptions &&
                   !string.IsNullOrWhiteSpace(SelectedTestTask);
        }

        private bool CanReloadConfig()
        {
            return !IsConfigurationLocked &&
                   HasTestTaskOptions &&
                   !string.IsNullOrWhiteSpace(SelectedTestTask);
        }

        private void MarkDirty()
        {
            if (_isApplyingTaskConfig)
                return;
            HasPendingChanges = true;
        }

        private void UpdateSaveReloadCanExecute()
        {
            (SaveConfigCommand as DelegateCommand)?.RaiseCanExecuteChanged();
            (ReloadConfigCommand as DelegateCommand)?.RaiseCanExecuteChanged();
        }

        private void OnAvailableTestTasksChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            RaisePropertyChanged(nameof(HasTestTaskOptions));
            UpdateSaveReloadCanExecute();
        }

        private void OnChannelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (_isApplyingTaskConfig)
                return;

            if (e.PropertyName == nameof(ResistanceChannelInfo.IsEnabled))
            {
                RaisePropertyChanged(nameof(IsAllEnabled));
                MarkDirty();
            }
            else if (e.PropertyName == nameof(ResistanceChannelInfo.Offset) ||
                     e.PropertyName == nameof(ResistanceChannelInfo.TargetResistance))
            {
                MarkDirty();
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

        private void OnProjectSaving()
        {
            if (_disposed)
            {
                return;
            }

            if (!HasPendingChanges || IsConfigurationLocked || string.IsNullOrWhiteSpace(SelectedTestTask))
            {
                return;
            }

            SaveCurrentTaskConfig(false);
        }

        /// <summary>
        /// 保存通道配置到设备
        /// </summary>
        private void SaveChannelConfigsToDevice(ResistanceOutputCardConfig cardConfig)
        {
            System.Diagnostics.Debug.WriteLine($"[ResistanceConfig] SaveChannelConfigsToDevice 开始...");
            if (Device == null || cardConfig == null)
            {
                System.Diagnostics.Debug.WriteLine($"[ResistanceConfig] Device 或 cardConfig 为空，跳过保存");
                return;
            }

            cardConfig.CardId = Device.Id;
            cardConfig.CardName = CardName;
            cardConfig.CardModel = CardModel;
            cardConfig.ChassisName = ChassisName;
            Device.CardConfigData = cardConfig;

            if (Device.CardName != CardName)
            {
                Device.CardName = CardName;
            }

            _pxiChassisService?.UpdateDeviceCardConfig(Device.Id, Device.CardConfigData);

            _eventAggregator?.GetEvent<ProjectModifiedEvent>()?.Publish(new ProjectModifiedEventArgs
            {
                ModificationType = "ResistanceOutputConfig",
                Description = "电阻输出通道配置已更新"
            });
        }

        /// <summary>
        /// 打开板卡 - 检测板卡是否在线
        /// </summary>
        private async Task OnOpenDeviceAsync()
        {
            if (Device == null) return;

            if (IsBusy)
            {
                return;
            }

            IsBusy = true;
            IsDeviceConnecting = true;

            try
            {
                ConnectionStatus = "检测中";
                
                // 创建驱动实例
                _driver = DriverFactory.CreateDriver(Device);
                
                // 连接设备（检测板卡）
                bool connected = await _driver.ConnectAsync();
                
                if (connected)
                {
                    IsDeviceConnected = true;
                    ConnectionStatus = "在线";
                    System.Diagnostics.Debug.WriteLine($"[ResistanceConfig] 板卡检测成功: {Device.Name}");
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
            catch (System.IO.FileNotFoundException ex)
            {
                HandleConnectionFailure(ex, "DLL文件未找到");
            }
            catch (System.DllNotFoundException ex)
            {
                HandleConnectionFailure(ex, "DLL加载失败");
            }
            catch (System.BadImageFormatException ex)
            {
                HandleConnectionFailure(ex, "DLL格式错误");
            }
            catch (InvalidOperationException ex)
            {
                HandleConnectionFailure(ex, "板卡连接失败");
            }
            catch (Exception ex)
            {
                HandleConnectionFailure(ex, $"板卡检测异常({ex.GetType().Name})");
                System.Diagnostics.Debug.WriteLine($"[ResistanceConfig] 异常堆栈: {ex.StackTrace}");
            }
            finally
            {
                IsDeviceConnecting = false;
                IsBusy = false;
            }
        }

        private void HandleConnectionFailure(Exception ex, string debugTag)
        {
            IsDeviceConnected = false;
            ConnectionStatus = "离线";
            _driver = null;

            ReMessageBox.Show(
                "板卡连接失败，请检查板卡位置及驱动。",
                "连接失败",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);

            System.Diagnostics.Debug.WriteLine($"[ResistanceConfig] {debugTag}: {ex.Message}");
        }

        /// <summary>
        /// 停止调试
        /// </summary>
        public async Task StopDebugAsync()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[ResistanceConfig] 停止调试: {Device?.Name}");

                // 停止 Timer
                StopReadTimer();

                // 停止并释放 Driver
                if (_driver != null)
                {
                    await _driver.StopAcquisitionAsync();
                    await _driver.DisconnectAsync();
                    _driver = null;
                }

                // 重置 UI 状态
                IsDeviceConnected = false;
                ConnectionStatus = "离线";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ResistanceConfig] 停止调试异常: {ex.Message}");
            }
        }

        private void StartReadTimer()
        {
            if (_readTimer == null)
            {
                _readTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(500)
                };
                //_readTimer.Tick += ReadTimer_Tick;
            }
            _readTimer.Start();
        }

        private void StopReadTimer()
        {
            _readTimer?.Stop();
        }

        //private async void ReadTimer_Tick(object sender, EventArgs e)
        //{
        //    // 定时器暂时不使用，改为手动获取阻值
        //}

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
            if (IsDeviceConnecting)
            {
                ReMessageBox.Show(
                    "正在打开板卡，请稍后再切换页面。",
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

        public bool CanClose()
        {
            if (IsDeviceConnecting)
            {
                ReMessageBox.Show(
                    "正在打开板卡，请稍后再切换页面。",
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

        #endregion

        #region IDisposable

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
                StopReadTimer();
                Task.Run(async () => await StopDebugAsync()).Wait(TimeSpan.FromSeconds(2));
                _availableTestTasks.CollectionChanged -= OnAvailableTestTasksChanged;
                if (Channels != null)
                {
                    foreach (var channel in Channels)
                    {
                        channel.PropertyChanged -= OnChannelPropertyChanged;
                    }
                }
                if (_projectModifiedToken != null)
                {
                    _eventAggregator?.GetEvent<ProjectModifiedEvent>()?.Unsubscribe(_projectModifiedToken);
                    _projectModifiedToken = null;
                }
                if (_projectSavingToken != null)
                {
                    _eventAggregator?.GetEvent<ProjectSavingEvent>()?.Unsubscribe(_projectSavingToken);
                    _projectSavingToken = null;
                }
            }

            _disposed = true;
        }

        #endregion
    }

    /// <summary>
    /// 配置加载结果
    /// </summary>
    public class ConfigLoadResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public ResistanceOutputTestTaskConfig ValidatedConfig { get; set; }
        public ResistanceOutputTestTaskConfig OriginalConfig { get; set; }
    }

    /// <summary>
    /// 配置加载操作
    /// </summary>
    public enum ConfigLoadAction
    {
        Apply,    // 直接应用
        Merge,    // 合并
        Cancel    // 取消
    }

    /// <summary>
    /// 电阻通道信息
    /// </summary>
    public class ResistanceChannelInfo : BindableBase
    {
        private string _channelName;
        private int _channelIndex;
        private double _offset;
        private double _resistance; // 保留用于兼容，但不再使用
        private double _currentResistance; // 当前阻值（从硬件读取，只读显示）
        private double _targetResistance; // 修改值（用户输入，用于设置）
        private double _minResistance;
        private double _maxResistance;
        private bool _isPathRelayClosed;
        private bool _isShortCircuitClosed;
        private bool _isEnabled = true;
        private bool _previousPathRelayClosed;
        private bool _previousShortCircuitClosed;

        public string ChannelName
        {
            get => _channelName;
            set => SetProperty(ref _channelName, value);
        }

        public int ChannelIndex
        {
            get => _channelIndex;
            set => SetProperty(ref _channelIndex, value);
        }

        /// <summary>
        /// 通道偏移值（Ω）
        /// </summary>
        public double Offset
        {
            get => _offset;
            set
            {
                if (SetProperty(ref _offset, value))
                {
                    RaisePropertyChanged(nameof(OffsetDisplay));
                }
            }
        }

        /// <summary>
        /// 通道偏移值显示（Ω）- 格式化为4位小数
        /// 偏移值本身无限制，但偏移值+设定值必须在 2-6700 范围内
        /// </summary>
        public string OffsetDisplay
        {
            get => Offset.ToString("F4");
            set
            {
                if (double.TryParse(value, out double result))
                {
                    // 偏移值本身无限制，但需要确保 偏移值 + 设定值 在有效范围内
                    double actualValue = result + TargetResistance;
                    
                    // 如果超出范围，以设定值为基准调整偏移值
                    if (actualValue < 2.0)
                    {
                        result = 2.0 - TargetResistance;
                    }
                    else if (actualValue > 6700.0)
                    {
                        result = 6700.0 - TargetResistance;
                    }
                    
                    Offset = result;
                }
            }
        }

        /// <summary>
        /// 通道阻值（Ω）- 保留用于兼容
        /// </summary>
        public double Resistance
        {
            get => _resistance;
            set
            {
                if (SetProperty(ref _resistance, value))
                {
                    RaisePropertyChanged(nameof(ResistanceDisplay));
                }
            }
        }

        /// <summary>
        /// 当前阻值（Ω）- 从硬件读取的值，只读显示
        /// </summary>
        public double CurrentResistance
        {
            get => _currentResistance;
            set
            {
                if (SetProperty(ref _currentResistance, value))
                {
                    RaisePropertyChanged(nameof(CurrentResistanceDisplay));
                }
            }
        }

        /// <summary>
        /// 修改值（Ω）- 用户输入的目标阻值，用于设置
        /// </summary>
        public double TargetResistance
        {
            get => _targetResistance;
            set
            {
                if (SetProperty(ref _targetResistance, value))
                {
                    RaisePropertyChanged(nameof(TargetResistanceDisplay));
                }
            }
        }

        /// <summary>
        /// 修改值显示（Ω）- 格式化为4位小数
        /// 设定值范围：2-6700，且偏移值+设定值必须在 2-6700 范围内
        /// </summary>
        public string TargetResistanceDisplay
        {
            get => TargetResistance.ToString("F4");
            set
            {
                if (double.TryParse(value, out double result))
                {
                    // 限制设定值范围：2-6700
                    if (result < 2.0)
                    {
                        result = 2.0;
                    }
                    else if (result > 6700.0)
                    {
                        result = 6700.0;
                    }

                    // 第二点：偏移值不做大小限制，但偏移值 + 设定值 必须在 [2,6700]
                    // 以设定值为基准，当二者相加越界时，回推偏移值。
                    double desiredOffset = Offset;
                    double actualValue = desiredOffset + result;
                    if (actualValue < 2.0)
                    {
                        desiredOffset = 2.0 - result;
                    }
                    else if (actualValue > 6700.0)
                    {
                        desiredOffset = 6700.0 - result;
                    }

                    TargetResistance = result;

                    if (Math.Abs(Offset - desiredOffset) > 1e-9)
                    {
                        Offset = desiredOffset;
                    }
                }
            }
        }

        public double MinResistance
        {
            get => _minResistance;
            set => SetProperty(ref _minResistance, value);
        }

        public double MaxResistance
        {
            get => _maxResistance;
            set => SetProperty(ref _maxResistance, value);
        }

        /// <summary>
        /// 通路继电器是否闭合
        /// </summary>
        public bool IsPathRelayClosed
        {
            get => _isPathRelayClosed;
            set
            {
                _previousPathRelayClosed = _isPathRelayClosed;
                if (SetProperty(ref _isPathRelayClosed, value))
                {
                    RaisePropertyChanged(nameof(IsBreakEnabled));
                    RaisePropertyChanged(nameof(CanAdjustResistance));
                    RaisePropertyChanged(nameof(ResistanceDisplay));
                    RaisePropertyChanged(nameof(CurrentResistanceDisplay));
                }
            }
        }

        /// <summary>
        /// 是否处于断路状态（true 表示断路）
        /// </summary>
        public bool IsBreakEnabled
        {
            get => !IsPathRelayClosed;
            set => IsPathRelayClosed = !value;
        }

        /// <summary>
        /// 短路继电器是否闭合
        /// </summary>
        public bool IsShortCircuitClosed
        {
            get => _isShortCircuitClosed;
            set
            {
                _previousShortCircuitClosed = _isShortCircuitClosed;
                if (SetProperty(ref _isShortCircuitClosed, value))
                {
                    RaisePropertyChanged(nameof(CanAdjustResistance));
                    RaisePropertyChanged(nameof(ResistanceDisplay));
                    RaisePropertyChanged(nameof(CurrentResistanceDisplay));
                }
            }
        }

        /// <summary>
        /// 是否允许调节阻值（通路闭合且未短路）
        /// </summary>
        public bool CanAdjustResistance => IsPathRelayClosed && !IsShortCircuitClosed;

        /// <summary>
        /// 通道是否使能（左侧配置中的使能开关）
        /// </summary>
        public bool IsEnabled
        {
            get => _isEnabled;
            set => SetProperty(ref _isEnabled, value);
        }

        /// <summary>
        /// 上一次通路继电器状态（用于先通后断/先断后连策略）
        /// </summary>
        public bool PreviousPathRelayClosed => _previousPathRelayClosed;

        /// <summary>
        /// 上一次短路继电器状态（用于先通后断/先断后连策略）
        /// </summary>
        public bool PreviousShortCircuitClosed => _previousShortCircuitClosed;

        /// <summary>
        /// 阻值显示：断路=OPEN，短路=SHORT，正常显示数值（保留用于兼容）
        /// </summary>
        public string ResistanceDisplay
        {
            get
            {
                if (IsBreakEnabled)
                    return "OPEN";
                if (IsShortCircuitClosed)
                    return "SHORT";
                return Resistance.ToString("F4");
            }
            set
            {
                // 允许用户在正常状态下直接输入数值
                if (double.TryParse(value, out var val))
                {
                    Resistance = val;
                }
            }
        }

        /// <summary>
        /// 当前阻值显示：断路=OPEN，短路=SHORT，正常显示数值
        /// </summary>
        public string CurrentResistanceDisplay
        {
            get
            {
                if (IsBreakEnabled)
                    return "OPEN";
                if (IsShortCircuitClosed)
                    return "SHORT";
                return CurrentResistance.ToString("F4");
            }
        }

        /// <summary>
        /// 获取阻值命令
        /// </summary>
        public ICommand GetResistanceCommand { get; set; }

        /// <summary>
        /// 设置阻值命令
        /// </summary>
        public ICommand SetResistanceCommand { get; set; }

        /// <summary>
        /// 切换通路继电器命令
        /// </summary>
        public ICommand TogglePathRelayCommand { get; set; }

        /// <summary>
        /// 切换短路继电器命令
        /// </summary>
        public ICommand ToggleShortCircuitCommand { get; set; }
    }

}
