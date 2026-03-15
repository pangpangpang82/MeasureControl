using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using MeasureControl.Constants;
using MeasureControl.Events;
using MeasureControl.Helpers;
using MeasureControl.Models;
using MeasureControl.Models.Devices;
using MeasureControl.Services;
using MeasureControl.ViewModels.TestTask.ConfigTabel;
using MeasureControl.Views;
using MeasureControl.Views.Common;
using MeasureControl.Views.Dialogs;
using MeasureControl.ViewModels.Dialogs;
using MeasureControl.Drivers;
using Microsoft.Win32;
using Prism.Commands;
using Prism.Events;
using Prism.Mvvm;
using Prism.Regions;
using Prism.Services.Dialogs;
using DialogServiceAlias = MeasureControl.Services.DialogService;
using MeasureControl.ViewModels.IcdConfig;
using MeasureControl.Helpers.SelfInspection;
using MeasureControl.Views.Dialogs;
using MeasureControl.ViewModels.Dialogs;

namespace MeasureControl.ViewModels.Common
{
    public class MainWindowViewModel : BindableBase, IDisposable
    {
        private const bool FixedDemoMode = true;

        public bool IsFixedDemoMode => FixedDemoMode;
        #region Private Fields

        private readonly ProjectService _projectService;
        private readonly IProjectTreeService _projectTreeService;
        private readonly IEventAggregator _eventAggregator;
        private readonly IRegionManager _regionManager;
        private readonly IPxiChassisService _pxiChassisService;
        private readonly IWindowManagerService _windowManager;
        private readonly IProjectSaveStateService _projectSaveStateService;
        private readonly IChassisConnectionService _chassisConnectionService;
        private readonly IChannelBindingService _channelBindingService;
        private readonly INavigationStateService _navigationState;
        private readonly INavigationService _navigationService;
        private readonly Prism.Services.Dialogs.IDialogService _dialogService;
        private readonly DeviceConfigurationService _deviceConfigurationService;
        private readonly SignalValueUpdateService _signalValueUpdateService;

        private bool _isProjectMenuOpen;
        private bool _isBottomBarVisible = true;
        private bool _isProjectModified = false;
        private bool _isSilentSavingSingleBoardTestResult = false;
        private string _currentTime;
        private string _currentProjectPath;
        private DispatcherTimer _timer;
        private ObservableCollection<ProjectItem> _currentProject;

        // 测试运行状态跟踪
        private ProjectItem _runningTestTask;
        private ChassisModel _runningChassis;
        private List<Models.Devices.DeviceBase> _runningDevices = new List<Models.Devices.DeviceBase>();

        private string _selfInspectingChassisName;

        public string SelfInspectingChassisName
        {
            get => _selfInspectingChassisName;
            private set => SetProperty(ref _selfInspectingChassisName, value);
        }

        private void EnsureFixedDemoPxiChassisAndDefaultCardConfigs(ProjectItem project)
        {
            if (project == null)
            {
                return;
            }

            var testTaskNames = _projectService?.GetGlobalTestTaskNames() ?? new List<string>();

            project.PxiChassisData ??= new ObservableCollection<ChassisModel>();

            if (project.PxiChassisData.Count == 0)
            {
                var chassis1 = ChassisFactory.CreateChassis("PXIe-2722G2", "PXI机箱1", 0, 0);
                if (chassis1 != null)
                {
                    project.PxiChassisData.Add(chassis1);
                }

                var chassis2 = ChassisFactory.CreateChassis("PXIe-2519G2", "PXI机箱2", 0, 1);
                if (chassis2 != null)
                {
                    project.PxiChassisData.Add(chassis2);
                }
            }

            foreach (var chassis in project.PxiChassisData.Where(c => c != null))
            {
                chassis.Devices ??= new ObservableCollection<DeviceBase>();

                var chassisDevice = chassis.Devices.OfType<ChassisDevice>().FirstOrDefault();
                if (chassisDevice == null)
                {
                    chassisDevice = new ChassisDevice(chassis.Model ?? chassis.Name);
                    chassisDevice.CardName = chassis.Name;
                    chassisDevice.SlotCount = chassis.SlotCount;
                    chassisDevice.ParentNode = $"{chassisDevice.SlotCount}槽机箱";
                    chassisDevice.ConnectionMethod = "详细信息";
                    chassisDevice.Details = "详细信息";
                    chassisDevice.DeviceType = AppConstants.DeviceTypeChassis;
                    chassisDevice.Status = "正常";
                    chassisDevice.IsExpanded = true;
                    chassisDevice.Model = chassis.Model;
                    chassisDevice.ChassisModel = chassis.Model;
                    chassisDevice.Children ??= new ObservableCollection<DeviceBase>();
                    chassis.Devices.Add(chassisDevice);
                }

                if (string.Equals(chassis.Name, "PXI机箱1", StringComparison.OrdinalIgnoreCase))
                {
                    
                }
                else if (string.Equals(chassis.Name, "PXI机箱2", StringComparison.OrdinalIgnoreCase))
                {
                   
                }
            }
        }

