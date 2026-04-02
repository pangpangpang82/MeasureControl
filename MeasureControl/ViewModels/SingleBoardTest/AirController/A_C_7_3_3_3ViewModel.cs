using Prism.Commands;
using Prism.Mvvm;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using MeasureControl.Helpers;
using MeasureControl.Services;
using MeasureControl.Services.HardwareApis;

namespace MeasureControl.ViewModels.SingleBoardTest.AirController
{
    public sealed class A_C_7_3_3_3ViewModel : BindableBase, IDisposable
    {
        private const string DefaultFpgaIpAddress = "192.168.1.10";
        private const int DefaultFpgaPort = 5001;

        private const string MatrixIpAddress = "192.168.1.3";
        private const int MatrixTcpBasePort = 50200;

        private const int MatrixSlotSig = 6;
        private const int MatrixSlotDmm = 4;
        private static readonly (string In, string Out, int Slot) MatrixPointJ216J217 = ("I1", "O8", MatrixSlotSig);
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

        private static readonly byte[] DeviceInitCommandFrame = { 0xAA, 0x55, 0x02, 0x02, 0x01 };
        private static readonly byte[] PhHighCommandFrame = { 0xAA, 0x55, 0x0A, 0x03, 0x03, 0xE8, 0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] PhLowCommandFrame = { 0xAA, 0x55, 0x0A, 0x03, 0x02, 0xE8, 0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] ResetToInitialCommandFrame = { 0xAA, 0x55, 0x0A, 0x03, 0x00, 0xE8, 0x03, 0x00, 0x00, 0xE8, 0x03, 0x00, 0x00 };

        private const double VoltageMin = 17.0;
        private const double VoltageMax = 32.0;

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

        private string _step1Result = "--";
        private string _step2Voltage = "--";
        private string _step2Result = "--";
        private string _step3Result = "--";
        private string _step4Voltage = "--";
        private string _step4Result = "--";

        private bool _isMeasuringStep2;
        private bool _isMeasuringStep4;

        private string _lastTestTime = "--";
        private string _overallResult = "--";

        public A_C_7_3_3_3ViewModel()
        {
            ManualTestCommand = new DelegateCommand(OnManualTest, () => !IsAutoTestRunning && (!IsBusy || IsManualTestRunning));
            AutoTestCommand = new DelegateCommand(OnAutoTest, () => !IsManualTestRunning && (!IsBusy || IsAutoTestRunning));
            ClearLogCommand = new DelegateCommand(() => Logs.Clear());

            Step1SendFrame1Command = new DelegateCommand(async () => await SendTestCommandAndUpdateAsync(isFrame1: true));
            Step2MeasureVoltage1Command = new DelegateCommand(async () => await MeasureVoltageAndUpdateAsync(isFirst: true));
            Step3SendFrame2Command = new DelegateCommand(async () => await SendTestCommandAndUpdateAsync(isFrame1: false));
            Step4MeasureVoltage2Command = new DelegateCommand(async () => await MeasureVoltageAndUpdateAsync(isFirst: false));
        }

        private bool EnsureManualStepAllowed()
        {
            if (IsAutoTestRunning)
            {
                AddLog("自动测试运行中：手动步骤不可操作");
                return false;
            }

            if (!IsManualTestRunning)
            {
                AddLog("请先点击【手动测试】完成上电与连接");
                return false;
            }

            if (!IsPowerOn)
            {
                AddLog("未上电：请先点击【手动测试】");
                return false;
            }

            return true;
        }

        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();

        public DelegateCommand ManualTestCommand { get; }
        public DelegateCommand AutoTestCommand { get; }
        public DelegateCommand ClearLogCommand { get; }

