using Prism.Commands;
using Prism.Mvvm;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using MeasureControl.Services;
using MeasureControl.Services.HardwareApis;
using MeasureControl.Models;
using MeasureControl.Models.Devices;

namespace MeasureControl.ViewModels.SingleBoardTest.AirController
{
    public sealed class PowerToGroundImpedanceTestViewModel : BindableBase, IDisposable
    {
        private const string DmmIpAddress = "192.168.1.13";
        private const string MatrixIpAddress = "192.168.1.3";

        private const string RelayPowerSupplyIpAddress = "192.168.1.16";
        private const PowerSupplyChannel RelayPowerSupplyChannel = PowerSupplyChannel.CH1;
        private const double RelayVoltage = 5.0;
        private const double RelayCurrentLimit = 1.0;

        private const string RelayControlChannel1 = "DO17";
        private const string RelayControlChannel2 = "DO18";

        private const int DefaultTimeoutMs = 3000;
        private const int DmmTimeoutMs = 8000;
        private const int RelayTimeoutMs = 2000;
        private const int RelayPowerTimeoutMs = 2000;

        private const int MatrixTcpBasePort = 50200;
        private const int MatrixSlotSequence = 6;
        private const int MatrixSlotCommon = 4;

        private const double PassThresholdOhm = 200.0;

        private readonly IPxiChassisService _pxiChassisService;
        private readonly IComponentPowerStateApi _componentPowerStateApi;
        private readonly IDmmApi _dmm;
        private readonly SemaphoreSlim _measureLock = new SemaphoreSlim(1, 1);

        private CancellationTokenSource _opCts;
        private bool _hardwareInitialized;
        private IJy7131Api _jy7131Api;

        private IPowerSupplyApi _relayPowerSupply;
        private bool _relaySupplyOn;

        private bool _isManualTestRunning;
        private bool _isAutoTestRunning;
        private bool _isBusy;
        private bool _isRelayActivated;

        private string _overallResult = "--";
        private string _lastTestTime = "--";

        public PowerToGroundImpedanceTestViewModel(
            IPxiChassisService pxiChassisService,
            IComponentPowerStateApi componentPowerStateApi,
            IDmmApi dmm)
        {
            _pxiChassisService = pxiChassisService;
            _componentPowerStateApi = componentPowerStateApi;
            _dmm = dmm;

            ManualTestCommand = new DelegateCommand(OnManualTest);
            AutoTestCommand = new DelegateCommand(OnAutoTest);
            ClearLogCommand = new DelegateCommand(() => Logs.Clear());

            Items.Add(new ImpedanceItemViewModel(this,8, "28V_DC_BUS1", "输入", "J189，J190", "J126，J127"));
            Items.Add(new ImpedanceItemViewModel(this, 9, "+15V", "输出", "J222", "J209"));
            Items.Add(new ImpedanceItemViewModel(this, 10, "+5V", "输出", "J160", "J146"));
            Items.Add(new ImpedanceItemViewModel(this, 11, "+1.9V", "输出", "J223", "J242"));
            Items.Add(new ImpedanceItemViewModel(this, 12, "+1.5V", "输出", "J98", "J118"));
            Items.Add(new ImpedanceItemViewModel(this, 13, "+3.3V", "输出", "J97，J35", "J118,J55"));
        }

        public ObservableCollection<ImpedanceItemViewModel> Items { get; } = new ObservableCollection<ImpedanceItemViewModel>();

        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();

        public DelegateCommand ManualTestCommand { get; }

        public DelegateCommand AutoTestCommand { get; }

        public DelegateCommand ClearLogCommand { get; }

        public bool IsRelayActivated
        {
            get => _isRelayActivated;
            private set
            {
                if (SetProperty(ref _isRelayActivated, value))
                {
                    RaiseCanExecuteChangedForItems();
                }
            }
        }

        public bool IsManualTestRunning
        {
            get => _isManualTestRunning;
            private set
            {
                if (SetProperty(ref _isManualTestRunning, value))
                {
                    RaiseCanExecuteChangedForItems();
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
                    RaiseCanExecuteChangedForItems();
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
                    RaiseCanExecuteChangedForItems();
                }
            }
        }

