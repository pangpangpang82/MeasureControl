using Prism.Commands;
using Prism.Mvvm;
using System;

namespace MeasureControl.ViewModels.SingleBoardTest.HydraulicController
{
    public class HC_6_2ViewModel : BindableBase
    {
        private bool _isManualTestRunning;
        private bool _isAutoTestRunning;

        private string _lastTestTime = "--";
        private string _lastTestResult = "--";
        private string _previousTestTime = "--";
        private string _previousTestResult = "--";

        public HC_6_2ViewModel()
        {
            ManualTestCommand = new DelegateCommand(OnManualTest);
            AutoTestCommand = new DelegateCommand(OnAutoTest);
        }

        public DelegateCommand ManualTestCommand { get; }
        public DelegateCommand AutoTestCommand { get; }

        public bool IsManualTestRunning
        {
            get => _isManualTestRunning;
            set => SetProperty(ref _isManualTestRunning, value);
        }

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

        public string PreviousTestTime
        {
            get => _previousTestTime;
            set => SetProperty(ref _previousTestTime, value);
        }

        public string PreviousTestResult
        {
            get => _previousTestResult;
            set => SetProperty(ref _previousTestResult, value);
        }

        private void OnManualTest()
        {
            if (IsManualTestRunning)
            {
                IsManualTestRunning = false;
                return;
            }

            IsAutoTestRunning = false;
            IsManualTestRunning = true;
            PreviousTestTime = LastTestTime;
            PreviousTestResult = LastTestResult;
            LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            LastTestResult = "--";
        }

        private void OnAutoTest()
        {
            if (IsAutoTestRunning)
            {
                IsAutoTestRunning = false;
                return;
            }

            IsManualTestRunning = false;
            IsAutoTestRunning = true;
            PreviousTestTime = LastTestTime;
            PreviousTestResult = LastTestResult;
            LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            LastTestResult = "--";
        }
    }
}
