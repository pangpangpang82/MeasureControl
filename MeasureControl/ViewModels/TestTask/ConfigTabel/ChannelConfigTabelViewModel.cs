using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using MeasureControl.Constants;
using MeasureControl.Drivers;
using MeasureControl.Helpers;
using MeasureControl.Models;
using MeasureControl.Models.Devices;
using MeasureControl.Services;
using MeasureControl.ViewModels.Dialogs;
using MeasureControl.Views;
using MeasureControl.Views.Dialogs;
using Prism.Commands;
using Prism.Events;
using Prism.Mvvm;
using Prism.Regions;

namespace MeasureControl.ViewModels
{
    /// <summary>
    /// 通道配置表的ViewModel
    /// 这是一个通用的ViewModel，通过导航参数接收不同的配置表数据
    /// </summary>
    public class ChannelConfigTabelViewModel : BindableBase, INavigationAware, IDisposable
    {
        private readonly IRegionManager _regionManager;
        private readonly IEventAggregator _eventAggregator;
        private readonly ProjectService _projectService;
        private readonly IPxiChassisService _pxiChassisService;
        
        // 用于存储所有通道配置表数据的静态字典（key格式：测试任务名/配置表名）
        private static Dictionary<string, ObservableCollection<ChannelTabelItem>> _allChannelTabelItems = new Dictionary<string, ObservableCollection<ChannelTabelItem>>();

        // 用于同步访问静态字典的锁对象
        private static readonly object _allChannelTabelItemsLock = new object();

        // 用于存储通道树节点的展开状态（key格式：机箱名/测试任务名/配置表名/节点路径）
        private static Dictionary<string, Dictionary<string, bool>> _treeNodeExpandedStates = new Dictionary<string, Dictionary<string, bool>>();

        // 用于同步访问树节点展开状态的锁对象
        private static readonly object _treeNodeExpandedStatesLock = new object();
        
        /// <summary>获取所有通道配置表数据</summary>
        public static Dictionary<string, List<ChannelTabelItem>> GetAllChannelTabelItems()
        {
            lock (_allChannelTabelItemsLock)
            {
                return _allChannelTabelItems.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value?.Where(c => !c.IsEmpty).Select(c => { var clone = c.Clone(); clone.IsEmpty = false; return clone; }).ToList() 
                           ?? new List<ChannelTabelItem>());
            }
        }

        /// <summary>清空所有通道配置表数据</summary>
        public static void ClearAllChannelTabelItems()
        {
            lock (_allChannelTabelItemsLock) { _allChannelTabelItems.Clear(); }
        }

        /// <summary>获取树节点的展开状态</summary>
        private bool GetTreeNodeExpandedState(string nodeKey)
        {
            lock (_treeNodeExpandedStatesLock)
            {
                string configKey = GetConfigKey();
                if (_treeNodeExpandedStates.TryGetValue(configKey, out var states))
                {
                    if (states.TryGetValue(nodeKey, out var expanded))
                    {
                        return expanded;
                    }
                }

                // 如果没有保存的状态，则机箱节点默认展开，其他节点默认收起
                // 机箱节点的nodeKey就是机箱名称，不包含路径分隔符
                return !nodeKey.Contains("/");
            }
        }

        /// <summary>设置树节点的展开状态</summary>
        private void SetTreeNodeExpandedState(string nodeKey, bool isExpanded)
        {
            lock (_treeNodeExpandedStatesLock)
            {
                string configKey = GetConfigKey();
                if (!_treeNodeExpandedStates.TryGetValue(configKey, out var states))
                {
                    states = new Dictionary<string, bool>();
                    _treeNodeExpandedStates[configKey] = states;
                }
                states[nodeKey] = isExpanded;
            }
        }

        /// <summary>生成配置表的唯一键</summary>
        private string GetConfigKey()
        {
            return $"{ChassisName ?? "Default"}/{TestTaskName ?? "Default"}/{ConfigTabelName ?? "Default"}";
        }

        /// <summary>生成树节点的唯一键</summary>
        private string GetNodeKey(ChannelTreeNode node, string parentPath = "")
        {
            string currentPath = string.IsNullOrEmpty(parentPath)
                ? node.DisplayName
                : $"{parentPath}/{node.DisplayName}";
            return currentPath;
        }

        /// <summary>保存树节点的展开状态</summary>
        private void SaveTreeNodeExpandedState(ChannelTreeNode node)
        {
            string nodeKey = GetNodePath(node);
            SetTreeNodeExpandedState(nodeKey, node.IsExpanded);
        }

        /// <summary>获取节点的完整路径</summary>
        private string GetNodePath(ChannelTreeNode node)
        {
            var pathParts = new List<string>();
            var current = node;

            // 向上遍历构建路径
            while (current != null)
            {
                pathParts.Insert(0, current.DisplayName);
                current = FindParentNode(current);
            }

            return string.Join("/", pathParts);
        }

        /// <summary>保存所有树节点的展开状态</summary>
        private void SaveAllTreeNodeExpandedStates()
        {
            foreach (var rootNode in ChannelTreeRoot)
            {
                SaveTreeNodeExpandedStatesRecursive(rootNode);
            }
        }

        /// <summary>递归保存树节点的展开状态</summary>
        private void SaveTreeNodeExpandedStatesRecursive(ChannelTreeNode node)
        {
            // 保存当前节点的展开状态
            string nodeKey = GetNodePath(node);
            SetTreeNodeExpandedState(nodeKey, node.IsExpanded);

            // 递归处理子节点
            foreach (var child in node.Children)
            {
                SaveTreeNodeExpandedStatesRecursive(child);
            }
        }

