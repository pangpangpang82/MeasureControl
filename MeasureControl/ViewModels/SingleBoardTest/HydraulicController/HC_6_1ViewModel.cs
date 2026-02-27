using Prism.Commands;
using Prism.Mvvm;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using MeasureControl.Models.Devices;
using MeasureControl.Services;
using MeasureControl.Services.HardwareApis;

namespace MeasureControl.ViewModels.SingleBoardTest.HydraulicController
{
    /// <summary>
    /// HC_6_1 测试项：RS-485 通信线路绝缘电阻测试
    /// 测试目的：验证 RS-485 通信线路的绝缘性能
    /// 测试方法：使用万用表测量 485 线路对地的绝缘电阻，要求 ≥500Ω
    /// </summary>
    public class HC_6_1ViewModel : BindableBase
    {
        // 硬件设备 IP 地址
        private const string DmmIpAddress = "192.168.1.13";        // 万用表 IP
        private const string MatrixIpAddress = "192.168.1.3";      // 矩阵开关 IP

        // 矩阵开关槽位配置（用于信号路由）
        private const int MatrixSlotResistanceCh1 = 6;   // 485 线路 1-4 通道
        private const int MatrixSlotResistanceCh2 = 6;   // 485 线路 18-2 通道
        private const int MatrixSlotCommon = 4;          // 公共端（地）

        // 485 继电器控制引脚（PXIe-7131 的 DO29）
        private const int Relay485DoIndex = 29;

        // 测试通过阈值：绝缘电阻 ≥ 500Ω 为合格
        private const double PassThresholdOhm = 500.0;

        private readonly SemaphoreSlim _measureLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _relayLock = new SemaphoreSlim(1, 1);
        private CancellationTokenSource _manualCts;
        private CancellationTokenSource _autoCts;

        private IDmmApi _dmm;

        private readonly IPxiChassisService _pxiChassisService;
        private IJy7131Api _jy7131;
        private bool _isRelay485On;

        private bool _isManualTestRunning;
        private bool _isAutoTestRunning;

        private bool _canMeasure;

        private bool _measured14;
        private bool _measured182;
        private bool _manualAborted;

        private string _resistance14Text = "--";
        private string _resistance182Text = "--";

        private double? _resistance14;
        private double? _resistance182;

        private string _lastTestTime = "--";
        private string _lastTestResult = "--";
        private string _previousTestTime = "--";
        private string _previousTestResult = "--";

        public HC_6_1ViewModel(IPxiChassisService pxiChassisService)
        {
            _pxiChassisService = pxiChassisService;
            ManualTestCommand = new DelegateCommand(async () => await OnManualTestAsync());
            AutoTestCommand = new DelegateCommand(async () => await OnAutoTestAsync());
            Measure14Command = new DelegateCommand(async () => await OnMeasure14Async(), () => CanMeasure14);
            Measure182Command = new DelegateCommand(async () => await OnMeasure182Async(), () => CanMeasure182);
            ClearLogCommand = new DelegateCommand(() => Logs.Clear());
        }

