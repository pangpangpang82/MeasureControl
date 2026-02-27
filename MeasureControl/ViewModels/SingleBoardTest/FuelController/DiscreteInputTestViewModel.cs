using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using MeasureControl.Events;
using MeasureControl.Models;
using MeasureControl.Services;
using MeasureControl.Services.HardwareApis;
using MeasureControl.Simulations.FuelController;
using Prism.Commands;
using Prism.Events;
using Prism.Mvvm;

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
        private const int HardwareTimeoutMs = 10000;

        #endregion

        #region 依赖服务

        private readonly IEventAggregator _eventAggregator;
        private readonly ProjectService _projectService;
        private readonly IPxiChassisService _pxiChassisService;
        private readonly IComponentPowerStateApi _componentPowerStateApi;

        #endregion

        #region 状态字段

        private readonly DiscreteInputSimulation _simulation;
        private CancellationTokenSource _testCts;
        private bool _disposed;
        private bool _hardwareInitialized;
        private bool _useSimulation = true;
        private FpgaIoClient _fpga;
        private bool _fpgaConnected;

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
            GroundedTestCommand = new DelegateCommand(ExecuteGroundedTest, CanExecuteGroundedTest);
            OpenTestCommand = new DelegateCommand(ExecuteOpenTest, CanExecuteOpenTest);
            ClearLogCommand = new DelegateCommand(ExecuteClearLog);

            // 订阅事件
            _eventAggregator.GetEvent<ProjectSavingEvent>().Subscribe(OnProjectSaving);

            // 加载持久化数据
            LoadPersistedState();
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
        public DelegateCommand GroundedTestCommand { get; }
        public DelegateCommand OpenTestCommand { get; }
        public DelegateCommand ClearLogCommand { get; }

        #endregion

        #region 命令执行方法

        private bool CanExecuteManualTest() => !IsAutoTestRunning;
        private bool CanExecuteAutoTest() => !IsManualTestRunning;
        private bool CanExecuteGroundedTest() => IsManualTestRunning && _hardwareInitialized;
        private bool CanExecuteOpenTest() => IsManualTestRunning && _hardwareInitialized;

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
                _testCts?.Cancel();
                return;
            }

            IsAutoTestRunning = true;
            _testCts = new CancellationTokenSource();

            try
            {
                AddLog("========== 自动测试开始 ==========");

                // 1. 初始化硬件
                await InitializeHardwareAsync(_testCts.Token);

                // 2. 执行接地测试
                AddLog("--- 步骤a: 接地测试 ---");
                bool groundedPass = await PerformGroundedTestAsync(_testCts.Token);

                // 3. 执行开路测试
                AddLog("--- 步骤b: 开路测试 ---");
                bool openPass = await PerformOpenTestAsync(_testCts.Token);

                // 4. 复位硬件
                await ResetHardwareAsync(_testCts.Token);

                // 5. 判定综合结果
                bool overallPass = groundedPass && openPass;
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    OverallResult = overallPass ? "PASS" : "FAIL";
                    LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                });

                AddLog($"========== 自动测试完成: {(overallPass ? "PASS" : "FAIL")} ==========");
            }
            catch (OperationCanceledException)
            {
                AddLog("自动测试已取消");
                await SafeResetHardwareAsync();
            }
            catch (Exception ex)
            {
                AddLog($"自动测试异常: {ex.Message}");
                await SafeResetHardwareAsync();
            }
            finally
            {
                IsAutoTestRunning = false;
                _hardwareInitialized = false;
                UpdateCommandStates();
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
                GroundedTestCommand.RaiseCanExecuteChanged();
                OpenTestCommand.RaiseCanExecuteChanged();
            });
        }

        #endregion

        #region 测试执行方法

        /// <summary>
        /// 执行接地测试
        /// </summary>
        private async Task<bool> PerformGroundedTestAsync(CancellationToken token)
        {
            // 1. 设置所有DO通道为接地状态
            if (_fpgaConnected && _fpga != null)
            {
                try
                {
                    // IO11-32 对应 bit0-21; DO通道对应FPGA GPIO输出
                    // 接地状态：将DO对应的GPIO输出置低（接地）→ MUX选通
                    await _fpga.WriteGpioAsync(0x00000000u, token);
                    AddLog("[FPGA] DO通道全部设置为接地（GPIO全低）");
                }
                catch (Exception ex)
                {
                    AddLog($"[FPGA] GPIO写入失败: {ex.Message}，降级仿真");
                    await _simulation.SetAllDoGroundedAsync(AddLog, token);
                }
            }
            else
            {
                await _simulation.SetAllDoGroundedAsync(AddLog, token);
            }

            // 2. 等待稳定
            await Task.Delay(100, token);

            // 3. 读取离散量采集结果
            int[] results;
            if (_fpgaConnected && _fpga != null)
            {
                try
                {
                    results = await ReadHi8435ResultsAsync(token);
                }
                catch (Exception ex)
                {
                    AddLog($"[FPGA] HI8435读取失败: {ex.Message}，降级仿真");
                    results = await _simulation.ReadDiscreteInputsAsync(AddLog, token);
                }
            }
            else
            {
                results = await _simulation.ReadDiscreteInputsAsync(AddLog, token);
            }

            // 4. 保存结果
            Array.Copy(results, _groundedTestResults, results.Length);

            // 5. 更新UI显示
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                Bank0GroundedResults = FormatBankResults(results, 0, DiscreteInputSimulation.Bank0ChannelCount);
                Bank1GroundedResults = FormatBankResults(results, DiscreteInputSimulation.Bank0ChannelCount, DiscreteInputSimulation.Bank1ChannelCount);
            });

            // 6. 判定结果：所有通道结果均为1
            bool pass = true;
            for (int i = 0; i < results.Length; i++)
            {
                if (results[i] != 1)
                {
                    pass = false;
                    AddLog($"  通道{i}: 期望1, 实际{results[i]} - FAIL");
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
        /// </summary>
        private async Task<bool> PerformOpenTestAsync(CancellationToken token)
        {
            // 1. 设置所有DO通道为开路状态
            if (_fpgaConnected && _fpga != null)
            {
                try
                {
                    // 开路状态：将DO对应的GPIO输出置高（悬空/开路）
                    // IO11-32 bit0-21全部置1表示输出高
                    await _fpga.WriteGpioAsync(0x003FFFFFu, token);
                    AddLog("[FPGA] DO通道全部设置为开路（GPIO全高）");
                }
                catch (Exception ex)
                {
                    AddLog($"[FPGA] GPIO写入失败: {ex.Message}，降级仿真");
                    await _simulation.SetAllDoOpenAsync(AddLog, token);
                }
            }
            else
            {
                await _simulation.SetAllDoOpenAsync(AddLog, token);
            }

            // 2. 等待稳定
            await Task.Delay(100, token);

            // 3. 读取离散量采集结果
            int[] results;
            if (_fpgaConnected && _fpga != null)
            {
                try
                {
                    results = await ReadHi8435ResultsAsync(token);
                }
                catch (Exception ex)
                {
                    AddLog($"[FPGA] HI8435读取失败: {ex.Message}，降级仿真");
                    results = await _simulation.ReadDiscreteInputsAsync(AddLog, token);
                }
            }
            else
            {
                results = await _simulation.ReadDiscreteInputsAsync(AddLog, token);
            }

            // 4. 保存结果
            Array.Copy(results, _openTestResults, results.Length);

            // 5. 更新UI显示
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                Bank0OpenResults = FormatBankResults(results, 0, DiscreteInputSimulation.Bank0ChannelCount);
                Bank1OpenResults = FormatBankResults(results, DiscreteInputSimulation.Bank0ChannelCount, DiscreteInputSimulation.Bank1ChannelCount);
            });

            // 6. 判定结果：所有通道结果均为0
            bool pass = true;
            for (int i = 0; i < results.Length; i++)
            {
                if (results[i] != 0)
                {
                    pass = false;
                    AddLog($"  通道{i}: 期望0, 实际{results[i]} - FAIL");
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

        #endregion

        #region 硬件操作

        /// <summary>
        /// 初始化硬件
        /// </summary>
        private async Task InitializeHardwareAsync(CancellationToken token)
        {
            // 1. 设置组件28V供电状态
            AddLog("正在设置组件28V供电状态...");

            if (_componentPowerStateApi != null)
            {
                try
                {
                    await _componentPowerStateApi.ApplyComponent28VStateAsync(token);
                    AddLog("组件28V供电状态已设置");
                    _useSimulation = false;
                }
                catch (Exception ex)
                {
                    AddLog($"供电API异常: {ex.Message}，使用仿真模式");
                    await _simulation.ApplyComponent28VStateAsync(AddLog, token);
                    _useSimulation = true;
                }
            }
            else
            {
                await _simulation.ApplyComponent28VStateAsync(AddLog, token);
                _useSimulation = true;
            }

            Application.Current?.Dispatcher?.Invoke(() =>
            {
                IsPowerOn = true;
                PowerStatus = "已上电";
            });

            // 2. 连接FPGA
            AddLog("正在连接FPGA...");
            try
            {
                _fpga ??= new FpgaIoClient();
                if (!_fpga.IsConnected)
                    await _fpga.ConnectAsync(token);
                _fpgaConnected = true;
                AddLog("FPGA连接成功");

                // 3. 初始化HI8435 (cmd 0x04)
                AddLog("正在初始化HI8435...");
                await _fpga.InitHi8435Async(token);
                await Task.Delay(50, token);
                AddLog("HI8435初始化完成");
            }
            catch (Exception ex)
            {
                AddLog($"FPGA连接/初始化失败: {ex.Message}，将使用仿真模式");
                _fpgaConnected = false;
            }

            _hardwareInitialized = true;
            UpdateCommandStates();
        }

        /// <summary>
        /// 复位硬件
        /// </summary>
        private async Task ResetHardwareAsync(CancellationToken token)
        {
            AddLog("正在复位硬件...");

            // 断开FPGA
            if (_fpga != null)
            {
                try { _fpga.Disconnect(); } catch { }
                _fpga = null;
                _fpgaConnected = false;
            }

            // 下电
            if (_componentPowerStateApi != null && !_useSimulation)
            {
                try
                {
                    await _componentPowerStateApi.ApplyComponentDownStateAsync(token);
                    AddLog("组件已下电");
                }
                catch (Exception ex)
                {
                    AddLog($"下电异常: {ex.Message}");
                }
            }
            else
            {
                await _simulation.ApplyComponentDownStateAsync(AddLog, token);
            }

            Application.Current?.Dispatcher?.Invoke(() =>
            {
                IsPowerOn = false;
                PowerStatus = "未上电";
            });

            _hardwareInitialized = false;
            AddLog("硬件复位完成");
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
