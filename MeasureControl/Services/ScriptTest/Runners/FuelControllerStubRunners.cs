using MeasureControl.Services;
using MeasureControl.Services.ScriptTest.Judgements;
using MeasureControl.Services.ScriptTest.Models;
using MeasureControl.ViewModels.SingleBoardTest.FuelController;
using Prism.Ioc;
using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MeasureControl.Services.ScriptTest.Runners
{
    // ─── FC1: 电源阻抗测试（锁死判据，RunOnceAsync后读取ImpedanceA-D回填输出值） ────
    internal sealed class Fc1StubRunner : IFcRunner
    {
        public string TestId => "FC1";

        public async Task<FcRunResult> RunAsync(FcGroup group, RunFcContext ctx)
        {
            ctx.Log($"[{TestId}] 电源阻抗测试 开始");
            FcRunnerHelpers.ClearBoardPowerState(ctx);
            var vm = ContainerLocator.Container.Resolve<PowerImpedanceTestViewModel>();
            await vm.RunOnceAsync(ctx.CancellationToken).ConfigureAwait(false);

            double?[] vals = { vm.ImpedanceA, vm.ImpedanceB, vm.ImpedanceC, vm.ImpedanceD };
            return FcRunnerHelpers.ApplyJudgements(TestId, group, vals, ctx);
        }
    }

    // ─── FC2: 二次电源测试（参数化供电电压 + RANGE判据） ────────────────────────────
    internal sealed class Fc2StubRunner : IFcRunner
    {
        public string TestId => "FC2";

        public async Task<FcRunResult> RunAsync(FcGroup group, RunFcContext ctx)
        {
            ctx.Log($"[{TestId}] 二次电源测试 开始");
            var row = group.Rows[0];
            if (!FcRunnerHelpers.TryParseDouble(row.InputValueRaw, out double voltage))
                throw new InvalidOperationException($"[{TestId}] POWER_IN 输入值无效: '{row.InputValueRaw}'");

            ctx.Log($"[{TestId}]   供电电压: {voltage}V");
            FcRunnerHelpers.ClearBoardPowerState(ctx);
            var vm = ContainerLocator.Container.Resolve<SecondaryPowerTestViewModel>();
            double? measured = await vm.RunWithScriptVoltageAsync(voltage, ctx.CancellationToken).ConfigureAwait(false);
            return FcRunnerHelpers.ApplyJudgements(TestId, group, new[] { measured }, ctx);
        }
    }

    // ─── FC3: 低电压告警功能测试（锁死判据，RunOnceAsync后读取FlipVoltage回填） ──────
    internal sealed class Fc3StubRunner : IFcRunner
    {
        public string TestId => "FC3";

        public async Task<FcRunResult> RunAsync(FcGroup group, RunFcContext ctx)
        {
            ctx.Log($"[{TestId}] 低电压告警功能测试 开始");
            FcRunnerHelpers.ClearBoardPowerState(ctx);
            var vm = ContainerLocator.Container.Resolve<LowVoltageAlarmTestViewModel>();
            await vm.RunOnceAsync(ctx.CancellationToken).ConfigureAwait(false);
            return FcRunnerHelpers.ApplyJudgements(TestId, group, new[] { vm.FlipVoltage }, ctx);
        }
    }

    // ─── FC4: 温度采集功能测试（参数化供电电压 + RANGE判据） ────────────────────────
    internal sealed class Fc4StubRunner : IFcRunner
    {
        public string TestId => "FC4";

        public async Task<FcRunResult> RunAsync(FcGroup group, RunFcContext ctx)
        {
            ctx.Log($"[{TestId}] 温度采集功能测试 开始");
            var row = group.Rows[0];
            if (!FcRunnerHelpers.TryParseDouble(row.InputValueRaw, out double voltage))
                throw new InvalidOperationException($"[{TestId}] POWER_IN 输入值无效: '{row.InputValueRaw}'");

            ctx.Log($"[{TestId}]   供电电压: {voltage}V");
            FcRunnerHelpers.ClearBoardPowerState(ctx);
            var vm = ContainerLocator.Container.Resolve<TemperatureAcquisitionTestViewModel>();
            double? measured = await vm.RunWithScriptVoltageAsync(voltage, ctx.CancellationToken).ConfigureAwait(false);
            return FcRunnerHelpers.ApplyJudgements(TestId, group, new[] { measured }, ctx);
        }
    }

    // ─── FC5: 离散量采集功能测试（参数化供电电压，28行：14接地+14开路，EQ BOOL判据） ──
    internal sealed class Fc5StubRunner : IFcRunner
    {
        public string TestId => "FC5";

        public async Task<FcRunResult> RunAsync(FcGroup group, RunFcContext ctx)
        {
            ctx.Log($"[{TestId}] 离散量采集功能测试 开始");
            var powerRow = group.Rows.FirstOrDefault(r =>
                string.Equals(r.InputSignal, "POWER_IN", StringComparison.OrdinalIgnoreCase));
            if (powerRow == null || !FcRunnerHelpers.TryParseDouble(powerRow.InputValueRaw, out double voltage))
                throw new InvalidOperationException($"[{TestId}] 找不到有效的 POWER_IN 电压");

            ctx.Log($"[{TestId}]   供电电压: {voltage}V");
            FcRunnerHelpers.ClearBoardPowerState(ctx);
            var vm = ContainerLocator.Container.Resolve<DiscreteInputTestViewModel>();
            await vm.RunWithScriptVoltageAsync(voltage, ctx.CancellationToken).ConfigureAwait(false);

            int[] grounded = vm.GroundedChannelResults;
            int[] open     = vm.OpenChannelResults;
            int n = grounded?.Length ?? 0;
            bool allPass = true;

            for (int i = 0; i < group.Rows.Count; i++)
            {
                var row = group.Rows[i];
                bool isOpen = i >= n;
                int idx = isOpen ? i - n : i;
                int[] arr = isOpen ? open : grounded;
                int val = (arr != null && idx < arr.Length) ? arr[idx] : -1;

                if (val >= 0 && JudgementRegistry.TryGet(row.JudgementType, out var judge))
                {
                    var r = judge.Evaluate(row.JudgementParamRaw, (double)val);
                    row.OutputValue = r.ActualText;
                    row.Pass = r.Pass;
                }
                else
                {
                    row.OutputValue = val >= 0 ? val.ToString() : "--";
                    row.Pass = false;
                }
                if (row.Pass != true) allPass = false;
                ctx.Log($"[{TestId}]   [{row.OutputSignal}] val={row.OutputValue} {(row.Pass == true ? "PASS" : "FAIL")}");
            }
            return FcRunnerHelpers.MakeResult(TestId, allPass);
        }
    }

    // ─── FC6: 离散量输出功能测试（步骤A/B锁死阻抗 + 步骤C参数化电压） ────────────────
    internal sealed class Fc6StubRunner : IFcRunner
    {
        public string TestId => "FC6";

        public async Task<FcRunResult> RunAsync(FcGroup group, RunFcContext ctx)
        {
            ctx.Log($"[{TestId}] 离散量输出功能测试 开始");
            FcRunnerHelpers.ClearBoardPowerState(ctx);
            var vm = ContainerLocator.Container.Resolve<DiscreteOutputTestViewModel>();

            var powerRow = group.Rows.LastOrDefault(r =>
                string.Equals(r.InputSignal, "POWER_IN", StringComparison.OrdinalIgnoreCase));
            if (powerRow != null && FcRunnerHelpers.TryParseDouble(powerRow.InputValueRaw, out double voltage))
            {
                vm.SelectedSupplyVoltage = voltage;
                ctx.Log($"[{TestId}]   步骤C供电电压: {voltage}V");
            }

            await vm.RunOnceAsync(ctx.CancellationToken).ConfigureAwait(false);

            double?[] groundedImpedances =
            {
                vm.ImpedanceJ6,     vm.ImpedanceJ7,     vm.ImpedanceJ8,
                vm.ImpedanceJ9,     vm.ImpedanceJ10,    vm.ImpedanceJ11,    vm.ImpedanceJ12
            };
            double?[] openImpedances =
            {
                vm.ImpedanceOpenJ6, vm.ImpedanceOpenJ7, vm.ImpedanceOpenJ8,
                vm.ImpedanceOpenJ9, vm.ImpedanceOpenJ10,vm.ImpedanceOpenJ11,vm.ImpedanceOpenJ12
            };

            bool allPass = true;
            for (int i = 0; i < group.Rows.Count; i++)
            {
                var row = group.Rows[i];
                double? val;
                if      (i < 7)  val = groundedImpedances[i];
                else if (i < 14) val = openImpedances[i - 7];
                else             val = vm.J14Voltage;

                if (val.HasValue && JudgementRegistry.TryGet(row.JudgementType, out var judge))
                {
                    var r = judge.Evaluate(row.JudgementParamRaw, val.Value);
                    row.OutputValue = r.ActualText;
                    row.Pass = r.Pass;
                }
                else
                {
                    row.OutputValue = val.HasValue ? val.Value.ToString("0.##") : "--";
                    row.Pass = false;
                }
                if (row.Pass != true) allPass = false;
                ctx.Log($"[{TestId}]   [{row.OutputSignal}] 实测={row.OutputValue} {(row.Pass == true ? "PASS" : "FAIL")}");
            }
            return FcRunnerHelpers.MakeResult(TestId, allPass);
        }
    }

    // ─── FC7: RS422通信功能测试（RunOnceAsync后读StepA-DRxData，EQ HEX判据） ─────────
    internal sealed class Fc7StubRunner : IFcRunner
    {
        public string TestId => "FC7";

        public async Task<FcRunResult> RunAsync(FcGroup group, RunFcContext ctx)
        {
            ctx.Log($"[{TestId}] RS422通信功能测试 开始（28V固定电压）");
            FcRunnerHelpers.ClearBoardPowerState(ctx);
            var vm = ContainerLocator.Container.Resolve<RS422CommunicationFunctionTestViewModel>();

            // 每行独立解析 InputValueRaw 作为该步骤的 TX 字节
            byte[][] txPerStep = group.Rows
                .Select(r => FcRunnerHelpers.ParseTxBytes(r.InputValueRaw))
                .ToArray();
            for (int i = 0; i < txPerStep.Length; i++)
                ctx.Log($"[{TestId}]   步骤{(char)('A' + i)} TX: {(txPerStep[i] != null ? "0x" + BitConverter.ToString(txPerStep[i]).Replace("-", "") : "DefaultTxData")}");
            await vm.RunWithScriptPerStepTxDataAsync(txPerStep, ctx.CancellationToken).ConfigureAwait(false);

            string[] rxByStep = { vm.StepARxData, vm.StepBRxData, vm.StepCRxData, vm.StepDRxData };
            return FcRunnerHelpers.ApplyHexJudgements(TestId, group, rxByStep, ctx);
        }
    }

    // ─── FC8: RS422自检测功能测试（RunOnceAsync后读StepA-BRxData，EQ HEX判据） ─────────
    internal sealed class Fc8StubRunner : IFcRunner
    {
        public string TestId => "FC8";

        public async Task<FcRunResult> RunAsync(FcGroup group, RunFcContext ctx)
        {
            ctx.Log($"[{TestId}] RS422自检测功能测试 开始（28V固定电压）");
            FcRunnerHelpers.ClearBoardPowerState(ctx);
            var vm = ContainerLocator.Container.Resolve<RS422SelfCheckTestViewModel>();

            // 每行独立解析 InputValueRaw 作为该步骤的 TX 字节
            byte[][] txPerStep = group.Rows
                .Select(r => FcRunnerHelpers.ParseTxBytes(r.InputValueRaw))
                .ToArray();
            for (int i = 0; i < txPerStep.Length; i++)
                ctx.Log($"[{TestId}]   步骤{(char)('A' + i)} TX: {(txPerStep[i] != null ? "0x" + BitConverter.ToString(txPerStep[i]).Replace("-", "") : "DefaultTxData")}");
            await vm.RunWithScriptPerStepTxDataAsync(txPerStep, ctx.CancellationToken).ConfigureAwait(false);

            string[] rxByStep = { vm.StepARxData, vm.StepBRxData };
            return FcRunnerHelpers.ApplyHexJudgements(TestId, group, rxByStep, ctx);
        }
    }

    // ─── 共享辅助方法 ─────────────────────────────────────────────────────────────────
    internal static class FcRunnerHelpers
    {
        internal static FcRunResult ApplyJudgements(string testId, FcGroup group, double?[] vals, RunFcContext ctx)
        {
            bool allPass = true;
            for (int i = 0; i < group.Rows.Count; i++)
            {
                var row = group.Rows[i];
                double? val = i < vals.Length ? vals[i] : null;

                if (val.HasValue && JudgementRegistry.TryGet(row.JudgementType, out var judge))
                {
                    var r = judge.Evaluate(row.JudgementParamRaw, val.Value);
                    row.OutputValue = r.ActualText;
                    row.Pass = r.Pass;
                }
                else
                {
                    row.OutputValue = val.HasValue ? val.Value.ToString("0.##") : "--";
                    row.Pass = false;
                }
                if (row.Pass != true) allPass = false;
                ctx.Log($"[{testId}]   [{row.OutputSignal}] 实测={row.OutputValue} {(row.Pass == true ? "PASS" : "FAIL")}");
            }
            return MakeResult(testId, allPass);
        }

        internal static FcRunResult ApplyHexJudgements(string testId, FcGroup group, string[] rxByStep, RunFcContext ctx)
        {
            bool allPass = true;
            for (int i = 0; i < group.Rows.Count && i < rxByStep.Length; i++)
            {
                var row = group.Rows[i];
                string rxHex = rxByStep[i];
                long? rxLong = ParseRxHex(rxHex);

                if (rxLong.HasValue && JudgementRegistry.TryGet(row.JudgementType, out var judge))
                {
                    var r = judge.Evaluate(row.JudgementParamRaw, rxLong.Value);
                    row.OutputValue = rxHex;
                    row.Pass = r.Pass;
                }
                else
                {
                    row.OutputValue = rxHex ?? "--";
                    row.Pass = false;
                }
                if (row.Pass != true) allPass = false;
                ctx.Log($"[{testId}]   [步骤{(char)('A' + i)}] RX={row.OutputValue} {(row.Pass == true ? "PASS" : "FAIL")}");
            }
            return MakeResult(testId, allPass);
        }

        internal static FcRunResult MakeResult(string testId, bool allPass) =>
            new FcRunResult { TestId = testId, Status = allPass ? FcResultStatus.Pass : FcResultStatus.Fail };

        internal static long? ParseRxHex(string rxData)
        {
            if (string.IsNullOrEmpty(rxData) || rxData == "--") return null;
            string clean = rxData.ToLowerInvariant().Replace("0x", "").Replace(" ", "");
            return long.TryParse(clean, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long v)
                ? v : (long?)null;
        }

        internal static bool TryParseDouble(string raw, out double value) =>
            double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out value);

        /// <summary>
        /// 解析十六进制字符串为字节数组。支持格式: "0xAA55", "AA55", "0xAA 55", "AA 55"。
        /// 输入为 null/空/"--" 时返回 null（Runner 会退化为 VM 默认 DefaultTxData）。
        /// </summary>
        internal static byte[] ParseTxBytes(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw) || raw.Trim() == "--") return null;
            string clean = raw.ToLowerInvariant()
                .Replace("0x", "")
                .Replace(" ", "")
                .Replace("-", "");
            if (clean.Length == 0 || clean.Length % 2 != 0) return null;
            try
            {
                var bytes = new byte[clean.Length / 2];
                for (int i = 0; i < bytes.Length; i++)
                    bytes[i] = Convert.ToByte(clean.Substring(i * 2, 2), 16);
                return bytes;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 在调用 VM 之前清除 IBoardPowerService 的外部上电状态。
        /// 确保 VM.InitializeHardwareAsync 中 fuelAlreadyPowered=false，
        /// 使 _powerManagedExternally=false，从而让 VM 自行管理上电和下电全流程。
        /// </summary>
        internal static void ClearBoardPowerState(RunFcContext ctx)
        {
            try
            {
                ContainerLocator.Container.Resolve<IBoardPowerService>()?.SetPoweredState(false);
            }
            catch (Exception ex)
            {
                ctx.Log($"  [ClearBoardPowerState 异常: {ex.Message}]");
            }
        }
    }
}
