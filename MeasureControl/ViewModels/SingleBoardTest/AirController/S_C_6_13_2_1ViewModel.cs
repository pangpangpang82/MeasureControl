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
using MeasureControl.Simulations.Common;
using MeasureControl.Simulations.S_C_6_13_2_1;

namespace MeasureControl.ViewModels.SingleBoardTest.AirController
{
    public sealed class S_C_6_13_2_1ViewModel : BindableBase, IDisposable
    {
        private static readonly byte[] EnterAtpCommand8 = { 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] EnterAtpOk8 = { 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x02 };
        private static readonly byte[] ExitAtpCommand8 = { 0x00, 0x02, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01 };
        private static readonly byte[] ExitAtpOk8 = { 0x00, 0x02, 0x00, 0x01, 0x00, 0x00, 0x00, 0x03 };

        private static readonly byte[] ScpMeaCommand8 = { 0x16, 0x01, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] ScpMeaResponsePrefix4 = { 0x16, 0x01, 0x01, 0x01 };

        private readonly S_C_6_13_2_1Simulation _simulation = new S_C_6_13_2_1Simulation();
        private readonly SemaphoreSlim _arincOpLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _manualTestLock = new SemaphoreSlim(1, 1);

        private string _testTxChannel;
        private string _testRxChannel;

        private string _enterAtpTxChannel;
        private string _enterAtpRxChannel;

        private string _scpMeaTxChannel;
        private string _scpMeaRxChannel;

        private string _exitAtpTxChannel;
        private string _exitAtpRxChannel;

        private string _enterAtpRxDataText;
        private string _scpMeaRxDataText;
        private string _pressureValueText;
        private string _exitAtpRxDataText;

        private string _lastTestTime;
        private string _lastTestResult;

        private bool _isManualTestRunning;
        private bool _isBusy;
        private bool _isInAtp;

        private double _arincRate = 100000.0;
        private int _currentTestPointIndex = 1;

        public S_C_6_13_2_1ViewModel()
        {
            TestTxChannel = "CH0";
            TestRxChannel = "CH1";

            EnterAtpTxChannel = TestTxChannel;
            EnterAtpRxChannel = TestRxChannel;
            ScpMeaTxChannel = TestTxChannel;
            ScpMeaRxChannel = TestRxChannel;
            ExitAtpTxChannel = TestTxChannel;
            ExitAtpRxChannel = TestRxChannel;

            EnterAtpRxDataText = "--";
            ScpMeaRxDataText = "--";
            PressureValueText = "--";
            ExitAtpRxDataText = "--";

            LastTestTime = "--";
            LastTestResult = "--";

            ManualTestCommand = new DelegateCommand(OnManualTest);
            SendEnterAtpCommand = new DelegateCommand(async () => await OnSendEnterAtpAsync());
            SendScpMeaCommand = new DelegateCommand(async () => await OnSendScpMeaAsync());
            SendExitAtpCommand = new DelegateCommand(async () => await OnSendExitAtpAsync());
            ClearLogCommand = new DelegateCommand(() => Logs.Clear());

            _simulation.GetCurrentTestPointIndex = () => CurrentTestPointIndex;
        }

        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();

        public DelegateCommand ManualTestCommand { get; }
        public DelegateCommand SendEnterAtpCommand { get; }
        public DelegateCommand SendScpMeaCommand { get; }
        public DelegateCommand SendExitAtpCommand { get; }
        public DelegateCommand ClearLogCommand { get; }

        public string TestTxChannel
        {
            get => _testTxChannel;
            set => SetProperty(ref _testTxChannel, value);
        }

        public string TestRxChannel
        {
            get => _testRxChannel;
            set => SetProperty(ref _testRxChannel, value);
        }

        public string EnterAtpTxChannel
        {
            get => _enterAtpTxChannel;
            set => SetProperty(ref _enterAtpTxChannel, value);
        }

        public string EnterAtpRxChannel
        {
            get => _enterAtpRxChannel;
            set => SetProperty(ref _enterAtpRxChannel, value);
        }

        public string ScpMeaTxChannel
        {
            get => _scpMeaTxChannel;
            set => SetProperty(ref _scpMeaTxChannel, value);
        }