        public string OverallResult
        {
            get => _overallResult;
            private set => SetProperty(ref _overallResult, value);
        }

        public string LastTestTime
        {
            get => _lastTestTime;
            private set => SetProperty(ref _lastTestTime, value);
        }

        private void OnManualTest()
        {
            if (IsManualTestRunning)
            {
                StopTest();
                return;
            }

            if (IsAutoTestRunning)
            {
                StopTest();
                return;
            }

            StartManualTest();
        }

        private void OnAutoTest()
        {
            if (IsAutoTestRunning)
            {
                StopTest();
                return;
            }

            if (IsManualTestRunning)
            {
                StopTest();
                return;
            }

            StartAutoTest();
        }

        private void StartManualTest()
        {
            _opCts?.Cancel();
            _opCts?.Dispose();
            _opCts = new CancellationTokenSource();

            ResetResults();

            IsManualTestRunning = true;
            IsAutoTestRunning = false;
            IsRelayActivated = false;
            OverallResult = "--";
            LastTestTime = "--";

            AddLog("手动测试开始");

            Task.Run(async () =>
            {
                var token = _opCts.Token;
                try
                {
                    AddLog("步骤1: 初始化硬件设备（7131板卡、万用表、矩阵）...");
                    await InitializeHardwareWithTimeoutAsync(token);
                    if (token.IsCancellationRequested) return;

                    AddLog("步骤1.5: 继电器供电上电（5V）...");
                    await PowerOnRelaySupplyWithTimeoutAsync(token);
                    if (token.IsCancellationRequested) return;

                    AddLog("步骤2: 激活继电器，隔离产品与试验台...");
                    await ActivateRelayWithTimeoutAsync(token);
                    if (token.IsCancellationRequested) return;

                    AddLog("硬件初始化完成，请依次点击各测试项的\"测量\"按钮进行阻抗测量");
                }
                catch (OperationCanceledException)
                {
                    AddLog("初始化已取消");
                    Application.Current?.Dispatcher?.Invoke(() => IsManualTestRunning = false);
                }
                catch (TimeoutException ex)
                {
                    AddLog($"初始化超时: {ex.Message}");
                    Application.Current?.Dispatcher?.Invoke(() => IsManualTestRunning = false);
                    try { await ResetHardwareAsync(CancellationToken.None); } catch { }
                }
                catch (Exception ex)
                {
                    AddLog($"初始化失败: {ex.Message}");
                    Application.Current?.Dispatcher?.Invoke(() => IsManualTestRunning = false);
                    try { await ResetHardwareAsync(CancellationToken.None); } catch { }
                }
            });
        }

        private void StartAutoTest()
        {
            _opCts?.Cancel();
            _opCts?.Dispose();
            _opCts = new CancellationTokenSource();

            ResetResults();

            IsAutoTestRunning = true;
            IsManualTestRunning = false;
            IsRelayActivated = false;
            OverallResult = "--";
            LastTestTime = "--";

            AddLog("自动测试开始");

            Task.Run(async () =>
            {
                var token = _opCts.Token;
                try
                {
                    AddLog("步骤1: 初始化硬件设备（7131板卡、万用表、矩阵）...");
                    await InitializeHardwareWithTimeoutAsync(token);
                    if (token.IsCancellationRequested) return;

                    AddLog("步骤1.5: 继电器供电上电（5V）...");
                    await PowerOnRelaySupplyWithTimeoutAsync(token);
                    if (token.IsCancellationRequested) return;

                    AddLog("步骤2: 激活继电器，隔离产品与试验台...");
                    await ActivateRelayWithTimeoutAsync(token);
                    if (token.IsCancellationRequested) return;

                    foreach (var item in Items)
                    {
                        if (token.IsCancellationRequested) return;
                        await MeasureAsync(item, token);
                        await Task.Delay(100, token);
                    }

                    EvaluateOverall();
                    LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                    AddLog($"自动测试完成，总体结果: {OverallResult}");

                    await ResetHardwareAsync(CancellationToken.None);
                }
                catch (OperationCanceledException)
                {
                    AddLog("自动测试已取消");
                    try { await ResetHardwareAsync(CancellationToken.None); } catch { }
                }
                catch (TimeoutException ex)
                {
                    AddLog($"自动测试超时: {ex.Message}");
                    try { await ResetHardwareAsync(CancellationToken.None); } catch { }
                }
                catch (Exception ex)
                {
                    AddLog($"自动测试异常: {ex.Message}");
                    try { await ResetHardwareAsync(CancellationToken.None); } catch { }
                }
                finally
                {
                    Application.Current?.Dispatcher?.Invoke(() => IsAutoTestRunning = false);
                }
            });
        }

