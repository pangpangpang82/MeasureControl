using Prism.Commands;
using Prism.Mvvm;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MeasureControl.Models.Devices;
using MeasureControl.Services.HardwareApis;
using MeasureControl.Services;

namespace MeasureControl.ViewModels.SingleBoardTest.InertController
{
    public sealed class TemperatureSensorSignalAcquisitionTestViewModel : BindableBase, IDisposable
    {
        private const string TestItemKey = "InertController_TemperatureSensorSignalAcquisition";

        private readonly ISingleBoardTestContextService _singleBoardTestContext;
        private readonly SynchronizationContext _uiContext;

        private readonly SemaphoreSlim _opLock = new SemaphoreSlim(1, 1);
        private CancellationTokenSource _cts;

        private bool _isManualTestRunning;
        private bool _isAutoTestRunning;
        private string _lastTestTime = "--";
        private string _lastTestResult = "--";

        private IPxi7012Api _resistor;
        private uint? _connectedLogicalId;

        public TemperatureSensorSignalAcquisitionTestViewModel(
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
            SetPoint4Command = new DelegateCommand(async () => await ApplyPointAsync(4));
            ApplySelectedPointCommand = new DelegateCommand(async () => await ApplyPointAsync(SelectedPointIndex));

            SensorItems.Add(new SensorItemViewModel("PT500A", "J52、J53、J56", "RO1"));
            SensorItems.Add(new SensorItemViewModel("PT500B", "J61、J62、J63", "RO2"));
            SensorItems.Add(new SensorItemViewModel("PT1000A", "J54、J55、J57", "RO3"));
            SensorItems.Add(new SensorItemViewModel("PT1000B", "J28、J29、J97", "RO4"));

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

        public ObservableCollection<SensorItemViewModel> SensorItems { get; } = new ObservableCollection<SensorItemViewModel>();

        public DelegateCommand ManualTestCommand { get; }
        public DelegateCommand AutoTestCommand { get; }
        public DelegateCommand ClearLogCommand { get; }

        public DelegateCommand SetPoint1Command { get; }
        public DelegateCommand SetPoint2Command { get; }
        public DelegateCommand SetPoint3Command { get; }
        public DelegateCommand SetPoint4Command { get; }
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

        private int _selectedPointIndex;
        public int SelectedPointIndex
        {
            get => _selectedPointIndex;
            set
            {
                if (SetProperty(ref _selectedPointIndex, value))
                {
                    UpdatePreviewResistances();
                }
            }
        }

        private string _connectionText = "7012: 未连接";
        public string ConnectionText
        {
            get => _connectionText;
            private set => SetProperty(ref _connectionText, value);
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

                Log("开始手动测试（温度传感器信号采集）：准备连接7012电阻输出板卡");

                var ok = await EnsureResistorReadyAsync(_cts.Token).ConfigureAwait(false);
                if (!ok)
                {
                    Log("7012连接失败：请检查板卡/驱动/逻辑ID");
                    await StopAsync().ConfigureAwait(false);
                    return;
                }

                UpdatePreviewResistances();
                Log("7012已就绪：可执行电阻设置（矩阵开关/通讯采集暂未接入）");
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

                Log("开始自动测试（占位）：将按表7-2依次设置点位1~4的电阻值。通讯采集暂不执行。");

                var ok = await EnsureResistorReadyAsync(_cts.Token).ConfigureAwait(false);
                if (!ok)
                {
                    Log("7012连接失败：自动测试终止");
                    LastTestResult = "FAIL";
                    LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    return;
                }

                for (var i = 1; i <= 4; i++)
                {
                    _cts.Token.ThrowIfCancellationRequested();
                    await ApplyPointInternalAsync(i, _cts.Token).ConfigureAwait(false);
                }

                LastTestResult = "--";
                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                Log("自动测试结束（占位）：电阻输出已执行，通讯采集与判定待接入。");
            }
            catch (OperationCanceledException)
            {
                Log("自动测试取消");
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
            await _opLock.WaitAsync().ConfigureAwait(false);
            try
            {
                try { _cts?.Cancel(); } catch { }

                if (_resistor != null)
                {
                    try
                    {
                        await _resistor.DisconnectAsync().ConfigureAwait(false);
                    }
                    catch
                    {
                    }

                    try
                    {
                        await _resistor.DisposeAsync().ConfigureAwait(false);
                    }
                    catch
                    {
                    }

                    _resistor = null;
                }

                _connectedLogicalId = null;
                ConnectionText = "7012: 未连接";

                IsManualTestRunning = false;
                IsAutoTestRunning = false;
                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            }
            finally
            {
                _opLock.Release();
            }
        }

        private async Task ApplyPointAsync(int pointIndex)
        {
            if (pointIndex < 1 || pointIndex > 4)
                return;

            if (!IsManualTestRunning && !IsAutoTestRunning)
            {
                Log("请先启动手动测试，以连接7012板卡。");
                return;
            }

            await _opLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_cts == null)
                    _cts = new CancellationTokenSource();

                var ok = await EnsureResistorReadyAsync(_cts.Token).ConfigureAwait(false);
                if (!ok)
                {
                    Log("7012未就绪：无法下发电阻值");
                    return;
                }

                await ApplyPointInternalAsync(pointIndex, _cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Log($"设置点位{pointIndex}异常：{ex.Message}");
            }
            finally
            {
                _opLock.Release();
            }
        }

