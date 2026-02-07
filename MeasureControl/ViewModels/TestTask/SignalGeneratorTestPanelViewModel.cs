using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using MeasureControl.Drivers;
using MeasureControl.Drivers.ArtSwitch;
using MeasureControl.Models;
using MeasureControl.Models.Devices;
using MeasureControl.Services;
using MeasureControl.Views;
using MeasureControl.Views.Dialogs;
using Prism.Commands;
using Prism.Mvvm;

namespace MeasureControl.ViewModels.TestTask
{
    /// <summary>
    /// 信号发生器独立测试面板ViewModel
    /// </summary>
    public class SignalGeneratorTestPanelViewModel : BindableBase, IDisposable, ICloseGuard
    {
        private readonly IPxiChassisService _pxiChassisService;
        private readonly SignalGeneratorDevice _signalGeneratorDevice;
        private TcpClient _signalGeneratorClient;
        private NetworkStream _signalGeneratorStream;
        private readonly SemaphoreSlim _signalGeneratorIoLock = new SemaphoreSlim(1, 1);
        private TcpClient _oscilloscopeClient;
        private NetworkStream _oscilloscopeStream;
        private readonly SemaphoreSlim _oscilloscopeIoLock = new SemaphoreSlim(1, 1);
        private CancellationTokenSource _oscilloscopeMeasureCts;
        private Task _oscilloscopeMeasureTask;
        private bool _oscilloscopeMeasureItemsDirty;
        private string _oscilloscopeMeasureCategory;
        private CancellationTokenSource _ch1AutoApplyCts;
        private CancellationTokenSource _ch2AutoApplyCts;
        private bool _disposed = false;

        public SignalGeneratorDevice Device => _signalGeneratorDevice;

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

        private static string ConvertVrmsTextToVppTextForPreview(string waveformType, string vrmsText)
        {
            double vrms = TryParseDouble(vrmsText, 0);
            if (vrms <= 0)
                return vrmsText;

            string wf = (waveformType ?? "").Trim().ToUpperInvariant();
            double vpp;
            if (wf == "SIN")
                vpp = vrms * 2.0 * Math.Sqrt(2.0);
            else if (wf == "RAMP")
                vpp = vrms * 2.0 * Math.Sqrt(3.0);
            else
                vpp = vrms * 2.0;

            return vpp.ToString("0.########", CultureInfo.InvariantCulture);
        }

        private static bool TryConvertVrmsVTextToVppVTextForScpi(string waveformType, string vrmsVText, out string vppVText)
        {
            vppVText = null;
            if (string.IsNullOrWhiteSpace(vrmsVText))
                return false;
            if (!double.TryParse(vrmsVText.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double vrms))
                return false;
            if (double.IsNaN(vrms) || double.IsInfinity(vrms) || vrms <= 0)
                return false;

            string wf = (waveformType ?? "").Trim().ToUpperInvariant();
            double vpp;
            if (wf == "SIN")
                vpp = vrms * 2.0 * Math.Sqrt(2.0);
            else if (wf == "RAMP")
                vpp = vrms * 2.0 * Math.Sqrt(3.0);
            else
                vpp = vrms * 2.0;

            vppVText = vpp.ToString("0.########", CultureInfo.InvariantCulture);
            return true;
        }

