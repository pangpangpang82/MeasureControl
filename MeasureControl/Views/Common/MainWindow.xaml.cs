using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using MeasureControl.Models;
using MeasureControl.ViewModels;
using MeasureControl.Services;
using MeasureControl.Helpers;
using MeasureControl.Events;
using Prism.Ioc;
using Prism.Events;
using MeasureControl.ViewModels.Common;
using System.Windows.Navigation;
using System.Windows.Shapes;
using MeasureControl.Models.Devices;
using MeasureControl.ViewModels.SingleBoardTest;
using MeasureControl.ViewModels.SingleBoardTest.HydraulicController;
using MeasureControl.ViewModels.SingleBoardTest.FuelController;
using MeasureControl.Services.HardwareApis;
using MeasureControl.Views.Dialogs;
using MeasureControl.ViewModels.Dialogs;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;

namespace MeasureControl.Views.Common
{
    /// <summary>
    /// 主窗口
    /// </summary>
    public partial class MainWindow : Window
    {
        #region Private Fields

        private readonly MainWindowViewModel _viewModel;
        private readonly IEventAggregator _eventAggregator;

        private CancellationTokenSource _singleBoardAutoTestCts;
        private ProjectItem _activeTestProjectItem;
        private string _singleBoardAutoTestReportPath;
        private string _singleBoardAutoTestExcelReportPath;
        private HashSet<string> _selectedSingleBoardAutoTestItems;
        private Dictionary<string, string> _singleBoardAutoStepResults;
        private string[] _inertControlPowerImpedanceValues;
        private string[] _inertControlPowerImpedanceResults;
        private string _inertControlPowerImpedanceOverallResult;
        private bool _inertControlPowerImpedanceSelected;
        private bool _inertControlPowerImpedanceExecuted;
        private string[] _inertControlSecondaryTertiaryValues;
        private string[] _inertControlSecondaryTertiaryResults;
        private string _inertControlSecondaryTertiaryOverallResult;
        private bool _inertControlSecondaryTertiarySelected;
        private bool _inertControlSecondaryTertiaryExecuted;
        private string[] _inertControlDiscreteInputPrimaryValues;
        private string[] _inertControlDiscreteInputPrimaryResults;
        private string[] _inertControlDiscreteInputSecondaryValues;
        private string[] _inertControlDiscreteInputSecondaryResults;
        private string _inertControlDiscreteInputOverallResult;
        private bool _inertControlDiscreteInputSelected;
        private bool _inertControlDiscreteInputExecuted;
        private string[] _inertControlDiscreteOutputHighValues;
        private string[] _inertControlDiscreteOutputHighResults;
        private string[] _inertControlDiscreteOutputLowValues;
        private string[] _inertControlDiscreteOutputLowResults;
        private string _inertControlDiscreteOutputOverallResult;
        private bool _inertControlDiscreteOutputSelected;
        private bool _inertControlDiscreteOutputExecuted;
        private string[] _inertControlTempSensorValues;
        private string[] _inertControlTempSensorResults;
        private string _inertControlTempSensorOverallResult;
        private bool _inertControlTempSensorSelected;
        private bool _inertControlTempSensorExecuted;
        private string[] _inertControlPressureSensorValues;
        private string[] _inertControlPressureSensorResults;
        private string _inertControlPressureSensorOverallResult;
        private bool _inertControlPressureSensorSelected;
        private bool _inertControlPressureSensorExecuted;
        private string[] _inertControlOxygenConcentrationValues;
        private string[] _inertControlOxygenConcentrationResults;
        private string[] _inertControlOxygenPressureValues;
        private string[] _inertControlOxygenPressureResults;
        private string _inertControlOxygenSensorOverallResult;
        private bool _inertControlOxygenSensorSelected;
        private bool _inertControlOxygenSensorExecuted;
        private string[] _inertControlTcvMotorResults;
        private string _inertControlTcvMotorOverallResult;
        private bool _inertControlTcvMotorSelected;
        private bool _inertControlTcvMotorExecuted;
        private string[] _inertSimPowerImpedanceValues;
        private string[] _inertSimPowerImpedanceResults;
        private string _inertSimPowerImpedanceOverallResult;
        private bool _inertSimPowerImpedanceSelected;
        private bool _inertSimPowerImpedanceExecuted;
        private string[] _inertSimSecondaryTertiaryValues;
        private string[] _inertSimSecondaryTertiaryResults;
        private string _inertSimSecondaryTertiaryOverallResult;
        private bool _inertSimSecondaryTertiarySelected;
        private bool _inertSimSecondaryTertiaryExecuted;
        private string[] _inertSimOverTempValues;
        private string[] _inertSimOverTempResults;
        private string _inertSimOverTempOverallResult;
        private bool _inertSimOverTempSelected;
        private bool _inertSimOverTempExecuted;
        private string[] _inertSimLatchValues;
        private string[] _inertSimLatchResults;
        private string _inertSimLatchOverallResult;
        private bool _inertSimLatchSelected;
        private bool _inertSimLatchExecuted;
        private HC_6_1ViewModel _hydraulicAutoTestVm61;
        private HC_6_2ViewModel _hydraulicAutoTestVm62ChannelId;
        private HC_6_3ViewModel _hydraulicAutoTestVm62;
        private HC_6_4ViewModel _hydraulicAutoTestVm63;
        private HC_6_5ViewModel _hydraulicAutoTestVm64;
        private HC_6_6ViewModel _hydraulicAutoTestVm65;
        private HC_6_7ViewModel _hydraulicAutoTestVm66;
        private HC_6_8ViewModel _hydraulicAutoTestVm67;
        private HC_6_9ViewModel _hydraulicAutoTestVm68;
        private HC_6_10ViewModel _hydraulicAutoTestVm69;

        private PowerImpedanceTestViewModel _fuelAutoTestVm1;
        private SecondaryPowerTestViewModel _fuelAutoTestVm2;
        private LowVoltageAlarmTestViewModel _fuelAutoTestVm3;
        private TemperatureAcquisitionTestViewModel _fuelAutoTestVm4;
        private DiscreteInputTestViewModel _fuelAutoTestVm5;
        private DiscreteOutputTestViewModel _fuelAutoTestVm6;
        private RS422CommunicationFunctionTestViewModel _fuelAutoTestVm7;
        private RS422SelfCheckTestViewModel _fuelAutoTestVm8;
        private FuelRoundSnapshot _fuelSnapshot17V;
        private FuelRoundSnapshot _fuelSnapshot28V;
        private FuelRoundSnapshot _fuelSnapshot322V;

        private sealed class FuelRoundSnapshot
        {
            public double Voltage;
            public bool Aborted;
            public double? Vm1_ImpA, Vm1_ImpB, Vm1_ImpC, Vm1_ImpD;
            public string Vm1_ResA, Vm1_ResB, Vm1_ResC, Vm1_ResD, Vm1_Overall;
            public double? Vm2_Voltage;
            public string Vm2_TestResult, Vm2_Overall;
            public double? Vm3_FlipVoltage;
            public string Vm3_TestResult, Vm3_Overall;
            public double? Vm4_Temp;
            public string Vm4_TestResult, Vm4_Overall;
            public string Vm5_B0Gnd, Vm5_B1Gnd, Vm5_B0Open, Vm5_B1Open;
            public string Vm5_GndResult, Vm5_OpenResult, Vm5_Overall;
            public double? Vm6_J6, Vm6_J7, Vm6_J8, Vm6_J9, Vm6_J10, Vm6_J11, Vm6_J12, Vm6_J13;
            public double? Vm6_OJ6, Vm6_OJ7, Vm6_OJ8, Vm6_OJ9, Vm6_OJ10, Vm6_OJ11, Vm6_OJ12, Vm6_OJ13;
            public double? Vm6_J14V;
            public string Vm6_StepA, Vm6_StepB, Vm6_StepC, Vm6_Overall;
            public string Vm7_ARx, Vm7_BRx, Vm7_CRx, Vm7_DRx;
            public string Vm7_StepA, Vm7_StepB, Vm7_StepC, Vm7_StepD, Vm7_Overall;
            public string Vm8_ARx;
            public string Vm8_StepA, Vm8_StepB, Vm8_Overall;
        }

        private MeasureControl.ViewModels.SingleBoardTest.InertController.PowerImpedanceTestViewModel _inertSimulationAutoTestVm1;
        private MeasureControl.ViewModels.SingleBoardTest.InertController.SecondaryTertiaryPowerTestViewModel _inertSimulationAutoTestVm2;
        private MeasureControl.ViewModels.SingleBoardTest.InertController.OverTemperatureCutoffTestViewModel _inertSimulationAutoTestVm3;
        private MeasureControl.ViewModels.SingleBoardTest.InertController.LatchModuleCircuitTestViewModel _inertSimulationAutoTestVm4;
        private MeasureControl.ViewModels.SingleBoardTest.InertController.ControlBoardPowerImpedanceTestViewModel _inertControlAutoTestVm1;
        private MeasureControl.ViewModels.SingleBoardTest.InertController.ControlBoardSecondaryTertiaryPowerTestViewModel _inertControlAutoTestVm2;
        private MeasureControl.ViewModels.SingleBoardTest.InertController.ControlBoardDiscreteInputModuleTestViewModel _inertControlAutoTestVm3;
        private MeasureControl.ViewModels.SingleBoardTest.InertController.DiscreteOutputModuleTestViewModel _inertControlAutoTestVm4;
        private MeasureControl.ViewModels.SingleBoardTest.InertController.TemperatureSensorSignalAcquisitionTestViewModel _inertControlAutoTestVm5;
        private MeasureControl.ViewModels.SingleBoardTest.InertController.PressureSensorSignalAcquisitionTestViewModel _inertControlAutoTestVm6;
        private MeasureControl.ViewModels.SingleBoardTest.InertController.OxygenSensorSignalAcquisitionTestViewModel _inertControlAutoTestVm7;
        private MeasureControl.ViewModels.SingleBoardTest.InertController.TcvMotorDriveTestViewModel _inertControlAutoTestVm8;

        #endregion

        /// <summary>
        /// 检查目标节点是否属于根节点的子树（包含根节点本身）
        /// </summary>
        private static bool IsInProjectSubtree(ProjectItem root, ProjectItem target)
        {
            if (root == null || target == null) return false;
            if (root == target) return true;
            if (root.Children == null) return false;
            foreach (var child in root.Children)
                if (IsInProjectSubtree(child, target)) return true;
            return false;
        }

        private static bool IsSingleBoardTestTaskNode(ProjectItem projectItem)
        {
            if (projectItem == null)
            {
                return false;
            }

            var v = (projectItem.Tag ?? projectItem.Name)?.Trim();
            return string.Equals(v, "空气控制板", StringComparison.OrdinalIgnoreCase)
                || string.Equals(v, "空气功率板", StringComparison.OrdinalIgnoreCase)
                || string.Equals(v, "空气安全板", StringComparison.OrdinalIgnoreCase)
                || string.Equals(v, "液压单板", StringComparison.OrdinalIgnoreCase)
                || string.Equals(v, "惰化单板", StringComparison.OrdinalIgnoreCase)
                || string.Equals(v, "惰化模拟板", StringComparison.OrdinalIgnoreCase)
                || string.Equals(v, "惰化控制板", StringComparison.OrdinalIgnoreCase)
                || string.Equals(v, "加放油单板", StringComparison.OrdinalIgnoreCase);
        }

        #region Constructor

        public MainWindow(MainWindowViewModel viewModel, IEventAggregator eventAggregator)
        {
            InitializeComponent();
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
            DataContext = _viewModel;

            // 在窗口加载完成后导航到HomePage
            Loaded += OnMainWindowLoaded;
            Closing += OnMainWindowClosing;
            Closed += OnMainWindowClosed;
            _viewModel.PropertyChanged += ViewModelOnPropertyChanged;
            ProjectTreeView.Loaded += ProjectTreeView_OnLoaded;

            // 订阅测试任务创建事件，用于展开项目树到新节点
            _eventAggregator.GetEvent<TestTaskCreatedEvent>().Subscribe(OnTestTaskCreated);

            // 订阅选中项目树节点事件
            _eventAggregator.GetEvent<SelectProjectItemEvent>().Subscribe(OnSelectProjectItem);

            // ========== 调试日志：添加窗口焦点事件监听 ==========
            // 监听窗口激活事件
            Activated += OnMainWindowActivated;
            // 监听窗口失去激活事件
            Deactivated += OnMainWindowDeactivated;
            // 监听获得焦点事件
            GotFocus += OnMainWindowGotFocus;
            // 监听失去焦点事件
            LostFocus += OnMainWindowLostFocus;
        }

