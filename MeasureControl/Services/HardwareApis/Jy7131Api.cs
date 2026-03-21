using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MeasureControl.Drivers;
using MeasureControl.Helpers;
using MeasureControl.Models.Devices;

namespace MeasureControl.Services.HardwareApis
{
    /// <summary>
    /// PXIe-7131 数字输出模式
    /// </summary>
    public enum Jy7131OutputMode
    {
        Sourcing,  // 源型输出（输出高电平时提供电流）
        Sinking,   // 漏型输出（输出低电平时吸收电流）
        PushPull   // 推挽输出（双向驱动）
    }

    /// <summary>
    /// PXIe-7131 数字输入阈值电压配置（8组）
    /// </summary>
    public sealed class Jy7131DiThresholds
    {
        public double Group1 { get; set; }
        public double Group2 { get; set; }
        public double Group3 { get; set; }
        public double Group4 { get; set; }
        public double Group5 { get; set; }
        public double Group6 { get; set; }
        public double Group7 { get; set; }
        public double Group8 { get; set; }

        public double[] ToArray() => new[] { Group1, Group2, Group3, Group4, Group5, Group6, Group7, Group8 };
    }

    /// <summary>
    /// PXIe-7131 数字 I/O 板卡控制接口
    /// 功能：
    /// 1. 数字输入/输出（32路 DI + 32路 DO）
    /// 2. 外部 485 继电器控制（16路，通过 COM24）
    /// 3. DI 阈值电压设置（通过 COM14）
    /// 4. 电源输出控制（4组可调电压）
    /// </summary>
    public interface IJy7131Api : IAsyncDisposable
    {
        bool IsConnected { get; }  // 是否已连接到板卡
        bool IsRunning { get; }    // 板卡是否正在运行

        Task ConnectAsync(CancellationToken cancellationToken = default);  // 连接到板卡
        Task DisconnectAsync(CancellationToken cancellationToken = default);  // 断开板卡连接

        Task StartAsync(CancellationToken cancellationToken = default);  // 启动板卡
        Task StopAsync(CancellationToken cancellationToken = default);   // 停止板卡

        Task SetOutputModeAsync(Jy7131OutputMode mode, CancellationToken cancellationToken = default);

        Task<bool> ReadDiAsync(string diChannel, CancellationToken cancellationToken = default);  // 读取单个数字输入通道（DI0-DI31）
        Task WriteDoAsync(string doChannel, bool value, CancellationToken cancellationToken = default);  // 写入单个数字输出通道（DO0-DO31）

        Task<uint> ReadDiBitmaskAsync(CancellationToken cancellationToken = default);
        Task<uint> ReadDoBitmaskAsync(CancellationToken cancellationToken = default);
        Task WriteDoBitmaskAsync(uint mask, CancellationToken cancellationToken = default);

        Task ResetAllDoAsync(CancellationToken cancellationToken = default);

        Task ApplyDiThresholdsAsync(Jy7131DiThresholds thresholds, CancellationToken cancellationToken = default);

        Task<bool[]> ReadRelayStatesAsync(CancellationToken cancellationToken = default);  // 读取外部 485 继电器状态（16路）
        Task SetRelayAsync(int index, bool on, CancellationToken cancellationToken = default);  // 控制单个 485 继电器（index: 0-15）
        Task SetAllRelaysAsync(bool on, CancellationToken cancellationToken = default);  // 控制所有 485 继电器

        Task SetPowerVoltagesAsync(double group1Voltage, double group2Voltage, double group3Voltage, double group4Voltage, CancellationToken cancellationToken = default);
        Task EnablePowerOutputsAsync(double group1Voltage, double group2Voltage, double group3Voltage, double group4Voltage, CancellationToken cancellationToken = default);
        Task DisablePowerOutputsAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// PXIe-7131 数字 I/O 板卡实现类
    /// 集成了板卡本身的 DI/DO 功能 + 外部 485 继电器控制 + 阈值设置
    /// </summary>
    public sealed class Jy7131Api : IJy7131Api
    {
        private const string ThresholdComPort = "COM40";  // DI 阈值设置串口 加放油
        private const int ThresholdBaudRate = 115200;

        private const string RelayComPort = "COM30";      // 外部485继电器/电源控制串口
        private const int RelayBaudRate = 9600;
        private const byte RelaySlaveAddress = 1;
        private const ushort RelayStartCoilAddress = 0;
        private const int RelayChannelCount = 16;         // 外部继电器数量（16路）

