using Prism.Commands;
using Prism.Mvvm;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MeasureControl.Drivers;
using MeasureControl.Models.Devices;
using MeasureControl.Services;
using MeasureControl.Services.HardwareApis;

namespace MeasureControl.ViewModels.SingleBoardTest.HydraulicController
{
    public class HC_6_3ViewModel : BindableBase
    {
        private const string PowerSupplyIpAddress = "192.168.1.15";
        private const double InputVoltageV = 28.0;
        private const double InputCurrentA = 0.1;

        private const int RxChannelIndex = 2;
        private const double ArincRate = 12500.0;

        private const byte TempLabelDec = 125; // 175(oct)
        private const int SamplesPerMeasure = 5;
        private const int SampleTimeoutMs = 5000;
        private const int ResistanceSettleMs = 100;

        private const double R1_Ohm = 763.3;
        private const double R2_Ohm = 1758.6;
        private const double R3_Ohm = 1155.4;

        private const double T1_Min = -66.6;
        private const double T1_Max = -53.4;
        private const double T2_Min = 193.4;
        private const double T2_Max = 206.6;
        private const double T3_Min = 32.4;
        private const double T3_Max = 46.6;

        private const int TempBnrBitLength = 9;
        private const double TempResolution = 1.0;
        private const int TempMsbPosition = 28;

        private readonly SemaphoreSlim _measureLock = new SemaphoreSlim(1, 1);

        private readonly IPxiChassisService _pxiChassisService;
        private CancellationTokenSource _manualCts;
        private CancellationTokenSource _autoCts;

        private IPowerSupplyApi _power;
        private IArt4229Api _arinc;
        private ACTS6010Driver _res;
        private bool _canMeasure;
        private bool _measured1;
        private bool _measured2;
        private bool _measured3;
        private bool _manualAborted;

        private bool _isManualTestRunning;
        private bool _isAutoTestRunning;

        private string _lastTestTime = "--";
        private string _lastTestResult = "--";
        private string _previousTestTime = "--";
        private string _previousTestResult = "--";

        private string _temp1Text = "--";
        private string _temp2Text = "--";
        private string _temp3Text = "--";

        private double? _temp1;
        private double? _temp2;
        private double? _temp3;

        public HC_6_3ViewModel(IPxiChassisService pxiChassisService)
        {
            _pxiChassisService = pxiChassisService;

            ManualTestCommand = new DelegateCommand(async () => await OnManualTestAsync());
            AutoTestCommand = new DelegateCommand(async () => await OnAutoTestAsync());

            MeasurePoint1Command = new DelegateCommand(async () => await OnMeasurePoint1Async(), () => CanMeasurePoint1);
            MeasurePoint2Command = new DelegateCommand(async () => await OnMeasurePoint2Async(), () => CanMeasurePoint2);
            MeasurePoint3Command = new DelegateCommand(async () => await OnMeasurePoint3Async(), () => CanMeasurePoint3);

            ClearLogCommand = new DelegateCommand(() => Logs.Clear());
        }

        public DelegateCommand ManualTestCommand { get; }
        public DelegateCommand AutoTestCommand { get; }

        public DelegateCommand MeasurePoint1Command { get; }
        public DelegateCommand MeasurePoint2Command { get; }
        public DelegateCommand MeasurePoint3Command { get; }

        public DelegateCommand ClearLogCommand { get; }

        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();

        public bool IsManualTestRunning
        {
            get => _isManualTestRunning;
            set
            {
                if (SetProperty(ref _isManualTestRunning, value))
                {
                    RaisePropertyChanged(nameof(CanMeasurePoint1));
                    RaisePropertyChanged(nameof(CanMeasurePoint2));
                    RaisePropertyChanged(nameof(CanMeasurePoint3));
                    MeasurePoint1Command?.RaiseCanExecuteChanged();
                    MeasurePoint2Command?.RaiseCanExecuteChanged();
                    MeasurePoint3Command?.RaiseCanExecuteChanged();
                }
            }
        }

