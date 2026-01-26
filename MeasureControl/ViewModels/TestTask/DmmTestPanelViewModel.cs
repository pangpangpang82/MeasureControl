using System;
using System.Globalization;
using System.Net;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using MeasureControl.Services;
using MeasureControl.ViewModels.Dialogs;
using MeasureControl.Views.Dialogs;
using NationalInstruments.Visa;
using Prism.Commands;
using Prism.Mvvm;

namespace MeasureControl.ViewModels.TestTask
{
    /// <summary>
    /// DMM独立测试面板ViewModel
    /// </summary>
    public class DmmTestPanelViewModel : BindableBase, IDisposable, ICloseGuard
    {
        private const int DefaultPort = 5555;

        private readonly IPxiChassisService _pxiChassisService;
        private NationalInstruments.Visa.MessageBasedSession _dmmSession;
        private NationalInstruments.Visa.ResourceManager _resourceManager;
        private readonly SemaphoreSlim _dmmIoLock = new SemaphoreSlim(1, 1);
        private CancellationTokenSource _pollingCts;
        private Task _pollingTask;
        private TaskCompletionSource<bool> _pollNowTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _disposed = false;

        private volatile bool _isFreqConfigPending;
        private int _lastFreqRangeIndex = 2;

        public sealed class DmmHistoryItem
        {
            public string TimeText { get; set; }
            public string ValueText { get; set; }
        }

        #region Properties

        private string _cardName = "DM3068";

        public string CardName
        {
            get => _cardName;
            set => SetProperty(ref _cardName, value);
        }

        private string _dmmIpAddress = "192.168.1.13";

        /// <summary>
        /// DMM设备IP地址
        /// </summary>
        public string DmmIpAddress
        {
            get => _dmmIpAddress;
            set => SetProperty(ref _dmmIpAddress, value);
        }

        private string _connectionStatus = "离线";

        public string ConnectionStatus
        {
            get => _connectionStatus;
            set => SetProperty(ref _connectionStatus, value);
        }

        private string _measurementResult;

        public string MeasurementResult
        {
            get => _measurementResult;
            set => SetProperty(ref _measurementResult, value);
        }

        private ObservableCollection<DmmHistoryItem> _historyItems = new ObservableCollection<DmmHistoryItem>();
        public ObservableCollection<DmmHistoryItem> HistoryItems
        {
            get => _historyItems;
            set => SetProperty(ref _historyItems, value);
        }

        private int _pollingIntervalMs = 200;

        public int PollingIntervalMs
        {
            get => _pollingIntervalMs;
            set => SetProperty(ref _pollingIntervalMs, value);
        }

        private DmmMeasureMode _selectedMode;
        public DmmMeasureMode SelectedMode
        {
            get => _selectedMode;
            set
            {
                if (SetProperty(ref _selectedMode, value))
                {
                    RaisePropertyChanged(nameof(SelectedModeText));
                }
            }
        }

        public string SelectedModeText => SelectedMode.ToString();

        private bool _isDmmConnected;

        /// <summary>
        /// DMM是否已连接
        /// </summary>
        public bool IsDmmConnected
        {
            get => _isDmmConnected;
            set
            {
                if (SetProperty(ref _isDmmConnected, value))
                {
                    RaisePropertyChanged(nameof(DmmConnectButtonText));
                }
            }
        }

        public string DmmConnectButtonText => IsDmmConnected ? "关闭设备" : "打开设备";

        private bool _isDmmConnecting;
        public bool IsDmmConnecting
        {
            get => _isDmmConnecting;
            private set => SetProperty(ref _isDmmConnecting, value);
        }

        #endregion

        #region Commands

        public ICommand ToggleDeviceCommand { get; private set; }
        public ICommand SetModeCommand { get; private set; }

        #endregion

        #region Constructor

        public DmmTestPanelViewModel(IPxiChassisService pxiChassisService)
        {
            _pxiChassisService = pxiChassisService;
            _resourceManager = new NationalInstruments.Visa.ResourceManager();
            ConnectionStatus = "离线";
            MeasurementResult = "-------";

            // 默认模式：直流电压（应用启动/项目重新打开时使用默认值）
            SelectedMode = DmmMeasureMode.DCV;

            InitializeCommands();
        }

        public DmmTestPanelViewModel(string testTaskName, string configTableName, string chassisName,
            IPxiChassisService pxiChassisService) : this(pxiChassisService)
        {
        }

        private void InitializeCommands()
        {
            ToggleDeviceCommand = new DelegateCommand(async () => await ToggleDeviceAsync(),
                () => true)
                .ObservesProperty(() => IsDmmConnected);

            SetModeCommand = new DelegateCommand<string>(SetModeFromUi, _ => IsDmmConnected)
                .ObservesProperty(() => IsDmmConnected);
        }

        #endregion

