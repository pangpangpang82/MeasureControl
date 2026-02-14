using Prism.Commands;
using Prism.Mvvm;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MeasureControl.Models.Devices;
using MeasureControl.Services;
using MeasureControl.Services.HardwareApis;

namespace MeasureControl.ViewModels.SingleBoardTest.HydraulicController
{
    public class HC_6_2ViewModel : BindableBase
    {
        private const string PowerSupplyIpAddress = "192.168.1.15";
        private const double InputVoltageV = 28.0;
        private const double InputCurrentA = 0.1;

        private const int RxChannelIndex = 2;
        private const double ArincRate = 12500.0;

        private const byte Label5V = 050;
        private const byte Label15V = 048;
        private const byte LabelM15V = 049;

        private const int SamplesPerMeasure = 5;
        private const int SampleTimeoutMs = 5000;

        private const double Min5V = 4.925;
        private const double Max5V = 5.075;
        private const double Min15V = 14.775;
        private const double Max15V = 15.225;
        private const double MinM15V = -15.225;
        private const double MaxM15V = -14.775;

        private readonly SemaphoreSlim _measureLock = new SemaphoreSlim(1, 1);
        private CancellationTokenSource _manualCts;
        private CancellationTokenSource _autoCts;

        private readonly IPxiChassisService _pxiChassisService;
        private IPowerSupplyApi _power;
        private IArt4229Api _arinc;

        private bool _canMeasure;
        private bool _measured5v;
        private bool _measured15v;
        private bool _measuredM15v;
        private bool _manualAborted;

        private bool _isManualTestRunning;
        private bool _isAutoTestRunning;

        private string _lastTestTime = "--";
        private string _lastTestResult = "--";
        private string _previousTestTime = "--";
        private string _previousTestResult = "--";

        private string _voltage5VText = "--";
        private string _voltage15VText = "--";
        private string _voltageM15VText = "--";

        private double? _voltage5V;
        private double? _voltage15V;
        private double? _voltageM15V;

        public HC_6_2ViewModel(IPxiChassisService pxiChassisService)
        {
            _pxiChassisService = pxiChassisService;

            ManualTestCommand = new DelegateCommand(async () => await OnManualTestAsync());
            AutoTestCommand = new DelegateCommand(async () => await OnAutoTestAsync());

            Measure5VCommand = new DelegateCommand(async () => await OnMeasure5VAsync(), () => CanMeasure5V);
            Measure15VCommand = new DelegateCommand(async () => await OnMeasure15VAsync(), () => CanMeasure15V);
            MeasureM15VCommand = new DelegateCommand(async () => await OnMeasureM15VAsync(), () => CanMeasureM15V);
            ClearLogCommand = new DelegateCommand(() => Logs.Clear());
        }

        public DelegateCommand ManualTestCommand { get; }
        public DelegateCommand AutoTestCommand { get; }

        public DelegateCommand Measure5VCommand { get; }
        public DelegateCommand Measure15VCommand { get; }
        public DelegateCommand MeasureM15VCommand { get; }

        public DelegateCommand ClearLogCommand { get; }

        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();

