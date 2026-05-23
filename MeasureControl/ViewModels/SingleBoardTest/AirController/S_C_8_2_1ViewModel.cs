using Prism.Commands;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace MeasureControl.ViewModels.SingleBoardTest.AirController
{
    public class S_C_8_2_1ViewModel : AirSimpleSequenceViewModel
    {
        private CancellationTokenSource _testCts;
        private bool _isManualTestRunning;
        private bool _isAutoTestRunning;
        private bool? _test32vPassed;
        private bool? _test28vPassed;

        public S_C_8_2_1ViewModel()
        {
            Title = "8.2.1电源模块测试";
            ManualPowerOffCommand = new DelegateCommand(async () => await OnManualPowerOffAsync());
            AutoSequenceCommand = new DelegateCommand(async () => await OnAutoSequenceAsync());
            Test32VCommand = new DelegateCommand(async () => await OnSingleVoltageTestAsync(32.0, 0.94));
            Test28VCommand = new DelegateCommand(async () => await OnSingleVoltageTestAsync(28.0, 1.07));
        }

        public new DelegateCommand ManualTestCommand => ManualPowerOffCommand;

        public new DelegateCommand AutoTestCommand => AutoSequenceCommand;

        public DelegateCommand ManualPowerOffCommand { get; }

        public DelegateCommand AutoSequenceCommand { get; }

        public DelegateCommand Test32VCommand { get; }

        public DelegateCommand Test28VCommand { get; }

        public new bool IsManualTestRunning
        {
            get => _isManualTestRunning;
            private set => SetProperty(ref _isManualTestRunning, value);
        }

        public new bool IsAutoTestRunning
        {
            get => _isAutoTestRunning;
            private set => SetProperty(ref _isAutoTestRunning, value);
        }

        private async Task OnManualPowerOffAsync()
        {
            if (IsManualTestRunning)
                return;

            IsManualTestRunning = true;
            try
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试：确保产品全部下电");
                try { _testCts?.Cancel(); } catch { }
                await CleanupHardwareAfterTestAsync();
                _test32vPassed = null;
                _test28vPassed = null;
                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                LastTestResult = "已下电";
                AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试：产品已全部下电");
            }
            catch (Exception ex)
            {
                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                LastTestResult = "异常";
                AddLog($"[{DateTime.Now:HH:mm:ss}] 手动下电异常：{ex.Message}");
            }
            finally
            {
                IsManualTestRunning = false;
            }
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

        private async Task OnSingleVoltageTestAsync(double voltage, double currentUpperLimit)
        {
            if (IsAutoTestRunning || IsManualTestRunning)
                return;

            IsAutoTestRunning = true;
            _testCts?.Dispose();
            _testCts = new CancellationTokenSource();

            try
            {
                var passed = await RunSingleSupplyVoltageTestAsync(voltage, currentUpperLimit, _testCts.Token);
                if (voltage >= 31.0)
                    _test32vPassed = passed;
                else
                    _test28vPassed = passed;

                RefreshOverallResult();
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
