using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using MeasureControl.Drivers;
using MeasureControl.Drivers.ArtSwitch;
using MeasureControl.Models;
using MeasureControl.Models.Devices;
using MeasureControl.Services;
using MeasureControl.Views;
using MeasureControl.Views.Dialogs;
using NationalInstruments.Visa;
using Prism.Commands;
using Prism.Mvvm;

namespace MeasureControl.ViewModels.TestTask
{
    /// <summary>
    /// 频率计独立测试面板ViewModel
    /// </summary>
    public class FrequencyCounterTestPanelViewModel : BindableBase, IDisposable, MeasureControl.ViewModels.ICloseGuard
    {
        private const string Placeholder = "------";

        private const int FastPollIntervalMs = 100;
        private const int SlowPollEveryNFastTicks = 3;
        private const int ErrorDrainMinIntervalMs = 2000;

        private volatile bool _forceSlowTick;
        private DateTime _lastErrorQueueDrainUtc = DateTime.MinValue;

        private readonly IPxiChassisService _pxiChassisService;
        private NationalInstruments.Visa.MessageBasedSession _frequencyCounterSession;
        private NationalInstruments.Visa.ResourceManager _resourceManager;
        private readonly SemaphoreSlim _ioLock = new SemaphoreSlim(1, 1);
        private bool _disposed = false;

        private CancellationTokenSource _monitorCts;
        private Task _monitorTask;


        #region Properties

        private string _cardName;
        /// <summary>
        /// 仪表名称
        /// </summary>
        public string CardName
        {
            get => _cardName;
            set => SetProperty(ref _cardName, value);
        }

        private bool _isFrequencyCounterConnecting;
        public bool IsFrequencyCounterConnecting
        {
            get => _isFrequencyCounterConnecting;
            private set => SetProperty(ref _isFrequencyCounterConnecting, value);
        }

        private string _frequencyCounterIpAddress;
        /// <summary>
        /// 频率计设备IP地址
        /// </summary>
        public string FrequencyCounterIpAddress
        {
            get => _frequencyCounterIpAddress;
            set => SetProperty(ref _frequencyCounterIpAddress, value);
        }

        private string _frequencyCounterPort = "5025";
        public string FrequencyCounterPort
        {
            get => _frequencyCounterPort;
            set => SetProperty(ref _frequencyCounterPort, value);
        }

        private bool _isFrequencyCounterConnected;
        /// <summary>
        /// 频率计是否已连接
        /// </summary>
        public bool IsFrequencyCounterConnected
        {
            get => _isFrequencyCounterConnected;
            set
            {
                if (SetProperty(ref _isFrequencyCounterConnected, value))
                {
                    RaisePropertyChanged(nameof(FrequencyCounterConnectButtonText));
                }
            }
        }

