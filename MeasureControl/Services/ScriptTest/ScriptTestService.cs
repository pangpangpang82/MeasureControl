// ============================================================================
// 脚本测试 - 调度服务
// 流程：加载 xlsx → L1~L5 校验 → 任一错误就汇总返回（不进入测试）
//      → 逐 FC 调用 IFcRunner.RunAsync → 异常隔离（继续下一 FC）→ 写回副本
// ============================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MeasureControl.Services.ScriptTest.Models;
using MeasureControl.Services.ScriptTest.Plugins;
using MeasureControl.Services.ScriptTest.Runners;

namespace MeasureControl.Services.ScriptTest
{
    /// <summary>
    /// 无状态的脚本测试调度器。每次 RunAsync 根据传入的 IScriptTestPlugin 構建解析器/校验器/Runner 集合。
    /// 新增板型只需新建 IScriptTestPlugin 实现 + 在 ScriptTestFeature 注册即可，本类无需修改。
    /// </summary>
    public sealed class ScriptTestService : IScriptTestService
    {
        public async Task<ScriptTestRunSummary> RunAsync(
            IScriptTestPlugin plugin,
            string scriptPath,
            Action<string> logSink,
            Action<string> progressSink,
            CancellationToken cancellationToken)
        {
            if (plugin == null) throw new ArgumentNullException(nameof(plugin));

            var summary = new ScriptTestRunSummary { SourceScriptPath = scriptPath };
            void Log(string msg) { try { logSink?.Invoke(msg); } catch { } }
            void Progress(string msg) { try { progressSink?.Invoke(msg); } catch { } }

            var runners = (plugin.CreateRunners() ?? Array.Empty<IFcRunner>())
                .ToDictionary(r => r.TestId, r => r, StringComparer.OrdinalIgnoreCase);

            // ---- 解析 + 结构校验（使用插件提供的标题与 Specs）
            var parser = new ScriptParser(plugin.ScriptTitle, plugin.Specs);
            var doc = parser.Parse(scriptPath, out var parseIssues);
            if (parseIssues != null && parseIssues.Count > 0)
            {
                summary.LoadingIssues.AddRange(parseIssues);
                return summary;
            }
            if (doc == null)
            {
                summary.LoadingIssues.Add(new ValidationIssue { Message = "脚本解析失败（未知原因）" });
                return summary;
            }

            // ---- L5 字段值校验（使用插件提供的 POWER_IN 合法区间）
            var validator = new ScriptValidator(plugin.PowerInVoltageRange);
            var fieldIssues = validator.Validate(doc);
            if (fieldIssues.Count > 0)
            {
                summary.LoadingIssues.AddRange(fieldIssues);
                return summary;
            }

            Log($"脚本加载成功: {scriptPath}");
            Log($"共 {doc.Groups.Count} 个 FC 测试项，准备执行");

            // ---- 逐 FC 执行
            var ctx = new RunFcContext(cancellationToken, Log, Progress);
            try
            {
            foreach (var group in doc.Groups)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    summary.Cancelled = true;
                    summary.FcResults.Add(new FcRunResult { TestId = group.TestId, Status = FcResultStatus.Cancelled });
                    foreach (var row in group.Rows) { row.OutputValue = "--"; }
                    continue;
                }

                FcRunResult result;
                if (!runners.TryGetValue(group.TestId, out var runner))
                {
                    result = new FcRunResult { TestId = group.TestId, Status = FcResultStatus.Exception, Message = $"未注册 Runner: {group.TestId}" };
                    foreach (var row in group.Rows) { row.OutputValue = "--"; }
                }
                else
                {
                    Progress($"开始执行 {group.TestId} {group.TestItem}");
                    try
                    {
                        result = await runner.RunAsync(group, ctx).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        summary.Cancelled = true;
                        result = new FcRunResult { TestId = group.TestId, Status = FcResultStatus.Cancelled };
                        foreach (var row in group.Rows) { row.OutputValue = "--"; }
                    }
                    catch (Exception ex)
                    {
                        Log($"{group.TestId} 测试异常: {ex.Message}");
                        result = new FcRunResult { TestId = group.TestId, Status = FcResultStatus.Exception, Message = ex.Message };
                        foreach (var row in group.Rows) { row.OutputValue = "--"; }
                    }
                }

                // 兜底：Runner 若以"返回值"形式报告 Exception/Cancelled（而非抛异常），
                // 上面的 catch 不会触发，此处统一把行输出置为 "--"，避免副本残留空值或旧值。
                if (result.Status != FcResultStatus.Pass && result.Status != FcResultStatus.Fail)
                {
                    foreach (var row in group.Rows) { row.OutputValue = "--"; }
                    if (result.Status == FcResultStatus.Cancelled) summary.Cancelled = true;
                }

                summary.FcResults.Add(result);
                Log($"{group.TestId} 结果: {result.ToCellText()}");

                // FC1/HC1 门控：不合格则取消所有后续测试
                if ((string.Equals(group.TestId, "FC1", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(group.TestId, "HC1", StringComparison.OrdinalIgnoreCase))
                    && result.Status != FcResultStatus.Pass)
                {
                    Log($"{group.TestId} 测试不合格，终止后续所有测试项");
                    summary.Cancelled = true;
                    foreach (var remaining in doc.Groups)
                    {
                        if (!summary.FcResults.Exists(r => r.TestId == remaining.TestId))
                        {
                            summary.FcResults.Add(new FcRunResult { TestId = remaining.TestId, Status = FcResultStatus.Cancelled });
                            foreach (var row in remaining.Rows) row.OutputValue = "--";
                        }
                    }
                    break;
                }
            }

            // ---- 写回副本
            try
            {
                var writer = new ScriptResultWriter();
                summary.ResultScriptPath = writer.WriteResultCopy(doc, summary.FcResults.ToArray());
                Log($"结果副本已生成: {summary.ResultScriptPath}");
            }
            catch (Exception ex)
            {
                Log($"写入结果副本失败: {ex.Message}");
            }
            } // end try (FC 执行循环)
            finally
            {
                // ---- 收尾下电（无论正常完成/中止/异常均执行，使用 None 保证下电不被取消）
                try { await plugin.TeardownAsync(Log, CancellationToken.None).ConfigureAwait(false); }
                catch (Exception ex) { Log($"Teardown 异常: {ex.Message}"); }
            }

            summary.OverallPass = !summary.Cancelled
                && summary.FcResults.All(r => r.Status == FcResultStatus.Pass);

            return summary;
        }
    }
}
