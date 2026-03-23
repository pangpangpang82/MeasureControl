using Prism.Commands;
using Prism.Mvvm;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using MeasureControl.Simulations.PT500;

namespace MeasureControl.ViewModels.SingleBoardTest.AirController
{
    public class RS422CommunicationCheckViewModel : BindableBase, IDisposable
    {
        public enum Rs422CommTestMode
        {
            All = 0,
            TransmitOnly = 1,
            ReceiveOnly = 2
        }

        private const byte DefaultLabel = 0x6A;

        private const string FixedTxChannel = "429_CH0";
        private const string FixedRxChannel = "429_CH2";

        private static readonly byte[] AtpR = { 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] AtpEnterOk = { 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x02 };
        private static readonly byte[] AtpE = { 0x00, 0x02, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01 };
        private static readonly byte[] ExitOk = { 0x00, 0x02, 0x00, 0x01, 0x00, 0x00, 0x00, 0x03 };
        private static readonly byte[] AbRs422TransmitSpeedPos10 = { 0x06, 0x01, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] AbRs422Receive = { 0x06, 0x02, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00 };

        private PT500TemperatureSensor429Simulation _simulation = new PT500TemperatureSensor429Simulation();
        private readonly SemaphoreSlim _arincOpLock = new SemaphoreSlim(1, 1);

        private string _startedBenchTxChannel;
        private string _startedBenchRxChannel;

        private readonly SemaphoreSlim _manualTestLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _autoTestLock = new SemaphoreSlim(1, 1);
        private CancellationTokenSource _autoTestCts;

        private SerialPort _serialPort;
        private readonly object _serialLock = new object();
        private readonly System.Collections.Generic.List<byte> _rxBytesBuffer = new System.Collections.Generic.List<byte>(4096);
        private readonly StringBuilder _rxAsciiBuffer = new StringBuilder(4096);

        private TaskCompletionSource<bool> _waitHexTcs;
        private byte[] _waitHexPattern;
        private TaskCompletionSource<bool> _waitAsciiTcs;
        private string _waitAsciiPattern;

        private readonly Rs422CommTestMode _mode;

        private string _enterAtpTxChannel;
        private string _enterAtpRxChannel;
        private string _exitAtpTxChannel;
        private string _exitAtpRxChannel;
        private string _rs422TransmitTxChannel;
        private string _rs422TransmitRxChannel;
        private string _rs422ReceiveTxChannel;
        private string _rs422ReceiveRxChannel;

        private string _enterAtpRxDataText;
        private string _exitAtpRxDataText;
        private string _rs422TransmitRxDataText;
        private string _rs422TransmitResultText;
        private string _rs422ReceiveRxDataText;
        private string _rs422ReceiveResultText;

        private ObservableCollection<string> _availablePorts;
        private string _selectedPortName;
        private ObservableCollection<int> _baudRates;
        private int _selectedBaudRate;
        private ObservableCollection<Parity> _parities;
        private Parity _selectedParity;
        private ObservableCollection<int> _dataBitsList;
        private int _selectedDataBits;
        private ObservableCollection<StopBits> _stopBitsList;
        private StopBits _selectedStopBits;
        private bool _isPortOpen;
        private string _portStatusText;

        private string _lastTestTime;
        private string _lastTestResult;

        private bool _isManualTestRunning;
        private bool _isAutoTestRunning;

