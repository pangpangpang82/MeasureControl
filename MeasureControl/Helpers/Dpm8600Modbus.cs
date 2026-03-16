using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Ports;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MeasureControl.Helpers
{
    public static class SerialPortMutex
    {
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new ConcurrentDictionary<string, SemaphoreSlim>(StringComparer.OrdinalIgnoreCase);

        public static async Task<IDisposable> AcquireAsync(string portName)
        {
            if (string.IsNullOrWhiteSpace(portName)) throw new ArgumentException("portName不能为空", nameof(portName));
            var sem = _locks.GetOrAdd(portName, _ => new SemaphoreSlim(1, 1));
            await sem.WaitAsync().ConfigureAwait(false);
            return new Releaser(sem);
        }

        private sealed class Releaser : IDisposable
        {
            private SemaphoreSlim _sem;

            public Releaser(SemaphoreSlim sem)
            {
                _sem = sem;
            }

            public void Dispose()
            {
                var s = Interlocked.Exchange(ref _sem, null);
                if (s != null)
                {
                    try { s.Release(); } catch { }
                }
            }
        }
    }

    public enum PowerSupplyProtocol
    {
        ModbusRtu,
        AsciiCustom
    }

    public sealed class Dpm8600Client : IDisposable
    {
        private readonly SerialPort _sp;
        private readonly object _ioLock = new();
        private readonly byte _slave;
        public PowerSupplyProtocol Protocol { get; }

        // ====== 构造与连接 ======
        public Dpm8600Client(
            string com,
            PowerSupplyProtocol protocol,
            byte slave = 1,
            int baud = 9600,
            int readTimeoutMs = 800,
            int writeTimeoutMs = 800)
        {
            Protocol = protocol;
            _slave = slave;

            _sp = new SerialPort(com, baud, Parity.None, 8, StopBits.One)
            {
                ReadTimeout = readTimeoutMs,
                WriteTimeout = writeTimeoutMs,
                Encoding = Encoding.ASCII,
                NewLine = "\r\n"
            };

            try
            {
                _sp.Open(); // 连接
            }
            catch (Exception ex)
            {
                throw new IOException($"打开串口失败：{com}（可能被占用/无权限/参数错误）", ex);
            }
        }

        public bool IsOpen => _sp.IsOpen;

        public void Dispose()
        {
            try { if (_sp.IsOpen) _sp.Close(); } catch { /* ignore */ }
            _sp.Dispose();
        }

        public (double U, double I) ReadUI_Rtu()
        {
            EnsureProtocol(PowerSupplyProtocol.ModbusRtu);

            // U=0x1001 (2位小数), I=0x1002 (3位小数)
            var regs = ReadHoldingRegisters_Rtu(0x1001, 2);
            return (regs[0] / 100.0, regs[1] / 1000.0);
        }

        public void SetVoltage_Rtu(double volts)
        {
            EnsureProtocol(PowerSupplyProtocol.ModbusRtu);
            WriteSingleRegister_Rtu(0x0000, ToUShortChecked(Math.Round(volts * 100, MidpointRounding.AwayFromZero), "voltage*100"));
            //WriteSingleRegister_Rtu(0x0000, ToUShortChecked(Math.Round(volts, MidpointRounding.AwayFromZero), "voltage*100"));
        }

        public void SetCurrent_Rtu(double amps)
        {
            EnsureProtocol(PowerSupplyProtocol.ModbusRtu);
            WriteSingleRegister_Rtu(0x0001, ToUShortChecked(Math.Round(amps * 1000, MidpointRounding.AwayFromZero), "current*1000"));
            //WriteSingleRegister_Rtu(0x0001, ToUShortChecked(Math.Round(amps, MidpointRounding.AwayFromZero), "current*1000"));
        }

        public void SetOutput_Rtu(bool on)
        {
            EnsureProtocol(PowerSupplyProtocol.ModbusRtu);
            WriteSingleRegister_Rtu(0x0002, (ushort)(on ? 1 : 0));
        }

        public ushort[] ReadHoldingRegisters_Rtu(ushort start, ushort count)
        {
            byte[] req = new byte[8];
            req[0] = _slave;
            req[1] = 0x03;
            req[2] = (byte)(start >> 8);
            req[3] = (byte)(start & 0xFF);
            req[4] = (byte)(count >> 8);
            req[5] = (byte)(count & 0xFF);
            AppendCrc(req);

            // RTU 响应长度 = 1(addr)+1(func)+1(byteCount)+2*count(data)+2(crc)
            int expected = 5 + 2 * count;
            byte[] resp = TransceiveRtu(req, expected);

            ValidateRtuResponse(resp, 0x03, count);

            ushort[] regs = new ushort[count];
            for (int i = 0; i < count; i++)
                regs[i] = (ushort)((resp[3 + 2 * i] << 8) | resp[4 + 2 * i]);
            return regs;
        }

        public void WriteSingleRegister_Rtu(ushort reg, ushort val)
        {
            byte[] req = new byte[8];
            req[0] = _slave;
            req[1] = 0x06;
            req[2] = (byte)(reg >> 8);
            req[3] = (byte)(reg & 0xFF);
            req[4] = (byte)(val >> 8);
            req[5] = (byte)(val & 0xFF);
            AppendCrc(req);

            byte[] resp = TransceiveRtu(req, expectedLen: 8);
            if (!CheckCrc(resp)) throw new IOException("Modbus CRC 错误（写寄存器回包校验失败）");

            // 0x06 正常回显同一帧（前6字节一致）
            for (int i = 0; i < 6; i++)
                if (resp[i] != req[i])
                    throw new IOException("Modbus 写寄存器回显不匹配（可能地址/线序/干扰）");
        }

        /// <summary>
        /// 发送一帧RTU并读取指定长度响应
        /// </summary>
        public byte[] TransceiveRtu(byte[] req, int expectedLen)
        {
            EnsureProtocol(PowerSupplyProtocol.ModbusRtu);

            lock (_ioLock)
            {
                _sp.DiscardInBuffer();
                _sp.DiscardOutBuffer();

                Thread.Sleep(8); // 帧间隔

                _sp.Write(req, 0, req.Length);

                // 等首字节
                var waitStart = Environment.TickCount;
                while (_sp.BytesToRead == 0)
                {
                    if (Environment.TickCount - waitStart > _sp.ReadTimeout)
                        throw new TimeoutException("等待 Modbus 响应超时（无首字节）");
                    Thread.Sleep(2);
                }

                // 先读 3 字节头：addr, func, (byteCount 或 exCode 或 regHi)
                byte[] head = ReadExact(3);
                byte func = head[1];

                // 异常响应：总长 5 字节（addr, func|0x80, exCode, crcLo, crcHi）
                if ((func & 0x80) != 0)
                {
                    byte[] rest = ReadExact(2); // CRC
                    var full = Combine(head, rest);
                    if (!CheckCrc(full)) throw new IOException("Modbus CRC 错误（异常响应）");
                    throw new IOException($"Modbus 异常响应：Func=0x{func:X2}, ExCode=0x{head[2]:X2}");
                }

                // 正常响应：按功能码决定剩余长度
                int remaining;
                if (func == 0x03) // 读保持寄存器
                {
                    int byteCount = head[2];
                    remaining = byteCount + 2; // data + CRC
                }
                else if (func == 0x06 || func == 0x10) // 写单/写多：固定 8 字节
                {
                    remaining = 8 - 3; // 已读3，还差5
                }
                else
                {
                    // 兜底：按 expectedLen 补齐
                    remaining = Math.Max(0, expectedLen - head.Length);
                }

                byte[] body = remaining > 0 ? ReadExact(remaining) : Array.Empty<byte>();
                return Combine(head, body);
            }
        }


        private byte[] ReadExact(int count)
        {
            byte[] buf = new byte[count];
            int off = 0;
            while (off < count)
            {
                int n = _sp.Read(buf, off, count - off);
                if (n == 0)
                {
                    throw new IOException($"串口读取返回0，已读{off}/{count}，可能连接中断或超时。");
                }
                off += n;
            }
            return buf;
        }

        private static byte[] Combine(byte[] a, byte[] b)
        {
            if (b == null || b.Length == 0) return a;
            var r = new byte[a.Length + b.Length];
            Buffer.BlockCopy(a, 0, r, 0, a.Length);
            Buffer.BlockCopy(b, 0, r, a.Length, b.Length);
            return r;
        }

        private static void ValidateRtuResponse(byte[] resp, byte expectedFunc, ushort count)
        {
            if (!CheckCrc(resp)) throw new IOException("Modbus CRC 错误（线/波特率/干扰/解析）");

            // 异常响应：功能码最高位=1，后面跟异常码
            if ((resp[1] & 0x80) != 0)
            {
                byte exceptionCode = resp.Length > 2 ? resp[2] : (byte)0xFF;
                throw new IOException($"Modbus 异常响应：Func=0x{resp[1]:X2}, ExCode=0x{exceptionCode:X2}");
            }

            if (resp[1] != expectedFunc) throw new IOException($"功能码异常：0x{resp[1]:X2}");
            if (resp[2] != 2 * count) throw new IOException("字节数不匹配");
        }




        /// <summary>
        /// 发送ASCII命令（自动补\r\n）
        /// </summary>
        public string SendAscii(string command, bool expectReply = true)
        {
            EnsureProtocol(PowerSupplyProtocol.AsciiCustom);

            lock (_ioLock)
            {
                _sp.DiscardInBuffer();
                _sp.DiscardOutBuffer();

                if (!command.EndsWith("\r\n", StringComparison.Ordinal))
                    command += "\r\n";

                _sp.Write(command);

                if (!expectReply)
                {
                    Thread.Sleep(30);
                    return string.Empty;
                }

                try
                {
                    return _sp.ReadLine();
                }
                catch (TimeoutException)
                {
                    throw;
                }
            }
        }

        /// <summary>
        /// ASCII快速拼命令：地址两位 + 功能字符串
        /// </summary>
        public string SendAsciiCommand(byte addr, string body, bool expectReply = true)
        {
            EnsureProtocol(PowerSupplyProtocol.AsciiCustom);

            string cmd = $":{addr:D2}{body}";
            return SendAscii(cmd, expectReply);
        }

        // CRC16 
        private static void AppendCrc(byte[] frame)
        {
            ushort crc = Crc16(frame, frame.Length - 2);
            frame[frame.Length - 2] = (byte)(crc & 0xFF);   // CRC Lo
            frame[frame.Length - 1] = (byte)(crc >> 8);     // CRC Hi
        }

        private static bool CheckCrc(byte[] frame)
        {
            ushort crc = Crc16(frame, frame.Length - 2);
            return frame[frame.Length - 2] == (byte)(crc & 0xFF) && frame[frame.Length - 1] == (byte)(crc >> 8);
        }

        private static ushort Crc16(byte[] data, int len)
        {
            ushort crc = 0xFFFF;
            for (int i = 0; i < len; i++)
            {
                crc ^= data[i];
                for (int b = 0; b < 8; b++)
                    crc = (ushort)(((crc & 1) != 0) ? ((crc >> 1) ^ 0xA001) : (crc >> 1));
            }
            return crc;
        }

        private void EnsureProtocol(PowerSupplyProtocol expected)
        {
            if (Protocol != expected)
                throw new InvalidOperationException($"当前实例协议={Protocol}，不能调用 {expected} 的方法。");
        }

        private static ushort ToUShortChecked(double val, string name)
        {
            if (val < 0 || val > ushort.MaxValue)
                throw new ArgumentOutOfRangeException(name, $"数值超出 ushort 范围：{val}");
            return (ushort)val;
        }
    }

    public sealed class RelayModbusClient : IDisposable
    {
        private readonly SerialPort _sp;
        private readonly object _ioLock = new();
        private readonly byte _slave;

        public RelayModbusClient(
            string com,
            byte slave,
            int baud = 9600,
            int readTimeoutMs = 500,
            int writeTimeoutMs = 500)
        {
            _slave = slave;

            _sp = new SerialPort(com, baud, Parity.None, 8, StopBits.One)
            {
                ReadTimeout = readTimeoutMs,
                WriteTimeout = writeTimeoutMs,
                Handshake = Handshake.None,
                DtrEnable = false,
                RtsEnable = false
            };

            try
            {
                _sp.Open();
            }
            catch (Exception ex)
            {
                throw new IOException($"打开串口失败：{com}（可能被占用/无权限/参数错误）", ex);
            }
        }

        public bool IsOpen => _sp.IsOpen;

        public void Dispose()
        {
            try { if (_sp.IsOpen) _sp.Close(); } catch { }
            _sp.Dispose();
        }

        public void WriteSingleCoil(ushort coilAddress, bool on)
        {
            byte[] req = new byte[8];
            req[0] = _slave;
            req[1] = 0x05;
            req[2] = (byte)(coilAddress >> 8);
            req[3] = (byte)(coilAddress & 0xFF);
            req[4] = (byte)(on ? 0xFF : 0x00);
            req[5] = 0x00;
            AppendCrc(req);

            byte[] resp = TransceiveRtu(req, expectedLen: 8);
            if (!CheckCrc(resp)) throw new IOException("Modbus CRC 错误（写线圈回包校验失败）");

            for (int i = 0; i < 6; i++)
            {
                if (resp[i] != req[i])
                {
                    throw new IOException("Modbus 写线圈回显不匹配（可能地址/线序/干扰）");
                }
            }
        }

        public void WriteMultipleCoils(ushort startCoilAddress, bool[] values)
        {
            if (values == null || values.Length == 0)
            {
                throw new ArgumentException("values不能为空", nameof(values));
            }

            ushort quantity = (ushort)values.Length;
            int byteCount = (quantity + 7) / 8;
            byte[] data = new byte[byteCount];

            for (int i = 0; i < quantity; i++)
            {
                if (values[i])
                {
                    data[i / 8] |= (byte)(1 << (i % 8));
                }
            }

            byte[] req = new byte[7 + byteCount + 2];
            req[0] = _slave;
            req[1] = 0x0F;
            req[2] = (byte)(startCoilAddress >> 8);
            req[3] = (byte)(startCoilAddress & 0xFF);
            req[4] = (byte)(quantity >> 8);
            req[5] = (byte)(quantity & 0xFF);
            req[6] = (byte)byteCount;
            Buffer.BlockCopy(data, 0, req, 7, byteCount);
            AppendCrc(req);

            byte[] resp = TransceiveRtu(req, expectedLen: 8);
            if (!CheckCrc(resp)) throw new IOException("Modbus CRC 错误（写多线圈回包校验失败）");

            if (resp[0] != req[0] || resp[1] != req[1])
            {
                throw new IOException("Modbus 写多线圈响应帧头不匹配");
            }

            for (int i = 2; i < 6; i++)
            {
                if (resp[i] != req[i])
                {
                    throw new IOException("Modbus 写多线圈回显不匹配（可能地址/线序/干扰）");
                }
            }
        }

        public bool[] ReadCoils(ushort startCoilAddress, ushort count)
        {
            if (count == 0) return Array.Empty<bool>();

            byte[] req = new byte[8];
            req[0] = _slave;
            req[1] = 0x01;
            req[2] = (byte)(startCoilAddress >> 8);
            req[3] = (byte)(startCoilAddress & 0xFF);
            req[4] = (byte)(count >> 8);
            req[5] = (byte)(count & 0xFF);
            AppendCrc(req);

            int byteCount = (count + 7) / 8;
            int expectedLen = 5 + byteCount;
            byte[] resp = TransceiveRtu(req, expectedLen);
            if (!CheckCrc(resp)) throw new IOException("Modbus CRC 错误（读线圈回包校验失败）");

            if (resp[0] != _slave || resp[1] != 0x01)
            {
                throw new IOException("Modbus 读线圈响应帧头不匹配");
            }

            if (resp[2] != (byte)byteCount)
            {
                throw new IOException("Modbus 读线圈响应字节数不匹配");
            }

            var values = new bool[count];
            for (int i = 0; i < count; i++)
            {
                int b = resp[3 + (i / 8)];
                values[i] = ((b >> (i % 8)) & 0x01) != 0;
            }
            return values;
        }

        public void SetAll(ushort startCoilAddress, int count, bool on)
        {
            if (count <= 0) return;
            var values = new bool[count];
            for (int i = 0; i < count; i++) values[i] = on;
            WriteMultipleCoils(startCoilAddress, values);
        }

        private byte[] TransceiveRtu(byte[] req, int expectedLen)
        {
            lock (_ioLock)
            {
                _sp.DiscardInBuffer();
                _sp.DiscardOutBuffer();

                Thread.Sleep(8);

                _sp.Write(req, 0, req.Length);

                var waitStart = Environment.TickCount;
                while (_sp.BytesToRead == 0)
                {
                    if (Environment.TickCount - waitStart > _sp.ReadTimeout)
                        throw new TimeoutException("等待 Modbus 响应超时（无首字节）");
                    Thread.Sleep(2);
                }

                byte[] head = ReadExact(3);
                byte func = head[1];

                if ((func & 0x80) != 0)
                {
                    byte[] rest = ReadExact(2);
                    var full = Combine(head, rest);
                    if (!CheckCrc(full)) throw new IOException("Modbus CRC 错误（异常响应）");
                    throw new IOException($"Modbus 异常响应：Func=0x{func:X2}, ExCode=0x{head[2]:X2}");
                }

                int remaining = Math.Max(0, expectedLen - head.Length);
                byte[] body = remaining > 0 ? ReadExact(remaining) : Array.Empty<byte>();
                return Combine(head, body);
            }
        }

        private byte[] ReadExact(int count)
        {
            byte[] buf = new byte[count];
            int off = 0;
            while (off < count)
            {
                int n = _sp.Read(buf, off, count - off);
                if (n == 0)
                {
                    throw new IOException($"串口读取返回0，已读{off}/{count}，可能连接中断或超时。");
                }
                off += n;
            }
            return buf;
        }

        private static byte[] Combine(byte[] a, byte[] b)
        {
            if (b == null || b.Length == 0) return a;
            var r = new byte[a.Length + b.Length];
            Buffer.BlockCopy(a, 0, r, 0, a.Length);
            Buffer.BlockCopy(b, 0, r, a.Length, b.Length);
            return r;
        }

        private static void AppendCrc(byte[] frame)
        {
            ushort crc = Crc16(frame, frame.Length - 2);
            frame[frame.Length - 2] = (byte)(crc & 0xFF);
            frame[frame.Length - 1] = (byte)(crc >> 8);
        }

        private static bool CheckCrc(byte[] frame)
        {
            ushort crc = Crc16(frame, frame.Length - 2);
            return frame[frame.Length - 2] == (byte)(crc & 0xFF) && frame[frame.Length - 1] == (byte)(crc >> 8);
        }

        private static ushort Crc16(byte[] data, int len)
        {
            ushort crc = 0xFFFF;
            for (int i = 0; i < len; i++)
            {
                crc ^= data[i];
                for (int b = 0; b < 8; b++)
                    crc = (ushort)(((crc & 1) != 0) ? ((crc >> 1) ^ 0xA001) : (crc >> 1));
            }
            return crc;
        }
    }

    public sealed class DacGroupsSerialClient : IDisposable
    {
        private readonly SerialPort _sp;
        private readonly object _ioLock = new();

        public DacGroupsSerialClient(
            string portName,
            int baud = 115200,
            int readTimeoutMs = 500,
            int writeTimeoutMs = 500,
            bool dtrEnable = false,
            bool rtsEnable = false)
        {
            _sp = new SerialPort(portName, baud, Parity.None, 8, StopBits.One)
            {
                Handshake = Handshake.None,
                ReadTimeout = readTimeoutMs,
                WriteTimeout = writeTimeoutMs,
                DtrEnable = dtrEnable,
                RtsEnable = rtsEnable,
                NewLine = "\r\n"
            };

            try
            {
                _sp.Open();
            }
            catch (Exception ex)
            {
                throw new IOException($"打开串口失败：{portName}（可能被占用/无权限/参数错误）", ex);
            }

            Thread.Sleep(100);
        }

        public void Dispose()
        {
            try { if (_sp.IsOpen) _sp.Close(); } catch { }
            _sp.Dispose();
        }

        public void Send8Groups(double g0, double g1, double g2, double g3, double g4, double g5, double g6, double g7)
        {
            Send8Groups(new[] { g0, g1, g2, g3, g4, g5, g6, g7 });
        }

        public void Send8Groups(double[] groupVoltages8)
        {
            if (groupVoltages8 is null || groupVoltages8.Length != 8)
                throw new ArgumentException("groupVoltages8 must have length 8", nameof(groupVoltages8));

            const byte H1 = 0xAA, H2 = 0x55, CMD = 0x0E;
            byte[] frame = new byte[20];
            frame[0] = H1;
            frame[1] = H2;
            frame[2] = CMD;

            int idx = 3;
            int sum = CMD;

            for (int i = 0; i < 8; i++)
            {
                ushort code = VoltageToU16(groupVoltages8[i]);
                byte hi = (byte)(code >> 8);
                byte lo = (byte)(code & 0xFF);

                frame[idx++] = hi; sum += hi;
                frame[idx++] = lo; sum += lo;
            }

            frame[idx] = (byte)(sum & 0xFF);
            Write(frame);
        }

        public void SendRaw16Bytes(byte[] data16)
        {
            if (data16 == null || data16.Length != 16) throw new ArgumentException("need 16 bytes", nameof(data16));

            const byte H1 = 0xAA, H2 = 0x55, CMD = 0x0E;
            byte[] frame = new byte[20];
            frame[0] = H1;
            frame[1] = H2;
            frame[2] = CMD;
            Buffer.BlockCopy(data16, 0, frame, 3, 16);

            int sum = CMD;
            for (int i = 0; i < 16; i++) sum += data16[i];
            frame[19] = (byte)(sum & 0xFF);

            Write(frame);
        }

        private void Write(byte[] bytes)
        {
            lock (_ioLock)
            {
                _sp.Write(bytes, 0, bytes.Length);
                _sp.BaseStream.Flush();
            }
        }

        private static ushort VoltageToU16(double v, double vMin = -10.0, double vMax = 10.0)
        {
            if (double.IsNaN(v) || double.IsInfinity(v))
                v = 0;

            if (v < vMin) v = vMin;
            if (v > vMax) v = vMax;

            double t = (v - vMin) / (vMax - vMin);
            int code = (int)Math.Round(t * 65535.0, MidpointRounding.AwayFromZero);
            if (code < 0) code = 0;
            if (code > 65535) code = 65535;
            return (ushort)code;
        }
    }


}
