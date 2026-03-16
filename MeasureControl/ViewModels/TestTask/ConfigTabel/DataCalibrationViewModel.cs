using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.IO;
using System.Windows.Input;
using MeasureControl.Helpers;
using MeasureControl.Models;
using MeasureControl.Services;
using Microsoft.Win32;
using Prism.Commands;
using Prism.Mvvm;
using MeasureControl.Views.Dialogs;
using System.Windows.Media;
using MeasureControl.Events;
using Prism.Events;
using System.ComponentModel;

namespace MeasureControl.ViewModels.TestTask.ConfigTabel
{
    /// <summary>
    /// 数据标定界面的ViewModel
    /// </summary>
    public class DataCalibrationViewModel : BindableBase
    {
        private readonly CalibrationFileService _calibrationFileService;
        private readonly IEventAggregator _eventAggregator;

        private int _aiChannelCount = 32;
        private string _channelPrefix = "AI";
        private string _currentContextKey;
        private List<string> _explicitChannelAddresses;

        private string _currentDeviceId;
        private Dictionary<string, ChannelCalibrationRecord> _projectCalibrationRecords = new Dictionary<string, ChannelCalibrationRecord>(StringComparer.OrdinalIgnoreCase);

        private ProjectItem _currentProject;

        // Command字段
        private DelegateCommand _calculateCalibrationCommand;
        private DelegateCommand _saveToFileCommand;
        private DelegateCommand _browseCalibrationFileCommand;
        private DelegateCommand _importCalibrationCommand;
        private DelegateCommand _exportCalibrationCommand;
        private DelegateCommand<ChannelCalibrationRecord> _calibrationRecordDoubleClickCommand;

        public DataCalibrationViewModel(IEventAggregator eventAggregator)
        {
            _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
            _calibrationFileService = new CalibrationFileService();
            InitializeViewModel();
        }

        public void SetAiChannelCount(int count)
        {
            _aiChannelCount = count > 0 ? count : 32;
        }

        private void SetChannelPrefix(string prefix)
        {
            if (string.IsNullOrWhiteSpace(prefix))
                return;

            _channelPrefix = prefix.Trim();
        }

        public void SetCurrentDevice(string deviceId)
        {
            _currentDeviceId = deviceId ?? string.Empty;
        }

        public void SetProjectContext(ProjectItem project)
        {
            _currentProject = project;
            SetProjectCalibrationRecords(project?.CalibrationRecords);
        }

