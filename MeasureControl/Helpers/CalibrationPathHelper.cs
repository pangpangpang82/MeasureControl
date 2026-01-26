using System;
using System.IO;
using System.Threading;

namespace MeasureControl.Helpers
{
    /// <summary>
    /// 管理项目级的标定数据存储路径。
    /// </summary>
    public static class CalibrationPathHelper
    {
        private static string _projectPath;
        private static string _projectName;
        private static long _projectVersion = 0;

        /// <summary>
        /// 设置当前项目路径（含 .json），用于生成标定文件默认路径。
        /// </summary>
        public static void SetProjectPath(string projectPath)
        {
            _projectPath = projectPath;
            _projectName = string.IsNullOrEmpty(projectPath)
                ? string.Empty
                : Path.GetFileNameWithoutExtension(projectPath);
            Interlocked.Increment(ref _projectVersion);
        }

        /// <summary>
        /// 清空当前项目路径。
        /// </summary>
        public static void Reset()
        {
            _projectPath = null;
            _projectName = null;
            Interlocked.Increment(ref _projectVersion);
        }

        /// <summary>
        /// 标定文件存储目录（默认放在项目同级的 DataCalibration 文件夹）。
        /// </summary>
        public static string DefaultFolder
        {
            get
            {
                if (string.IsNullOrEmpty(_projectPath))
                    return string.Empty;
                var projectDir = Path.GetDirectoryName(_projectPath);
                return string.IsNullOrEmpty(projectDir)
                    ? string.Empty
                    : Path.Combine(projectDir, "DataCalibration");
            }
        }

        /// <summary>
        /// 默认标定文件路径（项目名_校准数据.json）。
        /// </summary>
        public static string DefaultFile
        {
            get
            {
                if (string.IsNullOrEmpty(DefaultFolder) || string.IsNullOrEmpty(_projectName))
                    return string.Empty;
                return Path.Combine(DefaultFolder, $"{_projectName}_校准数据.json");
            }
        }

        /// <summary>
        /// 确保标定目录存在。
        /// </summary>
        public static void EnsureFolder()
        {
            if (!string.IsNullOrEmpty(DefaultFolder) && !Directory.Exists(DefaultFolder))
            {
                Directory.CreateDirectory(DefaultFolder);
            }
        }
    }
}

