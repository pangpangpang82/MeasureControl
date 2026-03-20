using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using MeasureControl.Helpers;
using MeasureControl.Models;
using MeasureControl.Services;
using MeasureControl.ViewModels.IcdConfig;
using MeasureControl.Views;
using MeasureControl.Views.Dialogs;
using Prism.Commands;
using Prism.Events;
using Prism.Mvvm;
using Prism.Regions;

namespace MeasureControl.ViewModels
{
    /// <summary>
    /// ICD映射表 ViewModel（包含数据、分页、命令及导航逻辑）
    /// </summary>
    public class IcdMappingTabelViewModel : BindableBase, INavigationAware, IDisposable
    {
        private readonly IRegionManager _regionManager;
        private readonly IEventAggregator _eventAggregator;
        private readonly ProjectService _projectService;
        private readonly MeasureControl.Services.IDialogService _dialogService;
        private bool _disposed;

        // 静态存储所有通讯变量表数据（key: 测试任务名/配置表名）
        private static readonly Dictionary<string, ObservableCollection<IcdMappingItem>> _allIcdMappingItems = new Dictionary<string, ObservableCollection<IcdMappingItem>>();
        private static readonly object _allIcdMappingItemsLock = new object();
        private static readonly object _projectGenerationLock = new object();
        private static Guid _currentProjectGeneration = Guid.NewGuid();

        private const int PageSize = 18;
        private int _currentPage = 1;
        private Guid _localProjectGeneration = Guid.Empty;

        private string _chassisName;
        
        public string ChassisName
        {
            get => _chassisName;
            set => SetProperty(ref _chassisName, value);
        }

        public string TestTaskName
        {
            get => _testTaskName;
            set => SetProperty(ref _testTaskName, value);
        }
        private string _testTaskName;

        public string ConfigTabelName
        {
            get => _configTabelName;
            set => SetProperty(ref _configTabelName, value);
        }
        private string _configTabelName;

        public string ParentType
        {
            get => _parentType;
            set => SetProperty(ref _parentType, value);
        }
        private string _parentType;

        public string DisplayPath
        {
            get => _displayPath;
            set => SetProperty(ref _displayPath, value);
        }
        private string _displayPath;

        /// <summary>占位的通道树数据（UI保留，逻辑不实现）</summary>
        public ObservableCollection<ChannelTreeNode> ChannelBindingTreeRoot { get; }

        private ObservableCollection<IcdMappingItem> _signals;
        /// <summary>信号配置列表</summary>
        public ObservableCollection<IcdMappingItem> Signals
        {
            get => _signals;
            set => SetProperty(ref _signals, value);
        }

        private ObservableCollection<IcdMappingItem> _pagedSignals;
        /// <summary>当前页显示的信号列表</summary>
        public ObservableCollection<IcdMappingItem> PagedSignals
        {
            get => _pagedSignals;
            set => SetProperty(ref _pagedSignals, value);
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

        #region Commands

        public DelegateCommand AddSignalCommand { get; }
        public DelegateCommand<IcdMappingItem> DeleteSignalCommand { get; }
        public DelegateCommand<IcdMappingItem> EditSignalCommand { get; }
        public DelegateCommand PreviousPageCommand { get; }
        public DelegateCommand NextPageCommand { get; }
        public DelegateCommand FloatWindowCommand { get; }
        public DelegateCommand MinimizeInRegionCommand { get; }
        public DelegateCommand CloseInRegionCommand { get; }

        #endregion

        public IcdMappingTabelViewModel(
            IRegionManager regionManager,
            IEventAggregator eventAggregator,
            ProjectService projectService,
            MeasureControl.Services.IDialogService dialogService)
        {
            _regionManager = regionManager ?? throw new ArgumentNullException(nameof(regionManager));
            _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
            _projectService = projectService ?? throw new ArgumentNullException(nameof(projectService));
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));

            ChannelBindingTreeRoot = new ObservableCollection<ChannelTreeNode>();

            // 初始化命令
            AddSignalCommand = new DelegateCommand(OnAddSignal);
            DeleteSignalCommand = new DelegateCommand<IcdMappingItem>(OnDeleteSignal);
            EditSignalCommand = new DelegateCommand<IcdMappingItem>(OnEditSignal);
            PreviousPageCommand = new DelegateCommand(OnPreviousPage, CanGoToPreviousPage);
            NextPageCommand = new DelegateCommand(OnNextPage, CanGoToNextPage);