        /// <summary>加载通道配置表数据到静态字典</summary>
        public static void LoadChannelTabelItems(Dictionary<string, List<ChannelTabelItem>> items)
        {
            lock (_allChannelTabelItemsLock)
            {
                _allChannelTabelItems.Clear();
                if (items == null) return;
                foreach (var kvp in items)
                    _allChannelTabelItems[kvp.Key] = new ObservableCollection<ChannelTabelItem>(
                        kvp.Value?.Where(c => c != null).Select(c => c.Clone()) ?? Enumerable.Empty<ChannelTabelItem>());
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

        private ObservableCollection<ChannelTabelItem> _channels;
        /// <summary>
        /// 通道配置列表
        /// </summary>
        public ObservableCollection<ChannelTabelItem> Channels
        {
            get => _channels;
            set
            {
                if (_channels != null)
                {
                    _channels.CollectionChanged -= Channels_CollectionChanged;
                }
                SetProperty(ref _channels, value);
                if (_channels != null)
                {
                    _channels.CollectionChanged += Channels_CollectionChanged;
                }
                UpdatePagination();
            }
        }

        private ObservableCollection<ChannelTreeNode> _channelTreeRoot;
        /// <summary>
        /// 通道树根节点集合（每个机箱是一个根节点）
        /// </summary>
        public ObservableCollection<ChannelTreeNode> ChannelTreeRoot
        {
            get => _channelTreeRoot;
            set => SetProperty(ref _channelTreeRoot, value);
        }

        private const int PageSize = 14;
        private int _currentPage = 1;

        /// <summary>
        /// 当前页码（从1开始）
        /// </summary>
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

        /// <summary>
        /// 总页数
        /// </summary>
        public int TotalPages
        {
            get
            {
                if (Channels == null || Channels.Count == 0)
                    return 1;
                return (int)Math.Ceiling((double)Channels.Count / PageSize);
            }
        }

        private ObservableCollection<ChannelTabelItem> _pagedChannels;
        /// <summary>
        /// 当前页显示的通道列表
        /// </summary>
        public ObservableCollection<ChannelTabelItem> PagedChannels
        {
            get => _pagedChannels;
            set => SetProperty(ref _pagedChannels, value);
        }

        private string _paginationInfo;
        /// <summary>
        /// 分页信息文本
        /// </summary>
        public string PaginationInfo
        {
            get => _paginationInfo;
            set => SetProperty(ref _paginationInfo, value);
        }

        private ObservableCollection<PaginationButtonInfo> _pageNumbers;
        /// <summary>
        /// 分页按钮信息列表
        /// </summary>
        public ObservableCollection<PaginationButtonInfo> PageNumbers
        {
            get => _pageNumbers;
            set => SetProperty(ref _pageNumbers, value);
        }

        #endregion

        #region Commands

        public DelegateCommand AddChannelCommand { get; }
        public DelegateCommand<ChannelTabelItem> DeleteChannelCommand { get; }
        public DelegateCommand<ChannelTabelItem> EditChannelCommand { get; }
        public DelegateCommand CloseInRegionCommand { get; }
        public DelegateCommand<ChannelTreeNode> ToggleTreeNodeCommand { get; }
        public DelegateCommand<ChannelTreeNode> AddChannelFromTreeCommand { get; }
        public DelegateCommand PreviousPageCommand { get; }
        public DelegateCommand NextPageCommand { get; }
        public DelegateCommand NavigateToProjectTreeCommand { get; }

        #endregion

        #region Constructor

        public ChannelConfigTabelViewModel(
            IRegionManager regionManager,
            IEventAggregator eventAggregator,
            ProjectService projectService,
            IPxiChassisService pxiChassisService)
        {
            _regionManager = regionManager ?? throw new ArgumentNullException(nameof(regionManager));
            _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
            _projectService = projectService ?? throw new ArgumentNullException(nameof(projectService));
            _pxiChassisService = pxiChassisService ?? throw new ArgumentNullException(nameof(pxiChassisService));

            // 初始化命令
            AddChannelCommand = new DelegateCommand(OnAddChannel);
            DeleteChannelCommand = new DelegateCommand<ChannelTabelItem>(OnDeleteChannel);
            EditChannelCommand = new DelegateCommand<ChannelTabelItem>(OnEditChannel);
            CloseInRegionCommand = new DelegateCommand(OnCloseInRegion);
            ToggleTreeNodeCommand = new DelegateCommand<ChannelTreeNode>(OnToggleTreeNode);
            AddChannelFromTreeCommand = new DelegateCommand<ChannelTreeNode>(OnAddChannelFromTree);
            PreviousPageCommand = new DelegateCommand(OnPreviousPage, CanGoToPreviousPage);
            NextPageCommand = new DelegateCommand(OnNextPage, CanGoToNextPage);
            NavigateToProjectTreeCommand = new DelegateCommand(OnNavigateToProjectTree);

            // 初始化集合
            Channels = new ObservableCollection<ChannelTabelItem>();
            ChannelTreeRoot = new ObservableCollection<ChannelTreeNode>();
            PagedChannels = new ObservableCollection<ChannelTabelItem>();
            PageNumbers = new ObservableCollection<PaginationButtonInfo>();
            
            // 注意：通道树在 OnNavigatedTo 中构建，此时 TestTaskName 已设置
            
            // 初始化分页
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
                DisplayPath = null;
            }
            
            // 订阅设备修改事件
            _eventAggregator.GetEvent<Events.DeviceModifiedEvent>().Subscribe(OnDeviceModified, ThreadOption.UIThread);
            
            // 订阅通道数据加载事件
            _eventAggregator.GetEvent<Events.ChannelTabelItemsLoadEvent>().Subscribe(OnChannelTabelItemsLoad, ThreadOption.UIThread);
            
            // 订阅通道数据请求事件
            _eventAggregator.GetEvent<Events.ChannelTabelItemsRequestEvent>().Subscribe(OnChannelTabelItemsRequest, ThreadOption.UIThread);
            
            // 订阅通道配置变化事件（使能状态变化时刷新通道树）
            _eventAggregator.GetEvent<Events.ChannelConfigChangedEvent>().Subscribe(OnChannelConfigChanged, ThreadOption.UIThread);
            
            // 订阅通道使能变化事件（板卡配置面板使能状态变化时刷新通道树）
            _eventAggregator.GetEvent<Events.ChannelEnableChangedEvent>().Subscribe(OnChannelEnableChanged, ThreadOption.UIThread);
            
            // 加载配置表数据
            LoadConfigTabelData();
            
            // 重新构建通道树（确保获取最新数据）
            BuildChannelTree();
        }

        public bool IsNavigationTarget(NavigationContext navigationContext)
        {
            // 每次创建新实例，支持多个相同类型页面
            return false;
        }

        public void OnNavigatedFrom(NavigationContext navigationContext)
        {
            // 保存通道数据到内存（离开页面时保存）
            SaveChannelsToMemory();

            // 保存树节点展开状态
            SaveAllTreeNodeExpandedStates();

            // 取消订阅事件
            _eventAggregator.GetEvent<Events.DeviceModifiedEvent>().Unsubscribe(OnDeviceModified);
            _eventAggregator.GetEvent<Events.ChannelTabelItemsLoadEvent>().Unsubscribe(OnChannelTabelItemsLoad);
            _eventAggregator.GetEvent<Events.ChannelTabelItemsRequestEvent>().Unsubscribe(OnChannelTabelItemsRequest);
            _eventAggregator.GetEvent<Events.ChannelConfigChangedEvent>().Unsubscribe(OnChannelConfigChanged);
            _eventAggregator.GetEvent<Events.ChannelEnableChangedEvent>().Unsubscribe(OnChannelEnableChanged);
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
                // 保存通道数据到内存
                SaveChannelsToMemory();
                
                // 取消订阅事件
                _eventAggregator.GetEvent<Events.DeviceModifiedEvent>().Unsubscribe(OnDeviceModified);
                _eventAggregator.GetEvent<Events.ChannelTabelItemsLoadEvent>().Unsubscribe(OnChannelTabelItemsLoad);
                _eventAggregator.GetEvent<Events.ChannelTabelItemsRequestEvent>().Unsubscribe(OnChannelTabelItemsRequest);
                _eventAggregator.GetEvent<Events.ChannelConfigChangedEvent>().Unsubscribe(OnChannelConfigChanged);
                
                // 清理资源
                _disposed = true;
            }
        }
        
        /// <summary>
        /// 处理设备修改事件
        /// </summary>
        private void OnDeviceModified(Events.DeviceModifiedEventArgs args)
        {
            // 重新构建通道树
            BuildChannelTree();
        }
        
        /// <summary>
        /// 处理通道配置变化事件（使能状态变化时刷新通道树）
        /// </summary>
        private void OnChannelConfigChanged(Events.ChannelConfigChangedEventArgs args)
        {
            // 重新构建通道树以反映使能状态变化
            BuildChannelTree();
        }

        /// <summary>
        /// 处理通道使能变化事件（板卡配置面板使能状态变化时刷新通道树）
        /// </summary>
        private void OnChannelEnableChanged(Events.ChannelEnableChangedEventArgs args)
        {
            System.Diagnostics.Debug.WriteLine($"[ChannelConfigTabel] 收到通道使能变化事件: DeviceId={args.DeviceId}, CardName={args.CardName}");
            // 重新构建通道树以反映使能状态变化
            BuildChannelTree();
        }
        
