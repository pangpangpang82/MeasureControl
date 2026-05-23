using Prism.Commands;
using Prism.Mvvm;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Globalization;
using MeasureControl.Views.Dialogs;
using MeasureControl.Models.Devices;
using MeasureControl.Models.Devices.DeviceCategories;
using MeasureControl.Events;
using MeasureControl.Services;
using MeasureControl.Services.HardwareApis;
using Prism.Events;
using Prism.Ioc;

namespace MeasureControl.ViewModels.SingleBoardTest.HydraulicController
{
    public class HC_6_6ViewModel : BindableBase
    {
        private const string PowerSupplyIpAddress = "192.168.1.15";
        private const string AuxiliaryPowerSupplyIpAddress = "192.168.1.16";
        private const double AuxiliaryInputVoltageV = 24.0;
        private const double AuxiliaryInputCurrentA = 1.0;
        private const double InputVoltageV = 28.0;
        private const double InputCurrentA = 1;

        private const int RxChannelIndex = 2;
        private const double ArincRate = 100000.0;

        private const int Relay485ChannelIndex = 6;
        private const int RelayAuxDoIndex = 25;
        //private const int RelayGroundDoIndex = 26;
        private const int RelayEnableDoIndex = 27;

        private const string PressureUnit = "Psid";
        private const int SamplesPerMeasure = 1;
        private const int SampleTimeoutMs = 3000;
        private const int AoSettleMs = 800;
        private const int Mtx532ReadyTimeoutMs = 6000;
        private const int Mtx532ReadyPollMs = 200;
        private const int PostSwitchRxFlushMs = 120;

        private const double Current4mA = 4.0;
        private const double Current20mA = 20.0;
        private const double Current10mA = 10.0;
        private const double CustomCurrentMinmA = 0.0;
        private const double CustomCurrentMaxmA = 42.0;
        private const double Range4mAMin = 0.0;
        private const double Range4mAMax = 3.4;
        private const double Range20mAMin = 121.5;
        private const double Range20mAMax = 128.4;
        private const double Range10mAMin = 43.44;
        private const double Range10mAMax = 50.31;

        private const byte LabelDptRfDec = 56;
        private const byte LabelDptEdpDec = 57;
        private const byte LabelDptSysDec = 58;
        private const byte LabelDptEmpDec = 59;
        private const byte SsmNormal = 1;

        private const int DataBitLength = 9;
        private const double DataResolution = 1.0;
        private const int DataMsbPosition = 27;

        private static readonly string[] AoChannels = { "AO6", "AO7", "AO8", "AO9", "AO10", "AO11" };

        private static readonly DptChannelDefinition[] DptChannels =
        {
            new DptChannelDefinition("RX", "EDP2", "DPT_EDP2", LabelDptEdpDec, 2),
            new DptChannelDefinition("RX", "EMP2B", "DPT_EMP2B", LabelDptEmpDec, 2),
            new DptChannelDefinition("RX", "EMP3B", "DPT_EMP3B", LabelDptEmpDec, 3),
            new DptChannelDefinition("RX", "RF2", "DPT_RF2", LabelDptRfDec, 2),
            new DptChannelDefinition("RX", "SYS2", "DPT_SYS2", LabelDptSysDec, 2),
            new DptChannelDefinition("RX", "SYS3", "DPT_SYS3", LabelDptSysDec, 3),
        };

        private readonly SemaphoreSlim _measureLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _relayLock = new SemaphoreSlim(1, 1);
        private readonly IPxiChassisService _pxiChassisService;
        private readonly ISingleBoardTestContextService _singleBoardTestContext;
        private readonly IBoardPowerService _boardPowerService;

        private CancellationTokenSource _manualCts;
        private CancellationTokenSource _autoCts;

        private IPowerSupplyApi _power;
        private IPowerSupplyApi _auxPower;
        private IArt4229Api _arinc;
        private IMtx532Api _mtx532;
        private IJy7131Api _jy7131;
        private bool _isRelay485On;

        private const string TestItemName = "压差传感器信号采集测试";

        private bool _isManualTestRunning;
        private bool _isAutoTestRunning;
        private bool _isManualTestInitializing;
        private bool _isAutoTestInitializing;
        private bool _isManualTestStopping;
        private bool _isAutoTestStopping;
        private bool _canMeasure;

        private bool _measured4mA;
        private bool _measured20mA;
        private bool _measured10mA;
        private bool _passed4mA;
        private bool _passed20mA;
        private bool _passed10mA;
        private bool _manualAborted;
        private bool _historyLoaded;
        private int _selectedTabIndex;

        private string _lastTestTime = "--";
        private string _lastTestResult = "--";
        private string _previousTestTime = "--";
        private string _previousTestResult = "--";
        private string _currentTestResult = "--";

        private string _DptEdp24mAText = "--";
        private string _dptEmp2B4mAText = "--";
        private string _dptEmp3B4mAText = "--";
        private string _dptSys14mAText = "--";
        private string _dptSys24mAText = "--";
        private string _dptSys34mAText = "--";

        private string _dptEdp2A20mAText = "--";
        private string _dptEmp2B20mAText = "--";
        private string _dptEmp3B20mAText = "--";
        private string _dptSys120mAText = "--";
        private string _dptSys220mAText = "--";
        private string _dptSys320mAText = "--";

        private string _dptEdp2A10mAText = "--";
        private string _dptEmp2B10mAText = "--";
        private string _dptEmp3B10mAText = "--";
        private string _dptSys110mAText = "--";
        private string _dptSys210mAText = "--";
        private string _dptSys310mAText = "--";

        private string _dptEdp2CustommAText = "--";
        private string _dptEmp2BCustommAText = "--";
        private string _dptEmp3BCustommAText = "--";
        private string _dptSys1CustommAText = "--";
        private double? _scriptDptEdp2;
        private double? _scriptDptEmp2B;
        private double? _scriptDptEmp3B;
        private double? _scriptDptRf2;
        private double? _scriptDptSys2;
        private double? _scriptDptSys3;
        private string _dptSys2CustommAText = "--";
        private string _dptSys3CustommAText = "--";
        private string _customCurrentInput = "10.0";

        private sealed class DptChannelDefinition
        {
            public DptChannelDefinition(string group, string slotKey, string channelName, byte label, byte sdi)
            {
                Group = group;
                SlotKey = slotKey;
                ChannelName = channelName;
                Label = label;
                Sdi = sdi;
            }

            public string Group { get; }
            public string SlotKey { get; }
            public string ChannelName { get; }
            public byte Label { get; }
            public byte Sdi { get; }
        }

        public HC_6_6ViewModel(IPxiChassisService pxiChassisService, ISingleBoardTestContextService singleBoardTestContext, IBoardPowerService hydraulicPowerService)
        {
            _pxiChassisService = pxiChassisService;
            _singleBoardTestContext = singleBoardTestContext;
            _boardPowerService = hydraulicPowerService;

            ManualTestCommand = new DelegateCommand(async () => await OnManualTestAsync());
            AutoTestCommand = new DelegateCommand(async () => await OnAutoTestAsync());
            Measure14Command = new DelegateCommand(async () => await OnMeasure14Async(), () => CanMeasure14);
            MeasureCustomCurrentCommand = new DelegateCommand(async () => await OnMeasureCustomCurrentAsync(), () => CanMeasureCustomCurrent);
            ClearLogCommand = new DelegateCommand(() => Logs.Clear());

            LoadLastTestResultFromProject();
            SubscribeStopEvent();
        }

        private void SubscribeStopEvent()
        {
            var ea = ContainerLocator.Container?.Resolve(typeof(IEventAggregator)) as IEventAggregator;
            ea?.GetEvent<RequestStopHydraulicTestsEvent>().Subscribe(OnRequestStopAllTests);
        }

        private void OnRequestStopAllTests(RequestStopHydraulicTestsEventArgs args)
        {
            if (_isManualTestRunning || _isManualTestInitializing)
                args.StopTasks.Add(StopManualTestAsync());
            if (_isAutoTestRunning || _isAutoTestInitializing)
                args.StopTasks.Add(StopAutoTestAsync());
        }