        public DelegateCommand Step1SendFrame1Command { get; }
        public DelegateCommand Step2MeasureVoltage1Command { get; }
        public DelegateCommand Step3SendFrame2Command { get; }
        public DelegateCommand Step4MeasureVoltage2Command { get; }

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
                {
                    RaiseAllCanExecuteChanged();
                }
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
                {
                    RaiseAllCanExecuteChanged();
                }
            }
        }

        public bool IsAutoTestRunning
        {
            get => _isAutoTestRunning;
            private set
            {
                if (SetProperty(ref _isAutoTestRunning, value))
                {
                    RaiseAllCanExecuteChanged();
                }
            }
        }

        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (SetProperty(ref _isBusy, value))
                {
                    RaiseAllCanExecuteChanged();
                }
            }
        }

        public string Step1Result { get => _step1Result; private set => SetProperty(ref _step1Result, value); }
        public string Step2Voltage { get => _step2Voltage; private set => SetProperty(ref _step2Voltage, value); }
        public string Step2Result { get => _step2Result; private set => SetProperty(ref _step2Result, value); }
        public string Step3Result { get => _step3Result; private set => SetProperty(ref _step3Result, value); }
        public string Step4Voltage { get => _step4Voltage; private set => SetProperty(ref _step4Voltage, value); }
        public string Step4Result { get => _step4Result; private set => SetProperty(ref _step4Result, value); }

        public bool IsMeasuringStep2 { get => _isMeasuringStep2; private set => SetProperty(ref _isMeasuringStep2, value); }
        public bool IsMeasuringStep4 { get => _isMeasuringStep4; private set => SetProperty(ref _isMeasuringStep4, value); }

        public string LastTestTime { get => _lastTestTime; private set => SetProperty(ref _lastTestTime, value); }
        public string OverallResult { get => _overallResult; private set => SetProperty(ref _overallResult, value); }

        private void RaiseAllCanExecuteChanged()
        {
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                ManualTestCommand?.RaiseCanExecuteChanged();
                AutoTestCommand?.RaiseCanExecuteChanged();

                Step1SendFrame1Command?.RaiseCanExecuteChanged();
                Step2MeasureVoltage1Command?.RaiseCanExecuteChanged();
                Step3SendFrame2Command?.RaiseCanExecuteChanged();
                Step4MeasureVoltage2Command?.RaiseCanExecuteChanged();
            });
        }

        private void AddLog(string msg)
        {
            var line = $"[{DateTime.Now:HH:mm:ss.fff}] {msg}";
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                Logs.Add(line);
                while (Logs.Count > 500)
                    Logs.RemoveAt(0);
            });
        }

        private void ClearResults()
        {
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                Step1Result = "--";
                Step2Voltage = "--";
                Step2Result = "--";
                Step3Result = "--";
                Step4Voltage = "--";
                Step4Result = "--";
                LastTestTime = "--";
                OverallResult = "--";
            });
        }

        private void UpdateOverallIfComplete()
        {
            var allDone = new[] { Step1Result, Step2Result, Step3Result, Step4Result }.All(v => v == "PASS" || v == "FAIL");
            if (!allDone)
                return;

            var pass = new[] { Step1Result, Step2Result, Step3Result, Step4Result }.All(v => v == "PASS");
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                OverallResult = pass ? "PASS" : "FAIL";
                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            });
        }

        private void TryFinalizeManualTestIfComplete()
        {
            if (!IsManualTestRunning)
                return;

            var allDone = new[] { Step1Result, Step2Result, Step3Result, Step4Result }.All(v => v == "PASS" || v == "FAIL");
            if (!allDone)
                return;

            _ = CompleteManualTestAsync();
        }

        private async Task CompleteManualTestAsync()
        {
            await _manualTestLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!IsManualTestRunning)
                    return;

                var allDone = new[] { Step1Result, Step2Result, Step3Result, Step4Result }.All(v => v == "PASS" || v == "FAIL");
                if (!allDone)
                    return;

                AddLog($"========== 手动测试完成: {OverallResult} ==========");

                await StopManualTestCoreAsync(addStoppedLog: false).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AddLog($"手动测试收尾异常: {ex.Message}");
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    IsManualTestRunning = false;
                    IsBusy = false;
                });
            }
            finally
            {
                _manualTestLock.Release();
            }
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
                IsManualTestRunning = true;
                AddLog("========== 手动测试开始 ==========");

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
            }
            catch (Exception ex)
            {
                AddLog($"手动测试启动异常: {ex.Message}");
            }
            finally
            {
                _manualTestLock.Release();
            }
        }

        private async Task StopManualTestCoreAsync(bool addStoppedLog)
        {
            Application.Current?.Dispatcher?.Invoke(() => { IsBusy = true; });
            try
            {
                await CleanupInstrumentsAsync(CancellationToken.None).ConfigureAwait(false);
                if (IsPowerOn)
                    await PowerOffAsync(CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    IsManualTestRunning = false;
                    IsBusy = false;
                });

                if (addStoppedLog)
                    AddLog("========== 手动测试已停止 ==========");
            }
        }

        private async Task StopManualTestAsync()
        {
            await _manualTestLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!IsManualTestRunning)
                    return;

                Application.Current?.Dispatcher?.Invoke(() => { IsManualTestRunning = false; });

                try
                {
                    await StopManualTestCoreAsync(addStoppedLog: true).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    AddLog($"停止手动测试异常: {ex.Message}");
                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        IsManualTestRunning = false;
                        IsBusy = false;
                    });
                }
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
                _autoTestCts?.Cancel();
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

                    var ok1 = await SendTestCommandAsync(isPhHigh: true, token).ConfigureAwait(false);
                    Step1Result = ok1 ? "PASS" : "FAIL";

                    await Task.Delay(500, token).ConfigureAwait(false);
                    var (ok2, v1) = await MeasureVoltageAsync(keepRouted: true, token).ConfigureAwait(false);
                    UpdateVoltageStep(isFirst: true, ok2, v1);

                    var ok3 = await SendTestCommandAsync(isPhHigh: false, token).ConfigureAwait(false);
                    Step3Result = ok3 ? "PASS" : "FAIL";

                    await Task.Delay(500, token).ConfigureAwait(false);
                    var (ok4, v2) = await MeasureVoltageAsync(keepRouted: true, token).ConfigureAwait(false);
                    UpdateVoltageStep(isFirst: false, ok4, v2);

                    UpdateOverallIfComplete();

                    AddLog($"========== 自动测试完成: {OverallResult} ==========");
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

        private async Task SendTestCommandAndUpdateAsync(bool isFrame1)
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
                    var ok = await SendTestCommandAsync(isPhHigh: isFrame1, CancellationToken.None).ConfigureAwait(false);
                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        if (isFrame1)
                            Step1Result = ok ? "PASS" : "FAIL";
                        else
                            Step3Result = ok ? "PASS" : "FAIL";
                    });

                    UpdateOverallIfComplete();
                    TryFinalizeManualTestIfComplete();
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

            if ((isFirst && IsMeasuringStep2) || (!isFirst && IsMeasuringStep4))
                return;

            if (!await _opLock.WaitAsync(0).ConfigureAwait(false))
            {
                AddLog("操作进行中，请稍后再试");
                return;
            }

            try
            {
                if (isFirst) IsMeasuringStep2 = true; else IsMeasuringStep4 = true;
                try
                {
                    IsBusy = true;
                    try
                    {
                        var (ok, v) = await MeasureVoltageAsync(keepRouted: true, CancellationToken.None).ConfigureAwait(false);
                        UpdateVoltageStep(isFirst, ok, v);
                        UpdateOverallIfComplete();
                        TryFinalizeManualTestIfComplete();
                    }
                    finally
                    {
                        IsBusy = false;
                    }
                }
                finally
                {
                    if (isFirst) IsMeasuringStep2 = false; else IsMeasuringStep4 = false;
                }
            }
            finally
            {
                _opLock.Release();
            }
        }

        private void UpdateVoltageStep(bool isFirst, bool ok, double? voltage)
        {
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                var absV = voltage.HasValue ? (double?)Math.Abs(voltage.Value) : null;
                var vText = voltage.HasValue ? $"{voltage.Value:F3} V" : "--";

                var polarityOk = voltage.HasValue && (isFirst ? voltage.Value > 0 : voltage.Value < 0);
                var magnitudeOk = absV.HasValue && absV.Value >= VoltageMin && absV.Value <= VoltageMax;
                var pass = ok && polarityOk && magnitudeOk;
                var r = pass ? "PASS" : "FAIL";

                if (isFirst)
                {
                    Step2Voltage = vText;
                    Step2Result = r;
                }
                else
                {
                    Step4Voltage = vText;
                    Step4Result = r;
                }
            });
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

            try { await CleanupInstrumentsAsync(token).ConfigureAwait(false); } catch { }

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
            try { await TryResetFpgaToInitialAsync(token).ConfigureAwait(false); } catch { }

            try { _fpga?.Disconnect(); } catch { }
            try { _fpga?.Dispose(); } catch { }
            _fpga = null;
        }

        private async Task TryResetFpgaToInitialAsync(CancellationToken token)
        {
            try
            {
                if (_fpga == null || !_fpga.IsConnected)
                    return;

                AddLog($"FPGA复位指令(恢复初始状态): {FormatData(ResetToInitialCommandFrame)}");
                await _fpga.WriteAsync(ResetToInitialCommandFrame, 0, ResetToInitialCommandFrame.Length, token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AddLog($"FPGA复位失败: {ex.Message}");
            }
        }

        private async Task<bool> SendTestCommandAsync(bool isPhHigh, CancellationToken token)
        {
            try
            {
                await EnsureFpgaConnectedAsync(token).ConfigureAwait(false);

                if (isPhHigh)
                {
                    AddLog($"FPGA发送指令(设备初始化): {FormatData(DeviceInitCommandFrame)}");
                    await _fpga.WriteAsync(DeviceInitCommandFrame, 0, DeviceInitCommandFrame.Length, token).ConfigureAwait(false);

                    AddLog($"FPGA发送指令(PH高电平): {FormatData(PhHighCommandFrame)}");
                    await _fpga.WriteAsync(PhHighCommandFrame, 0, PhHighCommandFrame.Length, token).ConfigureAwait(false);
                }
                else
                {
                    AddLog($"FPGA发送指令(PH低电平): {FormatData(PhLowCommandFrame)}");
                    await _fpga.WriteAsync(PhLowCommandFrame, 0, PhLowCommandFrame.Length, token).ConfigureAwait(false);
                }

                return true;
            }
            catch (Exception ex)
            {
                AddLog($"FPGA发送失败: {ex.Message}");
                return false;
            }
        }

        private async Task<(bool Ok, double? Voltage)> MeasureVoltageAsync(bool keepRouted, CancellationToken token)
        {
            await _instrumentLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                var matrixOk = await EnsureMatrixRoutedAsync(token).ConfigureAwait(false);
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
                if (!keepRouted)
                {
                    try { await UnrouteMatrixAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                }

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

        private async Task<bool> EnsureMatrixRoutedAsync(CancellationToken token)
        {
            if (_isMatrixRouted)
                return true;

            var matrix = MatrixControlService.Instance;

            AddLog($"矩阵路由: slot{MatrixSlotDmm} {MatrixDmmH.In}-{MatrixDmmH.Out} + slot{MatrixSlotSig} {MatrixPointJ216J217.In}-{MatrixPointJ216J217.Out}");

            _matrixRoutedDmm = await matrix.ConnectNodesAsync(MatrixDmmH.In, MatrixDmmH.Out, MatrixDmmH.Slot, MatrixIpAddress, MatrixTcpBasePort).ConfigureAwait(false);
            _matrixRoutedSig = await matrix.ConnectNodesAsync(MatrixPointJ216J217.In, MatrixPointJ216J217.Out, MatrixPointJ216J217.Slot, MatrixIpAddress, MatrixTcpBasePort).ConfigureAwait(false);
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
                    _ = await matrix.DisconnectNodesAsync(MatrixDmmH.In, MatrixDmmH.Out, MatrixDmmH.Slot, MatrixIpAddress, MatrixTcpBasePort).ConfigureAwait(false);
            }
            catch { }

            try
            {
                if (_matrixRoutedSig)
                    _ = await matrix.DisconnectNodesAsync(MatrixPointJ216J217.In, MatrixPointJ216J217.Out, MatrixPointJ216J217.Slot, MatrixIpAddress, MatrixTcpBasePort).ConfigureAwait(false);
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

        private static string FormatData(byte[] data)
        {
            if (data == null || data.Length == 0)
                return string.Empty;
            return string.Join(" ", data.Select(b => b.ToString("X2")));
        }

        public void Dispose()
        {
            try { _autoTestCts?.Cancel(); } catch { }
            try { _autoTestCts?.Dispose(); } catch { }
            _autoTestCts = null;

            try { CleanupInstrumentsAsync(CancellationToken.None).GetAwaiter().GetResult(); } catch { }
            try { if (IsPowerOn) PowerOffAsync(CancellationToken.None).GetAwaiter().GetResult(); } catch { }

            try { _powerSupply28V?.DisposeAsync().AsTask().GetAwaiter().GetResult(); } catch { }
            try { _powerSupply3V3?.DisposeAsync().AsTask().GetAwaiter().GetResult(); } catch { }

            try { _manualTestLock.Dispose(); } catch { }
            try { _autoTestLock.Dispose(); } catch { }
            try { _opLock.Dispose(); } catch { }
            try { _instrumentLock.Dispose(); } catch { }
        }
    }
}
