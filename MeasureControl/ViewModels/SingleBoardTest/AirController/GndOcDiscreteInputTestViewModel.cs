using Prism.Commands;
using Prism.Ioc;
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
using MeasureControl.Simulations.S_C_8_3_1;

namespace MeasureControl.ViewModels.SingleBoardTest.AirController
{
    public class GndOcDiscreteInputTestViewModel : BindableBase
    {
        private const string TxChannel = "429_CH5";
        private const string RxChannel = "429_CH2";

        // 程控电源配置
        private const string PowerSupplyIpAddress = "192.168.1.15";
        private const double PowerSupplyVoltage = 28.0;
        private const double PowerSupplyCurrentLimit = 3.0;
        private const int TestCommandIntervalMs = 300;
        private const int TestCommandRetryCount = 2;
        private const int TestCommandRetryDelayMs = 300;

        // ATP指令 (label 61 62 63 64)
        private static readonly byte[] AtpEnterCommand = { 0x30, 0x01, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] AtpExitCommand = { 0x30, 0x02, 0x02, 0x01, 0x00, 0x00, 0x00, 0x00 };

        // 485继电器通道索引
        // 通道3 (索引2): 用于接地/接开测试的离散输入切换
        // 通道5 (索引4): 用于使能7131板卡的DO17-DO20，控制产品上电
        private const int Relay485GndOcChannelIndex = 2;
        private const int Relay485PowerChannelIndex = 4;

        // 7131 DO通道
        // DO9-DO12: 用于接地/接开测试的离散输入控制
        // DO17 (API索引): 用于产品28V上电 (界面显示DO18)
        private static readonly string[] DoChannelsGndOc = { "DO8", "DO9", "DO10", "DO11" };
        private const string DoPowerChannel = "DO17";

        private static readonly int[] TestPins = 
        {
            11, 12, 13, 14, 15, 16, 17, 18, 19, 20,
            73, 74, 76, 77, 78, 79, 80, 81, 82, 83,
            136, 137, 138, 142,
            198, 199, 200, 201, 204
        };

        // 针脚到DSI_GND通道的映射
        private static readonly Dictionary<int, int> PinToDsiChannel = new Dictionary<int, int>
        {
            // J11-J20 -> DSI_GND奇数 (1,3,5,7,9,13,15,17,19,21)
            { 11, 1 }, { 12, 3 }, { 13, 5 }, { 14, 7 }, { 15, 9 },
            { 16, 13 }, { 17, 15 }, { 18, 17 }, { 19, 19 }, { 20, 21 },
            // J73,J74,J76,J77,J78-J83 -> DSI_GND偶数 (0,2,6,8,12,14,16,18,20,22)
            { 73, 0 }, { 74, 2 }, { 76, 6 }, { 77, 8 },
            { 78, 12 }, { 79, 14 }, { 80, 16 }, { 81, 18 }, { 82, 20 }, { 83, 22 },
            // J136,J137,J138,J142 -> DSI_GND偶数 (24,26,28,36)
            { 136, 24 }, { 137, 26 }, { 138, 28 }, { 142, 36 },
            // J198,J199,J200,J201,J204 -> DSI_GND奇数 (23,25,27,29,35)
            { 198, 23 }, { 199, 25 }, { 200, 27 }, { 201, 29 }, { 204, 35 }
        };

        // DSI_GND通道到指令序号的映射 (去除4,10,11,30,31,32,33,34后按顺序编号)
        private static readonly Dictionary<int, byte> DsiChannelToSeqId = new Dictionary<int, byte>
        {
            { 0, 0x01 }, { 1, 0x02 }, { 2, 0x03 }, { 3, 0x04 },
            { 5, 0x05 }, { 6, 0x06 }, { 7, 0x07 }, { 8, 0x08 }, { 9, 0x09 },
            { 12, 0x0A }, { 13, 0x0B }, { 14, 0x0C }, { 15, 0x0D },
            { 16, 0x0E }, { 17, 0x0F }, { 18, 0x10 }, { 19, 0x11 },
            { 20, 0x12 }, { 21, 0x13 }, { 22, 0x14 }, { 23, 0x15 },
            { 24, 0x16 }, { 25, 0x17 }, { 26, 0x18 }, { 27, 0x19 },
            { 28, 0x1A }, { 29, 0x1B }, { 35, 0x1C }, { 36, 0x1D }
        };

