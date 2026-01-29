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
        private const uint DefaultCanFrameId = 0;
        private const int DmmDefaultPort = 5555;
        private const string DmmDefaultIp = "192.168.1.13";
        private const string MatrixIpAddress = "192.168.1.3";
        private const int MatrixFixedSlotIndex = 4;
        private const int MatrixDmmSlotIndex = 7;

        private static readonly byte[] AtpR = { 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] AtpEnterOk = { 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x02 };
        private static readonly byte[] AtpE = { 0x00, 0x02, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01 };
        private static readonly byte[] AbPdtsTemperature = { 0x07, 0x01, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00 };

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
            _enterAtpTxChannel = "CAN CH0";
            _enterAtpRxChannel = "CAN CH1";
            _controllerTemperatureTestTxChannel = "CAN CH2";
            _controllerTemperatureTestRxChannel = "CAN CH3";
            _temperatureTelemetryRxChannel = "CAN CH4";
            _exitAtpTxChannel = "CAN CH5";
            _exitAtpRxChannel = "CAN CH6";

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
            TestTemperatureTelemetryCommand = new DelegateCommand(() =>
            {
                TemperatureTelemetryValueText = "--";
                AddLog($"[{DateTime.Now:HH:mm:ss}] 测试：温度回采值，RX通道={TemperatureTelemetryRxChannel}");
            });
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

                ok = await _canDriver.OpenChannelAsync(txIndex) && await _canDriver.OpenChannelAsync(rxIndex);
                if (!ok)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 进入ATP失败：打开通道失败 TX={EnterAtpTxChannel}, RX={EnterAtpRxChannel}");
                    return;
                }

                var frame = PXI4004.CreateDataFrame(DefaultCanFrameId, AtpR);
                AddLog($"[{DateTime.Now:HH:mm:ss}] 发送：进入ATP(ATP R)，TX={EnterAtpTxChannel}, RX={EnterAtpRxChannel}, Data={FormatData(AtpR)}");

                ok = await _canDriver.SendFrameAsync(txIndex, frame, 0.2);
                if (!ok)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 进入ATP失败：发送失败");
                    return;
                }

                var received = await WaitSpecificDataFrameAsync(rxIndex, AtpR, TimeSpan.FromMilliseconds(800));
                if (!received)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 进入ATP失败：RX未收到ATP R返回(Data={FormatData(AtpR)})");
                    return;
                }

                AddLog($"[{DateTime.Now:HH:mm:ss}] 进入ATP：RX已收到返回，发送进入ATP成功帧(固定 CAN CH2->CH3)");

                var fixedTx = 2;
                var fixedRx = 3;
                ok = await _canDriver.OpenChannelAsync(fixedTx) && await _canDriver.OpenChannelAsync(fixedRx);
                if (!ok)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 进入ATP成功帧发送失败：打开固定通道失败");
                    return;
                }

                var okFrame = PXI4004.CreateDataFrame(DefaultCanFrameId, AtpEnterOk);
                ok = await _canDriver.SendFrameAsync(fixedTx, okFrame, 0.2);
                if (!ok)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 进入ATP成功帧发送失败");
                    return;
                }

                AddLog($"[{DateTime.Now:HH:mm:ss}] 进入ATP成功帧已发送：TX=CAN CH2, RX=CAN CH3, Data={FormatData(AtpEnterOk)}");
                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                LastTestResult = "进入ATP成功";
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

                ok = await _canDriver.OpenChannelAsync(txIndex) && await _canDriver.OpenChannelAsync(rxIndex);
                if (!ok)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 控制器温度测试失败：打开通道失败 TX={ControllerTemperatureTestTxChannel}, RX={ControllerTemperatureTestRxChannel}");
                    return;
                }

                var frame = PXI4004.CreateDataFrame(DefaultCanFrameId, AbPdtsTemperature);
                AddLog($"[{DateTime.Now:HH:mm:ss}] 测试：控制器温度(AB_PDTS_Temperature)，TX={ControllerTemperatureTestTxChannel}, RX={ControllerTemperatureTestRxChannel}, Data={FormatData(AbPdtsTemperature)}");

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

                ok = await _canDriver.OpenChannelAsync(txIndex) && await _canDriver.OpenChannelAsync(rxIndex);
                if (!ok)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 退出ATP失败：打开通道失败 TX={ExitAtpTxChannel}, RX={ExitAtpRxChannel}");
                    return;
                }

                var frame = PXI4004.CreateDataFrame(DefaultCanFrameId, AtpE);
                AddLog($"[{DateTime.Now:HH:mm:ss}] 发送：退出ATP(ATP E)，TX={ExitAtpTxChannel}, RX={ExitAtpRxChannel}, Data={FormatData(AtpE)}");

                ok = await _canDriver.SendFrameAsync(txIndex, frame, 0.2);
                if (!ok)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 退出ATP失败：发送失败");
                    return;
                }

                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                LastTestResult = "退出ATP已发送";
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

        private async Task<bool> EnsureCanDriverReadyAsync()
        {
            if (_canDriver != null && _canDriver.IsConnected)
                return true;

            try
            {
                var pxiChassisService = ContainerLocator.Container?.Resolve(typeof(IPxiChassisService)) as IPxiChassisService;
                if (pxiChassisService == null)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] CAN驱动未准备：未获取到IPxiChassisService");
                    return false;
                }

                System.Collections.Generic.List<DeviceBase> allDevices = null;
                var ctx = ContainerLocator.Container?.Resolve(typeof(ISingleBoardTestContextService)) as ISingleBoardTestContextService;
                var chassisName = ctx?.ChassisName ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(chassisName))
                {
                    var chassisDevices = pxiChassisService.GetChassisDevices(chassisName);
                    if (chassisDevices != null)
                    {
                        allDevices = FlattenDevices(chassisDevices).ToList();
                        AddLog($"[{DateTime.Now:HH:mm:ss}] CAN设备查找：使用当前机箱={chassisName}, 设备数={allDevices.Count}");
                    }
                }

                if (allDevices == null)
                {
                    var chassisList = pxiChassisService.GetAllChassis();
                    if (chassisList == null)
                    {
                        AddLog($"[{DateTime.Now:HH:mm:ss}] CAN驱动未准备：GetAllChassis 返回空");
                        return false;
                    }

                    allDevices = chassisList
                        .Where(c => c?.Devices != null)
                        .SelectMany(c => FlattenDevices(c.Devices))
                        .Where(d => d != null)
                        .ToList();

                    AddLog($"[{DateTime.Now:HH:mm:ss}] CAN设备查找：使用全局设备列表，设备数={allDevices.Count}");
                }

                if (allDevices.Count == 0)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] CAN驱动未准备：机箱设备列表为空");
                    return false;
                }

                try
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] CAN设备列表预览(前30条)：");
                    int idx = 0;
                    foreach (var d in allDevices.Take(30))
                    {
                        var slot = (d as PxiDeviceBase)?.SlotIndex;
                        var typeName = d?.GetType()?.Name ?? "<null>";
                        AddLog($"[{DateTime.Now:HH:mm:ss}]  #{idx} Type={typeName}, Model={d?.Model}, Name={d?.Name}, CardName={d?.CardName}, Slot={slot}, Children={(d?.Children?.Count ?? 0)}, Id={d?.Id}");
                        idx++;
                    }
                }
                catch
                {
                }

                DeviceBase target = allDevices
                    .OfType<CanBusDevice>()
                    .FirstOrDefault(d => d != null && ((d.Model ?? string.Empty).ToUpperInvariant().Contains("4004") || (d.Name ?? string.Empty).ToUpperInvariant().Contains("4004")));

                target ??= allDevices
                    .OfType<CanBusDevice>()
                    .FirstOrDefault();

                target ??= allDevices
                    .FirstOrDefault(d =>
                        ((d.Model ?? string.Empty).ToUpperInvariant().Contains("4004") ||
                         (d.Name ?? string.Empty).ToUpperInvariant().Contains("4004") ||
                         (d.CardName ?? string.Empty).ToUpperInvariant().Contains("4004") ||
                         (d.Model ?? string.Empty).ToUpperInvariant().Contains("CAN") ||
                         (d.Name ?? string.Empty).ToUpperInvariant().Contains("CAN") ||
                         (d.CardName ?? string.Empty).ToUpperInvariant().Contains("CAN")));

                PXI4004Driver driver = null;
                if (target == null)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] CAN设备查找：字符串匹配未命中，开始按驱动类型探测(PXI4004Driver)...");
                    foreach (var d in allDevices)
                    {
                        if (d == null)
                            continue;

                        try
                        {
                            var slotIndex = (d as PxiDeviceBase)?.SlotIndex ?? -1;
                            var probed = DriverFactory.GetCachedDriver(d.Id, slotIndex) ?? DriverFactory.CreateDriver(d);
                            if (probed is PXI4004Driver p)
                            {
                                target = d;
                                driver = p;
                                AddLog($"[{DateTime.Now:HH:mm:ss}] CAN设备查找：探测命中PXI4004Driver，Device={d?.Model ?? d?.Name ?? d?.CardName}, Slot={slotIndex}, Id={d?.Id}");
                                break;
                            }
                        }
                        catch
                        {
                        }
                    }
                }

                if (target == null)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] CAN驱动未准备：未找到CAN板卡设备(4004/CAN)，尝试直接连接PXI4004逻辑设备0");
                    try
                    {
                        for (var logicalIndex = 0; logicalIndex <= 7; logicalIndex++)
                        {
                            AddLog($"[{DateTime.Now:HH:mm:ss}] CAN直接连接：尝试PXI4004逻辑设备{logicalIndex}");
                            var dummy = new CanBusDevice
                            {
                                Name = "PXI4004",
                                Model = "PXI-4004",
                                CardName = $"PXI4004(自动探测-{logicalIndex})",
                                SlotIndex = logicalIndex
                            };

                            var direct = new PXI4004Driver(dummy, logicalIndex);
                            var directOk = await direct.ConnectAsync();
                            if (directOk)
                            {
                                _canDriver = direct;
                                AddLog($"[{DateTime.Now:HH:mm:ss}] CAN驱动已连接：PXI4004(自动探测) 逻辑设备{logicalIndex}");
                                return true;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        AddLog($"[{DateTime.Now:HH:mm:ss}] CAN直接连接失败：{ex.Message}");
                    }

                    return false;
                }

                if (driver == null)
                {
                    try
                    {
                        var slotIndex = (target as PxiDeviceBase)?.SlotIndex ?? -1;
                        driver = (DriverFactory.GetCachedDriver(target.Id, slotIndex) ?? DriverFactory.CreateDriver(target)) as PXI4004Driver;
                    }
                    catch (Exception ex)
                    {
                        AddLog($"[{DateTime.Now:HH:mm:ss}] CAN驱动未准备：DriverFactory.CreateDriver异常：{ex.Message}");
                    }
                }

                if (driver == null)
                {
                    int slot = 0;
                    if (target is CanBusDevice c) slot = c.SlotIndex > 0 ? c.SlotIndex : 0;
                    else if (target is PxiDeviceBase p) slot = p.SlotIndex > 0 ? p.SlotIndex : 0;
                    driver = new PXI4004Driver(target, slot);
                }

                var ok = await driver.ConnectAsync();
                if (!ok)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] CAN驱动未准备：ConnectAsync失败 (Device={target?.Model ?? target?.Name ?? target?.CardName})");
                    return false;
                }

                _canDriver = driver;
                return _canDriver.IsConnected;
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

        private async Task<bool> WaitAnyFrameAsync(int rxChannelIndex, TimeSpan timeout)
        {
            var start = DateTime.UtcNow;
            while ((DateTime.UtcNow - start) < timeout)
            {
                var frame = await _canDriver.ReceiveFrameAsync(rxChannelIndex, 0.02);
                if (frame.HasValue)
                {
                    var data = frame.Value.DataBuf;
                    var len = frame.Value.nDataLength;
                    AddLog($"[{DateTime.Now:HH:mm:ss}] RX收到：CH{rxChannelIndex}, ID=0x{frame.Value.nFrameID:X}, Len={len}, Data={FormatData(data, len)}");
                    return true;
                }

                await Task.Delay(10);
            }

            return false;
        }

        private async Task<bool> WaitSpecificDataFrameAsync(int rxChannelIndex, byte[] expectedData, TimeSpan timeout)
        {
            var start = DateTime.UtcNow;
            while ((DateTime.UtcNow - start) < timeout)
            {
                var frame = await _canDriver.ReceiveFrameAsync(rxChannelIndex, 0.02);
                if (frame.HasValue)
                {
                    var buf = frame.Value.DataBuf;
                    var len = frame.Value.nDataLength;
                    AddLog($"[{DateTime.Now:HH:mm:ss}] RX收到：CH{rxChannelIndex}, ID=0x{frame.Value.nFrameID:X}, Len={len}, Data={FormatData(buf, len)}");

                    if (IsDataMatch(buf, len, expectedData))
                        return true;
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
                AddLog($"[{DateTime.Now:HH:mm:ss}] 发送：接入电阻，档位={ResistorGear}，目标={targetOhm.ToString("F2", CultureInfo.InvariantCulture)}Ω");

                var relayOk = await _resistorDriver.SetRelayStateAsync("RO0", true, false);
                if (!relayOk)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 7012设置RO0继电器失败(通路闭合/短路断开)");
                    return;
                }

                var writeOk = await _resistorDriver.WriteChannelAsync("RO0", targetOhm);
                if (!writeOk)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 7012写入RO0失败");
                    return;
                }

                await Task.Delay(50);

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
