using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MeasureControl.Drivers;
using MeasureControl.Helpers;
using MeasureControl.Services;
using MeasureControl.Models.Devices;
using MeasureControl.Models.Devices.DeviceCategories;
using Newtonsoft.Json;
using NationalInstruments.Visa;

namespace MeasureControl.Helpers.SelfInspection
{
    internal sealed class PXIe7131DidoSelfInspectionTask : ISelfInspectionTask
    {
        //private const string ThresholdComPort = "COM14"; // 第一套
        //private const string ThresholdComPort = "COM10"; // 第二套
        private const string ThresholdComPort = "COM24"; // 第三套
        private const int ThresholdBaudRate = 115200;
        private const double InitialThresholdGroup0To3ActualV = 29.0;
        private const double InitialThresholdGroup4To7ActualV = 0.6;

        private const double ScenarioThresholdHighV = 30.0;
        private const double ScenarioThresholdLowV = 2.8;

        private const double ScenarioDo32V = 32.0;
        private const double ScenarioDo29V = 29.0;
        private const double ScenarioDo33V = 3.3;
        private const double ScenarioDo23V = 2.3;

        private const double ScenarioToleranceV = 0.1;

        private const string MatrixIpAddress = "192.168.1.3";
        private const int MatrixSelectSlotIndex = 2;
        private const int Matrix3022TcpBasePort = 50300;
        private const int MatrixCommonSlotIndex = 4;
        private const string MatrixSelectInputNodeId = "I0";
        private const string MatrixCommonInputNodeId = "I4";
        private const string MatrixCommonOutputNodeId = "O0";

        private const string DmmIpAddress = "192.168.1.13";
        private const int DmmStabilizeDelayMs = 80;

        private const string ReportTemplateRelativePath = "Projects\\自检报表模板.xlsx";

        private static readonly IReadOnlyList<string> AllDiChannels = Enumerable.Range(0, 32).Select(i => $"DI{i}").ToList();
        private static readonly IReadOnlyList<string> AllDoChannels = Enumerable.Range(0, 32).Select(i => $"DO{i}").ToList();
        private static readonly IReadOnlyList<string> Di0To15 = Enumerable.Range(0, 16).Select(i => $"DI{i}").ToList();
        private static readonly IReadOnlyList<string> Do0To15 = Enumerable.Range(0, 16).Select(i => $"DO{i}").ToList();
        private static readonly IReadOnlyList<string> Di16To31 = Enumerable.Range(16, 16).Select(i => $"DI{i}").ToList();
        private static readonly IReadOnlyList<string> Do16To31 = Enumerable.Range(16, 16).Select(i => $"DO{i}").ToList();

        private static Dictionary<string, double> BuildDoMap(IEnumerable<string> channels, double value)
        {
            var list = channels?.ToList() ?? new List<string>();
            var map = new Dictionary<string, double>(capacity: list.Count, comparer: StringComparer.OrdinalIgnoreCase);
            foreach (var ch in list)
            {
                map[ch] = value;
            }
            return map;
        }

        private static string FormatBits(Dictionary<string, double> values, string prefix)
        {
            if (values == null) return string.Empty;

            var bits = new char[32];
            for (int i = 0; i < 32; i++)
            {
                var key = $"{prefix}{i}";
                bits[i] = (values.TryGetValue(key, out var v) && v != 0) ? '1' : '0';
            }

            return new string(bits);
        }

        private static string FormatBitsRange(Dictionary<string, double> values, string prefix, int start, int count)
        {
            if (values == null) return string.Empty;

            var bits = new char[count];
            for (int i = 0; i < count; i++)
            {
                int index = start + i;
                var key = $"{prefix}{index}";
                bits[i] = (values.TryGetValue(key, out var v) && v != 0) ? '1' : '0';
            }

            return new string(bits);
        }