        private readonly S_C_8_3_1Simulation _arinc = new S_C_8_3_1Simulation();
        private readonly SemaphoreSlim _opLock = new SemaphoreSlim(1, 1);
        private readonly object _testLock = new object();

        private readonly IPxiChassisService _pxiChassisService;
        private IJy7131Api _jy7131Api;
        private IPowerSupplyApi _powerSupply;

        private CancellationTokenSource _autoCts;
        private bool _isTestBusy;
        private bool _isAutoTestRunning;

        // 硬件状态标志
        private bool _isRelay485PowerOn;    // 485继电器第5路（使能DO17-DO20）
        private bool _isRelay485GndOcOn;    // 485继电器第3路（接地/接开切换）
        private bool _isDoPowerOn;          // DO18上电状态
        private bool _isDoGndOcOn;          // DO9-DO12接地/接开状态
        private string _lastTestTime = "--";
        private string _lastTestResult = "--";
        private int _selectedTabIndex;

        private readonly Dictionary<int, string> _groundPinTexts = new Dictionary<int, string>();
        private readonly Dictionary<int, string> _openPinTexts = new Dictionary<int, string>();

        public GndOcDiscreteInputTestViewModel()
        {
            _pxiChassisService = ContainerLocator.Container?.Resolve<IPxiChassisService>();

            AutoTestCommand = new DelegateCommand(OnAutoTest);
            ClearLogCommand = new DelegateCommand(() => Logs.Clear());

            foreach (var pin in TestPins)
            {
                _groundPinTexts[pin] = "---";
                _openPinTexts[pin] = "---";
            }
        }

        public string Title => "6.14.1控制通道GND/OC离散输入通道输入测试";

        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();

        public DelegateCommand AutoTestCommand { get; }
        public DelegateCommand ClearLogCommand { get; }

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

        public int SelectedTabIndex
        {
            get => _selectedTabIndex;
            set => SetProperty(ref _selectedTabIndex, value);
        }

        public string GroundJ11Text { get => _groundPinTexts[11]; set { _groundPinTexts[11] = value; RaisePropertyChanged(); } }
        public string GroundJ12Text { get => _groundPinTexts[12]; set { _groundPinTexts[12] = value; RaisePropertyChanged(); } }
        public string GroundJ13Text { get => _groundPinTexts[13]; set { _groundPinTexts[13] = value; RaisePropertyChanged(); } }
        public string GroundJ14Text { get => _groundPinTexts[14]; set { _groundPinTexts[14] = value; RaisePropertyChanged(); } }
        public string GroundJ15Text { get => _groundPinTexts[15]; set { _groundPinTexts[15] = value; RaisePropertyChanged(); } }
        public string GroundJ16Text { get => _groundPinTexts[16]; set { _groundPinTexts[16] = value; RaisePropertyChanged(); } }
        public string GroundJ17Text { get => _groundPinTexts[17]; set { _groundPinTexts[17] = value; RaisePropertyChanged(); } }
        public string GroundJ18Text { get => _groundPinTexts[18]; set { _groundPinTexts[18] = value; RaisePropertyChanged(); } }
        public string GroundJ19Text { get => _groundPinTexts[19]; set { _groundPinTexts[19] = value; RaisePropertyChanged(); } }
        public string GroundJ20Text { get => _groundPinTexts[20]; set { _groundPinTexts[20] = value; RaisePropertyChanged(); } }

