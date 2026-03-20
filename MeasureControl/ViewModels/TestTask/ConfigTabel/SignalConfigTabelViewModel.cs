using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;
using MeasureControl.Constants;
using MeasureControl.Helpers;
using MeasureControl.Models;
using MeasureControl.Services;
using MeasureControl.ViewModels.Dialogs;
using MeasureControl.Views;
using MeasureControl.Views.Dialogs;
using static MeasureControl.Models.Devices.DeviceBase;
using Prism.Commands;
using Prism.Events;
using Prism.Mvvm;
using Prism.Regions;

namespace MeasureControl.ViewModels.TestTask.ConfigTabel
{
    /// <summary>
    /// 信号配置表的ViewModel
    /// </summary>
    public class SignalConfigTabelViewModel : BindableBase, INavigationAware, IDisposable
    {
        private readonly IRegionManager _regionManager;
        private readonly IEventAggregator _eventAggregator;
        private readonly ProjectService _projectService;
        private const int PageSize = 14;
        private int _currentPage = 1;
        
        // 用于存储所有信号配置表数据的静态字典（key格式：测试任务名/配置表名）
        private static Dictionary<string, ObservableCollection<SignalConfigItem>> _allSignalTabelItems = new Dictionary<string, ObservableCollection<SignalConfigItem>>();
        
        // 用于同步访问静态字典的锁对象
        private static readonly object _allSignalTabelItemsLock = new object();
        
        /// <summary>获取所有信号配置表数据</summary>
        public static Dictionary<string, List<SignalConfigItem>> GetAllSignalTabelItems()
        {
            lock (_allSignalTabelItemsLock)
            {
                return _allSignalTabelItems.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value?.Where(s => !s.IsEmpty).Select(s => { var clone = s.Clone(); clone.IsEmpty = false; return clone; }).ToList()
                           ?? new List<SignalConfigItem>());
            }
        }

        /// <summary>加载信号配置表数据到静态字典</summary>
        public static void LoadSignalTabelItems(Dictionary<string, List<SignalConfigItem>> items)
        {
            lock (_allSignalTabelItemsLock)
            {
                _allSignalTabelItems.Clear();
                if (items == null) return;
                foreach (var kvp in items)
                    _allSignalTabelItems[kvp.Key] = new ObservableCollection<SignalConfigItem>(
                        kvp.Value?.Where(s => s != null).Select(s => s.Clone()) ?? Enumerable.Empty<SignalConfigItem>());
            }
        }

        /// <summary>清空所有信号配置表数据</summary>
        public static void ClearAllSignalTabelItems()
        {
            lock (_allSignalTabelItemsLock) { _allSignalTabelItems.Clear(); }
        }

