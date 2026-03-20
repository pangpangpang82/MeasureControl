using System.Collections.ObjectModel;
using System.Linq;
using MeasureControl.Models;
using MeasureControl.Constants;
using MeasureControl.Helpers;

namespace MeasureControl.Services
{
    public class ProjectTreeService : IProjectTreeService
    {
        public void AddPxiChassisToProject(ObservableCollection<ProjectItem> project, ChassisModel chassis)
        {
            if (project == null || project.Count == 0 || chassis == null)
                return;

            var rootNode = project[0];
            if (rootNode == null) return;

            // 1. 添加到"硬件配置"下，与"设备与网络"同级
            var hardwareConfigItem = rootNode.Children?
                .FirstOrDefault(item => item.Name == AppConstants.NodeNameHardwareConfig);

            if (hardwareConfigItem != null)
            {
                // 检查硬件配置下是否已存在同名机箱节点
                var existingChassisInHardwareConfig = hardwareConfigItem.Children?
                    .FirstOrDefault(child => child.Name == chassis.Name && child.Type == AppConstants.NodeTypePxiChassis);

                if (existingChassisInHardwareConfig == null)
                {
                    // 创建新的机箱节点（在硬件配置下，与设备与网络同级）
                    var chassisItem = new ProjectItem
                    {
                        Name = chassis.Name,
                        Icon = AppConstants.IconHardware,
                        Type = AppConstants.NodeTypePxiChassis,
                        Children = new ObservableCollection<ProjectItem>()
                    };

                    if (hardwareConfigItem.Children == null)
                        hardwareConfigItem.Children = new ObservableCollection<ProjectItem>();

                    hardwareConfigItem.Children.Add(chassisItem);
                }
            }

            // 2. 创建顶级机箱节点（如果不存在）
            var existingTopLevelChassis = rootNode.Children?
                .FirstOrDefault(item => item.Name == chassis.Name && item.Type == AppConstants.NodeTypePxiChassis);

            if (existingTopLevelChassis == null)
            {
                // 创建顶级机箱节点，包含任务配置、监控与回放、远程接口
                var topLevelChassis = new ProjectItem
                {
                    Name = chassis.Name,
                    Icon = AppConstants.IconHardware,
                    Type = AppConstants.NodeTypePxiChassis,
                    Tag = "PXIChassis",
                    Children = new ObservableCollection<ProjectItem>()
                };

                // 创建"任务配置"子节点
                var taskConfig = new ProjectItem
                {
                    Name = AppConstants.NodeNameTaskConfig,
                    Icon = AppConstants.IconTasks,
                    Type = AppConstants.NodeTypeTaskConfig,
                    Tag = "TaskConfig",
                    Children = new ObservableCollection<ProjectItem>()
                };
                topLevelChassis.Children.Add(taskConfig);

                // 创建"监控与回放"子节点
                var monitorPlayback = new ProjectItem
                {
                    Name = "监控与回放",
                    Icon = AppConstants.IconMonitor,
                    Type = "monitor_playback",
                    Tag = "MonitorPlayback",
                    Children = new ObservableCollection<ProjectItem>()
                };
                topLevelChassis.Children.Add(monitorPlayback);

                // 创建"远程接口"子节点，其下包含"TDM系统"
                var remoteInterface = new ProjectItem
                {
                    Name = AppConstants.NodeNameRemoteInterface,
                    Icon = AppConstants.IconHand,
                    Type = AppConstants.NodeTypeRemoteInterface,
                    Tag = "RemoteInterface",
                    Children = new ObservableCollection<ProjectItem>()
                };
                remoteInterface.Children.Add(new ProjectItem
                {
                    Name = "TDM系统",
                    Icon = AppConstants.IconHand,
                    Type = "tdm_system",
                    Tag = "TDMSystem",
                    Children = new ObservableCollection<ProjectItem>()
                });
                topLevelChassis.Children.Add(remoteInterface);

                // 添加到根节点：插入到上一个机箱节点之后，但要在数据分析之前
                if (rootNode.Children == null)
                    rootNode.Children = new ObservableCollection<ProjectItem>();

                // 查找最后一个机箱节点的位置
                int lastChassisIndex = -1;
                int dataAnalysisIndex = -1;
                
                for (int i = 0; i < rootNode.Children.Count; i++)
                {
                    var child = rootNode.Children[i];
                    if (child.Type == AppConstants.NodeTypePxiChassis)
                    {
                        lastChassisIndex = i;
                    }
                    else if (child.Name == AppConstants.NodeNameDataAnalysis)
                    {
                        dataAnalysisIndex = i;
                    }
                }

                // 确定插入位置
                int insertIndex;
                if (lastChassisIndex >= 0)
                {
                    // 如果存在机箱节点，插入到最后一个机箱节点之后
                    insertIndex = lastChassisIndex + 1;
                    
                    // 确保不超过数据分析的位置
                    if (dataAnalysisIndex >= 0 && insertIndex > dataAnalysisIndex)
                    {
                        insertIndex = dataAnalysisIndex;
                    }
                }
                else
                {
                    // 如果没有机箱节点，插入到硬件配置之后
                    var hardwareConfigIndex = -1;
                    for (int i = 0; i < rootNode.Children.Count; i++)
                    {
                        if (rootNode.Children[i].Name == AppConstants.NodeNameHardwareConfig)
                        {
                            hardwareConfigIndex = i;
                            break;
                        }
                    }
                    
                    if (hardwareConfigIndex >= 0)
                    {
                        insertIndex = hardwareConfigIndex + 1;
                        // 确保不超过数据分析的位置
                        if (dataAnalysisIndex >= 0 && insertIndex > dataAnalysisIndex)
                        {
                            insertIndex = dataAnalysisIndex;
                        }
                    }
                    else
                    {
                        // 如果找不到硬件配置，插入到数据分析之前
                        insertIndex = dataAnalysisIndex >= 0 ? dataAnalysisIndex : rootNode.Children.Count;
                    }
                }

                rootNode.Children.Insert(insertIndex, topLevelChassis);
            }
        }

