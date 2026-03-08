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
        private static readonly string[] HydraulicSingleBoardTestItems =
        {
            "电源阻抗测试",
            "二次电源测试",
            "温度采集测试",
            "压力传感器信号采集测试",
            "压差传感器信号采集测试",
            "油量传感器信号采集测试",
            "离散量采集测试",
            "离散量输出测试"
        };

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

                ApplyFixedChassisLayoutTemplateIfNeeded(rootNode);

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
                    Tag = "液压单板",
                    Children = new ObservableCollection<ProjectItem>(HydraulicSingleBoardTestItems.Select(name => new ProjectItem
                    {
                        Name = name,
                        Icon = AppConstants.IconTasks,
                        Type = "single_board_test_item",
                        Tag = "SingleBoardTestItem"
                    }))
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

        private class FixedLayoutTemplate
        {
            [JsonProperty("version")]
            public int Version { get; set; }

            [JsonProperty("chassisTemplates")]
            public List<FixedChassisTemplate> ChassisTemplates { get; set; }
        }

        private class FixedChassisTemplate
        {
            [JsonProperty("chassisType")]
            public string ChassisType { get; set; }

            [JsonProperty("chassisName")]
            public string ChassisName { get; set; }

            [JsonProperty("gridRow")]
            public int GridRow { get; set; }

            [JsonProperty("gridColumn")]
            public int GridColumn { get; set; }

            [JsonProperty("slots")]
            public List<FixedSlotTemplate> Slots { get; set; }
        }

        private class FixedSlotTemplate
        {
            [JsonProperty("slot")]
            public string Slot { get; set; }

            [JsonProperty("device")]
            public string Device { get; set; }
        }

        private void ApplyFixedChassisLayoutTemplateIfNeeded(ProjectItem rootNode)
        {
            if (rootNode == null)
            {
                return;
            }

            rootNode.PxiChassisData ??= new ObservableCollection<ChassisModel>();
            if (rootNode.PxiChassisData.Count > 0)
            {
                return;
            }

            var template = LoadOrCreateFixedLayoutTemplate();

            if (template?.ChassisTemplates == null || template.ChassisTemplates.Count == 0)
            {
                return;
            }

            foreach (var chassisTemplate in template.ChassisTemplates.Where(t => t != null))
            {
                if (string.IsNullOrWhiteSpace(chassisTemplate.ChassisName) || string.IsNullOrWhiteSpace(chassisTemplate.ChassisType))
                {
                    continue;
                }

                var chassis = ChassisFactory.CreateChassis(chassisTemplate.ChassisType, chassisTemplate.ChassisName, chassisTemplate.GridRow, chassisTemplate.GridColumn);
                if (chassis == null)
                {
                    continue;
                }

                chassis.Devices ??= new ObservableCollection<Models.Devices.DeviceBase>();
                var chassisDevice = chassis.Devices.OfType<Models.Devices.ChassisDevice>().FirstOrDefault();
                if (chassisDevice == null)
                {
                    chassisDevice = new Models.Devices.ChassisDevice(chassis.Model ?? chassis.Name)
                    {
                        CardName = chassis.Name,
                        SlotCount = chassis.SlotCount,
                        ParentNode = $"{chassis.SlotCount}槽机箱",
                        ConnectionMethod = "详细信息",
                        Details = "详细信息",
                        DeviceType = AppConstants.DeviceTypeChassis,
                        Status = "正常",
                        IsExpanded = true,
                        Model = chassis.Model,
                        ChassisModel = chassis.Model,
                        Children = new ObservableCollection<Models.Devices.DeviceBase>()
                    };
                    chassis.Devices.Add(chassisDevice);
                }
                else
                {
                    chassisDevice.Children ??= new ObservableCollection<Models.Devices.DeviceBase>();
                }

                if (chassisTemplate.Slots != null)
                {
                    chassisDevice.Children.Clear();
                    foreach (var slot in chassisTemplate.Slots)
                    {
                        var name = slot?.Device ?? string.Empty;
                        var pos = slot?.Slot ?? string.Empty;

                        var device = Helpers.DeviceFactory.CreateDevice(name, pos);
                        if (device == null)
                        {
                            continue;
                        }

                        chassisDevice.Children.Add(device);
                    }
                }

                // 新建项目：一次性写入机箱1默认仪器清单到 proj.json。
                // 后续加载项目完全以 proj.json 为准，不在 UI 层重复补齐，避免覆盖用户配置。
                ApplyDefaultFixedDemoInstrumentsIfNeeded(chassis);

                rootNode.PxiChassisData.Add(chassis);
            }
        }

        private static void ApplyDefaultFixedDemoInstrumentsIfNeeded(ChassisModel chassis)
        {
            if (chassis == null)
            {
                return;
            }

            if (!string.Equals(chassis.Name, "PXI机箱1", StringComparison.Ordinal))
            {
                return;
            }

            chassis.Devices ??= new ObservableCollection<Models.Devices.DeviceBase>();

            int CountInstrumentByName(string name)
            {
                try
                {
                    return chassis.Devices.Count(d => d != null &&
                                                    string.Equals(d.DeviceType, "Instrument", StringComparison.Ordinal) &&
                                                    string.Equals(d.Name, name, StringComparison.Ordinal));
                }
                catch
                {
                    return 0;
                }
            }

            void EnsureInstrument(string name, int requiredCount, Action<Models.Devices.DeviceBase, int> configure = null)
            {
                if (string.IsNullOrWhiteSpace(name) || requiredCount <= 0)
                {
                    return;
                }

                var existing = CountInstrumentByName(name);
                var need = requiredCount - existing;
                for (int i = 0; i < need; i++)
                {
                    var created = Helpers.DeviceFactory.CreateDevice(name, string.Empty);
                    if (created == null)
                    {
                        continue;
                    }

                    // 某些自定义设备可能会被工厂识别为 Card（如 GenericDevice），
                    // 但在演示模板中我们将其作为独立仪器设备保存。
                    created.DeviceType = "Instrument";
                    if (string.IsNullOrWhiteSpace(created.Name))
                    {
                        created.Name = name;
                    }

                    if (string.IsNullOrWhiteSpace(created.DisplayName))
                    {
                        created.DisplayName = name;
                    }

                    if (string.IsNullOrWhiteSpace(created.ParentNode))
                    {
                        created.ParentNode = "其他自定义设备";
                    }

                    configure?.Invoke(created, existing + i);
                    chassis.Devices.Add(created);
                }
            }

            // 仪器/模块清单
            EnsureInstrument("普源 DG1032Z", 1);
            EnsureInstrument("普源 DM3068", 1);
            EnsureInstrument("是德 53220A", 1);
            EnsureInstrument("普源 DH04804", 1);

            // 三台电源默认 IP：192.168.1.15/16/17
            var ips = new[] { "192.168.1.15", "192.168.1.16", "192.168.1.17" };
            EnsureInstrument("艾德克斯 IT-N6332B", 3, (d, index) =>
            {
                if (d is Models.Devices.DeviceCategories.InstrumentDeviceBase inst)
                {
                    if (index >= 0 && index < ips.Length)
                    {
                        inst.IpAddress = ips[index];
                    }
                }
            });

            EnsureInstrument("RS422模块", 2);
            EnsureInstrument("RS232模块", 1);
        }

        private static FixedLayoutTemplate LoadOrCreateFixedLayoutTemplate()
        {
            var runtimePath = GetFixedLayoutTemplateRuntimePath();
            var candidatePaths = new List<string>();
            if (!string.IsNullOrWhiteSpace(runtimePath))
            {
                candidatePaths.Add(runtimePath);
            }

            var sourcePath = GetFixedLayoutTemplateSourcePath();
            if (!string.IsNullOrWhiteSpace(sourcePath))
            {
                candidatePaths.Add(sourcePath);
            }

            foreach (var path in candidatePaths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    continue;
                }

                try
                {
                    var json = File.ReadAllText(path);
                    var template = JsonConvert.DeserializeObject<FixedLayoutTemplate>(json);
                    if (template?.ChassisTemplates != null && template.ChassisTemplates.Count > 0)
                    {
                        return template;
                    }
                }
                catch
                {
                }
            }

            // fallback: in-code default template
            var fallbackTemplate = CreateDefaultFixedLayoutTemplate();
            TryPersistTemplateToRuntimeProjects(fallbackTemplate);
            return fallbackTemplate;
        }

        private static FixedLayoutTemplate CreateDefaultFixedLayoutTemplate()
        {
            return new FixedLayoutTemplate
            {
                Version = 1,
                ChassisTemplates = new List<FixedChassisTemplate>
                {
                    new FixedChassisTemplate
                    {
                        ChassisType = "PXIe-2722G2",
                        ChassisName = "PXI机箱1",
                        GridRow = 0,
                        GridColumn = 0,
                        Slots = new List<FixedSlotTemplate>
                        {
                            new FixedSlotTemplate { Slot = "Slot1", Device = "凌华 PXIe-3987" },
                            new FixedSlotTemplate { Slot = "Slot2", Device = "欧开 PXI-4087A" },
                            new FixedSlotTemplate { Slot = "Slot3", Device = "欧开 PXI-4087C" },
                            new FixedSlotTemplate { Slot = "Slot4", Device = "欧开 PXI-4087C" },
                            new FixedSlotTemplate { Slot = "Slot5", Device = "阿尔泰 PXI-7012" },
                            new FixedSlotTemplate { Slot = "Slot6", Device = "阿尔泰 PXI-7012" },
                            new FixedSlotTemplate { Slot = "Slot7", Device = "芒果树 MT-X532" },
                            new FixedSlotTemplate { Slot = "Slot8", Device = "阿尔泰 PXIe-4227" },
                            new FixedSlotTemplate { Slot = "Slot9", Device = "阿尔泰 PXIe-9774" },
                            new FixedSlotTemplate { Slot = "Slot10", Device = "盲板" },
                            new FixedSlotTemplate { Slot = "Slot11", Device = "阿尔泰 PXI-4004" },
                            new FixedSlotTemplate { Slot = "Slot12", Device = "简仪 PXIe-7131" },
                            new FixedSlotTemplate { Slot = "Slot13", Device = "芒果树 MT-X970" },
                            new FixedSlotTemplate { Slot = "Slot14", Device = "阿尔泰 PXI-4332" },
                            new FixedSlotTemplate { Slot = "Slot15", Device = "怀智 HZ-MIL1394B-PX1e-4N" },
                            new FixedSlotTemplate { Slot = "Slot16", Device = "盲板" },
                            new FixedSlotTemplate { Slot = "Slot17", Device = "盲板" },
                            new FixedSlotTemplate { Slot = "Slot18", Device = "盲板" }
                        }
                    },
                    new FixedChassisTemplate
                    {
                        ChassisType = "PXIe-2519G2",
                        ChassisName = "PXI机箱2",
                        GridRow = 0,
                        GridColumn = 1,
                        Slots = new List<FixedSlotTemplate>
                        {
                            new FixedSlotTemplate { Slot = "Slot1", Device = "空槽" },
                            new FixedSlotTemplate { Slot = "Slot2", Device = "空槽" },
                            new FixedSlotTemplate { Slot = "Slot3", Device = "空槽" },
                            new FixedSlotTemplate { Slot = "Slot4", Device = "空槽" },
                            new FixedSlotTemplate { Slot = "Slot5", Device = "空槽" },
                            new FixedSlotTemplate { Slot = "Slot6", Device = "空槽" },
                            new FixedSlotTemplate { Slot = "Slot7", Device = "空槽" },
                            new FixedSlotTemplate { Slot = "Slot8", Device = "空槽" },
                            new FixedSlotTemplate { Slot = "Slot9", Device = "空槽" }
                        }
                    }
                }
            };
        }

        private static void TryPersistTemplateToRuntimeProjects(FixedLayoutTemplate template)
        {
            try
            {
                var runtimePath = GetFixedLayoutTemplateRuntimePath();
                if (string.IsNullOrWhiteSpace(runtimePath))
                {
                    return;
                }

                var dir = Path.GetDirectoryName(runtimePath);
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                if (File.Exists(runtimePath))
                {
                    return;
                }

                var json = JsonConvert.SerializeObject(template, Formatting.Indented);
                File.WriteAllText(runtimePath, json);
            }
            catch
            {
            }
        }

        private static string GetFixedLayoutTemplateRuntimePath()
        {
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                return Path.Combine(baseDir, "Projects", "fixed_layout.json");
            }
            catch
            {
            }

            return null;
        }

        private static string GetFixedLayoutTemplateSourcePath()
        {
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                return Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "Projects", "fixed_layout.json"));
            }
            catch
            {
            }

            return null;
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
