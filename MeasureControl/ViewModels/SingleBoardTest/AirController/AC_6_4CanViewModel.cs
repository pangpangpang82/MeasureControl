using Prism.Commands;
using Prism.Mvvm;
using System;
using System.Collections.ObjectModel;

namespace MeasureControl.ViewModels.SingleBoardTest.AirController
{
    public class AC_6_4CanViewModel : BindableBase
    {
        public AC_6_4CanViewModel()
        {
            _enterAtpTxChannel = "CAN CH0";
            _enterAtpRxChannel = "CAN CH1";
            _setVoltageTxChannel = "CAN CH2";
            _setVoltageRxChannel = "CAN CH3";
            _telemetryRxChannel = "CAN CH4";
            _exitAtpTxChannel = "CAN CH5";
            _exitAtpRxChannel = "CAN CH6";

            _dmmChannel = "Port1";

            DmmVoltageText = "--";
            TelemetryVoltageText = "--";
            EnterAtpRxDataText = "--";
            TelemetryRxDataText = "--";
            ExitAtpRxDataText = "--";
            LastTestTime = "--";
            LastTestResult = "--";

            ManualTestCommand = new DelegateCommand(OnManualTest);
            AutoTestCommand = new DelegateCommand(OnAutoTest);

            SendEnterAtpCommand = new DelegateCommand(() => AddLog($"[{DateTime.Now:HH:mm:ss}] 发送：进入ATP"));
            SendSetVoltageCommand = new DelegateCommand(() => AddLog($"[{DateTime.Now:HH:mm:ss}] 发送：控制器输出电压"));
            SendExitAtpCommand = new DelegateCommand(() => AddLog($"[{DateTime.Now:HH:mm:ss}] 发送：退出ATP"));

            ClearLogCommand = new DelegateCommand(() => Logs.Clear());
        }

        private string _enterAtpTxChannel;
        private string _enterAtpRxChannel;
        private string _setVoltageTxChannel;
        private string _setVoltageRxChannel;
        private string _telemetryRxChannel;
        private string _exitAtpTxChannel;
        private string _exitAtpRxChannel;

        private string _dmmChannel;

        private bool _isManualTestRunning;
        private bool _isAutoTestRunning;
        private string _dmmVoltageText;
        private string _telemetryVoltageText;
        private string _enterAtpRxDataText;
        private string _telemetryRxDataText;
        private string _exitAtpRxDataText;
        private string _lastTestTime;
        private string _lastTestResult;

        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();

        public DelegateCommand ManualTestCommand { get; }
        public DelegateCommand AutoTestCommand { get; }

        public DelegateCommand SendEnterAtpCommand { get; }
        public DelegateCommand SendSetVoltageCommand { get; }
        public DelegateCommand SendExitAtpCommand { get; }

        public DelegateCommand ClearLogCommand { get; }

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

        public string SetVoltageTxChannel
        {
            get => _setVoltageTxChannel;
            set => SetProperty(ref _setVoltageTxChannel, value);
        }

        public string SetVoltageRxChannel
        {
            get => _setVoltageRxChannel;
            set => SetProperty(ref _setVoltageRxChannel, value);
        }

        public string TelemetryRxChannel
        {
            get => _telemetryRxChannel;
            set => SetProperty(ref _telemetryRxChannel, value);
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

        public string DmmChannel
        {
            get => _dmmChannel;
            set => SetProperty(ref _dmmChannel, value);
        }

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

        public string DmmVoltageText
        {
            get => _dmmVoltageText;
            set => SetProperty(ref _dmmVoltageText, value);
        }

        public string TelemetryVoltageText
        {
            get => _telemetryVoltageText;
            set => SetProperty(ref _telemetryVoltageText, value);
        }

        public string EnterAtpRxDataText
        {
            get => _enterAtpRxDataText;
            set => SetProperty(ref _enterAtpRxDataText, value);
        }

        public string TelemetryRxDataText
        {
            get => _telemetryRxDataText;
            set => SetProperty(ref _telemetryRxDataText, value);
        }

        public string ExitAtpRxDataText
        {
            get => _exitAtpRxDataText;
            set => SetProperty(ref _exitAtpRxDataText, value);
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
