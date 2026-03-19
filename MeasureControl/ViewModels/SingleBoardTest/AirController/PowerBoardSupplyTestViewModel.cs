using MeasureControl.Services.HardwareApis;
using Prism.Commands;
using Prism.Mvvm;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace MeasureControl.ViewModels.SingleBoardTest.AirController
{
    public sealed class PowerBoardSupplyTestViewModel : BindableBase, IDisposable
    {
        private const string DefaultPowerSupplyIpAddress = "192.168.1.15";
        private const double DefaultCurrentLimitA = 3.0;
        private const double ReadbackMin = 2.375;
        private const double ReadbackMax = 2.625;
        private readonly SemaphoreSlim _manualTestLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _autoTestLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _opLock = new SemaphoreSlim(1, 1);

        private CancellationTokenSource _autoTestCts;

        private IPowerSupplyApi _powerSupply;
        private readonly SemaphoreSlim _powerSupplyLock = new SemaphoreSlim(1, 1);

        private string _title = "测试";
        private string _testPointsText;
        private string _controlChannel = "A";
        private PowerSupplyChannel _supplyChannel = PowerSupplyChannel.CH1;

        private bool _isManualTestRunning;
        private bool _isAutoTestRunning;
        private bool _isBusy;
        private string _powerStatus = "未供电";
        private string _lastTestTime = "--";
        private string _lastTestResult = "--";

        private string _powerSupplyIpAddress = DefaultPowerSupplyIpAddress;

        private string _supply32VMeasuredCurrentText = "--";

        private string _supply28VMeasuredCurrentText = "--";

        public PowerBoardSupplyTestViewModel()
        {
            ManualTestCommand = new DelegateCommand(OnManualTest, () => !IsAutoTestRunning && (!IsBusy || IsManualTestRunning));
            AutoTestCommand = new DelegateCommand(OnAutoTest, () => !IsManualTestRunning && (!IsBusy || IsAutoTestRunning));
            ClearLogCommand = new DelegateCommand(() => Logs.Clear());

            Supply32VCommand = new DelegateCommand(async () => await SupplyAndUpdateAsync(32.0, is32V: true));
            ReadVbit32VCommand = new DelegateCommand(async () => await ReadCurrentAndUpdateAsync(is32V: true));
            PowerOffCommand = new DelegateCommand(async () => await PowerOffAndUpdateAsync());
            Supply28VCommand = new DelegateCommand(async () => await SupplyAndUpdateAsync(28.0, is32V: false));
            ReadVbit28VCommand = new DelegateCommand(async () => await ReadCurrentAndUpdateAsync(is32V: false));

            Configure("A", "7.2.1A控制通道功率板供电测试");
        }

        public void Configure(string channel, string title)
        {
            Title = string.IsNullOrWhiteSpace(title) ? "测试" : title;

            _controlChannel = string.Equals(channel, "B", StringComparison.OrdinalIgnoreCase) ? "B" : "A";
            var isA = string.Equals(_controlChannel, "A", StringComparison.OrdinalIgnoreCase);
            _supplyChannel = isA ? PowerSupplyChannel.CH1 : PowerSupplyChannel.CH2;

            TestPointsText = isA
                ? "测试点：J126-J128、J127-J129、J189-J191、J190-J192"
                : "测试点：J3-J5、J4-J6、J65-J67、J66-J68、J63-J62、J125-J124、J188-J187、J250-J249";
        }

        public string Title
        {
            get => _title;
            set => SetProperty(ref _title, value);
        }

        public string TestPointsText
        {
            get => _testPointsText;
            set => SetProperty(ref _testPointsText, value);
        }

        public string PowerSupplyIpAddress
        {
            get => _powerSupplyIpAddress;
            set => SetProperty(ref _powerSupplyIpAddress, value);
        }

        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();

        public DelegateCommand ManualTestCommand { get; }
        public DelegateCommand AutoTestCommand { get; }
        public DelegateCommand ClearLogCommand { get; }

        public DelegateCommand Supply32VCommand { get; }
        public DelegateCommand ReadVbit32VCommand { get; }
        public DelegateCommand PowerOffCommand { get; }
        public DelegateCommand Supply28VCommand { get; }
        public DelegateCommand ReadVbit28VCommand { get; }

        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (SetProperty(ref _isBusy, value))
                    RaiseAllCanExecuteChanged();
            }
        }

        public string PowerStatus
        {
            get => _powerStatus;
            private set => SetProperty(ref _powerStatus, value);
        }

        public string Supply32VMeasuredCurrentText
        {
            get => _supply32VMeasuredCurrentText;
            private set => SetProperty(ref _supply32VMeasuredCurrentText, value);
        }

        public string Supply28VMeasuredCurrentText
        {
            get => _supply28VMeasuredCurrentText;
            private set => SetProperty(ref _supply28VMeasuredCurrentText, value);
        }

        public bool IsManualTestRunning
        {
            get => _isManualTestRunning;
            private set
            {
                if (SetProperty(ref _isManualTestRunning, value))
                {
                    if (value)
                        IsAutoTestRunning = false;
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
                    if (value)
                        IsManualTestRunning = false;
                    RaiseAllCanExecuteChanged();
                }
            }
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

        private void RaiseAllCanExecuteChanged()
        {
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                ManualTestCommand.RaiseCanExecuteChanged();
                AutoTestCommand.RaiseCanExecuteChanged();
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

        private void OnAutoTest()
        {
            if (IsAutoTestRunning)
            {
                try { _autoTestCts?.Cancel(); } catch { }
                AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试停止请求");
                return;
            }

            _ = StartAutoTestAsync();
        }

        private async Task StartManualTestAsync()
        {
            await _manualTestLock.WaitAsync();
            try
            {
                if (IsManualTestRunning)
                    return;

                IsManualTestRunning = true;
                IsBusy = true;
                LastTestTime = "--";
                LastTestResult = "--";

                ClearResults();
                AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试启动：打开设备 (通道{_controlChannel}, 电源{PowerSupplyIpAddress} CH{(int)_supplyChannel})");

                await EnsurePowerSupplyConnectedAsync(CancellationToken.None).ConfigureAwait(false);

                AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试启动：设备已就绪");
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试启动异常：{ex.Message}");
                IsManualTestRunning = false;
                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            }
            finally
            {
                IsBusy = false;
                _manualTestLock.Release();
            }
        }

        private async Task StopManualTestAsync()
        {
            await _manualTestLock.WaitAsync();
            try
            {
                if (!IsManualTestRunning)
                    return;

                AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试停止：关闭设备");
                IsManualTestRunning = false;

                try { await PowerSupplyOutputOffAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                try { await DisconnectPowerSupplyAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                PowerStatus = "未供电";

                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试停止异常：{ex.Message}");
                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            }
            finally
            {
                _manualTestLock.Release();
            }
        }

        private async Task StartAutoTestAsync()
        {
            await _autoTestLock.WaitAsync();
            try
            {
                if (IsAutoTestRunning)
                    return;

                IsAutoTestRunning = true;
                IsBusy = true;
                LastTestTime = "--";
                LastTestResult = "--";

                ClearResults();

                _autoTestCts?.Dispose();
                _autoTestCts = new CancellationTokenSource();
                var token = _autoTestCts.Token;

                AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试启动");
                await RunAutoTestAsync(token).ConfigureAwait(false);
            }
            finally
            {
                _autoTestLock.Release();
            }
        }

        private async Task RunAutoTestAsync(CancellationToken token)
        {
            try
            {
                token.ThrowIfCancellationRequested();

                AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试：开始打开设备");
                await EnsurePowerSupplyConnectedAsync(token).ConfigureAwait(false);

                AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试：向控制通道{_controlChannel}供电 32V");
                await SupplyAndUpdateCoreAsync(32.0, is32V: true, token).ConfigureAwait(false);

                token.ThrowIfCancellationRequested();
                var vbit32Ok = await ReadCurrentAndUpdateCoreAsync(is32V: true, token).ConfigureAwait(false);

                token.ThrowIfCancellationRequested();
                AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试：断开控制通道{_controlChannel}供电");
                await PowerOffAndUpdateCoreAsync(token).ConfigureAwait(false);

                token.ThrowIfCancellationRequested();
                AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试：向控制通道{_controlChannel}供电 28V");
                await SupplyAndUpdateCoreAsync(28.0, is32V: false, token).ConfigureAwait(false);

                token.ThrowIfCancellationRequested();
                var vbit28Ok = await ReadCurrentAndUpdateCoreAsync(is32V: false, token).ConfigureAwait(false);

                var overallOk = vbit32Ok && vbit28Ok;
                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                LastTestResult = overallOk ? "PASS" : "FAIL";
                AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试完成：{LastTestResult}");
            }
            catch (OperationCanceledException)
            {
                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                LastTestResult = "已停止";
                AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试已停止");
            }
            catch (Exception ex)
            {
                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                LastTestResult = "异常";
                AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试异常：{ex.Message}");
            }
            finally
            {
                try { await PowerSupplyOutputOffAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                try { await DisconnectPowerSupplyAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                IsAutoTestRunning = false;
                IsBusy = false;
            }
        }

        private void ClearResults()
        {
            Supply32VMeasuredCurrentText = "--";
            Supply28VMeasuredCurrentText = "--";

            PowerStatus = "未供电";
        }

        private bool EnsureManualStepAllowed()
        {
            if (!IsManualTestRunning)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 请先点击“手动测试”启动设备");
                return false;
            }

            if (IsAutoTestRunning)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试进行中，禁止手动步骤");
                return false;
            }

            return true;
        }

        private async Task SupplyAndUpdateAsync(double voltage, bool is32V)
        {
            if (!EnsureManualStepAllowed())
                return;

            if (!await _opLock.WaitAsync(0).ConfigureAwait(false))
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 操作进行中，请稍后再试");
                return;
            }

            try
            {
                IsBusy = true;
                await SupplyAndUpdateCoreAsync(voltage, is32V, CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                IsBusy = false;
                _opLock.Release();
            }
        }

        private async Task SupplyAndUpdateCoreAsync(double voltage, bool is32V, CancellationToken token)
        {
            await EnsurePowerSupplyConnectedAsync(token).ConfigureAwait(false);
            await PowerSupplyApplyAsync(voltage, DefaultCurrentLimitA, token).ConfigureAwait(false);

            Application.Current?.Dispatcher?.Invoke(() =>
            {
                PowerStatus = $"已供电 {voltage:0.###}V";
            });
        }

        private async Task PowerOffAndUpdateAsync()
        {
            if (!EnsureManualStepAllowed())
                return;

            if (!await _opLock.WaitAsync(0).ConfigureAwait(false))
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 操作进行中，请稍后再试");
                return;
            }

            try
            {
                IsBusy = true;
                await PowerOffAndUpdateCoreAsync(CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                IsBusy = false;
                _opLock.Release();
            }
        }

        private async Task PowerOffAndUpdateCoreAsync(CancellationToken token)
        {
            await PowerSupplyOutputOffAsync(token).ConfigureAwait(false);
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                PowerStatus = "未供电";
            });
        }

        private async Task ReadCurrentAndUpdateAsync(bool is32V)
        {
            if (!EnsureManualStepAllowed())
                return;

            if (!await _opLock.WaitAsync(0).ConfigureAwait(false))
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 操作进行中，请稍后再试");
                return;
            }

            try
            {
                IsBusy = true;
                _ = await ReadCurrentAndUpdateCoreAsync(is32V, CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                IsBusy = false;
                _opLock.Release();
            }
        }

        private async Task<bool> ReadCurrentAndUpdateCoreAsync(bool is32V, CancellationToken token)
        {
            AddLog($"[{DateTime.Now:HH:mm:ss}] 读取二次电源回采值(电流)");
            await Task.Delay(200, token).ConfigureAwait(false);

            var ms = await PowerSupplyReadMeasurementsAsync(token).ConfigureAwait(false);
            var current = ms?.Current?.Value;
            var cText = current != null ? $"{current.Value:0.000} A" : "--";

            Application.Current?.Dispatcher?.Invoke(() =>
            {
                if (is32V)
                    Supply32VMeasuredCurrentText = cText;
                else
                    Supply28VMeasuredCurrentText = cText;
            });

            if (current == null)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 电流回采失败");
                return false;
            }

            var ok = current.Value >= ReadbackMin && current.Value <= ReadbackMax;
            AddLog($"[{DateTime.Now:HH:mm:ss}] 判定：电流={cText} {(ok ? "PASS" : "FAIL")} (范围[{ReadbackMin:0.###},{ReadbackMax:0.###}])");
            return ok;
        }

        private void AddLog(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            try
            {
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher != null && !dispatcher.CheckAccess())
                {
                    dispatcher.BeginInvoke(new Action(() => Logs.Add(message)));
                }
                else
                {
                    Logs.Add(message);
                }
            }
            catch
            {
            }

            try
            {
                Debug.WriteLine(message);
            }
            catch
            {
            }
        }

        private async Task EnsurePowerSupplyConnectedAsync(CancellationToken token)
        {
            await _powerSupplyLock.WaitAsync(token);
            try
            {
                if (_powerSupply != null && _powerSupply.IsConnected)
                    return;

                if (_powerSupply != null)
                {
                    try { await _powerSupply.DisposeAsync(); } catch { }
                    _powerSupply = null;
                }

                _powerSupply = new PowerSupplySocketApi();
                await _powerSupply.ConnectAsync(PowerSupplyIpAddress, token);
                AddLog($"[{DateTime.Now:HH:mm:ss}] 程控电源已连接：{PowerSupplyIpAddress}");
            }
            finally
            {
                _powerSupplyLock.Release();
            }
        }

        private async Task DisconnectPowerSupplyAsync(CancellationToken token)
        {
            await _powerSupplyLock.WaitAsync(token);
            try
            {
                if (_powerSupply == null)
                    return;

                try { await _powerSupply.DisconnectAsync(token); } catch { }
                try { await _powerSupply.DisposeAsync(); } catch { }
                _powerSupply = null;
                AddLog($"[{DateTime.Now:HH:mm:ss}] 程控电源已断开");
            }
            finally
            {
                _powerSupplyLock.Release();
            }
        }

        private async Task PowerSupplyApplyAsync(double voltage, double currentLimit, CancellationToken token)
        {
            await EnsurePowerSupplyConnectedAsync(token);

            await _powerSupply.ApplyAsync(_supplyChannel, voltage, currentLimit, token);
            await _powerSupply.SetOutputEnabledAsync(_supplyChannel, true, token);
        }

        private async Task PowerSupplyOutputOffAsync(CancellationToken token)
        {
            if (_powerSupply == null || !_powerSupply.IsConnected)
                return;

            await _powerSupply.SetOutputEnabledAsync(_supplyChannel, false, token);
        }

        private async Task<PowerSupplyMeasurements> PowerSupplyReadMeasurementsAsync(CancellationToken token)
        {
            if (_powerSupply == null || !_powerSupply.IsConnected)
                return null;

            return await _powerSupply.ReadMeasurementsAsync(_supplyChannel, options: null, cancellationToken: token);
        }

        public void Dispose()
        {
            try { _autoTestCts?.Cancel(); } catch { }
            try { _autoTestCts?.Dispose(); } catch { }
            _autoTestCts = null;

            try { PowerSupplyOutputOffAsync(CancellationToken.None).GetAwaiter().GetResult(); } catch { }
            try { DisconnectPowerSupplyAsync(CancellationToken.None).GetAwaiter().GetResult(); } catch { }

            try { _manualTestLock.Dispose(); } catch { }
            try { _autoTestLock.Dispose(); } catch { }
            try { _opLock.Dispose(); } catch { }
            try { _powerSupplyLock.Dispose(); } catch { }
        }
    }
}