        #region Methods

        private async Task ToggleDeviceAsync()
        {
            if (IsDmmConnected)
            {
                await DisconnectDmmAsync();
            }
            else
            {
                await ConnectDmmAsync();
            }
        }

        private async void SetModeFromUi(string mode)
        {
            if (string.IsNullOrWhiteSpace(mode))
                return;

            if (Enum.TryParse(mode, true, out DmmMeasureMode parsed))
            {
                var previousMode = SelectedMode;

                // 如果重复点击当前模式：不显示“-------”，但仍触发一次立即刷新
                if (parsed == SelectedMode)
                {
                    if (IsDmmConnected && parsed == DmmMeasureMode.FREQ)
                    {
                        // 允许再次点击频率模式，重新选择量程
                        MeasurementResult = "-------";

                        var selectedIndex = ShowFreqVoltageRangeDialog(_lastFreqRangeIndex);
                        if (!selectedIndex.HasValue)
                            return;

                        _lastFreqRangeIndex = selectedIndex.Value;

                        _isFreqConfigPending = true;
                        try
                        {
                            var ok = await ApplyFrequencyRangeAsync(selectedIndex.Value).ConfigureAwait(true);
                            if (ok)
                            {
                                RequestPollNow();
                                _ = StartPollingAsync();
                            }
                        }
                        finally
                        {
                            _isFreqConfigPending = false;
                        }
                    }

                    return;
                }

                if (IsDmmConnected && parsed == DmmMeasureMode.FREQ)
                {
                    // 只有点击“确定”后才真正切换到频率模式；取消则保持原模式不变
                    MeasurementResult = "-------";

                    var selectedIndex = ShowFreqVoltageRangeDialog(_lastFreqRangeIndex);
                    if (!selectedIndex.HasValue)
                    {
                        MeasurementResult = "-------";
                        return;
                    }

                    _lastFreqRangeIndex = selectedIndex.Value;

                    _isFreqConfigPending = true;
                    try
                    {
                        var ok = await ApplyFrequencyRangeAsync(selectedIndex.Value).ConfigureAwait(true);
                        if (!ok)
                        {
                            // 用户取消：不切换模式
                            MeasurementResult = "-------";
                            return;
                        }
                    }
                    finally
                    {
                        _isFreqConfigPending = false;
                    }

                    SelectedMode = parsed;
                    RequestPollNow();
                    _ = StartPollingAsync();
                    return;
                }

                SelectedMode = parsed;
                if (IsDmmConnected)
                {
                    MeasurementResult = "-------";
                    RequestPollNow();
                    _ = StartPollingAsync();
                }
            }
        }

        public void OnViewLoaded()
        {
            if (IsDmmConnected)
            {
                MeasurementResult = "-------";
                RequestPollNow();
                _ = StartPollingAsync();
            }
        }

        private void RequestPollNow()
        {
            var newTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var old = Interlocked.Exchange(ref _pollNowTcs, newTcs);
            try
            {
                old?.TrySetResult(true);
            }
            catch
            {
            }
        }