        public string GroundJ73Text { get => _groundPinTexts[73]; set { _groundPinTexts[73] = value; RaisePropertyChanged(); } }
        public string GroundJ74Text { get => _groundPinTexts[74]; set { _groundPinTexts[74] = value; RaisePropertyChanged(); } }
        public string GroundJ76Text { get => _groundPinTexts[76]; set { _groundPinTexts[76] = value; RaisePropertyChanged(); } }
        public string GroundJ77Text { get => _groundPinTexts[77]; set { _groundPinTexts[77] = value; RaisePropertyChanged(); } }
        public string GroundJ78Text { get => _groundPinTexts[78]; set { _groundPinTexts[78] = value; RaisePropertyChanged(); } }
        public string GroundJ79Text { get => _groundPinTexts[79]; set { _groundPinTexts[79] = value; RaisePropertyChanged(); } }
        public string GroundJ80Text { get => _groundPinTexts[80]; set { _groundPinTexts[80] = value; RaisePropertyChanged(); } }
        public string GroundJ81Text { get => _groundPinTexts[81]; set { _groundPinTexts[81] = value; RaisePropertyChanged(); } }
        public string GroundJ82Text { get => _groundPinTexts[82]; set { _groundPinTexts[82] = value; RaisePropertyChanged(); } }
        public string GroundJ83Text { get => _groundPinTexts[83]; set { _groundPinTexts[83] = value; RaisePropertyChanged(); } }

        public string GroundJ136Text { get => _groundPinTexts[136]; set { _groundPinTexts[136] = value; RaisePropertyChanged(); } }
        public string GroundJ137Text { get => _groundPinTexts[137]; set { _groundPinTexts[137] = value; RaisePropertyChanged(); } }
        public string GroundJ138Text { get => _groundPinTexts[138]; set { _groundPinTexts[138] = value; RaisePropertyChanged(); } }
        public string GroundJ142Text { get => _groundPinTexts[142]; set { _groundPinTexts[142] = value; RaisePropertyChanged(); } }

        public string GroundJ198Text { get => _groundPinTexts[198]; set { _groundPinTexts[198] = value; RaisePropertyChanged(); } }
        public string GroundJ199Text { get => _groundPinTexts[199]; set { _groundPinTexts[199] = value; RaisePropertyChanged(); } }
        public string GroundJ200Text { get => _groundPinTexts[200]; set { _groundPinTexts[200] = value; RaisePropertyChanged(); } }
        public string GroundJ201Text { get => _groundPinTexts[201]; set { _groundPinTexts[201] = value; RaisePropertyChanged(); } }
        public string GroundJ204Text { get => _groundPinTexts[204]; set { _groundPinTexts[204] = value; RaisePropertyChanged(); } }

        public string OpenJ11Text { get => _openPinTexts[11]; set { _openPinTexts[11] = value; RaisePropertyChanged(); } }
        public string OpenJ12Text { get => _openPinTexts[12]; set { _openPinTexts[12] = value; RaisePropertyChanged(); } }
        public string OpenJ13Text { get => _openPinTexts[13]; set { _openPinTexts[13] = value; RaisePropertyChanged(); } }
        public string OpenJ14Text { get => _openPinTexts[14]; set { _openPinTexts[14] = value; RaisePropertyChanged(); } }
        public string OpenJ15Text { get => _openPinTexts[15]; set { _openPinTexts[15] = value; RaisePropertyChanged(); } }
        public string OpenJ16Text { get => _openPinTexts[16]; set { _openPinTexts[16] = value; RaisePropertyChanged(); } }
        public string OpenJ17Text { get => _openPinTexts[17]; set { _openPinTexts[17] = value; RaisePropertyChanged(); } }
        public string OpenJ18Text { get => _openPinTexts[18]; set { _openPinTexts[18] = value; RaisePropertyChanged(); } }
        public string OpenJ19Text { get => _openPinTexts[19]; set { _openPinTexts[19] = value; RaisePropertyChanged(); } }
        public string OpenJ20Text { get => _openPinTexts[20]; set { _openPinTexts[20] = value; RaisePropertyChanged(); } }