        /// <summary>
        /// 处理通道数据加载事件
        /// 注意：数据已经通过静态方法LoadChannelTabelItems加载到静态字典中
        /// 这个方法只需要更新当前打开的页面数据
        /// </summary>
        private void OnChannelTabelItemsLoad(Events.ChannelTabelItemsLoadEventArgs args)
        {
            // 如果当前页面的TestTaskName和ConfigTabelName已设置，从静态字典加载对应的数据
            if (!string.IsNullOrEmpty(TestTaskName) && !string.IsNullOrEmpty(ConfigTabelName))
            {
                string key = GetChannelTabelKey();
                ObservableCollection<ChannelTabelItem> savedChannels = null;
                lock (_allChannelTabelItemsLock)
                {
                    if (_allChannelTabelItems.ContainsKey(key))
                    {
                        savedChannels = _allChannelTabelItems[key];
                    }
                }
                
                if (savedChannels != null)
                {
                    if (Channels == null)
                    {
                        Channels = new ObservableCollection<ChannelTabelItem>();
                    }
                    Channels.Clear();
                    foreach (var channel in savedChannels)
                    {
                        // 创建新实例，避免引用问题
                        var newChannel = new ChannelTabelItem
                        {
                            Index = channel.Index,
                            ChannelName = channel.ChannelName,
                            CardName = channel.CardName,
                            ChassisName = channel.ChassisName,
                            Remarks = channel.Remarks,
                            ChannelType = channel.ChannelType,
                            InputOutputType = channel.InputOutputType,
                            AssociatedChannel = channel.AssociatedChannel,
                            IsEmpty = channel.IsEmpty
                        };
                        Channels.Add(newChannel);
                    }
                    UpdatePagination();
                }
            }
        }
        
        /// <summary>
        /// 处理通道数据请求事件
        /// 从静态字典中获取所有通道配置表数据，确保所有数据都被保存
        /// </summary>
        private void OnChannelTabelItemsRequest(Events.ChannelTabelItemsRequestEventArgs args)
        {
            if (args == null)
                return;

            // 初始化结果字典
            if (args.ChannelTabelItems == null)
            {
                args.ChannelTabelItems = new Dictionary<string, List<ChannelTabelItem>>();
            }

            // 如果当前页面有TestTaskName和ConfigTabelName，先保存当前页面的数据到静态字典
            if (!string.IsNullOrEmpty(TestTaskName) && !string.IsNullOrEmpty(ConfigTabelName))
            {
                SaveChannelsToMemory();
            }

            // 从静态字典中获取所有通道配置表数据
            // 由于可能有多个实例响应事件，需要确保数据被正确合并
            // 使用lock确保线程安全（虽然通常在UI线程，但为了安全起见）
            lock (_allChannelTabelItemsLock)
            {
                foreach (var kvp in _allChannelTabelItems)
                {
                    // 转换ObservableCollection为List，并排除空行
                    var channelsList = kvp.Value?.Where(c => !c.IsEmpty).Select(c => new ChannelTabelItem
                    {
                        Index = c.Index,
                        ChannelName = c.ChannelName,
                        CardName = c.CardName,
                        ChassisName = c.ChassisName,
                        Remarks = c.Remarks,
                        ChannelType = c.ChannelType,
                        InputOutputType = c.InputOutputType,
                        AssociatedChannel = c.AssociatedChannel,
                        IsEmpty = false
                    }).ToList() ?? new List<ChannelTabelItem>();
                    
                    // 直接覆盖，使用最新的数据（如果多个实例响应，后面的会覆盖前面的，但数据应该是一样的）
                    args.ChannelTabelItems[kvp.Key] = channelsList;
                }
            }
        }
        
        /// <summary>
        /// 获取通道配置表的唯一键（格式：机箱名/测试任务名/配置表名，如果没有机箱名则使用：测试任务名/配置表名）
        /// </summary>
        private string GetChannelTabelKey()
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
        /// 加载配置表数据
        /// </summary>
        private void LoadConfigTabelData()
        {
            // 初始化Channels集合
            if (Channels == null)
            {
                Channels = new ObservableCollection<ChannelTabelItem>();
            }
            Channels.Clear();

            // 从静态字典中加载数据（如果存在）
            string key = GetChannelTabelKey();
            
            if (!string.IsNullOrEmpty(key))
            {
                // 在锁内创建数据的快照，避免在锁外使用引用时数据被修改
                List<ChannelTabelItem> channelsSnapshot = null;
                lock (_allChannelTabelItemsLock)
                {
                    if (_allChannelTabelItems.ContainsKey(key))
                    {
                        var savedChannels = _allChannelTabelItems[key];
                        
                        // 详细检查集合内容
                        if (savedChannels != null)
                        {
                            if (savedChannels.Count > 0)
                            {
                                // 在锁内创建快照，避免锁外数据被修改
                                channelsSnapshot = new List<ChannelTabelItem>();
                                foreach (var ch in savedChannels)
                                {
                                    if (ch != null)
                                    {
                                        channelsSnapshot.Add(new ChannelTabelItem
                                        {
                                            Index = ch.Index,
                                            ChannelName = ch.ChannelName,
                                            CardName = ch.CardName,
                                            ChassisName = ch.ChassisName,
                                            Remarks = ch.Remarks,
                                            ChannelType = ch.ChannelType,
                                            InputOutputType = ch.InputOutputType,
                                            AssociatedChannel = ch.AssociatedChannel,
                                            IsEmpty = ch.IsEmpty
                                        });
                                    }
                                }
                            }
                        }
                    }
                }
                
                // 在锁外使用快照数据
                if (channelsSnapshot != null && channelsSnapshot.Count > 0)
                {
                    foreach (var channel in channelsSnapshot)
                    {
                        Channels.Add(channel);
                    }
                    UpdatePagination();
                }
            }
        }
        
        /// <summary>
        /// 保存通道数据到内存
        /// </summary>
        private void SaveChannelsToMemory()
        {
            if (!string.IsNullOrEmpty(TestTaskName) && !string.IsNullOrEmpty(ConfigTabelName))
            {
                string key = GetChannelTabelKey();
                // 保存当前配置表的数据（排除空行），创建新实例避免引用问题
                var channelsToSave = Channels?.Where(c => !c.IsEmpty).Select(c => new ChannelTabelItem
                {
                    Index = c.Index,
                    ChannelName = c.ChannelName,
                    CardName = c.CardName,
                    ChassisName = c.ChassisName,
                    Remarks = c.Remarks,
                    ChannelType = c.ChannelType,
                    InputOutputType = c.InputOutputType,
                    AssociatedChannel = c.AssociatedChannel,
                    IsEmpty = false
                }).ToList() ?? new List<ChannelTabelItem>();
                
                lock (_allChannelTabelItemsLock)
                {
                    // 如果当前要保存的集合为空，且静态字典中已经有数据，则不要覆盖
                    // 这可以防止在数据加载完成前，空集合覆盖已加载的数据
                    if (channelsToSave.Count == 0 && _allChannelTabelItems.ContainsKey(key))
                    {
                        var existingCollection = _allChannelTabelItems[key];
                        if (existingCollection != null && existingCollection.Count > 0)
                        {
                            return;
                        }
                    }
                    
                    _allChannelTabelItems[key] = new ObservableCollection<ChannelTabelItem>(channelsToSave);
                }
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
                "signal_config" => "变量表",
                "icd_config" => "ICD配置",
                "test_sequence" => "测试序列",
                "report" => "报表模板",
                _ => parentType
            };
        }

