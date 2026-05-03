// ============================================================================
// 液压控制器脚本测试 Runner 集合（HC1 ~ HC10）
// ============================================================================
using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using MeasureControl.Services.ScriptTest.Judgements;
using MeasureControl.Services.ScriptTest.Models;
using MeasureControl.ViewModels.SingleBoardTest.HydraulicController;
using Prism.Ioc;

namespace MeasureControl.Services.ScriptTest.Runners
{
    // ─── HC1: 电源阻抗测试 (2 行) ─────────────────────────────────────────────────
    public sealed class Hc1Runner : IFcRunner
    {
        public string TestId => "HC1";

        public async Task<FcRunResult> RunAsync(FcGroup group, RunFcContext ctx)
        {
            ctx.Log($"[{TestId}] 电源阻抗测试 开始");
            var vm = ContainerLocator.Container.Resolve<HC_6_1ViewModel>();
            await vm.RunOnceAsync(ctx.CancellationToken).ConfigureAwait(false);

            return HcRunnerHelpers.ApplyJudgements(TestId, group,
                new double?[] { vm.Resistance14Value, vm.Resistance182Value }, ctx);
        }
    }

    // ─── HC2: 通道ID测试 (2 行, Hex EQ) ────────────────────────────────────────────
    public sealed class Hc2Runner : IFcRunner
    {
        public string TestId => "HC2";

        public async Task<FcRunResult> RunAsync(FcGroup group, RunFcContext ctx)
        {
            ctx.Log($"[{TestId}] 通道ID测试 开始");
            var vm = ContainerLocator.Container.Resolve<HC_6_2ViewModel>();
            await vm.RunOnceAsync(ctx.CancellationToken).ConfigureAwait(false);

            return HcRunnerHelpers.ApplyHexJudgements(TestId, group,
                new[] { vm.Resistance14Text, vm.Resistance182Text }, ctx);
        }
    }

    // ─── HC3: 二次电源测试 (3 行, RANGE) ──────────────────────────────────────────
    public sealed class Hc3Runner : IFcRunner
    {
        public string TestId => "HC3";

        public async Task<FcRunResult> RunAsync(FcGroup group, RunFcContext ctx)
        {
            ctx.Log($"[{TestId}] 二次电源测试 开始");
            var vm = ContainerLocator.Container.Resolve<HC_6_3ViewModel>();
            await vm.RunOnceAsync(ctx.CancellationToken).ConfigureAwait(false);

            return HcRunnerHelpers.ApplyJudgements(TestId, group,
                new double?[] { vm.Voltage5VValue, vm.Voltage15VValue, vm.VoltageM15VValue }, ctx);
        }
    }

    // ─── HC4: 温度采集测试 (2 行, RANGE) ──────────────────────────────────────────
    public sealed class Hc4Runner : IFcRunner
    {
        public string TestId => "HC4";

        public async Task<FcRunResult> RunAsync(FcGroup group, RunFcContext ctx)
        {
            ctx.Log($"[{TestId}] 温度采集测试 开始");
            var res2 = HcRunnerHelpers.ParseInputDouble(group.Rows[0].InputValueRaw) ?? 763.2;
            var res3 = HcRunnerHelpers.ParseInputDouble(group.Rows[1].InputValueRaw) ?? 763.2;
            ctx.Log($"[{TestId}]   RTD2={res2:0.#}Ω  RTD3={res3:0.#}Ω");

            var vm = ContainerLocator.Container.Resolve<HC_6_4ViewModel>();
            await vm.RunWithScriptResistancesAsync(res2, res3, ctx.CancellationToken).ConfigureAwait(false);

            return HcRunnerHelpers.ApplyJudgements(TestId, group,
                new double?[] { vm.ScriptTemp2Value, vm.ScriptTemp3Value }, ctx);
        }
    }

    // ─── HC5: 压力传感器信号采集测试 (3 行, RANGE) ────────────────────────────────
    public sealed class Hc5Runner : IFcRunner
    {
        public string TestId => "HC5";