        public string OpenJ73Text { get => _openPinTexts[73]; set { _openPinTexts[73] = value; RaisePropertyChanged(); } }
        public string OpenJ74Text { get => _openPinTexts[74]; set { _openPinTexts[74] = value; RaisePropertyChanged(); } }
        public string OpenJ76Text { get => _openPinTexts[76]; set { _openPinTexts[76] = value; RaisePropertyChanged(); } }
        public string OpenJ77Text { get => _openPinTexts[77]; set { _openPinTexts[77] = value; RaisePropertyChanged(); } }
        public string OpenJ78Text { get => _openPinTexts[78]; set { _openPinTexts[78] = value; RaisePropertyChanged(); } }
        public string OpenJ79Text { get => _openPinTexts[79]; set { _openPinTexts[79] = value; RaisePropertyChanged(); } }
        public string OpenJ80Text { get => _openPinTexts[80]; set { _openPinTexts[80] = value; RaisePropertyChanged(); } }
        public string OpenJ81Text { get => _openPinTexts[81]; set { _openPinTexts[81] = value; RaisePropertyChanged(); } }
        public string OpenJ82Text { get => _openPinTexts[82]; set { _openPinTexts[82] = value; RaisePropertyChanged(); } }
        public string OpenJ83Text { get => _openPinTexts[83]; set { _openPinTexts[83] = value; RaisePropertyChanged(); } }

        public string OpenJ136Text { get => _openPinTexts[136]; set { _openPinTexts[136] = value; RaisePropertyChanged(); } }
        public string OpenJ137Text { get => _openPinTexts[137]; set { _openPinTexts[137] = value; RaisePropertyChanged(); } }
        public string OpenJ138Text { get => _openPinTexts[138]; set { _openPinTexts[138] = value; RaisePropertyChanged(); } }
        public string OpenJ142Text { get => _openPinTexts[142]; set { _openPinTexts[142] = value; RaisePropertyChanged(); } }

        public string OpenJ198Text { get => _openPinTexts[198]; set { _openPinTexts[198] = value; RaisePropertyChanged(); } }
        public string OpenJ199Text { get => _openPinTexts[199]; set { _openPinTexts[199] = value; RaisePropertyChanged(); } }
        public string OpenJ200Text { get => _openPinTexts[200]; set { _openPinTexts[200] = value; RaisePropertyChanged(); } }
        public string OpenJ201Text { get => _openPinTexts[201]; set { _openPinTexts[201] = value; RaisePropertyChanged(); } }
        public string OpenJ204Text { get => _openPinTexts[204]; set { _openPinTexts[204] = value; RaisePropertyChanged(); } }

        private void OnAutoTest()
        {
            lock (_testLock)
            {
                if (_isTestBusy)
                {
                    if (IsAutoTestRunning)
                    {
                        _autoCts?.Cancel();
                    }
                    return;
                }
                _isTestBusy = true;
            }

            _ = RunAutoTestAsync();
        }

        private async Task RunAutoTestAsync()
        {
            await _opLock.WaitAsync();
            try
            {
                IsAutoTestRunning = true;
                LastTestTime = "--";
                LastTestResult = "--";
                ResetAllPinTexts();

                _autoCts?.Cancel();
                _autoCts?.Dispose();
                _autoCts = new CancellationTokenSource();
                var token = _autoCts.Token;

                AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试开始");

                // 步骤1: 初始化硬件设备（不包含上电逻辑）
                AddLog($"[{DateTime.Now:HH:mm:ss}] 步骤1: 初始化硬件设备...");
                await InitializeHardwareAsync(token);

                // 步骤2: 发送ATP指令进入测试模式
                AddLog($"[{DateTime.Now:HH:mm:ss}] 步骤2: 发送ATP指令进入测试模式...");
                await SendEnterAtpAsync(token);

                // 步骤3: 接地测试
                SelectedTabIndex = 0;
                AddLog($"[{DateTime.Now:HH:mm:ss}] 步骤3: 开始接地测试...");
                await SetGndStateAsync(true, token);
                await Task.Delay(200, token);

                var gndFailures = await TestAllPinsAsync(true, token);

                // 步骤4: 接开测试
                SelectedTabIndex = 1;
                AddLog($"[{DateTime.Now:HH:mm:ss}] 步骤4: 开始接开测试...");
                await SetGndStateAsync(false, token);
                await Task.Delay(200, token);

                var ocFailures = await TestAllPinsAsync(false, token);

                // 步骤5: 退出ATP模式
                AddLog($"[{DateTime.Now:HH:mm:ss}] 步骤5: 退出ATP模式...");
                await SendExitAtpAsync(token);

                // 汇总结果
                var allFailures = gndFailures.Concat(ocFailures).ToList();
                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                LastTestResult = allFailures.Count == 0 ? "PASS" : "FAIL";
                AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试完成：{LastTestResult}");

                if (allFailures.Count > 0)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 失败项({allFailures.Count}):");
                    foreach (var f in allFailures)
                        AddLog($"  - {f}");
                }
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
                LastTestResult = "FAIL";
                AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试异常：{ex.Message}");
            }
            finally
            {
                await CleanupHardwareAsync();
                IsAutoTestRunning = false;
                lock (_testLock)
                {
                    _isTestBusy = false;
                }
                _opLock.Release();
            }
        }

