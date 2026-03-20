using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;
using MeasureControl.Models;
using MeasureControl.Services;
using MeasureControl.Views;
using Prism.Commands;
using Prism.Events;
using Prism.Mvvm;
using Prism.Regions;
using MeasureControl.Helpers;
using MeasureControl.Views.Dialogs;

namespace MeasureControl.ViewModels.IcdConfig
{
    /// <summary>
    /// ICD配置表的ViewModel
    /// </summary>
    public class IcdConfigTabelViewModel : BindableBase, INavigationAware, IDisposable
    {
        private readonly IRegionManager _regionManager;
        private readonly IEventAggregator _eventAggregator;
        private readonly ProjectService _projectService;
        private readonly IDialogService _dialogService;

        /// <summary>
        /// 字段显示名到像素宽度的映射。用于前端多绑定计算列宽。
        /// </summary>
        public IDictionary<string, double> FieldWidthMap { get; } = new Dictionary<string, double>
        {
            ["帧ID段"] = 150,
            ["数据长度段"] = 140,
            ["源数据段"] = 260,
            ["命令字段"] = 180,
            ["校验和字段"] = 130,
            ["LABEL"] = 110,
            ["SD"] = 140,
            ["DATA"] = 170,
            ["SIGN"] = 200,
            ["SSM"] = 230,
            ["PARITY"] = 130,
            ["标号"] = 190,
            ["负载数据长度段"] = 220
        };

        // 用于存储所有ICD配置表数据的静态字典（key格式：测试任务名/配置表名）
        private static Dictionary<string, ObservableCollection<IcdFrameItem>> _allIcdTabelItems = new Dictionary<string, ObservableCollection<IcdFrameItem>>();
        // 用于存储ICD配置表的协议类型（key格式：测试任务名/配置表名）
        private static Dictionary<string, string> _icdTabelProtocolTypes = new Dictionary<string, string>();
        private static Guid _currentProjectGeneration = Guid.NewGuid();

        // 用于同步访问静态字典的锁对象
        private static readonly object _allIcdTabelItemsLock = new object();
        private static readonly object _projectGenerationLock = new object();
        private Guid _localProjectGeneration = Guid.Empty;

        /// <summary>获取所有ICD配置表数据</summary>
        public static Dictionary<string, List<IcdFrameItem>> GetAllIcdTabelItems()
        {
            lock (_allIcdTabelItemsLock)
            {
                return _allIcdTabelItems.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value?.Where(f => f != null).Select(f => CloneIcdFrameItem(f)).ToList() ?? new List<IcdFrameItem>());
            }
        }

        /// <summary>清空所有ICD配置表数据</summary>
        public static void ClearAllIcdTabelItems()
        {
            lock (_allIcdTabelItemsLock) 
            { 
                _allIcdTabelItems.Clear();
                _icdTabelProtocolTypes.Clear();
            }
            BumpProjectGeneration();
        }

        /// <summary>获取ICD配置表的协议类型</summary>
        public static string GetIcdTabelProtocolType(string tabelKey)
        {
            lock (_allIcdTabelItemsLock)
            {
                return _icdTabelProtocolTypes.TryGetValue(tabelKey, out var protocolType) ? protocolType : null;
            }
        }

        /// <summary>设置ICD配置表的协议类型</summary>
        public static void SetIcdTabelProtocolType(string tabelKey, string protocolType)
        {
            lock (_allIcdTabelItemsLock)
            {
                if (string.IsNullOrEmpty(protocolType))
                {
                    _icdTabelProtocolTypes.Remove(tabelKey);
                }
                else
                {
                    _icdTabelProtocolTypes[tabelKey] = protocolType;
                }
            }
        }

        /// <summary>加载ICD配置表数据到静态字典</summary>
        public static void LoadIcdTabelItems(Dictionary<string, List<IcdFrameItem>> items)
        {
            lock (_allIcdTabelItemsLock)
            {
                _allIcdTabelItems.Clear();
                if (items == null) return;
                foreach (var kvp in items)
                    _allIcdTabelItems[kvp.Key] = new ObservableCollection<IcdFrameItem>(
                        kvp.Value?.Where(f => f != null).Select(f => CloneIcdFrameItem(f)) ?? Enumerable.Empty<IcdFrameItem>());
            }
        }

        private static Guid GetProjectGeneration()
        {
            lock (_projectGenerationLock)
            {
                return _currentProjectGeneration;
            }
        }

        private static void BumpProjectGeneration()
        {
            lock (_projectGenerationLock)
            {
                _currentProjectGeneration = Guid.NewGuid();
            }
        }

