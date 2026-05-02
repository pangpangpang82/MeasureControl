// ============================================================================
// 脚本测试 - 判据引擎
// 支持判据类型：GT / GE / LT / LE / EQ / RANGE / EQ_BYTES
// ============================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace MeasureControl.Services.ScriptTest.Judgements
{
    /// <summary>
    /// 单次判定结果。
    /// </summary>
    public sealed class JudgeResult
    {
        public bool Pass { get; set; }
        public string ActualText { get; set; }   // 实测值文本（用于回填"输出值"列）
        public string Reason { get; set; }       // 失败原因（PASS 时为空）

        public static JudgeResult Ok(string actual) => new JudgeResult { Pass = true, ActualText = actual, Reason = string.Empty };
        public static JudgeResult NotPass(string actual, string reason) => new JudgeResult { Pass = false, ActualText = actual, Reason = reason };
    }

    /// <summary>
    /// 判据接口。每种判据类型一个实现。
    /// </summary>
    public interface IJudgement
    {
        string TypeKey { get; }

        /// <summary>加载脚本时校验判据参数是否合法。</summary>
        bool ValidateParam(string parameterRaw, out string error);

        /// <summary>测试时执行判定。measured 可能是 double / byte[] / int / bool。</summary>
        JudgeResult Evaluate(string parameterRaw, object measured);
    }

    /// <summary>
    /// 判据工厂。
    /// </summary>
    public static class JudgementRegistry
    {
        private static readonly Dictionary<string, IJudgement> _map = new Dictionary<string, IJudgement>(StringComparer.OrdinalIgnoreCase)
        {
            { "GT", new ScalarComparisonJudgement("GT", (a, b) => a > b, "应 > {0}") },
            { "GE", new ScalarComparisonJudgement("GE", (a, b) => a >= b, "应 ≥ {0}") },
            { "LT", new ScalarComparisonJudgement("LT", (a, b) => a < b, "应 < {0}") },
            { "LE", new ScalarComparisonJudgement("LE", (a, b) => a <= b, "应 ≤ {0}") },
            { "EQ", new EqualityJudgement() },
            { "RANGE", new RangeJudgement() },
            { "EQ_BYTES", new EqualBytesJudgement() },
        };

        public static IEnumerable<string> KnownTypes => _map.Keys;

        public static bool TryGet(string typeKey, out IJudgement judgement)
        {
            if (string.IsNullOrEmpty(typeKey))
            {
                judgement = null;
                return false;
            }
            return _map.TryGetValue(typeKey, out judgement);
        }
    }

    // ------------------------------------------------------------------ Helpers

    internal static class JudgeHelpers
    {
        public static bool TryParseDouble(string text, out double value)
        {
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        public static bool TryParseHexNumber(string text, out long value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            text = text.Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                text = text.Substring(2);
            }
            return long.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
        }

        /// <summary>把 "AA 55" 之类的字节序列解析为 byte[]。</summary>
        public static bool TryParseHexBytes(string text, out byte[] bytes)
        {
            bytes = null;
            if (string.IsNullOrWhiteSpace(text)) return false;
            var parts = text.Split(new[] { ' ', '\t', '-', ',' }, StringSplitOptions.RemoveEmptyEntries);
            var list = new List<byte>(parts.Length);
            foreach (var p in parts)
            {
                if (!byte.TryParse(p, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
                {
                    return false;
                }
                list.Add(b);
            }
            bytes = list.ToArray();
            return bytes.Length > 0;
        }

        public static string FormatDouble(double value)
        {
            return value.ToString("0.######", CultureInfo.InvariantCulture);
        }

        public static double? ToDouble(object measured)
        {
            if (measured == null) return null;
            switch (measured)
            {
                case double d: return d;
                case float f: return f;
                case int i: return i;
                case long l: return l;
                case bool b: return b ? 1.0 : 0.0;
                case string s when TryParseDouble(s, out var sd): return sd;
                default:
                    if (measured is IConvertible)
                    {
                        try { return Convert.ToDouble(measured, CultureInfo.InvariantCulture); }
                        catch { return null; }
                    }
                    return null;
            }
        }
    }

    // ------------------------------------------------------------------ Implementations

    /// <summary>GT/GE/LT/LE 通用比较。</summary>
    internal sealed class ScalarComparisonJudgement : IJudgement
    {
        private readonly Func<double, double, bool> _comparer;
        private readonly string _expectedFormat;

        public ScalarComparisonJudgement(string typeKey, Func<double, double, bool> comparer, string expectedFormat)
        {
            TypeKey = typeKey;
            _comparer = comparer;
            _expectedFormat = expectedFormat;
        }

        public string TypeKey { get; }

        public bool ValidateParam(string parameterRaw, out string error)
        {
            if (!JudgeHelpers.TryParseDouble(parameterRaw, out _))
            {
                error = $"判据参数应为数值，实际='{parameterRaw}'";
                return false;
            }
            error = null;
            return true;
        }

        public JudgeResult Evaluate(string parameterRaw, object measured)
        {
            var threshold = double.Parse(parameterRaw, CultureInfo.InvariantCulture);
            var actual = JudgeHelpers.ToDouble(measured);
            var actualText = actual.HasValue ? JudgeHelpers.FormatDouble(actual.Value) : "(无)";

            if (!actual.HasValue)
                return JudgeResult.NotPass(actualText, "未读到实测值");

            var pass = _comparer(actual.Value, threshold);
            return pass
                ? JudgeResult.Ok(actualText)
                : JudgeResult.NotPass(actualText, string.Format(_expectedFormat, JudgeHelpers.FormatDouble(threshold)));
        }
    }

    /// <summary>EQ：数值或十六进制相等。HEX 自动识别。</summary>
    internal sealed class EqualityJudgement : IJudgement
    {
        public string TypeKey => "EQ";

        public bool ValidateParam(string parameterRaw, out string error)
        {
            if (string.IsNullOrWhiteSpace(parameterRaw))
            {
                error = "判据参数不能为空";
                return false;
            }
            // 接受十进制、十六进制 0x..、bool（0/1）
            if (JudgeHelpers.TryParseDouble(parameterRaw, out _)) { error = null; return true; }
            if (JudgeHelpers.TryParseHexNumber(parameterRaw, out _)) { error = null; return true; }
            error = $"无法解析为数值或十六进制：'{parameterRaw}'";
            return false;
        }

        public JudgeResult Evaluate(string parameterRaw, object measured)
        {
            // 优先按十六进制比较（脚本里写 0xAA55）
            long expectedI;
            if (parameterRaw.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                JudgeHelpers.TryParseHexNumber(parameterRaw, out expectedI);
                long actualI = ConvertToInt64(measured);
                var actualHex = "0x" + actualI.ToString("X");
                return actualI == expectedI ? JudgeResult.Ok(actualHex) : JudgeResult.NotPass(actualHex, $"应 == {parameterRaw}");
            }

            JudgeHelpers.TryParseDouble(parameterRaw, out var expectedD);
            var actualD = JudgeHelpers.ToDouble(measured) ?? double.NaN;
            var pass = Math.Abs(actualD - expectedD) < 1e-9;
            var actualText = JudgeHelpers.FormatDouble(actualD);
            return pass ? JudgeResult.Ok(actualText) : JudgeResult.NotPass(actualText, $"应 == {parameterRaw}");
        }

        private static long ConvertToInt64(object measured)
        {
            switch (measured)
            {
                case long l: return l;
                case int i: return i;
                case short s: return s;
                case byte b: return b;
                case bool bo: return bo ? 1 : 0;
                case double d: return (long)d;
                case byte[] arr:
                    long v = 0;
                    foreach (var b2 in arr) v = (v << 8) | b2;
                    return v;
                case string st when JudgeHelpers.TryParseHexNumber(st, out var hv): return hv;
                default:
                    if (measured is IConvertible) try { return Convert.ToInt64(measured); } catch { return 0; }
                    return 0;
            }
        }
    }

    /// <summary>RANGE：闭区间 [low, high]。参数格式 "low,high"。</summary>
    internal sealed class RangeJudgement : IJudgement
    {
        public string TypeKey => "RANGE";

        public bool ValidateParam(string parameterRaw, out string error)
        {
            if (!TryParse(parameterRaw, out var lo, out var hi))
            {
                error = $"判据参数格式应为 'low,high'，实际='{parameterRaw}'";
                return false;
            }
            if (lo > hi)
            {
                error = $"判据下限 {lo} 大于上限 {hi}";
                return false;
            }
            error = null;
            return true;
        }

        public JudgeResult Evaluate(string parameterRaw, object measured)
        {
            TryParse(parameterRaw, out var lo, out var hi);
            var actual = JudgeHelpers.ToDouble(measured);
            var actualText = actual.HasValue ? JudgeHelpers.FormatDouble(actual.Value) : "(无)";

            if (!actual.HasValue)
                return JudgeResult.NotPass(actualText, "未读到实测值");

            var pass = actual.Value >= lo && actual.Value <= hi;
            return pass
                ? JudgeResult.Ok(actualText)
                : JudgeResult.NotPass(actualText, $"应 ∈ [{JudgeHelpers.FormatDouble(lo)}, {JudgeHelpers.FormatDouble(hi)}]");
        }

        private static bool TryParse(string parameterRaw, out double low, out double high)
        {
            low = high = 0;
            if (string.IsNullOrEmpty(parameterRaw)) return false;
            var parts = parameterRaw.Split(',');
            if (parts.Length != 2) return false;
            return JudgeHelpers.TryParseDouble(parts[0].Trim(), out low)
                && JudgeHelpers.TryParseDouble(parts[1].Trim(), out high);
        }
    }

    /// <summary>EQ_BYTES：字节序列比对。参数格式 "AA 55"。</summary>
    internal sealed class EqualBytesJudgement : IJudgement
    {
        public string TypeKey => "EQ_BYTES";

        public bool ValidateParam(string parameterRaw, out string error)
        {
            if (!JudgeHelpers.TryParseHexBytes(parameterRaw, out _))
            {
                error = $"判据参数应为十六进制字节序列（如 'AA 55'），实际='{parameterRaw}'";
                return false;
            }
            error = null;
            return true;
        }

        public JudgeResult Evaluate(string parameterRaw, object measured)
        {
            JudgeHelpers.TryParseHexBytes(parameterRaw, out var expected);

            byte[] actual;
            switch (measured)
            {
                case byte[] arr: actual = arr; break;
                case string s when JudgeHelpers.TryParseHexBytes(s, out var sb): actual = sb; break;
                default: actual = null; break;
            }

            var actualText = actual == null ? "(无)" : BytesToHex(actual);
            if (actual == null)
                return JudgeResult.NotPass(actualText, "未读到实测字节");

            var pass = actual.Length == expected.Length && actual.SequenceEqual(expected);
            return pass
                ? JudgeResult.Ok(actualText)
                : JudgeResult.NotPass(actualText, $"应 == [{BytesToHex(expected)}]");
        }

        private static string BytesToHex(byte[] bytes)
        {
            return string.Join(" ", bytes.Select(b => b.ToString("X2")));
        }
    }
}