        private static async Task RecordOnceAsync(IDeviceDriver driver, SelfInspectionContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var di = await driver.ReadChannelsBatchAsync(AllDiChannels);
            var doVals = await driver.ReadChannelsBatchAsync(AllDoChannels);

            context.Log($"DI值：{FormatBits(di, "DI")}");
            context.Log($"DO值：{FormatBits(doVals, "DO")}");
        }

        private static async Task RecordRangeOnceAsync(IDeviceDriver driver, SelfInspectionContext context, CancellationToken cancellationToken, int start, int count)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var diRange = Enumerable.Range(start, count).Select(i => $"DI{i}").ToList();
            var doRange = Enumerable.Range(start, count).Select(i => $"DO{i}").ToList();

            var di = await driver.ReadChannelsBatchAsync(diRange);
            var doVals = await driver.ReadChannelsBatchAsync(doRange);

            context.Log($"DI{start}~DI{start + count - 1}：{FormatBitsRange(di, "DI", start, count)}");
            context.Log($"DO{start}~DO{start + count - 1}：{FormatBitsRange(doVals, "DO", start, count)}");
        }

        private static async Task<bool> VerifyDiRangeAsync(
            IDeviceDriver driver,
            SelfInspectionContext context,
            CancellationToken cancellationToken,
            int start,
            int count,
            int expectedBit)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var diRange = Enumerable.Range(start, count).Select(i => $"DI{i}").ToList();
            var di = await driver.ReadChannelsBatchAsync(diRange);

            bool ok = true;
            for (int i = 0; i < count; i++)
            {
                int index = start + i;
                var key = $"DI{index}";
                var bit = (di.TryGetValue(key, out var v) && v != 0) ? 1 : 0;
                if (bit != expectedBit)
                {
                    ok = false;
                    break;
                }
            }