            FloatWindowCommand = new DelegateCommand(OnFloatWindow);
            MinimizeInRegionCommand = new DelegateCommand(OnMinimizeInRegion);
            CloseInRegionCommand = new DelegateCommand(OnCloseInRegion);

            // 初始化集合
            Signals = new ObservableCollection<IcdMappingItem>();
            PagedSignals = new ObservableCollection<IcdMappingItem>();
            PageNumbers = new ObservableCollection<PaginationButtonInfo>();

            Signals.CollectionChanged += Signals_CollectionChanged;

            // 订阅事件
            _eventAggregator.GetEvent<Events.IcdMappingItemsRequestEvent>().Subscribe(OnIcdMappingItemsRequest, ThreadOption.UIThread);
            _eventAggregator.GetEvent<Events.ProjectClosedEvent>().Subscribe(OnProjectClosed, ThreadOption.UIThread);
            _eventAggregator.GetEvent<Events.ProjectOpenedEvent>().Subscribe(OnProjectOpened, ThreadOption.UIThread);
        }

        #region INavigationAware

        public void OnNavigatedTo(NavigationContext navigationContext)
        {
            string chassisName = navigationContext.Parameters.ContainsKey("ChassisName")
                ? navigationContext.Parameters["ChassisName"] as string
                : null;
            string testTaskName = navigationContext.Parameters.ContainsKey("TestTaskName")
                ? navigationContext.Parameters["TestTaskName"] as string
                : null;
            string configTabelName = navigationContext.Parameters.ContainsKey("ConfigTabelName")
                ? navigationContext.Parameters["ConfigTabelName"] as string
                : null;
            string parentType = navigationContext.Parameters.ContainsKey("ParentType")
                ? navigationContext.Parameters["ParentType"] as string
                : null;

            ChassisName = chassisName ?? string.Empty;
            Initialize(testTaskName, configTabelName, parentType);
        }

        public bool IsNavigationTarget(NavigationContext navigationContext) => false;

        public void OnNavigatedFrom(NavigationContext navigationContext)
        {
            SaveSignalsToMemory();
            UnsubscribeFromAllSignalPropertyChanged();
            _eventAggregator.GetEvent<Events.IcdMappingItemsRequestEvent>().Unsubscribe(OnIcdMappingItemsRequest);
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_disposed) return;

            SaveSignalsToMemory();
            UnsubscribeFromAllSignalPropertyChanged();

            if (Signals != null)
            {
                Signals.CollectionChanged -= Signals_CollectionChanged;
            }

            _eventAggregator.GetEvent<Events.IcdMappingItemsRequestEvent>().Unsubscribe(OnIcdMappingItemsRequest);
            _eventAggregator.GetEvent<Events.ProjectClosedEvent>().Unsubscribe(OnProjectClosed);
            _eventAggregator.GetEvent<Events.ProjectOpenedEvent>().Unsubscribe(OnProjectOpened);