        private void StopTest()
        {
            _opCts?.Cancel();
            IsManualTestRunning = false;
            IsAutoTestRunning = false;
            IsRelayActivated = false;
            AddLog("测试已停止，正在复位硬件...");

            Task.Run(async () =>
            {
                try
                {
                    await ResetHardwareAsync(CancellationToken.None);
                    AddLog("硬件复位完成，资源已释放");
                }
                catch (Exception ex)
                {
                    AddLog($"停止测试时复位硬件失败: {ex.Message}");
                }
            });
        }

        internal bool CanMeasureItem(ImpedanceItemViewModel item)
        {
            if (item == null) return false;
            return IsManualTestRunning && IsRelayActivated && !IsBusy;
        }

        internal async Task MeasureAsync(ImpedanceItemViewModel item)
        {
            if (item == null) return;

            var token = _opCts?.Token ?? CancellationToken.None;
            await MeasureAsync(item, token).ConfigureAwait(false);

            EvaluateOverall();
            if (Items.All(i => i.IsMeasured))
            {
                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            }
        }

        private async Task MeasureAsync(ImpedanceItemViewModel item, CancellationToken token)
        {
            if (item == null) return;

            static List<string> SplitPins(string text)
            {
                if (string.IsNullOrWhiteSpace(text))
                    return new List<string>();

                var normalized = text
                    .Replace('，', ',')
                    .Replace('、', ',')
                    .Replace('；', ';');

                return normalized
                    .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .ToList();
            }

            await _measureLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                Application.Current?.Dispatcher?.Invoke(() => IsBusy = true);

                var matrix = MatrixControlService.Instance;

                var signalPins = SplitPins(item.SignalPin);
                var groundPins = SplitPins(item.GroundPin);

                if (signalPins.Count == 0) signalPins.Add(item.SignalPin);
                if (groundPins.Count == 0) groundPins.Add(item.GroundPin);

                if (signalPins.Count != groundPins.Count)
                {
                    AddLog($"针脚数量不匹配: SignalPin={item.SignalPin}, GroundPin={item.GroundPin}");
                    item.UpdateMeasurement(null, "--", "FAIL", measured: true);
                    return;
                }

                var texts = new List<string>(signalPins.Count);
                var passes = new List<bool>(signalPins.Count);

                for (int i = 0; i < signalPins.Count; i++)
                {
                    var signalPin = signalPins[i];
                    var groundPin = groundPins[i];
                    var colIndex = item.ColumnIndex + i;
                    var output = $"O{colIndex}";

                    AddLog($"开始测量: {item.PowerName} {signalPin}-{groundPin} (r1c{colIndex})");

                    bool connected = false;
                    try
                    {
                        connected = await matrix.ConnectNodesAsync("I1", output, MatrixSlotSequence, MatrixIpAddress, MatrixTcpBasePort).ConfigureAwait(false);
                        AddLog($"矩阵连接 I1-{output}(slot{MatrixSlotSequence}) {(connected ? "OK" : "FAIL")}");
                        if (!connected)
                        {
                            texts.Add("--");
                            passes.Add(false);
                            continue;
                        }

                        DmmReading reading;
                        try
                        {
                            reading = await _dmm.ReadOnceAsync(DmmMeasureMode.RES, new DmmReadOptions { TimeoutMilliseconds = DmmTimeoutMs }, token).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            AddLog($"万用表读数异常: {ex.Message}");
                            texts.Add("--");
                            passes.Add(false);
                            continue;
                        }

                        if (reading == null)
                        {
                            texts.Add("--");
                            passes.Add(false);
                            continue;
                        }

                        if (reading.IsOverrange)
                        {
                            texts.Add("OL");
                            passes.Add(true);
                            AddLog("读数为OL(过量程)，判为PASS");
                            continue;
                        }

                        if (reading.Value == null)
                        {
                            texts.Add("--");
                            passes.Add(false);
                            continue;
                        }

                        var ohm = reading.Value.Value;
                        var kohm = ohm / 1000.0;
                        var text = kohm.ToString("0.###", CultureInfo.InvariantCulture);
                        var pass = ohm >= PassThresholdOhm;

                        texts.Add(text);
                        passes.Add(pass);
                        AddLog($"读数: {ohm:0.###} Ω ({text} kΩ) => {(pass ? "PASS" : "FAIL")}");
                    }
                    finally
                    {
                        try
                        {
                            if (connected)
                                _ = await matrix.DisconnectNodesAsync("I1", output, MatrixSlotSequence, MatrixIpAddress, MatrixTcpBasePort).ConfigureAwait(false);
                        }
                        catch
                        {
                        }
                    }
                }

                var mergedText = string.Join("，", texts);
                var mergedPass = passes.All(x => x);
                item.UpdateMeasurement(null, mergedText, mergedPass ? "PASS" : "FAIL", measured: true);
            }
            finally
            {
                Application.Current?.Dispatcher?.Invoke(() => IsBusy = false);
                _measureLock.Release();
            }
        }

