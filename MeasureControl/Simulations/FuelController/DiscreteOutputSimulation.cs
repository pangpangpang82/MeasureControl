using System;
using System.Threading;
using System.Threading.Tasks;
using MeasureControl.Services;

namespace MeasureControl.Simulations.FuelController
{
    public sealed class DiscreteOutputSimulation : IDisposable
    {
        private readonly Random _rand = new Random();
        private readonly SemaphoreSlim _matrixSwitchLock = new SemaphoreSlim(1, 1);

        private bool _disposed;
        private bool _matrixConnected;
        private bool _component28vOn;
        private bool _doGrounded;

        private const string MatrixIpAddress = "192.168.1.3";

        // 步骤a/b：J6~J13离散量输出对地阻抗采集
        // 对应矩阵对应表 "6.6离散量输出功能测试（对地阻抗采集节点）"
        //   J6~J13/J24 → ResAcquire6~13 → 2601(2) 1/12~1/19 → I1,O12~O19, slot=6
        //   万用表侧：2601(1) 4/2 → I4,O2, slot=4
        private const int MatrixSlotDo = 6;           // 2601(2) slotindex=6，信号侧
        private const int MatrixSlotDmmDo = 4;        // 2601(1) slotindex=4，万用表侧

        // J6~J13对应的矩阵节点（2601(2) 1/12~1/19）
        private static readonly (string In, string Out)[] DoChannels = new[]
        {
            ("I1", "O12"),  // J6/J24  ResAcquire6
            ("I1", "O13"),  // J7/J24  ResAcquire7
            ("I1", "O14"),  // J8/J24  ResAcquire8
            ("I1", "O15"),  // J9/J24  ResAcquire9
            ("I1", "O16"),  // J10/J24 ResAcquire10
            ("I1", "O17"),  // J11/J24 ResAcquire11
            ("I1", "O18"),  // J12/J24 ResAcquire12
            ("I1", "O19"),  // J13/J24 ResAcquire13
        };
        private static readonly (string In, string Out) MatrixDmmDo = ("I4", "O2");   // 万用表侧

        // 步骤c：J14（POWER_ON）电压测量
        // 对应矩阵对应表 "6.6离散量输出功能测试" J14/J4行
        //   2601(1) 4/0 → I4,O0, slot=4
        //   3022(10) 0/50 → I0,O50, slot=2, tcpBasePort=50300
        private const int MatrixSlot2601J14 = 4;      // 2601(1) slotindex=4
        private const int MatrixSlot3022J14 = 2;      // 3022(1) slotindx=2
        private const int MatrixTcpBasePort3022 = 50300;
        private static readonly (string In, string Out) Matrix2601J14 = ("I4", "O0");
        private static readonly (string In, string Out) Matrix3022J14 = ("I0", "O50");

        public async Task ApplyComponentDownStateAsync(Action<string> log, CancellationToken token = default)
        {
            await Task.Delay(60, token);
            _component28vOn = false;
            log?.Invoke("[SIM] 组件下电状态已设置");
        }

        public async Task ApplyComponent28VStateAsync(Action<string> log, CancellationToken token = default)
        {
            await Task.Delay(120, token);
            _component28vOn = true;
            log?.Invoke("[SIM] 组件28V供电状态已设置");
        }

        /// <summary>
        /// 连接步骤a/b矩阵通路：J6~J13对地阻抗采集
        /// 2601(2) 1/12~1/19 (I1,O12~O19,slot6) + 万用表侧 2601(1) I4,O2,slot4
        /// </summary>
        public async Task<bool> ConnectMatrixAsync(Action<string> log, CancellationToken token = default)
        {
            if (_matrixConnected)
            {
                log?.Invoke("[SIM] 矩阵开关已连接，跳过");
                return true;
            }

            await _matrixSwitchLock.WaitAsync(token);
            try
            {
                if (_matrixConnected)
                    return true;

                log?.Invoke("[SIM] 正在配置离散量输出对地阻抗采集矩阵通路（J6~J13）...");
                var svc = MatrixControlService.Instance;

                // 万用表侧
                bool okDmm = await svc.ConnectNodesAsync(MatrixDmmDo.In, MatrixDmmDo.Out, MatrixSlotDmmDo, MatrixIpAddress);
                log?.Invoke($"[SIM] 万用表侧: {MatrixDmmDo.In}->{MatrixDmmDo.Out} slot={MatrixSlotDmmDo}, ok={okDmm}");

                // 信号侧（J6~J13各自独立节点 2601(2) 1/12~1/19）
                bool allOk = true;
                foreach (var ch in DoChannels)
                {
                    bool ok = await svc.ConnectNodesAsync(ch.In, ch.Out, MatrixSlotDo, MatrixIpAddress);
                    log?.Invoke($"[SIM] 信号侧: {ch.In}->{ch.Out} slot={MatrixSlotDo}, ok={ok}");
                    allOk &= ok;
                }

                _matrixConnected = okDmm && allOk;
                return _matrixConnected;
            }
            catch (Exception ex)
            {
                log?.Invoke($"[SIM] 矩阵开关配置失败: {ex.Message}");
                return false;
            }
            finally
            {
                _matrixSwitchLock.Release();
            }
        }

