using Prism.Commands;
using Prism.Mvvm;
using System;
using System.Collections.ObjectModel;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace MeasureControl.ViewModels.TestTask
{
    public class RsSerialDebugPanelViewModel : BindableBase
    {
        private static readonly object _lastPortLock = new object();
        private static readonly System.Collections.Generic.Dictionary<string, string> _lastPortByRsType = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private readonly string _rsType;
        private SerialPort _serialPort;

        private string _title;
        private bool _isOpen;
        private string _selectedPort;
        private int _selectedBaudRate;
        private string _baudRateText;
        private string _selectedParity;
        private int _selectedDataBits;
        private string _selectedStopBits;
        private bool _isHexSend;
        private bool _isHexDisplay;
        private string _sendText;
        private string _receiveText;
        private string _statusText;

        private DispatcherTimer _timedSendTimer;
        private bool _isTimedSendEnabled;
        private int _timedSendIntervalMs;
        private string _timedSendStatusText;
        private bool _isTimedSendTickRunning;

        private bool _isSending;
        private bool _isReceiving;

        private bool _isBusy;

        public ObservableCollection<string> Ports { get; } = new ObservableCollection<string>();
        public ObservableCollection<int> BaudRates { get; } = new ObservableCollection<int>(new[] { 9600, 19200, 38400, 57600, 115200 });
        public ObservableCollection<string> ParityOptions { get; } = new ObservableCollection<string>(new[] { "None", "Odd", "Even" });
        public ObservableCollection<int> DataBitsOptions { get; } = new ObservableCollection<int>(new[] { 7, 8 });
        public ObservableCollection<string> StopBitsOptions { get; } = new ObservableCollection<string>(new[] { "One", "Two" });

        public string Title
        {
            get => _title;
            set => SetProperty(ref _title, value);
        }

        public string RsType => _rsType;

        public bool IsOpen
        {
            get => _isOpen;
            private set
            {
                if (SetProperty(ref _isOpen, value))
                {
                    RaisePropertyChanged(nameof(IsNotOpen));
                    RaisePropertyChanged(nameof(OpenCloseText));
                }
            }
        }

        public bool IsNotOpen => !IsOpen;

        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (SetProperty(ref _isBusy, value))
                {
                    RaisePropertyChanged(nameof(IsNotBusy));
                }
            }
        }

        public bool IsNotBusy => !IsBusy;

        public string OpenCloseText => IsOpen ? "关闭串口" : "打开串口";

        public string SelectedPort
        {
            get => _selectedPort;
            set => SetProperty(ref _selectedPort, value);
        }

        public int SelectedBaudRate
        {
            get => _selectedBaudRate;
            set
            {
                if (SetProperty(ref _selectedBaudRate, value))
                {
                    var text = value > 0 ? value.ToString() : string.Empty;
                    if (!string.Equals(_baudRateText, text, StringComparison.Ordinal))
                        SetProperty(ref _baudRateText, text, nameof(BaudRateText));
                }
            }
        }

        public string BaudRateText
        {
            get => _baudRateText;
            set
            {
                if (SetProperty(ref _baudRateText, value))
                {
                    if (int.TryParse((value ?? string.Empty).Trim(), out var br) && br > 0)
                    {
                        if (_selectedBaudRate != br)
                            SetProperty(ref _selectedBaudRate, br, nameof(SelectedBaudRate));
                    }
                }
            }
        }

        public string SelectedParity
        {
            get => _selectedParity;
            set => SetProperty(ref _selectedParity, value);
        }

        public int SelectedDataBits
        {
            get => _selectedDataBits;
            set => SetProperty(ref _selectedDataBits, value);
        }

        public string SelectedStopBits
        {
            get => _selectedStopBits;
            set => SetProperty(ref _selectedStopBits, value);
        }

        public bool IsHexSend
        {
            get => _isHexSend;
            set => SetProperty(ref _isHexSend, value);
        }

        public bool IsHexDisplay
        {
            get => _isHexDisplay;
            set => SetProperty(ref _isHexDisplay, value);
        }

        public string SendText
        {
            get => _sendText;
            set => SetProperty(ref _sendText, value);
        }

        public string ReceiveText
        {
            get => _receiveText;
            private set => SetProperty(ref _receiveText, value);
        }

        public string StatusText
        {
            get => _statusText;
            private set => SetProperty(ref _statusText, value);
        }

        public bool IsTimedSendEnabled
        {
            get => _isTimedSendEnabled;
            private set
            {
                if (SetProperty(ref _isTimedSendEnabled, value))
                {
                    RaisePropertyChanged(nameof(TimedSendButtonText));
                }
            }
        }

        public int TimedSendIntervalMs
        {
            get => _timedSendIntervalMs;
            set
            {
                if (SetProperty(ref _timedSendIntervalMs, value))
                {
                    UpdateTimedSendInterval();
                }
            }
        }

        public string TimedSendButtonText => IsTimedSendEnabled ? "停止发送" : "定时发送";

        public string TimedSendStatusText
        {
            get => _timedSendStatusText;
            private set => SetProperty(ref _timedSendStatusText, value);
        }

        public bool IsSending
        {
            get => _isSending;
            private set
            {
                if (SetProperty(ref _isSending, value))
                {
                    RaisePropertyChanged(nameof(SendStatusText));
                }
            }
        }

        public bool IsReceiving
        {
            get => _isReceiving;
            private set
            {
                if (SetProperty(ref _isReceiving, value))
                {
                    RaisePropertyChanged(nameof(ReceiveStatusText));
                }
            }
        }

        public string SendStatusText
        {
            get
            {
                if (!IsOpen) return "--";
                return IsSending ? "发送中" : "就绪";
            }
        }

        public string ReceiveStatusText
        {
            get
            {
                if (!IsOpen) return "--";
                return IsReceiving ? "接收中" : "就绪";
            }
        }

        public DelegateCommand RefreshPortsCommand { get; }
        public DelegateCommand ToggleOpenCommand { get; }
        public DelegateCommand SendCommand { get; }
        public DelegateCommand ClearReceiveCommand { get; }
        public DelegateCommand ToggleTimedSendCommand { get; }

        public RsSerialDebugPanelViewModel(string rsType, string deviceDisplayName)
        {
            _rsType = string.IsNullOrWhiteSpace(rsType) ? "RS" : rsType;
            Title = string.IsNullOrWhiteSpace(deviceDisplayName) ? $"{_rsType} 串口调试" : deviceDisplayName;

            SelectedBaudRate = BaudRates.FirstOrDefault();
            BaudRateText = SelectedBaudRate.ToString();
            SelectedParity = ParityOptions.FirstOrDefault();
            SelectedDataBits = DataBitsOptions.LastOrDefault();
            SelectedStopBits = StopBitsOptions.FirstOrDefault();

            RefreshPortsCommand = new DelegateCommand(RefreshPorts);
            ToggleOpenCommand = new DelegateCommand(ToggleOpen);
            SendCommand = new DelegateCommand(Send);
            ClearReceiveCommand = new DelegateCommand(() => ReceiveText = string.Empty);
            ToggleTimedSendCommand = new DelegateCommand(ToggleTimedSend);

            TimedSendIntervalMs = 1000;
            TimedSendStatusText = string.Empty;

            RefreshPorts();
        }

        private void AppendReceiveLine(string line)
        {
            if (line == null) line = string.Empty;
            if (!line.EndsWith(Environment.NewLine, StringComparison.Ordinal))
            {
                line += Environment.NewLine;
            }

            ReceiveText = (ReceiveText ?? string.Empty) + line;
        }
        private void AppendReceiveChunk(string chunk)
        {
            if (chunk == null) chunk = string.Empty;

            var newLinePerChunk = string.Equals(_rsType, "RS422", StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(_rsType, "RS232", StringComparison.OrdinalIgnoreCase);

            var current = ReceiveText ?? string.Empty;
            if (newLinePerChunk && current.Length > 0 && !current.EndsWith(Environment.NewLine, StringComparison.Ordinal))
            {
                current += Environment.NewLine;
            }

            if (IsHexDisplay && current.Length > 0)
            {
                var last = current[current.Length - 1];
                if (!char.IsWhiteSpace(last))
                {
                    current += " ";
                }
            }

            var next = current + chunk;
            if (newLinePerChunk && !next.EndsWith(Environment.NewLine, StringComparison.Ordinal))
            {
                next += Environment.NewLine;
            }

            ReceiveText = next;
        }


        private void RefreshPorts()
        {
            try
            {
                var ports = SerialPort.GetPortNames().OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
                Ports.Clear();
                foreach (var p in ports)
                {
                    Ports.Add(p);
                }

                if (Ports.Count == 0)
                {
                    SelectedPort = null;
                    return;
                }

                if (!string.IsNullOrWhiteSpace(SelectedPort) && Ports.Contains(SelectedPort))
                {
                    return;
                }

                string lastPort = null;
                lock (_lastPortLock)
                {
                    _lastPortByRsType.TryGetValue(_rsType, out lastPort);
                }

                if (!string.IsNullOrWhiteSpace(lastPort) && Ports.Contains(lastPort))
                {
                    SelectedPort = lastPort;
                    return;
                }

                if (Ports.Count == 1)
                {
                    SelectedPort = Ports[0];
                    return;
                }

                SelectedPort = Ports.FirstOrDefault();
            }
            catch (Exception ex)
            {
                StatusText = ex.Message;
            }
        }

        private void ToggleOpen()
        {
            if (IsBusy)
            {
                return;
            }

            if (IsOpen)
            {
                _ = CloseSerialAsync();
            }
            else
            {
                _ = OpenSerialAsync();
            }
        }

        private Task OpenSerialAsync()
        {
            if (string.IsNullOrWhiteSpace(SelectedPort))
            {
                StatusText = "未选择端口";
                return Task.CompletedTask;
            }

            if (SelectedBaudRate <= 0)
            {
                StatusText = "波特率无效";
                return Task.CompletedTask;
            }

            IsBusy = true;
            StatusText = "正在打开...";

            return Task.Run(() =>
            {
                Exception openEx = null;
                SerialPort sp = null;

                try
                {
                    var parity = ParseParity(SelectedParity);
                    var stopBits = ParseStopBits(SelectedStopBits);

                    sp = new SerialPort(SelectedPort, SelectedBaudRate, parity, SelectedDataBits, stopBits)
                    {
                        Handshake = Handshake.None,
                        Encoding = Encoding.ASCII,
                        ReadTimeout = 500,
                        WriteTimeout = 500
                    };

                    var t = new Thread(() =>
                    {
                        try
                        {
                            sp.Open();
                        }
                        catch (Exception ex)
                        {
                            openEx = ex;
                        }
                    })
                    {
                        IsBackground = true,
                        Name = "SerialPortOpen"
                    };

                    t.Start();

                    if (!t.Join(1500))
                    {
                        openEx = new TimeoutException("打开串口超时（端口可能被占用或驱动无响应）");
                    }

                    if (openEx != null)
                    {
                        try { if (sp.IsOpen) sp.Close(); } catch { }
                        try { sp.Dispose(); } catch { }
                        sp = null;
                    }
                }
                catch (Exception ex)
                {
                    openEx = ex;
                    try { if (sp != null) sp.Dispose(); } catch { }
                    sp = null;
                }

                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        if (openEx != null)
                        {
                            StatusText = openEx.Message;
                            IsOpen = false;
                        }
                        else
                        {
                            _serialPort = sp;
                            _serialPort.DataReceived += SerialPort_DataReceived;
                            IsOpen = true;
                            RaisePropertyChanged(nameof(SendStatusText));
                            RaisePropertyChanged(nameof(ReceiveStatusText));
                            StatusText = $"已打开 {SelectedPort}";

                            lock (_lastPortLock)
                            {
                                _lastPortByRsType[_rsType] = SelectedPort;
                            }
                        }
                    }
                    finally
                    {
                        IsBusy = false;
                    }
                }));
            });
        }

        private Task CloseSerialAsync()
        {
            IsBusy = true;
            StatusText = "正在关闭...";

            return Task.Run(() =>
            {
                try
                {
                    var sp = _serialPort;
                    if (sp != null)
                    {
                        try { sp.DataReceived -= SerialPort_DataReceived; } catch { }
                        try { if (sp.IsOpen) sp.Close(); } catch { }
                        try { sp.Dispose(); } catch { }
                    }
                }
                finally
                {
                    Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        _serialPort = null;
                        IsOpen = false;
                        IsSending = false;
                        IsReceiving = false;
                        StopTimedSendInternal();
                        RaisePropertyChanged(nameof(SendStatusText));
                        RaisePropertyChanged(nameof(ReceiveStatusText));
                        StatusText = "已关闭";
                        IsBusy = false;
                    }));
                }
            });
        }

        private bool IsTimedSendSupported()
        {
            return string.Equals(_rsType, "RS422", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(_rsType, "RS232", StringComparison.OrdinalIgnoreCase);
        }

        private void ToggleTimedSend()
        {
            if (!IsTimedSendSupported())
            {
                StatusText = "当前仅RS422/RS232支持定时发送";
                return;
            }

            if (!IsOpen || _serialPort == null)
            {
                StatusText = "串口未打开";
                return;
            }

            if (IsTimedSendEnabled)
            {
                StopTimedSendInternal();
                TimedSendStatusText = "已停止";
                return;
            }

            EnsureTimedSendTimer();
            UpdateTimedSendInterval();

            IsTimedSendEnabled = true;
            _timedSendTimer.Start();
            TimedSendStatusText = $"已启动：{_timedSendTimer.Interval.TotalMilliseconds:0} ms";
        }

        private void EnsureTimedSendTimer()
        {
            if (_timedSendTimer != null) return;

            _timedSendTimer = new DispatcherTimer(DispatcherPriority.Background, Application.Current.Dispatcher);
            _timedSendTimer.Tick += TimedSendTimer_Tick;
        }

        private void UpdateTimedSendInterval()
        {
            if (_timedSendTimer == null) return;

            var ms = TimedSendIntervalMs;
            if (ms < 10) ms = 10;
            _timedSendTimer.Interval = TimeSpan.FromMilliseconds(ms);
        }

        private void TimedSendTimer_Tick(object sender, EventArgs e)
        {
            if (!IsTimedSendEnabled) return;
            if (!IsOpen || _serialPort == null)
            {
                StopTimedSendInternal();
                TimedSendStatusText = "串口已关闭，定时发送停止";
                return;
            }

            if (_isTimedSendTickRunning) return;
            _isTimedSendTickRunning = true;
            try
            {
                if (string.IsNullOrWhiteSpace(SendText))
                {
                    return;
                }
                Send();
            }
            finally
            {
                _isTimedSendTickRunning = false;
            }
        }

        private void StopTimedSendInternal()
        {
            try
            {
                if (_timedSendTimer != null)
                {
                    _timedSendTimer.Stop();
                }
            }
            catch
            {
            }
            IsTimedSendEnabled = false;
        }

        private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                var sp = _serialPort;
                if (sp == null || !sp.IsOpen) return;

                int count = sp.BytesToRead;
                if (count <= 0) return;

                byte[] buffer = new byte[count];
                int read = sp.Read(buffer, 0, count);
                if (read <= 0) return;

                string append;
                if (IsHexDisplay)
                {
                    append = BitConverter.ToString(buffer, 0, read).Replace("-", " ");
                }
                else
                {
                    append = sp.Encoding.GetString(buffer, 0, read);
                }

                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    IsReceiving = true;
                    AppendReceiveChunk(append);
                }));
            }
            catch (Exception ex)
            {
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    StatusText = ex.Message;
                }));
            }
        }

        private void Send()
        {
            if (!IsOpen || _serialPort == null)
            {
                StatusText = "串口未打开";
                return;
            }

            try
            {
                if (string.IsNullOrWhiteSpace(SendText))
                {
                    StatusText = "发送内容为空";
                    return;
                }

                IsSending = true;

                var displayPayload = SendText;

                if (IsHexSend)
                {
                    var bytes = ParseHex(SendText);
                    _serialPort.Write(bytes, 0, bytes.Length);
                    displayPayload = BitConverter.ToString(bytes).Replace("-", " ");
                }
                else
                {
                    _serialPort.Write(SendText);
                }

                StatusText = "已发送";

                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    AppendReceiveLine($"已发送: {displayPayload}");
                }));
            }
            catch (Exception ex)
            {
                StatusText = ex.Message;
            }
            finally
            {
                IsSending = false;
            }
        }

        private static byte[] ParseHex(string text)
        {
            var cleaned = new string(text.Where(c => !char.IsWhiteSpace(c)).ToArray());
            if (cleaned.Length % 2 != 0)
            {
                throw new FormatException("HEX长度必须为偶数");
            }

            int len = cleaned.Length / 2;
            var bytes = new byte[len];
            for (int i = 0; i < len; i++)
            {
                bytes[i] = Convert.ToByte(cleaned.Substring(i * 2, 2), 16);
            }
            return bytes;
        }

        private static Parity ParseParity(string parity)
        {
            return parity switch
            {
                "Odd" => Parity.Odd,
                "Even" => Parity.Even,
                _ => Parity.None
            };
        }

        private static StopBits ParseStopBits(string stopBits)
        {
            return stopBits switch
            {
                "Two" => StopBits.Two,
                _ => StopBits.One
            };
        }
    }
}
