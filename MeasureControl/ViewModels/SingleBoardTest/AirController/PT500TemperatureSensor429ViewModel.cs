using Prism.Commands;
using Prism.Mvvm;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using MeasureControl.Drivers;
using MeasureControl.Models.Devices;
using MeasureControl.Services;
using NationalInstruments.Visa;
using Prism.Ioc;

namespace MeasureControl.ViewModels.SingleBoardTest.AirController
{
    public class PT500TemperatureSensor429ViewModel : BindableBase
    {
        public PT500TemperatureSensor429ViewModel()
        {
            _enterAtpTxChannel = "CH0";
            _enterAtpRxChannel = "CH1";
            _controllerTemperatureTestTxChannel = "CH0";
            _controllerTemperatureTestRxChannel = "CH1";
            _temperatureTelemetryRxChannel = "CH1";
            _exitAtpTxChannel = "CH0";
            _exitAtpRxChannel = "CH1";

            _enterAtpRxDataText = "--";
            _controllerTemperatureTestRxDataText = "--";
            _temperatureTelemetryRxDataText = "--";
            _exitAtpRxDataText = "--";

            _resistorGear = "1挡";
            ResistorGearValueText = _resistorGear;
            MeasuredResistanceValueText = "--";
            TemperatureTelemetryValueText = "--";
            LastTestTime = "--";
            LastTestResult = "--";

            SendEnterAtpCommand = new DelegateCommand(() => AddLog($"[{DateTime.Now:HH:mm:ss}] 发送：进入ATP"));
            SendSetControllerResistorCommand = new DelegateCommand(async () => await SendSetControllerResistorAsync(), () => !IsResistorMeasuring)
                .ObservesProperty(() => IsResistorMeasuring);
            TestControllerTemperatureCommand = new DelegateCommand(() => AddLog($"[{DateTime.Now:HH:mm:ss}] 测试：控制器温度"));
            TestTemperatureTelemetryCommand = new DelegateCommand(() =>
            {
                TemperatureTelemetryValueText = "--";
                AddLog($"[{DateTime.Now:HH:mm:ss}] 测试：温度回采值，RX通道={TemperatureTelemetryRxChannel}");
            });
            SendExitAtpCommand = new DelegateCommand(() => AddLog($"[{DateTime.Now:HH:mm:ss}] 发送：退出ATP"));
            ClearLogCommand = new DelegateCommand(() => Logs.Clear());

            ManualTestCommand = new DelegateCommand(OnManualTest);
            AutoTestCommand = new DelegateCommand(OnAutoTest);
        }

        private ACTS6010Driver _resistorDriver;

        private string _enterAtpTxChannel;
        private string _enterAtpRxChannel;
        private string _controllerTemperatureTestTxChannel;
        private string _controllerTemperatureTestRxChannel;
        private string _temperatureTelemetryRxChannel;
        private string _exitAtpTxChannel;
        private string _exitAtpRxChannel;

        private string _enterAtpRxDataText;
        private string _controllerTemperatureTestRxDataText;
        private string _temperatureTelemetryRxDataText;
        private string _exitAtpRxDataText;

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
                if (SetProperty(ref _isManualTestRunning, value) && value)
                {
                    IsAutoTestRunning = false;
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
                    RaisePropertyChanged(nameof(CanEditStepControls));
                    RaisePropertyChanged(nameof(CanClickManualTestButton));
                    RaisePropertyChanged(nameof(CanClickAutoTestButton));

                    if (value)
                    {
                        IsManualTestRunning = false;
                    }
                }
            }
        }

        public bool CanEditStepControls => !IsAutoTestRunning;

        public bool CanClickManualTestButton => !IsAutoTestRunning;

        public bool CanClickAutoTestButton => !IsManualTestRunning;

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

        public string EnterAtpRxDataText
        {
            get => _enterAtpRxDataText;
            private set => SetProperty(ref _enterAtpRxDataText, value);
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

        public string ControllerTemperatureTestRxDataText
        {
            get => _controllerTemperatureTestRxDataText;
            private set => SetProperty(ref _controllerTemperatureTestRxDataText, value);
        }

        public string TemperatureTelemetryRxChannel
        {
            get => _temperatureTelemetryRxChannel;
            set => SetProperty(ref _temperatureTelemetryRxChannel, value);
        }

        public string TemperatureTelemetryRxDataText
        {
            get => _temperatureTelemetryRxDataText;
            private set => SetProperty(ref _temperatureTelemetryRxDataText, value);
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

        public string ExitAtpRxDataText
        {
            get => _exitAtpRxDataText;
            private set => SetProperty(ref _exitAtpRxDataText, value);
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
            IsManualTestRunning = !IsManualTestRunning;
            AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试{(IsManualTestRunning ? "启动" : "停止")}");
            if (!IsManualTestRunning)
            {
                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            }
        }

        private void OnAutoTest()
        {
            IsAutoTestRunning = !IsAutoTestRunning;
            AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试{(IsAutoTestRunning ? "启动" : "停止")}");
            if (!IsAutoTestRunning)
            {
                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            }
        }

        private void AddLog(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            try
            {
                Logs.Add(message);
            }
            catch
            {
            }
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
                }
            }
            catch
            {
            }
            finally
            {
                _resistorDriver = null;
            }
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
