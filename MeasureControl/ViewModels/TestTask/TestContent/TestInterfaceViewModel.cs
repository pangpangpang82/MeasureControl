using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using MeasureControl.Drivers;
using MeasureControl.Events;
using MeasureControl.Helpers;
using MeasureControl.Models;
using MeasureControl.Models.Devices;
using MeasureControl.Services;
using MeasureControl.ViewModels.TestTask.ConfigTabel;
using MeasureControl.Views.Dialogs;
using Prism.Commands;
using Prism.Events;
using Prism.Mvvm;
using Prism.Regions;

namespace MeasureControl.ViewModels
{
    /// <summary>
    /// 测试界面的ViewModel
    /// </summary>
    public class TestInterfaceViewModel : BindableBase, INavigationAware, IDisposable
    {
        private readonly IRegionManager _regionManager;
        private readonly IEventAggregator _eventAggregator;
        private readonly IPxiChassisService _pxiChassisService;
        private readonly SignalValueUpdateService _signalValueUpdateService;

        #region Properties

        private string _chassisName;
        /// <summary>
        /// 机箱名称
        /// </summary>
        public string ChassisName
        {
            get => _chassisName;
            set => SetProperty(ref _chassisName, value);
        }

        private string _testTaskName;
        /// <summary>
        /// 测试任务名称
        /// </summary>
        public string TestTaskName
        {
            get => _testTaskName;
            set => SetProperty(ref _testTaskName, value);
        }

        private string _configTabelName;
        /// <summary>
        /// 测试界面名称
        /// </summary>
        public string ConfigTabelName
        {
            get => _configTabelName;
            set => SetProperty(ref _configTabelName, value);
        }

        private string _parentType;
        private bool _disposed = false;
        /// <summary>
        /// 父节点类型
        /// </summary>
        public string ParentType
        {
            get => _parentType;
            set => SetProperty(ref _parentType, value);
        }

        private string _displayPath;
        /// <summary>
        /// 显示路径（用于界面标题）
        /// </summary>
        public string DisplayPath
        {
            get => _displayPath;
            set => SetProperty(ref _displayPath, value);
        }

        private ObservableCollection<TestInterfaceControlItem> _controls;
        /// <summary>
        /// 界面控件列表
        /// </summary>
        public ObservableCollection<TestInterfaceControlItem> Controls
        {
            get => _controls ?? (_controls = new ObservableCollection<TestInterfaceControlItem>());
            set => SetProperty(ref _controls, value);
        }

        /// <summary>
        /// 项目数据引用（用于保存）
        /// </summary>
        public ProjectItem ProjectData { get; set; }

        /// <summary>
        /// 数据Key（格式：机箱名/测试任务名/界面名，如果没有机箱名则使用：测试任务名/界面名）
        /// </summary>
        public string DataKey
        {
            get
            {
                if (!string.IsNullOrEmpty(ChassisName))
                {
                    return $"{ChassisName}/{TestTaskName}/{ConfigTabelName}";
                }
                return $"{TestTaskName}/{ConfigTabelName}";
            }
        }

        private TestInterfaceControlItem _selectedControl;
        /// <summary>
        /// 当前选中的控件
        /// </summary>
        public TestInterfaceControlItem SelectedControl
        {
            get => _selectedControl;
            set
            {
                if (SetProperty(ref _selectedControl, value))
                {
                    RaisePropertyChanged(nameof(SelectedControlTitle));
                    RaisePropertyChanged(nameof(HasSelectedControl));
                }
            }
        }

        /// <summary>
        /// 选中控件的标题（用于配置面板显示）
        /// </summary>
        public string SelectedControlTitle
        {
            get
            {
                if (SelectedControl == null) return "暂无信息";
                return SelectedControl.ControlType switch
                {
                    "Button" => "按钮配置",
                    "Switch" => "开关配置",
                    "Indicator" => "指示灯配置",
                    "TextLabel" => "标签配置",
                    "DisplayBox" => "显示框配置",
                    "InputBox" => "输入框配置",
                    _ => $"{SelectedControl.ControlType}配置"
                };
            }
        }

        /// <summary>
        /// 是否有选中的控件
        /// </summary>
        public bool HasSelectedControl => SelectedControl != null;

        private ObservableCollection<VariableItem> _availableVariables;
        /// <summary>
        /// 可用的变量列表（用于数据源下拉框）
        /// </summary>
        public ObservableCollection<VariableItem> AvailableVariables
        {
            get => _availableVariables ?? (_availableVariables = new ObservableCollection<VariableItem>());
            set => SetProperty(ref _availableVariables, value);
        }

        private ObservableCollection<string> _availableVariableNames;
        /// <summary>
        /// 可用的变量名称列表（用于数据源下拉框显示）
        /// </summary>
        public ObservableCollection<string> AvailableVariableNames
        {
            get => _availableVariableNames ?? (_availableVariableNames = new ObservableCollection<string>());
            set => SetProperty(ref _availableVariableNames, value);
        }

        private ObservableCollection<ControlConfigItem> _controlConfigItems;
        /// <summary>
        /// 控件配置项列表（用于下方配置面板）
        /// </summary>
        public ObservableCollection<ControlConfigItem> ControlConfigItems
        {
            get => _controlConfigItems ?? (_controlConfigItems = new ObservableCollection<ControlConfigItem>());
            set => SetProperty(ref _controlConfigItems, value);
        }