        private async Task ConnectDmmAsync()
        {
            if (string.IsNullOrEmpty(DmmIpAddress))
            {
                ReMessageBox.Show("请输入DMM设备IP地址", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var ip = DmmIpAddress?.Trim();
            if (!IPAddress.TryParse(ip, out _))
            {
                ReMessageBox.Show("请输入有效的IP地址", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (IsDmmConnected && _dmmSession != null)
            {
                await StartPollingAsync();
                return;
            }

            try
            {
                ConnectionStatus = "检测中";
                IsDmmConnected = false;
                IsDmmConnecting = true;

                await Task.Run(async () =>
                {
                    // 构建VISA资源字符串（固定端口 5555，SOCKET 方式）
                    string resourceString = $"TCPIP0::{ip}::{DefaultPort}::SOCKET";

                    // 打开VISA会话（连接DMM）
                    _dmmSession = (NationalInstruments.Visa.MessageBasedSession)_resourceManager.Open(resourceString, 0, 5000);

                    try
                    {
                        _dmmSession.TimeoutMilliseconds = 8000;
                        _dmmSession.TerminationCharacterEnabled = true;
                        _dmmSession.TerminationCharacter = (byte)'\n';
                    }
                    catch
                    {
                        // 部分 VISA 实现可能不支持这些属性，忽略即可
                    }

                    // 查询设备信息
                    string deviceInfo = await QueryDmmStringAsync("*IDN?").ConfigureAwait(false);
                    deviceInfo = deviceInfo?.Trim();

                    System.Diagnostics.Debug.WriteLine($"[DmmTestPanel] DMM设备信息: {deviceInfo}");

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        IsDmmConnected = true;

                        ConnectionStatus = $"在线";
                    });
                });

                MeasurementResult = "-------";
                RequestPollNow();
                await StartPollingAsync();
            }
            catch (Exception ex)
            {
                ConnectionStatus = "离线";
                IsDmmConnected = false;
                MeasurementResult = "-------";

                ReMessageBox.Show($"连接DMM设备失败", "连接错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                System.Diagnostics.Debug.WriteLine($"[DmmTestPanel] 连接失败: {ex.Message}");
            }
            finally
            {
                IsDmmConnecting = false;
            }
        }

        /// <summary>
        /// 断开DMM设备连接
        /// </summary>
        private async Task DisconnectDmmAsync()
        {
            try
            {
                ConnectionStatus = "断开中";

                await StopPollingAsync();

                await Task.Run(async () =>
                {
                    // 断开DMM
                    if (_dmmSession != null)
                    {
                        _dmmSession.Dispose();
                        _dmmSession = null;
                    }
                });

                IsDmmConnected = false;
                ConnectionStatus = "离线";
                MeasurementResult = "-------";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DmmTestPanel] 断开连接失败: {ex.Message}");
                ConnectionStatus = "断开失败";

                // 即使出错，也要更新界面状态
                Application.Current.Dispatcher.Invoke(() =>
                {
                    IsDmmConnected = false;
                    ConnectionStatus = "断开失败";
                });
            }
        }

        private async Task StartPollingAsync()
        {
            if (!IsDmmConnected || _dmmSession == null)
                return;

            if (_pollingTask != null && !_pollingTask.IsCompleted)
                return;

            _pollingCts?.Cancel();
            _pollingCts = new CancellationTokenSource();
            var token = _pollingCts.Token;

            _pollingTask = Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        var (cmd, unit, label) = GetQueryForMode(SelectedMode);
                        if (string.IsNullOrWhiteSpace(cmd))
                        {
                            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                            {
                                MeasurementResult = $"{label}: 该模式未配置查询指令";
                            }));
                            await Task.Delay(Math.Max(200, PollingIntervalMs), token).ConfigureAwait(false);
                            continue;
                        }

                        if (_isFreqConfigPending)
                        {
                            await Task.Delay(50, token).ConfigureAwait(false);
                            continue;
                        }

                        var raw = await QueryDmmStringAsync(cmd).ConfigureAwait(false);
                        raw = raw?.Trim();

                        string displayValue = FormatDmmReading(raw, unit);

                        Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            MeasurementResult = displayValue;

                            var item = new DmmHistoryItem
                            {
                                TimeText = DateTime.Now.ToString("HH:mm:ss.fff"),
                                ValueText = displayValue
                            };

                            HistoryItems.Insert(0, item);
                            while (HistoryItems.Count > 8)
                            {
                                HistoryItems.RemoveAt(HistoryItems.Count - 1);
                            }
                        }));
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            MeasurementResult = $"轮询失败: {ex.Message}";
                        }));
                    }

                    try
                    {
                        var delayTask = Task.Delay(Math.Max(200, PollingIntervalMs), token);
                        var pollNowTask = _pollNowTcs.Task;
                        await Task.WhenAny(delayTask, pollNowTask).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }, token);
        }

        private Task StopPollingAsync()
        {
            try
            {
                _pollingCts?.Cancel();
            }
            catch
            {
            }

            return Task.CompletedTask;
        }

        private (string QueryCmd, string Unit, string Label) GetQueryForMode(DmmMeasureMode mode)
        {
            switch (mode)
            {
                case DmmMeasureMode.DCV:
                    return (":MEAS:VOLT:DC?", "V", "直流电压");
                case DmmMeasureMode.ACV:
                    return (":MEAS:VOLT:AC?", "V", "交流电压");
                case DmmMeasureMode.DCI:
                    return (":MEAS:CURR:DC?", "A", "直流电流");
                case DmmMeasureMode.ACI:
                    return (":MEAS:CURR:AC?", "A", "交流电流");
                case DmmMeasureMode.RES:
                    return (":MEAS:RES?", "Ω", "电阻");
                case DmmMeasureMode.CAP:
                    return (":MEAS:CAP?", "F", "电容");
                case DmmMeasureMode.CONT:
                    return (null, null, "通断");
                case DmmMeasureMode.DIODE:
                    return (":MEAS:DIODe?", "V", "二极管");
                case DmmMeasureMode.FREQ:
                    return (":MEASure:FREQuency?", "Hz", "频率");
                default:
                    return (null, null, mode.ToString());
            }
        }

        private async Task<string> QueryDmmStringAsync(string query)
        {
            if (_dmmSession == null)
                throw new InvalidOperationException("DMM会话未建立");

            await _dmmIoLock.WaitAsync().ConfigureAwait(false);
            try
            {
                var cmd = query.EndsWith("\n", StringComparison.Ordinal) ? query : query + "\n";
                _dmmSession.RawIO.Write(cmd);
                return _dmmSession.RawIO.ReadString();
            }
            finally
            {
                _dmmIoLock.Release();
            }
        }

        private async Task SendDmmCommandAsync(string command)
        {
            if (_dmmSession == null)
                throw new InvalidOperationException("DMM会话未建立");

            await _dmmIoLock.WaitAsync().ConfigureAwait(false);
            try
            {
                var cmd = command.EndsWith("\n", StringComparison.Ordinal) ? command : command + "\n";
                _dmmSession.RawIO.Write(cmd);
            }
            finally
            {
                _dmmIoLock.Release();
            }
        }

        private async Task<bool> ApplyFrequencyRangeAsync(int rangeIndex)
        {
            if (!IsDmmConnected || _dmmSession == null)
                return false;

            var pollingTask = _pollingTask;
            await StopPollingAsync().ConfigureAwait(true);
            if (pollingTask != null)
            {
                try
                {
                    await Task.WhenAny(pollingTask, Task.Delay(500)).ConfigureAwait(true);
                }
                catch
                {
                }
            }

            try
            {
                await SendDmmCommandAsync("FREQ").ConfigureAwait(true);
                await Task.Delay(80).ConfigureAwait(true);
            }
            catch
            {
                try
                {
                    await SendDmmCommandAsync("FUNC FREQ").ConfigureAwait(true);
                    await Task.Delay(80).ConfigureAwait(true);
                }
                catch
                {
                }
            }

            await SendDmmCommandAsync($":MEASure:FREQuency {rangeIndex}").ConfigureAwait(true);
            await Task.Delay(80).ConfigureAwait(true);
            return true;
        }

        private int? ShowFreqVoltageRangeDialog(int defaultIndex)
        {
            var vm = new FreqVoltageRangeDialogViewModel();
            vm.Initialize(defaultIndex);

            var dialog = new FreqVoltageRangeDialog
            {
                Owner = Application.Current?.MainWindow,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                DataContext = vm
            };

            var ok = dialog.ShowDialog();
            if (ok == true)
            {
                return dialog.SelectedIndex;
            }

            return null;
        }

        private static string FormatDmmReading(string raw, string unit)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return "--";

            if (IsOverrangeRaw(raw))
                return "超出量程";

            if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var dv))
            {
                if (IsOverrangeNumeric(dv))
                    return "超出量程";

                return FormatWithEngineeringUnit(dv, unit);
            }

            return raw;
        }

        private static bool IsOverrangeRaw(string raw)
        {
            var s = raw.Trim();

            if (s.Equals("OL", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("OVLD", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("OVER", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("OVERLOAD", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("INF", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("INFINITY", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("+INF", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("-INF", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return s.IndexOf("OVLD", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   s.IndexOf("OVER", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsOverrangeNumeric(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                return true;

            return Math.Abs(value) >= 1e36;
        }

        private static string FormatWithEngineeringUnit(double value, string baseUnit)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                return value.ToString(CultureInfo.InvariantCulture);

            var abs = Math.Abs(value);
            // Only keep these 4 prefixes:
            // -3 => m, 0 => (none), 3 => k, 6 => M
            int exp;
            if (abs == 0)
                exp = 0;
            else if (abs >= 1e6)
                exp = 6;
            else if (abs >= 1e3)
                exp = 3;
            else if (abs >= 1)
                exp = 0;
            else
                exp = -3;

            double scaled = exp == 0 ? value : value / Math.Pow(10, exp);

            string prefix = exp switch
            {
                -3 => "m",
                0 => "",
                3 => "k",
                6 => "M",
                _ => ""
            };

            // MeasurementResult fixed to 5 decimals.
            string number = scaled.ToString("0.00000", CultureInfo.InvariantCulture);

            if (string.IsNullOrWhiteSpace(baseUnit))
                return number;

            return $"{number} {prefix}{baseUnit}";
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
                        StopPollingAsync().Wait(TimeSpan.FromSeconds(1));

                        if (_dmmSession != null)
                        {
                            try
                            {
                                _dmmSession.Dispose();
                            }
                            catch { }
                            _dmmSession = null;
                        }

                        if (_resourceManager != null)
                        {
                            try
                            {
                                _resourceManager.Dispose();
                            }
                            catch { }
                            _resourceManager = null;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[DmmTestPanel] Dispose失败: {ex.Message}");
                    }
                }
                _disposed = true;
            }
        }

        #endregion

        public bool CanClose()
        {
            if (IsDmmConnecting)
            {
                ReMessageBox.Show($"正在打开万用表({CardName})，请稍候连接完成后再切换页面", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }

            return true;
        }

        public enum DmmMeasureMode
        {
            DCV,
            ACV,
            DCI,
            ACI,
            RES,
            CAP,
            CONT,
            DIODE,
            FREQ
        }
    }
}