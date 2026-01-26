using System;
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

        #endregion

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
                    treeViewItem.ContextMenu = null;
                    e.Handled = true;
                    return;
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
                else if (projectItem.Type == "test_task")
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
