using Prism.Commands;
using Prism.Mvvm;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using MeasureControl.Services;

namespace MeasureControl.ViewModels.SingleBoardTest.InertController
{
    public sealed class PressureSensorSignalAcquisitionTestViewModel : BindableBase, IDisposable
    {
        private readonly ISingleBoardTestContextService _singleBoardTestContext;
        private readonly SynchronizationContext _uiContext;

        private readonly SemaphoreSlim _opLock = new SemaphoreSlim(1, 1);
        private CancellationTokenSource _cts;

        private bool _isManualTestRunning;
        private bool _isAutoTestRunning;
        private string _lastTestTime = "--";
        private string _lastTestResult = "--";

        private int _selectedPointIndex;

        public PressureSensorSignalAcquisitionTestViewModel(
            ISingleBoardTestContextService singleBoardTestContext,
            Prism.Events.IEventAggregator eventAggregator)
        {
            _singleBoardTestContext = singleBoardTestContext;
            _uiContext = SynchronizationContext.Current;

            ManualTestCommand = new DelegateCommand(async () => await OnManualTestAsync());
            AutoTestCommand = new DelegateCommand(async () => await OnAutoTestAsync());
            ClearLogCommand = new DelegateCommand(() => Logs.Clear());

            SetPoint1Command = new DelegateCommand(async () => await ApplyPointAsync(1));
            SetPoint2Command = new DelegateCommand(async () => await ApplyPointAsync(2));
            SetPoint3Command = new DelegateCommand(async () => await ApplyPointAsync(3));
            ApplySelectedPointCommand = new DelegateCommand(async () => await ApplyPointAsync(SelectedPointIndex));

            SignalItems.Add(new PressureSignalItemViewModel("压力传感器", "J25、J26"));

            SelectedPointIndex = 1;
        }

        private void PostToUi(Action action)
        {
            if (action == null)
                return;

            if (_uiContext != null && !ReferenceEquals(SynchronizationContext.Current, _uiContext))
            {
                _uiContext.Post(_ =>
                {
                    try { action(); } catch { }
                }, null);
                return;
            }

            action();
        }

        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();

        public ObservableCollection<PressureSignalItemViewModel> SignalItems { get; } = new ObservableCollection<PressureSignalItemViewModel>();

        public DelegateCommand ManualTestCommand { get; }
        public DelegateCommand AutoTestCommand { get; }
        public DelegateCommand ClearLogCommand { get; }

        public DelegateCommand SetPoint1Command { get; }
        public DelegateCommand SetPoint2Command { get; }
        public DelegateCommand SetPoint3Command { get; }
        public DelegateCommand ApplySelectedPointCommand { get; }

        public bool IsManualTestRunning
        {
            get => _isManualTestRunning;
            private set => SetProperty(ref _isManualTestRunning, value);
        }

        public bool IsAutoTestRunning
        {
            get => _isAutoTestRunning;
            private set => SetProperty(ref _isAutoTestRunning, value);
        }

        public string LastTestTime
        {
            get => _lastTestTime;
            private set => SetProperty(ref _lastTestTime, value);
        }

        public string LastTestResult
        {
            get => _lastTestResult;
            private set => SetProperty(ref _lastTestResult, value);
        }

        public int SelectedPointIndex
        {
            get => _selectedPointIndex;
            set
            {
                if (SetProperty(ref _selectedPointIndex, value))
                {
                    UpdatePreviewVoltage();
                }
            }
        }

        private async Task OnManualTestAsync()
        {
            if (IsManualTestRunning)
            {
                await StopAsync().ConfigureAwait(false);
                return;
            }

            if (IsAutoTestRunning)
            {
                await StopAsync().ConfigureAwait(false);
            }

            await _opLock.WaitAsync().ConfigureAwait(false);
            try
            {
                _cts?.Cancel();
                _cts?.Dispose();
                _cts = new CancellationTokenSource();

                IsManualTestRunning = true;
                IsAutoTestRunning = false;
                LastTestTime = "--";
                LastTestResult = "--";

                Log("开始手动测试（压力传感器信号采集）：模拟电压输出/矩阵开关/通讯采集暂未接入");
                UpdatePreviewVoltage();
            }
            catch (OperationCanceledException)
            {
                await StopAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log($"手动测试初始化异常：{ex.Message}");
                await StopAsync().ConfigureAwait(false);
            }
            finally
            {
                _opLock.Release();
            }
        }

