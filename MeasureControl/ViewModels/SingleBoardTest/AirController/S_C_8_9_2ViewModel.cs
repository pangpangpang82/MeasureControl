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
    public sealed class S_C_8_9_2ViewModel : BindableBase, IDisposable
    {
        private const string DefaultFpgaIpAddress = "192.168.1.10";
        private const int DefaultFpgaPort = 5001;

        private const string MatrixIpAddress = "192.168.1.3";
        private const int MatrixTcpBasePort = 50200;

        private const int MatrixSlotSig = 6;
        private const int MatrixSlotDmm = 4;
        private const string DefaultMatrixSigOut = "O0";
        private static readonly (string Name, string In, string Out, int Slot) MatrixPointDcmV1Gnd = ("DCM_V1对地", "I1", "O15", MatrixSlotSig);
        private static readonly (string Name, string In, string Out, int Slot) MatrixPointDcmV2Gnd = ("DCM_V2对地", "I1", "O14", MatrixSlotSig);
        private static readonly (string Name, string In, string Out, int Slot) MatrixPointDcmV1V1 = ("DCM_V1对DCM_V2", "I1", "O16", MatrixSlotSig);
        private static readonly (string In, string Out, int Slot) MatrixDmmH = ("I4", "O2", MatrixSlotDmm);

        private static string NormalizeSigOut(string node)
        {
            return string.IsNullOrWhiteSpace(node) ? DefaultMatrixSigOut : node;
        }

        private const string DmmIpAddress = "192.168.1.13";
        private const int DmmTimeoutMs = 8000;

        private const string PowerSupply28VIpAddress = "192.168.1.15";

        private const PowerSupplyChannel PowerSupply28VCh1 = PowerSupplyChannel.CH1;
        private const double Power28VVoltage = 28.0;
        private const double Power28VCurrentLimit = 3.0;

        private static readonly byte[] DeviceInitCommandFrame = { 0xAA, 0x55, 0x02, 0x02, 0x01 };
        private static readonly byte[] PhHighCommandFrame = { 0xAA, 0x55, 0x0A, 0x05, 0x01, 0xE8, 0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] PhLowCommandFrame = { 0xAA, 0x55, 0x0A, 0x05, 0x00, 0xE8, 0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] ResetToInitialCommandFrame = { 0xAA, 0x55, 0x0A, 0x05, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };

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

        public S_C_8_9_2ViewModel()
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
            var allDone = new[] { Step1Result, Step2Result, Step3Result, Step4Result }.All(IsResultDone);
            if (!allDone)
                return;

            var pass = new[] { Step1Result, Step2Result, Step3Result, Step4Result }.All(v => v == "PASS");
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                var failPoint = ExtractOverallFailPoint();
                OverallResult = pass ? "PASS" : (string.IsNullOrWhiteSpace(failPoint) ? "FAIL" : $"FAIL({failPoint})");
                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            });
        }

        private void TryFinalizeManualTestIfComplete()
        {
            if (!IsManualTestRunning)
                return;

            var allDone = new[] { Step1Result, Step2Result, Step3Result, Step4Result }.All(IsResultDone);
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

                var allDone = new[] { Step1Result, Step2Result, Step3Result, Step4Result }.All(IsResultDone);
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
                    var (pass2, failPoint2, voltageText2) = await MeasureThreePointsAsync(expectPositive: true, token).ConfigureAwait(false);
                    UpdateVoltageStep(isFirst: true, pass2, failPoint2, voltageText2);

                    var ok3 = await SendTestCommandAsync(isPhHigh: false, token).ConfigureAwait(false);
                    Step3Result = ok3 ? "PASS" : "FAIL";

                    await Task.Delay(500, token).ConfigureAwait(false);
                    var (pass4, failPoint4, voltageText4) = await MeasureThreePointsAsync(expectPositive: false, token).ConfigureAwait(false);
                    UpdateVoltageStep(isFirst: false, pass4, failPoint4, voltageText4);

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
                        var (pass, failPoint, voltageText) = await MeasureThreePointsAsync(expectPositive: isFirst, CancellationToken.None).ConfigureAwait(false);
                        UpdateVoltageStep(isFirst, pass, failPoint, voltageText);
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

        private void UpdateVoltageStep(bool isFirst, bool pass, string failPoint, string voltageText)
        {
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                var r = pass ? "PASS" : $"FAIL({failPoint})";

                if (isFirst)
                {
                    Step2Voltage = voltageText;
                    Step2Result = r;
                }
                else
                {
                    Step4Voltage = voltageText;
                    Step4Result = r;
                }
            });
        }

        private static bool IsResultDone(string v)
        {
            if (string.IsNullOrWhiteSpace(v))
                return false;
            return v == "PASS" || v.StartsWith("FAIL", StringComparison.Ordinal);
        }

        private string ExtractOverallFailPoint()
        {
            if (TryExtractFailPoint(Step2Result, out var p2))
                return p2;
            if (TryExtractFailPoint(Step4Result, out var p4))
                return p4;

            if (Step1Result == "FAIL")
                return "步骤1";
            if (Step3Result == "FAIL")
                return "步骤3";

            return string.Empty;
        }

        private static bool TryExtractFailPoint(string result, out string failPoint)
        {
            failPoint = string.Empty;
            if (string.IsNullOrWhiteSpace(result))
                return false;

            var prefix = "FAIL(";
            var idx = result.IndexOf(prefix, StringComparison.Ordinal);
            if (idx < 0)
                return false;

            var start = idx + prefix.Length;
            var end = result.IndexOf(')', start);
            if (end <= start)
                return false;

            failPoint = result.Substring(start, end - start);
            return !string.IsNullOrWhiteSpace(failPoint);
        }

        private async Task<(bool Pass, string FailPoint, string VoltageText)> MeasureThreePointsAsync(bool expectPositive, CancellationToken token)
        {
            await _instrumentLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                var r1 = await MeasureVoltageAtPointCoreAsync(MatrixPointDcmV1Gnd, token).ConfigureAwait(false);
                if (!IsPointPass(r1.Ok, r1.Voltage, expectPositive, out var reason1))
                {
                    AddLog($"{MatrixPointDcmV1Gnd.Name} FAIL: {reason1}");
                    return (false, MatrixPointDcmV1Gnd.Name, BuildVoltageText(r1.Voltage, null, null));
                }

                var r2 = await MeasureVoltageAtPointCoreAsync(MatrixPointDcmV2Gnd, token).ConfigureAwait(false);
                if (!IsPointPass(r2.Ok, r2.Voltage, expectPositive, out var reason2))
                {
                    AddLog($"{MatrixPointDcmV2Gnd.Name} FAIL: {reason2}");
                    return (false, MatrixPointDcmV2Gnd.Name, BuildVoltageText(r1.Voltage, r2.Voltage, null));
                }

                var r3 = await MeasureVoltageAtPointCoreAsync(MatrixPointDcmV1V1, token).ConfigureAwait(false);
                if (!IsPointPass(r3.Ok, r3.Voltage, expectPositive, out var reason3))
                {
                    AddLog($"{MatrixPointDcmV1V1.Name} FAIL: {reason3}");
                    return (false, MatrixPointDcmV1V1.Name, BuildVoltageText(r1.Voltage, r2.Voltage, r3.Voltage));
                }

                return (true, string.Empty, BuildVoltageText(r1.Voltage, r2.Voltage, r3.Voltage));
            }
            finally
            {
                _instrumentLock.Release();
            }
        }

        private static string BuildVoltageText(double? v1, double? v2, double? v12)
        {
            string F(double? v) => v.HasValue ? $"{v.Value:F3}" : "--";
            return $"V1={F(v1)} V, V2={F(v2)} V, V1V1={F(v12)} V";
        }

        private static bool IsPointPass(bool ok, double? voltage, bool expectPositive, out string reason)
        {
            if (!ok)
            {
                reason = "测量失败";
                return false;
            }
            if (!voltage.HasValue)
            {
                reason = "电压无有效值";
                return false;
            }

            var v = voltage.Value;
            var polarityOk = expectPositive ? v > 0 : v < 0;
            if (!polarityOk)
            {
                reason = expectPositive ? $"极性错误(应为正): {v:F3}V" : $"极性错误(应为负): {v:F3}V";
                return false;
            }

            var absV = Math.Abs(v);
            if (absV < VoltageMin || absV > VoltageMax)
            {
                reason = $"幅值不在[{VoltageMin},{VoltageMax}]V: {absV:F3}V";
                return false;
            }

            reason = null;
            return true;
        }

        private async Task<(bool Ok, double? Voltage)> MeasureVoltageAtPointCoreAsync((string Name, string In, string Out, int Slot) sigPoint, CancellationToken token)
        {
            var matrix = MatrixControlService.Instance;
            bool okDmm = false;
            bool okSig = false;
            var sigOut = NormalizeSigOut(sigPoint.Out);
            try
            {
                AddLog($"矩阵路由: slot{MatrixSlotDmm} {MatrixDmmH.In}-{MatrixDmmH.Out} + slot{sigPoint.Slot} {sigPoint.In}-{sigOut} ({sigPoint.Name})");
                okDmm = await matrix.ConnectNodesAsync(MatrixDmmH.In, MatrixDmmH.Out, MatrixDmmH.Slot, MatrixIpAddress, MatrixTcpBasePort).ConfigureAwait(false);
                okSig = await matrix.ConnectNodesAsync(sigPoint.In, sigOut, sigPoint.Slot, MatrixIpAddress, MatrixTcpBasePort).ConfigureAwait(false);
                if (!okDmm || !okSig)
                    return (false, null);

                await Task.Delay(200, token).ConfigureAwait(false);
                var reading = await DmmReadVoltageAsync(token).ConfigureAwait(false);
                if (reading?.Value == null)
                    return (false, null);
                return (true, reading.Value.Value);
            }
            catch (Exception ex)
            {
                AddLog($"电压测量异常({sigPoint.Name}): {ex.Message}");
                return (false, null);
            }
            finally
            {
                try
                {
                    if (okSig)
                        _ = await matrix.DisconnectNodesAsync(sigPoint.In, sigOut, sigPoint.Slot, MatrixIpAddress, MatrixTcpBasePort).ConfigureAwait(false);
                }
                catch { }
                try
                {
                    if (okDmm)
                        _ = await matrix.DisconnectNodesAsync(MatrixDmmH.In, MatrixDmmH.Out, MatrixDmmH.Slot, MatrixIpAddress, MatrixTcpBasePort).ConfigureAwait(false);
                }
                catch { }
            }
        }

        private async Task PowerOnAsync(CancellationToken token)
        {
            AddLog("组件供电：上电中...");

            await EnsurePowerSupply28VConnectedAsync(token).ConfigureAwait(false);
            try
            {
                await _powerSupply28V.ApplyAsync(PowerSupply28VCh1, Power28VVoltage, Power28VCurrentLimit, token).ConfigureAwait(false);
                await _powerSupply28V.SetOutputEnabledAsync(PowerSupply28VCh1, true, token).ConfigureAwait(false);
                await Task.Delay(300, token).ConfigureAwait(false);
                AddLog($"组件供电：28V 上电(程控电源1) CH1, IP={PowerSupply28VIpAddress}");
            }
            catch (Exception ex)
            {
                AddLog($"组件供电：28V 上电失败(程控电源1) CH1: {ex.Message}");
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
                if (_powerSupply28V != null)
                {
                    await _powerSupply28V.SetOutputEnabledAsync(PowerSupply28VCh1, false, token).ConfigureAwait(false);
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
            var sigOut = NormalizeSigOut(MatrixPointDcmV1Gnd.Out);

            AddLog($"矩阵路由: slot{MatrixSlotDmm} {MatrixDmmH.In}-{MatrixDmmH.Out} + slot{MatrixPointDcmV1Gnd.Slot} {MatrixPointDcmV1Gnd.In}-{sigOut} ({MatrixPointDcmV1Gnd.Name})");

            _matrixRoutedDmm = await matrix.ConnectNodesAsync(MatrixDmmH.In, MatrixDmmH.Out, MatrixDmmH.Slot, MatrixIpAddress, MatrixTcpBasePort).ConfigureAwait(false);
            _matrixRoutedSig = await matrix.ConnectNodesAsync(MatrixPointDcmV1Gnd.In, sigOut, MatrixPointDcmV1Gnd.Slot, MatrixIpAddress, MatrixTcpBasePort).ConfigureAwait(false);
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
                {
                    try
                    {
                        _ = await matrix.DisconnectNodesAsync(MatrixPointDcmV1Gnd.In, NormalizeSigOut(MatrixPointDcmV1Gnd.Out), MatrixPointDcmV1Gnd.Slot, MatrixIpAddress, MatrixTcpBasePort).ConfigureAwait(false);
                    }
                    catch { }
                }
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

            try { _manualTestLock.Dispose(); } catch { }
            try { _autoTestLock.Dispose(); } catch { }
            try { _opLock.Dispose(); } catch { }
            try { _instrumentLock.Dispose(); } catch { }
        }
    }
}