        public bool IsManualTestRunning
        {
            get => _isManualTestRunning;
            set
            {
                if (SetProperty(ref _isManualTestRunning, value))
                {
                    RaisePropertyChanged(nameof(CanMeasure5V));
                    RaisePropertyChanged(nameof(CanMeasure15V));
                    RaisePropertyChanged(nameof(CanMeasureM15V));
                    Measure5VCommand?.RaiseCanExecuteChanged();
                    Measure15VCommand?.RaiseCanExecuteChanged();
                    MeasureM15VCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        public bool IsAutoTestRunning
        {
            get => _isAutoTestRunning;
            set => SetProperty(ref _isAutoTestRunning, value);
        }

        public bool CanMeasure
        {
            get => _canMeasure;
            private set
            {
                if (SetProperty(ref _canMeasure, value))
                {
                    RaisePropertyChanged(nameof(CanMeasure5V));
                    RaisePropertyChanged(nameof(CanMeasure15V));
                    RaisePropertyChanged(nameof(CanMeasureM15V));
                    Measure5VCommand?.RaiseCanExecuteChanged();
                    Measure15VCommand?.RaiseCanExecuteChanged();
                    MeasureM15VCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        public bool CanMeasure5V => IsManualTestRunning && CanMeasure && !_measured5v;
        public bool CanMeasure15V => IsManualTestRunning && CanMeasure && !_measured15v;
        public bool CanMeasureM15V => IsManualTestRunning && CanMeasure && !_measuredM15v;

        public string Voltage5VText
        {
            get => _voltage5VText;
            private set => SetProperty(ref _voltage5VText, value);
        }

        public string Voltage15VText
        {
            get => _voltage15VText;
            private set => SetProperty(ref _voltage15VText, value);
        }

        public string VoltageM15VText
        {
            get => _voltageM15VText;
            private set => SetProperty(ref _voltageM15VText, value);
        }

        public string LastTestTime
        {
            get => _lastTestTime;
            set => SetProperty(ref _lastTestTime, value);
        }

        public string LastTestResult
        {
            get => _lastTestResult;
            set => SetProperty(ref _lastTestResult, value);
        }

        public string PreviousTestTime
        {
            get => _previousTestTime;
            set => SetProperty(ref _previousTestTime, value);
        }

        public string PreviousTestResult
        {
            get => _previousTestResult;
            set => SetProperty(ref _previousTestResult, value);
        }

        private async Task OnManualTestAsync()
        {
            if (IsManualTestRunning)
            {
                await StopManualTestAsync().ConfigureAwait(false);
                return;
            }

            IsAutoTestRunning = false;
            IsManualTestRunning = true;

            CanMeasure = false;
            _manualAborted = false;
            _measured5v = false;
            _measured15v = false;
            _measuredM15v = false;

            _voltage5V = null;
            _voltage15V = null;
            _voltageM15V = null;
            Voltage5VText = "--";
            Voltage15VText = "--";
            VoltageM15VText = "--";

            _manualCts?.Cancel();
            _manualCts?.Dispose();
            _manualCts = new CancellationTokenSource();

            Log("开始手动测试");
            Log($"电源配置: CH1/CH2 {InputVoltageV:0.###}V {InputCurrentA:0.###}A, IP={PowerSupplyIpAddress}");
            Log($"ARINC429接收: 通道{RxChannelIndex + 1}, 码率 {ArincRate:0}bps");

            try
            {
                await EnsurePowerAsync(_manualCts.Token).ConfigureAwait(false);
                await EnsureArincRxAsync(_manualCts.Token).ConfigureAwait(false);
                CanMeasure = true;
                Log("手动测试初始化完成，可开始分别测量 5V/15V/-15V");
            }
            catch (Exception ex)
            {
                await AbortManualTestAsync($"手动测试初始化失败，中止: {ex.Message}").ConfigureAwait(false);
            }
        }

        private async Task OnAutoTestAsync()
        {
            if (IsAutoTestRunning)
            {
                await StopAutoTestAsync().ConfigureAwait(false);
                return;
            }

            if (IsManualTestRunning)
            {
                await StopManualTestAsync().ConfigureAwait(false);
            }

            IsAutoTestRunning = true;

            _autoCts?.Cancel();
            _autoCts?.Dispose();
            _autoCts = new CancellationTokenSource();

            _manualAborted = false;
            CanMeasure = false;

            _measured5v = false;
            _measured15v = false;
            _measuredM15v = false;

            _voltage5V = null;
            _voltage15V = null;
            _voltageM15V = null;
            Voltage5VText = "--";
            Voltage15VText = "--";
            VoltageM15VText = "--";

            Log("开始自动测试");
            Log($"判据: 5V[{Min5V:0.###},{Max5V:0.###}]  15V[{Min15V:0.###},{Max15V:0.###}]  -15V[{MinM15V:0.###},{MaxM15V:0.###}]");

            try
            {
                await EnsurePowerAsync(_autoCts.Token).ConfigureAwait(false);
                await EnsureArincRxAsync(_autoCts.Token).ConfigureAwait(false);

                await MeasureVoltageFrom429Async(
                        title: "5V",
                        expectedLabel: Label5V,
                        decode: Decode5V,
                        setText: t => Voltage5VText = t,
                        setValue: v => _voltage5V = v,
                        cancellationToken: _autoCts.Token)
                    .ConfigureAwait(false);

                await Task.Delay(120, _autoCts.Token).ConfigureAwait(false);

                await MeasureVoltageFrom429Async(
                        title: "15V",
                        expectedLabel: Label15V,
                        decode: Decode15V,
                        setText: t => Voltage15VText = t,
                        setValue: v => _voltage15V = v,
                        cancellationToken: _autoCts.Token)
                    .ConfigureAwait(false);

                await Task.Delay(120, _autoCts.Token).ConfigureAwait(false);

                await MeasureVoltageFrom429Async(
                        title: "-15V",
                        expectedLabel: LabelM15V,
                        decode: DecodeM15V,
                        setText: t => VoltageM15VText = t,
                        setValue: v => _voltageM15V = v,
                        cancellationToken: _autoCts.Token)
                    .ConfigureAwait(false);

                _measured5v = _voltage5V != null;
                _measured15v = _voltage15V != null;
                _measuredM15v = _voltageM15V != null;

                await TryFinalizeIfAllMeasuredAsync().ConfigureAwait(false);

                if (IsAutoTestRunning)
                {
                    await StopAutoTestAsync().ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                Log("自动测试已停止");
                await StopAutoTestAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log($"自动测试异常: {ex.Message}");
                await StopAutoTestAsync().ConfigureAwait(false);
            }
        }

        private async Task OnMeasure5VAsync()
        {
            var ok = await MeasureVoltageFrom429Async(
                title: "5V",
                expectedLabel: Label5V,
                decode: Decode5V,
                setText: t => Voltage5VText = t,
                setValue: v => _voltage5V = v,
                cancellationToken: _manualCts?.Token ?? CancellationToken.None).ConfigureAwait(false);

            if (!IsManualTestRunning || _manualAborted)
                return;

            if (ok)
            {
                _measured5v = true;
                RaisePropertyChanged(nameof(CanMeasure5V));
                Measure5VCommand?.RaiseCanExecuteChanged();
            }

            await TryFinalizeIfAllMeasuredAsync().ConfigureAwait(false);
        }

        private async Task OnMeasure15VAsync()
        {
            var ok = await MeasureVoltageFrom429Async(
                title: "15V",
                expectedLabel: Label15V,
                decode: Decode15V,
                setText: t => Voltage15VText = t,
                setValue: v => _voltage15V = v,
                cancellationToken: _manualCts?.Token ?? CancellationToken.None).ConfigureAwait(false);

            if (!IsManualTestRunning || _manualAborted)
                return;

            if (ok)
            {
                _measured15v = true;
                RaisePropertyChanged(nameof(CanMeasure15V));
                Measure15VCommand?.RaiseCanExecuteChanged();
            }

            await TryFinalizeIfAllMeasuredAsync().ConfigureAwait(false);
        }

        private async Task OnMeasureM15VAsync()
        {
            var ok = await MeasureVoltageFrom429Async(
                title: "-15V",
                expectedLabel: LabelM15V,
                decode: DecodeM15V,
                setText: t => VoltageM15VText = t,
                setValue: v => _voltageM15V = v,
                cancellationToken: _manualCts?.Token ?? CancellationToken.None).ConfigureAwait(false);

            if (!IsManualTestRunning || _manualAborted)
                return;

            if (ok)
            {
                _measuredM15v = true;
                RaisePropertyChanged(nameof(CanMeasureM15V));
                MeasureM15VCommand?.RaiseCanExecuteChanged();
            }

            await TryFinalizeIfAllMeasuredAsync().ConfigureAwait(false);
        }

        private async Task<bool> MeasureVoltageFrom429Async(
            string title,
            byte expectedLabel,
            Func<uint, double?> decode,
            Action<string> setText,
            Action<double?> setValue,
            CancellationToken cancellationToken)
        {
            if (!(IsAutoTestRunning || (IsManualTestRunning && CanMeasure)))
            {
                Log($"{title}: 当前未处于测试状态");
                return false;
            }

            await _measureLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                Log($"{title}: 开始接收429数据，label={expectedLabel}");

                var samples = new List<double>(SamplesPerMeasure);
                var deadline = DateTime.UtcNow.AddMilliseconds(SampleTimeoutMs);

                while (!cancellationToken.IsCancellationRequested && DateTime.UtcNow <= deadline)
                {
                    var words = await _arinc.ReadRxWordsAsync(RxChannelIndex, maxCount: 512, enableTimeTag: false, enableRateAdaption: false, cancellationToken: cancellationToken)
                        .ConfigureAwait(false);

                    if (words.Count > 0)
                    {
                        foreach (var w in words)
                        {
                            if (!IsExpectedLabel(w.Data429, expectedLabel))
                                continue;

                            if (!_arinc.VerifyOddParity(w.Data429))
                                continue;

                            _arinc.ParseRawWord(w.Data429, out _, out _, out var data19, out var ssm);
                            if (ssm != 3)
                                continue;

                            var v = decode(data19);
                            if (v == null)
                                continue;

                            samples.Add(v.Value);

                            var avg = samples.Average();
                            setText($"{v.Value:0.###} V ({samples.Count}/{SamplesPerMeasure})  平均:{avg:0.###} V");

                            if (samples.Count >= SamplesPerMeasure)
                            {
                                setValue(avg);
                                setText($"{avg:0.###} V");
                                Log($"{title}: 完成，平均值={avg:0.###}V");
                                return true;
                            }
                        }
                    }

                    await Task.Delay(10, cancellationToken).ConfigureAwait(false);
                }

                setText("--");
                setValue(null);
                if (IsManualTestRunning)
                {
                    await AbortManualTestAsync($"{title}: 接收超时，未获取到{SamplesPerMeasure}帧有效数据").ConfigureAwait(false);
                }
                else
                {
                    Log($"{title}: 接收超时，未获取到{SamplesPerMeasure}帧有效数据");
                }

                return false;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception ex)
            {
                setText("--");
                setValue(null);
                if (IsManualTestRunning)
                {
                    await AbortManualTestAsync($"{title}: 采集异常，手动测试中止: {ex.Message}").ConfigureAwait(false);
                }
                else
                {
                    Log($"{title}: 采集异常: {ex.Message}");
                }
                return false;
            }
            finally
            {
                _measureLock.Release();
            }
        }

        private double? Decode5V(uint data19)
        {
            return _arinc.DecodeUbnr(data19, bitLength: 8, resolution: 0.1, msbPosition: 27);
        }

        private double? Decode15V(uint data19)
        {
            return _arinc.DecodeUbnr(data19, bitLength: 8, resolution: 0.1, msbPosition: 27);
        }

        private double? DecodeM15V(uint data19)
        {
            return _arinc.DecodeBnr(data19, bitLength: 9, resolution: 0.1, msbPosition: 28);
        }

        private bool IsExpectedLabel(uint rawWord, byte expected)
        {
            _arinc.ParseRawWord(rawWord, out var label, out _, out _, out _);
            return label == expected || label == _arinc.ReverseLabel(expected);
        }

        private async Task TryFinalizeIfAllMeasuredAsync()
        {
            if (!IsManualTestRunning || _manualAborted)
                return;

            if (!(_measured5v && _measured15v && _measuredM15v))
                return;

            var pass5 = _voltage5V != null && _voltage5V >= Min5V && _voltage5V <= Max5V;
            var pass15 = _voltage15V != null && _voltage15V >= Min15V && _voltage15V <= Max15V;
            var passM15 = _voltageM15V != null && _voltageM15V >= MinM15V && _voltageM15V <= MaxM15V;

            var pass = pass5 && pass15 && passM15;

            PreviousTestTime = LastTestTime;
            PreviousTestResult = LastTestResult;
            LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            LastTestResult = pass ? "合格" : "不合格";

            Log($"判据: 5V[{Min5V:0.###},{Max5V:0.###}] => {FormatBool(pass5)}");
            Log($"判据: 15V[{Min15V:0.###},{Max15V:0.###}] => {FormatBool(pass15)}");
            Log($"判据: -15V[{MinM15V:0.###},{MaxM15V:0.###}] => {FormatBool(passM15)}");
            Log($"最终结果: {LastTestResult}");

            await StopManualTestAsync().ConfigureAwait(false);
        }

        private async Task AbortManualTestAsync(string reason)
        {
            _manualAborted = true;
            if (!string.IsNullOrWhiteSpace(reason))
            {
                Log(reason);
            }

            await StopManualTestAsync().ConfigureAwait(false);
        }

        private async Task StopManualTestAsync()
        {
            try
            {
                CanMeasure = false;
                _manualCts?.Cancel();
            }
            catch
            {
            }

            Log("手动测试停止/结束，正在关闭电源输出并停止429接收...");
            await CleanupIoAsync().ConfigureAwait(false);

            IsManualTestRunning = false;
            Log("手动测试已结束");
        }

        private async Task StopAutoTestAsync()
        {
            try
            {
                _autoCts?.Cancel();
            }
            catch
            {
            }

            Log("自动测试停止/结束，正在关闭电源输出并停止429接收...");
            await CleanupIoAsync().ConfigureAwait(false);

            IsAutoTestRunning = false;
            Log("自动测试已结束");
        }

        private async Task CleanupIoAsync()
        {
            try
            {
                if (_arinc != null)
                {
                    try { await _arinc.StopRxAsync(RxChannelIndex, CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _arinc.CloseRxAsync(RxChannelIndex, CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _arinc.DisconnectAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _arinc.DisposeAsync().ConfigureAwait(false); } catch { }
                }
            }
            catch
            {
            }
            finally
            {
                _arinc = null;
            }

            try
            {
                if (_power != null)
                {
                    try { await _power.SetOutputEnabledAsync(PowerSupplyChannel.CH1, false, CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _power.SetOutputEnabledAsync(PowerSupplyChannel.CH2, false, CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _power.DisconnectAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _power.DisposeAsync().ConfigureAwait(false); } catch { }
                }
            }
            catch
            {
            }
            finally
            {
                _power = null;
            }
        }

        private async Task EnsurePowerAsync(CancellationToken cancellationToken)
        {
            _power ??= new PowerSupplySocketApi();
            await _power.ConnectAsync(PowerSupplyIpAddress, cancellationToken).ConfigureAwait(false);
            await _power.ApplyAsync(PowerSupplyChannel.CH1, InputVoltageV, InputCurrentA, cancellationToken).ConfigureAwait(false);
            await _power.ApplyAsync(PowerSupplyChannel.CH2, InputVoltageV, InputCurrentA, cancellationToken).ConfigureAwait(false);
            await _power.SetOutputEnabledAsync(PowerSupplyChannel.CH1, true, cancellationToken).ConfigureAwait(false);
            await _power.SetOutputEnabledAsync(PowerSupplyChannel.CH2, true, cancellationToken).ConfigureAwait(false);
            await Task.Delay(300, cancellationToken).ConfigureAwait(false);
        }

        private async Task EnsureArincRxAsync(CancellationToken cancellationToken)
        {
            if (_arinc == null)
            {
                var device = FindFirstArincDevice();
                if (device == null)
                    throw new InvalidOperationException("未找到ART4227/ART4229(ARINC429)板卡，无法接收429数据");
                _arinc = new Art4229Api(device, deviceIndex: 0);
            }

            if (!_arinc.IsConnected)
            {
                await _arinc.ConnectAsync(cancellationToken).ConfigureAwait(false);
            }

            await _arinc.OpenRxAsync(RxChannelIndex, cancellationToken).ConfigureAwait(false);
            await _arinc.ConfigureRxAsync(
                RxChannelIndex,
                rate: ArincRate,
                parity: Art4229Parity.Odd,
                wordFormat: Art4229WordFormat.Standard429,
                enableInterrupt: false,
                interruptDepth: 512,
                enableTimeTag: false,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            await _arinc.StartRxAsync(RxChannelIndex, cancellationToken).ConfigureAwait(false);

            _ = await _arinc.ReadRxWordsAsync(RxChannelIndex, maxCount: 4096, enableTimeTag: false, enableRateAdaption: false, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        private DeviceBase FindFirstArincDevice()
        {
            var chassisList = _pxiChassisService?.GetAllChassis();
            if (chassisList == null)
                return null;

            foreach (var chassis in chassisList)
            {
                var device = chassis?.Devices?.FirstOrDefault(d =>
                    (d?.Model?.IndexOf("4227", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.Model?.IndexOf("4229", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.Model?.IndexOf("ARINC", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.Model?.IndexOf("429", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.Name?.IndexOf("4227", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.Name?.IndexOf("4229", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.Name?.IndexOf("429", StringComparison.OrdinalIgnoreCase) >= 0));

                if (device != null)
                {
                    return device;
                }
            }

            return null;
        }

        private void Log(string message)
        {
            var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
            Logs.Add(line);
        }

        private static string FormatBool(bool value) => value ? "PASS" : "FAIL";
    }
}
