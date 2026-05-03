// ============================================================================
// 液压控制器脚本模板（行结构写死）。脚本测试时按此校验脚本结构是否合规。
// ============================================================================
using System.Collections.Generic;

namespace MeasureControl.Services.ScriptTest.Models
{
    /// <summary>
    /// 液压控制器测试脚本模板常量。
    /// </summary>
    public static class HydraulicControllerScriptTemplate
    {
        /// <summary>第一行标题字面量。脚本第一行第一格必须等于此字符串。</summary>
        public const string ScriptTitle = "液压控制器测试脚本";

        /// <summary>HC 编号 → 期望行数（按 V2 模板）。</summary>
        public static readonly IReadOnlyList<FcSpec> Specs = new[]
        {
            new FcSpec("HC1",  "电源阻抗测试",           2),
            new FcSpec("HC2",  "通道ID测试",              2),
            new FcSpec("HC3",  "二次电源测试",            3),
            new FcSpec("HC4",  "温度采集测试",            2),
            new FcSpec("HC5",  "压力传感器信号采集测试",  3),
            new FcSpec("HC6",  "压差传感器信号采集测试",  6),
            new FcSpec("HC7",  "油量传感器信号采集测试",  6),
            new FcSpec("HC8",  "离散量采集测试",          54),
            new FcSpec("HC9",  "离散量输出测试",          14),
            new FcSpec("HC10", "通讯模块测试",            2),
        };
    }
}