        public RS422CommunicationCheckViewModel(Rs422CommTestMode mode = Rs422CommTestMode.All)
        {
            _mode = mode;

            _enterAtpTxChannel = FixedTxChannel;
            _enterAtpRxChannel = FixedRxChannel;
            _exitAtpTxChannel = FixedTxChannel;
            _exitAtpRxChannel = FixedRxChannel;
            _rs422TransmitTxChannel = FixedTxChannel;
            _rs422TransmitRxChannel = FixedRxChannel;
            _rs422ReceiveTxChannel = FixedTxChannel;
            _rs422ReceiveRxChannel = FixedRxChannel;

            EnterAtpRxDataText = "--";
            ExitAtpRxDataText = "--";
            Rs422TransmitRxDataText = "--";
            Rs422TransmitResultText = "--";
            Rs422ReceiveRxDataText = "--";
            Rs422ReceiveResultText = "--";

            Logs = new ObservableCollection<string>();
            LastTestTime = "--";
            LastTestResult = "--";

            BaudRates = new ObservableCollection<int>(new[] { 9600, 19200, 38400, 57600, 115200 });
            SelectedBaudRate = 115200;
            Parities = new ObservableCollection<Parity>(new[] { Parity.None, Parity.Odd, Parity.Even });
            SelectedParity = Parity.None;
            DataBitsList = new ObservableCollection<int>(new[] { 7, 8 });
            SelectedDataBits = 8;
            StopBitsList = new ObservableCollection<StopBits>(new[] { StopBits.One, StopBits.Two });
            SelectedStopBits = StopBits.One;

            RefreshPortsCommand = new DelegateCommand(RefreshPorts);
            OpenPortCommand = new DelegateCommand(OpenPort);
            ClosePortCommand = new DelegateCommand(ClosePort);

            ManualTestCommand = new DelegateCommand(OnManualTest, () => CanClickManualTestButton)
                .ObservesProperty(() => IsAutoTestRunning);
            AutoTestCommand = new DelegateCommand(async () => await OnAutoTestAsync(), () => CanClickAutoTestButton)
                .ObservesProperty(() => IsManualTestRunning);

            SendEnterAtpCommand = new DelegateCommand(async () => await OnSendEnterAtpAsync());
            SendExitAtpCommand = new DelegateCommand(async () => await OnSendExitAtpAsync());
            Rs422TransmitTestCommand = new DelegateCommand(async () => await OnRs422TransmitTestAsync());
            Rs422ReceiveTestCommand = new DelegateCommand(async () => await OnRs422ReceiveTestAsync());
            ClearLogCommand = new DelegateCommand(() => Logs.Clear());

            RefreshPorts();
        }

        public string PageTitle
        {
            get
            {
                return _mode switch
                {
                    Rs422CommTestMode.TransmitOnly => "控制通道422发送测试",
                    Rs422CommTestMode.ReceiveOnly => "控制通道422接收测试",
                    _ => "RS422通信测试"
                };
            }
        }

        public Visibility TransmitSectionVisibility => _mode == Rs422CommTestMode.ReceiveOnly ? Visibility.Collapsed : Visibility.Visible;

        public Visibility ReceiveSectionVisibility => _mode == Rs422CommTestMode.TransmitOnly ? Visibility.Collapsed : Visibility.Visible;

        public string TransmitStepTitleText => _mode == Rs422CommTestMode.All ? "2.RS422发送测试：" : "2.422发送测试：";

        public string ReceiveStepTitleText => _mode == Rs422CommTestMode.All ? "3.RS422接收测试：" : "2.422接收测试：";

        public string ExitAtpStepTitleText => _mode == Rs422CommTestMode.All ? "4.退出ATP模式：" : "3.退出ATP模式：";

        public ObservableCollection<string> Logs { get; }

        public string EnterAtpTxChannel
        {
            get => _enterAtpTxChannel;
            set => SetProperty(ref _enterAtpTxChannel, FixedTxChannel);
        }

        public string EnterAtpRxChannel
        {
            get => _enterAtpRxChannel;
            set => SetProperty(ref _enterAtpRxChannel, FixedRxChannel);
        }

        public string ExitAtpTxChannel
        {
            get => _exitAtpTxChannel;
            set => SetProperty(ref _exitAtpTxChannel, FixedTxChannel);
        }

        public string ExitAtpRxChannel
        {
            get => _exitAtpRxChannel;
            set => SetProperty(ref _exitAtpRxChannel, FixedRxChannel);
        }

        public string Rs422TransmitTxChannel
        {
            get => _rs422TransmitTxChannel;
            set => SetProperty(ref _rs422TransmitTxChannel, FixedTxChannel);
        }

        public string Rs422TransmitRxChannel
        {
            get => _rs422TransmitRxChannel;
            set => SetProperty(ref _rs422TransmitRxChannel, FixedRxChannel);
        }

        public string Rs422ReceiveTxChannel
        {
            get => _rs422ReceiveTxChannel;
            set => SetProperty(ref _rs422ReceiveTxChannel, FixedTxChannel);
        }

