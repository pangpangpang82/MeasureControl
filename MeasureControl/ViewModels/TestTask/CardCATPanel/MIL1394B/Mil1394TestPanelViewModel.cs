using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MeasureControl.Constants;
using MeasureControl.Drivers;
using MeasureControl.Events;
using MeasureControl.Helpers;
using MeasureControl.Models;
using MeasureControl.Models.Devices;
using MeasureControl.Services;
using MeasureControl.Views.Dialogs;
using MeasureControl.Views.TestTask;
using MeasureControl.Views.TestTask.CardCATPanel.Mil1394B;
using Prism.Commands;
using Prism.Events;
using Prism.Mvvm;
using Prism.Regions;

namespace MeasureControl.ViewModels.TestTask.CardCATPanel.MIL1394B
{
    /// <summary>
    /// 1394B板卡测试界面ViewModel
    /// </summary>
    public class Mil1394TestPanelViewModel : BindableBase, INavigationAware, IDisposable
    {
        private readonly Mil1394BDevice _device;
        private readonly string _chassisName;
        private readonly IPxiChassisService _pxiChassisService;
        private readonly IEventAggregator _eventAggregator;
        private readonly ProjectService _projectService;
        private UserControl _wpfContent;
        private bool _isLoading;
        private string _statusMessage;
        private string _title;
        private IRegionNavigationJournal _journal;
        private Mil1394CardPanel _currentCardPanel;
        private HZ1394DriverInterface[] _currentDriverInterfaces;
        private IntPtr[] _currentPnode;
        private List<Mil1394NodeSendRcvPanelViewModel> _currentNodeSendRcvViewModels;
        // loading flag removed (unused)

        private readonly object _openNodeTasksLock = new object();
        private readonly Dictionary<uint, Task<IntPtr>> _openNodeTasks = new Dictionary<uint, Task<IntPtr>>();

        public string Title
        {
            get => _title;
            set => SetProperty(ref _title, value);
        }

        public UserControl WpfContent
        {
            get => _wpfContent;
            set => SetProperty(ref _wpfContent, value);
        }

        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public ICommand RefreshDeviceCommand { get; }
        public ICommand CloseCommand { get; }
        public ICommand SwitchToNodeConfigCommand { get; }
        public ICommand SwitchToDataTransferCommand { get; }
        public ICommand ToggleDeviceCommand { get; }
        public ICommand OpenDeviceCommand { get; }

        private bool _isDeviceConnected;
        private string _connectionStatus = "离线";
        private ObservableCollection<string> _availableTestTasks;
        private string _selectedTestTask;
        private string _cardName = "1394B";

        public bool IsDeviceConnected
        {
            get => _isDeviceConnected;
            set => SetProperty(ref _isDeviceConnected, value);
        }

        public string ConnectionStatus
        {
            get => _connectionStatus;
            set => SetProperty(ref _connectionStatus, value);
        }

        public ObservableCollection<string> AvailableTestTasks
        {
            get => _availableTestTasks;
            set => SetProperty(ref _availableTestTasks, value);
        }

        public string SelectedTestTask
        {
            get => _selectedTestTask;
            set => ChangeSelectedTestTask(value);
        }

        public string CardName
        {
            get => _cardName;
            set => SetProperty(ref _cardName, value);
        }

        public Mil1394BDevice Device => _device;

        public Mil1394TestPanelViewModel(Mil1394BDevice device, string chassisName,
            IPxiChassisService pxiChassisService = null, IEventAggregator eventAggregator = null, ProjectService projectService = null)
        {
            _device = device ?? throw new ArgumentNullException(nameof(device));
            _chassisName = chassisName ?? throw new ArgumentNullException(nameof(chassisName));
            _pxiChassisService = pxiChassisService;
            _eventAggregator = eventAggregator;
            _projectService = projectService;

            Title = $"1394B板卡测试 - {device.Name} ({chassisName})";
            IsLoading = true;
            StatusMessage = "正在扫描设备...";

            AvailableTestTasks = new ObservableCollection<string>();
            SelectedTestTask = string.Empty;

            // 加载板卡名称
            if (!string.IsNullOrEmpty(device.CardName))
            {
                CardName = device.CardName;
            }

            RefreshDeviceCommand = new DelegateCommand(RefreshDevice);
            CloseCommand = new DelegateCommand(Close);
            SwitchToNodeConfigCommand = new DelegateCommand(() => SwitchTab(0));
            SwitchToDataTransferCommand = new DelegateCommand(() => SwitchTab(1));
            OpenDeviceCommand = new DelegateCommand(async () => await OnOpenDeviceAsync(), () => !IsDeviceConnected)
                .ObservesProperty(() => IsDeviceConnected);
            ToggleDeviceCommand = new DelegateCommand(async () =>
            {
                if (IsDeviceConnected)
                {
                    await OnCloseDeviceAsync();
                }
                else
                {
                    await OnOpenDeviceAsync();
                }
            });

            // 加载测试任务选项
            LoadTestTaskOptions();

            // 订阅测试任务创建事件，用于更新可用测试任务列表
            _eventAggregator?.GetEvent<TestTaskCreatedEvent>()?.Subscribe(OnTestTaskCreated);
        }

        /// <summary>
        /// 处理测试任务创建事件，更新可用测试任务列表
        /// </summary>
        private void OnTestTaskCreated(ProjectItem testTask)
        {
            LoadTestTaskOptions();
        }