        /// <summary>
        /// 更新指定变量的原始值和实时值（直接更新静态字典中的原始对象）
        /// </summary>
        /// <param name="tabelKey">表键（格式：测试任务名/变量表名）</param>
        /// <param name="signalName">信号名称</param>
        /// <param name="rawValue">原始值</param>
        /// <param name="realTimeValue">实时值（可选，不提供则根据校准计算）</param>
        /// <returns>是否更新成功</returns>
        public static bool UpdateSignalValue(string tabelKey, string signalName, double rawValue, double? realTimeValue = null)
        {
            lock (_allSignalTabelItemsLock)
            {
                if (!_allSignalTabelItems.TryGetValue(tabelKey, out var signals))
                    return false;
                
                var signal = signals?.FirstOrDefault(s => s.SignalName == signalName);
                if (signal == null)
                    return false;
                
                signal.RawValue = rawValue;
                
                // 计算实时值
                if (realTimeValue.HasValue)
                {
                    signal.RealTimeValue = realTimeValue.Value;
                }
                else
                {
                    // 数字量直接等于原始值，模拟量应用校准公式
                    bool isDigital = signal.SignalType == "数字量" || 
                                     signal.SignalType?.Contains("DI") == true || 
                                     signal.SignalType?.Contains("DO") == true;
                    
                    if (!isDigital && signal.IsCalibrated)
                    {
                        signal.RealTimeValue = rawValue * signal.Slope + signal.Intercept;
                    }
                    else
                    {
                        signal.RealTimeValue = rawValue;
                    }
                }
                
                return true;
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
            set => SetProperty(ref _chassisName, value);
        }

        private string _testTaskName;
        /// <summary>
        /// 测试任务名称
        /// </summary>
        public string TestTaskName
        {
            get => _testTaskName;
            set => SetProperty(ref _testTaskName, value);
        }

        private string _configTabelName;
        /// <summary>
        /// 配置表名称
        /// </summary>
        public string ConfigTabelName
        {
            get => _configTabelName;
            set => SetProperty(ref _configTabelName, value);
        }

        private string _parentType;
        private bool _disposed = false;
        /// <summary>
        /// 父节点类型
        /// </summary>
        public string ParentType
        {
            get => _parentType;
            set => SetProperty(ref _parentType, value);
        }

        private string _displayPath;
        /// <summary>
        /// 显示路径（用于界面标题）
        /// </summary>
        public string DisplayPath
        {
            get => _displayPath;
            set => SetProperty(ref _displayPath, value);
        }

        private ObservableCollection<SignalConfigItem> _signals;
        /// <summary>
        /// 信号配置列表
        /// </summary>
        public ObservableCollection<SignalConfigItem> Signals
        {
            get => _signals;
            set
            {
                if (_signals != null)
                {
                    _signals.CollectionChanged -= Signals_CollectionChanged;
                }
                SetProperty(ref _signals, value);
                if (_signals != null)
                {
                    _signals.CollectionChanged += Signals_CollectionChanged;
                }
            }
        }

        private ObservableCollection<ChannelTreeNode> _channelBindingTreeRoot;
        /// <summary>
        /// 通道绑定信息树根节点集合
        /// </summary>
        public ObservableCollection<ChannelTreeNode> ChannelBindingTreeRoot
        {
            get => _channelBindingTreeRoot;
            set => SetProperty(ref _channelBindingTreeRoot, value);
        }

        private ObservableCollection<ChannelTreeNode> _boundChannelTreeRoot;
        /// <summary>
        /// 已绑定通道树根节点集合（用于右侧显示）
        /// 结构：通道配置表名 -> 通道类型(DI/DO/AI/AO/RO) -> 已绑定通道名称
        /// </summary>
        public ObservableCollection<ChannelTreeNode> BoundChannelTreeRoot
        {
            get => _boundChannelTreeRoot;
            set => SetProperty(ref _boundChannelTreeRoot, value);
        }

        private ObservableCollection<SignalConfigItem> _pagedSignals;
        /// <summary>当前页显示的信号列表</summary>
        public ObservableCollection<SignalConfigItem> PagedSignals
        {
            get => _pagedSignals;
            set => SetProperty(ref _pagedSignals, value);
        }

        private string _paginationInfo;
        /// <summary>分页信息文本</summary>
        public string PaginationInfo
        {
            get => _paginationInfo;
            set => SetProperty(ref _paginationInfo, value);
        }

        private ObservableCollection<PaginationButtonInfo> _pageNumbers;
        /// <summary>分页按钮信息列表</summary>
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
                if (Signals == null || Signals.Count == 0)
                    return 1;
                return (int)Math.Ceiling((double)Signals.Count / PageSize);
            }
        }

        #endregion

        #region Commands

        public DelegateCommand AddSignalCommand { get; }
        public DelegateCommand<SignalConfigItem> DeleteSignalCommand { get; }
        public DelegateCommand<SignalConfigItem> EditSignalCommand { get; }
        public DelegateCommand<SignalConfigItem> CalibrationCommand { get; }
        public DelegateCommand<SignalConfigItem> WaveformCommand { get; }
        public DelegateCommand<ChannelTreeNode> ToggleTreeNodeCommand { get; }
        public DelegateCommand<ChannelTreeNode> AddSignalFromTreeCommand { get; }
        public DelegateCommand PreviousPageCommand { get; }
        public DelegateCommand NextPageCommand { get; }
        // 浮动窗口命令
        public DelegateCommand FloatWindowCommand { get; }
        public DelegateCommand MinimizeInRegionCommand { get; }
        public DelegateCommand CloseInRegionCommand { get; }
        
        // 导航命令
        public DelegateCommand NavigateToProjectTreeCommand { get; }

        #endregion

        #region Constructor

        public SignalConfigTabelViewModel(
            IRegionManager regionManager,
            IEventAggregator eventAggregator,
            ProjectService projectService,
            IChannelBindingService channelBindingService,
            ChannelManager channelManager)
        {
            _regionManager = regionManager ?? throw new ArgumentNullException(nameof(regionManager));
            _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
            _projectService = projectService ?? throw new ArgumentNullException(nameof(projectService));

            // 初始化命令
            AddSignalCommand = new DelegateCommand(OnAddSignal);
            DeleteSignalCommand = new DelegateCommand<SignalConfigItem>(OnDeleteSignal);
            EditSignalCommand = new DelegateCommand<SignalConfigItem>(OnEditSignal);
            // 信号标定命令
            CalibrationCommand = new DelegateCommand<SignalConfigItem>(OnCalibration);
            WaveformCommand = new DelegateCommand<SignalConfigItem>(OnWaveform);
            ToggleTreeNodeCommand = new DelegateCommand<ChannelTreeNode>(OnToggleTreeNode);
            AddSignalFromTreeCommand = new DelegateCommand<ChannelTreeNode>(OnAddSignalFromTree);
            PreviousPageCommand = new DelegateCommand(OnPreviousPage, CanGoToPreviousPage);
            NextPageCommand = new DelegateCommand(OnNextPage, CanGoToNextPage);
            
            // 浮动窗口命令
            FloatWindowCommand = new DelegateCommand(OnFloatWindow);
            MinimizeInRegionCommand = new DelegateCommand(OnMinimizeInRegion);
            CloseInRegionCommand = new DelegateCommand(OnCloseInRegion);
            
            // 导航命令
            NavigateToProjectTreeCommand = new DelegateCommand(OnNavigateToProjectTree);

            // 初始化集合（在创建子 ViewModel 之前初始化，确保子 ViewModel 可以访问）
            Signals = new ObservableCollection<SignalConfigItem>();
            ChannelBindingTreeRoot = new ObservableCollection<ChannelTreeNode>();
            BoundChannelTreeRoot = new ObservableCollection<ChannelTreeNode>();
            PagedSignals = new ObservableCollection<SignalConfigItem>();
            PageNumbers = new ObservableCollection<PaginationButtonInfo>();
            
            // 订阅Signals集合变化事件，以便在添加/删除信号时自动订阅/取消订阅属性更改事件
            Signals.CollectionChanged += Signals_CollectionChanged;
            
            // 构建通道绑定信息树
            BuildChannelBindingTree();
            
            // 构建已绑定通道树
            BuildBoundChannelTree();

            UpdatePagination();
        }

        #endregion

        #region INavigationAware Implementation

        public void OnNavigatedTo(NavigationContext navigationContext)
        {
            // 从导航参数中获取信息
            if (navigationContext.Parameters.ContainsKey("ChassisName"))
            {
                ChassisName = navigationContext.Parameters["ChassisName"] as string;
            }

            if (navigationContext.Parameters.ContainsKey("TestTaskName"))
            {
                TestTaskName = navigationContext.Parameters["TestTaskName"] as string;
            }

            if (navigationContext.Parameters.ContainsKey("ConfigTabelName"))
            {
                ConfigTabelName = navigationContext.Parameters["ConfigTabelName"] as string;
            }

            if (navigationContext.Parameters.ContainsKey("ParentType"))
            {
                ParentType = navigationContext.Parameters["ParentType"] as string;
            }

            // 生成显示路径，包含机箱名称
            string parentName = GetParentDisplayName(ParentType);
            if (!string.IsNullOrEmpty(ChassisName))
            {
                DisplayPath = $"{ChassisName}/{TestTaskName}/{parentName}/{ConfigTabelName}";
            }
            else
            {
                DisplayPath = $"{TestTaskName}/{parentName}/{ConfigTabelName}";
            }
            
            // 订阅通道数据加载事件（用于在通道配置表数据变化时更新通道绑定树）
            _eventAggregator.GetEvent<Events.ChannelTabelItemsLoadEvent>().Subscribe(OnChannelTabelItemsLoad, ThreadOption.UIThread);
            
            // 订阅信号数据请求事件（用于项目保存时收集所有信号配置表数据）
            _eventAggregator.GetEvent<Events.SignalTabelItemsRequestEvent>().Subscribe(OnSignalTabelItemsRequest, ThreadOption.UIThread);
            
            // 加载配置表数据
            LoadConfigTabelData();
            
            // 重新构建通道绑定信息树（确保获取最新数据）
            BuildChannelBindingTree();

            UpdatePagination();
        }

        public bool IsNavigationTarget(NavigationContext navigationContext)
        {
            // 每次创建新实例，支持多个相同类型页面
            return false;
        }

        public void OnNavigatedFrom(NavigationContext navigationContext)
        {
            // 保存信号数据到内存（离开页面时保存）
            SaveSignalsToMemory();
            
            // 取消订阅所有信号的属性更改事件
            UnsubscribeFromAllSignalPropertyChanged();
            
            // 取消订阅事件
            _eventAggregator.GetEvent<Events.ChannelTabelItemsLoadEvent>().Unsubscribe(OnChannelTabelItemsLoad);
            _eventAggregator.GetEvent<Events.SignalTabelItemsRequestEvent>().Unsubscribe(OnSignalTabelItemsRequest);
        }

        #endregion

        #region IDisposable Implementation

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                // 保存信号数据到内存
                SaveSignalsToMemory();
                
                // 取消订阅所有信号的属性更改事件
                UnsubscribeFromAllSignalPropertyChanged();
                
                // 取消订阅集合变化事件
                if (Signals != null)
                {
                    Signals.CollectionChanged -= Signals_CollectionChanged;
                }
                
                // 取消订阅事件
                _eventAggregator.GetEvent<Events.ChannelTabelItemsLoadEvent>().Unsubscribe(OnChannelTabelItemsLoad);
                _eventAggregator.GetEvent<Events.SignalTabelItemsRequestEvent>().Unsubscribe(OnSignalTabelItemsRequest);
                
                // 清理资源
                _disposed = true;
            }
        }
        
        /// <summary>
        /// 处理通道数据加载事件（当通道配置表数据变化时，更新通道绑定树）
        /// </summary>
        private void OnChannelTabelItemsLoad(Events.ChannelTabelItemsLoadEventArgs args)
        {
            // 重新构建通道绑定信息树
            BuildChannelBindingTree();
            
            // 重新构建已绑定通道树（因为通道类型信息可能已更新）
            BuildBoundChannelTree();
            UpdatePagination();
        }
        
        /// <summary>
        /// 获取信号配置表的唯一键（格式：机箱名/测试任务名/配置表名，如果没有机箱名则使用：测试任务名/配置表名）
        /// </summary>
        private string GetSignalTabelKey()
        {
            if (!string.IsNullOrEmpty(ChassisName))
            {
                return $"{ChassisName}/{TestTaskName}/{ConfigTabelName}";
            }
            return $"{TestTaskName}/{ConfigTabelName}";
        }

        #endregion

        #region Private Methods
        /// <summary>
        /// 处理信号数据请求事件
        /// 从静态字典中获取所有信号配置表数据，确保当前页面数据已保存
        /// </summary>
        private void OnSignalTabelItemsRequest(Events.SignalTabelItemsRequestEventArgs args)
        {
            if (args == null)
                return;

            // 初始化结果字典
            if (args.SignalTabelItems == null)
            {
                args.SignalTabelItems = new Dictionary<string, List<SignalConfigItem>>();
            }

            // 如果当前页面有TestTaskName和ConfigTabelName，先保存当前页面的数据到静态字典
            if (!string.IsNullOrEmpty(TestTaskName) && !string.IsNullOrEmpty(ConfigTabelName))
            {
                SaveSignalsToMemory();
            }

            // 从静态字典中获取所有信号配置表数据
            lock (_allSignalTabelItemsLock)
            {
                foreach (var kvp in _allSignalTabelItems)
                {
                    // 转换ObservableCollection为List，并排除空行
                    var signalsList = kvp.Value?.Where(s => !s.IsEmpty).Select(s => new SignalConfigItem
                    {
                        Index = s.Index,
                        SignalName = s.SignalName,
                        SignalType = s.SignalType,
                        ActualChannel = s.ActualChannel,
                        RawValueUnit = s.RawValueUnit,
                        RealTimeValueUnit = s.RealTimeValueUnit,
                        RawValue = s.RawValue,
                        RealTimeValue = s.RealTimeValue,
                        Remarks = s.Remarks,
                        IsEmpty = false
                    }).ToList() ?? new List<SignalConfigItem>();

                    // 使用最新的数据覆盖
                    args.SignalTabelItems[kvp.Key] = signalsList;
                }
            }
        }

        /// <summary>
        /// 加载配置表数据
        /// </summary>
        private void LoadConfigTabelData()
        {
            // 初始化Signals集合
            if (Signals == null)
            {
                Signals = new ObservableCollection<SignalConfigItem>();
            }
            
            // 临时取消订阅，避免在加载时触发保存
            Signals.CollectionChanged -= Signals_CollectionChanged;
            Signals.Clear();

            // 从静态字典中加载数据（如果存在）
            string key = GetSignalTabelKey();
            
            if (!string.IsNullOrEmpty(key))
            {
                // 在锁内创建数据的快照，避免在锁外使用引用时数据被修改
                List<SignalConfigItem> signalsSnapshot = null;
                lock (_allSignalTabelItemsLock)
                {
                    if (_allSignalTabelItems.ContainsKey(key))
                    {
                        var savedSignals = _allSignalTabelItems[key];
                        
                        // 详细检查集合内容
                        if (savedSignals != null)
                        {
                            if (savedSignals.Count > 0)
                            {
                                // 在锁内创建快照，避免锁外数据被修改
                                signalsSnapshot = new List<SignalConfigItem>();
                                foreach (var sig in savedSignals)
                                {
                                    if (sig != null)
                                    {
                                        signalsSnapshot.Add(new SignalConfigItem
                                        {
                                            Index = sig.Index,
                                            SignalName = sig.SignalName,
                                            SignalType = sig.SignalType,
                                            ActualChannel = sig.ActualChannel,
                                            RawValueUnit = sig.RawValueUnit,
                                            RealTimeValueUnit = sig.RealTimeValueUnit,
                                            RawValue = sig.RawValue,
                                            RealTimeValue = sig.RealTimeValue,
                                            Remarks = sig.Remarks,
                                            IsEmpty = sig.IsEmpty
                                        });
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                    }
                }
                
                // 在锁外使用快照数据
                if (signalsSnapshot != null && signalsSnapshot.Count > 0)
                {
                    foreach (var signal in signalsSnapshot)
                    {
                        Signals.Add(signal);
                        // 为每个信号订阅属性更改事件
                        SubscribeToSignalPropertyChanged(signal);
                    }
                    
                    // 构建已绑定通道树
                    BuildBoundChannelTree();
                }
            }
            else
            {
            }
            
            // 重新订阅集合变化事件
            Signals.CollectionChanged += Signals_CollectionChanged;
        }
        
        /// <summary>
        /// 保存信号配置表数据到内存
        /// </summary>
        private void SaveSignalsToMemory()
        {
            string key = GetSignalTabelKey();
            if (string.IsNullOrEmpty(key))
                return;

            lock (_allSignalTabelItemsLock)
            {
                // 创建新集合，只保存非空行（排除 IsEmpty = true 的行）
                var signalsCollection = new ObservableCollection<SignalConfigItem>();
                foreach (var signal in Signals)
                {
                    // 只保存非空行
                    if (!signal.IsEmpty)
                    {
                        var newSignal = new SignalConfigItem
                        {
                            Index = signal.Index,
                            SignalName = signal.SignalName,
                            SignalType = signal.SignalType,
                            ActualChannel = signal.ActualChannel,
                            RawValueUnit = signal.RawValueUnit,
                            RealTimeValueUnit = signal.RealTimeValueUnit,
                            RawValue = signal.RawValue,
                            RealTimeValue = signal.RealTimeValue,
                            Remarks = signal.Remarks,
                            IsEmpty = false
                        };
                        signalsCollection.Add(newSignal);
                    }
                }
                
                // 如果当前要保存的集合为空，且静态字典中已经有数据，则不要覆盖
                // 这可以防止在数据加载完成前，空集合覆盖已加载的数据
                if (signalsCollection.Count == 0 && _allSignalTabelItems.ContainsKey(key))
                {
                    var existingCollection = _allSignalTabelItems[key];
                    if (existingCollection != null && existingCollection.Count > 0)
                    {
                        return;
                    }
                }
                
                _allSignalTabelItems[key] = signalsCollection;
            }
        }

        /// <summary>
        /// 获取父节点显示名称
        /// </summary>
        private string GetParentDisplayName(string parentType)
        {
            return parentType switch
            {
                "channel_config" => "通道配置",
                "signal_config" => "信号配置",
                "icd_config" => "ICD配置",
                "test_sequence" => "测试序列",
                "report" => "报表模板",
                _ => parentType
            };
        }

        /// <summary>
        /// 构建通道绑定信息树
        /// 从所有通道配置表中获取数据，构建树形结构：通道配置表（根节点）→ 通道名称
        /// 如果TestTaskName不为空，只显示当前测试任务下的通道配置表
        /// 节点完全展开，按通道类型分组显示：配置表名 -> 通道类型 -> 通道名称
        /// </summary>
        private void BuildChannelBindingTree()
        {
            ChannelBindingTreeRoot.Clear();

            try
            {
                // 从ChannelConfigTabelViewModel获取所有通道配置表数据
                var allChannelTabelItems = ChannelConfigTabelViewModel.GetAllChannelTabelItems();

                // 过滤通道配置表：如果指定了机箱名和测试任务名称，只处理当前机箱和测试任务的通道配置表
                IEnumerable<KeyValuePair<string, List<ChannelTabelItem>>> filteredItems;
                if (!string.IsNullOrEmpty(TestTaskName))
                {
                    if (allChannelTabelItems != null && allChannelTabelItems.Count > 0)
                    {
                        // 构建期望的前缀：如果有机箱名，格式为"机箱名/测试任务名/"，否则为"测试任务名/"
                        string expectedPrefix;
                        if (!string.IsNullOrEmpty(ChassisName))
                        {
                            expectedPrefix = $"{ChassisName}/{TestTaskName}/";
                        }
                        else
                        {
                            expectedPrefix = $"{TestTaskName}/";
                        }
                        
                        // 过滤出当前机箱和测试任务的通道配置表
                        filteredItems = allChannelTabelItems
                            .Where(kvp => kvp.Key.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase));
                    }
                    else
                    {
                        // 如果没有通道配置表数据，创建一个占位结构
                        filteredItems = new List<KeyValuePair<string, List<ChannelTabelItem>>>();
                    }
                }
                else
                {
                    // 显示所有测试任务的通道配置表
                    filteredItems = allChannelTabelItems != null && allChannelTabelItems.Count > 0 
                        ? allChannelTabelItems 
                        : new List<KeyValuePair<string, List<ChannelTabelItem>>>();
                }

                // 如果过滤后没有数据，但当前有TestTaskName，创建一个占位结构
                if (!filteredItems.Any() && !string.IsNullOrEmpty(TestTaskName))
                {
                    // 创建占位配置表节点（使用"默认配置表"作为名称）
                    var placeholderConfigTabelNode = new ChannelTreeNode
                    {
                        DisplayName = "默认配置表",
                        NodeType = "ConfigTabel",
                        Tag = $"{TestTaskName}/默认配置表",
                        IsExpanded = true  // 完全展开
                    };

                    ChannelBindingTreeRoot.Add(placeholderConfigTabelNode);
                    return;
                }

                // 遍历所有通道配置表，每个配置表都是一个根节点
                foreach (var kvp in filteredItems.OrderBy(x => x.Key))
                {
                    string configTabelKey = kvp.Key;
                    var channels = kvp.Value;
                    
                    // 提取配置表名称（key格式可能是"机箱名/测试任务名/配置表名"或"测试任务名/配置表名"）
                    var parts = configTabelKey.Split('/');
                    string configTabelName;
                    if (parts.Length >= 3)
                    {
                        // 格式：机箱名/测试任务名/配置表名
                        configTabelName = parts[2];
                    }
                    else if (parts.Length == 2)
                    {
                        // 格式：测试任务名/配置表名
                        configTabelName = parts[1];
                    }
                    else
                    {
                        configTabelName = configTabelKey;
                    }
                    
                    // 创建配置表节点（作为根节点，完全展开）
                    var configTabelNode = new ChannelTreeNode
                    {
                        DisplayName = configTabelName,
                        NodeType = "ConfigTabel",
                        Tag = configTabelKey,
                        IsExpanded = true  // 完全展开
                    };

                    // 按通道类型分组显示
                    var channelsByType = channels?
                        .Where(c => !string.IsNullOrEmpty(c.ChannelName) && !string.IsNullOrEmpty(c.InputOutputType))
                        .GroupBy(c => NormalizeChannelType(c.InputOutputType))
                        .OrderBy(g => GetChannelTypeOrderWeight(g.Key))
                        .ToDictionary(g => g.Key, g => g.OrderBy(c => c.ChannelName, StringComparer.OrdinalIgnoreCase).ToList())
                        ?? new Dictionary<string, List<ChannelTabelItem>>();

                    // 定义要显示的通道类型顺序
                    var channelTypes = new[] { "DI", "DO", "AI", "AO", "RO" };

                    // 为每个通道类型创建子节点
                    foreach (var channelType in channelTypes)
                    {
                        if (channelsByType.ContainsKey(channelType) && channelsByType[channelType].Count > 0)
                        {
                            // 创建通道类型节点
                            var channelTypeNode = new ChannelTreeNode
                            {
                                DisplayName = channelType,
                                NodeType = "ChannelType",
                                Tag = $"{configTabelName}:{channelType}",
                                IsExpanded = true  // 通道类型节点默认展开
                            };

                            // 为该类型下的所有通道创建子节点
                            foreach (var channel in channelsByType[channelType])
                            {
                                var channelNode = new ChannelTreeNode
                                {
                                    DisplayName = channel.ChannelName,
                                    NodeType = "Channel",
                                    // Tag格式：配置表名:通道名称（与AddSignalDialogViewModel中的格式一致）
                                    Tag = $"{configTabelName}:{channel.ChannelName}",
                                    IsExpanded = false
                                };
                                channelTypeNode.Children.Add(channelNode);
                            }

                            configTabelNode.Children.Add(channelTypeNode);
                        }
                    }

                    // 处理其他未分类的通道类型
                    foreach (var otherTypeKvp in channelsByType.Where(kvp => !channelTypes.Contains(kvp.Key)))
                    {
                        var channelType = otherTypeKvp.Key;
                        var channelsInType = otherTypeKvp.Value;

                        // 创建通道类型节点
                        var channelTypeNode = new ChannelTreeNode
                        {
                            DisplayName = channelType,
                            NodeType = "ChannelType",
                            Tag = $"{configTabelName}:{channelType}",
                            IsExpanded = false
                        };

                        // 为该类型下的所有通道创建子节点
                        foreach (var channel in channelsInType)
                        {
                            var channelNode = new ChannelTreeNode
                            {
                                DisplayName = channel.ChannelName,
                                NodeType = "Channel",
                                Tag = $"{configTabelName}:{channel.ChannelName}",
                                IsExpanded = false
                            };
                            channelTypeNode.Children.Add(channelNode);
                        }

                        configTabelNode.Children.Add(channelTypeNode);
                    }

                    // 将配置表节点添加到根节点列表
                    ChannelBindingTreeRoot.Add(configTabelNode);
                }
            }
            catch (Exception)
            {
            }
        }

        private int GetChannelOrderWeight(string inputOutputType)
        {
            if (string.IsNullOrWhiteSpace(inputOutputType))
                return 50;

            switch (inputOutputType.ToUpperInvariant())
            {
                case "DI": return 0;
                case "DO": return 1;
                case "AI": return 2;
                case "AO": return 3;
                case "RO": return 4;
                case "CAN": return 5;
                default: return 50; // 其他类型置于后面
            }
        }

        /// <summary>
        /// 获取通道类型排序权重（用于类型分组排序）
        /// </summary>
        private int GetChannelTypeOrderWeight(string channelType)
        {
            switch (channelType?.ToUpper())
            {
                case "DI": return 0;
                case "DO": return 1;
                case "AI": return 2;
                case "AO": return 3;
                case "RO": return 4;
                default: return 50; // 其他类型置于后面
            }
        }

        private void Signals_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            // 处理新添加的信号
            if (e.NewItems != null)
            {
                // 为新添加的信号订阅属性更改事件
                foreach (SignalConfigItem signal in e.NewItems)
                {
                    SubscribeToSignalPropertyChanged(signal);
                }
            }

            // 处理移除的信号
            if (e.OldItems != null)
            {
                // 为移除的信号取消订阅属性更改事件
                foreach (SignalConfigItem signal in e.OldItems)
                {
                    UnsubscribeFromSignalPropertyChanged(signal);
                }
            }

            // 集合变化时自动保存到内存
            SaveSignalsToMemory();

            // 新增时如果超出当前页，跳到最后一页
            if (e.NewItems != null)
            {
                int lastPage = TotalPages;
                if (lastPage > 0 && CurrentPage < lastPage)
                {
                    CurrentPage = lastPage;
                }
            }

            // 删除后如果当前页超出总页数，回退
            if (e.OldItems != null)
            {
                int totalPages = TotalPages;
                if (totalPages > 0 && CurrentPage > totalPages)
                {
                    CurrentPage = totalPages;
                }
            }

            // 更新已绑定通道树
            BuildBoundChannelTree();

            UpdatePagination();
        }

        private void UpdateSignalIndices()
        {
            for (int i = 0; i < Signals.Count; i++)
            {
                Signals[i].Index = i + 1;
            }
        }

        /// <summary>
        /// 订阅信号的属性更改事件
        /// </summary>
        private void SubscribeToSignalPropertyChanged(SignalConfigItem signal)
        {
            if (signal != null)
            {
                signal.PropertyChanged += Signal_PropertyChanged;
            }
        }

        /// <summary>
        /// 取消订阅信号的属性更改事件
        /// </summary>
        private void UnsubscribeFromSignalPropertyChanged(SignalConfigItem signal)
        {
            if (signal != null)
            {
                signal.PropertyChanged -= Signal_PropertyChanged;
            }
        }

        /// <summary>
        /// 取消订阅所有信号的属性更改事件
        /// </summary>
        private void UnsubscribeFromAllSignalPropertyChanged()
        {
            if (Signals != null)
            {
                foreach (var signal in Signals)
                {
                    UnsubscribeFromSignalPropertyChanged(signal);
                }
            }
        }

        /// <summary>
        /// 处理信号属性更改事件
        /// </summary>
        private void Signal_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // 当信号属性更改时，自动保存到内存
            // 排除Index属性，因为Index变化不会影响数据内容
            if (e.PropertyName != nameof(SignalConfigItem.Index))
            {
                SaveSignalsToMemory();
                
                // 如果ActualChannel属性更改，更新已绑定通道树
                if (e.PropertyName == nameof(SignalConfigItem.ActualChannel))
                {
                    BuildBoundChannelTree();
                }
                
                // 标记项目为已修改
                _eventAggregator.GetEvent<Events.ProjectModifiedEvent>().Publish(new Events.ProjectModifiedEventArgs
                {
                    ModificationType = "SignalTabel",
                    Description = $"修改信号属性: {e.PropertyName}"
                });
            }
        }

        /// <summary>
        /// 构建已绑定通道树
        /// 从当前Signals集合中提取已绑定的通道，构建树形结构：通道配置表名 -> 通道类型(DI/DO/AI/AO/RO) -> 已绑定通道名称
        /// </summary>
        private void BuildBoundChannelTree()
        {
            if (BoundChannelTreeRoot == null)
            {
                BoundChannelTreeRoot = new ObservableCollection<ChannelTreeNode>();
            }
            
            BoundChannelTreeRoot.Clear();

            try
            {
                if (Signals == null || Signals.Count == 0)
                    return;

                // 从ChannelConfigTabelViewModel获取所有通道配置表数据，用于获取通道类型信息
                var allChannelTabelItems = ChannelConfigTabelViewModel.GetAllChannelTabelItems();
                if (allChannelTabelItems == null || allChannelTabelItems.Count == 0)
                    return;

                // 创建一个字典，用于快速查找通道类型：key格式为"配置表名:通道名称"，value为通道类型
                var channelTypeDict = new Dictionary<string, string>();
                foreach (var kvp in allChannelTabelItems)
                {
                    // 提取配置表名称（key格式可能是"机箱名/测试任务名/配置表名"或"测试任务名/配置表名"）
                    var parts = kvp.Key.Split('/');
                    string configTabelName;
                    if (parts.Length >= 3)
                    {
                        // 格式：机箱名/测试任务名/配置表名
                        configTabelName = parts[2];
                    }
                    else if (parts.Length == 2)
                    {
                        // 格式：测试任务名/配置表名
                        configTabelName = parts[1];
                    }
                    else
                    {
                        configTabelName = kvp.Key;
                    }
                    
                    foreach (var channel in kvp.Value)
                    {
                        if (!string.IsNullOrEmpty(channel.ChannelName) && !string.IsNullOrEmpty(channel.InputOutputType))
                        {
                            string channelKey = $"{configTabelName}:{channel.ChannelName}";
                            channelTypeDict[channelKey] = channel.InputOutputType;
                        }
                    }
                }

                // 从Signals中提取所有已绑定的通道（ActualChannel不为空）
                var boundChannels = Signals
                    .Where(s => !s.IsEmpty && !string.IsNullOrEmpty(s.ActualChannel))
                    .Select(s => s.ActualChannel)
                    .Distinct()
                    .ToList();

                if (boundChannels.Count == 0)
                    return;

                // 按配置表名和通道类型分组
                var groupedByConfigTabel = new Dictionary<string, Dictionary<string, List<string>>>();
                foreach (var channel in boundChannels)
                {
                    // ActualChannel格式为"配置表名:通道名称"
                    var channelParts = channel.Split(new[] { ':' }, 2);
                    if (channelParts.Length == 2)
                    {
                        string configTabelName = channelParts[0];
                        string channelName = channelParts[1];

                        // 获取通道类型
                        string channelKey = $"{configTabelName}:{channelName}";
                        channelTypeDict.TryGetValue(channelKey, out string channelType);

                        // 标准化通道类型名称
                        string normalizedType = NormalizeChannelType(channelType);

                        if (!groupedByConfigTabel.ContainsKey(configTabelName))
                        {
                            groupedByConfigTabel[configTabelName] = new Dictionary<string, List<string>>();
                        }

                        if (!groupedByConfigTabel[configTabelName].ContainsKey(normalizedType))
                        {
                            groupedByConfigTabel[configTabelName][normalizedType] = new List<string>();
                        }

                        groupedByConfigTabel[configTabelName][normalizedType].Add(channelName);
                    }
                }

                // 构建树结构
                foreach (var kvp in groupedByConfigTabel.OrderBy(x => x.Key))
                {
                    string configTabelName = kvp.Key;
                    var channelsByType = kvp.Value;

                    // 创建配置表节点
                    var configTabelNode = new ChannelTreeNode
                    {
                        DisplayName = configTabelName,
                        NodeType = "ConfigTabel",
                        Tag = configTabelName,
                        IsExpanded = true  // 配置表节点默认展开
                    };

                    // 定义通道类型的显示顺序，RO单独显示
                    var channelTypeOrder = new[] { "DI", "DO", "AI", "AO", "RO" };

                    // 按通道类型创建子节点
                    foreach (var type in channelTypeOrder)
                    {
                        if (channelsByType.ContainsKey(type) && channelsByType[type].Count > 0)
                        {
                            // 创建通道类型节点
                            var channelTypeNode = new ChannelTreeNode
                            {
                                DisplayName = type,
                                NodeType = "ChannelType",
                                Tag = $"{configTabelName}:{type}",
                                IsExpanded = true  // 通道类型节点默认展开
                            };

                            // 为该类型下的所有通道创建子节点
                            var orderedChannels = channelsByType[type]
                                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase);

                            foreach (var channelName in orderedChannels)
                            {
                                var channelNode = new ChannelTreeNode
                                {
                                    DisplayName = channelName,
                                    NodeType = "Channel",
                                    Tag = $"{configTabelName}:{channelName}",
                                    IsExpanded = false
                                };
                                channelTypeNode.Children.Add(channelNode);
                            }

                            configTabelNode.Children.Add(channelTypeNode);
                        }
                    }

                    // 只添加有通道的配置表节点
                    if (configTabelNode.Children.Count > 0)
                    {
                        BoundChannelTreeRoot.Add(configTabelNode);
                    }
                }
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// 切换树节点展开/收起状态
        /// </summary>
        private void OnToggleTreeNode(ChannelTreeNode node)
        {
            if (node != null)
            {
                // 清除所有节点的选中状态（需要同时清除两个树）
                ClearAllNodeSelection(ChannelBindingTreeRoot);
                ClearAllNodeSelection(BoundChannelTreeRoot);
                
                // 设置当前节点为选中状态
                node.IsSelected = true;
                
                // 切换展开/折叠状态
                node.IsExpanded = !node.IsExpanded;
            }
        }

        /// <summary>
        /// 递归清除所有树节点的选中状态
        /// </summary>
        private void ClearAllNodeSelection(ObservableCollection<ChannelTreeNode> nodes)
        {
            if (nodes == null) return;
            
            foreach (var node in nodes)
            {
                node.IsSelected = false;
                if (node.Children != null && node.Children.Count > 0)
                {
                    ClearAllNodeSelection(node.Children);
                }
            }
        }

        /// <summary>
        /// 从树节点添加信号
        /// </summary>
        private void OnAddSignalFromTree(ChannelTreeNode node)
        {
            if (node == null || node.NodeType != "Channel")
            {
                return;
            }

            try
            {
                // 从Tag中提取通道信息（格式："配置表名:通道名称"）
                string channelInfo = node.Tag as string;
                if (string.IsNullOrEmpty(channelInfo))
                {
                    return;
                }

                // 解析通道信息
                var channelParts = channelInfo.Split(new[] { ':' }, 2);
                if (channelParts.Length != 2)
                {
                    return;
                }

                string configTabelName = channelParts[0];
                string channelName = channelParts[1];

                // 确定信号类型（根据通道类型：AI/AO/RO为模拟量，DI/DO为数字量）
                string signalType = null;
                string channelPrefix = new string(channelName.TakeWhile(c => !char.IsDigit(c)).ToArray());
                if (channelPrefix == "AI" || channelPrefix == "AO" || channelPrefix == "RO")
                {
                    signalType = "模拟量";
                }
                else if (channelPrefix == "DI" || channelPrefix == "DO")
                {
                    signalType = "数字量";
                }

                // 创建对话框并预填充数据
                var viewModel = new AddSignalDialogViewModel(TestTaskName, ChassisName);
                
                // 预填充信号类型和硬件通道
                if (!string.IsNullOrEmpty(signalType))
                {
                    viewModel.SelectedSignalType = signalType;
                }
                viewModel.SelectedActualChannel = channelInfo;

                var dialog = new AddSignalDialog(viewModel);
                dialog.ShowDialog();

                if (dialog.SignalResult != null)
                {
                    var newSignal = dialog.SignalResult;
                    // 设置序号
                    newSignal.Index = Signals.Count + 1;
                    Signals.Add(newSignal);

                    // 重新计算所有信号的序号
                    UpdateSignalIndices();

                    // 标记项目为已修改
                    _eventAggregator.GetEvent<Events.ProjectModifiedEvent>().Publish(new Events.ProjectModifiedEventArgs
                    {
                        ModificationType = "SignalTabel",
                        Description = $"添加信号: {newSignal.SignalName}"
                    });

                    // 通知板卡配置界面刷新Data_info
                    _eventAggregator.GetEvent<Events.SignalTabelChangedEvent>().Publish(new Events.SignalTabelChangedEventArgs
                    {
                        ChangeType = "Added",
                        SignalName = newSignal.SignalName,
                        ActualChannel = newSignal.ActualChannel
                    });
                }
            }
            catch (Exception)
            {
            }
        }

        #endregion

        #region Command Handlers

        private void OnAddSignal()
        {
            try
            {
                var viewModel = new AddSignalDialogViewModel(TestTaskName, ChassisName);
                var dialog = new AddSignalDialog(viewModel);
                dialog.ShowDialog();

                if (dialog.SignalResult != null)
                {
                    var newSignal = dialog.SignalResult;
                    // 设置序号
                    newSignal.Index = Signals.Count + 1;
                    Signals.Add(newSignal);

                    // 重新计算所有信号的序号
                    UpdateSignalIndices();

                    // 标记项目为已修改
                    _eventAggregator.GetEvent<Events.ProjectModifiedEvent>().Publish(new Events.ProjectModifiedEventArgs
                    {
                        ModificationType = "SignalTabel",
                        Description = $"添加信号: {newSignal.SignalName}"
                    });

                    // 通知板卡配置界面刷新Data_info
                    _eventAggregator.GetEvent<Events.SignalTabelChangedEvent>().Publish(new Events.SignalTabelChangedEventArgs
                    {
                        ChangeType = "Added",
                        SignalName = newSignal.SignalName,
                        ActualChannel = newSignal.ActualChannel
                    });
                }
            }
            catch (Exception)
            {
            }
        }

        private void OnDeleteSignal(SignalConfigItem signal)
        {
            if (signal != null)
            {
                string signalName = signal.SignalName;
                
                // 显示确认删除对话框
                var result = ReMessageBox.Show(
                    $"确定要删除信号 '{signalName}' 吗？",
                    "确认删除",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Question);
                
                if (result == System.Windows.MessageBoxResult.Yes)
                {
                    string actualChannel = signal.ActualChannel;
                    Signals.Remove(signal);
                    // 重新计算所有信号的序号
                    UpdateSignalIndices();
                    
                    // 标记项目为已修改
                    _eventAggregator.GetEvent<Events.ProjectModifiedEvent>().Publish(new Events.ProjectModifiedEventArgs
                    {
                        ModificationType = "SignalTabel",
                        Description = $"删除信号: {signalName}"
                    });

                    // 通知板卡配置界面刷新Data_info
                    _eventAggregator.GetEvent<Events.SignalTabelChangedEvent>().Publish(new Events.SignalTabelChangedEventArgs
                    {
                        ChangeType = "Removed",
                        SignalName = signalName,
                        ActualChannel = actualChannel
                    });
                }
            }
        }

        private void OnEditSignal(SignalConfigItem signal)
        {
            if (signal != null)
            {
                try
                {
                    // 创建编辑模式的 ViewModel，传入当前信号数据
                    var viewModel = new AddSignalDialogViewModel(signal, TestTaskName, ChassisName);
                    
                    var dialog = new AddSignalDialog(viewModel);
                    dialog.ShowDialog();

                    if (dialog.SignalResult != null)
                    {
                        var editedSignal = dialog.SignalResult;
                        // 更新信号数据
                        signal.SignalType = editedSignal.SignalType;
                        signal.SignalName = editedSignal.SignalName;
                        signal.ActualChannel = editedSignal.ActualChannel;
                        signal.RawValueUnit = editedSignal.RawValueUnit;
                        signal.RealTimeValueUnit = editedSignal.RealTimeValueUnit;
                        signal.Remarks = editedSignal.Remarks;
                        
                        // 保存到内存并标记项目为已修改
                        SaveSignalsToMemory();
                        _eventAggregator.GetEvent<Events.ProjectModifiedEvent>().Publish(new Events.ProjectModifiedEventArgs
                        {
                            ModificationType = "SignalTabel",
                            Description = $"编辑信号: {signal.SignalName}"
                        });

                        // 通知板卡配置界面刷新Data_info
                        _eventAggregator.GetEvent<Events.SignalTabelChangedEvent>().Publish(new Events.SignalTabelChangedEventArgs
                        {
                            ChangeType = "Modified",
                            SignalName = signal.SignalName,
                            ActualChannel = signal.ActualChannel
                        });
                    }
                }
                catch (Exception)
                {
                }
            }
        }

        /// <summary>
        /// 信号标定命令处理
        /// </summary>
        private void OnCalibration(SignalConfigItem signal)
        {
            if (signal == null)
                return;

            try
            {
                // 创建标定对话框
                var viewModel = new SignalCalibrationDialogViewModel(signal);
                var dialog = new Views.Dialogs.SignalCalibrationDialog(viewModel);

                // 显示对话框
                var result = dialog.ShowDialog();

                if (result == true && dialog.CalibrationResult != null)
                {
                    // 应用标定参数
                    signal.Slope = dialog.CalibrationResult.Slope;
                    signal.Intercept = dialog.CalibrationResult.Intercept;
                    signal.IsCalibrated = dialog.CalibrationResult.IsCalibrated;

                    // 重新计算实时值
                    signal.ApplyCalibration();

                    // 保存到内存
                    SaveSignalsToMemory();

                    // 标记项目为已修改
                    _eventAggregator.GetEvent<Events.ProjectModifiedEvent>().Publish(
                        new Events.ProjectModifiedEventArgs());
                }
            }
            catch (Exception ex)
            {
                ReMessageBox.Show($"标定失败: {ex.Message}", "错误",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private void OnWaveform(SignalConfigItem signal)
        {
            if (signal == null || string.IsNullOrEmpty(signal.ActualChannel))
            {
                ReMessageBox.Show("请先为信号配置硬件通道", "提示", 
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            try
            {
                // 解析 ActualChannel，格式为"配置表名:通道名称"
                var channelParts = signal.ActualChannel.Split(new[] { ':' }, 2);
                if (channelParts.Length != 2)
                {
                    ReMessageBox.Show("通道格式错误，无法打开配置界面", "错误",
                        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                    return;
                }

                string configTabelName = channelParts[0];
                string channelName = channelParts[1];

                // 从通道配置表获取通道的板卡信息
                var allChannelTabelItems = ChannelConfigTabelViewModel.GetAllChannelTabelItems();
                if (allChannelTabelItems == null || allChannelTabelItems.Count == 0)
                {
                    ReMessageBox.Show("未找到通道配置表数据", "错误",
                        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                    return;
                }

                // 查找对应的通道配置表项
                ChannelTabelItem channelTabelItem = null;
                string channelTabelKey = $"{TestTaskName}/{configTabelName}";
                
                if (allChannelTabelItems.TryGetValue(channelTabelKey, out var channelItems))
                {
                    channelTabelItem = channelItems?.FirstOrDefault(c => c.ChannelName == channelName);
                }

                if (channelTabelItem == null || string.IsNullOrEmpty(channelTabelItem.ChassisName) || string.IsNullOrEmpty(channelTabelItem.CardName))
                {
                    ReMessageBox.Show("未找到通道对应的板卡信息，请先配置通道", "错误",
                        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                    return;
                }

                string chassisName = channelTabelItem.ChassisName;
                string cardName = channelTabelItem.CardName;

                // 导航到PXI机箱界面，传递配置参数（打开配置界面）
                var navParamsDict = new Dictionary<string, object>
                {
                    { "ChassisName", chassisName },
                    { "CardName", cardName },
                    { "ChannelName", channelName },
                    { "SignalName", signal.SignalName },
                    { "ConfigTabelName", configTabelName },
                    { "IsWaveformNavigation", true }
                };

                // 将字典转换为NavigationParameters
                var navParams = new NavigationParameters();
                foreach (var param in navParamsDict)
                {
                    navParams.Add(param.Key, param.Value);
                }

                // 使用导航服务导航到PXI机箱界面
                var navigationService = _regionManager.Regions[AppConstants.MainRegionName].NavigationService;
                if (navigationService != null)
                {
                    navigationService.RequestNavigate(new Uri("PxiChassis", UriKind.Relative), navParams);
                }
                else
                {
                    ReMessageBox.Show("导航服务不可用", "错误",
                        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                ReMessageBox.Show($"打开配置界面失败: {ex.Message}", "错误",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 浮动窗口 - 将当前视图弹出到独立窗口
        /// </summary>
        private void OnFloatWindow()
        {
            // 需要从View中获取CenterRegion的Border
            // 这个逻辑需要在View的CodeBehind中实现
            ReMessageBox.Show("浮动功能需要在View中实现，请绑定到View的事件");
        }

        /// <summary>
        /// 最小化 - 在区域内隐藏（博图风格）
        /// </summary>
        private void OnMinimizeInRegion()
        {
            ReMessageBox.Show("最小化功能待实现");
        }

        /// <summary>
        /// 关闭 - 从区域中移除视图
        /// </summary>
        /// <summary>
        /// 导航到项目树中对应的节点（双击配置表时触发）
        /// </summary>
        private void OnNavigateToProjectTree()
        {
            try
            {
                // 发布事件，选中项目树中对应的配置表节点
                _eventAggregator.GetEvent<Events.SelectProjectItemEvent>().Publish(
                    new Events.SelectProjectItemEventArgs
                    {
                        TestTaskName = TestTaskName,
                        ConfigTabelName = ConfigTabelName,
                        ConfigTabelType = "signal_config_tabel",
                        TriggerDoubleClick = true
                    });
            }
            catch (Exception)
            {
                // 导航失败时记录错误，但不显示错误消息（避免干扰用户）
            }
        }

        private void OnCloseInRegion()
        {
            var result = ReMessageBox.Show("确定要关闭当前配置表吗？", "确认", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
            if (result == System.Windows.MessageBoxResult.Yes)
            {
                // 构建完整的pageKey: SignalConfigTabel_任务名-配置表名
                string pageKey = $"SignalConfigTabel_{TestTaskName}-{ConfigTabelName}";
                
                // 传递完整的pageKey，这样MainWindowViewModel可以正确识别和关闭该页面
                _eventAggregator.GetEvent<Events.ReleaseCurrentPageEvent>().Publish(pageKey);
            }
        }

        private void UpdatePagination()
        {
            UpdatePagedSignals();
            UpdatePaginationInfo();
            UpdatePageNumbers();
            ((DelegateCommand)PreviousPageCommand).RaiseCanExecuteChanged();
            ((DelegateCommand)NextPageCommand).RaiseCanExecuteChanged();
            RaisePropertyChanged(nameof(TotalPages));
        }

        private void UpdatePagedSignals()
        {
            if (PagedSignals == null)
            {
                PagedSignals = new ObservableCollection<SignalConfigItem>();
            }

            PagedSignals.Clear();

            if (Signals == null || Signals.Count == 0)
            {
                for (int i = 0; i < PageSize; i++)
                {
                    PagedSignals.Add(new SignalConfigItem { IsEmpty = true });
                }
                return;
            }

            int startIndex = (CurrentPage - 1) * PageSize;
            int endIndex = Math.Min(startIndex + PageSize, Signals.Count);

            for (int i = startIndex; i < endIndex; i++)
            {
                var signal = Signals[i];
                if (signal != null)
                {
                    signal.IsEmpty = false;
                }
                PagedSignals.Add(signal);
            }

            while (PagedSignals.Count < PageSize)
            {
                PagedSignals.Add(new SignalConfigItem { IsEmpty = true });
            }
        }

        private void UpdatePaginationInfo()
        {
            PaginationInfo = PaginationHelper.GetPaginationInfo(Signals?.Count ?? 0, CurrentPage, PageSize);
        }

        private void UpdatePageNumbers()
        {
            if (PageNumbers == null) PageNumbers = new ObservableCollection<PaginationButtonInfo>();
            PaginationHelper.UpdatePageNumbers(PageNumbers, TotalPages, CurrentPage, OnGoToPage);
        }

        private void OnGoToPage(int page)
        {
            if (page >= 1 && page <= TotalPages)
            {
                CurrentPage = page;
            }
        }

        private void OnPreviousPage()
        {
            if (CurrentPage > 1)
            {
                CurrentPage--;
            }
        }

        private bool CanGoToPreviousPage()
        {
            return CurrentPage > 1;
        }

        private void OnNextPage()
        {
            if (CurrentPage < TotalPages)
            {
                CurrentPage++;
            }
        }

        private bool CanGoToNextPage()
        {
            return CurrentPage < TotalPages;
        }

        /// <summary>
        /// 标准化通道类型名称
        /// 将各种可能的通道类型表示标准化为统一的简称
        /// </summary>
        /// <param name="channelType">原始通道类型</param>
        /// <returns>标准化的通道类型简称</returns>
        private string NormalizeChannelType(string channelType)
        {
            if (string.IsNullOrEmpty(channelType))
                return "其他";

            // 转换为大写以便匹配
            string upperType = channelType.ToUpper();

            // 匹配数字输入类型
            if (upperType.Contains("DI") || upperType.Contains("DIGITAL INPUT") || upperType.Contains("数字输入"))
                return "DI";

            // 匹配数字输出类型
            if (upperType.Contains("DO") || upperType.Contains("DIGITAL OUTPUT") || upperType.Contains("数字输出"))
                return "DO";

            // 匹配模拟输入类型
            if (upperType.Contains("AI") || upperType.Contains("ANALOG INPUT") || upperType.Contains("模拟输入"))
                return "AI";

            // 匹配模拟输出类型
            if (upperType.Contains("AO") || upperType.Contains("ANALOG OUTPUT") || upperType.Contains("模拟输出"))
                return "AO";

            // 匹配电阻输出类型
            if (upperType.Contains("RO") || upperType.Contains("Resist Output") || upperType.Contains("电阻输出"))
                return "RO";

            // 其他类型保持原样，但限制长度
            return channelType.Length > 10 ? channelType.Substring(0, 10) + "..." : channelType;
        }

        #endregion
    }
}
