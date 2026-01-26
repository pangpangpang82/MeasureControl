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
        public ICommand CloseCommand { get; private set; }

        // 用于存储所有矩阵开关配置表数据的静态字典（key格式：测试任务名/配置表名）
        private static Dictionary<string, ObservableCollection<MatrixSwitchConfigItem>> _allMatrixSwitchTableItems = new Dictionary<string, ObservableCollection<MatrixSwitchConfigItem>>();

        // 用于同步访问静态字典的锁对象
        private static readonly object _allMatrixSwitchTableItemsLock = new object();

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

            // 订阅设备修改事件，当设备变化时更新可用设备列表
            _eventAggregator.GetEvent<DeviceModifiedEvent>().Subscribe(OnDeviceModified, ThreadOption.UIThread);
            DebugLog($"订阅DeviceModifiedEvent");

            DebugLog($"MatrixSwitchConfigTableViewModel 构造函数完成");
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
                        DebugLog($"键 '{key}' 不存在于静态字典中，创建一个有效项");
                        matrixSwitchesSnapshot = new List<MatrixSwitchConfigItem> { new MatrixSwitchConfigItem { IsEmpty = false, Index = 1 } };
                        // 同时在静态字典中初始化
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

            // 创建当前数据的快照，避免在锁外修改数据
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