        private async Task ApplyPointInternalAsync(int pointIndex, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            var targets = GetTargetsForPoint(pointIndex);
            PostToUi(() =>
            {
                foreach (var item in SensorItems)
                {
                    if (targets.TryGetValue(item.SensorName, out var ohm))
                    {
                        item.TargetResistanceOhm = ohm;
                    }
                }
            });

            var roToOhm = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["RO1"] = targets["PT500A"],
                ["RO2"] = targets["PT500B"],
                ["RO3"] = targets["PT1000A"],
                ["RO4"] = targets["PT1000B"],
            };

            Log($"下发点位{pointIndex}：PT500A={targets["PT500A"].ToString("F2", CultureInfo.InvariantCulture)}Ω, PT500B={targets["PT500B"].ToString("F2", CultureInfo.InvariantCulture)}Ω, PT1000A={targets["PT1000A"].ToString("F1", CultureInfo.InvariantCulture)}Ω, PT1000B={targets["PT1000B"].ToString("F1", CultureInfo.InvariantCulture)}Ω");

            await _resistor.SetResistancesAsync(roToOhm, Pxi7012OutputMode.NoWait, token).ConfigureAwait(false);

            foreach (var ch in new[] { "RO1", "RO2", "RO3", "RO4" })
            {
                try
                {
                    await _resistor.SetRelayStateAsync(ch, pathRelayClosed: true, shortCircuitClosed: false, token).ConfigureAwait(false);
                }
                catch
                {
                }
            }

            Log($"点位{pointIndex}设置完成：已写入阻值并闭合通路继电器（矩阵开关映射未接入）");
        }

        private static Dictionary<string, double> GetTargetsForPoint(int pointIndex)
        {
            return pointIndex switch
            {
                1 => new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                {
                    ["PT500A"] = 361.65,
                    ["PT500B"] = 361.65,
                    ["PT1000A"] = 723.3,
                    ["PT1000B"] = 723.3,
                },
                2 => new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                {
                    ["PT500A"] = 500.0,
                    ["PT500B"] = 500.0,
                    ["PT1000A"] = 1000.0,
                    ["PT1000B"] = 1000.0,
                },
                3 => new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                {
                    ["PT500A"] = 715.25,
                    ["PT500B"] = 715.25,
                    ["PT1000A"] = 1430.5,
                    ["PT1000B"] = 1430.5,
                },
                _ => new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                {
                    ["PT500A"] = 835.0,
                    ["PT500B"] = 835.0,
                    ["PT1000A"] = 1670.0,
                    ["PT1000B"] = 1670.0,
                }
            };
        }

        private async Task<bool> EnsureResistorReadyAsync(CancellationToken token)
        {
            if (_resistor != null && _resistor.IsConnected)
                return true;

            await DisconnectResistorAsync().ConfigureAwait(false);

            var candidates = new uint[] { 1, 0, 2, 3, 4, 5, 6, 7 };
            foreach (var logicalId in candidates)
            {
                token.ThrowIfCancellationRequested();

                try
                {
                    var device = new ProgrammableResistorDevice
                    {
                        Name = "电阻输出",
                        Model = "PXI-7012",
                        CardName = $"电阻输出(自动探测-{logicalId})",
                        SlotIndex = (int)logicalId
                    };

                    var api = new Pxi7012Api(device, logicalId);
                    await api.ConnectAsync(token).ConfigureAwait(false);

                    _resistor = api;
                    _connectedLogicalId = logicalId;
                    ConnectionText = $"7012: 已连接(逻辑ID={logicalId})";
                    Log($"7012连接成功：逻辑ID={logicalId}");
                    return true;
                }
                catch
                {
                    try
                    {
                        if (_resistor != null)
                        {
                            await _resistor.DisposeAsync().ConfigureAwait(false);
                        }
                    }
                    catch
                    {
                    }

                    _resistor = null;
                    _connectedLogicalId = null;
                }
            }

            ConnectionText = "7012: 未连接";
            return false;
        }

        private async Task DisconnectResistorAsync()
        {
            if (_resistor == null)
                return;

            try { await _resistor.DisconnectAsync().ConfigureAwait(false); } catch { }
            try { await _resistor.DisposeAsync().ConfigureAwait(false); } catch { }
            _resistor = null;
            _connectedLogicalId = null;
            ConnectionText = "7012: 未连接";
        }

        private void UpdatePreviewResistances()
        {
            var targets = GetTargetsForPoint(SelectedPointIndex);
            PostToUi(() =>
            {
                foreach (var item in SensorItems)
                {
                    if (targets.TryGetValue(item.SensorName, out var ohm))
                    {
                        item.TargetResistanceOhm = ohm;
                    }
                }
            });
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

            try
            {
                if (_resistor != null)
                {
                    _resistor.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }
            }
            catch
            {
            }
        }

        public sealed class SensorItemViewModel : BindableBase
        {
            private double _targetResistanceOhm;

            public SensorItemViewModel(string sensorName, string pins, string roChannel)
            {
                SensorName = sensorName;
                Pins = pins;
                RoChannel = roChannel;
            }

            public string SensorName { get; }

            public string Pins { get; }

            public string RoChannel { get; }

            public double TargetResistanceOhm
            {
                get => _targetResistanceOhm;
                set => SetProperty(ref _targetResistanceOhm, value);
            }

            public string TargetResistanceText => TargetResistanceOhm.ToString("F2", CultureInfo.InvariantCulture);
        }
    }
}