        private void EnsureFixedDemoCard(ChassisDevice chassisDevice, string deviceName, string slotPosition, string chassisName, List<string> testTaskNames)
        {
            if (chassisDevice?.Children == null)
            {
                return;
            }

            var existing = chassisDevice.Children.FirstOrDefault(d => d != null && string.Equals(d.Name, deviceName, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                existing = DeviceFactory.CreateDevice(deviceName, slotPosition);
                if (existing == null)
                {
                    return;
                }
                chassisDevice.Children.Add(existing);
            }

            if (existing is DigitalIODevice dioDevice)
            {
                var config = existing.CardConfigData as DigitalIOCardConfig;
                if (config == null)
                {
                    config = new DigitalIOCardConfig();
                    existing.CardConfigData = config;
                }
                config.CardId = existing.Id;
                config.CardName = existing.CardName;
                config.CardModel = existing.Model;
                config.ChassisName = chassisName;
                EnsureDigitalIoTaskConfigs(config, dioDevice, testTaskNames);
                return;
            }

            if (existing is AnalogOutputDevice aoDevice)
            {
                var config = existing.CardConfigData as AnalogOutputCardConfig;
                if (config == null)
                {
                    config = new AnalogOutputCardConfig();
                    existing.CardConfigData = config;
                }
                config.CardId = existing.Id;
                config.CardName = existing.CardName;
                config.CardModel = existing.Model;
                config.ChassisName = chassisName;
                EnsureAnalogOutputTaskConfigs(config, aoDevice, testTaskNames);
                return;
            }

            if (existing is AnalogAcquisitionDevice aiDevice)
            {
                var config = existing.CardConfigData as AnalogInputCardConfig;
                if (config == null)
                {
                    config = new AnalogInputCardConfig();
                    existing.CardConfigData = config;
                }
                config.CardId = existing.Id;
                config.CardName = existing.CardName;
                config.CardModel = existing.Model;
                config.ChassisName = chassisName;
                EnsureAnalogInputTaskConfigs(config, aiDevice, testTaskNames);
                return;
            }

            if (existing is ProgrammableResistorDevice roDevice)
            {
                var config = existing.CardConfigData as ResistanceOutputCardConfig;
                if (config == null)
                {
                    config = new ResistanceOutputCardConfig();
                    existing.CardConfigData = config;
                }
                config.CardId = existing.Id;
                config.CardName = existing.CardName;
                config.CardModel = existing.Model;
                config.ChassisName = chassisName;
                EnsureResistanceOutputTaskConfigs(config, roDevice, testTaskNames);
            }
        }

        private void EnsureDigitalIoTaskConfigs(DigitalIOCardConfig cardConfig, DigitalIODevice device, List<string> testTaskNames)
        {
            if (cardConfig == null)
            {
                return;
            }

            cardConfig.InputChannels ??= new ObservableCollection<DiscreteChannelConfig>();
            cardConfig.OutputChannels ??= new ObservableCollection<DiscreteChannelConfig>();

            if (device != null)
            {
                if (cardConfig.InputChannels.Count == 0)
                {
                    for (int i = 0; i < device.InputChannels; i++)
                    {
                        cardConfig.InputChannels.Add(new DiscreteChannelConfig { ChannelName = $"DI{i}", IsEnabled = false, IsOutput = false });
                    }
                }
                if (cardConfig.OutputChannels.Count == 0)
                {
                    for (int i = 0; i < device.OutputChannels; i++)
                    {
                        cardConfig.OutputChannels.Add(new DiscreteChannelConfig { ChannelName = $"DO{i}", IsEnabled = false, IsOutput = true });
                    }
                }
            }

            cardConfig.TestTaskConfigs ??= new ObservableCollection<DigitalIOTestTaskConfig>();
            foreach (var name in (testTaskNames ?? new List<string>()).Where(t => !string.IsNullOrWhiteSpace(t)))
            {
                if (cardConfig.TestTaskConfigs.Any(c => string.Equals(c?.TestTaskName, name, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var cfg = new DigitalIOTestTaskConfig { TestTaskName = name };
                cfg.InputChannels ??= new ObservableCollection<DiscreteChannelConfig>();
                cfg.OutputChannels ??= new ObservableCollection<DiscreteChannelConfig>();

                foreach (var ch in cardConfig.InputChannels)
                {
                    if (ch == null || string.IsNullOrWhiteSpace(ch.ChannelName)) continue;
                    cfg.InputChannels.Add(new DiscreteChannelConfig { ChannelName = ch.ChannelName, IsEnabled = false, IsOutput = false });
                }
                foreach (var ch in cardConfig.OutputChannels)
                {
                    if (ch == null || string.IsNullOrWhiteSpace(ch.ChannelName)) continue;
                    cfg.OutputChannels.Add(new DiscreteChannelConfig { ChannelName = ch.ChannelName, IsEnabled = false, IsOutput = true });
                }

                cfg.OutputMode = cardConfig.OutputMode;
                cfg.PowerVoltage = cardConfig.PowerVoltage;
                cfg.PowerVoltageGroup2 = cardConfig.PowerVoltageGroup2;
                cfg.PowerVoltageGroup3 = cardConfig.PowerVoltageGroup3;
                cfg.PowerVoltageGroup4 = cardConfig.PowerVoltageGroup4;

                cardConfig.TestTaskConfigs.Add(cfg);
            }
        }

        private void EnsureAnalogInputTaskConfigs(AnalogInputCardConfig cardConfig, AnalogAcquisitionDevice device, List<string> testTaskNames)
        {
            if (cardConfig == null)
            {
                return;
            }

            cardConfig.Channels ??= new ObservableCollection<AnalogChannelConfig>();
            if (cardConfig.Channels.Count == 0 && device != null)
            {
                for (int i = 0; i < device.ChannelCount; i++)
                {
                    cardConfig.Channels.Add(new AnalogChannelConfig
                    {
                        ChannelName = $"AI{i}",
                        IsEnabled = false,
                        Range = "±10V",
                        AvailableRanges = new List<string> { "±10V", "±5V", "±2V", "±1V" },
                        CurrentValue = 0,
                        Unit = "V",
                        Status = ""
                    });
                }
            }

            cardConfig.TestTaskConfigs ??= new ObservableCollection<AnalogInputTestTaskConfig>();
            foreach (var name in (testTaskNames ?? new List<string>()).Where(t => !string.IsNullOrWhiteSpace(t)))
            {
                if (cardConfig.TestTaskConfigs.Any(c => string.Equals(c?.TestTaskName, name, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var cfg = new AnalogInputTestTaskConfig { TestTaskName = name };
                cfg.Channels ??= new ObservableCollection<AnalogChannelConfig>();
                foreach (var ch in cardConfig.Channels)
                {
                    if (ch == null || string.IsNullOrWhiteSpace(ch.ChannelName)) continue;
                    cfg.Channels.Add(new AnalogChannelConfig
                    {
                        ChannelName = ch.ChannelName,
                        IsEnabled = false,
                        Range = ch.Range,
                        AvailableRanges = ch.AvailableRanges?.ToList() ?? new List<string> { "±10V" },
                        CurrentValue = 0,
                        Unit = ch.Unit,
                        Status = ch.Status
                    });
                }

                cardConfig.TestTaskConfigs.Add(cfg);
            }
        }

        private void EnsureAnalogOutputTaskConfigs(AnalogOutputCardConfig cardConfig, AnalogOutputDevice device, List<string> testTaskNames)
        {
            if (cardConfig == null)
            {
                return;
            }

            cardConfig.Channels ??= new ObservableCollection<AnalogChannelConfig>();
            if (cardConfig.Channels.Count == 0 && device != null)
            {
                for (int i = 0; i < device.ChannelCount; i++)
                {
                    cardConfig.Channels.Add(new AnalogChannelConfig
                    {
                        ChannelName = $"AO{i}",
                        IsEnabled = false,
                        Range = "直流",
                        AvailableRanges = new List<string> { "直流", "正弦", "方波" },
                        CurrentValue = 0,
                        Unit = "V",
                        Status = ""
                    });
                }
            }

            cardConfig.TestTaskConfigs ??= new ObservableCollection<AnalogOutputTestTaskConfig>();
            foreach (var name in (testTaskNames ?? new List<string>()).Where(t => !string.IsNullOrWhiteSpace(t)))
            {
                if (cardConfig.TestTaskConfigs.Any(c => string.Equals(c?.TestTaskName, name, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var cfg = new AnalogOutputTestTaskConfig { TestTaskName = name };
                cfg.Channels ??= new ObservableCollection<AnalogOutputExtendedChannelConfig>();
                foreach (var ch in cardConfig.Channels)
                {
                    if (ch == null || string.IsNullOrWhiteSpace(ch.ChannelName)) continue;
                    cfg.Channels.Add(new AnalogOutputExtendedChannelConfig
                    {
                        ChannelName = ch.ChannelName,
                        IsEnabled = false,
                        Range = ch.Range,
                        AvailableRanges = ch.AvailableRanges?.ToList() ?? new List<string> { "直流" },
                        CurrentValue = 0,
                        Unit = ch.Unit,
                        Status = ch.Status,
                        WaveformType = OutputWaveformType.Dc,
                        Amplitude = 0,
                        Frequency = 0,
                        Offset = 0,
                        DutyCycle = 50,
                        IsPreviewEnabled = false
                    });
                }

                cardConfig.TestTaskConfigs.Add(cfg);
            }
        }

        private void EnsureResistanceOutputTaskConfigs(ResistanceOutputCardConfig cardConfig, ProgrammableResistorDevice device, List<string> testTaskNames)
        {
            if (cardConfig == null)
            {
                return;
            }

            cardConfig.Channels ??= new ObservableCollection<ResistanceChannelConfigData>();
            if (cardConfig.Channels.Count == 0 && device != null)
            {
                for (int i = 0; i < device.ChannelCount; i++)
                {
                    cardConfig.Channels.Add(new ResistanceChannelConfigData
                    {
                        ChannelName = $"RO{i}",
                        IsEnabled = false,
                        Offset = 0.000,
                        TargetResistance = 2.000
                    });
                }
            }

            cardConfig.TestTaskConfigs ??= new ObservableCollection<ResistanceOutputTestTaskConfig>();
            foreach (var name in (testTaskNames ?? new List<string>()).Where(t => !string.IsNullOrWhiteSpace(t)))
            {
                if (cardConfig.TestTaskConfigs.Any(c => string.Equals(c?.TestTaskName, name, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var cfg = new ResistanceOutputTestTaskConfig { TestTaskName = name, OutputMode = "" };
                cfg.Channels ??= new ObservableCollection<ResistanceChannelConfigData>();
                foreach (var ch in cardConfig.Channels)
                {
                    if (ch == null || string.IsNullOrWhiteSpace(ch.ChannelName)) continue;
                    cfg.Channels.Add(new ResistanceChannelConfigData
                    {
                        ChannelName = ch.ChannelName,
                        IsEnabled = false,
                        Offset = ch.Offset,
                        TargetResistance = ch.TargetResistance
                    });
                }

                cardConfig.TestTaskConfigs.Add(cfg);
            }
        }

        public bool HasProject => CurrentProject?.Count > 0;

        // 导航历史管理
        private Stack<string> _navigationHistory = new Stack<string>();
        private string _currentPageName;

        // 防止循环调用的标志
        private bool _isClosing = false;

        // 浮动窗口激活时间戳
        private DateTime _lastFloatingWindowActivatedTime = DateTime.MinValue;

        #endregion

        #region Public Properties

        /// <summary>
        /// 当前时间显示
        /// </summary>
        public string CurrentTime
        {
            get => _currentTime;
            set => SetProperty(ref _currentTime, value);
        }

        /// <summary>
        /// 当前打开的项目
        /// </summary>
        public ObservableCollection<ProjectItem> CurrentProject
        {
            get => _currentProject;
            set
            {
                if (SetProperty(ref _currentProject, value))
                {
                    (SaveProjectCommand as DelegateCommand)?.RaiseCanExecuteChanged();
                    (CloseProjectCommand as DelegateCommand)?.RaiseCanExecuteChanged();
                    RaisePropertyChanged(nameof(HasProject));
                    RaisePropertyChanged(nameof(ProjectTreeItems));
                }
            }
        }

        public bool IsDemoLockedConfigNode(ProjectItem item)
        {
            if (!FixedDemoMode || item == null)
            {
                return false;
            }

            if (item.Type == AppConstants.NodeTypeTestTask)
            {
                return false;
            }

            var isConfigNodeType = item.Type == "channel_config" ||
                                   item.Type == "signal_config" ||
                                   item.Type == "icd_mapping" ||
                                   item.Type == "icd_config" ||
                                   item.Type == "test_ui" ||
                                   item.Type == "test_sequence" ||
                                   item.Type == "report" ||
                                   IsConfigTabelType(item.Type);

            if (!isConfigNodeType)
            {
                return false;
            }

            var parentTestTask = GetParentTestTaskName(item);
            if (string.IsNullOrEmpty(parentTestTask))
            {
                parentTestTask = GetParentTestTaskNameAlternative(item);
            }

            if (string.IsNullOrEmpty(parentTestTask))
            {
                return false;
            }

            // 单板测试任务定义在项目树顶层“测试任务”节点下（不隶属于任何机箱）
            var parentChassis = GetParentChassisName(item);
            if (!string.IsNullOrEmpty(parentChassis))
            {
                return false;
            }

            return true;
        }

        public ObservableCollection<ProjectItem> ProjectTreeItems
        {
            get
            {
                if (CurrentProject != null && CurrentProject.Count > 0)
                {
                    return CurrentProject[0]?.Children ?? new ObservableCollection<ProjectItem>();
                }

                return new ObservableCollection<ProjectItem>();
            }
        }

        private string GetFixedDemoProjectPath()
        {
            // 固定为程序目录下 Projects\proj.json（用于演示版本的内置项目文件）
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            return Path.Combine(baseDir, "Projects", "proj.json");
        }

        private void EnsureFixedDemoProjectTree(ProjectItem projectRoot)
        {
            if (projectRoot == null)
            {
                return;
            }

            var hardwareConfig = projectRoot.Children?.FirstOrDefault(c => c != null && (c.Type == AppConstants.NodeTypeHardwareConfig || (c.Tag as string) == "HardwareConfig" || c.Name == AppConstants.NodeNameHardwareConfig));
            if (hardwareConfig == null)
            {
                hardwareConfig = new ProjectItem();
            }
            hardwareConfig.Name = AppConstants.NodeNameHardwareConfig;
            hardwareConfig.Icon = AppConstants.IconHardware;
            hardwareConfig.Type = AppConstants.NodeTypeHardwareConfig;
            hardwareConfig.Tag = "HardwareConfig";
            hardwareConfig.Children.Clear();

            var deviceNetwork = new ProjectItem
            {
                Name = AppConstants.NodeNameDeviceNetwork,
                Icon = AppConstants.IconHardware,
                Type = AppConstants.NodeTypeDevice,
                Tag = "Device"
            };

            hardwareConfig.Children.Add(deviceNetwork);
            hardwareConfig.Children.Add(new ProjectItem
            {
                Name = "PXI机箱1",
                Icon = AppConstants.IconHardware,
                Type = AppConstants.NodeTypePxiChassis,
                Tag = "PXIChassis"
            });
            hardwareConfig.Children.Add(new ProjectItem
            {
                Name = "PXI机箱2",
                Icon = AppConstants.IconHardware,
                Type = AppConstants.NodeTypePxiChassis,
                Tag = "PXIChassis"
            });

            var testTasks = projectRoot.Children?.FirstOrDefault(c => c != null && ((c.Tag as string) == "TestTasks" || c.Name == "测试任务"));
            if (testTasks == null)
            {
                testTasks = new ProjectItem();
            }
            testTasks.Name = "测试任务";
            testTasks.Icon = AppConstants.IconTasks;
            testTasks.Type = "test_tasks";
            testTasks.Tag = "TestTasks";

            if (testTasks.Children == null)
            {
                testTasks.Children = new ObservableCollection<ProjectItem>();
            }

            if (testTasks.Children.Count == 0)
            {
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
            }

            projectRoot.Children.Clear();
            projectRoot.Children.Add(hardwareConfig);
            projectRoot.Children.Add(testTasks);

            RaisePropertyChanged(nameof(CurrentProject));
            RaisePropertyChanged(nameof(ProjectTreeItems));
        }

        private void InitializeFixedDemoStartupProject()
        {
            if (!FixedDemoMode)
            {
                return;
            }

            var fixedProjectPath = GetFixedDemoProjectPath();
            var fixedProjectDir = Path.GetDirectoryName(fixedProjectPath);
            if (!string.IsNullOrWhiteSpace(fixedProjectDir))
            {
                Directory.CreateDirectory(fixedProjectDir);
            }

            ProjectItem project = null;
            if (File.Exists(fixedProjectPath))
            {
                try
                {
                    project = _projectService.LoadProject(fixedProjectPath);
                }
                catch
                {
                    project = null;
                }
            }

            if (project == null)
            {
                project = new ProjectItem
                {
                    Name = "",
                    Icon = AppConstants.IconFolder,
                    Type = AppConstants.NodeTypeRoot,
                    Tag = "Root"
                };
                _projectService.EnsureProjectItemProperties(project);
                EnsureFixedDemoProjectTree(project);
                EnsureFixedDemoPxiChassisAndDefaultCardConfigs(project);
                _projectService.SaveProject(project, fixedProjectPath);
            }

            ApplyLoadedProject(project, fixedProjectPath);
        }

        private void ApplyLoadedProject(ProjectItem project, string projectPath)
        {
            if (project == null)
            {
                return;
            }

            // Demo 模式下强制固定项目树结构，但保留项目内嵌的板卡/标定等数据
            if (FixedDemoMode)
            {
                EnsureFixedDemoProjectTree(project);
            }

            // 加载机箱数据到服务
            _pxiChassisService.LoadChassisData(project.PxiChassisData);

            // 存储连接数据，等待HardwareConfig页面导航时再加载
            if (project.ChassisConnections != null && project.ChassisConnections.Count > 0)
            {
                // 直接加载到ChassisConnectionService中
                _chassisConnectionService.ClearConnections();
                foreach (var connection in project.ChassisConnections)
                {
                    _chassisConnectionService.AddConnection(connection);
                }
            }

            // 存储连接线数据，等待HardwareConfig页面导航时再加载
            if (project.ConnectionLines != null && project.ConnectionLines.Count > 0)
            {
                // 通过事件通知HardwareConfigViewModel存储连接线数据
                var connectionLinesLoadArgs = new ConnectionLinesLoadEventArgs
                {
                    ConnectionLines = new List<ConnectionLine>(project.ConnectionLines)
                };
                _eventAggregator.GetEvent<ConnectionLinesLoadEvent>().Publish(connectionLinesLoadArgs);
            }
            else
            {
            }

            // 加载通道配置表数据前，先清空静态字典
            ChannelConfigTabelViewModel.ClearAllChannelTabelItems();
            SignalConfigTabelViewModel.ClearAllSignalTabelItems();

            // 加载通道配置表数据
            if (project.ChannelTabelItems != null && project.ChannelTabelItems.Count > 0)
            {
                // 将通道配置表数据加载到ChannelConfigTabelViewModel的静态字典
                ChannelConfigTabelViewModel.LoadChannelTabelItems(new Dictionary<string, List<ChannelTabelItem>>(project.ChannelTabelItems));
            }

            // 加载信号配置表数据
            if (project.SignalTabelItems != null && project.SignalTabelItems.Count > 0)
            {
                // 将信号配置表数据加载到SignalConfigTabelViewModel的静态字典
                SignalConfigTabelViewModel.LoadSignalTabelItems(new Dictionary<string, List<SignalConfigItem>>(project.SignalTabelItems));
            }

            // 加载ICD配置表数据
            if (project.IcdTabelItems != null && project.IcdTabelItems.Count > 0)
            {
                // 将ICD配置表数据加载到IcdConfigTabelViewModel的静态字典
                IcdConfigTabelViewModel.LoadIcdTabelItems(new Dictionary<string, List<IcdFrameItem>>(project.IcdTabelItems));

                // 加载ICD配置表的协议类型
                if (project.Children != null)
                {
                    foreach (var chassisNode in project.Children.Where(c => c.Type == AppConstants.NodeTypePxiChassis))
                    {
                        if (chassisNode.Children == null) continue;
                        var taskConfigNode = chassisNode.Children.FirstOrDefault(c => c.Type == AppConstants.NodeTypeTaskConfig);
                        if (taskConfigNode?.Children == null) continue;

                        foreach (var testTask in taskConfigNode.Children.Where(t => t.Type == AppConstants.NodeTypeTestTask))
                        {
                            if (testTask.Children == null) continue;
                            var icdConfigNode = testTask.Children.FirstOrDefault(c => c.Type == "icd_config");
                            if (icdConfigNode?.Children == null) continue;

                            foreach (var icdTabel in icdConfigNode.Children.Where(t => t.Type == "icd_config_tabel"))
                            {
                                if (!string.IsNullOrEmpty(icdTabel.ProtocolType))
                                {
                                    string key = $"{testTask.Name}/{icdTabel.Name}";
                                    IcdConfigTabelViewModel.SetIcdTabelProtocolType(key, icdTabel.ProtocolType);
                                }
                            }
                        }
                    }
                }
            }

            // 加载通讯信号配置表数据
            if (project.IcdMappingItems != null && project.IcdMappingItems.Count > 0)
            {
                // 将通讯信号配置表数据加载到通讯表 ViewModel 的静态字典
                IcdMappingTabelViewModel.LoadIcdMappingItems(new Dictionary<string, List<IcdMappingItem>>(project.IcdMappingItems));
            }
            else
            {
                // 清空通讯信号配置表数据
                IcdMappingTabelViewModel.ClearAllIcdMappingItems();
            }

            // 加载标定数据
            System.Diagnostics.Debug.WriteLine("[MainWindow] About to load calibration data...");
            System.Diagnostics.Debug.WriteLine($"[MainWindow] Checking calibration records: project.CalibrationRecords is {(project.CalibrationRecords == null ? "null" : "not null")}, Count={(project.CalibrationRecords?.Count ?? 0)}");

            if (project.CalibrationRecords != null && project.CalibrationRecords.Count > 0)
            {
                System.Diagnostics.Debug.WriteLine($"[MainWindow] Loading {project.CalibrationRecords.Count} calibration records from project");

                // 通过事件通知DataCalibrationViewModel加载标定数据
                var calibrationRecordsLoadArgs = new CalibrationRecordsLoadEventArgs
                {
                    CalibrationRecords = new Dictionary<string, ChannelCalibrationRecord>(project.CalibrationRecords)
                };
                _eventAggregator.GetEvent<CalibrationRecordsLoadEvent>().Publish(calibrationRecordsLoadArgs);

                // 更新全局标定服务（用于物理层信号调理）
                Services.CalibrationService.Instance.UpdateCalibrationData(project.CalibrationRecords);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("[MainWindow] No calibration records found in project");
            }

            // 加载矩阵开关配置表数据（如果有）
            // 先清空静态字典（避免旧数据残留）
            MatrixSwitchConfigTableViewModel.ClearAllMatrixSwitchTableItems();

            if (project.MatrixSwitchTableItems != null && project.MatrixSwitchTableItems.Count > 0)
            {
                // 将矩阵开关配置表数据加载到MatrixSwitchConfigTableViewModel的静态字典
                MatrixSwitchConfigTableViewModel.LoadMatrixSwitchTableItems(new Dictionary<string, List<MatrixSwitchConfigItem>>(project.MatrixSwitchTableItems));
            }
            else
            {
                // 清空矩阵开关配置表数据
                MatrixSwitchConfigTableViewModel.ClearAllMatrixSwitchTableItems();
            }

            // 包装成集合
            CurrentProject = new ObservableCollection<ProjectItem> { project };
            _currentProjectPath = projectPath;
            CalibrationPathHelper.SetProjectPath(_currentProjectPath);
            RaisePropertyChanged(nameof(CurrentProjectFilePath));
            _projectSaveStateService.MarkAsSaved(); // 标记为已保存
            IsProjectModified = false; // 刚打开的项目未修改

            // 展开项目树的一级目录
            ExpandProjectTreeLevel1(); //TODO：一级目录？

            // 发布项目打开事件，通知订阅者重置/加载其状态
            _eventAggregator.GetEvent<ProjectOpenedEvent>().Publish(project);

            // 项目加载成功后，导航到HomePage
            _regionManager.RequestNavigate(AppConstants.MainRegionName, "HomePage");
        }

        private int GetFirstCommunicatingTableIndex(ObservableCollection<ProjectItem> children)
        {
            if (children == null || children.Count == 0)
            {
                return -1;
            }

            for (int i = 0; i < children.Count; i++)
            {
                var child = children[i];
                if (child != null && child.Type == "communicating_signal_config_tabel")
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// 当前项目文件路径
        /// </summary>
        public string CurrentProjectFilePath
        {
            get
            {
                if (string.IsNullOrEmpty(_currentProjectPath))
                    return string.Empty;

                return _currentProjectPath;
            }
        }

        /// <summary>
        /// 工具集合
        /// </summary>
        public ObservableCollection<ProjectItem> Tools { get; set; }

        /// <summary>
        /// 导航按钮集合
        /// </summary>
        public ObservableCollection<NavigationButton> NavigationButtons => _navigationService.NavigationButtons;

        /// <summary>
        /// 项目菜单项集合
        /// </summary>
        public ObservableCollection<MenuItemModel> ProjectMenuItems { get; set; }

        /// <summary>
        /// 项目菜单是否打开
        /// </summary>
        public bool IsProjectMenuOpen
        {
            get => _isProjectMenuOpen;
            set => SetProperty(ref _isProjectMenuOpen, value);
        }

        /// <summary>
        /// 项目是否已修改
        /// </summary>
        public bool IsProjectModified
        {
            get => _isProjectModified;
            private set => SetProperty(ref _isProjectModified, value);
        }

        public bool IsBottomBarVisible
        {
            get => _isBottomBarVisible;
            set => SetProperty(ref _isBottomBarVisible, value);
        }

        #endregion

        #region Commands

        public ICommand ToggleMinimizeCommand { get; private set; }
        public ICommand ToggleMaximizeCommand { get; private set; }
        public ICommand CloseMainWindowCommand { get; private set; }
        public ICommand ShowProjectMenuCommand { get; private set; }
        public ICommand NewProjectCommand { get; private set; }
        public ICommand OpenProjectCommand { get; private set; }
        public ICommand SaveProjectCommand { get; private set; }
        public ICommand CloseProjectCommand { get; private set; }
        public ICommand NavigateCommand { get; private set; }
        public ICommand TreeItemDoubleClickCommand { get; private set; }
        public ICommand NavigationButtonClickCommand { get; private set; }
        public ICommand HideBottomBarCommand { get; private set; }
        public ICommand ShowBottomBarCommand { get; private set; }
        public ICommand CloseTabCommand { get; private set; }
        public ICommand RenamePxiChassisCommand { get; private set; }
        public ICommand DeletePxiChassisFromTreeCommand { get; private set; }
        public ICommand AddPxiChassisToTreeCommand { get; private set; }
        public ICommand AddPxi2722G2ToTreeCommand { get; private set; }
        public ICommand AddPxi2519G2ToTreeCommand { get; private set; }
        public ICommand CreateTestTaskCommand { get; private set; }
        public ICommand RenameTestTaskCommand { get; private set; }
        public ICommand DeleteTestTaskCommand { get; private set; }
        public ICommand CreateChannelConfigTabelCommand { get; private set; }
        public ICommand CreateSignalConfigTabelCommand { get; private set; }
        public ICommand CreateIcdConfigTabelCommand { get; private set; }
        public ICommand CreateIcdMappingTabelCommand { get; private set; }
        public ICommand CreateTestSequenceCommand { get; private set; }
        public ICommand CreateReportConfigTabelCommand { get; private set; }
        public ICommand RenameConfigTabelCommand { get; private set; }
        public ICommand DeleteConfigTabelCommand { get; private set; }
        public ICommand CreateTestInterfaceCommand { get; private set; }
        public ICommand CreateMatrixSwitchConfigTableCommand { get; private set; }
        
        // 测试控制命令
        public ICommand StartPauseTestCommand { get; private set; }
        public ICommand StopTestCommand { get; private set; }

        public ICommand SelfInspectionCommand { get; private set; }

        #endregion

        #region Test Running State

        private bool _isTestRunning = false;

        /// <summary>
        /// 测试是否正在运行
        /// </summary>
        public bool IsTestRunning
        {
            get => _isTestRunning;
            set
            {
                if (SetProperty(ref _isTestRunning, value))
                {
                    RaisePropertyChanged(nameof(IsTestStopped));
                    RaisePropertyChanged(nameof(ToolTipContent));
                    // 发布测试状态变化事件
                    _eventAggregator.GetEvent<TestRunningStateChangedEvent>().Publish(value);
                }
            }
        }

        private bool _isTestPaused = false;
        /// <summary>
        /// 测试是否已暂停
        /// </summary>
        public bool IsTestPaused
        {
            get => _isTestPaused;
            set
            {
                if (SetProperty(ref _isTestPaused, value))
                {
                    RaisePropertyChanged(nameof(ToolTipContent));
                }
            }
        }

        /// <summary>
        /// 启动/暂停按钮的工具提示内容
        /// </summary>
        public string ToolTipContent
        {
            get
            {
                if (IsTestRunning)
                {
                    if (IsTestPaused)
                        return "继续测试";
                    else
                        return "暂停测试";
                }
                else
                {
                    return "开始测试";
                }
            }
        }

        /// <summary>
        /// 测试是否已停止
        /// </summary>
        public bool IsTestStopped => !IsTestRunning;

        #endregion

        #region Constructor

        public MainWindowViewModel(IRegionManager regionManager, IEventAggregator eventAggregator,
            IProjectTreeService projectTreeService, IPxiChassisService pxiChassisService,
            IWindowManagerService windowManager, IProjectSaveStateService projectSaveStateService,
            IChassisConnectionService chassisConnectionService, IChannelBindingService channelBindingService,
            ProjectService projectService, INavigationStateService navigationState, INavigationService navigationService,
            Prism.Services.Dialogs.IDialogService dialogService, SignalValueUpdateService signalValueUpdateService)
        {
            // 依赖注入
            _projectService = projectService ?? throw new ArgumentNullException(nameof(projectService));
            _projectTreeService = projectTreeService;
            _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
            _regionManager = regionManager ?? throw new ArgumentNullException(nameof(regionManager));
            _pxiChassisService = pxiChassisService ?? throw new ArgumentNullException(nameof(pxiChassisService));
            _windowManager = windowManager ?? throw new ArgumentNullException(nameof(windowManager));
            _projectSaveStateService = projectSaveStateService ?? throw new ArgumentNullException(nameof(projectSaveStateService));
            _chassisConnectionService = chassisConnectionService ?? throw new ArgumentNullException(nameof(chassisConnectionService));
            _channelBindingService = channelBindingService ?? throw new ArgumentNullException(nameof(channelBindingService));
            _navigationState = navigationState ?? throw new ArgumentNullException(nameof(navigationState));
            _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
            _deviceConfigurationService = new DeviceConfigurationService();
            _signalValueUpdateService = signalValueUpdateService ?? throw new ArgumentNullException(nameof(signalValueUpdateService));

            // 初始化集合
            InitializeCollections();

            // 订阅事件
            SubscribeToEvents();

            // 初始化命令
            InitializeCommands();

            // 初始化菜单项
            InitializeProjectMenu();

            if (FixedDemoMode)
            {
                InitializeFixedDemoStartupProject();
            }

            // 启动时间更新
            StartTimeUpdater();

            // 初始化工具树
            //InitializeTools();

            // 不在构造函数中导航，而是等待窗口加载完成后再导航
        }

        #endregion

        #region Initialization Methods

        private void InitializeCollections()
        {
            Tools = new ObservableCollection<ProjectItem>();
            // NavigationButtons 由 NavigationService 管理
        }

        private void SubscribeToEvents()
        {
            _eventAggregator.GetEvent<AddPxiChassisEvent>().Subscribe(OnAddPxiChassis);
            _eventAggregator.GetEvent<RenamePxiChassisEvent>().Subscribe(OnRenamePxiChassis);
            _eventAggregator.GetEvent<DeletePxiChassisEvent>().Subscribe(OnDeletePxiChassis);
            _eventAggregator.GetEvent<DeviceModifiedEvent>().Subscribe(OnDeviceModified);
            _eventAggregator.GetEvent<ProjectModifiedEvent>().Subscribe(OnProjectModified);
            _eventAggregator.GetEvent<AddNavigationButtonEvent>().Subscribe(OnAddNavigationButton);
            _eventAggregator.GetEvent<PxiChassisSelectedEvent>().Subscribe(OnPxiChassisSelected);
            _eventAggregator.GetEvent<WindowMinimizedEvent>().Subscribe(OnWindowMinimized);
            _eventAggregator.GetEvent<WindowClosingEvent>().Subscribe(OnWindowClosing);
            _eventAggregator.GetEvent<WindowRestoredEvent>().Subscribe(OnWindowRestored);
            _eventAggregator.GetEvent<ProjectClosedEvent>().Subscribe(OnProjectClosed);
            _eventAggregator.GetEvent<HideCurrentPageEvent>().Subscribe(OnHideCurrentPage);
            _eventAggregator.GetEvent<ReleaseCurrentPageEvent>().Subscribe(OnReleaseCurrentPage);

            // 订阅浮动窗口事件
            _eventAggregator.GetEvent<PageFloatedEvent>().Subscribe(OnPageFloated);
            _eventAggregator.GetEvent<PageEmbeddedEvent>().Subscribe(OnPageEmbedded);
            //_eventAggregator.GetEvent<FloatingWindowMinimizedEvent>().Subscribe(OnFloatingWindowMinimized);
            //_eventAggregator.GetEvent<FloatingWindowRestoredEvent>().Subscribe(OnFloatingWindowRestored);
            _eventAggregator.GetEvent<FloatingWindowActivatedEvent>().Subscribe(OnFloatingWindowActivated);
            _eventAggregator.GetEvent<WindowActivatedEvent>().Subscribe(OnWindowActivated);

            // 订阅项目保存状态变化事件
            _projectSaveStateService.SaveStateChanged += OnProjectSaveStateChanged;
        }

        private void InitializeCommands()
        {
            ToggleMinimizeCommand = new DelegateCommand<Window>(OnMinimizeWindow);
            ToggleMaximizeCommand = new DelegateCommand<Window>(OnMaximizeWindow);
            CloseMainWindowCommand = new DelegateCommand<Window>(OnCloseWindow);
            ShowProjectMenuCommand = new DelegateCommand(OnShowProjectMenu);
            NewProjectCommand = new DelegateCommand(OnNewProject);
            OpenProjectCommand = new DelegateCommand(OnOpenProject);
            SaveProjectCommand = new DelegateCommand(OnSaveProject, CanSaveProject);
            CloseProjectCommand = new DelegateCommand(OnCloseProject, CanCloseProject);
            NavigateCommand = new DelegateCommand<string>(Navigate);
            TreeItemDoubleClickCommand = new DelegateCommand<ProjectItem>(OnTreeItemDoubleClick);
            NavigationButtonClickCommand = new DelegateCommand<string>(OnNavigationButtonClick);
            HideBottomBarCommand = new DelegateCommand(OnHideBottomBar);
            ShowBottomBarCommand = new DelegateCommand(OnShowBottomBar);
            CloseTabCommand = new DelegateCommand<string>(OnCloseTab);
            RenamePxiChassisCommand = new DelegateCommand<string>(OnRenamePxiChassisFromTree);
            DeletePxiChassisFromTreeCommand = new DelegateCommand<string>(OnDeletePxiChassisFromTree);
            //AddPxiChassisToTreeCommand = new DelegateCommand<ProjectItem>(OnAddPxiChassisToTree);
            AddPxi2722G2ToTreeCommand = new DelegateCommand(OnAddPxi2722G2ToTree);
            AddPxi2519G2ToTreeCommand = new DelegateCommand(OnAddPxi2519G2ToTree);
            CreateTestTaskCommand = new DelegateCommand<ProjectItem>(OnCreateTestTask);
            RenameTestTaskCommand = new DelegateCommand<ProjectItem>(OnRenameTestTask);
            DeleteTestTaskCommand = new DelegateCommand<ProjectItem>(OnDeleteTestTask);
            CreateChannelConfigTabelCommand = new DelegateCommand<ProjectItem>(OnCreateChannelConfigTabel);
            CreateSignalConfigTabelCommand = new DelegateCommand<ProjectItem>(OnCreateSignalConfigTabel);
            CreateIcdConfigTabelCommand = new DelegateCommand<ProjectItem>(OnCreateIcdConfigTabel);
            CreateIcdMappingTabelCommand = new DelegateCommand<ProjectItem>(OnCreateIcdMappingTabel);
            CreateTestSequenceCommand = new DelegateCommand<ProjectItem>(OnCreateTestSequence);
            CreateReportConfigTabelCommand = new DelegateCommand<ProjectItem>(OnCreateReportConfigTabel);
            RenameConfigTabelCommand = new DelegateCommand<ProjectItem>(OnRenameConfigTabel);
            DeleteConfigTabelCommand = new DelegateCommand<ProjectItem>(OnDeleteConfigTabel);
            CreateTestInterfaceCommand = new DelegateCommand<ProjectItem>(OnCreateTestInterface);
            CreateMatrixSwitchConfigTableCommand = new DelegateCommand<ProjectItem>(OnCreateMatrixSwitchConfigTable);
            
            // 测试控制命令
            StartPauseTestCommand = new DelegateCommand(OnStartPauseTest);
            StopTestCommand = new DelegateCommand(OnStopTest);

            SelfInspectionCommand = new DelegateCommand(OnSelfInspection);

            if (FixedDemoMode)
            {
                ShowProjectMenuCommand = new DelegateCommand(() => { });
                NewProjectCommand = new DelegateCommand(() => { });
                OpenProjectCommand = new DelegateCommand(() => { });
                SaveProjectCommand = new DelegateCommand(() => { });
                CloseProjectCommand = new DelegateCommand(() => { });
                IsProjectMenuOpen = false;
            }
        }

        private void OnSelfInspection()
        {

        }

        //private async void OnSelfInspection()
        //{
        //TestProgressDialog progressDialog = null;
        //var progressVm = new TestProgressDialogViewModel();
        //ChassisModel selectedChassis = null;
        //string logFilePath = null;
        //try
        //{
        //    if (string.IsNullOrWhiteSpace(_currentProjectPath) || !File.Exists(_currentProjectPath))
        //    {
        //        ReMessageBox.Show("请先打开项目后再进行自检（需要项目路径保存日志）。", "提示",
        //            MessageBoxButton.OK, MessageBoxImage.Information);
        //        return;
        //    }

        //    if (CurrentProject == null || CurrentProject.Count == 0)
        //    {
        //        ReMessageBox.Show("项目未加载，请先打开项目。", "提示",
        //            MessageBoxButton.OK, MessageBoxImage.Information);
        //        return;
        //    }

        //    var dialog = new SelfInspectionDialog
        //    {
        //        Owner = Application.Current?.MainWindow
        //    };

        //    var vm = new SelfInspectionDialogViewModel();
        //    dialog.Initialize(CurrentProject, vm);

        //    var result = dialog.ShowDialog();
        //    if (result != true)
        //    {
        //        return;
        //    }

        //    selectedChassis = dialog.SelectedChassis;
        //    if (selectedChassis == null || string.IsNullOrWhiteSpace(selectedChassis.Name))
        //    {
        //        return;
        //    }

        //    // 保护：如果板卡已在面板中连接（缓存驱动已连接），则不允许自检，避免状态互相影响
        //    var chassisDevices = _pxiChassisService?.GetChassisDevices(selectedChassis.Name);
        //    if (chassisDevices != null)
        //    {
        //        foreach (var dev in chassisDevices)
        //        {
        //            if (dev == null) continue;
        //            var model = (dev.Model ?? string.Empty).ToUpperInvariant();
        //            if (!model.Contains("7131") && !model.Contains("PXIE-7131") &&
        //                !model.Contains("9774") && !model.Contains("PXIE-9774") && !model.Contains("PXI-9774") &&
        //                !model.Contains("X532") && !model.Contains("MT-X532") && !model.Contains("532") &&
        //                !model.Contains("X970") && !model.Contains("MT-X970"))
        //            {
        //                continue;
        //            }

        //            int slotIndex = -1;
        //            if (dev is Models.Devices.DeviceCategories.PxiDeviceBase pxi)
        //            {
        //                slotIndex = pxi.SlotIndex;
        //            }

        //            var cached = DriverFactory.GetCachedDriver(dev.Id, slotIndex);
        //            if (cached != null && cached.IsConnected)
        //            {
        //                ReMessageBox.Show(
        //                    "检测到板卡已在面板中连接/运行。\n请先在对应面板停止输出并关闭板卡，再执行自检。",
        //                    "提示",
        //                    MessageBoxButton.OK,
        //                    MessageBoxImage.Warning);
        //                return;
        //            }
        //        }
        //    }

        //    var projectDir = Path.GetDirectoryName(_currentProjectPath);
        //    var projectName = Path.GetFileNameWithoutExtension(_currentProjectPath) ?? string.Empty;
        //    var safeProjectName = MakeSafeFileName(projectName);
        //    var safeChassisName = MakeSafeFileName(selectedChassis.Name);
        //    logFilePath = Path.Combine(projectDir ?? string.Empty, $"{safeProjectName}_{safeChassisName}_自检.txt");

        //    // 自检进度弹窗
        //    progressDialog = new TestProgressDialog
        //    {
        //        Owner = Application.Current?.MainWindow,
        //        DataContext = progressVm
        //    };
        //    progressVm.Progress = 0;
        //    progressVm.HeaderText = "自检中";
        //    progressVm.StatusText = "准备自检...";
        //    progressDialog.Show();

        //    // 机箱节点黄色高亮
        //    SetChassisSelfInspectingHighlight(selectedChassis.Name, true);

        //    Action<int, string> reportProgress = (p, s) =>
        //    {
        //        try
        //        {
        //            Application.Current?.Dispatcher?.Invoke(() =>
        //            {
        //                progressVm.Progress = p;
        //                if (!string.IsNullOrWhiteSpace(s))
        //                    progressVm.StatusText = s;
        //            });
        //        }
        //        catch
        //        {
        //        }
        //    };

        //    await SelfInspectionRunner.RunChassisAsync(
        //        selectedChassis.Name,
        //        _pxiChassisService,
        //        logFilePath,
        //        line => System.Diagnostics.Debug.WriteLine(line),
        //        CancellationToken.None,
        //        reportProgress);
        //}
        //catch (OperationCanceledException)
        //{
        //    try
        //    {
        //        new MeasureControl.Helpers.SelfInspection.SelfInspectionContext(
        //                selectedChassis?.Name ?? string.Empty,
        //                logFilePath,
        //                null)
        //            .Log("自检被取消");
        //    }
        //    catch
        //    {
        //    }
        //}
        //catch (Exception ex)
        //{
        //    try
        //    {
        //        new MeasureControl.Helpers.SelfInspection.SelfInspectionContext(
        //                selectedChassis?.Name ?? string.Empty,
        //                logFilePath,
        //                null)
        //            .Log($"自检异常（整体流程）：{ex}");
        //    }
        //    catch
        //    {
        //    }
        //}
        //finally
        //{
        //    try
        //    {
        //        // 取消机箱高亮
        //        if (!string.IsNullOrWhiteSpace(SelfInspectingChassisName))
        //        {
        //            SetChassisSelfInspectingHighlight(SelfInspectingChassisName, false);
        //        }
        //    }
        //    catch { }
        //}
        //}

        private void SetChassisSelfInspectingHighlight(string chassisName, bool enable)
        {
            if (string.IsNullOrWhiteSpace(chassisName))
            {
                return;
            }

            // 纯 UI 高亮：不修改 ProjectItem（否则会触发“项目已修改”）
            SelfInspectingChassisName = enable ? chassisName : null;
        }

        private static string MakeSafeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "";
            }

            var invalid = Path.GetInvalidFileNameChars();
            var chars = name.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (invalid.Contains(chars[i]))
                {
                    chars[i] = '_';
                }
            }

            return new string(chars).Trim();
        }

        /// <summary>
        /// 开始/暂停测试
        /// </summary>
        private void OnStartPauseTest()
        {
            var hardwareService = HardwareControlService.Instance;

            if (!IsTestRunning)
            {
                // 显示测试启动选择对话框
                ShowTestStartDialog();
                return; // 对话框处理完成后会调用StartTestExecution
            }
            else if (!IsTestPaused)
            {
                // 暂停测试：暂停硬件轮询，保持连接
                try
                {
                    hardwareService.Pause();
                    IsTestPaused = true;

                    System.Diagnostics.Debug.WriteLine("[MainWindowViewModel] 硬件采集服务已暂停");

                    // 更新测试任务高亮状态（暂停状态）
                    UpdateTestTaskHighlighting();

                    RaisePropertyChanged(nameof(IsTestPaused));
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[MainWindowViewModel] 暂停硬件采集服务失败: {ex.Message}");
                }
            }
            else
            {
                // 恢复测试：恢复硬件轮询
                try
                {
                    hardwareService.Resume();
                    IsTestPaused = false;

                    System.Diagnostics.Debug.WriteLine("[MainWindowViewModel] 硬件采集服务已恢复");

                    // 更新测试任务高亮状态（运行状态）
                    UpdateTestTaskHighlighting();

                    RaisePropertyChanged(nameof(IsTestPaused));
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[MainWindowViewModel] 恢复硬件采集服务失败: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 停止测试
        /// </summary>
        private async void OnStopTest()
        {
            var hardwareService = HardwareControlService.Instance;

            try
            {
                // 停止硬件采集服务
                await hardwareService.StopAsync();

                IsTestRunning = false;
                IsTestPaused = false;

                // 清理数值更新服务的运行上下文
                if (_signalValueUpdateService != null)
                {
                    _signalValueUpdateService.SetRunningContext(null, null);
                    System.Diagnostics.Debug.WriteLine("[MainWindowViewModel] 已清理数值更新上下文");
                }

                System.Diagnostics.Debug.WriteLine("[MainWindowViewModel] 硬件采集服务已停止");

                RaisePropertyChanged(nameof(IsTestRunning));
                RaisePropertyChanged(nameof(IsTestPaused));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainWindowViewModel] 停止硬件采集服务失败: {ex.Message}");
            }
        }

        private void InitializeProjectMenu()
        {
            ProjectMenuItems = new ObservableCollection<MenuItemModel>
            {
                new MenuItemModel { Header = "新建项目", Command = NewProjectCommand },
                new MenuItemModel { Header = "打开项目", Command = OpenProjectCommand },
                new MenuItemModel { Header = "保存项目", Command = SaveProjectCommand },
                new MenuItemModel { Header = "关闭项目", Command = CloseProjectCommand }
            };
        }

        private void OnShowProjectMenu()
        {
            IsProjectMenuOpen = !IsProjectMenuOpen;
        }

        /// <summary>
        /// 显示测试启动选择对话框
        /// </summary>
        private void ShowTestStartDialog()
        {
            var dialog = new TestStartDialog
            {
                Owner = Application.Current?.MainWindow
            };

            var dialogVm = new TestStartDialogViewModel(_dialogService);
            dialog.Initialize(CurrentProject, dialogVm);

            var result = dialog.ShowDialog();
            if (result == true)
            {
                var selectedChassis = dialog.SelectedChassis;
                var selectedTestTask = dialog.SelectedTestTask;

                if (selectedChassis != null && selectedTestTask != null)
                {
                    StartTestExecution(selectedChassis, selectedTestTask);
                }
            }
        }

        /// <summary>
        /// 开始测试执行
        /// </summary>
        private async void StartTestExecution(ChassisModel chassis, ProjectItem testTask)
        {
            TestProgressDialog progressDialog = null;
            var progressVm = new TestProgressDialogViewModel();
            Window ownerWindow = null;
            EventHandler ownerStateChangedHandler = null;
            EventHandler ownerActivatedHandler = null;
            EventHandler ownerDeactivatedHandler = null;
            try
            {
                // 设置运行状态
                _runningChassis = chassis;
                _runningTestTask = testTask;
                IsTestRunning = true;
                IsTestPaused = false;

                // 设置数值更新服务的运行上下文
                if (_signalValueUpdateService != null)
                {
                    _signalValueUpdateService.SetRunningContext(chassis, testTask);
                    System.Diagnostics.Debug.WriteLine($"[MainWindowViewModel] 已设置数值更新上下文: Chassis={chassis?.Name}, TestTask={testTask?.Name}");
                }

                // 更新项目树高亮
                UpdateTestTaskHighlighting();

                // 显示配置进度弹窗
                ownerWindow = Application.Current?.MainWindow;
                progressDialog = new TestProgressDialog
                {
                    Owner = ownerWindow,
                    DataContext = progressVm
                };
                progressVm.HeaderText = "配置中";

                if (ownerWindow != null)
                {
                    progressDialog.Topmost = ownerWindow.WindowState != WindowState.Minimized;

                    ownerStateChangedHandler = (_, __) =>
                    {
                        if (progressDialog == null || ownerWindow == null)
                        {
                            return;
                        }

                        progressDialog.Topmost = ownerWindow.WindowState != WindowState.Minimized;
                    };
                    ownerActivatedHandler = (_, __) =>
                    {
                        if (progressDialog == null || ownerWindow == null)
                        {
                            return;
                        }

                        if (ownerWindow.WindowState != WindowState.Minimized)
                        {
                            progressDialog.Topmost = true;
                        }
                    };
                    ownerDeactivatedHandler = (_, __) =>
                    {
                        if (progressDialog == null)
                        {
                            return;
                        }

                        progressDialog.Topmost = false;
                    };

                    ownerWindow.StateChanged += ownerStateChangedHandler;
                    ownerWindow.Activated += ownerActivatedHandler;
                    ownerWindow.Deactivated += ownerDeactivatedHandler;
                }

                progressDialog.Show();

                // 获取实际的设备对象并启动
                var devices = await GetDevicesForTestTask(chassis, testTask);
                _runningDevices = devices;

                // 调试输出当前机箱下将要配置的设备列表
                if (devices != null)
                {
                    System.Diagnostics.Debug.WriteLine($"[MainWindowViewModel] 即将配置设备（机箱：{chassis?.Name}，任务：{testTask?.Name}）：");
                    foreach (var dev in devices)
                    {
                        System.Diagnostics.Debug.WriteLine($"  - {dev?.Name} ({dev?.DeviceTypeName}) Slot={dev?.SlotPosition}");
                    }
                }

                // 批量启动所有设备
                await StartAllDevices(devices, progressVm);

                System.Diagnostics.Debug.WriteLine($"[MainWindowViewModel] 测试启动成功: 机箱={chassis.Name}, 任务={testTask.Name}, 设备数量={devices.Count}");
                progressVm.Progress = 100;
                progressVm.StatusText = "配置完成";
                progressVm.IsCompleted = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainWindowViewModel] 测试启动失败: {ex.Message}");
                ReMessageBox.Show($"测试启动失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);

                // 清理状态
                StopAllRunningDevices();
                IsTestRunning = false;
                IsTestPaused = false;
                UpdateTestTaskHighlighting();

                if (progressVm != null)
                {
                    progressVm.IsFailed = true;
                    progressVm.ErrorText = ex.Message;
                }
            }
            finally
            {
                // 完成或失败后关闭进度窗口
                if (progressDialog != null)
                {
                    try
                    {
                        if (ownerWindow != null)
                        {
                            if (ownerStateChangedHandler != null)
                            {
                                ownerWindow.StateChanged -= ownerStateChangedHandler;
                            }
                            if (ownerActivatedHandler != null)
                            {
                                ownerWindow.Activated -= ownerActivatedHandler;
                            }
                            if (ownerDeactivatedHandler != null)
                            {
                                ownerWindow.Deactivated -= ownerDeactivatedHandler;
                            }
                        }
                    }
                    catch
                    {
                    }

                    progressDialog.Close();
                }
            }
        }

        /// <summary>
        /// 获取测试任务的设备列表（直接复用机箱中已创建的设备实例）
        /// </summary>
        private Task<List<Models.Devices.DeviceBase>> GetDevicesForTestTask(ChassisModel chassis, ProjectItem testTask)
        {
            var devices = new List<Models.Devices.DeviceBase>();

            // 从机箱服务中获取当前机箱的设备实例
            var chassisData = _pxiChassisService?.GetAllChassis()?.FirstOrDefault(c =>
                string.Equals(c.Name, chassis?.Name, StringComparison.OrdinalIgnoreCase));

            if (chassisData?.Devices != null)
            {
                // 默认使用机箱中全部非机箱/非控制器设备
                devices = chassisData.Devices
                    .Where(d =>
                    {
                        if (d == null) return false;

                        // 排除机箱自身、系统控制器
                        if (string.Equals(d.DeviceType, "Chassis", StringComparison.OrdinalIgnoreCase))
                            return false;
                        if (string.Equals(d.DeviceType, "Controller", StringComparison.OrdinalIgnoreCase))
                            return false;
                        if (d is ControllerDevice)
                            return false;
                        if ((d.DeviceTypeName ?? string.Empty).Contains("控制器"))
                            return false;

                        return true;
                    })
                    .ToList();

                System.Diagnostics.Debug.WriteLine($"[MainWindowViewModel] 机箱 {chassis?.Name} 包含设备：");
                foreach (var dev in devices)
                {
                    System.Diagnostics.Debug.WriteLine($"  * {dev.Name} ({dev.DeviceTypeName}) Slot={dev.SlotPosition}");
                }
            }

            return Task.FromResult(devices);
        }

        /// <summary>
        /// 启动所有设备
        /// </summary>
        private async Task StartAllDevices(List<Models.Devices.DeviceBase> devices, TestProgressDialogViewModel progressVm = null)
        {
            int total = devices?.Count ?? 0;
            int index = 0;
            foreach (var device in devices)
            {
                if (device != null)
                {
                    index++;
                    if (progressVm != null && total > 0)
                    {
                        progressVm.StatusText = $"配置 {device.Name} ({index}/{total})";
                        progressVm.Progress = (int)((index - 1) * 100.0 / total);
                    }

                    await StartDevice(device);

                    if (progressVm != null && total > 0)
                    {
                        progressVm.Progress = (int)(index * 100.0 / total);
                    }
                }
            }
        }

        /// <summary>
        /// 启动设备
        /// </summary>
        private async Task StartDevice(Models.Devices.DeviceBase device)
        {
            if (device == null)
            {
                return;
            }

            try
            {
                var driver = DriverFactory.CreateDriver(device);
                if (driver == null)
                {
                    System.Diagnostics.Debug.WriteLine($"[MainWindowViewModel] 未找到驱动，跳过设备 {device.Name}");
                    return;
                }

                // 仅执行配置（包含驱动连接），不在此处启动采集/输出，实际运行由 TestInterface 控制
                await ExecuteDeviceConfiguration(device, driver);

                System.Diagnostics.Debug.WriteLine($"[MainWindowViewModel] 设备启动成功: {device.Name}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainWindowViewModel] 设备启动失败 {device.Name}: {ex.Message}");
                // 未实现驱动等情况，跳过当前设备继续下一个
            }
        }

        /// <summary>
        /// 执行设备特定的配置
        /// </summary>
        private async Task ExecuteDeviceConfiguration(Models.Devices.DeviceBase device, IDeviceDriver driver)
        {
            if (_deviceConfigurationService == null)
            {
                System.Diagnostics.Debug.WriteLine("[MainWindowViewModel] 未注入设备配置服务，跳过配置");
                return;
            }

            if (driver == null)
            {
                System.Diagnostics.Debug.WriteLine("[MainWindowViewModel] 驱动为空，跳过配置");
                return;
            }

            // 委托给设备配置服务执行具体的配置逻辑
            await _deviceConfigurationService.ExecuteDeviceConfiguration(device, driver);
        }


        /// <summary>
        /// 停止所有运行中的设备
        /// </summary>
        private async void StopAllRunningDevices()
        {
            foreach (var device in _runningDevices)
            {
                if (device != null)
                {
                    await StopDevice(device);
                }
            }
            _runningDevices.Clear();
        }

        /// <summary>
        /// 暂停当前运行设备（仅停采集/输出，不断开）
        /// </summary>
        private async Task PauseRunningDevices()
        {
            if (_runningDevices == null) return;

            foreach (var device in _runningDevices)
            {
                var driver = DriverFactory.GetCachedDriver(device.Id);
                if (driver != null && driver.IsConnected)
                {
                    try
                    {
                        await driver.StopAcquisitionAsync();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[MainWindowViewModel] 暂停设备失败 {device.Name}: {ex.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// 恢复当前运行设备（在已连接状态下恢复采集/输出）
        /// </summary>
        private async Task ResumeRunningDevices()
        {
            if (_runningDevices == null) return;

            foreach (var device in _runningDevices)
            {
                var driver = DriverFactory.GetCachedDriver(device.Id);
                if (driver != null && driver.IsConnected)
                {
                    try
                    {
                        await driver.StartAcquisitionAsync();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[MainWindowViewModel] 恢复设备失败 {device.Name}: {ex.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// 停止单个设备
        /// </summary>
        private async Task StopDevice(Models.Devices.DeviceBase device)
        {
            try
            {
                var driver = DriverFactory.GetCachedDriver(device.Id);
                if (driver != null && driver.IsConnected)
                {
                    await driver.StopAcquisitionAsync();
                    await driver.DisconnectAsync();
                }
                System.Diagnostics.Debug.WriteLine($"[MainWindowViewModel] 设备停止成功: {device.Name}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainWindowViewModel] 设备停止失败 {device.Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// 更新测试任务的高亮显示
        /// </summary>
        private void UpdateTestTaskHighlighting()
        {
            // 清除之前的高亮
            ClearTestTaskHighlighting();

            if (IsTestRunning && _runningTestTask != null)
            {
                // 设置当前运行的测试任务为高亮状态
                // 暂停时显示黄色高亮，运行时显示绿色高亮
                _runningTestTask.Tag = IsTestPaused ? "Paused" : "Running";
                // 这里可以触发UI更新，通过事件或者属性通知
                RaisePropertyChanged(nameof(CurrentProject));
            }
        }

        /// <summary>
        /// 清除测试任务的高亮显示
        /// </summary>
        private void ClearTestTaskHighlighting()
        {
            if (_runningTestTask != null)
            {
                _runningTestTask.Tag = null;
            }
        }

        private void OnHideBottomBar()
        {
            IsBottomBarVisible = false;
        }

        private void OnShowBottomBar()
        {
            IsBottomBarVisible = true;
        }

        /// <summary>
        /// 关闭选项卡
        /// </summary>
        private void OnCloseTab(string pageName)
        {
            if (string.IsNullOrEmpty(pageName))
            {
                return;
            }

            // 发布ReleaseCurrentPageEvent事件
            _eventAggregator.GetEvent<ReleaseCurrentPageEvent>().Publish(pageName);
        }

        private void OnNewProject()
        {
            IsProjectMenuOpen = false;

            if (CurrentProject != null)
            {
                if (!CheckAndCloseCurrentProject())
                    return;
            }

            var saveFileDialog = new SaveFileDialog
            {
                Filter = AppConstants.ProjectFileFilter,
                Title = "新建项目",
                FileName = AppConstants.DefaultProjectName
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                try
                {
                    string projectName = Path.GetFileNameWithoutExtension(saveFileDialog.FileName);
                    string projectPath = saveFileDialog.FileName;

                    // 新建项目前，确保清空旧项目的各类缓存和服务状态
                    _pxiChassisService.LoadChassisData(null);
                    _chassisConnectionService.ClearConnections();
                    _channelBindingService.ClearAll();
                    ChannelConfigTabelViewModel.ClearAllChannelTabelItems();
                    SignalConfigTabelViewModel.ClearAllSignalTabelItems();
                    IcdMappingTabelViewModel.ClearAllIcdMappingItems();
                    IcdConfigTabelViewModel.ClearAllIcdTabelItems();

                    var newProject = _projectService.CreateNewProject(projectName, projectPath);
                    
                    // 确保项目对象完全初始化后再保存和绑定
                    if (newProject == null)
                    {
                        throw new Exception("创建的项目对象为空");
                    }

                    if (FixedDemoMode)
                    {
                        EnsureFixedDemoProjectTree(newProject);
                    }
                    
                    // 再次确保所有属性都已初始化
                    _projectService.EnsureProjectItemProperties(newProject);
                    
                    _projectService.SaveProject(newProject, projectPath);

                    // 创建集合并设置
                    var projectCollection = new ObservableCollection<ProjectItem> { newProject };
                    CurrentProject = projectCollection;
                    _currentProjectPath = projectPath;
                    CalibrationPathHelper.SetProjectPath(projectPath);
                    RaisePropertyChanged(nameof(CurrentProjectFilePath));
                    _projectSaveStateService.MarkAsSaved(); // 标记为已保存
                    IsProjectModified = false; // 新项目未修改

                    // 发布项目打开事件，通知订阅者刷新状态为新项目
                    _eventAggregator.GetEvent<ProjectOpenedEvent>().Publish(newProject);

                    // 新建项目完成后，导航到HomePage，防止旧页面残留状态
                    _regionManager.RequestNavigate(AppConstants.MainRegionName, "HomePage");

                    // 展开项目树的一级目录
                    ExpandProjectTreeLevel1(); //TODO: 是否需要展开更多级别？
                }
                catch (Exception ex)
                {
                    // 打印详细错误信息到输出窗口
                    System.Diagnostics.Trace.WriteLine("========== 创建项目失败 ==========");
                    System.Diagnostics.Trace.WriteLine($"异常类型: {ex.GetType().FullName}");
                    System.Diagnostics.Trace.WriteLine($"错误消息: {ex.Message}");
                    System.Diagnostics.Trace.WriteLine("堆栈跟踪:");
                    System.Diagnostics.Trace.WriteLine(ex.StackTrace);
                    
                    if (ex.InnerException != null)
                    {
                        System.Diagnostics.Trace.WriteLine("---------- 内部异常 ----------");
                        System.Diagnostics.Trace.WriteLine($"内部异常类型: {ex.InnerException.GetType().FullName}");
                        System.Diagnostics.Trace.WriteLine($"内部异常消息: {ex.InnerException.Message}");
                        System.Diagnostics.Trace.WriteLine("内部异常堆栈:");
                        System.Diagnostics.Trace.WriteLine(ex.InnerException.StackTrace);
                    }
                    System.Diagnostics.Trace.WriteLine("================================");
                }
            }
        }

        private async void OnOpenProject()
        {
            IsProjectMenuOpen = false;

            var openFileDialog = new OpenFileDialog
            {
                Filter = AppConstants.ProjectFileFilter,
                Title = "打开项目",
                Multiselect = false
            };

            if (openFileDialog.ShowDialog() != true)
            {
                return;
            }

            var selectedProjectPath = openFileDialog.FileName;
            if (string.IsNullOrWhiteSpace(selectedProjectPath))
            {
                return;
            }

            if (HasAnyActiveHardware())
            {
                var confirm = ReMessageBox.Show(
                    "切换项目将停止并断开所有运行中的板卡，是否继续？",
                    "切换项目",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (confirm != MessageBoxResult.Yes)
                {
                    return;
                }
            }

            await ShutdownAllHardwareAsync();

            if (CurrentProject != null)
            {
                if (!CheckAndCloseCurrentProject())
                {
                    return;
                }
            }

            try
            {
                var project = _projectService.LoadProject(selectedProjectPath);
                if (project != null)
                {
                    System.Diagnostics.Debug.WriteLine($"[MainWindow] Project loaded successfully. CalibrationRecords: {(project.CalibrationRecords == null ? "null" : $"Count={project.CalibrationRecords.Count}")}");
                    ApplyLoadedProject(project, selectedProjectPath);
                }
                else
                {
                    ReMessageBox.Show($"项目文件 '{Path.GetFileName(selectedProjectPath)}' 为空或格式不正确", "错误",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                ReMessageBox.Show($"打开项目失败：{ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private bool HasAnyActiveHardware()
        {
            try
            {
                if (HardwareControlService.Instance != null && HardwareControlService.Instance.IsRunning)
                {
                    return true;
                }

                if (_runningDevices != null && _runningDevices.Count > 0)
                {
                    return true;
                }

                foreach (var driver in Drivers.DriverFactory.GetCachedDrivers())
                {
                    if (driver != null && driver.IsConnected)
                    {
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainWindow] HasAnyActiveHardware check failed: {ex.Message}");
            }

            return false;
        }

        private async Task ShutdownAllHardwareAsync()
        {
            try
            {
                if (HardwareControlService.Instance != null)
                {
                    await HardwareControlService.Instance.StopAsync();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainWindow] HardwareControlService.StopAsync failed: {ex.Message}");
            }

            try
            {
                await Drivers.DriverFactory.ShutdownAllAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainWindow] DriverFactory.ShutdownAllAsync failed: {ex.Message}");
            }

            try
            {
                HardwareControlService.Instance?.ClearAll();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainWindow] HardwareControlService.ClearAll failed: {ex.Message}");
            }
        }

        private bool CanSaveProject()
        {
            return CurrentProject != null && !string.IsNullOrEmpty(_currentProjectPath);
        }

        private void OnSaveProject()
        {
            IsProjectMenuOpen = false;

            if (CurrentProject == null || CurrentProject.Count == 0)
            {
                ReMessageBox.Show("当前没有打开的项目", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrEmpty(_currentProjectPath))
            {
                ReMessageBox.Show("无法找到项目路径", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                SaveProjectInternal();
                ReMessageBox.Show($"项目 '{CurrentProject[0].Name}' 保存成功！", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                ReMessageBox.Show($"保存项目失败：{ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        private bool CanCloseProject()
        {
            return CurrentProject != null;
        }

        private async void OnCloseProject()
        {
            IsProjectMenuOpen = false;

            if (HasAnyActiveHardware())
            {
                var confirm = ReMessageBox.Show(
                    "关闭项目将停止并断开所有运行中的板卡，是否继续？",
                    "关闭项目",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (confirm != MessageBoxResult.Yes)
                {
                    return;
                }
            }

            await ShutdownAllHardwareAsync();

            CheckAndCloseCurrentProject();
        }

        private bool CheckAndCloseCurrentProject(bool allowCancel = true)
        {
            if (CurrentProject == null || CurrentProject.Count == 0)
                return true;

            if (!CheckAllOpenPanelsCanClose())
            {
                return false;
            }

            // 如果项目没有修改，直接关闭
            if (!HasUnsavedChanges)
            {
                CloseProject();
                return true;
            }

            var projectToSave = CurrentProject[0]; //TODO：同时打开多个项目

            var result = ReMessageBox.Show(
                $"是否保存项目的更改？",
                "关闭项目",
                allowCancel ? MessageBoxButton.YesNoCancel : MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Cancel)
                return false;

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    SaveProjectInternal();
                }
                catch (Exception ex)
                {
                    var continueClose = ReMessageBox.Show(
                        $"保存项目失败：{ex.Message}\n\n是否仍然关闭项目？",
                        "错误",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Error);

                    if (continueClose == MessageBoxResult.No)
                        return false;
                }
            }

            CloseProject();
            return true;
        }


        private void CloseProject()
        {
            if (CurrentProject != null && CurrentProject.Count > 0)
            {
                string projectName = CurrentProject[0].Name;

                // 1. 清理项目数据
                CurrentProject = null;
                _currentProjectPath = null;
                RaisePropertyChanged(nameof(CurrentProjectFilePath));
                IsProjectModified = false; // 重置修改状态
                _projectSaveStateService.Reset(); // 重置保存状态
                _projectService.ClearCurrentProjectRoot();
                CalibrationPathHelper.Reset();

                // 2. 清空机箱服务中的数据
                _pxiChassisService.LoadChassisData(null);

                // 3. 清空连接服务中的数据
                _chassisConnectionService.ClearConnections();

                // 4. 清空通道绑定服务中的数据
                _channelBindingService.ClearAll();

                // 5. 清空通道、信号和通讯信号配置表的静态字典
                ChannelConfigTabelViewModel.ClearAllChannelTabelItems();
                SignalConfigTabelViewModel.ClearAllSignalTabelItems();
                IcdMappingTabelViewModel.ClearAllIcdMappingItems();
                // 同步清空ICD配置（与无导航关闭保持一致）
                IcdConfigTabelViewModel.ClearAllIcdTabelItems();
                System.Diagnostics.Debug.WriteLine("[Project] Cleared Channel/Signal/ICD static tables");

                // 5.5 清空驱动缓存，防止跨项目残留
                Drivers.DriverFactory.ClearCache();

                // 5.6 关闭所有浮动窗口，防止跨项目残留
                FloatingWindowHelper.CloseAllFloatingWindows();

                // 6. 清理导航状态
                NavigationButtons.Clear();
                _navigationHistory.Clear();
                _navigationState.Clear();
                _currentPageName = null;

                // 7. 发布清理事件，通知其他ViewModel清理状态
                _eventAggregator.GetEvent<ProjectClosedEvent>().Publish();

                // 8. 项目关闭后，导航到HomePage
                _regionManager.RequestNavigate(AppConstants.MainRegionName, "HomePage");
            }
        }

        /// <summary>
        /// 关闭项目但不进行导航
        /// </summary>
        private void CloseProjectWithoutNavigation()
        {
            if (CurrentProject != null && CurrentProject.Count > 0)
            {
                string projectName = CurrentProject[0].Name;

                // 1. 清理项目数据
                CurrentProject = null;
                _currentProjectPath = null;
                RaisePropertyChanged(nameof(CurrentProjectFilePath));
                _projectSaveStateService.Reset(); // 重置保存状态
                IsProjectModified = false; // 重置修改状态

                // 2. 清空机箱服务中的数据
                _pxiChassisService.LoadChassisData(null);

                // 3. 清空连接服务中的数据
                _chassisConnectionService.ClearConnections();

                // 4. 清空通道绑定服务中的数据
                _channelBindingService.ClearAll();

                // 5. 清空通道、信号和通讯信号配置表的静态字典
                ChannelConfigTabelViewModel.ClearAllChannelTabelItems();
                SignalConfigTabelViewModel.ClearAllSignalTabelItems();
                IcdMappingTabelViewModel.ClearAllIcdMappingItems();
                IcdConfigTabelViewModel.ClearAllIcdTabelItems();

                // 6. 清理导航状态
                NavigationButtons.Clear();
                _navigationHistory.Clear();
                _navigationState.Clear();
                _currentPageName = null;

                // 7. 发布清理事件，通知其他ViewModel清理状态
                _eventAggregator.GetEvent<ProjectClosedEvent>().Publish();
            }
        }

        private async void OnCloseWindow(Window window)
        {
            if (window != null && !_isClosing)
            {
                _isClosing = true;
                try
                {
                    if (HasAnyActiveHardware())
                    {
                        var confirm = ReMessageBox.Show(
                            "退出程序将停止并断开所有运行中的板卡，是否继续？",
                            "退出程序",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Question);

                        if (confirm != MessageBoxResult.Yes)
                        {
                            return;
                        }
                    }

                    await ShutdownAllHardwareAsync();

                    if (!CheckAllOpenPanelsCanClose())
                    {
                        return;
                    }

                    // 自动保存项目（proj.json层面），无需提示
                    if (CurrentProject != null && CurrentProject.Count > 0)
                    {
                        try
                        {
                            SaveProjectInternal();
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[MainWindow] 自动保存项目失败: {ex.Message}");
                        }
                    }

                    // 关闭项目但不进行导航
                    CloseProjectWithoutNavigation();

                    // 关闭所有浮动窗口
                    FloatingWindowHelper.CloseAllFloatingWindows();

                    window.Close();
                }
                finally
                {
                    _isClosing = false;
                }
            }
        }

        private bool CheckAllOpenPanelsCanClose()
        {
            try
            {
                foreach (var guard in EnumerateOpenCloseGuards())
                {
                    if (!guard.CanClose())
                    {
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainWindow] CheckAllOpenPanelsCanClose failed: {ex.Message}");
            }

            return true;
        }

        private IEnumerable<ICloseGuard> EnumerateOpenCloseGuards()
        {
            foreach (var guard in EnumerateRegionCloseGuards(_regionManager, AppConstants.MainRegionName))
            {
                yield return guard;
            }

            if (Application.Current?.Windows == null)
            {
                yield break;
            }

            foreach (Window window in Application.Current.Windows)
            {
                if (window is FloatingWindow floatingWindow)
                {
                    var scopedRegionManager = Prism.Regions.RegionManager.GetRegionManager(floatingWindow)
                        ?? Prism.Regions.RegionManager.GetRegionManager(floatingWindow.Content as DependencyObject);

                    foreach (var guard in EnumerateRegionCloseGuards(scopedRegionManager, "FloatingRegion"))
                    {
                        yield return guard;
                    }
                }
            }
        }

        private IEnumerable<ICloseGuard> EnumerateRegionCloseGuards(IRegionManager regionManager, string regionName)
        {
            if (regionManager == null || string.IsNullOrWhiteSpace(regionName))
            {
                yield break;
            }

            if (!regionManager.Regions.ContainsRegionWithName(regionName))
            {
                yield break;
            }

            var region = regionManager.Regions[regionName];
            foreach (var view in region.Views)
            {
                if (view is FrameworkElement fe)
                {
                    if (fe.DataContext is ICloseGuard vmGuard)
                    {
                        yield return vmGuard;
                    }
                }
                else if (view is ICloseGuard viewGuard)
                {
                    yield return viewGuard;
                }
            }
        }

        private void OnTreeItemDoubleClick(ProjectItem item)
        {
            if (item == null)
            {
                return;
            }

            if (IsDemoLockedConfigNode(item))
            {
                return;
            }

            var hasChildren = item.Children != null && item.Children.Count > 0;
            var isNavigableType = item.Type == "PXIChassis" ||
                                  item.Type == "tdm_system" ||
                                  item.Type == "task_database" ||
                                  item.Type == "test_database" ||
                                  item.Type == AppConstants.NodeTypeTestTask ||
                                  IsConfigTabelType(item.Type);

            // 非叶子节点且不是导航类型，则不处理
            if (hasChildren && !isNavigableType)
            {
                return;
            }

            string pageType = null;
            string instanceId = null;
            Dictionary<string, object> navParams = null;

            System.Diagnostics.Debug.WriteLine($"[TreeDoubleClick] Item Name={item.Name}, Type={item.Type}, HasChildren={(item.Children?.Count ?? 0)}, ParentTestTask={GetParentTestTaskName(item)}, ParentChassis={GetParentChassisName(item)}");

            switch (item.Name)
            {
                case "设备与网络":
                    pageType = "HardwareConfig";
                    break;
                default:
                    if (item.Type == "PXIChassis")
                    {
                        pageType = "PxiChassis";
                        instanceId = item.Name;
                        navParams = new Dictionary<string, object>
                        {
                            { "ChassisName", item.Name }
                        };
                    }
                    else if (item.Type == AppConstants.NodeTypeTestTask)
                    {
                        pageType = "BoardTest";

                        var chassisName = GetParentChassisName(item);
                        if (!string.IsNullOrEmpty(chassisName))
                        {
                            instanceId = $"{chassisName}-{item.Name}";
                        }
                        else
                        {
                            instanceId = item.Name;
                        }

                        navParams = new Dictionary<string, object>
                        {
                            { "TestTaskName", item.Name },
                            { "BoardType", item.Tag },
                            { "ParentChassisName", chassisName ?? string.Empty },
                            { "ProjectData", CurrentProject?.FirstOrDefault() }
                        };
                    }
                    else if (item.Type == "tdm_system")
                    {
                        pageType = "TDMSystem";
                    }
                    else if (item.Type == "task_database")
                    {
                        pageType = "DatabaseConfig";
                        navParams = new Dictionary<string, object>
                        {
                            { "DatabaseType", "TaskDatabase" }
                        };
                    }
                    else if (item.Type == "test_database")
                    {
                        pageType = "DatabaseConfig";
                        navParams = new Dictionary<string, object>
                        {
                            { "DatabaseType", "TestDatabase" }
                        };
                    }
                    // 配置表子项（多例页面）
                    else if (IsConfigTabelType(item.Type))
                    {
                        var testTaskName = GetParentTestTaskName(item);
                        if (string.IsNullOrEmpty(testTaskName))
                        {
                            // 如果找不到测试任务名称，尝试通过项目树结构直接查找
                            testTaskName = GetParentTestTaskNameAlternative(item);
                        }

                        var chassisName = GetParentChassisName(item);

                        if (!string.IsNullOrEmpty(testTaskName))
                        {
                            pageType = GetPageTypeByItemType(item.Type);
                            // 使用 "机箱名-测试任务名-配置表名" 作为instanceId，确保不同机箱的配置表分开
                            if (!string.IsNullOrEmpty(chassisName))
                            {
                                instanceId = $"{chassisName}-{testTaskName}-{item.Name}";
                            }
                            else
                            {
                                instanceId = $"{testTaskName}-{item.Name}";
                            }

                            navParams = new Dictionary<string, object>
                            {
                                { "TestTaskName", testTaskName },
                                { "ConfigTabelName", item.Name },
                                { "ChassisName", chassisName ?? string.Empty },
                                { "ParentType", GetParentType(item) },
                                { "ProjectData", CurrentProject?.FirstOrDefault() }
                            };
                        }
                        else
                        {
                            ReMessageBox.Show($"无法找到通道配置表 \"{item.Name}\" 所属的测试任务，请检查项目结构。", "导航失败");
                        }
                    }
                    break;
            }

            if (!string.IsNullOrEmpty(pageType))
            {
                try
                {
                    _navigationService.NavigateToPage(pageType, instanceId, navParams);
                }
                catch (Exception ex)
                {
                    ReMessageBox.Show($"导航失败: {ex.Message}");
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[TreeDoubleClick] 未找到pageType，Item Name={item.Name}, Type={item.Type}");
            }
        }

        /// <summary>
        /// 根据项目类型获取页面类型
        /// </summary>
        private string GetPageTypeByItemType(string itemType)
        {
            return itemType switch
            {
                "channel_config_table" => "ChannelConfigTabel",
                "channel_config_tabel" => "ChannelConfigTabel",
                "ncommunicating_signal_config_table" => "SignalConfigTabel",
                "signal_config_tabel" => "SignalConfigTabel",
                "icd_mapping_table" => "IcdMappingTabel",
                "icd_mapping_tabel" => "IcdMappingTabel",
                "icd_config_table" => "IcdConfigTabel",
                "icd_config_tabel" => "IcdConfigTabel",
                "test_sequence_tabel" => "TestSequence",
                "report_config_tabel" => "ReportConfigTabel",
                "test_interface" => "TestInterface",
                _ => null
            };
        }

        private void Navigate(string viewName)
        {
            try
            {
                // 将当前页面添加到导航历史中（如果不是相同的页面）
                if (!string.IsNullOrEmpty(_currentPageName) && _currentPageName != viewName)
                {
                    _navigationHistory.Push(_currentPageName);
                }

                // 设置当前页面名称
                _currentPageName = viewName;

                if (_regionManager.Regions.ContainsRegionWithName("MainRegion"))
                {
                    _regionManager.RequestNavigate("MainRegion", viewName, result =>
                    {
                        // 导航成功后设置按钮高亮状态
                        if (result.Result == true)
                        {
                            // 根据viewName找到对应的按钮名称
                            var buttonName = GetButtonNameByViewName(viewName);
                            if (!string.IsNullOrEmpty(buttonName))
                            {
                                SetActiveNavigationButton(buttonName);
                            }
                        }
                    });
                }
                else
                {
                    ReMessageBox.Show("MainRegion 未找到，请检查 MainWindow.xaml 中的区域定义。");
                }
            }
            catch (Exception ex)
            {
                ReMessageBox.Show($"导航失败: {ex.Message}");
            }
        }

        public void AddNavigationButton(string pageName)
        {
            var existingButton = NavigationButtons.FirstOrDefault(b => b.Name == pageName);

            if (existingButton != null)
            {
                // 页面已存在，标记为打开并推到激活历史最前
                _navigationState.OpenPage(pageName);
                SetActiveNavigationButton(pageName);
                AddToNavigationHistory(pageName);
                return;
            }

            var navigationButton = new NavigationButton
            {
                Name = pageName,
                DisplayName = pageName, 
                Tag = pageName,
                IsActive = false
            };

            // 插入到正确的位置：设备与网络 → 机箱 → 测试任务界面
            InsertNavigationButtonInOrder(navigationButton);

            // 添加到NavigationStateService
            _navigationState.OpenPage(pageName);

            SetActiveNavigationButton(pageName);
            AddToNavigationHistory(pageName);
        }

        /// <summary>
        /// 添加页面到导航历史
        /// </summary>
        private void AddToNavigationHistory(string pageName)
        {
            if (!string.IsNullOrEmpty(_currentPageName) && _currentPageName != pageName)
            {
                _navigationHistory.Push(_currentPageName);
            }
            _currentPageName = pageName;
        }

        ///// <summary>
        ///// 导航到前一个页面
        ///// </summary>
        //public void NavigateToPreviousPage()
        //{
        //    // 从剩余的导航按钮中找到前一个页面
        //    if (NavigationButtons.Count > 0)
        //    {
        //        // 选择最后一个按钮作为前一个页面
        //        var previousButton = NavigationButtons.Last();
        //        OnNavigationButtonClick(previousButton.Name);
        //    }
        //    else
        //    {
        //        // 如果没有剩余的导航按钮，MainRegion显示空白
        //    }
        //}

        /// <summary>
        /// 隐藏当前页面，导航到前一个页面
        /// </summary>
        public void HideCurrentPage(bool isMinimize = true)
        {
            try
            {
                // 获取当前激活的导航按钮
                var activeButton = NavigationButtons.FirstOrDefault(b => b.IsActive);
                if (activeButton == null)
                {
                    return;
                }

                string currentPageName = activeButton.Name;

                // 使用NavigationStateService标记状态
                if (isMinimize)
                {
                    _navigationState.MarkMinimized(currentPageName);
                }
                else
                {
                    // 对于嵌入页面的浮动操作
                    _navigationState.MarkFloating(currentPageName, currentPageName);
                }

                // 获取下一个可导航的页面（在标记状态之后，排除当前页面），默认fallback为HomePage
                string nextPageName = _navigationState.GetNextPageOrFallback("HomePage", currentPageName);

                // 将当前按钮设置为非激活状态（但不移除）
                activeButton.IsActive = false;

                if (string.IsNullOrEmpty(nextPageName))
                {
                    // 没有其他可显示的页面，导航到HomePage
                    _currentPageName = null;
                    NavigateToHomePage();
                }
                else
                {
                    // 导航到下一个页面
                    OnNavigationButtonClick(nextPageName);
                }

                // 浮动页面的导航按钮保持激活状态（白色）
                if (!isMinimize)
                {
                    activeButton.IsActive = true;
                }

            }
            catch (Exception ex)
            {
                ReMessageBox.Show($"隐藏页面失败: {ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        /// <summary>
        /// 释放当前页面
        /// </summary>
        public void ReleaseCurrentPage()
        {
            try
            {
                // 获取当前激活的导航按钮
                var activeButton = NavigationButtons.FirstOrDefault(b => b.IsActive);
                if (activeButton == null)
                {
                    return;
                }

                var pageName = activeButton.Name;
                ReleasePageByName(pageName);
            }
            catch (Exception ex)
            {
                ReMessageBox.Show($"释放页面失败: {ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 根据页面名称释放指定页面
        /// </summary>
        /// <param name="pageName">要释放的页面名称</param>
        private void ReleasePageByName(string pageName)
        {
            try
            {
                // 查找指定的导航按钮
                var buttonToRemove = NavigationButtons.FirstOrDefault(b => b.Name == pageName);
                if (buttonToRemove == null)
                {
                    return;
                }

                // 1. 关闭所有该类型的浮动窗口
                FloatingWindowHelper.CloseAllFloatingWindowsByPageName(pageName);

                // 2. 移除MainRegion中的视图（如果存在）
                var mainRegion = _regionManager.Regions["MainRegion"];

                // 从pageKey提取ViewName（通过按钮的ViewName属性）
                string viewNameToFind = buttonToRemove.ViewName;

                var viewsToRemove = mainRegion.Views
                    .Where(v => v.GetType().Name == viewNameToFind)
                    .ToList();

                foreach (var view in viewsToRemove)
                {
                    // 对于多实例页面，需要进一步匹配实例ID
                    bool shouldRemove = true;

                    if (view is FrameworkElement fe && fe.DataContext != null)
                    {
                        var dataContext = fe.DataContext;

                        // 检查PxiChassis类型（通过ChassisName匹配）
                        if (viewNameToFind == "PxiChassis")
                        {
                            var chassisNameProp = dataContext.GetType().GetProperty("ChassisName");
                            if (chassisNameProp != null)
                            {
                                var chassisName = chassisNameProp.GetValue(dataContext) as string;
                                // 检查pageKey是否包含该机箱名
                                if (!pageName.Contains(chassisName))
                                {
                                    shouldRemove = false;
                                }
                            }
                        }
                        // 检查测试任务类型的配置表（通过TestTaskName和ConfigTableName匹配）
                        else if (viewNameToFind == "ChannelConfigTable" ||
                                 viewNameToFind == "SignalConfigTable" ||
                                 viewNameToFind == "CommunicatingSignalConfigTable" ||
                                 viewNameToFind == "IcdMappingTable" ||
                                 viewNameToFind == "IcdConfigTabel" ||
                                 viewNameToFind == "TestSequence" ||
                                 viewNameToFind == "ReportConfigTabel")
                        {
                            var testTaskNameProp = dataContext.GetType().GetProperty("TestTaskName");
                            var configTabelNameProp = dataContext.GetType().GetProperty("ConfigTabelName");

                            if (testTaskNameProp != null && configTabelNameProp != null)
                            {
                                var testTaskName = testTaskNameProp.GetValue(dataContext) as string;
                                var configTabelName = configTabelNameProp.GetValue(dataContext) as string;

                                // 检查pageKey是否匹配 "PageType_任务名-配置表名"
                                string expectedSuffix = $"{testTaskName}-{configTabelName}";
                                if (!pageName.Contains(expectedSuffix))
                                {
                                    shouldRemove = false;
                                }
                            }
                        }
                    }

                    if (!shouldRemove)
                    {
                        continue; // 不是要移除的实例，跳过
                    }

                    // 释放ViewModel资源
                    if (view is FrameworkElement element && element.DataContext is IDisposable disposable)
                    {
                        disposable.Dispose();
                    }
                    mainRegion.Remove(view);
                }

                // 使用NavigationStateService关闭页面（会从所有集合和历史中移除）
                _navigationState.ClosePage(pageName);

                // 获取当前激活的按钮
                var activeButton = NavigationButtons.FirstOrDefault(b => b.IsActive);
                bool wasActive = (activeButton == buttonToRemove);

                // 从导航按钮中移除指定页面
                NavigationButtons.Remove(buttonToRemove);

                // 如果移除的是当前激活的页面，需要导航到其他页面
                if (wasActive)
                {
                    // 使用NavigationStateService获取下一个可导航的页面，默认fallback为HomePage
                    string nextPageName = _navigationState.GetNextPageOrFallback("HomePage");

                    if (string.IsNullOrEmpty(nextPageName))
                    {
                        // 没有其他可显示的页面，导航到HomePage
                        _currentPageName = null;
                        NavigateToHomePage();
                    }
                    else
                    {
                        OnNavigationButtonClick(nextPageName);
                    }
                }
            }
            catch (Exception ex)
            {
                ReMessageBox.Show($"释放页面失败: {ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SetActiveNavigationButton(string activeButtonName)
        {
            // 委托给NavigationService
            _navigationService.SetActiveButton(activeButtonName);


            // 记录当前激活的按钮数量
            var activeCount = NavigationButtons.Count(b => b.IsActive);
        }

        /// <summary>
        /// 从pageKey提取页面类型名
        /// </summary>
        /// <param name="pageKey">完整的页面键（可能包含实例标识）</param>
        /// <returns>页面类型名</returns>
        private string ExtractPageType(string pageKey)
        {
            if (string.IsNullOrEmpty(pageKey))
                return pageKey;

            // 配置表格式: PageType_TestTask_ConfigTabel
            // 机箱格式: PageType_ChassisName
            // 单例格式: PageType

            int firstUnderscore = pageKey.IndexOf('_');
            if (firstUnderscore == -1)
            {
                // 单例页面，无后缀
                return pageKey;
            }

            // 提取第一个下划线之前的部分
            return pageKey.Substring(0, firstUnderscore);
        }


        private void OnMinimizeWindow(Window window)
        {
            if (window != null)
            {
                // 最小化整个主窗口（而不是隐藏页面）
                window.WindowState = WindowState.Minimized;
            }
        }

        private void OnMaximizeWindow(Window window)
        {
            if (window != null)
            {
                // 使用 WindowManager 服务进行真正的窗口全屏最大化
                _windowManager?.ToggleMaximizeWindow(window);
            }
        }

        private void StartTimeUpdater()
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (s, e) => CurrentTime = DateTime.Now.ToString(AppConstants.TimeFormat);
            _timer.Start();
        }

        //private void InitializeTools()
        //{
        //    // 添加工具项
        //    Tools.Add(new ProjectItem
        //    {
        //        Name = "示例工具",
        //        Icon = "/Resources/Logo/tool.png"
        //    });
        //}


        /// <summary>
        /// 导航到首页
        /// </summary>
        private void NavigateToHomePage()
        {
            try
            {
                _navigationService.NavigateToHomePage();
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// 主窗口启动时导航到首页（由MainWindow.Loaded事件调用）
        /// </summary>
        public void NavigateToHomePageOnStartup()
        {         
            _regionManager.RequestNavigate(AppConstants.MainRegionName, "HomePage");            
        }

        #endregion

        #region Navigation Methods

        public void OnNavigationButtonClick(string pageName)
        {
            // 使用NavigationStateService处理状态
            _navigationState.Unminimize(pageName);

            if (_navigationState.IsFloating(pageName))
            {
                // 检查是否最小化
                var floatingStates = _navigationState.GetFloatingPageStates();
                var pageState = floatingStates.FirstOrDefault(s => s.PageKey == pageName);

                // 先激活浮动窗口（最高优先级）
                if (pageState?.IsMinimized == true)
                {
                    // 从最小化恢复
                    FloatingWindowHelper.RestoreFloatingWindowFromMinimized(pageName, _navigationState, _eventAggregator);
                }
                else
                {
                    // 直接激活浮动窗口
                    // 浮动窗口的 Activated 事件会自动触发按钮高亮
                    FloatingWindowHelper.ActivateFloatingWindow(pageName);
                }

                return;
            }

            // 使用NavigationService导航（PageKey方式）
            try
            {
                _navigationService.NavigateByPageKey(pageName);
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// 导航到指定页面（供外部调用）
        /// </summary>
        /// <param name="pageName">页面名称</param>
        public void NavigateToPage(string pageName)
        {
            OnNavigationButtonClick(pageName);
        }

        /// <summary>
        /// 处理页面浮动事件
        /// </summary>
        private void OnPageFloated(PageFloatedEventArgs args)
        {
            try
            {
                string pageKey = args.PageName;

                // 提取PageName（页面类型）
                string pageName = ExtractPageType(pageKey);

                // 设置当前激活的浮动窗口
                _navigationState.SetActiveFloatingPageKey(pageKey);
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// 处理页面嵌入事件
        /// </summary>
        private void OnPageEmbedded(PageEmbeddedEventArgs args)
        {
            try
            {
                string pageKey = args.PageName;

                // 提取 PageName（页面类型）
                string pageName = ExtractPageType(pageKey);

                // 记录嵌入前的CurrentProject状态
                int projectCountBefore = CurrentProject?.Count ?? -1;

                // 推到激活历史最前
                _navigationState.PushActivated(pageKey);

                // 激活对应的导航按钮并导航
                OnNavigationButtonClick(pageKey);

                // 记录嵌入后的CurrentProject状态
                int projectCountAfter = CurrentProject?.Count ?? -1;

                // 检测异常情况
                if (projectCountBefore > 0 && projectCountAfter == 0)
                {
                }
            }
            catch (Exception)
            {
            }
        }

        ///// <summary>
        ///// 处理浮动窗口最小化事件
        ///// </summary>
        //private void OnFloatingWindowMinimized(FloatingWindowMinimizedEventArgs args)
        //{
        //    // 更新导航按钮高亮状态（通过Navigate方法触发，在FloatingWindowHelper中已处理）
        //}

        ///// <summary>
        ///// 处理浮动窗口恢复事件
        ///// </summary>
        //private void OnFloatingWindowRestored(FloatingWindowRestoredEventArgs args)
        //{
        //    // 导航逻辑已在FloatingWindowHelper中处理
        //}

        /// <summary>
        /// 处理浮动窗口激活事件
        /// </summary>
        private void OnFloatingWindowActivated(FloatingWindowActivatedEventArgs args)
        {
            try
            {
                // 记录浮动窗口激活时间，用于OnMainWindowActivated中的智能判断
                _lastFloatingWindowActivatedTime = DateTime.Now;
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// 处理窗口激活事件（窗口焦点感知）
        /// </summary>
        private void OnWindowActivated(WindowActivatedEventArgs args)
        {
            try
            {
                // 检查页面是否在浮动状态
                if (_navigationState.IsPageFloating(args.PageName))
                {
                    // 浮动窗口激活时，更新按钮高亮状态
                    SetActiveNavigationButton(args.PageName);
                }
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// MainWindow激活时的处理（MainWindow获得焦点）
        /// </summary>
        public void OnMainWindowActivated()
        {
            try
            {
                var timeSinceFloatingActivation = DateTime.Now - _lastFloatingWindowActivatedTime;
                if (timeSinceFloatingActivation.TotalSeconds < 2)
                {
                    return;
                }

                // 高亮MainRegion当前显示的页面按钮
                var currentRegion = _regionManager.Regions["MainRegion"];
                var currentView = currentRegion?.ActiveViews?.FirstOrDefault();
                string currentPageName = ExtractPageNameFromView(currentView);

                if (!string.IsNullOrEmpty(currentPageName))
                {
                    SetActiveNavigationButton(currentPageName);
                }

            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// 从View提取页面名称
        /// </summary>
        private string ExtractPageNameFromView(object view)
        {
            if (view == null)
                return null;

            try
            {
                // 获取View的类型名称
                string viewTypeName = view.GetType().Name;

                // 对于多实例页面（如PxiChassis），尝试从DataContext获取实例ID
                string instanceId = null;
                if (view is FrameworkElement fe && fe.DataContext != null)
                {
                    var dataContext = fe.DataContext;

                    // 检查是否为PxiChassisViewModel
                    if (dataContext.GetType().Name == "PxiChassisViewModel")
                    {
                        // 使用反射获取ChassisName属性
                        var chassisNameProp = dataContext.GetType().GetProperty("ChassisName");
                        if (chassisNameProp != null)
                        {
                            instanceId = chassisNameProp.GetValue(dataContext) as string;
                        }
                    }
                }

                // 尝试从NavigationButtons中找到匹配的按钮
                var matchingButtons = NavigationButtons.Where(b =>
                {
                    // 检查ViewName是否匹配
                    if (!string.IsNullOrEmpty(b.ViewName) && b.ViewName == viewTypeName)
                        return true;

                    // 检查Name是否匹配（对于单例页面）
                    if (GetViewTypeByPageName(b.Name) == viewTypeName)
                        return true;

                    return false;
                }).ToList();

                // 如果只有一个匹配的按钮，直接返回
                if (matchingButtons.Count == 1)
                {
                    return matchingButtons[0].Name;
                }

                // 如果有多个匹配的按钮（多实例页面），使用instanceId精确匹配
                if (matchingButtons.Count > 1 && !string.IsNullOrEmpty(instanceId))
                {
                    var exactMatch = matchingButtons.FirstOrDefault(b => b.Name.Contains(instanceId));
                    if (exactMatch != null)
                    {
                        return exactMatch.Name;
                    }
                }

                // 如果有多个匹配但没有instanceId，或instanceId不匹配，返回null避免高亮错误的按钮
                if (matchingButtons.Count > 1)
                {
                    return null;
                }

                // 如果没有找到匹配的按钮，尝试根据View类型名称推断
                return viewTypeName switch
                {
                    "HardwareConfig" => "设备与网络",
                    "TDMSystem" => "TDM系统",
                    "DatabaseConfig" => "数据库管理",
                    "HomePage" => null,
                    _ => null
                };
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// 根据页面名称获取View类型名称
        /// </summary>
        private string GetViewTypeByPageName(string pageName)
        {
            return pageName switch
            {
                "设备与网络" => "HardwareConfig",
                "TDM系统" => "TDMSystem",
                "数据库管理" => "DatabaseConfig",
                _ => null
            };
        }

        /// <summary>
        /// 恢复MainRegion显示原内容
        /// </summary>
        /// <param name="pageName">要恢复的页面名称</param>
        private void RestoreMainRegionContent(string pageName)
        {
            try
            {
                // 设置当前页面名称
                _currentPageName = pageName;

                // 激活对应的导航按钮
                SetActiveNavigationButton(pageName);
            }
            catch (Exception)
            {
            }
        }

        #endregion

        #region Test Task Operations

        private void OnAddPxiChassis(string chassisName)
        {
            // 根据名称获取机箱对象
            var chassis = _pxiChassisService.GetChassisByName(chassisName);
            if (chassis != null)
            {
                _projectTreeService.AddPxiChassisToProject(CurrentProject, chassis);
                RaisePropertyChanged(nameof(CurrentProject));
                MarkProjectAsModified(); 

                var chassisNode = FindChassisNode(chassis.Name);
            }
        }

        //private void OnAddPxiChassisToTree(ProjectItem projectItem)
        //{
        //    if (projectItem == null) return;

        //    // 使用与hardwareConfig拖动机箱相同的逻辑生成机箱名称
        //    var selectedChassis = _pxiChassisService.GenerateUniqueName("PXI机箱");

        //    // 获取下一个可用位置
        //    var nextPosition = _pxiChassisService.GetNextAvailablePosition();
        //    if (nextPosition?.Row == -1 || nextPosition?.Column == -1)
        //    {
        //        ReMessageBox.Show("机箱区域已满，无法添加更多机箱！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
        //        return;
        //    }

        //    // 占用机箱名称
        //    _pxiChassisService.ReserveChassisName(selectedChassis);

        //    // 创建机箱对象
        //    var newChassis = ChassisFactory.CreateChassis(selectedChassis, selectedChassis, nextPosition.Value.Row, nextPosition.Value.Column);

        //    // 添加到PxiChassisService（与hardwareconfig页面同步）
        //    _pxiChassisService.AddChassis(newChassis);
        //    _pxiChassisService.EnsureChassisDevice(selectedChassis, selectedChassis);
        //    // 发布添加机箱事件（通知hardwareconfig页面更新）
        //    _eventAggregator.GetEvent<AddPxiChassisEvent>().Publish(selectedChassis);

        //    // 添加到项目树
        //    _projectTreeService.AddPxiChassisToProject(CurrentProject, newChassis);
        //    RaisePropertyChanged(nameof(CurrentProject));
        //    MarkProjectAsModified(); // 标记项目为已修改

        //    var chassisNode = FindChassisNode(newChassis.Name);
        //    EnsureDefaultTestTaskForChassis(chassisNode);
        //}

        private void AddChassisOfModelToTree(string chassisModel)
        {
            // 生成机箱名称
            var chassisName = _pxiChassisService.GenerateUniqueName("PXI机箱");

            // 获取下一个可用位置
            var nextPosition = _pxiChassisService.GetNextAvailablePosition();
            if (nextPosition?.Row == -1 || nextPosition?.Column == -1)
            {
                ReMessageBox.Show("机箱区域已满，无法添加更多机箱！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 占用机箱名称
            _pxiChassisService.ReserveChassisName(chassisName);

            // 创建指定型号机箱
            var newChassis = ChassisFactory.CreateChassis(chassisModel, chassisName, nextPosition.Value.Row, nextPosition.Value.Column);

            // 添加到服务并同步UI
            _pxiChassisService.AddChassis(newChassis);
            _pxiChassisService.EnsureChassisDevice(chassisName, chassisModel);
            _eventAggregator.GetEvent<AddPxiChassisEvent>().Publish(chassisName);

            // 添加到项目树
            _projectTreeService.AddPxiChassisToProject(CurrentProject, newChassis);
            RaisePropertyChanged(nameof(CurrentProject));
            MarkProjectAsModified();

            var chassisNode = FindChassisNode(newChassis.Name);
        }

        private void OnAddPxi2722G2ToTree()
        {
            AddChassisOfModelToTree("PXIe-2722G2");
        }

        private void OnAddPxi2519G2ToTree()
        {
            AddChassisOfModelToTree("PXIe-2519G2");
        }

        private void OnAddNavigationButton(string pageName)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                AddNavigationButton(pageName);
            });
        }

        /// <summary>
        /// 处理机箱选择事件
        /// </summary>
        private void OnPxiChassisSelected(PxiChassisSelectedEventArgs args)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (string.IsNullOrEmpty(args.ChassisName))
                {
                    return;
                }

                // 使用NavigationService统一导航，避免重复添加导航按钮
                // PXI机箱是多例页面，使用机箱名作为instanceId
                var navParams = new Dictionary<string, object>
                {
                    { "ChassisName", args.ChassisName }
                };

                try
                {
                    _navigationService.NavigateToPage("PxiChassis", args.ChassisName, navParams);
                }
                catch (Exception ex)
                {
                    ReMessageBox.Show($"导航失败: {ex.Message}", "错误",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            });
        }

        private void OnRenamePxiChassis(RenamePxiChassisEventArgs renameInfo)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                try
                {
                    _projectTreeService.RenamePxiChassisInProject(CurrentProject, renameInfo.OldName, renameInfo.NewName);
                    RaisePropertyChanged(nameof(CurrentProject));
                    MarkProjectAsModified(); // 标记项目为已修改
                }
                catch (Exception ex)
                {
                    ReMessageBox.Show($"重命名机箱失败: {ex.Message}", "错误",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            });
        }

        /// <summary>
        /// 处理设备修改事件
        /// </summary>
        private void OnDeviceModified(DeviceModifiedEventArgs args)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                try
                {
                    // 标记项目为已修改
                    MarkProjectAsModified();
                }
                catch (Exception)
                {
                }
            });
        }

        /// <summary>
        /// 处理项目修改事件
        /// </summary>
        private void OnProjectModified(ProjectModifiedEventArgs args)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                try
                {
                    // 标记项目为已修改
                    MarkProjectAsModified();

                    if (_isSilentSavingSingleBoardTestResult)
                    {
                        return;
                    }

                    if (args != null
                        && string.Equals(args.ModificationType, "SingleBoardTestResult", StringComparison.Ordinal)
                        && CurrentProject != null
                        && CurrentProject.Count > 0
                        && !string.IsNullOrWhiteSpace(_currentProjectPath))
                    {
                        try
                        {
                            _isSilentSavingSingleBoardTestResult = true;
                            SaveProjectInternal();
                        }
                        catch
                        {
                        }
                        finally
                        {
                            _isSilentSavingSingleBoardTestResult = false;
                        }
                    }
                }
                catch (Exception)
                {
                }
            });
        }

        // 删除PXI机箱 - 同步项目树（由HardwareConfig页面触发）
        private void OnDeletePxiChassis(string chassisName)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                try
                {
                    // 只处理项目树的同步，机箱数据的删除已经在HardwareConfigViewModel中处理
                    _projectTreeService.RemovePxiChassisFromProject(CurrentProject, chassisName);
                    RaisePropertyChanged(nameof(CurrentProject));
                    
                    // 标记项目为已修改
                    MarkProjectAsModified();
                }
                catch (Exception ex)
                {
                    ReMessageBox.Show($"同步项目树失败: {ex.Message}", "错误",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            });
        }

        // 从项目树重命名PXI机箱
        private void OnRenamePxiChassisFromTree(string chassisName)
        {
            if (string.IsNullOrEmpty(chassisName)) return;

            try
            {
                // 显示重命名对话框
                var dialogService = new DialogServiceAlias();
                var newName = dialogService.ShowRenameDialog(chassisName, "重命名机箱");

                if (!string.IsNullOrEmpty(newName) && newName != chassisName)
                {
                    // 检查新名称是否已存在
                    if (_pxiChassisService.ChassisExists(newName))
                    {
                        ReMessageBox.Show("机箱名称已存在，请选择其他名称。", "警告",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    // 重命名机箱
                    _pxiChassisService.RenameChassis(chassisName, newName);
                    if (true)
                    {
                        // 同步项目树
                        _projectTreeService.RenamePxiChassisInProject(CurrentProject, chassisName, newName);
                        RaisePropertyChanged(nameof(CurrentProject));

                        // 发布重命名事件
                        _eventAggregator.GetEvent<RenamePxiChassisEvent>().Publish(
                            new RenamePxiChassisEventArgs { OldName = chassisName, NewName = newName });
                    }
                }
            }
            catch (Exception ex)
            {
                ReMessageBox.Show($"重命名机箱失败: {ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // 从项目树删除PXI机箱
        private void OnDeletePxiChassisFromTree(string chassisName)
        {
            if (string.IsNullOrEmpty(chassisName)) return;

            try
            {
                // 检查机箱是否有连接
                var chassis = _pxiChassisService.GetAllChassis().FirstOrDefault(c => c.Name == chassisName);
                if (chassis != null && _chassisConnectionService.HasChassisConnections(chassis.Id))
                {
                    ReMessageBox.Show("请先断开机箱的连接后再尝试删除。", "无法删除机箱", 
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // 收集所有需要检查的页面
                var openPages = new List<string>();
                
                // 1. 检查 PxiChassis 页面是否打开（按钮Name格式：PxiChassis_{ChassisName}）
                var chassisButton = NavigationButtons.FirstOrDefault(b => b.Name == $"PxiChassis_{chassisName}");
                if (chassisButton != null)
                {
                    openPages.Add(chassisButton.DisplayName ?? chassisName);
                }
                
                // 2. 检查机箱下所有测试任务的子页面是否打开
                var chassisNode = FindChassisNode(chassisName);
                if (chassisNode != null)
                {
                    var testTaskNames = GetTestTaskNamesUnderChassis(chassisNode);
                    foreach (var testTaskName in testTaskNames)
                    {
                        // 按钮Name格式：{PageType}_{TestTaskName}-{ConfigTabelName}
                        var taskOpenButtons = NavigationButtons
                            .Where(b => b.Name.Contains($"_{testTaskName}-"))
                            .ToList();
                        openPages.AddRange(taskOpenButtons.Select(b => b.DisplayName ?? b.Name));
                    }
                }

                // 如果有页面打开，提示用户先关闭
                if (openPages.Any())
                {
                    var pageList = string.Join("\n", openPages.Take(2)); // 最多显示2个
                    var moreText = openPages.Count > 10 ? $"\n...等共 {openPages.Count} 个页面" : "";
                    ReMessageBox.Show($"请先关闭以下页面后再删除机箱：\n{pageList}{moreText}",
                        "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var result = ReMessageBox.Show($"确定要删除机箱 '{chassisName}' 吗？", "确认删除",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    // 删除机箱
                    _pxiChassisService.RemoveChassis(chassisName);
                    
                    // 同步项目树
                    _projectTreeService.RemovePxiChassisFromProject(CurrentProject, chassisName);
                    RaisePropertyChanged(nameof(CurrentProject));
                    MarkProjectAsModified();

                    // 发布删除事件
                    _eventAggregator.GetEvent<DeletePxiChassisEvent>().Publish(chassisName);

                    // 发布清除设备详细信息事件
                    _eventAggregator.GetEvent<ClearDeviceDetailsEvent>().Publish();
                }
            }
            catch (Exception ex)
            {
                ReMessageBox.Show($"删除机箱失败: {ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        /// <summary>
        /// 查找机箱节点
        /// </summary>
        private ProjectItem FindChassisNode(string chassisName)
        {
            if (CurrentProject == null || CurrentProject.Count == 0) return null;
            var rootNode = CurrentProject[0];
            return rootNode?.Children?.FirstOrDefault(c => c.Name == chassisName && c.Type == AppConstants.NodeTypePxiChassis);
        }
        
        /// <summary>
        /// 获取机箱下所有测试任务的名称
        /// </summary>
        private List<string> GetTestTaskNamesUnderChassis(ProjectItem chassisNode)
        {
            var result = new List<string>();
            if (chassisNode?.Children == null) return result;
            
            // 查找任务配置节点
            var taskConfigNode = chassisNode.Children
                .FirstOrDefault(c => c.Type == AppConstants.NodeTypeTaskConfig);
            
            if (taskConfigNode?.Children != null)
            {
                foreach (var testTask in taskConfigNode.Children)
                {
                    if (testTask.Type == "test_task")
                    {
                        result.Add(testTask.Name);
                    }
                }
            }
            
            return result;
        }

        private void EnsureDefaultTestTaskForChassis(ProjectItem chassisNode)
        {
            return;
        }

        /// <summary>
        /// 创建测试任务
        /// </summary>
        private void OnCreateTestTask(ProjectItem taskConfigNode)
        {
            if (taskConfigNode == null || taskConfigNode.Type != "task_config") return;

            try
            {
                var dialogService = new DialogServiceAlias();
                var selected = dialogService.ShowCreateSingleBoardTestTaskDialog("创建测试任务");
                if (selected == null) return;

                var taskName = selected.TaskName?.Trim();
                if (string.IsNullOrWhiteSpace(taskName)) return;

                // 固定单板默认名就是下拉项本身，因此同机箱内不允许重复
                if (_projectService.IsTestTaskNameExists(taskConfigNode, taskName))
                {
                    ReMessageBox.Show($"测试任务名称 '{taskName}' 已存在，请使用其他名称", "提示",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var newTestTask = new ProjectItem
                {
                    Name = taskName,
                    Icon = AppConstants.IconTasks,
                    Type = AppConstants.NodeTypeTestTask,
                    Tag = selected.BoardType
                };

                taskConfigNode.Children.Add(newTestTask);

                _eventAggregator.GetEvent<TestTaskCreatedEvent>().Publish(newTestTask);
                MarkProjectAsModified();
            }
            catch (Exception ex)
            {
                ReMessageBox.Show($"创建测试任务失败: {ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnRenameTestTask(ProjectItem testTask)
        {
            if (testTask == null || testTask.Type != "test_task") return;

            try
            {
                // 使用统一的DialogService显示重命名对话框
                var dialogService = new DialogServiceAlias();
                var newName = dialogService.ShowRenameDialog(testTask.Name, "重命名测试任务");

                if (!string.IsNullOrEmpty(newName) && newName != testTask.Name)
                {
                    // 找到任务配置节点
                    var taskConfigNode = FindTaskConfigNode();
                    if (taskConfigNode == null)
                    {
                        ReMessageBox.Show("未找到任务配置节点", "错误",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    // 使用ProjectService重命名
                    bool success = _projectService.RenameTestTask(testTask, newName, taskConfigNode);
                    if (success)
                    {
                        // 标记项目为已修改
                        MarkProjectAsModified();
                    }
                    else
                    {
                        ReMessageBox.Show($"重命名失败: 任务名称 '{newName}' 已存在", "错误",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
            }
            catch (Exception ex)
            {
                ReMessageBox.Show($"重命名测试任务失败: {ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnDeleteTestTask(ProjectItem testTask)
        {
            if (testTask == null || testTask.Type != "test_task") return;

            try
            {
                // 检查是否有与此测试任务相关的页面已打开
                // 按钮Name格式：{PageType}_{TestTaskName}-{ConfigTabelName}，如 SignalConfigTabel_测试任务1-变量表1
                var openButtons = NavigationButtons
                    .Where(b => b.Name.Contains($"_{testTask.Name}-"))
                    .ToList();

                // 如果有页面已打开，提示用户先关闭
                if (openButtons.Any())
                {
                    var openPageNames = string.Join("、", openButtons.Select(b => b.DisplayName ?? b.Name));
                    ReMessageBox.Show($"请先关闭以下页面后再删除测试任务：\n{openPageNames}",
                        "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // 确认删除
                var result = ReMessageBox.Show($"确定要删除测试任务 '{testTask.Name}' 吗？",
                    "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    // 找到任务配置节点
                    var taskConfigNode = FindTaskConfigNode();
                    if (taskConfigNode == null)
                    {
                        ReMessageBox.Show("未找到任务配置节点", "错误",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    // 使用ProjectService删除
                    _projectService.DeleteTestTask(taskConfigNode, testTask);

                    // 标记项目为已修改
                    MarkProjectAsModified();
                }
            }
            catch (Exception ex)
            {
                ReMessageBox.Show($"删除测试任务失败: {ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private ProjectItem FindTaskConfigNode()
        {
            if (CurrentProject == null || CurrentProject.Count == 0) return null;

            var rootNode = CurrentProject[0];
            if (rootNode?.Children == null) return null;

            // 在所有机箱节点下查找任务配置节点
            foreach (var child in rootNode.Children)
            {
                if (child.Type == AppConstants.NodeTypePxiChassis && child.Children != null)
                {
                    var taskConfigNode = child.Children
                        .FirstOrDefault(item => item.Name == AppConstants.NodeNameTaskConfig && item.Type == AppConstants.NodeTypeTaskConfig);
                    
                    if (taskConfigNode != null)
                        return taskConfigNode;
                }
            }

            return null;
        }

        #region 配置表创建命令实现

        /// <summary>
        /// 创建通道配置表
        /// </summary>
        private void OnCreateChannelConfigTabel(ProjectItem channelConfigNode)
        {
            if (channelConfigNode == null || channelConfigNode.Type != "channel_config") return;

            try
            {
                // 生成默认名称
                int nextNumber = GetNextConfigTabelNumber(channelConfigNode, "通道配置表");
                string defaultName = $"通道配置表{nextNumber}";

                // 弹出对话框让用户输入名称
                var dialogService = new DialogServiceAlias();
                var inputName = dialogService.ShowInputDialog(defaultName, "创建通道配置表");

                // 用户取消
                if (string.IsNullOrEmpty(inputName)) return;

                // 检查名称是否重复
                if (IsConfigTabelNameExists(channelConfigNode, inputName))
                {
                    ReMessageBox.Show($"通道配置表名称 '{inputName}' 已存在，请使用其他名称", "提示",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var newTabel = new ProjectItem
                {
                    Name = inputName,
                    Icon = AppConstants.IconHardware,
                    Type = "channel_config_tabel",
                    Tag = "ChannelConfigTabel"
                };

                if (channelConfigNode.Children == null)
                    channelConfigNode.Children = new ObservableCollection<ProjectItem>();

                channelConfigNode.Children.Add(newTabel);
                MarkProjectAsModified();
                
                // 创建成功后自动导航到新页面
                OnTreeItemDoubleClick(newTabel);
            }
            catch (Exception ex)
            {
                ReMessageBox.Show($"创建通道配置表失败: {ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 创建信号配置表（变量表）
        /// </summary>
        private void OnCreateSignalConfigTabel(ProjectItem signalConfigNode)
        {
            if (signalConfigNode == null || signalConfigNode.Type != "signal_config") return;

            try
            {
                // 生成默认名称
                int nextNumber = GetNextConfigTabelNumber(signalConfigNode, "变量表");
                string defaultName = $"变量表{nextNumber}";

                // 弹出对话框让用户输入名称
                var dialogService = new DialogServiceAlias();
                var inputName = dialogService.ShowInputDialog(defaultName, "创建变量表");

                // 用户取消
                if (string.IsNullOrEmpty(inputName)) return;

                // 检查名称是否重复
                if (IsConfigTabelNameExists(signalConfigNode, inputName))
                {
                    ReMessageBox.Show($"变量表名称 '{inputName}' 已存在，请使用其他名称", "提示",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var newTabel = new ProjectItem
                {
                    Name = inputName,
                    Icon = AppConstants.IconNonCommunicate,
                    Type = "signal_config_tabel",
                    Tag = "SignalConfigTabel"
                };

                if (signalConfigNode.Children == null)
                    signalConfigNode.Children = new ObservableCollection<ProjectItem>();

                signalConfigNode.Children.Add(newTabel);
                MarkProjectAsModified();

                // 创建成功后自动导航到新页面
                OnTreeItemDoubleClick(newTabel);
            }
            catch (Exception ex)
            {
                ReMessageBox.Show($"创建变量表失败: {ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 创建矩阵开关配置表（变量表2）
        /// </summary>
        private void OnCreateMatrixSwitchConfigTable(ProjectItem signalConfigNode)
        {
            if (signalConfigNode == null || signalConfigNode.Type != "signal_config") return;

            try
            {
                // 生成默认名称
                int nextNumber = GetNextConfigTabelNumber(signalConfigNode, "变量表");
                string defaultName = $"变量表{nextNumber}";

                // 弹出对话框让用户输入名称
                var dialogService = new DialogServiceAlias();
                var inputName = dialogService.ShowInputDialog(defaultName, "创建变量表");

                // 用户取消
                if (string.IsNullOrEmpty(inputName)) return;

                // 检查名称是否重复
                if (IsConfigTabelNameExists(signalConfigNode, inputName))
                {
                    ReMessageBox.Show($"变量表名称 '{inputName}' 已存在，请使用其他名称", "提示",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var newTable = new ProjectItem
                {
                    Name = inputName,
                    Icon = AppConstants.IconNonCommunicate,
                    Type = "matrix_switch_config_table",
                    Tag = "MatrixSwitchConfigTable"
                };

                if (signalConfigNode.Children == null)
                    signalConfigNode.Children = new ObservableCollection<ProjectItem>();

                int insertIndex = GetFirstCommunicatingTableIndex(signalConfigNode.Children);
                if (insertIndex < 0)
                {
                    signalConfigNode.Children.Add(newTable);
                }
                else
                {
                    signalConfigNode.Children.Insert(insertIndex, newTable);
                }
                MarkProjectAsModified();
                
                // 创建成功后自动导航到新页面
                OnTreeItemDoubleClick(newTable);
            }
            catch (Exception ex)
            {
                ReMessageBox.Show($"创建变量表失败: {ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 创建ICD映射表
        /// </summary>
        private void OnCreateIcdMappingTabel(ProjectItem icdMappingNode)
        {
            if (icdMappingNode == null || icdMappingNode.Type != "icd_mapping") return;

            try
            {
                int nextNumber = GetNextConfigTabelNumber(icdMappingNode, "ICD映射表");
                string defaultName = $"ICD映射表{nextNumber}";

                var dialogService = new DialogServiceAlias();
                var inputName = dialogService.ShowInputDialog(defaultName, "创建ICD映射表");
                if (string.IsNullOrEmpty(inputName)) return;

                if (IsConfigTabelNameExists(icdMappingNode, inputName))
                {
                    ReMessageBox.Show($"ICD映射表名称 '{inputName}' 已存在，请使用其他名称", "提示",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var newTabel = new ProjectItem
                {
                    Name = inputName,
                    Icon = AppConstants.IconMapping,
                    Type = "icd_mapping_tabel",
                    Tag = "IcdMappingTabel"
                };

                icdMappingNode.Children ??= new ObservableCollection<ProjectItem>();
                icdMappingNode.Children.Add(newTabel);
                MarkProjectAsModified();

                OnTreeItemDoubleClick(newTabel);
            }
            catch (Exception ex)
            {
                ReMessageBox.Show($"创建ICD映射表失败: {ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 创建ICD配置表
        /// </summary>
        private void OnCreateIcdConfigTabel(ProjectItem signalConfigNode)
        {
            if (signalConfigNode == null || signalConfigNode.Type != "icd_config") return;

            try
            {
                // 生成默认名称（需要查找所有ICD配置表，忽略协议后缀）
                int nextNumber = GetNextIcdConfigTabelNumber(signalConfigNode);
                string defaultName = $"ICD配置表{nextNumber}";

                // 构建协议-通道映射并弹出对话框让用户输入名称和选择协议、通讯通道
                var availableChannels = BuildProtocolChannelMap(signalConfigNode);
                var createDialog = new CreateIcdConfigTabelDialog(defaultName, availableChannels);
                createDialog.Owner = Application.Current.MainWindow;
                var dialogResult = createDialog.ShowDialog();

                // 用户取消
                if (dialogResult != true || string.IsNullOrWhiteSpace(createDialog.TabelName) || string.IsNullOrEmpty(createDialog.SelectedProtocol))
                {
                    return;
                }

                string inputName = createDialog.TabelName.Trim();
                string selectedProtocol = createDialog.SelectedProtocol;
                
                // 获取协议后缀并生成完整名称
                string protocolSuffix = GetIcdProtocolSuffix(selectedProtocol);
                string fullTabelName = $"{inputName}-{protocolSuffix}";

                // 检查名称是否重复（使用完整名称）
                if (IsConfigTabelNameExists(signalConfigNode, fullTabelName))
                {
                    ReMessageBox.Show($"ICD配置表名称 '{fullTabelName}' 已存在，请使用其他名称", "提示",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var newTabel = new ProjectItem
                {
                    Name = fullTabelName,
                    Icon = AppConstants.IconTabel,
                    Type = "icd_config_tabel",
                    Tag = "IcdConfigTabel",
                    ProtocolType = selectedProtocol,
                    CommunicationChannelName = createDialog.SelectedChannelBinding
                };

                if (signalConfigNode.Children == null)
                    signalConfigNode.Children = new ObservableCollection<ProjectItem>();

                signalConfigNode.Children.Add(newTabel);
                MarkProjectAsModified();
                
                // 创建成功后自动导航到新页面
                OnTreeItemDoubleClick(newTabel);
            }
            catch (Exception ex)
            {
                ReMessageBox.Show($"创建ICD配置表失败: {ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 创建测试序列界面
        /// </summary>
        private void OnCreateTestSequence(ProjectItem testSequenceNode)
        {
            if (testSequenceNode == null || testSequenceNode.Type != "test_sequence") return;

            try
            {
                int nextNumber = GetNextConfigTabelNumber(testSequenceNode, "测试序列");
                string tabelName = $"测试序列{nextNumber}";

                var newTabel = new ProjectItem
                {
                    Name = tabelName,
                    Icon = AppConstants.IconTest,
                    Type = "test_sequence_tabel",
                    Tag = "TestSequence"
                };

                if (testSequenceNode.Children == null)
                    testSequenceNode.Children = new ObservableCollection<ProjectItem>();

                testSequenceNode.Children.Add(newTabel);
                MarkProjectAsModified();
                
                // 创建成功后自动导航到新页面
                OnTreeItemDoubleClick(newTabel);
            }
            catch (Exception ex)
            {
                ReMessageBox.Show($"创建测试序列失败: {ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 创建报表模板界面
        /// </summary>
        private void OnCreateReportConfigTabel(ProjectItem reportNode)
        {
            if (reportNode == null || reportNode.Type != "report") return;

            try
            {
                int nextNumber = GetNextConfigTabelNumber(reportNode, "报表模板");
                string tabelName = $"报表模板{nextNumber}";

                var newTabel = new ProjectItem
                {
                    Name = tabelName,
                    Icon = AppConstants.IconFileRed,
                    Type = "report_config_tabel",
                    Tag = "ReportConfigTabel"
                };

                if (reportNode.Children == null)
                    reportNode.Children = new ObservableCollection<ProjectItem>();

                reportNode.Children.Add(newTabel);
                MarkProjectAsModified();
                
                // 创建成功后自动导航到新页面
                OnTreeItemDoubleClick(newTabel);
            }
            catch (Exception ex)
            {
                ReMessageBox.Show($"创建报表模板失败: {ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 创建测试界面
        /// </summary>
        private void OnCreateTestInterface(ProjectItem testUINode)
        {
            if (testUINode == null || testUINode.Type != "test_ui") return;

            try
            {
                // 生成默认名称
                int nextNumber = GetNextConfigTabelNumber(testUINode, "测试界面");
                string defaultName = $"测试界面{nextNumber}";

                // 弹出对话框让用户输入名称
                var dialogService = new DialogServiceAlias();
                var inputName = dialogService.ShowInputDialog(defaultName, "创建测试界面");

                // 用户取消
                if (string.IsNullOrEmpty(inputName)) return;

                // 检查名称是否重复
                if (IsConfigTabelNameExists(testUINode, inputName))
                {
                    ReMessageBox.Show($"测试界面名称 '{inputName}' 已存在，请使用其他名称", "提示",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var newInterface = new ProjectItem
                {
                    Name = inputName,
                    Icon = AppConstants.IconHand,
                    Type = "test_interface",
                    Tag = "TestInterface"
                };

                if (testUINode.Children == null)
                    testUINode.Children = new ObservableCollection<ProjectItem>();

                testUINode.Children.Add(newInterface);
                MarkProjectAsModified();
                
                // 创建成功后自动导航到新页面
                OnTreeItemDoubleClick(newInterface);
            }
            catch (Exception ex)
            {
                ReMessageBox.Show($"创建测试界面失败: {ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 获取下一个配置表编号
        /// </summary>
        private int GetNextConfigTabelNumber(ProjectItem parentNode, string prefix)
        {
            int maxNumber = 0;

            if (parentNode?.Children != null)
            {
                foreach (var child in parentNode.Children)
                {
                    if (child.Name.StartsWith(prefix))
                    {
                        string numberPart = child.Name.Substring(prefix.Length);
                        if (int.TryParse(numberPart, out int number))
                        {
                            maxNumber = Math.Max(maxNumber, number);
                        }
                    }
                }
            }

            return maxNumber + 1;
        }

        /// <summary>
        /// 获取ICD配置表的协议后缀
        /// </summary>
        private string GetIcdProtocolSuffix(string protocol)
        {
            return protocol switch
            {
                "CAN" => "CAN",
                "ARINC429" => "429",
                "1553B" => "1553",
                "MIL1394" => "1394",
                _ => protocol
            };
        }

        /// <summary>
        /// 获取下一个ICD配置表编号
        /// </summary>
        private int GetNextIcdConfigTabelNumber(ProjectItem parentNode)
        {
            int maxNumber = 0;
            string prefix = "ICD配置表";

            if (parentNode?.Children != null)
            {
                foreach (var child in parentNode.Children)
                {
                    if (child.Type == "icd_config_tabel" && child.Name.StartsWith(prefix))
                    {
                        // 去掉前缀
                        string remaining = child.Name.Substring(prefix.Length);
                        
                        // 查找数字部分（可能在"-协议"之前）
                        int dashIndex = remaining.IndexOf('-');
                        if (dashIndex > 0)
                        {
                            remaining = remaining.Substring(0, dashIndex);
                        }
                        
                        if (int.TryParse(remaining, out int number))
                        {
                            maxNumber = Math.Max(maxNumber, number);
                        }
                    }
                }
            }

            return maxNumber + 1;
        }

        /// <summary>
        /// 检查配置表名称是否已存在
        /// </summary>
        private bool IsConfigTabelNameExists(ProjectItem parentNode, string tabelName)
        {
            if (parentNode?.Children == null) return false;
            
            return parentNode.Children.Any(child => 
                child.Name.Equals(tabelName, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// 重命名配置表
        /// </summary>
        private void OnRenameConfigTabel(ProjectItem configTabel)
        {
            if (configTabel == null) return;

            try
            {
                // 使用统一的DialogService显示重命名对话框
                var dialogService = new DialogServiceAlias();
                var newName = dialogService.ShowRenameDialog(configTabel.Name, "重命名配置表");

                if (!string.IsNullOrEmpty(newName) && newName != configTabel.Name)
                {
                    // 找到父节点和测试任务
                    var (testTask, parentNode) = FindConfigTabelParent(configTabel);
                    if (testTask == null || parentNode == null)
                    {
                        ReMessageBox.Show("未找到配置表的父节点", "错误",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    // 检查新名称是否已存在
                    if (parentNode.Children?.Any(c => c.Name == newName && c != configTabel) == true)
                    {
                        ReMessageBox.Show($"配置表名称 '{newName}' 已存在，请选择其他名称。", "警告",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    string oldName = configTabel.Name;

                    // 重命名配置表
                    configTabel.Name = newName;

                    // 同步更新导航栏按钮
                    UpdateNavigationButtonAfterRename(testTask.Name, oldName, newName);

                    // 标记项目为已修改
                    MarkProjectAsModified();
                }
            }
            catch (Exception ex)
            {
                ReMessageBox.Show($"重命名配置表失败: {ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 删除配置表（叶子节点）
        /// </summary>
        private void OnDeleteConfigTabel(ProjectItem configTabel)
        {
            if (configTabel == null) return;

            try
            {
                // 找到父节点和测试任务
                var (testTask, parentNode) = FindConfigTabelParent(configTabel);
                if (testTask == null || parentNode == null)
                {
                    ReMessageBox.Show("未找到配置表的父节点", "错误",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // 根据配置表类型确定页面类型前缀
                string pageTypePrefix = configTabel.Type switch
                {
                    "channel_config_tabel" => "ChannelConfigTabel",
                    "signal_config_tabel" => "SignalConfigTabel",
                    "icd_mapping_tabel" => "IcdMappingTabel",
                    "icd_config_tabel" => "IcdConfigTabel",
                    "test_sequence_tabel" => "TestSequence",
                    "report_config_tabel" => "ReportConfigTabel",
                    "test_interface" => "TestInterface",
                    _ => configTabel.Type
                };
                
                // 检查页面是否已在导航栏中打开（按钮Name格式：{PageType}_{TestTaskName}-{ConfigTabelName}）
                string instanceId = $"{testTask.Name}-{configTabel.Name}";
                string pageKey = $"{pageTypePrefix}_{instanceId}";
                
                var buttonExists = NavigationButtons.Any(b => b.Name == pageKey);
                
                // 如果页面已打开，提示用户先关闭
                if (buttonExists)
                {
                    ReMessageBox.Show($"请先关闭 '{configTabel.Name}' 页面后再删除",
                        "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // 确认删除
                var result = ReMessageBox.Show($"确定要删除 '{configTabel.Name}' 吗？",
                    "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    // 从父节点中删除配置表
                    parentNode.Children?.Remove(configTabel);

                    // 标记项目为已修改
                    MarkProjectAsModified();
                }
            }
            catch (Exception ex)
            {
                ReMessageBox.Show($"删除失败: {ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 查找配置表的父节点和所属测试任务
        /// </summary>
        private (ProjectItem testTask, ProjectItem parentNode) FindConfigTabelParent(ProjectItem configTabel)
        {
            if (CurrentProject == null || CurrentProject.Count == 0) return (null, null);

            var rootNode = CurrentProject[0];
            if (rootNode?.Children == null) return (null, null);

            // 在所有机箱节点下查找
            foreach (var chassisNode in rootNode.Children)
            {
                if (chassisNode.Type != AppConstants.NodeTypePxiChassis || chassisNode.Children == null) continue;

                var taskConfigNode = chassisNode.Children
                    .FirstOrDefault(item => item.Name == AppConstants.NodeNameTaskConfig && item.Type == AppConstants.NodeTypeTaskConfig);

                if (taskConfigNode?.Children == null) continue;

                // 遍历所有测试任务
                foreach (var testTask in taskConfigNode.Children)
                {
                    if (testTask.Type != AppConstants.NodeTypeTestTask || testTask.Children == null) continue;

                    // 遍历测试任务的子节点（通道配置、信号配置等）
                    foreach (var parentNode in testTask.Children)
                    {
                        if (parentNode.Children?.Contains(configTabel) == true)
                        {
                            return (testTask, parentNode);
                        }
                    }
                }
            }

            return (null, null);
        }

        /// <summary>
        /// 重命名后更新导航栏按钮
        /// </summary>
        private void UpdateNavigationButtonAfterRename(string testTaskName, string oldConfigTabelName, string newConfigTabelName)
        {
            string oldButtonName = $"{testTaskName}-{oldConfigTabelName}";
            string newButtonName = $"{testTaskName}-{newConfigTabelName}";

            var button = NavigationButtons.FirstOrDefault(b => b.Name == oldButtonName);
            if (button != null)
            {
                button.Name = newButtonName;
                button.DisplayName = newConfigTabelName;
                button.Tag = newButtonName;

                // 更新浮悬路径
                if (!string.IsNullOrEmpty(button.TooltipPath))
                {
                    // 替换路径中的最后一部分（配置表名称）
                    var pathParts = button.TooltipPath.Split('/');
                    if (pathParts.Length > 0)
                    {
                        pathParts[pathParts.Length - 1] = newConfigTabelName;
                        button.TooltipPath = string.Join("/", pathParts);
                    }
                }
            }
        }

        #endregion

        #region 导航辅助方法

        /// <summary>
        /// 获取父测试任务名称
        /// </summary>
        private string GetParentTestTaskName(ProjectItem item)
        {
            // 通过项目树向上查找测试任务节点
            // 先找到机箱，然后在机箱下的任务配置中查找测试任务
            if (CurrentProject == null || CurrentProject.Count == 0) return null;

            var rootNode = CurrentProject[0];
            if (rootNode?.Children == null) return null;

            // 遍历所有机箱节点
            foreach (var chassisNode in rootNode.Children)
            {
                if (chassisNode.Type == AppConstants.NodeTypePxiChassis && chassisNode.Children != null)
                {
                    // 检查item是否是此机箱的后代节点
                    if (!IsDescendantOf(item, chassisNode))
                        continue;

                    // 在此机箱下查找任务配置节点
                    var taskConfigNode = chassisNode.Children
                        .FirstOrDefault(node => node.Name == AppConstants.NodeNameTaskConfig && 
                                               node.Type == AppConstants.NodeTypeTaskConfig);

                    if (taskConfigNode?.Children != null)
                    {
                        // 在任务配置节点下查找测试任务
                        foreach (var testTask in taskConfigNode.Children)
                        {
                            if (testTask.Type == "test_task")
                            {
                                // 检查item是否是testTask的子节点或孙子节点
                                if (IsDescendantOf(item, testTask))
                                {
                                    return testTask.Name;
                                }
                            }
                        }
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// 获取配置表所属的机箱名称
        /// </summary>
        private string GetParentChassisName(ProjectItem item)
        {
            if (item == null || CurrentProject == null || CurrentProject.Count == 0) return null;

            var rootNode = CurrentProject[0];
            if (rootNode?.Children == null) return null;

            // 遍历所有机箱节点
            foreach (var chassisNode in rootNode.Children)
            {
                if (chassisNode.Type == AppConstants.NodeTypePxiChassis)
                {
                    // 检查item是否是此机箱的后代节点
                    if (IsDescendantOf(item, chassisNode))
                    {
                        return chassisNode.Name;
                    }
                }
            }

            return null;
        }

        private Dictionary<string, List<string>> BuildProtocolChannelMap(ProjectItem icdConfigNode)
        {
            var channelMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            try
            {
                // 尝试通过事件请求已打开页面的通道数据（优先），以确保最新编辑的数据也能被包含
                var channelRequestArgs = new Events.ChannelTabelItemsRequestEventArgs();
                _eventAggregator?.GetEvent<Events.ChannelTabelItemsRequestEvent>()?.Publish(channelRequestArgs);

                Dictionary<string, List<ChannelTabelItem>> allChannels = null;
                if (channelRequestArgs.ChannelTabelItems != null && channelRequestArgs.ChannelTabelItems.Count > 0)
                {
                    allChannels = channelRequestArgs.ChannelTabelItems;
                }
                else
                {
                    allChannels = ChannelConfigTabelViewModel.GetAllChannelTabelItems();
                }

                if (allChannels == null || allChannels.Count == 0)
                    return channelMap;

                // 按照父测试任务过滤：同一测试任务下的通道才会被包含（保持任务之间隔离）
                var targetTestTask = GetParentTestTaskName(icdConfigNode) ?? GetParentTestTaskNameAlternative(icdConfigNode);
                foreach (var kvp in allChannels)
                {
                    if (string.IsNullOrEmpty(kvp.Key))
                        continue;

                    // kvp.Key 常见格式为 "测试任务名/配置表名"，从中提取测试任务名并与目标测试任务比较
                    var parts = kvp.Key.Split('/');
                    var testTaskName = parts.Length > 0 ? parts[0] : null;
                    if (!string.IsNullOrEmpty(targetTestTask) &&
                        !string.Equals(testTaskName, targetTestTask, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (kvp.Value == null)
                        continue;

                    foreach (var channel in kvp.Value)
                    {
                        if (channel == null || channel.IsEmpty)
                            continue;

                        if (!string.Equals(channel.ChannelType, "通讯通道", StringComparison.Ordinal))
                            continue;

                        var protocolKey = NormalizeProtocolName(channel.InputOutputType);
                        if (string.IsNullOrEmpty(protocolKey))
                            continue;

                        var displayName = BuildChannelDisplayName(channel);
                        if (!channelMap.TryGetValue(protocolKey, out var list))
                        {
                            list = new List<string>();
                            channelMap[protocolKey] = list;
                        }

                        if (!list.Any(existing => string.Equals(existing, displayName, StringComparison.OrdinalIgnoreCase)))
                        {
                            list.Add(displayName);
                        }
                    }
                }

                foreach (var list in channelMap.Values)
                {
                    list.Sort(StringComparer.OrdinalIgnoreCase);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"构建通讯通道映射失败: {ex}");
            }

            return channelMap;
        }

        private static string BuildChannelDisplayName(ChannelTabelItem channel)
        {
            if (channel == null)
                return string.Empty;

            if (!string.IsNullOrEmpty(channel.CardName))
            {
                return $"{channel.ChannelName} ({channel.CardName})";
            }

            return channel.ChannelName ?? string.Empty;
        }

        private static string NormalizeProtocolName(string protocolName)
        {
            if (string.IsNullOrWhiteSpace(protocolName))
                return null;

            var normalized = protocolName.Trim();
            if (normalized.Equals("CAN", StringComparison.OrdinalIgnoreCase))
                return "CAN";
            if (normalized.Equals("ARINC429", StringComparison.OrdinalIgnoreCase))
                return "ARINC429";
            if (normalized.Equals("1553B", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("MIL-1553B", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("MIL1553B", StringComparison.OrdinalIgnoreCase))
                return "1553B";
            if (normalized.Equals("MIL1394", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("MIL-1394B", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("MIL1394B", StringComparison.OrdinalIgnoreCase))
                return "MIL1394";

            return normalized;
        }

        /// <summary>
        /// 检查item是否是parent的后代节点
        /// 使用引用比较，确保不同机箱下的同名配置表不会被误判
        /// </summary>
        private bool IsDescendantOf(ProjectItem item, ProjectItem parent)
        {
            if (parent.Children == null) return false;

            foreach (var child in parent.Children)
            {
                // 优先使用引用比较，确保准确性
                if (child == item)
                    return true;

                // 递归检查子节点
                if (child.Children != null && IsDescendantOf(item, child))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 获取父测试任务名称（备用方法，通过向上遍历项目树）
        /// </summary>
        private string GetParentTestTaskNameAlternative(ProjectItem item)
        {
            if (item == null || CurrentProject == null || CurrentProject.Count == 0) return null;

            var rootNode = CurrentProject[0];
            if (rootNode?.Children == null) return null;

            // 遍历所有机箱节点
            foreach (var chassisNode in rootNode.Children)
            {
                if (chassisNode.Type == AppConstants.NodeTypePxiChassis && chassisNode.Children != null)
                {
                    // 在此机箱下查找任务配置节点
                    var taskConfigNode = chassisNode.Children
                        .FirstOrDefault(node => node.Name == AppConstants.NodeNameTaskConfig && 
                                               node.Type == AppConstants.NodeTypeTaskConfig);

                    if (taskConfigNode?.Children != null)
                    {
                        // 遍历所有测试任务
                        foreach (var testTask in taskConfigNode.Children)
                        {
                            if (testTask.Type == "test_task" && testTask.Children != null)
                            {
                                // 遍历测试任务的所有子节点（channel_config, signal_config等）
                                foreach (var configNode in testTask.Children)
                                {
                                    if (configNode.Children != null)
                                    {
                                        // 检查配置节点下的所有配置表
                                        foreach (var configTabel in configNode.Children)
                                        {
                                            // 通过名称和类型匹配，而不是引用匹配
                                            if (configTabel.Name == item.Name && configTabel.Type == item.Type)
                                            {
                                                return testTask.Name;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// 根据类型获取视图名称
        /// </summary>
        private string GetViewNameByType(string type)
        {
            return type switch
            {
                "channel_config_tabel" => "ChannelConfigTabel",
                "signal_config_tabel" => "SignalConfigTabel",
                "icd_mapping_tabel" => "IcdMappingTabel",
                "icd_config_tabel" => "IcdConfigTabel",
                "test_sequence_tabel" => "TestSequence",
                "report_config_tabel" => "ReportConfigTabel",
                "tdm_system" => "TDMSystem",
                _ => null
            };
        }

        /// <summary>
        /// 判断是否是配置表类型
        /// </summary>
        private bool IsConfigTabelType(string type)
        {
            return 
                   type == "channel_config_tabel" ||
                   type == "signal_config_tabel" ||
                   type == "icd_mapping_tabel" ||
                   type == "icd_config_tabel" ||
                   type == "test_sequence_table" ||
                   type == "report_config_table" ||
                   type == "test_interface";
        }

        /// <summary>
        /// 获取父节点类型
        /// </summary>
        private string GetParentType(ProjectItem item)
        {
            if (CurrentProject == null || CurrentProject.Count == 0) return null;

            var rootNode = CurrentProject[0];
            if (rootNode?.Children == null) return null;

            // 遍历所有机箱节点
            foreach (var chassisNode in rootNode.Children)
            {
                if (chassisNode.Type == AppConstants.NodeTypePxiChassis && chassisNode.Children != null)
                {
                    // 检查item是否是此机箱的后代节点
                    if (!IsDescendantOf(item, chassisNode))
                        continue;

                    // 在此机箱下查找任务配置节点
                    var taskConfigNode = chassisNode.Children
                        .FirstOrDefault(node => node.Name == AppConstants.NodeNameTaskConfig && 
                                               node.Type == AppConstants.NodeTypeTaskConfig);

                    if (taskConfigNode?.Children != null)
                    {
                        // 在任务配置节点下查找测试任务
                        foreach (var testTask in taskConfigNode.Children)
                        {
                            if (testTask.Type == "test_task" && testTask.Children != null)
                            {
                                foreach (var child in testTask.Children)
                                {
                                    if (child.Children != null && child.Children.Contains(item))
                                    {
                                        return child.Type;
                                    }
                                }
                            }
                        }
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// 根据视图名称获取对应的按钮名称
        /// </summary>
        private string GetButtonNameByViewName(string viewName)
        {
            // 首先检查是否有匹配的导航按钮
            var matchingButton = NavigationButtons.FirstOrDefault(b => b.ViewName == viewName);
            if (matchingButton != null)
            {
                return matchingButton.Name;
            }

            // 如果没有找到，根据viewName推断按钮名称
            return viewName switch
            {
                "HardwareConfig" => "设备与网络",
                "TDMSystem" => "TDM系统",
                "DatabaseConfig" => "数据库管理",
                _ => null
            };
        }


        /// <summary>
        /// 添加带路径的导航按钮
        /// </summary>
        private void AddNavigationButtonWithPath(string pageName, string testTaskName, string parentType,
            string viewName, NavigationParameters navigationParameters)
        {
            // 生成唯一标识：测试任务名-配置表名
            string uniqueName = $"{testTaskName}-{pageName}";

            var existingButton = NavigationButtons.FirstOrDefault(b => b.Name == uniqueName);

            if (existingButton != null)
            {
                // 页面已存在，标记为打开并推到激活历史最前
                _navigationState.OpenPage(uniqueName);
                SetActiveNavigationButton(uniqueName);
                AddToNavigationHistory(uniqueName);
                return;
            }

            // 生成路径信息
            string parentName = GetParentDisplayName(parentType);
            string tooltipPath = $"{testTaskName}/{parentName}/{pageName}";

            // 转换 NavigationParameters 为 Dictionary
            var navParams = new Dictionary<string, object>();
            if (navigationParameters != null)
            {
                foreach (var key in navigationParameters)
                {
                    navParams[key.Key] = key.Value;
                }
            }

            var navigationButton = new NavigationButton
            {
                Name = uniqueName,           // 唯一标识：测试任务1-通道配置表1
                DisplayName = pageName,      // 显示名称：通道配置表1
                Tag = uniqueName,
                ViewName = viewName,         // 视图名称：ChannelConfigTabel
                NavigationParams = navParams, // 导航参数
                IsActive = false,
                TooltipPath = tooltipPath    // 浮悬路径：测试任务1/通道配置/通道配置表1
            };

            // 插入到正确的位置：设备与网络 → 机箱 → 测试任务界面
            InsertNavigationButtonInOrder(navigationButton);

            // 添加到NavigationStateService
            _navigationState.OpenPage(uniqueName);

            SetActiveNavigationButton(uniqueName);
            AddToNavigationHistory(uniqueName);
        }

        /// <summary>
        /// 获取父节点显示名称
        /// </summary>
        private string GetParentDisplayName(string parentType)
        {
            return parentType switch
            {
                "channel_config" => "通道配置",
                "icd_config" => "ICD配置",
                "icd_mapping" => "ICD映射",
                "signal_config" => "信号配置",
                "test_sequence" => "测试序列",
                "report" => "报表",
                _ => ""
            };
        }

        /// <summary>
        /// 按顺序插入导航按钮：设备与网络 → 机箱 → 测试任务界面
        /// </summary>
        private void InsertNavigationButtonInOrder(NavigationButton newButton)
        {
            // 如果是空列表，直接添加
            if (NavigationButtons.Count == 0)
            {
                NavigationButtons.Add(newButton);
                return;
            }

            // 获取按钮类型优先级
            int newButtonPriority = GetNavigationButtonPriority(newButton.Name);

            // 找到合适的插入位置
            int insertIndex = NavigationButtons.Count;
            for (int i = 0; i < NavigationButtons.Count; i++)
            {
                int existingPriority = GetNavigationButtonPriority(NavigationButtons[i].Name);
                if (newButtonPriority < existingPriority)
                {
                    insertIndex = i;
                    break;
                }
            }

            NavigationButtons.Insert(insertIndex, newButton);
        }

        /// <summary>
        /// 获取导航按钮的优先级（数字越小，优先级越高）
        /// 顺序：设备与网络(0) → 机箱(1) → 测试任务界面(2)
        /// </summary>
        private int GetNavigationButtonPriority(string buttonName)
        {
            if (buttonName == "设备与网络")
                return 0;

            // 检查是否是机箱（PXI机箱1、PXI机箱2等）
            if (_pxiChassisService.ChassisExists(buttonName))
                return 1;

            // 测试任务相关界面（包含"-"的都是测试任务界面）
            if (buttonName.Contains("-"))
                return 2;

            // 其他界面放在最后
            return 3;
        }

        /// <summary>
        /// 页面被删除后的智能导航逻辑
        /// 优先导航到剩余导航按钮中的最后一个，如果没有则显示空白
        /// </summary>
        private void NavigateAfterPageRemoval()
        {
            try
            {
                // 如果还有剩余的导航按钮，导航到最后一个
                if (NavigationButtons.Count > 0)
                {
                    var lastButton = NavigationButtons.Last();
                    OnNavigationButtonClick(lastButton.Name);
                }
                else
                {
                    // 如果没有剩余的导航按钮，MainRegion显示空白
                    _currentPageName = null;
                }
            }
            catch (Exception)
            {
            }
        }

        #endregion

        /// <summary>
        /// </summary>
        private string GetCurrentChassisName()
        {
            // 从导航参数中获取当前机箱名称
            if (_regionManager.Regions.ContainsRegionWithName("MainRegion"))
            {
                var region = _regionManager.Regions["MainRegion"];
                if (region.ActiveViews.Count() > 0)
                {
                    var activeView = region.ActiveViews.FirstOrDefault();
                    if (activeView is FrameworkElement element && element.DataContext is PxiChassisViewModel viewModel)
                    {
                        return viewModel.ChassisName;
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// 获取当前活跃的机箱对象
        /// TODO: 实现正确的机箱获取逻辑
        /// </summary>
        private Models.ChassisModel GetCurrentChassis()
        {
            // 暂时返回null，后续完善
            // 需要从当前项目结构中找到正在运行的机箱
            return null;
        }

        /// <summary>
        /// 获取当前活跃的测试任务对象
        /// TODO: 实现正确的测试任务获取逻辑
        /// </summary>
        private Models.ProjectItem GetCurrentTestTask()
        {
            // 暂时返回null，后续完善
            // 需要从当前项目结构中找到正在运行的测试任务
            return null;
        }

        /// <summary>
        /// 窗口最小化事件处理
        /// </summary>
        private void OnWindowMinimized(WindowMinimizedEventArgs args)
        {
            if (args.KeepNavigationButtons)
            {
                // 保留导航按钮，不执行任何操作
            }
        }

        /// <summary>
        /// 窗口关闭事件处理
        /// </summary>
        private void OnWindowClosing(WindowClosingEventArgs args)
        {
            if (args.ReleaseContent)
            {
                // 如果指定了页面名称，释放指定页面；否则释放当前页面
                if (!string.IsNullOrEmpty(args.PageName))
                {
                    ReleasePageByName(args.PageName);
                }
                else
                {
                    ReleaseCurrentPage();
                }
            }
        }

        /// <summary>
        /// 窗口恢复事件处理（从浮动窗口嵌入回主窗口）
        /// </summary>
        private void OnWindowRestored(WindowRestoredEventArgs args)
        {
            if (!string.IsNullOrEmpty(args.PageName))
            {
                // 恢复MainRegion显示原内容
                RestoreMainRegionContent(args.PageName);
            }
        }

        /// <summary>
        /// 隐藏当前页面事件处理（最小化按钮在嵌入模式下）
        /// </summary>
        private void OnHideCurrentPage(HideCurrentPageEventArgs args)
        {
            HideCurrentPage(args?.IsMinimize ?? true);
        }

        /// <summary>
        /// 释放当前页面事件处理（关闭按钮）
        /// </summary>
        /// <param name="pageName">要释放的页面名称，如果为null则释放当前激活的页面</param>
        private void OnReleaseCurrentPage(string pageName)
        {
            // 兼容：如果传入的是具体浮动实例的 pageKey，则仅关闭该实例
            if (!string.IsNullOrEmpty(pageName) && _navigationState != null && _navigationState.IsFloating(pageName))
            {
                // 先从浮动集合移除，再按 key 清理关闭逻辑
                _navigationState.Unfloat(pageName);
                _navigationState.ClosePage(pageName);
                return;
            }

            if (string.IsNullOrEmpty(pageName))
            {
                ReleaseCurrentPage();
            }
            else
            {
                ReleasePageByName(pageName);
            }
        }

        /// <summary>
        /// 展开项目树的一级目录
        /// </summary>
        private void ExpandProjectTreeLevel1()
        {
            // 检查项目数据
            if (CurrentProject == null || CurrentProject.Count == 0)
            {
                return;
            }

            var rootProject = CurrentProject[0];
            // 使用Dispatcher延迟执行，确保UI完全加载后再展开
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    // 查找MainWindow并调用展开方法
                    var mainWindow = Application.Current.MainWindow as MainWindow;
                    if (mainWindow != null)
                    {
                        mainWindow.ExpandProjectTreeToLevel3();
                    }
                }
                catch (Exception)
                {
                }
            }), DispatcherPriority.Loaded);
        }

        /// <summary>
        /// 测试方法：手动触发项目树展开（用于调试）
        /// </summary>
        public void TestExpandProjectTree()
        {
            if (CurrentProject != null)
            {
                if (CurrentProject.Count > 0)
                {
                    var rootProject = CurrentProject[0];
                    if (rootProject.Children != null)
                    {
                        foreach (var child in rootProject.Children)
                        {
                        }
                    }
                    else
                    {
                    }
                }
                else
                {
                }
            }
            else
            {
            }

            // 调用展开方法
            ExpandProjectTreeLevel1();
        }

        #endregion

        #region IDisposable Implementation

        private bool _disposed = false;

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                // 使用ResourceCleanupHelper统一清理资源
                ResourceCleanupHelper.TryCleanup(() =>
                {
                    // 停止定时器
                    ResourceCleanupHelper.CleanupTimer(ref _timer);

                    // 取消事件订阅
                    UnsubscribeEvents();

                    // 取消订阅项目保存状态服务事件
                    if (_projectSaveStateService != null)
                    {
                        _projectSaveStateService.SaveStateChanged -= OnProjectSaveStateChanged;
                    }

                    // 清理集合
                    ResourceCleanupHelper.CleanupCollection(Tools);
                    ResourceCleanupHelper.CleanupCollection(NavigationButtons);
                    _navigationHistory?.Clear();

                    // 清理项目数据
                    ResourceCleanupHelper.CleanupProjectData(ref _currentProject, ref _currentProjectPath);
                    RaisePropertyChanged(nameof(CurrentProjectFilePath));

                }, "MainWindowViewModel资源清理");
            }
            _disposed = true;
        }

        /// <summary>
        /// 取消所有事件订阅
        /// </summary>
        private void UnsubscribeEvents()
        {
            if (_eventAggregator == null) return;

            try
            {
                _eventAggregator.GetEvent<AddPxiChassisEvent>()?.Unsubscribe(OnAddPxiChassis);
                _eventAggregator.GetEvent<RenamePxiChassisEvent>()?.Unsubscribe(OnRenamePxiChassis);
                _eventAggregator.GetEvent<DeletePxiChassisEvent>()?.Unsubscribe(OnDeletePxiChassis);
                _eventAggregator.GetEvent<DeviceModifiedEvent>()?.Unsubscribe(OnDeviceModified);
                _eventAggregator.GetEvent<ProjectModifiedEvent>()?.Unsubscribe(OnProjectModified);
                _eventAggregator.GetEvent<AddNavigationButtonEvent>()?.Unsubscribe(OnAddNavigationButton);
                _eventAggregator.GetEvent<PxiChassisSelectedEvent>()?.Unsubscribe(OnPxiChassisSelected);
                _eventAggregator.GetEvent<WindowMinimizedEvent>()?.Unsubscribe(OnWindowMinimized);
                _eventAggregator.GetEvent<WindowClosingEvent>()?.Unsubscribe(OnWindowClosing);
                _eventAggregator.GetEvent<ProjectClosedEvent>()?.Unsubscribe(OnProjectClosed);
                _eventAggregator.GetEvent<WindowRestoredEvent>()?.Unsubscribe(OnWindowRestored);
                _eventAggregator.GetEvent<HideCurrentPageEvent>()?.Unsubscribe(OnHideCurrentPage);
                _eventAggregator.GetEvent<ReleaseCurrentPageEvent>()?.Unsubscribe(OnReleaseCurrentPage);
                _eventAggregator.GetEvent<PageFloatedEvent>()?.Unsubscribe(OnPageFloated);
                _eventAggregator.GetEvent<PageEmbeddedEvent>()?.Unsubscribe(OnPageEmbedded);
                //_eventAggregator.GetEvent<FloatingWindowMinimizedEvent>()?.Unsubscribe(OnFloatingWindowMinimized);
                //_eventAggregator.GetEvent<FloatingWindowRestoredEvent>()?.Unsubscribe(OnFloatingWindowRestored);
                _eventAggregator.GetEvent<FloatingWindowActivatedEvent>()?.Unsubscribe(OnFloatingWindowActivated);
                _eventAggregator.GetEvent<WindowActivatedEvent>()?.Unsubscribe(OnWindowActivated);
            }
            catch (Exception)
            {
            }
        }

        #endregion

        #region Project Save State Management

        /// <summary>
        /// 标记项目为已修改
        /// </summary>
        public void MarkProjectAsModified()
        {
            _projectSaveStateService.MarkAsModified();
            IsProjectModified = true;
        }

        /// <summary>
        /// 检查项目是否有未保存的更改
        /// </summary>
        public bool HasUnsavedChanges => _projectSaveStateService.HasUnsavedChanges;

        ///// <summary>
        ///// 自动保存项目
        ///// </summary>
        //private void AutoSaveProject()
        //{
        //    try
        //    {
        //        if (CurrentProject != null && CurrentProject.Count > 0 && !string.IsNullOrEmpty(_currentProjectPath))
        //        {
        //            SaveProjectInternal();
        //        }
        //    }
        //    catch (Exception)
        //    {
        //    }
        //}

        /// <summary>
        /// 内部保存项目方法，统一处理所有保存逻辑
        /// </summary>
        private void SaveProjectInternal()
        {
            if (CurrentProject == null || CurrentProject.Count == 0 || string.IsNullOrEmpty(_currentProjectPath))
            {
                throw new InvalidOperationException("当前没有可保存的项目或项目路径无效");
            }

            // 通知配置面板在序列化项目前提交未保存的更改
            _eventAggregator?.GetEvent<ProjectSavingEvent>()?.Publish();

            var projectToSave = CurrentProject[0];

            // 1. 保存机箱数据到项目
            _pxiChassisService.SaveChassisData(projectToSave.PxiChassisData);

            // 2. 请求并保存机箱连接数据
            var connectionsRequestArgs = new ChassisConnectionsRequestEventArgs();
            _eventAggregator.GetEvent<ChassisConnectionsRequestEvent>().Publish(connectionsRequestArgs);

            if (connectionsRequestArgs.Connections != null)
            {
                projectToSave.ChassisConnections.Clear();
                foreach (var connection in connectionsRequestArgs.Connections)
                {
                    projectToSave.ChassisConnections.Add(connection);
                }
            }

            // 3. 请求并保存连接线数据
            var connectionLinesRequestArgs = new ConnectionLinesRequestEventArgs();
            _eventAggregator.GetEvent<ConnectionLinesRequestEvent>().Publish(connectionLinesRequestArgs);

            if (connectionLinesRequestArgs.ConnectionLines != null && connectionLinesRequestArgs.ConnectionLines.Count > 0)
            {
                projectToSave.ConnectionLines.Clear();
                foreach (var connectionLine in connectionLinesRequestArgs.ConnectionLines)
                {
                    projectToSave.ConnectionLines.Add(connectionLine);
                }
            }
            else
            {
            }

            // 4. 请求并保存通道配置表数据
            // 首先尝试通过事件获取数据（兼容现有逻辑）
            var channelTabelItemsRequestArgs = new ChannelTabelItemsRequestEventArgs();
            _eventAggregator.GetEvent<ChannelTabelItemsRequestEvent>().Publish(channelTabelItemsRequestArgs);

            // 同时从静态方法获取数据（确保即使没有打开的页面也能获取数据）
            var allChannelTabelItems = ChannelConfigTabelViewModel.GetAllChannelTabelItems();

            // 合并数据：优先使用事件返回的数据（如果有），否则使用静态方法获取的数据
            projectToSave.ChannelTabelItems.Clear();
            if (channelTabelItemsRequestArgs.ChannelTabelItems != null && channelTabelItemsRequestArgs.ChannelTabelItems.Count > 0)
            {
                // 使用事件返回的数据
                foreach (var kvp in channelTabelItemsRequestArgs.ChannelTabelItems)
                {
                    projectToSave.ChannelTabelItems[kvp.Key] = kvp.Value;
                }
            }
            else if (allChannelTabelItems != null && allChannelTabelItems.Count > 0)
            {
                // 使用静态方法获取的数据
                foreach (var kvp in allChannelTabelItems)
                {
                    projectToSave.ChannelTabelItems[kvp.Key] = kvp.Value;
                }
            }
            // 如果两种方法都没有数据，字典保持为空（已在上面清空）

            // 5. 请求并保存信号配置表数据
            var signalTabelItemsRequestArgs = new SignalTabelItemsRequestEventArgs();
            _eventAggregator.GetEvent<SignalTabelItemsRequestEvent>().Publish(signalTabelItemsRequestArgs);

            // 同时从静态方法获取数据（确保即使没有打开的页面也能获取数据）
            var allSignalTabelItems = SignalConfigTabelViewModel.GetAllSignalTabelItems();

            // 合并数据：优先使用事件返回的数据（如果有），否则使用静态方法获取的数据
            projectToSave.SignalTabelItems.Clear();
            if (signalTabelItemsRequestArgs.SignalTabelItems != null && signalTabelItemsRequestArgs.SignalTabelItems.Count > 0)
            {
                // 使用事件返回的数据
                foreach (var kvp in signalTabelItemsRequestArgs.SignalTabelItems)
                {
                    projectToSave.SignalTabelItems[kvp.Key] = kvp.Value;
                }
            }
            else if (allSignalTabelItems != null && allSignalTabelItems.Count > 0)
            {
                // 使用静态方法获取的数据
                foreach (var kvp in allSignalTabelItems)
                {
                    projectToSave.SignalTabelItems[kvp.Key] = kvp.Value;
                }
            }
            // 如果两种方法都没有数据，字典保持为空（已在上面清空）

            // 6. 请求并保存ICD配置表数据
            var icdTabelItemsRequestArgs = new IcdTabelItemsRequestEventArgs();
            _eventAggregator.GetEvent<IcdTabelItemsRequestEvent>().Publish(icdTabelItemsRequestArgs);

            // 同时从静态方法获取数据（确保即使没有打开的页面也能获取数据）
            var allIcdTabelItems = ViewModels.IcdConfig.IcdConfigTabelViewModel.GetAllIcdTabelItems();

            // 合并数据：优先使用事件返回的数据（如果有），否则使用静态方法获取的数据
            projectToSave.IcdTabelItems.Clear();
            if (icdTabelItemsRequestArgs.IcdTabelItems != null && icdTabelItemsRequestArgs.IcdTabelItems.Count > 0)
            {
                // 使用事件返回的数据
                foreach (var kvp in icdTabelItemsRequestArgs.IcdTabelItems)
                {
                    projectToSave.IcdTabelItems[kvp.Key] = kvp.Value;
                }
            }
            else if (allIcdTabelItems != null && allIcdTabelItems.Count > 0)
            {
                // 使用静态方法获取的数据
                foreach (var kvp in allIcdTabelItems)
                {
                    projectToSave.IcdTabelItems[kvp.Key] = kvp.Value;
                }
            }
            // 如果两种方法都没有数据，字典保持为空（已在上面清空）

            // 7. 请求并保存通讯信号配置表数据
            var IcdMappingItemsRequestArgs = new IcdMappingItemsRequestEventArgs();
            _eventAggregator.GetEvent<IcdMappingItemsRequestEvent>().Publish(IcdMappingItemsRequestArgs);

            // 同时从静态方法获取数据（确保即使没有打开的页面也能获取数据）
            var allIcdMappingItems = IcdMappingTabelViewModel.GetAllIcdMappingItems();

            // 合并数据：优先使用事件返回的数据（如果有），否则使用静态方法获取的数据
            projectToSave.IcdMappingItems.Clear();
            if (IcdMappingItemsRequestArgs.SignalTabelItems != null && IcdMappingItemsRequestArgs.SignalTabelItems.Count > 0)
            {
                // 使用事件返回的数据
                foreach (var kvp in IcdMappingItemsRequestArgs.SignalTabelItems)
                {
                    projectToSave.IcdMappingItems[kvp.Key] = kvp.Value;
                }
            }
            else if (allIcdMappingItems != null && allIcdMappingItems.Count > 0)
            {
                // 使用静态方法获取的数据
                foreach (var kvp in allIcdMappingItems)
                {
                    projectToSave.IcdMappingItems[kvp.Key] = kvp.Value;
                }
            }
            // 如果两种方法都没有数据，字典保持为空（已在上面清空）

            // 8. 请求并保存标定数据
            var calibrationRecordsRequestArgs = new CalibrationRecordsRequestEventArgs();
            _eventAggregator.GetEvent<CalibrationRecordsRequestEvent>().Publish(calibrationRecordsRequestArgs);

            if (calibrationRecordsRequestArgs.CalibrationRecords != null)
            {
                projectToSave.CalibrationRecords.Clear();
                foreach (var kvp in calibrationRecordsRequestArgs.CalibrationRecords)
                {
                    projectToSave.CalibrationRecords[kvp.Key] = kvp.Value;
                }
            }

            // 9. 保存项目到文件
            _projectService.SaveProject(projectToSave, _currentProjectPath);

            // 10. 更新保存状态
            _projectSaveStateService.MarkAsSaved();
            IsProjectModified = false;
        }


        /// <summary>
        /// 项目保存状态变化事件处理
        /// </summary>
        private void OnProjectSaveStateChanged(object sender, bool hasUnsavedChanges)
        {
            // 添加UI更新逻辑，如更新窗口标题显示*号
        }

        /// <summary>
        /// 处理项目关闭事件（用于额外的清理工作）
        /// </summary>
        private void OnProjectClosed()
        {
            // 项目关闭时的清理工作（如需要）
        }

        #endregion
    }
}
