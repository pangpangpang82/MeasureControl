using System;
using System.Text;
using Prism.Mvvm;

namespace MeasureControl.Models
{
    /// <summary>
    /// 矩阵连接信息
    /// </summary>
    [Serializable]
    public class MatrixConnection : BindableBase
    {
        /// <summary>
        /// 连接ID
        /// </summary>
        public string ConnectionId { get; set; }

        /// <summary>
        /// 输入通道名称
        /// </summary>
        public string InputChannel { get; set; }

        /// <summary>
        /// 输出通道名称
        /// </summary>
        public string OutputChannel { get; set; }

        /// <summary>
        /// 对应的继电器名称
        /// </summary>
        public string RelayName { get; set; }

        private SwitchConnectionState _state;
        /// <summary>
        /// 连接状态
        /// </summary>
        public SwitchConnectionState State
        {
            get => _state;
            set => SetProperty(ref _state, value);
        }

        private int _connectionCount;
        /// <summary>
        /// 连接次数
        /// </summary>
        public int ConnectionCount
        {
            get => _connectionCount;
            set => SetProperty(ref _connectionCount, value);
        }

        /// <summary>
        /// 最后连接时间
        /// </summary>
        public DateTime? LastConnectedTime { get; set; }

        /// <summary>
        /// 最后断开时间
        /// </summary>
        public DateTime? LastDisconnectedTime { get; set; }

        /// <summary>
        /// 连接持续时间（秒）
        /// </summary>
        public double ConnectionDuration { get; set; }

        /// <summary>
        /// 是否启用
        /// </summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreatedTime { get; set; }

        /// <summary>
        /// 备注信息
        /// </summary>
        public string Remarks { get; set; }

        /// <summary>
        /// 错误信息
        /// </summary>
        public string ErrorMessage { get; set; }

        /// <summary>
        /// 连接质量评分（0-100）
        /// </summary>
        public int QualityScore { get; set; }

        private string _connectionColor;
        public string ConnectionColor
        {
            get => _connectionColor;
            set => SetProperty(ref _connectionColor, value);
        }

        /// <summary>
        /// 获取连接状态文本
        /// </summary>
        public string StateText
        {
            get
            {
                switch (State)
                {
                    case SwitchConnectionState.Connected:
                        return "通路";
                    case SwitchConnectionState.Disconnected:
                        return "断路";
                    case SwitchConnectionState.Error:
                        return "错误";
                    default:
                        return "未知";
                }
            }
        }

        /// <summary>
        /// 获取状态颜色代码
        /// </summary>
        public string StateColor
        {
            get
            {
                switch (State)
                {
                    case SwitchConnectionState.Connected:
                        return "#4CAF50"; // Green
                    case SwitchConnectionState.Disconnected:
                        return "#9E9E9E"; // Gray
                    case SwitchConnectionState.Error:
                        return "#F44336"; // Red
                    default:
                        return "#607D8B"; // Dark Gray
                }
            }
        }

        /// <summary>
        /// 获取连接摘要
        /// </summary>
        public string GetConnectionSummary()
        {
            return $"{InputChannel} → {OutputChannel} [{StateText}]";
        }

        /// <summary>
        /// 获取详细统计信息
        /// </summary>
        public string GetStatistics()
        {
            var stats = new StringBuilder();
            stats.AppendLine($"连接ID: {ConnectionId}");
            stats.AppendLine($"状态: {StateText}");
            stats.AppendLine($"连接次数: {ConnectionCount}");
            stats.AppendLine($"继电器: {RelayName}");

            if (LastConnectedTime.HasValue)
                stats.AppendLine($"最后连接: {LastConnectedTime:yyyy-MM-dd HH:mm:ss}");

            if (LastDisconnectedTime.HasValue)
                stats.AppendLine($"最后断开: {LastDisconnectedTime:yyyy-MM-dd HH:mm:ss}");

            if (ConnectionDuration > 0)
                stats.AppendLine($"累计连接时间: {ConnectionDuration:F2}秒");

            if (!string.IsNullOrEmpty(ErrorMessage))
                stats.AppendLine($"错误: {ErrorMessage}");

            stats.AppendLine($"质量评分: {QualityScore}/100");

            return stats.ToString();
        }