        public string ScpMeaRxChannel
        {
            get => _scpMeaRxChannel;
            set => SetProperty(ref _scpMeaRxChannel, value);
        }

        public string ExitAtpTxChannel
        {
            get => _exitAtpTxChannel;
            set => SetProperty(ref _exitAtpTxChannel, value);
        }

        public string ExitAtpRxChannel
        {
            get => _exitAtpRxChannel;
            set => SetProperty(ref _exitAtpRxChannel, value);
        }

        public string EnterAtpRxDataText
        {
            get => _enterAtpRxDataText;
            set => SetProperty(ref _enterAtpRxDataText, value);
        }

        public string ScpMeaRxDataText
        {
            get => _scpMeaRxDataText;
            set => SetProperty(ref _scpMeaRxDataText, value);
        }

        public string PressureValueText
        {
            get => _pressureValueText;
            set => SetProperty(ref _pressureValueText, value);
        }

        public string ExitAtpRxDataText
        {
            get => _exitAtpRxDataText;
            set => SetProperty(ref _exitAtpRxDataText, value);
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

        public bool IsManualTestRunning
        {
            get => _isManualTestRunning;
            private set => SetProperty(ref _isManualTestRunning, value);
        }

        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (SetProperty(ref _isBusy, value))
                {
                    RaisePropertyChanged(nameof(CanEditStepControls));
                }
            }
        }

        public bool CanEditStepControls => !IsBusy;

        public bool IsInAtp
        {
            get => _isInAtp;
            private set => SetProperty(ref _isInAtp, value);
        }

        public double ArincRate
        {
            get => _arincRate;
            set => SetProperty(ref _arincRate, value);
        }

        public int CurrentTestPointIndex
        {
            get => _currentTestPointIndex;
            set
            {
                var v = Math.Max(1, Math.Min(3, value));
                SetProperty(ref _currentTestPointIndex, v);
            }
        }

        private void AddLog(string msg)
        {
            try
            {
                Application.Current?.Dispatcher?.Invoke(() => Logs.Add(msg));
            }
            catch
            {
                Logs.Add(msg);
            }
        }

