using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
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
using MeasureControl.Constants;
using MeasureControl.Helpers;
using MeasureControl.Views.Dialogs;
using MeasureControl.Views.TestTask.CardCATPanel.PXIe7131;
using System.Diagnostics;

namespace MeasureControl.ViewModels.TestTask.CardCATPanel
{
    /// <summary>
    /// 离散量通道配置面板的ViewModel - 用于配置离散量输入输出板卡
    /// </summary>
    public class PXIe7131_DIDOViewModel : BindableBase, IDisposable, ICloseGuard, IConfirmNavigationRequest
    {
        
        private DeviceBase _device;
        private string _chassisName;
        private string _cardModel;
        private string _cardName;
        private ObservableCollection<DiscreteChannelInfo> _inputChannels;
        private ObservableCollection<DiscreteChannelInfo> _outputChannels;
        private ObservableCollection<DiscreteChannelRow> _channelRows;
        private ObservableCollection<DiscreteStatusItem> _inputStatusItems;
        private ObservableCollection<DiscreteStatusItem> _outputStatusItems;
        private readonly IPxiChassisService _pxiChassisService;
        private readonly IEventAggregator _eventAggregator;
        private bool _isInputAllEnabled;
        private bool _isOutputAllEnabled;
        private bool _isInputReading;
        private bool _isOutputReading;
        private bool _isOutputTesting;
        private bool _isRunTransitioning;
        private bool _isBusy;
        private string _selectedOutputMode;
        private DispatcherTimer _readTimer;
        private IDeviceDriver _driver;
        private bool _ownsDriverLifecycle;
        private bool _isDeviceConnected;
        private string _connectionStatus;
        private readonly ProjectService _projectService;
        private readonly ObservableCollection<string> _availableTestTasks = new ObservableCollection<string>();
        private string _selectedTestTask;
        private bool _hasPendingChanges;
        private bool _isApplyingTaskConfig;
        private bool _isLoadingTaskOptions;
        private bool _isConfigurationLocked;
        private string _powerVoltageText;
        private string _powerVoltageGroup2Text;
        private string _powerVoltageGroup3Text;
        private string _powerVoltageGroup4Text;
        private bool _isThresholdSyncEnabled;
        private bool _isVoltageSyncEnabled;
        private bool _isSyncUpdatingThreshold;
        private bool _isSyncUpdatingVoltage;
        private DM8600_485 _dm8600Window;
        private bool _isRelayConnected;
        private ObservableCollection<RelayChannelState> _relayChannels;
        private DelegateCommand<object> _toggleRelayCommand;
        private DelegateCommand _relayAllOnCommand;
        private DelegateCommand _relayAllOffCommand;

        private const string ThresholdComPort = "COM14"; //第一套
        //private const string ThresholdComPort = "COM10"; //第二套
        //private const string ThresholdComPort = "COM9"; //第三套

        private const string RelayComPort = "COM18"; //第一套
        //private const string RelayComPort = "COM11"; //第二套
        //private const string RelayComPort = "COM9"; //第三套
        private const int RelayBaudRate = 9600;
        private const byte RelaySlaveAddress = 1;
        private const ushort RelayStartCoilAddress = 0;
        private const int RelayChannelCount = 16;
        public bool CanEditOutputConfig => !IsOutputRunning && !IsConfigurationLocked;
        public bool CanEditConfig => !IsBusy && !IsOutputRunning;
        public bool HasTestTaskOptions => AvailableTestTasks.Count > 0;
        private SubscriptionToken _projectModifiedToken;
        private SubscriptionToken _projectSavingToken;

        public bool IsConnectionIndicatorOn => IsDeviceConnected;

        public bool CanEditChannelEnable => !IsOutputRunning && !IsLeftConfigLocked && !IsConfigurationLocked;

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
        /// 输入通道列表
        /// </summary>
        public ObservableCollection<DiscreteChannelInfo> InputChannels
        {
            get => _inputChannels;
            set => SetProperty(ref _inputChannels, value);
        }

        /// <summary>
        /// 输出通道列表
        /// </summary>
        public ObservableCollection<DiscreteChannelInfo> OutputChannels
        {
            get => _outputChannels;
            set => SetProperty(ref _outputChannels, value);
        }

        public ObservableCollection<DiscreteChannelRow> ChannelRows
        {
            get => _channelRows;
            set => SetProperty(ref _channelRows, value);
        }

        /// <summary>
        /// 输出模式候选项（字符串形式）：Sourcing / Sinking / Push_Pull
        /// </summary>
        public List<string> OutputModes { get; } = new List<string>
        {
            "Sourcing",
            "Sinking",
            "Push_Pull"
        };

        /// <summary>
        /// 输入状态显示列表（右侧面板）
        /// </summary>
        public ObservableCollection<DiscreteStatusItem> InputStatusItems
        {
            get => _inputStatusItems;
            set => SetProperty(ref _inputStatusItems, value);
        }

        /// <summary>
        /// 输出状态显示列表（右侧面板）
        /// </summary>
        public ObservableCollection<DiscreteStatusItem> OutputStatusItems
        {
            get => _outputStatusItems;
            set => SetProperty(ref _outputStatusItems, value);
        }

        /// <summary>
        /// 输入通道全选
        /// </summary>
        public bool IsInputAllEnabled
        {
            get => _isInputAllEnabled;
            set
            {
                if (SetProperty(ref _isInputAllEnabled, value))
                {
                    if (_isApplyingTaskConfig)
                    {
                        return;
                    }
                    SetAllChannelsEnabled(InputChannels, value);
                }
            }
        }

        private void OpenDm8600Window()
        {
            if (Application.Current == null)
            {
                return;
            }

            if (_dm8600Window == null || !_dm8600Window.IsLoaded || !_dm8600Window.IsVisible)
            {
                _dm8600Window = new DM8600_485
                {
                    DataContext = this,
                    Owner = Application.Current.MainWindow,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };
                _dm8600Window.Closed += (_, __) => _dm8600Window = null;
                _dm8600Window.Show();
            }
            else
            {
                if (_dm8600Window.WindowState == WindowState.Minimized)
                {
                    _dm8600Window.WindowState = WindowState.Normal;
                }

                _dm8600Window.Show();
                _dm8600Window.Activate();
                _dm8600Window.Focus();
            }
        }

