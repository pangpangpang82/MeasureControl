using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;
using MeasureControl.Constants;
using MeasureControl.Events;
using MeasureControl.Helpers;
using MeasureControl.Models;
using MeasureControl.Models.Devices;
using MeasureControl.Services;
using MeasureControl.Views.Dialogs;
using Prism.Commands;
using Prism.Events;
using Prism.Mvvm;
using Prism.Regions;
using System.Threading.Tasks;

namespace MeasureControl.ViewModels
{
    /// <summary>
    /// 矩阵开关配置表的ViewModel
    /// </summary>
    public class MatrixSwitchConfigTableViewModel : BindableBase, INavigationAware, IDisposable
    {
        private readonly IRegionManager _regionManager;
        private readonly IEventAggregator _eventAggregator;
        private readonly ProjectService _projectService;
        private readonly IPxiChassisService _pxiChassisService;
        private const int PageSize = 14;
        private int _currentPage = 1;
        private bool _isRestoringRuntimeState;
        public ICommand CloseCommand { get; private set; }

        // 用于存储所有矩阵开关配置表数据的静态字典（key格式：测试任务名/配置表名）
        private static Dictionary<string, ObservableCollection<MatrixSwitchConfigItem>> _allMatrixSwitchTableItems = new Dictionary<string, ObservableCollection<MatrixSwitchConfigItem>>();

        // 用于同步访问静态字典的锁对象
        private static readonly object _allMatrixSwitchTableItemsLock = new object();

        private class MatrixSwitchRuntimeState
        {
            public int CurrentPage { get; set; } = 1;

            public string SelectedResistanceRoute { get; set; }

            public string SelectedDiscreteInputLoopbackRoute { get; set; }
            public string SelectedDiscreteInputLoopbackMeasureOption { get; set; }

            public string SelectedDiscreteOutputLoopbackRoute { get; set; }
            public string SelectedDiscreteOutputLoopbackMeasureOption { get; set; }

            public string SelectedHighSpeedIoLoopbackRoute { get; set; }
            public string SelectedHighSpeedIoLoopbackMeasureOption { get; set; }

            public string SelectedFrequencyAcquireRoute { get; set; }
            public string SelectedFrequencyAcquireMeasureOption { get; set; }

            public string SelectedFrequencyOutput1MeasureOption { get; set; }

            public string SelectedFrequencyOutputRoute { get; set; }

            public string SelectedCurrentLoopbackRoute { get; set; }

            public string SelectedAdAcquireRoute { get; set; }
            public string SelectedAdAcquireMeasureOption { get; set; }

            public string SelectedResolverLoopbackRoute { get; set; }
            public string SelectedResolverLoopbackMeasureOption { get; set; }

            public string SelectedLvdtLoopbackRoute { get; set; }
            public string SelectedLvdtLoopbackMeasureOption { get; set; }
        }

        private static readonly Dictionary<string, MatrixSwitchRuntimeState> _runtimeStateByKey = new Dictionary<string, MatrixSwitchRuntimeState>();
        private static readonly object _runtimeStateByKeyLock = new object();

        private static readonly Dictionary<string, bool> _connectedRouteStateByKey = new Dictionary<string, bool>();
        private static readonly object _connectedRouteStateByKeyLock = new object();

        private static string BuildRouteStateKey(string category, params string[] parts)
        {
            var safeParts = parts?.Where(p => !string.IsNullOrEmpty(p)) ?? Enumerable.Empty<string>();
            return string.Join("|", new[] { category }.Concat(safeParts));
        }

        private static bool GetRouteConnectedState(string routeStateKey)
        {
            if (string.IsNullOrEmpty(routeStateKey))
            {
                return false;
            }

            lock (_connectedRouteStateByKeyLock)
            {
                return _connectedRouteStateByKey.TryGetValue(routeStateKey, out var v) && v;
            }
        }

        private static void SetRouteConnectedState(string routeStateKey, bool isConnected)
        {
            if (string.IsNullOrEmpty(routeStateKey))
            {
                return;
            }

            lock (_connectedRouteStateByKeyLock)
            {
                _connectedRouteStateByKey[routeStateKey] = isConnected;
            }
        }

        private void RefreshConnectedFlagsFromRegistry()
        {
            IsSelectedResistanceConnected = GetRouteConnectedState(BuildRouteStateKey("Resistance", SelectedResistanceRoute));

            IsDiscreteInputLoopbackConnected = GetRouteConnectedState(BuildRouteStateKey("DiscreteInput", SelectedDiscreteInputLoopbackRoute, SelectedDiscreteInputLoopbackMeasureOption));
            IsDiscreteOutputLoopbackConnected = GetRouteConnectedState(BuildRouteStateKey("DiscreteOutput", SelectedDiscreteOutputLoopbackRoute, SelectedDiscreteOutputLoopbackMeasureOption));

            IsHighSpeedIoLoopbackConnected = GetRouteConnectedState(BuildRouteStateKey("HighSpeedIo", SelectedHighSpeedIoLoopbackRoute, SelectedHighSpeedIoLoopbackMeasureOption));

            IsFrequencyAcquireConnected = GetRouteConnectedState(BuildRouteStateKey("FrequencyAcquire", SelectedFrequencyAcquireRoute, SelectedFrequencyAcquireMeasureOption));
            IsFrequencyOutput1Connected = GetRouteConnectedState(BuildRouteStateKey("FrequencyOutput1", SelectedFrequencyOutput1MeasureOption));
            IsFrequencyOutputConnected = GetRouteConnectedState(BuildRouteStateKey("FrequencyOutput", SelectedFrequencyOutputRoute));

            IsCurrentLoopbackConnected = GetRouteConnectedState(BuildRouteStateKey("CurrentLoopback", SelectedCurrentLoopbackRoute));

            IsAdAcquireConnected = GetRouteConnectedState(BuildRouteStateKey("AdAcquire", SelectedAdAcquireRoute, SelectedAdAcquireMeasureOption));

            IsResolverLoopbackConnected = GetRouteConnectedState(BuildRouteStateKey("ResolverLoopback", SelectedResolverLoopbackRoute, SelectedResolverLoopbackMeasureOption));
            IsLvdtLoopbackConnected = GetRouteConnectedState(BuildRouteStateKey("LvdtLoopback", SelectedLvdtLoopbackRoute, SelectedLvdtLoopbackMeasureOption));
        }

        /// <summary>获取所有矩阵开关配置表数据</summary>
        public static Dictionary<string, List<MatrixSwitchConfigItem>> GetAllMatrixSwitchTableItems()
        {
            lock (_allMatrixSwitchTableItemsLock)
            {
                return _allMatrixSwitchTableItems.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value?.Where(s => !s.IsEmpty).Select(s => { var clone = s.Clone(); clone.IsEmpty = false; return clone; }).ToList()
                           ?? new List<MatrixSwitchConfigItem>());
            }
        }

        /// <summary>加载矩阵开关配置表数据到静态字典</summary>
        public static void LoadMatrixSwitchTableItems(Dictionary<string, List<MatrixSwitchConfigItem>> items)
        {
            DebugLog($"开始加载矩阵开关表数据到静态字典，项目数量: {items?.Count}");
            lock (_allMatrixSwitchTableItemsLock)
            {
                _allMatrixSwitchTableItems.Clear();
                if (items == null)
                {
                    DebugLog("传入的项目数据为null");
                    return;
                }

                foreach (var kvp in items)
                {
                    DebugLog($"加载矩阵开关表: {kvp.Key}, 项目数: {kvp.Value?.Count}");
                    _allMatrixSwitchTableItems[kvp.Key] = new ObservableCollection<MatrixSwitchConfigItem>(
                        kvp.Value?.Where(s => s != null).Select(s => s.Clone()) ?? Enumerable.Empty<MatrixSwitchConfigItem>());
                }
                DebugLog($"静态字典加载完成，总项目数: {_allMatrixSwitchTableItems.Count}");
            }
        }

        /// <summary>清空所有矩阵开关配置表数据</summary>
        public static void ClearAllMatrixSwitchTableItems()
        {
            lock (_allMatrixSwitchTableItemsLock)
            {
                DebugLog($"清空矩阵开关表数据，原有项目数: {_allMatrixSwitchTableItems.Count}");
                _allMatrixSwitchTableItems.Clear();
            }
        }

        #region Properties

        private string _chassisName;
        /// <summary>
        /// 机箱名称
        /// </summary>
        public string ChassisName
        {
            get => _chassisName;
            set
            {
                DebugLog($"设置ChassisName: 从 '{_chassisName}' 到 '{value}'");
                SetProperty(ref _chassisName, value);
            }
        }

        /// <summary>
        /// 设备型号与拓扑的映射配置
        /// </summary>
        private static readonly Dictionary<string, List<string>> DeviceTopologyMap = new Dictionary<string, List<string>>
        {
            { "PXI-3022", new List<string> { "4*64Matrix" } },
            { "PXI-2601", new List<string> { "4*32Matrix", "8*16Matrix" } }
        };

        /// <summary>
        /// 根据设备名称获取可用的拓扑类型
        /// </summary>
        public List<string> GetAvailableTopologies(string deviceName)
        {
            DebugLog($"获取可用拓扑类型，设备名称: '{deviceName}'");

            if (string.IsNullOrEmpty(deviceName))
            {
                DebugLog("设备名称为空，返回默认拓扑选项");
                return new List<string> { "4*32Matrix", "8*16Matrix", "4*64Matrix" };
            }

            // 从设备名称中提取型号（如 "欧开 PXI-3022" -> "PXI-3022"）
            string model = ExtractModelFromDeviceName(deviceName);
            DebugLog($"从设备名称 '{deviceName}' 提取到型号: '{model}'");

            if (!string.IsNullOrEmpty(model) && DeviceTopologyMap.ContainsKey(model))
            {
                DebugLog($"找到型号 '{model}' 对应的拓扑: {string.Join(", ", DeviceTopologyMap[model])}");
                return DeviceTopologyMap[model];
            }

            DebugLog($"未找到型号 '{model}' 的拓扑映射，返回默认拓扑选项");
            return new List<string> { "4*32Matrix", "8*16Matrix", "4*64Matrix" };
        }

        /// <summary>
        /// 从设备名称中提取型号
        /// </summary>
        private string ExtractModelFromDeviceName(string deviceName)
        {
            if (string.IsNullOrEmpty(deviceName))
            {
                DebugLog("设备名称为空，无法提取型号");
                return null;
            }

            DebugLog($"从设备名称 '{deviceName}' 提取型号");

            // 如果设备名称格式为 "矩阵开关X 厂商型号"，先去掉前缀
            string nameToProcess = deviceName;
            var prefixMatch = System.Text.RegularExpressions.Regex.Match(deviceName, @"^矩阵开关\d+\s+");
            if (prefixMatch.Success)
            {
                nameToProcess = deviceName.Substring(prefixMatch.Length).Trim();
                DebugLog($"  去掉前缀后: '{nameToProcess}'");
            }

            // 如果去掉前缀后是 "Card" 或其他无意义的值，说明没有型号信息
            if (string.IsNullOrEmpty(nameToProcess) ||
                nameToProcess.Equals("Card", StringComparison.OrdinalIgnoreCase) ||
                nameToProcess.Equals("N/A", StringComparison.OrdinalIgnoreCase))
            {
                DebugLog($"  去掉前缀后无有效型号信息: '{nameToProcess}'");
                return null;
            }

            // 尝试匹配常见的型号格式（如 PXI-3022, PXIe-2722G2 等）
            var match = System.Text.RegularExpressions.Regex.Match(nameToProcess, @"PXI[eE]?-[\w\d]+");
            string result = match.Success ? match.Value : null;
            DebugLog($"  提取型号结果: '{result}'");
            return result;
        }

        private ObservableCollection<string> _availableSwitchDevices;
        /// <summary>
        /// 可用的矩阵开关设备列表（来自已添加的SwitchDevice）
        /// </summary>
        public ObservableCollection<string> AvailableSwitchDevices
        {
            get
            {
                DebugLog($"获取AvailableSwitchDevices，当前数量: {_availableSwitchDevices?.Count ?? 0}");
                return _availableSwitchDevices;
            }
            set
            {
                DebugLog($"设置AvailableSwitchDevices，新数量: {value?.Count ?? 0}");
                SetProperty(ref _availableSwitchDevices, value);
            }
        }

        /// <summary>
        /// 根据拓扑获取可用的矩阵输入选项
        /// </summary>
        public List<string> GetAvailableInputs(string topology)
        {
            DebugLog($"获取可用矩阵输入，拓扑: '{topology}'");
            var inputs = new List<string>();
            if (string.IsNullOrEmpty(topology))
            {
                DebugLog("拓扑为空，返回空列表");
                return inputs;
            }

            switch (topology)
            {
                case "4*32Matrix":
                    // 4行，输入为r0-r3
                    for (int i = 0; i < 4; i++)
                    {
                        inputs.Add($"r{i}");
                    }
                    DebugLog($"拓扑 4*32Matrix: 返回4个输入");
                    break;
                case "8*16Matrix":
                    // 8行，输入为r0-r7
                    for (int i = 0; i < 8; i++)
                    {
                        inputs.Add($"r{i}");
                    }
                    DebugLog($"拓扑 8*16Matrix: 返回8个输入");
                    break;
                case "4*64Matrix":
                    // 4行，输入为r0-r3
                    for (int i = 0; i < 4; i++)
                    {
                        inputs.Add($"r{i}");
                    }
                    DebugLog($"拓扑 4*64Matrix: 返回4个输入");
                    break;
                default:
                    DebugLog($"未知拓扑: '{topology}'");
                    break;
            }
            return inputs;
        }

        /// <summary>
        /// 根据拓扑获取可用的矩阵输出选项
        /// </summary>
        public List<string> GetAvailableOutputs(string topology)
        {
            DebugLog($"获取可用矩阵输出，拓扑: '{topology}'");
            var outputs = new List<string>();
            if (string.IsNullOrEmpty(topology))
            {
                DebugLog("拓扑为空，返回空列表");
                return outputs;
            }

            switch (topology)
            {
                case "4*32Matrix":
                    // 32列，输出为c0-c31
                    for (int i = 0; i < 32; i++)
                    {
                        outputs.Add($"c{i}");
                    }
                    DebugLog($"拓扑 4*32Matrix: 返回32个输出");
                    break;
                case "8*16Matrix":
                    // 16列，输出为c0-c15
                    for (int i = 0; i < 16; i++)
                    {
                        outputs.Add($"c{i}");
                    }
                    DebugLog($"拓扑 8*16Matrix: 返回16个输出");
                    break;
                case "4*64Matrix":
                    // 64列，输出为c0-c63
                    for (int i = 0; i < 64; i++)
                    {
                        outputs.Add($"c{i}");
                    }
                    DebugLog($"拓扑 4*64Matrix: 返回64个输出");
                    break;
                default:
                    DebugLog($"未知拓扑: '{topology}'");
                    break;
            }
            return outputs;
        }

        private string _testTaskName;
        /// <summary>
        /// 测试任务名称
        /// </summary>
        public string TestTaskName
        {
            get => _testTaskName;
            set
            {
                DebugLog($"设置TestTaskName: 从 '{_testTaskName}' 到 '{value}'");
                SetProperty(ref _testTaskName, value);
            }
        }

        private string _configTableName;
        /// <summary>
        /// 配置表名称
        /// </summary>
        public string ConfigTableName
        {
            get => _configTableName;
            set
            {
                DebugLog($"设置ConfigTableName: 从 '{_configTableName}' 到 '{value}'");
                SetProperty(ref _configTableName, value);
            }
        }

        private string _parentType;
        private bool _disposed = false;
        /// <summary>
        /// 父节点类型
        /// </summary>
        public string ParentType
        {
            get => _parentType;
            set
            {
                DebugLog($"设置ParentType: 从 '{_parentType}' 到 '{value}'");
                SetProperty(ref _parentType, value);
            }
        }

        private string _displayPath;
        /// <summary>
        /// 显示路径（用于界面标题）
        /// </summary>
        public string DisplayPath
        {
            get => _displayPath;
            set
            {
                DebugLog($"设置DisplayPath: 从 '{_displayPath}' 到 '{value}'");
                SetProperty(ref _displayPath, value);
            }
        }

        private ObservableCollection<MatrixSwitchConfigItem> _matrixSwitches;
        /// <summary>
        /// 矩阵开关配置列表
        /// </summary>
        public ObservableCollection<MatrixSwitchConfigItem> MatrixSwitches
        {
            get
            {
                DebugLog($"获取MatrixSwitches，当前数量: {_matrixSwitches?.Count ?? 0}");
                return _matrixSwitches;
            }
            set
            {
                if (_matrixSwitches != null)
                {
                    DebugLog($"取消订阅MatrixSwitches集合变化事件");
                    _matrixSwitches.CollectionChanged -= MatrixSwitches_CollectionChanged;
                }

                DebugLog($"设置MatrixSwitches，新数量: {value?.Count ?? 0}");
                SetProperty(ref _matrixSwitches, value);

                if (_matrixSwitches != null)
                {
                    DebugLog($"订阅MatrixSwitches集合变化事件");
                    _matrixSwitches.CollectionChanged += MatrixSwitches_CollectionChanged;
                }
            }
        }

        private string _paginationInfo;
        /// <summary>
        /// 分页信息
        /// </summary>
        public string PaginationInfo
        {
            get => _paginationInfo;
            set => SetProperty(ref _paginationInfo, value);
        }

        private ObservableCollection<MatrixSwitchConfigItem> _pagedMatrixSwitches;
        /// <summary>
        /// 当前页显示的矩阵开关列表
        /// </summary>
        public ObservableCollection<MatrixSwitchConfigItem> PagedMatrixSwitches
        {
            get => _pagedMatrixSwitches;
            set => SetProperty(ref _pagedMatrixSwitches, value);
        }

        private ObservableCollection<PaginationButtonInfo> _pageNumbers;
        /// <summary>分页按钮列表</summary>
        public ObservableCollection<PaginationButtonInfo> PageNumbers
        {
            get => _pageNumbers;
            set => SetProperty(ref _pageNumbers, value);
        }

        /// <summary>当前页码（从1开始）</summary>
        public int CurrentPage
        {
            get => _currentPage;
            set
            {
                DebugLog($"设置CurrentPage: 从 {_currentPage} 到 {value}");
                if (SetProperty(ref _currentPage, value))
                {
                    UpdatePagination();
                }
            }
        }

        /// <summary>总页数</summary>
        public int TotalPages
        {
            get
            {
                int totalPages = 1;
                if (MatrixSwitches == null || MatrixSwitches.Count == 0)
                {
                    DebugLog($"计算TotalPages: MatrixSwitches为空或数量为0，返回1");
                    totalPages = 1;
                }
                else
                {
                    totalPages = (int)Math.Ceiling((double)MatrixSwitches.Count / PageSize);
                    DebugLog($"计算TotalPages: MatrixSwitches.Count={MatrixSwitches.Count}, PageSize={PageSize}, TotalPages={totalPages}");
                }
                return totalPages;
            }
        }

        #endregion

        #region Commands

        public ICommand AddMatrixSwitchCommand { get; }
        public ICommand DeleteMatrixSwitchCommand { get; }
        public ICommand EditMatrixSwitchCommand { get; }
        public ICommand NavigateBackCommand { get; }
        public DelegateCommand PreviousPageCommand { get; }
        public DelegateCommand NextPageCommand { get; }
        // 电阻采集命令（原有的单一路由保留为兼容）
        public DelegateCommand ConnectResistanceAcquire1Command { get; }

