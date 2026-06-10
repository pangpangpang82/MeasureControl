using Prism.Commands;
using Prism.Mvvm;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using MeasureControl.Helpers;
using MeasureControl.Services;
using MeasureControl.Services.HardwareApis;

namespace MeasureControl.ViewModels.SingleBoardTest.AirController
{
    public sealed class B_C_7_4_8_2ViewModel : BindableBase, IDisposable
    {
        private const string DefaultFpgaIpAddress = "192.168.1.10";
        private const int DefaultFpgaPort = 5001;

        private const string PowerSupply28VIpAddress = "192.168.1.15";
        private const string PowerSupply3V3IpAddress = "192.168.1.16";

        private const PowerSupplyChannel PowerSupply28VCh1 = PowerSupplyChannel.CH1;
        private const PowerSupplyChannel PowerSupply28VCh2 = PowerSupplyChannel.CH2;
        private const double Power28VVoltage = 28.0;
        private const double Power28VCurrentLimit = 3.0;

        private const PowerSupplyChannel PowerSupply3V3Channel = PowerSupplyChannel.CH3;
        private const double Power3V3Voltage = 3.3;
        private const double Power3V3CurrentLimit = 1.0;

        private const string DefaultMatrixIpAddress = "192.168.1.3";
        private const int DefaultMatrixTcpBasePort = 50200;

        private const int Chassis2Slot2601_1 = 4; // 2601(1)
        private const int Chassis2Slot2601_2 = 6; // 2601(2)

        private const string Slot2601_2_In = "I1";
        private const string Slot2601_2_J221_Out = "O18";
        private const string Slot2601_2_J223_Out = "O19";

        private const string Slot2601_1_Ch1_In = "I0";
        private const string Slot2601_1_Ch2_In = "I1";
        private const string Slot2601_1_ToScope_Out = "O2";

        private const int DefaultScopePort = 5555;
        private const string DefaultScopeIpAddress = "192.168.1.18";

        private static readonly byte[] DeviceInitCommandFrame = { 0xAA, 0x55, 0x02, 0x02, 0x01 };
        private static readonly byte[] DirHighCommand = { 0xAA, 0x55, 0x0A, 0x04, 0x06, 0xE8, 0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] DirLowCommand = { 0xAA, 0x55, 0x0A, 0x04, 0x02, 0xE8, 0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] ResetToInitialCommandFrame = { 0xAA, 0x55, 0x0A, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };

        private const double ExpectedPhaseHighDeg = 90.0;
        private const double ExpectedPhaseLowDeg = 270.0;
        private const double PhaseToleranceDeg = 20.0;

        private readonly SemaphoreSlim _manualTestLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _autoTestLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _opLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _instrumentLock = new SemaphoreSlim(1, 1);

        private CancellationTokenSource _autoTestCts;

        private FpgaTcpClient _fpga;

        private IPowerSupplyApi _powerSupply28V;
        private IPowerSupplyApi _powerSupply3V3;

        private TcpClient _scopeTcpClient;
        private NetworkStream _scopeTcpStream;

        private bool _isMatrixRouted;
        private bool _matrixRouted2601_2_J221;
        private bool _matrixRouted2601_2_J223;
        private bool _matrixRouted2601_1_Ch1;
        private bool _matrixRouted2601_1_Ch2;

        private bool _isManualTestRunning;
        private bool _isAutoTestRunning;
        private bool _isBusy;

        private bool _isPowerOn;
        private string _powerStatus = "未供电";

        private string _phaseHighText = "--";
        private string _phaseLowText = "--";

        private string _step2Result = "--";
        private string _step4Result = "--";

        private string _lastTestTime = "--";
        private string _overallResult = "--";

        public B_C_7_4_8_2ViewModel()
        {
            ManualTestCommand = new DelegateCommand(OnManualTest, () => !IsAutoTestRunning && (!IsBusy || IsManualTestRunning));
            AutoTestCommand = new DelegateCommand(OnAutoTest, () => !IsManualTestRunning && (!IsBusy || IsAutoTestRunning));
            ClearLogCommand = new DelegateCommand(() => Logs.Clear());

            Step1SendDirHighCommand = new DelegateCommand(async () => await SendDirHighAsync());
            Step2MeasurePhaseHighCommand = new DelegateCommand(async () => await MeasurePhaseHighAsync());
            Step3SendDirLowCommand = new DelegateCommand(async () => await SendDirLowAsync());
            Step4MeasurePhaseLowCommand = new DelegateCommand(async () => await MeasurePhaseLowAsync());
        }

        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();

