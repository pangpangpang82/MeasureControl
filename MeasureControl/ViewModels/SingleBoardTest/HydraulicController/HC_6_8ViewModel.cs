using Prism.Commands;
using Prism.Mvvm;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MeasureControl.Models.Devices;
using MeasureControl.Models.Devices.DeviceCategories;
using MeasureControl.Services;
using MeasureControl.Services.HardwareApis;

namespace MeasureControl.ViewModels.SingleBoardTest.HydraulicController
{
    /// <summary>
    /// HC_6_8 测试项：输出有效性测试（Output Valid 控制验证）
    /// 测试目的：验证液压控制器通过 ARINC429 接收"输出有效"指令后，
    ///          能够正确控制输出针脚（Pin9-15）的开关状态。
    /// 测试方法：
    /// 1) 给被测板供电 28V。
    /// 2) 开路测试：不发送指令，测量 Pin9-15 对地阻抗（应为高阻，>100kΩ）。
    /// 3) 通路测试：通过 ARINC429 发送"输出有效"指令（Label=65oct），
    ///    再测量 Pin9-15 对地阻抗（应为低阻，<10Ω）。
    /// 4) 所有针脚都满足判据则“合格”。
    /// </summary>
    public class HC_6_8ViewModel : BindableBase
    {
        // 电源配置（给被测板供电）
        private const string PowerSupplyIpAddress = "192.168.1.15";
        private const double InputVoltageV = 28.0;
        private const double InputCurrentA = 0.1;

        // 万用表和矩阵开关配置
        private const string DmmIpAddress = "192.168.1.13";
        private const string MatrixIpAddress = "192.168.1.3";

        // 矩阵开关槽位配置
        private const int MatrixSlotCommon = 4;       // 公共端槽位
        private const int MatrixSlotPinRoute = 1;     // 针脚路由槽位

        // ARINC429 发送配置
        private const int TxChannelIndex = 0; // 发送通道1 => index 0
        private const double ArincRate = 12500.0;

        // ARINC429 指令参数（输出有效控制指令）
        private const byte LabelCmdDec = 53; // 65(oct)
        private const byte CmdSdi = 0;

        // 阻抗判据
        private const double OpenPassThresholdOhm = 100_000.0;   // 开路阻抗阈值：>100kΩ 为合格
        private const double ClosePassThresholdOhm = 10.0;       // 通路阻抗阈值：<10Ω 为合格

        private readonly SemaphoreSlim _measureLock = new SemaphoreSlim(1, 1);
        private readonly IPxiChassisService _pxiChassisService;
        private readonly ISingleBoardTestContextService _singleBoardTestContext;

        private const string TestItemName = "离散量输出测试";

        private CancellationTokenSource _manualCts;
        private CancellationTokenSource _autoCts;

        private IDmmApi _dmm;
        private IPowerSupplyApi _power;
        private IArt4229Api _arinc;

        private bool _isManualTestRunning;
        private bool _isAutoTestRunning;
        private bool _canMeasure;

        private bool _measuredOpen;
        private bool _measuredClose;
        private bool _manualAborted;

        private string _lastTestTime = "--";
        private string _lastTestResult = "--";
        private string _previousTestTime = "--";
        private string _previousTestResult = "--";

        private string _openPin9Text = "--";
        private string _openPin10Text = "--";
        private string _openPin11Text = "--";
        private string _openPin12Text = "--";
        private string _openPin13Text = "--";
        private string _openPin14Text = "--";
        private string _openPin15Text = "--";

        private string _closePin9Text = "--";
        private string _closePin10Text = "--";
        private string _closePin11Text = "--";
        private string _closePin12Text = "--";
        private string _closePin13Text = "--";
        private string _closePin14Text = "--";
        private string _closePin15Text = "--";

        private readonly Dictionary<int, double?> _openValuesByPin = new Dictionary<int, double?>();
        private readonly Dictionary<int, double?> _closeValuesByPin = new Dictionary<int, double?>();

