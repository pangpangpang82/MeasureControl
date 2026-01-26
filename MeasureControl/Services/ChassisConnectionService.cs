using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using MeasureControl.Models;
using MeasureControl.Services;

namespace MeasureControl.Services
{
    /// <summary>
    /// 机箱连接服务实现
    /// </summary>
    public class ChassisConnectionService : IChassisConnectionService
    {
        private readonly ObservableCollection<ChassisConnection> _connections;
        private readonly List<ConnectionLine> _connectionLines;
        private readonly IPxiChassisService _pxiChassisService;

        public ChassisConnectionService(IPxiChassisService pxiChassisService)
        {
            _connections = new ObservableCollection<ChassisConnection>();
            _connectionLines = new List<ConnectionLine>();
            _pxiChassisService = pxiChassisService;
        }

        /// <summary>
        /// 获取所有连接
        /// </summary>
        /// <returns>连接列表</returns>
        public ObservableCollection<ChassisConnection> GetAllConnections()
        {
            return _connections;
        }

        /// <summary>
        /// 添加连接
        /// </summary>
        /// <param name="connection">连接</param>
        public bool AddConnection(ChassisConnection connection)
        {
            if (connection != null && !_connections.Any(c => c.Id == connection.Id))
            {
                if (string.IsNullOrWhiteSpace(connection.ConnectionName))
                {
                    connection.ConnectionName = ChassisConnection.GetConnectionTypeDisplayName(connection.ConnectionType);
                }

                _connections.Add(connection);
                
                // 同步添加连接线
                var connectionLine = ConvertToConnectionLine(connection);
                if (connectionLine != null)
                {
                    _connectionLines.Add(connectionLine);
                }
                
                return true;
            }
            return false;
        }

        /// <summary>
        /// 移除连接
        /// </summary>
        /// <param name="connectionId">连接ID</param>
        public bool RemoveConnection(string connectionId)
        {
            var connection = _connections.FirstOrDefault(c => c.Id == connectionId);
            if (connection != null)
            {
                _connections.Remove(connection);
                
                // 同步移除连接线
                var connectionLineToRemove = _connectionLines.FirstOrDefault(cl => 
                    cl.ConnectionId == connection.Id);
                
                if (connectionLineToRemove != null)
                {
                    _connectionLines.Remove(connectionLineToRemove);
                }
                
                return true;
            }
            return false;
        }

        /// <summary>
        /// 清除所有连接
        /// </summary>
        public void ClearConnections()
        {
            _connections.Clear();
            _connectionLines.Clear();
        }

        /// <summary>
        /// 清除所有连接（别名方法）
        /// </summary>
        public void ClearAllConnections()
        {
            ClearConnections();
        }

        /// <summary>
        /// 获取连接线
        /// </summary>
        /// <returns>连接线列表</returns>
        public List<ConnectionLine> GetConnectionLines()
        {
            // 如果连接线列表为空，尝试从现有连接重新生成
            if (_connectionLines.Count == 0 && _connections.Count > 0)
            {
                foreach (var connection in _connections)
                {
                    var connectionLine = ConvertToConnectionLine(connection);
                    if (connectionLine != null)
                    {
                        _connectionLines.Add(connectionLine);
                    }
                }
            }
            
            return _connectionLines.ToList();
        }

        /// <summary>
        /// 检查两个机箱是否已连接
        /// </summary>
        /// <param name="chassis1">机箱1</param>
        /// <param name="chassis2">机箱2</param>
        /// <returns>是否已连接</returns>
        public bool AreChassisConnected(string chassis1, string chassis2)
        {
            return _connections.Any(c => 
                (c.SourceChassisId == chassis1 && c.TargetChassisId == chassis2) ||
                (c.SourceChassisId == chassis2 && c.TargetChassisId == chassis1));
        }

        /// <summary>
        /// 根据机箱获取连接
        /// </summary>
        /// <param name="chassisName">机箱名称</param>
        /// <returns>连接列表</returns>
        public List<ChassisConnection> GetConnectionsByChassis(string chassisName)
        {
            return _connections.Where(c => c.SourceChassisId == chassisName || c.TargetChassisId == chassisName).ToList();
        }

        /// <summary>
        /// 检查机箱是否有连接
        /// </summary>
        /// <param name="chassisId">机箱ID</param>
        /// <returns>是否有连接</returns>
        public bool HasChassisConnections(string chassisId)
        {
            return _connections.Any(c => c.SourceChassisId == chassisId || c.TargetChassisId == chassisId);
        }