        public DelegateCommand ManualTestCommand { get; }
        public DelegateCommand AutoTestCommand { get; }
        public DelegateCommand ClearLogCommand { get; }

        public DelegateCommand Step1SendDirHighCommand { get; }
        public DelegateCommand Step2MeasurePhaseHighCommand { get; }
        public DelegateCommand Step3SendDirLowCommand { get; }
        public DelegateCommand Step4MeasurePhaseLowCommand { get; }

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

        public string PhaseHighText { get => _phaseHighText; private set => SetProperty(ref _phaseHighText, value); }
        public string PhaseLowText { get => _phaseLowText; private set => SetProperty(ref _phaseLowText, value); }

        public string Step2Result { get => _step2Result; private set => SetProperty(ref _step2Result, value); }
        public string Step4Result { get => _step4Result; private set => SetProperty(ref _step4Result, value); }

        public string LastTestTime { get => _lastTestTime; private set => SetProperty(ref _lastTestTime, value); }
        public string OverallResult { get => _overallResult; private set => SetProperty(ref _overallResult, value); }

        private void RaiseAllCanExecuteChanged()
        {
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                ManualTestCommand?.RaiseCanExecuteChanged();
                AutoTestCommand?.RaiseCanExecuteChanged();
                Step1SendDirHighCommand?.RaiseCanExecuteChanged();
                Step2MeasurePhaseHighCommand?.RaiseCanExecuteChanged();
                Step3SendDirLowCommand?.RaiseCanExecuteChanged();
                Step4MeasurePhaseLowCommand?.RaiseCanExecuteChanged();
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
                AddLog("未供电：请先点击【手动测试】");
                return false;
            }

            return true;
        }

        private void ClearResults()
        {
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                PhaseHighText = "--";
                PhaseLowText = "--";
                Step2Result = "--";
                Step4Result = "--";
                OverallResult = "--";
                LastTestTime = "--";
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
                    await Apply28VPowerAsync(CancellationToken.None).ConfigureAwait(false);
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

                IsBusy = true;
                try
                {
                    await CleanupAsync(CancellationToken.None).ConfigureAwait(false);
                    if (IsPowerOn)
                        await ApplyDownPowerAsync(CancellationToken.None).ConfigureAwait(false);
                }
                finally
                {
                    IsBusy = false;
                }

                AddLog("========== 手动测试已停止 ==========");
            }
            catch (Exception ex)
            {
                AddLog($"停止手动测试异常: {ex.Message}");
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
                        await Apply28VPowerAsync(token).ConfigureAwait(false);
                        await EnsureFpgaConnectedAsync(token).ConfigureAwait(false);
                    }
                    finally
                    {
                        IsBusy = false;
                    }

                    AddLog("========== 自动测试开始 ==========");

                    var ok1 = await SendDirHighCommandAsync(token).ConfigureAwait(false);
                    if (!ok1)
                    {
                        SetOverall("FAIL");
                        return;
                    }

                    AddLog("等待500ms后开始波形检测...");
                    await Task.Delay(500, token).ConfigureAwait(false);

                    var (ok2, phaseHighDeg) = await MeasurePhaseDiffDegAsync(token).ConfigureAwait(false);
                    PhaseHighText = phaseHighDeg.HasValue ? $"{phaseHighDeg.Value:F1} °" : "--";
                    var pass2 = ok2 && phaseHighDeg.HasValue && IsPhaseMatch(phaseHighDeg.Value, ExpectedPhaseHighDeg, PhaseToleranceDeg);
                    Step2Result = pass2 ? "PASS" : "FAIL";
                    AddLog($"DIR高电平相位差: {PhaseHighText}  判据: {ExpectedPhaseHighDeg:F0}°±{PhaseToleranceDeg:F0}° => {Step2Result}");

                    var ok3 = await SendFpgaCommandAsync(DirLowCommand, "DIR低电平", token).ConfigureAwait(false);
                    if (!ok3)
                    {
                        SetOverall("FAIL");
                        return;
                    }

                    AddLog("等待500ms后开始波形检测...");
                    await Task.Delay(500, token).ConfigureAwait(false);