        /// <summary>
        /// 确保 485 继电器处于指定状态
        /// 需要两步操作：1) 开启 PXIe-7131 的 DO29  2) 开启外部 485 继电器板的 K8（第8路）
        /// </summary>
        /// <param name="on">true=开启，false=关闭</param>
        private async Task EnsureRelay485Async(bool on, CancellationToken cancellationToken)
        {
            await _relayLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (on)
                {
                    // 如果已经开启，直接返回
                    if (_isRelay485On)
                    {
                        return;
                    }

                    // 查找 PXIe-7131 板卡
                    var device = FindFirstJy7131Device();
                    if (device == null)
                    {
                        throw new InvalidOperationException("未找到PXIe-7131(JY7131)板卡，无法开启485继电器");
                    }

                    // 创建 7131 API 实例（如果尚未创建）
                    if (_jy7131 == null)
                    {
                        var slot = device is DigitalIODevice dio ? dio.SlotIndex : 0;
                        _jy7131 = new Jy7131Api(device, slot);
                    }

                    // 确保板卡已连接
                    if (!_jy7131.IsConnected)
                    {
                        await _jy7131.ConnectAsync(cancellationToken).ConfigureAwait(false);
                    }

                    // 确保板卡已启动
                    if (!_jy7131.IsRunning)
                    {
                        await _jy7131.StartAsync(cancellationToken).ConfigureAwait(false);
                    }

                    // 先开启 7131 的 DO29
                    await _jy7131.WriteDoAsync($"DO{Relay485DoIndex}", true, cancellationToken).ConfigureAwait(false);
                    Log($"7131 DO{Relay485DoIndex} 已置位");

                    // 再开启继电器板 K8（第8路，index=7）
                    await _jy7131.SetRelayAsync(7, true, cancellationToken).ConfigureAwait(false);
                    Log($"485继电器板 K8（第8路）已开启");

                    // 等待继电器吸合稳定
                    await Task.Delay(100, cancellationToken).ConfigureAwait(false);

                    _isRelay485On = true;
                    Log($"485继电器K8开启完成: DO{Relay485DoIndex}=1, 继电器K8=ON");
                }
                else
                {
                    if (!_isRelay485On)
                    {
                        return;
                    }

                    if (_jy7131 != null)
                    {
                        // 先关闭继电器板 K8
                        try
                        {
                            await _jy7131.SetRelayAsync(7, false, cancellationToken).ConfigureAwait(false);
                            Log($"485继电器板 K8（第8路）已关闭");
                        }
                        catch (Exception ex)
                        {
                            Log($"关闭继电器 K8 失败: {ex.Message}");
                        }

                        // 再关闭 DO29
                        await _jy7131.WriteDoAsync($"DO{Relay485DoIndex}", false, cancellationToken).ConfigureAwait(false);
                        Log($"7131 DO{Relay485DoIndex} 已复位");
                    }

                    _isRelay485On = false;
                    Log($"485继电器K8关闭完成: DO{Relay485DoIndex}=0, 继电器K8=OFF");
                }
            }
            finally
            {
                _relayLock.Release();
            }
        }

