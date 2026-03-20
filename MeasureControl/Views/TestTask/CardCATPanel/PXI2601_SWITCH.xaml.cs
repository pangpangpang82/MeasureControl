using MeasureControl.ViewModels;
using MeasureControl.ViewModels.TestTask.CardCATPanel;
using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shapes;

namespace MeasureControl.Views.TestTask.CardCATPanel
{
    public partial class PXI2601_SWITCH : UserControl
    {
        public PXI2601_SWITCH()
        {
            InitializeComponent();
        }

        // 辅助方法：安全执行命令
        private void SafeExecuteCommand(ICommand command, object parameter)
        {
            try
            {
                if (command != null && command.CanExecute(parameter))
                {
                    command.Execute(parameter);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"命令执行失败: {ex.Message}");
            }
        }

        // 获取 ViewModel
        private PXI2601_SWITCHViewModel GetViewModel()
        {
            return DataContext as PXI2601_SWITCHViewModel;
        }

        #region 鼠标事件处理

        // 矩阵节点鼠标点击事件（专门处理 MatrixNodeViewModel）
        private void MatrixNode_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (sender is FrameworkElement element && element.DataContext is MatrixNodeViewModel matrixNode)
                {
                    var viewModel = GetViewModel();
                    if (viewModel != null)
                    {
                        Debug.WriteLine($"矩阵节点点击: {matrixNode.NodeId} ({matrixNode.NodeType})");

                        // 直接调用 MatrixNodeClickedCommand
                        SafeExecuteCommand(viewModel.MatrixNodeClickedCommand, matrixNode);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"矩阵节点点击错误: {ex.Message}");
            }
            finally
            {
                e.Handled = true;
            }
        }

