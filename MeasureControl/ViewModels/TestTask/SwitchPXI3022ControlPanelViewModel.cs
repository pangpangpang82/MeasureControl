using MeasureControl.Drivers;
using MeasureControl.Drivers.PXI3022;
using MeasureControl.Events;
using MeasureControl.Helpers;
using MeasureControl.Models;
using MeasureControl.Models.Devices;
using MeasureControl.Models.Devices.DeviceCategories;
using MeasureControl.Services;
using MeasureControl.Views;
using MeasureControl.Views.Dialogs;
using Prism.Commands;
using Prism.Events;
using Prism.Mvvm;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace MeasureControl.ViewModels.TestTask
{
    #region 枚举和模型类

    /// <summary>
    /// 开关连接状态枚举
    /// </summary>
    public enum SwitchConnectionState
    {
        /// <summary>
        /// 已连接
        /// </summary>
        Connected,

        /// <summary>
        /// 已断开
        /// </summary>
        Disconnected,

        /// <summary>
        /// 错误状态
        /// </summary>
        Error
    }

    /// <summary>
    /// 矩阵连接类
    /// </summary>
    public class MatrixConnection : BindableBase
    {
        private string _inputChannel;
        private string _outputChannel;
        private SwitchConnectionState _state;
        private string _stateColor;
        private int _connectionCount;
        private double _connectionDuration;
        private DateTime _lastConnectedTime;
        private DateTime _lastDisconnectedTime;
        private string _errorMessage;

        public string InputChannel
        {
            get => _inputChannel;
            set => SetProperty(ref _inputChannel, value);
        }

        public string OutputChannel
        {
            get => _outputChannel;
            set => SetProperty(ref _outputChannel, value);
        }

        public string RelayName => $"{InputChannel}->{OutputChannel}";

        public SwitchConnectionState State
        {
            get => _state;
            set => SetProperty(ref _state, value);
        }

        public string StateColor
        {
            get => _stateColor;
            set => SetProperty(ref _stateColor, value);
        }


        public string ConnectionColor
        {
            get => _stateColor;
            set => _stateColor = value;
        }

        public int ConnectionCount
        {
            get => _connectionCount;
            set => SetProperty(ref _connectionCount, value);
        }

        public double ConnectionDuration
        {
            get => _connectionDuration;
            set => SetProperty(ref _connectionDuration, value);
        }

        public DateTime LastConnectedTime
        {
            get => _lastConnectedTime;
            set => SetProperty(ref _lastConnectedTime, value);
        }

        public DateTime LastDisconnectedTime
        {
            get => _lastDisconnectedTime;
            set => SetProperty(ref _lastDisconnectedTime, value);
        }

        public string ErrorMessage
        {
            get => _errorMessage;
            set => SetProperty(ref _errorMessage, value);
        }

        public MatrixConnection(string inputChannel, string outputChannel)
        {
            InputChannel = inputChannel;
            OutputChannel = outputChannel;
            State = SwitchConnectionState.Disconnected;
            StateColor = "#9E9E9E"; // 灰色
            ConnectionCount = 0;
            ConnectionDuration = 0;
            LastConnectedTime = DateTime.MinValue;
            LastDisconnectedTime = DateTime.MinValue;
        }

        public void SetConnectionState(SwitchConnectionState newState, string errorMessage = null)
        {
            var oldState = State;
            State = newState;

            if (newState == SwitchConnectionState.Connected)
            {
                ConnectionCount++;
                LastConnectedTime = DateTime.Now;
                StateColor = "#4CAF50"; // 绿色
                ErrorMessage = null;
            }
            else if (newState == SwitchConnectionState.Disconnected)
            {
                if (oldState == SwitchConnectionState.Connected)
                {
                    LastDisconnectedTime = DateTime.Now;
                    if (LastConnectedTime != DateTime.MinValue)
                    {
                        ConnectionDuration += (LastDisconnectedTime - LastConnectedTime).TotalSeconds;
                    }
                }
                StateColor = "#9E9E9E"; // 灰色
                ErrorMessage = null;
            }
            else if (newState == SwitchConnectionState.Error)
            {
                StateColor = "#F44336"; // 红色
                ErrorMessage = errorMessage;
            }
        }
    }

    /// <summary>
    /// 开关矩阵卡配置
    /// </summary>
    public class SwitchMatrixCardConfig : CardConfigDataBase
    {
        private string _cardId;
        private string _cardName;
        private string _cardModel;
        private string _topology;
        private int _inputCount;
        private int _outputCount;
        private Dictionary<string, MatrixConnection> _connectionMap;
        private ObservableCollection<MatrixConnection> _activeConnections;
        private int _activeRelayCount;
        private int _errorConnectionCount;

        public override string CardType => "矩阵开关";
        public string CardId
        {
            get => _cardId;
            set => SetProperty(ref _cardId, value);
        }

        public string CardName
        {
            get => _cardName;
            set => SetProperty(ref _cardName, value);
        }

        public string CardModel
        {
            get => _cardModel;
            set => SetProperty(ref _cardModel, value);
        }

        public string Topology
        {
            get => _topology;
            set => SetProperty(ref _topology, value);
        }

        public int InputCount
        {
            get => _inputCount;
            set => SetProperty(ref _inputCount, value);
        }

        public int OutputCount
        {
            get => _outputCount;
            set => SetProperty(ref _outputCount, value);
        }

        public Dictionary<string, MatrixConnection> ConnectionMap
        {
            get => _connectionMap;
            set => SetProperty(ref _connectionMap, value);
        }

        public ObservableCollection<MatrixConnection> ActiveConnections
        {
            get => _activeConnections;
            set => SetProperty(ref _activeConnections, value);
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

        public SwitchMatrixCardConfig()
        {
            ConnectionMap = new Dictionary<string, MatrixConnection>();
            ActiveConnections = new ObservableCollection<MatrixConnection>();
        }

        /// <summary>
        /// 初始化矩阵
        /// </summary>
        public void InitializeMatrix(int inputCount, int outputCount)
        {
            InputCount = inputCount;
            OutputCount = outputCount;
            ConnectionMap.Clear();
            ActiveConnections.Clear();
            ActiveRelayCount = 0;
            ErrorConnectionCount = 0;

            // 创建所有可能的连接
            for (int i = 0; i < inputCount; i++)
            {
                for (int j = 0; j < outputCount; j++)
                {
                    string input = $"r{i}";
                    string output = $"c{j}";
                    string key = GetConnectionKey(input, output);

                    var connection = new MatrixConnection(input, output);
                    ConnectionMap[key] = connection;
                }
            }
        }

        /// <summary>
        /// 获取连接键
        /// </summary>
        private string GetConnectionKey(string input, string output)
        {
            return $"{input}->{output}";
        }

        /// <summary>
        /// 获取连接
        /// </summary>
        public MatrixConnection GetConnection(string input, string output)
        {
            string key = GetConnectionKey(input, output);
            return ConnectionMap.ContainsKey(key) ? ConnectionMap[key] : null;
        }

        /// <summary>
        /// 创建连接
        /// </summary>
        public MatrixConnection CreateConnection(string input, string output)
        {
            string key = GetConnectionKey(input, output);
            if (!ConnectionMap.ContainsKey(key))
            {
                var connection = new MatrixConnection(input, output);
                ConnectionMap[key] = connection;
            }
            return ConnectionMap[key];
        }

        /// <summary>
        /// 设置连接状态
        /// </summary>
        public void SetConnection(string input, string output, SwitchConnectionState state, string errorMessage = null)
        {
            var connection = GetConnection(input, output);
            if (connection != null)
            {
                var oldState = connection.State;
                connection.SetConnectionState(state, errorMessage);

                // 更新活跃连接列表
                if (state == SwitchConnectionState.Connected)
                {
                    if (!ActiveConnections.Contains(connection))
                    {
                        ActiveConnections.Add(connection);
                    }
                }
                else
                {
                    if (ActiveConnections.Contains(connection))
                    {
                        ActiveConnections.Remove(connection);
                    }
                }

                // 更新计数
                UpdateCounts();
            }
        }

        /// <summary>
        /// 获取连接的输出
        /// </summary>
        public string GetConnectedOutput(string input)
        {
            foreach (var connection in ConnectionMap.Values)
            {
                if (connection.InputChannel == input && connection.State == SwitchConnectionState.Connected)
                {
                    return connection.OutputChannel;
                }
            }
            return null;
        }

        /// <summary>
        /// 获取连接的输入
        /// </summary>
        public string GetConnectedInput(string output)
        {
            foreach (var connection in ConnectionMap.Values)
            {
                if (connection.OutputChannel == output && connection.State == SwitchConnectionState.Connected)
                {
                    return connection.InputChannel;
                }
            }
            return null;
        }

        /// <summary>
        /// 检查输入是否连接
        /// </summary>
        public bool IsInputConnected(string input)
        {
            return !string.IsNullOrEmpty(GetConnectedOutput(input));
        }

        /// <summary>
        /// 检查输出是否连接
        /// </summary>
        public bool IsOutputConnected(string output)
        {
            return !string.IsNullOrEmpty(GetConnectedInput(output));
        }

        /// <summary>
        /// 获取所有活跃连接
        /// </summary>
        public IEnumerable<MatrixConnection> GetActiveConnections()
        {
            return ConnectionMap.Values.Where(c => c.State == SwitchConnectionState.Connected);
        }

        /// <summary>
        /// 获取所有活跃连接（列表形式）
        /// </summary>
        public List<MatrixConnection> GetAllActiveConnections()
        {
            return ConnectionMap.Values.Where(c => c.State == SwitchConnectionState.Connected).ToList();
        }

        /// <summary>
        /// 更新活跃连接计数
        /// </summary>
        public void UpdateCounts()
        {
            ActiveRelayCount = ConnectionMap.Values.Count(c => c.State == SwitchConnectionState.Connected);
            ErrorConnectionCount = ConnectionMap.Values.Count(c => c.State == SwitchConnectionState.Error);
        }

        /// <summary>
        /// 更新活跃连接列表
        /// </summary>
        public void UpdateActiveConnectionsList()
        {
            ActiveConnections.Clear();
            foreach (var connection in GetActiveConnections())
            {
                ActiveConnections.Add(connection);
            }
        }

        /// <summary>
        /// 重置所有连接计数
        /// </summary>
        public void ResetConnectionCounts()
        {
            foreach (var connection in ConnectionMap.Values)
            {
                connection.ConnectionCount = 0;
                connection.ConnectionDuration = 0;
                connection.LastConnectedTime = DateTime.MinValue;
                connection.LastDisconnectedTime = DateTime.MinValue;
            }
            UpdateCounts();
        }

        /// <summary>
        /// 清除所有错误
        /// </summary>
        public void ClearAllErrors()
        {
            foreach (var connection in ConnectionMap.Values)
            {
                if (connection.State == SwitchConnectionState.Error)
                {
                    connection.SetConnectionState(SwitchConnectionState.Disconnected);
                }
            }
            UpdateCounts();
        }

        /// <summary>
        /// 获取统计信息
        /// </summary>
        public string GetStatistics()
        {
            return $"{CardName} ({Topology})\n" +
                   $"输入: {InputCount}, 输出: {OutputCount}\n" +
                   $"活跃连接: {ActiveRelayCount}, 错误: {ErrorConnectionCount}";
        }
    }

    #endregion

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
            set => SetProperty(ref _connectionColor, value);
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

            ConnectionColor = "#4CAF50"; // 固定为绿色
        }
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

            string autoColor = "#4CAF50";
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


    #endregion

    /// <summary>
    /// PXI3022 矩阵继电器控制面板 ViewModel
    /// </summary>
    public class SwitchPXI3022ControlPanelViewModel : BindableBase, IDisposable
    {
        #region Private Fields

        private DeviceBase _device;
        private string _chassisName;
        private string _cardModel;
        private string _cardName;
        private string _connectionStatus;
        private bool _isDeviceConnected;
        private PXI3022Driver _driver;

        // 连接状态相关
        private int _errorConnectionCount;

        // 复用连接相关
        public bool KeepMatrixConnectionOnClose { get; set; }

        // TCP相关字段
        private const int TcpBasePort3022 = 50300; // PXI3022使用不同的端口范围
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

        private static readonly object TcpServersLock = new object();
        private static readonly Dictionary<string, TcpServerInfo> _tcpServers = new Dictionary<string, TcpServerInfo>();

        private class TcpServerInfo
        {
            public TcpListener Listener { get; set; }
            public CancellationTokenSource Cts { get; set; }
            public Task AcceptTask { get; set; }
            public int Port { get; set; }
            public string BoardIdentifier { get; set; }
            public int RefCount { get; set; }
        }

        // 矩阵拓扑配置
        private string _topology = "4x64 Matrix";
        private int _currentInputCount = 4;
        private int _currentOutputCount = 64;
        private int _currentCrossPointCount = 256;
        private string _inputLineText = "输入线 (4条)";
        private string _outputLineText = "输出线 (64条)";

        // 矩阵拓扑交互状态
        private MatrixNodeViewModel _selectedInputNode;
        private MatrixNodeViewModel _selectedOutputNode;
        private CrossPointViewModel _selectedCrossPoint;
        private CrossPointViewModel _pendingCrossPoint;

        // 拓扑视图交互状态
        private TopologyNodeInfo _firstSelectedNode;
        private TopologyNodeInfo _secondSelectedNode;
        private TopologyNodeInfo _hoveredNode;

        // 视图集合
        private ObservableCollection<string> _inputChannels;
        private ObservableCollection<string> _outputChannels;
        private ObservableCollection<MatrixConnection> _activeConnections;
        private ObservableCollection<MatrixNodeViewModel> _matrixNodes;
        private ObservableCollection<CrossPointViewModel> _crossPoints;
        private ObservableCollection<LineViewModel> _verticalLines;
        private ObservableCollection<TopologyNodeInfo> _topologyNodes;
        private ObservableCollection<LineViewModel> _horizontalLines;
        private ObservableCollection<LineViewModel> _horizontalLinesPage1;
        private ObservableCollection<LineViewModel> _horizontalLinesPage2;
        private ObservableCollection<MatrixConnectionViewModel> _matrixConnections;
        private ObservableCollection<LabelViewModel> _inputLabels;
        private ObservableCollection<LabelViewModel> _outputLabels;

        private ObservableCollection<CrossPointViewModel> _crossPointsPage1;
        private ObservableCollection<CrossPointViewModel> _crossPointsPage2;
        private ObservableCollection<MatrixNodeViewModel> _matrixNodesPage1;
        private ObservableCollection<MatrixNodeViewModel> _matrixNodesPage2;
        private ObservableCollection<LineViewModel> _verticalLinesPage1;
        private ObservableCollection<LineViewModel> _verticalLinesPage2;

        // 配置
        private SwitchMatrixCardConfig _cardConfig;

        // 服务
        private readonly IPxiChassisService _pxiChassisService;
        private readonly IEventAggregator _eventAggregator;
        private DispatcherTimer _statusTimer;
        private Dispatcher _dispatcher;

        // 画布尺寸
        private double _canvasWidth = 2000;
        private double _canvasHeight = 600;
        private double _availableWidth = 0;
        private double _availableHeight = 0;
        // 实际可见的每页 Viewport 宽度（由 Canvas/ScrollViewer 提供）
        private double _canvasViewportWidthPage1 = 0;
        private double _canvasViewportWidthPage2 = 0;
        // 分页画布宽度（分别用于上半/下半，避免左右留白）
        private double _canvasWidthPage1 = 1000;
        private double _canvasWidthPage2 = 1000;

        #endregion

        #region Properties

        public DeviceBase Device
        {
            get => _device;
            set => SetProperty(ref _device, value);
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


        public int ErrorConnectionCount
        {
            get => _errorConnectionCount;
            set => SetProperty(ref _errorConnectionCount, value);
        }

        public string Topology
        {
            get => _topology;
            set => SetProperty(ref _topology, value);
        }

        /// <summary>
        /// 连接按钮文本
        /// </summary>
        public string ConnectButtonText => IsDeviceConnected ? "关闭板卡" : "打开板卡";

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

        public double CanvasWidthPage1
        {
            get => _canvasWidthPage1;
            set => SetProperty(ref _canvasWidthPage1, value);
        }

        public double CanvasWidthPage2
        {
            get => _canvasWidthPage2;
            set => SetProperty(ref _canvasWidthPage2, value);
        }

        public double CanvasViewportWidthPage1
        {
            get => _canvasViewportWidthPage1;
            set => SetProperty(ref _canvasViewportWidthPage1, value);
        }

        public double CanvasViewportWidthPage2
        {
            get => _canvasViewportWidthPage2;
            set => SetProperty(ref _canvasViewportWidthPage2, value);
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

        public string MatrixStatisticsInfo => $"{CurrentInputCount}x{CurrentOutputCount} 矩阵 ({CurrentCrossPointCount}个交叉点)";

        // 集合属性
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

        // 兼容旧逻辑：继电器列表与计数属性（保留供视图绑定和旧方法使用）
        public ObservableCollection<RelayStatusInfo> RelayStatusList { get; set; } = new System.Collections.ObjectModel.ObservableCollection<RelayStatusInfo>();

        public int TotalRelayCount { get; set; } = 0;

        public int ActiveRelayCount { get; set; } = 0;


        public ObservableCollection<CrossPointViewModel> CrossPointsPage1
        {
            get => _crossPointsPage1;
            set => SetProperty(ref _crossPointsPage1, value);
        }

        public ObservableCollection<CrossPointViewModel> CrossPointsPage2
        {
            get => _crossPointsPage2;
            set => SetProperty(ref _crossPointsPage2, value);
        }

        public ObservableCollection<MatrixNodeViewModel> MatrixNodesPage1
        {
            get => _matrixNodesPage1;
            set => SetProperty(ref _matrixNodesPage1, value);
        }

        public ObservableCollection<MatrixNodeViewModel> MatrixNodesPage2
        {
            get => _matrixNodesPage2;
            set => SetProperty(ref _matrixNodesPage2, value);
        }

        public ObservableCollection<LineViewModel> VerticalLinesPage1
        {
            get => _verticalLinesPage1;
            set => SetProperty(ref _verticalLinesPage1, value);
        }

        public ObservableCollection<LineViewModel> VerticalLinesPage2
        {
            get => _verticalLinesPage2;
            set => SetProperty(ref _verticalLinesPage2, value);
        }

        public ObservableCollection<LineViewModel> HorizontalLinesPage1
        {
            get => _horizontalLinesPage1;
            set => SetProperty(ref _horizontalLinesPage1, value);
        }

        public ObservableCollection<LineViewModel> HorizontalLinesPage2
        {
            get => _horizontalLinesPage2;
            set => SetProperty(ref _horizontalLinesPage2, value);
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

        // 交互状态属性
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

        /// <summary>
        /// 矩阵选择状态提示
        /// </summary>
        public string MatrixSelectionStatus
        {
            get
            {
                if (!IsDeviceConnected)
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

        #endregion

        private void OnRemoteMatrixCommand(MeasureControl.Events.RemoteMatrixCommandEventArgs args)
        {
            try
            {
                if (args == null) return;
                var mySlot = (Device as MeasureControl.Models.Devices.DeviceCategories.PxiDeviceBase)?.SlotIndex ?? -1;
                if (mySlot <= 0) return;
                if (mySlot != args.SlotIndex) return;

                Debug.WriteLine($"[PXI3022ControlPanelViewModel] Received remote command for slot {args.SlotIndex}: {args.InputNodeId}->{args.OutputNodeId}, state={args.State}");

                // 在后台处理，避免阻塞事件发布线程
                _ = Task.Run(async () =>
                {
                    try
                    {
                        if (args.State == 0)
                        {
                            await ConnectNodesLocalAsync(args.InputNodeId, args.OutputNodeId);
                        }
                        else
                        {
                            await DisconnectNodesLocalAsync(args.InputNodeId, args.OutputNodeId);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[PXI3022ControlPanelViewModel] Remote command handling failed: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PXI3022ControlPanelViewModel] OnRemoteMatrixCommand exception: {ex.Message}");
            }
        }

        private void OnDeviceModified(DeviceModifiedEventArgs args)
        {
            try
            {
                if (args?.Device == null || Device == null) return;
                if (args.Device.Id != Device.Id) return;

                // 只有在非远程命令情况下才重新加载配置，避免重置TCP连接状态
                if (args.ModificationType != "RemoteCommand")
                {
                    Debug.WriteLine($"[SwitchPXI3022Control] DeviceModifiedEvent received for DeviceId={Device.Id}, reloading CardConfigData");
                    // Reload CardConfigData and refresh UI on UI thread
                    Dispatcher.Invoke(() =>
                    {
                        LoadDeviceConfig();
                    });
                }
                else
                {
                    Debug.WriteLine($"[SwitchPXI3022Control] DeviceModifiedEvent received for DeviceId={Device.Id}, skipping reload for RemoteCommand to preserve TCP connections");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SwitchPXI3022Control] OnDeviceModified exception: {ex.Message}");
            }
        }


        #region Commands

        public ICommand ToggleDeviceCommand { get; private set; }
        public ICommand DisconnectAllCommand { get; private set; }
        public ICommand RefreshStatusCommand { get; private set; }
        public ICommand ResetCountersCommand { get; private set; }
        public ICommand ClearErrorsCommand { get; private set; }

        // 矩阵拓扑交互命令
        public ICommand MatrixNodeClickedCommand { get; private set; }
        public ICommand CrossPointClickedCommand { get; private set; }
        public ICommand MatrixConnectionRightClickedCommand { get; private set; }
        public ICommand RefreshTopologyCommand { get; private set; }

        public ICommand NodeClickedCommand { get; private set; }
        public ICommand ConnectionRightClickedCommand { get; private set; }

        public ICommand NodeHoveredCommand { get; private set; }

        // 悬停命令
        public ICommand CrossPointHoveredCommand { get; private set; }
        public ICommand CrossPointMouseLeaveCommand { get; private set; }


        // 确认/取消连接命令
        public ICommand ConfirmConnectionCommand { get; private set; }
        public ICommand CancelConnectionCommand { get; private set; }

        // 右键命令
        public ICommand MatrixNodeRightClickedCommand { get; private set; }
        public ICommand DisconnectCrossPointCommand { get; private set; }
        public ICommand ConnectCrossPointCommand { get; private set; }

        #endregion

        #region Constructor

        public SwitchPXI3022ControlPanelViewModel()
        {
            InitializeCollections();
            InitializeCommands();

            ConnectionStatus = "离线";

            _dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
        }

        public SwitchPXI3022ControlPanelViewModel(DeviceBase device, string chassisName,
            IPxiChassisService pxiChassisService = null, IEventAggregator eventAggregator = null) : this()
        {
            Device = device;
            ChassisName = chassisName;
            CardModel = device?.Model ?? "矩阵开关";
            CardName = !string.IsNullOrEmpty(device?.CardName) ? device.CardName : device?.Model ?? "PXI-3022";
            _pxiChassisService = pxiChassisService;
            _eventAggregator = eventAggregator;

            LoadDeviceConfig();

            // 订阅设备修改事件用于刷新UI（当机箱回退执行并更新服务时触发）
            try
            {
                _eventAggregator?.GetEvent<MeasureControl.Events.DeviceModifiedEvent>()?.Subscribe(OnDeviceModified, Prism.Events.ThreadOption.UIThread);
            }
            catch { }

            // 订阅远程矩阵命令事件（用于接收来自服务器的远程命令）
            try
            {
                _eventAggregator?.GetEvent<MeasureControl.Events.RemoteMatrixCommandEvent>()?.Subscribe(OnRemoteMatrixCommand, Prism.Events.ThreadOption.BackgroundThread);
            }
            catch { }

            // 初始化 PXI3022 矩阵拓扑
            InitializePXI3022Matrix();

            // 初始化TCP相关设置
            try
            {
                var ips = string.Join(",", GetLocalIpv4Addresses());
                Debug.WriteLine($"[SwitchPXI3022ControlPanelViewModel] LocalIPv4=[{ips}] Mode={(IsLocalChassisByIp() ? "LocalChassis(Server)" : "RemoteClient(TCP)")}");
            }
            catch
            {
            }

            // 注意：TCP服务器由PxiChassisViewModel统一管理，在设备添加到机箱时启动
            // ViewModel不应重复启动TCP服务器，以避免引用计数混乱
            if (IsLocalChassisByIp())
            {
                Debug.WriteLine($"[SwitchPXI3022ControlPanelViewModel] 本地机箱模式，TCP服务器应已由PxiChassisViewModel启动");
            }
        }

        private void InitializeCollections()
        {
            InputChannels = new ObservableCollection<string>();
            OutputChannels = new ObservableCollection<string>();
            ActiveConnections = new ObservableCollection<MatrixConnection>();
            MatrixNodes = new ObservableCollection<MatrixNodeViewModel>();
            CrossPoints = new ObservableCollection<CrossPointViewModel>();
            VerticalLines = new ObservableCollection<LineViewModel>();
            HorizontalLines = new ObservableCollection<LineViewModel>();
            MatrixConnections = new ObservableCollection<MatrixConnectionViewModel>();
            InputLabels = new ObservableCollection<LabelViewModel>();
            OutputLabels = new ObservableCollection<LabelViewModel>();

            CrossPointsPage1 = new ObservableCollection<CrossPointViewModel>();
            CrossPointsPage2 = new ObservableCollection<CrossPointViewModel>();
            MatrixNodesPage1 = new ObservableCollection<MatrixNodeViewModel>();
            MatrixNodesPage2 = new ObservableCollection<MatrixNodeViewModel>();
            VerticalLinesPage1 = new ObservableCollection<LineViewModel>();
            VerticalLinesPage2 = new ObservableCollection<LineViewModel>();
            HorizontalLinesPage1 = new ObservableCollection<LineViewModel>();
            HorizontalLinesPage2 = new ObservableCollection<LineViewModel>();
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

        private void InitializeCommands()
        {
            ToggleDeviceCommand = new DelegateCommand(async () => await ToggleDeviceAsync(),
                () => true)
                .ObservesProperty(() => IsDeviceConnected);

            DisconnectAllCommand = new DelegateCommand(async () => await DisconnectAllAsync(),
                () => IsDeviceConnected)
                .ObservesProperty(() => IsDeviceConnected);

            RefreshStatusCommand = new DelegateCommand(async () => await RefreshConnectionStatusAsync(),
                () => IsDeviceConnected)
                .ObservesProperty(() => IsDeviceConnected);

            ResetCountersCommand = new DelegateCommand(ResetConnectionCounters);
            ClearErrorsCommand = new DelegateCommand(ClearAllErrors);

            // 矩阵拓扑命令
            MatrixNodeClickedCommand = new DelegateCommand<MatrixNodeViewModel>(OnMatrixNodeClicked);
            CrossPointClickedCommand = new DelegateCommand<CrossPointViewModel>(OnCrossPointClicked);
            RefreshTopologyCommand = new DelegateCommand(RefreshMatrixTopology);

            CrossPointHoveredCommand = new DelegateCommand<CrossPointViewModel>(OnCrossPointHovered);
            CrossPointMouseLeaveCommand = new DelegateCommand<CrossPointViewModel>(OnCrossPointMouseLeave);
            MatrixConnectionRightClickedCommand = new DelegateCommand<MatrixConnectionViewModel>(OnMatrixConnectionRightClicked);

            ConfirmConnectionCommand = new DelegateCommand(ConfirmConnection);
            CancelConnectionCommand = new DelegateCommand(CancelConnection);
            NodeHoveredCommand = new DelegateCommand<TopologyNodeInfo>(OnNodeHovered);
            ConnectionRightClickedCommand = new DelegateCommand<TopologyConnectionInfo>(OnConnectionRightClicked);

            MatrixNodeRightClickedCommand = new DelegateCommand<MatrixNodeViewModel>(OnMatrixNodeRightClicked);
            DisconnectCrossPointCommand = new DelegateCommand<CrossPointViewModel>(OnDisconnectCrossPoint);
            ConnectCrossPointCommand = new DelegateCommand<CrossPointViewModel>(OnConnectCrossPoint);

            NodeClickedCommand = new DelegateCommand<TopologyNodeInfo>(OnNodeClicked);
        }

        #region TCP服务器端方法

        private async Task HandleClientAsync(TcpClient client, TcpServerInfo serverInfo, CancellationToken token)
        {
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

                        // PXI3022特殊处理：将行列坐标转换为一维数组索引
                        int pointIndex = MapRowColToPointIndex(inputIndex, outputIndex);
                        string inputNodeId = $"r{inputIndex}";
                        string outputNodeId = $"c{outputIndex}";

                        bool ok = true;
                        if (inputIndex == 0xFF)
                        {
                            await _remoteCommandLock.WaitAsync(token);
                            try
                            {
                                if (state == RemoteCommandConnect) // 0 = 连接设备
                                {
                                    await ConnectDeviceAsync();
                                    ok = IsDeviceConnected;
                                }
                                else if (state == RemoteCommandDisconnect) // 1 = 断开设备
                                {
                                    await DisconnectDeviceAsync();
                                    ok = !IsDeviceConnected;
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
                            await _remoteCommandLock.WaitAsync(token);
                            try
                            {
                                ok = await ConnectNodesLocalAsync(inputNodeId, outputNodeId);
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
                                ok = await DisconnectNodesLocalAsync(inputNodeId, outputNodeId);
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
        /// 将行列坐标映射到PXI3022的一维数组索引
        /// PXI3022是4x64矩阵，总共256个点
        /// </summary>
        private int MapRowColToPointIndex(int row, int col)
        {
            // PXI3022的映射逻辑：pointIndex = row * 64 + col
            // 行范围：0-3，列范围：0-63
            if (row < 0 || row > 3 || col < 0 || col > 63)
            {
                Debug.WriteLine($"[MapRowColToPointIndex] 无效的行列坐标: row={row}, col={col}");
                return -1;
            }

            int pointIndex = row * 64 + col;
            Debug.WriteLine($"[MapRowColToPointIndex] row={row}, col={col} -> pointIndex={pointIndex}");
            return pointIndex;
        }

        /// <summary>
        /// 将一维数组索引映射回行列坐标
        /// </summary>
        private (int row, int col) MapPointIndexToRowCol(int pointIndex)
        {
            if (pointIndex < 0 || pointIndex >= 256)
            {
                Debug.WriteLine($"[MapPointIndexToRowCol] 无效的点索引: pointIndex={pointIndex}");
                return (-1, -1);
            }

            int row = pointIndex / 64;
            int col = pointIndex % 64;
            Debug.WriteLine($"[MapPointIndexToRowCol] pointIndex={pointIndex} -> row={row}, col={col}");
            return (row, col);
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

        private async Task<bool> ConnectTcpNodesAsync(string inputNodeId, string outputNodeId)
        {
            if (_cardConfig == null) return false;

            if (IsRemoteChassis)
            {
                return await SendMatrixCommandAsync(inputNodeId, outputNodeId, RemoteCommandConnect);
            }

            // 如果驱动为空，尝试自动连接设备（与2601保持一致）
            if (_driver == null)
            {
                await ConnectDeviceAsync();
            }

            if (_driver == null || !IsDeviceConnected) return false;

            // PXI3022的连接逻辑：使用行列坐标转换为点索引，然后连接
            if (int.TryParse(inputNodeId.Substring(1), out int inputIndex) &&
                int.TryParse(outputNodeId.Substring(1), out int outputIndex))
            {
                int pointIndex = MapRowColToPointIndex(inputIndex, outputIndex);
                if (pointIndex >= 0)
                {
                    // 调用PXI3022驱动的连接方法 - 使用WriteChannelAsync连接继电器
                    var (row, col) = MapPointIndexToRowCol(pointIndex);
                    if (row >= 0 && col >= 0)
                    {
                        string channelId = $"R{row}C{col}";
                        bool ok = await _driver.WriteChannelAsync(channelId, 1.0); // 1.0表示连接
                        if (ok)
                        {
                            _cardConfig.SetConnection(inputNodeId, outputNodeId, SwitchConnectionState.Connected);
                            await Dispatcher.InvokeAsync(() =>
                            {
                                UpdateMatrixNodesConnectionStatus();
                                UpdateActiveConnections();
                                UpdateConnectionCounts();
                                RefreshMatrixTopology();
                                UpdateCrossPointsConnectionStatus();
                            });
                            SaveDeviceConfig();
                        }
                        return ok;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// 连接节点（支持远程机箱模式）
        /// </summary>
        private async Task<bool> ConnectNodesAsync(string inputNodeId, string outputNodeId)
        {
            if (_cardConfig == null) return false;

            if (IsRemoteChassis)
            {
                // 远程机箱模式：发送TCP命令
                bool success = await SendMatrixCommandAsync(inputNodeId, outputNodeId, RemoteCommandConnect);

                if (success)
                {
                    // TCP命令发送成功后，立即更新本地UI状态以提供即时反馈
                    _cardConfig.SetConnection(inputNodeId, outputNodeId, SwitchConnectionState.Connected);

                    // 设置设备连接状态（与PXI2601保持一致）
                    if (!IsDeviceConnected)
                    {
                        IsDeviceConnected = true;
                        ConnectionStatus = "远程在线";
                        RaisePropertyChanged(nameof(ConnectButtonText));
                    }

                    await Dispatcher.InvokeAsync(() =>
                    {
                        UpdateMatrixNodesConnectionStatus();
                        UpdateActiveConnections();
                        UpdateConnectionCounts();
                        RefreshMatrixTopology();
                        UpdateCrossPointsConnectionStatus();
                    });

                    // 保存配置
                    SaveDeviceConfig();

                    Debug.WriteLine($"[ConnectNodesAsync] 远程连接成功，UI已更新: {inputNodeId} -> {outputNodeId}");
                }

                return success;
            }
            else
            {
                // 本地机箱模式：直接调用硬件
                return await ConnectNodesLocalAsync(inputNodeId, outputNodeId);
            }
        }

        /// <summary>
        /// 本地硬件连接操作（回退方案）
        /// </summary>
        private async Task<bool> ConnectNodesLocalAsync(string inputNodeId, string outputNodeId)
        {
            if (_driver == null || !IsDeviceConnected) return false;

            // PXI3022的连接逻辑：将行列坐标转换为一维数组索引，然后连接
            if (int.TryParse(inputNodeId.Substring(1), out int inputIndex) &&
                int.TryParse(outputNodeId.Substring(1), out int outputIndex))
            {
                int pointIndex = MapRowColToPointIndex(inputIndex, outputIndex);
                if (pointIndex >= 0)
                {
                    // 调用PXI3022驱动的连接方法 - 使用WriteChannelAsync连接继电器
                    var (row, col) = MapPointIndexToRowCol(pointIndex);
                    if (row >= 0 && col >= 0)
                    {
                        string channelId = $"R{row}C{col}";
                        bool ok = await _driver.WriteChannelAsync(channelId, 1.0); // 1.0表示连接
                        if (ok)
                        {
                            _cardConfig.SetConnection(inputNodeId, outputNodeId, SwitchConnectionState.Connected);
                            await Dispatcher.InvokeAsync(() =>
                            {
                                UpdateMatrixNodesConnectionStatus();
                                UpdateActiveConnections();
                                UpdateConnectionCounts();
                                RefreshMatrixTopology();
                                UpdateCrossPointsConnectionStatus();
                            });
                            SaveDeviceConfig();
                        }
                        return ok;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// 断开节点连接（支持远程机箱模式）
        /// </summary>
        private async Task<bool> DisconnectNodesAsync(string inputNodeId, string outputNodeId)
        {
            if (_cardConfig == null) return false;

            if (IsRemoteChassis)
            {
                // 远程机箱模式：发送TCP命令
                bool success = await SendMatrixCommandAsync(inputNodeId, outputNodeId, RemoteCommandDisconnect);

                if (success)
                {
                    // TCP命令发送成功后，立即更新本地UI状态以提供即时反馈
                    _cardConfig.SetConnection(inputNodeId, outputNodeId, SwitchConnectionState.Disconnected);

                    await Dispatcher.InvokeAsync(() =>
                    {
                        UpdateMatrixNodesConnectionStatus();
                        UpdateActiveConnections();
                        UpdateConnectionCounts();
                        RefreshMatrixTopology();
                        UpdateCrossPointsConnectionStatus();
                    });

                    // 保存配置
                    SaveDeviceConfig();

                    Debug.WriteLine($"[DisconnectNodesAsync] 远程断开成功，UI已更新: {inputNodeId} -> {outputNodeId}");
                }

                return success;
            }
            else
            {
                // 本地机箱模式：直接调用硬件
                return await DisconnectNodesLocalAsync(inputNodeId, outputNodeId);
            }
        }

        /// <summary>
        /// 本地硬件断开操作（回退方案）
        /// </summary>
        private async Task<bool> DisconnectNodesLocalAsync(string inputNodeId, string outputNodeId)
        {
            if (_driver == null || !IsDeviceConnected) return false;

            // PXI3022的断开逻辑：将行列坐标转换为一维数组索引，然后断开
            if (int.TryParse(inputNodeId.Substring(1), out int inputIndex) &&
                int.TryParse(outputNodeId.Substring(1), out int outputIndex))
            {
                int pointIndex = MapRowColToPointIndex(inputIndex, outputIndex);
                if (pointIndex >= 0)
                {
                    // 调用PXI3022驱动的断开方法 - 使用WriteChannelAsync断开连接
                    var (row, col) = MapPointIndexToRowCol(pointIndex);
                    if (row >= 0 && col >= 0)
                    {
                        string channelId = $"R{row}C{col}";
                        bool ok = await _driver.WriteChannelAsync(channelId, 0.0); // 0.0表示断开
                        if (ok)
                        {
                            _cardConfig.SetConnection(inputNodeId, outputNodeId, SwitchConnectionState.Disconnected);
                            await Dispatcher.InvokeAsync(() =>
                            {
                                UpdateMatrixNodesConnectionStatus();
                                UpdateActiveConnections();
                                UpdateConnectionCounts();
                                RefreshMatrixTopology();
                                UpdateCrossPointsConnectionStatus();
                            });
                            SaveDeviceConfig();
                        }
                        return ok;
                    }
                }
            }

            return false;
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// 更新可用空间大小
        /// </summary>
        public void UpdateAvailableSpace(double width, double height)
        {
            AvailableWidth = width;
            AvailableHeight = height;

            if (width > 0 && height > 0)
            {
                RefreshMatrixTopology();
            }
        }

        /// <summary>
        /// 强制使用 Canvas 的 viewport 宽度重新计算布局（当通过代码-behind 获取到 viewportWidth 后调用）
        /// </summary>
        public void RefreshUsingViewport()
        {
            // 如果页面 viewport 已经设置，优先使用它作为可用宽度
            if (CanvasViewportWidthPage1 > 0 && AvailableHeight > 0)
            {
                AvailableWidth = CanvasViewportWidthPage1;
                RefreshMatrixTopology();
            }
            else if (CanvasViewportWidthPage2 > 0 && AvailableHeight > 0)
            {
                AvailableWidth = CanvasViewportWidthPage2;
                RefreshMatrixTopology();
            }
        }

        /// <summary>
        /// 使用 Canvas 的 viewport 大小即时重新计算各列的 X 坐标并写回已有的 MatrixNode/CrossPoint/Line，
        /// 以保证在 Canvas 大小变化时视图能立即按可见区域等分列并避免元素重叠。
        /// 该方法不会重建集合，只会更新现有项的位置和大小。
        /// </summary>
        public void RecalculatePositionsUsingViewport(double viewportWidth, double viewportHeight)
        {
            try
            {
                if (viewportWidth <= 0 || viewportHeight <= 0) return;

                int inputCount = 4;
                int outputCount = 64;
                int outputsPerPage = outputCount / 2;

                double minVerticalSpacing = 20;
                double minHorizontalSpacing = 25;

                double marginLeft = 20;
                double marginRight = 10;
                double marginTop = 30;
                double marginBottom = 30;
                double extensionLength = 15;

                // 以约定的 nodeRadius 为基准（与 RefreshMatrixTopology 保持一致）
                double nodeRadius = 10;

                // 计算每页的可用网格宽度与页内列间距
                double pageGridWidth = viewportWidth - marginLeft - marginRight - extensionLength - nodeRadius * 2;
                pageGridWidth = Math.Max(pageGridWidth, 0);
                double pageHorizontalSpacing = (outputsPerPage > 1) ? (pageGridWidth / (outputsPerPage - 1)) : minHorizontalSpacing;
                if (pageHorizontalSpacing < minHorizontalSpacing)
                    pageHorizontalSpacing = minHorizontalSpacing;

                // 根据列间距自动缩放节点与交叉点大小，避免重叠
                double desiredNodeDiameter = Math.Max(8, Math.Min(24, pageHorizontalSpacing * 0.6));
                double desiredCrossSize = Math.Max(6, Math.Min(18, desiredNodeDiameter * 0.7));

                // 更新输出节点的位置与大小
                foreach (var node in MatrixNodes)
                {
                    if (string.IsNullOrEmpty(node?.NodeId)) continue;
                    if (node.NodeType == "Output")
                    {
                        if (node.NodeId.StartsWith("c") && int.TryParse(node.NodeId.Substring(1), out int idx))
                        {
                            int jPage = idx % outputsPerPage;
                            double x = marginLeft + extensionLength + nodeRadius + jPage * pageHorizontalSpacing;
                            double circleY = marginTop - desiredNodeDiameter / 2.0;
                            node.DisplayX = x - desiredNodeDiameter / 2.0;
                            node.DisplayY = circleY;
                            node.Radius = desiredNodeDiameter;
                        }
                    }
                    else if (node.NodeType == "Input")
                    {
                        // 输入节点垂直位置依据 viewportHeight 分配
                        if (node.NodeId.StartsWith("r") && int.TryParse(node.NodeId.Substring(1), out int ridx))
                        {
                            double availableGridHeight = viewportHeight - marginTop - marginBottom - extensionLength - nodeRadius * 2;
                            double verticalSpacing = (inputCount > 1) ? (availableGridHeight / (inputCount - 1)) : minVerticalSpacing;
                            if (verticalSpacing < minVerticalSpacing) verticalSpacing = minVerticalSpacing;
                            double y = marginTop + extensionLength + nodeRadius + ridx * verticalSpacing;
                            double circleX = marginLeft - desiredNodeDiameter / 2.0;
                            node.DisplayX = circleX;
                            node.DisplayY = y - desiredNodeDiameter / 2.0;
                            node.Radius = desiredNodeDiameter;
                        }
                    }
                }

                // 更新交叉点的位置与大小（CrossPoints 包含所有页）
                foreach (var cp in CrossPoints)
                {
                    if (cp == null) continue;
                    if (string.IsNullOrEmpty(cp.OutputNodeId) || string.IsNullOrEmpty(cp.InputNodeId)) continue;
                    if (!cp.OutputNodeId.StartsWith("c") || !cp.InputNodeId.StartsWith("r")) continue;
                    if (!int.TryParse(cp.OutputNodeId.Substring(1), out int outIdx)) continue;
                    if (!int.TryParse(cp.InputNodeId.Substring(1), out int inIdx)) continue;

                    int jPage = outIdx % outputsPerPage;
                    double x = marginLeft + extensionLength + nodeRadius + jPage * pageHorizontalSpacing;

                    double availableGridHeight = viewportHeight - marginTop - marginBottom - extensionLength - nodeRadius * 2;
                    double verticalSpacing = (inputCount > 1) ? (availableGridHeight / (inputCount - 1)) : minVerticalSpacing;
                    if (verticalSpacing < minVerticalSpacing) verticalSpacing = minVerticalSpacing;
                    double y = marginTop + extensionLength + nodeRadius + inIdx * verticalSpacing;

                    cp.X = x - desiredCrossSize / 2.0;
                    cp.Y = y - desiredCrossSize / 2.0;
                    cp.Size = (int)Math.Round(desiredCrossSize);
                }

                // 更新分页水平线宽度（HorizontalLinesPage1/2）
                double horizontalStartX = marginLeft + extensionLength + nodeRadius;
                double horizontalEndX = horizontalStartX + pageGridWidth;
                for (int i = 0; i < HorizontalLinesPage1.Count; i++)
                {
                    var hl = HorizontalLinesPage1[i];
                    hl.StartX = horizontalStartX;
                    hl.EndX = horizontalEndX;
                }
                for (int i = 0; i < HorizontalLinesPage2.Count; i++)
                {
                    var hl = HorizontalLinesPage2[i];
                    hl.StartX = horizontalStartX;
                    hl.EndX = horizontalEndX;
                }

                // 更新分页垂直线位置（VerticalLinesPage1/2）
                double vStartY = marginTop + extensionLength + nodeRadius;
                double vEndY = vStartY + Math.Max(0, viewportHeight - marginTop - marginBottom - extensionLength - nodeRadius * 2);
                for (int j = 0; j < VerticalLinesPage1.Count && j < outputsPerPage; j++)
                {
                    double x = marginLeft + extensionLength + nodeRadius + j * pageHorizontalSpacing;
                    var v = VerticalLinesPage1[j];
                    v.StartX = x;
                    v.EndX = x;
                    v.StartY = vStartY;
                    v.EndY = vEndY;
                }
                for (int j = 0; j < VerticalLinesPage2.Count && j < outputsPerPage; j++)
                {
                    double x = marginLeft + extensionLength + nodeRadius + j * pageHorizontalSpacing;
                    var v = VerticalLinesPage2[j];
                    v.StartX = x;
                    v.EndX = x;
                    v.StartY = vStartY;
                    v.EndY = vEndY;
                }

                Debug.WriteLine($"[RecalculatePositionsUsingViewport] viewport:{viewportWidth}x{viewportHeight}, pageHorizontalSpacing:{pageHorizontalSpacing}, nodeDiameter:{desiredNodeDiameter}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[RecalculatePositionsUsingViewport] 错误: {ex.Message}");
            }
        }

        #endregion

        #region 设备连接相关方法

        /// <summary>
        /// 初始化 PXI3022 矩阵拓扑
        /// </summary>
        private void InitializePXI3022Matrix()
        {
            Debug.WriteLine("[InitializePXI3022Matrix] 开始初始化 PXI3022 矩阵拓扑");

            // PXI3022 固定为 4x64 矩阵
            CurrentInputCount = 4;
            CurrentOutputCount = 64;
            CurrentCrossPointCount = CurrentInputCount * CurrentOutputCount;
            InputLineText = $"输入线 ({CurrentInputCount}条)";
            OutputLineText = $"输出线 ({CurrentOutputCount}条)";

            // 初始化通道
            InitializeChannels(CurrentInputCount, CurrentOutputCount);

            // 初始化配置
            if (_cardConfig == null)
            {
                _cardConfig = new SwitchMatrixCardConfig
                {
                    CardId = Device?.Id ?? Guid.NewGuid().ToString(),
                    CardName = CardName,
                    CardModel = CardModel,
                    Topology = "4x64 Matrix"
                };
            }

            _cardConfig.InitializeMatrix(CurrentInputCount, CurrentOutputCount);
            _cardConfig.Topology = "4x64 Matrix";

            UpdateConnectionCounts();
            RefreshMatrixTopology();

            ErrorConnectionCount = 0;

            // 清除选择状态
            ClearAllSelectionStates();

            RaisePropertyChanged(nameof(MatrixStatisticsInfo));
            RaisePropertyChanged(nameof(MatrixSelectionStatus));

            Debug.WriteLine($"[InitializePXI3022Matrix] PXI3022 矩阵初始化完成: {CurrentInputCount}x{CurrentOutputCount}");
        }

        /// <summary>
        /// 初始化通道
        /// </summary>
        private void InitializeChannels(int inputCount, int outputCount)
        {
            Debug.WriteLine($"[InitializeChannels] 初始化通道: {inputCount}个输入, {outputCount}个输出");

            InputChannels.Clear();
            OutputChannels.Clear();

            for (int i = 0; i < inputCount; i++)
                InputChannels.Add($"r{i}");

            for (int i = 0; i < outputCount; i++)
                OutputChannels.Add($"c{i}");

            Debug.WriteLine($"[InitializeChannels] 通道初始化完成");
        }


        /// <summary>
        /// 切换设备连接状态
        /// </summary>
        private async Task ToggleDeviceAsync()
        {
            if (IsDeviceConnected)
            {
                await DisconnectDeviceAsync();
            }
            else
            {
                await ConnectDeviceAsync();
            }
        }

        /// <summary>
        /// 连接设备
        /// </summary>
        private async Task ConnectDeviceAsync()
        {
            if (Device == null) return;

            try
            {
                ConnectionStatus = "连接中...";

                // 创建 PXI3022 驱动
                // 根据在机箱中出现的顺序确定设备ID：第一个PXI3022设备为1，第二个为2，以此类推
                ushort deviceId = GetPxi3022DeviceId();
                _driver = new PXI3022Driver(Device, deviceId);

                bool connected = await _driver.ConnectAsync();

                if (connected)
                {
                    IsDeviceConnected = true;
                    ConnectionStatus = "在线";
                    Debug.WriteLine($"[SwitchPXI3022Control] PXI3022设备连接成功: {Device.Name}");
                    RaisePropertyChanged(nameof(ConnectButtonText));

                    // 启动状态定时器
                    StartStatusTimer();

                    // 刷新连接状态
                    await RefreshConnectionStatusAsync();
                }
                else
                {
                    IsDeviceConnected = false;
                    ConnectionStatus = "离线";
                    _driver = null;
                    RaisePropertyChanged(nameof(ConnectButtonText));
                    ReMessageBox.Show($"PXI3022设备连接失败", "连接失败",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                IsDeviceConnected = false;
                ConnectionStatus = "离线";
                _driver = null;

                ReMessageBox.Show($"PXI3022设备连接异常: {ex.Message}", "连接异常",
                    MessageBoxButton.OK, MessageBoxImage.Error);

                Debug.WriteLine($"[SwitchPXI3022Control] 设备连接异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取当前PXI3022设备在机箱中的逻辑编号
        /// 按照SlotIndex排序，第一个PXI3022设备返回1，第二个返回2，以此类推
        /// </summary>
        private ushort GetPxi3022DeviceId()
        {
            if (_pxiChassisService == null || string.IsNullOrEmpty(ChassisName))
                return 1;

            try
            {
                // 获取当前机箱中的所有设备
                var chassisDevices = _pxiChassisService.GetChassisDevices(ChassisName);
                if (chassisDevices == null)
                    return 1;

                // 筛选出所有PXI3022设备（按拖拽加入机箱的顺序）
                var pxi3022Devices = chassisDevices
                    .Where(d => d != null && (d.Model?.Contains("3022") == true || d.Model?.Contains("PXI3022") == true || d.Model?.Contains("PXI-3022") == true))
                    .OfType<PxiDeviceBase>()
                    .ToList();

                // 找到当前设备在排序列表中的位置
                int index = pxi3022Devices.FindIndex(d => d.Id == Device?.Id);
                if (index >= 0)
                {
                    // 返回位置+1作为deviceId（1-based indexing）
                    return (ushort)(index + 1);
                }

                // 如果找不到，返回默认值1
                return 1;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SwitchPXI3022Control] 获取设备ID异常: {ex.Message}");
                return 1;
            }
        }

        /// <summary>
        /// 断开设备连接
        /// </summary>
        private async Task DisconnectDeviceAsync()
        {
            try
            {
                ConnectionStatus = "断开中";

                StopStatusTimer();

                if (_driver != null)
                {
                    // 1. 断开所有硬件连接
                    await DisconnectAllAsync();

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

                        // 6. 更新配置中的计数
                        _cardConfig.UpdateCounts();
                        _cardConfig.UpdateActiveConnectionsList();
                    }

                    // 7. 强制更新所有继电器状态
                    UpdateAllRelayStatus();

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

                    // 12. 更新节点连接状态
                    UpdateMatrixNodesConnectionStatus();

                    // 13. 清除所有选择状态
                    ClearAllSelectionStates();

                    Debug.WriteLine($"[DisconnectDeviceAsync] 软件状态更新完成，ActiveRelayCount: {ActiveRelayCount}, TotalConnectionCount: {TotalConnectionCount}");
                });

                // 14. 保存配置
                SaveDeviceConfig();

                Debug.WriteLine($"[SwitchPXI3022Control] 设备已断开连接，所有继电器已关闭，连接计数保留: {TotalConnectionCount}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SwitchPXI3022Control] 断开连接异常: {ex.Message}");
            }
            finally
            {
                RaisePropertyChanged(nameof(ConnectButtonText));
            }
        }

        /// <summary>
        /// 断开所有连接
        /// </summary>
        private async Task DisconnectAllAsync()
        {
            if (_driver == null || !IsDeviceConnected) return;

            try
            {
                Debug.WriteLine($"[DisconnectAllAsync] 开始断开所有连接");

                // PXI3022 通过重置设备来断开所有连接
                bool success = await _driver.ResetAsync();

                if (success)
                {
                    Debug.WriteLine($"[DisconnectAllAsync] 硬件连接已断开，开始更新软件状态");

                    // 同时更新软件配置中的所有连接状态
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

                    // 在UI线程上更新显示
                    Dispatcher.Invoke(() =>
                    {
                        Debug.WriteLine("[DisconnectAllAsync] 在UI线程更新显示");

                        // 强制更新所有继电器状态
                        UpdateAllRelayStatus();

                        // 清空活动连接列表
                        ActiveConnections.Clear();

                        // 更新连接计数显示
                        UpdateConnectionCounts();

                        // 清除所有待处理连接和连接状态
                        foreach (var crossPoint in CrossPoints)
                        {
                            crossPoint.IsPendingConnection = false;
                            crossPoint.IsConnected = false;
                            crossPoint.ConnectionColor = null;
                        }

                        // 更新交叉点状态
                        UpdateCrossPointsConnectionStatus();

                        // 更新节点连接状态
                        UpdateMatrixNodesConnectionStatus();

                        // 清除所有选择状态
                        ClearAllSelectionStates();

                        // 更新状态显示
                        RaisePropertyChanged(nameof(MatrixSelectionStatus));

                        Debug.WriteLine($"[DisconnectAllAsync] UI状态更新完成");
                    });

                    // 保存配置
                    SaveDeviceConfig();

                    Debug.WriteLine($"[SwitchPXI3022Control] 所有硬件和软件连接已断开，ActiveRelayCount: {ActiveRelayCount}");
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

        /// <summary>
        /// 刷新连接状态
        /// </summary>
        private async Task RefreshConnectionStatusAsync()
        {
            if (_driver == null || !IsDeviceConnected) return;

            try
            {
                await RefreshHardwareDriverStatusAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SwitchPXI3022Control] 刷新连接状态失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 刷新硬件驱动状态
        /// </summary>
        private async Task RefreshHardwareDriverStatusAsync()
        {
            Debug.WriteLine($"[SwitchPXI3022Control] 刷新 PXI3022 硬件状态");

            try
            {
                // 执行硬件自检
                bool isAlive = await _driver.SelfTestAsync();
                if (!isAlive)
                {
                    Debug.WriteLine($"[SwitchPXI3022Control] 硬件离线警告，但保持软件连接状态不变");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SwitchPXI3022Control] 硬件检查异常，保持软件状态: {ex.Message}");
            }

            // 确保UI状态正确（基于软件配置）
            Dispatcher.Invoke(() =>
            {
                // 只刷新显示，不修改配置
                UpdateAllRelayStatus();
                UpdateConnectionCounts();
                UpdateCrossPointsConnectionStatus();
                UpdateMatrixNodesConnectionStatus();
            });
        }

        /// <summary>
        /// 启动状态定时器
        /// </summary>
        private void StartStatusTimer()
        {
            if (_statusTimer == null)
            {
                _statusTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(2)
                };
                _statusTimer.Tick += StatusTimer_Tick;
                Debug.WriteLine("[StartStatusTimer] 状态定时器已创建");
            }
            _statusTimer.Start();
            Debug.WriteLine("[StartStatusTimer] 状态定时器已启动");
        }

        /// <summary>
        /// 停止状态定时器
        /// </summary>
        private void StopStatusTimer()
        {
            if (_statusTimer != null)
            {
                _statusTimer.Stop();
                Debug.WriteLine("[StopStatusTimer] 状态定时器已停止");
            }
        }

        /// <summary>
        /// 状态定时器回调
        /// </summary>
        private async void StatusTimer_Tick(object sender, EventArgs e)
        {
            if (_driver == null || !IsDeviceConnected) return;

            try
            {
                Debug.WriteLine($"[StatusTimer_Tick] 定时刷新连接状态");

                // 在 UI 线程上更新所有状态
                Dispatcher.Invoke(() =>
                {
                    // 刷新所有继电器状态
                    UpdateAllRelayStatus();

                    // 刷新连接计数
                    UpdateConnectionCounts();

                    // 刷新交叉点状态
                    UpdateCrossPointsConnectionStatus();

                    // 刷新节点状态
                    UpdateMatrixNodesConnectionStatus();
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[StatusTimer_Tick] 状态更新失败: {ex.Message}");
            }
        }

        #endregion

        #region 配置管理方法

        /// <summary>
        /// 加载设备配置
        /// </summary>
        private void LoadDeviceConfig()
        {
            Debug.WriteLine($"[LoadDeviceConfig] 开始加载设备配置");

            // 确保使用服务中的权威 Device 实例（这样当机箱回退执行并更新 CardConfigData 时，面板能读取到最新数据）
            try
            {
                if (!string.IsNullOrWhiteSpace(Device?.Id) && _pxiChassisService != null)
                {
                    var svcDevice = _pxiChassisService.GetDeviceById(Device.Id);
                    if (svcDevice != null && !ReferenceEquals(svcDevice, Device))
                    {
                        Device = svcDevice;
                    }
                }
            }
            catch { }

            // 注册 dispatcher handler（按槽位）
            try
            {
                int slot = (Device as PxiDeviceBase)?.SlotIndex ?? -1;
                if (slot > 0 && slot != _registeredSlot)
                {
                    if (_registeredSlot > 0)
                    {
                        try { MeasureControl.Services.RemoteMatrixCommandDispatcher.Instance.Unregister(_registeredSlot); } catch { }
                    }

                    MeasureControl.Services.RemoteMatrixCommandDispatcher.Instance.Register(slot, async (args) =>
                    {
                        try
                        {
                            if (args == null) return false;
                            // args.InputNodeId like "r{n}", args.OutputNodeId like "c{m}"
                            int input = 0, output = 0;
                            if (!int.TryParse(args.InputNodeId.TrimStart('r','R'), out input)) return false;
                            if (!int.TryParse(args.OutputNodeId.TrimStart('c','C'), out output)) return false;

                            // PXI3022 is 4 rows x 64 cols
                            int row = input % 4;
                            int col = output % 64;
                            string channelId = $"R{row}C{col}";

                            // 获取驱动
                            var driverObj = MeasureControl.Drivers.DriverFactory.GetCachedDriver(Device?.Id, slot)
                                            ?? MeasureControl.Drivers.DriverFactory.CreateDriver(Device);

                            if (driverObj is MeasureControl.Drivers.PXI3022.PXI3022Driver pxi3022)
                            {
                                if (!pxi3022.IsConnected)
                                {
                                    var connected = await pxi3022.ConnectAsync().ConfigureAwait(false);
                                    Debug.WriteLine($"[3022 Handler] ConnectAsync result: {connected}");
                                    if (!connected) return false;
                                }

                                bool opResult = false;
                                if (args.State == 0)
                                {
                                    opResult = await pxi3022.WriteChannelAsync(channelId, 1.0).ConfigureAwait(false);
                                }
                                else
                                {
                                    opResult = await pxi3022.WriteChannelAsync(channelId, 0.0).ConfigureAwait(false);
                                }

                                Debug.WriteLine($"[3022 Handler] Channel {channelId} op result: {opResult}");

                                // 更新 CardConfigData 并持久化
                                if (Device?.CardConfigData is SwitchMatrixCardConfig cfg)
                                {
                                    var state = args.State == 0 ? SwitchConnectionState.Connected : SwitchConnectionState.Disconnected;
                                    cfg.SetConnection(args.InputNodeId, args.OutputNodeId, state);
                                    try { _pxiChassisService?.UpdateDeviceCardConfig(Device.Id, cfg); } catch { }
                                    try
                                    {
                                        _eventAggregator?.GetEvent<MeasureControl.Events.DeviceModifiedEvent>()?.Publish(new MeasureControl.Events.DeviceModifiedEventArgs
                                        {
                                            ChassisName = this.ChassisName,
                                            ModificationType = "RemoteCommand",
                                            Device = Device
                                        });
                                    }
                                    catch { }
                                }

                                return opResult;
                            }

                            return false;
                        }
                        catch
                        {
                            return false;
                        }
                    });

                    _registeredSlot = slot;
                }
            }
            catch { }

            if (Device?.CardConfigData is SwitchMatrixCardConfig cardConfig)
            {
                _cardConfig = cardConfig;

                // 加载配置到UI
                if (!string.IsNullOrEmpty(cardConfig.CardName))
                {
                    _cardName = cardConfig.CardName;
                    RaisePropertyChanged(nameof(CardName));
                }

                // 更新显示
                UpdateAllRelayStatus();
                UpdateConnectionCounts();
                RefreshMatrixTopology();

                Debug.WriteLine($"[LoadDeviceConfig] 设备配置已加载: {CardName}");
            }
            else
            {
                Debug.WriteLine($"[LoadDeviceConfig] 无设备配置，使用默认配置");
            }
        }

        /// <summary>
        /// 保存设备配置
        /// </summary>
        private void SaveDeviceConfig()
        {
            if (Device == null || _cardConfig == null)
            {
                Debug.WriteLine("[SaveDeviceConfig] 警告：设备或配置为空");
                return;
            }

            Debug.WriteLine($"[SaveDeviceConfig] 开始保存配置");

            // 记录当前的连接计数
            int totalConnectionCount = 0;
            int connectedCount = 0;

            foreach (var kvp in _cardConfig.ConnectionMap)
            {
                var conn = kvp.Value;
                totalConnectionCount += conn.ConnectionCount;

                if (conn.State == SwitchConnectionState.Connected)
                {
                    connectedCount++;
                    Debug.WriteLine($"[SaveDeviceConfig] 活跃连接: {conn.InputChannel}->{conn.OutputChannel}, " +
                                   $"计数={conn.ConnectionCount}, 状态={conn.State}");
                }
            }

            Debug.WriteLine($"[SaveDeviceConfig] 活跃连接数: {connectedCount}, 总连接次数: {totalConnectionCount}");

            // 保存到设备配置
            Device.CardConfigData = _cardConfig;

            // 更新服务层配置
            _pxiChassisService?.UpdateDeviceCardConfig(Device.Id, _cardConfig);

            // 触发项目修改事件
            _eventAggregator?.GetEvent<ProjectModifiedEvent>()?.Publish(new ProjectModifiedEventArgs
            {
                ModificationType = "PXI3022MatrixConfig",
                Description = $"PXI3022矩阵配置已更新: {CardName}，总连接次数: {totalConnectionCount}"
            });

            Debug.WriteLine($"[SaveDeviceConfig] 配置保存完成，总连接次数: {totalConnectionCount}");
        }

        /// <summary>
        /// 继电器状态信息ViewModel（用于重建被移除的类型，保持与旧逻辑兼容）
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
                        // notify
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
                ConnectionCount = Connection.ConnectionCount;
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
        /// 更新所有继电器状态
        /// </summary>
        private void UpdateAllRelayStatus()
        {
            Debug.WriteLine($"[UpdateAllRelayStatus] 开始更新所有继电器状态");

            if (_cardConfig == null)
            {
                Debug.WriteLine("[UpdateAllRelayStatus] 警告：cardConfig为空");
                return;
            }

            int updatedCount = 0;
            int openedCount = 0;

            foreach (var relay in RelayStatusList)
            {
                var connection = _cardConfig.GetConnection(relay.ConnectedInput, relay.ConnectedOutput);

                if (connection != null)
                {
                    // 如果设备未连接，强制设置连接状态为断开
                    if (!IsDeviceConnected)
                    {
                        connection.SetConnectionState(SwitchConnectionState.Disconnected);
                        Debug.WriteLine($"[UpdateAllRelayStatus] 设备未连接，强制设置连接 {relay.ConnectedInput}->{relay.ConnectedOutput} 为断开状态");
                    }

                    // 保存旧状态用于对比
                    bool wasOpened = relay.IsOpened;
                    int oldCount = relay.ConnectionCount;

                    // 更新连接对象
                    relay.Connection = connection;

                    // 触发更新
                    relay.UpdateFromConnection();

                    if (relay.IsOpened) openedCount++;

                    Debug.WriteLine($"[UpdateAllRelayStatus] 继电器 {relay.RelayName}: " +
                                   $"IsOpened={relay.IsOpened}, Count={relay.ConnectionCount}");

                    updatedCount++;
                }
            }

            // 更新活跃连接计数
            ActiveRelayCount = openedCount;
            RaisePropertyChanged(nameof(ActiveRelayCount));

            // 通知列表变更
            RaisePropertyChanged(nameof(RelayStatusList));

            Debug.WriteLine($"[UpdateAllRelayStatus] 完成更新 {updatedCount} 个继电器状态，活跃: {openedCount}");
        }

        /// <summary>
        /// 更新连接计数
        /// </summary>
        private void UpdateConnectionCounts()
        {
            if (_cardConfig == null)
            {
                Debug.WriteLine("[UpdateConnectionCounts] 警告：cardConfig为空");
                return;
            }

            // 计算活跃连接数
            ActiveRelayCount = _cardConfig.ActiveRelayCount;
            ErrorConnectionCount = _cardConfig.ErrorConnectionCount;

            // 通知总连接次数属性变化
            RaisePropertyChanged(nameof(TotalConnectionCount));

            Debug.WriteLine($"[UpdateConnectionCounts] 更新连接计数: " +
                           $"活跃={ActiveRelayCount}, 错误={ErrorConnectionCount}, " +
                           $"总连接次数={TotalConnectionCount}");
        }

        /// <summary>
        /// 更新活动连接列表
        /// </summary>
        private void UpdateActiveConnections()
        {
            Debug.WriteLine($"[UpdateActiveConnections] 开始更新活动连接列表");

            ActiveConnections.Clear();

            if (_cardConfig != null)
            {
                var active = _cardConfig.GetActiveConnections();
                Debug.WriteLine($"[UpdateActiveConnections] 配置中有 {active.Count()} 个活跃连接");

                foreach (var connection in active)
                {
                    ActiveConnections.Add(connection);
                }

                Debug.WriteLine($"[UpdateActiveConnections] 活动连接列表更新完成，当前有 {ActiveConnections.Count} 个连接");
            }
            else
            {
                Debug.WriteLine($"[UpdateActiveConnections] 警告：cardConfig为空");
            }
        }

        /// <summary>
        /// 更新继电器状态
        /// </summary>
        private void UpdateRelayStatus(string input, string output)
        {
            Debug.WriteLine($"[UpdateRelayStatus] 更新继电器状态: {input} -> {output}");

            var relayInfo = RelayStatusList.FirstOrDefault(r =>
                r.ConnectedInput == input && r.ConnectedOutput == output);

            if (relayInfo != null)
            {
                // 更新 Connection 对象
                var connection = _cardConfig.GetConnection(input, output);
                if (connection != null)
                {
                    relayInfo.Connection = connection;
                }
                else
                {
                    // 如果没有连接，手动更新
                    relayInfo.UpdateFromConnection();
                }

                Debug.WriteLine($"[UpdateRelayStatus] 继电器状态已更新: {relayInfo.DisplayText}");
            }
            else
            {
                Debug.WriteLine($"[UpdateRelayStatus] 警告：未找到对应的继电器信息");
            }
        }

        /// <summary>
        /// 重置所有连接计数
        /// </summary>
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

        /// <summary>
        /// 清除所有错误
        /// </summary>
        private void ClearAllErrors()
        {
            if (_cardConfig == null)
            {
                Debug.WriteLine("[ClearAllErrors] 警告：cardConfig为空");
                return;
            }

            _cardConfig.ClearAllErrors();
            UpdateAllRelayStatus();
            UpdateConnectionCounts();
            SaveDeviceConfig();

            ReMessageBox.Show(
                "所有错误状态已清除",
                "清除成功",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            Debug.WriteLine("[ClearAllErrors] 所有错误状态已清除");
        }

        #endregion

        #region 拓扑交互处理方法

        private void OnNodeClicked(TopologyNodeInfo node)
        {
            if (node == null || !IsDeviceConnected) return;

            if (FirstSelectedNode == null)
            {
                // 第一步：选择输入节点
                if (node.NodeType == "Input")
                {
                    FirstSelectedNode = node;
                    node.IsSelected = true;
                    RaisePropertyChanged(nameof(MatrixSelectionStatus));
                }
                else
                {
                    ReMessageBox.Show("请先选择一个输入节点", "提示",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            else
            {
                // 第二步：选择输出节点
                if (node.NodeType == "Output")
                {
                    _secondSelectedNode = node;

                    // 连接两个节点
                    Task.Run(async () =>
                    {
                        await ConnectNodesAsync(FirstSelectedNode.NodeId, node.NodeId);
                    });
                }
                else if (node.NodeType == "Input")
                {
                    // 用户点击了另一个输入节点 - 直接切换
                    FirstSelectedNode.IsSelected = false;
                    FirstSelectedNode = node;
                    node.IsSelected = true;
                    RaisePropertyChanged(nameof(MatrixSelectionStatus));
                }
                else
                {
                    ReMessageBox.Show("请选择一个输出节点", "提示",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }

        private void OnNodeRightClicked(TopologyNodeInfo node)
        {
            if (node == null || !node.IsConnected) return;

            var result = ReMessageBox.Show($"是否要断开 {node.NodeId} 的连接？",
                "断开连接",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                if (node.NodeType == "Input")
                {
                    var connectedOutput = _cardConfig?.GetConnectedOutput(node.NodeId);
                    if (!string.IsNullOrEmpty(connectedOutput))
                    {
                        Task.Run(async () =>
                        {
                            await DisconnectNodesAsync(node.NodeId, connectedOutput);
                        });
                    }
                }
                else if (node.NodeType == "Output")
                {
                    var connectedInput = _cardConfig?.GetConnectedInput(node.NodeId);
                    if (!string.IsNullOrEmpty(connectedInput))
                    {
                        Task.Run(async () =>
                        {
                            await DisconnectNodesAsync(connectedInput, node.NodeId);
                        });
                    }
                }
            }
        }



        private void OnNodeHovered(TopologyNodeInfo node)
        {
            if (node != null)
            {
                HoveredNode = node;
            }
        }

        private void OnConnectionRightClicked(TopologyConnectionInfo connection)
        {
            if (connection == null) return;

            var result = ReMessageBox.Show($"是否要断开连接 {connection.InputNodeId} → {connection.OutputNodeId}？",
                "断开连接",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                Task.Run(async () =>
                {
                    await DisconnectNodesAsync(connection.InputNodeId, connection.OutputNodeId);
                });
            }
        }

        private void OnMatrixConnectionRightClicked(MatrixConnectionViewModel connection)
        {
            if (connection == null) return;

            var result = ReMessageBox.Show($"是否要断开连接 {connection.InputNodeId} → {connection.OutputNodeId}？",
                "断开连接",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                Task.Run(async () =>
                {
                    await DisconnectNodesAsync(connection.InputNodeId, connection.OutputNodeId);
                });
            }
        }

        #endregion

        #region 矩阵拓扑交互方法

        /// <summary>
        /// 矩阵节点点击处理
        /// </summary>
        private void OnMatrixNodeClicked(MatrixNodeViewModel node)
        {
            if (node == null || !IsDeviceConnected) return;

            if (node.NodeType == "Input")
            {
                HandleInputNodeClick(node);
            }
            else if (node.NodeType == "Output")
            {
                HandleOutputNodeClick(node);
            }
        }

        /// <summary>
        /// 处理输入节点点击
        /// </summary>
        private void HandleInputNodeClick(MatrixNodeViewModel inputNode)
        {
            Debug.WriteLine($"[HandleInputNodeClick] 第一步：选择输入节点 {inputNode.NodeId}");

            // 清除之前的选择状态
            ClearAllSelectionStates();

            // 设置第一步选择状态
            SelectedInputNode = inputNode;
            inputNode.IsFirstStepSelected = true;

            // 清除第二步选择状态
            if (SelectedOutputNode != null)
            {
                SelectedOutputNode.IsSecondStepSelected = false;
                SelectedOutputNode = null;
            }

            // 清除待确认的交叉点
            if (_pendingCrossPoint != null)
            {
                _pendingCrossPoint.IsPendingConnection = false;
                _pendingCrossPoint = null;
            }

            // 更新状态显示
            RaisePropertyChanged(nameof(MatrixSelectionStatus));
        }

        /// <summary>
        /// 处理输出节点点击
        /// </summary>
        private void HandleOutputNodeClick(MatrixNodeViewModel outputNode)
        {
            Debug.WriteLine($"[HandleOutputNodeClick] 开始 - 输入: {SelectedInputNode?.NodeId}, 输出: {outputNode.NodeId}");

            if (SelectedInputNode == null)
            {
                Debug.WriteLine("[HandleOutputNodeClick] 错误：没有选择输入节点");
                ReMessageBox.Show("请先选择一个输入节点", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // 设置第二步选择状态
            SelectedOutputNode = outputNode;
            outputNode.IsSecondStepSelected = true;

            Debug.WriteLine($"[HandleOutputNodeClick] 设置输出节点选择状态: {outputNode.NodeId}");

            // 找到对应的交叉点
            var crossPoint = CrossPoints.FirstOrDefault(cp =>
                cp.InputNodeId == SelectedInputNode.NodeId &&
                cp.OutputNodeId == outputNode.NodeId);

            if (crossPoint != null)
            {
                Debug.WriteLine($"[HandleOutputNodeClick] 找到交叉点: {crossPoint.DisplayName}, 当前连接状态: {crossPoint.IsConnected}");

                // 检查是否已经连接
                if (crossPoint.IsConnected)
                {
                    Debug.WriteLine($"[HandleOutputNodeClick] 连接已存在，询问是否断开");

                    var result = ReMessageBox.Show(
                        $"连接 {SelectedInputNode.NodeId} → {outputNode.NodeId} 已经存在，是否断开？",
                        "断开连接",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        Debug.WriteLine($"[HandleOutputNodeClick] 用户确认断开连接");

                        Task.Run(async () =>
                        {
                            await DisconnectNodesAsync(SelectedInputNode.NodeId, outputNode.NodeId);
                        });
                    }
                    else
                    {
                        Debug.WriteLine($"[HandleOutputNodeClick] 用户取消断开连接");
                    }

                    // 清除选择状态
                    ClearAllSelectionStates();
                    return;
                }

                // 设置交叉点为待确认状态
               // Debug.WriteLine($"[HandleOutputNodeClick] 设置交叉点为待确认状态");
                crossPoint.IsPendingConnection = true;
                crossPoint.ConnectionColor = "#FF9800";
                _pendingCrossPoint = crossPoint;

                // 显示连接确认对话框
                Debug.WriteLine($"[HandleOutputNodeClick] 显示连接确认对话框");
                ShowConnectionConfirmationDialog();
            }
            else
            {
                Debug.WriteLine($"[HandleOutputNodeClick] 错误：没有找到交叉点");
            }

            // 更新状态显示
            RaisePropertyChanged(nameof(MatrixSelectionStatus));
        }

        /// <summary>
        /// 显示连接确认对话框
        /// </summary>
        private void ShowConnectionConfirmationDialog()
        {
            if (SelectedInputNode == null || SelectedOutputNode == null)
            {
                Debug.WriteLine("[ShowConnectionConfirmationDialog] 错误：输入或输出节点为空");
                return;
            }

            Debug.WriteLine($"[ShowConnectionConfirmationDialog] 显示确认对话框: {SelectedInputNode.NodeId} -> {SelectedOutputNode.NodeId}");

            var result = ReMessageBox.Show(
                $"确认要连接 {SelectedInputNode.NodeId} → {SelectedOutputNode.NodeId} 吗？",
                "确认连接",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                Debug.WriteLine($"[ShowConnectionConfirmationDialog] 用户确认连接");

                Task.Run(async () =>
                {
                    await ConnectNodesAsync(SelectedInputNode.NodeId, SelectedOutputNode.NodeId);
                });
            }
            else
            {
                Debug.WriteLine($"[ShowConnectionConfirmationDialog] 用户取消连接");
                CancelConnection();
            }
        }

        /// <summary>
        /// 确认连接
        /// </summary>
        private void ConfirmConnection()
        {
            Debug.WriteLine("[ConfirmConnection] 确认连接");

            if (SelectedInputNode != null && SelectedOutputNode != null)
            {
                Task.Run(async () =>
                {
                    await ConnectNodesAsync(SelectedInputNode.NodeId, SelectedOutputNode.NodeId);
                });
            }
            else
            {
                Debug.WriteLine("[ConfirmConnection] 错误：没有选择输入或输出节点");
            }
        }

        /// <summary>
        /// 取消连接
        /// </summary>
        private void CancelConnection()
        {
            Debug.WriteLine("[CancelConnection] 取消连接");

            // 清除选择状态
            ClearAllSelectionStates();

            // 清除待确认的交叉点
            if (_pendingCrossPoint != null)
            {
                _pendingCrossPoint.IsPendingConnection = false;
                _pendingCrossPoint = null;
            }

            // 更新状态显示
            RaisePropertyChanged(nameof(MatrixSelectionStatus));
        }

        /// <summary>
        /// 交叉点点击处理
        /// </summary>
        private async void OnCrossPointClicked(CrossPointViewModel crossPoint)
        {
            if (crossPoint == null) return;

            if (crossPoint.IsConnected)
            {
                await DisconnectNodesAsync(crossPoint.InputNodeId, crossPoint.OutputNodeId);
            }
            else
            {
                var inputNode = MatrixNodes.FirstOrDefault(n => n.NodeId == crossPoint.InputNodeId);
                var outputNode = MatrixNodes.FirstOrDefault(n => n.NodeId == crossPoint.OutputNodeId);

                if (inputNode != null && outputNode != null)
                {
                    HandleInputNodeClick(inputNode);
                    HandleOutputNodeClick(outputNode);
                }
            }
        }

        /// <summary>
        /// 悬停交叉点
        /// </summary>
        private void OnCrossPointHovered(CrossPointViewModel crossPoint)
        {
            if (crossPoint == null) return;

            if (!crossPoint.IsSelected && !crossPoint.IsPendingConnection)
            {
                crossPoint.ConnectionColor = "#FFC107";
            }

            Debug.WriteLine($"交叉点悬停: {crossPoint.DisplayName}");
        }

        /// <summary>
        /// 离开交叉点
        /// </summary>
        private void OnCrossPointMouseLeave(CrossPointViewModel crossPoint)
        {
            if (crossPoint == null) return;

            if (!crossPoint.IsSelected && !crossPoint.IsPendingConnection)
            {
                if (crossPoint.IsConnected)
                {
                    crossPoint.ConnectionColor = "#4CAF50";
                }
                else
                {
                    crossPoint.ConnectionColor = null;
                }
            }
        }



        /// <summary>
        /// 清除所有选择状态
        /// </summary>
        private void ClearAllSelectionStates()
        {
            Debug.WriteLine("[ClearAllSelectionStates] 开始清除所有选择状态");

            // 清除所有节点的选择状态
            foreach (var node in MatrixNodes)
            {
                node.IsFirstStepSelected = false;
                node.IsSecondStepSelected = false;
            }

            // 清除所有交叉点的选择状态
            foreach (var crossPoint in CrossPoints)
            {
                crossPoint.IsSelected = false;
                crossPoint.IsPendingConnection = false;
            }

            // 清除引用
            SelectedInputNode = null;
            SelectedOutputNode = null;
            _pendingCrossPoint = null;

            Debug.WriteLine("[ClearAllSelectionStates] 所有选择状态已清除");
        }

        /// <summary>
        /// 矩阵节点右键点击处理
        /// </summary>
        private async void OnMatrixNodeRightClicked(MatrixNodeViewModel node)
        {
            if (node == null || !node.IsConnected || !IsDeviceConnected) return;

            Debug.WriteLine($"[OnMatrixNodeRightClicked] 右键点击已连接的节点: {node.NodeId} ({node.NodeType})");

            string message = "";
            string connectedNode = "";

            if (node.NodeType == "Input")
            {
                connectedNode = _cardConfig?.GetConnectedOutput(node.NodeId);
                message = $"是否要断开输入节点 {node.NodeId} 到输出节点 {connectedNode} 的连接？";
            }
            else if (node.NodeType == "Output")
            {
                connectedNode = _cardConfig?.GetConnectedInput(node.NodeId);
                message = $"是否要断开输入节点 {connectedNode} 到输出节点 {node.NodeId} 的连接？";
            }

            if (string.IsNullOrEmpty(connectedNode))
            {
                Debug.WriteLine($"[OnMatrixNodeRightClicked] 警告：未找到连接的节点");
                return;
            }

            var result = ReMessageBox.Show(
                message,
                "断开连接",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                Debug.WriteLine($"[OnMatrixNodeRightClicked] 用户确认断开连接");

                if (node.NodeType == "Input")
                {
                    await DisconnectNodesAsync(node.NodeId, connectedNode);
                }
                else if (node.NodeType == "Output")
                {
                    await DisconnectNodesAsync(connectedNode, node.NodeId);
                }
            }
            else
            {
                Debug.WriteLine($"[OnMatrixNodeRightClicked] 用户取消断开连接");
            }
        }

        /// <summary>
        /// 断开交叉点命令处理
        /// </summary>
        private async void OnDisconnectCrossPoint(CrossPointViewModel crossPoint)
        {
            if (crossPoint == null) return;

            Debug.WriteLine($"[OnDisconnectCrossPoint] 右键点击断开已连接的交叉点: {crossPoint.DisplayName}");

            var result = ReMessageBox.Show(
                $"是否要断开连接 {crossPoint.InputNodeId} → {crossPoint.OutputNodeId}？",
                "断开连接",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                Debug.WriteLine($"[OnDisconnectCrossPoint] 用户确认断开连接");
                await DisconnectNodesAsync(crossPoint.InputNodeId, crossPoint.OutputNodeId);
            }
        }

        /// <summary>
        /// 连接交叉点命令处理
        /// </summary>
        private async void OnConnectCrossPoint(CrossPointViewModel crossPoint)
        {
            if (crossPoint == null) return;

            Debug.WriteLine($"[OnConnectCrossPoint] 右键点击连接交叉点: {crossPoint.DisplayName}");

            var result = ReMessageBox.Show(
                $"是否要连接 {crossPoint.InputNodeId} → {crossPoint.OutputNodeId}？",
                "连接交叉点",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                Debug.WriteLine($"[OnConnectCrossPoint] 用户确认连接交叉点");

                // 直接调用异步方法，和PXI-2601保持一致（不使用额外的 Task.Run 包装）
                await ConnectNodesAsync(crossPoint.InputNodeId, crossPoint.OutputNodeId);
            }
        }

        #endregion

        #region 连接和断开连接方法



        /// <summary>
        /// 立即更新交叉点连接状态（用于即时反馈）
        /// </summary>
        private void UpdateCrossPointImmediately(string inputNodeId, string outputNodeId, bool isConnected, string color = null)
        {
            var crossPoint = CrossPoints.FirstOrDefault(cp =>
                cp.InputNodeId == inputNodeId &&
                cp.OutputNodeId == outputNodeId);

            if (crossPoint != null)
            {
                crossPoint.IsConnected = isConnected;

                if (isConnected)
                {
                    crossPoint.ConnectionColor = color ?? "#4CAF50";
                }
                else
                {
                    crossPoint.ConnectionColor = color;
                }
            }
        }

        /// <summary>
        /// 立即更新节点连接状态（用于即时反馈）
        /// </summary>
        private void UpdateNodeConnectionImmediately(string nodeId, bool isConnected)
        {
            var node = MatrixNodes.FirstOrDefault(n => n.NodeId == nodeId);
            if (node != null)
            {
                node.IsConnected = isConnected;
            }
        }

        /// <summary>
        /// 立即更新继电器状态显示
        /// </summary>
        private void UpdateRelayStatusInUI(string inputNodeId, string outputNodeId)
        {
            var relayInfo = RelayStatusList.FirstOrDefault(r =>
                r.ConnectedInput == inputNodeId && r.ConnectedOutput == outputNodeId);

            if (relayInfo != null)
            {
                var connection = _cardConfig.GetConnection(inputNodeId, outputNodeId);
                if (connection != null)
                {
                    relayInfo.Connection = connection;
                    relayInfo.UpdateFromConnection();
                }
            }
        }

        #endregion

        #region 矩阵拓扑布局方法

        /// <summary>
        /// 刷新矩阵拓扑
        /// </summary>
        private void RefreshMatrixTopology()
        {
            if (_cardConfig == null) return;

            // PXI3022 固定为 4x64 矩阵
            int inputCount = 4;
            int outputCount = 64;

            // 清空集合
            CrossPoints.Clear();
            VerticalLines.Clear();
            HorizontalLines.Clear();
            InputLabels.Clear();
            OutputLabels.Clear();
            MatrixNodes.Clear();
            CrossPointsPage1.Clear();
            CrossPointsPage2.Clear();
            MatrixNodesPage1.Clear();
            MatrixNodesPage2.Clear();
            VerticalLinesPage1.Clear();
            VerticalLinesPage2.Clear();
            HorizontalLinesPage1.Clear();
            HorizontalLinesPage2.Clear();

            // 根据输出数量动态计算画布宽度
            double minVerticalSpacing = 20;
            double minHorizontalSpacing = 25;

            double marginLeft = 20;
            double marginRight = 10;
            double marginTop = 30;
            double marginBottom = 30;

            double extensionLength = 15;
            double nodeRadius = 10;

            // 如果有可用空间，根据可见页面宽度或可用空间计算间距
            double availableWidth = CanvasViewportWidthPage1 > 0 ? CanvasViewportWidthPage1 : AvailableWidth;
            double availableHeight = AvailableHeight;
            double horizontalSpacing = minHorizontalSpacing;
            double verticalSpacing = minVerticalSpacing;

            if (availableWidth > 0 && availableHeight > 0)
            {
                // 对于PXI3022分页显示，使用固定的画布高度避免闪烁
                CanvasWidth = availableWidth;
                // 只在第一次设置时初始化CanvasHeight，避免动态变化导致的闪烁
                if (CanvasHeight == 0)
                {
                    CanvasHeight = Math.Max(600, availableHeight * 0.8); // 使用80%的可用高度作为初始值
                }

                // 计算可用的网格空间
                double availableGridWidth = CanvasWidth - marginLeft - marginRight - extensionLength - nodeRadius * 2;
                double availableGridHeight = CanvasHeight - marginTop - marginBottom - extensionLength - nodeRadius * 2;

                // 计算基于可用空间的最佳间距
                if (outputCount > 1)
                {
                    horizontalSpacing = availableGridWidth / (outputCount - 1);
                    if (horizontalSpacing < minHorizontalSpacing)
                    {
                        horizontalSpacing = minHorizontalSpacing;
                        double requiredWidth = marginLeft + marginRight + (outputCount - 1) * horizontalSpacing + extensionLength + nodeRadius * 2;
                        if (requiredWidth > availableWidth)
                        {
                            horizontalSpacing = (availableWidth - marginLeft - marginRight - extensionLength - nodeRadius * 2) / (outputCount - 1);
                        }
                    }
                }

                if (inputCount > 1)
                {
                    verticalSpacing = availableGridHeight / (inputCount - 1);
                    if (verticalSpacing < minVerticalSpacing)
                    {
                        verticalSpacing = minVerticalSpacing;
                        double requiredHeight = marginTop + marginBottom + (inputCount - 1) * verticalSpacing + extensionLength + nodeRadius * 2;
                        if (requiredHeight > availableHeight)
                        {
                            verticalSpacing = (availableHeight - marginTop - marginBottom - extensionLength - nodeRadius * 2) / (inputCount - 1);
                        }
                    }
                }
            }
            else
            {
                // 使用默认计算方式
                double requiredWidth = marginLeft + marginRight + (outputCount - 1) * minHorizontalSpacing + extensionLength + nodeRadius * 2;

                CanvasWidth = Math.Max(800, requiredWidth);
                // 使用固定的默认高度，避免动态变化
                if (CanvasHeight == 0)
                {
                    CanvasHeight = 600; // 使用与初始值一致的默认高度
                }

                horizontalSpacing = minHorizontalSpacing;
                verticalSpacing = horizontalSpacing * 2.0;
            }

            // 根据计算出的间距调整画布尺寸
            double calculatedHeight = marginTop + marginBottom + extensionLength + nodeRadius * 2 + (inputCount - 1) * verticalSpacing;
            double calculatedWidth = marginLeft + marginRight + (outputCount - 1) * horizontalSpacing + extensionLength + nodeRadius * 2;

            // 仅在宽度显著变化或未初始化时更新CanvasWidth，避免小变动触发重绘
            if (CanvasWidth == 0 || Math.Abs(CanvasWidth - calculatedWidth) > 30)
            {
            CanvasWidth = calculatedWidth;
            }
            // 仅在高度显著变化或未初始化时设置CanvasHeight，避免频繁抖动
            if (CanvasHeight == 0 || Math.Abs(CanvasHeight - calculatedHeight) > 30)
            {
                CanvasHeight = calculatedHeight;
            }

            // 计算分页画布宽度（上下半部分分别展示输出的前半/后半）
            int outputsPerPage = outputCount / 2;
            double pageCalculatedWidth = marginLeft + marginRight + (outputsPerPage - 1) * horizontalSpacing + extensionLength + nodeRadius * 2;

            // 优先使用可用宽度使分页画布占满容器（避免为另一页预留大段空白）
            if (availableWidth > 0)
            {
                // 使用可用宽度作为每页画布宽度（保留最小宽度限制）
                CanvasWidthPage1 = Math.Max(600, availableWidth);
                CanvasWidthPage2 = Math.Max(600, availableWidth);
            }
            else
            {
                CanvasWidthPage1 = Math.Max(600, pageCalculatedWidth);
                CanvasWidthPage2 = Math.Max(600, pageCalculatedWidth);
            }

            // 每页的网格宽度（用于在分页画布上绘制水平延伸线）
            double pageGridWidth = CanvasWidthPage1 - marginLeft - marginRight - extensionLength - nodeRadius * 2;
            pageGridWidth = Math.Max(pageGridWidth, 0);

            // 基于每页网格宽度计算页内列间距（确保列在当前页面内平均分布，从而“占满整个区域”）
            double pageHorizontalSpacing = (outputsPerPage > 1) ? (pageGridWidth / (outputsPerPage - 1)) : minHorizontalSpacing;
            if (pageHorizontalSpacing < minHorizontalSpacing)
            {
                pageHorizontalSpacing = minHorizontalSpacing;
                // 如果页内所需宽度超过当前页面宽度，则保持 pageHorizontalSpacing 为 min，再依赖滚动条
            }
            Debug.WriteLine($"[RefreshMatrixTopology] availableWidth:{availableWidth}, CanvasWidthPage1:{CanvasWidthPage1}, pageGridWidth:{pageGridWidth}, pageHorizontalSpacing:{pageHorizontalSpacing}");

            // 重新计算网格尺寸
            double gridWidth = CanvasWidth - marginLeft - marginRight - extensionLength - nodeRadius * 2;
            double gridHeight = CanvasHeight - marginTop - marginBottom - extensionLength - nodeRadius * 2;

            gridWidth = Math.Max(gridWidth, 0);
            gridHeight = Math.Max(gridHeight, 0);

            // 创建水平线（4条输入线），为每页分别创建，避免跨页占满整宽
            // 注意：水平线只添加到分页集合，不添加到全局集合，避免重复绘制
            double horizontalStartX = marginLeft + extensionLength + nodeRadius;
            for (int i = 0; i < inputCount; i++)
            {
                double y = marginTop + extensionLength + nodeRadius + i * verticalSpacing;

                var horizontalLinePage1 = new LineViewModel(
                    horizontalStartX, y,
                    horizontalStartX + pageGridWidth, y,
                    "Horizontal"
                );
                var horizontalLinePage2 = new LineViewModel(
                    horizontalStartX, y,
                    horizontalStartX + pageGridWidth, y,
                    "Horizontal"
                );

                HorizontalLinesPage1.Add(horizontalLinePage1);
                HorizontalLinesPage2.Add(horizontalLinePage2);
            }

            // 创建垂直线（分两页，各32列），使用每页间距pageHorizontalSpacing填充当前页面
            // 注意：垂直线只添加到对应的分页集合，不添加到全局集合，避免重复绘制
            for (int page = 0; page < 2; page++)
            {
                for (int jPage = 0; jPage < outputsPerPage; jPage++)
                {
                    // 页内索引转为全局输出索引
                    int globalIndex = page * outputsPerPage + jPage;
                    double x = marginLeft + extensionLength + nodeRadius + jPage * pageHorizontalSpacing;

                    var verticalLine = new LineViewModel(
                        x, marginTop + extensionLength + nodeRadius,
                        x, marginTop + extensionLength + nodeRadius + gridHeight,
                        "Vertical"
                    );

                    // 只加入对应的分页集合，避免重复绘制
                    if (page == 0)
                        VerticalLinesPage1.Add(verticalLine);
                    else
                        VerticalLinesPage2.Add(verticalLine);
                }
            }

            // 创建输入节点和延长线
            for (int i = 0; i < inputCount; i++)
            {
                double y = marginTop + extensionLength + nodeRadius + i * verticalSpacing;

                double lineStartX = marginLeft;
                double lineEndX = marginLeft + extensionLength;
                double circleX = lineStartX - nodeRadius;

                // 创建输入节点的延长线（只加入分页集合，避免全局重复）
                var inputExtensionLine = new LineViewModel(
                    lineStartX, y,
                    lineEndX, y,
                    "Extension"
                );
                HorizontalLinesPage1.Add(inputExtensionLine);
                HorizontalLinesPage2.Add(inputExtensionLine);

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
                // 输入节点在上下两页都应显示
                MatrixNodesPage1.Add(inputNode);
                MatrixNodesPage2.Add(inputNode);

                // 添加标签
                var inputLabel = new LabelViewModel(
                    $"r{i}",
                    circleX - 5,
                    y - 7,
                    9,
                    "Normal"
                );
                InputLabels.Add(inputLabel);
            }

            // 创建输出节点和延长线（按页生成，每页 32 列，页内位置从0开始）
            for (int page = 0; page < 2; page++)
            {
                for (int jPage = 0; jPage < outputsPerPage; jPage++)
                {
                    int globalIndex = page * outputsPerPage + jPage;
                    double x = marginLeft + extensionLength + nodeRadius + jPage * pageHorizontalSpacing;

                    double lineStartY = marginTop;
                    double lineEndY = marginTop + extensionLength;
                    double circleY = lineStartY - nodeRadius;

                    // 创建输出节点的延长线（只加入对应的分页集合）
                    var outputExtensionLine = new LineViewModel(
                        x, lineStartY,
                        x, lineEndY,
                        "Extension"
                    );
                    if (page == 0)
                        VerticalLinesPage1.Add(outputExtensionLine);
                    else
                        VerticalLinesPage2.Add(outputExtensionLine);

                    var outputNode = new MatrixNodeViewModel($"c{globalIndex}", "Output",
                        x, lineStartY)
                    {
                        IsConnected = _cardConfig.IsOutputConnected($"c{globalIndex}"),
                        DisplayX = x - nodeRadius,
                        DisplayY = circleY,
                        NodeColor = "#F44336",
                        Radius = nodeRadius * 2
                    };
                    MatrixNodes.Add(outputNode);

                    if (page == 0)
                        MatrixNodesPage1.Add(outputNode);
                    else
                        MatrixNodesPage2.Add(outputNode);

                    // 添加标签（页内位置，仅显示属于本页的标签）
                    var outputLabel = new LabelViewModel(
                        $"c{globalIndex}",
                        x - 6,
                        circleY - 5,
                        9,
                        "Normal"
                    );
                    OutputLabels.Add(outputLabel);
                }
            }

            // 创建交叉点网格（按页生成：每页只创建属于该页的交叉点，页内列索引从0开始）
            for (int i = 0; i < inputCount; i++)
            {
                for (int page = 0; page < 2; page++)
                {
                    for (int jPage = 0; jPage < outputsPerPage; jPage++)
                    {
                        int globalIndex = page * outputsPerPage + jPage;
                        double x = marginLeft + extensionLength + nodeRadius + jPage * pageHorizontalSpacing;
                        double y = marginTop + extensionLength + nodeRadius + i * verticalSpacing;

                        var crossPoint = new CrossPointViewModel(
                            $"CP_{i}_{globalIndex}",
                            $"r{i}",
                            $"c{globalIndex}",
                            x - 8,
                            y - 8,
                            $"r{i} ↔ c{globalIndex}"
                        )
                        {
                            IsConnected = _cardConfig.GetConnection($"r{i}", $"c{globalIndex}")?.State == SwitchConnectionState.Connected,
                            Size = 16
                        };

                        // 同时加入全局集合和对应的分页集合
                        CrossPoints.Add(crossPoint);
                        if (page == 0)
                            CrossPointsPage1.Add(crossPoint);
                        else
                            CrossPointsPage2.Add(crossPoint);
                    }
                }
            }

            // 更新交叉点的连接状态
            UpdateCrossPointsConnectionStatus();
        }

        /// <summary>
        /// 更新交叉点连接状态
        /// </summary>
        private void UpdateCrossPointsConnectionStatus()
        {
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
                bool isConnected = connection?.State == SwitchConnectionState.Connected;

                Debug.WriteLine($"[UpdateCrossPointsConnectionStatus] 交叉点 {crossPoint.CrossPointId} - 输入:{crossPoint.InputNodeId}, 输出:{crossPoint.OutputNodeId}, 连接:{connection?.State}, isConnected:{isConnected}, IsPendingConnection:{crossPoint.IsPendingConnection}");

                if (isConnected) connectedCount++;
                else disconnectedCount++;

                crossPoint.IsConnected = isConnected;

                if (isConnected)
                {
                    crossPoint.ConnectionColor = "#4CAF50";
                    //Debug.WriteLine($"[UpdateCrossPointsConnectionStatus] 设置交叉点 {crossPoint.CrossPointId} 为绿色");
                }
                else
                {
                    if (!crossPoint.IsPendingConnection)
                    {
                        crossPoint.ConnectionColor = null;
                       // Debug.WriteLine($"[UpdateCrossPointsConnectionStatus] 设置交叉点 {crossPoint.CrossPointId} 为默认颜色");
                    }
                    else
                    {
                       // Debug.WriteLine($"[UpdateCrossPointsConnectionStatus] 交叉点 {crossPoint.CrossPointId} 是待处理连接，保持颜色不变");
                    }
                }
            }

            //Debug.WriteLine($"[UpdateCrossPointsConnectionStatus] 完成更新: 已连接 {connectedCount}, 未连接 {disconnectedCount}");
        }

        /// <summary>
        /// 更新矩阵节点连接状态
        /// </summary>
        private void UpdateMatrixNodesConnectionStatus()
        {
            if (_cardConfig == null)
            {
                Debug.WriteLine("[UpdateMatrixNodesConnectionStatus] 警告：cardConfig为空");
                return;
            }

           // Debug.WriteLine($"[UpdateMatrixNodesConnectionStatus] 开始更新所有节点状态，节点总数: {MatrixNodes.Count}");

            // 获取所有活跃连接
            var activeConnections = _cardConfig.GetActiveConnections().ToList();
            Debug.WriteLine($"[UpdateMatrixNodesConnectionStatus] 当前活跃连接数: {activeConnections.Count}");

            // 创建输入和输出节点的连接状态集合
            var connectedInputs = new HashSet<string>();
            var connectedOutputs = new HashSet<string>();

            foreach (var connection in activeConnections)
            {
                connectedInputs.Add(connection.InputChannel);
                connectedOutputs.Add(connection.OutputChannel);
            }

            // 更新所有节点状态
            foreach (var node in MatrixNodes)
            {
                bool isConnected = false;

                if (node.NodeType == "Input")
                {
                    isConnected = connectedInputs.Contains(node.NodeId);
                }
                else if (node.NodeType == "Output")
                {
                    isConnected = connectedOutputs.Contains(node.NodeId);
                }

               // Debug.WriteLine($"[UpdateMatrixNodesConnectionStatus] 节点 {node.NodeId} ({node.NodeType}): {isConnected}");
                node.IsConnected = isConnected;
            }

          //  Debug.WriteLine($"[UpdateMatrixNodesConnectionStatus] 完成更新");
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
                SaveDeviceConfig();
                StopStatusTimer();

                Task.Run(async () =>
                {
                    try
                    {
                        if (!KeepMatrixConnectionOnClose)
                        {
                            if (_driver != null && IsDeviceConnected)
                            {
                                await DisconnectAllAsync();
                                await DisconnectDeviceAsync();
                            }
                        }
                        else
                        {
                            Debug.WriteLine($"[SwitchPXI3022Control] KeepMatrixConnectionOnClose=true，跳过断开连接操作");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[SwitchPXI3022Control] Dispose 异常: {ex.Message}");
                    }
                }).Wait(TimeSpan.FromSeconds(3));
                
                // 清理TCP连接
                CleanupTcpConnection();

                // 停止拥有的TCP服务器
                foreach (var boardIdentifier in _ownedTcpServerIdentifiers.ToArray())
                {
                    StopTcpServer(boardIdentifier);
                }
                _ownedTcpServerIdentifiers.Clear();

                try
                {
                    _eventAggregator?.GetEvent<MeasureControl.Events.DeviceModifiedEvent>()?.Unsubscribe(OnDeviceModified);
                }
                catch { }
                try
                {
                    _eventAggregator?.GetEvent<MeasureControl.Events.RemoteMatrixCommandEvent>()?.Unsubscribe(OnRemoteMatrixCommand);
                }
                catch { }
                try
                {
                    if (_registeredSlot > 0)
                        MeasureControl.Services.RemoteMatrixCommandDispatcher.Instance.Unregister(_registeredSlot);
                }
                catch { }

                // 停止所有拥有的TCP服务器
                try
                {
                    foreach (var boardIdentifier in _ownedTcpServerIdentifiers.ToArray())
                    {
                        StopTcpServer(boardIdentifier);
                    }
                    _ownedTcpServerIdentifiers.Clear();
                }
                catch { }
            }

            _disposed = true;
        }

        #endregion


        #region TCP服务器方法

        /// <summary>
        /// 获取TCP监听端口
        /// </summary>
        private int GetTcpListenPort()
        {
            int slotIndex;
            try
            {
                slotIndex = (Device as MeasureControl.Models.Devices.DeviceCategories.PxiDeviceBase)?.SlotIndex ?? 0;
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

            int port = TcpBasePort3022 + slotIndex;
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
                lock (TcpServersLock)
                {
                    if (_tcpServers.TryGetValue(boardIdentifier, out var existing))
                    {
                        existing.RefCount++;
                        _ownedTcpServerIdentifiers.Add(boardIdentifier);
                        Debug.WriteLine($"[StartTcpServerForPort] TCP服务器复用: {boardIdentifier}, RefCount={existing.RefCount}");
                        return;
                    }

                    Debug.WriteLine($"[StartTcpServerForPort] 启动TCP服务器: 端口={port}, 板卡={boardIdentifier}");

                    var serverInfo = new TcpServerInfo
                    {
                        Port = port,
                        BoardIdentifier = boardIdentifier,
                        Cts = new CancellationTokenSource(),
                        RefCount = 1
                    };

                    serverInfo.Listener = new TcpListener(IPAddress.Any, port);
                    serverInfo.Listener.Start();
                    Debug.WriteLine($"[StartTcpServerForPort] Listening LocalEndpoint={serverInfo.Listener.LocalEndpoint}");

                    var token = serverInfo.Cts.Token;
                    serverInfo.AcceptTask = Task.Run(() => AcceptLoopAsync(serverInfo, token));

                    _tcpServers[boardIdentifier] = serverInfo;
                    _ownedTcpServerIdentifiers.Add(boardIdentifier);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[StartTcpServerForPort] 启动失败: {ex.Message}");
                StopTcpServer(boardIdentifier);
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
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[AcceptLoopAsync] 接受连接异常: {ex.Message}");
                        continue;
                    }

                    if (client != null)
                    {
                        _ = Task.Run(() => HandleClientAsync(client, serverInfo, token));
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AcceptLoopAsync] 异常: {ex.Message}");
            }
        }


        /// <summary>
        /// 停止指定板卡的TCP服务器
        /// </summary>
        private void StopTcpServer(string boardIdentifier)
        {
            try
            {
                lock (TcpServersLock)
                {
                    if (!_tcpServers.TryGetValue(boardIdentifier, out var serverInfo))
                        return;

                    if (serverInfo.RefCount > 1)
                    {
                        serverInfo.RefCount--;
                        _ownedTcpServerIdentifiers.Remove(boardIdentifier);
                        Debug.WriteLine($"[StopTcpServer] TCP服务器引用减少: {boardIdentifier}, RefCount={serverInfo.RefCount}");
                        return;
                    }

                    Debug.WriteLine($"[StopTcpServer] 停止TCP服务器: {boardIdentifier}");

                    serverInfo.Cts?.Cancel();
                    serverInfo.Listener?.Stop();
                    serverInfo.AcceptTask?.Wait(5000); // 等待最多5秒

                    _tcpServers.Remove(boardIdentifier);
                    _ownedTcpServerIdentifiers.Remove(boardIdentifier);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[StopTcpServer] 停止失败: {ex.Message}");
            }
        }

        #endregion

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

        #region TCP客户端方法

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

        private async Task DisconnectAllRemoteConnectionsAsync()
        {
            try
            {
                // 发送断开所有连接的命令
                var cmd = new byte[] { 0xFF, 0xFF, 0 };

                int slotIndex = (Device as MeasureControl.Models.Devices.DeviceCategories.PxiDeviceBase)?.SlotIndex ?? 0;
                int port = TcpBasePort3022 + slotIndex;

                Debug.WriteLine($"[DisconnectAllRemoteConnectionsAsync] Sending disconnect all to {RemoteClientIpAddress}:{port}");

                using (var client = new TcpClient())
                {
                    await client.ConnectAsync(RemoteClientIpAddress, port);
                    using (var stream = client.GetStream())
                    {
                        await stream.WriteAsync(cmd, 0, cmd.Length);
                        await stream.FlushAsync();

                        var ack = new byte[3];
                        int read = await ReadExactAsync(stream, ack, 0, ack.Length, CancellationToken.None);
                        if (read == 3)
                        {
                            Debug.WriteLine($"[DisconnectAllRemoteConnectionsAsync] ACK: {BitConverter.ToString(ack)}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DisconnectAllRemoteConnectionsAsync] 异常: {ex.Message}");
            }
        }

        private async Task SendRemoteDriverControlAsync(byte state)
        {
            try
            {
                // 发送驱动控制命令
                var cmd = new byte[] { 0xFF, 0xFF, state };

                int slotIndex = (Device as MeasureControl.Models.Devices.DeviceCategories.PxiDeviceBase)?.SlotIndex ?? 0;
                int port = TcpBasePort3022 + slotIndex;

                Debug.WriteLine($"[SendRemoteDriverControlAsync] Sending driver control to {RemoteClientIpAddress}:{port}, state={state}");

                using (var client = new TcpClient())
                {
                    await client.ConnectAsync(RemoteClientIpAddress, port);
                    using (var stream = client.GetStream())
                    {
                        await stream.WriteAsync(cmd, 0, cmd.Length);
                        await stream.FlushAsync();

                        var ack = new byte[3];
                        int read = await ReadExactAsync(stream, ack, 0, ack.Length, CancellationToken.None);
                        if (read == 3)
                        {
                            Debug.WriteLine($"[SendRemoteDriverControlAsync] ACK: {BitConverter.ToString(ack)}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SendRemoteDriverControlAsync] 异常: {ex.Message}");
            }
        }

        #endregion
    }
}
#endregion