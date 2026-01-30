using Prism.Commands;
using Prism.Mvvm;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using MeasureControl.Drivers;
using MeasureControl.Drivers.ArtSwitch;
using MeasureControl.Drivers.PXI4004CAN;
using MeasureControl.Models.Devices;
using MeasureControl.Models.Devices.DeviceCategories;
using MeasureControl.Services;
using NationalInstruments.Visa;
using Prism.Ioc;

namespace MeasureControl.ViewModels.SingleBoardTest.AirController
{
    public class PT500TemperatureSensorCanViewModel : BindableBase
    {
        private const string StepEnterAtp = "进入ATP模式";
        private const string StepResistor = "接入电阻";
        private const string StepControllerTemp = "控制器温度测试";
        private const string StepTelemetry = "温度回采值";
        private const string StepExitAtp = "退出ATP模式";

        private const uint DefaultCanFrameId = 0;
        private const int DmmDefaultPort = 5555;
        private const string DmmDefaultIp = "192.168.1.13";
        private const string MatrixIpAddress = "192.168.1.3";
        private const int MatrixFixedSlotIndex = 4;
        private const int MatrixDmmSlotIndex = 7;

        private const int SimControllerRxChannel = 2;
        private const int SimControllerTxChannel = 3;

        private static readonly byte[] AtpR = { 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] AtpEnterOk = { 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x02 };
        private static readonly byte[] AtpFault = { 0x00, 0x01, 0x00, 0x01, 0x11, 0x11, 0x11, 0x11 };
        private static readonly byte[] AtpE = { 0x00, 0x02, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01 };
        private static readonly byte[] ExitOk = { 0x00, 0x02, 0x00, 0x01, 0x00, 0x00, 0x00, 0x03 };
        private static readonly byte[] ExitFault = { 0x00, 0x02, 0x00, 0x01, 0x11, 0x11, 0x11, 0x11 };
        private static readonly byte[] AbPdtsTemperature = { 0x07, 0x01, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] TelemetryTemperaturePrefix = { 0x07, 0x01, 0x01, 0x02 };
        private static readonly byte[] TelemetryRawPrefix = { 0x07, 0x01, 0x01, 0x03 };

        private PXI4004Driver _canDriver;
        private ACTS6010Driver _resistorDriver;
        private ResourceManager _dmmResourceManager;
        private MessageBasedSession _dmmSession;
        private readonly SemaphoreSlim _dmmIoLock = new SemaphoreSlim(1, 1);

        private readonly SemaphoreSlim _matrixSwitchLock = new SemaphoreSlim(1, 1);
        private bool _matrixConnected;

        private readonly SemaphoreSlim _canOpLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _manualTestLock = new SemaphoreSlim(1, 1);

        public PT500TemperatureSensorCanViewModel()
        {
            _enterAtpTxChannel = "CH0";
            _enterAtpRxChannel = "CH1";
            _controllerTemperatureTestTxChannel = "CH0";
            _controllerTemperatureTestRxChannel = "CH1";
            _temperatureTelemetryRxChannel = "CH1";
            _exitAtpTxChannel = "CH0";
            _exitAtpRxChannel = "CH1";

            _resistorGear = "1挡";
            ResistorGearValueText = _resistorGear;
            MeasuredResistanceValueText = "--";
            TemperatureTelemetryValueText = "--";
            LastTestTime = "--";
            LastTestResult = "--";

            SendEnterAtpCommand = new DelegateCommand(async () => await OnSendEnterAtpAsync());
            SendSetControllerResistorCommand = new DelegateCommand(async () => await SendSetControllerResistorAsync(), () => !IsResistorMeasuring)
                .ObservesProperty(() => IsResistorMeasuring);
            TestControllerTemperatureCommand = new DelegateCommand(async () => await OnTestControllerTemperatureAsync());
            TestTemperatureTelemetryCommand = new DelegateCommand(async () => await OnTestTemperatureTelemetryAsync());
            SendExitAtpCommand = new DelegateCommand(async () => await OnSendExitAtpAsync());
            ClearLogCommand = new DelegateCommand(() => Logs.Clear());

            ManualTestCommand = new DelegateCommand(OnManualTest);
            AutoTestCommand = new DelegateCommand(OnAutoTest);
        }

        private string _enterAtpTxChannel;
        private string _enterAtpRxChannel;
        private string _controllerTemperatureTestTxChannel;
        private string _controllerTemperatureTestRxChannel;
        private string _temperatureTelemetryRxChannel;
        private string _exitAtpTxChannel;
        private string _exitAtpRxChannel;

        private string _resistorGear;
        private string _resistorGearValueText;
        private string _measuredResistanceValueText;
        private string _temperatureTelemetryValueText;
        private string _lastTestTime;
        private string _lastTestResult;

        private bool _isManualTestRunning;
        private bool _isAutoTestRunning;
        private bool _isResistorMeasuring;

        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();

        public DelegateCommand ManualTestCommand { get; }
        public DelegateCommand AutoTestCommand { get; }

        public DelegateCommand SendEnterAtpCommand { get; }
        public DelegateCommand SendSetControllerResistorCommand { get; }
        public DelegateCommand TestControllerTemperatureCommand { get; }
        public DelegateCommand TestTemperatureTelemetryCommand { get; }
        public DelegateCommand SendExitAtpCommand { get; }
        public DelegateCommand ClearLogCommand { get; }

        public bool IsManualTestRunning
        {
            get => _isManualTestRunning;
            set
            {
                if (SetProperty(ref _isManualTestRunning, value))
                {
                    if (value)
                    {
                        IsAutoTestRunning = false;
                    }
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
                    if (value)
                    {
                        IsManualTestRunning = false;
                    }
                }
            }
        }

        public string EnterAtpTxChannel
        {
            get => _enterAtpTxChannel;
            set => SetProperty(ref _enterAtpTxChannel, value);
        }

        public string EnterAtpRxChannel
        {
            get => _enterAtpRxChannel;
            set => SetProperty(ref _enterAtpRxChannel, value);
        }

        public string ResistorGear
        {
            get => _resistorGear;
            set
            {
                if (SetProperty(ref _resistorGear, value))
                {
                    ResistorGearValueText = _resistorGear;
                }
            }
        }

        public string ResistorGearValueText
        {
            get => _resistorGearValueText;
            set => SetProperty(ref _resistorGearValueText, value);
        }

