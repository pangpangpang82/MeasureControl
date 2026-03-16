using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MeasureControl.Models;
using MeasureControl.Views;
using MeasureControl.Views.Dialogs;

namespace MeasureControl.Services
{
    public class DragDropService : IDragDropService
    {
        #region Events

        public event EventHandler<DropPxiChassisArgs> PxiChassisDropped;
        public event EventHandler RefreshRequested;

        #endregion

        #region Private Fields

        private FrameworkElement _mainContainer; // 改为支持Canvas和Grid
        private Border _pxiSourceBorder2722; // PXI-2722G2的Border
        private Border _pxiSourceBorder2519; // PXI-2519G2的Border
        private Border _currentHighlightedBorder; // 当前高亮的Border
        private readonly List<ChassisModel> _chassisList = new List<ChassisModel>();

        #endregion

        #region Constructor

        public DragDropService()
        {
        }

        #endregion

        #region Public Methods

        public void Initialize(FrameworkElement mainContainer, Border pxiSourceBorder2722, Border pxiSourceBorder2519)
        {
            _mainContainer = mainContainer ?? throw new ArgumentException("mainContainer cannot be null", nameof(mainContainer));
            _pxiSourceBorder2722 = pxiSourceBorder2722 ?? throw new ArgumentException("pxiSourceBorder2722 cannot be null", nameof(pxiSourceBorder2722));
            _pxiSourceBorder2519 = pxiSourceBorder2519 ?? throw new ArgumentException("pxiSourceBorder2519 cannot be null", nameof(pxiSourceBorder2519));
        }

        public void UpdateChassisList(System.Collections.ObjectModel.ObservableCollection<Models.ChassisModel> chassisList)
        {
            // 这个方法用于更新机箱列表，具体实现可以根据需要调整
            RefreshRequested?.Invoke(this, EventArgs.Empty);
        }

        public void StartPxiChassisDrag(FrameworkElement source)
        {
            try
            {
                // 根据source的Name确定机箱型号和对应的Border
                string chassisModel = "PXIe-2722G2"; // 默认18槽机箱型号
                Border targetBorder = _pxiSourceBorder2722; // 默认高亮2722

                // 现在 source 可能是 Border 或 StackPanel
                if (source is Border border)
                {
                    // 如果是 Border，根据 Name 判断
                    if (border.Name == "PxiSourceBorder2519")
                    {
                        chassisModel = "PXIe-2519G2"; // 8槽机箱型号
                        targetBorder = _pxiSourceBorder2519;
                    }
                    else if (border.Name == "PxiSourceBorder2722")
                    {
                        chassisModel = "PXIe-2722G2"; // 18槽机箱型号
                        targetBorder = _pxiSourceBorder2722;
                    }
                }
                else if (source is StackPanel stackPanel)
                {
                    // 兼容旧的 StackPanel 方式
                    if (stackPanel.Name == "Pxi_2519")
                    {
                        chassisModel = "PXIe-2519G2"; // 8槽机箱型号
                        targetBorder = _pxiSourceBorder2519;
                    }
                    else if (stackPanel.Name == "PxiSource" || stackPanel.Name == "Pxi_2722")
                    {
                        chassisModel = "PXIe-2722G2"; // 18槽机箱型号
                        targetBorder = _pxiSourceBorder2722;
                    }
                }
                
                // 高亮对应的Border（使用浅蓝色，30%不透明度）
                HighlightPxiSource(targetBorder, true, "#4D9ED9F2"); // 浅蓝色高亮
                
                var dragData = new DataObject();
                dragData.SetData("PxiChassis", "PXI机箱");
                dragData.SetData("ChassisModel", chassisModel);
                DragDrop.DoDragDrop(source, dragData, DragDropEffects.Copy);
                
                // 拖拽结束后取消高亮
                HighlightPxiSource(targetBorder, false);
            }
            catch (Exception)
            {
                // 拖拽开始失败，忽略错误
                if (_currentHighlightedBorder != null)
                {
                    HighlightPxiSource(_currentHighlightedBorder, false);
                }
            }
        }

