using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using MeasureControl.Drivers;
using MeasureControl.Helpers;
using MeasureControl.Models;
using MeasureControl.Models.Devices;
using MeasureControl.Services;
using Prism.Commands;
using Prism.Mvvm;
using Prism.Regions;
using MeasureControl.Views.Dialogs;

namespace MeasureControl.ViewModels.TestTask.CardCATPanel
{
    public class MTX970_LVDSViewModel : BindableBase, IConfirmNavigationRequest, ICloseGuard
    {
        private const string DefaultTaskName = "默认";
        private const string FixedDevConditionModel = "*";
        private const string FixedDevConditionId = "*";
        private const string FixedDevConditionPxiSlot = "7";

        private readonly SemaphoreSlim _driverStateSyncLock = new SemaphoreSlim(1, 1);

        private LvdsDevice _device;
        private string _chassisName;
        private MTX970LvdsDriver _driver;
        private ProjectService _projectService;

        private bool _isConnected;
        private bool _isConfigurationLocked;
        private bool _isBusy;
        private bool _isTesting;
        private string _connectionStatus = "离线";

        private readonly ObservableCollection<string> _availableTestTasks = new ObservableCollection<string>();
        private string _selectedTestTask;
        private bool _isApplyingTaskConfig;
        private bool _hasPendingChanges;

        private bool _configOsc = true;
        private bool _staticCount;
        private string _clockFrequencyText = "45M";
        private string _lvdsDataSampleWrText = "1234";
        private string _patternMatchText = "1234";
        private string _numSamplesText = "100";

        private string _devConditionModel = FixedDevConditionModel;
        private string _devConditionId = FixedDevConditionId;
        private string _devConditionPxiSlot = FixedDevConditionPxiSlot;

        private string _indexOfElementText = "-1";
        private string _triggerSampleLocationText = "0";
        private ObservableCollection<ushort> _arrayItems = new ObservableCollection<ushort>();
        private string _arrayCountText = "总计：0 个项目";

        public MTX970_LVDSViewModel()
        {
            _devConditionModel = FixedDevConditionModel;
            _devConditionId = FixedDevConditionId;
            _devConditionPxiSlot = FixedDevConditionPxiSlot;

            SaveConfigCommand = new DelegateCommand(
                    () => SaveCurrentTaskConfig(),
                    () => HasPendingChanges && CanEditConfiguration && HasTestTaskOptions && !string.IsNullOrWhiteSpace(SelectedTestTask))
                .ObservesProperty(() => HasPendingChanges)
                .ObservesProperty(() => CanEditConfiguration)
                .ObservesProperty(() => SelectedTestTask);

            LoadConfigCommand = new DelegateCommand(
                    () => ReloadCurrentTaskConfig(),
                    () => CanEditConfiguration && HasTestTaskOptions && !string.IsNullOrWhiteSpace(SelectedTestTask))
                .ObservesProperty(() => CanEditConfiguration)
                .ObservesProperty(() => SelectedTestTask);

            ToggleDeviceCommand = new DelegateCommand(async () => await ToggleConnectionAsync(), () => !IsBusy)
                .ObservesProperty(() => IsBusy);

            RunLoopbackCommand = new DelegateCommand(async () => await RunLoopbackAsync(), () => CanRun)
                .ObservesProperty(() => CanRun);

            ClockFrequencyUpCommand = new DelegateCommand(() => AdjustClockFrequency(1000000), () => CanEditConfiguration)
                .ObservesProperty(() => CanEditConfiguration);
            ClockFrequencyDownCommand = new DelegateCommand(() => AdjustClockFrequency(-1000000), () => CanEditConfiguration)
                .ObservesProperty(() => CanEditConfiguration);

            LvdsDataSampleWrUpCommand = new DelegateCommand(() => AdjustUShortText(ref _lvdsDataSampleWrText, 1, nameof(LvdsDataSampleWrText)), () => CanEditConfiguration)
                .ObservesProperty(() => CanEditConfiguration);
            LvdsDataSampleWrDownCommand = new DelegateCommand(() => AdjustUShortText(ref _lvdsDataSampleWrText, -1, nameof(LvdsDataSampleWrText)), () => CanEditConfiguration)
                .ObservesProperty(() => CanEditConfiguration);

            PatternMatchUpCommand = new DelegateCommand(() => AdjustUShortText(ref _patternMatchText, 1, nameof(PatternMatchText)), () => CanEditConfiguration)
                .ObservesProperty(() => CanEditConfiguration);
            PatternMatchDownCommand = new DelegateCommand(() => AdjustUShortText(ref _patternMatchText, -1, nameof(PatternMatchText)), () => CanEditConfiguration)
                .ObservesProperty(() => CanEditConfiguration);

            NumSamplesUpCommand = new DelegateCommand(() => AdjustUShortText(ref _numSamplesText, 10, nameof(NumSamplesText)), () => CanEditConfiguration)
                .ObservesProperty(() => CanEditConfiguration);
            NumSamplesDownCommand = new DelegateCommand(() => AdjustUShortText(ref _numSamplesText, -10, nameof(NumSamplesText)), () => CanEditConfiguration)
                .ObservesProperty(() => CanEditConfiguration);

            LoadTestTaskOptions();
            SelectedTestTask = AvailableTestTasks.FirstOrDefault();
        }

