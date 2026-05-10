// ============================================================================
// 脚本测试 - xlsx 解析器（基于 ClosedXML）。
// 解析时同时执行：L1 文件可读 / L2 标题 / L3 表头 / L4 行数结构 校验。
// ============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using MeasureControl.Services.ScriptTest.Models;

namespace MeasureControl.Services.ScriptTest
{
    /// <summary>
    /// 脚本解析与结构校验。L5 字段值校验在 ScriptValidator 中。
    /// 通过构造注入板型模板（标题 + FC 行数 Specs），使解析器可复用于任意板型。
    /// </summary>
    public sealed class ScriptParser
    {
        private readonly string _expectedTitle;
        private readonly IReadOnlyList<FcSpec> _specs;

        public ScriptParser(string expectedTitle, IReadOnlyList<FcSpec> specs)
        {
            if (string.IsNullOrEmpty(expectedTitle)) throw new ArgumentException("expectedTitle 不能为空", nameof(expectedTitle));
            if (specs == null || specs.Count == 0) throw new ArgumentException("specs 不能为空", nameof(specs));
            _expectedTitle = expectedTitle;
            _specs = specs;
        }

        /// <summary>
        /// 解析脚本 xlsx。返回 ScriptDocument（成功时 issues 为空），或在 issues 中列出所有错误。
        /// </summary>
        public ScriptDocument Parse(string filePath, out List<ValidationIssue> issues)
        {
            issues = new List<ValidationIssue>();
            var doc = new ScriptDocument { SourceFilePath = filePath };

            // L1: 文件可读
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                issues.Add(new ValidationIssue { Message = $"脚本文件不存在: {filePath}" });
                return null;
            }