        // 新增：可选的电阻采集路由列表与选择
        public ObservableCollection<string> ResistanceRoutes { get; } = new ObservableCollection<string>();
        private string _selectedResistanceRoute;
        private bool _isSelectedResistanceConnected;
        private bool _isTogglingSelectedResistance;
        public string SelectedResistanceRoute
        {
            get => _selectedResistanceRoute;
            set
            {
                DebugLog($"设置 SelectedResistanceRoute: 从 '{_selectedResistanceRoute}' 到 '{value}'");
                if (SetProperty(ref _selectedResistanceRoute, value))
                {
                    if (!_isRestoringRuntimeState)
                    {
                        IsSelectedResistanceConnected = GetRouteConnectedState(BuildRouteStateKey("Resistance", SelectedResistanceRoute));
                    }
                    // 更新命令可执行状态
                    ConnectSelectedResistanceCommand?.RaiseCanExecuteChanged();
                    DisconnectSelectedResistanceCommand?.RaiseCanExecuteChanged();
                    ToggleSelectedResistanceCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        public bool IsSelectedResistanceConnected
        {
            get => _isSelectedResistanceConnected;
            set => SetProperty(ref _isSelectedResistanceConnected, value);
        }

        // 新增：通用连接/断开命令
        public DelegateCommand ConnectSelectedResistanceCommand { get; }
        public DelegateCommand DisconnectSelectedResistanceCommand { get; }
        public DelegateCommand<object> ToggleSelectedResistanceCommand { get; }

        // 新增：32通道离散量输入回采测试
        public DelegateCommand<object> ToggleDiscreteInputLoopbackCommand { get; }
        public DelegateCommand<object> ToggleDiscreteOutputLoopbackCommand { get; }

        public ObservableCollection<string> DiscreteInputLoopbackRoutes { get; } = new ObservableCollection<string>();
        private string _selectedDiscreteInputLoopbackRoute;
        public string SelectedDiscreteInputLoopbackRoute
        {
            get => _selectedDiscreteInputLoopbackRoute;
            set
            {
                if (SetProperty(ref _selectedDiscreteInputLoopbackRoute, value))
                {
                    if (!_isRestoringRuntimeState)
                    {
                        IsDiscreteInputLoopbackConnected = GetRouteConnectedState(BuildRouteStateKey("DiscreteInput", SelectedDiscreteInputLoopbackRoute, SelectedDiscreteInputLoopbackMeasureOption));
                    }
                    ToggleDiscreteInputLoopbackCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        public ObservableCollection<string> DiscreteInputLoopbackMeasureOptions { get; } = new ObservableCollection<string>();
        private string _selectedDiscreteInputLoopbackMeasureOption;
        public string SelectedDiscreteInputLoopbackMeasureOption
        {
            get => _selectedDiscreteInputLoopbackMeasureOption;
            set
            {
                if (SetProperty(ref _selectedDiscreteInputLoopbackMeasureOption, value))
                {
                    if (!_isRestoringRuntimeState)
                    {
                        IsDiscreteInputLoopbackConnected = GetRouteConnectedState(BuildRouteStateKey("DiscreteInput", SelectedDiscreteInputLoopbackRoute, SelectedDiscreteInputLoopbackMeasureOption));
                    }
                    ToggleDiscreteInputLoopbackCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        public ObservableCollection<string> DiscreteOutputLoopbackRoutes { get; } = new ObservableCollection<string>();
        private string _selectedDiscreteOutputLoopbackRoute;
        public string SelectedDiscreteOutputLoopbackRoute
        {
            get => _selectedDiscreteOutputLoopbackRoute;
            set
            {
                if (SetProperty(ref _selectedDiscreteOutputLoopbackRoute, value))
                {
                    if (!_isRestoringRuntimeState)
                    {
                        IsDiscreteOutputLoopbackConnected = GetRouteConnectedState(BuildRouteStateKey("DiscreteOutput", SelectedDiscreteOutputLoopbackRoute, SelectedDiscreteOutputLoopbackMeasureOption));
                    }
                    ToggleDiscreteOutputLoopbackCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        public ObservableCollection<string> DiscreteOutputLoopbackMeasureOptions { get; } = new ObservableCollection<string>();
        private string _selectedDiscreteOutputLoopbackMeasureOption;
        public string SelectedDiscreteOutputLoopbackMeasureOption
        {
            get => _selectedDiscreteOutputLoopbackMeasureOption;
            set
            {
                if (SetProperty(ref _selectedDiscreteOutputLoopbackMeasureOption, value))
                {
                    if (!_isRestoringRuntimeState)
                    {
                        IsDiscreteOutputLoopbackConnected = GetRouteConnectedState(BuildRouteStateKey("DiscreteOutput", SelectedDiscreteOutputLoopbackRoute, SelectedDiscreteOutputLoopbackMeasureOption));
                    }
                    ToggleDiscreteOutputLoopbackCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        private bool _isDiscreteInputLoopbackConnected;
        private bool _isTogglingDiscreteInputLoopback;
        public bool IsDiscreteInputLoopbackConnected
        {
            get => _isDiscreteInputLoopbackConnected;
            set => SetProperty(ref _isDiscreteInputLoopbackConnected, value);
        }

        private bool _isDiscreteOutputLoopbackConnected;
        private bool _isTogglingDiscreteOutputLoopback;
        public bool IsDiscreteOutputLoopbackConnected
        {
            get => _isDiscreteOutputLoopbackConnected;
            set => SetProperty(ref _isDiscreteOutputLoopbackConnected, value);
        }

        // 新增：64通道高速IO回采测试
        public DelegateCommand<object> ToggleHighSpeedIoLoopbackCommand { get; }

        public ObservableCollection<string> HighSpeedIoLoopbackRoutes { get; } = new ObservableCollection<string>();
        private string _selectedHighSpeedIoLoopbackRoute;
        public string SelectedHighSpeedIoLoopbackRoute
        {
            get => _selectedHighSpeedIoLoopbackRoute;
            set
            {
                if (SetProperty(ref _selectedHighSpeedIoLoopbackRoute, value))
                {
                    if (!_isRestoringRuntimeState)
                    {
                        IsHighSpeedIoLoopbackConnected = GetRouteConnectedState(BuildRouteStateKey("HighSpeedIo", SelectedHighSpeedIoLoopbackRoute, SelectedHighSpeedIoLoopbackMeasureOption));
                    }
                    ToggleHighSpeedIoLoopbackCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        public ObservableCollection<string> HighSpeedIoLoopbackMeasureOptions { get; } = new ObservableCollection<string>();
        private string _selectedHighSpeedIoLoopbackMeasureOption;
        public string SelectedHighSpeedIoLoopbackMeasureOption
        {
            get => _selectedHighSpeedIoLoopbackMeasureOption;
            set
            {
                if (SetProperty(ref _selectedHighSpeedIoLoopbackMeasureOption, value))
                {
                    if (!_isRestoringRuntimeState)
                    {
                        IsHighSpeedIoLoopbackConnected = GetRouteConnectedState(BuildRouteStateKey("HighSpeedIo", SelectedHighSpeedIoLoopbackRoute, SelectedHighSpeedIoLoopbackMeasureOption));
                    }
                    ToggleHighSpeedIoLoopbackCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        private bool _isHighSpeedIoLoopbackConnected;
        private bool _isTogglingHighSpeedIoLoopback;
        public bool IsHighSpeedIoLoopbackConnected
        {
            get => _isHighSpeedIoLoopbackConnected;
            set => SetProperty(ref _isHighSpeedIoLoopbackConnected, value);
        }

        public DelegateCommand<object> ToggleFrequencyAcquireCommand { get; }
        public ObservableCollection<string> FrequencyAcquireRoutes { get; } = new ObservableCollection<string>();
        private string _selectedFrequencyAcquireRoute;
        public string SelectedFrequencyAcquireRoute
        {
            get => _selectedFrequencyAcquireRoute;
            set
            {
                if (SetProperty(ref _selectedFrequencyAcquireRoute, value))
                {
                    if (!_isRestoringRuntimeState)
                    {
                        IsFrequencyAcquireConnected = GetRouteConnectedState(BuildRouteStateKey("FrequencyAcquire", SelectedFrequencyAcquireRoute, SelectedFrequencyAcquireMeasureOption));
                    }
                    ToggleFrequencyAcquireCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        public ObservableCollection<string> FrequencyAcquireMeasureOptions { get; } = new ObservableCollection<string>();
        private string _selectedFrequencyAcquireMeasureOption;
        public string SelectedFrequencyAcquireMeasureOption
        {
            get => _selectedFrequencyAcquireMeasureOption;
            set
            {
                if (SetProperty(ref _selectedFrequencyAcquireMeasureOption, value))
                {
                    if (!_isRestoringRuntimeState)
                    {
                        IsFrequencyAcquireConnected = GetRouteConnectedState(BuildRouteStateKey("FrequencyAcquire", SelectedFrequencyAcquireRoute, SelectedFrequencyAcquireMeasureOption));
                    }
                    ToggleFrequencyAcquireCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        private bool _isFrequencyAcquireConnected;
        private bool _isTogglingFrequencyAcquire;
        public bool IsFrequencyAcquireConnected
        {
            get => _isFrequencyAcquireConnected;
            set => SetProperty(ref _isFrequencyAcquireConnected, value);
        }

        public DelegateCommand<object> ToggleFrequencyOutput1Command { get; }
        public ObservableCollection<string> FrequencyOutput1MeasureOptions { get; } = new ObservableCollection<string>();
        private string _selectedFrequencyOutput1MeasureOption;
        public string SelectedFrequencyOutput1MeasureOption
        {
            get => _selectedFrequencyOutput1MeasureOption;
            set
            {
                if (SetProperty(ref _selectedFrequencyOutput1MeasureOption, value))
                {
                    if (!_isRestoringRuntimeState)
                    {
                        IsFrequencyOutput1Connected = GetRouteConnectedState(BuildRouteStateKey("FrequencyOutput1", SelectedFrequencyOutput1MeasureOption));
                    }
                    ToggleFrequencyOutput1Command?.RaiseCanExecuteChanged();
                }
            }
        }

        private bool _isFrequencyOutput1Connected;
        private bool _isTogglingFrequencyOutput1;
        public bool IsFrequencyOutput1Connected
        {
            get => _isFrequencyOutput1Connected;
            set => SetProperty(ref _isFrequencyOutput1Connected, value);
        }

        public DelegateCommand<object> ToggleFrequencyOutputCommand { get; }
        public ObservableCollection<string> FrequencyOutputRoutes { get; } = new ObservableCollection<string>();
        private string _selectedFrequencyOutputRoute;
        public string SelectedFrequencyOutputRoute
        {
            get => _selectedFrequencyOutputRoute;
            set
            {
                if (SetProperty(ref _selectedFrequencyOutputRoute, value))
                {
                    if (!_isRestoringRuntimeState)
                    {
                        IsFrequencyOutputConnected = GetRouteConnectedState(BuildRouteStateKey("FrequencyOutput", SelectedFrequencyOutputRoute));
                    }
                    ToggleFrequencyOutputCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        private bool _isFrequencyOutputConnected;
        private bool _isTogglingFrequencyOutput;
        public bool IsFrequencyOutputConnected
        {
            get => _isFrequencyOutputConnected;
            set => SetProperty(ref _isFrequencyOutputConnected, value);
        }

        // 新增：8通道电流回采
        public ObservableCollection<string> CurrentLoopbackRoutes { get; } = new ObservableCollection<string>();
        private string _selectedCurrentLoopbackRoute;
        public string SelectedCurrentLoopbackRoute
        {
            get => _selectedCurrentLoopbackRoute;
            set
            {
                if (SetProperty(ref _selectedCurrentLoopbackRoute, value))
                {
                    if (!_isRestoringRuntimeState)
                    {
                        IsCurrentLoopbackConnected = GetRouteConnectedState(BuildRouteStateKey("CurrentLoopback", SelectedCurrentLoopbackRoute));
                    }
                    ToggleCurrentLoopbackCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        private bool _isCurrentLoopbackConnected;
        private bool _isTogglingCurrentLoopback;
        public bool IsCurrentLoopbackConnected
        {
            get => _isCurrentLoopbackConnected;
            set => SetProperty(ref _isCurrentLoopbackConnected, value);
        }

        public DelegateCommand<object> ToggleCurrentLoopbackCommand { get; }

        // 新增：32通道AD采集回采测试
        public ObservableCollection<string> AdAcquireRoutes { get; } = new ObservableCollection<string>();
        private string _selectedAdAcquireRoute;
        public string SelectedAdAcquireRoute
        {
            get => _selectedAdAcquireRoute;
            set
            {
                if (SetProperty(ref _selectedAdAcquireRoute, value))
                {
                    if (!_isRestoringRuntimeState)
                    {
                        IsAdAcquireConnected = GetRouteConnectedState(BuildRouteStateKey("AdAcquire", SelectedAdAcquireRoute, SelectedAdAcquireMeasureOption));
                    }
                    ToggleAdAcquireCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        public ObservableCollection<string> AdAcquireMeasureOptions { get; } = new ObservableCollection<string>();
        private string _selectedAdAcquireMeasureOption;
        public string SelectedAdAcquireMeasureOption
        {
            get => _selectedAdAcquireMeasureOption;
            set
            {
                if (SetProperty(ref _selectedAdAcquireMeasureOption, value))
                {
                    if (!_isRestoringRuntimeState)
                    {
                        IsAdAcquireConnected = GetRouteConnectedState(BuildRouteStateKey("AdAcquire", SelectedAdAcquireRoute, SelectedAdAcquireMeasureOption));
                    }
                    ToggleAdAcquireCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        private bool _isAdAcquireConnected;
        private bool _isTogglingAdAcquire;
        public bool IsAdAcquireConnected
        {
            get => _isAdAcquireConnected;
            set => SetProperty(ref _isAdAcquireConnected, value);
        }

        public DelegateCommand<object> ToggleAdAcquireCommand { get; }

        public DelegateCommand<object> ToggleResolverLoopbackCommand { get; }
        public ObservableCollection<string> ResolverLoopbackRoutes { get; } = new ObservableCollection<string>();
        private string _selectedResolverLoopbackRoute;
        public string SelectedResolverLoopbackRoute
        {
            get => _selectedResolverLoopbackRoute;
            set
            {
                if (SetProperty(ref _selectedResolverLoopbackRoute, value))
                {
                    if (!_isRestoringRuntimeState)
                    {
                        IsResolverLoopbackConnected = GetRouteConnectedState(BuildRouteStateKey("ResolverLoopback", SelectedResolverLoopbackRoute, SelectedResolverLoopbackMeasureOption));
                    }
                    ToggleResolverLoopbackCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        public ObservableCollection<string> ResolverLoopbackMeasureOptions { get; } = new ObservableCollection<string>();
        private string _selectedResolverLoopbackMeasureOption;
        public string SelectedResolverLoopbackMeasureOption
        {
            get => _selectedResolverLoopbackMeasureOption;
            set
            {
                if (SetProperty(ref _selectedResolverLoopbackMeasureOption, value))
                {
                    if (!_isRestoringRuntimeState)
                    {
                        IsResolverLoopbackConnected = GetRouteConnectedState(BuildRouteStateKey("ResolverLoopback", SelectedResolverLoopbackRoute, SelectedResolverLoopbackMeasureOption));
                    }
                    ToggleResolverLoopbackCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        private bool _isResolverLoopbackConnected;
        private bool _isTogglingResolverLoopback;
        public bool IsResolverLoopbackConnected
        {
            get => _isResolverLoopbackConnected;
            set => SetProperty(ref _isResolverLoopbackConnected, value);
        }

        public DelegateCommand<object> ToggleLvdtLoopbackCommand { get; }
        public ObservableCollection<string> LvdtLoopbackRoutes { get; } = new ObservableCollection<string>();
        private string _selectedLvdtLoopbackRoute;
        public string SelectedLvdtLoopbackRoute
        {
            get => _selectedLvdtLoopbackRoute;
            set
            {
                if (SetProperty(ref _selectedLvdtLoopbackRoute, value))
                {
                    if (!_isRestoringRuntimeState)
                    {
                        IsLvdtLoopbackConnected = GetRouteConnectedState(BuildRouteStateKey("LvdtLoopback", SelectedLvdtLoopbackRoute, SelectedLvdtLoopbackMeasureOption));
                    }
                    ToggleLvdtLoopbackCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        public ObservableCollection<string> LvdtLoopbackMeasureOptions { get; } = new ObservableCollection<string>();
        private string _selectedLvdtLoopbackMeasureOption;
        public string SelectedLvdtLoopbackMeasureOption
        {
            get => _selectedLvdtLoopbackMeasureOption;
            set
            {
                if (SetProperty(ref _selectedLvdtLoopbackMeasureOption, value))
                {
                    if (!_isRestoringRuntimeState)
                    {
                        IsLvdtLoopbackConnected = GetRouteConnectedState(BuildRouteStateKey("LvdtLoopback", SelectedLvdtLoopbackRoute, SelectedLvdtLoopbackMeasureOption));
                    }
                    ToggleLvdtLoopbackCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        private bool _isLvdtLoopbackConnected;
        private bool _isTogglingLvdtLoopback;
        public bool IsLvdtLoopbackConnected
        {
            get => _isLvdtLoopbackConnected;
            set => SetProperty(ref _isLvdtLoopbackConnected, value);
        }

        // 路由构造器字典（用于将下拉项映射到对应的 BuildXxxRoute 方法）
        private readonly Dictionary<string, Func<Route>> _routeBuilders = new Dictionary<string, Func<Route>>();

        private readonly Dictionary<int, MatrixRouteAction[]> _resistanceAcquireRouteActions = new Dictionary<int, MatrixRouteAction[]>
    {
        {
            1,
            new[]
            {
                new MatrixRouteAction { InNode = "I1", OutNode = "O8", Slot = 6, Ip = "192.168.1.3" },
                new MatrixRouteAction { InNode = "I4", OutNode = "O2", Slot = 4, Ip = "192.168.1.3" }
            }
        },
        {
            2,
            new[]
            {
                new MatrixRouteAction { InNode = "I1", OutNode = "O9", Slot = 6, Ip = "192.168.1.3" },
                new MatrixRouteAction { InNode = "I4", OutNode = "O2", Slot = 4, Ip = "192.168.1.3" }
            }
        },
        {
            3,
            new[]
            {
                new MatrixRouteAction { InNode = "I1", OutNode = "O10", Slot = 6, Ip = "192.168.1.3" },
                new MatrixRouteAction { InNode = "I4", OutNode = "O2", Slot = 4, Ip = "192.168.1.3" }
            }
        },

        {
            4,
            new[]
            {
                new MatrixRouteAction { InNode = "I1", OutNode = "O11", Slot = 6, Ip = "192.168.1.3" },
                new MatrixRouteAction { InNode = "I4", OutNode = "O2", Slot = 4, Ip = "192.168.1.3" }
            }
        },

        {
            5,
            new[]
            {
                new MatrixRouteAction { InNode = "I1", OutNode = "O12", Slot = 6, Ip = "192.168.1.3" },
                new MatrixRouteAction { InNode = "I4", OutNode = "O2", Slot = 4, Ip = "192.168.1.3" }
            }
        },

        {
            6,
            new[]
            {
                new MatrixRouteAction { InNode = "I1", OutNode = "O13", Slot = 6, Ip = "192.168.1.3" },
                new MatrixRouteAction { InNode = "I4", OutNode = "O2", Slot = 4, Ip = "192.168.1.3" }
            }
        },

        {
            7,
            new[]
            {
                new MatrixRouteAction { InNode = "I1", OutNode = "O14", Slot = 6, Ip = "192.168.1.3" },
                new MatrixRouteAction { InNode = "I4", OutNode = "O2", Slot = 4, Ip = "192.168.1.3" }
            }
        },

        {
            8,
            new[]
            {
                new MatrixRouteAction { InNode = "I1", OutNode = "O15", Slot = 6, Ip = "192.168.1.3" },
                new MatrixRouteAction { InNode = "I4", OutNode = "O2", Slot = 4, Ip = "192.168.1.3" }
            }
        },

        {
            9,
            new[]
            {
                new MatrixRouteAction { InNode = "I1", OutNode = "O16", Slot = 6, Ip = "192.168.1.3" },
                new MatrixRouteAction { InNode = "I4", OutNode = "O2", Slot = 4, Ip = "192.168.1.3" }
            }
        },

        {
            10,
            new[]
            {
                new MatrixRouteAction { InNode = "I1", OutNode = "O17", Slot = 6, Ip = "192.168.1.3" },
                new MatrixRouteAction { InNode = "I4", OutNode = "O2", Slot = 4, Ip = "192.168.1.3" }
            }
        },

        {
            11,
            new[]
            {
               new MatrixRouteAction { InNode = "I1", OutNode = "O18", Slot = 6, Ip = "192.168.1.3" },
                new MatrixRouteAction { InNode = "I4", OutNode = "O2", Slot = 4, Ip = "192.168.1.3" }
            }
        },

        {
            12,
            new[]
            {
                new MatrixRouteAction { InNode = "I1", OutNode = "O19", Slot = 6, Ip = "192.168.1.3" },
                new MatrixRouteAction { InNode = "I4", OutNode = "O2", Slot = 4, Ip = "192.168.1.3" }
            }
        },

        {
            13,
            new[]
            {
                new MatrixRouteAction { InNode = "I1", OutNode = "O20", Slot = 6, Ip = "192.168.1.3" },
                new MatrixRouteAction { InNode = "I4", OutNode = "O2", Slot = 4, Ip = "192.168.1.3" }
            }
        },

        {
            14,
            new[]
            {
                new MatrixRouteAction { InNode = "I1", OutNode = "O21", Slot = 6, Ip = "192.168.1.3" },
                new MatrixRouteAction { InNode = "I4", OutNode = "O2", Slot = 4, Ip = "192.168.1.3" }
            }
        },

        {
            15,
            new[]
            {
               new MatrixRouteAction { InNode = "I1", OutNode = "O22", Slot = 6, Ip = "192.168.1.3" },
                new MatrixRouteAction { InNode = "I4", OutNode = "O2", Slot = 4, Ip = "192.168.1.3" }
            }
        },

        {
            16,
            new[]
            {
               new MatrixRouteAction { InNode = "I1", OutNode = "O23", Slot = 6, Ip = "192.168.1.3" },
                new MatrixRouteAction { InNode = "I4", OutNode = "O2", Slot = 4, Ip = "192.168.1.3" }
            }
        },

        {
            17,
            new[]
            {
                new MatrixRouteAction { InNode = "I1", OutNode = "O24", Slot = 6, Ip = "192.168.1.3" },
                new MatrixRouteAction { InNode = "I4", OutNode = "O2", Slot = 4, Ip = "192.168.1.3" }
            }
        },

        {
            18,
            new[]
            {
                new MatrixRouteAction { InNode = "I1", OutNode = "O25", Slot = 6, Ip = "192.168.1.3" },
                new MatrixRouteAction { InNode = "I4", OutNode = "O2", Slot = 4, Ip = "192.168.1.3" }
            }
        },

        {
            19,
            new[]
            {
                new MatrixRouteAction { InNode = "I1", OutNode = "O26", Slot = 6, Ip = "192.168.1.3" },
                new MatrixRouteAction { InNode = "I4", OutNode = "O2", Slot = 4, Ip = "192.168.1.3" }
            }
        },

        {
            20,
            new[]
            {
                new MatrixRouteAction { InNode = "I1", OutNode = "O27", Slot = 6, Ip = "192.168.1.3" },
                new MatrixRouteAction { InNode = "I4", OutNode = "O2", Slot = 4, Ip = "192.168.1.3" }
            }
        }
    };

        private readonly Dictionary<int, MatrixRouteAction[]> _discreteInputLoopbackRouteActions = new Dictionary<int, MatrixRouteAction[]>
    {
        { 1, new[] { new MatrixRouteAction { InNode = "I0", OutNode = "O0", Slot = 2, Ip = "192.168.1.3", TcpBasePort = 50300 } } },
        { 2, new[] { new MatrixRouteAction { InNode = "I0", OutNode = "O1", Slot = 2, Ip = "192.168.1.3", TcpBasePort = 50300 } } },
        { 3, new[] { new MatrixRouteAction { InNode = "I0", OutNode = "O2", Slot = 2, Ip = "192.168.1.3", TcpBasePort = 50300 } } },
        { 4, new[] { new MatrixRouteAction { InNode = "I0", OutNode = "O3", Slot = 2, Ip = "192.168.1.3", TcpBasePort = 50300 } } },
        { 5, new[] { new MatrixRouteAction { InNode = "I0", OutNode = "O4", Slot = 2, Ip = "192.168.1.3", TcpBasePort = 50300 } } },
        { 6, new[] { new MatrixRouteAction { InNode = "I0", OutNode = "O5", Slot = 2, Ip = "192.168.1.3", TcpBasePort = 50300 } } },
        { 7, new[] { new MatrixRouteAction { InNode = "I0", OutNode = "O6", Slot = 2, Ip = "192.168.1.3", TcpBasePort = 50300 } } },
        { 8, new[] { new MatrixRouteAction { InNode = "I0", OutNode = "O7", Slot = 2, Ip = "192.168.1.3", TcpBasePort = 50300 } } },
        { 9, new[] { new MatrixRouteAction { InNode = "I0", OutNode = "O8", Slot = 2, Ip = "192.168.1.3", TcpBasePort = 50300 } } },
        { 10, new[] { new MatrixRouteAction { InNode = "I0", OutNode = "O9", Slot = 2, Ip = "192.168.1.3", TcpBasePort = 50300 } } },
        { 11, new[] { new MatrixRouteAction { InNode = "I0", OutNode = "O10", Slot = 2, Ip = "192.168.1.3", TcpBasePort = 50300 } } },
        { 12, new[] { new MatrixRouteAction { InNode = "I0", OutNode = "O11", Slot = 2, Ip = "192.168.1.3", TcpBasePort = 50300 } } },
        { 13, new[] { new MatrixRouteAction { InNode = "I0", OutNode = "O12", Slot = 2, Ip = "192.168.1.3", TcpBasePort = 50300 } } },
        { 14, new[] { new MatrixRouteAction { InNode = "I0", OutNode = "O13", Slot = 2, Ip = "192.168.1.3", TcpBasePort = 50300 } } },
        { 15, new[] { new MatrixRouteAction { InNode = "I0", OutNode = "O14", Slot = 2, Ip = "192.168.1.3", TcpBasePort = 50300 } } },
        { 16, new[] { new MatrixRouteAction { InNode = "I0", OutNode = "O15", Slot = 2, Ip = "192.168.1.3", TcpBasePort = 50300 } } },
        { 17, new[] { new MatrixRouteAction { InNode = "I0", OutNode = "O16", Slot = 2, Ip = "192.168.1.3", TcpBasePort = 50300 } } },
        { 18, new[] { new MatrixRouteAction { InNode = "I0", OutNode = "O17", Slot = 2, Ip = "192.168.1.3", TcpBasePort = 50300 } } },
        { 19, new[] { new MatrixRouteAction { InNode = "I0", OutNode = "O18", Slot = 2, Ip = "192.168.1.3", TcpBasePort = 50300 } } },
        { 20, new[] { new MatrixRouteAction { InNode = "I0", OutNode = "O19", Slot = 2, Ip = "192.168.1.3", TcpBasePort = 50300 } } },
        { 21, new[] { new MatrixRouteAction { InNode = "I0", OutNode = "O20", Slot = 2, Ip = "192.168.1.3", TcpBasePort = 50300 } } },
        { 22, new[] { new MatrixRouteAction { InNode = "I0", OutNode = "O21", Slot = 2, Ip = "192.168.1.3", TcpBasePort = 50300 } } },
        { 23, new[] { new MatrixRouteAction { InNode = "I0", OutNode = "O22", Slot = 2, Ip = "192.168.1.3", TcpBasePort = 50300 } } },
        { 24, new[] { new MatrixRouteAction { InNode = "I0", OutNode = "O23", Slot = 2, Ip = "192.168.1.3", TcpBasePort = 50300 } } },
        { 25, new[] { new MatrixRouteAction { InNode = "I0", OutNode = "O24", Slot = 2, Ip = "192.168.1.3", TcpBasePort = 50300 } } },
        { 26, new[] { new MatrixRouteAction { InNode = "I0", OutNode = "O25", Slot = 2, Ip = "192.168.1.3", TcpBasePort = 50300 } } },
        { 27, new[] { new MatrixRouteAction { InNode = "I0", OutNode = "O26", Slot = 2, Ip = "192.168.1.3", TcpBasePort = 50300 } } },
        { 28, new[] { new MatrixRouteAction { InNode = "I0", OutNode = "O27", Slot = 2, Ip = "192.168.1.3", TcpBasePort = 50300 } } },
        { 29, new[] { new MatrixRouteAction { InNode = "I0", OutNode = "O28", Slot = 2, Ip = "192.168.1.3", TcpBasePort = 50300 } } },
        { 30, new[] { new MatrixRouteAction { InNode = "I0", OutNode = "O29", Slot = 2, Ip = "192.168.1.3", TcpBasePort = 50300 } } },
        { 31, new[] { new MatrixRouteAction { InNode = "I0", OutNode = "O30", Slot = 2, Ip = "192.168.1.3", TcpBasePort = 50300 } } },
        { 32, new[] { new MatrixRouteAction { InNode = "I0", OutNode = "O31", Slot = 2, Ip = "192.168.1.3", TcpBasePort = 50300 } } }
    };

        private readonly Dictionary<int, MatrixRouteAction[]> _discreteOutputLoopbackRouteActions = new Dictionary<int, MatrixRouteAction[]>
    {
        { 1, new[] { new MatrixRouteAction { InNode = "I1", OutNode = "O32", Slot = 2, Ip = "192.168.1.3", TcpBasePort = 50300 } } },
        { 2, new[] { new MatrixRouteAction { InNode = "I1", OutNode = "O33", Slot = 2, Ip = "192.168.1.3", TcpBasePort = 50300 } } },
        { 3, new[] { new MatrixRouteAction { InNode = "I1", OutNode = "O34", Slot = 2, Ip = "192.168.1.3", TcpBasePort = 50300 } } },
        { 4, new[] { new MatrixRouteAction { InNode = "I1", OutNode = "O35", Slot = 2, Ip = "192.168.1.3", TcpBasePort = 50300 } } },
        { 5, new[] { new MatrixRouteAction { InNode = "I1", OutNode = "O36", Slot = 2, Ip = "192.168.1.3", TcpBasePort = 50300 } } },
        { 6, new[] { new MatrixRouteAction { InNode = "I1", OutNode = "O37", Slot = 2, Ip = "192.168.1.3", TcpBasePort = 50300 } } },
        { 7, new[] { new MatrixRouteAction { InNode = "I1", OutNode = "O38", Slot = 2, Ip = "192.168.1.3", TcpBasePort = 50300 } } },
        { 8, new[] { new MatrixRouteAction { InNode = "I1", OutNode = "O39", Slot = 2, Ip = "192.168.1.3", TcpBasePort = 50300 } } },
        { 9, new[] { new MatrixRouteAction { InNode = "I1", OutNode = "O40", Slot = 2, Ip = "192.168.1.3", TcpBasePort = 50300 } } },
        { 10, new[] { new MatrixRouteAction { InNode = "I1", OutNode = "O41", Slot = 2, Ip = "192.168.1.3", TcpBasePort = 50300 } } },
        { 11, new[] { new MatrixRouteAction { InNode = "I1", OutNode = "O42", Slot = 2, Ip = "192.168.1.3", TcpBasePort = 50300 } } },
        { 12, new[] { new MatrixRouteAction { InNode = "I1", OutNode = "O43", Slot = 2, Ip = "192.168.1.3", TcpBasePort = 50300 } } },
        { 13, new[] { new MatrixRouteAction { InNode = "I1", OutNode = "O44", Slot = 2, Ip = "192.168.1.3", TcpBasePort = 50300 } } },
        { 14, new[] { new MatrixRouteAction { InNode = "I1", OutNode = "O45", Slot = 2, Ip = "192.168.1.3", TcpBasePort = 50300 } } },
        { 15, new[] { new MatrixRouteAction { InNode = "I1", OutNode = "O46", Slot = 2, Ip = "192.168.1.3", TcpBasePort = 50300 } } },
        { 16, new[] { new MatrixRouteAction { InNode = "I1", OutNode = "O47", Slot = 2, Ip = "192.168.1.3", TcpBasePort = 50300 } } },
        { 17, new[] { new MatrixRouteAction { InNode = "I1", OutNode = "O48", Slot = 2, Ip = "192.168.1.3", TcpBasePort = 50300 } } },
        { 18, new[] { new MatrixRouteAction { InNode = "I1", OutNode = "O49", Slot = 2, Ip = "192.168.1.3", TcpBasePort = 50300 } } },
        { 19, new[] { new MatrixRouteAction { InNode = "I1", OutNode = "O50", Slot = 2, Ip = "192.168.1.3", TcpBasePort = 50300 } } },
        { 20, new[] { new MatrixRouteAction { InNode = "I1", OutNode = "O51", Slot = 2, Ip = "192.168.1.3", TcpBasePort = 50300 } } },
        { 21, new[] { new MatrixRouteAction { InNode = "I1", OutNode = "O52", Slot = 2, Ip = "192.168.1.3", TcpBasePort = 50300 } } },
        { 22, new[] { new MatrixRouteAction { InNode = "I1", OutNode = "O53", Slot = 2, Ip = "192.168.1.3", TcpBasePort = 50300 } } },
        { 23, new[] { new MatrixRouteAction { InNode = "I1", OutNode = "O54", Slot = 2, Ip = "192.168.1.3", TcpBasePort = 50300 } } },
        { 24, new[] { new MatrixRouteAction { InNode = "I1", OutNode = "O55", Slot = 2, Ip = "192.168.1.3", TcpBasePort = 50300 } } },
        { 25, new[] { new MatrixRouteAction { InNode = "I1", OutNode = "O56", Slot = 2, Ip = "192.168.1.3", TcpBasePort = 50300 } } },
        { 26, new[] { new MatrixRouteAction { InNode = "I1", OutNode = "O57", Slot = 2, Ip = "192.168.1.3", TcpBasePort = 50300 } } },
        { 27, new[] { new MatrixRouteAction { InNode = "I1", OutNode = "O58", Slot = 2, Ip = "192.168.1.3", TcpBasePort = 50300 } } },
        { 28, new[] { new MatrixRouteAction { InNode = "I1", OutNode = "O59", Slot = 2, Ip = "192.168.1.3", TcpBasePort = 50300 } } },
        { 29, new[] { new MatrixRouteAction { InNode = "I1", OutNode = "O60", Slot = 2, Ip = "192.168.1.3", TcpBasePort = 50300 } } },
        { 30, new[] { new MatrixRouteAction { InNode = "I1", OutNode = "O61", Slot = 2, Ip = "192.168.1.3", TcpBasePort = 50300 } } },
        { 31, new[] { new MatrixRouteAction { InNode = "I1", OutNode = "O62", Slot = 2, Ip = "192.168.1.3", TcpBasePort = 50300 } } },
        { 32, new[] { new MatrixRouteAction { InNode = "I1", OutNode = "O63", Slot = 2, Ip = "192.168.1.3", TcpBasePort = 50300 } } }
    };

        private readonly Dictionary<int, MatrixRouteAction[]> _currentLoopbackRouteActions = new Dictionary<int, MatrixRouteAction[]>
    {
        { 1, new MatrixRouteAction[] { new MatrixRouteAction { InNode = "I0", OutNode = "O0", Slot = 6, Ip = "192.168.1.3" } } },
        { 2, new MatrixRouteAction[] { new MatrixRouteAction { InNode = "I0", OutNode = "O1", Slot = 6, Ip = "192.168.1.3" } } },
        { 3, new MatrixRouteAction[] { new MatrixRouteAction { InNode = "I0", OutNode = "O2", Slot = 6, Ip = "192.168.1.3" } } },
        { 4, new MatrixRouteAction[] { new MatrixRouteAction { InNode = "I0", OutNode = "O3", Slot = 6, Ip = "192.168.1.3" } } },
        { 5, new MatrixRouteAction[] { new MatrixRouteAction { InNode = "I0", OutNode = "O4", Slot = 6, Ip = "192.168.1.3" } } },
        { 6, new MatrixRouteAction[] { new MatrixRouteAction { InNode = "I0", OutNode = "O5", Slot = 6, Ip = "192.168.1.3" } } },
        { 7, new MatrixRouteAction[] { new MatrixRouteAction { InNode = "I0", OutNode = "O6", Slot = 6, Ip = "192.168.1.3" } } },
        { 8, new MatrixRouteAction[] { new MatrixRouteAction { InNode = "I0", OutNode = "O7", Slot = 6, Ip = "192.168.1.3" } } }
    };

        private readonly Dictionary<int, MatrixRouteAction[]> _adAcquireRouteActions = new Dictionary<int, MatrixRouteAction[]>
    {
        { 1, new[] { new MatrixRouteAction { InNode = "I0", OutNode = "O0", Slot = 9, Ip = "192.168.1.3" } } },
        { 2, new[] { new MatrixRouteAction { InNode = "I0", OutNode = "O1", Slot = 9, Ip = "192.168.1.3" } } },
        { 3, new[] { new MatrixRouteAction { InNode = "I0", OutNode = "O2", Slot = 9, Ip = "192.168.1.3" } } },
        { 4, new[] { new MatrixRouteAction { InNode = "I0", OutNode = "O3", Slot = 9, Ip = "192.168.1.3" } } },
        { 5, new[] { new MatrixRouteAction { InNode = "I0", OutNode = "O4", Slot = 9, Ip = "192.168.1.3" } } },
        { 6, new[] { new MatrixRouteAction { InNode = "I0", OutNode = "O5", Slot = 9, Ip = "192.168.1.3" } } },
        { 7, new[] { new MatrixRouteAction { InNode = "I0", OutNode = "O6", Slot = 9, Ip = "192.168.1.3" } } },
        { 8, new[] { new MatrixRouteAction { InNode = "I0", OutNode = "O7", Slot = 9, Ip = "192.168.1.3" } } },
        { 9, new[] { new MatrixRouteAction { InNode = "I0", OutNode = "O8", Slot = 9, Ip = "192.168.1.3" } } },
        { 10, new[] { new MatrixRouteAction { InNode = "I0", OutNode = "O9", Slot = 9, Ip = "192.168.1.3" } } },
        { 11, new[] { new MatrixRouteAction { InNode = "I0", OutNode = "O10", Slot = 9, Ip = "192.168.1.3" } } },
        { 12, new[] { new MatrixRouteAction { InNode = "I0", OutNode = "O11", Slot = 9, Ip = "192.168.1.3" } } },
        { 13, new[] { new MatrixRouteAction { InNode = "I0", OutNode = "O12", Slot = 9, Ip = "192.168.1.3" } } },
        { 14, new[] { new MatrixRouteAction { InNode = "I0", OutNode = "O13", Slot = 9, Ip = "192.168.1.3" } } },
        { 15, new[] { new MatrixRouteAction { InNode = "I0", OutNode = "O14", Slot = 9, Ip = "192.168.1.3" } } },
        { 16, new[] { new MatrixRouteAction { InNode = "I0", OutNode = "O15", Slot = 9, Ip = "192.168.1.3" } } },
        { 17, new[] { new MatrixRouteAction { InNode = "I0", OutNode = "O16", Slot = 9, Ip = "192.168.1.3" } } },
        { 18, new[] { new MatrixRouteAction { InNode = "I0", OutNode = "O17", Slot = 9, Ip = "192.168.1.3" } } },
        { 19, new[] { new MatrixRouteAction { InNode = "I0", OutNode = "O18", Slot = 9, Ip = "192.168.1.3" } } },
        { 20, new[] { new MatrixRouteAction { InNode = "I0", OutNode = "O19", Slot = 9, Ip = "192.168.1.3" } } },
        { 21, new[] { new MatrixRouteAction { InNode = "I0", OutNode = "O20", Slot = 9, Ip = "192.168.1.3" } } },
        { 22, new[] { new MatrixRouteAction { InNode = "I0", OutNode = "O21", Slot = 9, Ip = "192.168.1.3" } } },
        { 23, new[] { new MatrixRouteAction { InNode = "I0", OutNode = "O22", Slot = 9, Ip = "192.168.1.3" } } },
        { 24, new[] { new MatrixRouteAction { InNode = "I0", OutNode = "O23", Slot = 9, Ip = "192.168.1.3" } } },
        { 25, new[] { new MatrixRouteAction { InNode = "I0", OutNode = "O24", Slot = 9, Ip = "192.168.1.3" } } },
        { 26, new[] { new MatrixRouteAction { InNode = "I0", OutNode = "O25", Slot = 9, Ip = "192.168.1.3" } } },
        { 27, new[] { new MatrixRouteAction { InNode = "I0", OutNode = "O26", Slot = 9, Ip = "192.168.1.3" } } },
        { 28, new[] { new MatrixRouteAction { InNode = "I0", OutNode = "O27", Slot = 9, Ip = "192.168.1.3" } } },
        { 29, new[] { new MatrixRouteAction { InNode = "I0", OutNode = "O28", Slot = 9, Ip = "192.168.1.3" } } },
        { 30, new[] { new MatrixRouteAction { InNode = "I0", OutNode = "O29", Slot = 9, Ip = "192.168.1.3" } } },
        { 31, new[] { new MatrixRouteAction { InNode = "I0", OutNode = "O30", Slot = 9, Ip = "192.168.1.3" } } },
        { 32, new[] { new MatrixRouteAction { InNode = "I0", OutNode = "O31", Slot = 9, Ip = "192.168.1.3" } } }
    };

        #endregion

        #region Constructor
        public MatrixSwitchConfigTableViewModel(
            IRegionManager regionManager,
            IEventAggregator eventAggregator,
            ProjectService projectService,
            IPxiChassisService pxiChassisService)
        {
            DebugLog($"MatrixSwitchConfigTableViewModel 构造函数开始");

            _regionManager = regionManager;
            _eventAggregator = eventAggregator;
            _projectService = projectService;
            _pxiChassisService = pxiChassisService;

            // 初始化可用设备列表
            AvailableSwitchDevices = new ObservableCollection<string>();
            DebugLog($"初始化AvailableSwitchDevices，初始数量: {AvailableSwitchDevices.Count}");

            // 初始化命令
            AddMatrixSwitchCommand = new DelegateCommand(OnAddMatrixSwitch);
            DeleteMatrixSwitchCommand = new DelegateCommand<MatrixSwitchConfigItem>(OnDeleteMatrixSwitch);
            EditMatrixSwitchCommand = new DelegateCommand<MatrixSwitchConfigItem>(OnEditMatrixSwitch);
            NavigateBackCommand = new DelegateCommand(OnNavigateBack);
            CloseCommand = new DelegateCommand(OnClose);
            PreviousPageCommand = new DelegateCommand(OnPreviousPage, CanGoToPreviousPage);
            NextPageCommand = new DelegateCommand(OnNextPage, CanGoToNextPage);
            // 电阻采集1 一键连接命令
            ConnectResistanceAcquire1Command = new DelegateCommand(async () => await ConnectSelectedResistanceAsync());

            // 订阅设备修改事件，当设备变化时更新可用设备列表
            _eventAggregator.GetEvent<DeviceModifiedEvent>().Subscribe(OnDeviceModified, ThreadOption.UIThread);
            DebugLog($"订阅DeviceModifiedEvent");

            foreach (var idx in _resistanceAcquireRouteActions.Keys.OrderBy(x => x))
            {
                var index = idx;
                var name = $"电阻采集{index}";
                _routeBuilders[name] = () => BuildResistanceAcquireRoute(index);
                ResistanceRoutes.Add(name);
            }

            // 使用 Prism 的 FromAsyncHandler 来支持 async 方法和可执行检查
            ConnectSelectedResistanceCommand = new DelegateCommand(async () => await ConnectSelectedResistanceAsync(), () => !string.IsNullOrEmpty(SelectedResistanceRoute));
            DisconnectSelectedResistanceCommand = new DelegateCommand(async () => await DisconnectSelectedResistanceAsync(), () => !string.IsNullOrEmpty(SelectedResistanceRoute));
            ToggleSelectedResistanceCommand = new DelegateCommand<object>(OnToggleSelectedResistance, _ => !string.IsNullOrEmpty(SelectedResistanceRoute));

            for (int i = 1; i <= 32; i++)
            {
                DiscreteInputLoopbackRoutes.Add($"离散输入回采{i}");
                DiscreteOutputLoopbackRoutes.Add($"离散输出回采{i}");
            }

            DiscreteInputLoopbackMeasureOptions.Add("万用表");
            DiscreteInputLoopbackMeasureOptions.Add("示波器通道1");
            DiscreteInputLoopbackMeasureOptions.Add("示波器通道2");
            DiscreteInputLoopbackMeasureOptions.Add("示波器通道3");
            DiscreteInputLoopbackMeasureOptions.Add("示波器通道4");

            DiscreteOutputLoopbackMeasureOptions.Add("万用表");
            DiscreteOutputLoopbackMeasureOptions.Add("示波器通道1");
            DiscreteOutputLoopbackMeasureOptions.Add("示波器通道2");
            DiscreteOutputLoopbackMeasureOptions.Add("示波器通道3");
            DiscreteOutputLoopbackMeasureOptions.Add("示波器通道4");

            ToggleDiscreteInputLoopbackCommand = new DelegateCommand<object>(OnToggleDiscreteInputLoopback, _ => !string.IsNullOrEmpty(SelectedDiscreteInputLoopbackRoute) && !string.IsNullOrEmpty(SelectedDiscreteInputLoopbackMeasureOption));
            ToggleDiscreteOutputLoopbackCommand = new DelegateCommand<object>(OnToggleDiscreteOutputLoopback, _ => !string.IsNullOrEmpty(SelectedDiscreteOutputLoopbackRoute) && !string.IsNullOrEmpty(SelectedDiscreteOutputLoopbackMeasureOption));

            for (int i = 1; i <= 64; i++)
            {
                HighSpeedIoLoopbackRoutes.Add($"高速IO回采{i}");
            }

            HighSpeedIoLoopbackMeasureOptions.Add("万用表");
            HighSpeedIoLoopbackMeasureOptions.Add("示波器通道1");
            HighSpeedIoLoopbackMeasureOptions.Add("示波器通道2");
            HighSpeedIoLoopbackMeasureOptions.Add("示波器通道3");
            HighSpeedIoLoopbackMeasureOptions.Add("示波器通道4");

            ToggleHighSpeedIoLoopbackCommand = new DelegateCommand<object>(OnToggleHighSpeedIoLoopback, _ => !string.IsNullOrEmpty(SelectedHighSpeedIoLoopbackRoute) && !string.IsNullOrEmpty(SelectedHighSpeedIoLoopbackMeasureOption));

            for (int i = 2; i <= 8; i++)
            {
                FrequencyAcquireRoutes.Add($"频率采集{i}");
            }



            for (int i = 2; i <= 10; i++)
            {
                FrequencyOutputRoutes.Add($"频率输出{i}");
            }

            FrequencyOutput1MeasureOptions.Add("万用表");
            FrequencyOutput1MeasureOptions.Add("示波器通道1");
            FrequencyOutput1MeasureOptions.Add("示波器通道2");
            FrequencyOutput1MeasureOptions.Add("示波器通道3");
            FrequencyOutput1MeasureOptions.Add("示波器通道4");
            FrequencyAcquireMeasureOptions.Add("频率计");
            FrequencyAcquireMeasureOptions.Add("万用表测频率");
            FrequencyAcquireMeasureOptions.Add("示波器通道1");
            FrequencyAcquireMeasureOptions.Add("示波器通道2");
            FrequencyAcquireMeasureOptions.Add("示波器通道3");
            FrequencyAcquireMeasureOptions.Add("示波器通道4");

            ToggleFrequencyAcquireCommand = new DelegateCommand<object>(OnToggleFrequencyAcquire, _ => !string.IsNullOrEmpty(SelectedFrequencyAcquireRoute) && !string.IsNullOrEmpty(SelectedFrequencyAcquireMeasureOption));
            ToggleFrequencyOutput1Command = new DelegateCommand<object>(OnToggleFrequencyOutput1, _ => !string.IsNullOrEmpty(SelectedFrequencyOutput1MeasureOption));
            ToggleFrequencyOutputCommand = new DelegateCommand<object>(OnToggleFrequencyOutput, _ => !string.IsNullOrEmpty(SelectedFrequencyOutputRoute));

            for (int i = 1; i <= 8; i++)
            {
                CurrentLoopbackRoutes.Add($"电流回采{i}");
            }

            ToggleCurrentLoopbackCommand = new DelegateCommand<object>(OnToggleCurrentLoopback, _ => !string.IsNullOrEmpty(SelectedCurrentLoopbackRoute));

            for (int i = 1; i <= 32; i++)
            {
                AdAcquireRoutes.Add($"AD采集{i}");
            }

            AdAcquireMeasureOptions.Add("万用表");
            AdAcquireMeasureOptions.Add("示波器通道1");
            AdAcquireMeasureOptions.Add("示波器通道2");
            AdAcquireMeasureOptions.Add("示波器通道3");
            AdAcquireMeasureOptions.Add("示波器通道4");

            ToggleAdAcquireCommand = new DelegateCommand<object>(OnToggleAdAcquire, _ => !string.IsNullOrEmpty(SelectedAdAcquireRoute) && !string.IsNullOrEmpty(SelectedAdAcquireMeasureOption));

            for (int i = 1; i <= 5; i++)
            {
                ResolverLoopbackRoutes.Add($"RVDT_EXC{i}");
                ResolverLoopbackRoutes.Add($"RVDT_SIN{i}");
                ResolverLoopbackRoutes.Add($"RVDT_COS{i}");
            }

            for (int i = 1; i <= 5; i++)
            {
                LvdtLoopbackRoutes.Add($"LVDT_EXC{i}");
                LvdtLoopbackRoutes.Add($"LVDT_VA{i}");
                LvdtLoopbackRoutes.Add($"LVDT_VB{i}");
            }

            ResolverLoopbackMeasureOptions.Add("万用表");
            ResolverLoopbackMeasureOptions.Add("示波器通道1");
            ResolverLoopbackMeasureOptions.Add("示波器通道2");
            ResolverLoopbackMeasureOptions.Add("示波器通道3");

            LvdtLoopbackMeasureOptions.Add("万用表");
            LvdtLoopbackMeasureOptions.Add("示波器通道1");
            LvdtLoopbackMeasureOptions.Add("示波器通道2");
            LvdtLoopbackMeasureOptions.Add("示波器通道3");

            ToggleResolverLoopbackCommand = new DelegateCommand<object>(OnToggleResolverLoopback, _ => !string.IsNullOrEmpty(SelectedResolverLoopbackRoute) && !string.IsNullOrEmpty(SelectedResolverLoopbackMeasureOption));
            ToggleLvdtLoopbackCommand = new DelegateCommand<object>(OnToggleLvdtLoopback, _ => !string.IsNullOrEmpty(SelectedLvdtLoopbackRoute) && !string.IsNullOrEmpty(SelectedLvdtLoopbackMeasureOption));

            DebugLog($"MatrixSwitchConfigTableViewModel 构造函数完成");
        }

        #endregion

        // Public wrapper so code-behind can directly trigger the connect for debugging
        public async Task TriggerConnectResistanceAcquire1Async()
        {
            DebugLog("TriggerConnectResistanceAcquire1Async called (public wrapper)");
            var ok = await ConnectSelectedResistanceAsync();
            DebugLog($"TriggerConnectResistanceAcquire1Async result: {ok}");
        }

        #region ResistanceAcquire1 Implementation

        // 简单的内部类型表示单个矩阵动作
        private class MatrixRouteAction
        {
            public string InNode { get; set; }
            public string OutNode { get; set; }
            public int Slot { get; set; }
            public string Ip { get; set; }
            public int TcpBasePort { get; set; } = 50200;
        }

        // 一条通路（变量对应的 route）
        private class Route
        {
            public string Id { get; set; }
            public string DisplayName { get; set; }
            public List<MatrixRouteAction> Actions { get; } = new List<MatrixRouteAction>();
        }

        private Route BuildResistanceAcquireRoute(int index)
        {
            if (!_resistanceAcquireRouteActions.TryGetValue(index, out var actions))
            {
                throw new InvalidOperationException($"未找到电阻采集{index}的路由配置");
            }

            var r = new Route
            {
                Id = $"ResistanceAcquire{index}",
                DisplayName = $"电阻采集{index}"
            };

            foreach (var a in actions)
            {
                r.Actions.Add(new MatrixRouteAction
                {
                    InNode = a.InNode,
                    OutNode = a.OutNode,
                    Slot = a.Slot,
                    Ip = a.Ip,
                    TcpBasePort = a.TcpBasePort
                });
            }

            return r;
        }

        // 构造“电阻采集1”这条通路：两条动作
        private Route BuildResistanceAcquire1Route()
        {
            return BuildResistanceAcquireRoute(1);
        }

        // 示例：电阻采集2 的路由构造（按需修改节点/槽/IP）
        private Route BuildResistanceAcquire2Route()
        {
            return BuildResistanceAcquireRoute(2);
        }

        // 示例：电阻采集3 的路由构造（按需修改节点/槽/IP）
        private Route BuildResistanceAcquire3Route()
        {
            return BuildResistanceAcquireRoute(3);
        }

        private async Task<bool> ConnectSelectedResistanceAsync()
        {
            if (string.IsNullOrEmpty(SelectedResistanceRoute) || !_routeBuilders.ContainsKey(SelectedResistanceRoute))
            {
                DebugLog($"ConnectSelectedResistanceAsync: 无效的 SelectedResistanceRoute='{SelectedResistanceRoute}'");
                return false;
            }

            var svc = MatrixControlService.Instance;
            var route = _routeBuilders[SelectedResistanceRoute]();
            var completed = new List<MatrixRouteAction>();

            DebugLog($"开始执行 ConnectSelectedResistanceAsync '{SelectedResistanceRoute}': Actions={route.Actions.Count}");

            try
            {
                foreach (var a in route.Actions)
                {
                    DebugLog($"尝试连接 {a.InNode}->{a.OutNode} (slot={a.Slot}, ip={a.Ip}, tcpBasePort={a.TcpBasePort})");
                    bool ok = await svc.ConnectNodesAsync(a.InNode, a.OutNode, a.Slot, a.Ip, a.TcpBasePort);
                    DebugLog($"ConnectNodesAsync 返回: {ok} for {a.InNode}->{a.OutNode} (slot={a.Slot})");
                    if (!ok) throw new Exception($"连接失败 {a.InNode}->{a.OutNode} (slot={a.Slot})");
                    completed.Add(a);
                }

                DebugLog("ConnectSelectedResistanceAsync 完成: 所有动作成功");
                SetRouteConnectedState(BuildRouteStateKey("Resistance", SelectedResistanceRoute), true);
                return true;
            }
            catch (Exception ex)
            {
                DebugLog($"ConnectSelectedResistanceAsync 发生异常: {ex.Message}. 开始回滚已完成动作 count={completed.Count}");
                foreach (var a in completed.AsEnumerable().Reverse())
                {
                    try
                    {
                        DebugLog($"回滚: 断开 {a.InNode}->{a.OutNode} (slot={a.Slot})");
                        var dOk = await svc.DisconnectNodesAsync(a.InNode, a.OutNode, a.Slot, a.Ip, a.TcpBasePort);
                        DebugLog($"回滚结果: {dOk} for {a.InNode}->{a.OutNode} (slot={a.Slot})");
                    }
                    catch (Exception rex)
                    {
                        DebugLog($"回滚时异常: {rex.Message}");
                    }
                }

                return false;
            }
        }

        private async Task<bool> DisconnectSelectedResistanceAsync()
        {
            if (string.IsNullOrEmpty(SelectedResistanceRoute) || !_routeBuilders.ContainsKey(SelectedResistanceRoute))
            {
                DebugLog($"DisconnectSelectedResistanceAsync: 无效的 SelectedResistanceRoute='{SelectedResistanceRoute}'");
                return false;
            }

            var svc = MatrixControlService.Instance;
            var route = _routeBuilders[SelectedResistanceRoute]();

            bool anyFailed = false;
            DebugLog($"开始执行 DisconnectSelectedResistanceAsync '{SelectedResistanceRoute}': Actions={route.Actions.Count}");

            try
            {
                foreach (var a in route.Actions.AsEnumerable().Reverse())
                {
                    try
                    {
                        DebugLog($"尝试断开 {a.InNode}->{a.OutNode} (slot={a.Slot}, ip={a.Ip})");
                        var ok = await svc.DisconnectNodesAsync(a.InNode, a.OutNode, a.Slot, a.Ip, a.TcpBasePort);
                        DebugLog($"DisconnectNodesAsync 返回: {ok} for {a.InNode}->{a.OutNode} (slot={a.Slot})");
                        if (!ok) anyFailed = true;
                    }
                    catch (Exception ex)
                    {
                        DebugLog($"断开时异常: {ex.Message}");
                        anyFailed = true;
                    }
                }
                SetRouteConnectedState(BuildRouteStateKey("Resistance", SelectedResistanceRoute), anyFailed);
                return !anyFailed;
            }
            catch (Exception ex)
            {
                DebugLog($"DisconnectSelectedResistanceAsync 发生异常: {ex.Message}");
                return false;
            }
        }

        private static bool TryParseRouteIndex(string value, string prefix, out int index)
        {
            index = 0;
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            if (!value.StartsWith(prefix))
            {
                return false;
            }

            var suffix = value.Substring(prefix.Length);
            return int.TryParse(suffix, out index);
        }

        private static bool TryParseRvdtOutNodeIndex(string routeName, out int outNodeIndex)
        {
            outNodeIndex = 0;
            if (string.IsNullOrEmpty(routeName))
            {
                return false;
            }

            if (TryParseRouteIndex(routeName, "RVDT_EXC", out var idxExc))
            {
                if (idxExc < 1 || idxExc > 5) return false;
                outNodeIndex = (idxExc - 1) * 3;
                return true;
            }

            if (TryParseRouteIndex(routeName, "RVDT_SIN", out var idxSin))
            {
                if (idxSin < 1 || idxSin > 5) return false;
                outNodeIndex = (idxSin - 1) * 3 + 1;
                return true;
            }

            if (TryParseRouteIndex(routeName, "RVDT_COS", out var idxCos))
            {
                if (idxCos < 1 || idxCos > 5) return false;
                outNodeIndex = (idxCos - 1) * 3 + 2;
                return true;
            }

            return false;
        }

        private static bool TryParseLvdtOutNodeIndex(string routeName, out int outNodeIndex)
        {
            outNodeIndex = 0;
            if (string.IsNullOrEmpty(routeName))
            {
                return false;
            }

            const int baseOut = 15;

            if (TryParseRouteIndex(routeName, "LVDT_EXC", out var idxExc))
            {
                if (idxExc < 1 || idxExc > 5) return false;
                outNodeIndex = baseOut + (idxExc - 1) * 3;
                return true;
            }

            if (TryParseRouteIndex(routeName, "LVDT_VA", out var idxVa))
            {
                if (idxVa < 1 || idxVa > 5) return false;
                outNodeIndex = baseOut + (idxVa - 1) * 3 + 1;
                return true;
            }

            if (TryParseRouteIndex(routeName, "LVDT_VB", out var idxV))
            {
                if (idxV < 1 || idxV > 5) return false;
                outNodeIndex = baseOut + (idxV - 1) * 3 + 2;
                return true;
            }

            return false;
        }

        private Route BuildDiscreteInputLoopbackRoute(int index)
        {
            if (!_discreteOutputLoopbackRouteActions.TryGetValue(index, out var actions) || actions == null || actions.Length == 0)
            {
                throw new InvalidOperationException($"未找到离散输入回采{index}的路由配置");
            }

            var r = new Route
            {
                Id = $"DiscreteInputLoopback{index}",
                DisplayName = $"离散输入回采{index}"
            };

            string firstInNode = null;
            if (SelectedDiscreteInputLoopbackMeasureOption == "万用表")
            {
                firstInNode = "I0";
            }
            else if (!string.IsNullOrEmpty(SelectedDiscreteInputLoopbackMeasureOption) && SelectedDiscreteInputLoopbackMeasureOption.StartsWith("示波器"))
            {
                firstInNode = "I1";
            }

            for (int i = 0; i < actions.Length; i++)
            {
                var a = actions[i];
                var inNode = (i == 0 && !string.IsNullOrEmpty(firstInNode)) ? firstInNode : a.InNode;
                r.Actions.Add(new MatrixRouteAction
                {
                    InNode = inNode,
                    OutNode = a.OutNode,
                    Slot = a.Slot,
                    Ip = a.Ip,
                    TcpBasePort = (a.TcpBasePort == 50200 && a.Slot == 6) ? 50300 : a.TcpBasePort
                });
            }

            var measureAction = BuildDiscreteLoopbackMeasureAction(SelectedDiscreteInputLoopbackMeasureOption);
            if (measureAction != null)
            {
                r.Actions.Add(measureAction);
            }

            return r;
        }

        private Route BuildDiscreteOutputLoopbackRoute(int index)
        {
            if (!_discreteInputLoopbackRouteActions.TryGetValue(index, out var actions) || actions == null || actions.Length == 0)
            {
                throw new InvalidOperationException($"未找到离散输出回采{index}的路由配置");
            }

            var r = new Route
            {
                Id = $"DiscreteOutputLoopback{index}",
                DisplayName = $"离散输出回采{index}"
            };

            string firstInNode = null;
            if (SelectedDiscreteOutputLoopbackMeasureOption == "万用表")
            {
                firstInNode = "I0";
            }
            else if (!string.IsNullOrEmpty(SelectedDiscreteOutputLoopbackMeasureOption) && SelectedDiscreteOutputLoopbackMeasureOption.StartsWith("示波器"))
            {
                firstInNode = "I1";
            }

            for (int i = 0; i < actions.Length; i++)
            {
                var a = actions[i];
                var inNode = (i == 0 && !string.IsNullOrEmpty(firstInNode)) ? firstInNode : a.InNode;
                r.Actions.Add(new MatrixRouteAction
                {
                    InNode = inNode,
                    OutNode = a.OutNode,
                    Slot = a.Slot,
                    Ip = a.Ip,
                    TcpBasePort = (a.TcpBasePort == 50200 && a.Slot == 6) ? 50300 : a.TcpBasePort
                });
            }

            var measureAction = BuildDiscreteLoopbackMeasureAction(SelectedDiscreteOutputLoopbackMeasureOption);
            if (measureAction != null)
            {
                r.Actions.Add(measureAction);
            }

            return r;
        }

        private Route BuildCurrentLoopbackRoute(int index)
        {
            if (!_currentLoopbackRouteActions.TryGetValue(index, out var actions) || actions == null || actions.Length == 0)
            {
                throw new InvalidOperationException($"未找到电流回采{index}的路由配置");
            }

            var r = new Route
            {
                Id = $"CurrentLoopback{index}",
                DisplayName = $"电流回采{index}"
            };

            foreach (var a in actions)
            {
                r.Actions.Add(new MatrixRouteAction
                {
                    InNode = a.InNode,
                    OutNode = a.OutNode,
                    Slot = a.Slot,
                    Ip = a.Ip,
                    TcpBasePort = a.TcpBasePort
                });
            }

            return r;
        }

        private Route BuildAdAcquireRoute(int index)
        {
            if (!_adAcquireRouteActions.TryGetValue(index, out var actions) || actions == null || actions.Length == 0)
            {
                throw new InvalidOperationException($"未找到AD采集{index}的路由配置");
            }

            var r = new Route
            {
                Id = $"AdAcquire{index}",
                DisplayName = $"AD采集{index}"
            };

            string firstInNode = null;
            if (SelectedAdAcquireMeasureOption == "万用表")
            {
                firstInNode = "I0";
            }
            else if (!string.IsNullOrEmpty(SelectedAdAcquireMeasureOption) && SelectedAdAcquireMeasureOption.StartsWith("示波器"))
            {
                firstInNode = "I1";
            }

            for (int i = 0; i < actions.Length; i++)
            {
                var a = actions[i];
                if (string.IsNullOrWhiteSpace(a.InNode) || string.IsNullOrWhiteSpace(a.OutNode) || a.Slot <= 0 || string.IsNullOrWhiteSpace(a.Ip))
                {
                    throw new InvalidOperationException($"AD采集{index}的2601动作未配置完整");
                }

                var inNode = (i == 0 && !string.IsNullOrEmpty(firstInNode)) ? firstInNode : a.InNode;

                r.Actions.Add(new MatrixRouteAction
                {
                    InNode = inNode,
                    OutNode = a.OutNode,
                    Slot = a.Slot,
                    Ip = a.Ip,
                    TcpBasePort = a.TcpBasePort
                });
            }

            var measureAction = BuildAdAcquireMeasureAction(SelectedAdAcquireMeasureOption);
            if (measureAction != null)
            {
                r.Actions.Add(measureAction);
            }

            return r;
        }

        private Route BuildRvdtLoopbackRoute()
        {
            if (!TryParseRvdtOutNodeIndex(SelectedResolverLoopbackRoute, out var outNodeIndex))
            {
                throw new InvalidOperationException($"未找到旋变回采信号路由配置: {SelectedResolverLoopbackRoute}");
            }

            var firstInNode = BuildRvdtLvdtFirstInNode(SelectedResolverLoopbackMeasureOption);
            var secondAction = BuildRvdtLvdtSecondAction(SelectedResolverLoopbackMeasureOption);
            if (string.IsNullOrEmpty(firstInNode) || secondAction == null)
            {
                throw new InvalidOperationException($"旋变回采信号测量选项无效: {SelectedResolverLoopbackMeasureOption}");
            }

            var r = new Route
            {
                Id = $"RVDT_{SelectedResolverLoopbackRoute}",
                DisplayName = SelectedResolverLoopbackRoute
            };

            r.Actions.Add(new MatrixRouteAction
            {
                InNode = firstInNode,
                OutNode = $"O{outNodeIndex}",
                Slot = 7,
                Ip = "192.168.1.3"
            });

            r.Actions.Add(secondAction);
            return r;
        }

        private Route BuildLvdtLoopbackRoute()
        {
            if (!TryParseLvdtOutNodeIndex(SelectedLvdtLoopbackRoute, out var outNodeIndex))
            {
                throw new InvalidOperationException($"未找到LVDT回采信号路由配置: {SelectedLvdtLoopbackRoute}");
            }

            var firstInNode = BuildRvdtLvdtFirstInNode(SelectedLvdtLoopbackMeasureOption);
            var secondAction = BuildRvdtLvdtSecondAction(SelectedLvdtLoopbackMeasureOption);
            if (string.IsNullOrEmpty(firstInNode) || secondAction == null)
            {
                throw new InvalidOperationException($"LVDT回采信号测量选项无效: {SelectedLvdtLoopbackMeasureOption}");
            }

            var r = new Route
            {
                Id = $"LVDT_{SelectedLvdtLoopbackRoute}",
                DisplayName = SelectedLvdtLoopbackRoute
            };

            r.Actions.Add(new MatrixRouteAction
            {
                InNode = firstInNode,
                OutNode = $"O{outNodeIndex}",
                Slot = 7,
                Ip = "192.168.1.3"
            });

            r.Actions.Add(secondAction);
            return r;
        }

        private Route BuildHighSpeedIoLoopbackRoute(int index)
        {
            if (index < 1 || index > 64)
            {
                throw new InvalidOperationException($"未找到高速IO回采{index}的路由配置");
            }

            string firstInNode = null;
            if (SelectedHighSpeedIoLoopbackMeasureOption == "万用表")
            {
                firstInNode = "I0";
            }
            else if (!string.IsNullOrEmpty(SelectedHighSpeedIoLoopbackMeasureOption) && SelectedHighSpeedIoLoopbackMeasureOption.StartsWith("示波器"))
            {
                firstInNode = "I1";
            }

            var secondAction = BuildHighSpeedIoLoopbackMeasureAction(SelectedHighSpeedIoLoopbackMeasureOption);
            if (string.IsNullOrEmpty(firstInNode) || secondAction == null)
            {
                throw new InvalidOperationException($"高速IO回采测量选项无效: {SelectedHighSpeedIoLoopbackMeasureOption}");
            }

            var r = new Route
            {
                Id = $"HighSpeedIoLoopback{index}",
                DisplayName = $"高速IO回采{index}"
            };

            // 3022(2)：slot=3，端口基址=50300
            r.Actions.Add(new MatrixRouteAction
            {
                InNode = firstInNode,
                OutNode = $"O{index - 1}",
                Slot = 3,
                Ip = "192.168.1.3",
                TcpBasePort = 50300
            });

            // 2601(1)：slot=4
            r.Actions.Add(secondAction);
            return r;
        }

        private Route BuildFrequencyOutputRoute(int index)
        {
            if (index < 2 || index > 10)
            {
                throw new InvalidOperationException($"未找到频率输出{index}的路由配置");
            }

            // 频率输出2..10：2601(4) I2 -> O0..O8
            var outNodeIndex = index - 2;
            var r = new Route
            {
                Id = $"FrequencyOutput{index}",
                DisplayName = $"频率输出{index}"
            };

            r.Actions.Add(new MatrixRouteAction
            {
                InNode = "I2",
                OutNode = $"O{outNodeIndex}",
                Slot = 8,
                Ip = "192.168.1.3"
            });

            return r;
        }

        private Route BuildFrequencyAcquireRoute(int index)
        {
            if (index < 2 || index > 8)
            {
                throw new InvalidOperationException($"未找到频率采集{index}的路由配置");
            }

            if (string.IsNullOrEmpty(SelectedFrequencyAcquireMeasureOption))
            {
                throw new InvalidOperationException("未选择2-8通道频率采集测量选项");
            }

            // 频率采集2..8：2601(4) InNode -> O9..O15
            var outNodeIndex = index + 7;
            var r = new Route
            {
                Id = $"FrequencyAcquire{index}",
                DisplayName = $"频率采集{index}"
            };

            string primaryInNode;
            if (SelectedFrequencyAcquireMeasureOption == "频率计")
            {
                primaryInNode = "I3";
            }
            else if (SelectedFrequencyAcquireMeasureOption == "万用表测频率")
            {
                primaryInNode = "I0";
            }
            else if (SelectedFrequencyAcquireMeasureOption == "示波器通道1" ||
                     SelectedFrequencyAcquireMeasureOption == "示波器通道2" ||
                     SelectedFrequencyAcquireMeasureOption == "示波器通道3" ||
                     SelectedFrequencyAcquireMeasureOption == "示波器通道4")
            {
                primaryInNode = "I2";
            }
            else
            {
                throw new InvalidOperationException($"2-8通道频率采集测量选项无效: {SelectedFrequencyAcquireMeasureOption}");
            }

            r.Actions.Add(new MatrixRouteAction
            {
                InNode = primaryInNode,
                OutNode = $"O{outNodeIndex}",
                Slot = 8,
                Ip = "192.168.1.3"
            });

            // 额外测量动作：2601(4) slot=4
            const int slot = 4;
            const string ip = "192.168.1.3";
            MatrixRouteAction measureAction = null;
            if (SelectedFrequencyAcquireMeasureOption == "万用表测频率")
            {
                measureAction = new MatrixRouteAction { InNode = "I4", OutNode = "O9", Slot = slot, Ip = ip };
            }
            else if (SelectedFrequencyAcquireMeasureOption == "示波器通道1")
            {
                measureAction = new MatrixRouteAction { InNode = "I0", OutNode = "O10", Slot = slot, Ip = ip };
            }
            else if (SelectedFrequencyAcquireMeasureOption == "示波器通道2")
            {
                measureAction = new MatrixRouteAction { InNode = "I1", OutNode = "O10", Slot = slot, Ip = ip };
            }
            else if (SelectedFrequencyAcquireMeasureOption == "示波器通道3")
            {
                measureAction = new MatrixRouteAction { InNode = "I2", OutNode = "O10", Slot = slot, Ip = ip };
            }
            else if (SelectedFrequencyAcquireMeasureOption == "示波器通道4")
            {
                measureAction = new MatrixRouteAction { InNode = "I3", OutNode = "O10", Slot = slot, Ip = ip };
            }

            if (measureAction != null)
            {
                r.Actions.Add(measureAction);
            }

            return r;
        }

        private Route BuildFrequencyOutput1Route()
        {
            if (string.IsNullOrEmpty(SelectedFrequencyOutput1MeasureOption))
            {
                throw new InvalidOperationException("未选择1路频率输出测量选项");
            }

            const int slot = 4;
            const string ip = "192.168.1.3";

            MatrixRouteAction action;
            if (SelectedFrequencyOutput1MeasureOption == "万用表")
            {
                action = new MatrixRouteAction { InNode = "I4", OutNode = "O9", Slot = slot, Ip = ip };
            }
            else if (SelectedFrequencyOutput1MeasureOption == "示波器通道1")
            {
                action = new MatrixRouteAction { InNode = "I0", OutNode = "O10", Slot = slot, Ip = ip };
            }
            else if (SelectedFrequencyOutput1MeasureOption == "示波器通道2")
            {
                action = new MatrixRouteAction { InNode = "I1", OutNode = "O10", Slot = slot, Ip = ip };
            }
            else if (SelectedFrequencyOutput1MeasureOption == "示波器通道3")
            {
                action = new MatrixRouteAction { InNode = "I2", OutNode = "O10", Slot = slot, Ip = ip };
            }
            else if (SelectedFrequencyOutput1MeasureOption == "示波器通道4")
            {
                action = new MatrixRouteAction { InNode = "I3", OutNode = "O10", Slot = slot, Ip = ip };
            }
            else
            {
                throw new InvalidOperationException($"1路频率输出测量选项无效: {SelectedFrequencyOutput1MeasureOption}");
            }

            var r = new Route
            {
                Id = "FrequencyOutput1",
                DisplayName = "1路频率输出"
            };

            r.Actions.Add(action);
            return r;
        }

        private static MatrixRouteAction BuildDiscreteLoopbackMeasureAction(string option)
        {
            if (string.IsNullOrEmpty(option))
            {
                return null;
            }

            const int slot = 4;
            const string ip = "192.168.1.3";

            if (option == "万用表")
            {
                return new MatrixRouteAction { InNode = "I4", OutNode = "O0", Slot = slot, Ip = ip };
            }

            string inNode;
            if (option == "示波器通道1")
            {
                inNode = "I0";
            }
            else if (option == "示波器通道2")
            {
                inNode = "I1";
            }
            else if (option == "示波器通道3")
            {
                inNode = "I2";
            }
            else if (option == "示波器通道4")
            {
                inNode = "I3";
            }
            else
            {
                return null;
            }

            return new MatrixRouteAction { InNode = inNode, OutNode = "O1", Slot = slot, Ip = ip };
        }

        private static MatrixRouteAction BuildHighSpeedIoLoopbackMeasureAction(string option)
        {
            if (string.IsNullOrEmpty(option))
            {
                return null;
            }

            const int slot = 4;
            const string ip = "192.168.1.3";

            if (option == "万用表")
            {
                return new MatrixRouteAction { InNode = "I4", OutNode = "O11", Slot = slot, Ip = ip };
            }

            if (option == "示波器通道1")
            {
                return new MatrixRouteAction { InNode = "I0", OutNode = "O12", Slot = slot, Ip = ip };
            }

            if (option == "示波器通道2")
            {
                return new MatrixRouteAction { InNode = "I1", OutNode = "O12", Slot = slot, Ip = ip };
            }

            if (option == "示波器通道3")
            {
                return new MatrixRouteAction { InNode = "I2", OutNode = "O12", Slot = slot, Ip = ip };
            }

            if (option == "示波器通道4")
            {
                return new MatrixRouteAction { InNode = "I3", OutNode = "O12", Slot = slot, Ip = ip };
            }

            return null;
        }

        private static MatrixRouteAction BuildAdAcquireMeasureAction(string option)
        {
            if (string.IsNullOrEmpty(option))
            {
                return null;
            }

            const int slot = 4;
            const string ip = "192.168.1.3";

            if (option == "万用表")
            {
                return new MatrixRouteAction { InNode = "I4", OutNode = "O7", Slot = slot, Ip = ip };
            }

            if (option == "示波器通道1")
            {
                return new MatrixRouteAction { InNode = "I0", OutNode = "O8", Slot = slot, Ip = ip };
            }

            if (option == "示波器通道2")
            {
                return new MatrixRouteAction { InNode = "I1", OutNode = "O8", Slot = slot, Ip = ip };
            }

            if (option == "示波器通道3")
            {
                return new MatrixRouteAction { InNode = "I2", OutNode = "O8", Slot = slot, Ip = ip };
            }

            if (option == "示波器通道4")
            {
                return new MatrixRouteAction { InNode = "I3", OutNode = "O8", Slot = slot, Ip = ip };
            }

            return null;
        }

        private static string BuildRvdtLvdtFirstInNode(string option)
        {
            if (string.IsNullOrEmpty(option))
            {
                return null;
            }

            if (option == "万用表")
            {
                return "I3";
            }

            if (option == "示波器通道1")
            {
                return "I0";
            }

            if (option == "示波器通道2")
            {
                return "I1";
            }

            if (option == "示波器通道3")
            {
                return "I2";
            }

            return null;
        }

        private static MatrixRouteAction BuildRvdtLvdtSecondAction(string option)
        {
            if (string.IsNullOrEmpty(option))
            {
                return null;
            }

            const int slot = 4;
            const string ip = "192.168.1.3";

            if (option == "示波器通道1")
            {
                return new MatrixRouteAction { InNode = "I0", OutNode = "O3", Slot = slot, Ip = ip };
            }

            if (option == "示波器通道2")
            {
                return new MatrixRouteAction { InNode = "I1", OutNode = "O4", Slot = slot, Ip = ip };
            }

            if (option == "示波器通道3")
            {
                return new MatrixRouteAction { InNode = "I2", OutNode = "O5", Slot = slot, Ip = ip };
            }

            if (option == "万用表")
            {
                return new MatrixRouteAction { InNode = "I4", OutNode = "O6", Slot = slot, Ip = ip };
            }

            return null;
        }

        private async Task<bool> ConnectDiscreteInputLoopback32Async()
        {
            if (!TryParseRouteIndex(SelectedDiscreteInputLoopbackRoute, "离散输入回采", out var index))
            {
                DebugLog($"ConnectDiscreteInputLoopback32: 无效的 SelectedDiscreteInputLoopbackRoute='{SelectedDiscreteInputLoopbackRoute}'");
                return false;
            }

            Route route;
            try
            {
                route = BuildDiscreteInputLoopbackRoute(index);
            }
            catch (Exception ex)
            {
                DebugLog($"ConnectDiscreteInputLoopback32: {ex.Message}");
                return false;
            }

            var svc = MatrixControlService.Instance;
            var completed = new List<MatrixRouteAction>();

            DebugLog($"开始执行 ConnectDiscreteInputLoopback32 '{SelectedDiscreteInputLoopbackRoute}': Actions={route.Actions.Count}");

            try
            {
                foreach (var a in route.Actions)
                {
                    DebugLog($"尝试连接 {a.InNode}->{a.OutNode} (slot={a.Slot}, ip={a.Ip})");
                    bool ok = await svc.ConnectNodesAsync(a.InNode, a.OutNode, a.Slot, a.Ip, a.TcpBasePort);
                    DebugLog($"ConnectNodesAsync 返回: {ok} for {a.InNode}->{a.OutNode} (slot={a.Slot})");
                    if (!ok) throw new Exception($"连接失败 {a.InNode}->{a.OutNode} (slot={a.Slot})");
                    completed.Add(a);
                }

                DebugLog("ConnectDiscreteInputLoopback32 完成: 所有动作成功");
                SetRouteConnectedState(BuildRouteStateKey("DiscreteInput", SelectedDiscreteInputLoopbackRoute, SelectedDiscreteInputLoopbackMeasureOption), true);
                return true;
            }
            catch (Exception ex)
            {
                DebugLog($"ConnectDiscreteInputLoopback32 发生异常: {ex.Message}. 开始回滚已完成动作 count={completed.Count}");
                foreach (var a in completed.AsEnumerable().Reverse())
                {
                    try
                    {
                        DebugLog($"回滚: 断开 {a.InNode}->{a.OutNode} (slot={a.Slot})");
                        var dOk = await MatrixControlService.Instance.DisconnectNodesAsync(a.InNode, a.OutNode, a.Slot, a.Ip, a.TcpBasePort);
                        DebugLog($"回滚结果: {dOk} for {a.InNode}->{a.OutNode} (slot={a.Slot})");
                    }
                    catch (Exception rex)
                    {
                        DebugLog($"回滚时异常: {rex.Message}");
                    }
                }

                return false;
            }
        }

        private async Task<bool> DisconnectDiscreteInputLoopback32Async()
        {
            if (!TryParseRouteIndex(SelectedDiscreteInputLoopbackRoute, "离散输入回采", out var index))
            {
                DebugLog($"DisconnectDiscreteInputLoopback32: 无效的 SelectedDiscreteInputLoopbackRoute='{SelectedDiscreteInputLoopbackRoute}'");
                return false;
            }

            Route route;
            try
            {
                route = BuildDiscreteInputLoopbackRoute(index);
            }
            catch (Exception ex)
            {
                DebugLog($"DisconnectDiscreteInputLoopback32: {ex.Message}");
                return true;
            }

            bool anyFailed = false;
            DebugLog($"开始执行 DisconnectDiscreteInputLoopback32: Actions={route.Actions.Count}");
            foreach (var a in route.Actions.AsEnumerable().Reverse())
            {
                try
                {
                    DebugLog($"尝试断开 {a.InNode}->{a.OutNode} (slot={a.Slot}, ip={a.Ip})");
                    var ok = await MatrixControlService.Instance.DisconnectNodesAsync(a.InNode, a.OutNode, a.Slot, a.Ip, a.TcpBasePort);
                    DebugLog($"DisconnectNodesAsync 返回: {ok} for {a.InNode}->{a.OutNode} (slot={a.Slot})");
                    if (!ok) anyFailed = true;
                }
                catch (Exception ex)
                {
                    DebugLog($"断开时异常: {ex.Message}");
                    anyFailed = true;
                }
            }
            SetRouteConnectedState(BuildRouteStateKey("DiscreteInput", SelectedDiscreteInputLoopbackRoute, SelectedDiscreteInputLoopbackMeasureOption), anyFailed);
            return !anyFailed;
        }

        private async Task<bool> ConnectDiscreteOutputLoopback32Async()
        {
            if (!TryParseRouteIndex(SelectedDiscreteOutputLoopbackRoute, "离散输出回采", out var index))
            {
                DebugLog($"ConnectDiscreteOutputLoopback32: 无效的 SelectedDiscreteOutputLoopbackRoute='{SelectedDiscreteOutputLoopbackRoute}'");
                return false;
            }

            Route route;
            try
            {
                route = BuildDiscreteOutputLoopbackRoute(index);
            }
            catch (Exception ex)
            {
                DebugLog($"ConnectDiscreteOutputLoopback32: {ex.Message}");
                return false;
            }

            var svc = MatrixControlService.Instance;
            var completed = new List<MatrixRouteAction>();

            DebugLog($"开始执行 ConnectDiscreteOutputLoopback32 '{SelectedDiscreteOutputLoopbackRoute}': Actions={route.Actions.Count}");

            try
            {
                foreach (var a in route.Actions)
                {
                    DebugLog($"尝试连接 {a.InNode}->{a.OutNode} (slot={a.Slot}, ip={a.Ip})");
                    bool ok = await svc.ConnectNodesAsync(a.InNode, a.OutNode, a.Slot, a.Ip, a.TcpBasePort);
                    DebugLog($"ConnectNodesAsync 返回: {ok} for {a.InNode}->{a.OutNode} (slot={a.Slot})");
                    if (!ok) throw new Exception($"连接失败 {a.InNode}->{a.OutNode} (slot={a.Slot})");
                    completed.Add(a);
                }

                DebugLog("ConnectDiscreteOutputLoopback32 完成: 所有动作成功");
                SetRouteConnectedState(BuildRouteStateKey("DiscreteOutput", SelectedDiscreteOutputLoopbackRoute, SelectedDiscreteOutputLoopbackMeasureOption), true);
                return true;
            }
            catch (Exception ex)
            {
                DebugLog($"ConnectDiscreteOutputLoopback32 发生异常: {ex.Message}. 开始回滚已完成动作 count={completed.Count}");
                foreach (var a in completed.AsEnumerable().Reverse())
                {
                    try
                    {
                        DebugLog($"回滚: 断开 {a.InNode}->{a.OutNode} (slot={a.Slot})");
                        var dOk = await MatrixControlService.Instance.DisconnectNodesAsync(a.InNode, a.OutNode, a.Slot, a.Ip, a.TcpBasePort);
                        DebugLog($"回滚结果: {dOk} for {a.InNode}->{a.OutNode} (slot={a.Slot})");
                    }
                    catch (Exception rex)
                    {
                        DebugLog($"回滚时异常: {rex.Message}");
                    }
                }

                return false;
            }
        }

        private async Task<bool> DisconnectDiscreteOutputLoopback32Async()
        {
            if (!TryParseRouteIndex(SelectedDiscreteOutputLoopbackRoute, "离散输出回采", out var index))
            {
                DebugLog($"DisconnectDiscreteOutputLoopback32: 无效的 SelectedDiscreteOutputLoopbackRoute='{SelectedDiscreteOutputLoopbackRoute}'");
                return false;
            }

            Route route;
            try
            {
                route = BuildDiscreteOutputLoopbackRoute(index);
            }
            catch (Exception ex)
            {
                DebugLog($"DisconnectDiscreteOutputLoopback32: {ex.Message}");
                return true;
            }

            bool anyFailed = false;
            DebugLog($"开始执行 DisconnectDiscreteOutputLoopback32: Actions={route.Actions.Count}");
            foreach (var a in route.Actions.AsEnumerable().Reverse())
            {
                try
                {
                    DebugLog($"尝试断开 {a.InNode}->{a.OutNode} (slot={a.Slot}, ip={a.Ip})");
                    var ok = await MatrixControlService.Instance.DisconnectNodesAsync(a.InNode, a.OutNode, a.Slot, a.Ip, a.TcpBasePort);
                    DebugLog($"DisconnectNodesAsync 返回: {ok} for {a.InNode}->{a.OutNode} (slot={a.Slot})");
                    if (!ok) anyFailed = true;
                }
                catch (Exception ex)
                {
                    DebugLog($"断开时异常: {ex.Message}");
                    anyFailed = true;
                }
            }
            SetRouteConnectedState(BuildRouteStateKey("DiscreteOutput", SelectedDiscreteOutputLoopbackRoute, SelectedDiscreteOutputLoopbackMeasureOption), anyFailed);
            return !anyFailed;
        }

        private async Task<bool> ConnectHighSpeedIoLoopback64Async()
        {
            if (!TryParseRouteIndex(SelectedHighSpeedIoLoopbackRoute, "高速IO回采", out var index))
            {
                DebugLog($"ConnectHighSpeedIoLoopback64Async: 无效的 SelectedHighSpeedIoLoopbackRoute='{SelectedHighSpeedIoLoopbackRoute}'");
                return false;
            }

            Route route;
            try
            {
                route = BuildHighSpeedIoLoopbackRoute(index);
            }
            catch (Exception ex)
            {
                DebugLog($"ConnectHighSpeedIoLoopback64Async: {ex.Message}");
                return false;
            }

            var svc = MatrixControlService.Instance;
            var completed = new List<MatrixRouteAction>();

            DebugLog($"开始执行 ConnectHighSpeedIoLoopback64Async '{SelectedHighSpeedIoLoopbackRoute}': Actions={route.Actions.Count}");

            try
            {
                foreach (var a in route.Actions)
                {
                    DebugLog($"尝试连接 {a.InNode}->{a.OutNode} (slot={a.Slot}, ip={a.Ip})");
                    bool ok = await svc.ConnectNodesAsync(a.InNode, a.OutNode, a.Slot, a.Ip, a.TcpBasePort);
                    DebugLog($"ConnectNodesAsync 返回: {ok} for {a.InNode}->{a.OutNode} (slot={a.Slot})");
                    if (!ok) throw new Exception($"连接失败 {a.InNode}->{a.OutNode} (slot={a.Slot})");
                    completed.Add(a);
                }

                DebugLog("ConnectHighSpeedIoLoopback64Async 完成: 所有动作成功");
                SetRouteConnectedState(BuildRouteStateKey("HighSpeedIo", SelectedHighSpeedIoLoopbackRoute, SelectedHighSpeedIoLoopbackMeasureOption), true);
                return true;
            }
            catch (Exception ex)
            {
                DebugLog($"ConnectHighSpeedIoLoopback64Async 发生异常: {ex.Message}. 开始回滚已完成动作 count={completed.Count}");
                foreach (var a in completed.AsEnumerable().Reverse())
                {
                    try
                    {
                        DebugLog($"回滚: 断开 {a.InNode}->{a.OutNode} (slot={a.Slot})");
                        var dOk = await MatrixControlService.Instance.DisconnectNodesAsync(a.InNode, a.OutNode, a.Slot, a.Ip, a.TcpBasePort);
                        DebugLog($"回滚结果: {dOk} for {a.InNode}->{a.OutNode} (slot={a.Slot})");
                    }
                    catch (Exception rex)
                    {
                        DebugLog($"回滚时异常: {rex.Message}");
                    }
                }

                return false;
            }
        }

        private async Task<bool> DisconnectHighSpeedIoLoopback64Async()
        {
            if (!TryParseRouteIndex(SelectedHighSpeedIoLoopbackRoute, "高速IO回采", out var index))
            {
                DebugLog($"DisconnectHighSpeedIoLoopback64Async: 无效的 SelectedHighSpeedIoLoopbackRoute='{SelectedHighSpeedIoLoopbackRoute}'");
                return false;
            }

            Route route;
            try
            {
                route = BuildHighSpeedIoLoopbackRoute(index);
            }
            catch (Exception ex)
            {
                DebugLog($"DisconnectHighSpeedIoLoopback64Async: {ex.Message}");
                return true;
            }

            bool anyFailed = false;
            DebugLog($"开始执行 DisconnectHighSpeedIoLoopback64Async: Actions={route.Actions.Count}");
            foreach (var a in route.Actions.AsEnumerable().Reverse())
            {
                try
                {
                    DebugLog($"尝试断开 {a.InNode}->{a.OutNode} (slot={a.Slot}, ip={a.Ip})");
                    var ok = await MatrixControlService.Instance.DisconnectNodesAsync(a.InNode, a.OutNode, a.Slot, a.Ip, a.TcpBasePort);
                    DebugLog($"DisconnectNodesAsync 返回: {ok} for {a.InNode}->{a.OutNode} (slot={a.Slot})");
                    if (!ok) anyFailed = true;
                }
                catch (Exception ex)
                {
                    DebugLog($"断开时异常: {ex.Message}");
                    anyFailed = true;
                }
            }
            SetRouteConnectedState(BuildRouteStateKey("HighSpeedIo", SelectedHighSpeedIoLoopbackRoute, SelectedHighSpeedIoLoopbackMeasureOption), anyFailed);
            return !anyFailed;
        }

        private async Task<bool> ConnectFrequencyAcquireAsync()
        {
            if (!TryParseRouteIndex(SelectedFrequencyAcquireRoute, "频率采集", out var index))
            {
                DebugLog($"ConnectFrequencyAcquireAsync: 无效的 SelectedFrequencyAcquireRoute='{SelectedFrequencyAcquireRoute}'");
                return false;
            }

            Route route;
            try
            {
                route = BuildFrequencyAcquireRoute(index);
            }
            catch (Exception ex)
            {
                DebugLog($"ConnectFrequencyAcquireAsync: {ex.Message}");
                return false;
            }

            var svc = MatrixControlService.Instance;
            var completed = new List<MatrixRouteAction>();
            DebugLog($"开始执行 ConnectFrequencyAcquireAsync '{SelectedFrequencyAcquireRoute}': Actions={route.Actions.Count}");

            try
            {
                foreach (var a in route.Actions)
                {
                    DebugLog($"尝试连接 {a.InNode}->{a.OutNode} (slot={a.Slot}, ip={a.Ip})");
                    bool ok = await svc.ConnectNodesAsync(a.InNode, a.OutNode, a.Slot, a.Ip, a.TcpBasePort);
                    DebugLog($"ConnectNodesAsync 返回: {ok} for {a.InNode}->{a.OutNode} (slot={a.Slot})");
                    if (!ok) throw new Exception($"连接失败 {a.InNode}->{a.OutNode} (slot={a.Slot})");
                    completed.Add(a);
                }

                DebugLog("ConnectFrequencyAcquireAsync 完成: 所有动作成功");
                SetRouteConnectedState(BuildRouteStateKey("FrequencyAcquire", SelectedFrequencyAcquireRoute, SelectedFrequencyAcquireMeasureOption), true);
                return true;
            }
            catch (Exception ex)
            {
                DebugLog($"ConnectFrequencyAcquireAsync 发生异常: {ex.Message}. 开始回滚已完成动作 count={completed.Count}");
                foreach (var a in completed.AsEnumerable().Reverse())
                {
                    try
                    {
                        DebugLog($"回滚: 断开 {a.InNode}->{a.OutNode} (slot={a.Slot})");
                        var dOk = await svc.DisconnectNodesAsync(a.InNode, a.OutNode, a.Slot, a.Ip, a.TcpBasePort);
                        DebugLog($"回滚结果: {dOk} for {a.InNode}->{a.OutNode} (slot={a.Slot})");
                    }
                    catch (Exception rex)
                    {
                        DebugLog($"回滚时异常: {rex.Message}");
                    }
                }

                return false;
            }
        }

        private async Task<bool> DisconnectFrequencyAcquireAsync()
        {
            if (!TryParseRouteIndex(SelectedFrequencyAcquireRoute, "频率采集", out var index))
            {
                DebugLog($"DisconnectFrequencyAcquireAsync: 无效的 SelectedFrequencyAcquireRoute='{SelectedFrequencyAcquireRoute}'");
                return false;
            }

            Route route;
            try
            {
                route = BuildFrequencyAcquireRoute(index);
            }
            catch (Exception ex)
            {
                DebugLog($"DisconnectFrequencyAcquireAsync: {ex.Message}");
                return true;
            }

            bool anyFailed = false;
            DebugLog($"开始执行 DisconnectFrequencyAcquireAsync: Actions={route.Actions.Count}");
            foreach (var a in route.Actions.AsEnumerable().Reverse())
            {
                try
                {
                    DebugLog($"尝试断开 {a.InNode}->{a.OutNode} (slot={a.Slot}, ip={a.Ip})");
                    var ok = await MatrixControlService.Instance.DisconnectNodesAsync(a.InNode, a.OutNode, a.Slot, a.Ip, a.TcpBasePort);
                    DebugLog($"DisconnectNodesAsync 返回: {ok} for {a.InNode}->{a.OutNode} (slot={a.Slot})");
                    if (!ok) anyFailed = true;
                }
                catch (Exception ex)
                {
                    DebugLog($"断开时异常: {ex.Message}");
                    anyFailed = true;
                }
            }
            SetRouteConnectedState(BuildRouteStateKey("FrequencyAcquire", SelectedFrequencyAcquireRoute, SelectedFrequencyAcquireMeasureOption), anyFailed);
            return !anyFailed;
        }

        private async Task<bool> ConnectFrequencyOutputAsync()
        {
            if (!TryParseRouteIndex(SelectedFrequencyOutputRoute, "频率输出", out var index))
            {
                DebugLog($"ConnectFrequencyOutputAsync: 无效的 SelectedFrequencyOutputRoute='{SelectedFrequencyOutputRoute}'");
                return false;
            }

            Route route;
            try
            {
                route = BuildFrequencyOutputRoute(index);
            }
            catch (Exception ex)
            {
                DebugLog($"ConnectFrequencyOutputAsync: {ex.Message}");
                return false;
            }

            var svc = MatrixControlService.Instance;
            var completed = new List<MatrixRouteAction>();
            DebugLog($"开始执行 ConnectFrequencyOutputAsync '{SelectedFrequencyOutputRoute}': Actions={route.Actions.Count}");

            try
            {
                foreach (var a in route.Actions)
                {
                    DebugLog($"尝试连接 {a.InNode}->{a.OutNode} (slot={a.Slot}, ip={a.Ip})");
                    bool ok = await svc.ConnectNodesAsync(a.InNode, a.OutNode, a.Slot, a.Ip, a.TcpBasePort);
                    DebugLog($"ConnectNodesAsync 返回: {ok} for {a.InNode}->{a.OutNode} (slot={a.Slot})");
                    if (!ok) throw new Exception($"连接失败 {a.InNode}->{a.OutNode} (slot={a.Slot})");
                    completed.Add(a);
                }

                DebugLog("ConnectFrequencyOutputAsync 完成: 所有动作成功");
                SetRouteConnectedState(BuildRouteStateKey("FrequencyOutput", SelectedFrequencyOutputRoute), true);
                return true;
            }
            catch (Exception ex)
            {
                DebugLog($"ConnectFrequencyOutputAsync 发生异常: {ex.Message}. 开始回滚已完成动作 count={completed.Count}");
                foreach (var a in completed.AsEnumerable().Reverse())
                {
                    try
                    {
                        DebugLog($"回滚: 断开 {a.InNode}->{a.OutNode} (slot={a.Slot})");
                        var dOk = await svc.DisconnectNodesAsync(a.InNode, a.OutNode, a.Slot, a.Ip, a.TcpBasePort);
                        DebugLog($"回滚结果: {dOk} for {a.InNode}->{a.OutNode} (slot={a.Slot})");
                    }
                    catch (Exception rex)
                    {
                        DebugLog($"回滚时异常: {rex.Message}");
                    }
                }

                return false;
            }
        }

        private async Task<bool> DisconnectFrequencyOutputAsync()
        {
            if (!TryParseRouteIndex(SelectedFrequencyOutputRoute, "频率输出", out var index))
            {
                DebugLog($"DisconnectFrequencyOutputAsync: 无效的 SelectedFrequencyOutputRoute='{SelectedFrequencyOutputRoute}'");
                return false;
            }

            Route route;
            try
            {
                route = BuildFrequencyOutputRoute(index);
            }
            catch (Exception ex)
            {
                DebugLog($"DisconnectFrequencyOutputAsync: {ex.Message}");
                return true;
            }

            bool anyFailed = false;
            DebugLog($"开始执行 DisconnectFrequencyOutputAsync: Actions={route.Actions.Count}");
            foreach (var a in route.Actions.AsEnumerable().Reverse())
            {
                try
                {
                    DebugLog($"尝试断开 {a.InNode}->{a.OutNode} (slot={a.Slot}, ip={a.Ip})");
                    var ok = await MatrixControlService.Instance.DisconnectNodesAsync(a.InNode, a.OutNode, a.Slot, a.Ip, a.TcpBasePort);
                    DebugLog($"DisconnectNodesAsync 返回: {ok} for {a.InNode}->{a.OutNode} (slot={a.Slot})");
                    if (!ok) anyFailed = true;
                }
                catch (Exception ex)
                {
                    DebugLog($"断开时异常: {ex.Message}");
                    anyFailed = true;
                }
            }
            SetRouteConnectedState(BuildRouteStateKey("FrequencyOutput", SelectedFrequencyOutputRoute), anyFailed);
            return !anyFailed;
        }

        private async Task<bool> ConnectFrequencyOutput1Async()
        {
            Route route;
            try
            {
                route = BuildFrequencyOutput1Route();
            }
            catch (Exception ex)
            {
                DebugLog($"ConnectFrequencyOutput1Async: {ex.Message}");
                return false;
            }

            var svc = MatrixControlService.Instance;
            var completed = new List<MatrixRouteAction>();
            DebugLog($"开始执行 ConnectFrequencyOutput1Async '{SelectedFrequencyOutput1MeasureOption}': Actions={route.Actions.Count}");

            try
            {
                foreach (var a in route.Actions)
                {
                    DebugLog($"尝试连接 {a.InNode}->{a.OutNode} (slot={a.Slot}, ip={a.Ip})");
                    bool ok = await svc.ConnectNodesAsync(a.InNode, a.OutNode, a.Slot, a.Ip, a.TcpBasePort);
                    DebugLog($"ConnectNodesAsync 返回: {ok} for {a.InNode}->{a.OutNode} (slot={a.Slot})");
                    if (!ok) throw new Exception($"连接失败 {a.InNode}->{a.OutNode} (slot={a.Slot})");
                    completed.Add(a);
                }

                DebugLog("ConnectFrequencyOutput1Async 完成: 所有动作成功");
                SetRouteConnectedState(BuildRouteStateKey("FrequencyOutput1", SelectedFrequencyOutput1MeasureOption), true);
                return true;
            }
            catch (Exception ex)
            {
                DebugLog($"ConnectFrequencyOutput1Async 发生异常: {ex.Message}. 开始回滚已完成动作 count={completed.Count}");
                foreach (var a in completed.AsEnumerable().Reverse())
                {
                    try
                    {
                        DebugLog($"回滚: 断开 {a.InNode}->{a.OutNode} (slot={a.Slot})");
                        var dOk = await svc.DisconnectNodesAsync(a.InNode, a.OutNode, a.Slot, a.Ip, a.TcpBasePort);
                        DebugLog($"回滚结果: {dOk} for {a.InNode}->{a.OutNode} (slot={a.Slot})");
                    }
                    catch (Exception rex)
                    {
                        DebugLog($"回滚时异常: {rex.Message}");
                    }
                }

                return false;
            }
        }

        private async Task<bool> DisconnectFrequencyOutput1Async()
        {
            Route route;
            try
            {
                route = BuildFrequencyOutput1Route();
            }
            catch (Exception ex)
            {
                DebugLog($"DisconnectFrequencyOutput1Async: {ex.Message}");
                return true;
            }

            bool anyFailed = false;
            DebugLog($"开始执行 DisconnectFrequencyOutput1Async: Actions={route.Actions.Count}");
            foreach (var a in route.Actions.AsEnumerable().Reverse())
            {
                try
                {
                    DebugLog($"尝试断开 {a.InNode}->{a.OutNode} (slot={a.Slot}, ip={a.Ip})");
                    var ok = await MatrixControlService.Instance.DisconnectNodesAsync(a.InNode, a.OutNode, a.Slot, a.Ip, a.TcpBasePort);
                    DebugLog($"DisconnectNodesAsync 返回: {ok} for {a.InNode}->{a.OutNode} (slot={a.Slot})");
                    if (!ok) anyFailed = true;
                }
                catch (Exception ex)
                {
                    DebugLog($"断开时异常: {ex.Message}");
                    anyFailed = true;
                }
            }
            SetRouteConnectedState(BuildRouteStateKey("FrequencyOutput1", SelectedFrequencyOutput1MeasureOption), anyFailed);
            return !anyFailed;
        }

        private async Task<bool> ConnectAdAcquire32Async()
        {
            if (!TryParseRouteIndex(SelectedAdAcquireRoute, "AD采集", out var index))
            {
                DebugLog($"ConnectAdAcquire32Async: 无效的 SelectedAdAcquireRoute='{SelectedAdAcquireRoute}'");
                return false;
            }

            Route route;
            try
            {
                route = BuildAdAcquireRoute(index);
            }
            catch (Exception ex)
            {
                DebugLog($"ConnectAdAcquire32Async: {ex.Message}");
                return false;
            }

            var svc = MatrixControlService.Instance;
            var completed = new List<MatrixRouteAction>();
            DebugLog($"开始执行 ConnectAdAcquire32Async '{SelectedAdAcquireRoute}': Actions={route.Actions.Count}");

            try
            {
                foreach (var a in route.Actions)
                {
                    DebugLog($"尝试连接 {a.InNode}->{a.OutNode} (slot={a.Slot}, ip={a.Ip})");
                    bool ok = await svc.ConnectNodesAsync(a.InNode, a.OutNode, a.Slot, a.Ip, a.TcpBasePort);
                    DebugLog($"ConnectNodesAsync 返回: {ok} for {a.InNode}->{a.OutNode} (slot={a.Slot})");
                    if (!ok) throw new Exception($"连接失败 {a.InNode}->{a.OutNode} (slot={a.Slot})");
                    completed.Add(a);
                }

                DebugLog("ConnectAdAcquire32Async 完成: 所有动作成功");
                SetRouteConnectedState(BuildRouteStateKey("AdAcquire", SelectedAdAcquireRoute, SelectedAdAcquireMeasureOption), true);
                return true;
            }
            catch (Exception ex)
            {
                DebugLog($"ConnectAdAcquire32Async 发生异常: {ex.Message}. 开始回滚已完成动作 count={completed.Count}");
                foreach (var a in completed.AsEnumerable().Reverse())
                {
                    try
                    {
                        DebugLog($"回滚: 断开 {a.InNode}->{a.OutNode} (slot={a.Slot})");
                        var dOk = await svc.DisconnectNodesAsync(a.InNode, a.OutNode, a.Slot, a.Ip, a.TcpBasePort);
                        DebugLog($"回滚结果: {dOk} for {a.InNode}->{a.OutNode} (slot={a.Slot})");
                    }
                    catch (Exception rex)
                    {
                        DebugLog($"回滚时异常: {rex.Message}");
                    }
                }

                return false;
            }
        }

        private async Task<bool> DisconnectAdAcquire32Async()
        {
            if (!TryParseRouteIndex(SelectedAdAcquireRoute, "AD采集", out var index))
            {
                DebugLog($"DisconnectAdAcquire32Async: 无效的 SelectedAdAcquireRoute='{SelectedAdAcquireRoute}'");
                return false;
            }

            Route route;
            try
            {
                route = BuildAdAcquireRoute(index);
            }
            catch (Exception ex)
            {
                DebugLog($"DisconnectAdAcquire32Async: {ex.Message}");
                return true;
            }

            bool anyFailed = false;
            DebugLog($"开始执行 DisconnectAdAcquire32Async: Actions={route.Actions.Count}");
            foreach (var a in route.Actions.AsEnumerable().Reverse())
            {
                try
                {
                    DebugLog($"尝试断开 {a.InNode}->{a.OutNode} (slot={a.Slot}, ip={a.Ip})");
                    var ok = await MatrixControlService.Instance.DisconnectNodesAsync(a.InNode, a.OutNode, a.Slot, a.Ip, a.TcpBasePort);
                    DebugLog($"DisconnectNodesAsync 返回: {ok} for {a.InNode}->{a.OutNode} (slot={a.Slot})");
                    if (!ok) anyFailed = true;
                }
                catch (Exception ex)
                {
                    DebugLog($"断开时异常: {ex.Message}");
                    anyFailed = true;
                }
            }
            SetRouteConnectedState(BuildRouteStateKey("AdAcquire", SelectedAdAcquireRoute, SelectedAdAcquireMeasureOption), anyFailed);
            return !anyFailed;
        }

        private async Task<bool> ConnectCurrentLoopbackAsync()
        {
            if (!TryParseRouteIndex(SelectedCurrentLoopbackRoute, "电流回采", out var index))
            {
                DebugLog($"ConnectCurrentLoopbackAsync: 无效的 SelectedCurrentLoopbackRoute='{SelectedCurrentLoopbackRoute}'");
                return false;
            }

            Route route;
            try
            {
                route = BuildCurrentLoopbackRoute(index);
            }
            catch (Exception ex)
            {
                DebugLog($"ConnectCurrentLoopbackAsync: {ex.Message}");
                return false;
            }

            var svc = MatrixControlService.Instance;
            var completed = new List<MatrixRouteAction>();
            DebugLog($"开始执行 ConnectCurrentLoopbackAsync '{SelectedCurrentLoopbackRoute}': Actions={route.Actions.Count}");

            try
            {
                foreach (var a in route.Actions)
                {
                    DebugLog($"尝试连接 {a.InNode}->{a.OutNode} (slot={a.Slot}, ip={a.Ip})");
                    bool ok = await svc.ConnectNodesAsync(a.InNode, a.OutNode, a.Slot, a.Ip, a.TcpBasePort);
                    DebugLog($"ConnectNodesAsync 返回: {ok} for {a.InNode}->{a.OutNode} (slot={a.Slot})");
                    if (!ok) throw new Exception($"连接失败 {a.InNode}->{a.OutNode} (slot={a.Slot})");
                    completed.Add(a);
                }

                DebugLog("ConnectCurrentLoopbackAsync 完成: 所有动作成功");
                SetRouteConnectedState(BuildRouteStateKey("CurrentLoopback", SelectedCurrentLoopbackRoute), true);
                return true;
            }
            catch (Exception ex)
            {
                DebugLog($"ConnectCurrentLoopbackAsync 发生异常: {ex.Message}. 开始回滚已完成动作 count={completed.Count}");
                foreach (var a in completed.AsEnumerable().Reverse())
                {
                    try
                    {
                        DebugLog($"回滚: 断开 {a.InNode}->{a.OutNode} (slot={a.Slot})");
                        var dOk = await svc.DisconnectNodesAsync(a.InNode, a.OutNode, a.Slot, a.Ip, a.TcpBasePort);
                        DebugLog($"回滚结果: {dOk} for {a.InNode}->{a.OutNode} (slot={a.Slot})");
                    }
                    catch (Exception rex)
                    {
                        DebugLog($"回滚时异常: {rex.Message}");
                    }
                }

                return false;
            }
        }

        private async Task<bool> DisconnectCurrentLoopbackAsync()
        {
            if (!TryParseRouteIndex(SelectedCurrentLoopbackRoute, "电流回采", out var index))
            {
                DebugLog($"DisconnectCurrentLoopbackAsync: 无效的 SelectedCurrentLoopbackRoute='{SelectedCurrentLoopbackRoute}'");
                return false;
            }

            Route route;
            try
            {
                route = BuildCurrentLoopbackRoute(index);
            }
            catch (Exception ex)
            {
                DebugLog($"DisconnectCurrentLoopbackAsync: {ex.Message}");
                return true;
            }

            bool anyFailed = false;
            DebugLog($"开始执行 DisconnectCurrentLoopbackAsync: Actions={route.Actions.Count}");
            foreach (var a in route.Actions.AsEnumerable().Reverse())
            {
                try
                {
                    DebugLog($"尝试断开 {a.InNode}->{a.OutNode} (slot={a.Slot}, ip={a.Ip})");
                    var ok = await MatrixControlService.Instance.DisconnectNodesAsync(a.InNode, a.OutNode, a.Slot, a.Ip, a.TcpBasePort);
                    DebugLog($"DisconnectNodesAsync 返回: {ok} for {a.InNode}->{a.OutNode} (slot={a.Slot})");
                    if (!ok) anyFailed = true;
                }
                catch (Exception ex)
                {
                    DebugLog($"断开时异常: {ex.Message}");
                    anyFailed = true;
                }
            }
            SetRouteConnectedState(BuildRouteStateKey("CurrentLoopback", SelectedCurrentLoopbackRoute), anyFailed);
            return !anyFailed;
        }

        private async Task<bool> ConnectResolverLoopbackAsync()
        {
            if (string.IsNullOrEmpty(SelectedResolverLoopbackRoute) || string.IsNullOrEmpty(SelectedResolverLoopbackMeasureOption))
            {
                DebugLog("ConnectResolverLoopbackAsync: SelectedResolverLoopbackRoute或SelectedResolverLoopbackMeasureOption为空");
                return false;
            }

            Route route;
            try
            {
                route = BuildRvdtLoopbackRoute();
            }
            catch (Exception ex)
            {
                DebugLog($"ConnectResolverLoopbackAsync: {ex.Message}");
                return false;
            }

            var svc = MatrixControlService.Instance;
            var completed = new List<MatrixRouteAction>();
            DebugLog($"开始执行 ConnectResolverLoopbackAsync '{SelectedResolverLoopbackRoute}': Actions={route.Actions.Count}");

            try
            {
                foreach (var a in route.Actions)
                {
                    DebugLog($"尝试连接 {a.InNode}->{a.OutNode} (slot={a.Slot}, ip={a.Ip})");
                    bool ok = await svc.ConnectNodesAsync(a.InNode, a.OutNode, a.Slot, a.Ip, a.TcpBasePort);
                    DebugLog($"ConnectNodesAsync 返回: {ok} for {a.InNode}->{a.OutNode} (slot={a.Slot})");
                    if (!ok) throw new Exception($"连接失败 {a.InNode}->{a.OutNode} (slot={a.Slot})");
                    completed.Add(a);
                }

                DebugLog("ConnectResolverLoopbackAsync 完成: 所有动作成功");
                SetRouteConnectedState(BuildRouteStateKey("ResolverLoopback", SelectedResolverLoopbackRoute, SelectedResolverLoopbackMeasureOption), true);
                return true;
            }
            catch (Exception ex)
            {
                DebugLog($"ConnectResolverLoopbackAsync 发生异常: {ex.Message}. 开始回滚已完成动作 count={completed.Count}");
                foreach (var a in completed.AsEnumerable().Reverse())
                {
                    try
                    {
                        DebugLog($"回滚: 断开 {a.InNode}->{a.OutNode} (slot={a.Slot})");
                        var dOk = await svc.DisconnectNodesAsync(a.InNode, a.OutNode, a.Slot, a.Ip, a.TcpBasePort);
                        DebugLog($"回滚结果: {dOk} for {a.InNode}->{a.OutNode} (slot={a.Slot})");
                    }
                    catch (Exception rex)
                    {
                        DebugLog($"回滚时异常: {rex.Message}");
                    }
                }

                return false;
            }
        }

        private async Task<bool> DisconnectResolverLoopbackAsync()
        {
            if (string.IsNullOrEmpty(SelectedResolverLoopbackRoute) || string.IsNullOrEmpty(SelectedResolverLoopbackMeasureOption))
            {
                DebugLog("DisconnectResolverLoopbackAsync: SelectedResolverLoopbackRoute或SelectedResolverLoopbackMeasureOption为空");
                return false;
            }

            Route route;
            try
            {
                route = BuildRvdtLoopbackRoute();
            }
            catch (Exception ex)
            {
                DebugLog($"DisconnectResolverLoopbackAsync: {ex.Message}");
                return true;
            }

            bool anyFailed = false;
            DebugLog($"开始执行 DisconnectResolverLoopbackAsync: Actions={route.Actions.Count}");
            foreach (var a in route.Actions.AsEnumerable().Reverse())
            {
                try
                {
                    DebugLog($"尝试断开 {a.InNode}->{a.OutNode} (slot={a.Slot}, ip={a.Ip})");
                    var ok = await MatrixControlService.Instance.DisconnectNodesAsync(a.InNode, a.OutNode, a.Slot, a.Ip, a.TcpBasePort);
                    DebugLog($"DisconnectNodesAsync 返回: {ok} for {a.InNode}->{a.OutNode} (slot={a.Slot})");
                    if (!ok) anyFailed = true;
                }
                catch (Exception ex)
                {
                    DebugLog($"断开时异常: {ex.Message}");
                    anyFailed = true;
                }
            }
            SetRouteConnectedState(BuildRouteStateKey("ResolverLoopback", SelectedResolverLoopbackRoute, SelectedResolverLoopbackMeasureOption), anyFailed);
            return !anyFailed;
        }

        private async Task<bool> ConnectLvdtLoopbackAsync()
        {
            if (string.IsNullOrEmpty(SelectedLvdtLoopbackRoute) || string.IsNullOrEmpty(SelectedLvdtLoopbackMeasureOption))
            {
                DebugLog("ConnectLvdtLoopbackAsync: SelectedLvdtLoopbackRoute或SelectedLvdtLoopbackMeasureOption为空");
                return false;
            }

            Route route;
            try
            {
                route = BuildLvdtLoopbackRoute();
            }
            catch (Exception ex)
            {
                DebugLog($"ConnectLvdtLoopbackAsync: {ex.Message}");
                return false;
            }

            var svc = MatrixControlService.Instance;
            var completed = new List<MatrixRouteAction>();
            DebugLog($"开始执行 ConnectLvdtLoopbackAsync '{SelectedLvdtLoopbackRoute}': Actions={route.Actions.Count}");

            try
            {
                foreach (var a in route.Actions)
                {
                    DebugLog($"尝试连接 {a.InNode}->{a.OutNode} (slot={a.Slot}, ip={a.Ip})");
                    bool ok = await svc.ConnectNodesAsync(a.InNode, a.OutNode, a.Slot, a.Ip, a.TcpBasePort);
                    DebugLog($"ConnectNodesAsync 返回: {ok} for {a.InNode}->{a.OutNode} (slot={a.Slot})");
                    if (!ok) throw new Exception($"连接失败 {a.InNode}->{a.OutNode} (slot={a.Slot})");
                    completed.Add(a);
                }

                DebugLog("ConnectLvdtLoopbackAsync 完成: 所有动作成功");
                SetRouteConnectedState(BuildRouteStateKey("LvdtLoopback", SelectedLvdtLoopbackRoute, SelectedLvdtLoopbackMeasureOption), true);
                return true;
            }
            catch (Exception ex)
            {
                DebugLog($"ConnectLvdtLoopbackAsync 发生异常: {ex.Message}. 开始回滚已完成动作 count={completed.Count}");
                foreach (var a in completed.AsEnumerable().Reverse())
                {
                    try
                    {
                        DebugLog($"回滚: 断开 {a.InNode}->{a.OutNode} (slot={a.Slot})");
                        var dOk = await svc.DisconnectNodesAsync(a.InNode, a.OutNode, a.Slot, a.Ip, a.TcpBasePort);
                        DebugLog($"回滚结果: {dOk} for {a.InNode}->{a.OutNode} (slot={a.Slot})");
                    }
                    catch (Exception rex)
                    {
                        DebugLog($"回滚时异常: {rex.Message}");
                    }
                }

                return false;
            }
        }

        private async Task<bool> DisconnectLvdtLoopbackAsync()
        {
            if (string.IsNullOrEmpty(SelectedLvdtLoopbackRoute) || string.IsNullOrEmpty(SelectedLvdtLoopbackMeasureOption))
            {
                DebugLog("DisconnectLvdtLoopbackAsync: SelectedLvdtLoopbackRoute或SelectedLvdtLoopbackMeasureOption为空");
                return false;
            }

            Route route;
            try
            {
                route = BuildLvdtLoopbackRoute();
            }
            catch (Exception ex)
            {
                DebugLog($"DisconnectLvdtLoopbackAsync: {ex.Message}");
                return true;
            }

            bool anyFailed = false;
            DebugLog($"开始执行 DisconnectLvdtLoopbackAsync: Actions={route.Actions.Count}");
            foreach (var a in route.Actions.AsEnumerable().Reverse())
            {
                try
                {
                    DebugLog($"尝试断开 {a.InNode}->{a.OutNode} (slot={a.Slot}, ip={a.Ip})");
                    var ok = await MatrixControlService.Instance.DisconnectNodesAsync(a.InNode, a.OutNode, a.Slot, a.Ip, a.TcpBasePort);
                    DebugLog($"DisconnectNodesAsync 返回: {ok} for {a.InNode}->{a.OutNode} (slot={a.Slot})");
                    if (!ok) anyFailed = true;
                }
                catch (Exception ex)
                {
                    DebugLog($"断开时异常: {ex.Message}");
                    anyFailed = true;
                }
            }
            SetRouteConnectedState(BuildRouteStateKey("LvdtLoopback", SelectedLvdtLoopbackRoute, SelectedLvdtLoopbackMeasureOption), anyFailed);
            return !anyFailed;
        }

        private async void OnToggleHighSpeedIoLoopback(object parameter)
        {
            if (_isTogglingHighSpeedIoLoopback)
            {
                return;
            }

            _isTogglingHighSpeedIoLoopback = true;
            try
            {
                bool desired = parameter is bool b ? b : parameter == null ? false : IsHighSpeedIoLoopbackConnected;
                if (desired == IsHighSpeedIoLoopbackConnected) return;

                if (desired)
                {
                    var ok = await ConnectHighSpeedIoLoopback64Async();
                    IsHighSpeedIoLoopbackConnected = ok;
                }
                else
                {
                    var ok = await DisconnectHighSpeedIoLoopback64Async();
                    IsHighSpeedIoLoopbackConnected = ok ? false : true;
                }
            }
            catch (Exception ex)
            {
                DebugLog($"OnToggleHighSpeedIoLoopback exception: {ex.Message}");
                IsHighSpeedIoLoopbackConnected = false;
            }
            finally
            {
                _isTogglingHighSpeedIoLoopback = false;
            }
        }

        private async void OnToggleDiscreteInputLoopback(object parameter)
        {
            if (_isTogglingDiscreteInputLoopback)
            {
                return;
            }

            _isTogglingDiscreteInputLoopback = true;
            try
            {
                bool desired = parameter is bool b ? b : parameter == null ? false : IsDiscreteInputLoopbackConnected;
                if (desired == IsDiscreteInputLoopbackConnected) return;

                if (desired)
                {
                    var ok = await ConnectDiscreteInputLoopback32Async();
                    IsDiscreteInputLoopbackConnected = ok;
                }
                else
                {
                    var ok = await DisconnectDiscreteInputLoopback32Async();
                    IsDiscreteInputLoopbackConnected = ok ? false : true;
                }
            }
            catch (Exception ex)
            {
                DebugLog($"OnToggleDiscreteInputLoopback exception: {ex.Message}");
                IsDiscreteInputLoopbackConnected = false;
            }
            finally
            {
                _isTogglingDiscreteInputLoopback = false;
            }
        }

        private async void OnToggleDiscreteOutputLoopback(object parameter)
        {
            if (_isTogglingDiscreteOutputLoopback)
            {
                return;
            }

            _isTogglingDiscreteOutputLoopback = true;
            try
            {
                bool desired = parameter is bool b ? b : parameter == null ? false : IsDiscreteOutputLoopbackConnected;
                if (desired == IsDiscreteOutputLoopbackConnected) return;

                if (desired)
                {
                    var ok = await ConnectDiscreteOutputLoopback32Async();
                    IsDiscreteOutputLoopbackConnected = ok;
                }
                else
                {
                    var ok = await DisconnectDiscreteOutputLoopback32Async();
                    IsDiscreteOutputLoopbackConnected = ok ? false : true;
                }
            }
            catch (Exception ex)
            {
                DebugLog($"OnToggleDiscreteOutputLoopback exception: {ex.Message}");
                IsDiscreteOutputLoopbackConnected = false;
            }
            finally
            {
                _isTogglingDiscreteOutputLoopback = false;
            }
        }

        private async void OnToggleCurrentLoopback(object parameter)
        {
            if (_isTogglingCurrentLoopback)
            {
                return;
            }

            _isTogglingCurrentLoopback = true;
            try
            {
                bool desired;
                if (parameter is bool b)
                {
                    desired = b;
                }
                else if (parameter == null)
                {
                    desired = false;
                }
                else
                {
                    desired = IsCurrentLoopbackConnected;
                }

                if (desired == IsCurrentLoopbackConnected)
                {
                    return;
                }

                if (desired)
                {
                    var ok = await ConnectCurrentLoopbackAsync();
                    IsCurrentLoopbackConnected = ok;
                }
                else
                {
                    var ok = await DisconnectCurrentLoopbackAsync();
                    IsCurrentLoopbackConnected = ok ? false : true;
                }
            }
            catch (Exception ex)
            {
                DebugLog($"OnToggleCurrentLoopback exception: {ex.Message}");
                IsCurrentLoopbackConnected = false;
            }
            finally
            {
                _isTogglingCurrentLoopback = false;
            }
        }

        private async void OnToggleResolverLoopback(object parameter)
        {
            if (_isTogglingResolverLoopback)
            {
                return;
            }

            _isTogglingResolverLoopback = true;
            try
            {
                bool desired;
                if (parameter is bool b)
                {
                    desired = b;
                }
                else if (parameter == null)
                {
                    desired = false;
                }
                else
                {
                    desired = IsResolverLoopbackConnected;
                }

                if (desired == IsResolverLoopbackConnected)
                {
                    return;
                }

                if (desired)
                {
                    var ok = await ConnectResolverLoopbackAsync();
                    IsResolverLoopbackConnected = ok;
                }
                else
                {
                    var ok = await DisconnectResolverLoopbackAsync();
                    IsResolverLoopbackConnected = ok ? false : true;
                }
            }
            catch (Exception ex)
            {
                DebugLog($"OnToggleResolverLoopback exception: {ex.Message}");
                IsResolverLoopbackConnected = false;
            }
            finally
            {
                _isTogglingResolverLoopback = false;
            }
        }

        private async void OnToggleLvdtLoopback(object parameter)
        {
            if (_isTogglingLvdtLoopback)
            {
                return;
            }

            _isTogglingLvdtLoopback = true;
            try
            {
                bool desired;
                if (parameter is bool b)
                {
                    desired = b;
                }
                else if (parameter == null)
                {
                    desired = false;
                }
                else
                {
                    desired = IsLvdtLoopbackConnected;
                }

                if (desired == IsLvdtLoopbackConnected)
                {
                    return;
                }

                if (desired)
                {
                    var ok = await ConnectLvdtLoopbackAsync();
                    IsLvdtLoopbackConnected = ok;
                }
                else
                {
                    var ok = await DisconnectLvdtLoopbackAsync();
                    IsLvdtLoopbackConnected = ok ? false : true;
                }
            }
            catch (Exception ex)
            {
                DebugLog($"OnToggleLvdtLoopback exception: {ex.Message}");
                IsLvdtLoopbackConnected = false;
            }
            finally
            {
                _isTogglingLvdtLoopback = false;
            }
        }

        private async void OnToggleFrequencyAcquire(object parameter)
        {
            if (_isTogglingFrequencyAcquire)
            {
                return;
            }

            _isTogglingFrequencyAcquire = true;
            try
            {
                bool desired;
                if (parameter is bool b)
                {
                    desired = b;
                }
                else if (parameter == null)
                {
                    desired = false;
                }
                else
                {
                    desired = IsFrequencyAcquireConnected;
                }

                if (desired == IsFrequencyAcquireConnected)
                {
                    return;
                }

                if (desired)
                {
                    var ok = await ConnectFrequencyAcquireAsync();
                    IsFrequencyAcquireConnected = ok;
                }
                else
                {
                    var ok = await DisconnectFrequencyAcquireAsync();
                    IsFrequencyAcquireConnected = ok ? false : true;
                }
            }
            catch (Exception ex)
            {
                DebugLog($"OnToggleFrequencyAcquire exception: {ex.Message}");
                IsFrequencyAcquireConnected = false;
            }
            finally
            {
                _isTogglingFrequencyAcquire = false;
            }
        }

        private async void OnToggleFrequencyOutput(object parameter)
        {
            if (_isTogglingFrequencyOutput)
            {
                return;
            }

            _isTogglingFrequencyOutput = true;
            try
            {
                bool desired;
                if (parameter is bool b)
                {
                    desired = b;
                }
                else if (parameter == null)
                {
                    desired = false;
                }
                else
                {
                    desired = IsFrequencyOutputConnected;
                }

                if (desired == IsFrequencyOutputConnected)
                {
                    return;
                }

                if (desired)
                {
                    var ok = await ConnectFrequencyOutputAsync();
                    IsFrequencyOutputConnected = ok;
                }
                else
                {
                    var ok = await DisconnectFrequencyOutputAsync();
                    IsFrequencyOutputConnected = ok ? false : true;
                }
            }
            catch (Exception ex)
            {
                DebugLog($"OnToggleFrequencyOutput exception: {ex.Message}");
                IsFrequencyOutputConnected = false;
            }
            finally
            {
                _isTogglingFrequencyOutput = false;
            }
        }

        private async void OnToggleFrequencyOutput1(object parameter)
        {
            if (_isTogglingFrequencyOutput1)
            {
                return;
            }

            _isTogglingFrequencyOutput1 = true;
            try
            {
                bool desired;
                if (parameter is bool b)
                {
                    desired = b;
                }
                else if (parameter == null)
                {
                    desired = false;
                }
                else
                {
                    desired = IsFrequencyOutput1Connected;
                }

                if (desired == IsFrequencyOutput1Connected)
                {
                    return;
                }

                if (desired)
                {
                    var ok = await ConnectFrequencyOutput1Async();
                    IsFrequencyOutput1Connected = ok;
                }
                else
                {
                    var ok = await DisconnectFrequencyOutput1Async();
                    IsFrequencyOutput1Connected = ok ? false : true;
                }
            }
            catch (Exception ex)
            {
                DebugLog($"OnToggleFrequencyOutput1 exception: {ex.Message}");
                IsFrequencyOutput1Connected = false;
            }
            finally
            {
                _isTogglingFrequencyOutput1 = false;
            }
        }

        private async void OnToggleAdAcquire(object parameter)
        {
            if (_isTogglingAdAcquire)
            {
                return;
            }

            _isTogglingAdAcquire = true;
            try
            {
                bool desired;
                if (parameter is bool b)
                {
                    desired = b;
                }
                else if (parameter == null)
                {
                    desired = false;
                }
                else
                {
                    desired = IsAdAcquireConnected;
                }

                if (desired == IsAdAcquireConnected)
                {
                    return;
                }

                if (desired)
                {
                    var ok = await ConnectAdAcquire32Async();
                    IsAdAcquireConnected = ok;
                }
                else
                {
                    var ok = await DisconnectAdAcquire32Async();
                    IsAdAcquireConnected = ok ? false : true;
                }
            }
            catch (Exception ex)
            {
                DebugLog($"OnToggleAdAcquire exception: {ex.Message}");
                IsAdAcquireConnected = false;
            }
            finally
            {
                _isTogglingAdAcquire = false;
            }
        }

        private async void OnToggleSelectedResistance(object parameter)
        {
            if (_isTogglingSelectedResistance)
            {
                return;
            }

            _isTogglingSelectedResistance = true;
            try
            {
                if (string.IsNullOrEmpty(SelectedResistanceRoute))
                {
                    IsSelectedResistanceConnected = false;
                    return;
                }

                bool desired;
                if (parameter is bool b)
                {
                    desired = b;
                }
                else if (parameter == null)
                {
                    desired = false;
                }
                else
                {
                    desired = IsSelectedResistanceConnected;
                }

                if (desired == IsSelectedResistanceConnected)
                {
                    return;
                }

                if (desired)
                {
                    var ok = await ConnectSelectedResistanceAsync();
                    IsSelectedResistanceConnected = ok;
                }
                else
                {
                    var ok = await DisconnectSelectedResistanceAsync();
                    IsSelectedResistanceConnected = ok ? false : true;
                }
            }
            catch (Exception ex)
            {
                DebugLog($"OnToggleSelectedResistance exception: {ex.Message}");
                IsSelectedResistanceConnected = false;
            }
            finally
            {
                _isTogglingSelectedResistance = false;
            }
        }

        #endregion

        #region Navigation

        public bool IsNavigationTarget(NavigationContext navigationContext)
        {
            // 始终重新加载数据
            DebugLog($"IsNavigationTarget 被调用，返回true");
            return true;
        }

        public void OnNavigatedFrom(NavigationContext navigationContext)
        {
            DebugLog($"OnNavigatedFrom 被调用");
            SaveMatrixSwitchesToMemory();
            SaveRuntimeStateToMemory();
        }

        public void OnNavigatedTo(NavigationContext navigationContext)
        {
            DebugLog($"OnNavigatedTo 开始");
            DebugLog($"导航参数: {string.Join(", ", navigationContext.Parameters.Keys.Select(k => $"{k}={navigationContext.Parameters[k]}"))}");

            // 从导航参数中获取测试任务名和配置表名
            TestTaskName = navigationContext.Parameters.TryGetValue<string>("TestTaskName", out var testTaskNameParam) ? testTaskNameParam : null;
            ConfigTableName = navigationContext.Parameters.TryGetValue<string>("ConfigTableName", out var configTableNameParam) ? configTableNameParam : null;
            ParentType = navigationContext.Parameters.TryGetValue<string>("ParentType", out var parentTypeParam) ? parentTypeParam : null;
            ChassisName = navigationContext.Parameters.TryGetValue<string>("ChassisName", out var chassisNameParam) ? chassisNameParam : null;

            DebugLog($"导航参数解析结果:");
            DebugLog($"  TestTaskName: '{TestTaskName}'");
            DebugLog($"  ConfigTableName: '{ConfigTableName}'");
            DebugLog($"  ParentType: '{ParentType}'");
            DebugLog($"  ChassisName: '{ChassisName}'");

            _projectService?.SetLastMatrixSwitchContext(TestTaskName, ConfigTableName, ChassisName);

            // 设置显示路径
            if (!string.IsNullOrEmpty(TestTaskName) && !string.IsNullOrEmpty(ConfigTableName))
            {
                DisplayPath = $"{TestTaskName} / {ConfigTableName}";
                DebugLog($"设置DisplayPath: '{DisplayPath}'");
            }
            else
            {
                DebugLog($"TestTaskName或ConfigTableName为空，不设置DisplayPath");
            }

            // 先加载可用的矩阵开关设备
            DebugLog($"开始加载可用矩阵开关设备...");
            LoadAvailableSwitchDevices();

            // 然后加载配置表数据
            DebugLog($"开始加载配置表数据...");
            LoadConfigTableData();

            RestoreRuntimeStateFromMemory();

            // 进入页面后用“真实连线表”（程序内维护）刷新按钮红/蓝
            RefreshConnectedFlagsFromRegistry();

            DebugLog($"OnNavigatedTo 完成");
        }

        #endregion

        #region Data Management

        /// <summary>
        /// 加载可用的矩阵开关设备(从当前机箱的SwitchDevice)
        /// </summary>
        private void LoadAvailableSwitchDevices()
        {
            DebugLog($"LoadAvailableSwitchDevices 开始");
            DebugLog($"AvailableSwitchDevices初始数量: {AvailableSwitchDevices.Count}");

            AvailableSwitchDevices.Clear();
            DebugLog($"清空AvailableSwitchDevices，清空后数量: {AvailableSwitchDevices.Count}");

            if (string.IsNullOrEmpty(ChassisName))
            {
                DebugLog($"加载矩阵开关设备列表失败: ChassisName为空");
                return;
            }

            DebugLog($"开始从机箱 '{ChassisName}' 加载矩阵开关设备");

            try
            {
                DebugLog($"调用_pxiChassisService.GetChassisDevices('{ChassisName}')...");
                // 从PxiChassisService获取当前机箱的所有设备
                var devices = _pxiChassisService.GetChassisDevices(ChassisName);

                if (devices == null)
                {
                    DebugLog($"GetChassisDevices返回null");
                    return;
                }

                DebugLog($"GetChassisDevices返回设备数量: {devices.Count}");

                // 详细记录每个设备的信息
                for (int i = 0; i < devices.Count; i++)
                {
                    var device = devices[i];
                    DebugLog($"设备[{i}]: 类型={device.GetType().Name}, Name='{device.Name}', Model='{device.Model}', Manufacturer='{device.Manufacturer}'");

                    // 如果是ChassisDevice，记录子设备信息
                    if (device is ChassisDevice cd && cd.Children != null)
                    {
                        DebugLog($"  子设备数量: {cd.Children.Count}");
                        for (int j = 0; j < cd.Children.Count; j++)
                        {
                            var child = cd.Children[j];
                            DebugLog($"  子设备[{j}]: 类型={child.GetType().Name}, Name='{child.Name}', Model='{child.Model}'");
                        }
                    }
                }

                // 筛选出SwitchDevice类型的设备（使用HashSet去重）
                var switchDevices = new HashSet<MeasureControl.Models.Devices.SwitchDevice>();
                bool foundDevices = false;

                // 先找到 ChassisDevice，用于后续匹配
                ChassisDevice chassisDevice = null;
                foreach (var device in devices)
                {
                    if (device is ChassisDevice cd)
                    {
                        chassisDevice = cd;
                        break;
                    }
                }

                // 先处理 chassis.Devices 中的 SwitchDevice
                foreach (var device in devices)
                {
                    if (device is MeasureControl.Models.Devices.SwitchDevice switchDev)
                    {
                        DebugLog($"找到矩阵开关设备: {switchDev.GetType().Name}, CardName='{switchDev.CardName}', Name='{switchDev.Name}', Model='{switchDev.Model}', Manufacturer='{switchDev.Manufacturer}'");

                        // 如果设备信息不完整，尝试从 ChassisDevice.Children 中通过 CardName 匹配并补充
                        if (chassisDevice != null && chassisDevice.Children != null)
                        {
                            var matchingChild = chassisDevice.Children.FirstOrDefault(c =>
                                c is MeasureControl.Models.Devices.SwitchDevice &&
                                !string.IsNullOrEmpty(c.CardName) &&
                                c.CardName == switchDev.CardName) as MeasureControl.Models.Devices.SwitchDevice;

                            if (matchingChild != null)
                            {
                                DebugLog($"通过CardName匹配到Children中的设备: CardName='{matchingChild.CardName}', Name='{matchingChild.Name}', Model='{matchingChild.Model}', Manufacturer='{matchingChild.Manufacturer}'");

                                // 补充缺失的信息
                                if (string.IsNullOrEmpty(switchDev.Name) && !string.IsNullOrEmpty(matchingChild.Name))
                                {
                                    switchDev.Name = matchingChild.Name;
                                    DebugLog($"从Children补充Name: {switchDev.CardName} -> {matchingChild.Name}");
                                }
                                if (string.IsNullOrEmpty(switchDev.Model) && !string.IsNullOrEmpty(matchingChild.Model))
                                {
                                    switchDev.Model = matchingChild.Model;
                                    DebugLog($"从Children补充Model: {switchDev.CardName} -> {matchingChild.Model}");
                                }
                                if (string.IsNullOrEmpty(switchDev.Manufacturer) && !string.IsNullOrEmpty(matchingChild.Manufacturer))
                                {
                                    switchDev.Manufacturer = matchingChild.Manufacturer;
                                    DebugLog($"从Children补充Manufacturer: {switchDev.CardName} -> {matchingChild.Manufacturer}");
                                }
                            }
                            else
                            {
                                DebugLog($"未在Children中找到CardName匹配的设备: CardName='{switchDev.CardName}'");
                            }
                        }

                        switchDevices.Add(switchDev);
                        foundDevices = true;
                    }
                }

                // 如果 chassis.Devices 中没有找到，则检查 Children 中的设备
                if (!foundDevices && chassisDevice != null && chassisDevice.Children != null)
                {
                    foreach (var child in chassisDevice.Children)
                    {
                        if (child is MeasureControl.Models.Devices.SwitchDevice childSwitchDev)
                        {
                            DebugLog($"找到矩阵开关子设备（Devices中未找到）: CardName='{childSwitchDev.CardName}', Name='{childSwitchDev.Name}', Model='{childSwitchDev.Model}'");
                            switchDevices.Add(childSwitchDev);
                            foundDevices = true;
                        }
                    }
                }

                DebugLog($"找到的矩阵开关设备总数: {switchDevices.Count}");

                if (!foundDevices)
                {
                    DebugLog($"在机箱 '{ChassisName}' 中没有找到任何矩阵开关设备");
                    return;
                }

                // 将设备名称添加到可选列表，格式为"矩阵开关X 厂商型号"
                UpdateAvailableSwitchDevicesList(switchDevices);
            }
            catch (Exception ex)
            {
                // 记录错误但不中断流程
                DebugLog($"加载矩阵开关设备列表时发生异常: {ex.Message}");
                DebugLog($"StackTrace: {ex.StackTrace}");
                if (ex.InnerException != null)
                {
                    DebugLog($"InnerException: {ex.InnerException.Message}");
                }
            }

            DebugLog($"LoadAvailableSwitchDevices 完成");
        }
        /// <summary>
        /// 更新可用设备列表
        /// </summary>
        private void UpdateAvailableSwitchDevicesList(HashSet<MeasureControl.Models.Devices.SwitchDevice> switchDevices)
        {
            DebugLog($"UpdateAvailableSwitchDevicesList 开始，设备数量: {switchDevices.Count}");

            // 将设备名称添加到可选列表，格式为"矩阵开关X 厂商型号"
            int switchIndex = 1;

            // 直接遍历设备，不排序
            foreach (var device in switchDevices)
            {
                // 构建完整的设备名称
                string manufacturerModel = "";

                // 详细记录设备属性
                DebugLog($"处理矩阵开关设备 {switchIndex}:");
                DebugLog($"  设备类型: {device.GetType().Name}");
                DebugLog($"  Manufacturer: '{device.Manufacturer}'");
                DebugLog($"  Model: '{device.Model}'");
                DebugLog($"  Name: '{device.Name}'");

                // 优先使用 Manufacturer + Model 组合
                if (!string.IsNullOrEmpty(device.Manufacturer) && !string.IsNullOrEmpty(device.Model))
                {
                    manufacturerModel = $"{device.Manufacturer} {device.Model}";
                    DebugLog($"  使用Manufacturer+Model组合: '{manufacturerModel}'");
                }
                // 如果 Name 包含完整信息，使用 Name
                else if (!string.IsNullOrEmpty(device.Name))
                {
                    manufacturerModel = device.Name;
                    DebugLog($"  使用Name: '{manufacturerModel}'");
                }
                // 最后的备选方案
                else if (!string.IsNullOrEmpty(device.Model))
                {
                    manufacturerModel = device.Model;
                    DebugLog($"  使用Model: '{manufacturerModel}'");
                }
                else if (!string.IsNullOrEmpty(device.DeviceType))
                {
                    manufacturerModel = device.DeviceType;
                    DebugLog($"  使用DeviceType: '{manufacturerModel}'");
                }
                else
                {
                    DebugLog($"  所有属性都为空，跳过此设备");
                    continue;
                }

                if (!string.IsNullOrEmpty(manufacturerModel))
                {
                    // 格式化为：矩阵开关1 欧开 PXI-2601
                    string displayName = $"矩阵开关{switchIndex} {manufacturerModel.Trim()}";
                    AvailableSwitchDevices.Add(displayName);
                    DebugLog($"  添加显示名称到列表: '{displayName}'");
                    switchIndex++;
                }
            }

            DebugLog($"最终AvailableSwitchDevices数量: {AvailableSwitchDevices.Count}");
            if (AvailableSwitchDevices.Count == 0)
            {
                DebugLog("警告: 可用矩阵开关设备列表为空");
            }
            else
            {
                DebugLog($"成功加载了{AvailableSwitchDevices.Count}个矩阵开关设备");
            }

            // 通知界面更新
            RaisePropertyChanged(nameof(AvailableSwitchDevices));
            DebugLog($"已通知界面AvailableSwitchDevices属性变化");
        }
        /// <summary>
        /// 设备修改事件处理（更新可用设备列表）
        /// </summary>
        private void OnDeviceModified(DeviceModifiedEventArgs args)
        {
            DebugLog($"OnDeviceModified 被调用: ChassisName={args.ChassisName}, ModificationType={args.ModificationType}");
            // 当设备被添加或删除时，刷新可用设备列表
            if (args.ChassisName == ChassisName)
            {
                DebugLog($"设备修改事件匹配当前机箱，刷新设备列表");
                LoadAvailableSwitchDevices();
            }
            else
            {
                DebugLog($"设备修改事件不匹配当前机箱 (当前: {ChassisName}, 事件: {args.ChassisName})");
            }
        }

        /// <summary>
        /// 加载配置表数据
        /// </summary>
        private void LoadConfigTableData()
        {
            DebugLog($"LoadConfigTableData 开始");

            // 初始化MatrixSwitches集合
            if (MatrixSwitches == null)
            {
                MatrixSwitches = new ObservableCollection<MatrixSwitchConfigItem>();
                DebugLog($"初始化MatrixSwitches集合");
            }
            else
            {
                DebugLog($"MatrixSwitches已初始化，当前数量: {MatrixSwitches.Count}");
            }

            // 临时取消订阅，避免在加载时触发保存
            if (MatrixSwitches != null)
            {
                MatrixSwitches.CollectionChanged -= MatrixSwitches_CollectionChanged;
                DebugLog($"临时取消订阅MatrixSwitches.CollectionChanged事件");
            }

            MatrixSwitches.Clear();
            DebugLog($"清空MatrixSwitches集合");

            // 从静态字典中加载数据（如果存在）
            string key = GetMatrixSwitchTableKey();
            DebugLog($"获取矩阵开关表键: '{key}'");

            if (!string.IsNullOrEmpty(key))
            {
                // 在锁内创建数据的快照，避免在锁外使用引用时数据被修改
                List<MatrixSwitchConfigItem> matrixSwitchesSnapshot = null;
                lock (_allMatrixSwitchTableItemsLock)
                {
                    DebugLog($"进入锁，检查静态字典");
                    DebugLog($"静态字典大小: {_allMatrixSwitchTableItems.Count}");

                    if (_allMatrixSwitchTableItems.ContainsKey(key))
                    {
                        var savedMatrixSwitches = _allMatrixSwitchTableItems[key];
                        DebugLog($"找到键 '{key}'，保存的数据数量: {savedMatrixSwitches?.Count}");

                        if (savedMatrixSwitches != null)
                        {
                            if (savedMatrixSwitches.Count > 0)
                            {
                                // 在锁内创建快照，避免锁外数据被修改
                                matrixSwitchesSnapshot = new List<MatrixSwitchConfigItem>();
                                int itemCount = 0;
                                int emptyCount = 0;

                                foreach (var item in savedMatrixSwitches)
                                {
                                    if (item != null)
                                    {
                                        DebugLog($"克隆项目: Index={item.Index}, MatrixSwitchName='{item.MatrixSwitchName}', IsEmpty={item.IsEmpty}");
                                        var clonedItem = item.Clone();
                                        matrixSwitchesSnapshot.Add(clonedItem);
                                        itemCount++;

                                        if (item.IsEmpty)
                                            emptyCount++;
                                    }
                                }

                                DebugLog($"创建快照完成: 总项目数={itemCount}, 空项目数={emptyCount}");
                            }
                            else
                            {
                                DebugLog($"保存的数据集合为空，创建一个有效项");
                                matrixSwitchesSnapshot = new List<MatrixSwitchConfigItem> { new MatrixSwitchConfigItem { IsEmpty = false, Index = 1 } };
                            }
                        }
                        else
                        {
                            DebugLog($"保存的数据为null，创建一个有效项");
                            matrixSwitchesSnapshot = new List<MatrixSwitchConfigItem> { new MatrixSwitchConfigItem { IsEmpty = false, Index = 1 } };
                        }
                    }
                    else
                    {
                        DebugLog($"键 '{key}' 不存在于静态字典中，创建新键 '{key}'");
                        _allMatrixSwitchTableItems[key] = new ObservableCollection<MatrixSwitchConfigItem>(matrixSwitchesSnapshot);
                        DebugLog($"在静态字典中创建新键 '{key}'");
                    }
                }

                // 使用快照数据填充当前表（在锁外操作）
                if (matrixSwitchesSnapshot != null && matrixSwitchesSnapshot.Count > 0)
                {
                    DebugLog($"开始填充MatrixSwitches，快照项目数: {matrixSwitchesSnapshot.Count}");
                    foreach (var item in matrixSwitchesSnapshot)
                    {
                        DebugLog($"添加项目到MatrixSwitches: Index={item.Index}, MatrixSwitchName='{item.MatrixSwitchName}'");
                        MatrixSwitches.Add(item);
                    }
                    DebugLog($"填充MatrixSwitches完成，当前数量: {MatrixSwitches.Count}");
                }
                else
                {
                    DebugLog($"快照为空或null，不填充数据");
                }
            }
            else
            {
                DebugLog($"键为空，创建默认项目");
                MatrixSwitches.Add(new MatrixSwitchConfigItem { IsEmpty = false, Index = 1 });
            }

            // 重新订阅集合变化事件
            MatrixSwitches.CollectionChanged += MatrixSwitches_CollectionChanged;
            DebugLog($"重新订阅MatrixSwitches.CollectionChanged事件");

            // 更新序号
            UpdateMatrixSwitchIndices();

            // 更新分页
            UpdatePagination();

            DebugLog($"LoadConfigTableData 完成");
        }

        /// <summary>
        /// 获取矩阵开关表的键（用于静态字典）
        /// </summary>
        private string GetMatrixSwitchTableKey()
        {
            string key = null;
            if (!string.IsNullOrEmpty(TestTaskName) && !string.IsNullOrEmpty(ConfigTableName))
            {
                key = $"{TestTaskName}/{ConfigTableName}";
            }
            DebugLog($"GetMatrixSwitchTableKey: TestTaskName='{TestTaskName}', ConfigTableName='{ConfigTableName}', Key='{key}'");
            return key;
        }

        /// <summary>
        /// 保存矩阵开关配置到内存
        /// </summary>
        private void SaveMatrixSwitchesToMemory()
        {
            DebugLog($"SaveMatrixSwitchesToMemory 开始");
            string key = GetMatrixSwitchTableKey();
            if (string.IsNullOrEmpty(key))
            {
                DebugLog($"键为空，不保存");
                return;
            }

            DebugLog($"准备保存数据到键: '{key}'");
            DebugLog($"当前MatrixSwitches数量: {MatrixSwitches?.Count ?? 0}");

            // 创建当前数据的快照，避免在锁内修改数据
            List<MatrixSwitchConfigItem> currentMatrixSwitches = MatrixSwitches?.Select(s => s.Clone()).ToList() ?? new List<MatrixSwitchConfigItem>();
            DebugLog($"创建快照，项目数: {currentMatrixSwitches.Count}");

            // 在锁内更新静态字典
            lock (_allMatrixSwitchTableItemsLock)
            {
                DebugLog($"进入锁，更新静态字典");
                if (_allMatrixSwitchTableItems.ContainsKey(key))
                {
                    DebugLog($"键 '{key}' 已存在，清空原有数据");
                    _allMatrixSwitchTableItems[key].Clear();
                    foreach (var item in currentMatrixSwitches)
                    {
                        _allMatrixSwitchTableItems[key].Add(item);
                    }
                    DebugLog($"更新完成，新数量: {_allMatrixSwitchTableItems[key].Count}");
                }
                else
                {
                    DebugLog($"键 '{key}' 不存在，创建新条目");
                    _allMatrixSwitchTableItems[key] = new ObservableCollection<MatrixSwitchConfigItem>(currentMatrixSwitches);
                    DebugLog($"创建完成，数量: {_allMatrixSwitchTableItems[key].Count}");
                }
            }
            DebugLog($"SaveMatrixSwitchesToMemory 完成");
        }

        private void SaveRuntimeStateToMemory()
        {
            string key = GetMatrixSwitchTableKey();
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            var state = new MatrixSwitchRuntimeState
            {
                CurrentPage = CurrentPage,

                SelectedResistanceRoute = SelectedResistanceRoute,

                SelectedDiscreteInputLoopbackRoute = SelectedDiscreteInputLoopbackRoute,
                SelectedDiscreteInputLoopbackMeasureOption = SelectedDiscreteInputLoopbackMeasureOption,

                SelectedDiscreteOutputLoopbackRoute = SelectedDiscreteOutputLoopbackRoute,
                SelectedDiscreteOutputLoopbackMeasureOption = SelectedDiscreteOutputLoopbackMeasureOption,

                SelectedHighSpeedIoLoopbackRoute = SelectedHighSpeedIoLoopbackRoute,
                SelectedHighSpeedIoLoopbackMeasureOption = SelectedHighSpeedIoLoopbackMeasureOption,

                SelectedFrequencyAcquireRoute = SelectedFrequencyAcquireRoute,
                SelectedFrequencyAcquireMeasureOption = SelectedFrequencyAcquireMeasureOption,

                SelectedFrequencyOutput1MeasureOption = SelectedFrequencyOutput1MeasureOption,

                SelectedFrequencyOutputRoute = SelectedFrequencyOutputRoute,

                SelectedCurrentLoopbackRoute = SelectedCurrentLoopbackRoute,

                SelectedAdAcquireRoute = SelectedAdAcquireRoute,
                SelectedAdAcquireMeasureOption = SelectedAdAcquireMeasureOption,

                SelectedResolverLoopbackRoute = SelectedResolverLoopbackRoute,
                SelectedResolverLoopbackMeasureOption = SelectedResolverLoopbackMeasureOption,

                SelectedLvdtLoopbackRoute = SelectedLvdtLoopbackRoute,
                SelectedLvdtLoopbackMeasureOption = SelectedLvdtLoopbackMeasureOption
            };

            lock (_runtimeStateByKeyLock)
            {
                _runtimeStateByKey[key] = state;
            }
        }

        private void RestoreRuntimeStateFromMemory()
        {
            string key = GetMatrixSwitchTableKey();
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            MatrixSwitchRuntimeState state;
            lock (_runtimeStateByKeyLock)
            {
                if (!_runtimeStateByKey.TryGetValue(key, out state))
                {
                    return;
                }
            }

            _isRestoringRuntimeState = true;
            try
            {
                if (state.CurrentPage >= 1)
                {
                    CurrentPage = Math.Min(Math.Max(1, state.CurrentPage), TotalPages);
                }

                SelectedResistanceRoute = state.SelectedResistanceRoute;

                SelectedDiscreteInputLoopbackRoute = state.SelectedDiscreteInputLoopbackRoute;
                SelectedDiscreteInputLoopbackMeasureOption = state.SelectedDiscreteInputLoopbackMeasureOption;

                SelectedDiscreteOutputLoopbackRoute = state.SelectedDiscreteOutputLoopbackRoute;
                SelectedDiscreteOutputLoopbackMeasureOption = state.SelectedDiscreteOutputLoopbackMeasureOption;

                SelectedHighSpeedIoLoopbackRoute = state.SelectedHighSpeedIoLoopbackRoute;
                SelectedHighSpeedIoLoopbackMeasureOption = state.SelectedHighSpeedIoLoopbackMeasureOption;

                SelectedFrequencyAcquireRoute = state.SelectedFrequencyAcquireRoute;
                SelectedFrequencyAcquireMeasureOption = state.SelectedFrequencyAcquireMeasureOption;

                SelectedFrequencyOutput1MeasureOption = state.SelectedFrequencyOutput1MeasureOption;

                SelectedFrequencyOutputRoute = state.SelectedFrequencyOutputRoute;

                SelectedCurrentLoopbackRoute = state.SelectedCurrentLoopbackRoute;

                SelectedAdAcquireRoute = state.SelectedAdAcquireRoute;
                SelectedAdAcquireMeasureOption = state.SelectedAdAcquireMeasureOption;

                SelectedResolverLoopbackRoute = state.SelectedResolverLoopbackRoute;
                SelectedResolverLoopbackMeasureOption = state.SelectedResolverLoopbackMeasureOption;

                SelectedLvdtLoopbackRoute = state.SelectedLvdtLoopbackRoute;
                SelectedLvdtLoopbackMeasureOption = state.SelectedLvdtLoopbackMeasureOption;
            }
            finally
            {
                _isRestoringRuntimeState = false;
            }
        }

        /// <summary>
        /// 监听矩阵开关集合变化，保存数据
        /// </summary>
        private void MatrixSwitches_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            DebugLog($"MatrixSwitches_CollectionChanged 被调用");
            DebugLog($"变化类型: {e.Action}");
            DebugLog($"新项目数: {e.NewItems?.Count ?? 0}");
            DebugLog($"旧项目数: {e.OldItems?.Count ?? 0}");

            // 保存数据
            SaveMatrixSwitchesToMemory();

            // 重新计算序号
            UpdateMatrixSwitchIndices();

            // 更新分页
            UpdatePagination();

            // 通知总页数变化
            RaisePropertyChanged(nameof(TotalPages));

            // 标记项目为已修改
            _eventAggregator.GetEvent<Events.ProjectModifiedEvent>().Publish(new Events.ProjectModifiedEventArgs
            {
                ModificationType = "MatrixSwitchTable",
                Description = "更新矩阵开关配置表"
            });

            DebugLog($"MatrixSwitches_CollectionChanged 完成");
        }

        /// <summary>
        /// 更新矩阵开关的序号
        /// </summary>
        private void UpdateMatrixSwitchIndices()
        {
            DebugLog($"UpdateMatrixSwitchIndices 开始");
            if (MatrixSwitches == null)
            {
                DebugLog($"MatrixSwitches为null");
                return;
            }

            DebugLog($"MatrixSwitches数量: {MatrixSwitches.Count}");
            int index = 1;
            int nonEmptyCount = 0;

            foreach (var item in MatrixSwitches.Where(i => !i.IsEmpty))
            {
                int oldIndex = item.Index;
                item.Index = index++;
                nonEmptyCount++;
                if (oldIndex != item.Index)
                {
                    DebugLog($"更新项目序号: 从 {oldIndex} 到 {item.Index}");
                }
            }

            DebugLog($"UpdateMatrixSwitchIndices 完成: 更新了{nonEmptyCount}个项目");
        }

        #endregion

        #region Command Handlers

        private void OnAddMatrixSwitch()
        {
            DebugLog($"OnAddMatrixSwitch 开始");
            try
            {
                // 确保MatrixSwitches集合已初始化
                if (MatrixSwitches == null)
                {
                    MatrixSwitches = new ObservableCollection<MatrixSwitchConfigItem>();
                    DebugLog($"初始化MatrixSwitches集合");
                }

                // 计算非空项目数量
                int nonEmptyCount = MatrixSwitches.Count(s => !s.IsEmpty);
                DebugLog($"当前非空项目数量: {nonEmptyCount}");

                // 创建新的矩阵开关配置项
                var newItem = new MatrixSwitchConfigItem
                {
                    Index = nonEmptyCount + 1,
                    IsEmpty = false
                };

                DebugLog($"创建新项目: Index={newItem.Index}");

                // 添加到集合
                MatrixSwitches.Add(newItem);
                DebugLog($"添加到集合，当前MatrixSwitches数量: {MatrixSwitches.Count}");

                // 重新计算所有矩阵开关的序号
                UpdateMatrixSwitchIndices();

                // 标记项目为已修改
                _eventAggregator.GetEvent<Events.ProjectModifiedEvent>().Publish(new Events.ProjectModifiedEventArgs
                {
                    ModificationType = "MatrixSwitchTable",
                    Description = "添加矩阵开关配置"
                });

                DebugLog($"发布ProjectModifiedEvent");
            }
            catch (Exception ex)
            {
                DebugLog($"添加矩阵开关时发生错误: {ex.Message}");
                DebugLog($"StackTrace: {ex.StackTrace}");
            }
            DebugLog($"OnAddMatrixSwitch 完成");
        }

        private void OnDeleteMatrixSwitch(MatrixSwitchConfigItem matrixSwitch)
        {
            DebugLog($"OnDeleteMatrixSwitch 开始");
            if (matrixSwitch != null)
            {
                DebugLog($"要删除的项目: Index={matrixSwitch.Index}, MatrixSwitchName='{matrixSwitch.MatrixSwitchName}'");
                // 显示确认删除对话框
                var result = ReMessageBox.Show(
                    $"确定要删除矩阵开关配置吗？",
                    "确认删除",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Question);

                if (result == System.Windows.MessageBoxResult.Yes)
                {
                    DebugLog($"用户确认删除");
                    MatrixSwitches.Remove(matrixSwitch);
                    DebugLog($"删除后MatrixSwitches数量: {MatrixSwitches.Count}");

                    // 重新计算所有矩阵开关的序号
                    UpdateMatrixSwitchIndices();

                    // 标记项目为已修改
                    _eventAggregator.GetEvent<Events.ProjectModifiedEvent>().Publish(new Events.ProjectModifiedEventArgs
                    {
                        ModificationType = "MatrixSwitchTable",
                        Description = "删除矩阵开关配置"
                    });
                    DebugLog($"发布ProjectModifiedEvent");
                }
                else
                {
                    DebugLog($"用户取消删除");
                }
            }
            else
            {
                DebugLog($"要删除的项目为null");
            }
            DebugLog($"OnDeleteMatrixSwitch 完成");
        }

        private void OnEditMatrixSwitch(MatrixSwitchConfigItem matrixSwitch)
        {
            // 直接在表格中编辑，无需额外对话框
            if (matrixSwitch != null)
            {
                DebugLog($"OnEditMatrixSwitch: Index={matrixSwitch.Index}, MatrixSwitchName='{matrixSwitch.MatrixSwitchName}'");
                // 标记项目为已修改
                _eventAggregator.GetEvent<Events.ProjectModifiedEvent>().Publish(new Events.ProjectModifiedEventArgs
                {
                    ModificationType = "MatrixSwitchTable",
                    Description = "编辑矩阵开关配置"
                });
            }
        }

        private void OnNavigateBack()
        {
            DebugLog($"OnNavigateBack 被调用，导航回SignalConfigView");
            // 导航回信号配置界面
            _regionManager.RequestNavigate(
                "MainRegion",
                "SignalConfigView",
                new NavigationParameters
                {
                    { "TestTaskName", TestTaskName },
                    { "ChassisName", ChassisName }
                });
        }

        private void OnClose()
        {
            DebugLog($"OnClose 被调用");
            var result = ReMessageBox.Show("确定要关闭当前配置表吗？", "确认", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
            if (result == System.Windows.MessageBoxResult.Yes)
            {
                DebugLog($"用户确认关闭");
                // 构建完整的pageKey: MatrixSwitchConfigTable_任务名-配置表名
                string pageKey = $"MatrixSwitchConfigTable_{TestTaskName}-{ConfigTableName}";
                DebugLog($"发布ReleaseCurrentPageEvent，pageKey: {pageKey}");

                // 传递完整的pageKey，这样MainWindowViewModel可以正确识别和关闭该页面
                _eventAggregator.GetEvent<Events.ReleaseCurrentPageEvent>().Publish(pageKey);
            }
            else
            {
                DebugLog($"用户取消关闭");
            }
        }

        #endregion

        #region Pagination Methods

        /// <summary>
        /// 更新分页显示
        /// </summary>
        private void UpdatePagination()
        {
            DebugLog($"UpdatePagination 开始");
            UpdatePagedMatrixSwitches();
            UpdatePaginationInfo();
            UpdatePageNumbers();

            PreviousPageCommand?.RaiseCanExecuteChanged();
            NextPageCommand?.RaiseCanExecuteChanged();
            DebugLog($"UpdatePagination 完成");
        }

        /// <summary>
        /// 更新当前页的矩阵开关数据
        /// </summary>
        private void UpdatePagedMatrixSwitches()
        {
            DebugLog($"UpdatePagedMatrixSwitches 开始");
            DebugLog($"CurrentPage: {CurrentPage}, TotalPages: {TotalPages}");

            if (PagedMatrixSwitches == null)
            {
                PagedMatrixSwitches = new ObservableCollection<MatrixSwitchConfigItem>();
                DebugLog($"初始化PagedMatrixSwitches");
            }

            PagedMatrixSwitches.Clear();
            DebugLog($"清空PagedMatrixSwitches");

            if (MatrixSwitches == null || MatrixSwitches.Count == 0)
            {
                DebugLog($"MatrixSwitches为空，添加{PageSize}个空项目");
                for (int i = 0; i < PageSize; i++)
                {
                    PagedMatrixSwitches.Add(new MatrixSwitchConfigItem { IsEmpty = true });
                }
                return;
            }

            int startIndex = (CurrentPage - 1) * PageSize;
            int endIndex = Math.Min(startIndex + PageSize, MatrixSwitches.Count);
            DebugLog($"分页范围: startIndex={startIndex}, endIndex={endIndex}");

            for (int i = startIndex; i < endIndex; i++)
            {
                var item = MatrixSwitches[i];
                if (item != null)
                {
                    item.IsEmpty = false;
                }
                PagedMatrixSwitches.Add(item);
                DebugLog($"添加项目到分页: Index={item?.Index}, IsEmpty={item?.IsEmpty}");
            }

            while (PagedMatrixSwitches.Count < PageSize)
            {
                PagedMatrixSwitches.Add(new MatrixSwitchConfigItem { IsEmpty = true });
                DebugLog($"补充空项目到分页");
            }

            DebugLog($"UpdatePagedMatrixSwitches 完成，PagedMatrixSwitches数量: {PagedMatrixSwitches.Count}");
        }

        private void UpdatePaginationInfo()
        {
            DebugLog($"UpdatePaginationInfo 开始");
            PaginationInfo = PaginationHelper.GetPaginationInfo(MatrixSwitches?.Count ?? 0, CurrentPage, PageSize);
            DebugLog($"设置PaginationInfo: '{PaginationInfo}'");
        }

        private void UpdatePageNumbers()
        {
            DebugLog($"UpdatePageNumbers 开始");
            if (PageNumbers == null)
            {
                PageNumbers = new ObservableCollection<PaginationButtonInfo>();
                DebugLog($"初始化PageNumbers");
            }
            PaginationHelper.UpdatePageNumbers(PageNumbers, TotalPages, CurrentPage, OnGoToPage);
            DebugLog($"UpdatePageNumbers 完成，PageNumbers数量: {PageNumbers.Count}");
        }

        private void OnGoToPage(int page)
        {
            DebugLog($"OnGoToPage: {page}");
            if (page >= 1 && page <= TotalPages)
            {
                CurrentPage = page;
            }
        }

        private void OnPreviousPage()
        {
            DebugLog($"OnPreviousPage: 当前页 {CurrentPage}");
            if (CurrentPage > 1)
            {
                CurrentPage--;
                DebugLog($"跳转到上一页: {CurrentPage}");
            }
            else
            {
                DebugLog($"已经在第一页");
            }
        }

        private bool CanGoToPreviousPage()
        {
            bool canGo = CurrentPage > 1;
            DebugLog($"CanGoToPreviousPage: {canGo}");
            return canGo;
        }

        private void OnNextPage()
        {
            DebugLog($"OnNextPage: 当前页 {CurrentPage}, 总页数 {TotalPages}");
            if (CurrentPage < TotalPages)
            {
                CurrentPage++;
                DebugLog($"跳转到下一页: {CurrentPage}");
            }
            else
            {
                DebugLog($"已经在最后一页");
            }
        }

        private bool CanGoToNextPage()
        {
            bool canGo = CurrentPage < TotalPages;
            DebugLog($"CanGoToNextPage: {canGo}");
            return canGo;
        }

        #endregion

        #region IDisposable

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    DebugLog($"Dispose 开始 (disposing={disposing})");
                    SaveRuntimeStateToMemory();
                    // 取消事件订阅
                    if (MatrixSwitches != null)
                    {
                        MatrixSwitches.CollectionChanged -= MatrixSwitches_CollectionChanged;
                        DebugLog($"取消订阅MatrixSwitches.CollectionChanged事件");
                    }
                }
                _disposed = true;
                DebugLog($"Dispose 完成");
            }
        }

        public void Dispose()
        {
            DebugLog($"IDisposable.Dispose 被调用");
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Debug Helper

        private static void DebugLog(string message)
        {
            string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            string logMessage = $"[{timestamp}] [MatrixSwitchConfigTableViewModel] {message}";
            System.Diagnostics.Debug.WriteLine(logMessage);

            // 可选：同时输出到文件
            // LogToFile(logMessage);
        }

        #endregion
    }
}
