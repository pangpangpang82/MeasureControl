using MeasureControl.Models.Devices;
using MeasureControl.Services;
using MeasureControl.Services.HardwareApis;
using MeasureControl.Simulations.S_C_8_3_1;
using Prism.Commands;
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
    public class Pot5VSupplyTestViewModel : BindableBase
    {
        private const string TxChannel = "429_CH5";
        private const string RxChannel = "429_CH2";

        private const string DmmIpAddress = "192.168.1.13";
        private const string MatrixIpAddress = "192.168.1.3";

        // 矩阵开关配置：2601(2) 1/14 和 2601(1) 4/2
        private const int MatrixSlot1 = 4;  // 2601(1)
        private const int MatrixSlot2 = 6;  // 2601(2)
        private static readonly (string In, string Out, int Slot) MatrixDmmRoute1 = ("I4", "O2", MatrixSlot1);   // 2601(1) 4/2
        private static readonly (string In, string Out, int Slot) MatrixDmmRoute2 = ("I1", "O14", MatrixSlot2);  // 2601(2) 1/14

        // 合格判据
        private const double DmmVoltageMin = 4.5;
        private const double DmmVoltageMax = 5.5;
        private const double PotSupVbitMin = 2.25;
        private const double PotSupVbitMax = 2.75;

        // ATP和测试指令
        private static readonly byte[] AtpEnterCommand = { 0x30, 0x01, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] Ab5VPotSupplyCommand = { 0x01, 0x02, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00 };
        // 回采值前缀（前4字节用于匹配）
        private static readonly byte[] PotSupVbitPrefix4 = { 0x01, 0x02, 0x01, 0x02 };

        private readonly S_C_8_3_1Simulation _arinc = new S_C_8_3_1Simulation();
        private readonly SemaphoreSlim _testLock = new SemaphoreSlim(1, 1);

        private CancellationTokenSource _testCts;
        private IDmmApi _dmmSocket;

        private readonly IPxiChassisService _pxiChassisService;

        private bool _isAutoTestRunning;
        private string _lastTestTime = "--";
        private string _lastTestResult = "--";

        private string _dmmVoltageText = "--";
        private string _potSupVbitText = "--";

        private double? _dmmVoltage;
        private double? _potSupVbit;

        public Pot5VSupplyTestViewModel()
        {
            _pxiChassisService = ContainerLocator.Container?.Resolve<IPxiChassisService>();

            AutoTestCommand = new DelegateCommand(async () => await OnAutoTestAsync());
            ClearLogCommand = new DelegateCommand(() => Logs.Clear());
        }

        public string Title => "6.3 5V传感器供电电压测试";

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

        public string DmmVoltageText
        {
            get => _dmmVoltageText;
            set => SetProperty(ref _dmmVoltageText, value);
        }

        public string PotSupVbitText
        {
            get => _potSupVbitText;
            set => SetProperty(ref _potSupVbitText, value);
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

                DmmVoltageText = "--";
                PotSupVbitText = "--";

                _dmmVoltage = null;
                _potSupVbit = null;

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

                AddLog($"[{DateTime.Now:HH:mm:ss}] 初始化设备");

                // 初始化ARINC429
                _arinc.IsRealProduct = true;
                _arinc.ArincRate = 100000.0;
                await _arinc.StartAsync(TxChannel, RxChannel, msg => AddLog(msg));

                token.ThrowIfCancellationRequested();

                // 步骤1：进入ATP并发送测试指令
                AddLog($"[{DateTime.Now:HH:mm:ss}] 步骤1：进入ATP，发送测试指令AB_5VPOT_SUPPLY");
                await SendEnterAtpAsync(token);
                AddLog($"[{DateTime.Now:HH:mm:ss}] ATP指令已发送 0x{FormatBytesHex(AtpEnterCommand)}");
                await Task.Delay(300, token);

                try { await _arinc.ClearRxFifoAsync(RxChannel); } catch { }
                await Task.Delay(20, token);
                await _arinc.SendBenchCommandOnlyAsync(TxChannel, Ab5VPotSupplyCommand, msg => AddLog(msg), token);
                AddLog($"[{DateTime.Now:HH:mm:ss}] AB_5VPOT_SUPPLY指令已发送 0x{FormatBytesHex(Ab5VPotSupplyCommand)}");

                token.ThrowIfCancellationRequested();

                // 步骤2：测量5V传感器供电模块输出电压（万用表+矩阵开关）
                AddLog($"[{DateTime.Now:HH:mm:ss}] 步骤2：测量5V传感器供电模块输出电压");
                var dmmVoltage = await MeasureDmmVoltageAsync(token);
                if (dmmVoltage.HasValue)
                {
                    _dmmVoltage = dmmVoltage.Value;
                    DmmVoltageText = $"{dmmVoltage.Value:0.000} V";
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 万用表测量电压：{dmmVoltage.Value:0.000} V");

                    if (dmmVoltage.Value < DmmVoltageMin || dmmVoltage.Value > DmmVoltageMax)
                    {
                        failures.Add($"输出电压={dmmVoltage.Value:0.000}V 不在[{DmmVoltageMin},{DmmVoltageMax}]范围内");
                    }
                }
                else
                {
                    failures.Add("万用表测量失败");
                }

                token.ThrowIfCancellationRequested();

                // 步骤3：判读回采值POT_SUP_VBIT
                AddLog($"[{DateTime.Now:HH:mm:ss}] 步骤3：判读回采值POT_SUP_VBIT");
                var potSupResp = await WaitPotSupVbitAsync(timeoutMs: 4000, token);

                if (potSupResp == null)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] POT_SUP_VBIT 回采超时");
                    failures.Add("POT_SUP_VBIT 回采失败");
                }
                else
                {
                    if (!TryParsePotSupVbitValue(potSupResp, out var vbit))
                    {
                        failures.Add($"POT_SUP_VBIT 解析失败: 0x{FormatBytesHex(potSupResp)}");
                    }
                    else
                    {
                        _potSupVbit = vbit;
                        PotSupVbitText = $"{vbit:0.000} V";
                        AddLog($"[{DateTime.Now:HH:mm:ss}] 回采结果：POT_SUP_VBIT={vbit:0.000}V");
                        AddLog($"[{DateTime.Now:HH:mm:ss}] 原始数据：0x{FormatBytesHex(potSupResp)}");

                        if (vbit < PotSupVbitMin || vbit > PotSupVbitMax)
                        {
                            failures.Add($"POT_SUP_VBIT={vbit:0.000}V 不在[{PotSupVbitMin},{PotSupVbitMax}]范围内");
                        }
                    }
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
                await CleanupHardwareAsync();
                IsAutoTestRunning = false;
            }
        }

        private async Task SendEnterAtpAsync(CancellationToken token)
        {
            try
            {
                await _arinc.ClearRxFifoAsync(RxChannel);
            }
            catch { }

            await _arinc.SendBenchCommandOnlyAsync(TxChannel, AtpEnterCommand, msg => AddLog(msg), token);
            await Task.Delay(100, token);
        }

        private async Task<double?> MeasureDmmVoltageAsync(CancellationToken token)
        {
            try
            {
                // 连接万用表
                _dmmSocket ??= new DmmSocketApi();
                if (!_dmmSocket.IsConnected)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 正在连接万用表 {DmmIpAddress}...");
                    await _dmmSocket.ConnectAsync(DmmIpAddress, token);
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 万用表连接成功");
                }

                // 连接矩阵开关
                var matrix = MatrixControlService.Instance;

                AddLog($"[{DateTime.Now:HH:mm:ss}] 正在连接矩阵开关 2601(2) 1/14 和 2601(1) 4/2...");
                var ok1 = await matrix.ConnectNodesAsync(MatrixDmmRoute1.In, MatrixDmmRoute1.Out, MatrixDmmRoute1.Slot, MatrixIpAddress);
                var ok2 = await matrix.ConnectNodesAsync(MatrixDmmRoute2.In, MatrixDmmRoute2.Out, MatrixDmmRoute2.Slot, MatrixIpAddress);
                await Task.Delay(500, token);

                AddLog($"[{DateTime.Now:HH:mm:ss}] 矩阵连接 {(ok1 && ok2 ? "OK" : "FAIL")} - {MatrixDmmRoute1.In}-{MatrixDmmRoute1.Out}(slot{MatrixDmmRoute1.Slot}), {MatrixDmmRoute2.In}-{MatrixDmmRoute2.Out}(slot{MatrixDmmRoute2.Slot})");

                if (!ok1 || !ok2)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 矩阵通路连接失败");
                    return null;
                }

                // 读取直流电压
                var reading = await _dmmSocket.ReadOnceAsync(
                    DmmMeasureMode.DCV,
                    new DmmReadOptions { TimeoutMilliseconds = 3000 },
                    token);

                // 断开矩阵开关
                try { await matrix.DisconnectNodesAsync(MatrixDmmRoute1.In, MatrixDmmRoute1.Out, MatrixDmmRoute1.Slot, MatrixIpAddress); } catch { }
                try { await matrix.DisconnectNodesAsync(MatrixDmmRoute2.In, MatrixDmmRoute2.Out, MatrixDmmRoute2.Slot, MatrixIpAddress); } catch { }

                if (reading?.Value != null)
                    return reading.Value.Value;

                AddLog($"[{DateTime.Now:HH:mm:ss}] 万用表读数无效: {reading?.Raw}");
                return null;
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 万用表测量异常: {ex.Message}");
                return null;
            }
        }

        private async Task<byte[]> WaitPotSupVbitAsync(int timeoutMs, CancellationToken token)
        {
            var resp = await _arinc.WaitBenchResponse8Async(
                RxChannel,
                IsPotSupVbitPayload,
                timeoutMs,
                msg => AddLog(msg),
                token);

            return resp;
        }

        private static bool IsPotSupVbitPayload(byte[] frame)
        {
            return IsPrefix4(frame, PotSupVbitPrefix4);
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
        /// 解析POT_SUP_VBIT电压值
        /// 数据格式: 01 02 01 02 XX XX XX XX
        /// 电压值在最后2字节(data[6], data[7])，单位mV，需除以1000得到V
        /// </summary>
        private static bool TryParsePotSupVbitValue(byte[] frame, out double value)
        {
            value = 0;
            if (!IsPrefix4(frame, PotSupVbitPrefix4))
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

        private async Task CleanupHardwareAsync()
        {
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

            // 断开矩阵开关
            try
            {
                var matrix = MatrixControlService.Instance;
                await matrix.DisconnectNodesAsync(MatrixDmmRoute1.In, MatrixDmmRoute1.Out, MatrixDmmRoute1.Slot, MatrixIpAddress);
                await matrix.DisconnectNodesAsync(MatrixDmmRoute2.In, MatrixDmmRoute2.Out, MatrixDmmRoute2.Slot, MatrixIpAddress);
            }
            catch { }

            // 断开万用表
            try
            {
                if (_dmmSocket?.IsConnected == true)
                {
                    await _dmmSocket.DisconnectAsync(CancellationToken.None);
                }
            }
            catch { }
        }
    }
}