            context.Log($"DI{start}~DI{start + count - 1} 判定期望={expectedBit} => {(ok ? "OK" : "NG")}");
            return ok;
        }

        private static int GetSlotIndex(DeviceBase device)
        {
            if (device is PxiDeviceBase pxi)
            {
                return pxi.SlotIndex;
            }
            return -1;
        }

        private static bool Is7131(DeviceBase device)
        {
            var model = (device?.Model ?? string.Empty).ToUpperInvariant();
            return model.Contains("7131") || model.Contains("PXIE-7131");
        }

        private static void ApplyThresholds8Groups(double g0, double g1, double g2, double g3, double g4, double g5, double g6, double g7)
        {
            using var cli = new DacGroupsSerialClient(ThresholdComPort, ThresholdBaudRate, dtrEnable: false, rtsEnable: false);
            cli.Send8Groups(
                g0,
                g1,
                g2,
                g3,
                g4,
                g5,
                g6,
                g7);
        }

        private static Task ApplyPowerAsync(IDeviceDriver driver, double voltage)
        {
            if (driver is JY7131Driver jy)
            {
                return jy.EnsurePowerOutputsOrThrowAsync(voltage, voltage, voltage, voltage);
            }

            return Task.CompletedTask;
        }

        private static string GetMatrixOutputNodeIdForDoIndex(int doIndex)
        {
            // DO0 -> O32 ... DO31 -> O63
            return $"O{32 + doIndex}";
        }

        private static async Task<int[]> CaptureDiBits32Async(IDeviceDriver driver, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var di = await driver.ReadChannelsBatchAsync(AllDiChannels);
            var bits = new int[32];
            for (int i = 0; i < 32; i++)
            {
                var key = $"DI{i}";
                bits[i] = (di.TryGetValue(key, out var v) && v != 0) ? 1 : 0;
            }
            return bits;
        }

        private static bool AllEqual(int[] bits, int expected)
        {
            if (bits == null || bits.Length == 0) return false;
            for (int i = 0; i < bits.Length; i++)
            {
                if (bits[i] != expected) return false;
            }
            return true;
        }

        private static async Task<(MessageBasedSession Session, ResourceManager Rm)> OpenDmmAsync(SelfInspectionContext context)
        {
            var rm = new ResourceManager();
            var resource = $"TCPIP0::{DmmIpAddress}::inst0::INSTR";
            try
            {
                var session = (MessageBasedSession)rm.Open(resource);
                session.TimeoutMilliseconds = 2000;
                session.RawIO.Write("*CLS\n");
                session.RawIO.Write(":SYST:REM\n");
                try { session.RawIO.Write("CMDSet AGILENT\n"); } catch { }
                session.RawIO.Write(":CONF:VOLT:DC\n");
                try { session.RawIO.Write(":VOLT:DC:RANG 20\n"); } catch { }
                try { session.RawIO.Write(":VOLT:DC:RANG:AUTO 0\n"); } catch { }
                try { session.RawIO.Write(":VOLT:DC:NPLC 1\n"); } catch { }
                try { session.RawIO.Write(":TRIG:SOUR IMM\n"); } catch { }
                try { session.RawIO.Write(":SAMP:COUN 1\n"); } catch { }
                try
                {
                    session.RawIO.Write("*IDN?\n");
                    var idn = session.RawIO.ReadString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(idn))
                    {
                        context.Log($"DMM识别: {idn}");
                    }
                }
                catch { }
                context.Log($"DMM连接成功: {resource}");
                return (session, rm);
            }
            catch (Exception ex)
            {
                try { rm.Dispose(); } catch { }
                context.Log($"DMM连接失败: {resource}, {ex.Message}");
                throw;
            }
        }

        private static double QueryDmmVoltage(MessageBasedSession session)
        {
            session.RawIO.Write(":MEAS:VOLT:DC?\n");
            var resp = session.RawIO.ReadString();
            if (double.TryParse(resp?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            {
                return v;
            }
            if (double.TryParse(resp?.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out v))
            {
                return v;
            }
            return double.NaN;
        }

        private static async Task<(bool Ok, double[] Voltages)> MeasureDoVoltagesWithResultAsync(
            SelfInspectionContext context,
            MessageBasedSession dmmSession,
            IEnumerable<int> doIndices,
            double expectedVoltage,
            double tolerance,
            CancellationToken cancellationToken)
        {
            bool allOk = true;
            var results = new double[32];

            var svc = MatrixControlService.Instance;
            bool commonConnected = false;
            string prevSelectOutput = null;

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                commonConnected = await svc.ConnectNodesAsync(MatrixCommonInputNodeId, MatrixCommonOutputNodeId, MatrixCommonSlotIndex, MatrixIpAddress);
                if (!commonConnected)
                {
                    context.Log($"矩阵公共链路连接失败: {MatrixCommonInputNodeId}->{MatrixCommonOutputNodeId} slot={MatrixCommonSlotIndex}");
                    return (false, results);
                }

                foreach (var doIndex in doIndices)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var curOutput = GetMatrixOutputNodeIdForDoIndex(doIndex);

                    if (prevSelectOutput != null && !string.Equals(prevSelectOutput, curOutput, StringComparison.OrdinalIgnoreCase))
                    {
                        await svc.DisconnectNodesAsync(MatrixSelectInputNodeId, prevSelectOutput, MatrixSelectSlotIndex, MatrixIpAddress, Matrix3022TcpBasePort);
                    }

                    var selOk = await svc.ConnectNodesAsync(MatrixSelectInputNodeId, curOutput, MatrixSelectSlotIndex, MatrixIpAddress, Matrix3022TcpBasePort);
                    if (!selOk)
                    {
                        allOk = false;
                        context.Log($"DO{doIndex}: 矩阵选择链路连接失败 {MatrixSelectInputNodeId}->{curOutput} slot={MatrixSelectSlotIndex}");
                        prevSelectOutput = curOutput;
                        continue;
                    }

                    prevSelectOutput = curOutput;
                    await Task.Delay(DmmStabilizeDelayMs, cancellationToken);

                    var measured = QueryDmmVoltage(dmmSession);
                    if (doIndex >= 0 && doIndex < results.Length) results[doIndex] = measured;
                    var low = expectedVoltage - tolerance;
                    var high = expectedVoltage + tolerance;
                    var ok = !double.IsNaN(measured) && measured >= low && measured <= high;
                    if (!ok) allOk = false;

                    context.Log($"DO{doIndex} 电压: {measured:F4}V, 期望={expectedVoltage:F3}V, 范围=[{low:F3},{high:F3}] => {(ok ? "OK" : "NG")}");
                }

                return (allOk, results);
            }
            finally
            {
                try
                {
                    if (prevSelectOutput != null)
                    {
                        await svc.DisconnectNodesAsync(MatrixSelectInputNodeId, prevSelectOutput, MatrixSelectSlotIndex, MatrixIpAddress, Matrix3022TcpBasePort);
                    }
                }
                catch
                {
                }

                try
                {
                    if (commonConnected)
                    {
                        await svc.DisconnectNodesAsync(MatrixCommonInputNodeId, MatrixCommonOutputNodeId, MatrixCommonSlotIndex, MatrixIpAddress);
                    }
                }
                catch
                {
                }
            }
        }

        private static async Task MeasureDoVoltagesAsync(
            SelfInspectionContext context,
            MessageBasedSession dmmSession,
            IEnumerable<int> doIndices,
            double expectedVoltage,
            double tolerance,
            CancellationToken cancellationToken)
        {
            var svc = MatrixControlService.Instance;
            bool commonConnected = false;
            string prevSelectOutput = null;

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                commonConnected = await svc.ConnectNodesAsync(MatrixCommonInputNodeId, MatrixCommonOutputNodeId, MatrixCommonSlotIndex, MatrixIpAddress);
                if (!commonConnected)
                {
                    throw new InvalidOperationException($"矩阵公共链路连接失败: {MatrixCommonInputNodeId}->{MatrixCommonOutputNodeId} slot={MatrixCommonSlotIndex}");
                }

                foreach (var doIndex in doIndices)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var curOutput = GetMatrixOutputNodeIdForDoIndex(doIndex);

                    if (prevSelectOutput != null && !string.Equals(prevSelectOutput, curOutput, StringComparison.OrdinalIgnoreCase))
                    {
                        await svc.DisconnectNodesAsync(MatrixSelectInputNodeId, prevSelectOutput, MatrixSelectSlotIndex, MatrixIpAddress, Matrix3022TcpBasePort);
                    }

                    var selOk = await svc.ConnectNodesAsync(MatrixSelectInputNodeId, curOutput, MatrixSelectSlotIndex, MatrixIpAddress, Matrix3022TcpBasePort);
                    if (!selOk)
                    {
                        context.Log($"DO{doIndex}: 矩阵选择链路连接失败 {MatrixSelectInputNodeId}->{curOutput} slot={MatrixSelectSlotIndex}");
                        prevSelectOutput = curOutput;
                        continue;
                    }

                    prevSelectOutput = curOutput;
                    await Task.Delay(DmmStabilizeDelayMs, cancellationToken);
                    var measured = QueryDmmVoltage(dmmSession);
                    var low = expectedVoltage - tolerance;
                    var high = expectedVoltage + tolerance;
                    var ok = !double.IsNaN(measured) && measured >= low && measured <= high;
                    context.Log($"DO{doIndex} 电压: {measured:F4}V, 期望={expectedVoltage:F3}V, 范围=[{low:F3},{high:F3}] => {(ok ? "OK" : "NG")}");
                }
            }
            finally
            {
                try
                {
                    if (prevSelectOutput != null)
                    {
                        await svc.DisconnectNodesAsync(MatrixSelectInputNodeId, prevSelectOutput, MatrixSelectSlotIndex, MatrixIpAddress, Matrix3022TcpBasePort);
                    }
                }
                catch
                {
                }

                try
                {
                    if (commonConnected)
                    {
                        await svc.DisconnectNodesAsync(MatrixCommonInputNodeId, MatrixCommonOutputNodeId, MatrixCommonSlotIndex, MatrixIpAddress);
                    }
                }
                catch
                {
                }
            }
        }

        private static double ConvertDIThresholdActualToBoardParam(double actualV)
        {
            var v = actualV / 3.0;
            if (v < -10.0) v = -10.0;
            else if (v > 10.0) v = 10.0;
            return Math.Round(v, 2, MidpointRounding.AwayFromZero);
        }

        public bool CanHandle(DeviceBase device)
        {
            return device != null && Is7131(device);
        }

        public async Task RunAsync(DeviceBase device, SelfInspectionContext context, CancellationToken cancellationToken)
        {
            if (device == null) throw new ArgumentNullException(nameof(device));
            if (context == null) throw new ArgumentNullException(nameof(context));

            if (!Is7131(device))
            {
                context.Log($"跳过：非7131板卡 {device.Name} Model={device.Model}");
                return;
            }

            var slotIndex = GetSlotIndex(device);
            var cached = DriverFactory.GetCachedDriver(device.Id, slotIndex);
            if (cached != null && cached.IsConnected)
            {
                context.Log("检测到板卡已连接，取消自检以避免影响面板。");
                throw new InvalidOperationException("板卡已连接，无法自检。");
            }

            var driver = DriverFactory.CreateDriver(device);
            if (driver == null)
            {
                context.Log($"未找到驱动，无法自检：{device.Name} Model={device.Model}");
                return;
            }

            bool acquisitionStarted = false;
            bool connected = false;

            bool finalOk = true;

            MessageBasedSession dmmSession = null;
            ResourceManager dmmRm = null;

            string reportPath = null;

            var stopwatch = Stopwatch.StartNew();

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                context.Log("连接板卡 PXIe-7131");
                var connectOk = await driver.ConnectAsync();
                connected = connectOk && driver.IsConnected;
                if (!connected)
                {
                    context.Log("连接板卡 PXIe-7131 失败");
                    throw new InvalidOperationException("连接板卡 PXIe-7131 失败");
                }
                context.Log("连接板卡 PXIe-7131 成功");

                context.Log("初始化 PXIe-7131 参数");
                try
                {
                    var init0To3 = ConvertDIThresholdActualToBoardParam(InitialThresholdGroup0To3ActualV);
                    var init4To7 = ConvertDIThresholdActualToBoardParam(InitialThresholdGroup4To7ActualV);
                    using (await SerialPortMutex.AcquireAsync(ThresholdComPort))
                    {
                        ApplyThresholds8Groups(
                            init0To3,
                            init0To3,
                            init0To3,
                            init0To3,
                            init4To7,
                            init4To7,
                            init4To7,
                            init4To7);
                    }

                    context.Log("设置输入阈值 成功");
                }
                catch (Exception ex)
                {
                    context.Log($"设置输入阈值失败：{ex.Message}");
                    finalOk = false;
                }

                try
                {
                    (dmmSession, dmmRm) = await OpenDmmAsync(context);
                }
                catch
                {
                    finalOk = false;
                    throw;
                }

                context.Log("开始采集和输出");
                var started = await driver.StartAcquisitionAsync();
                if (!started)
                {
                    context.Log("开始采集和输出 失败");
                    finalOk = false;
                    throw new InvalidOperationException("开始采集和输出失败");
                }

                acquisitionStarted = true;
                context.Log("开始采集和输出 成功");

                if (driver is JY7131Driver jy30)
                {
                    context.Log("设置DO输出模式：Sinking");
                    var modeOk = await jy30.ReconfigureDoOutputModeAsync("Sinking");
                    if (!modeOk)
                    {
                        finalOk = false;
                        throw new InvalidOperationException("设置DO输出模式失败");
                    }
                }

                reportPath = context.ReportPath;
                if (string.IsNullOrWhiteSpace(reportPath))
                {
                    var projectDir = System.IO.Path.GetDirectoryName(context.LogFilePath) ?? string.Empty;
                    var projectNameFromLog = System.IO.Path.GetFileNameWithoutExtension(context.LogFilePath) ?? string.Empty;
                    // expected: {project}_{chassis}_自检
                    var token = $"_{context.ChassisName}_自检";
                    if (projectNameFromLog.EndsWith(token, StringComparison.OrdinalIgnoreCase))
                    {
                        projectNameFromLog = projectNameFromLog.Substring(0, projectNameFromLog.Length - token.Length);
                    }
                    if (string.IsNullOrWhiteSpace(projectNameFromLog)) projectNameFromLog = "项目";

                    var templatePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ReportTemplateRelativePath);
                    if (!System.IO.File.Exists(templatePath))
                    {
                        // fallback: walk up to find Projects\自检报表模板.xlsx
                        var probe = AppDomain.CurrentDomain.BaseDirectory;
                        for (int i = 0; i < 6 && !string.IsNullOrWhiteSpace(probe); i++)
                        {
                            var candidate = System.IO.Path.Combine(probe, ReportTemplateRelativePath);
                            if (System.IO.File.Exists(candidate))
                            {
                                templatePath = candidate;
                                break;
                            }
                            var parent = System.IO.Directory.GetParent(probe);
                            probe = parent?.FullName;
                        }
                    }
                    var reportName = $"{projectNameFromLog}_{context.ChassisName}_自检_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
                    reportPath = SelfInspectionReportWriter.CreateReportFromTemplate(templatePath, projectDir, reportName);
                    context.ReportPath = reportPath;
                    context.Log($"生成自检报表: {reportPath}");
                }

                async Task<(bool DoOk, bool DiOk, double[] DoVoltages, int[] DiBits)> RunScenarioAsync(double diThresholdActualV, double doOutputVoltage, double dmmExpectedVoltage, int expectedDiBit)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var boardParam = ConvertDIThresholdActualToBoardParam(diThresholdActualV);
                    context.Log($"设置全部DI阈值: {diThresholdActualV:F3}V => 下发阈值={boardParam:F2}V");
                    using (await SerialPortMutex.AcquireAsync(ThresholdComPort))
                    {
                        ApplyThresholds8Groups(boardParam, boardParam, boardParam, boardParam, boardParam, boardParam, boardParam, boardParam);
                    }

                    context.Log($"设置所有DO输出电压: {doOutputVoltage:F3}V");
                    await ApplyPowerAsync(driver, doOutputVoltage);

                    context.Log("所有DO置1");
                    var doOk = await driver.WriteChannelsBatchAsync(BuildDoMap(AllDoChannels, 1));
                    if (!doOk) throw new InvalidOperationException("写入所有DO=1失败");
                    await Task.Delay(150, cancellationToken);

                    var diBits = await CaptureDiBits32Async(driver, cancellationToken);
                    context.Log($"DI状态(32): {JsonConvert.SerializeObject(diBits)}");

                    context.Log("万用表回采32路DO输出电压");
                    var (vOk, voltages) = await MeasureDoVoltagesWithResultAsync(
                        context,
                        dmmSession,
                        Enumerable.Range(0, 32),
                        dmmExpectedVoltage,
                        ScenarioToleranceV,
                        cancellationToken);

                    var diOk = AllEqual(diBits, expectedDiBit);
                    context.Log($"工况判定: DO回采期望={dmmExpectedVoltage:F3}±{ScenarioToleranceV:F1} => {(vOk ? "合格" : "不合格")}, DI全={expectedDiBit} => {(diOk ? "合格" : "不合格")}");
                    return (vOk, diOk, voltages, diBits);
                }

                var cells = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                // Scenario 1: threshold=30V, DO=32V, expect DI=0
                var s1 = await RunScenarioAsync(ScenarioThresholdHighV, ScenarioDo32V, dmmExpectedVoltage: ScenarioDo32V, expectedDiBit: 0);
                cells["E6"] = JsonConvert.SerializeObject(s1.DoVoltages);
                cells["E7"] = JsonConvert.SerializeObject(s1.DiBits);
                cells["F6"] = s1.DoOk ? "合格" : "不合格";
                cells["F7"] = s1.DiOk ? "合格" : "不合格";
                if (!(s1.DoOk && s1.DiOk)) finalOk = false;

                // Scenario 2: threshold=30V, DO=29V, expect DI=1
                var s2 = await RunScenarioAsync(ScenarioThresholdHighV, ScenarioDo29V, dmmExpectedVoltage: ScenarioDo29V, expectedDiBit: 1);
                cells["E8"] = JsonConvert.SerializeObject(s2.DoVoltages);
                cells["E9"] = JsonConvert.SerializeObject(s2.DiBits);
                cells["F8"] = s2.DoOk ? "合格" : "不合格";
                cells["F9"] = s2.DiOk ? "合格" : "不合格";
                if (!(s2.DoOk && s2.DiOk)) finalOk = false;

                // Scenario 3: threshold=2.8V, DO=3.3V, expect DI=0
                var s3 = await RunScenarioAsync(ScenarioThresholdLowV, ScenarioDo33V, dmmExpectedVoltage: ScenarioDo33V, expectedDiBit: 0);
                cells["E10"] = JsonConvert.SerializeObject(s3.DoVoltages);
                cells["E11"] = JsonConvert.SerializeObject(s3.DiBits);
                cells["F10"] = s3.DoOk ? "合格" : "不合格";
                cells["F11"] = s3.DiOk ? "合格" : "不合格";
                if (!(s3.DoOk && s3.DiOk)) finalOk = false;

                // Scenario 4: threshold=2.8V, DO=2.3V, expect DI=1
                var s4 = await RunScenarioAsync(ScenarioThresholdLowV, ScenarioDo23V, dmmExpectedVoltage: ScenarioDo23V, expectedDiBit: 1);
                cells["E12"] = JsonConvert.SerializeObject(s4.DoVoltages);
                cells["E13"] = JsonConvert.SerializeObject(s4.DiBits);
                cells["F12"] = s4.DoOk ? "合格" : "不合格";
                cells["F13"] = s4.DiOk ? "合格" : "不合格";
                if (!(s4.DoOk && s4.DiOk)) finalOk = false;

                SelfInspectionReportWriter.WriteCells(reportPath, cells);
                context.Log("自检报表已写入");
            }
            finally
            {
                try
                {
                    context.Log(finalOk ? "自检结束：测试合格" : "自检结束：测试不合格");
                }
                catch
                {
                }

                try
                {
                    var elapsed = stopwatch.Elapsed;
                    context.Log($"自检耗时(PXIe-7131)：{(int)elapsed.TotalMinutes}分{elapsed.Seconds}秒{elapsed.Milliseconds:D3}毫秒");
                }
                catch
                {
                }

                if (connected)
                {
                    try
                    {
                        context.Log("停止采集和输出");
                        if (driver.IsConnected)
                        {
                            try { await driver.WriteChannelsBatchAsync(BuildDoMap(AllDoChannels, 0)); } catch { }
                            if (acquisitionStarted)
                            {
                                try { await driver.StopAcquisitionAsync(); } catch { }
                            }

                            if (driver is JY7131Driver jy)
                            {
                                try { await jy.StopPowerOutputAsync(); } catch { }
                            }
                        }
                    }
                    catch
                    {
                    }
                }

                try { dmmSession?.RawIO.Write(":VOLT:DC:RANG:AUTO 1\n"); } catch { }

                VisaSessionUtilities.SafeDisposeInstrumentSession(dmmSession, dmmRm, exitRemote: false);

                if (connected)
                {
                    try
                    {
                        context.Log("断开板卡 PXIe-7131");
                        if (driver.IsConnected)
                        {
                            try { await driver.DisconnectAsync(); } catch { }
                        }
                    }
                    catch
                    {
                    }
                }
            }
        }
    }
}