        public bool IsAutoTestRunning
        {
            get => _isAutoTestRunning;
            set
            {
                if (SetProperty(ref _isAutoTestRunning, value))
                {
                    RaisePropertyChanged(nameof(CanMeasurePoint1));
                    RaisePropertyChanged(nameof(CanMeasurePoint2));
                    RaisePropertyChanged(nameof(CanMeasurePoint3));
                    MeasurePoint1Command?.RaiseCanExecuteChanged();
                    MeasurePoint2Command?.RaiseCanExecuteChanged();
                    MeasurePoint3Command?.RaiseCanExecuteChanged();
                }
            }
        }

        public bool CanMeasure
        {
            get => _canMeasure;
            private set
            {
                if (SetProperty(ref _canMeasure, value))
                {
                    RaisePropertyChanged(nameof(CanMeasurePoint1));
                    RaisePropertyChanged(nameof(CanMeasurePoint2));
                    RaisePropertyChanged(nameof(CanMeasurePoint3));
                    MeasurePoint1Command?.RaiseCanExecuteChanged();
                    MeasurePoint2Command?.RaiseCanExecuteChanged();
                    MeasurePoint3Command?.RaiseCanExecuteChanged();
                }
            }
        }

        public bool CanMeasurePoint1 => IsManualTestRunning && CanMeasure && !_measured1;
        public bool CanMeasurePoint2 => IsManualTestRunning && CanMeasure && !_measured2;
        public bool CanMeasurePoint3 => IsManualTestRunning && CanMeasure && !_measured3;

        public string Temp1Text
        {
            get => _temp1Text;
            private set => SetProperty(ref _temp1Text, value);
        }

        public string Temp2Text
        {
            get => _temp2Text;
            private set => SetProperty(ref _temp2Text, value);
        }