        public void SetProjectCalibrationRecords(Dictionary<string, ChannelCalibrationRecord> records)
        {
            _projectCalibrationRecords = records != null
                ? new Dictionary<string, ChannelCalibrationRecord>(records, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, ChannelCalibrationRecord>(StringComparer.OrdinalIgnoreCase);

            if (!string.IsNullOrWhiteSpace(_currentDeviceId))
            {
                RefreshRecordsForCurrentDevice();
            }
        }

        private void CommitCurrentDeviceRecordsToProjectMemory()
        {
            if (_currentProject == null)
                return;

            if (_currentProject.CalibrationRecords == null)
            {
                _currentProject.CalibrationRecords = new Dictionary<string, ChannelCalibrationRecord>();
            }

            if (CalibrationRecords == null)
                return;

            foreach (var record in CalibrationRecords)
            {
                if (record == null || string.IsNullOrWhiteSpace(record.ChannelAddress))
                    continue;

                var scopedKey = GetScopedKey(record.ChannelAddress);
                if (string.IsNullOrWhiteSpace(scopedKey))
                    continue;

                _currentProject.CalibrationRecords[scopedKey] = record;
                _projectCalibrationRecords[scopedKey] = record;
            }
        }

        public void ApplyAnalogInputContext(int channelCount)
        {
            SetAiChannelCount(channelCount);
            SetChannelPrefix("AI");
            ApplyChannelContextInternal();
        }

        public void ApplyAnalogOutputContext(int channelCount)
        {
            SetAiChannelCount(channelCount);
            SetChannelPrefix("AO");
            ApplyChannelContextInternal();
        }

        public void ApplyAnalogInputContext(string deviceId, int channelCount)
        {
            SetCurrentDevice(deviceId);
            ApplyAnalogInputContext(channelCount);
            RefreshRecordsForCurrentDevice();
        }

        public void ApplyAnalogOutputContext(string deviceId, int channelCount)
        {
            SetCurrentDevice(deviceId);
            ApplyAnalogOutputContext(channelCount);
            RefreshRecordsForCurrentDevice();
        }

        public void ApplyExplicitSignalContext(string deviceId, IEnumerable<string> channelAddresses, string contextKey)
        {
            SetCurrentDevice(deviceId);
            _explicitChannelAddresses = channelAddresses?
                .Where(address => !string.IsNullOrWhiteSpace(address))
                .Select(address => address.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? new List<string>();

            var key = $"{_currentDeviceId}|CUSTOM|{contextKey}|{string.Join("|", _explicitChannelAddresses)}";
            if (string.Equals(_currentContextKey, key, StringComparison.OrdinalIgnoreCase))
            {
                RefreshRecordsForCurrentDevice();
                return;
            }

            _currentContextKey = key;

            InitializeFixedAnalogChannels();
            EnsureCalibrationRecordsForAvailableChannels();
            ResetUiToDefaults();
            LoadCurrentChannelData();
            RefreshRecordsForCurrentDevice();
        }

        private void ApplyChannelContextInternal()
        {
            // 标定界面仅通过板卡“标定”按钮打开，通道上下文由调用方注入。
            // 为避免不同板卡类型扩展时产生多次初始化/闪烁，这里统一延迟到 Apply*Context 才初始化通道列表与默认记录。

            _explicitChannelAddresses = null;

            var key = $"{_currentDeviceId}|{_channelPrefix}|{_aiChannelCount}";
            if (string.Equals(_currentContextKey, key, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            _currentContextKey = key;

            InitializeFixedAnalogChannels();
            EnsureCalibrationRecordsForAvailableChannels();
            ResetUiToDefaults();
            LoadCurrentChannelData();
        }

        private string GetScopedKey(string channelAddress)
        {
            if (string.IsNullOrWhiteSpace(channelAddress))
                return string.Empty;

            if (string.IsNullOrWhiteSpace(_currentDeviceId))
                return channelAddress;

            return $"{_currentDeviceId}/{channelAddress}";
        }

        private bool TryParseScopedKey(string key, out string deviceId, out string channelAddress)
        {
            deviceId = string.Empty;
            channelAddress = string.Empty;

            if (string.IsNullOrWhiteSpace(key))
                return false;

            var idx = key.IndexOf('/');
            if (idx <= 0 || idx >= key.Length - 1)
                return false;

            deviceId = key.Substring(0, idx);
            channelAddress = key.Substring(idx + 1);
            return true;
        }

        private void RefreshRecordsForCurrentDevice()
        {
            if (AvailableChannelAddresses == null || AvailableChannelAddresses.Count == 0)
                return;

            if (CalibrationRecords == null)
                CalibrationRecords = new ObservableCollection<ChannelCalibrationRecord>();

            CalibrationRecords.Clear();

            foreach (var address in AvailableChannelAddresses)
            {
                CalibrationRecords.Add(CreateDefaultRecord(address));
            }

            if (!string.IsNullOrWhiteSpace(_currentDeviceId) && _projectCalibrationRecords != null && _projectCalibrationRecords.Count > 0)
            {
                var prefix = $"{_currentDeviceId}/";
                bool appliedAny = false;
                foreach (var kvp in _projectCalibrationRecords)
                {
                    if (kvp.Key != null && kvp.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        var record = kvp.Value;
                        if (record == null)
                            continue;

                        var addr = record.ChannelAddress;
                        if (string.IsNullOrWhiteSpace(addr))
                        {
                            if (TryParseScopedKey(kvp.Key, out _, out var parsedAddr))
                                addr = parsedAddr;
                        }

                        if (string.IsNullOrWhiteSpace(addr))
                            continue;

                        var existing = CalibrationRecords.FirstOrDefault(r =>
                            string.Equals(r.ChannelAddress, addr, StringComparison.OrdinalIgnoreCase));
                        if (existing != null)
                        {
                            existing.ChannelName = record.ChannelName;
                            existing.Slope = record.Slope;
                            existing.Intercept = record.Intercept;
                            existing.IsCalibrated = record.IsCalibrated;
                            existing.LastCalibrationTime = record.LastCalibrationTime;
                            existing.MeasurementPointCount = record.MeasurementPointCount;
                            existing.InstrumentSetValues = record.InstrumentSetValues;
                            existing.CardMeasuredValues = record.CardMeasuredValues;
                            appliedAny = true;
                        }
                    }
                }
            }

            RaisePropertyChanged(nameof(CalibrationRecords));
            ResetUiToDefaults();
            LoadCurrentChannelData();
        }

        private void InitializeViewModel()
        {
            System.Diagnostics.Debug.WriteLine("[DataCalibration] DataCalibrationViewModel InitializeViewModel called");

            // 初始化集合（必须在设置MeasurementPointCount之前，因为setter会调用UpdateMeasurementPointInputs）
            InstrumentSetValues = new ObservableCollection<MeasurementPoint>();
            CardMeasuredValues = new ObservableCollection<MeasurementPoint>();
            CalibrationRecords = new ObservableCollection<ChannelCalibrationRecord>();

            // 初始化命令
            _calculateCalibrationCommand = new DelegateCommand(OnCalculateCalibration, CanCalculateCalibration);
            _saveToFileCommand = new DelegateCommand(OnSaveToFile, CanSaveToFile);
            _browseCalibrationFileCommand = new DelegateCommand(OnBrowseCalibrationFile);
            _importCalibrationCommand = new DelegateCommand(OnImportCalibration);
            _exportCalibrationCommand = new DelegateCommand(OnExportCalibration);
            _calibrationRecordDoubleClickCommand = new DelegateCommand<ChannelCalibrationRecord>(OnCalibrationRecordDoubleClick);

            // 订阅标定数据请求事件和加载事件
            // 先取消订阅，避免重复订阅
            _eventAggregator?.GetEvent<CalibrationRecordsRequestEvent>()?.Unsubscribe(OnCalibrationRecordsRequested);
            _eventAggregator?.GetEvent<CalibrationRecordsLoadEvent>()?.Unsubscribe(OnCalibrationRecordsLoaded);
            _eventAggregator?.GetEvent<ProjectOpenedEvent>()?.Unsubscribe(OnProjectOpened);
            _eventAggregator?.GetEvent<ProjectClosedEvent>()?.Unsubscribe(OnProjectClosed);

            // 重新订阅
            _eventAggregator?.GetEvent<CalibrationRecordsRequestEvent>()?.Subscribe(OnCalibrationRecordsRequested);
            _eventAggregator?.GetEvent<CalibrationRecordsLoadEvent>()?.Subscribe(OnCalibrationRecordsLoaded);
            _eventAggregator?.GetEvent<ProjectOpenedEvent>()?.Subscribe(OnProjectOpened);
            _eventAggregator?.GetEvent<ProjectClosedEvent>()?.Subscribe(OnProjectClosed);

            System.Diagnostics.Debug.WriteLine($"[DataCalibration] Events subscribed successfully. EventAggregator is null: {_eventAggregator == null}");

            // 初始化标定存储路径
            InitializeCalibrationStorage();

            // ⚠️ 通道列表/默认记录初始化延迟到 Apply*Context（由板卡标定按钮注入具体上下文）

            System.Diagnostics.Debug.WriteLine("[DataCalibration] DataCalibrationViewModel InitializeViewModel completed");
        }

        private void EnsureCalibrationRecordsForAvailableChannels()
        {
            if (AvailableChannelAddresses == null)
                return;

            if (CalibrationRecords == null)
                CalibrationRecords = new ObservableCollection<ChannelCalibrationRecord>();

            var allowed = new HashSet<string>(AvailableChannelAddresses, StringComparer.OrdinalIgnoreCase);

            for (int i = CalibrationRecords.Count - 1; i >= 0; i--)
            {
                var addr = CalibrationRecords[i]?.ChannelAddress;
                if (string.IsNullOrWhiteSpace(addr) || !allowed.Contains(addr))
                {
                    CalibrationRecords.RemoveAt(i);
                }
            }

            foreach (var address in AvailableChannelAddresses)
            {
                if (string.IsNullOrWhiteSpace(address))
                    continue;

                var existing = CalibrationRecords.FirstOrDefault(r =>
                    string.Equals(r.ChannelAddress, address, StringComparison.OrdinalIgnoreCase));
                if (existing == null)
                {
                    CalibrationRecords.Add(CreateDefaultRecord(address));
                }
                else
                {
                    EnsureRecordDefaults(existing);
                }
            }

            RaisePropertyChanged(nameof(CalibrationRecords));
            ResetUiToDefaults();
        }

        private int _measurementPointCount;
        private double _slope;
        private double _intercept;
        private string _channelName;
        private string _calibrationFilePath;
        private string _calibrationFolderPath;
        private string _selectedChannelAddress;
        private PointCollection _calibrationLinePoints = new PointCollection();
        private ObservableCollection<MeasurementPoint> _instrumentSetValues;
        private ObservableCollection<MeasurementPoint> _cardMeasuredValues;
        private ObservableCollection<ChannelCalibrationRecord> _calibrationRecords;
        private ObservableCollection<System.Windows.Point> _calibrationPoints = new ObservableCollection<System.Windows.Point>();
        private ObservableCollection<System.Windows.Point> _calibrationLine = new ObservableCollection<System.Windows.Point>();
        private ObservableCollection<AxisTick> _xAxisTicks = new ObservableCollection<AxisTick>();
        private ObservableCollection<AxisTick> _yAxisTicks = new ObservableCollection<AxisTick>();
        private ObservableCollection<string> _availableChannelAddresses;
        private bool _hasValidCalibrationResult;

        /// <summary>
        /// 通道名称
        /// </summary>
        public string ChannelName
        {
            get => _channelName;
            set => SetProperty(ref _channelName, value);
        }

        /// <summary>
        /// 测量点数
        /// </summary>
        public int MeasurementPointCount
        {
            get => _measurementPointCount;
            set
            {
                if (SetProperty(ref _measurementPointCount, value))
                {
                    UpdateMeasurementPointInputs();
                    InvalidateCalibrationResult();
                    ((DelegateCommand)CalculateCalibrationCommand).RaiseCanExecuteChanged();
                }
            }
        }

        /// <summary>
        /// 仪器设定值列表
        /// </summary>
        public ObservableCollection<MeasurementPoint> InstrumentSetValues
        {
            get => _instrumentSetValues;
            set => SetProperty(ref _instrumentSetValues, value);
        }

        /// <summary>
        /// 板卡测量值列表
        /// </summary>
        public ObservableCollection<MeasurementPoint> CardMeasuredValues
        {
            get => _cardMeasuredValues;
            set => SetProperty(ref _cardMeasuredValues, value);
        }

        /// <summary>
        /// 斜率
        /// </summary>
        public double Slope
        {
            get => _slope;
            set => SetProperty(ref _slope, value);
        }

        /// <summary>
        /// 截距
        /// </summary>
        public double Intercept
        {
            get => _intercept;
            set => SetProperty(ref _intercept, value);
        }

        /// <summary>
        /// 通道校准记录列表
        /// </summary>
        public ObservableCollection<ChannelCalibrationRecord> CalibrationRecords
        {
            get => _calibrationRecords;
            set => SetProperty(ref _calibrationRecords, value);
        }

        /// <summary>校准散点（X=仪器设定值，Y=板卡测量值）</summary>
        public ObservableCollection<System.Windows.Point> CalibrationPoints
        {
            get => _calibrationPoints;
            set => SetProperty(ref _calibrationPoints, value);
        }

        /// <summary>校准拟合线（两个端点用于绘制直线）</summary>
        public ObservableCollection<System.Windows.Point> CalibrationLine
        {
            get => _calibrationLine;
            set => SetProperty(ref _calibrationLine, value);
        }

        /// <summary>校准拟合线点集合（用于Polyline绑定）</summary>
        public PointCollection CalibrationLinePoints
        {
            get => _calibrationLinePoints;
            set => SetProperty(ref _calibrationLinePoints, value);
        }

        public ObservableCollection<AxisTick> XAxisTicks
        {
            get => _xAxisTicks;
            set => SetProperty(ref _xAxisTicks, value);
        }

        public ObservableCollection<AxisTick> YAxisTicks
        {
            get => _yAxisTicks;
            set => SetProperty(ref _yAxisTicks, value);
        }

        /// <summary>
        /// 标定数据存储目录
        /// </summary>
        public string CalibrationFolderPath
        {
            get => _calibrationFolderPath;
            set => SetProperty(ref _calibrationFolderPath, value);
        }

        /// <summary>
        /// 标定文件路径
        /// </summary>
        public string CalibrationFilePath
        {
            get => _calibrationFilePath;
            set => SetProperty(ref _calibrationFilePath, value);
        }

        /// <summary>
        /// 可选通道地址列表
        /// </summary>
        public ObservableCollection<string> AvailableChannelAddresses
        {
            get => _availableChannelAddresses;
            set => SetProperty(ref _availableChannelAddresses, value);
        }

        /// <summary>
        /// 当前选择的通道地址
        /// </summary>
        public string SelectedChannelAddress
        {
            get => _selectedChannelAddress;
            set
            {
                if (SetProperty(ref _selectedChannelAddress, value))
                {
                    // 同步到 ChannelName
                    if (!string.IsNullOrEmpty(value))
                    {
                        ChannelName = value;
                    }

                    InvalidateCalibrationResult();
                }
            }
        }

        /// <summary>
        /// 计算校准参数命令
        /// </summary>
        public ICommand CalculateCalibrationCommand => _calculateCalibrationCommand;

        /// <summary>
        /// 保存到文件命令
        /// </summary>
        public ICommand SaveToFileCommand => _saveToFileCommand;

        /// <summary>
        /// 浏览标定文件命令
        /// </summary>
        public ICommand BrowseCalibrationFileCommand => _browseCalibrationFileCommand;

        /// <summary>
        /// 导入标定文件命令
        /// </summary>
        public ICommand ImportCalibrationCommand => _importCalibrationCommand;

        /// <summary>
        /// 导出标定文件命令
        /// </summary>
        public ICommand ExportCalibrationCommand => _exportCalibrationCommand;

        /// <summary>
        /// 双击校准记录命令
        /// </summary>
        public ICommand CalibrationRecordDoubleClickCommand => _calibrationRecordDoubleClickCommand;

        // 为了向后兼容保留无参数构造函数，但标记为过时

        /// <summary>
        /// 初始化固定AI0-AI15模拟通道地址
        /// </summary>
        private void InitializeFixedAnalogChannels()
        {
            var addresses = new List<string>();
            if (_explicitChannelAddresses != null && _explicitChannelAddresses.Count > 0)
            {
                addresses.AddRange(_explicitChannelAddresses);
            }
            else
            {
                var count = _aiChannelCount > 0 ? _aiChannelCount : 32;
                for (int i = 0; i < count; i++)
                {
                    addresses.Add($"{_channelPrefix}{i}");
                }
            }
            AvailableChannelAddresses = new ObservableCollection<string>(addresses);

            // 设置默认选择AI0
            if ((string.IsNullOrEmpty(SelectedChannelAddress) || !AvailableChannelAddresses.Contains(SelectedChannelAddress)) && AvailableChannelAddresses.Count > 0)
            {
                SelectedChannelAddress = AvailableChannelAddresses[0];
            }
        }

        /// <summary>
        /// 重新加载项目标定数据（用于界面初始化时）
        /// </summary>
        public void ReloadProjectCalibrationData()
        {
            System.Diagnostics.Debug.WriteLine("[DataCalibration] ReloadProjectCalibrationData called");

            try
            {
                // 尝试从项目文件中重新加载数据
                string projectPath = GetCurrentProjectPath();
                if (!string.IsNullOrEmpty(projectPath) && System.IO.File.Exists(projectPath))
                {
                    System.Diagnostics.Debug.WriteLine($"[DataCalibration] Reloading from project file: {projectPath}");

                    var projectJson = System.IO.File.ReadAllText(projectPath);
                    var projectData = Newtonsoft.Json.JsonConvert.DeserializeObject<MeasureControl.Models.ProjectItem>(projectJson);

                    if (projectData?.CalibrationRecords != null && projectData.CalibrationRecords.Count > 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"[DataCalibration] Found {projectData.CalibrationRecords.Count} calibration records in project file");

                        // 模拟OnCalibrationRecordsLoaded的逻辑
                        var args = new CalibrationRecordsLoadEventArgs { CalibrationRecords = projectData.CalibrationRecords };
                        OnCalibrationRecordsLoaded(args);
                        return;
                    }
                }

                // 如果项目文件没有数据，尝试从全局标定服务获取
                var service = Services.CalibrationService.Instance;
                if (service != null)
                {
                    System.Diagnostics.Debug.WriteLine("[DataCalibration] Trying to reload from global calibration service");

                    var projectData = new Dictionary<string, ChannelCalibrationRecord>();
                    bool hasData = false;

                    // 从全局服务中获取所有通道的数据
                    foreach (var address in AvailableChannelAddresses ?? new ObservableCollection<string>())
                    {
                        var (slope, intercept, isCalibrated) = service.GetCalibrationParams(GetScopedKey(address));
                        if (isCalibrated || slope != 1.0 || intercept != 0.0)
                        {
                            // 如果有有效的标定数据，创建记录
                            var record = new ChannelCalibrationRecord
                            {
                                ChannelAddress = address,
                                ChannelName = address, // 默认名称
                                Slope = slope,
                                Intercept = intercept,
                                IsCalibrated = isCalibrated,
                                MeasurementPointCount = 5 // 默认值
                            };
                            projectData[GetScopedKey(address)] = record;
                            hasData = true;
                        }
                    }

                    if (hasData)
                    {
                        System.Diagnostics.Debug.WriteLine($"[DataCalibration] Reloaded {projectData.Count} calibration records from global service");

                        // 模拟OnCalibrationRecordsLoaded的逻辑
                        var args = new CalibrationRecordsLoadEventArgs { CalibrationRecords = projectData };
                        OnCalibrationRecordsLoaded(args);
                        return;
                    }
                }

                System.Diagnostics.Debug.WriteLine("[DataCalibration] No calibration data found anywhere");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DataCalibration] Error reloading project calibration data: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取当前项目文件路径
        /// </summary>
        private string GetCurrentProjectPath()
        {
            // 从CalibrationPathHelper获取项目路径（项目名.json）
            var projectDir = Path.GetDirectoryName(CalibrationPathHelper.DefaultFolder);
            var projectName = Path.GetFileNameWithoutExtension(CalibrationPathHelper.DefaultFile);

            if (!string.IsNullOrEmpty(projectDir) && !string.IsNullOrEmpty(projectName))
            {
                // 构造项目文件路径：项目目录/项目名.json
                var projectPath = Path.Combine(projectDir, $"{projectName.Replace("_校准数据", "")}.json");
                return projectPath;
            }

            return string.Empty;
        }

        /// <summary>
        /// 每次打开界面都加载当前选中通道的数据（默认为AI0）
        /// </summary>
        public void LoadCurrentChannelData()
        {
            System.Diagnostics.Debug.WriteLine("[DataCalibration] LoadCurrentChannelData called");

            // 如果没有选中通道，默认选择AI0
            if (string.IsNullOrEmpty(SelectedChannelAddress))
            {
                var defaultAddr = AvailableChannelAddresses != null && AvailableChannelAddresses.Count > 0
                    ? AvailableChannelAddresses[0]
                    : string.Empty;

                System.Diagnostics.Debug.WriteLine($"[DataCalibration] No channel selected, defaulting to {defaultAddr}");
                SelectedChannelAddress = defaultAddr;
            }

            System.Diagnostics.Debug.WriteLine($"[DataCalibration] Loading data for channel: {SelectedChannelAddress}");

            // 查找当前选中通道的记录
            var currentRecord = CalibrationRecords?.FirstOrDefault(r => r.ChannelAddress == SelectedChannelAddress);
            if (currentRecord != null)
            {
                System.Diagnostics.Debug.WriteLine($"[DataCalibration] Found record for {SelectedChannelAddress}: Name={currentRecord.ChannelName}, Points={currentRecord.MeasurementPointCount}, Calibrated={currentRecord.IsCalibrated}");

                // 加载通道的数据到界面
                ChannelName = currentRecord.ChannelName;
                int pointCount = currentRecord.MeasurementPointCount > 0 ? currentRecord.MeasurementPointCount : 5;
                System.Diagnostics.Debug.WriteLine($"[DataCalibration] Setting MeasurementPointCount to {pointCount} (from record: {currentRecord.MeasurementPointCount})");
                MeasurementPointCount = pointCount;

                if (currentRecord.IsCalibrated)
                {
                    System.Diagnostics.Debug.WriteLine($"[DataCalibration] Loading calibrated data: Slope={currentRecord.Slope}, Intercept={currentRecord.Intercept}");
                    Slope = currentRecord.Slope;
                    Intercept = currentRecord.Intercept;

                    // 填充仪器设定值
                    if (currentRecord.InstrumentSetValues != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"[DataCalibration] Loading {currentRecord.InstrumentSetValues.Count} instrument set values, UI collection size: {InstrumentSetValues.Count}");
                        for (int i = 0; i < currentRecord.InstrumentSetValues.Count && i < InstrumentSetValues.Count; i++)
                        {
                            if (InstrumentSetValues[i] != null)
                            {
                                InstrumentSetValues[i].Value = currentRecord.InstrumentSetValues[i];
                                System.Diagnostics.Debug.WriteLine($"[DataCalibration] Set InstrumentSetValues[{i}] = {currentRecord.InstrumentSetValues[i]}");
                            }
                            else
                            {
                                System.Diagnostics.Debug.WriteLine($"[DataCalibration] InstrumentSetValues[{i}] is null!");
                            }
                        }
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("[DataCalibration] No instrument set values to load");
                    }

                    // 填充板卡测量值
                    if (currentRecord.CardMeasuredValues != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"[DataCalibration] Loading {currentRecord.CardMeasuredValues.Count} card measured values, UI collection size: {CardMeasuredValues.Count}");
                        for (int i = 0; i < currentRecord.CardMeasuredValues.Count && i < CardMeasuredValues.Count; i++)
                        {
                            if (CardMeasuredValues[i] != null)
                            {
                                CardMeasuredValues[i].Value = currentRecord.CardMeasuredValues[i];
                                System.Diagnostics.Debug.WriteLine($"[DataCalibration] Set CardMeasuredValues[{i}] = {currentRecord.CardMeasuredValues[i]}");
                            }
                            else
                            {
                                System.Diagnostics.Debug.WriteLine($"[DataCalibration] CardMeasuredValues[{i}] is null!");
                            }
                        }
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("[DataCalibration] No card measured values to load");
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[DataCalibration] Record for {SelectedChannelAddress} is not calibrated, resetting to defaults");
                    // 未校准，重置斜率和截距
                    Slope = 1.0;
                    Intercept = 0.0;
                    ClearMeasurementInputs();
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[DataCalibration] Record for {SelectedChannelAddress} not found, using defaults");
                // 如果没有找到记录，使用默认值
                ChannelName = SelectedChannelAddress;
                MeasurementPointCount = 5;
                Slope = 1.0;
                Intercept = 0.0;
                ClearMeasurementInputs();
            }

            System.Diagnostics.Debug.WriteLine($"[DataCalibration] LoadCurrentChannelData completed: SelectedChannel={SelectedChannelAddress}, Slope={Slope}, Intercept={Intercept}");
            UpdateCalibrationPlot();
        }

        /// <summary>
        /// 初始化标定存储路径（默认 DataCalibration/项目名_校准数据.json）
        /// </summary>
        private void InitializeCalibrationStorage()
            {
            // 默认目录仅供导入/导出对话框使用，不自动填充文件路径
            var folder = CalibrationPathHelper.DefaultFolder;
            if (string.IsNullOrEmpty(folder))
                {
                folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DataCalibration");
                }
            CalibrationFolderPath = folder;
            CalibrationFilePath = string.Empty; // 新项目默认留空，导入/导出后再记录
        }

        /// <summary>
        /// 初始化/刷新校准记录，优先读取设备中的记录，其次按通道地址补全默认记录
        /// </summary>
        private void InitializeCalibrationRecords(bool restoreFromState)
        {
            CalibrationRecords.Clear();

            // 直接为AI0-AI15创建默认记录
            if (AvailableChannelAddresses != null)
            {
                foreach (var address in AvailableChannelAddresses)
                {
                    CalibrationRecords.Add(CreateDefaultRecord(address));
                }
            }

            // 如果有文件路径，尝试从文件加载
            if (!string.IsNullOrEmpty(CalibrationFilePath))
            {
                var fileRecords = _calibrationFileService.LoadCalibrationRecords(CalibrationFilePath);
                if (fileRecords != null && fileRecords.Count > 0)
            {
                    // 合并文件记录到现有记录
                    foreach (var fileRecord in fileRecords)
                {
                        var existingRecord = CalibrationRecords.FirstOrDefault(r => r.ChannelAddress == fileRecord.ChannelAddress);
                        if (existingRecord != null)
                        {
                            existingRecord.ChannelName = fileRecord.ChannelName; // 复制用户修改的通道名称
                            existingRecord.Slope = fileRecord.Slope;
                            existingRecord.Intercept = fileRecord.Intercept;
                            existingRecord.IsCalibrated = fileRecord.IsCalibrated;
                            existingRecord.LastCalibrationTime = fileRecord.LastCalibrationTime;
                            existingRecord.MeasurementPointCount = fileRecord.MeasurementPointCount;
                            existingRecord.InstrumentSetValues = fileRecord.InstrumentSetValues;
                            existingRecord.CardMeasuredValues = fileRecord.CardMeasuredValues;
                        }
                    }
                }
            }

            if (CalibrationRecords.Count == 0)
            {
                ResetUiToDefaults();
                return;
            }

            if (restoreFromState)
            {
                UpdateCalibrationPlot();
            }
            else
            {
                ResetUiToDefaults();
            }
        }

        private ChannelCalibrationRecord CreateDefaultRecord(string address)
        {
            return new ChannelCalibrationRecord
            {
                ChannelAddress = address,
                ChannelName = address, // 初始时名称与地址相同
                IsCalibrated = false,
                Slope = 1.0,
                Intercept = 0.0,
                MeasurementPointCount = 5
            };
        }

        /// <summary>
        /// 确保记录具备基础信息（点数、类型等）
        /// </summary>
        private void EnsureRecordDefaults(ChannelCalibrationRecord record)
        {
            if (record == null) return;

            if (record.MeasurementPointCount <= 0)
            {
                record.MeasurementPointCount = 5;
            }


            if (!record.IsCalibrated && Math.Abs(record.Slope) < double.Epsilon && Math.Abs(record.Intercept) < double.Epsilon)
            {
                record.Slope = 1.0;
                record.Intercept = 0.0;
            }
        }

        private string ExtractChannelAddress(string channelName)
        {
            if (string.IsNullOrWhiteSpace(channelName))
                return string.Empty;

            var spaceIndex = channelName.IndexOf(' ');
            if (spaceIndex > 0)
                return channelName.Substring(0, spaceIndex);

            return channelName.Trim();
        }


        private void ClearMeasurementInputs()
        {
            if (InstrumentSetValues != null)
            {
                foreach (var point in InstrumentSetValues)
                {
                    point.Value = null;
                }
            }

            if (CardMeasuredValues != null)
            {
                foreach (var point in CardMeasuredValues)
                {
                    point.Value = null;
                }
            }
        }

        private void ResetUiToDefaults(string preferredAddress = null)
        {
            var address = preferredAddress;

            if (string.IsNullOrWhiteSpace(address))
            {
                address = SelectedChannelAddress;
            }

            if (string.IsNullOrWhiteSpace(address) && AvailableChannelAddresses != null && AvailableChannelAddresses.Count > 0)
            {
                address = AvailableChannelAddresses[0];
            }

            if (string.IsNullOrWhiteSpace(address) && CalibrationRecords != null && CalibrationRecords.Count > 0)
            {
                address = ExtractChannelAddress(CalibrationRecords[0]?.ChannelName);
            }

            if (!string.IsNullOrWhiteSpace(address))
            {
                SelectedChannelAddress = address;
                ChannelName = address;
            }

            MeasurementPointCount = 5;
            Slope = 1.0;
            Intercept = 0.0;

            ClearMeasurementInputs();
            UpdateCalibrationPlot();
        }

        /// <summary>
        /// 双击校准记录时填充数据
        /// </summary>
        private void OnCalibrationRecordDoubleClick(ChannelCalibrationRecord record)
        {
            if (record == null) return;

            // 填充通道地址和名称
            SelectedChannelAddress = record.ChannelAddress;
            ChannelName = record.ChannelName;

            // 填充测量点数（至少5个）
            int pointCount = record.MeasurementPointCount > 0 ? record.MeasurementPointCount : 5;
            MeasurementPointCount = Math.Max(pointCount, 5);

            // 如果已校准，填充斜率、截距和测量值
            if (record.IsCalibrated)
            {
                Slope = record.Slope;
                Intercept = record.Intercept;

                // 填充仪器设定值
                if (record.InstrumentSetValues != null)
                {
                    for (int i = 0; i < record.InstrumentSetValues.Count && i < InstrumentSetValues.Count; i++)
                    {
                        InstrumentSetValues[i].Value = record.InstrumentSetValues[i];
                    }
                }

                // 填充板卡测量值
                if (record.CardMeasuredValues != null)
                {
                    for (int i = 0; i < record.CardMeasuredValues.Count && i < CardMeasuredValues.Count; i++)
                    {
                        CardMeasuredValues[i].Value = record.CardMeasuredValues[i];
                    }
                }
            }
            else
            {
                // 未校准，重置斜率和截距
                Slope = 1.0;
                Intercept = 0.0;

                // 清空输入值
                ClearMeasurementInputs();
            }

            UpdateCalibrationPlot();
        }

        /// <summary>
        /// 根据测量点数动态更新输入框
        /// </summary>
        private void UpdateMeasurementPointInputs()
        {
            System.Diagnostics.Debug.WriteLine($"[DataCalibration] UpdateMeasurementPointInputs called. MeasurementPointCount={MeasurementPointCount}, InstrumentSetValues.Count={InstrumentSetValues?.Count ?? -1}, CardMeasuredValues.Count={CardMeasuredValues?.Count ?? -1}");

            // 更新仪器设定值列表
            while (InstrumentSetValues.Count < MeasurementPointCount)
            {
                InstrumentSetValues.Add(new MeasurementPoint { Index = InstrumentSetValues.Count + 1 });
                System.Diagnostics.Debug.WriteLine($"[DataCalibration] Added InstrumentSetValues item, new count: {InstrumentSetValues.Count}");
            }
            while (InstrumentSetValues.Count > MeasurementPointCount)
            {
                InstrumentSetValues.RemoveAt(InstrumentSetValues.Count - 1);
                System.Diagnostics.Debug.WriteLine($"[DataCalibration] Removed InstrumentSetValues item, new count: {InstrumentSetValues.Count}");
            }

            // 更新板卡测量值列表
            while (CardMeasuredValues.Count < MeasurementPointCount)
            {
                CardMeasuredValues.Add(new MeasurementPoint { Index = CardMeasuredValues.Count + 1 });
                System.Diagnostics.Debug.WriteLine($"[DataCalibration] Added CardMeasuredValues item, new count: {CardMeasuredValues.Count}");
            }
            while (CardMeasuredValues.Count > MeasurementPointCount)
            {
                CardMeasuredValues.RemoveAt(CardMeasuredValues.Count - 1);
                System.Diagnostics.Debug.WriteLine($"[DataCalibration] Removed CardMeasuredValues item, new count: {CardMeasuredValues.Count}");
            }

            // 更新索引
            for (int i = 0; i < InstrumentSetValues.Count; i++)
            {
                InstrumentSetValues[i].Index = i + 1;
            }
            for (int i = 0; i < CardMeasuredValues.Count; i++)
            {
                CardMeasuredValues[i].Index = i + 1;
            }

            AttachMeasurementPointHandlers(InstrumentSetValues);
            AttachMeasurementPointHandlers(CardMeasuredValues);

            System.Diagnostics.Debug.WriteLine($"[DataCalibration] UpdateMeasurementPointInputs completed. Final counts: InstrumentSetValues={InstrumentSetValues.Count}, CardMeasuredValues={CardMeasuredValues.Count}");
        }

        private void AttachMeasurementPointHandlers(ObservableCollection<MeasurementPoint> collection)
        {
            if (collection == null) return;

            foreach (var point in collection)
            {
                point.PropertyChanged -= OnMeasurementPointChanged;
                point.PropertyChanged += OnMeasurementPointChanged;
            }
        }

        private void OnMeasurementPointChanged(object sender, PropertyChangedEventArgs e)
        {
            // 测量点值改变时不需要保存状态
            if (e.PropertyName == nameof(MeasurementPoint.Value))
            {
                InvalidateCalibrationResult();
            }
        }

        private void InvalidateCalibrationResult()
        {
            _hasValidCalibrationResult = false;
            ((DelegateCommand)SaveToFileCommand).RaiseCanExecuteChanged();
        }

        /// <summary>
        /// 使用最小二乘法计算斜率和截距
        /// </summary>
        private void CalculateLinearRegression()
        {
            if (InstrumentSetValues == null || CardMeasuredValues == null ||
                InstrumentSetValues.Count != CardMeasuredValues.Count ||
                InstrumentSetValues.Count < 2)
            {
                Slope = 1.0;
                Intercept = 0.0;
                return;
            }

            // 提取有效数据点（两个值都不为空）
            var validPoints = new List<(double x, double y)>();
            for (int i = 0; i < InstrumentSetValues.Count; i++)
            {
                if (InstrumentSetValues[i].Value.HasValue && CardMeasuredValues[i].Value.HasValue)
                {
                    // 直接回归补偿系数：y = kx + b
                    // x: 板卡读数/实际值（raw/measured）
                    // y: 目标真值/设定目标（true/target）
                    validPoints.Add((CardMeasuredValues[i].Value.Value, InstrumentSetValues[i].Value.Value));
                }
            }

            if (validPoints.Count < 2)
            {
                Slope = 1.0;
                Intercept = 0.0;
                return;
            }

            // 最小二乘法线性回归：y = kx + b
            // k = (nΣxy - ΣxΣy) / (nΣx² - (Σx)²)
            // b = (Σy - kΣx) / n
            int n = validPoints.Count;
            double sumX = validPoints.Sum(p => p.x);
            double sumY = validPoints.Sum(p => p.y);
            double sumXY = validPoints.Sum(p => p.x * p.y);
            double sumX2 = validPoints.Sum(p => p.x * p.x);

            double denominator = n * sumX2 - sumX * sumX;
            if (Math.Abs(denominator) < 1e-10)
            {
                // 分母为0，无法计算
                Slope = 1.0;
                Intercept = 0.0;
                return;
            }

            double k = (n * sumXY - sumX * sumY) / denominator;
            double b = (sumY - k * sumX) / n;

            if (double.IsNaN(k) || double.IsInfinity(k) || double.IsNaN(b) || double.IsInfinity(b))
            {
                Slope = 1.0;
                Intercept = 0.0;
                return;
            }

            Slope = k;
            Intercept = b;

            UpdateCalibrationPlot();
        }

        /// <summary>
        /// 更新绘图点（原点在左下角，0 -> Max，5 段刻度）
        /// </summary>
        private void UpdateCalibrationPlot()
        {
            CalibrationPoints.Clear();
            CalibrationLine.Clear();
            XAxisTicks.Clear();
            YAxisTicks.Clear();

            var pairs = new List<(double x, double y)>();
            for (int i = 0; i < InstrumentSetValues.Count && i < CardMeasuredValues.Count; i++)
            {
                if (InstrumentSetValues[i].Value.HasValue && CardMeasuredValues[i].Value.HasValue)
                {
                    // 与回归一致：x=实际/读数，y=目标真值
                    pairs.Add((CardMeasuredValues[i].Value.Value, InstrumentSetValues[i].Value.Value));
                }
            }

            if (pairs.Count == 0)
            {
                CalibrationLinePoints = new PointCollection();
                return;
            }

            double maxXData = pairs.Max(p => p.x);
            double maxYData = pairs.Max(p => p.y);

            // X轴最大刻度：设定值最大值向上取整到1位小数
            double maxXTick = Math.Max(maxXData, 0);
            maxXTick = Math.Ceiling(maxXTick * 10.0) / 10.0;
            if (maxXTick <= 0) maxXTick = 1.0;

            // Y轴最大刻度：测量值最大值和拟合线最大值取大，向上取整到1位小数
            double maxYCandidate = Math.Max(maxYData, Slope * maxXData + Intercept);
            double maxYTick = Math.Max(maxYCandidate, 0);
            maxYTick = Math.Ceiling(maxYTick * 10.0) / 10.0;
            if (maxYTick <= 0) maxYTick = 1.0;

            // 调试输出
            System.Diagnostics.Debug.WriteLine($"[DataCalibration] maxXData={maxXData:F3}, maxXTick={maxXTick:F3}");
            System.Diagnostics.Debug.WriteLine($"[DataCalibration] maxYData={maxYData:F3}, maxYCandidate={maxYCandidate:F3}, maxYTick={maxYTick:F3}");
            System.Diagnostics.Debug.WriteLine($"[DataCalibration] Slope={Slope:F6}, Intercept={Intercept:F6}");

            double minX = 0.0;
            double maxX = maxXTick;
            double minY = 0.0;
            double maxY = maxYTick;

            // 坐标轴位置（与 XAML 匹配，原点左下）
            const double axisOriginX = 0;
            const double axisOriginY = 190;
            const double axisEndX = 510;
            const double axisEndY = 20;

            double plotWidth = axisEndX - axisOriginX;
            double plotHeight = axisOriginY - axisEndY;

            double originX = axisOriginX;
            double originY = axisOriginY;

            double rangeX = Math.Max(maxX - minX, 1e-6);
            double rangeY = Math.Max(maxY - minY, 1e-6);

            double Clamp(double value, double min, double max) => Math.Max(min, Math.Min(max, value));
            double ScaleX(double x) => originX + (Clamp(x, minX, maxX) - minX) / rangeX * plotWidth;
            double ScaleY(double y) => originY - (Clamp(y, minY, maxY) - minY) / rangeY * plotHeight; // Y轴向上

            // 刻度：0 -> Max，5 段（含原点共 6 个刻度），显示 1 位小数
            for (int i = 0; i <= 5; i++)
            {
                // X轴刻度：基于X轴最大值
                double xTickVal = maxX / 5.0 * i;
                double xPosition = originX + (plotWidth / 5.0) * i;
                XAxisTicks.Add(new AxisTick { Position = xPosition, Label = $"{xTickVal:0.0}" });

                // Y轴刻度：基于Y轴最大值
                double yTickVal = maxY / 5.0 * i;
                double yPosition = originY - (plotHeight / 5.0) * i;
                YAxisTicks.Add(new AxisTick { Position = yPosition, Label = $"{yTickVal:0.0}" });
            }

            foreach (var p in pairs)
            {
                CalibrationPoints.Add(new System.Windows.Point(ScaleX(p.x)-2, ScaleY(p.y)-4));
            }

            // 拟合线：从原点到最大刻度
            double lineY1 = Clamp(Slope * minX + Intercept, minY, maxY);
            double lineY2 = Clamp(Slope * maxX + Intercept, minY, maxY);

            CalibrationLine.Add(new System.Windows.Point(ScaleX(minX), ScaleY(lineY1)));
            CalibrationLine.Add(new System.Windows.Point(ScaleX(maxX), ScaleY(lineY2)));

            CalibrationLinePoints = new PointCollection(CalibrationLine);
        }

        /// <summary>
        /// 更新当前选中通道的标定记录
        /// </summary>
        private void UpsertCalibrationRecordForCurrentChannel()
        {
            if (string.IsNullOrEmpty(SelectedChannelAddress))
                return;

            System.Diagnostics.Debug.WriteLine($"[DataCalibration] UpsertCalibrationRecordForCurrentChannel called for {SelectedChannelAddress}");

            // 收集仪器设定值和板卡测量值（保存所有有效的值对）
            var instrumentValues = new List<double>();
            var cardValues = new List<double>();

            int maxCount = Math.Min(InstrumentSetValues.Count, CardMeasuredValues.Count);
            System.Diagnostics.Debug.WriteLine($"[DataCalibration] Collecting data from {maxCount} measurement points");

            for (int i = 0; i < maxCount; i++)
            {
                // 保存所有有值的组合，只要至少有一个值就保存
                double? instrumentValue = InstrumentSetValues[i]?.Value;
                double? cardValue = CardMeasuredValues[i]?.Value;

                System.Diagnostics.Debug.WriteLine($"[DataCalibration] Point {i}: Instrument={instrumentValue}, Card={cardValue}");

                if (instrumentValue.HasValue || cardValue.HasValue)
                {
                    // 如果只有一侧有值，用0填充另一侧
                    instrumentValues.Add(instrumentValue ?? 0.0);
                    cardValues.Add(cardValue ?? 0.0);
                    System.Diagnostics.Debug.WriteLine($"[DataCalibration] Saved point {i}: Instrument={instrumentValues.Last()}, Card={cardValues.Last()}");
                }
            }

            var record = new ChannelCalibrationRecord
            {
                ChannelAddress = SelectedChannelAddress,
                ChannelName = ChannelName, // 使用用户可能修改过的通道名称
                Slope = Slope,
                Intercept = Intercept,
                LastCalibrationTime = DateTime.Now,
                IsCalibrated = true,
                MeasurementPointCount = MeasurementPointCount,
                InstrumentSetValues = instrumentValues,
                CardMeasuredValues = cardValues
            };

            // 更新界面列表
            UpsertCalibrationRecord(record);
        }

        /// <summary>
        /// 保存校准记录到内存与文件
        /// </summary>
        private void SaveCalibrationRecord(bool persistToFile = true)
        {
            // 更新界面列表中的当前记录
            UpsertCalibrationRecordForCurrentChannel();
            UpdateCalibrationPlot();

            // 更新全局标定服务（物理层标定）
            MeasureControl.Services.CalibrationService.Instance.UpdateCalibrationData(_projectCalibrationRecords);

            // 持久化到文件（可选，导出/保存项目时调用）
            if (persistToFile && !string.IsNullOrEmpty(CalibrationFilePath))
            {
                _calibrationFileService.SaveCalibrationRecords(CalibrationFilePath, CalibrationRecords?.ToList() ?? new List<ChannelCalibrationRecord>());
            }
        }

        /// <summary>
        /// 计算校准参数
        /// </summary>
        private void OnCalculateCalibration()
        {
            // 验证数据（必须至少2组完整数据，且不能存在“只填一侧”的半残数据）
            if (!ValidateCalibrationData(out var validationMessage))
            {
                ReMessageBox.Show(validationMessage, "提示",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            // 只计算斜率、截距和绘制校准曲线，不保存数据
            CalculateLinearRegression();
            _hasValidCalibrationResult = true;
            ((DelegateCommand)SaveToFileCommand).RaiseCanExecuteChanged();
            UpdateCalibrationPlot();
        }

        /// <summary>
        /// 将记录写回列表（存在则更新，不存在则添加）
        /// </summary>
        public void UpsertCalibrationRecord(ChannelCalibrationRecord record)
        {
            if (CalibrationRecords == null)
            {
                CalibrationRecords = new ObservableCollection<ChannelCalibrationRecord>();
            }

            var existing = CalibrationRecords.FirstOrDefault(r =>
                string.Equals(r.ChannelAddress, record.ChannelAddress, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                existing.ChannelName = record.ChannelName; // 更新用户修改的通道名称
                existing.Slope = record.Slope;
                existing.Intercept = record.Intercept;
                existing.LastCalibrationTime = record.LastCalibrationTime;
                existing.IsCalibrated = record.IsCalibrated;
                existing.MeasurementPointCount = record.MeasurementPointCount;
                existing.InstrumentSetValues = record.InstrumentSetValues;
                existing.CardMeasuredValues = record.CardMeasuredValues;
            }
            else
            {
                CalibrationRecords.Add(record);
            }

            var scopedKey = GetScopedKey(record.ChannelAddress);
            if (!string.IsNullOrWhiteSpace(scopedKey))
            {
                _projectCalibrationRecords[scopedKey] = record;
            }
        }

        /// <summary>
        /// 验证校准数据是否有效
        /// </summary>
        private bool ValidateCalibrationData(out string message)
        {
            message = "";
            if (InstrumentSetValues == null || CardMeasuredValues == null)
            {
                message = "请输入至少2组有效的仪器设定值和板卡测量值";
                return false;
            }

            if (InstrumentSetValues.Count == 0 || CardMeasuredValues.Count == 0)
            {
                message = "请输入至少2组有效的仪器设定值和板卡测量值";
                return false;
            }

            // 至少需要2个完整数据点（两侧都填）
            int validCount = 0;
            bool hasIncompletePair = false;
            for (int i = 0; i < InstrumentSetValues.Count && i < CardMeasuredValues.Count; i++)
            {
                var leftHas = InstrumentSetValues[i].Value.HasValue;
                var rightHas = CardMeasuredValues[i].Value.HasValue;
                if (leftHas && rightHas)
                {
                    validCount++;
                }
                else if (leftHas || rightHas)
                {
                    // 只填了一侧，视为无效输入（要求用户补齐或清空）
                    hasIncompletePair = true;
                }
            }

            if (hasIncompletePair)
            {
                message = "存在未成对的输入";
                return false;
            }

            if (validCount < 2)
            {
                message = "请至少输入2组有效的仪器设定值和板卡测量值";
                return false;
            }

            return true;
        }

        /// <summary>
        /// 验证是否有数据可以导出
        /// </summary>
        private bool ValidateCalibrationDataForExport()
        {
            // 检查是否有任何校准记录
            if (CalibrationRecords == null || CalibrationRecords.Count == 0)
                return false;

            // 检查是否有至少一条已校准的记录
            return CalibrationRecords.Any(r => r.IsCalibrated);
        }

        /// <summary>
        /// 是否可以计算校准参数（始终返回true，在点击时验证）
        /// </summary>
        private bool CanCalculateCalibration()
        {
            // 始终允许点击，在OnCalculateCalibration中验证数据
            return true;
        }

        /// <summary>
        /// 保存到文件
        /// </summary>
        private void OnSaveToFile()
        {
            System.Diagnostics.Debug.WriteLine($"[DataCalibration] OnSaveToFile called: Slope={Slope}, Intercept={Intercept}, Channel={SelectedChannelAddress}, Name={ChannelName}, Points={MeasurementPointCount}");

            if (!_hasValidCalibrationResult)
            {
                ReMessageBox.Show("请先计算校准参数", "提示",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            if (!ValidateCalibrationData(out var validationMessage))
            {
                ReMessageBox.Show(validationMessage, "提示",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            // 更新内存记录和全局标定服务
            SaveCalibrationRecord(false);
            System.Diagnostics.Debug.WriteLine("[DataCalibration] SaveCalibrationRecord completed");

            CommitCurrentDeviceRecordsToProjectMemory();

            // 发布项目修改事件，标记项目为已修改状态
            _eventAggregator?.GetEvent<ProjectModifiedEvent>()?.Publish(new ProjectModifiedEventArgs
            {
                ModificationType = "Calibration",
                Description = $"更新了通道 {SelectedChannelAddress} 的标定数据"
            });

            System.Diagnostics.Debug.WriteLine("[DataCalibration] OnSaveToFile completed");
        }

        /// <summary>
        /// 是否可以保存到文件
        /// </summary>
        private bool CanSaveToFile()
        {
            return _hasValidCalibrationResult && !string.IsNullOrEmpty(ChannelName);
        }

        /// <summary>
        /// 浏览选择标定文件
        /// </summary>
        private void OnBrowseCalibrationFile()
        {
            var dialog = new OpenFileDialog
            {
                Filter = "JSON文件 (*.json)|*.json|所有文件 (*.*)|*.*",
                Title = "选择标定文件",
                CheckFileExists = false
            };

            if (dialog.ShowDialog() == true)
            {
                CalibrationFilePath = dialog.FileName;
            }
        }

        /// <summary>
        /// 从指定文件导入标定数据
        /// </summary>
        private void OnImportCalibration()
        {
            string importFilePath = CalibrationFilePath;

            // 如果TextBox为空，弹出文件选择对话框
            if (string.IsNullOrEmpty(importFilePath))
            {
                var openDialog = new OpenFileDialog
                {
                    Filter = "JSON文件 (*.json)|*.json|所有文件 (*.*)|*.*",
                    Title = "选择要导入的校准文件"
                };

                if (openDialog.ShowDialog() != true)
                {
                    return; // 用户取消了选择
                }

                importFilePath = openDialog.FileName;
                CalibrationFilePath = importFilePath; // 更新TextBox显示
            }

            // 校验文件是否存在
            if (!File.Exists(importFilePath))
            {
                ReMessageBox.Show("选中的标定文件不存在", "错误",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                return;
            }

            try
            {
                // 记录导入前的记录数量
                int recordsBeforeImport = CalibrationRecords?.Count ?? 0;

                // 临时设置文件路径用于加载
                var originalPath = CalibrationFilePath;
                CalibrationFilePath = importFilePath;

                var records = _calibrationFileService.LoadCalibrationRecords(importFilePath);
                if (records != null && records.Count > 0)
                {
                    // 更新界面记录
                    CalibrationRecords.Clear();
                    foreach (var record in records)
                    {
                        EnsureRecordDefaults(record);
                        CalibrationRecords.Add(record);
                    }

                    ReMessageBox.Show($"标定数据已成功导入，共{records.Count}条记录", "成功",
                        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                }
                else
                {
                    ReMessageBox.Show("标定数据导入失败，文件可能格式不正确", "错误",
                        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                ReMessageBox.Show($"导入标定数据失败：{ex.Message}", "错误",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 导出当前标定记录到文件
        /// </summary>
        private void OnExportCalibration()
        {
            string exportFilePath = CalibrationFilePath;

            // 如果TextBox为空，弹出文件保存对话框
            if (string.IsNullOrEmpty(exportFilePath))
            {
                var defaultFileName = string.IsNullOrEmpty(CalibrationPathHelper.DefaultFile)
                    ? "校准数据.json"
                    : Path.GetFileName(CalibrationPathHelper.DefaultFile);

                var saveDialog = new SaveFileDialog
                {
                    Filter = "JSON文件 (*.json)|*.json|所有文件 (*.*)|*.*",
                    Title = "保存校准文件",
                    FileName = defaultFileName,
                    InitialDirectory = !string.IsNullOrEmpty(CalibrationFolderPath) && Directory.Exists(CalibrationFolderPath)
                        ? CalibrationFolderPath
                        : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                };

                if (saveDialog.ShowDialog() != true)
                {
                    return; // 用户取消了保存
                }

                exportFilePath = saveDialog.FileName;
                CalibrationFilePath = exportFilePath; // 更新TextBox显示
            }

            try
            {
                // 校验是否有数据可以导出
                if (!ValidateCalibrationDataForExport())
                {
                    ReMessageBox.Show("没有有效的标定数据可以导出", "提示",
                        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    return;
                }

                // 确保目标目录存在
                var targetDir = Path.GetDirectoryName(exportFilePath);
                if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }

                // 确保保存当前通道的最新结果
                SaveCalibrationRecord();

                // 导出数据到文件
                var recordsToExport = CalibrationRecords?.ToList() ?? new List<ChannelCalibrationRecord>();
                var success = _calibrationFileService.SaveCalibrationRecords(exportFilePath, recordsToExport);

                if (success)
                {
                    ReMessageBox.Show($"标定数据已成功导出，共{recordsToExport.Count}条记录", "成功",
                        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                }
                else
                {
                    ReMessageBox.Show("标定数据导出失败", "错误",
                        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                ReMessageBox.Show($"导出标定数据失败：{ex.Message}", "错误",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 处理标定数据请求事件
        /// </summary>
        private void OnCalibrationRecordsRequested(CalibrationRecordsRequestEventArgs args)
        {
            System.Diagnostics.Debug.WriteLine("[DataCalibration] OnCalibrationRecordsRequested called");

            if (args == null)
            {
                System.Diagnostics.Debug.WriteLine("[DataCalibration] OnCalibrationRecordsRequested: args is null");
                return;
            }

            args.CalibrationRecords = new Dictionary<string, ChannelCalibrationRecord>(_projectCalibrationRecords, StringComparer.OrdinalIgnoreCase);

            if (CalibrationRecords != null)
            {
                System.Diagnostics.Debug.WriteLine($"[DataCalibration] Collecting {CalibrationRecords.Count} calibration records for saving");
                foreach (var record in CalibrationRecords)
                {
                    if (record == null || string.IsNullOrEmpty(record.ChannelAddress))
                        continue;

                    var scopedKey = GetScopedKey(record.ChannelAddress);
                    if (string.IsNullOrWhiteSpace(scopedKey))
                        continue;

                    System.Diagnostics.Debug.WriteLine($"[DataCalibration] Collecting record: {scopedKey}, Slope={record.Slope}, Intercept={record.Intercept}, IsCalibrated={record.IsCalibrated}");
                    args.CalibrationRecords[scopedKey] = record;
                }
            }
        }

        /// <summary>
        /// 处理标定数据加载事件
        /// </summary>
        private void OnCalibrationRecordsLoaded(CalibrationRecordsLoadEventArgs args)
        {
            System.Diagnostics.Debug.WriteLine($"[DataCalibration] OnCalibrationRecordsLoaded called with {args?.CalibrationRecords?.Count ?? 0} records");

            if (args == null || args.CalibrationRecords == null)
            {
                System.Diagnostics.Debug.WriteLine("[DataCalibration] OnCalibrationRecordsLoaded: args is null or CalibrationRecords is null");
                return;
            }

            if (args.CalibrationRecords.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine("[DataCalibration] No calibration records to load from project");
                return;
            }

            _projectCalibrationRecords = new Dictionary<string, ChannelCalibrationRecord>(args.CalibrationRecords, StringComparer.OrdinalIgnoreCase);

            // 如果当前还没绑定具体设备，则仅做缓存；等 ApplyAnalogInputContext(deviceId, ...) 再刷新列表
            if (!string.IsNullOrWhiteSpace(_currentDeviceId))
            {
                RefreshRecordsForCurrentDevice();
            }
        }

        /// <summary>
        /// 处理项目打开事件，标定数据通过CalibrationRecordsLoadEvent加载，这里只做初始化
        /// </summary>
        private void OnProjectOpened(MeasureControl.Models.ProjectItem project)
        {
            System.Diagnostics.Debug.WriteLine($"[DataCalibration] OnProjectOpened called. Current CalibrationRecords count: {(CalibrationRecords?.Count ?? 0)}");

            // 确保CalibrationRecords已初始化
            if (CalibrationRecords == null)
            {
                CalibrationRecords = new ObservableCollection<ChannelCalibrationRecord>();
            }

            // 项目打开时仅缓存项目上下文，通道/记录初始化延迟到 Apply*Context
        }

        /// <summary>
        /// 处理项目关闭事件，清理标定数据
        /// </summary>
        private void OnProjectClosed()
        {
            System.Diagnostics.Debug.WriteLine("[DataCalibration] OnProjectClosed called, resetting calibration data");

            // 清理标定数据
            ResetViewModelState();

            // 清理全局标定服务
            Services.CalibrationService.Instance.ClearCalibrationData();
        }

        /// <summary>
        /// 重置ViewModel状态，清空所有标定数据
        /// </summary>
        private void ResetViewModelState()
        {
            System.Diagnostics.Debug.WriteLine("[DataCalibration] ResetViewModelState called");

            // 清空所有集合
            CalibrationRecords?.Clear();
            InstrumentSetValues?.Clear();
            CardMeasuredValues?.Clear();
            CalibrationPoints?.Clear();
            CalibrationLine?.Clear();
            XAxisTicks?.Clear();
            YAxisTicks?.Clear();

            // 重置所有属性为默认值
            ChannelName = string.Empty;
            MeasurementPointCount = 5;
            Slope = 1.0;
            Intercept = 0.0;
            CalibrationFilePath = string.Empty;
            SelectedChannelAddress = string.Empty;

            // 重新初始化固定通道地址
            SetChannelPrefix("AI");
            InitializeFixedAnalogChannels();

            // 重新初始化默认标定记录（AI0-AI15的空记录）
            InitializeCalibrationRecords(true);

            _currentDeviceId = string.Empty;
            _projectCalibrationRecords.Clear();

            System.Diagnostics.Debug.WriteLine("[DataCalibration] ResetViewModelState completed");
        }
    }

    /// <summary>
    /// 测量点数据模型
    /// </summary>
    public class AxisTick : BindableBase
    {
        private double _position;
        private string _label;

        public double Position
        {
            get => _position;
            set => SetProperty(ref _position, value);
        }

        public string Label
        {
            get => _label;
            set => SetProperty(ref _label, value);
        }
    }

    public class MeasurementPoint : BindableBase
    {
        private int _index;
        private double? _value;

        public int Index
        {
            get => _index;
            set => SetProperty(ref _index, value);
        }

        public double? Value
        {
            get => _value;
            set => SetProperty(ref _value, value);
        }
    }
}