        public string MeasuredResistanceValueText
        {
            get => _measuredResistanceValueText;
            private set => SetProperty(ref _measuredResistanceValueText, value);
        }

        public bool IsResistorMeasuring
        {
            get => _isResistorMeasuring;
            private set => SetProperty(ref _isResistorMeasuring, value);
        }

        public string ControllerTemperatureTestTxChannel
        {
            get => _controllerTemperatureTestTxChannel;
            set => SetProperty(ref _controllerTemperatureTestTxChannel, value);
        }

        public string ControllerTemperatureTestRxChannel
        {
            get => _controllerTemperatureTestRxChannel;
            set => SetProperty(ref _controllerTemperatureTestRxChannel, value);
        }

        public string TemperatureTelemetryRxChannel
        {
            get => _temperatureTelemetryRxChannel;
            set => SetProperty(ref _temperatureTelemetryRxChannel, value);
        }

        public string ExitAtpTxChannel
        {
            get => _exitAtpTxChannel;
            set => SetProperty(ref _exitAtpTxChannel, value);
        }

        public string ExitAtpRxChannel
        {
            get => _exitAtpRxChannel;
            set => SetProperty(ref _exitAtpRxChannel, value);
        }

        public string TemperatureTelemetryValueText
        {
            get => _temperatureTelemetryValueText;
            set => SetProperty(ref _temperatureTelemetryValueText, value);
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
            AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试按钮点击");
            if (IsManualTestRunning)
            {
                _ = StopManualTestAsync();
                return;
            }

            _ = StartManualTestAsync();
        }

        private void OnAutoTest()
        {
            if (IsAutoTestRunning)
            {
                IsAutoTestRunning = false;
                AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试停止");
                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                return;
            }

            IsAutoTestRunning = true;
            AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试启动");
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
                AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试启动：开始打开设备");

                var ok = await EnsureAllDevicesReadyAsync();
                if (!ok)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试启动失败：设备未准备就绪");
                    IsManualTestRunning = false;
                    LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    return;
                }

                AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试启动：设备已就绪");
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

                await DisconnectCanAsync();
                await DisconnectDmmAsync();
                await DisconnectMatrixAsync();
                await DisconnectResistorAsync();

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

        private async Task<bool> EnsureAllDevicesReadyAsync()
        {
            var canOk = false;
            var dmmOk = false;
            var matrixOk = false;
            var resistorOk = false;

            try { canOk = await EnsureCanDriverReadyAsync(); } catch (Exception ex) { AddLog($"[{DateTime.Now:HH:mm:ss}] CAN打开异常：{ex.Message}"); }
            try { dmmOk = await EnsureDmmReadyAsync(); } catch (Exception ex) { AddLog($"[{DateTime.Now:HH:mm:ss}] 万用表打开异常：{ex.Message}"); }
            try { matrixOk = await EnsureMatrixReadyAsync(); } catch (Exception ex) { AddLog($"[{DateTime.Now:HH:mm:ss}] 矩阵开关打开异常：{ex.Message}"); }
            try { resistorOk = await EnsureResistorReadyAsync(); } catch (Exception ex) { AddLog($"[{DateTime.Now:HH:mm:ss}] 电阻板卡打开异常：{ex.Message}"); }

            AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试设备就绪结果：CAN={canOk}, DMM={dmmOk}, 矩阵={matrixOk}, 电阻={resistorOk}");

            return canOk || dmmOk || matrixOk || resistorOk;
        }

        private async Task DisconnectCanAsync()
        {
            try
            {
                if (_canDriver != null)
                {
                    await _canDriver.DisconnectAsync();
                    _canDriver = null;
                }
            }
            catch
            {
            }
        }

        private async Task<bool> EnsureDmmReadyAsync()
        {
            if (_dmmSession != null)
                return true;

            try
            {
                _dmmResourceManager ??= new ResourceManager();
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 万用表打开失败：ResourceManager创建失败：{ex.Message}");
                return false;
            }

            try
            {
                var ip = DmmDefaultIp;
                if (!IPAddress.TryParse(ip, out _))
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 万用表打开失败：IP无效({ip})");
                    return false;
                }

                try
                {
                    var resourceString = $"TCPIP0::{ip}::{DmmDefaultPort}::SOCKET";
                    _dmmSession = (MessageBasedSession)_dmmResourceManager.Open(resourceString, 0, 5000);
                }
                catch
                {
                    var resourceString = $"TCPIP0::{ip}::inst0::INSTR";
                    _dmmSession = (MessageBasedSession)_dmmResourceManager.Open(resourceString, 0, 5000);
                }

                try
                {
                    _dmmSession.TimeoutMilliseconds = 8000;
                    _dmmSession.TerminationCharacterEnabled = true;
                    _dmmSession.TerminationCharacter = (byte)'\n';
                }
                catch
                {
                }

                var idn = await QueryDmmStringAsync("*IDN?");
                AddLog($"[{DateTime.Now:HH:mm:ss}] 万用表已打开：{(idn ?? string.Empty).Trim()}");
                return true;
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 万用表打开失败：{ex.Message}");
                await DisconnectDmmAsync();
                return false;
            }
        }

        private async Task<string> QueryDmmStringAsync(string query)
        {
            if (_dmmSession == null)
                throw new InvalidOperationException("DMM会话未建立");

            await _dmmIoLock.WaitAsync();
            try
            {
                var cmd = query.EndsWith("\n", StringComparison.Ordinal) ? query : query + "\n";
                _dmmSession.RawIO.Write(cmd);
                return _dmmSession.RawIO.ReadString();
            }
            finally
            {
                _dmmIoLock.Release();
            }
        }

        private Task DisconnectDmmAsync()
        {
            try
            {
                if (_dmmSession != null)
                {
                    _dmmSession.Dispose();
                    _dmmSession = null;
                }
            }
            catch
            {
            }

            return Task.CompletedTask;
        }

