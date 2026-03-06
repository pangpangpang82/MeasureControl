using Prism.Commands;
using Prism.Mvvm;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using MeasureControl.Models.Devices;
using MeasureControl.Models.Devices.DeviceCategories;
using MeasureControl.Services;
using MeasureControl.Services.HardwareApis;

namespace MeasureControl.ViewModels.SingleBoardTest.HydraulicController
{
    /// <summary>
    /// HC_6_5 测试项：差压信号测试（差压传感器校验）
    /// 测试目的：验证液压控制器在不同电流档位（4mA/10mA/20mA）下，
    ///          从 ARINC429 接收的差压数据是否正确。
    /// 测试方法：
    /// 1) 给被测板供电 28V。
    /// 2) 被测板会输出三种电流档位（4mA/10mA/20mA）的差压信号。
    /// 3) 从 ARINC429 接收差压数据，包括：
    ///    - DPT_SYS (Label=72oct, SDI=1/2/3) - 三路系统差压
    ///    - DPT_EDP2A (Label=71oct, SDI=2) - EDP2A 差压
    ///    - DPT_EMP (Label=73oct, SDI=2/3) - EMP2B/EMP3B 差压
    /// 4) 每个通道每个档位采集 5 帧数据取平均值，共 18 组数据（6通道×3电流）。
    /// </summary>
    public class HC_6_5ViewModel : BindableBase
    {
        // 电源配置（给被测板供电）
        private const string PowerSupplyIpAddress = "192.168.1.15";
        private const double InputVoltageV = 28.0;
        private const double InputCurrentA = 0.1;

        // ARINC429 接收配置
        private const int RxChannelIndex = 2;
        private const double ArincRate = 12500.0;

        // 差压单位
        private const string PressureUnit = "Psid";

        // 采样参数
        private const int SamplesPerMeasure = 5;      // 每路采集 5 帧取平均
        private const int SampleTimeoutMs = 5000;     // 采样超时 5 秒

        // 差压数据的 ARINC429 Label 定义
        private const byte LabelDptSysDec = 58; // 72(oct) - 系统差压
        private const byte LabelDptEdp2ADec = 57; // 71(oct) - EDP2A 差压
        private const byte LabelDptEmpDec = 59; // 73(oct) - EMP 差压

        // 差压数据的 SDI 定义（用于区分不同通道）
        private const byte SdiSys1 = 1;    // 系统1
        private const byte SdiSys2 = 2;    // 系统2
        private const byte SdiSys3 = 3;    // 系统3
        private const byte SdiEdp2A = 2;   // EDP2A
        private const byte SdiEmp2B = 2;   // EMP2B
        private const byte SdiEmp3B = 3;   // EMP3B

        // 差压的 ARINC429 编码参数（BNR：有符号二进制数）
        private const int DataBitLength = 9;
        private const double DataResolution = 1.0;
        private const int DataMsbPosition = 27;

        private readonly SemaphoreSlim _measureLock = new SemaphoreSlim(1, 1);
        private readonly IPxiChassisService _pxiChassisService;
        private readonly ISingleBoardTestContextService _singleBoardTestContext;

        private CancellationTokenSource _manualCts;
        private CancellationTokenSource _autoCts;

        private IPowerSupplyApi _power;
        private IArt4229Api _arinc;

        private const string TestItemName = "压差传感器信号采集测试";

        private bool _canMeasure;
        private bool _measured14;
        private bool _manualAborted;

        private bool _isManualTestRunning;
        private bool _isAutoTestRunning;
        private int _selectedTabIndex;

        private string _lastTestTime = "--";
        private string _lastTestResult = "--";
        private string _previousTestTime = "--";
        private string _previousTestResult = "--";
        private string _currentTestResult = "--";

        private string _dptEdp2A4mAText = "--";
        private string _dptEmp2B4mAText = "--";
        private string _dptEmp3B4mAText = "--";
        private string _dptSys14mAText = "--";
        private string _dptSys24mAText = "--";
        private string _dptSys34mAText = "--";

