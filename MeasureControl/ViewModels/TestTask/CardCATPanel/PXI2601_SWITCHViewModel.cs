using MeasureControl.Drivers;
using MeasureControl.Drivers.ArtSwitch;
using MeasureControl.Events;
using MeasureControl.Models;
using MeasureControl.Models.Devices;
using MeasureControl.Services;
using MeasureControl.Views;
using MeasureControl.Views.Dialogs;
using MeasureControl.Helpers;
using Prism.Commands;
using Prism.Events;
using Prism.Mvvm;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SwitchConnectionState = MeasureControl.Models.SwitchConnectionState;
using SwitchMatrixCardConfig = MeasureControl.Models.SwitchMatrixCardConfig;
using static MeasureControl.ViewModels.TestTask.CardCATPanel.PXI2601_SWITCHViewModel;
using System.IO;

namespace MeasureControl.ViewModels.TestTask.CardCATPanel
{
    #region 矩阵拓扑相关类

    /// <summary>
    /// 标签ViewModel（用于显示节点标签）
    /// </summary>
    public class LabelViewModel : BindableBase
    {
        private string _label;
        private double _x;
        private double _y;
        private double _fontSize = 12;
        private string _fontWeight = "Normal";

        public string Label
        {
            get => _label;
            set => SetProperty(ref _label, value);
        }

        public double X
        {
            get => _x;
            set => SetProperty(ref _x, value);
        }

        public double Y
        {
            get => _y;
            set => SetProperty(ref _y, value);
        }

        public double FontSize
        {
            get => _fontSize;
            set => SetProperty(ref _fontSize, value);
        }

        public string FontWeight
        {
            get => _fontWeight;
            set => SetProperty(ref _fontWeight, value);
        }

        public LabelViewModel(string label, double x, double y)
        {
            Label = label;
            X = x;
            Y = y;
        }

        public LabelViewModel(string label, double x, double y, double fontSize, string fontWeight)
        {
            Label = label;
            X = x;
            Y = y;
            FontSize = fontSize;
            FontWeight = fontWeight;
        }
    }

    /// <summary>
    /// 矩阵节点ViewModel
    /// </summary>
    public class MatrixNodeViewModel : BindableBase
    {
        private string _nodeId;
        private string _nodeType;
        private double _x;
        private double _y;
        private double _displayX;
        private double _displayY;
        private bool _isConnected;
        private bool _isSelected;
        private string _nodeColor;
        private double _radius = 10;
        private bool _isHovered;
        private string _selectionColor;
        private bool _isFirstStepSelected;
        private bool _isSecondStepSelected;
        private int _nodeIndex;
        private string _nodeName;
        private string _highlightColor;

        public string NodeId
        {
            get => _nodeId;
            set => SetProperty(ref _nodeId, value);
        }

        public string NodeType
        {
            get => _nodeType;
            set => SetProperty(ref _nodeType, value);
        }

        public double X
        {
            get => _x;
            set => SetProperty(ref _x, value);
        }

        public double Y
        {
            get => _y;
            set => SetProperty(ref _y, value);
        }

        public double DisplayX
        {
            get => _displayX;
            set => SetProperty(ref _displayX, value);
        }

        public double DisplayY
        {
            get => _displayY;
            set => SetProperty(ref _displayY, value);
        }

        public bool IsConnected
        {
            get => _isConnected;
            set => SetProperty(ref _isConnected, value);
        }

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        public string NodeColor
        {
            get => _nodeColor;
            set => SetProperty(ref _nodeColor, value);
        }

        public double Radius
        {
            get => _radius;
            set => SetProperty(ref _radius, value);
        }

        public int NodeIndex
        {
            get => _nodeIndex;
            set => SetProperty(ref _nodeIndex, value);
        }

        public string NodeName
        {
            get => _nodeName;
            set => SetProperty(ref _nodeName, value);
        }

        public string HighlightColor
        {
            get => _highlightColor;
            set => SetProperty(ref _highlightColor, value);
        }

        public string SelectionColor
        {
            get => _selectionColor;
            set => SetProperty(ref _selectionColor, value);
        }

        public bool IsFirstStepSelected
        {
            get => _isFirstStepSelected;
            set => SetProperty(ref _isFirstStepSelected, value);
        }

        public bool IsSecondStepSelected
        {
            get => _isSecondStepSelected;
            set => SetProperty(ref _isSecondStepSelected, value);
        }

        public string ToolTipText
        {
            get
            {
                var tip = $"节点: {NodeId}\n类型: {NodeType}\n状态: {(IsConnected ? "已连接" : "未连接")}";
                if (!string.IsNullOrEmpty(NodeName))
                    tip += $"\n名称: {NodeName}";
                return tip;
            }
        }

        public bool IsHovered
        {
            get => _isHovered;
            set => SetProperty(ref _isHovered, value);
        }

        public MatrixNodeViewModel(string nodeId, string nodeType, double x, double y)
        {
            NodeId = nodeId;
            NodeType = nodeType;
            X = x;
            Y = y;

            if (nodeType == "Input" && nodeId.StartsWith("r") && int.TryParse(nodeId.Substring(1), out int inputIndex))
            {
                NodeIndex = inputIndex;
                NodeName = $"行通道 {inputIndex + 1}";
            }
            else if (nodeType == "Output" && nodeId.StartsWith("c") && int.TryParse(nodeId.Substring(1), out int outputIndex))
            {
                NodeIndex = outputIndex;
                NodeName = $"列通道 {outputIndex + 1}";
            }

            NodeColor = nodeType == "Input" ? "#2196F3" : "#F44336";
            HighlightColor = nodeType == "Input" ? "#1565C0" : "#C62828";
            SelectionColor = "#FF9800";

            DisplayX = x;
            DisplayY = y;
        }
    }

    /// <summary>
    /// 交叉点ViewModel
    /// </summary>
    public class CrossPointViewModel : BindableBase
    {
        private string _crossPointId;
        private string _inputNodeId;
        private string _outputNodeId;
        private double _x;
        private double _y;
        private string _displayName;
        private bool _isConnected;
        private string _connectionColor;
        private double _size = 16;
        private bool _isSelected;
        private bool _isPendingConnection;

        public string CrossPointId
        {
            get => _crossPointId;
            set => SetProperty(ref _crossPointId, value);
        }

        public string InputNodeId
        {
            get => _inputNodeId;
            set => SetProperty(ref _inputNodeId, value);
        }

        public string OutputNodeId
        {
            get => _outputNodeId;
            set => SetProperty(ref _outputNodeId, value);
        }

        public double X
        {
            get => _x;
            set => SetProperty(ref _x, value);
        }

        public double Y
        {
            get => _y;
            set => SetProperty(ref _y, value);
        }

        public string DisplayName
        {
            get => _displayName;
            set => SetProperty(ref _displayName, value);
        }

        public bool IsConnected
        {
            get => _isConnected;
            set => SetProperty(ref _isConnected, value);
        }

        public string ConnectionColor
        {
            get => _connectionColor;
            set
            {
                if (SetProperty(ref _connectionColor, value))
                {
                    RaisePropertyChanged(nameof(ConnectionBrush));
                }
            }
        }

        private static readonly BrushConverter ConnectionBrushConverter = new BrushConverter();

        public Brush ConnectionBrush
        {
            get
            {
                if (string.IsNullOrWhiteSpace(ConnectionColor)) return Brushes.White;
                try
                {
                    return (Brush)ConnectionBrushConverter.ConvertFromString(ConnectionColor);
                }
                catch
                {
                    return Brushes.White;
                }
            }
        }

        public double Size
        {
            get => _size;
            set => SetProperty(ref _size, value);
        }

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        public bool IsPendingConnection
        {
            get => _isPendingConnection;
            set => SetProperty(ref _isPendingConnection, value);
        }

        public string ToolTipText
        {
            get
            {
                var tip = $"{DisplayName}\n" +
                       $"输入: {InputNodeId}, 输出: {OutputNodeId}\n" +
                       $"状态: {(IsConnected ? "已连接" : "未连接")}";

                if (IsPendingConnection)
                    tip += "\n待确认连接...";

                return tip;
            }
        }

        public CrossPointViewModel(string crossPointId, string inputNodeId, string outputNodeId,
                             double x, double y, string displayName)
        {
            CrossPointId = crossPointId;
            InputNodeId = inputNodeId;
            OutputNodeId = outputNodeId;
            X = x;
            Y = y;
            DisplayName = displayName;

            if (outputNodeId.StartsWith("c") && outputNodeId.Length > 1)
            {
                if (int.TryParse(outputNodeId.Substring(1), out int outputNum))
                {
                    if (outputNum >= 32)
                        Size = 12;
                    else if (outputNum >= 16)
                        Size = 14;
                }
            }
        }
    }

    /// <summary>
    /// 连接线ViewModel
    /// </summary>
    public class LineViewModel : BindableBase
    {
        private double _startX;
        private double _startY;
        private double _endX;
        private double _endY;
        private string _lineType;
        private string _strokeColor = "#CCCCCC";
        private double _strokeThickness = 2;
        private string _strokeDashArray;

        public double StartX
        {
            get => _startX;
            set => SetProperty(ref _startX, value);
        }

        public double StartY
        {
            get => _startY;
            set => SetProperty(ref _startY, value);
        }

        public double EndX
        {
            get => _endX;
            set => SetProperty(ref _endX, value);
        }

        public double EndY
        {
            get => _endY;
            set => SetProperty(ref _endY, value);
        }

        public string LineType
        {
            get => _lineType;
            set => SetProperty(ref _lineType, value);
        }

        public string StrokeColor
        {
            get => _strokeColor;
            set => SetProperty(ref _strokeColor, value);
        }

        public double StrokeThickness
        {
            get => _strokeThickness;
            set => SetProperty(ref _strokeThickness, value);
        }

        public string StrokeDashArray
        {
            get => _strokeDashArray;
            set => SetProperty(ref _strokeDashArray, value);
        }

        public LineViewModel(double startX, double startY, double endX, double endY, string lineType)
        {
            StartX = startX;
            StartY = startY;
            EndX = endX;
            EndY = endY;
            LineType = lineType;

            if (lineType == "Extension")
            {
                StrokeColor = lineType == "Vertical" || lineType == "Horizontal" ? "#CCCCCC" : "#888888";
                StrokeThickness = 1.5;
                StrokeDashArray = "5,2";
            }
            else
            {
                StrokeColor = "#CCCCCC";
                StrokeThickness = 2;
            }
        }

        public LineViewModel(double startX, double startY, double endX, double endY,
                            string lineType, string strokeColor, double strokeThickness, string strokeDashArray = null)
        {
            StartX = startX;
            StartY = startY;
            EndX = endX;
            EndY = endY;
            LineType = lineType;
            StrokeColor = strokeColor;
            StrokeThickness = strokeThickness;
            StrokeDashArray = strokeDashArray;
        }
    }

    /// <summary>
    /// 活跃连接ViewModel（矩阵拓扑用）
    /// </summary>
    public class MatrixConnectionViewModel : BindableBase
    {
        private string _inputNodeId;
        private string _outputNodeId;
        private double _inputX;
        private double _inputY;
        private double _outputX;
        private double _outputY;
        private string _connectionColor;
        private bool _isActive;

        public string InputNodeId
        {
            get => _inputNodeId;
            set => SetProperty(ref _inputNodeId, value);
        }

        public string OutputNodeId
        {
            get => _outputNodeId;
            set => SetProperty(ref _outputNodeId, value);
        }

        public double InputX
        {
            get => _inputX;
            set => SetProperty(ref _inputX, value);
        }

        public double InputY
        {
            get => _inputY;
            set => SetProperty(ref _inputY, value);
        }

        public double OutputX
        {
            get => _outputX;
            set => SetProperty(ref _outputX, value);
        }

        public double OutputY
        {
            get => _outputY;
            set => SetProperty(ref _outputY, value);
        }

        public string ConnectionColor
        {
            get => _connectionColor;
            set => SetProperty(ref _connectionColor, value);
        }

        public bool IsActive
        {
            get => _isActive;
            set => SetProperty(ref _isActive, value);
        }

        public string ToolTipText
        {
            get
            {
                return $"{InputNodeId} → {OutputNodeId}\n" +
                       $"状态: {(IsActive ? "已连接" : "未连接")}";
            }
        }

        public MatrixConnectionViewModel(string inputNodeId, string outputNodeId,
                                        double inputX, double inputY, double outputX, double outputY,
                                        bool isActive)
        {
            InputNodeId = inputNodeId;
            OutputNodeId = outputNodeId;
            InputX = inputX;
            InputY = inputY;
            OutputX = outputX;
            OutputY = outputY;
            IsActive = isActive;

            ConnectionColor = AdvancedColorGenerator.GenerateColorForConnection(inputNodeId, outputNodeId);
        }
    }

    #endregion

    /// <summary>
    /// 继电器状态信息ViewModel
    /// </summary>
    public class RelayStatusInfo : BindableBase
    {
        private MatrixConnection _connection;
        private string _displayText;
        private string _stateColor;
        private bool _isOpened;
        private int _connectionCount;

        public MatrixConnection Connection
        {
            get => _connection;
            set
            {
                if (SetProperty(ref _connection, value))
                {
                    UpdateDisplay();
                    RaisePropertyChanged(nameof(ConnectionCount));
                }
            }
        }

        public string RelayName => Connection?.RelayName;

        public bool IsOpened
        {
            get => _isOpened;
            set => SetProperty(ref _isOpened, value);
        }

        public string ConnectedInput => Connection?.InputChannel;
        public string ConnectedOutput => Connection?.OutputChannel;

        public int ConnectionCount
        {
            get => _connectionCount;
            set
            {
                if (SetProperty(ref _connectionCount, value))
                {
                    // 连接计数变化时通知 UI
                    RaisePropertyChanged();
                }
            }
        }

        public string DisplayText
        {
            get => _displayText;
            private set => SetProperty(ref _displayText, value);
        }

        public string StateColor
        {
            get => _stateColor;
            private set => SetProperty(ref _stateColor, value);
        }

        public string ToolTipText => GetToolTipText();

        public RelayStatusInfo(MatrixConnection connection)
        {
            Connection = connection ?? throw new ArgumentNullException(nameof(connection));
            UpdateDisplay();
        }

        private void UpdateDisplay()
        {
            if (Connection == null)
            {
                IsOpened = false;
                DisplayText = "✖";
                StateColor = "#9E9E9E";
                ConnectionCount = 0;
                return;
            }

            IsOpened = Connection.State == SwitchConnectionState.Connected;
            DisplayText = IsOpened ? "Opened" : "✖";
            StateColor = Connection.StateColor ?? "#9E9E9E";

            // 确保连接计数正确
            ConnectionCount = Connection.ConnectionCount;

            Debug.WriteLine($"[RelayStatusInfo.UpdateDisplay] {RelayName}: " +
                           $"IsOpened={IsOpened}, ConnectionCount={ConnectionCount}, " +
                           $"Connection.State={Connection.State}");
        }

        private string GetToolTipText()
        {
            if (Connection == null) return string.Empty;

            return $"继电器: {RelayName}\n" +
                   $"连接: {ConnectedInput} → {ConnectedOutput}\n" +
                   $"状态: {(IsOpened ? "已打开" : "已关闭")}\n" +
                   $"打开次数: {ConnectionCount}\n" +
                   $"最后操作: {Connection.LastDisconnectedTime:yyyy-MM-dd HH:mm:ss}";
        }