        private readonly DeviceBase _device;
        private readonly int _slotNumber;
        private JY7131Driver _driver;
        private readonly SemaphoreSlim _lifecycleLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _ioLock = new SemaphoreSlim(1, 1);
        private bool _isRunning;
        private bool _disposed;

        public Jy7131Api(DeviceBase device, int slotNumber = 12)
        {
            _device = device ?? throw new ArgumentNullException(nameof(device));
            _slotNumber = slotNumber;
        }

        public bool IsConnected => _driver?.IsConnected == true;

        public bool IsRunning => _isRunning;

        /// <summary>
        /// 连接到 PXIe-7131 板卡
        /// </summary>
        public async Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (IsConnected)
                    return;

                _driver = new JY7131Driver(_device, _slotNumber);
                var ok = await _driver.ConnectAsync().ConfigureAwait(false);
                if (!ok)
                    throw new InvalidOperationException("JY7131 connect returned false");

                _isRunning = false;
            }
            finally
            {
                _lifecycleLock.Release();
            }
        }

        /// <summary>
        /// 断开与 PXIe-7131 板卡的连接
        /// </summary>
        public async Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                // 如果未连接，直接返回（安全的重复调用）
                if (_driver == null)
                    return;

                try
                {
                    await _driver.DisconnectAsync().ConfigureAwait(false);
                }
                finally
                {
                    _isRunning = false;
                    _driver = null;
                }
            }
            finally
            {
                _lifecycleLock.Release();
            }
        }

        /// <summary>
        /// 启动板卡（开始数据采集）
        /// </summary>
        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            EnsureConnected();
            await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var ok = await _driver.StartAcquisitionAsync().ConfigureAwait(false);
                if (!ok)
                    throw new InvalidOperationException("JY7131 start acquisition failed");
                _isRunning = true;
            }
            finally
            {
                _ioLock.Release();
            }
        }

        /// <summary>
        /// 停止板卡（停止数据采集）
        /// </summary>
        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            EnsureConnected();
            await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var ok = await _driver.StopAcquisitionAsync().ConfigureAwait(false);
                if (!ok)
                    throw new InvalidOperationException("JY7131 stop acquisition failed");
                _isRunning = false;
            }
            finally
            {
                _ioLock.Release();
            }
        }

        /// <summary>
        /// 设置数字输出模式（源型/漏型/推挽）
        /// </summary>
        public async Task SetOutputModeAsync(Jy7131OutputMode mode, CancellationToken cancellationToken = default)
        {
            EnsureConnected();
            string raw = mode switch
            {
                Jy7131OutputMode.Sourcing => "Sourcing",
                Jy7131OutputMode.Sinking => "Sinking",
                Jy7131OutputMode.PushPull => "Push_Pull",
                _ => "Push_Pull"
            };

            await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await _driver.ReconfigureDoOutputModeAsync(raw).ConfigureAwait(false);
            }
            finally
            {
                _ioLock.Release();
            }
        }

        /// <summary>
        /// 读取单个数字输入通道（DI0-DI31）
        /// </summary>
        /// <param name="diChannel">通道名称，如 "DI0"、"DI15" 等</param>
        /// <returns>true=高电平，false=低电平</returns>
        public async Task<bool> ReadDiAsync(string diChannel, CancellationToken cancellationToken = default)
        {
            EnsureConnected();
            var idx = ParseChannelIndex(diChannel, "DI");

            await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var v = await _driver.ReadChannelAsync($"DI{idx}").ConfigureAwait(false);
                return v != 0;
            }
            finally
            {
                _ioLock.Release();
            }
        }

        /// <summary>
        /// 写入单个数字输出通道（DO0-DO31）
        /// </summary>
        /// <param name="doChannel">通道名称，如 "DO0"、"DO29" 等</param>
        /// <param name="value">true=高电平，false=低电平</param>
        public async Task WriteDoAsync(string doChannel, bool value, CancellationToken cancellationToken = default)
        {
            EnsureConnected();
            var idx = ParseChannelIndex(doChannel, "DO");
            double v = value ? 1.0 : 0.0;

            await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var ok = await _driver.WriteChannelAsync($"DO{idx}", v).ConfigureAwait(false);
                if (!ok)
                    throw new InvalidOperationException($"Write DO{idx} failed");
            }
            finally
            {
                _ioLock.Release();
            }
        }

        /// <summary>
        /// 读取所有 32 路数字输入的状态（位掩码形式）
        /// </summary>
        /// <returns>32位掩码，bit0=DI0, bit1=DI1, ..., bit31=DI31</returns>
        public async Task<uint> ReadDiBitmaskAsync(CancellationToken cancellationToken = default)
        {
            EnsureConnected();
            var ids = Enumerable.Range(0, 32).Select(i => $"DI{i}").ToList();

            await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var values = await _driver.ReadChannelsBatchAsync(ids).ConfigureAwait(false);
                uint mask = 0;
                for (int i = 0; i < 32; i++)
                {
                    if (values.TryGetValue($"DI{i}", out var v) && v != 0)
                        mask |= (1u << i);
                }
                return mask;
            }
            finally
            {
                _ioLock.Release();
            }
        }

        /// <summary>
        /// 读取所有 32 路数字输出的状态（位掩码形式）
        /// </summary>
        /// <returns>32位掩码，bit0=DO0, bit1=DO1, ..., bit31=DO31</returns>
        public async Task<uint> ReadDoBitmaskAsync(CancellationToken cancellationToken = default)
        {
            EnsureConnected();
            var ids = Enumerable.Range(0, 32).Select(i => $"DO{i}").ToList();

            await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var values = await _driver.ReadChannelsBatchAsync(ids).ConfigureAwait(false);
                uint mask = 0;
                for (int i = 0; i < 32; i++)
                {
                    if (values.TryGetValue($"DO{i}", out var v) && v != 0)
                        mask |= (1u << i);
                }
                return mask;
            }
            finally
            {
                _ioLock.Release();
            }
        }

        /// <summary>
        /// 批量写入所有 32 路数字输出（位掩码形式）
        /// </summary>
        /// <param name="mask">32位掩码，bit0=DO0, bit1=DO1, ..., bit31=DO31</param>
        public async Task WriteDoBitmaskAsync(uint mask, CancellationToken cancellationToken = default)
        {
            EnsureConnected();

            var dict = new Dictionary<string, double>(32, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < 32; i++)
            {
                bool bit = (mask & (1u << i)) != 0;
                dict[$"DO{i}"] = bit ? 1.0 : 0.0;
            }

            await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var ok = await _driver.WriteChannelsBatchAsync(dict).ConfigureAwait(false);
                if (!ok)
                    throw new InvalidOperationException("Write DO bitmask failed");
            }
            finally
            {
                _ioLock.Release();
            }
        }

        /// <summary>
        /// 复位所有数字输出为低电平（全部置 0）
        /// </summary>
        public async Task ResetAllDoAsync(CancellationToken cancellationToken = default)
        {
            await WriteDoBitmaskAsync(0u, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// 设置数字输入阈值电压（通过 COM14 串口发送到 DAC）
        /// </summary>
        /// <param name="thresholds">8组阈值电压配置</param>
        public async Task ApplyDiThresholdsAsync(Jy7131DiThresholds thresholds, CancellationToken cancellationToken = default)
        {
            if (thresholds == null)
                throw new ArgumentNullException(nameof(thresholds));

            var groups = thresholds.ToArray();
            if (groups.Length != 8)
                throw new ArgumentException("Threshold groups must be length 8", nameof(thresholds));

            using (await SerialPortMutex.AcquireAsync(ThresholdComPort).ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var cli = new DacGroupsSerialClient(ThresholdComPort, ThresholdBaudRate, dtrEnable: false, rtsEnable: false);
                cli.Send8Groups(
                    ClampThreshold(groups[0]),
                    ClampThreshold(groups[1]),
                    ClampThreshold(groups[2]),
                    ClampThreshold(groups[3]),
                    ClampThreshold(groups[4]),
                    ClampThreshold(groups[5]),
                    ClampThreshold(groups[6]),
                    ClampThreshold(groups[7]));
            }
        }

        /// <summary>
        /// 读取外部 485 继电器板的所有继电器状态（16路）
        /// </summary>
        /// <returns>长度为 16 的布尔数组，index 0-15 对应继电器 1-16</returns>
        public async Task<bool[]> ReadRelayStatesAsync(CancellationToken cancellationToken = default)
        {
            using (await SerialPortMutex.AcquireAsync(RelayComPort).ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                return await Task.Run(() =>
                {
                    using var cli = new RelayModbusClient(RelayComPort, RelaySlaveAddress, RelayBaudRate);
                    return cli.ReadCoils(RelayStartCoilAddress, (ushort)RelayChannelCount);
                }, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// 控制外部 485 继电器板的单个继电器（通过 COM24 串口 Modbus 协议）
        /// </summary>
        /// <param name="index">继电器索引 0-15（对应继电器 1-16）</param>
        /// <param name="on">true=吸合，false=断开</param>
        public async Task SetRelayAsync(int index, bool on, CancellationToken cancellationToken = default)
        {
            if (index < 0 || index >= RelayChannelCount)
                throw new ArgumentOutOfRangeException(nameof(index));

            using (await SerialPortMutex.AcquireAsync(RelayComPort).ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Run(() =>
                {
                    using var cli = new RelayModbusClient(RelayComPort, RelaySlaveAddress, RelayBaudRate);
                    cli.WriteSingleCoil((ushort)index, on);
                }, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// 控制外部 485 继电器板的所有继电器（16路同时操作）
        /// </summary>
        /// <param name="on">true=全部吸合，false=全部断开</param>
        public async Task SetAllRelaysAsync(bool on, CancellationToken cancellationToken = default)
        {
            using (await SerialPortMutex.AcquireAsync(RelayComPort).ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Run(() =>
                {
                    using var cli = new RelayModbusClient(RelayComPort, RelaySlaveAddress, RelayBaudRate);
                    cli.SetAll(RelayStartCoilAddress, RelayChannelCount, on);
                }, cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task SetPowerVoltagesAsync(double group1Voltage, double group2Voltage, double group3Voltage, double group4Voltage, CancellationToken cancellationToken = default)
        {
            EnsureConnected();
            await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await _driver.SetPowerVoltagesAsync(group1Voltage, group2Voltage, group3Voltage, group4Voltage).ConfigureAwait(false);
            }
            finally
            {
                _ioLock.Release();
            }
        }

        public async Task EnablePowerOutputsAsync(double group1Voltage, double group2Voltage, double group3Voltage, double group4Voltage, CancellationToken cancellationToken = default)
        {
            EnsureConnected();
            await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await _driver.EnsurePowerOutputsAsync(group1Voltage, group2Voltage, group3Voltage, group4Voltage).ConfigureAwait(false);
            }
            finally
            {
                _ioLock.Release();
            }
        }

        public async Task DisablePowerOutputsAsync(CancellationToken cancellationToken = default)
        {
            EnsureConnected();
            await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await _driver.StopPowerOutputAsync().ConfigureAwait(false);
            }
            finally
            {
                _ioLock.Release();
            }
        }

        private void EnsureConnected()
        {
            if (_driver == null || !_driver.IsConnected)
                throw new InvalidOperationException("JY7131 is not connected");
        }

        private static int ParseChannelIndex(string channel, string prefix)
        {
            if (string.IsNullOrWhiteSpace(channel))
                throw new ArgumentException("Channel is required", nameof(channel));

            var raw = channel.Trim();
            if (!raw.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"Channel must start with '{prefix}'", nameof(channel));

            var num = raw.Substring(prefix.Length);
            if (!int.TryParse(num, NumberStyles.Integer, CultureInfo.InvariantCulture, out var idx))
                throw new ArgumentException("Invalid channel index", nameof(channel));

            // Public API accepts 1-based channel number (DI1..DI32 / DO1..DO32).
            // Hardware uses 0-based (DI0..DI31 / DO0..DO31).
            if (idx >= 0 && idx <= 31)
                return idx;
            //if (idx >= 1 && idx <= 32)
            //    return idx - 1;

            throw new ArgumentOutOfRangeException(nameof(channel), "Channel index must be 0..31 or 1..32");
        }

        private static double ClampThreshold(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                return 0.0;
            if (value < -10.0)
                return -10.0;
            if (value > 10.0)
                return 10.0;
            return value;
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
                return;

            _disposed = true;

            try
            {
                await DisconnectAsync().ConfigureAwait(false);
            }
            catch
            {
            }
            finally
            {
                try { _lifecycleLock.Dispose(); } catch { }
                try { _ioLock.Dispose(); } catch { }
            }
        }
    }
}
