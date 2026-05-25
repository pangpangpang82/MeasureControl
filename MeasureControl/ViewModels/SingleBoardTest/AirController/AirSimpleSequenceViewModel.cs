using MeasureControl.Services.HardwareApis;
using MeasureControl.Services;
using MeasureControl.Simulations.S_C_8_3_1;
using Prism.Commands;
using Prism.Mvvm;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace MeasureControl.ViewModels.SingleBoardTest.AirController
{
    public class AirSimpleSequenceViewModel : BindableBase
    {
        private const string FixedTxChannel = "429_CH0";
        private const string FixedRxChannel = "429_CH2";
        private const string AirSafetyTxChannel = "429_CH1";
        private const string AirSafetyRxChannel = "429_CH0";

        private const byte DefaultLabel = 0x6A;

        private static readonly byte[] AtpR = { 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] AtpEnterOk = { 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x02 };
        private static readonly byte[] AirSafetyAtpR = { 0x30, 0x01, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] AirSafetyAtpEnterOk = { 0x30, 0x01, 0x01, 0x02, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] AtpE = { 0x30, 0x02, 0x02, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] ExitOk = { 0x30, 0x02, 0x02, 0x02, 0x00, 0x00, 0x00, 0x00 };

        private static readonly byte[] Ab28vSupply = { 0x01, 0x01, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] S28vSupplyVoltage01 = { 0x10, 0x01, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00 };

        private static readonly byte[] Vbit15Prefix4 = { 0x01, 0x01, 0x01, 0x02 };
        private static readonly byte[] Vbit5Prefix4 = { 0x01, 0x01, 0x01, 0x03 };

        private readonly S_C_8_3_1Simulation _arinc = new S_C_8_3_1Simulation();
        private readonly SemaphoreSlim _arincOpLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _manualTestLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _autoTestLock = new SemaphoreSlim(1, 1);

        private CancellationTokenSource _autoTestCts;
        private bool _autoTestEnteredAtp;
        private bool _suppressResultUpdates;

        private IPowerSupplyApi _powerSupply;
        private readonly SemaphoreSlim _powerSupplyLock = new SemaphoreSlim(1, 1);

        private string _title = "测试";
        private bool _isFixedArincChannels;
        private bool _isManualTestRunning;
        private bool _isAutoTestRunning;
        private string _lastTestTime = "--";
        private string _lastTestResult = "--";

        private string _powerSupplyIpAddress = "192.168.1.15";
        private string _powerSupplyMeasuredCurrentText = "--";

        private double? _activeSupplyVoltage;
        private double _selectedSupplyVoltage = 32.0;

        private string _dmmVoltage32Text;
        private string _dmmVoltage28Text;
        private string _powerSupplyMeasuredCurrent32Text;
        private string _powerSupplyMeasuredCurrent28Text;

        private string _telemetryVoltage32Text;
        private string _telemetryVoltage28Text;
        private string _telemetryRxData32Text;
        private string _telemetryRxData28Text;

        private string _enterAtpTxChannel;
        private string _enterAtpRxChannel;
        private string _setVoltageTxChannel;
        private string _telemetryRxChannel;
        private string _exitAtpTxChannel;
        private string _exitAtpRxChannel;

        private string _dmmVoltageText;
        private string _telemetryVoltageText;
        private string _enterAtpRxDataText;
        private string _enterAtpRxData32Text;
        private string _enterAtpRxData28Text;
        private string _telemetryRxDataText;
        private string _exitAtpRxDataText;

        public AirSimpleSequenceViewModel()
        {
            _enterAtpTxChannel = FixedTxChannel;
            _enterAtpRxChannel = FixedRxChannel;
            _setVoltageTxChannel = FixedTxChannel;
            _telemetryRxChannel = FixedRxChannel;
            _exitAtpTxChannel = FixedTxChannel;
            _exitAtpRxChannel = FixedRxChannel;

            DmmVoltageText = "--";
            PowerSupplyMeasuredCurrentText = "--";
            TelemetryVoltageText = "--";
            DmmVoltage32Text = "--";
            DmmVoltage28Text = "--";
            PowerSupplyMeasuredCurrent32Text = "--";
            PowerSupplyMeasuredCurrent28Text = "--";
            TelemetryVoltage32Text = "--";
            TelemetryVoltage28Text = "--";
            TelemetryRxData32Text = "--";
            TelemetryRxData28Text = "--";
            EnterAtpRxDataText = "--";
            EnterAtpRxData32Text = "--";
            EnterAtpRxData28Text = "--";
            TelemetryRxDataText = "--";
            ExitAtpRxDataText = "--";

            ManualTestCommand = new DelegateCommand(OnManualTest);
            AutoTestCommand = new DelegateCommand(OnAutoTest);
            ClearLogCommand = new DelegateCommand(() => Logs.Clear());

            Supply32vCommand = new DelegateCommand(async () => await OnSupplyVoltageAsync(32.0));
            Supply28vCommand = new DelegateCommand(async () => await OnSupplyVoltageAsync(28.0));
            SupplySelectedVoltageCommand = new DelegateCommand(async () => await OnSupplyVoltageAsync(SelectedSupplyVoltage));
            PowerSupplyOutputOffCommand = new DelegateCommand(async () => await OnPowerSupplyOutputOffAsync());
            ReadPowerSupplyMeasurementsCommand = new DelegateCommand(async () => await OnReadPowerSupplyMeasurementsAsync());

            SendEnterAtpCommand = new DelegateCommand(async () => await OnSendEnterAtpAsync());
            SendSetVoltageCommand = new DelegateCommand(async () => await OnSendAb28vSupplyAsync());
            SendExitAtpCommand = new DelegateCommand(async () => await OnSendExitAtpAsync());

            // In case Title keeps default value; View may overwrite Title afterwards.
            ApplyFixedArincChannelPolicy();
        }

        public string Title
        {
            get => _title;
            set
            {
                if (SetProperty(ref _title, value))
                {
                    ApplyFixedArincChannelPolicy();
                    RaisePropertyChanged(nameof(IsSafetyChannelTest));
                    RaisePropertyChanged(nameof(ChannelName));
                    RaisePropertyChanged(nameof(TestCommandName));
                    RaisePropertyChanged(nameof(TestCommandBytesText));
                    RaisePropertyChanged(nameof(IsCombinedVoltageSelectionTest));
                    RaisePropertyChanged(nameof(SupplyStepText));
                    RaisePropertyChanged(nameof(SelectedSupplyCurrentUpperLimit));
                }
            }
        }

        public bool IsSafetyChannelTest
        {
            get => !string.IsNullOrWhiteSpace(Title) && (Title.IndexOf("安全通道", StringComparison.OrdinalIgnoreCase) >= 0 || Title.StartsWith("8.2.1", StringComparison.OrdinalIgnoreCase));
        }

        public string ChannelName => IsSafetyChannelTest ? "安全通道S" : "控制通道";

        public string TestCommandName => IsSafetyChannelTest ? "S_28VSupply_Voltage01" : "AB_28V_SUPPLY";

        public string TestCommandBytesText => IsSafetyChannelTest ? "0x10 01 01 01 00 00 00 00" : "0x01 01 01 01 00 00 00 00";

        public bool IsCombinedVoltageSelectionTest => IsSafetyChannelTest;

        public double SelectedSupplyVoltage
        {
            get => _selectedSupplyVoltage;
            set
            {
                if (SetProperty(ref _selectedSupplyVoltage, value))
                {
                    RaisePropertyChanged(nameof(SelectedSupplyVoltageText));
                    RaisePropertyChanged(nameof(SupplyStepText));
                    RaisePropertyChanged(nameof(SelectedSupplyCurrentUpperLimit));
                }
            }
        }

        public string SelectedSupplyVoltageText => $"{SelectedSupplyVoltage:0.###}V";

        public string SupplyStepText => $"1.向{ChannelName}供电 {SelectedSupplyVoltageText}";

        public double SelectedSupplyCurrentUpperLimit
        {
            get
            {
                if (IsSafetyChannelTest)
                    return SelectedSupplyVoltage >= 31.0 ? 0.94 : 1.07;

                return SelectedSupplyVoltage >= 31.0 ? 2.18 : 2.50;
            }
        }

        public bool IsFixedArincChannels
        {
            get => _isFixedArincChannels;
            private set => SetProperty(ref _isFixedArincChannels, value);
        }

        public string PowerSupplyIpAddress
        {
            get => _powerSupplyIpAddress;
            set => SetProperty(ref _powerSupplyIpAddress, value);
        }

        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();

        public ObservableCollection<double> SupplyVoltageOptions { get; } = new ObservableCollection<double> { 32.0, 28.0 };

        public DelegateCommand ManualTestCommand { get; }
        public DelegateCommand AutoTestCommand { get; }
        public DelegateCommand ClearLogCommand { get; }

        public DelegateCommand Supply32vCommand { get; }
        public DelegateCommand Supply28vCommand { get; }
        public DelegateCommand SupplySelectedVoltageCommand { get; }
        public DelegateCommand PowerSupplyOutputOffCommand { get; }
        public DelegateCommand ReadPowerSupplyMeasurementsCommand { get; }

        public DelegateCommand SendEnterAtpCommand { get; }
        public DelegateCommand SendSetVoltageCommand { get; }
        public DelegateCommand SendExitAtpCommand { get; }

        public string EnterAtpTxChannel
        {
            get => _enterAtpTxChannel;
        }

        public string EnterAtpRxChannel
        {
            get => _enterAtpRxChannel;
        }

        public string SetVoltageTxChannel
        {
            get => _setVoltageTxChannel;
        }

        public string TelemetryRxChannel
        {
            get => _telemetryRxChannel;
        }

        public string ExitAtpTxChannel
        {
            get => _exitAtpTxChannel;
        }

        public string ExitAtpRxChannel
        {
            get => _exitAtpRxChannel;
        }

        private void ApplyFixedArincChannelPolicy()
        {
            IsFixedArincChannels = true;

            var tx = IsSafetyChannelTest ? AirSafetyTxChannel : FixedTxChannel;
            var rx = IsSafetyChannelTest ? AirSafetyRxChannel : FixedRxChannel;

            SetProperty(ref _enterAtpTxChannel, tx);
            SetProperty(ref _enterAtpRxChannel, rx);
            SetProperty(ref _setVoltageTxChannel, tx);
            SetProperty(ref _telemetryRxChannel, rx);
            SetProperty(ref _exitAtpTxChannel, tx);
            SetProperty(ref _exitAtpRxChannel, rx);
        }

        public string DmmVoltageText
        {
            get => _dmmVoltageText;
            set => SetProperty(ref _dmmVoltageText, value);
        }

        public string DmmVoltage32Text
        {
            get => _dmmVoltage32Text;
            set => SetProperty(ref _dmmVoltage32Text, value);
        }

        public string DmmVoltage28Text
        {
            get => _dmmVoltage28Text;
            set => SetProperty(ref _dmmVoltage28Text, value);
        }

        public string PowerSupplyMeasuredCurrentText
        {
            get => _powerSupplyMeasuredCurrentText;
            set => SetProperty(ref _powerSupplyMeasuredCurrentText, value);
        }

        public string PowerSupplyMeasuredCurrent32Text
        {
            get => _powerSupplyMeasuredCurrent32Text;
            set => SetProperty(ref _powerSupplyMeasuredCurrent32Text, value);
        }

        public string PowerSupplyMeasuredCurrent28Text
        {
            get => _powerSupplyMeasuredCurrent28Text;
            set => SetProperty(ref _powerSupplyMeasuredCurrent28Text, value);
        }

        public string TelemetryVoltageText
        {
            get => _telemetryVoltageText;
            set => SetProperty(ref _telemetryVoltageText, value);
        }

        public string TelemetryVoltage32Text
        {
            get => _telemetryVoltage32Text;
            set => SetProperty(ref _telemetryVoltage32Text, value);
        }

        public string TelemetryVoltage28Text
        {
            get => _telemetryVoltage28Text;
            set => SetProperty(ref _telemetryVoltage28Text, value);
        }

        public string TelemetryRxData32Text
        {
            get => _telemetryRxData32Text;
            private set => SetProperty(ref _telemetryRxData32Text, value);
        }

        public string TelemetryRxData28Text
        {
            get => _telemetryRxData28Text;
            private set => SetProperty(ref _telemetryRxData28Text, value);
        }

        public string EnterAtpRxDataText
        {
            get => _enterAtpRxDataText;
            private set => SetProperty(ref _enterAtpRxDataText, value);
        }

        public string EnterAtpRxData32Text
        {
            get => _enterAtpRxData32Text;
            private set => SetProperty(ref _enterAtpRxData32Text, value);
        }

        public string EnterAtpRxData28Text
        {
            get => _enterAtpRxData28Text;
            private set => SetProperty(ref _enterAtpRxData28Text, value);
        }

        public string TelemetryRxDataText
        {
            get => _telemetryRxDataText;
            private set => SetProperty(ref _telemetryRxDataText, value);
        }

        public string ExitAtpRxDataText
        {
            get => _exitAtpRxDataText;
            private set => SetProperty(ref _exitAtpRxDataText, value);
        }

        public bool IsManualTestRunning
        {
            get => _isManualTestRunning;
            set
            {
                if (SetProperty(ref _isManualTestRunning, value) && value)
                {
                    IsAutoTestRunning = false;
                }
            }
        }

        public bool IsAutoTestRunning
        {
            get => _isAutoTestRunning;
            set
            {
                if (SetProperty(ref _isAutoTestRunning, value) && value)
                {
                    IsManualTestRunning = false;
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

        private void OnManualTest()
        {
            if (IsManualTestRunning)
            {
                _ = StopManualTestAsync();
                return;
            }

            if (IsAutoTestRunning)
            {
                _ = StopAutoThenStartManualAsync();
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

            if (IsManualTestRunning)
            {
                _ = StopManualThenStartAutoAsync();
                return;
            }

            _ = StartAutoTestAsync();
        }

        private async Task StopManualThenStartAutoAsync()
        {
            await StopManualTestAsync();
            await StartAutoTestAsync();
        }

        private async Task StopAutoThenStartManualAsync()
        {
            try { _autoTestCts?.Cancel(); } catch { }
            AddLog($"[{DateTime.Now:HH:mm:ss}] 等待自动测试停止后启动手动测试");

            await _autoTestLock.WaitAsync();
            try
            {
            }
            finally
            {
                _autoTestLock.Release();
            }

            await StartManualTestAsync();
        }

        private async Task StartManualTestAsync()
        {
            await _manualTestLock.WaitAsync();
            try
            {
                if (IsManualTestRunning)
                    return;

                IsManualTestRunning = true;
                LastTestTime = "--";
                LastTestResult = "--";
                _activeSupplyVoltage = null;
                DmmVoltage32Text = "--";
                DmmVoltage28Text = "--";
                PowerSupplyMeasuredCurrent32Text = "--";
                PowerSupplyMeasuredCurrent28Text = "--";
                TelemetryVoltage32Text = "--";
                TelemetryVoltage28Text = "--";
                TelemetryRxData32Text = "--";
                TelemetryRxData28Text = "--";
                EnterAtpRxData32Text = "--";
                EnterAtpRxData28Text = "--";
                AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试启动：开始打开设备");

                await EnsurePowerSupplyConnectedAsync(CancellationToken.None);
                _arinc.IsRealProduct = true;
                _arinc.ArincRate = 100000.0;
                await _arinc.StartAsync(EnterAtpTxChannel, EnterAtpRxChannel, msg => AddLog(msg));

                AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试启动：429板卡/电源已就绪");
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试启动异常：{ex.Message}");
                IsManualTestRunning = false;
                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
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
                if (!IsManualTestRunning)
                    return;

                AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试停止：关闭设备");
                IsManualTestRunning = false;

                await CleanupHardwareAfterTestAsync();

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
                _suppressResultUpdates = true;
                _autoTestEnteredAtp = false;
                LastTestTime = "--";
                LastTestResult = "--";
                _activeSupplyVoltage = null;
                DmmVoltageText = "--";
                PowerSupplyMeasuredCurrentText = "--";
                TelemetryVoltageText = "--";
                DmmVoltage32Text = "--";
                DmmVoltage28Text = "--";
                PowerSupplyMeasuredCurrent32Text = "--";
                PowerSupplyMeasuredCurrent28Text = "--";
                TelemetryVoltage32Text = "--";
                TelemetryVoltage28Text = "--";
                TelemetryRxData32Text = "--";
                TelemetryRxData28Text = "--";
                EnterAtpRxData32Text = "--";
                EnterAtpRxData28Text = "--";
                EnterAtpRxDataText = "--";
                TelemetryRxDataText = "--";
                ExitAtpRxDataText = "--";

                _autoTestCts?.Dispose();
                _autoTestCts = new CancellationTokenSource();
                var token = _autoTestCts.Token;

                AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试启动");
                await RunAutoTestAsync(token);
            }
            finally
            {
                _autoTestLock.Release();
            }
        }

        protected virtual async Task RunAutoTestAsync(CancellationToken token)
        {
            var failures = new ObservableCollection<string>();
            try
            {
                token.ThrowIfCancellationRequested();

                AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试：开始打开设备");
                await EnsurePowerSupplyConnectedAsync(token);
                _arinc.IsRealProduct = true;
                _arinc.ArincRate = 100000.0;
                await _arinc.StartAsync(EnterAtpTxChannel, EnterAtpRxChannel, msg => AddLog(msg));

                if (IsSafetyChannelTest)
                {
                    await RunSupplyVoltageScenarioAsync(
                        supplyVoltage: 32.0,
                        currentUpperLimit: 0.94,
                        token,
                        failures);

                    token.ThrowIfCancellationRequested();

                    await RunSupplyVoltageScenarioAsync(
                        supplyVoltage: 28.0,
                        currentUpperLimit: 1.07,
                        token,
                        failures);
                }
                else if (IsCombinedVoltageSelectionTest)
                {
                    await RunSupplyVoltageScenarioAsync(
                        supplyVoltage: SelectedSupplyVoltage,
                        currentUpperLimit: SelectedSupplyCurrentUpperLimit,
                        token,
                        failures);
                }
                else
                {
                    await RunSupplyVoltageScenarioAsync(
                        supplyVoltage: 32.0,
                        currentUpperLimit: 2.18,
                        token,
                        failures);

                    token.ThrowIfCancellationRequested();

                    await RunSupplyVoltageScenarioAsync(
                        supplyVoltage: 28.0,
                        currentUpperLimit: 2.50,
                        token,
                        failures);
                }

                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                LastTestResult = failures.Count == 0 ? "PASS" : "FAIL";
                AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试汇总：{LastTestResult}");
                foreach (var f in failures)
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 不合格：{f}");
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
                await CleanupHardwareAfterTestAsync();

                _suppressResultUpdates = false;
                IsAutoTestRunning = false;
            }
        }

        protected async Task CleanupHardwareAfterTestAsync()
        {
            _activeSupplyVoltage = null;

            try { await PowerSupplyOutputOffAsync(CancellationToken.None); } catch { }
            try { await DisconnectPowerSupplyAsync(CancellationToken.None); } catch { }

            try
            {
                try { await _arinc.ClearRxFifoAsync(EnterAtpRxChannel); } catch { }
                try { await _arinc.ClearRxFifoAsync(TelemetryRxChannel); } catch { }
                try { await _arinc.ClearRxFifoAsync(ExitAtpRxChannel); } catch { }
            }
            catch
            {
            }

            try { await _arinc.StopAsync(msg => AddLog(msg)); } catch { }
        }

        protected async Task RunSupplyVoltageScenarioAsync(double supplyVoltage, double currentUpperLimit, CancellationToken token, ObservableCollection<string> failures)
        {
            token.ThrowIfCancellationRequested();

            _activeSupplyVoltage = supplyVoltage;
            AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试：向{ChannelName}供电 {supplyVoltage.ToString("0.###", CultureInfo.InvariantCulture)}V");
            await PowerSupplyApplyAsync(supplyVoltage, currentLimit: Math.Max(3.0, currentUpperLimit + 0.5), token);
            await Task.Delay(300, token);

            token.ThrowIfCancellationRequested();

            var enteredAtpOk = await AutoEnterAtpAsync(token);
            if (!enteredAtpOk)
            {
                failures.Add($"{supplyVoltage:0.###}V：进入ATP失败");
                await PowerSupplyOutputOffAsync(token);
                return;
            }

            _autoTestEnteredAtp = true;

            var ms = await PowerSupplyReadMeasurementsAsync(token);
            if (ms?.Voltage?.Value != null)
                DmmVoltageText = $"{ms.Voltage.Value:0.000} V";
            if (ms?.Current?.Value != null)
                PowerSupplyMeasuredCurrentText = $"{ms.Current.Value:0.000} A";

            if (supplyVoltage >= 31.0)
            {
                if (ms?.Voltage?.Value != null)
                    DmmVoltage32Text = $"{ms.Voltage.Value:0.000} V";
                if (ms?.Current?.Value != null)
                    PowerSupplyMeasuredCurrent32Text = $"{ms.Current.Value:0.000} A";
            }
            else
            {
                if (ms?.Voltage?.Value != null)
                    DmmVoltage28Text = $"{ms.Voltage.Value:0.000} V";
                if (ms?.Current?.Value != null)
                    PowerSupplyMeasuredCurrent28Text = $"{ms.Current.Value:0.000} A";
            }

            if (ms?.Voltage?.Value != null || ms?.Current?.Value != null)
            {
                var vText = ms?.Voltage?.Value != null ? $"{ms.Voltage.Value:0.000}V" : "--";
                var iText = ms?.Current?.Value != null ? $"{ms.Current.Value:0.000}A" : "--";
                AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试：记录供电电源读数 V={vText}, I={iText}");
            }

            if (ms?.Current?.Value != null && ms.Current.Value > currentUpperLimit)
            {
                failures.Add($"{supplyVoltage:0.###}V供电电流={ms.Current.Value:0.000}A > {currentUpperLimit:0.###}A");
            }

            token.ThrowIfCancellationRequested();
            var telemetryOk = await AutoSendAb28vSupplyAndReadTelemetryAsync(token, failures);
            if (!telemetryOk)
            {
                failures.Add($"{supplyVoltage:0.###}V：{TestCommandName} 回采失败");
            }

            AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试：断开{ChannelName}供电");
            await PowerSupplyOutputOffAsync(token);
            await Task.Delay(200, token);

            _autoTestEnteredAtp = false;
        }

        protected async Task<bool?> RunSingleSupplyVoltageTestAsync(double supplyVoltage, double currentUpperLimit, CancellationToken token)
        {
            var failures = new ObservableCollection<string>();
            try
            {
                LastTestTime = "--";
                LastTestResult = "--";
                await EnsurePowerSupplyConnectedAsync(token);
                _arinc.IsRealProduct = true;
                _arinc.ArincRate = 100000.0;
                await _arinc.StartAsync(EnterAtpTxChannel, EnterAtpRxChannel, msg => AddLog(msg));
                await RunSupplyVoltageScenarioAsync(supplyVoltage, currentUpperLimit, token, failures);
                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                LastTestResult = failures.Count == 0 ? "PASS" : "FAIL";
                AddLog($"[{DateTime.Now:HH:mm:ss}] {supplyVoltage:0.###}V测试完成：{LastTestResult}");
                foreach (var f in failures)
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 不合格：{f}");
                return failures.Count == 0;
            }
            catch (OperationCanceledException)
            {
                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                LastTestResult = "已停止";
                AddLog($"[{DateTime.Now:HH:mm:ss}] {supplyVoltage:0.###}V测试已停止");
                return null;
            }
            catch (Exception ex)
            {
                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                LastTestResult = "异常";
                AddLog($"[{DateTime.Now:HH:mm:ss}] {supplyVoltage:0.###}V测试异常：{ex.Message}");
                return false;
            }
            finally
            {
                await CleanupHardwareAfterTestAsync();
            }
        }

        private async Task<bool> AutoEnterAtpAsync(CancellationToken token)
        {
            await _arincOpLock.WaitAsync(token);
            try
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试：发送进入ATP");
                EnterAtpRxDataText = "--";

                byte[] resp = null;
                const int maxRetry = 3;
                for (int attempt = 1; attempt <= maxRetry && resp == null; attempt++)
                {
                    if (attempt > 1)
                        AddLog($"[{DateTime.Now:HH:mm:ss}] 进入ATP重试 ({attempt}/{maxRetry})");

                    try { await _arinc.ClearRxFifoAsync(EnterAtpRxChannel); } catch { }
                    await Task.Delay(20, token);

                    if (IsSafetyChannelTest)
                    {
                        await _arinc.SendBenchCommandOnlyAsync(
                            EnterAtpTxChannel,
                            AirSafetyAtpR,
                            msg => AddLog(msg),
                            token);

                        resp = await _arinc.WaitBenchResponse8Async(
                            EnterAtpRxChannel,
                            b => b != null && b.SequenceEqual(AirSafetyAtpEnterOk),
                            timeoutMs: 1500,
                            msg => AddLog(msg),
                            token);
                    }
                    else
                    {
                        await _arinc.SendBenchCommandOnlyAsync(
                            EnterAtpTxChannel,
                            AtpR,
                            msg => AddLog(msg),
                            token);

                        resp = await _arinc.WaitBenchResponse8Async(
                            EnterAtpRxChannel,
                            b => b != null && b.SequenceEqual(AtpEnterOk),
                            timeoutMs: 1500,
                            msg => AddLog(msg),
                            token);
                    }
                }

                if (resp == null)
                    return false;

                EnterAtpRxDataText = "0x" + FormatBytesHex(resp);

                if (_activeSupplyVoltage.HasValue)
                {
                    if (_activeSupplyVoltage.Value >= 31.0)
                        EnterAtpRxData32Text = EnterAtpRxDataText;
                    else
                        EnterAtpRxData28Text = EnterAtpRxDataText;
                }
                return true;
            }
            finally
            {
                _arincOpLock.Release();
            }
        }

        private async Task<bool> AutoExitAtpAsync(CancellationToken token)
        {
            await _arincOpLock.WaitAsync(token);
            try
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试：发送退出ATP");
                ExitAtpRxDataText = "--";

                try { await _arinc.ClearRxFifoAsync(ExitAtpRxChannel); } catch { }
                await Task.Delay(20, token);

                await _arinc.SendBenchCommandOnlyAsync(
                    ExitAtpTxChannel,
                    AtpE,
                    msg => AddLog(msg),
                    token);

                var resp = await _arinc.WaitBenchResponse8Async(
                    ExitAtpRxChannel,
                    b => b != null && b.SequenceEqual(ExitOk),
                    timeoutMs: 2000,
                    msg => AddLog(msg),
                    token);

                if (resp == null)
                    return false;

                ExitAtpRxDataText = "0x" + FormatBytesHex(resp);
                return true;
            }
            finally
            {
                _arincOpLock.Release();
            }
        }

        private async Task<bool> AutoSendAb28vSupplyAndReadTelemetryAsync(CancellationToken token, ObservableCollection<string> failures)
        {
            await _arincOpLock.WaitAsync(token);
            try
            {
                TelemetryVoltageText = "--";
                TelemetryRxDataText = "--";

                AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试：发送测试指令 {TestCommandName}");
                try { await _arinc.ClearRxFifoAsync(TelemetryRxChannel); } catch { }
                await Task.Delay(20, token);

                await _arinc.SendBenchCommandOnlyAsync(
                    SetVoltageTxChannel,
                    GetTestCommandBytes(),
                    msg => AddLog(msg),
                    token);

                // 发送后立即开始接收，不要延时
                token.ThrowIfCancellationRequested();

                if (IsSafetyChannelTest)
                {
                    var resp = await WaitAirTelemetryVoltageAsync(
                        timeoutMs: 4000,
                        log: msg => AddLog(msg),
                        token);

                    if (resp == null)
                        return false;

                    if (!TryParseAirTelemetryVoltage(resp, out var voltage))
                    {
                        failures.Add($"回采电压解析失败: 0x{FormatBytesHex(resp)}");
                        return true;
                    }

                    TelemetryRxDataText = $"0x{FormatBytesHex(resp)}";
                    TelemetryVoltageText = $"{voltage:0.000}V";

                    if (_activeSupplyVoltage.HasValue)
                    {
                        if (_activeSupplyVoltage.Value >= 31.0)
                        {
                            TelemetryRxData32Text = TelemetryRxDataText;
                            TelemetryVoltage32Text = TelemetryVoltageText;
                        }
                        else
                        {
                            TelemetryRxData28Text = TelemetryRxDataText;
                            TelemetryVoltage28Text = TelemetryVoltageText;
                        }
                    }

                    if (!IsWithin(voltage, 2.375, 2.625))
                        failures.Add($"回采电压={voltage:0.000}V 不在[2.375,2.625]V");

                    return true;
                }

                var (resp15, resp5) = await WaitVbitPairAsync(
                    timeoutMs: 4000,
                    log: msg => AddLog(msg),
                    token);

                if (resp15 == null || resp5 == null)
                    return false;

                if (!TryParseVbitValue(resp15, Vbit15Prefix4, out var v15))
                {
                    failures.Add($"15V_VBIT 解析失败: 0x{FormatBytesHex(resp15)}");
                    return true;
                }

                if (!TryParseVbitValue(resp5, Vbit5Prefix4, out var v5))
                {
                    failures.Add($"5V_VBIT 解析失败: 0x{FormatBytesHex(resp5)}");
                    return true;
                }

                TelemetryRxDataText = $"15V:0x{FormatBytesHex(resp15)}  5V:0x{FormatBytesHex(resp5)}";
                TelemetryVoltageText = $"15V={v15:0.000}V, 5V={v5:0.000}V";

                if (_activeSupplyVoltage.HasValue)
                {
                    if (_activeSupplyVoltage.Value >= 31.0)
                    {
                        TelemetryRxData32Text = TelemetryRxDataText;
                        TelemetryVoltage32Text = TelemetryVoltageText;
                    }
                    else
                    {
                        TelemetryRxData28Text = TelemetryRxDataText;
                        TelemetryVoltage28Text = TelemetryVoltageText;
                    }
                }

                if (!IsWithin(v15, 2.375, 2.625))
                    failures.Add($"15V_VBIT={v15:0.000}V 不在[2.375,2.625]V");
                if (!IsWithin(v5, 2.375, 2.625))
                    failures.Add($"5V_VBIT={v5:0.000}V 不在[2.375,2.625]V");

                return true;
            }
            finally
            {
                _arincOpLock.Release();
            }
        }

        private async Task OnSendEnterAtpAsync()
        {
            await _arincOpLock.WaitAsync();
            try
            {
                EnterAtpRxDataText = "--";
                AddLog($"[{DateTime.Now:HH:mm:ss}] 发送：进入ATP，TX={EnterAtpTxChannel}, RX={EnterAtpRxChannel}");

                await EnsurePowerSupplyConnectedAsync(CancellationToken.None);
                _arinc.IsRealProduct = true;
                _arinc.ArincRate = 100000.0;
                await _arinc.StartAsync(EnterAtpTxChannel, EnterAtpRxChannel, msg => AddLog(msg));

                byte[] resp = null;
                const int maxRetry = 3;
                for (int attempt = 1; attempt <= maxRetry && resp == null; attempt++)
                {
                    if (attempt > 1)
                        AddLog($"[{DateTime.Now:HH:mm:ss}] 进入ATP重试 ({attempt}/{maxRetry})");

                    try { await _arinc.ClearRxFifoAsync(EnterAtpRxChannel); } catch { }
                    await Task.Delay(20);

                    if (IsSafetyChannelTest)
                    {
                        await _arinc.SendBenchCommandOnlyAsync(
                            EnterAtpTxChannel,
                            AirSafetyAtpR,
                            msg => AddLog(msg),
                            CancellationToken.None);

                        resp = await _arinc.WaitBenchResponse8Async(
                            EnterAtpRxChannel,
                            b => b != null && b.SequenceEqual(AirSafetyAtpEnterOk),
                            timeoutMs: 1500,
                            msg => AddLog(msg),
                            CancellationToken.None);
                    }
                    else
                    {
                        await _arinc.SendBenchCommandOnlyAsync(
                            EnterAtpTxChannel,
                            AtpR,
                            msg => AddLog(msg),
                            CancellationToken.None);

                        resp = await _arinc.WaitBenchResponse8Async(
                            EnterAtpRxChannel,
                            b => b != null && b.SequenceEqual(AtpEnterOk),
                            timeoutMs: 1500,
                            msg => AddLog(msg),
                            CancellationToken.None);
                    }
                }

                if (resp == null)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 进入ATP超时，未收到OK");
                    return;
                }

                EnterAtpRxDataText = "0x" + FormatBytesHex(resp);

                if (_activeSupplyVoltage.HasValue)
                {
                    if (_activeSupplyVoltage.Value >= 31.0)
                        EnterAtpRxData32Text = EnterAtpRxDataText;
                    else
                        EnterAtpRxData28Text = EnterAtpRxDataText;
                }
                AddLog($"[{DateTime.Now:HH:mm:ss}] 收到ATP OK");
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 进入ATP异常：{ex.Message}");
            }
            finally
            {
                _arincOpLock.Release();
            }
        }

        private async Task OnSendAb28vSupplyAsync()
        {
            await _arincOpLock.WaitAsync();
            try
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 发送：{TestCommandName}，TX={SetVoltageTxChannel}, RX={TelemetryRxChannel}");
                TelemetryRxDataText = "--";
                TelemetryVoltageText = "--";

                await EnsurePowerSupplyConnectedAsync(CancellationToken.None);
                _arinc.IsRealProduct = true;
                _arinc.ArincRate = 100000.0;
                await _arinc.StartAsync(EnterAtpTxChannel, EnterAtpRxChannel, msg => AddLog(msg));

                try { await _arinc.ClearRxFifoAsync(TelemetryRxChannel); } catch { }
                await Task.Delay(20);

                await _arinc.SendBenchCommandOnlyAsync(
                    SetVoltageTxChannel,
                    GetTestCommandBytes(),
                    msg => AddLog(msg),
                    CancellationToken.None);

                // 发送后立即开始接收，不要延时
                if (IsSafetyChannelTest)
                {
                    var resp = await WaitAirTelemetryVoltageAsync(
                        timeoutMs: 4000,
                        log: msg => AddLog(msg),
                        CancellationToken.None);

                    if (resp == null)
                    {
                        AddLog($"[{DateTime.Now:HH:mm:ss}] 回采电压超时");
                        return;
                    }

                    if (TryParseAirTelemetryVoltage(resp, out var voltage))
                    {
                        TelemetryRxDataText = $"0x{FormatBytesHex(resp)}";
                        TelemetryVoltageText = $"{voltage:0.000}V";

                        if (_activeSupplyVoltage.HasValue)
                        {
                            if (_activeSupplyVoltage.Value >= 31.0)
                            {
                                TelemetryRxData32Text = TelemetryRxDataText;
                                TelemetryVoltage32Text = TelemetryVoltageText;
                            }
                            else
                            {
                                TelemetryRxData28Text = TelemetryRxDataText;
                                TelemetryVoltage28Text = TelemetryVoltageText;
                            }
                        }
                    }
                    else
                    {
                        AddLog($"[{DateTime.Now:HH:mm:ss}] 回采电压解析失败: 0x{FormatBytesHex(resp)}");
                    }

                    return;
                }

                var (resp15, resp5) = await WaitVbitPairAsync(
                    timeoutMs: 4000,
                    log: msg => AddLog(msg),
                    CancellationToken.None);

                if (resp15 == null)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 15V_VBIT 回采超时");
                    return;
                }

                if (resp5 == null)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 5V_VBIT 回采超时");
                    return;
                }

                if (TryParseVbitValue(resp15, Vbit15Prefix4, out var v15) && TryParseVbitValue(resp5, Vbit5Prefix4, out var v5))
                {
                    TelemetryRxDataText = $"15V:0x{FormatBytesHex(resp15)}  5V:0x{FormatBytesHex(resp5)}";
                    TelemetryVoltageText = $"15V={v15:0.000}V, 5V={v5:0.000}V";

                    if (_activeSupplyVoltage.HasValue)
                    {
                        if (_activeSupplyVoltage.Value >= 31.0)
                        {
                            TelemetryRxData32Text = TelemetryRxDataText;
                            TelemetryVoltage32Text = TelemetryVoltageText;
                        }
                        else
                        {
                            TelemetryRxData28Text = TelemetryRxDataText;
                            TelemetryVoltage28Text = TelemetryVoltageText;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] {TestCommandName} 异常：{ex.Message}");
            }
            finally
            {
                _arincOpLock.Release();
            }
        }

        private async Task OnSendExitAtpAsync()
        {
            await _arincOpLock.WaitAsync();
            try
            {
                ExitAtpRxDataText = "--";
                AddLog($"[{DateTime.Now:HH:mm:ss}] 发送：退出ATP，TX={ExitAtpTxChannel}, RX={ExitAtpRxChannel}");

                await EnsurePowerSupplyConnectedAsync(CancellationToken.None);
                _arinc.IsRealProduct = true;
                _arinc.ArincRate = 100000.0;
                await _arinc.StartAsync(EnterAtpTxChannel, EnterAtpRxChannel, msg => AddLog(msg));

                try { await _arinc.ClearRxFifoAsync(ExitAtpRxChannel); } catch { }
                await Task.Delay(20);

                await _arinc.SendBenchCommandOnlyAsync(
                    ExitAtpTxChannel,
                    AtpE,
                    msg => AddLog(msg),
                    CancellationToken.None);

                var resp = await _arinc.WaitBenchResponse8Async(
                    ExitAtpRxChannel,
                    b => b != null && b.SequenceEqual(ExitOk),
                    timeoutMs: 2000,
                    msg => AddLog(msg),
                    CancellationToken.None);

                if (resp == null)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 退出ATP超时，未收到OK");
                    return;
                }

                ExitAtpRxDataText = "0x" + FormatBytesHex(resp);
                AddLog($"[{DateTime.Now:HH:mm:ss}] 收到退出ATP OK");
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 退出ATP异常：{ex.Message}");
            }
            finally
            {
                _arincOpLock.Release();
            }
        }

        protected void AddLog(string message)
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

        private byte[] GetTestCommandBytes()
        {
            return IsSafetyChannelTest ? S28vSupplyVoltage01 : Ab28vSupply;
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

            await _powerSupply.ApplyAsync(PowerSupplyChannel.CH1, voltage, currentLimit, token);
            await _powerSupply.SetOutputEnabledAsync(PowerSupplyChannel.CH1, true, token);
            SyncBoardPowerState(true, voltage);
        }

        private async Task PowerSupplyOutputOffAsync(CancellationToken token)
        {
            if (_powerSupply == null || !_powerSupply.IsConnected)
                await EnsurePowerSupplyConnectedAsync(token);

            await _powerSupply.SetOutputEnabledAsync(PowerSupplyChannel.CH1, false, token);
            SyncBoardPowerState(false, 0);
        }

        private void SyncBoardPowerState(bool powered, double voltage)
        {
            try
            {
                var service = Prism.Ioc.ContainerLocator.Container.Resolve(typeof(MeasureControl.Services.IBoardPowerService))
                    as MeasureControl.Services.IBoardPowerService;
                service?.SetPoweredState(powered, powered ? (IsSafetyChannelTest ? "空气安全板" : "空气控制板") : null, voltage);
            }
            catch
            {
            }
        }

        private async Task<PowerSupplyMeasurements> PowerSupplyReadMeasurementsAsync(CancellationToken token)
        {
            if (_powerSupply == null || !_powerSupply.IsConnected)
                return null;

            return await _powerSupply.ReadMeasurementsAsync(PowerSupplyChannel.CH1, options: null, cancellationToken: token);
        }

        private static bool Is15vVbitPayload(byte[] frame)
        {
            return IsPrefix4(frame, Vbit15Prefix4)
                && (Ab28vSupply == null || frame == null || !frame.SequenceEqual(Ab28vSupply));
        }

        private static bool Is5vVbitPayload(byte[] frame)
        {
            return IsPrefix4(frame, Vbit5Prefix4);
        }

        private static bool IsAnyVbitPayload(byte[] frame)
        {
            return Is15vVbitPayload(frame) || Is5vVbitPayload(frame);
        }

        private async Task<(byte[] Resp15, byte[] Resp5)> WaitVbitPairAsync(
            int timeoutMs,
            Action<string> log,
            CancellationToken token)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(Math.Max(200, timeoutMs));
            byte[] resp15 = null;
            byte[] resp5 = null;

            while (!token.IsCancellationRequested && DateTime.UtcNow <= deadline && (resp15 == null || resp5 == null))
            {
                int remainMs = (int)Math.Max(0, (deadline - DateTime.UtcNow).TotalMilliseconds);
                if (remainMs <= 0)
                    break;

                var resp = await _arinc.WaitBenchResponse8Async(
                    TelemetryRxChannel,
                    IsAnyVbitPayload,
                    timeoutMs: Math.Min(500, remainMs),
                    log,
                    token);

                if (resp == null)
                    continue;

                if (resp15 == null && Is15vVbitPayload(resp))
                    resp15 = resp;
                else if (resp5 == null && Is5vVbitPayload(resp))
                    resp5 = resp;
            }

            return (resp15, resp5);
        }

        private async Task<byte[]> WaitAirTelemetryVoltageAsync(
            int timeoutMs,
            Action<string> log,
            CancellationToken token)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(Math.Max(200, timeoutMs));

            while (!token.IsCancellationRequested && DateTime.UtcNow <= deadline)
            {
                int remainingMs = (int)Math.Max(100, (deadline - DateTime.UtcNow).TotalMilliseconds);

                var resp8 = await _arinc.WaitBenchResponse8Async(
                    TelemetryRxChannel,
                    null,  // 先不过滤，接收所有拼包完成的数据
                    timeoutMs: remainingMs,
                    log,
                    token);

                if (resp8 == null)
                    continue;

                log?.Invoke($"[{DateTime.Now:HH:mm:ss}] 收到回采数据: 0x{FormatBytesHex(resp8)}");

                // 检查是否是电压回采数据 (前缀 0x10 01 01 01 表示 S_28VSupply_Voltage01 响应)
                if (IsAirTelemetryVoltagePayload(resp8))
                    return resp8;

                log?.Invoke($"[{DateTime.Now:HH:mm:ss}] 数据前缀不匹配，继续等待...");
            }

            return null;
        }

        private static bool IsAirTelemetryVoltagePayload(byte[] frame)
        {
            if (frame == null || frame.Length != 8)
                return false;

            // 回采数据前缀: 0x10 01 01 01 (S_28VSupply_Voltage01 响应)
            // 或者 0x10 01 01 02/03 等变体
            return frame[0] == 0x10
                && frame[1] == 0x01
                && frame[2] == 0x01;
        }

        private static bool IsPrefix4(byte[] frame, byte[] prefix4)
        {
            if (frame == null || frame.Length != 8)
                return false;
            if (prefix4 == null || prefix4.Length != 4)
                return false;

            for (int i = 0; i < 4; i++)
            {
                if (frame[i] != prefix4[i])
                    return false;
            }

            return true;
        }

        private bool TryParseVbitValue(byte[] frame, byte[] prefix4, out double value)
        {
            return TryParseSingleVbitValue(frame, prefix4, out value);
        }

        private static bool TryParseSingleVbitValue(byte[] frame, byte[] prefix4, out double value)
        {
            value = 0;
            if (!IsPrefix4(frame, prefix4))
                return false;

            try
            {
                var fbytes = new byte[4] { frame[4], frame[5], frame[6], frame[7] };
                if (BitConverter.IsLittleEndian)
                    Array.Reverse(fbytes);
                float f = BitConverter.ToSingle(fbytes, 0);
                if (!float.IsNaN(f) && !float.IsInfinity(f) && f > -1000 && f < 1000)
                {
                    value = f;
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private static bool TryParseAirTelemetryVoltage(byte[] frame, out double value)
        {
            value = 0;
            if (!IsAirTelemetryVoltagePayload(frame))
                return false;

            var raw = ((uint)frame[4] << 24)
                | ((uint)frame[5] << 16)
                | ((uint)frame[6] << 8)
                | frame[7];
            value = raw / 1000.0;
            return true;
        }

        private static bool IsWithin(double value, double min, double max)
        {
            return value >= min && value <= max;
        }

        private static string FormatBytesHex(byte[] data)
        {
            if (data == null || data.Length == 0)
                return string.Empty;

            return string.Join(" ", data.Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));
        }

        private async Task OnSupplyVoltageAsync(double voltage)
        {
            await _arincOpLock.WaitAsync();
            try
            {
                _activeSupplyVoltage = voltage;
                AddLog($"[{DateTime.Now:HH:mm:ss}] 供电：程控电源输出 {voltage:0.###}V");
                await PowerSupplyApplyAsync(voltage, currentLimit: 3.0, CancellationToken.None);
                await Task.Delay(300);
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 供电异常：{ex.Message}");
            }
            finally
            {
                _arincOpLock.Release();
            }
        }

        private async Task OnPowerSupplyOutputOffAsync()
        {
            await _arincOpLock.WaitAsync();
            try
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 断电：关闭程控电源输出");
                await PowerSupplyOutputOffAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 断电异常：{ex.Message}");
            }
            finally
            {
                _arincOpLock.Release();
            }
        }

        private async Task OnReadPowerSupplyMeasurementsAsync()
        {
            await _arincOpLock.WaitAsync();
            try
            {
                var ms = await PowerSupplyReadMeasurementsAsync(CancellationToken.None);
                if (ms?.Voltage?.Value != null)
                    DmmVoltageText = $"{ms.Voltage.Value:0.000} V";
                if (ms?.Current?.Value != null)
                    PowerSupplyMeasuredCurrentText = $"{ms.Current.Value:0.000} A";

                if (_activeSupplyVoltage.HasValue && _activeSupplyVoltage.Value >= 31.0)
                {
                    if (ms?.Voltage?.Value != null)
                        DmmVoltage32Text = $"{ms.Voltage.Value:0.000} V";
                    if (ms?.Current?.Value != null)
                        PowerSupplyMeasuredCurrent32Text = $"{ms.Current.Value:0.000} A";
                }
                else if (_activeSupplyVoltage.HasValue)
                {
                    if (ms?.Voltage?.Value != null)
                        DmmVoltage28Text = $"{ms.Voltage.Value:0.000} V";
                    if (ms?.Current?.Value != null)
                        PowerSupplyMeasuredCurrent28Text = $"{ms.Current.Value:0.000} A";
                }

                var vText = ms?.Voltage?.Value != null ? $"{ms.Voltage.Value:0.000}V" : "--";
                var iText = ms?.Current?.Value != null ? $"{ms.Current.Value:0.000}A" : "--";
                AddLog($"[{DateTime.Now:HH:mm:ss}] 记录供电电源读数 V={vText}, I={iText}");
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 读取电源读数异常：{ex.Message}");
            }
            finally
            {
                _arincOpLock.Release();
            }
        }
    }
}
