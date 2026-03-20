using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Prism.Mvvm;

namespace MeasureControl.Models
{
    /// <summary>
    /// 板卡配置数据基类 - 所有板卡配置的公共属性
    /// </summary>
    public abstract class CardConfigDataBase : BindableBase
    {
        private string _cardId;
        private string _cardName;
        private string _cardModel;
        private string _chassisName;

        public string CardId { get => _cardId; set => SetProperty(ref _cardId, value); }
        public string CardName { get => _cardName; set => SetProperty(ref _cardName, value); }
        public string CardModel { get => _cardModel; set => SetProperty(ref _cardModel, value); }
        public string ChassisName { get => _chassisName; set => SetProperty(ref _chassisName, value); }

        /// <summary>板卡类型标识（由派生类实现）</summary>
        public abstract string CardType { get; }
    }

    public class LvdtSimulatorCardConfig : CardConfigDataBase
    {
        public override string CardType => "LvdtSimulator";

        private ObservableCollection<LvdtSimulatorChannelConfig> _channels;

        public ObservableCollection<LvdtSimulatorChannelConfig> Channels
        {
            get => _channels;
            set => SetProperty(ref _channels, value);
        }

        public LvdtSimulatorCardConfig()
        {
            Channels = new ObservableCollection<LvdtSimulatorChannelConfig>();
        }
    }

    public class ResolverSimulatorCardConfig : CardConfigDataBase
    {
        public override string CardType => "ResolverSimulator";

        private ObservableCollection<ResolverSimulatorChannelConfig> _channels;

        public ObservableCollection<ResolverSimulatorChannelConfig> Channels
        {
            get => _channels;
            set => SetProperty(ref _channels, value);
        }

        public ResolverSimulatorCardConfig()
        {
            Channels = new ObservableCollection<ResolverSimulatorChannelConfig>();
        }
    }

    public class LvdtSimulatorChannelConfig
    {
        public ushort ChannelIndex { get; set; }
        public string ChannelName { get; set; }

        public bool IsEnabled { get; set; }
        public string WorkMode { get; set; }
        public string SensorType { get; set; }
        public string OutputMode { get; set; }

        public bool UseInternalExcitation { get; set; }
        public double ExcitationVoltage { get; set; }
        public double ExcitationFrequency { get; set; }
        public double TransmissionRatio { get; set; }
        public double PhaseDelay { get; set; }
        public int AdcRangeIndex { get; set; }

        public double Position { get; set; }
        public double VaVoltage { get; set; }
        public double VbVoltage { get; set; }
        public double Vsum { get; set; }
        public double Vdiff { get; set; }
        public bool SwapVaVb { get; set; }
        public bool VaInverse { get; set; }
        public bool VbInverse { get; set; }

        public bool IsDynamicOutput { get; set; }
        public double DynamicStartPosition { get; set; }
        public double DynamicEndPosition { get; set; }
        public double DynamicPointFreq { get; set; }
        public int DynamicWaveformLength { get; set; }
        public int DynamicOutputCount { get; set; }
        public bool GoBackOutput { get; set; }

        public bool UseResolverAngleOutput { get; set; }
        public double ResolverPhaseDiff { get; set; }
        public double ResolverOutputAngle { get; set; }
        public double ResolverMotorSpeed { get; set; }
        public bool AutoLoadResolverWave { get; set; }
        public int ResolverWaveformLength { get; set; }
        public double ResolverStartAngle { get; set; }
        public double ResolverEndAngle { get; set; }
        public int WaveformOutputCount { get; set; }
    }

    public class ResolverSimulatorChannelConfig
    {
        public ushort ChannelIndex { get; set; }
        public string ChannelName { get; set; }

        public bool IsEnabled { get; set; }
        public string WorkMode { get; set; }
        public string OutputMode { get; set; }

        public bool UseInternalExcitation { get; set; }
        public double ExcitationVoltage { get; set; }
        public double ExcitationFrequency { get; set; }
        public double TransmissionRatio { get; set; }
        public double PhaseDelay { get; set; }
        public int AdcRangeIndex { get; set; }

        public double Position { get; set; }
        public double VaVoltage { get; set; }
        public double VbVoltage { get; set; }
        public double Vsum { get; set; }
        public double Vdiff { get; set; }
        public bool SwapVaVb { get; set; }
        public bool VaInverse { get; set; }
        public bool VbInverse { get; set; }

        public bool IsDynamicOutput { get; set; }
        public double DynamicStartPosition { get; set; }
        public double DynamicEndPosition { get; set; }
        public double DynamicPointFreq { get; set; }
        public int DynamicWaveformLength { get; set; }
        public int DynamicOutputCount { get; set; }
        public bool GoBackOutput { get; set; }

        public bool UseResolverAngleOutput { get; set; }
        public double ResolverPhaseDiff { get; set; }
        public double ResolverOutputAngle { get; set; }
        public double ResolverMotorSpeed { get; set; }
        public bool AutoLoadResolverWave { get; set; }
        public int ResolverWaveformLength { get; set; }
        public double ResolverStartAngle { get; set; }
        public double ResolverEndAngle { get; set; }
        public int WaveformOutputCount { get; set; }
    }

    /// <summary>
    /// 模拟量采集板卡配置
    /// </summary>
    public class AnalogInputCardConfig : CardConfigDataBase
    {
        public override string CardType => "AnalogInput";

        private ObservableCollection<AnalogChannelConfig> _channels;
        private ObservableCollection<BoundSignalData> _boundSignals;
        private ObservableCollection<AnalogInputTestTaskConfig> _testTaskConfigs;
        private string _lastSelectedTestTask;

        public ObservableCollection<AnalogChannelConfig> Channels { get => _channels; set => SetProperty(ref _channels, value); }
        public ObservableCollection<BoundSignalData> BoundSignals { get => _boundSignals; set => SetProperty(ref _boundSignals, value); }
        public ObservableCollection<AnalogInputTestTaskConfig> TestTaskConfigs { get => _testTaskConfigs; set => SetProperty(ref _testTaskConfigs, value); }
        public string LastSelectedTestTask { get => _lastSelectedTestTask; set => SetProperty(ref _lastSelectedTestTask, value); }

        public AnalogInputCardConfig()
        {
            Channels = new ObservableCollection<AnalogChannelConfig>();
            BoundSignals = new ObservableCollection<BoundSignalData>();
            TestTaskConfigs = new ObservableCollection<AnalogInputTestTaskConfig>();
        }
    }

    /// <summary>
    /// 模拟量采集板卡对单个测试任务的配置
    /// </summary>
    public class AnalogInputTestTaskConfig : BindableBase
    {
        private string _testTaskName;
        private double _sampleRate;
        private int _sampleCount;
        private string _acquisitionMode;
        private bool _isRealTimeEnabled;
        private ObservableCollection<AnalogChannelConfig> _channels = new ObservableCollection<AnalogChannelConfig>();

        public string TestTaskName
        {
            get => _testTaskName;
            set => SetProperty(ref _testTaskName, value);
        }

