using System;
using System.Threading;
using System.Threading.Tasks;

namespace MeasureControl.Simulations.FuelController
{
    /// <summary>
    /// 离散量采集功能测试仿真类
    /// 模拟HI-8435PQTF离散量采集芯片的SPI通信和DO输出控制
    /// </summary>
    public sealed class DiscreteInputSimulation : IDisposable
    {
        #region 常量定义

        /// <summary>
        /// 矩阵开关IP地址
        /// </summary>
        private const string MatrixIp = "192.168.1.3";

        /// <summary>
        /// 矩阵开关槽位号（离散量采集测试）
        /// </summary>
        private const int MatrixSlot = 8;

        /// <summary>
        /// DO通道数量（DO1-DO14）
        /// </summary>
        public const int DoChannelCount = 14;

        /// <summary>
        /// Bank0通道数量
        /// </summary>
        public const int Bank0ChannelCount = 7;

        /// <summary>
        /// Bank1通道数量
        /// </summary>
        public const int Bank1ChannelCount = 7;

        /// <summary>
        /// 总采集通道数量
        /// </summary>
        public const int TotalChannelCount = Bank0ChannelCount + Bank1ChannelCount;

        #endregion

        #region 字段

        private bool _disposed;
        private bool _isPowerOn;
        private bool _isMatrixConnected;
        private readonly Random _random = new Random();

        // DO通道状态（true=接地, false=开路）
        private readonly bool[] _doChannelStates = new bool[DoChannelCount];

        // 采集结果（bank0[0:6] + bank1[0:6]）
        private readonly int[] _acquisitionResults = new int[TotalChannelCount];

        #endregion

        #region SPI信号定义

        /// <summary>
        /// SPI通信接口针脚定义
        /// </summary>
        public static class SpiPins
        {
            public const string DSI_CSn = "CRM_PIN14";      // 片选，低有效
            public const string DSI_CLK = "CRM_PIN4";       // 时钟
            public const string DSI_MISO = "CRM_PIN6";      // 数据（主入从出）
            public const string DSI_MOSI = "CRM_PIN15";     // 数据（主出从入）
            public const string DSI_RESETn = "CRM_PIN17";   // 复位，低有效
        }

        /// <summary>
        /// DO通道名称定义
        /// </summary>
        public static readonly string[] DoChannelNames = new string[]
        {
            "DO1 (XFR)",
            "DO2 (MANUAL_REFUEL)",
            "DO3 (DEFUEL)",
            "DO4 (AUTO_REFUEL)",
            "DO5 (OFF)",
            "DO6 (POWER ON SW)",
            "DO7 (RIGHT SOV OPEN)",
            "DO8 (CENTER SOV OPEN)",
            "DO9 (INCREASE)",
            "DO10 (DECREASE)",
            "DO11 (START)",
            "DO12 (STOP/SOV TEST)",
            "DO13 (LEFT SOV OPEN)",
            "DO14 (POWER SW)"
        };

        /// <summary>
        /// DO通道槽位映射（1槽1-17通道，跳过GND）
        /// </summary>
        public static readonly int[] DoSlotChannels = new int[]
        {
            1, 2, 3, 4, 5,      // DO1-DO5
            7, 8,               // DO6-DO7 (跳过6=GND)
            10, 11,             // DO8-DO9 (跳过9=GND)
            12, 13, 14, 15,     // DO10-DO13
            17                  // DO14 (跳过16=GND)
        };

        #endregion

        #region 供电控制仿真

        /// <summary>
        /// 仿真设置组件28V供电状态
        /// </summary>
        public async Task ApplyComponent28VStateAsync(Action<string> log, CancellationToken token)
        {
            EnsureNotDisposed();
            log?.Invoke("[仿真] 正在设置组件28V供电状态...");
            await Task.Delay(150, token).ConfigureAwait(false);
            _isPowerOn = true;
            log?.Invoke("[仿真] 组件28V供电状态已设置");
        }

        /// <summary>
        /// 仿真设置组件下电状态
        /// </summary>
        public async Task ApplyComponentDownStateAsync(Action<string> log, CancellationToken token)
        {
            EnsureNotDisposed();
            log?.Invoke("[仿真] 正在设置组件下电状态...");
            await Task.Delay(100, token).ConfigureAwait(false);
            _isPowerOn = false;
            log?.Invoke("[仿真] 组件已下电");
        }

        #endregion

        #region 矩阵开关仿真

        /// <summary>
        /// 仿真连接矩阵开关通路
        /// </summary>
        public async Task ConnectMatrixAsync(Action<string> log, CancellationToken token)
        {
            EnsureNotDisposed();
            log?.Invoke($"[仿真] 正在连接矩阵开关 {MatrixIp} 槽位 {MatrixSlot}...");
            await Task.Delay(100, token).ConfigureAwait(false);
            _isMatrixConnected = true;
            log?.Invoke("[仿真] 矩阵开关通路已连接");
        }