        public string Rs422ReceiveRxChannel
        {
            get => _rs422ReceiveRxChannel;
            set => SetProperty(ref _rs422ReceiveRxChannel, FixedRxChannel);
        }

        public string EnterAtpRxDataText
        {
            get => _enterAtpRxDataText;
            set => SetProperty(ref _enterAtpRxDataText, value);
        }

        public string ExitAtpRxDataText
        {
            get => _exitAtpRxDataText;
            set => SetProperty(ref _exitAtpRxDataText, value);
        }

        public string Rs422TransmitRxDataText
        {
            get => _rs422TransmitRxDataText;
            set => SetProperty(ref _rs422TransmitRxDataText, value);
        }

        public string Rs422TransmitResultText
        {
            get => _rs422TransmitResultText;
            set => SetProperty(ref _rs422TransmitResultText, value);
        }

        public string Rs422ReceiveRxDataText
        {
            get => _rs422ReceiveRxDataText;
            set => SetProperty(ref _rs422ReceiveRxDataText, value);
        }

        public string Rs422ReceiveResultText
        {
            get => _rs422ReceiveResultText;
            set => SetProperty(ref _rs422ReceiveResultText, value);
        }

        private static bool TryExtractPatternBytes(System.Collections.Generic.List<byte> buffer, byte[] pattern, out byte[] found)
        {
            found = null;
            if (buffer == null || pattern == null || pattern.Length == 0 || buffer.Count < pattern.Length)
                return false;

            int max = buffer.Count - pattern.Length;
            for (int i = max; i >= 0; i--)
            {
                bool ok = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (buffer[i + j] != pattern[j])
                    {
                        ok = false;
                        break;
                    }
                }
                if (!ok)
                    continue;

                found = new byte[pattern.Length];
                for (int j = 0; j < pattern.Length; j++)
                    found[j] = buffer[i + j];
                return true;
            }

            return false;
        }

        public ObservableCollection<string> AvailablePorts
        {
            get => _availablePorts;
            private set => SetProperty(ref _availablePorts, value);
        }

        public string SelectedPortName
        {
            get => _selectedPortName;
            set => SetProperty(ref _selectedPortName, value);
        }

        public ObservableCollection<int> BaudRates
        {
            get => _baudRates;
            private set => SetProperty(ref _baudRates, value);
        }

        public int SelectedBaudRate
        {
            get => _selectedBaudRate;
            set => SetProperty(ref _selectedBaudRate, value);
        }

        public ObservableCollection<Parity> Parities
        {
            get => _parities;
            private set => SetProperty(ref _parities, value);
        }

        public Parity SelectedParity
        {
            get => _selectedParity;
            set => SetProperty(ref _selectedParity, value);
        }

        public ObservableCollection<int> DataBitsList
        {
            get => _dataBitsList;
            private set => SetProperty(ref _dataBitsList, value);
        }

        public int SelectedDataBits
        {
            get => _selectedDataBits;
            set => SetProperty(ref _selectedDataBits, value);
        }

        public ObservableCollection<StopBits> StopBitsList
        {
            get => _stopBitsList;
            private set => SetProperty(ref _stopBitsList, value);
        }

        public StopBits SelectedStopBits
        {
            get => _selectedStopBits;
            set => SetProperty(ref _selectedStopBits, value);
        }

        public bool IsPortOpen
        {
            get => _isPortOpen;
            private set => SetProperty(ref _isPortOpen, value);
        }

        public string PortStatusText
        {
            get => _portStatusText;
            private set => SetProperty(ref _portStatusText, value);
        }

        public string LastTestTime
        {
            get => _lastTestTime;
            private set => SetProperty(ref _lastTestTime, value);
        }

        public string LastTestResult
        {
            get => _lastTestResult;
            private set => SetProperty(ref _lastTestResult, value);
        }

        public DelegateCommand RefreshPortsCommand { get; }
        public DelegateCommand OpenPortCommand { get; }
        public DelegateCommand ClosePortCommand { get; }

        public DelegateCommand ManualTestCommand { get; }
        public DelegateCommand AutoTestCommand { get; }