        public double SampleRate
        {
            get => _sampleRate;
            set => SetProperty(ref _sampleRate, value);
        }

        public int SampleCount
        {
            get => _sampleCount;
            set => SetProperty(ref _sampleCount, value);
        }

        public string AcquisitionMode
        {
            get => _acquisitionMode;
            set => SetProperty(ref _acquisitionMode, value);
        }

        public bool IsRealTimeEnabled
        {
            get => _isRealTimeEnabled;
            set => SetProperty(ref _isRealTimeEnabled, value);
        }

        public ObservableCollection<AnalogChannelConfig> Channels
        {
            get => _channels;
            set => SetProperty(ref _channels, value);
        }

        public AnalogInputTestTaskConfig()
        {
            _sampleRate = 10000;
            _sampleCount = 1000;
            _acquisitionMode = "连续";
            _isRealTimeEnabled = true;
        }
    }

    /// <summary>
    /// 模拟量输出板卡配置
    /// </summary>
    public class AnalogOutputCardConfig : CardConfigDataBase
    {
        public override string CardType => "AnalogOutput";

        private ObservableCollection<AnalogChannelConfig> _channels;
        private ObservableCollection<BoundSignalData> _boundSignals;
        private ObservableCollection<AnalogOutputTestTaskConfig> _testTaskConfigs;
        private string _lastSelectedTestTask;

        public ObservableCollection<AnalogChannelConfig> Channels { get => _channels; set => SetProperty(ref _channels, value); }
        public ObservableCollection<BoundSignalData> BoundSignals { get => _boundSignals; set => SetProperty(ref _boundSignals, value); }
        public ObservableCollection<AnalogOutputTestTaskConfig> TestTaskConfigs { get => _testTaskConfigs; set => SetProperty(ref _testTaskConfigs, value); }
        public string LastSelectedTestTask { get => _lastSelectedTestTask; set => SetProperty(ref _lastSelectedTestTask, value); }

        public AnalogOutputCardConfig()
        {
            Channels = new ObservableCollection<AnalogChannelConfig>();
            BoundSignals = new ObservableCollection<BoundSignalData>();
            TestTaskConfigs = new ObservableCollection<AnalogOutputTestTaskConfig>();
        }
    }

    /// <summary>
    /// 模拟量输出波形类型
    /// </summary>
    public enum OutputWaveformType
    {
        Dc,
        Sine,
        Square
    }

    /// <summary>
    /// 扩展的模拟量输出通道配置，存储波形参数
    /// </summary>
    public class AnalogOutputExtendedChannelConfig : AnalogChannelConfig
    {
        public OutputWaveformType WaveformType { get; set; }
        public double Amplitude { get; set; }
        public double Frequency { get; set; }
        public double Offset { get; set; }
        public double DutyCycle { get; set; }
        public new bool IsPreviewEnabled { get; set; }
        public string PreviewColorHex { get; set; }
    }

    /// <summary>
    /// 模拟量输出卡针对单个测试任务的配置
    /// </summary>
    public class AnalogOutputTestTaskConfig : BindableBase
    {
        private string _testTaskName;
        private double _sampleRate;
        private ObservableCollection<AnalogOutputExtendedChannelConfig> _channels = new ObservableCollection<AnalogOutputExtendedChannelConfig>();
        private double _powerVoltage;

        public string TestTaskName
        {
            get => _testTaskName;
            set => SetProperty(ref _testTaskName, value);
        }

        public double SampleRate
        {
            get => _sampleRate;
            set => SetProperty(ref _sampleRate, value);
        }

        /// <summary>
        /// 外部电源电压（V），供模拟量输出面板保存。
        /// </summary>
        public double PowerVoltage
        {
            get => _powerVoltage;
            set => SetProperty(ref _powerVoltage, value);
        }

        public ObservableCollection<AnalogOutputExtendedChannelConfig> Channels
        {
            get => _channels;
            set => SetProperty(ref _channels, value);
        }

        public AnalogOutputTestTaskConfig()
        {
            SampleRate = 1000;
        }
    }

    /// <summary>
    /// LVDS 板卡配置（MT-X970）
    /// </summary>
    public class LvdsCardConfig : CardConfigDataBase
    {
        public override string CardType => "LVDS";

        private ObservableCollection<Mtx970LvdsTestTaskConfig> _testTaskConfigs;
        private string _lastSelectedTestTask;

        public ObservableCollection<Mtx970LvdsTestTaskConfig> TestTaskConfigs
        {
            get => _testTaskConfigs;
            set => SetProperty(ref _testTaskConfigs, value);
        }

        public string LastSelectedTestTask
        {
            get => _lastSelectedTestTask;
            set => SetProperty(ref _lastSelectedTestTask, value);
        }

        public LvdsCardConfig()
        {
            TestTaskConfigs = new ObservableCollection<Mtx970LvdsTestTaskConfig>();
        }
    }

    /// <summary>
    /// LVDS 对单个测试任务的配置（MT-X970）
    /// </summary>
    public class Mtx970LvdsTestTaskConfig : BindableBase
    {
        private string _testTaskName;
        private bool _configOsc;
        private bool _staticCount;
        private string _clockFrequencyText;
        private string _lvdsDataSampleWr;
        private string _patternMatch;
        private string _numSamples;

        public string TestTaskName
        {
            get => _testTaskName;
            set => SetProperty(ref _testTaskName, value);
        }

        public bool ConfigOsc
        {
            get => _configOsc;
            set => SetProperty(ref _configOsc, value);
        }

        public bool StaticCount
        {
            get => _staticCount;
            set => SetProperty(ref _staticCount, value);
        }

        public string ClockFrequencyText
        {
            get => _clockFrequencyText;
            set => SetProperty(ref _clockFrequencyText, value);
        }

        public string LvdsDataSampleWr
        {
            get => _lvdsDataSampleWr;
            set => SetProperty(ref _lvdsDataSampleWr, value);
        }

        public string PatternMatch
        {
            get => _patternMatch;
            set => SetProperty(ref _patternMatch, value);
        }

        public string NumSamples
        {
            get => _numSamples;
            set => SetProperty(ref _numSamples, value);
        }
    }

    /// <summary>
    /// 离散量IO板卡配置
    /// </summary>
    public class DigitalIOCardConfig : CardConfigDataBase
    {
        public override string CardType => "DigitalIO";

        private ObservableCollection<DiscreteChannelConfig> _inputChannels;
        private ObservableCollection<DiscreteChannelConfig> _outputChannels;
        private string _outputMode;
        private ObservableCollection<DigitalIOTestTaskConfig> _testTaskConfigs;
        private string _lastSelectedTestTask;
        private double _powerVoltage;
        private double _powerVoltageGroup2;
        private double _powerVoltageGroup3;
        private double _powerVoltageGroup4;

        /// <summary>输入通道配置列表</summary>
        public ObservableCollection<DiscreteChannelConfig> InputChannels
        {
            get => _inputChannels;
            set => SetProperty(ref _inputChannels, value);
        }

