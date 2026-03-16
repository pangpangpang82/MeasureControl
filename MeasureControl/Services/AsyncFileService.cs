using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using MeasureControl.Models;
using MeasureControl.Helpers;
using Newtonsoft.Json;

namespace MeasureControl.Services
{
    /// <summary>
    /// 异步文件服务 - 提供异步的文件操作功能
    /// </summary>
    public class AsyncFileService
    {
        #region Constants

        private const int BufferSize = 4096;
        private const int MaxRetryAttempts = 3;
        private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(100);

        #endregion

        #region Public Methods

        /// <summary>
        /// 异步保存项目
        /// </summary>
        /// <param name="project">项目对象</param>
        /// <param name="filePath">文件路径</param>
        /// <param name="progress">进度报告</param>
        /// <returns>保存任务</returns>
        public async Task SaveProjectAsync(ProjectItem project, string filePath, IProgress<FileOperationProgress> progress = null)
        {
            if (project == null)
                throw new ArgumentNullException(nameof(project));
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentException("文件路径不能为空", nameof(filePath));

            progress?.Report(new FileOperationProgress { Operation = "开始保存项目", Progress = 0 });
            Debug.WriteLine($"[File] SaveProjectAsync path='{filePath}'");

            try
            {
                // 序列化项目
                progress?.Report(new FileOperationProgress { Operation = "序列化项目数据", Progress = 25 });
                var json = await SerializeProjectAsync(project);

                // 写入文件
                progress?.Report(new FileOperationProgress { Operation = "写入文件", Progress = 50 });
                await WriteTextToFileAsync(filePath, json);

                progress?.Report(new FileOperationProgress { Operation = "保存完成", Progress = 100 });
                Debug.WriteLine($"[File] SaveProjectAsync done path='{filePath}'");
            }
            catch (Exception ex)
            {
                progress?.Report(new FileOperationProgress { Operation = $"保存失败: {ex.Message}", Progress = 0 });
                throw new Exception($"保存项目失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 异步加载项目
        /// </summary>
        /// <param name="filePath">文件路径</param>
        /// <param name="progress">进度报告</param>
        /// <returns>项目对象</returns>
        public async Task<ProjectItem> LoadProjectAsync(string filePath, IProgress<FileOperationProgress> progress = null)
        {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentException("文件路径不能为空", nameof(filePath));

            progress?.Report(new FileOperationProgress { Operation = "开始加载项目", Progress = 0 });
            Debug.WriteLine($"[File] LoadProjectAsync path='{filePath}'");

            try
            {
                // 检查文件是否存在
                if (!File.Exists(filePath))
                {
                    throw new FileNotFoundException($"项目文件不存在: {filePath}");
                }

                // 读取文件
                progress?.Report(new FileOperationProgress { Operation = "读取文件", Progress = 25 });
                var json = await ReadTextFromFileAsync(filePath);

                // 反序列化项目
                progress?.Report(new FileOperationProgress { Operation = "反序列化项目数据", Progress = 50 });
                var project = await DeserializeProjectAsync(json);

                progress?.Report(new FileOperationProgress { Operation = "加载完成", Progress = 100 });
                Debug.WriteLine($"[File] LoadProjectAsync done path='{filePath}'");
                return project;
            }
            catch (Exception ex)
            {
                progress?.Report(new FileOperationProgress { Operation = $"加载失败: {ex.Message}", Progress = 0 });
                throw new Exception($"加载项目失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 异步保存机箱数据
        /// </summary>
        /// <param name="chassisData">机箱数据</param>
        /// <param name="filePath">文件路径</param>
        /// <param name="progress">进度报告</param>
        /// <returns>保存任务</returns>
        public async Task SaveChassisDataAsync(System.Collections.ObjectModel.ObservableCollection<ChassisModel> chassisData, string filePath, IProgress<FileOperationProgress> progress = null)
        {
            if (chassisData == null)
                throw new ArgumentNullException(nameof(chassisData));
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentException("文件路径不能为空", nameof(filePath));

            progress?.Report(new FileOperationProgress { Operation = "开始保存机箱数据", Progress = 0 });

            try
            {
                // 序列化机箱数据
                progress?.Report(new FileOperationProgress { Operation = "序列化机箱数据", Progress = 25 });
                var json = await SerializeChassisDataAsync(chassisData);

                // 写入文件
                progress?.Report(new FileOperationProgress { Operation = "写入文件", Progress = 50 });
                await WriteTextToFileAsync(filePath, json);

                progress?.Report(new FileOperationProgress { Operation = "保存完成", Progress = 100 });
            }
            catch (Exception ex)
            {
                progress?.Report(new FileOperationProgress { Operation = $"保存失败: {ex.Message}", Progress = 0 });
                throw new Exception($"保存机箱数据失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 异步加载机箱数据
        /// </summary>
        /// <param name="filePath">文件路径</param>
        /// <param name="progress">进度报告</param>
        /// <returns>机箱数据</returns>
        public async Task<System.Collections.ObjectModel.ObservableCollection<ChassisModel>> LoadChassisDataAsync(string filePath, IProgress<FileOperationProgress> progress = null)
        {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentException("文件路径不能为空", nameof(filePath));

            progress?.Report(new FileOperationProgress { Operation = "开始加载机箱数据", Progress = 0 });

            try
            {
                // 检查文件是否存在
                if (!File.Exists(filePath))
                {
                    return new System.Collections.ObjectModel.ObservableCollection<ChassisModel>();
                }

                // 读取文件
                progress?.Report(new FileOperationProgress { Operation = "读取文件", Progress = 25 });
                var json = await ReadTextFromFileAsync(filePath);

                // 反序列化机箱数据
                progress?.Report(new FileOperationProgress { Operation = "反序列化机箱数据", Progress = 50 });
                var chassisData = await DeserializeChassisDataAsync(json);

                progress?.Report(new FileOperationProgress { Operation = "加载完成", Progress = 100 });
                return chassisData;
            }
            catch (Exception ex)
            {
                progress?.Report(new FileOperationProgress { Operation = $"加载失败: {ex.Message}", Progress = 0 });
                throw new Exception($"加载机箱数据失败: {ex.Message}", ex);
            }
        }

        #endregion

        #region Private Methods

        private async Task<string> SerializeProjectAsync(ProjectItem project)
        {
            return await Task.Run(() =>
            {
                try
                {
                    return JsonConvert.SerializeObject(project, Formatting.Indented);
                }
                catch (Exception ex)
                {
                    throw new Exception($"序列化项目失败: {ex.Message}", ex);
                }
            });
        }

        private async Task<ProjectItem> DeserializeProjectAsync(string json)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var settings = new JsonSerializerSettings
                    {
                        Converters = { 
                            new DeviceBaseJsonConverter(),
                            new ChassisModelJsonConverter()
                        },
                        // 确保JSON数据能够替换构造函数初始化的字典和集合
                        ObjectCreationHandling = ObjectCreationHandling.Replace,
                        // 允许null值
                        NullValueHandling = NullValueHandling.Ignore,
                        // 不忽略默认值（确保字典和集合能够被正确反序列化）
                        DefaultValueHandling = DefaultValueHandling.Include
                    };
                    return JsonConvert.DeserializeObject<ProjectItem>(json, settings);
                }
                catch (Exception ex)
                {
                    throw new Exception($"反序列化项目失败: {ex.Message}", ex);
                }
            });
        }

        private async Task<string> SerializeChassisDataAsync(System.Collections.ObjectModel.ObservableCollection<ChassisModel> chassisData)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var settings = new JsonSerializerSettings
                    {
                        Formatting = Formatting.Indented,
                        Converters = { 
                            new DeviceBaseJsonConverter(),
                            new ChassisModelJsonConverter()
                        }
                    };
                    return JsonConvert.SerializeObject(chassisData, settings);
                }
                catch (Exception ex)
                {
                    throw new Exception($"序列化机箱数据失败: {ex.Message}", ex);
                }
            });
        }

        private async Task<System.Collections.ObjectModel.ObservableCollection<ChassisModel>> DeserializeChassisDataAsync(string json)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var settings = new JsonSerializerSettings
                    {
                        Converters = { 
                            new DeviceBaseJsonConverter(),
                            new ChassisModelJsonConverter()
                        }
                    };
                    var list = JsonConvert.DeserializeObject<List<ChassisModel>>(json, settings);
                    return new System.Collections.ObjectModel.ObservableCollection<ChassisModel>(list ?? new List<ChassisModel>());
                }
                catch (Exception ex)
                {
                    throw new Exception($"反序列化机箱数据失败: {ex.Message}", ex);
                }
            });
        }

        private async Task WriteTextToFileAsync(string filePath, string content)
        {
            await Task.Run(async () =>
            {
                var attempts = 0;
                while (attempts < MaxRetryAttempts)
                {
                    try
                    {
                        // 确保目录存在
                        var directory = Path.GetDirectoryName(filePath);
                        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                        {
                            Directory.CreateDirectory(directory);
                        }

                        // 写入文件
                        using (var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, true))
                        using (var writer = new StreamWriter(fileStream, Encoding.UTF8))
                        {
                            await writer.WriteAsync(content);
                            await writer.FlushAsync();
                        }
                        return;
                    }
                    catch (IOException) when (attempts < MaxRetryAttempts - 1)
                    {
                        attempts++;
                        await Task.Delay(RetryDelay);
                    }
                    catch (Exception ex)
                    {
                        throw new Exception($"写入文件失败: {ex.Message}", ex);
                    }
                }
            });
        }

        private async Task<string> ReadTextFromFileAsync(string filePath)
        {
            return await Task.Run(async () =>
            {
                var attempts = 0;
                while (attempts < MaxRetryAttempts)
                {
                    try
                    {
                        using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, true))
                        using (var reader = new StreamReader(fileStream, Encoding.UTF8))
                        {
                            return await reader.ReadToEndAsync();
                        }
                    }
                    catch (IOException) when (attempts < MaxRetryAttempts - 1)
                    {
                        attempts++;
                        await Task.Delay(RetryDelay);
                    }
                    catch (Exception ex)
                    {
                        throw new Exception($"读取文件失败: {ex.Message}", ex);
                    }
                }
                throw new Exception("读取文件失败，已达到最大重试次数");
            });
        }

        #endregion
    }

    /// <summary>
    /// 文件操作进度报告
    /// </summary>
    public class FileOperationProgress
    {
        /// <summary>
        /// 操作描述
        /// </summary>
        public string Operation { get; set; }

        /// <summary>
        /// 进度百分比 (0-100)
        /// </summary>
        public int Progress { get; set; }

        /// <summary>
        /// 是否完成
        /// </summary>
        public bool IsCompleted => Progress >= 100;

        /// <summary>
        /// 是否有错误
        /// </summary>
        public bool HasError => !string.IsNullOrEmpty(Operation) && Operation.Contains("失败");
    }
}