        /// <summary>
        /// 将ChassisConnection转换为ConnectionLine
        /// </summary>
        /// <param name="connection">机箱连接</param>
        /// <returns>连接线</returns>
        private ConnectionLine ConvertToConnectionLine(ChassisConnection connection)
        {
            if (connection == null) return null;
            
            // 通过机箱ID获取机箱信息
            var allChassis = _pxiChassisService?.GetAllChassis();
            var sourceChassis = allChassis?.FirstOrDefault(c => c.Id == connection.SourceChassisId);
            var targetChassis = allChassis?.FirstOrDefault(c => c.Id == connection.TargetChassisId);
            
            if (sourceChassis == null || targetChassis == null) 
            {
                // 如果找不到机箱信息，创建一个基本的连接线，使用默认位置
                var connectionDetails = new ConnectionDetails
                {
                    ConnectionName = GetConnectionTypeDisplayName(connection.ConnectionType.ToString()),
                    SourceObject = connection.SourceChassisId,
                    TargetObject = connection.TargetChassisId,
                    InterfaceType = connection.ConnectionType.ToString(),
                    // prefer actual measured speed; if not present use "--"
                    Speed = !string.IsNullOrWhiteSpace(connection.ActualLinkSpeed) ? connection.ActualLinkSpeed : "--",
                    BusProtocol = GetDefaultProtocol(connection.ConnectionType)
                };
                
                var fallbackLine = ConnectionLine.FromConnectionDetails(
                    connectionDetails,
                    0, 0, // 默认位置
                    1, 1, // 默认位置
                    connection.ConnectionType.ToString(),
                    connection.SourceChassisId,
                    connection.TargetChassisId,
                    connection.SourceChassisId,
                    connection.TargetChassisId
                );
                fallbackLine.ConnectionId = connection.Id;
                fallbackLine.ConnectionName = string.IsNullOrWhiteSpace(connection.ConnectionName)
                    ? fallbackLine.ConnectionName
                    : connection.ConnectionName;
                // 保留选中的通讯变量表信息
                fallbackLine.SourceCommunicatingTabel = connection.SourceCommunicatingTabel;
                fallbackLine.TargetCommunicatingTabel = connection.TargetCommunicatingTabel;
                return fallbackLine;
            }
            
            // 创建默认的连接详细信息
            var connectionDetails2 = new ConnectionDetails
            {
                ConnectionName = GetConnectionTypeDisplayName(connection.ConnectionType.ToString()),
                SourceObject = sourceChassis.Name,
                TargetObject = targetChassis.Name,
                InterfaceType = connection.ConnectionType.ToString(),
                // prefer actual measured speed; if not present use "--"
                Speed = !string.IsNullOrWhiteSpace(connection.ActualLinkSpeed) ? connection.ActualLinkSpeed : "--",
                BusProtocol = GetDefaultProtocol(connection.ConnectionType)
            };
            
            var line = ConnectionLine.FromConnectionDetails(
                connectionDetails2,
                sourceChassis.GridRow,
                sourceChassis.GridColumn,
                targetChassis.GridRow,
                targetChassis.GridColumn,
                connection.ConnectionType.ToString(),
                connection.SourceChassisId,
                connection.TargetChassisId,
                sourceChassis.Name,
                targetChassis.Name
            );
            line.ConnectionId = connection.Id;
            line.ConnectionName = string.IsNullOrWhiteSpace(connection.ConnectionName)
                ? line.ConnectionName
                : connection.ConnectionName;
            // 保留选中的通讯变量表信息
            line.SourceCommunicatingTabel = connection.SourceCommunicatingTabel;
            line.TargetCommunicatingTabel = connection.TargetCommunicatingTabel;
            return line;
        }

        /// <summary>
        /// 获取连接类型显示名称
        /// </summary>
        private string GetConnectionTypeDisplayName(string connectionType)
        {
            return connectionType switch
            {
                "Ethernet" => "以太网连接",
                "USB" => "USB连接",
                "Serial" => "串口连接",
                _ => "未知连接"
            };
        }

        /// <summary>
        /// 获取默认传输速率
        /// </summary>
        private string GetDefaultSpeed(ConnectionType connectionType)
        {
            return connectionType switch
            {
                ConnectionType.Ethernet => "1000 Mbps",
                ConnectionType.USB => "480 Mbps",
                ConnectionType.Serial => "115200 bps",
                _ => "未知"
            };
        }

        /// <summary>
        /// 获取默认总线协议
        /// </summary>
        private string GetDefaultProtocol(ConnectionType connectionType)
        {
            return connectionType switch
            {
                ConnectionType.Ethernet => "TCP/IP",
                ConnectionType.USB => "USB 2.0",
                ConnectionType.Serial => "RS-232",
                _ => "未知"
            };
        }

        public bool RenameConnection(string connectionId, string newName)
        {
            if (string.IsNullOrWhiteSpace(connectionId) || string.IsNullOrWhiteSpace(newName))
            {
                return false;
            }

            if (IsConnectionNameInUse(newName, connectionId))
            {
                return false;
            }

            var connection = _connections.FirstOrDefault(c => c.Id == connectionId);
            if (connection == null)
            {
                return false;
            }

            connection.ConnectionName = newName;
            var connectionLine = _connectionLines.FirstOrDefault(cl => cl.ConnectionId == connectionId);
            if (connectionLine != null)
            {
                connectionLine.ConnectionName = newName;
            }

            return true;
        }

        public bool IsConnectionNameInUse(string name, string excludeConnectionId = null)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            return _connections.Any(c =>
                !string.Equals(c.Id, excludeConnectionId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(c.ConnectionName, name, StringComparison.OrdinalIgnoreCase));
        }
    }
}