        public string Temp3Text
        {
            get => _temp3Text;
            private set => SetProperty(ref _temp3Text, value);
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

            if (IsAutoTestRunning)
            {
                await StopAutoTestAsync().ConfigureAwait(false);
            }

            IsManualTestRunning = true;
            CanMeasure = false;
            _manualAborted = false;

            _measured1 = false;
            _measured2 = false;
            _measured3 = false;

            _temp1 = null;
            _temp2 = null;
            _temp3 = null;
            Temp1Text = "--";
            Temp2Text = "--";
            Temp3Text = "--";

            _manualCts?.Cancel();
            _manualCts?.Dispose();
            _manualCts = new CancellationTokenSource();

            Log("开始手动测试");
            Log($"电源: CH1/CH2 {InputVoltageV:0.###}V {InputCurrentA:0.###}A, IP={PowerSupplyIpAddress}");
            Log($"ARINC429: RX通道{RxChannelIndex + 1}, 码率 {ArincRate:0}bps, 温度Label=175(oct) SDI区分系统");

            try
            {
                await EnsurePowerAsync(_manualCts.Token).ConfigureAwait(false);
                await EnsureArincRxAsync(_manualCts.Token).ConfigureAwait(false);
                await EnsureResistanceAsync(_manualCts.Token).ConfigureAwait(false);
                CanMeasure = true;
                Log("手动测试初始化完成，可分别点击三档电阻测量温度");
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
            CanMeasure = false;
            _manualAborted = false;

            _measured1 = false;
            _measured2 = false;
            _measured3 = false;

            _temp1 = null;
            _temp2 = null;
            _temp3 = null;
            Temp1Text = "--";
            Temp2Text = "--";
            Temp3Text = "--";

            _autoCts?.Cancel();
            _autoCts?.Dispose();
            _autoCts = new CancellationTokenSource();

            Log("开始自动测试");
            Log($"点1: R={R1_Ohm:0.###}Ω SDI=1 温度[{T1_Min:0.###},{T1_Max:0.###}]℃");
            Log($"点2: R={R2_Ohm:0.###}Ω SDI=2 温度[{T2_Min:0.###},{T2_Max:0.###}]℃");
            Log($"点3: R={R3_Ohm:0.###}Ω SDI=3 温度[{T3_Min:0.###},{T3_Max:0.###}]℃");

            try
            {
                await EnsurePowerAsync(_autoCts.Token).ConfigureAwait(false);
                await EnsureArincRxAsync(_autoCts.Token).ConfigureAwait(false);
                await EnsureResistanceAsync(_autoCts.Token).ConfigureAwait(false);

                await MeasurePointAsync("点1", R1_Ohm, sdi: 1, t => Temp1Text = t, v => _temp1 = v, _autoCts.Token).ConfigureAwait(false);
                await Task.Delay(80, _autoCts.Token).ConfigureAwait(false);
                await MeasurePointAsync("点2", R2_Ohm, sdi: 2, t => Temp2Text = t, v => _temp2 = v, _autoCts.Token).ConfigureAwait(false);
                await Task.Delay(80, _autoCts.Token).ConfigureAwait(false);
                await MeasurePointAsync("点3", R3_Ohm, sdi: 3, t => Temp3Text = t, v => _temp3 = v, _autoCts.Token).ConfigureAwait(false);

                _measured1 = _temp1 != null;
                _measured2 = _temp2 != null;
                _measured3 = _temp3 != null;

                await TryFinalizeIfAllMeasuredAsync().ConfigureAwait(false);
                await StopAutoTestAsync().ConfigureAwait(false);
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

        private async Task OnMeasurePoint1Async()
        {
            var ok = await MeasurePointAsync("点1", R1_Ohm, sdi: 1, t => Temp1Text = t, v => _temp1 = v, _manualCts?.Token ?? CancellationToken.None).ConfigureAwait(false);
            if (!IsManualTestRunning || _manualAborted) return;
            if (ok)
            {
                _measured1 = true;
                RaisePropertyChanged(nameof(CanMeasurePoint1));
                MeasurePoint1Command?.RaiseCanExecuteChanged();
            }
            await TryFinalizeIfAllMeasuredAsync().ConfigureAwait(false);
        }

        private async Task OnMeasurePoint2Async()
        {
            var ok = await MeasurePointAsync("点2", R2_Ohm, sdi: 2, t => Temp2Text = t, v => _temp2 = v, _manualCts?.Token ?? CancellationToken.None).ConfigureAwait(false);
            if (!IsManualTestRunning || _manualAborted) return;
            if (ok)
            {
                _measured2 = true;
                RaisePropertyChanged(nameof(CanMeasurePoint2));
                MeasurePoint2Command?.RaiseCanExecuteChanged();
            }
            await TryFinalizeIfAllMeasuredAsync().ConfigureAwait(false);
        }

        private async Task OnMeasurePoint3Async()
        {
            var ok = await MeasurePointAsync("点3", R3_Ohm, sdi: 3, t => Temp3Text = t, v => _temp3 = v, _manualCts?.Token ?? CancellationToken.None).ConfigureAwait(false);
            if (!IsManualTestRunning || _manualAborted) return;
            if (ok)
            {
                _measured3 = true;
                RaisePropertyChanged(nameof(CanMeasurePoint3));
                MeasurePoint3Command?.RaiseCanExecuteChanged();
            }
            await TryFinalizeIfAllMeasuredAsync().ConfigureAwait(false);
        }

        private async Task<bool> MeasurePointAsync(string title, double resistanceOhm, byte sdi, Action<string> setText, Action<double?> setValue, CancellationToken cancellationToken)
        {
            if (!(IsAutoTestRunning || (IsManualTestRunning && CanMeasure)))
            {
                Log($"{title}: 当前未处于测试状态");
                return false;
            }

            await _measureLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                Log($"{title}: 设置程控电阻 RO0/RO1={resistanceOhm:0.###}Ω");
                await SetResistanceAsync(resistanceOhm, cancellationToken).ConfigureAwait(false);
                await Task.Delay(ResistanceSettleMs, cancellationToken).ConfigureAwait(false);

                Log($"{title}: 开始接收温度数据，Label=175(oct) SDI={sdi}");

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
                            if (!_arinc.VerifyOddParity(w.Data429))
                                continue;

                            _arinc.ParseRawWord(w.Data429, out var label, out var wordSdi, out var data19, out var ssm);

                            if (!IsExpectedLabel(label))
                                continue;

                            if (wordSdi != sdi)
                                continue;

                            if (!(ssm == 0 || ssm == 3))
                                continue;

                            var v = DecodeTemp(data19);
                            if (v == null)
                                continue;

                            samples.Add(v.Value);
                            var avg = samples.Average();
                            setText($"{v.Value:0.###} ℃ ({samples.Count}/{SamplesPerMeasure})  平均:{avg:0.###} ℃");

                            if (samples.Count >= SamplesPerMeasure)
                            {
                                setValue(avg);
                                setText($"{avg:0.###} ℃");
                                Log($"{title}: 完成，平均温度={avg:0.###}℃");
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
                    await AbortManualTestAsync($"{title}: 接收超时，未获取到{SamplesPerMeasure}帧有效温度数据").ConfigureAwait(false);
                }
                else
                {
                    Log($"{title}: 接收超时，未获取到{SamplesPerMeasure}帧有效温度数据");
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

        private bool IsExpectedLabel(byte label)
        {
            return label == TempLabelDec || label == _arinc.ReverseLabel(TempLabelDec);
        }

        private double? DecodeTemp(uint data19)
        {
            return _arinc.DecodeBnr(data19, bitLength: TempBnrBitLength, resolution: TempResolution, msbPosition: TempMsbPosition);
        }

        private async Task TryFinalizeIfAllMeasuredAsync()
        {
            if (!(_measured1 && _measured2 && _measured3))
                return;

            var p1 = _temp1 != null && _temp1 >= T1_Min && _temp1 <= T1_Max;
            var p2 = _temp2 != null && _temp2 >= T2_Min && _temp2 <= T2_Max;
            var p3 = _temp3 != null && _temp3 >= T3_Min && _temp3 <= T3_Max;
            var pass = p1 && p2 && p3;

            PreviousTestTime = LastTestTime;
            PreviousTestResult = LastTestResult;
            LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            LastTestResult = pass ? "合格" : "不合格";

            Log($"判据: 点1[{T1_Min:0.###},{T1_Max:0.###}] => {FormatBool(p1)}");
            Log($"判据: 点2[{T2_Min:0.###},{T2_Max:0.###}] => {FormatBool(p2)}");
            Log($"判据: 点3[{T3_Min:0.###},{T3_Max:0.###}] => {FormatBool(p3)}");
            Log($"最终结果: {LastTestResult}");

            if (IsManualTestRunning)
            {
                await StopManualTestAsync().ConfigureAwait(false);
            }
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

            try
            {
                if (_res != null)
                {
                    try { await _res.DisconnectAsync().ConfigureAwait(false); } catch { }
                }
            }
            catch
            {
            }
            finally
            {
                _res = null;
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

        private async Task EnsureResistanceAsync(CancellationToken cancellationToken)
        {
            if (_res != null && _res.IsConnected)
                return;

            var device = FindFirstActs6010Device();
            if (device == null)
                throw new InvalidOperationException("未找到ACTS6010(程控电阻)板卡");

            _res = new ACTS6010Driver(device, logicalId: 0);
            var ok = await _res.ConnectAsync().ConfigureAwait(false);
            if (!ok)
                throw new InvalidOperationException("ACTS6010连接失败");

            await SetResistanceAsync(0.0, cancellationToken).ConfigureAwait(false);
        }

        private async Task SetResistanceAsync(double resistanceOhm, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_res == null || !_res.IsConnected)
                throw new InvalidOperationException("ACTS6010未连接");

            var ok0 = await _res.WriteChannelAsync("RO0", resistanceOhm).ConfigureAwait(false);
            var ok1 = await _res.WriteChannelAsync("RO1", resistanceOhm).ConfigureAwait(false);
            if (!(ok0 && ok1))
                throw new InvalidOperationException("设置ACTS6010阻值失败");
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
                    return device;
            }

            return null;
        }

        private DeviceBase FindFirstActs6010Device()
        {
            var chassisList = _pxiChassisService?.GetAllChassis();
            if (chassisList == null)
                return null;

            foreach (var chassis in chassisList)
            {
                var device = chassis?.Devices?.FirstOrDefault(d =>
                    (d?.Model?.IndexOf("6010", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.Model?.IndexOf("ACTS", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.Name?.IndexOf("6010", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.Name?.IndexOf("ACTS", StringComparison.OrdinalIgnoreCase) >= 0));

                if (device != null)
                    return device;
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