        public DelegateCommand SendEnterAtpCommand { get; }
        public DelegateCommand SendExitAtpCommand { get; }
        public DelegateCommand Rs422TransmitTestCommand { get; }
        public DelegateCommand Rs422ReceiveTestCommand { get; }
        public DelegateCommand ClearLogCommand { get; }

        public bool IsManualTestRunning
        {
            get => _isManualTestRunning;
            set
            {
                if (SetProperty(ref _isManualTestRunning, value) && value)
                {
                    IsAutoTestRunning = false;
                }
                RaisePropertyChanged(nameof(CanClickManualTestButton));
                RaisePropertyChanged(nameof(CanClickAutoTestButton));
            }
        }

        public bool IsAutoTestRunning
        {
            get => _isAutoTestRunning;
            set
            {
                if (SetProperty(ref _isAutoTestRunning, value) && value)
                {
                    IsManualTestRunning = false;
                }
                RaisePropertyChanged(nameof(CanClickManualTestButton));
                RaisePropertyChanged(nameof(CanClickAutoTestButton));
            }
        }

        public bool CanClickManualTestButton => !IsAutoTestRunning;

        public bool CanClickAutoTestButton => !IsManualTestRunning;

        private void OnManualTest()
        {
            _ = ToggleManualTestAsync();
        }

        private async Task ToggleManualTestAsync()
        {
            await _manualTestLock.WaitAsync();
            try
            {
                if (IsManualTestRunning)
                {
                    IsManualTestRunning = false;
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试停止");
                    return;
                }

                IsManualTestRunning = true;

                try
                {
                    var api = Prism.Ioc.ContainerLocator.Container.Resolve(typeof(MeasureControl.Services.HardwareApis.IComponentPowerStateApi)) as MeasureControl.Services.HardwareApis.IComponentPowerStateApi;
                    if (api != null)
                        await api.ApplyComponent28VStateAsync(CancellationToken.None);
                }
                catch { }

                AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试模式");
            }
            finally
            {
                _manualTestLock.Release();
            }
        }