        /// <summary>
        /// 构建通道树形结构
        /// </summary>
        private void BuildChannelTree()
        {
            System.Diagnostics.Debug.WriteLine($"[ChannelConfigTabel] BuildChannelTree 开始构建通道树... ChassisName={ChassisName}, TestTaskName={TestTaskName}, ConfigTabelName={ConfigTabelName}");
            ChannelTreeRoot.Clear();

            try
            {
                // 获取所有机箱
                var allChassisList = _pxiChassisService.GetAllChassis();
                if (allChassisList == null || allChassisList.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[ChannelConfigTabel] 没有找到机箱数据");
                    return;
                }
                
                // 如果指定了机箱名称，只遍历该机箱
                IEnumerable<ChassisModel> chassisList = allChassisList;
                if (!string.IsNullOrEmpty(ChassisName))
                {
                    chassisList = allChassisList.Where(c => string.Equals(c.Name, ChassisName, StringComparison.Ordinal)).ToList();
                    System.Diagnostics.Debug.WriteLine($"[ChannelConfigTabel] 过滤后找到 {chassisList.Count()} 个匹配的机箱（机箱名称={ChassisName}）");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[ChannelConfigTabel] 找到 {chassisList.Count()} 个机箱（未指定机箱名称，显示所有机箱）");
                }

                // 遍历每个机箱，创建机箱节点
                foreach (var chassis in chassisList)
                {
                    var chassisNode = new ChannelTreeNode
                    {
                        DisplayName = chassis.Name,
                        NodeType = "Chassis",
                        Tag = chassis,
                        IsExpanded = GetTreeNodeExpandedState(chassis.Name)
                    };

                    // 遍历机箱中的设备，过滤出板卡
                    if (chassis.Devices != null)
                    {
                        var cards = chassis.Devices.Where(d => d.DeviceType == "Card").ToList();
                        System.Diagnostics.Debug.WriteLine($"[ChannelConfigTabel] 机箱 {chassis.Name} 有 {cards.Count} 个板卡");
                        
                        foreach (var card in cards)
                        {
                            ChannelTreeNode cardNode = null;
                            
                            // 如果是1394B板卡，使用特殊的三级结构：板卡 -> 节点0-3 -> 通道
                            if (card is Mil1394BDevice)
                            {
                                // 只添加有通道的板卡
                                if (card.Children == null || card.Children.Count == 0)
                                    continue;
                                
                                // 调试：显示板卡的 CardConfigData 状态和 HashCode
                                System.Diagnostics.Debug.WriteLine($"[ChannelConfigTabel] 板卡 {card.CardName}: ID={card.Id}, HashCode={card.GetHashCode()}");
                                if (card.CardConfigData is Models.DigitalIOCardConfig digitalConfig)
                                {
                                    var enabledDI = digitalConfig.InputChannels?.Count(c => c.IsEnabled) ?? 0;
                                    var enabledDO = digitalConfig.OutputChannels?.Count(c => c.IsEnabled) ?? 0;
                                    System.Diagnostics.Debug.WriteLine($"[ChannelConfigTabel] 板卡 {card.CardName}: DI使能={enabledDI}, DO使能={enabledDO}");
                                }
                                else
                                {
                                    System.Diagnostics.Debug.WriteLine($"[ChannelConfigTabel] 板卡 {card.CardName}: CardConfigData={(card.CardConfigData?.GetType().Name ?? "null")}");
                                }

                                cardNode = new ChannelTreeNode
                                {
                                    DisplayName = !string.IsNullOrEmpty(card.CardName) ? card.CardName : card.Model,
                                    NodeType = "Card",
                                    Tag = card,
                                    IsExpanded = GetTreeNodeExpandedState($"{chassis.Name}/{(!string.IsNullOrEmpty(card.CardName) ? card.CardName : card.Model)}")
                                };

                                Build1394BNodeTree(cardNode, card, chassis.Name);
                                
                                // 只添加有通道的板卡节点
                                if (cardNode.Children.Count == 0)
                                    cardNode = null;
                            }
                            else
                            {
                                // 其他板卡使用CreateCardNode方法（支持电阻输出、离散量IO等特殊处理）
                                cardNode = CreateCardNode(card, chassis.Name);
                            }
                            
                            if (cardNode != null)
                            {
                                chassisNode.Children.Add(cardNode);
                            }
                        }
                    }

                    // 只添加有板卡的机箱节点
                    if (chassisNode.Children.Count > 0)
                    {
                        ChannelTreeRoot.Add(chassisNode);
                    }
                }
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// 构建1394B板卡的三级树形结构：板卡 -> 节点0-3 -> 通道（通道号0-63）
        /// </summary>
        private void Build1394BNodeTree(ChannelTreeNode cardNode, DeviceBase card, string chassisName)
        {
            if (card is Mil1394BDevice card1394)
            {
                var cardConfig = card1394.CardConfigData as Models.Mil1394BCardConfig;
                if (cardConfig == null)
                {
                    System.Diagnostics.Debug.WriteLine($"[ChannelConfigTable] Build1394BNodeTree: 板卡 {card.CardName} 的CardConfigData为null或不是Mil1394BCardConfig类型");
                    return;
                }

                // 获取测试任务配置
                Models.Mil1394BTestTaskConfig taskConfig = null;
                if (!string.IsNullOrEmpty(TestTaskName) && cardConfig.TestTaskConfigs != null)
                {
                    taskConfig = cardConfig.TestTaskConfigs.FirstOrDefault(t => t.TestTaskName == TestTaskName);
                }

                // 获取节点配置列表（优先从测试任务配置中获取）
                var nodeConfigs = taskConfig?.NodeConfigs ?? cardConfig.NodeConfigs;

                if (nodeConfigs == null || nodeConfigs.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[ChannelConfigTable] Build1394BNodeTree: 板卡 {card.CardName} 没有节点配置");
                    return;
                }

                // 遍历节点0-3
                for (uint nodeNum = 0; nodeNum < 4; nodeNum++)
                {
                    var nodeConfig = nodeConfigs.FirstOrDefault(n => n.NodeNumber == nodeNum);
                    if (nodeConfig == null || nodeConfig.AsyncSendConfig == null || nodeConfig.AsyncSendConfig.Count == 0)
                    {
                        continue;
                    }

                    // 创建节点节点
                    var nodeTreeNode = new ChannelTreeNode
                    {
                        DisplayName = $"节点{nodeNum}",
                        NodeType = "Node",
                        IsExpanded = true
                    };

                    // 从AsyncSendConfig中获取通道号（0-63），去重并排序
                    var channels = nodeConfig.AsyncSendConfig
                        .Where(item => item.Channel >= 0 && item.Channel <= 63)
                        .Select(item => item.Channel)
                        .Distinct()
                        .OrderBy(c => c)
                        .ToList();

                    // 为每个通道创建通道节点
                    foreach (var channelNum in channels)
                    {
                        string channelName = $"节点{nodeNum}-通道{channelNum}";
                        var channelNode = new ChannelTreeNode
                        {
                            DisplayName = $"通道{channelNum}",
                            NodeType = "Channel",
                            Tag = new ChannelTabelItem
                            {
                                ChannelName = channelName,
                                CardName = cardNode.DisplayName,
                                ChassisName = chassisName,
                                ChannelType = "通讯通道",
                                InputOutputType = $"节点{nodeNum}",
                                AssociatedChannel = channelName
                            },
                            IsExpanded = false
                        };
                        nodeTreeNode.Children.Add(channelNode);
                    }

                    // 只添加有通道的节点
                    if (nodeTreeNode.Children.Count > 0)
                    {
                        cardNode.Children.Add(nodeTreeNode);
                    }
                }

                System.Diagnostics.Debug.WriteLine($"[ChannelConfigTable] Build1394BNodeTree: 板卡 {card.CardName} 构建了 {cardNode.Children.Count} 个节点");
            }
        }

        /// <summary>
        /// 解析通道范围（从通道组节点）
        /// </summary>
        private System.Collections.Generic.List<string> ParseChannelRange(Models.Devices.DeviceBase channelGroup)
        {
            var channels = new System.Collections.Generic.List<string>();
            
            if (channelGroup == null || string.IsNullOrEmpty(channelGroup.SlotPosition))
                return channels;

            if (channelGroup is SwitchChannelNode)
                return channels;

            string slotPosition = channelGroup.SlotPosition;
            if (string.Equals(slotPosition, "Matrix", StringComparison.OrdinalIgnoreCase))
                return channels;
            
            // 解析格式如 "AI0–AI15", "DI0–DI7", "AO0–AO3"
            if (slotPosition.Contains("–") || slotPosition.Contains("-"))
            {
                string separator = slotPosition.Contains("–") ? "–" : "-";
                var parts = slotPosition.Split(new[] { separator }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2)
                {
                    string prefix = new string(parts[0].TakeWhile(c => !char.IsDigit(c)).ToArray());
                    if (int.TryParse(new string(parts[0].SkipWhile(c => !char.IsDigit(c)).ToArray()), out int start) &&
                        int.TryParse(new string(parts[1].SkipWhile(c => !char.IsDigit(c)).ToArray()), out int end))
                    {
                        for (int i = start; i <= end; i++)
                        {
                            channels.Add($"{prefix}{i}");
                        }
                    }
                }
            }
            else
            {
                // 单个通道
                channels.Add(slotPosition);
            }

            return channels;
        }

        /// <summary>
        /// 检查通道是否使能
        /// 只从测试任务特定的配置检查，不再使用全局配置
        /// </summary>
        private bool IsChannelEnabled(Models.Devices.DeviceBase card, string channelName)
        {
            if (card == null)
                return false;

            // 优先从 CardConfigData 检查
            if (card.CardConfigData != null)
            {
                // 检查模拟量输入板卡（测试任务隔离）
                if (card.CardConfigData is Models.AnalogInputCardConfig inputConfig)
                {
                    if (!string.IsNullOrEmpty(TestTaskName) && inputConfig.TestTaskConfigs != null)
                    {
                        var taskConfig = inputConfig.TestTaskConfigs.FirstOrDefault(t => t.TestTaskName == TestTaskName);
                        if (taskConfig?.Channels != null)
                        {
                            var channelConfig = taskConfig.Channels.FirstOrDefault(c => c.ChannelName == channelName);
                            if (channelConfig != null)
                            {
                                System.Diagnostics.Debug.WriteLine($"[IsChannelEnabled] 模拟量采集 {card.CardName} 通道 {channelName} 使用测试任务 '{TestTaskName}' 配置: IsEnabled={channelConfig.IsEnabled}");
                                return channelConfig.IsEnabled;
                            }
                        }
                    }
                    
                    // 未找到对应测试任务配置时，默认禁用
                    System.Diagnostics.Debug.WriteLine($"[IsChannelEnabled] 模拟量采集 {card.CardName} 通道 {channelName} 未找到测试任务 '{TestTaskName}' 配置，默认禁用");
                    return false;
                }
                // 检查模拟量输出板卡（有测试任务隔离）
                else if (card.CardConfigData is Models.AnalogOutputCardConfig outputConfig)
                {
                    // 如果指定了测试任务，优先从测试任务配置中查找
                    if (!string.IsNullOrEmpty(TestTaskName) && outputConfig.TestTaskConfigs != null)
                    {
                        var taskConfig = outputConfig.TestTaskConfigs.FirstOrDefault(t => t.TestTaskName == TestTaskName);
                        if (taskConfig != null && taskConfig.Channels != null)
                        {
                            var channelConfig = taskConfig.Channels.FirstOrDefault(c => c.ChannelName == channelName);
                            if (channelConfig != null)
                            {
                                System.Diagnostics.Debug.WriteLine($"[IsChannelEnabled] 模拟量输出 {card.CardName} 通道 {channelName} 使用测试任务 '{TestTaskName}' 配置: IsEnabled={channelConfig.IsEnabled}");
                                return channelConfig.IsEnabled;
                            }
                        }
                    }

                    // 未找到对应测试任务配置时，默认禁用
                    System.Diagnostics.Debug.WriteLine($"[IsChannelEnabled] 模拟量输出 {card.CardName} 通道 {channelName} 未找到测试任务 '{TestTaskName}' 配置，默认禁用");
                    return false;
                }
                // 检查离散量板卡（有测试任务隔离）
                else if (card.CardConfigData is Models.DigitalIOCardConfig digitalConfig)
                {
                    // 如果指定了测试任务，优先从测试任务配置中查找
                    if (!string.IsNullOrEmpty(TestTaskName) && digitalConfig.TestTaskConfigs != null)
                    {
                        var taskConfig = digitalConfig.TestTaskConfigs.FirstOrDefault(t => t.TestTaskName == TestTaskName);
                        if (taskConfig != null)
                        {
                            // 检查输入通道
                            var inputChannel = taskConfig.InputChannels?.FirstOrDefault(c => c.ChannelName == channelName);
                            if (inputChannel != null)
                            {
                                System.Diagnostics.Debug.WriteLine($"[IsChannelEnabled] 离散量 {card.CardName} 通道 {channelName} 使用测试任务 '{TestTaskName}' 配置: IsEnabled={inputChannel.IsEnabled}");
                                return inputChannel.IsEnabled;
                            }
                            
                            // 检查输出通道
                            var outputChannel = taskConfig.OutputChannels?.FirstOrDefault(c => c.ChannelName == channelName);
                            if (outputChannel != null)
                            {
                                System.Diagnostics.Debug.WriteLine($"[IsChannelEnabled] 离散量 {card.CardName} 通道 {channelName} 使用测试任务 '{TestTaskName}' 配置: IsEnabled={outputChannel.IsEnabled}");
                                return outputChannel.IsEnabled;
                            }
                        }
                    }

                    // 未找到对应测试任务配置时，默认禁用
                    System.Diagnostics.Debug.WriteLine($"[IsChannelEnabled] 离散量 {card.CardName} 通道 {channelName} 未找到测试任务 '{TestTaskName}' 配置，默认禁用");
                    return false;
                }
                else if (card.CardConfigData is CanCardConfig canConfig)
                {
                    if (!string.IsNullOrEmpty(TestTaskName) && canConfig.TestTaskConfigs != null)
                    {
                        var taskConfig = canConfig.TestTaskConfigs.FirstOrDefault(t => t.TestTaskName == TestTaskName);
                        var taskChannel = taskConfig?.Channels?.FirstOrDefault(c => c.ChannelName == channelName);
                        if (taskChannel != null)
                        {
                            System.Diagnostics.Debug.WriteLine($"[IsChannelEnabled] CAN {card.CardName} 通道 {channelName} 使用测试任务 '{TestTaskName}' 配置: IsEnabled={taskChannel.IsEnabled}");
                            return taskChannel.IsEnabled;
                        }
                    }

                    var globalChannel = canConfig.Channels?.FirstOrDefault(c => c.ChannelName == channelName);
                    if (globalChannel != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"[IsChannelEnabled] CAN {card.CardName} 通道 {channelName} 使用全局配置: IsEnabled={globalChannel.IsEnabled}");
                        return globalChannel.IsEnabled;
                    }

                    System.Diagnostics.Debug.WriteLine($"[IsChannelEnabled] CAN {card.CardName} 通道 {channelName} 未找到配置，默认禁用");
                    return false;
                }
                else if (card.CardConfigData is Models.ResistanceOutputCardConfig resistanceConfig)
                {
                    // 检查电阻输出配置
                    if (!string.IsNullOrEmpty(TestTaskName) && resistanceConfig.TestTaskConfigs != null)
                    {
                        var taskConfig = resistanceConfig.TestTaskConfigs.FirstOrDefault(t => t.TestTaskName == TestTaskName);
                        var taskChannel = taskConfig?.Channels?.FirstOrDefault(c => c.ChannelName == channelName);
                        if (taskChannel != null)
                        {
                            System.Diagnostics.Debug.WriteLine($"[IsChannelEnabled] 电阻输出 {card.CardName} 通道 {channelName} 使用测试任务 '{TestTaskName}' 配置: IsEnabled={taskChannel.IsEnabled}");
                            return taskChannel.IsEnabled;
                        }
                    }

                    System.Diagnostics.Debug.WriteLine($"[IsChannelEnabled] 电阻输出 {card.CardName} 通道 {channelName} 未找到测试任务配置，默认禁用");
                    return false;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[IsChannelEnabled] CardConfigData 类型未知: {card.CardConfigData.GetType().Name}");
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[IsChannelEnabled] 板卡 {card.CardName} 的 CardConfigData 为 null");
            }

            // 默认返回未使能（无配置信息或离线时视为未使能）
            return false;
        }

        #endregion

        #region Command Handlers

        private void OnAddChannel()
        {
            try
            {
                var viewModel = new AddChannelDialogViewModel(_pxiChassisService, ChassisName, TestTaskName);
                var dialog = new AddChannelDialog(viewModel, _pxiChassisService);
                dialog.ShowDialog();

                if (dialog.ChannelResult != null)
                {
                    var newChannel = dialog.ChannelResult;
                    // 设置序号
                    newChannel.Index = Channels.Count + 1;
                    Channels.Add(newChannel);

                    // 重新计算所有通道的序号
                    UpdateChannelIndices();

                    // 更新分页信息（会自动触发 Channels_CollectionChanged，但为了确保，我们手动更新）
                    UpdatePagination();

                    // 如果新增的通道不在当前页，自动跳转到最后一页
                    int lastPage = TotalPages;
                    if (lastPage > 0 && CurrentPage < lastPage)
                    {
                        CurrentPage = lastPage;
                    }

                    // 保存到内存并标记项目为已修改
                    SaveChannelsToMemory();
                    _eventAggregator.GetEvent<Events.ProjectModifiedEvent>().Publish(new Events.ProjectModifiedEventArgs
                    {
                        ModificationType = "ChannelTabel",
                        Description = $"添加通道: {newChannel.ChannelName}"
                    });
                }
            }
            catch (Exception)
            {
            }
        }

        private void OnAddChannelFromTree(ChannelTreeNode node)
        {
            if (node == null || node.NodeType != "Channel")
            {
                return;
            }

            try
            {
                // 从树节点提取ChannelTabelItem信息
                ChannelTabelItem template = node.Tag as ChannelTabelItem;
                if (template == null)
                {
                    // 如果没有Tag，尝试从父节点获取信息
                    template = new ChannelTabelItem
                    {
                        ChannelName = node.DisplayName
                    };
                    
                    // 尝试从父节点获取板卡和机箱信息
                    var parent = FindParentNode(node);
                    if (parent != null && parent.NodeType == "Card")
                    {
                        var cardTag = parent.Tag as Models.Devices.DeviceBase;
                        if (cardTag != null)
                        {
                            template.CardName = !string.IsNullOrEmpty(cardTag.CardName) ? cardTag.CardName : cardTag.Model;
                            
                            // 继续向上查找机箱
                            var chassisParent = FindParentNode(parent);
                            if (chassisParent != null && chassisParent.NodeType == "Chassis")
                            {
                                var chassisTag = chassisParent.Tag as Models.ChassisModel;
                                if (chassisTag != null)
                                {
                                    template.ChassisName = chassisTag.Name;
                                }
                            }
                        }
                    }
                    
                    // 从通道名称推断通道类型
                    if (string.IsNullOrEmpty(template.ChannelType))
                    {
                        string channelPrefix = new string(template.ChannelName.TakeWhile(c => !char.IsDigit(c)).ToArray());
                        if (channelPrefix == "AI" || channelPrefix == "AO")
                        {
                            template.ChannelType = "模拟量通道";
                            template.InputOutputType = channelPrefix;
                        }
                        else if (channelPrefix == "DI" || channelPrefix == "DO")
                        {
                            template.ChannelType = "离散量通道";
                            template.InputOutputType = channelPrefix;
                        }
                        template.AssociatedChannel = template.ChannelName;
                    }
                }

                // 创建对话框并预填充数据
                var viewModel = new AddChannelDialogViewModel(_pxiChassisService, ChassisName, TestTaskName);
                viewModel.PrefillData(template);
                var dialog = new AddChannelDialog(viewModel, _pxiChassisService);
                dialog.ShowDialog();

                if (dialog.ChannelResult != null)
                {
                    var newChannel = dialog.ChannelResult;
                    // 设置序号
                    newChannel.Index = Channels.Count + 1;
                    Channels.Add(newChannel);

                    // 重新计算所有通道的序号
                    UpdateChannelIndices();

                    // 更新分页信息
                    UpdatePagination();

                    // 如果新增的通道不在当前页，自动跳转到最后一页
                    int lastPage = TotalPages;
                    if (lastPage > 0 && CurrentPage < lastPage)
                    {
                        CurrentPage = lastPage;
                    }

                    // 保存到内存并标记项目为已修改
                    SaveChannelsToMemory();
                    _eventAggregator.GetEvent<Events.ProjectModifiedEvent>().Publish(new Events.ProjectModifiedEventArgs
                    {
                        ModificationType = "ChannelTabel",
                        Description = $"添加通道: {newChannel.ChannelName}"
                    });
                }
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// 查找节点的父节点（辅助方法）
        /// </summary>
        private ChannelTreeNode FindParentNode(ChannelTreeNode node)
        {
            if (node == null || ChannelTreeRoot == null)
                return null;

            foreach (var rootNode in ChannelTreeRoot)
            {
                var found = FindParentNodeRecursive(rootNode, node);
                if (found != null)
                    return found;
            }

            return null;
        }

        /// <summary>
        /// 递归查找父节点
        /// </summary>
        private ChannelTreeNode FindParentNodeRecursive(ChannelTreeNode parent, ChannelTreeNode target)
        {
            if (parent?.Children == null)
                return null;

            foreach (var child in parent.Children)
            {
                if (child == target)
                    return parent;

                var found = FindParentNodeRecursive(child, target);
                if (found != null)
                    return found;
            }

            return null;
        }

        private void OnDeleteChannel(ChannelTabelItem channel)
        {
            if (channel != null)
            {
                string channelName = channel.ChannelName;
                
                // 显示确认删除对话框
                var result = ReMessageBox.Show(
                    $"确定要删除通道 '{channelName}' 吗？",
                    "确认删除",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Question);
                
                if (result == System.Windows.MessageBoxResult.Yes)
                {
                    Channels.Remove(channel);
                    // 重新计算所有通道的序号
                    UpdateChannelIndices();
                    // 更新分页
                    UpdatePagination();
                    
                    // 保存到内存并标记项目为已修改
                    SaveChannelsToMemory();
                    _eventAggregator.GetEvent<Events.ProjectModifiedEvent>().Publish(new Events.ProjectModifiedEventArgs
                    {
                        ModificationType = "ChannelTabel",
                        Description = $"删除通道: {channelName}"
                    });
                }
            }
        }

        private void UpdateChannelIndices()
        {
            for (int i = 0; i < Channels.Count; i++)
            {
                Channels[i].Index = i + 1;
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

        private void OnGoToPage(int page)
        {
            if (page >= 1 && page <= TotalPages)
            {
                CurrentPage = page;
            }
        }

        private void Channels_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            try
            {
                UpdatePagination();
                
                // 集合变化时自动保存到内存
                SaveChannelsToMemory();
            }
            catch (Exception)
            {
            }
        }

        private void UpdatePagination()
        {
            UpdatePagedChannels();
            UpdatePaginationInfo();
            UpdatePageNumbers();
            ((DelegateCommand)PreviousPageCommand).RaiseCanExecuteChanged();
            ((DelegateCommand)NextPageCommand).RaiseCanExecuteChanged();
            RaisePropertyChanged(nameof(TotalPages));
        }

        private void UpdatePagedChannels()
        {
            // 确保 PagedChannels 已初始化
            if (PagedChannels == null)
            {
                PagedChannels = new ObservableCollection<ChannelTabelItem>();
            }

            PagedChannels.Clear();

            if (Channels == null || Channels.Count == 0)
            {
                // 添加空行以保持8行显示
                for (int i = 0; i < PageSize; i++)
                {
                    var emptyItem = new ChannelTabelItem { IsEmpty = true };
                    PagedChannels.Add(emptyItem);
                }
                return;
            }

            int startIndex = (CurrentPage - 1) * PageSize;
            int endIndex = Math.Min(startIndex + PageSize, Channels.Count);

            for (int i = startIndex; i < endIndex; i++)
            {
                var channel = Channels[i];
                // 确保真实通道的IsEmpty为false
                if (channel != null)
                {
                    channel.IsEmpty = false;
                }
                PagedChannels.Add(channel);
            }

            // 如果当前页不足8行，添加空行填充
            while (PagedChannels.Count < PageSize)
            {
                var emptyItem = new ChannelTabelItem { IsEmpty = true };
                PagedChannels.Add(emptyItem);
            }
        }

        private void UpdatePaginationInfo()
            => PaginationInfo = PaginationHelper.GetPaginationInfo(Channels?.Count ?? 0, CurrentPage, PageSize);

        private void UpdatePageNumbers()
        {
            if (PageNumbers == null) PageNumbers = new ObservableCollection<PaginationButtonInfo>();
            PaginationHelper.UpdatePageNumbers(PageNumbers, TotalPages, CurrentPage, OnGoToPage);
        }

        private void OnEditChannel(ChannelTabelItem channel)
        {
            if (channel != null)
            {
                try
                {
                    var viewModel = new AddChannelDialogViewModel(_pxiChassisService, ChassisName, TestTaskName);
                    
                    // 按照顺序设置字段，确保每次设置都能触发相应的Change事件来加载选项列表
                    // 1. 先设置通道类型（会触发OnChannelTypeChanged，清除后续字段并加载机箱列表）
                    viewModel.SelectedChannelType = channel.ChannelType;
                    
                    // 2. 设置机箱（会触发OnChassisChanged，清除后续字段并加载板卡列表）
                    var chassis = _pxiChassisService.GetAllChassis()?.FirstOrDefault(c => c.Name == channel.ChassisName);
                    if (chassis != null)
                    {
                        viewModel.SelectedChassis = chassis.Name;
                        
                        // 3. 设置板卡（会触发OnCardChanged，清除后续字段并加载输入输出类型列表）
                        // 注意：CardName可能是CardName属性或Model属性
                        var card = chassis.Devices?.FirstOrDefault(d => 
                            d.DeviceType == "Card" && 
                            (!string.IsNullOrEmpty(d.CardName) ? d.CardName == channel.CardName : d.Model == channel.CardName));
                        if (card != null)
                        {
                            viewModel.SelectedCard = card;
                            
                            // 4. 对于离散量通道和模拟量通道，需要设置输入输出类型和关联通道
                            if (channel.ChannelType == "离散量通道" || channel.ChannelType == "模拟量通道")
                            {
                                // 设置输入输出类型（会触发OnInputOutputTypeChanged，清除后续字段并加载关联通道列表）
                                if (!string.IsNullOrEmpty(channel.InputOutputType))
                                {
                                    viewModel.SelectedInputOutputType = channel.InputOutputType;
                                    
                                    // 5. 设置关联通道（会触发OnAssociatedChannelChanged，自动填充通道名称）
                                    if (!string.IsNullOrEmpty(channel.AssociatedChannel))
                                    {
                                        viewModel.SelectedAssociatedChannel = channel.AssociatedChannel;
                                    }
                                }
                            }
                            // 对于通讯通道和其他通道，不需要输入输出类型和关联通道
                            // 通道名称需要手动设置
                        }
                    }
                    
                    // 6. 设置通道名称和备注
                    // 对于离散量通道和模拟量通道，SelectedAssociatedChannel可能已经自动填充了通道名称
                    // 但在编辑模式下，我们总是使用保存的通道名称，即使它与关联通道的名称不同
                    // 对于通讯通道和其他通道，通道名称不会被自动填充，需要手动设置
                    if (!string.IsNullOrEmpty(channel.ChannelName))
                    {
                        if (channel.ChannelType == "离散量通道" || channel.ChannelType == "模拟量通道")
                        {
                            // 离散量通道和模拟量通道：如果自动填充的通道名称与保存的不同，使用保存的通道名称
                            if (viewModel.ChannelName != channel.ChannelName)
                            {
                                viewModel.MarkChannelNameAsUserEdited();
                                viewModel.ChannelName = channel.ChannelName;
                            }
                            // 如果相同，则不需要做任何处理，自动填充的就是正确的
                        }
                        else
                        {
                            // 通讯通道和其他通道：直接设置通道名称（不会被自动填充）
                            viewModel.ChannelName = channel.ChannelName;
                        }
                    }
                    
                    viewModel.Remarks = channel.Remarks;
                    
                    var dialog = new AddChannelDialog(viewModel, _pxiChassisService);
                    dialog.ShowDialog();

                    if (dialog.ChannelResult != null)
                    {
                        var editedChannel = dialog.ChannelResult;
                        // 更新通道数据
                        channel.ChannelName = editedChannel.ChannelName;
                        channel.CardName = editedChannel.CardName;
                        channel.ChassisName = editedChannel.ChassisName;
                        channel.Remarks = editedChannel.Remarks;
                        channel.ChannelType = editedChannel.ChannelType;
                        channel.InputOutputType = editedChannel.InputOutputType;
                        channel.AssociatedChannel = editedChannel.AssociatedChannel;
                        
                        // 保存到内存并标记项目为已修改
                        SaveChannelsToMemory();
                        _eventAggregator.GetEvent<Events.ProjectModifiedEvent>().Publish(new Events.ProjectModifiedEventArgs
                        {
                            ModificationType = "ChannelTabel",
                            Description = $"编辑通道: {channel.ChannelName}"
                        });
                    }
                }
                catch (Exception)
                {
                }
            }
        }

        private void OnCloseInRegion()
        {
            var result = ReMessageBox.Show("确定要关闭当前配置表吗？", "确认", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
            if (result == System.Windows.MessageBoxResult.Yes)
            {
                // 构建完整的pageKey: ChannelConfigTabel_任务名-配置表名
                string pageKey = $"ChannelConfigTabel_{TestTaskName}-{ConfigTabelName}";
                
                // 传递完整的pageKey，这样MainWindowViewModel可以正确识别和关闭该页面
                _eventAggregator.GetEvent<Events.ReleaseCurrentPageEvent>().Publish(pageKey);
            }
        }

        private void OnToggleTreeNode(ChannelTreeNode node)
        {
            if (node != null)
            {
                // 清除所有节点的选中状态
                ClearAllNodeSelection(ChannelTreeRoot);

                // 设置当前节点为选中状态
                node.IsSelected = true;

                // 切换展开/折叠状态
                node.IsExpanded = !node.IsExpanded;

                // 保存展开状态
                SaveTreeNodeExpandedState(node);
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
                        ConfigTabelType = "channel_config_tabel",
                        TriggerDoubleClick = true
                    });
            }
            catch (Exception ex)
            {
                // 导航失败时记录错误，但不显示错误消息（避免干扰用户）
                System.Diagnostics.Debug.WriteLine($"导航到项目树节点失败: {ex.Message}");
            }
        }

        #endregion

        #region 私有方法

        /// <summary>
        /// 为指定的板卡创建树节点
        /// </summary>
        /// <param name="card">板卡设备</param>
        /// <param name="chassisName">机箱名称</param>
        /// <returns>板卡树节点，如果没有有效通道则返回null</returns>
        private ChannelTreeNode CreateCardNode(DeviceBase card, string chassisName)
        {
            // 检查板卡是否有通道
            if (!HasValidChannels(card))
                return null;

            // 输出调试信息
            LogCardDebugInfo(card);

            // 创建板卡节点
            var cardNode = new ChannelTreeNode
            {
                DisplayName = GetCardDisplayName(card),
                NodeType = "Card",
                Tag = card,
                IsExpanded = GetTreeNodeExpandedState($"{chassisName}/{GetCardDisplayName(card)}")
            };

            // 根据板卡类型添加通道节点
            AddChannelsToCardNode(cardNode, card, chassisName);

            // 只返回有通道的板卡节点
            return cardNode.Children.Count > 0 ? cardNode : null;
        }

        /// <summary>
        /// 检查板卡是否有有效的通道
        /// </summary>
        private bool HasValidChannels(DeviceBase card)
        {
            if (card is ProgrammableResistorDevice)
            {
                // 电阻输出板卡固定有9个通道
                return true;
            }
            // 其他板卡检查是否有子节点
            return card.Children != null && card.Children.Count > 0;
        }

        /// <summary>
        /// 获取板卡的显示名称
        /// </summary>
        private string GetCardDisplayName(DeviceBase card)
        {
            return !string.IsNullOrEmpty(card.CardName) ? card.CardName : card.Model;
        }

        /// <summary>
        /// 输出板卡的调试信息
        /// </summary>
        private void LogCardDebugInfo(DeviceBase card)
        {
            System.Diagnostics.Debug.WriteLine($"[ChannelConfigTabel] 板卡 {card.CardName}: ID={card.Id}, HashCode={card.GetHashCode()}");

            if (card.CardConfigData is Models.DigitalIOCardConfig digitalConfig)
            {
                var enabledDI = digitalConfig.InputChannels?.Count(c => c.IsEnabled) ?? 0;
                var enabledDO = digitalConfig.OutputChannels?.Count(c => c.IsEnabled) ?? 0;
                System.Diagnostics.Debug.WriteLine($"[ChannelConfigTabel] 板卡 {card.CardName}: DI使能={enabledDI}, DO使能={enabledDO}");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[ChannelConfigTabel] 板卡 {card.CardName}: CardConfigData={(card.CardConfigData?.GetType().Name ?? "null")}");
            }
        }

        /// <summary>
        /// 为板卡节点添加通道子节点
        /// </summary>
        private void AddChannelsToCardNode(ChannelTreeNode cardNode, DeviceBase card, string chassisName)
        {
            if (card is ProgrammableResistorDevice)
            {
                AddResistorOutputChannels(cardNode, card, chassisName);
            }
            else if (card is DigitalIODevice)
            {
                AddDigitalIOChannels(cardNode, card, chassisName);
            }
            else
            {
                AddStandardChannels(cardNode, card, chassisName);
            }
        }

        /// <summary>
        /// 为电阻输出板卡添加固定9个通道 (RO0-RO8)
        /// </summary>
        private void AddResistorOutputChannels(ChannelTreeNode cardNode, DeviceBase card, string chassisName)
        {
            for (int i = 0; i < 9; i++)
            {
                string channelName = $"RO{i}";
                if (!IsChannelEnabled(card, channelName))
                    continue;

                var channelNode = CreateChannelNode(channelName, cardNode.DisplayName, chassisName, "模拟量通道", "RO");
                cardNode.Children.Add(channelNode);
            }
        }

        /// <summary>
        /// 为离散量输入输出板卡添加通道节点（创建"输入"和"输出"子节点）
        /// </summary>
        private void AddDigitalIOChannels(ChannelTreeNode cardNode, DeviceBase card, string chassisName)
        {
            // 创建"输入"节点
            var inputNode = new ChannelTreeNode
            {
                DisplayName = "输入",
                NodeType = "InputGroup",
                Tag = card,
                IsExpanded = GetTreeNodeExpandedState($"{chassisName}/{cardNode.DisplayName}/输入")
            };

            // 创建"输出"节点
            var outputNode = new ChannelTreeNode
            {
                DisplayName = "输出",
                NodeType = "OutputGroup",
                Tag = card,
                IsExpanded = GetTreeNodeExpandedState($"{chassisName}/{cardNode.DisplayName}/输出")
            };

            foreach (var channelGroup in card.Children)
            {
                var channels = ParseChannelRange(channelGroup);
                foreach (var channelName in channels)
                {
                    if (!IsChannelEnabled(card, channelName))
                        continue;

                    var (channelType, inputOutputType) = InferChannelType(channelName, card);
                    var channelNode = CreateChannelNode(channelName, cardNode.DisplayName, chassisName, channelType, inputOutputType);

                    // 根据通道类型添加到对应的分组节点
                    if (inputOutputType == "DI")
                    {
                        inputNode.Children.Add(channelNode);
                    }
                    else if (inputOutputType == "DO")
                    {
                        outputNode.Children.Add(channelNode);
                    }
                }
            }

            // 只添加有子节点的组
            if (inputNode.Children.Count > 0)
            {
                cardNode.Children.Add(inputNode);
            }
            if (outputNode.Children.Count > 0)
            {
                cardNode.Children.Add(outputNode);
            }
        }

        /// <summary>
        /// 为标准板卡添加通道节点
        /// </summary>
        private void AddStandardChannels(ChannelTreeNode cardNode, DeviceBase card, string chassisName)
        {
            foreach (var channelGroup in card.Children)
            {
                var channels = ParseChannelRange(channelGroup);
                foreach (var channelName in channels)
                {
                    if (!IsChannelEnabled(card, channelName))
                        continue;

                    var (channelType, inputOutputType) = InferChannelType(channelName, card);
                    var channelNode = CreateChannelNode(channelName, cardNode.DisplayName, chassisName, channelType, inputOutputType);
                    cardNode.Children.Add(channelNode);
                }
            }
        }

        /// <summary>
        /// 从通道名称推断通道类型和输入输出类型
        /// </summary>
        private (string ChannelType, string InputOutputType) InferChannelType(string channelName, DeviceBase card)
        {
            string channelPrefix = new string(channelName.TakeWhile(c => !char.IsDigit(c)).ToArray());

            if (channelPrefix == "AI" || channelPrefix == "AO" || channelPrefix == "RO")
                return ("模拟量通道", channelPrefix);
            else if (channelPrefix == "DI" || channelPrefix == "DO")
                return ("离散量通道", channelPrefix);
            else if (channelPrefix == "CAN" || card is CanBusDevice)
                return ("通讯通道", "CAN");

            return (null, null);
        }

        /// <summary>
        /// 创建通道节点
        /// </summary>
        private ChannelTreeNode CreateChannelNode(string channelName, string cardName, string chassisName, string channelType, string inputOutputType)
        {
            return new ChannelTreeNode
            {
                DisplayName = channelName,
                NodeType = "Channel",
                Tag = new ChannelTabelItem
                {
                    ChannelName = channelName,
                    CardName = cardName,
                    ChassisName = chassisName,
                    ChannelType = channelType,
                    InputOutputType = inputOutputType,
                    AssociatedChannel = channelName
                },
                IsExpanded = false
            };
        }

        #endregion
    }

}

