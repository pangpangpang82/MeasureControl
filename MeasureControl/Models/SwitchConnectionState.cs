using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MeasureControl.Models
{
    /// <summary>
    /// 连接状态
    /// </summary>
    public enum SwitchConnectionState
    {
        /// <summary>
        /// 通路 - 信号可通过
        /// </summary>
        Connected = 1,

        /// <summary>
        /// 断路 - 信号阻断
        /// </summary>
        Disconnected = 0,

        /// <summary>
        /// 错误状态
        /// </summary>
        Error = -1
    }
}
