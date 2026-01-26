using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MeasureControl.Constants;
using MeasureControl.Models;
using MeasureControl.Views;
using MeasureControl.Helpers;
using Newtonsoft.Json;
using System.Diagnostics;
using System.Reflection;

namespace MeasureControl.Services
{

    public class ProjectService
    {
        public string LastMatrixSwitchTestTaskName { get; private set; }
        public string LastMatrixSwitchConfigTableName { get; private set; }
        public string LastMatrixSwitchChassisName { get; private set; }

        public void SetLastMatrixSwitchContext(string testTaskName, string configTableName, string chassisName)
        {
            LastMatrixSwitchTestTaskName = testTaskName;
            LastMatrixSwitchConfigTableName = configTableName;
            LastMatrixSwitchChassisName = chassisName;
        }

        public Guid CurrentProjectId { get; private set; } = Guid.Empty;
        public ProjectItem CurrentProjectRoot { get; private set; }

        public string CurrentProjectFilePath { get; private set; }

        public List<string> GetGlobalTestTaskNames()
        {
            var result = new List<string>();
            var root = CurrentProjectRoot;
            if (root?.Children == null)
            {
                return result;
            }

            var testTasksNode = root.Children.FirstOrDefault(c => c != null && (c.Tag == "TestTasks" || c.Name == "测试任务"));
            if (testTasksNode?.Children == null)
            {
                return result;
            }

            foreach (var testTask in testTasksNode.Children.Where(c => c != null && c.Type == AppConstants.NodeTypeTestTask))
            {
                if (!string.IsNullOrWhiteSpace(testTask.Name))
                {
                    result.Add(testTask.Name);
                }
            }

            return result;
        }