        private void ResetResults()
        {
            foreach (var item in Items)
            {
                item.UpdateMeasurement(null, "--", "--", measured: false);
            }
        }

        private void EvaluateOverall()
        {
            if (Items.Count == 0)
            {
                OverallResult = "--";
                return;
            }

            if (!Items.All(i => i.IsMeasured))
            {
                OverallResult = "--";
                return;
            }

            OverallResult = Items.All(i => string.Equals(i.Result, "PASS", StringComparison.OrdinalIgnoreCase)) ? "PASS" : "FAIL";
        }

        private async Task InitializeHardwareWithTimeoutAsync(CancellationToken token)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeoutCts.CancelAfter(DefaultTimeoutMs);

            if (_hardwareInitialized)
            {
                AddLog("硬件已初始化，跳过");
                return;
            }

            try
            {
                try
                {
                    await _dmm.ConnectAsync(DmmIpAddress, timeoutCts.Token);
                    AddLog($"万用表连接成功: {DmmIpAddress}");
                }
                catch (Exception ex)
                {
                    AddLog($"万用表连接异常: {ex.Message}");
                }

                if (_jy7131Api == null)
                {
                    var device7131 = FindFirstJy7131Device();
                    if (device7131 != null)
                    {
                        var devSlot = Infer7131SlotNumber(device7131);
                        AddLog($"找到7131板卡: {device7131.Model ?? device7131.Name}，槽位={devSlot}");
                        if (int.TryParse(devSlot, out var slotNum))
                            _jy7131Api = new Jy7131Api(device7131, slotNum);
                        else
                            _jy7131Api = new Jy7131Api(device7131);
                    }
                    else
                    {
                        AddLog("未找到7131板卡，将跳过继电器DO控制");
                    }
                }

                if (_jy7131Api != null)
                {
                    try
                    {
                        AddLog("正在连接7131板卡...");
                        if (!_jy7131Api.IsConnected)
                        {
                            await _jy7131Api.ConnectAsync(timeoutCts.Token);
                            AddLog("7131板卡连接成功");
                        }

                        if (!_jy7131Api.IsRunning)
                        {
                            await _jy7131Api.SetOutputModeAsync(Jy7131OutputMode.PushPull, timeoutCts.Token);
                            await _jy7131Api.StartAsync(timeoutCts.Token);
                            AddLog("7131板卡已启动");
                        }
                    }
                    catch (Exception ex)
                    {
                        AddLog($"7131板卡初始化异常: {ex.Message}，将跳过继电器DO控制");
                        _jy7131Api = null;
                    }
                }

                try
                {
                    if (_componentPowerStateApi != null)
                    {
                        AddLog("正在设置组件供电状态: 下电...");
                        await _componentPowerStateApi.ApplyComponentDownStateAsync(timeoutCts.Token);
                        AddLog("组件供电状态已设置为下电");
                    }
                }
                catch (Exception ex)
                {
                    AddLog($"组件下电状态设置异常: {ex.Message}");
                }

                var matrix = MatrixControlService.Instance;
                var okCommon = await matrix.ConnectNodesAsync("I4", "O2", MatrixSlotCommon, MatrixIpAddress, MatrixTcpBasePort);
                AddLog($"矩阵公共通路 I4-O2(slot{MatrixSlotCommon}) {(okCommon ? "OK" : "FAIL")}");
                if (!okCommon)
                    throw new InvalidOperationException("矩阵公共通路连接失败");

                _hardwareInitialized = true;
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !token.IsCancellationRequested)
            {
                throw new TimeoutException($"硬件初始化超时（{DefaultTimeoutMs}ms）");
            }
        }

        private async Task ResetHardwareAsync(CancellationToken token)
        {
            try
            {
                AddLog("正在复位硬件设备...");

                try
                {
                    if (_componentPowerStateApi != null)
                        await _componentPowerStateApi.ApplyComponentDownStateAsync(token);
                }
                catch (Exception ex)
                {
                    AddLog($"复位时组件下电状态设置异常: {ex.Message}");
                }

                if (IsRelayActivated)
                    await DeactivateRelayWithTimeoutAsync(token);

                await PowerOffRelaySupplyAsync(token);

                if (_jy7131Api != null && _jy7131Api.IsConnected)
                {
                    try
                    {
                        if (_jy7131Api.IsRunning)
                        {
                            AddLog("正在停止7131板卡...");
                            await _jy7131Api.StopAsync(token);
                            AddLog("7131板卡已停止");
                        }

                        AddLog("正在断开7131板卡连接...");
                        await _jy7131Api.DisconnectAsync(token);
                        AddLog("7131板卡已断开");
                    }
                    catch (Exception ex)
                    {
                        AddLog($"7131板卡复位异常: {ex.Message}");
                    }
                }

                try
                {
                    var matrix = MatrixControlService.Instance;
                    _ = await matrix.DisconnectNodesAsync("I4", "O2", MatrixSlotCommon, MatrixIpAddress, MatrixTcpBasePort);
                    foreach (var item in Items)
                    {
                        var output = $"O{item.ColumnIndex}";
                        _ = await matrix.DisconnectNodesAsync("I1", output, MatrixSlotSequence, MatrixIpAddress, MatrixTcpBasePort);
                    }
                }
                catch
                {
                }

                try
                {
                    await _dmm.DisconnectAsync(token);
                }
                catch
                {
                }

                _hardwareInitialized = false;
                AddLog("硬件设备已复位");
            }
            catch (Exception ex)
            {
                AddLog($"硬件复位异常: {ex.Message}");
            }
        }

        private async Task PowerOnRelaySupplyWithTimeoutAsync(CancellationToken token)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeoutCts.CancelAfter(RelayPowerTimeoutMs);

            try
            {
                if (_relaySupplyOn)
                {
                    AddLog("继电器供电已上电，跳过");
                    return;
                }

                AddLog($"正在开启继电器供电（5V）：电源2 {RelayPowerSupplyIpAddress} CH1...");
                _relayPowerSupply ??= new PowerSupplySocketApi();
                if (!_relayPowerSupply.IsConnected)
                    await _relayPowerSupply.ConnectAsync(RelayPowerSupplyIpAddress, timeoutCts.Token);

                await _relayPowerSupply.ApplyAsync(RelayPowerSupplyChannel, RelayVoltage, RelayCurrentLimit, timeoutCts.Token);
                await _relayPowerSupply.SetOutputEnabledAsync(RelayPowerSupplyChannel, true, timeoutCts.Token);
                await Task.Delay(200, timeoutCts.Token);

                _relaySupplyOn = true;
                AddLog($"继电器供电已上电：电源2 {RelayPowerSupplyIpAddress} CH1 {RelayVoltage:0.###}V/{RelayCurrentLimit:0.###}A");
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !token.IsCancellationRequested)
            {
                throw new TimeoutException($"继电器供电上电超时（{RelayPowerTimeoutMs}ms）");
            }
            catch (Exception ex)
            {
                AddLog($"继电器供电上电失败: {ex.Message}");
                _relaySupplyOn = false;
            }
        }

        private async Task PowerOffRelaySupplyAsync(CancellationToken token)
        {
            if (!_relaySupplyOn)
                return;

            try
            {
                if (_relayPowerSupply != null)
                {
                    try { await _relayPowerSupply.SetOutputEnabledAsync(RelayPowerSupplyChannel, false, token); } catch { }
                    try { await _relayPowerSupply.DisconnectAsync(token); } catch { }
                    try { await _relayPowerSupply.DisposeAsync(); } catch { }
                    _relayPowerSupply = null;
                }

                AddLog("继电器供电已关闭");
            }
            catch (Exception ex)
            {
                AddLog($"继电器供电关闭异常: {ex.Message}");
            }
            finally
            {
                _relaySupplyOn = false;
            }
        }

        private async Task ActivateRelayWithTimeoutAsync(CancellationToken token)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeoutCts.CancelAfter(RelayTimeoutMs);

            try
            {
                AddLog($"正在激活继电器（{RelayControlChannel2}+{RelayControlChannel1}）...");

                if (_jy7131Api != null && _jy7131Api.IsConnected)
                {
                    if (!_jy7131Api.IsRunning)
                    {
                        await _jy7131Api.SetOutputModeAsync(Jy7131OutputMode.PushPull, timeoutCts.Token);
                        await _jy7131Api.StartAsync(timeoutCts.Token);
                        AddLog("7131板卡已启动");
                    }

                    await _jy7131Api.WriteDoAsync(RelayControlChannel1, true, timeoutCts.Token);
                    await _jy7131Api.WriteDoAsync(RelayControlChannel2, true, timeoutCts.Token);
                }
                else
                {
                    AddLog("7131板卡不可用，跳过继电器DO动作");
                }

                await Task.Delay(200, timeoutCts.Token);
                Application.Current?.Dispatcher?.Invoke(() => IsRelayActivated = true);
                AddLog("继电器已激活");
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !token.IsCancellationRequested)
            {
                throw new TimeoutException($"激活继电器超时（{RelayTimeoutMs}ms）");
            }
        }

        private async Task DeactivateRelayWithTimeoutAsync(CancellationToken token)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeoutCts.CancelAfter(RelayTimeoutMs);

            try
            {
                AddLog($"正在复位继电器（{RelayControlChannel2}+{RelayControlChannel1}）...");

                if (_jy7131Api != null && _jy7131Api.IsConnected)
                {
                    if (!_jy7131Api.IsRunning)
                    {
                        await _jy7131Api.SetOutputModeAsync(Jy7131OutputMode.PushPull, timeoutCts.Token);
                        await _jy7131Api.StartAsync(timeoutCts.Token);
                        AddLog("7131板卡已启动");
                    }

                    await _jy7131Api.WriteDoAsync(RelayControlChannel1, false, timeoutCts.Token);
                    await _jy7131Api.WriteDoAsync(RelayControlChannel2, false, timeoutCts.Token);
                }
                else
                {
                    AddLog("7131板卡不可用，跳过继电器DO动作");
                }

                await Task.Delay(200, timeoutCts.Token);
                Application.Current?.Dispatcher?.Invoke(() => IsRelayActivated = false);
                AddLog("继电器已复位");
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !token.IsCancellationRequested)
            {
                throw new TimeoutException($"复位继电器超时（{RelayTimeoutMs}ms）");
            }
        }

        private DeviceBase FindFirstJy7131Device()
        {
            var chassisList = _pxiChassisService?.GetAllChassis();
            if (chassisList == null)
            {
                AddLog("[7131查找] 机箱列表为null");
                return null;
            }

            foreach (var chassis in chassisList)
            {
                if (chassis?.Devices == null)
                    continue;

                var device = chassis.Devices.FirstOrDefault(d =>
                    d is DigitalIODevice ||
                    (d?.Model?.IndexOf("7131", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.DeviceTypeName?.IndexOf("离散量", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.DeviceTypeName?.IndexOf("数字量", StringComparison.OrdinalIgnoreCase) >= 0));

                if (device != null)
                    return device;

                foreach (var d in chassis.Devices)
                {
                    if (d?.Children == null)
                        continue;

                    var childDevice = d.Children.FirstOrDefault(c =>
                        c is DigitalIODevice ||
                        (c?.Model?.IndexOf("7131", StringComparison.OrdinalIgnoreCase) >= 0) ||
                        (c?.DeviceTypeName?.IndexOf("离散量", StringComparison.OrdinalIgnoreCase) >= 0) ||
                        (c?.DeviceTypeName?.IndexOf("数字量", StringComparison.OrdinalIgnoreCase) >= 0));

                    if (childDevice != null)
                        return childDevice;
                }
            }

            return null;
        }

        private static string Infer7131SlotNumber(DeviceBase device)
        {
            if (device is MeasureControl.Models.Devices.DeviceCategories.PxiDeviceBase pxi && pxi.SlotIndex > 0)
                return pxi.SlotIndex.ToString();

            var slot = device?.SlotPosition;
            if (!string.IsNullOrWhiteSpace(slot))
            {
                var trimmed = slot.Replace("Slot", "").Replace("slot", "").Trim();
                if (int.TryParse(trimmed, out var slotNum) && slotNum > 0)
                    return slotNum.ToString();
            }

            return "12";
        }

        private void RaiseCanExecuteChangedForItems()
        {
            foreach (var item in Items)
            {
                item.MeasureCommand?.RaiseCanExecuteChanged();
            }
        }

        private void AddLog(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            var logEntry = $"[{DateTime.Now:HH:mm:ss}] {message}";
            Application.Current?.Dispatcher?.Invoke(() => Logs.Add(logEntry));
        }

        public void Dispose()
        {
            try
            {
                _opCts?.Cancel();
            }
            catch
            {
            }

            _opCts?.Dispose();

            try { ResetHardwareAsync(CancellationToken.None).GetAwaiter().GetResult(); } catch { }

            _measureLock?.Dispose();
        }

        public sealed class ImpedanceItemViewModel : BindableBase
        {
            private readonly PowerToGroundImpedanceTestViewModel _owner;

            private string _impedanceKohmText = "--";
            private string _result = "--";
            private bool _isMeasured;

            internal ImpedanceItemViewModel(PowerToGroundImpedanceTestViewModel owner, int columnIndex, string powerName, string signalType, string signalPin, string groundPin)
            {
                _owner = owner;
                ColumnIndex = columnIndex;
                PowerName = powerName;
                SignalType = signalType;
                SignalPin = signalPin;
                GroundPin = groundPin;

                MeasureCommand = new DelegateCommand(async () => await _owner.MeasureAsync(this), () => _owner.CanMeasureItem(this));
            }

            public int ColumnIndex { get; }

            public string PowerName { get; }

            public string SignalType { get; }

            public string SignalPin { get; }

            public string GroundPin { get; }

            public string ImpedanceKohmText
            {
                get => _impedanceKohmText;
                private set => SetProperty(ref _impedanceKohmText, value);
            }

            public string Result
            {
                get => _result;
                private set => SetProperty(ref _result, value);
            }

            public bool IsMeasured
            {
                get => _isMeasured;
                private set => SetProperty(ref _isMeasured, value);
            }

            public DelegateCommand MeasureCommand { get; }

            internal void UpdateMeasurement(double? valueKohm, string valueText, string result, bool measured)
            {
                if (Application.Current?.Dispatcher != null)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        ImpedanceKohmText = valueText;
                        Result = result;
                        IsMeasured = measured;
                    });
                    return;
                }

                ImpedanceKohmText = valueText;
                Result = result;
                IsMeasured = measured;
            }
        }
    }
}
