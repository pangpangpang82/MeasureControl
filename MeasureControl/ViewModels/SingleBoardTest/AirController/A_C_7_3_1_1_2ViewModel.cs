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
using MeasureControl.Models.Devices;
using MeasureControl.Services;
using MeasureControl.Services.HardwareApis;

namespace MeasureControl.ViewModels.SingleBoardTest.AirController
{
    public sealed class A_C_7_3_1_1_2ViewModel : BindableBase, IDisposable
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

        private static readonly byte[] TestCommandFrame = { 0x0A, 0x55, 0x02, 0x0B, 0x07 };

        private const double Load50Ohm = 50.0;
        private const double Load12Ohm = 12.0;
        private const double LoadToleranceOhm = 1.0;

        private const double VoltageMin = 17.0;
        private const double VoltageMax = 32.0;

        private readonly SemaphoreSlim _manualTestLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _autoTestLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _opLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _instrumentLock = new SemaphoreSlim(1, 1);

        private CancellationTokenSource _autoTestCts;

        private FpgaTcpClient _fpga;
        private ACTS6010Driver _resistorDriver;
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

        private string _step1LoadReadback = "--";
        private string _step1Result = "--";
        private string _step2Result = "--";
        private string _step3Voltage = "--";
        private string _step3Result = "--";
        private string _step4LoadReadback = "--";
        private string _step4Result = "--";
        private string _step5Result = "--";
        private string _step6Voltage = "--";
        private string _step6Result = "--";

        private bool _isMeasuringStep3;
        private bool _isMeasuringStep6;

        private string _lastTestTime = "--";
        private string _overallResult = "--";