        public MTX970_LVDSViewModel(DeviceBase device, string chassisName, ProjectService projectService = null) : this()
        {
            _device = device as LvdsDevice;
            _chassisName = chassisName;
            _projectService = projectService;

            if (_device != null)
            {
                Model = _device.Model;
                CardName = !string.IsNullOrEmpty(_device.CardName) ? _device.CardName : _device.Model;

                var cached = DriverFactory.GetCachedDriver(_device.Id) as MTX970LvdsDriver;
                if (cached != null)
                {
                    _driver = cached;
                    if (_driver.IsConnected)
                    {
                        IsConnected = true;
                    }
                }
            }

            LoadTestTaskOptions();
            if (!string.IsNullOrWhiteSpace(SelectedTestTask))
            {
                _ = LoadSelectedTaskConfigAsync();
            }
        }

        public DeviceBase Device => _device;

        public string ChassisName
        {
            get => _chassisName;
            set => SetProperty(ref _chassisName, value);
        }

        private string _model;
        public string Model
        {
            get => _model;
            set => SetProperty(ref _model, value);
        }

        private string _cardName;
        public string CardName
        {
            get => _cardName;
            set
            {
                if (SetProperty(ref _cardName, value) && _device != null)
                {
                    _device.CardName = value;
                }
            }
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
                    SaveConfigCommand?.RaiseCanExecuteChanged();
                    LoadConfigCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        public bool IsConnected
        {
            get => _isConnected;
            private set
            {
                if (SetProperty(ref _isConnected, value))
                {
                    RaisePropertyChanged(nameof(CanRun));
                    RaisePropertyChanged(nameof(ConnectButtonText));
                }
            }
        }

        public string ConnectionStatus
        {
            get => _connectionStatus;
            private set => SetProperty(ref _connectionStatus, value);
        }

        /// <summary>
        /// 配置是否被锁定（连接中或测试中锁定，成功/失败后解除）
        /// </summary>
        public bool IsConfigurationLocked
        {
            get => _isConfigurationLocked;
            private set
            {
                if (SetProperty(ref _isConfigurationLocked, value))
                {
                    RaisePropertyChanged(nameof(CanEditConfiguration));
                    RaisePropertyChanged(nameof(CanRun));
                }
            }
        }

        private void UpdateConfigurationLock()
        {
            IsConfigurationLocked = IsBusy || IsTesting;
        }

        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (SetProperty(ref _isBusy, value))
                {
                    UpdateConfigurationLock();
                    RaisePropertyChanged(nameof(CanEditConfiguration));
                    RaisePropertyChanged(nameof(CanRun));
                    RaisePropertyChanged(nameof(IsLeftConfigLocked));
                }
            }
        }

        public bool IsTesting
        {
            get => _isTesting;
            private set
            {
                if (SetProperty(ref _isTesting, value))
                {
                    UpdateConfigurationLock();
                    RaisePropertyChanged(nameof(CanRun));
                    RaisePropertyChanged(nameof(CanEditConfiguration));
                    RaisePropertyChanged(nameof(IsLeftConfigLocked));
                }
            }
        }

        public bool CanEditConfiguration => !IsConfigurationLocked;

        public bool IsLeftConfigLocked => IsTesting;

        public bool CanRun => IsConnected && !IsBusy && !IsTesting;

        public string ConnectButtonText => IsConnected ? "关闭板卡" : "打开板卡";