        /// <summary>
        /// 获取工具提示文本
        /// </summary>
        public string GetToolTipText()
        {
            var tooltip = new StringBuilder();
            tooltip.AppendLine($"输入: {InputChannel}");
            tooltip.AppendLine($"输出: {OutputChannel}");
            tooltip.AppendLine($"状态: {StateText}");
            tooltip.AppendLine($"继电器: {RelayName}");
            tooltip.AppendLine($"连接次数: {ConnectionCount}");

            if (LastConnectedTime.HasValue)
                tooltip.AppendLine($"最后连接: {LastConnectedTime:yyyy-MM-dd HH:mm:ss}");

            if (!string.IsNullOrEmpty(Remarks))
                tooltip.AppendLine($"备注: {Remarks}");

            if (State == SwitchConnectionState.Error && !string.IsNullOrEmpty(ErrorMessage))
                tooltip.AppendLine($"错误: {ErrorMessage}");

            return tooltip.ToString();
        }

        /// <summary>
        /// 设置连接状态
        /// </summary>
        public void SetConnectionState(SwitchConnectionState newState, string errorMessage = null)
        {
            var oldState = State;
            State = newState;
            
            // 手动触发StateColor属性的PropertyChanged事件，因为它依赖于State属性
            RaisePropertyChanged(nameof(StateColor));
            RaisePropertyChanged(nameof(StateText));

            if (newState == SwitchConnectionState.Connected)
            {
                ConnectionCount++;
                LastConnectedTime = DateTime.Now;

                // 如果之前是断开状态，计算持续时间
                if (oldState == SwitchConnectionState.Disconnected && LastDisconnectedTime.HasValue)
                {
                    ConnectionDuration += (DateTime.Now - LastDisconnectedTime.Value).TotalSeconds;
                }
            }
            else if (newState == SwitchConnectionState.Disconnected)
            {
                LastDisconnectedTime = DateTime.Now;
            }
            else if (newState == SwitchConnectionState.Error)
            {
                ErrorMessage = errorMessage;
            }
        }

        /// <summary>
        /// 更新连接持续时间
        /// </summary>
        public void UpdateConnectionDuration()
        {
            if (State == SwitchConnectionState.Connected && LastConnectedTime.HasValue)
            {
                ConnectionDuration += (DateTime.Now - LastConnectedTime.Value).TotalSeconds;
                LastConnectedTime = DateTime.Now;
            }
        }

        /// <summary>
        /// 验证连接配置
        /// </summary>
        public bool ValidateConnection()
        {
            if (string.IsNullOrEmpty(InputChannel) || string.IsNullOrEmpty(OutputChannel))
                return false;

            if (string.IsNullOrEmpty(RelayName))
                return false;

            if (InputChannel == OutputChannel)
            {
                ErrorMessage = "输入和输出不能相同";
                return false;
            }

            return true;
        }

        /// <summary>
        /// 重置连接统计
        /// </summary>
        public void ResetStatistics()
        {
            ConnectionCount = 0;
            ConnectionDuration = 0;
            LastConnectedTime = null;
            LastDisconnectedTime = null;
            ErrorMessage = null;
            QualityScore = 0;
        }

        public void IncrementConnectionCount()
        {
            ConnectionCount++;
        }

        /// <summary>
        /// 克隆连接
        /// </summary>
        public MatrixConnection Clone()
        {
            return new MatrixConnection
            {
                ConnectionId = this.ConnectionId,
                InputChannel = this.InputChannel,
                OutputChannel = this.OutputChannel,
                RelayName = this.RelayName,
                State = this.State,
                ConnectionCount = this.ConnectionCount,
                LastConnectedTime = this.LastConnectedTime,
                LastDisconnectedTime = this.LastDisconnectedTime,
                ConnectionDuration = this.ConnectionDuration,
                IsEnabled = this.IsEnabled,
                CreatedTime = this.CreatedTime,
                Remarks = this.Remarks,
                ErrorMessage = this.ErrorMessage,
                QualityScore = this.QualityScore,
                ConnectionColor = this.ConnectionColor
            };
        }

        /// <summary>
        /// 复制到目标连接
        /// </summary>
        public void CopyTo(MatrixConnection target)
        {
            if (target == null) return;

            target.ConnectionId = this.ConnectionId;
            target.InputChannel = this.InputChannel;
            target.OutputChannel = this.OutputChannel;
            target.RelayName = this.RelayName;
            target.State = this.State;
            target.ConnectionCount = this.ConnectionCount;
            target.LastConnectedTime = this.LastConnectedTime;
            target.LastDisconnectedTime = this.LastDisconnectedTime;
            target.ConnectionDuration = this.ConnectionDuration;
            target.IsEnabled = this.IsEnabled;
            target.CreatedTime = this.CreatedTime;
            target.Remarks = this.Remarks;
            target.ErrorMessage = this.ErrorMessage;
            target.QualityScore = this.QualityScore;
            target.ConnectionColor = this.ConnectionColor;
        }

        /// <summary>
        /// 转换为字符串
        /// </summary>
        public override string ToString()
        {
            return GetConnectionSummary();
        }
    }
}