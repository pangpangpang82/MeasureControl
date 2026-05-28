using MeasureControl.Events;
using MeasureControl.Models.Devices;
using MeasureControl.Services;
using MeasureControl.Services.HardwareApis;
using MeasureControl.Simulations.S_C_8_3_1;
using Prism.Commands;
using Prism.Events;
using Prism.Ioc;
using Prism.Mvvm;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MeasureControl.ViewModels.SingleBoardTest.AirController
{
    public class S_C_6_2ViewModel : BindableBase
    {
        private const string TxChannel = "429_CH5";
        private const string RxChannel = "429_CH2";

        private const string PowerSupplyIpAddress = "192.168.1.15";

        private const double VoltageMin = 2.375;
        private const double VoltageMax = 2.625;
        private const double Current32VMax = 2.18;
        private const double Current28VMax = 2.5;

        private static readonly byte[] AtpR = { 0x30, 0x01, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] Ab28vSupply = { 0x01, 0x01, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00 };
        // 自动返回的回采值前缀（前4字节用于匹配）
        private static readonly byte[] Vbit15Prefix4 = { 0x01, 0x01, 0x01, 0x02 };  // 15V_VBIT响应前缀
        private static readonly byte[] Vbit5Prefix4 = { 0x01, 0x01, 0x01, 0x03 };   // 5V_VBIT响应前缀

        private readonly S_C_8_3_1Simulation _arinc = new S_C_8_3_1Simulation();
        private readonly SemaphoreSlim _testLock = new SemaphoreSlim(1, 1);

        private CancellationTokenSource _testCts;
        private IPowerSupplyApi _powerSupply;
        private IJy7131Api _jy7131;

        private readonly IPxiChassisService _pxiChassisService;

        private bool _isAutoTestRunning;
        private string _lastTestTime = "--";
        private string _lastTestResult = "--";

        private string _voltage15V32Text = "--";
        private string _voltage5V32Text = "--";
        private string _voltage15V28Text = "--";
        private string _voltage5V28Text = "--";
        private string _powerSupplyMeasuredCurrent32Text = "--";
        private string _powerSupplyMeasuredCurrent28Text = "--";

        private double? _voltage15V_32;
        private double? _voltage5V_32;
        private double? _current32;
        private double? _voltage15V_28;
        private double? _voltage5V_28;
        private double? _current28;

        private bool _isRelay485On;
        private bool _isDo18On;

        public S_C_6_2ViewModel()
        {
            _pxiChassisService = ContainerLocator.Container?.Resolve<IPxiChassisService>();

            AutoTestCommand = new DelegateCommand(async () => await OnAutoTestAsync());
            ClearLogCommand = new DelegateCommand(() => Logs.Clear());
        }

        public string Title => "6.2.1A控制通道供电测试";

        public DelegateCommand AutoTestCommand { get; }
        public DelegateCommand ClearLogCommand { get; }

        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();

        public bool IsAutoTestRunning
        {
            get => _isAutoTestRunning;
            set => SetProperty(ref _isAutoTestRunning, value);
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

        public string Voltage15V32Text
        {
            get => _voltage15V32Text;
            set => SetProperty(ref _voltage15V32Text, value);
        }

        public string Voltage5V32Text
        {
            get => _voltage5V32Text;
            set => SetProperty(ref _voltage5V32Text, value);
        }

        public string Voltage15V28Text
        {
            get => _voltage15V28Text;
            set => SetProperty(ref _voltage15V28Text, value);
        }

        public string Voltage5V28Text
        {
            get => _voltage5V28Text;
            set => SetProperty(ref _voltage5V28Text, value);
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

        private void AddLog(string message)
        {
            if (System.Windows.Application.Current?.Dispatcher?.CheckAccess() == false)
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() => Logs.Add(message));
            }
            else
            {
                Logs.Add(message);
            }
        }

        private async Task OnAutoTestAsync()
        {
            if (IsAutoTestRunning)
            {
                try { _testCts?.Cancel(); } catch { }
                AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试停止请求");
                return;
            }

            await _testLock.WaitAsync();
            try
            {
                IsAutoTestRunning = true;
                LastTestTime = "--";
                LastTestResult = "--";

                Voltage15V32Text = "--";
                Voltage5V32Text = "--";
                Voltage15V28Text = "--";
                Voltage5V28Text = "--";
                PowerSupplyMeasuredCurrent32Text = "--";
                PowerSupplyMeasuredCurrent28Text = "--";

                _voltage15V_32 = null;
                _voltage5V_32 = null;
                _current32 = null;
                _voltage15V_28 = null;
                _voltage5V_28 = null;
                _current28 = null;

                _testCts?.Dispose();
                _testCts = new CancellationTokenSource();
                var token = _testCts.Token;

                AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试启动");
                await RunAutoTestAsync(token);
            }
            finally
            {
                _testLock.Release();
            }
        }

        private async Task RunAutoTestAsync(CancellationToken token)
        {
            var failures = new ObservableCollection<string>();
            try
            {
                token.ThrowIfCancellationRequested();

                AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试：初始化设备");
                await EnsurePowerSupplyConnectedAsync(token);
                await EnsureJy7131ReadyAsync(token);

                _arinc.IsRealProduct = true;
                _arinc.ArincRate = 100000.0;
                await _arinc.StartAsync(TxChannel, RxChannel, msg => AddLog(msg));

                await RunSupplyVoltageScenarioAsync(32.0, Current32VMax, token, failures, true);

                token.ThrowIfCancellationRequested();

                await RunSupplyVoltageScenarioAsync(28.0, Current28VMax, token, failures, false);

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
                await CleanupHardwareAsync();
                IsAutoTestRunning = false;
            }
        }

        private async Task RunSupplyVoltageScenarioAsync(double supplyVoltage, double currentUpperLimit, CancellationToken token, ObservableCollection<string> failures, bool is32V)
        {
            var stepBase = is32V ? 1 : 6;  // 32V从步骤1开始，28V从步骤6开始

            token.ThrowIfCancellationRequested();

            // 步骤1/6：向控制通道供电
            AddLog($"[{DateTime.Now:HH:mm:ss}] 步骤{stepBase}：向控制通道供电 {supplyVoltage:0.###}V，进入ATP");
            await PowerSupplyApplyAsync(supplyVoltage, currentLimit: Math.Max(3.0, currentUpperLimit + 0.5), token);
            await EnsureDo18OnAsync(token);
            await Task.Delay(500, token);

            token.ThrowIfCancellationRequested();

            // 进入ATP模式（发送指令，不判断返回）
            await SendEnterAtpAsync(token);
            AddLog($"[{DateTime.Now:HH:mm:ss}] ATP指令已发送 0x30 01 01 01 00 00 00 00");
            await Task.Delay(300, token);

            token.ThrowIfCancellationRequested();

            // 步骤2/7：记录供电电源处电流值
            AddLog($"[{DateTime.Now:HH:mm:ss}] 步骤{stepBase + 1}：记录供电电源处电流值");
            var ms = await PowerSupplyReadMeasurementsAsync(token);
            double? measuredCurrent = ms?.Current?.Value;
            if (measuredCurrent.HasValue)
            {
                var currentText = $"{measuredCurrent.Value:0.000} A";
                AddLog($"[{DateTime.Now:HH:mm:ss}] 电流值：{currentText}");

                if (is32V)
                {
                    _current32 = measuredCurrent.Value;
                    PowerSupplyMeasuredCurrent32Text = currentText;
                }
                else
                {
                    _current28 = measuredCurrent.Value;
                    PowerSupplyMeasuredCurrent28Text = currentText;
                }

                if (measuredCurrent.Value > currentUpperLimit)
                {
                    failures.Add($"{supplyVoltage:0.###}V供电电流={measuredCurrent.Value:0.000}A > {currentUpperLimit:0.###}A");
                }
            }

            token.ThrowIfCancellationRequested();

            // 步骤3/8：发送测试指令AB_28V_SUPPLY（完全参考AirSimpleSequenceViewModel）
            AddLog($"[{DateTime.Now:HH:mm:ss}] 步骤{stepBase + 2}：发送测试指令AB_28V_SUPPLY 0x01 01 01 01 00 00 00 00");
            try { await _arinc.ClearRxFifoAsync(RxChannel); } catch { }
            await Task.Delay(20, token);
            await _arinc.SendBenchCommandOnlyAsync(TxChannel, Ab28vSupply, msg => AddLog(msg), token);

            token.ThrowIfCancellationRequested();

            // 步骤4/9：判读自动返回的两个回采值（完全参考AirSimpleSequenceViewModel.AutoSendAb28vSupplyAndReadTelemetryAsync）
            AddLog($"[{DateTime.Now:HH:mm:ss}] 步骤{stepBase + 3}：判读自动返回的回采值15V_VBIT和5V_VBIT");
            var (resp15, resp5) = await WaitVbitPairAsync(timeoutMs: 4000, token);

            if (resp15 == null || resp5 == null)
            {
                if (resp15 == null)
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 15V_VBIT 回采超时");
                if (resp5 == null)
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 5V_VBIT 回采超时");
                failures.Add($"{supplyVoltage:0.###}V 二次电源回采失败");
            }
            else
            {
                // 解析电压值
                if (!TryParseVbitValue(resp15, Vbit15Prefix4, out var v15))
                {
                    failures.Add($"15V_VBIT 解析失败: 0x{FormatBytesHex(resp15)}");
                }
                else if (!TryParseVbitValue(resp5, Vbit5Prefix4, out var v5))
                {
                    failures.Add($"5V_VBIT 解析失败: 0x{FormatBytesHex(resp5)}");
                }
                else
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 回采结果：15V_VBIT={v15:0.000}V, 5V_VBIT={v5:0.000}V");
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 原始数据：15V:0x{FormatBytesHex(resp15)}  5V:0x{FormatBytesHex(resp5)}");

                    if (is32V)
                    {
                        _voltage15V_32 = v15;
                        _voltage5V_32 = v5;
                        Voltage15V32Text = $"{v15:0.000} V";
                        Voltage5V32Text = $"{v5:0.000} V";
                    }
                    else
                    {
                        _voltage15V_28 = v15;
                        _voltage5V_28 = v5;
                        Voltage15V28Text = $"{v15:0.000} V";
                        Voltage5V28Text = $"{v5:0.000} V";
                    }

                    // 合格判据：电压回采值均在[2.375V, 2.625V]内
                    if (v15 < VoltageMin || v15 > VoltageMax)
                    {
                        failures.Add($"{supplyVoltage:0.###}V 15V_VBIT={v15:0.000}V 不在[{VoltageMin},{VoltageMax}]范围内");
                    }
                    if (v5 < VoltageMin || v5 > VoltageMax)
                    {
                        failures.Add($"{supplyVoltage:0.###}V 5V_VBIT={v5:0.000}V 不在[{VoltageMin},{VoltageMax}]范围内");
                    }
                }
            }

            // 步骤5：断开控制通道供电（仅32V测试后执行，28V测试后在finally中清理）
            AddLog($"[{DateTime.Now:HH:mm:ss}] 步骤{stepBase + 4}：断开控制通道供电");
            await EnsureDo18OffAsync(token);
            await PowerSupplyOutputOffAsync(token);
            await Task.Delay(300, token);
        }

        private async Task SendEnterAtpAsync(CancellationToken token)
        {
            try
            {
                await _arinc.ClearRxFifoAsync(RxChannel);
            }
            catch { }

            await _arinc.SendBenchCommandOnlyAsync(TxChannel, AtpR, msg => AddLog(msg), token);
            await Task.Delay(100, token);
        }

        /// <summary>
        /// 等待15V_VBIT和5V_VBIT两个回采值
        /// 使用WaitBenchResponsePairAsync一次性读取两组数据，避免第二组数据丢失
        /// label 011-014（八进制）经过位翻转后 = 0x90, 0x50, 0xD0, 0x30
        /// </summary>
        private async Task<(byte[] Resp15, byte[] Resp5)> WaitVbitPairAsync(int timeoutMs, CancellationToken token)
        {
            // 使用新方法一次性读取两组数据
            var (resp1, resp2) = await _arinc.WaitBenchResponsePairAsync(
                RxChannel,
                Is15vVbitPayload,
                Is5vVbitPayload,
                timeoutMs,
                msg => AddLog(msg),
                token);

            return (resp1, resp2);
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

        /// <summary>
        /// 解析VBIT电压值
        /// 数据格式: 01 01 01 02/03 00 00 XX XX
        /// 电压值在最后2字节(data[6], data[7])，单位mV，需除以1000得到V
        /// 例如: 09 BE = 2494 -> 2.494V
        /// </summary>
        private static bool TryParseVbitValue(byte[] frame, byte[] prefix4, out double value)
        {
            value = 0;
            if (!IsPrefix4(frame, prefix4))
                return false;

            if (frame == null || frame.Length < 8)
                return false;

            // 电压值在最后2字节，大端序
            int rawValue = (frame[6] << 8) | frame[7];
            value = rawValue / 1000.0;
            return true;
        }

        private static string FormatBytesHex(byte[] data)
        {
            if (data == null || data.Length == 0)
                return string.Empty;

            return string.Join(" ", data.Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));
        }

        private DeviceBase FindFirstJy7131Device()
        {
            var chassisList = _pxiChassisService?.GetAllChassis();
            if (chassisList == null)
                return null;

            foreach (var chassis in chassisList)
            {
                var device = chassis?.Devices?.FirstOrDefault(d =>
                    d is DigitalIODevice ||
                    (d?.Model?.IndexOf("7131", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.DeviceTypeName?.IndexOf("离散量", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.DeviceTypeName?.IndexOf("数字量", StringComparison.OrdinalIgnoreCase) >= 0));

                if (device != null)
                    return device;
            }

            return null;
        }

        private async Task EnsureJy7131ReadyAsync(CancellationToken token)
        {
            var device = FindFirstJy7131Device();
            if (device == null)
            {
                throw new InvalidOperationException("未找到PXIe-7131(JY7131)板卡");
            }

            if (_jy7131 == null)
            {
                var slot = device is DigitalIODevice dio ? dio.SlotIndex : 0;
                _jy7131 = new Jy7131Api(device, slot);
            }

            if (!_jy7131.IsConnected)
            {
                await _jy7131.ConnectAsync(token).ConfigureAwait(false);
            }

            if (!_jy7131.IsRunning)
            {
                await _jy7131.SetOutputModeAsync(Jy7131OutputMode.Sinking, token).ConfigureAwait(false);
                await _jy7131.StartAsync(token).ConfigureAwait(false);
            }
        }

        private async Task EnsureRelay485OnAsync(CancellationToken token)
        {
            if (_isRelay485On)
                return;

            // 打开485继电器板的DO5（索引4，对应第5路继电器），使能7131板卡的DO17-DO20
            // SetRelayAsync索引0-15对应继电器1-16，所以DO5用索引4
            await _jy7131.SetRelayAsync(4, true, token).ConfigureAwait(false);
            AddLog($"[{DateTime.Now:HH:mm:ss}] 485继电器板第5路已开启（使能DO17-DO20）");

            await Task.Delay(100, token);
            _isRelay485On = true;
        }

        private async Task EnsureDo18OnAsync(CancellationToken token)
        {
            if (_isDo18On)
                return;

            // 先确保485继电器板DO5已开启（使能DO17-DO20）
            await EnsureRelay485OnAsync(token);

            // 开启7131板卡DO18给控制板上电
            // API使用DO0-DO31（从0开始），界面显示DO1-DO32（从1开始）
            // 界面DO18 = API DO17
            await _jy7131.WriteDoAsync("DO17", true, token).ConfigureAwait(false);
            await Task.Delay(200, token);
            _isDo18On = true;
            AddLog($"[{DateTime.Now:HH:mm:ss}] 7131板卡DO18已开启，控制板上电");
        }

        private async Task EnsureDo18OffAsync(CancellationToken token)
        {
            if (!_isDo18On)
                return;

            try
            {
                // 界面DO18 = API DO17
                await _jy7131.WriteDoAsync("DO17", false, token).ConfigureAwait(false);
                _isDo18On = false;
                AddLog($"[{DateTime.Now:HH:mm:ss}] 7131板卡DO18已关闭，控制板下电");
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 关闭DO18失败: {ex.Message}");
            }
        }

        private async Task EnsureRelay485OffAsync(CancellationToken token)
        {
            if (!_isRelay485On)
                return;

            try
            {
                // 关闭485继电器板的DO5（索引4）
                await _jy7131.SetRelayAsync(4, false, token).ConfigureAwait(false);
                AddLog($"[{DateTime.Now:HH:mm:ss}] 485继电器板DO5已关闭");
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 关闭485继电器板DO5失败: {ex.Message}");
            }

            _isRelay485On = false;
        }

        private async Task EnsurePowerSupplyConnectedAsync(CancellationToken token)
        {
            _powerSupply ??= new PowerSupplySocketApi();
            if (!_powerSupply.IsConnected)
            {
                await _powerSupply.ConnectAsync(PowerSupplyIpAddress, token);
                AddLog($"[{DateTime.Now:HH:mm:ss}] 程控电源已连接");
            }
        }

        private async Task PowerSupplyApplyAsync(double voltage, double currentLimit, CancellationToken token)
        {
            if (_powerSupply == null || !_powerSupply.IsConnected)
            {
                await EnsurePowerSupplyConnectedAsync(token);
            }

            await _powerSupply.SetVoltageAsync(PowerSupplyChannel.CH1, voltage, token);
            await _powerSupply.SetCurrentAsync(PowerSupplyChannel.CH1, currentLimit, token);
            await _powerSupply.SetOutputEnabledAsync(PowerSupplyChannel.CH1, true, token);
            AddLog($"[{DateTime.Now:HH:mm:ss}] 程控电源输出 {voltage:0.###}V / {currentLimit:0.###}A");
        }

        private async Task<PowerSupplyMeasurements> PowerSupplyReadMeasurementsAsync(CancellationToken token)
        {
            if (_powerSupply == null || !_powerSupply.IsConnected)
                return null;

            return await _powerSupply.ReadMeasurementsAsync(PowerSupplyChannel.CH1, null, token);
        }

        private async Task PowerSupplyOutputOffAsync(CancellationToken token)
        {
            if (_powerSupply == null || !_powerSupply.IsConnected)
                return;

            try
            {
                await _powerSupply.SetOutputEnabledAsync(PowerSupplyChannel.CH1, false, token);
                AddLog($"[{DateTime.Now:HH:mm:ss}] 程控电源输出已关闭");
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 关闭程控电源输出失败: {ex.Message}");
            }
        }

        private async Task CleanupHardwareAsync()
        {
            try { await EnsureDo18OffAsync(CancellationToken.None); } catch { }
            try { await EnsureRelay485OffAsync(CancellationToken.None); } catch { }
            try { await PowerSupplyOutputOffAsync(CancellationToken.None); } catch { }

            try
            {
                if (_powerSupply?.IsConnected == true)
                {
                    await _powerSupply.DisconnectAsync(CancellationToken.None);
                }
            }
            catch { }

            try
            {
                await _arinc.ClearRxFifoAsync(RxChannel);
            }
            catch { }

            try
            {
                await _arinc.StopAsync(msg => AddLog(msg));
            }
            catch { }
        }
    }
}
