using Prism.Commands;
using Prism.Mvvm;
using System;
using System.Collections.ObjectModel;

namespace MeasureControl.ViewModels.SingleBoardTest.AirController
{
    public class PT500TemperatureSensor429ViewModel : BindableBase
    {
        public PT500TemperatureSensor429ViewModel()
        {
            _enterAtpTxChannel = "ARINC429 CH0";
            _enterAtpRxChannel = "ARINC429 CH1";
            _controllerTemperatureTestTxChannel = "ARINC429 CH2";
            _controllerTemperatureTestRxChannel = "ARINC429 CH3";
            _temperatureTelemetryRxChannel = "ARINC429 CH4";
            _exitAtpTxChannel = "ARINC429 CH5";
            _exitAtpRxChannel = "ARINC429 CH6";

            _resistorGear = "1挡";
            ResistorGearValueText = _resistorGear;
            TemperatureTelemetryValueText = "--";
            LastTestTime = "--";
            LastTestResult = "--";

            SendEnterAtpCommand = new DelegateCommand(() => AddLog($"[{DateTime.Now:HH:mm:ss}] 发送：进入ATP"));
            SendSetControllerResistorCommand = new DelegateCommand(() => AddLog($"[{DateTime.Now:HH:mm:ss}] 发送：接入电阻，档位={ResistorGear}"));
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
        private string _temperatureTelemetryValueText;
        private string _lastTestTime;
        private string _lastTestResult;

        private bool _isManualTestRunning;
        private bool _isAutoTestRunning;

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
                if (SetProperty(ref _isAutoTestRunning, value) && value)
                {
                    IsManualTestRunning = false;
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
    }
}