        private async Task OnAutoTestAsync()
        {
            if (IsAutoTestRunning)
            {
                try { _autoTestCts?.Cancel(); } catch { }
                AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试停止请求");
                return;
            }

            await _autoTestLock.WaitAsync();
            try
            {
                if (IsAutoTestRunning)
                    return;

                IsAutoTestRunning = true;
                _autoTestCts = new CancellationTokenSource();
                var token = _autoTestCts.Token;

                try
                {
                    var api = Prism.Ioc.ContainerLocator.Container.Resolve(typeof(MeasureControl.Services.HardwareApis.IComponentPowerStateApi)) as MeasureControl.Services.HardwareApis.IComponentPowerStateApi;
                    if (api != null)
                        await api.ApplyComponent28VStateAsync(token);
                }
                catch { }

                AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试开始");

                if (!IsPortOpen)
                {
                    PortStatusText = "请先打开串口";
                    LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    LastTestResult = _mode == Rs422CommTestMode.All ? "RS422自动测试不通过" : $"{PageTitle}自动测试不通过";
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 串口未打开，自动测试结束");
                    return;
                }

                bool enteredAtp = await OnSendEnterAtpAsync();
                if (token.IsCancellationRequested) return;

                try
                {
                    bool pass;
                    switch (_mode)
                    {
                        case Rs422CommTestMode.TransmitOnly:
                            await OnRs422TransmitTestAsync();
                            if (token.IsCancellationRequested) return;
                            pass = enteredAtp && string.Equals(Rs422TransmitResultText, "PASS", StringComparison.OrdinalIgnoreCase);
                            break;
                        case Rs422CommTestMode.ReceiveOnly:
                            await OnRs422ReceiveTestAsync();
                            if (token.IsCancellationRequested) return;
                            pass = enteredAtp && string.Equals(Rs422ReceiveResultText, "PASS", StringComparison.OrdinalIgnoreCase);
                            break;
                        default:
                            await OnRs422TransmitTestAsync();
                            if (token.IsCancellationRequested) return;

                            await OnRs422ReceiveTestAsync();
                            if (token.IsCancellationRequested) return;

                            pass = enteredAtp
                                   && string.Equals(Rs422TransmitResultText, "PASS", StringComparison.OrdinalIgnoreCase)
                                   && string.Equals(Rs422ReceiveResultText, "PASS", StringComparison.OrdinalIgnoreCase);
                            break;
                    }

                    LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    LastTestResult = pass
                        ? (_mode == Rs422CommTestMode.All ? "RS422自动测试PASS" : $"{PageTitle}自动测试PASS")
                        : (_mode == Rs422CommTestMode.All ? "RS422自动测试不通过" : $"{PageTitle}自动测试不通过");
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试结束：{(pass ? "PASS" : "FAIL")}");
                }
                finally
                {
                    if (enteredAtp)
                    {
                        try { await OnSendExitAtpAsync(); } catch { }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试已取消");
            }
            catch (Exception ex)
            {
                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                LastTestResult = _mode == Rs422CommTestMode.All ? "RS422自动测试不通过" : $"{PageTitle}自动测试不通过";
                AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试异常：{ex.Message}");
            }
            finally
            {
                IsAutoTestRunning = false;
                try { _autoTestCts?.Dispose(); } catch { }
                _autoTestCts = null;
                _autoTestLock.Release();
            }
        }

        private void AddLog(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            try
            {
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher != null && !dispatcher.CheckAccess())
                {
                    dispatcher.BeginInvoke(new Action(() => AddLog(message)));
                    return;
                }
            }
            catch
            {
            }

            try
            {
                Logs.Add(message);
            }
            catch
            {
            }

            try
            {
                Debug.WriteLine(message);
            }
            catch
            {
            }
        }

        private void RefreshPorts()
        {
            try
            {
                var ports = SerialPort.GetPortNames()?.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList() ?? new System.Collections.Generic.List<string>();
                AvailablePorts = new ObservableCollection<string>(ports);
                if (!string.IsNullOrWhiteSpace(SelectedPortName) && ports.Contains(SelectedPortName, StringComparer.OrdinalIgnoreCase))
                    return;
                SelectedPortName = ports.FirstOrDefault();
                PortStatusText = ports.Count == 0 ? "未发现串口" : "串口列表已刷新";
            }
            catch (Exception ex)
            {
                AvailablePorts = new ObservableCollection<string>();
                SelectedPortName = null;
                PortStatusText = "刷新串口失败：" + ex.Message;
            }
        }

        private void OpenPort()
        {
            try
            {
                if (IsPortOpen)
                {
                    PortStatusText = "串口已打开";
                    return;
                }

                if (string.IsNullOrWhiteSpace(SelectedPortName))
                {
                    PortStatusText = "请选择COM口";
                    return;
                }

                var port = new SerialPort(SelectedPortName, SelectedBaudRate, SelectedParity, SelectedDataBits, SelectedStopBits)
                {
                    Encoding = Encoding.ASCII,
                    ReadTimeout = 500,
                    WriteTimeout = 500
                };

                port.DataReceived += SerialPort_DataReceived;
                port.Open();

                lock (_serialLock)
                {
                    _serialPort = port;
                    _rxBytesBuffer.Clear();
                    _rxAsciiBuffer.Clear();
                }

                IsPortOpen = true;
                PortStatusText = $"已打开：{SelectedPortName}";
            }
            catch (Exception ex)
            {
                IsPortOpen = false;
                PortStatusText = "打开失败：" + ex.Message;
                SafeCloseSerialPort();
            }
        }

        private void ClosePort()
        {
            SafeCloseSerialPort();
            IsPortOpen = false;
            PortStatusText = "已关闭";
        }

        private void SafeCloseSerialPort()
        {
            SerialPort port;
            lock (_serialLock)
            {
                port = _serialPort;
                _serialPort = null;
            }

            if (port == null)
                return;

            try
            {
                port.DataReceived -= SerialPort_DataReceived;
            }
            catch
            {
            }

            try
            {
                if (port.IsOpen)
                    port.Close();
            }
            catch
            {
            }

            try
            {
                port.Dispose();
            }
            catch
            {
            }
        }

        private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            SerialPort port;
            lock (_serialLock)
            {
                port = _serialPort;
            }

            if (port == null)
                return;

            try
            {
                int toRead = port.BytesToRead;
                if (toRead <= 0)
                    return;

                var buf = new byte[toRead];
                int read = port.Read(buf, 0, buf.Length);
                if (read <= 0)
                    return;

                lock (_serialLock)
                {
                    for (int i = 0; i < read; i++)
                        _rxBytesBuffer.Add(buf[i]);
                    if (_rxBytesBuffer.Count > 4096)
                        _rxBytesBuffer.RemoveRange(0, _rxBytesBuffer.Count - 4096);

                    _rxAsciiBuffer.Append(Encoding.ASCII.GetString(buf, 0, read));
                    if (_rxAsciiBuffer.Length > 4096)
                        _rxAsciiBuffer.Remove(0, _rxAsciiBuffer.Length - 4096);

                    if (_waitHexTcs != null && _waitHexPattern != null)
                    {
                        if (ContainsPattern(_rxBytesBuffer, _waitHexPattern))
                        {
                            _waitHexTcs.TrySetResult(true);
                            _waitHexTcs = null;
                            _waitHexPattern = null;
                        }
                    }

                    if (_waitAsciiTcs != null && !string.IsNullOrEmpty(_waitAsciiPattern))
                    {
                        if (_rxAsciiBuffer.ToString().Contains(_waitAsciiPattern))
                        {
                            _waitAsciiTcs.TrySetResult(true);
                            _waitAsciiTcs = null;
                            _waitAsciiPattern = null;
                        }
                    }
                }
            }
            catch
            {
            }
        }

