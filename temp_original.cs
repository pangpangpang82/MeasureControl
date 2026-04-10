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

            SensorItems.Add(new SensorItemViewModel("PT500A", "J52銆丣53銆丣56", "RO1"));
            SensorItems.Add(new SensorItemViewModel("PT500B", "J61銆丣62銆丣63", "RO2"));
            SensorItems.Add(new SensorItemViewModel("PT1000A", "J54銆丣55銆丣57", "RO3"));
            SensorItems.Add(new SensorItemViewModel("PT1000B", "J28銆丣29銆丣97", "RO4"));

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

        private string _connectionText = "7012: 鏈繛鎺?;
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

                Log("寮€濮嬫墜鍔ㄦ祴璇曪紙娓╁害浼犳劅鍣ㄤ俊鍙烽噰闆嗭級锛氬噯澶囪繛鎺?012鐢甸樆杈撳嚭鏉垮崱");

                var ok = await EnsureResistorReadyAsync(_cts.Token).ConfigureAwait(false);
                if (!ok)
                {
                    Log("7012杩炴帴澶辫触锛氳妫€鏌ユ澘鍗?椹卞姩/閫昏緫ID");
                    await StopAsync().ConfigureAwait(false);
                    return;
                }

                UpdatePreviewResistances();
                Log("7012宸插氨缁細鍙墽琛岀數闃昏缃紙鐭╅樀寮€鍏?閫氳閲囬泦鏆傛湭鎺ュ叆锛?);
            }
            catch (OperationCanceledException)
            {
                await StopAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log($"鎵嬪姩娴嬭瘯鍒濆鍖栧紓甯革細{ex.Message}");
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

                Log("寮€濮嬭嚜鍔ㄦ祴璇曪紙鍗犱綅锛夛細灏嗘寜琛?-2渚濇璁剧疆鐐逛綅1~4鐨勭數闃诲€笺€傞€氳閲囬泦鏆備笉鎵ц銆?);

                var ok = await EnsureResistorReadyAsync(_cts.Token).ConfigureAwait(false);
                if (!ok)
                {
                    Log("7012杩炴帴澶辫触锛氳嚜鍔ㄦ祴璇曠粓姝?);
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
                Log("鑷姩娴嬭瘯缁撴潫锛堝崰浣嶏級锛氱數闃昏緭鍑哄凡鎵ц锛岄€氳閲囬泦涓庡垽瀹氬緟鎺ュ叆銆?);
            }
            catch (OperationCanceledException)
            {
                Log("鑷姩娴嬭瘯鍙栨秷");
            }
            catch (Exception ex)
            {
                LastTestResult = "FAIL";
                LastTestTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                Log($"鑷姩娴嬭瘯寮傚父锛歿ex.Message}");
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
                ConnectionText = "7012: 鏈繛鎺?;

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
                Log("璇峰厛鍚姩鎵嬪姩娴嬭瘯锛屼互杩炴帴7012鏉垮崱銆?);
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
                    Log("7012鏈氨缁細鏃犳硶涓嬪彂鐢甸樆鍊?);
                    return;
                }

                await ApplyPointInternalAsync(pointIndex, _cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Log($"璁剧疆鐐逛綅{pointIndex}寮傚父锛歿ex.Message}");
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

            Log($"涓嬪彂鐐逛綅{pointIndex}锛歅T500A={targets["PT500A"].ToString("F2", CultureInfo.InvariantCulture)}惟, PT500B={targets["PT500B"].ToString("F2", CultureInfo.InvariantCulture)}惟, PT1000A={targets["PT1000A"].ToString("F1", CultureInfo.InvariantCulture)}惟, PT1000B={targets["PT1000B"].ToString("F1", CultureInfo.InvariantCulture)}惟");

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

            Log($"鐐逛綅{pointIndex}璁剧疆瀹屾垚锛氬凡鍐欏叆闃诲€煎苟闂悎閫氳矾缁х數鍣紙鐭╅樀寮€鍏虫槧灏勬湭鎺ュ叆锛?);
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
                        Name = "鐢甸樆杈撳嚭",
                        Model = "PXI-7012",
                        CardName = $"鐢甸樆杈撳嚭(鑷姩鎺㈡祴-{logicalId})",
                        SlotIndex = (int)logicalId
                    };

                    var api = new Pxi7012Api(device, logicalId);
                    await api.ConnectAsync(token).ConfigureAwait(false);

                    _resistor = api;
                    _connectedLogicalId = logicalId;
                    ConnectionText = $"7012: 宸茶繛鎺?閫昏緫ID={logicalId})";
                    Log($"7012杩炴帴鎴愬姛锛氶€昏緫ID={logicalId}");
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

            ConnectionText = "7012: 鏈繛鎺?;
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
            ConnectionText = "7012: 鏈繛鎺?;
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