        public void HandlePxiChassisDragEnter(DragEventArgs e)
        {
            if (e.Data.GetDataPresent("PxiChassis"))
            {
                e.Effects = DragDropEffects.Copy;
                // 高亮整个机箱区域而不是单个格子
                HighlightChassisArea(true);
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
        }

        public void HandlePxiChassisDragLeave(DragEventArgs e)
        {
            if (e.Data.GetDataPresent("PxiChassis"))
            {
                // 检查鼠标是否真的离开了整个机箱区域
                var position = e.GetPosition(_mainContainer);
                if (position.X < 0 || position.Y < 0 || 
                    position.X > _mainContainer.ActualWidth || position.Y > _mainContainer.ActualHeight)
                {
                    HighlightChassisArea(false);
                }
            }
        }

        public void HandlePxiChassisDrop(DragEventArgs e)
        {
            if (e.Data.GetDataPresent("PxiChassis"))
            {
                try
                {
                    // 取消区域高亮
                    HighlightChassisArea(false);

                    // 找到下一个可用位置
                    var nextPosition = FindNextAvailablePosition();
                    if (nextPosition == null)
                    {
                        ReMessageBox.Show("机箱区域已满，无法添加更多机箱！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    // 获取机箱型号
                    string chassisModel = "PXIe-2722G2"; // 默认9槽机箱型号
                    if (e.Data.GetDataPresent("ChassisModel"))
                    {
                        chassisModel = e.Data.GetData("ChassisModel") as string ?? "PXIe-2722G2";
                    }

                    // 创建机箱对象并添加到服务
                    var dropArgs = new DropPxiChassisArgs(nextPosition.Value.Row, nextPosition.Value.Column, chassisModel);
                    PxiChassisDropped?.Invoke(this, dropArgs);
                }
                catch (Exception)
                {
                    // 拖拽放置失败，忽略错误
                    HighlightChassisArea(false);
                }
            }
        }

        public (int Row, int Column)? FindNextAvailablePosition()
        {
            // 对于Canvas布局，固定为2行5列
            for (int row = 0; row < 2; row++)
            {
                for (int column = 0; column < 5; column++)
                {
                    if (!HasChassisAtPosition(row, column))
                    {
                        return (row, column);
                    }
                }
            }
            return null; // 没有可用位置
        }

        public void HighlightChassisArea(bool highlight)
        {
            if (_mainContainer == null) return;

            // 使用浅蓝色 #9ED9F2 的半透明版本（4D为30%不透明度）
            var backgroundColor = highlight ? 
                new SolidColorBrush(Color.FromArgb(0x4D, 0x9E, 0xD9, 0xF2)) : 
                Brushes.Transparent;

            // 支持Canvas和Grid两种布局
            if (_mainContainer is Canvas canvas)
            {
                // Canvas布局：通过名称查找单元格，高亮Cell本身（Border）
                for (int row = 0; row < 2; row++)
                {
                    for (int col = 0; col < 5; col++)
                    {
                        var cellName = $"Cell_{row}_{col}";
                        var cell = canvas.FindName(cellName) as Border;
                        if (cell != null)
                        {
                            // 高亮Cell Border本身，而不是它的Child
                            cell.Background = backgroundColor;
                        }
                    }
                }
            }
            else if (_mainContainer is Grid grid)
            {
                // Grid布局：遍历子元素
                foreach (UIElement child in grid.Children)
                {
                    if (child is Border border && 
                        border.Tag is ChassisModel && 
                        border.Child is StackPanel stackPanel)
                    {
                        stackPanel.Background = backgroundColor;
                    }
                }
            }
        }

        public void HighlightSingleChassis(int row, int column, bool highlight)
        {
            if (_mainContainer == null) 
            {
                return;
            }


            // 支持Canvas和Grid两种布局
            if (_mainContainer is Canvas canvas)
            {
                // Canvas布局：通过名称查找单元格
                var cellName = $"Cell_{row}_{column}";
                var cell = canvas.FindName(cellName) as Border;
                if (cell?.Child is StackPanel stackPanel)
                {
                    stackPanel.Background = highlight ? 
                        new SolidColorBrush(Color.FromArgb(40, 80, 130, 180)) : 
                        Brushes.Transparent;
                }
            }
            else if (_mainContainer is Grid grid)
            {
                // Grid布局：遍历子元素
                foreach (UIElement child in grid.Children)
                {
                    if (child is Border border && 
                        Grid.GetRow(border) == row && 
                        Grid.GetColumn(border) == column &&
                        border.Tag is ChassisModel && 
                        border.Child is StackPanel stackPanel)
                    {
                        stackPanel.Background = highlight ? 
                            new SolidColorBrush(Color.FromArgb(40, 80, 130, 180)) : 
                            Brushes.Transparent;
                        break;
                    }
                }
            }
        }

        public void HighlightPxiSource(Border targetBorder, bool highlight, string color = "")
        {
            if (targetBorder == null) return;

            if (highlight && !string.IsNullOrEmpty(color))
            {
                targetBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
                _currentHighlightedBorder = targetBorder;
            }
            else
            {
                targetBorder.Background = new SolidColorBrush(Colors.Transparent);
                if (_currentHighlightedBorder == targetBorder)
                {
                    _currentHighlightedBorder = null;
                }
            }
        }

        #endregion

        #region Private Methods

        private bool HasChassisAtPosition(int row, int column)
        {
            if (_mainContainer == null) return false;

            // 支持Canvas和Grid两种布局
            if (_mainContainer is Canvas canvas)
            {
                // Canvas布局：通过名称查找单元格
                var cellName = $"Cell_{row}_{column}";
                var cell = canvas.FindName(cellName) as Border;
                return cell?.Child != null;
            }
            else if (_mainContainer is Grid grid)
            {
                // Grid布局：遍历子元素
                foreach (UIElement child in grid.Children)
                {
                    if (child is Border border && 
                        Grid.GetRow(border) == row && 
                        Grid.GetColumn(border) == column &&
                        border.Child != null)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        #endregion
    }
}
