using Prism.Commands;
using Prism.Mvvm;
using System;
using System.Collections.ObjectModel;

namespace MeasureControl.ViewModels.SingleBoardTest.AirController
{
    public class AirSimpleSequenceViewModel : BindableBase
    {
        private string _title = "测试";
        private bool _isManualTestRunning;
        private bool _isAutoTestRunning;
        private string _lastTestTime = "--";
        private string _lastTestResult = "--";

        private string _enterAtpTxChannel;
        private string _enterAtpRxChannel;
        private string _setVoltageTxChannel;
        private string _dmmChannel;
        private string _telemetryRxChannel;
        private string _exitAtpTxChannel;
        private string _exitAtpRxChannel;

        private string _dmmVoltageText;
        private string _telemetryVoltageText;
        private string _enterAtpRxDataText;
        private string _telemetryRxDataText;
        private string _exitAtpRxDataText;

        public AirSimpleSequenceViewModel()
        {
            _enterAtpTxChannel = "429_CH0";
            _enterAtpRxChannel = "429_CH1";
            _setVoltageTxChannel = "429_CH2";
            _telemetryRxChannel = "429_CH4";
            _exitAtpTxChannel = "429_CH5";
            _exitAtpRxChannel = "429_CH6";
            _dmmChannel = "Port1";

            DmmVoltageText = "--";
            TelemetryVoltageText = "--";
            EnterAtpRxDataText = "--";
            TelemetryRxDataText = "--";
            ExitAtpRxDataText = "--";

            ManualTestCommand = new DelegateCommand(OnManualTest);
            AutoTestCommand = new DelegateCommand(OnAutoTest);
            ClearLogCommand = new DelegateCommand(() => Logs.Clear());
            StepActionCommand = new DelegateCommand<string>(OnStepAction);

            SendEnterAtpCommand = new DelegateCommand(OnSendEnterAtp);
            SendSetVoltageCommand = new DelegateCommand(OnSendSetVoltage);
            SendExitAtpCommand = new DelegateCommand(OnSendExitAtp);
        }

        public string Title
        {
            get => _title;
            set => SetProperty(ref _title, value);
        }

        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();

        public DelegateCommand ManualTestCommand { get; }
        public DelegateCommand AutoTestCommand { get; }
        public DelegateCommand ClearLogCommand { get; }
        public DelegateCommand<string> StepActionCommand { get; }

        public DelegateCommand SendEnterAtpCommand { get; }
        public DelegateCommand SendSetVoltageCommand { get; }
        public DelegateCommand SendExitAtpCommand { get; }

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

        public string DmmChannel
        {
            get => _dmmChannel;
            set => SetProperty(ref _dmmChannel, value);
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

        private void OnStepAction(string step)
        {
            if (string.IsNullOrWhiteSpace(step))
            {
                step = "步骤";
            }

            AddLog($"[{DateTime.Now:HH:mm:ss}] 执行：{step}");
        }

        private void OnSendEnterAtp()
        {
            AddLog($"[{DateTime.Now:HH:mm:ss}] 发送：进入ATP (TX={EnterAtpTxChannel}, RX={EnterAtpRxChannel})");
            EnterAtpRxDataText = "--";
        }

        private void OnSendSetVoltage()
        {
            AddLog($"[{DateTime.Now:HH:mm:ss}] 发送：控制器输出电压 (TX={SetVoltageTxChannel})");
            DmmVoltageText = "--";
        }

        private void OnSendExitAtp()
        {
            AddLog($"[{DateTime.Now:HH:mm:ss}] 发送：退出ATP (TX={ExitAtpTxChannel}, RX={ExitAtpRxChannel})");
            ExitAtpRxDataText = "--";
        }

        private void AddLog(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

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