        public void UpdateFromConnection()
        {
            if (Connection == null)
            {
                IsOpened = false;
                DisplayText = "✖";
                StateColor = "#9E9E9E";
                ConnectionCount = 0;
            }
            else
            {
                IsOpened = Connection.State == SwitchConnectionState.Connected;
                DisplayText = IsOpened ? "Opened" : "✖";
                StateColor = Connection.StateColor ?? "#9E9E9E";
                ConnectionCount = Connection.ConnectionCount;
            }

            // 通知所有属性变更
            RaisePropertyChanged(nameof(IsOpened));
            RaisePropertyChanged(nameof(DisplayText));
            RaisePropertyChanged(nameof(StateColor));
            RaisePropertyChanged(nameof(ConnectionCount));
            RaisePropertyChanged(nameof(ToolTipText));
            RaisePropertyChanged(nameof(ConnectedInput));
            RaisePropertyChanged(nameof(ConnectedOutput));
            RaisePropertyChanged(nameof(RelayName));
        }
    }

    /// <summary>
    /// 拓扑结构配置
    /// </summary>
    public class TopologyConfig
    {
        public string Name { get; set; }
        public string TopologyString { get; set; }
        public int InputCount { get; set; }
        public int OutputCount { get; set; }
        public string Description { get; set; }

        public string DisplayInfo => $"{Name} ";
    }

    /// <summary>
    /// 拓扑节点信息
    /// </summary>
    public class TopologyNodeInfo : BindableBase
    {
        private string _nodeId;
        private string _nodeType;
        private double _x;
        private double _y;
        private bool _isConnected;
        private string _connectedTo;
        private string _displayColor;
        private bool _isSelected;
        private bool _isHovered;

        public string NodeId
        {
            get => _nodeId;
            set => SetProperty(ref _nodeId, value);
        }

        public string NodeType
        {
            get => _nodeType;
            set => SetProperty(ref _nodeType, value);
        }

        public double X
        {
            get => _x;
            set => SetProperty(ref _x, value);
        }

        public double Y
        {
            get => _y;
            set => SetProperty(ref _y, value);
        }

        public bool IsConnected
        {
            get => _isConnected;
            set => SetProperty(ref _isConnected, value);
        }

        public string ConnectedTo
        {
            get => _connectedTo;
            set => SetProperty(ref _connectedTo, value);
        }

        public string DisplayColor
        {
            get => _displayColor;
            set => SetProperty(ref _displayColor, value);
        }

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        public bool IsHovered
        {
            get => _isHovered;
            set => SetProperty(ref _isHovered, value);
        }

        public double Radius => 20;

        public string ToolTipText
        {
            get
            {
                var tip = $"节点: {NodeId}\n类型: {NodeType}\n状态: {(IsConnected ? "已连接" : "未连接")}";
                if (!string.IsNullOrEmpty(ConnectedTo))
                    tip += $"\n连接到: {ConnectedTo}";
                return tip;
            }
        }

        public TopologyNodeInfo(string nodeId, string nodeType, double x, double y)
        {
            NodeId = nodeId;
            NodeType = nodeType;
            X = x;
            Y = y;
            DisplayColor = nodeType == "Input" ? "#2196F3" : "#F44336";
        }
    }

    /// <summary>
    /// 拓扑连接线信息
    /// </summary>
    public class TopologyConnectionInfo : BindableBase
    {
        private string _inputNodeId;
        private string _outputNodeId;
        private double _inputX;
        private double _inputY;
        private double _outputX;
        private double _outputY;
        private bool _isActive;
        private int _connectionCount;
        private string _lineColor;
        private double _strokeThickness = 2.0;

        public string InputNodeId
        {
            get => _inputNodeId;
            set => SetProperty(ref _inputNodeId, value);
        }

        public string OutputNodeId
        {
            get => _outputNodeId;
            set => SetProperty(ref _outputNodeId, value);
        }

        public double InputX
        {
            get => _inputX;
            set => SetProperty(ref _inputX, value);
        }

        public double InputY
        {
            get => _inputY;
            set => SetProperty(ref _inputY, value);
        }

        public double OutputX
        {
            get => _outputX;
            set => SetProperty(ref _outputX, value);
        }

        public double OutputY
        {
            get => _outputY;
            set => SetProperty(ref _outputY, value);
        }

        public bool IsActive
        {
            get => _isActive;
            set
            {
                if (SetProperty(ref _isActive, value))
                {
                    UpdateDisplayColor();
                }
            }
        }

        public int ConnectionCount
        {
            get => _connectionCount;
            set => SetProperty(ref _connectionCount, value);
        }

        public string LineColor
        {
            get => _lineColor;
            set => SetProperty(ref _lineColor, value);
        }

        public double StrokeThickness
        {
            get => _strokeThickness;
            set => SetProperty(ref _strokeThickness, value);
        }

        public int ConnectionIndex { get; set; }

        public string ToolTipText
        {
            get
            {
                return $"{InputNodeId} → {OutputNodeId}\n" +
                       $"状态: {(IsActive ? "已连接" : "未连接")}\n" +
                       $"连接次数: {ConnectionCount}";
            }
        }

        public TopologyConnectionInfo(string inputNodeId, string outputNodeId,
                                     double inputX, double inputY, double outputX, double outputY,
                                     bool isActive, int connectionCount, int connectionIndex = -1)
        {
            InputNodeId = inputNodeId;
            OutputNodeId = outputNodeId;
            InputX = inputX;
            InputY = inputY;
            OutputX = outputX;
            OutputY = outputY;
            IsActive = isActive;
            ConnectionCount = connectionCount;
            ConnectionIndex = connectionIndex;

            string autoColor = AdvancedColorGenerator.GenerateColorForConnection(inputNodeId, outputNodeId, connectionIndex);
            _lineColor = autoColor;

            UpdateDisplayColor();
        }

        private void UpdateDisplayColor()
        {
            if (!IsActive)
            {
                LineColor = "#CCCCCC";
            }
            else
            {
                LineColor = _lineColor;
            }
        }
    }

    /// <summary>
    /// 开关矩阵控制面板ViewModel
    /// </summary>
    public class PXI2601_SWITCHViewModel : BindableBase, IDisposable
    {
        #region Private Fields

        private DeviceBase _device;
        private string _chassisName;
        private string _cardModel;
        private string _cardName;
        private string _connectionStatus;
        private bool _isDeviceConnected;
        private bool _isDeviceOpened;
        private string _selectedTopology;
        private int _totalRelayCount;
        private int _activeRelayCount;
        private int _errorConnectionCount;
        private double _canvasWidth = 2000;
        private double _canvasHeight = 600;
        private double _availableWidth = 0;
        private double _availableHeight = 0;

        // 拓扑视图交互状态
        private TopologyNodeInfo _firstSelectedNode;
        private TopologyNodeInfo _secondSelectedNode;
        private TopologyNodeInfo _hoveredNode;

        // 矩阵拓扑交互状态
        private MatrixNodeViewModel _selectedInputNode;
        private MatrixNodeViewModel _selectedOutputNode;
        private CrossPointViewModel _selectedCrossPoint;
        private CrossPointViewModel _pendingCrossPoint;

        // 视图集合
        private ObservableCollection<string> _inputChannels;
        private ObservableCollection<string> _outputChannels;
        private ObservableCollection<RelayStatusInfo> _relayStatusList;
        private ObservableCollection<TopologyConfig> _topologyConfigs;
        private ObservableCollection<MatrixConnection> _activeConnections;
        private ObservableCollection<TopologyNodeInfo> _topologyNodes;
        private ObservableCollection<TopologyConnectionInfo> _topologyConnections;
        private ObservableCollection<MatrixNodeViewModel> _matrixNodes;

        // 矩阵拓扑相关字段
        private ObservableCollection<MatrixNodeViewModel> _outputNodes;
        private ObservableCollection<MatrixNodeViewModel> _inputNodes;
        private ObservableCollection<CrossPointViewModel> _crossPoints;
        private ObservableCollection<LineViewModel> _verticalLines;
        private ObservableCollection<LineViewModel> _horizontalLines;
        private ObservableCollection<MatrixConnectionViewModel> _matrixConnections;
        private ObservableCollection<LabelViewModel> _inputLabels;
        private ObservableCollection<LabelViewModel> _outputLabels;

        // 配置
        private SwitchMatrixCardConfig _cardConfig;

        private readonly IPxiChassisService _pxiChassisService;
        private readonly IEventAggregator _eventAggregator;
        private DispatcherTimer _statusTimer;
        private ArtSwitchDriver _driver;

        private Dispatcher _dispatcher;

        //拓扑统计属性
        private int _currentInputCount = 4;
        private int _currentOutputCount = 32;
        private int _currentCrossPointCount = 128;
        private string _inputLineText = "输入线 (4条)";
        private string _outputLineText = "输出线 (32条)";

        public bool KeepMatrixConnectionOnClose { get; set; }

        private const int TcpBasePort2601 = 50200;
        private const string LocalChassisIpAddress = "192.168.1.3";
        private const string RemoteClientIpAddress = "192.168.1.2";
        private const byte RemoteCommandConnect = 0;
        private const byte RemoteCommandDisconnect = 1;

        private TcpClient _tcpClient;
        private NetworkStream _tcpStream;
        private DateTime _tcpLastActivityTime = DateTime.MinValue;
        private readonly TimeSpan _tcpInactivityTimeout = TimeSpan.FromSeconds(60);

        private readonly HashSet<string> _ownedTcpServerIdentifiers = new HashSet<string>();

        private readonly SemaphoreSlim _remoteCommandLock = new SemaphoreSlim(1, 1);
        private int _isProcessingRemoteCommand;

        private readonly SemaphoreSlim _driverInitLock = new SemaphoreSlim(1, 1);
        private DateTime _lastDriverInitAttemptUtc = DateTime.MinValue;
        private int _driverInitFailureCount;
        private static readonly TimeSpan DriverInitBaseBackoff = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan DriverInitMaxBackoff = TimeSpan.FromSeconds(30);

        #endregion

        #region Properties

        public DeviceBase Device
        {
            get => _device;
            set
            {
                if (SetProperty(ref _device, value))
                {
                    // 当设备改变时，根据槽位自动设置拓扑参数
                    UpdateTopologyFromDevice();
                    // 通知拓扑显示文本更新
                    RaisePropertyChanged(nameof(CurrentTopologyText));
                }
            }
        }

        public string ChassisName
        {
            get => _chassisName;
            set => SetProperty(ref _chassisName, value);
        }

        public string CardModel
        {
            get => _cardModel;
            set => SetProperty(ref _cardModel, value);
        }

        public string CardName
        {
            get => _cardName;
            set => SetProperty(ref _cardName, value);
        }

        public string ConnectionStatus
        {
            get => _connectionStatus;
            set => SetProperty(ref _connectionStatus, value);
        }

        public bool IsDeviceConnected
        {
            get => _isDeviceConnected;
            set => SetProperty(ref _isDeviceConnected, value);
        }

        public bool IsDeviceOpened
        {
            get => _isDeviceOpened;
            set => SetProperty(ref _isDeviceOpened, value);
        }

        public string SelectedTopology
        {
            get => _selectedTopology;
            set
            {
                if (SetProperty(ref _selectedTopology, value))
                {
                    OnTopologyChanged();
                }
            }
        }

        public int TotalRelayCount
        {
            get => _totalRelayCount;
            set => SetProperty(ref _totalRelayCount, value);
        }

        public int ActiveRelayCount
        {
            get => _activeRelayCount;
            set => SetProperty(ref _activeRelayCount, value);
        }

        public int ErrorConnectionCount
        {
            get => _errorConnectionCount;
            set => SetProperty(ref _errorConnectionCount, value);
        }

        /// <summary>
        /// 连接按钮文本
        /// </summary>
        public string ConnectButtonText
        {
            get => IsDeviceOpened ? "关闭板卡" : "打开板卡";
        }


        /// <summary>
        /// 所有继电器的总连接次数
        /// </summary>
        public int TotalConnectionCount
        {
            get
            {
                if (_cardConfig == null)
                    return 0;

                int total = 0;
                foreach (var connection in _cardConfig.ConnectionMap.Values)
                {
                    total += connection.ConnectionCount;
                }
                return total;
            }
        }

        public double CanvasWidth
        {
            get => _canvasWidth;
            set => SetProperty(ref _canvasWidth, value);
        }

        public double CanvasHeight
        {
            get => _canvasHeight;
            set => SetProperty(ref _canvasHeight, value);
        }


        public double AvailableWidth
        {
            get => _availableWidth;
            set => SetProperty(ref _availableWidth, value);
        }

        public double AvailableHeight
        {
            get => _availableHeight;
            set => SetProperty(ref _availableHeight, value);
        }
        public int CurrentInputCount
        {
            get => _currentInputCount;
            set => SetProperty(ref _currentInputCount, value);
        }

        public int CurrentOutputCount
        {
            get => _currentOutputCount;
            set => SetProperty(ref _currentOutputCount, value);
        }

        public int CurrentCrossPointCount
        {
            get => _currentCrossPointCount;
            set => SetProperty(ref _currentCrossPointCount, value);
        }

        public string InputLineText
        {
            get => _inputLineText;
            set => SetProperty(ref _inputLineText, value);
        }

        public string OutputLineText
        {
            get => _outputLineText;
            set => SetProperty(ref _outputLineText, value);
        }

        /// <summary>
        /// 根据设备槽位更新拓扑参数
        /// </summary>
        private void UpdateTopologyFromDevice()
        {
            if (Device is SwitchDevice switchDevice)
            {
                int slotIndex = switchDevice.SlotIndex;
                switch (slotIndex)
                {
                    case 4:
                        // 8x16 Matrix
                        CurrentInputCount = 8;
                        CurrentOutputCount = 16;
                        CurrentCrossPointCount = 8 * 16;
                        InputLineText = "输入线 (8条)";
                        OutputLineText = "输出线 (16条)";
                        break;
                    default:
                        // 4x32 Matrix (默认)
                        CurrentInputCount = 4;
                        CurrentOutputCount = 32;
                        CurrentCrossPointCount = 4 * 32;
                        InputLineText = "输入线 (4条)";
                        OutputLineText = "输出线 (32条)";
                        break;
                }

                // 刷新UI显示
                RaisePropertyChanged(nameof(MatrixStatisticsInfo));
                RefreshMatrixTopology();
            }
        }

        /// <summary>
        /// 当前拓扑显示文本
        /// </summary>
        public string CurrentTopologyText
        {
            get
            {
                // 根据槽位确定拓扑类型
                if (Device is SwitchDevice switchDevice)
                {
                    int slotIndex = switchDevice.SlotIndex;
                    switch (slotIndex)
                    {
                        case 4:
                            return "8x16 Matrix";
                        default:
                            return "4x32 Matrix";
                    }
                }
                return "4x32 Matrix";
            }
        }

        private Dispatcher Dispatcher
        {
            get
            {
                if (_dispatcher == null)
                {
                    _dispatcher = System.Windows.Application.Current?.Dispatcher ??
                                 System.Windows.Threading.Dispatcher.CurrentDispatcher;
                }
                return _dispatcher;
            }
        }

        public string MatrixStatisticsInfo => $"{CurrentInputCount}x{CurrentOutputCount} 矩阵 ({CurrentCrossPointCount}个交叉点)";

        public ObservableCollection<string> InputChannels
        {
            get => _inputChannels;
            set => SetProperty(ref _inputChannels, value);
        }

        public ObservableCollection<string> OutputChannels
        {
            get => _outputChannels;
            set => SetProperty(ref _outputChannels, value);
        }

        public ObservableCollection<RelayStatusInfo> RelayStatusList
        {
            get => _relayStatusList;
            set => SetProperty(ref _relayStatusList, value);
        }

        public ObservableCollection<TopologyConfig> TopologyConfigs
        {
            get => _topologyConfigs;
            set => SetProperty(ref _topologyConfigs, value);
        }

        public ObservableCollection<MatrixConnection> ActiveConnections
        {
            get => _activeConnections;
            set => SetProperty(ref _activeConnections, value);
        }

        public ObservableCollection<TopologyNodeInfo> TopologyNodes
        {
            get => _topologyNodes;
            set => SetProperty(ref _topologyNodes, value);
        }