        /// <summary>
        /// 从 PXI 机箱中查找第一个 PXIe-7131 板卡
        /// </summary>
        private DeviceBase FindFirstJy7131Device()
        {
            var chassisList = _pxiChassisService?.GetAllChassis();
            if (chassisList == null)
            {
                return null;
            }

            foreach (var chassis in chassisList)
            {
                var device = chassis?.Devices?.FirstOrDefault(d =>
                    d is DigitalIODevice ||
                    (d?.Model?.IndexOf("7131", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.DeviceTypeName?.IndexOf("离散量", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.DeviceTypeName?.IndexOf("数字量", StringComparison.OrdinalIgnoreCase) >= 0));

                if (device != null)
                {
                    return device;
                }
            }

            return null;
        }

        public DelegateCommand ManualTestCommand { get; }
        public DelegateCommand AutoTestCommand { get; }

        public DelegateCommand Measure14Command { get; }

        public DelegateCommand Measure182Command { get; }

        public DelegateCommand ClearLogCommand { get; }

        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();

        public bool IsManualTestRunning
        {
            get => _isManualTestRunning;
            set
            {
                if (SetProperty(ref _isManualTestRunning, value))
                {
                    Measure14Command?.RaiseCanExecuteChanged();
                    Measure182Command?.RaiseCanExecuteChanged();
                    RaisePropertyChanged(nameof(CanMeasure14));
                    RaisePropertyChanged(nameof(CanMeasure182));
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
                    Measure14Command?.RaiseCanExecuteChanged();
                    Measure182Command?.RaiseCanExecuteChanged();
                    RaisePropertyChanged(nameof(CanMeasure14));
                    RaisePropertyChanged(nameof(CanMeasure182));
                }
            }
        }

        public bool CanMeasure14 => IsManualTestRunning && CanMeasure && !_measured14;
        public bool CanMeasure182 => IsManualTestRunning && CanMeasure && !_measured182;

        /// <summary>
        /// 自动测试流程
        /// 自动依次测量两个通道的电阻并判断结果
        /// </summary>
        private async Task OnAutoTestAsync()
        {
            if (IsAutoTestRunning)
            {
                await StopAutoTestAsync().ConfigureAwait(false);
                return;
            }

            // stop manual mode if it was running
            if (IsManualTestRunning)
            {
                await StopManualTestAsync().ConfigureAwait(false);
            }

            IsAutoTestRunning = true;
            CanMeasure = false;

            _resistance14 = null;
            _resistance182 = null;
            Resistance14Text = "--";
            Resistance182Text = "--";

            _autoCts?.Cancel();
            _autoCts?.Dispose();
            _autoCts = new CancellationTokenSource();

            Log("开始自动测试");
            Log($"判据: R14>{PassThresholdOhm:0}Ω && R182>{PassThresholdOhm:0}Ω");
            Log($"连接万用表 {DmmIpAddress} ...");

            try
            {
                _dmm ??= new DmmSocketApi();
                await _dmm.ConnectAsync(DmmIpAddress, _autoCts.Token).ConfigureAwait(false);
                Log("万用表连接成功");

                // 自动完成两路测量（测完矩阵立刻断开，逻辑已在 MeasureResistanceAsync 的 finally 中处理）
                await MeasureResistanceAsync(
                        name: "通道1(1-4)",
                        connect1: ("I1", "O8", MatrixSlotResistanceCh1),
                        connect2: ("I4", "O2", MatrixSlotCommon),
                        afterSetText: (v, text) => { _resistance14 = v; Resistance14Text = text; },
                        cancellationToken: _autoCts.Token)
                    .ConfigureAwait(false);

                // 给继电器/采样一点间隔，避免读数抖动
                await Task.Delay(120, _autoCts.Token).ConfigureAwait(false);

                await MeasureResistanceAsync(
                        name: "通道2(1-82)",
                        connect1: ("I1", "O9", MatrixSlotResistanceCh2),
                        connect2: ("I4", "O2", MatrixSlotCommon),
                        afterSetText: (v, text) => { _resistance182 = v; Resistance182Text = text; },
                        cancellationToken: _autoCts.Token)
                    .ConfigureAwait(false);

                await FinalizeIfBothMeasuredAsync(stopAfterFinalize: true, isAutoMode: true).ConfigureAwait(false);
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
        }

        public string Resistance14Text
        {
            get => _resistance14Text;
            private set => SetProperty(ref _resistance14Text, value);
        }

        public string Resistance182Text
        {
            get => _resistance182Text;
            private set => SetProperty(ref _resistance182Text, value);
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

        /// <summary>
        /// 手动测试流程
        /// 用户可以手动点击按钮分别测量两个通道的电阻
        /// </summary>
        private async Task OnManualTestAsync()
        {
            if (IsManualTestRunning)
            {
                await StopManualTestAsync().ConfigureAwait(false);
                return;
            }

            IsAutoTestRunning = false;
            IsManualTestRunning = true;
            CanMeasure = false;

            _manualAborted = false;
            _measured14 = false;
            _measured182 = false;
            RaisePropertyChanged(nameof(CanMeasure14));
            RaisePropertyChanged(nameof(CanMeasure182));

            _resistance14 = null;
            _resistance182 = null;
            Resistance14Text = "--";
            Resistance182Text = "--";

            _manualCts?.Cancel();
            _manualCts?.Dispose();
            _manualCts = new CancellationTokenSource();

            Log("开始手动测试");
            Log($"连接万用表 {DmmIpAddress} ...");

            try
            {
                _dmm ??= new DmmSocketApi();
                await _dmm.ConnectAsync(DmmIpAddress, _manualCts.Token).ConfigureAwait(false);
                Log("万用表连接成功");

                await EnsureRelay485Async(on: true, cancellationToken: _manualCts.Token).ConfigureAwait(false);

                CanMeasure = true;
            }
            catch (Exception ex)
            {
                await AbortManualTestAsync($"万用表连接失败，手动测试中止: {ex.Message}").ConfigureAwait(false);
            }
        }

        private async Task AbortManualTestAsync(string reason)
        {
            _manualAborted = true;
            if (!string.IsNullOrWhiteSpace(reason))
            {
                Log(reason);
            }

            await StopManualTestAsync().ConfigureAwait(false);
        }

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

            Log("手动测试停止/结束，正在断开矩阵与万用表...");

            try
            {
                await EnsureRelay485Async(on: false, cancellationToken: CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
            }

            try
            {
                await DisconnectAllMatrixRoutesAsync().ConfigureAwait(false);
            }
            catch
            {
            }

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
                if (_jy7131 != null)
                {
                    try { await _jy7131.StopAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _jy7131.DisconnectAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _jy7131.DisposeAsync().ConfigureAwait(false); } catch { }
                }
            }
            catch
            {
            }
            finally
            {
                _jy7131 = null;
                _isRelay485On = false;
            }

            IsManualTestRunning = false;
            Log("手动测试已结束");
        }

        private async Task StopAutoTestAsync()
        {
            try
            {
                _autoCts?.Cancel();
            }
            catch
            {
            }

            Log("自动测试停止/结束，正在断开矩阵与万用表...");

            try
            {
                await DisconnectAllMatrixRoutesAsync().ConfigureAwait(false);
            }
            catch
            {
            }

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

            IsAutoTestRunning = false;
            Log("自动测试已结束");
        }

        /// <summary>
        /// 测量 1-4 通道的绝缘电阻（手动模式）
        /// </summary>
        private async Task OnMeasure14Async()
        {
            var ok = await MeasureResistanceAsync(
                name: "通道1(1-4)",
                connect1: ("I1", "O8", MatrixSlotResistanceCh1),
                connect2: ("I4", "O2", MatrixSlotCommon),
                afterSetText: (v, text) => { _resistance14 = v; Resistance14Text = text; },
                cancellationToken: _manualCts?.Token ?? CancellationToken.None).ConfigureAwait(false);

            if (!IsManualTestRunning || _manualAborted)
            {
                return;
            }

            if (ok)
            {
                _measured14 = true;
                RaisePropertyChanged(nameof(CanMeasure14));
                Measure14Command?.RaiseCanExecuteChanged();
            }

            await TryFinalizeIfBothMeasuredAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 测量 18-2 通道的绝缘电阻（手动模式）
        /// </summary>
        private async Task OnMeasure182Async()
        {
            var ok = await MeasureResistanceAsync(
                name: "通道2(1-82)",
                connect1: ("I1", "O9", MatrixSlotResistanceCh2),
                connect2: ("I4", "O2", MatrixSlotCommon),
                afterSetText: (v, text) => { _resistance182 = v; Resistance182Text = text; },
                cancellationToken: _manualCts?.Token ?? CancellationToken.None).ConfigureAwait(false);

            if (!IsManualTestRunning || _manualAborted)
            {
                return;
            }

            if (ok)
            {
                _measured182 = true;
                RaisePropertyChanged(nameof(CanMeasure182));
                Measure182Command?.RaiseCanExecuteChanged();
            }

            await TryFinalizeIfBothMeasuredAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 测量指定通道的绝缘电阻（核心测量方法）
        /// 流程：1) 配置矩阵开关连接信号路径  2) 使用万用表测量电阻  3) 判断结果
        /// </summary>
        /// <param name="name">通道名称（用于日志）</param>
        /// <param name="connect1">矩阵连接1（信号端）</param>
        /// <param name="connect2">矩阵连接2（地端）</param>
        /// <param name="afterSetText">测量完成后的回调（更新界面显示）</param>
        /// <returns>true=测量成功，false=测量失败</returns>
        private async Task<bool> MeasureResistanceAsync(
            string name,
            (string In, string Out, int Slot) connect1,
            (string In, string Out, int Slot) connect2,
            Action<double?, string> afterSetText,
            CancellationToken cancellationToken)
        {
            if (!(IsAutoTestRunning || (IsManualTestRunning && CanMeasure)))
            {
                Log($"{name}: 当前未处于测试状态");
                return false;
            }

            await _measureLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                Log($"{name}: 开始测量");

                // 配置矩阵开关，连接测量信号路径
                var matrix = MatrixControlService.Instance;

                var ok1 = await matrix.ConnectNodesAsync(connect1.In, connect1.Out, connect1.Slot, MatrixIpAddress).ConfigureAwait(false);
                var ok2 = await matrix.ConnectNodesAsync(connect2.In, connect2.Out, connect2.Slot, MatrixIpAddress).ConfigureAwait(false);
                Log($"{name}: 矩阵连接 {(ok1 && ok2 ? "OK" : "FAIL")} - {connect1.In}-{connect1.Out}(slot{connect1.Slot}), {connect2.In}-{connect2.Out}(slot{connect2.Slot})");
                if (!ok1 || !ok2)
                {
                    afterSetText(null, "--");
                    if (IsManualTestRunning)
                    {
                        await AbortManualTestAsync($"{name}: 矩阵连接失败，手动测试中止").ConfigureAwait(false);
                    }

                    return false;
                }

                // 使用万用表测量电阻（超时时间 8 秒）
                DmmReading reading = null;
                try
                {
                    reading = await _dmm.ReadOnceAsync(DmmMeasureMode.RES, new DmmReadOptions { TimeoutMilliseconds = 8000 }, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Log($"{name}: 电阻采集异常: {ex.Message}");
                    if (IsManualTestRunning)
                    {
                        await AbortManualTestAsync($"{name}: 电阻采集异常，手动测试中止").ConfigureAwait(false);
                    }

                    return false;
                }

                if (reading == null)
                {
                    afterSetText(null, "--");
                    if (IsManualTestRunning)
                    {
                        await AbortManualTestAsync($"{name}: 电阻读数为空，手动测试中止").ConfigureAwait(false);
                    }

                    return false;
                }

                // 检查是否超量程（OL = Over Load）
                if (reading.IsOverrange)
                {
                    afterSetText(null, "OL");
                    Log($"{name}: 读数为OL(过量程)，视为无效，本次手动测试中止");
                    if (IsManualTestRunning)
                    {
                        await AbortManualTestAsync($"{name}: 读数OL(过量程)，手动测试中止").ConfigureAwait(false);
                    }

                    return false;
                }

                if (reading.Value == null)
                {
                    afterSetText(null, "--");
                    if (IsManualTestRunning)
                    {
                        await AbortManualTestAsync($"{name}: 电阻读数无效，手动测试中止").ConfigureAwait(false);
                    }

                    return false;
                }

                // 提取并格式化测量结果
                var value = reading.Value;
                var text = FormatOhmText(reading);
                afterSetText(value, text);

                Log($"{name}: 读数 Raw={reading?.Raw ?? ""} Value={(value?.ToString("0.###", CultureInfo.InvariantCulture) ?? "--")} Unit={reading?.Unit ?? ""}");
                Log($"{name}: 阻抗={text}");

                return true;
            }
            finally
            {
                try
                {
                    await DisconnectAllMatrixRoutesAsync().ConfigureAwait(false);
                }
                catch
                {
                }

                _measureLock.Release();
            }
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

        private async Task DisconnectAllMatrixRoutesAsync()
        {
            var matrix = MatrixControlService.Instance;

            // disconnect all nodes that might be used by this test
            _ = await matrix.DisconnectNodesAsync("I1", "O8", MatrixSlotResistanceCh1, MatrixIpAddress).ConfigureAwait(false);
            _ = await matrix.DisconnectNodesAsync("I1", "O9", MatrixSlotResistanceCh2, MatrixIpAddress).ConfigureAwait(false);
            _ = await matrix.DisconnectNodesAsync("I4", "O2", MatrixSlotCommon, MatrixIpAddress).ConfigureAwait(false);
        }

        private async Task TryFinalizeIfBothMeasuredAsync()
        {
            await FinalizeIfBothMeasuredAsync(stopAfterFinalize: true, isAutoMode: false).ConfigureAwait(false);
        }

        private async Task FinalizeIfBothMeasuredAsync(bool stopAfterFinalize, bool isAutoMode)
        {
            if (!isAutoMode && _manualAborted)
            {
                return;
            }

            if (_resistance14 == null || _resistance182 == null)
            {
                return;
            }

            var pass14 = _resistance14 > PassThresholdOhm;
            var pass182 = _resistance182 > PassThresholdOhm;
            var pass = pass14 && pass182;

            Log($"判据: R14>{PassThresholdOhm:0}Ω && R182>{PassThresholdOhm:0}Ω");
            Log($"R14={Resistance14Text} => {(pass14 ? "OK" : "NG")}");
            Log($"R182={Resistance182Text} => {(pass182 ? "OK" : "NG")}");

            PreviousTestTime = LastTestTime;
            PreviousTestResult = LastTestResult;
            LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            LastTestResult = pass ? "合格" : "不合格";
            Log($"最终结果: {LastTestResult}");

            if (!stopAfterFinalize)
            {
                return;
            }

            if (isAutoMode)
            {
                await StopAutoTestAsync().ConfigureAwait(false);
            }
            else
            {
                await StopManualTestAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// 记录日志到界面
        /// </summary>
        private void Log(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            var text = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
            Logs.Add(text);
        }
    }
}