        public bool CanClose()
        {
            if (IsFrequencyCounterConnecting)
            {
                ReMessageBox.Show($"正在打开频率计({CardName})，请稍候连接完成后再切换页面", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }

            return true;
        }

        /// <summary>
        /// 连接/断开 按钮文本
        /// </summary>
        public string FrequencyCounterConnectButtonText => IsFrequencyCounterConnected ? "断开中" : "连接中";


        private string _connectionStatus;
        /// <summary>
        /// 连接状态
        /// </summary>
        public string ConnectionStatus
        {
            get => _connectionStatus;
            set => SetProperty(ref _connectionStatus, value);
        }

        private string _variableName;

        private bool _isSwitchConnected;

        // 是否有匹配的开关配置（用于命令 CanExecute）
        private bool _hasMatchedSwitch;
        public bool HasMatchedSwitch
        {
            get => _hasMatchedSwitch;
            private set => SetProperty(ref _hasMatchedSwitch, value);
        }

        // 当前匹配的开关信息（只读，用于UI显示）
        private string _currentSwitchName;
        public string CurrentSwitchName
        {
            get => _currentSwitchName;
            private set => SetProperty(ref _currentSwitchName, value);
        }

        private string _currentSwitchInput;
        public string CurrentSwitchInput
        {
            get => _currentSwitchInput;
            private set => SetProperty(ref _currentSwitchInput, value);
        }

        private string _currentSwitchOutput;
        public string CurrentSwitchOutput
        {
            get => _currentSwitchOutput;
            private set => SetProperty(ref _currentSwitchOutput, value);
        }

        private string _testTaskName;
        /// <summary>
        /// 测试任务名称
        /// </summary>
        public string TestTaskName
        {
            get => _testTaskName;
            set => SetProperty(ref _testTaskName, value);
        }

        private string _configTableName;
        /// <summary>
        /// 配置表名称
        /// </summary>
        public string ConfigTableName
        {
            get => _configTableName;
            set => SetProperty(ref _configTableName, value);
        }

        private string _chassisName;
        /// <summary>
        /// 机箱名称
        /// </summary>
        public string ChassisName
        {
            get => _chassisName;
            set => SetProperty(ref _chassisName, value);
        }

        // 测量模式相关属性
        private ObservableCollection<string> _measurementModes;
        /// <summary>
        /// 测量模式列表
        /// </summary>
        public ObservableCollection<string> MeasurementModes
        {
            get => _measurementModes;
            set => SetProperty(ref _measurementModes, value);
        }

        private string _selectedMeasurementMode;
        /// <summary>
        /// 选中的测量模式
        /// </summary>
        public string SelectedMeasurementMode
        {
            get => _selectedMeasurementMode;
            set => SetProperty(ref _selectedMeasurementMode, value);
        }

        // 通道选择
        private ObservableCollection<string> _channels;
        /// <summary>
        /// 通道列表
        /// </summary>
        public ObservableCollection<string> Channels
        {
            get => _channels;
            set => SetProperty(ref _channels, value);
        }

        private string _selectedChannel;
        /// <summary>
        /// 选中的通道
        /// </summary>
        public string SelectedChannel
        {
            get => _selectedChannel;
            set => SetProperty(ref _selectedChannel, value);
        }

        private string _selectedAcquireChannel;
        /// <summary>
        /// 当前采集通道（单通道采集）。默认 CH1。
        /// </summary>
        public string SelectedAcquireChannel
        {
            get => _selectedAcquireChannel;
            set => SetProperty(ref _selectedAcquireChannel, value);
        }

        // 门控时间
        private string _gateTime;
        /// <summary>
        /// 门控时间（秒）
        /// </summary>
        public string GateTime
        {
            get => _gateTime;
            set => SetProperty(ref _gateTime, value);
        }

        // 测量结果
        private string _measurementResult;
        /// <summary>
        /// 测量结果
        /// </summary>
        public string MeasurementResult
        {
            get => _measurementResult;
            set => SetProperty(ref _measurementResult, value);
        }

        private string _ch1MeasurementResult;
        public string Ch1MeasurementResult
        {
            get => _ch1MeasurementResult;
            set => SetProperty(ref _ch1MeasurementResult, value);
        }

        private string _ch2MeasurementResult;
        public string Ch2MeasurementResult
        {
            get => _ch2MeasurementResult;
            set => SetProperty(ref _ch2MeasurementResult, value);
        }

        private string _ch1Vpp;
        public string Ch1Vpp
        {
            get => _ch1Vpp;
            set => SetProperty(ref _ch1Vpp, value);
        }

        private string _ch1Vmax;
        public string Ch1Vmax
        {
            get => _ch1Vmax;
            set => SetProperty(ref _ch1Vmax, value);
        }

        private string _ch1Vmin;
        public string Ch1Vmin
        {
            get => _ch1Vmin;
            set => SetProperty(ref _ch1Vmin, value);
        }

        private string _ch1Duty;
        public string Ch1Duty
        {
            get => _ch1Duty;
            set => SetProperty(ref _ch1Duty, value);
        }

        private string _ch2Vpp;
        public string Ch2Vpp
        {
            get => _ch2Vpp;
            set => SetProperty(ref _ch2Vpp, value);
        }

        private string _ch2Vmax;
        public string Ch2Vmax
        {
            get => _ch2Vmax;
            set => SetProperty(ref _ch2Vmax, value);
        }

        private string _ch2Vmin;
        public string Ch2Vmin
        {
            get => _ch2Vmin;
            set => SetProperty(ref _ch2Vmin, value);
        }

        private string _ch2Duty;
        public string Ch2Duty
        {
            get => _ch2Duty;
            set => SetProperty(ref _ch2Duty, value);
        }

        private string _measurementStatus;
        /// <summary>
        /// 测量状态
        /// </summary>
        public string MeasurementStatus
        {
            get => _measurementStatus;
            set => SetProperty(ref _measurementStatus, value);
        }

        private bool _isMonitoring;
        public bool IsMonitoring
        {
            get => _isMonitoring;
            private set
            {
                if (SetProperty(ref _isMonitoring, value))
                {
                    RaisePropertyChanged(nameof(MonitorButtonText));
                }
            }
        }

        public string MonitorButtonText => IsMonitoring ? "停止测量" : "开始测量";

        private ObservableCollection<ChannelMeasurementItem> _channelMeasurements;
        public ObservableCollection<ChannelMeasurementItem> ChannelMeasurements
        {
            get => _channelMeasurements;
            private set => SetProperty(ref _channelMeasurements, value);
        }

        #endregion

        #region Commands

        public ICommand ConnectFrequencyCounterCommand { get; private set; }
        public ICommand DisconnectFrequencyCounterCommand { get; private set; }
        public ICommand ToggleDeviceCommand { get; private set; }
        public ICommand ToggleSwitchConnectionCommand { get; private set; }
        public ICommand MeasureCommand { get; private set; }
        public ICommand SearchVariableCommand { get; private set; }
        public ICommand ToggleMonitoringCommand { get; private set; }

        public ICommand SelectAcquireChannelCommand { get; private set; }

        #endregion

        #region Constructor

        public FrequencyCounterTestPanelViewModel(IPxiChassisService pxiChassisService)
        {
            _pxiChassisService = pxiChassisService;
            _resourceManager = new NationalInstruments.Visa.ResourceManager();

            ConnectionStatus = "离线";

            FrequencyCounterIpAddress = "192.168.1.14";

            // 初始化测量模式列表
            MeasurementModes = new ObservableCollection<string>
            {
                "频率(FREQ)", "周期(PER)", "时间间隔(TINT)", "脉冲宽度(PWID)", "占空比(DUTY)"
            };
            SelectedMeasurementMode = MeasurementModes[0]; // 默认选择频率

            // 初始化通道列表
            Channels = new ObservableCollection<string> { "CH1", "CH2" };
            SelectedChannel = Channels[0]; // 默认选择CH1
            SelectedAcquireChannel = "CH1";

            ChannelMeasurements = new ObservableCollection<ChannelMeasurementItem>(
                Channels.Select(c => new ChannelMeasurementItem { Channel = c, IsEnabled = false }));

            // 初始化门控时间
            GateTime = "1.0";

            MeasurementResult = Placeholder;
            Ch1MeasurementResult = Placeholder;
            Ch2MeasurementResult = Placeholder;
            Ch1Vpp = Placeholder;
            Ch1Vmax = Placeholder;
            Ch1Vmin = Placeholder;
            Ch1Duty = Placeholder;
            Ch2Vpp = Placeholder;
            Ch2Vmax = Placeholder;
            Ch2Vmin = Placeholder;
            Ch2Duty = Placeholder;
            MeasurementStatus = "未测量";

            InitializeCommands();
        }

        public FrequencyCounterTestPanelViewModel(string testTaskName, string configTableName, string chassisName,
            IPxiChassisService pxiChassisService) : this(pxiChassisService)
        {
            TestTaskName = testTaskName;
            ConfigTableName = configTableName;
            ChassisName = chassisName;
        }

        private void InitializeCommands()
        {
            ConnectFrequencyCounterCommand = new DelegateCommand(async () => await ConnectFrequencyCounterAsync(),
                () => !IsFrequencyCounterConnected &&
                      !string.IsNullOrEmpty(FrequencyCounterIpAddress))
                .ObservesProperty(() => IsFrequencyCounterConnected)
                .ObservesProperty(() => FrequencyCounterIpAddress)
                .ObservesProperty(() => FrequencyCounterPort);

            DisconnectFrequencyCounterCommand = new DelegateCommand(async () => await DisconnectFrequencyCounterAsync(),
                () => IsFrequencyCounterConnected)
                .ObservesProperty(() => IsFrequencyCounterConnected);

            SelectAcquireChannelCommand = new DelegateCommand<string>(ch =>
            {
                if (string.IsNullOrWhiteSpace(ch))
                    return;

                if (!string.Equals(SelectedAcquireChannel, ch, StringComparison.OrdinalIgnoreCase))
                {
                    SelectedAcquireChannel = ch;
                    _forceSlowTick = true;
                }
            });

            ToggleDeviceCommand = new DelegateCommand(async () => await ToggleDeviceAsync(),
                () => true)
                .ObservesProperty(() => IsFrequencyCounterConnected);
            ToggleMonitoringCommand = new DelegateCommand(async () => await ToggleMonitoringAsync(),
                () => IsFrequencyCounterConnected)
                .ObservesProperty(() => IsFrequencyCounterConnected);
        }

        #endregion

        #region Methods
        /// <summary>
        /// 连接频率计设备
        /// </summary>
        private async Task ToggleDeviceAsync()
        {
            if (IsFrequencyCounterConnected)
            {
                await DisconnectFrequencyCounterAsync();
            }
            else
            {
                await ConnectFrequencyCounterAsync();
            }
        }

        private async Task ConnectFrequencyCounterAsync()
        {
            if (string.IsNullOrEmpty(FrequencyCounterIpAddress))
            {
                ReMessageBox.Show("请输入频率计设备IP地址", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                ConnectionStatus = "检测中";
                IsFrequencyCounterConnected = false;
                IsFrequencyCounterConnecting = true;

                string deviceInfo = null;
                await Task.Run(async () =>
                {
                    string resourceString = $"TCPIP0::{FrequencyCounterIpAddress}::INSTR";
                    _frequencyCounterSession = (NationalInstruments.Visa.MessageBasedSession)_resourceManager.Open(resourceString, 0, 5000);

                    try
                    {
                        _frequencyCounterSession.TimeoutMilliseconds = 3000;
                        _frequencyCounterSession.TerminationCharacterEnabled = true;
                        _frequencyCounterSession.TerminationCharacter = (byte)'\n';
                    }
                    catch
                    {
                    }

                    try
                    {
                        await SendScpiAsync("*CLS", CancellationToken.None);
                        await DrainErrorQueueAsync(CancellationToken.None, "After *CLS");
                    }
                    catch
                    {
                    }

                    deviceInfo = (await QueryScpiAsync("*IDN?", CancellationToken.None)).Trim();
                    System.Diagnostics.Debug.WriteLine($"[FrequencyCounterTestPanel] 频率计设备信息: {deviceInfo}");

                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        ConnectionStatus = $"在线";
                        IsFrequencyCounterConnected = true;
                    });
                });

                await StartMonitoringAsync();
            }
            catch (Exception ex)
            {
                ConnectionStatus = "离线";
                IsFrequencyCounterConnected = false;
                ReMessageBox.Show($"连接失败: {ex.Message}", "连接错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                System.Diagnostics.Debug.WriteLine($"[FrequencyCounterTestPanel] 连接失败: {ex.Message}");

                MeasurementResult = Placeholder;
                Ch1MeasurementResult = Placeholder;
                Ch2MeasurementResult = Placeholder;
                Ch1Vpp = Placeholder;
                Ch1Vmax = Placeholder;
                Ch1Vmin = Placeholder;
                Ch1Duty = Placeholder;
                Ch2Vpp = Placeholder;
                Ch2Vmax = Placeholder;
                Ch2Vmin = Placeholder;
                Ch2Duty = Placeholder;
                MeasurementStatus = "未测量";

                // 清理已连接的资源
                try
                {
                    await StopMonitoringAsync();
                    SafeCloseFrequencyCounterSession();
                }
                catch { }
            }
            finally
            {
                IsFrequencyCounterConnecting = false;
            }
        }

        /// <summary>
        /// 断开频率计设备连接
        /// </summary>
        private async Task DisconnectFrequencyCounterAsync()
        {
            try
            {
                ConnectionStatus = "断开中";

                await Task.Run(async () =>
                {
                    // 断开频率计
                    await StopMonitoringAsync();
                    SafeCloseFrequencyCounterSession();
                });

                IsFrequencyCounterConnected = false;
                MeasurementResult = Placeholder;
                Ch1MeasurementResult = Placeholder;
                Ch2MeasurementResult = Placeholder;
                Ch1Vpp = Placeholder;
                Ch1Vmax = Placeholder;
                Ch1Vmin = Placeholder;
                Ch1Duty = Placeholder;
                Ch2Vpp = Placeholder;
                Ch2Vmax = Placeholder;
                Ch2Vmin = Placeholder;
                Ch2Duty = Placeholder;
                MeasurementStatus = "未测量";
                ConnectionStatus = "离线";

            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FrequencyCounterTestPanel] 断开连接失败: {ex.Message}");
                ConnectionStatus = "断开失败";

                Application.Current.Dispatcher.Invoke(() =>
                {
                    IsFrequencyCounterConnected = false;
                    ConnectionStatus = "断开失败";
                });
            }
        }