        public async Task<FcRunResult> RunAsync(FcGroup group, RunFcContext ctx)
        {
            ctx.Log($"[{TestId}] 压力传感器信号采集测试 开始");
            var v1 = HcRunnerHelpers.ParseInputDouble(group.Rows[0].InputValueRaw) ?? 0.5;
            var v2 = HcRunnerHelpers.ParseInputDouble(group.Rows[1].InputValueRaw) ?? 0.5;
            var v3 = HcRunnerHelpers.ParseInputDouble(group.Rows[2].InputValueRaw) ?? 0.5;
            ctx.Log($"[{TestId}]   SYS1={v1:0.##}V  SYS2={v2:0.##}V  SYS3={v3:0.##}V");

            var vm = ContainerLocator.Container.Resolve<HC_6_5ViewModel>();
            await vm.RunWithScriptVoltagesAsync(v1, v2, v3, ctx.CancellationToken).ConfigureAwait(false);

            return HcRunnerHelpers.ApplyJudgements(TestId, group,
                new double?[] { vm.ScriptPressureSys1Value, vm.ScriptPressureSys2Value, vm.ScriptPressureSys3Value }, ctx);
        }
    }

    // ─── HC6: 压差传感器信号采集测试 (6 行, RANGE) ────────────────────────────────
    public sealed class Hc6Runner : IFcRunner
    {
        public string TestId => "HC6";

        public async Task<FcRunResult> RunAsync(FcGroup group, RunFcContext ctx)
        {
            ctx.Log($"[{TestId}] 压差传感器信号采集测试 开始");
            var currents = group.Rows.Take(6)
                .Select(r => HcRunnerHelpers.ParseInputDouble(r.InputValueRaw) ?? 4.0)
                .ToArray();
            ctx.Log($"[{TestId}]   EDP2={currents[0]:0.#} EMP2B={currents[1]:0.#} EMP3B={currents[2]:0.#} RF2={currents[3]:0.#} SYS2={currents[4]:0.#} SYS3={currents[5]:0.#} mA");

            var vm = ContainerLocator.Container.Resolve<HC_6_6ViewModel>();
            await vm.RunWithScriptCurrentsAsync(currents, ctx.CancellationToken).ConfigureAwait(false);

            return HcRunnerHelpers.ApplyJudgements(TestId, group,
                new double?[]
                {
                    vm.ScriptDptEdp2Value,
                    vm.ScriptDptEmp2BValue,
                    vm.ScriptDptEmp3BValue,
                    vm.ScriptDptRf2Value,
                    vm.ScriptDptSys2Value,
                    vm.ScriptDptSys3Value,
                }, ctx);
        }
    }

    // ─── HC7: 油量传感器信号采集测试 (6 行, RANGE) ────────────────────────────────
    // 行0: 激励幅值Sys2 → Pin3031VoltValue (Vrms)
    // 行1: 激励幅值Sys3 → Pin3334VoltValue (Vrms)
    // 行2: 激励频率Sys2 → Pin3031FreqValue (Hz)
    // 行3: 激励频率Sys3 → Pin3334FreqValue (Hz)
    // 行4: 油量Sys2 (输入VA1/VB1) → ScriptQtySys1Value (%)
    // 行5: 油量Sys3 (输入VA2/VB2) → ScriptQtySys2Value (%)
    public sealed class Hc7Runner : IFcRunner
    {
        public string TestId => "HC7";

