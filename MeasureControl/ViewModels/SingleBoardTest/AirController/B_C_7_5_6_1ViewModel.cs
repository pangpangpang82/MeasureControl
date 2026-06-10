using Prism.Commands;
using Prism.Mvvm;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using MeasureControl.Drivers;
using MeasureControl.Helpers;
using MeasureControl.Services;
using MeasureControl.Services.HardwareApis;

namespace MeasureControl.ViewModels.SingleBoardTest.AirController
{
    public sealed class B_C_7_5_6_1ViewModel : BindableBase, IDisposable
    {
        private const string DefaultFpgaIpAddress = "192.168.1.10";
        private const int DefaultFpgaPort = 5001;

        private const string MatrixIpAddress = "192.168.1.3";
        private const int MatrixTcpBasePort = 50200;

        private const int MatrixSlotSig = 6;
        private const int MatrixSlotDmm = 4;
        private static readonly (string In, string Out, int Slot) MatrixPointSig = ("I1", "O21", MatrixSlotSig);
        private static readonly (string In, string Out, int Slot) MatrixDmmH = ("I4", "O2", MatrixSlotDmm);

        private const string DmmIpAddress = "192.168.1.13";
        private const int DmmTimeoutMs = 8000;

        private const string PowerSupply28VIpAddress = "192.168.1.15";
        private const string PowerSupply3V3IpAddress = "192.168.1.16";

        private const PowerSupplyChannel PowerSupply28VCh1 = PowerSupplyChannel.CH1;
        private const PowerSupplyChannel PowerSupply28VCh2 = PowerSupplyChannel.CH2;
        private const double Power28VVoltage = 28.0;
        private const double Power28VCurrentLimit = 3.0;

        private const PowerSupplyChannel PowerSupply3V3Channel = PowerSupplyChannel.CH3;
        private const double Power3V3Voltage = 3.3;
        private const double Power3V3CurrentLimit = 1.0;

        private const int CurrentSet0mA = 0;
        private const int CurrentSet400mA = 400;

        private const double Voltage0mAMin = 0.0;
        private const double Voltage0mAMax = 0.41;

        private const double Voltage400mAMin = 15.21;
        private const double Voltage400mAMax = 16.81;

        private const double Current0mAMinA = 0.0;
        private const double Current0mAMaxA = 0.03;

        private const double Current400mAMinA = 0.37;
        private const double Current400mAMaxA = 0.43;

        private const int DefaultFpgaReplyBytes = 68;
        private const int DefaultFpgaReplyTimeoutMs = 2000;

        private static readonly byte[] CurrentSet0mAFrame = { 0xAA, 0x55, 0x12, 0x05, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] CurrentSet250mAFrame = { 0xAA, 0x55, 0x12, 0x05, 0x01, 0x00, 0x80, 0x00, 0x80, 0x00, 0x80, 0x00, 0x80, 0x00, 0x80, 0x00, 0x80, 0x00, 0x80, 0x00, 0x80 };
        private static readonly byte[] CurrentSet400mAFrame = { 0xAA, 0x55, 0x12, 0x05, 0x01, 0xCC, 0xCC, 0xCC, 0xCC, 0xCC, 0xCC, 0xCC, 0xCC, 0xCC, 0xCC, 0xCC, 0xCC, 0xCC, 0xCC, 0xCC, 0xCC };
        private static readonly byte[] CurrentStopFrame = { 0xAA, 0x55, 0x12, 0x05, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };

        private static readonly byte[] CurrentTelemetryQueryFrame = { 0xAA, 0x55, 0x02, 0x06, 0x01 };

        private readonly SemaphoreSlim _manualTestLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _autoTestLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _opLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _instrumentLock = new SemaphoreSlim(1, 1);

        private CancellationTokenSource _autoTestCts;

        private FpgaTcpClient _fpga;
        private IDmmApi _dmmSocket;