        public DelegateCommand ManualTestCommand { get; }
        public DelegateCommand AutoTestCommand { get; }
        public DelegateCommand Measure14Command { get; }
        public DelegateCommand MeasureCustomCurrentCommand { get; }
        public DelegateCommand ClearLogCommand { get; }

        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();

        public bool IsManualTestBusy => IsManualTestInitializing || IsManualTestStopping;

        public bool IsAutoTestBusy => IsAutoTestInitializing || IsAutoTestStopping;

        public bool IsManualTestInitializing
        {
            get => _isManualTestInitializing;
            private set
            {
                if (SetProperty(ref _isManualTestInitializing, value))
                {
                    RaisePropertyChanged(nameof(IsManualTestBusy));
                    RaisePropertyChanged(nameof(CanStartManualTest));
                    RaisePropertyChanged(nameof(CanStartAutoTest));
                }
            }
        }

        public bool IsAutoTestInitializing
        {
            get => _isAutoTestInitializing;
            private set
            {
                if (SetProperty(ref _isAutoTestInitializing, value))
                {
                    RaisePropertyChanged(nameof(IsAutoTestBusy));
                    RaisePropertyChanged(nameof(CanStartManualTest));
                    RaisePropertyChanged(nameof(CanStartAutoTest));
                }
            }
        }

        public bool IsManualTestStopping
        {
            get => _isManualTestStopping;
            private set
            {
                if (SetProperty(ref _isManualTestStopping, value))
                {
                    RaisePropertyChanged(nameof(IsManualTestBusy));
                    RaisePropertyChanged(nameof(CanStartManualTest));
                    RaisePropertyChanged(nameof(CanStartAutoTest));
                }
            }
        }

        public bool IsAutoTestStopping
        {
            get => _isAutoTestStopping;
            private set
            {
                if (SetProperty(ref _isAutoTestStopping, value))
                {
                    RaisePropertyChanged(nameof(IsAutoTestBusy));
                    RaisePropertyChanged(nameof(CanStartManualTest));
                    RaisePropertyChanged(nameof(CanStartAutoTest));
                }
            }
        }

