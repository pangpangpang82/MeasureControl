using System;
using System.IO;

namespace MeasureControl.Helpers.SelfInspection
{
    internal sealed class SelfInspectionContext
    {
        public SelfInspectionContext(string chassisName, string logFilePath, Action<string> logToUi)
        {
            ChassisName = chassisName;
            LogFilePath = logFilePath;
            LogToUi = logToUi;
        }

        public string ChassisName { get; }

        public string LogFilePath { get; }

        public Action<string> LogToUi { get; }

        public void Log(string message)
        {
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";

            try
            {
                var dir = Path.GetDirectoryName(LogFilePath);
                if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                using (var fs = new FileStream(LogFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                using (var sw = new StreamWriter(fs))
                {
                    sw.WriteLine(line);
                }
            }
            catch
            {
            }

            try
            {
                LogToUi?.Invoke(line);
            }
            catch
            {
            }
        }
    }
}