        public ObservableCollection<MatrixNodeViewModel> MatrixNodes
        {
            get => _matrixNodes;
            set => SetProperty(ref _matrixNodes, value);
        }

        public ObservableCollection<TopologyConnectionInfo> TopologyConnections
        {
            get => _topologyConnections;
            set => SetProperty(ref _topologyConnections, value);
        }

        public ObservableCollection<LabelViewModel> InputLabels
        {
            get => _inputLabels;
            set => SetProperty(ref _inputLabels, value);
        }

        public ObservableCollection<LabelViewModel> OutputLabels
        {
            get => _outputLabels;
            set => SetProperty(ref _outputLabels, value);
        }

        public ObservableCollection<MatrixNodeViewModel> OutputNodes
        {
            get => _outputNodes;
            set => SetProperty(ref _outputNodes, value);
        }

        public ObservableCollection<MatrixNodeViewModel> InputNodes
        {
            get => _inputNodes;
            set => SetProperty(ref _inputNodes, value);
        }

        public ObservableCollection<CrossPointViewModel> CrossPoints
        {
            get => _crossPoints;
            set => SetProperty(ref _crossPoints, value);
        }

        public ObservableCollection<LineViewModel> VerticalLines
        {
            get => _verticalLines;
            set => SetProperty(ref _verticalLines, value);
        }

        public ObservableCollection<LineViewModel> HorizontalLines
        {
            get => _horizontalLines;
            set => SetProperty(ref _horizontalLines, value);
        }

        public ObservableCollection<MatrixConnectionViewModel> MatrixConnections
        {
            get => _matrixConnections;
            set => SetProperty(ref _matrixConnections, value);
        }

        public MatrixNodeViewModel SelectedInputNode
        {
            get => _selectedInputNode;
            set => SetProperty(ref _selectedInputNode, value);
        }

        public MatrixNodeViewModel SelectedOutputNode
        {
            get => _selectedOutputNode;
            set => SetProperty(ref _selectedOutputNode, value);
        }

        public CrossPointViewModel SelectedCrossPoint
        {
            get => _selectedCrossPoint;
            set => SetProperty(ref _selectedCrossPoint, value);
        }

        public TopologyNodeInfo FirstSelectedNode
        {
            get => _firstSelectedNode;
            private set => SetProperty(ref _firstSelectedNode, value);
        }

        public TopologyNodeInfo HoveredNode
        {
            get => _hoveredNode;
            set => SetProperty(ref _hoveredNode, value);
        }

        public string SelectionStatus
        {
            get
            {
                if (!IsDeviceOpened)
                    return "设备未连接";

                if (FirstSelectedNode == null)
                    return "请选择一个输入节点";
                else
                    return $"已选择: {FirstSelectedNode.NodeId}，请选择一个输出节点";
            }
        }

        public string MatrixSelectionStatus
        {
            get
            {
                if (!IsDeviceOpened)
                    return "设备未连接";

                if (SelectedInputNode == null)
                    return "第一步：请点击一个输入节点（蓝色圆圈）";
                else if (SelectedOutputNode == null)
                    return $"第二步：已选择输入节点 {SelectedInputNode.NodeId}，请点击一个输出节点（红色圆圈）";
                else
                    return $"第三步：准备连接 {SelectedInputNode.NodeId} → {SelectedOutputNode.NodeId}";
            }
        }

        public string ConfigurationInfo => _cardConfig?.GetStatistics() ?? "无配置信息";

        #endregion

        #region Public Methods

        /// <summary>
        /// 更新可用空间大小
        /// </summary>
        /// <param name="width">可用宽度</param>
        /// <param name="height">可用高度</param>
        public void UpdateAvailableSpace(double width, double height)
        {
            AvailableWidth = width;
            AvailableHeight = height;

            // 当可用空间变化时，重新刷新矩阵拓扑
            if (width > 0 && height > 0)
            {
                RefreshMatrixTopology();
            }
        }

        #endregion
        #region Commands
        public ICommand ToggleDeviceCommand { get; private set; }
        //public ICommand ConnectDeviceCommand { get; private set; }
        //public ICommand DisconnectDeviceCommand { get; private set; }
        public ICommand DisconnectAllCommand { get; private set; }
        public ICommand RefreshStatusCommand { get; private set; }
        public ICommand ResetCountersCommand { get; private set; }
        public ICommand ClearErrorsCommand { get; private set; }

        // 拓扑交互命令
        public ICommand NodeClickedCommand { get; private set; }
        public ICommand NodeRightClickedCommand { get; private set; }
        public ICommand NodeHoveredCommand { get; private set; }
        public ICommand ConnectionRightClickedCommand { get; private set; }

        // 矩阵拓扑交互命令
        public ICommand OutputNodeClickedCommand { get; private set; }
        public ICommand InputNodeClickedCommand { get; private set; }
        public ICommand CrossPointClickedCommand { get; private set; }
        public ICommand MatrixConnectionRightClickedCommand { get; private set; }
        public ICommand RefreshTopologyCommand { get; private set; }
        public ICommand MatrixNodeClickedCommand { get; private set; }

        // 新增的悬停命令
        public ICommand CrossPointHoveredCommand { get; private set; }
        public ICommand CrossPointMouseLeaveCommand { get; private set; }

        // 新增的确认连接命令
        public ICommand ConfirmConnectionCommand { get; private set; }
        public ICommand CancelConnectionCommand { get; private set; }
        // 矩阵拓扑右键命令
        public ICommand MatrixNodeRightClickedCommand { get; private set; }
        //public ICommand MatrixCrossPointRightClickedCommand { get; private set; }

        // 分解为两个独立命令
        public ICommand DisconnectCrossPointCommand { get; private set; }
        public ICommand ConnectCrossPointCommand { get; private set; }

        #endregion

        #region Constructor

        public PXI2601_SWITCHViewModel()
        {
            InitializeCollections();
            InitializeCommands();
            InitializeTopologyConfigs();

            ConnectionStatus = "离线";
            // 不再需要设置SelectedTopology，因为我们现在根据设备槽位自动确定拓扑
            // SelectedTopology = "4x32 Matrix";

            _dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
        }

        public PXI2601_SWITCHViewModel(DeviceBase device, string chassisName,
            IPxiChassisService pxiChassisService = null, IEventAggregator eventAggregator = null) : this()
        {

            if (device == null)
            {
                Debug.Write("DEVICE是空");
            }
            Device = device;
            ChassisName = chassisName;
            CardModel = device?.Model ?? "矩阵开关";
            CardName = !string.IsNullOrEmpty(device?.CardName) ? device.CardName : device?.Model ?? "PXI-2601";
            _pxiChassisService = pxiChassisService;
            _eventAggregator = eventAggregator;

            // 订阅来自机箱的远程矩阵命令事件（兼容性保留 EventAggregator）
            try
            {
                _eventAggregator?.GetEvent<MeasureControl.Events.RemoteMatrixCommandEvent>()?.Subscribe(OnRemoteMatrixCommand, Prism.Events.ThreadOption.BackgroundThread);
            }
            catch
            {
            }

            // dispatcher 注册会在 LoadDeviceConfig 中进行（确保使用服务中的权威 Device 实例）

            // 订阅设备修改事件，以便在面板未打开时被机箱回退执行后刷新 UI
            try
            {
                _eventAggregator?.GetEvent<MeasureControl.Events.DeviceModifiedEvent>()?.Subscribe(OnDeviceModified, Prism.Events.ThreadOption.UIThread);
            }
            catch { }

            LoadDeviceConfig();
            try
            {
                var ips = string.Join(",", GetLocalIpv4Addresses());
                Debug.WriteLine($"[PXI2601_SWITCHViewModel] LocalIPv4=[{ips}] Mode={(IsLocalChassisByIp() ? "LocalChassis(Server)" : "RemoteClient(TCP)")}");
            }
            catch
            {
            }

            // 注意：TCP服务器由PxiChassisViewModel统一管理，在设备添加到机箱时启动
            // ViewModel不应重复启动TCP服务器，以避免引用计数混乱
            if (IsLocalChassisByIp())
            {
                Debug.WriteLine($"[PXI2601_SWITCHViewModel] 本地机箱模式，TCP服务器应已由PxiChassisViewModel启动");
            }
        }

        private static string[] GetLocalIpv4Addresses()
        {
            try
            {
                return Dns.GetHostAddresses(Dns.GetHostName())
                    .Where(a => a.AddressFamily == AddressFamily.InterNetwork)
                    .Where(a => !IPAddress.IsLoopback(a))
                    .Select(a => a.ToString())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Distinct()
                    .ToArray();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        private bool IsLocalChassisByIp()
        {
            var ips = GetLocalIpv4Addresses();
            if (ips.Contains(LocalChassisIpAddress)) return true;
            if (ips.Contains(RemoteClientIpAddress)) return false;
            return false;
        }

        private bool IsRemoteChassis => !IsLocalChassisByIp();

        private async Task<bool> EnsureTcpConnectedAsync(string ipAddress, int port)
        {
            try
            {
                if (_tcpClient != null && _tcpClient.Connected)
                {
                    if (DateTime.Now - _tcpLastActivityTime < _tcpInactivityTimeout)
                    {
                        return true;
                    }
                    else
                    {
                        CleanupTcpConnection();
                    }
                }

                Debug.WriteLine($"[EnsureTcpConnectedAsync] 创建新TCP连接到 {ipAddress}:{port}, LocalIPv4=[{string.Join(",", GetLocalIpv4Addresses())}]");

                var client = new TcpClient();
                client.NoDelay = true;  // 禁用Nagle算法，减少延迟
                client.SendTimeout = 3000;  // 减少发送超时时间
                client.ReceiveTimeout = 3000;  // 减少接收超时时间

                // 设置更大的发送/接收缓冲区，提高性能
                client.SendBufferSize = 8192;
                client.ReceiveBufferSize = 8192;

                // 设置linger选项，确保数据完全发送
                client.LingerState = new LingerOption(true, 0);

                await client.ConnectAsync(ipAddress, port);

                _tcpClient = client;
                _tcpStream = client.GetStream();
                _tcpLastActivityTime = DateTime.Now;

                Debug.WriteLine($"[EnsureTcpConnectedAsync] TCP连接建立成功 Local={client.Client?.LocalEndPoint} Remote={client.Client?.RemoteEndPoint}");
                return true;
            }
            catch (Exception ex)
            {
                if (ex is SocketException se)
                {
                    Debug.WriteLine($"[EnsureTcpConnectedAsync] 连接失败(Socket): {se.SocketErrorCode}, {se.Message}");
                }
                else
                {
                    Debug.WriteLine($"[EnsureTcpConnectedAsync] 连接失败: {ex.Message}");
                }
                CleanupTcpConnection();
                return false;
            }
        }

        private void CleanupTcpConnection()
        {
            try
            {
                _tcpStream?.Dispose();
                _tcpStream = null;

                _tcpClient?.Close();
                _tcpClient?.Dispose();
                _tcpClient = null;

                Debug.WriteLine("[CleanupTcpConnection] TCP连接已清理");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CleanupTcpConnection] 清理失败: {ex.Message}");
            }
        }

        private string ResolveControlledChassisIpAddress()
        {
            return LocalChassisIpAddress;
        }

        private static bool TryParseNodeIndex(string nodeId, out byte index)
        {
            index = 0;
            if (string.IsNullOrWhiteSpace(nodeId) || nodeId.Length < 2) return false;
            if (!int.TryParse(nodeId.Substring(1), out int value)) return false;
            if (value < 0 || value > byte.MaxValue) return false;
            index = (byte)value;
            return true;
        }

        private async Task<bool> SendRemoteCommandAsync(string ipAddress, int port, byte inputIndex, byte outputIndex, byte state)
        {
            var startTime = DateTime.Now;
            try
            {
                if (!await EnsureTcpConnectedAsync(ipAddress, port))
                {
                    Debug.WriteLine("[SendRemoteCommandAsync] TCP连接失败");
                    return false;
                }

                _tcpLastActivityTime = DateTime.Now;

                var buffer = new[] { inputIndex, outputIndex, state };
                Debug.WriteLine($"[SendRemoteCommandAsync] TX({ipAddress}:{port}): {BitConverter.ToString(buffer)}");
                await _tcpStream.WriteAsync(buffer, 0, buffer.Length);
                await _tcpStream.FlushAsync();

                var sendTime = DateTime.Now - startTime;
                Debug.WriteLine($"[SendRemoteCommandAsync] 发送耗时: {(int)sendTime.TotalMilliseconds}ms");

                var ack = new byte[3];
                int timeoutMs = 2000; // 减少超时时间，从5秒改为2秒，提高响应速度

                // 使用异步读取替代轮询，提高响应速度
                using (var cts = new CancellationTokenSource(timeoutMs))
                {
                    try
                    {
                        int totalRead = 0;
                        while (totalRead < ack.Length)
                        {
                            int read = await _tcpStream.ReadAsync(ack, totalRead, ack.Length - totalRead, cts.Token);
                            if (read <= 0)
                            {
                                Debug.WriteLine("[SendRemoteCommandAsync] 连接中断");
                                CleanupTcpConnection();
                                return false;
                            }
                            totalRead += read;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        Debug.WriteLine("[SendRemoteCommandAsync] 接收响应超时");
                        CleanupTcpConnection();
                        return false;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[SendRemoteCommandAsync] 读取异常: {ex.Message}");
                        CleanupTcpConnection();
                        return false;
                    }
                }

                var totalTime = DateTime.Now - startTime;
                bool success = ack[0] == inputIndex && ack[1] == outputIndex && ack[2] == state;
                if (!success)
                {
                    Debug.WriteLine("[SendRemoteCommandAsync] 响应验证失败");
                    CleanupTcpConnection();
                }

                Debug.WriteLine($"[SendRemoteCommandAsync] RX({ipAddress}:{port}): {BitConverter.ToString(ack)}, 总耗时: {(int)totalTime.TotalMilliseconds}ms");
                return success;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SendRemoteCommandAsync] 异常: {ex.Message}");
                CleanupTcpConnection();
                return false;
            }
        }

        private async Task<bool> SendRemoteCommandWithRetryAsync(string ipAddress, int port, byte inputIndex, byte outputIndex, byte state, int maxRetries = 2)
        {
            for (int retry = 0; retry < maxRetries; retry++)
            {
                try
                {
                    if (retry > 0)
                    {
                        Debug.WriteLine($"[SendRemoteCommandWithRetryAsync] 第 {retry + 1} 次重试");
                        await Task.Delay(50 * retry); // 减少重试间隔，从100ms改为50ms，提高响应速度
                        CleanupTcpConnection();
                    }

                    bool success = await SendRemoteCommandAsync(ipAddress, port, inputIndex, outputIndex, state);
                    if (success) return true;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[SendRemoteCommandWithRetryAsync] 重试 {retry + 1} 失败: {ex.Message}");
                    if (retry == maxRetries - 1) return false;
                }
            }

            return false;
        }

        private async Task<bool> SendRemoteDriverControlAsync(byte state)
        {
            int port = GetTcpListenPort();
            var ipAddress = ResolveControlledChassisIpAddress();
            Debug.WriteLine($"[SendRemoteDriverControlAsync] 发送驱动控制: state={state}, IP={ipAddress}, Port={port}");
            return await SendRemoteCommandWithRetryAsync(ipAddress, port, 0xFF, 0, state);
        }

        private async Task<bool> SendMatrixCommandAsync(string inputNodeId, string outputNodeId, byte state)
        {
            if (!TryParseNodeIndex(inputNodeId, out var inputIndex) ||
                !TryParseNodeIndex(outputNodeId, out var outputIndex))
            {
                Debug.WriteLine($"[SendMatrixCommandAsync] 节点解析失败: {inputNodeId} -> {outputNodeId}");
                return false;
            }

            int port = GetTcpListenPort();
            var ipAddress = ResolveControlledChassisIpAddress();
            Debug.WriteLine($"[SendMatrixCommandAsync] 发送矩阵命令: {inputNodeId}({inputIndex})->{outputNodeId}({outputIndex}), state={state}");
            return await SendRemoteCommandWithRetryAsync(ipAddress, port, inputIndex, outputIndex, state);
        }

        private async Task<bool> SendMatrixCommandAsync(string inputNodeId, string outputNodeId, byte state, int slotIndex)
        {
            if (!TryParseNodeIndex(inputNodeId, out var inputIndex) ||
                !TryParseNodeIndex(outputNodeId, out var outputIndex))
            {
                Debug.WriteLine($"[SendMatrixCommandAsync] 节点解析失败: {inputNodeId} -> {outputNodeId}");
                return false;
            }

            int port = 50200 + slotIndex;
            var ipAddress = ResolveControlledChassisIpAddress();
            Debug.WriteLine($"[SendMatrixCommandAsync] 发送矩阵命令: {inputNodeId}({inputIndex})->{outputNodeId}({outputIndex}), state={state}");
            return await SendRemoteCommandWithRetryAsync(ipAddress, port, inputIndex, outputIndex, state);
        }

        private async Task DisconnectAllRemoteConnectionsAsync()
        {
            try
            {
                var activeConnections = _cardConfig?.GetAllActiveConnections() ?? new List<MatrixConnection>();
                Debug.WriteLine($"[DisconnectAllRemoteConnectionsAsync] 有 {activeConnections.Count} 个活跃连接需要断开");

                foreach (var conn in activeConnections)
                {
                    try
                    {
                        await SendMatrixCommandAsync(conn.InputChannel, conn.OutputChannel, RemoteCommandDisconnect);
                        _cardConfig.SetConnection(conn.InputChannel, conn.OutputChannel, SwitchConnectionState.Disconnected);
                        await Task.Delay(10);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[DisconnectAllRemoteConnectionsAsync] 断开 {conn.InputChannel}->{conn.OutputChannel} 失败: {ex.Message}");
                    }
                }

                Debug.WriteLine("[DisconnectAllRemoteConnectionsAsync] 所有连接已断开");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DisconnectAllRemoteConnectionsAsync] 异常: {ex.Message}");
            }
        }

