// ============================================================================
// 脚本测试 - 单 FC 执行抽象
// 每个 FC 一个 Runner（Fc1Runner..Fc8Runner），由 ScriptTestService 调度。
// ============================================================================
using System;
using System.Threading;
using System.Threading.Tasks;
using MeasureControl.Services.ScriptTest.Models;

namespace MeasureControl.Services.ScriptTest.Runners
{
    /// <summary>
    /// 单个 FC 的执行接口。一次脚本测试会按顺序对 FC1..FC8 各调用一次 RunAsync。
    /// </summary>
    public interface IFcRunner
    {
        /// <summary>
        /// 处理的测试编号（如 "FC1"）。
        /// </summary>
        string TestId { get; }

        /// <summary>
        /// 执行该 FC 的所有判据行。
        /// 实现内部应：上电（如需要）→ 测量 → 判定 → 回填 row.OutputValue/row.Pass → 反序下电（下电先行）。
        /// 抛出异常视为"测试异常"，由 ScriptTestService 兜底处理。
        /// </summary>
        Task<FcRunResult> RunAsync(FcGroup group, RunFcContext context);
    }

    /// <summary>
    /// 运行上下文。提供日志、取消、进度回调。
    /// </summary>
    public sealed class RunFcContext
    {
        public CancellationToken CancellationToken { get; }
        public Action<string> Log { get; }
        public Action<string> Progress { get; }

        public RunFcContext(CancellationToken token, Action<string> log, Action<string> progress)
        {
            CancellationToken = token;
            Log = log ?? (_ => { });
            Progress = progress ?? (_ => { });
        }
    }
}