        private async Task InitializeHardwareAsync(CancellationToken token)
        {
            // 1. 初始化程控电源
            await EnsurePowerSupplyConnectedAsync(token);

            // 2. 初始化7131板卡
            await EnsureJy7131ReadyAsync(token);

            // 3. 产品上电：先开485继电器第5路使能DO17-DO20，再开DO18给产品供电
            await PowerOnProductAsync(token);

            // 4. 初始化ARINC429
            try { await _arinc.StopAsync(msg => { }); } catch { }
            await Task.Delay(100, token);

            _arinc.IsRealProduct = true;
            _arinc.ArincRate = 100000.0;
            await _arinc.StartAsync(TxChannel, RxChannel, msg => AddLog(msg));
            AddLog($"[{DateTime.Now:HH:mm:ss}] ARINC429初始化完成 (TX:{TxChannel}, RX:{RxChannel})");

            // 清理接收缓存
            for (int i = 0; i < 3; i++)
            {
                try { await _arinc.ClearRxFifoAsync(RxChannel); } catch { }
                await Task.Delay(50, token);
            }
        }

        private async Task EnsurePowerSupplyConnectedAsync(CancellationToken token)
        {
            _powerSupply ??= new PowerSupplySocketApi();
            if (!_powerSupply.IsConnected)
            {
                await _powerSupply.ConnectAsync(PowerSupplyIpAddress, token);
                AddLog($"[{DateTime.Now:HH:mm:ss}] 程控电源已连接 ({PowerSupplyIpAddress})");
            }
        }

        private async Task EnsureJy7131ReadyAsync(CancellationToken token)
        {
            try
            {
                if (_jy7131Api == null)
                {
                    var device7131 = FindFirstJy7131Device();
                    if (device7131 != null)
                    {
                        var devSlot = Infer7131SlotNumber(device7131);
                        AddLog($"[{DateTime.Now:HH:mm:ss}] 找到7131板卡: {device7131.Model ?? device7131.Name}，槽位={devSlot}");
                        if (int.TryParse(devSlot, out var slotNum))
                            _jy7131Api = new Jy7131Api(device7131, slotNum);
                        else
                            _jy7131Api = new Jy7131Api(device7131);
                    }
                    else
                    {
                        throw new InvalidOperationException("未找到7131板卡");
                    }
                }

                if (!_jy7131Api.IsConnected)
                {
                    await _jy7131Api.ConnectAsync(token);
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 7131板卡连接成功");
                }

                if (!_jy7131Api.IsRunning)
                {
                    await _jy7131Api.SetOutputModeAsync(Jy7131OutputMode.Sinking, token);
                    await _jy7131Api.StartAsync(token);
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 7131板卡已启动");
                }
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 7131板卡初始化失败: {ex.Message}");
                throw;
            }
        }

