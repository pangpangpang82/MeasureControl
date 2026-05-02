// ============================================================================
// 脚本测试 - xlsx 解析器（基于 ClosedXML）。
// 解析时同时执行：L1 文件可读 / L2 标题 / L3 表头 / L4 行数结构 校验。
// ============================================================================
using System;
using System.Collections.Generic;
using System.IO;
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

                // L4: 按模板逐 FC 解析行
                int row = ScriptColumns.FirstDataRow;
                string fwdInputSignal = null, fwdInputUnit = null, fwdInputValue = null;

                foreach (var spec in _specs)
                {
                    var group = new FcGroup
                    {
                        TestId = spec.TestId,
                        TestItem = spec.TestItem,
                        FirstRowNumber = row,
                    };

                    // 第一行的"测试编号"必须等于 spec.TestId（防止结构错位）
                    var firstId = GetCellTrim(ws, row, ScriptColumns.TestId);
                    if (!string.Equals(firstId, spec.TestId, StringComparison.OrdinalIgnoreCase))
                    {
                        issues.Add(new ValidationIssue
                        {
                            RowNumber = row,
                            Column = "A",
                            Message = $"期望 {spec.TestId}，实际='{firstId}'。可能行数结构与模板不一致"
                        });
                        return null;
                    }

                    // FC 第一行重置 forward-fill 上下文
                    fwdInputSignal = NormalizeOrNull(GetCellTrim(ws, row, ScriptColumns.InputSignal));
                    fwdInputUnit = NormalizeOrNull(GetCellTrim(ws, row, ScriptColumns.InputUnit));
                    fwdInputValue = NormalizeOrNull(GetCellTrim(ws, row, ScriptColumns.InputValue));

                    for (int i = 0; i < spec.RowCount; i++)
                    {
                        int r = row + i;

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
                                Message = $"{spec.TestId} 的输出信号为空"
                            });
                        }
                        if (string.IsNullOrEmpty(rowModel.JudgementType))
                        {
                            issues.Add(new ValidationIssue
                            {
                                RowNumber = r,
                                Column = "G",
                                Message = $"{spec.TestId} 的判据类型为空"
                            });
                        }

                        group.Rows.Add(rowModel);
                    }

                    group.LastRowNumber = row + spec.RowCount - 1;
                    doc.Groups.Add(group);
                    row += spec.RowCount;
                }

                // 校验脚本没有"多余"的行（除空白行外不应出现下一个 FC 编号）
                var trailingId = GetCellTrim(ws, row, ScriptColumns.TestId);
                if (!string.IsNullOrEmpty(trailingId))
                {
                    issues.Add(new ValidationIssue
                    {
                        RowNumber = row,
                        Column = "A",
                        Message = $"脚本超出模板预期行数，发现额外的测试编号='{trailingId}'"
                    });
                }
            }

            return issues.Count == 0 ? doc : null;
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

        private static string NormalizeOrNull(string s) => string.IsNullOrEmpty(s) ? null : s;
    }
}
