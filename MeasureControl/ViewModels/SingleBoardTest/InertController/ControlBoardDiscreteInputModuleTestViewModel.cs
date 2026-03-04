using Prism.Commands;
using Prism.Mvvm;
using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;

namespace MeasureControl.ViewModels.SingleBoardTest.InertController
{
    public class ControlBoardDiscreteInputModuleTestViewModel : BindableBase, IDisposable
    {
        private readonly SemaphoreSlim _opLock = new SemaphoreSlim(1, 1);
        private CancellationTokenSource _cts;

        private bool _isManualTestRunning;
        private bool _isAutoTestRunning;
        private string _lastTestTime = "--";
        private string _lastTestResult = "--";

        public ControlBoardDiscreteInputModuleTestViewModel()
        {
            ManualTestCommand = new DelegateCommand(OnManualTest);
            AutoTestCommand = new DelegateCommand(async () => await OnAutoTestAsync());
            ClearLogCommand = new DelegateCommand(() => Logs.Clear());

            Items = new ObservableCollection<DiscreteInputItemViewModel>
            {
                new DiscreteInputItemViewModel(
                    "a) J40-J45、J75-J83",
                    "J40-J45、J75-J83",
                    new []{ "GND", "开路" },
                    "GND",
                    this,
                    "a"),
                new DiscreteInputItemViewModel(
                    "a) J40-J45、J75-J83",
                    "J40-J45、J75-J83",
                    new []{ "GND", "开路" },
                    "开路",
                    this,
                    "b"),
                new DiscreteInputItemViewModel(
                    "b) J84、J85",
                    "J84、J85",
                    new []{ "28V", "开路" },
                    "28V",
                    this,
                    "c"),
                new DiscreteInputItemViewModel(
                    "b) J84、J85",
                    "J84、J85",
                    new []{ "28V", "开路" },
                    "开路",
                    this,
                    "d"),
            };
        }

        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();

        public ObservableCollection<DiscreteInputItemViewModel> Items { get; }

        public DelegateCommand ManualTestCommand { get; }
        public DelegateCommand AutoTestCommand { get; }
        public DelegateCommand ClearLogCommand { get; }

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

        private void OnManualTest()
        {
            if (IsManualTestRunning)
            {
                _ = StopAsync();
                return;
            }

            _ = StartAsync();
        }

        private async Task StartAsync()
        {
            await _opLock.WaitAsync();
            try
            {
                if (IsManualTestRunning)
                    return;

                IsManualTestRunning = true;
                LastTestTime = "--";
                LastTestResult = "--";

                _cts?.Cancel();
                _cts?.Dispose();
                _cts = new CancellationTokenSource();

                AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试启动：离散输入模块测试（占位模式：未接通信采集）");
            }
            finally
            {
                _opLock.Release();
            }
        }

        private async Task StopAsync()
        {
            await _opLock.WaitAsync();
            try
            {
                if (!IsManualTestRunning)
                    return;

                try { _cts?.Cancel(); } catch { }

                IsManualTestRunning = false;
                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                AddLog($"[{DateTime.Now:HH:mm:ss}] 手动测试停止");
            }
            finally
            {
                _opLock.Release();
            }
        }

        private async Task OnAutoTestAsync()
        {
            await _opLock.WaitAsync();
            try
            {
                if (IsAutoTestRunning)
                    return;

                IsAutoTestRunning = true;
                LastTestTime = "--";
                LastTestResult = "--";

                AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试开始（占位模式）：将依次执行表 7-1 的配置与采集检查");

                foreach (var item in Items)
                {
                    await item.MeasureAsync();
                }

                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                LastTestResult = "--";
                AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试结束（占位模式）：通信采集未接入，未生成 PASS/FAIL");
            }
            catch (Exception ex)
            {
                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                LastTestResult = "FAIL";
                AddLog($"[{DateTime.Now:HH:mm:ss}] 自动测试异常：{ex.Message}");
            }
            finally
            {
                IsAutoTestRunning = false;
                _opLock.Release();
            }
        }

        internal void AddLog(string msg)
        {
            if (string.IsNullOrWhiteSpace(msg))
                return;

            Logs.Add(msg);
        }

        public void Dispose()
        {
            try { _cts?.Cancel(); } catch { }
            try { _cts?.Dispose(); } catch { }
            _opLock?.Dispose();
        }

        public class DiscreteInputItemViewModel : BindableBase
        {
            private readonly ControlBoardDiscreteInputModuleTestViewModel _owner;

            private string _selectedConfigState;
            private string _actualResult = "--";
            private string _result = "--";

            public DiscreteInputItemViewModel(
                string groupName,
                string pins,
                string[] configStateOptions,
                string defaultConfigState,
                ControlBoardDiscreteInputModuleTestViewModel owner,
                string indexText)
            {
                GroupName = groupName;
                Pins = pins;
                IndexText = indexText;

                ConfigStateOptions = new ObservableCollection<string>(configStateOptions ?? Array.Empty<string>());
                SelectedConfigState = defaultConfigState;
                ExpectedResult = defaultConfigState;

                _owner = owner;

                MeasureCommand = new DelegateCommand(async () => await MeasureAsync());
            }

            public string IndexText { get; }

            public string GroupName { get; }

            public string Pins { get; }

            public ObservableCollection<string> ConfigStateOptions { get; }

            public string SelectedConfigState
            {
                get => _selectedConfigState;
                set
                {
                    if (SetProperty(ref _selectedConfigState, value))
                    {
                        ExpectedResult = value;
                    }
                }
            }

            private string _expectedResult;
            public string ExpectedResult
            {
                get => _expectedResult;
                private set => SetProperty(ref _expectedResult, value);
            }

            public string ActualResult
            {
                get => _actualResult;
                set => SetProperty(ref _actualResult, value);
            }

            public string Result
            {
                get => _result;
                set => SetProperty(ref _result, value);
            }

            public DelegateCommand MeasureCommand { get; }

            public async Task MeasureAsync()
            {
                await Task.Yield();

                ActualResult = "--";
                Result = "--";

                _owner?.AddLog($"[{DateTime.Now:HH:mm:ss}] 采集占位：{Pins} 配置={SelectedConfigState}，需通过通信读取采集结果（待接入）");
            }
        }
    }
}