        public void RenamePxiChassisInProject(ObservableCollection<ProjectItem> project, string oldName, string newName)
        {
            if (project == null || project.Count == 0)
                return;

            var rootNode = project[0];
            if (rootNode == null) return;

            // 1. 重命名"设备与网络"下的机箱节点
            var hardwareConfigItem = rootNode.Children?
                .FirstOrDefault(item => item.Name == AppConstants.NodeNameHardwareConfig);

            if (hardwareConfigItem != null)
            {
                var deviceNetworkItem = hardwareConfigItem.Children?
                    .FirstOrDefault(item => item.Name == AppConstants.NodeNameDeviceNetwork);

                if (deviceNetworkItem?.Children != null)
                {
                    var chassisItem = deviceNetworkItem.Children
                        .FirstOrDefault(child => child.Name == oldName && child.Type == AppConstants.NodeTypePxiChassis);

                    if (chassisItem != null)
                    {
                        chassisItem.Name = newName;
                    }
                }
            }

            // 2. 重命名顶级机箱节点
            if (rootNode.Children != null)
            {
                var topLevelChassis = rootNode.Children
                    .FirstOrDefault(item => item.Name == oldName && item.Type == AppConstants.NodeTypePxiChassis);

                if (topLevelChassis != null)
                {
                    topLevelChassis.Name = newName;
                }
            }
        }

        public void RemovePxiChassisFromProject(ObservableCollection<ProjectItem> project, string chassisName)
        {
            if (project == null || project.Count == 0)
                return;

            var rootNode = project[0];
            if (rootNode == null) return;

            bool removedFromHardwareConfig = false;
            bool removedFromDeviceNetwork = false;
            bool removedFromTopLevel = false;

            // 1. 从"硬件配置"下删除机箱节点（有两种挂载方式：
            //    - 直接挂在"硬件配置"下（AddPxiChassisToProject 的实现）
            //    - 挂在"硬件配置 -> 设备与网络"下（兼容旧结构）
            var hardwareConfigItem = rootNode.Children?
                .FirstOrDefault(item => item.Name == AppConstants.NodeNameHardwareConfig);

            if (hardwareConfigItem != null)
            {
                // 1.1 直接在"硬件配置"下查找并删除
                if (hardwareConfigItem.Children != null)
                {
                    var chassisInHardwareConfig = hardwareConfigItem.Children
                        .FirstOrDefault(child => child.Name == chassisName && child.Type == AppConstants.NodeTypePxiChassis);

                    if (chassisInHardwareConfig != null)
                    {
                        hardwareConfigItem.Children.Remove(chassisInHardwareConfig);
                        removedFromHardwareConfig = true;
                        System.Diagnostics.Debug.WriteLine($"[ProjectTreeService] 已从'硬件配置'下删除机箱节点: {chassisName}");
                    }
                }

                // 1.2 在"硬件配置 -> 设备与网络"下查找并删除（兼容）
                var deviceNetworkItem = hardwareConfigItem.Children?
                    .FirstOrDefault(item => item.Name == AppConstants.NodeNameDeviceNetwork);

                if (deviceNetworkItem?.Children != null)
                {
                    var chassisItem = deviceNetworkItem.Children
                        .FirstOrDefault(child => child.Name == chassisName && child.Type == AppConstants.NodeTypePxiChassis);

                    if (chassisItem != null)
                    {
                        deviceNetworkItem.Children.Remove(chassisItem);
                        removedFromDeviceNetwork = true;
                        System.Diagnostics.Debug.WriteLine($"[ProjectTreeService] 已从'设备与网络'下删除机箱节点: {chassisName}");
                    }
                }
            }

            // 2. 删除顶级机箱节点（硬件设备下）
            if (rootNode.Children != null)
            {
                var topLevelChassis = rootNode.Children
                    .FirstOrDefault(item => item.Name == chassisName && item.Type == AppConstants.NodeTypePxiChassis);

                if (topLevelChassis != null)
                {
                    rootNode.Children.Remove(topLevelChassis);
                    removedFromTopLevel = true;
                    System.Diagnostics.Debug.WriteLine($"[ProjectTreeService] 已删除顶级机箱节点: {chassisName}");
                }
            }

            // 记录删除结果
            if (!removedFromHardwareConfig && !removedFromDeviceNetwork && !removedFromTopLevel)
            {
                System.Diagnostics.Debug.WriteLine($"[ProjectTreeService] 警告：未找到机箱节点 '{chassisName}'，可能已被删除");
            }
        }