        private async Task PowerOnProductAsync(CancellationToken token)
        {
            // 1. 程控电源设置28V输出
            await _powerSupply.SetVoltageAsync(PowerSupplyChannel.CH1, PowerSupplyVoltage, token);
            await _powerSupply.SetCurrentAsync(PowerSupplyChannel.CH1, PowerSupplyCurrentLimit, token);
            await _powerSupply.SetOutputEnabledAsync(PowerSupplyChannel.CH1, true, token);
            AddLog($"[{DateTime.Now:HH:mm:ss}] 程控电源输出 {PowerSupplyVoltage:0.#}V / {PowerSupplyCurrentLimit:0.#}A");

            // 2. 打开485继电器第5路（索引4），使能DO17-DO20
            if (!_isRelay485PowerOn)
            {
                await _jy7131Api.SetRelayAsync(Relay485PowerChannelIndex, true, token);
                _isRelay485PowerOn = true;
                AddLog($"[{DateTime.Now:HH:mm:ss}] 485继电器第{Relay485PowerChannelIndex + 1}路已开启（使能DO17-DO20）");
                await Task.Delay(100, token);
            }

            // 3. 开启DO18（API索引DO17）给产品上电
            if (!_isDoPowerOn)
            {
                await _jy7131Api.WriteDoAsync(DoPowerChannel, true, token);
                _isDoPowerOn = true;
                AddLog($"[{DateTime.Now:HH:mm:ss}] 7131板卡{DoPowerChannel}已开启，产品28V上电");
                await Task.Delay(500, token);  // 等待产品稳定
            }
        }

        private async Task PowerOffProductAsync(CancellationToken token)
        {
            // 1. 关闭DO18（API索引DO17）
            if (_isDoPowerOn)
            {
                try
                {
                    await _jy7131Api.WriteDoAsync(DoPowerChannel, false, token);
                    _isDoPowerOn = false;
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 7131板卡{DoPowerChannel}已关闭，产品下电");
                }
                catch (Exception ex)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 关闭{DoPowerChannel}失败: {ex.Message}");
                }
            }