            using (var wb = new XLWorkbook(filePath))
            {
                var ws = wb.Worksheet(1);
                if (ws == null)
                {
                    issues.Add(new ValidationIssue { Message = "脚本不包含任何工作表" });
                    return null;
                }

                // L2: 标题
                var title = ws.Cell(ScriptColumns.TitleRow, 1).GetString().Trim();
                doc.Title = title;
                if (!string.Equals(title, _expectedTitle, StringComparison.Ordinal))
                {
                    issues.Add(new ValidationIssue
                    {
                        RowNumber = ScriptColumns.TitleRow,
                        Column = "A",
                        Message = $"脚本标题不匹配。期望='{_expectedTitle}'，实际='{title}'"
                    });
                    return null; // 标题不对没必要继续
                }

                // L3: 表头
                for (int c = 1; c <= ScriptColumns.TotalColumns; c++)
                {
                    var actual = ws.Cell(ScriptColumns.HeaderRow, c).GetString().Trim();
                    var expected = ScriptColumns.HeaderTexts[c - 1];
                    if (!string.Equals(actual, expected, StringComparison.Ordinal))
                    {
                        issues.Add(new ValidationIssue
                        {
                            RowNumber = ScriptColumns.HeaderRow,
                            Column = ((char)('A' + c - 1)).ToString(),
                            Message = $"表头列名不匹配。期望='{expected}'，实际='{actual}'"
                        });
                    }
                }
                if (issues.Count > 0) return null;

                // L4: 动态解析 — 按 xlsx 实际 TestId 顺序，支持任意排列和重复次数
                var specMap = _specs.ToDictionary(s => s.TestId, s => s, StringComparer.OrdinalIgnoreCase);
                var instanceCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                int row = ScriptColumns.FirstDataRow;
                int maxRow = ws.LastRowUsed()?.RowNumber() ?? (ScriptColumns.FirstDataRow - 1);
                string fwdInputSignal = null, fwdInputUnit = null, fwdInputValue = null;

                while (row <= maxRow)
                {
                    var testId = GetCellTrim(ws, row, ScriptColumns.TestId);

                    if (string.IsNullOrEmpty(testId))
                    {
                        row++;
                        continue;
                    }

                    if (!specMap.TryGetValue(testId, out var spec))
                    {
                        issues.Add(new ValidationIssue
                        {
                            RowNumber = row,
                            Column = "A",
                            Message = $"未知的测试编号: '{testId}'（合法值: {string.Join(", ", specMap.Keys)}）"
                        });
                        return null;
                    }

                    instanceCount.TryGetValue(testId, out int prev);
                    int instance = prev + 1;
                    instanceCount[testId] = instance;

                    var group = new FcGroup
                    {
                        TestId = testId,
                        TestItem = spec.TestItem,
                        InstanceIndex = instance,
                        FirstRowNumber = row,
                    };

                    fwdInputSignal = null;
                    fwdInputUnit = null;
                    fwdInputValue = null;

                    // nextRow = 正常完成后 row 推进到的位置；groupSkipped = 该组是否因结构异常被跳过
                    int nextRow = row + spec.RowCount;
                    bool groupSkipped = false;

                    for (int i = 0; i < spec.RowCount; i++)
                    {
                        int r = row + i;

                        // 行数不足：文件提前结束 → 跳过该组，继续外层循环（退出 while）
                        if (r > maxRow)
                        {
                            issues.Add(new ValidationIssue
                            {
                                RowNumber = r,
                                Column = "A",
                                Message = $"{group.GroupKey} 需要 {spec.RowCount} 行，但脚本在第 {r - 1} 行已结束，已跳过该测试项",
                                IsFatal = false
                            });
                            groupSkipped = true;
                            nextRow = maxRow + 1;
                            break;
                        }

                        // 行数不足：第 2+ 行出现了不同的 TestId → 跳过该组，从该行重新解析
                        if (i > 0)
                        {
                            var midId = GetCellTrim(ws, r, ScriptColumns.TestId);
                            if (!string.IsNullOrEmpty(midId) &&
                                !string.Equals(midId, testId, StringComparison.OrdinalIgnoreCase))
                            {
                                issues.Add(new ValidationIssue
                                {
                                    RowNumber = row,
                                    Column = "A",
                                    Message = $"{group.GroupKey} 行数不足（第 {r} 行出现新测试编号 '{midId}'），已跳过该测试项",
                                    IsFatal = false
                                });
                                groupSkipped = true;
                                nextRow = r; // 从该行重新开始解析
                                break;
                            }
                        }

                        var inSig = GetCellTrim(ws, r, ScriptColumns.InputSignal);
                        var inUnit = GetCellTrim(ws, r, ScriptColumns.InputUnit);
                        var inVal = GetCellTrim(ws, r, ScriptColumns.InputValue);

                        // forward-fill：空字符串 → 继承上一行；"--" 当作显式值不继承
                        if (!string.IsNullOrEmpty(inSig)) fwdInputSignal = inSig; else inSig = fwdInputSignal ?? string.Empty;
                        if (!string.IsNullOrEmpty(inUnit)) fwdInputUnit = inUnit; else inUnit = fwdInputUnit ?? string.Empty;
                        if (!string.IsNullOrEmpty(inVal)) fwdInputValue = inVal; else inVal = fwdInputValue ?? string.Empty;

                        var rowModel = new ScriptRow
                        {
                            RowNumber = r,
                            InputSignal = inSig,
                            OutputSignal = GetCellTrim(ws, r, ScriptColumns.OutputSignal),
                            InputUnit = inUnit,
                            JudgementType = GetCellTrim(ws, r, ScriptColumns.JudgementType),
                            JudgementUnit = GetCellTrim(ws, r, ScriptColumns.JudgementUnit),
                            InputValueRaw = inVal,
                            JudgementParamRaw = GetCellTrim(ws, r, ScriptColumns.JudgementParam),
                        };

                        // 输出信号必须有值（每行独立）
                        if (string.IsNullOrEmpty(rowModel.OutputSignal))
                        {
                            issues.Add(new ValidationIssue
                            {
                                RowNumber = r,
                                Column = "E",
                                Message = $"{group.GroupKey} 的输出信号为空"
                            });
                        }
                        if (string.IsNullOrEmpty(rowModel.JudgementType))
                        {
                            issues.Add(new ValidationIssue
                            {
                                RowNumber = r,
                                Column = "G",
                                Message = $"{group.GroupKey} 的判据类型为空"
                            });
                        }

                        group.Rows.Add(rowModel);
                    }

                    row = nextRow;
                    if (!groupSkipped)
                    {
                        group.LastRowNumber = row - 1;
                        doc.Groups.Add(group);
                    }
                }

                if (doc.Groups.Count == 0)
                {
                    issues.Add(new ValidationIssue { Message = "脚本不包含任何测试项（数据从第3行起）" });
                }
            }

            return issues.Any(i => i.IsFatal) ? null : doc;
        }

        private static string GetCellTrim(IXLWorksheet ws, int row, int col)
        {
            try
            {
                return ws.Cell(row, col).GetString()?.Trim() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

    }
}
