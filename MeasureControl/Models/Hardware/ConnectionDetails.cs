using Prism.Mvvm;

namespace MeasureControl.Models
{
    /// <summary>
    /// 连接线详细信息模型
    /// </summary>
    public class ConnectionDetails : BindableBase
    {
        private string _connectionName;
        private string _sourceObject;
        private string _targetObject;
        private string _interfaceType;
        private string _speed;
        private string _busProtocol;

        /// <summary>
        /// 链接名称
        /// </summary>
        public string ConnectionName
        {
            get => _connectionName;
            set => SetProperty(ref _connectionName, value);
        }

        /// <summary>
        /// 连接对象1
        /// </summary>
        public string SourceObject
        {
            get => _sourceObject;
            set => SetProperty(ref _sourceObject, value);
        }

        /// <summary>
        /// 链接对象2
        /// </summary>
        public string TargetObject
        {
            get => _targetObject;
            set => SetProperty(ref _targetObject, value);
        }

        /// <summary>
        /// 接口类型
        /// </summary>
        public string InterfaceType
        {
            get => _interfaceType;
            set => SetProperty(ref _interfaceType, value);
        }

        /// <summary>
        /// 速率
        /// </summary>
        public string Speed
        {
            get => _speed;
            set => SetProperty(ref _speed, value);
        }

        /// <summary>
        /// 总线协议
        /// </summary>
        public string BusProtocol
        {
            get => _busProtocol;
            set => SetProperty(ref _busProtocol, value);
        }

        public ConnectionDetails()
        {
            ConnectionName = "";
            SourceObject = "";
            TargetObject = "";
            InterfaceType = "";
            Speed = "";
            BusProtocol = "";
        }

        public ConnectionDetails(string connectionName, string sourceObject, string targetObject, 
            string interfaceType, string speed, string busProtocol)
        {
            ConnectionName = connectionName;
            SourceObject = sourceObject;
            TargetObject = targetObject;
            InterfaceType = interfaceType;
            Speed = speed;
            BusProtocol = busProtocol;
        }
    }
}
