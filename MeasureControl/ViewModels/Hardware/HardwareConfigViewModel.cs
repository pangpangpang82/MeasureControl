using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using MeasureControl.Constants;
using MeasureControl.Events;
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
using Prism.Services;

namespace MeasureControl.ViewModels.Hardware
{
    public class HardwareConfigViewModel : BindableBase, INavigationAware, IDisposable
    {
        private const bool FixedDemoMode = true;

        private readonly IRegionManager _regionManager;
        private readonly IEventAggregator _eventAggregator;
        private readonly IDialogService _dialogService;
        private readonly IPxiChassisService _pxiChassisService;
        private readonly IDragDropService _dragDropService;
        private readonly IWindowManagerService _windowManagerService;
        private readonly IChassisConnectionService _chassisConnectionService;
        private readonly ProjectService _projectService;
        private IRegionNavigationJournal _journal;
        private ProjectItem _currentProjectRoot;
        private ChassisConnection _activeConnection;
        private bool _suppressSourceSelectionChange;
        private bool _suppressTargetSelectionChange;
        private bool _suppressAssociatedSelectionChange;
        private bool _isPublishingDeleteEvent;

        /// <summary>
        /// 机箱控件刷新请求事件
        /// </summary>
        public event Action ChassisControlsRefreshRequested;

        /// <summary>
        /// 单个机箱状态更新事件
        /// </summary>
        public event Action<ChassisModel> ChassisStatusUpdateRequested;

        /// <summary>
        /// 连接线更新请求事件
        /// </summary>
        public event EventHandler ConnectionLinesUpdateRequested;

        public ObservableCollection<ChassisModel> PxiChassisList => _pxiChassisService.GetAllChassis();
        
        private ObservableCollection<ChassisConnection> _chassisConnections;
        public ObservableCollection<ChassisConnection> ChassisConnections 
        { 
            get 
            {
                if (_chassisConnections == null)
                {
                    _chassisConnections = new ObservableCollection<ChassisConnection>();
                }
                return _chassisConnections;
            }
        }

        /// <summary>
        /// 连接线列表（用于绘制）
        /// </summary>
        public List<ConnectionLine> ConnectionLines => _chassisConnectionService.GetConnectionLines();

        /// <summary>
        /// 更新连接集合
        /// </summary>
        private void UpdateChassisConnections()
        {
            var currentConnections = _chassisConnectionService.GetAllConnections();
            if (_chassisConnections == null)
            {
                _chassisConnections = new ObservableCollection<ChassisConnection>(currentConnections);
            }
            else
            {
                // 更新现有集合，保持引用不变
                var oldCount = _chassisConnections.Count;
                _chassisConnections.Clear();
                foreach (var connection in currentConnections)
                {
                    _chassisConnections.Add(connection);
                }
            }
            
            // 通知属性变更
            RaisePropertyChanged(nameof(ChassisConnections));
        }

        /// <summary>
        /// 选中的设备
        /// </summary>
        public Models.Devices.DeviceBase SelectedDevice
        {
            get => _selectedDevice;
            set => SetProperty(ref _selectedDevice, value);
        }

        /// <summary>
        /// 设备详细信息是否可见
        /// </summary>
        public bool IsDeviceDetailsVisible
        {
            get => _isDeviceDetailsVisible;
            set => SetProperty(ref _isDeviceDetailsVisible, value);
        }

        /// <summary>
        /// 选中的连接线详细信息
        /// </summary>
        public ConnectionDetails SelectedConnection
        {
            get => _selectedConnection;
            set => SetProperty(ref _selectedConnection, value);
        }

        /// <summary>
        /// 连接线详细信息是否可见
        /// </summary>
        public bool IsConnectionDetailsVisible
        {
            get => _isConnectionDetailsVisible;
            set => SetProperty(ref _isConnectionDetailsVisible, value);
        }

        private string _sourceChassisName;
        public string SourceChassisName
        {
            get => _sourceChassisName;
            set
            {
                if (SetProperty(ref _sourceChassisName, value))
                {
                    // 当源端机箱名变化时，刷新可选的通讯变量表
                    UpdateCommunicatingTabelOptions(true, GetCommunicatingTabelOptions(_sourceChassisName));
                }
            }
        }

        private string _sourceIpAddress;
        public string SourceIpAddress
        {
            get => _sourceIpAddress;
            set => SetProperty(ref _sourceIpAddress, value);
        }

        private string _sourcePort;
        public string SourcePort
        {
            get => _sourcePort;
            set
            {
                if (SetProperty(ref _sourcePort, value))
                {
                    UpdateConnectionPort(isSource: true, value);
                }
            }
        }

        private string _sourceLinkSpeed;
        public string SourceLinkSpeed
        {
            get => _sourceLinkSpeed;
            set => SetProperty(ref _sourceLinkSpeed, value);
        }

        public ObservableCollection<string> SourceCommunicatingTabels { get; } = new ObservableCollection<string>();

        private string _selectedSourceCommunicatingTabel;
        public string SelectedSourceCommunicatingTabel
        {
            get => _selectedSourceCommunicatingTabel;
            set
            {
                if (SetProperty(ref _selectedSourceCommunicatingTabel, value))
                {
                    UpdateConnectionCommunicatingTabel(true, value);
                }
            }
        }

        public bool HasSourceCommunicatingTabels => SourceCommunicatingTabels.Count > 0;

        private string _targetChassisName;
        public string TargetChassisName
        {
            get => _targetChassisName;
            set
            {
                if (SetProperty(ref _targetChassisName, value))
                {
                    // 当目标端机箱名变化时，刷新可选的通讯变量表
                    UpdateCommunicatingTabelOptions(false, GetCommunicatingTabelOptions(_targetChassisName));
                }
            }
        }

        private string _targetIpAddress;
        public string TargetIpAddress
        {
            get => _targetIpAddress;
            set => SetProperty(ref _targetIpAddress, value);
        }

        private string _targetPort;
        public string TargetPort
        {
            get => _targetPort;
            set
            {
                if (SetProperty(ref _targetPort, value))
                {
                    UpdateConnectionPort(isSource: false, value);
                }
            }
        }

        private string _targetLinkSpeed;
        public string TargetLinkSpeed
        {
            get => _targetLinkSpeed;
            set => SetProperty(ref _targetLinkSpeed, value);
        }

        public ObservableCollection<string> TargetCommunicatingTabels { get; } = new ObservableCollection<string>();

        private string _selectedTargetCommunicatingTabel;
        public string SelectedTargetCommunicatingTabel
        {
            get => _selectedTargetCommunicatingTabel;
            set
            {
                if (SetProperty(ref _selectedTargetCommunicatingTabel, value))
                {
                    UpdateConnectionCommunicatingTabel(false, value);
                }
            }
        }

        public bool HasTargetCommunicatingTabels => TargetCommunicatingTabels.Count > 0;

        public ObservableCollection<string> AssociatedNonCommunicatingTabels { get; } = new ObservableCollection<string>();

        private string _selectedAssociatedNonCommunicatingTabel;
        public string SelectedAssociatedNonCommunicatingTabel
        {
            get => _selectedAssociatedNonCommunicatingTabel;
            set
            {
                if (SetProperty(ref _selectedAssociatedNonCommunicatingTabel, value))
                {
                    UpdateAssociatedNonCommunicatingTabel(value);
                }
            }
        }

        public bool HasAssociatedNonCommunicatingTabels => AssociatedNonCommunicatingTabels.Count > 0;

        /// <summary>
        /// 详细信息面板是否可见（统一控制）
        /// </summary>
        public bool IsDetailsVisible
        {
            get => _isDetailsVisible;
            set => SetProperty(ref _isDetailsVisible, value);
        }

        /// <summary>
        /// 设备信息标题
        /// </summary>
        public string DeviceInfoTitle
        {
            get => _deviceInfoTitle;
            set => SetProperty(ref _deviceInfoTitle, value);
        }

        /// <summary>
        /// 动态字段1
        /// </summary>
        public string DeviceField1
        {
            get => _deviceField1;
            set => SetProperty(ref _deviceField1, value);
        }

        /// <summary>
        /// 动态字段2
        /// </summary>
        public string DeviceField2
        {
            get => _deviceField2;
            set => SetProperty(ref _deviceField2, value);
        }

        /// <summary>
        /// 动态字段3
        /// </summary>
        public string DeviceField3
        {
            get => _deviceField3;
            set => SetProperty(ref _deviceField3, value);
        }

        /// <summary>
        /// 动态字段4
        /// </summary>
        public string DeviceField4
        {
            get => _deviceField4;
            set => SetProperty(ref _deviceField4, value);
        }

        /// <summary>
        /// 动态字段5
        /// </summary>
        public string DeviceField5
        {
            get => _deviceField5;
            set => SetProperty(ref _deviceField5, value);
        }

        /// <summary>
        /// 动态字段6
        /// </summary>
        public string DeviceField6
        {
            get => _deviceField6;
            set => SetProperty(ref _deviceField6, value);
        }


        // 选中的机箱列表（用于连接）
        private readonly List<ChassisModel> _selectedChassis = new List<ChassisModel>();

        // 设备详细信息相关属性
        private Models.Devices.DeviceBase _selectedDevice;
        private bool _isDeviceDetailsVisible;
        private bool _isDetailsVisible;
        private string _deviceInfoTitle = "暂无信息";

        // 动态字段属性
        private string _deviceField1;
        private string _deviceField2;
        private string _deviceField3;
        private string _deviceField4;
        private string _deviceField5;
        private string _deviceField6;
        private string _deviceField7;

        // 连接线详细信息相关属性
        private ConnectionDetails _selectedConnection;
        private bool _isConnectionDetailsVisible;

