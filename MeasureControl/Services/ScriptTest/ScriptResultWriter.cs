// ============================================================================
// 脚本测试 - 结果副本写入器
// 复制源 xlsx → 原名_结果_yyyyMMdd_HHmmss.xlsx
// 按 FC 锚点回填：每行 K 列"输出值"，FC 首行 L 列"测试结果"（合并 FirstRow..LastRow）。
// ============================================================================
using System;
using System.IO;
using ClosedXML.Excel;
using MeasureControl.Services.ScriptTest.Models;

namespace MeasureControl.Services.ScriptTest
{
    public sealed class ScriptResultWriter
    {
        /// <summary>
        /// 基于源脚本生成结果副本，并把 ScriptDocument 中的实测值/测试结果写入。
        /// </summary>
        /// <returns>副本文件的完整路径。</returns>
        public string WriteResultCopy(ScriptDocument doc, FcRunResult[] fcResults)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (string.IsNullOrEmpty(doc.SourceFilePath) || !File.Exists(doc.SourceFilePath))
                throw new FileNotFoundException("源脚本文件不存在", doc.SourceFilePath);

            // 完全用 string API 避开 Path 方法（LangVersion=latest 下 Path 可能选到 ROS<char> 重载导致编译错误）
            string srcPath = doc.SourceFilePath;
            int sepIdx = srcPath.LastIndexOfAny(new[] { '\\', '/' });
            string dir = sepIdx >= 0 ? srcPath.Substring(0, sepIdx) : string.Empty;
            string fileName = sepIdx >= 0 ? srcPath.Substring(sepIdx + 1) : srcPath;
            int dotIdx = fileName.LastIndexOf('.');
            string nameNoExt = dotIdx > 0 ? fileName.Substring(0, dotIdx) : fileName;
            string ext = dotIdx > 0 ? fileName.Substring(dotIdx) : string.Empty;
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string dstFileName = nameNoExt + "_结果_" + stamp + ext;
            string dstPath = string.IsNullOrEmpty(dir) ? dstFileName : (dir + System.IO.Path.DirectorySeparatorChar + dstFileName);

            File.Copy(srcPath, dstPath, overwrite: true);

            using (var wb = new XLWorkbook(dstPath))
            {
                var ws = wb.Worksheet(1);

                foreach (var group in doc.Groups)
                {
                    var fcRes = Array.Find(fcResults, r => r != null && r.TestId == group.GroupKey);
                    bool groupAbnormal = fcRes != null
                        && fcRes.Status != FcResultStatus.Pass
                        && fcRes.Status != FcResultStatus.Fail;
                    string abnormalText = groupAbnormal ? fcRes.ToCellText() : null;

                    // 每行独立回填"输出值"和"测试结果"
                    foreach (var row in group.Rows)
                    {
                        ws.Cell(row.RowNumber, ScriptColumns.OutputValue).Value = row.OutputValue ?? string.Empty;

                        string rowResult;
                        if (abnormalText != null)
                            rowResult = abnormalText;
                        else if (row.Pass == true)
                            rowResult = "PASS";
                        else if (row.Pass == false)
                            rowResult = "FAIL";
                        else
                            rowResult = "--";

                        var cell = ws.Cell(row.RowNumber, ScriptColumns.TestResult);
                        cell.Value = rowResult;
                        cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                    }
                }

                wb.Save();
            }

            return dstPath;
        }
    }
}
