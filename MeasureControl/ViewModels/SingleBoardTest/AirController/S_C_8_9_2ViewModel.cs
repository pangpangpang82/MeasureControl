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
        private string _step3Result = "--";

        // PH高电平测量结果
        private string _phHighV1GndVoltage = "--";
        private string _phHighV1GndResult = "--";
        private string _phHighV2GndVoltage = "--";
        private string _phHighV2GndResult = "--";
        private string _phHighV1V2Voltage = "--";
        private string _phHighV1V2Result = "--";

        // PH低电平测量结果
        private string _phLowV1GndVoltage = "--";
        private string _phLowV1GndResult = "--";
        private string _phLowV2GndVoltage = "--";
        private string _phLowV2GndResult = "--";
        private string _phLowV1V2Voltage = "--";
        private string _phLowV1V2Result = "--";

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

        // PH高电平测量结果
        public string PhHighV1GndVoltage { get => _phHighV1GndVoltage; private set => SetProperty(ref _phHighV1GndVoltage, value); }
        public string PhHighV1GndResult { get => _phHighV1GndResult; private set => SetProperty(ref _phHighV1GndResult, value); }
        public string PhHighV2GndVoltage { get => _phHighV2GndVoltage; private set => SetProperty(ref _phHighV2GndVoltage, value); }
        public string PhHighV2GndResult { get => _phHighV2GndResult; private set => SetProperty(ref _phHighV2GndResult, value); }
        public string PhHighV1V2Voltage { get => _phHighV1V2Voltage; private set => SetProperty(ref _phHighV1V2Voltage, value); }
        public string PhHighV1V2Result { get => _phHighV1V2Result; private set => SetProperty(ref _phHighV1V2Result, value); }

        public string Step3Result { get => _step3Result; private set => SetProperty(ref _step3Result, value); }

        // PH低电平测量结果
        public string PhLowV1GndVoltage { get => _phLowV1GndVoltage; private set => SetProperty(ref _phLowV1GndVoltage, value); }
        public string PhLowV1GndResult { get => _phLowV1GndResult; private set => SetProperty(ref _phLowV1GndResult, value); }
        public string PhLowV2GndVoltage { get => _phLowV2GndVoltage; private set => SetProperty(ref _phLowV2GndVoltage, value); }
        public string PhLowV2GndResult { get => _phLowV2GndResult; private set => SetProperty(ref _phLowV2GndResult, value); }
        public string PhLowV1V2Voltage { get => _phLowV1V2Voltage; private set => SetProperty(ref _phLowV1V2Voltage, value); }
        public string PhLowV1V2Result { get => _phLowV1V2Result; private set => SetProperty(ref _phLowV1V2Result, value); }

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
                PhHighV1GndVoltage = "--";
                PhHighV1GndResult = "--";
                PhHighV2GndVoltage = "--";
                PhHighV2GndResult = "--";
                PhHighV1V2Voltage = "--";
                PhHighV1V2Result = "--";
                Step3Result = "--";
                PhLowV1GndVoltage = "--";
                PhLowV1GndResult = "--";
                PhLowV2GndVoltage = "--";
                PhLowV2GndResult = "--";
                PhLowV1V2Voltage = "--";
                PhLowV1V2Result = "--";
                LastTestTime = "--";
                OverallResult = "--";
            });
        }

        private string[] AllResultValues => new[]
        {
            Step1Result,
            PhHighV1GndResult, PhHighV2GndResult, PhHighV1V2Result,
            Step3Result,
            PhLowV1GndResult, PhLowV2GndResult, PhLowV1V2Result
        };

        private void UpdateOverallIfComplete()
        {
            var allDone = AllResultValues.All(IsResultDone);
            if (!allDone)
                return;

            var pass = AllResultValues.All(v => v == "PASS");
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

            var allDone = AllResultValues.All(IsResultDone);
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

                var allDone = AllResultValues.All(IsResultDone);
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
            await StopTestAsync().ConfigureAwait(false);

            if (addStoppedLog)
                AddLog("========== 手动测试已停止 ==========");
        }

        /// <summary>
        /// 统一停止方法：无论测试是否完成，都断开FPGA、断开DMM、断开矩阵、下电
        /// </summary>
        private async Task StopTestAsync()
        {
            try { _autoTestCts?.Cancel(); } catch { }

            IsManualTestRunning = false;
            IsAutoTestRunning = false;
            IsBusy = false;

            try { await CleanupInstrumentsAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
            try { await DisconnectFpgaAsync(CancellationToken.None).ConfigureAwait(false); } catch { }

            try
            {
                if (IsPowerOn)
                    await PowerOffAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch { }
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

                    await Task.Delay(1500, token).ConfigureAwait(false);
                    var highResult = await MeasureThreePointsAsync(isPhHigh: true, token).ConfigureAwait(false);
                    UpdatePhMeasurementResults(isPhHigh: true, highResult);

                    var ok3 = await SendTestCommandAsync(isPhHigh: false, token).ConfigureAwait(false);
                    Step3Result = ok3 ? "PASS" : "FAIL";

                    await Task.Delay(1500, token).ConfigureAwait(false);
                    var lowResult = await MeasureThreePointsAsync(isPhHigh: false, token).ConfigureAwait(false);
                    UpdatePhMeasurementResults(isPhHigh: false, lowResult);

                    UpdateOverallIfComplete();

                    AddLog($"========== 自动测试完成: {OverallResult} ==========");
                }
                catch (OperationCanceledException)
                {
                    AddLog("自动测试已取消");
                    await StopTestAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    AddLog($"自动测试异常: {ex.Message}");
                    await StopTestAsync().ConfigureAwait(false);
                }
                finally
                {
                    await StopTestAsync().ConfigureAwait(false);

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
                        var result = await MeasureThreePointsAsync(isPhHigh: isFirst, CancellationToken.None).ConfigureAwait(false);
                        UpdatePhMeasurementResults(isPhHigh: isFirst, result);
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

        private void UpdatePhMeasurementResults(bool isPhHigh, (double? V1Gnd, double? V2Gnd, double? V1V2, bool V1GndPass, bool V2GndPass, bool V1V2Pass, string V1GndReason, string V2GndReason, string V1V2Reason) result)
        {
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                string F(double? v) => v.HasValue ? $"{v.Value:F3}V" : "--";

                if (isPhHigh)
                {
                    PhHighV1GndVoltage = F(result.V1Gnd);
                    PhHighV1GndResult = result.V1GndPass ? "PASS" : $"FAIL({result.V1GndReason})";
                    PhHighV2GndVoltage = F(result.V2Gnd);
                    PhHighV2GndResult = result.V2GndPass ? "PASS" : $"FAIL({result.V2GndReason})";
                    PhHighV1V2Voltage = F(result.V1V2);
                    PhHighV1V2Result = result.V1V2Pass ? "PASS" : $"FAIL({result.V1V2Reason})";
                }
                else
                {
                    PhLowV1GndVoltage = F(result.V1Gnd);
                    PhLowV1GndResult = result.V1GndPass ? "PASS" : $"FAIL({result.V1GndReason})";
                    PhLowV2GndVoltage = F(result.V2Gnd);
                    PhLowV2GndResult = result.V2GndPass ? "PASS" : $"FAIL({result.V2GndReason})";
                    PhLowV1V2Voltage = F(result.V1V2);
                    PhLowV1V2Result = result.V1V2Pass ? "PASS" : $"FAIL({result.V1V2Reason})";
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
            // 按顺序检查所有测量点的失败原因
            if (TryExtractFailPoint(PhHighV1GndResult, out var p1)) return $"PH高V1对地:{p1}";
            if (TryExtractFailPoint(PhHighV2GndResult, out var p2)) return $"PH高V2对地:{p2}";
            if (TryExtractFailPoint(PhHighV1V2Result, out var p3)) return $"PH高V1V2:{p3}";
            if (TryExtractFailPoint(PhLowV1GndResult, out var p4)) return $"PH低V1对地:{p4}";
            if (TryExtractFailPoint(PhLowV2GndResult, out var p5)) return $"PH低V2对地:{p5}";
            if (TryExtractFailPoint(PhLowV1V2Result, out var p6)) return $"PH低V1V2:{p6}";

            if (Step1Result == "FAIL") return "PH高指令发送";
            if (Step3Result == "FAIL") return "PH低指令发送";

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

        private async Task<(double? V1Gnd, double? V2Gnd, double? V1V2, bool V1GndPass, bool V2GndPass, bool V1V2Pass, string V1GndReason, string V2GndReason, string V1V2Reason)> MeasureThreePointsAsync(bool isPhHigh, CancellationToken token)
        {
            await _instrumentLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                var matrix = MatrixControlService.Instance;

                // DMM路由全程保持连接，避免断开重连时DMM返回缓存值
                AddLog($"矩阵路由(DMM): slot{MatrixSlotDmm} {MatrixDmmH.In}-{MatrixDmmH.Out}");
                var okDmm = await matrix.ConnectNodesAsync(MatrixDmmH.In, MatrixDmmH.Out, MatrixDmmH.Slot, MatrixIpAddress, MatrixTcpBasePort).ConfigureAwait(false);
                if (!okDmm)
                {
                    AddLog("DMM矩阵路由失败");
                    return (null, null, null, true, true, false, null, null, "DMM矩阵路由失败");
                }

                try
                {
                    // 测量V1对地
                    var r1 = await MeasureSignalPointAsync(matrix, MatrixPointDcmV1Gnd, token).ConfigureAwait(false);
                    // 测量V2对地
                    var r2 = await MeasureSignalPointAsync(matrix, MatrixPointDcmV2Gnd, token).ConfigureAwait(false);
                    // 测量V1V2
                    var r3 = await MeasureSignalPointAsync(matrix, MatrixPointDcmV1V1, token).ConfigureAwait(false);

                    // V1Gnd、V2Gnd只显示不判断，V1V2为合格判据
                    // PH高电平：V1V2 > 0
                    // PH低电平：V1V2 < 0
                    var v1Pass = true; string reason1 = null;
                    var v2Pass = true; string reason2 = null;
                    var v12Pass = IsV1V2Pass(r3.Ok, r3.Voltage, isPhHigh, out var reason3);

                    if (!v12Pass) AddLog($"{MatrixPointDcmV1V1.Name} FAIL: {reason3}");

                    return (r1.Voltage, r2.Voltage, r3.Voltage, v1Pass, v2Pass, v12Pass, reason1, reason2, reason3);
                }
                finally
                {
                    // 最后断开DMM路由
                    try { await matrix.DisconnectNodesAsync(MatrixDmmH.In, MatrixDmmH.Out, MatrixDmmH.Slot, MatrixIpAddress, MatrixTcpBasePort).ConfigureAwait(false); } catch { }
                }
            }
            finally
            {
                _instrumentLock.Release();
            }
        }

        /// <summary>
        /// 单信号点测量：连接信号路由 - flush read清缓存 - 等待稳定 - 正式读数 - 断开信号路由
        /// DMM路由由调用方保持连接
        /// </summary>
        private async Task<(bool Ok, double? Voltage)> MeasureSignalPointAsync(MatrixControlService matrix, (string Name, string In, string Out, int Slot) sigPoint, CancellationToken token)
        {
            var sigOut = NormalizeSigOut(sigPoint.Out);
            bool okSig = false;
            try
            {
                AddLog($"矩阵路由(信号): slot{sigPoint.Slot} {sigPoint.In}-{sigOut} ({sigPoint.Name})");
                okSig = await matrix.ConnectNodesAsync(sigPoint.In, sigOut, sigPoint.Slot, MatrixIpAddress, MatrixTcpBasePort).ConfigureAwait(false);
                if (!okSig)
                    return (false, null);

                // flush read：丢弃DMM可能缓存的旧值
                await Task.Delay(300, token).ConfigureAwait(false);
                _ = await DmmReadVoltageAsync(token).ConfigureAwait(false);

                // 等待信号稳定后正式读数
                await Task.Delay(1000, token).ConfigureAwait(false);
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
            }
        }

        /// <summary>
        /// V1V2合格判据：
        /// PH高电平：V1V2 > 0
        /// PH低电平：V1V2 < 0
        /// </summary>
        private static bool IsV1V2Pass(bool ok, double? voltage, bool isPhHigh, out string reason)
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
            if (isPhHigh)
            {
                if (v <= 0)
                {
                    reason = $"PH高V1V2应>0V: {v:F3}V";
                    return false;
                }
            }
            else
            {
                if (v >= 0)
                {
                    reason = $"PH低V1V2应<0V: {v:F3}V";
                    return false;
                }
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

                await Task.Delay(1500, token).ConfigureAwait(false);
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
