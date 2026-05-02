// ============================================================================
// 加放油控制器脚本模板（行结构写死）。脚本测试时按此校验脚本结构是否合规。
// ============================================================================
using System.Collections.Generic;

namespace MeasureControl.Services.ScriptTest.Models
{
    /// <summary>
    /// 加放油控制器测试脚本模板常量。
    /// </summary>
    public static class FuelControllerScriptTemplate
    {
        /// <summary>第一行标题字面量。脚本第一行第一格必须等于此字符串。</summary>
        public const string ScriptTitle = "加放油控制器测试脚本";

        /// <summary>FC 编号 → 期望行数（按 V3 模板）。</summary>
        public static readonly IReadOnlyList<FcSpec> Specs = new[]
        {
            new FcSpec("FC1", "电源阻抗测试", 4),
            new FcSpec("FC2", "二次电源测试", 1),
            new FcSpec("FC3", "低电压告警功能测试", 1),
            new FcSpec("FC4", "温度采集功能", 1),
            new FcSpec("FC5", "离散量采集功能测试", 28),
            new FcSpec("FC6", "离散量输出功能测试", 15),
            new FcSpec("FC7", "RS422通信功能测试", 4),
            new FcSpec("FC8", "RS422通信自检测功能测试", 2),
        };
    }

    public sealed class FcSpec
    {
        public string TestId { get; }
        public string TestItem { get; }
        public int RowCount { get; }

        public FcSpec(string testId, string testItem, int rowCount)
        {
            TestId = testId;
            TestItem = testItem;
            RowCount = rowCount;
        }
    }
}