        /// <summary>
        /// 克隆ICD帧项
        /// </summary>
        private static IcdFrameItem CloneIcdFrameItem(IcdFrameItem source)
        {
            if (source == null)
                return null;

            var cloned = new IcdFrameItem
            {
                Index = source.Index,
                FrameName = source.FrameName,
                FrameId = source.FrameId,
                Protocol = source.Protocol,
                Remarks = source.Remarks
            };

            // 克隆字段
            if (source.Fields != null)
            {
                cloned.Fields = new ObservableCollection<IcdFrameField>();
                foreach (var field in source.Fields)
                {
                    if (field == null)
                        continue;

                    var clonedField = new IcdFrameField
                    {
                        Name = field.Name,
                        DisplayName = field.DisplayName,
                        BackgroundColor = field.BackgroundColor,
                        IsSelected = false
                    };

                    // 克隆配置项
                    if (field.ConfigItems != null)
                    {
                        clonedField.ConfigItems = new ObservableCollection<IcdFieldConfigItem>();
                        foreach (var configItem in field.ConfigItems)
                        {
                            if (configItem == null)
                                continue;

                            var clonedConfigItem = new IcdFieldConfigItem
                            {
                                Name = configItem.Name,
                                Value = configItem.Value,
                                ConfigType = configItem.ConfigType,
                                IsVisible = configItem.IsVisible
                            };

                            if (configItem.Options != null)
                            {
                                clonedConfigItem.Options = new ObservableCollection<string>(configItem.Options);
                            }

                            clonedField.ConfigItems.Add(clonedConfigItem);
                        }
                    }

                    cloned.Fields.Add(clonedField);
                }
            }

            return cloned;
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

        private ObservableCollection<IcdFrameItem> _icdFrames;
        /// <summary>
        /// ICD帧配置列表
        /// </summary>
        public ObservableCollection<IcdFrameItem> IcdFrames
        {
            get => _icdFrames;
            set => SetProperty(ref _icdFrames, value);
        }

        // 分页相关属性
        private const int PageSize = 10;
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
                if (IcdFrames == null || IcdFrames.Count == 0)
                    return 1;
                return (int)Math.Ceiling((double)IcdFrames.Count / PageSize);
            }
        }

        private ObservableCollection<IcdFrameItem> _pagedFrames;
        /// <summary>
        /// 当前页显示的帧列表
        /// </summary>
        public ObservableCollection<IcdFrameItem> PagedFrames
        {
            get => _pagedFrames;
            set => SetProperty(ref _pagedFrames, value);
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

        // 字段选择相关属性
        private IcdFrameItem _selectedFrame;
        /// <summary>
        /// 当前选中的帧
        /// </summary>
        public IcdFrameItem SelectedFrame
        {
            get => _selectedFrame;
            set => SetProperty(ref _selectedFrame, value);
        }

        private IcdFrameField _selectedField;
        /// <summary>
        /// 当前选中的字段
        /// </summary>
        public IcdFrameField SelectedField
        {
            get => _selectedField;
            set
            {
                if (SetProperty(ref _selectedField, value))
                {
                    System.Diagnostics.Debug.WriteLine($"[ICD] SelectedField changed to: {(value == null ? "null" : value.DisplayName ?? value.Name)}");
                    System.Diagnostics.Trace.WriteLine($"[ICD] SelectedField changed to: {(value == null ? "null" : value.DisplayName ?? value.Name)}");
                    UpdateFieldConfigItems();
                }
            }
        }

        private ObservableCollection<IcdFieldConfigItem> _fieldConfigItems;
        /// <summary>
        /// 当前选中字段的配置项列表
        /// </summary>
        public ObservableCollection<IcdFieldConfigItem> FieldConfigItems
        {
            get => _fieldConfigItems;
            set => SetProperty(ref _fieldConfigItems, value);
        }

        // 协议选择相关属性
        private string _selectedProtocol = "RS422";
        /// <summary>
        /// 当前选中的协议
        /// </summary>
        public string SelectedProtocol
        {
            get => _selectedProtocol;
            set => SetProperty(ref _selectedProtocol, value);
        }

        private ObservableCollection<string> _availableProtocols;
        /// <summary>
        /// 可用的协议列表
        /// </summary>
        public ObservableCollection<string> AvailableProtocols
        {
            get => _availableProtocols;
            set => SetProperty(ref _availableProtocols, value);
        }

        #endregion

        #region Commands

        public DelegateCommand AddIcdFrameCommand { get; }
        public DelegateCommand AddIcdMappingCommand { get; }
        public DelegateCommand<IcdFrameItem> DeleteIcdFrameCommand { get; }
        public DelegateCommand<IcdFrameItem> EditIcdFrameCommand { get; }
        public DelegateCommand<IcdFrameItem> SelectFrameCommand { get; }
        public DelegateCommand<IcdFrameField> SelectFieldCommand { get; }
        public DelegateCommand PreviousPageCommand { get; }
        public DelegateCommand NextPageCommand { get; }

        // 浮动窗口命令
        public DelegateCommand FloatWindowCommand { get; }
        public DelegateCommand MinimizeInRegionCommand { get; }
        public DelegateCommand CloseInRegionCommand { get; }

        #endregion

        #region Constructor

        public IcdConfigTabelViewModel(
            IRegionManager regionManager,
            IEventAggregator eventAggregator,
            ProjectService projectService,
            IDialogService dialogService)
        {
            _regionManager = regionManager ?? throw new ArgumentNullException(nameof(regionManager));
            _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
            _projectService = projectService ?? throw new ArgumentNullException(nameof(projectService));
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));

            // 初始化命令
            AddIcdFrameCommand = new DelegateCommand(OnAddIcdFrame);
            AddIcdMappingCommand = new DelegateCommand(OnAddIcdMapping);
            DeleteIcdFrameCommand = new DelegateCommand<IcdFrameItem>(OnDeleteIcdFrame);
            EditIcdFrameCommand = new DelegateCommand<IcdFrameItem>(OnEditIcdFrame);
            SelectFrameCommand = new DelegateCommand<IcdFrameItem>(OnSelectFrame);
            SelectFieldCommand = new DelegateCommand<IcdFrameField>(OnSelectField);
            PreviousPageCommand = new DelegateCommand(OnPreviousPage, () => CurrentPage > 1);
            NextPageCommand = new DelegateCommand(OnNextPage, () => CurrentPage < TotalPages);

            // 浮动窗口命令
            FloatWindowCommand = new DelegateCommand(OnFloatWindow);
            MinimizeInRegionCommand = new DelegateCommand(OnMinimizeInRegion);
            CloseInRegionCommand = new DelegateCommand(OnCloseInRegion);

            // 初始化集合
            IcdFrames = new ObservableCollection<IcdFrameItem>();
            PagedFrames = new ObservableCollection<IcdFrameItem>();
            PageNumbers = new ObservableCollection<PaginationButtonInfo>();
            FieldConfigItems = new ObservableCollection<IcdFieldConfigItem>();

            // 初始化协议列表（与ICD配置表协议类型一致）
            AvailableProtocols = new ObservableCollection<string> { "CAN", "ARINC429", "1553B", "MIL1394" };
            SelectedProtocol = "CAN";

            // 订阅事件
            _eventAggregator.GetEvent<Events.IcdTabelItemsLoadEvent>().Subscribe(OnIcdTabelItemsLoad, ThreadOption.UIThread);
            _eventAggregator.GetEvent<Events.IcdTabelItemsRequestEvent>().Subscribe(OnIcdTabelItemsRequest, ThreadOption.UIThread);
            _eventAggregator.GetEvent<Events.ProjectClosedEvent>().Subscribe(OnProjectClosed, ThreadOption.UIThread);
            _eventAggregator.GetEvent<Events.ProjectOpenedEvent>().Subscribe(OnProjectOpened, ThreadOption.UIThread);
        }

        #endregion

        #region INavigationAware Implementation

        public void OnNavigatedTo(NavigationContext navigationContext)
        {
            var newChassisName = navigationContext.Parameters.ContainsKey("ChassisName")
                ? navigationContext.Parameters["ChassisName"] as string
                : null;

            var newTestTaskName = navigationContext.Parameters.ContainsKey("TestTaskName")
                ? navigationContext.Parameters["TestTaskName"] as string
                : null;

            var newConfigTabelName = navigationContext.Parameters.ContainsKey("ConfigTabelName")
                ? navigationContext.Parameters["ConfigTabelName"] as string
                : null;

            var newParentType = navigationContext.Parameters.ContainsKey("ParentType")
                ? navigationContext.Parameters["ParentType"] as string
                : null;

            bool contextChanged =
                !string.Equals(ChassisName, newChassisName, StringComparison.Ordinal) ||
                !string.Equals(TestTaskName, newTestTaskName, StringComparison.Ordinal) ||
                !string.Equals(ConfigTabelName, newConfigTabelName, StringComparison.Ordinal) ||
                !string.Equals(ParentType, newParentType, StringComparison.Ordinal);

            bool projectGenerationChanged = HasProjectGenerationChanged();

            if (projectGenerationChanged)
            {
                System.Diagnostics.Debug.WriteLine("[ICD] Project generation changed; resetting local caches before navigation load.");
                ResetViewModelState();
            }

            if (!contextChanged && !projectGenerationChanged && IcdFrames != null && IcdFrames.Count > 0)
            {
                System.Diagnostics.Debug.WriteLine("[ICD] OnNavigatedTo called but context unchanged; skipping reload.");
                return;
            }

            ChassisName = newChassisName ?? string.Empty;
            TestTaskName = newTestTaskName;
            ConfigTabelName = newConfigTabelName;
            ParentType = newParentType;

            string parentName = GetParentDisplayName(ParentType);
            var pathParts = new List<string>();
            
            if (!string.IsNullOrEmpty(ChassisName))
            {
                pathParts.Add(ChassisName);
            }
            if (!string.IsNullOrEmpty(TestTaskName))
            {
                pathParts.Add(TestTaskName);
            }
            if (!string.IsNullOrEmpty(parentName))
            {
                pathParts.Add(parentName);
            }
            if (!string.IsNullOrEmpty(ConfigTabelName))
            {
                pathParts.Add(ConfigTabelName);
            }
            
            DisplayPath = pathParts.Count > 0 ? string.Join("/", pathParts) : "ICD配置表";

            // 从导航参数中获取ProjectData，读取ProtocolType
            var projectData = navigationContext.Parameters.ContainsKey("ProjectData")
                ? navigationContext.Parameters["ProjectData"] as ProjectItem
                : null;
            
            if (projectData != null && !string.IsNullOrEmpty(TestTaskName) && !string.IsNullOrEmpty(ConfigTabelName))
            {
                // 查找ICD配置表节点并读取ProtocolType
                if (projectData.Children != null)
                {
                    foreach (var chassisNode in projectData.Children.Where(c => c.Type == AppConstants.NodeTypePxiChassis))
                    {
                        if (chassisNode.Children == null) continue;
                        var taskConfigNode = chassisNode.Children.FirstOrDefault(c => c.Type == AppConstants.NodeTypeTaskConfig);
                        if (taskConfigNode?.Children == null) continue;
                        
                        var testTask = taskConfigNode.Children.FirstOrDefault(t => t.Type == AppConstants.NodeTypeTestTask && t.Name == TestTaskName);
                        if (testTask?.Children == null) continue;
                        
                        var icdConfigNode = testTask.Children.FirstOrDefault(c => c.Type == "icd_config");
                        if (icdConfigNode?.Children == null) continue;
                        
                        var icdTabel = icdConfigNode.Children.FirstOrDefault(t => t.Type == "icd_config_tabel" && t.Name == ConfigTabelName);
                        if (icdTabel != null && !string.IsNullOrEmpty(icdTabel.ProtocolType))
                        {
                            string key = GetIcdTabelKey();
                            SetIcdTabelProtocolType(key, icdTabel.ProtocolType);
                        }
                    }
                }
            }

            _localProjectGeneration = GetProjectGeneration();

            SelectedFrame = null;
            SelectedField = null;
            FieldConfigItems?.Clear();

            _currentPage = 1;
            RaisePropertyChanged(nameof(CurrentPage));

            LoadConfigTabelData();
        }

        public bool IsNavigationTarget(NavigationContext navigationContext)
        {
            // 每次创建新实例，支持多个相同类型页面
            return false;
        }

        public void OnNavigatedFrom(NavigationContext navigationContext)
        {
            // 导航离开时保存数据
            SaveFramesToMemory();
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
                SaveFramesToMemory();
                _eventAggregator.GetEvent<Events.IcdTabelItemsLoadEvent>().Unsubscribe(OnIcdTabelItemsLoad);
                _eventAggregator.GetEvent<Events.IcdTabelItemsRequestEvent>().Unsubscribe(OnIcdTabelItemsRequest);
                _eventAggregator.GetEvent<Events.ProjectClosedEvent>().Unsubscribe(OnProjectClosed);
                _eventAggregator.GetEvent<Events.ProjectOpenedEvent>().Unsubscribe(OnProjectOpened);
                _disposed = true;
            }
        }

        #endregion

        #region Event Handlers

        /// <summary>
        /// 处理ICD表数据加载事件
        /// </summary>
        private void OnIcdTabelItemsLoad(Events.IcdTabelItemsLoadEventArgs args)
        {
            if (args == null || string.IsNullOrEmpty(args.TestTaskName) || string.IsNullOrEmpty(args.ConfigTabelName))
                return;

            // 注意：这里需要根据事件参数中的机箱名称来构建key，但目前事件参数可能没有机箱名称
            // 为了兼容，先使用当前实例的 ChassisName
            string key;
            if (!string.IsNullOrEmpty(ChassisName))
            {
                key = $"{ChassisName}/{args.TestTaskName}/{args.ConfigTabelName}";
            }
            else
            {
                key = $"{args.TestTaskName}/{args.ConfigTabelName}";
            }

            if (key == GetIcdTabelKey())
            {
                ObservableCollection<IcdFrameItem> savedFrames = null;
                lock (_allIcdTabelItemsLock)
                {
                    if (_allIcdTabelItems.ContainsKey(key))
                    {
                        savedFrames = _allIcdTabelItems[key];
                    }
                }

                if (savedFrames != null && savedFrames.Count > 0)
                {
                    IcdFrames.Clear();
                    foreach (var frame in savedFrames)
                    {
                        IcdFrames.Add(CloneIcdFrameItem(frame));
                    }
                    UpdatePagination();
                }
            }
        }

        /// <summary>
        /// 处理ICD表数据请求事件
        /// </summary>
        private void OnIcdTabelItemsRequest(Events.IcdTabelItemsRequestEventArgs args)
        {
            if (args == null)
            {
                args = new Events.IcdTabelItemsRequestEventArgs();
            }

            if (args.IcdTabelItems == null)
            {
                args.IcdTabelItems = new Dictionary<string, List<IcdFrameItem>>();
            }

            // 先保存当前数据
            SaveFramesToMemory();

            // 从静态字典中获取所有数据
            lock (_allIcdTabelItemsLock)
            {
                foreach (var kvp in _allIcdTabelItems)
                {
                    var framesList = kvp.Value?.Where(f => f != null).Select(f => CloneIcdFrameItem(f)).ToList() ?? new List<IcdFrameItem>();
                    args.IcdTabelItems[kvp.Key] = framesList;
                }
            }
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// 获取ICD配置表的唯一键（格式：机箱名/测试任务名/配置表名，如果没有机箱名则使用：测试任务名/配置表名）
        /// </summary>
        private string GetIcdTabelKey()
        {
            if (!string.IsNullOrEmpty(ChassisName))
            {
                return $"{ChassisName}/{TestTaskName}/{ConfigTabelName}";
            }
            return $"{TestTaskName}/{ConfigTabelName}";
        }

        private bool HasProjectGenerationChanged()
        {
            return _localProjectGeneration != GetProjectGeneration();
        }

        private void ResetViewModelState()
        {
            try
            {
                SelectedFrame = null;
                SelectedField = null;

                IcdFrames = new ObservableCollection<IcdFrameItem>();
                PagedFrames = new ObservableCollection<IcdFrameItem>();
                PageNumbers = new ObservableCollection<PaginationButtonInfo>();
                FieldConfigItems = new ObservableCollection<IcdFieldConfigItem>();

                _currentPage = 1;
                RaisePropertyChanged(nameof(CurrentPage));
                PaginationInfo = "显示0条到0条，共0条记录";

                TestTaskName = null;
                ConfigTabelName = null;
                ParentType = null;
                DisplayPath = null;

                UpdatePagination();
            }
            catch (Exception)
            {
            }
        }

        private void OnProjectClosed()
        {
            System.Diagnostics.Debug.WriteLine("[ICD] ProjectClosedEvent received, resetting state.");
            ResetViewModelState();
            _localProjectGeneration = Guid.Empty;
        }

        private void OnProjectOpened(ProjectItem project)
        {
            System.Diagnostics.Debug.WriteLine($"[ICD] ProjectOpenedEvent received for '{project?.Name ?? "unknown"}', resetting state.");
            ResetViewModelState();
            _localProjectGeneration = Guid.Empty;
        }

        /// <summary>
        /// 加载配置表数据
        /// </summary>
        private void LoadConfigTabelData()
        {
            // 初始化IcdFrames集合
            if (IcdFrames == null)
            {
                IcdFrames = new ObservableCollection<IcdFrameItem>();
            }
            IcdFrames.Clear();

            // 从静态字典中加载数据（如果存在）
            string key = GetIcdTabelKey();

            if (!string.IsNullOrEmpty(key))
            {
                List<IcdFrameItem> framesSnapshot = null;
                lock (_allIcdTabelItemsLock)
                {
                    if (_allIcdTabelItems.ContainsKey(key))
                    {
                        var savedFrames = _allIcdTabelItems[key];

                        if (savedFrames != null && savedFrames.Count > 0)
                        {
                            framesSnapshot = new List<IcdFrameItem>();
                            foreach (var frame in savedFrames)
                            {
                                if (frame != null)
                                {
                                    framesSnapshot.Add(CloneIcdFrameItem(frame));
                                }
                            }
                        }
                    }
                }

                if (framesSnapshot != null && framesSnapshot.Count > 0)
                {
                    foreach (var frame in framesSnapshot)
                    {
                        ApplyProtocolSpecificBehaviors(frame);
                        IcdFrames.Add(frame);
                    }
                }
            }

            // 无论是否有数据，都更新分页以显示空行占位符
            UpdatePagination();
        }

        /// <summary>
        /// 保存帧数据到内存
        /// </summary>
        private void SaveFramesToMemory()
        {
            if (!string.IsNullOrEmpty(TestTaskName) && !string.IsNullOrEmpty(ConfigTabelName))
            {
                string key = GetIcdTabelKey();
                var framesToSave = IcdFrames?.Where(f => f != null).Select(f => CloneIcdFrameItem(f)).ToList() ?? new List<IcdFrameItem>();

                lock (_allIcdTabelItemsLock)
                {
                    if (framesToSave.Count == 0 && _allIcdTabelItems.ContainsKey(key))
                    {
                        var existingCollection = _allIcdTabelItems[key];
                        if (existingCollection != null && existingCollection.Count > 0)
                        {
                            return;
                        }
                    }

                    _allIcdTabelItems[key] = new ObservableCollection<IcdFrameItem>(framesToSave);
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
                "signal_config" => "信号配置",
                "icd_config" => "ICD配置",
                "test_sequence" => "测试序列",
                "report" => "报表模板",
                _ => parentType
            };
        }

        /// <summary>
        /// 创建协议字段
        /// </summary>
        private ObservableCollection<IcdFrameField> CreateProtocolFields(string protocol)
        {
            var fields = new ObservableCollection<IcdFrameField>();

            switch (protocol)
            {
                case "RS422":
                    fields.Add(new IcdFrameField
                    {
                        Name = "FrameHeader",
                        DisplayName = "帧头段",
                        BackgroundColor = "#ff6b6b",
                        ConfigItems = CreateRs422FrameHeaderConfigItems()
                    });
                    fields.Add(new IcdFrameField
                    {
                        Name = "DataLength",
                        DisplayName = "数据长度段",
                        BackgroundColor = "#4ecdc4",
                        ConfigItems = CreateRs422DataLengthConfigItems()
                    });
                    fields.Add(new IcdFrameField
                    {
                        Name = "Command",
                        DisplayName = "命令字段",
                        BackgroundColor = "#45b7d1",
                        ConfigItems = CreateRs422CommandConfigItems()
                    });
                    fields.Add(new IcdFrameField
                    {
                        Name = "SourceData",
                        DisplayName = "源数据段",
                        BackgroundColor = "#96ceb4",
                        ConfigItems = CreateRs422SourceDataConfigItems()
                    });
                    fields.Add(new IcdFrameField
                    {
                        Name = "Checksum",
                        DisplayName = "校验和字段",
                        BackgroundColor = "#ffeead",
                        ConfigItems = CreateRs422ChecksumConfigItems()
                    });
                    break;

                case "CAN":
                    fields.Add(new IcdFrameField
                    {
                        Name = "CanFrameId",
                        DisplayName = "帧ID段",
                        BackgroundColor = "#FFB347",
                        ConfigItems = CreateCanHeaderConfigItems()
                    });
                    fields.Add(new IcdFrameField
                    {
                        Name = "DLC",
                        DisplayName = "数据长度段",
                        BackgroundColor = "#4ecdc4",
                        ConfigItems = CreateCanDlcConfigItems()
                    });
                    fields.Add(new IcdFrameField
                    {
                        Name = "DataField",
                        DisplayName = "源数据段",
                        BackgroundColor = "#90CAF9",
                        ConfigItems = CreateCanDataFieldConfigItems()
                    });
                    break;

                case "ARINC429":
                    fields.Add(new IcdFrameField
                    {
                        Name = "Label",
                        DisplayName = "LABEL",
                        BackgroundColor = "#C33D3D",
                        ConfigItems = CreateArinc429LabelConfigItems()
                    });
                    fields.Add(new IcdFrameField
                    {
                        Name = "SD",
                        DisplayName = "SD",
                        BackgroundColor = "#DF5721",
                        ConfigItems = CreateArinc429SdConfigItems()
                    });
                    fields.Add(new IcdFrameField
                    {
                        Name = "Data",
                        DisplayName = "DATA",
                        BackgroundColor = "#F0EB10",
                        ConfigItems = CreateArinc429DataConfigItems()
                    });
                    fields.Add(new IcdFrameField
                    {
                        Name = "Sign",
                        DisplayName = "SIGN",
                        BackgroundColor = "#21DF2A",
                        ConfigItems = CreateArinc429SignConfigItems()
                    });
                    fields.Add(new IcdFrameField
                    {
                        Name = "SSM",
                        DisplayName = "SSM",
                        BackgroundColor = "#03FDDF",
                        ConfigItems = CreateArinc429SsmConfigItems()
                    });
                    fields.Add(new IcdFrameField
                    {
                        Name = "Parity",
                        DisplayName = "PARITY",
                        BackgroundColor = "#01CFFF",
                        ConfigItems = CreateArinc429ParityConfigItems()
                    });
                    break;

                case "MIL1394":
                    fields.Add(new IcdFrameField
                    {
                        Name = "Label",
                        DisplayName = "标号段",
                        BackgroundColor = "#FA8DFD",
                        ConfigItems = CreateMil1394LabelConfigItems()
                    });
                    fields.Add(new IcdFrameField
                    {
                        Name = "PayloadLength",
                        DisplayName = "负载数据长度段",
                        BackgroundColor = "#909090",
                        ConfigItems = CreateMil1394PayloadLengthConfigItems()
                    });
                    break;
            }

            return fields;
        }

        private void ApplyProtocolSpecificBehaviors(IcdFrameItem frame)
            => ApplyProtocolSpecificBehaviors(frame?.Protocol, frame?.Fields);

        private void ApplyProtocolSpecificBehaviors(string protocol, ObservableCollection<IcdFrameField> fields)
        {
            if (string.IsNullOrEmpty(protocol) || fields == null)
                return;

            if (protocol.Equals("CAN", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var field in fields)
                {
                    if (field == null)
                        continue;

                    if (string.Equals(field.Name, "CanFrameId", StringComparison.OrdinalIgnoreCase))
                    {
                        AttachCanHeaderBehavior(field);
                    }
                }
            }
        }

        private void AttachCanHeaderBehavior(IcdFrameField field)
        {
            if (field?.ConfigItems == null)
                return;

            var directionItem = field.ConfigItems.FirstOrDefault(ci => ci?.Name == "方向");
            var sendPeriodItem = field.ConfigItems.FirstOrDefault(ci => ci?.Name == "发送周期(ms)");

            if (directionItem == null || sendPeriodItem == null)
                return;

            void UpdateVisibility()
            {
                sendPeriodItem.IsVisible = string.Equals(directionItem.Value, "Tx", StringComparison.OrdinalIgnoreCase);
            }

            UpdateVisibility();

            directionItem.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(IcdFieldConfigItem.Value))
                {
                    UpdateVisibility();
                }
            };
        }

        // RS422字段配置项创建方法
        private ObservableCollection<IcdFieldConfigItem> CreateRs422FrameHeaderConfigItems()
        {
            return new ObservableCollection<IcdFieldConfigItem>
            {
                new IcdFieldConfigItem { Name = "帧头内容", Value = "0xAA55", ConfigType = "TextBox" },
                new IcdFieldConfigItem { Name = "帧头长度", Value = "2", ConfigType = "TextBox" },
                new IcdFieldConfigItem
                {
                    Name = "字节顺序",
                    Value = "大端",
                    ConfigType = "ComboBox",
                    Options = new ObservableCollection<string> { "大端", "小端" }
                },
                new IcdFieldConfigItem
                {
                    Name = "匹配方式",
                    Value = "固定值",
                    ConfigType = "ComboBox",
                    Options = new ObservableCollection<string> { "固定值", "动态匹配" }
                },
                new IcdFieldConfigItem
                {
                    Name = "是否参与校验",
                    Value = "是",
                    ConfigType = "ComboBox",
                    Options = new ObservableCollection<string> { "是", "否" }
                }
            };
        }

        private ObservableCollection<IcdFieldConfigItem> CreateRs422DataLengthConfigItems()
        {
            return new ObservableCollection<IcdFieldConfigItem>
            {
                new IcdFieldConfigItem { Name = "长度位置", Value = "1", ConfigType = "TextBox" },
                new IcdFieldConfigItem { Name = "长度字节数", Value = "1", ConfigType = "TextBox" },
                new IcdFieldConfigItem
                {
                    Name = "长度计算方式",
                    Value = "数据段长度",
                    ConfigType = "ComboBox",
                    Options = new ObservableCollection<string> { "数据段长度", "总帧长度" }
                },
                new IcdFieldConfigItem
                {
                    Name = "字节顺序",
                    Value = "小端",
                    ConfigType = "ComboBox",
                    Options = new ObservableCollection<string> { "大端", "小端" }
                }
            };
        }

        private ObservableCollection<IcdFieldConfigItem> CreateRs422CommandConfigItems()
        {
            return new ObservableCollection<IcdFieldConfigItem>
            {
                new IcdFieldConfigItem { Name = "命令码位置", Value = "2", ConfigType = "TextBox" },
                new IcdFieldConfigItem { Name = "命令码长度", Value = "1", ConfigType = "TextBox" },
                new IcdFieldConfigItem
                {
                    Name = "命令含义映射表",
                    Value = "0x01:状态查询;0x02:设置参数",
                    ConfigType = "TextBox"
                },
                new IcdFieldConfigItem
                {
                    Name = "字节顺序",
                    Value = "小端",
                    ConfigType = "ComboBox",
                    Options = new ObservableCollection<string> { "大端", "小端" }
                }
            };
        }

        private ObservableCollection<IcdFieldConfigItem> CreateRs422SourceDataConfigItems()
        {
            return new ObservableCollection<IcdFieldConfigItem>
            {
                new IcdFieldConfigItem { Name = "起始位置", Value = "3", ConfigType = "TextBox" },
                new IcdFieldConfigItem { Name = "数据长度", Value = "10", ConfigType = "TextBox" },
                new IcdFieldConfigItem
                {
                    Name = "数据格式",
                    Value = "Int16",
                    ConfigType = "ComboBox",
                    Options = new ObservableCollection<string> { "Int8", "Int16", "Int32", "Float32", "BCD", "ASCII" }
                },
                new IcdFieldConfigItem
                {
                    Name = "字节顺序",
                    Value = "小端",
                    ConfigType = "ComboBox",
                    Options = new ObservableCollection<string> { "大端", "小端" }
                },
                new IcdFieldConfigItem { Name = "数据含义描述", Value = "温度(℃)", ConfigType = "TextBox" },
                new IcdFieldConfigItem
                {
                    Name = "缩放因子",
                    Value = "0.1",
                    ConfigType = "TextBox"
                }
            };
        }

        private ObservableCollection<IcdFieldConfigItem> CreateRs422ChecksumConfigItems()
        {
            return new ObservableCollection<IcdFieldConfigItem>
            {
                new IcdFieldConfigItem
                {
                    Name = "校验类型",
                    Value = "CRC16",
                    ConfigType = "ComboBox",
                    Options = new ObservableCollection<string> { "SUM", "XOR", "CRC8", "CRC16", "CRC32" }
                },
                new IcdFieldConfigItem
                {
                    Name = "存储格式",
                    Value = "Hex",
                    ConfigType = "ComboBox",
                    Options = new ObservableCollection<string> { "Hex", "Decimal", "Binary" }
                },
                new IcdFieldConfigItem
                {
                    Name = "校验最小单位",
                    Value = "字节",
                    ConfigType = "ComboBox",
                    Options = new ObservableCollection<string> { "字节", "字(2字节)" }
                },
                new IcdFieldConfigItem { Name = "校验起始段", Value = "帧头段", ConfigType = "ComboBox" },
                new IcdFieldConfigItem { Name = "校验终止段", Value = "源数据段", ConfigType = "ComboBox" },
                new IcdFieldConfigItem
                {
                    Name = "是否包含自身",
                    Value = "否",
                    ConfigType = "ComboBox",
                    Options = new ObservableCollection<string> { "是", "否" }
                },
                new IcdFieldConfigItem
                {
                    Name = "字节顺序",
                    Value = "小端",
                    ConfigType = "ComboBox",
                    Options = new ObservableCollection<string> { "大端", "小端" }
                }
            };
        }

        // ARINC429字段配置项创建方法
        private ObservableCollection<IcdFieldConfigItem> CreateArinc429LabelConfigItems()
        {
            return new ObservableCollection<IcdFieldConfigItem>
            {
                new IcdFieldConfigItem { Name = "字节位置", Value = "0", ConfigType = "TextBox" },
                new IcdFieldConfigItem { Name = "长度", Value = "1", ConfigType = "TextBox" },
                new IcdFieldConfigItem { Name = "默认值", Value = "0x00", ConfigType = "TextBox" }
            };
        }

        private ObservableCollection<IcdFieldConfigItem> CreateArinc429SdConfigItems()
        {
            return new ObservableCollection<IcdFieldConfigItem>
            {
                new IcdFieldConfigItem { Name = "字节位置", Value = "1", ConfigType = "TextBox" },
                new IcdFieldConfigItem { Name = "长度", Value = "1", ConfigType = "TextBox" },
                new IcdFieldConfigItem { Name = "SDI值", Value = "0", ConfigType = "TextBox" }
            };
        }

        private ObservableCollection<IcdFieldConfigItem> CreateArinc429DataConfigItems()
        {
            return new ObservableCollection<IcdFieldConfigItem>
            {
                new IcdFieldConfigItem { Name = "起始位置", Value = "2", ConfigType = "TextBox" },
                new IcdFieldConfigItem { Name = "长度", Value = "19", ConfigType = "TextBox" },
                new IcdFieldConfigItem
                {
                    Name = "数据格式",
                    Value = "BCD",
                    ConfigType = "ComboBox",
                    Options = new ObservableCollection<string> { "BCD", "BNR", "离散" }
                },
                new IcdFieldConfigItem { Name = "缩放因子", Value = "0.01", ConfigType = "TextBox" },
                new IcdFieldConfigItem { Name = "偏移量", Value = "0", ConfigType = "TextBox" },
                new IcdFieldConfigItem { Name = "单位", Value = "", ConfigType = "TextBox" },
                new IcdFieldConfigItem { Name = "符号位", Value = "位19", ConfigType = "TextBox" }
            };
        }

        private ObservableCollection<IcdFieldConfigItem> CreateArinc429SignConfigItems()
        {
            return new ObservableCollection<IcdFieldConfigItem>
            {
                new IcdFieldConfigItem { Name = "符号位位置", Value = "位19", ConfigType = "TextBox" },
                new IcdFieldConfigItem
                {
                    Name = "符号规则",
                    Value = "正数",
                    ConfigType = "ComboBox",
                    Options = new ObservableCollection<string> { "正数", "负数", "无符号" }
                }
            };
        }

        private ObservableCollection<IcdFieldConfigItem> CreateArinc429SsmConfigItems()
        {
            return new ObservableCollection<IcdFieldConfigItem>
            {
                new IcdFieldConfigItem { Name = "SSM位位置", Value = "位29-30", ConfigType = "TextBox" },
                new IcdFieldConfigItem { Name = "SSM值", Value = "正常", ConfigType = "ComboBox" }
            };
        }

        private ObservableCollection<IcdFieldConfigItem> CreateArinc429ParityConfigItems()
        {
            return new ObservableCollection<IcdFieldConfigItem>
            {
                new IcdFieldConfigItem { Name = "校验位位置", Value = "位31", ConfigType = "TextBox" },
                new IcdFieldConfigItem
                {
                    Name = "校验类型",
                    Value = "奇校验",
                    ConfigType = "ComboBox",
                    Options = new ObservableCollection<string> { "奇校验", "偶校验" }
                }
            };
        }

        // MIL1394字段配置项创建方法
        private ObservableCollection<IcdFieldConfigItem> CreateMil1394LabelConfigItems()
        {
            return new ObservableCollection<IcdFieldConfigItem>
            {
                new IcdFieldConfigItem { Name = "起始字节", Value = "0", ConfigType = "TextBox" },
                new IcdFieldConfigItem { Name = "长度", Value = "2", ConfigType = "TextBox" },
                new IcdFieldConfigItem { Name = "标号值", Value = "0x0000", ConfigType = "TextBox" }
            };
        }

        private ObservableCollection<IcdFieldConfigItem> CreateMil1394PayloadLengthConfigItems()
        {
            return new ObservableCollection<IcdFieldConfigItem>
            {
                new IcdFieldConfigItem { Name = "字节位置", Value = "2", ConfigType = "TextBox" },
                new IcdFieldConfigItem { Name = "长度", Value = "2", ConfigType = "TextBox" },
                new IcdFieldConfigItem
                {
                    Name = "长度单位",
                    Value = "字节",
                    ConfigType = "ComboBox",
                    Options = new ObservableCollection<string> { "字节", "字" }
                }
            };
        }

        // CAN字段配置项创建方法
        private ObservableCollection<IcdFieldConfigItem> CreateCanHeaderConfigItems()

        {

            var items = new ObservableCollection<IcdFieldConfigItem>();



            items.Add(new IcdFieldConfigItem

            {

                Name = "帧类型",

                Value = "标准帧",

                ConfigType = "TextBlock",

                Options = new ObservableCollection<string> { "标准帧", "扩展帧" }

            });



            items.Add(new IcdFieldConfigItem { Name = "帧ID (Hex)", Value = "0x180", ConfigType = "TextBox" });



            var directionItem = new IcdFieldConfigItem

            {

                Name = "方向",

                Value = "Tx",

                ConfigType = "ComboBox",

                Options = new ObservableCollection<string> { "Tx", "Rx" }

            };

            items.Add(directionItem);



            items.Add(new IcdFieldConfigItem

            {

                Name = "远程帧标志(RTR)",

                Value = "否",

                ConfigType = "ComboBox",

                Options = new ObservableCollection<string> { "否", "是" }

            });



            var sendPeriodItem = new IcdFieldConfigItem

            {

                Name = "发送周期(ms)",

                Value = "1000",

                ConfigType = "TextBox"

            };

            sendPeriodItem.IsVisible = string.Equals(directionItem.Value, "Tx", StringComparison.OrdinalIgnoreCase);



            items.Add(sendPeriodItem);

            return items;

        }



        private ObservableCollection<IcdFieldConfigItem> CreateCanDlcConfigItems()

        {

            return new ObservableCollection<IcdFieldConfigItem>

            {

                new IcdFieldConfigItem

                {

                    Name = "数据长度 (DLC)",

                    Value = "8",

                    ConfigType = "ComboBox",

                    Options = new ObservableCollection<string> { "0", "1", "2", "3", "4", "5", "6", "7", "8" }

                },

                new IcdFieldConfigItem

                {

                    Name = "填充方式",

                    Value = "补零",

                    ConfigType = "ComboBox",

                    Options = new ObservableCollection<string> { "补零", "保留原值" }

                }

            };

        }



        private ObservableCollection<IcdFieldConfigItem> CreateCanDataFieldConfigItems()
        {
            return new ObservableCollection<IcdFieldConfigItem>();
        }

        /// <summary>
        /// 更新字段配置项显示
        /// </summary>
        private void UpdateFieldConfigItems()
        {
            FieldConfigItems.Clear();

            if (SelectedField != null && SelectedField.ConfigItems != null)
            {
                foreach (var configItem in SelectedField.ConfigItems)
                {
                    FieldConfigItems.Add(configItem);
                }
            }
        }

        /// <summary>
        /// 更新分页
        /// </summary>
        private void UpdatePagination()
        {
            UpdatePagedFrames();
            UpdatePaginationInfo();
            UpdatePageNumbers();
            PreviousPageCommand.RaiseCanExecuteChanged();
            NextPageCommand.RaiseCanExecuteChanged();
        }

        private void UpdatePagedFrames()
        {
            if (PagedFrames == null)
            {
                PagedFrames = new ObservableCollection<IcdFrameItem>();
            }

            PagedFrames.Clear();

            int currentPageStartIndex = (CurrentPage - 1) * PageSize;

            if (IcdFrames == null || IcdFrames.Count == 0)
            {
                // 添加10个空行占位符，Index从当前页开始计算
                for (int i = 0; i < PageSize; i++)
                {
                    var emptyFrame = new IcdFrameItem
                    {
                        IsEmpty = true,
                        Index = currentPageStartIndex + i + 1,
                        FrameName = $"ICD帧{currentPageStartIndex + i + 1}"
                    };
                    PagedFrames.Add(emptyFrame);
                }
                return;
            }

            // 添加当前页的数据
            int startIndex = (CurrentPage - 1) * PageSize;
            int endIndex = Math.Min(startIndex + PageSize, IcdFrames.Count);

            for (int i = startIndex; i < endIndex; i++)
            {
                var frame = IcdFrames[i];
                if (frame != null)
                {
                    frame.IsEmpty = false;
                    // 确保Index正确（基于全局位置）
                    frame.Index = i + 1;
                }
                PagedFrames.Add(frame);
            }

            // 如果当前页不足10行，添加空行填充
            while (PagedFrames.Count < PageSize)
            {
                var emptyFrame = new IcdFrameItem
                {
                    IsEmpty = true,
                    Index = currentPageStartIndex + PagedFrames.Count + 1,
                    FrameName = $"ICD帧{currentPageStartIndex + PagedFrames.Count + 1}"
                };
                PagedFrames.Add(emptyFrame);
            }
        }

        private void UpdatePaginationInfo()
            => PaginationInfo = PaginationHelper.GetPaginationInfo(IcdFrames?.Count ?? 0, CurrentPage, PageSize);

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

        private void OnNextPage()
        {
            if (CurrentPage < TotalPages)
            {
                CurrentPage++;
            }
        }

        #endregion

        #region Command Handlers

        private void OnAddIcdFrame()
        {
            // 使用当前选中的协议；如果为空则回退到已有帧或可用协议列表的首项
            string selectedProtocol = SelectedProtocol;
            if (string.IsNullOrWhiteSpace(selectedProtocol))
            {
                selectedProtocol = IcdFrames?.FirstOrDefault(f => !string.IsNullOrWhiteSpace(f?.Protocol))?.Protocol
                    ?? AvailableProtocols?.FirstOrDefault()
                    ?? "CAN";
                SelectedProtocol = selectedProtocol;
            }

            // 计算正确的Index（基于总数量，而不是当前页）
            int newIndex = IcdFrames.Count + 1;

            // 创建新的ICD帧
            var newFrame = new IcdFrameItem
            {
                Index = newIndex,
                FrameName = $"ICD帧{newIndex}",
                FrameId = $"Frame{newIndex}",
                Protocol = selectedProtocol,
                Remarks = "",
                Fields = CreateProtocolFields(selectedProtocol),
                IsEmpty = false
            };

            ApplyProtocolSpecificBehaviors(newFrame);
            IcdFrames.Add(newFrame);
            SaveFramesToMemory();
            UpdatePagination();
        }

        private void OnDeleteIcdFrame(IcdFrameItem frame)
        {
            if (frame != null)
            {
                bool wasSelected = ReferenceEquals(SelectedFrame, frame);
                IcdFrames.Remove(frame);
                // 更新索引
                for (int i = 0; i < IcdFrames.Count; i++)
                {
                    IcdFrames[i].Index = i + 1;
                }
                SaveFramesToMemory();
                UpdatePagination();
                if (wasSelected)
                {
                    var nextFrame = IcdFrames.FirstOrDefault(f => f != null && !f.IsEmpty);
                    OnSelectFrame(nextFrame);
                }
            }
        }

        private void OnEditIcdFrame(IcdFrameItem frame)
        {
            if (frame != null)
            {
                // TODO: 实现编辑ICD帧逻辑（可以打开编辑对话框）
            }
        }

        private void SetSelectedFrame(IcdFrameItem frame)
        {
            if (IcdFrames != null)
            {
                foreach (var item in IcdFrames)
                {
                    if (item != null)
                    {
                        item.IsSelected = ReferenceEquals(item, frame);
                    }
                }
            }

            SelectedFrame = frame;
        }

        private void OnSelectFrame(IcdFrameItem frame)
        {
            if (frame == null || frame.IsEmpty)
            {
                SetSelectedFrame(null);
                OnSelectField(null);
                return;
            }

            SetSelectedFrame(frame);

            if (SelectedField == null || frame.Fields == null || !frame.Fields.Contains(SelectedField))
            {
                var firstField = frame.Fields?.FirstOrDefault();
                OnSelectField(firstField);
            }
            else
            {
                OnSelectField(SelectedField);
            }
        }

        private void OnSelectField(IcdFrameField field)
        {
            System.Diagnostics.Debug.WriteLine($"[ICD] OnSelectField invoked with: {(field == null ? "null" : field.DisplayName ?? field.Name)}");
            System.Diagnostics.Trace.WriteLine($"[ICD] OnSelectField invoked with: {(field == null ? "null" : field.DisplayName ?? field.Name)}");
            // 先重置全部字段的选中状态，再设置新的选中字段
            if (IcdFrames != null)
            {
                foreach (var frameItem in IcdFrames)
                {
                    if (frameItem?.Fields == null) continue;
                    foreach (var f in frameItem.Fields)
                    {
                        f.IsSelected = ReferenceEquals(f, field);
                    }
                }
            }

            SelectedField = field;
            if (field != null)
            {
                var ownerFrame = IcdFrames?.FirstOrDefault(f => f?.Fields != null && f.Fields.Contains(field));
                SetSelectedFrame(ownerFrame);
            }
        }

        private void OnAddIcdMapping()
        {
            // 准备可用的ICD表列表（这里需要根据实际需求获取）
            var availableIcdTabels = new System.Collections.ObjectModel.ObservableCollection<string>
            {
                "CAN配置表1",
                "CAN配置表2",
                // TODO: 从项目数据中动态获取可用的ICD表
            };

            // 准备可用的帧列表（从当前ICD配置表中获取）
            var availableFrames = new System.Collections.ObjectModel.ObservableCollection<IcdFrameItem>();
            if (IcdFrames != null)
            {
                foreach (var frame in IcdFrames.Where(f => f != null && !f.IsEmpty))
                {
                    availableFrames.Add(frame);
                }
            }

            // 显示添加ICD映射对话框
            var mappingResult = _dialogService.ShowAddIcdMappingDialog(availableIcdTabels, availableFrames);

            if (mappingResult != null)
            {
                // 处理返回的映射结果
                System.Diagnostics.Debug.WriteLine($"[ICD] 添加映射成功: {mappingResult.SignalId}");

                // TODO: 将映射结果保存到相应的映射表中
                // 可以发布事件给IcdMappingTabelViewModel处理
                // _eventAggregator.GetEvent<Events.IcdMappingAddedEvent>().Publish(mappingResult);
            }
        }

        private void OnFloatWindow()
        {
            ReMessageBox.Show("浮动功能需要在View中实现");
        }

        private void OnMinimizeInRegion()
        {
            ReMessageBox.Show("最小化功能待实现");
        }

        private void OnCloseInRegion()
        {
            var result = ReMessageBox.Show("确定要关闭当前配置表吗？", "确认", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
            if (result == System.Windows.MessageBoxResult.Yes)
            {
                string pageKey = $"IcdConfigTabel_{TestTaskName}-{ConfigTabelName}";
                _eventAggregator.GetEvent<Events.ReleaseCurrentPageEvent>().Publish(pageKey);
            }
        }

        #endregion
    }
}