        /// <summary>输出通道配置列表</summary>
        public ObservableCollection<DiscreteChannelConfig> OutputChannels
        {
            get => _outputChannels;
            set => SetProperty(ref _outputChannels, value);
        }

        /// <summary>
        /// 全局输出模式（例如: "Sourcing" / "Sinking" / "Push_Pull"）
        /// 以字符串形式存储，便于 JSON 序列化和兼容旧版本
        /// </summary>
        public string OutputMode
        {
            get => _outputMode;
            set => SetProperty(ref _outputMode, value);
        }

        /// <summary>不同测试任务的独立配置</summary>
        public ObservableCollection<DigitalIOTestTaskConfig> TestTaskConfigs
        {
            get => _testTaskConfigs;
            set => SetProperty(ref _testTaskConfigs, value);
        }

        /// <summary>上次选中的测试任务</summary>
        public string LastSelectedTestTask
        {
            get => _lastSelectedTestTask;
            set => SetProperty(ref _lastSelectedTestTask, value);
        }

        /// <summary>外部电源电压（V），作为默认值。</summary>
        public double PowerVoltage
        {
            get => _powerVoltage;
            set => SetProperty(ref _powerVoltage, value);
        }

        public double PowerVoltageGroup2
        {
            get => _powerVoltageGroup2;
            set => SetProperty(ref _powerVoltageGroup2, value);
        }

        public double PowerVoltageGroup3
        {
            get => _powerVoltageGroup3;
            set => SetProperty(ref _powerVoltageGroup3, value);
        }

        public double PowerVoltageGroup4
        {
            get => _powerVoltageGroup4;
            set => SetProperty(ref _powerVoltageGroup4, value);
        }

        public DigitalIOCardConfig()
        {
            InputChannels = new ObservableCollection<DiscreteChannelConfig>();
            OutputChannels = new ObservableCollection<DiscreteChannelConfig>();
            OutputMode = "Push_Pull";
            TestTaskConfigs = new ObservableCollection<DigitalIOTestTaskConfig>();
            PowerVoltage = 0;
            PowerVoltageGroup2 = 0;
            PowerVoltageGroup3 = 0;
            PowerVoltageGroup4 = 0;
        }
    }

    /// <summary>
    /// 开关矩阵板卡配置
    /// </summary>
    public class SwitchMatrixCardConfig : CardConfigDataBase
    {
        #region Private Fields

        private string _topology;
        private int _inputCount;
        private int _outputCount;
        private bool _isInputExclusive;
        private bool _isOutputExclusive;
        private Dictionary<string, MatrixConnection> _connectionMap;
        private int _activeRelayCount;
        private int _errorConnectionCount;
        private ObservableCollection<BoundSignalData> _boundSignals;
        private ObservableCollection<MatrixConnection> _activeConnections;

        #endregion

        #region Properties

        /// <summary>
        /// 板卡类型标识
        /// </summary>
        public override string CardType => "矩阵开关";

        /// <summary>
        /// 拓扑结构名称
        /// </summary>
        public string Topology
        {
            get => _topology;
            set
            {
                if (SetProperty(ref _topology, value))
                {
                    // 拓扑改变时可能需要重新初始化矩阵
                    InitializeFromTopology(value);
                }
            }
        }

        /// <summary>
        /// 输入通道数量
        /// </summary>
        public int InputCount
        {
            get => _inputCount;
            private set => SetProperty(ref _inputCount, value);
        }

        /// <summary>
        /// 输出通道数量
        /// </summary>
        public int OutputCount
        {
            get => _outputCount;
            private set => SetProperty(ref _outputCount, value);
        }

        /// <summary>
        /// 输入独占模式（一个输入只能连接一个输出）
        /// </summary>
        public bool IsInputExclusive
        {
            get => _isInputExclusive;
            set => SetProperty(ref _isInputExclusive, value);
        }

        /// <summary>
        /// 输出独占模式（一个输出只能连接一个输入）
        /// </summary>
        public bool IsOutputExclusive
        {
            get => _isOutputExclusive;
            set => SetProperty(ref _isOutputExclusive, value);
        }

        /// <summary>
        /// 连接映射表
        /// </summary>
        public Dictionary<string, MatrixConnection> ConnectionMap
        {
            get => _connectionMap ??= new Dictionary<string, MatrixConnection>();
            private set => SetProperty(ref _connectionMap, value);
        }

        /// <summary>
        /// 活动继电器数量
        /// </summary>
        public int ActiveRelayCount
        {
            get => _activeRelayCount;
            private set => SetProperty(ref _activeRelayCount, value);
        }

        /// <summary>
        /// 错误连接数量
        /// </summary>
        public int ErrorConnectionCount
        {
            get => _errorConnectionCount;
            private set => SetProperty(ref _errorConnectionCount, value);
        }

        /// <summary>
        /// 绑定信号数据（来自变量表）- 与其他板卡保持一致
        /// </summary>
        public ObservableCollection<BoundSignalData> BoundSignals
        {
            get => _boundSignals ??= new ObservableCollection<BoundSignalData>();
            set => SetProperty(ref _boundSignals, value);
        }

        /// <summary>
        /// 活动连接列表（用于显示）
        /// </summary>
        public ObservableCollection<MatrixConnection> ActiveConnections
        {
            get => _activeConnections ??= new ObservableCollection<MatrixConnection>();
            private set => SetProperty(ref _activeConnections, value);
        }


        public bool IsInputConnected(string input)
        {
            return GetConnectedOutput(input) != null;
        }

        public bool IsOutputConnected(string output)
        {
            return GetConnectedInput(output) != null;
        }

        #endregion

        #region Constructor

        /// <summary>
        /// 默认构造函数
        /// </summary>
        public SwitchMatrixCardConfig()
        {
            // 继承自 CardConfigDataBase 的属性
            CardId = Guid.NewGuid().ToString();
            CardName = "PXI-2601";
            CardModel = "矩阵开关";

            // SwitchMatrix 特定属性
            _topology = "4x32 Matrix";
            _isInputExclusive = true;
            _isOutputExclusive = false;
            _connectionMap = new Dictionary<string, MatrixConnection>();
            _boundSignals = new ObservableCollection<BoundSignalData>();
            _activeConnections = new ObservableCollection<MatrixConnection>();
        }

        /// <summary>
        /// 使用指定参数初始化
        /// </summary>
        public SwitchMatrixCardConfig(string cardName, string topology, int inputCount, int outputCount)
            : this()
        {
            CardName = cardName;
            Topology = topology;
            InitializeMatrix(inputCount, outputCount);
        }

        #endregion

        #region Public Methods