        private string _dptEdp2A20mAText = "--";
        private string _dptEmp2B20mAText = "--";
        private string _dptEmp3B20mAText = "--";
        private string _dptSys120mAText = "--";
        private string _dptSys220mAText = "--";
        private string _dptSys320mAText = "--";

        private string _dptEdp2A10mAText = "--";
        private string _dptEmp2B10mAText = "--";
        private string _dptEmp3B10mAText = "--";
        private string _dptSys110mAText = "--";
        private string _dptSys210mAText = "--";
        private string _dptSys310mAText = "--";

        public HC_6_5ViewModel(IPxiChassisService pxiChassisService, ISingleBoardTestContextService singleBoardTestContext)
        {
            _pxiChassisService = pxiChassisService;
            _singleBoardTestContext = singleBoardTestContext;

            ManualTestCommand = new DelegateCommand(async () => await OnManualTestAsync());
            AutoTestCommand = new DelegateCommand(async () => await OnAutoTestAsync());
            Measure14Command = new DelegateCommand(async () => await OnMeasure14Async(), () => CanMeasure14);
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
                    _previousTestTime = testItemNode.LastTestTime;
                    RaisePropertyChanged(nameof(PreviousTestTime));
                }
                if (!string.IsNullOrWhiteSpace(testItemNode.LastTestResult))
                {
                    _previousTestResult = testItemNode.LastTestResult;
                    RaisePropertyChanged(nameof(PreviousTestResult));
                }
            }
        }

        private void SaveTestResultToProject()
        {
            var testItemNode = _singleBoardTestContext?.GetCurrentTestItemNode(TestItemName);
            if (testItemNode != null)
            {
                testItemNode.LastTestTime = PreviousTestTime;
                testItemNode.LastTestResult = PreviousTestResult;
            }
        }

        public string CurrentTestResult
        {
            get => _currentTestResult;
            private set => SetProperty(ref _currentTestResult, value);
        }

        public DelegateCommand ManualTestCommand { get; }
        public DelegateCommand AutoTestCommand { get; }
        public DelegateCommand Measure14Command { get; }
        public DelegateCommand ClearLogCommand { get; }

        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();

        public bool IsManualTestRunning
        {
            get => _isManualTestRunning;
            set
            {
                if (SetProperty(ref _isManualTestRunning, value))
                {
                    RaisePropertyChanged(nameof(CanMeasure14));
                    Measure14Command?.RaiseCanExecuteChanged();
                }
            }
        }

        public bool IsAutoTestRunning
        {
            get => _isAutoTestRunning;
            set
            {
                if (SetProperty(ref _isAutoTestRunning, value))
                {
                    RaisePropertyChanged(nameof(CanMeasure14));
                    Measure14Command?.RaiseCanExecuteChanged();
                }
            }
        }

        public int SelectedTabIndex
        {
            get => _selectedTabIndex;
            set => SetProperty(ref _selectedTabIndex, value);
        }

        public bool CanMeasure
        {
            get => _canMeasure;
            private set
            {
                if (SetProperty(ref _canMeasure, value))
                {
                    RaisePropertyChanged(nameof(CanMeasure14));
                    Measure14Command?.RaiseCanExecuteChanged();
                }
            }
        }

        public bool CanMeasure14 => IsManualTestRunning && CanMeasure && !_measured14;

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

        public string PreviousTestTime
        {
            get => _previousTestTime;
            set => SetProperty(ref _previousTestTime, value);
        }

        public string PreviousTestResult
        {
            get => _previousTestResult;
            set => SetProperty(ref _previousTestResult, value);
        }

        public string DptEdp2A4mAText { get => _dptEdp2A4mAText; private set => SetProperty(ref _dptEdp2A4mAText, value); }
        public string DptEmp2B4mAText { get => _dptEmp2B4mAText; private set => SetProperty(ref _dptEmp2B4mAText, value); }
        public string DptEmp3B4mAText { get => _dptEmp3B4mAText; private set => SetProperty(ref _dptEmp3B4mAText, value); }
        public string DptSys14mAText { get => _dptSys14mAText; private set => SetProperty(ref _dptSys14mAText, value); }
        public string DptSys24mAText { get => _dptSys24mAText; private set => SetProperty(ref _dptSys24mAText, value); }
        public string DptSys34mAText { get => _dptSys34mAText; private set => SetProperty(ref _dptSys34mAText, value); }

        public string DptEdp2A20mAText { get => _dptEdp2A20mAText; private set => SetProperty(ref _dptEdp2A20mAText, value); }
        public string DptEmp2B20mAText { get => _dptEmp2B20mAText; private set => SetProperty(ref _dptEmp2B20mAText, value); }
        public string DptEmp3B20mAText { get => _dptEmp3B20mAText; private set => SetProperty(ref _dptEmp3B20mAText, value); }
        public string DptSys120mAText { get => _dptSys120mAText; private set => SetProperty(ref _dptSys120mAText, value); }
        public string DptSys220mAText { get => _dptSys220mAText; private set => SetProperty(ref _dptSys220mAText, value); }
        public string DptSys320mAText { get => _dptSys320mAText; private set => SetProperty(ref _dptSys320mAText, value); }

        public string DptEdp2A10mAText { get => _dptEdp2A10mAText; private set => SetProperty(ref _dptEdp2A10mAText, value); }
        public string DptEmp2B10mAText { get => _dptEmp2B10mAText; private set => SetProperty(ref _dptEmp2B10mAText, value); }
        public string DptEmp3B10mAText { get => _dptEmp3B10mAText; private set => SetProperty(ref _dptEmp3B10mAText, value); }
        public string DptSys110mAText { get => _dptSys110mAText; private set => SetProperty(ref _dptSys110mAText, value); }
        public string DptSys210mAText { get => _dptSys210mAText; private set => SetProperty(ref _dptSys210mAText, value); }
        public string DptSys310mAText { get => _dptSys310mAText; private set => SetProperty(ref _dptSys310mAText, value); }

        /// <summary>
        /// 手动测试流程
        /// 进入手动模式后，先初始化电源/ARINC429，
        /// 然后由用户点击"测量 1-4mA"按钮执行测量（会依次测量 4mA/20mA/10mA 三个档位）。
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
            CurrentTestResult = "--";
            CanMeasure = false;
            _manualAborted = false;
            _measured14 = false;

            ResetAllDisplays();

            _manualCts?.Cancel();
            _manualCts?.Dispose();
            _manualCts = new CancellationTokenSource();

            Log("开始手动测试");
            Log($"电源: CH1/CH2 {InputVoltageV:0.###}V {InputCurrentA:0.###}A, IP={PowerSupplyIpAddress}");
            Log($"ARINC429: RX通道{RxChannelIndex + 1}, 码率 {ArincRate:0}bps, DPT数据 bit19-27 UBNR(9bit) LSB=1");

            try
            {
                await EnsurePowerAsync(_manualCts.Token).ConfigureAwait(false);
                await EnsureArincRxAsync(_manualCts.Token).ConfigureAwait(false);
                CanMeasure = true;
                Log("手动测试初始化完成，可点击测量");
            }
            catch (Exception ex)
            {
                await AbortManualTestAsync($"手动测试初始化失败，中止: {ex.Message}").ConfigureAwait(false);
            }
        }

        /// <summary>
        /// 自动测试流程
        /// 自动依次测量 4mA/20mA/10mA 三个档位的所有通道差压数据，共 18 组数据。
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
            CurrentTestResult = "--";
            CanMeasure = false;
            _manualAborted = false;
            _measured14 = false;

            ResetAllDisplays();

            _autoCts?.Cancel();
            _autoCts?.Dispose();
            _autoCts = new CancellationTokenSource();

            Log("开始自动测试");

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
            _measured14 = false;

            ResetAllDisplays();

            Log("开始自动测试");

            try
            {
                await EnsurePowerAsync(cancellationToken).ConfigureAwait(false);
                await EnsureArincRxAsync(cancellationToken).ConfigureAwait(false);

                var ok4 = await MeasureGroupAsync("4mA", Set4mA, cancellationToken).ConfigureAwait(false);
                await Task.Delay(80, cancellationToken).ConfigureAwait(false);
                var ok20 = await MeasureGroupAsync("20mA", Set20mA, cancellationToken).ConfigureAwait(false);
                await Task.Delay(80, cancellationToken).ConfigureAwait(false);
                var ok10 = await MeasureGroupAsync("10mA", Set10mA, cancellationToken).ConfigureAwait(false);

                _measured14 = ok4 && ok20 && ok10;
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
        /// 测量 1-4mA 档位（手动模式）
        /// 根据当前选中的 Tab 页，测量对应档位的所有通道差压数据。
        /// </summary>
        private async Task OnMeasure14Async()
        {
            var token = _manualCts?.Token ?? CancellationToken.None;

            bool ok;
            switch (SelectedTabIndex)
            {
                case 0:
                    ok = await MeasureGroupAsync("4mA", Set4mA, token).ConfigureAwait(false);
                    break;
                case 1:
                    ok = await MeasureGroupAsync("20mA", Set20mA, token).ConfigureAwait(false);
                    break;
                case 2:
                    ok = await MeasureGroupAsync("10mA", Set10mA, token).ConfigureAwait(false);
                    break;
                default:
                    ok = await MeasureGroupAsync("当前档位", Set4mA, token).ConfigureAwait(false);
                    break;
            }

            if (!IsManualTestRunning || _manualAborted)
                return;

            if (ok)
            {
                _measured14 = true;
                RaisePropertyChanged(nameof(CanMeasure14));
                Measure14Command?.RaiseCanExecuteChanged();
            }

            await TryFinalizeAsync().ConfigureAwait(false);
        }

        private void Set4mA(string name, string text)
        {
            switch (name)
            {
                case "EDP2A": DptEdp2A4mAText = text; break;
                case "EMP2B": DptEmp2B4mAText = text; break;
                case "EMP3B": DptEmp3B4mAText = text; break;
                case "SYS1": DptSys14mAText = text; break;
                case "SYS2": DptSys24mAText = text; break;
                case "SYS3": DptSys34mAText = text; break;
            }
        }

        private void Set20mA(string name, string text)
        {
            switch (name)
            {
                case "EDP2A": DptEdp2A20mAText = text; break;
                case "EMP2B": DptEmp2B20mAText = text; break;
                case "EMP3B": DptEmp3B20mAText = text; break;
                case "SYS1": DptSys120mAText = text; break;
                case "SYS2": DptSys220mAText = text; break;
                case "SYS3": DptSys320mAText = text; break;
            }
        }

        private void Set10mA(string name, string text)
        {
            switch (name)
            {
                case "EDP2A": DptEdp2A10mAText = text; break;
                case "EMP2B": DptEmp2B10mAText = text; break;
                case "EMP3B": DptEmp3B10mAText = text; break;
                case "SYS1": DptSys110mAText = text; break;
                case "SYS2": DptSys210mAText = text; break;
                case "SYS3": DptSys310mAText = text; break;
            }
        }

        /// <summary>
        /// 测量一组电流档位的所有通道差压数据（核心测量方法）
        /// 流程：
        /// 1) 从 ARINC429 接收差压数据，根据 Label 和 SDI 过滤出 6 个通道。
        /// 2) 每个通道采集 5 帧有效数据取平均值。
        /// 3) 所有通道都采集完成后返回成功。
        /// </summary>
        /// <param name="title">档位名称（4mA/10mA/20mA，用于日志）</param>
        /// <param name="setTextByName">设置界面显示文本的回调函数</param>
        /// <returns>true=所有通道都成功采集，false=超时/异常</returns>
        private async Task<bool> MeasureGroupAsync(string title, Action<string, string> setTextByName, CancellationToken cancellationToken)
        {
            if (!(IsAutoTestRunning || (IsManualTestRunning && CanMeasure)))
            {
                Log($"{title}: 当前未处于测试状态");
                return false;
            }

            await _measureLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                Log($"{title}: 开始接收DPT数据 Label/SDI过滤，SSM不限制");

                var samples = new Dictionary<string, List<double>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["SYS1"] = new List<double>(SamplesPerMeasure),
                    ["SYS2"] = new List<double>(SamplesPerMeasure),
                    ["SYS3"] = new List<double>(SamplesPerMeasure),
                    ["EDP2A"] = new List<double>(SamplesPerMeasure),
                    ["EMP2B"] = new List<double>(SamplesPerMeasure),
                    ["EMP3B"] = new List<double>(SamplesPerMeasure),
                };

                var deadline = DateTime.UtcNow.AddMilliseconds(SampleTimeoutMs);
                while (!cancellationToken.IsCancellationRequested && DateTime.UtcNow <= deadline)
                {
                    var words = await _arinc.ReadRxWordsAsync(RxChannelIndex, maxCount: 512, enableTimeTag: false, enableRateAdaption: false, cancellationToken: cancellationToken)
                        .ConfigureAwait(false);

                    foreach (var w in words)
                    {
                        if (!_arinc.VerifyOddParity(w.Data429))
                            continue;

                        _arinc.ParseRawWord(w.Data429, out var label, out var wordSdi, out var data19, out _);

                        var channel = ResolveChannel(label, wordSdi);
                        if (channel == null)
                            continue;

                        var v = DecodeValue(data19);
                        if (!v.HasValue)
                            continue;

                        var list = samples[channel];
                        if (list.Count >= SamplesPerMeasure)
                            continue;

                        list.Add(v.Value);
                        var avg = list.Average();
                        setTextByName(channel, $"{v.Value:0.###} {PressureUnit} ({list.Count}/{SamplesPerMeasure}) 平均:{avg:0.###} {PressureUnit}");
                    }

                    if (samples.Values.All(l => l.Count >= SamplesPerMeasure))
                    {
                        foreach (var kv in samples)
                        {
                            var avg = kv.Value.Average();
                            setTextByName(kv.Key, $"{avg:0.###} {PressureUnit}");
                        }

                        Log($"{title}: 完成");
                        return true;
                    }

                    await Task.Delay(10, cancellationToken).ConfigureAwait(false);
                }

                foreach (var key in samples.Keys)
                {
                    setTextByName(key, "--");
                }

                if (IsManualTestRunning)
                {
                    await AbortManualTestAsync($"{title}: 接收超时，未获取到{SamplesPerMeasure}帧有效DPT数据").ConfigureAwait(false);
                }
                else
                {
                    Log($"{title}: 接收超时，未获取到{SamplesPerMeasure}帧有效DPT数据");
                }

                return false;
            }
            finally
            {
                _measureLock.Release();
            }
        }

        /// <summary>
        /// 根据 ARINC429 的 Label 和 SDI 解析出通道名称
        /// </summary>
        /// <returns>通道名称（SYS1/SYS2/SYS3/EDP2A/EMP2B/EMP3B），如果不匹配则返回 null</returns>
        private string ResolveChannel(byte label, byte sdi)
        {
            if (IsExpectedLabel(label, LabelDptSysDec))
            {
                if (sdi == SdiSys1) return "SYS1";
                if (sdi == SdiSys2) return "SYS2";
                if (sdi == SdiSys3) return "SYS3";
                return null;
            }

            if (IsExpectedLabel(label, LabelDptEdp2ADec) && sdi == SdiEdp2A)
                return "EDP2A";

            if (IsExpectedLabel(label, LabelDptEmpDec))
            {
                if (sdi == SdiEmp2B) return "EMP2B";
                if (sdi == SdiEmp3B) return "EMP3B";
            }

            return null;
        }

        /// <summary>
        /// 判断当前 ARINC429 Label 是否匹配期望值（兼容字节序反转）
        /// </summary>
        private bool IsExpectedLabel(byte label, byte expected)
        {
            return label == expected || label == _arinc.ReverseLabel(expected);
        }

        /// <summary>
        /// 解码差压值（UBNR：无符号二进制数，9-bit，范围 0-511）
        /// </summary>
        private double? DecodeValue(uint data19)
        {
            var v = _arinc.DecodeUbnr(data19, bitLength: DataBitLength, resolution: DataResolution, msbPosition: DataMsbPosition);
            if (v < 0 || v > 511)
                return null;
            return v;
        }

        /// <summary>
        /// 当所有档位都已测量完成时，更新"上次/本次"测试结论并结束测试
        /// </summary>
        private async Task TryFinalizeAsync()
        {
            var pass = _measured14;

            var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            var resultText = pass ? "合格" : "不合格";
            CurrentTestResult = resultText;
            PreviousTestTime = now;
            PreviousTestResult = resultText;
            LastTestTime = now;
            LastTestResult = resultText;

            SaveTestResultToProject();
            Log($"最终结果: {resultText}");

            if (IsManualTestRunning)
            {
                await StopManualTestAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// 中止手动测试（通常用于初始化失败、采集超时、采集异常等）
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
        /// 停止手动测试并释放硬件资源（停止 ARINC429 接收、关闭电源输出）
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

            Log("手动测试停止/结束，正在停止429接收...");
            await CleanupArincAsync().ConfigureAwait(false);

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

            Log("自动测试停止/结束，正在停止429接收...");
            await CleanupArincAsync().ConfigureAwait(false);

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

        /// <summary>
        /// 确保程控电源已连接并输出 28V（CH1/CH2）
        /// </summary>
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
                    try { await _arinc.StopRxAsync(RxChannelIndex, CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _arinc.CloseRxAsync(RxChannelIndex, CancellationToken.None).ConfigureAwait(false); } catch { }
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
            DptEdp2A4mAText = "--";
            DptEmp2B4mAText = "--";
            DptEmp3B4mAText = "--";
            DptSys14mAText = "--";
            DptSys24mAText = "--";
            DptSys34mAText = "--";

            DptEdp2A20mAText = "--";
            DptEmp2B20mAText = "--";
            DptEmp3B20mAText = "--";
            DptSys120mAText = "--";
            DptSys220mAText = "--";
            DptSys320mAText = "--";

            DptEdp2A10mAText = "--";
            DptEmp2B10mAText = "--";
            DptEmp3B10mAText = "--";
            DptSys110mAText = "--";
            DptSys210mAText = "--";
            DptSys310mAText = "--";
        }

        private async Task EnsureArincRxAsync(CancellationToken cancellationToken)
        {
            if (_arinc == null)
            {
                var device = FindFirstArincDevice();
                if (device == null)
                    throw new InvalidOperationException("未找到ART4227/ART4229(ARINC429)板卡，无法接收429数据");
                _arinc = new Art4229Api(device, deviceIndex: 0);
            }

            if (!_arinc.IsConnected)
            {
                await _arinc.ConnectAsync(cancellationToken).ConfigureAwait(false);
            }

            await _arinc.OpenRxAsync(RxChannelIndex, cancellationToken).ConfigureAwait(false);
            await _arinc.ConfigureRxAsync(
                RxChannelIndex,
                rate: ArincRate,
                parity: Art4229Parity.Odd,
                wordFormat: Art4229WordFormat.Standard429,
                enableInterrupt: false,
                interruptDepth: 512,
                enableTimeTag: false,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            await _arinc.StartRxAsync(RxChannelIndex, cancellationToken).ConfigureAwait(false);
            _ = await _arinc.ReadRxWordsAsync(RxChannelIndex, maxCount: 4096, enableTimeTag: false, enableRateAdaption: false, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

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

        private void Log(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.Invoke(() => Logs.Add(line));
                return;
            }

            Logs.Add(line);
        }
    }
}
