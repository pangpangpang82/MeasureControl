using MeasureControl.Events;
using MeasureControl.Models;
using MeasureControl.Models.Devices;
using MeasureControl.Services;
using MeasureControl.Services.HardwareApis;
using MeasureControl.Simulations.FuelController;
using Newtonsoft.Json.Linq;
using Prism.Commands;
using Prism.Events;
using Prism.Ioc;
using Prism.Mvvm;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace MeasureControl.ViewModels.SingleBoardTest.FuelController
{
    /// <summary>
    /// 离散量采集功能测试ViewModel
    /// 测试HI-8435PQTF离散量采集芯片的SPI通信功能
    /// </summary>
    public class DiscreteInputTestViewModel : BindableBase, IDisposable
    {
        #region 常量

        /// <summary>
        /// 数据持久化键
        /// </summary>
        private const string PersistDataKey = "DiscreteInputTest";

        /// <summary>
        /// 硬件操作超时时间（毫秒）
        /// </summary>
        private const int HardwareTimeoutMs = 3000;

        /// <summary>
        /// 继电器控制通道，使用7131板卡的DO14（物理DO15映射到API的DO14）
        /// </summary>
        private const string RelayControlChannel = "DO14";

        /// <summary>
        /// 7131板卡DI输入阈值电压（3V）
        /// DI1-4、DI5-8、DI9-12的阈值设置为3V
        /// </summary>
        private const double Jy7131DiThresholdV = 3.0;

        #endregion

        #region 依赖服务

        private readonly IEventAggregator _eventAggregator;
        private readonly ProjectService _projectService;
        private readonly IPxiChassisService _pxiChassisService;
        private readonly IComponentPowerStateApi _componentPowerStateApi;
        private IJy7131Api _jy7131Api;

        #endregion

        #region 状态字段

        private readonly DiscreteInputSimulation _simulation;
        private CancellationTokenSource _testCts;
        private bool _disposed;
        private bool _hardwareInitialized;
        private bool _useSimulation = true;
        private FpgaIoClient _fpga;
        private bool _fpgaConnected;
        private bool _jy7131DiThresholdApplied;

        // DO0-DO13通道名称（物理DO1-DO14映射到API的DO0-DO13）
        private static readonly string[] DoChannels = new[]
        {
            "DO0", "DO1", "DO2", "DO3", "DO4", "DO5", "DO6",
            "DO7", "DO8", "DO9", "DO10", "DO11", "DO12", "DO13"
        };

        #endregion

        #region 测量结果字段

        // 接地测试结果（bank0[0:6] + bank1[0:6]）
        private readonly int[] _groundedTestResults = new int[DiscreteInputSimulation.TotalChannelCount];
        
        // 开路测试结果（bank0[0:6] + bank1[0:6]）
        private readonly int[] _openTestResults = new int[DiscreteInputSimulation.TotalChannelCount];

        #endregion

        #region 构造函数

        public DiscreteInputTestViewModel(
            IEventAggregator eventAggregator,
            ProjectService projectService,
            IPxiChassisService pxiChassisService)
        {
            _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
            _projectService = projectService ?? throw new ArgumentNullException(nameof(projectService));
            _pxiChassisService = pxiChassisService;

            _simulation = new DiscreteInputSimulation();

            // 尝试获取供电API
            try
            {
                _componentPowerStateApi = new ComponentPowerStateApi();
            }
            catch
            {
                _componentPowerStateApi = null;
            }

            // 初始化命令
            ManualTestCommand = new DelegateCommand(ExecuteManualTest, CanExecuteManualTest);
            AutoTestCommand = new DelegateCommand(ExecuteAutoTest, CanExecuteAutoTest);
            SetGroundedSignalCommand = new DelegateCommand(ExecuteSetGroundedSignal, CanExecuteSetSignal);
            SetOpenSignalCommand = new DelegateCommand(ExecuteSetOpenSignal, CanExecuteSetSignal);
            GroundedTestCommand = new DelegateCommand(ExecuteGroundedTest, CanExecuteGroundedTest);
            OpenTestCommand = new DelegateCommand(ExecuteOpenTest, CanExecuteOpenTest);
            ClearLogCommand = new DelegateCommand(ExecuteClearLog);

            // 订阅事件
            _eventAggregator.GetEvent<ProjectSavingEvent>().Subscribe(OnProjectSaving);

            // 加载持久化数据
            //LoadPersistedState();
        }

        #endregion

        #region 公共属性

        private bool _isManualTestRunning;
        public bool IsManualTestRunning
        {
            get => _isManualTestRunning;
            set
            {
                if (SetProperty(ref _isManualTestRunning, value))
                    UpdateCommandStates();
            }
        }

        private bool _isAutoTestRunning;
        public bool IsAutoTestRunning
        {
            get => _isAutoTestRunning;
            set
            {
                if (SetProperty(ref _isAutoTestRunning, value))
                    UpdateCommandStates();
            }
        }

        private bool _isPowerOn;
        public bool IsPowerOn
        {
            get => _isPowerOn;
            set => SetProperty(ref _isPowerOn, value);
        }

        private string _powerStatus = "未上电";
        public string PowerStatus
        {
            get => _powerStatus;
            set => SetProperty(ref _powerStatus, value);
        }

        private string _groundedTestResult = "--";
        public string GroundedTestResult
        {
            get => _groundedTestResult;
            set => SetProperty(ref _groundedTestResult, value);
        }

        private string _openTestResult = "--";
        public string OpenTestResult
        {
            get => _openTestResult;
            set => SetProperty(ref _openTestResult, value);
        }

        private string _overallResult = "--";
        public string OverallResult
        {
            get => _overallResult;
            set => SetProperty(ref _overallResult, value);
        }

        private string _lastTestTime = "--";
        public string LastTestTime
        {
            get => _lastTestTime;
            set => SetProperty(ref _lastTestTime, value);
        }

        // Bank0采集结果显示
        private string _bank0GroundedResults = "-- -- -- -- -- -- --";
        public string Bank0GroundedResults
        {
            get => _bank0GroundedResults;
            set => SetProperty(ref _bank0GroundedResults, value);
        }

        private string _bank1GroundedResults = "-- -- -- -- -- -- --";
        public string Bank1GroundedResults
        {
            get => _bank1GroundedResults;
            set => SetProperty(ref _bank1GroundedResults, value);
        }

        private string _bank0OpenResults = "-- -- -- -- -- -- --";
        public string Bank0OpenResults
        {
            get => _bank0OpenResults;
            set => SetProperty(ref _bank0OpenResults, value);
        }

        private string _bank1OpenResults = "-- -- -- -- -- -- --";
        public string Bank1OpenResults
        {
            get => _bank1OpenResults;
            set => SetProperty(ref _bank1OpenResults, value);
        }

        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();

        #endregion

        #region 命令

        public DelegateCommand ManualTestCommand { get; }
        public DelegateCommand AutoTestCommand { get; }
        public DelegateCommand SetGroundedSignalCommand { get; }
        public DelegateCommand SetOpenSignalCommand { get; }
        public DelegateCommand GroundedTestCommand { get; }
        public DelegateCommand OpenTestCommand { get; }
        public DelegateCommand ClearLogCommand { get; }

        #endregion

        #region 命令执行方法

        private bool CanExecuteManualTest() => !IsAutoTestRunning;
        private bool CanExecuteAutoTest() => !IsManualTestRunning;
        private bool CanExecuteSetSignal() => IsManualTestRunning && _hardwareInitialized;
        private bool CanExecuteGroundedTest() => IsManualTestRunning && _hardwareInitialized;
        private bool CanExecuteOpenTest() => IsManualTestRunning && _hardwareInitialized;

        /// <summary>
        /// 执行设置接地信号
        /// </summary>
        private async void ExecuteSetGroundedSignal()
        {
            if (_testCts == null || _testCts.IsCancellationRequested)
                return;

            try
            {
                AddLog("--- 设置接地信号 ---");
                await SetDoOutputAsync(true, _testCts.Token); // true = 接地（高电平）

                AddLog("接地信号已设置完成，可点击\"接地测试\"按钮进行测量");
            }
            catch (Exception ex)
            {
                AddLog($"设置接地信号异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 执行设置开路信号
        /// </summary>
        private async void ExecuteSetOpenSignal()
        {
            if (_testCts == null || _testCts.IsCancellationRequested)
                return;

            try
            {
                AddLog("--- 设置开路信号 ---");
                await SetDoOutputAsync(false, _testCts.Token); // false = 开路（低电平）
                
                AddLog("开路信号已设置完成，可点击\"开路测试\"按钮进行测量");
            }
            catch (Exception ex)
            {
                AddLog($"设置开路信号异常: {ex.Message}");
            }
        }

        private async void ExecuteManualTest()
        {
            if (IsManualTestRunning)
            {
                // 停止测试：取消令牌 + 安全复位硬件 + 重置状态
                try
                {
                    AddLog("========== 手动测试停止中... ==========");
                    _testCts?.Cancel();
                    await SafeResetHardwareAsync();
                }
                catch (Exception ex)
                {
                    AddLog($"停止手动测试异常: {ex.Message}");
                }
                finally
                {
                    try
                    {
                        _testCts?.Dispose();
                    }
                    catch { }

                    _testCts = null;
                    _hardwareInitialized = false;
                    IsManualTestRunning = false;
                    UpdateCommandStates();
                    AddLog("========== 手动测试已停止 ==========");
                }
                return;
            }

            if (!EnsureFuelBoardPowered())
                return;

            IsManualTestRunning = true;
            _testCts = new CancellationTokenSource();

            try
            {
                AddLog("========== 手动测试开始 ==========");
                await InitializeHardwareAsync(_testCts.Token);
                AddLog("硬件初始化完成，可以进行手动测试");
                AddLog("请点击\"接地测试\"或\"开路测试\"按钮进行测试");
            }
            catch (OperationCanceledException)
            {
                AddLog("手动测试已取消");
            }
            catch (Exception ex)
            {
                AddLog($"手动测试异常: {ex.Message}");
            }
        }

        private async void ExecuteAutoTest()
        {
            if (IsAutoTestRunning)
            {
                AddLog("正在停止自动测试...");
                _testCts?.Cancel();
                // 等待测试停止并重置状态
                await Task.Delay(200);
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    IsAutoTestRunning = false;
                    _hardwareInitialized = false;
                    UpdateCommandStates();
                });
                AddLog("自动测试已停止");
                return;
            }

            if (!EnsureFuelBoardPowered())
                return;

            _testCts = new CancellationTokenSource();
            try
            {
                await ExecuteAutoTestCoreAsync(_testCts.Token);
            }
            catch (OperationCanceledException)
            {
                // 已在 ExecuteAutoTestCoreAsync 中处理
            }
            catch (Exception ex)
            {
                AddLog($"自动测试异常: {ex.Message}");
            }
            finally
            {
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    IsAutoTestRunning = false;
                    _hardwareInitialized = false;
                    UpdateCommandStates();
                });
                _testCts?.Dispose();
                _testCts = null;
            }
        }

        public async Task<string> RunOnceAsync(CancellationToken cancellationToken)
        {
            if (IsAutoTestRunning || IsManualTestRunning)
            {
                _testCts?.Cancel();
                await Task.Delay(100, CancellationToken.None).ConfigureAwait(false);
            }

            _testCts?.Cancel();
            _testCts?.Dispose();
            _testCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            try
            {
                return await ExecuteAutoTestCoreAsync(_testCts.Token).ConfigureAwait(false);
            }
            finally
            {
                Application.Current?.Dispatcher?.Invoke(() => IsAutoTestRunning = false);
                _hardwareInitialized = false;
                UpdateCommandStates();
                _testCts?.Dispose();
                _testCts = null;
            }
        }

        private async Task<string> ExecuteAutoTestCoreAsync(CancellationToken token)
        {
            if (!EnsureFuelBoardPowered())
                return "不合格";

            Application.Current?.Dispatcher?.Invoke(() => IsAutoTestRunning = true);
            AddLog("========== 自动测试开始 ==========");

            try
            {
                await InitializeHardwareAsync(token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();

                AddLog("--- 步骤a: 接地测试 ---");
                await SetDoOutputAsync(true, token).ConfigureAwait(false);
                await Task.Delay(500, token).ConfigureAwait(false);
                bool groundedPass = await PerformGroundedTestAsync(token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();

                AddLog("--- 步骤b: 开路测试 ---");
                await SetDoOutputAsync(false, token).ConfigureAwait(false);
                await Task.Delay(500, token).ConfigureAwait(false);
                bool openPass = await PerformOpenTestAsync(token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();

                await ResetHardwareAsync(CancellationToken.None).ConfigureAwait(false);

                bool overallPass = groundedPass && openPass;
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    OverallResult = overallPass ? "合格" : "不合格";
                    LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                });

                AddLog($"========== 自动测试完成: {OverallResult} ==========");
                return OverallResult;
            }
            catch (OperationCanceledException)
            {
                AddLog("自动测试已取消");
                await SafeResetHardwareAsync().ConfigureAwait(false);
                throw;
            }
            catch (Exception ex)
            {
                AddLog($"自动测试异常: {ex.Message}");
                await SafeResetHardwareAsync().ConfigureAwait(false);
                return "不合格";
            }
        }

        private async void ExecuteGroundedTest()
        {
            if (_testCts == null || _testCts.IsCancellationRequested)
                return;

            try
            {
                AddLog("--- 执行接地测试 ---");
                await PerformGroundedTestAsync(_testCts.Token);
                UpdateOverallResultIfReady();
            }
            catch (Exception ex)
            {
                AddLog($"接地测试异常: {ex.Message}");
            }
        }

        private async void ExecuteOpenTest()
        {
            if (_testCts == null || _testCts.IsCancellationRequested)
                return;

            try
            {
                AddLog("--- 执行开路测试 ---");
                await PerformOpenTestAsync(_testCts.Token);
                UpdateOverallResultIfReady();
            }
            catch (Exception ex)
            {
                AddLog($"开路测试异常: {ex.Message}");
            }
        }

        private void UpdateOverallResultIfReady()
        {
            // 手动测试模式下：当a/b两步都完成后，自动更新综合结果和时间
            if (!IsManualTestRunning)
                return;

            if (string.IsNullOrWhiteSpace(GroundedTestResult) || string.IsNullOrWhiteSpace(OpenTestResult))
                return;

            if (string.Equals(GroundedTestResult, "--", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(OpenTestResult, "--", StringComparison.OrdinalIgnoreCase))
                return;

            bool overallPass =
                string.Equals(GroundedTestResult, "PASS", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(OpenTestResult, "PASS", StringComparison.OrdinalIgnoreCase);

            Application.Current?.Dispatcher?.Invoke(() =>
            {
                OverallResult = overallPass ? "PASS" : "FAIL";
                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            });
        }

        private void ExecuteClearLog()
        {
            Application.Current?.Dispatcher?.Invoke(() => Logs.Clear());
        }

        private void UpdateCommandStates()
        {
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                ManualTestCommand.RaiseCanExecuteChanged();
                AutoTestCommand.RaiseCanExecuteChanged();
                SetGroundedSignalCommand.RaiseCanExecuteChanged();
                SetOpenSignalCommand.RaiseCanExecuteChanged();
                GroundedTestCommand.RaiseCanExecuteChanged();
                OpenTestCommand.RaiseCanExecuteChanged();
            });
        }

        #endregion

        #region 测试执行方法

        /// <summary>
        /// 执行接地测试
        /// 使用7131板卡DI值作为判断接地的基准（阈值3V）
        /// </summary>
        private async Task<bool> PerformGroundedTestAsync(CancellationToken token)
        {
            // 1. 读取7131板卡DI值作为判断基准
            int[] diResults = await Read7131DiResultsAsync(token);
            AddLog($"[7131] DI读取结果: {string.Join(" ", diResults.Select(v => v.ToString()))}");

            // 2. 读取HI8435离散量采集结果（通过FPGA命令0x06）
            int[] hi8435Results;
            if (_fpgaConnected && _fpga != null)
            {
                try
                {
                    hi8435Results = await ReadHi8435WithAsyncReceiveAsync(token);
                }
                catch (Exception ex)
                {
                    AddLog($"[FPGA] HI8435读取失败: {ex.Message}，降级仿真");
                    hi8435Results = await _simulation.ReadDiscreteInputsAsync(AddLog, token);
                }
            }
            else
            {
                hi8435Results = await _simulation.ReadDiscreteInputsAsync(AddLog, token);
            }

            // 3. 保存结果
            Array.Copy(hi8435Results, _groundedTestResults, hi8435Results.Length);

            // 4. 更新UI显示
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                Bank0GroundedResults = FormatBankResults(hi8435Results, 0, DiscreteInputSimulation.Bank0ChannelCount);
                Bank1GroundedResults = FormatBankResults(hi8435Results, DiscreteInputSimulation.Bank0ChannelCount, DiscreteInputSimulation.Bank1ChannelCount);
            });

            // 5. 判定结果：使用7131板卡DI值作为基准，接地时DI应为高电平(1)
            bool pass = true;
            for (int i = 0; i < diResults.Length && i < hi8435Results.Length; i++)
            {
                // 接地测试：7131 DI应为高电平(1)，HI8435也应为1
                if (diResults[i] != 1)
                {
                    pass = false;
                    AddLog($"  通道{i}: 7131 DI期望1, 实际{diResults[i]} - FAIL");
                }
                if (hi8435Results[i] != 1)
                {
                    pass = false;
                    AddLog($"  通道{i}: HI8435期望1, 实际{hi8435Results[i]} - FAIL");
                }
            }

            Application.Current?.Dispatcher?.Invoke(() =>
            {
                GroundedTestResult = pass ? "PASS" : "FAIL";
            });

            AddLog($"接地测试结果: {(pass ? "PASS" : "FAIL")}");
            return pass;
        }

        /// <summary>
        /// 执行开路测试
        /// 使用7131板卡DI值作为判断开路的基准（阈值3V）
        /// </summary>
        private async Task<bool> PerformOpenTestAsync(CancellationToken token)
        {
            // 1. 读取7131板卡DI值作为判断基准
            int[] diResults = await Read7131DiResultsAsync(token);
            AddLog($"[7131] DI读取结果: {string.Join(" ", diResults.Select(v => v.ToString()))}");

            // 2. 读取HI8435离散量采集结果（通过FPGA命令0x06）
            int[] hi8435Results;
            if (_fpgaConnected && _fpga != null)
            {
                try
                {
                    hi8435Results = await ReadHi8435WithAsyncReceiveAsync(token);
                }
                catch (Exception ex)
                {
                    AddLog($"[FPGA] HI8435读取失败: {ex.Message}，降级仿真");
                    hi8435Results = await _simulation.ReadDiscreteInputsAsync(AddLog, token);
                }
            }
            else
            {
                hi8435Results = await _simulation.ReadDiscreteInputsAsync(AddLog, token);
            }

            // 3. 保存结果
            Array.Copy(hi8435Results, _openTestResults, hi8435Results.Length);

            // 4. 更新UI显示
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                Bank0OpenResults = FormatBankResults(hi8435Results, 0, DiscreteInputSimulation.Bank0ChannelCount);
                Bank1OpenResults = FormatBankResults(hi8435Results, DiscreteInputSimulation.Bank0ChannelCount, DiscreteInputSimulation.Bank1ChannelCount);
            });

            // 5. 判定结果：使用7131板卡DI值作为基准，开路时DI应为低电平(0)
            bool pass = true;
            for (int i = 0; i < diResults.Length && i < hi8435Results.Length; i++)
            {
                // 开路测试：7131 DI应为低电平(0)，HI8435也应为0
                if (diResults[i] != 0)
                {
                    pass = false;
                    AddLog($"  通道{i}: 7131 DI期望0, 实际{diResults[i]} - FAIL");
                }
                if (hi8435Results[i] != 0)
                {
                    pass = false;
                    AddLog($"  通道{i}: HI8435期望0, 实际{hi8435Results[i]} - FAIL");
                }
            }

            Application.Current?.Dispatcher?.Invoke(() =>
            {
                OpenTestResult = pass ? "PASS" : "FAIL";
            });

            AddLog($"开路测试结果: {(pass ? "PASS" : "FAIL")}");
            return pass;
        }

        /// <summary>
        /// 通过FPGA命令0x06读取HI8435 BANK3-0，并检测异步接收中的响应
        /// 协议：发送 AA 55 02 06 00，接收 AA 55 05 06 [bank3] [bank2] [bank1] [bank0]
        /// </summary>
        private async Task<int[]> ReadHi8435WithAsyncReceiveAsync(CancellationToken token)
        {
            const byte Cmd06 = 0x06;
            const int WaitTimeMs = 3000; // 等待响应的最大时间

            // 记录发送前的时间，用于过滤旧帧
            var sendTime = DateTime.UtcNow;

            // 1. 发送命令0x06读取HI8435
            AddLog("[FPGA] 发送命令0x06读取HI8435 BANK3-0...");
            await _fpga.SendReadHi8435CommandAsync(token);

            // 2. 等待并检测异步接收中的响应
            AddLog("[FPGA] 等待异步接收响应...");
            var startTime = DateTime.Now;
            byte[] responseData = null;

            while ((DateTime.Now - startTime).TotalMilliseconds < WaitTimeMs)
            {
                token.ThrowIfCancellationRequested();

                // 检查异步接收缓存中是否有命令0x06的响应（发送后收到的）
                var frames = _fpga.GetReceivedFramesByCommandAfter(Cmd06, sendTime);
                if (frames != null && frames.Count > 0)
                {
                    var latestFrame = frames[frames.Count - 1];
                    if (latestFrame.Payload != null && latestFrame.Payload.Length >= 4)
                    {
                        responseData = latestFrame.Payload;
                        AddLog($"[FPGA] 收到命令0x06响应: {latestFrame.RawHex}");
                        break;
                    }
                }

                await Task.Delay(100, token);
            }

            // 3. 检查是否收到响应
            if (responseData == null || responseData.Length < 4)
            {
                // 弹窗提示
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    MessageBox.Show(
                        "未收到FPGA的HI8435读取响应（命令0x06）。\n请检查FPGA连接和通信状态。",
                        "通信超时",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                });
                throw new TimeoutException("FPGA未返回HI8435读取响应");
            }

            // 4. 解析响应数据
            // 响应格式：[bank3] [bank2] [bank1] [bank0]，每个bank 8位
            // 我们只需要bank0[0:6]和bank1[0:6]，共14个通道
            byte bank0 = responseData.Length > 3 ? responseData[3] : (byte)0;
            byte bank1 = responseData.Length > 2 ? responseData[2] : (byte)0;

            AddLog($"[FPGA] HI8435数据: Bank0=0x{bank0:X2}, Bank1=0x{bank1:X2}");

            // 5. 转换为通道结果数组
            int[] results = new int[DiscreteInputSimulation.TotalChannelCount];

            // Bank0[0:6] -> results[0:6]
            for (int i = 0; i < DiscreteInputSimulation.Bank0ChannelCount && i < 7; i++)
            {
                results[i] = (bank0 >> i) & 1;
            }

            // Bank1[0:6] -> results[7:13]
            for (int i = 0; i < DiscreteInputSimulation.Bank1ChannelCount && i < 7; i++)
            {
                results[DiscreteInputSimulation.Bank0ChannelCount + i] = (bank1 >> i) & 1;
            }

            return results;
        }

        /// <summary>
        /// 格式化Bank结果显示
        /// </summary>
        private string FormatBankResults(int[] results, int startIndex, int count)
        {
            var parts = new string[count];
            for (int i = 0; i < count; i++)
            {
                parts[i] = results[startIndex + i].ToString();
            }
            return string.Join(" ", parts);
        }

        /// <summary>
        /// 读取7131板卡DI值（DI0-DI13，共14通道）
        /// 作为判断接地/开路的基准
        /// </summary>
        private async Task<int[]> Read7131DiResultsAsync(CancellationToken token)
        {
            int[] results = new int[DiscreteInputSimulation.TotalChannelCount];

            if (_jy7131Api != null && _jy7131Api.IsConnected)
            {
                try
                {
                    // 读取DI0-DI13（对应14个通道）
                    for (int i = 0; i < DiscreteInputSimulation.TotalChannelCount; i++)
                    {
                        string diChannel = $"DI{i}";
                        bool value = await _jy7131Api.ReadDiAsync(diChannel, token);
                        results[i] = value ? 1 : 0;
                    }
                    AddLog($"[7131] 成功读取DI0-DI13");
                }
                catch (Exception ex)
                {
                    AddLog($"[7131] DI读取失败: {ex.Message}，使用仿真值");
                    // 仿真模式：返回全0
                    for (int i = 0; i < results.Length; i++)
                        results[i] = 0;
                }
            }
            else
            {
                AddLog("[7131] 板卡不可用，使用仿真DI值");
                // 仿真模式：返回全0
                for (int i = 0; i < results.Length; i++)
                    results[i] = 0;
            }

            return results;
        }

        #endregion

        #region 硬件操作

        /// <summary>
        /// 初始化硬件
        /// </summary>
        private async Task InitializeHardwareAsync(CancellationToken token)
        {
            AddLog("检测组件供电状态...");
            if (!EnsureFuelBoardPowered())
                throw new InvalidOperationException("请先给加放油单板上电后再进行测试。");
            AddLog("已检测到加放油单板处于上电状态");

            // 2. 连接7131板卡（用于提供地/开信号）
            AddLog("正在连接7131板卡...");
            if (_jy7131Api == null)
            {
                var device7131 = FindFirstJy7131Device();
                if (device7131 != null)
                {
                    string devSlot = Infer7131SlotNumber(device7131);
                    AddLog($"找到7131板卡: {device7131.Model ?? device7131.Name}，槽位={devSlot}");
                    if (int.TryParse(devSlot, out int slotNum))
                        _jy7131Api = new Jy7131Api(device7131, slotNum);
                    else
                        _jy7131Api = new Jy7131Api(device7131);
                }
            }

            if (_jy7131Api != null)
            {
                try
                {
                    if (!_jy7131Api.IsConnected)
                    {
                        await _jy7131Api.ConnectAsync(token);
                        AddLog("7131板卡连接成功");
                        await _jy7131Api.SetOutputModeAsync(Jy7131OutputMode.Sinking, token);
                        await _jy7131Api.StartAsync(token);
                        AddLog("7131板卡已启动");
                    }

                    // 设置DI输入阈值为3V（DI1-4、DI5-8、DI9-12）
                    if (!_jy7131DiThresholdApplied)
                    {
                        AddLog("正在设置7131板卡DI输入阈值为3V...");
                        await _jy7131Api.ApplyDiThresholdsAsync(new Jy7131DiThresholds
                        {
                            Group1 = Jy7131DiThresholdV,  // DI1-4
                            Group2 = Jy7131DiThresholdV,  // DI5-8
                            Group3 = Jy7131DiThresholdV,  // DI9-12
                            Group4 = Jy7131DiThresholdV,
                            Group5 = Jy7131DiThresholdV,
                            Group6 = Jy7131DiThresholdV,
                            Group7 = Jy7131DiThresholdV,
                            Group8 = Jy7131DiThresholdV,
                        }, token);
                        _jy7131DiThresholdApplied = true;
                        AddLog("7131板卡DI输入阈值设置完成（3V）");
                    }
                }
                catch (Exception ex)
                {
                    AddLog($"7131板卡初始化异常: {ex.Message}，使用仿真信号模式");
                    _jy7131Api = null;
                }
            }

            // 3. 连接FPGA
            AddLog("正在连接FPGA...");
            try
            {
                _fpga ??= new FpgaIoClient();
                if (!_fpga.IsConnected)
                    await _fpga.ConnectAsync(token);
                _fpgaConnected = true;
                AddLog("FPGA连接成功");

                // 4. 初始化HI8435 (cmd 0x04)
                AddLog("正在初始化HI8435...");
                await _fpga.InitHi8435AfterConnectAsync(token);
                await Task.Delay(50, token);
                AddLog("HI8435初始化完成");

                // 启动异步接收功能
                _fpga.StartAsyncReceive(AddLog);
            }
            catch (Exception ex)
            {
                AddLog($"FPGA连接/初始化失败: {ex.Message}，将使用仿真模式读取");
                _fpgaConnected = false;
            }

            _hardwareInitialized = true;
            UpdateCommandStates();
        }

        private DeviceBase FindFirstJy7131Device()
        {
            var chassisList = _pxiChassisService?.GetAllChassis();
            if (chassisList == null)
                return null;

            foreach (var chassis in chassisList)
            {
                if (chassis?.Devices == null)
                    continue;

                var device = chassis.Devices.FirstOrDefault(d =>
                    d is MeasureControl.Models.Devices.DigitalIODevice ||
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
                        c is MeasureControl.Models.Devices.DigitalIODevice ||
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

        /// <summary>
        /// 复位硬件
        /// </summary>
        private async Task ResetHardwareAsync(CancellationToken token)
        {
            AddLog("正在复位硬件...");

            // 关闭 485 继电器前 4 路
            AddLog("正在关闭 485 继电器前 4 路...");
            try
            {
                await _jy7131Api.SetRelayAsync(0, false, token);
                await _jy7131Api.SetRelayAsync(1, false, token);
                await _jy7131Api.SetRelayAsync(2, false, token);
                await _jy7131Api.SetRelayAsync(3, false, token);
                AddLog("485 继电器前 4 路已关闭");
            }
            catch (Exception ex)
            {
                AddLog($"485 继电器操作失败: {ex.Message}");
            }

            // 断开7131
            if (_jy7131Api != null)
            {
                try
                {
                    if (_jy7131Api.IsRunning) await _jy7131Api.StopAsync(token);
                    await _jy7131Api.DisconnectAsync(token);
                    AddLog("7131板卡已断开");
                }
                catch { }
                _jy7131Api = null;
                _jy7131DiThresholdApplied = false;
            }

            // 断开FPGA
            if (_fpga != null)
            {
                try { _fpga.StopAsyncReceive(); } catch { }
                try { _fpga.Disconnect(); } catch { }
                _fpga = null;
                _fpgaConnected = false;
            }

            _hardwareInitialized = false;
            AddLog("硬件复位完成");
            RefreshPowerStateDisplay();
        }

        private bool EnsureFuelBoardPowered()
        {
            var powerService = ContainerLocator.Container.Resolve<IHydraulicPowerService>();
            if (powerService == null || !powerService.IsHydraulicPowered)
            {
                AddLog("未检测到加放油单板上电，请先通过左上角组件上电按钮上电。");
                Application.Current?.Dispatcher?.Invoke(() =>
                    MessageBox.Show("请先点击左上角组件上电按钮，并选择“加放油单板”上电后再进行测试。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning));
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    IsPowerOn = false;
                    PowerStatus = "未上电";
                });
                return false;
            }

            if (!string.Equals(powerService.PoweredBoardType, "加放油单板", StringComparison.OrdinalIgnoreCase))
            {
                AddLog($"当前上电单板为{powerService.PoweredBoardType ?? "未知"}，请切换为加放油单板。");
                Application.Current?.Dispatcher?.Invoke(() =>
                    MessageBox.Show($"当前已上电单板为“{powerService.PoweredBoardType ?? "未知"}”，请先下电并选择“加放油单板”上电。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning));
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    IsPowerOn = false;
                    PowerStatus = "未上电";
                });
                return false;
            }

            Application.Current?.Dispatcher?.Invoke(() =>
            {
                IsPowerOn = true;
                PowerStatus = "已上电";
            });
            return true;
        }

        private void RefreshPowerStateDisplay()
        {
            var powerService = ContainerLocator.Container.Resolve<IHydraulicPowerService>();
            var isFuelPowered = powerService != null && powerService.IsHydraulicPowered &&
                                string.Equals(powerService.PoweredBoardType, "加放油单板", StringComparison.OrdinalIgnoreCase);
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                IsPowerOn = isFuelPowered;
                PowerStatus = isFuelPowered ? "已上电" : "未上电";
            });
        }

        /// <summary>
        /// 安全复位硬件（忽略异常）
        /// </summary>
        private async Task SafeResetHardwareAsync()
        {
            try
            {
                using (var cts = new CancellationTokenSource(5000))
                {
                    await ResetHardwareAsync(cts.Token);
                }
            }
            catch
            {
                // 忽略异常
            }
        }

        #endregion

        #region DO输出控制

        /// <summary>
        /// 设置DO1-DO14输出状态（参考DiscreteOutputTestViewModel）
        /// </summary>
        /// <param name="grounded">true=接地（高电平），false=开路（低电平）</param>
        private async Task SetDoOutputAsync(bool grounded, CancellationToken token)
        {
            if (_jy7131Api != null && _jy7131Api.IsConnected)
            {
                try
                {
                    // 确保7131板卡已连接并启动
                    //await _jy7131Api.EnsureConnectedAndRunningAsync(token);
                    if (!_jy7131Api.IsRunning)
                    {
                        await _jy7131Api.SetOutputModeAsync(Jy7131OutputMode.Sinking, token);
                        await _jy7131Api.StartAsync(token);
                        AddLog("7131板卡已启动");
                    }

                    //打开前四路 485 继电器
                    AddLog("正在打开 485 继电器前 4 路...");
                    try
                    {
                        await _jy7131Api.SetRelayAsync(0, true, token);
                        await _jy7131Api.SetRelayAsync(1, true, token);
                        await _jy7131Api.SetRelayAsync(2, true, token);
                        await _jy7131Api.SetRelayAsync(3, true, token);
                        AddLog("485 继电器前 4 路已打开");
                    }
                    catch (Exception ex)
                    {
                        AddLog($"485 继电器操作失败: {ex.Message}");
                    }

                    // 设置DO1-DO14输出
                    AddLog($"正在写DO1-DO14（{(grounded ? "高电平" : "低电平")}）...");
                    foreach (var channel in DoChannels)
                    {
                        await _jy7131Api.WriteDoAsync(channel, grounded, token);
                    }
                    AddLog($"[7131] DO1-DO14已设置为{(grounded ? "接地（高电平）" : "开路（低电平）")}");

                    // 回读验证DO输出状态
                    try
                    {
                        var mask = await _jy7131Api.ReadDoBitmaskAsync(token);
                        // DO0-DO13对应bit0-bit13
                        uint expectedMask = grounded ? 0x3FFFu : 0x0000u;
                        uint actualDo0To13 = mask & 0x3FFFu;
                        bool verified = (grounded && actualDo0To13 == expectedMask) || (!grounded && actualDo0To13 == 0);
                        AddLog($"DO回读验证: mask=0x{mask:X8}, DO0-13=0x{actualDo0To13:X4}, 期望=0x{expectedMask:X4}, {(verified ? "✓" : "✗")}");
                }
                catch (Exception ex)
                {
                        AddLog($"DO回读验证失败: {ex.Message}");
                    }
                }
                catch (Exception ex)
                {
                    AddLog($"[7131] DO写入失败: {ex.Message}，降级仿真");
                    if (grounded)
                        await _simulation.SetAllDoGroundedAsync(AddLog, token);
                    else
                        await _simulation.SetAllDoOpenAsync(AddLog, token);
                }
            }
            else
            {
                AddLog("[7131] 板卡不可用，使用仿真模式");
                if (grounded)
                    await _simulation.SetAllDoGroundedAsync(AddLog, token);
                else
                    await _simulation.SetAllDoOpenAsync(AddLog, token);
            }
        }

        /// <summary>
        /// 激活继电器，然后 DO15 输出
        /// </summary>
        private async Task ActivateRelayAsync(CancellationToken token)
        {
            if (_jy7131Api != null && _jy7131Api.IsConnected)
            {
                try
                {
                    //await _jy7131Api.EnsureConnectedAndRunningAsync(token);

                    if (!_jy7131Api.IsRunning)
                    {
                        await _jy7131Api.SetOutputModeAsync(Jy7131OutputMode.Sinking, token);
                        await _jy7131Api.StartAsync(token);
                        AddLog("7131板卡已启动");
                    }

                    

                    // 2. 再输出 DO15
                    AddLog("正在激活继电器 (DO15高电平)...");
                    await _jy7131Api.WriteDoAsync(RelayControlChannel, true, token);

                    // 回读验证
                    var mask = await _jy7131Api.ReadDoBitmaskAsync(token);
                    bool do14State = (mask & (1u << 14)) != 0;
                    AddLog($"继电器已激活: DO14={do14State}");
                    
                    await Task.Delay(200, token); // 等待继电器动作完成
                }
                catch (Exception ex)
                {
                    AddLog($"激活继电器异常: {ex.Message}");
                }
            }
            else
            {
                AddLog("[7131] 板卡不可用，跳过继电器激活");
            }
        }

        #endregion

        #region 辅助方法

        private void AddLog(string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            var logEntry = $"[{timestamp}] {message}";

            Application.Current?.Dispatcher?.Invoke(() =>
            {
                Logs.Add(logEntry);
            });

            System.Diagnostics.Trace.WriteLine($"[DiscreteInputTest] {logEntry}");
        }

        #endregion

        #region 数据持久化

        private void LoadPersistedState()
        {
            try
            {
                var root = _projectService?.CurrentProjectRoot;
                if (root?.TestInterfaceControls == null)
                    return;

                if (!root.TestInterfaceControls.TryGetValue(PersistDataKey, out var items) || items == null)
                    return;

                string Read(string key)
                {
                    return items.FirstOrDefault(x => string.Equals(x?.BoundVariableName, key, StringComparison.OrdinalIgnoreCase))?.BoundVariablePath;
                }

                GroundedTestResult = Read("GroundedTestResult") ?? "--";
                OpenTestResult = Read("OpenTestResult") ?? "--";
                OverallResult = Read("OverallResult") ?? "--";
                LastTestTime = Read("LastTestTime") ?? "--";
                Bank0GroundedResults = Read("Bank0GroundedResults") ?? "-- -- -- -- -- -- --";
                Bank1GroundedResults = Read("Bank1GroundedResults") ?? "-- -- -- -- -- -- --";
                Bank0OpenResults = Read("Bank0OpenResults") ?? "-- -- -- -- -- -- --";
                Bank1OpenResults = Read("Bank1OpenResults") ?? "-- -- -- -- -- -- --";
            }
            catch
            {
                // 忽略加载异常
            }
        }

        private void OnProjectSaving()
        {
            try
            {
                var root = _projectService?.CurrentProjectRoot;
                if (root?.TestInterfaceControls == null)
                    return;

                if (!root.TestInterfaceControls.TryGetValue(PersistDataKey, out var items) || items == null)
                {
                    items = new System.Collections.Generic.List<TestInterfaceControlItem>();
                    root.TestInterfaceControls[PersistDataKey] = items;
                }

                void Upsert(string key, string value)
                {
                    var item = items.FirstOrDefault(x => string.Equals(x?.BoundVariableName, key, StringComparison.OrdinalIgnoreCase));
                    if (item == null)
                    {
                        item = new TestInterfaceControlItem
                        {
                            ControlType = "Value",
                            BoundVariableName = key
                        };
                        items.Add(item);
                    }
                    item.BoundVariablePath = value ?? string.Empty;
                }

                Upsert("GroundedTestResult", GroundedTestResult);
                Upsert("OpenTestResult", OpenTestResult);
                Upsert("OverallResult", OverallResult);
                Upsert("LastTestTime", LastTestTime);
                Upsert("Bank0GroundedResults", Bank0GroundedResults);
                Upsert("Bank1GroundedResults", Bank1GroundedResults);
                Upsert("Bank0OpenResults", Bank0OpenResults);
                Upsert("Bank1OpenResults", Bank1OpenResults);
            }
            catch
            {
                // 忽略保存异常
            }
        }

        #endregion

        /// <summary>
        /// 通过FPGA读取HI8435 BANK3-0状态，解析为14通道结果数组
        /// cmd 0x06 → 返回4字节 byte0-3对应bank3-0
        /// bank0 bit[0:6] → 通道0-6, bank1 bit[0:6] → 通道7-13
        /// </summary>
        private async Task<int[]> ReadHi8435ResultsAsync(CancellationToken token)
        {
            var banks = await _fpga.ReadHi8435Async(token);
            AddLog($"[FPGA] HI8435 bank3={banks[0]:X2} bank2={banks[1]:X2} bank1={banks[2]:X2} bank0={banks[3]:X2}");

            var results = new int[DiscreteInputSimulation.TotalChannelCount];
            // bank3 (banks[0]) bit0-6 → 通道0-6 (bank0 of HI8435)
            for (int i = 0; i < DiscreteInputSimulation.Bank0ChannelCount; i++)
                results[i] = (banks[0] >> i) & 1;
            // bank2 (banks[1]) bit0-6 → 通道7-13 (bank1 of HI8435)
            for (int i = 0; i < DiscreteInputSimulation.Bank1ChannelCount; i++)
                results[DiscreteInputSimulation.Bank0ChannelCount + i] = (banks[1] >> i) & 1;
            return results;
        }

        #region IDisposable

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _testCts?.Cancel();
            _testCts?.Dispose();
            _simulation?.Dispose();

            try { _fpga?.Disconnect(); } catch { }
            _fpga = null;

            if (_componentPowerStateApi != null)
            {
                try
                {
                    _componentPowerStateApi.DisposeAsync().AsTask().Wait(1000);
                }
                catch { }
            }

            _eventAggregator?.GetEvent<ProjectSavingEvent>().Unsubscribe(OnProjectSaving);
        }

        #endregion
    }
}
