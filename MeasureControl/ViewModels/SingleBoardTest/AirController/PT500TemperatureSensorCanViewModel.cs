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
    public class PT500TemperatureSensorCanViewModel : BindableBase
    {
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
            if (IsManualTestRunning)
            {
                IsManualTestRunning = false;
                AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试停止");
                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                return;
            }

            IsManualTestRunning = true;
            AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试启动");
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
            bool resistorConnected = false;

            ACTS6010Driver resistorDriver = null;

            try
            {
                var contextSvc = ContainerLocator.Container?.Resolve(typeof(ISingleBoardTestContextService)) as ISingleBoardTestContextService;
                var chassisName = contextSvc?.ChassisName ?? string.Empty;
                var pxiChassisService = ContainerLocator.Container?.Resolve(typeof(IPxiChassisService)) as IPxiChassisService;

                var device = Resolve7012Device(chassisName, pxiChassisService);
                if (device == null)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 接入电阻失败：未找到PXI-7012设备，机箱={chassisName}");
                    return;
                }

                resistorDriver = DriverFactory.CreateDriver(device) as ACTS6010Driver;
                if (resistorDriver == null)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 接入电阻失败：未找到7012驱动");
                    return;
                }

                var targetOhm = GetTargetResistanceOhm(ResistorGear);
                AddLog($"[{DateTime.Now:HH:mm:ss}] 发送：接入电阻，档位={ResistorGear}，目标={targetOhm.ToString("F2", CultureInfo.InvariantCulture)}Ω");

                resistorConnected = await resistorDriver.ConnectAsync();
                resistorConnected = resistorConnected && resistorDriver.IsConnected;
                if (!resistorConnected)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 7012连接失败");
                    return;
                }

                var writeOk = await resistorDriver.WriteChannelAsync("RO0", targetOhm);
                if (!writeOk)
                {
                    AddLog($"[{DateTime.Now:HH:mm:ss}] 7012写入RO0失败");
                    return;
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
                    if (resistorDriver != null && resistorConnected)
                    {
                        await resistorDriver.DisconnectAsync();
                    }
                }
                catch { }

                IsResistorMeasuring = false;
            }
        }
    }
}
