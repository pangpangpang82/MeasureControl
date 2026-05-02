// ============================================================================
// 脚本测试 - 服务接口
// ============================================================================
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MeasureControl.Services.ScriptTest.Models;
using MeasureControl.Services.ScriptTest.Plugins;

namespace MeasureControl.Services.ScriptTest
{
    public sealed class ScriptTestRunSummary
    {
        public string SourceScriptPath { get; set; }
        public string ResultScriptPath { get; set; }   // 副本路径
        public List<FcRunResult> FcResults { get; } = new List<FcRunResult>();
        public bool OverallPass { get; set; }
        public bool Cancelled { get; set; }

        /// <summary>
        /// 加载阶段错误。若不为空，表示脚本未进入测试。
        /// </summary>
        public List<ValidationIssue> LoadingIssues { get; } = new List<ValidationIssue>();
    }

    public interface IScriptTestService
    {
        /// <summary>
        /// 按指定插件执行整脚本测试。服务本身无状态，可跨板型复用。
        /// </summary>
        /// <param name="plugin">目标板型插件（提供模板与 Runner）。</param>
        /// <param name="scriptPath">用户选择的 xlsx 路径。</param>
        /// <param name="logSink">实时日志回调（UI 线程外可调）。</param>
        /// <param name="progressSink">实时进度回调。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>运行汇总；副本路径 + 各 FC 结果。</returns>
        Task<ScriptTestRunSummary> RunAsync(
            IScriptTestPlugin plugin,
            string scriptPath,
            Action<string> logSink,
            Action<string> progressSink,
            CancellationToken cancellationToken);
    }
}