        public bool IsManualTestRunning
        {
            get => _isManualTestRunning;
            set
            {
                if (SetProperty(ref _isManualTestRunning, value))
                {
                    RaisePropertyChanged(nameof(CanMeasure14));
                    RaisePropertyChanged(nameof(CanMeasureCustomCurrent));
                    RaisePropertyChanged(nameof(CanStartManualTest));
                    RaisePropertyChanged(nameof(CanStartAutoTest));
                    Measure14Command?.RaiseCanExecuteChanged();
                    MeasureCustomCurrentCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        public bool IsAutoTestRunning
        {
            get => _isAutoTestRunning;
            set
            {
                if (SetProperty(ref _isAutoTestRunning, value))
                {
                    RaisePropertyChanged(nameof(CanMeasure14));
                    RaisePropertyChanged(nameof(CanMeasureCustomCurrent));
                    RaisePropertyChanged(nameof(CanStartManualTest));
                    RaisePropertyChanged(nameof(CanStartAutoTest));
                    Measure14Command?.RaiseCanExecuteChanged();
                    MeasureCustomCurrentCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        public int SelectedTabIndex
        {
            get => _selectedTabIndex;
            set
            {
                if (SetProperty(ref _selectedTabIndex, value))
                {
                    RaisePropertyChanged(nameof(CanMeasure14));
                    RaisePropertyChanged(nameof(CanMeasureCustomCurrent));
                    Measure14Command?.RaiseCanExecuteChanged();
                    MeasureCustomCurrentCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        public bool CanMeasure
        {
            get => _canMeasure;
            private set
            {
                if (SetProperty(ref _canMeasure, value))
                {
                    RaisePropertyChanged(nameof(CanMeasure14));
                    RaisePropertyChanged(nameof(CanMeasureCustomCurrent));
                    Measure14Command?.RaiseCanExecuteChanged();
                    MeasureCustomCurrentCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        public bool CanMeasure14 => IsManualTestRunning && CanMeasure;

        public bool CanMeasureCustomCurrent => IsManualTestRunning && CanMeasure && TryGetValidatedCustomCurrent(out _);

        private void RefreshMeasureCommand()
        {
            RaisePropertyChanged(nameof(CanMeasure14));
            RaisePropertyChanged(nameof(CanMeasureCustomCurrent));
            Measure14Command?.RaiseCanExecuteChanged();
            MeasureCustomCurrentCommand?.RaiseCanExecuteChanged();
        }

        public bool CanStartManualTest => !IsManualTestBusy && !IsAutoTestBusy && !IsAutoTestRunning;
        public bool CanStartAutoTest => !IsManualTestBusy && !IsAutoTestBusy && !IsManualTestRunning;

        public string CurrentTestResult
        {
            get => _currentTestResult;
            private set => SetProperty(ref _currentTestResult, value);
        }

        public string LastTestTime
        {
            get
            {
                LoadLastTestResultFromProject();
                return _lastTestTime;
            }
            set => SetProperty(ref _lastTestTime, value);
        }

        public string LastTestResult
        {
            get
            {
                LoadLastTestResultFromProject();
                return _lastTestResult;
            }
            set => SetProperty(ref _lastTestResult, value);
        }

        public string PreviousTestTime
        {
            get
            {
                LoadLastTestResultFromProject();
                return _previousTestTime;
            }
            set => SetProperty(ref _previousTestTime, value);
        }

        public string PreviousTestResult
        {
            get
            {
                LoadLastTestResultFromProject();
                return _previousTestResult;
            }
            set => SetProperty(ref _previousTestResult, value);
        }

        public string DptEdp24mAText { get => _DptEdp24mAText; private set => SetProperty(ref _DptEdp24mAText, value); }
        public string DptEmp2B4mAText { get => _dptEmp2B4mAText; private set => SetProperty(ref _dptEmp2B4mAText, value); }
        public string DptEmp3B4mAText { get => _dptEmp3B4mAText; private set => SetProperty(ref _dptEmp3B4mAText, value); }
        public string DptSys14mAText { get => _dptSys14mAText; private set => SetProperty(ref _dptSys14mAText, value); }
        public string DptSys24mAText { get => _dptSys24mAText; private set => SetProperty(ref _dptSys24mAText, value); }
        public string DptSys34mAText { get => _dptSys34mAText; private set => SetProperty(ref _dptSys34mAText, value); }

        public string DptEdp2A20mAText { get => _dptEdp2A20mAText; private set => SetProperty(ref _dptEdp2A20mAText, value); }
        public string DptEmp2B20mAText { get => _dptEmp2B20mAText; private set => SetProperty(ref _dptEmp2B20mAText, value); }
        public string DptEmp3B20mAText { get => _dptEmp3B20mAText; private set => SetProperty(ref _dptEmp3B20mAText, value); }
        public string DptSys120mAText { get => _dptSys120mAText; private set => SetProperty(ref _dptSys120mAText, value); }
        public string DptSys220mAText { get => _dptSys220mAText; private set => SetProperty(ref _dptSys220mAText, value); }
        public string DptSys320mAText { get => _dptSys320mAText; private set => SetProperty(ref _dptSys320mAText, value); }

        public string DptEdp2A10mAText { get => _dptEdp2A10mAText; private set => SetProperty(ref _dptEdp2A10mAText, value); }
        public string DptEmp2B10mAText { get => _dptEmp2B10mAText; private set => SetProperty(ref _dptEmp2B10mAText, value); }
        public string DptEmp3B10mAText { get => _dptEmp3B10mAText; private set => SetProperty(ref _dptEmp3B10mAText, value); }
        public string DptSys110mAText { get => _dptSys110mAText; private set => SetProperty(ref _dptSys110mAText, value); }
        public string DptSys210mAText { get => _dptSys210mAText; private set => SetProperty(ref _dptSys210mAText, value); }
        public string DptSys310mAText { get => _dptSys310mAText; private set => SetProperty(ref _dptSys310mAText, value); }

        public string DptEdp2CustommAText { get => _dptEdp2CustommAText; private set => SetProperty(ref _dptEdp2CustommAText, value); }
        public string DptEmp2BCustommAText { get => _dptEmp2BCustommAText; private set => SetProperty(ref _dptEmp2BCustommAText, value); }
        public string DptEmp3BCustommAText { get => _dptEmp3BCustommAText; private set => SetProperty(ref _dptEmp3BCustommAText, value); }
        public string DptSys1CustommAText { get => _dptSys1CustommAText; private set => SetProperty(ref _dptSys1CustommAText, value); }
        public string DptSys2CustommAText { get => _dptSys2CustommAText; private set => SetProperty(ref _dptSys2CustommAText, value); }
        public string DptSys3CustommAText { get => _dptSys3CustommAText; private set => SetProperty(ref _dptSys3CustommAText, value); }

        public string CustomCurrentInput
        {
            get => _customCurrentInput;
            set
            {
                var normalized = NormalizeCurrentInput(value);
                if (SetProperty(ref _customCurrentInput, normalized))
                {
                    RaisePropertyChanged(nameof(CanMeasureCustomCurrent));
                    MeasureCustomCurrentCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        public double? DptEdp24mAValue => ParseMeasurementValue(DptEdp24mAText);
        public double? DptEmp2B4mAValue => ParseMeasurementValue(DptEmp2B4mAText);
        public double? DptEmp3B4mAValue => ParseMeasurementValue(DptEmp3B4mAText);
        public double? DptSys14mAValue => ParseMeasurementValue(DptSys14mAText);
        public double? DptSys24mAValue => ParseMeasurementValue(DptSys24mAText);
        public double? DptSys34mAValue => ParseMeasurementValue(DptSys34mAText);

        public double? DptEdp2A20mAValue => ParseMeasurementValue(DptEdp2A20mAText);
        public double? DptEmp2B20mAValue => ParseMeasurementValue(DptEmp2B20mAText);
        public double? DptEmp3B20mAValue => ParseMeasurementValue(DptEmp3B20mAText);
        public double? DptSys120mAValue => ParseMeasurementValue(DptSys120mAText);
        public double? DptSys220mAValue => ParseMeasurementValue(DptSys220mAText);
        public double? DptSys320mAValue => ParseMeasurementValue(DptSys320mAText);

        public double? DptEdp2A10mAValue => ParseMeasurementValue(DptEdp2A10mAText);
        public double? DptEmp2B10mAValue => ParseMeasurementValue(DptEmp2B10mAText);
        public double? DptEmp3B10mAValue => ParseMeasurementValue(DptEmp3B10mAText);
        public double? DptSys110mAValue => ParseMeasurementValue(DptSys110mAText);
        public double? DptSys210mAValue => ParseMeasurementValue(DptSys210mAText);
        public double? DptSys310mAValue => ParseMeasurementValue(DptSys310mAText);

        public bool IsDptEdp24mAPass => IsWithinRange(DptEdp24mAValue ?? double.MinValue, Range4mAMin, Range4mAMax) && DptEdp24mAValue.HasValue;
        public bool IsDptEmp2B4mAPass => IsWithinRange(DptEmp2B4mAValue ?? double.MinValue, Range4mAMin, Range4mAMax) && DptEmp2B4mAValue.HasValue;
        public bool IsDptEmp3B4mAPass => IsWithinRange(DptEmp3B4mAValue ?? double.MinValue, Range4mAMin, Range4mAMax) && DptEmp3B4mAValue.HasValue;
        public bool IsDptSys14mAPass => IsWithinRange(DptSys14mAValue ?? double.MinValue, Range4mAMin, Range4mAMax) && DptSys14mAValue.HasValue;
        public bool IsDptSys24mAPass => IsWithinRange(DptSys24mAValue ?? double.MinValue, Range4mAMin, Range4mAMax) && DptSys24mAValue.HasValue;
        public bool IsDptSys34mAPass => IsWithinRange(DptSys34mAValue ?? double.MinValue, Range4mAMin, Range4mAMax) && DptSys34mAValue.HasValue;

        public bool IsDptEdp2A20mAPass => IsWithinRange(DptEdp2A20mAValue ?? double.MinValue, Range20mAMin, Range20mAMax) && DptEdp2A20mAValue.HasValue;
        public bool IsDptEmp2B20mAPass => IsWithinRange(DptEmp2B20mAValue ?? double.MinValue, Range20mAMin, Range20mAMax) && DptEmp2B20mAValue.HasValue;
        public bool IsDptEmp3B20mAPass => IsWithinRange(DptEmp3B20mAValue ?? double.MinValue, Range20mAMin, Range20mAMax) && DptEmp3B20mAValue.HasValue;
        public bool IsDptSys120mAPass => IsWithinRange(DptSys120mAValue ?? double.MinValue, Range20mAMin, Range20mAMax) && DptSys120mAValue.HasValue;
        public bool IsDptSys220mAPass => IsWithinRange(DptSys220mAValue ?? double.MinValue, Range20mAMin, Range20mAMax) && DptSys220mAValue.HasValue;
        public bool IsDptSys320mAPass => IsWithinRange(DptSys320mAValue ?? double.MinValue, Range20mAMin, Range20mAMax) && DptSys320mAValue.HasValue;

        public bool IsDptEdp2A10mAPass => IsWithinRange(DptEdp2A10mAValue ?? double.MinValue, Range10mAMin, Range10mAMax) && DptEdp2A10mAValue.HasValue;
        public bool IsDptEmp2B10mAPass => IsWithinRange(DptEmp2B10mAValue ?? double.MinValue, Range10mAMin, Range10mAMax) && DptEmp2B10mAValue.HasValue;
        public bool IsDptEmp3B10mAPass => IsWithinRange(DptEmp3B10mAValue ?? double.MinValue, Range10mAMin, Range10mAMax) && DptEmp3B10mAValue.HasValue;
        public bool IsDptSys110mAPass => IsWithinRange(DptSys110mAValue ?? double.MinValue, Range10mAMin, Range10mAMax) && DptSys110mAValue.HasValue;
        public bool IsDptSys210mAPass => IsWithinRange(DptSys210mAValue ?? double.MinValue, Range10mAMin, Range10mAMax) && DptSys210mAValue.HasValue;
        public bool IsDptSys310mAPass => IsWithinRange(DptSys310mAValue ?? double.MinValue, Range10mAMin, Range10mAMax) && DptSys310mAValue.HasValue;

        public double? ScriptDptEdp2Value => _scriptDptEdp2;
        public double? ScriptDptEmp2BValue => _scriptDptEmp2B;
        public double? ScriptDptEmp3BValue => _scriptDptEmp3B;
        public double? ScriptDptRf2Value => _scriptDptRf2;
        public double? ScriptDptSys2Value => _scriptDptSys2;
        public double? ScriptDptSys3Value => _scriptDptSys3;

        public async Task<string> RunOnceAsync(CancellationToken cancellationToken)
        {
            if (IsAutoTestRunning)
                await StopAutoTestAsync().ConfigureAwait(false);

            if (IsManualTestRunning)
                await StopManualTestAsync().ConfigureAwait(false);

            _autoCts?.Cancel();
            _autoCts?.Dispose();
            _autoCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            try
            {
                return await ExecuteAutoTestAsync(_autoCts.Token).ConfigureAwait(false);
            }
            finally
            {
                _autoCts?.Dispose();
                _autoCts = null;
            }
        }

        public async Task RunWithScriptCurrentsAsync(double[] currents, CancellationToken cancellationToken)
        {
            if (currents == null || currents.Length != 6)
                throw new ArgumentException("脚本压差测试需要 6 个电流输入值", nameof(currents));

            if (IsAutoTestRunning)
                await StopAutoTestAsync().ConfigureAwait(false);
            if (IsManualTestRunning)
                await StopManualTestAsync().ConfigureAwait(false);

            _autoCts?.Cancel();
            _autoCts?.Dispose();
            _autoCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            try
            {
                await ExecuteScriptCurrentsTestAsync(currents, _autoCts.Token).ConfigureAwait(false);
            }
            finally
            {
                _autoCts?.Dispose();
                _autoCts = null;
            }
        }

        private async Task ExecuteScriptCurrentsTestAsync(double[] currents, CancellationToken cancellationToken)
        {
            _scriptDptEdp2 = null;
            _scriptDptEmp2B = null;
            _scriptDptEmp3B = null;
            _scriptDptRf2 = null;
            _scriptDptSys2 = null;
            _scriptDptSys3 = null;
            IsAutoTestStopping = false;
            CanMeasure = false;
            Log($"脚本压差测试: EDP2={currents[0]:0.#}mA EMP2B={currents[1]:0.#}mA EMP3B={currents[2]:0.#}mA RF2={currents[3]:0.#}mA SYS2={currents[4]:0.#}mA SYS3={currents[5]:0.#}mA");

            try
            {
                await EnsureRelay485Async(on: true, cancellationToken: cancellationToken).ConfigureAwait(false);
                await EnsureGroundDoAsync(on: true, cancellationToken: cancellationToken).ConfigureAwait(false);
                await EnsureArincRxAsync(cancellationToken).ConfigureAwait(false);
                await EnsureMtx532Async(cancellationToken).ConfigureAwait(false);
                await EnsurePowerAsync(cancellationToken).ConfigureAwait(false);
                IsAutoTestRunning = true;

                foreach (var ch in DptChannels) SetCustomCurrent(ch.SlotKey, "--");
                await MeasureGroupScriptAsync(currents, cancellationToken).ConfigureAwait(false);

                await StopAutoTestAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Log("脚本压差测试已停止");
                await StopAutoTestAsync().ConfigureAwait(false);
                throw;
            }
            catch (Exception ex)
            {
                Log($"脚本压差测试异常: {ex.Message}");
                await StopAutoTestAsync().ConfigureAwait(false);
                throw;
            }
        }

        private async Task<bool> MeasureGroupScriptAsync(double[] currents, CancellationToken cancellationToken)
        {
            if (!IsAutoTestRunning)
                return false;

            var voltages = new double[currents.Length];
            for (var i = 0; i < currents.Length; i++)
                voltages[i] = ConvertCurrentToVoltage(currents[i]);

            await _measureLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await SetAo67891011IndependentAsync(voltages, cancellationToken).ConfigureAwait(false);
                await Task.Delay(AoSettleMs, cancellationToken).ConfigureAwait(false);
                await DrainArincBufferAsync(cancellationToken).ConfigureAwait(false);
                await Task.Delay(PostSwitchRxFlushMs, cancellationToken).ConfigureAwait(false);

                var samples = new Dictionary<string, List<double>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["EDP2"] = new List<double>(SamplesPerMeasure),
                    ["EMP2B"] = new List<double>(SamplesPerMeasure),
                    ["EMP3B"] = new List<double>(SamplesPerMeasure),
                    ["RF2"] = new List<double>(SamplesPerMeasure),
                    ["SYS2"] = new List<double>(SamplesPerMeasure),
                    ["SYS3"] = new List<double>(SamplesPerMeasure),
                };

                var deadline = DateTime.UtcNow.AddMilliseconds(SampleTimeoutMs);
                while (!cancellationToken.IsCancellationRequested && DateTime.UtcNow <= deadline)
                {
                    var words = await _arinc.ReadRxWordsAsync(RxChannelIndex, maxCount: 512, enableTimeTag: false, enableRateAdaption: false, cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
                    foreach (var w in words)
                    {
                        _arinc.ParseRawWord(w.Data429, out var label, out var wordSdi, out var data19, out var ssm);
                        var definition = ResolveChannel(label, wordSdi);
                        if (definition == null || ssm != SsmNormal) continue;
                        var value = DecodeValue(data19);
                        if (!value.HasValue) continue;
                        var list = samples[definition.SlotKey];
                        if (list.Count >= SamplesPerMeasure) continue;
                        list.Add(value.Value);
                        SetCustomCurrent(definition.SlotKey, $"{value.Value:0.0} {PressureUnit}");
                    }
                    if (samples.Values.All(l => l.Count >= SamplesPerMeasure))
                        break;
                    await Task.Delay(10, cancellationToken).ConfigureAwait(false);
                }

                if (samples["EDP2"].Count > 0) _scriptDptEdp2 = samples["EDP2"].Average();
                if (samples["EMP2B"].Count > 0) _scriptDptEmp2B = samples["EMP2B"].Average();
                if (samples["EMP3B"].Count > 0) _scriptDptEmp3B = samples["EMP3B"].Average();
                if (samples["RF2"].Count > 0) _scriptDptRf2 = samples["RF2"].Average();
                if (samples["SYS2"].Count > 0) _scriptDptSys2 = samples["SYS2"].Average();
                if (samples["SYS3"].Count > 0) _scriptDptSys3 = samples["SYS3"].Average();

                Log($"脚本压差测试完成: EDP2={_scriptDptEdp2:0.0} EMP2B={_scriptDptEmp2B:0.0} EMP3B={_scriptDptEmp3B:0.0} RF2={_scriptDptRf2:0.0} SYS2={_scriptDptSys2:0.0} SYS3={_scriptDptSys3:0.0}");
                return true;
            }
            finally
            {
                _measureLock.Release();
            }
        }

        private async Task SetAo67891011IndependentAsync(double[] voltages, CancellationToken cancellationToken)
        {
            if (_mtx532 == null || !_mtx532.IsConnected)
                throw new InvalidOperationException("MTX532未连接");

            var outputs = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < AoChannels.Length && i < voltages.Length; i++)
                outputs[AoChannels[i]] = voltages[i];
            await _mtx532.WriteOnceDcAsync(outputs, cancellationToken).ConfigureAwait(false);
        }

        private void LoadLastTestResultFromProject()
        {
            if (_historyLoaded)
                return;

            var testItemNode = _singleBoardTestContext?.GetCurrentTestItemNode(TestItemName);
            if (testItemNode == null)
                return;

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

            _historyLoaded = true;
        }

        private void SaveTestResultToProject()
        {
            var testItemNode = _singleBoardTestContext?.GetCurrentTestItemNode(TestItemName);
            if (testItemNode == null)
                return;

            testItemNode.LastTestTime = PreviousTestTime;
            testItemNode.LastTestResult = PreviousTestResult;

            var eventAggregator = ContainerLocator.Container?.Resolve(typeof(IEventAggregator)) as IEventAggregator;
            eventAggregator?.GetEvent<ProjectModifiedEvent>()?.Publish(new ProjectModifiedEventArgs
            {
                ModificationType = "SingleBoardTestResult",
                Description = $"单板测试结果已更新: {TestItemName}"
            });
        }

        private async Task OnManualTestAsync()
        {
            if (IsManualTestStopping)
            {
                return;
            }

            if (IsManualTestRunning || IsManualTestInitializing)
            {
                await StopManualTestAsync().ConfigureAwait(false);
                return;
            }

            if (IsAutoTestRunning)
                await StopAutoTestAsync().ConfigureAwait(false);

            IsManualTestInitializing = true;
            IsManualTestStopping = false;
            CurrentTestResult = "--";
            PreviousTestTime = "--";
            CanMeasure = false;
            _manualAborted = false;
            _measured4mA = false;
            _measured20mA = false;
            _measured10mA = false;
            _passed4mA = false;
            _passed20mA = false;
            _passed10mA = false;

            ResetAllDisplays();

            _manualCts?.Cancel();
            _manualCts?.Dispose();
            _manualCts = new CancellationTokenSource();

            Log("开始手动测试");
            Log("正在初始化设备...");


            try
            {
                await EnsureRelay485Async(on: true, cancellationToken: _manualCts.Token).ConfigureAwait(false);
                await EnsureGroundDoAsync(on: true, cancellationToken: _manualCts.Token).ConfigureAwait(false);
                await EnsureArincRxAsync(_manualCts.Token).ConfigureAwait(false);
                await EnsureMtx532Async(_manualCts.Token).ConfigureAwait(false);
                await EnsurePowerAsync(_manualCts.Token).ConfigureAwait(false);

                IsManualTestInitializing = false;
                IsManualTestRunning = true;
                CanMeasure = true;
                Log("手动测试初始化完成，可点击固定电流值或自定义电流值测量");
            }
            catch (Exception ex)
            {
                await AbortManualTestAsync($"手动测试初始化失败，中止: {ex.Message}").ConfigureAwait(false);
            }
        }

        private async Task OnAutoTestAsync()
        {
            if (IsAutoTestStopping)
            {
                return;
            }

            if (IsAutoTestRunning || IsAutoTestInitializing)
            {
                await StopAutoTestAsync().ConfigureAwait(false);
                return;
            }

            if (IsManualTestRunning)
                await StopManualTestAsync().ConfigureAwait(false);

            IsAutoTestInitializing = true;
            IsAutoTestStopping = false;
            CurrentTestResult = "--";
            PreviousTestTime = "--";
            CanMeasure = false;
            _manualAborted = false;
            _measured4mA = false;
            _measured20mA = false;
            _measured10mA = false;
            _passed4mA = false;
            _passed20mA = false;
            _passed10mA = false;

            ResetAllDisplays();

            _autoCts?.Cancel();
            _autoCts?.Dispose();
            _autoCts = new CancellationTokenSource();

            Log("开始自动测试");
            Log("正在初始化设备...");


            try
            {
                await EnsureRelay485Async(on: true, cancellationToken: _autoCts.Token).ConfigureAwait(false);
                await EnsureGroundDoAsync(on: true, cancellationToken: _autoCts.Token).ConfigureAwait(false);
                await EnsureArincRxAsync(_autoCts.Token).ConfigureAwait(false);
                await EnsureMtx532Async(_autoCts.Token).ConfigureAwait(false);
                await EnsurePowerAsync(_autoCts.Token).ConfigureAwait(false);

                IsAutoTestInitializing = false;
                IsAutoTestRunning = true;

                var ok4 = await MeasureGroupAsync("4mA", Current4mA, Set4mA, _autoCts.Token).ConfigureAwait(false);
                if (!IsAutoTestRunning)
                    return;
                _measured4mA = true;
                _passed4mA = ok4;
                await Task.Delay(80, _autoCts.Token).ConfigureAwait(false);
                var ok20 = await MeasureGroupAsync("20mA", Current20mA, Set20mA, _autoCts.Token).ConfigureAwait(false);
                if (!IsAutoTestRunning)
                    return;
                _measured20mA = true;
                _passed20mA = ok20;
                await Task.Delay(80, _autoCts.Token).ConfigureAwait(false);
                var ok10 = await MeasureGroupAsync("10mA", Current10mA, Set10mA, _autoCts.Token).ConfigureAwait(false);
                if (!IsAutoTestRunning)
                    return;
                _measured10mA = true;
                _passed10mA = ok10;

                await TryFinalizeAsync().ConfigureAwait(false);
                await StopAutoTestAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Log("自动测试已停止");
                await StopAutoTestAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log($"自动测试异常: {ex.Message}");
                await StopAutoTestAsync().ConfigureAwait(false);
            }
            finally
            {
                IsAutoTestInitializing = false;
                _autoCts?.Dispose();
                _autoCts = null;
            }
        }

        private async Task<string> ExecuteAutoTestAsync(CancellationToken cancellationToken)
        {
            CurrentTestResult = "--";
            PreviousTestTime = "--";
            CanMeasure = false;
            _manualAborted = false;
            _measured4mA = false;
            _measured20mA = false;
            _measured10mA = false;
            _passed4mA = false;
            _passed20mA = false;
            _passed10mA = false;

            ResetAllDisplays();

            await EnsureRelay485Async(on: true, cancellationToken: cancellationToken).ConfigureAwait(false);
            await EnsureGroundDoAsync(on: true, cancellationToken: cancellationToken).ConfigureAwait(false);
            await EnsureArincRxAsync(cancellationToken).ConfigureAwait(false);
            await EnsureMtx532Async(cancellationToken).ConfigureAwait(false);
            await EnsurePowerAsync(cancellationToken).ConfigureAwait(false);
            IsAutoTestInitializing = false;
            IsAutoTestRunning = true;

            var ok4 = await MeasureGroupAsync("4mA", Current4mA, Set4mA, cancellationToken).ConfigureAwait(false);
            if (!IsAutoTestRunning)
                return CurrentTestResult ?? "--";
            _measured4mA = true;
            _passed4mA = ok4;
            await Task.Delay(80, cancellationToken).ConfigureAwait(false);
            var ok20 = await MeasureGroupAsync("20mA", Current20mA, Set20mA, cancellationToken).ConfigureAwait(false);
            if (!IsAutoTestRunning)
                return CurrentTestResult ?? "--";
            _measured20mA = true;
            _passed20mA = ok20;
            await Task.Delay(80, cancellationToken).ConfigureAwait(false);
            var ok10 = await MeasureGroupAsync("10mA", Current10mA, Set10mA, cancellationToken).ConfigureAwait(false);
            if (!IsAutoTestRunning)
                return CurrentTestResult ?? "--";
            _measured10mA = true;
            _passed10mA = ok10;

            await TryFinalizeAsync().ConfigureAwait(false);
            await StopAutoTestAsync().ConfigureAwait(false);
            return LastTestResult;
        }

        private async Task OnMeasure14Async()
        {
            switch (SelectedTabIndex)
            {
                case 0:
                    foreach (var ch in DptChannels) Set4mA(ch.SlotKey, "--");
                    break;
                case 1:
                    foreach (var ch in DptChannels) Set20mA(ch.SlotKey, "--");
                    break;
                case 2:
                    foreach (var ch in DptChannels) Set10mA(ch.SlotKey, "--");
                    break;
                default:
                    foreach (var ch in DptChannels) Set4mA(ch.SlotKey, "--");
                    break;
            }
            CanMeasure = false;
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(delegate { }, System.Windows.Threading.DispatcherPriority.Background);
            var token = _manualCts?.Token ?? CancellationToken.None;
            var ok = false;
            switch (SelectedTabIndex)
            {
                case 0:
                    ok = await MeasureGroupAsync("4mA", Current4mA, Set4mA, token).ConfigureAwait(false);
                    break;
                case 1:
                    ok = await MeasureGroupAsync("20mA", Current20mA, Set20mA, token).ConfigureAwait(false);
                    break;
                case 2:
                    ok = await MeasureGroupAsync("10mA", Current10mA, Set10mA, token).ConfigureAwait(false);
                    break;
                default:
                    ok = await MeasureGroupAsync("当前档位", Current4mA, Set4mA, token).ConfigureAwait(false);
                    break;
            }
            CanMeasure = IsManualTestRunning;
            if (!IsManualTestRunning || _manualAborted)
                return;

            switch (SelectedTabIndex)
            {
                case 0:
                    _measured4mA = true;
                    _passed4mA = ok;
                    break;
                case 1:
                    _measured20mA = true;
                    _passed20mA = ok;
                    break;
                case 2:
                    _measured10mA = true;
                    _passed10mA = ok;
                    break;
                default:
                    _measured4mA = true;
                    _passed4mA = ok;
                    break;
            }
            RefreshMeasureCommand();
        }

        private async Task OnMeasureCustomCurrentAsync()
        {
            if (!TryGetValidatedCustomCurrent(out var currentmA))
            {
                Log("自定义电流输入无效，请输入 4~20mA，且最多 1 位小数");
                RefreshMeasureCommand();
                return;
            }

            foreach (var ch in DptChannels) SetCustomCurrent(ch.SlotKey, "--");
            CanMeasure = false;
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(delegate { }, System.Windows.Threading.DispatcherPriority.Background);
            var token = _manualCts?.Token ?? CancellationToken.None;
            var ok = await MeasureGroupAsync($"自定义点({currentmA:0.0}mA)", currentmA, SetCustomCurrent, token).ConfigureAwait(false);
            CanMeasure = IsManualTestRunning;
            if (!IsManualTestRunning || _manualAborted || !ok)
                return;

            Log($"自定义电流测量完成: {currentmA:0.0}mA，可继续测量");
        }

        private void Set4mA(string name, string text)
        {
            switch (name)
            {
                case "EDP2": DptEdp24mAText = text; break;
                case "EMP2B": DptEmp2B4mAText = text; break;
                case "EMP3B": DptEmp3B4mAText = text; break;
                case "RF2": DptSys14mAText = text; break;
                case "SYS2": DptSys24mAText = text; break;
                case "SYS3": DptSys34mAText = text; break;
            }
        }

        private void Set20mA(string name, string text)
        {
            switch (name)
            {
                case "EDP2": DptEdp2A20mAText = text; break;
                case "EMP2B": DptEmp2B20mAText = text; break;
                case "EMP3B": DptEmp3B20mAText = text; break;
                case "RF2": DptSys120mAText = text; break;
                case "SYS2": DptSys220mAText = text; break;
                case "SYS3": DptSys320mAText = text; break;
            }
        }

        private void Set10mA(string name, string text)
        {
            switch (name)
            {
                case "EDP2": DptEdp2A10mAText = text; break;
                case "EMP2B": DptEmp2B10mAText = text; break;
                case "EMP3B": DptEmp3B10mAText = text; break;
                case "RF2": DptSys110mAText = text; break;
                case "SYS2": DptSys210mAText = text; break;
                case "SYS3": DptSys310mAText = text; break;
            }
        }

        private void SetCustomCurrent(string name, string text)
        {
            switch (name)
            {
                case "EDP2": DptEdp2CustommAText = text; break;
                case "EMP2B": DptEmp2BCustommAText = text; break;
                case "EMP3B": DptEmp3BCustommAText = text; break;
                case "RF2": DptSys1CustommAText = text; break;
                case "SYS2": DptSys2CustommAText = text; break;
                case "SYS3": DptSys3CustommAText = text; break;
            }
        }

        private async Task<bool> MeasureGroupAsync(string title, double currentmA, Action<string, string> setTextByName, CancellationToken cancellationToken)
        {
            if (!IsAutoTestRunning && !IsManualTestRunning)
            {
                Log($"{title}: 当前未处于测试状态");
                return false;
            }

            await _measureLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var voltageV = ConvertCurrentToVoltage(currentmA);
                await SetAo67891011Async(voltageV, cancellationToken).ConfigureAwait(false);
                await Task.Delay(AoSettleMs, cancellationToken).ConfigureAwait(false);
                await DrainArincBufferAsync(cancellationToken).ConfigureAwait(false);
                await Task.Delay(PostSwitchRxFlushMs, cancellationToken).ConfigureAwait(false);

                var samples = new Dictionary<string, List<double>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["EDP2"] = new List<double>(SamplesPerMeasure),
                    ["EMP2B"] = new List<double>(SamplesPerMeasure),
                    ["EMP3B"] = new List<double>(SamplesPerMeasure),
                    ["RF2"] = new List<double>(SamplesPerMeasure),
                    ["SYS2"] = new List<double>(SamplesPerMeasure),
                    ["SYS3"] = new List<double>(SamplesPerMeasure),
                };

                var deadline = DateTime.UtcNow.AddMilliseconds(SampleTimeoutMs);
                while (!cancellationToken.IsCancellationRequested && DateTime.UtcNow <= deadline)
                {
                    var words = await _arinc.ReadRxWordsAsync(RxChannelIndex, maxCount: 512, enableTimeTag: false, enableRateAdaption: false, cancellationToken: cancellationToken)
                        .ConfigureAwait(false);

                    foreach (var w in words)
                    {

                        _arinc.ParseRawWord(w.Data429, out var label, out var wordSdi, out var data19, out var ssm);
                        var definition = ResolveChannel(label, wordSdi);
                        if (definition == null)
                            continue;

                        if (ssm != SsmNormal)
                            continue;

                        var value = DecodeValue(data19);
                        if (!value.HasValue)
                            continue;

                        var list = samples[definition.SlotKey];
                        if (list.Count >= SamplesPerMeasure)
                            continue;

                        list.Add(value.Value);
                        var avg = list.Average();
                        setTextByName(definition.SlotKey, $"{value.Value:0.0} {PressureUnit}");
                    }

                    if (samples.Values.All(l => l.Count >= SamplesPerMeasure))
                    {
                        var range = GetExpectedRange(currentmA);

                        if (range == null)
                        {
                            foreach (var kv in samples)
                            {
                                var average = kv.Value.Average();
                                setTextByName(kv.Key, $"{average:0.0} {PressureUnit}");
                            }

                            Log($"{title}: 完成，自定义电流无判据范围，仅显示测量值");
                            return true;
                        }

                        var outOfRangeChannels = new List<string>();

                        foreach (var kv in samples)
                        {
                            var average = kv.Value.Average();
                            setTextByName(kv.Key, $"{average:0.0} {PressureUnit}");

                            if (!IsWithinRange(average, range.Value.min, range.Value.max))
                            {
                                outOfRangeChannels.Add($"{kv.Key}={average:0.0}");
                            }
                        }

                        if (outOfRangeChannels.Count == 0)
                        {
                            Log($"{title}: 完成，全部测点满足判据[{range.Value.min:0.##}, {range.Value.max:0.##}]");
                            return true;
                        }

                        Log($"{title}: 判定FAIL，压差值: {string.Join(", ", outOfRangeChannels)}，判据范围[{range.Value.min:0.##}, {range.Value.max:0.##}]");
                        return false;
                    }

                    await Task.Delay(10, cancellationToken).ConfigureAwait(false);
                }

                foreach (var key in samples.Keys)
                {
                    if (samples[key].Count >= SamplesPerMeasure)
                        setTextByName(key, $"{samples[key].Average():0.0} {PressureUnit}");
                    else
                        setTextByName(key, "超时");
                }

                if (IsManualTestRunning)
                {
                    var missing = string.Join(",", samples.Where(kv => kv.Value.Count < SamplesPerMeasure).Select(kv => kv.Key));
                    Log($"{title}: 接收超时，以下通道未获取到{SamplesPerMeasure}帧有效DPT数据: {missing}");
                }
                else if (IsAutoTestRunning)
                {
                    var missing = string.Join(",", samples.Where(kv => kv.Value.Count < SamplesPerMeasure).Select(kv => kv.Key));
                    Log($"{title}: 接收超时，以下通道未获取到{SamplesPerMeasure}帧有效DPT数据: {missing}");
                }

                return false;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception ex)
            {
                if (IsManualTestRunning)
                {
                    await AbortManualTestAsync($"{title}: 采集异常，手动测试中止: {ex.Message}").ConfigureAwait(false);
                }
                else if (IsAutoTestRunning)
                {
                    await AbortAutoTestAsync($"{title}: 采集异常，自动测试中止: {ex.Message}").ConfigureAwait(false);
                }

                return false;
            }
            finally
            {
                _measureLock.Release();
            }
        }

        private async Task DrainArincBufferAsync(CancellationToken cancellationToken)
        {
            for (int i = 0; i < 100; i++)
            {
                var batch = await _arinc.ReadRxWordsAsync(
                    RxChannelIndex, maxCount: 4096,
                    enableTimeTag: false, enableRateAdaption: false,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                if (batch.Count == 0)
                    break;
            }
        }

        private DptChannelDefinition ResolveChannel(byte label, byte sdi)
        {
            return DptChannels.FirstOrDefault(d => IsExpectedLabel(label, d.Label) && d.Sdi == sdi);
        }

        private bool IsExpectedLabel(byte label, byte expected)
        {
            return _arinc.ReverseLabel(label) == expected;
        }

        private double? DecodeValue(uint data19)
        {
            var value = _arinc.DecodeUbnr(data19, bitLength: DataBitLength, resolution: DataResolution, msbPosition: DataMsbPosition);
            if (value < 0 || value > 511)
                return null;

            return Math.Round(value, 1, MidpointRounding.AwayFromZero);
        }

        private (double min, double max)? GetExpectedRange(double currentmA)
        {
            if (Math.Abs(currentmA - Current4mA) < 0.001)
                return (Range4mAMin, Range4mAMax);

            if (Math.Abs(currentmA - Current20mA) < 0.001)
                return (Range20mAMin, Range20mAMax);

            if (Math.Abs(currentmA - Current10mA) < 0.001)
                return (Range10mAMin, Range10mAMax);

            return null;
        }

        private static bool IsWithinRange(double value, double min, double max)
        {
            return value >= min && value <= max;
        }

        private static double? ParseMeasurementValue(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            var trimmed = text.Trim();
            if (string.Equals(trimmed, "--", StringComparison.OrdinalIgnoreCase)
                || string.Equals(trimmed, "超时", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var firstToken = trimmed.Split(' ')[0];
            if (double.TryParse(firstToken, out var value))
                return value;

            return null;
        }

        private async Task TryFinalizeAsync()
        {
            if (!(_measured4mA && _measured20mA && _measured10mA))
                return;

            var resultText = (_passed4mA && _passed20mA && _passed10mA) ? "PASS" : "FAIL";
            var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            CurrentTestResult = resultText;
            PreviousTestTime = now;
            PreviousTestResult = resultText;
            LastTestTime = now;
            LastTestResult = resultText;
            SaveTestResultToProject();
            Log($"测试结果: {resultText}");
        }

        private async Task AbortManualTestAsync(string reason)
        {
            _manualAborted = true;
            if (!string.IsNullOrWhiteSpace(reason))
                Log(reason);

            await StopManualTestAsync().ConfigureAwait(false);
        }

        private async Task AbortAutoTestAsync(string reason)
        {
            if (!string.IsNullOrWhiteSpace(reason))
                Log(reason);

            await StopAutoTestAsync().ConfigureAwait(false);
        }

        private async Task StopManualTestAsync()
        {
            if (IsManualTestStopping)
            {
                return;
            }

            IsManualTestStopping = true;
            IsManualTestInitializing = false;
            try
            {
                CanMeasure = false;
                _manualCts?.Cancel();
            }
            catch
            {
            }

            Log($"手动测试停止/结束，正在断开设备...");
            try
            {
                await CleanupPowerAsync().ConfigureAwait(false);
                await CleanupMtxAsync().ConfigureAwait(false);
                await CleanupArincAsync().ConfigureAwait(false);
                await EnsureGroundDoAsync(on: false, cancellationToken: CancellationToken.None).ConfigureAwait(false);
                await EnsureRelay485Async(on: false, cancellationToken: CancellationToken.None).ConfigureAwait(false);
                await CleanupJy7131Async().ConfigureAwait(false);
            }
            finally
            {
                IsManualTestInitializing = false;
                IsManualTestRunning = false;
                IsManualTestStopping = false;
                RaisePropertyChanged(nameof(CanStartManualTest));
                RaisePropertyChanged(nameof(CanStartAutoTest));
                Log("手动测试已结束");
            }
        }

        private async Task StopAutoTestAsync()
        {
            if (IsAutoTestStopping)
            {
                return;
            }

            IsAutoTestStopping = true;
            IsAutoTestInitializing = false;
            try
            {
                _autoCts?.Cancel();
            }
            catch
            {
            }

            Log($"自动测试停止/结束，正在断开设备...");
            try
            {
                await CleanupPowerAsync().ConfigureAwait(false);
                await CleanupMtxAsync().ConfigureAwait(false);
                await CleanupArincAsync().ConfigureAwait(false);
                await EnsureGroundDoAsync(on: false, cancellationToken: CancellationToken.None).ConfigureAwait(false);
                await EnsureRelay485Async(on: false, cancellationToken: CancellationToken.None).ConfigureAwait(false);
                await CleanupJy7131Async().ConfigureAwait(false);
            }
            finally
            {
                IsAutoTestInitializing = false;
                IsAutoTestRunning = false;
                IsAutoTestStopping = false;
                RaisePropertyChanged(nameof(CanStartManualTest));
                RaisePropertyChanged(nameof(CanStartAutoTest));
                Log("自动测试已结束");
            }
        }

        private async Task EnsurePowerAsync(CancellationToken cancellationToken)
        {
            _auxPower ??= new PowerSupplySocketApi();
            await _auxPower.ConnectAsync(AuxiliaryPowerSupplyIpAddress, cancellationToken).ConfigureAwait(false);
            await _auxPower.ApplyAsync(PowerSupplyChannel.CH1, AuxiliaryInputVoltageV, AuxiliaryInputCurrentA, cancellationToken).ConfigureAwait(false);
            await _auxPower.SetOutputEnabledAsync(PowerSupplyChannel.CH1, true, cancellationToken).ConfigureAwait(false);

            if (!_boardPowerService.IsPowered)
            {
                var (confirmed, _) = PowerOnPromptDialog.ShowPrompt("液压单板", showVoltage: false);
                if (!confirmed) throw new OperationCanceledException("用户取消上电");
                await _boardPowerService.PowerOnAsync("液压单板", cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            await Task.Delay(300, cancellationToken).ConfigureAwait(false);
        }

        private async Task CleanupPowerAsync()
        {
            try
            {
                if (_power != null)
                {
                    try { await _power.DisconnectAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _power.DisposeAsync().ConfigureAwait(false); } catch { }
                }

                if (_auxPower != null)
                {
                    try { await _auxPower.SetOutputEnabledAsync(PowerSupplyChannel.CH1, false, CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _auxPower.DisconnectAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _auxPower.DisposeAsync().ConfigureAwait(false); } catch { }
                }
            }
            finally
            {
                _power = null;
                _auxPower = null;
            }
        }

        private async Task EnsureMtx532Async(CancellationToken cancellationToken)
        {
            if (_mtx532 != null && _mtx532.IsConnected)
                return;

            var device = FindFirstMtx532Device();
            if (device == null)
                throw new InvalidOperationException("未找到 MTX532 模拟量输出设备");

            var slot = device is PxiDeviceBase pxi ? pxi.SlotIndex : 7;
            _mtx532 = new Mtx532Api(device, options: new Mtx532Options { SampleRateHz = 20000.0 }, slotNumber: slot);
            await _mtx532.ConnectAsync(cancellationToken, AoChannels).ConfigureAwait(false);
            await SetAo67891011Async(0.0, cancellationToken).ConfigureAwait(false);
            await Task.Delay(300, cancellationToken).ConfigureAwait(false);
            await WaitForMtx532ReadyAsync(cancellationToken).ConfigureAwait(false);
            await _mtx532.StartOutputAsync(cancellationToken).ConfigureAwait(false);
            await Task.Delay(300, cancellationToken).ConfigureAwait(false);
        }

        private async Task WaitForMtx532ReadyAsync(CancellationToken cancellationToken)
        {
            if (_mtx532 == null || !_mtx532.IsConnected)
                throw new InvalidOperationException("MTX532未连接");

            var deadline = DateTime.UtcNow.AddMilliseconds(Mtx532ReadyTimeoutMs);
            while (DateTime.UtcNow <= deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (await _mtx532.CanStartOutputAsync(cancellationToken).ConfigureAwait(false))
                    return;

                await Task.Delay(Mtx532ReadyPollMs, cancellationToken).ConfigureAwait(false);
            }

            throw new InvalidOperationException("MTX532已连接，但在等待超时前仍未准备好输出");
        }

        private async Task CleanupMtxAsync()
        {
            try
            {
                if (_mtx532 != null)
                {
                    try { await _mtx532.StopOutputAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _mtx532.ResetAllToZeroAsync(disableAfterReset: true, cancellationToken: CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _mtx532.DisconnectAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _mtx532.DisposeAsync().ConfigureAwait(false); } catch { }
                }
            }
            finally
            {
                _mtx532 = null;
            }
        }

        private async Task SetAo67891011Async(double voltageV, CancellationToken cancellationToken)
        {
            if (_mtx532 == null || !_mtx532.IsConnected)
                throw new InvalidOperationException("MTX532未连接");

            var outputs = AoChannels.ToDictionary(ch => ch, _ => voltageV, StringComparer.OrdinalIgnoreCase);
            await _mtx532.WriteOnceDcAsync(outputs, cancellationToken).ConfigureAwait(false);
        }

        private double ConvertCurrentToVoltage(double currentmA)
        {
            return currentmA * 10.0 / 42.0;
        }

        private async Task EnsureArincRxAsync(CancellationToken cancellationToken)
        {
            if (_arinc == null)
            {
                var device = FindFirstArincDevice();
                if (device == null)
                    throw new InvalidOperationException("未找到ART4227/ART4229(ARINC429)板卡，无法接收429数据");
                _arinc = new Art4229Api(device, deviceIndex: 0);
            }

            if (!_arinc.IsConnected)
                await _arinc.ConnectAsync(cancellationToken).ConfigureAwait(false);

            await _arinc.OpenRxAsync(RxChannelIndex, cancellationToken).ConfigureAwait(false);
            await _arinc.ConfigureRxAsync(RxChannelIndex, ArincRate, Art4229Parity.Odd, Art4229WordFormat.Standard429, false, 512, false, cancellationToken).ConfigureAwait(false);
            await _arinc.StartRxAsync(RxChannelIndex, cancellationToken).ConfigureAwait(false);
            _ = await _arinc.ReadRxWordsAsync(RxChannelIndex, 4096, false, false, cancellationToken).ConfigureAwait(false);
        }

        private async Task CleanupArincAsync()
        {
            try
            {
                if (_arinc != null)
                {
                    try { await _arinc.StopRxAsync(RxChannelIndex, CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _arinc.CloseRxAsync(RxChannelIndex, CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _arinc.DisconnectAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _arinc.DisposeAsync().ConfigureAwait(false); } catch { }
                }
            }
            finally
            {
                _arinc = null;
            }
        }

        private async Task EnsureRelay485Async(bool on, CancellationToken cancellationToken)
        {
            await _relayLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (on)
                {
                    if (_isRelay485On)
                        return;

                    var device = FindFirstJy7131Device();
                    if (device == null)
                        throw new InvalidOperationException("未找到PXIe-7131(JY7131)板卡，无法开启485继电器");

                    if (_jy7131 == null)
                    {
                        var slot = device is DigitalIODevice dio ? dio.SlotIndex : 0;
                        _jy7131 = new Jy7131Api(device, slot);
                    }

                    if (!_jy7131.IsConnected)
                        await _jy7131.ConnectAsync(cancellationToken).ConfigureAwait(false);

                    if (!_jy7131.IsRunning)
                    {
                        await _jy7131.SetOutputModeAsync(Jy7131OutputMode.Sinking, cancellationToken).ConfigureAwait(false);
                        await _jy7131.StartAsync(cancellationToken).ConfigureAwait(false);
                    }

                    await _jy7131.SetRelayAsync(Relay485ChannelIndex, true, cancellationToken).ConfigureAwait(false);
                    _isRelay485On = true;
                }
                else
                {
                    if (!_isRelay485On)
                        return;

                    if (_jy7131 != null)
                    {
                        try
                        {
                            await _jy7131.SetRelayAsync(Relay485ChannelIndex, false, cancellationToken).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            Log($"关闭继电器板 第{Relay485ChannelIndex + 1}路失败: {ex.Message}");
                        }
                    }

                    _isRelay485On = false;
                }
            }
            finally
            {
                _relayLock.Release();
            }
        }

        private async Task EnsureGroundDoAsync(bool on, CancellationToken cancellationToken)
        {
            await _relayLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_jy7131 == null)
                {
                    var device = FindFirstJy7131Device();
                    if (device == null)
                        throw new InvalidOperationException("未找到PXIe-7131(JY7131)板卡，无法设置DO");

                    var slot = device is DigitalIODevice dio ? dio.SlotIndex : 0;
                    _jy7131 = new Jy7131Api(device, slot);
                }

                if (!_jy7131.IsConnected)
                    await _jy7131.ConnectAsync(cancellationToken).ConfigureAwait(false);

                if (!_jy7131.IsRunning)
                {
                    await _jy7131.SetOutputModeAsync(Jy7131OutputMode.Sinking, cancellationToken).ConfigureAwait(false);
                    await _jy7131.StartAsync(cancellationToken).ConfigureAwait(false);
                }

                await WriteInitDosAsync(on, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _relayLock.Release();
            }
        }

        private async Task WriteInitDosAsync(bool on, CancellationToken cancellationToken)
        {
            await _jy7131.WriteDoAsync($"DO{RelayAuxDoIndex}", on, cancellationToken).ConfigureAwait(false);

            await _jy7131.WriteDoAsync($"DO{RelayEnableDoIndex}", on, cancellationToken).ConfigureAwait(false);
        }

        private async Task CleanupJy7131Async()
        {
            try
            {
                if (_jy7131 != null)
                {
                    try { await _jy7131.StopAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _jy7131.DisconnectAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await _jy7131.DisposeAsync().ConfigureAwait(false); } catch { }
                }
            }
            finally
            {
                _jy7131 = null;
                _isRelay485On = false;
            }
        }

        private DeviceBase FindFirstArincDevice()
        {
            var chassisList = _pxiChassisService?.GetAllChassis();
            if (chassisList == null)
                return null;

            foreach (var chassis in chassisList)
            {
                var device = chassis?.Devices?.FirstOrDefault(d =>
                    (d?.Model?.IndexOf("4227", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.Model?.IndexOf("4229", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.Model?.IndexOf("ARINC", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.Model?.IndexOf("429", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.Name?.IndexOf("4227", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.Name?.IndexOf("4229", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.Name?.IndexOf("429", StringComparison.OrdinalIgnoreCase) >= 0));

                if (device != null)
                    return device;
            }

            return null;
        }

        private DeviceBase FindFirstJy7131Device()
        {
            var chassisList = _pxiChassisService?.GetAllChassis();
            if (chassisList == null)
                return null;

            foreach (var chassis in chassisList)
            {
                var device = chassis?.Devices?.FirstOrDefault(d =>
                    d is DigitalIODevice ||
                    (d?.Model?.IndexOf("7131", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.DeviceTypeName?.IndexOf("离散量", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.DeviceTypeName?.IndexOf("数字量", StringComparison.OrdinalIgnoreCase) >= 0));

                if (device != null)
                    return device;
            }

            return null;
        }

        private DeviceBase FindFirstMtx532Device()
        {
            var chassisList = _pxiChassisService?.GetAllChassis();
            if (chassisList == null)
                return null;

            foreach (var chassis in chassisList)
            {
                var device = chassis?.Devices?.FirstOrDefault(d =>
                    (d?.Model?.IndexOf("X532", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d?.Name?.IndexOf("mtx532", StringComparison.OrdinalIgnoreCase) >= 0));

                if (device != null)
                    return device;
            }

            return null;
        }

        private void ResetAllDisplays()
        {
            DptEdp24mAText = "--";
            DptEmp2B4mAText = "--";
            DptEmp3B4mAText = "--";
            DptSys14mAText = "--";
            DptSys24mAText = "--";
            DptSys34mAText = "--";

            DptEdp2A20mAText = "--";
            DptEmp2B20mAText = "--";
            DptEmp3B20mAText = "--";
            DptSys120mAText = "--";
            DptSys220mAText = "--";
            DptSys320mAText = "--";

            DptEdp2A10mAText = "--";
            DptEmp2B10mAText = "--";
            DptEmp3B10mAText = "--";
            DptSys110mAText = "--";
            DptSys210mAText = "--";
            DptSys310mAText = "--";

            DptEdp2CustommAText = "--";
            DptEmp2BCustommAText = "--";
            DptEmp3BCustommAText = "--";
            DptSys1CustommAText = "--";
            DptSys2CustommAText = "--";
            DptSys3CustommAText = "--";
        }

        private string NormalizeCurrentInput(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var sanitized = value.Replace("mA", string.Empty).Replace("MA", string.Empty).Replace("ma", string.Empty).Trim();
            sanitized = sanitized.Replace(',', '.');

            var chars = new List<char>(sanitized.Length);
            var hasDot = false;
            var decimalCount = 0;
            foreach (var ch in sanitized)
            {
                if (char.IsDigit(ch))
                {
                    if (hasDot)
                    {
                        if (decimalCount >= 1)
                            continue;

                        decimalCount++;
                    }

                    chars.Add(ch);
                    continue;
                }

                if (ch == '.' && !hasDot)
                {
                    hasDot = true;
                    chars.Add(ch);
                }
            }

            return new string(chars.ToArray());
        }

        private bool TryGetValidatedCustomCurrent(out double currentmA)
        {
            currentmA = 0;
            var text = NormalizeCurrentInput(CustomCurrentInput);
            if (string.IsNullOrWhiteSpace(text))
                return false;

            if (text.EndsWith(".", StringComparison.Ordinal))
                text = text.TrimEnd('.');

            if (!double.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out currentmA))
                return false;

            currentmA = Math.Truncate(currentmA * 10d) / 10d;
            return currentmA >= 4d && currentmA <= 20d;
        }

        private void Log(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke(new Action(() => Logs.Add(line)));
                return;
            }

            Logs.Add(line);
        }
    }
}
