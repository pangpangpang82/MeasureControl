using Prism.Mvvm;
using System;

namespace MeasureControl.Models
{
    /// <summary>
    /// 连接线模型，用于绘制机箱之间的连接
    /// </summary>
    public class ConnectionLine : BindableBase
    {
        private int _sourceRow;
        private int _sourceColumn;
        private int _targetRow;
        private int _targetColumn;
        private string _connectionType;
        private string _connectionId;
        private string _sourceChassisId;
        private string _targetChassisId;
        private string _sourceChassisName;
        private string _targetChassisName;
        private string _connectionName;
        private string _sourceObject;
        private string _targetObject;
        private string _interfaceType;
        private string _speed;
        private string _busProtocol;
        private string _sourceCommunicatingTabel;
        private string _targetCommunicatingTabel;
        private string _id;

        public string Id
        {
            get => _id;
            set => SetProperty(ref _id, value);
        }

        public int SourceRow
        {
            get => _sourceRow;
            set => SetProperty(ref _sourceRow, value);
        }

        public int SourceColumn
        {
            get => _sourceColumn;
            set => SetProperty(ref _sourceColumn, value);
        }

        public int TargetRow
        {
            get => _targetRow;
            set => SetProperty(ref _targetRow, value);
        }

        public int TargetColumn
        {
            get => _targetColumn;
            set => SetProperty(ref _targetColumn, value);
        }

        public string ConnectionType
        {
            get => _connectionType;
            set => SetProperty(ref _connectionType, value);
        }

        public string ConnectionId
        {
            get => _connectionId;
            set => SetProperty(ref _connectionId, value);
        }

        public string SourceChassisId
        {
            get => _sourceChassisId;
            set => SetProperty(ref _sourceChassisId, value);
        }

        public string TargetChassisId
        {
            get => _targetChassisId;
            set => SetProperty(ref _targetChassisId, value);
        }

        public string SourceChassisName
        {
            get => _sourceChassisName;
            set => SetProperty(ref _sourceChassisName, value);
        }

        public string TargetChassisName
        {
            get => _targetChassisName;
            set => SetProperty(ref _targetChassisName, value);
        }

        public string ConnectionName
        {
            get => _connectionName;
            set => SetProperty(ref _connectionName, value);
        }

        public string SourceObject
        {
            get => _sourceObject;
            set => SetProperty(ref _sourceObject, value);
        }

        public string TargetObject
        {
            get => _targetObject;
            set => SetProperty(ref _targetObject, value);
        }

        public string InterfaceType
        {
            get => _interfaceType;
            set => SetProperty(ref _interfaceType, value);
        }

        public string Speed
        {
            get => _speed;
            set => SetProperty(ref _speed, value);
        }

        public string BusProtocol
        {
            get => _busProtocol;
            set => SetProperty(ref _busProtocol, value);
        }

        public string SourceCommunicatingTabel
        {
            get => _sourceCommunicatingTabel;
            set => SetProperty(ref _sourceCommunicatingTabel, value);
        }

        public string TargetCommunicatingTabel
        {
            get => _targetCommunicatingTabel;
            set => SetProperty(ref _targetCommunicatingTabel, value);
        }

        public ConnectionLine()
        {
            Id = Guid.NewGuid().ToString();
            ConnectionName = "";
            SourceObject = "";
            TargetObject = "";
            InterfaceType = "";
            Speed = "";
            BusProtocol = "";
        }
        
        public ConnectionLine(int sourceRow, int sourceColumn, int targetRow, int targetColumn, string connectionType) : this()
        {
            SourceRow = sourceRow;
            SourceColumn = sourceColumn;
            TargetRow = targetRow;
            TargetColumn = targetColumn;
            ConnectionType = connectionType;
        }

        /// <summary>
        /// 从ConnectionDetails创建ConnectionLine
        /// </summary>
        public static ConnectionLine FromConnectionDetails(ConnectionDetails details, int sourceRow, int sourceColumn, int targetRow, int targetColumn, string connectionType, string sourceChassisId, string targetChassisId, string sourceChassisName, string targetChassisName)
        {
            var connectionLine = new ConnectionLine(sourceRow, sourceColumn, targetRow, targetColumn, connectionType)
            {
                SourceChassisId = sourceChassisId,
                TargetChassisId = targetChassisId,
                SourceChassisName = sourceChassisName,
                TargetChassisName = targetChassisName,
                ConnectionName = details.ConnectionName,
                SourceObject = details.SourceObject,
                TargetObject = details.TargetObject,
                InterfaceType = details.InterfaceType,
                Speed = details.Speed,
                BusProtocol = details.BusProtocol
            };
            return connectionLine;
        }

        /// <summary>
        /// 转换为ConnectionDetails
        /// </summary>
        public ConnectionDetails ToConnectionDetails()
        {
            return new ConnectionDetails(ConnectionName, SourceObject, TargetObject, InterfaceType, Speed, BusProtocol);
        }
    }
}