        /// <summary>
        /// 创建新的连接（如果不存在则创建）
        /// </summary>
        public MatrixConnection CreateConnection(string input, string output)
        {
            if (string.IsNullOrEmpty(input) || string.IsNullOrEmpty(output))
                return null;

            string key = GetConnectionKey(input, output);

            // 如果连接已存在，直接返回
            if (ConnectionMap.TryGetValue(key, out var existingConnection))
                return existingConnection;

            // 创建新的连接
            int inputIndex = GetChannelIndex(input);
            int outputIndex = GetChannelIndex(output);

            var connection = new MatrixConnection
            {
                ConnectionId = Guid.NewGuid().ToString(),
                InputChannel = input,
                OutputChannel = output,
                RelayName = GenerateRelayName(inputIndex, outputIndex),
                State = SwitchConnectionState.Disconnected,
                ConnectionCount = 0,
                CreatedTime = DateTime.Now,
                IsEnabled = true,
                Remarks = $"创建于 {DateTime.Now:yyyy-MM-dd HH:mm:ss}"
            };

            ConnectionMap[key] = connection;

            // 如果矩阵已初始化，更新计数
            if (InputCount > 0 && OutputCount > 0)
            {
                UpdateCounts();
                UpdateActiveConnectionsList();
            }

            return connection;
        }
        

            /// <summary>
            /// 标记所有连接为错误状态（最简单的实现）
            /// </summary>
            public void MarkAllConnectionsAsError()
            {
                if (ConnectionMap == null) return;

                foreach (var connection in ConnectionMap.Values)
                {
                    connection.State = SwitchConnectionState.Error;  // 直接设置状态
                    connection.ConnectionColor = "#F44336";  // 红色
                }
            }
        
        /// <summary>
        /// 获取通道索引
        /// </summary>
        private int GetChannelIndex(string channelName)
        {
            if (string.IsNullOrEmpty(channelName))
                return -1;

            try
            {
                // 支持格式: "IN0", "OUT5", "CH1", 等等
                if (channelName.StartsWith("IN"))
                    return int.Parse(channelName.Substring(2));
                else if (channelName.StartsWith("OUT"))
                    return int.Parse(channelName.Substring(3));
                else if (channelName.StartsWith("CH"))
                    return int.Parse(channelName.Substring(2));
                else
                    return int.Parse(new string(channelName.Where(char.IsDigit).ToArray()));
            }
            catch
            {
                return -1;
            }
        }

        // 在 SwitchMatrixCardConfig.cs 中添加
        public List<MatrixConnection> GetAllActiveConnections()
        {
            return ConnectionMap.Values
                .Where(conn => conn.State == SwitchConnectionState.Connected)
                .ToList();
        }

        /// <summary>
        /// 初始化矩阵
        /// </summary>
        public void InitializeMatrix(int inputCount, int outputCount)
        {
            InputCount = inputCount;
            OutputCount = outputCount;
            ConnectionMap.Clear();
            UpdateActiveConnectionsList();

            // 创建所有可能的连接
            for (int i = 0; i < inputCount; i++)
            {
                string input = $"IN{i}";
                for (int j = 0; j < outputCount; j++)
                {
                    string output = $"OUT{j}";
                    string key = GetConnectionKey(input, output);

                    var connection = new MatrixConnection
                    {
                        InputChannel = input,
                        OutputChannel = output,
                        RelayName = GenerateRelayName(i, j),
                        State = SwitchConnectionState.Disconnected
                    };

                    ConnectionMap[key] = connection;
                }
            }

            UpdateCounts();
        }