        // 矩阵节点鼠标右键点击事件
        private void MatrixNode_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (sender is FrameworkElement element && element.DataContext is MatrixNodeViewModel matrixNode)
                {
                    var viewModel = GetViewModel();
                    if (viewModel != null)
                    {
                        Debug.WriteLine($"矩阵节点右键: {matrixNode.NodeId}");
                        SafeExecuteCommand(viewModel.MatrixNodeRightClickedCommand, matrixNode);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"矩阵节点右键错误: {ex.Message}");
            }
            finally
            {
                e.Handled = true;
            }
        }

        // 拓扑节点鼠标点击事件（专门处理 TopologyNodeInfo）
        private void TopologyNode_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (sender is FrameworkElement element && element.DataContext is TopologyNodeInfo topologyNode)
                {
                    var viewModel = GetViewModel();
                    if (viewModel != null)
                    {
                        Debug.WriteLine($"拓扑节点点击: {topologyNode.NodeId}");
                        SafeExecuteCommand(viewModel.NodeClickedCommand, topologyNode);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"拓扑节点点击错误: {ex.Message}");
            }
            finally
            {
                e.Handled = true;
            }
        }

        // 节点鼠标进入事件（通用方法）
        private void Node_MouseEnter(object sender, MouseEventArgs e)
        {
            try
            {
                if (sender is FrameworkElement element)
                {
                    var viewModel = GetViewModel();
                    if (viewModel != null)
                    {
                        // 根据类型调用不同的悬停命令
                        if (element.DataContext is MatrixNodeViewModel matrixNode)
                        {
                            Debug.WriteLine($"矩阵节点悬停: {matrixNode.NodeId}");
                            // 设置悬停状态
                            matrixNode.IsHovered = true;
                        }
                        else if (element.DataContext is TopologyNodeInfo topologyNode)
                        {
                            Debug.WriteLine($"拓扑节点悬停: {topologyNode.NodeId}");
                            SafeExecuteCommand(viewModel.NodeHoveredCommand, topologyNode);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"节点悬停错误: {ex.Message}");
            }
        }

        // 节点鼠标离开事件（通用方法）
        private void Node_MouseLeave(object sender, MouseEventArgs e)
        {
            try
            {
                if (sender is FrameworkElement element)
                {
                    if (element.DataContext is MatrixNodeViewModel matrixNode)
                    {
                        matrixNode.IsHovered = false;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"节点离开错误: {ex.Message}");
            }
        }

        // 矩阵容器大小变化事件
        private void MatrixContainer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            try
            {
                var viewModel = GetViewModel();
                if (viewModel != null && sender is FrameworkElement element)
                {
                    // 计算可用空间，减去边距和内边距
                    double availableWidth = element.ActualWidth - 20;
                    double availableHeight = element.ActualHeight - 20;

                    // 确保可用空间为正数
                    availableWidth = Math.Max(availableWidth, 100);
                    availableHeight = Math.Max(availableHeight, 100);

                    Debug.WriteLine($"容器大小变化: {availableWidth}x{availableHeight}");

                    // 通知ViewModel可用空间大小
                    viewModel.UpdateAvailableSpace(availableWidth, availableHeight);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"容器大小变化错误: {ex.Message}");
            }
        }

        // 交叉点鼠标点击事件
        // 交叉点鼠标点击事件
        private void CrossPoint_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (sender is FrameworkElement element && element.DataContext is CrossPointViewModel crossPoint)
                {
                    var viewModel = GetViewModel();
                    if (viewModel != null)
                    {
                        Debug.WriteLine($"交叉点点击: {crossPoint.DisplayName} - 连接状态: {crossPoint.IsConnected}");

                        // 如果是已连接的交叉点，调用断开连接命令（与右键菜单相同）
                        if (crossPoint.IsConnected)
                        {
                            Debug.WriteLine($"执行断开连接命令: {crossPoint.DisplayName}");
                            SafeExecuteCommand(viewModel.DisconnectCrossPointCommand, crossPoint);
                        }
                        else
                        {
                            // 未连接的交叉点，直接执行点击命令（可能会触发连接流程）
                            Debug.WriteLine($"交叉点未连接，执行连接流程");
                            SafeExecuteCommand(viewModel.CrossPointClickedCommand, crossPoint);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"交叉点点击错误: {ex.Message}");
            }
            finally
            {
                e.Handled = true;
            }
        }

        // 交叉点鼠标进入事件
        private void CrossPoint_MouseEnter(object sender, MouseEventArgs e)
        {
            try
            {
                if (sender is FrameworkElement element && element.DataContext is CrossPointViewModel crossPoint)
                {
                    var viewModel = GetViewModel();
                    if (viewModel != null)
                    {
                        Debug.WriteLine($"交叉点悬停: {crossPoint.DisplayName}");
                        SafeExecuteCommand(viewModel.CrossPointHoveredCommand, crossPoint);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"交叉点悬停错误: {ex.Message}");
            }
        }

        // 交叉点鼠标离开事件
        private void CrossPoint_MouseLeave(object sender, MouseEventArgs e)
        {
            try
            {
                if (sender is FrameworkElement element && element.DataContext is CrossPointViewModel crossPoint)
                {
                    var viewModel = GetViewModel();
                    if (viewModel != null)
                    {
                        Debug.WriteLine($"交叉点离开: {crossPoint.DisplayName}");
                        SafeExecuteCommand(viewModel.CrossPointMouseLeaveCommand, crossPoint);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"交叉点离开错误: {ex.Message}");
            }
        }

        // 连接线鼠标右键点击事件
        private void Connection_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (sender is FrameworkElement element)
                {
                    var viewModel = GetViewModel();
                    if (viewModel != null)
                    {
                        // 根据 DataContext 类型执行相应的命令
                        if (element.DataContext is MatrixConnectionViewModel matrixConn)
                        {
                            Debug.WriteLine($"矩阵连接右键: {matrixConn.InputNodeId} -> {matrixConn.OutputNodeId}");
                            SafeExecuteCommand(viewModel.MatrixConnectionRightClickedCommand, matrixConn);
                        }
                        else if (element.DataContext is TopologyConnectionInfo topologyConn)
                        {
                            Debug.WriteLine($"拓扑连接右键: {topologyConn.InputNodeId} -> {topologyConn.OutputNodeId}");
                            SafeExecuteCommand(viewModel.ConnectionRightClickedCommand, topologyConn);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"连接右键错误: {ex.Message}");
            }
            finally
            {
                e.Handled = true;
            }
        }

        #endregion

        #region 右键菜单事件处理

        // 右键菜单点击事件（用于调试）
        private void MenuItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is MenuItem menuItem && menuItem.DataContext is CrossPointViewModel crossPoint)
                {
                    var viewModel = GetViewModel();
                    if (viewModel != null)
                    {
                        Debug.WriteLine($"右键菜单点击: {crossPoint.DisplayName}, 连接状态: {crossPoint.IsConnected}");

                        // 这里可以根据菜单项的标题执行不同的命令
                        if (menuItem.Header.ToString().Contains("断开"))
                        {
                            SafeExecuteCommand(viewModel.DisconnectCrossPointCommand, crossPoint);
                        }
                        else if (menuItem.Header.ToString().Contains("连接"))
                        {
                            SafeExecuteCommand(viewModel.ConnectCrossPointCommand, crossPoint);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"右键菜单点击错误: {ex.Message}");
            }
        }

        #endregion

        // 控件加载时初始化
        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                var viewModel = GetViewModel();
                if (viewModel != null)
                {
                    Debug.WriteLine($"开关控制面板已加载: {viewModel.CardName}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"控件加载错误: {ex.Message}");
            }
        }

        // 添加缺少的 MatrixNode 悬停事件处理函数
        private void MatrixNode_MouseEnter(object sender, MouseEventArgs e)
        {
            try
            {
                if (sender is FrameworkElement element && element.DataContext is MatrixNodeViewModel matrixNode)
                {
                    var viewModel = GetViewModel();
                    if (viewModel != null)
                    {
                        Debug.WriteLine($"矩阵节点悬停进入: {matrixNode.NodeId}");
                        matrixNode.IsHovered = true;

                        // 如果需要，可以调用专门的悬停命令
                        // SafeExecuteCommand(viewModel.MatrixNodeHoveredCommand, matrixNode);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"矩阵节点悬停进入错误: {ex.Message}");
            }
        }

        private void MatrixNode_MouseLeave(object sender, MouseEventArgs e)
        {
            try
            {
                if (sender is FrameworkElement element && element.DataContext is MatrixNodeViewModel matrixNode)
                {
                    var viewModel = GetViewModel();
                    if (viewModel != null)
                    {
                        Debug.WriteLine($"矩阵节点悬停离开: {matrixNode.NodeId}");
                        matrixNode.IsHovered = false;

                        // 如果需要，可以调用专门的离开命令
                        // SafeExecuteCommand(viewModel.MatrixNodeMouseLeaveCommand, matrixNode);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"矩阵节点悬停离开错误: {ex.Message}");
            }
        }
        #region 板卡名称输入事件处理

        // 板卡名称文本框失去焦点事件
        private void CardNameTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            try
            {
                var viewModel = GetViewModel();
                if (viewModel != null && !string.IsNullOrEmpty(viewModel.CardName))
                {
                    Debug.WriteLine($"板卡名称已更新: {viewModel.CardName}");

                    // 可以在这里添加额外的处理逻辑
                    // 例如：验证名称、保存到数据库等
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"板卡名称更新错误: {ex.Message}");
            }
        }
        #endregion
    }
}