        public async Task<FcRunResult> RunAsync(FcGroup group, RunFcContext ctx)
        {
            ctx.Log($"[{TestId}] 油量传感器信号采集测试 开始");
            var (va1, vb1) = HcRunnerHelpers.ParseVaVb(group.Rows[4].InputValueRaw);
            var (va2, vb2) = HcRunnerHelpers.ParseVaVb(group.Rows[5].InputValueRaw);
            ctx.Log($"[{TestId}]   Sys2(Va={va1:0.##}V Vb={vb1:0.##}V)  Sys3(Va={va2:0.##}V Vb={vb2:0.##}V)");

            var vm = ContainerLocator.Container.Resolve<HC_6_7ViewModel>();
            await vm.RunWithScriptLvdtAsync(va1, vb1, va2, vb2, ctx.CancellationToken).ConfigureAwait(false);

            return HcRunnerHelpers.ApplyJudgements(TestId, group,
                new double?[]
                {
                    vm.Pin3031VoltValue,
                    vm.Pin3334VoltValue,
                    vm.Pin3031FreqValue,
                    vm.Pin3334FreqValue,
                    vm.ScriptQtySys1Value,
                    vm.ScriptQtySys2Value,
                }, ctx);
        }
    }

    // ─── HC8: 离散量采集测试 (54 行 = 27接地 + 27开路, EQ) ─────────────────────────
    // 行0~26:  各针脚接地状态 → GetGroundPinText(pin)
    // 行27~53: 各针脚开路状态 → GetOpenPinText(pin)
    public sealed class Hc8Runner : IFcRunner
    {
        public string TestId => "HC8";

        private static readonly int[] Pins = { 49, 50, 51, 52, 53, 54, 55, 56, 57, 58, 59, 60, 61, 62, 63,
                                                89, 90, 91, 92, 93, 94, 95, 96, 97, 98, 99, 100 };

        public async Task<FcRunResult> RunAsync(FcGroup group, RunFcContext ctx)
        {
            ctx.Log($"[{TestId}] 离散量采集测试 开始（{Pins.Length}针脚 × 2状态）");
            var vm = ContainerLocator.Container.Resolve<HC_6_8ViewModel>();
            await vm.RunOnceAsync(ctx.CancellationToken).ConfigureAwait(false);

            var texts = new string[54];
            for (var i = 0; i < Pins.Length; i++)
                texts[i] = vm.GetGroundPinText(Pins[i]);
            for (var i = 0; i < Pins.Length; i++)
                texts[27 + i] = vm.GetOpenPinText(Pins[i]);

            return HcRunnerHelpers.ApplyHexJudgements(TestId, group, texts, ctx);
        }
    }

    // ─── HC9: 离散量输出测试 (14 行 = 7开路 + 7闭合, GT/LT) ───────────────────────
    // 行0~6:   针脚J9~J15 开路阻抗 → GetOpenPinValue(9..15)
    // 行7~13:  针脚J9~J15 闭合阻抗 → GetClosePinValue(9..15)
    public sealed class Hc9Runner : IFcRunner
    {
        public string TestId => "HC9";

        public async Task<FcRunResult> RunAsync(FcGroup group, RunFcContext ctx)
        {
            ctx.Log($"[{TestId}] 离散量输出测试 开始（J9~J15 开路+闭合阻抗）");
            var vm = ContainerLocator.Container.Resolve<HC_6_9ViewModel>();
            await vm.RunOnceAsync(ctx.CancellationToken).ConfigureAwait(false);

            var vals = new double?[14];
            for (var i = 0; i < 7; i++)
                vals[i] = vm.GetOpenPinValue(9 + i);
            for (var i = 0; i < 7; i++)
                vals[7 + i] = vm.GetClosePinValue(9 + i);

            return HcRunnerHelpers.ApplyJudgements(TestId, group, vals, ctx);
        }
    }

    // ─── HC10: 通讯模块测试 (2 行, EQ decimal) ────────────────────────────────────
    // 行0: 发送油量设定qty（InputValue），接收测试台读到的2号油箱油量 → TestBenchTank2Text, JudgeParam=0
    // 行1: 控制板回传的1号油箱油量 → ControlBoardTank1Text, JudgeParam=qty
    public sealed class Hc10Runner : IFcRunner
    {
        public string TestId => "HC10";

