// ============================================================================
// 脚本测试 - 加放油 8 个 FC 的占位 Runner
// 当前阶段：仅打通骨架。每个 Runner 会调用现有单板测试 VM 的 RunOnceAsync 拿到 PASS/FAIL，
// 输出值列回填 "--"。后续会将每个 Runner 替换为参数化版本（复制 FuelController VM 后改造）。
// ============================================================================
using System;
using System.Threading.Tasks;
using MeasureControl.Services.ScriptTest.Models;
using MeasureControl.ViewModels.SingleBoardTest.FuelController;
using Prism.Ioc;

namespace MeasureControl.Services.ScriptTest.Runners
{
    /// <summary>所有 FC Runner 的基类：按 IoC 解析对应 VM，调用 RunOnceAsync。</summary>
    internal abstract class FuelStubRunnerBase<TVm> : IFcRunner where TVm : class
    {
        public abstract string TestId { get; }

        public async Task<FcRunResult> RunAsync(FcGroup group, RunFcContext context)
        {
            context.Progress($"{TestId} 开始（占位实现：调用现有 VM 的 RunOnceAsync）");

            TVm vm;
            try
            {
                vm = ContainerLocator.Container.Resolve<TVm>();
            }
            catch (Exception ex)
            {
                return new FcRunResult { TestId = TestId, Status = FcResultStatus.Exception, Message = $"无法解析 VM {typeof(TVm).Name}: {ex.Message}" };
            }

            string vmResult;
            try
            {
                vmResult = await InvokeRunOnceAsync(vm, context).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return new FcRunResult { TestId = TestId, Status = FcResultStatus.Cancelled };
            }
            catch (Exception ex)
            {
                return new FcRunResult { TestId = TestId, Status = FcResultStatus.Exception, Message = ex.Message };
            }

            // 锁死/参数化（占位阶段不区分），按 VM 返回值做整 FC PASS/FAIL
            var status = string.Equals(vmResult, "PASS", StringComparison.OrdinalIgnoreCase) ? FcResultStatus.Pass : FcResultStatus.Fail;

            // 占位：所有判据行的输出值都填 "--"，待替换为参数化版本后填真实测量值
            foreach (var row in group.Rows)
            {
                row.OutputValue = "--";
                row.Pass = status == FcResultStatus.Pass;
            }

            context.Progress($"{TestId} 结束: {status}");
            return new FcRunResult { TestId = TestId, Status = status };
        }

        protected abstract Task<string> InvokeRunOnceAsync(TVm vm, RunFcContext context);
    }

    internal sealed class Fc1StubRunner : FuelStubRunnerBase<PowerImpedanceTestViewModel>
    {
        public override string TestId => "FC1";
        protected override Task<string> InvokeRunOnceAsync(PowerImpedanceTestViewModel vm, RunFcContext ctx)
            => vm.RunOnceAsync(ctx.CancellationToken);
    }

    internal sealed class Fc2StubRunner : FuelStubRunnerBase<SecondaryPowerTestViewModel>
    {
        public override string TestId => "FC2";
        protected override Task<string> InvokeRunOnceAsync(SecondaryPowerTestViewModel vm, RunFcContext ctx)
            => vm.RunOnceAsync(ctx.CancellationToken);
    }

    internal sealed class Fc3StubRunner : FuelStubRunnerBase<LowVoltageAlarmTestViewModel>
    {
        public override string TestId => "FC3";
        protected override Task<string> InvokeRunOnceAsync(LowVoltageAlarmTestViewModel vm, RunFcContext ctx)
            => vm.RunOnceAsync(ctx.CancellationToken);
    }

    internal sealed class Fc4StubRunner : FuelStubRunnerBase<TemperatureAcquisitionTestViewModel>
    {
        public override string TestId => "FC4";
        protected override Task<string> InvokeRunOnceAsync(TemperatureAcquisitionTestViewModel vm, RunFcContext ctx)
            => vm.RunOnceAsync(ctx.CancellationToken);
    }

    internal sealed class Fc5StubRunner : FuelStubRunnerBase<DiscreteInputTestViewModel>
    {
        public override string TestId => "FC5";
        protected override Task<string> InvokeRunOnceAsync(DiscreteInputTestViewModel vm, RunFcContext ctx)
            => vm.RunOnceAsync(ctx.CancellationToken);
    }

    internal sealed class Fc6StubRunner : FuelStubRunnerBase<DiscreteOutputTestViewModel>
    {
        public override string TestId => "FC6";
        protected override Task<string> InvokeRunOnceAsync(DiscreteOutputTestViewModel vm, RunFcContext ctx)
            => vm.RunOnceAsync(ctx.CancellationToken);
    }

    internal sealed class Fc7StubRunner : FuelStubRunnerBase<RS422CommunicationFunctionTestViewModel>
    {
        public override string TestId => "FC7";
        protected override Task<string> InvokeRunOnceAsync(RS422CommunicationFunctionTestViewModel vm, RunFcContext ctx)
            => vm.RunOnceAsync(ctx.CancellationToken);
    }

    internal sealed class Fc8StubRunner : FuelStubRunnerBase<RS422SelfCheckTestViewModel>
    {
        public override string TestId => "FC8";
        protected override Task<string> InvokeRunOnceAsync(RS422SelfCheckTestViewModel vm, RunFcContext ctx)
            => vm.RunOnceAsync(ctx.CancellationToken);
    }
}