            // 2. 关闭485继电器第5路
            if (_isRelay485PowerOn)
            {
                try
                {
                    await _jy7131Api.SetRelayAsync(Relay485PowerChannelIndex, false, token);
                    _isRelay485PowerOn = false;
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 485继电器第{Relay485PowerChannelIndex + 1}路已关闭");
                }
                catch (Exception ex)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 关闭485继电器第{Relay485PowerChannelIndex + 1}路失败: {ex.Message}");
                }
            }

            // 3. 关闭程控电源输出
            if (_powerSupply?.IsConnected == true)
            {
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
        }

        private async Task SetGndStateAsync(bool isGnd, CancellationToken token)
        {
            try
            {
                if (_jy7131Api == null || !_jy7131Api.IsConnected)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 7131板卡不可用，跳过继电器控制");
                    return;
                }

                if (isGnd)
                {
                    // 接地：打开485继电器通道3，打开DO9-DO12
                    if (!_isRelay485GndOcOn)
                    {
                        AddLog($"[{DateTime.Now:HH:mm:ss}] 打开485继电器通道{Relay485GndOcChannelIndex + 1}...");
                        await _jy7131Api.SetRelayAsync(Relay485GndOcChannelIndex, true, token);
                        _isRelay485GndOcOn = true;
                        await Task.Delay(100, token);
                    }

                    foreach (var doChannel in DoChannelsGndOc)
                    {
                        AddLog($"[{DateTime.Now:HH:mm:ss}] 打开{doChannel}...");
                        await _jy7131Api.WriteDoAsync(doChannel, true, token);
                        await Task.Delay(50, token);
                    }
                    _isDoGndOcOn = true;
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 已将所有地/开型离散输入接\"地\"信号");
                }
                else
                {
                    // 接开：关闭DO9-DO12，关闭485继电器通道3
                    foreach (var doChannel in DoChannelsGndOc)
                    {
                        AddLog($"[{DateTime.Now:HH:mm:ss}] 关闭{doChannel}...");
                        await _jy7131Api.WriteDoAsync(doChannel, false, token);
                        await Task.Delay(50, token);
                    }
                    _isDoGndOcOn = false;

                    if (_isRelay485GndOcOn)
                    {
                        AddLog($"[{DateTime.Now:HH:mm:ss}] 关闭485继电器通道{Relay485GndOcChannelIndex + 1}...");
                        await _jy7131Api.SetRelayAsync(Relay485GndOcChannelIndex, false, token);
                        _isRelay485GndOcOn = false;
                        await Task.Delay(100, token);
                    }
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 已将所有地/开型离散输入接\"开\"信号");
                }
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 设置{(isGnd ? "接地" : "接开")}状态失败: {ex.Message}");
            }
        }

        private async Task<List<string>> TestAllPinsAsync(bool isGroundTest, CancellationToken token)
        {
            var failures = new List<string>();
            string expectedValue = isGroundTest ? "1" : "0";
            string testType = isGroundTest ? "接地" : "接开";

            foreach (var pin in TestPins)
            {
                token.ThrowIfCancellationRequested();
                var result = await TestSinglePinAsync(pin, isGroundTest, token);
                SetPinText(pin, isGroundTest, result);

                bool pass = result == expectedValue;
                if (!pass)
                    failures.Add($"J{pin}{testType}: {result}");

                AddLog($"[{DateTime.Now:HH:mm:ss}] J{pin} {testType}测试: {result} -> {(pass ? "PASS" : "FAIL")}");
                await Task.Delay(TestCommandIntervalMs, token);
            }

            return failures;
        }

        private async Task<string> TestSinglePinAsync(int pin, bool isGroundTest, CancellationToken token)
        {
            try
            {
                if (!PinToDsiChannel.TryGetValue(pin, out int dsiChannel))
                    return "ERR";
                if (!DsiChannelToSeqId.TryGetValue(dsiChannel, out byte seqId))
                    return "ERR";

                // 构造发送指令: 08 01 XX 01 00 00 00 00
                var sendCmd = new byte[8] { 0x08, 0x01, seqId, 0x01, 0x00, 0x00, 0x00, 0x00 };

                string lastResult = "超时";
                for (int attempt = 1; attempt <= TestCommandRetryCount + 1; attempt++)
                {
                    try { await _arinc.ClearRxFifoAsync(RxChannel); } catch { }
                    await Task.Delay(50, token);

                    AddLog($"[{DateTime.Now:HH:mm:ss}] J{pin} 发送测试指令，第{attempt}次：{FormatBytesHex(sendCmd)}");
                    await _arinc.SendBenchCommandOnlyAsync(TxChannel, sendCmd, msg => { }, token);

                    var resp = await _arinc.WaitBenchResponse8Async(
                        RxChannel,
                        b => b != null && b.Length == 8 && b[0] == 0x08 && b[1] == 0x01 && b[2] == seqId && b[3] == 0x02,
                        timeoutMs: 3000,
                        msg => { },
                        token);

                    if (resp == null)
                    {
                        lastResult = "超时";
                    }
                    else
                    {
                        ushort data = (ushort)((resp[6] << 8) | resp[7]);
                        if (isGroundTest)
                        {
                            lastResult = data == 0xAAAA ? "1" : $"0x{data:X4}";
                        }
                        else
                        {
                            lastResult = data == 0x5555 ? "0" : $"0x{data:X4}";
                        }
                    }

                    if ((isGroundTest && lastResult == "1") || (!isGroundTest && lastResult == "0"))
                        return lastResult;

                    if (attempt <= TestCommandRetryCount)
                    {
                        AddLog($"[{DateTime.Now:HH:mm:ss}] J{pin} 第{attempt}次测试结果={lastResult}，{TestCommandRetryDelayMs}ms后重试");
                        await Task.Delay(TestCommandRetryDelayMs, token);
                    }
                }

                return lastResult;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 测试J{pin}异常: {ex.Message}");
                return "ERR";
            }
        }

        private async Task SendEnterAtpAsync(CancellationToken token)
        {
            try { await _arinc.ClearRxFifoAsync(RxChannel); } catch { }
            await Task.Delay(20, token);

            AddLog($"[{DateTime.Now:HH:mm:ss}] 发送进入ATP：{FormatBytesHex(AtpEnterCommand)}");
            await _arinc.SendBenchCommandOnlyAsync(TxChannel, AtpEnterCommand, msg => AddLog(msg), token);
            await Task.Delay(300, token);
            AddLog($"[{DateTime.Now:HH:mm:ss}] ATP指令已发送");
        }

        private async Task SendExitAtpAsync(CancellationToken token)
        {
            try { await _arinc.ClearRxFifoAsync(RxChannel); } catch { }
            await Task.Delay(20, token);

            AddLog($"[{DateTime.Now:HH:mm:ss}] 发送退出ATP：{FormatBytesHex(AtpExitCommand)}");
            await _arinc.SendBenchCommandOnlyAsync(TxChannel, AtpExitCommand, msg => AddLog(msg), token);
            await Task.Delay(100, token);
            AddLog($"[{DateTime.Now:HH:mm:ss}] 退出ATP完成");
        }

        private async Task CleanupHardwareAsync()
        {
            AddLog($"[{DateTime.Now:HH:mm:ss}] 开始清理硬件资源...");

            // 1. 发送退出ATP指令
            try
            {
                await _arinc.SendBenchCommandOnlyAsync(TxChannel, AtpExitCommand, msg => { }, CancellationToken.None);
                await Task.Delay(100);
                AddLog($"[{DateTime.Now:HH:mm:ss}] 已发送退出ATP指令");
            }
            catch { }

            // 2. 关闭接地/接开测试用的DO通道和485继电器
            try
            {
                if (_jy7131Api != null && _jy7131Api.IsConnected)
                {
                    foreach (var doChannel in DoChannelsGndOc)
                    {
                        try { await _jy7131Api.WriteDoAsync(doChannel, false, CancellationToken.None); } catch { }
                    }
                    _isDoGndOcOn = false;
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 接地/接开测试DO通道已关闭");

                    try { await _jy7131Api.SetRelayAsync(Relay485GndOcChannelIndex, false, CancellationToken.None); } catch { }
                    _isRelay485GndOcOn = false;
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 485继电器通道{Relay485GndOcChannelIndex + 1}已关闭");
                }
            }
            catch { }

            // 3. 清空429接收缓冲区并关闭
            try { await _arinc.ClearRxFifoAsync(RxChannel); } catch { }
            try { await _arinc.ClearRxFifoAsync(TxChannel); } catch { }
            try
            {
                await _arinc.StopAsync(msg => AddLog(msg));
                AddLog($"[{DateTime.Now:HH:mm:ss}] 429板卡已关闭");
            }
            catch { }

            // 4. 产品下电（关闭DO18、485继电器第5路、程控电源）
            try
            {
                await PowerOffProductAsync(CancellationToken.None);
            }
            catch { }

            // 5. 停止并断开7131板卡，释放给其他测试项使用
            try
            {
                if (_jy7131Api != null && _jy7131Api.IsConnected)
                {
                    if (_jy7131Api.IsRunning)
                    {
                        await _jy7131Api.StopAsync(CancellationToken.None).ConfigureAwait(false);
                        AddLog($"[{DateTime.Now:HH:mm:ss}] 7131板卡已停止");
                    }

                    await _jy7131Api.DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 7131板卡已断开");
                }
            }
            catch { }
            finally
            {
                _jy7131Api = null;
                _isRelay485PowerOn = false;
                _isRelay485GndOcOn = false;
                _isDoPowerOn = false;
                _isDoGndOcOn = false;
            }

            // 6. 断开程控电源连接
            try
            {
                if (_powerSupply?.IsConnected == true)
                {
                    await _powerSupply.DisconnectAsync(CancellationToken.None);
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 程控电源已断开");
                }
            }
            catch { }

            await Task.Delay(200);
            AddLog($"[{DateTime.Now:HH:mm:ss}] 硬件资源清理完成");
        }

        private void ResetAllPinTexts()
        {
            foreach (var pin in TestPins)
            {
                SetPinText(pin, true, "---");
                SetPinText(pin, false, "---");
            }
        }

        private void SetPinText(int pin, bool isGround, string value)
        {
            var propName = isGround ? $"GroundJ{pin}Text" : $"OpenJ{pin}Text";
            var prop = GetType().GetProperty(propName);
            prop?.SetValue(this, value);
        }

        private DeviceBase FindFirstJy7131Device()
        {
            var chassisList = _pxiChassisService?.GetAllChassis();
            if (chassisList == null)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] [7131查找] 机箱列表为null");
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
            if (device is PxiDeviceBase pxi && pxi.SlotIndex > 0)
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

        private static string FormatBytesHex(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                return string.Empty;
            return string.Join(" ", bytes.Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));
        }

        private void AddLog(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            try
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
            catch
            {
            }
        }
    }
}