        public async Task<FcRunResult> RunAsync(FcGroup group, RunFcContext ctx)
        {
            ctx.Log($"[{TestId}] 通讯模块测试 开始");
            var qty = (int)Math.Round(HcRunnerHelpers.ParseInputDouble(group.Rows[0].InputValueRaw) ?? 30.0);
            ctx.Log($"[{TestId}]   发送油量设定值 = {qty}");

            var vm = ContainerLocator.Container.Resolve<HC_6_10ViewModel>();
            await vm.RunWithScriptQuantityAsync(qty, ctx.CancellationToken).ConfigureAwait(false);

            var tank2 = HcRunnerHelpers.ParseTextDouble(vm.TestBenchTank2Text);
            var tank1 = HcRunnerHelpers.ParseTextDouble(vm.ControlBoardTank1Text);
            return HcRunnerHelpers.ApplyJudgements(TestId, group, new double?[] { tank2, tank1 }, ctx);
        }
    }

    // ─── 共享辅助方法 ───────────────────────────────────────────────────────────────
    internal static class HcRunnerHelpers
    {
        internal static FcRunResult ApplyJudgements(string testId, FcGroup group, double?[] vals, RunFcContext ctx)
        {
            var allPass = true;
            for (var i = 0; i < group.Rows.Count; i++)
            {
                var row = group.Rows[i];
                var val = i < vals.Length ? vals[i] : null;

                if (val.HasValue && JudgementRegistry.TryGet(row.JudgementType, out var judge))
                {
                    var r = judge.Evaluate(row.JudgementParamRaw, val.Value);
                    row.OutputValue = r.ActualText;
                    row.Pass = r.Pass;
                }
                else
                {
                    row.OutputValue = val.HasValue ? val.Value.ToString("0.######", CultureInfo.InvariantCulture) : "--";
                    row.Pass = false;
                }

                if (row.Pass != true) allPass = false;
                ctx.Log($"[{testId}]   [{row.OutputSignal}] 实测={row.OutputValue} {(row.Pass == true ? "PASS" : "FAIL")}");
            }

            return MakeResult(testId, allPass);
        }

        internal static FcRunResult ApplyHexJudgements(string testId, FcGroup group, string[] texts, RunFcContext ctx)
        {
            var allPass = true;
            for (var i = 0; i < group.Rows.Count && i < texts.Length; i++)
            {
                var row = group.Rows[i];
                var rxHex = texts[i];
                var rxLong = ParseRxHex(rxHex);

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
                ctx.Log($"[{testId}]   [{row.OutputSignal}] RX={row.OutputValue} {(row.Pass == true ? "PASS" : "FAIL")}");
            }

            return MakeResult(testId, allPass);
        }

        internal static FcRunResult MakeResult(string testId, bool allPass) =>
            new FcRunResult { TestId = testId, Status = allPass ? FcResultStatus.Pass : FcResultStatus.Fail };

        internal static double? ParseInputDouble(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw) || raw.Trim() == "--") return null;
            return double.TryParse(raw.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : (double?)null;
        }

        internal static double? ParseTextDouble(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var clean = text.Trim();
            if (clean == "--" || clean == "---" || clean == "无数据" || clean == "超时") return null;
            var parts = clean.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 0 && double.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out var v))
                return v;
            return null;
        }

        internal static (double Va, double Vb) ParseVaVb(string raw)
        {
            if (!string.IsNullOrWhiteSpace(raw) && raw.Trim() != "--")
            {
                var parts = raw.Trim().Split(new[] { ',', '/' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2
                    && double.TryParse(parts[0].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var va)
                    && double.TryParse(parts[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var vb))
                    return (va, vb);
            }

            return (2.4, 3.6);
        }

        private static long? ParseRxHex(string rxData)
        {
            if (string.IsNullOrEmpty(rxData) || rxData == "--") return null;
            var clean = rxData.ToLowerInvariant().Replace("0x", "").Replace(" ", "");
            return long.TryParse(clean, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v) ? v : (long?)null;
        }
    }
}