        public bool ConfigOsc
        {
            get => _configOsc;
            set
            {
                if (SetProperty(ref _configOsc, value))
                {
                    MarkDirty();
                }
            }
        }

        public bool StaticCount
        {
            get => _staticCount;
            set
            {
                if (SetProperty(ref _staticCount, value))
                {
                    MarkDirty();
                }
            }
        }

        public string ClockFrequencyText
        {
            get => _clockFrequencyText;
            set
            {
                if (SetProperty(ref _clockFrequencyText, value))
                {
                    MarkDirty();
                }
            }
        }

        public string LvdsDataSampleWrText
        {
            get => _lvdsDataSampleWrText;
            set
            {
                if (SetProperty(ref _lvdsDataSampleWrText, value))
                {
                    MarkDirty();
                }
            }
        }

        public string PatternMatchText
        {
            get => _patternMatchText;
            set
            {
                if (SetProperty(ref _patternMatchText, value))
                {
                    MarkDirty();
                }
            }
        }

        public string NumSamplesText
        {
            get => _numSamplesText;
            set
            {
                if (SetProperty(ref _numSamplesText, value))
                {
                    MarkDirty();
                }
            }
        }

        public string DevConditionModel
        {
            get => _devConditionModel;
            private set => SetProperty(ref _devConditionModel, value);
        }

        public string DevConditionId
        {
            get => _devConditionId;
            private set => SetProperty(ref _devConditionId, value);
        }

        public string DevConditionPxiSlot
        {
            get => _devConditionPxiSlot;
            private set => SetProperty(ref _devConditionPxiSlot, value);
        }

        public string IndexOfElementText
        {
            get => _indexOfElementText;
            private set => SetProperty(ref _indexOfElementText, value);
        }

        public string TriggerSampleLocationText
        {
            get => _triggerSampleLocationText;
            private set => SetProperty(ref _triggerSampleLocationText, value);
        }

        public ObservableCollection<ushort> ArrayItems
        {
            get => _arrayItems;
            private set => SetProperty(ref _arrayItems, value);
        }

        public string ArrayCountText
        {
            get => _arrayCountText;
            private set => SetProperty(ref _arrayCountText, value);
        }

        public DelegateCommand SaveConfigCommand { get; }
        public DelegateCommand LoadConfigCommand { get; }
        public DelegateCommand ToggleDeviceCommand { get; }
        public DelegateCommand RunLoopbackCommand { get; }

        public DelegateCommand ClockFrequencyUpCommand { get; }
        public DelegateCommand ClockFrequencyDownCommand { get; }
        public DelegateCommand LvdsDataSampleWrUpCommand { get; }
        public DelegateCommand LvdsDataSampleWrDownCommand { get; }
        public DelegateCommand PatternMatchUpCommand { get; }
        public DelegateCommand PatternMatchDownCommand { get; }
        public DelegateCommand NumSamplesUpCommand { get; }
        public DelegateCommand NumSamplesDownCommand { get; }

        private void MarkDirty()
        {
            if (!_isApplyingTaskConfig)
            {
                HasPendingChanges = true;
            }
        }

        private void ChangeSelectedTestTask(string taskName)
        {
            if (_selectedTestTask == taskName)
            {
                return;
            }

            if (!_isApplyingTaskConfig)
            {
                if (!EnsurePendingChangesHandled())
                {
                    RaisePropertyChanged(nameof(SelectedTestTask));
                    return;
                }
            }

            _selectedTestTask = taskName;
            RaisePropertyChanged(nameof(SelectedTestTask));
            RaisePropertyChanged(nameof(CanEditConfiguration));

            if (!_isApplyingTaskConfig)
            {
                LoadConfigForTask(taskName);
            }
        }

        private Task LoadSelectedTaskConfigAsync()
        {
            if (string.IsNullOrWhiteSpace(SelectedTestTask))
                return Task.CompletedTask;

            LoadConfigForTask(SelectedTestTask);
            return Task.CompletedTask;
        }