        private IPowerSupplyApi _powerSupply28V;
        private IPowerSupplyApi _powerSupply3V3;

        private bool _isPowerOn;
        private string _powerStatus = "未上电";

        private bool _isMatrixRouted;
        private bool _matrixRoutedSig;
        private bool _matrixRoutedDmm;

        private bool _isManualTestRunning;
        private bool _isAutoTestRunning;
        private bool _isBusy;

        private string _fpgaIpAddress = DefaultFpgaIpAddress;
        private int _fpgaPort = DefaultFpgaPort;

        private string _step3Voltage = "--";
        private string _step4Current = "--";

        private string _step7Voltage = "--";
        private string _step8Current = "--";

        private bool _isMeasuringStep3;
        private bool _isMeasuringStep7;

        private string _lastTestTime = "--";
        private string _overallResult = "--";

        public B_C_7_5_6_1ViewModel()
        {
            ManualTestCommand = new DelegateCommand(OnManualTest, () => !IsAutoTestRunning && (!IsBusy || IsManualTestRunning));
            AutoTestCommand = new DelegateCommand(OnAutoTest, () => !IsManualTestRunning && (!IsBusy || IsAutoTestRunning));
            ClearLogCommand = new DelegateCommand(() => Logs.Clear());

            Step2Set0mACommand = new DelegateCommand(async () => await SendSetCurrentAndUpdateAsync(CurrentSet0mA, isFirst: true));
            Step3Measure0mACommand = new DelegateCommand(async () => await MeasureVoltageAndUpdateAsync(isFirst: true));
            Step4Readback0mACommand = new DelegateCommand(async () => await QueryCurrentReadbackAndUpdateAsync(isFirst: true));

            Step6Set400mACommand = new DelegateCommand(async () => await SendSetCurrentAndUpdateAsync(CurrentSet400mA, isFirst: false));
            Step7Measure400mACommand = new DelegateCommand(async () => await MeasureVoltageAndUpdateAsync(isFirst: false));
            Step8Readback400mACommand = new DelegateCommand(async () => await QueryCurrentReadbackAndUpdateAsync(isFirst: false));
        }

        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();

        public DelegateCommand ManualTestCommand { get; }
        public DelegateCommand AutoTestCommand { get; }
        public DelegateCommand ClearLogCommand { get; }

        public DelegateCommand Step2Set0mACommand { get; }
        public DelegateCommand Step3Measure0mACommand { get; }
        public DelegateCommand Step4Readback0mACommand { get; }

        public DelegateCommand Step6Set400mACommand { get; }
        public DelegateCommand Step7Measure400mACommand { get; }
        public DelegateCommand Step8Readback400mACommand { get; }

        public string FpgaIpAddress
        {
            get => _fpgaIpAddress;
            set => SetProperty(ref _fpgaIpAddress, value);
        }

        public int FpgaPort
        {
            get => _fpgaPort;
            set => SetProperty(ref _fpgaPort, value);
        }

        public bool IsPowerOn
        {
            get => _isPowerOn;
            private set
            {
                if (SetProperty(ref _isPowerOn, value))
                    RaiseAllCanExecuteChanged();
            }
        }

        public string PowerStatus
        {
            get => _powerStatus;
            private set => SetProperty(ref _powerStatus, value);
        }

        public bool IsManualTestRunning
        {
            get => _isManualTestRunning;
            private set
            {
                if (SetProperty(ref _isManualTestRunning, value))
                    RaiseAllCanExecuteChanged();
            }
        }