        private async Task ApplyThresholdsAsync()
        {
            if (!IsDeviceConnected)
            {
                ReMessageBox.Show(
                    "请先打开板卡再下发阈值。",
                    "提示",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
                return;
            }

            NormalizeNumericInput(nameof(DIport1));
            NormalizeNumericInput(nameof(DIport2));
            NormalizeNumericInput(nameof(DIport3));
            NormalizeNumericInput(nameof(DIport4));
            NormalizeNumericInput(nameof(DIport5));
            NormalizeNumericInput(nameof(DIport6));
            NormalizeNumericInput(nameof(DIport7));
            NormalizeNumericInput(nameof(DIport8));

            await Task.Run(() => ApplyDIThresholdsBeforePolling());
        }

        private async Task ApplyVoltagePresetAsync()
        {
            if (!IsDeviceConnected)
            {
                ReMessageBox.Show(
                    "请先打开板卡再下发电压设定值。",
                    "提示",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
                return;
            }

            NormalizeNumericInput(nameof(PowerVoltageText));
            NormalizeNumericInput(nameof(PowerVoltageGroup2Text));
            NormalizeNumericInput(nameof(PowerVoltageGroup3Text));
            NormalizeNumericInput(nameof(PowerVoltageGroup4Text));

            await SendPowerPresetWithoutEnablingOutputAsync();
        }

        ///// <summary>
        ///// 仅停止读取任务，不断开设备连接
        ///// 用于页面切换等场景：保持板卡在线，但停止轮询任务，并复位 DO
        ///// </summary>
        //private async Task StopReadOnlyAsync()
        //{
        //    try
        //    {
        //        System.Diagnostics.Debug.WriteLine($"[DiscreteConfig] StopReadOnlyAsync: {Device?.Name}");

        //        // 停止定时器和输入读取标志
        //        StopReadTimer();
        //        IsInputReading = false;

        //        // 复位 DO 到安全状态（全部输出 0），但不关闭连接
        //        if (_driver != null && IsDeviceConnected && _ownsDriverLifecycle)
        //        {
        //            await ResetAllDigitalOutputsAsync();
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        System.Diagnostics.Debug.WriteLine($"[DiscreteConfig] StopReadOnlyAsync 异常: {ex.Message}");
        //    }
        //}

        /// <summary>
        /// 输出通道全选
        /// </summary>
        public bool IsOutputAllEnabled
        {
            get => _isOutputAllEnabled;
            set
            {
                if (SetProperty(ref _isOutputAllEnabled, value))
                {
                    if (_isApplyingTaskConfig)
                    {
                        return;
                    }
                    SetAllChannelsEnabled(OutputChannels, value);
                }
            }
        }

        /// <summary>
        /// 全局输出模式（保持与 DigitalIOCardConfig.OutputMode 一致）
        /// 仅在未进行任何测试时允许修改
        /// </summary>
        public string SelectedOutputMode
        {
            get => _selectedOutputMode;
            set
            {
                if (SetProperty(ref _selectedOutputMode, value))
                {
                    if (!_isApplyingTaskConfig)
                    {
                        MarkDirty();
                    }
                    // 更新配置并在允许编辑时通知驱动重新配置 DO 输出模式
                    _ = ApplyOutputModeToDriverAsync(value);
                }
            }
        }

        /// <summary>
        /// 是否正在读取输入
        /// </summary>
        public bool IsInputReading
        {
            get => _isInputReading;
            set
            {
                if (SetProperty(ref _isInputReading, value))
                {
                    RaisePropertyChanged(nameof(CanEditOutputConfig));
                }
            }
        }

        /// <summary>
        /// 是否正在读取输出
        /// </summary>
        public bool IsOutputReading
        {
            get => _isOutputReading;
            set
            {
                if (SetProperty(ref _isOutputReading, value))
                {
                    RaisePropertyChanged(nameof(CanEditOutputConfig));
                }
            }
        }

        /// <summary>
        /// 是否正在进行离散量输出测试
        /// </summary>
        public bool IsOutputTesting
        {
            get => _isOutputTesting;
            set
            {
                if (SetProperty(ref _isOutputTesting, value))
                {
                    UpdateConfigurationLock();
                    RaisePropertyChanged(nameof(CanEditOutputConfig));
                    RaisePropertyChanged(nameof(IsOutputRunning));
                    RaisePropertyChanged(nameof(CanToggleOutput));
                    RaisePropertyChanged(nameof(CanEditConfig));
                    RaisePropertyChanged(nameof(IsLeftConfigLocked));
                    RaisePropertyChanged(nameof(CanEditChannelEnable));
                }
            }
        }

        public bool IsOutputRunning
        {
            get => IsOutputTesting;
            private set
            {
                if (IsOutputTesting != value)
                {
                    IsOutputTesting = value;
                    RaisePropertyChanged(nameof(CanEditConfig));
                }
            }
        }

        /// <summary>
        /// 是否允许编辑输出相关配置（包括输出模式）
        /// 当设备处于连接中、断开中或运行中时锁定配置
        /// </summary>
        public bool IsConfigurationLocked
        {
            get => _isConfigurationLocked;
            private set
            {
                if (SetProperty(ref _isConfigurationLocked, value))
                {
                    RaisePropertyChanged(nameof(CanEditOutputConfig));
                    RaisePropertyChanged(nameof(IsLeftConfigLocked));
                    RaisePropertyChanged(nameof(CanEditChannelEnable));
                }
            }
        }

        private void UpdateConfigurationLock()
        {
            IsConfigurationLocked = IsBusy || IsOutputRunning;
        }

        public bool IsLeftConfigLocked => IsOutputRunning || _isRunTransitioning;

        private void SetRunTransitioning(bool value)
        {
            if (_isRunTransitioning != value)
            {
                _isRunTransitioning = value;
                RaisePropertyChanged(nameof(IsLeftConfigLocked));
                RaisePropertyChanged(nameof(CanEditChannelEnable));
            }
        }

        public bool CanToggleOutput => IsDeviceConnected && !IsBusy;

        /// <summary>外部电源电压（V）</summary>
        public string PowerVoltageText
        {
            get => _powerVoltageText;
            set
            {
                var raw = value?.Trim() ?? string.Empty;
                if (SetProperty(ref _powerVoltageText, raw) && !_isApplyingTaskConfig)
                {
                    MarkDirty();
                    ApplySyncedPowerVoltages(raw);
                }
            }
        }

        public string PowerVoltageGroup2Text
        {
            get => _powerVoltageGroup2Text;
            set
            {
                var raw = value?.Trim() ?? string.Empty;
                if (SetProperty(ref _powerVoltageGroup2Text, raw) && !_isApplyingTaskConfig)
                {
                    MarkDirty();
                    ApplySyncedPowerVoltages(raw);
                }
            }
        }

        public string PowerVoltageGroup3Text
        {
            get => _powerVoltageGroup3Text;
            set
            {
                var raw = value?.Trim() ?? string.Empty;
                if (SetProperty(ref _powerVoltageGroup3Text, raw) && !_isApplyingTaskConfig)
                {
                    MarkDirty();
                    ApplySyncedPowerVoltages(raw);
                }
            }
        }

        public string PowerVoltageGroup4Text
        {
            get => _powerVoltageGroup4Text;
            set
            {
                var raw = value?.Trim() ?? string.Empty;
                if (SetProperty(ref _powerVoltageGroup4Text, raw) && !_isApplyingTaskConfig)
                {
                    MarkDirty();
                    ApplySyncedPowerVoltages(raw);
                }
            }
        }

        public bool IsThresholdSyncEnabled
        {
            get => _isThresholdSyncEnabled;
            set => SetProperty(ref _isThresholdSyncEnabled, value);
        }

        public bool IsVoltageSyncEnabled
        {
            get => _isVoltageSyncEnabled;
            set => SetProperty(ref _isVoltageSyncEnabled, value);
        }

        #region DI Port Threshold Values

        private string _dIport1 = "0.00";
        private string _dIport2 = "0.00";
        private string _dIport3 = "0.00";
        private string _dIport4 = "0.00";
        private string _dIport5 = "0.00";
        private string _dIport6 = "0.00";
        private string _dIport7 = "0.00";
        private string _dIport8 = "0.00";

        /// <summary>
        /// DI0-3 高电平阈值 (V)
        /// </summary>
        public string DIport1
        {
            get => _dIport1;
            set
            {
                var raw = value?.Trim() ?? string.Empty;
                if (SetProperty(ref _dIport1, raw) && !_isApplyingTaskConfig)
                {
                    MarkDirty();
                    ApplySyncedDIThresholds(raw);
                }
            }
        }

        /// <summary>
        /// DI4-7 高电平阈值 (V)
        /// </summary>
        public string DIport2
        {
            get => _dIport2;
            set
            {
                var raw = value?.Trim() ?? string.Empty;
                if (SetProperty(ref _dIport2, raw) && !_isApplyingTaskConfig)
                {
                    MarkDirty();
                    ApplySyncedDIThresholds(raw);
                }
            }
        }

        /// <summary>
        /// DI8-11 高电平阈值 (V)
        /// </summary>
        public string DIport3
        {
            get => _dIport3;
            set
            {
                var raw = value?.Trim() ?? string.Empty;
                if (SetProperty(ref _dIport3, raw) && !_isApplyingTaskConfig)
                {
                    MarkDirty();
                    ApplySyncedDIThresholds(raw);
                }
            }
        }

        /// <summary>
        /// DI12-15 高电平阈值 (V)
        /// </summary>
        public string DIport4
        {
            get => _dIport4;
            set
            {
                var raw = value?.Trim() ?? string.Empty;
                if (SetProperty(ref _dIport4, raw) && !_isApplyingTaskConfig)
                {
                    MarkDirty();
                    ApplySyncedDIThresholds(raw);
                }
            }
        }

        /// <summary>
        /// DI16-19 高电平阈值 (V)
        /// </summary>
        public string DIport5
        {
            get => _dIport5;
            set
            {
                var raw = value?.Trim() ?? string.Empty;
                if (SetProperty(ref _dIport5, raw) && !_isApplyingTaskConfig)
                {
                    MarkDirty();
                    ApplySyncedDIThresholds(raw);
                }
            }
        }

        /// <summary>
        /// DI20-23 高电平阈值 (V)
        /// </summary>
        public string DIport6
        {
            get => _dIport6;
            set
            {
                var raw = value?.Trim() ?? string.Empty;
                if (SetProperty(ref _dIport6, raw) && !_isApplyingTaskConfig)
                {
                    MarkDirty();
                    ApplySyncedDIThresholds(raw);
                }
            }
        }

        /// <summary>
        /// DI24-27 高电平阈值 (V)
        /// </summary>
        public string DIport7
        {
            get => _dIport7;
            set
            {
                var raw = value?.Trim() ?? string.Empty;
                if (SetProperty(ref _dIport7, raw) && !_isApplyingTaskConfig)
                {
                    MarkDirty();
                    ApplySyncedDIThresholds(raw);
                }
            }
        }

        /// <summary>
        /// DI28-31 高电平阈值 (V)
        /// </summary>
        public string DIport8
        {
            get => _dIport8;
            set
            {
                var raw = value?.Trim() ?? string.Empty;
                if (SetProperty(ref _dIport8, raw) && !_isApplyingTaskConfig)
                {
                    MarkDirty();
                    ApplySyncedDIThresholds(raw);
                }
            }
        }

        public void NormalizeNumericInput(string propertyName)
        {
            if (string.IsNullOrWhiteSpace(propertyName))
            {
                return;
            }

            var fallback = "0.00";
            switch (propertyName)
            {
                case nameof(DIport1):
                    DIport1 = NormalizeDIThresholdText(DIport1, fallback);
                    break;
                case nameof(DIport2):
                    DIport2 = NormalizeDIThresholdText(DIport2, fallback);
                    break;
                case nameof(DIport3):
                    DIport3 = NormalizeDIThresholdText(DIport3, fallback);
                    break;
                case nameof(DIport4):
                    DIport4 = NormalizeDIThresholdText(DIport4, fallback);
                    break;
                case nameof(DIport5):
                    DIport5 = NormalizeDIThresholdText(DIport5, fallback);
                    break;
                case nameof(DIport6):
                    DIport6 = NormalizeDIThresholdText(DIport6, fallback);
                    break;
                case nameof(DIport7):
                    DIport7 = NormalizeDIThresholdText(DIport7, fallback);
                    break;
                case nameof(DIport8):
                    DIport8 = NormalizeDIThresholdText(DIport8, fallback);
                    break;
                case nameof(PowerVoltageText):
                    PowerVoltageText = NormalizePowerVoltageTextForGroup(PowerVoltageText, fallback);
                    break;
                case nameof(PowerVoltageGroup2Text):
                    PowerVoltageGroup2Text = NormalizePowerVoltageTextForGroup(PowerVoltageGroup2Text, fallback);
                    break;
                case nameof(PowerVoltageGroup3Text):
                    PowerVoltageGroup3Text = NormalizePowerVoltageTextForGroup(PowerVoltageGroup3Text, fallback);
                    break;
                case nameof(PowerVoltageGroup4Text):
                    PowerVoltageGroup4Text = NormalizePowerVoltageTextForGroup(PowerVoltageGroup4Text, fallback);
                    break;
            }
        }

        private void ApplySyncedDIThresholds(string normalized)
        {
            if (!_isThresholdSyncEnabled || _isSyncUpdatingThreshold)
            {
                return;
            }

            _isSyncUpdatingThreshold = true;
            try
            {
                SetProperty(ref _dIport1, normalized, nameof(DIport1));
                SetProperty(ref _dIport2, normalized, nameof(DIport2));
                SetProperty(ref _dIport3, normalized, nameof(DIport3));
                SetProperty(ref _dIport4, normalized, nameof(DIport4));
                SetProperty(ref _dIport5, normalized, nameof(DIport5));
                SetProperty(ref _dIport6, normalized, nameof(DIport6));
                SetProperty(ref _dIport7, normalized, nameof(DIport7));
                SetProperty(ref _dIport8, normalized, nameof(DIport8));
            }
            finally
            {
                _isSyncUpdatingThreshold = false;
            }
        }

        private void ApplySyncedPowerVoltages(string normalized)
        {
            if (!_isVoltageSyncEnabled || _isSyncUpdatingVoltage)
            {
                return;
            }

            _isSyncUpdatingVoltage = true;
            try
            {
                SetProperty(ref _powerVoltageText, normalized, nameof(PowerVoltageText));
                SetProperty(ref _powerVoltageGroup2Text, normalized, nameof(PowerVoltageGroup2Text));
                SetProperty(ref _powerVoltageGroup3Text, normalized, nameof(PowerVoltageGroup3Text));
                SetProperty(ref _powerVoltageGroup4Text, normalized, nameof(PowerVoltageGroup4Text));
            }
            finally
            {
                _isSyncUpdatingVoltage = false;
            }
        }

        /// <summary>
        /// 规范化DI阈值文本
        /// - 验证范围：0.00 - 10.00
        /// - 小数部分截断到2位
        /// - 格式化为2位小数
        /// </summary>
        private string NormalizeDIThresholdText(string input, string currentValue)
        {
            if (string.IsNullOrWhiteSpace(input))
                return currentValue ?? "0.00";

            // 移除所有空格
            input = input.Trim();

            // 尝试解析为数字
            if (!double.TryParse(input, out double value))
                return currentValue ?? "0.00"; // 返回当前值或默认值

            if (double.IsNaN(value) || double.IsInfinity(value))
                return currentValue ?? "0.00";

            // 验证范围：-10.00 - +10.00
            if (value < -10.00)
                value = -10.00;
            else if (value > 10.00)
                value = 10.00;

            // 格式化为2位小数
            return value.ToString("F2");
        }

        /// <summary>
        /// 解析DI阈值，返回double值用于配置
        /// </summary>
        private double ParseDIThreshold(string text)
        {
            if (!double.TryParse(text, out var v))
            {
                v = 0.0;
            }
            if (double.IsNaN(v) || double.IsInfinity(v))
                return 0.0;
            // 再次确保范围
            return Math.Max(-10.00, Math.Min(v, 10.00));
        }

        private void ApplyDIThresholdsBeforePolling()
        {
            try
            {
                using var cli = new DacGroupsSerialClient(ThresholdComPort, 115200, dtrEnable: false, rtsEnable: false);
                cli.Send8Groups(
                    ParseDIThreshold(DIport1),
                    ParseDIThreshold(DIport2),
                    ParseDIThreshold(DIport3),
                    ParseDIThreshold(DIport4),
                    ParseDIThreshold(DIport5),
                    ParseDIThreshold(DIport6),
                    ParseDIThreshold(DIport7),
                    ParseDIThreshold(DIport8));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DiscreteConfig] 设置DI阈值失败: {ex.Message}");
            }
        }

        #endregion

        /// <summary>
        /// 规范化电源电压文本
        /// - 如果输入整数，自动添加小数点和00
        /// - 验证范围：0.00 - 32.00
        /// - 小数部分截断到2位
        /// </summary>
        private string NormalizePowerVoltageText(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return "0.00";

            // 移除所有空格
            input = input.Trim();

            // 尝试解析为数字
            if (!double.TryParse(input, out double value))
                return _powerVoltageText ?? "0.00"; // 返回当前值或默认值

            // 验证范围：0.00 - 32.00
            if (value < 0.00)
                value = 0.00;
            else if (value > 32.00)
                value = 32.00;

            // 格式化为2位小数
            string formatted = value.ToString("F2");

            // 如果是整数（没有小数部分），添加.00
            if (formatted.EndsWith(".00") || !formatted.Contains("."))
            {
                // 已经是正确的格式
            }
            else
            {
                // 确保只有2位小数
                int decimalIndex = formatted.IndexOf('.');
                if (decimalIndex >= 0 && formatted.Length > decimalIndex + 3)
                {
                    formatted = formatted.Substring(0, decimalIndex + 3);
                }
            }

            return formatted;
        }

        private string NormalizePowerVoltageTextForGroup(string input, string currentValue)
        {
            if (string.IsNullOrWhiteSpace(input))
                return "0.00";

            input = input.Trim();

            if (!double.TryParse(input, out double value))
                return currentValue ?? "0.00";

            if (value < 0.00)
                value = 0.00;
            else if (value > 32.00)
                value = 32.00;

            string formatted = value.ToString("F2");

            if (!formatted.EndsWith(".00") && formatted.Contains("."))
            {
                int decimalIndex = formatted.IndexOf('.');
                if (decimalIndex >= 0 && formatted.Length > decimalIndex + 3)
                {
                    formatted = formatted.Substring(0, decimalIndex + 3);
                }
            }

            return formatted;
        }

        /// <summary>
        /// 将电压转换为串口发送格式（去掉小数点，成为4位数字）
        /// 例如：5.00 -> "0500", 12.34 -> "1234"
        /// </summary>
        public string GetPowerVoltageForSerial()
        {
            if (!double.TryParse(PowerVoltageText, out double value))
                return "0000";

            // 确保在有效范围内
            value = Math.Max(0.00, Math.Min(32.00, value));

            // 转换为4位数字字符串（乘以100后转为整数）
            int intValue = (int)Math.Round(value * 100);
            return intValue.ToString("D4"); // 确保4位，不足前面补0
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
                    RaisePropertyChanged(nameof(CanToggleOutput));
                    RaisePropertyChanged(nameof(CanUseRelay));
                    RaisePropertyChanged(nameof(IsLeftConfigLocked));
                    RaisePropertyChanged(nameof(CanEditChannelEnable));
                    RaisePropertyChanged(nameof(CanEditConfig));
                    RaisePropertyChanged(nameof(IsConnectionIndicatorOn));
                    _toggleRelayCommand?.RaiseCanExecuteChanged();
                    _relayAllOnCommand?.RaiseCanExecuteChanged();
                    _relayAllOffCommand?.RaiseCanExecuteChanged();
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

        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (SetProperty(ref _isBusy, value))
                {
                    UpdateConfigurationLock();
                    RaisePropertyChanged(nameof(IsLeftConfigLocked));
                    RaisePropertyChanged(nameof(CanToggleOutput));
                    RaisePropertyChanged(nameof(CanUseRelay));
                    RaisePropertyChanged(nameof(CanEditChannelEnable));
                    _toggleRelayCommand?.RaiseCanExecuteChanged();
                    _relayAllOnCommand?.RaiseCanExecuteChanged();
                    _relayAllOffCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        public bool IsRelayConnected
        {
            get => _isRelayConnected;
            private set
            {
                if (SetProperty(ref _isRelayConnected, value))
                {
                    RaisePropertyChanged(nameof(CanUseRelay));
                    _toggleRelayCommand?.RaiseCanExecuteChanged();
                    _relayAllOnCommand?.RaiseCanExecuteChanged();
                    _relayAllOffCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        public bool CanUseRelay => IsDeviceConnected && IsRelayConnected && !IsBusy;

        public ObservableCollection<RelayChannelState> RelayChannels
        {
            get => _relayChannels;
            private set => SetProperty(ref _relayChannels, value);
        }

        /// <summary>
        /// 当前机箱可用的测试任务
        /// </summary>
        public ObservableCollection<string> AvailableTestTasks => _availableTestTasks;

        /// <summary>
        /// 是否存在可配置的测试任务
        /// </summary>


        /// <summary>
        /// 选中的测试任务
        /// </summary>
        public string SelectedTestTask
        {
            get => _selectedTestTask;
            set => ChangeSelectedTestTask(value);
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
        public ICommand OpenDeviceCommand { get; }
        public ICommand CloseDeviceCommand { get; }
        public ICommand ToggleDeviceCommand { get; }
        public ICommand StartInputReadCommand { get; }
        public ICommand StopInputReadCommand { get; }
        public ICommand StartOutputTestCommand { get; }
        public ICommand StopOutputTestCommand { get; }
        public ICommand ToggleOutputCommand { get; }
        public ICommand OpenDm8600WindowCommand { get; }
        public ICommand ApplyThresholdsCommand { get; }
        public ICommand ApplyVoltagePresetCommand { get; }

        public ICommand ToggleRelayCommand => _toggleRelayCommand;
        public ICommand RelayAllOnCommand => _relayAllOnCommand;
        public ICommand RelayAllOffCommand => _relayAllOffCommand;

        #endregion

        #region Constructor

        public PXIe7131_DIDOViewModel()
        {
            InputChannels = new ObservableCollection<DiscreteChannelInfo>();
            OutputChannels = new ObservableCollection<DiscreteChannelInfo>();
            ChannelRows = new ObservableCollection<DiscreteChannelRow>();
            InputStatusItems = new ObservableCollection<DiscreteStatusItem>();
            OutputStatusItems = new ObservableCollection<DiscreteStatusItem>();
            _availableTestTasks.CollectionChanged += OnAvailableTestTasksChanged;
            _isConfigurationLocked = false;

            InputChannels.CollectionChanged += (s, e) => RebuildChannelRows();
            OutputChannels.CollectionChanged += (s, e) => RebuildChannelRows();

            SaveConfigCommand = new DelegateCommand(
                    () => SaveCurrentTaskConfig(),
                    () => HasPendingChanges && !string.IsNullOrEmpty(SelectedTestTask) && !IsConfigurationLocked)
                .ObservesProperty(() => HasPendingChanges)
                .ObservesProperty(() => SelectedTestTask)
                .ObservesProperty(() => IsConfigurationLocked);
            ReloadConfigCommand = new DelegateCommand(
                    () => ReloadCurrentTaskConfig(),
                    () => HasPendingChanges && !string.IsNullOrEmpty(SelectedTestTask) && !IsConfigurationLocked)
                .ObservesProperty(() => HasPendingChanges)
                .ObservesProperty(() => SelectedTestTask)
                .ObservesProperty(() => IsConfigurationLocked);

            OpenDeviceCommand = new DelegateCommand(async () => await OnOpenDeviceAsync(), () => !IsDeviceConnected)
                .ObservesProperty(() => IsDeviceConnected);

            CloseDeviceCommand = new DelegateCommand(
                    async () => await CloseDeviceWithStopAsync(),
                    () => IsDeviceConnected && !IsBusy)
                .ObservesProperty(() => IsDeviceConnected)
                .ObservesProperty(() => IsBusy);

            ToggleDeviceCommand = new DelegateCommand(async () =>
            {
                if (!IsDeviceConnected)
                {
                    await OnOpenDeviceAsync();
                }
                else
                {
                    await CloseDeviceWithStopAsync();
                }
            }, () => !IsBusy)
                .ObservesProperty(() => IsBusy);

            StartInputReadCommand = new DelegateCommand(OnStartInputRead, () => IsDeviceConnected && !IsInputReading)
                .ObservesProperty(() => IsInputReading)
                .ObservesProperty(() => IsDeviceConnected);
            StopInputReadCommand = new DelegateCommand(OnStopInputRead, () => IsInputReading)
                .ObservesProperty(() => IsInputReading);
            StartOutputTestCommand = new DelegateCommand(async () => await OnStartOutputTest(), () => IsDeviceConnected && !IsOutputTesting)
                .ObservesProperty(() => IsDeviceConnected)
                .ObservesProperty(() => IsOutputTesting);
            StopOutputTestCommand = new DelegateCommand(async () => await OnStopOutputTest(), () => IsOutputTesting)
                .ObservesProperty(() => IsOutputTesting);

            ToggleOutputCommand = new DelegateCommand(
                    async () =>
                    {
                        if (IsOutputRunning)
                        {
                            await OnStopRunAsync();
                        }
                        else
                        {
                            await OnStartRunAsync();
                        }
                    }, () => CanToggleOutput)
                .ObservesProperty(() => IsOutputRunning)
                .ObservesProperty(() => IsDeviceConnected)
                .ObservesProperty(() => IsBusy);

            OpenDm8600WindowCommand = new DelegateCommand(OpenDm8600Window);
            ApplyThresholdsCommand = new DelegateCommand(async () => await ApplyThresholdsAsync());
            ApplyVoltagePresetCommand = new DelegateCommand(async () => await ApplyVoltagePresetAsync());

            RelayChannels = new ObservableCollection<RelayChannelState>();
            for (int i = 0; i < 16; i++)
            {
                RelayChannels.Add(new RelayChannelState(i));
            }
            IsRelayConnected = false;

            _toggleRelayCommand = new DelegateCommand<object>(async p => await ToggleRelayAsync(p), _ => CanUseRelay);
            _relayAllOnCommand = new DelegateCommand(async () => await SetAllRelaysAsync(true), () => CanUseRelay);
            _relayAllOffCommand = new DelegateCommand(async () => await SetAllRelaysAsync(false), () => CanUseRelay);

            _isInputAllEnabled = false;
            _isOutputAllEnabled = false;
            _isOutputTesting = false;
            _connectionStatus = "离线";
            _powerVoltageText = "0.00";
            _powerVoltageGroup2Text = "0.00";
            _powerVoltageGroup3Text = "0.00";
            _powerVoltageGroup4Text = "0.00";
        }

        /// <summary>
        /// 使用指定的设备初始化ViewModel
        /// </summary>
        public PXIe7131_DIDOViewModel(DeviceBase device, string chassisName,
            IPxiChassisService pxiChassisService = null, IEventAggregator eventAggregator = null, ProjectService projectService = null) : this()
        {
            Device = device;
            ChassisName = chassisName;
            CardModel = device?.Model ?? "";
            CardName = !string.IsNullOrEmpty(device?.CardName) ? device.CardName : device?.Model ?? "";
            _pxiChassisService = pxiChassisService;
            _eventAggregator = eventAggregator;
            _projectService = projectService;
            _projectModifiedToken = _eventAggregator?.GetEvent<ProjectModifiedEvent>()?.Subscribe(OnProjectModified);
            _projectSavingToken = _eventAggregator?.GetEvent<ProjectSavingEvent>()?.Subscribe(OnProjectSaving);

            if (Device == null) return;

            InitializeChannels();
            UpdateStatusItems();
            LoadChannelConfigsFromDevice();
            LoadTestTaskOptions();
            var cardConfig = EnsureDigitalCardConfig();
            if (cardConfig != null && cardConfig.PowerVoltage > 0)
            {
                PowerVoltageText = NormalizePowerVoltageText(cardConfig.PowerVoltage.ToString("F2"));
            }
            if (cardConfig != null && cardConfig.PowerVoltageGroup2 > 0)
            {
                PowerVoltageGroup2Text = NormalizePowerVoltageText(cardConfig.PowerVoltageGroup2.ToString("F2"));
            }
            if (cardConfig != null && cardConfig.PowerVoltageGroup3 > 0)
            {
                PowerVoltageGroup3Text = NormalizePowerVoltageText(cardConfig.PowerVoltageGroup3.ToString("F2"));
            }
            if (cardConfig != null && cardConfig.PowerVoltageGroup4 > 0)
            {
                PowerVoltageGroup4Text = NormalizePowerVoltageText(cardConfig.PowerVoltageGroup4.ToString("F2"));
            }
            //TrySendPowerPresetCommands();

            // 监听通道使能变化
            foreach (var channel in InputChannels)
            {
                channel.PropertyChanged += OnChannelPropertyChanged;
            }
            foreach (var channel in OutputChannels)
            {
                channel.PropertyChanged += OnChannelPropertyChanged;
            }
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// 初始化通道信息
        /// </summary>
        private void InitializeChannels()
        {
            InputChannels.Clear();
            OutputChannels.Clear();

            if (Device == null) return;

            // 离散量IO设备
            if (Device is DigitalIODevice dioDevice)
            {
                // 添加DI通道
                var diNode = dioDevice.DiNode ?? dioDevice.Children?.FirstOrDefault(c => c is DigitalInputNode) as DigitalInputNode;
                if (diNode != null)
                {
                    var (startIndex, endIndex) = ParseSlotPosition(diNode.SlotPosition, "DI");
                    for (int i = startIndex; i <= endIndex; i++)
                    {
                        InputChannels.Add(new DiscreteChannelInfo
                        {
                            ChannelName = $"DI{i}",
                            IsEnabled = true
                        });
                    }
                }

                // 添加DO通道
                var doNode = dioDevice.DoNode ?? dioDevice.Children?.FirstOrDefault(c => c is DigitalOutputNode) as DigitalOutputNode;
                if (doNode != null)
                {
                    var (startIndex, endIndex) = ParseSlotPosition(doNode.SlotPosition, "DO");
                    for (int i = startIndex; i <= endIndex; i++)
                    {
                        OutputChannels.Add(new DiscreteChannelInfo
                        {
                            ChannelName = $"DO{i}",
                            IsEnabled = true
                        });
                    }
                }
            }

            // 更新全选状态
            UpdateAllEnabledState();

            RebuildChannelRows();
        }

        private void RebuildChannelRows()
        {
            if (ChannelRows == null)
            {
                ChannelRows = new ObservableCollection<DiscreteChannelRow>();
            }

            ChannelRows.Clear();

            var maxCount = Math.Max(InputChannels?.Count ?? 0, OutputChannels?.Count ?? 0);
            for (int i = 0; i < maxCount; i++)
            {
                var input = (InputChannels != null && i < InputChannels.Count)
                    ? InputChannels[i]
                    : new DiscreteChannelInfo { ChannelName = string.Empty, IsEnabled = false };
                var output = (OutputChannels != null && i < OutputChannels.Count)
                    ? OutputChannels[i]
                    : new DiscreteChannelInfo { ChannelName = string.Empty, IsEnabled = false };

                ChannelRows.Add(new DiscreteChannelRow
                {
                    InputChannel = input,
                    OutputChannel = output
                });
            }
        }

        /// <summary>
        /// 将全局输出模式应用到底层驱动（仅在允许编辑且驱动为 JY7131Driver 时生效）
        /// </summary>
        private async Task ApplyOutputModeToDriverAsync(string mode)
        {
            try
            {
                // 仅在允许编辑输出配置、驱动已连接且驱动类型为 JY7131Driver 时才进行硬件重配置
                if (!CanEditOutputConfig)
                {
                    System.Diagnostics.Debug.WriteLine("[DiscreteConfig] 当前正在测试，忽略输出模式切换请求");
                    return;
                }

                if (_driver is JY7131Driver jy7131Driver && jy7131Driver.IsConnected)
                {
                    System.Diagnostics.Debug.WriteLine($"[DiscreteConfig] 应用输出模式到 JY7131Driver: {mode}");
                    await jy7131Driver.ReconfigureDoOutputModeAsync(mode);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DiscreteConfig] 应用输出模式失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 更新右侧状态显示项
        /// </summary>
        private void UpdateStatusItems()
        {
            // 更新输入状态（只读）
            InputStatusItems.Clear();
            foreach (var channel in InputChannels.Where(c => c.IsEnabled))
            {
                InputStatusItems.Add(new DiscreteStatusItem
                {
                    ChannelName = channel.ChannelName,
                    Value = false, // 默认为0（红灯）
                    IsOutput = false
                });
            }

            // 更新输出状态（可切换）
            OutputStatusItems.Clear();
            foreach (var channel in OutputChannels.Where(c => c.IsEnabled))
            {
                var item = new DiscreteStatusItem
                {
                    ChannelName = channel.ChannelName,
                    Value = false, // 默认为0（红灯）
                    IsOutput = true
                };
                // 设置切换回调，用于调用驱动写入 DO
                item.ToggleAsync = WriteDigitalOutputAsync;
                // 设置切换命令，点击按钮时调用 Toggle 方法
                item.ToggleCommand = new DelegateCommand(() => item.Toggle());
                OutputStatusItems.Add(item);
            }
        }

        /// <summary>
        /// 写入数字输出通道
        /// 由于 JY7131 驱动不返回写入是否成功，所以直接写入并更新指示灯状态
        /// </summary>
        /// <param name="channelName">通道名称（如 DO0）</param>
        /// <param name="value">输出值（true=1, false=0）</param>
        /// <returns>始终返回 true，写入后直接更新 UI</returns>
        private async Task<bool> WriteDigitalOutputAsync(string channelName, bool value)
        {
            if (_driver == null || !IsDeviceConnected)
            {
                System.Diagnostics.Debug.WriteLine($"[DiscreteConfig] 写入失败：驱动未连接");
                return false;
            }

            if (!IsOutputRunning)
            {
                System.Diagnostics.Debug.WriteLine($"[DiscreteConfig] 写入失败：未开始输出，禁止写入 {channelName}");
                return false;
            }

            try
            {
                // 调用驱动写入通道（JY7131 不返回写入结果，直接认为成功）
                double writeValue = value ? 1.0 : 0.0;
                await _driver.WriteChannelAsync(channelName, writeValue);

                System.Diagnostics.Debug.WriteLine($"[DiscreteConfig] 写入 {channelName} = {value}");
                // 由于驱动不返回写入结果，直接返回 true，让指示灯更新状态
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DiscreteConfig] 写入 {channelName} 失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 全部 DO 输出复位为 0（安全状态）
        /// </summary>
        private async Task ResetAllDigitalOutputsAsync()
        {
            if (_driver == null || !IsDeviceConnected) return;

            try
            {
                System.Diagnostics.Debug.WriteLine($"[DiscreteConfig] 复位所有 DO 输出为 0");

                foreach (var channel in OutputChannels)
                {
                    await _driver.WriteChannelAsync(channel.ChannelName, 0);
                }

                foreach (var item in OutputStatusItems)
                {
                    item.Value = false;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DiscreteConfig] 复位 DO 失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 通道属性变化处理
        /// </summary>
        private void OnChannelPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(DiscreteChannelInfo.IsEnabled))
            {
                if (_isApplyingTaskConfig)
                {
                    return;
                }

                var channelInfo = sender as DiscreteChannelInfo;
                System.Diagnostics.Debug.WriteLine($"[DiscreteConfig] 通道使能变化: {channelInfo?.ChannelName} = {channelInfo?.IsEnabled}");
                UpdateStatusItems();
                UpdateAllEnabledState();
                MarkDirty();
            }
        }

        /// <summary>
        /// 更新全选复选框状态
        /// </summary>
        private void UpdateAllEnabledState()
        {
            _isInputAllEnabled = InputChannels.Count > 0 && InputChannels.All(c => c.IsEnabled);
            RaisePropertyChanged(nameof(IsInputAllEnabled));

            _isOutputAllEnabled = OutputChannels.Count > 0 && OutputChannels.All(c => c.IsEnabled);
            RaisePropertyChanged(nameof(IsOutputAllEnabled));
        }

        /// <summary>
        /// 设置所有通道的使能状态
        /// </summary>
        private void SetAllChannelsEnabled(ObservableCollection<DiscreteChannelInfo> channels, bool enabled)
        {
            foreach (var channel in channels)
            {
                channel.IsEnabled = enabled;
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
            (SaveConfigCommand as DelegateCommand)?.RaiseCanExecuteChanged();
            (ReloadConfigCommand as DelegateCommand)?.RaiseCanExecuteChanged();
            if (!_isLoadingTaskOptions)
            {
                LoadConfigForTask(taskName);
            }
        }

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

                var cardConfig = EnsureDigitalCardConfig();
                if (cardConfig != null)
                {
                    EnsureTaskConfigsExist(cardConfig, taskNames);
                }

                string initialTask = null;
                if (Device?.CardConfigData is Models.DigitalIOCardConfig existingConfig &&
                    !string.IsNullOrEmpty(existingConfig.LastSelectedTestTask) &&
                    taskNames.Contains(existingConfig.LastSelectedTestTask))
                {
                    initialTask = existingConfig.LastSelectedTestTask;
                }
                else
                {
                    initialTask = taskNames.FirstOrDefault();
                }

                _selectedTestTask = initialTask;
                RaisePropertyChanged(nameof(SelectedTestTask));
                (SaveConfigCommand as DelegateCommand)?.RaiseCanExecuteChanged();
                (ReloadConfigCommand as DelegateCommand)?.RaiseCanExecuteChanged();
                LoadConfigForTask(initialTask);
            }
            finally
            {
                _isLoadingTaskOptions = false;
            }
        }

        private void EnsureTaskConfigsExist(Models.DigitalIOCardConfig cardConfig, IEnumerable<string> taskNames)
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
                ReMessageBox.Show("请选择要读取的测试任务", "提示",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
                return;
            }

            if (HasPendingChanges)
            {
                var message = $"读取配置将放弃对 \"{SelectedTestTask}\" 的未保存修改，是否继续？";
                var confirm = ReMessageBox.Show(message, "提示",
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

        private void LoadConfigForTask(string taskName)
        {
            var cardConfig = EnsureDigitalCardConfig();
            if (cardConfig == null)
            {
                HasPendingChanges = false;
                return;
            }

            var config = GetOrCreateTaskConfig(cardConfig, taskName ?? string.Empty);
            ApplyTaskConfig(config);
            cardConfig.LastSelectedTestTask = taskName;
            HasPendingChanges = false;
        }

        private void ApplyTaskConfig(Models.DigitalIOTestTaskConfig config)
        {
            _isApplyingTaskConfig = true;
            try
            {
                foreach (var channel in InputChannels)
                {
                    var saved = config.InputChannels.FirstOrDefault(c => c.ChannelName == channel.ChannelName);
                    channel.IsEnabled = saved?.IsEnabled ?? false;
                }

                foreach (var channel in OutputChannels)
                {
                    var saved = config.OutputChannels.FirstOrDefault(c => c.ChannelName == channel.ChannelName);
                    channel.IsEnabled = saved?.IsEnabled ?? false;
                }

                _selectedOutputMode = string.IsNullOrEmpty(config.OutputMode) ? "Push_Pull" : config.OutputMode;
                RaisePropertyChanged(nameof(SelectedOutputMode));
                PowerVoltageText = NormalizePowerVoltageText((config?.PowerVoltage ?? 0).ToString("F2"));
                PowerVoltageGroup2Text = NormalizePowerVoltageText((config?.PowerVoltageGroup2 ?? 0).ToString("F2"));
                PowerVoltageGroup3Text = NormalizePowerVoltageText((config?.PowerVoltageGroup3 ?? 0).ToString("F2"));
                PowerVoltageGroup4Text = NormalizePowerVoltageText((config?.PowerVoltageGroup4 ?? 0).ToString("F2"));

                // 加载DI阈值配置
                DIport1 = config?.DIport1.ToString("F2") ?? "0.00";
                DIport2 = config?.DIport2.ToString("F2") ?? "0.00";
                DIport3 = config?.DIport3.ToString("F2") ?? "0.00";
                DIport4 = config?.DIport4.ToString("F2") ?? "0.00";
                DIport5 = config?.DIport5.ToString("F2") ?? "0.00";
                DIport6 = config?.DIport6.ToString("F2") ?? "0.00";
                DIport7 = config?.DIport7.ToString("F2") ?? "0.00";
                DIport8 = config?.DIport8.ToString("F2") ?? "0.00";

                IsThresholdSyncEnabled = config?.ThresholdSyncEnabled ?? false;
                IsVoltageSyncEnabled = config?.VoltageSyncEnabled ?? false;

                UpdateAllEnabledState();
                UpdateStatusItems();
            }
            finally
            {
                _isApplyingTaskConfig = false;
            }
        }

        private Models.DigitalIOTestTaskConfig GetOrCreateTaskConfig(Models.DigitalIOCardConfig cardConfig, string taskName)
        {
            taskName ??= string.Empty;
            var config = cardConfig.TestTaskConfigs.FirstOrDefault(c => c.TestTaskName == taskName);
            if (config == null)
            {
                config = new Models.DigitalIOTestTaskConfig { TestTaskName = taskName };
                config.PowerVoltage = cardConfig.PowerVoltage;
                config.PowerVoltageGroup2 = cardConfig.PowerVoltageGroup2;
                config.PowerVoltageGroup3 = cardConfig.PowerVoltageGroup3;
                config.PowerVoltageGroup4 = cardConfig.PowerVoltageGroup4;
                InitializeTaskConfigChannels(config, cardConfig);
                cardConfig.TestTaskConfigs.Add(config);
            }
            return config;
        }

        private void InitializeTaskConfigChannels(Models.DigitalIOTestTaskConfig targetConfig, Models.DigitalIOCardConfig sourceConfig)
        {
            IEnumerable<Models.DiscreteChannelConfig> inputSource = sourceConfig.InputChannels.Any()
                ? sourceConfig.InputChannels
                : InputChannels.Select(c => new Models.DiscreteChannelConfig
                {
                    ChannelName = c.ChannelName,
                    IsEnabled = c.IsEnabled,
                    IsOutput = false
                });

            foreach (var channel in inputSource)
            {
                targetConfig.InputChannels.Add(new Models.DiscreteChannelConfig
                {
                    ChannelName = channel.ChannelName,
                    IsEnabled = false, // 默认不勾选
                    IsOutput = false
                });
            }

            IEnumerable<Models.DiscreteChannelConfig> outputSource = sourceConfig.OutputChannels.Any()
                ? sourceConfig.OutputChannels
                : OutputChannels.Select(c => new Models.DiscreteChannelConfig
                {
                    ChannelName = c.ChannelName,
                    IsEnabled = c.IsEnabled,
                    IsOutput = true
                });

            foreach (var channel in outputSource)
            {
                targetConfig.OutputChannels.Add(new Models.DiscreteChannelConfig
                {
                    ChannelName = channel.ChannelName,
                    IsEnabled = false, // 默认不勾选
                    IsOutput = true
                });
            }

            targetConfig.OutputMode = "Sinking";
        }

        private Models.DigitalIOCardConfig EnsureDigitalCardConfig()
        {
            if (Device == null)
            {
                return null;
            }

            var cardConfig = Device.CardConfigData as Models.DigitalIOCardConfig;
            if (cardConfig == null)
            {
                cardConfig = new Models.DigitalIOCardConfig();
                Device.CardConfigData = cardConfig;
            }

            cardConfig.CardId = Device.Id;
            cardConfig.CardName = CardName;
            cardConfig.CardModel = CardModel;
            cardConfig.ChassisName = ChassisName;
            return cardConfig;
        }

        private void MarkDirty()
        {
            if (!_isApplyingTaskConfig)
            {
                HasPendingChanges = true;
            }
        }

        private double ParsePowerVoltage()
        {
            return ParsePowerVoltageValue(PowerVoltageText);
        }

        private double ParsePowerVoltageValue(string text)
        {
            if (!double.TryParse(text, out var v))
            {
                v = 0;
            }
            if (double.IsNaN(v) || double.IsInfinity(v)) return 0;
            return Math.Max(0, Math.Min(v, 32.00));
        }

        private async Task EnsurePowerClientAsync()
        {
            if (_driver is JY7131Driver jy)
            {
                await jy.EnsurePowerOutputsAsync(
                    ParsePowerVoltageValue(PowerVoltageText),
                    ParsePowerVoltageValue(PowerVoltageGroup2Text),
                    ParsePowerVoltageValue(PowerVoltageGroup3Text),
                    ParsePowerVoltageValue(PowerVoltageGroup4Text));
            }
        }

        private async Task SendPowerPresetWithoutEnablingOutputAsync()
        {
            if (_driver is JY7131Driver jy)
            {
                await jy.SetPowerVoltagesAsync(
                    ParsePowerVoltageValue(PowerVoltageText),
                    ParsePowerVoltageValue(PowerVoltageGroup2Text),
                    ParsePowerVoltageValue(PowerVoltageGroup3Text),
                    ParsePowerVoltageValue(PowerVoltageGroup4Text));
            }
        }

        private async Task ApplyOpenCardPresetsAsync()
        {
            await Task.Run(() => ApplyDIThresholdsBeforePolling());
            try
            {
                await SendPowerPresetWithoutEnablingOutputAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DiscreteConfig] 打开板卡时预设电压失败: {ex.Message}");
            }
        }

        private async Task StopPowerClientAsync()
        {
            if (_driver is JY7131Driver jy)
            {
                try
                {
                    var stopTask = jy.StopPowerOutputAsync();
                    var completed = await Task.WhenAny(stopTask, Task.Delay(1500));
                    if (completed != stopTask)
                    {
                        System.Diagnostics.Debug.WriteLine("[DiscreteConfig] StopPowerOutputAsync 超时，忽略并继续后续流程");
                        return;
                    }
                    await stopTask;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[DiscreteConfig] StopPowerOutputAsync 异常(忽略): {ex.Message}");
                }
            }
        }

        private bool EnsurePendingChangesHandled()
        {
            if (!HasPendingChanges || _isLoadingTaskOptions)
            {
                return true;
            }

            var message = string.IsNullOrEmpty(SelectedTestTask)
                ? "离散量配置尚未保存，是否现在保存？"
                : $"\"{SelectedTestTask}\"的{CardName}配置尚未保存，是否保存？";

            var result = ReMessageBox.Show(message, "提示",
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
            if (_disposed ||
                !HasPendingChanges ||
                string.IsNullOrEmpty(SelectedTestTask) ||
                IsConfigurationLocked)
            {
                return;
            }

            SaveCurrentTaskConfig(false);
        }

        /// <summary>
        /// 从设备加载已保存的通道配置
        /// </summary>
        private void LoadChannelConfigsFromDevice()
        {
            if (!(Device?.CardConfigData is Models.DigitalIOCardConfig cardConfig))
                return;

            if (!string.IsNullOrEmpty(cardConfig.CardName))
            {
                _cardName = cardConfig.CardName;
                RaisePropertyChanged(nameof(CardName));
            }

            _selectedOutputMode = string.IsNullOrEmpty(cardConfig.OutputMode)
                ? "Push_Pull"
                : cardConfig.OutputMode;
            RaisePropertyChanged(nameof(SelectedOutputMode));
        }

        /// <summary>
        /// 保存通道配置到设备
        /// </summary>
        private bool SaveCurrentTaskConfig(bool showMessages = true)
        {
            System.Diagnostics.Debug.WriteLine("[DiscreteConfig] SaveCurrentTaskConfig 开始...");
            if (Device == null)
            {
                System.Diagnostics.Debug.WriteLine("[DiscreteConfig] Device == null，跳过保存");
                return false;
            }

            if (string.IsNullOrEmpty(SelectedTestTask))
            {
                if (showMessages)
                {
                    ReMessageBox.Show("请选择测试任务", "提示",
                        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                }
                return false;
            }

            var cardConfig = EnsureDigitalCardConfig();
            if (cardConfig == null)
            {
                return false;
            }

            var taskConfig = GetOrCreateTaskConfig(cardConfig, SelectedTestTask);

            taskConfig.InputChannels.Clear();
            foreach (var channel in InputChannels)
            {
                taskConfig.InputChannels.Add(new Models.DiscreteChannelConfig
                {
                    ChannelName = channel.ChannelName,
                    IsEnabled = channel.IsEnabled,
                    IsOutput = false
                });
            }

            taskConfig.OutputChannels.Clear();
            foreach (var channel in OutputChannels)
            {
                taskConfig.OutputChannels.Add(new Models.DiscreteChannelConfig
                {
                    ChannelName = channel.ChannelName,
                    IsEnabled = channel.IsEnabled,
                    IsOutput = true
                });
            }

            taskConfig.OutputMode = SelectedOutputMode;
            var powerVoltage = ParsePowerVoltage();
            taskConfig.PowerVoltage = powerVoltage;
            taskConfig.PowerVoltageGroup2 = ParsePowerVoltageValue(PowerVoltageGroup2Text);
            taskConfig.PowerVoltageGroup3 = ParsePowerVoltageValue(PowerVoltageGroup3Text);
            taskConfig.PowerVoltageGroup4 = ParsePowerVoltageValue(PowerVoltageGroup4Text);
            cardConfig.OutputMode = SelectedOutputMode;
            cardConfig.PowerVoltage = powerVoltage;
            cardConfig.PowerVoltageGroup2 = taskConfig.PowerVoltageGroup2;
            cardConfig.PowerVoltageGroup3 = taskConfig.PowerVoltageGroup3;
            cardConfig.PowerVoltageGroup4 = taskConfig.PowerVoltageGroup4;

            // 保存DI阈值配置
            taskConfig.DIport1 = ParseDIThreshold(DIport1);
            taskConfig.DIport2 = ParseDIThreshold(DIport2);
            taskConfig.DIport3 = ParseDIThreshold(DIport3);
            taskConfig.DIport4 = ParseDIThreshold(DIport4);
            taskConfig.DIport5 = ParseDIThreshold(DIport5);
            taskConfig.DIport6 = ParseDIThreshold(DIport6);
            taskConfig.DIport7 = ParseDIThreshold(DIport7);
            taskConfig.DIport8 = ParseDIThreshold(DIport8);

            taskConfig.ThresholdSyncEnabled = IsThresholdSyncEnabled;
            taskConfig.VoltageSyncEnabled = IsVoltageSyncEnabled;
            cardConfig.LastSelectedTestTask = SelectedTestTask;

            cardConfig.InputChannels.Clear();
            foreach (var channel in taskConfig.InputChannels)
            {
                cardConfig.InputChannels.Add(new Models.DiscreteChannelConfig
                {
                    ChannelName = channel.ChannelName,
                    IsEnabled = channel.IsEnabled,
                    IsOutput = false
                });
            }

            cardConfig.OutputChannels.Clear();
            foreach (var channel in taskConfig.OutputChannels)
            {
                cardConfig.OutputChannels.Add(new Models.DiscreteChannelConfig
                {
                    ChannelName = channel.ChannelName,
                    IsEnabled = channel.IsEnabled,
                    IsOutput = true
                });
            }

            if (Device.CardName != CardName)
            {
                Device.CardName = CardName;
            }

            System.Diagnostics.Debug.WriteLine($"[DiscreteConfig] 调用 UpdateDeviceCardConfig: DeviceId={Device.Id}, HashCode={Device.GetHashCode()}");
            _pxiChassisService?.UpdateDeviceCardConfig(Device.Id, cardConfig);

            _eventAggregator?.GetEvent<Events.ProjectModifiedEvent>()?.Publish(new Events.ProjectModifiedEventArgs
            {
                ModificationType = "ChannelConfig",
                Description = $"离散量通道配置已更新 {SelectedTestTask}"
            });

            var enabledDI = taskConfig.InputChannels?.Count(c => c.IsEnabled) ?? 0;
            var enabledDO = taskConfig.OutputChannels?.Count(c => c.IsEnabled) ?? 0;
            System.Diagnostics.Debug.WriteLine($"[DiscreteConfig] 统计: DI使能={enabledDI}, DO使能={enabledDO}");

            _eventAggregator?.GetEvent<Events.ChannelEnableChangedEvent>()?.Publish(new Events.ChannelEnableChangedEventArgs
            {
                DeviceId = Device.Id,
                CardName = CardName,
                ChassisName = ChassisName
            });
            System.Diagnostics.Debug.WriteLine("[DiscreteConfig] ChannelEnableChangedEvent 已发布");

            HasPendingChanges = false;
            if (showMessages)
            {
                ReMessageBox.Show("保存成功", "提示",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
            }
            return true;
        }


        /// <summary>
        /// 解析SlotPosition字符串，例如 "DI0–DI31"
        /// </summary>
        private (int startIndex, int endIndex) ParseSlotPosition(string slotPosition, string prefix)
        {
            try
            {
                var parts = slotPosition?.Replace(" ", "").Split('–', '-');
                if (parts == null || parts.Length != 2)
                    return (0, 31); // 默认返回32个通道

                var startStr = parts[0].Replace(prefix, "");
                var endStr = parts[1].Replace(prefix, "");

                if (int.TryParse(startStr, out int start) && int.TryParse(endStr, out int end))
                {
                    return (start, end);
                }
            }
            catch (Exception)
            {
            }

            return (0, 31); // 默认返回32个通道
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
            ConnectionStatus = "连接中";

            try
            {
                if (_driver != null && _driver.IsConnected)
                {
                    if (await TryReuseConnectedDriverAsync("[DiscreteConfig] 复用已连接的驱动实例", true))
                        return;
                }

                ConnectionStatus = "检测中";

                // 创建或获取驱动实例
                _driver = DriverFactory.CreateDriver(Device);
                if (await TryReuseConnectedDriverAsync("[DiscreteConfig] 驱动已处于连接状态，直接复用", true))
                    return;

                // 连接设备（检测板卡）
                bool connected = await _driver.ConnectAsync();

                if (connected)
                {
                    _ownsDriverLifecycle = true;
                    IsDeviceConnected = true;
                    ConnectionStatus = "在线";
                    await ApplyOpenCardPresetsAsync();
                    await OpenRelayAsync();
                    System.Diagnostics.Debug.WriteLine($"[DiscreteConfig] 板卡检测成功: {Device.Name}");
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

                System.Diagnostics.Debug.WriteLine($"[DiscreteConfig] 板卡检测异常: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// 接管已经处于连接状态的驱动，确保采集任务启动并复位输出
        /// </summary>
        private async Task<bool> TryReuseConnectedDriverAsync(string logMessage, bool autoStartInputRead = false)
        {
            if (_driver == null || !_driver.IsConnected)
            {
                return false;
            }

            _ownsDriverLifecycle = false;
            IsDeviceConnected = true;
            ConnectionStatus = "在线";
            System.Diagnostics.Debug.WriteLine(logMessage);

            await ApplyOpenCardPresetsAsync();
            await OpenRelayAsync();

            return true;
        }

        private async Task OpenRelayAsync()
        {
            if (!IsDeviceConnected)
            {
                IsRelayConnected = false;
                return;
            }

            await RefreshRelayStatesAsync(showError: true);
        }

        private async Task CloseRelayAsync(bool tryAllOff)
        {
            IsRelayConnected = false;

            if (!tryAllOff)
            {
                return;
            }

            try
            {
                // 添加超时处理，避免卡住
                var timeoutTask = Task.Delay(2000);
                var acquireTask = SerialPortMutex.AcquireAsync(RelayComPort);
                var completedTask = await Task.WhenAny(acquireTask, timeoutTask);
                
                if (completedTask == timeoutTask)
                {
                    System.Diagnostics.Debug.WriteLine($"[DiscreteConfig] 关闭485继电器超时，跳过");
                    return;
                }

                using (await acquireTask)
                {
                    await Task.Run(() =>
                    {
                        using var cli = new RelayModbusClient(RelayComPort, RelaySlaveAddress, RelayBaudRate);
                        cli.SetAll(RelayStartCoilAddress, RelayChannelCount, false);
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DiscreteConfig] 关闭485继电器时全关失败: {ex.Message}");
            }
            finally
            {
                foreach (var ch in RelayChannels)
                {
                    ch.IsOn = false;
                }
            }
        }

        private async Task RefreshRelayStatesAsync(bool showError)
        {
            try
            {
                bool[] states;
                using (await SerialPortMutex.AcquireAsync(RelayComPort))
                {
                    states = await Task.Run(() =>
                    {
                        using var cli = new RelayModbusClient(RelayComPort, RelaySlaveAddress, RelayBaudRate);
                        return cli.ReadCoils(RelayStartCoilAddress, (ushort)RelayChannelCount);
                    });
                }

                if (states != null)
                {
                    int count = Math.Min(states.Length, RelayChannels.Count);
                    for (int i = 0; i < count; i++)
                    {
                        RelayChannels[i].IsOn = states[i];
                    }
                }

                IsRelayConnected = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DiscreteConfig] 读取485继电器状态失败: {ex.Message}");

                if (showError)
                {
                    try
                    {
                        ReMessageBox.Show(
                            "485继电器串口打开/回读失败（COM15/9600）。请检查串口是否被占用。",
                            "提示",
                            System.Windows.MessageBoxButton.OK,
                            System.Windows.MessageBoxImage.Warning);
                    }
                    catch { }
                }

                IsRelayConnected = false;
            }
        }

        private async Task ToggleRelayAsync(object parameter)
        {
            if (!CanUseRelay)
            {
                return;
            }

            int index;
            try
            {
                index = Convert.ToInt32(parameter);
            }
            catch
            {
                return;
            }

            if (index < 0 || index >= RelayChannels.Count)
            {
                return;
            }

            bool target = !RelayChannels[index].IsOn;

            if (IsBusy)
            {
                return;
            }

            IsBusy = true;
            try
            {
                using (await SerialPortMutex.AcquireAsync(RelayComPort))
                {
                    await Task.Run(() =>
                    {
                        using var cli = new RelayModbusClient(RelayComPort, RelaySlaveAddress, RelayBaudRate);
                        cli.WriteSingleCoil((ushort)index, target);
                    });
                }

                await RefreshRelayStatesAsync(showError: false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DiscreteConfig] 切换继电器失败: {ex.Message}");
                IsRelayConnected = false;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task SetAllRelaysAsync(bool on)
        {
            if (!CanUseRelay)
            {
                return;
            }

            if (IsBusy)
            {
                return;
            }

            IsBusy = true;
            try
            {
                using (await SerialPortMutex.AcquireAsync(RelayComPort))
                {
                    await Task.Run(() =>
                    {
                        using var cli = new RelayModbusClient(RelayComPort, RelaySlaveAddress, RelayBaudRate);
                        cli.SetAll(RelayStartCoilAddress, RelayChannelCount, on);
                    });
                }

                await RefreshRelayStatesAsync(showError: false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DiscreteConfig] 全部继电器操作失败: {ex.Message}");
                IsRelayConnected = false;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task OnStartRunAsync()
        {
            if (_driver == null || !IsDeviceConnected)
            {
                System.Diagnostics.Debug.WriteLine("[DiscreteConfig] 无法开始运行：驱动未连接");
                return;
            }

            if (IsBusy)
            {
                return;
            }

            SetRunTransitioning(true);
            IsBusy = true;
            try
            {
                if (_driver is JY7131Driver jy7131Driver)
                {
                    try
                    {
                        await jy7131Driver.ReconfigureDoOutputModeAsync(SelectedOutputMode);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[DiscreteConfig] 开始运行时应用输出模式失败: {ex.Message}");
                    }
                }

                // 1) 先下发 DI 阈值
                ApplyDIThresholdsBeforePolling();

                // 2) 再下发电源电压并开启输出
                try
                {
                    await EnsurePowerClientAsync();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[DiscreteConfig] 开始运行时设置电源输出失败: {ex.Message}");
                }

                try
                {
                    await _driver.StartAcquisitionAsync();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[DiscreteConfig] 开始运行时启动采集失败: {ex.Message}");
                }

                // 3) 启动前将 DO 复位到安全态（0）
                await ResetAllDigitalOutputsAsync();

                // 4) 最后启动轮询，并放开 DO 写入
                EnsureInputReadLoop();

                IsOutputRunning = true;
            }
            finally
            {
                IsBusy = false;
                SetRunTransitioning(false);
            }
        }

        private async Task OnStopRunAsync()
        {
            if (_driver == null || !IsDeviceConnected)
            {
                IsOutputRunning = false;
                IsConfigurationLocked = false;
                return;
            }

            if (IsBusy)
            {
                return;
            }

            SetRunTransitioning(true);
            IsBusy = true;
            try
            {
                OnStopInputRead();

                // 1) 先复位 DI/DO UI 状态
                foreach (var item in InputStatusItems)
                {
                    item.Value = false;
                }

                // 2) 再复位 DO 输出
                await ResetAllDigitalOutputsAsync();

                // 3) 再关闭电源输出（best-effort）
                try
                {
                    await StopPowerClientAsync();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[DiscreteConfig] 停止运行时关闭电源输出失败: {ex.Message}");
                }

                // 4) 最后停止采集
                if (_ownsDriverLifecycle)
                {
                    try
                    {
                        await _driver.StopAcquisitionAsync();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[DiscreteConfig] 停止运行时停止采集失败: {ex.Message}");
                    }
                }

                IsOutputRunning = false;
            }
            finally
            {
                IsBusy = false;
                SetRunTransitioning(false);
            }
        }

        private async Task CloseDeviceWithStopAsync()
        {
            if (IsBusy)
            {
                return;
            }

            if (!IsDeviceConnected)
            {
                return;
            }

            IsBusy = true;
            ConnectionStatus = "断开中";
            try
            {
                if (IsOutputRunning)
                {
                    await OnStopRunAsync();
                }

                await StopDebugAsync();
            }
            finally
            {
                IsBusy = false;
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
            // 注意：不检查 IsBusy，因为可能被 CloseDeviceWithStopAsync 调用（已设置 IsBusy = true）
            // 如果需要防止重入，应该使用其他标志
            
            try
            {
                System.Diagnostics.Debug.WriteLine($"[DiscreteConfig] 停止调试: {Device?.Name}");

                // 1. 停止 Timer
                StopReadTimer();
                IsInputReading = false;
                IsOutputReading = false;
                IsOutputRunning = false;

                await CloseRelayAsync(true);

                // 2. 仅在当前面板拥有驱动生命周期时复位 DO
                if (_driver != null && IsDeviceConnected && _ownsDriverLifecycle)
                {
                    await ResetAllDigitalOutputsAsync();
                }

                // 3. 停止并释放 Driver/Task（仅当由本面板建立连接时）
                if (_driver != null && _ownsDriverLifecycle)
                {
                    try
                    {
                        await _driver.StopAcquisitionAsync();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[DiscreteConfig] StopDebugAsync 停止采集失败(忽略): {ex.Message}");
                    }

                    try
                    {
                        await _driver.DisconnectAsync();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[DiscreteConfig] StopDebugAsync 断开板卡失败(忽略): {ex.Message}");
                    }
                }
                await StopPowerClientAsync();

                _driver = null;
                _ownsDriverLifecycle = false;

                // 4. 重置 UI 状态
                IsDeviceConnected = false;
                ConnectionStatus = "离线";

                // 5. 重置所有指示灯为 0
                foreach (var item in InputStatusItems)
                {
                    item.Value = false;
                }
                foreach (var item in OutputStatusItems)
                {
                    item.Value = false;
                }

                System.Diagnostics.Debug.WriteLine($"[DiscreteConfig] 调试已停止");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DiscreteConfig] 停止调试异常: {ex.Message}");
            }
        }

        private void EnsureInputReadLoop()
        {
            if (!IsInputReading)
            {
                OnStartInputRead();
            }
        }

        private void OnStartInputRead()
        {
            // 此时采集任务已在打开板卡时启动，这里只控制是否轮询读取 DI
            IsInputReading = true;
            StartReadTimer();
        }

        private void OnStopInputRead()
        {
            IsInputReading = false;
            StopReadTimer();
        }

        private async Task OnStartOutputTest()
        {
            if (_driver == null || !IsDeviceConnected)
            {
                System.Diagnostics.Debug.WriteLine("[DiscreteConfig] 无法开始 DO 测试：驱动未连接");
                return;
            }

            try
            {
                System.Diagnostics.Debug.WriteLine("[DiscreteConfig] 开始 DO 测试，先复位所有 DO 输出为 0");
                await ResetAllDigitalOutputsAsync();
                IsOutputTesting = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DiscreteConfig] 开始 DO 测试失败: {ex.Message}");
            }
        }

        private async Task OnStopOutputTest()
        {
            if (_driver == null || !IsDeviceConnected)
            {
                IsOutputTesting = false;
                System.Diagnostics.Debug.WriteLine("[DiscreteConfig] 无法停止 DO 测试：驱动未连接");
                return;
            }

            try
            {
                System.Diagnostics.Debug.WriteLine("[DiscreteConfig] 停止 DO 测试，复位所有 DO 输出为 0");
                await ResetAllDigitalOutputsAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DiscreteConfig] 停止 DO 测试失败: {ex.Message}");
            }
            finally
            {
                IsOutputTesting = false;
            }
        }

        private void StartReadTimer()
        {
            if (_readTimer == null)
            {
                _readTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(100)
                };
                _readTimer.Tick += ReadTimer_Tick;
            }
            _readTimer.Start();
        }

        private void StopReadTimer()
        {
            _readTimer?.Stop();
        }

        private async void ReadTimer_Tick(object sender, EventArgs e)
        {
            if (_driver == null || !IsDeviceConnected) return;

            try
            {
                // DI 读取：每100ms从板卡读取输入通道状态
                if (IsInputReading && InputStatusItems.Count > 0)
                {
                    // 批量读取所有使能的 DI 通道
                    var channelIds = InputStatusItems.Select(i => i.ChannelName).ToList();
                    var values = await _driver.ReadChannelsBatchAsync(channelIds);

                    foreach (var item in InputStatusItems)
                    {
                        if (values.TryGetValue(item.ChannelName, out double value))
                        {
                            // 0 = false (红灯), 非0 = true (绿灯)
                            item.Value = value != 0;
                        }
                    }
                }

                // DO 不需要从板卡读取
                // DO 的指示灯状态由写入操作结果决定：
                // - 写入成功：指示灯变为目标状态
                // - 写入失败：指示灯保持原状态
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DiscreteConfig] 读取通道状态失败: {ex.Message}");
            }
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
                try
                {
                    try
                    {
                        using (SerialPortMutex.AcquireAsync(RelayComPort).GetAwaiter().GetResult())
                        {
                            using var cli = new RelayModbusClient(RelayComPort, RelaySlaveAddress, RelayBaudRate);
                            cli.SetAll(RelayStartCoilAddress, RelayChannelCount, false);
                        }
                    }
                    catch { }
                }
                catch { }

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
    /// 离散量通道信息
    /// </summary>
    public class DiscreteChannelInfo : BindableBase
    {
        private string _channelName;
        private bool _isEnabled;

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
    }

    public class DiscreteChannelRow : BindableBase
    {
        private DiscreteChannelInfo _inputChannel;
        private DiscreteChannelInfo _outputChannel;

        public DiscreteChannelInfo InputChannel
        {
            get => _inputChannel;
            set
            {
                if (!ReferenceEquals(_inputChannel, value))
                {
                    if (_inputChannel != null)
                    {
                        _inputChannel.PropertyChanged -= OnChannelPropertyChanged;
                    }
                    _inputChannel = value;
                    if (_inputChannel != null)
                    {
                        _inputChannel.PropertyChanged += OnChannelPropertyChanged;
                    }
                    RaisePropertyChanged(nameof(InputChannel));
                    RaisePropertyChanged(nameof(HasInputChannel));
                    RaisePropertyChanged(nameof(InputEnabled));
                }
            }
        }

        public DiscreteChannelInfo OutputChannel
        {
            get => _outputChannel;
            set
            {
                if (!ReferenceEquals(_outputChannel, value))
                {
                    if (_outputChannel != null)
                    {
                        _outputChannel.PropertyChanged -= OnChannelPropertyChanged;
                    }
                    _outputChannel = value;
                    if (_outputChannel != null)
                    {
                        _outputChannel.PropertyChanged += OnChannelPropertyChanged;
                    }
                    RaisePropertyChanged(nameof(OutputChannel));
                    RaisePropertyChanged(nameof(HasOutputChannel));
                    RaisePropertyChanged(nameof(OutputEnabled));
                }
            }
        }

        public bool HasInputChannel => InputChannel != null && !string.IsNullOrWhiteSpace(InputChannel.ChannelName);
        public bool HasOutputChannel => OutputChannel != null && !string.IsNullOrWhiteSpace(OutputChannel.ChannelName);

        public bool? InputEnabled
        {
            get => InputChannel?.IsEnabled;
            set
            {
                if (InputChannel == null)
                {
                    return;
                }

                var enabled = value ?? false;
                if (InputChannel.IsEnabled != enabled)
                {
                    InputChannel.IsEnabled = enabled;
                    RaisePropertyChanged(nameof(InputEnabled));
                }
            }
        }

        public bool? OutputEnabled
        {
            get => OutputChannel?.IsEnabled;
            set
            {
                if (OutputChannel == null)
                {
                    return;
                }

                var enabled = value ?? false;
                if (OutputChannel.IsEnabled != enabled)
                {
                    OutputChannel.IsEnabled = enabled;
                    RaisePropertyChanged(nameof(OutputEnabled));
                }
            }
        }

        private void OnChannelPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(DiscreteChannelInfo.IsEnabled))
            {
                RaisePropertyChanged(nameof(InputEnabled));
                RaisePropertyChanged(nameof(OutputEnabled));
            }
            else if (e.PropertyName == nameof(DiscreteChannelInfo.ChannelName))
            {
                RaisePropertyChanged(nameof(HasInputChannel));
                RaisePropertyChanged(nameof(HasOutputChannel));
            }
        }
    }

    /// <summary>
    /// 离散量状态显示项
    /// </summary>
    public class DiscreteStatusItem : BindableBase
    {
        private string _channelName;
        private bool _value;
        private bool _isOutput;

        public string ChannelName
        {
            get => _channelName;
            set => SetProperty(ref _channelName, value);
        }

        /// <summary>
        /// 值：false=0(红灯), true=1(绿灯)
        /// </summary>
        public bool Value
        {
            get => _value;
            set => SetProperty(ref _value, value);
        }

        /// <summary>
        /// 是否为输出通道（输出通道可以切换状态）
        /// </summary>
        public bool IsOutput
        {
            get => _isOutput;
            set => SetProperty(ref _isOutput, value);
        }

        /// <summary>
        /// 切换命令（仅输出通道使用）
        /// </summary>
        public ICommand ToggleCommand { get; set; }

        /// <summary>
        /// 切换回调（由 ViewModel 设置，用于调用驱动，返回是否成功）
        /// </summary>
        public Func<string, bool, Task<bool>> ToggleAsync { get; set; }

        /// <summary>
        /// 切换输出状态（0→1 或 1→0）
        /// </summary>
        public async void Toggle()
        {
            if (ToggleAsync != null)
            {
                // 计算要设置的目标值（当前值取反）
                bool targetValue = !Value;

                // 调用 ViewModel 提供的回调来写入驱动
                bool success = await ToggleAsync(ChannelName, targetValue);

                // 只有写入成功才更新指示灯状态
                // 写入失败则保持原状态
                if (success)
                {
                    Value = targetValue;
                }
            }
        }
    }

    public class RelayChannelState : BindableBase
    {
        private bool _isOn;

        public RelayChannelState(int index)
        {
            Index = index;
        }

        public int Index { get; }

        public bool IsOn
        {
            get => _isOn;
            set => SetProperty(ref _isOn, value);
        }
    }
}