        public HC_6_8ViewModel(IPxiChassisService pxiChassisService, ISingleBoardTestContextService singleBoardTestContext)
        {
            _pxiChassisService = pxiChassisService;
            _singleBoardTestContext = singleBoardTestContext;

            ManualTestCommand = new DelegateCommand(async () => await OnManualTestAsync());
            AutoTestCommand = new DelegateCommand(async () => await OnAutoTestAsync());
            MeasureOpenCommand = new DelegateCommand(async () => await OnMeasureOpenAsync(), () => CanMeasureOpen);
            MeasureCloseCommand = new DelegateCommand(async () => await OnMeasureCloseAsync(), () => CanMeasureClose);
            ClearLogCommand = new DelegateCommand(() => Logs.Clear());

            LoadLastTestResultFromProject();
        }

        private void LoadLastTestResultFromProject()
        {
            var testItemNode = _singleBoardTestContext?.GetCurrentTestItemNode(TestItemName);
            if (testItemNode != null)
            {
                if (!string.IsNullOrWhiteSpace(testItemNode.LastTestTime))
                {
                    _lastTestTime = testItemNode.LastTestTime;
                    RaisePropertyChanged(nameof(LastTestTime));
                }
                if (!string.IsNullOrWhiteSpace(testItemNode.LastTestResult))
                {
                    _lastTestResult = testItemNode.LastTestResult;
                    RaisePropertyChanged(nameof(LastTestResult));
                }
            }
        }

        private void SaveTestResultToProject()
        {
            var testItemNode = _singleBoardTestContext?.GetCurrentTestItemNode(TestItemName);
            if (testItemNode != null)
            {
                testItemNode.LastTestTime = LastTestTime;
                testItemNode.LastTestResult = LastTestResult;
            }
        }

        public DelegateCommand ManualTestCommand { get; }
        public DelegateCommand AutoTestCommand { get; }
        public DelegateCommand MeasureOpenCommand { get; }
        public DelegateCommand MeasureCloseCommand { get; }
        public DelegateCommand ClearLogCommand { get; }

        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();