        private static bool ContainsPattern(System.Collections.Generic.List<byte> buffer, byte[] pattern)
        {
            if (buffer == null || pattern == null || buffer.Count < pattern.Length || pattern.Length == 0)
                return false;

            int max = buffer.Count - pattern.Length;
            for (int i = 0; i <= max; i++)
            {
                bool ok = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (buffer[i + j] != pattern[j])
                    {
                        ok = false;
                        break;
                    }
                }
                if (ok)
                    return true;
            }
            return false;
        }

        private async Task EnsureArincStartedAsync(string txChannel, string rxChannel)
        {
            if (_simulation == null)
            {
                _simulation = new PT500TemperatureSensor429Simulation();
                _startedBenchTxChannel = null;
                _startedBenchRxChannel = null;
            }

            if (!string.Equals(_startedBenchTxChannel, txChannel, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(_startedBenchRxChannel, rxChannel, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    _simulation.Dispose();
                }
                catch
                {
                }

                _simulation = new PT500TemperatureSensor429Simulation();
                _startedBenchTxChannel = txChannel;
                _startedBenchRxChannel = rxChannel;
            }

            _simulation.EnableFrameLogging = true;
            await _simulation.StartAsync(txChannel, rxChannel, msg => AddLog(msg));
        }

        private async Task<bool> OnSendEnterAtpAsync()
        {
            await _arincOpLock.WaitAsync();
            try
            {
                EnterAtpRxDataText = "--";
                AddLog($"[{DateTime.Now:HH:mm:ss}] 发送：进入ATP，TX={EnterAtpTxChannel}, RX={EnterAtpRxChannel}, Label=0x{DefaultLabel:X2}");

                await EnsureArincStartedAsync(EnterAtpTxChannel, EnterAtpRxChannel);

                try { await _simulation.ClearRxFifoAsync(EnterAtpRxChannel); } catch { }
                await Task.Delay(50);

                var resp = await _simulation.SendBenchCommandAndWaitAsync(
                    EnterAtpTxChannel, EnterAtpRxChannel,
                    DefaultLabel, AtpR,
                    b => b.SequenceEqual(AtpEnterOk),
                    timeoutMs: 3000,
                    msg => AddLog(msg), CancellationToken.None);

                if (resp == null)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 进入ATP超时，未收到OK");
                    return false;
                }

                EnterAtpRxDataText = "0x" + FormatData(resp);
                AddLog($"[{DateTime.Now:HH:mm:ss}] 收到ATP OK");
                return true;
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 进入ATP异常：{ex.Message}");
                return false;
            }
            finally
            {
                _arincOpLock.Release();
            }
        }

        private async Task OnSendExitAtpAsync()
        {
            await _arincOpLock.WaitAsync();
            try
            {
                ExitAtpRxDataText = "--";
                AddLog($"[{DateTime.Now:HH:mm:ss}] 发送：退出ATP，TX={ExitAtpTxChannel}, RX={ExitAtpRxChannel}, Label=0x{DefaultLabel:X2}");

                await EnsureArincStartedAsync(ExitAtpTxChannel, ExitAtpRxChannel);

                try { await _simulation.ClearRxFifoAsync(ExitAtpRxChannel); } catch { }
                await Task.Delay(50);

                var resp = await _simulation.SendBenchCommandAndWaitAsync(
                    ExitAtpTxChannel, ExitAtpRxChannel,
                    DefaultLabel, AtpE,
                    b => b.SequenceEqual(ExitOk),
                    timeoutMs: 2000,
                    msg => AddLog(msg), CancellationToken.None);

                if (resp == null)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 退出ATP超时，未收到OK");
                    return;
                }

                ExitAtpRxDataText = "0x" + FormatData(resp);
                AddLog($"[{DateTime.Now:HH:mm:ss}] 收到退出ATP OK");
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 退出ATP异常：{ex.Message}");
            }
            finally
            {
                _arincOpLock.Release();
            }
        }

        private async Task OnRs422TransmitTestAsync()
        {
            if (!IsPortOpen)
            {
                PortStatusText = "请先打开串口";
                return;
            }

            await _arincOpLock.WaitAsync();
            try
            {
                Rs422TransmitRxDataText = "--";
                Rs422TransmitResultText = "--";
                AddLog($"[{DateTime.Now:HH:mm:ss}] RS422发送测试：TX={Rs422TransmitTxChannel}, RX={Rs422TransmitRxChannel}, AB=06 01 01 01 00 00 00 00");

                lock (_serialLock)
                {
                    _rxBytesBuffer.Clear();
                    _rxAsciiBuffer.Clear();
                    _waitHexTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    _waitHexPattern = new byte[] { 0x10, 0x40, 0x82 };
                }

                await EnsureArincStartedAsync(Rs422TransmitTxChannel, Rs422TransmitRxChannel);
                try { await _simulation.ClearRxFifoAsync(Rs422TransmitRxChannel); } catch { }
                await Task.Delay(20);

                await _simulation.SendBenchCommandOnlyAsync(
                    Rs422TransmitTxChannel,
                    DefaultLabel,
                    AbRs422TransmitSpeedPos10,
                    msg => AddLog(msg),
                    CancellationToken.None);

                bool ok = false;
                TaskCompletionSource<bool> tcs;
                lock (_serialLock) { tcs = _waitHexTcs; }
                if (tcs != null)
                {
                    var done = await Task.WhenAny(tcs.Task, Task.Delay(3000));
                    ok = ReferenceEquals(done, tcs.Task) && tcs.Task.Result;
                }

                Rs422TransmitResultText = ok ? "PASS" : "FAIL";

                if (ok)
                {
                    byte[] found;
                    lock (_serialLock)
                    {
                        TryExtractPatternBytes(_rxBytesBuffer, new byte[] { 0x10, 0x40, 0x82 }, out found);
                    }
                    Rs422TransmitRxDataText = found != null ? ("0x" + FormatData(found, found.Length)) : "0x10 40 82";
                }

                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                if (_mode == Rs422CommTestMode.All)
                    LastTestResult = ok ? "RS422发送测试PASS" : "RS422发送测试不通过";
                else
                    LastTestResult = ok ? $"{PageTitle}PASS" : $"{PageTitle}不通过";

                AddLog($"[{DateTime.Now:HH:mm:ss}] RS422发送测试{(ok ? "PASS" : "FAIL")}");
            }
            catch (Exception ex)
            {
                Rs422TransmitRxDataText = "--";
                Rs422TransmitResultText = "FAIL";
                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                LastTestResult = _mode == Rs422CommTestMode.All ? "RS422发送测试不通过" : $"{PageTitle}不通过";
                AddLog($"[{DateTime.Now:HH:mm:ss}] RS422发送测试异常：{ex.Message}");
            }
            finally
            {
                lock (_serialLock)
                {
                    _waitHexTcs = null;
                    _waitHexPattern = null;
                }
                _arincOpLock.Release();
            }
        }

        private async Task OnRs422ReceiveTestAsync()
        {
            if (!IsPortOpen)
            {
                PortStatusText = "请先打开串口";
                return;
            }

            await _arincOpLock.WaitAsync();
            try
            {
                Rs422ReceiveRxDataText = "--";
                Rs422ReceiveResultText = "--";
                AddLog($"[{DateTime.Now:HH:mm:ss}] RS422接收测试：TX={Rs422ReceiveTxChannel}, RX={Rs422ReceiveRxChannel}, AB=06 02 01 01 00 00 00 00");

                lock (_serialLock)
                {
                    _rxBytesBuffer.Clear();
                    _rxAsciiBuffer.Clear();
                    _waitAsciiTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    _waitAsciiPattern = "AAAAAAAA";
                }

                SerialPort port;
                lock (_serialLock) { port = _serialPort; }
                if (port == null || !port.IsOpen)
                {
                    PortStatusText = "串口未打开";
                    return;
                }

                try
                {
                    port.Write("AAAAAAAA");
                }
                catch (Exception ex)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 串口发送失败：{ex.Message}");
                    Rs422ReceiveResultText = "FAIL";
                    LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    LastTestResult = "RS422接收测试不通过";
                    return;
                }

                await EnsureArincStartedAsync(Rs422ReceiveTxChannel, Rs422ReceiveRxChannel);
                try { await _simulation.ClearRxFifoAsync(Rs422ReceiveRxChannel); } catch { }
                await Task.Delay(20);

                await _simulation.SendBenchCommandOnlyAsync(
                    Rs422ReceiveTxChannel,
                    DefaultLabel,
                    AbRs422Receive,
                    msg => AddLog(msg),
                    CancellationToken.None);

                bool ok = false;
                TaskCompletionSource<bool> tcs;
                lock (_serialLock) { tcs = _waitAsciiTcs; }
                if (tcs != null)
                {
                    var done = await Task.WhenAny(tcs.Task, Task.Delay(3000));
                    ok = ReferenceEquals(done, tcs.Task) && tcs.Task.Result;
                }

                Rs422ReceiveResultText = ok ? "PASS" : "FAIL";

                if (ok)
                {
                    string found;
                    lock (_serialLock)
                    {
                        var s = _rxAsciiBuffer.ToString();
                        found = s.Contains("AAAAAAAA") ? "AAAAAAAA" : s;
                    }
                    Rs422ReceiveRxDataText = string.IsNullOrWhiteSpace(found) ? "AAAAAAAA" : found;
                }

                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                if (_mode == Rs422CommTestMode.All)
                    LastTestResult = ok ? "RS422接收测试PASS" : "RS422接收测试不通过";
                else
                    LastTestResult = ok ? $"{PageTitle}PASS" : $"{PageTitle}不通过";
                AddLog($"[{DateTime.Now:HH:mm:ss}] RS422接收测试{(ok ? "PASS" : "FAIL")}");
            }
            catch (Exception ex)
            {
                Rs422ReceiveRxDataText = "--";
                Rs422ReceiveResultText = "FAIL";
                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                LastTestResult = _mode == Rs422CommTestMode.All ? "RS422接收测试不通过" : $"{PageTitle}不通过";
                AddLog($"[{DateTime.Now:HH:mm:ss}] RS422接收测试异常：{ex.Message}");
            }
            finally
            {
                lock (_serialLock)
                {
                    _waitAsciiTcs = null;
                    _waitAsciiPattern = null;
                }
                _arincOpLock.Release();
            }
        }

        private static string FormatData(byte[] bytes, int length = 8)
        {
            if (bytes == null || bytes.Length == 0)
                return string.Empty;
            int len = Math.Min(bytes.Length, length);
            return string.Join(" ", bytes.Take(len).Select(b => b.ToString("X2")));
        }

        public void Dispose()
        {
            try { _autoTestCts?.Cancel(); } catch { }
            try { SafeCloseSerialPort(); } catch { }
            try { _simulation?.Dispose(); } catch { }
            try { _arincOpLock?.Dispose(); } catch { }
            try { _manualTestLock?.Dispose(); } catch { }
            try { _autoTestLock?.Dispose(); } catch { }
            try { _autoTestCts?.Dispose(); } catch { }
        }
    }
}
