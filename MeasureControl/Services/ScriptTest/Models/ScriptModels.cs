// ============================================================================
// 脚本测试 - 数据模型
// 临时性甲方需求功能。本文件夹（Services/ScriptTest/）整体可删除以彻底关闭脚本测试能力。
// ============================================================================
using System;
using System.Collections.Generic;

namespace MeasureControl.Services.ScriptTest.Models
{
    /// <summary>
    /// 脚本表头列索引（1-based，与 ClosedXML 一致）。
    /// 与 V3 模板列顺序对齐：
    /// A=测试编号 B=测试项 C=测试步骤 D=输入信号 E=输出信号 F=输入单位
    /// G=判据类型 H=判据单位 I=输入值 J=判据参数 K=输出值 L=测试结果
    /// </summary>
    public static class ScriptColumns
    {
        public const int TestId = 1;          // A 测试编号
        public const int TestItem = 2;        // B 测试项
        public const int TestSteps = 3;       // C 测试步骤
        public const int InputSignal = 4;     // D 输入信号
        public const int OutputSignal = 5;    // E 输出信号
        public const int InputUnit = 6;       // F 输入单位
        public const int JudgementType = 7;   // G 判据类型
        public const int JudgementUnit = 8;   // H 判据单位
        public const int InputValue = 9;      // I 输入值
        public const int JudgementParam = 10; // J 判据参数
        public const int OutputValue = 11;    // K 输出值（软件回填）
        public const int TestResult = 12;     // L 测试结果（软件回填，FC 首行合并）

        public const int TotalColumns = 12;

        public static readonly string[] HeaderTexts =
        {
            "测试编号", "测试项", "测试步骤", "输入信号", "输出信号", "输入单位",
            "判据类型", "判据单位", "输入值", "判据参数", "输出值", "测试结果"
        };

        // 行偏移（1-based）
        public const int TitleRow = 1;        // 第一行：脚本标题
        public const int HeaderRow = 2;       // 第二行：列表头
        public const int FirstDataRow = 3;    // 第三行起为数据
    }

    /// <summary>
    /// 单条空白判据/输入约定写法。脚本里的 "--" 表示"无/锁死"。
    /// </summary>
    public static class ScriptTokens
    {
        public const string None = "--";
        public const string Empty = "";
    }

    /// <summary>
    /// 解析后的整份脚本。
    /// </summary>
    public sealed class ScriptDocument
    {
        public string SourceFilePath { get; set; }

        /// <summary>第一行标题（如"加放油控制器测试脚本"），用于板卡模板匹配校验。</summary>
        public string Title { get; set; }

        /// <summary>所有 FC 组（按出现顺序）。</summary>
        public List<FcGroup> Groups { get; } = new List<FcGroup>();
    }

    /// <summary>
    /// 一个 FC 组（如 FC1/HC4）。包含若干判据行。
    /// 同一 TestId 可在脚本中多次出现（每次使用不同输入值），由 InstanceIndex 区分。
    /// </summary>
    public sealed class FcGroup
    {
        /// <summary>FC 基础编号，例如 "HC4"。同一 TestId 可在脚本中多次出现。</summary>
        public string TestId { get; set; }

        /// <summary>测试项名称（如"温度采集测试"）。</summary>
        public string TestItem { get; set; }

        /// <summary>相同 TestId 第几次出现（1-based）。首次出现为 1。</summary>
        public int InstanceIndex { get; set; } = 1;

        /// <summary>
        /// 唯一组键，用于结果匹配和日志显示。
        /// 单实例时等于 TestId（如 "HC4"），多实例时追加序号（如 "HC4[2]"）。
        /// </summary>
        public string GroupKey => InstanceIndex <= 1 ? TestId : $"{TestId}[{InstanceIndex}]";

        /// <summary>FC 在 xlsx 中第一行的行号（1-based），用于回填"测试结果"列。</summary>
        public int FirstRowNumber { get; set; }

        /// <summary>FC 在 xlsx 中最后一行的行号（含），用于合并"测试结果"列。</summary>
        public int LastRowNumber { get; set; }

        /// <summary>该 FC 所有判据行（按表格顺序）。</summary>
        public List<ScriptRow> Rows { get; } = new List<ScriptRow>();
    }

    /// <summary>
    /// 一条判据行（一个输出信号 + 一个判据 + 可能的一个输入值）。
    /// </summary>
    public sealed class ScriptRow
    {
        /// <summary>该行在 xlsx 中的行号（1-based）。</summary>
        public int RowNumber { get; set; }

        /// <summary>输入信号名（forward-fill 后），可能是 "--"。</summary>
        public string InputSignal { get; set; }

        /// <summary>输出信号名（每行独立）。</summary>
        public string OutputSignal { get; set; }

        /// <summary>输入单位（如 V/HEX，可能为空）。</summary>
        public string InputUnit { get; set; }

        /// <summary>判据类型（GT/GE/LT/LE/EQ/RANGE/EQ_BYTES）。</summary>
        public string JudgementType { get; set; }

        /// <summary>判据单位（如 V/Ω/℃/BOOL/HEX）。仅展示用，软件不读。</summary>
        public string JudgementUnit { get; set; }

        /// <summary>输入值原文（forward-fill 后），可能是 "--" 或具体值。</summary>
        public string InputValueRaw { get; set; }

        /// <summary>判据参数原文（如 "500"、"4.5,5.5"、"AA 55"）。</summary>
        public string JudgementParamRaw { get; set; }

        /// <summary>是否锁死行（输入值为 "--"）。锁死时软件不读判据参数，调用现有 VM 的 RunOnceAsync。</summary>
        public bool IsLocked => string.Equals(InputValueRaw, ScriptTokens.None, StringComparison.Ordinal)
                                 || string.IsNullOrEmpty(InputValueRaw);

        // ---- 运行期回填字段 ----
        public string OutputValue { get; set; } = string.Empty;
        public bool? Pass { get; set; }
    }

    /// <summary>
    /// FC 执行最终结果。写回"测试结果"列（FC 首行）。
    /// </summary>
    public enum FcResultStatus
    {
        Pass,
        Fail,
        Exception,
        Cancelled,
        Skipped
    }

    public sealed class FcRunResult
    {
        public string TestId { get; set; }
        public FcResultStatus Status { get; set; }
        public string Message { get; set; } // 异常时填错误信息

        public string ToCellText()
        {
            switch (Status)
            {
                case FcResultStatus.Pass: return "PASS";
                case FcResultStatus.Fail: return "FAIL";
                case FcResultStatus.Exception: return string.IsNullOrEmpty(Message) ? "测试异常" : $"测试异常: {Message}";
                case FcResultStatus.Cancelled: return "已取消";
                case FcResultStatus.Skipped: return "已跳过";
                default: return "--";
            }
        }
    }

    /// <summary>
    /// 加载/校验阶段的问题。任一条出现时，软件不进入测试。
    /// </summary>
    public sealed class ValidationIssue
    {
        public int? RowNumber { get; set; }      // 出问题的行号（如有）
        public string Column { get; set; }        // 出问题的列名（如有）
        public string Message { get; set; }       // 错误描述

        public override string ToString()
        {
            if (RowNumber.HasValue && !string.IsNullOrEmpty(Column))
                return $"第 {RowNumber} 行 [{Column}]: {Message}";
            if (RowNumber.HasValue)
                return $"第 {RowNumber} 行: {Message}";
            return Message;
        }
    }
}