        public bool IsManualTestRunning
        {
            get => _isManualTestRunning;
            set
            {
                if (SetProperty(ref _isManualTestRunning, value))
                {
                    RaisePropertyChanged(nameof(CanMeasureOpen));
                    RaisePropertyChanged(nameof(CanMeasureClose));
                    MeasureOpenCommand?.RaiseCanExecuteChanged();
                    MeasureCloseCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        public bool IsAutoTestRunning
        {
            get => _isAutoTestRunning;
            set => SetProperty(ref _isAutoTestRunning, value);
        }

        public bool CanMeasure
        {
            get => _canMeasure;
            private set
            {
                if (SetProperty(ref _canMeasure, value))
                {
                    RaisePropertyChanged(nameof(CanMeasureOpen));
                    RaisePropertyChanged(nameof(CanMeasureClose));
                    MeasureOpenCommand?.RaiseCanExecuteChanged();
                    MeasureCloseCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        public bool CanMeasureOpen => IsManualTestRunning && CanMeasure && !_measuredOpen;
        public bool CanMeasureClose => IsManualTestRunning && CanMeasure && _measuredOpen && !_measuredClose;

        /// <summary>
        /// 整板串行自动测试入口。
        /// 由外部(整板自动测试)调用，支持 await 等待完成，并通过 CancellationToken 实现“立即停止当前测量”。
        /// 返回值仅为“合格/不合格”。
        /// </summary>
        public async Task<string> RunOnceAsync(CancellationToken cancellationToken)
        {
            if (IsAutoTestRunning)
            {
                await StopAutoTestAsync().ConfigureAwait(false);
            }

            if (IsManualTestRunning)
            {
                await StopManualTestAsync().ConfigureAwait(false);
            }

            _autoCts?.Cancel();
            _autoCts?.Dispose();
            _autoCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            try
            {
                return await ExecuteAutoTestAsync(_autoCts.Token).ConfigureAwait(false);
            }
            finally
            {
                _autoCts?.Dispose();
                _autoCts = null;
            }
        }

        public string LastTestTime { get => _lastTestTime; private set => SetProperty(ref _lastTestTime, value); }
        public string LastTestResult { get => _lastTestResult; private set => SetProperty(ref _lastTestResult, value); }
        public string PreviousTestTime { get => _previousTestTime; private set => SetProperty(ref _previousTestTime, value); }
        public string PreviousTestResult { get => _previousTestResult; private set => SetProperty(ref _previousTestResult, value); }

        public string OpenPin9Text { get => _openPin9Text; private set => SetProperty(ref _openPin9Text, value); }
        public string OpenPin10Text { get => _openPin10Text; private set => SetProperty(ref _openPin10Text, value); }
        public string OpenPin11Text { get => _openPin11Text; private set => SetProperty(ref _openPin11Text, value); }
        public string OpenPin12Text { get => _openPin12Text; private set => SetProperty(ref _openPin12Text, value); }
        public string OpenPin13Text { get => _openPin13Text; private set => SetProperty(ref _openPin13Text, value); }
        public string OpenPin14Text { get => _openPin14Text; private set => SetProperty(ref _openPin14Text, value); }
        public string OpenPin15Text { get => _openPin15Text; private set => SetProperty(ref _openPin15Text, value); }

        public string ClosePin9Text { get => _closePin9Text; private set => SetProperty(ref _closePin9Text, value); }
        public string ClosePin10Text { get => _closePin10Text; private set => SetProperty(ref _closePin10Text, value); }
        public string ClosePin11Text { get => _closePin11Text; private set => SetProperty(ref _closePin11Text, value); }
        public string ClosePin12Text { get => _closePin12Text; private set => SetProperty(ref _closePin12Text, value); }
        public string ClosePin13Text { get => _closePin13Text; private set => SetProperty(ref _closePin13Text, value); }
        public string ClosePin14Text { get => _closePin14Text; private set => SetProperty(ref _closePin14Text, value); }
        public string ClosePin15Text { get => _closePin15Text; private set => SetProperty(ref _closePin15Text, value); }

        /// <summary>
        /// 手动测试流程
        /// 进入手动模式后，先初始化万用表和电源，
        /// 然后由用户分别点击"测量开路"和"测量通路"按钮执行测量。
        /// </summary>
        private async Task OnManualTestAsync()
        {
            if (IsManualTestRunning)
            {
                await StopManualTestAsync().ConfigureAwait(false);
                return;
            }

            if (IsAutoTestRunning)
            {
                await StopAutoTestAsync().ConfigureAwait(false);
            }

            IsManualTestRunning = true;
            CanMeasure = false;
            _manualAborted = false;
            _measuredOpen = false;
            _measuredClose = false;

            ResetAllDisplays();
            _openValuesByPin.Clear();
            _closeValuesByPin.Clear();

            _manualCts?.Cancel();
            _manualCts?.Dispose();
            _manualCts = new CancellationTokenSource();

            Log("开始手动测试");
            Log($"判据: 开路>={OpenPassThresholdOhm:0}Ω, 通路<={ClosePassThresholdOhm:0}Ω");

            try
            {
                _dmm ??= new DmmSocketApi();
                await _dmm.ConnectAsync(DmmIpAddress, _manualCts.Token).ConfigureAwait(false);
                Log("万用表连接成功");

                await EnsurePowerAsync(_manualCts.Token).ConfigureAwait(false);
                Log("28V电源上电成功");

                CanMeasure = true;
            }
            catch (Exception ex)
            {
                await AbortManualTestAsync($"手动测试初始化失败，中止: {ex.Message}").ConfigureAwait(false);
            }
        }

        /// <summary>
        /// 自动测试流程
        /// 自动依次执行开路测试和通路测试，所有针脚都满足判据则“合格”。
        /// </summary>
        private async Task OnAutoTestAsync()
        {
            if (IsAutoTestRunning)
            {
                await StopAutoTestAsync().ConfigureAwait(false);
                return;
            }

            if (IsManualTestRunning)
            {
                await StopManualTestAsync().ConfigureAwait(false);
            }

            IsAutoTestRunning = true;
            CanMeasure = false;
            _manualAborted = false;
            _measuredOpen = false;
            _measuredClose = false;

            ResetAllDisplays();
            _openValuesByPin.Clear();
            _closeValuesByPin.Clear();

            _autoCts?.Cancel();
            _autoCts?.Dispose();
            _autoCts = new CancellationTokenSource();

            Log("开始自动测试");
            Log($"判据: 开路>={OpenPassThresholdOhm:0}Ω, 通路<={ClosePassThresholdOhm:0}Ω");

            try
            {
                _ = await ExecuteAutoTestAsync(_autoCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Log("自动测试已停止");
                await StopAutoTestAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log($"自动测试异常: {ex.Message}");
                await StopAutoTestAsync().ConfigureAwait(false);
            }
            finally
            {
                _autoCts?.Dispose();
                _autoCts = null;
            }
        }

        private async Task<string> ExecuteAutoTestAsync(CancellationToken cancellationToken)
        {
            IsAutoTestRunning = true;
            CanMeasure = false;
            _manualAborted = false;
            _measuredOpen = false;
            _measuredClose = false;

            ResetAllDisplays();
            _openValuesByPin.Clear();
            _closeValuesByPin.Clear();

            Log("开始自动测试");
            Log($"判据: 开路>={OpenPassThresholdOhm:0}Ω, 通路<={ClosePassThresholdOhm:0}Ω");

            try
            {
                _dmm ??= new DmmSocketApi();
                await _dmm.ConnectAsync(DmmIpAddress, cancellationToken).ConfigureAwait(false);
                Log("万用表连接成功");

                await EnsurePowerAsync(cancellationToken).ConfigureAwait(false);
                Log("28V电源上电成功");

                var okOpen = await MeasureOpenAsync(cancellationToken).ConfigureAwait(false);
                if (!okOpen)
                {
                    await StopAutoTestAsync().ConfigureAwait(false);
                    return "不合格";
                }

                await Task.Delay(100, cancellationToken).ConfigureAwait(false);

                var okClose = await MeasureCloseAsync(cancellationToken).ConfigureAwait(false);
                if (!okClose)
                {
                    await StopAutoTestAsync().ConfigureAwait(false);
                    return "不合格";
                }

                _measuredOpen = true;
                _measuredClose = true;
                await TryFinalizeAsync().ConfigureAwait(false);
                await StopAutoTestAsync().ConfigureAwait(false);

                return LastTestResult;
            }
            catch (OperationCanceledException)
            {
                Log("自动测试已停止");
                await StopAutoTestAsync().ConfigureAwait(false);
                throw;
            }
            catch (Exception ex)
            {
                Log($"自动测试异常: {ex.Message}");
                await StopAutoTestAsync().ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>
        /// 测量开路（手动模式）
        /// 不发送 ARINC429 指令，直接测量 Pin9-15 对地阻抗。
        /// </summary>
        private async Task OnMeasureOpenAsync()
        {
            var token = _manualCts?.Token ?? CancellationToken.None;
            var ok = await MeasureOpenAsync(token).ConfigureAwait(false);

            if (!IsManualTestRunning || _manualAborted)
                return;

            if (ok)
            {
                _measuredOpen = true;
                RaisePropertyChanged(nameof(CanMeasureOpen));
                RaisePropertyChanged(nameof(CanMeasureClose));
                MeasureOpenCommand?.RaiseCanExecuteChanged();
                MeasureCloseCommand?.RaiseCanExecuteChanged();
            }

            await TryFinalizeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 测量通路（手动模式）
        /// 先发送 ARINC429"输出有效"指令，再测量 Pin9-15 对地阻抗。
        /// </summary>
        private async Task OnMeasureCloseAsync()
        {
            var token = _manualCts?.Token ?? CancellationToken.None;
            var ok = await MeasureCloseAsync(token).ConfigureAwait(false);

            if (!IsManualTestRunning || _manualAborted)
                return;

            if (ok)
            {
                _measuredClose = true;
                RaisePropertyChanged(nameof(CanMeasureClose));
                MeasureCloseCommand?.RaiseCanExecuteChanged();
            }

            await TryFinalizeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 开路测试（核心测量方法）
        /// 流程：
        /// 1) 不发送 ARINC429 指令（输出应处于关闭状态）。
        /// 2) 依次测量 Pin9-15 对地阻抗（应为高阻，>100kΩ）。
        /// 3) 通过矩阵开关连接万用表到各针脚进行测量。
        /// </summary>
        private async Task<bool> MeasureOpenAsync(CancellationToken cancellationToken)
        {
            await _measureLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                Log("开路: 开始测量针脚9~15对地阻抗");

                for (int pin = 9; pin <= 15; pin++)
                {
                    var (value, text) = await MeasureOnePinResistanceAsync(pin, cancellationToken).ConfigureAwait(false);
                    _openValuesByPin[pin] = value;
                    SetOpenPinText(pin, text);

                    if (cancellationToken.IsCancellationRequested)
                        return false;

                    await Task.Delay(50, cancellationToken).ConfigureAwait(false);
                }

                Log("开路: 测量完成");
                return true;
            }
            catch (Exception ex)
            {
                Log($"开路: 测量异常: {ex.Message}");
                if (IsManualTestRunning)
                {
                    await AbortManualTestAsync("开路: 测量异常，手动测试中止").ConfigureAwait(false);
                }
                return false;
            }
            finally
            {
                try { await DisconnectAllMatrixRoutesAsync().ConfigureAwait(false); } catch { }
                _measureLock.Release();
            }
        }

        /// <summary>
        /// 通路测试（核心测量方法）
        /// 流程：
        /// 1) 通过 ARINC429 发送"输出有效"指令（Label=65oct）。
        /// 2) 等待 100ms 让输出稳定。
        /// 3) 依次测量 Pin9-15 对地阻抗（应为低阻，<10Ω）。
        /// 4) 测量完成后发送"关闭输出"指令。
        /// </summary>
        private async Task<bool> MeasureCloseAsync(CancellationToken cancellationToken)
        {
            await _measureLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                Log("通路: 下发429开启输出有效指令");
                await SendOutputEnableAsync(enable: true, cancellationToken).ConfigureAwait(false);
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);

                Log("通路: 开始测量针脚9~15对地阻抗");
                for (int pin = 9; pin <= 15; pin++)
                {
                    var (value, text) = await MeasureOnePinResistanceAsync(pin, cancellationToken).ConfigureAwait(false);
                    _closeValuesByPin[pin] = value;
                    SetClosePinText(pin, text);

                    if (cancellationToken.IsCancellationRequested)
                        return false;

                    await Task.Delay(50, cancellationToken).ConfigureAwait(false);
                }

                Log("通路: 测量完成");
                return true;
            }
            catch (Exception ex)
            {
                Log($"通路: 测量异常: {ex.Message}");
                if (IsManualTestRunning)
                {
                    await AbortManualTestAsync("通路: 测量异常，手动测试中止").ConfigureAwait(false);
                }
                return false;
            }
            finally
            {
                try
                {
                    Log("通路: 下发429关闭输出有效指令");
                    await SendOutputEnableAsync(enable: false, CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                }

                try { await DisconnectAllMatrixRoutesAsync().ConfigureAwait(false); } catch { }
                _measureLock.Release();
            }
        }

        /// <summary>
        /// 测量单个针脚对地阻抗（通过矩阵开关连接万用表）
        /// </summary>
        /// <param name="pin">针脚编号（9-15）</param>
        /// <returns>阻抗值和显示文本</returns>
        private async Task<(double? Value, string Text)> MeasureOnePinResistanceAsync(int pin, CancellationToken cancellationToken)
        {
            if (_dmm == null)
                throw new InvalidOperationException("万用表未连接");

            await DisconnectAllMatrixRoutesAsync().ConfigureAwait(false);

            var matrix = MatrixControlService.Instance;

            var okCommon = await matrix.ConnectNodesAsync("I4", "O0", MatrixSlotCommon, MatrixIpAddress).ConfigureAwait(false);
            var outNode = $"O{pin - 9}";
            var okPin = await matrix.ConnectNodesAsync("I1", outNode, MatrixSlotPinRoute, MatrixIpAddress).ConfigureAwait(false);

            Log($"PIN{pin}: 矩阵连接 {(okCommon && okPin ? "成功" : "失败")} - I4-O0(slot{MatrixSlotCommon}), I1-{outNode}(slot{MatrixSlotPinRoute})");
            if (!okCommon || !okPin)
            {
                return (null, "--");
            }

            DmmReading reading = await _dmm.ReadOnceAsync(DmmMeasureMode.RES, new DmmReadOptions { TimeoutMilliseconds = 8000 }, cancellationToken)
                .ConfigureAwait(false);

            if (reading == null)
            {
                return (null, "--");
            }

            if (reading.IsOverrange)
            {
                return (null, "OL");
            }

            if (reading.Value == null)
            {
                return (null, "--");
            }

            var text = FormatOhmText(reading);
            Log($"PIN{pin}: 读数 Raw={reading.Raw ?? ""} Value={(reading.Value?.ToString("0.###", CultureInfo.InvariantCulture) ?? "--")} Unit={reading.Unit ?? ""}");
            return (reading.Value, text);
        }

        private static string FormatOhmText(DmmReading reading)
        {
            if (reading == null)
                return "--";

            if (reading.IsOverrange)
                return "OL";

            if (reading.Value == null)
                return "--";

            return $"{reading.Value.Value:0.###} Ω";
        }

        private void SetOpenPinText(int pin, string text)
        {
            switch (pin)
            {
                case 9: OpenPin9Text = text; break;
                case 10: OpenPin10Text = text; break;
                case 11: OpenPin11Text = text; break;
                case 12: OpenPin12Text = text; break;
                case 13: OpenPin13Text = text; break;
                case 14: OpenPin14Text = text; break;
                case 15: OpenPin15Text = text; break;
            }
        }

        private void SetClosePinText(int pin, string text)
        {
            switch (pin)
            {
                case 9: ClosePin9Text = text; break;
                case 10: ClosePin10Text = text; break;
                case 11: ClosePin11Text = text; break;
                case 12: ClosePin12Text = text; break;
                case 13: ClosePin13Text = text; break;
                case 14: ClosePin14Text = text; break;
                case 15: ClosePin15Text = text; break;
            }
        }

        /// <summary>
        /// 通过 ARINC429 发送"输出有效"控制指令
        /// </summary>
        /// <param name="enable">true=开启输出，false=关闭输出</param>
        private async Task SendOutputEnableAsync(bool enable, CancellationToken cancellationToken)
        {
            await EnsureArincTxAsync(cancellationToken).ConfigureAwait(false);

            var data19 = enable ? BuildEnableData19() : 0u;
            var word = _arinc.BuildRawWord(LabelCmdDec, CmdSdi, data19, ssm: 0, applyOddParity: true);
            await _arinc.SendWordsSingleAsync(TxChannelIndex, new[] { word }, Art4229Parity.Odd, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// 构建"输出有效"指令的数据域（19-bit）
        /// 按照协议要求设置特定位为 1，其余为 0
        /// </summary>
        private static uint BuildEnableData19()
        {
            uint data19 = 0;

            // Mapping: bit10..28 => data19 bit0..18
            data19 |= 1u << 0; // bit10 = 1
            // bit11 = 0

            // bit12..16 = 1
            for (int b = 2; b <= 6; b++)
                data19 |= 1u << b;

            // bit17 = 0
            data19 |= 1u << 8; // bit18 = 1
            // bit19 = 0

            // bit20..24 = 1
            for (int b = 10; b <= 14; b++)
                data19 |= 1u << b;

            // bit25..28 = 0
            return data19;
        }

        /// <summary>
        /// 确保 ARINC429 发送通道已打开并配置（Odd parity, 标准 429 格式）
        /// </summary>
        private async Task EnsureArincTxAsync(CancellationToken cancellationToken)
        {
            if (_arinc == null)
            {
                var device = FindFirstArincDevice();
                if (device == null)
                    throw new InvalidOperationException("未找到ART4227/ART4229(ARINC429)板卡，无法发送429指令");

                _arinc = new Art4229Api(device, deviceIndex: 0);
            }

            if (!_arinc.IsConnected)
            {
                await _arinc.ConnectAsync(cancellationToken).ConfigureAwait(false);
            }

            await _arinc.OpenTxAsync(TxChannelIndex, cancellationToken).ConfigureAwait(false);
            await _arinc.ConfigureTxAsync(
                TxChannelIndex,
                rate: ArincRate,
                mode: Art4229TxMode.Single,
                parity: Art4229Parity.Odd,
                wordFormat: Art4229WordFormat.Standard429,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// 在 PXI 机箱中查找 ARINC429 板卡（4227/4229）
        /// </summary>
        private DeviceBase FindFirstArincDevice()
        {
            var chassisList = _pxiChassisService?.GetAllChassis();
            if (chassisList == null)
                return null;

            foreach (var chassis in chassisList)
            {
                var device = chassis?.Devices?.FirstOrDefault(d =>
                    (d?.Model?.IndexOf("4227", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.Model?.IndexOf("4229", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.Model?.IndexOf("ARINC", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.Model?.IndexOf("429", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.Name?.IndexOf("4227", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.Name?.IndexOf("4229", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.Name?.IndexOf("429", StringComparison.OrdinalIgnoreCase) >= 0));

                if (device != null)
                    return device;
            }

            return null;
        }

        private async Task DisconnectAllMatrixRoutesAsync()
        {
            var matrix = MatrixControlService.Instance;

            _ = await matrix.DisconnectNodesAsync("I4", "O0", MatrixSlotCommon, MatrixIpAddress).ConfigureAwait(false);
            for (int i = 0; i <= 6; i++)
            {
                _ = await matrix.DisconnectNodesAsync("I1", $"O{i}", MatrixSlotPinRoute, MatrixIpAddress).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// 当开路和通路都已测量完成时，按阈值范围判定结果，并更新"上次/本次"测试结论。
        /// 判据：开路 >100kΩ, 通路 <10Ω
        /// </summary>
        private async Task TryFinalizeAsync()
        {
            if (!_measuredOpen || !_measuredClose)
                return;

            var openPass = true;
            var closePass = true;

            for (int pin = 9; pin <= 15; pin++)
            {
                var vOpen = _openValuesByPin.TryGetValue(pin, out var o) ? o : null;
                var vClose = _closeValuesByPin.TryGetValue(pin, out var c) ? c : null;

                if (!(vOpen != null && vOpen >= OpenPassThresholdOhm))
                    openPass = false;

                if (!(vClose != null && vClose <= ClosePassThresholdOhm))
                    closePass = false;
            }

            var pass = openPass && closePass;

            PreviousTestTime = LastTestTime;
            PreviousTestResult = LastTestResult;
            LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            LastTestResult = pass ? "合格" : "不合格";

            SaveTestResultToProject();
            Log($"最终结果: {LastTestResult}");

            if (IsManualTestRunning)
            {
                await StopManualTestAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// 中止手动测试（通常用于初始化失败、测量超时、测量异常等）
        /// </summary>
        private async Task AbortManualTestAsync(string reason)
        {
            _manualAborted = true;
            if (!string.IsNullOrWhiteSpace(reason))
            {
                Log(reason);
            }

            await StopManualTestAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 停止手动测试并释放硬件资源（断开矩阵开关、万用表、ARINC429、关闭电源输出）
        /// </summary>
        private async Task StopManualTestAsync()
        {
            try
            {
                CanMeasure = false;
                _manualCts?.Cancel();
            }
            catch
            {
            }

            Log("手动测试停止/结束，正在断开矩阵、万用表与429...");

            try { await DisconnectAllMatrixRoutesAsync().ConfigureAwait(false); } catch { }

            try
            {
                if (_dmm != null)
                {
                    await _dmm.DisconnectAsync().ConfigureAwait(false);
                }
            }
            catch
            {
            }

            try
            {
                await CleanupArincAsync().ConfigureAwait(false);
            }
            catch
            {
            }

            try
            {
                await CleanupPowerAsync().ConfigureAwait(false);
            }
            catch
            {
            }

            IsManualTestRunning = false;
            Log("手动测试已结束");
        }

        /// <summary>
        /// 停止自动测试并释放硬件资源
        /// </summary>
        private async Task StopAutoTestAsync()
        {
            try
            {
                _autoCts?.Cancel();
            }
            catch
            {
            }

            Log("自动测试停止/结束，正在断开矩阵、万用表与429...");

            try { await DisconnectAllMatrixRoutesAsync().ConfigureAwait(false); } catch { }

            try
            {
                if (_dmm != null)
                {
                    await _dmm.DisconnectAsync().ConfigureAwait(false);
                }
            }
            catch
            {
            }

            try
            {
                await CleanupArincAsync().ConfigureAwait(false);
            }
            catch
            {
            }

            try
            {
                await CleanupPowerAsync().ConfigureAwait(false);
            }
            catch
            {
            }

            IsAutoTestRunning = false;
            Log("自动测试已结束");
        }

        private async Task EnsurePowerAsync(CancellationToken cancellationToken)
        {
            _power ??= new PowerSupplySocketApi();
            await _power.ConnectAsync(PowerSupplyIpAddress, cancellationToken).ConfigureAwait(false);
            await _power.ApplyAsync(PowerSupplyChannel.CH1, InputVoltageV, InputCurrentA, cancellationToken).ConfigureAwait(false);
            await _power.ApplyAsync(PowerSupplyChannel.CH2, InputVoltageV, InputCurrentA, cancellationToken).ConfigureAwait(false);
            await _power.SetOutputEnabledAsync(PowerSupplyChannel.CH1, true, cancellationToken).ConfigureAwait(false);
            await _power.SetOutputEnabledAsync(PowerSupplyChannel.CH2, true, cancellationToken).ConfigureAwait(false);
            await Task.Delay(300, cancellationToken).ConfigureAwait(false);
        }

        private async Task CleanupPowerAsync()
        {
            try
            {
                if (_power != null)
                {
                    try { await _power.SetOutputEnabledAsync(PowerSupplyChannel.CH1, false, CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _power.SetOutputEnabledAsync(PowerSupplyChannel.CH2, false, CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _power.DisconnectAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _power.DisposeAsync().ConfigureAwait(false); } catch { }
                }
            }
            finally
            {
                _power = null;
            }
        }

        private async Task CleanupArincAsync()
        {
            try
            {
                if (_arinc != null)
                {
                    try { await _arinc.CloseTxAsync(TxChannelIndex, CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _arinc.DisconnectAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _arinc.DisposeAsync().ConfigureAwait(false); } catch { }
                }
            }
            catch
            {
            }
            finally
            {
                _arinc = null;
            }
        }

        private void ResetAllDisplays()
        {
            OpenPin9Text = "--";
            OpenPin10Text = "--";
            OpenPin11Text = "--";
            OpenPin12Text = "--";
            OpenPin13Text = "--";
            OpenPin14Text = "--";
            OpenPin15Text = "--";

            ClosePin9Text = "--";
            ClosePin10Text = "--";
            ClosePin11Text = "--";
            ClosePin12Text = "--";
            ClosePin13Text = "--";
            ClosePin14Text = "--";
            ClosePin15Text = "--";

            LastTestTime = "--";
            LastTestResult = "--";
            PreviousTestTime = "--";
            PreviousTestResult = "--";
        }

        private void Log(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            Logs.Add($"[{DateTime.Now:HH:mm:ss.fff}] {message}");
        }
    }
}