        /// <summary>
        /// 断开步骤a/b矩阵通路
        /// </summary>
        public async Task DisconnectMatrixAsync(Action<string> log, CancellationToken token = default)
        {
            await _matrixSwitchLock.WaitAsync(token);
            try
            {
                if (!_matrixConnected)
                    return;

                log?.Invoke("[SIM] 正在断开矩阵开关通路...");
                var svc = MatrixControlService.Instance;

                // 断开万用表侧
                await svc.DisconnectNodesAsync(MatrixDmmDo.In, MatrixDmmDo.Out, MatrixSlotDmmDo, MatrixIpAddress);

                // 断开信号侧（J6~J13各自独立节点）
                foreach (var ch in DoChannels)
                {
                    await svc.DisconnectNodesAsync(ch.In, ch.Out, MatrixSlotDo, MatrixIpAddress);
                }

                _matrixConnected = false;
                log?.Invoke("[SIM] 矩阵开关通路已断开");
            }
            finally
            {
                _matrixSwitchLock.Release();
            }
        }

        /// <summary>
        /// 连接步骤c矩阵通路：J14电压测量
        /// 2601(1) 4/0 → I4,O0,slot4 + 3022(10) 0/50 → I0,O50,slot2(basePort50300)
        /// </summary>
        public async Task<bool> ConnectMatrixJ14Async(Action<string> log, CancellationToken token = default)
        {
            var svc = MatrixControlService.Instance;
            bool ok2601 = await svc.ConnectNodesAsync(Matrix2601J14.In, Matrix2601J14.Out, MatrixSlot2601J14, MatrixIpAddress);
            bool ok3022 = await svc.ConnectNodesAsync(Matrix3022J14.In, Matrix3022J14.Out, MatrixSlot3022J14, MatrixIpAddress, MatrixTcpBasePort3022);
            log?.Invoke($"[SIM] J14电压通路: 2601(1)={Matrix2601J14.In}->{Matrix2601J14.Out}(slot{MatrixSlot2601J14}) 3022(10)={Matrix3022J14.In}->{Matrix3022J14.Out}(slot{MatrixSlot3022J14},basePort{MatrixTcpBasePort3022}), ok={ok2601 && ok3022}");
            return ok2601 && ok3022;
        }

        /// <summary>
        /// 断开步骤c矩阵通路
        /// </summary>
        public async Task DisconnectMatrixJ14Async(Action<string> log, CancellationToken token = default)
        {
            var svc = MatrixControlService.Instance;
            await svc.DisconnectNodesAsync(Matrix2601J14.In, Matrix2601J14.Out, MatrixSlot2601J14, MatrixIpAddress);
            await svc.DisconnectNodesAsync(Matrix3022J14.In, Matrix3022J14.Out, MatrixSlot3022J14, MatrixIpAddress, MatrixTcpBasePort3022);
            log?.Invoke("[SIM] J14电压通路已断开");
        }

        public async Task SetDoGroundedAsync(Action<string> log, CancellationToken token = default)
        {
            await Task.Delay(80, token);
            _doGrounded = true;
            log?.Invoke("[SIM] DO已设置为接地/低电平(模拟0~2V输入)");
        }

        public async Task SetDoOpenAsync(Action<string> log, CancellationToken token = default)
        {
            await Task.Delay(80, token);
            _doGrounded = false;
            log?.Invoke("[SIM] DO已设置为开路");
        }

        public async Task<double> MeasureImpedanceToGroundAsync(Action<string> log, CancellationToken token = default)
        {
            await Task.Delay(300, token);

            if (_component28vOn)
            {
                log?.Invoke("[SIM] 当前为上电状态，阻抗测量仅用于占位仿真");
            }

            if (_doGrounded)
            {
                double v = 3.0 + _rand.NextDouble() * 4.0;
                return Math.Round(v, 3);
            }

            double high = 120000.0 + _rand.NextDouble() * 80000.0;
            return Math.Round(high, 0);
        }

        public async Task<double> MeasureJ14VoltageAsync(Action<string> log, CancellationToken token = default)
        {
            await Task.Delay(300, token);

            if (!_component28vOn)
            {
                log?.Invoke("[SIM] 当前为下电状态，电压测量将返回0(占位仿真)");
                return 0.0;
            }

            double v = 24.0 + (_rand.NextDouble() - 0.5) * 4.0;
            v = Math.Max(0.0, Math.Min(32.0, v));
            return Math.Round(v, 3);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _matrixConnected = false;
            _component28vOn = false;
            _matrixSwitchLock?.Dispose();
        }
    }
}