        public ICommand NavigateToPxiChassisCommand { get; }
        public ICommand AddPxiChassisCommand { get; }
        public ICommand RenamePxiChassisCommand { get; }
        public ICommand DeletePxiChassisCommand { get; }
        public ICommand DropPxiChassisCommand { get; }
        public ICommand PxiChassisDoubleClickCommand { get; }
        public ICommand StartPxiChassisDragCommand { get; }
        public ICommand PxiSourceMouseEnterCommand { get; }
        public ICommand PxiSourceMouseLeaveCommand { get; }
        public ICommand PxiChassisClickCommand { get; }
        public ICommand ConnectChassisCommand { get; }
        public ICommand DisconnectChassisCommand { get; }
        public ICommand ClearChassisSelectionCommand { get; }
        public ICommand EthernetConnectCommand { get; }
        public ICommand UsbConnectCommand { get; }
        public ICommand SerialConnectCommand { get; }
        public ICommand ConnectionLineClickCommand { get; }
        public ICommand DisconnectConnectionLineCommand { get; }
        public ICommand RenameConnectionCommand { get; }
        public ICommand AddChassisFromDoubleClickCommand { get; }
        public DelegateCommand CloseInRegionCommand { get; }


        public HardwareConfigViewModel(IRegionManager regionManager, IEventAggregator eventAggregator,
            IDialogService dialogService, IPxiChassisService pxiChassisService, IDragDropService dragDropService,
            IWindowManagerService windowManagerService, IChassisConnectionService chassisConnectionService,
            ProjectService projectService)
        {
            _regionManager = regionManager;
            _eventAggregator = eventAggregator;
            _dialogService = dialogService;
            _pxiChassisService = pxiChassisService;
            _dragDropService = dragDropService;
            _windowManagerService = windowManagerService;
            _chassisConnectionService = chassisConnectionService;
            _projectService = projectService ?? throw new ArgumentNullException(nameof(projectService));
            _currentProjectRoot = _projectService.CurrentProjectRoot;

            NavigateToPxiChassisCommand = new DelegateCommand<string>(OnNavigateToPxiChassis);
            //AddPxiChassisCommand = new DelegateCommand<string>(OnAddPxiChassis);
            RenamePxiChassisCommand = new DelegateCommand<ChassisModel>(OnRenamePxiChassis);
            DeletePxiChassisCommand = new DelegateCommand<ChassisModel>(OnDeletePxiChassis);
            DropPxiChassisCommand = new DelegateCommand<DropPxiChassisArgs>(OnDropPxiChassis);
            PxiChassisDoubleClickCommand = new DelegateCommand<ChassisModel>(OnPxiChassisDoubleClick);
            StartPxiChassisDragCommand = new DelegateCommand<object>(OnStartPxiChassisDrag);
            PxiChassisClickCommand = new DelegateCommand<ChassisModel>(OnPxiChassisClick);
            ConnectChassisCommand = new DelegateCommand(OnConnectChassis);
            DisconnectChassisCommand = new DelegateCommand(OnDisconnectChassis);
            ClearChassisSelectionCommand = new DelegateCommand(OnClearChassisSelection);
            EthernetConnectCommand = new DelegateCommand(() => OnDirectConnect(ConnectionType.Ethernet));
            UsbConnectCommand = new DelegateCommand(() => OnDirectConnect(ConnectionType.USB));
            SerialConnectCommand = new DelegateCommand(() => OnDirectConnect(ConnectionType.Serial));
            ConnectionLineClickCommand = new DelegateCommand<ChassisConnection>(OnConnectionLineClick);
            DisconnectConnectionLineCommand = new DelegateCommand<ChassisConnection>(OnDisconnectConnectionLine);
            RenameConnectionCommand = new DelegateCommand<ChassisConnection>(OnRenameConnection);
            AddChassisFromDoubleClickCommand = new DelegateCommand<string>(OnAddChassisFromDoubleClick);
            CloseInRegionCommand = new DelegateCommand(OnCloseInRegion);

            // 订阅事件以保持数据同步
            SubscribeToEvents();
        }

        /// <summary>
        /// 订阅相关事件
        /// </summary>
        private void SubscribeToEvents()
        {
            _eventAggregator.GetEvent<DeletePxiChassisEvent>().Subscribe(OnPxiChassisDeleted);
            _eventAggregator.GetEvent<RenamePxiChassisEvent>().Subscribe(OnPxiChassisRenamed);
            _eventAggregator.GetEvent<AddPxiChassisEvent>().Subscribe(OnPxiChassisAdded);

            // 订阅拖拽服务事件
            _dragDropService.PxiChassisDropped += OnPxiChassisDropped;

            // 订阅机箱连接变化事件
            ChassisConnections.CollectionChanged += OnChassisConnectionsChanged;

            // 订阅连接数据请求事件
            _eventAggregator.GetEvent<ChassisConnectionsRequestEvent>().Subscribe(OnChassisConnectionsRequested);

            // 订阅连接数据加载事件
            _eventAggregator.GetEvent<ChassisConnectionsLoadEvent>().Subscribe(OnChassisConnectionsLoaded);
            // 订阅连接线请求事件
            _eventAggregator.GetEvent<ConnectionLinesRequestEvent>().Subscribe(OnConnectionLinesRequest);

            // 订阅设备点击事件
            _eventAggregator.GetEvent<DeviceClickedEvent>().Subscribe(OnDeviceClicked);

            // 订阅连接线加载事件
            _eventAggregator.GetEvent<ConnectionLinesLoadEvent>().Subscribe(OnConnectionLinesLoad);

            // 订阅机箱选择事件
            _eventAggregator.GetEvent<PxiChassisSelectedEvent>().Subscribe(OnPxiChassisSelected);

            // 订阅清除设备详细信息事件
            _eventAggregator.GetEvent<ClearDeviceDetailsEvent>().Subscribe(OnClearDeviceDetails);
            
            // 订阅项目关闭事件
            _eventAggregator.GetEvent<ProjectClosedEvent>().Subscribe(OnProjectClosed);

            _eventAggregator.GetEvent<ProjectOpenedEvent>().Subscribe(OnProjectOpened);
            _eventAggregator.GetEvent<ProjectCreatedEvent>().Subscribe(OnProjectOpened);

            // 订阅测试任务创建事件，用于更新通信变量表选项
            _eventAggregator.GetEvent<TestTaskCreatedEvent>().Subscribe(OnTestTaskCreated);
        }

        private void OnPxiChassisDropped(object sender, DropPxiChassisArgs e)
        {
            OnDropPxiChassis(e);
        }

        /// <summary>
        /// 处理机箱连接变化事件
        /// </summary>
        private void OnChassisConnectionsChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            // 通知UI更新连接线
            RaisePropertyChanged(nameof(ChassisConnections));
        }

        /// <summary>
        /// 处理连接数据请求事件
        /// </summary>
        private void OnChassisConnectionsRequested(ChassisConnectionsRequestEventArgs args)
        {
            // 提供当前的连接数据
            args.Connections = new List<ChassisConnection>(_chassisConnectionService.GetAllConnections());
        }