        /// <summary>
        /// 执行测量
        /// </summary>
        private async Task MeasureAsync()
        {
            if (!IsFrequencyCounterConnected || _frequencyCounterSession == null)
            {
                ReMessageBox.Show("频率计未连接", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                MeasurementStatus = "在线";
                MeasurementResult = "";

                var raw = await MeasureOnceAsync(SelectedChannel, CancellationToken.None);
                var result = FormatValueForSelectedMode(raw, SelectedMeasurementMode);

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    MeasurementResult = result;
                    MeasurementStatus = "测量完成";

                    if (!string.IsNullOrWhiteSpace(SelectedChannel) && SelectedChannel.IndexOf("CH2", StringComparison.OrdinalIgnoreCase) >= 0)
                        Ch2MeasurementResult = result;
                    else
                        Ch1MeasurementResult = result;
                });

                System.Diagnostics.Debug.WriteLine($"[FrequencyCounterTestPanel] 测量结果: {result}");
            }
            catch (Exception ex)
            {
                MeasurementStatus = "测量失败";
                MeasurementResult = "";
                ReMessageBox.Show($"测量失败: {ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                System.Diagnostics.Debug.WriteLine($"[FrequencyCounterTestPanel] 测量失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取测量模式对应的SCPI命令
        /// </summary>
        private string GetMeasurementModeCommand(string mode)
        {
            if (string.IsNullOrEmpty(mode))
                return null;

            if (mode.Contains("频率") || mode.Contains("FREQ"))
                return "CONF:FREQ";
            else if (mode.Contains("周期") || mode.Contains("PER"))
                return "CONF:PER";
            else if (mode.Contains("时间间隔") || mode.Contains("TINT"))
                return "CONF:TINT";
            else if (mode.Contains("脉冲宽度") || mode.Contains("PWID"))
                return "CONF:PWID";
            else if (mode.Contains("占空比") || mode.Contains("DUTY"))
                return "CONF:DUTY";

            return "CONF:FREQ"; // 默认频率测量
        }

        /// <summary>
        /// 获取通道对应的SCPI命令
        /// </summary>
        private string GetChannelCommand(string channel)
        {
            if (string.IsNullOrEmpty(channel))
                return null;

            if (channel.Contains("CH1") || channel.Contains("1"))
                return "ROUT:CHAN (@1)";
            else if (channel.Contains("CH2") || channel.Contains("2"))
                return "ROUT:CHAN (@2)";

            return "ROUT:CHAN (@1)"; // 默认通道1
        }

        private async Task ToggleMonitoringAsync()
        {
            if (IsMonitoring)
            {
                await StopMonitoringAsync();
            }
            else
            {
                await StartMonitoringAsync();
            }
        }

        private async Task StartMonitoringAsync()
        {
            if (!IsFrequencyCounterConnected || _frequencyCounterSession == null)
                return;

            if (IsMonitoring)
                return;

            _monitorCts = new CancellationTokenSource();
            var token = _monitorCts.Token;
            IsMonitoring = true;

            _monitorTask = Task.Run(async () =>
            {
                int tick = 0;
                while (!token.IsCancellationRequested)
                {
                    string acquire = SelectedAcquireChannel;
                    int ch = string.Equals(acquire, "CH2", StringComparison.OrdinalIgnoreCase) ? 2 : 1;
                    bool force = _forceSlowTick;
                    bool slowTick = force || (tick % SlowPollEveryNFastTicks) == 0;

                    string newFreq = (ch == 1) ? Ch1MeasurementResult : Ch2MeasurementResult;
                    string newDuty = (ch == 1) ? Ch1Duty : Ch2Duty;
                    string newVpp = (ch == 1) ? Ch1Vpp : Ch2Vpp;
                    string newVmin = (ch == 1) ? Ch1Vmin : Ch2Vmin;
                    string newVmax = (ch == 1) ? Ch1Vmax : Ch2Vmax;

                    try { newFreq = FormatFrequencyHz(ParseOrNaN(await QueryScpiAsync($"MEASure:FREQuency? (@{ch})", token))); }
                    catch (Exception ex) { await MaybeDrainErrorQueueAsync(token, $"Poll FREQ CH{ch}: {ex.Message}"); }

                    try { newDuty = FormatPercent(ParseOrNaN(await QueryScpiAsync($"MEASure:PDUTycycle? 50,(@{ch})", token))); }
                    catch (Exception ex) { await MaybeDrainErrorQueueAsync(token, $"Poll PDUT CH{ch}: {ex.Message}"); }

                    if (slowTick)
                    {
                        try { newVpp = FormatVoltageV(ParseOrNaN(await QueryScpiAsync($"INPut{ch}:LEVel:PTPeak?", token))); }
                        catch (Exception ex) { await MaybeDrainErrorQueueAsync(token, $"Poll VPP CH{ch}: {ex.Message}"); }

                        try { newVmax = FormatVoltageV(ParseOrNaN(await QueryScpiAsync($"INPut{ch}:LEVel:MAXimum?", token))); }
                        catch (Exception ex) { await MaybeDrainErrorQueueAsync(token, $"Poll VMAX CH{ch}: {ex.Message}"); }

                        try { newVmin = FormatVoltageV(ParseOrNaN(await QueryScpiAsync($"INPut{ch}:LEVel:MINimum?", token))); }
                        catch (Exception ex) { await MaybeDrainErrorQueueAsync(token, $"Poll VMIN CH{ch}: {ex.Message}"); }
                    }

                    if (force)
                        _forceSlowTick = false;

                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        if (ch == 1)
                        {
                            Ch1MeasurementResult = string.IsNullOrWhiteSpace(newFreq) ? Placeholder : newFreq;
                            Ch1Duty = string.IsNullOrWhiteSpace(newDuty) ? Placeholder : newDuty;
                            if (slowTick)
                            {
                                Ch1Vpp = string.IsNullOrWhiteSpace(newVpp) ? Ch1Vpp : newVpp;
                                Ch1Vmin = string.IsNullOrWhiteSpace(newVmin) ? Ch1Vmin : newVmin;
                                Ch1Vmax = string.IsNullOrWhiteSpace(newVmax) ? Ch1Vmax : newVmax;
                            }
                        }
                        else
                        {
                            Ch2MeasurementResult = string.IsNullOrWhiteSpace(newFreq) ? Placeholder : newFreq;
                            Ch2Duty = string.IsNullOrWhiteSpace(newDuty) ? Placeholder : newDuty;
                            if (slowTick)
                            {
                                Ch2Vpp = string.IsNullOrWhiteSpace(newVpp) ? Ch2Vpp : newVpp;
                                Ch2Vmin = string.IsNullOrWhiteSpace(newVmin) ? Ch2Vmin : newVmin;
                                Ch2Vmax = string.IsNullOrWhiteSpace(newVmax) ? Ch2Vmax : newVmax;
                            }
                        }
                    });

                    tick++;
                    await Task.Delay(FastPollIntervalMs, token);
                }
            }, token);
        }

