using System;
using Prism.Mvvm;

namespace MeasureControl.Models
{
    /// <summary>
    /// 机箱连接类型枚举
    /// </summary>
    public enum ConnectionType
    {
        Ethernet,   // 以太网连接
        USB,       // USB连接
        Serial     // 串口连接
    }

    /// <summary>
    /// 机箱连接模型
    /// </summary>
    public class ChassisConnection : BindableBase
    {
        private string _sourceChassisId;
        private string _targetChassisId;
        private ConnectionType _connectionType;
        private string _connectionName;
        private string _actualLinkSpeed;
        private string _sourcePort;
        private string _targetPort;
        private string _sourceCommunicatingTabel;
        private string _targetCommunicatingTabel;
        private string _associatedNonCommunicatingTabel;

        /// <summary>
        /// 源机箱ID
        /// </summary>
        public string SourceChassisId
        {
            get => _sourceChassisId;
            set => SetProperty(ref _sourceChassisId, value);
        }

        /// <summary>
        /// 目标机箱ID
        /// </summary>
        public string TargetChassisId
        {
            get => _targetChassisId;
            set => SetProperty(ref _targetChassisId, value);
        }

        /// <summary>
        /// 连接类型
        /// </summary>
        public ConnectionType ConnectionType
        {
            get => _connectionType;
            set => SetProperty(ref _connectionType, value);
        }

        /// <summary>
        /// 连接名称（用于显示）
        /// </summary>
        public string ConnectionName
        {
            get => _connectionName;
            set => SetProperty(ref _connectionName, value);
        }

        /// <summary>
        /// 实际链路速率
        /// </summary>
        public string ActualLinkSpeed
        {
            get => _actualLinkSpeed;
            set => SetProperty(ref _actualLinkSpeed, value);
        }

        /// <summary>
        /// 源端口（用于人工记录）
        /// </summary>
        public string SourcePort
        {
            get => _sourcePort;
            set => SetProperty(ref _sourcePort, value);
        }

        /// <summary>
        /// 目标端口（用于人工记录）
        /// </summary>
        public string TargetPort
        {
            get => _targetPort;
            set => SetProperty(ref _targetPort, value);
        }

        /// <summary>
        /// 源端选择的通讯变量表（格式：测试任务/表名）
        /// </summary>
        public string SourceCommunicatingTabel
        {
            get => _sourceCommunicatingTabel;
            set => SetProperty(ref _sourceCommunicatingTabel, value);
        }

        /// <summary>
        /// 目标端选择的通讯变量表（格式：测试任务/表名）
        /// </summary>
        public string TargetCommunicatingTabel
        {
            get => _targetCommunicatingTabel;
            set => SetProperty(ref _targetCommunicatingTabel, value);
        }

        /// <summary>
        /// 分享的非通讯变量表（格式：测任务/表名）
        /// </summary>
        public string AssociatedNonCommunicatingTabel
        {
            get => _associatedNonCommunicatingTabel;
            set => SetProperty(ref _associatedNonCommunicatingTabel, value);
        }

        /// <summary>
        /// 连接ID
        /// </summary>
        public string Id { get; set; }

        public ChassisConnection()
        {
            Id = Guid.NewGuid().ToString();
        }

        public ChassisConnection(string sourceChassisId, string targetChassisId, ConnectionType connectionType)
            : this()
        {
            SourceChassisId = sourceChassisId;
            TargetChassisId = targetChassisId;
            ConnectionType = connectionType;
            ConnectionName = GetConnectionTypeDisplayName(connectionType);
        }

        /// <summary>
        /// 获取连接类型的显示名称
        /// </summary>
        public static string GetConnectionTypeDisplayName(ConnectionType connectionType)
        {
            return connectionType switch
            {
                ConnectionType.Ethernet => "以太网连接",
                ConnectionType.USB => "USB连接",
                ConnectionType.Serial => "串口连接",
                _ => "未知连接"
            };
        }

        /// <summary>
        /// 检查是否包含指定的机箱ID
        /// </summary>
        public bool ContainsChassis(string chassisId)
        {
            return SourceChassisId == chassisId || TargetChassisId == chassisId;
        }

        /// <summary>
        /// 获取连接的另一个机箱ID
        /// </summary>
        public string GetOtherChassisId(string chassisId)
        {
            if (SourceChassisId == chassisId)
                return TargetChassisId;
            if (TargetChassisId == chassisId)
                return SourceChassisId;
            return null;
        }
    }
}