        private bool _isTestRunning;
        /// <summary>
        /// 测试界面是否正在运行测试
        /// </summary>
        public bool IsTestRunning
        {
            get => _isTestRunning;
            set
            {
                if (SetProperty(ref _isTestRunning, value))
                {
                    StartPauseTestCommand?.RaiseCanExecuteChanged();
                    StopTestCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        private bool _isTestPaused;

        private bool _canExecuteTestCommands = false; // 根据MainWindow的测试状态控制
        /// <summary>
        /// 测试界面测试是否暂停
        /// </summary>
        public bool IsTestPaused
        {
            get => _isTestPaused;
            set
            {
                if (SetProperty(ref _isTestPaused, value))
                {
                    // 暂停状态变化时更新测试界面节点高亮
                    UpdateTestInterfaceHighlighting();

                    StartPauseTestCommand?.RaiseCanExecuteChanged();
                    StopTestCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        /// <summary>
        /// 是否允许执行测试命令（与MinWindow的启动状态相关）
        /// </summary>
        public bool CanExecuteTestCommands
        {
            get => _canExecuteTestCommands;
            set
            {
                if (SetProperty(ref _canExecuteTestCommands, value))
                {
                    StartPauseTestCommand?.RaiseCanExecuteChanged();
                    StopTestCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        #endregion

        #region Commands

        // 浮动窗口命令
        public DelegateCommand FloatWindowCommand { get; }
        public DelegateCommand MinimizeInRegionCommand { get; }
        public DelegateCommand CloseInRegionCommand { get; }

        // 测试控制命令
        public DelegateCommand StartPauseTestCommand { get; }
        public DelegateCommand StopTestCommand { get; }

        #endregion

        #region Constructor

        public TestInterfaceViewModel(IRegionManager regionManager, IEventAggregator eventAggregator, IPxiChassisService pxiChassisService, SignalValueUpdateService signalValueUpdateService)
        {
            _regionManager = regionManager ?? throw new ArgumentNullException(nameof(regionManager));
            _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
            _pxiChassisService = pxiChassisService ?? throw new ArgumentNullException(nameof(pxiChassisService));
            _signalValueUpdateService = signalValueUpdateService;
            
            // 浮动窗口命令
            FloatWindowCommand = new DelegateCommand(OnFloatWindow);
            MinimizeInRegionCommand = new DelegateCommand(OnMinimizeInRegion);
            CloseInRegionCommand = new DelegateCommand(OnCloseInRegion);
            
            // 测试控制命令 - 只有在MinWindow启动测试后才允许点击
            StartPauseTestCommand = new DelegateCommand(OnStartPauseTest, () => CanExecuteTestCommands);
            StopTestCommand = new DelegateCommand(OnStopTest, () => CanExecuteTestCommands && (IsTestRunning || IsTestPaused));
            
            DisplayPath = "测试界面";
        }

        #endregion

        #region INavigationAware Implementation

        public void OnNavigatedTo(NavigationContext navigationContext)
        {
            // 检查是否为复用实例（已有测试任务名和配置表名）
            bool isReusedInstance = !string.IsNullOrEmpty(TestTaskName) && !string.IsNullOrEmpty(ConfigTabelName);
            
            // 从导航参数中获取信息
            if (navigationContext.Parameters.ContainsKey("ChassisName"))
            {
                ChassisName = navigationContext.Parameters["ChassisName"] as string;
            }

            if (navigationContext.Parameters.ContainsKey("TestTaskName"))
            {
                TestTaskName = navigationContext.Parameters["TestTaskName"] as string;
            }

            if (navigationContext.Parameters.ContainsKey("ConfigTabelName"))
            {
                ConfigTabelName = navigationContext.Parameters["ConfigTabelName"] as string;
            }

            if (navigationContext.Parameters.ContainsKey("ParentType"))
            {
                ParentType = navigationContext.Parameters["ParentType"] as string;
            }

            // 获取项目数据引用
            if (navigationContext.Parameters.ContainsKey("ProjectData"))
            {
                ProjectData = navigationContext.Parameters["ProjectData"] as ProjectItem;
            }

            // 生成显示路径，包含机箱名称
            string parentName = GetParentDisplayName(ParentType);
            if (!string.IsNullOrEmpty(ChassisName))
            {
                DisplayPath = $"{ChassisName}/{TestTaskName}/{parentName}/{ConfigTabelName}";
            }
            else
            {
                DisplayPath = $"{TestTaskName}/{parentName}/{ConfigTabelName}";
            }

            // 只在新实例时加载控件，复用实例保持现有状态
            if (!isReusedInstance)
            {
                LoadControls();
            }
        }

        public bool IsNavigationTarget(NavigationContext navigationContext)
        {
            // 检查是否为同一个测试界面（相同的机箱名 + 测试任务名 + 测试界面名）
            // 如果匹配则复用当前实例，保持测试运行状态
            var chassisName = navigationContext.Parameters.ContainsKey("ChassisName")
                ? navigationContext.Parameters["ChassisName"] as string
                : null;
            var testTaskName = navigationContext.Parameters["TestTaskName"] as string;
            var configTabelName = navigationContext.Parameters["ConfigTabelName"] as string;
            
            bool isSameInterface = chassisName == ChassisName && 
                                   testTaskName == TestTaskName && 
                                   configTabelName == ConfigTabelName;
            return isSameInterface;
        }

        public void OnNavigatedFrom(NavigationContext navigationContext)
        {
            // 导航离开时不停止测试，保持运行状态
            // 测试只在用户点击"停止"按钮时才会停止
        }

        #endregion

        #region IDisposable Implementation

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                // 清理资源
                _disposed = true;
            }
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// 获取父节点显示名称
        /// </summary>
        private string GetParentDisplayName(string parentType)
        {
            return parentType switch
            {
                "channel_config" => "通道配置",
                "icd_config" => "ICD配置",
                "signal_config" => "信号配置",
                "test_ui" => "测试界面",
                "test_sequence" => "测试序列",
                "report" => "报表",
                _ => parentType
            };
        }

        /// <summary>
        /// 加载界面控件数据
        /// </summary>
        public void LoadControls()
        {
            if (ProjectData?.TestInterfaceControls == null) return;

            if (ProjectData.TestInterfaceControls.TryGetValue(DataKey, out var controls))
            {
                Controls = new ObservableCollection<TestInterfaceControlItem>(controls);
            }
        }

        /// <summary>
        /// 保存界面控件数据
        /// </summary>
        public void SaveControls()
        {
            if (ProjectData == null) return;

            if (ProjectData.TestInterfaceControls == null)
            {
                ProjectData.TestInterfaceControls = new Dictionary<string, List<TestInterfaceControlItem>>();
            }

            ProjectData.TestInterfaceControls[DataKey] = Controls.ToList();
            
            // 调试：输出保存的控件信息
            System.Diagnostics.Debug.WriteLine($"[SaveControls] 保存 {Controls.Count} 个控件到 {DataKey}");
            foreach (var c in Controls)
            {
                System.Diagnostics.Debug.WriteLine($"[SaveControls] Control: Name={c.Name}, BoundVariablePath='{c.BoundVariablePath}'");
            }
            
            // 发布项目修改事件
            _eventAggregator.GetEvent<Events.ProjectModifiedEvent>().Publish(new Events.ProjectModifiedEventArgs
            {
                ModificationType = "TestInterface",
                Description = $"测试界面控件更新: {DataKey}"
            });
        }

        /// <summary>
        /// 添加控件
        /// </summary>
        public void AddControl(TestInterfaceControlItem control)
        {
            Controls.Add(control);
            SaveControls();
        }

        /// <summary>
        /// 移除控件
        /// </summary>
        public void RemoveControl(TestInterfaceControlItem control)
        {
            Controls.Remove(control);
            SaveControls();
        }

        /// <summary>
        /// 更新控件位置
        /// </summary>
        public void UpdateControlPosition(string controlId, double x, double y)
        {
            var control = Controls.FirstOrDefault(c => c.Id == controlId);
            if (control != null)
            {
                control.PositionX = x;
                control.PositionY = y;
                SaveControls();
            }
        }

        /// <summary>
        /// 更新控件大小
        /// </summary>
        public void UpdateControlSize(string controlId, double width, double height)
        {
            var control = Controls.FirstOrDefault(c => c.Id == controlId);
            if (control != null)
            {
                control.Width = width;
                control.Height = height;
                SaveControls();
            }
        }

        /// <summary>
        /// 选中控件（由 View 调用，更新下方配置面板）
        /// </summary>
        public void SelectControl(TestInterfaceControlItem control)
        {
            SelectedControl = control;
            
            // 根据控件类型加载可用变量列表和配置项
            if (control != null)
            {
                LoadAvailableVariables(control.ControlType);
                GenerateControlConfigItems(control);
            }
            else
            {
                ControlConfigItems.Clear();
            }
        }
        
        /// <summary>
        /// 删除控件
        /// </summary>
        public void DeleteControl(string controlId)
        {
            if (string.IsNullOrEmpty(controlId)) return;
            
            var control = Controls.FirstOrDefault(c => c.Id == controlId);
            if (control != null)
            {
                Controls.Remove(control);
                SaveControls();
            }
        }
        
        /// <summary>
        /// 清除选中状态
        /// </summary>
        public void ClearSelection()
        {
            SelectedControl = null;
            ControlConfigItems.Clear();
        }

        /// <summary>
        /// 根据控件类型生成配置项
        /// </summary>
        private void GenerateControlConfigItems(TestInterfaceControlItem control)
        {
            ControlConfigItems.Clear();

            // 按钮和文字标签不需要控件名称（按钮文字/标签文字足以表示）
            bool needsControlName = control.ControlType != "Button" && control.ControlType != "TextLabel";
            
            if (needsControlName)
            {
                ControlConfigItems.Add(new ControlConfigItem
                {
                    PropertyName = "Name",
                    Label = "控件名称",
                    Value = control.Name ?? "",
                    ConfigType = "TextBox",
                    IsEnabled = true
                });
            }

            switch (control.ControlType)
            {
                case "Button":
                    // 按钮：按钮文字、底部颜色、文字颜色、数据源（不需要控件名称）
                    ControlConfigItems.Add(new ControlConfigItem
                    {
                        PropertyName = "ButtonText",
                        Label = "按钮文字",
                        Value = control.ButtonText ?? "按钮",
                        ConfigType = "TextBox",
                        IsEnabled = true
                    });
                    ControlConfigItems.Add(new ControlConfigItem
                    {
                        PropertyName = "BackgroundColor",
                        Label = "底部颜色",
                        Value = control.BackgroundColor ?? "#e8ebed",
                        ConfigType = "ColorPicker",
                        IsEnabled = true
                    });
                    ControlConfigItems.Add(new ControlConfigItem
                    {
                        PropertyName = "TextColor",
                        Label = "文字颜色",
                        Value = control.TextColor ?? "#000000",
                        ConfigType = "ColorPicker",
                        IsEnabled = true
                    });
                    ControlConfigItems.Add(new ControlConfigItem
                    {
                        PropertyName = "BoundVariablePath",
                        Label = "数据源",
                        Value = control.BoundVariablePath ?? "",
                        ConfigType = "ComboBox",
                        IsEnabled = true,
                        SimpleOptions = new ObservableCollection<string>(AvailableVariableNames)
                    });
                    break;

                case "Switch":
                    // 开关：名称、数据源
                    ControlConfigItems.Add(new ControlConfigItem
                    {
                        PropertyName = "BoundVariablePath",
                        Label = "数据源",
                        Value = control.BoundVariablePath ?? "",
                        ConfigType = "ComboBox",
                        IsEnabled = true,
                        SimpleOptions = new ObservableCollection<string>(AvailableVariableNames)
                    });
                    break;

                case "Indicator":
                    // 指示灯：名称、数据源（实时更新，无需配置刷新频率）
                    ControlConfigItems.Add(new ControlConfigItem
                    {
                        PropertyName = "BoundVariablePath",
                        Label = "数据源",
                        Value = control.BoundVariablePath ?? "",
                        ConfigType = "ComboBox",
                        IsEnabled = true,
                        SimpleOptions = new ObservableCollection<string>(AvailableVariableNames)
                    });
                    break;

                case "TextLabel":
                    // 标签：只有标签文字（纯文本标签，无颜色配置）
                    ControlConfigItems.Add(new ControlConfigItem
                    {
                        PropertyName = "ButtonText",
                        Label = "标签文字",
                        Value = control.ButtonText ?? control.Name ?? "标签",
                        ConfigType = "TextBox",
                        IsEnabled = true
                    });
                    break;

                case "DisplayBox":
                    // 显示框：名称、底部颜色、文字颜色、数据源、刷新频率、小数位数
                    ControlConfigItems.Add(new ControlConfigItem
                    {
                        PropertyName = "BackgroundColor",
                        Label = "底部颜色",
                        Value = control.BackgroundColor ?? "#e8ebed",
                        ConfigType = "ColorPicker",
                        IsEnabled = true
                    });
                    ControlConfigItems.Add(new ControlConfigItem
                    {
                        PropertyName = "TextColor",
                        Label = "文字颜色",
                        Value = control.TextColor ?? "#000000",
                        ConfigType = "ColorPicker",
                        IsEnabled = true
                    });
                    ControlConfigItems.Add(new ControlConfigItem
                    {
                        PropertyName = "BoundVariablePath",
                        Label = "数据源",
                        Value = control.BoundVariablePath ?? "",
                        ConfigType = "ComboBox",
                        IsEnabled = true,
                        SimpleOptions = new ObservableCollection<string>(AvailableVariableNames)
                    });
                    ControlConfigItems.Add(new ControlConfigItem
                    {
                        PropertyName = "RefreshRate",
                        Label = "刷新频率",
                        Value = (control.RefreshRate > 0 ? control.RefreshRate : 10) + " Hz",
                        ConfigType = "SimpleComboBox",
                        IsEnabled = true,
                        SimpleOptions = new ObservableCollection<string> { "10 Hz", "50 Hz", "100 Hz", "500 Hz" }
                    });
                    ControlConfigItems.Add(new ControlConfigItem
                    {
                        PropertyName = "DecimalPlaces",
                        Label = "小数位数",
                        Value = control.DecimalPlaces.ToString(),
                        ConfigType = "SimpleComboBox",
                        IsEnabled = true,
                        SimpleOptions = new ObservableCollection<string> { "0", "1", "2", "3", "4", "5" }
                    });
                    break;

                case "InputBox":
                    // 输入框：名称、底部颜色、文字颜色、数据源、小数位数
                    ControlConfigItems.Add(new ControlConfigItem
                    {
                        PropertyName = "BackgroundColor",
                        Label = "底部颜色",
                        Value = control.BackgroundColor ?? "#e8ebed",
                        ConfigType = "ColorPicker",
                        IsEnabled = true
                    });
                    ControlConfigItems.Add(new ControlConfigItem
                    {
                        PropertyName = "TextColor",
                        Label = "文字颜色",
                        Value = control.TextColor ?? "#000000",
                        ConfigType = "ColorPicker",
                        IsEnabled = true
                    });
                    ControlConfigItems.Add(new ControlConfigItem
                    {
                        PropertyName = "BoundVariablePath",
                        Label = "数据源",
                        Value = control.BoundVariablePath ?? "",
                        ConfigType = "ComboBox",
                        IsEnabled = true,
                        SimpleOptions = new ObservableCollection<string>(AvailableVariableNames)
                    });
                    ControlConfigItems.Add(new ControlConfigItem
                    {
                        PropertyName = "DecimalPlaces",
                        Label = "小数位数",
                        Value = control.DecimalPlaces.ToString(),
                        ConfigType = "SimpleComboBox",
                        IsEnabled = true,
                        SimpleOptions = new ObservableCollection<string> { "0", "1", "2", "3", "4", "5" }
                    });
                    break;

                case "CircularGauge":
                    // 环形仪表：名称、单位、最大值、数据源、小数位数、刷新频率
                    ControlConfigItems.Add(new ControlConfigItem
                    {
                        PropertyName = "Unit",
                        Label = "单位",
                        Value = control.Unit ?? "",
                        ConfigType = "TextBox",
                        IsEnabled = true
                    });
                    ControlConfigItems.Add(new ControlConfigItem
                    {
                        PropertyName = "MaxValue",
                        Label = "最大值",
                        Value = (control.MaxValue > 0 ? control.MaxValue : 100.0).ToString(),
                        ConfigType = "TextBox",
                        IsEnabled = true
                    });
                    ControlConfigItems.Add(new ControlConfigItem
                    {
                        PropertyName = "BoundVariablePath",
                        Label = "数据源",
                        Value = control.BoundVariablePath ?? "",
                        ConfigType = "ComboBox",
                        IsEnabled = true,
                        SimpleOptions = new ObservableCollection<string>(AvailableVariableNames)
                    });
                    ControlConfigItems.Add(new ControlConfigItem
                    {
                        PropertyName = "ManualValue",
                        Label = "手动设置当前值",
                        Value = control.ManualValue?.ToString() ?? "",
                        ConfigType = "TextBox",
                        IsEnabled = true
                    });
                    ControlConfigItems.Add(new ControlConfigItem
                    {
                        PropertyName = "DecimalPlaces",
                        Label = "小数位数",
                        Value = control.DecimalPlaces.ToString(),
                        ConfigType = "SimpleComboBox",
                        IsEnabled = true,
                        SimpleOptions = new ObservableCollection<string> { "0", "1", "2", "3", "4", "5" }
                    });
                    ControlConfigItems.Add(new ControlConfigItem
                    {
                        PropertyName = "RefreshRate",
                        Label = "刷新频率",
                        Value = (control.RefreshRate > 0 ? control.RefreshRate : 10) + " Hz",
                        ConfigType = "SimpleComboBox",
                        IsEnabled = true,
                        SimpleOptions = new ObservableCollection<string> { "10 Hz", "50 Hz", "100 Hz", "500 Hz" }
                    });
                    break;

                case "VerticalGauge":
                    // 竖形仪表：名称、单位、最大值、数据源、手动设置当前值、小数位数、刷新频率
                    ControlConfigItems.Add(new ControlConfigItem
                    {
                        PropertyName = "Unit",
                        Label = "单位",
                        Value = control.Unit ?? "",
                        ConfigType = "TextBox",
                        IsEnabled = true
                    });
                    ControlConfigItems.Add(new ControlConfigItem
                    {
                        PropertyName = "MaxValue",
                        Label = "最大值",
                        Value = (control.MaxValue > 0 ? control.MaxValue : 100.0).ToString(),
                        ConfigType = "TextBox",
                        IsEnabled = true
                    });
                    ControlConfigItems.Add(new ControlConfigItem
                    {
                        PropertyName = "BoundVariablePath",
                        Label = "数据源",
                        Value = control.BoundVariablePath ?? "",
                        ConfigType = "ComboBox",
                        IsEnabled = true,
                        SimpleOptions = new ObservableCollection<string>(AvailableVariableNames)
                    });
                    ControlConfigItems.Add(new ControlConfigItem
                    {
                        PropertyName = "ManualValue",
                        Label = "手动设置当前值",
                        Value = control.ManualValue?.ToString() ?? "",
                        ConfigType = "TextBox",
                        IsEnabled = true
                    });
                    ControlConfigItems.Add(new ControlConfigItem
                    {
                        PropertyName = "DecimalPlaces",
                        Label = "小数位数",
                        Value = control.DecimalPlaces.ToString(),
                        ConfigType = "SimpleComboBox",
                        IsEnabled = true,
                        SimpleOptions = new ObservableCollection<string> { "0", "1", "2", "3", "4", "5" }
                    });
                    ControlConfigItems.Add(new ControlConfigItem
                    {
                        PropertyName = "RefreshRate",
                        Label = "刷新频率",
                        Value = (control.RefreshRate > 0 ? control.RefreshRate : 10) + " Hz",
                        ConfigType = "SimpleComboBox",
                        IsEnabled = true,
                        SimpleOptions = new ObservableCollection<string> { "10 Hz", "50 Hz", "100 Hz", "500 Hz" }
                    });
                    break;
            }

            // 订阅属性变更事件
            foreach (var configItem in ControlConfigItems)
            {
                configItem.PropertyChanged += ConfigItem_PropertyChanged;
            }
        }

        /// <summary>
        /// 配置项属性变更处理
        /// </summary>
        private void ConfigItem_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (sender is ControlConfigItem item && e.PropertyName == "Value")
            {
                UpdateControlProperty(item.PropertyName, item.Value);
            }
        }

        /// <summary>
        /// 加载可用变量列表（根据控件类型过滤）
        /// </summary>
        private void LoadAvailableVariables(string controlType)
        {
            AvailableVariables.Clear();
            AvailableVariableNames.Clear();
            
            if (string.IsNullOrEmpty(TestTaskName)) return;

            // 获取所有信号配置表数据
            var allSignalTabelItems = SignalConfigTabelViewModel.GetAllSignalTabelItems();
            if (allSignalTabelItems == null || allSignalTabelItems.Count == 0) return;

            // 从通道配置表中构建通道方向字典：key = "配置表名:通道名称"，value = 输入输出类型(DI/DO/AI/AO)
            var channelDirectionDict = new Dictionary<string, string>();
            var allChannelTabelItems = ChannelConfigTabelViewModel.GetAllChannelTabelItems();
            if (allChannelTabelItems != null && allChannelTabelItems.Count > 0)
            {
                foreach (var kvp in allChannelTabelItems)
                {
                    // 提取配置表名称（key 格式：机箱名/测试任务名/通道表名 或 测试任务名/通道表名）
                    string configTabelName = kvp.Key;
                    if (!string.IsNullOrEmpty(ChassisName) && !string.IsNullOrEmpty(TestTaskName))
                    {
                        // 新格式：机箱名/测试任务名/通道表名
                        string expectedPrefix = $"{ChassisName}/{TestTaskName}/";
                        if (kvp.Key.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
                        {
                            configTabelName = kvp.Key.Substring(expectedPrefix.Length);
                        }
                        else
                        {
                            // 不匹配当前机箱和测试任务，跳过
                            continue;
                        }
                    }
                    else if (!string.IsNullOrEmpty(TestTaskName))
                    {
                        // 旧格式：测试任务名/通道表名（向后兼容）
                        string expectedPrefix = $"{TestTaskName}/";
                        if (kvp.Key.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
                        {
                            configTabelName = kvp.Key.Substring(expectedPrefix.Length);
                        }
                        else
                        {
                            // 不匹配当前测试任务，跳过
                            continue;
                        }
                    }
                    else if (kvp.Key.Contains("/"))
                    {
                        // 如果没有指定测试任务名称，但key包含"/"，则提取配置表名称（最后一个"/"之后的部分）
                        int lastSlashIndex = kvp.Key.LastIndexOf('/');
                        if (lastSlashIndex >= 0 && lastSlashIndex < kvp.Key.Length - 1)
                        {
                            configTabelName = kvp.Key.Substring(lastSlashIndex + 1);
                        }
                    }

                    foreach (var channel in kvp.Value)
                    {
                        if (channel == null || string.IsNullOrEmpty(channel.ChannelName) || string.IsNullOrEmpty(channel.InputOutputType))
                            continue;

                        string channelKey = $"{configTabelName}:{channel.ChannelName}";
                        if (!channelDirectionDict.ContainsKey(channelKey))
                        {
                            channelDirectionDict[channelKey] = channel.InputOutputType; // DI/DO/AI/AO
                        }
                    }
                }
            }

            // 控件 -> 需要的变量类型/方向
            bool needDigitalInput = controlType == "Indicator";
            bool needDigitalOutput = controlType == "Button" || controlType == "Switch" || controlType == "InputBox";
            bool needAnalogInput = controlType == "DisplayBox" || controlType == "CircularGauge" || controlType == "VerticalGauge";

            foreach (var kvp in allSignalTabelItems)
            {
                // 只获取当前机箱和测试任务下的信号配置表
                // kvp.Key 格式: "机箱名/测试任务1/变量表1" 或 "测试任务1/变量表1"（向后兼容）
                string expectedPrefix = null;
                if (!string.IsNullOrEmpty(ChassisName) && !string.IsNullOrEmpty(TestTaskName))
                {
                    expectedPrefix = $"{ChassisName}/{TestTaskName}/";
                }
                else if (!string.IsNullOrEmpty(TestTaskName))
                {
                    expectedPrefix = $"{TestTaskName}/";
                }
                
                if (expectedPrefix != null && kvp.Key.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    // 提取变量表名称（去掉前缀）
                    string signalTabelName = kvp.Key.Substring(expectedPrefix.Length);
                    
                    foreach (var signal in kvp.Value)
                    {
                        if (signal.IsEmpty || string.IsNullOrEmpty(signal.SignalName))
                            continue;

                        bool isDigital = signal.SignalType == "数字量";
                        bool isAnalog = signal.SignalType == "模拟量";

                        // 通过 ActualChannel + 通道配置表，推断通道方向（AI/DI = 输入，AO/DO = 输出）
                        string direction = null; // DI/DO/AI/AO
                        if (!string.IsNullOrEmpty(signal.ActualChannel))
                        {
                            // ActualChannel 格式："配置表名:通道名称"
                            var chParts = signal.ActualChannel.Split(new[] { ':' }, 2);
                            if (chParts.Length == 2)
                            {
                                string channelKey = $"{chParts[0]}:{chParts[1]}";
                                if (!channelDirectionDict.TryGetValue(channelKey, out direction))
                                {
                                    // 如果在通道表中找不到，退回到根据通道名前缀粗略判断
                                    string channelName = chParts[1];
                                    string prefix = new string(channelName.TakeWhile(c => !char.IsDigit(c)).ToArray());
                                    if (prefix == "AI" || prefix == "AO" || prefix == "DI" || prefix == "DO")
                                    {
                                        direction = prefix;
                                    }
                                }
                            }
                        }

                        bool isDigitalInput = isDigital && direction == "DI";
                        bool isDigitalOutput = isDigital && direction == "DO";
                        bool isAnalogInput = isAnalog && direction == "AI";
                        bool isAnalogOutput = isAnalog && direction == "AO";

                        bool match = false;
                        if (needDigitalInput && isDigitalInput) match = true;
                        if (needDigitalOutput && isDigitalOutput) match = true;
                        if (needAnalogInput && isAnalogInput) match = true;

                        // 若无法确定方向（例如未配置通道），则不允许绑定，避免误用
                        if (!match)
                            continue;

                        AvailableVariables.Add(new VariableItem
                        {
                            Name = signal.SignalName,
                            Type = signal.SignalType ?? "模拟量",
                            Unit = isDigital ? "" : signal.RealTimeValueUnit,
                            FullPath = $"{kvp.Key}/{signal.SignalName}"
                        });
                        // 格式: "变量表名:变量名"，例如 "变量表1:开关"
                        AvailableVariableNames.Add($"{signalTabelName}:{signal.SignalName}");
                    }
                }
            }
            
            System.Diagnostics.Debug.WriteLine($"[LoadAvailableVariables] 控件类型: {controlType}, 找到 {AvailableVariableNames.Count} 个变量");
            foreach (var name in AvailableVariableNames)
            {
                System.Diagnostics.Debug.WriteLine($"[LoadAvailableVariables]   - {name}");
            }
        }

        /// <summary>
        /// 更新控件属性
        /// </summary>
        public void UpdateControlProperty(string propertyName, object value)
        {
            System.Diagnostics.Debug.WriteLine($"[UpdateControlProperty] START propertyName='{propertyName}', value='{value}'");
            
            if (SelectedControl == null)
            {
                System.Diagnostics.Debug.WriteLine($"[UpdateControlProperty] SelectedControl is null, returning");
                return;
            }
            
            System.Diagnostics.Debug.WriteLine($"[UpdateControlProperty] SelectedControl.Id='{SelectedControl.Id}', Name='{SelectedControl.Name}'");

            // 检查 SelectedControl 是否在 Controls 集合中
            var inControls = Controls.Contains(SelectedControl);
            var matchById = Controls.FirstOrDefault(c => c.Id == SelectedControl.Id);
            System.Diagnostics.Debug.WriteLine($"[UpdateControlProperty] SelectedControl 在 Controls 中: {inControls}, 按ID匹配: {matchById != null}, 是同一对象: {ReferenceEquals(SelectedControl, matchById)}");
            
            switch (propertyName)
            {
                case "Name":
                    SelectedControl.Name = value as string;
                    break;
                case "ButtonText":
                    SelectedControl.ButtonText = value as string;
                    // 如果是标签控件，同时更新Name
                    if (SelectedControl.ControlType == "TextLabel")
                    {
                        SelectedControl.Name = value as string;
                        if (matchById != null && !ReferenceEquals(SelectedControl, matchById))
                        {
                            matchById.Name = value as string;
                        }
                    }
                    break;
                case "BackgroundColor":
                    SelectedControl.BackgroundColor = value as string;
                    break;
                case "TextColor":
                    SelectedControl.TextColor = value as string;
                    break;
                case "RefreshRate":
                    if (int.TryParse(value?.ToString(), out int rate))
                    {
                        SelectedControl.RefreshRate = rate;
                        if (matchById != null && !ReferenceEquals(SelectedControl, matchById))
                        {
                            matchById.RefreshRate = rate;
                        }
                    }
                    break;
                case "DecimalPlaces":
                    if (int.TryParse(value?.ToString(), out int decimalPlaces) && decimalPlaces >= 0 && decimalPlaces <= 5)
                    {
                        SelectedControl.DecimalPlaces = decimalPlaces;
                        if (matchById != null && !ReferenceEquals(SelectedControl, matchById))
                        {
                            matchById.DecimalPlaces = decimalPlaces;
                        }
                    }
                    break;
                case "BoundVariablePath":
                    // 当绑定变量路径时，自动获取单位
                    SelectedControl.BoundVariablePath = value as string;
                    System.Diagnostics.Debug.WriteLine($"[UpdateControlProperty] Set BoundVariablePath='{SelectedControl.BoundVariablePath}'");
                    // 同时更新 Controls 中的对象（如果不是同一引用）
                    if (matchById != null && !ReferenceEquals(SelectedControl, matchById))
                    {
                        matchById.BoundVariablePath = value as string;
                        System.Diagnostics.Debug.WriteLine($"[UpdateControlProperty] 同步更新 Controls 中的对象");
                    }
                    // 获取单位并更新
                    UpdateUnitFromVariablePath(SelectedControl);
                    if (matchById != null && !ReferenceEquals(SelectedControl, matchById))
                    {
                        UpdateUnitFromVariablePath(matchById);
                    }
                    break;
                case "Unit":
                    SelectedControl.Unit = value as string ?? "";
                    if (matchById != null && !ReferenceEquals(SelectedControl, matchById))
                    {
                        matchById.Unit = value as string ?? "";
                    }
                    break;
                case "MaxValue":
                    if (double.TryParse(value?.ToString(), out double maxValue) && maxValue > 0)
                    {
                        SelectedControl.MaxValue = maxValue;
                        if (matchById != null && !ReferenceEquals(SelectedControl, matchById))
                        {
                            matchById.MaxValue = maxValue;
                        }
                    }
                    break;
                case "ManualValue":
                    if (string.IsNullOrWhiteSpace(value?.ToString()))
                    {
                        SelectedControl.ManualValue = null;
                        if (matchById != null && !ReferenceEquals(SelectedControl, matchById))
                        {
                            matchById.ManualValue = null;
                        }
                    }
                    else if (double.TryParse(value?.ToString(), out double manualValue))
                    {
                        SelectedControl.ManualValue = manualValue;
                        if (matchById != null && !ReferenceEquals(SelectedControl, matchById))
                        {
                            matchById.ManualValue = manualValue;
                        }
                    }
                    break;
            }

            System.Diagnostics.Debug.WriteLine($"[UpdateControlProperty] Calling SaveControls...");
            SaveControls();
            System.Diagnostics.Debug.WriteLine($"[UpdateControlProperty] SaveControls completed");
            
            // 通知控件需要刷新
            _eventAggregator.GetEvent<Events.ControlPropertyChangedEvent>().Publish(
                new Events.ControlPropertyChangedEventArgs
                {
                    ControlId = SelectedControl.Id,
                    PropertyName = propertyName,
                    NewValue = value
                });
        }

        #endregion

        #region 设备特定输出函数

        /// <summary>
        /// 更新测试界面节点的高亮显示（浅色版本）
        /// </summary>
        private void UpdateTestInterfaceHighlighting()
        {
            if (ProjectData == null || string.IsNullOrEmpty(ChassisName) ||
                string.IsNullOrEmpty(TestTaskName) || string.IsNullOrEmpty(ConfigTabelName))
                return;

            // 清除之前的高亮
            ClearTestInterfaceHighlighting();

            if (IsTestRunning)
            {
                // 找到对应的测试界面节点并设置高亮（浅色版本）
                var testInterfaceNode = FindTestInterfaceNode();
                if (testInterfaceNode != null)
                {
                    // 使用浅色高亮，与主测试任务区分
                    testInterfaceNode.Tag = IsTestPaused ? "PausedLight" : "RunningLight";
                    System.Diagnostics.Debug.WriteLine($"[TestInterface] 设置测试界面节点高亮: {testInterfaceNode.Name}, 状态: {testInterfaceNode.Tag}");
                }
            }
        }

        /// <summary>
        /// 调试方法：输出当前绑定状态和变量信息
        /// </summary>
        public void DebugVariableBindings()
        {
            System.Diagnostics.Debug.WriteLine($"[TestInterface.Debug] === 调试信息 ===");
            System.Diagnostics.Debug.WriteLine($"[TestInterface.Debug] ChassisName: {ChassisName}");
            System.Diagnostics.Debug.WriteLine($"[TestInterface.Debug] TestTaskName: {TestTaskName}");
            System.Diagnostics.Debug.WriteLine($"[TestInterface.Debug] ConfigTabelName: {ConfigTabelName}");
            System.Diagnostics.Debug.WriteLine($"[TestInterface.Debug] IsTestRunning: {IsTestRunning}");
            System.Diagnostics.Debug.WriteLine($"[TestInterface.Debug] Controls.Count: {Controls?.Count ?? 0}");

            var hardwareService = Services.HardwareControlService.Instance;
            System.Diagnostics.Debug.WriteLine($"[TestInterface.Debug] HardwareService.IsRunning: {hardwareService.IsRunning}");

            if (_signalValueUpdateService != null)
            {
                System.Diagnostics.Debug.WriteLine($"[TestInterface.Debug] SignalValueUpdateService: 已注入");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[TestInterface.Debug] SignalValueUpdateService: 未注入");
            }

            // 输出所有控件的绑定信息
            if (Controls != null)
            {
                foreach (var control in Controls)
                {
                    if (!string.IsNullOrEmpty(control.BoundVariablePath))
                    {
                        System.Diagnostics.Debug.WriteLine($"[TestInterface.Debug] 控件绑定: {control.Name}({control.ControlType}) -> {control.BoundVariablePath}");

                        // 如果是指示灯，尝试获取当前值
                        if (control.ControlType == "Indicator")
                        {
                            try
                            {
                                double currentValue = hardwareService.GetVariableValue(control.BoundVariablePath);
                                System.Diagnostics.Debug.WriteLine($"[TestInterface.Debug] 指示灯当前值: {control.BoundVariablePath} = {currentValue}");
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"[TestInterface.Debug] 获取指示灯值失败: {control.BoundVariablePath}, 错误: {ex.Message}");
                            }
                        }
                    }
                }
            }

            System.Diagnostics.Debug.WriteLine($"[TestInterface.Debug] === 调试信息结束 ===");
        }

        /// <summary>
        /// 清除测试界面节点的高亮显示
        /// </summary>
        private void ClearTestInterfaceHighlighting()
        {
            var testInterfaceNode = FindTestInterfaceNode();
            if (testInterfaceNode != null && testInterfaceNode.Tag != null)
            {
                testInterfaceNode.Tag = null;
                System.Diagnostics.Debug.WriteLine($"[TestInterface] 清除测试界面节点高亮: {testInterfaceNode.Name}");
            }
        }

        /// <summary>
        /// 查找对应的测试界面节点
        /// </summary>
        private ProjectItem FindTestInterfaceNode()
        {
            if (ProjectData?.Children == null) return null;

            // 遍历项目树找到对应的测试界面节点
            foreach (var chassisNode in ProjectData.Children)
            {
                if (chassisNode.Type == "PXIChassis" && chassisNode.Name == ChassisName)
                {
                    if (chassisNode.Children != null)
                    {
                        foreach (var taskNode in chassisNode.Children)
                        {
                            if (taskNode.Type == "test_task" && taskNode.Name == TestTaskName)
                            {
                                if (taskNode.Children != null)
                                {
                                    foreach (var configNode in taskNode.Children)
                                    {
                                        if (configNode.Type == "task_config")
                                        {
                                            if (configNode.Children != null)
                                            {
                                                foreach (var subNode in configNode.Children)
                                                {
                                                    if (subNode.Type == "test_interface" && subNode.Name == ConfigTabelName)
                                                    {
                                                        return subNode;
                                                    }
                                                }
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
        /// 执行设备特定的输出函数（在测试开始时调用）
        /// </summary>
        private async void ExecuteDeviceSpecificOutputFunctions()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[TestInterface] 开始执行设备特定输出函数: {ChassisName}/{TestTaskName}");

                // 获取当前测试任务相关的所有设备
                var devices = GetTestTaskDevices();
                if (devices == null || devices.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[TestInterface] 未找到相关的设备，跳过输出函数执行");
                    return;
                }

                // 为每个设备执行特定的输出函数
                foreach (var device in devices)
                {
                    await ExecuteDeviceOutputFunction(device);
                }

                System.Diagnostics.Debug.WriteLine($"[TestInterface] 设备特定输出函数执行完成");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TestInterface] 执行设备特定输出函数失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取当前测试任务相关的设备列表
        /// </summary>
        private List<DeviceBase> GetTestTaskDevices()
        {
            if (string.IsNullOrEmpty(ChassisName) || string.IsNullOrEmpty(TestTaskName))
                return null;

            var chassis = _pxiChassisService.GetChassisByName(ChassisName);
            if (chassis == null)
                return null;

            // 获取该机箱下所有设备（暂时返回所有设备，后续可以根据测试任务配置过滤）
            var devices = new List<DeviceBase>();
            foreach (var device in chassis.Devices)
            {
                devices.Add(device);
            }

            return devices;
        }

        /// <summary>
        /// 为指定设备执行特定的输出函数
        /// </summary>
        private async Task ExecuteDeviceOutputFunction(DeviceBase device)
        {
            if (device == null)
                return;

            try
            {
                // 根据设备名称或型号直接识别设备类型并执行对应逻辑
                string deviceName = device.Name?.ToLower() ?? "";
                string deviceModel = device.Model?.ToLower() ?? "";

                // Art9774 模拟量采集卡
                if (deviceModel.Contains("art9774") || deviceName.Contains("art9774"))
                {
                    await ExecuteArt9774OutputFunction(device);
                }
                // JY7131 数字I/O卡
                else if (deviceModel.Contains("jy7131") || deviceName.Contains("jy7131"))
                {
                    await ExecuteJY7131OutputFunction(device);
                }
                // MTX532 模拟量输出卡
                else if (deviceModel.Contains("mtx532") || deviceName.Contains("mtx532"))
                {
                    await ExecuteMTX532OutputFunction(device);
                }
                // MTX970 LVDS通信卡
                else if (deviceModel.Contains("mtx970") || deviceName.Contains("mtx970"))
                {
                    await ExecuteMTX970OutputFunction(device);
                }
                // HZ1394B 1394B通信卡
                else if (deviceModel.Contains("hz1394b") || deviceName.Contains("hz1394b"))
                {
                    await ExecuteHZ1394BOutputFunction(device);
                }
                // ArtSwitch 网络切换系统
                else if (deviceModel.Contains("artswitch") || deviceName.Contains("artswitch"))
                {
                    await ExecuteArtSwitchOutputFunction(device);
                }
                // 其他设备
                else
                {
                    await ExecuteGenericDeviceOutputFunction(device);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TestInterface] 执行设备 {device.Name} 输出函数失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 执行Art9774模拟量采集卡的输出函数
        /// </summary>
        private async Task ExecuteArt9774OutputFunction(DeviceBase device)
        {
            System.Diagnostics.Debug.WriteLine($"[TestInterface] 执行Art9774 {device.Name} 的输出函数");

            // TODO: 实现Art9774设备的测试开始时的输出逻辑
            // 例如：配置采集参数、设置触发条件、启动预热等

            await Task.Delay(10); // 模拟异步操作
        }

        /// <summary>
        /// 执行JY7131数字I/O卡的输出函数
        /// </summary>
        private async Task ExecuteJY7131OutputFunction(DeviceBase device)
        {
            System.Diagnostics.Debug.WriteLine($"[TestInterface] 执行JY7131 {device.Name} 的输出函数");

            // TODO: 实现JY7131设备的测试开始时的输出逻辑
            // 例如：配置I/O方向、设置初始状态、启动I/O监控等

            await Task.Delay(10); // 模拟异步操作
        }

        /// <summary>
        /// 执行MTX532模拟量输出卡的输出函数
        /// </summary>
        private async Task ExecuteMTX532OutputFunction(DeviceBase device)
        {
            System.Diagnostics.Debug.WriteLine($"[TestInterface] 执行MTX532 {device.Name} 的输出函数");

            // TODO: 实现MTX532设备的测试开始时的输出逻辑
            // 例如：设置默认输出值、启动信号发生器、配置输出模式等

            await Task.Delay(10); // 模拟异步操作
        }

        /// <summary>
        /// 执行MTX970 LVDS通信卡的输出函数
        /// </summary>
        private async Task ExecuteMTX970OutputFunction(DeviceBase device)
        {
            System.Diagnostics.Debug.WriteLine($"[TestInterface] 执行MTX970 {device.Name} 的输出函数");

            // TODO: 实现MTX970设备的测试开始时的输出逻辑
            // 例如：建立通信连接、发送初始化命令、配置通信参数等

            await Task.Delay(10); // 模拟异步操作
        }

        /// <summary>
        /// 执行HZ1394B 1394B通信卡的输出函数
        /// </summary>
        private async Task ExecuteHZ1394BOutputFunction(DeviceBase device)
        {
            System.Diagnostics.Debug.WriteLine($"[TestInterface] 执行HZ1394B {device.Name} 的输出函数");

            // TODO: 实现HZ1394B设备的测试开始时的输出逻辑
            // 例如：建立通信连接、发送初始化命令、配置通信参数等

            await Task.Delay(10); // 模拟异步操作
        }

        /// <summary>
        /// 执行ArtSwitch网络切换系统的输出函数
        /// </summary>
        private async Task ExecuteArtSwitchOutputFunction(DeviceBase device)
        {
            System.Diagnostics.Debug.WriteLine($"[TestInterface] 执行ArtSwitch {device.Name} 的输出函数");

            // TODO: 实现ArtSwitch设备的测试开始时的输出逻辑
            // 例如：配置网络参数、建立连接、设置切换逻辑等

            await Task.Delay(10); // 模拟异步操作
        }

        /// <summary>
        /// 执行通用设备的输出函数
        /// </summary>
        private async Task ExecuteGenericDeviceOutputFunction(DeviceBase device)
        {
            System.Diagnostics.Debug.WriteLine($"[TestInterface] 执行通用设备 {device.Name} 的输出函数");

            // TODO: 实现通用设备的测试开始时的输出逻辑
            // 对于未识别的设备类型，提供基本的初始化功能

            await Task.Delay(10); // 模拟异步操作
        }

        #endregion

        #region Command Handlers

        private void OnFloatWindow()
        {
            ReMessageBox.Show("浮动功能需要在View中实现");
        }

        private void OnMinimizeInRegion()
        {
            ReMessageBox.Show("最小化功能待实现");
        }

        private void OnCloseInRegion()
        {
            var result = ReMessageBox.Show("确定要关闭当前测试界面吗？", "确认", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
            if (result == System.Windows.MessageBoxResult.Yes)
            {
                // 构建完整的pageKey: TestInterface_任务名-界面名
                string pageKey = $"TestInterface_{TestTaskName}-{ConfigTabelName}";
                
                // 传递完整的pageKey，这样MainWindowViewModel可以正确识别和关闭该页面
                _eventAggregator.GetEvent<Events.ReleaseCurrentPageEvent>().Publish(pageKey);
            }
        }

        /// <summary>
        /// 开始/暂停/继续 测试界面测试
        /// </summary>
        private void OnStartPauseTest()
        {
            System.Diagnostics.Debug.WriteLine($"[TestInterface.OnStartPauseTest] 开始执行，当前状态: IsTestRunning={IsTestRunning}, IsTestPaused={IsTestPaused}");

            // 检查硬件服务状态
            var hardwareService = Services.HardwareControlService.Instance;
            System.Diagnostics.Debug.WriteLine($"[TestInterface.OnStartPauseTest] 硬件服务状态: IsRunning={hardwareService.IsRunning}");

            // TestInterface现在只处理UI状态，不再直接控制硬件
            // 硬件控制由MainWindowViewModel统一管理
            // TODO: 发布停止采集/输出的指令

            if (!IsTestRunning)
            {
                // 检查是否存在未绑定数据源的控件（排除不需要数据源的控件类型：TextLabel）
                var unboundControls = Controls
                    .Where(c => c.ControlType != "TextLabel" && string.IsNullOrEmpty(c.BoundVariablePath))
                    .ToList();

                if (unboundControls.Any())
                {
                    var controlNames = string.Join("、", unboundControls.Select(c =>
                        !string.IsNullOrEmpty(c.Name) ? c.Name : $"{c.ControlType}({c.Id.Substring(0, 8)})"));
                    ReMessageBox.Show(
                        $"存在未绑定数据源，无法开始测试",
                        "无法启动测试",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Warning);
                    return;
                }

                // 开始测试 - 设置UI状态
                ExecuteDeviceSpecificOutputFunctions();

                IsTestRunning = true;
                IsTestPaused = false;
                System.Diagnostics.Debug.WriteLine($"[TestInterface] 测试界面状态已启动: {DisplayPath}");

                // 初始化变量绑定（用于UI显示）
                SetupVariableBindings();

                // 输出调试信息
                DebugVariableBindings();

                // 设置信号值更新服务的上下文（如果MainWindow没有设置的话）
                if (_signalValueUpdateService != null)
                {
                    // 尝试获取机箱和测试任务信息
                    var chassisName = ChassisName;
                    var testTaskName = TestTaskName;

                    if (!string.IsNullOrEmpty(chassisName) && !string.IsNullOrEmpty(testTaskName))
                    {
                        var chassis = _pxiChassisService?.GetChassisByName(chassisName);
                        if (chassis != null)
                        {
                            // 创建一个临时的ProjectItem作为测试任务
                            var testTask = new Models.ProjectItem { Name = testTaskName, Type = "test_task" };
                            _signalValueUpdateService.SetRunningContext(chassis, testTask);
                            System.Diagnostics.Debug.WriteLine($"[TestInterface] 已设置信号值更新上下文: Chassis={chassisName}, TestTask={testTaskName}");
                        }
                    }
                }

                // 检查硬件服务是否运行
                if (!hardwareService.IsRunning)
                {
                    System.Diagnostics.Debug.WriteLine("[TestInterface] 硬件服务未运行，无法启动测试界面");
                    ReMessageBox.Show("硬件服务未运行，请先通过主窗口启动测试", "无法启动", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    return;
                }

                // 更新测试界面节点高亮
                UpdateTestInterfaceHighlighting();

                // 通知 View 层更新UI状态
                _eventAggregator?.GetEvent<TestRunningStateChangedEvent>()?.Publish(true);

                System.Diagnostics.Debug.WriteLine($"[TestInterface.OnStartPauseTest] 测试界面启动完成");
            }
            else if (!IsTestPaused)
            {
                // 暂停测试 - 只设置UI状态
                // TODO: 发布暂停采集/输出的指令
                IsTestPaused = true;
                System.Diagnostics.Debug.WriteLine($"[TestInterface] 测试界面状态已暂停: {DisplayPath}");

                // 通知 View 层暂停UI更新
                _eventAggregator?.GetEvent<TestRunningStateChangedEvent>()?.Publish(false);
            }
            else
            {
                // 继续测试 - 只设置UI状态
                // TODO: 发布继续采集/输出的指令
                IsTestPaused = false;
                System.Diagnostics.Debug.WriteLine($"[TestInterface] 测试界面状态已恢复: {DisplayPath}");

                // 通知 View 层恢复UI更新
                _eventAggregator?.GetEvent<TestRunningStateChangedEvent>()?.Publish(true);
            }
        }

        /// <summary>
        /// 停止测试界面测试（公共方法）
        /// </summary>
        public void StopTest()
        {
            OnStopTest();
        }

        /// <summary>
        /// 停止测试界面测试
        /// </summary>
        private void OnStopTest()
        {
            System.Diagnostics.Debug.WriteLine($"[TestInterface] 停止测试界面: {DisplayPath}");

            // TestInterface现在只处理UI状态，不再控制硬件
            // 硬件控制由MainWindowViewModel统一管理

            IsTestRunning = false;
            IsTestPaused = false;

            // 清除测试界面节点高亮
            ClearTestInterfaceHighlighting();

            // 通知 View 层停止 UI 轮询
            _eventAggregator?.GetEvent<TestRunningStateChangedEvent>()?.Publish(false);

            // 重置变量表的实时值（清除采集数据）
            ResetSignalTabelValues();

            System.Diagnostics.Debug.WriteLine($"[TestInterface] 测试界面状态已重置");
        }
        
        /// <summary>
        /// 重置变量表的实时值和原始值
        /// </summary>
        private void ResetSignalTabelValues()
        {
            var allSignalTabels = SignalConfigTabelViewModel.GetAllSignalTabelItems();
            if (allSignalTabels == null) return;
            
            string tabelKeyPrefix = $"{TestTaskName}/";
            
            foreach (var kvp in allSignalTabels)
            {
                if (!kvp.Key.StartsWith(tabelKeyPrefix)) continue;
                
                foreach (var signal in kvp.Value)
                {
                    signal.RawValue = 0;
                    signal.RealTimeValue = 0;
                }
            }
        }
        
        /// <summary>
        /// 设置变量绑定（根据控件的 BoundVariablePath 建立与硬件通道的映射）
        /// </summary>
        private void SetupVariableBindings()
        {
            var hardwareService = HardwareControlService.Instance;
            var registeredDevices = new HashSet<string>();
            
            // 调试：输出当前 Controls 集合中的所有控件信息
            System.Diagnostics.Debug.WriteLine($"[SetupVariableBindings] Controls.Count={Controls.Count}");
            foreach (var c in Controls)
            {
                System.Diagnostics.Debug.WriteLine($"[SetupVariableBindings] Control: Id={c.Id}, Name={c.Name}, Type={c.ControlType}, BoundVariablePath='{c.BoundVariablePath}'");
            }
            
            foreach (var control in Controls)
            {
                if (string.IsNullOrEmpty(control.BoundVariablePath))
                    continue;
                    
                // 解析变量路径格式: "变量表名:变量名"
                var parts = control.BoundVariablePath.Split(':');
                if (parts.Length != 2)
                    continue;
                    
                string variableTabelName = parts[0];
                string variableName = parts[1];
                
                // 从信号配置表中查找对应的通道信息
                var channelInfo = FindChannelInfoByVariable(variableTabelName, variableName);
                if (channelInfo != null)
                {
                    // 确保驱动已注册
                    if (!registeredDevices.Contains(channelInfo.DeviceId))
                    {
                        RegisterDriverForDevice(channelInfo.DeviceId, hardwareService);
                        registeredDevices.Add(channelInfo.DeviceId);
                    }
                    
                    // 判断是输入还是输出
                    bool isOutput = control.ControlType == "Switch" || 
                                    control.ControlType == "Button" ||
                                    control.ControlType == "InputBox";
                    
                    // 建立变量绑定
                    hardwareService.BindVariable(
                        control.BoundVariablePath,
                        channelInfo.DeviceId,
                        channelInfo.ChannelId,
                        isOutput);
                        
                    System.Diagnostics.Debug.WriteLine($"[TestInterface] 绑定变量: {control.BoundVariablePath} -> {channelInfo.DeviceId}/{channelInfo.ChannelId}");
                }
            }
        }
        
        /// <summary>
        /// 为指定设备注册驱动
        /// </summary>
        private void RegisterDriverForDevice(string deviceId, HardwareControlService hardwareService)
        {
            // 检查是否已经有缓存的驱动
            var cachedDriver = DriverFactory.GetCachedDriver(deviceId);
            if (cachedDriver != null)
            {
                hardwareService.RegisterDriver(deviceId, cachedDriver);
                System.Diagnostics.Debug.WriteLine($"[TestInterface] 使用缓存驱动: {deviceId}");
                return;
            }
            
            // 从 PxiChassisService 获取实时机箱数据（遍历所有机箱及其设备）
            var chassisList = _pxiChassisService.GetAllChassis();
            if (chassisList != null)
            {
                foreach (var chassis in chassisList)
                {
                    if (chassis.Devices == null) continue;
                    
                    var device = chassis.Devices.FirstOrDefault(d => d.Id == deviceId);
                    if (device != null)
                    {
                        // 使用 DriverFactory 创建驱动（使用真实硬件模式，会自动回退到模拟模式）
                        var driver = DriverFactory.CreateDriver(device, useSimulation: false);
                        hardwareService.RegisterDriver(deviceId, driver);
                        System.Diagnostics.Debug.WriteLine($"[TestInterface] 注册驱动: {deviceId}, SlotIndex={GetDeviceSlotIndex(device)}");
                        return;
                    }
                }
            }
            
            // 如果找不到设备，创建一个模拟驱动
            System.Diagnostics.Debug.WriteLine($"[TestInterface] 未找到设备 {deviceId}，将使用模拟模式");
            var simDevice = new DigitalIODevice { Id = deviceId, Name = deviceId };
            var simDriver = DriverFactory.CreateDriver(simDevice, useSimulation: true);
            hardwareService.RegisterDriver(deviceId, simDriver);
        }
        
        /// <summary>
        /// 获取设备的 SlotIndex
        /// </summary>
        private int GetDeviceSlotIndex(DeviceBase device)
        {
            if (device is DigitalIODevice dioDevice)
                return dioDevice.SlotIndex;
            return -1;
        }
        
        /// <summary>
        /// 根据变量表名和变量名查找通道信息
        /// </summary>
        private ChannelInfo FindChannelInfoByVariable(string variableTabelName, string variableName)
        {
            // 从信号配置表中查找
            var allSignalTabels = SignalConfigTabelViewModel.GetAllSignalTabelItems();
            if (allSignalTabels == null) return null;
            
            // 构建完整的 key: "机箱名/测试任务名/变量表名" 或 "测试任务名/变量表名"（向后兼容）
            string tabelKey = null;
            if (!string.IsNullOrEmpty(ChassisName) && !string.IsNullOrEmpty(TestTaskName))
            {
                tabelKey = $"{ChassisName}/{TestTaskName}/{variableTabelName}";
            }
            else if (!string.IsNullOrEmpty(TestTaskName))
            {
                tabelKey = $"{TestTaskName}/{variableTabelName}";
            }
            else
            {
                return null;
            }
            
            if (!allSignalTabels.TryGetValue(tabelKey, out var signals))
                return null;
                
            var signal = signals?.FirstOrDefault(s => s.SignalName == variableName);
            if (signal == null) return null;
            
            // 解析 ActualChannel 格式: "通道配置表1:DI0" 或 "DI0"
            string actualChannel = signal.ActualChannel;
            if (string.IsNullOrEmpty(actualChannel))
                return null;
            
            string channelTabelName = null;
            string channelName = actualChannel;
            
            // 解析格式 "通道配置表1:DI0"
            int colonIndex = actualChannel.IndexOf(':');
            if (colonIndex > 0)
            {
                channelTabelName = actualChannel.Substring(0, colonIndex);
                channelName = actualChannel.Substring(colonIndex + 1);
            }
            
            // 从通道配置表中查找设备信息
            string deviceId = null;
            if (!string.IsNullOrEmpty(channelTabelName))
            {
                var allChannelTabels = ChannelConfigTabelViewModel.GetAllChannelTabelItems();
                if (allChannelTabels != null)
                {
                    // 构建通道配置表的 key: "机箱名/测试任务名/通道表名" 或 "测试任务名/通道表名"（向后兼容）
                    string channelTabelKey = null;
                    if (!string.IsNullOrEmpty(ChassisName) && !string.IsNullOrEmpty(TestTaskName))
                    {
                        channelTabelKey = $"{ChassisName}/{TestTaskName}/{channelTabelName}";
                    }
                    else if (!string.IsNullOrEmpty(TestTaskName))
                    {
                        channelTabelKey = $"{TestTaskName}/{channelTabelName}";
                    }
                    
                    if (channelTabelKey != null && allChannelTabels.TryGetValue(channelTabelKey, out var channels))
                    {
                        var channel = channels?.FirstOrDefault(c => c.ChannelName == channelName || c.AssociatedChannel == channelName);
                        if (channel != null)
                        {
                            // 从 CardName 找到对应的设备 ID（使用通道的 ChassisName 进行精确查找）
                            deviceId = FindDeviceIdByCardName(channel.CardName, channel.ChassisName);
                            System.Diagnostics.Debug.WriteLine($"[FindChannelInfo] 通道 {channelName} -> CardName={channel.CardName}, ChassisName={channel.ChassisName}, DeviceId={deviceId}");
                        }
                    }
                }
            }
            
            // 如果找不到设备 ID，使用默认设备
            if (string.IsNullOrEmpty(deviceId))
            {
                // 尝试找到第一个数字量板卡
                deviceId = FindFirstDigitalIODeviceId();
                System.Diagnostics.Debug.WriteLine($"[FindChannelInfo] 使用默认设备 ID: {deviceId}");
            }
            
            if (string.IsNullOrEmpty(deviceId))
            {
                System.Diagnostics.Debug.WriteLine($"[FindChannelInfo] 无法找到变量 {variableName} 对应的设备");
                return null;
            }
            
            return new ChannelInfo
            {
                DeviceId = deviceId,
                ChannelId = channelName  // 使用通道标识，如 "DI0"
            };
        }
        
        /// <summary>
        /// 根据板卡名称查找设备 ID
        /// </summary>
        /// <param name="cardName">板卡名称</param>
        /// <param name="chassisName">机箱名称（可选，如果提供则只在该机箱中查找）</param>
        private string FindDeviceIdByCardName(string cardName, string chassisName = null)
        {
            System.Diagnostics.Debug.WriteLine($"[FindDeviceIdByCardName] 查找板卡: {cardName}, 机箱: {chassisName ?? "所有机箱"}");
            
            if (string.IsNullOrEmpty(cardName))
            {
                System.Diagnostics.Debug.WriteLine($"[FindDeviceIdByCardName] cardName 为空");
                return null;
            }
            
            // 从 PxiChassisService 获取实时机箱数据
            var allChassisList = _pxiChassisService.GetAllChassis();
            if (allChassisList == null || allChassisList.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine($"[FindDeviceIdByCardName] PxiChassisService 中没有机箱数据");
                return null;
            }
            
            // 如果指定了机箱名称，只在该机箱中查找
            IEnumerable<ChassisModel> chassisList = allChassisList;
            if (!string.IsNullOrEmpty(chassisName))
            {
                chassisList = allChassisList.Where(c => string.Equals(c.Name, chassisName, StringComparison.Ordinal)).ToList();
                System.Diagnostics.Debug.WriteLine($"[FindDeviceIdByCardName] 过滤后找到 {chassisList.Count()} 个匹配的机箱（机箱名称={chassisName}）");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[FindDeviceIdByCardName] 在所有 {chassisList.Count()} 个机箱中查找");
            }
                
            foreach (var chassis in chassisList)
            {
                if (chassis.Devices == null)
                {
                    System.Diagnostics.Debug.WriteLine($"[FindDeviceIdByCardName] 机箱 {chassis.Name} 的 Devices 为空");
                    continue;
                }
                
                System.Diagnostics.Debug.WriteLine($"[FindDeviceIdByCardName] 机箱 {chassis.Name} 有 {chassis.Devices.Count} 个设备");
                
                foreach (var d in chassis.Devices)
                {
                    System.Diagnostics.Debug.WriteLine($"[FindDeviceIdByCardName] 设备: Id={d.Id}, Name={d.Name}, CardName={d.CardName}");
                }
                
                var device = chassis.Devices.FirstOrDefault(d => d.CardName == cardName);
                if (device != null)
                {
                    System.Diagnostics.Debug.WriteLine($"[FindDeviceIdByCardName] 找到设备: {device.Id}");
                    return device.Id;
                }
            }
            
            System.Diagnostics.Debug.WriteLine($"[FindDeviceIdByCardName] 未找到板卡: {cardName}");
            return null;
        }
        
        /// <summary>
        /// 查找第一个数字量板卡的设备 ID
        /// </summary>
        private string FindFirstDigitalIODeviceId()
        {
            // 从 PxiChassisService 获取实时机箱数据
            var chassisList = _pxiChassisService.GetAllChassis();
            if (chassisList == null || chassisList.Count == 0)
                return null;
                
            foreach (var chassis in chassisList)
            {
                if (chassis.Devices == null) continue;
                
                // 查找 PXIe-7131 或其他数字量板卡
                var device = chassis.Devices.FirstOrDefault(d => 
                    d.Model?.Contains("7131") == true || 
                    d.DeviceTypeName?.Contains("离散量") == true ||
                    d.DeviceTypeName?.Contains("数字量") == true);
                if (device != null)
                    return device.Id;
            }
            return null;
        }
        
        /// <summary>
        /// 变量值变化回调
        /// </summary>
        private void OnVariableValueChanged(object sender, VariableValueChangedEventArgs e)
        {
            // 在 UI 线程更新控件值和变量表
            System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
            {
                // 1. 更新绑定到该变量的控件
                var boundControls = Controls.Where(c => c.BoundVariablePath == e.VariablePath);
                foreach (var control in boundControls)
                {
                    control.CurrentValue = e.Value;
                    System.Diagnostics.Debug.WriteLine($"[TestInterface] 更新控件 {control.Name}: {e.VariablePath} = {e.Value}");
                }
                
                // 2. 更新变量表的实时值和原始值
                UpdateSignalTabelValues(e.VariablePath, e.Value);
            });
        }
        
        /// <summary>
        /// 根据变量路径更新控件的单位
        /// </summary>
        private void UpdateUnitFromVariablePath(TestInterfaceControlItem control)
        {
            if (string.IsNullOrEmpty(control.BoundVariablePath))
            {
                control.Unit = "";
                return;
            }

            // 解析变量路径格式: "变量表名:变量名"
            var parts = control.BoundVariablePath.Split(':');
            if (parts.Length != 2)
            {
                control.Unit = "";
                return;
            }

            string variableTabelName = parts[0];
            string variableName = parts[1];

            // 构建完整的 key: "机箱名/测试任务名/变量表名" 或 "测试任务名/变量表名"（向后兼容）
            string tabelKey = null;
            if (!string.IsNullOrEmpty(ChassisName) && !string.IsNullOrEmpty(TestTaskName))
            {
                tabelKey = $"{ChassisName}/{TestTaskName}/{variableTabelName}";
            }
            else if (!string.IsNullOrEmpty(TestTaskName))
            {
                tabelKey = $"{TestTaskName}/{variableTabelName}";
            }
            else
            {
                control.Unit = "";
                return;
            }

            // 从信号配置表中查找单位
            var allSignalTabels = SignalConfigTabelViewModel.GetAllSignalTabelItems();
            if (allSignalTabels != null && allSignalTabels.TryGetValue(tabelKey, out var signals))
            {
                var signal = signals?.FirstOrDefault(s => s.SignalName == variableName);
                if (signal != null && signal.SignalType == "模拟量")
                {
                    control.Unit = signal.RealTimeValueUnit ?? "";
                }
                else
                {
                    control.Unit = "";
                }
            }
            else
            {
                control.Unit = "";
            }
        }

        /// <summary>
        /// 更新变量表的实时值和原始值（输入通道采集结果）
        /// </summary>
        private void UpdateSignalTabelValues(string variablePath, double value)
        {
            // 解析变量路径格式: "变量表名:变量名"
            var parts = variablePath.Split(':');
            if (parts.Length != 2) return;
            
            string variableTabelName = parts[0];
            string variableName = parts[1];
            
            // 构建完整的 key: "机箱名/测试任务名/变量表名" 或 "测试任务名/变量表名"（向后兼容）
            string tabelKey = null;
            if (!string.IsNullOrEmpty(ChassisName) && !string.IsNullOrEmpty(TestTaskName))
            {
                tabelKey = $"{ChassisName}/{TestTaskName}/{variableTabelName}";
            }
            else if (!string.IsNullOrEmpty(TestTaskName))
            {
                tabelKey = $"{TestTaskName}/{variableTabelName}";
            }
            else
            {
                return;
            }
            
            // 直接更新静态字典中的原始对象（而非克隆对象）
            bool updated = SignalConfigTabelViewModel.UpdateSignalValue(tabelKey, variableName, value);
            
            if (updated)
            {
                System.Diagnostics.Debug.WriteLine($"[TestInterface] 更新变量表 {variablePath}: 原始值={value}");
            }
        }
        
        /// <summary>
        /// 通道信息
        /// </summary>
        private class ChannelInfo
        {
            public string DeviceId { get; set; }
            public string ChannelId { get; set; }
        }

        #endregion
    }
}