        private void ReloadCurrentTaskConfig()
        {
            if (string.IsNullOrWhiteSpace(SelectedTestTask))
            {
                ReMessageBox.Show(
                    "请选择一个测试任务再读取配置",
                    "提示",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            if (HasPendingChanges)
            {
                var result = ReMessageBox.Show(
                    $"读取配置会覆盖对 \"{SelectedTestTask}\" 的修改，是否继续？",
                    "提示",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                if (result != MessageBoxResult.Yes)
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

        private void LoadTestTaskOptions()
        {
            _availableTestTasks.Clear();

            var tasks = GetTestTaskNamesFromProject();
            foreach (var t in tasks)
            {
                if (!string.IsNullOrWhiteSpace(t) && !_availableTestTasks.Contains(t))
                {
                    _availableTestTasks.Add(t);
                }
            }

            // 兼容：工程未配置测试任务时，提供一个默认项避免 UI 空白
            if (_availableTestTasks.Count == 0)
            {
                _availableTestTasks.Add(DefaultTaskName);
            }

            string initialTask = null;
            if (_device?.CardConfigData is LvdsCardConfig cardConfig &&
                !string.IsNullOrEmpty(cardConfig.LastSelectedTestTask) &&
                _availableTestTasks.Contains(cardConfig.LastSelectedTestTask))
            {
                initialTask = cardConfig.LastSelectedTestTask;
            }
            else
            {
                initialTask = _availableTestTasks.FirstOrDefault();
            }

            _selectedTestTask = initialTask;
            RaisePropertyChanged(nameof(SelectedTestTask));
        }

        private LvdsCardConfig EnsureLvdsCardConfig()
        {
            if (_device == null)
            {
                return null;
            }

            if (_device.CardConfigData is LvdsCardConfig existing)
            {
                if (!string.IsNullOrEmpty(_device.Id)) existing.CardId = _device.Id;
                if (!string.IsNullOrEmpty(_device.Model)) existing.CardModel = _device.Model;
                existing.CardName = _device.CardName;
                existing.ChassisName = ChassisName;
                return existing;
            }

            var cfg = new LvdsCardConfig
            {
                CardId = _device.Id,
                CardModel = _device.Model,
                CardName = _device.CardName,
                ChassisName = ChassisName
            };
            _device.CardConfigData = cfg;
            return cfg;
        }

        private Mtx970LvdsTestTaskConfig GetOrCreateTaskConfig(LvdsCardConfig cardConfig, string taskName)
        {
            taskName ??= string.Empty;
            var config = cardConfig?.TestTaskConfigs?.FirstOrDefault(c => c.TestTaskName == taskName);
            if (config == null)
            {
                config = new Mtx970LvdsTestTaskConfig
                {
                    TestTaskName = taskName,
                    ConfigOsc = ConfigOsc,
                    StaticCount = StaticCount,
                    ClockFrequencyText = ClockFrequencyText,
                    LvdsDataSampleWr = LvdsDataSampleWrText,
                    PatternMatch = PatternMatchText,
                    NumSamples = NumSamplesText
                };
                cardConfig?.TestTaskConfigs?.Add(config);
            }
            return config;
        }

        private void SaveToTaskConfig(Mtx970LvdsTestTaskConfig config)
        {
            if (config == null) return;

            config.ConfigOsc = ConfigOsc;
            config.StaticCount = StaticCount;
            config.ClockFrequencyText = ClockFrequencyText ?? string.Empty;
            config.LvdsDataSampleWr = LvdsDataSampleWrText ?? string.Empty;
            config.PatternMatch = PatternMatchText ?? string.Empty;
            config.NumSamples = NumSamplesText ?? string.Empty;
        }

        private void ApplyConfig(Mtx970LvdsTestTaskConfig config)
        {
            if (config == null) return;

            _isApplyingTaskConfig = true;
            try
            {
                ConfigOsc = config.ConfigOsc;
                StaticCount = config.StaticCount;

                ClockFrequencyText = config.ClockFrequencyText ?? string.Empty;
                LvdsDataSampleWrText = config.LvdsDataSampleWr ?? string.Empty;
                PatternMatchText = config.PatternMatch ?? string.Empty;
                NumSamplesText = config.NumSamples ?? string.Empty;

                DevConditionModel = FixedDevConditionModel;
                DevConditionId = FixedDevConditionId;
                DevConditionPxiSlot = FixedDevConditionPxiSlot;
            }
            finally
            {
                _isApplyingTaskConfig = false;
            }
        }

        private void LoadConfigForTask(string taskName)
        {
            var cardConfig = EnsureLvdsCardConfig();
            if (cardConfig == null)
            {
                return;
            }

            var cfg = GetOrCreateTaskConfig(cardConfig, taskName ?? string.Empty);
            cardConfig.LastSelectedTestTask = taskName;
            ApplyConfig(cfg);
            HasPendingChanges = false;
        }

        private bool SaveCurrentTaskConfig(bool showMessages = true)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(SelectedTestTask))
                {
                    if (showMessages)
                    {
                        ReMessageBox.Show(
                            "请选择一个测试任务再保存配置",
                            "提示",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    }
                    return false;
                }

                var cardConfig = EnsureLvdsCardConfig();
                if (cardConfig == null)
                {
                    return false;
                }

                var taskCfg = GetOrCreateTaskConfig(cardConfig, SelectedTestTask);
                SaveToTaskConfig(taskCfg);
                cardConfig.LastSelectedTestTask = SelectedTestTask;

                HasPendingChanges = false;
                if (showMessages)
                {
                    ReMessageBox.Show(
                        "配置已保存",
                        "提示",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                return true;
            }
            catch (Exception ex)
            {
                ReMessageBox.Show($"保存配置失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private bool EnsurePendingChangesHandled()
        {
            if (!HasPendingChanges)
            {
                return true;
            }

            var message = string.IsNullOrWhiteSpace(SelectedTestTask)
                ? "配置尚未保存，是否现在保存？"
                : $"\"{SelectedTestTask}\" 的配置尚未保存，是否保存？";

            var result = ReMessageBox.Show(
                message,
                "提示",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                return SaveCurrentTaskConfig();
            }

            if (result == MessageBoxResult.No)
            {
                HasPendingChanges = false;
                return true;
            }

            return false;
        }

        private async Task ToggleConnectionAsync()
        {
            if (_device == null)
            {
                ReMessageBox.Show("未找到设备上下文(DataContext)", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (IsBusy || IsTesting)
            {
                ReMessageBox.Show(
                    "正在采集中，无法关闭板卡，请等待采集完成。",
                    "提示",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            await _driverStateSyncLock.WaitAsync();
            try
            {
                if (IsConnected)
                {
                    IsBusy = true;
                    ConnectionStatus = "断开中";
                    try
                    {
                        if (_driver != null)
                        {
                            await _driver.DisconnectAsync();
                        }
                    }
                    finally
                    {
                        IsConnected = false;
                        ConnectionStatus = "离线";
                        IsBusy = false;
                    }

                    return;
                }

                IsBusy = true;
                ConnectionStatus = "检测中";
                try
                {
                    _driver ??= DriverFactory.CreateDriver(_device) as MTX970LvdsDriver ?? new MTX970LvdsDriver(_device);

                    bool ok = await _driver.ConnectAsync();
                    if (!ok)
                    {
                        ReMessageBox.Show("连接 SharedLib.dll 失败，请检查：\n1. DLL 是否已复制到输出目录\n2. LabVIEW Runtime 是否已安装\n3. DLL 位数是否匹配（32/64位）", "连接失败", MessageBoxButton.OK, MessageBoxImage.Error);
                        IsConnected = false;
                        ConnectionStatus = "离线";
                        return;
                    }

                    IsConnected = true;
                    ConnectionStatus = "在线";
                }
                finally
                {
                    IsBusy = false;
                }
            }
            finally
            {
                _driverStateSyncLock.Release();
            }
        }

        private async Task RunLoopbackAsync()
        {
            try
            {
                if (!IsConnected || _driver == null)
                {
                    ReMessageBox.Show("请先连接设备", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                double clockHz = ParseFrequencyHz(ClockFrequencyText);
                if (clockHz <= 0)
                {
                    ReMessageBox.Show("时钟频率无效", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                if (!ushort.TryParse(LvdsDataSampleWrText, out ushort lvdsDataSampleWr))
                {
                    ReMessageBox.Show("LVDS数据采样写值无效", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                if (!ushort.TryParse(PatternMatchText, out ushort patternMatch))
                {
                    ReMessageBox.Show("模式匹配值无效", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                if (!ushort.TryParse(NumSamplesText, out ushort numSamples) || numSamples == 0)
                {
                    ReMessageBox.Show("采样数量无效", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                IsBusy = true;
                IsTesting = true;
                try
                {
                    var result = await _driver.RunLoopbackAsync(
                        configOsc: ConfigOsc,
                        clockFrequencyHz: clockHz,
                        staticTCountUpF: StaticCount,
                        lvdsDataSampleWr: lvdsDataSampleWr,
                        patternMatch: patternMatch,
                        numSamples: numSamples,
                        devConditionModel: DevConditionModel ?? string.Empty,
                        devConditionId: DevConditionId ?? string.Empty,
                        devConditionPxiSlot: DevConditionPxiSlot ?? string.Empty);

                    IndexOfElementText = result.IndexOfElement.ToString();
                    TriggerSampleLocationText = result.TriggerSampleLocation.ToString();

                    ArrayItems = new ObservableCollection<ushort>(result.ArrayWSubsetDeleted ?? Array.Empty<ushort>());
                    ArrayCountText = $"总计：{ArrayItems.Count} 个项目";
                }
                finally
                {
                    IsTesting = false;
                    IsBusy = false;
                }
            }
            catch (Exception ex)
            {
                ReMessageBox.Show($"运行失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AdjustClockFrequency(double deltaHz)
        {
            if (string.IsNullOrWhiteSpace(ClockFrequencyText))
            {
                return;
            }

            string original = ClockFrequencyText.Trim();
            if (!TryParseFrequencyWithUnit(original, out double value, out char unit))
            {
                return;
            }

            double unitMultiplier = GetUnitMultiplier(unit);
            if (unitMultiplier <= 0)
            {
                unitMultiplier = 1;
                unit = '\0';
            }

            // 保持原单位增减（例如 45M -> 46M）
            double deltaInUnit = deltaHz / unitMultiplier;
            value += deltaInUnit;
            if (value < 0) value = 0;

            string formatted = Math.Round(value).ToString("0");
            ClockFrequencyText = unit == '\0' ? formatted : (formatted + char.ToUpperInvariant(unit));
        }

        private static bool TryParseFrequencyWithUnit(string text, out double value, out char unit)
        {
            value = 0;
            unit = '\0';

            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            string s = text.Trim();
            char last = s[s.Length - 1];
            if (last == 'k' || last == 'K' || last == 'm' || last == 'M' || last == 'g' || last == 'G')
            {
                unit = last;
                s = s.Substring(0, s.Length - 1);
            }

            return double.TryParse(s.Trim(), out value);
        }

        private static double GetUnitMultiplier(char unit)
        {
            if (unit == 'k' || unit == 'K') return 1e3;
            if (unit == 'm' || unit == 'M') return 1e6;
            if (unit == 'g' || unit == 'G') return 1e9;
            return 1;
        }

        private void AdjustUShortText(ref string backingField, int delta, string propertyName)
        {
            if (!int.TryParse(backingField, out int val))
            {
                return;
            }

            val += delta;
            if (val < 0) val = 0;
            if (val > 65535) val = 65535;

            backingField = val.ToString();
            RaisePropertyChanged(propertyName);
            MarkDirty();
        }

        private static double ParseFrequencyHz(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;

            string s = text.Trim();
            double multiplier = 1;

            char last = s[s.Length - 1];
            if (last == 'k' || last == 'K')
            {
                multiplier = 1e3;
                s = s.Substring(0, s.Length - 1);
            }
            else if (last == 'm' || last == 'M')
            {
                multiplier = 1e6;
                s = s.Substring(0, s.Length - 1);
            }
            else if (last == 'g' || last == 'G')
            {
                multiplier = 1e9;
                s = s.Substring(0, s.Length - 1);
            }

            if (!double.TryParse(s, out double val)) return 0;
            return val * multiplier;
        }

        public void ConfirmNavigationRequest(NavigationContext navigationContext, Action<bool> continuationCallback)
        {
            if (IsBusy)
            {
                var opText = IsConnected ? "关闭" : "打开";
                ReMessageBox.Show(
                    $"正在{opText}板卡，请稍候...",
                    "提示",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                continuationCallback(false);
                return;
            }

            if (IsConnected)
            {
                continuationCallback(true);
                return;
            }

            continuationCallback(EnsurePendingChangesHandled());
        }

        public bool CanClose()
        {
            if (IsBusy)
            {
                var opText = IsConnected ? "关闭" : "打开";
                ReMessageBox.Show(
                    $"正在{opText}板卡，请稍候...",
                    "提示",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return false;
            }

            if (IsConnected)
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

        // Task config model moved to MeasureControl.Models (Mtx970LvdsTestTaskConfig) and persisted via project JSON.
    }
}