        private void UpdateAllRelayStatusToDisconnected()
        {
            if (_cardConfig == null) return;

            foreach (var connection in _cardConfig.ConnectionMap.Values)
            {
                if (connection.State == SwitchConnectionState.Connected)
                {
                    connection.SetConnectionState(SwitchConnectionState.Disconnected);
                }
            }

            UpdateAllRelayStatus();
            ActiveConnections.Clear();
            RaisePropertyChanged(nameof(RelayStatusList));
        }

        private async Task<bool> ConnectNodesAsync(string inputNodeId, string outputNodeId)
        {
            Debug.WriteLine($"========================================");
            Debug.WriteLine($"[ConnectNodesAsync] 开始连接流程");
            Debug.WriteLine($"[ConnectNodesAsync] 目标连接: {inputNodeId} -> {outputNodeId}");

            if (IsRemoteChassis)
            {
                bool success = false;
                try
                {
                    if (!IsDeviceOpened)
                    {
                        IsDeviceOpened = true;
                        RaisePropertyChanged(nameof(ConnectButtonText));
                    }

                    success = await SendMatrixCommandAsync(inputNodeId, outputNodeId, RemoteCommandConnect);

                    if (success)
                    {
                        if (!IsDeviceConnected)
                        {
                            IsDeviceConnected = true;
                            ConnectionStatus = "远程在线";
                        }

                        var connection = _cardConfig.GetConnection(inputNodeId, outputNodeId);
                        if (connection == null)
                        {
                            connection = _cardConfig.CreateConnection(inputNodeId, outputNodeId);
                        }

                        _cardConfig.SetConnection(inputNodeId, outputNodeId, SwitchConnectionState.Connected);

                        await Dispatcher.InvokeAsync(() =>
                        {


                            IsDeviceOpened = true;
                            RaisePropertyChanged(nameof(IsDeviceOpened));
                            if (connection != null)
                            {
                                connection.ConnectionColor = "#4CAF50";
                            }

                            UpdateRelayStatusInUI(inputNodeId, outputNodeId);

                            UpdateActiveConnections();
                            UpdateConnectionCounts();
                            RefreshTopologyVisualization();
                            UpdateCrossPointsConnectionStatus();
                            UpdateMatrixNodesConnectionStatus();
                            RaisePropertyChanged(nameof(RelayStatusList));
                        });

                        SaveDeviceConfig();
                    }
                    else
                    {
                        var connection = _cardConfig.GetConnection(inputNodeId, outputNodeId);
                        if (connection == null)
                        {
                            connection = _cardConfig.CreateConnection(inputNodeId, outputNodeId);
                        }

                        _cardConfig.SetConnection(inputNodeId, outputNodeId, SwitchConnectionState.Error, "连接失败");

                        await Dispatcher.InvokeAsync(() =>
                        {
                            UpdateRelayStatus(inputNodeId, outputNodeId);
                            UpdateConnectionCounts();
                        });

                        SaveDeviceConfig();

                        if (Interlocked.CompareExchange(ref _isProcessingRemoteCommand, 0, 0) == 0)
                        {
                            ReMessageBox.Show($"连接失败: {inputNodeId} -> {outputNodeId}", "连接错误",
                                MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ConnectNodesAsync] 异常: {ex.Message}");

                    _cardConfig.SetConnection(inputNodeId, outputNodeId, SwitchConnectionState.Error, ex.Message);
                    var connection = _cardConfig.GetConnection(inputNodeId, outputNodeId);
                    if (connection == null)
                    {
                        connection = _cardConfig.CreateConnection(inputNodeId, outputNodeId);
                        _cardConfig.SetConnection(inputNodeId, outputNodeId, SwitchConnectionState.Error, ex.Message);
                    }
                    await Dispatcher.InvokeAsync(() =>
                    {
                        if (connection != null)
                        {
                            connection.ConnectionColor = "#F44336";
                        }

                        UpdateRelayStatus(inputNodeId, outputNodeId);
                        UpdateConnectionCounts();

                        var errorCrossPoint = CrossPoints.FirstOrDefault(cp =>
                            cp.InputNodeId == inputNodeId &&
                            cp.OutputNodeId == outputNodeId);

                        if (errorCrossPoint != null)
                        {
                            errorCrossPoint.IsConnected = false;
                            errorCrossPoint.ConnectionColor = "#F44336";
                        }

                        // 更新节点连接状态
                        UpdateMatrixNodesConnectionStatus();
                    });

                    SaveDeviceConfig();

                    if (Interlocked.CompareExchange(ref _isProcessingRemoteCommand, 0, 0) == 0)
                    {
                        ReMessageBox.Show($"连接失败: {ex.Message}", "连接错误",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }

                    success = false;
                }

                return success;
            }

            var existingConnections = _cardConfig.GetAllActiveConnections();
            Debug.WriteLine($"[ConnectNodesAsync] 当前活跃连接数: {existingConnections.Count}");
            foreach (var conn in existingConnections)
            {
                Debug.WriteLine($"[ConnectNodesAsync] 现有连接: {conn.InputChannel} -> {conn.OutputChannel}");
            }

            Debug.WriteLine($"========================================");

            // 统一连接逻辑：优先使用现有驱动，避免重置硬件
            if (_driver == null || !_driver.IsConnected)
            {
                Debug.WriteLine("[ConnectNodesAsync] 连接设备");
                await ConnectDeviceAsync();
            }

            if (_driver == null || !_driver.IsConnected)
            {
                Debug.WriteLine("[ConnectNodesAsync] 设备连接失败");
                IsDeviceConnected = false;
                return false;
            }

            // 同步状态
            IsDeviceConnected = true;
            ConnectionStatus = "在线";

            try
            {
                // 1. 检查是否已经有连接
                var existingConnection = _cardConfig.GetConnection(inputNodeId, outputNodeId);
                int currentCount = existingConnection?.ConnectionCount ?? 0;
                Debug.WriteLine($"[ConnectNodesAsync] 现有连接: {existingConnection != null}, 当前计数: {currentCount}");

                // ===== 关键修改：移除自动断开相关连接的逻辑 =====
                // 不再检查并断开 inputNodeId 或 outputNodeId 的其他连接
                // 允许一个输入连接多个输出，一个输出连接多个输入
                // ================================================

                // 2. 先设置连接中状态（可选）
                Debug.WriteLine($"[ConnectNodesAsync] 设置连接中状态");
                Dispatcher.Invoke(() =>
                {
                    // 更新交叉点
                    var crossPoint = CrossPoints.FirstOrDefault(cp =>
                        cp.InputNodeId == inputNodeId &&
                        cp.OutputNodeId == outputNodeId);

                    if (crossPoint != null)
                    {
                        crossPoint.IsPendingConnection = true; // 显示连接中状态
                        Debug.WriteLine($"[ConnectNodesAsync] 交叉点设置为连接中: {crossPoint.DisplayName}");
                    }
                });

                // 3. 执行硬件连接
                Debug.WriteLine($"[ConnectNodesAsync] 开始硬件连接");
                bool success = false;

                try
                {
                    // 使用新的方法，避免断开现有连接
                    success = await _driver.ConnectChannelsWithoutDisconnectAsync(outputNodeId, inputNodeId);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ConnectNodesAsync] 硬件连接异常: {ex.Message}");

                    // 特殊处理"连接已存在"的错误
                    if (ex.Message.Contains("already exists") || ex.Message.Contains("0xBFFA200C"))
                    {
                        Debug.WriteLine($"[ConnectNodesAsync] 连接已存在，视为成功");
                        success = true;
                    }
                    else
                    {
                        throw;
                    }
                }

                Debug.WriteLine($"[ConnectNodesAsync] 硬件连接结果: {success}");

                // 4. 连接成功后更新本地配置
                if (success)
                {
                    Debug.WriteLine($"[ConnectNodesAsync] 硬件连接成功，更新本地配置");

                    // 获取或创建连接
                    var connection = _cardConfig.GetConnection(inputNodeId, outputNodeId);
                    if (connection == null)
                    {
                        Debug.WriteLine($"[ConnectNodesAsync] 创建新连接");
                        connection = _cardConfig.CreateConnection(inputNodeId, outputNodeId);
                    }

                    // 更新连接状态（通过cardConfig，确保计数正确更新）
                    _cardConfig.SetConnection(inputNodeId, outputNodeId, SwitchConnectionState.Connected);
                    Debug.WriteLine($"[ConnectNodesAsync] 连接计数: {connection.ConnectionCount}");

                    // 在UI线程上更新所有相关UI
                    Dispatcher.Invoke(() =>
                    {
                        Debug.WriteLine($"[ConnectNodesAsync] 在UI线程更新显示");

                        if (connection != null)
                        {
                            connection.ConnectionColor = "#4CAF50";
                        }

                        // 更新继电器状态
                        UpdateRelayStatusInUI(inputNodeId, outputNodeId);

                        // 更新活动连接列表
                        UpdateActiveConnections();

                        // 更新连接计数
                        UpdateConnectionCounts();

                        // 刷新拓扑可视化
                        RefreshTopologyVisualization();

                        // 优化：优先更新关键UI元素
                        UpdateCrossPointStatus(inputNodeId, outputNodeId); // 快速更新单个交叉点
                        UpdateMatrixNodesConnectionStatus(); // 更新节点状态

                        // 其他耗时操作移到后台线程
                        Task.Run(async () =>
                        {
                            UpdateRelayStatusInUI(inputNodeId, outputNodeId);
                            UpdateActiveConnections();
                            UpdateConnectionCounts();
                            await UpdateCrossPointsConnectionStatusAsync(); // 异步更新所有交叉点

                            // 在UI线程中更新最终状态
                            Dispatcher.Invoke(() =>
                            {
                                RaisePropertyChanged(nameof(RelayStatusList));
                                RaisePropertyChanged(nameof(ActiveConnections));
                                RaisePropertyChanged(nameof(TotalConnectionCount));
                            });
                        });

                        Debug.WriteLine($"[ConnectNodesAsync] UI已更新完成");
                    });

                    Debug.WriteLine($"[ConnectNodesAsync] 连接成功完成: {inputNodeId} -> {outputNodeId}, 计数: {connection.ConnectionCount}");

                    // 保存配置
                    SaveDeviceConfig();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ConnectNodesAsync] 连接过程中发生异常: {ex.Message}");

                // 设置错误状态
                _cardConfig.SetConnection(inputNodeId, outputNodeId, SwitchConnectionState.Error, ex.Message);
                var connection = _cardConfig.GetConnection(inputNodeId, outputNodeId);
                Dispatcher.Invoke(() =>
                {
                    if (connection != null)
                    {
                        connection.ConnectionColor = "#F44336";
                    }

                    UpdateRelayStatus(inputNodeId, outputNodeId);
                    UpdateConnectionCounts();

                    var errorCrossPoint = CrossPoints.FirstOrDefault(cp =>
                        cp.InputNodeId == inputNodeId &&
                        cp.OutputNodeId == outputNodeId);

                    if (errorCrossPoint != null)
                    {
                        errorCrossPoint.IsConnected = false;
                        errorCrossPoint.ConnectionColor = "#F44336";
                    }

                    // 更新节点连接状态
                    UpdateMatrixNodesConnectionStatus();
                });

                SaveDeviceConfig();

                ReMessageBox.Show($"连接失败: {ex.Message}", "连接错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                // 无论成功失败，都更新状态显示
                Dispatcher.Invoke(() =>
                {
                    RaisePropertyChanged(nameof(MatrixSelectionStatus));
                });
            }

            return true;
        }


        private async Task<bool> ConnectTcpNodesAsync(string inputNodeId, string outputNodeId)
        {

            File.AppendAllText(@"C:\LOG\LOG.TXT", "ConnectTcpNodesAsync+FFFF" + Environment.NewLine);
            IsDeviceOpened = true;
            RaisePropertyChanged(nameof(IsDeviceOpened));
            Debug.WriteLine($"========================================");
            Debug.WriteLine($"[ConnectNodesAsync] 开始连接流程");
            Debug.WriteLine($"[ConnectNodesAsync] 目标连接: {inputNodeId} -> {outputNodeId}");

            var existingConnections = _cardConfig.GetAllActiveConnections();
            Debug.WriteLine($"[ConnectNodesAsync] 当前活跃连接数: {existingConnections.Count}");
            foreach (var conn in existingConnections)
            {
                Debug.WriteLine($"[ConnectNodesAsync] 现有连接: {conn.InputChannel} -> {conn.OutputChannel}");
            }

            Debug.WriteLine($"========================================");

            // 首先检查是否有缓存的已连接驱动
            if (_driver == null)
            {
                var cachedDriver = DriverFactory.GetCachedDriver(Device.Id, (Device as MeasureControl.Models.Devices.DeviceCategories.PxiDeviceBase)?.SlotIndex ?? -1);
                if (cachedDriver != null && cachedDriver.IsConnected)
                {
                    _driver = cachedDriver as ArtSwitchDriver;
                    IsDeviceConnected = true;
                    ConnectionStatus = "在线";
                    Debug.WriteLine("[ConnectNodesAsync] 使用缓存的已连接驱动");
                }
                else
                {
                    await ConnectDeviceAsync();
                }
            }

            if (_driver == null)
            {
                Debug.WriteLine("[ConnectNodesAsync] 错误：无法获取驱动");
                return false;
            }

            // 如果驱动已经连接但ViewModel的状态未同步，更新状态
            if (_driver.IsConnected && !IsDeviceConnected)
            {
                IsDeviceConnected = true;
                ConnectionStatus = "在线";
                Debug.WriteLine("[ConnectNodesAsync] 同步驱动连接状态");
            }

            // 对于远程TCP命令，优先使用已有的驱动连接，避免重置硬件状态
            if (_driver == null || !_driver.IsConnected)
            {
                Debug.WriteLine("[ConnectNodesAsync] 驱动未连接，尝试连接设备");
                await ConnectDeviceAsync();
                if (_driver == null || !_driver.IsConnected)
                {
                    Debug.WriteLine("[ConnectNodesAsync] 连接设备失败");
                    IsDeviceConnected = false;
                    return false;
                }
            }

            // 同步ViewModel的状态
            if (_driver.IsConnected && !IsDeviceConnected)
            {
                IsDeviceConnected = true;
                ConnectionStatus = "在线";
                Debug.WriteLine("[ConnectNodesAsync] 同步设备连接状态");
            }

            try
            {
                // 1. 检查是否已经有连接
                var existingConnection = _cardConfig.GetConnection(inputNodeId, outputNodeId);
                int currentCount = existingConnection?.ConnectionCount ?? 0;
                Debug.WriteLine($"[ConnectNodesAsync] 现有连接: {existingConnection != null}, 当前计数: {currentCount}");

                // 2. 立即更新UI，显示连接中状态
                Debug.WriteLine($"[ConnectNodesAsync] 更新UI为连接中状态");
                Dispatcher.Invoke(() =>
                {

                    var crossPoint = CrossPoints.FirstOrDefault(cp =>
                        cp.InputNodeId == inputNodeId &&
                        cp.OutputNodeId == outputNodeId);

                    if (crossPoint != null)
                    {
                        Debug.WriteLine($"[ConnectNodesAsync] 更新交叉点UI: {crossPoint.DisplayName}");
                        crossPoint.IsPendingConnection = false;
                        crossPoint.IsConnected = true;
                        crossPoint.ConnectionColor = "#4CAF50";
                        Debug.WriteLine($"[ConnectNodesAsync] 交叉点状态: Connected={crossPoint.IsConnected}, Color={crossPoint.ConnectionColor}");
                    }
                    else
                    {
                        Debug.WriteLine($"[ConnectNodesAsync] 警告：没有找到对应的交叉点");
                    }

                    var inputNode = MatrixNodes.FirstOrDefault(n => n.NodeId == inputNodeId);
                    var outputNode = MatrixNodes.FirstOrDefault(n => n.NodeId == outputNodeId);

                    if (inputNode != null)
                    {
                        Debug.WriteLine($"[ConnectNodesAsync] 输入节点 {inputNodeId} 标记为需要更新");
                    }
                    if (outputNode != null)
                    {
                        Debug.WriteLine($"[ConnectNodesAsync] 输出节点 {outputNodeId} 标记为需要更新");
                    }
                });

                // 3. 执行硬件连接
                Debug.WriteLine($"[ConnectNodesAsync] 开始硬件连接");
                bool success;
                try
                {
                    success = await _driver.ConnectChannelsWithoutDisconnectAsync(outputNodeId, inputNodeId);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ConnectNodesAsync] 硬件连接异常: {ex.Message}");
                    if (ex.Message.Contains("already exists") || ex.Message.Contains("0xBFFA200C"))
                    {
                        Debug.WriteLine($"[ConnectNodesAsync] 连接已存在，视为成功");
                        success = true;
                    }
                    else
                    {
                        throw;
                    }
                }

                Debug.WriteLine($"[ConnectNodesAsync] 硬件连接结果: {success}");
                if (!success) return false;

                // 4. 连接成功后更新本地配置
                var connection = _cardConfig.GetConnection(inputNodeId, outputNodeId);
                if (connection == null)
                {
                    Debug.WriteLine($"[ConnectNodesAsync] 创建新连接");
                    connection = _cardConfig.CreateConnection(inputNodeId, outputNodeId);
                }

                _cardConfig.SetConnection(inputNodeId, outputNodeId, SwitchConnectionState.Connected);
                Debug.WriteLine($"[ConnectNodesAsync] 连接计数: {connection.ConnectionCount}");

                Dispatcher.Invoke(() =>
                {
                    if (connection != null)
                    {
                        connection.ConnectionColor = "#4CAF50";
                    }

                    UpdateRelayStatusInUI(inputNodeId, outputNodeId);
                    UpdateActiveConnections();
                    UpdateConnectionCounts();
                    RefreshTopologyVisualization();
                    UpdateCrossPointsConnectionStatus();
                    UpdateMatrixNodesConnectionStatus();
                    UpdateRelayStatusInUI(inputNodeId, outputNodeId);
                    RaisePropertyChanged(nameof(RelayStatusList));

                    Debug.WriteLine($"[ConnectNodesAsync] UI已更新完成");
                });

                Debug.WriteLine($"[ConnectNodesAsync] 连接成功完成: {inputNodeId} -> {outputNodeId}, 计数: {connection.ConnectionCount}");
                SaveDeviceConfig();
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ConnectNodesAsync] 连接过程中发生异常: {ex.Message}");

                _cardConfig.SetConnection(inputNodeId, outputNodeId, SwitchConnectionState.Error, ex.Message);
                var connection = _cardConfig.GetConnection(inputNodeId, outputNodeId);

                Dispatcher.Invoke(() =>
                {
                    if (connection != null)
                    {
                        connection.ConnectionColor = "#F44336";
                    }

                    UpdateRelayStatus(inputNodeId, outputNodeId);
                    UpdateConnectionCounts();

                    var errorCrossPoint = CrossPoints.FirstOrDefault(cp =>
                        cp.InputNodeId == inputNodeId &&
                        cp.OutputNodeId == outputNodeId);

                    if (errorCrossPoint != null)
                    {
                        errorCrossPoint.IsConnected = false;
                        errorCrossPoint.ConnectionColor = "#F44336";
                    }

                    UpdateMatrixNodesConnectionStatus();
                });

                SaveDeviceConfig();
                ReMessageBox.Show($"连接失败: {ex.Message}", "连接错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                File.AppendAllText(@"C:\LOG\LOG.TXT", "error" + Environment.NewLine);
                return false;
            }
            finally
            {
                Dispatcher.Invoke(() =>
                {
                    RaisePropertyChanged(nameof(MatrixSelectionStatus));
                });
            }
            File.AppendAllText(@"C:\LOG\LOG.TXT", "4444" + Environment.NewLine);
        }

        private void OnRemoteMatrixCommand(MeasureControl.Events.RemoteMatrixCommandEventArgs args)
        {
            try
            {
                if (args == null) return;
                var mySlot = (Device as MeasureControl.Models.Devices.DeviceCategories.PxiDeviceBase)?.SlotIndex ?? -1;
                if (mySlot <= 0) return;
                if (mySlot != args.SlotIndex) return;

                Debug.WriteLine($"[PXI2601_SWITCHViewModel] Received remote command for slot {args.SlotIndex}: {args.InputNodeId}->{args.OutputNodeId}, state={args.State}");

                // 在后台处理，避免阻塞事件发布线程
                _ = Task.Run(async () =>
                {
                    try
                    {
                        if (args.State == 0)
                        {
                            await ConnectTcpNodesAsync(args.InputNodeId, args.OutputNodeId);
                        }
                        else
                        {
                            await DisconnectNodesAsync(args.InputNodeId, args.OutputNodeId);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[PXI2601_SWITCHViewModel] Remote command handling failed: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PXI2601_SWITCHViewModel] OnRemoteMatrixCommand exception: {ex.Message}");
            }
        }

        private void OnDeviceModified(MeasureControl.Events.DeviceModifiedEventArgs args)
        {
            try
            {
                if (args?.Device == null || Device == null) return;
                if (args.Device.Id != Device.Id) return;

                // 只有在非远程命令情况下才重新加载配置，避免重置TCP连接状态
                if (args.ModificationType != "RemoteCommand")
                {
                    Debug.WriteLine($"[PXI2601_SWITCHViewModel] DeviceModifiedEvent received for DeviceId={Device.Id}, reloading CardConfigData");
                    // Reload CardConfigData and refresh UI on UI thread
                    Dispatcher.Invoke(() =>
                    {
                        LoadDeviceConfig();
                    });
                }
                else
                {
                    Debug.WriteLine($"[PXI2601_SWITCHViewModel] DeviceModifiedEvent received for DeviceId={Device.Id}, skipping reload for RemoteCommand to preserve TCP connections");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PXI2601_SWITCHViewModel] OnDeviceModified exception: {ex.Message}");
            }
        }
        private async Task ConnectDeviceAsync()
        {
            if (Device == null) return;

            if (IsRemoteChassis)
            {
                try
                {
                    RaisePropertyChanged(nameof(ConnectButtonText));
                    return;
                }
                catch (Exception ex)
                {
                    IsDeviceConnected = false;
                    ConnectionStatus = "远程离线";
                    CleanupTcpConnection();

                    if (Interlocked.CompareExchange(ref _isProcessingRemoteCommand, 0, 0) == 0)
                    {
                        ReMessageBox.Show($"远程矩阵开关驱动连接异常: {ex.Message}", "连接异常",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }

                    return;
                }
            }

            try
            {
                ConnectionStatus = "连接中";

                var topologyConfig = TopologyConfigs.FirstOrDefault(t => t.Name == SelectedTopology);
                if (topologyConfig == null) return;

                _driver = DriverFactory.CreateDriver(Device) as ArtSwitchDriver;

                (_driver as ArtSwitchDriver)!.CurrentTopology = topologyConfig.TopologyString;

                bool connected = await _driver.ConnectAsync(topologyConfig.TopologyString);

                if (connected)
                {
                    IsDeviceConnected = true;
                    ConnectionStatus = "在线";
                    Debug.WriteLine($"[SwitchControl] ART-SWITCH设备连接成功: {Device.Name}");
                    RaisePropertyChanged(nameof(ConnectButtonText));

                    // 注意：连接设备成功后，不再自动断开所有连接，以避免破坏通过TCP命令建立的连接
                    // 只有在确实需要同步硬件状态时才断开连接
                    // await _driver.DisconnectAllAsync();

                    StartStatusTimer();

                    await RefreshConnectionStatusAsync();
                }
                else
                {
                    IsDeviceConnected = false;
                    ConnectionStatus = "离线";
                    _driver = null;
                    RaisePropertyChanged(nameof(ConnectButtonText));
                    ReMessageBox.Show($"ART-SWITCH设备连接失败", "连接失败",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                IsDeviceConnected = false;
                ConnectionStatus = "离线";
                _driver = null;

                ReMessageBox.Show($"ART-SWITCH设备连接异常: {ex.Message}", "连接异常",
                    MessageBoxButton.OK, MessageBoxImage.Error);

                Debug.WriteLine($"[SwitchControl] 设备连接异常: {ex.Message}");
            }
        }

        private async Task DisconnectDeviceAsync()
        {
            try
            {
                ConnectionStatus = "断开中";

                if (IsRemoteChassis)
                {
                    await DisconnectAllRemoteConnectionsAsync();

                    bool driverDisconnected = await SendRemoteDriverControlAsync(0);
                    if (driverDisconnected)
                    {
                        IsDeviceConnected = false;
                        ConnectionStatus = "远程离线";

                        await Dispatcher.InvokeAsync(() =>
                        {
                            UpdateAllRelayStatusToDisconnected();
                            UpdateActiveConnections();
                            UpdateConnectionCounts();
                            RefreshTopologyVisualization();
                            UpdateCrossPointsConnectionStatus();
                            UpdateMatrixNodesConnectionStatus();
                            ClearAllSelectionStates();
                        });

                        CleanupTcpConnection();
                    }
                    else
                    {
                        ConnectionStatus = "远程断开失败";
                    }

                    RaisePropertyChanged(nameof(ConnectButtonText));
                    return;
                }

                StopStatusTimer();

                if (_driver != null)
                {
                    // 1. 断开所有硬件连接
                    await _driver.DisconnectAllAsync();

                    // 2. 断开设备连接
                    await _driver.DisconnectAsync();
                    _driver = null;

                    Debug.WriteLine($"[DisconnectDeviceAsync] 硬件连接已断开");
                }

                // 3. 更新设备连接状态
                IsDeviceConnected = false;
                ConnectionStatus = "离线";

                // 4. 在 UI 线程上更新软件状态
                Dispatcher.Invoke(() =>
                {
                    Debug.WriteLine("[DisconnectDeviceAsync] 开始更新软件状态");

                    if (_cardConfig != null)
                    {
                        // 5. 将所有连接状态设置为 Disconnected（但不重置计数）
                        foreach (var connection in _cardConfig.ConnectionMap.Values)
                        {
                            if (connection.State == SwitchConnectionState.Connected)
                            {
                                // 只改变状态，不重置计数
                                connection.SetConnectionState(SwitchConnectionState.Disconnected);
                                Debug.WriteLine($"[DisconnectDeviceAsync] 设置连接 {connection.InputChannel}->{connection.OutputChannel} 状态为 Disconnected，计数保持: {connection.ConnectionCount}");
                            }
                        }

                        // 更新配置中的计数
                        _cardConfig.UpdateCounts();
                        _cardConfig.UpdateActiveConnectionsList();
                    }

                    // ================ 新增代码开始 ================
                    // 7. 强制更新所有继电器状态
                    UpdateAllRelayStatus();
                    // ================ 新增代码结束 ================

                    // 8. 清空活动连接列表
                    ActiveConnections.Clear();

                    // 9. 更新连接计数显示
                    UpdateConnectionCounts();

                    // 10. 清除所有待处理连接
                    foreach (var crossPoint in CrossPoints)
                    {
                        crossPoint.IsPendingConnection = false;
                        crossPoint.IsConnected = false;
                        crossPoint.ConnectionColor = null;
                    }

                    // 11. 更新交叉点状态
                    UpdateCrossPointsConnectionStatus();

                    // 12. 刷新拓扑可视化
                    RefreshTopologyVisualization();

                    // 13. 更新节点连接状态
                    UpdateMatrixNodesConnectionStatus();

                    // 14. 清除所有选择状态
                    ClearAllSelectionStates();

                    Debug.WriteLine($"[DisconnectDeviceAsync] 软件状态更新完成，ActiveRelayCount: {ActiveRelayCount}, TotalConnectionCount: {TotalConnectionCount}");
                });

                // 15. 保存配置
                SaveDeviceConfig();

                Debug.WriteLine($"[SwitchControl] 设备已断开连接，所有继电器已关闭，连接计数保留: {TotalConnectionCount}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SwitchControl] 断开连接异常: {ex.Message}");
            }
            finally
            {
                RaisePropertyChanged(nameof(ConnectButtonText));
            }
        }

        private async Task DisconnectAllAsync()
        {
            if (!IsDeviceConnected) return;

            if (IsRemoteChassis)
            {
                try
                {
                    Debug.WriteLine("[DisconnectAllAsync] 开始断开所有远程连接");

                    if (!IsDeviceConnected)
                    {
                        Debug.WriteLine("[DisconnectAllAsync] 驱动未连接，无法断开所有连接");
                        return;
                    }

                    await DisconnectAllRemoteConnectionsAsync();

                    Dispatcher.Invoke(() =>
                    {
                        UpdateAllRelayStatus();
                        UpdateActiveConnections();
                        UpdateConnectionCounts();
                        RefreshTopologyVisualization();
                        UpdateCrossPointsConnectionStatus();
                        UpdateMatrixNodesConnectionStatus();
                        RaisePropertyChanged(nameof(RelayStatusList));
                    });

                    SaveDeviceConfig();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[DisconnectAllAsync] 断开所有连接异常: {ex.Message}");
                }

                return;
            }

            if (_driver == null) return;

            try
            {
                Debug.WriteLine($"[DisconnectAllAsync] 开始断开所有连接");

                // 1. 断开所有硬件连接
                bool success = await _driver.DisconnectAllAsync();

                if (success)
                {
                    Debug.WriteLine($"[DisconnectAllAsync] 硬件连接已断开，开始更新软件状态");

                    // 2. 同时更新软件配置中的所有连接状态
                    if (_cardConfig != null)
                    {
                        int disconnectedCount = 0;
                        foreach (var connection in _cardConfig.ConnectionMap.Values)
                        {
                            if (connection.State == SwitchConnectionState.Connected)
                            {
                                // 更新软件连接状态为断开
                                connection.SetConnectionState(SwitchConnectionState.Disconnected);
                                disconnectedCount++;
                                Debug.WriteLine($"[DisconnectAllAsync] 软件设置连接 {connection.InputChannel}->{connection.OutputChannel} 状态为 Disconnected");
                            }
                        }

                        // 更新配置统计
                        _cardConfig.UpdateCounts();
                        _cardConfig.UpdateActiveConnectionsList();

                        Debug.WriteLine($"[DisconnectAllAsync] 共断开 {disconnectedCount} 个软件连接");
                    }

                    // 3. 在UI线程上更新显示
                    Dispatcher.Invoke(() =>
                    {
                        Debug.WriteLine("[DisconnectAllAsync] 在UI线程更新显示");

                        // 4. 强制更新所有继电器状态（这会设置为关闭状态）
                        UpdateAllRelayStatus();

                        // 5. 清空活动连接列表
                        ActiveConnections.Clear();

                        // 6. 更新连接计数显示
                        UpdateConnectionCounts();

                        // 7. 清除所有待处理连接和连接状态
                        foreach (var crossPoint in CrossPoints)
                        {
                            crossPoint.IsPendingConnection = false;
                            crossPoint.IsConnected = false;
                            crossPoint.ConnectionColor = null;
                        }

                        // 8. 更新交叉点状态
                        UpdateCrossPointsConnectionStatus();

                        // 9. 刷新拓扑可视化
                        RefreshTopologyVisualization();

                        // 10. 更新节点连接状态
                        UpdateMatrixNodesConnectionStatus();

                        // 11. 清除所有选择状态
                        ClearAllSelectionStates();

                        // 12. 更新状态显示
                        RaisePropertyChanged(nameof(MatrixSelectionStatus));

                        Debug.WriteLine($"[DisconnectAllAsync] UI状态更新完成");
                    });

                    // 13. 保存配置
                    SaveDeviceConfig();

                    Debug.WriteLine($"[SwitchControl] 所有硬件和软件连接已断开，ActiveRelayCount: {ActiveRelayCount}");
                }
                else
                {
                    Debug.WriteLine($"[DisconnectAllAsync] 硬件断开失败");
                    ReMessageBox.Show("断开所有硬件连接失败",
                        "断开失败",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DisconnectAllAsync] 断开所有连接异常: {ex.Message}");
                ReMessageBox.Show($"断开所有连接失败: {ex.Message}", "断开错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task RefreshConnectionStatusAsync()
        {
            if (_driver == null || !IsDeviceConnected) return;

            try
            {
                // 移除模拟驱动类型判断，直接使用硬件驱动刷新逻辑
                await RefreshHardwareDriverStatusAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SwitchControl] 刷新连接状态失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 刷新模拟驱动的连接状态
        /// </summary>
        private async Task RefreshSimulatedDriverStatusAsync()
        {
            Debug.WriteLine($"[SwitchControl] 模拟开关矩阵：信任软件配置，完全跳过硬件检查");

            // 完全信任软件配置，不进行任何硬件检查
            // 这样可以避免硬件模拟问题导致连接状态被错误重置

            // 可选的：只记录日志，不做任何状态修改
            try
            {
                bool isAlive = await _driver.SelfTestAsync();
                if (!isAlive)
                {
                    Debug.WriteLine($"[SwitchControl] 硬件离线警告，但保持软件连接状态不变");
                    // 只记录警告，不修改连接状态
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SwitchControl] 硬件检查异常，保持软件状态: {ex.Message}");
            }

            // 确保UI状态正确（基于软件配置）
            Dispatcher.Invoke(() =>
            {
                // 只刷新显示，不修改配置
                UpdateAllRelayStatus();
                UpdateConnectionCounts();
                RefreshTopologyVisualization();
                UpdateCrossPointsConnectionStatus();
                UpdateMatrixNodesConnectionStatus();
            });
        }

        // 1. 修改状态刷新逻辑，不检查硬件连接
        private async Task RefreshHardwareDriverStatusAsync()
        {
            Debug.WriteLine($"[SwitchControl] 开关矩阵：信任软件配置，完全跳过硬件检查");

            // 完全信任软件配置，不进行任何硬件检查
            // 这样可以避免硬件问题导致连接状态被错误重置

            // 可选的：只记录日志，不做任何状态修改
            try
            {
                bool isAlive = await _driver.SelfTestAsync();
                if (!isAlive)
                {
                    Debug.WriteLine($"[SwitchControl] 硬件离线警告，但保持软件连接状态不变");
                    // 只记录警告，不修改连接状态
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SwitchControl] 硬件检查异常，保持软件状态: {ex.Message}");
            }

            // 确保UI状态正确（基于软件配置）
            Dispatcher.Invoke(() =>
            {
                // 只刷新显示，不修改配置
                UpdateAllRelayStatus();
                UpdateConnectionCounts();
                RefreshTopologyVisualization();
                UpdateCrossPointsConnectionStatus();
                UpdateMatrixNodesConnectionStatus();
            });
        }

        private void ResetConnectionCounters()
        {
            if (_cardConfig == null) return;

            _cardConfig.ResetConnectionCounts();
            UpdateAllRelayStatus();
            UpdateConnectionCounts();
            SaveDeviceConfig();

            ReMessageBox.Show("所有连接计数器已重置", "重置成功",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        #endregion

        #region 矩阵拓扑布局方法

        private void RefreshMatrixTopology()
        {
            if (_cardConfig == null) return;

            // 使用当前的拓扑参数，而不是依赖TopologyConfigs
            // var topology = TopologyConfigs.FirstOrDefault(t => t.Name == SelectedTopology);
            // if (topology == null) return;

            // 清空集合
            CrossPoints.Clear();
            VerticalLines.Clear();
            HorizontalLines.Clear();
            InputLabels.Clear();
            OutputLabels.Clear();
            MatrixNodes.Clear();

            // 根据输出数量动态计算画布宽度，将间距进一步缩小
            double minVerticalSpacing = 20; // 进一步减小最小垂直间距
            double minHorizontalSpacing = 25; // 进一步减小最小水平间距

            double marginLeft = 20; // 进一步减小左侧边距
            double marginRight = 10; // 进一步减小右侧边距
            double marginTop = 30; // 保持上边距
            double marginBottom = 30; // 保持下边距

            double extensionLength = 15; // 缩短延长线长度
            double nodeRadius = 10;

            // 如果有可用空间，根据可用空间计算间距
            double availableWidth = AvailableWidth;
            double availableHeight = AvailableHeight;
            double horizontalSpacing = minHorizontalSpacing;
            double verticalSpacing = minVerticalSpacing;

            if (availableWidth > 0 && availableHeight > 0)
            {
                // 使用可用空间作为画布尺寸
                CanvasWidth = availableWidth;
                CanvasHeight = availableHeight;

                // 计算可用的网格空间
                double availableGridWidth = CanvasWidth - marginLeft - marginRight - extensionLength - nodeRadius * 2;
                double availableGridHeight = CanvasHeight - marginTop - marginBottom - extensionLength - nodeRadius * 2;

                // 计算基于可用空间的最佳间距，确保所有节点都能显示
                if (CurrentOutputCount > 1)
                {
                    // 确保水平间距足够小，以容纳所有输出节点
                    horizontalSpacing = availableGridWidth / (CurrentOutputCount - 1);
                    // 如果计算出的间距小于最小间距，使用最小间距但调整画布宽度
                    if (horizontalSpacing < minHorizontalSpacing)
                    {
                        horizontalSpacing = minHorizontalSpacing;
                        // 计算所需的最小画布宽度
                        double requiredWidth = marginLeft + marginRight + (CurrentOutputCount - 1) * horizontalSpacing + extensionLength + nodeRadius * 2;
                        // 如果所需宽度超过可用宽度，按比例缩小水平间距
                        if (requiredWidth > availableWidth)
                        {
                            horizontalSpacing = (availableWidth - marginLeft - marginRight - extensionLength - nodeRadius * 2) / (CurrentOutputCount - 1);
                        }
                    }
                }

                if (CurrentInputCount > 1)
                {
                    // 确保垂直间距足够小，以容纳所有输入节点
                    verticalSpacing = availableGridHeight / (CurrentInputCount - 1);
                    // 如果计算出的间距小于最小间距，使用最小间距但调整画布高度
                    if (verticalSpacing < minVerticalSpacing)
                    {
                        verticalSpacing = minVerticalSpacing;
                        // 计算所需的最小画布高度
                        double requiredHeight = marginTop + marginBottom + (CurrentInputCount - 1) * verticalSpacing + extensionLength + nodeRadius * 2;
                        // 如果所需高度超过可用高度，按比例缩小垂直间距
                        if (requiredHeight > availableHeight)
                        {
                            verticalSpacing = (availableHeight - marginTop - marginBottom - extensionLength - nodeRadius * 2) / (CurrentInputCount - 1);
                        }
                    }
                }
            }
            else
            {
                // 使用默认计算方式作为 fallback
                // 使用实际的水平间距来计算初始宽度，而不是minVerticalSpacing
                double requiredWidth = marginLeft + marginRight + (CurrentOutputCount - 1) * minHorizontalSpacing + extensionLength + nodeRadius * 2;

                CanvasWidth = Math.Max(800, requiredWidth); // 减小画布的最小宽度限制，缩小整体留白
                CanvasHeight = 700;

                // 设置水平间距为固定值，垂直间距为水平间距的2.0倍（缩小垂直距离）
                horizontalSpacing = minHorizontalSpacing; // 使用设置的50作为固定水平间距
                verticalSpacing = horizontalSpacing * 2.0; // 垂直间距为水平间距的2.0倍，垂直距离缩小
            }

            // 根据计算出的间距调整画布高度和宽度
            // 水平线（输入线）之间的距离是垂直间距，所以画布高度使用垂直间距
            double calculatedHeight = marginTop + marginBottom + extensionLength + nodeRadius * 2 + (CurrentInputCount - 1) * verticalSpacing;
            // 垂直线（输出线）之间的距离是水平间距，所以画布宽度使用水平间距
            double calculatedWidth = marginLeft + marginRight + (CurrentOutputCount - 1) * horizontalSpacing + extensionLength + nodeRadius * 2;

            // 确保画布尺寸足够大以容纳所有连接点
            CanvasWidth = calculatedWidth;
            CanvasHeight = calculatedHeight;

            // 重新计算网格尺寸
            double gridWidth = CanvasWidth - marginLeft - marginRight - extensionLength - nodeRadius * 2;
            double gridHeight = CanvasHeight - marginTop - marginBottom - extensionLength - nodeRadius * 2;

            // 确保网格尺寸不为负数
            gridWidth = Math.Max(gridWidth, 0);
            gridHeight = Math.Max(gridHeight, 0);

            // 创建水平线（输入线）
            for (int i = 0; i < CurrentInputCount; i++)
            {
                double y = marginTop + extensionLength + nodeRadius + i * verticalSpacing;

                var horizontalLine = new LineViewModel(
                    marginLeft + extensionLength + nodeRadius, y,
                    marginLeft + extensionLength + nodeRadius + gridWidth, y,
                    "Horizontal"
                );
                HorizontalLines.Add(horizontalLine);
            }

            // 创建垂直线（输出线）
            for (int j = 0; j < CurrentOutputCount; j++)
            {
                double x = marginLeft + extensionLength + nodeRadius + j * horizontalSpacing;

                var verticalLine = new LineViewModel(
                    x, marginTop + extensionLength + nodeRadius,
                    x, marginTop + extensionLength + nodeRadius + gridHeight,
                    "Vertical"
                );
                VerticalLines.Add(verticalLine);
            }

            // 创建输入节点和延长线
            for (int i = 0; i < CurrentInputCount; i++)
            {
                double y = marginTop + extensionLength + nodeRadius + i * verticalSpacing;

                double lineStartX = marginLeft;
                double lineEndX = marginLeft + extensionLength;
                double circleX = lineStartX - nodeRadius;

                // 创建输入节点的延长线
                var inputExtensionLine = new LineViewModel(
                    lineStartX, y,
                    lineEndX, y,
                    "Extension"
                );
                HorizontalLines.Add(inputExtensionLine);

                var inputNode = new MatrixNodeViewModel($"r{i}", "Input",
                    lineStartX, y)
                {
                    IsConnected = _cardConfig.IsInputConnected($"r{i}"),
                    DisplayX = circleX,
                    DisplayY = y - nodeRadius,
                    NodeColor = "#2196F3",
                    Radius = nodeRadius * 2
                };
                MatrixNodes.Add(inputNode);

                // 将标签直接显示在连接点旁，尽可能靠近节点
                var inputLabel = new LabelViewModel(
                    $"r{i}",
                    circleX - 5,  // 调整位置，更靠近节点
                    y - 7,
                    9,
                    "Normal"
                );
                InputLabels.Add(inputLabel);
            }

            // 创建输出节点和延长线
            for (int j = 0; j < CurrentOutputCount; j++)
            {
                double x = marginLeft + extensionLength + nodeRadius + j * horizontalSpacing;

                double lineStartY = marginTop;
                double lineEndY = marginTop + extensionLength;
                double circleY = lineStartY - nodeRadius;

                // 创建输出节点的延长线
                var outputExtensionLine = new LineViewModel(
                    x, lineStartY,
                    x, lineEndY,
                    "Extension"
                );
                VerticalLines.Add(outputExtensionLine);

                var outputNode = new MatrixNodeViewModel($"c{j}", "Output",
                    x, lineStartY)
                {
                    IsConnected = _cardConfig.IsOutputConnected($"c{j}"),
                    DisplayX = x - nodeRadius,
                    DisplayY = circleY,
                    NodeColor = "#F44336",
                    Radius = nodeRadius * 2
                };
                MatrixNodes.Add(outputNode);

                // 显示所有输出标签，直接显示在节点旁，尽可能靠近节点
                var outputLabel = new LabelViewModel(
                    $"c{j}",
                    x - 6,  // 调整位置，更靠近节点
                    circleY - 5,  // 调整位置，更靠近节点
                    9,
                    "Normal"
                );
                OutputLabels.Add(outputLabel);
            }

            // 创建交叉点网格
            for (int i = 0; i < CurrentInputCount; i++)
            {
                for (int j = 0; j < CurrentOutputCount; j++)
                {
                    // x坐标使用水平间距
                    double x = marginLeft + extensionLength + nodeRadius + j * horizontalSpacing;
                    // y坐标使用垂直间距
                    double y = marginTop + extensionLength + nodeRadius + i * verticalSpacing;

                    var crossPoint = new CrossPointViewModel(
                        $"CP_{i}_{j}",
                        $"r{i}",
                        $"c{j}",
                        x - 8,
                        y - 8,
                        $"r{i} ↔ c{j}"
                    )
                    {
                        IsConnected = _cardConfig.GetConnection($"r{i}", $"c{j}")?.State == SwitchConnectionState.Connected,
                        Size = 16
                    };

                    CrossPoints.Add(crossPoint);
                }
            }

            // 更新交叉点的连接状态
            UpdateCrossPointsConnectionStatus();
        }

        private void UpdateCrossPointsConnectionStatus()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(UpdateCrossPointsConnectionStatus);
                return;
            }

            if (_cardConfig == null)
            {
                Debug.WriteLine("[UpdateCrossPointsConnectionStatus] 警告：cardConfig为空");
                return;
            }

            Debug.WriteLine($"[UpdateCrossPointsConnectionStatus] 开始更新交叉点状态，总数: {CrossPoints.Count}");

            int connectedCount = 0;
            int disconnectedCount = 0;

            foreach (var crossPoint in CrossPoints)
            {
                var connection = _cardConfig.GetConnection(crossPoint.InputNodeId, crossPoint.OutputNodeId);
                var state = connection?.State ?? SwitchConnectionState.Disconnected;
                bool isConnected = state == SwitchConnectionState.Connected;

                if (isConnected) connectedCount++;
                else disconnectedCount++;

                crossPoint.IsConnected = isConnected;

                if (state == SwitchConnectionState.Connected)
                {
                    crossPoint.ConnectionColor = string.IsNullOrWhiteSpace(connection?.ConnectionColor)
                        ? "#4CAF50"
                        : connection.ConnectionColor;
                }
                else if (state == SwitchConnectionState.Error)
                {
                    crossPoint.ConnectionColor = string.IsNullOrWhiteSpace(connection?.ConnectionColor)
                        ? "#F44336"
                        : connection.ConnectionColor;
                }
                else
                {
                    if (!crossPoint.IsPendingConnection)
                    {
                        crossPoint.ConnectionColor = null;
                    }
                }
            }

            Debug.WriteLine($"[UpdateCrossPointsConnectionStatus] 完成更新: 已连接 {connectedCount}, 未连接 {disconnectedCount}");
        }

        /// <summary>
        /// 异步更新所有交叉点状态（用于后台更新，避免阻塞UI）
        /// </summary>
        private async Task UpdateCrossPointsConnectionStatusAsync()
        {
            await Task.Run(() =>
            {
                if (_cardConfig == null) return;

                Dispatcher.Invoke(() =>
                {
                    UpdateCrossPointsConnectionStatus();
                });
            });
        }

        #endregion

        private void InitializeCollections()
        {
            InputChannels = new ObservableCollection<string>();
            OutputChannels = new ObservableCollection<string>();
            RelayStatusList = new ObservableCollection<RelayStatusInfo>();
            TopologyConfigs = new ObservableCollection<TopologyConfig>();
            ActiveConnections = new ObservableCollection<MatrixConnection>();
            TopologyNodes = new ObservableCollection<TopologyNodeInfo>();
            TopologyConnections = new ObservableCollection<TopologyConnectionInfo>();
            MatrixNodes = new ObservableCollection<MatrixNodeViewModel>();

            OutputNodes = new ObservableCollection<MatrixNodeViewModel>();
            InputNodes = new ObservableCollection<MatrixNodeViewModel>();
            CrossPoints = new ObservableCollection<CrossPointViewModel>();
            VerticalLines = new ObservableCollection<LineViewModel>();
            HorizontalLines = new ObservableCollection<LineViewModel>();
            MatrixConnections = new ObservableCollection<MatrixConnectionViewModel>();
            InputLabels = new ObservableCollection<LabelViewModel>();
            OutputLabels = new ObservableCollection<LabelViewModel>();
        }

        private void InitializeCommands()
        {
            ToggleDeviceCommand = new DelegateCommand(async () => await ToggleDeviceAsync());
            DisconnectAllCommand = new DelegateCommand(async () => await DisconnectAllAsync(), () => IsDeviceConnected)
                .ObservesProperty(() => IsDeviceConnected);
            RefreshTopologyCommand = new DelegateCommand(RefreshMatrixTopology);
            ResetCountersCommand = new DelegateCommand(ResetConnectionCounters);
            ClearErrorsCommand = new DelegateCommand(ClearAllErrors);

            ConnectCrossPointCommand = new DelegateCommand<CrossPointViewModel>(async cp =>
            {
                if (cp == null) return;
                await ConnectNodesAsync(cp.InputNodeId, cp.OutputNodeId);
            });

            DisconnectCrossPointCommand = new DelegateCommand<CrossPointViewModel>(async cp =>
            {
                if (cp == null) return;
                await DisconnectNodesAsync(cp.InputNodeId, cp.OutputNodeId);
            });
        }

        private void InitializeTopologyConfigs()
        {
            if (TopologyConfigs == null) return;

            TopologyConfigs.Clear();
            TopologyConfigs.Add(new TopologyConfig
            {
                Name = "4x32 Matrix",
                TopologyString = artSwitchTopologies.ARTSWITCH_TOPOLOGY_2601_2_WIRE_4X32_MATRIX,
                InputCount = 4,
                OutputCount = 32,
                Description = string.Empty
            });

            TopologyConfigs.Add(new TopologyConfig
            {
                Name = "8x16 Matrix",
                TopologyString = artSwitchTopologies.ARTSWITCH_TOPOLOGY_2601_2_WIRE_8X16_MATRIX,
                InputCount = 8,
                OutputCount = 16,
                Description = ""
            });

            if (TopologyConfigs.Count > 0)
            {
                SelectedTopology = TopologyConfigs[0].Name;
            }
        }

        private void OnTopologyChanged()
        {
            var topology = TopologyConfigs?.FirstOrDefault(t => t.Name == SelectedTopology);
            if (topology == null) return;

            CurrentInputCount = topology.InputCount;
            CurrentOutputCount = topology.OutputCount;
            CurrentCrossPointCount = topology.InputCount * topology.OutputCount;
            InputLineText = $"输入线 ({topology.InputCount}条)";
            OutputLineText = $"输出线 ({topology.OutputCount}条)";

            RaisePropertyChanged(nameof(MatrixStatisticsInfo));

            RefreshMatrixTopology();
        }

        private async Task ToggleDeviceAsync()//打开板卡，关闭板卡
        {
            if (IsRemoteChassis)
            {
                if (IsDeviceOpened)
                {
                    await DisconnectDeviceAsync();
                    IsDeviceOpened = false;
                }
                else
                {
                    IsDeviceOpened = true;
                }

                RaisePropertyChanged(nameof(ConnectButtonText));
                RaisePropertyChanged(nameof(MatrixSelectionStatus));
                return;
            }

            if (IsDeviceConnected)
            {
                await DisconnectDeviceAsync();
                IsDeviceOpened = false;
            }
            else
            {
                await ConnectDeviceAsync();
                IsDeviceOpened = IsDeviceConnected;
            }

            RaisePropertyChanged(nameof(ConnectButtonText));
            RaisePropertyChanged(nameof(MatrixSelectionStatus));
        }

        internal void LoadDeviceConfig()
        {
            // 尝试从服务中获取权威的 Device 实例，确保我们使用的是服务中保存的对象（这样在机箱回退执行后保存的 CardConfigData 能被正确读取）
            try
            {
                if (!string.IsNullOrEmpty(Device?.Id) && _pxiChassisService != null)
                {
                    var svcDevice = _pxiChassisService.GetDeviceById(Device.Id);
                    if (svcDevice != null && !ReferenceEquals(svcDevice, Device))
                    {
                        Device = svcDevice;
                    }
                }
            }
            catch { }

            // 注册 dispatcher handler（如果尚未注册或槽位发生变化）
            try
            {
                int newSlot = (Device as MeasureControl.Models.Devices.DeviceCategories.PxiDeviceBase)?.SlotIndex ?? -1;
                if (newSlot > 0 && newSlot != _registeredSlot)
                {
                    // 取消之前的注册
                    if (_registeredSlot > 0)
                    {
                        try { MeasureControl.Services.RemoteMatrixCommandDispatcher.Instance.Unregister(_registeredSlot); } catch { }
                    }

                    // 注册远程命令处理器
                    MeasureControl.Services.RemoteMatrixCommandDispatcher.Instance.Register(newSlot, async (args) =>
                    {
                        try
                        {
                            if (args.State == 0)
                            {
                                await ConnectTcpNodesAsync(args.InputNodeId, args.OutputNodeId).ConfigureAwait(false);
                            }
                            else
                            {
                                await DisconnectNodesAsync(args.InputNodeId, args.OutputNodeId).ConfigureAwait(false);
                            }
                            return true;
                        }
                        catch
                        {
                            return false;
                        }
                    });
                    _registeredSlot = newSlot;
                }
            }
            catch { }

            // 简单直接：从设备获取配置，如果没有就创建新的
            if (Device?.CardConfigData is SwitchMatrixCardConfig existingConfig)
            {
                _cardConfig = existingConfig;
                Debug.WriteLine($"[LoadDeviceConfig] 使用设备配置，连接数: {_cardConfig.ConnectionMap.Count}");
            }
            else
            {
                _cardConfig = new SwitchMatrixCardConfig();
                if (Device != null)
                {
                    Device.CardConfigData = _cardConfig;
                }
                Debug.WriteLine($"[LoadDeviceConfig] 创建新配置");
            }

            UpdateAllRelayStatus();
            UpdateActiveConnections();
            UpdateConnectionCounts();
            RefreshMatrixTopology();

            // 延迟执行一次完整的UI状态同步，确保所有连接状态都正确显示
            _ = Task.Delay(100).ContinueWith(_ =>
            {
                Dispatcher.Invoke(() =>
                {
                    UpdateCrossPointsConnectionStatus();
                    UpdateMatrixNodesConnectionStatus();
                    UpdateActiveConnections();
                    UpdateConnectionCounts();
                    RaisePropertyChanged(nameof(ActiveConnections));
                    RaisePropertyChanged(nameof(MatrixSelectionStatus));

                    // 再次刷新拓扑以确保所有状态正确显示
                    RefreshMatrixTopology();
                });
            });
        }

        private void SaveDeviceConfig()
        {
            if (Device == null || _cardConfig == null) return;

            // 确保使用服务中的权威Device实例
            var authoritativeDevice = Device;
            if (!string.IsNullOrEmpty(Device?.Id) && _pxiChassisService != null)
            {
                var svcDevice = _pxiChassisService.GetDeviceById(Device.Id);
                if (svcDevice != null)
                {
                    authoritativeDevice = svcDevice;
                }
            }

            authoritativeDevice.CardConfigData = _cardConfig;
            _pxiChassisService?.UpdateDeviceCardConfig(Device.Id, _cardConfig);
            _eventAggregator?.GetEvent<ProjectModifiedEvent>()?.Publish(new ProjectModifiedEventArgs
            {
                ModificationType = "PXI2601MatrixConfig",
                Description = $"PXI2601矩阵配置已更新: {CardName}"
            });
        }

        private void UpdateAllRelayStatus()
        {
            if (_cardConfig == null || RelayStatusList == null) return;

            if (RelayStatusList.Count == 0)
            {
                foreach (var conn in _cardConfig.ConnectionMap.Values)
                {
                    RelayStatusList.Add(new RelayStatusInfo(conn));
                }
            }

            foreach (var relay in RelayStatusList)
            {
                relay.UpdateFromConnection();
            }

            RaisePropertyChanged(nameof(RelayStatusList));
        }

        private void UpdateActiveConnections()
        {
            if (ActiveConnections == null) return;
            ActiveConnections.Clear();

            var list = _cardConfig?.GetAllActiveConnections() ?? new List<MatrixConnection>();
            foreach (var conn in list)
            {
                ActiveConnections.Add(conn);
            }

            RaisePropertyChanged(nameof(ActiveConnections));
        }

        private void UpdateConnectionCounts()
        {
            // UpdateConnectionCounts 方法只需要确保连接计数被正确计算
            // UI 绑定会通过其他途径获取这些信息
            // 这里不需要设置不存在的属性
        }

        private void RefreshTopologyVisualization()
        {
            TopologyNodes?.Clear();
            TopologyConnections?.Clear();
            RaisePropertyChanged(nameof(TopologyNodes));
            RaisePropertyChanged(nameof(TopologyConnections));
        }

        private void UpdateRelayStatusInUI(string inputNodeId, string outputNodeId)
        {
            UpdateAllRelayStatus();
        }

        private void UpdateRelayStatus(string inputNodeId, string outputNodeId)
        {
            UpdateAllRelayStatus();
        }

        /// <summary>
        /// 快速更新单个交叉点状态（优化版本，避免遍历所有交叉点）
        /// </summary>
        private void UpdateCrossPointStatus(string inputNodeId, string outputNodeId)
        {
            var crossPoint = CrossPoints.FirstOrDefault(cp =>
                cp.InputNodeId == inputNodeId && cp.OutputNodeId == outputNodeId);

            if (crossPoint != null)
            {
                var connection = _cardConfig.GetConnection(inputNodeId, outputNodeId);
                var state = connection?.State ?? SwitchConnectionState.Disconnected;
                bool isConnected = state == SwitchConnectionState.Connected;

                crossPoint.IsConnected = isConnected;
                crossPoint.IsPendingConnection = false;

                if (state == SwitchConnectionState.Connected)
                {
                    crossPoint.ConnectionColor = string.IsNullOrWhiteSpace(connection?.ConnectionColor)
                        ? "#4CAF50"
                        : connection.ConnectionColor;
                }
                else if (state == SwitchConnectionState.Error)
                {
                    crossPoint.ConnectionColor = "#F44336";
                }
                else
                {
                    crossPoint.ConnectionColor = null;
                }
            }
        }

        private void UpdateMatrixNodesConnectionStatus()
        {
            if (_cardConfig == null || MatrixNodes == null) return;

            foreach (var node in MatrixNodes)
            {
                bool isConnected;
                if (node.NodeType == "Input")
                {
                    isConnected = _cardConfig.ConnectionMap.Values.Any(c => c.State == SwitchConnectionState.Connected && c.InputChannel == node.NodeId);
                }
                else
                {
                    isConnected = _cardConfig.ConnectionMap.Values.Any(c => c.State == SwitchConnectionState.Connected && c.OutputChannel == node.NodeId);
                }

                node.IsConnected = isConnected;
            }

            RaisePropertyChanged(nameof(MatrixNodes));
        }

        private void ClearAllSelectionStates()
        {
            SelectedInputNode = null;
            SelectedOutputNode = null;
            SelectedCrossPoint = null;
            FirstSelectedNode = null;
            HoveredNode = null;
            RaisePropertyChanged(nameof(MatrixSelectionStatus));
        }

        private void ClearAllErrors()
        {
            if (_cardConfig == null) return;

            foreach (var conn in _cardConfig.ConnectionMap.Values)
            {
                if (conn.State == SwitchConnectionState.Error)
                {
                    conn.SetConnectionState(SwitchConnectionState.Disconnected);
                }
            }

            UpdateAllRelayStatus();
            UpdateActiveConnections();
            UpdateConnectionCounts();
            RefreshTopologyVisualization();
            UpdateCrossPointsConnectionStatus();
            UpdateMatrixNodesConnectionStatus();
            SaveDeviceConfig();
        }

        private void StartStatusTimer()
        {
            if (_statusTimer == null)
            {
                _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                _statusTimer.Tick += StatusTimer_Tick;
            }

            _statusTimer.Start();
        }

        private void StopStatusTimer()
        {
            _statusTimer?.Stop();
        }

        private async void StatusTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                if (!IsDeviceConnected) return;
                if (IsRemoteChassis) return;
                await RefreshConnectionStatusAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[StatusTimer_Tick] 异常: {ex.Message}");
            }
        }

        private int GetTcpListenPort()
        {
            int slotIndex = 0;
            try
            {
                if (Device is MeasureControl.Models.Devices.DeviceCategories.PxiDeviceBase pxiDevice && pxiDevice.SlotIndex > 0)
                {
                    slotIndex = pxiDevice.SlotIndex;
                }
                else
                {
                    var match = Regex.Match(Device?.SlotPosition ?? string.Empty, "(\\d+)");
                    if (match.Success) int.TryParse(match.Groups[1].Value, out slotIndex);
                }
            }
            catch
            {
                slotIndex = 0;
            }

            if (slotIndex <= 0)
            {
                // 兜底：如果槽位信息缺失，用稳定 hash 做唯一端口（同一设备两端一致）
                int hash = Device?.Id?.GetHashCode() ?? 0;
                slotIndex = Math.Abs(hash % 1000) + 1;
            }

            int port = TcpBasePort2601 + slotIndex;
            Debug.WriteLine($"[GetTcpListenPort] DeviceId={Device?.Id}, SlotIndex={slotIndex}, SlotPosition={Device?.SlotPosition}, Port={port}");
            return port;
        }

        /// <summary>
        /// 启动指定端口的TCP服务器
        /// </summary>
        private void StartTcpServerForPort(int port, string boardIdentifier)
        {
            try
            {
                bool ok = TcpServerManager.Instance.Start(port, boardIdentifier, (client, serverInfo, token) =>
                {
                    return HandleClientAsync(client, serverInfo, token);
                });

                if (ok)
                {
                    _ownedTcpServerIdentifiers.Add(boardIdentifier);
                    Debug.WriteLine($"[StartTcpServerForPort] (via manager) 启动或复用 TCP 服务器: 端口={port}, 板卡={boardIdentifier}");
                    File.AppendAllText(@"C:\LOG\LOG.TXT", $"StartTcpServerForPort: 端口={port}, 板卡={boardIdentifier}\n");
                }
                else
                {
                    Debug.WriteLine($"[StartTcpServerForPort] (via manager) 启动失败: 端口={port}, 板卡={boardIdentifier}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[StartTcpServerForPort] 启动失败: {ex.Message}");
                try { TcpServerManager.Instance.Stop(boardIdentifier); } catch { }
            }
        }

        private async Task AcceptLoopAsync(TcpServerInfo serverInfo, CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    TcpClient client = null;
                    try
                    {
                        var acceptTask = serverInfo.Listener.AcceptTcpClientAsync();
                        var completed = await Task.WhenAny(acceptTask, Task.Delay(Timeout.Infinite, token));
                        if (completed != acceptTask)
                            break;

                        client = acceptTask.Result;
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (ObjectDisposedException)
                    {
                        break;
                    }
                    catch (InvalidOperationException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[AcceptLoopAsync] 异常: {ex.Message}");
                        continue;
                    }

                    _ = Task.Run(() => HandleClientAsync(client, serverInfo, token));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AcceptLoopAsync] 循环异常: {ex.Message}");
            }
        }

        private static async Task<int> ReadExactAsync(Stream stream, byte[] buffer, int offset, int count, CancellationToken token)
        {
            int totalRead = 0;
            while (totalRead < count && !token.IsCancellationRequested)
            {
                int read = await stream.ReadAsync(buffer, offset + totalRead, count - totalRead, token);
                if (read <= 0)
                    break;

                totalRead += read;
            }

            return totalRead;
        }

        private async Task HandleClientAsync(TcpClient client, TcpServerInfo serverInfo, CancellationToken token)
        {

            File.AppendAllText(@"C:\LOG\LOG.TXT", "HandleClientAsync+ FFFF" + Environment.NewLine);

            try
            {
                if (client == null) return;
                Debug.WriteLine($"[HandleClientAsync] Accepted Remote={client.Client?.RemoteEndPoint} Local={client.Client?.LocalEndPoint} Board={serverInfo?.BoardIdentifier} Port={serverInfo?.Port}");
                using (var stream = client.GetStream())
                {
                    var cmd = new byte[3];

                    while (!token.IsCancellationRequested)
                    {
                        int read = await ReadExactAsync(stream, cmd, 0, cmd.Length, token);
                        if (read != cmd.Length) continue;

                        byte inputIndex = cmd[0];
                        byte outputIndex = cmd[1];
                        byte state = cmd[2];

                        Debug.WriteLine($"[HandleClientAsync] RX({serverInfo?.Port}): {BitConverter.ToString(cmd)} => r{inputIndex},c{outputIndex},state={state}");

                        File.AppendAllText(@"C:\LOG\LOG.TXT", "1111" + Environment.NewLine);

                        string inputNodeId = "r" + inputIndex;
                        string outputNodeId = "c" + outputIndex;

                        bool ok = true;
                        if (inputIndex == 0xFF)
                        {
                            await _remoteCommandLock.WaitAsync(token);
                            try
                            {
                                if (state == 0)
                                {
                                    await DisconnectDeviceAsync();
                                    ok = !IsDeviceConnected;
                                }
                                else if (state == 1)
                                {
                                    await ConnectDeviceAsync();
                                    ok = IsDeviceConnected;
                                }
                                else
                                {
                                    ok = false;
                                }
                            }
                            catch
                            {
                                ok = false;
                            }
                            finally
                            {
                                _remoteCommandLock.Release();
                            }
                        }
                        else if (state == 0)
                        {
                            File.AppendAllText(@"C:\LOG\LOG.TXT", "2222" + Environment.NewLine);
                            await _remoteCommandLock.WaitAsync(token);
                            try
                            {
                                ok = await ConnectTcpNodesAsync(inputNodeId, outputNodeId);
                            }
                            finally
                            {
                                _remoteCommandLock.Release();
                            }
                        }
                        else if (state == 1)
                        {
                            await _remoteCommandLock.WaitAsync(token);
                            try
                            {
                                ok = await DisconnectNodesAsync(inputNodeId, outputNodeId);
                            }
                            finally
                            {
                                _remoteCommandLock.Release();
                            }
                        }
                        else
                        {
                            ok = false;
                        }

                        var ack = ok ? cmd : new[] { cmd[0], cmd[1], (byte)(cmd[2] ^ 0xFF) };

                        // ACK: 回包与请求一致，供 client 做完整性校验
                        await stream.WriteAsync(ack, 0, ack.Length);
                        await stream.FlushAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HandleClientAsync] 异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 停止指定板卡的TCP服务器
        /// </summary>
        private void StopTcpServer(string boardIdentifier)
        {
            try
            {
                TcpServerManager.Instance.Stop(boardIdentifier);
                _ownedTcpServerIdentifiers.Remove(boardIdentifier);
                Debug.WriteLine($"[StopTcpServer] (via manager) 停止/减少引用: {boardIdentifier}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[StopTcpServer] 停止失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 停止所有TCP服务器
        /// </summary>
        private void StopAllTcpServers()
        {
            try
            {
                Debug.WriteLine("[StopAllTcpServers] 停止当前实例启动的TCP服务器");

                var boardIdentifiers = _ownedTcpServerIdentifiers.ToList();
                foreach (var boardIdentifier in boardIdentifiers)
                {
                    StopTcpServer(boardIdentifier);
                }

                _ownedTcpServerIdentifiers.Clear();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[StopAllTcpServers] 停止失败: {ex.Message}");
            }
        }

        private async Task<bool> DisconnectNodesAsync(string inputNodeId, string outputNodeId)
        {
            if (_cardConfig == null) return false;

            if (IsRemoteChassis)
            {
                bool success = await SendMatrixCommandAsync(inputNodeId, outputNodeId, RemoteCommandDisconnect);
                if (success)
                {
                    _cardConfig.SetConnection(inputNodeId, outputNodeId, SwitchConnectionState.Disconnected);
                    await Dispatcher.InvokeAsync(() =>
                    {
                        UpdateRelayStatusInUI(inputNodeId, outputNodeId);
                        UpdateActiveConnections();
                        UpdateConnectionCounts();
                        RefreshTopologyVisualization();
                        UpdateCrossPointsConnectionStatus();
                        UpdateMatrixNodesConnectionStatus();
                        RaisePropertyChanged(nameof(RelayStatusList));
                    });
                    SaveDeviceConfig();
                }
                return success;
            }

            if (_driver == null || !IsDeviceConnected) return false;
            bool ok = await _driver.DisconnectChannelsAsync(outputNodeId, inputNodeId);
            if (ok)
            {
                _cardConfig.SetConnection(inputNodeId, outputNodeId, SwitchConnectionState.Disconnected);
                Dispatcher.Invoke(() =>
                {
                    UpdateRelayStatusInUI(inputNodeId, outputNodeId);
                    UpdateActiveConnections();
                    UpdateConnectionCounts();
                    RefreshTopologyVisualization();
                    UpdateCrossPointsConnectionStatus();
                    UpdateMatrixNodesConnectionStatus();
                });
                SaveDeviceConfig();
            }

            return ok;
        }

        #region Helper Method

        /// <summary>
        /// 高级颜色生成器
        /// </summary>
        public static class AdvancedColorGenerator
        {
            private static readonly Dictionary<string, string> ColorCache = new Dictionary<string, string>();
            private static readonly List<string> GeneratedColors = new List<string>();
            private static readonly Random Random = new Random();

            /// <summary>
            /// 为连接生成颜色（支持大量连接）
            /// </summary>
            public static string GenerateColorForConnection(string inputNodeId, string outputNodeId, int connectionIndex = -1)
            {
                // 固定返回绿色
                return "#4CAF50";
            }

            /// <summary>
            /// 基于索引生成颜色（顺序确定）
            /// </summary>
            private static string GenerateByIndex(int index, int totalGenerated)
            {
                double goldenAngle = 137.508;
                double hue = (index * goldenAngle) % 360;

                double saturation = 0.6 + (index % 3) * 0.1;
                double lightness = 0.5 + ((index / 3) % 3) * 0.1;

                return HslToHex(hue, saturation, lightness);
            }

            /// <summary>
            /// 基于哈希值生成颜色
            /// </summary>
            private static string GenerateByHash(int hash, int totalGenerated)
            {
                double hue = hash % 360;

                double baseSaturation = 0.7;
                double baseLightness = 0.55;

                for (int attempt = 0; attempt < 5; attempt++)
                {
                    double saturation = baseSaturation + (hash % 100) * 0.003;
                    double lightness = baseLightness + ((hash / 100) % 100) * 0.003;

                    string candidate = HslToHex(hue, saturation, lightness);

                    if (totalGenerated == 0 || GetMinColorDifference(candidate, GeneratedColors) > 30)
                        return candidate;

                    hue = (hue + 60) % 360;
                    hash = hash * 397;
                }

                return GenerateRandomDistinctColor();
            }

            /// <summary>
            /// 生成与已有颜色区分度高的随机颜色
            /// </summary>
            private static string GenerateRandomDistinctColor()
            {
                for (int attempt = 0; attempt < 100; attempt++)
                {
                    double hue = Random.NextDouble() * 360;
                    double saturation = 0.5 + Random.NextDouble() * 0.5;
                    double lightness = 0.4 + Random.NextDouble() * 0.4;

                    string candidate = HslToHex(hue, saturation, lightness);

                    if (GeneratedColors.Count == 0 || GetMinColorDifference(candidate, GeneratedColors) > 40)
                        return candidate;
                }

                return GetFallbackColor(GeneratedColors.Count);
            }

            /// <summary>
            /// 计算与已有颜色的最小差异
            /// </summary>
            private static double GetMinColorDifference(string newColor, List<string> existingColors)
            {
                if (existingColors.Count == 0) return double.MaxValue;

                var newRgb = HexToRgb(newColor);
                return existingColors
                    .Select(HexToRgb)
                    .Min(existing => ColorDifference(newRgb, existing));
            }

            /// <summary>
            /// HSL转HEX
            /// </summary>
            private static string HslToHex(double h, double s, double l)
            {
                double r, g, b;

                if (s == 0)
                {
                    r = g = b = l;
                }
                else
                {
                    double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
                    double p = 2 * l - q;

                    r = HueToRgb(p, q, h + 120);
                    g = HueToRgb(p, q, h);
                    b = HueToRgb(p, q, h - 120);
                }

                return $"#{(int)(r * 255):X2}{(int)(g * 255):X2}{(int)(b * 255):X2}";
            }

            private static double HueToRgb(double p, double q, double t)
            {
                if (t < 0) t += 360;
                if (t > 360) t -= 360;

                if (t < 60) return p + (q - p) * t / 60;
                if (t < 180) return q;
                if (t < 240) return p + (q - p) * (240 - t) / 60;
                return p;
            }

            /// <summary>
            /// 计算两个RGB颜色的差异
            /// </summary>
            private static double ColorDifference((byte R, byte G, byte B) c1, (byte R, byte G, byte B) c2)
            {
                double deltaR = c1.R - c2.R;
                double deltaG = c1.G - c2.G;
                double deltaB = c1.B - c2.B;

                return Math.Sqrt(deltaR * deltaR + deltaG * deltaG + deltaB * deltaB);
            }

            /// <summary>
            /// HEX转RGB
            /// </summary>
            private static (byte R, byte G, byte B) HexToRgb(string hex)
            {
                hex = hex.TrimStart('#');
                return (
                    Convert.ToByte(hex.Substring(0, 2), 16),
                    Convert.ToByte(hex.Substring(2, 2), 16),
                    Convert.ToByte(hex.Substring(4, 2), 16)
                );
            }

            /// <summary>
            /// 备用颜色（当算法无法生成足够区分的颜色时）
            /// </summary>
            private static string GetFallbackColor(int index)
            {
                string[] extendedColors =
                {
                    "#FF6B6B", "#4ECDC4", "#FFD166", "#06D6A0", "#118AB2", "#EF476F",
                    "#FFD166", "#06D6A0", "#073B4C", "#7209B7", "#9D4EDD", "#FF9E00",
                    "#00BBF9", "#00F5D4", "#FF0054", "#8338EC", "#3A86FF", "#FF006E",
                    "#FB5607", "#FFBE0B", "#264653", "#2A9D8F", "#E9C46A", "#F4A261",
                    "#E76F51", "#1D3557", "#457B9D", "#A8DADC", "#F1FAEE", "#E63946",
                    "#FFAFCC", "#CDB4DB", "#A2D2FF", "#BDE0FE", "#FFC8DD", "#FFAFCC",
                    "#390099", "#9E0059", "#FF0054", "#FF5400", "#FFBD00", "#38B000",
                    "#70E000", "#CCFF33", "#8AC926", "#6A994E", "#386641", "#2D936C",
                    "#9B5DE5", "#F15BB5", "#00BBF9", "#00F5D4", "#FEE440", "#9B5DE5",
                    "#F15BB5", "#00F5D4", "#00BBF9", "#FEE440", "#FF6F61", "#6B5B95"
                };

                return extendedColors[index % extendedColors.Length];
            }

            /// <summary>
            /// 清空颜色缓存（切换拓扑时使用）
            /// </summary>
            public static void ClearCache()
            {
                ColorCache.Clear();
                GeneratedColors.Clear();
            }
        }

        #endregion

        #region IDisposable

        private bool _disposed = false;
        private int _registeredSlot = -1;

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                try
                {
                    _eventAggregator?.GetEvent<MeasureControl.Events.RemoteMatrixCommandEvent>()?.Unsubscribe(OnRemoteMatrixCommand);
                }
                catch { }
                try
                {
                    _eventAggregator?.GetEvent<MeasureControl.Events.DeviceModifiedEvent>()?.Unsubscribe(OnDeviceModified);
                }
                catch { }
                try
                {
                    if (_registeredSlot > 0)
                        MeasureControl.Services.RemoteMatrixCommandDispatcher.Instance.Unregister(_registeredSlot);
                }
                catch { }
                SaveDeviceConfig();

                // 注意：TCP服务器由PxiChassisViewModel统一管理，ViewModel不应停止TCP服务器
                // 以避免引用计数混乱和意外断开连接
                //StopTcpServer();
                //StopAllTcpServers();
                StopStatusTimer();

                Task.Run(async () =>
                {
                    try
                    {
                        if (IsDeviceConnected)
                        {
                            if (IsRemoteChassis)
                            {
                                await DisconnectAllRemoteConnectionsAsync();
                                await SendRemoteDriverControlAsync(0);
                            }
                            else if (_driver != null)
                            {
                                if (!KeepMatrixConnectionOnClose)
                                {
                                    await DisconnectAllAsync();
                                    await DisconnectDeviceAsync();
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[SwitchControl] Dispose 异常: {ex.Message}");
                    }
                }).Wait(TimeSpan.FromSeconds(3));

                CleanupTcpConnection();
            }

            _disposed = true;
        }

        #endregion
    }
}