        public bool IsAutoTestRunning
        {
            get => _isAutoTestRunning;
            private set
            {
                if (SetProperty(ref _isAutoTestRunning, value))
                    RaiseAllCanExecuteChanged();
            }
        }

        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (SetProperty(ref _isBusy, value))
                    RaiseAllCanExecuteChanged();
            }
        }

        public string Step3Voltage { get => _step3Voltage; private set => SetProperty(ref _step3Voltage, value); }
        public string Step4Current { get => _step4Current; private set => SetProperty(ref _step4Current, value); }
        public string Step7Voltage { get => _step7Voltage; private set => SetProperty(ref _step7Voltage, value); }
        public string Step8Current { get => _step8Current; private set => SetProperty(ref _step8Current, value); }

        public bool IsMeasuringStep3 { get => _isMeasuringStep3; private set => SetProperty(ref _isMeasuringStep3, value); }
        public bool IsMeasuringStep7 { get => _isMeasuringStep7; private set => SetProperty(ref _isMeasuringStep7, value); }

        public string LastTestTime { get => _lastTestTime; private set => SetProperty(ref _lastTestTime, value); }
        public string OverallResult { get => _overallResult; private set => SetProperty(ref _overallResult, value); }

        private void RaiseAllCanExecuteChanged()
        {
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                ManualTestCommand?.RaiseCanExecuteChanged();
                AutoTestCommand?.RaiseCanExecuteChanged();
                Step2Set0mACommand?.RaiseCanExecuteChanged();
                Step3Measure0mACommand?.RaiseCanExecuteChanged();
                Step4Readback0mACommand?.RaiseCanExecuteChanged();
                Step6Set400mACommand?.RaiseCanExecuteChanged();
                Step7Measure400mACommand?.RaiseCanExecuteChanged();
                Step8Readback400mACommand?.RaiseCanExecuteChanged();
            });
        }

        private void AddLog(string msg)
        {
            var line = $"[{DateTime.Now:HH:mm:ss.fff}] {msg}";
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                Logs.Add(line);
                while (Logs.Count > 1000) Logs.RemoveAt(0);
            });
        }

        private void ClearResults()
        {
            Step3Voltage = "--";
            Step4Current = "--";
            Step7Voltage = "--";
            Step8Current = "--";
            OverallResult = "--";
            LastTestTime = "--";
            IsMeasuringStep3 = false;
            IsMeasuringStep7 = false;
        }

        private void SetOverall(string result)
        {
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                OverallResult = result;
                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            });
        }

        private bool EnsureManualStepAllowed()
        {
            if (!IsManualTestRunning)
            {
                AddLog("请先点击‘手动测试’进入手动模式");
                return false;
            }

            if (!IsPowerOn)
            {
                AddLog("当前未上电，请先启动手动测试（会自动上电）");
                return false;
            }

            return true;
        }

        private void OnManualTest()
        {
            if (IsManualTestRunning)
            {
                _ = StopManualTestAsync();
                return;
            }

            _ = StartManualTestAsync();
        }

        private async Task StartManualTestAsync()
        {
            await _manualTestLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (IsManualTestRunning)
                    return;

                ClearResults();
                Logs.Clear();

                IsManualTestRunning = true;
                try
                {
                    IsBusy = true;
                    try
                    {
                        await PowerOnAsync(CancellationToken.None).ConfigureAwait(false);
                        await EnsureFpgaConnectedAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                    finally
                    {
                        IsBusy = false;
                    }

                    AddLog("========== 手动测试开始 ==========");
                }
                catch (Exception ex)
                {
                    AddLog($"手动测试初始化失败: {ex.Message}");
                    IsManualTestRunning = false;
                    try { await CleanupInstrumentsAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { if (IsPowerOn) await PowerOffAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                }
            }
            finally
            {
                _manualTestLock.Release();
            }
        }

        private async Task StopManualTestAsync()
        {
            await _manualTestLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!IsManualTestRunning)
                    return;

                AddLog("========== 停止手动测试 ==========");

                try { await CleanupInstrumentsAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                try { if (IsPowerOn) await PowerOffAsync(CancellationToken.None).ConfigureAwait(false); } catch { }

                IsManualTestRunning = false;
            }
            finally
            {
                _manualTestLock.Release();
            }
        }

        private void OnAutoTest()
        {
            if (IsAutoTestRunning)
            {
                try { _autoTestCts?.Cancel(); } catch { }
                return;
            }

            _ = StartAutoTestAsync();
        }

        private async Task StartAutoTestAsync()
        {
            await _autoTestLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (IsAutoTestRunning)
                    return;

                ClearResults();
                IsAutoTestRunning = true;
                _autoTestCts = new CancellationTokenSource();
                var token = _autoTestCts.Token;

                try
                {
                    IsBusy = true;
                    try
                    {
                        await PowerOnAsync(token).ConfigureAwait(false);
                        await EnsureFpgaConnectedAsync(token).ConfigureAwait(false);
                    }
                    finally
                    {
                        IsBusy = false;
                    }

                    AddLog("========== 自动测试开始 ==========");

                    var ok2 = await SendSetCurrentAsync(CurrentSet0mA, token).ConfigureAwait(false);
                    AddLog($"0mA设定指令: {(ok2 ? "PASS" : "FAIL")}");

                    await Task.Delay(500, token).ConfigureAwait(false);
                    var (ok3, v0) = await MeasureVoltageAsync(token).ConfigureAwait(false);
                    var pass3 = UpdateVoltageStepAndGetPass(isFirst: true, ok3, v0);
                    AddLog($"0mA电压判定: {(pass3 ? "PASS" : "FAIL")}");

                    var (ok4, cur0, hex0) = await QueryCurrentTelemetryAsync(token).ConfigureAwait(false);
                    var pass4 = UpdateCurrentStepAndGetPass(isFirst: true, ok4, cur0, hex0);
                    AddLog($"0mA回采电流判定: {(pass4 ? "PASS" : "FAIL")}");

                    var ok6 = await SendSetCurrentAsync(CurrentSet400mA, token).ConfigureAwait(false);
                    AddLog($"400mA设定指令: {(ok6 ? "PASS" : "FAIL")}");

                    await Task.Delay(500, token).ConfigureAwait(false);
                    var (ok7, v1) = await MeasureVoltageAsync(token).ConfigureAwait(false);
                    var pass7 = UpdateVoltageStepAndGetPass(isFirst: false, ok7, v1);
                    AddLog($"400mA电压判定: {(pass7 ? "PASS" : "FAIL")}");

                    var (ok8, cur1, hex1) = await QueryCurrentTelemetryAsync(token).ConfigureAwait(false);
                    var pass8 = UpdateCurrentStepAndGetPass(isFirst: false, ok8, cur1, hex1);
                    AddLog($"400mA回采电流判定: {(pass8 ? "PASS" : "FAIL")}");

                    var overallOk = ok2 && ok6 && pass3 && pass4 && pass7 && pass8;

                    SetOverall(overallOk ? "PASS" : "FAIL");
                    AddLog($"========== 自动测试完成: {(overallOk ? "PASS" : "FAIL")} ==========");
                }
                catch (OperationCanceledException)
                {
                    AddLog("自动测试已取消");
                }
                catch (Exception ex)
                {
                    AddLog($"自动测试异常: {ex.Message}");
                }
                finally
                {
                    try { await CleanupInstrumentsAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { if (IsPowerOn) await PowerOffAsync(CancellationToken.None).ConfigureAwait(false); } catch { }

                    IsAutoTestRunning = false;
                    try { _autoTestCts?.Dispose(); } catch { }
                    _autoTestCts = null;
                }
            }
            finally
            {
                _autoTestLock.Release();
            }
        }

        private async Task SendSetCurrentAndUpdateAsync(int currentmA, bool isFirst)
        {
            if (!EnsureManualStepAllowed())
                return;

            if (!await _opLock.WaitAsync(0).ConfigureAwait(false))
            {
                AddLog("操作进行中，请稍后再试");
                return;
            }

            try
            {
                IsBusy = true;
                try
                {
                    var ok = await SendSetCurrentAsync(currentmA, CancellationToken.None).ConfigureAwait(false);
                    AddLog($"{currentmA}mA设定指令: {(ok ? "PASS" : "FAIL")}");
                }
                finally
                {
                    IsBusy = false;
                }
            }
            finally
            {
                _opLock.Release();
            }
        }

        private async Task MeasureVoltageAndUpdateAsync(bool isFirst)
        {
            if (!EnsureManualStepAllowed())
                return;

            if ((isFirst && IsMeasuringStep3) || (!isFirst && IsMeasuringStep7))
                return;

            if (!await _opLock.WaitAsync(0).ConfigureAwait(false))
            {
                AddLog("操作进行中，请稍后再试");
                return;
            }

            try
            {
                if (isFirst) IsMeasuringStep3 = true; else IsMeasuringStep7 = true;
                try
                {
                    IsBusy = true;
                    try
                    {
                        var (ok, v) = await MeasureVoltageAsync(CancellationToken.None).ConfigureAwait(false);
                        var pass = UpdateVoltageStepAndGetPass(isFirst, ok, v);
                        AddLog($"{(isFirst ? "0mA" : "400mA")}电压判定: {(pass ? "PASS" : "FAIL")}");
                    }
                    finally
                    {
                        IsBusy = false;
                    }
                }
                finally
                {
                    if (isFirst) IsMeasuringStep3 = false; else IsMeasuringStep7 = false;
                }
            }
            finally
            {
                _opLock.Release();
            }
        }

        private async Task QueryCurrentReadbackAndUpdateAsync(bool isFirst)
        {
            if (!EnsureManualStepAllowed())
                return;

            if (!await _opLock.WaitAsync(0).ConfigureAwait(false))
            {
                AddLog("操作进行中，请稍后再试");
                return;
            }

            try
            {
                IsBusy = true;
                try
                {
                    var (ok, currentA, hex) = await QueryCurrentTelemetryAsync(CancellationToken.None).ConfigureAwait(false);
                    var pass = UpdateCurrentStepAndGetPass(isFirst, ok, currentA, hex);
                    AddLog($"{(isFirst ? "0mA" : "400mA")}回采电流判定: {(pass ? "PASS" : "FAIL")}");
                }
                finally
                {
                    IsBusy = false;
                }
            }
            finally
            {
                _opLock.Release();
            }
        }

        private bool UpdateVoltageStepAndGetPass(bool isFirst, bool ok, double? voltage)
        {
            if (!ok || voltage == null)
            {
                if (isFirst) Step3Voltage = "--"; else Step7Voltage = "--";
                return false;
            }

            var v = voltage.Value;
            if (isFirst)
            {
                Step3Voltage = v.ToString("F3", CultureInfo.InvariantCulture);
                return v >= Voltage0mAMin && v <= Voltage0mAMax;
            }

            Step7Voltage = v.ToString("F3", CultureInfo.InvariantCulture);
            return v >= Voltage400mAMin && v <= Voltage400mAMax;
        }

        private bool UpdateCurrentStepAndGetPass(bool isFirst, bool ok, double? currentA, string replyHex)
        {
            _ = replyHex;

            if (!ok || currentA == null)
            {
                if (isFirst) Step4Current = "--"; else Step8Current = "--";
                return false;
            }

            var a = currentA.Value;
            if (isFirst)
            {
                Step4Current = a.ToString("F3", CultureInfo.InvariantCulture);
                return a >= Current0mAMinA && a <= Current0mAMaxA;
            }

            Step8Current = a.ToString("F3", CultureInfo.InvariantCulture);
            return a >= Current400mAMinA && a <= Current400mAMaxA;
        }

        private async Task PowerOnAsync(CancellationToken token)
        {
            AddLog("组件供电：上电中...");

            await EnsurePowerSupply28VConnectedAsync(token).ConfigureAwait(false);
            try
            {
                await _powerSupply28V.ApplyAsync(PowerSupply28VCh1, Power28VVoltage, Power28VCurrentLimit, token).ConfigureAwait(false);
                await _powerSupply28V.ApplyAsync(PowerSupply28VCh2, Power28VVoltage, Power28VCurrentLimit, token).ConfigureAwait(false);
                await _powerSupply28V.SetOutputEnabledAsync(PowerSupply28VCh1, true, token).ConfigureAwait(false);
                await _powerSupply28V.SetOutputEnabledAsync(PowerSupply28VCh2, true, token).ConfigureAwait(false);
                await Task.Delay(300, token).ConfigureAwait(false);
                AddLog($"组件供电：28V 上电 CH1+CH2, IP={PowerSupply28VIpAddress}");
            }
            catch (Exception ex)
            {
                AddLog($"组件供电：28V 上电失败: {ex.Message}");
            }

            await EnsurePowerSupply3V3ConnectedAsync(token).ConfigureAwait(false);
            try
            {
                await _powerSupply3V3.ApplyAsync(PowerSupply3V3Channel, Power3V3Voltage, Power3V3CurrentLimit, token).ConfigureAwait(false);
                await _powerSupply3V3.SetOutputEnabledAsync(PowerSupply3V3Channel, true, token).ConfigureAwait(false);
                await Task.Delay(200, token).ConfigureAwait(false);
                AddLog($"组件供电：3.3V 上电 CH3, IP={PowerSupply3V3IpAddress}");
            }
            catch (Exception ex)
            {
                AddLog($"组件供电：3.3V 上电失败: {ex.Message}");
            }

            Application.Current?.Dispatcher?.Invoke(() =>
            {
                IsPowerOn = true;
                PowerStatus = "已上电";
            });
        }

        private async Task PowerOffAsync(CancellationToken token)
        {
            AddLog("组件供电：下电中...");

            try { await UnrouteMatrixAsync(token).ConfigureAwait(false); } catch { }

            try
            {
                if (_powerSupply3V3 != null)
                    await _powerSupply3V3.SetOutputEnabledAsync(PowerSupply3V3Channel, false, token).ConfigureAwait(false);
            }
            catch { }

            try
            {
                if (_powerSupply28V != null)
                {
                    await _powerSupply28V.SetOutputEnabledAsync(PowerSupply28VCh1, false, token).ConfigureAwait(false);
                    await _powerSupply28V.SetOutputEnabledAsync(PowerSupply28VCh2, false, token).ConfigureAwait(false);
                }
            }
            catch { }

            Application.Current?.Dispatcher?.Invoke(() =>
            {
                IsPowerOn = false;
                PowerStatus = "未上电";
            });
        }

        private async Task EnsurePowerSupply28VConnectedAsync(CancellationToken token)
        {
            if (_powerSupply28V != null && _powerSupply28V.IsConnected)
                return;

            _powerSupply28V ??= new PowerSupplySocketApi();
            await _powerSupply28V.ConnectAsync(PowerSupply28VIpAddress, token).ConfigureAwait(false);
        }

        private async Task EnsurePowerSupply3V3ConnectedAsync(CancellationToken token)
        {
            if (_powerSupply3V3 != null && _powerSupply3V3.IsConnected)
                return;

            _powerSupply3V3 ??= new PowerSupplySocketApi();
            await _powerSupply3V3.ConnectAsync(PowerSupply3V3IpAddress, token).ConfigureAwait(false);
        }

        private async Task EnsureFpgaConnectedAsync(CancellationToken token)
        {
            if (_fpga != null && _fpga.IsConnected)
                return;

            _fpga?.Dispose();
            _fpga = new FpgaTcpClient();
            await _fpga.ConnectAsync(FpgaIpAddress, FpgaPort, token).ConfigureAwait(false);
            AddLog("FPGA连接成功");
        }

        private async Task DisconnectFpgaAsync(CancellationToken token)
        {
            try
            {
                if (_fpga != null && _fpga.IsConnected)
                {
                    AddLog($"FPGA停止输出: 发送 {FormatData(CurrentStopFrame)}");
                    await _fpga.WriteAsync(CurrentStopFrame, 0, CurrentStopFrame.Length, token).ConfigureAwait(false);
                }
            }
            catch { }

            try { _fpga?.Disconnect(); } catch { }
            try { _fpga?.Dispose(); } catch { }
            _fpga = null;
        }

        private async Task<bool> SendSetCurrentAsync(int currentmA, CancellationToken token)
        {
            try
            {
                await EnsureFpgaConnectedAsync(token).ConfigureAwait(false);

                var cmd = BuildCurrentSetCommand(currentmA);
                AddLog($"电流设定: {currentmA}mA -> FPGA {FormatData(cmd)}");
                await _fpga.WriteAsync(cmd, 0, cmd.Length, token).ConfigureAwait(false);
                return true;
            }
            catch (Exception ex)
            {
                AddLog($"电流设定发送失败: {ex.Message}");
                return false;
            }
        }

        private async Task<(bool Ok, double? CurrentA, string ReplyHex)> QueryCurrentTelemetryAsync(CancellationToken token)
        {
            try
            {
                await EnsureFpgaConnectedAsync(token).ConfigureAwait(false);

                AddLog($"回采查询: FPGA发送 {FormatData(CurrentTelemetryQueryFrame)}");
                var reply = await _fpga.RequestReplyAsync(CurrentTelemetryQueryFrame, DefaultFpgaReplyBytes, DefaultFpgaReplyTimeoutMs, token)
                    .ConfigureAwait(false);

                var hex = FormatData(reply);
                var currentA = TryParseCurrentAFromReply(reply);
                AddLog($"回采接收: {hex}");
                return (true, currentA, hex);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                AddLog($"回采接收失败: {ex.Message}");
                return (false, null, null);
            }
        }

        private async Task<(bool Ok, double? Voltage)> MeasureVoltageAsync(CancellationToken token)
        {
            await _instrumentLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                var matrixOk = await RouteMatrixAsync(token).ConfigureAwait(false);
                if (!matrixOk)
                    return (false, null);

                await Task.Delay(200, token).ConfigureAwait(false);

                var reading = await DmmReadVoltageAsync(token).ConfigureAwait(false);
                if (reading?.Value == null)
                    return (false, null);

                return (true, reading.Value.Value);
            }
            catch (Exception ex)
            {
                AddLog($"电压测量异常: {ex.Message}");
                return (false, null);
            }
            finally
            {
                try { await UnrouteMatrixAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                _instrumentLock.Release();
            }
        }

        private async Task<DmmReading> DmmReadVoltageAsync(CancellationToken token)
        {
            if (_dmmSocket == null)
                _dmmSocket = new DmmSocketApi();

            if (!_dmmSocket.IsConnected)
                await _dmmSocket.ConnectAsync(DmmIpAddress, token).ConfigureAwait(false);

            return await _dmmSocket.ReadOnceAsync(DmmMeasureMode.DCV, new DmmReadOptions { TimeoutMilliseconds = DmmTimeoutMs }, token)
                .ConfigureAwait(false);
        }

        private async Task<bool> RouteMatrixAsync(CancellationToken token)
        {
            var matrix = MatrixControlService.Instance;

            if (_isMatrixRouted)
                await UnrouteMatrixAsync(token).ConfigureAwait(false);

            AddLog($"矩阵路由: slot{MatrixSlotDmm} {MatrixDmmH.In}-{MatrixDmmH.Out} + slot{MatrixSlotSig} {MatrixPointSig.In}-{MatrixPointSig.Out}");

            _matrixRoutedDmm = await matrix.ConnectNodesAsync(MatrixDmmH.In, MatrixDmmH.Out, MatrixDmmH.Slot, MatrixIpAddress, MatrixTcpBasePort)
                .ConfigureAwait(false);
            _matrixRoutedSig = await matrix.ConnectNodesAsync(MatrixPointSig.In, MatrixPointSig.Out, MatrixPointSig.Slot, MatrixIpAddress, MatrixTcpBasePort)
                .ConfigureAwait(false);
            _isMatrixRouted = _matrixRoutedDmm && _matrixRoutedSig;

            AddLog($"矩阵路由结果: DMM={(_matrixRoutedDmm ? "OK" : "FAIL")}, SIG={(_matrixRoutedSig ? "OK" : "FAIL")}");
            if (!_isMatrixRouted)
            {
                await UnrouteMatrixAsync(token).ConfigureAwait(false);
                return false;
            }

            return true;
        }

        private async Task UnrouteMatrixAsync(CancellationToken token)
        {
            if (!_isMatrixRouted && !_matrixRoutedDmm && !_matrixRoutedSig)
                return;

            var matrix = MatrixControlService.Instance;
            try
            {
                if (_matrixRoutedDmm)
                    _ = await matrix.DisconnectNodesAsync(MatrixDmmH.In, MatrixDmmH.Out, MatrixDmmH.Slot, MatrixIpAddress, MatrixTcpBasePort)
                        .ConfigureAwait(false);
            }
            catch { }

            try
            {
                if (_matrixRoutedSig)
                    _ = await matrix.DisconnectNodesAsync(MatrixPointSig.In, MatrixPointSig.Out, MatrixPointSig.Slot, MatrixIpAddress, MatrixTcpBasePort)
                        .ConfigureAwait(false);
            }
            catch { }

            _isMatrixRouted = false;
            _matrixRoutedDmm = false;
            _matrixRoutedSig = false;
        }

        private async Task CleanupInstrumentsAsync(CancellationToken token)
        {
            try { await UnrouteMatrixAsync(token).ConfigureAwait(false); } catch { }

            try
            {
                if (_dmmSocket != null)
                {
                    try { if (_dmmSocket.IsConnected) await _dmmSocket.DisconnectAsync(token).ConfigureAwait(false); } catch { }
                    _dmmSocket = null;
                }
            }
            catch { }

            try { await DisconnectFpgaAsync(token).ConfigureAwait(false); } catch { }
        }

        private static byte[] BuildCurrentSetCommand(int currentmA)
        {
            if (currentmA <= 0)
                return CurrentSet0mAFrame;
            if (currentmA == 250)
                return CurrentSet250mAFrame;
            if (currentmA == 400)
                return CurrentSet400mAFrame;
            return CurrentSet0mAFrame;
        }

        private static double? TryParseCurrentAFromReply(byte[] reply)
        {
            if (reply == null || reply.Length < 7)
                return null;

            var dataLen = reply[2];
            if (dataLen < 2)
                return null;

            var high = reply[4];
            var low = reply[5];
            var mA = (high << 8) | low;
            return mA / 1000.0;
        }

        private static string FormatData(byte[] data)
        {
            if (data == null) return "";
            return string.Join(" ", data.Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));
        }

        public void Dispose()
        {
            try { _autoTestCts?.Cancel(); } catch { }
            try { _autoTestCts?.Dispose(); } catch { }
            _autoTestCts = null;

            try { _fpga?.Disconnect(); } catch { }
            try { _fpga?.Dispose(); } catch { }
            _fpga = null;

            try { _ = _dmmSocket?.DisconnectAsync(CancellationToken.None); } catch { }
            _dmmSocket = null;

            try { _ = _powerSupply28V?.DisconnectAsync(CancellationToken.None); } catch { }
            try { _ = _powerSupply3V3?.DisconnectAsync(CancellationToken.None); } catch { }
            _powerSupply28V = null;
            _powerSupply3V3 = null;

            try { _manualTestLock?.Dispose(); } catch { }
            try { _autoTestLock?.Dispose(); } catch { }
            try { _opLock?.Dispose(); } catch { }
            try { _instrumentLock?.Dispose(); } catch { }
        }
    }
}