        public A_C_7_3_1_1_2ViewModel()
        {
            ManualTestCommand = new DelegateCommand(OnManualTest, () => !IsAutoTestRunning && (!IsBusy || IsManualTestRunning));
            AutoTestCommand = new DelegateCommand(OnAutoTest, () => !IsManualTestRunning && (!IsBusy || IsAutoTestRunning));
            ClearLogCommand = new DelegateCommand(() => Logs.Clear());

            Step1Connect50OhmCommand = new DelegateCommand(async () => await ConnectLoadAndUpdateAsync(Load50Ohm, isStep1: true));
            Step2SendCommand = new DelegateCommand(async () => await SendTestCommandAndUpdateAsync(isStep2: true));
            Step3MeasureVoltageCommand = new DelegateCommand(async () => await MeasureVoltageAndUpdateAsync(isStep3: true));

            Step4Connect12OhmCommand = new DelegateCommand(async () => await ConnectLoadAndUpdateAsync(Load12Ohm, isStep1: false));
            Step5SendCommand = new DelegateCommand(async () => await SendTestCommandAndUpdateAsync(isStep2: false));
            Step6MeasureVoltageCommand = new DelegateCommand(async () => await MeasureVoltageAndUpdateAsync(isStep3: false));
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

        public DelegateCommand Step1Connect50OhmCommand { get; }
        public DelegateCommand Step2SendCommand { get; }
        public DelegateCommand Step3MeasureVoltageCommand { get; }
        public DelegateCommand Step4Connect12OhmCommand { get; }
        public DelegateCommand Step5SendCommand { get; }
        public DelegateCommand Step6MeasureVoltageCommand { get; }

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
                    RaisePropertyChanged(nameof(CanOperateSteps));
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
                    RaisePropertyChanged(nameof(CanOperateSteps));
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
                    RaisePropertyChanged(nameof(CanOperateSteps));
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
                    RaisePropertyChanged(nameof(CanOperateSteps));
                    RaiseAllCanExecuteChanged();
                }
            }
        }

        public bool CanOperateSteps => IsManualTestRunning && IsPowerOn && !IsBusy;

        public string Step1LoadReadback { get => _step1LoadReadback; private set => SetProperty(ref _step1LoadReadback, value); }
        public string Step1Result { get => _step1Result; private set => SetProperty(ref _step1Result, value); }
        public string Step2Result { get => _step2Result; private set => SetProperty(ref _step2Result, value); }
        public string Step3Voltage { get => _step3Voltage; private set => SetProperty(ref _step3Voltage, value); }
        public string Step3Result { get => _step3Result; private set => SetProperty(ref _step3Result, value); }
        public string Step4LoadReadback { get => _step4LoadReadback; private set => SetProperty(ref _step4LoadReadback, value); }
        public string Step4Result { get => _step4Result; private set => SetProperty(ref _step4Result, value); }
        public string Step5Result { get => _step5Result; private set => SetProperty(ref _step5Result, value); }
        public string Step6Voltage { get => _step6Voltage; private set => SetProperty(ref _step6Voltage, value); }
        public string Step6Result { get => _step6Result; private set => SetProperty(ref _step6Result, value); }

        public bool IsMeasuringStep3 { get => _isMeasuringStep3; private set => SetProperty(ref _isMeasuringStep3, value); }
        public bool IsMeasuringStep6 { get => _isMeasuringStep6; private set => SetProperty(ref _isMeasuringStep6, value); }

        public string LastTestTime { get => _lastTestTime; private set => SetProperty(ref _lastTestTime, value); }
        public string OverallResult { get => _overallResult; private set => SetProperty(ref _overallResult, value); }

        private void RaiseAllCanExecuteChanged()
        {
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                ManualTestCommand?.RaiseCanExecuteChanged();
                AutoTestCommand?.RaiseCanExecuteChanged();

                Step1Connect50OhmCommand?.RaiseCanExecuteChanged();
                Step2SendCommand?.RaiseCanExecuteChanged();
                Step3MeasureVoltageCommand?.RaiseCanExecuteChanged();
                Step4Connect12OhmCommand?.RaiseCanExecuteChanged();
                Step5SendCommand?.RaiseCanExecuteChanged();
                Step6MeasureVoltageCommand?.RaiseCanExecuteChanged();
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

        private void OnManualTest()
        {
            if (IsManualTestRunning)
            {
                _ = StopManualTestAsync();
                return;
            }

            _ = StartManualTestAsync();
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

                    var ok1 = await ConnectLoadAsync(Load50Ohm, token).ConfigureAwait(false);
                    UpdateLoadStep(is50: true, ok1);

                    var ok2 = await SendTestCommandAsync(token).ConfigureAwait(false);
                    UpdateSendStep(isFirst: true, ok2);

                    await Task.Delay(500, token).ConfigureAwait(false);
                    var (ok3, v1) = await MeasureVoltageAsync(token).ConfigureAwait(false);
                    UpdateVoltageStep(isFirst: true, ok3, v1);

                    var ok4 = await ConnectLoadAsync(Load12Ohm, token).ConfigureAwait(false);
                    UpdateLoadStep(is50: false, ok4);

                    var ok5 = await SendTestCommandAsync(token).ConfigureAwait(false);
                    UpdateSendStep(isFirst: false, ok5);

                    await Task.Delay(500, token).ConfigureAwait(false);
                    var (ok6, v2) = await MeasureVoltageAsync(token).ConfigureAwait(false);
                    UpdateVoltageStep(isFirst: false, ok6, v2);

                    var overallOk = new[] { Step1Result, Step2Result, Step3Result, Step4Result, Step5Result, Step6Result }.All(r => r == "PASS");
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

        private void ClearResults()
        {
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                Step1LoadReadback = "--";
                Step1Result = "--";
                Step2Result = "--";
                Step3Voltage = "--";
                Step3Result = "--";
                Step4LoadReadback = "--";
                Step4Result = "--";
                Step5Result = "--";
                Step6Voltage = "--";
                Step6Result = "--";
                LastTestTime = "--";
                OverallResult = "--";
            });
        }

        private void SetOverall(string result)
        {
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                OverallResult = result;
                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            });
        }

        private async Task ConnectLoadAndUpdateAsync(double ohm, bool isStep1)
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
                    var ok = await ConnectLoadAsync(ohm, CancellationToken.None).ConfigureAwait(false);
                    UpdateLoadStep(is50: isStep1, ok);
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

        private void UpdateLoadStep(bool is50, bool ok)
        {
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                var result = ok ? "PASS" : "FAIL";
                if (is50)
                {
                    Step1Result = result;
                }
                else
                {
                    Step4Result = result;
                }
            });
        }

        private async Task SendTestCommandAndUpdateAsync(bool isStep2)
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
                    var ok = await SendTestCommandAsync(CancellationToken.None).ConfigureAwait(false);
                    UpdateSendStep(isFirst: isStep2, ok);
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

        private void UpdateSendStep(bool isFirst, bool ok)
        {
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                var result = ok ? "PASS" : "FAIL";
                if (isFirst)
                    Step2Result = result;
                else
                    Step5Result = result;
            });
        }

        private async Task MeasureVoltageAndUpdateAsync(bool isStep3)
        {
            if (!EnsureManualStepAllowed())
                return;

            if ((isStep3 && IsMeasuringStep3) || (!isStep3 && IsMeasuringStep6))
                return;

            if (!await _opLock.WaitAsync(0).ConfigureAwait(false))
            {
                AddLog("操作进行中，请稍后再试");
                return;
            }

            try
            {
                if (isStep3) IsMeasuringStep3 = true; else IsMeasuringStep6 = true;
                try
                {
                    IsBusy = true;
                    try
                    {
                        var (ok, v) = await MeasureVoltageAsync(CancellationToken.None).ConfigureAwait(false);
                        UpdateVoltageStep(isFirst: isStep3, ok, v);
                    }
                    finally
                    {
                        IsBusy = false;
                    }
                }
                finally
                {
                    if (isStep3) IsMeasuringStep3 = false; else IsMeasuringStep6 = false;
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
                var vText = absV.HasValue ? $"{absV.Value:F3} V" : "--";
                var pass = ok && absV.HasValue && absV.Value >= VoltageMin && absV.Value <= VoltageMax;
                var r = pass ? "PASS" : "FAIL";

                if (isFirst)
                {
                    Step3Voltage = vText;
                    Step3Result = r;
                }
                else
                {
                    Step6Voltage = vText;
                    Step6Result = r;
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

        private Task DisconnectFpgaAsync(CancellationToken token)
        {
            _ = token;
            try
            {
                _fpga?.Dispose();
            }
            catch { }
            _fpga = null;
            return Task.CompletedTask;
        }

        private async Task<bool> SendTestCommandAsync(CancellationToken token)
        {
            try
            {
                await EnsureFpgaConnectedAsync(token).ConfigureAwait(false);
                AddLog($"FPGA发送指令: {FormatData(TestCommandFrame)}");
                await _fpga.WriteAsync(TestCommandFrame, 0, TestCommandFrame.Length, token).ConfigureAwait(false);
                return true;
            }
            catch (Exception ex)
            {
                AddLog($"FPGA发送失败: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> ConnectLoadAsync(double targetOhm, CancellationToken token)
        {
            try
            {
                bool ok = await EnsureResistorReadyAsync().ConfigureAwait(false);
                if (!ok || _resistorDriver == null || !_resistorDriver.IsConnected)
                {
                    AddLog("接入负载失败：电阻输出未就绪");
                    UpdateLoadReadback(targetOhm, null);
                    return false;
                }

                AddLog($"接入负载: 目标={targetOhm:F2}Ω (电阻输出1)");

                var relayOk = await _resistorDriver.SetRelayStateAsync("RO1", true, false).ConfigureAwait(false);
                if (!relayOk)
                {
                    AddLog("7012设置RO1继电器失败");
                    UpdateLoadReadback(targetOhm, null);
                    return false;
                }

                var writeOk = await _resistorDriver.WriteChannelAsync("RO1", targetOhm).ConfigureAwait(false);
                if (!writeOk)
                {
                    AddLog("7012写入RO1失败");
                    UpdateLoadReadback(targetOhm, null);
                    return false;
                }

                await Task.Delay(50, token).ConfigureAwait(false);
                var readBack = await _resistorDriver.ReadChannelAsync("RO1").ConfigureAwait(false);

                UpdateLoadReadback(targetOhm, readBack);

                var within = Math.Abs(readBack - targetOhm) <= LoadToleranceOhm;
                AddLog($"负载读回: {readBack:F5}Ω, 判定={(within ? "PASS" : "FAIL")}");
                return within;
            }
            catch (Exception ex)
            {
                AddLog($"接入负载异常: {ex.Message}");
                UpdateLoadReadback(targetOhm, null);
                return false;
            }
        }

        private void UpdateLoadReadback(double targetOhm, double? readBack)
        {
            var text = readBack.HasValue ? $"{readBack.Value:F5} Ω" : "--";
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                if (Math.Abs(targetOhm - Load50Ohm) < 0.01)
                    Step1LoadReadback = text;
                else
                    Step4LoadReadback = text;
            });
        }

        private async Task<bool> EnsureResistorReadyAsync()
        {
            if (_resistorDriver != null && _resistorDriver.IsConnected)
                return true;

            try
            {
                var candidates = new uint[] { 1, 0, 2, 3, 4, 5, 6, 7 };
                foreach (var logicalId in candidates)
                {
                    AddLog($"电阻板卡直连：尝试ACTS6010逻辑ID={logicalId}");
                    var dummy = new ProgrammableResistorDevice
                    {
                        Name = "电阻输出",
                        Model = "PXI-7012",
                        CardName = $"电阻输出(自动探测-{logicalId})",
                        SlotIndex = (int)logicalId
                    };

                    var driver = new ACTS6010Driver(dummy, logicalId);
                    var ok = await driver.ConnectAsync().ConfigureAwait(false);
                    if (ok)
                    {
                        _resistorDriver = driver;
                        AddLog($"电阻板卡已连接：ACTS6010 逻辑ID={logicalId}");
                        return true;
                    }
                }

                AddLog("电阻板卡打开失败：ACTS6010 逻辑ID 0-7 均连接失败");
                return false;
            }
            catch (Exception ex)
            {
                AddLog($"电阻板卡打开异常: {ex.Message}");
                _resistorDriver = null;
                return false;
            }
        }

        private async Task DisconnectResistorAsync()
        {
            try
            {
                if (_resistorDriver != null)
                {
                    await _resistorDriver.DisconnectAsync().ConfigureAwait(false);
                    _resistorDriver = null;
                }
            }
            catch { }
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

            try { await DisconnectResistorAsync().ConfigureAwait(false); } catch { }
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