        private void SetLastTestResult(string result)
        {
            LastTestTime = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            LastTestResult = result;
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
            await _manualTestLock.WaitAsync();
            try
            {
                if (IsBusy)
                    return;

                IsBusy = true;
                try
                {
                    EnsureManualArincChannels();

                    IsManualTestRunning = true;
                    IsInAtp = false;

                    EnterAtpRxDataText = "--";
                    ScpMeaRxDataText = "--";
                    PressureValueText = "--";
                    ExitAtpRxDataText = "--";

                    LastTestTime = "--";
                    LastTestResult = "--";

                    _simulation.IsRealProduct = AppConstants.Arinc429IsRealProduct;
                    _simulation.ArincRate = ArincRate;
                    _simulation.SimProductArincRate = ArincRate;

                    if (!_simulation.IsRealProduct)
                    {
                        if (!TrySetupSimChannelMapping(out var mapError))
                            throw new InvalidOperationException(mapError);
                    }

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试启动({(_simulation.IsRealProduct ? "真实产品模式" : "仿真模式")})：打开ARINC429");
                    await _simulation.StartAsync(TestTxChannel, TestRxChannel, msg => AddLog(msg));
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试已启动");
                }
                catch (Exception ex)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试启动失败: {ex.Message}");
                    IsManualTestRunning = false;
                }
                finally
                {
                    IsBusy = false;
                }
            }
            finally
            {
                _manualTestLock.Release();
            }
        }

        private async Task StopManualTestAsync()
        {
            await _manualTestLock.WaitAsync();
            try
            {
                if (IsBusy)
                    return;

                IsBusy = true;
                try
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试停止：释放ARINC429资源");
                    await _simulation.StopAsync(msg => AddLog(msg));
                    IsManualTestRunning = false;
                    IsInAtp = false;
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试已停止");
                }
                catch (Exception ex)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试停止异常: {ex.Message}");
                }
                finally
                {
                    IsBusy = false;
                }
            }
            finally
            {
                _manualTestLock.Release();
            }
        }

        private void EnsureManualArincChannels()
        {
            if (string.IsNullOrWhiteSpace(TestTxChannel))
                TestTxChannel = "CH0";
            if (string.IsNullOrWhiteSpace(TestRxChannel))
                TestRxChannel = "CH1";

            if (string.IsNullOrWhiteSpace(EnterAtpTxChannel))
                EnterAtpTxChannel = TestTxChannel;
            if (string.IsNullOrWhiteSpace(EnterAtpRxChannel))
                EnterAtpRxChannel = TestRxChannel;

            if (string.IsNullOrWhiteSpace(ScpMeaTxChannel))
                ScpMeaTxChannel = TestTxChannel;
            if (string.IsNullOrWhiteSpace(ScpMeaRxChannel))
                ScpMeaRxChannel = TestRxChannel;

            if (string.IsNullOrWhiteSpace(ExitAtpTxChannel))
                ExitAtpTxChannel = TestTxChannel;
            if (string.IsNullOrWhiteSpace(ExitAtpRxChannel))
                ExitAtpRxChannel = TestRxChannel;
        }

        private bool TrySetupSimChannelMapping(out string error)
        {
            error = null;

            int tx = ARINC429SimulationBase.ParseChannelIndex(TestTxChannel);
            int rx = ARINC429SimulationBase.ParseChannelIndex(TestRxChannel);

            if (tx < 0 || rx < 0)
            {
                error = $"测试通道无效：TX={TestTxChannel}, RX={TestRxChannel}";
                return false;
            }

            if (tx > 11 || rx > 11)
            {
                error = $"测试通道过大(需<=11，避免sim映射溢出)：TX={tx}, RX={rx}";
                return false;
            }

            _simulation.SimProductRxChannelIndex = tx + 4;
            _simulation.SimProductTxChannelIndex = rx + 4;
            return true;
        }

        private async Task OnSendEnterAtpAsync()
        {
            if (!IsManualTestRunning || IsBusy)
                return;

            await _arincOpLock.WaitAsync();
            try
            {
                IsBusy = true;
                try
                {
                    await _simulation.ClearRxFifoAsync(EnterAtpRxChannel);
                    await Task.Delay(20);

                    var token = CancellationToken.None;

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 步骤1：进入ATP模式");
                    await _simulation.SendBenchCommandOnlyAsync(EnterAtpTxChannel, EnterAtpCommand8, msg => AddLog(msg), token);

                    var resp = await _simulation.WaitBenchResponse8Async(
                        EnterAtpRxChannel,
                        b => b != null && b.SequenceEqual(EnterAtpOk8),
                        timeoutMs: 1200,
                        log: msg => AddLog(msg),
                        token: token);

                    if (resp == null)
                    {
                        AddLog($"[{DateTime.Now:HH:mm:ss}] 进入ATP超时");
                        SetLastTestResult("FAIL");
                        IsInAtp = false;
                        return;
                    }

                    EnterAtpRxDataText = "0x" + FormatBytes(resp);
                    IsInAtp = true;
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 进入ATP成功");
                }
                finally
                {
                    IsBusy = false;
                }
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 进入ATP异常: {ex.Message}");
                SetLastTestResult("FAIL");
            }
            finally
            {
                _arincOpLock.Release();
            }
        }

        private async Task OnSendScpMeaAsync()
        {
            if (!IsManualTestRunning || IsBusy)
                return;

            if (!IsInAtp)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 请先进入ATP模式");
                return;
            }

            await _arincOpLock.WaitAsync();
            try
            {
                IsBusy = true;
                try
                {
                    ScpMeaRxDataText = "--";
                    PressureValueText = "--";

                    var token = CancellationToken.None;

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 步骤2：发送测试指令S_CP_MEA读取压力值");
                    await _simulation.ClearRxFifoAsync(ScpMeaRxChannel);
                    await Task.Delay(20, token);

                    await _simulation.SendBenchCommandOnlyAsync(ScpMeaTxChannel, ScpMeaCommand8, msg => AddLog(msg), token);

                    var resp = await _simulation.WaitBenchResponse8Async(
                        ScpMeaRxChannel,
                        IsScpMeaResponse,
                        timeoutMs: 1500,
                        log: msg => AddLog(msg),
                        token: token);

                    if (resp == null)
                    {
                        AddLog($"[{DateTime.Now:HH:mm:ss}] 压力回采超时");
                        SetLastTestResult("FAIL");
                        return;
                    }

                    ScpMeaRxDataText = "0x" + FormatBytes(resp);

                    if (!TryParsePressureResponse(resp, out var valueMbar))
                    {
                        AddLog($"[{DateTime.Now:HH:mm:ss}] 压力值解析失败");
                        SetLastTestResult("FAIL");
                        return;
                    }

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 步骤3：显示压力值");
                    PressureValueText = valueMbar.ToString("0.####", CultureInfo.InvariantCulture);

                    var pass = IsPressureQualified(CurrentTestPointIndex, valueMbar);
                    SetLastTestResult(pass ? "PASS" : "FAIL");
                }
                finally
                {
                    IsBusy = false;
                }
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] S_CP_MEA异常: {ex.Message}");
                SetLastTestResult("FAIL");
            }
            finally
            {
                _arincOpLock.Release();
            }
        }

        private async Task OnSendExitAtpAsync()
        {
            if (!IsManualTestRunning || IsBusy)
                return;

            await _arincOpLock.WaitAsync();
            try
            {
                IsBusy = true;
                try
                {
                    await _simulation.ClearRxFifoAsync(ExitAtpRxChannel);
                    await Task.Delay(20);

                    var token = CancellationToken.None;

                    AddLog($"[{DateTime.Now:HH:mm:ss}] 步骤4：退出ATP模式");
                    await _simulation.SendBenchCommandOnlyAsync(ExitAtpTxChannel, ExitAtpCommand8, msg => AddLog(msg), token);

                    var resp = await _simulation.WaitBenchResponse8Async(
                        ExitAtpRxChannel,
                        b => b != null && b.SequenceEqual(ExitAtpOk8),
                        timeoutMs: 1200,
                        log: msg => AddLog(msg),
                        token: token);

                    if (resp == null)
                    {
                        AddLog($"[{DateTime.Now:HH:mm:ss}] 退出ATP超时");
                        SetLastTestResult("FAIL");
                        return;
                    }

                    ExitAtpRxDataText = "0x" + FormatBytes(resp);
                    IsInAtp = false;
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 退出ATP成功");
                }
                finally
                {
                    IsBusy = false;
                }
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 退出ATP异常: {ex.Message}");
                SetLastTestResult("FAIL");
            }
            finally
            {
                _arincOpLock.Release();
            }
        }

        private static bool IsScpMeaResponse(byte[] resp8)
        {
            if (resp8 == null || resp8.Length != 8)
                return false;
            return resp8.Take(4).SequenceEqual(ScpMeaResponsePrefix4);
        }

        private static bool TryParsePressureResponse(byte[] resp8, out float valueMbar)
        {
            valueMbar = default;

            if (!IsScpMeaResponse(resp8))
                return false;

            var raw = new byte[4];
            Array.Copy(resp8, 4, raw, 0, 4);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(raw);
            valueMbar = BitConverter.ToSingle(raw, 0);

            if (float.IsNaN(valueMbar) || float.IsInfinity(valueMbar))
                return false;

            return true;
        }

        private static bool IsPressureQualified(int pointIndex, float valueMbar)
        {
            var (min, max) = pointIndex switch
            {
                1 => (1082f, 1118f),
                2 => (1482f, 1518f),
                3 => (1982f, 2018f),
                _ => (1082f, 1118f)
            };

            return valueMbar >= min && valueMbar <= max;
        }

        private static string FormatBytes(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                return string.Empty;
            return string.Join(" ", bytes.Select(b => b.ToString("X2")));
        }

        public void Dispose()
        {
            try { _simulation?.StopAsync(_ => { }).GetAwaiter().GetResult(); } catch { }
            try { _arincOpLock?.Dispose(); } catch { }
            try { _manualTestLock?.Dispose(); } catch { }
        }
    }
}