                    var (ok4, phaseLowDeg) = await MeasurePhaseDiffDegAsync(token).ConfigureAwait(false);
                    PhaseLowText = phaseLowDeg.HasValue ? $"{phaseLowDeg.Value:F1} °" : "--";
                    var pass4 = ok4 && phaseLowDeg.HasValue && IsPhaseMatch(phaseLowDeg.Value, ExpectedPhaseLowDeg, PhaseToleranceDeg);
                    Step4Result = pass4 ? "PASS" : "FAIL";
                    AddLog($"DIR低电平相位差: {PhaseLowText}  判据: {ExpectedPhaseLowDeg:F0}°±{PhaseToleranceDeg:F0}° => {Step4Result}");

                    SetOverall((pass2 && pass4) ? "PASS" : "FAIL");
                    AddLog($"========== 自动测试结束，总结果: {(pass2 && pass4 ? "PASS" : "FAIL")} ==========");
                }
                catch (OperationCanceledException)
                {
                    AddLog("自动测试已取消");
                    SetOverall("STOP");
                }
                catch (Exception ex)
                {
                    AddLog($"自动测试异常: {ex.Message}");
                    SetOverall("FAIL");
                }
                finally
                {
                    IsAutoTestRunning = false;
                    _autoTestCts?.Dispose();
                    _autoTestCts = null;

                    try { await CleanupAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                }
            }
            finally
            {
                _autoTestLock.Release();
            }
        }

        private async Task<bool> SendDirHighCommandAsync(CancellationToken token)
        {
            var okInit = await SendFpgaCommandAsync(DeviceInitCommandFrame, "设备初始化", token).ConfigureAwait(false);
            if (!okInit)
                return false;

            return await SendFpgaCommandAsync(DirHighCommand, "DIR高电平", token).ConfigureAwait(false);
        }

        private async Task SendDirHighAsync()
        {
            if (!EnsureManualStepAllowed())
                return;

            await _opLock.WaitAsync().ConfigureAwait(false);
            try
            {
                IsBusy = true;
                try
                {
                    var ok = await SendDirHighCommandAsync(CancellationToken.None).ConfigureAwait(false);
                    AddLog($"DIR高电平指令: {(ok ? "PASS" : "FAIL")}");
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

        private async Task SendDirLowAsync()
        {
            if (!EnsureManualStepAllowed())
                return;

            await _opLock.WaitAsync().ConfigureAwait(false);
            try
            {
                IsBusy = true;
                try
                {
                    _ = await SendFpgaCommandAsync(DirLowCommand, "DIR低电平", CancellationToken.None).ConfigureAwait(false);
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

        private async Task MeasurePhaseHighAsync()
        {
            if (!EnsureManualStepAllowed())
                return;

            await _opLock.WaitAsync().ConfigureAwait(false);
            try
            {
                IsBusy = true;
                try
                {
                    var (ok, phaseDeg) = await MeasurePhaseDiffDegAsync(CancellationToken.None).ConfigureAwait(false);
                    PhaseHighText = phaseDeg.HasValue ? $"{phaseDeg.Value:F1} °" : "--";
                    var pass = ok && phaseDeg.HasValue && IsPhaseMatch(phaseDeg.Value, ExpectedPhaseHighDeg, PhaseToleranceDeg);
                    Step2Result = pass ? "PASS" : "FAIL";
                    AddLog($"手动-高电平相位差: {PhaseHighText}  判据: {ExpectedPhaseHighDeg:F0}°±{PhaseToleranceDeg:F0}° => {Step2Result}");
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

        private async Task MeasurePhaseLowAsync()
        {
            if (!EnsureManualStepAllowed())
                return;

            await _opLock.WaitAsync().ConfigureAwait(false);
            try
            {
                IsBusy = true;
                try
                {
                    var (ok, phaseDeg) = await MeasurePhaseDiffDegAsync(CancellationToken.None).ConfigureAwait(false);
                    PhaseLowText = phaseDeg.HasValue ? $"{phaseDeg.Value:F1} °" : "--";
                    var pass = ok && phaseDeg.HasValue && IsPhaseMatch(phaseDeg.Value, ExpectedPhaseLowDeg, PhaseToleranceDeg);
                    Step4Result = pass ? "PASS" : "FAIL";
                    AddLog($"手动-低电平相位差: {PhaseLowText}  判据: {ExpectedPhaseLowDeg:F0}°±{PhaseToleranceDeg:F0}° => {Step4Result}");
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

        public void Dispose()
        {
            try
            {
                _autoTestCts?.Cancel();
                _autoTestCts?.Dispose();
            }
            catch { }

            try
            {
                CleanupAsync(CancellationToken.None).GetAwaiter().GetResult();
            }
            catch { }

            _manualTestLock?.Dispose();
            _autoTestLock?.Dispose();
            _opLock?.Dispose();
            _instrumentLock?.Dispose();
        }

        private static bool IsPhaseMatch(double actualDeg, double expectedDeg, double toleranceDeg)
        {
            actualDeg = NormalizeDeg(actualDeg);
            expectedDeg = NormalizeDeg(expectedDeg);

            var diff = Math.Abs(actualDeg - expectedDeg);
            diff = Math.Min(diff, 360.0 - diff);
            return diff <= toleranceDeg;
        }

        private static double NormalizeDeg(double deg)
        {
            deg %= 360.0;
            if (deg < 0)
                deg += 360.0;
            return deg;
        }

        private async Task Apply28VPowerAsync(CancellationToken token)
        {
            AddLog("组件供电：上电中...");

            try
            {
                await EnsurePowerSupply28VConnectedAsync(token).ConfigureAwait(false);
                await _powerSupply28V.ApplyAsync(PowerSupply28VCh1, Power28VVoltage, Power28VCurrentLimit, token).ConfigureAwait(false);
                await _powerSupply28V.ApplyAsync(PowerSupply28VCh2, Power28VVoltage, Power28VCurrentLimit, token).ConfigureAwait(false);
                await _powerSupply28V.SetOutputEnabledAsync(PowerSupply28VCh1, true, token).ConfigureAwait(false);
                await _powerSupply28V.SetOutputEnabledAsync(PowerSupply28VCh2, true, token).ConfigureAwait(false);
                await Task.Delay(300, token).ConfigureAwait(false);
                AddLog($"组件供电：28V 上电 CH1+CH2, IP={PowerSupply28VIpAddress}");

                await EnsurePowerSupply3V3ConnectedAsync(token).ConfigureAwait(false);
                await _powerSupply3V3.ApplyAsync(PowerSupply3V3Channel, Power3V3Voltage, Power3V3CurrentLimit, token).ConfigureAwait(false);
                await _powerSupply3V3.SetOutputEnabledAsync(PowerSupply3V3Channel, true, token).ConfigureAwait(false);
                await Task.Delay(200, token).ConfigureAwait(false);
                AddLog($"组件供电：3.3V 上电 CH3, IP={PowerSupply3V3IpAddress}");

                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    IsPowerOn = true;
                    PowerStatus = "供电";
                });
            }
            catch (Exception ex)
            {
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    IsPowerOn = false;
                    PowerStatus = "未供电";
                });
                AddLog($"组件供电上电失败: {ex.Message}");
                throw;
            }
        }

        private async Task ApplyDownPowerAsync(CancellationToken token)
        {
            AddLog("组件供电：下电中...");

            try
            {
                try
                {
                    if (_powerSupply3V3 != null)
                        await _powerSupply3V3.SetOutputEnabledAsync(PowerSupply3V3Channel, false, token).ConfigureAwait(false);
                }
                catch
                {
                }

                try
                {
                    if (_powerSupply28V != null)
                    {
                        await _powerSupply28V.SetOutputEnabledAsync(PowerSupply28VCh1, false, token).ConfigureAwait(false);
                        await _powerSupply28V.SetOutputEnabledAsync(PowerSupply28VCh2, false, token).ConfigureAwait(false);
                    }
                }
                catch
                {
                }
            }
            finally
            {
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    IsPowerOn = false;
                    PowerStatus = "未供电";
                });
                AddLog("组件已下电");
            }
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
            await _fpga.ConnectAsync(DefaultFpgaIpAddress, DefaultFpgaPort, token).ConfigureAwait(false);
            AddLog("FPGA连接成功");
        }

        private async Task<bool> SendFpgaCommandAsync(byte[] cmd, string title, CancellationToken token)
        {
            try
            {
                await EnsureFpgaConnectedAsync(token).ConfigureAwait(false);
                AddLog($"FPGA发送{title}指令: {FormatData(cmd)}");
                await _fpga.WriteAsync(cmd, 0, cmd.Length, token).ConfigureAwait(false);
                return true;
            }
            catch (Exception ex)
            {
                AddLog($"FPGA发送失败({title}): {ex.Message}");
                return false;
            }
        }

        private async Task<(bool ok, double? phaseDeg)> MeasurePhaseDiffDegAsync(CancellationToken token)
        {
            await _instrumentLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                var matrix = MatrixControlService.Instance;

                bool okJ221 = false;
                bool okJ223 = false;
                bool okCh1 = false;
                bool okCh2 = false;

                try
                {
                    if (_isMatrixRouted)
                        await UnrouteMatrixAsync(token).ConfigureAwait(false);

                    AddLog("路由测相位：J221->CH1, J223->CH2");

                    okJ221 = await matrix.ConnectNodesAsync(Slot2601_2_In, Slot2601_2_J221_Out, Chassis2Slot2601_2, DefaultMatrixIpAddress, DefaultMatrixTcpBasePort).ConfigureAwait(false);
                    okJ223 = await matrix.ConnectNodesAsync(Slot2601_2_In, Slot2601_2_J223_Out, Chassis2Slot2601_2, DefaultMatrixIpAddress, DefaultMatrixTcpBasePort).ConfigureAwait(false);
                    okCh1 = await matrix.ConnectNodesAsync(Slot2601_1_Ch1_In, Slot2601_1_ToScope_Out, Chassis2Slot2601_1, DefaultMatrixIpAddress, DefaultMatrixTcpBasePort).ConfigureAwait(false);
                    okCh2 = await matrix.ConnectNodesAsync(Slot2601_1_Ch2_In, Slot2601_1_ToScope_Out, Chassis2Slot2601_1, DefaultMatrixIpAddress, DefaultMatrixTcpBasePort).ConfigureAwait(false);

                    _matrixRouted2601_2_J221 = okJ221;
                    _matrixRouted2601_2_J223 = okJ223;
                    _matrixRouted2601_1_Ch1 = okCh1;
                    _matrixRouted2601_1_Ch2 = okCh2;
                    _isMatrixRouted = okJ221 && okJ223 && okCh1 && okCh2;

                    AddLog($"路由结果：2601(2)J221={(okJ221 ? "OK" : "FAIL")}, J223={(okJ223 ? "OK" : "FAIL")}; 2601(1)CH1={(okCh1 ? "OK" : "FAIL")}, CH2={(okCh2 ? "OK" : "FAIL")}");
                    if (!_isMatrixRouted)
                        return (false, null);

                    await EnsureScopeConnectedAsync(token).ConfigureAwait(false);
                    await Task.Delay(200, token).ConfigureAwait(false);

                    var phase = await QueryScopePhaseDegAsync(token).ConfigureAwait(false);
                    return (phase.HasValue, phase);
                }
                finally
                {
                    await UnrouteMatrixAsync(token).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                AddLog($"测相位异常: {ex.Message}");
                return (false, null);
            }
            finally
            {
                _instrumentLock.Release();
            }
        }

        private async Task<double?> QueryScopePhaseDegAsync(CancellationToken token)
        {
            var candidates = new[]
            {
                ":MEASure:ITEM? PHASe",
                ":MEASure:ITEM? PHAS",
                ":MEASure:ITEM? PHASe,CHANnel1,CHANnel2",
                ":MEASure:ITEM? PHASe,CHANnel2,CHANnel1",
                ":MEASure:PHASe? CHANnel1,CHANnel2",
                ":MEASure:PHASe? CHANnel2,CHANnel1",
            };

            foreach (var cmd in candidates)
            {
                var v = await QueryScopeDoubleAsync(cmd, token).ConfigureAwait(false);
                if (v.HasValue)
                {
                    AddLog($"示波器相位读取({cmd})={v.Value:F1}°");
                    return v.Value;
                }
            }

            AddLog("示波器相位读取失败：未匹配到可用SCPI返回值");
            return null;
        }

        private async Task UnrouteMatrixAsync(CancellationToken token)
        {
            if (!_isMatrixRouted && !_matrixRouted2601_2_J221 && !_matrixRouted2601_2_J223 && !_matrixRouted2601_1_Ch1 && !_matrixRouted2601_1_Ch2)
                return;

            var matrix = MatrixControlService.Instance;

            try
            {
                if (_matrixRouted2601_1_Ch2)
                    _ = await matrix.DisconnectNodesAsync(Slot2601_1_Ch2_In, Slot2601_1_ToScope_Out, Chassis2Slot2601_1, DefaultMatrixIpAddress, DefaultMatrixTcpBasePort).ConfigureAwait(false);
            }
            catch { }

            try
            {
                if (_matrixRouted2601_1_Ch1)
                    _ = await matrix.DisconnectNodesAsync(Slot2601_1_Ch1_In, Slot2601_1_ToScope_Out, Chassis2Slot2601_1, DefaultMatrixIpAddress, DefaultMatrixTcpBasePort).ConfigureAwait(false);
            }
            catch { }

            try
            {
                if (_matrixRouted2601_2_J223)
                    _ = await matrix.DisconnectNodesAsync(Slot2601_2_In, Slot2601_2_J223_Out, Chassis2Slot2601_2, DefaultMatrixIpAddress, DefaultMatrixTcpBasePort).ConfigureAwait(false);
            }
            catch { }

            try
            {
                if (_matrixRouted2601_2_J221)
                    _ = await matrix.DisconnectNodesAsync(Slot2601_2_In, Slot2601_2_J221_Out, Chassis2Slot2601_2, DefaultMatrixIpAddress, DefaultMatrixTcpBasePort).ConfigureAwait(false);
            }
            catch { }

            _isMatrixRouted = false;
            _matrixRouted2601_2_J221 = false;
            _matrixRouted2601_2_J223 = false;
            _matrixRouted2601_1_Ch1 = false;
            _matrixRouted2601_1_Ch2 = false;
        }

        private async Task EnsureScopeConnectedAsync(CancellationToken token)
        {
            if (_scopeTcpClient != null && _scopeTcpStream != null)
                return;

            _scopeTcpClient = new TcpClient();
            await _scopeTcpClient.ConnectAsync(DefaultScopeIpAddress, DefaultScopePort).ConfigureAwait(false);
            _scopeTcpStream = _scopeTcpClient.GetStream();

            try
            {
                _scopeTcpStream.ReadTimeout = 5000;
                _scopeTcpStream.WriteTimeout = 5000;
            }
            catch
            {
            }

            await QueryScopeStringAsync(":MEASure:SOURce CHANnel1", token).ConfigureAwait(false);
            await QueryScopeStringAsync(":MEASure:CLEar", token).ConfigureAwait(false);
            AddLog("示波器连接成功");
        }

        private async Task<double?> QueryScopeDoubleAsync(string command, CancellationToken token)
        {
            var s = await QueryScopeStringAsync(command, token).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(s))
                return null;

            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                return v;

            return null;
        }

        private async Task<string> QueryScopeStringAsync(string command, CancellationToken token)
        {
            if (_scopeTcpStream == null)
                throw new InvalidOperationException("示波器未连接");

            var cmd = Encoding.ASCII.GetBytes(command + "\n");
            await _scopeTcpStream.WriteAsync(cmd, 0, cmd.Length, token).ConfigureAwait(false);
            await _scopeTcpStream.FlushAsync(token).ConfigureAwait(false);

            if (!command.TrimEnd().EndsWith("?", StringComparison.Ordinal))
                return string.Empty;

            return await ReadLineAsync(_scopeTcpStream, 5000, token).ConfigureAwait(false);
        }

        private static async Task<string> ReadLineAsync(NetworkStream stream, int timeoutMs, CancellationToken token)
        {
            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                cts.CancelAfter(timeoutMs);

                var sb = new StringBuilder();
                var buffer = new byte[1];

                while (true)
                {
                    int n = await stream.ReadAsync(buffer, 0, 1, cts.Token).ConfigureAwait(false);
                    if (n <= 0)
                        break;

                    char ch = (char)buffer[0];
                    if (ch == '\n')
                        break;
                    if (ch == '\r')
                        continue;
                    sb.Append(ch);

                    if (sb.Length > 4096)
                        break;
                }

                return sb.ToString().Trim();
            }
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

        private async Task CleanupAsync(CancellationToken token)
        {
            try { await UnrouteMatrixAsync(token).ConfigureAwait(false); } catch { }

            try { _scopeTcpStream?.Dispose(); } catch { }
            _scopeTcpStream = null;
            try { _scopeTcpClient?.Close(); } catch { }
            try { _scopeTcpClient?.Dispose(); } catch { }
            _scopeTcpClient = null;

            try { await TryResetFpgaToInitialAsync(token).ConfigureAwait(false); } catch { }
            try { _fpga?.Disconnect(); } catch { }
            try { _fpga?.Dispose(); } catch { }
            _fpga = null;
        }

        private static string FormatData(byte[] data)
        {
            if (data == null)
                return string.Empty;

            var sb = new StringBuilder();
            foreach (var b in data)
            {
                if (sb.Length > 0)
                    sb.Append(' ');
                sb.Append(b.ToString("X2"));
            }
            return sb.ToString();
        }
    }
}