            _disposed = true;
        }

        #endregion

        #region Static Helpers

        public static Dictionary<string, List<IcdMappingItem>> GetAllIcdMappingItems()
        {
            lock (_allIcdMappingItemsLock)
            {
                return _allIcdMappingItems.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value?.Select(s => CloneCommunicatingSignalConfigItem(s)).ToList()
                           ?? new List<IcdMappingItem>());
            }
        }

        public static void ClearAllIcdMappingItems()
        {
            lock (_allIcdMappingItemsLock) { _allIcdMappingItems.Clear(); }
            BumpProjectGeneration();
        }

        public static void LoadIcdMappingItems(Dictionary<string, List<IcdMappingItem>> items)
        {
            lock (_allIcdMappingItemsLock)
            {
                _allIcdMappingItems.Clear();
                if (items == null) return;
                foreach (var kvp in items)
                    _allIcdMappingItems[kvp.Key] = new ObservableCollection<IcdMappingItem>(
                        kvp.Value?.Where(s => s != null).Select(s => CloneCommunicatingSignalConfigItem(s))
                        ?? Enumerable.Empty<IcdMappingItem>());
            }
        }

        private static Guid GetProjectGeneration() { lock (_projectGenerationLock) return _currentProjectGeneration; }
        private static void BumpProjectGeneration() { lock (_projectGenerationLock) _currentProjectGeneration = Guid.NewGuid(); }

        private static IcdMappingItem CloneCommunicatingSignalConfigItem(IcdMappingItem source)
        {
            if (source == null) return null;

            return new IcdMappingItem
            {
                SignalId = source.SignalId,
                Description = source.Description,
                IcdTabelId = source.IcdTabelId,
                FrameId = source.FrameId,
                DataType = source.DataType,
                BitLength = source.BitLength,
                Direction = source.Direction,
                Cycle = source.Cycle,
                MessageId = source.MessageId,
                Channel = source.Channel,
                Dlc = source.Dlc,
                CalibrationFormula = source.CalibrationFormula,
                ConcatCount = source.ConcatCount,
                DoubleWordBits = source.DoubleWordBits,
                PositionInWord = source.PositionInWord
            };
        }

        #endregion

        #region 初始化与状态

        private string GetSignalTabelKey()
        {
            if (!string.IsNullOrEmpty(ChassisName))
            {
                return $"{ChassisName}/{TestTaskName}/{ConfigTabelName}";
            }
            return $"{TestTaskName}/{ConfigTabelName}";
        }

        private bool HasProjectGenerationChanged() => _localProjectGeneration != GetProjectGeneration();

        public void Initialize(string testTaskName, string configTabelName, string parentType)
        {
            bool contextChanged =
                !string.Equals(TestTaskName, testTaskName, StringComparison.Ordinal) ||
                !string.Equals(ConfigTabelName, configTabelName, StringComparison.Ordinal) ||
                !string.Equals(ParentType, parentType, StringComparison.Ordinal);

            bool projectGenerationChanged = HasProjectGenerationChanged();

            if (projectGenerationChanged)
            {
                ResetViewModelState();
            }

            if (!contextChanged && !projectGenerationChanged && Signals != null && Signals.Count > 0)
            {
                return;
            }

            if (!string.IsNullOrEmpty(TestTaskName) && !string.IsNullOrEmpty(ConfigTabelName))
            {
                SaveSignalsToMemory();
                UnsubscribeFromAllSignalPropertyChanged();
            }

            TestTaskName = testTaskName;
            ConfigTabelName = configTabelName;
            ParentType = parentType;

            string parentName = GetParentDisplayName(ParentType);
            if (!string.IsNullOrEmpty(ChassisName))
            {
                DisplayPath = $"{ChassisName}/{TestTaskName}/{parentName}/{ConfigTabelName}";
            }
            else
            {
                DisplayPath = $"{TestTaskName}/{parentName}/{ConfigTabelName}";
            }

            _localProjectGeneration = GetProjectGeneration();
            LoadConfigTabelData();
            CurrentPage = 1;
        }

        private void ResetViewModelState()
        {
            try
            {
                UnsubscribeFromAllSignalPropertyChanged();

                if (Signals != null)
                {
                    Signals.CollectionChanged -= Signals_CollectionChanged;
                }

                Signals = new ObservableCollection<IcdMappingItem>();
                Signals.CollectionChanged += Signals_CollectionChanged;

                if (PagedSignals == null)
                {
                    PagedSignals = new ObservableCollection<IcdMappingItem>();
                }
                else
                {
                    PagedSignals.Clear();
                }

                if (PageNumbers == null)
                {
                    PageNumbers = new ObservableCollection<PaginationButtonInfo>();
                }
                else
                {
                    PageNumbers.Clear();
                }

                _currentPage = 1;
                RaisePropertyChanged(nameof(CurrentPage));

                PaginationInfo = "共 0 条记录";

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
            ResetViewModelState();
            _localProjectGeneration = Guid.Empty;
        }

        private void OnProjectOpened(ProjectItem project)
        {
            ResetViewModelState();
            _localProjectGeneration = Guid.Empty;
        }

        #endregion

        #region 数据加载/保存

        private void LoadConfigTabelData()
        {
            if (Signals == null)
            {
                Signals = new ObservableCollection<IcdMappingItem>();
            }

            Signals.CollectionChanged -= Signals_CollectionChanged;
            Signals.Clear();

            string key = GetSignalTabelKey();

            if (string.IsNullOrEmpty(TestTaskName) || string.IsNullOrEmpty(ConfigTabelName))
            {
                Signals.CollectionChanged += Signals_CollectionChanged;
                UpdatePagination();
                return;
            }

            if (!string.IsNullOrEmpty(key))
            {
                List<IcdMappingItem> signalsSnapshot = null;
                lock (_allIcdMappingItemsLock)
                {
                    if (_allIcdMappingItems.ContainsKey(key))
                    {
                        var savedSignals = _allIcdMappingItems[key];
                        if (savedSignals != null && savedSignals.Count > 0)
                        {
                            signalsSnapshot = new List<IcdMappingItem>();
                            foreach (var sig in savedSignals)
                            {
                                if (sig != null)
                                {
                                    signalsSnapshot.Add(CloneCommunicatingSignalConfigItem(sig));
                                }
                            }
                        }
                    }
                }

                if (signalsSnapshot != null && signalsSnapshot.Count > 0)
                {
                    foreach (var signal in signalsSnapshot)
                    {
                        Signals.Add(signal);
                        SubscribeToSignalPropertyChanged(signal);
                    }
                }
            }

            Signals.CollectionChanged += Signals_CollectionChanged;
            UpdatePagination();
        }

        private void SaveSignalsToMemory()
        {
            string key = GetSignalTabelKey();
            if (string.IsNullOrEmpty(key))
                return;

            lock (_allIcdMappingItemsLock)
            {
                var signalsCollection = new ObservableCollection<IcdMappingItem>();
                foreach (var signal in Signals)
                {
                    var newSignal = CloneCommunicatingSignalConfigItem(signal);
                    signalsCollection.Add(newSignal);
                }

                if (signalsCollection.Count == 0 && _allIcdMappingItems.ContainsKey(key))
                {
                    var existingCollection = _allIcdMappingItems[key];
                    if (existingCollection != null && existingCollection.Count > 0)
                    {
                        return;
                    }
                }

                _allIcdMappingItems[key] = signalsCollection;
            }
        }

        #endregion

        #region 事件处理

        private void OnIcdMappingItemsRequest(Events.IcdMappingItemsRequestEventArgs args)
        {
            if (args == null) return;

            if (args.SignalTabelItems == null)
            {
                args.SignalTabelItems = new Dictionary<string, List<IcdMappingItem>>();
            }

            if (!string.IsNullOrEmpty(TestTaskName) && !string.IsNullOrEmpty(ConfigTabelName))
            {
                SaveSignalsToMemory();
            }

            lock (_allIcdMappingItemsLock)
            {
                foreach (var kvp in _allIcdMappingItems)
                {
                    var signalsList = kvp.Value?.Select(s => CloneCommunicatingSignalConfigItem(s)).ToList() ?? new List<IcdMappingItem>();
                    args.SignalTabelItems[kvp.Key] = signalsList;
                }
            }
        }

        private void Signals_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
            {
                foreach (IcdMappingItem signal in e.NewItems)
                {
                    SubscribeToSignalPropertyChanged(signal);
                }
            }

            if (e.OldItems != null)
            {
                foreach (IcdMappingItem signal in e.OldItems)
                {
                    UnsubscribeFromSignalPropertyChanged(signal);
                }
            }

            SaveSignalsToMemory();
            UpdatePagination();
        }

        private void SubscribeToSignalPropertyChanged(IcdMappingItem signal)
        {
            if (signal != null)
            {
                signal.PropertyChanged += Signal_PropertyChanged;
            }
        }

        private void UnsubscribeFromSignalPropertyChanged(IcdMappingItem signal)
        {
            if (signal != null)
            {
                signal.PropertyChanged -= Signal_PropertyChanged;
            }
        }

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

        private void Signal_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            SaveSignalsToMemory();

            _eventAggregator.GetEvent<Events.ProjectModifiedEvent>().Publish(new Events.ProjectModifiedEventArgs
            {
                ModificationType = "IcdMappingTabel",
                Description = $"修改ICD映射属性: {e.PropertyName}"
            });
        }

        #endregion

        #region 分页

        private void UpdatePagination()
        {
            if (Signals == null)
            {
                PagedSignals = new ObservableCollection<IcdMappingItem>();
                PaginationInfo = "共 0 条记录";
                PageNumbers = new ObservableCollection<PaginationButtonInfo>();
                return;
            }

            int totalPages = TotalPages;
            if (totalPages < 1) totalPages = 1;

            if (CurrentPage < 1)
            {
                CurrentPage = 1;
            }
            if (CurrentPage > totalPages)
            {
                CurrentPage = totalPages;
            }

            int skip = (CurrentPage - 1) * PageSize;
            var pagedData = Signals.Skip(skip).Take(PageSize).ToList();

            PagedSignals.Clear();
            foreach (var item in pagedData)
            {
                PagedSignals.Add(item);
            }

            PaginationInfo = $"共 {Signals.Count} 条记录";

            PageNumbers.Clear();
            int maxButtons = 5;
            int startPage = Math.Max(1, CurrentPage - maxButtons / 2);
            int endPage = Math.Min(totalPages, startPage + maxButtons - 1);
            startPage = Math.Max(1, endPage - maxButtons + 1);

            for (int i = startPage; i <= endPage; i++)
            {
                PageNumbers.Add(new PaginationButtonInfo
                {
                    PageNumber = i,
                    IsCurrentPage = i == CurrentPage,
                    Command = new DelegateCommand(() => CurrentPage = i)
                });
            }

            ((DelegateCommand)PreviousPageCommand).RaiseCanExecuteChanged();
            ((DelegateCommand)NextPageCommand).RaiseCanExecuteChanged();
            RaisePropertyChanged(nameof(PagedSignals));
        }

        private bool CanGoToPreviousPage() => CurrentPage > 1;
        private void OnPreviousPage()
        {
            if (CanGoToPreviousPage()) CurrentPage--;
        }

        private bool CanGoToNextPage() => CurrentPage < TotalPages;
        private void OnNextPage()
        {
            if (CanGoToNextPage()) CurrentPage++;
        }

        #endregion

        #region 命令处理

        private void OnAddSignal()
        {
            try
            {
                // 获取可用的ICD表
                var allIcdTabels = IcdConfigTabelViewModel.GetAllIcdTabelItems();
                var availableIcdTabels = new System.Collections.ObjectModel.ObservableCollection<string>();

                foreach (var kvp in allIcdTabels)
                {
                    var parts = kvp.Key.Split('/');
                    if (parts.Length == 2 && parts[0] == TestTaskName)
                    {
                        availableIcdTabels.Add(parts[1]);
                    }
                }

                // 获取可用的帧（暂时使用第一个ICD表的帧）
                var availableFrames = new System.Collections.ObjectModel.ObservableCollection<IcdFrameItem>();
                if (availableIcdTabels.Count > 0)
                {
                    var firstTabelKey = $"{TestTaskName}/{availableIcdTabels[0]}";
                    if (allIcdTabels.TryGetValue(firstTabelKey, out var frames))
                    {
                        foreach (var frame in frames.Where(f => f != null && !f.IsEmpty))
                        {
                            availableFrames.Add(frame);
                        }
                    }
                }

                var newSignal = _dialogService.ShowAddIcdMappingDialog(availableIcdTabels, availableFrames);

                if (newSignal != null)
                {
                    int skip = (CurrentPage - 1) * PageSize;
                    int currentPageItemCount = Signals.Skip(skip).Take(PageSize).Count();

                    Signals.Add(newSignal);

                    if (currentPageItemCount >= PageSize)
                    {
                        int lastPage = TotalPages;
                        if (lastPage > 0)
                        {
                            CurrentPage = lastPage;
                        }
                    }

                    _eventAggregator.GetEvent<Events.ProjectModifiedEvent>().Publish(new Events.ProjectModifiedEventArgs
                    {
                        ModificationType = "IcdMappingTabel",
                        Description = $"添加ICD映射: {newSignal.SignalId}"
                    });
                }
            }
            catch (Exception)
            {
            }
        }

        private void OnDeleteSignal(IcdMappingItem signal)
        {
            if (signal != null)
            {
                string signalIdentifier = signal.SignalId;
                var result = ReMessageBox.Show(
                    $"确定要删除信号 '{signalIdentifier}' 吗？",
                    "确认删除",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    Signals.Remove(signal);
                    UpdatePagination();

                    _eventAggregator.GetEvent<Events.ProjectModifiedEvent>().Publish(new Events.ProjectModifiedEventArgs
                    {
                        ModificationType = "IcdMappingTabel",
                        Description = $"删除ICD映射: {signalIdentifier}"
                    });
                }
            }
        }

        private void OnEditSignal(IcdMappingItem signal)
        {
            if (signal != null)
            {
                try
                {
                    // 获取可用的ICD表
                    var allIcdTabels = IcdConfigTabelViewModel.GetAllIcdTabelItems();
                    var availableIcdTabels = new System.Collections.ObjectModel.ObservableCollection<string>();

                    foreach (var kvp in allIcdTabels)
                    {
                        var parts = kvp.Key.Split('/');
                        if (parts.Length == 2 && parts[0] == TestTaskName)
                        {
                            availableIcdTabels.Add(parts[1]);
                        }
                    }

                    // 获取可用的帧
                    var availableFrames = new System.Collections.ObjectModel.ObservableCollection<IcdFrameItem>();
                    if (!string.IsNullOrEmpty(signal.IcdTabelId))
                    {
                        var tabelKey = $"{TestTaskName}/{signal.IcdTabelId}";
                        if (allIcdTabels.TryGetValue(tabelKey, out var frames))
                        {
                            foreach (var frame in frames.Where(f => f != null && !f.IsEmpty))
                            {
                                availableFrames.Add(frame);
                            }
                        }
                    }

                    // 创建一个副本用于编辑
                    var editedSignal = _dialogService.ShowAddIcdMappingDialog(availableIcdTabels, availableFrames);

                    if (editedSignal != null)
                    {
                        // 更新现有信号的属性
                        signal.SignalId = editedSignal.SignalId;
                        signal.Description = editedSignal.Description;
                        signal.IcdTabelId = editedSignal.IcdTabelId;
                        signal.FrameId = editedSignal.FrameId;
                        signal.DataType = editedSignal.DataType;
                        signal.BitLength = editedSignal.BitLength;
                        signal.CalibrationFormula = editedSignal.CalibrationFormula;
                        signal.ConcatCount = editedSignal.ConcatCount;
                        signal.DoubleWordBits = editedSignal.DoubleWordBits;
                        signal.PositionInWord = editedSignal.PositionInWord;
                        signal.MessageId = editedSignal.MessageId;
                        signal.Channel = editedSignal.Channel;
                        signal.Dlc = editedSignal.Dlc;
                        signal.Direction = editedSignal.Direction;
                        signal.Cycle = editedSignal.Cycle;

                        SaveSignalsToMemory();
                        _eventAggregator.GetEvent<Events.ProjectModifiedEvent>().Publish(new Events.ProjectModifiedEventArgs
                        {
                            ModificationType = "IcdMappingTabel",
                            Description = $"编辑ICD映射: {signal.SignalId}"
                        });
                    }
                }
                catch (Exception)
                {
                }
            }
        }

        private void OnFloatWindow()
        {
            ReMessageBox.Show("浮动功能需要在View中实现，请绑定到View的事件");
        }

        private void OnMinimizeInRegion()
        {
            ReMessageBox.Show("最小化功能待实现");
        }

        private void OnCloseInRegion()
        {
            var result = ReMessageBox.Show("确定要关闭当前配置表吗？", "确认", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                string pageKey = $"CommunicatingSignalConfigTabel_{TestTaskName}-{ConfigTabelName}";
                _eventAggregator.GetEvent<Events.ReleaseCurrentPageEvent>().Publish(pageKey);
            }
        }

        #endregion

        #region 辅助

        private string GetParentDisplayName(string parentType)
        {
            return parentType switch
            {
                "channel_config" => "通道配置",
                "signal_config" => "信号配置",
                "icd_config" => "ICD配置",
                "icd_mapping" => "ICD映射",
                "test_sequence" => "测试序列",
                "report" => "报表模板",
                _ => parentType
            };
        }


        #endregion
    }
}

