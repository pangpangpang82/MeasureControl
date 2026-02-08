using System;
using System.Collections.Generic;
using System.IO;
using MeasureControl.Models;
using Newtonsoft.Json;

namespace MeasureControl.Services
{
    /// <summary>
    /// 标定文件服务，负责标定数据的文件读写操作
    /// 不依赖任何板卡或设备信息，只处理JSON文件的序列化/反序列化
    /// </summary>
    public class CalibrationFileService
    {

        /// <summary>
        /// 从标定文件读取所有校准记录
        /// </summary>
        /// <param name="filePath">文件路径</param>
        /// <returns>校准记录列表，文件不存在或格式错误返回空列表</returns>
        public List<ChannelCalibrationRecord> LoadCalibrationRecords(string filePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                    return new List<ChannelCalibrationRecord>();

                var json = File.ReadAllText(filePath);
                if (string.IsNullOrWhiteSpace(json))
                    return new List<ChannelCalibrationRecord>();

                var records = JsonConvert.DeserializeObject<List<ChannelCalibrationRecord>>(json);
                return records ?? new List<ChannelCalibrationRecord>();
            }
            catch (Exception ex)
            {
                // 记录错误但不抛出异常，返回空列表
                System.Diagnostics.Debug.WriteLine($"加载标定文件失败: {ex.Message}");
                return new List<ChannelCalibrationRecord>();
            }
        }

        /// <summary>
        /// 将标定记录保存到文件
        /// </summary>
        /// <param name="filePath">文件路径</param>
        /// <param name="records">标定记录列表</param>
        /// <returns>保存是否成功</returns>
        public bool SaveCalibrationRecords(string filePath, List<ChannelCalibrationRecord> records)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(filePath) || records == null)
                    return false;

                var dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var json = JsonConvert.SerializeObject(records, Formatting.Indented);
                File.WriteAllText(filePath, json);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"保存标定文件失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 验证标定数据文件的完整性
        /// </summary>
        /// <param name="filePath">文件路径</param>
        /// <returns>验证结果</returns>
        public (bool IsValid, string Message, int RecordCount) ValidateCalibrationFile(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                    return (false, "文件不存在", 0);

                var fileInfo = new FileInfo(filePath);
                if (fileInfo.Length < 50) // 文件太小
                    return (false, "文件大小异常", 0);

                var records = LoadCalibrationRecords(filePath);
                if (records == null)
                    return (false, "文件格式错误", 0);

                return (true, "文件验证通过", records.Count);
            }
            catch (Exception ex)
            {
                return (false, $"验证失败: {ex.Message}", 0);
            }
        }
    }

    /// <summary>
    /// 全局标定服务
    /// 提供项目级别的标定数据访问，用于物理层信号调理
    /// </summary>
    public class CalibrationService
    {
        private static CalibrationService _instance;
        private static readonly object _lock = new object();

        /// <summary>
        /// 单例实例
        /// </summary>
        public static CalibrationService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new CalibrationService();
                        }
                    }
                }
                return _instance;
            }
        }

        /// <summary>
        /// 标定参数存储 (ChannelAddress -> (Slope, Intercept, IsCalibrated))
        /// </summary>
        private readonly Dictionary<string, (double Slope, double Intercept, bool IsCalibrated)> _calibrationData = new Dictionary<string, (double, double, bool)>();

        /// <summary>
        /// 更新标定数据
        /// </summary>
        /// <param name="calibrationRecords">标定记录字典</param>
        public void UpdateCalibrationData(Dictionary<string, ChannelCalibrationRecord> calibrationRecords)
        {
            lock (_lock)
            {
                _calibrationData.Clear();
                if (calibrationRecords != null)
                {
                    foreach (var kvp in calibrationRecords)
                    {
                        var record = kvp.Value;
                        if (record != null)
                        {
                            _calibrationData[kvp.Key] = (record.Slope, record.Intercept, record.IsCalibrated);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 获取通道的标定参数
        /// </summary>
        /// <param name="channelAddress">通道地址</param>
        /// <returns>标定参数</returns>
        public (double Slope, double Intercept, bool IsCalibrated) GetCalibrationParams(string channelAddress)
        {
            lock (_lock)
            {
                if (string.IsNullOrEmpty(channelAddress))
                {
                    return (1.0, 0.0, false); // 默认值：无标定
                }

                if (_calibrationData.TryGetValue(channelAddress, out var param))
                {
                    return param;
                }
                return (1.0, 0.0, false); // 默认值：无标定
            }
        }

        /// <summary>
        /// 清除所有标定数据
        /// </summary>
        public void ClearCalibrationData()
        {
            lock (_lock)
            {
                _calibrationData.Clear();
            }
        }
    }

    // 注意：以下是保留的兼容性方法，建议逐步移除

    /// <summary>
    /// 标定数据服务（保留用于兼容性，但已废弃，建议使用CalibrationFileService）
    /// </summary>
    [Obsolete("此服务已废弃，请使用CalibrationFileService")]
    public class CalibrationDataService
    {
        private readonly IPxiChassisService _pxiChassisService;

        [Obsolete("此构造函数已废弃")]
        public CalibrationDataService(IPxiChassisService pxiChassisService)
        {
            _pxiChassisService = pxiChassisService ?? throw new ArgumentNullException(nameof(pxiChassisService));
        }

        // 保留的方法仅用于向后兼容，内部调用新的CalibrationFileService
        private readonly CalibrationFileService _fileService = new CalibrationFileService();

        /// <summary>
        /// 从标定文件读取指定板卡的校准记录（兼容性方法，已废弃）
        /// </summary>
        [Obsolete("此方法已废弃，请使用CalibrationFileService.LoadCalibrationRecords")]
        public List<ChannelCalibrationRecord> LoadCalibrationRecordsFromFile(string filePath, string chassisName, string cardName)
        {
            return _fileService.LoadCalibrationRecords(filePath);
        }

        /// <summary>
        /// 将指定板卡的校准记录写入标定文件（兼容性方法，已废弃）
        /// </summary>
        [Obsolete("此方法已废弃，请使用CalibrationFileService.SaveCalibrationRecords")]
        public void SaveCalibrationRecordsToFile(string filePath, string chassisName, string cardName, List<ChannelCalibrationRecord> records)
        {
            _fileService.SaveCalibrationRecords(filePath, records);
        }

        // 其他依赖板卡的方法已移除...
    }
}