        private async Task<bool> EnsureMatrixReadyAsync()
        {
            if (_matrixConnected)
                return true;

            await _matrixSwitchLock.WaitAsync();
            try
            {
                if (_matrixConnected)
                    return true;

                var ok1 = await MatrixControlService.Instance.ConnectNodesAsync("I4", "O6", MatrixFixedSlotIndex, MatrixIpAddress);
                AddLog($"[{DateTime.Now:HH:mm:ss}] 矩阵开关通路(固定): I4->O6 slot={MatrixFixedSlotIndex} ip={MatrixIpAddress}, ok={ok1}");

                var ok2 = await MatrixControlService.Instance.ConnectNodesAsync("I3", "O30", MatrixDmmSlotIndex, MatrixIpAddress);
                AddLog($"[{DateTime.Now:HH:mm:ss}] 矩阵开关通路: I3->O30 slot={MatrixDmmSlotIndex} ip={MatrixIpAddress}, ok={ok2}");

                _matrixConnected = ok1 || ok2;
                return _matrixConnected;
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 矩阵开关打开异常：{ex.Message}");
                _matrixConnected = false;
                return false;
            }
            finally
            {
                _matrixSwitchLock.Release();
            }
        }

        private async Task DisconnectMatrixAsync()
        {
            await _matrixSwitchLock.WaitAsync();
            try
            {
                var ok1 = await MatrixControlService.Instance.DisconnectNodesAsync("I4", "O6", MatrixFixedSlotIndex, MatrixIpAddress);
                AddLog($"[{DateTime.Now:HH:mm:ss}] 矩阵开关断开(固定): I4->O6 slot={MatrixFixedSlotIndex} ip={MatrixIpAddress}, ok={ok1}");

                var ok2 = await MatrixControlService.Instance.DisconnectNodesAsync("I3", "O30", MatrixDmmSlotIndex, MatrixIpAddress);
                AddLog($"[{DateTime.Now:HH:mm:ss}] 矩阵开关断开: I3->O30 slot={MatrixDmmSlotIndex} ip={MatrixIpAddress}, ok={ok2}");

                var ok3 = await MatrixControlService.Instance.DisconnectNodesAsync("I3", "O31", MatrixDmmSlotIndex, MatrixIpAddress);
                AddLog($"[{DateTime.Now:HH:mm:ss}] 矩阵开关断开: I3->O31 slot={MatrixDmmSlotIndex} ip={MatrixIpAddress}, ok={ok3}");

                _matrixConnected = false;
            }
            catch
            {
                _matrixConnected = false;
            }
            finally
            {
                _matrixSwitchLock.Release();
            }
        }