        /// <summary>
        /// 处理连接数据加载事件
        /// </summary>
        private void OnChassisConnectionsLoaded(ChassisConnectionsLoadEventArgs args)
        {
            if (args.Connections != null)
            {
                // 清空现有连接
                _chassisConnectionService.ClearConnections();

                // 加载新的连接数据
                foreach (var connection in args.Connections)
                {
                    _chassisConnectionService.AddConnection(connection);
                }
                // 更新连接集合
                UpdateChassisConnections();

                // 更新持久的ChassisConnections集合
                if (_chassisConnections != null)
                {
                    _chassisConnections.Clear();
                    foreach (var connection in args.Connections)
                    {
                        _chassisConnections.Add(connection);
                    }
                }

                // 通知UI更新
                RaisePropertyChanged(nameof(ChassisConnections));

                // 延迟触发连接线更新，确保机箱控件已经创建和注册
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    ConnectionLinesUpdateRequested?.Invoke(this, EventArgs.Empty);
                }), System.Windows.Threading.DispatcherPriority.Loaded);
            }
        }

        /// <summary>
        /// 处理连接线请求事件
        /// </summary>
        private void OnConnectionLinesRequest(ConnectionLinesRequestEventArgs args)
        {
            if (args != null)
            {
                args.ConnectionLines = _chassisConnectionService.GetConnectionLines();
            }
        }

        /// <summary>
        /// 处理连接线加载事件
        /// </summary>
        private void OnConnectionLinesLoad(ConnectionLinesLoadEventArgs args)
        {
            if (args.ConnectionLines != null)
            {
                // 清除现有连接
                _chassisConnectionService.ClearConnections();

                // 重新创建连接和连接线
                foreach (var connectionLine in args.ConnectionLines)
                {
                    try
                    {
                        // 创建ChassisConnection
                        var parsedConnectionType = (ConnectionType)Enum.Parse(typeof(ConnectionType), connectionLine.ConnectionType);
                        var connection = new ChassisConnection(
                            connectionLine.SourceChassisId,
                            connectionLine.TargetChassisId,
                            parsedConnectionType
                        )
                        {
                            ConnectionName = string.IsNullOrWhiteSpace(connectionLine.ConnectionName)
                                ? ChassisConnection.GetConnectionTypeDisplayName(parsedConnectionType)
                                : connectionLine.ConnectionName
                        };

                        // 恢复额外的信息（如果 ConnectionLine 中包含）
                        try
                        {
                            // ConnectionLine 的 Speed 字段里可能包含真实检测速率或默认值
                            if (!string.IsNullOrWhiteSpace(connectionLine.Speed) && connectionLine.Speed != "未知")
                            {
                                connection.ActualLinkSpeed = connectionLine.Speed;
                            }

                            // 恢复通讯变量表选择（如果保存了）
                            connection.SourceCommunicatingTabel = connectionLine.SourceCommunicatingTabel;
                            connection.TargetCommunicatingTabel = connectionLine.TargetCommunicatingTabel;
                        }
                        catch
                        {
                            // 忽略恢复失败，不影响加载其余数据
                        }

                        _chassisConnectionService.AddConnection(connection);
                    }
            catch (Exception)
            {
            }
                }

                
                // 更新连接集合
                UpdateChassisConnections();

                // 更新持久的ChassisConnections集合
                if (_chassisConnections != null)
                {
                    _chassisConnections.Clear();
                    foreach (var connection in _chassisConnectionService.GetAllConnections())
                    {
                        _chassisConnections.Add(connection);
                    }
                }

                // 通知UI更新连接数据
                RaisePropertyChanged(nameof(ChassisConnections));

                // 使用更长的延迟确保所有机箱控件都已创建和注册到Canvas
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    ConnectionLinesUpdateRequested?.Invoke(this, EventArgs.Empty);
                    
                    // 再次延迟，确保连接线能够正确绘制
                    Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        ConnectionLinesUpdateRequested?.Invoke(this, EventArgs.Empty);
                    }), System.Windows.Threading.DispatcherPriority.ContextIdle);
                }), System.Windows.Threading.DispatcherPriority.Loaded);
            }
        }

        /// <summary>
        /// 处理机箱选择事件
        /// </summary>
        private void OnPxiChassisSelected(PxiChassisSelectedEventArgs args)
        {

            // 隐藏连接线详细信息
            IsConnectionDetailsVisible = false;

            // 查找对应的机箱模型
            var chassis = _pxiChassisService.GetAllChassis().FirstOrDefault(c => c.Id == args.ChassisId || c.Name == args.ChassisName);
            if (chassis == null)
            {
                return;
            }

            // 机箱选择时不需要设置 SelectedDevice，因为机箱信息已经通过属性字段显示
            SelectedDevice = null;

            // 设置机箱的属性字段（包括IP地址）
            DeviceField1 = $"机箱名称：  {chassis.Name}";
            DeviceField2 = $"制造商：  {chassis.Manufacturer ?? ""}";
            DeviceField3 = $"型号：  {chassis.Model ?? ""}";
            DeviceField4 = $"槽位数：  {chassis.SlotCount}";
            DeviceField5 = $"占位符1：  {chassis.DF1 ?? ""}";
            DeviceField6 = $"占位符2：  {chassis.DF2 ?? ""} ";

            // 显示统一属性面板
            IsDetailsVisible = true;

            // 更新标题为"设备详细信息"
            DeviceInfoTitle = "设备详细信息";

        }

        /// <summary>
        /// 处理清除设备详细信息事件
        /// </summary>
        private void OnClearDeviceDetails()
        {
            ClearDeviceDetails();
        }

        /// <summary>
        /// 清除设备详细信息显示
        /// </summary>
        private void ClearDeviceDetails()
        {
            // 隐藏详细信息面板
            IsDetailsVisible = false;
            IsConnectionDetailsVisible = false;
            
            // 清除选中的设备和连接
            SelectedDevice = null;
            SelectedConnection = null;
            _activeConnection = null;
            
            // 重置标题为"暂无信息"
            DeviceInfoTitle = "暂无信息";
            
            // 清空所有字段
            DeviceField1 = "";
            DeviceField2 = "";
            DeviceField3 = "";
            DeviceField4 = "";
            DeviceField5 = "";
            DeviceField6 = "";

            SourceChassisName = "";
            SourceIpAddress = "";
            SourcePort = "";
            SourceLinkSpeed = "";
            TargetChassisName = "";
            TargetIpAddress = "";
            TargetPort = "";
            TargetLinkSpeed = "";
            SourceCommunicatingTabels.Clear();
            TargetCommunicatingTabels.Clear();
            AssociatedNonCommunicatingTabels.Clear();
            RaisePropertyChanged(nameof(HasSourceCommunicatingTabels));
            RaisePropertyChanged(nameof(HasTargetCommunicatingTabels));
            RaisePropertyChanged(nameof(HasAssociatedNonCommunicatingTabels));
            _suppressSourceSelectionChange = true;
            SelectedSourceCommunicatingTabel = null;
            _suppressSourceSelectionChange = false;
            _suppressTargetSelectionChange = true;
            SelectedTargetCommunicatingTabel = null;
            _suppressTargetSelectionChange = false;
            _suppressAssociatedSelectionChange = true;
            SelectedAssociatedNonCommunicatingTabel = null;
            _suppressAssociatedSelectionChange = false;
        }

        /// <summary>
        /// 处理设备点击事件，显示设备详细信息
        /// </summary>
        private void OnDeviceClicked(DeviceClickedEventArgs args)
        {
            if (args?.Device == null) return;

            // 隐藏连接线详细信息
            IsConnectionDetailsVisible = false;

            // 设置选中的设备
            SelectedDevice = args.Device;

            // 根据设备类型设置不同的属性字段
            //SetDeviceFieldsByType(args);

            // 显示统一属性面板
            IsDetailsVisible = true;

            // 更新标题为"设备详细信息"
            DeviceInfoTitle = "设备详细信息";
        }

        /// <summary>
        /// 获取设备所属的机箱名称
        /// </summary>
        private string GetChassisNameForDevice(Models.Devices.DeviceBase device)
        {
            if (device == null) return "未知";
            
            // 如果设备本身就是机箱设备
            if (device.DeviceType == "Chassis")
            {
                return device.Name ?? "未知机箱";
            }
            
            return "当前机箱";
        }

        //private void SetDeviceFieldsByType(DeviceClickedEventArgs args)
        //{
        //    var device = args.Device;
            
        //    switch (args.DeviceType)
        //    {
        //        //case "Chassis":
        //        //    // 机箱设备
        //        //    DeviceField1 = $"机箱名称：  {args.DeviceName}";
        //        //    DeviceField2 = $"制造商：  {args.Manufacturer ?? ""}";
        //        //    DeviceField3 = $"型号：  {args.Model ?? ""}";
        //        //    DeviceField4 = $"槽位数：  {args.Status ?? ""}";
        //        //    DeviceField5 = $"占位符1：  {args.Description ?? ""}";
        //        //    DeviceField6 = $"占位符2：  {args.DF1 ?? ""}";
        //            break;

        //        //case "Card":
        //        //    // PXI板卡设备
        //        //    DeviceField1 = $"设备名称：  {args.DeviceName}";
        //        //    DeviceField2 = $"制造商：  {args.Manufacturer ?? ""}";
        //        //    DeviceField3 = $"型号：  {args.Model ?? ""}";
        //        //    DeviceField4 = $"所属机箱：  {GetChassisNameForDevice(device)}";
        //        //    DeviceField5 = $"插槽位置：  {device.SlotPosition ?? ""}";
        //        //    DeviceField6 = $"状态：  {args.Status ?? ""}";
        //        //    break;

        //        case "Instrument":
        //            // 程控仪器设备（电源、电子负载、仪表等）
        //            DeviceField1 = $"设备名称：  {args.DeviceName}";
        //            DeviceField2 = $"制造商：  {args.Manufacturer ?? ""}";
        //            DeviceField3 = $"型号：  {args.Model ?? ""}";
        //            DeviceField4 = $"连接方式：  {args.ConnectionMethod ?? ""}";
        //            DeviceField5 = $"父节点：  {args.ParentNode ?? ""}";
        //            DeviceField6 = $"状态：  {args.Status ?? ""}";
        //            break;

        //        default:
        //            // 其他设备类型
        //            DeviceField1 = $"设备名称：  {args.DeviceName}";
        //            DeviceField2 = $"制造商：  {args.Manufacturer ?? ""}";
        //            DeviceField3 = $"型号：  {args.Model ?? ""}";
        //            DeviceField4 = $"状态：  {args.Status ?? ""}";
        //            DeviceField5 = $"描述：  {args.Description ?? ""}";
        //            DeviceField6 = $"详细信息：  {args.Details ?? ""}";
        //            break;
        //    }
        //}

        /// <summary>
        /// 处理机箱删除事件
        /// </summary>
        private void OnPxiChassisDeleted(string chassisName)
        {
            if (_isPublishingDeleteEvent)
            {
                return;
            }

            HandleChassisDeleted(chassisName);
        }

        private void HandleChassisDeleted(string chassisName)
        {
            if (string.IsNullOrWhiteSpace(chassisName))
            {
                return;
            }

            ClearDeviceDetails();
            RemoveChassisFromCurrentProject(chassisName);
            RaisePropertyChanged(nameof(PxiChassisList));

            Application.Current.Dispatcher.Invoke(() =>
            {
                ChassisControlsRefreshRequested?.Invoke();
            });
        }

        private void RemoveChassisFromCurrentProject(string chassisName)
        {
            if (_currentProjectRoot?.Children == null)
            {
                return;
            }

            void RemoveNodes()
            {
                var hardwareConfigNode = _currentProjectRoot.Children?
                    .FirstOrDefault(item => item.Name == AppConstants.NodeNameHardwareConfig);
                var deviceNetworkNode = hardwareConfigNode?.Children?
                    .FirstOrDefault(item => item.Name == AppConstants.NodeNameDeviceNetwork);

                // 1) 直接挂在"硬件配置"下的机箱节点（新结构）
                var chassisDirectUnderHardwareConfig = hardwareConfigNode?.Children?
                    .FirstOrDefault(child => child.Name == chassisName && child.Type == AppConstants.NodeTypePxiChassis);
                if (chassisDirectUnderHardwareConfig != null)
                {
                    hardwareConfigNode.Children.Remove(chassisDirectUnderHardwareConfig);
                }

                var chassisInHardwareConfig = deviceNetworkNode?.Children?
                    .FirstOrDefault(child => child.Name == chassisName && child.Type == AppConstants.NodeTypePxiChassis);
                if (chassisInHardwareConfig != null)
                {
                    deviceNetworkNode.Children.Remove(chassisInHardwareConfig);
                }

                var topLevelChassis = _currentProjectRoot.Children
                    .FirstOrDefault(item => item.Name == chassisName && item.Type == AppConstants.NodeTypePxiChassis);
                if (topLevelChassis != null)
                {
                    _currentProjectRoot.Children.Remove(topLevelChassis);
                }
            }

            if (Application.Current.Dispatcher.CheckAccess())
            {
                RemoveNodes();
            }
            else
            {
                Application.Current.Dispatcher.Invoke(RemoveNodes);
            }
        }

        /// <summary>
        /// 处理机箱重命名事件
        /// </summary>
        private void OnPxiChassisRenamed(RenamePxiChassisEventArgs args)
        {
            // 通知UI更新机箱列表
            RaisePropertyChanged(nameof(PxiChassisList));

            // 通知View刷新机箱控件
            Application.Current.Dispatcher.Invoke(() =>
            {
                ChassisControlsRefreshRequested?.Invoke();
            });
        }

        /// <summary>
        /// 处理机箱添加事件
        /// </summary>
        private void OnPxiChassisAdded(string chassisName)
        {
            // 通知UI更新机箱列表
            RaisePropertyChanged(nameof(PxiChassisList));

            // 通知View刷新机箱控件
            Application.Current.Dispatcher.Invoke(() =>
            {
                ChassisControlsRefreshRequested?.Invoke();
            });
        }

        //private void OnAddPxiChassis(string chassisName)
        //{
        //    if (string.IsNullOrEmpty(chassisName))
        //        return;

        //    _eventAggregator.GetEvent<AddPxiChassisEvent>().Publish(chassisName);
        //}

        private void OnDropPxiChassis(DropPxiChassisArgs args)
        {
            if (FixedDemoMode)
            {
                return;
            }

            if (args == null) return;

            // 检查位置是否已被占用（这里只是双重检查，主要检查在View中进行）
            if (_pxiChassisService.IsPositionOccupied(args.Row, args.Column))
            {
                return; // View中已经显示了警告，这里直接返回
            }

            // 使用服务生成默认机箱名称
            var defaultChassisName = _pxiChassisService.GenerateUniqueName(AppConstants.DefaultChassisNamePrefix);

            // 弹出添加机箱对话框，让用户输入机箱名称和IP地址
            var dialogViewModel = new AddChassisDialogViewModel(args.ChassisModel, defaultChassisName);
            var dialog = new AddChassisDialog(dialogViewModel);
            dialog.Owner = Application.Current.MainWindow;
            
            if (dialog.ShowDialog() == true)
            {
                var chassisName = dialog.ChassisNameResult;
                var ipAddress = dialog.IpAddressResult;
                var subnetMask = dialog.SubnetMaskResult;
                
                // 检查名称是否已被占用
                if (_pxiChassisService.GetChassisByName(chassisName) != null)
                {
                    _dialogService.ShowWarningDialog($"机箱名称 '{chassisName}' 已被占用，请使用其他名称。", "名称冲突");
                    return;
                }

                // 使用 ChassisFactory 创建对应的机箱模型
                ChassisModel newChassis = ChassisFactory.CreateChassis(args.ChassisModel, chassisName, args.Row, args.Column);

                if (newChassis == null)
                {
                    _dialogService.ShowWarningDialog($"创建机箱失败：不支持的机箱型号 '{args.ChassisModel}'。", "提示");
                    return;
                }

                // 用户确认添加时才占用名称
                _pxiChassisService.ReserveChassisName(chassisName);
                
                // 设置IP地址和子网掩码
                if (!string.IsNullOrEmpty(ipAddress))
                {
                    newChassis.IpAddress = ipAddress;
                }
                if (!string.IsNullOrEmpty(subnetMask))
                {
                    newChassis.SubnetMask = subnetMask;
                }

                if (!_pxiChassisService.AddChassis(newChassis))
                {
                    _dialogService.ShowWarningDialog("添加机箱失败：位置可能已被占用或机箱数据无效。", "提示");
                    return;
                }

                // 自动创建对应的 ChassisDevice
                _pxiChassisService.EnsureChassisDevice(chassisName, args.ChassisModel);
                
                _eventAggregator.GetEvent<AddPxiChassisEvent>().Publish(chassisName);
                
                // 发布项目修改事件，触发自动保存
                _eventAggregator.GetEvent<ProjectModifiedEvent>().Publish(new ProjectModifiedEventArgs
                {
                    ModificationType = "Chassis",
                    Description = $"添加了机箱: {chassisName} ({args.ChassisModel})"
                });
            }
            // 用户取消则不添加机箱
        }

        private void OnRenamePxiChassis(ChassisModel chassis)
        {
            if (FixedDemoMode)
            {
                return;
            }

            if (chassis == null) return;

            var newName = _dialogService.ShowRenameDialog(chassis.Name, "重命名机箱");
            if (!string.IsNullOrEmpty(newName) && newName != chassis.Name)
            {
                var oldName = chassis.Name;

                _pxiChassisService.UpdateChassisName(chassis.Id, newName);
                // 发布事件，通知UI更新机箱名称
                var renameInfo = new RenamePxiChassisEventArgs
                {
                    ChassisId = chassis.Id,
                    OldName = oldName,
                    NewName = newName
                };
                _eventAggregator.GetEvent<RenamePxiChassisEvent>().Publish(renameInfo);
            }
        }

        private void OnDeletePxiChassis(ChassisModel chassis)
        {
            if (FixedDemoMode)
            {
                return;
            }

            if (chassis == null) return;

            // 检查机箱是否有连接
            if (_chassisConnectionService.HasChassisConnections(chassis.Id))
            {
                // 获取机箱的所有连接
                var connections = _chassisConnectionService.GetConnectionsByChassis(chassis.Id);
                var connectionDetails = new List<string>();
                
                foreach (var connection in connections)
                {
                    var otherChassisId = connection.GetOtherChassisId(chassis.Id);
                    var otherChassis = _pxiChassisService.GetAllChassis().FirstOrDefault(c => c.Id == otherChassisId);
                    var otherChassisName = otherChassis?.Name ?? otherChassisId;
                    connectionDetails.Add($"• {ChassisConnection.GetConnectionTypeDisplayName(connection.ConnectionType)} - 连接到 '{otherChassisName}'");
                }
                
                var message = $"请先断开机箱的连接后再尝试删除。";
                
                _dialogService.ShowWarningDialog(message, "无法删除机箱");
                return;
            }

            var result = _dialogService.ShowConfirmDialog(
                $"确定要删除机箱 '{chassis.Name}' 吗？",
                "确认删除");

            if (result == MessageBoxResult.Yes)
            {
                var chassisName = chassis.Name;
                var removed = _pxiChassisService.RemoveChassis(chassis.Id);
                if (!removed)
                {
                    return;
                }

                HandleChassisDeleted(chassisName);

                try
                {
                    _isPublishingDeleteEvent = true;
                    _eventAggregator.GetEvent<DeletePxiChassisEvent>().Publish(chassisName);
                }
                finally
                {
                    _isPublishingDeleteEvent = false;
                }
            }
        }

        private void OnPxiChassisDoubleClick(ChassisModel chassis)
        {
            if (chassis != null)
            {
                OnNavigateToPxiChassis(chassis.Name);
            }
        }

        /// <summary>
        /// 双击机箱图片添加机箱
        /// </summary>
        private void OnAddChassisFromDoubleClick(string chassisType)
        {
            if (FixedDemoMode)
            {
                return;
            }

            if (string.IsNullOrEmpty(chassisType)) return;

            // 查找下一个可用位置
            var position = _pxiChassisService.GetNextAvailablePosition();
            
            if (!position.HasValue || position.Value.Row < 0 || position.Value.Column < 0)
            {
                _dialogService.ShowWarningDialog("没有可用的机箱槽位，无法添加更多机箱。", "提示");
                return;
            }
            
            var row = position.Value.Row;
            var column = position.Value.Column;

            // 使用服务生成默认机箱名称
            var defaultChassisName = _pxiChassisService.GenerateUniqueName(AppConstants.DefaultChassisNamePrefix);

            // 弹出添加机箱对话框
            var dialogViewModel = new AddChassisDialogViewModel(chassisType, defaultChassisName);
            var dialog = new AddChassisDialog(dialogViewModel);
            dialog.Owner = Application.Current.MainWindow;
            
            if (dialog.ShowDialog() == true)
            {
                var chassisName = dialog.ChassisNameResult;
                var ipAddress = dialog.IpAddressResult;
                
                // 检查名称是否已被占用（可能在对话框打开期间被其他操作占用）
                if (_pxiChassisService.GetChassisByName(chassisName) != null)
                {
                    _dialogService.ShowWarningDialog($"机箱名称 '{chassisName}' 已被占用，请使用其他名称。", "名称冲突");
                    return;
                }

                // 使用 ChassisFactory 创建对应的机箱模型
                ChassisModel newChassis = ChassisFactory.CreateChassis(chassisType, chassisName, row, column);

                if (newChassis == null)
                {
                    _dialogService.ShowWarningDialog($"创建机箱失败：不支持的机箱型号 '{chassisType}'。", "提示");
                    return;
                }

                // 用户确认添加时才占用名称
                _pxiChassisService.ReserveChassisName(chassisName);

                // 设置IP地址
                if (!string.IsNullOrEmpty(ipAddress))
                {
                    newChassis.IpAddress = ipAddress;
                }

                if (!_pxiChassisService.AddChassis(newChassis))
                {
                    _dialogService.ShowWarningDialog("添加机箱失败：位置可能已被占用或机箱数据无效。", "提示");
                    return;
                }

                // 自动创建对应的 ChassisDevice
                _pxiChassisService.EnsureChassisDevice(chassisName, chassisType);
                
                _eventAggregator.GetEvent<AddPxiChassisEvent>().Publish(chassisName);
                
                // 刷新机箱控件
                ChassisControlsRefreshRequested?.Invoke();
                
                // 发布项目修改事件
                _eventAggregator.GetEvent<ProjectModifiedEvent>().Publish(new ProjectModifiedEventArgs
                {
                    ModificationType = "Chassis",
                    Description = $"添加了机箱: {chassisName} ({chassisType})"
                });
            }
        }

        private void OnStartPxiChassisDrag(object parameter)
        {
            if (FixedDemoMode)
            {
                return;
            }

            if (parameter is System.Windows.FrameworkElement element)
            {
                _dragDropService.StartPxiChassisDrag(element);
            }
        }

        private void EnsureFixedDemoChassis()
        {
            if (!FixedDemoMode)
            {
                return;
            }

            try
            {
                var all = _pxiChassisService.GetAllChassis();
                if (all != null)
                {
                    for (int i = all.Count - 1; i >= 0; i--)
                    {
                        var chassis = all[i];
                        if (chassis == null) continue;
                        if (chassis.Name != "PXI机箱1" && chassis.Name != "PXI机箱2")
                        {
                            _pxiChassisService.RemoveChassis(chassis.Id);
                        }
                    }
                }

                var chassis1 = _pxiChassisService.GetChassisByName("PXI机箱1");
                if (chassis1 == null)
                {
                    _pxiChassisService.ReserveChassisName("PXI机箱1");
                    chassis1 = ChassisFactory.CreateChassis("PXIe-2722G2", "PXI机箱1", 0, 0);
                    if (chassis1 != null)
                    {
                        _pxiChassisService.AddChassis(chassis1);
                    }
                }

                var chassis2 = _pxiChassisService.GetChassisByName("PXI机箱2");
                if (chassis2 == null)
                {
                    _pxiChassisService.ReserveChassisName("PXI机箱2");
                    chassis2 = ChassisFactory.CreateChassis("PXIe-2519G2", "PXI机箱2", 0, 1);
                    if (chassis2 != null)
                    {
                        _pxiChassisService.AddChassis(chassis2);
                    }
                }

                if (chassis1 != null)
                {
                    chassis1.GridRow = 0;
                    chassis1.GridColumn = 0;
                    chassis1.Model = "PXIe-2722G2";
                    chassis1.ChassisType = "PXIe-2722G2";
                    _pxiChassisService.EnsureChassisDevice("PXI机箱1", "PXIe-2722G2");
                }

                if (chassis2 != null)
                {
                    chassis2.GridRow = 0;
                    chassis2.GridColumn = 1;
                    chassis2.Model = "PXIe-2519G2";
                    chassis2.ChassisType = "PXIe-2519G2";
                    _pxiChassisService.EnsureChassisDevice("PXI机箱2", "PXIe-2519G2");
                }
            }
            catch
            {
            }

            RaisePropertyChanged(nameof(PxiChassisList));
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                ChassisControlsRefreshRequested?.Invoke();
                ConnectionLinesUpdateRequested?.Invoke(this, EventArgs.Empty);
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }


        /// <summary>
        /// 处理机箱点击事件（单击显示详细信息，Ctrl+左键选择）
        /// </summary>
        private void OnPxiChassisClick(ChassisModel chassis)
        {

            if (chassis == null) return;

            // 使用更可靠的键盘状态检测方法
            bool isCtrlPressed = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;

            // 检查是否按下了Ctrl键
            if (isCtrlPressed)
            {
                if (_selectedChassis.Contains(chassis))
                {
                    // 如果已选中，则取消选择
                    _selectedChassis.Remove(chassis);
                    chassis.IsSelected = false;
                }
                else
                {
                    // 如果未选中，则添加到选择列表
                    if (_selectedChassis.Count < 2)
                    {
                        _selectedChassis.Add(chassis);
                        chassis.IsSelected = true;
                    }
                    else
                    {
                        // 如果已选择2个机箱，先清除选择，再选择当前机箱
                        ClearSelection();
                        _selectedChassis.Add(chassis);
                        chassis.IsSelected = true;
                    }
                }

                // 通知UI更新当前机箱的状态
                ChassisStatusUpdateRequested?.Invoke(chassis);
            }
            else
            {
                // 没有按Ctrl键，清除所有选择
                ClearSelection();

                // 单击只显示详细信息面板，不导航（双击才导航）
                // 隐藏连接线详细信息
                IsConnectionDetailsVisible = false;

                SelectedDevice = null;

                // 设置机箱的7个属性字段
                DeviceField1 = $"机箱名称：  {chassis.Name}";
                DeviceField2 = $"制造商：  {chassis.Manufacturer ?? ""}";
                DeviceField3 = $"型号：  {chassis.Model ?? ""}";
                DeviceField4 = $"槽位数：  {chassis.SlotCount}";
                DeviceField5 = $"占位符1：  {chassis.DF1 ?? ""}";
                DeviceField6 = $"占位符2：  {chassis.DF2 ?? ""}";

                // 显示统一属性面板
                IsDetailsVisible = true;

                // 更新标题为"设备详细信息"
                DeviceInfoTitle = "设备详细信息";
            }
        }

        /// <summary>
        /// 清除机箱选择
        /// </summary>
        private void ClearSelection()
        {
            var clearedChassis = new List<ChassisModel>(_selectedChassis);
            foreach (var chassis in clearedChassis)
            {
                chassis.IsSelected = false;
                // 通知UI更新每个被清除的机箱状态
                ChassisStatusUpdateRequested?.Invoke(chassis);
            }
            _selectedChassis.Clear();

            // 隐藏详细信息面板
            IsDetailsVisible = false;

            // 重置标题为"暂无信息"
            DeviceInfoTitle = "暂无信息";
        }

        /// <summary>
        /// 处理连接机箱命令
        /// </summary>
        private void OnConnectChassis()
        {

            if (_selectedChassis.Count != 2)
            {
                ReMessageBox.Show("请 Ctrl 键，依次选中两个机箱", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var sourceChassis = _selectedChassis[0];
            var targetChassis = _selectedChassis[1];


            // 检查是否已经连接
            if (_chassisConnectionService.AreChassisConnected(sourceChassis.Id, targetChassis.Id))
            {
                ReMessageBox.Show("这两个机箱已经连接", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // 显示连接对话框
            var dialog = new ChassisConnectionDialog();
            // 禁用已有名称的确认：如果名称被占用则对话框确认按钮不可用（避免弹出另一个重命名对话框）
            Func<string, bool> isNameAvailable = name => !_chassisConnectionService.IsConnectionNameInUse(name);
            // 生成默认唯一名称（连接方式 + index），并预填到对话框以避免用户必须手动命名
            string baseNameForDialog = ChassisConnection.GetConnectionTypeDisplayName(ConnectionType.Ethernet);
            int dialogIdx = 1;
            string dialogCandidate;
            do
            {
                dialogCandidate = $"{baseNameForDialog} {dialogIdx}";
                dialogIdx++;
            } while (_chassisConnectionService.IsConnectionNameInUse(dialogCandidate));
            var dialogViewModel = new ChassisConnectionDialogViewModel(sourceChassis.Name, targetChassis.Name, isNameAvailable)
            {
                ConnectionName = dialogCandidate
            };
            dialog.DataContext = dialogViewModel;

            // 设置对话框的所有者为主窗口，确保对话框显示在主窗口中心
            dialog.Owner = Application.Current.MainWindow;

            // 订阅对话框关闭事件
            dialogViewModel.DialogClosed += (result) =>
            {
                dialog.DialogResult = result;
                dialog.Close();
            };

            var result = dialog.ShowDialog();
            if (result == true)
            {
                // 创建连接并采集名称
                var connection = new ChassisConnection(sourceChassis.Id, targetChassis.Id, dialogViewModel.SelectedConnectionType);
                var connectionName = dialogViewModel.ConnectionName?.Trim();
                if (string.IsNullOrWhiteSpace(connectionName))
                {
                    connectionName = ChassisConnection.GetConnectionTypeDisplayName(dialogViewModel.SelectedConnectionType);
                }
                if (_chassisConnectionService.IsConnectionNameInUse(connectionName))
                {
                    // 自动生成唯一名称：使用连接方式 + 索引（从1开始）
                    string baseName = ChassisConnection.GetConnectionTypeDisplayName(dialogViewModel.SelectedConnectionType);
                    int idx = 1;
                    string candidate;
                    do
                    {
                        candidate = $"{baseName} {idx}";
                        idx++;
                    } while (_chassisConnectionService.IsConnectionNameInUse(candidate));
                    connectionName = candidate;
                }
                connection.ConnectionName = connectionName;

                var addResult = _chassisConnectionService.AddConnection(connection);
                if (addResult)
                {
                    ReMessageBox.Show("机箱连接成功", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                    ClearSelection();
                    
                    // 更新连接集合
                    UpdateChassisConnections();

                    // 延迟触发连接线更新事件，确保机箱控件已经注册
                    Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        ConnectionLinesUpdateRequested?.Invoke(this, EventArgs.Empty);
                    }), System.Windows.Threading.DispatcherPriority.Loaded);

                    // 发布项目修改事件
                    _eventAggregator.GetEvent<ProjectModifiedEvent>().Publish(new ProjectModifiedEventArgs
                    {
                        ModificationType = "Connection",
                        Description = $"添加机箱连接: {sourceChassis.Name} -> {targetChassis.Name}"
                    });
                }
                else
                {
                    ReMessageBox.Show("机箱连接失败", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        /// <summary>
        /// 处理断开连接命令
        /// </summary>
        private void OnDisconnectChassis()
        {
            if (_selectedChassis.Count != 2)
            {
                ReMessageBox.Show("请先选择两个机箱进行断开连接", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var sourceChassis = _selectedChassis[0];
            var targetChassis = _selectedChassis[1];

            // 检查是否已经连接
            if (!_chassisConnectionService.AreChassisConnected(sourceChassis.Id, targetChassis.Id))
            {
                ReMessageBox.Show("这两个机箱没有连接", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = ReMessageBox.Show(
                $"确定要断开机箱 '{sourceChassis.Name}' 和 '{targetChassis.Name}' 之间的连接吗？",
                "确认断开连接",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                // 查找并删除连接
                var connections = _chassisConnectionService.GetConnectionsByChassis(sourceChassis.Id);
                var connectionToRemove = connections.FirstOrDefault(c =>
                    c.ContainsChassis(sourceChassis.Id) && c.ContainsChassis(targetChassis.Id));

                if (connectionToRemove != null)
                {
                    _chassisConnectionService.RemoveConnection(connectionToRemove.Id);
                    ReMessageBox.Show("机箱连接已断开", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                    ClearSelection();

                    // 更新连接集合
                    UpdateChassisConnections();

                    // 触发连接线更新事件
                    ConnectionLinesUpdateRequested?.Invoke(this, EventArgs.Empty);

                    // 发布项目修改事件
                    _eventAggregator.GetEvent<ProjectModifiedEvent>().Publish(new ProjectModifiedEventArgs
                    {
                        ModificationType = "Connection",
                        Description = $"断开机箱连接: {sourceChassis.Name} -> {targetChassis.Name}"
                    });
                }
            }
        }

        /// <summary>
        /// 清除机箱选择（点击空白区域时调用）
        /// </summary>
        private void OnClearChassisSelection()
        {
            ClearSelection();
        }
        
        /// <summary>
        /// 测试连接线绘制
        /// </summary>
        public void TestConnectionLineDrawing()
        {
            ConnectionLinesUpdateRequested?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// 直接连接机箱（Ctrl+点击连接按钮）
        /// </summary>
        private void OnDirectConnect(ConnectionType connectionType)
        {

            if (_selectedChassis.Count != 2)
            {
                ReMessageBox.Show("请按住 Ctrl 键，依次选中两个机箱", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var sourceChassis = _selectedChassis[0];
            var targetChassis = _selectedChassis[1];


            // 检查是否已经连接
            if (_chassisConnectionService.AreChassisConnected(sourceChassis.Id, targetChassis.Id))
            {
                ReMessageBox.Show("这两个机箱已经连接", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var connectionTypeName = connectionType switch
            {
                ConnectionType.Ethernet => "以太网",
                ConnectionType.USB => "USB",
                ConnectionType.Serial => "串口",
                _ => "未知"
            };

            var defaultName = BuildDefaultConnectionName(sourceChassis, targetChassis, connectionType);
            // 自动生成唯一名称（连接方式 + index），避免弹出重命名对话框
            string baseName = BuildDefaultConnectionName(sourceChassis, targetChassis, connectionType);
            int idx = 1;
            string connectionName;
            do
            {
                connectionName = $"{connectionTypeName} {idx}";
                idx++;
            } while (_chassisConnectionService.IsConnectionNameInUse(connectionName));

            var connection = new ChassisConnection(sourceChassis.Id, targetChassis.Id, connectionType)
            {
                ConnectionName = connectionName
            };

            _chassisConnectionService.AddConnection(connection);
            ReMessageBox.Show($"机箱{connectionTypeName}连接成功", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
            ClearSelection();

            // 更新连接集合
            UpdateChassisConnections();

            // 延迟触发连接线更新事件，确保机箱控件已经注册
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                ConnectionLinesUpdateRequested?.Invoke(this, EventArgs.Empty);
            }), System.Windows.Threading.DispatcherPriority.Loaded);

            // 发布项目修改事件
            _eventAggregator.GetEvent<ProjectModifiedEvent>().Publish(new ProjectModifiedEventArgs
            {
                ModificationType = "Connection",
                Description = $"直接添加{connectionTypeName}连接: {sourceChassis.Name} -> {targetChassis.Name}"
            });
        }

        /// <summary>
        /// 处理连接线点击事件
        /// </summary>
        private void OnConnectionLineClick(ChassisConnection connection)
        {
            if (connection == null) return;

            IsDetailsVisible = false;

            var sourceChassis = PxiChassisList.FirstOrDefault(c => c.Id == connection.SourceChassisId);
            var targetChassis = PxiChassisList.FirstOrDefault(c => c.Id == connection.TargetChassisId);
            var interfaceType = GetInterfaceTypeFromConnectionType(connection.ConnectionType.ToString());
            // UI 默认显示占位符速率，实际速率通过 DetectLinkSpeedAsync 检测后更新
            var speed = "----";
            var protocol = GetBusProtocolFromConnectionType(connection.ConnectionType.ToString());

            var connectionDetails = new ConnectionDetails
            {
                ConnectionName = string.IsNullOrWhiteSpace(connection.ConnectionName)
                    ? ChassisConnection.GetConnectionTypeDisplayName(connection.ConnectionType)
                    : connection.ConnectionName,
                SourceObject = sourceChassis?.Name ?? connection.SourceChassisId,
                TargetObject = targetChassis?.Name ?? connection.TargetChassisId,
                InterfaceType = interfaceType,
                Speed = speed,
                BusProtocol = protocol
            };

            SelectedConnection = connectionDetails;
            _activeConnection = connection;

            var actualSpeed = string.IsNullOrWhiteSpace(connection.ActualLinkSpeed) ? speed : connection.ActualLinkSpeed;
            if (string.IsNullOrWhiteSpace(connection.ActualLinkSpeed))
            {
                connection.ActualLinkSpeed = actualSpeed;
            }

            SourceChassisName = sourceChassis?.Name ?? connection.SourceChassisId;
            SourceIpAddress = sourceChassis?.IpAddress ?? string.Empty;
            SourcePort = connection.SourcePort;
            SourceLinkSpeed = actualSpeed;
            TargetChassisName = targetChassis?.Name ?? connection.TargetChassisId;
            TargetIpAddress = targetChassis?.IpAddress ?? string.Empty;
            TargetPort = connection.TargetPort;
            TargetLinkSpeed = actualSpeed;

            UpdateCommunicatingTabelOptions(true, GetCommunicatingTabelOptions(SourceChassisName));
            UpdateCommunicatingTabelOptions(false, GetCommunicatingTabelOptions(TargetChassisName));

            // Debug: 输出 Communicating 表选项与当前项目根，方便定位下拉为空的原因
            try
            {
                var srcTabels = GetCommunicatingTabelOptions(SourceChassisName);
                var tgtTabels = GetCommunicatingTabelOptions(TargetChassisName);
                System.Diagnostics.Debug.WriteLine($"[HardwareConfig] _currentProjectRoot: {_currentProjectRoot?.Name ?? null}");
                System.Diagnostics.Debug.WriteLine($"[HardwareConfig] SourceChassisName='{SourceChassisName}', SourceCommunicatingTabels.Count={SourceCommunicatingTabels.Count}, srcTabels.Count={srcTabels.Count}");
                foreach (var t in srcTabels) System.Diagnostics.Debug.WriteLine($"  src: {t}");
                System.Diagnostics.Debug.WriteLine($"[HardwareConfig] TargetChassisName='{TargetChassisName}', TargetCommunicatingTabels.Count={TargetCommunicatingTabels.Count}, tgtTabels.Count={tgtTabels.Count}");
                foreach (var t in tgtTabels) System.Diagnostics.Debug.WriteLine($"  tgt: {t}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[HardwareConfig] Debug logging failed: {ex.Message}");
            }

            _suppressSourceSelectionChange = true;
            SelectedSourceCommunicatingTabel = string.IsNullOrWhiteSpace(connection.SourceCommunicatingTabel)
                ? SourceCommunicatingTabels.FirstOrDefault()
                : connection.SourceCommunicatingTabel;
            _suppressSourceSelectionChange = false;

            _suppressTargetSelectionChange = true;
            SelectedTargetCommunicatingTabel = string.IsNullOrWhiteSpace(connection.TargetCommunicatingTabel)
                ? TargetCommunicatingTabels.FirstOrDefault()
                : connection.TargetCommunicatingTabel;
            _suppressTargetSelectionChange = false;

            UpdateAssociatedNonCommunicatingTabelOptions(GetNonCommunicatingTabelOptions(SourceChassisName, TargetChassisName));

            _suppressAssociatedSelectionChange = true;
            SelectedAssociatedNonCommunicatingTabel = string.IsNullOrWhiteSpace(connection.AssociatedNonCommunicatingTabel)
                ? AssociatedNonCommunicatingTabels.FirstOrDefault()
                : connection.AssociatedNonCommunicatingTabel;
            _suppressAssociatedSelectionChange = false;

            if (string.IsNullOrWhiteSpace(connection.AssociatedNonCommunicatingTabel) &&
                !string.IsNullOrWhiteSpace(SelectedAssociatedNonCommunicatingTabel))
            {
                _activeConnection.AssociatedNonCommunicatingTabel = SelectedAssociatedNonCommunicatingTabel;
            }

            IsDetailsVisible = false;
            IsConnectionDetailsVisible = true;
            DeviceInfoTitle = connectionDetails.ConnectionName;

            // 异步检测链路速率并更新（留接口以便实现具体检测逻辑）
            _ = Task.Run(async () =>
            {
                try
                {
                    var detected = await DetectLinkSpeedAsync(connection);
                    if (!string.IsNullOrWhiteSpace(detected))
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            connection.ActualLinkSpeed = detected;
                            // 如果当前显示的是该连接，更新 UI 字段
                            if (_activeConnection == connection)
                            {
                                SourceLinkSpeed = detected;
                                TargetLinkSpeed = detected;
                                if (SelectedConnection != null)
                                {
                                    SelectedConnection.Speed = detected;
                                }
                            }
                            PublishConnectionModification($"更新 {SourceChassisName}/{TargetChassisName} 链路速率");
                            ConnectionLinesUpdateRequested?.Invoke(this, EventArgs.Empty);
                        });
                    }
                }
                catch
                {
                    // 忽略检测异常
                }
            });
        }

        private void UpdateConnectionPort(bool isSource, string value)
        {
            if (_activeConnection == null)
            {
                return;
            }

            if (isSource)
            {
                if (_activeConnection.SourcePort == value) return;
                _activeConnection.SourcePort = value;
                PublishConnectionModification($"更新 {SourceChassisName} 端口");
            }
            else
            {
                if (_activeConnection.TargetPort == value) return;
                _activeConnection.TargetPort = value;
                PublishConnectionModification($"更新 {TargetChassisName} 端口");
            }
        }

        private void UpdateConnectionCommunicatingTabel(bool isSource, string value)
        {
            if (_activeConnection == null) return;

            if (isSource)
            {
                if (_suppressSourceSelectionChange || _activeConnection.SourceCommunicatingTabel == value) return;
                _activeConnection.SourceCommunicatingTabel = value;
                PublishConnectionModification($"更新 {SourceChassisName} 通讯变量表");
            }
            else
            {
                if (_suppressTargetSelectionChange || _activeConnection.TargetCommunicatingTabel == value) return;
                _activeConnection.TargetCommunicatingTabel = value;
                PublishConnectionModification($"更新 {TargetChassisName} 通讯变量表");
            }
        }

        /// <summary>
        /// 链路速率检测接口（占位实现）。
        /// </summary>
        /// <param name="connection">要检测的连接</param>
        /// <returns>检测到的速率字符串或 null</returns>
        private async Task<string> DetectLinkSpeedAsync(ChassisConnection connection)
        {
            // TODO: 在此处实现实际的链路速率检测逻辑，当前为占位实现，返回 null 表示未检测到
            await Task.CompletedTask;
            return null;
        }

        private void UpdateAssociatedNonCommunicatingTabel(string value)
        {
            if (_activeConnection == null ||
                _suppressAssociatedSelectionChange ||
                _activeConnection.AssociatedNonCommunicatingTabel == value)
            {
                return;
            }

            _activeConnection.AssociatedNonCommunicatingTabel = value;
            PublishConnectionModification("更新关联表");
        }

        private void PublishConnectionModification(string description)
        {
            _eventAggregator.GetEvent<ProjectModifiedEvent>().Publish(new ProjectModifiedEventArgs
            {
                ModificationType = "Connection",
                Description = description
            });
        }

        private void UpdateCommunicatingTabelOptions(bool isSource, List<string> tabels)
        {
            var targetCollection = isSource ? SourceCommunicatingTabels : TargetCommunicatingTabels;
            targetCollection.Clear();
            foreach (var tabel in tabels)
            {
                targetCollection.Add(tabel);
            }

            if (isSource)
            {
                RaisePropertyChanged(nameof(HasSourceCommunicatingTabels));
                // 如果当前没有选中项，自动选择第一个以便下拉有默认值
                if (SourceCommunicatingTabels.Count > 0 && string.IsNullOrWhiteSpace(SelectedSourceCommunicatingTabel))
                {
                    _suppressSourceSelectionChange = true;
                    SelectedSourceCommunicatingTabel = SourceCommunicatingTabels.FirstOrDefault();
                    _suppressSourceSelectionChange = false;
                }
            }
            else
            {
                RaisePropertyChanged(nameof(HasTargetCommunicatingTabels));
                if (TargetCommunicatingTabels.Count > 0 && string.IsNullOrWhiteSpace(SelectedTargetCommunicatingTabel))
                {
                    _suppressTargetSelectionChange = true;
                    SelectedTargetCommunicatingTabel = TargetCommunicatingTabels.FirstOrDefault();
                    _suppressTargetSelectionChange = false;
                }
            }
        }

        private void UpdateAssociatedNonCommunicatingTabelOptions(List<string> tabels)
        {
            AssociatedNonCommunicatingTabels.Clear();
            foreach (var tabel in tabels)
            {
                AssociatedNonCommunicatingTabels.Add(tabel);
            }

            RaisePropertyChanged(nameof(HasAssociatedNonCommunicatingTabels));
        }

        private List<string> GetCommunicatingTabelOptions(string chassisName)
        {
            var result = new List<string>();
            if (_currentProjectRoot?.Children == null || string.IsNullOrEmpty(chassisName)) return result;

            var chassisNode = _currentProjectRoot.Children
                .FirstOrDefault(c => c.Name == chassisName && c.Type == AppConstants.NodeTypePxiChassis);
            if (chassisNode?.Children == null) return result;

            var taskConfigNode = chassisNode.Children.FirstOrDefault(c => c.Type == AppConstants.NodeTypeTaskConfig);
            if (taskConfigNode?.Children == null) return result;

            foreach (var testTask in taskConfigNode.Children.Where(c => c.Type == AppConstants.NodeTypeTestTask))
            {
                var signalNode = testTask.Children?.FirstOrDefault(c => c.Type == "signal_config");
                if (signalNode?.Children == null) continue;

                foreach (var tabel in signalNode.Children)
                {
                    result.Add($"{testTask.Name}/{tabel.Name}");
                }
            }

            return result;
        }

        private List<string> GetNonCommunicatingTabelOptions(params string[] chassisNames)
        {
            var result = new List<string>();
            if (_currentProjectRoot?.Children == null || chassisNames == null) return result;

            foreach (var chassisName in chassisNames.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct())
            {
                var chassisNode = _currentProjectRoot.Children
                    .FirstOrDefault(c => c.Name == chassisName && c.Type == AppConstants.NodeTypePxiChassis);
                if (chassisNode?.Children == null) continue;

                var taskConfigNode = chassisNode.Children.FirstOrDefault(c => c.Type == AppConstants.NodeTypeTaskConfig);
                if (taskConfigNode?.Children == null) continue;

                foreach (var testTask in taskConfigNode.Children.Where(c => c.Type == AppConstants.NodeTypeTestTask))
                {
                    var signalNode = testTask.Children?.FirstOrDefault(c => c.Type == "signal_config");
                    if (signalNode?.Children == null) continue;

                    foreach (var tabel in signalNode.Children.Where(c => c.Type == "signal_config_tabel"))
                    {
                        result.Add($"{testTask.Name}/{tabel.Name}");
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// 处理连接线断开连接命令
        /// </summary>
        private void OnDisconnectConnectionLine(ChassisConnection connection)
        {
            if (connection == null)
            {
                ReMessageBox.Show("无效的连接", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // 获取源机箱和目标机箱信息
            var sourceChassis = PxiChassisList.FirstOrDefault(c => c.Id == connection.SourceChassisId);
            var targetChassis = PxiChassisList.FirstOrDefault(c => c.Id == connection.TargetChassisId);

            if (sourceChassis == null || targetChassis == null)
            {
                ReMessageBox.Show("无法找到对应的机箱信息", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var connectionTypeName = connection.ConnectionType switch
            {
                ConnectionType.Ethernet => "以太网",
                ConnectionType.USB => "USB",
                ConnectionType.Serial => "串口",
                _ => "未知"
            };

            var connectionDisplayName = string.IsNullOrWhiteSpace(connection.ConnectionName)
                ? connectionTypeName
                : connection.ConnectionName;

            var result = ReMessageBox.Show(
                $"确定要断开 '{sourceChassis.Name}' 和 '{targetChassis.Name}' 之间的链接（{connectionDisplayName}）吗？",
                "确认断开连接",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                // 删除连接
                _chassisConnectionService.RemoveConnection(connection.Id);
                ReMessageBox.Show("连接已断开", "成功", MessageBoxButton.OK, MessageBoxImage.Information);

                // 更新连接集合
                UpdateChassisConnections();

                // 触发连接线更新事件
                ConnectionLinesUpdateRequested?.Invoke(this, EventArgs.Empty);

                // 清除设备详细信息显示（如果当前显示的是连接线信息）
                ClearDeviceDetails();

                // 发布项目修改事件
                _eventAggregator.GetEvent<ProjectModifiedEvent>().Publish(new ProjectModifiedEventArgs
                {
                    ModificationType = "Connection",
                    Description = $"断开连接线: {sourceChassis.Name} -> {targetChassis.Name} ({connectionTypeName})"
                });
            }
        }

        private string GetConnectionTypeDisplayName(string connectionType)
        {
            return connectionType switch
            {
                "Ethernet" => "以太网连接",
                "USB" => "USB连接",
                "Serial" => "串口连接",
                _ => connectionType ?? "连接"
            };
        }

        private string GetConnectionTypeDisplayName(ConnectionType connectionType)
        {
            return GetConnectionTypeDisplayName(connectionType.ToString());
        }

        /// <summary>
        /// 根据连接类型获取接口类型
        /// </summary>
        private string GetInterfaceTypeFromConnectionType(string connectionType)
        {
            return connectionType switch
            {
                "Ethernet" => "RJ45",
                "USB" => "USB-A",
                "Serial" => "RS232",
                _ => "未知"
            };
        }

        /// <summary>
        /// 根据连接类型获取速率
        /// </summary>
        private string GetSpeedFromConnectionType(string connectionType)
        {
            return connectionType switch
            {
                "Ethernet" => "1000 Mbps",
                "USB" => "480 Mbps",
                "Serial" => "115200 bps",
                _ => "未知"
            };
        }

        /// <summary>
        /// 根据连接类型获取总线协议
        /// </summary>
        private string GetBusProtocolFromConnectionType(string connectionType)
        {
            return connectionType switch
            {
                "Ethernet" => "TCP/IP",
                "USB" => "USB 2.0",
                "Serial" => "RS232",
                _ => "未知"
            };
        }

        private string BuildDefaultConnectionName(ChassisModel sourceChassis, ChassisModel targetChassis, ConnectionType connectionType)
        {
            var sourceName = sourceChassis?.Name ?? "机箱1";
            var targetName = targetChassis?.Name ?? "机箱2";
            var typeDisplay = connectionType switch
            {
                ConnectionType.Ethernet => "以太网",
                ConnectionType.USB => "USB",
                ConnectionType.Serial => "串口",
                _ => "连接"
            };
            return $"{sourceName}-{targetName}-{typeDisplay}";
        }

        private string PromptConnectionName(string title, string initialName, string excludeConnectionId = null)
        {
            var defaultName = string.IsNullOrWhiteSpace(initialName) ? "新连接" : initialName.Trim();
            var dialog = new RenameDialog();
            var viewModel = new RenameDialogViewModel
            {
                Title = title,
                OldName = defaultName,
                NewName = defaultName
            };
            viewModel.SetValidateFunc(newName =>
            {
                var trimmed = newName?.Trim();
                if (string.IsNullOrWhiteSpace(trimmed))
                {
                    return false;
                }
                return !_chassisConnectionService.IsConnectionNameInUse(trimmed, excludeConnectionId);
            });
            dialog.DataContext = viewModel;
            return dialog.ShowDialog() == true ? viewModel.NewName?.Trim() : null;
        }

        private void OnRenameConnection(ChassisConnection connection)
        {
            if (connection == null)
            {
                return;
            }

            var updatedName = PromptConnectionName("重命名连接", connection.ConnectionName, connection.Id);
            if (string.IsNullOrWhiteSpace(updatedName) || updatedName == connection.ConnectionName)
            {
                return;
            }

            if (_chassisConnectionService.RenameConnection(connection.Id, updatedName))
            {
                UpdateChassisConnections();
                ConnectionLinesUpdateRequested?.Invoke(this, EventArgs.Empty);
                ReMessageBox.Show("连接名称已更新", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                ReMessageBox.Show("连接名称已存在或更新失败", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void OnNavigateToPxiChassis(string chassisName)
        {
            try
            {
                if (string.IsNullOrEmpty(chassisName))
                {
                    chassisName = "未命名机箱";
                }

                // 发布机箱选择事件，由MainWindowViewModel统一处理导航
                // 这样可以确保无论从哪里触发（设备与网络页面双击、项目树双击、导航栏点击）都有一致的行为
                var chassis = _pxiChassisService.GetChassisByName(chassisName);
                if (chassis != null)
                {
                    _eventAggregator.GetEvent<PxiChassisSelectedEvent>().Publish(new PxiChassisSelectedEventArgs
                    {
                        ChassisName = chassisName,
                        ChassisId = chassis.Id
                    });
                }
            }
            catch (Exception ex)
            {
                ReMessageBox.Show($"导航失败：{ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #region INavigationAware Implementation
        public void OnNavigatedTo(NavigationContext navigationContext)
        {
            // 缓存导航日志用于关闭时回退
            _journal = navigationContext?.NavigationService?.Journal;
            EnsureFixedDemoChassis();
            // 页面导航到时，确保数据是最新的
            RaisePropertyChanged(nameof(PxiChassisList));
            
            // 从服务加载连接数据
            var connections = _chassisConnectionService.GetAllConnections();
            if (connections.Count > 0)
            {
                UpdateChassisConnections();
            }
            
            RaisePropertyChanged(nameof(ChassisConnections));

            // 注意：不需要手动添加导航按钮，NavigationService已经自动处理
            
            // 通知View刷新机箱控件，确保JSON导入后的机箱能正确显示
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                ChassisControlsRefreshRequested?.Invoke();

                // 多次延迟触发连接线更新，确保机箱控件已经完全创建和注册
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    ConnectionLinesUpdateRequested?.Invoke(this, EventArgs.Empty);
                    
                    // 第二次延迟，确保机箱控件完全注册
                    Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        ConnectionLinesUpdateRequested?.Invoke(this, EventArgs.Empty);
                        
                        // 第三次延迟，确保连接线能够正确绘制
                        Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            ConnectionLinesUpdateRequested?.Invoke(this, EventArgs.Empty);
                        }), System.Windows.Threading.DispatcherPriority.ContextIdle);
                    }), System.Windows.Threading.DispatcherPriority.Loaded);
                }), System.Windows.Threading.DispatcherPriority.Loaded);
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        public bool IsNavigationTarget(NavigationContext navigationContext) => false;

        public void OnNavigatedFrom(NavigationContext navigationContext)
        {
            // 页面离开时可以进行清理工作
        }
        #endregion


        #region IDisposable Implementation
        public void Dispose()
        {
            // 取消事件订阅，避免内存泄漏
            _eventAggregator?.GetEvent<DeletePxiChassisEvent>()?.Unsubscribe(OnPxiChassisDeleted);
            _eventAggregator?.GetEvent<RenamePxiChassisEvent>()?.Unsubscribe(OnPxiChassisRenamed);
            _eventAggregator?.GetEvent<AddPxiChassisEvent>()?.Unsubscribe(OnPxiChassisAdded);

            // 取消机箱连接变化事件订阅
            if (ChassisConnections != null)
            {
                ChassisConnections.CollectionChanged -= OnChassisConnectionsChanged;
            }

            // 取消连接数据请求事件订阅
            _eventAggregator?.GetEvent<ChassisConnectionsRequestEvent>()?.Unsubscribe(OnChassisConnectionsRequested);

            // 取消连接数据加载事件订阅
            _eventAggregator?.GetEvent<ChassisConnectionsLoadEvent>()?.Unsubscribe(OnChassisConnectionsLoaded);

            // 取消连接线请求事件订阅
            _eventAggregator?.GetEvent<ConnectionLinesRequestEvent>()?.Unsubscribe(OnConnectionLinesRequest);

            // 取消连接线加载事件订阅
            _eventAggregator?.GetEvent<ConnectionLinesLoadEvent>()?.Unsubscribe(OnConnectionLinesLoad);

            // 取消机箱选择事件订阅
            _eventAggregator?.GetEvent<PxiChassisSelectedEvent>()?.Unsubscribe(OnPxiChassisSelected);

            // 取消清除设备详细信息事件订阅
            _eventAggregator?.GetEvent<ClearDeviceDetailsEvent>()?.Unsubscribe(OnClearDeviceDetails);
            
            // 取消设备点击事件订阅
            _eventAggregator?.GetEvent<DeviceClickedEvent>()?.Unsubscribe(OnDeviceClicked);

            _eventAggregator?.GetEvent<ProjectOpenedEvent>()?.Unsubscribe(OnProjectOpened);
            _eventAggregator?.GetEvent<ProjectCreatedEvent>()?.Unsubscribe(OnProjectOpened);
            _eventAggregator?.GetEvent<ProjectClosedEvent>()?.Unsubscribe(OnProjectClosed);

            // 取消测试任务创建事件订阅
            _eventAggregator?.GetEvent<TestTaskCreatedEvent>()?.Unsubscribe(OnTestTaskCreated);
        }

        /// <summary>
        /// 处理项目加载事件
        /// </summary>
        private void OnProjectOpened(ProjectItem project)
        {
            _currentProjectRoot = project;
            _activeConnection = null;
            SourceCommunicatingTabels.Clear();
            TargetCommunicatingTabels.Clear();
            ClearDeviceDetails();
            EnsureFixedDemoChassis();
        }

        /// <summary>
        /// 处理测试任务创建事件，更新通信变量表选项
        /// </summary>
        private void OnTestTaskCreated(ProjectItem testTask)
        {
            // 当有新的测试任务创建时，刷新通信变量表选项
            if (!string.IsNullOrEmpty(SourceChassisName))
            {
                UpdateCommunicatingTabelOptions(true, GetCommunicatingTabelOptions(SourceChassisName));
            }
            if (!string.IsNullOrEmpty(TargetChassisName))
            {
                UpdateCommunicatingTabelOptions(false, GetCommunicatingTabelOptions(TargetChassisName));
            }
        }

        /// <summary>
        /// 处理项目关闭事件
        /// </summary>
        private void OnProjectClosed()
        {
            ResourceCleanupHelper.TryCleanup(() =>
            {
                _currentProjectRoot = null;
                _activeConnection = null;
                SourceCommunicatingTabels.Clear();
                TargetCommunicatingTabels.Clear();
                AssociatedNonCommunicatingTabels.Clear();

                // 清理机箱连接数据
                ResourceCleanupHelper.CleanupCollection(ChassisConnections);
                
                // 清理连接线数据
                ConnectionLines.Clear();
                
                // 清理选中的机箱
                _selectedChassis.Clear();
                
                // 清理选中的设备
                SelectedDevice = null;
                
                // 隐藏详细信息面板
                IsDetailsVisible = false;
                IsConnectionDetailsVisible = false;
                
                // 重置设备属性字段
                DeviceField1 = "";
                DeviceField2 = "";
                DeviceField3 = "";
                DeviceField4 = "";
                DeviceField5 = "";
                DeviceField6 = "";
                
                // 重置连接详细信息
                SelectedConnection = null;
                
                // 重置设备信息标题
                DeviceInfoTitle = AppConstants.DeviceInfoDefaultTitle;
                
            }, "HardwareConfigViewModel项目关闭清理");
        }
        #endregion

        /// <summary>
        /// 关闭在区域中的视图
        /// </summary>
        private void OnCloseInRegion()
        {
            var result = ReMessageBox.Show("确定要关闭硬件配置吗？", "确认", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                // 传递页面类型名称，避免误关其他实例
                _eventAggregator.GetEvent<ReleaseCurrentPageEvent>().Publish("HardwareConfig");
            }
        }
    }
}
