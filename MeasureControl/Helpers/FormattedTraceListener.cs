using System;
using System.Diagnostics;
using System.Threading;

namespace MeasureControl.Helpers
{
    /// <summary>
    /// 将原始 Trace/Debug 输出进行统一格式化（添加时间戳、线程号等），并将格式化后的内容转发到原始 TraceListener。
    /// 这样可以在不修改大量现有 Debug.WriteLine 调用的情况下，让输出窗口显示更规范的日志。
    /// </summary>
    public class FormattedTraceListener : TraceListener
    {
        private readonly TraceListener _innerListener;
        // 重复合并相关状态（线程安全）
        private readonly object _sync = new object();
        private string _lastOriginalMessage;
        private int _repeatCount = 0;
        // 当重复次数达到此阈值时，周期性写出一条摘要以提示正在抑制重复日志（避免完全静默）
        private const int SummaryIntervalCount = 100;

        public FormattedTraceListener(TraceListener innerListener)
        {
            _innerListener = innerListener ?? throw new ArgumentNullException(nameof(innerListener));
        }

        public override void Write(string message)
        {
            // 将 Write 当作 WriteLine 的非换行版本处理：复用重复合并逻辑
            try
            {
                HandleMessage(message, isLine:false);
            }
            catch
            {
                // 忽略以避免影响主流程
            }
        }

        public override void WriteLine(string message)
        {
            try
            {
                HandleMessage(message, isLine:true);
            }
            catch
            {
                // 忽略以避免影响主流程
            }
        }

        private void HandleMessage(string original, bool isLine)
        {
            if (original == null) original = string.Empty;

            lock (_sync)
            {
                // 如果与上一条原始消息相同，则合并（抑制）并仅在达到一定间隔时写入一次摘要
                if (string.Equals(original, _lastOriginalMessage, StringComparison.Ordinal))
                {
                    _repeatCount++;
                    if (_repeatCount % SummaryIntervalCount == 0)
                    {
                        // 周期性输出摘要，提醒有重复消息被抑制
                        var summary = $"(suppressed) previous message repeated {_repeatCount} times";
                        if (isLine) _innerListener.WriteLine(Format(summary));
                        else _innerListener.Write(Format(summary));
                    }
                    // 否则完全抑制这条消息
                    return;
                }

                // 如果上一条消息有重复被抑制，先输出一条摘要
                if (_repeatCount > 0)
                {
                    var flush = $"(suppressed) previous message repeated {_repeatCount} times";
                    _innerListener.WriteLine(Format(flush));
                    _repeatCount = 0;
                }

                // 记录并输出当前新消息
                _lastOriginalMessage = original;
                if (isLine) _innerListener.WriteLine(Format(original));
                else _innerListener.Write(Format(original));
            }
        }

        private string Format(string original)
        {
            if (string.IsNullOrEmpty(original)) return original;
            // 保持原始消息主体，但在前面添加统一时间戳和线程信息
            return $"{DateTime.Now:HH:mm:ss.fff} [T{Thread.CurrentThread.ManagedThreadId}] {original}";
        }
    }
}