        /// <summary>
        /// 改变选中的测试任务
        /// </summary>
        private void ChangeSelectedTestTask(string taskName)
        {
            if (_selectedTestTask == taskName)
                return;

            _selectedTestTask = taskName;
            RaisePropertyChanged(nameof(SelectedTestTask));

            // 保存最后选中的测试任务
            if (_device?.CardConfigData is Models.Mil1394BCardConfig cardConfig)
            {
                cardConfig.LastSelectedTestTask = taskName;
            }

            // 通知所有节点配置面板加载新任务的配置
            NotifyNodeConfigPanelsLoadConfig(taskName);
        }

        /// <summary>
        /// 通知所有节点配置面板加载配置
        /// </summary>
        private void NotifyNodeConfigPanelsLoadConfig(string taskName)
        {
            if (_currentCardPanel == null)
                return;

            // 通过事件或直接调用节点配置面板的方法来加载配置
            // 这里可以通过事件聚合器发布事件，或者直接访问节点配置面板
            System.Diagnostics.Debug.WriteLine($"[Mil1394TestPanel] 测试任务切换为: {taskName}");
        }

        /// <summary>
        /// 加载测试任务选项
        /// </summary>
        private void LoadTestTaskOptions()
        {
            try
            {
                AvailableTestTasks.Clear();
                AvailableTestTasks.Add("默认测试任务");

                string initialTask = "默认测试任务";

                _selectedTestTask = initialTask;
                RaisePropertyChanged(nameof(SelectedTestTask));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadTestTaskOptions 异常: {ex}");
                StatusMessage = $"加载测试任务失败: {ex.Message}";
            }
        }

        /// <summary>
        /// 从项目中获取测试任务名称列表
        /// </summary>
        private List<string> GetTestTaskNamesFromProject()
        {
            var result = new List<string>();

            var globalTasks = _projectService?.GetGlobalTestTaskNames();
            if (globalTasks != null && globalTasks.Count > 0)
            {
                return globalTasks;
            }

            if (_projectService?.CurrentProjectRoot?.Children == null || string.IsNullOrEmpty(_chassisName))
            {
                return result;
            }

            var chassisNode = _projectService.CurrentProjectRoot.Children
                .FirstOrDefault(c => c.Name == _chassisName && c.Type == AppConstants.NodeTypePxiChassis);
            if (chassisNode?.Children == null)
            {
                return result;
            }

            var taskConfigNode = chassisNode.Children.FirstOrDefault(c => c.Type == AppConstants.NodeTypeTaskConfig);
            if (taskConfigNode?.Children == null)
            {
                return result;
            }

            foreach (var testTask in taskConfigNode.Children.Where(c => c.Type == AppConstants.NodeTypeTestTask))
            {
                result.Add(testTask.Name);
            }

            return result;
        }

        #region INavigationAware Implementation

        public void OnNavigatedTo(NavigationContext navigationContext)
        {
            // 缓存导航日志用于关闭时回退
            _journal = navigationContext?.NavigationService?.Journal;
        }

        public bool IsNavigationTarget(NavigationContext navigationContext)
        {
            return true;
        }

        public void OnNavigatedFrom(NavigationContext navigationContext)
        {
            // 清理资源
            WpfContent = null;
            _currentCardPanel = null;
        }

        #endregion

        /// <summary>
        /// 切换Tab页
        /// </summary>
        private void SwitchTab(int tabIndex)
        {
            if (_currentCardPanel != null)
            {
                _currentCardPanel.SwitchToTab(tabIndex);
            }
        }

        /// <summary>
        /// 初始化界面
        /// 右键打开配置时只启动板卡模拟界面，不实际连接板卡
        /// </summary>
        public void Initialize()
        {
            try
            {
                // 右键打开配置时，总是使用模拟模式显示界面，不实际连接板卡
                IsLoading = false;
                StatusMessage = "板卡模拟界面已启动，点击打开板卡按钮连接板卡";
                CreateSimulatedWpfForm();
            }
            catch (Exception ex)
            {
                IsLoading = false;
                StatusMessage = $"初始化失败: {ex.Message}，使用模拟模式显示界面";
                CreateSimulatedWpfForm(); // 异常时也使用模拟模式
            }
        }

        /// <summary>
        /// 扫描设备
        /// </summary>
        private PCI_DEV_FOUND ScanDevices()
        {
            var deviceInfo = new PCI_DEV_FOUND
            {
                DevNum = 0,
                DevType = new uint[32],
                DevNodeNum = new uint[32],
                DevSN = new uint[32]
            };

            try
            {
                int result = HZ1394Interface.Mil1394_Found(ref deviceInfo);

                // 检查返回值：0表示成功，非0表示失败
                if (result != 0)
                {
                    System.Diagnostics.Debug.WriteLine($"Mil1394_Found returned error code: {result}");
                    // 返回空设备信息
                    deviceInfo.DevNum = 0;
                    return deviceInfo;
                }

                // 检查是否检测到设备
                if (deviceInfo.DevNum == 0)
                {
                    System.Diagnostics.Debug.WriteLine("Mil1394_Found returned success but DevNum is 0");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Detected {deviceInfo.DevNum} devices");
                }

                return deviceInfo;
            }
            catch (DllNotFoundException ex)
            {
                System.Diagnostics.Debug.WriteLine($"DLL not found: {ex.Message}");
                return deviceInfo;
            }
            catch (BadImageFormatException ex)
            {
                System.Diagnostics.Debug.WriteLine($"Bad image format: {ex.Message}");
                return deviceInfo;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Exception in ScanDevices: {ex.Message}\n{ex.StackTrace}");
                return deviceInfo;
            }
        }