        private void TryDeleteSidecarMeta(string projectPath)
        {
            try
            {
                if (string.IsNullOrEmpty(projectPath)) return;
                var metaPath = projectPath + ".meta.json";
                if (File.Exists(metaPath))
                {
                    File.Delete(metaPath);
                    Debug.WriteLine($"[Project] Deleted legacy sidecar meta: {metaPath}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Project] Delete legacy sidecar meta failed: {ex.Message}");
            }
        }

        public ProjectItem CreateNewProject(string projectName, string projectPath)
        {
            try
            {
                // 不再生成或读取任何项目 GUID 或旁置元数据文件
                CurrentProjectId = Guid.Empty;
                CurrentProjectFilePath = projectPath;
                TryDeleteSidecarMeta(projectPath);
                Debug.WriteLine($"[Project] ===== CreateNewProject START =====");
                Debug.WriteLine($"[Project] Name='{projectName}' Path='{projectPath}'");

                Debug.WriteLine($"[Project] Creating rootNode...");
                var rootNode = new ProjectItem
                {
                    Name = projectName,
                    Icon = AppConstants.IconFolder,
                    Type = AppConstants.NodeTypeRoot,
                    Tag = "Root"
                };
                Debug.WriteLine($"[Project] rootNode created. Children={rootNode.Children?.Count ?? -1}, Children is null: {rootNode.Children == null}");

                CurrentProjectRoot = rootNode;

                Debug.WriteLine($"[Project] Creating HardwareConfig...");
                var HardwareConfig = new ProjectItem
                {
                    Name = AppConstants.NodeNameHardwareConfig,
                    Icon = AppConstants.IconHardware,
                    Type = AppConstants.NodeTypeHardwareConfig,
                    Tag = "HardwareConfig"
                };
                Debug.WriteLine($"[Project] HardwareConfig created. Children={HardwareConfig.Children?.Count ?? -1}");
                
                Debug.WriteLine($"[Project] Creating DeviceNetwork child...");
                var deviceNetwork = new ProjectItem
                {
                    Name = AppConstants.NodeNameDeviceNetwork,
                    Icon = AppConstants.IconHardware,
                    Type = AppConstants.NodeTypeDevice,
                    Tag = "Device"
                };
                Debug.WriteLine($"[Project] DeviceNetwork created. Children={deviceNetwork.Children?.Count ?? -1}");
                HardwareConfig.Children.Add(deviceNetwork);

                Debug.WriteLine($"[Project] Creating dataAnalysis...");
                var dataAnalysis = new ProjectItem
                {
                    Name = AppConstants.NodeNameDataAnalysis,
                    Icon = AppConstants.IconMonitor,
                    Type = AppConstants.NodeTypeDataAnalysis,
                    Tag = "DataAnalysis"
                };
                Debug.WriteLine($"[Project] dataAnalysis created. Children={dataAnalysis.Children?.Count ?? -1}");

                Debug.WriteLine($"[Project] Creating databaseManagement...");
                var databaseManagement = new ProjectItem
                {
                    Name = AppConstants.NodeNameDatabaseManagement,
                    Icon = AppConstants.IconDatabase,
                    Type = AppConstants.NodeTypeDatabaseManagement,
                    Tag = "DatabaseManagement"
                };
                Debug.WriteLine($"[Project] databaseManagement created. Children={databaseManagement.Children?.Count ?? -1}");

                Debug.WriteLine($"[Project] Creating task database child...");
                var taskDatabase = new ProjectItem
                {
                    Name = "任务数据库",
                    Icon = AppConstants.IconDatabase,
                    Type = "task_database",
                    Tag = "TaskDatabase"
                };
                Debug.WriteLine($"[Project] taskDatabase created. Children={taskDatabase.Children?.Count ?? -1}");
                databaseManagement.Children.Add(taskDatabase);
                
                Debug.WriteLine($"[Project] Creating test database child...");
                var testDatabase = new ProjectItem
                {
                    Name = "测试数据库",
                    Icon = AppConstants.IconDatabase,
                    Type = "test_database",
                    Tag = "TestDatabase"
                };
                Debug.WriteLine($"[Project] testDatabase created. Children={testDatabase.Children?.Count ?? -1}");
                databaseManagement.Children.Add(testDatabase);

                var testTasks = new ProjectItem
                {
                    Name = "测试任务",
                    Icon = AppConstants.IconTasks,
                    Type = "test_tasks",
                    Tag = "TestTasks"
                };
                testTasks.Children.Add(new ProjectItem
                {
                    Name = "空气单板",
                    Icon = AppConstants.IconTasks,
                    Type = AppConstants.NodeTypeTestTask,
                    Tag = "空气单板"
                });
                testTasks.Children.Add(new ProjectItem
                {
                    Name = "惰化单板",
                    Icon = AppConstants.IconTasks,
                    Type = AppConstants.NodeTypeTestTask,
                    Tag = "惰化单板"
                });
                testTasks.Children.Add(new ProjectItem
                {
                    Name = "加放油单板",
                    Icon = AppConstants.IconTasks,
                    Type = AppConstants.NodeTypeTestTask,
                    Tag = "加放油单板"
                });
                testTasks.Children.Add(new ProjectItem
                {
                    Name = "液压单板",
                    Icon = AppConstants.IconTasks,
                    Type = AppConstants.NodeTypeTestTask,
                    Tag = "液压单板"
                });

                Debug.WriteLine($"[Project] Adding children to rootNode...");
                rootNode.Children.Add(HardwareConfig);
                rootNode.Children.Add(dataAnalysis);
                rootNode.Children.Add(databaseManagement);
                rootNode.Children.Add(testTasks);
                Debug.WriteLine($"[Project] rootNode now has {rootNode.Children.Count} children");

                // 确保所有属性都已正确初始化
                Debug.WriteLine($"[Project] Ensuring all properties are initialized...");
                EnsureProjectItemProperties(rootNode);
                Debug.WriteLine($"[Project] Properties ensured. rootNode.Children={rootNode.Children?.Count ?? -1}");

                // 验证所有必需的属性
                Debug.WriteLine($"[Project] Validating rootNode properties...");
                ValidateProjectItem(rootNode, "rootNode");
                
                Debug.WriteLine($"[Project] ===== CreateNewProject SUCCESS =====");
                return rootNode;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Project] ===== CreateNewProject ERROR =====");
                Debug.WriteLine($"[Project] Exception Type: {ex.GetType().FullName}");
                Debug.WriteLine($"[Project] Exception: {ex.Message}");
                Debug.WriteLine($"[Project] StackTrace: {ex.StackTrace}");
                if (ex.InnerException != null)
                {
                    Debug.WriteLine($"[Project] InnerException Type: {ex.InnerException.GetType().FullName}");
                    Debug.WriteLine($"[Project] InnerException: {ex.InnerException.Message}");
                    Debug.WriteLine($"[Project] InnerException StackTrace: {ex.InnerException.StackTrace}");
                }
                throw new Exception($"创建项目失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 验证 ProjectItem 的所有必需属性
        /// </summary>
        private void ValidateProjectItem(ProjectItem item, string itemName)
        {
            if (item == null)
            {
                Debug.WriteLine($"[Project] VALIDATION ERROR: {itemName} is null!");
                return;
            }

            Debug.WriteLine($"[Project] Validating {itemName}: Name='{item.Name}', Type='{item.Type}'");
            
            if (item.Children == null)
            {
                Debug.WriteLine($"[Project] VALIDATION ERROR: {itemName}.Children is null!");
            }
            else
            {
                Debug.WriteLine($"[Project] {itemName}.Children is OK, Count={item.Children.Count}");
            }

            if (item.PxiChassisData == null)
            {
                Debug.WriteLine($"[Project] VALIDATION WARNING: {itemName}.PxiChassisData is null!");
            }

            if (item.ChassisConnections == null)
            {
                Debug.WriteLine($"[Project] VALIDATION WARNING: {itemName}.ChassisConnections is null!");
            }

            if (item.ConnectionLines == null)
            {
                Debug.WriteLine($"[Project] VALIDATION WARNING: {itemName}.ConnectionLines is null!");
            }

            if (item.ChannelTabelItems == null)
            {
                Debug.WriteLine($"[Project] VALIDATION WARNING: {itemName}.ChannelTabelItems is null!");
            }

            if (item.SignalTabelItems == null)
            {
                Debug.WriteLine($"[Project] VALIDATION WARNING: {itemName}.SignalTabelItems is null!");
            }

            if (item.IcdTabelItems == null)
            {
                Debug.WriteLine($"[Project] VALIDATION WARNING: {itemName}.IcdTabelItems is null!");
            }

            if (item.IcdMappingItems == null)
            {
                Debug.WriteLine($"[Project] VALIDATION WARNING: {itemName}.IcdMappingItems is null!");
            }

            // 递归验证子项
            if (item.Children != null)
            {
                for (int i = 0; i < item.Children.Count; i++)
                {
                    ValidateProjectItem(item.Children[i], $"{itemName}.Children[{i}]");
                }
            }
        }

        public void SaveProject(ProjectItem project, string filePath)
        {
            try
            {
                Debug.WriteLine($"[Project] ===== SaveProject START =====");
                Debug.WriteLine($"[Project] Path='{filePath}'");

                CurrentProjectFilePath = filePath;
                
                // 验证项目对象
                if (project == null)
                {
                    Debug.WriteLine($"[Project] ERROR: project is null!");
                    throw new ArgumentNullException(nameof(project), "项目对象不能为空");
                }

                // 确保所有属性都已正确初始化
                Debug.WriteLine($"[Project] Ensuring all properties are initialized before save...");
                EnsureProjectItemProperties(project);
                Debug.WriteLine($"[Project] Properties ensured.");

                // 验证所有必需的属性
                Debug.WriteLine($"[Project] Validating project before save...");
                ValidateProjectItem(project, "project");

                // 不再读写旁置元数据，清理历史文件
                TryDeleteSidecarMeta(filePath);

                Debug.WriteLine($"[Project] Serializing project to JSON...");
                var settings = new JsonSerializerSettings
                {
                    Formatting = Formatting.Indented,
                    Converters = { 
                        new DeviceBaseJsonConverter(),
                        new ChassisModelJsonConverter(),
                        new CardConfigDataJsonConverter()
                    },
                    ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                    NullValueHandling = NullValueHandling.Ignore
                };
                string json = JsonConvert.SerializeObject(project, settings);
                Debug.WriteLine($"[Project] JSON serialized. Length={json.Length}");

                Debug.WriteLine($"[Project] Writing to file...");
                File.WriteAllText(filePath, json);
                Debug.WriteLine($"[Project] ===== SaveProject SUCCESS =====");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Project] ===== SaveProject ERROR =====");
                Debug.WriteLine($"[Project] Exception: {ex.Message}");
                Debug.WriteLine($"[Project] StackTrace: {ex.StackTrace}");
                throw new Exception($"保存项目失败: {ex.Message}", ex);
            }
        }

        public ProjectItem CreateTestTask(ProjectItem taskConfigNode, string customName = null)
        {
            if (taskConfigNode == null || taskConfigNode.Type != AppConstants.NodeTypeTaskConfig)
                return null;

            string taskName;
            if (!string.IsNullOrEmpty(customName))
            {
                // 使用自定义名称
                taskName = customName;
            }
            else
            {
                // 计算下一个测试任务的序号，确保名称唯一
                int nextTaskNumber = GetNextTestTaskNumber(taskConfigNode);
                taskName = $"{AppConstants.TestTaskNamePrefix}{nextTaskNumber}";

                // 确保名称唯一性
                while (IsTestTaskNameExists(taskConfigNode, taskName))
                {
                    nextTaskNumber++;
                    taskName = $"{AppConstants.TestTaskNamePrefix}{nextTaskNumber}";
                }
            }

            var testTask = new ProjectItem
            {
                Name = taskName,
                Icon = AppConstants.IconTasks,
                Type = AppConstants.NodeTypeTestTask,
                Tag = "TestTask"
            };

            // 创建7个固定的子节点
            var channelConfig = new ProjectItem
            {
                Name = "通道配置",
                Icon = AppConstants.IconHardware,
                Type = "channel_config",
                Tag = "ChannelConfig"
            };

            var icdConfig = new ProjectItem
            {
                Name = "ICD配置",
                Icon = AppConstants.IconTabel,
                Type = "icd_config",
                Tag = "IcdConfig"
            };

            var signalConfig = new ProjectItem
            {
                Name = "信号配置",
                Icon = AppConstants.IconSignal,
                Type = "signal_config",
                Tag = "SignalConfig"
            };

            var testUI = new ProjectItem
            {
                Name = "测试界面",
                Icon = AppConstants.IconHand,
                Type = "test_ui",
                Tag = "TestUI"
            };

            var testSequence = new ProjectItem
            {
                Name = "测试序列",
                Icon = AppConstants.IconTest,
                Type = "test_sequence",
                Tag = "TestSequence"
            };

            var testScript = new ProjectItem
            {
                Name = "测试脚本",
                Icon = AppConstants.IconTestScript,
                Type = "test_script",
                Tag = "TestScript"
            };

            var report = new ProjectItem
            {
                Name = "报表",
                Icon = AppConstants.IconFileRed,
                Type = "report",
                Tag = "Report"
            };

            var monitor = new ProjectItem
            {
                Name = "监控与回放",
                Icon = AppConstants.IconMonitor,
                Type = "monitor",
                Tag = "Monitor"
            };

            // 添加子节点
            testTask.Children.Add(channelConfig);
            testTask.Children.Add(icdConfig);
            testTask.Children.Add(signalConfig);
            testTask.Children.Add(testUI);
            testTask.Children.Add(testSequence);
            testTask.Children.Add(testScript);
            testTask.Children.Add(report);
            testTask.Children.Add(monitor);

            return testTask;
        }

        public int GetNextTestTaskNumber(ProjectItem taskConfigNode)
        {
            int maxNumber = 0;
            
            if (taskConfigNode?.Children != null)
            {
                foreach (var child in taskConfigNode.Children)
                {
                    if (child.Type == AppConstants.NodeTypeTestTask && child.Name.StartsWith(AppConstants.TestTaskNamePrefix))
                    {
                        // 提取数字部分
                        string numberPart = child.Name.Substring(AppConstants.TestTaskNamePrefix.Length);
                        if (int.TryParse(numberPart, out int number))
                        {
                            maxNumber = Math.Max(maxNumber, number);
                        }
                    }
                }
            }
            
            return maxNumber + 1;
        }

        public bool IsTestTaskNameExists(ProjectItem taskConfigNode, string taskName)
        {
            if (taskConfigNode?.Children == null) return false;
            
            return taskConfigNode.Children.Any(child => 
                child.Type == AppConstants.NodeTypeTestTask && 
                child.Name.Equals(taskName, StringComparison.OrdinalIgnoreCase));
        }

        public bool RenameTestTask(ProjectItem testTask, string newName, ProjectItem taskConfigNode)
        {
            if (testTask == null || testTask.Type != AppConstants.NodeTypeTestTask || string.IsNullOrWhiteSpace(newName))
                return false;

            // 检查新名称是否已存在（排除当前任务）
            if (IsTestTaskNameExists(taskConfigNode, newName) && !testTask.Name.Equals(newName, StringComparison.OrdinalIgnoreCase))
            {
                return false; // 名称已存在
            }

            testTask.Name = newName;
            return true;
        }

        public void DeleteTestTask(ProjectItem taskConfigNode, ProjectItem testTask)
        {
            if (taskConfigNode != null && testTask != null && testTask.Type == AppConstants.NodeTypeTestTask)
            {
                taskConfigNode.Children.Remove(testTask);
            }
        }

        private bool IsChassisBoundTestTask(ProjectItem testTask)
        {
            if (testTask == null)
            {
                return false;
            }

            var root = CurrentProjectRoot;
            if (root?.Children == null)
            {
                return false;
            }

            foreach (var chassisNode in root.Children.Where(c => c != null && c.Type == AppConstants.NodeTypePxiChassis))
            {
                if (chassisNode.Children == null) continue;
                var taskConfigNode = chassisNode.Children.FirstOrDefault(c => c != null && c.Type == AppConstants.NodeTypeTaskConfig);
                if (taskConfigNode?.Children == null) continue;

                if (taskConfigNode.Children.Any(t => ReferenceEquals(t, testTask)))
                {
                    return true;
                }
            }

            return false;
        }

        public ProjectItem LoadProject(string filePath)
        {
            try
            {
                // 不再使用 GUID 旁置文件，尝试清理历史文件
                TryDeleteSidecarMeta(filePath);
                CurrentProjectId = Guid.Empty;
                CurrentProjectFilePath = filePath;
                Debug.WriteLine($"[Project] LoadProject Path='{filePath}'");

                string json = File.ReadAllText(filePath);
                var settings = new JsonSerializerSettings
                {
                    Converters = { 
                        new DeviceBaseJsonConverter(),
                        new ChassisModelJsonConverter(),
                        new CardConfigDataJsonConverter()
                    },
                    // 确保JSON数据能够替换构造函数初始化的字典和集合
                    ObjectCreationHandling = ObjectCreationHandling.Replace,
                    // 允许null值
                    NullValueHandling = NullValueHandling.Ignore,
                    // 不忽略默认值（确保字典和集合能够被正确反序列化）
                    DefaultValueHandling = DefaultValueHandling.Include
                };
                var project = JsonConvert.DeserializeObject<ProjectItem>(json, settings);
                
                // 确保所有必需的属性都有默认值
                CurrentProjectRoot = project;
                EnsureProjectItemProperties(project);
                return project;
            }
            catch (Exception ex)
            {
                throw new Exception($"加载项目失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 确保 ProjectItem 及其子项的所有必需属性都有默认值
        /// </summary>
        public void EnsureProjectItemProperties(ProjectItem item)
        {
            if (item == null) return;

            // 确保 Children 不为 null
            if (item.Children == null)
            {
                item.Children = new ObservableCollection<ProjectItem>();
            }

            // 确保其他集合属性不为 null
            if (item.PxiChassisData == null)
            {
                item.PxiChassisData = new ObservableCollection<ChassisModel>();
            }

            if (item.ChassisConnections == null)
            {
                item.ChassisConnections = new ObservableCollection<ChassisConnection>();
            }

            if (item.ConnectionLines == null)
            {
                item.ConnectionLines = new ObservableCollection<ConnectionLine>();
            }

            if (item.ChannelTabelItems == null)
            {
                item.ChannelTabelItems = new Dictionary<string, List<ChannelTabelItem>>();
            }

            if (item.SignalTabelItems == null)
            {
                item.SignalTabelItems = new Dictionary<string, List<SignalConfigItem>>();
            }

            if (item.IcdTabelItems == null)
            {
                item.IcdTabelItems = new Dictionary<string, List<IcdFrameItem>>();
            }

            if (item.IcdMappingItems == null)
            {
                item.IcdMappingItems = new Dictionary<string, List<IcdMappingItem>>();
            }

            if (item.CalibrationRecords == null)
            {
                item.CalibrationRecords = new Dictionary<string, ChannelCalibrationRecord>();
            }

            // 递归处理子项
            foreach (var child in item.Children)
            {
                EnsureProjectItemProperties(child);

                if (child.Type == AppConstants.NodeTypeTestTask)
                {
                    if (IsChassisBoundTestTask(child))
                    {
                        _ = EnsureTestTaskDefaultTabels(child);
                    }
                    else
                    {
                        child.Children?.Clear();
                    }
                }
            }
        }

        /// <summary>
        /// 确保测试任务节点具备ICD节点以及默认配置表
        /// </summary>
        public bool EnsureTestTaskDefaultTabels(ProjectItem testTask)
        {
            if (testTask == null)
            {
                return false;
            }

            bool changed = false;
            testTask.Children ??= new ObservableCollection<ProjectItem>();

            var channelNode = testTask.Children.FirstOrDefault(c => c.Type == "channel_config");
            if (channelNode == null)
            {
                channelNode = new ProjectItem
                {
                    Name = "通道配置",
                    Icon = AppConstants.IconHardware,
                    Type = "channel_config",
                    Tag = "ChannelConfig",
                    Children = new ObservableCollection<ProjectItem>()
                };
                testTask.Children.Insert(0, channelNode);
                changed = true;
            }
            channelNode.Children ??= new ObservableCollection<ProjectItem>();

            var icdNode = testTask.Children.FirstOrDefault(c => c.Type == "icd_config");
            if (icdNode == null)
            {
                icdNode = new ProjectItem
                {
                    Name = "ICD配置",
                    Icon = AppConstants.IconTabel,
                    Type = "icd_config",
                    Tag = "IcdConfig",
                    Children = new ObservableCollection<ProjectItem>()
                };

                var channelIndex = testTask.Children.IndexOf(channelNode);
                int insertIndex = channelIndex >= 0 ? channelIndex + 1 : testTask.Children.Count;
                testTask.Children.Insert(insertIndex, icdNode);
                changed = true;
            }
            icdNode.Children ??= new ObservableCollection<ProjectItem>();

            var icdMappingNode = testTask.Children.FirstOrDefault(c => c.Type == "icd_mapping");
            if (icdMappingNode == null)
            {
                icdMappingNode = new ProjectItem
                {
                    Name = "ICD映射",
                    Icon = AppConstants.IconMapping,
                    Type = "icd_mapping",
                    Tag = "IcdMapping",
                    Children = new ObservableCollection<ProjectItem>()
                };

                var icdIndex = testTask.Children.IndexOf(icdNode);
                int insertIndex = icdIndex >= 0 ? icdIndex + 1 : testTask.Children.Count;
                testTask.Children.Insert(insertIndex, icdMappingNode);
                changed = true;
            }
            icdMappingNode.Children ??= new ObservableCollection<ProjectItem>();

            var signalNode = testTask.Children.FirstOrDefault(c => c.Type == "signal_config");
            if (signalNode == null)
            {
                signalNode = new ProjectItem
                {
                    Name = "信号配置",
                    Icon = AppConstants.IconSignal,
                    Type = "signal_config",
                    Tag = "SignalConfig",
                    Children = new ObservableCollection<ProjectItem>()
                };
                testTask.Children.Add(signalNode);
                changed = true;
            }
            signalNode.Children ??= new ObservableCollection<ProjectItem>();
            foreach (var legacyTabel in signalNode.Children.Where(c => c.Type == "signal_config_tabel"))
            {
                legacyTabel.Type = "signal_config_tabel";
            }

            foreach (var commTabel in signalNode.Children.Where(c => c.Type == "communicating_signal_config_tabel"))
            {
                if (commTabel.Icon != AppConstants.IconSignal)
                {
                    commTabel.Icon = AppConstants.IconSignal;
                    changed = true;
                }
            }

            var desiredOrder = new List<ProjectItem> { channelNode, icdNode, icdMappingNode, signalNode };
            var orderedNodes = desiredOrder.Where(n => n != null).ToList();
            orderedNodes.AddRange(testTask.Children.Except(orderedNodes).ToList());
            if (!IsSameOrder(testTask.Children, orderedNodes))
            {
                testTask.Children.Clear();
                foreach (var node in orderedNodes)
                {
                    testTask.Children.Add(node);
                }
                changed = true;
            }

            changed |= EnsureConfigTabelExists(channelNode, "通道配置表1", "channel_config_tabel", AppConstants.IconHardware, "ChannelConfigTabel");
            if (!signalNode.Children.Any(c => c.Type == "signal_config_tabel"))
            {
                changed |= EnsureConfigTabelExists(signalNode, "变量表1", "signal_config_tabel", AppConstants.IconNonCommunicate, "SignalConfigTabel");
            }
            changed |= EnsureSignalTabelOrder(signalNode);
            // 不再创建默认ICD配置表

            return changed;
        }

        /// <summary>
        /// 确保项目根节点下包含各默认配置表的数据字典
        /// </summary>
        public void EnsureDefaultTabelData(ProjectItem rootProject, string testTaskName)
        {
            if (rootProject == null || string.IsNullOrWhiteSpace(testTaskName))
            {
                return;
            }

            rootProject.ChannelTabelItems ??= new Dictionary<string, List<ChannelTabelItem>>();
            rootProject.SignalTabelItems ??= new Dictionary<string, List<SignalConfigItem>>();
            rootProject.IcdMappingItems ??= new Dictionary<string, List<IcdMappingItem>>();
            rootProject.IcdTabelItems ??= new Dictionary<string, List<IcdFrameItem>>();

            var channelKey = $"{testTaskName}/通道配置表1";
            if (!rootProject.ChannelTabelItems.ContainsKey(channelKey))
            {
                rootProject.ChannelTabelItems[channelKey] = new List<ChannelTabelItem>();
            }

            var signalKey = $"{testTaskName}/变量表1";
            if (!rootProject.SignalTabelItems.ContainsKey(signalKey))
            {
                rootProject.SignalTabelItems[signalKey] = new List<SignalConfigItem>();
            }

            // 不再创建默认ICD配置表数据字典
        }

        private bool EnsureConfigTabelExists(ProjectItem parentNode, string tabelName, string type, string icon, string tag)
        {
            if (parentNode?.Children == null)
            {
                return false;
            }

            if (parentNode.Children.Any(c => c.Name == tabelName))
            {
                return false;
            }

            parentNode.Children.Add(new ProjectItem
            {
                Name = tabelName,
                Icon = icon,
                Type = type,
                Tag = tag
            });

            return true;
        }

        private bool EnsureSignalTabelOrder(ProjectItem signalNode)
        {
            if (signalNode?.Children == null || signalNode.Children.Count == 0)
            {
                return false;
            }

            var nonCommunicating = signalNode.Children
                .Where(c => c.Type == "signal_config_tabel")
                .ToList();
            var communicating = signalNode.Children
                .Where(c => c.Type == "communicating_signal_config_tabel")
                .ToList();
            var others = signalNode.Children
                .Where(c => c.Type != "signal_config_tabel" &&
                            c.Type != "communicating_signal_config_tabel")
                .ToList();

            var ordered = new List<ProjectItem>();
            ordered.AddRange(nonCommunicating);
            ordered.AddRange(communicating);
            ordered.AddRange(others);

            if (IsSameOrder(signalNode.Children, ordered))
            {
                return false;
            }

            signalNode.Children.Clear();
            foreach (var item in ordered)
            {
                signalNode.Children.Add(item);
            }

            return true;
        }

        private static bool IsSameOrder(IList<ProjectItem> original, IList<ProjectItem> ordered)
        {
            if (original.Count != ordered.Count)
            {
                return false;
            }

            for (int i = 0; i < original.Count; i++)
            {
                if (!ReferenceEquals(original[i], ordered[i]))
                {
                    return false;
                }
            }

            return true;
        }

        public void ClearCurrentProjectRoot()
        {
            CurrentProjectRoot = null;
        }
    }
}