        /// <summary>
        /// 根据拓扑名称初始化
        /// </summary>
        private void InitializeFromTopology(string topology)
        {
            // 这里可以根据拓扑名称解析输入输出数量
            // 例如: "4x32 Matrix" -> InputCount=4, OutputCount=32
            if (topology.Contains("x"))
            {
                try
                {
                    var parts = topology.Split('x');
                    if (parts.Length >= 2)
                    {
                        string inputStr = new string(parts[0].Where(char.IsDigit).ToArray());
                        string outputStr = new string(parts[1].Where(char.IsDigit).ToArray());

                        if (int.TryParse(inputStr, out int inputCount) &&
                            int.TryParse(outputStr, out int outputCount))
                        {
                            InitializeMatrix(inputCount, outputCount);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[SwitchMatrixCardConfig] 解析拓扑失败: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 获取连接状态
        /// </summary>
        public SwitchConnectionState GetConnectionState(string input, string output)
        {
            string key = GetConnectionKey(input, output);
            if (ConnectionMap.TryGetValue(key, out var connection))
            {
                return connection.State;
            }
            return SwitchConnectionState.Disconnected;
        }

        /// <summary>
        /// 获取连接对象
        /// </summary>
        public MatrixConnection GetConnection(string input, string output)
        {
            string key = GetConnectionKey(input, output);
            ConnectionMap.TryGetValue(key, out var connection);
            return connection;
        }

        /// <summary>
        /// 设置连接状态
        /// </summary>
        public void SetConnection(string input, string output, SwitchConnectionState state, string errorMessage = null)
        {
            string key = GetConnectionKey(input, output);
            if (ConnectionMap.TryGetValue(key, out var connection))
            {
                // 更新前状态
                var oldState = connection.State;

                // 更新连接状态
                connection.SetConnectionState(state, errorMessage);

                // 如果状态改变，更新计数和活动连接列表
                if (oldState != state)
                {
                    UpdateCounts();
                    UpdateActiveConnectionsList();
                }
            }
        }

        /// <summary>
        /// 检查独占规则
        /// </summary>
        public bool CheckExclusiveRules(string input, string output, out string errorMessage)
        {
            errorMessage = string.Empty;

            // 检查输入独占
            if (IsInputExclusive)
            {
                var existingConnection = GetConnectedOutput(input);
                if (!string.IsNullOrEmpty(existingConnection) && existingConnection != output)
                {
                    errorMessage = $"输入 {input} 已经连接到 {existingConnection}";
                    return false;
                }
            }

            // 检查输出独占
            if (IsOutputExclusive)
            {
                var existingConnection = GetConnectedInput(output);
                if (!string.IsNullOrEmpty(existingConnection) && existingConnection != input)
                {
                    errorMessage = $"输出 {output} 已经连接到 {existingConnection}";
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 获取输入连接的输出
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
            return string.Empty;
        }

        /// <summary>
        /// 获取输出连接的输入
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
            return string.Empty;
        }

        /// <summary>
        /// 获取所有活动连接
        /// </summary>
        public List<MatrixConnection> GetActiveConnections()
        {
            return ConnectionMap.Values
                .Where(c => c.State == SwitchConnectionState.Connected)
                .ToList();
        }

        /// <summary>
        /// 断开所有连接
        /// </summary>
        public void DisconnectAll()
        {
            foreach (var connection in ConnectionMap.Values)
            {
                if (connection.State == SwitchConnectionState.Connected)
                {
                    connection.SetConnectionState(SwitchConnectionState.Disconnected);
                }
            }
            UpdateCounts();
            UpdateActiveConnectionsList();
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
            UpdateActiveConnectionsList();
        }

        /// <summary>
        /// 重置连接计数器
        /// </summary>
        public void ResetConnectionCounts()
        {
            foreach (var connection in ConnectionMap.Values)
            {
                connection.ResetStatistics();
            }
        }

        /// <summary>
        /// 获取统计信息
        /// </summary>
        public string GetStatistics()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"开关矩阵配置信息:");
            sb.AppendLine($"名称: {CardName}");
            sb.AppendLine($"型号: {CardModel}");
            sb.AppendLine($"拓扑: {Topology}");
            sb.AppendLine($"输入通道: {InputCount}");
            sb.AppendLine($"输出通道: {OutputCount}");
            sb.AppendLine($"活动连接: {ActiveRelayCount}");
            sb.AppendLine($"错误连接: {ErrorConnectionCount}");
            sb.AppendLine($"总连接数: {ConnectionMap.Count}");
            sb.AppendLine($"输入独占: {(IsInputExclusive ? "是" : "否")}");
            sb.AppendLine($"输出独占: {(IsOutputExclusive ? "是" : "否")}");
            sb.AppendLine($"绑定信号数: {BoundSignals.Count}");

            return sb.ToString();
        }

        /// <summary>
        /// 添加绑定信号
        /// </summary>
        public void AddBoundSignal(string channelName, string signalName, string unit = "")
        {
            var boundSignal = new BoundSignalData
            {
                ChannelName = channelName,
                SignalName = signalName,
                Unit = unit,
                Status = "正常"
            };

            BoundSignals.Add(boundSignal);
        }

        /// <summary>
        /// 移除绑定信号
        /// </summary>
        public bool RemoveBoundSignal(string channelName)
        {
            var signal = BoundSignals.FirstOrDefault(s => s.ChannelName == channelName);
            if (signal != null)
            {
                BoundSignals.Remove(signal);
                return true;
            }
            return false;
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// 获取连接键值
        /// </summary>
        private string GetConnectionKey(string input, string output)
        {
            // 不依赖顺序，按字母顺序排序以确保一致性
            if (string.Compare(input, output) <= 0)
            {
                return $"{input}|{output}";
            }
            else
            {
                return $"{output}|{input}";
            }
        }

        /// <summary>
        /// 生成继电器名称
        /// </summary>
        private string GenerateRelayName(int inputIndex, int outputIndex)
        {
            // 生成更清晰的继电器名称格式
            // 例如: IN0->OUT5 = "R0_5", IN1->OUT3 = "R1_3"
            if (inputIndex >= 0 && outputIndex >= 0)
            {
                var topology = Topology ?? string.Empty;

                if (topology.IndexOf("8x16", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    topology.IndexOf("8*16", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    // 8x16：按行分板卡
                    // r0-r3 -> boardIndex=0, r4-r7 -> boardIndex=1
                    if (inputIndex > 7 || outputIndex > 15)
                        return string.Empty;

                    int boardIndex = inputIndex / 4;
                    int localRowIndex = inputIndex % 4;
                    return $"b{boardIndex}r{localRowIndex}c{outputIndex}";
                }

                if (topology.IndexOf("4x32", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    topology.IndexOf("4*32", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    // 4x32：按列分板卡
                    // c0-c15 -> boardIndex=0, c16-c31 -> boardIndex=1
                    if (inputIndex > 3 || outputIndex > 31)
                        return string.Empty;

                    int boardIndex = outputIndex / 16;
                    int localColIndex = outputIndex % 16;
                    return $"b{boardIndex}r{inputIndex}c{localColIndex}";
                }

                return $"R{inputIndex}_{outputIndex}";
            }
            else
            {
                return "";// 去除负号
            }
        }

        /// <summary>
        /// 更新计数
        /// </summary>
        public void UpdateCounts()
        {
            ActiveRelayCount = ConnectionMap.Values.Count(c => c.State == SwitchConnectionState.Connected);
            ErrorConnectionCount = ConnectionMap.Values.Count(c => c.State == SwitchConnectionState.Error);
        }

        /// <summary>
        /// 更新活动连接列表
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
        /// 记录连接次数
        /// </summary>
        /// <summary>
        /// 记录连接次数
        /// </summary>
        public void RecordConnectionCount(string input, string output)
        {
            // 获取连接
            var connection = GetConnection(input, output);

            if (connection != null)
            {
                // 连接存在，增加计数
                connection.IncrementConnectionCount();
                Debug.WriteLine($"[计数] 增加连接计数: {input} -> {output}，当前计数: {connection.ConnectionCount}");
            }
            else
            {
                // 连接不存在，创建新连接
                // 修正：MatrixConnection 使用默认构造函数，不是4个参数的构造函数
                connection = new MatrixConnection
                {
                    ConnectionId = $"{input}_{output}",
                    InputChannel = input,
                    OutputChannel = output,
                    RelayName = GenerateRelayName(GetInputIndex(input), GetOutputIndex(output)),
                    State = SwitchConnectionState.Disconnected,
                    ConnectionCount = 1,  // 首次连接，计数为1
                    CreatedTime = DateTime.Now,
                    IsEnabled = true,
                    Remarks = $"手动连接于 {DateTime.Now:yyyy-MM-dd HH:mm:ss}"
                };

                // 使用现有的 SetConnection 方法来添加连接
                string key = GetConnectionKey(input, output);
                ConnectionMap[key] = connection;  // 直接添加到字典

                Debug.WriteLine($"[计数] 创建新连接: {input} -> {output}，初始计数为1");
            }
        }

        private int GetInputIndex(string input)
        {
            return int.Parse(input.Replace("IN", ""));
        }

        private int GetOutputIndex(string output)
        {
            return int.Parse(output.Replace("OUT", ""));
        }

        #endregion

        #region 克隆方法（如果需要）

        /// <summary>
        /// 克隆配置（如果需要深度克隆）
        /// </summary>
        public SwitchMatrixCardConfig Clone()
        {
            var clone = new SwitchMatrixCardConfig
            {
                CardId = this.CardId,
                CardName = this.CardName,
                CardModel = this.CardModel,
                ChassisName = this.ChassisName,
                Topology = this.Topology,
                InputCount = this.InputCount,
                OutputCount = this.OutputCount,
                IsInputExclusive = this.IsInputExclusive,
                IsOutputExclusive = this.IsOutputExclusive
            };

            // 深度克隆连接映射
            foreach (var kvp in this.ConnectionMap)
            {
                clone.ConnectionMap[kvp.Key] = kvp.Value.Clone();
            }

            // 克隆绑定信号
            foreach (var signal in this.BoundSignals)
            {
                clone.BoundSignals.Add(new BoundSignalData
                {
                    ChannelName = signal.ChannelName,
                    SignalName = signal.SignalName,
                    CurrentValue = signal.CurrentValue,
                    Unit = signal.Unit,
                    Status = signal.Status
                });
            }

            // 更新计数
            clone.UpdateCounts();
            clone.UpdateActiveConnectionsList();

            return clone;
        }

        #endregion
    }

    /// <summary>
    /// 1394B板卡配置
    /// </summary>
    public class Mil1394BCardConfig : CardConfigDataBase
    {
        public override string CardType => "Mil1394B";

        private ObservableCollection<Mil1394BNodeConfig> _nodeConfigs;
        private ObservableCollection<Mil1394BTestTaskConfig> _testTaskConfigs;
        private string _lastSelectedTestTask;

        /// <summary>节点配置列表（每个节点一个配置，兼容旧版本）</summary>
        public ObservableCollection<Mil1394BNodeConfig> NodeConfigs
        {
            get => _nodeConfigs;
            set => SetProperty(ref _nodeConfigs, value);
        }

        /// <summary>不同测试任务的独立配置</summary>
        public ObservableCollection<Mil1394BTestTaskConfig> TestTaskConfigs
        {
            get => _testTaskConfigs;
            set => SetProperty(ref _testTaskConfigs, value);
        }

        /// <summary>上次选中的测试任务</summary>
        public string LastSelectedTestTask
        {
            get => _lastSelectedTestTask;
            set => SetProperty(ref _lastSelectedTestTask, value);
        }

        public Mil1394BCardConfig()
        {
            NodeConfigs = new ObservableCollection<Mil1394BNodeConfig>();
            TestTaskConfigs = new ObservableCollection<Mil1394BTestTaskConfig>();
        }
    }

    /// <summary>
    /// 1394B板卡针对某个测试任务的配置
    /// </summary>
    public class Mil1394BTestTaskConfig : BindableBase
    {
        private string _testTaskName;
        private ObservableCollection<Mil1394BNodeConfig> _nodeConfigs;

        public string TestTaskName
        {
            get => _testTaskName;
            set => SetProperty(ref _testTaskName, value);
        }

        /// <summary>节点配置列表（每个节点一个配置）</summary>
        public ObservableCollection<Mil1394BNodeConfig> NodeConfigs
        {
            get => _nodeConfigs;
            set => SetProperty(ref _nodeConfigs, value);
        }

        public Mil1394BTestTaskConfig()
        {
            NodeConfigs = new ObservableCollection<Mil1394BNodeConfig>();
        }
    }

    /// <summary>
    /// 1394B节点配置
    /// </summary>
    public class Mil1394BNodeConfig : BindableBase
    {
        private uint _nodeNumber;
        private string _nodeType;
        private string _nodeRate;
        private bool _bmEnabled;
        private int _stofSendStyleIndex;
        private string _stofPeriod;
        private string _stofSendTimes;
        private string _stofVpc;
        private uint[] _stofPayload;
        private string _recvAsyncChannel;
        private ObservableCollection<Mil1394BAsyncReceiveConfigItem> _asyncReceiveConfig;
        private ObservableCollection<Mil1394BAsyncSendConfigItem> _asyncSendConfig;

        /// <summary>节点号</summary>
        public uint NodeNumber
        {
            get => _nodeNumber;
            set => SetProperty(ref _nodeNumber, value);
        }

        /// <summary>节点类型（CC/RN/BM）</summary>
        public string NodeType
        {
            get => _nodeType;
            set => SetProperty(ref _nodeType, value);
        }

        /// <summary>节点速率（100M/200M/400M）</summary>
        public string NodeRate
        {
            get => _nodeRate;
            set => SetProperty(ref _nodeRate, value);
        }

        /// <summary>BM使能</summary>
        public bool BmEnabled
        {
            get => _bmEnabled;
            set => SetProperty(ref _bmEnabled, value);
        }

        /// <summary>STOF发送方式索引（0=按周期，1=按次数）</summary>
        public int StofSendStyleIndex
        {
            get => _stofSendStyleIndex;
            set => SetProperty(ref _stofSendStyleIndex, value);
        }

        /// <summary>STOF周期间隔（ms）</summary>
        public string StofPeriod
        {
            get => _stofPeriod;
            set => SetProperty(ref _stofPeriod, value);
        }

        /// <summary>STOF发送次数</summary>
        public string StofSendTimes
        {
            get => _stofSendTimes;
            set => SetProperty(ref _stofSendTimes, value);
        }

        /// <summary>STOF VPC</summary>
        public string StofVpc
        {
            get => _stofVpc;
            set => SetProperty(ref _stofVpc, value);
        }

        /// <summary>STOF Payload（9个值）</summary>
        public uint[] StofPayload
        {
            get => _stofPayload;
            set => SetProperty(ref _stofPayload, value);
        }

        /// <summary>接收通道</summary>
        public string RecvAsyncChannel
        {
            get => _recvAsyncChannel;
            set => SetProperty(ref _recvAsyncChannel, value);
        }

        /// <summary>异步流包接收配置</summary>
        public ObservableCollection<Mil1394BAsyncReceiveConfigItem> AsyncReceiveConfig
        {
            get => _asyncReceiveConfig;
            set => SetProperty(ref _asyncReceiveConfig, value);
        }

        /// <summary>异步流包发送配置</summary>
        public ObservableCollection<Mil1394BAsyncSendConfigItem> AsyncSendConfig
        {
            get => _asyncSendConfig;
            set => SetProperty(ref _asyncSendConfig, value);
        }

        public Mil1394BNodeConfig()
        {
            NodeType = "BM";
            NodeRate = "400M";
            BmEnabled = true;
            StofSendStyleIndex = 1;
            StofPeriod = "15";
            StofSendTimes = "100";
            StofVpc = "0";
            StofPayload = new uint[9];
            RecvAsyncChannel = "0";
            AsyncReceiveConfig = new ObservableCollection<Mil1394BAsyncReceiveConfigItem>();
            AsyncSendConfig = new ObservableCollection<Mil1394BAsyncSendConfigItem>();
        }
    }

    /// <summary>
    /// 1394B异步流包接收配置项
    /// </summary>
    public class Mil1394BAsyncReceiveConfigItem : BindableBase
    {
        private bool _isSelected;
        private string _msgID;
        private int _dataLength;

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        public string MsgID
        {
            get => _msgID;
            set => SetProperty(ref _msgID, value);
        }

        public int DataLength
        {
            get => _dataLength;
            set => SetProperty(ref _dataLength, value);
        }
    }

    /// <summary>
    /// 1394B异步流包发送配置项
    /// </summary>
    public class Mil1394BAsyncSendConfigItem : BindableBase
    {
        private int _messageID;
        private int _channel;
        private int _heartbeat;
        private int _health;
        private int _heartbeatStep;
        private int _payloadLength;
        private int _sendOffset;
        private bool _vpc;
        private int _vpcAsync;
        private uint _security;
        private uint _priority;
        private uint[] _payloadData;
        private uint _transmitOffset;
        private uint _receiveOffset;
        private uint _phmOffset;

        public int MessageID { get => _messageID; set => SetProperty(ref _messageID, value); }
        public int Channel { get => _channel; set => SetProperty(ref _channel, value); }
        public int Heartbeat { get => _heartbeat; set => SetProperty(ref _heartbeat, value); }
        public int Health { get => _health; set => SetProperty(ref _health, value); }
        public int HeartbeatStep { get => _heartbeatStep; set => SetProperty(ref _heartbeatStep, value); }
        public int PayloadLength { get => _payloadLength; set => SetProperty(ref _payloadLength, value); }
        public int SendOffset { get => _sendOffset; set => SetProperty(ref _sendOffset, value); }
        public bool VPC { get => _vpc; set => SetProperty(ref _vpc, value); }
        public int VPCAsync { get => _vpcAsync; set => SetProperty(ref _vpcAsync, value); }
        public uint Security { get => _security; set => SetProperty(ref _security, value); }
        public uint Priority { get => _priority; set => SetProperty(ref _priority, value); }
        public uint[] PayloadData { get => _payloadData; set => SetProperty(ref _payloadData, value); }
        public uint TransmitOffset { get => _transmitOffset; set => SetProperty(ref _transmitOffset, value); }
        public uint ReceiveOffset { get => _receiveOffset; set => SetProperty(ref _receiveOffset, value); }
        public uint PHMOffset { get => _phmOffset; set => SetProperty(ref _phmOffset, value); }

        public Mil1394BAsyncSendConfigItem()
        {
            PayloadData = new uint[500];
        }
    }

    /// <summary>
    /// 程控电阻板卡配置
    /// </summary>
    public class ResistanceOutputCardConfig : CardConfigDataBase
    {
        public override string CardType => "ResistanceOutput";

        private ObservableCollection<ResistanceChannelConfigData> _channels;
        private ObservableCollection<ResistanceOutputTestTaskConfig> _testTaskConfigs;
        private string _lastSelectedTestTask;

        /// <summary>板卡通道的基础配置（用于初始化任务配置）</summary>
        public ObservableCollection<ResistanceChannelConfigData> Channels
        {
            get => _channels;
            set => SetProperty(ref _channels, value);
        }

        /// <summary>不同测试任务下的电阻通道配置</summary>
        public ObservableCollection<ResistanceOutputTestTaskConfig> TestTaskConfigs
        {
            get => _testTaskConfigs;
            set => SetProperty(ref _testTaskConfigs, value);
        }

        /// <summary>上一次选中的测试任务</summary>
        public string LastSelectedTestTask
        {
            get => _lastSelectedTestTask;
            set => SetProperty(ref _lastSelectedTestTask, value);
        }

        public ResistanceOutputCardConfig()
        {
            Channels = new ObservableCollection<ResistanceChannelConfigData>();
            TestTaskConfigs = new ObservableCollection<ResistanceOutputTestTaskConfig>();
        }
    }

    /// <summary>
    /// 程控电阻单通道配置
    /// </summary>
    public class ResistanceChannelConfigData : BindableBase
    {
        private string _channelName;
        private bool _isEnabled;
        private bool _isPreviewEnabled;
        private double _offset;
        private double _targetResistance;

        public string ChannelName
        {
            get => _channelName;
            set => SetProperty(ref _channelName, value);
        }

        public bool IsEnabled
        {
            get => _isEnabled;
            set => SetProperty(ref _isEnabled, value);
        }

        public bool IsPreviewEnabled
        {
            get => _isPreviewEnabled;
            set => SetProperty(ref _isPreviewEnabled, value);
        }

        public double Offset
        {
            get => _offset;
            set => SetProperty(ref _offset, value);
        }

        public double TargetResistance
        {
            get => _targetResistance;
            set => SetProperty(ref _targetResistance, value);
        }
    }

    /// <summary>
    /// 程控电阻测试任务配置
    /// </summary>
    public class ResistanceOutputTestTaskConfig : BindableBase
    {
        private string _testTaskName;
        private string _outputMode;
        private ObservableCollection<ResistanceChannelConfigData> _channels = new ObservableCollection<ResistanceChannelConfigData>();

        public string TestTaskName
        {
            get => _testTaskName;
            set => SetProperty(ref _testTaskName, value);
        }

        public string OutputMode
        {
            get => _outputMode;
            set => SetProperty(ref _outputMode, value);
        }

        public ObservableCollection<ResistanceChannelConfigData> Channels
        {
            get => _channels;
            set => SetProperty(ref _channels, value);
        }
    }

    /// <summary>
    /// CAN板卡配置
    /// </summary>
    public class CanCardConfig : CardConfigDataBase
    {
        public override string CardType => "CAN";

        private ObservableCollection<CanChannelConfig> _channels;
        private ObservableCollection<CanTestTaskConfig> _testTaskConfigs;
        private string _lastSelectedTestTask;

        public ObservableCollection<CanChannelConfig> Channels
        {
            get => _channels;
            set => SetProperty(ref _channels, value);
        }

        public ObservableCollection<CanTestTaskConfig> TestTaskConfigs
        {
            get => _testTaskConfigs;
            set => SetProperty(ref _testTaskConfigs, value);
        }

        public string LastSelectedTestTask
        {
            get => _lastSelectedTestTask;
            set => SetProperty(ref _lastSelectedTestTask, value);
        }

        public CanCardConfig()
        {
            Channels = new ObservableCollection<CanChannelConfig>();
            TestTaskConfigs = new ObservableCollection<CanTestTaskConfig>();
        }
    }

    /// <summary>
    /// CAN通道配置
    /// </summary>
    public class CanChannelConfig : BindableBase
    {
        private string _channelName;
        private bool _isEnabled;
        private bool _isPreviewEnabled;
        private int _baudRate;
        private string _remarks;

        public string ChannelName
        {
            get => _channelName;
            set => SetProperty(ref _channelName, value);
        }

        public bool IsEnabled
        {
            get => _isEnabled;
            set => SetProperty(ref _isEnabled, value);
        }

        public bool IsPreviewEnabled
        {
            get => _isPreviewEnabled;
            set => SetProperty(ref _isPreviewEnabled, value);
        }

        /// <summary>波特率(bps)</summary>
        public int BaudRate
        {
            get => _baudRate;
            set => SetProperty(ref _baudRate, value);
        }

        public string Remarks
        {
            get => _remarks;
            set => SetProperty(ref _remarks, value);
        }

        public CanChannelConfig()
        {
            _isEnabled = true;
            _baudRate = 500000;
        }
    }

    /// <summary>
    /// CAN测试任务配置
    /// </summary>
    public class CanTestTaskConfig : BindableBase
    {
        private string _testTaskName;
        private ObservableCollection<CanChannelConfig> _channels = new ObservableCollection<CanChannelConfig>();
        private string _mode;

        public string TestTaskName
        {
            get => _testTaskName;
            set => SetProperty(ref _testTaskName, value);
        }

        public ObservableCollection<CanChannelConfig> Channels
        {
            get => _channels;
            set => SetProperty(ref _channels, value);
        }

        /// <summary>CAN模式</summary>
        public string Mode
        {
            get => _mode;
            set => SetProperty(ref _mode, value);
        }
    }

    // ===================== 以下为将来扩展的板卡配置类（暂时注释） =====================
    //
    // /// <summary>ARINC429板卡配置</summary>
    // public class Arinc429CardConfig : CardConfigDataBase { ... }
    //
    // /// <summary>MIL-1553B板卡配置</summary>
    // public class Mil1553BCardConfig : CardConfigDataBase { ... }
    // ==================================================================================

    /// <summary>
    /// 模拟量通道配置
    /// </summary>
    public class AnalogChannelConfig : BindableBase
    {
        private string _channelName;
        private bool _isEnabled;
        private bool _isPreviewEnabled;
        private string _range;
        private List<string> _availableRanges;
        private double _currentValue;
        private string _unit;
        private string _status;

        public string ChannelName { get => _channelName; set => SetProperty(ref _channelName, value); }
        public bool IsEnabled { get => _isEnabled; set => SetProperty(ref _isEnabled, value); }
        public bool IsPreviewEnabled { get => _isPreviewEnabled; set => SetProperty(ref _isPreviewEnabled, value); }
        public string Range { get => _range; set => SetProperty(ref _range, value); }
        public List<string> AvailableRanges { get => _availableRanges; set => SetProperty(ref _availableRanges, value); }
        public double CurrentValue { get => _currentValue; set => SetProperty(ref _currentValue, value); }
        public string Unit { get => _unit; set => SetProperty(ref _unit, value); }
        public string Status { get => _status; set => SetProperty(ref _status, value); }

        public AnalogChannelConfig()
        {
            AvailableRanges = new List<string>();
            IsEnabled = true;
            Status = "正常";
        }
    }

    /// <summary>
    /// 离散量通道配置
    /// </summary>
    public class DiscreteChannelConfig : BindableBase
    {
        private string _channelName;
        private bool _isEnabled;
        private bool _isPreviewEnabled;
        private bool _isOutput;
        private bool _currentValue;

        /// <summary>通道名称</summary>
        public string ChannelName { get => _channelName; set => SetProperty(ref _channelName, value); }

        /// <summary>是否使能</summary>
        public bool IsEnabled { get => _isEnabled; set => SetProperty(ref _isEnabled, value); }

        /// <summary>是否启用实时预览</summary>
        public bool IsPreviewEnabled { get => _isPreviewEnabled; set => SetProperty(ref _isPreviewEnabled, value); }

        /// <summary>是否为输出通道</summary>
        public bool IsOutput { get => _isOutput; set => SetProperty(ref _isOutput, value); }

        /// <summary>当前值（0/1）</summary>
        public bool CurrentValue { get => _currentValue; set => SetProperty(ref _currentValue, value); }

        public DiscreteChannelConfig()
        {
            IsEnabled = true;
            CurrentValue = false;
        }
    }

    /// <summary>
    /// 离散量板卡针对某个测试任务的配置
    /// </summary>
    public class DigitalIOTestTaskConfig : BindableBase
    {
        private string _testTaskName;
        private ObservableCollection<DiscreteChannelConfig> _inputChannels;
        private ObservableCollection<DiscreteChannelConfig> _outputChannels;
        private string _outputMode;
        private double _powerVoltage;
        private double _powerVoltageGroup2;
        private double _powerVoltageGroup3;
        private double _powerVoltageGroup4;
        private bool _thresholdSyncEnabled;
        private bool _voltageSyncEnabled;

        public string TestTaskName
        {
            get => _testTaskName;
            set => SetProperty(ref _testTaskName, value);
        }

        public ObservableCollection<DiscreteChannelConfig> InputChannels
        {
            get => _inputChannels;
            set => SetProperty(ref _inputChannels, value);
        }

        public ObservableCollection<DiscreteChannelConfig> OutputChannels
        {
            get => _outputChannels;
            set => SetProperty(ref _outputChannels, value);
        }

        public string OutputMode
        {
            get => _outputMode;
            set => SetProperty(ref _outputMode, value);
        }

        /// <summary>外部电源电压（V），随测试任务保存。</summary>
        public double PowerVoltage
        {
            get => _powerVoltage;
            set => SetProperty(ref _powerVoltage, value);
        }

        public double PowerVoltageGroup2
        {
            get => _powerVoltageGroup2;
            set => SetProperty(ref _powerVoltageGroup2, value);
        }

        public double PowerVoltageGroup3
        {
            get => _powerVoltageGroup3;
            set => SetProperty(ref _powerVoltageGroup3, value);
        }

        public double PowerVoltageGroup4
        {
            get => _powerVoltageGroup4;
            set => SetProperty(ref _powerVoltageGroup4, value);
        }

        public bool ThresholdSyncEnabled
        {
            get => _thresholdSyncEnabled;
            set => SetProperty(ref _thresholdSyncEnabled, value);
        }

        public bool VoltageSyncEnabled
        {
            get => _voltageSyncEnabled;
            set => SetProperty(ref _voltageSyncEnabled, value);
        }

        /// <summary>
        /// DI0-3 高电平阈值 (V)
        /// </summary>
        public double DIport1 { get; set; } = 0.00;

        /// <summary>
        /// DI4-7 高电平阈值 (V)
        /// </summary>
        public double DIport2 { get; set; } = 0.00;

        /// <summary>
        /// DI8-11 高电平阈值 (V)
        /// </summary>
        public double DIport3 { get; set; } = 0.00;

        /// <summary>
        /// DI12-15 高电平阈值 (V)
        /// </summary>
        public double DIport4 { get; set; } = 0.00;

        /// <summary>
        /// DI16-19 高电平阈值 (V)
        /// </summary>
        public double DIport5 { get; set; } = 0.00;

        /// <summary>
        /// DI20-23 高电平阈值 (V)
        /// </summary>
        public double DIport6 { get; set; } = 0.00;

        /// <summary>
        /// DI24-27 高电平阈值 (V)
        /// </summary>
        public double DIport7 { get; set; } = 0.00;

        /// <summary>
        /// DI28-31 高电平阈值 (V)
        /// </summary>
        public double DIport8 { get; set; } = 0.00;

        public DigitalIOTestTaskConfig()
        {
            InputChannels = new ObservableCollection<DiscreteChannelConfig>();
            OutputChannels = new ObservableCollection<DiscreteChannelConfig>();
            OutputMode = "Push_Pull";
            PowerVoltage = 0;
            PowerVoltageGroup2 = 0;
            PowerVoltageGroup3 = 0;
            PowerVoltageGroup4 = 0;
            ThresholdSyncEnabled = false;
            VoltageSyncEnabled = false;
        }
    }

    /// <summary>
    /// 绑定信号数据（来自变量表）- 所有板卡通用
    /// </summary>
    public class BoundSignalData : BindableBase
    {
        private string _channelName;
        private string _signalName;
        private double _currentValue;
        private string _unit;
        private string _status;

        public string ChannelName { get => _channelName; set => SetProperty(ref _channelName, value); }
        public string SignalName { get => _signalName; set => SetProperty(ref _signalName, value); }
        public double CurrentValue { get => _currentValue; set => SetProperty(ref _currentValue, value); }
        public string Unit { get => _unit; set => SetProperty(ref _unit, value); }
        public string Status { get => _status; set => SetProperty(ref _status, value); }

        public BoundSignalData() { Status = "正常"; }
    }
}
