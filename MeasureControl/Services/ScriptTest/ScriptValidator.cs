// ============================================================================
// 脚本测试 - L5 字段值校验
// 校验：判据类型白名单、判据参数可解析、POWER_IN 输入值 ∈ {18,28,32.2}、HEX 数据格式
// ============================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MeasureControl.Services.ScriptTest.Judgements;
using MeasureControl.Services.ScriptTest.Models;

namespace MeasureControl.Services.ScriptTest
{
    public sealed class ScriptValidator
    {
        private readonly (double Min, double Max)? _powerInVoltageRange;

        /// <summary>
        /// 构造校验器。powerInVoltageRange 为 null 时跳过 POWER_IN 区间校验（只要求可解析为数值）。
        /// </summary>
        public ScriptValidator((double Min, double Max)? powerInVoltageRange)
        {
            _powerInVoltageRange = powerInVoltageRange;
        }

        /// <summary>
        /// 对已解析的 ScriptDocument 做字段值层面的校验。
        /// </summary>
        public List<ValidationIssue> Validate(ScriptDocument doc)
        {
            var issues = new List<ValidationIssue>();
            if (doc == null) return issues;

            foreach (var group in doc.Groups)
            {
                foreach (var row in group.Rows)
                {
                    ValidateRow(group, row, issues);
                }
            }
            return issues;
        }

        private void ValidateRow(FcGroup group, ScriptRow row, List<ValidationIssue> issues)
        {
            // 判据类型白名单
            if (!JudgementRegistry.TryGet(row.JudgementType, out var judgement))
            {
                issues.Add(new ValidationIssue
                {
                    RowNumber = row.RowNumber,
                    Column = "G",
                    Message = $"未知判据类型 '{row.JudgementType}'，合法值: {string.Join("/", JudgementRegistry.KnownTypes)}"
                });
                return;
            }

            // 锁死行不读判据参数（软件直接调 RunOnceAsync）
            if (row.IsLocked) return;

            // 判据参数可解析
            if (!judgement.ValidateParam(row.JudgementParamRaw, out var paramErr))
            {
                issues.Add(new ValidationIssue
                {
                    RowNumber = row.RowNumber,
                    Column = "J",
                    Message = paramErr
                });
            }

            // POWER_IN 输入值必须在 {18,28,32.2} 档位之一
            if (string.Equals(row.InputSignal, "POWER_IN", StringComparison.OrdinalIgnoreCase))
            {
                if (!double.TryParse(row.InputValueRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                {
                    issues.Add(new ValidationIssue
                    {
                        RowNumber = row.RowNumber,
                        Column = "I",
                        Message = $"POWER_IN 输入值无法解析为数值: '{row.InputValueRaw}'"
                    });
                }
                else if (_powerInVoltageRange is (double min, double max))
                {
                    if (v < min || v > max)
                    {
                        issues.Add(new ValidationIssue
                        {
                            RowNumber = row.RowNumber,
                            Column = "I",
                            Message = $"POWER_IN 输入值 {v}V 超出合法区间 [{min}, {max}]"
                        });
                    }
                }
            }
            // HEX 类输入（如 FC7/FC8 的 0xAA55 / AA 55）
            else if (string.Equals(row.InputUnit, "HEX", StringComparison.OrdinalIgnoreCase))
            {
                bool ok = JudgeHelpersAccessor.IsHex(row.InputValueRaw);
                if (!ok)
                {
                    issues.Add(new ValidationIssue
                    {
                        RowNumber = row.RowNumber,
                        Column = "I",
                        Message = $"HEX 输入值格式不合法: '{row.InputValueRaw}'"
                    });
                }
            }
        }
    }

    /// <summary>暴露 Judgements 命名空间内的 helper（避免 internal 跨命名空间访问问题）。</summary>
    internal static class JudgeHelpersAccessor
    {
        public static bool IsHex(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            text = text.Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                return long.TryParse(text.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _);
            }
            // 字节序列 "AA 55"
            var parts = text.Split(new[] { ' ', '\t', '-', ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return false;
            foreach (var p in parts)
            {
                if (!byte.TryParse(p, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _)) return false;
            }
            return true;
        }
    }
}
