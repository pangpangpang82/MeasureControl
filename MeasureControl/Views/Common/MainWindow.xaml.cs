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
using MeasureControl.Views.Dialogs;
using MeasureControl.ViewModels.Dialogs;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

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
        private string _singleBoardAutoTestReportPath;
        private string _singleBoardAutoTestExcelReportPath;
        private HashSet<string> _executedFuelTestStepNames;

        #endregion

        private static bool IsSingleBoardTestTaskNode(ProjectItem projectItem)
        {
            if (projectItem == null)
            {
                return false;
            }

            var v = (projectItem.Tag ?? projectItem.Name)?.Trim();
            return string.Equals(v, "空气单板", StringComparison.OrdinalIgnoreCase)
                || string.Equals(v, "液压单板", StringComparison.OrdinalIgnoreCase)
                || string.Equals(v, "惰化单板", StringComparison.OrdinalIgnoreCase)
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
            ProjectTreeView.Loaded -= ProjectTreeView_OnLoaded;
            
            // 取消订阅事件
            if (_eventAggregator != null)
            {
                _eventAggregator.GetEvent<TestTaskCreatedEvent>().Unsubscribe(OnTestTaskCreated);
                _eventAggregator.GetEvent<SelectProjectItemEvent>().Unsubscribe(OnSelectProjectItem);
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
                if (_viewModel?.IsFixedDemoMode == true)
                {
                    var parentTreeViewItem = FindParent<TreeViewItem>(treeViewItem);
                    var parentProjectItem = parentTreeViewItem?.DataContext as ProjectItem;
                    var isUnderTestTasksFolder = parentProjectItem != null
                        && (string.Equals(parentProjectItem.Type, "test_tasks", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(parentProjectItem.Name, "测试任务", StringComparison.OrdinalIgnoreCase));

                    // Demo 模式下：只放开“测试任务”文件夹下的单板节点右键菜单，其它节点一律禁用
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
                    if (string.Equals(boardType, "空气单板", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(boardType, "液压单板", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(boardType, "惰化单板", StringComparison.OrdinalIgnoreCase)
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

            HashSet<string> selectedStepNames = null;
            if (boardType == "加放油单板")
            {
                selectedStepNames = GetSelectedFuelTestStepNames(boardName);
                _executedFuelTestStepNames = selectedStepNames != null 
                    ? new HashSet<string>(selectedStepNames, StringComparer.OrdinalIgnoreCase) 
                    : null;
            }
            else
            {
                _executedFuelTestStepNames = null;
            }

            (string Name, Func<CancellationToken, Task<string>> Run)[] steps = boardType switch
            {
                "液压单板" => BuildHydraulicSteps(),
                "空气单板" => BuildAirSteps(),
                "惰化单板" => BuildInertingSteps(),
                "加放油单板" => BuildFuelSteps(selectedStepNames),
                _ => null
            };

            if (steps == null)
            {
                ReMessageBox.Show($"未知单板类型: {boardType}", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (steps.Length == 0)
            {
                ReMessageBox.Show($"{boardType}整板自动测试未实现或没有选中任何测试项", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

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

            var originalIsEnabled = IsEnabled;

            try
            {
                PrepareSingleBoardReport(boardName);
                AppendSingleBoardReportLine($"START | {boardName} | {boardType}");

                if (string.Equals(boardType, "加放油单板", StringComparison.OrdinalIgnoreCase))
                {
                    var selected = _executedFuelTestStepNames == null
                        ? "ALL"
                        : (_executedFuelTestStepNames.Count == 0 ? "NONE" : string.Join(",", _executedFuelTestStepNames));
                    AppendSingleBoardReportLine($"SELECTED | {selected}");
                }

                // 整板自动测试期间禁用主窗口操作
                IsEnabled = false;

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
                bool hasAnyFailed = false;
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
                        result = "不合格";
                    }

                    AppendSingleBoardReportLine($"STEP | {steps[i].Name} | {NormalizeResult(result)}");

                    done++;
                    vm.Progress = done;

                    if (!IsPass(result))
                    {
                        hasAnyFailed = true;
                    }
                }

                AppendSingleBoardReportLine(hasAnyFailed ? "END | FAIL" : "END | PASS");
                TryGenerateSingleBoardExcelReport(boardName, boardType);
                vm.IsFailed = hasAnyFailed;
                vm.IsCompleted = !hasAnyFailed;
                vm.ConfirmStopOnClose = false;
                vm.Progress = steps.Length;
                vm.StatusText = "完成";
            }
            catch (OperationCanceledException)
            {
                AppendSingleBoardReportLine("END | CANCELED");
                if (vm != null)
                {
                    vm.IsFailed = true;
                    vm.ConfirmStopOnClose = false;
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
                IsEnabled = originalIsEnabled;

                _singleBoardAutoTestCts?.Dispose();
                _singleBoardAutoTestCts = null;
            }
        }

        private static (string Name, Func<CancellationToken, Task<string>> Run)[] BuildHydraulicSteps()
        {
            var vm61 = ContainerLocator.Container.Resolve<HC_6_1ViewModel>();
            var vm62 = ContainerLocator.Container.Resolve<HC_6_2ViewModel>();
            var vm63 = ContainerLocator.Container.Resolve<HC_6_3ViewModel>();
            var vm64 = ContainerLocator.Container.Resolve<HC_6_4ViewModel>();
            var vm65 = ContainerLocator.Container.Resolve<HC_6_5ViewModel>();
            var vm66 = ContainerLocator.Container.Resolve<HC_6_6ViewModel>();
            var vm67 = ContainerLocator.Container.Resolve<HC_6_7ViewModel>();
            var vm68 = ContainerLocator.Container.Resolve<HC_6_8ViewModel>();

            return new (string Name, Func<CancellationToken, Task<string>> Run)[]
            {
                ("电源阻抗测试", ct => vm61.RunOnceAsync(ct)),
                ("二次电源测试", ct => vm62.RunOnceAsync(ct)),
                ("温度采集测试", ct => vm63.RunOnceAsync(ct)),
                ("压力传感器信号采集测试", ct => vm64.RunOnceAsync(ct)),
                ("压差传感器信号采集测试", ct => vm65.RunOnceAsync(ct)),
                ("油量传感器信号采集测试", ct => vm66.RunOnceAsync(ct)),
                ("离散量采集测试", ct => vm67.RunOnceAsync(ct)),
                ("离散量输出测试", ct => vm68.RunOnceAsync(ct)),
            };
        }

        private static (string Name, Func<CancellationToken, Task<string>> Run)[] BuildAirSteps()
        {
            return Array.Empty<(string Name, Func<CancellationToken, Task<string>> Run)>();
        }

        private static HashSet<string> GetSelectedFuelTestStepNames()
        {
            try
            {
                return GetSelectedFuelTestStepNames(null);
            }
            catch
            {
                return null;
            }
        }

        private static HashSet<string> GetSelectedFuelTestStepNames(string expectedTestTaskName)
        {
            try
            {
                var regionManager = ContainerLocator.Container.Resolve<Prism.Regions.IRegionManager>();
                if (regionManager == null)
                    return null;

                BoardTestViewModel TryGetVmFromView(object view, string expectedName)
                {
                    if (view is not FrameworkElement fe)
                        return null;

                    if (fe.DataContext is not BoardTestViewModel vm)
                        return null;

                    if (!string.Equals(vm.BoardType, "加放油单板", StringComparison.OrdinalIgnoreCase))
                        return null;

                    if (!string.IsNullOrWhiteSpace(expectedName)
                        && !string.Equals(vm.TestTaskName, expectedName, StringComparison.OrdinalIgnoreCase))
                        return null;

                    return vm;
                }

                BoardTestViewModel FindVmInRegion(string regionName, string expectedName)
                {
                    if (string.IsNullOrWhiteSpace(regionName))
                        return null;

                    if (!regionManager.Regions.ContainsRegionWithName(regionName))
                        return null;

                    var region = regionManager.Regions[regionName];
                    foreach (var v in region.Views)
                    {
                        var vm = TryGetVmFromView(v, expectedName);
                        if (vm != null)
                            return vm;
                    }

                    foreach (var v in region.ActiveViews)
                    {
                        var vm = TryGetVmFromView(v, expectedName);
                        if (vm != null)
                            return vm;
                    }

                    return null;
                }

                var boardTestVm = FindVmInRegion("MainRegion", expectedTestTaskName)
                               ?? FindVmInRegion("FloatingRegion", expectedTestTaskName);

                if (boardTestVm == null && !string.IsNullOrWhiteSpace(expectedTestTaskName))
                {
                    boardTestVm = FindVmInRegion("MainRegion", null)
                               ?? FindVmInRegion("FloatingRegion", null);
                }

                if (boardTestVm == null)
                {
                    foreach (var region in regionManager.Regions)
                    {
                        foreach (var v in region.Views)
                        {
                            var vm = TryGetVmFromView(v, expectedTestTaskName);
                            if (vm != null)
                            {
                                boardTestVm = vm;
                                break;
                            }
                        }

                        if (boardTestVm != null)
                            break;

                        foreach (var v in region.ActiveViews)
                        {
                            var vm = TryGetVmFromView(v, expectedTestTaskName);
                            if (vm != null)
                            {
                                boardTestVm = vm;
                                break;
                            }
                        }

                        if (boardTestVm != null)
                            break;
                    }
                }

                if (boardTestVm == null && !string.IsNullOrWhiteSpace(expectedTestTaskName))
                {
                    foreach (var region in regionManager.Regions)
                    {
                        foreach (var v in region.Views)
                        {
                            var vm = TryGetVmFromView(v, null);
                            if (vm != null)
                            {
                                boardTestVm = vm;
                                break;
                            }
                        }

                        if (boardTestVm != null)
                            break;
                    }
                }

                if (boardTestVm == null)
                    return null;

                var selectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var item in boardTestVm.TestSequenceItems)
                {
                    if (item.IsSelected)
                        selectedNames.Add(item.Name);
                }

                return selectedNames;
            }
            catch
            {
                return null;
            }
        }

        private static (string Name, Func<CancellationToken, Task<string>> Run)[] BuildInertingSteps()
        {
            return Array.Empty<(string Name, Func<CancellationToken, Task<string>> Run)>();
        }

        private static (string Name, Func<CancellationToken, Task<string>> Run)[] BuildFuelSteps(HashSet<string> selectedStepNames = null)
        {
            var vm1 = ContainerLocator.Container.Resolve<PowerImpedanceTestViewModel>();
            var vm2 = ContainerLocator.Container.Resolve<SecondaryPowerTestViewModel>();
            var vm3 = ContainerLocator.Container.Resolve<TemperatureAcquisitionTestViewModel>();
            var vm4 = ContainerLocator.Container.Resolve<LowVoltageAlarmTestViewModel>();
            var vm5 = ContainerLocator.Container.Resolve<DiscreteInputTestViewModel>();
            var vm6 = ContainerLocator.Container.Resolve<DiscreteOutputTestViewModel>();
            var vm7 = ContainerLocator.Container.Resolve<RS422CommunicationFunctionTestViewModel>();
            var vm8 = ContainerLocator.Container.Resolve<RS422SelfCheckTestViewModel>();

            var allSteps = new (string Name, Func<CancellationToken, Task<string>> Run)[]
            {
                ("电源阻抗测试", ct => RunFuelAutoTestAsync(vm1?.AutoTestCommand, () => vm1?.IsAutoTestRunning ?? false, () => vm1?.OverallResult, ct)),
                ("二次电源测试", ct => RunFuelAutoTestAsync(vm2?.AutoTestCommand, () => vm2?.IsAutoTestRunning ?? false, () => vm2?.OverallResult, ct)),
                ("低电压告警功能测试", ct => RunFuelAutoTestAsync(vm4?.AutoTestCommand, () => vm4?.IsAutoTestRunning ?? false, () => vm4?.OverallResult, ct)),
                ("温度采集功能", ct => RunFuelAutoTestAsync(vm3?.AutoTestCommand, () => vm3?.IsAutoTestRunning ?? false, () => vm3?.OverallResult, ct)),
                ("离散量采集功能测试", ct => RunFuelAutoTestAsync(vm5?.AutoTestCommand, () => vm5?.IsAutoTestRunning ?? false, () => vm5?.OverallResult, ct)),
                ("离散量输出功能测试", ct => RunFuelAutoTestAsync(vm6?.AutoTestCommand, () => vm6?.IsAutoTestRunning ?? false, () => vm6?.OverallResult, ct)),
                ("RS422通信功能测试", ct => RunFuelAutoTestAsync(vm7?.AutoTestCommand, () => vm7?.IsAutoTestRunning ?? false, () => vm7?.OverallResult, ct)),
                ("RS422通信自检测功能测试", ct => RunFuelAutoTestAsync(vm8?.AutoTestCommand, () => vm8?.IsAutoTestRunning ?? false, () => vm8?.OverallResult, ct)),
            };

            if (selectedStepNames == null)
                return allSteps;

            return allSteps.Where(s => selectedStepNames.Contains(s.Name)).ToArray();
        }

        private static async Task<string> RunFuelAutoTestAsync(
            System.Windows.Input.ICommand autoTestCommand,
            Func<bool> isAutoTestRunning,
            Func<string> getOverallResult,
            CancellationToken cancellationToken)
        {
            if (autoTestCommand == null)
                throw new InvalidOperationException("AutoTestCommand is null");

            const int startTimeoutMs = 2000;
            const int runTimeoutMs = 30 * 60 * 1000;

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (autoTestCommand.CanExecute(null))
                    autoTestCommand.Execute(null);
            });

            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (!cancellationToken.IsCancellationRequested && sw.ElapsedMilliseconds < startTimeoutMs)
            {
                if (isAutoTestRunning != null && isAutoTestRunning())
                    break;
                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            }

            if (!cancellationToken.IsCancellationRequested && !(isAutoTestRunning?.Invoke() ?? false))
            {
                return "不合格";
            }

            sw.Restart();
            while (!cancellationToken.IsCancellationRequested && (isAutoTestRunning?.Invoke() ?? false) && sw.ElapsedMilliseconds < runTimeoutMs)
            {
                await Task.Delay(200, cancellationToken).ConfigureAwait(false);
            }

            if (!cancellationToken.IsCancellationRequested && (isAutoTestRunning?.Invoke() ?? false))
            {
                try
                {
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        if (autoTestCommand.CanExecute(null))
                            autoTestCommand.Execute(null);
                    });
                }
                catch
                {
                }
                return "不合格";
            }

            var r = (getOverallResult?.Invoke() ?? string.Empty).Trim();
            if (string.Equals(r, "PASS", StringComparison.OrdinalIgnoreCase) || string.Equals(r, "合格", StringComparison.OrdinalIgnoreCase))
                return "合格";
            return "不合格";
        }

        private void PrepareSingleBoardReport(string boardName)
        {
            _singleBoardAutoTestExcelReportPath = null;
            try
            {
                var baseDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "TestResults");
                Directory.CreateDirectory(baseDir);
                _singleBoardAutoTestReportPath = System.IO.Path.Combine(baseDir, $"整板自动测试_{boardName}_{DateTime.Now:yyyyMMdd_HHmmss}.log");
                File.WriteAllText(_singleBoardAutoTestReportPath, string.Empty);
            }
            catch
            {
                _singleBoardAutoTestReportPath = null;
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
                        FillAction = FillHydraulicBoardExcelReport
                    };
                case "加放油单板":
                    return new SingleBoardExcelReportConfig
                    {
                        TemplateFileName = "加放油报表模板.xlsx",
                        OutputFolderName = "TestResults",
                        FileNamePrefix = "加放油测试",
                        FillAction = FillFuelBoardExcelReport
                    };
                case "空气单板":
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
                reportConfig.FillAction?.Invoke(reportPath);
                _singleBoardAutoTestExcelReportPath = reportPath;
                AppendSingleBoardReportLine($"REPORT | EXCEL_CREATED | {reportPath}");
            }
            catch (Exception ex)
            {
                AppendSingleBoardReportLine($"REPORT | EXCEL_CREATE_FAILED | {ex.GetType().Name} | {ex.Message}");
            }
        }

        private void FillHydraulicBoardExcelReport(string reportPath)
        {
            var vm61 = ContainerLocator.Container.Resolve<HC_6_1ViewModel>();
            var vm62 = ContainerLocator.Container.Resolve<HC_6_2ViewModel>();
            var vm63 = ContainerLocator.Container.Resolve<HC_6_3ViewModel>();
            var vm64 = ContainerLocator.Container.Resolve<HC_6_4ViewModel>();
            var vm65 = ContainerLocator.Container.Resolve<HC_6_5ViewModel>();
            var vm66 = ContainerLocator.Container.Resolve<HC_6_6ViewModel>();
            var vm67 = ContainerLocator.Container.Resolve<HC_6_7ViewModel>();
            var vm68 = ContainerLocator.Container.Resolve<HC_6_8ViewModel>();
            if (vm61 == null && vm62 == null && vm63 == null && vm64 == null && vm65 == null && vm66 == null && vm67 == null && vm68 == null)
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
                    SetExcelCellValue(cells, 4, 5, vm61.Resistance14Text);
                    SetExcelCellValue(cells, 5, 5, vm61.Resistance182Text);

                    SetExcelCellValue(cells, 4, 6, vm61.IsResistance14Pass ? "合格" : "不合格");
                    SetExcelCellValue(cells, 5, 6, vm61.IsResistance182Pass ? "合格" : "不合格");

                    SetExcelCellFontColor(cells, 4, 6, vm61.IsResistance14Pass ? null : 255);
                    SetExcelCellFontColor(cells, 5, 6, vm61.IsResistance182Pass ? null : 255);

                    range = sheet.GetType().InvokeMember("Range", BindingFlags.GetProperty, null, sheet, new object[] { "G4:G5" });
                    range.GetType().InvokeMember("Merge", BindingFlags.InvokeMethod, null, range, null);
                    range.GetType().InvokeMember("Value", BindingFlags.SetProperty, null, range, new object[] { vm61.CurrentTestResult });
                    SetRangeFontColor(range, string.Equals(vm61.CurrentTestResult, "不合格", StringComparison.OrdinalIgnoreCase) ? 255 : (int?)null);
                    ReleaseComObject(range);
                    range = null;
                }

                if (vm62 != null)
                {
                    SetExcelCellValue(cells, 5, 5, FormatNullableNumber(vm62.Voltage5VValue));
                    SetExcelCellValue(cells, 6, 5, FormatNullableNumber(vm62.Voltage15VValue));
                    SetExcelCellValue(cells, 7, 5, FormatNullableNumber(vm62.VoltageM15VValue));

                    SetExcelCellValue(cells, 5, 6, vm62.IsVoltage5VPass ? "合格" : "不合格");
                    SetExcelCellValue(cells, 6, 6, vm62.IsVoltage15VPass ? "合格" : "不合格");
                    SetExcelCellValue(cells, 7, 6, vm62.IsVoltageM15VPass ? "合格" : "不合格");

                    SetExcelCellFontColor(cells, 5, 6, vm62.IsVoltage5VPass ? null : 255);
                    SetExcelCellFontColor(cells, 6, 6, vm62.IsVoltage15VPass ? null : 255);
                    SetExcelCellFontColor(cells, 7, 6, vm62.IsVoltageM15VPass ? null : 255);

                    range = sheet.GetType().InvokeMember("Range", BindingFlags.GetProperty, null, sheet, new object[] { "G5:G7" });
                    range.GetType().InvokeMember("Value", BindingFlags.SetProperty, null, range, new object[] { vm62.CurrentTestResult });
                    SetRangeFontColor(range, string.Equals(vm62.CurrentTestResult, "不合格", StringComparison.OrdinalIgnoreCase) ? 255 : (int?)null);
                    ReleaseComObject(range);
                    range = null;
                }

                if (vm63 != null)
                {
                    SetExcelCellValue(cells, 8, 5, FormatNullableNumber(vm63.Temp1Value));
                    SetExcelCellValue(cells, 9, 5, FormatNullableNumber(vm63.Temp1BValue));
                    SetExcelCellValue(cells, 10, 5, FormatNullableNumber(vm63.Temp2Value));
                    SetExcelCellValue(cells, 11, 5, FormatNullableNumber(vm63.Temp2BValue));
                    SetExcelCellValue(cells, 12, 5, FormatNullableNumber(vm63.Temp3Value));
                    SetExcelCellValue(cells, 13, 5, FormatNullableNumber(vm63.Temp3BValue));

                    SetExcelCellValue(cells, 8, 6, vm63.IsTemp1Pass ? "合格" : "不合格");
                    SetExcelCellValue(cells, 9, 6, vm63.IsTemp1BPass ? "合格" : "不合格");
                    SetExcelCellValue(cells, 10, 6, vm63.IsTemp2Pass ? "合格" : "不合格");
                    SetExcelCellValue(cells, 11, 6, vm63.IsTemp2BPass ? "合格" : "不合格");
                    SetExcelCellValue(cells, 12, 6, vm63.IsTemp3Pass ? "合格" : "不合格");
                    SetExcelCellValue(cells, 13, 6, vm63.IsTemp3BPass ? "合格" : "不合格");

                    SetExcelCellFontColor(cells, 8, 6, vm63.IsTemp1Pass ? null : 255);
                    SetExcelCellFontColor(cells, 9, 6, vm63.IsTemp1BPass ? null : 255);
                    SetExcelCellFontColor(cells, 10, 6, vm63.IsTemp2Pass ? null : 255);
                    SetExcelCellFontColor(cells, 11, 6, vm63.IsTemp2BPass ? null : 255);
                    SetExcelCellFontColor(cells, 12, 6, vm63.IsTemp3Pass ? null : 255);
                    SetExcelCellFontColor(cells, 13, 6, vm63.IsTemp3BPass ? null : 255);

                    range = sheet.GetType().InvokeMember("Range", BindingFlags.GetProperty, null, sheet, new object[] { "G8:G13" });
                    range.GetType().InvokeMember("Value", BindingFlags.SetProperty, null, range, new object[] { vm63.CurrentTestResult });
                    SetRangeFontColor(range, string.Equals(vm63.CurrentTestResult, "不合格", StringComparison.OrdinalIgnoreCase) ? 255 : (int?)null);
                    ReleaseComObject(range);
                    range = null;
                }

                if (vm64 != null)
                {
                    SetExcelCellValue(cells, 14, 5, FormatNullableNumber(vm64.PressurePoint1Sys1Value));
                    SetExcelCellValue(cells, 15, 5, FormatNullableNumber(vm64.PressurePoint1Sys2Value));
                    SetExcelCellValue(cells, 16, 5, FormatNullableNumber(vm64.PressurePoint1Sys3Value));
                    SetExcelCellValue(cells, 17, 5, FormatNullableNumber(vm64.PressurePoint2Sys1Value));
                    SetExcelCellValue(cells, 18, 5, FormatNullableNumber(vm64.PressurePoint2Sys2Value));
                    SetExcelCellValue(cells, 19, 5, FormatNullableNumber(vm64.PressurePoint2Sys3Value));
                    SetExcelCellValue(cells, 20, 5, FormatNullableNumber(vm64.PressurePoint3Sys1Value));
                    SetExcelCellValue(cells, 21, 5, FormatNullableNumber(vm64.PressurePoint3Sys2Value));
                    SetExcelCellValue(cells, 22, 5, FormatNullableNumber(vm64.PressurePoint3Sys3Value));

                    SetExcelCellValue(cells, 14, 6, vm64.IsPressurePoint1Sys1Pass ? "合格" : "不合格");
                    SetExcelCellValue(cells, 15, 6, vm64.IsPressurePoint1Sys2Pass ? "合格" : "不合格");
                    SetExcelCellValue(cells, 16, 6, vm64.IsPressurePoint1Sys3Pass ? "合格" : "不合格");
                    SetExcelCellValue(cells, 17, 6, vm64.IsPressurePoint2Sys1Pass ? "合格" : "不合格");
                    SetExcelCellValue(cells, 18, 6, vm64.IsPressurePoint2Sys2Pass ? "合格" : "不合格");
                    SetExcelCellValue(cells, 19, 6, vm64.IsPressurePoint2Sys3Pass ? "合格" : "不合格");
                    SetExcelCellValue(cells, 20, 6, vm64.IsPressurePoint3Sys1Pass ? "合格" : "不合格");
                    SetExcelCellValue(cells, 21, 6, vm64.IsPressurePoint3Sys2Pass ? "合格" : "不合格");
                    SetExcelCellValue(cells, 22, 6, vm64.IsPressurePoint3Sys3Pass ? "合格" : "不合格");

                    SetExcelCellFontColor(cells, 14, 6, vm64.IsPressurePoint1Sys1Pass ? null : 255);
                    SetExcelCellFontColor(cells, 15, 6, vm64.IsPressurePoint1Sys2Pass ? null : 255);
                    SetExcelCellFontColor(cells, 16, 6, vm64.IsPressurePoint1Sys3Pass ? null : 255);
                    SetExcelCellFontColor(cells, 17, 6, vm64.IsPressurePoint2Sys1Pass ? null : 255);
                    SetExcelCellFontColor(cells, 18, 6, vm64.IsPressurePoint2Sys2Pass ? null : 255);
                    SetExcelCellFontColor(cells, 19, 6, vm64.IsPressurePoint2Sys3Pass ? null : 255);
                    SetExcelCellFontColor(cells, 20, 6, vm64.IsPressurePoint3Sys1Pass ? null : 255);
                    SetExcelCellFontColor(cells, 21, 6, vm64.IsPressurePoint3Sys2Pass ? null : 255);
                    SetExcelCellFontColor(cells, 22, 6, vm64.IsPressurePoint3Sys3Pass ? null : 255);

                    range = sheet.GetType().InvokeMember("Range", BindingFlags.GetProperty, null, sheet, new object[] { "G14:G22" });
                    range.GetType().InvokeMember("Value", BindingFlags.SetProperty, null, range, new object[] { vm64.CurrentTestResult });
                    SetRangeFontColor(range, string.Equals(vm64.CurrentTestResult, "不合格", StringComparison.OrdinalIgnoreCase) ? 255 : (int?)null);
                    ReleaseComObject(range);
                    range = null;
                }

                if (vm65 != null)
                {
                    SetExcelCellValue(cells, 23, 5, FormatNullableNumber(vm65.DptEdp24mAValue));
                    SetExcelCellValue(cells, 24, 5, FormatNullableNumber(vm65.DptEmp2B4mAValue));
                    SetExcelCellValue(cells, 25, 5, FormatNullableNumber(vm65.DptEmp3B4mAValue));
                    SetExcelCellValue(cells, 26, 5, FormatNullableNumber(vm65.DptSys14mAValue));
                    SetExcelCellValue(cells, 27, 5, FormatNullableNumber(vm65.DptSys24mAValue));
                    SetExcelCellValue(cells, 28, 5, FormatNullableNumber(vm65.DptSys34mAValue));

                    SetExcelCellValue(cells, 29, 5, FormatNullableNumber(vm65.DptEdp2A20mAValue));
                    SetExcelCellValue(cells, 30, 5, FormatNullableNumber(vm65.DptEmp2B20mAValue));
                    SetExcelCellValue(cells, 31, 5, FormatNullableNumber(vm65.DptEmp3B20mAValue));
                    SetExcelCellValue(cells, 32, 5, FormatNullableNumber(vm65.DptSys120mAValue));
                    SetExcelCellValue(cells, 33, 5, FormatNullableNumber(vm65.DptSys220mAValue));
                    SetExcelCellValue(cells, 34, 5, FormatNullableNumber(vm65.DptSys320mAValue));

                    SetExcelCellValue(cells, 35, 5, FormatNullableNumber(vm65.DptEdp2A10mAValue));
                    SetExcelCellValue(cells, 36, 5, FormatNullableNumber(vm65.DptEmp2B10mAValue));
                    SetExcelCellValue(cells, 37, 5, FormatNullableNumber(vm65.DptEmp3B10mAValue));
                    SetExcelCellValue(cells, 38, 5, FormatNullableNumber(vm65.DptSys110mAValue));
                    SetExcelCellValue(cells, 39, 5, FormatNullableNumber(vm65.DptSys210mAValue));
                    SetExcelCellValue(cells, 40, 5, FormatNullableNumber(vm65.DptSys310mAValue));

                    SetExcelCellValue(cells, 23, 6, vm65.IsDptEdp24mAPass ? "合格" : "不合格");
                    SetExcelCellValue(cells, 24, 6, vm65.IsDptEmp2B4mAPass ? "合格" : "不合格");
                    SetExcelCellValue(cells, 25, 6, vm65.IsDptEmp3B4mAPass ? "合格" : "不合格");
                    SetExcelCellValue(cells, 26, 6, vm65.IsDptSys14mAPass ? "合格" : "不合格");
                    SetExcelCellValue(cells, 27, 6, vm65.IsDptSys24mAPass ? "合格" : "不合格");
                    SetExcelCellValue(cells, 28, 6, vm65.IsDptSys34mAPass ? "合格" : "不合格");

                    SetExcelCellValue(cells, 29, 6, vm65.IsDptEdp2A20mAPass ? "合格" : "不合格");
                    SetExcelCellValue(cells, 30, 6, vm65.IsDptEmp2B20mAPass ? "合格" : "不合格");
                    SetExcelCellValue(cells, 31, 6, vm65.IsDptEmp3B20mAPass ? "合格" : "不合格");
                    SetExcelCellValue(cells, 32, 6, vm65.IsDptSys120mAPass ? "合格" : "不合格");
                    SetExcelCellValue(cells, 33, 6, vm65.IsDptSys220mAPass ? "合格" : "不合格");
                    SetExcelCellValue(cells, 34, 6, vm65.IsDptSys320mAPass ? "合格" : "不合格");

                    SetExcelCellValue(cells, 35, 6, vm65.IsDptEdp2A10mAPass ? "合格" : "不合格");
                    SetExcelCellValue(cells, 36, 6, vm65.IsDptEmp2B10mAPass ? "合格" : "不合格");
                    SetExcelCellValue(cells, 37, 6, vm65.IsDptEmp3B10mAPass ? "合格" : "不合格");
                    SetExcelCellValue(cells, 38, 6, vm65.IsDptSys110mAPass ? "合格" : "不合格");
                    SetExcelCellValue(cells, 39, 6, vm65.IsDptSys210mAPass ? "合格" : "不合格");
                    SetExcelCellValue(cells, 40, 6, vm65.IsDptSys310mAPass ? "合格" : "不合格");

                    SetExcelCellFontColor(cells, 23, 6, vm65.IsDptEdp24mAPass ? null : 255);
                    SetExcelCellFontColor(cells, 24, 6, vm65.IsDptEmp2B4mAPass ? null : 255);
                    SetExcelCellFontColor(cells, 25, 6, vm65.IsDptEmp3B4mAPass ? null : 255);
                    SetExcelCellFontColor(cells, 26, 6, vm65.IsDptSys14mAPass ? null : 255);
                    SetExcelCellFontColor(cells, 27, 6, vm65.IsDptSys24mAPass ? null : 255);
                    SetExcelCellFontColor(cells, 28, 6, vm65.IsDptSys34mAPass ? null : 255);

                    SetExcelCellFontColor(cells, 29, 6, vm65.IsDptEdp2A20mAPass ? null : 255);
                    SetExcelCellFontColor(cells, 30, 6, vm65.IsDptEmp2B20mAPass ? null : 255);
                    SetExcelCellFontColor(cells, 31, 6, vm65.IsDptEmp3B20mAPass ? null : 255);
                    SetExcelCellFontColor(cells, 32, 6, vm65.IsDptSys120mAPass ? null : 255);
                    SetExcelCellFontColor(cells, 33, 6, vm65.IsDptSys220mAPass ? null : 255);
                    SetExcelCellFontColor(cells, 34, 6, vm65.IsDptSys320mAPass ? null : 255);

                    SetExcelCellFontColor(cells, 35, 6, vm65.IsDptEdp2A10mAPass ? null : 255);
                    SetExcelCellFontColor(cells, 36, 6, vm65.IsDptEmp2B10mAPass ? null : 255);
                    SetExcelCellFontColor(cells, 37, 6, vm65.IsDptEmp3B10mAPass ? null : 255);
                    SetExcelCellFontColor(cells, 38, 6, vm65.IsDptSys110mAPass ? null : 255);
                    SetExcelCellFontColor(cells, 39, 6, vm65.IsDptSys210mAPass ? null : 255);
                    SetExcelCellFontColor(cells, 40, 6, vm65.IsDptSys310mAPass ? null : 255);

                    range = sheet.GetType().InvokeMember("Range", BindingFlags.GetProperty, null, sheet, new object[] { "G23:G40" });
                    range.GetType().InvokeMember("Value", BindingFlags.SetProperty, null, range, new object[] { vm65.CurrentTestResult });
                    SetRangeFontColor(range, string.Equals(vm65.CurrentTestResult, "不合格", StringComparison.OrdinalIgnoreCase) ? 255 : (int?)null);
                    ReleaseComObject(range);
                    range = null;
                }

                if (vm66 != null)
                {
                    SetExcelCellValue(cells, 41, 5, vm66.Pin3031FreqText);
                    SetExcelCellValue(cells, 42, 5, vm66.Pin3334FreqText);
                    SetExcelCellValue(cells, 43, 5, vm66.Pin3031VoltText);
                    SetExcelCellValue(cells, 44, 5, vm66.Pin3334VoltText);
                    SetExcelCellValue(cells, 45, 5, vm66.PointLowSys1Text);
                    SetExcelCellValue(cells, 46, 5, vm66.PointLowSys2Text);
                    SetExcelCellValue(cells, 47, 5, vm66.PointMidSys1Text);
                    SetExcelCellValue(cells, 48, 5, vm66.PointMidSys2Text);
                    SetExcelCellValue(cells, 49, 5, vm66.PointHighSys1Text);
                    SetExcelCellValue(cells, 50, 5, vm66.PointHighSys2Text);

                    SetExcelCellValue(cells, 41, 6, vm66.IsPin3031Pass ? "合格" : "不合格");
                    SetExcelCellValue(cells, 42, 6, vm66.IsPin3334Pass ? "合格" : "不合格");
                    SetExcelCellValue(cells, 43, 6, vm66.IsPin3031Pass ? "合格" : "不合格");
                    SetExcelCellValue(cells, 44, 6, vm66.IsPin3334Pass ? "合格" : "不合格");
                    SetExcelCellValue(cells, 45, 6, vm66.IsPointLowSys1Pass ? "合格" : "不合格");
                    SetExcelCellValue(cells, 46, 6, vm66.IsPointLowSys2Pass ? "合格" : "不合格");
                    SetExcelCellValue(cells, 47, 6, vm66.IsPointMidSys1Pass ? "合格" : "不合格");
                    SetExcelCellValue(cells, 48, 6, vm66.IsPointMidSys2Pass ? "合格" : "不合格");
                    SetExcelCellValue(cells, 49, 6, vm66.IsPointHighSys1Pass ? "合格" : "不合格");
                    SetExcelCellValue(cells, 50, 6, vm66.IsPointHighSys2Pass ? "合格" : "不合格");

                    SetExcelCellFontColor(cells, 41, 6, vm66.IsPin3031Pass ? null : 255);
                    SetExcelCellFontColor(cells, 42, 6, vm66.IsPin3334Pass ? null : 255);
                    SetExcelCellFontColor(cells, 43, 6, vm66.IsPin3031Pass ? null : 255);
                    SetExcelCellFontColor(cells, 44, 6, vm66.IsPin3334Pass ? null : 255);
                    SetExcelCellFontColor(cells, 45, 6, vm66.IsPointLowSys1Pass ? null : 255);
                    SetExcelCellFontColor(cells, 46, 6, vm66.IsPointLowSys2Pass ? null : 255);
                    SetExcelCellFontColor(cells, 47, 6, vm66.IsPointMidSys1Pass ? null : 255);
                    SetExcelCellFontColor(cells, 48, 6, vm66.IsPointMidSys2Pass ? null : 255);
                    SetExcelCellFontColor(cells, 49, 6, vm66.IsPointHighSys1Pass ? null : 255);
                    SetExcelCellFontColor(cells, 50, 6, vm66.IsPointHighSys2Pass ? null : 255);

                    range = sheet.GetType().InvokeMember("Range", BindingFlags.GetProperty, null, sheet, new object[] { "G41:G50" });
                    range.GetType().InvokeMember("Merge", BindingFlags.InvokeMethod, null, range, null);
                    range.GetType().InvokeMember("Value", BindingFlags.SetProperty, null, range, new object[] { vm66.CurrentTestResult });
                    SetRangeFontColor(range, string.Equals(vm66.CurrentTestResult, "不合格", StringComparison.OrdinalIgnoreCase) ? 255 : (int?)null);
                    ReleaseComObject(range);
                    range = null;
                }

                if (vm67 != null)
                {
                    var hc67Values = new[]
                    {
                        vm67.Pin49Text, vm67.Pin50Text, vm67.Pin51Text, vm67.Pin52Text, vm67.Pin53Text, vm67.Pin54Text, vm67.Pin55Text,
                        vm67.Pin56Text, vm67.Pin57Text, vm67.Pin58Text, vm67.Pin59Text, vm67.Pin60Text, vm67.Pin61Text, vm67.Pin62Text,
                        vm67.Pin63Text, vm67.Pin89Text, vm67.Pin90Text, vm67.Pin91Text, vm67.Pin92Text, vm67.Pin93Text, vm67.Pin94Text,
                        vm67.Pin95Text, vm67.Pin96Text, vm67.Pin97Text, vm67.Pin98Text, vm67.Pin99Text, vm67.Pin100Text
                    };

                    var hc67Passes = new[]
                    {
                        vm67.IsPin49Pass, vm67.IsPin50Pass, vm67.IsPin51Pass, vm67.IsPin52Pass, vm67.IsPin53Pass, vm67.IsPin54Pass, vm67.IsPin55Pass,
                        vm67.IsPin56Pass, vm67.IsPin57Pass, vm67.IsPin58Pass, vm67.IsPin59Pass, vm67.IsPin60Pass, vm67.IsPin61Pass, vm67.IsPin62Pass,
                        vm67.IsPin63Pass, vm67.IsPin89Pass, vm67.IsPin90Pass, vm67.IsPin91Pass, vm67.IsPin92Pass, vm67.IsPin93Pass, vm67.IsPin94Pass,
                        vm67.IsPin95Pass, vm67.IsPin96Pass, vm67.IsPin97Pass, vm67.IsPin98Pass, vm67.IsPin99Pass, vm67.IsPin100Pass
                    };

                    for (var i = 0; i < hc67Values.Length; i++)
                    {
                        var row = 51 + i;
                        SetExcelCellValue(cells, row, 5, hc67Values[i]);
                        SetExcelCellValue(cells, row, 6, hc67Passes[i] ? "合格" : "不合格");
                        SetExcelCellFontColor(cells, row, 6, hc67Passes[i] ? null : 255);
                    }

                    range = sheet.GetType().InvokeMember("Range", BindingFlags.GetProperty, null, sheet, new object[] { "G51:G77" });
                    range.GetType().InvokeMember("Merge", BindingFlags.InvokeMethod, null, range, null);
                    range.GetType().InvokeMember("Value", BindingFlags.SetProperty, null, range, new object[] { vm67.CurrentTestResult });
                    SetRangeFontColor(range, string.Equals(vm67.CurrentTestResult, "不合格", StringComparison.OrdinalIgnoreCase) ? 255 : (int?)null);
                    ReleaseComObject(range);
                    range = null;
                }

                if (vm68 != null)
                {
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
                        var row = 78 + i;
                        SetExcelCellValue(cells, row, 5, hc68OpenValues[i]);
                        SetExcelCellValue(cells, row, 6, hc68OpenPasses[i] ? "合格" : "不合格");
                        SetExcelCellFontColor(cells, row, 6, hc68OpenPasses[i] ? null : 255);
                    }

                    for (var i = 0; i < hc68CloseValues.Length; i++)
                    {
                        var row = 85 + i;
                        SetExcelCellValue(cells, row, 5, hc68CloseValues[i]);
                        SetExcelCellValue(cells, row, 6, hc68ClosePasses[i] ? "合格" : "不合格");
                        SetExcelCellFontColor(cells, row, 6, hc68ClosePasses[i] ? null : 255);
                    }

                    range = sheet.GetType().InvokeMember("Range", BindingFlags.GetProperty, null, sheet, new object[] { "G85:G91" });
                    range.GetType().InvokeMember("Merge", BindingFlags.InvokeMethod, null, range, null);
                    range.GetType().InvokeMember("Value", BindingFlags.SetProperty, null, range, new object[] { vm68.CurrentTestResult });
                    SetRangeFontColor(range, string.Equals(vm68.CurrentTestResult, "不合格", StringComparison.OrdinalIgnoreCase) ? 255 : (int?)null);
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
            }
        }

        private static string FormatNullableNumber(double? value)
        {
            return value.HasValue ? value.Value.ToString("0.###") : "--";
        }

        private static void SetExcelCellValue(object cells, int row, int column, string value)
        {
            var cell = cells.GetType().InvokeMember("Item", BindingFlags.GetProperty, null, cells, new object[] { row, column });
            try
            {
                cell.GetType().InvokeMember("Value", BindingFlags.SetProperty, null, cell, new object[] { value });
            }
            finally
            {
                ReleaseComObject(cell);
            }
        }

        private static void SetExcelCellFontColor(object cells, int row, int column, int? oleColor)
        {
            var cell = cells.GetType().InvokeMember("Item", BindingFlags.GetProperty, null, cells, new object[] { row, column });
            object font = null;
            try
            {
                font = cell.GetType().InvokeMember("Font", BindingFlags.GetProperty, null, cell, null);
                if (oleColor.HasValue)
                {
                    font.GetType().InvokeMember("Color", BindingFlags.SetProperty, null, font, new object[] { oleColor.Value });
                }
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
                font = range.GetType().InvokeMember("Font", BindingFlags.GetProperty, null, range, null);
                if (oleColor.HasValue)
                {
                    font.GetType().InvokeMember("Color", BindingFlags.SetProperty, null, font, new object[] { oleColor.Value });
                }
            }
            finally
            {
                ReleaseComObject(font);
            }
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
            }
            catch
            {
            }
        }

        private static void ReleaseComObject(object comObject)
        {
            if (comObject != null && Marshal.IsComObject(comObject))
            {
                try
                {
                    Marshal.ReleaseComObject(comObject);
                }
                catch
                {
                }
            }
        }

        private void FillFuelBoardExcelReport(string reportPath)
        {
            var vm1 = ContainerLocator.Container.Resolve<PowerImpedanceTestViewModel>();
            var vm2 = ContainerLocator.Container.Resolve<SecondaryPowerTestViewModel>();
            var vm3 = ContainerLocator.Container.Resolve<TemperatureAcquisitionTestViewModel>();
            var vm4 = ContainerLocator.Container.Resolve<LowVoltageAlarmTestViewModel>();
            var vm5 = ContainerLocator.Container.Resolve<DiscreteInputTestViewModel>();
            var vm6 = ContainerLocator.Container.Resolve<DiscreteOutputTestViewModel>();
            var vm7 = ContainerLocator.Container.Resolve<RS422CommunicationFunctionTestViewModel>();
            var vm8 = ContainerLocator.Container.Resolve<RS422SelfCheckTestViewModel>();
            if (vm1 == null && vm2 == null && vm3 == null && vm4 == null && vm5 == null && vm6 == null && vm7 == null && vm8 == null)
            {
                return;
            }

            Type excelType = null;
            object excelApp = null;
            object workbooks = null;
            object workbook = null;
            object sheet = null;
            object cells = null;
            object usedRange = null;
            object foundCell = null;

            try
            {
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
                usedRange = sheet.GetType().InvokeMember("UsedRange", BindingFlags.GetProperty, null, sheet, null);

                int? FindColumnByHeader(string header)
                {
                    try
                    {
                        foundCell = usedRange.GetType().InvokeMember(
                            "Find",
                            BindingFlags.InvokeMethod,
                            null,
                            usedRange,
                            new object[] { header, Type.Missing, Type.Missing, 2, Type.Missing, Type.Missing, false, Type.Missing, Type.Missing });

                        if (foundCell == null)
                            return null;

                        var colObj = foundCell.GetType().InvokeMember("Column", BindingFlags.GetProperty, null, foundCell, null);
                        if (colObj == null)
                            return null;

                        return Convert.ToInt32(colObj);
                    }
                    catch
                    {
                        return null;
                    }
                    finally
                    {
                        ReleaseComObject(foundCell);
                        foundCell = null;
                    }
                }

                int? FindRowByText(string text)
                {
                    try
                    {
                        foundCell = usedRange.GetType().InvokeMember(
                            "Find",
                            BindingFlags.InvokeMethod,
                            null,
                            usedRange,
                            new object[] { text, Type.Missing, Type.Missing, 2, Type.Missing, Type.Missing, false, Type.Missing, Type.Missing });

                        if (foundCell == null)
                            return null;

                        var rowObj = foundCell.GetType().InvokeMember("Row", BindingFlags.GetProperty, null, foundCell, null);
                        if (rowObj == null)
                            return null;

                        return Convert.ToInt32(rowObj);
                    }
                    catch
                    {
                        return null;
                    }
                    finally
                    {
                        ReleaseComObject(foundCell);
                        foundCell = null;
                    }
                }

                string NormalizeFuelOverall(string overall)
                {
                    var r = (overall ?? string.Empty).Trim();
                    if (string.Equals(r, "PASS", StringComparison.OrdinalIgnoreCase) || string.Equals(r, "合格", StringComparison.OrdinalIgnoreCase))
                        return "合格";
                    if (string.Equals(r, "FAIL", StringComparison.OrdinalIgnoreCase) || string.Equals(r, "不合格", StringComparison.OrdinalIgnoreCase))
                        return "不合格";
                    return string.IsNullOrWhiteSpace(r) || r == "--" ? "未知" : r;
                }

                string NormalizeFuelStepResult(string result)
                {
                    var r = (result ?? string.Empty).Trim();
                    if (string.Equals(r, "PASS", StringComparison.OrdinalIgnoreCase) || string.Equals(r, "合格", StringComparison.OrdinalIgnoreCase))
                        return "合格";
                    if (string.Equals(r, "FAIL", StringComparison.OrdinalIgnoreCase) || string.Equals(r, "不合格", StringComparison.OrdinalIgnoreCase))
                        return "不合格";
                    return string.IsNullOrWhiteSpace(r) || r == "--" ? "--" : r;
                }

                var valueCol = FindColumnByHeader("测试值") ?? 4;
                var resultCol = FindColumnByHeader("测试结果") ?? 6;
                var remarkCol = FindColumnByHeader("备注") ?? 7;

                void WriteRow(int row, string value, string result, string remark)
                {
                    if (!string.IsNullOrWhiteSpace(value))
                        SetExcelCellValue(cells, row, valueCol, value);
                    if (!string.IsNullOrWhiteSpace(result))
                    {
                        SetExcelCellValue(cells, row, resultCol, result);
                        SetExcelCellFontColor(cells, row, resultCol, string.Equals(result, "不合格", StringComparison.OrdinalIgnoreCase) ? 255 : (int?)null);
                    }
                    if (!string.IsNullOrWhiteSpace(remark))
                        SetExcelCellValue(cells, row, remarkCol, remark);
                }

                string MapFuelTemplateStepToUiStep(string templateStepName)
                {
                    var n = (templateStepName ?? string.Empty).Trim();
                    return n switch
                    {
                        "二次供电测试" => "二次电源测试",
                        "温度采集测试" => "温度采集功能",
                        "低供电告警功能测试" => "低电压告警功能测试",
                        "离散量采集测试" => "离散量采集功能测试",
                        "离散量输出测试" => "离散量输出功能测试",
                        "RS422通信自检功能测试" => "RS422通信自检测功能测试",
                        _ => n
                    };
                }

                bool IsStepExecuted(string stepName)
                {
                    if (_executedFuelTestStepNames == null)
                        return true;
                    var uiName = MapFuelTemplateStepToUiStep(stepName);
                    return _executedFuelTestStepNames.Contains(uiName);
                }

                void FillStep(string stepName, Action<int> fillRows)
                {
                    var row = FindRowByText(stepName);
                    if (row.HasValue)
                    {
                        if (IsStepExecuted(stepName))
                        {
                            fillRows(row.Value);
                        }
                    }
                    else
                    {
                        AppendSingleBoardReportLine($"REPORT | FUEL_EXCEL_STEP_NOT_FOUND | {stepName}");
                    }
                }

                FillStep("电源阻抗测试", row =>
                {
                    if (vm1 == null)
                        return;

                    var values = new[]
                    {
                        FormatNullableNumber(vm1.ImpedanceA),
                        FormatNullableNumber(vm1.ImpedanceB),
                        FormatNullableNumber(vm1.ImpedanceC),
                        FormatNullableNumber(vm1.ImpedanceD)
                    };

                    var results = new[]
                    {
                        NormalizeFuelStepResult(vm1.ResultA),
                        NormalizeFuelStepResult(vm1.ResultB),
                        NormalizeFuelStepResult(vm1.ResultC),
                        NormalizeFuelStepResult(vm1.ResultD)
                    };

                    for (var i = 0; i < values.Length; i++)
                    {
                        WriteRow(row + i, values[i], results[i], null);
                    }

                    WriteRow(row, null, NormalizeFuelOverall(vm1.OverallResult), null);
                });

                FillStep("二次供电测试", row =>
                {
                    if (vm2 == null)
                        return;

                    WriteRow(row, FormatNullableNumber(vm2.VoltageValue), NormalizeFuelOverall(vm2.OverallResult), null);
                });

                FillStep("温度采集测试", row =>
                {
                    if (vm3 == null)
                        return;

                    WriteRow(row, FormatNullableNumber(vm3.TemperatureValue), NormalizeFuelOverall(vm3.OverallResult), null);
                });

                FillStep("低供电告警功能测试", row =>
                {
                    if (vm4 == null)
                        return;

                    WriteRow(row, FormatNullableNumber(vm4.FlipVoltage), NormalizeFuelOverall(vm4.OverallResult), null);
                });

                FillStep("离散量采集测试", row =>
                {
                    if (vm5 == null)
                        return;

                    var remark = $"接地[{vm5.Bank0GroundedResults}] [{vm5.Bank1GroundedResults}] 开路[{vm5.Bank0OpenResults}] [{vm5.Bank1OpenResults}]";
                    WriteRow(row, null, NormalizeFuelOverall(vm5.OverallResult), remark);
                });

                FillStep("离散量输出测试", row =>
                {
                    if (vm6 == null)
                        return;

                    var remark = $"GND={FormatNullableNumber(vm6.ImpedanceGrounded)} OPEN={FormatNullableNumber(vm6.ImpedanceOpen)} J14={FormatNullableNumber(vm6.J14Voltage)}";
                    WriteRow(row, null, NormalizeFuelOverall(vm6.OverallResult), remark);
                });

                FillStep("RS422通信功能测试", row =>
                {
                    if (vm7 == null)
                        return;

                    WriteRow(row + 0, null, NormalizeFuelStepResult(vm7.StepAResult), vm7.StepARxData);
                    WriteRow(row + 1, null, NormalizeFuelStepResult(vm7.StepBResult), vm7.StepBRxData);
                    WriteRow(row + 2, null, NormalizeFuelStepResult(vm7.StepCResult), vm7.StepCRxData);
                    WriteRow(row + 3, null, NormalizeFuelStepResult(vm7.StepDResult), vm7.StepDRxData);
                    WriteRow(row, null, NormalizeFuelOverall(vm7.OverallResult), null);
                });

                FillStep("RS422通信自检功能测试", row =>
                {
                    if (vm8 == null)
                        return;

                    WriteRow(row + 0, null, NormalizeFuelStepResult(vm8.StepAResult), vm8.StepARxData);
                    WriteRow(row + 1, null, NormalizeFuelStepResult(vm8.StepBResult), vm8.StepBRxData);
                    WriteRow(row, null, NormalizeFuelOverall(vm8.OverallResult), null);
                });

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
                ReleaseComObject(foundCell);
                ReleaseComObject(usedRange);
                ReleaseComObject(cells);
                ReleaseComObject(sheet);
                ReleaseComObject(workbook);
                ReleaseComObject(workbooks);
                ReleaseComObject(excelApp);
            }
        }

        private void AppendSingleBoardReportLine(string message)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_singleBoardAutoTestReportPath))
                    return;

                var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}";
                File.AppendAllText(_singleBoardAutoTestReportPath, line);
            }
            catch
            {
            }
        }

        private static bool IsPass(string result)
        {
            return string.Equals(result?.Trim(), "合格", StringComparison.OrdinalIgnoreCase);
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
    }
}
