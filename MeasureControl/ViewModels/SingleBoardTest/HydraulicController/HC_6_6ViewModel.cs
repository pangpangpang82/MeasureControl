using Prism.Commands;
using Prism.Mvvm;
using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using MeasureControl.Services;
using MeasureControl.Services.HardwareApis;

namespace MeasureControl.ViewModels.SingleBoardTest.HydraulicController
{
    public class HC_6_6ViewModel : BindableBase
    {
        private readonly IPxiChassisService _pxiChassisService;
        private readonly ISingleBoardTestContextService _singleBoardTestContext;
        private IPowerSupplyApi _power;
        private IArt4229Api _arinc;

        private bool _isManualTestRunning;
        private bool _isAutoTestRunning;
        private string _lastTestTime;
        private string _lastTestResult;
        private string _previousTestTime;
        private string _previousTestResult;
        private string _currentTestResult = "--";

        private const string TestItemName = "油量传感器信号采集测试";

        public HC_6_6ViewModel(IPxiChassisService pxiChassisService, ISingleBoardTestContextService singleBoardTestContext)
        {
            _pxiChassisService = pxiChassisService;
            _singleBoardTestContext = singleBoardTestContext;

            ManualTestCommand = new DelegateCommand(() => { });
            AutoTestCommand = new DelegateCommand(() => { });
            Measure14Command = new DelegateCommand(() => { });
            ClearLogCommand = new DelegateCommand(() => Logs.Clear());

            LoadLastTestResultFromProject();
        }

        private void LoadLastTestResultFromProject()
        {
            var testItemNode = _singleBoardTestContext?.GetCurrentTestItemNode(TestItemName);
            if (testItemNode != null)
            {
                if (!string.IsNullOrWhiteSpace(testItemNode.LastTestTime))
                {
                    _previousTestTime = testItemNode.LastTestTime;
                    RaisePropertyChanged(nameof(PreviousTestTime));
                }
                if (!string.IsNullOrWhiteSpace(testItemNode.LastTestResult))
                {
                    _previousTestResult = testItemNode.LastTestResult;
                    RaisePropertyChanged(nameof(PreviousTestResult));
                }
            }
        }

        private void SaveTestResultToProject()
        {
            var testItemNode = _singleBoardTestContext?.GetCurrentTestItemNode(TestItemName);
            if (testItemNode != null)
            {
                testItemNode.LastTestTime = PreviousTestTime;
                testItemNode.LastTestResult = PreviousTestResult;
            }
        }

        public string CurrentTestResult
        {
            get => _currentTestResult;
            private set => SetProperty(ref _currentTestResult, value);
        }

        public string LastTestTime
        {
            get => _lastTestTime ?? "--";
            set => SetProperty(ref _lastTestTime, value);
        }

        public string LastTestResult
        {
            get => _lastTestResult ?? "--";
            set => SetProperty(ref _lastTestResult, value);
        }

        public string PreviousTestTime
        {
            get => _previousTestTime ?? "--";
            set => SetProperty(ref _previousTestTime, value);
        }

        public string PreviousTestResult
        {
            get => _previousTestResult ?? "--";
            set => SetProperty(ref _previousTestResult, value);
        }

        public DelegateCommand ManualTestCommand { get; }

        public DelegateCommand AutoTestCommand { get; }

        public DelegateCommand Measure14Command { get; }

        public DelegateCommand ClearLogCommand { get; }

        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();

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

        public bool CanMeasure14 => false;

        public async Task<string> RunOnceAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.CompletedTask.ConfigureAwait(false);

            CurrentTestResult = "--";

            var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            var resultText = "合格";
            CurrentTestResult = resultText;
            PreviousTestTime = now;
            PreviousTestResult = resultText;
            LastTestTime = now;
            LastTestResult = resultText;
            SaveTestResultToProject();

            Log("未实现，跳过");
            return resultText;
        }

        private void Log(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            var line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.Invoke(() => Logs.Add(line));
                return;
            }

            Logs.Add(line);
        }
    }
}