        private async Task<bool> EnsureResistorReadyAsync()
        {
            if (_resistorDriver != null && _resistorDriver.IsConnected)
                return true;

            try
            {
                var candidates = new uint[] { 1, 0, 2, 3, 4, 5, 6, 7 };
                foreach (var logicalId in candidates)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 电阻板卡直连：尝试ACTS6010逻辑ID={logicalId}");
                    var dummy = new ProgrammableResistorDevice
                    {
                        Name = "电阻输出",
                        Model = "PXI-7012",
                        CardName = $"电阻输出(自动探测-{logicalId})",
                        SlotIndex = (int)logicalId
                    };

                    var driver = new ACTS6010Driver(dummy, logicalId);
                    var ok = await driver.ConnectAsync();
                    if (ok)
                    {
                        _resistorDriver = driver;
                        AddLog($"[{DateTime.Now:HH:mm:ss}] 电阻板卡已连接：ACTS6010 逻辑ID={logicalId}");
                        return true;
                    }
                }

                AddLog($"[{DateTime.Now:HH:mm:ss}] 电阻板卡打开失败：ACTS6010 逻辑ID 0-7 均连接失败");
                return false;
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 电阻板卡打开异常：{ex.Message}");
                _resistorDriver = null;
                return false;
            }
        }

        private async Task DisconnectResistorAsync()
        {
            try
            {
                if (_resistorDriver != null)
                {
                    await _resistorDriver.DisconnectAsync();
                    _resistorDriver = null;
                }
            }
            catch
            {
            }
        }

        private System.Collections.Generic.List<DeviceBase> GetDevicesInCurrentChassis()
        {
            try
            {
                var pxiChassisService = ContainerLocator.Container?.Resolve(typeof(IPxiChassisService)) as IPxiChassisService;
                if (pxiChassisService == null)
                    return null;

                var ctx = ContainerLocator.Container?.Resolve(typeof(ISingleBoardTestContextService)) as ISingleBoardTestContextService;
                var chassisName = ctx?.ChassisName ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(chassisName))
                {
                    var chassisDevices = pxiChassisService.GetChassisDevices(chassisName);
                    if (chassisDevices != null)
                    {
                        var list = FlattenDevices(chassisDevices).ToList();
                        AddLog($"[{DateTime.Now:HH:mm:ss}] 当前机箱={chassisName}, 设备数={list.Count}");
                        return list;
                    }
                }

                var all = pxiChassisService.GetAllChassis();
                if (all == null)
                    return null;

                var allDevices = all
                    .Where(c => c?.Devices != null)
                    .SelectMany(c => FlattenDevices(c.Devices))
                    .ToList();

                AddLog($"[{DateTime.Now:HH:mm:ss}] 当前机箱未指定或未找到，使用全局设备列表，设备数={allDevices.Count}");
                return allDevices;
            }
            catch
            {
                return null;
            }
        }

        private void AddLog(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            try
            {
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher != null && !dispatcher.CheckAccess())
                {
                    dispatcher.BeginInvoke(new Action(() => AddLog(message)));
                    return;
                }
            }
            catch
            {
            }

            try
            {
                Logs.Add(message);
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

        private async Task OnSendEnterAtpAsync()
        {
            await _canOpLock.WaitAsync();
            try
            {
                var txIndex = ParseCanChannelIndex(EnterAtpTxChannel);
                var rxIndex = ParseCanChannelIndex(EnterAtpRxChannel);
                if (txIndex < 0 || rxIndex < 0)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 进入ATP失败：通道选择无效");
                    return;
                }

                var ok = await EnsureCanDriverReadyAsync();
                if (!ok)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 进入ATP失败：CAN驱动未就绪");
                    return;
                }

                ok = await OpenCanChannelForPt500Async(txIndex)
                    && await OpenCanChannelForPt500Async(rxIndex)
                    && await OpenCanChannelForPt500Async(SimControllerRxChannel)
                    && await OpenCanChannelForPt500Async(SimControllerTxChannel);
                if (!ok)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 进入ATP失败：打开通道失败 UI_TX={EnterAtpTxChannel}, UI_RX={EnterAtpRxChannel}, CTRL_RX=CH{SimControllerRxChannel}, CTRL_TX=CH{SimControllerTxChannel}");
                    return;
                }

                var frame = PXI4004.CreateDataFrame(DefaultCanFrameId, AtpR);
                AddLog($"[{DateTime.Now:HH:mm:ss}] [{StepEnterAtp}] UI_TX发送->控制器RX(模拟)：UI_TX={EnterAtpTxChannel} -> CTRL_RX=CH{SimControllerRxChannel}, ID=0x{DefaultCanFrameId:X}, Data={FormatData(AtpR)}");

                ok = await _canDriver.SendFrameAsync(txIndex, frame, 0.2);
                if (!ok)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 进入ATP失败：发送失败");
                    return;
                }

                var controllerReceived = await WaitSpecificDataFrameAsync(StepEnterAtp, SimControllerRxChannel, AtpR, TimeSpan.FromMilliseconds(800));
                var responseData = controllerReceived ? AtpEnterOk : AtpFault;

                var respFrame = PXI4004.CreateDataFrame(DefaultCanFrameId, responseData);
                AddLog($"[{DateTime.Now:HH:mm:ss}] [{StepEnterAtp}] 控制器TX(模拟)->UI_RX：CTRL_TX=CH{SimControllerTxChannel} -> UI_RX={EnterAtpRxChannel}, ID=0x{DefaultCanFrameId:X}, Data={FormatData(responseData)}");

                ok = await _canDriver.SendFrameAsync(SimControllerTxChannel, respFrame, 0.2);
                if (!ok)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 进入ATP：控制器响应帧发送失败");
                    return;
                }

                var uiGotResponse = await WaitSpecificDataFrameAsync(StepEnterAtp, rxIndex, responseData, TimeSpan.FromMilliseconds(800));
                if (!uiGotResponse)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 进入ATP：UI_RX未收到控制器响应");
                    LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    LastTestResult = controllerReceived ? "进入ATP：UI未收到OK" : "进入ATP：UI未收到FAULT";
                    return;
                }

                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                LastTestResult = controllerReceived ? "进入ATP成功" : "进入ATP失败(FAULT)";
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 进入ATP异常：{ex.Message}");
            }
            finally
            {
                _canOpLock.Release();
            }
        }

        private async Task OnTestControllerTemperatureAsync()
        {
            await _canOpLock.WaitAsync();
            try
            {
                var txIndex = ParseCanChannelIndex(ControllerTemperatureTestTxChannel);
                var rxIndex = ParseCanChannelIndex(ControllerTemperatureTestRxChannel);
                if (txIndex < 0 || rxIndex < 0)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 控制器温度测试失败：通道选择无效");
                    return;
                }

                var ok = await EnsureCanDriverReadyAsync();
                if (!ok)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 控制器温度测试失败：CAN驱动未就绪");
                    return;
                }

                ok = await OpenCanChannelForPt500Async(txIndex)
                    && await OpenCanChannelForPt500Async(rxIndex)
                    && await OpenCanChannelForPt500Async(SimControllerRxChannel);
                if (!ok)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 控制器温度测试失败：打开通道失败 UI_TX={ControllerTemperatureTestTxChannel}, UI_RX={ControllerTemperatureTestRxChannel}, CTRL_RX=CH{SimControllerRxChannel}");
                    return;
                }

                var frame = PXI4004.CreateDataFrame(DefaultCanFrameId, AbPdtsTemperature);
                AddLog($"[{DateTime.Now:HH:mm:ss}] [{StepControllerTemp}] UI_TX发送->控制器RX(模拟)：UI_TX={ControllerTemperatureTestTxChannel} -> CTRL_RX=CH{SimControllerRxChannel}, ID=0x{DefaultCanFrameId:X}, Data={FormatData(AbPdtsTemperature)}");

                ok = await _canDriver.SendFrameAsync(txIndex, frame, 0.2);
                if (!ok)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 控制器温度测试失败：发送失败");
                    return;
                }

                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                LastTestResult = "控制器温度测试已发送";
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 控制器温度测试异常：{ex.Message}");
            }
            finally
            {
                _canOpLock.Release();
            }
        }

        private async Task OnTestTemperatureTelemetryAsync()
        {
            await _canOpLock.WaitAsync();
            try
            {
                TemperatureTelemetryValueText = "--";

                var rxIndex = ParseCanChannelIndex(TemperatureTelemetryRxChannel);
                if (rxIndex < 0)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 温度回采值失败：通道选择无效");
                    return;
                }

                var ok = await EnsureCanDriverReadyAsync();
                if (!ok)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 温度回采值失败：CAN驱动未就绪");
                    return;
                }

                ok = await OpenCanChannelForPt500Async(rxIndex) && await OpenCanChannelForPt500Async(SimControllerTxChannel);
                if (!ok)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 温度回采值失败：打开通道失败 UI_RX={TemperatureTelemetryRxChannel}, CTRL_TX=CH{SimControllerTxChannel}");
                    return;
                }

                await FlushRxChannelAsync(rxIndex, TimeSpan.FromMilliseconds(120));

                var temperatureSimulated = BuildTelemetryFrameData(TelemetryTemperaturePrefix, 25, 5000);
                var rawSimulated = BuildRawFrameData(TelemetryRawPrefix, 0x01020304);

                var frameTemperature = PXI4004.CreateDataFrame(DefaultCanFrameId, temperatureSimulated);
                AddLog($"[{DateTime.Now:HH:mm:ss}] [{StepTelemetry}] 控制器TX(模拟)->UI_RX：CTRL_TX=CH{SimControllerTxChannel} -> UI_RX={TemperatureTelemetryRxChannel}, ID=0x{DefaultCanFrameId:X}, Data={FormatData(temperatureSimulated)}");

                ok = await _canDriver.SendFrameAsync(SimControllerTxChannel, frameTemperature, 0.2);
                if (!ok)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 温度回采值失败：控制器发送温度采集值指令失败");
                    return;
                }

                var frameRaw = PXI4004.CreateDataFrame(DefaultCanFrameId, rawSimulated);
                AddLog($"[{DateTime.Now:HH:mm:ss}] [{StepTelemetry}] 控制器TX(模拟)->UI_RX：CTRL_TX=CH{SimControllerTxChannel} -> UI_RX={TemperatureTelemetryRxChannel}, ID=0x{DefaultCanFrameId:X}, Data={FormatData(rawSimulated)}");

                ok = await _canDriver.SendFrameAsync(SimControllerTxChannel, frameRaw, 0.2);
                if (!ok)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 温度回采值失败：控制器发送传感器温度原始数据指令失败");
                    return;
                }

                var received = await WaitTelemetryTemperatureAndRawAsync(rxIndex, TimeSpan.FromMilliseconds(800));
                if (received?.Temperature == null)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 温度回采值失败：UI_RX未收到温度采集值帧(07 01 01 02)" );
                    LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    LastTestResult = "温度回采失败(超时)";
                    return;
                }

                var temperatureText = TryParseTelemetryTemperature(received.Temperature, out var temperature)
                    ? temperature.ToString("0.####")
                    : "--";

                TemperatureTelemetryValueText = temperatureText;

                if (received.Raw != null)
                {
                    var rawHex = FormatData(received.Raw, received.Raw.Length);
                    if (TryParseBase6FromNibbles(received.Raw, out var rawBase6Decimal))
                        AddLog($"[{DateTime.Now:HH:mm:ss}] [{StepTelemetry}] 传感器温度原始数据(07 01 01 03) 后四字节(6进制)->10进制：{rawBase6Decimal}，Data={rawHex}");
                    else
                        AddLog($"[{DateTime.Now:HH:mm:ss}] [{StepTelemetry}] 传感器温度原始数据(07 01 01 03) Data={rawHex}");
                }
                else
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] [{StepTelemetry}] 未收到传感器温度原始数据帧(07 01 01 03)(超时)");
                }

                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                LastTestResult = TryParseTelemetryTemperature(received.Temperature, out _) ? "温度回采成功" : "温度回采(解析失败)";
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 温度回采值异常：{ex.Message}");
            }
            finally
            {
                _canOpLock.Release();
            }
        }

        private async Task OnSendExitAtpAsync()
        {
            await _canOpLock.WaitAsync();
            try
            {
                var txIndex = ParseCanChannelIndex(ExitAtpTxChannel);
                var rxIndex = ParseCanChannelIndex(ExitAtpRxChannel);
                if (txIndex < 0 || rxIndex < 0)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 退出ATP失败：通道选择无效");
                    return;
                }

                var ok = await EnsureCanDriverReadyAsync();
                if (!ok)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 退出ATP失败：CAN驱动未就绪");
                    return;
                }

                ok = await OpenCanChannelForPt500Async(txIndex)
                    && await OpenCanChannelForPt500Async(rxIndex)
                    && await OpenCanChannelForPt500Async(SimControllerRxChannel)
                    && await OpenCanChannelForPt500Async(SimControllerTxChannel);
                if (!ok)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 退出ATP失败：打开通道失败 UI_TX={ExitAtpTxChannel}, UI_RX={ExitAtpRxChannel}, CTRL_RX=CH{SimControllerRxChannel}, CTRL_TX=CH{SimControllerTxChannel}");
                    return;
                }

                var frame = PXI4004.CreateDataFrame(DefaultCanFrameId, AtpE);
                AddLog($"[{DateTime.Now:HH:mm:ss}] [{StepExitAtp}] UI_TX发送->控制器RX(模拟)：UI_TX={ExitAtpTxChannel} -> CTRL_RX=CH{SimControllerRxChannel}, ID=0x{DefaultCanFrameId:X}, Data={FormatData(AtpE)}");

                ok = await _canDriver.SendFrameAsync(txIndex, frame, 0.2);
                if (!ok)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 退出ATP失败：发送失败");
                    return;
                }

                var controllerReceived = await WaitSpecificDataFrameAsync(StepExitAtp, SimControllerRxChannel, AtpE, TimeSpan.FromMilliseconds(800));
                var responseData = controllerReceived ? ExitOk : ExitFault;

                var respFrame = PXI4004.CreateDataFrame(DefaultCanFrameId, responseData);
                AddLog($"[{DateTime.Now:HH:mm:ss}] [{StepExitAtp}] 控制器TX(模拟)->UI_RX：CTRL_TX=CH{SimControllerTxChannel} -> UI_RX={ExitAtpRxChannel}, ID=0x{DefaultCanFrameId:X}, Data={FormatData(responseData)}");

                ok = await _canDriver.SendFrameAsync(SimControllerTxChannel, respFrame, 0.2);
                if (!ok)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 退出ATP：控制器响应帧发送失败");
                    return;
                }

                var uiGotResponse = await WaitSpecificDataFrameAsync(StepExitAtp, rxIndex, responseData, TimeSpan.FromMilliseconds(800));
                if (!uiGotResponse)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 退出ATP：UI_RX未收到控制器响应");
                    LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    LastTestResult = controllerReceived ? "退出ATP：UI未收到OK" : "退出ATP：UI未收到FAULT";
                    return;
                }

                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                LastTestResult = controllerReceived ? "退出ATP成功" : "退出ATP失败(FAULT)";
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 退出ATP异常：{ex.Message}");
            }
            finally
            {
                _canOpLock.Release();
            }
        }

        private static byte[] BuildTelemetryFrameData(byte[] prefix, ushort integerPart, ushort fractionalPart)
        {
            var data = new byte[8];
            data[0] = prefix[0];
            data[1] = prefix[1];
            data[2] = prefix[2];
            data[3] = prefix[3];

            data[4] = (byte)(integerPart >> 8);
            data[5] = (byte)(integerPart & 0xFF);
            data[6] = (byte)(fractionalPart >> 8);
            data[7] = (byte)(fractionalPart & 0xFF);
            return data;
        }

        private static byte[] BuildRawFrameData(byte[] prefix, uint raw)
        {
            var data = new byte[8];
            data[0] = prefix[0];
            data[1] = prefix[1];
            data[2] = prefix[2];
            data[3] = prefix[3];
            data[4] = (byte)(raw >> 24);
            data[5] = (byte)((raw >> 16) & 0xFF);
            data[6] = (byte)((raw >> 8) & 0xFF);
            data[7] = (byte)(raw & 0xFF);
            return data;
        }

        private sealed class TelemetryReceive
        {
            public byte[] Temperature { get; set; }
            public byte[] Raw { get; set; }
        }

        private async Task<TelemetryReceive> WaitTelemetryTemperatureAndRawAsync(int rxChannelIndex, TimeSpan timeout)
        {
            var start = DateTime.UtcNow;
            var result = new TelemetryReceive();
            while ((DateTime.UtcNow - start) < timeout)
            {
                var frames = await _canDriver.ReceiveFramesBatchAsync(rxChannelIndex, 8, 0.02);
                if (frames != null && frames.Count > 0)
                {
                    foreach (var f in frames)
                    {
                        var buf = f.DataBuf;
                        var len = f.nDataLength;
                        if (len <= 0 || f.nFrameType != (byte)PXI4004.ARTCANX1_CAN_FRAME_TYPE_DATA_FRM)
                            continue;

                        if (result.Temperature == null && IsDataPrefixMatch(buf, len, TelemetryTemperaturePrefix))
                        {
                            result.Temperature = CopyFrameData(buf, len);
                            AddLog($"[{DateTime.Now:HH:mm:ss}] [{StepTelemetry}] RX命中(温度采集值)：CH{rxChannelIndex}, ID=0x{f.nFrameID:X}, Len={len}, Data={FormatData(buf, len)}");
                            continue;
                        }

                        if (result.Raw == null && IsDataPrefixMatch(buf, len, TelemetryRawPrefix))
                        {
                            result.Raw = CopyFrameData(buf, len);
                            AddLog($"[{DateTime.Now:HH:mm:ss}] [{StepTelemetry}] RX命中(原始数据)：CH{rxChannelIndex}, ID=0x{f.nFrameID:X}, Len={len}, Data={FormatData(buf, len)}");
                            continue;
                        }
                    }
                }

                if (result.Temperature != null && result.Raw != null)
                    return result;

                await Task.Delay(10);
            }

            return result;
        }

        private static byte[] CopyFrameData(byte[] buf, int len)
        {
            var copyLen = Math.Min(8, Math.Min(len, buf?.Length ?? 0));
            if (copyLen <= 0)
                return Array.Empty<byte>();
            var receivedData = new byte[copyLen];
            Array.Copy(buf, receivedData, copyLen);
            return receivedData;
        }

        private static bool TryParseTelemetryTemperature(byte[] frameData, out double temperature)
        {
            temperature = 0;
            if (frameData == null || frameData.Length < 8)
                return false;

            if (frameData[0] != TelemetryTemperaturePrefix[0]
                || frameData[1] != TelemetryTemperaturePrefix[1]
                || frameData[2] != TelemetryTemperaturePrefix[2]
                || frameData[3] != TelemetryTemperaturePrefix[3])
                return false;

            var intPart = (ushort)((frameData[4] << 8) | frameData[5]);
            var fracPart = (ushort)((frameData[6] << 8) | frameData[7]);

            temperature = intPart + fracPart / 10000.0;
            return true;
        }

        private static bool TryParseBase6FromNibbles(byte[] frameData, out long value)
        {
            value = 0;
            if (frameData == null || frameData.Length < 8)
                return false;
            if (frameData[0] != TelemetryRawPrefix[0]
                || frameData[1] != TelemetryRawPrefix[1]
                || frameData[2] != TelemetryRawPrefix[2]
                || frameData[3] != TelemetryRawPrefix[3])
                return false;

            for (var i = 4; i <= 7; i++)
            {
                var b = frameData[i];
                var hi = (b >> 4) & 0xF;
                var lo = b & 0xF;
                if (hi > 5 || lo > 5)
                    return false;
                value = checked(value * 6 + hi);
                value = checked(value * 6 + lo);
            }

            return true;
        }

        private async Task<byte[]> WaitDataFrameByPrefixAsync(string stepName, int rxChannelIndex, byte[] prefix, TimeSpan timeout)
        {
            var start = DateTime.UtcNow;
            while ((DateTime.UtcNow - start) < timeout)
            {
                var frames = await _canDriver.ReceiveFramesBatchAsync(rxChannelIndex, 5, 0.02);
                if (frames != null && frames.Count > 0)
                {
                    foreach (var f in frames)
                    {
                        var buf = f.DataBuf;
                        var len = f.nDataLength;

                        // 过滤空帧/非数据帧，避免底层在无帧时返回“成功但Len=0”的情况污染上层逻辑
                        if (len <= 0 || f.nFrameType != (byte)PXI4004.ARTCANX1_CAN_FRAME_TYPE_DATA_FRM)
                            continue;

                        if (IsDataPrefixMatch(buf, len, prefix))
                        {
                            AddLog($"[{DateTime.Now:HH:mm:ss}] [{stepName}] RX命中：CH{rxChannelIndex}, ID=0x{f.nFrameID:X}, Len={len}, Data={FormatData(buf, len)}");
                            return CopyFrameData(buf, len);
                        }
                    }
                }

                await Task.Delay(10);
            }

            return null;
        }

        private async Task FlushRxChannelAsync(int rxChannelIndex, TimeSpan duration)
        {
            var start = DateTime.UtcNow;
            while ((DateTime.UtcNow - start) < duration)
            {
                var frames = await _canDriver.ReceiveFramesBatchAsync(rxChannelIndex, 20, 0.001);
                if (frames == null || frames.Count == 0)
                    break;
                await Task.Delay(1);
            }
        }

        private async Task<bool> OpenCanChannelForPt500Async(int channelIndex)
        {
            if (_canDriver == null)
                return false;
            if (!_canDriver.IsConnected)
                return false;
            if (channelIndex < 0)
                return false;

            try
            {
                PXI4004.ARTCANX1_CAN_PARAM param;
                try
                {
                    var handle = _canDriver.DeviceHandle;
                    if (handle != IntPtr.Zero)
                    {
                        param = PXI4004.GetDefaultCANParam(handle, (uint)channelIndex);
                    }
                    else
                    {
                        param = new PXI4004.ARTCANX1_CAN_PARAM();
                    }
                }
                catch
                {
                    param = new PXI4004.ARTCANX1_CAN_PARAM();
                }

                if (param.nReserved1 == null || param.nReserved1.Length != 7)
                    param.nReserved1 = new uint[7];
                if (param.nReserved2 == null || param.nReserved2.Length != 32)
                    param.nReserved2 = new uint[32];

                if (param.SendTrig.nReserved == null || param.SendTrig.nReserved.Length != 20)
                    param.SendTrig.nReserved = new uint[20];

                param.nBaudRate = PXI4004.CAN_BAUD_500K;
                param.nWorkMode = (byte)PXI4004.ARTCANX1_CAN_WORKMODE_NORMAL;
                param.bRecvTimestampEn = 1;
                param.bAccExtID = 0;
                param.nAccFilterCnt = (byte)PXI4004.ARTCANX1_CAN_ACC_NUM_NONE;
                param.nAccCodeA = 0x00000000;
                param.nAccCodeB = 0x00000000;
                param.nAccMaskA = 0xFFFFFFFF;
                param.nAccMaskB = 0xFFFFFFFF;
                param.nFrameInterval = 0;
                param.SendTrig.nTriggerType = PXI4004.ARTCANX1_TRIGTYPE_NONE;

                return await _canDriver.OpenChannelAsync(channelIndex, param);
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 打开通道CH{channelIndex}失败：{ex.Message}");
                return false;
            }
        }

        private static bool IsDataPrefixMatch(byte[] receivedBuf, int receivedLen, byte[] prefix)
        {
            if (prefix == null || prefix.Length == 0)
                return false;
            if (receivedBuf == null)
                return false;
            if (receivedLen < prefix.Length)
                return false;
            if (receivedBuf.Length < prefix.Length)
                return false;

            for (var i = 0; i < prefix.Length; i++)
            {
                if (receivedBuf[i] != prefix[i])
                    return false;
            }

            return true;
        }

        private async Task<bool> EnsureCanDriverReadyAsync()
        {
            if (_canDriver != null && _canDriver.IsConnected)
                return true;

            try
            {
                // PT500 视图：固定使用“直连探测”方式连接 CAN 板卡（避免首次依赖机箱枚举/DriverFactory）
                for (var logicalIndex = 0; logicalIndex <= 7; logicalIndex++)
                {
                    var dummy = new CanBusDevice
                    {
                        Name = "PXI4004",
                        Model = "PXI-4004",
                        CardName = $"PXI4004(直连-{logicalIndex})",
                        SlotIndex = logicalIndex
                    };

                    var direct = new PXI4004Driver(dummy, logicalIndex);
                    var ok = await direct.ConnectAsync();
                    if (ok)
                    {
                        _canDriver = direct;
                        AddLog($"[{DateTime.Now:HH:mm:ss}] CAN已连接(直连)：逻辑设备{logicalIndex}");
                        return true;
                    }
                }

                AddLog($"[{DateTime.Now:HH:mm:ss}] CAN连接失败：未探测到可用PXI4004逻辑设备(0-7)");
                return false;
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] CAN驱动未准备：EnsureCanDriverReadyAsync异常：{ex.Message}");
                return false;
            }
        }

        private static System.Collections.Generic.IEnumerable<DeviceBase> FlattenDevices(System.Collections.Generic.IEnumerable<DeviceBase> devices)
        {
            if (devices == null)
                yield break;

            foreach (var d in devices)
            {
                if (d == null)
                    continue;

                yield return d;

                if (d.Children == null)
                    continue;

                foreach (var child in FlattenDevices(d.Children))
                    yield return child;
            }
        }

        private static int ParseCanChannelIndex(string channel)
        {
            if (string.IsNullOrWhiteSpace(channel))
                return -1;

            var s = channel.Trim();
            var idx = s.LastIndexOf("CH", StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
                return -1;

            var numberPart = s.Substring(idx + 2).Trim();
            if (!int.TryParse(numberPart, out var n))
                return -1;

            if (n < 0)
                return -1;

            return n;
        }

        private async Task<bool> WaitAnyFrameAsync(string stepName, int rxChannelIndex, TimeSpan timeout)
        {
            var start = DateTime.UtcNow;
            while ((DateTime.UtcNow - start) < timeout)
            {
                var frames = await _canDriver.ReceiveFramesBatchAsync(rxChannelIndex, 5, 0.02);
                if (frames != null && frames.Count > 0)
                {
                    foreach (var f in frames)
                    {
                        var data = f.DataBuf;
                        var len = f.nDataLength;
                        if (len <= 0 || f.nFrameType != (byte)PXI4004.ARTCANX1_CAN_FRAME_TYPE_DATA_FRM)
                            continue;
                        AddLog($"[{DateTime.Now:HH:mm:ss}] [{stepName}] RX：CH{rxChannelIndex}, ID=0x{f.nFrameID:X}, Len={len}, Data={FormatData(data, len)}");
                        return true;
                    }
                }

                await Task.Delay(10);
            }

            return false;
        }

        private async Task<bool> WaitSpecificDataFrameAsync(string stepName, int rxChannelIndex, byte[] expectedData, TimeSpan timeout)
        {
            var start = DateTime.UtcNow;
            while ((DateTime.UtcNow - start) < timeout)
            {
                var frames = await _canDriver.ReceiveFramesBatchAsync(rxChannelIndex, 5, 0.02);
                if (frames != null && frames.Count > 0)
                {
                    foreach (var f in frames)
                    {
                        var buf = f.DataBuf;
                        var len = f.nDataLength;
                        if (len <= 0 || f.nFrameType != (byte)PXI4004.ARTCANX1_CAN_FRAME_TYPE_DATA_FRM)
                            continue;

                        if (IsDataMatch(buf, len, expectedData))
                        {
                            AddLog($"[{DateTime.Now:HH:mm:ss}] [{stepName}] RX命中：CH{rxChannelIndex}, ID=0x{f.nFrameID:X}, Len={len}, Data={FormatData(buf, len)}");
                            return true;
                        }
                    }
                }

                await Task.Delay(10);
            }

            return false;
        }

        private static bool IsDataMatch(byte[] receivedBuf, int receivedLen, byte[] expected)
        {
            if (expected == null)
                return false;
            if (receivedBuf == null)
                return false;
            if (receivedLen < expected.Length)
                return false;
            if (receivedBuf.Length < expected.Length)
                return false;

            for (int i = 0; i < expected.Length; i++)
            {
                if (receivedBuf[i] != expected[i])
                    return false;
            }

            return true;
        }

        private static string FormatData(byte[] data, int length = -1)
        {
            if (data == null)
                return string.Empty;

            var len = length;
            if (len < 0)
                len = data.Length;

            len = Math.Min(len, data.Length);
            if (len <= 0)
                return string.Empty;

            return string.Join(" ", data.Take(len).Select(b => b.ToString("X2")));
        }

        private static double GetTargetResistanceOhm(string gear)
        {
            return gear switch
            {
                "1挡" => 371.65,
                "2挡" => 550.0,
                "3挡" => 758.55,
                _ => 371.65
            };
        }

        private static async Task<(MessageBasedSession Session, ResourceManager Rm)> OpenDmmAsync()
        {
            var rm = new ResourceManager();
            var resource = "TCPIP0::192.168.1.13::inst0::INSTR";
            try
            {
                var session = (MessageBasedSession)rm.Open(resource);
                session.TimeoutMilliseconds = 3000;
                session.RawIO.Write("*CLS\n");
                session.RawIO.Write(":SYST:REM\n");
                session.RawIO.Write(":CONF:RES\n");
                await Task.Yield();
                return (session, rm);
            }
            catch
            {
                try { rm.Dispose(); } catch { }
                throw;
            }
        }

        private static double QueryDmmResistance(MessageBasedSession session)
        {
            session.RawIO.Write(":MEAS:RES?\n");
            var resp = session.RawIO.ReadString();
            if (double.TryParse(resp?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var r))
            {
                return r;
            }

            if (double.TryParse(resp?.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out r))
            {
                return r;
            }

            return double.NaN;
        }

        private static DeviceBase Resolve7012Device(string chassisName, IPxiChassisService pxiChassisService)
        {
            if (pxiChassisService == null) return null;

            var chassis = pxiChassisService.GetAllChassis()?.FirstOrDefault(c =>
                string.Equals(c?.Name, chassisName, StringComparison.OrdinalIgnoreCase));

            var devices = chassis?.Devices;
            if (devices == null) return null;

            return devices.FirstOrDefault(d => d is ProgrammableResistorDevice)
                   ?? devices.FirstOrDefault(d => (d?.Model ?? string.Empty).ToUpperInvariant().Contains("7012"));
        }

        private async Task SendSetControllerResistorAsync()
        {
            if (IsResistorMeasuring)
            {
                return;
            }

            IsResistorMeasuring = true;
            MeasuredResistanceValueText = "--";

            MessageBasedSession dmmSession = null;
            ResourceManager dmmRm = null;
            bool matrix1Connected = false;
            bool matrix2Connected = false;
            bool resistorReady = false;

            try
            {
                // 优先使用上游新增的“电阻板卡直连(逻辑ID探测)”能力，不依赖机箱上下文
                resistorReady = await EnsureResistorReadyAsync();
                if (!resistorReady || _resistorDriver == null || !_resistorDriver.IsConnected)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 接入电阻失败：电阻板卡未就绪");
                    return;
                }

                var targetOhm = GetTargetResistanceOhm(ResistorGear);
                var channelId = "RO0";

                AddLog($"[{DateTime.Now:HH:mm:ss}] 发送：接入电阻，档位={ResistorGear}({channelId})，目标={targetOhm.ToString("F2", CultureInfo.InvariantCulture)}Ω");

                // 先断开其他档位，避免并联/串扰导致测量值异常
                foreach (var ch in new[] { "RO0", "RO1", "RO2" })
                {
                    if (!string.Equals(ch, channelId, StringComparison.OrdinalIgnoreCase))
                    {
                        try { await _resistorDriver.SetRelayStateAsync(ch, false, false); } catch { }
                    }
                }

                var relayOk = await _resistorDriver.SetRelayStateAsync(channelId, true, false);
                if (!relayOk)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 7012设置{channelId}继电器失败(通路闭合/短路断开)");
                    return;
                }

                var writeOk = await _resistorDriver.WriteChannelAsync(channelId, targetOhm);
                if (!writeOk)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 7012写入{channelId}失败");
                    return;
                }

                await Task.Delay(50);

                try
                {
                    var boardReadback = await _resistorDriver.ReadChannelAsync(channelId);
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 7012回读({channelId})={boardReadback.ToString("F5", CultureInfo.InvariantCulture)}Ω");
                }
                catch
                {
                }

                var matrixSvc = MatrixControlService.Instance;
                matrix1Connected = await matrixSvc.ConnectNodesAsync("I1", "O8", 6, "192.168.1.3");
                if (!matrix1Connected)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 矩阵开关1连接失败(I1->O8 slot6)");
                    return;
                }

                matrix2Connected = await matrixSvc.ConnectNodesAsync("I4", "O2", 4, "192.168.1.3");
                if (!matrix2Connected)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 矩阵开关2连接失败(I4->O2 slot4)");
                    return;
                }

                (dmmSession, dmmRm) = await OpenDmmAsync();
                await Task.Delay(200);
                var measured = QueryDmmResistance(dmmSession);

                if (double.IsNaN(measured))
                {
                    MeasuredResistanceValueText = "NaN";
                }
                else
                {
                    MeasuredResistanceValueText = $"{measured.ToString("F5", CultureInfo.InvariantCulture)}Ω";
                }

                AddLog($"[{DateTime.Now:HH:mm:ss}] 万用表实测电阻：{MeasuredResistanceValueText}");
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 接入电阻异常：{ex.Message}");
            }
            finally
            {
                var matrixSvc = MatrixControlService.Instance;

                try
                {
                    if (matrix2Connected)
                    {
                        await matrixSvc.DisconnectNodesAsync("I4", "O2", 4, "192.168.1.3");
                    }
                }
                catch { }

                try
                {
                    if (matrix1Connected)
                    {
                        await matrixSvc.DisconnectNodesAsync("I1", "O8", 6, "192.168.1.3");
                    }
                }
                catch { }

                try
                {
                    if (dmmSession != null)
                    {
                        try { dmmSession.RawIO.Write(":SYST:LOC\n"); } catch { }
                        try { dmmSession.Dispose(); } catch { }
                    }
                }
                catch { }

                try
                {
                    if (dmmRm != null)
                    {
                        try { dmmRm.Dispose(); } catch { }
                    }
                }
                catch { }

                try
                {
                    if (resistorReady)
                        await DisconnectResistorAsync();
                }
                catch { }

                IsResistorMeasuring = false;
            }
        }
    }
}