        private async Task OpenNodesAsync(uint cardIndex)
        {
            try
            {
                if (!IsDeviceConnected)
                    return;

                var cardPanel = _currentCardPanel;
                var driverInterfaces = _currentDriverInterfaces;
                var pnode = _currentPnode;

                if (cardPanel == null || driverInterfaces == null || pnode == null)
                    return;

                uint nodeCount = (uint)pnode.Length;
                for (uint i = 0; i < nodeCount; i++)
                {
                    if (!IsDeviceConnected)
                        return;

                    if (pnode[i] != IntPtr.Zero)
                        continue;

                    var di = driverInterfaces[i];
                    if (di == null)
                        continue;

                    IntPtr handle;
                    try
                    {
                        handle = await Task.Run(() => di.HZ1394_OPEN("BM", IntPtr.Zero, cardIndex, i));
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Mil1394TestPanel] 连接节点失败 (Card {cardIndex}, Node {i}): {ex.Message}");
                        continue;
                    }

                    if (handle == IntPtr.Zero)
                        continue;

                    await Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (!IsDeviceConnected)
                            return;

                        if (_currentCardPanel != cardPanel)
                            return;

                        if (_currentPnode == null || i >= _currentPnode.Length)
                            return;

                        _currentPnode[i] = handle;

                        if (_currentDriverInterfaces != null && i < _currentDriverInterfaces.Length && _currentDriverInterfaces[i] != null)
                        {
                            _currentDriverInterfaces[i].TmpnodeType = "BM";
                            _currentDriverInterfaces[i].Tmpnote = handle;
                        }

                        _currentCardPanel.SetNodeHandle(i, handle);
                    }));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Mil1394TestPanel] OpenNodesAsync异常: {ex.Message}");
            }
        }

        public Task<IntPtr> EnsureNodeOpenedAsync(uint nodeIndex)
        {
            if (!IsDeviceConnected)
                return Task.FromResult(IntPtr.Zero);

            var cardPanel = _currentCardPanel;
            var driverInterfaces = _currentDriverInterfaces;
            var pnode = _currentPnode;

            if (cardPanel == null || driverInterfaces == null || pnode == null)
                return Task.FromResult(IntPtr.Zero);

            if (nodeIndex >= pnode.Length || nodeIndex >= driverInterfaces.Length)
                return Task.FromResult(IntPtr.Zero);

            if (pnode[nodeIndex] != IntPtr.Zero)
                return Task.FromResult(pnode[nodeIndex]);

            lock (_openNodeTasksLock)
            {
                if (pnode[nodeIndex] != IntPtr.Zero)
                    return Task.FromResult(pnode[nodeIndex]);

                if (_openNodeTasks.TryGetValue(nodeIndex, out var existingTask))
                    return existingTask;

                var di = driverInterfaces[nodeIndex];
                if (di == null)
                    return Task.FromResult(IntPtr.Zero);

                var openTask = Task.Run(async () =>
                {
                    try
                    {
                        if (!IsDeviceConnected)
                            return IntPtr.Zero;

                        string nodeTypeToOpen = string.IsNullOrEmpty(di.TmpnodeType) ? "BM" : di.TmpnodeType;
                        IntPtr handle;
                        try
                        {
                            handle = di.HZ1394_OPEN(nodeTypeToOpen, IntPtr.Zero, di.CardNumber, nodeIndex);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[Mil1394TestPanel] EnsureNodeOpenedAsync 打开节点失败 (Card {di.CardNumber}, Node {nodeIndex}, Type {nodeTypeToOpen}): {ex.Message}");
                            return IntPtr.Zero;
                        }

                        if (handle == IntPtr.Zero)
                            return IntPtr.Zero;

                        if (Application.Current?.Dispatcher == null)
                            return handle;

                        await Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            if (!IsDeviceConnected)
                                return;

                            if (_currentCardPanel != cardPanel)
                                return;

                            if (_currentPnode == null || nodeIndex >= _currentPnode.Length)
                                return;

                            _currentPnode[nodeIndex] = handle;

                            if (_currentDriverInterfaces != null && nodeIndex < _currentDriverInterfaces.Length && _currentDriverInterfaces[nodeIndex] != null)
                            {
                                _currentDriverInterfaces[nodeIndex].Tmpnote = handle;
                            }

                            _currentCardPanel.SetNodeHandle(nodeIndex, handle);
                        }));

                        return handle;
                    }
                    finally
                    {
                        lock (_openNodeTasksLock)
                        {
                            _openNodeTasks.Remove(nodeIndex);
                        }
                    }
                });

                _openNodeTasks[nodeIndex] = openTask;
                return openTask;
            }
        }

        /// <summary>
        /// 创建WPF版本的1394B测试界面
        /// </summary>
        /// <param name="deviceInfo">设备信息</param>
        /// <param name="openNodes">是否实际打开节点（true=实际连接，false=模拟模式）</param>
        private void CreateHZ1394TestForm(PCI_DEV_FOUND deviceInfo, bool openNodes = false)
        {
            try
            {
                // 计算节点数量
                uint[] nodeCounts = new uint[32];
                uint totalNodes = 0;
                int deviceIndex = 0;

                for (int i = 0; i < deviceInfo.DevNodeNum.Length && deviceIndex < 32; i++)
                {
                    if (deviceInfo.DevNodeNum[i] > 0)
                    {
                        nodeCounts[deviceIndex] = deviceInfo.DevNodeNum[i];
                        totalNodes += nodeCounts[deviceIndex];
                        deviceIndex++;
                    }
                }

                // 如果没有检测到设备，使用模拟数据
                if (deviceIndex == 0)
                {
                    System.Diagnostics.Debug.WriteLine("未检测到设备，使用模拟数据");
                    nodeCounts[0] = 4; // 模拟4个节点
                    deviceIndex = 1;
                }

                // 直接使用第一个板卡的面板（单个板卡场景）
                if (deviceIndex > 0 && nodeCounts[0] > 0)
                {
                    try
                    {
                        // 根据openNodes参数决定是否实际打开节点
                        var cardPanel = CreateCardPanel(0, nodeCounts, openNodes: openNodes);
                        if (cardPanel != null)
                        {
                            _currentCardPanel = cardPanel;
                            WpfContent = cardPanel;
                        }
                        else
                        {
                            StatusMessage = "创建板卡面板失败";
                            CreateEmptyWpfForm();
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"创建板卡面板时发生异常: {ex.Message}");
                        StatusMessage = $"创建板卡面板失败: {ex.Message}";
                        CreateEmptyWpfForm();
                    }
                }
                else
                {
                    StatusMessage = "未能创建板卡面板";
                    CreateEmptyWpfForm();
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"创建测试界面失败: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"CreateHZ1394TestForm异常详情: {ex}");
                CreateEmptyWpfForm();
            }
        }

        /// <summary>
        /// 创建板卡面板
        /// </summary>
        /// <param name="cardIndex">板卡索引</param>
        /// <param name="nodeCounts">节点数量数组</param>
        /// <param name="openNodes">是否实际打开节点（true=实际连接，false=模拟模式）</param>
        private Mil1394CardPanel CreateCardPanel(uint cardIndex, uint[] nodeCounts, bool openNodes = false)
        {
            try
            {
                // 检查节点数量
                if (cardIndex >= nodeCounts.Length || nodeCounts[cardIndex] == 0)
                {
                    StatusMessage = $"板卡 {cardIndex} 的节点数量无效";
                    return null;
                }

                uint nodeCount = nodeCounts[cardIndex];

                // 初始化节点句柄
                IntPtr[] pnode = new IntPtr[nodeCount];
                HZ1394DriverInterface[] driverInterfaces = new HZ1394DriverInterface[nodeCount];

                // 初始化每个节点的DriverInterface和句柄
                for (uint i = 0; i < nodeCount; i++)
                {
                    try
                    {
                        driverInterfaces[i] = new HZ1394DriverInterface(i);
                        driverInterfaces[i].CardNumber = cardIndex;
                        driverInterfaces[i].NodeNumber = i;
                        driverInterfaces[i].RcvFlag = true;

                        // 根据openNodes参数决定是否实际打开节点
                        if (openNodes)
                        {
                            // 实际打开节点：严格按照例程逻辑，默认以BM模式打开所有节点
                            // 参考例程Form_Card_Num.cs第53行：pnode[i] = driverInterface[i].HZ1394_OPEN("BM", pnode[i], cardNum, i);
                            string nodeTypeToOpen = "BM"; // 默认BM模式（和例程一致）

                            // 注意：不要在UI线程里同步打开节点（可能耗时），句柄由 OpenNodesAsync 后台回填
                            driverInterfaces[i].TmpnodeType = nodeTypeToOpen;
                            driverInterfaces[i].Tmpnote = IntPtr.Zero;
                            pnode[i] = IntPtr.Zero;
                        }
                        else
                        {
                            // 模拟模式：不实际打开节点，使用空句柄
                            pnode[i] = IntPtr.Zero;
                            System.Diagnostics.Debug.WriteLine($"[Mil1394TestPanel] 模拟模式：节点 (Card {cardIndex}, Node {i}) 使用空句柄");
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Mil1394TestPanel] 初始化DriverInterface失败 (Card {cardIndex}, Node {i}): {ex.Message}");
                        // 确保driverInterfaces[i]不为null
                        driverInterfaces[i] = new HZ1394DriverInterface(i);
                        driverInterfaces[i].CardNumber = cardIndex;
                        driverInterfaces[i].NodeNumber = i;
                        driverInterfaces[i].RcvFlag = true;
                        pnode[i] = IntPtr.Zero;
                    }
                }

                // 验证所有driverInterfaces都已初始化
                for (uint i = 0; i < nodeCount; i++)
                {
                    if (driverInterfaces[i] == null)
                    {
                        throw new Exception($"DriverInterface[{i}] 未初始化");
                    }
                }

                // 创建ViewModel
                var cardViewModel = new Mil1394CardPanelViewModel(cardIndex, nodeCount, pnode);

                // 创建板卡面板
                var cardPanel = new Mil1394CardPanel(cardIndex, nodeCounts, cardViewModel);

                // 设置节点句柄
                for (uint i = 0; i < nodeCount; i++)
                {
                    cardPanel.SetNodeHandle(i, pnode[i]);
                }

                // 保存当前板卡的驱动接口和节点句柄引用（用于关闭时清理）
                _currentDriverInterfaces = driverInterfaces;
                _currentPnode = pnode;

                // 初始化各个Tab页
                InitializeCardPanelTabs(cardPanel, cardIndex, nodeCount, pnode, driverInterfaces);

                return cardPanel;
            }
            catch (Exception ex)
            {
                StatusMessage = $"创建板卡面板失败: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"CreateCardPanel异常详情: {ex}");
                return null;
            }
        }

        /// <summary>
        /// 初始化板卡面板的各个Tab页
        /// </summary>
        private void InitializeCardPanelTabs(Mil1394CardPanel cardPanel, uint cardIndex, uint nodeCount, IntPtr[] pnode, HZ1394DriverInterface[] driverInterfaces)
        {
            try
            {
                // 验证参数
                if (cardPanel == null)
                {
                    throw new ArgumentNullException(nameof(cardPanel));
                }
                if (pnode == null)
                {
                    throw new ArgumentNullException(nameof(pnode));
                }
                if (driverInterfaces == null)
                {
                    throw new ArgumentNullException(nameof(driverInterfaces));
                }
                if (driverInterfaces.Length < nodeCount)
                {
                    throw new ArgumentException($"driverInterfaces数组长度({driverInterfaces.Length})小于节点数量({nodeCount})");
                }

                // 创建节点配置面板列表
                var nodeConfigPanels = new List<UserControl>();
                for (uint i = 0; i < nodeCount; i++)
                {
                    if (driverInterfaces[i] == null)
                    {
                        throw new Exception($"DriverInterface[{i}] 为null");
                    }

                    var nodeConfigViewModel = new Mil1394NodeConfigPanelViewModel(
                        cardIndex, i, pnode, driverInterfaces[i], _device, _chassisName, _pxiChassisService, _eventAggregator);
                    var nodeConfigPanel = new Mil1394NodeConfigPanel(cardIndex, i, pnode, nodeConfigViewModel);

                    // 设置父级ViewModel引用，用于访问测试任务和连接状态
                    nodeConfigPanel.SetParentViewModel(this);

                    // 如果节点已连接，设置节点类型和句柄
                    // 参考例程：连接后默认以BM模式打开，用户可以后续配置节点类型
                    if (pnode[i] != IntPtr.Zero)
                    {
                        // 设置默认节点类型和句柄（确保数据收发面板能识别节点）
                        string defaultNodeType = "BM"; // 默认BM模式（和例程一致）
                        driverInterfaces[i].Tmpnote = pnode[i];
                        driverInterfaces[i].TmpnodeType = defaultNodeType;
                        
                        System.Diagnostics.Debug.WriteLine($"[Mil1394TestPanel] 节点{i}已连接，默认类型: BM，连接后可以对节点进行配置");
                        // 注意：连接后不自动应用配置，用户需要通过"保存配置"按钮来应用配置
                    }
                    else
                    {
                        // 节点未连接，设置默认节点类型（用于后续连接时使用）
                        driverInterfaces[i].TmpnodeType = "BM";
                        driverInterfaces[i].Tmpnote = IntPtr.Zero;
                    }

                    nodeConfigPanels.Add(nodeConfigPanel);
                }
                cardPanel.InitializeNodeConfigTabs(nodeConfigPanels);

                // 创建数据收发面板列表
                var nodeSendRcvPanels = new List<UserControl>();
                var nodeDataCountViewModels = new List<Mil1394NodeDataCountPanelViewModel>();
                var nodeSendRcvViewModels = new List<Mil1394NodeSendRcvPanelViewModel>();
                for (uint i = 0; i < nodeCount; i++)
                {
                    if (driverInterfaces[i] == null)
                    {
                        throw new Exception($"DriverInterface[{i}] 为null");
                    }

                    var nodeSendRcvViewModel = new Mil1394NodeSendRcvPanelViewModel(driverInterfaces[i]);
                    var nodeSendRcvPanel = new Mil1394NodeSendRcvPanel(cardIndex, i, pnode, nodeSendRcvViewModel);

                    // 设置父级ViewModel引用，用于按需打开节点
                    nodeSendRcvPanel.SetParentViewModel(this);

                    nodeSendRcvPanels.Add(nodeSendRcvPanel);
                    nodeSendRcvViewModels.Add(nodeSendRcvViewModel);

                    var nodeDataCountViewModel = new Mil1394NodeDataCountPanelViewModel(driverInterfaces[i]);
                    nodeDataCountViewModels.Add(nodeDataCountViewModel);
                }

                // 保存数据收发ViewModel引用（用于关闭时停止接收线程）
                _currentNodeSendRcvViewModels = nodeSendRcvViewModels;

                cardPanel.InitializeDataTransferTabs(nodeSendRcvPanels, nodeDataCountViewModels);
            }
            catch (Exception ex)
            {
                StatusMessage = $"初始化板卡Tab页失败: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"InitializeCardPanelTabs异常详情: {ex}");
                throw;
            }
        }

        /// <summary>
        /// 使用默认配置初始化节点（当没有保存的配置时）
        /// </summary>
        private void InitializeNodeWithDefaultConfig(Mil1394NodeConfigPanelViewModel viewModel,
            HZ1394DriverInterface driverInterface, IntPtr nodeHandle, string nodeType)
        {
            try
            {
                if (nodeHandle == IntPtr.Zero)
                    return;

                // 使用默认配置初始化节点
                var defaultStofPayload = new uint[9]; // 默认全0
                var defaultAsyncReceiveConfig = new ObservableCollection<Mil1394NodeConfigPanel.AsyncReceiveConfigItem>();
                var defaultAsyncSendConfig = new ObservableCollection<Mil1394NodeConfigPanel.AsyncSendConfigItem>();

                // 应用默认配置
                viewModel.ApplyConfiguration(
                    nodeType,
                    "400M", // 默认400M速率
                    true, // 默认启用BM使能
                    1, // 默认按次数发送
                    "15", // 默认周期15ms
                    "100", // 默认发送100次
                    "0", // 默认接收通道0
                    defaultStofPayload,
                    "0", // 默认VPC为0
                    defaultAsyncReceiveConfig,
                    defaultAsyncSendConfig
                );

                System.Diagnostics.Debug.WriteLine($"[Mil1394TestPanel] 节点已使用默认配置初始化");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Mil1394TestPanel] 使用默认配置初始化节点失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 应用已保存的配置到节点硬件
        /// </summary>
        private void ApplySavedConfigToNode(Mil1394NodeConfigPanelViewModel viewModel, Models.Mil1394BNodeConfig savedConfig,
            HZ1394DriverInterface driverInterface, IntPtr nodeHandle)
        {
            try
            {
                if (nodeHandle == IntPtr.Zero || savedConfig == null)
                    return;

                // 构建配置参数
                uint[] stofPayload = savedConfig.StofPayload ?? new uint[9];
                var asyncReceiveConfig = new ObservableCollection<Mil1394NodeConfigPanel.AsyncReceiveConfigItem>();
                if (savedConfig.AsyncReceiveConfig != null)
                {
                    foreach (var item in savedConfig.AsyncReceiveConfig)
                    {
                        asyncReceiveConfig.Add(new Mil1394NodeConfigPanel.AsyncReceiveConfigItem
                        {
                            IsSelected = item.IsSelected,
                            MsgID = item.MsgID,
                            DataLength = item.DataLength
                        });
                    }
                }

                var asyncSendConfig = new ObservableCollection<Mil1394NodeConfigPanel.AsyncSendConfigItem>();
                if (savedConfig.AsyncSendConfig != null)
                {
                    foreach (var item in savedConfig.AsyncSendConfig)
                    {
                        asyncSendConfig.Add(new Mil1394NodeConfigPanel.AsyncSendConfigItem
                        {
                            MessageID = item.MessageID,
                            Channel = item.Channel,
                            Heartbeat = item.Heartbeat,
                            Health = item.Health,
                            HeartbeatStep = item.HeartbeatStep,
                            PayloadLength = item.PayloadLength,
                            SendOffset = item.SendOffset,
                            VPC = item.VPC,
                            VPCAsync = item.VPCAsync,
                            Security = item.Security,
                            Priority = item.Priority,
                            PayloadData = item.PayloadData != null ? (uint[])item.PayloadData.Clone() : new uint[500],
                            TransmitOffset = item.TransmitOffset,
                            ReceiveOffset = item.ReceiveOffset,
                            PHMOffset = item.PHMOffset
                        });
                    }
                }

                // 应用配置
                viewModel.ApplyConfiguration(
                    savedConfig.NodeType ?? "BM",
                    savedConfig.NodeRate ?? "400M",
                    savedConfig.BmEnabled, // 使用保存的BM使能配置
                    savedConfig.StofSendStyleIndex,
                    savedConfig.StofPeriod ?? "15",
                    savedConfig.StofSendTimes ?? "100",
                    savedConfig.RecvAsyncChannel ?? "0",
                    stofPayload,
                    savedConfig.StofVpc ?? "0",
                    asyncReceiveConfig,
                    asyncSendConfig
                );

                System.Diagnostics.Debug.WriteLine($"[Mil1394TestPanel] 节点配置已应用到硬件");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Mil1394TestPanel] 应用节点配置到硬件失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 创建空的WPF表单
        /// </summary>
        private void CreateEmptyWpfForm()
        {
            var emptyPanel = new UserControl
            {
                Content = new TextBlock
                {
                    Text = "无法创建1394B测试界面",
                    FontSize = 16,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            WpfContent = emptyPanel;
        }

        /// <summary>
        /// 创建模拟WPF测试界面（用于无硬件环境下的界面测试）
        /// </summary>
        private void CreateSimulatedWpfForm()
        {
            try
            {
                // 创建模拟的设备信息
                var simulatedDeviceInfo = new PCI_DEV_FOUND
                {
                    DevNum = 1, // 模拟1个设备
                    DevType = new uint[32],
                    DevNodeNum = new uint[32],
                    DevSN = new uint[32]
                };

                // 模拟4个节点（可以根据需要调整数量）
                simulatedDeviceInfo.DevNodeNum[0] = 4;
                simulatedDeviceInfo.DevType[0] = 1; // 假设设备类型为1

                // 使用模拟数据创建WPF测试界面
                CreateHZ1394TestForm(simulatedDeviceInfo);
            }
            catch (Exception ex)
            {
                StatusMessage = $"模拟模式加载失败: {ex.Message}";
                CreateEmptyWpfForm();
            }
        }

        /// <summary>
        /// 刷新设备
        /// </summary>
        private async void RefreshDevice()
        {
            IsLoading = true;
            StatusMessage = "正在刷新设备...";

            try
            {
                var deviceInfo = await Task.Run(() => ScanDevices());

                // 根据连接状态决定是否实际打开节点
                bool openNodes = false;

                if (deviceInfo.DevNum > 0)
                {
                    CreateHZ1394TestForm(deviceInfo, openNodes);
                    StatusMessage = $"已检测到 {deviceInfo.DevNum} 个设备";

                    if (IsDeviceConnected)
                    {
                    }
                }
                else
                {
                    // 未检测到设备时，使用模拟数据创建界面
                    StatusMessage = "未检测到1394B设备，使用模拟模式显示界面";
                    CreateSimulatedWpfForm();
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"刷新失败: {ex.Message}，使用模拟模式显示界面";
                CreateSimulatedWpfForm(); // 异常时也使用模拟模式
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// 关闭界面，返回上一级
        /// </summary>
        private void Close()
        {
            // 清理资源
            WpfContent = null;
            _currentCardPanel = null;

            // 使用导航日志返回上一级
            if (_journal != null && _journal.CanGoBack)
            {
                _journal.GoBack();
            }
        }

        /// <summary>
        /// 打开板卡 - 与1394B板卡建立连接
        /// 参考1394B板卡例程，使用HZ1394DriverInterface打开节点
        /// </summary>
        private async Task OnOpenDeviceAsync()
        {
            if (_device == null)
                return;

            try
            {
                ConnectionStatus = "检测中";

                // 先扫描设备，检查板卡是否存在
                var deviceInfo = await Task.Run(() => ScanDevices());
                if (deviceInfo.DevNum == 0)
                {
                    IsDeviceConnected = false;
                    ConnectionStatus = "离线";
                    StatusMessage = "未检测到1394B设备";
                    ReMessageBox.Show(
                        $"未检测到1394B设备，请检查板卡是否已插入并上电",
                        "设备未找到",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Warning);
                    return;
                }

                // 创建驱动实例（用于状态管理）
                var driver = DriverFactory.CreateDriver(_device);

                if (driver == null)
                {
                    IsDeviceConnected = false;
                    ConnectionStatus = "离线";
                    ReMessageBox.Show(
                        $"板卡驱动创建失败，请检查板卡配置",
                        "驱动创建失败",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Error);
                    return;
                }

                // 连接设备（检测板卡）
                bool connected = await Task.Run(async () => await driver.ConnectAsync());

                if (connected)
                {
                    IsDeviceConnected = true;
                    ConnectionStatus = "在线";
                    System.Diagnostics.Debug.WriteLine($"[Mil1394TestPanel] 板卡连接成功: {_device?.Name}");

                    // 连接成功后，重新创建界面（实际打开节点）
                    try
                    {
                        // 重新扫描设备信息
                        deviceInfo = await Task.Run(() => ScanDevices());
                        if (deviceInfo.DevNum > 0)
                        {
                            // 先创建界面（不在UI线程里同步打开节点），节点句柄由后台线程回填
                            CreateHZ1394TestForm(deviceInfo, openNodes: false);
                            StatusMessage = $"板卡已连接，已检测到 {deviceInfo.DevNum} 个设备";
                        }
                        else
                        {
                            // 即使扫描不到设备，也尝试创建模拟界面
                            CreateSimulatedWpfForm();
                            StatusMessage = "板卡已连接，使用模拟模式显示界面";
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Mil1394TestPanel] 重新创建界面失败: {ex.Message}");
                        System.Diagnostics.Debug.WriteLine($"[Mil1394TestPanel] 异常堆栈: {ex.StackTrace}");
                        // 连接成功但界面创建失败不影响连接状态
                        StatusMessage = $"板卡已连接，但界面创建失败: {ex.Message}";
                    }
                }
                else
                {
                    IsDeviceConnected = false;
                    ConnectionStatus = "离线";
                    ReMessageBox.Show(
                        $"板卡连接失败，请检查板卡位置及驱动",
                        "连接失败",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Error);
                }
            }
            catch (System.IO.FileNotFoundException ex)
            {
                IsDeviceConnected = false;
                ConnectionStatus = "离线";

                string errorMsg = $"DLL文件未找到\n\n{ex.Message}\n\n" +
                                 $"解决方案:\n" +
                                 $"1. 检查 Libs 文件夹中是否有 DLL 文件\n" +
                                 $"2. 重新编译项目\n" +
                                 $"3. 手动将 DLL 复制到输出目录";

                ReMessageBox.Show(
                    errorMsg,
                    "DLL文件缺失",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);

                System.Diagnostics.Debug.WriteLine($"[Mil1394TestPanel] DLL文件未找到: {ex.Message}");
            }
            catch (System.DllNotFoundException ex)
            {
                IsDeviceConnected = false;
                ConnectionStatus = "离线";

                string errorMsg = $"无法加载DLL\n\n{ex.Message}\n\n" +
                                 $"可能原因:\n" +
                                 $"1. DLL文件缺失或路径不正确\n" +
                                 $"2. DLL依赖的库缺失（如 Visual C++ 运行库）\n" +
                                 $"3. DLL版本与系统不匹配";

                ReMessageBox.Show(
                    errorMsg,
                    "DLL加载失败",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);

                System.Diagnostics.Debug.WriteLine($"[Mil1394TestPanel] DLL加载失败: {ex.Message}");
            }
            catch (System.BadImageFormatException ex)
            {
                IsDeviceConnected = false;
                ConnectionStatus = "离线";

                string errorMsg = $"DLL格式错误\n\n{ex.Message}\n\n" +
                                 $"可能原因:\n" +
                                 $"1. DLL版本与系统位数不匹配（32位/64位）\n" +
                                 $"2. DLL文件损坏";

                ReMessageBox.Show(
                    errorMsg,
                    "DLL格式错误",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);

                System.Diagnostics.Debug.WriteLine($"[Mil1394TestPanel] DLL格式错误: {ex.Message}");
            }
            catch (Exception ex)
            {
                IsDeviceConnected = false;
                ConnectionStatus = "离线";

                string errorMsg = $"板卡连接失败\n\n错误信息: {ex.Message}\n\n" +
                                 $"异常类型: {ex.GetType().Name}\n\n" +
                                 $"请检查:\n" +
                                 $"1. 板卡位置及驱动\n" +
                                 $"2. 硬件连接\n" +
                                 $"3. 系统驱动";

                ReMessageBox.Show(
                    errorMsg,
                    "连接失败",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);

                System.Diagnostics.Debug.WriteLine($"[Mil1394TestPanel] 板卡连接异常: {ex.GetType().Name} - {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[Mil1394TestPanel] 异常堆栈: {ex.StackTrace}");
            }
        }

        /// <summary>
        /// 关闭板卡
        /// </summary>
        private async Task OnCloseDeviceAsync()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[Mil1394TestPanel] 关闭板卡: {_device?.Name}");

                // 0. 停止数据计数刷新定时器（防止定时器继续访问已关闭的节点）
                _currentCardPanel?.StopDataCountRefreshTimer();

                // 关闭流程可能较慢，先更新UI状态，避免用户误以为卡死
                ConnectionStatus = "断开中";
                StatusMessage = "正在断开板卡...";

                // 1. 先停止所有接收线程和BM监控（防止ReadFile错误）
                if (_currentNodeSendRcvViewModels != null && _currentPnode != null)
                {
                    for (int i = 0; i < _currentNodeSendRcvViewModels.Count && i < _currentPnode.Length; i++)
                    {
                        try
                        {
                            if (_currentPnode[i] != IntPtr.Zero)
                            {
                                // 停止BM数据监控
                                _currentNodeSendRcvViewModels[i]?.StopBMDataMonitor(_currentPnode[i]);

                                // 停止接收
                                try
                                {
                                    _currentNodeSendRcvViewModels[i]?.StopReceive(_currentPnode[i]);
                                }
                                catch (Exception recvEx)
                                {
                                    System.Diagnostics.Debug.WriteLine($"[Mil1394TestPanel] 停止节点{i}接收失败: {recvEx.Message}");
                                }

                                // 兜底：即使StopReceive异常，也尽量停止接收线程（参考SIMP_1394例程的close流程）
                                try
                                {
                                    var diForRecvStop = _currentDriverInterfaces != null && i < _currentDriverInterfaces.Length
                                        ? _currentDriverInterfaces[i]
                                        : null;
                                    diForRecvStop?.HZStopRecvThd(_currentPnode[i]);
                                }
                                catch { }

                                // 停止发送
                                try
                                {
                                    _currentNodeSendRcvViewModels[i]?.StopSend(_currentPnode[i]);
                                }
                                catch (Exception sendEx)
                                {
                                    System.Diagnostics.Debug.WriteLine($"[Mil1394TestPanel] 停止节点{i}发送失败: {sendEx.Message}");
                                }

                                // 关闭前尽量强制停用硬件功能（驱动可能在内核层仍保持中断/收发状态）
                                try
                                {
                                    var di = _currentDriverInterfaces != null && i < _currentDriverInterfaces.Length
                                        ? _currentDriverInterfaces[i]
                                        : null;

                                    if (di != null)
                                    {
                                        try
                                        {
                                            int r1 = di.HZ1394_CC_BM_ENABLE(_currentPnode[i], 0);
                                            System.Diagnostics.Debug.WriteLine($"[Mil1394TestPanel] 节点{i} BM_DISABLE: {r1}");
                                        }
                                        catch { }

                                        try
                                        {
                                            int r2 = di.HZ1394_CC_MSG_RCV_STOF_ENABLE(_currentPnode[i], 0);
                                            System.Diagnostics.Debug.WriteLine($"[Mil1394TestPanel] 节点{i} STOF_RCV_DISABLE: {r2}");
                                        }
                                        catch { }

                                        try
                                        {
                                            int r3 = di.HZ1394_CRB_LRTC_ENABLE(_currentPnode[i], 0);
                                            System.Diagnostics.Debug.WriteLine($"[Mil1394TestPanel] 节点{i} LRTC_DISABLE: {r3}");
                                        }
                                        catch { }

                                        try { di.HZ1394_CC_MSG_STOF_Stop(_currentPnode[i]); } catch { }
                                        try { di.HZ1394_CC_MSG_ASYNC_SEND_Stop(_currentPnode[i]); } catch { }
                                        try { di.HZ1394_CC_MSG_ASYNC_RECV_Stop(_currentPnode[i]); } catch { }
                                        try { di.HZStopRecvThd(_currentPnode[i]); } catch { }
                                    }
                                }
                                catch { }
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[Mil1394TestPanel] 停止节点{i}操作失败: {ex.Message}");
                        }
                    }
                }

                // 2. 参考官方例程：不调用HZ1394_CC_Close和HZ1394_CC_RESET
                // 官方例程只停止接收线程，不关闭节点句柄（句柄保持有效，下次可直接使用）
                if (_currentPnode != null)
                {
                    for (int i = 0; i < _currentPnode.Length; i++)
                    {
                        _currentPnode[i] = IntPtr.Zero;
                    }
                    System.Diagnostics.Debug.WriteLine("[Mil1394TestPanel] 已清理节点句柄引用（不调用Close）");
                }

                // 3. 断开驱动连接（跳过，因为节点已在上面关闭，避免重复调用DLL导致阻塞）
                int slotIndex = _device?.SlotIndex ?? -1;
                var driver = DriverFactory.GetCachedDriver(_device?.Id, slotIndex);
                if (driver != null)
                {
                    System.Diagnostics.Debug.WriteLine($"[Mil1394TestPanel] 清理驱动缓存: DeviceId={_device?.Id}");
                    // 不再调用DisconnectAsync（避免重复调用DLL函数），直接清理缓存
                    bool removed = DriverFactory.RemoveCachedDriver(_device.Id);
                    System.Diagnostics.Debug.WriteLine($"[Mil1394TestPanel] 驱动缓存清理结果: {removed}");
                }
                else
                {
                    DriverFactory.RemoveCachedDriver(_device?.Id);
                }

                // 4. 清理引用
                _currentDriverInterfaces = null;
                _currentPnode = null;
                _currentNodeSendRcvViewModels = null;

                IsDeviceConnected = false;
                ConnectionStatus = "离线";

                // 5. 关闭板卡后，重新创建模拟界面（不实际连接）
                try
                {
                    CreateSimulatedWpfForm();
                    StatusMessage = "板卡已断开，使用模拟模式显示界面";
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Mil1394TestPanel] 重新创建模拟界面失败: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Mil1394TestPanel] 关闭板卡失败: {ex.Message}");
                // 即使关闭失败，也更新UI状态
                IsDeviceConnected = false;
                ConnectionStatus = "离线";
            }
        }

        /// <summary>
        /// 板卡名称改变处理
        /// </summary>
        public void OnCardNameChanged(string originalName)
        {
            if (_device != null && !string.IsNullOrEmpty(CardName))
            {
                // 更新设备名称
                _device.Name = CardName;
                System.Diagnostics.Debug.WriteLine($"[Mil1394TestPanel] 板卡名称已更新: {CardName}");
            }
        }

        public void Dispose()
        {
            // 取消事件订阅，避免内存泄漏
            _eventAggregator?.GetEvent<TestTaskCreatedEvent>()?.Unsubscribe(OnTestTaskCreated);
        }
    }
}