        private async Task ApplyWaveformForChannelAsync(string channel)
        {
            if (!IsSignalGeneratorConnected || _signalGeneratorStream == null)
            {
                ReMessageBox.Show("信号发生器设备未连接", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            await ApplyWaveformForChannelInternalAsync(channel, false);
        }

        private async Task<bool> ApplyWaveformForChannelInternalAsync(string channel, bool silent)
        {
            if (!IsSignalGeneratorConnected || _signalGeneratorStream == null)
            {
                if (!silent)
                {
                    ReMessageBox.Show("信号发生器设备未连接", "错误",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
                return false;
            }

            if (string.IsNullOrWhiteSpace(channel))
                channel = "1";

            string waveformType = GetSelectedWaveformScpiFunction(channel);

            string frequency = channel == "2" ? (Ch2FrequencyHz ?? "") : (Ch1FrequencyHz ?? "");
            string amplitudeUnit = channel == "2" ? (Ch2AmplitudeUnit ?? "") : (Ch1AmplitudeUnit ?? "");
            string amplitudeValue = channel == "2" ? (Ch2AmplitudeValue ?? "") : (Ch1AmplitudeValue ?? "");
            string offset = channel == "2" ? (Ch2OffsetV ?? "") : (Ch1OffsetV ?? "");
            string duty = channel == "2" ? (Ch2DutyPercent ?? "") : (Ch1DutyPercent ?? "");
            string phase = channel == "2" ? (Ch2PhaseDeg ?? "") : (Ch1PhaseDeg ?? "");
            string symmetry = channel == "2" ? (Ch2SymmetryPercent ?? "") : (Ch1SymmetryPercent ?? "");

            string hzText = null;
            if (IsFrequencySupported(waveformType))
            {
                if (string.IsNullOrWhiteSpace(frequency) || !TryNormalizeFrequencyHzForScpi(frequency, out hzText))
                {
                    if (!silent)
                    {
                        ReMessageBox.Show("频率值无效，请输入如：1000、10K、0.5M", "错误",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    return false;
                }
            }

            string ampText = null;
            string ampUnit = null;
            if (IsAmplitudeSupported(waveformType))
            {
                string unit = string.IsNullOrWhiteSpace(amplitudeUnit) ? "Vpp" : amplitudeUnit.Trim();
                if (string.IsNullOrWhiteSpace(amplitudeValue) || !TryNormalizeVoltageVForScpi(amplitudeValue, out ampText))
                {
                    if (!silent)
                    {
                        ReMessageBox.Show("幅度值无效，请输入如：2.6、2.6V、2600mV", "错误",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    return false;
                }

                if (string.Equals(unit, "Vrms", StringComparison.OrdinalIgnoreCase))
                {
                    if (!TryConvertVrmsVTextToVppVTextForScpi(waveformType, ampText, out string vppText))
                    {
                        if (!silent)
                        {
                            ReMessageBox.Show("Vrms换算失败，请检查输入值", "错误",
                                MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                        return false;
                    }
                    ampText = vppText;
                }
                ampUnit = "VPP";
            }

            await SendSignalGeneratorScpiAsync($":SOURce{channel}:FUNCtion {waveformType}");

            if (hzText != null)
                await SendSignalGeneratorScpiAsync($":SOURce{channel}:FREQuency {hzText}");
            if (ampText != null)
                await SendSignalGeneratorScpiAsync($":SOURce{channel}:VOLTage:UNIT {ampUnit}");
            if (ampText != null)
                await SendSignalGeneratorScpiAsync($":SOURce{channel}:VOLTage {ampText}");
            if (IsOffsetSupported(waveformType) && !string.IsNullOrWhiteSpace(offset))
                await SendSignalGeneratorScpiAsync($":SOURce{channel}:VOLTage:OFFSet {offset}");
            if (IsPhaseSupported(waveformType) && !string.IsNullOrWhiteSpace(phase))
                await SendSignalGeneratorScpiAsync($":SOURce{channel}:PHASe {NormalizeDegreeString(phase)}");
            if (IsDutySupported(waveformType) && !string.IsNullOrWhiteSpace(duty))
                await SendSignalGeneratorScpiAsync($":SOURce{channel}:PULSe:DCYCle {NormalizePercentString(duty)}");
            if (IsSymmetrySupported(waveformType) && !string.IsNullOrWhiteSpace(symmetry))
                await SendSignalGeneratorScpiAsync($":SOURce{channel}:FUNCtion:RAMP:SYMMetry {NormalizePercentString(symmetry)}");

            return true;
        }

        private static bool TryNormalizeFrequencyHzForScpi(string input, out string hzText)
        {
            hzText = null;
            if (string.IsNullOrWhiteSpace(input))
                return false;

            string raw = input.Trim();
            raw = raw.Replace(" ", string.Empty).Replace("\t", string.Empty);
            if (raw.Length == 0)
                return false;

            string s = raw;
            double mul = 1.0;
            string upper = s.ToUpperInvariant();

            if (upper.EndsWith("MHZ", StringComparison.Ordinal))
            {
                mul = 1_000_000.0;
                s = s.Substring(0, s.Length - 3);
            }
            else if (upper.EndsWith("KHZ", StringComparison.Ordinal))
            {
                mul = 1_000.0;
                s = s.Substring(0, s.Length - 3);
            }
            else if (upper.EndsWith("HZ", StringComparison.Ordinal))
            {
                mul = 1.0;
                s = s.Substring(0, s.Length - 2);
            }
            else if (upper.EndsWith("M", StringComparison.Ordinal))
            {
                mul = 1_000_000.0;
                s = s.Substring(0, s.Length - 1);
            }
            else if (upper.EndsWith("K", StringComparison.Ordinal))
            {
                mul = 1_000.0;
                s = s.Substring(0, s.Length - 1);
            }

            s = s.Trim();
            if (s.Length == 0)
                return false;

            if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
            {
                if (!double.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out v))
                    return false;
            }

            if (double.IsNaN(v) || double.IsInfinity(v))
                return false;

            double hz = v * mul;
            if (hz <= 0)
                return false;

            hzText = hz.ToString("0.########", CultureInfo.InvariantCulture);
            return true;
        }

        private static bool TryNormalizeVoltageVForScpi(string input, out string vText)
        {
            vText = null;
            if (string.IsNullOrWhiteSpace(input))
                return false;

            string raw = input.Trim();
            raw = raw.Replace(" ", string.Empty).Replace("\t", string.Empty);
            if (raw.Length == 0)
                return false;

            string s = raw;
            double mul = 1.0;
            string upper = s.ToUpperInvariant();

            if (upper.EndsWith("MV", StringComparison.Ordinal))
            {
                mul = 0.001;
                s = s.Substring(0, s.Length - 2);
            }
            else if (upper.EndsWith("VPP", StringComparison.Ordinal))
            {
                mul = 1.0;
                s = s.Substring(0, s.Length - 3);
            }
            else if (upper.EndsWith("V", StringComparison.Ordinal))
            {
                mul = 1.0;
                s = s.Substring(0, s.Length - 1);
            }

            s = s.Trim();
            if (s.Length == 0)
                return false;

            if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
            {
                if (!double.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out v))
                    return false;
            }

            if (double.IsNaN(v) || double.IsInfinity(v))
                return false;

            double vv = v * mul;
            if (vv <= 0)
                return false;

            vText = vv.ToString("0.########", CultureInfo.InvariantCulture);
            return true;
        }

        private async Task ToggleOutputForChannelAsync(string channel)
        {
            if (!IsSignalGeneratorConnected || _signalGeneratorStream == null)
            {
                ReMessageBox.Show("信号发生器设备未连接", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (string.IsNullOrWhiteSpace(channel))
                channel = "1";

            bool isOn = string.Equals(channel, "2", StringComparison.OrdinalIgnoreCase) ? Ch2OutputEnabled : Ch1OutputEnabled;

            await Task.Run(async () =>
            {
                if (!isOn)
                {
                    bool ok = await ApplyWaveformForChannelInternalAsync(channel, false);
                    if (!ok)
                        return;
                    await SendSignalGeneratorScpiAsync($":OUTPut{channel} ON");
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        if (string.Equals(channel, "2", StringComparison.OrdinalIgnoreCase))
                            Ch2OutputEnabled = true;
                        else
                            Ch1OutputEnabled = true;
                        UpdateOverallOutputState();
                    });
                }
                else
                {
                    await SendSignalGeneratorScpiAsync($":OUTPut{channel} OFF");
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        if (string.Equals(channel, "2", StringComparison.OrdinalIgnoreCase))
                            Ch2OutputEnabled = false;
                        else
                            Ch1OutputEnabled = false;
                        UpdateOverallOutputState();
                    });
                }
            });

            if (OutputEnabled)
            {
                StartOscilloscopeMeasurementMonitoring();
            }
            else
            {
                await StopOscilloscopeMeasurementMonitoringAsync();
                ClearOscilloscopeMeasurements();
            }
        }

        private void UpdateOverallOutputState()
        {
            bool anyOn = Ch1OutputEnabled || Ch2OutputEnabled;
            OutputEnabled = anyOn;
            OutputStatus = anyOn ? "输出开启" : "输出关闭";
        }

        private void ScheduleAutoApplyForChannel(string channel)
        {
            if (!IsSignalGeneratorConnected || _signalGeneratorStream == null)
                return;

            string ch = (channel ?? "").Trim();
            if (string.IsNullOrWhiteSpace(ch))
                ch = "1";

            bool enabled = string.Equals(ch, "2", StringComparison.OrdinalIgnoreCase) ? Ch2OutputEnabled : Ch1OutputEnabled;
            if (!enabled)
                return;

            CancellationTokenSource oldCts;
            if (string.Equals(ch, "2", StringComparison.OrdinalIgnoreCase))
            {
                oldCts = _ch2AutoApplyCts;
                _ch2AutoApplyCts = new CancellationTokenSource();
            }
            else
            {
                oldCts = _ch1AutoApplyCts;
                _ch1AutoApplyCts = new CancellationTokenSource();
            }

            try { oldCts?.Cancel(); } catch { }
            try { oldCts?.Dispose(); } catch { }

            var token = string.Equals(ch, "2", StringComparison.OrdinalIgnoreCase) ? _ch2AutoApplyCts.Token : _ch1AutoApplyCts.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(250, token);
                    if (token.IsCancellationRequested)
                        return;
                    await ApplyWaveformForChannelInternalAsync(ch, true);
                }
                catch (OperationCanceledException)
                {
                }
                catch
                {
                }
            });
        }

        private bool _isSignalGeneratorConnecting;
        public bool IsSignalGeneratorConnecting
        {
            get => _isSignalGeneratorConnecting;
            private set => SetProperty(ref _isSignalGeneratorConnecting, value);
        }

        private static void SafeCloseNetworkStream(ref NetworkStream stream)
        {
            if (stream == null)
                return;
            try { stream.Close(); } catch { }
            try { stream.Dispose(); } catch { }
            stream = null;
        }

        private static void SafeCloseTcpClient(ref TcpClient client)
        {
            if (client == null)
                return;
            try { client.Close(); } catch { }
            try { client.Dispose(); } catch { }
            client = null;
        }

        private string _signalGeneratorIpAddress;
        /// <summary>
        /// 信号发生器设备IP地址
        /// </summary>
        public string SignalGeneratorIpAddress
        {
            get => _signalGeneratorIpAddress;
            set => SetProperty(ref _signalGeneratorIpAddress, value);
        }

        private int _signalGeneratorPort;
        public int SignalGeneratorPort
        {
            get => _signalGeneratorPort;
            set => SetProperty(ref _signalGeneratorPort, value);
        }

        private int _signalGeneratorCommandDelayMs = 50;
        public int SignalGeneratorCommandDelayMs
        {
            get => _signalGeneratorCommandDelayMs;
            set => SetProperty(ref _signalGeneratorCommandDelayMs, value);
        }

        private string _oscilloscopeIpAddress;
        /// <summary>
        /// 示波器设备IP地址（弹窗输入）
        /// </summary>
        public string OscilloscopeIpAddress
        {
            get => _oscilloscopeIpAddress;
            set => SetProperty(ref _oscilloscopeIpAddress, value);
        }

        private bool _isSignalGeneratorConnected;
        /// <summary>
        /// 信号发生器是否已连接
        /// </summary>
        public bool IsSignalGeneratorConnected
        {
            get => _isSignalGeneratorConnected;
            set
            {
                if (SetProperty(ref _isSignalGeneratorConnected, value))
                {
                    RaisePropertyChanged(nameof(SignalGeneratorConnectButtonText));
                }
            }
        }

        /// <summary>
        /// 连接/断开 按钮文本
        /// </summary>
        public string SignalGeneratorConnectButtonText => IsSignalGeneratorConnected ? "检测中" : "断开中";

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

        private string _ch1AmplitudeVpp;
        public string Ch1AmplitudeVpp
        {
            get => _ch1AmplitudeVpp;
            set
            {
                if (SetProperty(ref _ch1AmplitudeVpp, value))
                {
                    RaisePropertyChanged(nameof(Ch1AmplitudeValue));
                    UpdateWaveformPreview("1");
                    ScheduleAutoApplyForChannel("1");
                }
            }
        }

        private string _ch1AmplitudeVrms;
        public string Ch1AmplitudeVrms
        {
            get => _ch1AmplitudeVrms;
            set
            {
                if (SetProperty(ref _ch1AmplitudeVrms, value))
                {
                    RaisePropertyChanged(nameof(Ch1AmplitudeValue));
                    UpdateWaveformPreview("1");
                    ScheduleAutoApplyForChannel("1");
                }
            }
        }

        private string _ch1AmplitudeUnit;
        public string Ch1AmplitudeUnit
        {
            get => _ch1AmplitudeUnit;
            set
            {
                if (SetProperty(ref _ch1AmplitudeUnit, value))
                {
                    RaisePropertyChanged(nameof(Ch1AmplitudeValue));
                    UpdateWaveformPreview("1");
                    ScheduleAutoApplyForChannel("1");
                }
            }
        }

        public string Ch1AmplitudeValue
        {
            get
            {
                return string.Equals(Ch1AmplitudeUnit, "Vrms", StringComparison.OrdinalIgnoreCase)
                    ? Ch1AmplitudeVrms
                    : Ch1AmplitudeVpp;
            }
            set
            {
                if (string.Equals(Ch1AmplitudeUnit, "Vrms", StringComparison.OrdinalIgnoreCase))
                    Ch1AmplitudeVrms = value;
                else
                    Ch1AmplitudeVpp = value;
            }
        }

        private string _ch1FrequencyHz;
        public string Ch1FrequencyHz
        {
            get => _ch1FrequencyHz;
            set
            {
                if (SetProperty(ref _ch1FrequencyHz, value))
                {
                    UpdateWaveformPreview("1");
                    ScheduleAutoApplyForChannel("1");
                }
            }
        }

        private string _ch1OffsetV;
        public string Ch1OffsetV
        {
            get => _ch1OffsetV;
            set
            {
                if (SetProperty(ref _ch1OffsetV, value))
                {
                    UpdateWaveformPreview("1");
                    ScheduleAutoApplyForChannel("1");
                }
            }
        }

        private string _ch1DutyPercent;
        public string Ch1DutyPercent
        {
            get => _ch1DutyPercent;
            set
            {
                if (SetProperty(ref _ch1DutyPercent, value))
                {
                    UpdateWaveformPreview("1");
                    ScheduleAutoApplyForChannel("1");
                }
            }
        }

        private string _ch1PhaseDeg;
        public string Ch1PhaseDeg
        {
            get => _ch1PhaseDeg;
            set
            {
                if (SetProperty(ref _ch1PhaseDeg, value))
                {
                    UpdateWaveformPreview("1");
                    ScheduleAutoApplyForChannel("1");
                }
            }
        }

        private string _ch1SymmetryPercent;
        public string Ch1SymmetryPercent
        {
            get => _ch1SymmetryPercent;
            set
            {
                if (SetProperty(ref _ch1SymmetryPercent, value))
                {
                    UpdateWaveformPreview("1");
                    ScheduleAutoApplyForChannel("1");
                }
            }
        }

        private string _ch2AmplitudeVpp;
        public string Ch2AmplitudeVpp
        {
            get => _ch2AmplitudeVpp;
            set
            {
                if (SetProperty(ref _ch2AmplitudeVpp, value))
                {
                    RaisePropertyChanged(nameof(Ch2AmplitudeValue));
                    UpdateWaveformPreview("2");
                    ScheduleAutoApplyForChannel("2");
                }
            }
        }

        private string _ch2AmplitudeVrms;
        public string Ch2AmplitudeVrms
        {
            get => _ch2AmplitudeVrms;
            set
            {
                if (SetProperty(ref _ch2AmplitudeVrms, value))
                {
                    RaisePropertyChanged(nameof(Ch2AmplitudeValue));
                    UpdateWaveformPreview("2");
                    ScheduleAutoApplyForChannel("2");
                }
            }
        }

        private string _ch2AmplitudeUnit;
        public string Ch2AmplitudeUnit
        {
            get => _ch2AmplitudeUnit;
            set
            {
                if (SetProperty(ref _ch2AmplitudeUnit, value))
                {
                    RaisePropertyChanged(nameof(Ch2AmplitudeValue));
                    UpdateWaveformPreview("2");
                    ScheduleAutoApplyForChannel("2");
                }
            }
        }

        public string Ch2AmplitudeValue
        {
            get
            {
                return string.Equals(Ch2AmplitudeUnit, "Vrms", StringComparison.OrdinalIgnoreCase)
                    ? Ch2AmplitudeVrms
                    : Ch2AmplitudeVpp;
            }
            set
            {
                if (string.Equals(Ch2AmplitudeUnit, "Vrms", StringComparison.OrdinalIgnoreCase))
                    Ch2AmplitudeVrms = value;
                else
                    Ch2AmplitudeVpp = value;
            }
        }

        private string _ch2FrequencyHz;
        public string Ch2FrequencyHz
        {
            get => _ch2FrequencyHz;
            set
            {
                if (SetProperty(ref _ch2FrequencyHz, value))
                {
                    UpdateWaveformPreview("2");
                    ScheduleAutoApplyForChannel("2");
                }
            }
        }

        private string _ch2OffsetV;
        public string Ch2OffsetV
        {
            get => _ch2OffsetV;
            set
            {
                if (SetProperty(ref _ch2OffsetV, value))
                {
                    UpdateWaveformPreview("2");
                    ScheduleAutoApplyForChannel("2");
                }
            }
        }

        private string _ch2DutyPercent;
        public string Ch2DutyPercent
        {
            get => _ch2DutyPercent;
            set
            {
                if (SetProperty(ref _ch2DutyPercent, value))
                {
                    UpdateWaveformPreview("2");
                    ScheduleAutoApplyForChannel("2");
                }
            }
        }

        private string _ch2PhaseDeg;
        public string Ch2PhaseDeg
        {
            get => _ch2PhaseDeg;
            set
            {
                if (SetProperty(ref _ch2PhaseDeg, value))
                {
                    UpdateWaveformPreview("2");
                    ScheduleAutoApplyForChannel("2");
                }
            }
        }

        private string _ch2SymmetryPercent;
        public string Ch2SymmetryPercent
        {
            get => _ch2SymmetryPercent;
            set
            {
                if (SetProperty(ref _ch2SymmetryPercent, value))
                {
                    UpdateWaveformPreview("2");
                    ScheduleAutoApplyForChannel("2");
                }
            }
        }

        // 波形类型相关属性
        private ObservableCollection<string> _waveformTypes;
        /// <summary>
        /// 波形类型列表
        /// </summary>
        public ObservableCollection<string> WaveformTypes
        {
            get => _waveformTypes;
            set => SetProperty(ref _waveformTypes, value);
        }

        private string _ch1SelectedWaveformType;
        public string Ch1SelectedWaveformType
        {
            get => _ch1SelectedWaveformType;
            set
            {
                if (SetProperty(ref _ch1SelectedWaveformType, value))
                {
                    RaisePropertyChanged(nameof(Ch1WaveformDisplayText));
                    RaisePropertyChanged(nameof(Ch1ShowAmplitude));
                    RaisePropertyChanged(nameof(Ch1ShowFrequency));
                    RaisePropertyChanged(nameof(Ch1ShowOffset));
                    RaisePropertyChanged(nameof(Ch1ShowDuty));
                    RaisePropertyChanged(nameof(Ch1ShowPhase));
                    RaisePropertyChanged(nameof(Ch1ShowSymmetry));
                    UpdateWaveformPreview("1");
                    UpdateOscilloscopeMeasurementVisibility();
                    ScheduleAutoApplyForChannel("1");
                }
            }
        }

        private string _ch2SelectedWaveformType;
        public string Ch2SelectedWaveformType
        {
            get => _ch2SelectedWaveformType;
            set
            {
                if (SetProperty(ref _ch2SelectedWaveformType, value))
                {
                    RaisePropertyChanged(nameof(Ch2WaveformDisplayText));
                    RaisePropertyChanged(nameof(Ch2ShowAmplitude));
                    RaisePropertyChanged(nameof(Ch2ShowFrequency));
                    RaisePropertyChanged(nameof(Ch2ShowOffset));
                    RaisePropertyChanged(nameof(Ch2ShowDuty));
                    RaisePropertyChanged(nameof(Ch2ShowPhase));
                    RaisePropertyChanged(nameof(Ch2ShowSymmetry));
                    UpdateWaveformPreview("2");
                    UpdateOscilloscopeMeasurementVisibility();
                    ScheduleAutoApplyForChannel("2");
                }
            }
        }

        public string Ch1WaveformDisplayText => GetWaveformDisplayText(Ch1SelectedWaveformType);
        public string Ch2WaveformDisplayText => GetWaveformDisplayText(Ch2SelectedWaveformType);

        private PointCollection _ch1WaveformPoints;
        public PointCollection Ch1WaveformPoints
        {
            get => _ch1WaveformPoints;
            set => SetProperty(ref _ch1WaveformPoints, value);
        }

        private PointCollection _ch2WaveformPoints;
        public PointCollection Ch2WaveformPoints
        {
            get => _ch2WaveformPoints;
            set => SetProperty(ref _ch2WaveformPoints, value);
        }

        public bool Ch1ShowAmplitude => IsAmplitudeSupported(GetSelectedWaveformScpiFunction("1"));
        public bool Ch1ShowFrequency => IsFrequencySupported(GetSelectedWaveformScpiFunction("1"));
        public bool Ch1ShowOffset => IsOffsetSupported(GetSelectedWaveformScpiFunction("1"));
        public bool Ch1ShowDuty => IsDutySupported(GetSelectedWaveformScpiFunction("1"));
        public bool Ch1ShowPhase => IsPhaseSupported(GetSelectedWaveformScpiFunction("1"));
        public bool Ch1ShowSymmetry => IsSymmetrySupported(GetSelectedWaveformScpiFunction("1"));

        public bool Ch2ShowAmplitude => IsAmplitudeSupported(GetSelectedWaveformScpiFunction("2"));
        public bool Ch2ShowFrequency => IsFrequencySupported(GetSelectedWaveformScpiFunction("2"));
        public bool Ch2ShowOffset => IsOffsetSupported(GetSelectedWaveformScpiFunction("2"));
        public bool Ch2ShowDuty => IsDutySupported(GetSelectedWaveformScpiFunction("2"));
        public bool Ch2ShowPhase => IsPhaseSupported(GetSelectedWaveformScpiFunction("2"));
        public bool Ch2ShowSymmetry => IsSymmetrySupported(GetSelectedWaveformScpiFunction("2"));

        private ObservableCollection<string> _amplitudeUnits;
        public ObservableCollection<string> AmplitudeUnits
        {
            get => _amplitudeUnits;
            set => SetProperty(ref _amplitudeUnits, value);
        }

        // 频率相关属性
        private ObservableCollection<string> _frequencies;
        /// <summary>
        /// 频率列表
        /// </summary>
        public ObservableCollection<string> Frequencies
        {
            get => _frequencies;
            set => SetProperty(ref _frequencies, value);
        }

        private string _selectedFrequency;
        /// <summary>
        /// 选中的频率
        /// </summary>
        public string SelectedFrequency
        {
            get => _selectedFrequency;
            set => SetProperty(ref _selectedFrequency, value);
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
            set
            {
                if (SetProperty(ref _selectedChannel, value))
                {
                    UpdateOscilloscopeMeasurementVisibility();
                }
            }
        }

        private bool _ch1OutputEnabled;
        public bool Ch1OutputEnabled
        {
            get => _ch1OutputEnabled;
            set => SetProperty(ref _ch1OutputEnabled, value);
        }

        private bool _ch2OutputEnabled;
        public bool Ch2OutputEnabled
        {
            get => _ch2OutputEnabled;
            set => SetProperty(ref _ch2OutputEnabled, value);
        }

        private bool _outputEnabled;
        /// <summary>
        /// 输出使能
        /// </summary>
        public bool OutputEnabled
        {
            get => _outputEnabled;
            set => SetProperty(ref _outputEnabled, value);
        }

        private string _outputStatus;
        /// <summary>
        /// 输出状态
        /// </summary>
        public string OutputStatus
        {
            get => _outputStatus;
            set => SetProperty(ref _outputStatus, value);
        }

        private string _oscVpp;
        public string OscVpp
        {
            get => _oscVpp;
            set => SetProperty(ref _oscVpp, value);
        }

        private string _oscFrequency;
        public string OscFrequency
        {
            get => _oscFrequency;
            set => SetProperty(ref _oscFrequency, value);
        }

        private string _oscPeriod;
        public string OscPeriod
        {
            get => _oscPeriod;
            set => SetProperty(ref _oscPeriod, value);
        }

        private string _oscVmax;
        public string OscVmax
        {
            get => _oscVmax;
            set => SetProperty(ref _oscVmax, value);
        }

        private string _oscVmin;
        public string OscVmin
        {
            get => _oscVmin;
            set => SetProperty(ref _oscVmin, value);
        }

        private string _oscVavg;
        public string OscVavg
        {
            get => _oscVavg;
            set => SetProperty(ref _oscVavg, value);
        }

        private string _oscVrms;
        public string OscVrms
        {
            get => _oscVrms;
            set => SetProperty(ref _oscVrms, value);
        }

        private string _oscPwidth;
        public string OscPwidth
        {
            get => _oscPwidth;
            set => SetProperty(ref _oscPwidth, value);
        }

        private string _oscNwidth;
        public string OscNwidth
        {
            get => _oscNwidth;
            set => SetProperty(ref _oscNwidth, value);
        }

        private bool _showOscVpp;
        public bool ShowOscVpp
        {
            get => _showOscVpp;
            set => SetProperty(ref _showOscVpp, value);
        }

        private bool _showOscFrequency;
        public bool ShowOscFrequency
        {
            get => _showOscFrequency;
            set => SetProperty(ref _showOscFrequency, value);
        }

        private bool _showOscPeriod;
        public bool ShowOscPeriod
        {
            get => _showOscPeriod;
            set => SetProperty(ref _showOscPeriod, value);
        }

        private bool _showOscVmax;
        public bool ShowOscVmax
        {
            get => _showOscVmax;
            set => SetProperty(ref _showOscVmax, value);
        }

        private bool _showOscVmin;
        public bool ShowOscVmin
        {
            get => _showOscVmin;
            set => SetProperty(ref _showOscVmin, value);
        }

        private bool _showOscVavg;
        public bool ShowOscVavg
        {
            get => _showOscVavg;
            set => SetProperty(ref _showOscVavg, value);
        }

        private bool _showOscVrms;
        public bool ShowOscVrms
        {
            get => _showOscVrms;
            set => SetProperty(ref _showOscVrms, value);
        }

        private bool _showOscPwidth;
        public bool ShowOscPwidth
        {
            get => _showOscPwidth;
            set => SetProperty(ref _showOscPwidth, value);
        }

        private bool _showOscNwidth;
        public bool ShowOscNwidth
        {
            get => _showOscNwidth;
            set => SetProperty(ref _showOscNwidth, value);
        }

        #endregion

        #region Commands

        public ICommand ConnectSignalGeneratorCommand { get; private set; }
        public ICommand DisconnectSignalGeneratorCommand { get; private set; }
        public ICommand ToggleDeviceCommand { get; private set; }
        public ICommand ToggleSwitchConnectionCommand { get; private set; }
        public ICommand SetWaveformCommand { get; private set; }
        public ICommand ToggleOutputCommand { get; private set; }
        public ICommand SearchVariableCommand { get; private set; }

        public ICommand ApplyWaveformForChannelCommand { get; private set; }
        public ICommand ToggleOutputForChannelCommand { get; private set; }

        public ICommand SelectWaveformTypeCommand { get; private set; }

        #endregion

        #region Constructor

        public SignalGeneratorTestPanelViewModel(IPxiChassisService pxiChassisService)
        {
            _pxiChassisService = pxiChassisService;
            ConnectionStatus = "离线";

            SignalGeneratorIpAddress = "192.168.1.12";
            SignalGeneratorPort = 5555;

            // 初始化波形类型列表
            WaveformTypes = new ObservableCollection<string> { "SIN", "SQU", "RAMP", "PULS", "NOIS", "DC" };
            Ch1SelectedWaveformType = WaveformTypes[0];
            Ch2SelectedWaveformType = WaveformTypes[0];

            AmplitudeUnits = new ObservableCollection<string> { "Vpp", "Vrms" };
            Ch1AmplitudeUnit = AmplitudeUnits[0];
            Ch2AmplitudeUnit = AmplitudeUnits[0];

            // 初始化频率列表（Hz）
            Frequencies = new ObservableCollection<string>
            {
                "1000", "5000", "10000", "50000", "100000", "500000", "1000000", "5000000", "10000000"
            };
            SelectedFrequency = Frequencies[0]; // 默认选择1kHz

            // 初始化通道列表
            Channels = new ObservableCollection<string> { "CH1", "CH2" };
            SelectedChannel = Channels[0]; // 默认选择CH1

            OutputEnabled = false;
            OutputStatus = "输出关闭";

            Ch1OutputEnabled = false;
            Ch2OutputEnabled = false;

            Ch1AmplitudeVpp = "2.6";
            Ch1AmplitudeVrms = "";
            Ch1FrequencyHz = "1000";
            Ch1OffsetV = "0";
            Ch1DutyPercent = "50";

            Ch2AmplitudeVpp = "2.6";
            Ch2AmplitudeVrms = "";
            Ch2FrequencyHz = "1000";
            Ch2OffsetV = "0";
            Ch2DutyPercent = "50";

            UpdateWaveformPreview("1");
            UpdateWaveformPreview("2");

            InitializeCommands();
        }

        private async Task<string> QuerySignalGeneratorScpiAsync(string command, int timeoutMs = 5000)
        {
            if (_signalGeneratorStream == null)
                throw new InvalidOperationException("Signal generator stream not initialized.");

            await _signalGeneratorIoLock.WaitAsync();
            try
            {
                await WriteLineAsync(_signalGeneratorStream, command, timeoutMs);
                string resp = await ReadLineAsync(_signalGeneratorStream, timeoutMs);
                if (SignalGeneratorCommandDelayMs > 0)
                    await Task.Delay(SignalGeneratorCommandDelayMs);
                return resp;
            }
            finally
            {
                _signalGeneratorIoLock.Release();
            }
        }

        private async Task SendSignalGeneratorScpiAsync(string command, int timeoutMs = 5000)
        {
            if (_signalGeneratorStream == null)
                throw new InvalidOperationException("Signal generator stream not initialized.");

            await _signalGeneratorIoLock.WaitAsync();
            try
            {
                await WriteLineAsync(_signalGeneratorStream, command, timeoutMs);
                if (SignalGeneratorCommandDelayMs > 0)
                    await Task.Delay(SignalGeneratorCommandDelayMs);
            }
            finally
            {
                _signalGeneratorIoLock.Release();
            }
        }

        private static async Task WriteLineAsync(NetworkStream stream, string command, int timeoutMs)
        {
            using var cts = new CancellationTokenSource(timeoutMs);
            string payload = command.EndsWith("\n", StringComparison.Ordinal) ? command : command + "\n";
            byte[] bytes = Encoding.ASCII.GetBytes(payload);
            await stream.WriteAsync(bytes, 0, bytes.Length, cts.Token);
            await stream.FlushAsync(cts.Token);
        }

        private static async Task WriteLineAsync(NetworkStream stream, string command, int timeoutMs, CancellationToken token)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            cts.CancelAfter(timeoutMs);
            string payload = command.EndsWith("\n", StringComparison.Ordinal) ? command : command + "\n";
            byte[] bytes = Encoding.ASCII.GetBytes(payload);
            await stream.WriteAsync(bytes, 0, bytes.Length, cts.Token);
            await stream.FlushAsync(cts.Token);
        }

        private static async Task<string> ReadLineAsync(NetworkStream stream, int timeoutMs)
        {
            using var cts = new CancellationTokenSource(timeoutMs);
            var buffer = new byte[1];
            var sb = new StringBuilder();

            while (true)
            {
                int read = await stream.ReadAsync(buffer, 0, 1, cts.Token);
                if (read <= 0)
                    throw new InvalidOperationException("Connection closed by remote host.");

                char ch = (char)buffer[0];
                if (ch == '\n')
                    break;
                if (ch != '\r')
                    sb.Append(ch);
            }

            return sb.ToString().Trim();
        }

        private static async Task<string> ReadLineAsync(NetworkStream stream, int timeoutMs, CancellationToken token)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            cts.CancelAfter(timeoutMs);
            var buffer = new byte[1];
            var sb = new StringBuilder();

            while (true)
            {
                int read = await stream.ReadAsync(buffer, 0, 1, cts.Token);
                if (read <= 0)
                    throw new InvalidOperationException("Connection closed by remote host.");

                char ch = (char)buffer[0];
                if (ch == '\n')
                    break;
                if (ch != '\r')
                    sb.Append(ch);
            }

            return sb.ToString().Trim();
        }

        private static async Task ConnectTcpClientWithTimeoutAsync(TcpClient client, string host, int port, int timeoutMs)
        {
            var connectTask = client.ConnectAsync(host, port);
            var completed = await Task.WhenAny(connectTask, Task.Delay(timeoutMs));
            if (completed != connectTask)
                throw new TimeoutException($"连接超时: {host}:{port}");
            await connectTask;
        }

        public SignalGeneratorTestPanelViewModel(string testTaskName, string configTableName, string chassisName,
            SignalGeneratorDevice signalGeneratorDevice,
            IPxiChassisService pxiChassisService) : this(pxiChassisService)
        {
            TestTaskName = testTaskName;
            ConfigTableName = configTableName;
            ChassisName = chassisName;
            _signalGeneratorDevice = signalGeneratorDevice;

            if (_signalGeneratorDevice != null)
            {
                if (!string.IsNullOrWhiteSpace(_signalGeneratorDevice.IpAddress))
                    SignalGeneratorIpAddress = _signalGeneratorDevice.IpAddress;
                SignalGeneratorIpAddress = SignalGeneratorIpAddress.Trim();
                SignalGeneratorPort = 5555;
                _signalGeneratorDevice.IpAddress = SignalGeneratorIpAddress;
                _signalGeneratorDevice.LanPort = SignalGeneratorPort;
            }
        }

        public SignalGeneratorTestPanelViewModel(string testTaskName, string configTableName, string chassisName,
            IPxiChassisService pxiChassisService) : this(testTaskName, configTableName, chassisName, null, pxiChassisService)
        {
        }

        private void InitializeCommands()
        {
            ConnectSignalGeneratorCommand = new DelegateCommand(async () => await ConnectSignalGeneratorAsync(),
                () => !IsSignalGeneratorConnected && !string.IsNullOrEmpty(SignalGeneratorIpAddress))
                .ObservesProperty(() => IsSignalGeneratorConnected)
                .ObservesProperty(() => SignalGeneratorIpAddress);

            DisconnectSignalGeneratorCommand = new DelegateCommand(async () => await DisconnectSignalGeneratorAsync(),
                () => IsSignalGeneratorConnected)
                .ObservesProperty(() => IsSignalGeneratorConnected);

            ToggleDeviceCommand = new DelegateCommand(async () => await ToggleDeviceAsync(),
                () => true)
                .ObservesProperty(() => IsSignalGeneratorConnected);

            SetWaveformCommand = new DelegateCommand(async () => await SetWaveformAsync(),
                () => IsSignalGeneratorConnected)
                .ObservesProperty(() => IsSignalGeneratorConnected);

            ToggleOutputCommand = new DelegateCommand(async () => await ToggleOutputAsync(),
                () => IsSignalGeneratorConnected)
                .ObservesProperty(() => IsSignalGeneratorConnected);

            ApplyWaveformForChannelCommand = new DelegateCommand<string>(async ch => await ApplyWaveformForChannelAsync(ch),
                ch => IsSignalGeneratorConnected)
                .ObservesProperty(() => IsSignalGeneratorConnected);

            ToggleOutputForChannelCommand = new DelegateCommand<string>(async channel => await ToggleOutputForChannelAsync(channel))
                .ObservesCanExecute(() => IsSignalGeneratorConnected);

            SelectWaveformTypeCommand = new DelegateCommand<string>(t =>
            {
                if (string.IsNullOrWhiteSpace(t))
                    return;

                var parts = t.Split('|');
                if (parts.Length == 2)
                {
                    string ch = parts[0]?.Trim();
                    string wf = parts[1]?.Trim();
                    if (string.Equals(ch, "1", StringComparison.OrdinalIgnoreCase))
                        Ch1SelectedWaveformType = wf;
                    else if (string.Equals(ch, "2", StringComparison.OrdinalIgnoreCase))
                        Ch2SelectedWaveformType = wf;
                    return;
                }

                Ch1SelectedWaveformType = t.Trim();
            });
        }

        #endregion

        #region Methods

        /// <summary>
        /// 连接信号发生器设备
        /// </summary>
        private async Task ToggleDeviceAsync()
        {
            if (IsSignalGeneratorConnected)
            {
                await DisconnectSignalGeneratorAsync();
            }
            else
            {
                await ConnectSignalGeneratorAsync();
            }
        }

        private async Task ConnectSignalGeneratorAsync()
        {
            if (_signalGeneratorDevice != null)
            {
                if (string.IsNullOrWhiteSpace(SignalGeneratorIpAddress) && !string.IsNullOrWhiteSpace(_signalGeneratorDevice.IpAddress))
                    SignalGeneratorIpAddress = _signalGeneratorDevice.IpAddress;

                if (string.IsNullOrWhiteSpace(SignalGeneratorIpAddress))
                {
                    ReMessageBox.Show("请输入信号发生器设备IP地址", "提示",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                SignalGeneratorIpAddress = SignalGeneratorIpAddress.Trim();
                SignalGeneratorPort = 5555;
                _signalGeneratorDevice.IpAddress = SignalGeneratorIpAddress;
                _signalGeneratorDevice.LanPort = SignalGeneratorPort;
            }
            else if (string.IsNullOrEmpty(SignalGeneratorIpAddress))
            {
                ReMessageBox.Show("请输入信号发生器设备IP地址", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            else
            {
                SignalGeneratorPort = 5555;
            }

            try
            {
                ConnectionStatus = "检测中";
                IsSignalGeneratorConnected = false;
                IsSignalGeneratorConnecting = true;

                // 第一步：先连接信号发生器
                string signalGeneratorInfo = null;
                await Task.Run(async () =>
                {
                    _signalGeneratorClient = new TcpClient();
                    await ConnectTcpClientWithTimeoutAsync(_signalGeneratorClient, SignalGeneratorIpAddress, SignalGeneratorPort, 5000);
                    _signalGeneratorStream = _signalGeneratorClient.GetStream();
                    signalGeneratorInfo = await QuerySignalGeneratorScpiAsync("*IDN?", 5000);

                    System.Diagnostics.Debug.WriteLine($"[SignalGeneratorTestPanel] 信号发生器设备信息: {signalGeneratorInfo}");

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        ConnectionStatus = "在线";
                    });
                });

                if (_oscilloscopeStream != null)
                {
                    await InitializeOscilloscopeMeasurementSessionAsync();
                }

                // 第四步：更新最终状态
                Application.Current.Dispatcher.Invoke(() =>
                {
                    IsSignalGeneratorConnected = true;
                    ConnectionStatus = "在线";
                });
            }
            catch (Exception ex)
            {
                ConnectionStatus = "离线";
                IsSignalGeneratorConnected = false;
                ReMessageBox.Show($"连接失败: {ex.Message}", "连接错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                System.Diagnostics.Debug.WriteLine($"[SignalGeneratorTestPanel] 连接失败: {ex.Message}");

                // 清理已连接的资源
                try
                {
                    SafeCloseNetworkStream(ref _signalGeneratorStream);
                    SafeCloseTcpClient(ref _signalGeneratorClient);
                    SafeCloseNetworkStream(ref _oscilloscopeStream);
                    SafeCloseTcpClient(ref _oscilloscopeClient);
                   
                }
                catch { }
            }
            finally
            {
                IsSignalGeneratorConnecting = false;
            }
        }

        /// <summary>
        /// 断开信号发生器设备连接
        /// </summary>
        private async Task DisconnectSignalGeneratorAsync()
        {
            try
            {
                ConnectionStatus = "断开中";

                IsSignalGeneratorConnecting = true;

                IsSignalGeneratorConnected = false;
                Ch1OutputEnabled = false;
                Ch2OutputEnabled = false;
                OutputEnabled = false;
                OutputStatus = "输出关闭";
                ConnectionStatus = "离线";

                try { _ch1AutoApplyCts?.Cancel(); } catch { }
                try { _ch2AutoApplyCts?.Cancel(); } catch { }
                try { _ch1AutoApplyCts?.Dispose(); } catch { }
                try { _ch2AutoApplyCts?.Dispose(); } catch { }
                _ch1AutoApplyCts = null;
                _ch2AutoApplyCts = null;


                await StopOscilloscopeMeasurementMonitoringAsync();
                ClearOscilloscopeMeasurements();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SignalGeneratorTestPanel] 断开连接失败: {ex.Message}");
                ConnectionStatus = "断开失败";

                Application.Current.Dispatcher.Invoke(() =>
                {
                    IsSignalGeneratorConnected = false;
                    ConnectionStatus = "断开失败";
                });
            }
            finally
            {
                IsSignalGeneratorConnecting = false;
            }
        }

        /// <summary>
        /// 设置波形参数
        /// </summary>
        private async Task SetWaveformAsync()
        {
            if (!IsSignalGeneratorConnected || _signalGeneratorStream == null)
            {
                ReMessageBox.Show("信号发生器设备未连接", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                await ApplyWaveformForChannelAsync(SelectedChannel == "CH1" ? "1" : "2");
            }
            catch (Exception ex)
            {
                ReMessageBox.Show($"设置波形失败: {ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                System.Diagnostics.Debug.WriteLine($"[SignalGeneratorTestPanel] 设置波形失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 切换输出使能
        /// </summary>
        private async Task ToggleOutputAsync()
        {
            if (!IsSignalGeneratorConnected || _signalGeneratorStream == null)
            {
                ReMessageBox.Show("信号发生器设备未连接", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                await ToggleOutputForChannelAsync(SelectedChannel == "CH1" ? "1" : "2");
            }
            catch (Exception ex)
            {
                ReMessageBox.Show($"切换输出状态失败: {ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                System.Diagnostics.Debug.WriteLine($"[SignalGeneratorTestPanel] 切换输出状态失败: {ex.Message}");
            }

            if (OutputEnabled)
            {
                StartOscilloscopeMeasurementMonitoring();
            }
            else
            {
                await StopOscilloscopeMeasurementMonitoringAsync();
                ClearOscilloscopeMeasurements();
            }
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

        private async Task InitializeOscilloscopeMeasurementSessionAsync()
        {
            if (_oscilloscopeStream == null)
                return;

            try
            {
                UpdateOscilloscopeMeasurementVisibility();
                _oscilloscopeMeasureItemsDirty = true;

                await _oscilloscopeIoLock.WaitAsync(CancellationToken.None);
                try
                {
                    await WriteLineAsync(_oscilloscopeStream, ":MEASure:SOURce CHANnel1", 3000);
                    await WriteLineAsync(_oscilloscopeStream, ":MEASure:CLEar", 3000);
                }
                finally
                {
                    _oscilloscopeIoLock.Release();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SignalGeneratorTestPanel] 初始化示波器测量失败: {ex.Message}");
            }
        }

        private string GetOscilloscopeMeasureCategory()
        {
            string ch = SelectedChannel;
            string t = string.Equals(ch, "CH2", StringComparison.OrdinalIgnoreCase) ? Ch2SelectedWaveformType : Ch1SelectedWaveformType;
            if (!string.IsNullOrEmpty(t) && string.Equals(t, "SQU", StringComparison.OrdinalIgnoreCase))
                return "SQUARE";
            return "SINE";
        }

        private string GetSelectedWaveformScpiFunction(string channel)
        {
            string t = string.Equals(channel, "2", StringComparison.OrdinalIgnoreCase) ? Ch2SelectedWaveformType?.Trim() : Ch1SelectedWaveformType?.Trim();
            if (string.IsNullOrWhiteSpace(t))
                return "SIN";

            if (string.Equals(t, "SIN", StringComparison.OrdinalIgnoreCase) || t.Contains("正弦"))
                return "SIN";
            if (string.Equals(t, "SQU", StringComparison.OrdinalIgnoreCase) || t.Contains("方波"))
                return "SQU";
            if (string.Equals(t, "RAMP", StringComparison.OrdinalIgnoreCase) || t.Contains("三角"))
                return "RAMP";
            if (string.Equals(t, "PULS", StringComparison.OrdinalIgnoreCase) || t.Contains("脉冲"))
                return "PULS";
            if (string.Equals(t, "NOIS", StringComparison.OrdinalIgnoreCase) || t.Contains("噪"))
                return "NOIS";
            if (string.Equals(t, "DC", StringComparison.OrdinalIgnoreCase) || t.Contains("直流"))
                return "DC";

            return t;
        }

        private void UpdateWaveformPreview(string channel)
        {
            string ch = (channel ?? "").Trim();
            if (string.IsNullOrWhiteSpace(ch))
                ch = "1";

            string waveformType = GetSelectedWaveformScpiFunction(ch);

            string amplitudeVppText;
            if (string.Equals(ch, "2", StringComparison.OrdinalIgnoreCase))
            {
                amplitudeVppText = string.Equals(Ch2AmplitudeUnit, "Vrms", StringComparison.OrdinalIgnoreCase)
                    ? ConvertVrmsTextToVppTextForPreview(waveformType, Ch2AmplitudeVrms)
                    : Ch2AmplitudeVpp;
            }
            else
            {
                amplitudeVppText = string.Equals(Ch1AmplitudeUnit, "Vrms", StringComparison.OrdinalIgnoreCase)
                    ? ConvertVrmsTextToVppTextForPreview(waveformType, Ch1AmplitudeVrms)
                    : Ch1AmplitudeVpp;
            }
            string offsetVText = ch == "2" ? Ch2OffsetV : Ch1OffsetV;
            string dutyPercentText = ch == "2" ? Ch2DutyPercent : Ch1DutyPercent;
            string phaseDegText = ch == "2" ? Ch2PhaseDeg : Ch1PhaseDeg;
            string symmetryPercentText = ch == "2" ? Ch2SymmetryPercent : Ch1SymmetryPercent;

            double amplitudeVpp = TryParseDouble(amplitudeVppText, 1);
            double offsetV = TryParseDouble(offsetVText, 0);
            double dutyPercent = TryParseDouble(dutyPercentText, 50);
            double phaseDeg = TryParseDouble(phaseDegText, 0);
            double symmetryPercent = TryParseDouble(symmetryPercentText, 50);

            var points = BuildWaveformPoints(waveformType, amplitudeVpp, offsetV, dutyPercent, phaseDeg, symmetryPercent);

            if (ch == "2")
                Ch2WaveformPoints = points;
            else
                Ch1WaveformPoints = points;
        }

        private static double TryParseDouble(string text, double defaultValue)
        {
            if (string.IsNullOrWhiteSpace(text))
                return defaultValue;

            string cleaned = text.Trim();
            var sb = new StringBuilder(cleaned.Length);
            for (int i = 0; i < cleaned.Length; i++)
            {
                char c = cleaned[i];
                if ((c >= '0' && c <= '9') || c == '.' || c == '-' || c == '+' || c == 'e' || c == 'E')
                    sb.Append(c);
            }
            cleaned = sb.ToString();

            if (double.TryParse(cleaned, out double v))
                return v;
            return defaultValue;
        }

        private static double ClampDouble(double value, double min, double max)
        {
            if (value < min)
                return min;
            if (value > max)
                return max;
            return value;
        }

        private static PointCollection BuildWaveformPoints(
            string waveformType,
            double amplitudeVpp,
            double offsetV,
            double dutyPercent,
            double phaseDeg,
            double symmetryPercent)
        {
            const double width = 220;
            const double height = 70;
            const int samples = 220;

            double phase = (phaseDeg % 360.0) / 360.0;
            double duty = ClampDouble(dutyPercent / 100.0, 0.01, 0.99);
            double symmetry = ClampDouble(symmetryPercent / 100.0, 0.01, 0.99);

            double ampScale = ClampDouble(Math.Abs(amplitudeVpp) / 10.0, 0.15, 1.0);
            double offsetScale = ClampDouble(offsetV / 10.0, -0.6, 0.6);

            double centerY = height / 2.0;
            double scaleY = (height * 0.40) * ampScale;
            double offsetPx = offsetScale * (height * 0.40);

            string wf = (waveformType ?? "").Trim().ToUpperInvariant();

            int seed = (wf.GetHashCode() * 397) ^ amplitudeVpp.GetHashCode() ^ offsetV.GetHashCode() ^ dutyPercent.GetHashCode() ^ phaseDeg.GetHashCode() ^ symmetryPercent.GetHashCode();
            var rand = new Random(seed);

            var pts = new PointCollection(samples);
            for (int i = 0; i < samples; i++)
            {
                double x01 = samples == 1 ? 0 : (double)i / (samples - 1);
                double xShift = x01 + phase;
                xShift -= Math.Floor(xShift);

                double y;
                if (wf == "SIN")
                {
                    y = Math.Sin(2.0 * Math.PI * xShift);
                }
                else if (wf == "SQU" || wf == "PULS")
                {
                    y = xShift < duty ? 1.0 : -1.0;
                }
                else if (wf == "RAMP")
                {
                    if (xShift < symmetry)
                    {
                        y = -1.0 + (xShift / symmetry) * 2.0;
                    }
                    else
                    {
                        double t = (xShift - symmetry) / (1.0 - symmetry);
                        y = 1.0 - t * 2.0;
                    }
                }
                else if (wf == "DC")
                {
                    y = 0;
                }
                else if (wf == "NOIS")
                {
                    y = rand.NextDouble() * 2.0 - 1.0;
                }
                else
                {
                    y = Math.Sin(2.0 * Math.PI * xShift);
                }

                double px = x01 * width;
                double py = centerY - (y * scaleY) - offsetPx;
                py = ClampDouble(py, 0, height);

                pts.Add(new Point(px, py));
            }

            return pts;
        }

        private static string GetWaveformDisplayText(string t)
        {
            string code = (t ?? "").Trim().ToUpperInvariant();
            switch (code)
            {
                case "SIN":
                    return "正弦波 (SIN)";
                case "SQU":
                    return "方波 (SQU)";
                case "RAMP":
                    return "三角波 (RAMP)";
                case "PULS":
                    return "脉冲 (PULS)";
                case "NOIS":
                    return "噪声 (NOIS)";
                case "DC":
                    return "直流 (DC)";
                default:
                    return string.IsNullOrWhiteSpace(code) ? "--" : code;
            }
        }

        private static bool IsFrequencySupported(string waveformType)
        {
            string wf = (waveformType ?? "").Trim().ToUpperInvariant();
            return wf == "SIN" || wf == "SQU" || wf == "RAMP" || wf == "PULS";
        }

        private static bool IsAmplitudeSupported(string waveformType)
        {
            string wf = (waveformType ?? "").Trim().ToUpperInvariant();
            return wf == "SIN" || wf == "SQU" || wf == "RAMP" || wf == "PULS" || wf == "NOIS";
        }

        private static bool IsOffsetSupported(string waveformType)
        {
            string wf = (waveformType ?? "").Trim().ToUpperInvariant();
            return wf == "SIN" || wf == "SQU" || wf == "RAMP" || wf == "PULS" || wf == "NOIS" || wf == "DC";
        }

        private static bool IsDutySupported(string waveformType)
        {
            string wf = (waveformType ?? "").Trim().ToUpperInvariant();
            return wf == "SQU" || wf == "PULS";
        }

        private static bool IsPhaseSupported(string waveformType)
        {
            string wf = (waveformType ?? "").Trim().ToUpperInvariant();
            return wf == "SIN" || wf == "SQU" || wf == "RAMP" || wf == "PULS";
        }

        private static bool IsSymmetrySupported(string waveformType)
        {
            string wf = (waveformType ?? "").Trim().ToUpperInvariant();
            return wf == "RAMP";
        }

        private static string NormalizePercentString(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return value;
            return value.Trim().Replace("%", "");
        }

        private static string NormalizeDegreeString(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return value;
            return value.Trim().Replace("°", "");
        }

        private void UpdateOscilloscopeMeasurementVisibility()
        {
            string category = GetOscilloscopeMeasureCategory();
            bool changed = !string.Equals(_oscilloscopeMeasureCategory, category, StringComparison.OrdinalIgnoreCase);
            _oscilloscopeMeasureCategory = category;

            ShowOscVpp = true;
            ShowOscFrequency = true;
            ShowOscPeriod = true;
            ShowOscVmax = true;
            ShowOscVmin = true;
            ShowOscVavg = true;
            ShowOscVrms = true;
            ShowOscPwidth = category == "SQUARE";
            ShowOscNwidth = category == "SQUARE";

            if (changed && category == "SINE")
            {
                System.Diagnostics.Debug.WriteLine("[SignalGeneratorTestPanel] 当前波形=正弦，忽略测量项: PWIDth, NWIDth");
            }
            _oscilloscopeMeasureItemsDirty = true;
        }

        private void StartOscilloscopeMeasurementMonitoring()
        {
            if (_oscilloscopeStream == null)
                return;
            if (_oscilloscopeMeasureTask != null && !_oscilloscopeMeasureTask.IsCompleted)
                return;

            _oscilloscopeMeasureCts?.Cancel();
            _oscilloscopeMeasureCts?.Dispose();
            _oscilloscopeMeasureCts = new CancellationTokenSource();
            var token = _oscilloscopeMeasureCts.Token;

            _oscilloscopeMeasureTask = Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        if (!OutputEnabled || _oscilloscopeStream == null)
                        {
                            await Task.Delay(200, token);
                            continue;
                        }

                        await RefreshOscilloscopeMeasurementsOnceAsync(token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[SignalGeneratorTestPanel] 示波器测量轮询失败: {ex.Message}");
                        await Task.Delay(500, token);
                    }
                }
            }, token);
        }

        private async Task StopOscilloscopeMeasurementMonitoringAsync()
        {
            try { _oscilloscopeMeasureCts?.Cancel(); } catch { }
            try
            {
                if (_oscilloscopeMeasureTask != null)
                    await _oscilloscopeMeasureTask;
            }
            catch { }
            finally
            {
                _oscilloscopeMeasureTask = null;
                _oscilloscopeMeasureCts?.Dispose();
                _oscilloscopeMeasureCts = null;
            }
        }

        private async Task ConfigureOscilloscopeMeasurementItemsIfNeededAsync(CancellationToken token)
        {
            if (!_oscilloscopeMeasureItemsDirty)
                return;
            if (_oscilloscopeStream == null)
                return;

            await _oscilloscopeIoLock.WaitAsync(token);
            try
            {
                await WriteLineAsync(_oscilloscopeStream, ":MEASure:CLEar", 3000, token);
                await WriteLineAsync(_oscilloscopeStream, ":MEASure:ITEM VPP", 3000, token);
                await WriteLineAsync(_oscilloscopeStream, ":MEASure:ITEM FREQuency", 3000, token);
                await WriteLineAsync(_oscilloscopeStream, ":MEASure:ITEM PERiod", 3000, token);
                await WriteLineAsync(_oscilloscopeStream, ":MEASure:ITEM VMAX", 3000, token);
                await WriteLineAsync(_oscilloscopeStream, ":MEASure:ITEM VMIN", 3000, token);
                await WriteLineAsync(_oscilloscopeStream, ":MEASure:ITEM VAVG", 3000, token);
                await WriteLineAsync(_oscilloscopeStream, ":MEASure:ITEM VRMS", 3000, token);
                if (ShowOscPwidth)
                    await WriteLineAsync(_oscilloscopeStream, ":MEASure:ITEM PWIDth", 3000, token);
                if (ShowOscNwidth)
                    await WriteLineAsync(_oscilloscopeStream, ":MEASure:ITEM NWIDth", 3000, token);
                _oscilloscopeMeasureItemsDirty = false;
            }
            finally
            {
                _oscilloscopeIoLock.Release();
            }
        }

        private async Task<string> QueryOscilloscopeAsync(string command, CancellationToken token)
        {
            if (_oscilloscopeStream == null)
                return null;

            await _oscilloscopeIoLock.WaitAsync(token);
            try
            {
                await WriteLineAsync(_oscilloscopeStream, command, 3000, token);
                return await ReadLineAsync(_oscilloscopeStream, 3000, token);
            }
            finally
            {
                _oscilloscopeIoLock.Release();
            }
        }

        private static string NormalizeOscilloscopeNumber(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return "--";
            if (double.TryParse(raw, out var d))
            {
                if (double.IsNaN(d) || double.IsInfinity(d) || Math.Abs(d) > 1e36)
                    return "--";
                return raw;
            }
            return raw;
        }

        private async Task RefreshOscilloscopeMeasurementsOnceAsync(CancellationToken token)
        {
            await ConfigureOscilloscopeMeasurementItemsIfNeededAsync(token);

            string vpp = NormalizeOscilloscopeNumber(await QueryOscilloscopeAsync(":MEASure:ITEM? VPP", token));
            string freq = NormalizeOscilloscopeNumber(await QueryOscilloscopeAsync(":MEASure:ITEM? FREQuency", token));
            string per = NormalizeOscilloscopeNumber(await QueryOscilloscopeAsync(":MEASure:ITEM? PERiod", token));
            string vmax = NormalizeOscilloscopeNumber(await QueryOscilloscopeAsync(":MEASure:ITEM? VMAX", token));
            string vmin = NormalizeOscilloscopeNumber(await QueryOscilloscopeAsync(":MEASure:ITEM? VMIN", token));
            string vavg = NormalizeOscilloscopeNumber(await QueryOscilloscopeAsync(":MEASure:ITEM? VAVG", token));
            string vrms = NormalizeOscilloscopeNumber(await QueryOscilloscopeAsync(":MEASure:ITEM? VRMS", token));
            string pw = ShowOscPwidth ? NormalizeOscilloscopeNumber(await QueryOscilloscopeAsync(":MEASure:ITEM? PWIDth", token)) : null;
            string nw = ShowOscNwidth ? NormalizeOscilloscopeNumber(await QueryOscilloscopeAsync(":MEASure:ITEM? NWIDth", token)) : null;

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                OscVpp = vpp;
                OscFrequency = freq;
                OscPeriod = per;
                OscVmax = vmax;
                OscVmin = vmin;
                OscVavg = vavg;
                OscVrms = vrms;
                if (ShowOscPwidth) OscPwidth = pw;
                if (ShowOscNwidth) OscNwidth = nw;
            });

            await Task.Delay(500, token);
        }

        private void ClearOscilloscopeMeasurements()
        {
            OscVpp = "";
            OscFrequency = "";
            OscPeriod = "";
            OscVmax = "";
            OscVmin = "";
            OscVavg = "";
            OscVrms = "";
            OscPwidth = "";
            OscNwidth = "";
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
                        StopOscilloscopeMeasurementMonitoringAsync().Wait(TimeSpan.FromSeconds(1));

                        try { _ch1AutoApplyCts?.Cancel(); } catch { }
                        try { _ch2AutoApplyCts?.Cancel(); } catch { }
                        try { _ch1AutoApplyCts?.Dispose(); } catch { }
                        try { _ch2AutoApplyCts?.Dispose(); } catch { }
                        _ch1AutoApplyCts = null;
                        _ch2AutoApplyCts = null;

                        SafeCloseNetworkStream(ref _signalGeneratorStream);
                        SafeCloseTcpClient(ref _signalGeneratorClient);
                        SafeCloseNetworkStream(ref _oscilloscopeStream);
                        SafeCloseTcpClient(ref _oscilloscopeClient);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[SignalGeneratorTestPanel] Dispose失败: {ex.Message}");
                    }
                }
                _disposed = true;
            }
        }

        #endregion

        public bool CanClose()
        {
            if (IsSignalGeneratorConnecting)
            {
                ReMessageBox.Show($"正在打开信号发生器({CardName})，请稍候连接完成后再切换页面", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }

            return true;
        }
    }
}