        private static double ParseOrNaN(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return double.NaN;

            if (TryParseInvariantDouble(raw.Trim(), out var v))
                return v;

            return double.NaN;
        }

        private static string FormatValueForSelectedMode(string raw, string selectedMeasurementMode)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return Placeholder;

            string s = raw.Trim();
            if (!TryParseInvariantDouble(s, out var v))
                return s;

            if (!string.IsNullOrWhiteSpace(selectedMeasurementMode))
            {
                if (selectedMeasurementMode.IndexOf("DUTY", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    selectedMeasurementMode.IndexOf("占空比", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return FormatPercent(v);
                }

                if (selectedMeasurementMode.IndexOf("FREQ", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    selectedMeasurementMode.IndexOf("频率", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return FormatFrequencyHz(v);
                }

                if (selectedMeasurementMode.IndexOf("PER", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    selectedMeasurementMode.IndexOf("TINT", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    selectedMeasurementMode.IndexOf("PWID", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    selectedMeasurementMode.IndexOf("周期", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    selectedMeasurementMode.IndexOf("时间", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    selectedMeasurementMode.IndexOf("宽度", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return FormatTimeSeconds(v);
                }
            }

            return s;
        }

        private static bool TryParseInvariantDouble(string raw, out double value)
        {
            return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private static string FormatPercent(double ratio01)
        {
            if (double.IsNaN(ratio01) || double.IsInfinity(ratio01))
                return Placeholder;

            double pct = ratio01 * 100.0;
            return pct.ToString("0.00000", CultureInfo.InvariantCulture) + " %";
        }

        private static string FormatFrequencyHz(double hz)
        {
            if (double.IsNaN(hz) || double.IsInfinity(hz))
                return Placeholder;

            if (Math.Abs(hz) > 200e6)
                return "超出量程";

            return FormatEngineering(hz,
                new (double Factor, string Unit)[]
                {
                    (1e9, "GHz"),
                    (1e6, "MHz"),
                    (1e3, "kHz"),
                    (1.0, "Hz"),
                    (1e-3, "mHz"),
                    (1e-6, "μHz")
                });
        }

        private static string FormatTimeSeconds(double seconds)
        {
            double abs = Math.Abs(seconds);

            if (double.IsNaN(seconds) || double.IsInfinity(seconds))
                return Placeholder;

            if (abs >= 1.0)
                return seconds.ToString("0.00000", CultureInfo.InvariantCulture) + " s";
            if (abs >= 1e-3)
                return (seconds * 1e3).ToString("0.00000", CultureInfo.InvariantCulture) + " ms";
            if (abs >= 1e-6)
                return (seconds * 1e6).ToString("0.00000", CultureInfo.InvariantCulture) + " μs";
            if (abs >= 1e-9)
                return (seconds * 1e9).ToString("0.00000", CultureInfo.InvariantCulture) + " ns";
            return (seconds * 1e12).ToString("0.00000", CultureInfo.InvariantCulture) + " ps";
        }

        private static string FormatVoltageV(double volts)
        {
            return FormatEngineering(volts,
                new (double Factor, string Unit)[]
                {
                    (1e3, "kV"),
                    (1.0, "V"),
                    (1e-3, "mV"),
                    (1e-6, "μV"),
                    (1e-9, "nV")
                });
        }

        private static string FormatEngineering(double value, (double Factor, string Unit)[] units)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                return Placeholder;

            double abs = Math.Abs(value);
            if (abs == 0)
                return "0.00000 " + units.Last().Unit;

            foreach (var u in units)
            {
                if (abs >= u.Factor)
                {
                    double scaled = value / u.Factor;
                    return scaled.ToString("0.00000", CultureInfo.InvariantCulture) + " " + u.Unit;
                }
            }

            var last = units.Last();
            return (value / last.Factor).ToString("0.00000", CultureInfo.InvariantCulture) + " " + last.Unit;
        }

        private async Task StopMonitoringAsync()
        {
            if (!IsMonitoring)
                return;

            try
            {
                _monitorCts?.Cancel();
            }
            catch { }

            try
            {
                if (_monitorTask != null)
                    await _monitorTask;
            }
            catch { }

            _monitorTask = null;
            _monitorCts?.Dispose();
            _monitorCts = null;
            IsMonitoring = false;
        }

        private async Task<string> MeasureOnceAsync(string channel, CancellationToken token)
        {
            string modeCommand = GetMeasurementModeCommand(SelectedMeasurementMode);
            if (!string.IsNullOrEmpty(modeCommand))
            {
                await SendScpiAsync(modeCommand, token);
            }

            string channelCommand = GetChannelCommand(channel);
            if (!string.IsNullOrEmpty(channelCommand))
            {
                await SendScpiAsync(channelCommand, token);
            }

            if (!string.IsNullOrEmpty(GateTime) && double.TryParse(GateTime, out double gateTimeValue))
            {
                await SendScpiAsync($"FREQ:GATE:TIME {gateTimeValue}", token);
            }

            await SendScpiAsync("INIT", token);
            await SendScpiAsync("*WAI", token);

            var result = await QueryScpiAsync("FETCH?", token);
            return result.Trim();
        }

        private async Task SendScpiAsync(string command, CancellationToken token)
        {
            if (_frequencyCounterSession == null)
                throw new InvalidOperationException("频率计未连接");

            await _ioLock.WaitAsync(token);
            try
            {
                await Task.Run(() =>
                {
                    var cmd = command.EndsWith("\n", StringComparison.Ordinal) ? command : command + "\n";
                    _frequencyCounterSession.RawIO.Write(cmd);
                }, token);
            }
            finally
            {
                _ioLock.Release();
            }
        }

        private async Task MaybeDrainErrorQueueAsync(CancellationToken token, string context)
        {
            try
            {
                var now = DateTime.UtcNow;
                if ((now - _lastErrorQueueDrainUtc).TotalMilliseconds < ErrorDrainMinIntervalMs)
                    return;

                _lastErrorQueueDrainUtc = now;
                await DrainErrorQueueAsync(token, context);
            }
            catch
            {
            }
        }

        private async Task DrainErrorQueueAsync(CancellationToken token, string context)
        {
            try
            {
                for (int i = 0; i < 20; i++)
                {
                    string err = (await QueryScpiAsync("SYST:ERR?", token)).Trim();
                    System.Diagnostics.Debug.WriteLine($"[FrequencyCounterTestPanel][SYST:ERR][{context}] {err}");

                    if (err.StartsWith("+0", StringComparison.OrdinalIgnoreCase) ||
                        err.IndexOf("No error", StringComparison.OrdinalIgnoreCase) >= 0)
                        break;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FrequencyCounterTestPanel][SYST:ERR][{context}] 读取失败: {ex.Message}");
            }
        }

        private async Task<string> QueryScpiAsync(string command, CancellationToken token)
        {
            if (_frequencyCounterSession == null)
                throw new InvalidOperationException("频率计未连接");

            await _ioLock.WaitAsync(token);
            try
            {
                return await Task.Run(() =>
                {
                    var cmd = command.EndsWith("\n", StringComparison.Ordinal) ? command : command + "\n";
                    _frequencyCounterSession.RawIO.Write(cmd);
                    return _frequencyCounterSession.RawIO.ReadString();
                }, token);
            }
            finally
            {
                _ioLock.Release();
            }
        }

        private void SafeCloseFrequencyCounterSession()
        {
            try { _frequencyCounterSession?.Dispose(); } catch { }
            _frequencyCounterSession = null;
        }

        /// <summary>
        /// 获取拓扑字符串
        /// </summary>
        private string GetTopologyString(string topology)
        {
            if (string.IsNullOrEmpty(topology))
                return null;

            switch (topology)
            {
                case "4*32Matrix":
                case "4x32 Matrix":
                    return artSwitchTopologies.ARTSWITCH_TOPOLOGY_2601_2_WIRE_4X32_MATRIX;
                case "8*16Matrix":
                case "8x16 Matrix":
                    return artSwitchTopologies.ARTSWITCH_TOPOLOGY_2601_2_WIRE_8X16_MATRIX;
                case "4*64Matrix":
                case "4x64 Matrix":
                    return artSwitchTopologies.ARTSWITCH_TOPOLOGY_2601_2_WIRE_4X32_MATRIX;
                default:
                    return null;
            }
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    try
                    {
                        StopMonitoringAsync().Wait(TimeSpan.FromSeconds(3));
                        SafeCloseFrequencyCounterSession();

                        if (_resourceManager != null)
                        {
                            try { _resourceManager.Dispose(); } catch { }
                            _resourceManager = null;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[FrequencyCounterTestPanel] Dispose失败: {ex.Message}");
                    }
                }
                _disposed = true;
            }
        }

        public class ChannelMeasurementItem : BindableBase
        {
            private string _channel;
            public string Channel
            {
                get => _channel;
                set => SetProperty(ref _channel, value);
            }

            private bool _isEnabled;
            public bool IsEnabled
            {
                get => _isEnabled;
                set => SetProperty(ref _isEnabled, value);
            }

            private string _lastValue;
            public string LastValue
            {
                get => _lastValue;
                set => SetProperty(ref _lastValue, value);
            }

            private DateTime _lastUpdateTime;
            public DateTime LastUpdateTime
            {
                get => _lastUpdateTime;
                set => SetProperty(ref _lastUpdateTime, value);
            }
        }

        #endregion
    }
}