        /// <summary>
        /// 主窗口加载完成事件处理
        /// </summary>
        private void OnMainWindowLoaded(object sender, RoutedEventArgs e)
        {
            // 确保MainRegion完全初始化后再导航
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _viewModel?.NavigateToHomePageOnStartup();
            }), DispatcherPriority.Loaded);
        }

        /// <summary>
        /// 主窗口被激活事件处理（调试用）
        /// </summary>
        private void OnMainWindowActivated(object sender, EventArgs e)
        {
        }

        /// <summary>
        /// 主窗口失去激活事件处理（调试用）
        /// </summary>
        private void OnMainWindowDeactivated(object sender, EventArgs e)
        {
        }

        /// <summary>
        /// 主窗口获得焦点事件处理（调试用）
        /// </summary>
        private void OnMainWindowGotFocus(object sender, RoutedEventArgs e)
        {
            var focusedElement = FocusManager.GetFocusedElement(this);
            var focusedElementName = focusedElement?.GetType().Name ?? "null";
        }

        /// <summary>
        /// 主窗口失去焦点事件处理（调试用）
        /// </summary>
        private void OnMainWindowLostFocus(object sender, RoutedEventArgs e)
        {
        }

        private void ProjectTreeView_OnLoaded(object sender, RoutedEventArgs e)
        {
            ProjectTreeView?.ExpandAll();
        }

        private void ViewModelOnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainWindowViewModel.CurrentProject))
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    ProjectTreeView?.ExpandAll();
                }), DispatcherPriority.Loaded);
            }
        }

        private void OnMainWindowClosed(object sender, EventArgs e)
        {
            if (_viewModel != null)
            {
                _viewModel.PropertyChanged -= ViewModelOnPropertyChanged;
            }
            Closing -= OnMainWindowClosing;
            ProjectTreeView.Loaded -= ProjectTreeView_OnLoaded;

            // 取消订阅事件
            if (_eventAggregator != null)
            {
                _eventAggregator.GetEvent<TestTaskCreatedEvent>().Unsubscribe(OnTestTaskCreated);
                _eventAggregator.GetEvent<SelectProjectItemEvent>().Unsubscribe(OnSelectProjectItem);
            }
        }

        private void OnMainWindowClosing(object sender, CancelEventArgs e)
        {
            try
            {
                if (MainContentContainer?.Content is FrameworkElement element)
                {
                    if (element.DataContext is ICloseGuard guard && !guard.CanClose())
                    {
                        e.Cancel = true;
                    }
                }
            }
            catch
            {
                e.Cancel = true;
            }
        }

        /// <summary>
        /// 处理测试任务创建事件，展开项目树到新节点
        /// </summary>
        private void OnTestTaskCreated(ProjectItem newTestTask)
        {
            if (newTestTask == null || ProjectTreeView == null) return;

            // 延迟执行，确保UI已更新
            Dispatcher.BeginInvoke(new Action(() =>
            {
                // 展开所有节点
                ProjectTreeView.ExpandAll();

                // 查找并展开到新创建的测试任务节点
                var treeViewItem = FindTreeViewItem(ProjectTreeView, newTestTask);
                if (treeViewItem != null)
                {
                    // 展开父节点
                    var parent = FindParent<TreeViewItem>(treeViewItem);
                    while (parent != null)
                    {
                        parent.IsExpanded = true;
                        parent = FindParent<TreeViewItem>(parent);
                    }

                    // 滚动到新节点
                    treeViewItem.BringIntoView();
                }
            }), DispatcherPriority.Loaded);
        }

        /// <summary>
        /// 处理选中项目树节点事件
        /// </summary>
        private void OnSelectProjectItem(SelectProjectItemEventArgs args)
        {
            if (args == null || ProjectTreeView == null || _viewModel?.CurrentProject == null) return;

            // 延迟执行，确保UI已更新
            Dispatcher.BeginInvoke(new Action(() =>
            {
                // 展开所有节点
                ProjectTreeView.ExpandAll();

                // 查找对应的配置表节点
                ProjectItem targetItem = null;
                if (_viewModel.CurrentProject != null && _viewModel.CurrentProject.Count > 0)
                {
                    var rootNode = _viewModel.CurrentProject[0];
                    if (rootNode?.Children != null)
                    {
                        // 在所有机箱节点下查找
                        foreach (var chassisNode in rootNode.Children)
                        {
                            if (chassisNode.Type == "PXIChassis" && chassisNode.Children != null)
                            {
                                var taskConfigNode = chassisNode.Children.FirstOrDefault(p => p.Type == "task_config");
                                if (taskConfigNode?.Children != null)
                                {
                                    foreach (var testTask in taskConfigNode.Children)
                                    {
                                        if (testTask.Type == "test_task" && testTask.Name == args.TestTaskName && testTask.Children != null)
                                        {
                                            foreach (var configNode in testTask.Children)
                                            {
                                                if (configNode.Children != null)
                                                {
                                                    foreach (var configTabel in configNode.Children)
                                                    {
                                                        if (configTabel.Name == args.ConfigTabelName && configTabel.Type == args.ConfigTabelType)
                                                        {
                                                            targetItem = configTabel;
                                                            break;
                                                        }
                                                    }
                                                }
                                                if (targetItem != null) break;
                                            }
                                        }
                                        if (targetItem != null) break;
                                    }
                                }
                                if (targetItem != null) break;
                            }
                        }
                    }
                }

                if (targetItem != null)
                {
                    // 查找对应的TreeViewItem
                    var treeViewItem = FindTreeViewItem(ProjectTreeView, targetItem);
                    if (treeViewItem != null)
                    {
                        // 展开所有父节点
                        var parent = FindParent<TreeViewItem>(treeViewItem);
                        while (parent != null)
                        {
                            parent.IsExpanded = true;
                            parent = FindParent<TreeViewItem>(parent);
                        }

                        // 选中节点
                        treeViewItem.IsSelected = true;

                        // 滚动到节点
                        treeViewItem.BringIntoView();

                        // 如果设置了触发双击，则触发双击事件
                        if (args.TriggerDoubleClick && _viewModel?.TreeItemDoubleClickCommand?.CanExecute(targetItem) == true)
                        {
                            _viewModel.TreeItemDoubleClickCommand.Execute(targetItem);
                        }
                    }
                }
            }), DispatcherPriority.Loaded);
        }

        /// <summary>
        /// 在TreeView中查找指定项目的TreeViewItem
        /// </summary>
        private TreeViewItem FindTreeViewItem(TreeView treeView, ProjectItem item)
        {
            if (treeView == null || item == null) return null;

            foreach (var treeViewItem in treeView.Items)
            {
                var container = treeView.ItemContainerGenerator.ContainerFromItem(treeViewItem) as TreeViewItem;
                if (container != null)
                {
                    var found = FindTreeViewItemRecursive(container, item);
                    if (found != null) return found;
                }
            }

            return null;
        }

        /// <summary>
        /// 递归查找TreeViewItem
        /// </summary>
        private TreeViewItem FindTreeViewItemRecursive(TreeViewItem parent, ProjectItem item)
        {
            if (parent == null || item == null) return null;

            if (parent.DataContext == item)
            {
                return parent;
            }

            foreach (var child in parent.Items)
            {
                var childContainer = parent.ItemContainerGenerator.ContainerFromItem(child) as TreeViewItem;
                if (childContainer != null)
                {
                    var found = FindTreeViewItemRecursive(childContainer, item);
                    if (found != null) return found;
                }
            }

            return null;
        }

        #endregion

        #region TreeView Event Handlers

        /// <summary>
        /// 处理TreeView项目头部点击事件
        /// </summary>
        private void Header_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is ContentPresenter header)
            {
                var treeViewItem = FindAncestor<TreeViewItem>(header);
                if (treeViewItem?.HasItems == true)
                {
                    ToggleTreeViewItem(treeViewItem);
                    e.Handled = true;
                }
            }
        }

        /*
        /// <summary>
        /// 处理TreeView项目单击事件（用于展开/折叠节点）
        /// 注意：此方法已禁用，因为它会干扰TreeView的默认展开/折叠机制
        /// 现在由Border_MouseLeftButtonUp和Header_MouseLeftButtonUp来处理展开/折叠
        /// </summary>
        private void TreeViewItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is TreeViewItem treeViewItem)
            {
                // 检查是否点击的是当前节点（不是子节点）
                if (e.OriginalSource is DependencyObject originalSource)
                {
                    var clickedTreeViewItem = FindParent<TreeViewItem>(originalSource);
                    if (clickedTreeViewItem != treeViewItem)
                    {
                        return; // 是子节点的点击，不处理
                    }
                }

                // 如果有子项，切换展开/折叠状态
                if (treeViewItem.HasItems)
                {
                    ToggleTreeViewItem(treeViewItem);
                    // 不标记为已处理，让双击事件也能触发
                }
            }
        }
        */

        /// <summary>
        /// 处理TreeView项目边框点击事件
        /// </summary>
        private void Border_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border)
            {
                var treeViewItem = FindAncestor<TreeViewItem>(border);
                if (treeViewItem?.HasItems == true)
                {
                    ToggleTreeViewItem(treeViewItem);
                    e.Handled = true;
                }
            }
        }

        /// <summary>
        /// 切换TreeView项目的展开/折叠状态
        /// </summary>
        private void ToggleTreeViewItem(TreeViewItem treeViewItem)
        {
            if (treeViewItem.IsExpanded)
            {
                CollapseAllChildren(treeViewItem);
            }
            else
            {
                treeViewItem.IsExpanded = true;
            }
            treeViewItem.IsSelected = true;
        }

        /// <summary>
        /// 递归折叠所有子项
        /// </summary>
        private void CollapseAllChildren(TreeViewItem item)
        {
            item.IsExpanded = false;

            foreach (var child in item.Items)
            {
                if (item.ItemContainerGenerator.ContainerFromItem(child) is TreeViewItem childItem && childItem.HasItems)
                {
                    CollapseAllChildren(childItem);
                }
            }
        }

        /// <summary>
        /// 查找指定类型的父元素
        /// </summary>
        private T FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            DependencyObject parentObject = VisualTreeHelper.GetParent(child);

            if (parentObject == null)
                return null;

            if (parentObject is T parent)
                return parent;

            return FindParent<T>(parentObject);
        }

        /// <summary>
        /// 处理TreeView项目双击事件
        /// </summary>
        private void TreeViewItem_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is TreeViewItem treeViewItem && treeViewItem.DataContext is ProjectItem projectItem)
            {
                // 检查是否是子节点冒泡上来的事件
                if (e.OriginalSource is DependencyObject originalSource)
                {
                    var clickedTreeViewItem = FindParent<TreeViewItem>(originalSource);
                    if (clickedTreeViewItem != treeViewItem)
                    {
                        return;
                    }
                }

                if (_viewModel?.TreeItemDoubleClickCommand?.CanExecute(projectItem) == true)
                {
                    _viewModel.TreeItemDoubleClickCommand.Execute(projectItem);
                    // 标记事件为已处理，防止重复触发
                    e.Handled = true;
                }
            }
        }

        /// <summary>
        /// 处理TreeView项目选中事件
        /// </summary>
        private void TreeViewItem_Selected(object sender, RoutedEventArgs e)
        {
            if (sender is TreeViewItem tvi)
            {
                tvi.IsSelected = true;
                e.Handled = true;
            }
        }

        /// <summary>
        /// 处理TreeView项目右键点击事件
        /// </summary>
        private void TreeViewItem_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is TreeViewItem treeViewItem && treeViewItem.DataContext is ProjectItem projectItem)
            {
                // 自动测试期间：屏蔽测试板子树之外的右键菜单
                if (_viewModel?.IsAutoTestRunning == true && _activeTestProjectItem != null
                    && !IsInProjectSubtree(_activeTestProjectItem, projectItem))
                {
                    treeViewItem.ContextMenu = null;
                    e.Handled = true;
                    return;
                }

                if (_viewModel?.IsFixedDemoMode == true)
                {
                    var parentTreeViewItem = FindParent<TreeViewItem>(treeViewItem);
                    var parentProjectItem = parentTreeViewItem?.DataContext as ProjectItem;
                    var isUnderTestTasksFolder = parentProjectItem != null
                        && (string.Equals(parentProjectItem.Type, "test_tasks", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(parentProjectItem.Name, "测试任务", StringComparison.OrdinalIgnoreCase));

                    // Demo 模式下：只放开"测试任务"文件夹下的单板节点右键菜单，其它节点一律禁用
                    if (!(IsSingleBoardTestTaskNode(projectItem) && isUnderTestTasksFolder))
                    {
                        treeViewItem.ContextMenu = null;
                        e.Handled = true;
                        return;
                    }
                }

                // 检查鼠标点击位置是否在当前 TreeViewItem 的范围内（不包括子节点）
                if (e.OriginalSource is DependencyObject originalSource)
                {
                    // 查找原始点击源所属的 TreeViewItem
                    var clickedTreeViewItem = FindParent<TreeViewItem>(originalSource);

                    // 如果点击的不是当前节点，说明是子节点冒泡上来的事件，忽略
                    if (clickedTreeViewItem != treeViewItem)
                    {
                        return;
                    }
                }

                // 选中当前项
                treeViewItem.IsSelected = true;

                // 先清除旧的右键菜单（防止菜单残留）
                treeViewItem.ContextMenu = null;

                var contextMenu = new ContextMenu();

                // 应用自定义样式
                if (this.Resources["CustomContextMenuStyle"] is Style contextMenuStyle)
                {
                    contextMenu.Style = contextMenuStyle;
                }

                // 为PXI机箱节点显示右键菜单
                if (projectItem.Type == "PXIChassis")
                {
                    // 重命名菜单项
                    var renameMenuItem = new MenuItem { Header = "重命名" };

                    // 应用自定义菜单项样式
                    if (this.Resources["CustomMenuItemStyle"] is Style menuItemStyle)
                    {
                        renameMenuItem.Style = menuItemStyle;
                    }

                    renameMenuItem.Click += (s, args) =>
                    {
                        _viewModel?.RenamePxiChassisCommand?.Execute(projectItem.Name);
                    };
                    contextMenu.Items.Add(renameMenuItem);

                    // 删除菜单项
                    var deleteMenuItem = new MenuItem { Header = "删除" };

                    // 应用自定义菜单项样式
                    if (this.Resources["CustomMenuItemStyle"] is Style menuItemStyle2)
                    {
                        deleteMenuItem.Style = menuItemStyle2;
                    }

                    deleteMenuItem.Click += (s, args) =>
                    {
                        _viewModel?.DeletePxiChassisFromTreeCommand?.Execute(projectItem.Name);
                    };
                    contextMenu.Items.Add(deleteMenuItem);
                }
                // 为任务配置节点显示右键菜单
                else if (projectItem.Type == "task_config")
                {
                    // 创建测试任务菜单项
                    var createTestTaskMenuItem = new MenuItem { Header = "创建测试任务" };

                    // 应用自定义菜单项样式
                    if (this.Resources["CustomMenuItemStyle"] is Style menuItemStyle)
                    {
                        createTestTaskMenuItem.Style = menuItemStyle;
                    }

                    createTestTaskMenuItem.Click += (s, args) =>
                    {
                        _viewModel?.CreateTestTaskCommand?.Execute(projectItem);
                    };
                    contextMenu.Items.Add(createTestTaskMenuItem);
                }
                // 为测试任务节点显示右键菜单
                else if (projectItem.Type == "test_task" || IsSingleBoardTestTaskNode(projectItem))
                {
                    // 单板测试任务节点：增加“启动测试”（整板自动测试）
                    // 目前仅液压单板实现整板自动测试，其他单板进入页面后会提示未实现。
                    var boardType = (projectItem.Tag ?? projectItem.Name)?.Trim();
                    if (string.Equals(boardType, "空气控制板", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(boardType, "空气功率板", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(boardType, "空气安全板", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(boardType, "液压单板", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(boardType, "惰化单板", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(boardType, "惰化模拟板", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(boardType, "惰化控制板", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(boardType, "加放油单板", StringComparison.OrdinalIgnoreCase))
                    {
                        var startTestMenuItem = new MenuItem { Header = "启动测试" };
                        if (this.Resources["CustomMenuItemStyle"] is Style startStyle)
                        {
                            startTestMenuItem.Style = startStyle;
                        }

                        startTestMenuItem.Click += (s, args) =>
                        {
                            _ = StartSingleBoardAutoTestAsync(projectItem);
                        };

                        contextMenu.Items.Add(startTestMenuItem);
                    }

                    // 单板节点右键菜单仅显示“启动测试”
                    if (IsSingleBoardTestTaskNode(projectItem))
                    {
                        // 跳过重命名/删除等操作
                    }
                    else
                    {
                        // 重命名菜单项
                        var renameMenuItem = new MenuItem { Header = "重命名" };

                        // 应用自定义菜单项样式
                        if (this.Resources["CustomMenuItemStyle"] is Style menuItemStyle)
                        {
                            renameMenuItem.Style = menuItemStyle;
                        }

                        renameMenuItem.Click += (s, args) =>
                        {
                            _viewModel?.RenameTestTaskCommand?.Execute(projectItem);
                        };
                        contextMenu.Items.Add(renameMenuItem);

                        // 删除菜单项
                        var deleteMenuItem = new MenuItem { Header = "删除" };

                        // 应用自定义菜单项样式
                        if (this.Resources["CustomMenuItemStyle"] is Style menuItemStyle2)
                        {
                            deleteMenuItem.Style = menuItemStyle2;
                        }

                        deleteMenuItem.Click += (s, args) =>
                        {
                            _viewModel?.DeleteTestTaskCommand?.Execute(projectItem);
                        };
                        contextMenu.Items.Add(deleteMenuItem);
                    }
                }
                // 为通道配置节点显示右键菜单
                else if (projectItem.Type == "channel_config")
                {
                    var createMenuItem = new MenuItem { Header = "创建通道配置表" };

                    if (this.Resources["CustomMenuItemStyle"] is Style menuItemStyle)
                    {
                        createMenuItem.Style = menuItemStyle;
                    }

                    createMenuItem.Click += (s, args) =>
                    {
                        _viewModel?.CreateChannelConfigTabelCommand?.Execute(projectItem);
                    };
                    contextMenu.Items.Add(createMenuItem);
                }
                // 为信号配置节点显示右键菜单
                else if (projectItem.Type == "signal_config")
                {
                    // 创建变量表
                    var createVariableMenuItem = new MenuItem { Header = "创建变量表" };

                    if (this.Resources["CustomMenuItemStyle"] is Style menuItemStyle)
                    {
                        createVariableMenuItem.Style = menuItemStyle;
                    }

                    createVariableMenuItem.Click += (s, args) =>
                    {
                        _viewModel?.CreateSignalConfigTabelCommand?.Execute(projectItem);
                    };
                    contextMenu.Items.Add(createVariableMenuItem);

                    // 创建矩阵开关配置表
                    var createMatrixSwitchMenuItem = new MenuItem { Header = "创建矩阵开关配置表" };

                    if (this.Resources["CustomMenuItemStyle"] is Style menuItemStyle2)
                    {
                        createMatrixSwitchMenuItem.Style = menuItemStyle2;
                    }

                    createMatrixSwitchMenuItem.Click += (s, args) =>
                    {
                        _viewModel?.CreateMatrixSwitchConfigTableCommand?.Execute(projectItem);
                    };
                    contextMenu.Items.Add(createMatrixSwitchMenuItem);
                }
                // 为ICD映射节点显示右键菜单
                else if (projectItem.Type == "icd_mapping")
                {
                    var createMappingMenuItem = new MenuItem { Header = "创建ICD映射表" };

                    if (this.Resources["CustomMenuItemStyle"] is Style menuItemStyle)
                    {
                        createMappingMenuItem.Style = menuItemStyle;
                    }

                    createMappingMenuItem.Click += (s, args) =>
                    {
                        _viewModel?.CreateIcdMappingTabelCommand?.Execute(projectItem);
                    };
                    contextMenu.Items.Add(createMappingMenuItem);
                }
                // 为ICD配置节点显示右键菜单
                else if (projectItem.Type == "icd_config")
                {
                    var createIcdMenuItem = new MenuItem { Header = "创建ICD配置表" };

                    if (this.Resources["CustomMenuItemStyle"] is Style menuItemStyle)
                    {
                        createIcdMenuItem.Style = menuItemStyle;
                    }

                    createIcdMenuItem.Click += (s, args) =>
                    {
                        _viewModel?.CreateIcdConfigTabelCommand?.Execute(projectItem);
                    };
                    contextMenu.Items.Add(createIcdMenuItem);
                }
                // 为测试界面节点显示右键菜单
                else if (projectItem.Type == "test_ui")
                {
                    var createMenuItem = new MenuItem { Header = "创建测试界面" };

                    if (this.Resources["CustomMenuItemStyle"] is Style menuItemStyle)
                    {
                        createMenuItem.Style = menuItemStyle;
                    }

                    createMenuItem.Click += (s, args) =>
                    {
                        _viewModel?.CreateTestInterfaceCommand?.Execute(projectItem);
                    };
                    contextMenu.Items.Add(createMenuItem);
                }
                // 为测试序列节点显示右键菜单
                else if (projectItem.Type == "test_sequence")
                {
                    var createMenuItem = new MenuItem { Header = "创建测试序列" };

                    if (this.Resources["CustomMenuItemStyle"] is Style menuItemStyle)
                    {
                        createMenuItem.Style = menuItemStyle;
                    }

                    createMenuItem.Click += (s, args) =>
                    {
                        _viewModel?.CreateTestSequenceCommand?.Execute(projectItem);
                    };
                    contextMenu.Items.Add(createMenuItem);
                }
                // 为报表节点显示右键菜单
                else if (projectItem.Type == "report")
                {
                    var createMenuItem = new MenuItem { Header = "创建报表模板" };

                    if (this.Resources["CustomMenuItemStyle"] is Style menuItemStyle)
                    {
                        createMenuItem.Style = menuItemStyle;
                    }

                    createMenuItem.Click += (s, args) =>
                    {
                        _viewModel?.CreateReportConfigTabelCommand?.Execute(projectItem);
                    };
                    contextMenu.Items.Add(createMenuItem);
                }
                // 为TDM系统节点显示右键菜单（可选，如果需要的话）
                else if (projectItem.Type == "tdm_system")
                {
                    // TDM系统暂时不需要右键菜单，只支持双击导航
                }
                // 为配置表子节点显示右键菜单（通道配置表、非通讯变量表、ICD配置表、测试序列、报表模板、测试界面）
                else if (projectItem.Type == "channel_config_tabel" ||
                         projectItem.Type == "signal_config_tabel" ||
                         //projectItem.Type == "communicating_signal_config_tabel" || 
                         projectItem.Type == "icd_mapping_tabel" ||
                         projectItem.Type == "icd_config_tabel" ||
                         projectItem.Type == "test_sequence_item" ||
                         projectItem.Type == "report_config_tabel" ||
                         projectItem.Type == "test_interface")
                {
                    // 重命名菜单项
                    var renameMenuItem = new MenuItem { Header = "重命名" };

                    if (this.Resources["CustomMenuItemStyle"] is Style menuItemStyle)
                    {
                        renameMenuItem.Style = menuItemStyle;
                    }

                    renameMenuItem.Click += (s, args) =>
                    {
                        _viewModel?.RenameConfigTabelCommand?.Execute(projectItem);
                    };
                    contextMenu.Items.Add(renameMenuItem);

                    // 删除菜单项
                    var deleteMenuItem = new MenuItem { Header = "删除" };

                    if (this.Resources["CustomMenuItemStyle"] is Style menuItemStyle2)
                    {
                        deleteMenuItem.Style = menuItemStyle2;
                    }

                    deleteMenuItem.Click += (s, args) =>
                    {
                        _viewModel?.DeleteConfigTabelCommand?.Execute(projectItem);
                    };
                    contextMenu.Items.Add(deleteMenuItem);
                }

                // 如果有菜单项，显示右键菜单
                if (contextMenu.Items.Count > 0)
                {
                    treeViewItem.ContextMenu = contextMenu;
                    contextMenu.IsOpen = true;
                }
                else
                {
                    // 没有菜单项时，清除右键菜单（防止继承父节点的菜单）
                    treeViewItem.ContextMenu = null;
                }

                e.Handled = true;
            }
        }

        #endregion

        private async Task StartSingleBoardAutoTestAsync(ProjectItem projectItem)
        {
            if (projectItem == null)
            {
                return;
            }

            if (_singleBoardAutoTestCts != null)
            {
                ReMessageBox.Show("已有整板自动测试正在运行", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var boardType = projectItem.Tag;
            var boardName = projectItem.Name;
            if (string.IsNullOrWhiteSpace(boardType))
            {
                boardType = boardName;
            }

            _selectedSingleBoardAutoTestItems = null;
            (string Name, Func<CancellationToken, Task<string>> Run)[] steps;
            if (string.Equals(boardType, "液压单板", StringComparison.OrdinalIgnoreCase))
            {
                var allHydraulicSteps = BuildHydraulicSteps();
                var dialog = new HydraulicAutoTestSelectionDialog
                {
                    Owner = this
                };
                dialog.Initialize(allHydraulicSteps.Select(x => x.Name).ToArray(), new[] { "电源阻抗测试" });
                var confirmed = dialog.ShowDialog();
                if (confirmed != true)
                {
                    return;
                }

                var selectedItems = dialog.SelectedItems ?? Array.Empty<string>();
                if (selectedItems.Length == 0)
                {
                    ReMessageBox.Show("请至少勾选一个测试项", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                _selectedSingleBoardAutoTestItems = new HashSet<string>(selectedItems, StringComparer.OrdinalIgnoreCase);
                steps = allHydraulicSteps.Where(x => _selectedSingleBoardAutoTestItems.Contains(x.Name)).ToArray();
            }
            else if (string.Equals(boardType, "加放油单板", StringComparison.OrdinalIgnoreCase))
            {
                var allFuelSteps = BuildFuelSteps();
                var dialog = new FuelAutoTestSelectionDialog
                {
                    Owner = this
                };
                dialog.Initialize(allFuelSteps.Select(x => x.Name).ToArray(), new[] { "电源阻抗测试" });
                var confirmed = dialog.ShowDialog();
                if (confirmed != true)
                {
                    return;
                }

                var selectedItems = dialog.SelectedItems ?? Array.Empty<string>();
                if (selectedItems.Length == 0)
                {
                    ReMessageBox.Show("请至少勾选一个测试项", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                _selectedSingleBoardAutoTestItems = new HashSet<string>(selectedItems, StringComparer.OrdinalIgnoreCase);
                _activeTestProjectItem = projectItem;
                await RunFuelBoardMultiVoltageTestAsync(boardName, selectedItems).ConfigureAwait(true);
                return;
            }
            else if (string.Equals(boardType, "惰化模拟板", StringComparison.OrdinalIgnoreCase))
            {
                var allInertSimulationSteps = BuildInertSimulationSteps();
                var dialog = new FuelAutoTestSelectionDialog
                {
                    Owner = this,
                    Title = "选择惰化模拟板测试项"
                };
                dialog.Initialize(allInertSimulationSteps.Select(x => x.Name).ToArray());
                var dialogResult = dialog.ShowDialog();
                if (dialogResult != true)
                {
                    return;
                }

                var selectedItems = dialog.SelectedItems ?? Array.Empty<string>();
                if (selectedItems.Length == 0)
                {
                    ReMessageBox.Show("请至少勾选一个测试项", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                _selectedSingleBoardAutoTestItems = new HashSet<string>(selectedItems, StringComparer.OrdinalIgnoreCase);
                steps = allInertSimulationSteps.Where(x => _selectedSingleBoardAutoTestItems.Contains(x.Name)).ToArray();
            }
            else if (string.Equals(boardType, "惰化控制板", StringComparison.OrdinalIgnoreCase))
            {
                var allInertControlSteps = BuildInertControlSteps();
                var dialog = new FuelAutoTestSelectionDialog
                {
                    Owner = this,
                    Title = "选择惰化控制板测试项"
                };
                dialog.Initialize(allInertControlSteps.Select(x => x.Name).ToArray());
                var dialogResult = dialog.ShowDialog();
                if (dialogResult != true)
                {
                    return;
                }

                var selectedItems = dialog.SelectedItems ?? Array.Empty<string>();
                if (selectedItems.Length == 0)
                {
                    ReMessageBox.Show("请至少勾选一个测试项", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                _selectedSingleBoardAutoTestItems = new HashSet<string>(selectedItems, StringComparer.OrdinalIgnoreCase);
                steps = allInertControlSteps.Where(x => _selectedSingleBoardAutoTestItems.Contains(x.Name)).ToArray();
            }
            else
            {
                steps = boardType switch
                {
                    "空气控制板" => BuildAirSteps(),
                    "空气功率板" => BuildAirSteps(),
                    "空气安全板" => BuildAirSteps(),
                    "惰化单板" => BuildInertingSteps(),
                    _ => null
                };
            }

            if (steps == null)
            {
                ReMessageBox.Show($"未知单板类型: {boardType}", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (steps.Length == 0)
            {
                ReMessageBox.Show($"{boardType}整板自动测试未实现", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _activeTestProjectItem = projectItem;
            await RunSingleBoardStepsAsync(boardName, boardType, steps).ConfigureAwait(true);
        }

        private async Task RunSingleBoardStepsAsync(
            string boardName,
            string boardType,
            (string Name, Func<CancellationToken, Task<string>> Run)[] steps)
        {
            _singleBoardAutoTestCts = new CancellationTokenSource();
            var token = _singleBoardAutoTestCts.Token;

            TestProgressDialog dialog = null;
            TestProgressDialogViewModel vm = null;
            EventHandler ownerStateChangedHandler = null;
            EventHandler ownerActivatedHandler = null;
            EventHandler ownerDeactivatedHandler = null;

            var mainVm1 = DataContext as MainWindowViewModel;
            var anyFailed = false;
            var shouldNotifyCompletion = false;
            string completionMessage = null;
            string abortExceptionMessage = null;

            try
            {
                PrepareSingleBoardReport(boardName);
                AppendSingleBoardReportLine($"START | {boardName} | {boardType}");

                // 整板自动测试期间：锁定树导航，内容区保持可交互
                if (mainVm1 != null) mainVm1.IsAutoTestRunning = true;

                // 自动导航到测试单板第一个测试项（确保当前页面是该单板页）
                var firstTestItem1 = _activeTestProjectItem?.Children?.FirstOrDefault();
                if (firstTestItem1 != null && _viewModel?.TreeItemDoubleClickCommand?.CanExecute(firstTestItem1) == true)
                    _viewModel.TreeItemDoubleClickCommand.Execute(firstTestItem1);

                vm = new TestProgressDialogViewModel
                {
                    HeaderText = boardName,
                    StatusText = "准备开始...",
                    Progress = 0,
                    Total = steps.Length,
                    ConfirmStopOnClose = true
                };
                vm.RequestCancel = () =>
                {
                    try { _singleBoardAutoTestCts?.Cancel(); } catch { }
                };

                dialog = new TestProgressDialog
                {
                    DataContext = vm,
                    Owner = this,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Topmost = true
                };

                ownerStateChangedHandler = (_, __) =>
                {
                    if (dialog == null || dialog.Owner == null)
                    {
                        return;
                    }

                    dialog.Topmost = dialog.Owner.WindowState != WindowState.Minimized;
                };
                ownerActivatedHandler = (_, __) =>
                {
                    if (dialog == null || dialog.Owner == null)
                    {
                        return;
                    }

                    if (dialog.Owner.WindowState != WindowState.Minimized)
                    {
                        dialog.Topmost = true;
                    }
                };
                ownerDeactivatedHandler = (_, __) =>
                {
                    if (dialog == null)
                    {
                        return;
                    }

                    dialog.Topmost = false;
                };

                StateChanged += ownerStateChangedHandler;
                Activated += ownerActivatedHandler;
                Deactivated += ownerDeactivatedHandler;

                dialog.Show();

                int done = 0;
                for (int i = 0; i < steps.Length; i++)
                {
                    token.ThrowIfCancellationRequested();

                    try
                    {
                        var singleBoardTestContext = ContainerLocator.Container.Resolve<ISingleBoardTestContextService>();
                        singleBoardTestContext?.Update(string.Empty, boardName, boardType);
                    }
                    catch
                    {
                    }

                    vm.StatusText = $"{steps[i].Name}（{i + 1}/{steps.Length}）";
                    vm.Progress = done;

                    string result;
                    try
                    {
                        result = await steps[i].Run(token).ConfigureAwait(true);
                    }
                    catch (OperationCanceledException)
                    {
                        AppendSingleBoardReportLine($"CANCEL | {steps[i].Name}");
                        throw;
                    }
                    catch (Exception ex)
                    {
                        AppendSingleBoardReportLine($"EXCEPTION | {steps[i].Name} | {ex.GetType().Name} | {ex.Message}");
                        anyFailed = true;

                        if (string.Equals(boardType, "液压单板", StringComparison.OrdinalIgnoreCase))
                        {
                            abortExceptionMessage = ex is HydraulicAbortException
                                ? ex.Message
                                : $"{steps[i].Name}测试出现异常，已终止测试。\r\n异常信息：{ex.Message}";
                            AppendSingleBoardReportLine("END | FAIL | ABORT_ON_EXCEPTION");
                            if (vm != null)
                            {
                                vm.IsFailed = true;
                                vm.ConfirmStopOnClose = false;
                                vm.StatusText = $"异常终止：{steps[i].Name}";
                                vm.Progress = done;
                            }

                            throw new OperationCanceledException($"液压单板测试项异常终止: {steps[i].Name}", ex, token);
                        }

                        if (string.Equals(boardType, "加放油单板", StringComparison.OrdinalIgnoreCase) && ex is HydraulicAbortException)
                        {
                            abortExceptionMessage = ex.Message;
                            AppendSingleBoardReportLine("END | FAIL | ABORT_ON_EXCEPTION");
                            if (vm != null)
                            {
                                vm.IsFailed = true;
                                vm.ConfirmStopOnClose = false;
                                vm.StatusText = $"异常终止：{steps[i].Name}";
                                vm.Progress = done;
                            }

                            throw new OperationCanceledException($"加放油单板测试项异常终止: {steps[i].Name}", ex, token);
                        }

                        result = "异常";
                    }

                    AppendSingleBoardReportLine($"STEP | {steps[i].Name} | {NormalizeResult(result)}");
                    if (_singleBoardAutoStepResults != null)
                    {
                        _singleBoardAutoStepResults[steps[i].Name] = NormalizeResult(result);
                    }

                    done++;
                    vm.Progress = done;

                    if (!IsPass(result))
                    {
                        anyFailed = true;
                    }
                }

                AppendSingleBoardReportLine(anyFailed ? "END | FAIL" : "END | PASS");

                // 所有测试项完成后立即关闭28V电源，不等报表写入
                if (string.Equals(boardType, "液压单板", StringComparison.OrdinalIgnoreCase))
                {
                    await CleanupHydraulic28VPowerAsync().ConfigureAwait(true);
                }
                else if (string.Equals(boardType, "加放油单板", StringComparison.OrdinalIgnoreCase))
                {
                    try { await CleanupInert28VPowerAsync("192.168.1.15").ConfigureAwait(true); } catch { }
                    try { ContainerLocator.Container.Resolve<IBoardPowerService>()?.SetPoweredState(false); } catch { }
                }
                else if (string.Equals(boardType, "惰化模拟板", StringComparison.OrdinalIgnoreCase))
                {
                    try { await CleanupInert28VPowerAsync("192.168.1.15", "192.168.1.16").ConfigureAwait(true); } catch { }
                    try { ContainerLocator.Container.Resolve<IBoardPowerService>()?.SetPoweredState(false); } catch { }
                }
                else if (string.Equals(boardType, "惰化控制板", StringComparison.OrdinalIgnoreCase))
                {
                    try { await CleanupInert28VPowerAsync("192.168.1.15").ConfigureAwait(true); } catch { }
                    try { ContainerLocator.Container.Resolve<IBoardPowerService>()?.SetPoweredState(false); } catch { }
                }

                vm.StatusText = "写入报表...";
                vm.Progress = steps.Length;
                
                try
                {
                    await Task.Run(() => TryGenerateSingleBoardExcelReport(boardName, boardType)).ConfigureAwait(true);
                }
                catch (Exception reportEx)
                {
                    AppendSingleBoardReportLine($"REPORT | GENERATION_EXCEPTION | {reportEx.GetType().Name} | {reportEx.Message}");
                }
                
                vm.IsCompleted = !anyFailed;
                vm.IsFailed = anyFailed;
                vm.ConfirmStopOnClose = false;
                vm.Progress = steps.Length;
                vm.StatusText = anyFailed ? "完成" : "完成";

                // 所有单板测试完成后都弹出提示
                try
                {
                    dialog?.Close();
                    dialog = null;
                }
                catch
                {
                }

                shouldNotifyCompletion = true;
                completionMessage = anyFailed 
                    ? $"{boardName}测试完成" 
                    : $"{boardName}测试完成";
            }
            catch (OperationCanceledException)
            {
                if (string.IsNullOrWhiteSpace(abortExceptionMessage))
                {
                    AppendSingleBoardReportLine("END | CANCELED");
                }

                // 取消时也要关闭28V电源
                if (string.Equals(boardType, "液压单板", StringComparison.OrdinalIgnoreCase))
                {
                    await CleanupHydraulic28VPowerAsync().ConfigureAwait(true);
                }
                else if (string.Equals(boardType, "加放油单板", StringComparison.OrdinalIgnoreCase))
                {
                    try { await CleanupInert28VPowerAsync("192.168.1.15").ConfigureAwait(true); } catch { }
                    try { ContainerLocator.Container.Resolve<IBoardPowerService>()?.SetPoweredState(false); } catch { }
                }
                else if (string.Equals(boardType, "惰化模拟板", StringComparison.OrdinalIgnoreCase))
                {
                    try { await CleanupInert28VPowerAsync("192.168.1.15", "192.168.1.16").ConfigureAwait(true); } catch { }
                    try { ContainerLocator.Container.Resolve<IBoardPowerService>()?.SetPoweredState(false); } catch { }
                }
                else if (string.Equals(boardType, "惰化控制板", StringComparison.OrdinalIgnoreCase))
                {
                    try { await CleanupInert28VPowerAsync("192.168.1.15").ConfigureAwait(true); } catch { }
                    try { ContainerLocator.Container.Resolve<IBoardPowerService>()?.SetPoweredState(false); } catch { }
                }

                if (vm != null)
                {
                    vm.IsFailed = true;
                    vm.ConfirmStopOnClose = false;
                    if (string.IsNullOrWhiteSpace(abortExceptionMessage))
                    {
                        vm.StatusText = "已取消";
                    }
                }
            }
            finally
            {
                try
                {
                    if (ownerStateChangedHandler != null)
                    {
                        StateChanged -= ownerStateChangedHandler;
                    }
                    if (ownerActivatedHandler != null)
                    {
                        Activated -= ownerActivatedHandler;
                    }
                    if (ownerDeactivatedHandler != null)
                    {
                        Deactivated -= ownerDeactivatedHandler;
                    }
                }
                catch
                {
                }

                try
                {
                    dialog?.Close();
                }
                catch
                {
                }

                // 恢复主窗口操作
                if (mainVm1 != null) mainVm1.IsAutoTestRunning = false;
                try { _eventAggregator.GetEvent<Events.GlobalBatchTestEndedEvent>().Publish(); } catch { }
                _activeTestProjectItem = null;

                if (shouldNotifyCompletion && !string.IsNullOrWhiteSpace(completionMessage))
                {
                    try
                    {
                        ReMessageBox.Show(completionMessage, "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    catch
                    {
                    }
                }

                if (!string.IsNullOrWhiteSpace(abortExceptionMessage))
                {
                    try
                    {
                        ReMessageBox.Show(abortExceptionMessage, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    catch
                    {
                        try
                        {
                            MessageBox.Show(this, abortExceptionMessage, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                        catch
                        {
                        }
                    }
                }

                _singleBoardAutoTestCts?.Dispose();
                _singleBoardAutoTestCts = null;
                _selectedSingleBoardAutoTestItems = null;
                _singleBoardAutoStepResults = null;
                _hydraulicAutoTestVm61 = null;
                _hydraulicAutoTestVm62ChannelId = null;
                _hydraulicAutoTestVm62 = null;
                _hydraulicAutoTestVm63 = null;
                _hydraulicAutoTestVm64 = null;
                _hydraulicAutoTestVm65 = null;
                _hydraulicAutoTestVm66 = null;
                _hydraulicAutoTestVm67 = null;
                _hydraulicAutoTestVm68 = null;
                _hydraulicAutoTestVm69 = null;
                _fuelAutoTestVm1 = null;
                _fuelAutoTestVm2 = null;
                _fuelAutoTestVm3 = null;
                _fuelAutoTestVm4 = null;
                _fuelAutoTestVm5 = null;
                _fuelAutoTestVm6 = null;
                _fuelAutoTestVm7 = null;
                _fuelAutoTestVm8 = null;
                _inertSimulationAutoTestVm1 = null;
                _inertSimulationAutoTestVm2 = null;
                _inertSimulationAutoTestVm3 = null;
                _inertSimulationAutoTestVm4 = null;
                _inertControlAutoTestVm1 = null;
                _inertControlAutoTestVm2 = null;
                _inertControlAutoTestVm3 = null;
                _inertControlAutoTestVm4 = null;
                _inertControlAutoTestVm5 = null;
                _inertControlAutoTestVm6 = null;
                _inertControlAutoTestVm7 = null;
                _inertControlAutoTestVm8 = null;
            }
        }

        private static async Task CleanupHydraulic28VPowerAsync()
        {
            var boardPowerService = ContainerLocator.Container.Resolve<IBoardPowerService>();
            if (boardPowerService == null)
            {
                return;
            }

            try
            {
                await boardPowerService.PowerOffAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
            }

            try
            {
                boardPowerService.SetPoweredState(false);
            }
            catch
            {
            }
        }

        private static async Task CleanupInert28VPowerAsync(params string[] ipAddresses)
        {
            foreach (var ip in ipAddresses)
            {
                MeasureControl.Services.HardwareApis.IPowerSupplyApi ps = null;
                try
                {
                    ps = new MeasureControl.Services.HardwareApis.PowerSupplySocketApi();
                    await ps.ConnectAsync(ip, CancellationToken.None).ConfigureAwait(false);
                    await ps.SetOutputEnabledAsync(MeasureControl.Services.HardwareApis.PowerSupplyChannel.CH1, false, CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                }
                finally
                {
                    if (ps != null)
                    {
                        try { await ps.DisconnectAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                        try { await ps.DisposeAsync().ConfigureAwait(false); } catch { }
                    }
                }
            }
        }

        private (string Name, Func<CancellationToken, Task<string>> Run)[] BuildHydraulicSteps()
        {
            _hydraulicAutoTestVm61 = ContainerLocator.Container.Resolve<HC_6_1ViewModel>();
            _hydraulicAutoTestVm62ChannelId = ContainerLocator.Container.Resolve<HC_6_2ViewModel>();
            _hydraulicAutoTestVm62 = ContainerLocator.Container.Resolve<HC_6_3ViewModel>();
            _hydraulicAutoTestVm63 = ContainerLocator.Container.Resolve<HC_6_4ViewModel>();
            _hydraulicAutoTestVm64 = ContainerLocator.Container.Resolve<HC_6_5ViewModel>();
            _hydraulicAutoTestVm65 = ContainerLocator.Container.Resolve<HC_6_6ViewModel>();
            _hydraulicAutoTestVm66 = ContainerLocator.Container.Resolve<HC_6_7ViewModel>();
            _hydraulicAutoTestVm67 = ContainerLocator.Container.Resolve<HC_6_8ViewModel>();
            _hydraulicAutoTestVm68 = ContainerLocator.Container.Resolve<HC_6_9ViewModel>();
            _hydraulicAutoTestVm69 = ContainerLocator.Container.Resolve<HC_6_10ViewModel>();

            var vm61 = _hydraulicAutoTestVm61;
            var vm62ChannelId = _hydraulicAutoTestVm62ChannelId;
            var vm62 = _hydraulicAutoTestVm62;
            var vm63 = _hydraulicAutoTestVm63;
            var vm64 = _hydraulicAutoTestVm64;
            var vm65 = _hydraulicAutoTestVm65;
            var vm66 = _hydraulicAutoTestVm66;
            var vm67 = _hydraulicAutoTestVm67;
            var vm68 = _hydraulicAutoTestVm68;
            var vm69 = _hydraulicAutoTestVm69;

            async Task EnsureHydraulicPowerOnAsync(CancellationToken ct)
            {
                var hps = ContainerLocator.Container.Resolve<IBoardPowerService>();
                if (hps != null && !hps.IsPowered)
                    await hps.PowerOnAsync("液压单板", cancellationToken: ct).ConfigureAwait(false);
            }

            return new (string Name, Func<CancellationToken, Task<string>> Run)[]
            {
                ("电源阻抗测试", async ct =>
                {
                    var hps = ContainerLocator.Container.Resolve<IBoardPowerService>();
                    if (hps != null && hps.IsPowered)
                        await hps.PowerOffAsync(ct).ConfigureAwait(false);
                    var result = await vm61.RunOnceAsync(ct).ConfigureAwait(false);
                    if (!string.Equals(result, "PASS", StringComparison.OrdinalIgnoreCase))
                        throw new HydraulicAbortException("电源阻抗测试不合格，已终止后续测试");
                    return result;
                }),
                ("通道ID测试", async ct =>
                {
                    return await vm62ChannelId.RunOnceAsync(ct).ConfigureAwait(false);
                }),
                ("二次电源测试", async ct =>
                {
                    await EnsureHydraulicPowerOnAsync(ct).ConfigureAwait(false);
                    return await vm62.RunOnceAsync(ct).ConfigureAwait(false);
                }),
                ("温度采集测试", async ct =>
                {
                    await EnsureHydraulicPowerOnAsync(ct).ConfigureAwait(false);
                    return await vm63.RunOnceAsync(ct).ConfigureAwait(false);
                }),
                ("压力传感器信号采集测试", async ct =>
                {
                    await EnsureHydraulicPowerOnAsync(ct).ConfigureAwait(false);
                    return await vm64.RunOnceAsync(ct).ConfigureAwait(false);
                }),
                ("压差传感器信号采集测试", async ct =>
                {
                    await EnsureHydraulicPowerOnAsync(ct).ConfigureAwait(false);
                    return await vm65.RunOnceAsync(ct).ConfigureAwait(false);
                }),
                ("油量传感器信号采集测试", async ct =>
                {
                    await EnsureHydraulicPowerOnAsync(ct).ConfigureAwait(false);
                    return await vm66.RunOnceAsync(ct).ConfigureAwait(false);
                }),
                ("离散量采集测试", async ct =>
                {
                    return await vm67.RunOnceAsync(ct).ConfigureAwait(false);
                }),
                ("离散量输出测试", async ct =>
                {
                    return await vm68.RunOnceAsync(ct).ConfigureAwait(false);
                }),
                ("通讯模块测试", async ct =>
                {
                    return await vm69.RunOnceAsync(ct).ConfigureAwait(false);
                }),
            };
        }

        private static (string Name, Func<CancellationToken, Task<string>> Run)[] BuildAirSteps()
        {
            return Array.Empty<(string Name, Func<CancellationToken, Task<string>> Run)>();
        }

        private (string Name, Func<CancellationToken, Task<string>> Run)[] BuildInertSimulationSteps()
        {
            _inertSimulationAutoTestVm1 = ContainerLocator.Container.Resolve<MeasureControl.ViewModels.SingleBoardTest.InertController.PowerImpedanceTestViewModel>();
            _inertSimulationAutoTestVm2 = ContainerLocator.Container.Resolve<MeasureControl.ViewModels.SingleBoardTest.InertController.SecondaryTertiaryPowerTestViewModel>();
            _inertSimulationAutoTestVm3 = ContainerLocator.Container.Resolve<MeasureControl.ViewModels.SingleBoardTest.InertController.OverTemperatureCutoffTestViewModel>();
            _inertSimulationAutoTestVm4 = ContainerLocator.Container.Resolve<MeasureControl.ViewModels.SingleBoardTest.InertController.LatchModuleCircuitTestViewModel>();

            var vm1 = _inertSimulationAutoTestVm1;
            var vm2 = _inertSimulationAutoTestVm2;
            var vm3 = _inertSimulationAutoTestVm3;
            var vm4 = _inertSimulationAutoTestVm4;

            vm1.SkipMainPowerOff = true;
            vm2.SkipMainPowerOff = true;
            vm3.SkipMainPowerOff = true;
            vm4.SkipMainPowerOff = true;

            // 用于跟踪是否已经上电
            bool isPoweredOn = false;

            // 上电辅助方法 - 使用IBoardPowerService来更新状态
            async Task EnsurePowerOnAsync(CancellationToken ct)
            {
                if (isPoweredOn) return;
                isPoweredOn = true;
                try
                {
                    var hps = ContainerLocator.Container.Resolve<IBoardPowerService>();
                    if (hps != null && !hps.IsPowered)
                    {
                        await hps.PowerOnAsync("惰化模拟板", cancellationToken: ct).ConfigureAwait(false);
                    }
                }
                catch
                {
                }
            }

            return new (string Name, Func<CancellationToken, Task<string>> Run)[]
            {
                ("电源阻抗测试", async ct =>
                {
                    var result = await vm1.RunOnceAsync(ct).ConfigureAwait(false);
                    // 电源阻抗测试完成后上电
                    await EnsurePowerOnAsync(ct).ConfigureAwait(false);
                    return result;
                }),
                ("二次、三次电源测试", async ct =>
                {
                    await EnsurePowerOnAsync(ct).ConfigureAwait(false);
                    return await vm2.RunOnceAsync(ct).ConfigureAwait(false);
                }),
                ("超温切断模块电路测试", async ct =>
                {
                    await EnsurePowerOnAsync(ct).ConfigureAwait(false);
                    return await vm3.RunOnceAsync(ct).ConfigureAwait(false);
                }),
                ("锁存模块电路测试", async ct =>
                {
                    await EnsurePowerOnAsync(ct).ConfigureAwait(false);
                    return await vm4.RunOnceAsync(ct).ConfigureAwait(false);
                })
            };
        }

        private static (string Name, Func<CancellationToken, Task<string>> Run)[] BuildInertingSteps()
        {
            return Array.Empty<(string Name, Func<CancellationToken, Task<string>> Run)>();
        }

        private (string Name, Func<CancellationToken, Task<string>> Run)[] BuildInertControlSteps()
        {
            _inertControlAutoTestVm1 = ContainerLocator.Container.Resolve<MeasureControl.ViewModels.SingleBoardTest.InertController.ControlBoardPowerImpedanceTestViewModel>();
            _inertControlAutoTestVm2 = ContainerLocator.Container.Resolve<MeasureControl.ViewModels.SingleBoardTest.InertController.ControlBoardSecondaryTertiaryPowerTestViewModel>();
            _inertControlAutoTestVm3 = ContainerLocator.Container.Resolve<MeasureControl.ViewModels.SingleBoardTest.InertController.ControlBoardDiscreteInputModuleTestViewModel>();
            _inertControlAutoTestVm4 = ContainerLocator.Container.Resolve<MeasureControl.ViewModels.SingleBoardTest.InertController.DiscreteOutputModuleTestViewModel>();
            _inertControlAutoTestVm5 = ContainerLocator.Container.Resolve<MeasureControl.ViewModels.SingleBoardTest.InertController.TemperatureSensorSignalAcquisitionTestViewModel>();
            _inertControlAutoTestVm6 = ContainerLocator.Container.Resolve<MeasureControl.ViewModels.SingleBoardTest.InertController.PressureSensorSignalAcquisitionTestViewModel>();
            _inertControlAutoTestVm7 = ContainerLocator.Container.Resolve<MeasureControl.ViewModels.SingleBoardTest.InertController.OxygenSensorSignalAcquisitionTestViewModel>();
            _inertControlAutoTestVm8 = ContainerLocator.Container.Resolve<MeasureControl.ViewModels.SingleBoardTest.InertController.TcvMotorDriveTestViewModel>();

            var vm1 = _inertControlAutoTestVm1;
            var vm2 = _inertControlAutoTestVm2;
            var vm3 = _inertControlAutoTestVm3;
            var vm4 = _inertControlAutoTestVm4;
            var vm5 = _inertControlAutoTestVm5;
            var vm6 = _inertControlAutoTestVm6;
            var vm7 = _inertControlAutoTestVm7;
            var vm8 = _inertControlAutoTestVm8;

            vm2.SkipMainPowerOff = true;
            vm3.SkipMainPowerOff = true;
            vm4.SkipMainPowerOff = true;
            vm5.SkipMainPowerOff = true;
            vm6.SkipMainPowerOff = true;
            vm7.SkipMainPowerOff = true;
            vm8.SkipMainPowerOff = true;

            // 用于跟踪是否已经上电
            bool isPoweredOn = false;

            // 上电辅助方法 - 使用IBoardPowerService来更新状态
            async Task EnsurePowerOnAsync(CancellationToken ct)
            {
                if (isPoweredOn) return;
                isPoweredOn = true;
                try
                {
                    var hps = ContainerLocator.Container.Resolve<IBoardPowerService>();
                    if (hps != null && !hps.IsPowered)
                    {
                        await hps.PowerOnAsync("惰化控制板", cancellationToken: ct).ConfigureAwait(false);
                    }
                }
                catch
                {
                }
            }

            return new (string Name, Func<CancellationToken, Task<string>> Run)[]
            {
                ("控制板电源阻抗测试", async ct =>
                {
                    var result = await vm1.RunOnceAsync(ct).ConfigureAwait(false);
                    // 电源阻抗测试完成后上电
                    await EnsurePowerOnAsync(ct).ConfigureAwait(false);
                    return result;
                }),
                ("控制板二次、三次电源测试", async ct =>
                {
                    await EnsurePowerOnAsync(ct).ConfigureAwait(false);
                    return await vm2.RunOnceAsync(ct).ConfigureAwait(false);
                }),
                ("控制板离散输入模块测试", async ct =>
                {
                    await EnsurePowerOnAsync(ct).ConfigureAwait(false);
                    return await vm3.RunOnceAsync(ct).ConfigureAwait(false);
                }),
                ("控制板离散输出模块测试", async ct =>
                {
                    await EnsurePowerOnAsync(ct).ConfigureAwait(false);
                    return await vm4.RunOnceAsync(ct).ConfigureAwait(false);
                }),
                ("温度传感器信号采集测试", async ct =>
                {
                    await EnsurePowerOnAsync(ct).ConfigureAwait(false);
                    return await vm5.RunOnceAsync(ct).ConfigureAwait(false);
                }),
                ("压力传感器信号采集测试", async ct =>
                {
                    await EnsurePowerOnAsync(ct).ConfigureAwait(false);
                    return await vm6.RunOnceAsync(ct).ConfigureAwait(false);
                }),
                ("氧气传感器信号采集测试", async ct =>
                {
                    await EnsurePowerOnAsync(ct).ConfigureAwait(false);
                    return await vm7.RunOnceAsync(ct).ConfigureAwait(false);
                }),
                ("TCV电机驱动测试", async ct =>
                {
                    await EnsurePowerOnAsync(ct).ConfigureAwait(false);
                    return await vm8.RunOnceAsync(ct).ConfigureAwait(false);
                })
            };
        }

        private (string Name, Func<CancellationToken, Task<string>> Run)[] BuildFuelSteps(double voltage = 28.0, bool isFirstRound = true)
        {
            if (isFirstRound)
            {
                _fuelAutoTestVm1 = ContainerLocator.Container.Resolve<PowerImpedanceTestViewModel>();
                _fuelAutoTestVm2 = ContainerLocator.Container.Resolve<SecondaryPowerTestViewModel>();
                _fuelAutoTestVm3 = ContainerLocator.Container.Resolve<LowVoltageAlarmTestViewModel>();
                _fuelAutoTestVm4 = ContainerLocator.Container.Resolve<TemperatureAcquisitionTestViewModel>();
                _fuelAutoTestVm5 = ContainerLocator.Container.Resolve<DiscreteInputTestViewModel>();
                _fuelAutoTestVm6 = ContainerLocator.Container.Resolve<DiscreteOutputTestViewModel>();
                _fuelAutoTestVm7 = ContainerLocator.Container.Resolve<RS422CommunicationFunctionTestViewModel>();
                _fuelAutoTestVm8 = ContainerLocator.Container.Resolve<RS422SelfCheckTestViewModel>();
            }

            var Vm1 = _fuelAutoTestVm1;
            var Vm2 = _fuelAutoTestVm2;
            var Vm3 = _fuelAutoTestVm3;
            var Vm4 = _fuelAutoTestVm4;
            var Vm5 = _fuelAutoTestVm5;
            var Vm6 = _fuelAutoTestVm6;
            var Vm7 = _fuelAutoTestVm7;
            var Vm8 = _fuelAutoTestVm8;

            bool isPoweredOn = false;

            // 上电辅助方法 - 复用已有的全局上电状态，未上电则通过服务上电
            async Task EnsurePowerOnAsync(CancellationToken ct)
            {
                if (isPoweredOn) return;
                isPoweredOn = true;
                var hps = ContainerLocator.Container.Resolve<IBoardPowerService>();
                if (hps != null && !hps.IsPowered)
                    await hps.PowerOnAsync("加放油单板", voltage, ct).ConfigureAwait(false);
            }

            return new (string Name, Func<CancellationToken, Task<string>> Run)[]
            {
                ("电源阻抗测试", async ct =>
                {
                    if (!isFirstRound)
                    {
                        // 复用18V阻抗结果，跳过重测
                        await EnsurePowerOnAsync(ct).ConfigureAwait(false);
                        return Vm1?.OverallResult ?? "--";
                    }
                    var result = await Vm1.RunOnceAsync(ct);
                    bool impedancePass = string.Equals(result, "PASS", StringComparison.OrdinalIgnoreCase) ||
                                        string.Equals(result, "\u5408\u683c", StringComparison.OrdinalIgnoreCase);
                    if (!impedancePass)
                        throw new HydraulicAbortException("\u7535\u6e90\u963b\u6297\u6d4b\u8bd5\u4e0d\u5408\u683c\uff0c\u5df2\u7ec8\u6b62\u540e\u7eed\u6d4b\u8bd5");
                    // 电源阻抗测试合格后上电，供后续测试使用
                    await EnsurePowerOnAsync(ct).ConfigureAwait(false);
                    return result;
                }),
                ("二次电源测试", async ct =>
                {
                    await EnsurePowerOnAsync(ct).ConfigureAwait(false);
                    return await Vm2.RunOnceAsync(ct);
                }),
                ("低电压告警功能测试", async ct =>
                {
                    await EnsurePowerOnAsync(ct).ConfigureAwait(false);
                    return await Vm3.RunOnceAsync(ct);
                }),
                ("温度采集功能", async ct =>
                {
                    await EnsurePowerOnAsync(ct).ConfigureAwait(false);
                    return await Vm4.RunOnceAsync(ct);
                }),
                ("离散量采集功能测试", async ct =>
                {
                    await EnsurePowerOnAsync(ct).ConfigureAwait(false);
                    return await Vm5.RunOnceAsync(ct);
                }),
                ("离散量输出功能测试", async ct =>
                {
                    await EnsurePowerOnAsync(ct).ConfigureAwait(false);
                    Vm6.SelectedSupplyVoltage = voltage;
                    string r6 = !isFirstRound
                        ? await Vm6.RunStepCOnlyAsync(ct)
                        : await Vm6.RunOnceAsync(ct);
                    isPoweredOn = false; // 离散量输出测试强制下电，后续测试需重新上电
                    return r6;
                }),
                ("RS422通信功能测试", async ct =>
                {
                    await EnsurePowerOnAsync(ct).ConfigureAwait(false);
                    return await Vm7.RunOnceAsync(ct);
                }),
                ("RS422通信自检测功能测试", async ct =>
                {
                    await EnsurePowerOnAsync(ct).ConfigureAwait(false);
                    return await Vm8.RunOnceAsync(ct);
                }),
            };
        }

        private async Task RunFuelBoardMultiVoltageTestAsync(string boardName, string[] selectedItems)
        {
            _singleBoardAutoTestCts = new CancellationTokenSource();
            var token = _singleBoardAutoTestCts.Token;

            TestProgressDialog progressDialog = null;
            TestProgressDialogViewModel progressVm = null;
            EventHandler ownerStateChangedHandler = null;
            EventHandler ownerActivatedHandler = null;
            EventHandler ownerDeactivatedHandler = null;

            var mainVm2 = DataContext as MainWindowViewModel;
            bool anyFailed = false;
            bool shouldNotifyCompletion = false;
            string completionMessage = null;
            string abortMessage = null;

            var voltages = new double[] { 18.0, 28.0, 32.2 };
            var snapshots = new FuelRoundSnapshot[3];
            var roundResults = new Dictionary<string, string>[3];

            try
            {
                PrepareSingleBoardReport(boardName);
                AppendSingleBoardReportLine($"START | {boardName} | 加放油单板 | 三档电压测试");

                if (mainVm2 != null) mainVm2.IsAutoTestRunning = true;

                // 自动导航到测试单板第一个测试项
                var firstTestItem2 = _activeTestProjectItem?.Children?.FirstOrDefault();
                if (firstTestItem2 != null && _viewModel?.TreeItemDoubleClickCommand?.CanExecute(firstTestItem2) == true)
                    _viewModel.TreeItemDoubleClickCommand.Execute(firstTestItem2);

                var templateSteps = BuildFuelSteps();
                int stepsPerRound = templateSteps.Count(s => selectedItems.Contains(s.Name, StringComparer.OrdinalIgnoreCase));

                progressVm = new TestProgressDialogViewModel
                {
                    HeaderText = boardName,
                    StatusText = "准备开始...",
                    Progress = 0,
                    Total = stepsPerRound * 3,
                    ConfirmStopOnClose = true
                };
                progressVm.RequestCancel = () => { try { _singleBoardAutoTestCts?.Cancel(); } catch { } };

                progressDialog = new TestProgressDialog
                {
                    DataContext = progressVm,
                    Owner = this,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Topmost = true
                };

                ownerStateChangedHandler = (_, __) =>
                {
                    if (progressDialog?.Owner != null)
                        progressDialog.Topmost = progressDialog.Owner.WindowState != WindowState.Minimized;
                };
                ownerActivatedHandler = (_, __) =>
                {
                    if (progressDialog?.Owner != null && progressDialog.Owner.WindowState != WindowState.Minimized)
                        progressDialog.Topmost = true;
                };
                ownerDeactivatedHandler = (_, __) => { if (progressDialog != null) progressDialog.Topmost = false; };

                StateChanged += ownerStateChangedHandler;
                Activated += ownerActivatedHandler;
                Deactivated += ownerDeactivatedHandler;

                progressDialog.Show();

                int doneCount = 0;
                bool globalAbort = false;

                for (int vi = 0; vi < voltages.Length && !globalAbort; vi++)
                {
                    double voltage = voltages[vi];
                    // 每档开始前重置电源状态，确保本档以正确电压重新上电
                    try { ContainerLocator.Container.Resolve<IBoardPowerService>()?.SetPoweredState(false); } catch { }
                    var steps = BuildFuelSteps(voltage, isFirstRound: vi == 0);
                    var filteredSteps = steps.Where(s => selectedItems.Contains(s.Name, StringComparer.OrdinalIgnoreCase)).ToArray();

                    AppendSingleBoardReportLine($"ROUND | {voltage:G}V");
                    roundResults[vi] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    bool roundAborted = false;

                    for (int i = 0; i < filteredSteps.Length; i++)
                    {
                        token.ThrowIfCancellationRequested();

                        var step = filteredSteps[i];
                        progressVm.StatusText = $"[{voltage:G}V] {step.Name}（{i + 1}/{filteredSteps.Length}）";
                        progressVm.Progress = doneCount;

                        string result;
                        try
                        {
                            result = await step.Run(token).ConfigureAwait(true);
                        }
                        catch (OperationCanceledException)
                        {
                            AppendSingleBoardReportLine($"CANCEL | {step.Name}");
                            throw;
                        }
                        catch (HydraulicAbortException ex)
                        {
                            abortMessage = ex.Message;
                            AppendSingleBoardReportLine($"ABORT | {step.Name} | {ex.Message}");
                            anyFailed = true;
                            roundAborted = true;
                            globalAbort = true;
                            progressVm.IsFailed = true;
                            progressVm.ConfirmStopOnClose = false;
                            progressVm.StatusText = $"阻抗测试不合格，已终止全部测试";
                            break;
                        }
                        catch (Exception ex)
                        {
                            AppendSingleBoardReportLine($"EXCEPTION | {step.Name} | {ex.Message}");
                            anyFailed = true;
                            result = "异常";
                        }

                        AppendSingleBoardReportLine($"STEP | [{voltage:G}V] {step.Name} | {NormalizeResult(result)}");
                        roundResults[vi][step.Name] = NormalizeResult(result);
                        if (!IsPass(result)) anyFailed = true;
                        doneCount++;
                        progressVm.Progress = doneCount;
                    }

                    snapshots[vi] = SnapshotFuelVms(voltage, roundResults[vi], roundAborted);

                    // 每档结束后物理下电 CH1，确保下一档以新电压干净上电
                    try
                    {
                        var hps = ContainerLocator.Container.Resolve<IBoardPowerService>();
                        if (hps != null && hps.IsPowered)
                            await hps.PowerOffAsync(token).ConfigureAwait(true);
                        else
                            hps?.SetPoweredState(false);
                    }
                    catch { }
                }

                _fuelSnapshot17V = snapshots[0];
                _fuelSnapshot28V = snapshots[1];
                _fuelSnapshot322V = snapshots[2];

                AppendSingleBoardReportLine(anyFailed ? "END | FAIL" : "END | PASS");

                try { await CleanupInert28VPowerAsync("192.168.1.15").ConfigureAwait(true); } catch { }
                try { ContainerLocator.Container.Resolve<IBoardPowerService>()?.SetPoweredState(false); } catch { }

                progressVm.StatusText = "写入报表...";
                try
                {
                    await Task.Run(() => TryGenerateSingleBoardExcelReport(boardName, "加放油单板")).ConfigureAwait(true);
                }
                catch { }

                progressVm.IsCompleted = !anyFailed;
                progressVm.IsFailed = anyFailed;
                progressVm.ConfirmStopOnClose = false;
                progressVm.Progress = stepsPerRound * 3;
                progressVm.StatusText = "完成";

                try { progressDialog?.Close(); progressDialog = null; } catch { }

                shouldNotifyCompletion = true;
                completionMessage = $"{boardName}测试完成";
            }
            catch (OperationCanceledException)
            {
                AppendSingleBoardReportLine("END | CANCELED");

                for (int vi = 0; vi < voltages.Length; vi++)
                {
                    if (snapshots[vi] == null)
                        snapshots[vi] = SnapshotFuelVms(voltages[vi], roundResults[vi] ?? new Dictionary<string, string>(), true);
                }
                _fuelSnapshot17V = snapshots[0];
                _fuelSnapshot28V = snapshots[1];
                _fuelSnapshot322V = snapshots[2];

                try { await CleanupInert28VPowerAsync("192.168.1.15").ConfigureAwait(true); } catch { }
                try { ContainerLocator.Container.Resolve<IBoardPowerService>()?.SetPoweredState(false); } catch { }

                if (progressVm != null)
                {
                    progressVm.IsFailed = true;
                    progressVm.ConfirmStopOnClose = false;
                    progressVm.StatusText = string.IsNullOrWhiteSpace(abortMessage) ? "已取消" : "阻抗测试不合格，已终止";
                }
            }
            finally
            {
                try { if (ownerStateChangedHandler != null) StateChanged -= ownerStateChangedHandler; } catch { }
                try { if (ownerActivatedHandler != null) Activated -= ownerActivatedHandler; } catch { }
                try { if (ownerDeactivatedHandler != null) Deactivated -= ownerDeactivatedHandler; } catch { }
                try { progressDialog?.Close(); } catch { }

                if (mainVm2 != null) mainVm2.IsAutoTestRunning = false;
                try { _eventAggregator.GetEvent<Events.GlobalBatchTestEndedEvent>().Publish(); } catch { }
                _activeTestProjectItem = null;

                if (shouldNotifyCompletion && !string.IsNullOrWhiteSpace(completionMessage))
                    try { ReMessageBox.Show(completionMessage, "提示", MessageBoxButton.OK, MessageBoxImage.Information); } catch { }

                if (!string.IsNullOrWhiteSpace(abortMessage))
                    try { ReMessageBox.Show(abortMessage, "错误", MessageBoxButton.OK, MessageBoxImage.Error); } catch { }

                _singleBoardAutoTestCts?.Dispose();
                _singleBoardAutoTestCts = null;
                _selectedSingleBoardAutoTestItems = null;
                _singleBoardAutoStepResults = null;
                _fuelAutoTestVm1 = null;
                _fuelAutoTestVm2 = null;
                _fuelAutoTestVm3 = null;
                _fuelAutoTestVm4 = null;
                _fuelAutoTestVm5 = null;
                _fuelAutoTestVm6 = null;
                _fuelAutoTestVm7 = null;
                _fuelAutoTestVm8 = null;
            }
        }

        private FuelRoundSnapshot SnapshotFuelVms(double voltage, Dictionary<string, string> ran, bool aborted)
        {
            var vm1 = _fuelAutoTestVm1;
            var vm2 = _fuelAutoTestVm2;
            var vm3 = _fuelAutoTestVm3;
            var vm4 = _fuelAutoTestVm4;
            var vm5 = _fuelAutoTestVm5;
            var vm6 = _fuelAutoTestVm6;
            var vm7 = _fuelAutoTestVm7;
            var vm8 = _fuelAutoTestVm8;
            bool did(string name) => ran != null && ran.ContainsKey(name);

            return new FuelRoundSnapshot
            {
                Voltage = voltage,
                Aborted = aborted,
                Vm1_ImpA = did("电源阻抗测试") ? vm1?.ImpedanceA : null,
                Vm1_ImpB = did("电源阻抗测试") ? vm1?.ImpedanceB : null,
                Vm1_ImpC = did("电源阻抗测试") ? vm1?.ImpedanceC : null,
                Vm1_ImpD = did("电源阻抗测试") ? vm1?.ImpedanceD : null,
                Vm1_ResA = did("电源阻抗测试") ? vm1?.ResultA : null,
                Vm1_ResB = did("电源阻抗测试") ? vm1?.ResultB : null,
                Vm1_ResC = did("电源阻抗测试") ? vm1?.ResultC : null,
                Vm1_ResD = did("电源阻抗测试") ? vm1?.ResultD : null,
                Vm1_Overall = did("电源阻抗测试") ? vm1?.OverallResult : null,
                Vm2_Voltage = did("二次电源测试") ? vm2?.VoltageValue : null,
                Vm2_TestResult = did("二次电源测试") ? vm2?.TestResult : null,
                Vm2_Overall = did("二次电源测试") ? vm2?.OverallResult : null,
                Vm3_FlipVoltage = did("低电压告警功能测试") ? vm3?.FlipVoltage : null,
                Vm3_TestResult = did("低电压告警功能测试") ? vm3?.TestResult : null,
                Vm3_Overall = did("低电压告警功能测试") ? vm3?.OverallResult : null,
                Vm4_Temp = did("温度采集功能") ? vm4?.TemperatureValue : null,
                Vm4_TestResult = did("温度采集功能") ? vm4?.TestResult : null,
                Vm4_Overall = did("温度采集功能") ? vm4?.OverallResult : null,
                Vm5_B0Gnd = did("离散量采集功能测试") ? vm5?.Bank0GroundedResults : null,
                Vm5_B1Gnd = did("离散量采集功能测试") ? vm5?.Bank1GroundedResults : null,
                Vm5_B0Open = did("离散量采集功能测试") ? vm5?.Bank0OpenResults : null,
                Vm5_B1Open = did("离散量采集功能测试") ? vm5?.Bank1OpenResults : null,
                Vm5_GndResult = did("离散量采集功能测试") ? vm5?.GroundedTestResult : null,
                Vm5_OpenResult = did("离散量采集功能测试") ? vm5?.OpenTestResult : null,
                Vm5_Overall = did("离散量采集功能测试") ? vm5?.OverallResult : null,
                Vm6_J6 = did("离散量输出功能测试") ? vm6?.ImpedanceJ6 : null,
                Vm6_J7 = did("离散量输出功能测试") ? vm6?.ImpedanceJ7 : null,
                Vm6_J8 = did("离散量输出功能测试") ? vm6?.ImpedanceJ8 : null,
                Vm6_J9 = did("离散量输出功能测试") ? vm6?.ImpedanceJ9 : null,
                Vm6_J10 = did("离散量输出功能测试") ? vm6?.ImpedanceJ10 : null,
                Vm6_J11 = did("离散量输出功能测试") ? vm6?.ImpedanceJ11 : null,
                Vm6_J12 = did("离散量输出功能测试") ? vm6?.ImpedanceJ12 : null,
                Vm6_J13 = did("离散量输出功能测试") ? vm6?.ImpedanceJ13 : null,
                Vm6_OJ6 = did("离散量输出功能测试") ? vm6?.ImpedanceOpenJ6 : null,
                Vm6_OJ7 = did("离散量输出功能测试") ? vm6?.ImpedanceOpenJ7 : null,
                Vm6_OJ8 = did("离散量输出功能测试") ? vm6?.ImpedanceOpenJ8 : null,
                Vm6_OJ9 = did("离散量输出功能测试") ? vm6?.ImpedanceOpenJ9 : null,
                Vm6_OJ10 = did("离散量输出功能测试") ? vm6?.ImpedanceOpenJ10 : null,
                Vm6_OJ11 = did("离散量输出功能测试") ? vm6?.ImpedanceOpenJ11 : null,
                Vm6_OJ12 = did("离散量输出功能测试") ? vm6?.ImpedanceOpenJ12 : null,
                Vm6_OJ13 = did("离散量输出功能测试") ? vm6?.ImpedanceOpenJ13 : null,
                Vm6_J14V = did("离散量输出功能测试") ? vm6?.J14Voltage : null,
                Vm6_StepA = did("离散量输出功能测试") ? vm6?.StepAResult : null,
                Vm6_StepB = did("离散量输出功能测试") ? vm6?.StepBResult : null,
                Vm6_StepC = did("离散量输出功能测试") ? vm6?.StepCResult : null,
                Vm6_Overall = did("离散量输出功能测试") ? vm6?.OverallResult : null,
                Vm7_ARx = did("RS422通信功能测试") ? vm7?.StepARxData : null,
                Vm7_BRx = did("RS422通信功能测试") ? vm7?.StepBRxData : null,
                Vm7_CRx = did("RS422通信功能测试") ? vm7?.StepCRxData : null,
                Vm7_DRx = did("RS422通信功能测试") ? vm7?.StepDRxData : null,
                Vm7_StepA = did("RS422通信功能测试") ? vm7?.StepAResult : null,
                Vm7_StepB = did("RS422通信功能测试") ? vm7?.StepBResult : null,
                Vm7_StepC = did("RS422通信功能测试") ? vm7?.StepCResult : null,
                Vm7_StepD = did("RS422通信功能测试") ? vm7?.StepDResult : null,
                Vm7_Overall = did("RS422通信功能测试") ? vm7?.OverallResult : null,
                Vm8_ARx = did("RS422通信自检测功能测试") ? vm8?.StepARxData : null,
                Vm8_StepA = did("RS422通信自检测功能测试") ? vm8?.StepAResult : null,
                Vm8_StepB = did("RS422通信自检测功能测试") ? vm8?.StepBResult : null,
                Vm8_Overall = did("RS422通信自检测功能测试") ? vm8?.OverallResult : null,
            };
        }

        private void PrepareSingleBoardReport(string boardName)
        {
            _singleBoardAutoTestExcelReportPath = null;
            _singleBoardAutoTestReportPath = null;
            _singleBoardAutoStepResults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _fuelSnapshot17V = null;
            _fuelSnapshot28V = null;
            _fuelSnapshot322V = null;
            _inertControlPowerImpedanceValues = null;
            _inertControlPowerImpedanceResults = null;
            _inertControlPowerImpedanceOverallResult = null;
            _inertControlPowerImpedanceSelected = false;
            _inertControlPowerImpedanceExecuted = false;
            _inertControlSecondaryTertiaryValues = null;
            _inertControlSecondaryTertiaryResults = null;
            _inertControlSecondaryTertiaryOverallResult = null;
            _inertControlSecondaryTertiarySelected = false;
            _inertControlSecondaryTertiaryExecuted = false;
            _inertControlDiscreteInputPrimaryValues = null;
            _inertControlDiscreteInputPrimaryResults = null;
            _inertControlDiscreteInputSecondaryValues = null;
            _inertControlDiscreteInputSecondaryResults = null;
            _inertControlDiscreteInputOverallResult = null;
            _inertControlDiscreteInputSelected = false;
            _inertControlDiscreteInputExecuted = false;
            _inertControlDiscreteOutputHighValues = null;
            _inertControlDiscreteOutputHighResults = null;
            _inertControlDiscreteOutputLowValues = null;
            _inertControlDiscreteOutputLowResults = null;
            _inertControlDiscreteOutputOverallResult = null;
            _inertControlDiscreteOutputSelected = false;
            _inertControlDiscreteOutputExecuted = false;
            _inertControlTempSensorValues = null;
            _inertControlTempSensorResults = null;
            _inertControlTempSensorOverallResult = null;
            _inertControlTempSensorSelected = false;
            _inertControlTempSensorExecuted = false;
            _inertControlPressureSensorValues = null;
            _inertControlPressureSensorResults = null;
            _inertControlPressureSensorOverallResult = null;
            _inertControlPressureSensorSelected = false;
            _inertControlPressureSensorExecuted = false;
            _inertControlOxygenConcentrationValues = null;
            _inertControlOxygenConcentrationResults = null;
            _inertControlOxygenPressureValues = null;
            _inertControlOxygenPressureResults = null;
            _inertControlOxygenSensorOverallResult = null;
            _inertControlOxygenSensorSelected = false;
            _inertControlOxygenSensorExecuted = false;
            _inertControlTcvMotorResults = null;
            _inertControlTcvMotorOverallResult = null;
            _inertControlTcvMotorSelected = false;
            _inertControlTcvMotorExecuted = false;
            _inertSimPowerImpedanceValues = null;
            _inertSimPowerImpedanceResults = null;
            _inertSimPowerImpedanceOverallResult = null;
            _inertSimPowerImpedanceSelected = false;
            _inertSimPowerImpedanceExecuted = false;
            _inertSimSecondaryTertiaryValues = null;
            _inertSimSecondaryTertiaryResults = null;
            _inertSimSecondaryTertiaryOverallResult = null;
            _inertSimSecondaryTertiarySelected = false;
            _inertSimSecondaryTertiaryExecuted = false;
            _inertSimOverTempValues = null;
            _inertSimOverTempResults = null;
            _inertSimOverTempOverallResult = null;
            _inertSimOverTempSelected = false;
            _inertSimOverTempExecuted = false;
            _inertSimLatchValues = null;
            _inertSimLatchResults = null;
            _inertSimLatchOverallResult = null;
            _inertSimLatchSelected = false;
            _inertSimLatchExecuted = false;
        }

        private void CaptureSingleBoardExcelReportSnapshot(string boardType)
        {
            if (string.Equals(boardType, "惰化控制板", StringComparison.OrdinalIgnoreCase))
            {
                _inertControlPowerImpedanceSelected = IsSingleBoardStepSelected("控制板电源阻抗测试");
                _inertControlPowerImpedanceExecuted = DidSingleBoardStepExecute("控制板电源阻抗测试");
                _inertControlSecondaryTertiarySelected = IsSingleBoardStepSelected("控制板二次、三次电源测试");
                _inertControlSecondaryTertiaryExecuted = DidSingleBoardStepExecute("控制板二次、三次电源测试");

                var vm1 = _inertControlAutoTestVm1;
                if (vm1 != null)
                {
                    _inertControlPowerImpedanceValues = Enumerable.Range(0, 6)
                        .Select(i => vm1.Items.Count > i && vm1.Items[i] != null ? vm1.Items[i].ImpedanceText : "--")
                        .ToArray();
                    _inertControlPowerImpedanceResults = Enumerable.Range(0, 6)
                        .Select(i => vm1.Items.Count > i && vm1.Items[i] != null ? vm1.Items[i].Result : "--")
                        .ToArray();
                    _inertControlPowerImpedanceOverallResult = vm1.OverallResult;
                }

                var vm2 = _inertControlAutoTestVm2;
                if (vm2 != null)
                {
                    _inertControlSecondaryTertiaryValues = Enumerable.Range(0, 5)
                        .Select(i => vm2.Items.Count > i && vm2.Items[i] != null ? vm2.Items[i].ValueText : "--")
                        .ToArray();
                    _inertControlSecondaryTertiaryResults = Enumerable.Range(0, 5)
                        .Select(i => vm2.Items.Count > i && vm2.Items[i] != null ? vm2.Items[i].Result : "--")
                        .ToArray();
                    _inertControlSecondaryTertiaryOverallResult = vm2.OverallResult;
                }

                _inertControlDiscreteInputSelected = IsSingleBoardStepSelected("控制板离散输入模块测试");
                _inertControlDiscreteInputExecuted = DidSingleBoardStepExecute("控制板离散输入模块测试");

                var vm3 = _inertControlAutoTestVm3;
                if (vm3 != null)
                {
                    _inertControlDiscreteInputPrimaryValues = Enumerable.Range(0, 17)
                        .Select(i => vm3.Items.Count > i && vm3.Items[i] != null ? vm3.Items[i].PrimaryActualResult : "--")
                        .ToArray();
                    _inertControlDiscreteInputPrimaryResults = Enumerable.Range(0, 17)
                        .Select(i => vm3.Items.Count > i && vm3.Items[i] != null ? vm3.Items[i].PrimaryResult : "--")
                        .ToArray();
                    _inertControlDiscreteInputSecondaryValues = Enumerable.Range(0, 17)
                        .Select(i => vm3.Items.Count > i && vm3.Items[i] != null ? vm3.Items[i].SecondaryActualResult : "--")
                        .ToArray();
                    _inertControlDiscreteInputSecondaryResults = Enumerable.Range(0, 17)
                        .Select(i => vm3.Items.Count > i && vm3.Items[i] != null ? vm3.Items[i].SecondaryResult : "--")
                        .ToArray();
                    _inertControlDiscreteInputOverallResult = vm3.OverallResult;
                }

                _inertControlDiscreteOutputSelected = IsSingleBoardStepSelected("控制板离散输出模块测试");
                _inertControlDiscreteOutputExecuted = DidSingleBoardStepExecute("控制板离散输出模块测试");

                var vm4 = _inertControlAutoTestVm4;
                if (vm4 != null)
                {
                    _inertControlDiscreteOutputHighValues = Enumerable.Range(0, 7)
                        .Select(i => vm4.Items.Count > i && vm4.Items[i] != null ? vm4.Items[i].HighMeasuredText : "--")
                        .ToArray();
                    _inertControlDiscreteOutputHighResults = Enumerable.Range(0, 7)
                        .Select(i => vm4.Items.Count > i && vm4.Items[i] != null ? vm4.Items[i].HighResult : "--")
                        .ToArray();
                    _inertControlDiscreteOutputLowValues = Enumerable.Range(0, 7)
                        .Select(i => vm4.Items.Count > i && vm4.Items[i] != null ? vm4.Items[i].LowMeasuredText : "--")
                        .ToArray();
                    _inertControlDiscreteOutputLowResults = Enumerable.Range(0, 7)
                        .Select(i => vm4.Items.Count > i && vm4.Items[i] != null ? vm4.Items[i].LowResult : "--")
                        .ToArray();
                    _inertControlDiscreteOutputOverallResult = vm4.OverallResult;
                }

                _inertControlTempSensorSelected = IsSingleBoardStepSelected("温度传感器信号采集测试");
                _inertControlTempSensorExecuted = DidSingleBoardStepExecute("温度传感器信号采集测试");

                var vm5 = _inertControlAutoTestVm5;
                if (vm5 != null)
                {
                    // 温度传感器测试：4个传感器（PT500A, PT500B, PT1000A, PT1000B），每个测试4个点位
                    // 报表格式：点位1的4个传感器 + 点位2的4个传感器 + 点位3的4个传感器 + 点位4的4个传感器
                    // 传感器顺序：PT500A, PT500B, PT1000A, PT1000B
                    var sensorNames = new[] { "PT500A", "PT500B", "PT1000A", "PT1000B" };
                    var tempValues = new List<string>();
                    var tempResults = new List<string>();
                    
                    // 遍历4个点位（点位索引1-4）
                    for (int pointIndex = 1; pointIndex <= 4; pointIndex++)
                    {
                        // 每个点位添加4个传感器的数据
                        foreach (var sensorName in sensorNames)
                        {
                            var (measuredTemp, result) = vm5.GetPointTestResult(pointIndex, sensorName);
                            tempValues.Add(measuredTemp);
                            tempResults.Add(result);
                        }
                    }
                    
                    _inertControlTempSensorValues = tempValues.ToArray();
                    _inertControlTempSensorResults = tempResults.ToArray();
                    _inertControlTempSensorOverallResult = vm5.LastTestResult;
                }

                _inertControlPressureSensorSelected = IsSingleBoardStepSelected("压力传感器信号采集测试");
                _inertControlPressureSensorExecuted = DidSingleBoardStepExecute("压力传感器信号采集测试");

                var vm6 = _inertControlAutoTestVm6;
                if (vm6 != null)
                {
                    // 3个压力点：只填写压力值，不填电压
                    _inertControlPressureSensorValues = vm6.Items
                        .Select(item => item.MeasuredPressureText ?? "--")
                        .ToArray();
                    _inertControlPressureSensorResults = vm6.Items
                        .Select(item => item.Result ?? "--")
                        .ToArray();
                    _inertControlPressureSensorOverallResult = vm6.LastTestResult;
                }

                _inertControlOxygenSensorSelected = IsSingleBoardStepSelected("氧气传感器信号采集测试");
                _inertControlOxygenSensorExecuted = DidSingleBoardStepExecute("氧气传感器信号采集测试");

                var vm7 = _inertControlAutoTestVm7;
                if (vm7 != null)
                {
                    // 氧气传感器测试：10个测试项（5个浓度 + 5个压力）
                    // 跳过第一个项（expectedValueText 是 "--"），取第2、3、4个项（索引1、2、3）
                    var concentrationItems = vm7.Items.Where(item => item.SensorName == "氧气浓度").Skip(1).Take(3).ToList();
                    var pressureItems = vm7.Items.Where(item => item.SensorName == "氧气压力").Skip(1).Take(3).ToList();

                    _inertControlOxygenConcentrationValues = concentrationItems
                        .Select(item => item.MeasuredValueText ?? "--")
                        .ToArray();
                    _inertControlOxygenConcentrationResults = concentrationItems
                        .Select(item => item.Result ?? "--")
                        .ToArray();

                    _inertControlOxygenPressureValues = pressureItems
                        .Select(item => item.MeasuredValueText ?? "--")
                        .ToArray();
                    _inertControlOxygenPressureResults = pressureItems
                        .Select(item => item.Result ?? "--")
                        .ToArray();

                    _inertControlOxygenSensorOverallResult = vm7.LastTestResult;
                }

                _inertControlTcvMotorSelected = IsSingleBoardStepSelected("TCV电机驱动测试");
                _inertControlTcvMotorExecuted = DidSingleBoardStepExecute("TCV电机驱动测试");

                var vm8 = _inertControlAutoTestVm8;
                if (vm8 != null)
                {
                    // TCV电机测试：4个测试项（正转500Hz、正转1000Hz、反转500Hz、反转1000Hz）
                    // 测试值和测试结果都填写 PASS/FAIL
                    _inertControlTcvMotorResults = vm8.Results
                        .Select(item => item.Result ?? "--")
                        .ToArray();
                    _inertControlTcvMotorOverallResult = vm8.OverallResult;
                }
            }
            else if (string.Equals(boardType, "惰化模拟板", StringComparison.OrdinalIgnoreCase))
            {
                _inertSimPowerImpedanceSelected = IsSingleBoardStepSelected("电源阻抗测试");
                _inertSimPowerImpedanceExecuted = DidSingleBoardStepExecute("电源阻抗测试");
                _inertSimSecondaryTertiarySelected = IsSingleBoardStepSelected("二次、三次电源测试");
                _inertSimSecondaryTertiaryExecuted = DidSingleBoardStepExecute("二次、三次电源测试");
                _inertSimOverTempSelected = IsSingleBoardStepSelected("超温切断模块电路测试");
                _inertSimOverTempExecuted = DidSingleBoardStepExecute("超温切断模块电路测试");
                _inertSimLatchSelected = IsSingleBoardStepSelected("锁存模块电路测试");
                _inertSimLatchExecuted = DidSingleBoardStepExecute("锁存模块电路测试");

                var vm1 = _inertSimulationAutoTestVm1;
                if (vm1 != null)
                {
                    _inertSimPowerImpedanceValues = Enumerable.Range(0, 7)
                        .Select(i => vm1.Items.Count > i && vm1.Items[i] != null ? vm1.Items[i].ImpedanceText : "--")
                        .ToArray();
                    _inertSimPowerImpedanceResults = Enumerable.Range(0, 7)
                        .Select(i => vm1.Items.Count > i && vm1.Items[i] != null ? vm1.Items[i].Result : "--")
                        .ToArray();
                    _inertSimPowerImpedanceOverallResult = vm1.OverallResult;
                }

                var vm2 = _inertSimulationAutoTestVm2;
                if (vm2 != null)
                {
                    _inertSimSecondaryTertiaryValues = Enumerable.Range(0, 4)
                        .Select(i => vm2.Items.Count > i && vm2.Items[i] != null ? vm2.Items[i].VoltageText : "--")
                        .ToArray();
                    _inertSimSecondaryTertiaryResults = Enumerable.Range(0, 4)
                        .Select(i => vm2.Items.Count > i && vm2.Items[i] != null ? vm2.Items[i].Result : "--")
                        .ToArray();
                    _inertSimSecondaryTertiaryOverallResult = vm2.OverallResult;
                }

                var vm3 = _inertSimulationAutoTestVm3;
                if (vm3 != null)
                {
                    // 报表(超温切断模块电路测试)行序：
                    // PT500A首次超温触发阻值/温度
                    // PT500A设定首次超温触发阻值时 J31/J11/J12
                    // PT1000A首次超温触发阻值/温度
                    // PT1000A设定首次超温触发阻值时 J32/J13/J14
                    var values = new List<string>();
                    var results = new List<string>();

                    var pt500 = vm3.Items.Count > 0 ? vm3.Items[0] : null;
                    var pt1000 = vm3.Items.Count > 1 ? vm3.Items[1] : null;

                    values.Add(pt500?.FirstOverTempReportText ?? "--");
                    results.Add(pt500?.FirstOverTempTriggerResult ?? "--");

                    values.Add(pt500?.Checks != null && pt500.Checks.Count > 0 ? pt500.Checks[0]?.VoltageText ?? "--" : "--");
                    results.Add(pt500?.Checks != null && pt500.Checks.Count > 0 ? pt500.Checks[0]?.Result ?? "--" : "--");
                    values.Add(pt500?.Checks != null && pt500.Checks.Count > 1 ? pt500.Checks[1]?.VoltageText ?? "--" : "--");
                    results.Add(pt500?.Checks != null && pt500.Checks.Count > 1 ? pt500.Checks[1]?.Result ?? "--" : "--");
                    values.Add(pt500?.Checks != null && pt500.Checks.Count > 2 ? pt500.Checks[2]?.VoltageText ?? "--" : "--");
                    results.Add(pt500?.Checks != null && pt500.Checks.Count > 2 ? pt500.Checks[2]?.Result ?? "--" : "--");

                    values.Add(pt1000?.FirstOverTempReportText ?? "--");
                    results.Add(pt1000?.FirstOverTempTriggerResult ?? "--");

                    values.Add(pt1000?.Checks != null && pt1000.Checks.Count > 0 ? pt1000.Checks[0]?.VoltageText ?? "--" : "--");
                    results.Add(pt1000?.Checks != null && pt1000.Checks.Count > 0 ? pt1000.Checks[0]?.Result ?? "--" : "--");
                    values.Add(pt1000?.Checks != null && pt1000.Checks.Count > 1 ? pt1000.Checks[1]?.VoltageText ?? "--" : "--");
                    results.Add(pt1000?.Checks != null && pt1000.Checks.Count > 1 ? pt1000.Checks[1]?.Result ?? "--" : "--");
                    values.Add(pt1000?.Checks != null && pt1000.Checks.Count > 2 ? pt1000.Checks[2]?.VoltageText ?? "--" : "--");
                    results.Add(pt1000?.Checks != null && pt1000.Checks.Count > 2 ? pt1000.Checks[2]?.Result ?? "--" : "--");

                    _inertSimOverTempValues = values.ToArray();
                    _inertSimOverTempResults = results.ToArray();
                    _inertSimOverTempOverallResult = vm3.OverallResult;
                }

                var vm4 = _inertSimulationAutoTestVm4;
                if (vm4 != null)
                {
                    var steps = new List<MeasureControl.ViewModels.SingleBoardTest.InertController.LatchModuleCircuitTestViewModel.LatchStepViewModel>();
                    foreach (var item in vm4.Items)
                    {
                        if (item?.Steps != null)
                        {
                            foreach (var step in item.Steps)
                            {
                                steps.Add(step);
                            }
                        }
                    }
                    _inertSimLatchValues = Enumerable.Range(0, 6)
                        .Select(i => steps.Count > i && steps[i] != null ? steps[i].VoltageText : "--")
                        .ToArray();
                    _inertSimLatchResults = Enumerable.Range(0, 6)
                        .Select(i => steps.Count > i && steps[i] != null ? steps[i].Result : "--")
                        .ToArray();
                    _inertSimLatchOverallResult = vm4.OverallResult;
                }
            }
        }

        private sealed class SingleBoardExcelReportConfig
        {
            public string TemplateFileName { get; set; }
            public string OutputFolderName { get; set; }
            public string FileNamePrefix { get; set; }
            public Action<string> FillAction { get; set; }
        }

        private SingleBoardExcelReportConfig GetSingleBoardExcelReportConfig(string boardType)
        {
            switch (boardType?.Trim())
            {
                case "液压单板":
                    return new SingleBoardExcelReportConfig
                    {
                        TemplateFileName = "液压测试报表模板.xlsx",
                        OutputFolderName = "TestResults",
                        FileNamePrefix = "液压测试",
                        FillAction = FillHydraulicBoardExcelReportStable
                    };
                case "加放油单板":
                    return new SingleBoardExcelReportConfig
                    {
                        TemplateFileName = "加放油测试报表模板.xlsx",
                        OutputFolderName = "TestResults",
                        FileNamePrefix = "加放油测试",
                        FillAction = FillFuelBoardExcelReport
                    };
                case "惰化模拟板":
                    return new SingleBoardExcelReportConfig
                    {
                        TemplateFileName = "惰化模拟板测试报表.xlsx",
                        OutputFolderName = "TestResults",
                        FileNamePrefix = "惰化模拟板测试",
                        FillAction = FillInertSimulationBoardExcelReport
                    };
                case "惰化控制板":
                    return new SingleBoardExcelReportConfig
                    {
                        TemplateFileName = "惰化控制板测试报表.xlsx",
                        OutputFolderName = "TestResults",
                        FileNamePrefix = "惰化控制板测试",
                        FillAction = FillInertControlBoardExcelReport
                    };
                case "空气控制板":
                case "空气功率板":
                case "空气安全板":
                case "惰化单板":
                default:
                    return null;
            }
        }

        

        private void TryGenerateSingleBoardExcelReport(string boardName, string boardType)
        {
            var reportConfig = GetSingleBoardExcelReportConfig(boardType);
            if (reportConfig == null)
            {
                return;
            }

          

            try
            {
                string ResolveTemplatePath()
                {
                    var basePath = AppDomain.CurrentDomain.BaseDirectory;
                    var candidates = new[]
                    {
                        System.IO.Path.Combine(basePath, "Projects", reportConfig.TemplateFileName),
                        System.IO.Path.Combine(basePath, "Resources", "ReportTemplates", reportConfig.TemplateFileName),
                        System.IO.Path.Combine(basePath, reportConfig.TemplateFileName)
                    };
                    foreach (var c in candidates)
                    {
                        if (File.Exists(c))
                            return c;
                    }
                    return candidates[0];
                }

                var templatePath = ResolveTemplatePath();
                if (!File.Exists(templatePath))
                {
                    AppendSingleBoardReportLine($"REPORT | TEMPLATE_NOT_FOUND | {templatePath}");
                    return;
                }

                var baseDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), reportConfig.OutputFolderName);
                Directory.CreateDirectory(baseDir);

                var reportPath = System.IO.Path.Combine(baseDir, $"{reportConfig.FileNamePrefix}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
                File.Copy(templatePath, reportPath, true);

                if (File.Exists(reportPath))
                {
                    var fileInfo = new FileInfo(reportPath);
                    if (fileInfo.IsReadOnly)
                    {
                        fileInfo.IsReadOnly = false;
                    }
                    fileInfo.Attributes = FileAttributes.Normal;
                }

                try
                {
                    try
                    {
                        CaptureSingleBoardExcelReportSnapshot(boardType);
                    }
                    catch (Exception captureEx)
                    {
                        AppendSingleBoardReportLine($"REPORT | CAPTURE_EXCEPTION | {captureEx.GetType().Name} | {captureEx.Message}");
                    }

                    RunInSta(() => reportConfig.FillAction?.Invoke(reportPath));
                }
                catch
                {
                    throw;
                }

                _singleBoardAutoTestExcelReportPath = reportPath;
                AppendSingleBoardReportLine($"REPORT | EXCEL_CREATED | {reportPath}");
            }
            catch (Exception ex)
            {
                AppendSingleBoardReportLine($"REPORT | EXCEL_CREATE_FAILED | {ex.GetType().Name} | {ex.Message}");
            }
        }

        private void FillHydraulicBoardExcelReportStable(string reportPath)
        {
            var vm61 = _hydraulicAutoTestVm61 ?? ContainerLocator.Container.Resolve<HC_6_1ViewModel>();
            var vm62ChannelId = _hydraulicAutoTestVm62ChannelId ?? ContainerLocator.Container.Resolve<HC_6_2ViewModel>();
            var vm62 = _hydraulicAutoTestVm62 ?? ContainerLocator.Container.Resolve<HC_6_3ViewModel>();
            var vm63 = _hydraulicAutoTestVm63 ?? ContainerLocator.Container.Resolve<HC_6_4ViewModel>();
            var vm64 = _hydraulicAutoTestVm64 ?? ContainerLocator.Container.Resolve<HC_6_5ViewModel>();
            var vm65 = _hydraulicAutoTestVm65 ?? ContainerLocator.Container.Resolve<HC_6_6ViewModel>();
            var vm66 = _hydraulicAutoTestVm66 ?? ContainerLocator.Container.Resolve<HC_6_7ViewModel>();
            var vm67 = _hydraulicAutoTestVm67 ?? ContainerLocator.Container.Resolve<HC_6_8ViewModel>();
            var vm68 = _hydraulicAutoTestVm68 ?? ContainerLocator.Container.Resolve<HC_6_9ViewModel>();
            var vm69 = _hydraulicAutoTestVm69 ?? ContainerLocator.Container.Resolve<HC_6_10ViewModel>();
            if (vm61 == null && vm62ChannelId == null && vm62 == null && vm63 == null && vm64 == null && vm65 == null && vm66 == null && vm67 == null && vm68 == null && vm69 == null)
            {
                return;
            }

            Type excelType = null;
            object excelApp = null;
            object workbooks = null;
            object workbook = null;
            object sheet = null;
            object cells = null;
            object range = null;

            try
            {
                OleMessageFilter.Register();

                excelType = Type.GetTypeFromProgID("Excel.Application");
                if (excelType == null)
                {
                    throw new InvalidOperationException("未检测到 Excel COM 组件，无法写入报表模板。");
                }

                excelApp = Activator.CreateInstance(excelType);
                excelType.InvokeMember("Visible", BindingFlags.SetProperty, null, excelApp, new object[] { false });
                excelType.InvokeMember("DisplayAlerts", BindingFlags.SetProperty, null, excelApp, new object[] { false });

                workbooks = excelType.InvokeMember("Workbooks", BindingFlags.GetProperty, null, excelApp, null);
                workbook = workbooks.GetType().InvokeMember("Open", BindingFlags.InvokeMethod, null, workbooks, new object[] { reportPath });
                sheet = workbook.GetType().InvokeMember("Worksheets", BindingFlags.GetProperty, null, workbook, null);
                sheet = sheet.GetType().InvokeMember("Item", BindingFlags.GetProperty, null, sheet, new object[] { 1 });
                cells = sheet.GetType().InvokeMember("Cells", BindingFlags.GetProperty, null, sheet, null);

                if (vm61 != null)
                {
                    if (IsSingleBoardStepSelected("电源阻抗测试"))
                    {
                        var hc61Executed = DidSingleBoardStepExecute("电源阻抗测试");
                        SetExcelCellValue(cells, 3, 5, hc61Executed ? vm61.Resistance14Text : "--");
                        SetExcelCellValue(cells, 4, 5, hc61Executed ? vm61.Resistance182Text : "--");

                        SetExcelCellValue(cells, 3, 6, hc61Executed ? (vm61.IsResistance14Pass ? "合格" : "不合格") : "--");
                        SetExcelCellValue(cells, 4, 6, hc61Executed ? (vm61.IsResistance182Pass ? "合格" : "不合格") : "--");

                        range = sheet.GetType().InvokeMember("Range", BindingFlags.GetProperty, null, sheet, new object[] { "G3:G4" });
                        range.GetType().InvokeMember("Merge", BindingFlags.InvokeMethod, null, range, null);
                        var hc61Result = GetSingleBoardStepResult("电源阻抗测试", vm61.CurrentTestResult);
                        range.GetType().InvokeMember("Value", BindingFlags.SetProperty, null, range, new object[] { hc61Result });
                        ReleaseComObject(range);
                        range = null;
                    }
                    else
                    {
                        FillUntestedCells(cells, 3, 5, 4);
                        FillUntestedCells(cells, 3, 6, 4);
                        range = sheet.GetType().InvokeMember("Range", BindingFlags.GetProperty, null, sheet, new object[] { "G3:G4" });
                        range.GetType().InvokeMember("Merge", BindingFlags.InvokeMethod, null, range, null);
                        range.GetType().InvokeMember("Value", BindingFlags.SetProperty, null, range, new object[] { "未测试" });
                        ReleaseComObject(range);
                        range = null;
                    }
                }

                if (vm62ChannelId != null)
                {
                    if (IsSingleBoardStepSelected("通道ID测试"))
                    {
                        var hc62ChannelIdExecuted = DidSingleBoardStepExecute("通道ID测试");
                        SetExcelCellValue(cells, 5, 5, hc62ChannelIdExecuted ? vm62ChannelId.Resistance14Text : "--");
                        SetExcelCellValue(cells, 6, 5, hc62ChannelIdExecuted ? vm62ChannelId.Resistance182Text : "--");

                        SetExcelCellValue(cells, 5, 6, hc62ChannelIdExecuted ? (string.Equals(vm62ChannelId.Resistance14Text, "0x01", StringComparison.OrdinalIgnoreCase) ? "合格" : "不合格") : "--");
                        SetExcelCellValue(cells, 6, 6, hc62ChannelIdExecuted ? (string.Equals(vm62ChannelId.Resistance182Text, "0x02", StringComparison.OrdinalIgnoreCase) ? "合格" : "不合格") : "--");

                        range = sheet.GetType().InvokeMember("Range", BindingFlags.GetProperty, null, sheet, new object[] { "G5:G6" });
                        range.GetType().InvokeMember("Merge", BindingFlags.InvokeMethod, null, range, null);
                        var hc62ChannelIdResult = GetSingleBoardStepResult("通道ID测试", vm62ChannelId.CurrentTestResult);
                        range.GetType().InvokeMember("Value", BindingFlags.SetProperty, null, range, new object[] { hc62ChannelIdResult });
                        ReleaseComObject(range);
                        range = null;
                    }
                    else
                    {
                        FillUntestedCells(cells, 5, 5, 6);
                        FillUntestedCells(cells, 5, 6, 6);
                        range = sheet.GetType().InvokeMember("Range", BindingFlags.GetProperty, null, sheet, new object[] { "G5:G6" });
                        range.GetType().InvokeMember("Merge", BindingFlags.InvokeMethod, null, range, null);
                        range.GetType().InvokeMember("Value", BindingFlags.SetProperty, null, range, new object[] { "未测试" });
                        ReleaseComObject(range);
                        range = null;
                    }
                }

                if (vm62 != null)
                {
                    if (IsSingleBoardStepSelected("二次电源测试"))
                    {
                        var hc62Executed = DidSingleBoardStepExecute("二次电源测试");
                        SetExcelCellValue(cells, 7, 5, hc62Executed ? FormatNullableNumber(vm62.Voltage5VValue) : "--");
                        SetExcelCellValue(cells, 8, 5, hc62Executed ? FormatNullableNumber(vm62.Voltage15VValue) : "--");
                        SetExcelCellValue(cells, 9, 5, hc62Executed ? FormatNullableNumber(vm62.VoltageM15VValue) : "--");

                        SetExcelCellValue(cells, 7, 6, hc62Executed ? (vm62.IsVoltage5VPass ? "合格" : "不合格") : "--");
                        SetExcelCellValue(cells, 8, 6, hc62Executed ? (vm62.IsVoltage15VPass ? "合格" : "不合格") : "--");
                        SetExcelCellValue(cells, 9, 6, hc62Executed ? (vm62.IsVoltageM15VPass ? "合格" : "不合格") : "--");

                        range = sheet.GetType().InvokeMember("Range", BindingFlags.GetProperty, null, sheet, new object[] { "G7:G9" });
                        var hc62Result = GetSingleBoardStepResult("二次电源测试", vm62.CurrentTestResult);
                        range.GetType().InvokeMember("Value", BindingFlags.SetProperty, null, range, new object[] { hc62Result });
                        ReleaseComObject(range);
                        range = null;
                    }
                    else
                    {
                        FillUntestedCells(cells, 7, 5, 9);
                        FillUntestedCells(cells, 7, 6, 9);
                        range = sheet.GetType().InvokeMember("Range", BindingFlags.GetProperty, null, sheet, new object[] { "G7:G9" });
                        range.GetType().InvokeMember("Value", BindingFlags.SetProperty, null, range, new object[] { "未测试" });
                        ReleaseComObject(range);
                        range = null;
                    }
                }

                if (vm63 != null)
                {
                    if (IsSingleBoardStepSelected("温度采集测试"))
                    {
                        var hc63Executed = DidSingleBoardStepExecute("温度采集测试");
                        SetExcelCellValue(cells, 10, 5, hc63Executed ? FormatNullableNumber(vm63.Temp1Value) : "--");
                        SetExcelCellValue(cells, 11, 5, hc63Executed ? FormatNullableNumber(vm63.Temp1BValue) : "--");
                        SetExcelCellValue(cells, 12, 5, hc63Executed ? FormatNullableNumber(vm63.Temp2Value) : "--");
                        SetExcelCellValue(cells, 13, 5, hc63Executed ? FormatNullableNumber(vm63.Temp2BValue) : "--");
                        SetExcelCellValue(cells, 14, 5, hc63Executed ? FormatNullableNumber(vm63.Temp3Value) : "--");
                        SetExcelCellValue(cells, 15, 5, hc63Executed ? FormatNullableNumber(vm63.Temp3BValue) : "--");

                        SetExcelCellValue(cells, 10, 6, hc63Executed ? (vm63.IsTemp1Pass ? "合格" : "不合格") : "--");
                        SetExcelCellValue(cells, 11, 6, hc63Executed ? (vm63.IsTemp1BPass ? "合格" : "不合格") : "--");
                        SetExcelCellValue(cells, 12, 6, hc63Executed ? (vm63.IsTemp2Pass ? "合格" : "不合格") : "--");
                        SetExcelCellValue(cells, 13, 6, hc63Executed ? (vm63.IsTemp2BPass ? "合格" : "不合格") : "--");
                        SetExcelCellValue(cells, 14, 6, hc63Executed ? (vm63.IsTemp3Pass ? "合格" : "不合格") : "--");
                        SetExcelCellValue(cells, 15, 6, hc63Executed ? (vm63.IsTemp3BPass ? "合格" : "不合格") : "--");

                        range = sheet.GetType().InvokeMember("Range", BindingFlags.GetProperty, null, sheet, new object[] { "G10:G15" });
                        var hc63Result = GetSingleBoardStepResult("温度采集测试", vm63.CurrentTestResult);
                        range.GetType().InvokeMember("Value", BindingFlags.SetProperty, null, range, new object[] { hc63Result });
                        ReleaseComObject(range);
                        range = null;
                    }
                    else
                    {
                        FillUntestedCells(cells, 10, 5, 15);
                        FillUntestedCells(cells, 10, 6, 15);
                        range = sheet.GetType().InvokeMember("Range", BindingFlags.GetProperty, null, sheet, new object[] { "G10:G15" });
                        range.GetType().InvokeMember("Value", BindingFlags.SetProperty, null, range, new object[] { "未测试" });
                        ReleaseComObject(range);
                        range = null;
                    }
                }

                if (vm64 != null)
                {
                    if (IsSingleBoardStepSelected("压力传感器信号采集测试"))
                    {
                        var hc64Executed = DidSingleBoardStepExecute("压力传感器信号采集测试");
                        SetExcelCellValue(cells, 16, 5, hc64Executed ? FormatNullableNumber(vm64.PressurePoint1Sys1Value) : "--");
                        SetExcelCellValue(cells, 17, 5, hc64Executed ? FormatNullableNumber(vm64.PressurePoint1Sys2Value) : "--");
                        SetExcelCellValue(cells, 18, 5, hc64Executed ? FormatNullableNumber(vm64.PressurePoint1Sys3Value) : "--");
                        SetExcelCellValue(cells, 19, 5, hc64Executed ? FormatNullableNumber(vm64.PressurePoint2Sys1Value) : "--");
                        SetExcelCellValue(cells, 20, 5, hc64Executed ? FormatNullableNumber(vm64.PressurePoint2Sys2Value) : "--");
                        SetExcelCellValue(cells, 21, 5, hc64Executed ? FormatNullableNumber(vm64.PressurePoint2Sys3Value) : "--");
                        SetExcelCellValue(cells, 22, 5, hc64Executed ? FormatNullableNumber(vm64.PressurePoint3Sys1Value) : "--");
                        SetExcelCellValue(cells, 23, 5, hc64Executed ? FormatNullableNumber(vm64.PressurePoint3Sys2Value) : "--");
                        SetExcelCellValue(cells, 24, 5, hc64Executed ? FormatNullableNumber(vm64.PressurePoint3Sys3Value) : "--");

                        SetExcelCellValue(cells, 16, 6, hc64Executed ? (vm64.IsPressurePoint1Sys1Pass ? "合格" : "不合格") : "--");
                        SetExcelCellValue(cells, 17, 6, hc64Executed ? (vm64.IsPressurePoint1Sys2Pass ? "合格" : "不合格") : "--");
                        SetExcelCellValue(cells, 18, 6, hc64Executed ? (vm64.IsPressurePoint1Sys3Pass ? "合格" : "不合格") : "--");
                        SetExcelCellValue(cells, 19, 6, hc64Executed ? (vm64.IsPressurePoint2Sys1Pass ? "合格" : "不合格") : "--");
                        SetExcelCellValue(cells, 20, 6, hc64Executed ? (vm64.IsPressurePoint2Sys2Pass ? "合格" : "不合格") : "--");
                        SetExcelCellValue(cells, 21, 6, hc64Executed ? (vm64.IsPressurePoint2Sys3Pass ? "合格" : "不合格") : "--");
                        SetExcelCellValue(cells, 22, 6, hc64Executed ? (vm64.IsPressurePoint3Sys1Pass ? "合格" : "不合格") : "--");
                        SetExcelCellValue(cells, 23, 6, hc64Executed ? (vm64.IsPressurePoint3Sys2Pass ? "合格" : "不合格") : "--");
                        SetExcelCellValue(cells, 24, 6, hc64Executed ? (vm64.IsPressurePoint3Sys3Pass ? "合格" : "不合格") : "--");

                        range = sheet.GetType().InvokeMember("Range", BindingFlags.GetProperty, null, sheet, new object[] { "G16:G24" });
                        var hc64Result = GetSingleBoardStepResult("压力传感器信号采集测试", vm64.CurrentTestResult);
                        range.GetType().InvokeMember("Value", BindingFlags.SetProperty, null, range, new object[] { hc64Result });
                        ReleaseComObject(range);
                        range = null;
                    }
                    else
                    {
                        FillUntestedCells(cells, 16, 5, 24);
                        FillUntestedCells(cells, 16, 6, 24);
                        range = sheet.GetType().InvokeMember("Range", BindingFlags.GetProperty, null, sheet, new object[] { "G16:G24" });
                        range.GetType().InvokeMember("Value", BindingFlags.SetProperty, null, range, new object[] { "未测试" });
                        ReleaseComObject(range);
                        range = null;
                    }
                }

                if (vm65 != null)
                {
                    if (IsSingleBoardStepSelected("压差传感器信号采集测试"))
                    {
                        var hc65Executed = DidSingleBoardStepExecute("压差传感器信号采集测试");
                        SetExcelCellValue(cells, 25, 5, hc65Executed ? FormatNullableNumber(vm65.DptEdp24mAValue) : "--");
                        SetExcelCellValue(cells, 26, 5, hc65Executed ? FormatNullableNumber(vm65.DptEmp2B4mAValue) : "--");
                        SetExcelCellValue(cells, 27, 5, hc65Executed ? FormatNullableNumber(vm65.DptEmp3B4mAValue) : "--");
                        SetExcelCellValue(cells, 28, 5, hc65Executed ? FormatNullableNumber(vm65.DptSys14mAValue) : "--");
                        SetExcelCellValue(cells, 29, 5, hc65Executed ? FormatNullableNumber(vm65.DptSys24mAValue) : "--");
                        SetExcelCellValue(cells, 30, 5, hc65Executed ? FormatNullableNumber(vm65.DptSys34mAValue) : "--");

                        SetExcelCellValue(cells, 31, 5, hc65Executed ? FormatNullableNumber(vm65.DptEdp2A20mAValue) : "--");
                        SetExcelCellValue(cells, 32, 5, hc65Executed ? FormatNullableNumber(vm65.DptEmp2B20mAValue) : "--");
                        SetExcelCellValue(cells, 33, 5, hc65Executed ? FormatNullableNumber(vm65.DptEmp3B20mAValue) : "--");
                        SetExcelCellValue(cells, 34, 5, hc65Executed ? FormatNullableNumber(vm65.DptSys120mAValue) : "--");
                        SetExcelCellValue(cells, 35, 5, hc65Executed ? FormatNullableNumber(vm65.DptSys220mAValue) : "--");
                        SetExcelCellValue(cells, 36, 5, hc65Executed ? FormatNullableNumber(vm65.DptSys320mAValue) : "--");

                        SetExcelCellValue(cells, 37, 5, hc65Executed ? FormatNullableNumber(vm65.DptEdp2A10mAValue) : "--");
                        SetExcelCellValue(cells, 38, 5, hc65Executed ? FormatNullableNumber(vm65.DptEmp2B10mAValue) : "--");
                        SetExcelCellValue(cells, 39, 5, hc65Executed ? FormatNullableNumber(vm65.DptEmp3B10mAValue) : "--");
                        SetExcelCellValue(cells, 40, 5, hc65Executed ? FormatNullableNumber(vm65.DptSys110mAValue) : "--");
                        SetExcelCellValue(cells, 41, 5, hc65Executed ? FormatNullableNumber(vm65.DptSys210mAValue) : "--");
                        SetExcelCellValue(cells, 42, 5, hc65Executed ? FormatNullableNumber(vm65.DptSys310mAValue) : "--");

                        SetExcelCellValue(cells, 25, 6, hc65Executed ? (vm65.IsDptEdp24mAPass ? "合格" : "不合格") : "--");
                        SetExcelCellValue(cells, 26, 6, hc65Executed ? (vm65.IsDptEmp2B4mAPass ? "合格" : "不合格") : "--");
                        SetExcelCellValue(cells, 27, 6, hc65Executed ? (vm65.IsDptEmp3B4mAPass ? "合格" : "不合格") : "--");
                        SetExcelCellValue(cells, 28, 6, hc65Executed ? (vm65.IsDptSys14mAPass ? "合格" : "不合格") : "--");
                        SetExcelCellValue(cells, 29, 6, hc65Executed ? (vm65.IsDptSys24mAPass ? "合格" : "不合格") : "--");
                        SetExcelCellValue(cells, 30, 6, hc65Executed ? (vm65.IsDptSys34mAPass ? "合格" : "不合格") : "--");

                        SetExcelCellValue(cells, 31, 6, hc65Executed ? (vm65.IsDptEdp2A20mAPass ? "合格" : "不合格") : "--");
                        SetExcelCellValue(cells, 32, 6, hc65Executed ? (vm65.IsDptEmp2B20mAPass ? "合格" : "不合格") : "--");
                        SetExcelCellValue(cells, 33, 6, hc65Executed ? (vm65.IsDptEmp3B20mAPass ? "合格" : "不合格") : "--");
                        SetExcelCellValue(cells, 34, 6, hc65Executed ? (vm65.IsDptSys120mAPass ? "合格" : "不合格") : "--");
                        SetExcelCellValue(cells, 35, 6, hc65Executed ? (vm65.IsDptSys220mAPass ? "合格" : "不合格") : "--");
                        SetExcelCellValue(cells, 36, 6, hc65Executed ? (vm65.IsDptSys320mAPass ? "合格" : "不合格") : "--");

                        SetExcelCellValue(cells, 37, 6, hc65Executed ? (vm65.IsDptEdp2A10mAPass ? "合格" : "不合格") : "--");
                        SetExcelCellValue(cells, 38, 6, hc65Executed ? (vm65.IsDptEmp2B10mAPass ? "合格" : "不合格") : "--");
                        SetExcelCellValue(cells, 39, 6, hc65Executed ? (vm65.IsDptEmp3B10mAPass ? "合格" : "不合格") : "--");
                        SetExcelCellValue(cells, 40, 6, hc65Executed ? (vm65.IsDptSys110mAPass ? "合格" : "不合格") : "--");
                        SetExcelCellValue(cells, 41, 6, hc65Executed ? (vm65.IsDptSys210mAPass ? "合格" : "不合格") : "--");
                        SetExcelCellValue(cells, 42, 6, hc65Executed ? (vm65.IsDptSys310mAPass ? "合格" : "不合格") : "--");

                        range = sheet.GetType().InvokeMember("Range", BindingFlags.GetProperty, null, sheet, new object[] { "G25:G42" });
                        var hc65Result = GetSingleBoardStepResult("压差传感器信号采集测试", vm65.CurrentTestResult);
                        range.GetType().InvokeMember("Value", BindingFlags.SetProperty, null, range, new object[] { hc65Result });
                        ReleaseComObject(range);
                        range = null;
                    }
                    else
                    {
                        FillUntestedCells(cells, 25, 5, 42);
                        FillUntestedCells(cells, 25, 6, 42);
                        range = sheet.GetType().InvokeMember("Range", BindingFlags.GetProperty, null, sheet, new object[] { "G25:G42" });
                        range.GetType().InvokeMember("Value", BindingFlags.SetProperty, null, range, new object[] { "未测试" });
                        ReleaseComObject(range);
                        range = null;
                    }
                }

                if (vm66 != null)
                {
                    if (IsSingleBoardStepSelected("油量传感器信号采集测试"))
                    {
                        var hc66Executed = DidSingleBoardStepExecute("油量传感器信号采集测试");
                        SetExcelCellValue(cells, 43, 5, hc66Executed ? vm66.Pin3031FreqText : "--");
                        SetExcelCellValue(cells, 44, 5, hc66Executed ? vm66.Pin3334FreqText : "--");
                        SetExcelCellValue(cells, 45, 5, hc66Executed ? vm66.Pin3031VoltText : "--");
                        SetExcelCellValue(cells, 46, 5, hc66Executed ? vm66.Pin3334VoltText : "--");
                        SetExcelCellValue(cells, 47, 5, hc66Executed ? vm66.PointLowSys1Text : "--");
                        SetExcelCellValue(cells, 48, 5, hc66Executed ? vm66.PointLowSys2Text : "--");
                        SetExcelCellValue(cells, 49, 5, hc66Executed ? vm66.PointMidSys1Text : "--");
                        SetExcelCellValue(cells, 50, 5, hc66Executed ? vm66.PointMidSys2Text : "--");
                        SetExcelCellValue(cells, 51, 5, hc66Executed ? vm66.PointHighSys1Text : "--");
                        SetExcelCellValue(cells, 52, 5, hc66Executed ? vm66.PointHighSys2Text : "--");

                        SetExcelCellValue(cells, 43, 6, hc66Executed ? (vm66.IsPin3031Pass ? "合格" : "不合格") : "--");
                        SetExcelCellValue(cells, 44, 6, hc66Executed ? (vm66.IsPin3334Pass ? "合格" : "不合格") : "--");
                        SetExcelCellValue(cells, 45, 6, hc66Executed ? (vm66.IsPin3031Pass ? "合格" : "不合格") : "--");
                        SetExcelCellValue(cells, 46, 6, hc66Executed ? (vm66.IsPin3334Pass ? "合格" : "不合格") : "--");
                        SetExcelCellValue(cells, 47, 6, hc66Executed ? (vm66.IsPointLowSys1Pass ? "合格" : "不合格") : "--");
                        SetExcelCellValue(cells, 48, 6, hc66Executed ? (vm66.IsPointLowSys2Pass ? "合格" : "不合格") : "--");
                        SetExcelCellValue(cells, 49, 6, hc66Executed ? (vm66.IsPointMidSys1Pass ? "合格" : "不合格") : "--");
                        SetExcelCellValue(cells, 50, 6, hc66Executed ? (vm66.IsPointMidSys2Pass ? "合格" : "不合格") : "--");
                        SetExcelCellValue(cells, 51, 6, hc66Executed ? (vm66.IsPointHighSys1Pass ? "合格" : "不合格") : "--");
                        SetExcelCellValue(cells, 52, 6, hc66Executed ? (vm66.IsPointHighSys2Pass ? "合格" : "不合格") : "--");

                        range = sheet.GetType().InvokeMember("Range", BindingFlags.GetProperty, null, sheet, new object[] { "G43:G52" });
                        range.GetType().InvokeMember("Merge", BindingFlags.InvokeMethod, null, range, null);
                        var hc66Result = GetSingleBoardStepResult("油量传感器信号采集测试", vm66.CurrentTestResult);
                        range.GetType().InvokeMember("Value", BindingFlags.SetProperty, null, range, new object[] { hc66Result });
                        ReleaseComObject(range);
                        range = null;
                    }
                    else
                    {
                        FillUntestedCells(cells, 43, 5, 52);
                        FillUntestedCells(cells, 43, 6, 52);
                        range = sheet.GetType().InvokeMember("Range", BindingFlags.GetProperty, null, sheet, new object[] { "G43:G52" });
                        range.GetType().InvokeMember("Merge", BindingFlags.InvokeMethod, null, range, null);
                        range.GetType().InvokeMember("Value", BindingFlags.SetProperty, null, range, new object[] { "未测试" });
                        ReleaseComObject(range);
                        range = null;
                    }
                }

                if (vm67 != null)
                {
                    if (IsSingleBoardStepSelected("离散量采集测试"))
                    {
                        var hc67Executed = DidSingleBoardStepExecute("离散量采集测试");

                        var hc67GroundValues = new[]
                        {
                            vm67.GroundPin49Text, vm67.GroundPin50Text, vm67.GroundPin51Text, vm67.GroundPin52Text, vm67.GroundPin53Text, vm67.GroundPin54Text, vm67.GroundPin55Text,
                            vm67.GroundPin56Text, vm67.GroundPin57Text, vm67.GroundPin58Text, vm67.GroundPin59Text, vm67.GroundPin60Text, vm67.GroundPin61Text, vm67.GroundPin62Text,
                            vm67.GroundPin63Text, vm67.GroundPin89Text, vm67.GroundPin90Text, vm67.GroundPin91Text, vm67.GroundPin92Text, vm67.GroundPin93Text, vm67.GroundPin94Text,
                            vm67.GroundPin95Text, vm67.GroundPin96Text, vm67.GroundPin97Text, vm67.GroundPin98Text, vm67.GroundPin99Text, vm67.GroundPin100Text
                        };

                        var hc67GroundPasses = new[]
                        {
                            vm67.IsGroundPin49Pass, vm67.IsGroundPin50Pass, vm67.IsGroundPin51Pass, vm67.IsGroundPin52Pass, vm67.IsGroundPin53Pass, vm67.IsGroundPin54Pass, vm67.IsGroundPin55Pass,
                            vm67.IsGroundPin56Pass, vm67.IsGroundPin57Pass, vm67.IsGroundPin58Pass, vm67.IsGroundPin59Pass, vm67.IsGroundPin60Pass, vm67.IsGroundPin61Pass, vm67.IsGroundPin62Pass,
                            vm67.IsGroundPin63Pass, vm67.IsGroundPin89Pass, vm67.IsGroundPin90Pass, vm67.IsGroundPin91Pass, vm67.IsGroundPin92Pass, vm67.IsGroundPin93Pass, vm67.IsGroundPin94Pass,
                            vm67.IsGroundPin95Pass, vm67.IsGroundPin96Pass, vm67.IsGroundPin97Pass, vm67.IsGroundPin98Pass, vm67.IsGroundPin99Pass, vm67.IsGroundPin100Pass
                        };

                        var hc67OpenValues = new[]
                        {
                            vm67.OpenPin49Text, vm67.OpenPin50Text, vm67.OpenPin51Text, vm67.OpenPin52Text, vm67.OpenPin53Text, vm67.OpenPin54Text, vm67.OpenPin55Text,
                            vm67.OpenPin56Text, vm67.OpenPin57Text, vm67.OpenPin58Text, vm67.OpenPin59Text, vm67.OpenPin60Text, vm67.OpenPin61Text, vm67.OpenPin62Text,
                            vm67.OpenPin63Text, vm67.OpenPin89Text, vm67.OpenPin90Text, vm67.OpenPin91Text, vm67.OpenPin92Text, vm67.OpenPin93Text, vm67.OpenPin94Text,
                            vm67.OpenPin95Text, vm67.OpenPin96Text, vm67.OpenPin97Text, vm67.OpenPin98Text, vm67.OpenPin99Text, vm67.OpenPin100Text
                        };

                        var hc67OpenPasses = new[]
                        {
                            vm67.IsOpenPin49Pass, vm67.IsOpenPin50Pass, vm67.IsOpenPin51Pass, vm67.IsOpenPin52Pass, vm67.IsOpenPin53Pass, vm67.IsOpenPin54Pass, vm67.IsOpenPin55Pass,
                            vm67.IsOpenPin56Pass, vm67.IsOpenPin57Pass, vm67.IsOpenPin58Pass, vm67.IsOpenPin59Pass, vm67.IsOpenPin60Pass, vm67.IsOpenPin61Pass, vm67.IsOpenPin62Pass,
                            vm67.IsOpenPin63Pass, vm67.IsOpenPin89Pass, vm67.IsOpenPin90Pass, vm67.IsOpenPin91Pass, vm67.IsOpenPin92Pass, vm67.IsOpenPin93Pass, vm67.IsOpenPin94Pass,
                            vm67.IsOpenPin95Pass, vm67.IsOpenPin96Pass, vm67.IsOpenPin97Pass, vm67.IsOpenPin98Pass, vm67.IsOpenPin99Pass, vm67.IsOpenPin100Pass
                        };

                        for (var i = 0; i < hc67GroundValues.Length; i++)
                        {
                            var row = 53 + i;
                            SetExcelCellValue(cells, row, 5, hc67Executed ? hc67GroundValues[i] : "--");
                            SetExcelCellValue(cells, row, 6, hc67Executed ? (hc67GroundPasses[i] ? "合格" : "不合格") : "--");
                        }

                        for (var i = 0; i < hc67OpenValues.Length; i++)
                        {
                            var row = 80 + i;
                            SetExcelCellValue(cells, row, 5, hc67Executed ? hc67OpenValues[i] : "--");
                            SetExcelCellValue(cells, row, 6, hc67Executed ? (hc67OpenPasses[i] ? "合格" : "不合格") : "--");
                        }

                        range = sheet.GetType().InvokeMember("Range", BindingFlags.GetProperty, null, sheet, new object[] { "G53:G106" });
                        range.GetType().InvokeMember("Merge", BindingFlags.InvokeMethod, null, range, null);
                        var hc67Result = GetSingleBoardStepResult("离散量采集测试", vm67.CurrentTestResult);
                        range.GetType().InvokeMember("Value", BindingFlags.SetProperty, null, range, new object[] { hc67Result });
                        ReleaseComObject(range);
                        range = null;
                    }
                    else
                    {
                        FillUntestedCells(cells, 53, 5, 106);
                        FillUntestedCells(cells, 53, 6, 106);
                        range = sheet.GetType().InvokeMember("Range", BindingFlags.GetProperty, null, sheet, new object[] { "G53:G106" });
                        range.GetType().InvokeMember("Merge", BindingFlags.InvokeMethod, null, range, null);
                        range.GetType().InvokeMember("Value", BindingFlags.SetProperty, null, range, new object[] { "未测试" });
                        ReleaseComObject(range);
                        range = null;
                    }
                }

                if (vm68 != null)
                {
                    if (IsSingleBoardStepSelected("离散量输出测试"))
                    {
                        var hc68Executed = DidSingleBoardStepExecute("离散量输出测试");
                        var hc68OpenValues = new[]
                        {
                            vm68.OpenPin9Text, vm68.OpenPin10Text, vm68.OpenPin11Text, vm68.OpenPin12Text,
                            vm68.OpenPin13Text, vm68.OpenPin14Text, vm68.OpenPin15Text
                        };

                        var hc68OpenPasses = new[]
                        {
                            vm68.IsOpenPin9Pass, vm68.IsOpenPin10Pass, vm68.IsOpenPin11Pass, vm68.IsOpenPin12Pass,
                            vm68.IsOpenPin13Pass, vm68.IsOpenPin14Pass, vm68.IsOpenPin15Pass
                        };

                        var hc68CloseValues = new[]
                        {
                            vm68.ClosePin9Text, vm68.ClosePin10Text, vm68.ClosePin11Text, vm68.ClosePin12Text,
                            vm68.ClosePin13Text, vm68.ClosePin14Text, vm68.ClosePin15Text
                        };

                        var hc68ClosePasses = new[]
                        {
                            vm68.IsClosePin9Pass, vm68.IsClosePin10Pass, vm68.IsClosePin11Pass, vm68.IsClosePin12Pass,
                            vm68.IsClosePin13Pass, vm68.IsClosePin14Pass, vm68.IsClosePin15Pass
                        };

                        for (var i = 0; i < hc68OpenValues.Length; i++)
                        {
                            var row = 107 + i;
                            SetExcelCellValue(cells, row, 5, hc68Executed ? hc68OpenValues[i] : "--");
                            SetExcelCellValue(cells, row, 6, hc68Executed ? (hc68OpenPasses[i] ? "合格" : "不合格") : "--");
                        }

                        for (var i = 0; i < hc68CloseValues.Length; i++)
                        {
                            var row = 114 + i;
                            SetExcelCellValue(cells, row, 5, hc68Executed ? hc68CloseValues[i] : "--");
                            SetExcelCellValue(cells, row, 6, hc68Executed ? (hc68ClosePasses[i] ? "合格" : "不合格") : "--");
                        }

                        range = sheet.GetType().InvokeMember("Range", BindingFlags.GetProperty, null, sheet, new object[] { "G107:G120" });
                        range.GetType().InvokeMember("Merge", BindingFlags.InvokeMethod, null, range, null);
                        var hc68Result = GetSingleBoardStepResult("离散量输出测试", vm68.CurrentTestResult);
                        range.GetType().InvokeMember("Value", BindingFlags.SetProperty, null, range, new object[] { hc68Result });
                        ReleaseComObject(range);
                        range = null;
                    }
                    else
                    {
                        FillUntestedCells(cells, 107, 5, 120);
                        FillUntestedCells(cells, 107, 6, 120);
                        range = sheet.GetType().InvokeMember("Range", BindingFlags.GetProperty, null, sheet, new object[] { "G107:G120" });
                        range.GetType().InvokeMember("Merge", BindingFlags.InvokeMethod, null, range, null);
                        range.GetType().InvokeMember("Value", BindingFlags.SetProperty, null, range, new object[] { "未测试" });
                        ReleaseComObject(range);
                        range = null;
                    }
                }

                if (vm69 != null)
                {
                    if (IsSingleBoardStepSelected("通讯模块测试"))
                    {
                        var hc69Executed = DidSingleBoardStepExecute("通讯模块测试");
                        SetExcelCellValue(cells, 121, 5, hc69Executed ? vm69.TestBenchTank2Text : "--");
                        SetExcelCellValue(cells, 122, 5, hc69Executed ? vm69.ControlBoardTank1Text : "--");

                        SetExcelCellValue(cells, 121, 6, hc69Executed ? (string.Equals(vm69.TestBenchTank2Text, "0x00", StringComparison.OrdinalIgnoreCase) ? "合格" : "不合格") : "--");
                        SetExcelCellValue(cells, 122, 6, hc69Executed ? (double.TryParse(vm69.ControlBoardTank1Text, out var parsedTank1Qty) && Math.Abs(parsedTank1Qty - 30.0) < 0.5 ? "合格" : "不合格") : "--");

                        range = sheet.GetType().InvokeMember("Range", BindingFlags.GetProperty, null, sheet, new object[] { "G121:G122" });
                        range.GetType().InvokeMember("Merge", BindingFlags.InvokeMethod, null, range, null);
                        var hc69Result = GetSingleBoardStepResult("通讯模块测试", vm69.CurrentTestResult);
                        range.GetType().InvokeMember("Value", BindingFlags.SetProperty, null, range, new object[] { hc69Result });
                        ReleaseComObject(range);
                        range = null;
                    }
                    else
                    {
                        FillUntestedCells(cells, 121, 5, 122);
                        FillUntestedCells(cells, 121, 6, 122);
                        range = sheet.GetType().InvokeMember("Range", BindingFlags.GetProperty, null, sheet, new object[] { "G121:G122" });
                        range.GetType().InvokeMember("Merge", BindingFlags.InvokeMethod, null, range, null);
                        range.GetType().InvokeMember("Value", BindingFlags.SetProperty, null, range, new object[] { "未测试" });
                        ReleaseComObject(range);
                        range = null;
                    }
                }

                workbook.GetType().InvokeMember("Save", BindingFlags.InvokeMethod, null, workbook, null);
            }
            finally
            {
                TryInvoke(workbook, "Close", false);
                TryInvoke(excelApp, "Quit");
                ReleaseComObject(range);
                ReleaseComObject(cells);
                ReleaseComObject(sheet);
                ReleaseComObject(workbook);
                ReleaseComObject(workbooks);
                ReleaseComObject(excelApp);
                OleMessageFilter.Revoke();
            }
        }

        private void FillInertSimulationBoardExcelReport(string reportPath)
        {
            Type excelType = null;
            object excelApp = null;
            object workbooks = null;
            object workbook = null;
            object sheet = null;
            object cells = null;
            object range = null;

            try
            {
                OleMessageFilter.Register();

                excelType = Type.GetTypeFromProgID("Excel.Application");
                if (excelType == null)
                {
                    throw new InvalidOperationException("未检测到 Excel COM 组件，无法写入报表模板。");
                }

                excelApp = Activator.CreateInstance(excelType);
                excelType.InvokeMember("Visible", BindingFlags.SetProperty, null, excelApp, new object[] { false });
                excelType.InvokeMember("DisplayAlerts", BindingFlags.SetProperty, null, excelApp, new object[] { false });

                workbooks = excelType.InvokeMember("Workbooks", BindingFlags.GetProperty, null, excelApp, null);
                workbook = workbooks.GetType().InvokeMember("Open", BindingFlags.InvokeMethod, null, workbooks, new object[] { reportPath });
                sheet = workbook.GetType().InvokeMember("Worksheets", BindingFlags.GetProperty, null, workbook, null);
                sheet = sheet.GetType().InvokeMember("Item", BindingFlags.GetProperty, null, sheet, new object[] { 1 });
                cells = sheet.GetType().InvokeMember("Cells", BindingFlags.GetProperty, null, sheet, null);

                if (_inertSimPowerImpedanceSelected)
                {
                    for (var i = 0; i < 7; i++)
                    {
                        var row = 3 + i;
                        var value = _inertSimPowerImpedanceExecuted && _inertSimPowerImpedanceValues != null && _inertSimPowerImpedanceValues.Length > i
                            ? _inertSimPowerImpedanceValues[i]
                            : "--";
                        var result = _inertSimPowerImpedanceExecuted && _inertSimPowerImpedanceResults != null && _inertSimPowerImpedanceResults.Length > i
                            ? _inertSimPowerImpedanceResults[i]
                            : "--";

                        SetExcelCellValue(cells, row, 5, value);
                        SetExcelCellValue(cells, row, 6, result);
                    }

                    range = sheet.GetType().InvokeMember("Range", BindingFlags.GetProperty, null, sheet, new object[] { "G3:G9" });
                    range.GetType().InvokeMember("Merge", BindingFlags.InvokeMethod, null, range, null);
                    var overall = _inertSimPowerImpedanceExecuted
                        ? GetSingleBoardStepResult("电源阻抗测试", _inertSimPowerImpedanceOverallResult)
                        : "--";
                    range.GetType().InvokeMember("Value", BindingFlags.SetProperty, null, range, new object[] { overall });
                    ReleaseComObject(range);
                    range = null;
                }
                else
                {
                    FillUntestedCells(cells, 3, 5, 9);
                    FillUntestedCells(cells, 3, 6, 9);
                    range = sheet.GetType().InvokeMember("Range", BindingFlags.GetProperty, null, sheet, new object[] { "G3:G9" });
                    range.GetType().InvokeMember("Merge", BindingFlags.InvokeMethod, null, range, null);
                    range.GetType().InvokeMember("Value", BindingFlags.SetProperty, null, range, new object[] { "未测试" });
                    ReleaseComObject(range);
                    range = null;
                }

                if (_inertSimSecondaryTertiarySelected)
                {
                    for (var i = 0; i < 4; i++)
                    {
                        var row = 10 + i;
                        var value = _inertSimSecondaryTertiaryExecuted && _inertSimSecondaryTertiaryValues != null && _inertSimSecondaryTertiaryValues.Length > i
                            ? _inertSimSecondaryTertiaryValues[i]
                            : "--";
                        var result = _inertSimSecondaryTertiaryExecuted && _inertSimSecondaryTertiaryResults != null && _inertSimSecondaryTertiaryResults.Length > i
                            ? _inertSimSecondaryTertiaryResults[i]
                            : "--";

                        SetExcelCellValue(cells, row, 5, value);
                        SetExcelCellValue(cells, row, 6, result);
                    }

                    range = sheet.GetType().InvokeMember("Range", BindingFlags.GetProperty, null, sheet, new object[] { "G10:G13" });
                    range.GetType().InvokeMember("Merge", BindingFlags.InvokeMethod, null, range, null);
                    var overall = _inertSimSecondaryTertiaryExecuted
                        ? GetSingleBoardStepResult("二次、三次电源测试", _inertSimSecondaryTertiaryOverallResult)
                        : "--";
                    range.GetType().InvokeMember("Value", BindingFlags.SetProperty, null, range, new object[] { overall });
                    ReleaseComObject(range);
                    range = null;
                }
                else
                {
                    FillUntestedCells(cells, 10, 5, 13);
                    FillUntestedCells(cells, 10, 6, 13);
                    range = sheet.GetType().InvokeMember("Range", BindingFlags.GetProperty, null, sheet, new object[] { "G10:G13" });
                    range.GetType().InvokeMember("Merge", BindingFlags.InvokeMethod, null, range, null);
                    range.GetType().InvokeMember("Value", BindingFlags.SetProperty, null, range, new object[] { "未测试" });
                    ReleaseComObject(range);
                    range = null;
                }

                if (_inertSimOverTempSelected)
                {
                    for (var i = 0; i < 8; i++)
                    {
                        var row = 14 + i;
                        var value = _inertSimOverTempExecuted && _inertSimOverTempValues != null && _inertSimOverTempValues.Length > i
                            ? _inertSimOverTempValues[i]
                            : "--";
                        var result = _inertSimOverTempExecuted && _inertSimOverTempResults != null && _inertSimOverTempResults.Length > i
                            ? _inertSimOverTempResults[i]
                            : "--";

                        SetExcelCellValue(cells, row, 5, value);
                        SetExcelCellValue(cells, row, 6, result);
                    }

                    range = sheet.GetType().InvokeMember("Range", BindingFlags.GetProperty, null, sheet, new object[] { "G14:G21" });
                    range.GetType().InvokeMember("Merge", BindingFlags.InvokeMethod, null, range, null);
                    var overall = _inertSimOverTempExecuted
                        ? GetSingleBoardStepResult("超温切断模块电路测试", _inertSimOverTempOverallResult)
                        : "--";
                    range.GetType().InvokeMember("Value", BindingFlags.SetProperty, null, range, new object[] { overall });
                    ReleaseComObject(range);
                    range = null;
                }
                else
                {
                    FillUntestedCells(cells, 14, 5, 21);
                    FillUntestedCells(cells, 14, 6, 21);
                    range = sheet.GetType().InvokeMember("Range", BindingFlags.GetProperty, null, sheet, new object[] { "G14:G21" });
                    range.GetType().InvokeMember("Merge", BindingFlags.InvokeMethod, null, range, null);
                    range.GetType().InvokeMember("Value", BindingFlags.SetProperty, null, range, new object[] { "未测试" });
                    ReleaseComObject(range);
                    range = null;
                }

                if (_inertSimLatchSelected)
                {
                    for (var i = 0; i < 6; i++)
                    {
                        var row = 22 + i;
                        var value = _inertSimLatchExecuted && _inertSimLatchValues != null && _inertSimLatchValues.Length > i
                            ? _inertSimLatchValues[i]
                            : "--";
                        var result = _inertSimLatchExecuted && _inertSimLatchResults != null && _inertSimLatchResults.Length > i
                            ? _inertSimLatchResults[i]
                            : "--";

                        SetExcelCellValue(cells, row, 5, value);
                        SetExcelCellValue(cells, row, 6, result);
                    }

                    range = sheet.GetType().InvokeMember("Range", BindingFlags.GetProperty, null, sheet, new object[] { "G22:G27" });
                    range.GetType().InvokeMember("Merge", BindingFlags.InvokeMethod, null, range, null);
                    var overall = _inertSimLatchExecuted
                        ? GetSingleBoardStepResult("锁存模块电路测试", _inertSimLatchOverallResult)
                        : "--";
                    range.GetType().InvokeMember("Value", BindingFlags.SetProperty, null, range, new object[] { overall });
                    ReleaseComObject(range);
                    range = null;
                }
                else
                {
                    FillUntestedCells(cells, 22, 5, 27);
                    FillUntestedCells(cells, 22, 6, 27);
                    range = sheet.GetType().InvokeMember("Range", BindingFlags.GetProperty, null, sheet, new object[] { "G22:G27" });
                    range.GetType().InvokeMember("Merge", BindingFlags.InvokeMethod, null, range, null);
                    range.GetType().InvokeMember("Value", BindingFlags.SetProperty, null, range, new object[] { "未测试" });
                    ReleaseComObject(range);
                    range = null;
                }

                workbook.GetType().InvokeMember("Save", BindingFlags.InvokeMethod, null, workbook, null);
            }
            finally
            {
                TryInvoke(workbook, "Close", false);
                TryInvoke(excelApp, "Quit");
                ReleaseComObject(range);
                ReleaseComObject(cells);
                ReleaseComObject(sheet);
                ReleaseComObject(workbook);
                ReleaseComObject(workbooks);
                ReleaseComObject(excelApp);
                OleMessageFilter.Revoke();
            }
        }

        private void FillInertControlBoardExcelReport(string reportPath)
        {
            Type excelType = null;
            object excelApp = null;
            object workbooks = null;
            object workbook = null;
            object sheet = null;
            object cells = null;
            object range = null;

            try
            {
                OleMessageFilter.Register();

                excelType = Type.GetTypeFromProgID("Excel.Application");
                if (excelType == null)
                {
                    throw new InvalidOperationException("未检测到 Excel COM 组件，无法写入报表模板。");
                }

                excelApp = Activator.CreateInstance(excelType);
                excelType.InvokeMember("Visible", BindingFlags.SetProperty, null, excelApp, new object[] { false });
                excelType.InvokeMember("DisplayAlerts", BindingFlags.SetProperty, null, excelApp, new object[] { false });

                workbooks = excelType.InvokeMember("Workbooks", BindingFlags.GetProperty, null, excelApp, null);
                workbook = workbooks.GetType().InvokeMember("Open", BindingFlags.InvokeMethod, null, workbooks, new object[] { reportPath });
                sheet = workbook.GetType().InvokeMember("Worksheets", BindingFlags.GetProperty, null, workbook, null);
                sheet = sheet.GetType().InvokeMember("Item", BindingFlags.GetProperty, null, sheet, new object[] { 1 });
                cells = sheet.GetType().InvokeMember("Cells", BindingFlags.GetProperty, null, sheet, null);

                if (_inertControlPowerImpedanceSelected)
                {
                    for (var i = 0; i < 6; i++)
                    {
                        var row = 3 + i;
                        var value = _inertControlPowerImpedanceExecuted && _inertControlPowerImpedanceValues != null && _inertControlPowerImpedanceValues.Length > i
                            ? _inertControlPowerImpedanceValues[i]
                            : "--";
                        var result = _inertControlPowerImpedanceExecuted && _inertControlPowerImpedanceResults != null && _inertControlPowerImpedanceResults.Length > i
                            ? _inertControlPowerImpedanceResults[i]
                            : "--";

                        SetExcelCellValue(cells, row, 5, value);
                        SetExcelCellValue(cells, row, 6, result);
                    }

                    range = sheet.GetType().InvokeMember("Range", BindingFlags.GetProperty, null, sheet, new object[] { "G3:G8" });
                    range.GetType().InvokeMember("UnMerge", BindingFlags.InvokeMethod, null, range, null);
                    range.GetType().InvokeMember("Merge", BindingFlags.InvokeMethod, null, range, null);
                    var overall = _inertControlPowerImpedanceExecuted
                        ? GetSingleBoardStepResult("控制板电源阻抗测试", _inertControlPowerImpedanceOverallResult)
                        : "--";
                    range.GetType().InvokeMember("Value", BindingFlags.SetProperty, null, range, new object[] { overall });
                    ReleaseComObject(range);
                    range = null;
                }
                else
                {
                    FillUntestedCells(cells, 3, 5, 8);
                    FillUntestedCells(cells, 3, 6, 8);
                    range = sheet.GetType().InvokeMember("Range", BindingFlags.GetProperty, null, sheet, new object[] { "G3:G8" });
                    range.GetType().InvokeMember("UnMerge", BindingFlags.InvokeMethod, null, range, null);
                    range.GetType().InvokeMember("Merge", BindingFlags.InvokeMethod, null, range, null);
                    range.GetType().InvokeMember("Value", BindingFlags.SetProperty, null, range, new object[] { "未测试" });
                    ReleaseComObject(range);
                    range = null;
                }

                if (_inertControlSecondaryTertiarySelected)
                {
                    for (var i = 0; i < 5; i++)
                    {
                        var row = 9 + i;
                        var value = _inertControlSecondaryTertiaryExecuted && _inertControlSecondaryTertiaryValues != null && _inertControlSecondaryTertiaryValues.Length > i
                            ? _inertControlSecondaryTertiaryValues[i]
                            : "--";
                        var result = _inertControlSecondaryTertiaryExecuted && _inertControlSecondaryTertiaryResults != null && _inertControlSecondaryTertiaryResults.Length > i
                            ? _inertControlSecondaryTertiaryResults[i]
                            : "--";

                        SetExcelCellValue(cells, row, 5, value);
                        SetExcelCellValue(cells, row, 6, result);
                    }

                    range = sheet.GetType().InvokeMember("Range", BindingFlags.GetProperty, null, sheet, new object[] { "G9:G13" });
                    range.GetType().InvokeMember("UnMerge", BindingFlags.InvokeMethod, null, range, null);
                    range.GetType().InvokeMember("Merge", BindingFlags.InvokeMethod, null, range, null);
                    var overall = _inertControlSecondaryTertiaryExecuted
                        ? GetSingleBoardStepResult("控制板二次、三次电源测试", _inertControlSecondaryTertiaryOverallResult)
                        : "--";
                    range.GetType().InvokeMember("Value", BindingFlags.SetProperty, null, range, new object[] { overall });
                    ReleaseComObject(range);
                    range = null;
                }
                else
                {
                    FillUntestedCells(cells, 9, 5, 13);
                    FillUntestedCells(cells, 9, 6, 13);
                    range = sheet.GetType().InvokeMember("Range", BindingFlags.GetProperty, null, sheet, new object[] { "G9:G13" });
                    range.GetType().InvokeMember("UnMerge", BindingFlags.InvokeMethod, null, range, null);
                    range.GetType().InvokeMember("Merge", BindingFlags.InvokeMethod, null, range, null);
                    range.GetType().InvokeMember("Value", BindingFlags.SetProperty, null, range, new object[] { "未测试" });
                    ReleaseComObject(range);
                    range = null;
                }

                if (_inertControlDiscreteInputSelected)
                {
                    // 第一次测试（GND 和 28V）：14-30 行
                    for (var i = 0; i < 17; i++)
                    {
                        var row = 14 + i;
                        var value = _inertControlDiscreteInputExecuted && _inertControlDiscreteInputPrimaryValues != null && _inertControlDiscreteInputPrimaryValues.Length > i
                            ? _inertControlDiscreteInputPrimaryValues[i]
                            : "--";
                        var result = _inertControlDiscreteInputExecuted && _inertControlDiscreteInputPrimaryResults != null && _inertControlDiscreteInputPrimaryResults.Length > i
                            ? _inertControlDiscreteInputPrimaryResults[i]
                            : "--";

                        SetExcelCellValue(cells, row, 5, value);
                        SetExcelCellValue(cells, row, 6, result);
                    }

                    // 第二次测试（开路）：31-47 行
                    for (var i = 0; i < 17; i++)
                    {
                        var row = 31 + i;
                        var value = _inertControlDiscreteInputExecuted && _inertControlDiscreteInputSecondaryValues != null && _inertControlDiscreteInputSecondaryValues.Length > i
                            ? _inertControlDiscreteInputSecondaryValues[i]
                            : "--";
                        var result = _inertControlDiscreteInputExecuted && _inertControlDiscreteInputSecondaryResults != null && _inertControlDiscreteInputSecondaryResults.Length > i
                            ? _inertControlDiscreteInputSecondaryResults[i]
                            : "--";

                        SetExcelCellValue(cells, row, 5, value);
                        SetExcelCellValue(cells, row, 6, result);
                    }

                    range = sheet.GetType().InvokeMember("Range", BindingFlags.GetProperty, null, sheet, new object[] { "G14:G47" });
                    range.GetType().InvokeMember("UnMerge", BindingFlags.InvokeMethod, null, range, null);
                    range.GetType().InvokeMember("Merge", BindingFlags.InvokeMethod, null, range, null);
                    var overall = _inertControlDiscreteInputExecuted
                        ? GetSingleBoardStepResult("控制板离散输入模块测试", _inertControlDiscreteInputOverallResult)
                        : "--";
                    range.GetType().InvokeMember("Value", BindingFlags.SetProperty, null, range, new object[] { overall });
                    ReleaseComObject(range);
                    range = null;
                }
                else
                {
                    FillUntestedCells(cells, 14, 5, 47);
                    FillUntestedCells(cells, 14, 6, 47);
                    range = sheet.GetType().InvokeMember("Range", BindingFlags.GetProperty, null, sheet, new object[] { "G14:G47" });
                    range.GetType().InvokeMember("UnMerge", BindingFlags.InvokeMethod, null, range, null);
                    range.GetType().InvokeMember("Merge", BindingFlags.InvokeMethod, null, range, null);
                    range.GetType().InvokeMember("Value", BindingFlags.SetProperty, null, range, new object[] { "未测试" });
                    ReleaseComObject(range);
                    range = null;
                }

                if (_inertControlDiscreteOutputSelected)
                {
                    // High 测试：48-54 行
                    for (var i = 0; i < 7; i++)
                    {
                        var row = 48 + i;
                        var value = _inertControlDiscreteOutputExecuted && _inertControlDiscreteOutputHighValues != null && _inertControlDiscreteOutputHighValues.Length > i
                            ? _inertControlDiscreteOutputHighValues[i]
                            : "--";
                        var result = _inertControlDiscreteOutputExecuted && _inertControlDiscreteOutputHighResults != null && _inertControlDiscreteOutputHighResults.Length > i
                            ? _inertControlDiscreteOutputHighResults[i]
                            : "--";

                        SetExcelCellValue(cells, row, 5, value);
                        SetExcelCellValue(cells, row, 6, result);
                    }

                    // Low 测试：55-61 行
                    for (var i = 0; i < 7; i++)
                    {
                        var row = 55 + i;
                        var value = _inertControlDiscreteOutputExecuted && _inertControlDiscreteOutputLowValues != null && _inertControlDiscreteOutputLowValues.Length > i
                            ? _inertControlDiscreteOutputLowValues[i]
                            : "--";
                        var result = _inertControlDiscreteOutputExecuted && _inertControlDiscreteOutputLowResults != null && _inertControlDiscreteOutputLowResults.Length > i
                            ? _inertControlDiscreteOutputLowResults[i]
                            : "--";

                        SetExcelCellValue(cells, row, 5, value);
                        SetExcelCellValue(cells, row, 6, result);
                    }

                    range = sheet.GetType().InvokeMember("Range", BindingFlags.GetProperty, null, sheet, new object[] { "G48:G61" });
                    range.GetType().InvokeMember("UnMerge", BindingFlags.InvokeMethod, null, range, null);
                    range.GetType().InvokeMember("Merge", BindingFlags.InvokeMethod, null, range, null);
                    var overall = _inertControlDiscreteOutputExecuted
                        ? GetSingleBoardStepResult("控制板离散输出模块测试", _inertControlDiscreteOutputOverallResult)
                        : "--";
                    range.GetType().InvokeMember("Value", BindingFlags.SetProperty, null, range, new object[] { overall });
                    ReleaseComObject(range);
                    range = null;
                }
                else
                {
                    FillUntestedCells(cells, 48, 5, 61);
                    FillUntestedCells(cells, 48, 6, 61);
                    range = sheet.GetType().InvokeMember("Range", BindingFlags.GetProperty, null, sheet, new object[] { "G48:G61" });
                    range.GetType().InvokeMember("UnMerge", BindingFlags.InvokeMethod, null, range, null);
                    range.GetType().InvokeMember("Merge", BindingFlags.InvokeMethod, null, range, null);
                    range.GetType().InvokeMember("Value", BindingFlags.SetProperty, null, range, new object[] { "未测试" });
                    ReleaseComObject(range);
                    range = null;
                }

                if (_inertControlTempSensorSelected)
                {
                    // 温度传感器测试：62-77 行（4个点位 x 4个传感器 = 16行）
                    for (var i = 0; i < 16; i++)
                    {
                        var row = 62 + i;
                        var value = _inertControlTempSensorExecuted && _inertControlTempSensorValues != null && _inertControlTempSensorValues.Length > i
                            ? _inertControlTempSensorValues[i]
                            : "--";
                        var result = _inertControlTempSensorExecuted && _inertControlTempSensorResults != null && _inertControlTempSensorResults.Length > i
                            ? _inertControlTempSensorResults[i]
                            : "--";

                        SetExcelCellValue(cells, row, 5, value);
                        SetExcelCellValue(cells, row, 6, result);
                    }

                    range = sheet.GetType().InvokeMember("Range", BindingFlags.GetProperty, null, sheet, new object[] { "G62:G77" });
                    range.GetType().InvokeMember("UnMerge", BindingFlags.InvokeMethod, null, range, null);
                    range.GetType().InvokeMember("Merge", BindingFlags.InvokeMethod, null, range, null);
                    var overall = _inertControlTempSensorExecuted
                        ? GetSingleBoardStepResult("温度传感器信号采集测试", _inertControlTempSensorOverallResult)
                        : "--";
                    range.GetType().InvokeMember("Value", BindingFlags.SetProperty, null, range, new object[] { overall });
                    ReleaseComObject(range);
                    range = null;
                }
                else
                {
                    FillUntestedCells(cells, 62, 5, 77);
                    FillUntestedCells(cells, 62, 6, 77);
                    range = sheet.GetType().InvokeMember("Range", BindingFlags.GetProperty, null, sheet, new object[] { "G62:G77" });
                    range.GetType().InvokeMember("UnMerge", BindingFlags.InvokeMethod, null, range, null);
                    range.GetType().InvokeMember("Merge", BindingFlags.InvokeMethod, null, range, null);
                    range.GetType().InvokeMember("Value", BindingFlags.SetProperty, null, range, new object[] { "未测试" });
                    ReleaseComObject(range);
                    range = null;
                }

                if (_inertControlPressureSensorSelected)
                {
                    // 压力传感器测试：78-80 行（3个压力点）
                    for (var i = 0; i < 3; i++)
                    {
                        var row = 78 + i;
                        var value = _inertControlPressureSensorExecuted && _inertControlPressureSensorValues != null && _inertControlPressureSensorValues.Length > i
                            ? _inertControlPressureSensorValues[i]
                            : "--";
                        var result = _inertControlPressureSensorExecuted && _inertControlPressureSensorResults != null && _inertControlPressureSensorResults.Length > i
                            ? _inertControlPressureSensorResults[i]
                            : "--";

                        SetExcelCellValue(cells, row, 5, value);
                        SetExcelCellValue(cells, row, 6, result);
                    }

                    range = sheet.GetType().InvokeMember("Range", BindingFlags.GetProperty, null, sheet, new object[] { "G78:G80" });
                    range.GetType().InvokeMember("UnMerge", BindingFlags.InvokeMethod, null, range, null);
                    range.GetType().InvokeMember("Merge", BindingFlags.InvokeMethod, null, range, null);
                    var overall = _inertControlPressureSensorExecuted
                        ? GetSingleBoardStepResult("压力传感器信号采集测试", _inertControlPressureSensorOverallResult)
                        : "--";
                    range.GetType().InvokeMember("Value", BindingFlags.SetProperty, null, range, new object[] { overall });
                    ReleaseComObject(range);
                    range = null;
                }
                else
                {
                    FillUntestedCells(cells, 78, 5, 80);
                    FillUntestedCells(cells, 78, 6, 80);
                    range = sheet.GetType().InvokeMember("Range", BindingFlags.GetProperty, null, sheet, new object[] { "G78:G80" });
                    range.GetType().InvokeMember("UnMerge", BindingFlags.InvokeMethod, null, range, null);
                    range.GetType().InvokeMember("Merge", BindingFlags.InvokeMethod, null, range, null);
                    range.GetType().InvokeMember("Value", BindingFlags.SetProperty, null, range, new object[] { "未测试" });
                    ReleaseComObject(range);
                    range = null;
                }

                if (_inertControlOxygenSensorSelected)
                {
                    // 氧气传感器测试：81-86 行（3个浓度 + 3个压力 = 6行）
                    // 氧气浓度：81-83 行
                    for (var i = 0; i < 3; i++)
                    {
                        var row = 81 + i;
                        var value = _inertControlOxygenSensorExecuted && _inertControlOxygenConcentrationValues != null && _inertControlOxygenConcentrationValues.Length > i
                            ? _inertControlOxygenConcentrationValues[i]
                            : "--";
                        var result = _inertControlOxygenSensorExecuted && _inertControlOxygenConcentrationResults != null && _inertControlOxygenConcentrationResults.Length > i
                            ? _inertControlOxygenConcentrationResults[i]
                            : "--";

                        SetExcelCellValue(cells, row, 5, value);
                        SetExcelCellValue(cells, row, 6, result);
                    }

                    // 氧气压力：84-86 行
                    for (var i = 0; i < 3; i++)
                    {
                        var row = 84 + i;
                        var value = _inertControlOxygenSensorExecuted && _inertControlOxygenPressureValues != null && _inertControlOxygenPressureValues.Length > i
                            ? _inertControlOxygenPressureValues[i]
                            : "--";
                        var result = _inertControlOxygenSensorExecuted && _inertControlOxygenPressureResults != null && _inertControlOxygenPressureResults.Length > i
                            ? _inertControlOxygenPressureResults[i]
                            : "--";

                        SetExcelCellValue(cells, row, 5, value);
                        SetExcelCellValue(cells, row, 6, result);
                    }

                    range = sheet.GetType().InvokeMember("Range", BindingFlags.GetProperty, null, sheet, new object[] { "G81:G86" });
                    range.GetType().InvokeMember("UnMerge", BindingFlags.InvokeMethod, null, range, null);
                    range.GetType().InvokeMember("Merge", BindingFlags.InvokeMethod, null, range, null);
                    var overall = _inertControlOxygenSensorExecuted
                        ? GetSingleBoardStepResult("氧气传感器信号采集测试", _inertControlOxygenSensorOverallResult)
                        : "--";
                    range.GetType().InvokeMember("Value", BindingFlags.SetProperty, null, range, new object[] { overall });
                    ReleaseComObject(range);
                    range = null;
                }
                else
                {
                    FillUntestedCells(cells, 81, 5, 86);
                    FillUntestedCells(cells, 81, 6, 86);
                    range = sheet.GetType().InvokeMember("Range", BindingFlags.GetProperty, null, sheet, new object[] { "G81:G86" });
                    range.GetType().InvokeMember("UnMerge", BindingFlags.InvokeMethod, null, range, null);
                    range.GetType().InvokeMember("Merge", BindingFlags.InvokeMethod, null, range, null);
                    range.GetType().InvokeMember("Value", BindingFlags.SetProperty, null, range, new object[] { "未测试" });
                    ReleaseComObject(range);
                    range = null;
                }

                if (_inertControlTcvMotorSelected)
                {
                    // TCV电机测试：87-90 行（4个测试项）
                    // 测试值和测试结果都填写 PASS/FAIL
                    for (var i = 0; i < 4; i++)
                    {
                        var row = 87 + i;
                        var result = _inertControlTcvMotorExecuted && _inertControlTcvMotorResults != null && _inertControlTcvMotorResults.Length > i
                            ? _inertControlTcvMotorResults[i]
                            : "--";

                        // E列和F列都填写测试结果（PASS/FAIL）
                        SetExcelCellValue(cells, row, 5, result);
                        SetExcelCellValue(cells, row, 6, result);
                    }

                    range = sheet.GetType().InvokeMember("Range", BindingFlags.GetProperty, null, sheet, new object[] { "G87:G90" });
                    range.GetType().InvokeMember("UnMerge", BindingFlags.InvokeMethod, null, range, null);
                    range.GetType().InvokeMember("Merge", BindingFlags.InvokeMethod, null, range, null);
                    var overall = _inertControlTcvMotorExecuted
                        ? GetSingleBoardStepResult("TCV电机驱动测试", _inertControlTcvMotorOverallResult)
                        : "--";
                    range.GetType().InvokeMember("Value", BindingFlags.SetProperty, null, range, new object[] { overall });
                    ReleaseComObject(range);
                    range = null;
                }
                else
                {
                    FillUntestedCells(cells, 87, 5, 90);
                    FillUntestedCells(cells, 87, 6, 90);
                    range = sheet.GetType().InvokeMember("Range", BindingFlags.GetProperty, null, sheet, new object[] { "G87:G90" });
                    range.GetType().InvokeMember("UnMerge", BindingFlags.InvokeMethod, null, range, null);
                    range.GetType().InvokeMember("Merge", BindingFlags.InvokeMethod, null, range, null);
                    range.GetType().InvokeMember("Value", BindingFlags.SetProperty, null, range, new object[] { "未测试" });
                    ReleaseComObject(range);
                    range = null;
                }

            }
            finally
            {
                TryInvoke(workbook, "Save");
                TryInvoke(workbook, "Close", false);
                TryInvoke(excelApp, "Quit");
                ReleaseComObject(range);
                ReleaseComObject(cells);
                ReleaseComObject(sheet);
                ReleaseComObject(workbook);
                ReleaseComObject(workbooks);
                ReleaseComObject(excelApp);
                OleMessageFilter.Revoke();
            }
        }

        private static void RunInSta(Action action)
        {
            if (action == null)
            {
                return;
            }

            if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
            {
                action();
                return;
            }

            Exception captured = null;
            var t = new Thread(() =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    captured = ex;
                }
            });

            t.SetApartmentState(ApartmentState.STA);
            t.IsBackground = true;
            t.Start();
            t.Join();

            if (captured != null)
            {
                throw captured;
            }
        }

        private static string FormatNullableNumber(double? value)
        {
            return value.HasValue ? value.Value.ToString("0.###") : "--";
        }

        private bool IsSingleBoardStepSelected(string stepName)
        {
            return _selectedSingleBoardAutoTestItems == null || _selectedSingleBoardAutoTestItems.Contains(stepName);
        }

        private bool DidSingleBoardStepExecute(string stepName)
        {
            return _singleBoardAutoStepResults != null && _singleBoardAutoStepResults.ContainsKey(stepName);
        }

        private string GetSingleBoardStepResult(string stepName, string fallback)
        {
            if (_singleBoardAutoStepResults != null && _singleBoardAutoStepResults.TryGetValue(stepName, out var result))
            {
                return NormalizeResult(result);
            }

            return NormalizeResult(fallback);
        }

        private static void FillUntestedCells(object cells, int startRow, int column, int endRow)
        {
            for (var row = startRow; row <= endRow; row++)
            {
                SetExcelCellValue(cells, row, column, "--");
            }
        }

        private static void SetExcelCellValue(object cells, int row, int column, string value)
        {
            object cell = null;
            try
            {
                LogExcelDiagnostic($"EXCEL | CELL_VALUE_BEGIN | R{row}C{column} | VALUE={FormatExcelDebugValue(value)}");
                cell = cells.GetType().InvokeMember("Item", BindingFlags.GetProperty, null, cells, new object[] { row, column });
                LogExcelDiagnostic($"EXCEL | CELL_VALUE_GOT_CELL | R{row}C{column}");
                cell.GetType().InvokeMember("Value", BindingFlags.SetProperty, null, cell, new object[] { value });
                LogExcelDiagnostic($"EXCEL | CELL_VALUE_SUCCESS | R{row}C{column} | VALUE={FormatExcelDebugValue(value)}");
            }
            catch (Exception ex)
            {
                LogExcelDiagnostic($"EXCEL | CELL_VALUE_FAILED | R{row}C{column} | VALUE={FormatExcelDebugValue(value)} | {DescribeException(ex)}");
                throw;
            }
            finally
            {
                ReleaseComObject(cell);
            }
        }

        private static void SetExcelCellFontColor(object cells, int row, int column, int? oleColor)
        {
            object cell = null;
            object font = null;
            try
            {
                LogExcelDiagnostic($"EXCEL | CELL_FONT_BEGIN | R{row}C{column} | COLOR={(oleColor.HasValue ? oleColor.Value.ToString() : "<default>")}");
                cell = cells.GetType().InvokeMember("Item", BindingFlags.GetProperty, null, cells, new object[] { row, column });
                LogExcelDiagnostic($"EXCEL | CELL_FONT_GOT_CELL | R{row}C{column}");
                font = cell.GetType().InvokeMember("Font", BindingFlags.GetProperty, null, cell, null);
                LogExcelDiagnostic($"EXCEL | CELL_FONT_GOT_FONT | R{row}C{column}");
                if (oleColor.HasValue)
                {
                    font.GetType().InvokeMember("Color", BindingFlags.SetProperty, null, font, new object[] { oleColor.Value });
                    LogExcelDiagnostic($"EXCEL | CELL_FONT_SET_COLOR | R{row}C{column} | COLOR={oleColor.Value}");
                }
                else
                {
                    TryInvoke(font, "ColorIndex", -4105);
                    LogExcelDiagnostic($"EXCEL | CELL_FONT_RESET_COLOR | R{row}C{column}");
                }
            }
            catch (Exception ex)
            {
                LogExcelDiagnostic($"EXCEL | CELL_FONT_FAILED | R{row}C{column} | COLOR={(oleColor.HasValue ? oleColor.Value.ToString() : "<default>")} | {DescribeException(ex)}");
                throw;
            }
            finally
            {
                ReleaseComObject(font);
                ReleaseComObject(cell);
            }
        }

        private static void SetRangeFontColor(object range, int? oleColor)
        {
            if (range == null)
            {
                return;
            }

            object font = null;
            try
            {
                LogExcelDiagnostic($"EXCEL | RANGE_FONT_BEGIN | COLOR={(oleColor.HasValue ? oleColor.Value.ToString() : "<default>")}");
                font = range.GetType().InvokeMember("Font", BindingFlags.GetProperty, null, range, null);
                LogExcelDiagnostic("EXCEL | RANGE_FONT_GOT_FONT");
                if (oleColor.HasValue)
                {
                    font.GetType().InvokeMember("Color", BindingFlags.SetProperty, null, font, new object[] { oleColor.Value });
                    LogExcelDiagnostic($"EXCEL | RANGE_FONT_SET_COLOR | COLOR={oleColor.Value}");
                }
                else
                {
                    TryInvoke(font, "ColorIndex", -4105);
                    LogExcelDiagnostic("EXCEL | RANGE_FONT_RESET_COLOR");
                }
            }
            catch (Exception ex)
            {
                LogExcelDiagnostic($"EXCEL | RANGE_FONT_FAILED | COLOR={(oleColor.HasValue ? oleColor.Value.ToString() : "<default>")} | {DescribeException(ex)}");
                throw;
            }
            finally
            {
                ReleaseComObject(font);
            }
        }

        private static string FormatExcelDebugValue(string value)
        {
            if (value == null)
            {
                return "<null>";
            }

            return value
                .Replace("\r", "\\r")
                .Replace("\n", "\\n")
                .Replace("\t", "\\t");
        }

        private static string DescribeException(Exception ex)
        {
            if (ex == null)
            {
                return "<no exception>";
            }

            var parts = new List<string>
            {
                $"TYPE={ex.GetType().FullName}",
                $"MESSAGE={ex.Message}"
            };

            var inner = ex.InnerException;
            var level = 0;
            while (inner != null && level < 5)
            {
                parts.Add($"INNER{level}_TYPE={inner.GetType().FullName}");
                parts.Add($"INNER{level}_MESSAGE={inner.Message}");
                inner = inner.InnerException;
                level++;
            }

            return string.Join(" | ", parts);
        }

        private static void LogExcelDiagnostic(string message)
        {
            Debug.WriteLine(message);
        }

        private static void TryInvoke(object target, string methodName, params object[] args)
        {
            if (target == null)
            {
                return;
            }

            try
            {
                target.GetType().InvokeMember(methodName, BindingFlags.InvokeMethod, null, target, args);
                Console.WriteLine(methodName+"正常！！" );
            }
            catch
            {
                Console.WriteLine(methodName+"异常！！");
            }
        }

        private static void ReleaseComObject(object comObject)
        {
            if (comObject != null && Marshal.IsComObject(comObject))
            {
                try
                {
                    int refCount = 0;
                    do
                    {
                        refCount = Marshal.ReleaseComObject(comObject);
                    } while (refCount > 0);
                }
                catch
                {
                }
            }
        }

        private void FillFuelBoardExcelReport(string reportPath)
        {
            if (_fuelSnapshot17V == null && _fuelSnapshot28V == null && _fuelSnapshot322V == null)
                return;

            Type excelType = null;
            object excelApp = null;
            object workbooks = null;
            object workbook = null;
            object sheet = null;
            object cells = null;
            object range = null;

            try
            {
                OleMessageFilter.Register();

                excelType = Type.GetTypeFromProgID("Excel.Application");
                if (excelType == null)
                {
                    throw new InvalidOperationException("未检测到 Excel COM 组件，无法写入报表模板。");
                }

                excelApp = Activator.CreateInstance(excelType);
                excelType.InvokeMember("Visible", BindingFlags.SetProperty, null, excelApp, new object[] { false });
                excelType.InvokeMember("DisplayAlerts", BindingFlags.SetProperty, null, excelApp, new object[] { false });

                workbooks = excelType.InvokeMember("Workbooks", BindingFlags.GetProperty, null, excelApp, null);
                workbook = workbooks.GetType().InvokeMember("Open", BindingFlags.InvokeMethod, null, workbooks, new object[] { reportPath });
                sheet = workbook.GetType().InvokeMember("Worksheets", BindingFlags.GetProperty, null, workbook, null);
                sheet = sheet.GetType().InvokeMember("Item", BindingFlags.GetProperty, null, sheet, new object[] { 1 });
                cells = sheet.GetType().InvokeMember("Cells", BindingFlags.GetProperty, null, sheet, null);

                // 列定义：E=测试值(5), F=单项测试结果(6), G=测试结果(7)
                // 行定义（18V基准行）：电源阻抗3-6, 二次电源7, 低压告警8, 温度9,
                // 离散量采集10-13, 离散量输出14-30, RS422通信31-34, RS422自检35-36
                // 28V段行偏移+37, 32.2V段行偏移+74
                const int valueCol = 5;
                const int singleResultCol = 6;
                const int overallResultCol = 7;
                // const int timeCol = 8; // 模板已取消测试时间列

                string NormalizeFuelResult(string result)
                {
                    var r = (result ?? string.Empty).Trim();
                    if (string.Equals(r, "PASS", StringComparison.OrdinalIgnoreCase) || string.Equals(r, "合格", StringComparison.OrdinalIgnoreCase))
                        return "PASS";
                    if (string.Equals(r, "FAIL", StringComparison.OrdinalIgnoreCase) || string.Equals(r, "不合格", StringComparison.OrdinalIgnoreCase))
                        return "FAIL";
                    return string.IsNullOrWhiteSpace(r) || r == "--" ? "--" : r;
                }

                void SetOverall(int row, string value)
                {
                    object r = cells.GetType().InvokeMember("Item", BindingFlags.GetProperty, null, cells, new object[] { row, overallResultCol });
                    try { r.GetType().InvokeMember("Value", BindingFlags.SetProperty, null, r, new object[] { value }); }
                    finally { ReleaseComObject(r); }
                }

                // var testTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"); // 模板已取消测试时间列

                void FillSection(FuelRoundSnapshot snap, int ro)
                {
                    if (snap == null) return;

                    // vm1 - 电源阻抗测试 (行3-6)
                    if (IsSingleBoardStepSelected("电源阻抗测试"))
                    {
                        if (snap.Vm1_ImpA.HasValue || snap.Vm1_ResA != null)
                        {
                            SetExcelCellValue(cells, 3 + ro, valueCol, FormatNullableNumber(snap.Vm1_ImpA));
                            SetExcelCellValue(cells, 3 + ro, singleResultCol, NormalizeFuelResult(snap.Vm1_ResA));
                            SetExcelCellValue(cells, 4 + ro, valueCol, FormatNullableNumber(snap.Vm1_ImpB));
                            SetExcelCellValue(cells, 4 + ro, singleResultCol, NormalizeFuelResult(snap.Vm1_ResB));
                            SetExcelCellValue(cells, 5 + ro, valueCol, FormatNullableNumber(snap.Vm1_ImpC));
                            SetExcelCellValue(cells, 5 + ro, singleResultCol, NormalizeFuelResult(snap.Vm1_ResC));
                            SetExcelCellValue(cells, 6 + ro, valueCol, FormatNullableNumber(snap.Vm1_ImpD));
                            SetExcelCellValue(cells, 6 + ro, singleResultCol, NormalizeFuelResult(snap.Vm1_ResD));
                            SetOverall(3 + ro, NormalizeFuelResult(snap.Vm1_Overall));
                            // SetExcelCellValue(cells, 3 + ro, timeCol, testTime);
                        }
                        else
                        {
                            FillUntestedCells(cells, 3 + ro, valueCol, 6 + ro);
                            FillUntestedCells(cells, 3 + ro, singleResultCol, 6 + ro);
                            SetOverall(3 + ro, "未测试");
                        }
                    }
                    else
                    {
                        FillUntestedCells(cells, 3 + ro, valueCol, 6 + ro);
                        FillUntestedCells(cells, 3 + ro, singleResultCol, 6 + ro);
                        SetOverall(3 + ro, "未测试");
                    }

                    // vm2 - 二次电源测试 (行7)
                    if (IsSingleBoardStepSelected("二次电源测试"))
                    {
                        if (snap.Vm2_Voltage.HasValue || snap.Vm2_TestResult != null)
                        {
                            SetExcelCellValue(cells, 7 + ro, valueCol, FormatNullableNumber(snap.Vm2_Voltage));
                            SetExcelCellValue(cells, 7 + ro, singleResultCol, NormalizeFuelResult(snap.Vm2_TestResult));
                            SetOverall(7 + ro, NormalizeFuelResult(snap.Vm2_Overall));
                            // SetExcelCellValue(cells, 7 + ro, timeCol, testTime);
                        }
                        else
                        {
                            SetExcelCellValue(cells, 7 + ro, valueCol, "--");
                            SetExcelCellValue(cells, 7 + ro, singleResultCol, "--");
                            SetOverall(7 + ro, "未测试");
                        }
                    }
                    else
                    {
                        SetExcelCellValue(cells, 7 + ro, valueCol, "--");
                        SetExcelCellValue(cells, 7 + ro, singleResultCol, "--");
                        SetOverall(7 + ro, "未测试");
                    }

                    // vm3 - 低电压告警功能测试 (行8)
                    if (IsSingleBoardStepSelected("低电压告警功能测试"))
                    {
                        if (snap.Vm3_FlipVoltage.HasValue || snap.Vm3_TestResult != null)
                        {
                            SetExcelCellValue(cells, 8 + ro, valueCol, FormatNullableNumber(snap.Vm3_FlipVoltage));
                            SetExcelCellValue(cells, 8 + ro, singleResultCol, NormalizeFuelResult(snap.Vm3_TestResult));
                            SetOverall(8 + ro, NormalizeFuelResult(snap.Vm3_Overall));
                            // SetExcelCellValue(cells, 8 + ro, timeCol, testTime);
                        }
                        else
                        {
                            SetExcelCellValue(cells, 8 + ro, valueCol, "--");
                            SetExcelCellValue(cells, 8 + ro, singleResultCol, "--");
                            SetOverall(8 + ro, "未测试");
                        }
                    }
                    else
                    {
                        SetExcelCellValue(cells, 8 + ro, valueCol, "--");
                        SetExcelCellValue(cells, 8 + ro, singleResultCol, "--");
                        SetOverall(8 + ro, "未测试");
                    }

                    // vm4 - 温度采集功能 (行9)
                    if (IsSingleBoardStepSelected("温度采集功能"))
                    {
                        if (snap.Vm4_Temp.HasValue || snap.Vm4_TestResult != null)
                        {
                            SetExcelCellValue(cells, 9 + ro, valueCol, FormatNullableNumber(snap.Vm4_Temp));
                            SetExcelCellValue(cells, 9 + ro, singleResultCol, NormalizeFuelResult(snap.Vm4_TestResult));
                            SetOverall(9 + ro, NormalizeFuelResult(snap.Vm4_Overall));
                            // SetExcelCellValue(cells, 9 + ro, timeCol, testTime);
                        }
                        else
                        {
                            SetExcelCellValue(cells, 9 + ro, valueCol, "--");
                            SetExcelCellValue(cells, 9 + ro, singleResultCol, "--");
                            SetOverall(9 + ro, "未测试");
                        }
                    }
                    else
                    {
                        SetExcelCellValue(cells, 9 + ro, valueCol, "--");
                        SetExcelCellValue(cells, 9 + ro, singleResultCol, "--");
                        SetOverall(9 + ro, "未测试");
                    }

                    // vm5 - 离散量采集功能测试 (行10-13)
                    if (IsSingleBoardStepSelected("离散量采集功能测试"))
                    {
                        if (snap.Vm5_B0Gnd != null || snap.Vm5_GndResult != null)
                        {
                            SetExcelCellValue(cells, 10 + ro, valueCol, snap.Vm5_B0Gnd ?? "--");
                            SetExcelCellValue(cells, 10 + ro, singleResultCol, NormalizeFuelResult(snap.Vm5_GndResult));
                            SetExcelCellValue(cells, 11 + ro, valueCol, snap.Vm5_B1Gnd ?? "--");
                            SetExcelCellValue(cells, 11 + ro, singleResultCol, NormalizeFuelResult(snap.Vm5_GndResult));
                            SetExcelCellValue(cells, 12 + ro, valueCol, snap.Vm5_B0Open ?? "--");
                            SetExcelCellValue(cells, 12 + ro, singleResultCol, NormalizeFuelResult(snap.Vm5_OpenResult));
                            SetExcelCellValue(cells, 13 + ro, valueCol, snap.Vm5_B1Open ?? "--");
                            SetExcelCellValue(cells, 13 + ro, singleResultCol, NormalizeFuelResult(snap.Vm5_OpenResult));
                            SetOverall(10 + ro, NormalizeFuelResult(snap.Vm5_Overall));
                            // SetExcelCellValue(cells, 10 + ro, timeCol, testTime);
                        }
                        else
                        {
                            FillUntestedCells(cells, 10 + ro, valueCol, 13 + ro);
                            FillUntestedCells(cells, 10 + ro, singleResultCol, 13 + ro);
                            SetOverall(10 + ro, "未测试");
                        }
                    }
                    else
                    {
                        FillUntestedCells(cells, 10 + ro, valueCol, 13 + ro);
                        FillUntestedCells(cells, 10 + ro, singleResultCol, 13 + ro);
                        SetOverall(10 + ro, "未测试");
                    }

                    // vm6 - 离散量输出功能测试 (行14-30)
                    if (IsSingleBoardStepSelected("离散量输出功能测试"))
                    {
                        if (snap.Vm6_J6.HasValue || snap.Vm6_StepA != null)
                        {
                            // 接地阻抗测试各点值 + 单项结果（<10Ω → PASS）
                            var gndVals = new double?[] { snap.Vm6_J6, snap.Vm6_J7, snap.Vm6_J8, snap.Vm6_J9, snap.Vm6_J10, snap.Vm6_J11, snap.Vm6_J12, snap.Vm6_J13 };
                            for (int gi = 0; gi < 8; gi++)
                            {
                                SetExcelCellValue(cells, 14 + ro + gi, valueCol, FormatNullableNumber(gndVals[gi]));
                                var gr = gndVals[gi].HasValue ? (gndVals[gi].Value < 10.0 ? "PASS" : "FAIL") : "--";
                                SetExcelCellValue(cells, 14 + ro + gi, singleResultCol, gr);
                            }
                            // 开路阻抗测试各点值 + 单项结果（>100000Ω → PASS）
                            var openVals = new double?[] { snap.Vm6_OJ6, snap.Vm6_OJ7, snap.Vm6_OJ8, snap.Vm6_OJ9, snap.Vm6_OJ10, snap.Vm6_OJ11, snap.Vm6_OJ12, snap.Vm6_OJ13 };
                            for (int oi = 0; oi < 8; oi++)
                            {
                                SetExcelCellValue(cells, 22 + ro + oi, valueCol, FormatNullableNumber(openVals[oi]));
                                var or2 = openVals[oi].HasValue ? (openVals[oi].Value > 100000.0 ? "PASS" : "FAIL") : "--";
                                SetExcelCellValue(cells, 22 + ro + oi, singleResultCol, or2);
                            }
                            // J14 电压测试
                            SetExcelCellValue(cells, 30 + ro, valueCol, FormatNullableNumber(snap.Vm6_J14V));
                            SetExcelCellValue(cells, 30 + ro, singleResultCol, NormalizeFuelResult(snap.Vm6_StepC));
                            SetOverall(14 + ro, NormalizeFuelResult(snap.Vm6_Overall));
                            // SetExcelCellValue(cells, 14 + ro, timeCol, testTime);
                        }
                        else
                        {
                            FillUntestedCells(cells, 14 + ro, valueCol, 21 + ro);
                            FillUntestedCells(cells, 14 + ro, singleResultCol, 21 + ro);
                            FillUntestedCells(cells, 22 + ro, valueCol, 30 + ro);
                            FillUntestedCells(cells, 22 + ro, singleResultCol, 30 + ro);
                            SetOverall(14 + ro, "未测试");
                        }
                    }
                    else
                    {
                        FillUntestedCells(cells, 14 + ro, valueCol, 21 + ro);
                        FillUntestedCells(cells, 14 + ro, singleResultCol, 21 + ro);
                        FillUntestedCells(cells, 22 + ro, valueCol, 30 + ro);
                        FillUntestedCells(cells, 22 + ro, singleResultCol, 30 + ro);
                        SetOverall(14 + ro, "未测试");
                    }

                    // vm7 - RS422通信功能测试 (行31-34)
                    if (IsSingleBoardStepSelected("RS422通信功能测试"))
                    {
                        if (snap.Vm7_ARx != null || snap.Vm7_StepA != null)
                        {
                            SetExcelCellValue(cells, 31 + ro, valueCol, snap.Vm7_ARx ?? "--");
                            SetExcelCellValue(cells, 31 + ro, singleResultCol, NormalizeFuelResult(snap.Vm7_StepA));
                            SetExcelCellValue(cells, 32 + ro, valueCol, snap.Vm7_BRx ?? "--");
                            SetExcelCellValue(cells, 32 + ro, singleResultCol, NormalizeFuelResult(snap.Vm7_StepB));
                            SetExcelCellValue(cells, 33 + ro, valueCol, snap.Vm7_CRx ?? "--");
                            SetExcelCellValue(cells, 33 + ro, singleResultCol, NormalizeFuelResult(snap.Vm7_StepC));
                            SetExcelCellValue(cells, 34 + ro, valueCol, snap.Vm7_DRx ?? "--");
                            SetExcelCellValue(cells, 34 + ro, singleResultCol, NormalizeFuelResult(snap.Vm7_StepD));
                            SetOverall(31 + ro, NormalizeFuelResult(snap.Vm7_Overall));
                            // SetExcelCellValue(cells, 31 + ro, timeCol, testTime);
                        }
                        else
                        {
                            FillUntestedCells(cells, 31 + ro, valueCol, 34 + ro);
                            FillUntestedCells(cells, 31 + ro, singleResultCol, 34 + ro);
                            SetOverall(31 + ro, "未测试");
                        }
                    }
                    else
                    {
                        FillUntestedCells(cells, 31 + ro, valueCol, 34 + ro);
                        FillUntestedCells(cells, 31 + ro, singleResultCol, 34 + ro);
                        SetOverall(31 + ro, "未测试");
                    }

                    // vm8 - RS422通信自检测功能测试 (行35-36)
                    if (IsSingleBoardStepSelected("RS422通信自检测功能测试"))
                    {
                        if (snap.Vm8_ARx != null || snap.Vm8_StepA != null)
                        {
                            SetExcelCellValue(cells, 35 + ro, valueCol, snap.Vm8_ARx ?? "--");
                            SetExcelCellValue(cells, 35 + ro, singleResultCol, NormalizeFuelResult(snap.Vm8_StepA));
                            SetExcelCellValue(cells, 36 + ro, valueCol, snap.Vm8_ARx ?? "--");
                            SetExcelCellValue(cells, 36 + ro, singleResultCol, NormalizeFuelResult(snap.Vm8_StepB));
                            SetOverall(35 + ro, NormalizeFuelResult(snap.Vm8_Overall));
                            // SetExcelCellValue(cells, 35 + ro, timeCol, testTime);
                        }
                        else
                        {
                            FillUntestedCells(cells, 35 + ro, valueCol, 36 + ro);
                            FillUntestedCells(cells, 35 + ro, singleResultCol, 36 + ro);
                            SetOverall(35 + ro, "未测试");
                        }
                    }
                    else
                    {
                        FillUntestedCells(cells, 35 + ro, valueCol, 36 + ro);
                        FillUntestedCells(cells, 35 + ro, singleResultCol, 36 + ro);
                        SetOverall(35 + ro, "未测试");
                    }
                }

                FillSection(_fuelSnapshot17V, 0);   // 18V 基准行
                FillSection(_fuelSnapshot28V, 37);  // 28V +37行
                FillSection(_fuelSnapshot322V, 74); // 32.2V +74行

                workbook.GetType().InvokeMember("Save", BindingFlags.InvokeMethod, null, workbook, null);
            }
            catch (Exception ex)
            {
                AppendSingleBoardReportLine($"REPORT | FUEL_EXCEL_FILL_FAILED | {ex.GetType().Name} | {ex.Message}");
            }
            finally
            {
                TryInvoke(workbook, "Close", false);
                TryInvoke(excelApp, "Quit");
                ReleaseComObject(range);
                ReleaseComObject(cells);
                ReleaseComObject(sheet);
                ReleaseComObject(workbook);
                ReleaseComObject(workbooks);
                ReleaseComObject(excelApp);
                OleMessageFilter.Revoke();
            }
        }

        private void AppendSingleBoardReportLine(string message)
        {
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";
            Debug.WriteLine(line);
        }

        private static bool IsPass(string result)
        {
            var r = result?.Trim();
            return string.Equals(r, "PASS", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(r, "合格", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeResult(string result)
        {
            var r = result?.Trim();
            return string.IsNullOrEmpty(r) ? "未知" : r;
        }

        #region Window Event Handlers

        /// <summary>
        /// 处理顶部栏鼠标左键按下事件（窗口拖拽和最大化）
        /// </summary>
        private void TopBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                // 双击切换最大化状态 - 使用 WindowManager 服务
                var windowManager = ((App)Application.Current).Container.Resolve<IWindowManagerService>();
                windowManager?.ToggleMaximizeWindow(this);
                e.Handled = true;
            }
            else if (e.ChangedButton == MouseButton.Left)
            {
                // 单击拖拽窗口
                DragMove();
                e.Handled = true;
            }
        }

        /// <summary>
        /// 窗口激活事件处理（MainWindow获得焦点时）
        /// </summary>
        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            if (DataContext is MainWindowViewModel vm)
            {
                vm.OnMainWindowActivated();
            }
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// 在可视化树中查找指定类型的祖先元素
        /// </summary>
        /// <typeparam name="T">要查找的元素类型</typeparam>
        /// <param name="current">起始元素</param>
        /// <returns>找到的祖先元素，如果未找到则返回null</returns>
        private static T FindAncestor<T>(DependencyObject current) where T : DependencyObject
        {
            while (current != null && !(current is T))
            {
                current = VisualTreeHelper.GetParent(current);
            }
            return current as T;
        }

        /// <summary>
        /// 展开项目树到三级节点
        /// </summary>
        public void ExpandProjectTreeToLevel3()
        {
            var treeView = FindName("ProjectTreeView") as TreeView;
            if (treeView == null)
            {
                return;
            }
            // 检查TreeView是否已加载
            if (!treeView.IsLoaded)
            {
                treeView.Loaded += (s, e) => {
                    PerformTreeExpansion(treeView);
                };
                return;
            }

            // 如果已加载，直接执行展开
            PerformTreeExpansion(treeView);
        }

        /// <summary>
        /// 执行树展开操作
        /// </summary>
        private void PerformTreeExpansion(TreeView treeView)
        {
            // 使用更长的延迟确保容器完全生成
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    // 强制更新布局
                    treeView.UpdateLayout();

                    // 等待容器生成
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            // 展开所有一级项目
                            foreach (var item in treeView.Items)
                            {
                                var treeViewItem = treeView.ItemContainerGenerator.ContainerFromItem(item) as TreeViewItem;
                                if (treeViewItem != null)
                                {
                                    treeViewItem.IsExpanded = true;

                                    // 递归展开到三级节点
                                    ExpandToLevel3(treeViewItem, 1);
                                }
                                else
                                {
                                }
                            }
                        }
                        catch (Exception)
                        {
                        }
                    }), DispatcherPriority.Loaded);
                }
                catch (Exception)
                {
                }
            }), DispatcherPriority.Loaded);
        }

        /// <summary>
        /// 递归展开到三级节点
        /// </summary>
        private void ExpandToLevel3(TreeViewItem parentItem, int currentLevel)
        {
            if (currentLevel >= 3)
            {
                return; // 只展开到三级节点
            }
            // 强制更新布局以生成子容器
            parentItem.UpdateLayout();

            // 使用延迟确保容器生成完成
            Dispatcher.BeginInvoke(new Action(() =>
            {
                foreach (var child in parentItem.Items)
                {
                    var childItem = parentItem.ItemContainerGenerator.ContainerFromItem(child) as TreeViewItem;
                    if (childItem != null && childItem.HasItems)
                    {
                        childItem.IsExpanded = true;
                        // 递归展开下一级
                        ExpandToLevel3(childItem, currentLevel + 1);
                    }
                    else if (childItem != null)
                    {
                    }
                    else
                    {
                    }
                }
            }), DispatcherPriority.Loaded);
        }


        /// <summary>
        /// 测试方法：手动触发项目树展开（用于调试）
        /// </summary>
        public void TestExpandProjectTree()
        {
            // 检查ViewModel数据
            if (_viewModel?.CurrentProject != null)
            {
                if (_viewModel.CurrentProject.Count > 0)
                {
                    var rootProject = _viewModel.CurrentProject[0];
                    if (rootProject.Children != null)
                    {
                        foreach (var child in rootProject.Children)
                        {
                        }
                    }
                }
            }
            else
            {
            }

            // 检查TreeView
            var treeView = FindName("ProjectTreeView") as TreeView;
            if (treeView != null)
            {
            }
            else
            {
            }

            // 尝试展开
            ExpandProjectTreeToLevel3();
        }

        /// <summary>
        /// 关闭标签页菜单项点击事件
        /// </summary>
        private void CloseTabMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.Tag is string pageName)
            {
                _viewModel?.CloseTabCommand?.Execute(pageName);
            }
        }

        #endregion

        private sealed class HydraulicAbortException : Exception
        {
            public HydraulicAbortException(string message) : base(message) { }
        }
    }
}