        /// <summary>
        /// 仿真断开矩阵开关通路
        /// </summary>
        public async Task DisconnectMatrixAsync(Action<string> log, CancellationToken token)
        {
            EnsureNotDisposed();
            log?.Invoke("[仿真] 正在断开矩阵开关通路...");
            await Task.Delay(80, token).ConfigureAwait(false);
            _isMatrixConnected = false;
            log?.Invoke("[仿真] 矩阵开关通路已断开");
        }

        #endregion

        #region DO输出控制仿真

        /// <summary>
        /// 设置所有DO通道为接地状态（提供[0,2V]电压输入）
        /// </summary>
        public async Task SetAllDoGroundedAsync(Action<string> log, CancellationToken token)
        {
            EnsureNotDisposed();
            log?.Invoke("[仿真] 正在设置所有DO通道为接地状态...");
            await Task.Delay(50, token).ConfigureAwait(false);

            for (int i = 0; i < DoChannelCount; i++)
            {
                _doChannelStates[i] = true; // 接地
            }

            log?.Invoke("[仿真] 所有DO通道已设置为接地状态（提供[0,2V]电压输入）");
        }

        /// <summary>
        /// 设置所有DO通道为开路状态
        /// </summary>
        public async Task SetAllDoOpenAsync(Action<string> log, CancellationToken token)
        {
            EnsureNotDisposed();
            log?.Invoke("[仿真] 正在设置所有DO通道为开路状态...");
            await Task.Delay(50, token).ConfigureAwait(false);

            for (int i = 0; i < DoChannelCount; i++)
            {
                _doChannelStates[i] = false; // 开路
            }

            log?.Invoke("[仿真] 所有DO通道已设置为开路状态");
        }

        /// <summary>
        /// 设置单个DO通道状态
        /// </summary>
        public async Task SetDoChannelStateAsync(int channelIndex, bool grounded, Action<string> log, CancellationToken token)
        {
            EnsureNotDisposed();
            if (channelIndex < 0 || channelIndex >= DoChannelCount)
                throw new ArgumentOutOfRangeException(nameof(channelIndex));

            await Task.Delay(20, token).ConfigureAwait(false);
            _doChannelStates[channelIndex] = grounded;
            log?.Invoke($"[仿真] {DoChannelNames[channelIndex]} 已设置为{(grounded ? "接地" : "开路")}状态");
        }

        /// <summary>
        /// 获取DO通道状态
        /// </summary>
        public bool GetDoChannelState(int channelIndex)
        {
            if (channelIndex < 0 || channelIndex >= DoChannelCount)
                throw new ArgumentOutOfRangeException(nameof(channelIndex));
            return _doChannelStates[channelIndex];
        }

        #endregion

        #region SPI通信与离散量采集仿真

        /// <summary>
        /// 仿真SPI通信读取离散量采集结果
        /// </summary>
        public async Task<int[]> ReadDiscreteInputsAsync(Action<string> log, CancellationToken token)
        {
            EnsureNotDisposed();
            log?.Invoke("[仿真] 正在通过SPI通信读取离散量采集结果...");
            await Task.Delay(100, token).ConfigureAwait(false);

            // 根据DO通道状态生成采集结果
            // 接地状态 -> 采集结果为1
            // 开路状态 -> 采集结果为0
            for (int i = 0; i < TotalChannelCount; i++)
            {
                if (i < DoChannelCount)
                {
                    _acquisitionResults[i] = _doChannelStates[i] ? 1 : 0;
                }
                else
                {
                    // 超出DO通道范围的，默认为0
                    _acquisitionResults[i] = 0;
                }
            }

            log?.Invoke("[仿真] SPI通信完成，已获取采集结果");
            return (int[])_acquisitionResults.Clone();
        }

        /// <summary>
        /// 获取Bank0采集结果 [0:6]
        /// </summary>
        public int[] GetBank0Results()
        {
            int[] bank0 = new int[Bank0ChannelCount];
            Array.Copy(_acquisitionResults, 0, bank0, 0, Bank0ChannelCount);
            return bank0;
        }

        /// <summary>
        /// 获取Bank1采集结果 [0:6]
        /// </summary>
        public int[] GetBank1Results()
        {
            int[] bank1 = new int[Bank1ChannelCount];
            Array.Copy(_acquisitionResults, Bank0ChannelCount, bank1, 0, Bank1ChannelCount);
            return bank1;
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
        }

        private void EnsureNotDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(DiscreteInputSimulation));
        }

        #endregion
    }
}