        public void AddTestTaskToProject(ObservableCollection<ProjectItem> project, ProjectItem testTask)
        {
            if (project == null || project.Count == 0 || testTask == null)
                return;

            // 查找所有机箱节点下的任务配置节点
            var rootNode = project[0];
            if (rootNode?.Children == null) return;

            foreach (var child in rootNode.Children)
            {
                if (child.Type == AppConstants.NodeTypePxiChassis && child.Children != null)
                {
                    var taskConfigItem = child.Children
                        .FirstOrDefault(item => item.Name == AppConstants.NodeNameTaskConfig && item.Type == AppConstants.NodeTypeTaskConfig);

                    if (taskConfigItem != null)
                    {
                        if (taskConfigItem.Children == null)
                            taskConfigItem.Children = new ObservableCollection<ProjectItem>();

                        taskConfigItem.Children.Add(testTask);
                        return; // 只添加到第一个找到的任务配置节点
                    }
                }
            }
        }

        public void RenameTestTaskInProject(ObservableCollection<ProjectItem> project, string oldName, string newName)
        {
            if (project == null || project.Count == 0)
                return;

            // 在所有机箱节点下查找测试任务
            var rootNode = project[0];
            if (rootNode?.Children == null) return;

            foreach (var child in rootNode.Children)
            {
                if (child.Type == AppConstants.NodeTypePxiChassis && child.Children != null)
                {
                    var taskConfigItem = child.Children
                        .FirstOrDefault(item => item.Name == AppConstants.NodeNameTaskConfig && item.Type == AppConstants.NodeTypeTaskConfig);

                    if (taskConfigItem?.Children != null)
                    {
                        var testTaskItem = taskConfigItem.Children
                            .FirstOrDefault(t => t.Name == oldName && t.Type == AppConstants.NodeTypeTestTask);

                        if (testTaskItem != null)
                        {
                            testTaskItem.Name = newName;
                            return; // 只重命名第一个找到的测试任务
                        }
                    }
                }
            }
        }

        public void RemoveTestTaskFromProject(ObservableCollection<ProjectItem> project, string testTaskName)
        {
            if (project == null || project.Count == 0)
                return;

            // 在所有机箱节点下查找测试任务
            var rootNode = project[0];
            if (rootNode?.Children == null) return;

            foreach (var child in rootNode.Children)
            {
                if (child.Type == AppConstants.NodeTypePxiChassis && child.Children != null)
                {
                    var taskConfigItem = child.Children
                        .FirstOrDefault(item => item.Name == AppConstants.NodeNameTaskConfig && item.Type == AppConstants.NodeTypeTaskConfig);

                    if (taskConfigItem?.Children != null)
                    {
                        var testTaskItem = taskConfigItem.Children
                            .FirstOrDefault(t => t.Name == testTaskName && t.Type == AppConstants.NodeTypeTestTask);

                        if (testTaskItem != null)
                        {
                            taskConfigItem.Children.Remove(testTaskItem);
                            return; // 只删除第一个找到的测试任务
                        }
                    }
                }
            }
        }
    }
}