        private async Task OnAutoTestAsync()
        {
            if (IsAutoTestRunning)
            {
                await StopAsync().ConfigureAwait(false);
                return;
            }

            if (IsManualTestRunning)
            {
                await StopAsync().ConfigureAwait(false);
            }

            await _opLock.WaitAsync().ConfigureAwait(false);
            try
            {
                _cts?.Cancel();
                _cts?.Dispose();
                _cts = new CancellationTokenSource();

                IsAutoTestRunning = true;
                IsManualTestRunning = false;
                LastTestTime = "--";
                LastTestResult = "--";

                Log("开始自动测试（占位）：将依次设置点位1~3的模拟电压。通讯采集暂不执行。");

                for (int i = 1; i <= 3; i++)
                {
                    _cts.Token.ThrowIfCancellationRequested();
                    await ApplyPointInternalAsync(i, _cts.Token).ConfigureAwait(false);
                    await Task.Delay(200, _cts.Token).ConfigureAwait(false);
                }

                LastTestResult = "PASS";
                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                Log("自动测试结束（占位）：未执行通讯采集与判据判定。");
            }
            catch (OperationCanceledException)
            {
                Log("自动测试已取消");
            }
            catch (Exception ex)
            {
                LastTestResult = "FAIL";
                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                Log($"自动测试异常：{ex.Message}");
            }
            finally
            {
                IsAutoTestRunning = false;
                _opLock.Release();
            }
        }

        private async Task StopAsync()
        {
            try { _cts?.Cancel(); } catch { }
            IsManualTestRunning = false;
            IsAutoTestRunning = false;
            await Task.CompletedTask;
        }

        private async Task ApplyPointAsync(int pointIndex)
        {
            if (!IsManualTestRunning && !IsAutoTestRunning)
            {
                Log("请先点击“手动测试”连接流程（当前输出与采集功能占位）");
                return;
            }

            await _opLock.WaitAsync().ConfigureAwait(false);
            try
            {
                var token = _cts?.Token ?? CancellationToken.None;
                await ApplyPointInternalAsync(pointIndex, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Log("设置点位已取消");
            }
            catch (Exception ex)
            {
                Log($"设置点位异常：{ex.Message}");
            }
            finally
            {
                _opLock.Release();
            }
        }

        private async Task ApplyPointInternalAsync(int pointIndex, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            var v = GetVoltageForPoint(pointIndex);
            PostToUi(() =>
            {
                foreach (var item in SignalItems)
                {
                    item.TargetVoltageV = v;
                }
            });

            Log($"设置点位{pointIndex}：目标电压={v.ToString("F3", CultureInfo.InvariantCulture)}V（模拟电压输出/矩阵开关/通讯采集暂未接入）");
            await Task.CompletedTask;
        }

        private void UpdatePreviewVoltage()
        {
            var v = GetVoltageForPoint(SelectedPointIndex);
            PostToUi(() =>
            {
                foreach (var item in SignalItems)
                {
                    item.TargetVoltageV = v;
                }
            });
        }

        private static double GetVoltageForPoint(int pointIndex)
        {
            switch (pointIndex)
            {
                case 1:
                    return 0.5;
                case 2:
                    return 5.6;
                case 3:
                    return 9.0;
                default:
                    return 0.0;
            }
        }

        private void Log(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            PostToUi(() => Logs.Add($"[{DateTime.Now:HH:mm:ss}] {message}"));
        }

        public void Dispose()
        {
            try { _cts?.Cancel(); } catch { }
            try { _cts?.Dispose(); } catch { }
            try { _opLock?.Dispose(); } catch { }
        }

        public sealed class PressureSignalItemViewModel : BindableBase
        {
            private double _targetVoltageV;

            public PressureSignalItemViewModel(string signalName, string pins)
            {
                SignalName = signalName;
                Pins = pins;
            }

            public string SignalName { get; }

            public string Pins { get; }

            public double TargetVoltageV
            {
                get => _targetVoltageV;
                set => SetProperty(ref _targetVoltageV, value);
            }
        }
    }
}
