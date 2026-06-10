using Prism.Commands;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace MeasureControl.ViewModels.SingleBoardTest.AirController
{
    public class S_C_8_2_1ViewModel : AirSimpleSequenceViewModel
    {
        private CancellationTokenSource _testCts;
        private bool _isAutoTestRunning;
        private bool? _test32vPassed;
        private bool? _test28vPassed;

        public S_C_8_2_1ViewModel()
        {
            Title = "8.2.1电源模块测试";
            KeepPowerOnAfterTest = true;
            AutoSequenceCommand = new DelegateCommand(async () => await OnAutoSequenceAsync());
        }

        public new DelegateCommand AutoTestCommand => AutoSequenceCommand;

        public DelegateCommand AutoSequenceCommand { get; }

        public new bool IsAutoTestRunning
        {
            get => _isAutoTestRunning;
            private set => SetProperty(ref _isAutoTestRunning, value);
        }

        private async Task OnAutoSequenceAsync()
        {
            if (IsAutoTestRunning)
            {
                try { _testCts?.Cancel(); } catch { }
                return;
            }

            IsAutoTestRunning = true;
            _testCts?.Dispose();
            _testCts = new CancellationTokenSource();

            try
            {
                await RunAutoTestAsync(_testCts.Token);
                _test32vPassed = LastTestResult == "PASS";
                _test28vPassed = LastTestResult == "PASS";
            }
            finally
            {
                IsAutoTestRunning = false;
            }
        }

        private void RefreshOverallResult()
        {
            LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            if (_test32vPassed == true && _test28vPassed == true)
            {
                LastTestResult = "PASS";
                AddLog($"[{DateTime.Now:HH:mm:ss}] 8.2.1汇总：32V和28V电压/电流均合格，PASS");
                return;
            }

            if (_test32vPassed == false || _test28vPassed == false)
            {
                LastTestResult = "FAIL";
                AddLog($"[{DateTime.Now:HH:mm:ss}] 8.2.1汇总：存在电压或电流不合格，FAIL");
                return;
            }

            LastTestResult = "未完成";
            AddLog($"[{DateTime.Now:HH:mm:ss}] 8.2.1汇总：需完成32V和28V两组测试后才可判定PASS");
        }
    }
}
